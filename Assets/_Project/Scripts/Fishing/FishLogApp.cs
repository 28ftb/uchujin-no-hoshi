using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Fishing
{
    /// <summary>
    /// タブレットの釣果記録アプリ（AppId "fishlog"。釣り仕様 §8）。
    /// 実際に釣った魚・最大サイズ・場所・時刻・天気、見かけた魚影など「観察した事実」だけを表示する。
    /// 住民が魚を呼ぶ宇宙語は記録しない（辞書はプレイヤーが書く）。宇宙語辞書の正答集にはしない。
    /// </summary>
    public sealed class FishLogApp : ITabletApp
    {
        private readonly FishingSystem sys;
        private int tab;
        private Vector2 scroll;
        private string selected;

        public FishLogApp(FishingSystem sys) { this.sys = sys; }

        public string AppId => "fishlog";
        public string Title => Loc.T("釣果帳", "Fish Log");
        public int Order => 40;
        public string IconGlyph => Loc.T("魚", "Fi");
        public Color IconColor => new Color(0.4f, 0.75f, 1f);
        public bool IsAvailable => true;
        public void OnOpen() { scroll = Vector2.zero; }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            string[] tabs = { Loc.T("釣った魚", "Caught"), Loc.T("魚影の記録", "Sightings"), Loc.T("腕前", "Skills"), Loc.T("村の記録", "Village") };
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
                case 0: DrawCaught(); break;
                case 1: DrawSightings(); break;
                case 2: DrawSkills(); break;
                default: DrawVillage(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawCaught()
        {
            var entries = sys.FishLog.entries;
            int known = 0;
            foreach (var s in FishCatalog.All) if (!s.IsLegend) known++;
            GUILayout.Label(Loc.Format("釣った種類：{0}（伝説の魚を除く{1}種類がいるらしい）", "Species caught: {0} (about {1} kinds besides legends)", entries.Count, known), UIStyles.LabelDim);
            if (entries.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ何も釣っていない。釣り竿を装備して水辺で使ってみよう。", "Nothing caught yet. Equip the rod and use it at the water."), UIStyles.LabelWrap);
                return;
            }
            foreach (var e in entries)
            {
                var s = FishCatalog.Get(e.itemId);
                if (s == null) continue;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var icon = GUILayoutUtility.GetRect(52, 52, GUILayout.Width(52), GUILayout.Height(52));
                UIStyles.ItemIcon(icon, new ItemStack(e.itemId, 1, e.bestQuality));
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.T(s.NameJa, s.NameEn) + (s.IsLegend ? Loc.T("（伝説）", " (legend)") : "") + "  " + Loc.Format("×{0}", "x{0}", e.count), UIStyles.LabelBold);
                GUILayout.Label(Loc.Format("最大 {0:0.0}cm（{1}日目）　最高品質 {2}", "Max {0:0.0}cm (day {1})  Best {2}", e.maxSize, e.maxSizeDay,
                    e.bestQuality > 0 ? ItemStack.QualityStars(e.bestQuality) : "-"), UIStyles.LabelDim);
                GUILayout.EndVertical();
                if (GUILayout.Button(selected == e.itemId ? "▲" : "▼", UIStyles.ButtonSmall, GUILayout.Width(44))) selected = selected == e.itemId ? null : e.itemId;
                GUILayout.EndHorizontal();
                if (selected == e.itemId) DrawDetail(e, s);
            }
        }

        private void DrawDetail(FishLogEntry e, FishSpecies s)
        {
            GUILayout.BeginVertical(UIStyles.Box);
            GUILayout.Label(Loc.T(s.DescJa, s.DescEn), UIStyles.LabelWrap);
            var places = new List<string>();
            foreach (var id in e.spots) places.Add(SpotLabel(id));
            GUILayout.Label(Loc.T("釣れた場所：", "Caught at: ") + string.Join(Loc.T("、", ", "), places), UIStyles.LabelWrap);
            var parts = new List<string>();
            for (int i = 0; i < 6; i++) if ((e.dayPartMask & (1 << i)) != 0) parts.Add(DayPartUtil.Label((DayPart)i));
            GUILayout.Label(Loc.T("釣れた時間帯：", "Times: ") + string.Join(Loc.T("、", ", "), parts), UIStyles.LabelWrap);
            var weathers = new List<string>();
            foreach (var w in e.weatherTags) weathers.Add(FishingSystem.WeatherTagLabel(w));
            GUILayout.Label(Loc.T("その時の天気：", "Weather then: ") + string.Join(Loc.T("、", ", "), weathers), UIStyles.LabelWrap);
            GUILayout.Label(Loc.Format("最後に釣った日：{0}日目 {1:00}:{2:00}（{3}）", "Last caught: day {0} {1:00}:{2:00} ({3})",
                e.lastDay, e.lastMinute / 60, e.lastMinute % 60, e.lastWeather.Summary()), UIStyles.LabelDim);
            GUILayout.EndVertical();
        }

        private string SpotLabel(string spotId)
        {
            var spot = sys.Spawn.GetSpot(spotId);
            if (spot != null) return spot.Label();
            return Loc.T("名もない水辺", "Unnamed waters");
        }

        private void DrawSightings()
        {
            var list = sys.FishLog.sightings;
            GUILayout.Label(Loc.T("水辺で見かけた、普段と違う魚影の記録。", "Unusual fish shadows you have noticed by the water."), UIStyles.LabelDim);
            if (list.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ記録はない。時間や天気を変えて水辺を眺めてみよう。", "None yet. Watch the water at different times and weather."), UIStyles.LabelWrap);
                return;
            }
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var s = list[i];
                GUILayout.BeginHorizontal(UIStyles.Box);
                var sw = GUILayoutUtility.GetRect(24, 24, GUILayout.Width(24), GUILayout.Height(24));
                UIStyles.FillRect(sw, s.kind == "shadow" ? new Color(0.1f, 0.12f, 0.2f) : s.color);
                GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}", "Day {0} {1:00}:{2:00}", s.day, s.minute / 60, s.minute % 60), UIStyles.Label, GUILayout.Width(180));
                GUILayout.Label(SpotLabel(s.spotId), UIStyles.Label, GUILayout.Width(260));
                GUILayout.Label(KindLabel(s.kind) + "／" + s.weather.Summary(), UIStyles.LabelDim);
                GUILayout.EndHorizontal();
            }
        }

        private static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "frost": return Loc.T("霜のようなきらめき", "frosty glints");
                case "ripple": return Loc.T("風のない波紋", "windless ripples");
                case "glow": return Loc.T("光る魚影", "glowing shadow");
                default: return Loc.T("大きな魚影", "big shadow");
            }
        }

        private void DrawSkills()
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            if (p == null) return;
            var prof = sys.GetProfile(p.PlayerId);
            int tier = Mathf.Max(1, p.Tools.GetLevel(ToolType.FishingRod));
            GUILayout.Label(Loc.Format("釣りレベル {0}　経験 {1}", "Fishing Lv{0}  XP {1}", prof.level, prof.xp), UIStyles.LabelBold);
            int next = FishingTuning.XpToNext(prof.level);
            if (next > 0) GUILayout.Label(Loc.Format("次のレベルまで {0}", "{0} XP to next level", next - prof.xp), UIStyles.LabelDim);
            GUILayout.Label(Loc.Format("釣り竿：{0}（段階{1}）　これまでに釣った数：{2}", "Rod: {0} (tier {1})  Total caught: {2}", ToolTypeUtil.TierLabel(tier), tier, prof.totalCaught), UIStyles.Label);
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("覚えた技", "Techniques"), UIStyles.LabelBold);
            bool any = false;
            foreach (var t in TechniqueInfo.All)
            {
                if (!prof.Knows(t)) continue;
                any = true;
                GUILayout.Label("・" + TechniqueInfo.Name(t), UIStyles.Label);
                GUILayout.Label("　" + TechniqueInfo.Effect(t), UIStyles.LabelDim);
            }
            if (!any) GUILayout.Label(Loc.T("まだ無い。釣り名人の実演を見せてもらおう。", "None yet. Ask a fishing master for a demonstration."), UIStyles.LabelWrap);
        }

        private void DrawVillage()
        {
            var recs = new List<VillageFishRecord>(sys.PlanetData.records);
            recs.Sort((a, b) => b.size.CompareTo(a.size));
            GUILayout.Label(Loc.T("この星の釣果ボードの写し。", "A copy of this planet's catch board."), UIStyles.LabelDim);
            foreach (var r in recs)
            {
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(ItemRegistry.NameOf(r.itemId), UIStyles.Label, GUILayout.Width(300));
                GUILayout.Label(string.Format("{0:0.0}cm", r.size), UIStyles.LabelBold, GUILayout.Width(110));
                GUILayout.Label(r.playerName, UIStyles.LabelDim);
                GUILayout.EndHorizontal();
            }
        }
    }
}
