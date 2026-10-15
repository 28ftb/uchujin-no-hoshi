using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Shop
{
    /// <summary>商品・陳列台・需要に関する判定（商店仕様 §2〜§3、バランス仕様 §7.2〜§7.3）。</summary>
    public static class ShopRules
    {
        public static bool IsSellable(ItemDefinition def)
        {
            if (def == null) return false;
            if (def.HasTag(ItemTags.NotSellable)) return false;
            switch (def.Category)
            {
                case ItemCategory.KeyItem:
                case ItemCategory.Letter:
                    return false;
            }
            return true;
        }

        /// <summary>その陳列台に置けるか。</summary>
        public static bool CanDisplay(ShopDisplayType display, ItemDefinition def)
        {
            if (!IsSellable(def)) return false;
            bool large = def.Category == ItemCategory.Furniture;
            switch (display)
            {
                case ShopDisplayType.FurnitureStage:
                    return def.Category == ItemCategory.Furniture || def.Category == ItemCategory.Decor;
                case ShopDisplayType.Aquarium:
                    return def.Category == ItemCategory.Fish;
                case ShopDisplayType.Chilled:
                    return IsPerishable(def) || def.HasTag(ItemTags.Drink) || def.HasTag(ItemTags.Ingredient);
                default:
                    // 大きな家具は家具ステージが必要（商店仕様 §8「店舗面積の拡張：大きな商品・家具も扱える」）
                    return !large;
            }
        }

        public static string CannotDisplayReasonJa(ShopDisplayType display)
        {
            switch (display)
            {
                case ShopDisplayType.FurnitureStage: return "家具ステージには家具・装飾だけを置けます";
                case ShopDisplayType.Aquarium: return "水槽には魚だけを入れられます";
                case ShopDisplayType.Chilled: return "冷蔵棚には生鮮品・料理・飲み物を置けます";
                default: return "大きな家具は家具ステージに置きます";
            }
        }

        public static string CannotDisplayReasonEn(ShopDisplayType display)
        {
            switch (display)
            {
                case ShopDisplayType.FurnitureStage: return "The stage is for furniture and decor";
                case ShopDisplayType.Aquarium: return "The aquarium is for fish only";
                case ShopDisplayType.Chilled: return "The chilled case is for fresh food and drinks";
                default: return "Large furniture needs a furniture stage";
            }
        }

        /// <summary>時間がたつと魅力が落ちる生鮮品か（冷蔵棚・水槽なら落ちない）。</summary>
        public static bool IsPerishable(ItemDefinition def)
        {
            if (def == null) return false;
            switch (def.Category)
            {
                case ItemCategory.Fish:
                case ItemCategory.Crop:
                case ItemCategory.Food:
                    return true;
            }
            return false;
        }

        /// <summary>鮮度係数 0.5〜1（鮮度の詳細は未決定のため、売れ行きにだけ影響し、腐って消えることはしない）。</summary>
        public static float Freshness(ShelfSlotData slot, ItemDefinition def, int today)
        {
            if (!IsPerishable(def)) return 1f;
            if (slot.display == ShopDisplayType.Chilled || slot.display == ShopDisplayType.Aquarium) return 1f;
            int days = today - slot.listedDay;
            if (days <= ShopConfig.FreshDays) return 1f;
            return Mathf.Max(ShopConfig.MinFreshness, 1f - ShopConfig.FreshDecayPerDay * (days - ShopConfig.FreshDays));
        }

        public static float QualityMultiplier(int quality, float picky) => 1f + quality * (0.2f + 0.2f * picky);

        /// <summary>値付けの目安（主人公への表示用。基準価格×品質）。</summary>
        public static int SuggestedPrice(ItemDefinition def, int quality)
        {
            if (def == null) return 1;
            return Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, def.BasePrice) * QualityMultiplier(quality, 0.5f)));
        }

        public static string DisplayLabel(ShopDisplayType t)
        {
            switch (t)
            {
                case ShopDisplayType.Chilled: return Loc.T("冷蔵棚", "Chilled");
                case ShopDisplayType.Aquarium: return Loc.T("水槽", "Aquarium");
                case ShopDisplayType.FurnitureStage: return Loc.T("家具ステージ", "Stage");
                case ShopDisplayType.Specials: return Loc.T("特売コーナー", "Bargains");
                default: return Loc.T("棚", "Shelf");
            }
        }

        /// <summary>目的（ShoppingIntent.need）と商品の一致度 0〜1。</summary>
        public static float NeedMatch(ShoppingNeed need, ItemDefinition def)
        {
            if (def == null) return 0f;
            var c = def.Category;
            switch (need)
            {
                case ShoppingNeed.Any:
                    return 0.5f;
                case ShoppingNeed.Food:
                    if (c == ItemCategory.Food || c == ItemCategory.Processed) return 1f;
                    if (def.HasTag(ItemTags.Edible) || def.HasTag(ItemTags.Food) || def.HasTag(ItemTags.Fruit)) return 0.8f;
                    if (c == ItemCategory.Crop || c == ItemCategory.Fish) return 0.4f;
                    return 0f;
                case ShoppingNeed.Ingredient:
                    if (def.HasTag(ItemTags.Ingredient)) return 1f;
                    if (c == ItemCategory.Crop || c == ItemCategory.Fish || c == ItemCategory.Processed) return 0.9f;
                    return 0f;
                case ShoppingNeed.Fish:
                    return c == ItemCategory.Fish ? 1f : 0f;
                case ShoppingNeed.Crop:
                    if (c == ItemCategory.Crop) return 1f;
                    if (def.HasTag(ItemTags.Fruit) || def.HasTag(ItemTags.Vegetable)) return 0.8f;
                    return 0f;
                case ShoppingNeed.Furniture:
                    return c == ItemCategory.Furniture ? 1f : 0f;
                case ShoppingNeed.Decor:
                    if (c == ItemCategory.Decor) return 1f;
                    if (def.HasTag(ItemTags.Flower) || def.HasTag(ItemTags.Plant) || c == ItemCategory.Gem) return 0.6f;
                    if (c == ItemCategory.Furniture) return 0.4f;
                    return 0f;
                case ShoppingNeed.Gift:
                    if (def.HasTag(ItemTags.Gift) || def.HasTag(ItemTags.Flower) || c == ItemCategory.Gem || def.HasTag(ItemTags.Cute) || def.HasTag(ItemTags.Sweet)) return 1f;
                    if (c == ItemCategory.Food || c == ItemCategory.Processed || c == ItemCategory.Decor) return 0.5f;
                    return 0.1f;
                case ShoppingNeed.Tool:
                    return c == ItemCategory.Tool || c == ItemCategory.Bait ? 1f : 0f;
                case ShoppingNeed.Material:
                    if (c == ItemCategory.Material || c == ItemCategory.Ore) return 1f;
                    if (c == ItemCategory.Gem) return 0.6f;
                    return 0f;
                case ShoppingNeed.Curiosity:
                    if (def.Rarity >= 2 || def.HasTag(ItemTags.Rare)) return 1f;
                    if (def.Rarity == 1 || def.HasTag(ItemTags.Glowing)) return 0.4f;
                    return 0f;
            }
            return 0f;
        }

        public static bool IsRare(ItemDefinition def) => def != null && (def.Rarity >= 3 || def.HasTag(ItemTags.Rare));

        /// <summary>関連商品か（まとめ買い）。</summary>
        public static bool Related(ItemDefinition a, ItemDefinition b)
        {
            if (a == null || b == null || a.Id == b.Id) return false;
            if (a.Category == b.Category) return true;
            string[] shared = { ItemTags.Ingredient, ItemTags.Food, ItemTags.Flower, ItemTags.Furniture, ItemTags.Gem, ItemTags.Seed, ItemTags.Drink, ItemTags.Sweet };
            foreach (var t in shared) if (a.HasTag(t) && b.HasTag(t)) return true;
            return false;
        }

        /// <summary>職業ごとの関心（住民生活仕様 §4、§10）。</summary>
        public static float JobAffinity(ResidentJob job, ItemDefinition def)
        {
            if (def == null) return 0f;
            var c = def.Category;
            switch (job)
            {
                case ResidentJob.Cook:
                    return def.HasTag(ItemTags.Ingredient) || c == ItemCategory.Crop || c == ItemCategory.Fish || c == ItemCategory.Processed ? 0.3f : 0f;
                case ResidentJob.FishingMaster:
                    return c == ItemCategory.Fish || c == ItemCategory.Bait ? 0.3f : 0f;
                case ResidentJob.FarmingMaster:
                case ResidentJob.SeedSeller:
                    return c == ItemCategory.Seed || c == ItemCategory.Fertilizer || c == ItemCategory.Crop ? 0.3f : 0f;
                case ResidentJob.Florist:
                    return def.HasTag(ItemTags.Flower) || def.HasTag(ItemTags.Plant) ? 0.4f : 0f;
                case ResidentJob.Carpenter:
                case ResidentJob.Builder:
                    return c == ItemCategory.Material || c == ItemCategory.Furniture ? 0.25f : 0f;
                case ResidentJob.Blacksmith:
                case ResidentJob.Miner:
                case ResidentJob.Mechanic:
                    return c == ItemCategory.Ore || c == ItemCategory.Material || c == ItemCategory.RocketPart || def.HasTag(ItemTags.Metal) ? 0.3f : 0f;
                case ResidentJob.CreatureExpert:
                    return c == ItemCategory.CreatureFood || c == ItemCategory.CreatureItem || def.HasTag(ItemTags.CreatureHabitat) ? 0.35f : 0f;
                case ResidentJob.Artist:
                    return c == ItemCategory.Decor || def.HasTag(ItemTags.Cute) || def.HasTag(ItemTags.Shiny) || Concepts.ColorFromTags(def) != null ? 0.25f : 0f;
                case ResidentJob.Musician:
                    return def.HasTag(ItemTags.Music) ? 0.4f : 0f;
                case ResidentJob.Scholar:
                    return IsRare(def) || c == ItemCategory.Gem ? 0.25f : 0f;
                case ResidentJob.Child:
                    return def.HasTag(ItemTags.Toy) || def.HasTag(ItemTags.Sweet) || def.HasTag(ItemTags.Cute) ? 0.35f : 0f;
                case ResidentJob.Traveler:
                    return IsRare(def) ? 0.2f : 0f;
                case ResidentJob.WeatherReader:
                    return def.HasTag(ItemTags.Glowing) ? 0.2f : 0f;
                case ResidentJob.Courier:
                    return def.HasTag(ItemTags.Drink) || c == ItemCategory.Food ? 0.15f : 0f;
                case ResidentJob.Doctor:
                case ResidentJob.Retired:
                    return def.HasTag(ItemTags.Plant) || def.HasTag(ItemTags.Flower) ? 0.1f : 0f;
                case ResidentJob.Merchant:
                    return 0.05f;
            }
            return 0f;
        }

        /// <summary>時間帯・天気による需要の変化（バランス仕様 §7.2）。</summary>
        public static float WeatherTimeModifier(ItemDefinition def, int hour, WeatherState? weather)
        {
            if (def == null) return 0f;
            float m = 0f;
            var c = def.Category;
            // 朝は料理の材料、夕方は晩ごはん
            if (hour >= 6 && hour < 11 && (def.HasTag(ItemTags.Ingredient) || c == ItemCategory.Crop)) m += 0.1f;
            if (hour >= 16 && hour < 20 && (c == ItemCategory.Food || c == ItemCategory.Processed)) m += 0.15f;
            if (weather.HasValue)
            {
                var w = weather.Value;
                if (w.IsRaining)
                {
                    if (c == ItemCategory.Food) m += 0.1f;
                    if (def.HasTag(ItemTags.Outdoor)) m -= 0.15f;
                }
                if (w.warmth > 0.5f && (def.HasTag(ItemTags.Drink) || def.HasTag(ItemTags.Cooling))) m += 0.2f;
                if (w.warmth < -0.5f && c == ItemCategory.Food) m += 0.15f;
                if ((w.Has(AlienPhenomenon.StarBand) || w.Has(AlienPhenomenon.LightRain)) && def.HasTag(ItemTags.Glowing)) m += 0.2f;
                if (w.Has(AlienPhenomenon.BloomTide) && def.HasTag(ItemTags.Flower)) m += 0.15f;
                if (w.Has(AlienPhenomenon.CrystalHaze) && (c == ItemCategory.Gem || def.HasTag(ItemTags.Crystal))) m += 0.15f;
            }
            return m;
        }

        // ---------------- 宇宙語の意味データ用の対応表 ----------------

        /// <summary>目的の総称概念。</summary>
        public static string NeedConcept(ShoppingNeed need)
        {
            switch (need)
            {
                case ShoppingNeed.Food: return Concepts.FOOD;
                case ShoppingNeed.Ingredient: return Concepts.VEGETABLE;
                case ShoppingNeed.Fish: return Concepts.FISH;
                case ShoppingNeed.Crop: return Concepts.CROP;
                case ShoppingNeed.Furniture: return Concepts.FURNITURE;
                case ShoppingNeed.Decor: return Concepts.FLOWER;
                case ShoppingNeed.Gift: return Concepts.GIFT;
                case ShoppingNeed.Tool: return Concepts.TOOL;
                case ShoppingNeed.Material: return Concepts.METAL;
                default: return Concepts.THING;
            }
        }

        /// <summary>タグが表す名詞概念（無ければ null）。</summary>
        public static string TagNoun(string tag)
        {
            switch (tag)
            {
                case ItemTags.Flower: return Concepts.FLOWER;
                case ItemTags.Fruit: return Concepts.FRUIT;
                case ItemTags.Vegetable: return Concepts.VEGETABLE;
                case ItemTags.Fish: return Concepts.FISH;
                case ItemTags.Gem: return Concepts.GEM;
                case ItemTags.Ore: return Concepts.ORE;
                case ItemTags.Metal: return Concepts.METAL;
                case ItemTags.Music: return Concepts.MUSIC;
                case ItemTags.Light: return Concepts.LAMP;
                case ItemTags.Seating: return Concepts.CHAIR;
                case ItemTags.Table: return Concepts.TABLE;
                case ItemTags.Bed: return Concepts.BED;
                case ItemTags.Storage: return Concepts.BOX;
                case ItemTags.Seed: return Concepts.SEED;
                case ItemTags.Drink: return Concepts.DRINK_N;
                case ItemTags.Furniture: return Concepts.FURNITURE;
                case ItemTags.Tool: return Concepts.TOOL;
                case ItemTags.Food: return Concepts.FOOD;
                case ItemTags.Fuel: return Concepts.FUEL;
            }
            return null;
        }

        /// <summary>タグが表す形容詞概念（無ければ null）。</summary>
        public static string TagAdjective(string tag)
        {
            switch (tag)
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
                case ItemTags.Glowing: return Concepts.GLOWING;
                case ItemTags.Big: return Concepts.BIG;
                case ItemTags.Small: return Concepts.SMALL;
                case ItemTags.Cute: return Concepts.CUTE;
                case ItemTags.Rare: return Concepts.RARE;
                case ItemTags.Shiny: return Concepts.BRIGHT;
            }
            return null;
        }

        /// <summary>色概念 → 要望ボードの絵の色。</summary>
        public static Color ColorOfConcept(string colorConcept, Color fallback)
        {
            switch (colorConcept)
            {
                case Concepts.RED: return new Color(0.95f, 0.3f, 0.3f);
                case Concepts.BLUE: return new Color(0.35f, 0.55f, 1f);
                case Concepts.GREEN: return new Color(0.4f, 0.85f, 0.45f);
                case Concepts.YELLOW: return new Color(1f, 0.9f, 0.3f);
                case Concepts.WHITE: return new Color(0.97f, 0.97f, 0.97f);
                case Concepts.BLACK: return new Color(0.18f, 0.18f, 0.22f);
                case Concepts.PURPLE: return new Color(0.7f, 0.45f, 0.95f);
                case Concepts.ORANGE: return new Color(1f, 0.6f, 0.2f);
                case Concepts.PINK: return new Color(1f, 0.6f, 0.8f);
            }
            return fallback;
        }

        /// <summary>総称の絵の形（具体的な品が思い浮かばない要望用）。</summary>
        public static PrimitiveType NeedShape(ShoppingNeed need)
        {
            switch (need)
            {
                case ShoppingNeed.Fish: return PrimitiveType.Capsule;
                case ShoppingNeed.Crop:
                case ShoppingNeed.Food:
                case ShoppingNeed.Ingredient:
                case ShoppingNeed.Gift:
                case ShoppingNeed.Curiosity:
                    return PrimitiveType.Sphere;
                case ShoppingNeed.Decor: return PrimitiveType.Cylinder;
                default: return PrimitiveType.Cube;
            }
        }
    }

    /// <summary>
    /// 住民の買い物の傾向（商店仕様 §3.2 の表：節約家・お金持ち・食いしん坊・収集家・流行好き・こだわり派・気まぐれ）。
    /// 星のシードと住民IDから決定的に作る（住民モジュールの内部の性格とは独立。商店での振る舞い専用）。
    /// </summary>
    public struct ShopTraits
    {
        public float thrift;      // 節約家：安い物・割引を好み、高いと感じやすい
        public float collector;   // 収集家：珍品なら高くても買う
        public float glutton;     // 食いしん坊：食べ物をよく買う
        public float trendy;      // 流行好き：新商品に反応
        public float picky;       // こだわり派：品質・好物を重視
        public float whimsical;   // 気まぐれ：日によって傾向が変わる
        public float whim;        // 今日の気分 -1〜1
        public float wealth;      // お金持ち度 0〜1（所持金から）

        public static ShopTraits For(ulong seed, IResidentInfo r, int day)
        {
            string id = r.Id;
            var t = new ShopTraits
            {
                thrift = StableHash.Unit(seed, "shop.trait.thrift:" + id),
                collector = StableHash.Unit(seed, "shop.trait.collector:" + id) * 0.8f,
                glutton = StableHash.Unit(seed, "shop.trait.glutton:" + id),
                trendy = StableHash.Unit(seed, "shop.trait.trendy:" + id),
                picky = StableHash.Unit(seed, "shop.trait.picky:" + id),
                whimsical = StableHash.Unit(seed, "shop.trait.whimsical:" + id) * 0.8f,
                whim = StableHash.Unit(seed, "shop.whim:" + id + ":" + day) * 2f - 1f,
                wealth = Mathf.Clamp01(r.Money / 2000f)
            };
            switch (r.Job)
            {
                case ResidentJob.Retired: t.thrift += 0.3f; break;
                case ResidentJob.Child: t.thrift += 0.15f; t.glutton += 0.2f; t.trendy += 0.2f; break;
                case ResidentJob.Merchant: t.thrift += 0.2f; t.picky += 0.2f; break;
                case ResidentJob.Scholar:
                case ResidentJob.CreatureExpert:
                case ResidentJob.Traveler: t.collector += 0.4f; break;
                case ResidentJob.Cook: t.glutton += 0.3f; t.picky += 0.25f; break;
                case ResidentJob.Artist:
                case ResidentJob.Florist: t.trendy += 0.3f; break;
                case ResidentJob.Doctor: t.wealth = Mathf.Clamp01(t.wealth + 0.2f); break;
            }
            if (r.Lineage == AlienLineage.Lifestyle) t.picky += 0.15f;
            if (r.Lineage == AlienLineage.StarChild) t.collector += 0.15f;
            if (r.Lineage == AlienLineage.Expressive) t.whimsical += 0.2f;
            t.thrift = Mathf.Clamp01(t.thrift);
            t.collector = Mathf.Clamp01(t.collector);
            t.glutton = Mathf.Clamp01(t.glutton);
            t.trendy = Mathf.Clamp01(t.trendy);
            t.picky = Mathf.Clamp01(t.picky);
            t.whimsical = Mathf.Clamp01(t.whimsical);
            return t;
        }

        /// <summary>
        /// 「高い」と感じる境界（値段÷感じる価値の比率）。個体差・好み・懐具合・気分で変わる。
        /// 「適正価格を超えたら全員が絶対に買わない」にはしない（商店仕様 §3.1）。
        /// </summary>
        public float Tolerance(float preference)
        {
            float tol = 1.0f + 0.35f * wealth - 0.3f * thrift + 0.25f * Mathf.Max(0f, preference) + 0.12f * whim * whimsical;
            return Mathf.Clamp(tol, 0.6f, 2.0f);
        }
    }
}
