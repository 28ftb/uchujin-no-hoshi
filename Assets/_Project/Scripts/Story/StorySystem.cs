using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// タイトル・キャラクターメイク・プロローグ・章・エンディング・クリア後（メインストーリー v0.2、基本決定 v0.2 §3・§5）。
    ///
    /// ・タイトル画面（Game.TitleScreenFactory）とキャラクターメイク（PlayerAvatarFactory.BuildVisual の上書き）を提供する。
    /// ・新しい星の開始時にプロローグ（不時着〜案内役に連れられて村へ）を演出する。2つ目以降の星は短い到着の演出。
    /// ・章（第1章〜最終章）は画面に明示せず、節目のイベントから内部で追跡し Flags("story.chapter") に立てる。
    /// ・帰還を選ぶと（Rocket から BeginEnding）エンディング「最後の引っ越し」→ クリア後の選択（ロケット前へ戻る／次の星へ）。
    /// ・案内役（まんまるな宇宙人）と、その口癖（星ごとに決定的に選ぶ概念）を他モジュールへ公開する。
    /// </summary>
    public sealed class StorySystem : GameSystemBase, IStoryService
    {
        public static StorySystem Instance { get; private set; }

        public override string SaveKey => "story";
        public override int DailyUpdateOrder => 495;

        private StoryPlanetState state = new StoryPlanetState();
        private StoryProfileState profile = new StoryProfileState();
        private string preparedCatchphrase;
        private string preparedWorldId;
        /// <summary>案内役を確定して保存してよいか（住民の生成が終わる前に問い合わせられても、途中の結果を保存しない）。</summary>
        private bool guideCacheAllowed;
        private bool expectingPrologue;
        private bool subscribed;

        private PrologueSequence prologue;
        private EndingSequence ending;
        private MemoriesReel reel;

        // 案内役の世話焼き（第1章）
        private float guideCheckTimer;
        private bool guideApproaching;
        private float guideApproachTime;

        private static readonly AppearanceApp appearanceApp = new AppearanceApp();
        private static readonly MemoriesApp memoriesApp = new MemoriesApp();

        /// <summary>次の星へ進む時の引き継ぎ設定（OnNewPlanet で適用して消す）。</summary>
        internal static CarryOverPlan PendingCarry;

        public StorySystem()
        {
            Instance = this;
        }

        internal StoryPlanetState State => state;
        internal StoryProfileState Profile => profile;

        // ------------------------------------------------------------
        // IStoryService
        // ------------------------------------------------------------

        public int Chapter => state.chapter;
        public bool IsPrologueDone => state.prologueDone;

        public string GuideResidentId
        {
            get
            {
                EnsureGuide();
                return !string.IsNullOrEmpty(state.guideResidentId) ? state.guideResidentId : transientGuide;
            }
        }

        /// <summary>
        /// 案内役の口癖。星のシードから決定的に選ぶ（用途キー "story.catchphrase"）。
        /// どのシステムの PreparePlanet/OnNewPlanet から問い合わせられても、その星の値を返す。
        /// </summary>
        public string GuideCatchphraseConcept
        {
            get
            {
                var planet = Game != null ? Game.Planet : null;
                if (planet != null && preparedWorldId != planet.worldId)
                {
                    preparedCatchphrase = ComputeCatchphrase(planet.seed);
                    preparedWorldId = planet.worldId;
                }
                if (!string.IsNullOrEmpty(preparedCatchphrase)) return preparedCatchphrase;
                return string.IsNullOrEmpty(state.catchphraseConcept) ? Concepts.FRIEND : state.catchphraseConcept;
            }
        }

        public static string ComputeCatchphrase(ulong seed)
        {
            var rng = SeededRandom.For(seed, "story.catchphrase");
            return StoryTuning.CatchphraseCandidates[rng.Range(0, StoryTuning.CatchphraseCandidates.Length)];
        }

        public bool IsEndingInProgress => ending != null && ending.Running;

        public bool IsCutsceneRunning => (prologue != null && prologue.Running) || IsEndingInProgress || (reel != null && reel.Running);

        /// <summary>帰還を選んだ時にロケット側から呼ばれる。エンディング「最後の引っ越し」を始める。</summary>
        public void BeginEnding()
        {
            if (Game == null || Game.Planet == null) return;
            if (IsEndingInProgress || IsCutsceneRunning) return;
            if (!IsAuthority)
            {
                UIManager.Toast(Loc.T("帰還の日は、この星の持ち主が決めます。", "Only the planet's owner can choose to return home."));
                return;
            }
            UIManager.CloseAll();
            var tablet = Game.Service<ITabletService>();
            if (tablet != null && tablet.IsOpen) tablet.Close();
            ending = new EndingSequence(this);
            ending.Start();
        }

        // ------------------------------------------------------------
        // 起動時
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            Game.TitleScreenFactory = () => new TitleWindow();
            PlayerAvatarFactory.BuildVisual = AvatarBuilder.Build;
            Game.OnNewPlanetStarted -= HandleNewPlanetStarted;
            Game.OnNewPlanetStarted += HandleNewPlanetStarted;
            EventBus.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
            RegisterItems();
        }

        private static void RegisterItems()
        {
            ItemRegistry.Register(new ItemDefinition
            {
                Id = StoryTuning.ItemWelcomeSnack,
                NameJa = "はじめての差し入れ",
                NameEn = "First welcome snack",
                DescriptionJa = "不時着した日に、知らない宇宙人が差し出してくれた食べ物。ほんのり甘い。",
                DescriptionEn = "Food an unknown alien offered you on the day you crash-landed. Faintly sweet.",
                Category = ItemCategory.Food,
                BasePrice = 30,
                MaxStack = 10,
                Consumable = true,
                Color = new Color(1f, 0.8f, 0.5f),
                Shape = PrimitiveType.Sphere,
                GenericConceptId = Concepts.FOOD,
                SourceActivity = "story"
            }.WithTags(ItemTags.Food, ItemTags.Edible, ItemTags.Sweet, ItemTags.Gift));
            ItemRegistry.Register(new ItemDefinition
            {
                Id = StoryTuning.ItemGuideSnack,
                NameJa = "まんまる焼き",
                NameEn = "Round bun",
                DescriptionJa = "まんまるな住民がときどき持ってくる、丸い焼き菓子。",
                DescriptionEn = "A round baked treat the round resident brings now and then.",
                Category = ItemCategory.Food,
                BasePrice = 25,
                MaxStack = 20,
                Consumable = true,
                Color = new Color(0.95f, 0.72f, 0.45f),
                Shape = PrimitiveType.Sphere,
                GenericConceptId = Concepts.FOOD,
                SourceActivity = "story"
            }.WithTags(ItemTags.Food, ItemTags.Edible, ItemTags.Sweet, ItemTags.Gift));
            ItemRegistry.Register(new ItemDefinition
            {
                Id = StoryTuning.ItemTravelBento,
                NameJa = "旅のお弁当",
                NameEn = "Travel lunchbox",
                DescriptionJa = "帰る日に料理好きの住民が持たせてくれたお弁当。",
                DescriptionEn = "A lunchbox the cooking-loving resident gave you on the day you left.",
                Category = ItemCategory.Food,
                BasePrice = 80,
                MaxStack = 10,
                Consumable = true,
                Color = new Color(0.95f, 0.55f, 0.45f),
                Shape = PrimitiveType.Cube,
                GenericConceptId = Concepts.DISH,
                SourceActivity = "story"
            }.WithTags(ItemTags.Food, ItemTags.Edible, ItemTags.Savory, ItemTags.Gift, ItemTags.NotSellable));
        }

        private static void OnGameStateChanged(GameStateChangedEvent e)
        {
            if (e.Current != GameState.Title && e.Current != GameState.CharacterCreation) TitleBackdrop.DestroyIfAny();
        }

        // ------------------------------------------------------------
        // 星のライフサイクル
        // ------------------------------------------------------------

        public override void PreparePlanet(PlanetContext ctx)
        {
            state = new StoryPlanetState();
            expectingPrologue = false;
            guideCacheAllowed = false;
            preparedCatchphrase = ComputeCatchphrase(ctx.Planet.seed);
            preparedWorldId = ctx.Planet.worldId;
            state.catchphraseConcept = preparedCatchphrase;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            state.chapter = StoryTuning.ChapterPrologue;
            state.prologueDone = false;
            state.SetMilestoneOnce("arrived", 1);
            expectingPrologue = true;
            guideCacheAllowed = true;
            EnsureGuide();

            // 次の星へ進む場合の引き継ぎ（選択式・仮）。持ち物を引き継ぐ開始（KeepProfile）の時だけ適用する。
            // （「次の星へ」の開始が取り消されて残った設定を、新しいゲームに誤って適用しない）
            if (ctx.IsFreshProfile)
            {
                PendingCarry = null;
                // まったく新しい旅：旅の記録・見た目の控えも最初から（前のキャラクターの記録を持ち込まない）
                profile = new StoryProfileState();
            }
            else if (PendingCarry != null)
            {
                ApplyCarryOver(PendingCarry);
                PendingCarry = null;
            }
        }

        public override void OnPlayStarted()
        {
            guideCacheAllowed = true;
            Subscribe();
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                try
                {
                    tablet.RegisterApp(appearanceApp);
                    tablet.RegisterApp(memoriesApp);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            UpdateJourneyEntry(false);

            // ロードした星でプロローグが途中だった場合は、静かに終えた扱いにする（再生し直さない）
            if (!expectingPrologue && IsAuthority && !state.prologueDone)
            {
                CompletePrologue(true, false);
            }
            if (Game.Flags != null) Game.Flags.Set("story.chapter", state.chapter);
        }

        public override void Teardown()
        {
            Unsubscribe();
            prologue?.Abort();
            ending?.Abort();
            reel?.Abort();
            prologue = null;
            ending = null;
            reel = null;
            guideApproaching = false;
            UIManager.HudVisible = true;
            if (LocalPlayer != null) LocalPlayer.UnlockInput("story");
            if (Clock != null)
            {
                Clock.RemoveGlobalPause("story.ending");
                Clock.RemoveClockHold("story.ending");
                Clock.RemoveGlobalPause("story.reel");
            }
            if (Game != null && Game.Planet != null) UpdateJourneyEntry(false);
        }

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            EventBus.Subscribe<ResidentMovedInEvent>(OnMovedIn);
            EventBus.Subscribe<ResidentMovedOutEvent>(OnMovedOut);
            EventBus.Subscribe<RocketDeviceRepairedEvent>(OnRepaired);
            EventBus.Subscribe<RocketLaunchReadyEvent>(OnLaunchReady);
            EventBus.Subscribe<ShopItemSoldEvent>(OnShopSold);
            EventBus.Subscribe<FishCaughtEvent>(OnFish);
            EventBus.Subscribe<CropHarvestedEvent>(OnCrop);
            EventBus.Subscribe<OreMinedEvent>(OnOre);
            EventBus.Subscribe<CreatureCaughtEvent>(OnCreature);
            EventBus.Subscribe<ItemCraftedEvent>(OnCraft);
            EventBus.Subscribe<GiftGivenEvent>(OnGift);
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            EventBus.Unsubscribe<ResidentMovedInEvent>(OnMovedIn);
            EventBus.Unsubscribe<ResidentMovedOutEvent>(OnMovedOut);
            EventBus.Unsubscribe<RocketDeviceRepairedEvent>(OnRepaired);
            EventBus.Unsubscribe<RocketLaunchReadyEvent>(OnLaunchReady);
            EventBus.Unsubscribe<ShopItemSoldEvent>(OnShopSold);
            EventBus.Unsubscribe<FishCaughtEvent>(OnFish);
            EventBus.Unsubscribe<CropHarvestedEvent>(OnCrop);
            EventBus.Unsubscribe<OreMinedEvent>(OnOre);
            EventBus.Unsubscribe<CreatureCaughtEvent>(OnCreature);
            EventBus.Unsubscribe<ItemCraftedEvent>(OnCraft);
            EventBus.Unsubscribe<GiftGivenEvent>(OnGift);
        }

        // ------------------------------------------------------------
        // 案内役
        // ------------------------------------------------------------

        /// <summary>
        /// 案内役＝ActiveResidents のうち Lineage==Round を Id 順で最初の1人（Residents 担当と取り決め済み）。
        /// 一度決めたら星のセーブに残す（後で引っ越しても、思い出の相手は変わらない）。
        /// </summary>
        private void EnsureGuide()
        {
            if (!string.IsNullOrEmpty(state.guideResidentId)) return;
            var found = FindGuideCandidate();
            if (guideCacheAllowed) state.guideResidentId = found;
            else transientGuide = found;
        }

        private string transientGuide;

        private string FindGuideCandidate()
        {
            var dir = Game != null ? Game.Service<IResidentDirectory>() : null;
            if (dir == null) return null;
            string best = null;
            string anyBest = null;
            try
            {
                foreach (var r in dir.ActiveResidents)
                {
                    if (r == null || string.IsNullOrEmpty(r.Id)) continue;
                    if (anyBest == null || string.CompareOrdinal(r.Id, anyBest) < 0) anyBest = r.Id;
                    if (r.Lineage != AlienLineage.Round) continue;
                    if (best == null || string.CompareOrdinal(r.Id, best) < 0) best = r.Id;
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            return best ?? anyBest;
        }

        internal IResidentInfo GuideInfo()
        {
            var id = GuideResidentId;
            if (string.IsNullOrEmpty(id)) return null;
            var r = StoryActors.Get(id);
            return r != null && r.IsActive ? r : null;
        }

        // ------------------------------------------------------------
        // プロローグ
        // ------------------------------------------------------------

        private void HandleNewPlanetStarted(NewGameSettings settings)
        {
            if (Game == null || Game.Planet == null || !IsAuthority) return;
            expectingPrologue = false;
            bool arrival = Game.Planet.planetIndex > 1 || !string.IsNullOrEmpty(Game.Planet.previousWorldId);
            if (settings != null && settings.SkipPrologue)
            {
                CompletePrologue(true, true);
                return;
            }
            prologue = new PrologueSequence(this, arrival);
            prologue.Start();
        }

        /// <summary>プロローグを終える（演出の最後・スキップ・途中ロード）。</summary>
        internal void CompletePrologue(bool skipped, bool save)
        {
            if (state.prologueDone) return;
            state.prologueDone = true;
            if (!state.prologueFoodGiven)
            {
                state.prologueFoodGiven = true;
                if (LocalPlayer != null) LocalPlayer.GiveOrDrop(new ItemStack(PrologueFoodItemId(), 1), "story.prologue");
            }
            int day = Clock != null ? Clock.Day : 1;
            state.lastGuideVisitDay = day;
            state.guideVisitPending = false;
            if (state.chapter < StoryTuning.Chapter1) SetChapter(StoryTuning.Chapter1);
            MarkDirty();
            if (save && Game != null) Game.SaveGame(false);
        }

        internal void MarkPrologueFoodGiven() => state.prologueFoodGiven = true;
        internal bool PrologueFoodGiven => state.prologueFoodGiven;

        /// <summary>
        /// 料理好きが差し出す料理：他モジュール（Cooking）の料理があれば安いものを決定的に選ぶ。無ければストーリーの差し入れ。
        /// </summary>
        internal string PrologueFoodItemId()
        {
            ItemDefinition pick = null;
            foreach (var d in ItemRegistry.ByCategory(ItemCategory.Food))
            {
                if (d == null || d.SourceActivity == "story") continue;
                if (d.BasePrice <= 0 || d.BasePrice > 200) continue;
                if (pick == null || d.BasePrice < pick.BasePrice || (d.BasePrice == pick.BasePrice && string.CompareOrdinal(d.Id, pick.Id) < 0)) pick = d;
            }
            return pick != null ? pick.Id : StoryTuning.ItemWelcomeSnack;
        }

        // ------------------------------------------------------------
        // 章（明示表示しない体験の流れ）
        // ------------------------------------------------------------

        private int Today => Clock != null ? Clock.Day : 1;

        private void OnMovedIn(ResidentMovedInEvent e)
        {
            if (!IsAuthority || !state.prologueDone) return;
            state.SetMilestoneOnce("first_move_in", e.Day > 0 ? e.Day : Today);
            EvaluateChapter();
        }

        private void OnMovedOut(ResidentMovedOutEvent e)
        {
            if (!IsAuthority || !state.prologueDone) return;
            state.SetMilestoneOnce("first_move_out", e.Day > 0 ? e.Day : Today);
            EvaluateChapter();
        }

        private void OnRepaired(RocketDeviceRepairedEvent e)
        {
            if (!IsAuthority) return;
            state.SetMilestoneOnce("first_repair", Today);
            EvaluateChapter();
        }

        private void OnLaunchReady(RocketLaunchReadyEvent e)
        {
            if (!IsAuthority) return;
            state.SetMilestoneOnce("launch_ready", Today);
            Game.Flags?.Set("story.launch_ready", 1);
            MarkDirty();
        }

        private bool IsLocal(string playerId) => string.IsNullOrEmpty(playerId) || (LocalPlayer != null && playerId == LocalPlayer.PlayerId);

        private void OnShopSold(ShopItemSoldEvent e)
        {
            if (!IsAuthority || !IsLocal(e.ShopOwnerId)) return;
            if (!string.IsNullOrEmpty(e.ResidentId)) StoryPlanetState.Add(state.shopPurchases, e.ResidentId, 1);
            StoryPlanetState.Add(state.activity, "sold", 1);
        }

        private void OnFish(FishCaughtEvent e) { if (IsAuthority && IsLocal(e.PlayerId)) StoryPlanetState.Add(state.activity, "fish", 1); }
        private void OnCrop(CropHarvestedEvent e) { if (IsAuthority && IsLocal(e.PlayerId)) StoryPlanetState.Add(state.activity, "crop", Mathf.Max(1, e.Count)); }
        private void OnOre(OreMinedEvent e) { if (IsAuthority && IsLocal(e.PlayerId)) StoryPlanetState.Add(state.activity, "ore", 1); }
        private void OnCreature(CreatureCaughtEvent e) { if (IsAuthority && IsLocal(e.PlayerId)) StoryPlanetState.Add(state.activity, "creature", 1); }
        private void OnCraft(ItemCraftedEvent e) { if (IsAuthority && IsLocal(e.PlayerId)) StoryPlanetState.Add(state.activity, "craft", Mathf.Max(1, e.Count)); }

        private void OnGift(GiftGivenEvent e)
        {
            if (!IsAuthority || !IsLocal(e.PlayerId) || string.IsNullOrEmpty(e.ResidentId)) return;
            StoryPlanetState.Add(state.giftsGiven, e.ResidentId, 1);
        }

        /// <summary>章の判定。1回の判定で進むのは1段階だけ（体験の流れを飛ばさない）。</summary>
        private void EvaluateChapter()
        {
            if (!IsAuthority || !state.prologueDone || IsEndingInProgress) return;
            int day = Today;
            bool ok = false;
            switch (state.chapter)
            {
                case StoryTuning.Chapter1:
                    ok = state.HasMilestone("first_move_in");
                    break;
                case StoryTuning.Chapter2:
                    ok = day - state.GetMilestone("chapter2") >= StoryTuning.Chapter3MinDaysAfterChapter2
                         && day >= StoryTuning.Chapter3MinDaysTotal
                         && MaxFriendship() >= StoryTuning.Chapter3FriendshipThreshold;
                    break;
                case StoryTuning.Chapter3:
                    ok = state.HasMilestone("first_move_out");
                    break;
                case StoryTuning.Chapter4:
                    ok = state.HasMilestone("first_repair");
                    break;
            }
            if (ok) SetChapter(state.chapter + 1);
        }

        private int MaxFriendship()
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || LocalPlayer == null) return 0;
            int best = 0;
            try
            {
                foreach (var r in dir.ActiveResidents)
                    if (r != null) best = Mathf.Max(best, r.GetFriendship(LocalPlayer.PlayerId));
            }
            catch (Exception e) { Debug.LogException(e); }
            return best;
        }

        internal void SetChapter(int chapter)
        {
            if (chapter <= state.chapter && state.chapter != 0) return;
            state.chapter = chapter;
            int day = Today;
            state.SetMilestoneOnce("chapter" + chapter, day);
            if (Game.Flags != null)
            {
                Game.Flags.Set("story.chapter", chapter);
                Game.Flags.Set("story.chapter" + chapter + ".day", day);
            }
            // 章が変わった時の小さな演出：案内役が様子を見に来て、いつもの言葉をかける（第2〜4章に入った時）
            if (chapter >= StoryTuning.Chapter2 && chapter <= StoryTuning.Chapter4)
                state.guideVisitPending = true;
            MarkDirty();
            Debug.Log($"[Story] 章が進みました: {chapter}（{day}日目）");
        }

        // ------------------------------------------------------------
        // 日次・毎フレーム
        // ------------------------------------------------------------

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority || Game.Planet == null) return;
            EvaluateChapter();
            if (state.prologueDone && state.chapter >= StoryTuning.Chapter1 && state.chapter <= StoryTuning.GuideVisitLastChapter
                && day - state.lastGuideVisitDay >= StoryTuning.GuideVisitIntervalDays)
            {
                state.guideVisitPending = true;
            }
            UpdateJourneyEntry(false);
            MarkDirty();
        }

        public override void Tick(float deltaTime)
        {
            if (!IsAuthority || Game.State != GameState.Playing || !state.prologueDone || IsCutsceneRunning) return;
            guideCheckTimer -= deltaTime;
            if (guideCheckTimer > 0f) return;
            guideCheckTimer = 0.5f;
            UpdateGuideCare();
        }

        /// <summary>第1章「一人の宇宙人から世話を焼かれる」：案内役が食べ物を持ってきて、いつもの言葉をかける。</summary>
        private void UpdateGuideCare()
        {
            if (!state.guideVisitPending || LocalPlayer == null || Clock == null) return;
            if (LocalPlayer.CurrentAreaId != AreaIds.Overworld || UIManager.AnyModalOpen) return;
            int hour = Clock.Hour;
            if (hour < StoryTuning.GuideVisitFromHour || hour >= StoryTuning.GuideVisitToHour) return;
            var guide = GuideInfo();
            if (guide == null) return;
            float d = StoryActors.Distance(guide.Id, LocalPlayer.Position);
            if (d > StoryTuning.GuideNoticeDistance)
            {
                guideApproaching = false;
                return;
            }
            if (d > StoryTuning.GuideTalkDistance)
            {
                if (!guideApproaching || UnityEngine.Time.time - guideApproachTime > 6f)
                {
                    guideApproaching = true;
                    guideApproachTime = UnityEngine.Time.time;
                    StoryActors.GoTo(guide.Id, LocalPlayer.Position + LocalPlayer.Forward * 1.5f, 30, "story.guide_care");
                }
                return;
            }
            // 話しかける
            guideApproaching = false;
            state.guideVisitPending = false;
            state.lastGuideVisitDay = Clock.Day;
            StoryActors.Face(guide.Id, LocalPlayer.Position);
            var dir = Game.Service<IResidentDirectory>();
            try
            {
                dir?.Speak(guide.Id, StoryUtterances.GuideCareVisit(guide.Id, GuideCatchphraseConcept, StoryTuning.ItemGuideSnack));
                dir?.AddMemory(guide.Id, new ResidentMemoryEntry { kind = "gave_food", playerId = LocalPlayer.PlayerId, itemId = StoryTuning.ItemGuideSnack, day = Clock.Day, importance = 0.4f });
            }
            catch (Exception e) { Debug.LogException(e); }
            LocalPlayer.GiveOrDrop(new ItemStack(StoryTuning.ItemGuideSnack, 1), "story.guide_care");
            UIManager.Toast(Loc.Format("{0}をもらった", "Received: {0}", ItemRegistry.NameOf(StoryTuning.ItemGuideSnack)));
            MarkDirty();
        }

        // ------------------------------------------------------------
        // エンディング後（基本決定 §5）
        // ------------------------------------------------------------

        /// <summary>エンディングの本編（見送り〜クレジット）が終わった時に呼ばれる。</summary>
        internal void OnEndingPresented(EndingRecord record)
        {
            state.lastEnding = record;
            state.endingCount++;
            if (state.chapter < StoryTuning.ChapterFinal) state.chapter = StoryTuning.ChapterFinal;
            state.SetMilestoneOnce("chapter" + StoryTuning.ChapterFinal, Today);
            state.SetMilestoneOnce("first_ending", Today);
            if (Game.Flags != null)
            {
                Game.Flags.Set("story.chapter", state.chapter);
                Game.Flags.Set("story.ending_seen", state.endingCount);
            }
            MarkDirty();
        }

        internal int EndingCount => state.endingCount;

        /// <summary>A「ロケット前へ戻り、この星で生活を続ける」。強制終了しない。</summary>
        internal void ChooseReturnBeforeLaunch()
        {
            EventBus.Publish(new EndingFinishedEvent { Choice = PostEndingChoice.ReturnBeforeLaunch });
            if (ending != null) ending.ReturnToRocketFront();
            else
            {
                // 念のため：演出が無くても操作できる状態へ戻す（入力ロック・HUD・時間・状態）
                UIManager.CloseAll();
                if (LocalPlayer != null) LocalPlayer.UnlockInput("story");
                Clock?.RemoveGlobalPause("story.ending");
                Clock?.RemoveClockHold("story.ending");
                UIManager.HudVisible = true;
                Game.SetState(GameState.Playing);
                ScreenFader.FadeIn(1f);
            }
            ending = null;
            UpdateJourneyEntry(false);
            MarkDirty();
            Game.SaveGame(false);
            UIManager.Toast(Loc.T("ロケットの前に戻ってきた。この星での暮らしは続いていく。", "You are back in front of the rocket. Life on this planet goes on."), 6f);
        }

        /// <summary>B「次の星へ進む」。現在の星をセーブしてから、新しい星を始める（元の星のセーブは残る）。</summary>
        internal void ChooseNextPlanet(CarryOverPlan plan)
        {
            if (plan == null) plan = new CarryOverPlan();
            EventBus.Publish(new EndingFinishedEvent { Choice = PostEndingChoice.NextPlanet });
            state.departedToNextPlanet = true;
            UpdateJourneyEntry(true);
            // 見送りのために止めていた住民の生活AIを、この星のセーブより先に戻す（止めたまま保存しない）
            ending?.ReleaseResidents();
            MarkDirty();
            Game.SaveGame(false);

            var planet = Game.Planet;
            var appearance = plan.keepAppearance || plan.newAppearance == null
                ? (LocalPlayer != null ? LocalPlayer.Appearance.Clone() : null)
                : plan.newAppearance.Clone();
            PendingCarry = plan;
            ending?.Abort();
            ending = null;
            UIManager.HudVisible = true;
            Game.StartNewPlanet(new NewGameSettings
            {
                SeedText = plan.seedText,
                Appearance = appearance,
                PlanetNickname = string.IsNullOrWhiteSpace(plan.nickname) ? null : plan.nickname.Trim(),
                SkipPrologue = !plan.shortPrologue,
                // 持ち物の取捨は OnNewPlanet で CarryOverPlan に従って行う（コアの KeepProfile は全部か無しかのため）
                KeepProfile = true,
                KeepSkills = plan.keepSkills,
                KeepCompanions = plan.keepCompanions,
                PlanetIndex = (planet != null ? planet.planetIndex : 1) + 1,
                PreviousWorldId = planet != null ? planet.worldId : null
            });
        }

        /// <summary>引き継ぎ設定の適用（ローカルプレイヤー自身の持ち物なので直接変更してよい）。</summary>
        private void ApplyCarryOver(CarryOverPlan plan)
        {
            var p = LocalPlayer;
            if (p == null) return;
            if (!plan.keepItems)
            {
                var keep = new List<ItemStack>();
                foreach (var s in p.Inventory.NonEmpty())
                {
                    var cat = s.Definition.Category;
                    if (Array.IndexOf(StoryTuning.KeepsakeCategories, cat) >= 0) keep.Add(s.Clone());
                }
                p.Inventory.Clear();
                foreach (var s in keep) p.Inventory.Add(s, "story.keepsake");
                p.SelectedSlot = -1;
            }
            if (!plan.keepMoney) p.Wallet.money = PlayerSystem.StarterMoney;
            if (!plan.keepTools)
            {
                p.Tools.levels.Clear();
                foreach (var kv in ToolBelt.CreateStarter().levels) p.Tools.levels.Add(new StringIntPair(kv.key, kv.value));
                p.Tools.equipped = ToolType.Hand;
            }
        }

        // ------------------------------------------------------------
        // 思い出（タブレット "memories"）
        // ------------------------------------------------------------

        /// <summary>エンディングの最後（宇宙船の窓・辞書・クレジット）をもう一度見る。</summary>
        internal void PlayMemoriesReel()
        {
            if (IsCutsceneRunning || Game.State != GameState.Playing) return;
            var tablet = Game.Service<ITabletService>();
            if (tablet != null && tablet.IsOpen) tablet.Close();
            UIManager.CloseAll();
            reel = new MemoriesReel(this);
            reel.Start();
        }

        internal IEnumerable<KeyValuePair<string, int>> Milestones()
        {
            foreach (var p in state.milestones) if (p != null) yield return new KeyValuePair<string, int>(p.key, p.value);
        }

        internal string TopActivity()
        {
            string best = null;
            int bestV = 0;
            foreach (var p in state.activity)
            {
                if (p == null) continue;
                if (p.value > bestV)
                {
                    bestV = p.value;
                    best = p.key;
                }
            }
            return best;
        }

        internal int ShopPurchasesBy(string residentId) => StoryPlanetState.Find(state.shopPurchases, residentId);
        internal int GiftsGivenTo(string residentId) => StoryPlanetState.Find(state.giftsGiven, residentId);

        // ------------------------------------------------------------
        // 旅の記録（星をまたぐ）
        // ------------------------------------------------------------

        private void UpdateJourneyEntry(bool departed)
        {
            if (Game == null || Game.Planet == null || WorldAuthority.IsGuest) return;
            var planet = Game.Planet;
            JourneyEntry entry = null;
            foreach (var j in profile.journey) if (j != null && j.worldId == planet.worldId) entry = j;
            if (entry == null)
            {
                entry = new JourneyEntry { worldId = planet.worldId, seedText = planet.seedText, planetIndex = planet.planetIndex };
                profile.journey.Add(entry);
            }
            string name = planet.playerGivenName;
            if (string.IsNullOrEmpty(name))
            {
                var speech = Game.Service<ISpeechService>();
                try { name = speech?.PlanetName; } catch (Exception) { }
            }
            entry.planetName = name;
            entry.daysStayed = Clock != null ? Clock.Day : entry.daysStayed;
            if (departed) entry.departed = true;
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null && LocalPlayer != null)
            {
                int met = 0;
                try
                {
                    foreach (var r in dir.ActiveResidents) if (r != null && r.Knows(LocalPlayer.PlayerId)) met++;
                    foreach (var r in dir.FormerResidents) if (r != null && r.Knows(LocalPlayer.PlayerId)) met++;
                }
                catch (Exception) { }
                entry.residentsMet = Mathf.Max(entry.residentsMet, met);
            }
        }

        internal void SavePreset(string label, CharacterAppearance a)
        {
            if (a == null) return;
            profile.presets.Add(new AppearancePreset { label = label, appearance = a.Clone() });
            while (profile.presets.Count > 6) profile.presets.RemoveAt(0);
        }

        // ------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(state);
                case SaveScope.PlayerProfile:
                    if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                    return JsonUtility.ToJson(profile);
                default:
                    return null;
            }
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                if (scope == SaveScope.Planet)
                {
                    var s = JsonUtility.FromJson<StoryPlanetState>(json);
                    if (s == null) return;
                    if (s.milestones == null) s.milestones = new List<StringIntPair>();
                    if (s.shopPurchases == null) s.shopPurchases = new List<StringIntPair>();
                    if (s.activity == null) s.activity = new List<StringIntPair>();
                    if (s.giftsGiven == null) s.giftsGiven = new List<StringIntPair>();
                    if (string.IsNullOrEmpty(s.catchphraseConcept)) s.catchphraseConcept = preparedCatchphrase;
                    state = s;
                    guideCacheAllowed = true;
                }
                else if (scope == SaveScope.PlayerProfile && LocalPlayer != null && playerId == LocalPlayer.PlayerId)
                {
                    var p = JsonUtility.FromJson<StoryProfileState>(json);
                    if (p == null) return;
                    if (p.journey == null) p.journey = new List<JourneyEntry>();
                    if (p.presets == null) p.presets = new List<AppearancePreset>();
                    profile = p;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
