using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Quests
{
    /// <summary>
    /// 住民固有の個別ストーリー（§9、住民仕様 §10、メインストーリー 第3章）。
    /// ・話数や構成は住民ごとに違ってよい（§9.1）。
    /// ・参加しなくても住民の人生は進む（§10、住民 §3）。不正解で物語が消えない（§1.2）。
    /// ・住民が引っ越しても破綻しない（§14）。記録は依頼帳に残る。
    /// ・進行はプレイヤーごと（§17【未決定】→ 個人の関係として扱う【仮】）。暮らしの変化（花・作品）は星で共通。
    /// 実行場所：GetTalk / OnGesture / LocalCheck / LocalFish はその場の画面（ローカル）、それ以外はホスト。
    /// </summary>
    public abstract class ResidentStory
    {
        public abstract string Id { get; }
        public virtual int EarliestDay => 3;

        /// <summary>この物語を始められるなら状態を作る（主人公・相手の住民を選ぶ）。</summary>
        public abstract StoryState TryCreate(QuestSystem sys, string playerId, int day);

        public virtual void OnDayStarted(QuestSystem sys, StoryState st, int day) { }
        public virtual void OnHour(QuestSystem sys, StoryState st, int day, int hour) { }
        public virtual void OnMinutes(QuestSystem sys, StoryState st) { }
        public virtual Utterance GetTalk(QuestSystem sys, StoryState st, string residentId) => null;
        public virtual bool OnGesture(QuestSystem sys, StoryState st, string residentId, PlayerGesture g) => false;
        public virtual void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx) { }
        public virtual void OnQuestAccepted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx) { }
        public virtual void OnQuestDeclined(QuestSystem sys, StoryState st, QuestInstance q) { }
        public virtual void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx) { }
        public virtual Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId) => null;
        public virtual bool OnProgress(QuestSystem sys, StoryState st, string kind, string itemId, string speciesId, float size) => false;
        public virtual void OnShopSold(QuestSystem sys, StoryState st, ShopItemSoldEvent e) { }
        public virtual void OnRocketRepaired(QuestSystem sys, StoryState st, RocketDeviceRepairedEvent e) { }
        public virtual void OnWeather(QuestSystem sys, StoryState st, WeatherChangedEvent e) { }
        public virtual void OnMoveOutDecided(QuestSystem sys, StoryState st, string residentId) { }
        public virtual void OnResidentMovedOut(QuestSystem sys, StoryState st, string residentId)
        {
            // 既定：物語はそこで静かに終わる（記録は依頼帳に残る）
            var q = sys.FindQuest(st.questId);
            if (q != null && q.IsOpen) { sys.SetStatus(q, QuestStatus.Expired); q.outcome = 3; }
            Finish(sys, st, 5);
        }
        public virtual void OnEventFinished(QuestSystem sys, StoryState st, VillageEvent ev) { }
        public virtual void OnAttended(QuestSystem sys, StoryState st, VillageEvent ev, CommandContext ctx) { }
        public virtual Utterance PendingTalk(QuestSystem sys, StoryState st, PendingTalk p) => null;
        public virtual void LocalCheck(QuestSystem sys, StoryState st) { }
        public virtual void LocalFish(QuestSystem sys, StoryState st, FishCaughtEvent e) { }

        // ---- 共通の補助 ----

        /// <summary>その場で受け付ける身振りの時間（ローカル）。</summary>
        protected float offerTime = -999f;
        protected bool OfferOpen => UnityEngine.Time.realtimeSinceStartup - offerTime < QuestTuning.OfferResponseSeconds;
        protected void OpenOffer() => offerTime = UnityEngine.Time.realtimeSinceStartup;
        protected void CloseOffer() => offerTime = -999f;

        protected static void Advance(QuestSystem sys, StoryState st, int stage)
        {
            st.stage = stage;
            st.stageDay = sys.Today;
            st.stageMinute = sys.TotalMinutes;
            st.talkedThisStage = false;
            st.questId = null;
        }

        protected static void Finish(QuestSystem sys, StoryState st, int outcome)
        {
            if (st.finished) return;
            st.finished = true;
            if (st.outcome == 0) st.outcome = outcome;
            EventBus.Publish(new QuestStateChangedEvent { QuestId = "story:" + st.storyId, ResidentId = st.residentId, Status = QuestStatus.Completed, PlayerId = st.playerId });
        }

        protected void Send(string action, string arg = null, float value = 0f)
        {
            WorldAuthority.Submit(new QuestStoryCommand { storyId = Id, action = action, arg = arg, value = value });
        }

        /// <summary>「話した」をホストへ伝え、ローカルにも反映（同じ場面を繰り返さない）。</summary>
        protected void MarkTalked(StoryState st)
        {
            st.talkedThisStage = true;
            Send("talked");
        }

        protected static bool QuestOpen(QuestSystem sys, StoryState st)
        {
            var q = sys.FindQuest(st.questId);
            return q != null && q.IsOpen;
        }

        protected static QuestInstance Quest(QuestSystem sys, StoryState st) => sys.FindQuest(st.questId);

        protected static bool UsedInOtherStory(QuestSystem sys, string playerId, string residentId)
        {
            foreach (var s in sys.S.stories)
                if (s.playerId == playerId && (s.residentId == residentId || s.partnerResidentId == residentId)) return true;
            return false;
        }

        protected static IResidentInfo PickByJob(QuestSystem sys, string playerId, params ResidentJob[] jobs)
        {
            var all = sys.ActiveResidents();
            foreach (var job in jobs)
                foreach (var r in all)
                    if (r.Job == job && !UsedInOtherStory(sys, playerId, r.Id)) return r;
            return null;
        }

        protected static IResidentInfo PickByLineage(QuestSystem sys, string playerId, params AlienLineage[] lineages)
        {
            var all = sys.ActiveResidents();
            foreach (var l in lineages)
                foreach (var r in all)
                    if (r.Lineage == l && !UsedInOtherStory(sys, playerId, r.Id)) return r;
            return null;
        }

        protected Utterance U(string residentId) => new Utterance(residentId, QuestSpeech.TagStory);

        protected void AttachLocal(QuestSystem sys, StoryState st, UtteranceRecord rec, string scene)
        {
            if (st.playerId != sys.LocalId) return;
            sys.BookAttach(sys.BookNew(QuestSystem.StoryBookId(Id), QuestKind.Story, st.residentId, null), rec, scene);
        }

        protected void AwaitLocal(QuestSystem sys, StoryState st, string speakerId, string scene)
        {
            if (st.playerId != sys.LocalId) return;
            sys.Await(speakerId, QuestSpeech.TagStory, QuestSystem.StoryBookId(Id), scene, st.residentId, QuestKind.Story);
        }

        protected static bool LocalNear(QuestSystem sys, Vector3 pos, float radius)
        {
            var p = sys.LocalPlayerState;
            return p != null && p.Avatar != null && QuestSystem.Flat(p.Position - pos) < radius;
        }
    }

    // =====================================================================
    // 青い花（§9.2）：花を育てる年配の住民と、昔からの友達。
    //   0 見かける → 1 ためらい → 2 小さなお願い → 3 届けた花 → 4 花の意味
    //   プレイヤーの「花が好き」というメモは間違いかもしれない。ゲームは訂正しない。
    // =====================================================================
    public sealed class BlueFlowerStory : ResidentStory
    {
        public override string Id => "blue_flower";
        public override int EarliestDay => 3;

        public override StoryState TryCreate(QuestSystem sys, string playerId, int day)
        {
            var florist = PickByJob(sys, playerId, ResidentJob.Florist, ResidentJob.Retired, ResidentJob.FarmingMaster);
            if (florist == null) return null;
            var others = sys.ActiveResidents();
            others.RemoveAll(r => r.Id == florist.Id || UsedInOtherStory(sys, playerId, r.Id));
            if (others.Count == 0) return null;
            // 昔からの友達：決定的に選ぶ（内部の関係データが契約に無いため【仮】）
            others.Sort((a, b) => StableHash.Derive(sys.Seed, "bf:" + a.Id).CompareTo(StableHash.Derive(sys.Seed, "bf:" + b.Id)));
            var st = new StoryState { residentId = florist.Id, partnerResidentId = others[0].Id, itemId = QuestItemIds.BlueBouquet };
            return st;
        }

        public override void OnHour(QuestSystem sys, StoryState st, int day, int hour)
        {
            var florist = sys.R(st.residentId);
            var partner = sys.R(st.partnerResidentId);
            if (florist == null || partner == null) return;
            if (st.stage == 0 || st.stage == 1)
            {
                // 商店で青い花を買う（§9.2 第1話）。主人公の店に青い花があれば実際に買っていく
                var shop = sys.Game.Service<IShopService>();
                if (shop != null && shop.IsOpen && shop.OfferCount > 0)
                {
                    // 11時の来店処理まで店の前に留まる（10:50 で解くと、11時には店から離れた場所で来店処理をしてしまう）
                    if (hour == 10) sys.GoTo(st.residentId, shop.EntrancePosition, 11 * 60 + 10, "story.blue_flower.shop");
                    if (hour == 11 && !st.HasFlag("shop:" + day))
                    {
                        st.SetFlag("shop:" + day);
                        shop.ResidentVisit(florist, new ShoppingIntent { need = ShoppingNeed.Gift, budget = 400, preferredTag = ItemTags.ColorBlue, forResidentId = st.partnerResidentId, reasonTag = "gift" });
                    }
                }
            }
            if (st.stage == 1 && hour == 17)
            {
                // 友達の家の前まで行って、ためらって帰る（第2話）
                sys.GoTo(st.residentId, sys.HomeOf(partner) + new Vector3(2.5f, 0f, 1.5f), 17 * 60 + 50, "story.blue_flower.hesitate");
            }
            if (st.stage == 2 && hour == 16 && st.HasFlag("self_deliver") && !st.HasFlag("self_done"))
            {
                // 主人公が関わらなかった：自分で届けに行く（§10 不参加でも人生は進む）
                st.SetFlag("self_done");
                sys.GoTo(st.residentId, sys.HomeOf(partner) + new Vector3(1.2f, 0f, 0.6f), 17 * 60, "story.blue_flower.deliver");
                sys.GoTo(st.partnerResidentId, sys.HomeOf(partner), 17 * 60, "story.blue_flower.deliver");
                sys.Say(st.residentId, U(st.residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)).Recipient(Concepts.YOU))
                    .Face(Expression.Shy).With(Gesture.OfferItem, st.itemId), SpeechChannel.Overheard);
                sys.Say(st.partnerResidentId, U(st.partnerResidentId)
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                    .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS))
                    .Face(Expression.Love).With(Gesture.Hug), SpeechChannel.Overheard);
                sys.Memory(st.partnerResidentId, "received_flower", null, st.itemId, st.residentId, 0.9f);
                sys.Memory(st.residentId, "gave_flower", null, st.itemId, st.partnerResidentId, 0.9f);
                sys.AddLifeMark("blue_flowers", st.partnerResidentId, st.itemId, sys.HomeOf(partner), new Color(0.35f, 0.55f, 1f));
                st.outcome = 3;
                Advance(sys, st, 3);
            }
            if (st.stage == 3 && hour == 15 && sys.Today - st.stageDay < 3)
            {
                // その後、二人が一緒に過ごすことが増える（第4話）
                var spot = sys.FacilityPos(FacilityType.Bench, sys.PlazaPos());
                sys.GoTo(st.residentId, spot + new Vector3(0.7f, 0f, 0f), 16 * 60 + 30, "story.blue_flower.together");
                sys.GoTo(st.partnerResidentId, spot + new Vector3(-0.7f, 0f, 0f), 16 * 60 + 30, "story.blue_flower.together");
            }
        }

        public override void OnMinutes(QuestSystem sys, StoryState st)
        {
            if (st.stage != 1) return;
            int m = sys.Minute;
            string key = "said:" + sys.Today;
            if (m >= 17 * 60 + 30 && m < 17 * 60 + 45 && !st.HasFlag(key))
            {
                st.SetFlag(key);
                sys.Say(st.residentId, U(st.residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)).Recipient(Concepts.Name(st.partnerResidentId)))
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM))
                    .Face(Expression.Shy).With(Gesture.Hold).With(Gesture.Think), SpeechChannel.Overheard);
            }
        }

        public override void LocalCheck(QuestSystem sys, StoryState st)
        {
            if (st.stage != 1 || st.playerId != sys.LocalId) return;
            int m = sys.Minute;
            if (m < 17 * 60 + 28 || m > 17 * 60 + 45) return;
            var florist = sys.R(st.residentId);
            if (florist == null || !LocalNear(sys, florist.Position, 22f)) return;
            string key = "witness:" + sys.Today;
            if (st.HasFlag(key)) return;
            st.SetFlag(key);
            Send("witness");
            AwaitLocal(sys, st, st.residentId, BookScene.Overheard);
        }

        public override void OnDayStarted(QuestSystem sys, StoryState st, int day)
        {
            switch (st.stage)
            {
                case 0:
                    if (day - st.stageDay >= 2) Advance(sys, st, 1);
                    break;
                case 1:
                    if (day - st.stageDay >= 1)
                    {
                        Advance(sys, st, 2);
                        var goal = new QuestGoal { type = GoalType.DeliverToResident, itemId = QuestItemIds.BlueBouquet, targetResidentId = st.partnerResidentId, count = 1 };
                        // 理由は言わない（花の意味は最後に分かる：§9.2 最終話）
                        sys.CreateStoryQuest(st, st.residentId, goal, null, null, true, st.partnerResidentId, Expression.Shy, 120);
                    }
                    break;
                case 2:
                {
                    var q = Quest(sys, st);
                    bool waiting = q == null || q.status == QuestStatus.Hinted || q.status == QuestStatus.Declined;
                    if (waiting && day - st.stageDay >= QuestTuning.StoryNoParticipationDays)
                    {
                        if (q != null && q.IsOpen) { sys.SetStatus(q, QuestStatus.Expired); q.outcome = 3; }
                        st.SetFlag("self_deliver");
                    }
                    break;
                }
                case 3:
                    if (day - st.stageDay >= 3)
                    {
                        if (st.outcome == 3) Finish(sys, st, 3); // 主人公は関わらなかったが、二人の暮らしは続く
                        else if (sys.FriendshipOf(st.residentId, st.playerId) >= QuestTuning.StoryFriendshipForEpilogue || day - st.stageDay >= 8) Advance(sys, st, 4);
                    }
                    break;
            }
        }

        public override Utterance GetTalk(QuestSystem sys, StoryState st, string residentId)
        {
            if (st.talkedThisStage) return null;
            if (st.stage == 0 && residentId == st.residentId)
            {
                MarkTalked(st);
                // 青い花を大事そうに見せる。好きだから？ 誰かのため？（理由は言わない）
                return U(residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)))
                    .Face(Expression.Shy).With(Gesture.OfferItem, st.itemId).With(Gesture.Hold).Related(st.itemId);
            }
            if (st.stage == 3 && residentId == st.partnerResidentId && st.outcome != 3)
            {
                MarkTalked(st);
                return U(residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).Adverb(Concepts.VERY))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MEET).Agent(Concepts.WE).Adverb(Concepts.AGAIN).When(Concepts.EVERY_DAY))
                    .Face(Expression.Happy).With(Gesture.PointAt, st.residentId);
            }
            if (st.stage == 4 && residentId == st.residentId)
            {
                MarkTalked(st);
                Send("finale");
                var why = MeaningFrameLite.Make(Concepts.REMEMBER, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.FRIEND));
                why.timeWord = Concepts.LONG_AGO;
                return U(residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)).Recipient(Concepts.Name(st.partnerResidentId)).Past().Because(why))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FRIEND).Agent(Concepts.Name(st.partnerResidentId)).Adverb(Concepts.ALWAYS))
                    .Face(Expression.Happy).With(Gesture.Bow).With(Gesture.PointAt, st.partnerResidentId);
            }
            return null;
        }

        public override Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId)
        {
            if (residentId != st.partnerResidentId) return null;
            return U(residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.Name(st.residentId)).Patient(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)).Recipient(Concepts.I).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).When(Concepts.LONG_AGO))
                .Face(Expression.Love).With(Gesture.Hug).With(Gesture.PointAt, st.residentId);
        }

        public override void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage != 2) return;
            st.outcome = 1;
            sys.Memory(st.partnerResidentId, "received_flower", st.playerId, st.itemId, st.residentId, 0.9f);
            sys.Memory(st.residentId, "gave_flower", st.playerId, st.itemId, st.partnerResidentId, 0.9f);
            sys.Friendship(st.partnerResidentId, st.playerId, 3, "story.blue_flower");
            var partner = sys.R(st.partnerResidentId);
            if (partner != null) sys.AddLifeMark("blue_flowers", st.partnerResidentId, st.itemId, sys.HomeOf(partner), new Color(0.35f, 0.55f, 1f));
            Advance(sys, st, 3);
        }

        public override void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx)
        {
            if (action == "witness") sys.Memory(st.residentId, "seen_hesitating", ctx.IssuerId, st.itemId, st.partnerResidentId, 0.3f);
            if (action == "finale" && st.stage == 4 && !st.finished)
            {
                // お礼：花の種（あれば）
                string seed = null;
                foreach (var d in ItemRegistry.ByCategory(ItemCategory.Seed)) if (d.HasTag(ItemTags.Flower)) { seed = d.Id; break; }
                if (seed != null) ctx.GiveItem(new ItemStack(seed, 3), "story.blue_flower");
                else ctx.GiveItem(new ItemStack(SharedItemIds.GiftBox, 1), "story.blue_flower");
                sys.Friendship(st.residentId, ctx.IssuerId, 5, "story.blue_flower");
                sys.Memory(st.residentId, "shared_secret", ctx.IssuerId, st.itemId, st.partnerResidentId, 1f);
                Finish(sys, st, st.outcome == 0 ? 1 : st.outcome);
            }
        }

        public override void OnResidentMovedOut(QuestSystem sys, StoryState st, string residentId)
        {
            var q = Quest(sys, st);
            if (q != null && q.IsOpen) { sys.SetStatus(q, QuestStatus.Expired); q.outcome = 3; }
            if (residentId == st.partnerResidentId && st.stage < 3)
            {
                // 友達が先に引っ越した：渡せなかった花を、自分の庭に植える
                var florist = sys.R(st.residentId);
                if (florist != null)
                {
                    sys.AddLifeMark("blue_flowers", st.residentId, st.itemId, sys.HomeOf(florist), new Color(0.35f, 0.55f, 1f));
                    sys.AddPending(st.playerId, st.residentId, "story:" + Id, st.itemId, residentId);
                }
            }
            Finish(sys, st, 5);
        }

        public override Utterance PendingTalk(QuestSystem sys, StoryState st, PendingTalk p)
        {
            return U(p.residentId)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(Concepts.Name(p.otherResidentId)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PLANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FLOWER, Concepts.BLUE)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.Name(p.otherResidentId)).Adverb(Concepts.ALWAYS))
                .Face(Expression.Sad).With(Gesture.LookUp);
        }
    }

    // =====================================================================
    // 釣り名人（§9.3）：出会い → 釣り勝負 → 思い出の魚 → 技 → 釣りを続ける理由
    // =====================================================================
    public sealed class FishingMasterStory : ResidentStory
    {
        public override string Id => "fishing_master";
        public override int EarliestDay => 2;

        public override StoryState TryCreate(QuestSystem sys, string playerId, int day)
        {
            var master = PickByJob(sys, playerId, ResidentJob.FishingMaster);
            if (master == null) return null;
            var fish = QuestCatalog.Fish();
            if (fish.Count == 0) return null;
            string rare = fish[0];
            int best = -1;
            foreach (var f in fish)
            {
                var d = ItemRegistry.Get(f);
                if (d != null && d.Rarity > best) { best = d.Rarity; rare = f; }
            }
            return new StoryState { residentId = master.Id, itemId = rare };
        }

        private Vector3 SpotNear(QuestSystem sys, Vector3 from)
        {
            var l = sys.Layout;
            Vector3 best = from;
            float bd = float.MaxValue;
            if (l != null)
            {
                foreach (var s in l.FishingSpots)
                {
                    float d = QuestSystem.Flat(s.standPosition - from);
                    if (d < bd) { bd = d; best = s.standPosition; }
                }
            }
            return best;
        }

        public override void OnHour(QuestSystem sys, StoryState st, int day, int hour)
        {
            var m = sys.R(st.residentId);
            if (m == null) return;
            if (st.stage <= 1 && hour == 6)
                sys.GoTo(st.residentId, SpotNear(sys, sys.HomeOf(m)), 11 * 60, "story.fishing_master.morning");
        }

        public override Utterance GetTalk(QuestSystem sys, StoryState st, string residentId)
        {
            if (residentId != st.residentId) return null;
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    // 一緒に釣ろう（竿を振る身振りで誘う）
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CATCH_FISH).Agent(Concepts.WE).Adverb(Concepts.TOGETHER))
                        .Face(Expression.Happy).With(Gesture.Hold).With(Gesture.Beckon);
                case 1:
                    if (st.HasFlag("contest") || sys.Today <= st.stageDay) return null;
                    OpenOffer();
                    // 誰が大きい魚を釣るか？（勝負の誘い）
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CATCH_FISH).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).When(Concepts.NOW))
                        .Say(MeaningFrame.Make(SpeechAct.WhQuestion, Concepts.CATCH_FISH).Agent(Concepts.WHO).Patient(MeaningArg.Of(Concepts.FISH, Concepts.BIG)))
                        .Face(Expression.Excited).With(Gesture.Hold).With(Gesture.ShowNumber, null, 1).With(Gesture.Beckon);
                case 2:
                    if (QuestOpen(sys, st) || st.talkedThisStage || sys.Today <= st.stageDay) return null;
                    MarkTalked(st);
                    Send("ask_memory");
                    // 昔釣った大きな魚の話（絵を見せる）
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).Patient(QuestSpeech.ItemArg(st.itemId).Adj(Concepts.BIG)).Past().When(Concepts.LONG_AGO))
                        .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.SEE).Agent(Concepts.I).Patient(QuestSpeech.ItemArg(st.itemId)).Adverb(Concepts.AGAIN))
                        .Face(Expression.Love).With(Gesture.OfferItem, st.itemId).With(Gesture.LookUp).Related(st.itemId);
                case 3:
                    if (st.talkedThisStage || sys.Today <= st.stageDay) return null;
                    MarkTalked(st);
                    Send("teach");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.TEACH).Agent(Concepts.I).Recipient(Concepts.YOU).Patient(MeaningArg.Of(Concepts.FISHING_ROD)))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SLOW).Agent(MeaningArg.Of(Concepts.FISHING_ROD)).Adverb(Concepts.A_LITTLE))
                        .Face(Expression.Excited).With(Gesture.Hold).With(Gesture.Stretch);
                case 4:
                    if (st.talkedThisStage || sys.FriendshipOf(residentId, st.playerId) < QuestTuning.StoryFriendshipForEpilogue) return null;
                    MarkTalked(st);
                    Send("finale");
                    var why = MeaningFrameLite.Make(Concepts.REMEMBER, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.FAMILY));
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).Adverb(Concepts.ALWAYS).Because(why))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(MeaningArg.Of(Concepts.FAMILY).OwnedBy(Concepts.I)).Adverb(Concepts.TOGETHER).Past().When(Concepts.LONG_AGO))
                        .Face(Expression.Happy).With(Gesture.LookUp);
            }
            return null;
        }

        public override bool OnGesture(QuestSystem sys, StoryState st, string residentId, PlayerGesture g)
        {
            if (residentId != st.residentId || st.stage != 1 || !OfferOpen || st.HasFlag("contest")) return false;
            if (g == PlayerGesture.Nod || g == PlayerGesture.Happy || g == PlayerGesture.Clap)
            {
                CloseOffer();
                st.SetFlag("contest");
                Send("contest_accept");
                AttachLocal(sys, st, sys.Say(residentId, U(residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.OK))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.START).When(Concepts.NOW))
                    .Face(Expression.Excited).With(Gesture.Clap).With(Gesture.PointAt, "river")), BookScene.AfterNod);
                return true;
            }
            if (g == PlayerGesture.ShakeHead)
            {
                CloseOffer();
                AttachLocal(sys, st, sys.Say(residentId, QuestSpeech.Declined(residentId)), BookScene.AfterShake);
                return true;
            }
            return false;
        }

        public override void LocalFish(QuestSystem sys, StoryState st, FishCaughtEvent e)
        {
            if (st.stage != 0 || !st.talkedThisStage || st.HasFlag("together_sent")) return;
            var m = sys.R(st.residentId);
            if (m == null || !LocalNear(sys, m.Position, QuestTuning.FishingTogetherRadius)) return;
            st.SetFlag("together_sent");
            Send("fished_together");
            AttachLocal(sys, st, sys.Say(st.residentId, U(st.residentId)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.YOU).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Adverb(Concepts.VERY))
                .Face(Expression.Happy).With(Gesture.Clap)), BookScene.AfterHelp);
        }

        public override bool OnProgress(QuestSystem sys, StoryState st, string kind, string itemId, string speciesId, float size)
        {
            if (kind != "fish" || st.stage != 1 || !st.HasFlag("contest")) return false;
            if (sys.TotalMinutes - st.stageMinute > QuestTuning.FishingContestMinutes) return false;
            if (size > st.value2) { st.value2 = size; return true; }
            return false;
        }

        public override void OnMinutes(QuestSystem sys, StoryState st)
        {
            if (st.stage != 1 || !st.HasFlag("contest")) return;
            if (sys.TotalMinutes - st.stageMinute < QuestTuning.FishingContestMinutes) return;
            bool win = st.value2 > st.value;
            st.counter = st.value2 <= 0f ? 0 : (win ? 1 : 2);
            sys.Friendship(st.residentId, st.playerId, 3, "fishing_contest");
            sys.Memory(st.residentId, "fishing_contest", st.playerId, null, null, 0.8f, win ? "lost_to_player" : "won");
            sys.AddPending(st.playerId, st.residentId, "story:" + Id, st.itemId, null);
            Advance(sys, st, 2);
        }

        public override Utterance PendingTalk(QuestSystem sys, StoryState st, PendingTalk p)
        {
            var u = U(p.residentId);
            int masterSize = Mathf.RoundToInt(st.value);
            switch (st.counter)
            {
                case 1:
                    u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BIG).Agent(MeaningArg.Of(Concepts.FISH).OwnedBy(Concepts.YOU)).Adverb(Concepts.VERY))
                     .Face(Expression.Surprised).With(Gesture.Clap).With(Gesture.ShowNumber, null, masterSize);
                    break;
                case 2:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BIG).Agent(MeaningArg.Of(Concepts.FISH).OwnedBy(Concepts.I)))
                     .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CATCH_FISH).Agent(Concepts.WE).Adverb(Concepts.AGAIN).When(Concepts.SOMEDAY))
                     .Face(Expression.Happy).With(Gesture.ShowNumber, null, masterSize).With(Gesture.Jump);
                    break;
                default:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.YOU).Not().Past())
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.OK))
                     .Face(Expression.Happy).With(Gesture.Shrug).With(Gesture.ShowNumber, null, masterSize);
                    break;
            }
            return u;
        }

        public override Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId)
        {
            if (st.stage != 2 || residentId != st.residentId) return null;
            return U(residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SAME).Agent(MeaningArg.Of(Concepts.THIS)).When(Concepts.LONG_AGO))
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU).Adverb(Concepts.VERY))
                .Face(Expression.Love).With(Gesture.Hug).Related(st.itemId);
        }

        public override void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx)
        {
            switch (action)
            {
                case "fished_together":
                    if (st.stage != 0) return;
                    sys.Friendship(st.residentId, ctx.IssuerId, 3, "fished_together");
                    sys.Memory(st.residentId, "fished_together", ctx.IssuerId, null, null, 0.7f);
                    Advance(sys, st, 1);
                    break;
                case "contest_accept":
                {
                    if (st.stage != 1 || (st.HasFlag("contest") && st.value > 0f)) return;
                    st.SetFlag("contest");
                    st.stageMinute = sys.TotalMinutes;
                    var rng = sys.Rng("story.fishing.contest." + sys.Today);
                    var fish = QuestCatalog.Fish();
                    float typical = 0f;
                    foreach (var f in fish) typical = Mathf.Max(typical, sys.TypicalFishSize(f));
                    if (typical <= 0f) typical = 30f;
                    st.value = Mathf.Round(typical * rng.Range(0.7f, 1.2f));
                    st.value2 = 0f;
                    var m = sys.R(st.residentId);
                    if (m != null) sys.GoTo(st.residentId, SpotNear(sys, m.Position), sys.Minute + QuestTuning.FishingContestMinutes, "story.fishing_master.contest");
                    break;
                }
                case "ask_memory":
                    if (st.stage != 2 || QuestOpen(sys, st)) return;
                    sys.CreateStoryQuest(st, st.residentId, new QuestGoal { type = GoalType.DeliverItem, itemId = st.itemId, count = 1 },
                        Concepts.SEE, Concepts.FISH, true, null, Expression.Excited, 300);
                    break;
                case "teach":
                    if (st.stage != 3) return;
                    // 教わった技は、名人が引っ越した後も使える（§14）。釣りモジュールはこのフラグを参照できる
                    sys.Game.Flags.Set("quest.fishing_master.technique." + ctx.IssuerId, 1);
                    var bait = QuestCatalog.ByCategory(ItemCategory.Bait);
                    if (bait.Count > 0) ctx.GiveItem(new ItemStack(bait[0], 10), "story.fishing_master");
                    sys.Memory(st.residentId, "taught_technique", ctx.IssuerId, null, null, 0.8f);
                    ctx.Notify("名人の竿さばきを見て、こつが少し分かった気がする", "Watching the master, you feel you got the knack a little");
                    Advance(sys, st, 4);
                    break;
                case "finale":
                    if (st.stage != 4) return;
                    sys.Friendship(st.residentId, ctx.IssuerId, 5, "story.fishing_master");
                    sys.Memory(st.residentId, "shared_past", ctx.IssuerId, null, null, 1f);
                    Finish(sys, st, 1);
                    break;
            }
        }

        public override void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage != 2) return;
            var m = sys.R(st.residentId);
            if (m != null) sys.AddLifeMark("fish_trophy", st.residentId, st.itemId, sys.WorkplaceOf(m), ItemRegistry.GetOrPlaceholder(st.itemId).Color);
            Advance(sys, st, 3);
        }
    }

    // =====================================================================
    // 料理人になりたい住民（住民仕様 §10、メインストーリー 第3章「料理が大好きな住民」）
    //   0 練習 → 1 材料探し → 2 味見 → 3 みんなに振る舞う → 4 夢
    // =====================================================================
    public sealed class AspiringCookStory : ResidentStory
    {
        public override string Id => "aspiring_cook";
        public override int EarliestDay => 4;

        public override StoryState TryCreate(QuestSystem sys, string playerId, int day)
        {
            IResidentInfo who = null;
            foreach (var l in new[] { AlienLineage.Lifestyle, AlienLineage.Mochi, AlienLineage.Plush, AlienLineage.Round })
            {
                foreach (var r in sys.ActiveResidents())
                {
                    if (r.Lineage != l || r.Job == ResidentJob.Cook || UsedInOtherStory(sys, playerId, r.Id)) continue;
                    if (r.Job == ResidentJob.Blacksmith || r.Job == ResidentJob.Mechanic || r.Job == ResidentJob.FishingMaster) continue;
                    who = r;
                    break;
                }
                if (who != null) break;
            }
            if (who == null) who = PickByJob(sys, playerId, ResidentJob.Cook);
            if (who == null) return null;
            var rng = sys.Rng("story.cook." + who.Id);
            string crop = QuestCatalog.PickByPreference(who, QuestCatalog.Crops(), rng, day);
            string fish = QuestCatalog.PickByPreference(who, QuestCatalog.Fish(), rng, day);
            if (crop == null && fish == null) return null;
            return new StoryState { residentId = who.Id, itemId = crop, itemId2 = fish };
        }

        public override void OnHour(QuestSystem sys, StoryState st, int day, int hour)
        {
            var r = sys.R(st.residentId);
            if (r == null) return;
            if ((st.stage == 0 || st.stage == 1) && hour == 18)
            {
                // 家で料理の練習をしている（住民 §10）
                sys.GoTo(st.residentId, sys.FacilityPos(FacilityType.CommunityKitchen, sys.HomeOf(r)), 19 * 60 + 30, "story.cook.practice");
            }
        }

        private void MakeIngredientQuest(QuestSystem sys, StoryState st)
        {
            QuestGoal g1 = null, g2 = null;
            if (!string.IsNullOrEmpty(st.itemId)) g1 = new QuestGoal { type = GoalType.DeliverItem, itemId = st.itemId, count = 1 };
            if (!string.IsNullOrEmpty(st.itemId2)) g2 = new QuestGoal { type = GoalType.DeliverItem, itemId = st.itemId2, count = 1 };
            var first = g1 ?? g2;
            var q = sys.CreateStoryQuest(st, st.residentId, first, Concepts.COOK, Concepts.DISH, true, null, Expression.Excited, 150);
            if (g1 != null && g2 != null) q.goals.Add(g2);
        }

        public override void OnDayStarted(QuestSystem sys, StoryState st, int day)
        {
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage && day > st.stageDay) Advance(sys, st, 1);
                    else if (day - st.stageDay >= 3) Advance(sys, st, 1);
                    break;
                case 1:
                    if (Quest(sys, st) == null && !st.HasFlag("quest_made:" + st.counter))
                    {
                        st.SetFlag("quest_made:" + st.counter);
                        MakeIngredientQuest(sys, st);
                    }
                    else
                    {
                        var q = Quest(sys, st);
                        if (q != null && q.IsOpen && day - st.stageDay >= QuestTuning.StoryNoParticipationDays)
                        {
                            // 自分で材料を手に入れて作った（§8.1：主人公が関わらなければ別の方法で）
                            sys.SetStatus(q, QuestStatus.Expired);
                            q.outcome = 3;
                            st.outcome = 3;
                            sys.Memory(st.residentId, "solved_self", st.playerId, st.itemId, null, 0.4f, "ingredients");
                            Advance(sys, st, 2);
                        }
                    }
                    break;
                case 2:
                    if (day - st.stageDay >= 3) { st.SetFlag("taste_skipped"); StartParty(sys, st); }
                    break;
                case 3:
                    if (day - st.stageDay >= 3) Advance(sys, st, 4);
                    break;
            }
        }

        private void StartParty(QuestSystem sys, StoryState st)
        {
            Advance(sys, st, 3);
            var ev = sys.CreateEvent(VillageEventKind.CookParty, st.residentId, sys.Today + 1, 12 * 60, 14 * 60, sys.PlazaPos() + new Vector3(-3f, 0f, 2f));
            ev.storyPlayerId = st.playerId;
            sys.Announce(ev, sys.Today);
        }

        public override Utterance GetTalk(QuestSystem sys, StoryState st, string residentId)
        {
            if (residentId != st.residentId) return null;
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    Send("odd_dish");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Adverb(Concepts.EVERY_DAY))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DIFFICULT).Agent(MeaningArg.Of(Concepts.DISH)).Adverb(Concepts.VERY))
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.EAT).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.THIS)))
                        .Face(Expression.Shy).With(Gesture.Think).With(Gesture.OfferItem, QuestItemIds.OddDish);
                case 2:
                    if (st.talkedThisStage && !OfferOpen) return null;
                    if (sys.Today <= st.stageDay && st.outcome != 3) return null;
                    MarkTalked(st);
                    OpenOffer();
                    Send("give_dish");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.EAT).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.DISH, Concepts.NEW)))
                        .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.DELICIOUS).Agent(MeaningArg.Of(Concepts.THIS)))
                        .Face(Expression.Excited).With(Gesture.OfferItem, QuestItemIds.HomemadeDish).With(Gesture.Eat);
                case 3:
                {
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    var ev = FindParty(sys, st);
                    var u = ev != null ? QuestSpeech.EventAnnounce(ev, residentId, sys.Today) : null;
                    if (u != null) u.contextTag = QuestSpeech.TagStory;
                    return u;
                }
                case 4:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    Send("finale");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.HAVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.SHOP).OwnedBy(Concepts.I)).When(Concepts.SOMEDAY))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.DISH).Plural()).Recipient(Concepts.EVERYONE).Future())
                        .Face(Expression.Excited).With(Gesture.Spin);
            }
            return null;
        }

        private static VillageEvent FindParty(QuestSystem sys, StoryState st)
        {
            foreach (var e in sys.S.events) if (e.kind == VillageEventKind.CookParty && e.hostResidentId == st.residentId && !e.finished) return e;
            return null;
        }

        public override bool OnGesture(QuestSystem sys, StoryState st, string residentId, PlayerGesture g)
        {
            if (residentId != st.residentId || st.stage != 2 || !OfferOpen) return false;
            switch (g)
            {
                case PlayerGesture.Happy:
                case PlayerGesture.Clap:
                case PlayerGesture.Nod:
                case PlayerGesture.Bow:
                    CloseOffer();
                    Send("taste_good");
                    AttachLocal(sys, st, sys.Say(residentId, U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).Adverb(Concepts.VERY))
                        .Face(Expression.VeryHappy).With(Gesture.Jump).With(Gesture.Dance)), BookScene.AfterGesture);
                    return true;
                case PlayerGesture.ShakeHead:
                case PlayerGesture.Sad:
                    CloseOffer();
                    Send("taste_bad");
                    AttachLocal(sys, st, sys.Say(residentId, U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SAD).Agent(Concepts.I))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Adverb(Concepts.AGAIN).Future())
                        .Face(Expression.Sad).With(Gesture.Nod)), BookScene.AfterShake);
                    return true;
                case PlayerGesture.Think:
                    OpenOffer();
                    AttachLocal(sys, st, sys.Say(residentId, U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.DELICIOUS).Agent(MeaningArg.Of(Concepts.THIS)))
                        .Face(Expression.Worried).With(Gesture.Eat).With(Gesture.Think)), BookScene.AfterThink);
                    return true;
            }
            return false;
        }

        public override Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId)
        {
            if (residentId != st.residentId) return null;
            // 料理の手掛かり：受け取った材料を持って調理場へ急ぐ（レシピの手掛かり：§12）
            if (st.playerId == sys.LocalId)
            {
                var a = ItemRegistry.GetOrPlaceholder(st.itemId);
                var b = ItemRegistry.GetOrPlaceholder(st.itemId2);
                sys.Tablet?.AddMaterial(new TabletMaterial
                {
                    id = "story:cook:recipe:" + st.residentId + ":" + st.counter,
                    kind = TabletMaterialKind.Recipe,
                    titleJa = "料理好きの住民の様子",
                    titleEn = "The cooking-loving resident",
                    factJa = string.Format("{0}日目：{1}と{2}を受け取ると、両手に抱えて調理場のほうへ急いでいった。", sys.Today, a.NameJa, b.NameJa),
                    factEn = string.Format("Day {0}: after receiving {1} and {2}, they hurried off toward the kitchen holding them.", sys.Today, a.NameEn, b.NameEn),
                    day = sys.Today,
                    minuteOfDay = sys.Minute,
                    linkedResidentId = st.residentId
                });
            }
            return U(residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).When(Concepts.NOW).Future())
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.VeryHappy).With(Gesture.Hold).With(Gesture.Jump);
        }

        public override void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage != 1) return;
            st.outcome = 1;
            Advance(sys, st, 2);
        }

        public override void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx)
        {
            switch (action)
            {
                case "odd_dish":
                    if (st.stage == 0 && !st.HasFlag("odd_given")) { st.SetFlag("odd_given"); ctx.GiveItem(new ItemStack(QuestItemIds.OddDish, 1), "story.cook"); }
                    break;
                case "give_dish":
                    if (st.stage == 2 && !st.HasFlag("dish_given:" + st.counter)) { st.SetFlag("dish_given:" + st.counter); ctx.GiveItem(new ItemStack(QuestItemIds.HomemadeDish, 1), "story.cook"); }
                    break;
                case "taste_good":
                    if (st.stage != 2) return;
                    sys.Friendship(st.residentId, ctx.IssuerId, 4, "story.cook.encouraged");
                    sys.Memory(st.residentId, "encouraged_cooking", ctx.IssuerId, QuestItemIds.HomemadeDish, null, 0.9f);
                    StartParty(sys, st);
                    break;
                case "taste_bad":
                    if (st.stage != 2) return;
                    sys.Memory(st.residentId, "honest_feedback", ctx.IssuerId, QuestItemIds.HomemadeDish, null, 0.6f);
                    st.counter++;
                    if (st.counter < 2) Advance(sys, st, 1); // 材料を変えてもう一度（失敗しても物語は消えない）
                    else StartParty(sys, st);
                    break;
                case "finale":
                    if (st.stage != 4) return;
                    ctx.PayMoney(200, "story.cook");
                    sys.Friendship(st.residentId, ctx.IssuerId, 4, "story.cook");
                    sys.Memory(st.residentId, "dream_shared", ctx.IssuerId, null, null, 1f);
                    Finish(sys, st, st.outcome == 0 ? 1 : st.outcome);
                    break;
            }
        }

        public override void OnEventFinished(QuestSystem sys, StoryState st, VillageEvent ev)
        {
            if (ev.kind != VillageEventKind.CookParty || ev.hostResidentId != st.residentId || st.stage != 3) return;
            var r = sys.R(st.residentId);
            if (r != null && !st.HasFlag("stall")) { st.SetFlag("stall"); sys.AddLifeMark("food_stall", st.residentId, QuestItemIds.HomemadeDish, sys.HomeOf(r), new Color(1f, 0.7f, 0.4f)); }
            Advance(sys, st, 4);
        }

        public override void OnAttended(QuestSystem sys, StoryState st, VillageEvent ev, CommandContext ctx)
        {
            if (ev.kind == VillageEventKind.CookParty && ev.hostResidentId == st.residentId)
            {
                sys.Friendship(st.residentId, ctx.IssuerId, 3, "story.cook.party");
                sys.Memory(st.residentId, "came_to_party", ctx.IssuerId, null, null, 0.8f);
            }
        }
    }

    // =====================================================================
    // 旅に憧れる小さな住民（メインストーリー 第3章）：宇宙船に夢中 → 一緒に見に行く → 星のかけら → 星の光帯 → いつか
    // =====================================================================
    public sealed class LittleTravelerStory : ResidentStory
    {
        public override string Id => "little_traveler";
        public override int EarliestDay => 2;

        public override StoryState TryCreate(QuestSystem sys, string playerId, int day)
        {
            var who = PickByJob(sys, playerId, ResidentJob.Traveler, ResidentJob.Child);
            if (who == null) who = PickByLineage(sys, playerId, AlienLineage.StarChild, AlienLineage.Floating);
            if (who == null) return null;
            return new StoryState { residentId = who.Id };
        }

        public override void OnHour(QuestSystem sys, StoryState st, int day, int hour)
        {
            if (st.stage == 0 && hour == 10)
            {
                var rp = sys.RocketPos();
                sys.GoTo(st.residentId, rp + new Vector3(5f, 0f, 4f), 11 * 60, "story.traveler.rocket");
            }
        }

        public override void OnMinutes(QuestSystem sys, StoryState st)
        {
            if (st.stage != 0) return;
            int m = sys.Minute;
            string key = "wow:" + sys.Today;
            if (m >= 10 * 60 + 30 && m < 10 * 60 + 40 && !st.HasFlag(key))
            {
                st.SetFlag(key);
                sys.Say(st.residentId, U(st.residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BIG).Agent(Concepts.ROCKET).Adverb(Concepts.VERY))
                    .Face(Expression.Excited).With(Gesture.Jump).With(Gesture.PointAt, "rocket"), SpeechChannel.Overheard);
            }
        }

        public override void OnDayStarted(QuestSystem sys, StoryState st, int day)
        {
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage && day > st.stageDay) Advance(sys, st, 1);
                    break;
                case 1:
                {
                    var q = Quest(sys, st);
                    if (q != null && q.IsOpen && q.createdDay < day) { sys.SetStatus(q, QuestStatus.Expired); q.outcome = 3; }
                    if (q == null || !q.IsOpen)
                    {
                        if (st.counter >= 3)
                        {
                            // 主人公が来なくても、自分で何度も見に行った
                            st.outcome = 3;
                            Advance(sys, st, 2);
                            break;
                        }
                        st.counter++;
                        var goal = new QuestGoal { type = GoalType.Escort, place = sys.RocketPos(), count = 1 };
                        sys.CreateStoryQuest(st, st.residentId, goal, null, Concepts.ROCKET, false, "rocket", Expression.Excited, 0).deadlineDay = day;
                    }
                    break;
                }
                case 2:
                    if (Quest(sys, st) == null && !st.HasFlag("star_quest")) { st.SetFlag("star_quest"); MakeStarQuest(sys, st); }
                    break;
                case 3:
                    if (day - st.stageDay >= 5 && !st.HasFlag("nightsky"))
                    {
                        // 星の光帯がなかなか出ない時は、夜空を見に誘ってくれる
                        st.SetFlag("nightsky");
                        sys.CreateEvent(VillageEventKind.NightSky, st.residentId, day, 21 * 60, 22 * 60 + 30, sys.HillPos());
                    }
                    break;
            }
        }

        private void MakeStarQuest(QuestSystem sys, StoryState st)
        {
            QuestGoal goal = null;
            foreach (var d in ItemRegistry.All)
                if (d.HasTag(ItemTags.Glowing) && d.SourceActivity != "quest") { goal = new QuestGoal { type = GoalType.DeliverItem, tag = ItemTags.Glowing, count = 1, sampleItemId = null }; break; }
            if (goal == null)
            {
                foreach (var d in ItemRegistry.ByCategory(ItemCategory.Gem)) { goal = new QuestGoal { type = GoalType.DeliverItem, useCategory = true, category = ItemCategory.Gem, count = 1 }; break; }
            }
            if (goal == null && ItemRegistry.Exists(SharedItemIds.OreEnergyCrystal)) goal = new QuestGoal { type = GoalType.DeliverItem, itemId = SharedItemIds.OreEnergyCrystal, count = 1 };
            if (goal == null) { Advance(sys, st, 3); return; }
            var q = sys.CreateStoryQuest(st, st.residentId, goal, Concepts.LIKE, Concepts.STAR, false, "hill", Expression.Excited, 100);
            q.showNumber = false;
        }

        public override Utterance GetTalk(QuestSystem sys, StoryState st, string residentId)
        {
            if (residentId != st.residentId) return null;
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.HAVE).Agent(Concepts.YOU).Patient(Concepts.ROCKET))
                        .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.FLY).Agent(Concepts.ROCKET))
                        .Face(Expression.Excited).With(Gesture.PointAt, "rocket").With(Gesture.Jump);
                case 3:
                {
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.SEE).Agent(Concepts.WE).Patient(Concepts.STAR_BAND).At(Concepts.HILL).Adverb(Concepts.TOGETHER).When(Concepts.NIGHT))
                        .Face(Expression.Excited).With(Gesture.LookUp).With(Gesture.PointAt, "hill");
                }
                case 4:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    Send("finale");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GO).Agent(Concepts.I).At(Concepts.PLANET).When(Concepts.SOMEDAY).Future())
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PICTURE)).Recipient(Concepts.YOU))
                        .Face(Expression.Excited).With(Gesture.LookUp).With(Gesture.OfferItem, QuestItemIds.ChildDrawing);
            }
            return null;
        }

        public override void OnQuestAccepted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage == 1) sys.GoTo(st.residentId, sys.RocketPos() + new Vector3(3f, 0f, 2f), sys.Minute + 120, "story.traveler.escort");
        }

        public override void LocalCheck(QuestSystem sys, StoryState st)
        {
            if (st.stage != 1 || st.playerId != sys.LocalId) return;
            var q = Quest(sys, st);
            if (q == null || q.status != QuestStatus.Active || st.HasFlag("escort_sent:" + q.id)) return;
            var rp = sys.RocketPos();
            var child = sys.R(st.residentId);
            if (child == null || !LocalNear(sys, rp, 10f) || QuestSystem.Flat(child.Position - rp) > QuestTuning.EscortRadius) return;
            st.SetFlag("escort_sent:" + q.id);
            Send("escort_done", q.id);
            AttachLocal(sys, st, sys.Say(st.residentId, U(st.residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(Concepts.ROCKET).Adverb(Concepts.VERY))
                .Face(Expression.VeryHappy).With(Gesture.Spin).With(Gesture.Jump)), BookScene.AfterHelp);
        }

        public override Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId)
        {
            if (st.stage != 2 || residentId != st.residentId) return null;
            return U(residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SAME).Agent(MeaningArg.Of(Concepts.THIS)).Patient(Concepts.STAR))
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.Love).With(Gesture.Spin).With(Gesture.LookUp);
        }

        public override void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx)
        {
            if (action == "escort_done" && st.stage == 1)
            {
                sys.Memory(st.residentId, "saw_rocket_together", ctx.IssuerId, null, null, 0.9f);
                st.outcome = 1;
                var q = sys.FindQuest(arg);
                if (q != null && q.IsOpen)
                {
                    q.goals[0].progress = q.goals[0].count;
                    sys.CompleteQuest(q, ctx, 2, null); // → OnQuestCompleted で次の段階へ
                }
                if (st.stage == 1) Advance(sys, st, 2);
            }
            if (action == "finale" && st.stage == 4 && !st.finished)
            {
                ctx.GiveItem(new ItemStack(QuestItemIds.ChildDrawing, 1), "story.traveler");
                sys.Friendship(st.residentId, ctx.IssuerId, 5, "story.traveler");
                sys.Memory(st.residentId, "dream_shared", ctx.IssuerId, QuestItemIds.ChildDrawing, null, 1f);
                Finish(sys, st, st.outcome == 0 ? 1 : st.outcome);
            }
        }

        public override void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage == 1 && q.MainGoal != null && q.MainGoal.type == GoalType.Escort)
            {
                st.outcome = 1;
                Advance(sys, st, 2);
                return;
            }
            if (st.stage == 2) Advance(sys, st, 3);
        }

        public override void OnAttended(QuestSystem sys, StoryState st, VillageEvent ev, CommandContext ctx)
        {
            if (st.stage != 3) return;
            if (ev.kind == VillageEventKind.StarBandViewing || (ev.kind == VillageEventKind.NightSky && ev.hostResidentId == st.residentId))
            {
                sys.Memory(st.residentId, "watched_sky_together", ctx.IssuerId, null, null, 1f);
                Advance(sys, st, 4);
            }
        }

        public override void OnEventFinished(QuestSystem sys, StoryState st, VillageEvent ev)
        {
            if (st.stage == 3 && ev.kind == VillageEventKind.NightSky && ev.hostResidentId == st.residentId && ev.attendees.Count == 0)
                st.flags.Remove("nightsky"); // 来なかったら、また別の日に誘う
        }

        public override void OnWeather(QuestSystem sys, StoryState st, WeatherChangedEvent e)
        {
            // 星の光帯の夜は、真っ先に高台へ
            if (st.stage >= 2 && e.Current.Has(AlienPhenomenon.StarBand))
                sys.GoTo(st.residentId, sys.HillPos() + new Vector3(0.8f, 0f, 0.8f), QuestTuning.StarBandEndMinute, "story.traveler.starband");
        }
    }

    // =====================================================================
    // 宇宙船の修理屋の夢（メインストーリー 第3章、ロケット仕様 §14）：
    //   0 宇宙船に夢中 → 1 金属集め → 2 精密な部品 → 3 小さな宇宙船の模型 → 4 いつか空へ
    //   ロケット修理の必須条件ではない（ロケット仕様 §14.1：友好度をゲートにしない）。
    // =====================================================================
    public sealed class MechanicDreamStory : ResidentStory
    {
        public override string Id => "mechanic_dream";
        public override int EarliestDay => 3;

        public override StoryState TryCreate(QuestSystem sys, string playerId, int day)
        {
            var m = PickByJob(sys, playerId, ResidentJob.Mechanic);
            if (m == null) return null;
            var ores = QuestCatalog.Ores(playerId, 2);
            string ore = ores.Contains(SharedItemIds.OreSturdyMetal) ? SharedItemIds.OreSturdyMetal : (ores.Count > 0 ? ores[ores.Count - 1] : null);
            string part = ItemRegistry.Exists(SharedItemIds.PartConductiveWire) ? SharedItemIds.PartConductiveWire
                : (ItemRegistry.Exists(SharedItemIds.OreConductive) ? SharedItemIds.OreConductive : null);
            if (ore == null && part == null) return null;
            return new StoryState { residentId = m.Id, itemId = ore, itemId2 = part };
        }

        public override void OnHour(QuestSystem sys, StoryState st, int day, int hour)
        {
            if (st.stage <= 2 && hour == 14)
                sys.GoTo(st.residentId, sys.RocketPos() + new Vector3(-3f, 0f, 3f), 15 * 60, "story.mechanic.rocket");
        }

        public override void OnDayStarted(QuestSystem sys, StoryState st, int day)
        {
            var rocket = sys.Game.Service<IRocketService>();
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage && ((rocket != null && rocket.RepairedDeviceCount >= 1) || day - st.stageDay >= 4)) Advance(sys, st, 1);
                    break;
                case 1:
                case 2:
                {
                    string item = st.stage == 1 ? st.itemId : st.itemId2;
                    if (string.IsNullOrEmpty(item)) { Advance(sys, st, st.stage + 1); if (st.stage == 3) OnModel(sys, st); break; }
                    var q = Quest(sys, st);
                    if (q == null)
                    {
                        var goal = new QuestGoal { type = GoalType.DeliverItem, itemId = item, count = st.stage == 1 ? 3 : 1 };
                        sys.CreateStoryQuest(st, st.residentId, goal, Concepts.MAKE, Concepts.ROCKET, true, null, Expression.Excited, st.stage == 1 ? 200 : 250);
                    }
                    else if (q.IsOpen && day - st.stageDay >= QuestTuning.StoryNoParticipationDays + 2)
                    {
                        // 自分で集めて作業を進めた（主人公を待って止まらない）
                        sys.SetStatus(q, QuestStatus.Expired);
                        q.outcome = 3;
                        st.outcome = 3;
                        Advance(sys, st, st.stage + 1);
                        if (st.stage == 3) OnModel(sys, st);
                    }
                    break;
                }
                case 4:
                    break;
            }
        }

        private void OnModel(QuestSystem sys, StoryState st)
        {
            var m = sys.R(st.residentId);
            if (m == null || st.HasFlag("model")) return;
            st.SetFlag("model");
            var pos = sys.FacilityPos(FacilityType.RepairWorkshop, sys.WorkplaceOf(m));
            sys.AddLifeMark("rocket_model", st.residentId, st.itemId, pos, new Color(0.85f, 0.85f, 0.95f));
        }

        private Vector3 ModelPos(QuestSystem sys, StoryState st)
        {
            foreach (var mk in sys.S.marks) if (mk.residentId == st.residentId && mk.kind == "rocket_model") return mk.position;
            var m = sys.R(st.residentId);
            return m != null ? sys.WorkplaceOf(m) : Vector3.zero;
        }

        public override Utterance GetTalk(QuestSystem sys, StoryState st, string residentId)
        {
            if (residentId != st.residentId) return null;
            switch (st.stage)
            {
                case 0:
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.ROCKET).Adverb(Concepts.VERY))
                        .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ROCKET).OwnedBy(Concepts.I)).When(Concepts.SOMEDAY))
                        .Face(Expression.Excited).With(Gesture.Spin).With(Gesture.PointAt, "rocket");
                case 3:
                {
                    if (st.talkedThisStage) return null;
                    MarkTalked(st);
                    var m = sys.R(residentId);
                    var shop = sys.Layout?.GetFacility(FacilityType.RepairWorkshop);
                    string where = shop != null ? shop.id : (m != null ? m.HomeFacilityId : "rocket");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ROCKET, Concepts.SMALL)).Past())
                        .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.SEE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.ROCKET, Concepts.SMALL)))
                        .Face(Expression.Excited).With(Gesture.Beckon).With(Gesture.PointAt, where);
                }
                case 4:
                {
                    var rocket = sys.Game.Service<IRocketService>();
                    bool ready = rocket == null
                        ? sys.FriendshipOf(residentId, st.playerId) >= QuestTuning.StoryFriendshipForEpilogue
                        : (rocket.TotalDeviceCount > 0 && rocket.RepairedDeviceCount * 2 >= rocket.TotalDeviceCount) || rocket.IsLaunchReady || rocket.HasLaunched;
                    if (st.talkedThisStage || !ready) return null;
                    MarkTalked(st);
                    Send("finale");
                    return U(residentId)
                        .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                        .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.FLY).Agent(Concepts.I).At(Concepts.SKY).When(Concepts.SOMEDAY))
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PART)).Recipient(Concepts.YOU))
                        .Face(Expression.Happy).With(Gesture.LookUp).With(Gesture.Hug);
                }
            }
            return null;
        }

        public override void LocalCheck(QuestSystem sys, StoryState st)
        {
            if (st.stage != 3 || st.playerId != sys.LocalId || st.HasFlag("saw_sent")) return;
            if (!st.talkedThisStage) return;
            if (!LocalNear(sys, ModelPos(sys, st), 5f)) return;
            st.SetFlag("saw_sent");
            Send("saw_model");
            var m = sys.R(st.residentId);
            if (m != null && LocalNear(sys, m.Position, 25f))
                AttachLocal(sys, st, sys.Say(st.residentId, U(st.residentId)
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FLY).Agent(MeaningArg.Of(Concepts.ROCKET, Concepts.SMALL)).Future().Adverb(Concepts.MAYBE))
                    .Face(Expression.VeryHappy).With(Gesture.Spin)), BookScene.AfterHelp);
        }

        public override Utterance DeliveredSpeech(QuestSystem sys, StoryState st, QuestInstance q, string residentId)
        {
            if (residentId != st.residentId) return null;
            return U(residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ROCKET, Concepts.SMALL)).Future().When(Concepts.SOON))
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.VeryHappy).With(Gesture.Spin).With(Gesture.Hold);
        }

        public override void OnQuestCompleted(QuestSystem sys, StoryState st, QuestInstance q, CommandContext ctx)
        {
            if (st.stage != 1 && st.stage != 2) return;
            if (st.outcome == 0) st.outcome = 1;
            Advance(sys, st, st.stage + 1);
            if (st.stage == 3) OnModel(sys, st);
        }

        public override void HandleCommand(QuestSystem sys, StoryState st, string action, string arg, float value, CommandContext ctx)
        {
            if (action == "saw_model" && st.stage == 3)
            {
                sys.Friendship(st.residentId, ctx.IssuerId, 4, "story.mechanic.model");
                sys.Memory(st.residentId, "saw_my_model", ctx.IssuerId, null, null, 0.9f);
                Advance(sys, st, 4);
            }
            if (action == "finale" && st.stage == 4 && !st.finished)
            {
                // 修理素材の協力（§12, ロケット仕様 §8.3）。必須ではない
                if (ItemRegistry.Exists(SharedItemIds.PartConnectorKit)) ctx.GiveItem(new ItemStack(SharedItemIds.PartConnectorKit, 1), "story.mechanic");
                else ctx.PayMoney(300, "story.mechanic");
                sys.Friendship(st.residentId, ctx.IssuerId, 5, "story.mechanic");
                sys.Memory(st.residentId, "dream_shared", ctx.IssuerId, null, null, 1f);
                Finish(sys, st, st.outcome == 0 ? 1 : st.outcome);
            }
        }

        public override void OnRocketRepaired(QuestSystem sys, StoryState st, RocketDeviceRepairedEvent e)
        {
            // 修理が進むと「音が出た」などと喜ぶ（ロケット仕様 §14.2）
            if (!sys.IsActiveResident(st.residentId)) return;
            sys.Say(st.residentId, U(st.residentId)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REPAIR).Agent(Concepts.WE).Patient(Concepts.ROCKET).Past())
                .Face(Expression.VeryHappy).With(Gesture.Jump).With(Gesture.PointAt, "rocket"), SpeechChannel.Overheard);
            if (st.stage == 0 && st.talkedThisStage) Advance(sys, st, 1);
        }
    }
}
