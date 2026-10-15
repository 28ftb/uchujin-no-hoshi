using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// タブレットの「料理・加工記録」アプリ（AppId "recipes"、§12.2〜12.3）。
    /// 作った物・試した組み合わせ・結果・見せてもらった実演・仕込み中の物・食の記録。
    /// 自分で実際に行った手順と確認できた結果だけを記録する。料理の宇宙語名・住民の本心・未観察の手順は埋めない。
    /// 料理名・住民へのメモはプレイヤーの自由入力。
    /// </summary>
    public sealed class RecipeRecordApp : ITabletApp
    {
        private enum Page { Made, Demos, Tries, Timed, FoodLog }

        private readonly CookingSystem sys;
        private Page page = Page.Made;
        private string selectedRecipe;
        private Vector2 listScroll, detailScroll;

        public RecipeRecordApp(CookingSystem sys) { this.sys = sys; }

        public string AppId => "recipes";
        public string Title => Loc.T("料理・加工記録", "Recipe log");
        public int Order => 55;
        public string IconGlyph => Loc.T("料", "Rc");
        public Color IconColor => new Color(1f, 0.7f, 0.45f);
        public bool IsAvailable => true;

        private string Pid => Game.Instance != null ? Game.Instance.LocalPlayerId : null;

        public void OnOpen()
        {
            listScroll = Vector2.zero;
            detailScroll = Vector2.zero;
        }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            try
            {
                DrawInner(area);
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private void DrawInner(Rect area)
        {
            var k = sys.Knowledge(Pid);
            int made = 0, mystery = 0;
            foreach (var r in k.recipes) if (r.timesMade > 0) made++;
            var seenMystery = new HashSet<string>();
            foreach (var t in k.tries) if (t.mystery && seenMystery.Add(t.resultItemId)) mystery++;

            GUILayout.BeginHorizontal();
            PageButton(Page.Made, Loc.T("作った物", "Made"));
            PageButton(Page.Demos, Loc.T("見せてもらった", "Demos"));
            PageButton(Page.Tries, Loc.T("試した組み合わせ", "Tries"));
            PageButton(Page.Timed, Loc.T("仕込み中", "Brewing"));
            PageButton(Page.FoodLog, Loc.T("食の記録", "Food log"));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("その場で作る", "Craft by hand"), UIStyles.ButtonSmall, GUILayout.Height(36)))
            {
                var tablet = Game.Instance.Service<ITabletService>();
                tablet?.Close();
                sys.OpenStation(CraftStationType.Hand, "hand", Game.Instance.LocalPlayer);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.Format("この星で作った作り方 {0}／見つけた謎の結果 {1}", "Recipes made here: {0} / mystery results: {1}", made, mystery), UIStyles.LabelDim);

            switch (page)
            {
                case Page.Made: DrawMade(k, area); break;
                case Page.Demos: DrawDemos(k); break;
                case Page.Tries: DrawTries(k); break;
                case Page.Timed: DrawTimed(); break;
                case Page.FoodLog: DrawFoodLog(); break;
            }
        }

        private void PageButton(Page p, string label)
        {
            if (GUILayout.Button(label, page == p ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(36)))
            {
                page = p;
                listScroll = Vector2.zero;
            }
        }

        // ---- 作った物 ----
        private void DrawMade(CookingKnowledgeData k, Rect area)
        {
            float h = Mathf.Max(200f, area.height - 110f);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(Mathf.Min(360f, area.width * 0.38f)));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(h));
            bool any = false;
            foreach (var rec in k.recipes)
            {
                if (rec.timesMade <= 0 && rec.timesCommissioned <= 0) continue;
                any = true;
                var r = RecipeBook.Get(rec.recipeId);
                string outId = !string.IsNullOrEmpty(rec.lastOutputItemId) ? rec.lastOutputItemId : (r != null ? r.OutputItemId : null);
                GUILayout.BeginHorizontal();
                if (outId != null) CookingUI.ItemIcon(outId, 1, rec.bestQuality, 36f);
                string name = !string.IsNullOrEmpty(rec.customName) ? rec.customName : (r != null ? r.Name : rec.recipeId);
                if (GUILayout.Button(name, selectedRecipe == rec.recipeId ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(36)))
                {
                    selectedRecipe = rec.recipeId;
                    detailScroll = Vector2.zero;
                }
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("まだ何も作っていない。村の共用調理場で焼き魚から始めてみよう。", "Nothing yet. Try grilling a fish at the community kitchen."), CookingUI.SmallWrap);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(h));
            var sel = selectedRecipe != null ? sys.FindRecord(Pid, selectedRecipe) : null;
            if (sel != null) DrawRecordDetail(sel);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawRecordDetail(RecipeRecord rec)
        {
            var r = RecipeBook.Get(rec.recipeId);
            string outId = !string.IsNullOrEmpty(rec.lastOutputItemId) ? rec.lastOutputItemId : (r != null ? r.OutputItemId : null);
            var def = ItemRegistry.GetOrPlaceholder(outId);
            GUILayout.BeginHorizontal();
            CookingUI.ItemIcon(outId, 1, rec.bestQuality, 64f);
            GUILayout.BeginVertical();
            GUILayout.Label(!string.IsNullOrEmpty(rec.customName) ? rec.customName : def.Name, CookingUI.Header);
            if (!string.IsNullOrEmpty(rec.customName)) GUILayout.Label(Loc.Format("（{0}）", "({0})", def.Name), UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            if (GUILayout.Button(Loc.T("自分で名前を付ける", "Name it yourself"), UIStyles.ButtonSmall, GUILayout.Width(240)))
            {
                var target = rec;
                UIManager.InputText(Loc.T("料理の名前", "Dish name"), Loc.T("自分なりの呼び名を付けよう（宇宙語の名前とは別）。", "Give it your own name (separate from the alien name)."),
                    target.customName ?? "", s => target.customName = string.IsNullOrEmpty(s) ? null : s.Trim());
            }
            GUILayout.Label(Loc.Format("見た目・味：{0}", "Look & taste: {0}", CookingUI.FlavorText(def)), CookingUI.SmallWrap);
            GUILayout.Label(Loc.Format("自分で作った回数：{0}　頼んで作ってもらった回数：{1}", "Made: {0}   Commissioned: {1}", rec.timesMade, rec.timesCommissioned), UIStyles.LabelSmall);
            GUILayout.Label(Loc.Format("いちばん良い出来：{0}　はじめて：{1}日目", "Best: {0}   First: day {1}", CookingUI.QualityText(rec.bestQuality), rec.firstDay), UIStyles.LabelSmall);
            GUILayout.Label(Loc.Format("使った物：{0}", "Used: {0}", CraftEngine.ItemsText(rec.lastIngredients)), CookingUI.SmallWrap);
            GUILayout.Label(Loc.Format("自分で行った手順：{0}", "My steps: {0}", CraftEngine.StepsText(rec.lastSteps)), CookingUI.SmallWrap);
            if (rec.creatureSpecies.Count > 0)
            {
                var cs = Game.Instance.Service<ICreatureService>();
                var names = new List<string>();
                foreach (var sid in rec.creatureSpecies) names.Add(CraftEngine.SafeSpeciesName(cs, sid));
                GUILayout.Label(Loc.Format("手伝ってくれた生き物：{0}", "Helpers: {0}", string.Join(Loc.T("、", ", "), names.ToArray())), CookingUI.SmallWrap);
            }
            if (!string.IsNullOrEmpty(rec.weatherNote)) GUILayout.Label(Loc.Format("その時の天気：{0}", "Weather then: {0}", rec.weatherNote), UIStyles.LabelDim);
            var r2 = FoodEffects.EffectRecipeFor(outId);
            if (r2 != null && CookingTuning.FoodEffectsEnabled)
            {
                var eff = CookingTables.Effect(r2.FoodEffectId);
                if (eff != null) GUILayout.Label(Loc.Format("食べると：{0}", "When eaten: {0}", eff.Desc), UIStyles.LabelDim);
            }
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("メモ（住民の好みの予想など）", "Notes (guesses about who likes it, etc.)"), UIStyles.LabelBold);
            GUILayout.Label(string.IsNullOrEmpty(rec.note) ? Loc.T("（まだ何も書いていない）", "(nothing written yet)") : rec.note, CookingUI.SmallWrap);
            if (GUILayout.Button(Loc.T("メモを書く", "Write a note"), UIStyles.ButtonSmall, GUILayout.Width(200)))
            {
                var target = rec;
                UIManager.InputText(Loc.T("メモ", "Note"), Loc.T("例：丸い宇宙人が喜んでいた。熱い料理が好きかも。", "e.g. The round one looked happy. Likes hot food?"),
                    target.note ?? "", s => target.note = s, true);
            }
        }

        // ---- 見せてもらった ----
        private void DrawDemos(CookingKnowledgeData k)
        {
            listScroll = GUILayout.BeginScrollView(listScroll);
            if (k.demos.Count == 0) GUILayout.Label(Loc.T("料理人の住民に話しかけると、料理を見せてもらえることがある。", "Talk to a cook - they may show you how they cook."), CookingUI.SmallWrap);
            var speech = Game.Instance.Service<ISpeechService>();
            for (int i = k.demos.Count - 1; i >= 0; i--)
            {
                var d = k.demos[i];
                var r = RecipeBook.Get(d.recipeId);
                GUILayout.BeginHorizontal(UIStyles.Box);
                if (r != null) CookingUI.ItemIcon(r.OutputItemId, 1, 0, 48f);
                GUILayout.BeginVertical();
                string who = speech != null ? SafeName(speech, d.residentId) : d.residentId;
                GUILayout.Label(Loc.Format("{0}日目　{1}", "Day {0}  {1}", d.day, who), UIStyles.LabelSmall);
                GUILayout.Label(Loc.Format("見た材料：{0}", "Items: {0}", CraftEngine.ItemsText(d.ingredients)), CookingUI.SmallWrap);
                GUILayout.Label(Loc.Format("見た仕草：{0}", "Motions: {0}", CraftEngine.StepsText(d.steps)), CookingUI.SmallWrap);
                bool madeIt = r != null && sys.HasMade(Pid, r.Id);
                GUILayout.Label(madeIt ? Loc.T("→ 自分でも作れた", "-> I made it too") : Loc.T("→ まだ作ったことがない", "-> Not made yet"), UIStyles.LabelDim);
                GUILayout.EndVertical();
                if (speech != null && !string.IsNullOrEmpty(d.utteranceRecordId) && GUILayout.Button(Loc.T("声を聞き直す", "Replay"), UIStyles.ButtonSmall, GUILayout.Width(140)))
                {
                    try
                    {
                        var rec = speech.GetRecord(d.utteranceRecordId);
                        var p = Game.Instance.LocalPlayer;
                        if (rec != null && p != null && p.Avatar != null) speech.Replay(rec, p.Avatar);
                    }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private static string SafeName(ISpeechService speech, string residentId)
        {
            try { return speech.ResidentName(residentId); }
            catch { return "?"; }
        }

        // ---- 試した組み合わせ ----
        private void DrawTries(CookingKnowledgeData k)
        {
            listScroll = GUILayout.BeginScrollView(listScroll);
            if (k.tries.Count == 0) GUILayout.Label(Loc.T("まだ何も試していない。", "Nothing tried yet."), UIStyles.LabelDim);
            for (int i = k.tries.Count - 1; i >= 0; i--)
            {
                var t = k.tries[i];
                GUILayout.BeginHorizontal(UIStyles.Box);
                CookingUI.ItemIcon(t.resultItemId, 1, t.quality, 40f);
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("{0}日目：{1} → {2}", "Day {0}: {1} -> {2}", t.day, CraftEngine.ItemsText(t.ingredients), CraftEngine.StepsText(t.steps)), CookingUI.SmallWrap);
                var name = ItemRegistry.NameOf(t.resultItemId);
                GUILayout.Label(t.mystery ? Loc.Format("結果：{0}（思っていたのと違う）", "Result: {0} (unexpected)", name) : Loc.Format("結果：{0} {1}", "Result: {0} {1}", name, ItemStack.QualityStars(t.quality)), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---- 仕込み中 ----
        private void DrawTimed()
        {
            listScroll = GUILayout.BeginScrollView(listScroll);
            bool any = false;
            foreach (var s in sys.SlotsOwnedBy(Pid))
            {
                any = true;
                GUILayout.BeginHorizontal(UIStyles.Box);
                CookingUI.ItemIcon(s.outputItemId, s.count, s.quality, 44f);
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("{0} ×{1}（{2}）", "{0} x{1} ({2})", ItemRegistry.NameOf(s.outputItemId), s.count, Loc.T(s.stationNameJa, s.stationNameEn)), UIStyles.LabelSmall);
                var r = GUILayoutUtility.GetRect(300, 16, GUILayout.Width(300), GUILayout.Height(16));
                UIStyles.ProgressBar(r, s.Progress01, s.ready ? UIStyles.Good : UIStyles.AccentWarm);
                GUILayout.Label(s.ready ? Loc.T("できあがり！取りに行こう", "Ready! Go collect it") : Loc.Format("あと約{0}時間", "~{0}h left", Mathf.CeilToInt(Mathf.Max(0f, s.hoursRequired - s.progressHours))), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("仕込み中の物はない。", "Nothing brewing."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        // ---- 食の記録 ----
        private void DrawFoodLog()
        {
            var prof = sys.Profile(Pid);
            listScroll = GUILayout.BeginScrollView(listScroll);
            if (!CookingTuning.FoodLogEnabled)
            {
                GUILayout.Label(Loc.T("（食の記録は無効になっている）", "(Food log disabled)"), UIStyles.LabelDim);
                GUILayout.EndScrollView();
                return;
            }
            GUILayout.Label(Loc.Format("食べたことのある物：{0}種類", "Foods tasted: {0}", prof.foodLog.Count), UIStyles.LabelBold);
            GUILayout.Label(Loc.T("手に持って [F]／クリックで食べられる。", "Hold it and press [F] / click to eat."), UIStyles.LabelDim);
            foreach (var f in prof.foodLog)
            {
                var def = ItemRegistry.GetOrPlaceholder(f.itemId);
                GUILayout.BeginHorizontal(UIStyles.Box);
                CookingUI.ItemIcon(f.itemId, 1, f.bestQuality, 40f);
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("{0}　×{1}", "{0}  x{1}", def.Name, f.times), UIStyles.LabelSmall);
                string where = string.IsNullOrEmpty(f.planetName) ? "" : f.planetName + " ";
                GUILayout.Label(Loc.Format("はじめて食べた：{0}{1}日目　{2}", "First tasted: {0}day {1}  {2}", where, f.firstDay, CookingUI.FlavorText(def)), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }
}
