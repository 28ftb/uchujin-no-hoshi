using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Housing
{
    // 家の中・庭・扉の見た目と、家具を「使う」処理（家具仕様 §2, §5, §6, §10）。
    public sealed partial class HousingSystem
    {
        private Transform housingRoot, shellRoot, furnitureRoot, gardenShellRoot, gardenFurnitureRoot;
        private bool sceneBuilt;
        private bool visualsDirty;
        private string builtRoomsSig, builtGardenSig;
        private readonly Dictionary<string, FurnitureObject> furnitureObjects = new Dictionary<string, FurnitureObject>();
        private readonly Dictionary<string, string> furnitureSigs = new Dictionary<string, string>();
        private GameObject homeManepi;
        private bool manepiSpawnFailed;

        private class WallSeg
        {
            public Transform root;
            public Vector3 normal;
        }

        private readonly List<WallSeg> walls = new List<WallSeg>();
        private int lastCutawayKey = int.MinValue;

        private static readonly Color DefaultWall = new Color(0.96f, 0.92f, 0.84f);
        private static readonly Color Wainscot = new Color(0.72f, 0.58f, 0.44f);
        private static readonly Color DoorColor = new Color(0.55f, 0.38f, 0.25f);

        public Vector3 HouseInteriorSpawn
        {
            get
            {
                var r0 = state.rooms.Count > 0 ? state.rooms[0] : null;
                if (r0 == null) return HousingConst.HouseOrigin + new Vector3(3f, 0.05f, 1.4f);
                return HouseGeometry.RoomOrigin(state, r0) + new Vector3(r0.widthCells * HousingConst.Cell * 0.5f, 0.05f, 1.4f);
            }
        }

        // ------------------------------------------------------------
        // 生成
        // ------------------------------------------------------------

        private void BuildSceneObjects()
        {
            if (Game == null || Game.SceneRoot == null) return;
            if (housingRoot != null) UnityEngine.Object.Destroy(housingRoot.gameObject);
            housingRoot = PrimitiveBuilder.Empty("Housing", Game.SceneRoot, Vector3.zero).transform;
            furnitureRoot = PrimitiveBuilder.Empty("HouseFurniture", housingRoot, Vector3.zero).transform;
            gardenFurnitureRoot = PrimitiveBuilder.Empty("GardenFurniture", housingRoot, Vector3.zero).transform;
            residentDoorsRoot = PrimitiveBuilder.Empty("ResidentDoors", housingRoot, Vector3.zero).transform;
            furnitureObjects.Clear();
            furnitureSigs.Clear();
            builtRoomsSig = null;
            builtGardenSig = null;

            Game.Areas.Register(new AreaDescriptor
            {
                Id = HousingConst.LocHouse,
                NameJa = "自分の家",
                NameEn = "My home",
                IsIndoor = true,
                ShowWeather = false,
                DefaultSpawn = HouseInteriorSpawn,
                DefaultYaw = 0f,
                AmbientOverride = new Color(0.78f, 0.72f, 0.64f)
            });

            BuildOutsideDoor();
            sceneBuilt = true;
            SyncVisuals();
            RebuildResidentDoors();
        }

        private void TeardownScene()
        {
            DestroyResidentInterior();
            ClearVisitorProxies();
            ClearCreatureVisuals();
            if (housingRoot != null) UnityEngine.Object.Destroy(housingRoot.gameObject);
            housingRoot = shellRoot = furnitureRoot = gardenShellRoot = gardenFurnitureRoot = residentDoorsRoot = null;
            furnitureObjects.Clear();
            furnitureSigs.Clear();
            walls.Clear();
            homeManepi = null;
            manepiSpawnFailed = false;
            sceneBuilt = false;
            builtRoomsSig = builtGardenSig = null;
        }

        private void BuildOutsideDoor()
        {
            var layout = Layout;
            if (layout == null) return;
            var f = layout.GetFacility(FacilityType.PlayerHouse);
            Vector3 pos = f != null ? f.entrance : layout.PlayerSpawn + new Vector3(0, 0, 3f);
            float yaw = f != null ? f.rotationY : 0f;
            pos = layout.OnGround(pos);
            // World は外観だけ作る。扉（入口）はこのモジュールが置く（連携の約束）。
            var door = PrimitiveBuilder.Empty("HomeDoor", housingRoot, pos);
            door.transform.rotation = Quaternion.Euler(0, yaw, 0);
            PrimitiveBuilder.Part(door.transform, PrimitiveType.Cube, new Vector3(0, 0.015f, 0.2f), new Vector3(1.3f, 0.03f, 0.8f), new Color(0.75f, 0.45f, 0.4f), "Doormat");
            PrimitiveBuilder.Part(door.transform, PrimitiveType.Cylinder, new Vector3(0.75f, 0.6f, 0.4f), new Vector3(0.08f, 0.6f, 0.08f), DoorColor, "Post");
            PrimitiveBuilder.EmissivePart(door.transform, PrimitiveType.Sphere, new Vector3(0.75f, 1.25f, 0.4f), Vector3.one * 0.18f, new Color(1f, 0.85f, 0.55f), "DoorLamp");
            PrimitiveBuilder.AddBoxCollider(door, new Vector3(0, 1.1f, 0), new Vector3(1.6f, 2.2f, 1.2f), true);
            var si = SimpleInteractable.Attach(door, "家に入る", "Enter home", p => EnterHome(p));
            si.canInteract = p => p != null;
        }

        public void EnterHome(PlayerState p)
        {
            if (p == null || Game == null) return;
            if (WorldAuthority.IsMultiplayer && p.PlayerId != HouseOwnerId && !state.guestsMayEnter && p.Permission < PermissionLevel.CoLiving)
            {
                UIManager.Toast(Loc.T("今は家に入れないようだ（家主の設定）。", "The owner has closed the house to visitors."));
                return;
            }
            Game.Areas.Transition(p, HousingConst.LocHouse, HouseInteriorSpawn, 0f);
        }

        private Vector3 ExitPosition(out float yaw)
        {
            var layout = Layout;
            var f = layout?.GetFacility(FacilityType.PlayerHouse);
            if (f == null)
            {
                yaw = 180f;
                return layout != null ? layout.OnGround(layout.PlayerSpawn) + Vector3.up * 0.2f : Vector3.up;
            }
            var rot = Quaternion.Euler(0, f.rotationY, 0);
            yaw = f.rotationY;
            return layout.OnGround(f.entrance + rot * Vector3.forward * 1.0f) + Vector3.up * 0.2f;
        }

        // ------------------------------------------------------------
        // 見た目の同期（状態の差分だけ作り直す。ゲストの RestoreState でも冪等）
        // ------------------------------------------------------------

        private string RoomsSignature()
        {
            var sb = new StringBuilder();
            foreach (var r in state.rooms) sb.Append(r.index).Append(':').Append((int)r.kind).Append(':').Append(r.widthCells).Append('x').Append(r.depthCells).Append(':').Append(r.wallpaperId).Append(':').Append(r.floorId).Append('|');
            return sb.ToString();
        }

        private static string FurnitureSignature(PlacedFurniture f)
        {
            var sb = new StringBuilder();
            sb.Append(f.itemId).Append('|').Append(f.location).Append('|').Append(f.room).Append('|').Append(f.gx).Append('|').Append(f.gz).Append('|').Append(f.rot).Append('|').Append(f.lightOn ? 1 : 0);
            foreach (var d in f.displayed) if (d != null && !d.IsEmpty) sb.Append('|').Append(d.itemId).Append('*').Append(d.count);
            return sb.ToString();
        }

        public void SyncVisuals()
        {
            if (!sceneBuilt || housingRoot == null) return;
            string roomsSig = RoomsSignature();
            if (roomsSig != builtRoomsSig)
            {
                BuildHouseShell();
                builtRoomsSig = roomsSig;
                // 部屋の位置が変わると家具の位置も変わるので全部作り直す
                foreach (var fo in furnitureObjects.Values) if (fo != null) UnityEngine.Object.Destroy(fo.gameObject);
                furnitureObjects.Clear();
                furnitureSigs.Clear();
                if (Game?.Areas.Get(HousingConst.LocHouse) is AreaDescriptor a) a.DefaultSpawn = HouseInteriorSpawn;
            }
            string gardenSig = state.gardenWidthCells + "x" + state.gardenDepthCells;
            if (gardenSig != builtGardenSig)
            {
                BuildGardenShell();
                builtGardenSig = gardenSig;
            }

            var alive = new HashSet<string>();
            foreach (var f in state.furniture)
            {
                if (f.location != HousingConst.LocHouse && f.location != HousingConst.LocGarden) continue;
                alive.Add(f.instanceId);
                string sig = FurnitureSignature(f);
                if (furnitureObjects.TryGetValue(f.instanceId, out var existing) && existing != null && furnitureSigs.TryGetValue(f.instanceId, out var old) && old == sig)
                {
                    if (existing.Visual?.Fx != null && existing.Visual.Fx.lightOn != f.lightOn) existing.Visual.Fx.SetLight(f.lightOn);
                    continue;
                }
                if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);
                var built = BuildFurnitureObject(f);
                if (built != null)
                {
                    furnitureObjects[f.instanceId] = built;
                    furnitureSigs[f.instanceId] = sig;
                }
            }
            var remove = new List<string>();
            foreach (var kv in furnitureObjects) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (furnitureObjects[id] != null) UnityEngine.Object.Destroy(furnitureObjects[id].gameObject);
                furnitureObjects.Remove(id);
                furnitureSigs.Remove(id);
            }
            if (placementActive) placementVisualsDirty = true;
            refreshCreaturesSoon = true;
            visitorProxiesDirty = true;
        }

        private FurnitureObject BuildFurnitureObject(PlacedFurniture f)
        {
            var def = ItemRegistry.GetOrPlaceholder(f.itemId);
            var spec = FurnitureCatalog.Resolve(def);
            var pos = HouseGeometry.FurniturePosition(state, Layout, f, spec, out float yaw);
            var parent = f.location == HousingConst.LocGarden ? gardenFurnitureRoot : furnitureRoot;
            FurnitureVisuals.BuildResult res;
            try
            {
                res = FurnitureVisuals.Build(def, spec, parent, pos, yaw, "Furniture_" + def.Id);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
            FurnitureVisuals.BuildDisplayed(res, spec, f.displayed);
            res.Fx.lightOn = f.lightOn;
            var fo = res.Root.AddComponent<FurnitureObject>();
            fo.System = this;
            fo.InstanceId = f.instanceId;
            fo.ItemId = f.itemId;
            fo.Visual = res;
            return fo;
        }

        public FurnitureObject GetFurnitureObject(string instanceId)
        {
            if (instanceId != null && furnitureObjects.TryGetValue(instanceId, out var fo)) return fo;
            return null;
        }

        // ------------------------------------------------------------
        // 家の器（床・壁・窓・階段・出口）
        // ------------------------------------------------------------

        private void BuildHouseShell()
        {
            if (shellRoot != null) UnityEngine.Object.Destroy(shellRoot.gameObject);
            shellRoot = PrimitiveBuilder.Empty("HouseShell", housingRoot, Vector3.zero).transform;
            walls.Clear();
            lastCutawayKey = int.MinValue;
            homeManepi = null;

            // 家の周りの地面（真っ暗な空間に浮かないように）
            foreach (int row in new[] { 0, 1, 2 })
            {
                if (HouseGeometry.CountRow(state, row) == 0) continue;
                float width = 0f, depth = 0f;
                foreach (var r in state.rooms)
                {
                    if (HouseGeometry.RowOf(r.kind) != row) continue;
                    width += r.widthCells * HousingConst.Cell + HousingConst.WallThickness;
                    depth = Mathf.Max(depth, r.depthCells * HousingConst.Cell);
                }
                var first = HouseGeometry.FirstOfRow(state, row);
                var o = HouseGeometry.RoomOrigin(state, first);
                var groundColor = row == 2 ? new Color(0.25f, 0.22f, 0.2f) : row == 1 ? new Color(0.55f, 0.75f, 0.95f) : new Color(0.45f, 0.62f, 0.38f);
                PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(width * 0.5f, -0.35f, depth * 0.5f), new Vector3(width + 30f, 0.2f, depth + 30f), groundColor, "Yard");
            }

            foreach (var room in state.rooms) BuildRoom(room);

            // 出口（最初の部屋の手前中央）
            var r0 = state.rooms[0];
            var o0 = HouseGeometry.RoomOrigin(state, r0);
            float w0 = r0.widthCells * HousingConst.Cell;
            var exitPos = ExitPosition(out float exitYaw);
            var portal = AreaPortal.Create(shellRoot, o0 + new Vector3(w0 * 0.5f, 1.1f, -0.12f), new Vector3(1.3f, 2.2f, 0.1f), AreaIds.Overworld, exitPos, exitYaw, "外に出る", "Go outside", DoorColor);
            portal.gameObject.name = "ExitDoor";
            PrimitiveBuilder.Part(portal.transform, PrimitiveType.Sphere, new Vector3(0.35f, 0f, 1.2f), new Vector3(0.06f, 0.03f, 0.6f), new Color(1f, 0.85f, 0.4f), "Knob");
            PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o0 + new Vector3(w0 * 0.5f, 0.012f, 0.45f), new Vector3(1.2f, 0.02f, 0.7f), new Color(0.75f, 0.45f, 0.4f), "Doormat", false);

            // 模様替えのボード（入口の左の壁）
            var board = PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o0 + new Vector3(w0 * 0.5f - 1.7f, 1.45f, 0.06f), new Vector3(0.9f, 0.65f, 0.06f), new Color(0.82f, 0.66f, 0.45f), "DecorateBoard", false);
            PrimitiveBuilder.Part(board.transform, PrimitiveType.Cube, new Vector3(0f, -0.05f, 0.6f), new Vector3(0.45f, 0.35f, 0.3f), new Color(1f, 0.95f, 0.85f), "Sketch");
            PrimitiveBuilder.Part(board.transform, PrimitiveType.Cube, new Vector3(0f, 0.22f, 0.6f), new Vector3(0.55f, 0.15f, 0.3f), new Color(0.85f, 0.4f, 0.35f), "Roof", new Vector3(0, 0, 0));
            PrimitiveBuilder.AddBoxCollider(board, Vector3.zero, new Vector3(1.2f, 1.4f, 8f), true);
            var bi = SimpleInteractable.Attach(board, "模様替えをする（Shift+E：家の記録）", "Redecorate (Shift+E: home records)", p =>
            {
                if (GameInput.Run) OpenHomeOverview();
                else StartPlacementMode(-1);
            });

            BuildStairs();
            SpawnHomeManepi();
        }

        private void BuildRoom(RoomData room)
        {
            var o = HouseGeometry.RoomOrigin(state, room);
            float W = room.widthCells * HousingConst.Cell, D = room.depthCells * HousingConst.Cell;
            var floorDecor = FurnitureCatalog.GetDecor(room.floorId);
            var wallDecor = FurnitureCatalog.GetDecor(room.wallpaperId);
            Color floorColor = floorDecor != null ? floorDecor.baseColor : DefaultFloor(room.kind);
            var floor = PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, -0.1f, D * 0.5f), new Vector3(W, 0.2f, D), floorColor, "Floor" + room.index);
            FloorPattern(floor.transform.parent, o, W, D, floorDecor, room.kind);

            if (room.kind == RoomKind.Waterside)
            {
                int poolCells = Mathf.Max(3, room.depthCells / 3);
                float pz = (room.depthCells - poolCells) * HousingConst.Cell;
                PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, 0.015f, pz + poolCells * HousingConst.Cell * 0.5f), new Vector3(W, 0.03f, poolCells * HousingConst.Cell), new Color(0.35f, 0.65f, 0.95f, 0.6f), "Pool", false);
                PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, 0.04f, pz), new Vector3(W, 0.08f, 0.1f), new Color(0.6f, 0.6f, 0.62f), "PoolEdge", false);
            }

            var left = HouseGeometry.LeftNeighbor(state, room);
            var right = HouseGeometry.RightNeighbor(state, room);
            bool terrace = room.kind == RoomKind.Terrace;
            float t = HousingConst.WallThickness;
            float h = terrace ? 0.9f : HousingConst.WallHeight;

            // 奥の壁
            AddWall(room, wallDecor, new Vector3(o.x, 0, o.z + D + t * 0.5f), Vector3.right, W, h, Vector3.forward, null);
            // 手前の壁（最初の部屋は入口の隙間）
            if (room.index == state.rooms[0].index)
                AddWall(room, wallDecor, new Vector3(o.x, 0, o.z - t * 0.5f), Vector3.right, W, h, Vector3.back, new Vector2(W * 0.5f - 0.7f, W * 0.5f + 0.7f));
            else
                AddWall(room, wallDecor, new Vector3(o.x, 0, o.z - t * 0.5f), Vector3.right, W, h, Vector3.back, null);
            // 左の壁（隣があれば出入口）
            AddWall(room, wallDecor, new Vector3(o.x - t * 0.5f, 0, o.z), Vector3.forward, D, h, Vector3.left, left != null ? new Vector2(HouseGeometry.DoorZ0, HouseGeometry.DoorZ1) : (Vector2?)null);
            // 右の壁（隣の部屋の左の壁と重ならない部分だけ）
            if (right == null) AddWall(room, wallDecor, new Vector3(o.x + W + t * 0.5f, 0, o.z), Vector3.forward, D, h, Vector3.right, null);
            else
            {
                float nd = right.depthCells * HousingConst.Cell;
                if (D > nd) AddWall(room, wallDecor, new Vector3(o.x + W + t * 0.5f, 0, o.z + nd), Vector3.forward, D - nd, h, Vector3.right, null);
            }

            // 高窓（奥の壁の上の方。地下・テラス以外）
            if (room.kind != RoomKind.Basement && !terrace)
            {
                var winRoot = PrimitiveBuilder.Empty("HighWindow", shellRoot, o + new Vector3(W * 0.5f, 2.25f, D - 0.02f));
                winRoot.transform.rotation = Quaternion.Euler(0, 180f, 0);
                var fx = winRoot.AddComponent<FurnitureFx>();
                PrimitiveBuilder.Part(winRoot.transform, PrimitiveType.Cube, new Vector3(0, 0, 0.0f), new Vector3(1.4f, 0.42f, 0.04f), Color.white, "Frame");
                var pane = PrimitiveBuilder.EmissivePart(winRoot.transform, PrimitiveType.Cube, new Vector3(0, 0, 0.03f), new Vector3(1.25f, 0.32f, 0.01f), new Color(0.55f, 0.78f, 1f), "Pane");
                fx.weatherPane = pane.GetComponent<Renderer>();
                // 奥の壁と一緒に低くなるよう、壁の子にする
                foreach (var wseg in walls)
                {
                    if (wseg.normal == Vector3.forward && Vector3.Distance(wseg.root.position, new Vector3(o.x, 0, o.z + D + t * 0.5f)) < 0.01f)
                    {
                        winRoot.transform.SetParent(wseg.root, true);
                        break;
                    }
                }
            }

            // 部屋の灯り（家具の照明が無くても真っ暗にならないよう、控えめに常時点ける）【仮】
            if (!terrace)
            {
                var lightGo = PrimitiveBuilder.Empty("RoomLight", shellRoot, o + new Vector3(W * 0.5f, HousingConst.WallHeight - 0.2f, D * 0.5f));
                var l = lightGo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = Mathf.Max(W, D) * 0.95f;
                l.intensity = room.kind == RoomKind.Basement ? 0.65f : 0.45f;
                l.color = new Color(1f, 0.92f, 0.8f);
                l.shadows = LightShadows.None;
            }

            // 屋根の名残（ロフトの梁）
            if (room.kind == RoomKind.Loft)
                for (float x = 0.8f; x < W; x += 1.6f)
                    PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(x, HousingConst.WallHeight - 0.1f, D * 0.5f), new Vector3(0.15f, 0.15f, D), new Color(0.5f, 0.35f, 0.25f), "Beam", false);
        }

        private static Color DefaultFloor(RoomKind k)
        {
            switch (k)
            {
                case RoomKind.Basement: return new Color(0.48f, 0.47f, 0.5f);
                case RoomKind.Terrace: return new Color(0.66f, 0.5f, 0.36f);
                case RoomKind.Glass: return new Color(0.85f, 0.78f, 0.62f);
                default: return new Color(0.74f, 0.57f, 0.40f);
            }
        }

        private Color WallColorFor(RoomData room, DecorSpec decor)
        {
            if (room.kind == RoomKind.Glass) return new Color(0.8f, 0.95f, 1f, 0.28f);
            if (decor != null) return decor.baseColor;
            if (room.kind == RoomKind.Basement) return new Color(0.55f, 0.53f, 0.55f);
            if (room.kind == RoomKind.Terrace) return new Color(0.6f, 0.45f, 0.32f);
            return DefaultWall;
        }

        /// <summary>壁を1本作る。gap は along 方向の（開始,終了）の隙間（出入口）。</summary>
        private void AddWall(RoomData room, DecorSpec decor, Vector3 start, Vector3 along, float length, float height, Vector3 outward, Vector2? gap)
        {
            if (gap.HasValue)
            {
                var g = gap.Value;
                if (g.x > 0.01f) AddWallPiece(room, decor, start, along, 0f, g.x, height, outward);
                if (g.y < length - 0.01f) AddWallPiece(room, decor, start, along, g.y, length, height, outward);
                // 出入口の上の鴨居
                if (room.kind != RoomKind.Terrace)
                {
                    var mid = start + along * ((g.x + g.y) * 0.5f);
                    var lintel = PrimitiveBuilder.Empty("Lintel", shellRoot, mid);
                    var size = along == Vector3.right ? new Vector3(g.y - g.x, height - 2.25f, HousingConst.WallThickness) : new Vector3(HousingConst.WallThickness, height - 2.25f, g.y - g.x);
                    PrimitiveBuilder.Part(lintel.transform, PrimitiveType.Cube, new Vector3(0, 2.25f + (height - 2.25f) * 0.5f, 0), size, WallColorFor(room, decor), "Lintel");
                    walls.Add(new WallSeg { root = lintel.transform, normal = outward });
                }
                return;
            }
            AddWallPiece(room, decor, start, along, 0f, length, height, outward);
        }

        private void AddWallPiece(RoomData room, DecorSpec decor, Vector3 start, Vector3 along, float a, float b, float height, Vector3 outward)
        {
            float len = b - a;
            if (len <= 0.01f) return;
            var center = start + along * ((a + b) * 0.5f);
            var root = PrimitiveBuilder.Empty("Wall", shellRoot, center);
            bool alongX = along == Vector3.right;
            float t = HousingConst.WallThickness;
            var size = alongX ? new Vector3(len, height, t) : new Vector3(t, height, len);
            var color = WallColorFor(room, decor);
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, height * 0.5f, 0), size, color, "Body");
            // 当たり判定は低め（カメラの回避レイに当たらない高さ）。見た目とは別に持つ。
            var col = PrimitiveBuilder.Empty("WallCollider", shellRoot, center);
            PrimitiveBuilder.AddBoxCollider(col, new Vector3(0, HousingConst.WallColliderHeight * 0.5f, 0), alongX ? new Vector3(len, HousingConst.WallColliderHeight, t) : new Vector3(t, HousingConst.WallColliderHeight, len));

            if (room.kind != RoomKind.Terrace && room.kind != RoomKind.Glass)
            {
                var inward = -outward * (t * 0.5f + 0.006f);
                if (decor == null)
                {
                    // 既定の内装：腰板
                    var ws = alongX ? new Vector3(len, 0.9f, 0.01f) : new Vector3(0.01f, 0.9f, len);
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, inward + new Vector3(0, 0.45f, 0), ws, room.kind == RoomKind.Basement ? new Color(0.45f, 0.44f, 0.46f) : Wainscot, "Wainscot");
                }
                else WallPattern(root.transform, decor, len, height, alongX, inward);
            }
            if (room.kind == RoomKind.Glass)
            {
                // ガラス張り：枠
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 1.2f));
                for (int i = 0; i <= n; i++)
                {
                    float off = -len * 0.5f + len * i / n;
                    var p = alongX ? new Vector3(off, height * 0.5f, 0) : new Vector3(0, height * 0.5f, off);
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, p, new Vector3(0.06f, height, 0.06f), Color.white, "Mullion");
                }
            }
            walls.Add(new WallSeg { root = root.transform, normal = outward });
        }

        private void WallPattern(Transform root, DecorSpec decor, float len, float height, bool alongX, Vector3 inward)
        {
            Func<float, float, Vector3> at = (u, y) => inward + (alongX ? new Vector3(u, y, 0) : new Vector3(0, y, u));
            var flat = alongX ? new Vector3(1, 1, 0.01f) : new Vector3(0.01f, 1, 1);
            switch (decor.pattern)
            {
                case "planks":
                case "stripe":
                    for (float u = -len * 0.5f + 0.3f; u < len * 0.5f; u += 0.45f)
                        PrimitiveBuilder.Part(root, PrimitiveType.Cube, at(u, height * 0.5f), Vector3.Scale(flat, new Vector3(alongX ? 0.03f : 1, height, alongX ? 1 : 0.03f)), decor.accent, "Stripe");
                    break;
                case "brick":
                    for (float y = 0.3f; y < height; y += 0.3f)
                        PrimitiveBuilder.Part(root, PrimitiveType.Cube, at(0, y), Vector3.Scale(flat, new Vector3(alongX ? len : 1, 0.03f, alongX ? 1 : len)), decor.accent, "Mortar");
                    break;
                default:
                {
                    bool glow = decor.glow;
                    var shape = decor.pattern == "crystal" ? PrimitiveType.Cube : PrimitiveType.Sphere;
                    float size = decor.pattern == "stars" ? 0.06f : decor.pattern == "flowers" ? 0.12f : decor.pattern == "crystal" ? 0.14f : 0.1f;
                    int k = 0;
                    for (float y = 0.5f; y < height - 0.2f; y += 0.65f)
                        for (float u = -len * 0.5f + 0.35f + (k++ % 2) * 0.3f; u < len * 0.5f - 0.1f; u += 0.7f)
                        {
                            var s = alongX ? new Vector3(size, size, 0.02f) : new Vector3(0.02f, size, size);
                            if (glow) PrimitiveBuilder.EmissivePart(root, shape, at(u, y), s, decor.accent, "Dot");
                            else PrimitiveBuilder.Part(root, shape, at(u, y), s, decor.accent, "Dot");
                        }
                    break;
                }
            }
        }

        private void FloorPattern(Transform parent, Vector3 o, float W, float D, DecorSpec decor, RoomKind kind)
        {
            string pattern = decor != null ? decor.pattern : (kind == RoomKind.Basement ? "tiles" : "planks");
            Color acc = decor != null ? decor.accent : Color.Lerp(DefaultFloor(kind), Color.black, 0.15f);
            bool glow = decor != null && decor.glow;
            switch (pattern)
            {
                case "planks":
                    for (float z = 0.4f; z < D; z += 0.4f)
                        PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, 0.002f, z), new Vector3(W, 0.004f, 0.02f), acc, "Plank", false);
                    break;
                case "tiles":
                    for (float z = 0.6f; z < D; z += 0.6f)
                        PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, 0.002f, z), new Vector3(W, 0.004f, 0.03f), acc, "Grout", false);
                    for (float x = 0.6f; x < W; x += 0.6f)
                        PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(x, 0.002f, D * 0.5f), new Vector3(0.03f, 0.004f, D), acc, "Grout", false);
                    break;
                case "water":
                    var water = PrimitiveBuilder.Create(PrimitiveType.Cube, shellRoot, o + new Vector3(W * 0.5f, 0.004f, D * 0.5f), new Vector3(W, 0.006f, D), new Color(0.6f, 0.85f, 1f, 0.5f), "WaterSurface", false);
                    if (glow) water.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetEmissive(new Color(0.45f, 0.72f, 0.95f));
                    break;
                default:
                {
                    var shape = pattern == "grass" ? PrimitiveType.Capsule : PrimitiveType.Sphere;
                    int k = 0;
                    for (float z = 0.3f; z < D; z += 0.7f)
                        for (float x = 0.3f + (k++ % 2) * 0.35f; x < W; x += 0.7f)
                        {
                            var s = pattern == "grass" ? new Vector3(0.05f, 0.06f, 0.05f) : pattern == "fluffy" ? new Vector3(0.3f, 0.02f, 0.3f) : new Vector3(0.08f, 0.01f, 0.08f);
                            var go = glow
                                ? PrimitiveBuilder.EmissivePart(shellRoot, shape, o + new Vector3(x, 0.01f, z), s, acc, "FloorDot")
                                : PrimitiveBuilder.Create(PrimitiveType.Sphere, shellRoot, o + new Vector3(x, 0.01f, z), s, acc, "FloorDot", false);
                        }
                    break;
                }
            }
        }

        private void BuildStairs()
        {
            var r0 = state.rooms[0];
            var o0 = HouseGeometry.RoomOrigin(state, r0);
            var loft = HouseGeometry.FirstOfRow(state, 1);
            var basement = HouseGeometry.FirstOfRow(state, 2);
            int sc = HouseGeometry.StairCells;
            float s = sc * HousingConst.Cell;
            if (loft != null)
            {
                var a = o0 + new Vector3(s * 0.5f, 0, r0.depthCells * HousingConst.Cell - s * 0.5f);
                var b = HouseGeometry.RoomOrigin(state, loft) + new Vector3(s * 0.5f, 0, loft.depthCells * HousingConst.Cell - s * 0.5f);
                BuildStairObject(a, true, "2階へ上がる", "Go upstairs", StairArrival(loft));
                BuildStairObject(b, false, "下りる", "Go downstairs", StairArrival(r0, true));
            }
            if (basement != null)
            {
                var a = o0 + new Vector3(r0.widthCells * HousingConst.Cell - s * 0.5f, 0, r0.depthCells * HousingConst.Cell - s * 0.5f);
                var b = HouseGeometry.RoomOrigin(state, basement) + new Vector3(s * 0.5f, 0, basement.depthCells * HousingConst.Cell - s * 0.5f);
                BuildStairObject(a, false, "地下へ下りる", "Go to the basement", StairArrival(basement));
                BuildStairObject(b, true, "上がる", "Go up", StairArrival(r0, false, true));
            }
        }

        private Vector3 StairArrival(RoomData room, bool leftStairs = false, bool rightStairs = false)
        {
            var o = HouseGeometry.RoomOrigin(state, room);
            float s = HouseGeometry.StairCells * HousingConst.Cell;
            float x = rightStairs ? room.widthCells * HousingConst.Cell - s * 0.5f : s * 0.5f;
            return o + new Vector3(x, 0.05f, room.depthCells * HousingConst.Cell - s - 0.6f);
        }

        private void BuildStairObject(Vector3 center, bool up, string ja, string en, Vector3 arrival)
        {
            var root = PrimitiveBuilder.Empty(up ? "StairsUp" : "StairsDown", shellRoot, center);
            float s = HouseGeometry.StairCells * HousingConst.Cell;
            var c = new Color(0.62f, 0.45f, 0.3f);
            if (up)
            {
                for (int i = 0; i < 6; i++)
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, 0.12f + i * 0.24f, -s * 0.4f + i * s * 0.16f), new Vector3(s * 0.9f, 0.24f + i * 0.0f, s * 0.18f), Color.Lerp(c, Color.white, (i % 2) * 0.1f), "Step");
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Cylinder, new Vector3(s * 0.45f, 1.0f, 0), new Vector3(0.05f, 1f, 0.05f), new Color(0.45f, 0.3f, 0.2f), "Rail");
            }
            else
            {
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, 0.01f, 0), new Vector3(s * 0.9f, 0.02f, s * 0.9f), new Color(0.2f, 0.18f, 0.16f), "Hole");
                for (int i = 0; i < 3; i++)
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, 0.02f, -s * 0.3f + i * s * 0.3f), new Vector3(s * 0.8f, 0.02f, 0.12f), c, "Step");
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(-s * 0.45f, 0.45f, 0), new Vector3(0.05f, 0.9f, s * 0.9f), c, "Rail");
            }
            PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, 0.8f, 0), new Vector3(s, 1.6f, s), true);
            SimpleInteractable.Attach(root, ja, en, p =>
            {
                if (p == null) return;
                StandUp();
                p.Position = arrival;
                if (p.Avatar != null) p.Avatar.rotation = Quaternion.Euler(0, 180f, 0);
                if (p.IsLocal)
                {
                    ScreenFader.FadeIn(0.3f);
                    CameraRig.Instance?.SnapToTarget();
                }
            });
        }

        private void BuildGardenShell()
        {
            if (gardenShellRoot != null) UnityEngine.Object.Destroy(gardenShellRoot.gameObject);
            gardenShellRoot = PrimitiveBuilder.Empty("GardenShell", housingRoot, Vector3.zero).transform;
            var layout = Layout;
            if (layout == null) return;
            float yaw = HouseGeometry.GardenYaw(layout);
            int w = state.gardenWidthCells, d = state.gardenDepthCells;
            // 庭の範囲を示す小さな杭（4mごと）
            for (int x = 0; x <= w; x += 8)
                foreach (int z in new[] { 0, d })
                    Stake(HouseGeometry.GardenCellToWorld(layout, x, z), yaw);
            for (int z = 8; z < d; z += 8)
                foreach (int x in new[] { 0, w })
                    Stake(HouseGeometry.GardenCellToWorld(layout, x, z), yaw);
        }

        private void Stake(Vector3 p, float yaw)
        {
            var go = PrimitiveBuilder.Create(PrimitiveType.Cylinder, gardenShellRoot, p + new Vector3(0, 0.2f, 0), new Vector3(0.08f, 0.2f, 0.08f), new Color(0.75f, 0.6f, 0.4f), "GardenStake", false);
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Sphere, new Vector3(0, 1.1f, 0), new Vector3(1.6f, 0.4f, 1.6f), new Color(1f, 0.85f, 0.5f), "Cap");
        }

        public bool IsInGarden(Vector3 p, string areaId)
        {
            if (areaId != AreaIds.Overworld) return false;
            return HouseGeometry.WorldToGardenCell(Layout, state, p, out _, out _);
        }

        // ------------------------------------------------------------
        // 留守番マネピ（§13）
        // ------------------------------------------------------------

        private void SpawnHomeManepi()
        {
            if (shellRoot == null || homeManepi != null || manepiSpawnFailed) return;
            var manepi = Game?.Service<IManepiService>();
            if (manepi == null) return;
            var r0 = state.rooms[0];
            var o0 = HouseGeometry.RoomOrigin(state, r0);
            var pos = o0 + new Vector3(r0.widthCells * HousingConst.Cell * 0.5f + 1.6f, 0f, 0.5f);
            // 止まり木の台（マネピの見た目は Tablet/Creatures 側が作る）
            var stand = PrimitiveBuilder.Create(PrimitiveType.Cylinder, shellRoot, pos + new Vector3(0, 0.35f, 0), new Vector3(0.35f, 0.35f, 0.35f), new Color(0.65f, 0.48f, 0.32f), "ManepiStand", false);
            try
            {
                homeManepi = manepi.SpawnManepi(ManepiBoardId, shellRoot, pos + new Vector3(0, 0.72f, 0), "留守番マネピ", "Home Manepi");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                manepiSpawnFailed = true;
            }
        }

        // ------------------------------------------------------------
        // 壁の切り取り表示（カメラ側の壁を低くする）
        // ------------------------------------------------------------

        private void TickCutaway()
        {
            var p = LocalPlayer;
            var cam = Camera.main;
            if (p == null || cam == null || (walls.Count == 0 && residentInteriorWalls.Count == 0)) return;
            bool inHouse = p.CurrentAreaId == HousingConst.LocHouse || IsResidentHomeArea(p.CurrentAreaId);
            var fwd = cam.transform.forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            int key = inHouse ? Mathf.RoundToInt(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg / 10f) : 9999;
            if (key == lastCutawayKey) return;
            lastCutawayKey = key;
            foreach (var list in new[] { walls, residentInteriorWalls })
                foreach (var w in list)
                {
                    if (w.root == null) continue;
                    bool low = inHouse && Vector3.Dot(w.normal, -fwd) > 0.3f;
                    w.root.localScale = new Vector3(1f, low ? 0.16f : 1f, 1f);
                }
        }

        // ------------------------------------------------------------
        // 家具を使う（§6）
        // ------------------------------------------------------------

        public string GetFurniturePrompt(FurnitureObject fo, PlayerState player)
        {
            var def = fo.Def;
            if (!string.IsNullOrEmpty(fo.ResidentHomeOwner)) return Loc.Format("{0}を眺める", "Look at the {0}", def.Name);
            var f = FindFurniture(fo.InstanceId);
            var spec = FurnitureCatalog.Resolve(def);
            string verb = UseVerb(def, spec, f);
            return verb + Loc.T("　（Shift+E：調べる）", "  (Shift+E: inspect)");
        }

        private string UseVerb(ItemDefinition def, FurnitureSpec spec, PlacedFurniture f)
        {
            if (def.HasTag(ItemTags.Kitchen)) return Loc.Format("{0}で料理する", "Cook at the {0}", def.Name);
            if (def.HasTag("workbench")) return Loc.Format("{0}で作業する", "Work at the {0}", def.Name);
            switch (spec.use)
            {
                case FurnitureUse.Sit: return Loc.Format("{0}に座る", "Sit on the {0}", def.Name);
                case FurnitureUse.Sleep: return Loc.T("眠る（朝まで）", "Sleep until morning");
                case FurnitureUse.Light: return f != null && f.lightOn ? Loc.T("灯りを消す", "Turn off the light") : Loc.T("灯りを点ける", "Turn on the light");
                case FurnitureUse.Music: return Loc.Format("{0}を鳴らす", "Play the {0}", def.Name);
                case FurnitureUse.Aquarium: return Loc.T("水槽をのぞく", "Look into the tank");
                case FurnitureUse.Storage: return Loc.Format("{0}を開ける", "Open the {0}", def.Name);
                case FurnitureUse.Display: return Loc.T("飾る・眺める", "Display / look");
                case FurnitureUse.Table: return Loc.T("料理や小物を並べる", "Set dishes or things");
                case FurnitureUse.Toy: return Loc.T("遊ぶ", "Play");
                case FurnitureUse.Plant: return Loc.T("眺める", "Admire");
                case FurnitureUse.Book: return Loc.T("本を読む", "Read a book");
                case FurnitureUse.Window: return Loc.T("外を眺める", "Look outside");
                case FurnitureUse.Perch: return Loc.T("伝言を聞く", "Hear messages");
                case FurnitureUse.Habitat: return Loc.T("住処をのぞく", "Look at the nest");
                case FurnitureUse.Mirror: return Loc.T("鏡をのぞく", "Look in the mirror");
                case FurnitureUse.SoundFloor: return Loc.T("踏んで鳴らす", "Step to play");
                case FurnitureUse.Clock: return Loc.T("時計を見る", "Check the clock");
                case FurnitureUse.Workbench: return Loc.Format("{0}で作業する", "Work at the {0}", def.Name);
                default: return Loc.Format("{0}を調べる", "Inspect the {0}", def.Name);
            }
        }

        public void UseFurniture(FurnitureObject fo, PlayerState player)
        {
            if (fo == null || player == null) return;
            if (!string.IsNullOrEmpty(fo.ResidentHomeOwner))
            {
                InspectResidentFurniture(fo, player);
                return;
            }
            var f = FindFurniture(fo.InstanceId);
            if (f == null) return;
            var def = fo.Def;
            var spec = FurnitureCatalog.Resolve(def);

            if (def.HasTag(ItemTags.Kitchen) || def.HasTag("workbench") || spec.use == FurnitureUse.Workbench)
            {
                var crafting = Game.Service<ICraftingService>();
                if (crafting == null)
                {
                    UIManager.Toast(Loc.T("まだここでは何も作れないようだ。", "Nothing can be made here yet."));
                    return;
                }
                crafting.OpenStation(def.HasTag(ItemTags.Kitchen) ? CraftStationType.Kitchen : CraftStationType.Workbench, f.instanceId, player);
                return;
            }

            switch (spec.use)
            {
                case FurnitureUse.Sit:
                    SitOn(fo, f, spec, player);
                    break;
                case FurnitureUse.Sleep:
                    UIManager.Confirm(Loc.T("眠る", "Sleep"), Loc.T("眠って、翌朝まで時間を進めますか？", "Sleep and skip time until the next morning?"), () =>
                    {
                        StandUp();
                        SleepHelper.RequestSleep(player);
                    });
                    break;
                case FurnitureUse.Light:
                    WorldAuthority.Submit(new ToggleFurnitureLightCommand { instanceId = f.instanceId });
                    break;
                case FurnitureUse.Music:
                    PlayFurnitureMusic(fo, f, player);
                    break;
                case FurnitureUse.Aquarium:
                case FurnitureUse.Display:
                case FurnitureUse.Table:
                    UIManager.Open(new DisplayWindow(this, f.instanceId, player));
                    break;
                case FurnitureUse.Storage:
                    OpenStorage(f, player);
                    break;
                case FurnitureUse.Toy:
                    PlayWithToy(fo, f);
                    break;
                case FurnitureUse.Plant:
                    if (spec.displaySlots > 0) UIManager.Open(new DisplayWindow(this, f.instanceId, player));
                    else UIManager.Toast(Loc.T("葉っぱがつやつやしている。", "The leaves are glossy."));
                    break;
                case FurnitureUse.Book:
                    ReadBook(f);
                    break;
                case FurnitureUse.Window:
                {
                    var w = Game.Service<IWeatherService>();
                    string ws = w != null ? w.Current.Summary() : Loc.T("いい天気", "nice weather");
                    UIManager.Toast(Loc.Format("窓の外は{0}。", "Outside: {0}.", ws));
                    break;
                }
                case FurnitureUse.Perch:
                {
                    var manepi = Game.Service<IManepiService>();
                    if (manepi != null) manepi.OpenBoard(ManepiBoardId, player);
                    else UIManager.Toast(Loc.T("止まり木は静かだ。", "The perch is quiet."));
                    knowledge.seenHomeMessages = 0;
                    NotifyHomeMessages();
                    break;
                }
                case FurnitureUse.Habitat:
                    OpenFurnitureDetail(f.instanceId, player);
                    break;
                case FurnitureUse.Mirror:
                    Game.PlayerSys?.LocalController?.PlayGesture(PlayerGesture.Happy);
                    UIManager.Toast(Loc.T("鏡の中の自分が、にっこりした。", "Your reflection smiles back."));
                    break;
                case FurnitureUse.SoundFloor:
                    ProceduralMusic.PlayNote(fo.transform.position, StableHash.Int(planetSeed, f.instanceId + TimeKey(), 0, 8));
                    break;
                case FurnitureUse.Clock:
                    UIManager.Toast(Clock != null ? Clock.FormatDateLong() + "  " + Clock.FormatClock() : "");
                    break;
                default:
                    OpenFurnitureDetail(f.instanceId, player);
                    break;
            }
        }

        private string TimeKey() => Clock != null ? Clock.TotalMinutes.ToString() : "0";

        public void OpenFurnitureDetail(string instanceId, PlayerState player)
        {
            if (FindFurniture(instanceId) == null) return;
            UIManager.Open(new FurnitureDetailWindow(this, instanceId, player));
        }

        public void OpenStorage(PlacedFurniture f, PlayerState player)
        {
            if (f.privateStorage && f.ownerId != player.PlayerId)
            {
                UIManager.Toast(Loc.T("持ち主の個人用の収納だ。", "This is someone's private storage."));
                return;
            }
            UIManager.Open(new StorageWindow(this, f.instanceId, player));
        }

        public void OpenHomeOverview()
        {
            var tablet = Game.Service<ITabletService>();
            if (tablet != null) tablet.Open(AppId);
            else UIManager.Open(new HomeOverviewWindow(this));
        }

        private void ReadBook(PlacedFurniture f)
        {
            // 本の中身は宇宙語のまま（訳さない）。星のシードと本ごとに決まる。
            var speech = Game.Service<ISpeechService>();
            if (speech == null)
            {
                UIManager.Toast(Loc.T("見慣れない文字がびっしり並んでいる。", "Pages full of unfamiliar letters."));
                return;
            }
            var rng = SeededRandom.For(planetSeed, "housing.book." + f.instanceId + "." + Today % 5);
            var subjects = new[] { Concepts.STAR, Concepts.SKY, Concepts.FISH, Concepts.FLOWER, Concepts.CREATURE, Concepts.PLANET, Concepts.MOON, Concepts.TREE, Concepts.SEA };
            var preds = new[] { Concepts.SHINE, Concepts.FLY, Concepts.GROW, Concepts.SLEEP, Concepts.SING, Concepts.FLOAT, Concepts.LIVE };
            var u = new Utterance("book:" + f.instanceId, "housing.book");
            for (int i = 0; i < 3; i++)
                u.Say(MeaningFrame.Make(SpeechAct.Statement, rng.Pick(preds)).Agent(rng.Pick(subjects)).When(rng.Chance(0.5f) ? Concepts.LONG_AGO : Concepts.ALWAYS));
            string text;
            try { text = speech.Render(u)?.text; }
            catch (Exception e) { Debug.LogException(e); text = null; }
            UIManager.Message(Loc.T("本を開いた", "You open a book"), string.IsNullOrEmpty(text) ? "……" : text);
        }

        private void PlayWithToy(FurnitureObject fo, PlacedFurniture f)
        {
            if (fo.Visual?.Root != null) fo.StartCoroutine(Bounce(fo.Visual.Root.transform));
            int n = AttractCreaturesTo(fo.transform.position, 4f);
            UIManager.Toast(n > 0 ? Loc.T("生き物たちが集まってきた！", "Creatures gather around!") : Loc.T("ころころ転がして遊んだ。", "You roll it around."));
        }

        private System.Collections.IEnumerator Bounce(Transform t)
        {
            var basePos = t.position;
            float time = 0;
            while (time < 0.8f && t != null)
            {
                time += UnityEngine.Time.deltaTime;
                t.position = basePos + Vector3.up * Mathf.Abs(Mathf.Sin(time * 10f)) * 0.15f * (1f - time / 0.8f);
                yield return null;
            }
            if (t != null) t.position = basePos;
        }

        private void PlayFurnitureMusic(FurnitureObject fo, PlacedFurniture f, PlayerState player)
        {
            var spec = FurnitureCatalog.Resolve(fo.Def);
            ProceduralMusic.PlayTune(fo.transform.position, spec.style, StableHash.Derive(planetSeed, f.instanceId) ^ (ulong)Today);
            if (fo.Visual?.Root != null) fo.StartCoroutine(Bounce(fo.Visual.Root.transform));
            AttractCreaturesTo(fo.transform.position, 6f, CreatureAbility.Sound, CreatureAbility.Mimic, CreatureAbility.Echo);
            if (IsAuthority) OnPlayerMusic(f, player);
            UIManager.Toast(Loc.T("♪ この星の音階が響いた。", "♪ This planet's scale rings out."));
        }

        // ------------------------------------------------------------
        // 座る（スタミナは無いので「くつろぐ」演出だけ）
        // ------------------------------------------------------------

        private FurnitureObject sittingOn;
        private Collider sittingCollider;
        private Transform sittingVisual;
        private Vector3 sittingVisualPos, sittingVisualScale;
        private string sittingArea;

        private void SitOn(FurnitureObject fo, PlacedFurniture f, FurnitureSpec spec, PlayerState player)
        {
            if (!player.IsLocal || player.Avatar == null) return;
            StandUp();
            var seat = fo.Visual?.SeatPoint != null ? fo.Visual.SeatPoint.position : fo.transform.position + Vector3.up * spec.seatHeight;
            float floorY = fo.transform.position.y;
            sittingCollider = fo.GetComponent<Collider>();
            if (sittingCollider != null) sittingCollider.isTrigger = true;
            player.Position = new Vector3(seat.x, floorY + 0.02f, seat.z);
            player.Avatar.rotation = Quaternion.Euler(0, fo.transform.eulerAngles.y, 0);
            sittingVisual = player.Avatar.Find("Visual");
            if (sittingVisual != null)
            {
                sittingVisualPos = sittingVisual.localPosition;
                sittingVisualScale = sittingVisual.localScale;
                float lift = Mathf.Max(0f, seat.y - floorY - 0.45f);
                sittingVisual.localPosition = sittingVisualPos + new Vector3(0, lift + 0.05f, 0);
                sittingVisual.localScale = new Vector3(sittingVisualScale.x, sittingVisualScale.y * 0.78f, sittingVisualScale.z);
            }
            if (fo.Visual?.Fx != null) fo.Visual.Fx.occupiedUntil = UnityEngine.Time.time + 100000f;
            sittingOn = fo;
            sittingArea = player.CurrentAreaId;
            // マルチ：友達が座ったことを家具の思い出に（1日1回）
            if (WorldAuthority.IsMultiplayer && player.PlayerId != HouseOwnerId)
                WorldAuthority.Submit(new FurnitureNoteCommand { instanceId = f.instanceId, mode = FurnitureNoteCommand.ModeSatPlayer });
        }

        private void TickSitting()
        {
            if (sittingOn == null) return;
            var p = LocalPlayer;
            if (p == null || sittingOn == null || p.CurrentAreaId != sittingArea || GameInput.Move.sqrMagnitude > 0.01f || GameInput.Jump || p.InputLocked)
                StandUp();
        }

        public void StandUp()
        {
            if (sittingOn == null) return;
            if (sittingCollider != null) sittingCollider.isTrigger = false;
            if (sittingVisual != null)
            {
                sittingVisual.localPosition = sittingVisualPos;
                sittingVisual.localScale = sittingVisualScale;
            }
            if (sittingOn != null && sittingOn.Visual?.Fx != null) sittingOn.Visual.Fx.occupiedUntil = 0f;
            sittingOn = null;
            sittingCollider = null;
            sittingVisual = null;
        }

        // ------------------------------------------------------------
        // 音のなる床（§7）
        // ------------------------------------------------------------

        private string lastSoundTile;

        private void TickSoundFloor()
        {
            var p = LocalPlayer;
            if (p == null || p.Avatar == null || sceneBuilt == false) return;
            if (p.CurrentAreaId != HousingConst.LocHouse && p.CurrentAreaId != AreaIds.Overworld) return;
            string current = null;
            foreach (var kv in furnitureObjects)
            {
                var fo = kv.Value;
                if (fo == null) continue;
                var spec = FurnitureCatalog.Resolve(fo.Def);
                if (spec.use != FurnitureUse.SoundFloor) continue;
                var local = fo.transform.InverseTransformPoint(p.Position);
                float W = spec.w * HousingConst.Cell, D = spec.d * HousingConst.Cell;
                if (Mathf.Abs(local.x) > W * 0.5f || Mathf.Abs(local.z) > D * 0.5f || Mathf.Abs(local.y) > 0.6f) continue;
                int tx = Mathf.Clamp(Mathf.FloorToInt((local.x / W + 0.5f) * 4f), 0, 3);
                int tz = Mathf.Clamp(Mathf.FloorToInt((local.z / D + 0.5f) * 4f), 0, 3);
                current = kv.Key + ":" + tx + ":" + tz;
                if (current != lastSoundTile) ProceduralMusic.PlayNote(p.Position, tx + tz * 2);
                break;
            }
            lastSoundTile = current;
        }
    }
}
