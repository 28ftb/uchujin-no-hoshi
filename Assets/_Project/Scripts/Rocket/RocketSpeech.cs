using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Rocket
{
    /// <summary>
    /// 修理屋・見物の住民が話す内容（意味データ）。日本語の文は作らない（宇宙語仕様 §14）。
    /// 意味は「その住民が今その場で見て・知っていること」と矛盾しないようにする（§5.2）。
    /// </summary>
    internal static class RocketSpeech
    {
        private static MeaningFrame M(SpeechAct act, string predicate) => MeaningFrame.Make(act, predicate);
        private static MeaningArg Rocket => MeaningArg.Of(Concepts.ROCKET);

        private static MeaningArg ItemArg(string itemId, int qty)
        {
            var def = ItemRegistry.GetOrPlaceholder(itemId);
            var a = MeaningArg.Of(def.EffectiveConceptId);
            var color = Concepts.ColorFromTags(def);
            if (!string.IsNullOrEmpty(color)) a.Adj(color);
            if (qty > 0) a.Qty(qty);
            if (qty > 1) a.Plural();
            return a;
        }

        /// <summary>初めて図を見せる時：「宇宙船 壊れている（たくさん）。私 直す。あなた 部品 持ってきて」</summary>
        public static Utterance MechanicDiagramIntro(string id, int brokenCount, bool first)
        {
            var u = new Utterance(id, "rocket.diagram").Face(Expression.Excited);
            if (first) u.Say(M(SpeechAct.Greeting, Concepts.HELLO));
            var broken = M(SpeechAct.Statement, Concepts.BROKEN).Agent(Rocket);
            if (brokenCount >= 5) broken.Adverb(Concepts.VERY);
            u.Say(broken);
            if (brokenCount > 0)
            {
                u.Say(M(SpeechAct.Statement, Concepts.BROKEN).Agent(MeaningArg.Of(Concepts.THING).Qty(brokenCount).Plural()));
                u.Say(M(SpeechAct.Statement, Concepts.REPAIR).Agent(Concepts.I).Patient(Rocket).Future());
                u.Say(M(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.PART).Plural()));
                u.With(Gesture.PointAt, "rocket").With(Gesture.ShowNumber, null, brokenCount);
            }
            else
            {
                u.Say(M(SpeechAct.Statement, Concepts.GOOD).Agent(Rocket).Adverb(Concepts.VERY));
                u.With(Gesture.Clap);
            }
            return u.Related("rocket");
        }

        /// <summary>図で装置を指した時：「あなた ◯◯ N個 持ってきて」（必要なもの・数を身振りでも示す）。</summary>
        public static Utterance MechanicDeviceExplain(string id, DeviceDef def, string itemId, int qty, bool needsCreature, bool needsMoney)
        {
            var u = new Utterance(id, "rocket.diagram.device").Face(Expression.Thinking).With(Gesture.PointAt, "rocket:" + def.Id);
            if (!string.IsNullOrEmpty(itemId) && qty > 0)
            {
                u.Say(M(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(ItemArg(itemId, qty)));
                if (RocketTables.GetPurchased(itemId) != null)
                    u.Say(M(SpeechAct.Statement, Concepts.SELL).Agent(Concepts.I).Patient(ItemArg(itemId, 0)));
                u.With(Gesture.ShowNumber, null, qty).Related(itemId);
            }
            else if (needsCreature)
            {
                u.Say(M(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.CREATURE)));
                u.Say(M(SpeechAct.Statement, Concepts.HELP).Agent(MeaningArg.Of(Concepts.CREATURE)).Future());
            }
            else if (needsMoney)
            {
                u.Say(M(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.MONEY)));
                u.With(Gesture.OfferItem, "money");
            }
            else
            {
                u.Say(M(SpeechAct.Statement, Concepts.GOOD).Agent(Rocket));
                u.Face(Expression.Happy).With(Gesture.Nod);
            }
            return u;
        }

        public static Utterance MechanicSellOffer(string id)
        {
            return new Utterance(id, "rocket.shop")
                .Say(M(SpeechAct.Statement, Concepts.SELL).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PART).Plural()))
                .Say(M(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PART).Plural()).Past())
                .Face(Expression.Happy).With(Gesture.OfferItem, SharedItemIds.PartPrecisionParts);
        }

        public static Utterance MechanicThanks(string id, string itemId, int count)
        {
            return new Utterance(id, "rocket.shop.thanks")
                .Say(M(SpeechAct.Thanks, Concepts.THANKS))
                .Say(M(SpeechAct.Statement, Concepts.USE).Agent(Concepts.YOU).Patient(ItemArg(itemId, count)).At(Concepts.ROCKET).Future())
                .Face(Expression.Happy).With(Gesture.Bow).Related(itemId);
        }

        /// <summary>加工の依頼を受けた：「私 ◯◯ 作る（明日）」</summary>
        public static Utterance MechanicOrderAccepted(string id, string outputItemId, int qty, int days)
        {
            return new Utterance(id, "rocket.order")
                .Say(M(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(ItemArg(outputItemId, qty)).Future().When(days <= 1 ? Concepts.TOMORROW : Concepts.LATER))
                .Face(Expression.Happy).With(Gesture.Nod).With(Gesture.ShowNumber, null, qty).Related(outputItemId);
        }

        public static Utterance MechanicOrderReady(string id, string outputItemId)
        {
            return new Utterance(id, "rocket.order.ready")
                .Say(M(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ItemArg(outputItemId, 0)).Recipient(Concepts.YOU))
                .Face(Expression.Happy).With(Gesture.OfferItem, outputItemId).Related(outputItemId);
        }

        /// <summary>修理屋が喜ぶ（§5章：飛び跳ねて喜ぶ）。</summary>
        public static Utterance MechanicCelebrate(string id, DeviceDef def)
        {
            var u = new Utterance(id, "rocket.repaired." + def.Id)
                .Say(M(SpeechAct.Exclamation, Concepts.WOW));
            AddDeviceObservation(u, def, ResidentJob.Mechanic);
            u.Say(M(SpeechAct.Statement, Concepts.REPAIR).Agent(Concepts.WE).Patient(Rocket).Past().Adverb(Concepts.TOGETHER));
            return u.Face(Expression.VeryHappy).With(Gesture.Jump).With(Gesture.Clap).Related("rocket");
        }

        /// <summary>修理屋が作業場で作業している時のつぶやき。</summary>
        public static Utterance MechanicWorking(string id, RocketSystem sys)
        {
            var u = new Utterance(id, "rocket.working");
            if (sys.IsLaunchReady)
            {
                u.Say(M(SpeechAct.Statement, Concepts.GOOD).Agent(Rocket).Adverb(Concepts.STILL));
                u.Face(Expression.Happy).With(Gesture.Nod);
            }
            else
            {
                u.Say(M(SpeechAct.Statement, Concepts.REPAIR).Agent(Concepts.I).Patient(Rocket));
                if (sys.CountRepaired() > 0) u.Say(M(SpeechAct.Statement, Concepts.FUN).Agent(MeaningArg.Of(Concepts.WORK)));
                u.Face(Expression.Thinking).With(Gesture.Think);
            }
            return u.Related("rocket");
        }

        /// <summary>見物の住民の一言（今の宇宙船の状態に合わせる）。</summary>
        public static Utterance WatcherRemark(string id, RocketSystem sys, ResidentJob job)
        {
            var u = new Utterance(id, "rocket.watch");
            if (sys.IsLaunchReady)
            {
                u.Say(M(SpeechAct.Question, Concepts.FLY).Agent(Rocket).When(Concepts.SOON));
                u.Face(Expression.Worried).With(Gesture.LookUp);
                return u.Related("rocket");
            }
            if (job == ResidentJob.Traveler && sys.IsDeviceRepaired("R06"))
            {
                u.Say(M(SpeechAct.Desire, Concepts.GO).Agent(Concepts.I).At(Concepts.SKY));
                u.Face(Expression.Love).With(Gesture.LookUp);
                return u.Related("rocket");
            }
            if (sys.IsDeviceRepaired("R05"))
            {
                u.Say(M(SpeechAct.Question, Concepts.FLY).Agent(Rocket));
                u.Face(Expression.Excited).With(Gesture.PointAt, "rocket");
            }
            else if (sys.IsDeviceRepaired("R01"))
            {
                u.Say(M(SpeechAct.Statement, Concepts.SHINE).Agent(Rocket));
                u.Face(Expression.Happy).With(Gesture.PointAt, "rocket");
            }
            else
            {
                u.Say(M(SpeechAct.Exclamation, Concepts.WOW));
                u.Say(M(SpeechAct.Statement, Concepts.BIG).Agent(Rocket));
                u.Say(M(SpeechAct.Statement, Concepts.BROKEN).Agent(Rocket));
                u.Face(Expression.Surprised).With(Gesture.PointAt, "rocket");
            }
            return u.Related("rocket");
        }

        /// <summary>修理の瞬間を見た住民の反応（§12 の表）。</summary>
        public static Utterance RepairReaction(string id, DeviceDef def, ResidentJob job)
        {
            var u = new Utterance(id, "rocket.saw_repair." + def.Id);
            if (def.IsFinal)
            {
                u.Say(M(SpeechAct.Exclamation, Concepts.CONGRATS));
                u.Say(M(SpeechAct.Question, Concepts.LEAVE).Agent(Concepts.YOU));
                return u.Face(Expression.Happy).With(Gesture.Clap).Related("rocket");
            }
            u.Say(M(SpeechAct.Exclamation, Concepts.WOW));
            AddDeviceObservation(u, def, job);
            return u.Face(Expression.Surprised).With(Gesture.Clap).Related("rocket");
        }

        public static Utterance EngineTestReaction(string id, bool isMechanic)
        {
            var u = new Utterance(id, "rocket.engine_test")
                .Say(M(SpeechAct.Exclamation, Concepts.WOW))
                .Say(M(SpeechAct.Statement, Concepts.LOUD).Agent(Rocket).Adverb(Concepts.VERY));
            if (isMechanic) u.Say(M(SpeechAct.Statement, Concepts.GOOD).Agent(Rocket)).Face(Expression.VeryHappy).With(Gesture.Jump);
            else u.Say(M(SpeechAct.Question, Concepts.FLY).Agent(Rocket)).Face(Expression.Surprised).With(Gesture.Clap);
            return u.Related("rocket");
        }

        /// <summary>装置ごとに「目に見えた変化」を言う。</summary>
        private static void AddDeviceObservation(Utterance u, DeviceDef def, ResidentJob job)
        {
            switch (def.Id)
            {
                case "R01":
                    u.Say(M(SpeechAct.Statement, Concepts.BRIGHT).Agent(Rocket));
                    break;
                case "R02":
                    u.Say(M(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(Rocket));
                    u.Say(M(SpeechAct.Statement, Concepts.NEW).Agent(Rocket));
                    break;
                case "R03":
                    u.Say(M(SpeechAct.Statement, Concepts.GLOWING).Agent(MeaningArg.Of(Concepts.WATER)));
                    if (job == ResidentJob.FishingMaster) u.Say(M(SpeechAct.Statement, Concepts.HELP).Agent(MeaningArg.Of(Concepts.FISH)).Past());
                    break;
                case "R04":
                    u.Say(M(SpeechAct.Statement, Concepts.HAVE).Agent(Rocket).Patient(MeaningArg.Of(Concepts.FUEL)));
                    if (job == ResidentJob.FarmingMaster) u.Say(M(SpeechAct.Statement, Concepts.HELP).Agent(MeaningArg.Of(Concepts.FRUIT)).Past());
                    break;
                case "R05":
                    u.Say(M(SpeechAct.Statement, Concepts.STRONG).Agent(Rocket));
                    u.Say(M(SpeechAct.Question, Concepts.FLY).Agent(Rocket));
                    break;
                case "R06":
                    u.Say(M(SpeechAct.Statement, Concepts.SEE).Agent(Rocket).Patient(MeaningArg.Of(Concepts.STAR)));
                    if (job == ResidentJob.Traveler) u.Say(M(SpeechAct.Desire, Concepts.GO).Agent(Concepts.I).At(Concepts.SKY));
                    break;
                case "R07":
                    u.Say(M(SpeechAct.Statement, Concepts.QUIET).Agent(Rocket));
                    u.Say(M(SpeechAct.Statement, Concepts.HELP).Agent(MeaningArg.Of(Concepts.CREATURE)).Past());
                    break;
                default:
                    u.Say(M(SpeechAct.Statement, Concepts.GOOD).Agent(Rocket));
                    break;
            }
        }
    }

    /// <summary>修理屋（または後継者）に話しかけた時のメニュー（§14.1・§9.3）。</summary>
    internal sealed class MechanicInteractionProvider : IResidentInteractionProvider
    {
        private readonly RocketSystem sys;

        public MechanicInteractionProvider(RocketSystem sys) { this.sys = sys; }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            var list = new List<ResidentInteractionOption>();
            if (resident == null || player == null || !sys.IsMechanic(resident.Id)) return list;
            string rid = resident.Id;
            list.Add(new ResidentInteractionOption("宇宙船の図を見せてもらう", "Ask about the ship (diagram)", () => sys.OpenDiagram(rid), 20));
            list.Add(new ResidentInteractionOption("部品を買う", "Buy parts", () => sys.OpenVendor(rid), 21));
            list.Add(new ResidentInteractionOption("加工を頼む", "Request processing", () => sys.OpenOrders(rid), 22));
            if (sys.HasReadyOrders(player.PlayerId))
            {
                list.Add(new ResidentInteractionOption("頼んだ物を受け取る", "Collect your order", () =>
                {
                    string firstOut = null;
                    foreach (var o in sys.State.orders)
                    {
                        if (o.playerId != player.PlayerId) continue;
                        var r = RocketTables.GetProxy(o.recipeId);
                        if (r != null) { firstOut = r.OutputItemId; break; }
                    }
                    if (firstOut != null) Game.Instance?.Service<IResidentDirectory>()?.Speak(rid, RocketSpeech.MechanicOrderReady(rid, firstOut));
                    sys.CollectOrders();
                }, 19));
            }
            return list;
        }
    }
}
