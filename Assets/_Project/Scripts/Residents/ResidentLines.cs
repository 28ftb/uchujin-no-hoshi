using System.Collections.Generic;
using Uchujin.Core;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// 住民の発話（意味データ）を組み立てる（宇宙語仕様 §5・§14、住民生活仕様 §13）。
    /// 日本語の文は作らない。発話の意味は、話した時点の住民の状態・記憶・予定と矛盾しないものだけを選ぶ。
    /// （例：「明日は雨」は天気読みの予報を知っている住民だけが言う。「昨日魚を買った」は実際に買った住民だけが言う）
    /// </summary>
    public static class ResidentLines
    {
        // ================= 基本の部品 =================

        public static MeaningFrame F(SpeechAct act, string predicate) => MeaningFrame.Make(act, predicate);

        public static MeaningArg ItemArg(string itemId, bool withColor = true)
        {
            var def = ItemRegistry.Get(itemId);
            if (def == null) return MeaningArg.Of(Concepts.Item(itemId));
            var arg = MeaningArg.Of(def.EffectiveConceptId);
            if (withColor)
            {
                var c = Concepts.ColorFromTags(def);
                if (c != null) arg.Adj(c);
            }
            return arg;
        }

        public static MeaningArg GenericArg(string itemId)
        {
            var def = ItemRegistry.Get(itemId);
            return MeaningArg.Of(Concepts.GenericOf(def));
        }

        /// <summary>主人公の呼び方（最初は「変な生き物」、親しくなると住民が付けた呼び名）。住民生活仕様 §7。</summary>
        public static string PlayerConcept(Resident r, string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return Concepts.STRANGE_ONE;
            return r.GetFriendship(playerId) >= ResidentTuning.NameCallFriendship ? Concepts.Name("player:" + playerId) : Concepts.STRANGE_ONE;
        }

        public static string WeatherFeelingExpr(Resident r, WeatherState w)
        {
            if (w.IsRaining) return r.P.rainLike > 0.3f ? "happy" : (r.P.rainLike < -0.3f ? "sad" : "neutral");
            if (w.Has(AlienPhenomenon.StarBand) || w.Has(AlienPhenomenon.LightRain) || w.Has(AlienPhenomenon.BloomTide)) return "happy";
            if (w.sky <= SkyState.Sunny) return r.P.rainLike < 0.3f ? "happy" : "neutral";
            return "neutral";
        }

        /// <summary>天気を表す文（時間・天候仕様 §14）。tomorrow=true なら未来形＋「明日」。</summary>
        public static MeaningFrame WeatherFrame(WeatherState w, string timeWord, bool future)
        {
            MeaningFrame f;
            string main = w.MainConcept();
            if (main == Concepts.RAIN)
            {
                f = F(SpeechAct.Statement, Concepts.RAIN_VERB);
                if (w.precipitation == Precipitation.HeavyRain) f.Adverb(Concepts.VERY);
                else if (w.precipitation == Precipitation.Drizzle) f.Adverb(Concepts.A_LITTLE);
            }
            else if (main == Concepts.LIGHT_RAIN) f = F(SpeechAct.Statement, Concepts.FALL).Agent(Concepts.LIGHT_RAIN);
            else if (main == Concepts.REVERSE_RAIN) f = F(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.REVERSE_RAIN);
            else if (main == Concepts.STAR_WIND) f = F(SpeechAct.Statement, Concepts.BLOW).Agent(Concepts.STAR_WIND);
            else if (main == Concepts.STAR_BAND) f = F(SpeechAct.Statement, Concepts.SHINE).Agent(Concepts.STAR_BAND);
            else if (main == Concepts.FOG) f = F(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.FOG);
            else if (main == Concepts.WIND) f = F(SpeechAct.Statement, Concepts.BLOW).Agent(Concepts.WIND).Adverb(Concepts.VERY);
            else if (main == Concepts.CLOUD) f = F(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.CLOUD, Concepts.MANY));
            else f = F(SpeechAct.Statement, Concepts.SHINE).Agent(Concepts.SUN);
            if (!string.IsNullOrEmpty(timeWord)) f.When(timeWord);
            if (future) f.Future();
            return f;
        }

        private static Gesture WeatherGesture(WeatherState w)
        {
            if (w.IsRaining) return Gesture.Shiver;
            if (w.wind == WindLevel.Strong) return Gesture.Shiver;
            return Gesture.LookUp;
        }

        // ================= 天気予報（天気読み → 伝言広場のマネピ） =================

        /// <summary>
        /// 明日の天気予報。GetPlanned（生成済みの予定）に基づくので内容は真実。
        /// 昼の天気を主文にし、朝・夕で大きく違えば2文目を付ける。
        /// </summary>
        public static Utterance Forecast(Resident r, IWeatherService weather, int tomorrow)
        {
            var u = new Utterance(r.Id, "weather.forecast");
            var noon = weather.GetPlanned(tomorrow, 13);
            var morning = weather.GetPlanned(tomorrow, 8);
            var evening = weather.GetPlanned(tomorrow, 19);
            var night = weather.GetPlanned(tomorrow, 22);
            u.Say(WeatherFrame(noon, Concepts.TOMORROW, true));
            if (morning.MainConcept() != noon.MainConcept()) u.Say(WeatherFrame(morning, Concepts.MORNING, true));
            else if (evening.MainConcept() != noon.MainConcept()) u.Say(WeatherFrame(evening, Concepts.EVENING, true));
            if (night.Has(AlienPhenomenon.StarBand) && !noon.Has(AlienPhenomenon.StarBand))
            {
                u.Say(WeatherFrame(night, Concepts.NIGHT, true));
                u.Say(F(SpeechAct.Invitation, Concepts.SEE).Agent(Concepts.WE).Patient(Concepts.STAR_BAND).At(Concepts.HILL).Adverb(Concepts.TOGETHER));
            }
            string feel = WeatherFeelingExpr(r, noon);
            u.Face(feel == "happy" ? Expression.Happy : feel == "sad" ? Expression.Worried : Expression.Neutral);
            u.With(Gesture.PointAt, "sky").With(WeatherGesture(noon));
            return u;
        }

        // ================= 挨拶 =================

        public static Utterance Greeting(Resident r, string playerId, bool firstMeet, string guideCatchphrase, int hour)
        {
            var u = new Utterance(r.Id, firstMeet ? "greeting.first" : "greeting");
            int fr = r.GetFriendship(playerId);
            bool night = hour >= 21 || hour < 4;
            u.Say(F(night && !firstMeet ? SpeechAct.Farewell : SpeechAct.Greeting, night && !firstMeet ? Concepts.GOOD_NIGHT : Concepts.HELLO));
            if (!string.IsNullOrEmpty(guideCatchphrase))
            {
                // 案内役は出会いからずっと同じ言葉をかける（メインストーリー第1章・最終章）
                u.Say(F(SpeechAct.Exclamation, guideCatchphrase));
            }
            if (firstMeet)
            {
                // 自己紹介（名前は宇宙語。プレイヤーは推測するしかない）＋空から来たのかと尋ねる
                u.Say(F(SpeechAct.Statement, Concepts.Name(r.Id)).Agent(Concepts.I));
                u.With(Gesture.PointAt, r.Id);
                if (r.P.curiosity > 0.5f)
                    u.Say(F(SpeechAct.Question, Concepts.COME).Agent(Concepts.YOU).At(Concepts.SKY).Past());
                u.Face(r.P.shyness > 0.6f ? Expression.Shy : Expression.Surprised);
                u.With(r.P.shyness > 0.6f ? Gesture.Bow : Gesture.Wave);
            }
            else
            {
                if (fr >= ResidentTuning.NameCallFriendship && r.P.talkativeness > 0.4f)
                    u.Say(F(SpeechAct.Statement, Concepts.MEET).Agent(Concepts.I).Patient(PlayerConcept(r, playerId)).Adverb(Concepts.AGAIN));
                u.Face(fr >= 50 ? Expression.VeryHappy : fr >= 20 ? Expression.Happy : (r.P.shyness > 0.6f ? Expression.Shy : Expression.Neutral));
                u.With(Gesture.Wave);
            }
            return u;
        }

        // ================= 主人公への話題 =================

        private class Topic
        {
            public float weight;
            public System.Action<Utterance> build;
        }

        /// <summary>今の本当の状態から話題を1〜2個選んで発話にする（住民生活仕様 §13：会話の3種類）。</summary>
        public static Utterance TalkTopic(Resident r, string playerId, ResidentSystem sys, SeededRandom rng)
        {
            var u = new Utterance(r.Id, "talk");
            var topics = CollectTopics(r, playerId, sys, false, null);
            if (topics.Count == 0) return null;
            int n = r.P.talkativeness > 0.65f && topics.Count > 1 ? 2 : 1;
            for (int i = 0; i < n && topics.Count > 0; i++)
            {
                var t = rng.WeightedPick(topics, x => x.weight);
                topics.Remove(t);
                t.build(u);
            }
            if (u.sentences.Count == 0) return null;
            return u;
        }

        private static List<Topic> CollectTopics(Resident r, string playerId, ResidentSystem sys, bool overheard, Resident listener)
        {
            var list = new List<Topic>();
            int day = sys.Today;
            var st = r.State;
            void Add(float w, System.Action<Utterance> b) { if (w > 0) list.Add(new Topic { weight = w, build = b }); }

            // ---- 引っ越し（第4章）----
            if (r.IsMovingOut)
            {
                int left = st.moveOutDay - day;
                Add(6f, u =>
                {
                    var f = F(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(Concepts.I).Future();
                    f.When(left <= 1 ? Concepts.TOMORROW : Concepts.SOON);
                    var why = ReasonFrame(r, sys);
                    if (why != null) f.Because(why);
                    u.Say(f);
                    if (left > 1) u.With(Gesture.ShowNumber, null, left);
                    u.Face(r.P.sociability > 0.5f ? Expression.Sad : Expression.Thinking);
                });
            }
            if (!overheard && st.movedInDay >= 0 && day - st.movedInDay <= 2 && st.movedInDay > 0)
            {
                Add(4f, u =>
                {
                    u.Say(F(SpeechAct.Statement, Concepts.COME).Agent(Concepts.I).At(Concepts.VILLAGE).Past().When(st.movedInDay == day ? Concepts.TODAY : Concepts.YESTERDAY));
                    u.Say(F(SpeechAct.Statement, Concepts.NEW).Agent(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.I)));
                    u.With(Gesture.PointAt, st.homeFacilityId).Face(Expression.Excited);
                });
            }

            // ---- 誕生日 ----
            if (r.IsBirthday(day))
                Add(5f, u => u.Say(F(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.BIRTHDAY).OwnedBy(Concepts.I)).When(Concepts.TODAY)).Face(Expression.Excited).With(Gesture.Jump));

            // ---- 今していること ----
            Add(2f, u => BuildActivity(r, u, sys));

            // ---- 空腹 ----
            if (st.hunger > ResidentTuning.HungryThreshold)
                Add(2.5f, u => u.Say(F(SpeechAct.Statement, Concepts.HUNGRY).Agent(Concepts.I).Adverb(st.hunger > 0.8f ? Concepts.VERY : Concepts.A_LITTLE)).With(Gesture.Eat).Face(Expression.Worried));

            // ---- 買った物（主人公の商店で買った物の感想は特に重要：商店 §10） ----
            if (!string.IsNullOrEmpty(st.lastBoughtItemId) && day - st.lastBoughtDay <= 1)
            {
                string when = st.lastBoughtDay == day ? Concepts.TODAY : Concepts.YESTERDAY;
                Add(st.lastBoughtFromPlayer ? 4f : 2f, u =>
                {
                    var f = F(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(ItemArg(st.lastBoughtItemId)).When(when).Past();
                    if (st.lastBoughtFromPlayer)
                    {
                        if (!overheard) f.location = MeaningArg.Of(Concepts.SHOP).OwnedBy(Concepts.YOU);
                        else f.At(Concepts.SHOP);
                    }
                    u.Say(f);
                    if (st.lastBoughtFromPlayer)
                    {
                        if (st.lastBoughtSatisfaction > 0.3f) u.Say(F(SpeechAct.Statement, Concepts.PRICE_CHEAP).Agent(Concepts.THAT)).Face(Expression.Happy);
                        else if (st.lastBoughtSatisfaction < -0.2f) u.Say(F(SpeechAct.Statement, Concepts.PRICE_EXPENSIVE).Agent(Concepts.THAT).Adverb(Concepts.A_LITTLE)).Face(Expression.Worried);
                    }
                    u.Related(st.lastBoughtItemId).With(Gesture.Hold, st.lastBoughtItemId);
                });
            }
            // ---- 食べた物 ----
            if (!string.IsNullOrEmpty(st.lastAteItemId) && day - st.lastAteDay <= 1)
            {
                string when = st.lastAteDay == day ? Concepts.TODAY : Concepts.YESTERDAY;
                Add(2.5f, u =>
                {
                    u.Say(F(SpeechAct.Statement, Concepts.EAT).Agent(Concepts.I).Patient(ItemArg(st.lastAteItemId)).When(when).Past());
                    bool good = st.lastAteLiking >= 0f;
                    u.Say(F(SpeechAct.Statement, good ? Concepts.DELICIOUS : Concepts.BAD_TASTE).Agent(Concepts.THAT).Adverb(Mathf.Abs(st.lastAteLiking) > 0.5f ? Concepts.VERY : Concepts.A_LITTLE));
                    u.Face(good ? Expression.Happy : Expression.Disgusted).With(Gesture.Eat, st.lastAteItemId).Related(st.lastAteItemId);
                });
            }
            // ---- 釣った魚 ----
            if (!string.IsNullOrEmpty(st.lastCaughtItemId) && day - st.lastCaughtDay <= 1)
            {
                Add(2.5f, u =>
                {
                    u.Say(F(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).Patient(ItemArg(st.lastCaughtItemId)).When(st.lastCaughtDay == day ? Concepts.TODAY : Concepts.YESTERDAY).Past());
                    u.Face(Expression.Excited).With(Gesture.ShowNumber, null, 1).Related(st.lastCaughtItemId);
                });
            }
            // ---- 家に飾った物 ----
            if (st.homeItems.Count > 0)
            {
                string last = st.homeItems[st.homeItems.Count - 1];
                Add(1.2f, u => u.Say(F(SpeechAct.Statement, Concepts.PUT).Agent(Concepts.I).Patient(ItemArg(last)).At(Concepts.HOUSE).Past()).Face(Expression.Happy).Related(last));
            }
            // ---- 明日の天気（予報を知っている住民だけ） ----
            var weather = Game.Instance != null ? Game.Instance.Service<IWeatherService>() : null;
            if (weather != null && st.forecastKnownDay == day)
            {
                var w = weather.GetPlanned(day + 1, 13);
                Add(2f, u =>
                {
                    u.Say(WeatherFrame(w, Concepts.TOMORROW, true));
                    string feel = WeatherFeelingExpr(r, w);
                    if (feel == "sad") u.Say(F(SpeechAct.Statement, Concepts.DISLIKE).Agent(Concepts.I).Patient(Concepts.RAIN)).Face(Expression.Worried);
                    else if (feel == "happy" && w.IsRaining) u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.RAIN)).Face(Expression.Happy);
                    u.With(Gesture.LookUp);
                });
            }
            // ---- 今の天気 ----
            if (weather != null)
            {
                var now = weather.Current;
                Add(now.IsRaining || now.phenomena != AlienPhenomenon.None ? 1.6f : 0.6f, u =>
                {
                    u.Say(WeatherFrame(now, Concepts.NOW, false));
                    string feel = WeatherFeelingExpr(r, now);
                    u.Face(feel == "happy" ? Expression.Happy : feel == "sad" ? Expression.Worried : Expression.Neutral).With(Gesture.LookUp);
                });
            }
            // ---- 明日は休み（勤務予定と一致：宇宙語仕様 §5.2） ----
            if (sys.IsRestDayOn(day + 1) && JobTable.Workplace(st.job).HasValue && !sys.IsRestDayOn(day))
                Add(1.5f, u => u.Say(F(SpeechAct.Statement, Concepts.REST).Agent(Concepts.I).When(Concepts.TOMORROW).Future()).Face(Expression.Happy));

            // ---- 目標（中期） ----
            if (st.midGoal.kind != GoalKind.None && !st.midGoal.IsDone)
                Add(1.8f, u => BuildGoalDesire(r, u));
            // ---- 夢（長期）：親しい相手にだけ打ち明ける ----
            bool close = overheard ? (listener != null && r.Affinity(listener.Id) > 40f) : r.GetFriendship(playerId) >= ResidentTuning.ConfideFriendship;
            if (close && r.Profile.dream != GoalKind.None)
                Add(2.2f, u => BuildDream(r, u, sys));

            // ---- 約束 ----
            foreach (var pr in st.promises)
            {
                if (pr.day < day || pr.day > day + 1) continue;
                var promise = pr;
                Add(3f, u =>
                {
                    var f = F(SpeechAct.Statement, ActivityVerb(promise.activity)).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).When(promise.day == day ? Concepts.TODAY : Concepts.TOMORROW).Future();
                    if (!overheard || listener == null || listener.Id != promise.partnerId)
                        f.Agent(MeaningArg.Of(Concepts.Name(promise.partnerId)));
                    u.Say(f).Face(Expression.Happy).Related(promise.partnerId);
                });
            }

            // ---- 友達・喧嘩・引っ越した友達（住民生活仕様 §5・§15） ----
            foreach (var rel in st.relations)
            {
                if (overheard && listener != null && rel.otherId == listener.Id) continue;
                var other = sys.GetResident(rel.otherId);
                if (other == null) continue;
                var rr = rel;
                if (rr.quarrelUntilDay >= day)
                {
                    Add(2.2f, u => u.Say(F(SpeechAct.Statement, Concepts.FIGHT).Agent(Concepts.I).Patient(Concepts.Name(rr.otherId)).Past()).Face(Expression.Angry).Related(rr.otherId));
                }
                else if (!other.State.active && other.State.movedOutDay >= 0 && rr.affinity > 30f && day - other.State.movedOutDay < 20)
                {
                    Add(2f, u =>
                    {
                        u.Say(F(SpeechAct.Statement, Concepts.LEAVE).Agent(Concepts.Name(rr.otherId)).Past());
                        u.Say(F(SpeechAct.Statement, Concepts.LONELY).Agent(Concepts.I));
                        u.Face(Expression.Sad).Related(rr.otherId);
                    });
                }
                else if (other.State.active && rr.affinity > 55f)
                {
                    Add(0.8f, u => u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.Name(rr.otherId))).Face(Expression.Happy).Related(rr.otherId));
                }
                else if (other.State.active && other.IsMovingOut && rr.affinity > 20f)
                {
                    Add(2.5f, u =>
                    {
                        u.Say(F(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(Concepts.Name(rr.otherId)).When(Concepts.SOON).Future());
                        u.Say(F(SpeechAct.Statement, Concepts.SAD).Agent(Concepts.I));
                        u.Face(Expression.Sad).Related(rr.otherId);
                    });
                }
            }
            // ---- 新しい住民 ----
            var newest = sys.NewestResident();
            if (newest != null && newest != r && day - newest.State.movedInDay <= 3 && newest.State.movedInDay > 0)
                Add(1.8f, u => u.Say(F(SpeechAct.Statement, Concepts.COME).Agent(MeaningArg.Of(Concepts.NEIGHBOR, Concepts.NEW)).Past()).With(Gesture.PointAt, newest.Id).Face(Expression.Excited).Related(newest.Id));

            // ---- 主人公の話題（§7：住民が実際に見聞きしたことだけ） ----
            AddPlayerTopics(r, playerId, sys, overheard, list);

            return list;
        }

        private static void AddPlayerTopics(Resident r, string playerId, ResidentSystem sys, bool overheard, List<Topic> list)
        {
            int day = sys.Today;
            // 話題にする主人公（直接の会話ならその人、立ち話なら記憶にある主人公）
            string pid = playerId;
            if (overheard)
            {
                pid = null;
                for (int i = r.State.memories.Count - 1; i >= 0; i--)
                {
                    var m = r.State.memories[i];
                    if (!string.IsNullOrEmpty(m.playerId) && day - m.day <= 5) { pid = m.playerId; break; }
                }
                if (pid == null) return;
            }
            string who = overheard ? PlayerConcept(r, pid) : Concepts.YOU;

            var gift = r.LatestMemory("gift", pid, day - 3);
            if (gift != null)
            {
                list.Add(new Topic
                {
                    weight = overheard ? 2.5f : 3f,
                    build = u =>
                    {
                        var f = F(SpeechAct.Statement, Concepts.GIVE).Agent(who).Patient(ItemArg(gift.itemId)).Recipient(Concepts.I).Past();
                        f.When(gift.day == day ? Concepts.TODAY : gift.day == day - 1 ? Concepts.YESTERDAY : null);
                        u.Say(f);
                        if (!overheard) u.Say(F(SpeechAct.Thanks, Concepts.THANKS));
                        u.Face(Expression.Happy).Related(gift.itemId);
                    }
                });
            }
            int fishSeen = r.CountMemories("saw_fishing", pid, day - 7) + r.CountMemories("heard_fishing", pid, day - 7);
            if (fishSeen > 0)
            {
                list.Add(new Topic
                {
                    weight = 1.5f + fishSeen * 0.3f,
                    build = u =>
                    {
                        var f = F(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(who).At(Concepts.RIVER);
                        if (fishSeen >= 3) f.Adverb(Concepts.ALWAYS);
                        u.Say(f).Face(Expression.Happy);
                        var rare = r.LatestMemory("saw_rare_fish", pid, day - 7);
                        if (rare != null) u.Say(F(SpeechAct.Statement, Concepts.RARE).Agent(ItemArg(rare.itemId))).Related(rare.itemId).Face(Expression.Excited);
                    }
                });
            }
            int mineSeen = r.CountMemories("saw_mining", pid, day - 7) + r.CountMemories("heard_mining", pid, day - 7);
            if (mineSeen > 0)
            {
                var rep = sys.GetReputation(pid);
                list.Add(new Topic
                {
                    weight = 1.5f + mineSeen * 0.4f,
                    build = u =>
                    {
                        var f = F(SpeechAct.Statement, Concepts.DIG).Agent(who).At(Concepts.MINE);
                        if (rep != null && rep.mineStreak >= 3) f.When(Concepts.EVERY_DAY);
                        u.Say(f);
                        if (rep != null && rep.deepestFloor >= 5) u.Say(F(SpeechAct.Statement, Concepts.GO).Agent(who).At(Concepts.DOWN).Adverb(Concepts.VERY).Past());
                        u.Face(rep != null && rep.deepestFloor >= 5 ? Expression.Surprised : Expression.Neutral).With(Gesture.PointAt, "mine");
                    }
                });
            }
            int cropSeen = r.CountMemories("saw_harvest", pid, day - 7) + r.CountMemories("heard_harvest", pid, day - 7);
            if (cropSeen > 0)
            {
                var big = r.LatestMemory("saw_big_crop", pid, day - 7);
                list.Add(new Topic
                {
                    weight = 1.2f + cropSeen * 0.3f,
                    build = u =>
                    {
                        if (big != null) u.Say(F(SpeechAct.Statement, Concepts.GROW).Agent(who).Patient(MeaningArg.Of(Concepts.VEGETABLE, Concepts.BIG)).Past().Adverb(Concepts.VERY)).Face(Expression.Surprised).Related(big.itemId);
                        else u.Say(F(SpeechAct.Statement, Concepts.GROW).Agent(who).Patient(Concepts.CROP).At(Concepts.FARM)).Face(Expression.Happy);
                    }
                });
            }
            var creature = r.LatestMemory("saw_creature", pid, day - 5) ?? r.LatestMemory("heard_creature", pid, day - 5);
            if (creature != null)
            {
                list.Add(new Topic
                {
                    weight = 1.5f,
                    build = u =>
                    {
                        u.Say(F(SpeechAct.Statement, Concepts.CATCH).Agent(who).Patient(MeaningArg.Of(Concepts.Species(creature.itemId))).Past());
                        if (creature.importance > 0.7f) u.Say(F(SpeechAct.Statement, Concepts.RARE).Agent(Concepts.THAT)).Face(Expression.Surprised);
                        u.Related(creature.itemId);
                    }
                });
            }
            var bought = r.LatestMemory("bought_from_shop", pid, day - 4);
            if (bought != null && overheard)
            {
                list.Add(new Topic
                {
                    weight = 2f,
                    build = u =>
                    {
                        u.Say(F(SpeechAct.Statement, Concepts.SELL).Agent(who).Patient(ItemArg(bought.itemId)).At(Concepts.SHOP));
                        var rep = sys.GetReputation(pid);
                        if (rep != null && rep.shopSalesRecent >= 6) u.Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(Concepts.SHOP).OwnedBy(who)).Adverb(Concepts.VERY));
                        u.Face(Expression.Happy).Related(bought.itemId);
                    }
                });
            }
            var repair = r.LatestMemory("saw_repair", pid, day - 3) ?? r.LatestMemory("heard_repair", pid, day - 3);
            if (repair != null)
            {
                list.Add(new Topic
                {
                    weight = 2f,
                    build = u =>
                    {
                        u.Say(F(SpeechAct.Statement, Concepts.REPAIR).Agent(who).Patient(MeaningArg.Of(Concepts.ROCKET).OwnedBy(who == Concepts.YOU ? Concepts.YOU : who)).Past());
                        u.Face(r.State.job == ResidentJob.Mechanic || r.Profile.dream == GoalKind.TravelStars ? Expression.Excited : Expression.Happy).With(Gesture.PointAt, "rocket");
                    }
                });
            }
            var helped = r.LatestMemory("quest_done", pid, day - 5);
            if (helped != null && !overheard)
                list.Add(new Topic { weight = 2.5f, build = u => u.Say(F(SpeechAct.Statement, Concepts.HELP).Agent(Concepts.YOU).Patient(Concepts.I).Past()).Say(F(SpeechAct.Thanks, Concepts.THANKS)).Face(Expression.Happy).With(Gesture.Bow) });
            // 2人が最近よく一緒にいる（§7「最近あの2人は仲が良さそう」）
            if (overheard)
            {
                foreach (var rel in r.State.relations)
                {
                    if (rel.affinity < 50f || rel.timesTogether < 4) continue;
                    var other = sys.GetResident(rel.otherId);
                    if (other == null || !other.State.active) continue;
                    if (other.GetFriendship(pid) >= 40)
                    {
                        string oid = rel.otherId;
                        list.Add(new Topic { weight = 1f, build = u => u.Say(F(SpeechAct.Statement, Concepts.FRIEND).Agent(MeaningArg.Of(Concepts.Name(oid))).Patient(who)).Face(Expression.Happy).Related(oid) });
                        break;
                    }
                }
            }
        }

        private static void BuildActivity(Resident r, Utterance u, ResidentSystem sys)
        {
            var st = r.State;
            switch (st.activity)
            {
                case ResidentActivity.Working:
                case ResidentActivity.Crafting:
                    u.Say(F(SpeechAct.Statement, JobTable.JobVerb(st.job)).Agent(Concepts.I).Patient(JobTable.JobConcept(st.job)).When(Concepts.NOW));
                    if (st.job == ResidentJob.Blacksmith && st.midGoal.kind == GoalKind.Masterpiece)
                        u.Say(F(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PICKAXE, Concepts.STRONG, Concepts.NEW)));
                    u.Face(r.P.easygoing > 0.6f ? Expression.Happy : Expression.Thinking).With(Gesture.Hold);
                    break;
                case ResidentActivity.Fishing:
                    u.Say(F(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).When(Concepts.NOW));
                    u.Say(F(SpeechAct.Statement, Concepts.QUIET).Agent(Concepts.YOU)).With(Gesture.Shoo);
                    u.Face(Expression.Thinking);
                    break;
                case ResidentActivity.Gardening:
                case ResidentActivity.Farming:
                    u.Say(F(SpeechAct.Statement, Concepts.GROW).Agent(Concepts.I).Patient(st.job == ResidentJob.FarmingMaster ? Concepts.VEGETABLE : Concepts.FLOWER));
                    u.Say(F(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(Concepts.FLOWER)).With(Gesture.PointAt, "flower").Face(Expression.Happy);
                    break;
                case ResidentActivity.Cooking:
                    u.Say(F(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Patient(Concepts.DISH).When(Concepts.NOW)).Face(Expression.Excited);
                    if (r.P.generosity > 0.6f) u.Say(F(SpeechAct.Invitation, Concepts.EAT).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).When(Concepts.LATER));
                    break;
                case ResidentActivity.Eating:
                    u.Say(F(SpeechAct.Statement, Concepts.EAT).Agent(Concepts.I).When(Concepts.NOW)).With(Gesture.Eat).Face(Expression.Happy);
                    break;
                case ResidentActivity.WatchingSky:
                    u.Say(F(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.I).Patient(sys.IsNight ? Concepts.STAR : Concepts.SKY)).With(Gesture.LookUp).Face(Expression.Love);
                    break;
                case ResidentActivity.CatchingCreatures:
                    u.Say(F(SpeechAct.Statement, Concepts.LOOK_FOR).Agent(Concepts.I).Patient(Concepts.CREATURE)).Face(Expression.Excited);
                    break;
                case ResidentActivity.Shopping:
                    u.Say(F(SpeechAct.Statement, Concepts.GO).Agent(Concepts.I).At(Concepts.SHOP)).Face(Expression.Happy).With(Gesture.PointAt, "shop");
                    break;
                case ResidentActivity.Packing:
                    if (r.IsMovingOut) u.Say(F(SpeechAct.Statement, Concepts.CARRY).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.BOX).Plural())).Face(Expression.Worried).With(Gesture.Hold);
                    else u.Say(F(SpeechAct.Statement, Concepts.PUT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.BOX).Plural()).At(Concepts.HOUSE)).Face(Expression.Excited).With(Gesture.Hold);
                    break;
                case ResidentActivity.ShelteringFromRain:
                    u.Say(F(SpeechAct.Statement, Concepts.RAIN_VERB)).Say(F(SpeechAct.Statement, Concepts.WAIT).Agent(Concepts.I)).Face(Expression.Worried).With(Gesture.LookUp);
                    break;
                case ResidentActivity.Playing:
                    u.Say(F(SpeechAct.Invitation, Concepts.PLAY).Agent(Concepts.WE).Adverb(Concepts.TOGETHER)).Face(Expression.Excited).With(Gesture.Jump);
                    break;
                case ResidentActivity.Celebrating:
                    u.Say(F(SpeechAct.Statement, Concepts.CELEBRATE).Agent(Concepts.WE)).Face(Expression.VeryHappy).With(Gesture.Dance);
                    break;
                case ResidentActivity.Exploring:
                    u.Say(F(SpeechAct.Statement, Concepts.LOOK_FOR).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THING, Concepts.RARE))).Face(Expression.Thinking);
                    break;
                case ResidentActivity.Resting:
                    u.Say(F(SpeechAct.Statement, Concepts.REST).Agent(Concepts.I)).Face(Expression.Sleepy).With(Gesture.Stretch);
                    break;
                case ResidentActivity.Visiting:
                    if (!string.IsNullOrEmpty(st.activityTarget) && sys.GetResident(st.activityTarget) != null)
                        u.Say(F(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(Concepts.Name(st.activityTarget))).Face(Expression.Happy).Related(st.activityTarget);
                    else u.Say(F(SpeechAct.Statement, Concepts.WALK).Agent(Concepts.I));
                    break;
                default:
                    if (sys.IsNight) u.Say(F(SpeechAct.Statement, Concepts.TIRED).Agent(Concepts.I)).Face(Expression.Sleepy).With(Gesture.Stretch);
                    else
                    {
                        var hobbyObj = JobTable.HobbyObject(r.Profile.hobby);
                        var f = F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I);
                        if (hobbyObj != null) f.Patient(hobbyObj);
                        u.Say(f).Face(Expression.Happy);
                    }
                    break;
            }
        }

        private static void BuildGoalDesire(Resident r, Utterance u)
        {
            var g = r.State.midGoal;
            switch (g.kind)
            {
                case GoalKind.BuyFurniture:
                    u.Say(F(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FURNITURE, Concepts.NEW)));
                    if (r.State.money < 400) u.Say(F(SpeechAct.Statement, Concepts.FEW).Agent(MeaningArg.Of(Concepts.MONEY).OwnedBy(Concepts.I))).Face(Expression.Worried);
                    break;
                case GoalKind.NewRecipe:
                    u.Say(F(SpeechAct.Desire, Concepts.COOK).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.DISH, Concepts.NEW)));
                    u.Say(F(SpeechAct.Statement, Concepts.NEED).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.VEGETABLE).Plural())).With(Gesture.ShowNumber, null, Mathf.Max(1, Mathf.CeilToInt(g.target - g.progress)));
                    break;
                case GoalKind.RaiseCreature:
                    u.Say(F(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(Concepts.CREATURE)).Face(Expression.Love);
                    break;
                case GoalKind.BigFish:
                    u.Say(F(SpeechAct.Desire, Concepts.CATCH_FISH).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISH, Concepts.BIG))).Face(Expression.Excited);
                    break;
                case GoalKind.Masterpiece:
                    u.Say(F(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THING, Concepts.BEAUTIFUL)));
                    u.Say(F(SpeechAct.Statement, Concepts.NEED).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ORE).Plural())).With(Gesture.PointAt, "mine");
                    break;
                case GoalKind.DecorateHome:
                    u.Say(F(SpeechAct.Desire, Concepts.DECORATE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.I)));
                    if (!string.IsNullOrEmpty(r.Profile.favoriteColorTag))
                        u.Say(F(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FLOWER, ColorConcept(r.Profile.favoriteColorTag))));
                    break;
                case GoalKind.SaveMoney:
                    u.Say(F(SpeechAct.Statement, Concepts.WORK).Agent(Concepts.I).Adverb(Concepts.VERY)).Say(F(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.MONEY, Concepts.MANY)));
                    break;
                case GoalKind.LearnSkill:
                    u.Say(F(SpeechAct.Statement, Concepts.LEARN).Agent(Concepts.I).Patient(Concepts.JOB)).Face(Expression.Thinking);
                    break;
            }
        }

        private static void BuildDream(Resident r, Utterance u, ResidentSystem sys)
        {
            switch (r.Profile.dream)
            {
                case GoalKind.TravelStars:
                    { var f = F(SpeechAct.Desire, Concepts.GO).Agent(Concepts.I).When(Concepts.SOMEDAY); f.location = MeaningArg.Of(Concepts.PLANET, Concepts.DIFFERENT); u.Say(f); }
                    u.With(Gesture.PointAt, "sky").With(Gesture.LookUp).Face(Expression.Love);
                    break;
                case GoalKind.BuildSpaceship:
                    u.Say(F(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.ROCKET).OwnedBy(Concepts.I)).When(Concepts.SOMEDAY));
                    u.With(Gesture.PointAt, "rocket").Face(Expression.Excited);
                    break;
                case GoalKind.OwnShop:
                    u.Say(F(SpeechAct.Desire, Concepts.OPEN).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.SHOP).OwnedBy(Concepts.I)).When(Concepts.SOMEDAY)).Face(Expression.Love);
                    break;
                case GoalKind.BestCraftsman:
                    u.Say(F(SpeechAct.Desire, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THING, Concepts.GOOD)).Adverb(Concepts.VERY)).Face(Expression.Excited);
                    break;
                case GoalKind.FamilyReunion:
                    u.Say(F(SpeechAct.Statement, Concepts.LIVE).Agent(MeaningArg.Of(Concepts.FAMILY).OwnedBy(Concepts.I)).At(Concepts.FAR)).Say(F(SpeechAct.Desire, Concepts.MEET).Agent(Concepts.I).Patient(Concepts.FAMILY)).Face(Expression.Sad);
                    break;
                case GoalKind.QuietLife:
                    u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.VILLAGE).Adverb(Concepts.VERY)).Face(Expression.Happy);
                    break;
            }
        }

        public static MeaningFrameLite ReasonFrame(Resident r, ResidentSystem sys)
        {
            switch (r.State.moveReason)
            {
                case MoveReason.Dream:
                    if (r.Profile.dream == GoalKind.TravelStars) return MeaningFrameLite.Make(Concepts.WANT, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.PLANET, Concepts.DIFFERENT));
                    if (r.Profile.dream == GoalKind.OwnShop) return MeaningFrameLite.Make(Concepts.OPEN, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.SHOP));
                    return MeaningFrameLite.Make(Concepts.LEARN, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.JOB));
                case MoveReason.Family: return MeaningFrameLite.Make(Concepts.WAIT, MeaningArg.Of(Concepts.FAMILY).OwnedBy(Concepts.I));
                case MoveReason.Work: return MeaningFrameLite.Make(Concepts.EXIST, MeaningArg.Of(Concepts.JOB, Concepts.NEW));
                case MoveReason.Friend:
                    return MeaningFrameLite.Make(Concepts.LIVE, MeaningArg.Of(Concepts.FRIEND).OwnedBy(Concepts.I), MeaningArg.Of(Concepts.FAR));
                case MoveReason.OtherPlace: return MeaningFrameLite.Make(Concepts.WANT, MeaningArg.Of(Concepts.I), MeaningArg.Of(Concepts.VILLAGE, Concepts.DIFFERENT));
                default: return null;
            }
        }

        public static string ActivityVerb(ResidentActivity a)
        {
            switch (a)
            {
                case ResidentActivity.Fishing: return Concepts.CATCH_FISH;
                case ResidentActivity.Eating: return Concepts.EAT;
                case ResidentActivity.Cooking: return Concepts.COOK;
                case ResidentActivity.Gardening: return Concepts.GROW;
                case ResidentActivity.CatchingCreatures: return Concepts.CATCH;
                case ResidentActivity.WatchingSky: return Concepts.SEE;
                case ResidentActivity.Playing: return Concepts.PLAY;
                case ResidentActivity.Visiting: return Concepts.VISIT;
                case ResidentActivity.Exploring: return Concepts.WALK;
                default: return Concepts.PLAY;
            }
        }

        public static string ColorConcept(string colorTag)
        {
            switch (colorTag)
            {
                case ItemTags.ColorRed: return Concepts.RED;
                case ItemTags.ColorBlue: return Concepts.BLUE;
                case ItemTags.ColorGreen: return Concepts.GREEN;
                case ItemTags.ColorYellow: return Concepts.YELLOW;
                case ItemTags.ColorWhite: return Concepts.WHITE;
                case ItemTags.ColorBlack: return Concepts.BLACK;
                case ItemTags.ColorPurple: return Concepts.PURPLE;
                case ItemTags.ColorOrange: return Concepts.ORANGE;
                case ItemTags.ColorPink: return Concepts.PINK;
                default: return null;
            }
        }

        // ================= 住民同士の立ち話（言語学習の主材料） =================

        /// <summary>話し手の本当の状態から、相手に向けた一言を作る。topicKey は相手の返事を決めるために返す。</summary>
        public static Utterance ChatOpening(Resident speaker, Resident listener, ResidentSystem sys, SeededRandom rng, out string topicKey)
        {
            topicKey = "greet";
            var u = new Utterance(speaker.Id, "chat");
            u.Related(listener.Id);
            int day = sys.Today;
            var rel = speaker.FindRelation(listener.Id);

            // 喧嘩の後：謝る／仲直り（§5 関係は変化する）
            var madeUp = speaker.LatestMemory("made_up", null, day);
            if (madeUp != null && madeUp.otherResidentId == listener.Id)
            {
                topicKey = "makeup";
                u.Say(F(SpeechAct.Apology, Concepts.SORRY)).Say(F(SpeechAct.Statement, Concepts.MAKE_UP).Agent(Concepts.WE)).Face(Expression.Shy).With(Gesture.Bow);
                return u;
            }
            if (rel != null && rel.quarrelUntilDay >= day)
            {
                topicKey = "quarrel";
                u.Say(F(SpeechAct.Statement, Concepts.ANGRY).Agent(Concepts.I)).Face(Expression.Angry).With(Gesture.ShakeHead);
                return u;
            }

            // 誘い（約束があれば）
            foreach (var pr in speaker.State.promises)
            {
                if (pr.partnerId == listener.Id && (pr.day == day || pr.day == day + 1))
                {
                    topicKey = "promise";
                    u.Say(F(SpeechAct.Invitation, ActivityVerb(pr.activity)).Agent(Concepts.WE).Adverb(Concepts.TOGETHER).When(pr.day == day ? Concepts.TODAY : Concepts.TOMORROW).Future());
                    u.Face(Expression.Excited).With(Gesture.Beckon);
                    return u;
                }
            }

            // 好きかどうかを尋ねる（返事は相手の本当の好みで決まる）
            if (rng.Chance(0.2f))
            {
                string item = sys.PickTopicItem(speaker, rng);
                if (item != null)
                {
                    topicKey = "ask_like:" + item;
                    u.Say(F(SpeechAct.Question, Concepts.LIKE).Agent(Concepts.YOU).Patient(ItemArg(item))).Face(Expression.Thinking).Related(item);
                    return u;
                }
            }

            var topics = CollectTopics(speaker, null, sys, true, listener);
            if (topics.Count == 0 || rng.Chance(0.15f))
            {
                topicKey = "greet";
                u.Say(F(SpeechAct.Greeting, Concepts.HELLO)).Say(F(SpeechAct.Statement, Concepts.Name(listener.Id)));
                u.Face(Expression.Happy).With(Gesture.Wave);
                return u;
            }
            var t = rng.WeightedPick(topics, x => x.weight);
            t.build(u);
            topicKey = "topic";
            if (u.sentences.Count > 0)
            {
                var s0 = u.sentences[0];
                if (s0.predicate == Concepts.RAIN_VERB || s0.timeWord == Concepts.TOMORROW && (s0.predicate == Concepts.SHINE || s0.predicate == Concepts.EXIST || s0.predicate == Concepts.BLOW || s0.predicate == Concepts.FALL))
                    topicKey = "weather";
                else if (s0.predicate == Concepts.MOVE_HOUSE && s0.agent != null && s0.agent.concept == Concepts.I) topicKey = "moving";
                else if (s0.predicate == Concepts.BUY || s0.predicate == Concepts.CATCH_FISH && s0.agent != null && s0.agent.concept == Concepts.I) topicKey = "boast";
                else if (s0.agent != null && s0.agent.concept != null && (s0.agent.concept == Concepts.STRANGE_ONE || s0.agent.concept.StartsWith(Concepts.NamePrefix + "player:"))) topicKey = "gossip";
                else if (s0.predicate == Concepts.HUNGRY) topicKey = "hungry";
                else if (s0.act == SpeechAct.Desire) topicKey = "desire";
            }
            return u;
        }

        /// <summary>相手の返事（返事も返事をする側の本当の状態・好み・記憶に基づく）。</summary>
        public static Utterance ChatReply(Resident replier, Resident speaker, string topicKey, ResidentSystem sys, SeededRandom rng)
        {
            var u = new Utterance(replier.Id, "chat.reply");
            u.Related(speaker.Id);
            int day = sys.Today;
            if (topicKey == null) topicKey = "greet";
            if (topicKey.StartsWith("ask_like:"))
            {
                string item = topicKey.Substring("ask_like:".Length);
                float v = replier.EvaluateItem(ItemRegistry.Get(item));
                if (v > 0.15f)
                {
                    u.Say(F(SpeechAct.Statement, Concepts.YES)).Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(ItemArg(item)).Adverb(v > 0.5f ? Concepts.VERY : Concepts.A_LITTLE));
                    u.Face(Expression.Happy).With(Gesture.Nod);
                }
                else if (v < -0.15f)
                {
                    u.Say(F(SpeechAct.Statement, Concepts.NO)).Say(F(SpeechAct.Statement, Concepts.DISLIKE).Agent(Concepts.I).Patient(ItemArg(item)));
                    u.Face(Expression.Disgusted).With(Gesture.ShakeHead);
                }
                else
                {
                    u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Face(Expression.Thinking).With(Gesture.Shrug);
                }
                return u;
            }
            switch (topicKey)
            {
                case "quarrel":
                    u.Say(F(SpeechAct.Statement, Concepts.ANGRY).Agent(Concepts.I).Adverb(Concepts.ALSO)).Face(Expression.Angry).With(Gesture.Shoo);
                    break;
                case "makeup":
                    u.Say(F(SpeechAct.Statement, Concepts.OK)).Say(F(SpeechAct.Statement, Concepts.FRIEND).Agent(Concepts.WE)).Face(Expression.Happy).With(Gesture.Hug);
                    break;
                case "promise":
                    u.Say(F(SpeechAct.Statement, Concepts.YES)).Say(F(SpeechAct.Statement, Concepts.FUN).Agent(Concepts.THAT).Future()).Face(Expression.Excited).With(Gesture.Jump);
                    break;
                case "weather":
                {
                    var weather = Game.Instance != null ? Game.Instance.Service<IWeatherService>() : null;
                    if (weather != null)
                    {
                        var w = weather.GetPlanned(day + 1, 13);
                        if (w.IsRaining && replier.P.rainLike < -0.3f) u.Say(F(SpeechAct.Statement, Concepts.DISLIKE).Agent(Concepts.I).Patient(Concepts.RAIN)).Face(Expression.Worried).With(Gesture.ShakeHead);
                        else if (w.IsRaining && replier.P.rainLike > 0.3f) u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.RAIN)).Face(Expression.Happy).With(Gesture.Nod);
                        else u.Say(F(SpeechAct.Statement, Concepts.OK)).Face(Expression.Neutral).With(Gesture.LookUp);
                    }
                    else u.Say(F(SpeechAct.Exclamation, Concepts.HMM));
                    break;
                }
                case "moving":
                    u.Say(F(SpeechAct.Statement, Concepts.SAD).Agent(Concepts.I).Adverb(Concepts.VERY));
                    if (replier.Affinity(speaker.Id) > 40f) u.Say(F(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.YOU).When(Concepts.ALWAYS).Future()).Face(Expression.Crying).With(Gesture.Hug);
                    else u.Face(Expression.Sad);
                    break;
                case "boast":
                    if (replier.P.competitive > 0.65f && replier.FindRelation(speaker.Id) != null && replier.FindRelation(speaker.Id).kind == RelationKind.Rival)
                        u.Say(F(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISH, Concepts.BIG)).Past().Adverb(Concepts.ALSO)).Face(Expression.Angry).With(Gesture.ShowNumber, null, 2);
                    else u.Say(F(SpeechAct.Exclamation, Concepts.WOW)).Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.THAT)).Face(Expression.Happy).With(Gesture.Clap);
                    break;
                case "gossip":
                {
                    // 自分も見聞きしていれば同意、そうでなければ驚く（記憶と矛盾しない）
                    bool knows = false;
                    foreach (var m in replier.State.memories)
                        if (!string.IsNullOrEmpty(m.playerId) && day - m.day <= 7 && (m.kind.StartsWith("saw_") || m.kind.StartsWith("heard_"))) { knows = true; break; }
                    if (knows) u.Say(F(SpeechAct.Statement, Concepts.YES)).Say(F(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.I).Adverb(Concepts.ALSO).Past()).Face(Expression.Happy).With(Gesture.Nod);
                    else u.Say(F(SpeechAct.Exclamation, Concepts.WOW)).Say(F(SpeechAct.Statement, Concepts.STRANGE).Agent(Concepts.THAT)).Face(Expression.Surprised);
                    break;
                }
                case "hungry":
                    if (replier.P.generosity > 0.6f && replier.FindItem(Resident.IsEdible) != null)
                    {
                        var food = replier.FindItem(Resident.IsEdible);
                        u.Say(F(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ItemArg(food.itemId)).Recipient(Concepts.YOU)).With(Gesture.OfferItem, food.itemId).Face(Expression.Happy).Related(food.itemId);
                    }
                    else u.Say(F(SpeechAct.Statement, Concepts.HUNGRY).Agent(Concepts.I).Adverb(Concepts.ALSO)).Face(Expression.Worried).With(Gesture.Eat);
                    break;
                case "desire":
                    u.Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.THAT)).Face(Expression.Happy).With(Gesture.Nod);
                    break;
                case "greet":
                    u.Say(F(SpeechAct.Greeting, Concepts.HELLO)).Say(F(SpeechAct.Statement, Concepts.Name(speaker.Id))).Face(Expression.Happy).With(Gesture.Wave);
                    break;
                default:
                {
                    float aff = replier.Affinity(speaker.Id);
                    if (aff > 30f) u.Say(F(SpeechAct.Statement, Concepts.YES)).Face(Expression.Happy).With(Gesture.Nod);
                    else if (rng.Chance(0.5f)) u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Face(Expression.Thinking);
                    else u.Say(F(SpeechAct.Statement, Concepts.OK)).Face(Expression.Neutral);
                    break;
                }
            }
            return u;
        }

        /// <summary>一人のときの独り言（今の状態をそのまま口にする）。</summary>
        public static Utterance SelfTalk(Resident r, ResidentSystem sys, SeededRandom rng)
        {
            var u = new Utterance(r.Id, "self");
            var st = r.State;
            if (st.hunger > 0.7f) return u.Say(F(SpeechAct.Statement, Concepts.HUNGRY).Agent(Concepts.I)).Face(Expression.Worried).With(Gesture.Eat);
            switch (st.activity)
            {
                case ResidentActivity.ShelteringFromRain:
                    return u.Say(F(SpeechAct.Statement, Concepts.RAIN_VERB).Adverb(Concepts.STILL)).Face(Expression.Worried).With(Gesture.LookUp);
                case ResidentActivity.Fishing:
                    if (st.lastCaughtDay == sys.Today && !string.IsNullOrEmpty(st.lastCaughtItemId))
                        return u.Say(F(SpeechAct.Exclamation, Concepts.WOW)).Say(F(SpeechAct.Statement, Concepts.CATCH_FISH).Agent(Concepts.I).Patient(ItemArg(st.lastCaughtItemId)).Past()).Face(Expression.Excited).With(Gesture.Jump).Related(st.lastCaughtItemId);
                    return u.Say(F(SpeechAct.Statement, Concepts.COME).Agent(Concepts.FISH).Not()).Face(Expression.Thinking);
                case ResidentActivity.WatchingSky:
                    return u.Say(F(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(sys.IsNight ? Concepts.STAR : Concepts.SKY).Adverb(Concepts.VERY)).Face(Expression.Love).With(Gesture.LookUp);
                case ResidentActivity.Packing:
                    return u.Say(F(SpeechAct.Statement, Concepts.HEAVY).Agent(Concepts.BOX)).Face(Expression.Worried).With(Gesture.Hold);
                case ResidentActivity.Working:
                case ResidentActivity.Crafting:
                    return u.Say(F(SpeechAct.Statement, Concepts.BUSY).Agent(Concepts.I)).Face(Expression.Thinking);
                case ResidentActivity.Walking:
                case ResidentActivity.Exploring:
                    if (rng.Chance(0.5f)) return u.Say(F(SpeechAct.Statement, Concepts.SING).Agent(Concepts.I)).Face(Expression.Happy).With(Gesture.Dance);
                    break;
                case ResidentActivity.Gardening:
                    return u.Say(F(SpeechAct.Statement, Concepts.GROW).Agent(Concepts.FLOWER).Adverb(Concepts.SLOW)).Face(Expression.Happy);
            }
            if (r.IsBirthday(sys.Today)) return u.Say(F(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).When(Concepts.TODAY)).Face(Expression.VeryHappy).With(Gesture.Spin);
            return null;
        }

        // ================= 主人公の行動への反応 =================

        /// <summary>贈り物への反応（-2..2）。品質と誕生日・親しさで少し変わる。</summary>
        public static int GiftReaction(Resident r, ItemDefinition def, int quality, int day)
        {
            float score = r.EvaluateItem(def) + quality * 0.06f;
            if (r.IsBirthday(day)) score += 0.15f;
            if (def != null && def.Category == ItemCategory.Letter) score = 0.05f;
            if (score > 0.55f) return 2;
            if (score > 0.15f) return 1;
            if (score > -0.15f) return 0;
            if (score > -0.5f) return -1;
            return -2;
        }

        public static Utterance GiftResponse(Resident r, string itemId, int reaction, bool birthday, bool repeated)
        {
            var u = new Utterance(r.Id, "gift.reaction").Related(itemId);
            switch (reaction)
            {
                case 2:
                    u.Say(F(SpeechAct.Exclamation, Concepts.WOW));
                    u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(ItemArg(itemId)).Adverb(Concepts.VERY));
                    u.Say(F(SpeechAct.Thanks, Concepts.THANKS));
                    u.Face(Expression.VeryHappy).With(Gesture.Jump).With(Gesture.Hug);
                    break;
                case 1:
                    u.Say(F(SpeechAct.Thanks, Concepts.THANKS)).Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(ItemArg(itemId)));
                    u.Face(Expression.Happy).With(Gesture.Nod);
                    break;
                case 0:
                    u.Say(F(SpeechAct.Thanks, Concepts.THANKS));
                    u.Face(Expression.Neutral).With(Gesture.Bow);
                    break;
                case -1:
                    u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Say(F(SpeechAct.Thanks, Concepts.THANKS).Adverb(Concepts.A_LITTLE));
                    u.Face(Expression.Worried).With(Gesture.Shrug);
                    break;
                default:
                    u.Say(F(SpeechAct.Statement, Concepts.DISLIKE).Agent(Concepts.I).Patient(ItemArg(itemId)));
                    u.Face(Expression.Disgusted).With(Gesture.ShakeHead);
                    break;
            }
            if (birthday && reaction >= 0) u.Say(F(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).When(Concepts.TODAY).Adverb(Concepts.VERY));
            if (repeated && reaction >= 0) u.Say(F(SpeechAct.Statement, Concepts.MANY).Agent(MeaningArg.Of(Concepts.GIFT).Plural()).When(Concepts.TODAY));
            return u;
        }

        public static Utterance ShowItemResponse(Resident r, string itemId)
        {
            var def = ItemRegistry.Get(itemId);
            float v = r.EvaluateItem(def);
            var u = new Utterance(r.Id, "show.reaction").Related(itemId);
            if (def != null && def.Rarity >= 3 && r.P.curiosity > 0.5f)
                u.Say(F(SpeechAct.Exclamation, Concepts.WOW)).Say(F(SpeechAct.Statement, Concepts.RARE).Agent(Concepts.THIS));
            if (v > 0.5f)
            {
                u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.THIS).Adverb(Concepts.VERY));
                u.Say(F(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(Concepts.THIS));
                u.Face(Expression.Love).With(Gesture.PointAt, itemId);
            }
            else if (v > 0.15f)
            {
                u.Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.THIS)).Face(Expression.Happy).With(Gesture.Nod);
            }
            else if (v < -0.2f)
            {
                u.Say(F(SpeechAct.Statement, Concepts.DISLIKE).Agent(Concepts.I).Patient(Concepts.THIS)).Face(Expression.Disgusted).With(Gesture.ShakeHead);
            }
            else
            {
                u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Face(Expression.Thinking).With(Gesture.Think);
            }
            return u;
        }

        // ================= 伝言（マネピ）・手紙 =================

        public static Utterance MoveNotice(Resident r, int daysLeft, ResidentSystem sys)
        {
            var u = new Utterance(r.Id, "resident.move_notice");
            u.Say(F(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(Concepts.I).When(daysLeft <= 1 ? Concepts.TOMORROW : Concepts.SOON).Future().Because(ReasonFrame(r, sys)));
            u.Say(F(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.EVERYONE));
            u.Face(Expression.Sad).With(Gesture.ShowNumber, null, Mathf.Max(1, daysLeft)).With(Gesture.Bow);
            return u;
        }

        public static Utterance NewcomerHello(Resident r)
        {
            var u = new Utterance(r.Id, "resident.moved_in");
            u.Say(F(SpeechAct.Greeting, Concepts.HELLO));
            u.Say(F(SpeechAct.Statement, Concepts.Name(r.Id)).Agent(Concepts.I));
            u.Say(F(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).At(Concepts.VILLAGE).When(Concepts.TODAY));
            var hobbyObj = JobTable.HobbyObject(r.Profile.hobby);
            if (hobbyObj != null) u.Say(F(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(hobbyObj));
            u.Face(Expression.Excited).With(Gesture.Wave).With(Gesture.Bow);
            return u;
        }

        public static Utterance Farewell(Resident r, string playerId, string giftItemId)
        {
            var u = new Utterance(r.Id, "resident.farewell");
            u.Say(F(SpeechAct.Statement, Concepts.MOVE_HOUSE).Agent(Concepts.I).When(Concepts.TOMORROW).Future());
            u.Say(F(SpeechAct.Thanks, Concepts.THANKS)).Say(F(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.YOU).When(Concepts.ALWAYS).Future());
            if (!string.IsNullOrEmpty(giftItemId))
            {
                u.Say(F(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ItemArg(giftItemId)).Recipient(Concepts.YOU));
                u.With(Gesture.OfferItem, giftItemId).Related(giftItemId);
            }
            u.Face(Expression.Crying).With(Gesture.Hug);
            return u;
        }

        public static Utterance Letter(Resident r, string playerId, ResidentSystem sys)
        {
            var u = new Utterance(r.Id, "resident.letter");
            u.Say(F(SpeechAct.Greeting, Concepts.HELLO).Agent(Concepts.YOU));
            switch (r.State.moveReason)
            {
                case MoveReason.Dream:
                    u.Say(F(SpeechAct.Statement, Concepts.LEARN).Agent(Concepts.I).Patient(Concepts.JOB).When(Concepts.NOW)).Say(F(SpeechAct.Statement, Concepts.DIFFICULT).Agent(Concepts.THAT)).Say(F(SpeechAct.Statement, Concepts.FUN).Agent(Concepts.THAT).Adverb(Concepts.ALSO));
                    break;
                case MoveReason.Family:
                    u.Say(F(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).Patient(Concepts.FAMILY).Adverb(Concepts.TOGETHER)).Say(F(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I));
                    break;
                default:
                    { var f = F(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I); f.location = MeaningArg.Of(Concepts.VILLAGE, Concepts.DIFFERENT); u.Say(f).Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.THAT)); }
                    break;
            }
            u.Say(F(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.YOU));
            string friend = null;
            foreach (var rel in r.State.relations) if (rel.affinity > 45f) { friend = rel.otherId; break; }
            if (friend != null) u.Say(F(SpeechAct.Statement, Concepts.REMEMBER).Agent(Concepts.I).Patient(Concepts.Name(friend)).Adverb(Concepts.ALSO)).Related(friend);
            u.Say(F(SpeechAct.Farewell, Concepts.GOODBYE));
            u.Face(Expression.Happy).With(Gesture.Wave);
            return u;
        }

        // ================= 主人公の身振りへの反応 =================

        public static Utterance GestureResponse(Resident r, PlayerGesture g, string playerId, ResidentSystem sys, Utterance lastSpoken, out bool repeatLast)
        {
            repeatLast = false;
            var u = new Utterance(r.Id, "gesture.reaction");
            int fr = r.GetFriendship(playerId);
            bool askedSomething = lastSpoken != null && lastSpoken.sentences.Exists(s => s.act == SpeechAct.Question || s.act == SpeechAct.Invitation || s.act == SpeechAct.Request || s.act == SpeechAct.Offer);
            switch (g)
            {
                case PlayerGesture.Wave:
                    return u.Say(F(SpeechAct.Greeting, sys.IsNight ? Concepts.GOOD_NIGHT : Concepts.HELLO)).Face(Expression.Happy).With(Gesture.Wave);
                case PlayerGesture.Nod:
                    if (r.IsMovingOut) return u.Say(F(SpeechAct.Thanks, Concepts.THANKS)).Say(F(SpeechAct.Statement, DoBest()).Agent(Concepts.I).Adverb(Concepts.VERY)).Face(Expression.Happy).With(Gesture.Nod);
                    if (askedSomething) return u.Say(F(SpeechAct.Exclamation, Concepts.OK)).Say(F(SpeechAct.Thanks, Concepts.THANKS)).Face(Expression.Happy).With(Gesture.Jump);
                    return u.Say(F(SpeechAct.Statement, Concepts.YES)).Face(Expression.Happy).With(Gesture.Nod);
                case PlayerGesture.ShakeHead:
                    if (r.IsMovingOut) return u.Say(F(SpeechAct.Apology, Concepts.SORRY)).Face(Expression.Sad).With(Gesture.Hug);
                    if (askedSomething) return u.Say(F(SpeechAct.Statement, Concepts.OK)).Face(Expression.Sad).With(Gesture.Nod);
                    return u.Say(F(SpeechAct.Question, Concepts.WHY)).Face(Expression.Confused).With(Gesture.Think);
                case PlayerGesture.Bow:
                    return u.Say(F(SpeechAct.Thanks, Concepts.THANKS)).Face(Expression.Shy).With(Gesture.Bow);
                case PlayerGesture.Clap:
                case PlayerGesture.Happy:
                    return u.Say(F(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.WE)).Face(Expression.VeryHappy).With(Gesture.Jump).With(Gesture.Clap);
                case PlayerGesture.Sad:
                    u.Say(F(SpeechAct.Question, Concepts.SAD).Agent(Concepts.YOU)).Face(Expression.Worried);
                    if (fr >= 40) u.With(Gesture.Hug);
                    return u;
                case PlayerGesture.Surprised:
                    return u.Say(F(SpeechAct.Question, Concepts.WHAT)).Face(Expression.Surprised).With(Gesture.Jump);
                case PlayerGesture.Think:
                    // 「もう一度言って」：直前の言葉を繰り返す（言語学習の手掛かり）
                    if (lastSpoken != null)
                    {
                        repeatLast = true;
                        return null;
                    }
                    return u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Face(Expression.Thinking).With(Gesture.Think);
                case PlayerGesture.Point:
                    return u.Say(F(SpeechAct.Question, Concepts.WHAT)).Face(Expression.Confused).With(Gesture.LookUp);
                case PlayerGesture.Beckon:
                    bool busy = r.State.activity == ResidentActivity.Working || r.State.activity == ResidentActivity.Crafting || r.State.activity == ResidentActivity.Packing;
                    if (busy || fr < 15) return u.Say(F(SpeechAct.Statement, Concepts.BUSY).Agent(Concepts.I)).Say(F(SpeechAct.Apology, Concepts.SORRY)).Face(Expression.Worried).With(Gesture.ShakeHead);
                    return u.Say(F(SpeechAct.Statement, Concepts.OK)).Say(F(SpeechAct.Statement, Concepts.GO).Agent(Concepts.WE).Adverb(Concepts.TOGETHER)).Face(Expression.Happy).With(Gesture.Nod);
                default:
                    return u.Say(F(SpeechAct.Exclamation, Concepts.HMM)).Face(Expression.Confused);
            }
        }

        // 「がんばる」に相当する概念が無いので、意味の近い「働く」で代用する（Concepts に無い概念を勝手に作らない）
        private static string DoBest() => Concepts.WORK;

        /// <summary>食べ物を差し出す（料理人・案内役・気前のいい住民。メインストーリー第1章）。</summary>
        public static Utterance OfferFood(Resident r, string itemId, string playerId)
        {
            var u = new Utterance(r.Id, "resident.offer_food").Related(itemId);
            u.Say(F(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ItemArg(itemId)).Recipient(Concepts.YOU));
            u.Say(F(SpeechAct.Request, Concepts.EAT).Agent(Concepts.YOU));
            u.Face(Expression.Happy).With(Gesture.OfferItem, itemId);
            return u;
        }

        public static Utterance WaveFromAfar(Resident r, string playerId)
        {
            var u = new Utterance(r.Id, "greeting.far");
            u.Say(F(SpeechAct.Greeting, Concepts.HELLO)).Say(F(SpeechAct.Statement, PlayerConcept(r, playerId)));
            u.Face(Expression.Happy).With(Gesture.Wave);
            return u;
        }

        public static Utterance WitnessReaction(Resident r, string kind, string itemId)
        {
            var u = new Utterance(r.Id, "witness." + kind).Related(itemId);
            u.Say(F(SpeechAct.Exclamation, Concepts.WOW));
            if (!string.IsNullOrEmpty(itemId) && ItemRegistry.Exists(itemId))
            {
                var def = ItemRegistry.Get(itemId);
                if (def.Rarity >= 3) u.Say(F(SpeechAct.Statement, Concepts.RARE).Agent(ItemArg(itemId)));
                else u.Say(F(SpeechAct.Statement, Concepts.GOOD).Agent(ItemArg(itemId)));
            }
            u.Face(Expression.Surprised).With(Gesture.Clap);
            return u;
        }
    }
}
