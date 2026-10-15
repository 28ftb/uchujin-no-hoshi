using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Housing
{
    // =====================================================================
    // 家・家具の画面（IMGUI）：家具の詳細と思い出、収納、展示、増築、制作依頼、家の記録（タブレットアプリ）。
    // =====================================================================

    internal static class HousingUI
    {
        /// <summary>家具の思い出の一覧（§14）。声を聞き返す・音声カードに保存・印・メモ。</summary>
        public static void DrawMemories(HousingSystem sys, PlacedFurniture f, PlayerState player, ref Vector2 scroll, float height)
        {
            GUILayout.Label(Loc.Format("思い出（{0}）", "Memories ({0})", f.memories.Count), UIStyles.LabelBold);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            if (f.memories.Count == 0) GUILayout.Label(Loc.T("まだ何もない。これから作っていこう。", "Nothing yet. Memories will come."), UIStyles.LabelDim);
            var speech = Game.Instance?.Service<ISpeechService>();
            var tablet = Game.Instance?.Service<ITabletService>();
            for (int i = f.memories.Count - 1; i >= 0; i--)
            {
                var m = f.memories[i];
                int index = i;
                GUILayout.BeginHorizontal(UIStyles.Box);
                if (GUILayout.Button(m.pinned ? "★" : "☆", UIStyles.ButtonSmall, GUILayout.Width(40)))
                    WorldAuthority.Submit(new FurnitureNoteCommand { instanceId = f.instanceId, mode = FurnitureNoteCommand.ModePin, index = index, pinned = !m.pinned });
                GUILayout.Label((m.kind == "note" ? Loc.Format("［{0}日目のメモ］", "[Note, day {0}] ", m.day) : "") + m.Text, UIStyles.LabelWrap);
                if (!string.IsNullOrEmpty(m.recordId) && speech != null)
                {
                    var rec = speech.GetRecord(m.recordId);
                    if (rec != null)
                    {
                        if (GUILayout.Button(Loc.T("声を聞く", "Listen"), UIStyles.ButtonSmall, GUILayout.Width(100)))
                        {
                            var fo = sys.GetFurnitureObject(f.instanceId);
                            var at = fo != null ? fo.transform : player?.Avatar;
                            if (at != null) speech.Replay(rec, at);
                        }
                        if (tablet != null && GUILayout.Button(Loc.T("カードに保存", "Save card"), UIStyles.ButtonSmall, GUILayout.Width(120)))
                        {
                            var def = ItemRegistry.GetOrPlaceholder(f.itemId);
                            tablet.SaveVoiceCard(rec, def.NameJa + "の思い出", "Memory of the " + def.NameEn);
                            UIManager.Toast(Loc.T("音声カードに保存した", "Saved as a voice card"));
                        }
                    }
                }
                if (m.kind == "note" && GUILayout.Button(Loc.T("消す", "Erase"), UIStyles.ButtonSmall, GUILayout.Width(70)))
                    WorldAuthority.Submit(new FurnitureNoteCommand { instanceId = f.instanceId, mode = FurnitureNoteCommand.ModeRemove, index = index });
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(Loc.T("思い出のメモを書く", "Write a memory note"), UIStyles.ButtonSmall))
            {
                UIManager.InputText(Loc.T("メモを書く", "Write a note"), Loc.T("この家具の思い出を自由に残せます。", "Write anything you want to remember."), "",
                    text => WorldAuthority.Submit(new FurnitureNoteCommand { instanceId = f.instanceId, mode = FurnitureNoteCommand.ModeAddNote, text = text }), true);
            }
        }

        public static string LocationLabel(HousingSystem sys, PlacedFurniture f)
        {
            if (f.location == HousingConst.LocGarden) return Loc.T("庭", "Garden");
            if (f.location == HousingConst.LocStored) return Loc.T("しまってある", "Put away");
            return HousingSystem.RoomName(HouseGeometry.GetRoom(sys.State, f.room));
        }
    }

    /// <summary>家具を調べる（Shift+E）：詳細・思い出・使い方・生き物を住まわせる。</summary>
    public class FurnitureDetailWindow : UIWindow
    {
        private readonly HousingSystem sys;
        private readonly string instanceId;
        private readonly PlayerState player;
        private Vector2 memScroll, creatureScroll;
        private bool showCreatures;

        public FurnitureDetailWindow(HousingSystem sys, string instanceId, PlayerState player)
        {
            this.sys = sys;
            this.instanceId = instanceId;
            this.player = player;
        }

        public override string Title => ItemRegistry.NameOf(sys.FindFurniture(instanceId)?.itemId);
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(920, 760);

        public override void DrawContents(Rect content)
        {
            var f = sys.FindFurniture(instanceId);
            if (f == null || f.location == HousingConst.LocStored) { Close(); return; }
            var def = ItemRegistry.GetOrPlaceholder(f.itemId);
            var spec = FurnitureCatalog.Resolve(def);
            GUILayout.Label(def.Description, UIStyles.LabelWrap);
            GUILayout.Label(Loc.Format("シリーズ：{0}　場所：{1}　持ち主：{2}", "Series: {0}  Place: {1}  Owner: {2}",
                FurnitureCatalog.SeriesLabel(spec.series), HousingUI.LocationLabel(sys, f), sys.PlayerName(f.ownerId)), UIStyles.LabelDim);

            GUILayout.BeginHorizontal();
            var fo = sys.GetFurnitureObject(f.instanceId);
            if (fo != null && GUILayout.Button(Loc.T("使う", "Use"), UIStyles.ButtonSmall)) { Close(); sys.UseFurniture(fo, player); }
            if (spec.use == FurnitureUse.Light && GUILayout.Button(f.lightOn ? Loc.T("灯りを消す", "Light off") : Loc.T("灯りを点ける", "Light on"), UIStyles.ButtonSmall))
                WorldAuthority.Submit(new ToggleFurnitureLightCommand { instanceId = f.instanceId });
            if (spec.storageSlots > 0 && GUILayout.Button(Loc.T("収納を開く", "Open storage"), UIStyles.ButtonSmall)) { Close(); sys.OpenStorage(f, player); }
            if (spec.displaySlots > 0 && GUILayout.Button(Loc.T("飾る・並べる", "Display"), UIStyles.ButtonSmall)) { Close(); UIManager.Open(new DisplayWindow(sys, f.instanceId, player)); }
            if (spec.storageSlots > 0 && f.ownerId == player.PlayerId && WorldAuthority.IsMultiplayer)
                if (GUILayout.Button(f.privateStorage ? Loc.T("共有の収納にする", "Make shared") : Loc.T("自分専用の収納にする", "Make private"), UIStyles.ButtonSmall))
                    WorldAuthority.Submit(new SetStoragePrivateCommand { instanceId = f.instanceId, value = !f.privateStorage });
            if (spec.IsHabitat && Game.Instance.Service<ICreatureService>() != null && GUILayout.Button(Loc.T("生き物を住まわせる", "House a creature"), UIStyles.ButtonSmall)) showCreatures = !showCreatures;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(f.creatureUid))
            {
                var cs = Game.Instance.Service<ICreatureService>();
                var cv = cs?.Get(f.creatureUid);
                if (cv != null) GUILayout.Label(Loc.Format("住んでいる生き物：{0}", "Lives here: {0}", !string.IsNullOrEmpty(cv.Nickname) ? cv.Nickname : cs.GetSpeciesName(cv.SpeciesId)), UIStyles.Label);
            }
            if (showCreatures) DrawCreaturePicker(f, spec);

            GUILayout.Space(6);
            HousingUI.DrawMemories(sys, f, player, ref memScroll, showCreatures ? 220 : 380);
        }

        private void DrawCreaturePicker(PlacedFurniture f, FurnitureSpec spec)
        {
            var cs = Game.Instance.Service<ICreatureService>();
            if (cs == null) return;
            var owned = cs.GetOwned(player.PlayerId);
            creatureScroll = GUILayout.BeginScrollView(creatureScroll, GUILayout.Height(170));
            if (owned == null || owned.Count == 0) GUILayout.Label(Loc.T("一緒に暮らす生き物がいない。", "You have no creatures."), UIStyles.LabelDim);
            else
                foreach (var cv in owned)
                {
                    if (cv.Placement == CreaturePlacement.Deposited) continue;
                    var ab = cs.GetSpeciesAbilities(cv.SpeciesId);
                    bool suits = FurnitureCatalog.HabitatSuits(spec, cv.SpeciesId, ab);
                    string name = !string.IsNullOrEmpty(cv.Nickname) ? cv.Nickname : cs.GetSpeciesName(cv.SpeciesId);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label((suits ? "♥ " : "") + name, UIStyles.Label);
                    if (GUILayout.Button(Loc.T("ここに住まわせる", "Live here"), UIStyles.ButtonSmall, GUILayout.Width(200)))
                        HousingSystem.SubmitAssignCreature(cv.Uid, "habitat", f.instanceId);
                    GUILayout.EndHorizontal();
                }
            GUILayout.EndScrollView();
            GUILayout.Label(Loc.T("♥ はその生き物が好みそうな住処", "♥ marks a home the creature seems to like"), UIStyles.LabelDim);
        }
    }

    /// <summary>収納（家の共有収納・個人収納 §2.2・マルチ仕様 §16.4）と、届いた注文品。</summary>
    public class StorageWindow : UIWindow
    {
        private readonly HousingSystem sys;
        private readonly string instanceId;
        private readonly PlayerState player;
        private Vector2 leftScroll, rightScroll;

        public StorageWindow(HousingSystem sys, string instanceId, PlayerState player)
        {
            this.sys = sys;
            this.instanceId = instanceId;
            this.player = player;
        }

        public override string Title => ItemRegistry.NameOf(sys.FindFurniture(instanceId)?.itemId);
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(1100, 760);

        public override void DrawContents(Rect content)
        {
            var f = sys.FindFurniture(instanceId);
            if (f == null) { Close(); return; }
            var storage = sys.FindStorage(instanceId);
            GUILayout.Label(f.privateStorage ? Loc.T("（持ち主専用）", "(private)") : Loc.T("（家のみんなで使える）", "(shared with the household)"), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(520));
            GUILayout.Label(Loc.T("収納の中（クリックで取り出す）", "Inside (click to take)"), UIStyles.LabelBold);
            leftScroll = GUILayout.BeginScrollView(leftScroll, GUILayout.Height(420));
            if (storage != null)
                foreach (var s in storage.inventory.NonEmpty())
                {
                    var stack = s;
                    if (GUILayout.Button($"{stack.DisplayName} ×{stack.count}", UIStyles.ButtonSmall, GUILayout.Height(40)))
                        WorldAuthority.Submit(new StorageTransferCommand { instanceId = instanceId, put = false, itemId = stack.itemId, quality = stack.quality, count = stack.count, data = stack.data, size = stack.size });
                }
            GUILayout.EndScrollView();
            if (storage != null) GUILayout.Label(Loc.Format("空き {0}/{1}", "Free {0}/{1}", storage.inventory.FreeSlots(), storage.inventory.capacity), UIStyles.LabelDim);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(520));
            GUILayout.Label(Loc.T("持ち物（クリックでしまう）", "Carried (click to store)"), UIStyles.LabelBold);
            rightScroll = GUILayout.BeginScrollView(rightScroll, GUILayout.Height(420));
            foreach (var s in player.Inventory.NonEmpty())
            {
                var stack = s;
                if (GUILayout.Button($"{stack.DisplayName} ×{stack.count}", UIStyles.ButtonSmall, GUILayout.Height(40)))
                    WorldAuthority.Submit(new StorageTransferCommand { instanceId = instanceId, put = true, itemId = stack.itemId, quality = stack.quality, count = stack.count, data = stack.data, size = stack.size });
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            // 家具職人に作ってもらった物の受け取り
            var deliveries = sys.State.deliveries;
            if (deliveries.Count > 0)
            {
                GUILayout.Label(Loc.T("届いた物", "Deliveries"), UIStyles.LabelBold);
                for (int i = 0; i < deliveries.Count; i++)
                {
                    var d = deliveries[i];
                    int index = i;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(ItemRegistry.NameOf(d.itemId) + (string.IsNullOrEmpty(d.ownerId) || d.ownerId == player.PlayerId ? "" : Loc.Format("（{0}の注文）", " ({0}'s order)", sys.PlayerName(d.ownerId))), UIStyles.Label);
                    if (GUILayout.Button(Loc.T("受け取る", "Collect"), UIStyles.ButtonSmall, GUILayout.Width(140)))
                    {
                        string itemId = d.itemId;
                        string carpenter = d.data != null && d.data.StartsWith("commission:") ? d.data.Substring(11) : null;
                        WorldAuthority.Submit(new TakeDeliveryCommand { index = index }, r =>
                        {
                            if (r != null && r.Success) sys.AddProvenance(itemId, "commission", string.IsNullOrEmpty(carpenter) ? null : carpenter);
                        });
                    }
                    GUILayout.EndHorizontal();
                }
            }
        }
    }

    /// <summary>水槽・展示棚・食卓に飾る（§6、料理仕様 §10.3：食卓に料理を並べて訪問者と食事）。</summary>
    public class DisplayWindow : UIWindow
    {
        private readonly HousingSystem sys;
        private readonly string instanceId;
        private readonly PlayerState player;

        public DisplayWindow(HousingSystem sys, string instanceId, PlayerState player)
        {
            this.sys = sys;
            this.instanceId = instanceId;
            this.player = player;
        }

        public override string Title => ItemRegistry.NameOf(sys.FindFurniture(instanceId)?.itemId);
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(760, 600);

        public override void DrawContents(Rect content)
        {
            var f = sys.FindFurniture(instanceId);
            if (f == null) { Close(); return; }
            var spec = HouseGeometry.Resolve(f);
            string hint;
            switch (spec.displayKind)
            {
                case DisplayKind.Fish: hint = Loc.T("釣った魚を入れて眺められる。", "Put your catches in and watch them swim."); break;
                case DisplayKind.Mineral: hint = Loc.T("鉱石や宝石を飾れる。", "Display ores and gems."); break;
                case DisplayKind.Food: hint = Loc.T("料理を並べておくと、遊びに来た住民と一緒に食事ができる。", "Set out dishes to share a meal with visitors."); break;
                case DisplayKind.Plant: hint = Loc.T("花を飾れる。", "Put a flower in it."); break;
                default: hint = Loc.T("好きな物を飾れる。", "Display anything you like."); break;
            }
            GUILayout.Label(hint, UIStyles.LabelWrap);
            GUILayout.Label(Loc.Format("{0}/{1}", "{0}/{1}", f.displayed.Count, spec.displaySlots), UIStyles.LabelDim);
            for (int i = 0; i < f.displayed.Count; i++)
            {
                var s = f.displayed[i];
                int index = i;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var r = GUILayoutUtility.GetRect(44, 44, GUILayout.Width(44), GUILayout.Height(44));
                UIStyles.ItemIcon(r, s);
                GUILayout.Label(s.DisplayName + (s.size > 0 ? Loc.Format("（{0:0.0}）", " ({0:0.0})", s.size) : ""), UIStyles.Label);
                if (GUILayout.Button(Loc.T("外す", "Take"), UIStyles.ButtonSmall, GUILayout.Width(100)))
                    WorldAuthority.Submit(new DisplayItemCommand { instanceId = instanceId, put = false, index = index });
                GUILayout.EndHorizontal();
            }
            GUI.enabled = f.displayed.Count < spec.displaySlots;
            if (GUILayout.Button(Loc.T("持ち物から飾る", "Display from inventory"), UIStyles.Button, GUILayout.Height(46)))
            {
                UIManager.PickItem(player, Loc.T("飾る物を選ぶ", "Choose what to display"), s => HousingSystem.AcceptsDisplay(spec, s.Definition), (slot, s) =>
                    WorldAuthority.Submit(new DisplayItemCommand { instanceId = instanceId, put = true, itemId = s.itemId, quality = s.quality, data = s.data, size = s.size }));
            }
            GUI.enabled = true;
        }
    }

    /// <summary>家具職人に増築・改装を頼む（§3, §3.2：完成予定の間取りのプレビュー付き）。</summary>
    public class ConstructionWindow : UIWindow
    {
        private readonly HousingSystem sys;
        private readonly IResidentInfo carpenter;
        private readonly PlayerState player;
        private string previewKind;
        private int selectedRoom;

        public ConstructionWindow(HousingSystem sys, IResidentInfo carpenter, PlayerState player)
        {
            this.sys = sys;
            this.carpenter = carpenter;
            this.player = player;
        }

        public override string Title => Loc.T("増築・改装", "Building work");
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(1180, 820);

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.Format("所持金：{0}　家具職人は見本と素材の絵を指さして説明してくれる。", "Money: {0}  The carpenter explains by pointing at samples and pictures of materials.", UIStyles.Money(player.Wallet.money)), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(480));
            DrawPlan();
            DrawRoomPicker();
            DrawOrders();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(680));
            foreach (var o in ConstructionCatalog.Options) DrawOption(o);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawRoomPicker()
        {
            GUILayout.Label(Loc.T("広げる部屋：", "Room to enlarge:"), UIStyles.Label);
            GUILayout.BeginHorizontal();
            int n = 0;
            foreach (var r in sys.State.rooms)
            {
                if (GUILayout.Button(HousingSystem.RoomName(r), selectedRoom == r.index ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) selectedRoom = r.index;
                if (++n % 3 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawOrders()
        {
            GUILayout.Label(Loc.T("工事中・制作中", "In progress"), UIStyles.LabelBold);
            bool any = false;
            foreach (var o in sys.State.orders)
            {
                if (o.delivered) continue;
                any = true;
                var opt = o.kind == ConstructionKinds.Commission ? null : ConstructionCatalog.Get(o.kind);
                string label = opt != null ? opt.Label : ItemRegistry.NameOf(o.itemId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.Format("{0}（{1}日目に完成）", "{0} (ready on day {1})", label, o.readyDay), UIStyles.LabelSmall);
                if (o.ownerId == player.PlayerId && GUILayout.Button(Loc.T("取り消す", "Cancel"), UIStyles.ButtonSmall, GUILayout.Width(110)))
                {
                    var id = o.orderId;
                    UIManager.Confirm(Loc.T("取り消し", "Cancel"), Loc.T("注文を取り消して、お金と素材を返してもらいますか？", "Cancel and get your money and materials back?"),
                        () => WorldAuthority.Submit(new CancelConstructionCommand { orderId = id }));
                }
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("なし", "None"), UIStyles.LabelDim);
        }

        private void DrawOption(ConstructionOption o)
        {
            int room = o.perRoom ? selectedRoom : -1;
            bool available = sys.IsConstructionAvailable(o.kind, room, out var rja, out var ren);
            GUILayout.BeginVertical(UIStyles.Box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(o.Label + (o.perRoom ? Loc.Format("（{0}）", " ({0})", HousingSystem.RoomName(HouseGeometry.GetRoom(sys.State, selectedRoom))) : ""), UIStyles.LabelBold);
            GUILayout.FlexibleSpace();
            GUILayout.Label(UIStyles.Money(o.price) + Loc.Format("・{0}日", " / {0} days", o.days), UIStyles.Label);
            GUILayout.EndHorizontal();
            GUILayout.Label(o.Desc, UIStyles.LabelDim);
            bool enough = true;
            foreach (var m in o.materials)
            {
                int have = ConstructionCatalog.CountAvailable(player, m.Key);
                if (have < m.Value) enough = false;
                GUI.color = have >= m.Value ? UIStyles.Good : UIStyles.Bad;
                GUILayout.Label(Loc.Format("{0}：{1}/{2}", "{0}: {1}/{2}", ConstructionCatalog.GroupLabel(m.Key), have, m.Value), UIStyles.LabelSmall);
                GUI.color = Color.white;
            }
            if (!available) GUILayout.Label(Loc.T(rja, ren), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("完成予想を見る", "Preview"), UIStyles.ButtonSmall, GUILayout.Width(180))) previewKind = o.kind;
            GUI.enabled = available && enough && player.Wallet.CanAfford(o.price);
            if (GUILayout.Button(Loc.T("頼む", "Order"), UIStyles.Button, GUILayout.Width(140)))
            {
                var mats = new List<StringIntPair>();
                if (ConstructionCatalog.PickMaterials(player, o, mats))
                    WorldAuthority.Submit(new OrderConstructionCommand { kind = o.kind, room = room, materials = mats }, r =>
                    {
                        if (r != null && r.Success) UIManager.Toast(Loc.T("注文した。完成の日を楽しみに待とう。", "Ordered. Look forward to the day it's done."));
                    });
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        /// <summary>間取りの見取り図（上：ロフト、中：1階、下：地下）。完成予想を薄く重ねる。</summary>
        private void DrawPlan()
        {
            var rect = GUILayoutUtility.GetRect(460, 300, GUILayout.Width(460), GUILayout.Height(300));
            UIStyles.FillRect(rect, new Color(0.12f, 0.15f, 0.22f, 1f));
            var st = sys.State;
            float scale = 7f;
            float[] rowY = { rect.y + 110, rect.y + 10, rect.y + 210 };
            var rowX = new float[3];
            for (int i = 0; i < 3; i++) rowX[i] = rect.x + 10;
            foreach (var r in st.rooms)
            {
                int row = HouseGeometry.RowOf(r.kind);
                float w = r.widthCells * HousingConst.Cell * scale, d = Mathf.Min(r.depthCells * HousingConst.Cell * scale, 88f);
                if (previewKind == ConstructionKinds.ExpandWidth && r.index == selectedRoom)
                    UIStyles.FillRect(new Rect(rowX[row], rowY[row], w + HousingConst.ExpandStep * HousingConst.Cell * scale, d), new Color(0.5f, 1f, 0.6f, 0.35f));
                if (previewKind == ConstructionKinds.ExpandDepth && r.index == selectedRoom)
                    UIStyles.FillRect(new Rect(rowX[row], rowY[row], w, Mathf.Min(d + HousingConst.ExpandStep * HousingConst.Cell * scale, 95f)), new Color(0.5f, 1f, 0.6f, 0.35f));
                UIStyles.FillRect(new Rect(rowX[row], rowY[row], w - 2, d), RoomColor(r.kind));
                GUI.Label(new Rect(rowX[row] + 4, rowY[row] + 2, w, 24), HousingSystem.RoomName(r), UIStyles.LabelSmall);
                rowX[row] += w;
            }
            if (previewKind != null && previewKind != ConstructionKinds.ExpandWidth && previewKind != ConstructionKinds.ExpandDepth && previewKind != ConstructionKinds.GardenExpand)
            {
                var kind = previewKind == ConstructionKinds.AddLoft ? RoomKind.Loft : previewKind == ConstructionKinds.AddBasement ? RoomKind.Basement :
                           previewKind == ConstructionKinds.AddGlass ? RoomKind.Glass : previewKind == ConstructionKinds.AddWaterside ? RoomKind.Waterside :
                           previewKind == ConstructionKinds.AddTerrace ? RoomKind.Terrace : RoomKind.Normal;
                int row = HouseGeometry.RowOf(kind);
                float w = HousingConst.NewRoomWidth * HousingConst.Cell * scale, d = HousingConst.NewRoomDepth * HousingConst.Cell * scale;
                var c = RoomColor(kind);
                c.a = 0.45f;
                UIStyles.FillRect(new Rect(rowX[row], rowY[row], w, Mathf.Min(d, 88f)), c);
                GUI.Label(new Rect(rowX[row] + 4, rowY[row] + 2, w, 24), Loc.T("（新しい部屋）", "(new room)"), UIStyles.LabelSmall);
            }
            GUI.Label(new Rect(rect.x + 6, rect.yMax - 26, 450, 24), Loc.Format("庭：{0}×{1}m", "Garden: {0}x{1}m", st.gardenWidthCells * HousingConst.Cell, st.gardenDepthCells * HousingConst.Cell)
                + (previewKind == ConstructionKinds.GardenExpand ? Loc.T(" → もっと広く", " → bigger") : ""), UIStyles.LabelDim);
        }

        private static Color RoomColor(RoomKind k)
        {
            switch (k)
            {
                case RoomKind.Glass: return new Color(0.6f, 0.85f, 1f, 0.9f);
                case RoomKind.Waterside: return new Color(0.35f, 0.6f, 0.95f, 0.9f);
                case RoomKind.Terrace: return new Color(0.6f, 0.75f, 0.45f, 0.9f);
                case RoomKind.Loft: return new Color(0.85f, 0.7f, 0.5f, 0.9f);
                case RoomKind.Basement: return new Color(0.45f, 0.45f, 0.5f, 0.9f);
                default: return new Color(0.82f, 0.66f, 0.48f, 0.9f);
            }
        }
    }

    /// <summary>家具の制作依頼（§16.1：お金と素材を渡して作ってもらう）。</summary>
    public class CommissionWindow : UIWindow
    {
        private readonly HousingSystem sys;
        private readonly IResidentInfo carpenter;
        private readonly PlayerState player;
        private FurnitureSeries? filter;

        public CommissionWindow(HousingSystem sys, IResidentInfo carpenter, PlayerState player)
        {
            this.sys = sys;
            this.carpenter = carpenter;
            this.player = player;
        }

        public override string Title => Loc.T("家具を作ってもらう", "Commission furniture");
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(1000, 800);

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.Format("所持金：{0}　見本を指さして注文する。", "Money: {0}  Point at a sample to order.", UIStyles.Money(player.Wallet.money)), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("すべて", "All"), filter == null ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) filter = null;
            foreach (FurnitureSeries s in Enum.GetValues(typeof(FurnitureSeries)))
            {
                if (s == FurnitureSeries.Interior || s == FurnitureSeries.Basic) continue;
                if (GUILayout.Button(FurnitureCatalog.SeriesLabel(s), filter == s ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) filter = s;
            }
            GUILayout.EndHorizontal();
            int friendship = carpenter != null ? carpenter.GetFriendship(player.PlayerId) : 0;
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var def in FurnitureCatalog.OwnFurniture)
            {
                var spec = (FurnitureSpec)def.ModuleData;
                if (filter.HasValue && spec.series != filter.Value) continue;
                if (def.Id == FurnitureCatalog.StarterBedId) continue;
                var o = ConstructionCatalog.Commission(def);
                bool special = HousingSystem.IsSpecialDesign(spec);
                bool locked = special && friendship < HousingConst.SpecialDesignFriendship;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var r = GUILayoutUtility.GetRect(44, 44, GUILayout.Width(44), GUILayout.Height(44));
                UIStyles.ItemIcon(r, new ItemStack(def.Id, 1));
                GUILayout.BeginVertical();
                GUILayout.Label(def.Name + "　" + FurnitureCatalog.SeriesLabel(spec.series), UIStyles.Label);
                string mats = "";
                bool enough = true;
                foreach (var m in o.materials)
                {
                    int have = ConstructionCatalog.CountAvailable(player, m.Key);
                    if (have < m.Value) enough = false;
                    mats += ConstructionCatalog.GroupLabel(m.Key) + " " + have + "/" + m.Value + "  ";
                }
                GUILayout.Label(locked ? Loc.T("もっと親しくなると作ってくれそうだ。", "They might make this once you're closer.") : mats + Loc.Format("・{0}日", " / {0} days", o.days), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.Label(UIStyles.Money(o.price), UIStyles.LabelBold, GUILayout.Width(120));
                GUI.enabled = !locked && enough && player.Wallet.CanAfford(o.price);
                if (GUILayout.Button(Loc.T("頼む", "Order"), UIStyles.ButtonSmall, GUILayout.Width(100), GUILayout.Height(44)))
                {
                    var list = new List<StringIntPair>();
                    if (ConstructionCatalog.PickMaterials(player, o, list))
                        WorldAuthority.Submit(new OrderConstructionCommand { kind = ConstructionKinds.Commission, itemId = def.Id, room = -1, materials = list }, res =>
                        {
                            if (res != null && res.Success) UIManager.Toast(Loc.T("注文した。できあがったら家の収納箱に届く。", "Ordered. It will be delivered to your home chest."));
                        });
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }

    /// <summary>家の記録（タブレットのアプリ「家」と同じ内容。タブレットが無い場合の代わり）。</summary>
    public class HomeOverviewWindow : UIWindow
    {
        private readonly HomeRecordsView view;
        public HomeOverviewWindow(HousingSystem sys) { view = new HomeRecordsView(sys); }
        public override string Title => Loc.T("家の記録", "Home records");
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(1100, 800);
        public override void DrawContents(Rect content) => view.Draw(content);
    }

    /// <summary>タブレットのアプリ（AppId "house"）：家具の一覧と思い出、予定、生き物、設定。</summary>
    public class HomeTabletApp : ITabletApp
    {
        private readonly HousingSystem sys;
        private readonly HomeRecordsView view;
        public HomeTabletApp(HousingSystem sys) { this.sys = sys; view = new HomeRecordsView(sys); }
        public string AppId => HousingSystem.AppId;
        public string Title => Loc.T("家と思い出", "Home & memories");
        public int Order => 60;
        public string IconGlyph => "家";
        public Color IconColor => new Color(0.95f, 0.7f, 0.45f);
        public bool IsAvailable => sys.Game != null && sys.Game.State == GameState.Playing;
        public void OnOpen() { }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            try { view.Draw(new Rect(0, 0, area.width, area.height)); }
            finally { GUILayout.EndArea(); }
        }
    }

    public class HomeRecordsView
    {
        private readonly HousingSystem sys;
        private int tab;
        private Vector2 listScroll, memScroll, otherScroll;
        private string selected;

        public HomeRecordsView(HousingSystem sys) { this.sys = sys; }

        public void Draw(Rect area)
        {
            var player = Game.Instance?.LocalPlayer;
            if (player == null) return;
            string[] tabs = { Loc.T("家具と思い出", "Furniture"), Loc.T("予定", "Schedule"), Loc.T("生き物", "Creatures"), Loc.T("家", "House") };
            tab = GUILayout.Toolbar(tab, tabs, UIStyles.ButtonSmall, GUILayout.Height(40));
            switch (tab)
            {
                case 0: DrawFurniture(player, area); break;
                case 1: DrawSchedule(player); break;
                case 2: DrawCreatures(player); break;
                default: DrawHouse(player); break;
            }
        }

        private void DrawFurniture(PlayerState player, Rect area)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(300, area.width * 0.38f)));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(Mathf.Max(300, area.height - 70)));
            foreach (var f in sys.State.furniture)
            {
                if (f.location == HousingConst.LocStored && f.ownerId != player.PlayerId) continue;
                var def = ItemRegistry.GetOrPlaceholder(f.itemId);
                string label = def.Name + "\n" + HousingUI.LocationLabel(sys, f) + (f.memories.Count > 0 ? "  ♪" + f.memories.Count : "");
                if (GUILayout.Button(label, selected == f.instanceId ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(52))) selected = f.instanceId;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            var sel = sys.FindFurniture(selected);
            if (sel == null) GUILayout.Label(Loc.T("家具を選ぶと、思い出が見られます。", "Choose furniture to see its memories."), UIStyles.LabelDim);
            else
            {
                var def = ItemRegistry.GetOrPlaceholder(sel.itemId);
                GUILayout.Label(def.Name, UIStyles.LabelBold);
                GUILayout.Label(Loc.Format("{0}日目に手に入れた・{1}", "Got on day {0} - {1}", sel.acquiredDay, HousingUI.LocationLabel(sys, sel)), UIStyles.LabelDim);
                if (!string.IsNullOrEmpty(sel.giftFromResidentId)) GUILayout.Label(Loc.Format("贈り主：{0}", "From: {0}", sys.ResidentName(sel.giftFromResidentId)), UIStyles.Label);
                HousingUI.DrawMemories(sys, sel, player, ref memScroll, Mathf.Max(200, area.height - 230));
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawSchedule(PlayerState player)
        {
            otherScroll = GUILayout.BeginScrollView(otherScroll);
            GUILayout.Label(Loc.T("招待の約束", "Invitations"), UIStyles.LabelBold);
            bool any = false;
            foreach (var i in sys.State.invitations)
            {
                if (i.status == InvitationStatus.Cancelled) continue;
                any = true;
                string status;
                switch (i.status)
                {
                    case InvitationStatus.Planned: status = Loc.T("うなずいた（約束）", "nodded (planned)"); break;
                    case InvitationStatus.Arriving: status = Loc.T("向かっている", "on the way"); break;
                    case InvitationStatus.Inside: status = Loc.T("遊びに来ている", "visiting now"); break;
                    case InvitationStatus.Done: status = Loc.T("来てくれた", "came"); break;
                    case InvitationStatus.Missed: status = Loc.T("留守の時に来た", "came while you were out"); break;
                    case InvitationStatus.Declined: status = Loc.T("首を振った", "shook their head"); break;
                    default: status = ""; break;
                }
                GUILayout.Label(Loc.Format("{0}日目 {1}:00　{2}　{3}{4}", "Day {0} {1}:00  {2}  {3}{4}", i.day, i.hour, sys.ResidentName(i.residentId), status, i.spontaneous ? Loc.T("（ふらっと）", " (dropped by)") : ""), UIStyles.Label);
            }
            if (!any) GUILayout.Label(Loc.T("住民に話しかけて「家に招待する」で約束できる。", "Talk to a resident and choose 'Invite to your home'."), UIStyles.LabelDim);
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("家具職人への注文", "Carpenter orders"), UIStyles.LabelBold);
            foreach (var o in sys.State.orders)
            {
                var opt = o.kind == ConstructionKinds.Commission ? null : ConstructionCatalog.Get(o.kind);
                GUILayout.Label(Loc.Format("{0}（{1}日目に完成）", "{0} (ready day {1})", opt != null ? opt.Label : ItemRegistry.NameOf(o.itemId), o.readyDay), UIStyles.Label);
            }
            if (sys.State.deliveries.Count > 0) GUILayout.Label(Loc.Format("届いた物が{0}個あります（家の収納で受け取れる）", "{0} deliveries waiting (collect at a home storage)", sys.State.deliveries.Count), UIStyles.Label);
            GUILayout.EndScrollView();
        }

        private void DrawCreatures(PlayerState player)
        {
            var cs = Game.Instance.Service<ICreatureService>();
            if (cs == null) { GUILayout.Label(Loc.T("生き物の記録はまだない。", "No creature records."), UIStyles.LabelDim); return; }
            otherScroll = GUILayout.BeginScrollView(otherScroll);
            var owned = cs.GetOwned(player.PlayerId);
            if (owned == null || owned.Count == 0) GUILayout.Label(Loc.T("一緒に暮らす生き物はまだいない。", "No creatures live with you yet."), UIStyles.LabelDim);
            else
                foreach (var cv in owned)
                {
                    string name = !string.IsNullOrEmpty(cv.Nickname) ? cv.Nickname : cs.GetSpeciesName(cv.SpeciesId);
                    string where;
                    switch (cv.Placement)
                    {
                        case CreaturePlacement.Home: where = Loc.T("家の庭で暮らしている", "living in the home garden"); break;
                        case CreaturePlacement.Habitat: where = Loc.Format("{0}に住んでいる", "living in the {0}", ItemRegistry.NameOf(sys.FindFurniture(cv.PlacementTargetId)?.itemId)); break;
                        case CreaturePlacement.Companion: where = Loc.T("一緒に歩いている", "with you"); break;
                        case CreaturePlacement.Farm: where = Loc.T("農場", "on the farm"); break;
                        default: where = cv.Placement.ToString(); break;
                    }
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    GUILayout.Label(name + "　" + where, UIStyles.Label);
                    if (cv.Placement != CreaturePlacement.Home && cv.Placement != CreaturePlacement.Deposited && GUILayout.Button(Loc.T("家の庭で暮らす", "Live in the garden"), UIStyles.ButtonSmall, GUILayout.Width(180)))
                        HousingSystem.SubmitAssignCreature(cv.Uid, "home", null);
                    if ((cv.Placement == CreaturePlacement.Home || cv.Placement == CreaturePlacement.Habitat) && GUILayout.Button(Loc.T("連れて行く", "Take along"), UIStyles.ButtonSmall, GUILayout.Width(140)))
                        HousingSystem.SubmitAssignCreature(cv.Uid, "companion", null);
                    GUILayout.EndHorizontal();
                }
            GUILayout.Label(Loc.T("住処の家具を Shift+E で調べると、生き物を住まわせられる。", "Inspect a habitat with Shift+E to house a creature there."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        private void DrawHouse(PlayerState player)
        {
            var st = sys.State;
            int ground = HouseGeometry.CountRow(st, 0), loft = HouseGeometry.CountRow(st, 1), basement = HouseGeometry.CountRow(st, 2);
            GUILayout.Label(Loc.Format("部屋：1階{0}・ロフト{1}・地下{2}　庭：{3}×{4}m　座れる場所：{5}", "Rooms: ground {0}, loft {1}, basement {2}  Garden: {3}x{4}m  Seats: {5}",
                ground, loft, basement, st.gardenWidthCells * HousingConst.Cell, st.gardenDepthCells * HousingConst.Cell, sys.SeatCount()), UIStyles.Label);
            var culture = FurnitureCatalog.Culture;
            if (culture != null && culture.favored.Count > 0)
                GUILayout.Label(Loc.Format("この星でよく見かける家具：{0}・{1}", "Common furniture on this planet: {0}, {1}", FurnitureCatalog.SeriesLabel(culture.favored[0]), FurnitureCatalog.SeriesLabel(culture.favored[Mathf.Min(1, culture.favored.Count - 1)])), UIStyles.LabelDim);
            if (player.CurrentAreaId == HousingConst.LocHouse || sys.IsInGarden(player.Position, player.CurrentAreaId))
            {
                if (GUILayout.Button(Loc.T("模様替えを始める", "Start redecorating"), UIStyles.Button, GUILayout.Height(46)))
                {
                    Game.Instance.Service<ITabletService>()?.Close();
                    UIManager.CloseAll();
                    sys.StartPlacementMode(-1);
                }
            }
            if (!WorldAuthority.IsMultiplayer) return;
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("共同生活（マルチプレイ）", "Living together (multiplayer)"), UIStyles.LabelBold);
            bool owner = player.Permission >= PermissionLevel.Owner;
            GUI.enabled = owner;
            if (GUILayout.Button(st.guestsMayEnter ? Loc.T("ほかの人も家に入れる（変更）", "Others may enter (change)") : Loc.T("ほかの人は家に入れない（変更）", "Others may not enter (change)"), UIStyles.ButtonSmall))
                WorldAuthority.Submit(new SetGuestEntryCommand { value = !st.guestsMayEnter });
            var net = Game.Instance.Service<INetworkService>();
            foreach (var r in st.rooms)
            {
                if (r.index == 0) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(HousingSystem.RoomName(r) + "：" + (string.IsNullOrEmpty(r.ownerId) ? Loc.T("共有", "shared") : Loc.Format("{0}の個室", "{0}'s room", sys.PlayerName(r.ownerId))), UIStyles.Label);
                if (GUILayout.Button(Loc.T("共有にする", "Shared"), UIStyles.ButtonSmall, GUILayout.Width(130)))
                    WorldAuthority.Submit(new SetRoomOwnerCommand { room = r.index, ownerId = "" });
                if (net != null)
                    foreach (var pid in net.ConnectedPlayerIds)
                        if (GUILayout.Button(sys.PlayerName(pid), UIStyles.ButtonSmall, GUILayout.Width(130)))
                            WorldAuthority.Submit(new SetRoomOwnerCommand { room = r.index, ownerId = pid });
                GUILayout.EndHorizontal();
            }
            GUI.enabled = true;
        }
    }
}
