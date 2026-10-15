using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Housing
{
    // 配置モード（家具仕様 §5.1：置く・動かす・回転・片付ける・壁紙と床の変更。格子に吸着）。
    // 入力を止めて、カメラの注視点を WASD で動かし、マウスで格子を選ぶ。
    public sealed partial class HousingSystem
    {
        private bool placementActive;
        private bool placementVisualsDirty;
        private bool exitingPlacement;
        private PlacementPanel placementPanel;
        private Transform placeFocus;
        private Transform prevCamTarget;
        private float placementStartTime;
        private string placeLocation;

        private string heldItemId;
        private int heldQuality;
        private string heldData;
        private string movingInstanceId;
        private GameObject movingHidden;
        private int placeRot = 2;

        private GameObject ghost;
        private GameObject footprintPlate;
        private string ghostKey;

        public bool CursorValid { get; private set; }
        public string CursorReason { get; private set; }
        public string CursorRoomLabel { get; private set; }
        public int CursorRoom { get; private set; } = -1;
        public string HoverInstanceId { get; private set; }
        public bool PlacementActive => placementActive;
        public string HeldLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(movingInstanceId)) return Loc.Format("動かし中：{0}", "Moving: {0}", ItemRegistry.NameOf(FindFurniture(movingInstanceId)?.itemId));
                if (!string.IsNullOrEmpty(heldItemId)) return Loc.Format("置く物：{0}", "Placing: {0}", ItemRegistry.NameOf(heldItemId));
                return Loc.T("家具をクリックすると動かせます", "Click furniture to move it");
            }
        }

        private int cursorGx, cursorGz;
        private bool cursorHasCell;
        private string cursorLocation;

        public void StartPlacementMode(int inventorySlot)
        {
            var player = LocalPlayer;
            if (player == null || Game == null) return;
            if (!CanEdit(player))
            {
                UIManager.Toast(Loc.T("模様替えには家主の許可（共同生活）が必要です", "You need co-living permission to redecorate"));
                return;
            }
            string loc = player.CurrentAreaId == HousingConst.LocHouse ? HousingConst.LocHouse : IsInGarden(player.Position, player.CurrentAreaId) ? HousingConst.LocGarden : null;
            if (loc == null)
            {
                UIManager.Toast(Loc.T("家の中か庭で模様替えできます", "You can redecorate inside your home or in the garden"));
                return;
            }
            if (!placementActive)
            {
                StandUp();
                placementActive = true;
                placeLocation = loc;
                player.LockInput("housing.place");
                placeFocus = PrimitiveBuilder.Empty("PlacementFocus", housingRoot, player.Position).transform;
                var rig = CameraRig.Instance;
                if (rig != null)
                {
                    prevCamTarget = rig.target;
                    rig.target = placeFocus;
                }
                placementPanel = new PlacementPanel(this);
                UIManager.Open(placementPanel);
                placementStartTime = UnityEngine.Time.unscaledTime;
            }
            if (inventorySlot >= 0) HoldFromSlot(inventorySlot);
        }

        public void ExitPlacementMode(bool fromPanel)
        {
            if (!placementActive || exitingPlacement) return;
            exitingPlacement = true;
            try
            {
                CancelHold();
                if (ghost != null) Object.Destroy(ghost);
                if (footprintPlate != null) Object.Destroy(footprintPlate);
                ghost = footprintPlate = null;
                ghostKey = null;
                var player = LocalPlayer;
                var rig = CameraRig.Instance;
                if (rig != null) rig.target = player?.Avatar != null ? player.Avatar : prevCamTarget;
                if (placeFocus != null) Object.Destroy(placeFocus.gameObject);
                placeFocus = null;
                player?.UnlockInput("housing.place");
                if (!fromPanel && placementPanel != null && placementPanel.IsOpen) UIManager.Close(placementPanel);
                placementPanel = null;
                placementActive = false;
                HoverInstanceId = null;
            }
            finally
            {
                exitingPlacement = false;
            }
        }

        public void HoldFromSlot(int slot)
        {
            var player = LocalPlayer;
            var s = player?.Inventory.GetSlot(slot);
            if (s == null || s.IsEmpty) return;
            var def = s.Definition;
            if (FurnitureCatalog.IsSurfaceDecor(def, true) || FurnitureCatalog.IsSurfaceDecor(def, false))
            {
                ApplySurface(s);
                return;
            }
            if (!FurnitureCatalog.IsPlaceable(def)) return;
            CancelHold();
            heldItemId = s.itemId;
            heldQuality = s.quality;
            heldData = s.data;
            var spec = FurnitureCatalog.Resolve(def);
            if (placeLocation == HousingConst.LocGarden && !spec.allowOutdoor && !spec.outdoorOnly)
                UIManager.Toast(Loc.T("これは家の中で使う物のようだ", "This seems to be for indoors"));
        }

        public void ApplySurface(ItemStack s)
        {
            if (s == null) return;
            bool wall = FurnitureCatalog.IsSurfaceDecor(s.Definition, true);
            int room = CursorRoom >= 0 ? CursorRoom : RoomOfFocus();
            if (room < 0)
            {
                UIManager.Toast(Loc.T("部屋の中を指してください", "Point at a room"));
                return;
            }
            WorldAuthority.Submit(new SetRoomSurfaceCommand { room = room, wallpaper = wall, itemId = s.itemId, quality = s.quality }, r =>
            {
                if (r != null && r.Success) UIManager.Toast(wall ? Loc.T("壁紙を貼り替えた", "Wallpaper changed") : Loc.T("床を張り替えた", "Floor changed"));
            });
        }

        private int RoomOfFocus()
        {
            if (placeFocus == null) return -1;
            return HouseGeometry.WorldToRoomCell(state, placeFocus.position, out var r, out _, out _) ? r.index : -1;
        }

        public void CancelHold()
        {
            heldItemId = null;
            heldData = null;
            if (movingHidden != null) movingHidden.SetActive(true);
            movingHidden = null;
            movingInstanceId = null;
        }

        public void RotateHeld() => placeRot = (placeRot + 1) & 3;

        public void StoreHoveredOrMoving()
        {
            string id = !string.IsNullOrEmpty(movingInstanceId) ? movingInstanceId : HoverInstanceId;
            if (string.IsNullOrEmpty(id)) return;
            WorldAuthority.Submit(new StoreFurnitureCommand { instanceId = id }, r =>
            {
                if (r != null && r.Success)
                {
                    if (movingInstanceId == id) { movingHidden = null; movingInstanceId = null; }
                    UIManager.Toast(Loc.T("持ち物にしまった（思い出はそのまま）", "Put away (its memories are kept)"));
                }
            });
        }

        private bool MouseOverPanel()
        {
            if (placementPanel == null || !placementPanel.IsOpen) return false;
            var mp = GameInput.MousePosition;
            var v = new Vector2(mp.x / UIStyles.Scale, (Screen.height - mp.y) / UIStyles.Scale);
            return placementPanel.GetRect().Contains(v);
        }

        private void TickPlacement(float dt)
        {
            if (!placementActive) return;
            var player = LocalPlayer;
            if (player == null || placeFocus == null) { ExitPlacementMode(false); return; }
            bool blocked = false;
            foreach (var w in UIManager.OpenWindows) if (w.IsModal) blocked = true;

            // 注視点の移動（WASD）
            var cam = Camera.main;
            if (!blocked && cam != null)
            {
                var move = GameInput.Move;
                if (move.sqrMagnitude > 0.001f)
                {
                    var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    var right = Vector3.Cross(Vector3.up, fwd);
                    var np = placeFocus.position + (fwd * move.y + right * move.x) * 6f * dt;
                    var d = np - player.Position;
                    d.y = 0;
                    if (d.magnitude > 30f) np = player.Position + d.normalized * 30f;
                    if (placeLocation == HousingConst.LocGarden && Layout != null) np = Layout.OnGround(np);
                    placeFocus.position = np;
                }
            }

            // カーソルの格子
            cursorHasCell = false;
            HoverInstanceId = null;
            CursorRoom = -1;
            CursorRoomLabel = placeLocation == HousingConst.LocGarden ? Loc.T("庭", "Garden") : "";
            if (cam != null && !MouseOverPanel())
            {
                var ray = cam.ScreenPointToRay(GameInput.MousePosition);
                float planeY = placeLocation == HousingConst.LocGarden ? placeFocus.position.y : HousingConst.HouseOrigin.y;
                var plane = new Plane(Vector3.up, new Vector3(0, planeY, 0));
                if (plane.Raycast(ray, out float enter))
                {
                    var hit = ray.GetPoint(enter);
                    int cx, cz;
                    if (placeLocation == HousingConst.LocHouse)
                    {
                        if (HouseGeometry.WorldToRoomCell(state, hit, out var room, out cx, out cz))
                        {
                            cursorHasCell = true;
                            CursorRoom = room.index;
                            CursorRoomLabel = RoomName(room);
                            cursorLocation = HousingConst.LocHouse;
                            cursorGx = cx;
                            cursorGz = cz;
                        }
                    }
                    else if (HouseGeometry.WorldToGardenCell(Layout, state, hit, out cx, out cz))
                    {
                        cursorHasCell = true;
                        cursorLocation = HousingConst.LocGarden;
                        cursorGx = cx;
                        cursorGz = cz;
                    }
                }
            }

            bool holding = !string.IsNullOrEmpty(heldItemId) || !string.IsNullOrEmpty(movingInstanceId);
            int gx = 0, gz = 0, rot = placeRot;
            FurnitureSpec spec = null;
            ItemDefinition def = null;
            if (holding)
            {
                string itemId = heldItemId ?? FindFurniture(movingInstanceId)?.itemId;
                def = ItemRegistry.GetOrPlaceholder(itemId);
                spec = FurnitureCatalog.Resolve(def);
                if (cursorHasCell)
                {
                    var room = HouseGeometry.GetRoom(state, CursorRoom);
                    if (spec.surface == FurnitureSurface.Wall && room != null) rot = HouseGeometry.NearestWallRot(room, cursorGx, cursorGz);
                    HouseGeometry.Footprint(spec, rot, out int fw, out int fd);
                    gx = cursorGx - fw / 2;
                    gz = cursorGz - fd / 2;
                    if (spec.surface == FurnitureSurface.Wall && room != null) HouseGeometry.SnapToWall(room, spec, rot, ref gx, ref gz);
                    CursorValid = HouseGeometry.CanPlace(state, Layout, cursorLocation, CursorRoom, spec, gx, gz, rot, movingInstanceId, out var ja, out var en);
                    CursorReason = CursorValid ? Loc.T("クリックで置く", "Click to place") : Loc.T(ja, en);
                    UpdateGhost(def, spec, cursorLocation, CursorRoom, gx, gz, rot, CursorValid);
                }
                else
                {
                    CursorValid = false;
                    CursorReason = Loc.T("置ける場所を指してください", "Point at a place to put it");
                    if (ghost != null) ghost.SetActive(false);
                    if (footprintPlate != null) footprintPlate.SetActive(false);
                }
            }
            else
            {
                if (ghost != null) { Object.Destroy(ghost); ghost = null; ghostKey = null; }
                if (footprintPlate != null) footprintPlate.SetActive(false);
                if (cursorHasCell) HoverInstanceId = FurnitureAtCell(cursorLocation, CursorRoom, cursorGx, cursorGz)?.instanceId;
                CursorReason = HoverInstanceId != null ? Loc.Format("{0}：クリックで動かす／Xでしまう", "{0}: click to move / X to put away", ItemRegistry.NameOf(FindFurniture(HoverInstanceId)?.itemId)) : "";
                if (HoverInstanceId != null) ShowHoverPlate(FindFurniture(HoverInstanceId));
            }

            if (blocked || UnityEngine.Time.unscaledTime - placementStartTime < 0.25f) return;
            if (GameInput.KeyDown(KeyCode.R)) RotateHeld();
            if (GameInput.KeyDown(KeyCode.Q)) CancelHold();
            if (GameInput.KeyDown(KeyCode.X) || GameInput.KeyDown(KeyCode.Delete)) StoreHoveredOrMoving();
            if (GameInput.MouseButtonDown(0) && !MouseOverPanel())
            {
                if (holding && cursorHasCell && CursorValid) SubmitPlacement(def, gx, gz, rot);
                else if (!holding && HoverInstanceId != null) BeginMove(HoverInstanceId);
                else if (holding && cursorHasCell && !CursorValid) UIManager.Toast(CursorReason, 1.5f);
            }
        }

        private void BeginMove(string instanceId)
        {
            var f = FindFurniture(instanceId);
            if (f == null) return;
            CancelHold();
            movingInstanceId = instanceId;
            placeRot = f.rot;
            var fo = GetFurnitureObject(instanceId);
            if (fo != null)
            {
                movingHidden = fo.gameObject;
                movingHidden.SetActive(false);
            }
        }

        private void SubmitPlacement(ItemDefinition def, int gx, int gz, int rot)
        {
            if (!string.IsNullOrEmpty(movingInstanceId))
            {
                string id = movingInstanceId;
                WorldAuthority.Submit(new MoveFurnitureCommand { instanceId = id, location = cursorLocation, room = CursorRoom, gx = gx, gz = gz, rot = rot }, r =>
                {
                    if (r != null && r.Success && movingInstanceId == id)
                    {
                        // 見た目は状態の差分から作り直される（隠していた古い姿は破棄される）
                        movingHidden = null;
                        movingInstanceId = null;
                    }
                });
                return;
            }
            string itemId = heldItemId;
            int quality = heldQuality;
            string data = heldData;
            ProvenanceEntry origin = string.IsNullOrEmpty(data) ? TakeProvenance(itemId) : null;
            var cmd = new PlaceFurnitureCommand
            {
                itemId = itemId,
                quality = quality,
                data = data,
                location = cursorLocation,
                room = CursorRoom,
                gx = gx,
                gz = gz,
                rot = rot,
                originKind = origin?.kind,
                originResidentId = origin?.residentId,
                originDay = origin != null ? origin.day : 0
            };
            WorldAuthority.Submit(cmd, r =>
            {
                if (r == null || !r.Success)
                {
                    if (origin != null) profile.pending.Add(origin);
                    return;
                }
                // 同じ物をまだ持っていれば続けて置ける
                var player = LocalPlayer;
                bool more = false;
                if (player != null)
                    foreach (var s in player.Inventory.slots)
                        if (s != null && !s.IsEmpty && s.itemId == itemId && s.quality == quality && (s.data ?? "") == (data ?? "")) more = true;
                if (!more || !string.IsNullOrEmpty(data)) { heldItemId = null; heldData = null; }
            });
        }

        private void UpdateGhost(ItemDefinition def, FurnitureSpec spec, string location, int room, int gx, int gz, int rot, bool valid)
        {
            string key = def.Id + "|" + rot;
            var temp = new PlacedFurniture { itemId = def.Id, location = location, room = room, gx = gx, gz = gz, rot = rot };
            var pos = HouseGeometry.FurniturePosition(state, Layout, temp, spec, out float yaw);
            if (ghost == null || ghostKey != key)
            {
                if (ghost != null) Object.Destroy(ghost);
                var res = FurnitureVisuals.Build(def, spec, housingRoot, pos, yaw, "PlacementGhost", false);
                ghost = res.Root;
                FurnitureVisuals.MakeGhost(ghost, Color.white);
                ghostKey = key;
            }
            ghost.SetActive(true);
            ghost.transform.position = pos + Vector3.up * 0.02f;
            ghost.transform.rotation = Quaternion.Euler(0, yaw, 0);
            HouseGeometry.Footprint(spec, rot, out int fw, out int fd);
            PlaceFootprintPlate(location, room, gx, gz, fw, fd, valid ? new Color(0.4f, 1f, 0.5f, 0.45f) : new Color(1f, 0.4f, 0.4f, 0.45f), spec.surface == FurnitureSurface.Wall ? pos.y - 0.6f : (float?)null);
        }

        private void ShowHoverPlate(PlacedFurniture f)
        {
            if (f == null) return;
            var spec = HouseGeometry.Resolve(f);
            HouseGeometry.Footprint(spec, f.rot, out int fw, out int fd);
            PlaceFootprintPlate(f.location, f.room, f.gx, f.gz, fw, fd, new Color(1f, 0.9f, 0.4f, 0.4f), null);
        }

        private void PlaceFootprintPlate(string location, int room, int gx, int gz, int fw, int fd, Color color, float? y)
        {
            if (footprintPlate == null) footprintPlate = PrimitiveBuilder.Create(PrimitiveType.Cube, housingRoot, Vector3.zero, Vector3.one, color, "FootprintPlate", false);
            footprintPlate.SetActive(true);
            PrimitiveBuilder.SetColor(footprintPlate, color);
            Vector3 center;
            float yaw = 0f;
            if (location == HousingConst.LocGarden)
            {
                center = HouseGeometry.GardenCellToWorld(Layout, gx + fw * 0.5f, gz + fd * 0.5f);
                yaw = HouseGeometry.GardenYaw(Layout);
            }
            else
            {
                var r = HouseGeometry.GetRoom(state, room);
                if (r == null) { footprintPlate.SetActive(false); return; }
                center = HouseGeometry.CellToWorldInRoom(state, r, gx + fw * 0.5f, gz + fd * 0.5f);
            }
            if (y.HasValue) center.y = y.Value;
            footprintPlate.transform.position = center + Vector3.up * 0.015f;
            footprintPlate.transform.rotation = Quaternion.Euler(0, yaw, 0);
            footprintPlate.transform.localScale = new Vector3(fw * HousingConst.Cell, 0.02f, fd * HousingConst.Cell);
        }

        /// <summary>格子にある家具（上に置いた小物 → 床 → 壁 → 天井 → 敷物 の順に優先）。</summary>
        private PlacedFurniture FurnitureAtCell(string location, int room, int cx, int cz)
        {
            PlacedFurniture best = null;
            int bestRank = int.MaxValue;
            foreach (var f in state.furniture)
            {
                if (f.location != location || (location == HousingConst.LocHouse && f.room != room)) continue;
                var spec = HouseGeometry.Resolve(f);
                HouseGeometry.Footprint(spec, f.rot, out int fw, out int fd);
                if (cx < f.gx || cz < f.gz || cx >= f.gx + fw || cz >= f.gz + fd) continue;
                int rank;
                switch (spec.surface)
                {
                    case FurnitureSurface.Tabletop: rank = 0; break;
                    case FurnitureSurface.Floor: rank = 1; break;
                    case FurnitureSurface.Wall: rank = 2; break;
                    case FurnitureSurface.Ceiling: rank = 3; break;
                    default: rank = 4; break;
                }
                if (rank < bestRank) { bestRank = rank; best = f; }
            }
            return best;
        }
    }

    /// <summary>配置モードの操作パネル（右側。モーダルではない）。Esc で閉じると配置モードも終わる。</summary>
    public class PlacementPanel : UIWindow
    {
        private readonly HousingSystem sys;
        private Vector2 listScroll;

        public PlacementPanel(HousingSystem sys) { this.sys = sys; }

        public override string Title => Loc.T("模様替え", "Redecorate");
        public override bool IsModal => false;
        public override Vector2 Size => new Vector2(460, 780);
        public override Vector2? Position => new Vector2(UIStyles.VirtualWidth - 480, 150);

        public override void OnClose() => sys.ExitPlacementMode(true);

        public override void DrawContents(Rect content)
        {
            var player = Game.Instance?.LocalPlayer;
            if (player == null) return;
            GUILayout.Label(sys.CursorRoomLabel, UIStyles.LabelBold);
            GUILayout.Label(sys.HeldLabel, UIStyles.Label);
            GUI.color = sys.CursorValid ? UIStyles.Good : UIStyles.TextDim;
            GUILayout.Label(sys.CursorReason ?? "", UIStyles.LabelWrap);
            GUI.color = Color.white;
            GUILayout.Label(Loc.T("WASD：見る場所を動かす　マウス：場所を選ぶ\nクリック：置く／動かす　R：回転　X：しまう　Q：持つのをやめる　Esc：終わる",
                "WASD: move view  Mouse: choose spot\nClick: place/move  R: rotate  X: put away  Q: drop selection  Esc: finish"), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("回転", "Rotate"), UIStyles.ButtonSmall)) sys.RotateHeld();
            if (GUILayout.Button(Loc.T("しまう", "Put away"), UIStyles.ButtonSmall)) sys.StoreHoveredOrMoving();
            if (GUILayout.Button(Loc.T("やめる", "Drop"), UIStyles.ButtonSmall)) sys.CancelHold();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label(Loc.T("持ち物の家具・装飾", "Furniture & decor you carry"), UIStyles.LabelBold);
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(380));
            bool any = false;
            var slots = player.Inventory.slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty) continue;
                var def = s.Definition;
                bool wall = FurnitureCatalog.IsSurfaceDecor(def, true), floor = FurnitureCatalog.IsSurfaceDecor(def, false);
                if (!FurnitureCatalog.IsPlaceable(def) && !wall && !floor) continue;
                any = true;
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(44, 44, GUILayout.Width(44), GUILayout.Height(44));
                UIStyles.ItemIcon(r, s);
                string label = s.DisplayName + (string.IsNullOrEmpty(s.data) ? "" : Loc.T("（思い出つき）", " (with memories)"));
                if (wall) label += Loc.T("［壁紙］", " [wall]");
                if (floor) label += Loc.T("［床］", " [floor]");
                if (GUILayout.Button(label, UIStyles.ButtonSmall, GUILayout.Height(44)))
                {
                    if (wall || floor) sys.ApplySurface(s);
                    else sys.HoldFromSlot(i);
                }
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("置ける物を持っていません。家具職人から買えます。", "You carry nothing to place. The carpenter sells furniture."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.Label(Loc.T("壁紙・床：部屋を指してから選ぶ（前の物は持ち物に戻る）", "Wall/floor: point at a room first (the old one returns to you)"), UIStyles.LabelDim);
            if (GUILayout.Button(Loc.T("模様替えを終わる", "Finish"), UIStyles.Button, GUILayout.Height(44))) Close();
        }
    }
}
