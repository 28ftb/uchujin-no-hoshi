using Uchujin.Core;

namespace Uchujin.Quests
{
    /// <summary>
    /// クエストの発話を「意味データ」で組み立てる（宇宙語仕様 §14、クエスト仕様 §18.1）。
    /// 日本語の文は作らない。話し手（住民）が本当に思っていること・知っていることだけを言わせる（言語 §5.2）。
    /// 手掛かりは住民が実際に出したもの（身振り・見本・数・指さし・表情）だけ（§4.2）。
    /// </summary>
    public static class QuestSpeech
    {
        public const string TagRequest = "quest.request";
        public const string TagReminder = "quest.reminder";
        public const string TagThanks = "quest.thanks";
        public const string TagPartial = "quest.partial";
        public const string TagDifferent = "quest.different";
        public const string TagNearMiss = "quest.near_miss";
        public const string TagWrongRecipient = "quest.wrong_recipient";
        public const string TagSelfSolved = "quest.self_solved";
        public const string TagDeclined = "quest.declined";
        public const string TagAbandonAsk = "quest.abandon_ask";
        public const string TagLost = "quest.lost";
        public const string TagHelp = "quest.help";
        public const string TagHelpThanks = "quest.help_thanks";
        public const string TagEvent = "quest.event";
        public const string TagStory = "quest.story";
        public const string TagBoard = "quest.board";
        public const string TagRepairHint = "quest.repair_hint";

        // ------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------

        public static MeaningArg ItemArg(string itemId, int qty = 0)
        {
            var def = ItemRegistry.Get(itemId);
            var a = MeaningArg.Of(def != null ? def.EffectiveConceptId : Concepts.Item(itemId));
            if (qty > 1) a.Qty(qty);
            return a;
        }

        /// <summary>目標を名詞句に（「青い魚を3匹」「大きいX」等）。</summary>
        public static MeaningArg GoalArg(QuestGoal g, bool withQty = true)
        {
            if (g == null) return MeaningArg.Of(Concepts.THING);
            MeaningArg a;
            switch (g.type)
            {
                case GoalType.CatchCreature:
                    a = MeaningArg.Of(Concepts.Species(g.speciesId));
                    break;
                case GoalType.GiftFor:
                    a = MeaningArg.Of(Concepts.GIFT);
                    break;
                default:
                    if (!string.IsNullOrEmpty(g.itemId)) a = ItemArg(g.itemId);
                    else if (g.useCategory) a = MeaningArg.Of(QuestCatalog.CategoryConcept(g.category));
                    else if (!string.IsNullOrEmpty(g.tag)) a = MeaningArg.Of(Concepts.THING);
                    else a = MeaningArg.Of(Concepts.THING);
                    break;
            }
            if (!string.IsNullOrEmpty(g.tag))
            {
                var c = QuestCatalog.ColorConceptFromTag(g.tag);
                if (c != null && !a.adjectives.Contains(c)) a.Adj(c);
            }
            if (g.minSize > 0f) a.Adj(Concepts.BIG);
            if (g.minQuality > 0) a.Adj(Concepts.GOOD);
            if (withQty && g.count > 1) a.Qty(g.count);
            return a;
        }

        public static string DeadlineWord(QuestInstance q, int today)
        {
            if (q == null || q.deadlineDay < 0) return null;
            int d = q.deadlineDay - today;
            if (d <= 0) return Concepts.TODAY;
            if (d == 1) return Concepts.TOMORROW;
            return Concepts.SOON;
        }

        public static MeaningFrameLite Reason(QuestInstance q)
        {
            if (q == null || string.IsNullOrEmpty(q.reasonConcept)) return null;
            var lite = MeaningFrameLite.Make(q.reasonConcept, MeaningArg.Of(Concepts.I),
                string.IsNullOrEmpty(q.reasonPatient) ? null : MeaningArg.Of(q.reasonPatient));
            if (q.reasonConcept == Concepts.BIRTHDAY)
            {
                // 「（相手の）誕生日だから」
                lite = MeaningFrameLite.Make(Concepts.EXIST, MeaningArg.Of(Concepts.BIRTHDAY).OwnedBy(q.reasonPatient), null);
            }
            return lite;
        }

