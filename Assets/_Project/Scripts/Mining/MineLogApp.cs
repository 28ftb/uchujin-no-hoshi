using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Mining
{
    /// <summary>
    /// タブレットの鉱山記録アプリ（AppId "minelog"）。到達階・見つけた鉱石／宝石／発見物・その日に見かけた鉱石（観察の事実）・腕前。
    /// </summary>
    public sealed class MineLogApp : ITabletApp
    {
        private readonly MiningSystem sys;
        private int tab;
        private Vector2 scroll;

        public MineLogApp(MiningSystem sys) { this.sys = sys; }

        public string AppId => "minelog";
        public string Title => Loc.T("鉱山の記録", "Mine Log");
        public int Order => 41;
        public string IconGlyph => Loc.T("鉱", "Mi");
        public Color IconColor => new Color(0.8f, 0.65f, 0.4f);
        public bool IsAvailable => true;
        public void OnOpen() { scroll = Vector2.zero; }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            string[] tabs = { Loc.T("階層", "Floors"), Loc.T("見つけた物", "Finds"), Loc.T("見かけた鉱石", "Seen ores"), Loc.T("腕前", "Skills") };
            for (int i = 0; i < tabs.Length; i++)
            {
                if (GUILayout.Button(tabs[i], i == tab ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(40)))
                {
                    tab = i;
                    scroll = Vector2.zero;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: DrawFloors(); break;
                case 1: DrawFinds(); break;
                case 2: DrawSeen(); break;
                default: DrawSkills(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawFloors()
        {
            var log = sys.MineLog;
            GUILayout.Label(Loc.Format("最深到達：地下{0}階 / 全{1}階", "Deepest: B{0}F of {1}", log.deepest, MiningTuning.FloorCount), UIStyles.LabelBold);
            GUILayout.Label(Loc.T("鉱石は毎日、日付が変わった瞬間に別の場所へ現れる。到達した階へは入口や各階の昇降機から直接行ける。",
                "Ore reappears elsewhere the moment the date changes. Use the elevators to go straight to floors you have reached."), UIStyles.LabelWrap);
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                bool reached = log.reachedFloors.Contains(f);
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(Loc.Format("地下{0}階", "B{0}F", f), UIStyles.LabelBold, GUILayout.Width(120));
                if (!reached)
                {
                    GUILayout.Label(Loc.T("まだ行っていない", "Not reached yet"), UIStyles.LabelDim);
                }
                else
                {
                    var layout = sys.GetLayout(f);
                    var notes = new List<string>();
                    notes.Add(Loc.Format("岩の硬さ：{0}以上", "Rock: {0}+", ToolTypeUtil.TierLabel(MiningTuning.RockHardness(f))));
                    if (layout != null && layout.HasLake) notes.Add(Loc.T("地下湖あり", "Underground lake"));
                    if (layout != null && layout.HasDeepHall) notes.Add(Loc.T("星の結晶の広間", "Star crystal hall"));
                    GUILayout.Label(string.Join(Loc.T("・", " / "), notes), UIStyles.Label);
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawFinds()
        {
            var log = sys.MineLog;
            if (log.entries.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ何も掘っていない。鉱山でツルハシを使ってみよう。", "Nothing mined yet. Use your pickaxe in the mine."), UIStyles.LabelWrap);
                return;
            }
            foreach (var e in log.entries)
            {
                var def = ItemRegistry.GetOrPlaceholder(e.itemId);
                GUILayout.BeginHorizontal(UIStyles.Box);
                var icon = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(icon, new ItemStack(e.itemId, 1, e.bestQuality));
                GUILayout.BeginVertical();
                GUILayout.Label(def.Name + Loc.Format("  ×{0}", "  x{0}", e.count), UIStyles.LabelBold);
                var floors = new List<string>();
                foreach (var f in e.floors) floors.Add(f.ToString());
                string extra = e.bestSize > 0f ? Loc.Format("　最大 {0:0.0}cm", "  max {0:0.0}cm", e.bestSize) : "";
                if (e.bestQuality > 0) extra += "　" + ItemStack.QualityStars(e.bestQuality);
                GUILayout.Label(Loc.Format("初めて：{0}日目（地下{1}階）　見つけた階：{2}", "First: day {0} (B{1}F)  Floors: {2}", e.firstDay, e.firstFloor, string.Join(",", floors)) + extra, UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSeen()
        {
            var log = sys.MineLog;
            GUILayout.Label(Loc.T("その日に各階で見かけた鉱石・宝石（行った時点の記録）。", "Ores and gems you noticed on each floor that day (as of your visit)."), UIStyles.LabelDim);
            if (log.sightings.Count == 0) GUILayout.Label(Loc.T("まだ記録はない。", "Nothing yet."), UIStyles.LabelDim);
            for (int i = log.sightings.Count - 1; i >= 0; i--)
            {
                var s = log.sightings[i];
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}日目・地下{1}階", "Day {0} B{1}F", s.day, s.floor), UIStyles.LabelBold, GUILayout.Width(200));
                var names = new List<string>();
                foreach (var id in s.itemIds) names.Add(ItemRegistry.NameOf(id));
                GUILayout.Label(names.Count > 0 ? string.Join(Loc.T("、", ", "), names) : Loc.T("岩ばかり", "Only rocks"), UIStyles.LabelWrap);
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSkills()
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            if (p == null) return;
            var prof = sys.GetProfile(p.PlayerId);
            int tier = Mathf.Max(1, p.Tools.GetLevel(ToolType.Pickaxe));
            GUILayout.Label(Loc.Format("ツルハシ：{0}（段階{1}）", "Pickaxe: {0} (tier {1})", ToolTypeUtil.TierLabel(tier), tier), UIStyles.LabelBold);
            GUILayout.Label(Loc.Format("採掘レベル {0}　経験 {1}　掘った数 {2}", "Mining Lv{0}  XP {1}  Mined {2}", prof.level, prof.xp, prof.totalMined), UIStyles.Label);
            if (prof.level < MiningTuning.MaxLevel)
                GUILayout.Label(Loc.Format("次のレベルまで {0}", "{0} XP to next level", MiningTuning.LevelXp[prof.level] - prof.xp), UIStyles.LabelDim);
            GUILayout.Label(Loc.T("ツルハシの段階で「何を掘れるか」、レベルで「どれだけ効率よく掘れるか・良い物が出やすいか」が変わる。",
                "Pickaxe tier decides what you can mine; level decides how efficiently, and how often you find better items."), UIStyles.LabelWrap);
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("掘れる物", "Minable"), UIStyles.LabelBold);
            foreach (var m in MineCatalog.All)
            {
                if (m.FromRockOnly || m.Kind == MineNodeKind.Forage) continue;
                bool can = tier >= m.Hardness;
                bool known = false;
                foreach (var e in sys.MineLog.entries) if (e.itemId == m.Id) known = true;
                string name = known ? ItemRegistry.NameOf(m.Id) : Loc.T("？？？", "???");
                GUILayout.Label((can ? "○ " : "× ") + name + Loc.Format("（{0}以上）", " ({0}+)", ToolTypeUtil.TierLabel(m.Hardness)), can ? UIStyles.Label : UIStyles.LabelDim);
            }
        }
    }
}
