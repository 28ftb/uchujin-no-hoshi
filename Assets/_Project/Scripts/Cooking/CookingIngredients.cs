using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 材料指定の作り方。他モジュールのアイテムIDは分からないため、タグ・分類で指定する（「魚なら何でも」）。
    /// ロケット素材など共有IDが決まっているものは固有指定（SharedItemIds）を使う。
    /// </summary>
    public static class Ing
    {
        public static IngredientSpec Item(string itemId, int count = 1)
        {
            return new IngredientSpec { ItemId = itemId, Count = count, LabelJa = null, LabelEn = null };
        }

        public static bool IsFish(ItemDefinition d) => d.Category == ItemCategory.Fish || d.HasTag(ItemTags.Fish);
        public static bool IsFruit(ItemDefinition d) => d.HasTag(ItemTags.Fruit) && d.Category != ItemCategory.Seed;
        public static bool IsGrain(ItemDefinition d) => d.HasTag(ItemTags.Grain) && d.Category != ItemCategory.Seed;

        public static bool IsVegetable(ItemDefinition d)
        {
            if (d.Category == ItemCategory.Seed) return false;
            if (d.HasTag(ItemTags.Vegetable)) return true;
            return d.Category == ItemCategory.Crop && !d.HasTag(ItemTags.Fruit) && !d.HasTag(ItemTags.Grain) && !d.HasTag(ItemTags.Flower);
        }

        public static bool IsHerb(ItemDefinition d)
        {
            if (d.Category == ItemCategory.Seed || d.Category == ItemCategory.Furniture || d.Category == ItemCategory.Decor) return false;
            return d.HasTag(ItemTags.Fragrant) || (d.HasTag(ItemTags.Flower) && d.Category == ItemCategory.Crop);
        }

        public static bool IsOre(ItemDefinition d) => d.Category == ItemCategory.Ore || (d.HasTag(ItemTags.Ore) && d.Category != ItemCategory.Furniture);

        public static bool IsMetalOre(ItemDefinition d)
        {
            if (!IsOre(d)) return false;
            if (d.HasTag(ItemTags.Crystal) || d.HasTag(ItemTags.Gem)) return false;
            return true;
        }

        public static bool IsCrystal(ItemDefinition d) => d.Category == ItemCategory.Gem || d.HasTag(ItemTags.Crystal) || d.HasTag(ItemTags.Gem);

        public static bool IsPlantMatter(ItemDefinition d)
        {
            if (d.Category == ItemCategory.Seed) return false;
            if (d.Category != ItemCategory.Crop && !d.HasTag(ItemTags.Plant)) return false;
            return !d.HasTag(ItemTags.Fruit) && !d.HasTag(ItemTags.Grain);
        }

        public static bool IsColorful(ItemDefinition d)
        {
            if (d.Category == ItemCategory.Furniture || d.Category == ItemCategory.Decor || d.Category == ItemCategory.RocketPart) return false;
            if (d.HasTag(CookingTags.Dye)) return false;
            if (d.Category != ItemCategory.Crop && !d.HasTag(ItemTags.Flower) && !d.HasTag(ItemTags.Plant)) return false;
            return Concepts.ColorFromTags(d) != null;
        }

        /// <summary>料理として扱える物（盛り合わせ・詰め合わせ用）。</summary>
        public static bool IsDish(ItemDefinition d) => d.Category == ItemCategory.Food;

        public static IngredientSpec Fish(int n = 1) => new IngredientSpec { Predicate = IsFish, LabelJa = "魚（なんでも）", LabelEn = "Any fish", Count = n };
        public static IngredientSpec Fruit(int n = 1) => new IngredientSpec { Predicate = IsFruit, LabelJa = "果実（なんでも）", LabelEn = "Any fruit", Count = n };
        public static IngredientSpec Veg(int n = 1) => new IngredientSpec { Predicate = IsVegetable, LabelJa = "野菜（なんでも）", LabelEn = "Any vegetable", Count = n };
        public static IngredientSpec Grain(int n = 1) => new IngredientSpec { Predicate = IsGrain, LabelJa = "穀物（なんでも）", LabelEn = "Any grain", Count = n, SampleItemId = CookingIds.VillageGrain };
        public static IngredientSpec Herb(int n = 1) => new IngredientSpec { Predicate = IsHerb, LabelJa = "香り草・花", LabelEn = "Herb or flower", Count = n, SampleItemId = CookingIds.FragrantHerb };
        public static IngredientSpec MetalOre(int n = 1) => new IngredientSpec { Predicate = IsMetalOre, LabelJa = "金属の鉱石", LabelEn = "Metal ore", Count = n };
        public static IngredientSpec AnyOre(int n = 1) => new IngredientSpec { Predicate = IsOre, LabelJa = "鉱石（なんでも）", LabelEn = "Any ore", Count = n };
        public static IngredientSpec Crystal(int n = 1) => new IngredientSpec { Predicate = IsCrystal, LabelJa = "結晶・宝石", LabelEn = "Crystal or gem", Count = n };
        public static IngredientSpec Plant(int n = 1) => new IngredientSpec { Predicate = IsPlantMatter, LabelJa = "草・野菜・花", LabelEn = "Plants", Count = n };
        public static IngredientSpec Colorful(int n = 1) => new IngredientSpec { Predicate = IsColorful, LabelJa = "色のある植物", LabelEn = "Colorful plant", Count = n };
        public static IngredientSpec AnyDish(int n = 1) => new IngredientSpec { Predicate = IsDish, LabelJa = "料理（なんでも）", LabelEn = "Any dish", Count = n };

        /// <summary>料理の材料になりうる物か（実験の材料一覧の絞り込み）。</summary>
        public static bool IsFoodish(ItemDefinition d)
        {
            if (d == null) return false;
            switch (d.Category)
            {
                case ItemCategory.Fish:
                case ItemCategory.Crop:
                case ItemCategory.Food:
                case ItemCategory.Processed:
                    return true;
            }
            return d.HasTag(ItemTags.Edible) || d.HasTag(ItemTags.Ingredient) || d.HasTag(ItemTags.Food);
        }

        /// <summary>素材加工の材料になりうる物か。</summary>
        public static bool IsMaterialish(ItemDefinition d)
        {
            if (d == null) return false;
            switch (d.Category)
            {
                case ItemCategory.Ore:
                case ItemCategory.Gem:
                case ItemCategory.Material:
                case ItemCategory.Fish:
                case ItemCategory.Crop:
                    return true;
            }
            return d.HasTag(ItemTags.RocketMaterial) || d.HasTag(ItemTags.Plant) || d.HasTag(ItemTags.Flower);
        }
    }
}
