using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Rocket
{
    /// <summary>
    /// タブレットの「宇宙船診断」アプリ（ロケット仕様 §13）。
    /// 主人公の装置なので主人公の言語で表示する（§2.1）。宇宙語の辞書とは混ぜない（§13.3）。
    /// 宇宙船が知っていること（故障・必要な性能）と、主人公が星で知ったこと（素材の名前・入手の手がかり）を分ける（§13.2）。
    /// </summary>
    internal sealed class RocketDiagnosticsApp : ITabletApp
    {
        public const string Id = "rocket";
        private readonly RocketSystem sys;
        private readonly DiagnosticsPanel panel;

        public RocketDiagnosticsApp(RocketSystem sys)
        {
            this.sys = sys;
            panel = new DiagnosticsPanel(sys);
        }

        public string AppId => Id;
        public string Title => Loc.T("宇宙船診断", "Ship Diagnostics");
        public int Order => 30;
        public string IconGlyph => Loc.T("船", "SH");
        public Color IconColor => new Color(0.45f, 0.85f, 1f);
        public bool IsAvailable => true;

        public void OnOpen() => panel.OnOpen();

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            panel.Draw(area.width, area.height);
            GUILayout.EndArea();
        }
    }

    /// <summary>タブレットが無い構成でも診断を見られるようにするウィンドウ。</summary>
    internal sealed class DiagnosticsWindow : UIWindow
    {
        private readonly DiagnosticsPanel panel;

        public DiagnosticsWindow(RocketSystem sys)
        {
            panel = new DiagnosticsPanel(sys);
        }

        public override string Title => Loc.T("宇宙船診断", "Ship Diagnostics");
        public override Vector2 Size => new Vector2(1320, 880);
        public override bool PausesTimeInSolo => true;
        public override void OnOpen() => panel.OnOpen();
        public override void DrawContents(Rect content) => panel.Draw(content.width, content.height);
    }

    internal sealed class DiagnosticsPanel
    {
        private readonly RocketSystem sys;
        private string selected;
        private int tab;
        private Vector2 listScroll, detailScroll, historyScroll;

        /// <summary>完成した部品の取り付け位置（§13.1）。</summary>
        private static readonly Dictionary<string, string[]> Locations = new Dictionary<string, string[]>
        {
            { "R01", new[] { "船体前面の操作盤と窓の照明", "Front panel and cabin lights" } },
            { "R02", new[] { "船体の外装板（6か所）", "Hull plates (6 places)" } },
            { "R03", new[] { "背面を一周する冷却配管", "Coolant pipes around the back" } },
            { "R04", new[] { "側面の燃料タンクとエンジンへの燃料ライン", "Side fuel tank and the line to the engine" } },
            { "R05", new[] { "船底の主エンジン", "Main engine at the bottom" } },
            { "R06", new[] { "船首のアンテナ", "Antenna at the nose" } },
            { "R07", new[] { "胴体を囲む位相リング", "Phase ring around the hull" } },
            { "R08", new[] { "船全体", "The whole ship" } },
        };

        public DiagnosticsPanel(RocketSystem sys)
        {
            this.sys = sys;
        }

        public void OnOpen()
        {
            if (selected == null) selected = sys.Knowledge.lastSelectedDevice ?? "R01";
            Inspect(selected);
        }

        private void Inspect(string id)
        {
            var ds = sys.DS(id);
            if (ds != null && !ds.inspected) WorldAuthority.Submit(new RocketInspectCommand { deviceId = id });
        }

        public void Draw(float width, float height)
        {
            var st = sys.State;
            var player = Game.Instance != null ? Game.Instance.LocalPlayer : null;

            // ---- 全体の状態 ----
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("修理進捗 {0}/{1}", "Repairs {0}/{1}", sys.RepairedDeviceCount, sys.TotalDeviceCount), UIStyles.LabelBold, GUILayout.Width(220));
            var bar = GUILayoutUtility.GetRect(200, 20, GUILayout.Width(220), GUILayout.Height(20));
            UIStyles.ProgressBar(bar, sys.RepairProgress01, UIStyles.Good);
            GUILayout.Space(20);
            GUILayout.Label(Loc.Format("燃料 {0}/{1}", "Fuel {0}/{1}", st.fuelInTank, RocketTables.FuelRequiredForLaunch), UIStyles.LabelBold, GUILayout.Width(150));
            var fbar = GUILayoutUtility.GetRect(140, 20, GUILayout.Width(160), GUILayout.Height(20));
            UIStyles.ProgressBar(fbar, st.fuelInTank / (float)RocketTables.FuelRequiredForLaunch, new Color(1f, 0.72f, 0.2f));
            GUILayout.FlexibleSpace();
            string overall = st.hasLaunched ? Loc.T("出発中", "Departing")
                : st.launchReady ? RocketUI.Colored(Loc.T("全装置正常・帰還可能", "All systems nominal - return available"), UIStyles.Good)
                : Loc.T("航路データ：正常", "Route data: intact");
            GUILayout.Label(overall, UIStyles.LabelBold);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("装置", "Systems"), tab == 0 ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Width(160))) tab = 0;
            if (GUILayout.Button(Loc.T("修理記録", "Repair log"), tab == 1 ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Width(160))) tab = 1;
            if (sys.Knowledge.diagramSeen && GUILayout.Button(Loc.T("修理屋の図", "Mechanic's diagram"), UIStyles.Button, GUILayout.Width(200)))
                UIManager.Open(new RocketDiagramWindow(sys, null));
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            if (tab == 1) DrawHistory();
            else DrawSystems(player, width);
        }

        private void DrawSystems(PlayerState player, float width)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Min(320f, width * 0.3f)));
            listScroll = GUILayout.BeginScrollView(listScroll);
            foreach (var def in RocketTables.Devices)
            {
                var status = sys.GetStatus(def, player);
                string label = def.Name + "\n" + RocketUI.Colored(RocketTables.StatusLabel(status), RocketTables.StatusColor(status));
                var style = new GUIStyle(def.Id == selected ? UIStyles.ButtonSelected : UIStyles.Button) { alignment = TextAnchor.MiddleLeft, richText = true };
                if (GUILayout.Button(label, style, GUILayout.Height(60)))
                {
                    selected = def.Id;
                    sys.Knowledge.lastSelectedDevice = def.Id;
                    Inspect(def.Id);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll);
            var sel = RocketTables.Get(selected);
            if (sel != null) DrawDetail(sel, player);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawDetail(DeviceDef def, PlayerState player)
        {
            var ds = sys.DS(def.Id);
            var status = sys.GetStatus(def, player);
            GUILayout.BeginHorizontal();
            GUILayout.Label(def.Name, UIStyles.LabelTitle);
            GUILayout.Space(10);
            GUILayout.Label(RocketUI.Colored(RocketTables.StatusLabel(status), RocketTables.StatusColor(status)), UIStyles.LabelBold);
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("故障：", "Fault: ") + def.Broken, UIStyles.LabelWrap);
            GUILayout.Label(Loc.T("修理後：", "After repair: ") + def.Function, UIStyles.LabelWrap);
            if (Locations.TryGetValue(def.Id, out var loc)) GUILayout.Label(Loc.T("取り付け位置：", "Location: ") + Loc.T(loc[0], loc[1]), UIStyles.LabelDim);
            if (ds != null && ds.repaired && ds.repairedDay > 0)
                GUILayout.Label(Loc.Format("{0}日目に修理（{1}）", "Repaired on day {0} ({1})", ds.repairedDay, sys.PlayerName(ds.repairedBy)), UIStyles.LabelDim);
            GUILayout.Space(8);

            if (def.IsFinal)
            {
                GUILayout.Label(Loc.T("起動条件", "Startup conditions"), UIStyles.LabelBold);
                foreach (var d in RocketTables.Devices)
                {
                    if (d.IsFinal) continue;
                    GUILayout.Label(RocketUI.Check(sys.IsDeviceRepaired(d.Id)) + " " + d.Name, UIStyles.Label);
                }
                GUILayout.Label(RocketUI.Check(sys.State.fuelInTank >= RocketTables.FuelRequiredForLaunch) + " " +
                    Loc.Format("燃料 {0}/{1}", "Fuel {0}/{1}", sys.State.fuelInTank, RocketTables.FuelRequiredForLaunch), UIStyles.Label);
                GUILayout.Label(RocketUI.Check(true) + " " + Loc.T("航路データ（無事）", "Route data (intact)"), UIStyles.Label);
                GUILayout.Label(RocketUI.Check(sys.State.finalTestDone) + " " + Loc.T("最終起動試験", "Final startup test"), UIStyles.Label);
                DrawItemRequirement(SharedItemIds.FuelStarHoney, RocketTables.FuelRequiredForLaunch, sys.State.fuelInTank, player);
                return;
            }

            GUILayout.Label(Loc.T("必要なもの", "Requirements"), UIStyles.LabelBold);
            foreach (var r in def.Items) DrawItemRequirement(r.ItemId, r.Count, ds != null ? ds.Delivered(r.ItemId) : 0, player);

            if (def.LaborFee > 0)
            {
                int fee = sys.EffectiveFee(def);
                GUILayout.Label(RocketUI.Check(ds != null && ds.paid >= fee) + " " +
                    Loc.Format("作業費 {0} / {1}（修理屋への支払い）", "Labor {0} / {1} (paid to the mechanic)", UIStyles.Money(Mathf.Min(ds != null ? ds.paid : 0, fee)), UIStyles.Money(fee)), UIStyles.Label);
            }
            if (def.RequiresCreature)
            {
                var names = new List<string>();
                foreach (var a in def.RequiredAbilities) names.Add(RocketTables.AbilityLabel(a));
                bool ok = ds != null && sys.CreatureSatisfied(def, ds);
                GUILayout.Label(RocketUI.Check(ok) + " " + Loc.Format("揺らぎを整える生物の協力（能力：{0}）", "Help from a creature that can calm fluctuations (ability: {0})", string.Join(Loc.T("・", "/"), names)), UIStyles.Label);
                GUILayout.Label(Loc.T("　手がかり：生き物取りで能力を持つ生物を探し、作業場の生物スペースに連れてくる（消費しない）",
                    "  Hint: find such a creature, bring it to the workshop's creature space (it isn't consumed)"), UIStyles.LabelDim);
            }
            if (def.HelperAbilities != null && def.HelperAbilities.Length > 0)
            {
                var names = new List<string>();
                foreach (var a in def.HelperAbilities) names.Add(RocketTables.AbilityLabel(a));
                GUILayout.Label(Loc.Format("（任意）手伝える生物の能力：{0}　作業費が少し安くなる", "(Optional) Helper abilities: {0} - lowers the labor fee", string.Join(Loc.T("・", "/"), names)), UIStyles.LabelDim);
            }

            if (def.TestDependsOn.Length > 0 || def.TestNeedsFuel)
            {
                GUILayout.Space(6);
                GUILayout.Label(Loc.T("試運転の条件", "Test conditions"), UIStyles.LabelBold);
                foreach (var id in def.TestDependsOn)
                {
                    var d = RocketTables.Get(id);
                    if (d != null) GUILayout.Label(RocketUI.Check(sys.IsDeviceRepaired(id)) + " " + d.Name, UIStyles.Label);
                }
                if (def.TestNeedsFuel) GUILayout.Label(RocketUI.Check(sys.State.fuelInTank > 0) + " " + Loc.T("タンクの燃料", "Fuel in the tank"), UIStyles.Label);
            }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("※ 納品・取り付けは不時着地の修理台で行う。", "* Deliver and install at the repair bench by the crash site."), UIStyles.LabelDim);
        }

        private void DrawItemRequirement(string itemId, int need, int delivered, PlayerState player)
        {
            bool known = sys.IsDiscovered(itemId);
            var hint = RocketTables.Hint(itemId);
            int have = player != null ? player.Inventory.CountOf(itemId) : 0;
            GUILayout.BeginHorizontal(UIStyles.Box);
            RocketUI.ItemIconOrUnknown(sys, itemId, 56);
            GUILayout.BeginVertical();
            string title = known ? ItemRegistry.NameOf(itemId) : (hint != null ? hint.Property : "？");
            GUILayout.Label(RocketUI.Check(delivered >= need) + " " + title + Loc.Format("　{0}/{1}", "  {0}/{1}", Mathf.Min(delivered, need), need)
                + (known ? Loc.Format("　（持ち物 {0}）", "  (carrying {0})", have) : Loc.T("　未発見", "  not found yet")), UIStyles.Label);
            if (hint != null)
            {
                if (known) GUILayout.Label(Loc.T("性質：", "Property: ") + hint.Property, UIStyles.LabelDim);
                var src = new List<string>();
                if (hint.Sources != null) foreach (var s in hint.Sources) src.Add(RocketTables.SourceLabel(s));
                GUILayout.Label(Loc.T("手がかり：", "Hint: ") + hint.Where + (src.Count > 0 ? "［" + string.Join("・", src) + "］" : ""), UIStyles.LabelDim);
                if (!string.IsNullOrEmpty(hint.RawItemId))
                {
                    var raw = RocketTables.Hint(hint.RawItemId);
                    string rawName = sys.IsDiscovered(hint.RawItemId) ? ItemRegistry.NameOf(hint.RawItemId) : (raw != null ? raw.Property : "？");
                    GUILayout.Label(Loc.T("原料：", "Raw material: ") + rawName, UIStyles.LabelDim);
                }
            }
            DrawCraftHints(itemId);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        /// <summary>加工のヒント（料理・加工モジュールのレシピ）と修理屋への依頼。</summary>
        private void DrawCraftHints(string itemId)
        {
            var crafting = Game.Instance != null ? Game.Instance.Service<ICraftingService>() : null;
            string pid = Game.Instance != null ? Game.Instance.LocalPlayerId : null;
            if (crafting != null)
            {
                var ids = new List<string>();
                try { foreach (var r in crafting.RecipesProducing(itemId)) ids.Add(r); }
                catch (System.Exception e) { Debug.LogException(e); }
                foreach (var rid in ids)
                {
                    bool knownRecipe = crafting.IsRecipeKnown(pid, rid);
                    string name = knownRecipe ? crafting.GetRecipeName(rid) : Loc.T("（まだ知らない作り方）", "(an unknown recipe)");
                    GUILayout.Label(Loc.T("加工：", "Recipe: ") + name + (knownRecipe ? RocketUI.Colored(Loc.T("　作り方を知っている", "  known"), UIStyles.Good) : ""), UIStyles.LabelDim);
                }
            }
            foreach (var pr in RocketTables.ProxyRecipes)
            {
                if (pr.OutputItemId != itemId) continue;
                GUILayout.Label(Loc.Format("修理屋に加工を頼める（1回 {0}）", "The mechanic can process it ({0} each)", UIStyles.Money(pr.Fee)), UIStyles.LabelDim);
            }
            var bought = RocketTables.GetPurchased(itemId);
            if (bought != null)
                GUILayout.Label(Loc.Format("修理屋で買える（{0}）", "Sold by the mechanic ({0})", UIStyles.Money(bought.Price)), UIStyles.LabelDim);
        }

        private void DrawHistory()
        {
            historyScroll = GUILayout.BeginScrollView(historyScroll);
            var h = sys.State.history;
            if (h.Count == 0) GUILayout.Label(Loc.T("まだ記録はない。", "No records yet."), UIStyles.LabelDim);
            for (int i = h.Count - 1; i >= 0; i--)
            {
                var e = h[i];
                var def = RocketTables.Get(e.deviceId);
                string dev = def != null ? def.Name : "";
                string who = sys.PlayerName(e.playerId);
                string what;
                switch (e.kind)
                {
                    case "deliver": what = Loc.Format("{0}に {1}×{2} を納品", "Delivered {1} x{2} to {0}", dev, ItemRegistry.NameOf(e.itemId), e.count); break;
                    case "fuel": what = Loc.Format("燃料タンクに {0}×{1} を給油", "Added {0} x{1} to the fuel tank", ItemRegistry.NameOf(e.itemId), e.count); break;
                    case "pay": what = Loc.Format("{0}の作業費 {1} を支払い", "Paid {1} labor for {0}", dev, UIStyles.Money(e.count)); break;
                    case "creature":
                        var cs = Game.Instance?.Service<ICreatureService>();
                        string sp = cs != null ? cs.GetSpeciesName(e.itemId) : e.itemId;
                        what = Loc.Format("{0}を{1}が手伝った", "{1} helped with {0}", dev, sp);
                        break;
                    case "install": what = RocketUI.Colored(Loc.Format("{0}を修理した", "Repaired {0}", dev), UIStyles.Good); break;
                    case "test": what = Loc.Format("{0}：{1} 成功", "{0}: {1} passed", dev, def != null ? def.TestName : ""); break;
                    case "final": what = RocketUI.Colored(Loc.T("最終起動試験に成功・帰還可能", "Final startup test passed - return available"), UIStyles.Good); break;
                    case "launch": what = Loc.T("帰還を選んだ", "Chose to return home"); break;
                    case "return": what = Loc.T("ロケットの前に戻り、暮らしを続けた", "Came back to the rocket and kept living here"); break;
                    default: what = e.kind; break;
                }
                GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}　{3}　{4}", "Day {0} {1:00}:{2:00}  {3}  {4}", e.day, e.minute / 60, e.minute % 60, who, what), UIStyles.Label);
            }
            GUILayout.EndScrollView();
        }
    }
}
