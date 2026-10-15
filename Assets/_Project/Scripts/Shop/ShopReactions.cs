using Uchujin.Core;

namespace Uchujin.Shop
{
    /// <summary>反応を作るための状況（その住民が実際に感じたこと・知っていること）。</summary>
    public sealed class ShopReactionContext
    {
        public IResidentInfo resident;
        public ShopReactionKind kind;
        public ItemDefinition focusDef;
        public int focusQuality;
        public int qty = 1;
        public float ratio = 1f;
        public float tolerance = 1f;
        public float preference;
        public float thrift;
        public ShoppingIntent intent;
        public ShopRequestData request;
    }

    /// <summary>
    /// 商店での住民の反応を「意味データ」で組み立てる（宇宙語仕様 §14、タブレット仕様 §4.3〜§4.5）。
    /// 日本語の文は作らない。意味は住民がその時に本当に感じたこと・知っていることと矛盾させない（宇宙語仕様 §5.2）：
    ///  ・「高い」は実際にその住民の境界を超えた時だけ。
    ///  ・「おいしい」は前に同じ食べ物を買ったことがある時だけ（食べたことがある）。
    ///  ・「欲しい」は要望として実際に思い浮かべた物。
    /// 表情・身振り（首を振る・指さす・数を示す）を必ず添え、言葉が分からなくても状況が伝わるようにする。
    /// </summary>
    public static class ShopReactions
    {
        public static Utterance Build(ShopReactionContext c, SeededRandom rng)
        {
            string speaker = c.resident != null ? c.resident.Id : null;
            var def = c.focusDef;
            string itemId = def != null ? def.Id : null;
            Utterance u;
            switch (c.kind)
            {
                case ShopReactionKind.Bought:
                    u = new Utterance(speaker, "shop.purchase")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(ItemArg(def, c.qty, rng)).Past())
                        .Face(Expression.Happy)
                        .With(Gesture.Hold, itemId);
                    if (c.preference > 0.4f)
                    {
                        var like = MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.THIS);
                        if (c.preference > 0.75f) like.Adverb(Concepts.VERY);
                        u.Say(like);
                    }
                    AddPurposeSentence(u, c);
                    if (c.qty > 1) u.With(Gesture.ShowNumber, itemId, c.qty);
                    else u.With(Gesture.Nod);
                    break;

                case ShopReactionKind.BoughtCheap:
                {
                    var cheap = MeaningFrame.Make(SpeechAct.Exclamation, Concepts.PRICE_CHEAP).Agent(Concepts.THIS);
                    if (c.ratio < 0.45f) cheap.Adverb(Concepts.VERY);
                    u = new Utterance(speaker, "shop.cheap")
                        .Say(cheap)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(ItemArg(def, c.qty, rng)).Past())
                        .Face(Expression.Surprised)
                        .With(Gesture.PointAt, itemId)
                        .With(Gesture.Clap);
                    if (c.qty > 1) u.With(Gesture.ShowNumber, itemId, c.qty);
                    break;
                }

