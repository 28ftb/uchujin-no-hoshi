using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Rocket
{
    /// <summary>ロケットUI共通の小物。</summary>
    internal static class RocketUI
    {
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
        public static string Colored(string text, Color c) => "<color=" + Hex(c) + ">" + text + "</color>";
        public static string Check(bool ok) => ok ? Colored("✔", UIStyles.Good) : Colored("✘", UIStyles.Bad);

        /// <summary>まだ見つけていない素材の「？」アイコン。</summary>
        public static void UnknownIcon(Rect r)
        {
            GUI.Box(r, GUIContent.none, UIStyles.Slot);
            var s = new GUIStyle(UIStyles.LabelBold) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
            s.normal.textColor = UIStyles.TextDim;
            GUI.Label(r, "?", s);
        }

        public static void ItemIconOrUnknown(RocketSystem sys, string itemId, float size)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            if (sys.IsDiscovered(itemId)) UIStyles.ItemIcon(r, new ItemStack(itemId, 1));
            else UnknownIcon(r);
        }

        public static string ItemLabel(RocketSystem sys, string itemId)
        {
            if (sys.IsDiscovered(itemId)) return ItemRegistry.NameOf(itemId);
            var h = RocketTables.Hint(itemId);
            return h != null ? Loc.Format("（{0}）", "({0})", h.Property) : "？";
        }

        public static void ShowResult(CommandResult r)
        {
            if (r != null && r.Success && !string.IsNullOrEmpty(r.MessageJa)) UIManager.Toast(r.Message);
        }

        public static string CreatureName(string uid)
        {
            var svc = Game.Instance?.Service<ICreatureService>();
            if (svc == null || string.IsNullOrEmpty(uid)) return null;
            var c = svc.Get(uid);
            if (c == null) return null;
            return !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : svc.GetSpeciesName(c.SpeciesId);
        }
    }

    // =====================================================================
    // 修理台（作業台）：装置ごとの必要物・持ち物から納品・支払い・生物の配置・取り付け・試運転・最終起動
    // ロケット仕様 §11・§10（順序は原則自由。材料を持っていれば納品できる）
    // =====================================================================
    internal sealed class RepairWindow : UIWindow
    {
        private readonly RocketSystem sys;
        private readonly PlayerState player;
        private string selected;
        private Vector2 listScroll;

        public RepairWindow(RocketSystem sys, PlayerState player, string deviceId)
        {
            this.sys = sys;
            this.player = player;
            selected = deviceId ?? sys.Knowledge.lastSelectedDevice ?? FirstUnfinished();
        }

        public override string Title => Loc.T("修理台", "Repair Bench");
        public override Vector2 Size => new Vector2(1280, 860);
        public override bool PausesTimeInSolo => true;

        private string FirstUnfinished()
        {
            foreach (var d in RocketTables.Devices)
                if (!sys.IsDeviceRepaired(d.Id)) return d.Id;
            return RocketTables.FinalDeviceId;
        }

        public void Select(string id)
        {
            if (RocketTables.Get(id) == null) return;
            selected = id;
            sys.Knowledge.lastSelectedDevice = id;
            var ds = sys.DS(id);
            if (ds != null && !ds.inspected) WorldAuthority.Submit(new RocketInspectCommand { deviceId = id });
        }

        public override void OnOpen()
        {
            if (selected != null) Select(selected);
        }

        public override void DrawContents(Rect content)
        {
            if (player == null) return;
            var st = sys.State;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("修理 {0}/{1}", "Repaired {0}/{1}", sys.CountRepaired(), RocketTables.RepairableCount), UIStyles.LabelBold, GUILayout.Width(200));
            GUILayout.Label(Loc.Format("燃料タンク {0}/{1}", "Fuel {0}/{1}", st.fuelInTank, RocketTables.FuelRequiredForLaunch), UIStyles.LabelBold, GUILayout.Width(260));
            GUILayout.Label(Loc.Format("所持金 {0}", "Money {0}", UIStyles.Money(player.Wallet.money)), UIStyles.LabelBold);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("診断を見る", "Diagnostics"), UIStyles.ButtonSmall, GUILayout.Width(150))) sys.OpenDiagnostics();
            if (GUILayout.Button(Loc.T("加工を頼む", "Processing"), UIStyles.ButtonSmall, GUILayout.Width(150))) sys.OpenOrders(sys.FindMechanic()?.Id);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            // ---- 装置の一覧 ----
            GUILayout.BeginVertical(GUILayout.Width(320));
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var def in RocketTables.Devices)
            {
                var status = sys.GetStatus(def, player);
                string label = def.Id + "  " + def.Name + "\n" + RocketUI.Colored(RocketTables.StatusLabel(status), RocketTables.StatusColor(status));
                var style = new GUIStyle(def.Id == selected ? UIStyles.ButtonSelected : UIStyles.Button) { alignment = TextAnchor.MiddleLeft, richText = true };
                if (GUILayout.Button(label, style, GUILayout.Height(64))) Select(def.Id);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // ---- 選んだ装置 ----
            GUILayout.BeginVertical();
            scroll = GUILayout.BeginScrollView(scroll);
            var sel = RocketTables.Get(selected);
            if (sel != null)
            {
                if (sel.IsFinal) DrawFinal(sel);
                else DrawDevice(sel);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawHeader(DeviceDef def)
        {
            var status = sys.GetStatus(def, player);
            GUILayout.BeginHorizontal();
            GUILayout.Label(def.Name, UIStyles.LabelTitle);
            GUILayout.Space(12);
            GUILayout.Label(RocketUI.Colored(RocketTables.StatusLabel(status), RocketTables.StatusColor(status)), UIStyles.LabelBold);
            GUILayout.EndHorizontal();
            GUILayout.Label(def.Broken, UIStyles.LabelWrap);
            GUILayout.Label(Loc.Format("修理すると：{0}", "When repaired: {0}", def.Function), UIStyles.LabelDim);
            GUILayout.Space(8);
        }

        private void DrawDevice(DeviceDef def)
        {
            DrawHeader(def);
            var ds = sys.DS(def.Id);
            bool repaired = ds.repaired;

            // 素材・部品
            GUILayout.Label(Loc.T("必要なもの", "Requirements"), UIStyles.LabelBold);
            foreach (var r in def.Items)
            {
                int got = ds.Delivered(r.ItemId);
                int have = player.Inventory.CountOf(r.ItemId);
                int remaining = Mathf.Max(0, r.Count - got);
                GUILayout.BeginHorizontal(UIStyles.Box);
                RocketUI.ItemIconOrUnknown(sys, r.ItemId, 56);
                GUILayout.BeginVertical();
                GUILayout.Label(RocketUI.ItemLabel(sys, r.ItemId), UIStyles.Label);
                GUILayout.Label(Loc.Format("納品 {0}/{1} ・ 持ち物 {2}", "Delivered {0}/{1} - carrying {2}", Mathf.Min(got, r.Count), r.Count, have), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(RocketUI.Check(got >= r.Count), UIStyles.LabelBold, GUILayout.Width(30));
                GUI.enabled = !repaired && remaining > 0 && have > 0;
                if (GUILayout.Button(Loc.T("納品", "Deliver"), UIStyles.Button, GUILayout.Width(110), GUILayout.Height(48)))
                    WorldAuthority.Submit(new RocketDeliverCommand { deviceId = def.Id, itemId = r.ItemId, count = Mathf.Min(have, remaining) }, RocketUI.ShowResult);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // 作業費
            int fee = sys.EffectiveFee(def);
            if (def.LaborFee > 0)
            {
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("作業費（修理屋へ） {0} / {1}", "Labor fee (to the mechanic) {0} / {1}", UIStyles.Money(Mathf.Min(ds.paid, fee)), UIStyles.Money(fee)), UIStyles.Label);
                if (fee < def.LaborFee) GUILayout.Label(Loc.T("手伝いの生物がいるので少し安くなっている", "Discounted thanks to a helper creature"), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(RocketUI.Check(ds.paid >= fee), UIStyles.LabelBold, GUILayout.Width(30));
                int pay = Mathf.Min(player.Wallet.money, fee - ds.paid);
                GUI.enabled = !repaired && pay > 0;
                if (GUILayout.Button(pay > 0 ? Loc.Format("{0} 払う", "Pay {0}", UIStyles.Money(pay)) : Loc.T("払う", "Pay"), UIStyles.Button, GUILayout.Width(180), GUILayout.Height(48)))
                    WorldAuthority.Submit(new RocketPayCommand { deviceId = def.Id, amount = pay }, RocketUI.ShowResult);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            // 生物（必須・手伝い）
            if (def.RequiresCreature) DrawCreatureRow(def, ds, true, repaired);
            if (def.HelperAbilities != null && def.HelperAbilities.Length > 0) DrawCreatureRow(def, ds, false, repaired);

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUI.enabled = !repaired;
            if (GUILayout.Button(Loc.T("持っている物をまとめて納品", "Deliver everything I have"), UIStyles.Button, GUILayout.Height(52)))
            {
                foreach (var r in def.Items)
                {
                    int n = Mathf.Min(player.Inventory.CountOf(r.ItemId), r.Count - ds.Delivered(r.ItemId));
                    if (n > 0) WorldAuthority.Submit(new RocketDeliverCommand { deviceId = def.Id, itemId = r.ItemId, count = n }, RocketUI.ShowResult);
                }
            }
            GUI.enabled = !repaired && sys.RequirementsMet(def);
            if (GUILayout.Button(Loc.T("取り付ける", "Install"), UIStyles.Button, GUILayout.Height(52)))
                WorldAuthority.Submit(new RocketInstallCommand { deviceId = def.Id }, r => { if (r != null && r.Success) UIManager.Close(this); });
            GUI.enabled = repaired;
            if (GUILayout.Button(Loc.Format("試運転：{0}", "Test: {0}", def.TestName), UIStyles.Button, GUILayout.Height(52)))
                WorldAuthority.Submit(new RocketTestCommand { deviceId = def.Id }, RocketUI.ShowResult);
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 試運転の条件（§10.2）
            if (def.TestDependsOn.Length > 0 || def.TestNeedsFuel)
            {
                var parts = new List<string>();
                foreach (var id in def.TestDependsOn)
                {
                    var d = RocketTables.Get(id);
                    if (d != null) parts.Add(RocketUI.Check(sys.IsDeviceRepaired(id)) + d.Name);
                }
                if (def.TestNeedsFuel) parts.Add(RocketUI.Check(sys.State.fuelInTank > 0) + Loc.T("タンクの燃料", "fuel in tank"));
                GUILayout.Label(Loc.T("試運転に必要：", "Test requires: ") + string.Join("  ", parts), UIStyles.LabelDim);
            }
            DrawContributors(ds);
        }

        private void DrawCreatureRow(DeviceDef def, DeviceState ds, bool required, bool repaired)
        {
            var abilities = required ? def.RequiredAbilities : def.HelperAbilities;
            var names = new List<string>();
            foreach (var a in abilities) names.Add(RocketTables.AbilityLabel(a));
            string uid = required ? ds.creatureUid : ds.helperUid;
            bool ok = required ? sys.CreatureSatisfied(def, ds) : sys.HelperActive(def, ds);
            bool noService = Game.Instance?.Service<ICreatureService>() == null;
            GUILayout.BeginHorizontal(UIStyles.Box);
            GUILayout.BeginVertical();
            GUILayout.Label((required ? Loc.T("生物の能力（必須）：", "Creature ability (required): ") : Loc.T("手伝いの生物（任意）：", "Helper creature (optional): "))
                + string.Join(Loc.T(" または ", " or "), names), UIStyles.Label);
            string who = RocketUI.CreatureName(uid);
            if (noService) GUILayout.Label(Loc.T("（生物の仕組みが無いため、この条件は省略されます）", "(Creature system unavailable; this requirement is skipped)"), UIStyles.LabelDim);
            else GUILayout.Label(ok && who != null ? Loc.Format("{0}が作業場で手伝っている（生物は消費しない）", "{0} is helping at the workshop (not consumed)", who)
                : Loc.T("生物の作業スペースに連れてきて配置する", "Bring one and place it in the creature workspace"), UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.Label(RocketUI.Check(ok), UIStyles.LabelBold, GUILayout.Width(30));
            GUI.enabled = !noService;
            if (GUILayout.Button(ok && repaired ? Loc.T("連れて帰る", "Take back") : Loc.T("生物を選ぶ", "Choose"), UIStyles.Button, GUILayout.Width(150), GUILayout.Height(48)))
            {
                if (ok && repaired) WorldAuthority.Submit(new RocketAssignCreatureCommand { deviceId = def.Id, creatureUid = "", asHelper = !required });
                else sys.ChooseCreatureFor(def, required, player);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawContributors(DeviceState ds)
        {
            if (ds.contributions.Count == 0) return;
            var parts = new List<string>();
            foreach (var c in ds.contributions) parts.Add(sys.PlayerName(c.key));
            GUILayout.Label(Loc.T("協力した人：", "Contributors: ") + string.Join(Loc.T("、", ", "), parts), UIStyles.LabelDim);
        }

        private void DrawFinal(DeviceDef def)
        {
            DrawHeader(def);
            var st = sys.State;
            GUILayout.Label(Loc.T("全装置の状態", "All systems"), UIStyles.LabelBold);
            foreach (var d in RocketTables.Devices)
            {
                if (d.IsFinal) continue;
                GUILayout.Label(RocketUI.Check(sys.IsDeviceRepaired(d.Id)) + " " + d.Name, UIStyles.Label);
            }
            GUILayout.Space(8);
            GUILayout.BeginHorizontal(UIStyles.Box);
            RocketUI.ItemIconOrUnknown(sys, SharedItemIds.FuelStarHoney, 56);
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.Format("燃料タンク：{0} {1}/{2}", "Fuel tank: {0} {1}/{2}", RocketUI.ItemLabel(sys, SharedItemIds.FuelStarHoney), st.fuelInTank, RocketTables.FuelRequiredForLaunch), UIStyles.Label);
            var bar = GUILayoutUtility.GetRect(300, 18, GUILayout.Width(360), GUILayout.Height(18));
            UIStyles.ProgressBar(bar, st.fuelInTank / (float)RocketTables.FuelRequiredForLaunch, new Color(1f, 0.72f, 0.2f));
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            int have = player.Inventory.CountOf(SharedItemIds.FuelStarHoney);
            int space = RocketTables.FuelRequiredForLaunch - st.fuelInTank;
            GUI.enabled = have > 0 && space > 0 && !st.hasLaunched;
            if (GUILayout.Button(Loc.T("燃料を入れる", "Refuel"), UIStyles.Button, GUILayout.Width(160), GUILayout.Height(48)))
                WorldAuthority.Submit(new RocketDeliverCommand { deviceId = RocketTables.TankTargetId, itemId = SharedItemIds.FuelStarHoney, count = Mathf.Min(have, space) }, RocketUI.ShowResult);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            if (st.launchReady)
            {
                GUILayout.Label(RocketUI.Colored(Loc.T("全装置正常・帰還可能。帰還は宇宙船の前でいつでも選べます。", "All systems nominal. You can choose to return at the ship any time."), UIStyles.Good), UIStyles.LabelBold);
                if (GUILayout.Button(Loc.T("もう一度 最終起動試験を見る", "Watch the final test again"), UIStyles.Button, GUILayout.Height(48)))
                    WorldAuthority.Submit(new RocketFinalTestCommand());
            }
            else
            {
                GUI.enabled = sys.RequirementsMet(def);
                if (GUILayout.Button(Loc.T("最終起動試験を行う", "Run the final startup test"), UIStyles.Button, GUILayout.Height(56)))
                    WorldAuthority.Submit(new RocketFinalTestCommand());
                GUI.enabled = true;
                GUILayout.Label(Loc.T("最終起動に成功しても、自動では出発しません。", "Even after a successful startup, the ship won't leave on its own."), UIStyles.LabelDim);
            }
            DrawContributors(sys.DS(def.Id));
        }
    }

    // =====================================================================
    // 部品置き場：納品済みの物・燃料・修理屋に頼んだ物の受け取り
    // =====================================================================
    internal sealed class PartsStorageWindow : UIWindow
    {
        private readonly RocketSystem sys;
        private readonly PlayerState player;

        public PartsStorageWindow(RocketSystem sys, PlayerState player)
        {
            this.sys = sys;
            this.player = player;
        }

        public override string Title => Loc.T("部品置き場", "Parts Storage");
        public override Vector2 Size => new Vector2(900, 720);

        public override void DrawContents(Rect content)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            var st = sys.State;
            int today = Game.Instance != null && Game.Instance.Time != null ? Game.Instance.Time.Day : 0;

            GUILayout.Label(Loc.T("修理屋に頼んだ物", "Your orders"), UIStyles.LabelBold);
            bool any = false;
            foreach (var o in st.orders)
            {
                if (o.playerId != player.PlayerId) continue;
                var r = RocketTables.GetProxy(o.recipeId);
                if (r == null) continue;
                any = true;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var ir = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(ir, new ItemStack(r.OutputItemId, r.OutputCount * o.count));
                GUILayout.Label(ItemRegistry.NameOf(r.OutputItemId) + " ×" + (r.OutputCount * o.count), UIStyles.Label);
                GUILayout.FlexibleSpace();
                GUILayout.Label(o.readyDay <= today ? RocketUI.Colored(Loc.T("出来上がり", "Ready"), UIStyles.Good) : Loc.Format("{0}日目に出来上がり", "Ready on day {0}", o.readyDay), UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("頼んでいる物はない。", "No orders."), UIStyles.LabelDim);
            GUI.enabled = sys.HasReadyOrders(player.PlayerId);
            if (GUILayout.Button(Loc.T("出来上がった物を受け取る", "Collect ready items"), UIStyles.Button, GUILayout.Height(48))) sys.CollectOrders();
            GUI.enabled = true;

            GUILayout.Space(12);
            GUILayout.Label(Loc.Format("燃料タンク {0}/{1}", "Fuel tank {0}/{1}", st.fuelInTank, RocketTables.FuelRequiredForLaunch), UIStyles.LabelBold);
            int have = player.Inventory.CountOf(SharedItemIds.FuelStarHoney);
            int space = RocketTables.FuelRequiredForLaunch - st.fuelInTank;
            GUI.enabled = have > 0 && space > 0;
            if (GUILayout.Button(Loc.Format("燃料を入れる（持ち物 {0}）", "Refuel (carrying {0})", have), UIStyles.Button, GUILayout.Height(48)))
                WorldAuthority.Submit(new RocketDeliverCommand { deviceId = RocketTables.TankTargetId, itemId = SharedItemIds.FuelStarHoney, count = Mathf.Min(have, space) }, RocketUI.ShowResult);
            GUI.enabled = true;

            GUILayout.Space(12);
            GUILayout.Label(Loc.T("取り付けを待っている部品", "Parts waiting to be installed"), UIStyles.LabelBold);
            bool anyStored = false;
            foreach (var def in RocketTables.Devices)
            {
                if (def.IsFinal) continue;
                var ds = sys.DS(def.Id);
                if (ds == null || ds.repaired) continue;
                foreach (var r in def.Items)
                {
                    int n = ds.Delivered(r.ItemId);
                    if (n <= 0) continue;
                    anyStored = true;
                    GUILayout.BeginHorizontal();
                    var ir = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                    UIStyles.ItemIcon(ir, new ItemStack(r.ItemId, n));
                    GUILayout.Label(Loc.Format("{0} ×{1}（{2}用）", "{0} x{1} (for {2})", ItemRegistry.NameOf(r.ItemId), n, def.Name), UIStyles.Label);
                    GUILayout.EndHorizontal();
                }
            }
            if (!anyStored) GUILayout.Label(Loc.T("まだ何も置かれていない。", "Nothing here yet."), UIStyles.LabelDim);
            GUILayout.Space(8);
            if (GUILayout.Button(Loc.T("修理台を開く", "Open the repair bench"), UIStyles.Button, GUILayout.Height(44)))
            {
                Close();
                sys.OpenRepairWindow(null);
            }
            GUILayout.EndScrollView();
        }
    }

    // =====================================================================
    // 修理屋への加工依頼（§9.3）：原料＋お金 → 翌朝以降に部品を受け取る
    // =====================================================================
    internal sealed class MechanicOrderWindow : UIWindow
    {
        private readonly RocketSystem sys;
        private readonly PlayerState player;
        private readonly string mechanicId;
        private readonly Dictionary<string, int> amounts = new Dictionary<string, int>();

        public MechanicOrderWindow(RocketSystem sys, PlayerState player, string mechanicId)
        {
            this.sys = sys;
            this.player = player;
            this.mechanicId = mechanicId;
        }

        public override string Title => Loc.T("修理屋に加工を頼む", "Ask the Mechanic to Process");
        public override Vector2 Size => new Vector2(1000, 760);

        public override void DrawContents(Rect content)
        {
            if (player == null) return;
            GUILayout.Label(Loc.Format("所持金：{0}", "Money: {0}", UIStyles.Money(player.Wallet.money)), UIStyles.LabelBold);
            GUILayout.Label(Loc.T("原料とお金を預けると、修理屋が部品に加工してくれる。自分で加工するより少し高い。", "Leave raw materials and money; the mechanic turns them into parts. A bit pricier than doing it yourself."), UIStyles.LabelDim);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var r in RocketTables.ProxyRecipes)
            {
                if (!amounts.ContainsKey(r.Id)) amounts[r.Id] = 1;
                int have = player.Inventory.CountOf(r.InputItemId);
                GUILayout.BeginHorizontal(UIStyles.Box);
                RocketUI.ItemIconOrUnknown(sys, r.InputItemId, 52);
                GUILayout.Label("×" + r.InputCount, UIStyles.LabelBold, GUILayout.Width(44));
                GUILayout.Label("→", UIStyles.LabelBold, GUILayout.Width(30));
                RocketUI.ItemIconOrUnknown(sys, r.OutputItemId, 52);
                GUILayout.BeginVertical();
                GUILayout.Label(RocketUI.ItemLabel(sys, r.InputItemId) + " → " + RocketUI.ItemLabel(sys, r.OutputItemId), UIStyles.Label);
                GUILayout.Label(Loc.Format("1回 {0} ・ {1}日 ・ 持ち物 {2}", "{0} each - {1} day(s) - carrying {2}", UIStyles.Money(r.Fee), r.Days, have), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                int amount = amounts[r.Id];
                if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(36))) amount = Mathf.Max(1, amount - 1);
                GUILayout.Label(amount.ToString(), UIStyles.LabelCenter, GUILayout.Width(36));
                if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(36))) amount = Mathf.Min(20, amount + 1);
                amounts[r.Id] = amount;
                GUI.enabled = have >= r.InputCount * amount && player.Wallet.CanAfford(r.Fee * amount);
                var recipe = r;
                int times = amount;
                if (GUILayout.Button(Loc.T("頼む", "Order"), UIStyles.Button, GUILayout.Width(100), GUILayout.Height(48)))
                {
                    WorldAuthority.Submit(new RocketOrderCommand { recipeId = recipe.Id, count = times }, res =>
                    {
                        if (res == null || !res.Success) return;
                        UIManager.Toast(Loc.Format("{0}を頼んだ（{1}日後）", "Ordered {0} (in {1} day(s))", ItemRegistry.NameOf(recipe.OutputItemId), recipe.Days));
                        if (!string.IsNullOrEmpty(mechanicId))
                            Game.Instance?.Service<IResidentDirectory>()?.Speak(mechanicId, RocketSpeech.MechanicOrderAccepted(mechanicId, recipe.OutputItemId, recipe.OutputCount * times, recipe.Days));
                    });
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            GUI.enabled = sys.HasReadyOrders(player.PlayerId);
            if (GUILayout.Button(Loc.T("出来上がった物を受け取る", "Collect ready items"), UIStyles.Button, GUILayout.Height(48))) sys.CollectOrders();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }
    }

    // =====================================================================
    // 主人公の宇宙船の表示（§2.1：主人公の言語で表示してよい）
    // =====================================================================
    internal sealed class ShipMessageWindow : UIWindow
    {
        private readonly string main;
        private readonly string sub;

        public ShipMessageWindow(string main, string sub)
        {
            this.main = main;
            this.sub = sub;
        }

        public override string Title => Loc.T("宇宙船の表示", "Ship Display");
        public override Vector2 Size => new Vector2(980, 440);

        public override void DrawContents(Rect content)
        {
            GUILayout.FlexibleSpace();
            var s = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 34 };
            s.normal.textColor = new Color(0.55f, 1f, 0.75f);
            GUILayout.Label(main, s);
            GUILayout.Space(16);
            var d = new GUIStyle(UIStyles.LabelWrap) { alignment = TextAnchor.MiddleCenter };
            GUILayout.Label(sub, d);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", UIStyles.Button, GUILayout.Height(50))) Close();
        }
    }

    /// <summary>最終起動試験の計器表示（画面上部に1行ずつ出る）。</summary>
    internal sealed class ShipDisplayOverlay : IUIOverlay
    {
        private readonly List<KeyValuePair<string, bool>> lines = new List<KeyValuePair<string, bool>>();
        private string title;
        private bool active;
        private float fadeOutAt = -1f;

        public int OverlayOrder => 220;

        public void Begin(string t)
        {
            title = t;
            lines.Clear();
            active = true;
            fadeOutAt = -1f;
        }

        public void AddLine(string text, bool highlight = false) => lines.Add(new KeyValuePair<string, bool>(text, highlight));

        public void End() => fadeOutAt = UnityEngine.Time.unscaledTime + 1.5f;

        public void DrawOverlay()
        {
            if (!active) return;
            float alpha = 1f;
            if (fadeOutAt > 0f)
            {
                alpha = Mathf.Clamp01((fadeOutAt - UnityEngine.Time.unscaledTime) / 1.5f);
                if (alpha <= 0f)
                {
                    active = false;
                    return;
                }
            }
            float w = 760f, lh = 34f;
            float h = 70f + lines.Count * lh;
            var r = new Rect((UIStyles.VirtualWidth - w) * 0.5f, 60f, w, h);
            UIStyles.FillRect(r, new Color(0.02f, 0.06f, 0.05f, 0.85f * alpha));
            UIStyles.FillRect(new Rect(r.x, r.y, r.width, 3f), new Color(0.4f, 1f, 0.7f, alpha));
            var ts = new GUIStyle(UIStyles.LabelBold) { fontSize = 24, alignment = TextAnchor.MiddleCenter };
            ts.normal.textColor = new Color(0.5f, 1f, 0.75f, alpha);
            GUI.Label(new Rect(r.x, r.y + 10f, r.width, 36f), title, ts);
            var ls = new GUIStyle(UIStyles.Label) { fontSize = 21 };
            for (int i = 0; i < lines.Count; i++)
            {
                ls.normal.textColor = lines[i].Value ? new Color(1f, 1f, 0.6f, alpha) : new Color(0.6f, 1f, 0.8f, alpha);
                ls.fontStyle = lines[i].Value ? FontStyle.Bold : FontStyle.Normal;
                GUI.Label(new Rect(r.x + 30f, r.y + 54f + i * lh, r.width - 60f, lh), "> " + lines[i].Key, ls);
            }
        }
    }
}
