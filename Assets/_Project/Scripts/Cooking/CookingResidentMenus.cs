using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 住民に話しかけた時のメニュー（住民の料理・食文化 §11、器具の購入、職人への依頼 ロケット §9.3）。
    ///   料理人（Cook）：料理を見せてもらう（実演＝身振りと材料→レシピの手掛かり）／食材を買う
    ///   家具職人（Carpenter）：調理器具を注文する／作業台で加工・器具を作ってもらう
    ///   雑貨屋（Merchant）：台所用品を買う
    ///   修理屋（Mechanic）：部品の加工を頼む（修理作業場の画面を開く）
    /// 宇宙語の理解は不要（身振り・材料・完成品を見せる）。
    /// </summary>
    public sealed class CookingResidentMenus : IResidentInteractionProvider
    {
        private readonly CookingSystem sys;

        public CookingResidentMenus(CookingSystem sys) { this.sys = sys; }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            var list = new List<ResidentInteractionOption>();
            if (resident == null || player == null) return list;
            switch (resident.Job)
            {
                case ResidentJob.Cook:
                    list.Add(new ResidentInteractionOption("料理を見せてもらう", "Watch them cook", () => Demo(resident, player), 40));
                    list.Add(new ResidentInteractionOption("食材を買う", "Buy ingredients", () => OpenShop(resident, player, ResidentJob.Cook), 60));
                    break;
                case ResidentJob.Carpenter:
                    list.Add(new ResidentInteractionOption("調理器具を注文する", "Order kitchen devices", () => OpenShop(resident, player, ResidentJob.Carpenter), 60));
                    list.Add(new ResidentInteractionOption("作業台で加工してもらう", "Use the carpenter's bench", () => sys.OpenStation(CraftStationType.CarpenterBench, "carpenter:" + resident.Id, player), 65));
                    break;
                case ResidentJob.Merchant:
                    list.Add(new ResidentInteractionOption("台所用品を買う", "Buy kitchen goods", () => OpenShop(resident, player, ResidentJob.Merchant), 60));
                    break;
                case ResidentJob.Mechanic:
                    list.Add(new ResidentInteractionOption("部品の加工を頼む", "Ask to process parts", () => sys.OpenStation(CraftStationType.RocketWorkshop, "mechanic:" + resident.Id, player), 55));
                    break;
            }
            return list;
        }

        // =================================================================
        // 販売メニュー
        // =================================================================

        private void OpenShop(IResidentInfo resident, PlayerState player, ResidentJob job)
        {
            var entries = new List<VendorEntry>();
            if (job == ResidentJob.Cook || job == ResidentJob.Merchant)
            {
                // §2.1：序盤から料理できるよう、基本の食材・調味料を売る【仮数値：基準価格の1.2倍】
                foreach (var id in new[] { CookingIds.StarSalt, CookingIds.TreeNectar, CookingIds.FragrantHerb, CookingIds.VillageGrain, CookingIds.RoundBean })
                {
                    var d = ItemRegistry.Get(id);
                    if (d == null) continue;
                    entries.Add(new VendorEntry { ItemId = id, Price = Mathf.CeilToInt(d.BasePrice * 1.2f) });
                }
            }
            foreach (var dev in CookingContent.Devices)
            {
                bool sells = false;
                foreach (var s in dev.Sellers) if (s == job) sells = true;
                if (!sells) continue;
                entries.Add(new VendorEntry { ItemId = dev.ItemId, Price = dev.Price });
            }
            if (entries.Count == 0) return;
            var speech = Game.Instance.Service<ISpeechService>();
            string title = speech != null ? speech.ResidentName(resident.Id) : Loc.T("お店", "Shop");
            UIManager.Open(new VendorWindow(title, player, entries, (e, n) =>
            {
                var dir = Game.Instance.Service<IResidentDirectory>();
                if (dir == null) return;
                try
                {
                    dir.GiveMoney(resident.Id, e.Price * n, "sold:" + e.ItemId);
                    var u = new Utterance(resident.Id, "cooking.sold")
                        .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS))
                        .Face(Expression.Happy).With(Gesture.Bow).Related(e.ItemId);
                    dir.Speak(resident.Id, u);
                }
                catch (System.Exception ex) { Debug.LogException(ex); }
            }));
        }

        // =================================================================
        // 料理の実演（§11.1・§12.1：住民を観察する／一緒に調理する）
        // =================================================================

        private void Demo(IResidentInfo resident, PlayerState player)
        {
            var game = Game.Instance;
            var dir = game.Service<IResidentDirectory>();
            int day = game.Time != null ? game.Time.Day : 0;
            var k = sys.Knowledge(player.PlayerId);
            int last = -999;
            foreach (var p in k.lastDemoDay) if (p.key == resident.Id) last = p.value;
            if (day - last < CookingTuning.DemoIntervalDays)
            {
                // 今日はもう見せてくれた：首を振って「明日」と言う（言葉が分からなくても身振りで分かる）
                var no = new Utterance(resident.Id, "cooking.demo_later")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).When(Concepts.TOMORROW).Future())
                    .Face(Expression.Neutral).With(Gesture.ShakeHead).With(Gesture.Wave);
                dir?.Speak(resident.Id, no);
                return;
            }

            var recipe = PickDemoRecipe(resident, player, day);
            if (recipe == null)
            {
                var none = new Utterance(resident.Id, "cooking.demo_none")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.KNOW).Agent(Concepts.YOU).Patient(Concepts.ALL).Adverb(Concepts.ALREADY))
                    .Face(Expression.Surprised).With(Gesture.Clap);
                dir?.Speak(resident.Id, none);
                return;
            }

            var samples = new List<string>();
            foreach (var spec in recipe.Ingredients)
            {
                var s = spec.FindSample();
                if (s != null) samples.Add(s);
            }
            // 意味データ：「見て」「私は◯◯を料理する」＋材料を指さす・完成品を差し出す身振り
            var u = new Utterance(resident.Id, "cooking.demo")
                .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.SEE).Agent(Concepts.YOU).Patient(Concepts.THIS))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.Item(recipe.OutputItemId))))
                .Face(Expression.Happy);
            for (int i = 0; i < recipe.Ingredients.Count && i < samples.Count; i++)
            {
                u.With(Gesture.PointAt, samples[i]);
                if (recipe.Ingredients[i].Count > 1) u.With(Gesture.ShowNumber, samples[i], recipe.Ingredients[i].Count);
                u.Related(samples[i]);
            }
            u.With(Gesture.OfferItem, recipe.OutputItemId).Related(recipe.OutputItemId);
            UtteranceRecord record = null;
            if (dir != null)
            {
                try { record = dir.Speak(resident.Id, u); }
                catch (System.Exception e) { Debug.LogException(e); }
            }

            var demo = new DemoRecord { recipeId = recipe.Id, residentId = resident.Id, day = day, utteranceRecordId = record != null ? record.recordId : null };
            demo.ingredients.AddRange(samples);
            foreach (var s in recipe.Steps) demo.steps.Add((int)s);
            k.demos.RemoveAll(d => d.recipeId == recipe.Id);
            k.demos.Add(demo);
            bool found = false;
            foreach (var p in k.lastDemoDay) if (p.key == resident.Id) { p.value = day; found = true; }
            if (!found) k.lastDemoDay.Add(new StringIntPair(resident.Id, day));

            if (dir != null)
            {
                try
                {
                    dir.ChangeFriendship(resident.Id, player.PlayerId, CookingTuning.DemoFriendship, "cooking_demo");
                    dir.AddMemory(resident.Id, new ResidentMemoryEntry { kind = "cooked_together", playerId = player.PlayerId, itemId = recipe.OutputItemId, day = day, importance = 0.4f });
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }

            // タブレットの資料と住民辞典へ「観察できた事実」だけを記録（言葉の意味は書かない）
            var tablet = game.Service<ITabletService>();
            if (tablet != null)
            {
                try
                {
                    var outDef = ItemRegistry.GetOrPlaceholder(recipe.OutputItemId);
                    var m = new TabletMaterial
                    {
                        id = "cooking.demo." + resident.Id + "." + day,
                        kind = TabletMaterialKind.Recipe,
                        titleJa = "料理の実演（" + day + "日目）",
                        titleEn = "Cooking demo (day " + day + ")",
                        factJa = "住民が " + CraftEngine.ItemsText(samples) + " を指さし、" + StepsGesture(recipe.Steps, true) + " の仕草をして、" + outDef.NameJa + " を作ってみせた。",
                        factEn = "A resident pointed at " + CraftEngine.ItemsText(samples) + ", made gestures of " + StepsGesture(recipe.Steps, false) + ", and produced " + outDef.NameEn + ".",
                        day = day,
                        minuteOfDay = game.Time != null ? game.Time.MinuteOfDay : 0,
                        linkedRecordId = demo.utteranceRecordId,
                        linkedResidentId = resident.Id
                    };
                    m.relatedIds.AddRange(samples);
                    m.relatedIds.Add(recipe.OutputItemId);
                    m.tags.Add("cooking");
                    tablet.AddMaterial(m);
                    tablet.RecordResidentObservation(resident.Id, outDef.NameJa + " を作ってみせてくれた", "Showed me how to make " + outDef.NameEn, demo.utteranceRecordId);
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }

            UIManager.Open(new CookDemoWindow(sys, player, demo, record, false));
        }

        private static string StepsGesture(List<CraftStep> steps, bool ja)
        {
            var parts = new List<string>();
            foreach (var s in steps)
            {
                var info = CookingTables.Step(s);
                parts.Add(ja ? info.NameJa : info.NameEn);
            }
            return string.Join(ja ? "→" : " -> ", parts.ToArray());
        }

        /// <summary>
        /// 見せてくれる料理を選ぶ。まだ知らない作り方を優先し、この星でよく見る料理（§14）を先に。
        /// 決定的に選ぶ（星のシード・住民・日付）。
        /// </summary>
        private RecipeDef PickDemoRecipe(IResidentInfo resident, PlayerState player, int day)
        {
            var preferred = new List<RecipeDef>();
            var others = new List<RecipeDef>();
            foreach (var r in RecipeBook.All)
            {
                if (!r.Demonstrable) continue;
                if (sys.IsKnown(player.PlayerId, r)) continue;
                if (sys.FindDemo(player.PlayerId, r.Id) != null) continue;
                if (sys.LocalSpecialties.Contains(r.Id)) preferred.Add(r);
                else others.Add(r);
            }
            ulong seed = Game.Instance.Planet != null ? Game.Instance.Planet.seed : 1UL;
            var rng = SeededRandom.For(seed, "cooking.demo." + resident.Id + "." + day);
            if (preferred.Count > 0) return rng.Pick(preferred);
            if (others.Count > 0)
            {
                // 簡単な物（材料・手順が少ない物）から見せる
                others.Sort((a, b) => (a.Steps.Count + a.Ingredients.Count).CompareTo(b.Steps.Count + b.Ingredients.Count));
                int window = Mathf.Min(4, others.Count);
                return others[rng.Range(0, window)];
            }
            return null;
        }
    }

    /// <summary>
    /// 料理の実演を見る画面。言葉の訳は出さず、材料・身振り（工程）・完成品を順に見せる。
    /// マネピ（止まり木）で見直す時も使う。
    /// </summary>
    public sealed class CookDemoWindow : UIWindow
    {
        private readonly CookingSystem sys;
        private readonly PlayerState player;
        private readonly DemoRecord demo;
        private readonly UtteranceRecord record;
        private readonly bool replay;
        private float t;

        public CookDemoWindow(CookingSystem sys, PlayerState player, DemoRecord demo, UtteranceRecord record, bool replay)
        {
            this.sys = sys;
            this.player = player;
            this.demo = demo;
            this.record = record;
            this.replay = replay;
        }

        public override string Title => replay ? Loc.T("実演の再現", "Demo replay") : Loc.T("料理の実演", "Cooking demo");
        public override Vector2 Size => new Vector2(900, 560);
        public override bool PausesTimeInSolo => false;

        public static void Replay(CookingSystem sys, PlayerState player, DemoRecord demo)
        {
            UtteranceRecord rec = null;
            var speech = Game.Instance.Service<ISpeechService>();
            if (speech != null && !string.IsNullOrEmpty(demo.utteranceRecordId))
            {
                try
                {
                    rec = speech.GetRecord(demo.utteranceRecordId);
                    if (rec != null && player.Avatar != null) speech.Replay(rec, player.Avatar);
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            UIManager.Open(new CookDemoWindow(sys, player, demo, rec, true));
        }

        public override void Update()
        {
            t += UnityEngine.Time.unscaledDeltaTime;
        }

        public override void DrawContents(Rect content)
        {
            var recipe = RecipeBook.Get(demo.recipeId);
            GUILayout.Label(replay
                ? Loc.T("止まり木の生き物が、料理人の仕草をまねている。", "The mimic copies the cook's gestures.")
                : Loc.T("料理人が手元を見せてくれる。言葉は分からなくても、手の動きは分かる。", "The cook shows you their hands. Even without words, you can follow."), CookingUI.SmallWrap);
            GUILayout.Space(10);
            int phase = Mathf.FloorToInt(t / 0.9f);

            GUILayout.Label(Loc.T("使った物", "Used"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < demo.ingredients.Count; i++)
            {
                if (phase < i) break;
                CookingUI.ItemIcon(demo.ingredients[i], 1, 0, 64f);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUILayout.Label(Loc.T("した仕草", "Gestures"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            int baseIdx = demo.ingredients.Count;
            for (int i = 0; i < demo.steps.Count; i++)
            {
                if (phase < baseIdx + i) break;
                if (i > 0) GUILayout.Label("→", UIStyles.LabelBold, GUILayout.Width(22));
                var s = (CraftStep)demo.steps[i];
                GUILayout.BeginVertical(GUILayout.Width(110));
                CookingUI.StepIcon(s, 56f);
                GUILayout.Label(Loc.Format("{0}ような仕草", "a \"{0}\" motion", CookingTables.Step(s).Name), UIStyles.LabelDim);
                GUILayout.EndVertical();
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            if (recipe != null && phase >= baseIdx + demo.steps.Count)
            {
                GUILayout.BeginHorizontal();
                CookingUI.ItemIcon(recipe.OutputItemId, 1, 0, 72f);
                GUILayout.Label(Loc.Format("できあがり：{0}", "Result: {0}", ItemRegistry.NameOf(recipe.OutputItemId)), CookingUI.Header);
                GUILayout.EndHorizontal();
                GUILayout.Label(Loc.T("同じ材料と仕草で、自分でも作ってみよう（作り方の一覧に手掛かりとして載った）。", "Try it yourself with the same items and motions (added to your recipes as a hint)."), CookingUI.SmallWrap);
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("もう一度見る", "Watch again"), UIStyles.Button, GUILayout.Height(48))) t = 0f;
            var tablet = Game.Instance.Service<ITabletService>();
            if (record != null && tablet != null && GUILayout.Button(Loc.T("声を音声カードに保存", "Save voice card"), UIStyles.Button, GUILayout.Height(48)))
            {
                try
                {
                    tablet.SaveVoiceCard(record, "料理人の実演", "Cook's demo");
                    UIManager.Toast(Loc.T("音声カードに保存した", "Saved as a voice card"));
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (!replay && GUILayout.Button(Loc.T("お辞儀して終わる", "Bow and finish"), UIStyles.Button, GUILayout.Height(48)))
            {
                Close();
                Game.Instance.PerformLocalGesture(PlayerGesture.Bow);
                return;
            }
            if (GUILayout.Button(Loc.T("閉じる", "Close"), UIStyles.Button, GUILayout.Height(48))) Close();
            GUILayout.EndHorizontal();
        }
    }
}
