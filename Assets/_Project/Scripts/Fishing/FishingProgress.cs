using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Fishing
{
    /// <summary>
    /// 釣りの調整用の数値（すべて【仮数値】）。釣り仕様 §4「レベル上限・経験値テーブル・難易度補正は未決定」。
    /// Unity 上で遊びながらここを調整する。
    /// </summary>
    public static class FishingTuning
    {
        // ---- 釣りレベル（§4.2）----
        public const int MaxLevel = 10;
        /// <summary>レベル n に必要な累計経験値（index = level-1）。</summary>
        public static readonly int[] LevelXp = { 0, 40, 110, 220, 380, 600, 900, 1300, 1800, 2500 };
        public const float LevelPullReduction = 0.035f;    // 1レベルごとに魚の引きが軽くなる
        public const float LevelDrainBonus = 0.07f;        // 1レベルごとに魚が疲れやすくなる
        public const float LevelReactionBonus = 0.04f;     // アタリに合わせる猶予（秒）
        public const float LevelRareBonus = 0.03f;         // 珍しい魚（希少度2以上）の寄りやすさ

        // ---- 竿の段階（§4.1）。index = tier-1 ----
        public static readonly float[] RodLineStrength = { 1.0f, 1.15f, 1.3f, 1.45f, 1.65f };
        public static readonly float[] RodReelSpeed = { 3.0f, 3.4f, 3.8f, 4.2f, 4.7f };
        public const float RodReactionBonus = 0.05f;
        public const float RodSizeBonus = 0.03f;

        // ---- キャスト・待ち ----
        public const float CastMinDistance = 3f;
        public const float CastMaxDistance = 10f;
        public const float CastMeterSpeed = 0.9f;          // メーターの往復速度
        public const float SweetZoneBase = 0.07f;          // 狙いの幅（片側）
        public const float SweetZoneTechnique = 0.16f;
        public const float PrecisionRareMultiplier = 1.5f;
        public const float WaitMin = 3f, WaitMax = 11f;    // 食いつくまでの秒数
        public const float BaitWaitMultiplier = 0.75f;
        public const float LureTwitchCut = 0.08f, LureTwitchCutTechnique = 0.3f;
        public const int MaxTwitches = 4;
        public const float ReactionWindowBase = 0.75f;     // アタリ→合わせの猶予
        public const int FakeNibblesMax = 3;

        // ---- やり取り（§3）----
        public const float BreakHoldSeconds = 0.35f;       // 張力が限界を超えて糸が切れるまで
        public const float SlackEscapeSeconds = 2.6f;      // 緩みすぎて針が外れるまで
        public const float SlackThreshold = 0.07f;
        public const float SlackRate = 0.55f;
        public const float LineHandlingSlackMultiplier = 1.6f;
        public const float LineHandlingRelief = 0.28f;
        public const float LineHandlingCooldown = 3f;
        public const float PowerReelMultiplier = 1.8f;
        public const float GuideMultiplier = 1.5f;
        public const float MasterSecretZone = 0.2f;
        public const float FightMaxDistanceExtra = 12f;    // 開始距離＋この距離まで引き出されると逃げられる

        // ---- 大きさ・品質（§8）----
        public const float BigDaySizeBonus = 0.25f;
        public const float NewRecordShowSeconds = 3f;

        // ---- 経験値 ----
        public static int XpForCatch(FishSpecies s, bool firstTime)
        {
            int xp = 4 + s.Difficulty * 3 + s.Rarity * 5;
            return firstTime ? xp * 2 : xp;
        }

        // ---- 伝説の魚 ----
        public const int LegendRestDays = 7;

        // ---- 住民の反応 ----
        public const float ResidentReactRadius = 10f;
        public const bool ResidentReactsToBigCatch = true;

        public static int LevelForXp(int xp)
        {
            int lv = 1;
            for (int i = 0; i < LevelXp.Length; i++) if (xp >= LevelXp[i]) lv = i + 1;
            return Mathf.Clamp(lv, 1, MaxLevel);
        }

        public static int XpToNext(int level) => level >= MaxLevel ? 0 : LevelXp[level];
    }

    /// <summary>竿の強化に必要なもの（釣り仕様 §4.1。素材＋お金。素材・費用は【仮】）。</summary>
    public sealed class RodUpgradeRequirement
    {
        public int TargetTier;
        public int Money;
        public readonly List<MaterialNeed> Materials = new List<MaterialNeed>();
    }

    /// <summary>素材の条件：特定のアイテム、または「希少度N以上の魚」など。</summary>
    public sealed class MaterialNeed
    {
        public string ItemId;      // 指定があればこのアイテム
        public int MinFishRarity;  // ItemId が無いとき：希少度がこれ以上の魚なら何でも
        public int Count;

        public bool Matches(ItemStack s)
        {
            if (s == null || s.IsEmpty) return false;
            if (!string.IsNullOrEmpty(ItemId)) return s.itemId == ItemId;
            var def = s.Definition;
            if (def.Category != ItemCategory.Fish) return false;
            if (def.HasTag(FishCatalog.TagLegend)) return false; // 伝説の魚は素材に使わせない
            if (def.HasTag(ItemTags.RocketMaterial)) return false; // 修理用の魚は自動で使わない
            return def.Rarity >= MinFishRarity;
        }

        public string Label()
        {
            if (!string.IsNullOrEmpty(ItemId)) return Loc.Format("{0} ×{1}", "{0} x{1}", ItemRegistry.NameOf(ItemId), Count);
            if (MinFishRarity <= 0) return Loc.Format("魚（何でも） ×{0}", "Any fish x{0}", Count);
            return Loc.Format("珍しさ{0}以上の魚 ×{1}", "Fish of rarity {0}+ x{1}", MinFishRarity, Count);
        }
    }

    public static class RodUpgrades
    {
        private static List<RodUpgradeRequirement> table;

        /// <summary>竿の強化表【仮数値】。鉱石は鉱山モジュールの共有ID（未登録なら条件から外す）。</summary>
        public static IReadOnlyList<RodUpgradeRequirement> Table
        {
            get
            {
                if (table != null) return table;
                table = new List<RodUpgradeRequirement>();
                var t2 = new RodUpgradeRequirement { TargetTier = 2, Money = 400 };
                t2.Materials.Add(new MaterialNeed { MinFishRarity = 0, Count = 5 });
                t2.Materials.Add(new MaterialNeed { ItemId = SharedItemIds.OreConductive, Count = 2 });
                table.Add(t2);
                var t3 = new RodUpgradeRequirement { TargetTier = 3, Money = 1200 };
                t3.Materials.Add(new MaterialNeed { MinFishRarity = 1, Count = 5 });
                t3.Materials.Add(new MaterialNeed { ItemId = SharedItemIds.OreSturdyMetal, Count = 2 });
                table.Add(t3);
                var t4 = new RodUpgradeRequirement { TargetTier = 4, Money = 3000 };
                t4.Materials.Add(new MaterialNeed { MinFishRarity = 2, Count = 3 });
                t4.Materials.Add(new MaterialNeed { ItemId = SharedItemIds.OreSturdyMetal, Count = 4 });
                table.Add(t4);
                var t5 = new RodUpgradeRequirement { TargetTier = 5, Money = 6000 };
                t5.Materials.Add(new MaterialNeed { MinFishRarity = 3, Count = 2 });
                t5.Materials.Add(new MaterialNeed { ItemId = SharedItemIds.OreHeatproofMetal, Count = 3 });
                table.Add(t5);
                return table;
            }
        }

        public static RodUpgradeRequirement ForTier(int targetTier)
        {
            foreach (var r in Table) if (r.TargetTier == targetTier) return r;
            return null;
        }

        /// <summary>未登録（そのモジュールが無い）素材は条件から外す。</summary>
        public static bool IsNeedActive(MaterialNeed n) => string.IsNullOrEmpty(n.ItemId) || ItemRegistry.Exists(n.ItemId);

        /// <summary>持ち物から支払う具体的なアイテムを選ぶ（安い物から）。足りなければ null。</summary>
        public static List<KeyValuePair<string, int>> PlanPayment(RodUpgradeRequirement req, Inventory inv)
        {
            var plan = new List<KeyValuePair<string, int>>();
            var reserved = new Dictionary<string, int>();
            foreach (var need in req.Materials)
            {
                if (!IsNeedActive(need)) continue;
                int remaining = need.Count;
                var candidates = new List<ItemStack>();
                foreach (var s in inv.NonEmpty()) if (need.Matches(s)) candidates.Add(s);
                candidates.Sort((a, b) => a.Definition.BasePrice.CompareTo(b.Definition.BasePrice));
                var counted = new Dictionary<string, int>();
                foreach (var c in candidates)
                {
                    if (remaining <= 0) break;
                    reserved.TryGetValue(c.itemId, out var already);
                    counted.TryGetValue(c.itemId, out var thisNeed);
                    int available = inv.CountOf(c.itemId) - already - thisNeed;
                    int take = Mathf.Min(available, remaining);
                    if (take <= 0) continue;
                    counted[c.itemId] = thisNeed + take;
                    remaining -= take;
                }
                if (remaining > 0) return null;
                foreach (var kv in counted)
                {
                    reserved.TryGetValue(kv.Key, out var r);
                    reserved[kv.Key] = r + kv.Value;
                    plan.Add(new KeyValuePair<string, int>(kv.Key, kv.Value));
                }
            }
            return plan;
        }

        /// <summary>ホスト側の検証：送られてきた支払い内容が条件を満たすか。</summary>
        public static bool ValidatePayment(RodUpgradeRequirement req, List<string> ids, List<int> counts)
        {
            if (ids == null || counts == null || ids.Count != counts.Count) return false;
            var left = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < ids.Count; i++) left.Add(new KeyValuePair<string, int>(ids[i], counts[i]));
            foreach (var need in req.Materials)
            {
                if (!IsNeedActive(need)) continue;
                int remaining = need.Count;
                for (int i = 0; i < left.Count && remaining > 0; i++)
                {
                    var probe = new ItemStack(left[i].Key, 1);
                    if (!need.Matches(probe)) continue;
                    int take = Mathf.Min(left[i].Value, remaining);
                    remaining -= take;
                    left[i] = new KeyValuePair<string, int>(left[i].Key, left[i].Value - take);
                }
                if (remaining > 0) return false;
            }
            return true;
        }
    }

    /// <summary>技の説明・習得条件・実演の手順（釣り仕様 §4.3, §5.1）。</summary>
    public static class TechniqueInfo
    {
        /// <summary>実演の1動作。</summary>
        public enum Move { Left, Right, RaiseRod, Reel, Slack }

        public static readonly FishingTechnique[] All =
        {
            FishingTechnique.PrecisionCast, FishingTechnique.LineHandling, FishingTechnique.Lure, FishingTechnique.PowerReel,
            FishingTechnique.Guide, FishingTechnique.ReadFish, FishingTechnique.MasterSecret
        };

        public static string Name(FishingTechnique t)
        {
            switch (t)
            {
                case FishingTechnique.LineHandling: return Loc.T("糸さばき", "Line Handling");
                case FishingTechnique.PowerReel: return Loc.T("ため巻き", "Power Reel");
                case FishingTechnique.Guide: return Loc.T("魚の誘導", "Guiding");
                case FishingTechnique.PrecisionCast: return Loc.T("精密キャスト", "Precision Cast");
                case FishingTechnique.Lure: return Loc.T("誘い釣り", "Luring");
                case FishingTechnique.ReadFish: return Loc.T("魚の見極め", "Reading the Fish");
                default: return Loc.T("名人の極意", "Master's Secret");
            }
        }

        /// <summary>覚えた後に主人公がメモする効果（UI）。</summary>
        public static string Effect(FishingTechnique t)
        {
            switch (t)
            {
                case FishingTechnique.LineHandling: return Loc.T("糸を緩めた時に張力が早く抜ける。切れそうな瞬間に一度だけ自然に糸を逃がす。", "Tension drops faster when slacking; once in a while the line is eased automatically right before it snaps.");
                case FishingTechnique.PowerReel: return Loc.T("魚が休んだ時・疲れた時に、ぐっと速く巻ける。", "Reel much faster while the fish rests or is tired.");
                case FishingTechnique.Guide: return Loc.T("竿を倒す・立てる効き目が強くなり、魚が疲れやすい。", "Tilting and raising the rod works better and tires the fish faster.");
                case FishingTechnique.PrecisionCast: return Loc.T("狙いの幅が広がる。狙いどおりに投げると珍しい魚が寄りやすい。", "Wider aiming zone; a well-aimed cast attracts rarer fish.");
                case FishingTechnique.Lure: return Loc.T("待っている間に Space で誘うと、食いつきが早くなる。", "Twitch the lure with Space while waiting for a faster bite.");
                case FishingTechnique.ReadFish: return Loc.T("魚の次の動きの気配と、疲れ具合が見える。", "See hints of the fish's next move and how tired it is.");
                default: return Loc.T("釣り上げる直前に魚の抵抗が弱まり、針が外れにくい。", "Near the end the fish resists less and rarely slips the hook.");
            }
        }

        /// <summary>名人から教わる時の必要釣りレベル【仮】。小屋の見本（名人不在時の代替手段）は +2。</summary>
        public static int RequiredLevel(FishingTechnique t)
        {
            switch (t)
            {
                case FishingTechnique.PrecisionCast: return 1;
                case FishingTechnique.LineHandling: return 2;
                case FishingTechnique.Lure: return 2;
                case FishingTechnique.PowerReel: return 3;
                case FishingTechnique.Guide: return 4;
                case FishingTechnique.ReadFish: return 5;
                default: return 7;
            }
        }

        public const int HutLevelPenalty = 2;

        /// <summary>実演の手順。名人は宇宙語で話すが、動作を見れば真似できる（宇宙語の理解は不要）。</summary>
        public static Move[] Demonstration(FishingTechnique t)
        {
            switch (t)
            {
                case FishingTechnique.LineHandling: return new[] { Move.Reel, Move.Slack, Move.Reel, Move.Slack };
                case FishingTechnique.PowerReel: return new[] { Move.Slack, Move.Slack, Move.Reel, Move.Reel, Move.Reel };
                case FishingTechnique.Guide: return new[] { Move.Left, Move.Right, Move.RaiseRod, Move.Reel };
                case FishingTechnique.PrecisionCast: return new[] { Move.RaiseRod, Move.Reel, Move.Slack };
                case FishingTechnique.Lure: return new[] { Move.Left, Move.Left, Move.Right, Move.Slack, Move.Reel };
                case FishingTechnique.ReadFish: return new[] { Move.Slack, Move.Right, Move.Slack, Move.Left, Move.Reel };
                default: return new[] { Move.RaiseRod, Move.Slack, Move.Left, Move.Right, Move.Reel, Move.Reel };
            }
        }

        public static string MoveLabel(Move m)
        {
            switch (m)
            {
                case Move.Left: return Loc.T("竿を左へ倒す [A]", "Tilt rod left [A]");
                case Move.Right: return Loc.T("竿を右へ倒す [D]", "Tilt rod right [D]");
                case Move.RaiseRod: return Loc.T("竿を立てる [W]", "Raise rod [W]");
                case Move.Reel: return Loc.T("巻く [Space]", "Reel [Space]");
                default: return Loc.T("糸を緩める [S]", "Slack [S]");
            }
        }

        /// <summary>名人の動作を観察した描写（翻訳ではなく見たままの様子）。</summary>
        public static string MoveObservation(Move m)
        {
            switch (m)
            {
                case Move.Left: return Loc.T("竿をすっと左へ倒した", "tilted the rod to the left");
                case Move.Right: return Loc.T("竿を右へ倒した", "tilted the rod to the right");
                case Move.RaiseRod: return Loc.T("竿をぐっと立てた", "raised the rod high");
                case Move.Reel: return Loc.T("くるくると巻いた", "reeled in");
                default: return Loc.T("ふっと糸を緩めた", "let the line go slack");
            }
        }
    }
}