        private static string FirstItemOf(QuestGoal g)
        {
            if (g == null) return null;
            if (!string.IsNullOrEmpty(g.sampleItemId)) return g.sampleItemId;
            return g.itemId;
        }

        // ------------------------------------------------------------
        // お願い（§4, §5）
        // ------------------------------------------------------------

        /// <summary>お願いの発話。clueLevel が上がるほど身振り・見本を増やす（「考える」で再説明：§4.3）。</summary>
        public static Utterance Request(QuestInstance q, int today, int clueLevel = 0)
        {
            var u = new Utterance(q.residentId, TagRequest);
            var g = q.MainGoal;
            string when = DeadlineWord(q, today);
            var reason = Reason(q);
            if (g == null) return u.Face(q.face);

            switch (g.type)
            {
                case GoalType.DeliverToResident:
                {
                    var f = MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU)
                        .Patient(GoalArg(g)).Recipient(Concepts.Name(g.targetResidentId));
                    if (when != null) f.When(when);
                    if (reason != null) f.Because(reason);
                    u.Say(f);
                    u.With(Gesture.OfferItem, g.itemId);
                    u.With(Gesture.PointAt, g.targetResidentId);
                    break;
                }
                case GoalType.GiftFor:
                {
                    var f = MeaningFrame.Make(SpeechAct.Request, Concepts.LOOK_FOR).Agent(Concepts.YOU)
                        .Patient(MeaningArg.Of(Concepts.GIFT)).Recipient(Concepts.Name(g.targetResidentId));
                    if (when != null) f.When(when);
                    if (reason != null) f.Because(reason);
                    u.Say(f);
                    u.With(Gesture.PointAt, g.targetResidentId).With(Gesture.Think);
                    break;
                }
                case GoalType.CatchCreature:
                {
                    var f = MeaningFrame.Make(SpeechAct.Request, Concepts.CATCH).Agent(Concepts.YOU).Patient(GoalArg(g));
                    if (when != null) f.When(when);
                    if (reason != null) f.Because(reason);
                    u.Say(f);
                    u.Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.SEE).Agent(Concepts.I).Patient(GoalArg(g, false)));
                    u.With(Gesture.Hold);
                    break;
                }
                case GoalType.Escort:
                {
                    var f = MeaningFrame.Make(SpeechAct.Invitation, Concepts.GO).Agent(Concepts.WE).Adverb(Concepts.TOGETHER);
                    if (!string.IsNullOrEmpty(q.reasonPatient)) f.At(q.reasonPatient);
                    if (when != null) f.When(when);
                    u.Say(f);
                    u.With(Gesture.Beckon);
                    break;
                }
                case GoalType.Attend:
                {
                    var f = MeaningFrame.Make(SpeechAct.Invitation, Concepts.COME).Agent(Concepts.YOU);
                    if (!string.IsNullOrEmpty(q.reasonPatient)) f.At(q.reasonPatient);
                    if (when != null) f.When(when);
                    u.Say(f);
                    u.With(Gesture.Beckon);
                    break;
                }
                default:
                {
                    var f = MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU)
                        .Patient(GoalArg(g)).Recipient(Concepts.I);
                    if (when != null) f.When(when);
                    if (reason != null) f.Because(reason);
                    u.Say(f);
                    // 2つ目の目標（料理の材料など）
                    for (int i = 1; i < q.goals.Count; i++)
                    {
                        u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU)
                            .Patient(GoalArg(q.goals[i])).Adverb(Concepts.ALSO));
                    }
                    break;
                }
            }

            bool sample = q.showSample || clueLevel >= 1;
            bool number = (q.showNumber || clueLevel >= 1) && g.count > 1;
            if (sample && g.type != GoalType.DeliverToResident && g.type != GoalType.GiftFor)
            {
                string s = FirstItemOf(g);
                if (!string.IsNullOrEmpty(s)) u.With(Gesture.OfferItem, s);
                for (int i = 1; i < q.goals.Count; i++)
                {
                    string s2 = FirstItemOf(q.goals[i]);
                    if (!string.IsNullOrEmpty(s2)) u.With(Gesture.OfferItem, s2);
                }
            }
            if (number) u.With(Gesture.ShowNumber, null, g.count);
            if (!string.IsNullOrEmpty(q.pointTarget) && g.type != GoalType.DeliverToResident && g.type != GoalType.GiftFor)
                u.With(Gesture.PointAt, q.pointTarget);
            if (clueLevel >= 2)
            {
                // ゆっくり短く言い直す：目的語だけの短い文
                u.Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(GoalArg(g)));
            }
            u.Face(clueLevel >= 2 ? Expression.Thinking : q.face);
            u.Related(g.itemId).Related(g.targetResidentId);
            return u;
        }

        public static Utterance Reminder(QuestInstance q, int today)
        {
            var u = new Utterance(q.residentId, TagReminder);
            var g = q.MainGoal;
            if (g == null) return u;
            if (g.type == GoalType.DeliverToResident)
            {
                u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(GoalArg(g)).Recipient(Concepts.Name(g.targetResidentId)).Adverb(Concepts.STILL));
                u.With(Gesture.PointAt, g.targetResidentId);
            }
            else if (g.type == GoalType.Escort || g.type == GoalType.Attend)
            {
                return Request(q, today, 0);
            }
            else
            {
                var arg = GoalArg(g, false);
                if (g.Remaining > 1) arg.Qty(g.Remaining);
                u.Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(arg).Adverb(Concepts.STILL));
                if (q.showSample) { var s = FirstItemOf(g); if (s != null) u.With(Gesture.OfferItem, s); }
                if (q.showNumber && g.Remaining > 1) u.With(Gesture.ShowNumber, null, g.Remaining);
            }
            string when = DeadlineWord(q, today);
            if (when != null && u.sentences.Count > 0) u.sentences[0].When(when);
            return u.Face(Expression.Neutral).Related(g.itemId);
        }

        // ------------------------------------------------------------
        // 反応（§4.3 誤解から起きるやり取り、§5.3 結果）
        // ------------------------------------------------------------

        public static Utterance Thanks(string residentId, string itemId, bool liked, bool veryHappy)
        {
            var u = new Utterance(residentId, TagThanks)
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
            var happy = MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I);
            if (veryHappy) happy.Adverb(Concepts.VERY);
            u.Say(happy);
            if (liked && !string.IsNullOrEmpty(itemId))
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(ItemArg(itemId)));
            u.Face(veryHappy ? Expression.VeryHappy : Expression.Happy).With(veryHappy ? Gesture.Jump : Gesture.Clap);
            return u.Related(itemId);
        }

        public static Utterance Partial(QuestInstance q, string itemId)
        {
            var g = q.MainGoal;
            var u = new Utterance(q.residentId, TagPartial)
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
            int rem = 0;
            foreach (var x in q.goals) rem += x.Remaining;
            var more = MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.MORE).Qty(rem));
            u.Say(more);
            if (q.showNumber || rem > 1) u.With(Gesture.ShowNumber, null, rem);
            return u.Face(Expression.Happy).Related(itemId);
        }

        /// <summary>全く違う物：困った顔をしてもう一度見せる。でも贈り物として受け取る（罰しない）。</summary>
        public static Utterance Different(QuestInstance q, string givenItemId)
        {
            var g = q.MainGoal;
            var u = new Utterance(q.residentId, TagDifferent);
            u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM));
            u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DIFFERENT).Agent(ItemArg(givenItemId)));
            if (g != null)
            {
                if (g.type == GoalType.DeliverToResident)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(GoalArg(g)).Recipient(Concepts.Name(g.targetResidentId)));
                    u.With(Gesture.PointAt, g.targetResidentId);
                }
                else
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(GoalArg(g)));
                    var s = FirstItemOf(g);
                    if (q.showSample && s != null) u.With(Gesture.OfferItem, s);
                    if (q.showNumber && g.Remaining > 1) u.With(Gesture.ShowNumber, null, g.Remaining);
                }
            }
            u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Adverb(Concepts.ALSO));
            return u.Face(Expression.Confused).With(Gesture.ShakeHead).Related(givenItemId);
        }

        /// <summary>惜しい（物は合っているが大きさ・品質・色が違う）：首を振って返し、見本を見せる。</summary>
        public static Utterance NearMiss(QuestInstance q, ItemStack given)
        {
            var g = q.MainGoal;
            var u = new Utterance(q.residentId, TagNearMiss);
            u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM));
            string pred = Concepts.DIFFERENT;
            if (g != null && g.minSize > 0f && given.size > 0f && given.size < g.minSize) pred = Concepts.SMALL;
            else if (g != null && given.quality < g.minQuality) pred = Concepts.BAD;
            u.Say(MeaningFrame.Make(SpeechAct.Statement, pred).Agent(MeaningArg.Of(Concepts.THIS)).Adverb(Concepts.A_LITTLE));
            if (g != null) u.Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(GoalArg(g)));
            u.With(Gesture.ShakeHead).With(Gesture.OfferItem, given.itemId);
            if (g != null && q.showSample && FirstItemOf(g) != null) u.With(Gesture.OfferItem, FirstItemOf(g));
            return u.Face(Expression.Thinking).Related(given.itemId);
        }

        /// <summary>持ち主が違う：首を振り、持ち主（届け先）を指さす。分からなければ肩をすくめる。</summary>
        public static Utterance WrongRecipient(string residentId, string itemId, string ownerId, bool knowsOwner)
        {
            var u = new Utterance(residentId, TagWrongRecipient);
            u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAVE).Agent(Concepts.I).Patient(ItemArg(itemId)).Not());
            if (knowsOwner && !string.IsNullOrEmpty(ownerId))
            {
                u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.THIS)).Recipient(Concepts.Name(ownerId)));
                u.With(Gesture.ShakeHead).With(Gesture.PointAt, ownerId).With(Gesture.OfferItem, itemId);
                return u.Face(Expression.Confused).Related(itemId).Related(ownerId);
            }
            u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.KNOW).Agent(Concepts.I).Not());
            return u.With(Gesture.Shrug).With(Gesture.OfferItem, itemId).Face(Expression.Confused).Related(itemId);
        }

        public static Utterance SelfSolved(string residentId, string itemId, bool bought)
        {
            var u = new Utterance(residentId, TagSelfSolved);
            var f = MeaningFrame.Make(SpeechAct.Statement, bought ? Concepts.BUY : Concepts.FIND).Agent(Concepts.I).Past().Adverb(Concepts.ALREADY);
            if (!string.IsNullOrEmpty(itemId)) f.Patient(ItemArg(itemId));
            u.Say(f);
            u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.OK));
            return u.Face(Expression.Happy).With(Gesture.Nod).Related(itemId);
        }

        public static Utterance Declined(string residentId)
        {
            return new Utterance(residentId, TagDeclined)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.OK))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.WAIT).Agent(Concepts.I).When(Concepts.LATER))
                .Face(Expression.Neutral).With(Gesture.Nod);
        }

        public static Utterance AbandonAsk(string residentId)
        {
            return new Utterance(residentId, TagAbandonAsk)
                .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.HELP).Agent(Concepts.YOU).Recipient(Concepts.I).Not())
                .Face(Expression.Worried).With(Gesture.Think);
        }

        public static Utterance Accepted(string residentId)
        {
            return new Utterance(residentId, TagThanks)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.Happy).With(Gesture.Clap);
        }

        // ------------------------------------------------------------
        // 自発的なお手伝い（§7）
        // ------------------------------------------------------------

        public static Utterance LostSearching(string residentId, string itemId)
        {
            return new Utterance(residentId, TagLost)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LOSE).Agent(Concepts.I).Patient(ItemArg(itemId).OwnedBy(Concepts.I)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LOOK_FOR).Agent(Concepts.I).Adverb(Concepts.STILL))
                .Face(Expression.Worried).With(Gesture.Think).With(Gesture.LookUp).Related(itemId);
        }

        public static Utterance LostCreatureSearching(string residentId, string speciesId)
        {
            return new Utterance(residentId, TagLost)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LOSE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.Species(speciesId)).OwnedBy(Concepts.I)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SAD).Agent(Concepts.I))
                .Face(Expression.Crying).With(Gesture.Cry);
        }

        public static Utterance ReturnedThanks(string residentId, string itemId)
        {
            return new Utterance(residentId, TagHelpThanks)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FIND).Agent(Concepts.YOU).Patient(ItemArg(itemId).OwnedBy(Concepts.I)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU).Adverb(Concepts.VERY))
                .Face(Expression.VeryHappy).With(Gesture.Hug).Related(itemId);
        }

        public static Utterance CarryAsk(string residentId, string destinationTarget)
        {
            var u = new Utterance(residentId, TagHelp)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HEAVY).Agent(MeaningArg.Of(Concepts.BOX)).Adverb(Concepts.VERY))
                .Face(Expression.Worried).With(Gesture.Hold);
            if (!string.IsNullOrEmpty(destinationTarget)) u.With(Gesture.PointAt, destinationTarget);
            return u;
        }

        public static Utterance CarryThanks(string residentId)
        {
            return new Utterance(residentId, TagHelpThanks)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CARRY).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.BOX)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.VeryHappy).With(Gesture.Bow);
        }

        public static Utterance LaundryThanks(string residentId)
        {
            return new Utterance(residentId, TagHelpThanks)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.RAIN_VERB).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DRY).Agent(MeaningArg.Of(Concepts.CLOTHES).OwnedBy(Concepts.I)))
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                .Face(Expression.Happy).With(Gesture.Bow).With(Gesture.PointAt, residentId);
        }

        public static Utterance GiftGiven(string residentId, string itemId)
        {
            return new Utterance(residentId, TagHelpThanks)
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ItemArg(itemId)).Recipient(Concepts.YOU))
                .Face(Expression.Happy).With(Gesture.OfferItem, itemId).Related(itemId);
        }

        // ------------------------------------------------------------
        // 村の行事（§13）
        // ------------------------------------------------------------

        public static Utterance EventAnnounce(VillageEvent ev, string speakerId, int today)
        {
            var u = new Utterance(speakerId, TagEvent);
            string when = ev.day <= today ? Concepts.TODAY : (ev.day == today + 1 ? Concepts.TOMORROW : Concepts.SOON);
            // 本人が話す時は「私の」、ほかの住民が伝える時は名前で
            string host = speakerId == ev.hostResidentId ? Concepts.I : Concepts.Name(ev.hostResidentId);
            switch (ev.kind)
            {
                case VillageEventKind.Birthday:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.BIRTHDAY).OwnedBy(host)).When(when));
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CELEBRATE).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).At(Concepts.HOUSE).When(Concepts.EVENING));
                    u.With(Gesture.PointAt, ev.hostResidentId).With(Gesture.Clap).Face(Expression.Excited);
                    break;
                case VillageEventKind.MarketDay:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SELL).Agent(Concepts.EVERYONE).Patient(MeaningArg.Of(Concepts.THING).Plural()).At(Concepts.PLAZA).When(when));
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.COME).Agent(Concepts.YOU).When(Concepts.MORNING));
                    u.With(Gesture.Beckon).Face(Expression.Happy);
                    break;
                case VillageEventKind.StarBandViewing:
                case VillageEventKind.NightSky:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SHINE).Agent(Concepts.STAR_BAND).When(Concepts.NIGHT));
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.SEE).Agent(Concepts.WE).Patient(Concepts.SKY).At(Concepts.HILL).Adverb(Concepts.TOGETHER));
                    u.With(Gesture.LookUp).With(Gesture.PointAt, "hill").Face(Expression.Excited);
                    break;
                case VillageEventKind.FarewellParty:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(host).Future().When(Concepts.SOON));
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.MEET).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).When(when));
                    u.With(Gesture.PointAt, ev.hostResidentId).Face(Expression.Sad);
                    break;
                case VillageEventKind.WelcomeParty:
                    if (host == Concepts.I) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COME).Agent(Concepts.I).At(Concepts.VILLAGE).Past());
                    else u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COME).Agent(MeaningArg.Of(Concepts.NEIGHBOR, Concepts.NEW)).Past());
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CELEBRATE).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).At(Concepts.PLAZA).When(when));
                    u.With(Gesture.PointAt, ev.hostResidentId).With(Gesture.Wave).Face(Expression.Happy);
                    break;
                case VillageEventKind.CookParty:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.DISH).Plural()).Future().When(when));
                    u.Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.EAT).Agent(Concepts.EVERYONE).Adverb(Concepts.TOGETHER).At(Concepts.PLAZA));
                    u.With(Gesture.Eat).With(Gesture.Beckon).Face(Expression.Excited);
                    break;
            }
            return u.Related(ev.hostResidentId);
        }

        /// <summary>行事の場で、来てくれた主人公へ。</summary>
        public static Utterance EventWelcome(VillageEvent ev, string speakerId)
        {
            var u = new Utterance(speakerId, TagEvent);
            switch (ev.kind)
            {
                case VillageEventKind.Birthday:
                    u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).Adverb(Concepts.VERY).When(Concepts.TODAY));
                    u.Face(Expression.VeryHappy).With(Gesture.Spin);
                    break;
                case VillageEventKind.StarBandViewing:
                case VillageEventKind.NightSky:
                    u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(Concepts.STAR_BAND).Adverb(Concepts.VERY));
                    u.Face(Expression.Love).With(Gesture.LookUp);
                    break;
                case VillageEventKind.FarewellParty:
                    u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.YOU).Future().Adverb(Concepts.ALWAYS));
                    u.Face(Expression.Crying).With(Gesture.Hug);
                    break;
                case VillageEventKind.WelcomeParty:
                    u.Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).At(Concepts.HERE).Future());
                    u.Face(Expression.Shy).With(Gesture.Bow);
                    break;
                case VillageEventKind.CookParty:
                    u.Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.EAT).Agent(Concepts.YOU).Patient(Concepts.DISH));
                    u.Face(Expression.Excited).With(Gesture.OfferItem, QuestItemIds.HomemadeDish);
                    break;
                default:
                    u.Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.WELCOME));
                    u.Face(Expression.Happy).With(Gesture.Wave);
                    break;
            }
            return u.Related(ev.hostResidentId);
        }

        public static Utterance BirthdayGiftReaction(string residentId, string itemId, float like)
        {
            var u = new Utterance(residentId, TagEvent);
            if (like >= 0.35f)
            {
                u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW));
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(ItemArg(itemId)).Adverb(Concepts.VERY));
                u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                return u.Face(Expression.Love).With(Gesture.Hug).Related(itemId);
            }
            if (like <= -0.3f)
            {
                u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM));
                u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                return u.Face(Expression.Surprised).With(Gesture.Shrug).Related(itemId);
            }
            u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
            u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I));
            return u.Face(Expression.Happy).With(Gesture.Nod).Related(itemId);
        }

        public static Utterance MarketGreeting(string residentId, string itemId)
        {
            return new Utterance(residentId, TagEvent)
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.WELCOME))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FRESH).Agent(ItemArg(itemId)))
                .Face(Expression.Happy).With(Gesture.OfferItem, itemId).Related(itemId);
        }

        /// <summary>修理屋が修理素材の手掛かりを見せる（ロケット仕様 §8.3, §14.3：図・現物で分かる）。</summary>
        public static Utterance RepairHint(string residentId, string itemId)
        {
            return new Utterance(residentId, TagRepairHint)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.NEED).Agent(Concepts.ROCKET).Patient(ItemArg(itemId)))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REPAIR).Agent(Concepts.WE).Patient(Concepts.ROCKET).Adverb(Concepts.TOGETHER).Future())
                .Face(Expression.Excited).With(Gesture.OfferItem, itemId).With(Gesture.PointAt, "rocket").Related(itemId);
        }
    }
}
