using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 住民に話しかけた時のメニュー：
    ///  ・どの住民にも「連れている生き物を見せる」（住民の反応＝好み。住民辞典に事実を記録）
    ///  ・生き物名人（ResidentJob.CreatureExpert）：技の実演（身振りで学ぶ）、網の強化、餌を買う、見せた生き物の得意なことを身振りで教えてくれる
    /// 生き物仕様 §5.4・§11.1。宇宙語の文は作らず意味データ（MeaningFrame）を組み立てる。
    /// </summary>
    public sealed class CreatureExpertProvider : IResidentInteractionProvider
    {
        private readonly CreatureSystem sys;

        public CreatureExpertProvider(CreatureSystem sys) { this.sys = sys; }

        // ---------------- 【仮数値】技の習得条件・網の強化費用 ----------------
        private class TechniqueDef
        {
            public string Id;
            public int CatchLevel;
            public int Friendship;
            public string NameJa, NameEn, LearnedJa, LearnedEn;
        }

        private static readonly TechniqueDef[] TechniqueDefs =
        {
            new TechniqueDef { Id = CreatureSystem.TechSneak, CatchLevel = 2, Friendship = 0, NameJa = "忍び足", NameEn = "Soft steps",
                LearnedJa = "名人の身振りを見て、そっと近づくコツをつかんだ！（生き物に気づかれにくくなった）", LearnedEn = "Watching the expert, you got the knack of sneaking up! (Creatures notice you less)" },
            new TechniqueDef { Id = CreatureSystem.TechNet, CatchLevel = 3, Friendship = 20, NameJa = "網さばき", NameEn = "Net handling",
                LearnedJa = "名人の網さばきを真似して、振るタイミングがつかめた！（捕まえやすく、網が少し遠くまで届く）", LearnedEn = "Copying the expert's swing, you found your timing! (Easier catches, longer reach)" },
            new TechniqueDef { Id = CreatureSystem.TechBait, CatchLevel = 4, Friendship = 10, NameJa = "餌の使い方", NameEn = "Bait craft",
                LearnedJa = "餌の置き方を教わった！（餌が長持ちし、遠くの生き物も寄ってくる）", LearnedEn = "You learned how to set bait! (Bait lasts longer and draws creatures from farther)" },
            new TechniqueDef { Id = CreatureSystem.TechObserve, CatchLevel = 5, Friendship = 40, NameJa = "観察のコツ", NameEn = "Keen eye",
                LearnedJa = "じっと見るコツを教わった！（隠れている生き物に気づきやすくなった）", LearnedEn = "You learned to look closely! (Hidden creatures are easier to spot)" },
        };

        private struct NetUpgrade
        {
            public int Price;
            public string ItemId;
            public int ItemCount;
        }

        /// <summary>網の強化（index=今のレベル→次のレベル）。鉱石は鉱山モジュールの共有ID。</summary>
        private static readonly NetUpgrade[] NetUpgrades =
        {
            new NetUpgrade(), new NetUpgrade { Price = 600 },
            new NetUpgrade { Price = 2000, ItemId = SharedItemIds.OreConductive, ItemCount = 3 },
            new NetUpgrade { Price = 6000, ItemId = SharedItemIds.OreSturdyMetal, ItemCount = 3 },
            new NetUpgrade { Price = 15000, ItemId = SharedItemIds.OreEnergyCrystal, ItemCount = 2 },
        };

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            if (resident == null || player == null) yield break;
            var companions = Companions(player);
            if (companions.Count > 0)
                yield return new ResidentInteractionOption("連れている生き物を見せる", "Show your creature", () => ChooseAndShow(resident, player, companions), 60);

            if (resident.Job != ResidentJob.CreatureExpert) yield break;
            yield return new ResidentInteractionOption("生き物取りの技を見せてもらう", "Watch a catching technique", () => Demonstrate(resident, player), 20);
            int lv = player.Tools.GetLevel(ToolType.CatchNet);
            if (lv < 5) yield return new ResidentInteractionOption("網を強化してもらう", "Upgrade your net", () => OfferNetUpgrade(resident, player), 30);
            yield return new ResidentInteractionOption("生き物の餌を買う", "Buy creature food", () => OpenFoodVendor(player), 40);
        }

        private List<OwnedCreature> Companions(PlayerState player)
        {
            var list = new List<OwnedCreature>();
            foreach (var c in sys.LocalOwnedList())
                if (c.placement == CreaturePlacement.Companion && c.ownerId == player.PlayerId) list.Add(c);
            return list;
        }

        // ---------------- 生き物を見せる ----------------

        private void ChooseAndShow(IResidentInfo r, PlayerState player, List<OwnedCreature> companions)
        {
            if (companions.Count == 1)
            {
                Show(r, player, companions[0]);
                return;
            }
            var opts = new List<ChoiceOption>();
            foreach (var c in companions)
            {
                var cc = c;
                opts.Add(new ChoiceOption(sys.DisplayName(cc), () => Show(r, player, cc)));
            }
            UIManager.Choose(Loc.T("どの子を見せる？", "Which one?"), null, opts);
        }

        private void Show(IResidentInfo r, PlayerState player, OwnedCreature c)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            var sp = CreatureDatabase.Get(c.speciesId);
            if (dir == null || sp == null) return;
            var av = sys.GetAvatar(c.uid);
            if (av != null) av.Body.PlayAct(1.5f);
            Game.Instance.PerformLocalGesture(PlayerGesture.ShowItem);

            string concept = Concepts.Species(sp.Id);
            if (r.Job == ResidentJob.CreatureExpert)
            {
                // 名人は見せた生き物の得意なことを身振りで実演してくれる（言葉が分からなくても分かる）
                var u = new Utterance(r.Id, "creature.expert_explain").Related(sp.Id);
                if (sp.IsWorker)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(concept)).Adverb(Concepts.VERY));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, AbilityVerb(sp.Abilities[0])).Agent(MeaningArg.Of(concept)));
                    u.Face(Expression.Excited).With(GestureFor(sp.Abilities[0]));
                    foreach (var a in sp.Abilities) sys.RecordBehavior(sp.Id, "expert." + a, a == sp.Abilities[0]);
                }
                else
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CUTE).Agent(MeaningArg.Of(concept)).Adverb(Concepts.VERY));
                    u.Face(Expression.Love).With(Gesture.Hug);
                }
                dir.Speak(r.Id, u, SpeechChannel.Direct);
                var e = sys.GetEntry(sp.Id, true);
                if (!e.clueKnown)
                {
                    e.clueKnown = true;
                    UIManager.Toast(Loc.T("名人の身振りから、捕まえ方の手掛かりが分かった（図鑑に記録）", "From the expert's gestures you learned how to catch it (noted in the bestiary)"));
                }
            }
            else
            {
                float pref = 0f;
                var caseDef = ItemRegistry.Get(CreatureDatabase.CaseItemId(sp.Id));
                try { pref = r.EvaluateItem(caseDef); } catch { pref = 0f; }
                var u = new Utterance(r.Id, "creature.show").Related(sp.Id);
                string factJa, factEn;
                if (pref > 0.3f)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CUTE).Agent(MeaningArg.Of(concept)).Adverb(Concepts.VERY)).Face(Expression.Love).With(Gesture.Clap);
                    factJa = sp.NameJa + "を見せると、とてもうれしそうな顔をした"; factEn = "Looked very happy when shown " + sp.NameEn;
                }
                else if (pref < -0.3f)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SCARY).Agent(MeaningArg.Of(concept))).Face(Expression.Worried).With(Gesture.ShakeHead);
                    factJa = sp.NameJa + "を見せると、困った顔で後ずさりした"; factEn = "Backed away looking troubled when shown " + sp.NameEn;
                }
                else if (sp.Rarity >= 3)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW)).Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.RARE).Agent(MeaningArg.Of(concept))).Face(Expression.Surprised).With(Gesture.Jump);
                    factJa = sp.NameJa + "を見せると、目を丸くして驚いた"; factEn = "Was amazed when shown " + sp.NameEn;
                }
                else
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(MeaningArg.Of(Concepts.I)).Patient(MeaningArg.Of(concept)).Adverb(Concepts.A_LITTLE)).Face(Expression.Happy).With(Gesture.Nod);
                    factJa = sp.NameJa + "を見せると、にこにこしてうなずいた"; factEn = "Smiled and nodded when shown " + sp.NameEn;
                }
                dir.Speak(r.Id, u, SpeechChannel.Direct);
                Game.Instance.Service<ITabletService>()?.RecordResidentObservation(r.Id, factJa, factEn);
                if (sys.MarkShownToday(r.Id))
                {
                    dir.ChangeFriendship(r.Id, player.PlayerId, pref > 0.3f ? 2 : 1, "shown_creature");
                    dir.AddMemory(r.Id, new ResidentMemoryEntry { kind = "shown_creature", playerId = player.PlayerId, itemId = CreatureDatabase.CaseItemId(sp.Id), day = Game.Instance.Time.Day, importance = 0.3f, detail = sp.Id });
                }
            }
        }

        private static string AbilityVerb(CreatureAbility a)
        {
            switch (a)
            {
                case CreatureAbility.Water:
                case CreatureAbility.Splash: return Concepts.WATER_VERB;
                case CreatureAbility.Till: return Concepts.DIG;
                case CreatureAbility.Sow: return Concepts.PLANT;
                case CreatureAbility.Harvest: return Concepts.HARVEST;
                case CreatureAbility.Carry:
                case CreatureAbility.Pack: return Concepts.CARRY;
                case CreatureAbility.Glow: return Concepts.SHINE;
                case CreatureAbility.Sound:
                case CreatureAbility.Echo: return Concepts.SING;
                case CreatureAbility.Float: return Concepts.FLOAT;
                case CreatureAbility.Mimic: return Concepts.REMEMBER;
                case CreatureAbility.ColorShift: return Concepts.CHANGE;
                case CreatureAbility.Pollinate:
                case CreatureAbility.SoilImprove: return Concepts.GROW;
                case CreatureAbility.Sense: return Concepts.KNOW;
                default: return Concepts.HELP;
            }
        }

        private static Gesture GestureFor(CreatureAbility a)
        {
            switch (a)
            {
                case CreatureAbility.Water:
                case CreatureAbility.Splash:
                case CreatureAbility.Cool: return Gesture.LookUp;
                case CreatureAbility.Till:
                case CreatureAbility.Press: return Gesture.Bow;
                case CreatureAbility.Harvest:
                case CreatureAbility.Carry:
                case CreatureAbility.Pack: return Gesture.Hold;
                case CreatureAbility.Heat: return Gesture.Hug;
                case CreatureAbility.Sound:
                case CreatureAbility.Echo: return Gesture.Clap;
                case CreatureAbility.Grind: return Gesture.Spin;
                case CreatureAbility.Stabilize: return Gesture.Nod;
                case CreatureAbility.Sense: return Gesture.Think;
                default: return Gesture.Dance;
            }
        }

        // ---------------- 技の実演（身振りで学ぶ） ----------------

        private void Demonstrate(IResidentInfo r, PlayerState player)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (dir == null) return;
            int friendship = r.GetFriendship(player.PlayerId);
            TechniqueDef next = null;
            foreach (var t in TechniqueDefs)
            {
                if (sys.HasTechnique(t.Id)) continue;
                next = t;
                break;
            }
            if (next == null)
            {
                dir.Speak(r.Id, new Utterance(r.Id, "creature.expert_done")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(Concepts.YOU)).Adverb(Concepts.VERY))
                    .Face(Expression.VeryHappy).With(Gesture.Clap));
                return;
            }
            if (sys.CatchLevel < next.CatchLevel || friendship < next.Friendship)
            {
                // まだ早い：もっと捕まえてから（身振り：首を振って網を振る仕草）
                dir.Speak(r.Id, new Utterance(r.Id, "creature.expert_notyet")
                    .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.CATCH).Patient(MeaningArg.Of(Concepts.CREATURE).Plural()).Adverb(Concepts.MORE))
                    .Face(Expression.Thinking).With(Gesture.ShakeHead).With(Gesture.Beckon));
                // 意味の答えは出さず、見えた身振りだけを伝える（技の条件：生き物取りLv・親しさ）
                UIManager.Toast(Loc.T("名人は首を振り、網を何度も振る仕草をしてみせた", "The expert shook their head and mimed swinging a net again and again"), 5f);
                return;
            }
            Utterance u = new Utterance(r.Id, "creature.expert_teach." + next.Id);
            switch (next.Id)
            {
                case CreatureSystem.TechSneak:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.WALK).Agent(MeaningArg.Of(Concepts.YOU)).Adverb(Concepts.A_LITTLE))
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LEAVE).Agent(MeaningArg.Of(Concepts.CREATURE)).Because(MeaningFrameLite.Make(Concepts.RUN, MeaningArg.Of(Concepts.YOU))))
                     .Face(Expression.Thinking).With(Gesture.ShakeHead).With(Gesture.Hold);
                    break;
                case CreatureSystem.TechNet:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.WAIT))
                     .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.CATCH).Patient(MeaningArg.Of(Concepts.CREATURE)).When(Concepts.NOW))
                     .Face(Expression.Excited).With(Gesture.Beckon).With(Gesture.Clap);
                    break;
                case CreatureSystem.TechBait:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.PUT).Patient(MeaningArg.Of(Concepts.FOOD)))
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.COME).Agent(MeaningArg.Of(Concepts.CREATURE)).Future())
                     .Face(Expression.Happy).With(Gesture.OfferItem, CreatureDatabase.FoodBasic).With(Gesture.Beckon);
                    break;
                default:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.SEE).Patient(MeaningArg.Of(Concepts.THING, Concepts.SMALL)).Adverb(Concepts.VERY))
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.CREATURE)).At(Concepts.HERE))
                     .Face(Expression.Thinking).With(Gesture.PointAt).With(Gesture.Think);
                    break;
            }
            dir.Speak(r.Id, u, SpeechChannel.Direct);
            sys.LearnTechnique(next.Id);
            dir.ChangeFriendship(r.Id, player.PlayerId, 2, "taught_technique");
            UIManager.Toast(Loc.T(next.LearnedJa, next.LearnedEn), 6f);
        }

        // ---------------- 網の強化 ----------------

        private void OfferNetUpgrade(IResidentInfo r, PlayerState player)
        {
            int lv = Mathf.Max(1, player.Tools.GetLevel(ToolType.CatchNet));
            if (lv >= 5) return;
            var up = NetUpgrades[lv];
            string need = Loc.Format("{0}", "{0}", UIStyles.Money(up.Price));
            if (!string.IsNullOrEmpty(up.ItemId)) need += Loc.Format("＋{0}×{1}", " + {0} x{1}", ItemRegistry.NameOf(up.ItemId), up.ItemCount);
            UIManager.Confirm(Loc.T("網の強化", "Upgrade net"),
                Loc.Format("網を「{0}」にしてもらう？\n必要：{1}\n（強化すると網が遠くまで届き、捕まえやすくなる）", "Upgrade your net to \"{0}\"?\nCost: {1}\n(Reaches farther and catches more easily)", ToolTypeUtil.TierLabel(lv + 1), need),
                () =>
                {
                    if (!player.Wallet.CanAfford(up.Price) || (!string.IsNullOrEmpty(up.ItemId) && !player.Inventory.Has(up.ItemId, up.ItemCount)))
                    {
                        UIManager.Toast(Loc.T("足りない物がある…", "You don't have enough..."));
                        return;
                    }
                    if (!player.Wallet.TrySpend(up.Price, "net_upgrade")) return;
                    if (!string.IsNullOrEmpty(up.ItemId)) player.Inventory.Remove(up.ItemId, up.ItemCount, 0, "net_upgrade");
                    player.Tools.SetLevel(ToolType.CatchNet, lv + 1);
                    EventBus.Publish(new ToolUpgradedEvent { PlayerId = player.PlayerId, Tool = ToolType.CatchNet, NewLevel = lv + 1 });
                    var dir = Game.Instance.Service<IResidentDirectory>();
                    dir?.Speak(r.Id, new Utterance(r.Id, "creature.net_upgraded")
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.NET, Concepts.NEW)).Recipient(Concepts.YOU))
                        .Face(Expression.Happy).With(Gesture.OfferItem));
                    UIManager.Toast(Loc.Format("網が「{0}」になった！", "Your net is now \"{0}\"!", ToolTypeUtil.TierLabel(lv + 1)));
                });
        }

        // ---------------- 餌を買う ----------------

        public static void OpenFoodVendor(PlayerState player)
        {
            var entries = new List<VendorEntry>();
            string[] ids =
            {
                CreatureDatabase.FoodBasic, CreatureDatabase.FoodSeed, CreatureDatabase.FoodFruit, CreatureDatabase.FoodNectar,
                CreatureDatabase.FoodWater, CreatureDatabase.FoodMoss, CreatureDatabase.FoodMineral, CreatureDatabase.FoodGlow
            };
            foreach (var id in ids)
            {
                var def = ItemRegistry.Get(id);
                if (def == null) continue;
                entries.Add(new VendorEntry { ItemId = id, Price = def.BasePrice });
            }
            UIManager.Open(new VendorWindow(Loc.T("生き物の餌", "Creature food"), player, entries));
        }
    }
}
