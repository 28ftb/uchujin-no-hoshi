using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 調理・加工の画面（CraftStationType ごと。どこから開かれても動く）。
    /// タブ：作り方（知っているレシピ・見せてもらった料理）／実験（自由に材料と手順を組み合わせる）／
    ///       仕込み（時間の加工）／職人に頼む／生き物（器具に住んでもらう・作業場で手伝ってもらう）。
    /// </summary>
    public sealed class StationWindow : UIWindow
    {
        private enum Tab { Recipes, Experiment, Timed, Commission, Creatures }

        private readonly CookingSystem sys;
        private readonly CraftStationType openedType;
        private readonly string instanceId;
        private StationContext ctx;
        private Tab tab = Tab.Recipes;
        private RecipeDef selected;
        private int batch = 1;
        private bool preferBest = true;
        private bool needRefresh;
        private float refreshTimer;
        private readonly List<RecipeDef> visible = new List<RecipeDef>();
        private readonly List<ChosenIngredient> expIngredients = new List<ChosenIngredient>();
        private readonly List<CraftStep> expSteps = new List<CraftStep>();
        private Vector2 listScroll, detailScroll, invScroll, chosenScroll, tabScroll;

        public StationWindow(CookingSystem sys, StationContext ctx)
        {
            this.sys = sys;
            this.ctx = ctx;
            openedType = ctx.Type;
            instanceId = ctx.InstanceId;
            if (ctx.Device != null && ctx.Device.TimedStep.HasValue && ctx.Device.Station == CraftStationType.Kitchen) tab = Tab.Timed;
            RebuildList();
        }

        public override string Title => ctx.Title;
        public override Vector2 Size => new Vector2(Mathf.Min(1320f, UIStyles.VirtualWidth - 40f), 880f);
        public override bool PausesTimeInSolo => true;

        public void MarkRefresh() => needRefresh = true;

        public override void Update()
        {
            refreshTimer += UnityEngine.Time.unscaledDeltaTime;
            if (needRefresh || refreshTimer > 2f)
            {
                needRefresh = false;
                refreshTimer = 0f;
                try
                {
                    ctx = CraftEngine.BuildContext(openedType, instanceId, ctx.Player);
                }
                catch (System.Exception e) { Debug.LogException(e); }
                RebuildList();
            }
        }

        private string Pid => ctx.Player != null ? ctx.Player.PlayerId : null;

        private void RebuildList()
        {
            visible.Clear();
            foreach (var r in RecipeBook.All)
            {
                if (!r.AllowsStation(ctx.Type)) continue;
                bool known = sys.IsKnown(Pid, r);
                bool hinted = sys.FindDemo(Pid, r.Id) != null;
                if (!known && !hinted) continue;
                visible.Add(r);
            }
            visible.Sort((a, b) =>
            {
                int ka = (int)a.Kind, kb = (int)b.Kind;
                if (ka != kb) return ka.CompareTo(kb);
                return string.CompareOrdinal(a.Id, b.Id);
            });
            if (selected != null && !visible.Contains(selected)) selected = null;
            if (selected == null && visible.Count > 0) selected = visible[0];
        }

        // =================================================================

        public override void DrawContents(Rect content)
        {
            DrawHeader();
            DrawTabs();
            GUILayout.Space(4);
            switch (tab)
            {
                case Tab.Recipes: DrawRecipes(); break;
                case Tab.Experiment: DrawExperiment(); break;
                case Tab.Timed: DrawTimed(); break;
                case Tab.Commission: DrawCommission(); break;
                case Tab.Creatures: DrawCreatures(); break;
            }
        }

        private void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("使える力：", "Available:"), UIStyles.LabelSmall, GUILayout.Width(110));
            // 設備・手作業でできる工程
            foreach (var s in ctx.Profile.BuiltIn) CookingUI.StepIcon(s, 28f);
            GUILayout.Space(10);
            // 生物の能力（同じ能力は1つにまとめる）
            var shown = new HashSet<CreatureAbility>();
            int n = 0;
            foreach (var src in ctx.Sources)
            {
                if (!shown.Add(src.Ability)) continue;
                CookingUI.AbilityIcon(src.Ability, 28f);
                if (++n >= 14) break;
            }
            if (ctx.Sources.Count == 0) GUILayout.Label(Loc.T("（いっしょに料理する生き物はいない）", "(no creatures helping)"), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            if (ctx.Outdoor) GUILayout.Label(ctx.Weather.Summary(), UIStyles.LabelDim);
            GUILayout.Label(Loc.Format("まとめて最大 {0}", "Batch up to {0}", ctx.BatchMax), UIStyles.LabelDim);
            GUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            TabButton(Tab.Recipes, Loc.T("作り方", "Recipes"));
            TabButton(Tab.Experiment, Loc.T("実験", "Experiment"));
            if (HasTimed()) TabButton(Tab.Timed, Loc.T("仕込み", "Timed"));
            if (ctx.Profile.CommissionJob != ResidentJob.None) TabButton(Tab.Commission, Loc.T("職人に頼む", "Commission"));
            if (HasCreatureTab()) TabButton(Tab.Creatures, Loc.T("生き物", "Creatures"));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.Format("所持金 {0}", "Money {0}", UIStyles.Money(ctx.Player.Wallet.money)), UIStyles.LabelSmall);
            GUILayout.EndHorizontal();
        }

        private void TabButton(Tab t, string label)
        {
            if (GUILayout.Button(label, tab == t ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(40), GUILayout.MinWidth(130)))
            {
                tab = t;
                GUIUtility.keyboardControl = 0;
            }
        }

        private bool HasTimed() => ctx.TimedTargets.Count > 0 || ctx.OwnTimed != null;

        private bool HasCreatureTab()
        {
            return ctx.LinkedDevices.Count > 0 || ctx.Profile.AcceptsWorkshopCreatures || ctx.Type == CraftStationType.CommunityKitchen
                   || ctx.Type == CraftStationType.Hand || ctx.Type == CraftStationType.CreatureDevice;
        }

        // =================================================================
        // 作り方
        // =================================================================

        private void DrawRecipes()
        {
            GUILayout.BeginHorizontal();
            // 左：一覧
            GUILayout.BeginVertical(GUILayout.Width(380));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(700));
            if (visible.Count == 0)
                GUILayout.Label(Loc.T("ここで作れる作り方をまだ知らない。「実験」で試してみよう。", "You don't know any recipes for here yet. Try experimenting."), CookingUI.SmallWrap);
            RecipeKind? lastKind = null;
            foreach (var r in visible)
            {
                if (lastKind != r.Kind)
                {
                    lastKind = r.Kind;
                    GUILayout.Label(KindLabel(r.Kind), UIStyles.LabelDim);
                }
                bool known = sys.IsKnown(Pid, r);
                var plan = CraftEngine.Plan(ctx, r);
                string mark = !plan.Feasible ? "×" : (CraftEngine.AutoSelect(ctx.Player, r, 1, false, null) == null ? "…" : "○");
                string name = sys.GetRecipeName(r.Id);
                if (!known) name = Loc.Format("？{0}（見せてもらった）", "?{0} (seen)", name);
                GUILayout.BeginHorizontal();
                CookingUI.ItemIcon(r.OutputItemId, 1, 0, 40f);
                if (GUILayout.Button(mark + " " + name, selected == r ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(40)))
                {
                    selected = r;
                    batch = 1;
                }
                GUILayout.EndHorizontal();
            }
            int unknown = 0;
            foreach (var r in RecipeBook.All) if (r.AllowsStation(ctx.Type) && !sys.IsKnown(Pid, r) && r.Experimentable) unknown++;
            if (unknown > 0) GUILayout.Label(Loc.T("まだ知らない作り方がありそうだ…", "There seem to be more recipes to discover..."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 右：詳細
            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(700));
            if (selected != null) DrawRecipeDetail(selected);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static string KindLabel(RecipeKind k)
        {
            switch (k)
            {
                case RecipeKind.Dish: return Loc.T("― 料理 ―", "- Dishes -");
                case RecipeKind.Processed: return Loc.T("― 加工食品 ―", "- Processed food -");
                case RecipeKind.Material: return Loc.T("― 素材 ―", "- Materials -");
                case RecipeKind.RocketPart: return Loc.T("― ロケットの部品 ―", "- Rocket parts -");
                case RecipeKind.Device: return Loc.T("― 調理器具 ―", "- Kitchen devices -");
                default: return Loc.T("― 思いがけない物 ―", "- Happy accidents -");
            }
        }

        private void DrawRecipeDetail(RecipeDef r)
        {
            var def = ItemRegistry.GetOrPlaceholder(r.OutputItemId);
            bool known = sys.IsKnown(Pid, r);
            bool made = sys.HasMade(Pid, r.Id);
            var plan = CraftEngine.Plan(ctx, r);
            var rec = sys.FindRecord(Pid, r.Id);

            GUILayout.BeginHorizontal();
            CookingUI.ItemIcon(r.OutputItemId, r.OutputCount, 0, 72f);
            GUILayout.BeginVertical();
            GUILayout.Label(sys.GetRecipeName(r.Id) + (r.OutputCount > 1 ? " ×" + r.OutputCount : ""), CookingUI.Header);
            GUILayout.Label(def.Description, CookingUI.SmallWrap);
            string flavor = CookingUI.FlavorText(def);
            if (!string.IsNullOrEmpty(flavor)) GUILayout.Label(flavor, UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            if (!known)
            {
                var demo = sys.FindDemo(Pid, r.Id);
                GUILayout.Label(Loc.T("住民が作るところを見せてくれた。同じように作れるだろうか？", "A resident showed you how they make it. Can you do the same?"), CookingUI.SmallWrap);
                if (demo != null) GUILayout.Label(Loc.Format("見た材料：{0}", "Seen ingredients: {0}", CraftEngine.ItemsText(demo.ingredients)), CookingUI.SmallWrap);
            }
            else if (rec != null && rec.timesMade > 0)
            {
                GUILayout.Label(Loc.Format("作った回数 {0}／いちばん良い出来 {1}", "Made {0} times / best {1}", rec.timesMade, CookingUI.QualityText(rec.bestQuality)), UIStyles.LabelDim);
            }
            else if (sys.IsCarried(Pid, r.Id) && !made)
            {
                GUILayout.Label(Loc.T("前の星で覚えた作り方。", "A recipe you learned on another planet."), UIStyles.LabelDim);
            }
            if (r.RequiredPhenomenon != AlienPhenomenon.None)
                GUILayout.Label(Loc.T("※ 特別な天気の日だけの作り方", "* Only on special weather"), UIStyles.LabelDim);
            if (!string.IsNullOrEmpty(r.NoteJa)) GUILayout.Label(Loc.T(r.NoteJa, r.NoteEn), UIStyles.LabelDim);

            // 材料
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("材料", "Ingredients"), UIStyles.LabelBold);
            var have = new int[r.Ingredients.Count];
            var takes = CraftEngine.AutoSelect(ctx.Player, r, Mathf.Max(1, batch), preferBest, have);
            for (int i = 0; i < r.Ingredients.Count; i++)
            {
                var spec = r.Ingredients[i];
                int need = spec.Count * Mathf.Max(1, batch);
                GUILayout.BeginHorizontal();
                var sample = spec.FindSample();
                if (sample != null) CookingUI.ItemIcon(sample, 1, 0, 36f);
                var st = new GUIStyle(UIStyles.LabelSmall);
                st.normal.textColor = have[i] >= need ? UIStyles.Good : UIStyles.Bad;
                GUILayout.Label(Loc.Format("{0} ×{1}（持っている {2}）", "{0} x{1} (have {2})", CookingUI.IngredientLabel(spec), need, have[i]), st);
                GUILayout.EndHorizontal();
            }

            // 手順
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("手順", "Steps"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                if (i > 0) GUILayout.Label("→", UIStyles.LabelBold, GUILayout.Width(22));
                CookingUI.StepPlanBlock(plan.Steps[i], Pid, 150f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (plan.IsTimed)
                GUILayout.Label(Loc.Format("時間がかかる：約{0}時間（{1}に仕込む。その間ほかのことをしていてよい）", "Takes ~{0}h (left at {1}; go do other things)",
                    Mathf.CeilToInt(plan.TimedHours), Loc.T(plan.TimedNameJa, plan.TimedNameEn)), CookingUI.SmallWrap);
            if (!plan.WeatherOk) GUILayout.Label(Loc.T("今の天気ではできない。", "Not possible in this weather."), UIStyles.LabelDim);

            // 操作
            GUILayout.Space(10);
            bool canMake = plan.Feasible && takes != null;
            GUILayout.BeginHorizontal();
            preferBest = GUILayout.Toggle(preferBest, Loc.T(" 良い材料から使う", " Use best ingredients first"), UIStyles.Toggle, GUILayout.Width(260));
            if (ctx.Profile.UseFee > 0) GUILayout.Label(Loc.Format("炉の使用料 {0}", "Forge fee {0}", UIStyles.Money(ctx.Profile.UseFee)), UIStyles.LabelDim);
            GUILayout.EndHorizontal();

            if (!made)
            {
                GUI.enabled = canMake;
                string label = plan.IsTimed && !CraftActions.HasMinigame(plan) ? Loc.T("仕込んでみる", "Try leaving it") : Loc.T("はじめて作ってみる", "Make it for the first time");
                if (GUILayout.Button(label, UIStyles.Button, GUILayout.Height(52), GUILayout.Width(360)))
                    CraftActions.CraftRecipe(ctx, r, 1, preferBest, false, MarkRefresh);
                GUI.enabled = true;
            }
            else
            {
                int maxB = Mathf.Max(1, ctx.BatchMax);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(44), GUILayout.Height(44))) batch = Mathf.Max(1, batch - 1);
                GUILayout.Label("×" + batch, UIStyles.LabelCenter, GUILayout.Width(60));
                if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(44), GUILayout.Height(44))) batch = Mathf.Min(maxB, batch + 1);
                if (GUILayout.Button(Loc.T("最大", "Max"), UIStyles.ButtonSmall, GUILayout.Width(70), GUILayout.Height(44)))
                    batch = Mathf.Max(1, CraftEngine.MaxBatch(ctx.Player, r, maxB));
                GUI.enabled = canMake;
                string label = plan.IsTimed ? Loc.Format("まとめて仕込む ×{0}", "Leave x{0}", batch) : Loc.Format("まとめて作る ×{0}", "Make x{0}", batch);
                if (GUILayout.Button(label, UIStyles.Button, GUILayout.Height(52), GUILayout.Width(260)))
                    CraftActions.CraftRecipe(ctx, r, batch, preferBest, false, MarkRefresh);
                if (CraftActions.HasMinigame(plan) && GUILayout.Button(Loc.T("手で仕上げる（1つ）", "Craft by hand (1)"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(230)))
                    CraftActions.CraftRecipe(ctx, r, 1, preferBest, true, MarkRefresh);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Label(Loc.T("覚えた作り方は、生き物たちがいつもの手順でまとめて作ってくれる。手で仕上げると出来が良くなることも。",
                    "Known recipes can be batch-made with the usual routine. Crafting by hand may improve quality."), UIStyles.LabelDim);
            }
            if (!plan.Feasible && plan.Missing.Count > 0)
                GUILayout.Label(Loc.T("足りない力がある。上の赤い手順を見てみよう。", "Some abilities are missing (see red steps)."), CookingUI.SmallWrap);
        }

        // =================================================================
        // 実験（自由調理）
        // =================================================================

        private bool IsFoodStation => ctx.Type == CraftStationType.Kitchen || ctx.Type == CraftStationType.CommunityKitchen || ctx.Type == CraftStationType.Hand || ctx.Type == CraftStationType.CreatureDevice;
        private bool IsMaterialStation => ctx.Type != CraftStationType.CommunityKitchen && ctx.Type != CraftStationType.Hand;

        private void DrawExperiment()
        {
            GUILayout.Label(Loc.T("材料と手順を自由に組み合わせて試してみよう。思いがけない物ができることもある。", "Combine ingredients and steps freely. Unexpected results happen."), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();

            // 左：持ち物
            GUILayout.BeginVertical(UIStyles.Box, GUILayout.Width(400));
            GUILayout.Label(Loc.T("持ち物", "Your items"), UIStyles.LabelBold);
            invScroll = GUILayout.BeginScrollView(invScroll, GUILayout.Height(600));
            var slots = ctx.Player.Inventory.slots;
            bool any = false;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty) continue;
                var d = s.Definition;
                bool ok = (IsFoodStation && Ing.IsFoodish(d)) || (IsMaterialStation && Ing.IsMaterialish(d));
                if (!ok || d.Category == ItemCategory.Furniture) continue;
                any = true;
                int chosenCount = ChosenCount(i);
                GUILayout.BeginHorizontal();
                CookingUI.ItemIcon(s.itemId, s.count - chosenCount, s.quality, 40f);
                GUILayout.Label(s.DisplayName, UIStyles.LabelSmall, GUILayout.Width(230));
                GUI.enabled = chosenCount < s.count && (chosenCount > 0 || expIngredients.Count < CookingTuning.MaxExperimentIngredients);
                if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(44), GUILayout.Height(38))) AddChosen(i, s.itemId);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("使えそうな物を持っていない。", "Nothing usable in your bag."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // 中：選んだ材料
            GUILayout.BeginVertical(UIStyles.Box, GUILayout.Width(340));
            GUILayout.Label(Loc.T("使う材料", "Ingredients"), UIStyles.LabelBold);
            chosenScroll = GUILayout.BeginScrollView(chosenScroll, GUILayout.Height(300));
            for (int i = 0; i < expIngredients.Count; i++)
            {
                var c = expIngredients[i];
                GUILayout.BeginHorizontal();
                CookingUI.ItemIcon(c.ItemId, c.Count, 0, 40f);
                GUILayout.Label(ItemRegistry.NameOf(c.ItemId) + " ×" + c.Count, UIStyles.LabelSmall, GUILayout.Width(180));
                if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(40), GUILayout.Height(38)))
                {
                    c.Count--;
                    if (c.Count <= 0) expIngredients.RemoveAt(i);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
            if (expIngredients.Count == 0) GUILayout.Label(Loc.T("左の「＋」で材料を入れる", "Add with + on the left"), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            if (expIngredients.Count > 0 && GUILayout.Button(Loc.T("材料を全部戻す", "Clear ingredients"), UIStyles.ButtonSmall)) expIngredients.Clear();

            // 観察した手順（住民の実演・前に試したもの）
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("見たこと・試したことから", "From what you've seen"), UIStyles.LabelBold);
            int shown = 0;
            foreach (var d in sys.Knowledge(Pid).demos)
            {
                var r = RecipeBook.Get(d.recipeId);
                if (r == null || !r.AllowsStation(ctx.Type)) continue;
                if (GUILayout.Button(Loc.Format("実演の手順：{0}", "Demo steps: {0}", CraftEngine.StepsText(d.steps)), UIStyles.ButtonSmall))
                {
                    expSteps.Clear();
                    foreach (var s in d.steps) expSteps.Add((CraftStep)s);
                }
                if (++shown >= 4) break;
            }
            if (shown == 0) GUILayout.Label(Loc.T("（料理人の実演を見ると、ここに手順が出る）", "(Watch a cook's demo to see steps here)"), UIStyles.LabelDim);
            GUILayout.EndVertical();

            // 右：手順
            GUILayout.BeginVertical(UIStyles.Box);
            GUILayout.Label(Loc.T("手順（順番も大事）", "Steps (order matters)"), UIStyles.LabelBold);
            var all = (CraftStep[])System.Enum.GetValues(typeof(CraftStep));
            int col = 0;
            GUILayout.BeginHorizontal();
            foreach (var s in all)
            {
                var sp = CraftEngine.PlanStep(ctx, s);
                bool avail = sp.Mode != StepMode.Missing;
                var info = CookingTables.Step(s);
                GUI.enabled = avail && expSteps.Count < CookingTuning.MaxExperimentSteps;
                if (GUILayout.Button(info.Glyph + " " + info.Name, avail ? UIStyles.ButtonSmall : UIStyles.ButtonSmall, GUILayout.Width(150), GUILayout.Height(40))) expSteps.Add(s);
                GUI.enabled = true;
                if (++col % 3 == 0)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("押せない手順は、その力を持つ生き物や器具が要る。", "Greyed steps need a creature or device."), UIStyles.LabelDim);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < expSteps.Count; i++)
            {
                if (i > 0) GUILayout.Label("→", UIStyles.LabelBold, GUILayout.Width(20));
                CookingUI.StepIcon(expSteps[i], 36f);
            }
            if (expSteps.Count == 0) GUILayout.Label(Loc.T("（まだ何もしない）", "(nothing yet)"), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("1つ戻す", "Undo"), UIStyles.ButtonSmall, GUILayout.Width(120)) && expSteps.Count > 0) expSteps.RemoveAt(expSteps.Count - 1);
            if (GUILayout.Button(Loc.T("手順をやり直す", "Clear steps"), UIStyles.ButtonSmall, GUILayout.Width(160))) expSteps.Clear();
            GUILayout.EndHorizontal();

            var planSteps = CraftEngine.PlanSteps(ctx, expSteps);
            if (planSteps.IsTimed)
                GUILayout.Label(Loc.Format("時間のかかる手順がある（約{0}時間）", "Includes slow steps (~{0}h)", Mathf.CeilToInt(planSteps.TimedHours)), UIStyles.LabelDim);

            GUILayout.Space(10);
            GUI.enabled = expIngredients.Count > 0 && expSteps.Count > 0;
            if (GUILayout.Button(Loc.T("試してみる！", "Give it a try!"), UIStyles.Button, GUILayout.Height(60)))
            {
                var chosen = new List<ChosenIngredient>(expIngredients);
                var steps = new List<CraftStep>(expSteps);
                CraftActions.Experiment(ctx, chosen, steps, () =>
                {
                    expIngredients.Clear();
                    MarkRefresh();
                });
            }
            GUI.enabled = true;
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private int ChosenCount(int slotIndex)
        {
            foreach (var c in expIngredients) if (c.SlotIndex == slotIndex) return c.Count;
            return 0;
        }

        private void AddChosen(int slotIndex, string itemId)
        {
            foreach (var c in expIngredients)
            {
                if (c.SlotIndex == slotIndex)
                {
                    c.Count++;
                    return;
                }
            }
            expIngredients.Add(new ChosenIngredient { SlotIndex = slotIndex, ItemId = itemId, Count = 1 });
        }

        // =================================================================
        // 仕込み（時間の加工）
        // =================================================================

        private void DrawTimed()
        {
            var keys = new List<TimedTarget>();
            foreach (var t in ctx.TimedTargets.Values) if (!keys.Exists(k => k.Key == t.Key)) keys.Add(t);
            if (ctx.OwnTimed != null && !keys.Exists(k => k.Key == ctx.OwnTimed.Key)) keys.Add(ctx.OwnTimed);
            tabScroll = GUILayout.BeginScrollView(tabScroll, GUILayout.Height(720));
            if (keys.Count == 0) GUILayout.Label(Loc.T("ここには時間をかける加工の場所がない。", "No place here for slow processing."), UIStyles.LabelDim);
            foreach (var t in keys)
            {
                GUILayout.Label(Loc.Format("{0}（{1}/{2}）", "{0} ({1}/{2})", Loc.T(t.NameJa, t.NameEn), sys.CountSlots(t.Key), t.Capacity), CookingUI.Header);
                bool any = false;
                foreach (var s in new List<TimedSlotData>(sys.SlotsAt(t.Key)))
                {
                    any = true;
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    CookingUI.ItemIcon(s.outputItemId, s.count, s.quality, 52f);
                    GUILayout.BeginVertical();
                    GUILayout.Label(ItemRegistry.NameOf(s.outputItemId) + " ×" + s.count, UIStyles.Label);
                    GUILayout.Label(Loc.Format("仕込んだ人：{0}／{1}", "By {0} / {1}", s.ownerName, CraftEngine.StepsText(s.steps)), UIStyles.LabelDim);
                    var r = GUILayoutUtility.GetRect(400, 18, GUILayout.Width(400), GUILayout.Height(18));
                    UIStyles.ProgressBar(r, s.Progress01, s.ready ? UIStyles.Good : UIStyles.AccentWarm);
                    if (!s.ready) GUILayout.Label(Loc.Format("あと約{0}時間", "~{0}h left", Mathf.CeilToInt(Mathf.Max(0f, s.hoursRequired - s.progressHours))), UIStyles.LabelDim);
                    if (s.weatherBonusApplied) GUILayout.Label(Loc.T("天気のおかげで良い出来になりそう", "The weather is helping"), UIStyles.LabelDim);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    bool mine = s.ownerId == Pid || ctx.Player.Permission >= PermissionLevel.CoLiving;
                    GUI.enabled = s.ready && mine;
                    if (GUILayout.Button(Loc.T("取り出す", "Collect"), UIStyles.Button, GUILayout.Width(150), GUILayout.Height(52)))
                    {
                        CraftActions.Collect(ctx.Player, s);
                        MarkRefresh();
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
                if (!any) GUILayout.Label(Loc.T("空いている。「作り方」や「実験」から、干す・発酵させるなどの手順で仕込める。", "Empty. Leave something via Recipes or Experiment (dry, ferment...)."), CookingUI.SmallWrap);
                GUILayout.Space(8);
            }
            GUILayout.EndScrollView();
        }

        // =================================================================
        // 職人に頼む（ロケット仕様 §9.3）
        // =================================================================

        private void DrawCommission()
        {
            var job = ctx.Profile.CommissionJob;
            var dir = Game.Instance.Service<IResidentDirectory>();
            var resident = dir != null ? dir.FindByJob(job) : null;
            var speech = Game.Instance.Service<ISpeechService>();
            string who = resident != null && speech != null ? speech.ResidentName(resident.Id) : JobLabel(job);
            GUILayout.Label(Loc.Format("{0}に材料と手数料を渡して作ってもらう（出来は「良」）。", "Hand {0} the materials and a fee (quality: good).", who), CookingUI.SmallWrap);
            tabScroll = GUILayout.BeginScrollView(tabScroll, GUILayout.Height(700));
            bool any = false;
            foreach (var r in RecipeBook.All)
            {
                if (r.CommissionFee <= 0 || !r.AllowsStation(ctx.Type)) continue;
                if (!sys.IsKnown(Pid, r)) continue;
                any = true;
                var have = new int[r.Ingredients.Count];
                var takes = CraftEngine.AutoSelect(ctx.Player, r, 1, false, have);
                GUILayout.BeginHorizontal(UIStyles.Box);
                CookingUI.ItemIcon(r.OutputItemId, r.OutputCount, 1, 52f);
                GUILayout.BeginVertical();
                GUILayout.Label(sys.GetRecipeName(r.Id), UIStyles.Label);
                var parts = new List<string>();
                for (int i = 0; i < r.Ingredients.Count; i++)
                    parts.Add(Loc.Format("{0}×{1}({2})", "{0}x{1}({2})", CookingUI.IngredientLabel(r.Ingredients[i]), r.Ingredients[i].Count, have[i]));
                GUILayout.Label(string.Join(Loc.T("、", ", "), parts.ToArray()), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(UIStyles.Money(r.CommissionFee), UIStyles.LabelBold, GUILayout.Width(110));
                GUI.enabled = takes != null && ctx.Player.Wallet.CanAfford(r.CommissionFee);
                if (GUILayout.Button(Loc.T("頼む", "Request"), UIStyles.Button, GUILayout.Width(120), GUILayout.Height(52)))
                {
                    CraftActions.Commission(ctx, r, 1);
                    MarkRefresh();
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("ここで頼める物はない。", "Nothing to commission here."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        private static string JobLabel(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return Loc.T("鍛冶屋", "the blacksmith");
                case ResidentJob.Mechanic: return Loc.T("修理屋", "the mechanic");
                case ResidentJob.Carpenter: return Loc.T("家具職人", "the carpenter");
                case ResidentJob.Cook: return Loc.T("料理人", "the cook");
                default: return Loc.T("職人", "the artisan");
            }
        }

        // =================================================================
        // 生き物（§4.3：生物は機械ではなく生き物。器具に住んでもらう／作業場で手伝ってもらう）
        // =================================================================

        private void DrawCreatures()
        {
            var cs = Game.Instance.Service<ICreatureService>();
            tabScroll = GUILayout.BeginScrollView(tabScroll, GUILayout.Height(720));
            if (cs == null)
            {
                GUILayout.Label(Loc.T("（生き物の情報がない）", "(No creature info)"), UIStyles.LabelDim);
                GUILayout.EndScrollView();
                return;
            }
            var owned = new List<CreatureView>();
            try { owned.AddRange(cs.GetOwned(Pid)); } catch (System.Exception e) { Debug.LogException(e); }

            // ---- 台所の器具 ----
            foreach (var f in ctx.LinkedDevices)
            {
                var dev = CookingContent.Device(f.ItemId);
                if (dev == null) continue;
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.BeginHorizontal();
                CookingUI.ItemIcon(f.ItemId, 1, 0, 44f);
                GUILayout.Label(dev.Name, UIStyles.LabelBold);
                if (dev.Ability.HasValue)
                {
                    CookingUI.AbilityIcon(dev.Ability.Value, 28f);
                    GUILayout.Label(Loc.Format("「{0}」の生き物の居場所", "Home for a \"{0}\" creature", CookingTables.Ability(dev.Ability.Value).Name), UIStyles.LabelDim);
                }
                GUILayout.EndHorizontal();
                var living = new HashSet<string>();
                foreach (var src in ctx.Sources)
                {
                    if (src.DeviceInstanceId != f.InstanceId || !living.Add(src.CreatureUid)) continue;
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("・" + src.DisplayName + " " + IdleText(src, dev), CookingUI.SmallWrap, GUILayout.Width(760));
                    if (src.OwnerId == Pid && GUILayout.Button(Loc.T("連れて行く", "Take along"), UIStyles.ButtonSmall, GUILayout.Width(150)))
                    {
                        SafePlace(cs, src.CreatureUid, CreaturePlacement.Companion, null);
                    }
                    GUILayout.EndHorizontal();
                }
                if (living.Count == 0) GUILayout.Label(Loc.T("今は誰も住んでいない。", "Nobody lives here now."), UIStyles.LabelDim);
                if (dev.Ability.HasValue && f.OwnerId == Pid)
                {
                    foreach (var c in owned)
                    {
                        if (c.Placement == CreaturePlacement.Device && c.PlacementTargetId == f.InstanceId) continue;
                        if (c.AbilityLevel(dev.Ability.Value) <= 0) continue;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(Loc.Format("{0}（今：{1}）", "{0} (now: {1})", Name(cs, c), PlacementLabel(c.Placement)), UIStyles.LabelSmall, GUILayout.Width(560));
                        if (GUILayout.Button(Loc.T("ここに住んでもらう", "Move in here"), UIStyles.ButtonSmall, GUILayout.Width(220)))
                            SafePlace(cs, c.Uid, CreaturePlacement.Device, f.InstanceId);
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
            }

            // ---- 修理作業場・精製器で手伝う ----
            if (ctx.Profile.AcceptsWorkshopCreatures)
            {
                GUILayout.Label(Loc.T("作業場で手伝ってもらう", "Help at the workshop"), CookingUI.Header);
                foreach (var c in owned)
                {
                    bool here = c.Placement == CreaturePlacement.RocketWorkshop;
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    GUILayout.Label(Name(cs, c), UIStyles.LabelSmall, GUILayout.Width(220));
                    foreach (var al in c.Abilities) CookingUI.AbilityIcon(al.ability, 26f);
                    GUILayout.FlexibleSpace();
                    if (here)
                    {
                        if (GUILayout.Button(Loc.T("連れて行く", "Take along"), UIStyles.ButtonSmall, GUILayout.Width(160))) SafePlace(cs, c.Uid, CreaturePlacement.Companion, null);
                    }
                    else if (GUILayout.Button(Loc.T("ここで手伝ってもらう", "Help here"), UIStyles.ButtonSmall, GUILayout.Width(220)))
                        SafePlace(cs, c.Uid, CreaturePlacement.RocketWorkshop, ctx.InstanceId);
                    GUILayout.EndHorizontal();
                }
            }

            // ---- いっしょにいる生き物 ----
            GUILayout.Label(Loc.T("いっしょにいる生き物", "Creatures with you"), CookingUI.Header);
            bool anyCompanion = false;
            foreach (var c in owned)
            {
                if (c.Placement != CreaturePlacement.Companion) continue;
                anyCompanion = true;
                GUILayout.BeginHorizontal();
                GUILayout.Label(Name(cs, c), UIStyles.LabelSmall, GUILayout.Width(220));
                foreach (var al in c.Abilities)
                {
                    CookingUI.AbilityIcon(al.ability, 26f);
                    int m = CraftEngine.MasteryLevel(sys.Profile(Pid), c.Uid, al.ability);
                    if (m > 0) GUILayout.Label(new string('★', m), UIStyles.LabelDim);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (!anyCompanion) GUILayout.Label(Loc.T("同行している生き物はいない。生き物の力は、連れて歩くだけでも料理に使える。", "No companions. Creatures with you can lend their abilities."), CookingUI.SmallWrap);

            // ---- 止まり木：実演を見直す（マネピの再現） ----
            if (ctx.HasMimic)
            {
                GUILayout.Label(Loc.T("止まり木の生き物に、見た実演をまねてもらう", "Ask the mimic to replay a demo"), CookingUI.Header);
                foreach (var d in sys.Knowledge(Pid).demos)
                {
                    if (GUILayout.Button(Loc.Format("{0}日目に見た実演", "Demo seen on day {0}", d.day) + "：" + CraftEngine.ItemsText(d.ingredients), UIStyles.ButtonSmall))
                        CookDemoWindow.Replay(sys, ctx.Player, d);
                }
            }
            GUILayout.EndScrollView();
        }

        private string IdleText(AbilitySource src, DeviceDef dev)
        {
            // 調理していない時のくらし（§4.3）。夜は眠っていることが多い
            var clock = Game.Instance.Time;
            if (clock != null && clock.IsNight) return Loc.T("はすやすや眠っている", "is fast asleep");
            var ab = dev != null && dev.Ability.HasValue ? dev.Ability.Value : src.Ability;
            return CookingTables.Ability(ab).Idle;
        }

        private static string Name(ICreatureService cs, CreatureView c)
        {
            return !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : CraftEngine.SafeSpeciesName(cs, c.SpeciesId);
        }

        private static string PlacementLabel(CreaturePlacement p)
        {
            switch (p)
            {
                case CreaturePlacement.Companion: return Loc.T("同行", "with you");
                case CreaturePlacement.Farm: return Loc.T("農場", "farm");
                case CreaturePlacement.Home: return Loc.T("家", "home");
                case CreaturePlacement.Device: return Loc.T("器具", "device");
                case CreaturePlacement.RocketWorkshop: return Loc.T("作業場", "workshop");
                case CreaturePlacement.Storage: return Loc.T("預かり", "storage");
                default: return p.ToString();
            }
        }

        private void SafePlace(ICreatureService cs, string uid, CreaturePlacement p, string target)
        {
            try
            {
                if (!cs.SetPlacement(uid, p, target)) UIManager.Toast(Loc.T("今は動かせないようだ", "Can't move it right now"));
            }
            catch (System.Exception e) { Debug.LogException(e); }
            MarkRefresh();
        }
    }
}
