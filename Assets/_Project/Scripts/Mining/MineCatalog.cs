using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Mining
{
    /// <summary>
    /// 鉱山の調整用数値（すべて【仮数値】。鉱山仕様 §3.3「具体的な確率や出現数は未決定」）。
    /// </summary>
    public static class MiningTuning
    {
        public const int FloorCount = 10;

        // ---- 生成（地形）----
        public const int GridSize = 24;          // 1階あたりのマス数（一辺）
        public const float Cell = 3f;            // 1マスの大きさ（m）
        public const float WallHeight = 2.4f;
        public static readonly Vector3 MineOrigin = new Vector3(6000f, 0f, 6000f); // 屋外から離れた座標
        public const float FloorSpacing = 120f;
        /// <summary>地下湖のある階（釣りの地下湖：釣り仕様 §6, §12.3）。</summary>
        public static readonly int[] LakeFloors = { 2, 4, 6, 8, 10 };

        // ---- 日替わりの鉱石（§3）----
        public const int NodesBase = 16;
        public const int NodesPerFloor = 1;
        public const int NodesRandom = 7;
        public const float RockShare = 0.38f;
        public const float ForageShare = 0.05f;
        public const float FavoredOreMultiplier = 2.5f;   // 「今日は○○が多い」日
        public const float GemDayChance = 0.12f;          // 「今日は宝石が多い」日の確率
        public const float GemDayMultiplier = 4f;
        public const float GemShareBase = 0.08f;

        // ---- 硬さと採掘（§4）----
        public static float NodeHp(int hardness, MineNodeKind kind) => kind == MineNodeKind.Forage ? 1f : kind == MineNodeKind.Rock ? 2f + hardness : 3f + hardness * 2f;
        /// <summary>1回叩いた時のダメージ。段階が足りなければ0（掘れない）。強化するほど以前の岩も楽に掘れる（§9.2）。</summary>
        public static float HitDamage(int tier, int hardness, int level)
        {
            if (tier < hardness) return 0f;
            return (1f + (tier - hardness) * 1.0f) * (1f + 0.08f * (Mathf.Max(1, level) - 1));
        }

        // ---- 採掘レベル（§5。スタミナは無いので効率と発見に効く）----
        public const int MaxLevel = 10;
        public static readonly int[] LevelXp = { 0, 30, 90, 180, 320, 500, 750, 1050, 1450, 2000 };
        public const float LevelBonusDropChance = 0.03f;
        public const float LevelGemQualityBonus = 0.03f;
        public static int XpFor(MineNodeKind kind, int hardness) => kind == MineNodeKind.Forage ? 1 : 2 + hardness * 3 + (kind == MineNodeKind.Gem ? 6 : 0);
        public static int LevelForXp(int xp)
        {
            int lv = 1;
            for (int i = 0; i < LevelXp.Length; i++) if (xp >= LevelXp[i]) lv = i + 1;
            return Mathf.Clamp(lv, 1, MaxLevel);
        }

        // ---- 鍛冶屋の買い取り（§11.2：商店の価値を損なわないよう低価格・1日の上限あり）----
        public const float BuybackRate = 0.35f;
        public const int BuybackDailyLimit = 20;
        public const float BlacksmithFeeShare = 0.6f; // 強化代金のうち鍛冶屋の財布に入る割合（村の経済循環）

        /// <summary>岩・その日の地下水の硬さ帯（階層→必要段階）。</summary>
        public static int RockHardness(int floor) => floor <= 2 ? 1 : floor <= 5 ? 2 : floor <= 8 ? 3 : 4;
    }

    /// <summary>鉱石・宝石・発見物の定義。</summary>
    public sealed class MineMaterial
    {
        public string Id;
        public MineNodeKind Kind;
        public int Hardness;     // 必要なツルハシの段階（1〜5）
        public int MinFloor, MaxFloor;
        public float Weight;     // 出現の重み
        public float SizeMin, SizeMax; // 宝石の大きさ（cm）
        public bool FromRockOnly; // 岩の中からだけ見つかる（化石・遺物）
    }

    /// <summary>
    /// 鉱石・宝石・発見物の一覧（鉱山仕様 §2, §6。名称・階層割り当ては【仮案】）。
    /// 必須：導電性鉱石（1〜2階）・高強度金属鉱石（3〜5階）・耐熱金属鉱石（6〜8階）・高密度エネルギー結晶（9〜10階）。
    /// </summary>
    public static class MineCatalog
    {
        public const string Stone = "ore.stone";
        public const string RosyCopper = "ore.rosy_copper";
        public const string WarmCoal = "ore.warm_coal";
        public const string MoonSilver = "ore.moon_silver";
        public const string BubbleQuartz = "ore.bubble_quartz";
        public const string SkyGold = "ore.sky_gold";
        public const string StarCore = "ore.star_core";
        public const string GemDew = "gem.dew_drop";
        public const string GemLeaf = "gem.leaf_jade";
        public const string GemEmber = "gem.ember_heart";
        public const string GemTwilight = "gem.twilight_opal";
        public const string GemStarTear = "gem.star_tear";
        public const string FossilShell = "find.fossil_shell";
        public const string FossilWing = "find.fossil_wing";
        public const string AncientGear = "find.ancient_gear";
        public const string OldCharm = "find.old_tool_charm";
        public const string StarMapTablet = "find.star_map_tablet";
        public const string CaveMushroom = "find.cave_mushroom";

        public const string TagFossil = "fossil";
        public const string TagRelic = "relic";

        private static readonly List<MineMaterial> all = new List<MineMaterial>();
        private static readonly Dictionary<string, MineMaterial> byId = new Dictionary<string, MineMaterial>();

        public static IReadOnlyList<MineMaterial> All
        {
            get
            {
                if (all.Count == 0) Build();
                return all;
            }
        }

        public static MineMaterial Get(string id)
        {
            if (all.Count == 0) Build();
            if (id != null && byId.TryGetValue(id, out var m)) return m;
            return null;
        }

        private static void M(string id, MineNodeKind kind, int hardness, int minF, int maxF, float weight, float sizeMin = 0, float sizeMax = 0, bool rockOnly = false)
        {
            var m = new MineMaterial { Id = id, Kind = kind, Hardness = hardness, MinFloor = minF, MaxFloor = maxF, Weight = weight, SizeMin = sizeMin, SizeMax = sizeMax, FromRockOnly = rockOnly };
            all.Add(m);
            byId[id] = m;
        }

        private static void Build()
        {
            all.Clear();
            byId.Clear();
            // 鉱石（階層が深いほど硬く高価）
            M(Stone, MineNodeKind.Ore, 1, 1, 10, 3f);
            M(SharedItemIds.OreConductive, MineNodeKind.Ore, 1, 1, 2, 7f);
            M(RosyCopper, MineNodeKind.Ore, 1, 1, 4, 5f);
            M(WarmCoal, MineNodeKind.Ore, 1, 1, 6, 2.5f);
            M(SharedItemIds.OreSturdyMetal, MineNodeKind.Ore, 2, 3, 5, 7f);
            M(MoonSilver, MineNodeKind.Ore, 2, 4, 7, 4f);
            M(BubbleQuartz, MineNodeKind.Ore, 2, 3, 7, 3f);
            M(SharedItemIds.OreHeatproofMetal, MineNodeKind.Ore, 3, 6, 8, 7f);
            M(SkyGold, MineNodeKind.Ore, 3, 7, 10, 4f);
            M(SharedItemIds.OreEnergyCrystal, MineNodeKind.Ore, 4, 9, 10, 6f);
            M(StarCore, MineNodeKind.Ore, 5, 10, 10, 1.2f);
            // 宝石（大きさ・透明度の個体差 §6.1）
            M(GemDew, MineNodeKind.Gem, 1, 1, 4, 3f, 0.6f, 2.8f);
            M(GemLeaf, MineNodeKind.Gem, 2, 3, 6, 2.5f, 0.8f, 3.2f);
            M(GemEmber, MineNodeKind.Gem, 2, 4, 7, 2.2f, 0.8f, 3.5f);
            M(GemTwilight, MineNodeKind.Gem, 3, 6, 9, 2f, 1f, 4f);
            M(GemStarTear, MineNodeKind.Gem, 4, 9, 10, 1.4f, 1.2f, 5f);
            // 鉱石以外の発見物（岩の中から §6）
            M(FossilShell, MineNodeKind.Rock, 1, 1, 6, 1f, 0, 0, true);
            M(FossilWing, MineNodeKind.Rock, 2, 4, 9, 0.7f, 0, 0, true);
            M(AncientGear, MineNodeKind.Rock, 2, 3, 10, 0.6f, 0, 0, true);
            M(OldCharm, MineNodeKind.Rock, 1, 1, 8, 0.5f, 0, 0, true);
            M(StarMapTablet, MineNodeKind.Rock, 4, 9, 10, 0.25f, 0, 0, true);
            // 洞窟の採集物（クエスト仕様の「洞窟に生えるキノコ」§9）
            M(CaveMushroom, MineNodeKind.Forage, 0, 1, 10, 1f);
        }

        public static IEnumerable<MineMaterial> OnFloor(int floor, MineNodeKind kind)
        {
            foreach (var m in All)
                if (m.Kind == kind && !m.FromRockOnly && floor >= m.MinFloor && floor <= m.MaxFloor) yield return m;
        }

        public static IEnumerable<MineMaterial> RockFindsOnFloor(int floor)
        {
            foreach (var m in All)
                if (m.FromRockOnly && floor >= m.MinFloor && floor <= m.MaxFloor) yield return m;
        }

        // ---------------- アイテム登録 ----------------

        public static void RegisterItems()
        {
            R(Stone, "石材", "Stone", "鉱山のどこでも採れる石。家具や建材になる。", "Stone found all over the mine. Used for furniture and building.",
                ItemCategory.Ore, 6, 0, new Color(0.62f, 0.6f, 0.58f), PrimitiveType.Cube, Concepts.STONE, ItemTags.ColorWhite, ItemTags.Ore);
            R(SharedItemIds.OreConductive, "ピリ鉱（導電性鉱石）", "Tingle Ore (conductive)", "触るとかすかにぴりっとする黄色い鉱石。電気をよく通す。浅い階（1〜2階付近）で見つかる。",
                "A yellow ore that tingles faintly. Conducts electricity well. Found on shallow floors (around F1-2).",
                ItemCategory.Ore, 18, 1, new Color(1f, 0.85f, 0.25f), PrimitiveType.Cube, Concepts.ORE, ItemTags.ColorYellow, ItemTags.Ore, ItemTags.Metal, ItemTags.Conductive, ItemTags.RocketMaterial);
            R(RosyCopper, "あかね銅鉱", "Rosy Copper Ore", "夕焼け色の柔らかい銅の鉱石。", "A soft copper ore the color of sunset.",
                ItemCategory.Ore, 14, 0, new Color(0.9f, 0.5f, 0.35f), PrimitiveType.Cube, Concepts.ORE, ItemTags.ColorOrange, ItemTags.Ore, ItemTags.Metal);
            R(WarmCoal, "ぬくもり炭", "Warm Coal", "ほんのり温かい黒い石。よく燃える。", "A faintly warm black stone. Burns well.",
                ItemCategory.Ore, 12, 0, new Color(0.18f, 0.16f, 0.16f), PrimitiveType.Cube, Concepts.STONE, ItemTags.ColorBlack, ItemTags.Ore, ItemTags.Fuel);
            R(SharedItemIds.OreSturdyMetal, "かたがね鉱（高強度金属鉱石）", "Toughsteel Ore (sturdy metal)", "叩いても曲がらない青灰色の金属を含む。3〜5階あたりに多い。",
                "Contains a blue-grey metal that won't bend. Common around F3-5.",
                ItemCategory.Ore, 40, 1, new Color(0.45f, 0.55f, 0.7f), PrimitiveType.Cube, Concepts.METAL, ItemTags.ColorBlue, ItemTags.Ore, ItemTags.Metal, ItemTags.RocketMaterial);
            R(MoonSilver, "つきしろ銀鉱", "Moonwhite Silver Ore", "月明かりのように白く光る銀の鉱石。", "Silver ore that glows white like moonlight.",
                ItemCategory.Ore, 55, 2, new Color(0.88f, 0.9f, 0.95f), PrimitiveType.Cube, Concepts.METAL, ItemTags.ColorWhite, ItemTags.Ore, ItemTags.Metal, ItemTags.Shiny);
            R(BubbleQuartz, "あわ水晶", "Bubble Quartz", "中に小さな泡が閉じ込められた水晶。", "Quartz with tiny bubbles trapped inside.",
                ItemCategory.Ore, 45, 1, new Color(0.75f, 0.9f, 1f), PrimitiveType.Cube, Concepts.STONE, ItemTags.ColorBlue, ItemTags.Ore, ItemTags.Crystal, ItemTags.Shiny);
            R(SharedItemIds.OreHeatproofMetal, "ひだね鉄（耐熱金属鉱石）", "Emberproof Iron (heatproof metal)", "火にくべても赤くならない不思議な鉄。6〜8階あたりで見つかる。",
                "A strange iron that never glows red in fire. Found around F6-8.",
                ItemCategory.Ore, 95, 2, new Color(0.75f, 0.3f, 0.25f), PrimitiveType.Cube, Concepts.METAL, ItemTags.ColorRed, ItemTags.Ore, ItemTags.Metal, ItemTags.Heatproof, ItemTags.RocketMaterial);
            R(SkyGold, "そらきん鉱", "Skygold Ore", "空の色を映す金の鉱石。", "Gold ore that reflects the color of the sky.",
                ItemCategory.Ore, 130, 3, new Color(1f, 0.82f, 0.4f), PrimitiveType.Cube, Concepts.METAL, ItemTags.ColorYellow, ItemTags.Ore, ItemTags.Metal, ItemTags.Shiny, ItemTags.Rare);
            R(SharedItemIds.OreEnergyCrystal, "高密度エネルギー結晶", "Dense Energy Crystal", "内側で光がうずまく重い結晶。最深部（9〜10階）でしか見つからない。",
                "A heavy crystal with light swirling inside. Found only in the depths (F9-10).",
                ItemCategory.Ore, 220, 4, new Color(0.6f, 0.45f, 1f), PrimitiveType.Cube, Concepts.STONE, ItemTags.ColorPurple, ItemTags.Ore, ItemTags.Crystal, ItemTags.Fuel, ItemTags.Glowing, ItemTags.RocketMaterial, ItemTags.Rare);
            R(StarCore, "星核石", "Star Core Stone", "星の芯のかけらと言われる、とても硬い石。最高のツルハシでないと割れない。",
                "Said to be a fragment of a star's core. Only the finest pickaxe can crack it.",
                ItemCategory.Ore, 600, 5, new Color(1f, 0.95f, 0.75f), PrimitiveType.Cube, Concepts.STAR, ItemTags.ColorWhite, ItemTags.Ore, ItemTags.Glowing, ItemTags.Rare, ItemTags.Gift);

            R(GemDew, "しずく石", "Dewdrop Stone", "朝露のように澄んだ青い宝石。大きさと透明度で価値が変わる。", "A blue gem clear as morning dew. Size and clarity set its value.",
                ItemCategory.Gem, 80, 1, new Color(0.45f, 0.75f, 1f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.ColorBlue, ItemTags.Gem, ItemTags.Shiny, ItemTags.Gift);
            R(GemLeaf, "こもれび翠", "Sunleaf Jade", "木漏れ日のような緑の宝石。", "A green gem like sunlight through leaves.",
                ItemCategory.Gem, 120, 2, new Color(0.45f, 0.85f, 0.5f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.ColorGreen, ItemTags.Gem, ItemTags.Shiny, ItemTags.Gift);
            R(GemEmber, "ほむら玉", "Ember Heart", "中で小さな炎が揺れているような赤い宝石。", "A red gem with a tiny flame flickering inside.",
                ItemCategory.Gem, 160, 2, new Color(1f, 0.35f, 0.3f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.ColorRed, ItemTags.Gem, ItemTags.Shiny, ItemTags.Gift);
            R(GemTwilight, "たそがれオパール", "Twilight Opal", "見る角度で夕暮れの色に変わる紫の宝石。", "A purple gem that shifts to dusk colors as you turn it.",
                ItemCategory.Gem, 260, 3, new Color(0.7f, 0.45f, 0.9f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.ColorPurple, ItemTags.Gem, ItemTags.Shiny, ItemTags.Rare, ItemTags.Gift);
            R(GemStarTear, "ほしのなみだ", "Star Tear", "夜空のしずくのように白く光る、とても珍しい宝石。", "A very rare gem glowing white like a drop of night sky.",
                ItemCategory.Gem, 480, 4, new Color(0.95f, 0.97f, 1f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.ColorWhite, ItemTags.Gem, ItemTags.Glowing, ItemTags.Rare, ItemTags.Gift);

            R(FossilShell, "渦巻き貝の化石", "Spiral Shell Fossil", "大昔の渦巻き貝の化石。研究好きの住民が喜びそう。", "A fossil of an ancient spiral shell.",
                ItemCategory.Misc, 70, 2, new Color(0.85f, 0.78f, 0.6f), PrimitiveType.Sphere, Concepts.STONE, ItemTags.ColorWhite, TagFossil, ItemTags.Rare);
            R(FossilWing, "羽の化石", "Wing Fossil", "空を飛んでいた何かの羽の跡が残る石。", "A stone with the print of a wing from something that once flew.",
                ItemCategory.Misc, 110, 3, new Color(0.8f, 0.75f, 0.65f), PrimitiveType.Cube, Concepts.STONE, ItemTags.ColorWhite, TagFossil, ItemTags.Rare);
            R(AncientGear, "古い歯車", "Ancient Gear", "この星の昔の誰かが作ったらしい、錆びない歯車。", "A rustless gear apparently made by someone of this planet long ago.",
                ItemCategory.Misc, 120, 3, new Color(0.7f, 0.65f, 0.5f), PrimitiveType.Cylinder, Concepts.THING, ItemTags.ColorYellow, TagRelic, ItemTags.Metal, ItemTags.Precision);
            R(OldCharm, "古いツルハシの飾り", "Old Pickaxe Charm", "昔の採掘師が道具に付けていたらしい小さな飾り。", "A small charm an old miner seems to have hung on their tool.",
                ItemCategory.Misc, 50, 2, new Color(0.9f, 0.6f, 0.4f), PrimitiveType.Sphere, Concepts.THING, ItemTags.ColorOrange, TagRelic, ItemTags.Cute, ItemTags.Gift);
            R(StarMapTablet, "星図の石板", "Star Map Tablet", "星の並びが刻まれた石板。この星の空とは少し違う。", "A stone tablet carved with stars - slightly different from this planet's sky.",
                ItemCategory.Misc, 400, 4, new Color(0.35f, 0.35f, 0.5f), PrimitiveType.Cube, Concepts.STAR, ItemTags.ColorBlack, TagRelic, ItemTags.Rare, ItemTags.Gift);
            var mush = R(CaveMushroom, "ほたるダケ", "Glowcap", "洞窟の暗がりでぼんやり光るキノコ。料理に使える。", "A mushroom that glows dimly in dark caves. Good for cooking.",
                ItemCategory.Crop, 22, 1, new Color(0.6f, 0.9f, 0.8f), PrimitiveType.Capsule, Concepts.FOOD, ItemTags.ColorGreen, ItemTags.Ingredient, ItemTags.Vegetable, ItemTags.Glowing, ItemTags.Edible);
            mush.Consumable = true;
        }

        private static ItemDefinition R(string id, string ja, string en, string dja, string den, ItemCategory cat, int price, int rarity, Color color,
            PrimitiveType shape, string generic, params string[] tags)
        {
            var def = new ItemDefinition
            {
                Id = id, NameJa = ja, NameEn = en, DescriptionJa = dja, DescriptionEn = den, Category = cat, BasePrice = price,
                Rarity = rarity, Color = color, Shape = shape, GenericConceptId = generic, SourceActivity = "mining",
                MaxStack = cat == ItemCategory.Gem ? 30 : 99, Durable = cat != ItemCategory.Crop
            };
            def.WithTags(tags);
            ItemRegistry.Register(def);
            return def;
        }
    }

    /// <summary>ツルハシの強化表（鍛冶屋：前段階の鉱石＋お金。鉱山仕様 §4、バランス §9.2）【仮数値】。</summary>
    public sealed class PickaxeUpgrade
    {
        public int TargetTier;
        public int Money;
        public readonly List<KeyValuePair<string, int>> Ores = new List<KeyValuePair<string, int>>();

        private static List<PickaxeUpgrade> table;

        public static IReadOnlyList<PickaxeUpgrade> Table
        {
            get
            {
                if (table != null) return table;
                table = new List<PickaxeUpgrade>();
                var t2 = new PickaxeUpgrade { TargetTier = 2, Money = 300 };
                t2.Ores.Add(new KeyValuePair<string, int>(SharedItemIds.OreConductive, 5));
                t2.Ores.Add(new KeyValuePair<string, int>(MineCatalog.RosyCopper, 5));
                table.Add(t2);
                var t3 = new PickaxeUpgrade { TargetTier = 3, Money = 1000 };
                t3.Ores.Add(new KeyValuePair<string, int>(SharedItemIds.OreSturdyMetal, 6));
                t3.Ores.Add(new KeyValuePair<string, int>(MineCatalog.MoonSilver, 3));
                table.Add(t3);
                var t4 = new PickaxeUpgrade { TargetTier = 4, Money = 2500 };
                t4.Ores.Add(new KeyValuePair<string, int>(SharedItemIds.OreHeatproofMetal, 6));
                t4.Ores.Add(new KeyValuePair<string, int>(MineCatalog.SkyGold, 3));
                table.Add(t4);
                var t5 = new PickaxeUpgrade { TargetTier = 5, Money = 6000 };
                t5.Ores.Add(new KeyValuePair<string, int>(SharedItemIds.OreEnergyCrystal, 5));
                t5.Ores.Add(new KeyValuePair<string, int>(MineCatalog.SkyGold, 5));
                table.Add(t5);
                return table;
            }
        }

        public static PickaxeUpgrade ForTier(int tier)
        {
            foreach (var u in Table) if (u.TargetTier == tier) return u;
            return null;
        }
    }
}
