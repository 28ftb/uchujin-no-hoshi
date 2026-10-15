using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>
    /// ストーリー演出で住民が話す「意味データ」の組み立て（宇宙語仕様 §14：意味と表面を分離）。
    /// 日本語の文は作らない。ここで組む意味は「その住民がその場面で本当に思っていること・知っていること」に限る。
    /// 表面の宇宙語（単語・語順・文法）は Language モジュールが星ごとに生成する。翻訳は表示しない。
    /// </summary>
    public static class StoryUtterances
    {
        // ------------------------------------------------------------
        // 案内役の口癖（メインストーリー第1章・最終章）
        // ------------------------------------------------------------

        /// <summary>案内役の口癖。出会いから最後まで毎回同じ形で言う（意味はゲームが説明しない）。</summary>
        public static Utterance Catchphrase(string guideId, string concept, Expression face = Expression.Happy)
        {
            return new Utterance(guideId, "story.catchphrase")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, concept))
                .Face(face)
                .With(Gesture.Wave);
        }

        // ------------------------------------------------------------
        // プロローグ
        // ------------------------------------------------------------

        /// <summary>案内役が空から落ちてきた主人公を見つけた（光が落ちるのを見ていた）。</summary>
        public static Utterance GuideFirstSighting(string guideId)
        {
            return new Utterance(guideId, "story.prologue.sighting")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.COME).Agent(Concepts.YOU).At(Concepts.SKY).Past())
                .Face(Expression.Surprised)
                .With(Gesture.LookUp);
        }

        /// <summary>近づかれて驚いて逃げる。</summary>
        public static Utterance GuideStartled(string guideId)
        {
            return new Utterance(guideId, "story.prologue.startled")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Face(Expression.Surprised)
                .With(Gesture.Jump);
        }

        /// <summary>仲間を連れて戻ってきた：「ここに変な生き物がいる」。</summary>
        public static Utterance GuideCallsFriends(string guideId)
        {
            return new Utterance(guideId, "story.prologue.call_friends")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.STRANGE_ONE).At(Concepts.HERE))
                .Face(Expression.Excited)
                .With(Gesture.PointAt, "player");
        }

        /// <summary>修理屋が壊れた宇宙船を調べる：「わあ」「ロケットが壊れている」「（自分が）直したい」。</summary>
        public static Utterance MechanicInspects(string mechanicId)
        {
            return new Utterance(mechanicId, "story.prologue.mechanic")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BROKEN).Agent(Concepts.ROCKET))
                .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.REPAIR).Agent(Concepts.I).Patient(Concepts.ROCKET))
                .Face(Expression.Excited)
                .With(Gesture.PointAt, "rocket")
                .Related("rocket");
        }

        /// <summary>料理好きが食べ物を差し出す：「どうぞ」「これはおいしい」。</summary>
        public static Utterance CookOffers(string cookId, string foodItemId)
        {
            return new Utterance(cookId, "story.prologue.cook_offer")
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.Item(foodItemId))).Recipient(Concepts.YOU))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DELICIOUS).Agent(Concepts.THIS))
                .Face(Expression.Happy)
                .With(Gesture.OfferItem, foodItemId)
                .Related(foodItemId);
        }

        /// <summary>料理好きが仲間にたずねる：「その変な生き物はお腹が空いている？」。</summary>
        public static Utterance CookWonders(string cookId)
        {
            return new Utterance(cookId, "story.prologue.cook_wonder")
                .Say(MeaningFrame.Make(SpeechAct.Question, Concepts.HUNGRY).Agent(Concepts.STRANGE_ONE))
                .Face(Expression.Worried)
                .With(Gesture.Think);
        }

        /// <summary>案内役の挨拶：「こんにちは」＋口癖。</summary>
        public static Utterance GuideGreets(string guideId, string catchphrase)
        {
            return new Utterance(guideId, "story.prologue.greeting")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, catchphrase))
                .Face(Expression.Happy)
                .With(Gesture.Wave);
        }

        /// <summary>案内役が手を引く：「一緒に村へ行こう」。</summary>
        public static Utterance GuideInvitesToVillage(string guideId)
        {
            return new Utterance(guideId, "story.prologue.invite")
                .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.GO).Agent(Concepts.WE).At(Concepts.VILLAGE).Adverb(Concepts.TOGETHER))
                .Face(Expression.Happy)
                .With(Gesture.Beckon);
        }

        /// <summary>村に着いた：「ここが村」。</summary>
        public static Utterance GuideArrivedVillage(string guideId)
        {
            return new Utterance(guideId, "story.prologue.arrived")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.VILLAGE).At(Concepts.HERE))
                .Face(Expression.VeryHappy)
                .With(Gesture.Spin);
        }

        // ------------------------------------------------------------
        // 第1章：案内役の世話焼き
        // ------------------------------------------------------------

        public static Utterance GuideCareVisit(string guideId, string catchphrase, string snackItemId)
        {
            return new Utterance(guideId, "story.guide_care")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, catchphrase))
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.Item(snackItemId))).Recipient(Concepts.YOU))
                .Face(Expression.Happy)
                .With(Gesture.OfferItem, snackItemId)
                .Related(snackItemId);
        }

        // ------------------------------------------------------------
        // エンディング「最後の引っ越し」（ロケット §19）
        // ------------------------------------------------------------

        /// <summary>修理屋の最後の点検：「ロケットはとても良い」「いつか私もロケットを作りたい」（第3章の夢）。</summary>
        public static Utterance MechanicFinalCheck(string mechanicId)
        {
            return new Utterance(mechanicId, "story.ending.mechanic")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.ROCKET).Adverb(Concepts.VERY))
                .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(Concepts.ROCKET).When(Concepts.SOMEDAY))
                .Face(Expression.Excited)
                .With(Gesture.Nod)
                .Related("rocket");
        }

        /// <summary>料理好きが旅のための料理を持ってくる：「どうぞ」「あとで食べて」。</summary>
        public static Utterance CookBento(string cookId, string foodItemId)
        {
            return new Utterance(cookId, "story.ending.cook")
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.Item(foodItemId))).Recipient(Concepts.YOU))
                .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.EAT).Agent(Concepts.YOU).Patient(Concepts.DISH).When(Concepts.LATER))
                .Face(Expression.Happy)
                .With(Gesture.OfferItem, foodItemId)
                .Related(foodItemId);
        }

        /// <summary>仲の良い住民のプレゼント：「どうぞ」「あなたが行くので悲しい」「あなたがとても好き」。</summary>
        public static Utterance CloseFriendGift(string residentId, bool crying)
        {
            var sad = MeaningFrame.Make(SpeechAct.Statement, Concepts.SAD).Agent(Concepts.I)
                .Because(MeaningFrameLite.Make(Concepts.LEAVE, MeaningArg.Of(Concepts.YOU)));
            return new Utterance(residentId, "story.ending.gift")
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(Concepts.GIFT).Recipient(Concepts.YOU))
                .Say(sad)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.YOU).Adverb(Concepts.VERY))
                .Face(crying ? Expression.Crying : Expression.Sad)
                .With(crying ? Gesture.Cry : Gesture.OfferItem, crying ? null : SharedItemIds.GiftBox)
                .Related(SharedItemIds.GiftBox);
        }

        /// <summary>常連客：「ありがとう」「あなたの店でたくさんの物を買った」。</summary>
        public static Utterance RegularCustomer(string residentId)
        {
            return new Utterance(residentId, "story.ending.regular")
                .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I)
                    .Patient(MeaningArg.Of(Concepts.THING, Concepts.MANY).Plural())
                    .At(Concepts.SHOP).Past())
                .Face(Expression.Happy)
                .With(Gesture.Bow);
        }

        /// <summary>主人公が熱中した活動に関わる住民の言葉（ロケット §19.2 経験に応じた違い）。</summary>
        public static Utterance ActivityMemory(string residentId, string activityKey)
        {
            string verb, noun;
            Gesture g;
            switch (activityKey)
            {
                case "fish": verb = Concepts.CATCH_FISH; noun = Concepts.FISH; g = Gesture.Jump; break;
                case "crop": verb = Concepts.HARVEST; noun = Concepts.CROP; g = Gesture.Hold; break;
                case "ore": verb = Concepts.DIG; noun = Concepts.ORE; g = Gesture.Nod; break;
                case "creature": verb = Concepts.CATCH; noun = Concepts.CREATURE; g = Gesture.Spin; break;
                case "craft": verb = Concepts.MAKE; noun = Concepts.FOOD; g = Gesture.Clap; break;
                default: verb = Concepts.SELL; noun = Concepts.THING; g = Gesture.Clap; break;
            }
            return new Utterance(residentId, "story.ending.activity")
                .Say(MeaningFrame.Make(SpeechAct.Statement, verb).Agent(Concepts.YOU)
                    .Patient(MeaningArg.Of(noun, Concepts.MANY).Plural()).Past())
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Face(Expression.Excited)
                .With(g);
        }

        /// <summary>子ども：「ロケットが飛ぶ！」。</summary>
        public static Utterance ChildExcited(string residentId)
        {
            return new Utterance(residentId, "story.ending.child")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FLY).Agent(Concepts.ROCKET).Future())
                .Face(Expression.Excited)
                .With(Gesture.Jump);
        }

        /// <summary>旅に憧れる住民：「いつか私も星へ行きたい」（第3章の夢）。</summary>
        public static Utterance TravelerDream(string residentId)
        {
            return new Utterance(residentId, "story.ending.traveler")
                .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.GO).Agent(Concepts.I).At(Concepts.STAR).When(Concepts.SOMEDAY))
                .Face(Expression.Excited)
                .With(Gesture.LookUp);
        }

        /// <summary>ふつうの見送り：「さようなら」「あなたは故郷へ行く」「いつかまた来て」。</summary>
        public static Utterance PlainFarewell(string residentId)
        {
            return new Utterance(residentId, "story.ending.farewell")
                .Say(MeaningFrame.Make(SpeechAct.Farewell, Concepts.GOODBYE))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GO).Agent(Concepts.YOU).At(Concepts.HOME_PLANET).Future())
                .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.COME).Agent(Concepts.YOU).Adverb(Concepts.AGAIN).When(Concepts.SOMEDAY))
                .Face(Expression.Happy)
                .With(Gesture.Wave);
        }

        /// <summary>離陸を見送る一言（手を振る）。</summary>
        public static Utterance LiftoffWave(string residentId)
        {
            return new Utterance(residentId, "story.ending.liftoff")
                .Say(MeaningFrame.Make(SpeechAct.Farewell, Concepts.GOODBYE))
                .Face(Expression.Happy)
                .With(Gesture.Wave);
        }

        /// <summary>引っ越していった住民からの手紙：「こんにちは」「私は遠くで暮らしている」「あなたを覚えている」「さようなら」。</summary>
        public static Utterance LetterFromFormer(string residentId)
        {
            return new Utterance(residentId, "story.ending.letter")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).At(Concepts.FAR))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.YOU))
                .Say(MeaningFrame.Make(SpeechAct.Farewell, Concepts.GOODBYE))
                .Face(Expression.Happy);
        }

        /// <summary>手紙を届ける：「どうぞ」「手紙」。</summary>
        public static Utterance CourierLetters(string residentId, int count)
        {
            var letters = MeaningArg.Of(Concepts.LETTER);
            if (count > 1) letters.Qty(count).Plural();
            return new Utterance(residentId, "story.ending.letters")
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(letters).Recipient(Concepts.YOU))
                .Face(Expression.Happy)
                .With(Gesture.OfferItem, SharedItemIds.Letter)
                .Related(SharedItemIds.Letter);
        }

        // ---- 主人公がいない村（エピローグ）----

        /// <summary>住民同士の会話：「あの変な生き物は故郷へ行った」。</summary>
        public static Utterance EpilogueTalkA(string residentId)
        {
            return new Utterance(residentId, "story.epilogue.talk")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GO).Agent(Concepts.STRANGE_ONE).At(Concepts.HOME_PLANET).Past())
                .Face(Expression.Neutral)
                .With(Gesture.LookUp);
        }

        /// <summary>住民同士の会話の返事：「さびしい」「私たちはいつも覚えている」。</summary>
        public static Utterance EpilogueTalkB(string residentId)
        {
            return new Utterance(residentId, "story.epilogue.talk")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LONELY).Agent(Concepts.I).Adverb(Concepts.A_LITTLE))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.WE).Patient(Concepts.STRANGE_ONE).When(Concepts.ALWAYS))
                .Face(Expression.Sad)
                .With(Gesture.Nod);
        }

        /// <summary>主人公の家の前で立ち止まる（言葉にならない声）。</summary>
        public static Utterance EpilogueHomePause(string residentId)
        {
            return new Utterance(residentId, "story.epilogue.home")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM))
                .Face(Expression.Thinking)
                .With(Gesture.Think);
        }

        /// <summary>案内役が新しい住民を迎える：「こんにちは」＋口癖（主人公も、迎えられた一人だった）。</summary>
        public static Utterance GuideWelcomesNewcomer(string guideId, string catchphrase)
        {
            return new Utterance(guideId, "story.epilogue.welcome")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, catchphrase))
                .Face(Expression.VeryHappy)
                .With(Gesture.Wave);
        }
    }
}