                case ShopReactionKind.BoughtRare:
                    u = new Utterance(speaker, "shop.rare")
                        .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.RARE).Agent(ItemArg(def, 0, rng)).Adverb(Concepts.VERY))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(Concepts.THIS).Past())
                        .Face(Expression.Excited)
                        .With(Gesture.Jump)
                        .With(Gesture.Hold, itemId);
                    break;

                case ShopReactionKind.BoughtDelicious:
                    // 前に買って食べたことがあるので「おいしい」と言える
                    u = new Utterance(speaker, "shop.delicious")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DELICIOUS).Agent(ItemArg(def, 0, rng)))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(Concepts.THIS).Adverb(Concepts.AGAIN).Past())
                        .Face(Expression.VeryHappy)
                        .With(Gesture.Eat)
                        .With(Gesture.Hold, itemId);
                    if (c.qty > 1) u.With(Gesture.ShowNumber, itemId, c.qty);
                    break;

                case ShopReactionKind.FulfilledRequest:
                    u = new Utterance(speaker, "shop.thanks")
                        .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FIND).Agent(Concepts.I).Patient(ItemArg(def, 0, rng)).Past())
                        .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS))
                        .Face(Expression.VeryHappy)
                        .With(Gesture.Hold, itemId)
                        .With(Gesture.Bow);
                    break;

                case ShopReactionKind.TooExpensive:
                {
                    var exp = MeaningFrame.Make(SpeechAct.Statement, Concepts.PRICE_EXPENSIVE).Agent(ItemArg(def, 0, rng));
                    float over = c.ratio - c.tolerance;
                    if (over > 0.5f) exp.Adverb(Concepts.VERY);
                    else if (over < 0.15f) exp.Adverb(Concepts.A_LITTLE);
                    u = new Utterance(speaker, "shop.too_expensive")
                        .Say(exp)
                        .Face(over > 1f && c.thrift > 0.6f ? Expression.Angry : Expression.Worried)
                        .With(Gesture.PointAt, itemId)
                        .With(Gesture.ShakeHead);
                    if (rng.Chance(0.5f))
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BUY).Agent(Concepts.I).Patient(Concepts.THIS).Not());
                    break;
                }

                case ShopReactionKind.CantAfford:
                    // 値段に不満は無いが、予算（財布）が足りない
                    u = new Utterance(speaker, "shop.cant_afford")
                        .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(ItemArg(def, 0, rng)))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAVE).Agent(Concepts.I).Patient(Concepts.MONEY).Not())
                        .Face(Expression.Sad)
                        .With(Gesture.PointAt, itemId)
                        .With(Gesture.Shrug);
                    break;

                case ShopReactionKind.NotFresh:
                    u = new Utterance(speaker, "shop.not_fresh")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FRESH).Agent(ItemArg(def, 0, rng)).Not())
                        .Face(Expression.Disgusted)
                        .With(Gesture.PointAt, itemId)
                        .With(Gesture.ShakeHead);
                    break;

                case ShopReactionKind.WantedNotFound:
                {
                    var req = c.request;
                    var wanted = WantedArg(req, c.intent);
                    var want = MeaningFrame.Make(SpeechAct.Desire, Concepts.WANT).Agent(Concepts.I).Patient(wanted);
                    if (req != null && req.again) want.Adverb(Concepts.AGAIN);
                    u = new Utterance(speaker, "shop.request")
                        .Say(want)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.THAT).At(Concepts.HERE).Not())
                        .Face(Expression.Sad)
                        .With(Gesture.ShakeHead);
                    // 思い浮かべた品があれば、その絵（要望ボード）を指さす。無ければ考え込む仕草。
                    string target = req != null ? req.hintItemId : null;
                    if (target != null) u.With(Gesture.PointAt, target);
                    else u.With(Gesture.Think);
                    if (req != null && req.quantity > 1) u.With(Gesture.ShowNumber, target, req.quantity);
                    if (req != null && !string.IsNullOrEmpty(req.hintItemId)) u.Related(req.hintItemId);
                    break;
                }

                case ShopReactionKind.Closed:
                    u = new Utterance(speaker, "shop.closed")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CLOSE).Agent(Concepts.SHOP).Past())
                        .Face(Expression.Sad)
                        .With(Gesture.Shrug);
                    break;

                default: // NothingGood
                    u = new Utterance(speaker, "shop.nothing")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FIND).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THING, Concepts.GOOD)).Past().Not())
                        .Face(Expression.Thinking)
                        .With(Gesture.Shrug);
                    break;
            }

            // 表情・リアクション重視系（デザイン案 09）は大げさに
            if (c.resident != null && c.resident.Lineage == AlienLineage.Expressive)
            {
                if (u.expression == Expression.Happy) u.expression = Expression.VeryHappy;
                if (u.expression == Expression.Sad) u.expression = Expression.Crying;
                u.With(Gesture.Spin);
            }
            if (itemId != null) u.Related(itemId);
            return u;
        }

        /// <summary>商品を指す名詞句。たいていは固有の語、時々「青い魚」のような総称＋色（宇宙語仕様：多義・総称の学習）。</summary>
        private static MeaningArg ItemArg(ItemDefinition def, int qty, SeededRandom rng)
        {
            if (def == null) return MeaningArg.Of(Concepts.THIS);
            MeaningArg arg;
            string color = Concepts.ColorFromTags(def);
            if (color != null && rng.Chance(0.35f)) arg = MeaningArg.Of(Concepts.GenericOf(def), color);
            else arg = MeaningArg.Of(def.EffectiveConceptId);
            if (qty > 1) arg.Qty(qty);
            return arg;
        }

        private static MeaningArg WantedArg(ShopRequestData req, ShoppingIntent intent)
        {
            if (req == null)
            {
                var need = intent != null ? intent.need : ShoppingNeed.Any;
                return MeaningArg.Of(ShopRules.NeedConcept(need));
            }
            var arg = MeaningArg.Of(string.IsNullOrEmpty(req.wantedConcept) ? Concepts.THING : req.wantedConcept);
            if (!string.IsNullOrEmpty(req.colorConcept) && req.colorConcept != req.wantedConcept) arg.Adj(req.colorConcept);
            if (req.quantity > 1) arg.Qty(req.quantity);
            return arg;
        }

        /// <summary>買った目的が実際にある時だけ添える（料理する・贈る・お祝い）。</summary>
        private static void AddPurposeSentence(Utterance u, ShopReactionContext c)
        {
            var intent = c.intent;
            if (intent == null) return;
            if (!string.IsNullOrEmpty(intent.forResidentId))
            {
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.I).Patient(Concepts.THIS)
                    .Recipient(Concepts.Name(intent.forResidentId)).Future());
                u.Related(intent.forResidentId);
                return;
            }
            switch (intent.reasonTag)
            {
                case "cooking":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COOK).Agent(Concepts.I).Patient(Concepts.DISH).Future());
                    break;
                case "party":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CELEBRATE).Agent(Concepts.WE).Future());
                    break;
                case "redecorate":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.DECORATE).Agent(Concepts.I).Patient(Concepts.HOUSE).Future());
                    break;
                case "hungry":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HUNGRY).Agent(Concepts.I));
                    break;
            }
        }
    }
}
