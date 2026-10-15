using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>料理・加工モジュールが登録するアイテムID（名前・数値はすべて【仮】）。</summary>
    public static class CookingIds
    {
        // ---- 基本の食材・調味料（料理人・雑貨屋が売る。序盤から料理できるように §2.1）----
        public const string StarSalt = "cook.ing.star_salt";
        public const string TreeNectar = "cook.ing.tree_nectar";
        public const string FragrantHerb = "cook.ing.fragrant_herb";
        public const string VillageGrain = "cook.ing.village_grain";
        public const string RoundBean = "cook.ing.round_bean";

        // ---- 中間素材・加工食品 ----
        public const string Flour = "cook.proc.flour";
        public const string Dough = "cook.proc.dough";
        public const string Juice = "cook.proc.juice";
        public const string ChilledJuice = "cook.proc.chilled_juice";
        public const string Jam = "cook.proc.jam";
        public const string LightRainJam = "cook.proc.lightrain_jam";
        public const string FruitCandy = "cook.proc.fruit_candy";
        public const string DriedFish = "cook.proc.dried_fish";
        public const string SmokedFish = "cook.proc.smoked_fish";
        public const string DriedVeg = "cook.proc.dried_veg";
        public const string SunDriedFruit = "cook.proc.sun_dried_fruit";
        public const string Pickles = "cook.proc.pickles";
        public const string FermentSauce = "cook.proc.ferment_sauce";
        public const string StarFermentJuice = "cook.proc.star_ferment_juice";
        public const string FishPaste = "cook.proc.fish_paste";
        public const string FoamCream = "cook.proc.foam_cream";
        public const string VegPuree = "cook.proc.veg_puree";
        public const string HerbSalt = "cook.proc.herb_salt";
        public const string StarWindHerbSalt = "cook.proc.starwind_herb_salt";
        public const string HoneyCandy = "cook.proc.honey_candy";
        public const string ReverseRainDrops = "cook.proc.reverse_rain_drops";

        // ---- 料理 ----
        public const string GrilledFish = "cook.dish.grilled_fish";
        public const string Bread = "cook.dish.bread";

        // ---- 意図と違う結果・謎料理（§5.5）----
        public const string MysteryStew = "cook.mystery.stew";
        public const string MysteryGrill = "cook.mystery.grill";
        public const string MysteryFoam = "cook.mystery.foam";
        public const string MysteryDrink = "cook.mystery.drink";
        public const string MysteryPaste = "cook.mystery.paste";
        public const string MysteryChilled = "cook.mystery.chilled";
        public const string MysteryGlowing = "cook.mystery.glowing";
        public const string MysteryPickle = "cook.mystery.pickle";
        public const string MysteryDried = "cook.mystery.dried";
        public const string MysteryMix = "cook.mystery.mix";
        public const string MysteryLump = "cook.mystery.lump";
        public const string MysteryMetal = "cook.mystery.metal_scrap";

        // ---- 素材（§9）----
        public const string MetalIngot = "cook.mat.metal_ingot";
        public const string PolishedCrystal = "cook.mat.polished_crystal";
        public const string StarGlass = "cook.mat.star_glass";
        public const string MineralPowder = "cook.mat.mineral_powder";
        public const string PlantFiber = "cook.mat.plant_fiber";
        public const string Cloth = "cook.mat.cloth";
        public const string FiberBoard = "cook.mat.fiber_board";
        public const string StoneTile = "cook.mat.stone_tile";
        public const string DyePrefix = "cook.mat.dye_";

        // ---- 調理器具（家具）----
        public const string DevHearth = "cook.dev.hearth";
        public const string DevWaterStand = "cook.dev.water_stand";
        public const string DevGrindTable = "cook.dev.grind_table";
        public const string DevBubbleBowl = "cook.dev.bubble_bowl";
        public const string DevPress = "cook.dev.press";
        public const string DevFermentBarrel = "cook.dev.ferment_barrel";
        public const string DevDryRack = "cook.dev.dry_rack";
        public const string DevSmoker = "cook.dev.smoker";
        public const string DevCoolBox = "cook.dev.cool_box";
        public const string DevStackTable = "cook.dev.stack_table";
        public const string DevCarryShelf = "cook.dev.carry_shelf";
        public const string DevPerch = "cook.dev.kitchen_perch";
        public const string DevRefiner = "cook.dev.refiner";
        public const string DevWorkbench = "cook.dev.workbench";
    }

    /// <summary>アイテム・器具・レシピの登録（RegisterContent から1回だけ呼ぶ）。</summary>
    public static class CookingContent
    {
        private static readonly Dictionary<string, DeviceDef> devices = new Dictionary<string, DeviceDef>();
        public static IEnumerable<DeviceDef> Devices => devices.Values;

        public static DeviceDef Device(string itemId)
        {
            if (itemId != null && devices.TryGetValue(itemId, out var d)) return d;
            return null;
        }

        private static readonly CraftStationType[] Hand = { CraftStationType.Hand, CraftStationType.CommunityKitchen, CraftStationType.Kitchen, CraftStationType.CreatureDevice };
        private static readonly CraftStationType[] Food = { CraftStationType.CommunityKitchen, CraftStationType.Kitchen, CraftStationType.CreatureDevice };
        private static readonly CraftStationType[] HomeOnly = { CraftStationType.Kitchen, CraftStationType.CreatureDevice };
        private static readonly CraftStationType[] Timed = { CraftStationType.CommunityKitchen, CraftStationType.Kitchen };
        private static readonly CraftStationType[] HomeTimed = { CraftStationType.Kitchen };
        private static readonly CraftStationType[] Bench = { CraftStationType.Workbench, CraftStationType.CarpenterBench };

        public static void RegisterAll()
        {
            RegisterIngredients();
            RegisterProcessed();
            RegisterDishes();
            RegisterMystery();
            RegisterMaterials();
            RegisterRocketParts();
            RegisterDevices();
            RegisterRecipes();
            RecipeBook.Validate();
        }

        // =================================================================
        // アイテム登録ヘルパー
        // =================================================================

        private static ItemDefinition Reg(string id, string ja, string en, string dja, string den, ItemCategory cat, int price, Color color,
            PrimitiveType shape, string generic, params string[] tags)
        {
            var d = new ItemDefinition
            {
                Id = id,
                NameJa = ja,
                NameEn = en,
                DescriptionJa = dja,
                DescriptionEn = den,
                Category = cat,
                BasePrice = price,
                Color = color,
                Shape = shape,
                GenericConceptId = generic,
                SourceActivity = "cooking"
            };
            d.WithTags(tags);
            if (cat == ItemCategory.Food || cat == ItemCategory.Processed)
            {
                if (!d.HasTag(ItemTags.Edible) && !d.HasTag(ItemTags.Fuel)) d.Tags.Add(ItemTags.Edible);
                d.Consumable = true;
            }
            if (cat == ItemCategory.Furniture) { d.Durable = true; d.MaxStack = 5; }
            return ItemRegistry.Register(d);
        }

        private static ItemDefinition Dish(string id, string ja, string en, string dja, string den, int price, Color c, PrimitiveType shape, params string[] tags)
        {
            var d = Reg(id, ja, en, dja, den, ItemCategory.Food, price, c, shape, Concepts.DISH, tags);
            d.WithTags(ItemTags.Food, ItemTags.Edible, CookingTags.Dish);
            return d;
        }

        private static ItemDefinition Proc(string id, string ja, string en, string dja, string den, int price, Color c, PrimitiveType shape, params string[] tags)
        {
            var d = Reg(id, ja, en, dja, den, ItemCategory.Processed, price, c, shape, Concepts.FOOD, tags);
            d.WithTags(ItemTags.Ingredient, CookingTags.Processed);
            return d;
        }

        private static ItemDefinition Drink(string id, string ja, string en, string dja, string den, int price, Color c, params string[] tags)
        {
            var d = Reg(id, ja, en, dja, den, ItemCategory.Processed, price, c, PrimitiveType.Cylinder, Concepts.DRINK_N, tags);
            d.WithTags(ItemTags.Drink, ItemTags.Ingredient, CookingTags.Processed);
            return d;
        }

        // =================================================================
        // 基本の食材（§2.1：生物がいなくても共用調理場で料理を始められるよう、料理人・雑貨屋が売る）
        // =================================================================

        private static void RegisterIngredients()
        {
            Reg(CookingIds.StarSalt, "星の塩", "Star salt", "きらきらした粒の塩。料理の味を引きしめる。", "Glittering salt crystals.", ItemCategory.Processed, 12,
                new Color(0.95f, 0.95f, 1f), PrimitiveType.Sphere, Concepts.FOOD, ItemTags.Ingredient, ItemTags.Savory, CookingTags.Seasoning, ItemTags.ColorWhite);
            Reg(CookingIds.TreeNectar, "甘い樹蜜", "Tree nectar", "木からとれるとろりと甘い蜜。", "Thick, sweet tree nectar.", ItemCategory.Processed, 30,
                new Color(1f, 0.8f, 0.3f), PrimitiveType.Cylinder, Concepts.FOOD, ItemTags.Ingredient, ItemTags.Sweet, CookingTags.Seasoning, ItemTags.ColorYellow);
            Reg(CookingIds.FragrantHerb, "香り草", "Fragrant herb", "すっとする香りの葉。", "A leaf with a fresh scent.", ItemCategory.Crop, 18,
                new Color(0.45f, 0.85f, 0.45f), PrimitiveType.Capsule, Concepts.VEGETABLE, ItemTags.Ingredient, ItemTags.Fragrant, ItemTags.Plant, ItemTags.ColorGreen, ItemTags.Edible);
            Reg(CookingIds.VillageGrain, "村の麦粒", "Village grain", "村でよく食べられている丸い麦の粒。", "Round grains eaten around the village.", ItemCategory.Crop, 15,
                new Color(0.95f, 0.85f, 0.55f), PrimitiveType.Sphere, Concepts.CROP, ItemTags.Ingredient, ItemTags.Grain, ItemTags.Edible);
            Reg(CookingIds.RoundBean, "ころころ豆", "Roly beans", "転がりやすい丸い豆。煮ても発酵させてもおいしい。", "Round beans that roll everywhere.", ItemCategory.Crop, 16,
                new Color(0.7f, 0.55f, 0.35f), PrimitiveType.Sphere, Concepts.CROP, ItemTags.Ingredient, ItemTags.Edible, ItemTags.Savory);
        }

        // =================================================================
        // 加工食品（§5.3：加工の連鎖。ジュース・ジャム・干物・燻製・発酵品）
        // =================================================================

        private static void RegisterProcessed()
        {
            Proc(CookingIds.Flour, "星の粉", "Star flour", "麦粒を挽いたさらさらの粉。パンや菓子のもと。", "Finely ground grain.", 30, new Color(1f, 0.97f, 0.9f), PrimitiveType.Sphere, ItemTags.ColorWhite);
            Proc(CookingIds.Dough, "こね生地", "Dough", "粉に水を加えてこねた生地。", "Flour kneaded with water.", 45, new Color(0.98f, 0.9f, 0.75f), PrimitiveType.Sphere, CookingTags.Chewy);
            Drink(CookingIds.Juice, "果実ジュース", "Fruit juice", "果実をしぼった甘いジュース。", "Freshly pressed fruit juice.", 70, new Color(1f, 0.55f, 0.45f), ItemTags.Sweet, ItemTags.Gift);
            Drink(CookingIds.ChilledJuice, "ひんやりジュース", "Chilled juice", "よく冷えたジュース。のどが喜ぶ。", "Ice-cold juice.", 90, new Color(1f, 0.65f, 0.6f), ItemTags.Sweet, CookingTags.Cold);
            Proc(CookingIds.Jam, "果実ジャム", "Fruit jam", "果実をつぶして煮詰めたジャム。パンや菓子の材料にも。", "Crushed and simmered fruit.", 85, new Color(0.85f, 0.25f, 0.35f), PrimitiveType.Cylinder, ItemTags.Sweet, ItemTags.Gift);
            Proc(CookingIds.LightRainJam, "光雨ジャム", "Light-rain jam", "光雨の日に煮たジャム。瓶の中で光の粒が揺れている。", "Jam made under the light-rain; it sparkles.", 220, new Color(1f, 0.6f, 0.85f), PrimitiveType.Cylinder, ItemTags.Sweet, ItemTags.Glowing, ItemTags.Rare, ItemTags.Gift);
            Proc(CookingIds.FruitCandy, "果実キャンディ", "Fruit candy", "ジャムのつもりが煮詰めすぎてできた、カリカリの飴。", "Over-simmered jam turned into crunchy candy.", 75, new Color(0.95f, 0.35f, 0.4f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Crispy, CookingTags.Accidental);
            Proc(CookingIds.DriedFish, "干し魚", "Dried fish", "からりと干した魚。うまみがぎゅっと詰まっている。", "Sun-dried fish packed with flavor.", 95, new Color(0.75f, 0.6f, 0.45f), PrimitiveType.Capsule, ItemTags.Savory, CookingTags.Dried, CookingTags.Chewy);
            Proc(CookingIds.SmokedFish, "もくもく燻製魚", "Smoky fish", "燻して香りをまとわせた魚。", "Fish infused with smoky aroma.", 130, new Color(0.6f, 0.4f, 0.3f), PrimitiveType.Capsule, ItemTags.Savory, ItemTags.Fragrant, CookingTags.Smoked);
            Proc(CookingIds.DriedVeg, "乾燥野菜", "Dried vegetables", "干して甘みの増した野菜。", "Dried, sweeter vegetables.", 60, new Color(0.7f, 0.75f, 0.4f), PrimitiveType.Cube, CookingTags.Dried, CookingTags.Crispy);
            Proc(CookingIds.SunDriedFruit, "ほし果実", "Dried fruit", "日なたで干した果実。ねっとり甘い。", "Sun-dried fruit, chewy and sweet.", 80, new Color(0.85f, 0.5f, 0.35f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Dried, CookingTags.Chewy);
            Proc(CookingIds.Pickles, "星の漬け物", "Star pickles", "塩と時間で発酵させた野菜。すっぱくて香ばしい。", "Salted, fermented vegetables.", 75, new Color(0.6f, 0.8f, 0.35f), PrimitiveType.Cube, ItemTags.Sour, CookingTags.Fermented);
            Proc(CookingIds.FermentSauce, "発酵だれ", "Fermented sauce", "豆と塩を発酵させた調味料。煮物の味が深くなる。", "A savory seasoning of fermented beans.", 70, new Color(0.45f, 0.3f, 0.2f), PrimitiveType.Cylinder, ItemTags.Savory, CookingTags.Fermented, CookingTags.Seasoning);
            Drink(CookingIds.StarFermentJuice, "星の発酵ジュース", "Star fizz", "発酵させたジュース。泡がしゅわしゅわ光る。", "Fermented juice with glittering fizz.", 160, new Color(0.9f, 0.6f, 1f), ItemTags.Sweet, ItemTags.Sour, CookingTags.Fermented, ItemTags.Glowing, ItemTags.Gift);
            Proc(CookingIds.FishPaste, "すり身", "Fish paste", "魚を細かく砕いたもの。形を作って焼くとおいしい。", "Finely ground fish.", 55, new Color(0.95f, 0.85f, 0.85f), PrimitiveType.Sphere, ItemTags.Savory, CookingTags.Smooth);
            Proc(CookingIds.FoamCream, "あわあわクリーム", "Foam cream", "蜜を泡立てたふわふわのクリーム。", "Whipped nectar cream.", 70, new Color(1f, 0.98f, 0.9f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Fluffy);
            Proc(CookingIds.VegPuree, "野菜ピューレ", "Vegetable purée", "煮た野菜をなめらかにつぶしたもの。", "Smoothly mashed cooked vegetables.", 50, new Color(0.8f, 0.85f, 0.4f), PrimitiveType.Cylinder, CookingTags.Smooth);
            Proc(CookingIds.HerbSalt, "香り塩", "Herb salt", "香り草を砕いて混ぜた塩。", "Salt mixed with ground herbs.", 45, new Color(0.8f, 0.95f, 0.8f), PrimitiveType.Sphere, ItemTags.Savory, ItemTags.Fragrant, CookingTags.Seasoning);
            Proc(CookingIds.StarWindHerbSalt, "星風ハーブ塩", "Star-wind herb salt", "星風の日に作った香り塩。ひとつまみで空の香りがする。", "Herb salt made in the star wind.", 140, new Color(0.75f, 1f, 0.95f), PrimitiveType.Sphere, ItemTags.Savory, ItemTags.Fragrant, ItemTags.Rare, CookingTags.Seasoning, ItemTags.Glowing);
            Proc(CookingIds.HoneyCandy, "蜜あめ", "Nectar drops", "蜜を煮て冷やし固めた飴。", "Boiled and chilled nectar candy.", 55, new Color(1f, 0.75f, 0.3f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Crispy);
            Proc(CookingIds.ReverseRainDrops, "逆雨しずく飴", "Upward-drop candy", "逆雨の日に作った飴。手を離すとほんの少し浮き上がる。", "Candy made during reverse rain; it floats slightly.", 180, new Color(0.55f, 0.85f, 1f), PrimitiveType.Sphere, ItemTags.Sweet, ItemTags.Rare, ItemTags.Gift, ItemTags.Cute);
        }

        // =================================================================
        // 料理（約40品。§5.2 同じ魚から違う商品、§5.4 特殊料理）
        // =================================================================

        private static void RegisterDishes()
        {
            var fishC = new Color(0.85f, 0.6f, 0.45f);
            Dish(CookingIds.GrilledFish, "焼き魚", "Grilled fish", "魚をこんがり焼いた定番。", "A classic: fish grilled golden.", 80, fishC, PrimitiveType.Capsule, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.charred_fish", "こんがり焦げ魚", "Charred fish", "焼きすぎて真っ黒。でも固く香ばしいのが好きな人もいるかも。", "Overgrilled to black - some like it crunchy.", 45, new Color(0.3f, 0.22f, 0.18f), PrimitiveType.Capsule, ItemTags.Savory, CookingTags.Crispy, CookingTags.Accidental);
            Dish("cook.dish.salt_grilled_fish", "塩焼き魚", "Salt-grilled fish", "星の塩をふって焼いた魚。", "Fish grilled with star salt.", 110, new Color(0.9f, 0.7f, 0.55f), PrimitiveType.Capsule, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.steamed_fish", "ぷるぷる蒸し魚", "Jiggly steamed fish", "水と熱でやさしく蒸した、ぷるぷるの魚。", "Gently steamed, jiggly fish.", 120, new Color(0.95f, 0.85f, 0.8f), PrimitiveType.Capsule, CookingTags.Jiggly, CookingTags.Hot);
            Dish("cook.dish.fish_veg_soup", "魚と野菜のスープ", "Fish & veggie soup", "魚と野菜を煮込んだあたたかいスープ。", "A warm soup of fish and vegetables.", 160, new Color(0.95f, 0.6f, 0.45f), PrimitiveType.Cylinder, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.herb_fish", "香草包み焼き", "Herb-wrapped fish", "香り草で包んで焼いた魚。", "Fish wrapped in herbs and baked.", 150, new Color(0.6f, 0.8f, 0.5f), PrimitiveType.Capsule, ItemTags.Fragrant, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.surimi_grill", "すり身焼き", "Fish cake", "すり身を丸めて焼いた、ふっくら食感。", "Ground fish shaped and grilled.", 130, new Color(1f, 0.85f, 0.75f), PrimitiveType.Cylinder, CookingTags.Chewy, ItemTags.Savory);
            Dish("cook.dish.simmered_fish", "魚の煮付け", "Simmered fish", "発酵だれで煮た魚。味がしみている。", "Fish simmered in fermented sauce.", 190, new Color(0.6f, 0.4f, 0.3f), PrimitiveType.Capsule, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.ferment_hotpot", "発酵鍋", "Fermented hotpot", "発酵だれ・魚・野菜をぐつぐつ煮込んだ鍋。", "A bubbling hotpot of fish and vegetables.", 260, new Color(0.7f, 0.45f, 0.35f), PrimitiveType.Cylinder, ItemTags.Savory, CookingTags.Hot, CookingTags.Fermented, ItemTags.Big);
            Dish("cook.dish.seared_dried_fish", "あぶり干し魚", "Seared dried fish", "干し魚をさっとあぶった、カリカリのおつまみ。", "Lightly seared crispy dried fish.", 120, new Color(0.8f, 0.55f, 0.35f), PrimitiveType.Capsule, CookingTags.Crispy, ItemTags.Savory);
            Dish("cook.dish.fish_veg_grill", "魚と野菜の焼き合わせ", "Fish & veggie grill", "魚と野菜を一緒に焼いたひと皿。", "Fish and vegetables grilled together.", 140, new Color(0.8f, 0.7f, 0.45f), PrimitiveType.Cube, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.grain_porridge", "麦がゆ", "Grain porridge", "麦粒を水で煮たやさしいおかゆ。", "Grains simmered soft in water.", 50, new Color(0.98f, 0.95f, 0.85f), PrimitiveType.Cylinder, CookingTags.Smooth, CookingTags.Hot);
            Dish("cook.dish.flatbread", "麦の薄焼き", "Flatbread", "麦粒をつぶして薄くのばして焼いたもの。", "Crushed grain, flattened and baked.", 55, new Color(0.95f, 0.8f, 0.55f), PrimitiveType.Cylinder, CookingTags.Crispy);
            Dish("cook.dish.grilled_veg", "焼き野菜", "Grilled vegetables", "野菜を焼いて甘みを引き出した。", "Grilled to bring out the sweetness.", 60, new Color(0.6f, 0.75f, 0.35f), PrimitiveType.Cube, CookingTags.Hot);
            Dish("cook.dish.veg_stirfry", "野菜炒め", "Veggie stir-fry", "ふたつの野菜をさっと炒めた。", "Two vegetables tossed over heat.", 95, new Color(0.55f, 0.8f, 0.4f), PrimitiveType.Cube, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.bean_stew", "ころころ豆の煮込み", "Bean stew", "豆を塩で煮込んだほくほくの料理。", "Beans stewed soft with salt.", 85, new Color(0.65f, 0.5f, 0.35f), PrimitiveType.Cylinder, ItemTags.Savory, CookingTags.Hot);
            Dish("cook.dish.herb_tea", "香り草のお茶", "Herb tea", "香り草を煮出したお茶。ほっとする。", "A calming brew of herbs.", 50, new Color(0.7f, 0.9f, 0.6f), PrimitiveType.Cylinder, ItemTags.Drink, ItemTags.Fragrant, CookingTags.Hot);
            Dish("cook.dish.candied_fruit", "果実の蜜煮", "Candied fruit", "果実を蜜と水でことこと煮た甘い一品。", "Fruit simmered in nectar.", 110, new Color(1f, 0.6f, 0.4f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Hot);
            Dish("cook.dish.star_salad", "星屑サラダ", "Stardust salad", "野菜と果実を和えたさっぱりサラダ。", "Vegetables and fruit tossed together.", 85, new Color(0.55f, 0.9f, 0.55f), PrimitiveType.Cylinder, ItemTags.Sour, CookingTags.Cold);
            Dish("cook.dish.fruit_platter", "果物の盛り合わせ", "Fruit platter", "果実を2つ並べた色あざやかなひと皿。", "Two fruits arranged colorfully.", 90, new Color(1f, 0.5f, 0.5f), PrimitiveType.Cylinder, ItemTags.Sweet, ItemTags.Gift);
            Dish("cook.dish.whole_veg_soup", "まるごと野菜スープ", "Hearty veggie soup", "野菜をまるごと煮てかき混ぜた濃厚スープ。", "A thick soup of whole vegetables.", 150, new Color(0.85f, 0.7f, 0.35f), PrimitiveType.Cylinder, CookingTags.Hot, CookingTags.Smooth, ItemTags.Big);
            Dish("cook.dish.potage", "野菜ポタージュ", "Vegetable potage", "ピューレをのばして温めたなめらかなスープ。", "A velvety soup from purée.", 120, new Color(0.95f, 0.85f, 0.45f), PrimitiveType.Cylinder, CookingTags.Smooth, CookingTags.Hot);
            Dish("cook.dish.veg_roll", "ぐるぐる野菜ロール", "Swirl veggie roll", "生地で野菜を巻いて焼いた、渦巻き模様の一品。", "Vegetables rolled in dough - swirly.", 170, new Color(0.9f, 0.75f, 0.5f), PrimitiveType.Cylinder, CookingTags.Chewy, ItemTags.Cute);
            Dish(CookingIds.Bread, "ふつうのパン", "Plain bread", "生地を焼いたパン。もちもち。", "Chewy baked bread.", 90, new Color(0.9f, 0.7f, 0.4f), PrimitiveType.Capsule, CookingTags.Chewy);
            Dish("cook.dish.fluffy_star_bread", "ふわふわ星パン", "Fluffy star bread", "泡立てて焼いた、空気をたっぷり含んだ軽いパン。", "Airy bread made with whipped dough.", 200, new Color(1f, 0.9f, 0.6f), PrimitiveType.Sphere, CookingTags.Fluffy, ItemTags.Cute, ItemTags.Gift);
            Dish("cook.dish.pancake", "ふわふわパンケーキ", "Fluffy pancake", "粉と蜜を泡立てて焼いた。", "Whipped flour and nectar, griddled.", 150, new Color(1f, 0.85f, 0.55f), PrimitiveType.Cylinder, ItemTags.Sweet, CookingTags.Fluffy);
            Dish("cook.dish.jam_bread", "ジャムパン", "Jam bread", "生地でジャムを包んで焼いたパン。", "Bread filled with jam.", 180, new Color(0.95f, 0.6f, 0.45f), PrimitiveType.Capsule, ItemTags.Sweet, CookingTags.Chewy);
            Dish("cook.dish.honey_toast", "蜜トースト", "Nectar toast", "パンに蜜をかけて焼いた。", "Toasted bread with nectar.", 140, new Color(1f, 0.75f, 0.35f), PrimitiveType.Cube, ItemTags.Sweet, CookingTags.Crispy);
            Dish("cook.dish.fish_sandwich", "焼き魚サンド", "Fish sandwich", "焼き魚をパンではさんだ。持ち歩きにぴったり。", "Grilled fish in bread.", 200, new Color(0.9f, 0.75f, 0.5f), PrimitiveType.Cube, ItemTags.Savory);
            Dish("cook.dish.star_dumpling", "星形だんご", "Star dumplings", "粉をこねて星の形にして焼いた。", "Star-shaped baked dumplings.", 100, new Color(1f, 0.95f, 0.7f), PrimitiveType.Sphere, CookingTags.Chewy, ItemTags.Cute);
            Dish("cook.dish.rainbow_cake", "にじいろ重ねケーキ", "Rainbow layer cake", "泡立てて焼いた生地と果実を何層も重ねたケーキ。", "Layers of fluffy cake and fruit.", 420, new Color(1f, 0.6f, 0.85f), PrimitiveType.Cylinder, ItemTags.Sweet, CookingTags.Fluffy, ItemTags.Gift, ItemTags.Rare, ItemTags.Big);
            Dish("cook.dish.cloud_pudding", "ぷるぷる雲プリン", "Cloud pudding", "泡立てて蒸して冷やした、ぷるんと揺れる菓子。", "Whipped, steamed and chilled pudding.", 210, new Color(1f, 0.95f, 0.8f), PrimitiveType.Cylinder, ItemTags.Sweet, CookingTags.Jiggly, CookingTags.Cold, ItemTags.Cute);
            Dish("cook.dish.fruit_jelly", "キラキラ果実ゼリー", "Sparkle fruit jelly", "しぼった果汁を冷やして光らせた、光るデザート。", "A glowing dessert of chilled juice.", 260, new Color(1f, 0.5f, 0.75f), PrimitiveType.Cube, ItemTags.Sweet, CookingTags.Jiggly, CookingTags.Cold, ItemTags.Glowing, ItemTags.Gift);
            Dish("cook.dish.sherbet", "ひんやり果実シャーベット", "Fruit sherbet", "果実と蜜を砕いて冷やした。", "Crushed fruit and nectar, frozen.", 150, new Color(1f, 0.7f, 0.8f), PrimitiveType.Sphere, ItemTags.Sweet, CookingTags.Cold);
            Dish("cook.dish.foam_drink", "あわあわドリンク", "Foam drink", "ジュースを泡立てた、ふわふわの飲み物。", "Juice whipped into a fluffy drink.", 110, new Color(1f, 0.75f, 0.75f), PrimitiveType.Cylinder, ItemTags.Drink, ItemTags.Sweet, CookingTags.Fluffy);
            Dish("cook.dish.cream_fruit", "泡クリームの果実盛り", "Fruit with foam", "果実にあわあわクリームを積み上げた。", "Fruit topped with a tower of foam.", 180, new Color(1f, 0.85f, 0.85f), PrimitiveType.Cylinder, ItemTags.Sweet, CookingTags.Fluffy, ItemTags.Cute);
            Dish("cook.dish.glow_soup", "光るスープ", "Glowing soup", "煮込んだスープに光を宿した。夜の食卓が明るくなる。", "A soup that glows softly.", 230, new Color(0.9f, 1f, 0.6f), PrimitiveType.Cylinder, ItemTags.Glowing, CookingTags.Hot);
            Dish("cook.dish.smoked_platter", "燻製の盛り合わせ", "Smoked platter", "燻製魚と乾燥野菜を積み上げた盛り合わせ。", "Smoked fish and dried vegetables, stacked.", 280, new Color(0.65f, 0.45f, 0.35f), PrimitiveType.Cube, ItemTags.Savory, CookingTags.Smoked);
            Dish("cook.dish.feast_platter", "ごちそう盛り", "Feast platter", "料理をふたつ積み上げた、お祝い向けの大皿。", "Two dishes piled into a feast.", 300, new Color(1f, 0.8f, 0.5f), PrimitiveType.Cylinder, ItemTags.Big, ItemTags.Gift);
            Dish("cook.dish.gift_assort", "料理の詰め合わせ", "Dish assortment", "料理をふたつ箱に詰めた贈り物。", "Two dishes boxed as a gift.", 320, new Color(1f, 0.65f, 0.7f), PrimitiveType.Cube, ItemTags.Gift, ItemTags.Cute);
            Dish("cook.dish.pickle_porridge", "漬け物そえ麦がゆ", "Porridge with pickles", "麦がゆに星の漬け物を添えた朝ごはん。", "Porridge served with pickles.", 120, new Color(0.9f, 0.9f, 0.7f), PrimitiveType.Cylinder, ItemTags.Sour, CookingTags.Hot);
        }

        // =================================================================
        // 謎料理（§5.5：失敗作も無価値なゴミにせず、発見の一つにする）
        // =================================================================

        private static void RegisterMystery()
        {
            var t = new[] { CookingTags.Mystery, ItemTags.Food, ItemTags.Edible };
            Reg(CookingIds.MysteryStew, "なぞの煮込み", "Mystery stew", "何かを煮込んだら、何かになった。", "Something stewed into... something.", ItemCategory.Food, 30, new Color(0.6f, 0.55f, 0.4f), PrimitiveType.Cylinder, Concepts.DISH, t);
            Reg(CookingIds.MysteryGrill, "なぞの焼き物", "Mystery grill", "焼いたらこうなった。香ばしい…かも。", "Grilled into a puzzling shape.", ItemCategory.Food, 25, new Color(0.55f, 0.4f, 0.3f), PrimitiveType.Cube, Concepts.DISH, t);
            Reg(CookingIds.MysteryFoam, "なぞのふわふわ", "Mystery fluff", "泡立てたら正体不明のふわふわになった。", "An unidentified fluffy thing.", ItemCategory.Food, 25, new Color(0.95f, 0.95f, 0.95f), PrimitiveType.Sphere, Concepts.DISH, t);
            Reg(CookingIds.MysteryDrink, "なぞのしぼり汁", "Mystery juice", "しぼったら出てきた、ふしぎな色の汁。", "A strangely colored liquid.", ItemCategory.Food, 20, new Color(0.6f, 0.8f, 0.6f), PrimitiveType.Cylinder, Concepts.DRINK_N, t);
            Reg(CookingIds.MysteryPaste, "なぞのペースト", "Mystery paste", "砕いたら、ねっとりした何かに。", "A sticky ground-up something.", ItemCategory.Food, 20, new Color(0.7f, 0.65f, 0.55f), PrimitiveType.Sphere, Concepts.DISH, t);
            Reg(CookingIds.MysteryChilled, "なぞのひんやり", "Mystery chill", "冷やしたら固まった、何か。", "Something that froze solid.", ItemCategory.Food, 25, new Color(0.7f, 0.85f, 0.95f), PrimitiveType.Cube, Concepts.DISH, t);
            Reg(CookingIds.MysteryGlowing, "なぞの光る料理", "Mystery glow dish", "ぼんやり光っている。食べられる…はず。", "It glows faintly. Probably edible.", ItemCategory.Food, 45, new Color(0.85f, 1f, 0.6f), PrimitiveType.Sphere, Concepts.DISH, CookingTags.Mystery, ItemTags.Food, ItemTags.Edible, ItemTags.Glowing);
            Reg(CookingIds.MysteryPickle, "なぞの漬け物", "Mystery pickle", "すっぱい匂いがする。発酵した何か。", "Something fermented and sour.", ItemCategory.Food, 25, new Color(0.6f, 0.7f, 0.4f), PrimitiveType.Cube, Concepts.DISH, CookingTags.Mystery, ItemTags.Food, ItemTags.Edible, ItemTags.Sour);
            Reg(CookingIds.MysteryDried, "なぞのからから", "Mystery crisp", "干したらからからになった。", "Dried into a crisp something.", ItemCategory.Food, 20, new Color(0.8f, 0.7f, 0.5f), PrimitiveType.Cube, Concepts.DISH, CookingTags.Mystery, ItemTags.Food, ItemTags.Edible, CookingTags.Crispy);
            Reg(CookingIds.MysteryMix, "なぞの和え物", "Mystery mix", "混ぜてみた。見た目はにぎやか。", "A colorful jumble.", ItemCategory.Food, 20, new Color(0.75f, 0.8f, 0.6f), PrimitiveType.Cylinder, Concepts.DISH, t);
            Reg(CookingIds.MysteryLump, "ふしぎな塊", "Strange lump", "食べ物ではなさそうな塊。何かの素材になる…かもしれない。", "Probably not food. Maybe a material?", ItemCategory.Material, 8, new Color(0.5f, 0.5f, 0.5f), PrimitiveType.Sphere, Concepts.THING, CookingTags.Mystery);
            Reg(CookingIds.MysteryMetal, "ふしぎな金属片", "Odd metal scrap", "溶けて固まった金属のかけら。", "A melted and hardened metal chip.", ItemCategory.Material, 15, new Color(0.6f, 0.6f, 0.65f), PrimitiveType.Cube, Concepts.METAL, CookingTags.Mystery, ItemTags.Metal);
        }

        // =================================================================
        // 非食品の素材（§9）
        // =================================================================

        private static void RegisterMaterials()
        {
            Reg(CookingIds.MetalIngot, "金属インゴット", "Metal ingot", "鉱石を熱して取り出した金属。家具や道具の素材。", "Metal smelted from ore.", ItemCategory.Material, 90, new Color(0.7f, 0.72f, 0.78f), PrimitiveType.Cube, Concepts.METAL, ItemTags.Metal);
            Reg(CookingIds.PolishedCrystal, "磨いた結晶", "Polished crystal", "丁寧に磨いた結晶。ランプや展示に。", "A carefully polished crystal.", ItemCategory.Material, 160, new Color(0.7f, 0.9f, 1f), PrimitiveType.Sphere, Concepts.GEM, ItemTags.Crystal, ItemTags.Shiny, ItemTags.Gift);
            Reg(CookingIds.StarGlass, "星ガラス", "Star glass", "鉱石を砕いて熱して冷やした、透きとおった板。", "Clear glass from ground, heated ore.", ItemCategory.Material, 120, new Color(0.8f, 0.95f, 1f, 1f), PrimitiveType.Cube, Concepts.THING, ItemTags.Shiny);
            Reg(CookingIds.MineralPowder, "鉱石粉", "Mineral powder", "鉱石を砕いた粉。上薬や磨き粉に。", "Ground ore powder.", ItemCategory.Material, 35, new Color(0.65f, 0.62f, 0.6f), PrimitiveType.Sphere, Concepts.ORE, ItemTags.Ore);
            Reg(CookingIds.PlantFiber, "植物繊維", "Plant fiber", "草や野菜をしぼって砕いた丈夫な繊維。", "Tough fiber from pressed plants.", ItemCategory.Material, 30, new Color(0.7f, 0.8f, 0.5f), PrimitiveType.Capsule, Concepts.THING, CookingTags.Fiber, ItemTags.Plant);
            Reg(CookingIds.Cloth, "ふわ布", "Soft cloth", "繊維を紡いで重ね織りした布。カーテンや家具に。", "Woven cloth for curtains and furniture.", ItemCategory.Material, 85, new Color(0.95f, 0.9f, 0.8f), PrimitiveType.Cube, Concepts.CLOTHES, CookingTags.Fiber, CookingTags.Fluffy);
            Reg(CookingIds.FiberBoard, "繊維ボード", "Fiber board", "繊維を圧して熱で固めた板。この星の家具の定番素材。", "Pressed and heat-set fiber board.", ItemCategory.Material, 70, new Color(0.75f, 0.6f, 0.4f), PrimitiveType.Cube, Concepts.THING, CookingTags.Fiber);
            Reg(CookingIds.StoneTile, "加工石", "Stone tile", "鉱石を形作って磨いた石材。床や庭に。", "Shaped and polished stone.", ItemCategory.Material, 60, new Color(0.6f, 0.6f, 0.62f), PrimitiveType.Cube, Concepts.STONE);
            RegDye("red", "赤の染料", "Red dye", new Color(0.9f, 0.25f, 0.25f), ItemTags.ColorRed);
            RegDye("blue", "青の染料", "Blue dye", new Color(0.25f, 0.45f, 0.95f), ItemTags.ColorBlue);
            RegDye("yellow", "黄の染料", "Yellow dye", new Color(1f, 0.9f, 0.25f), ItemTags.ColorYellow);
            RegDye("green", "緑の染料", "Green dye", new Color(0.3f, 0.8f, 0.35f), ItemTags.ColorGreen);
            RegDye("purple", "紫の染料", "Purple dye", new Color(0.65f, 0.35f, 0.9f), ItemTags.ColorPurple);
            RegDye("pink", "桃色の染料", "Pink dye", new Color(1f, 0.55f, 0.75f), ItemTags.ColorPink);
            RegDye("orange", "橙の染料", "Orange dye", new Color(1f, 0.6f, 0.25f), ItemTags.ColorOrange);
            RegDye("white", "白の染料", "White dye", new Color(0.97f, 0.97f, 0.97f), ItemTags.ColorWhite);
            RegDye("black", "黒の染料", "Black dye", new Color(0.15f, 0.15f, 0.18f), ItemTags.ColorBlack);
        }

        private static void RegDye(string key, string ja, string en, Color c, string colorTag)
        {
            Reg(CookingIds.DyePrefix + key, ja, en, "色のある植物から取り出した染料。家具や壁紙の色を変えられる。", "Dye extracted from colorful plants.", ItemCategory.Material, 50, c,
                PrimitiveType.Cylinder, Concepts.THING, CookingTags.Dye, colorTag);
        }

        /// <summary>材料の色タグから染料のIDを決める（同じ作り方でも材料で完成品が変わる）。</summary>
        private static string DyeFromIngredients(IList<string> used)
        {
            foreach (var id in used)
            {
                var d = ItemRegistry.Get(id);
                var c = Concepts.ColorFromTags(d);
                if (c == null) continue;
                switch (c)
                {
                    case Concepts.RED: return CookingIds.DyePrefix + "red";
                    case Concepts.BLUE: return CookingIds.DyePrefix + "blue";
                    case Concepts.YELLOW: return CookingIds.DyePrefix + "yellow";
                    case Concepts.GREEN: return CookingIds.DyePrefix + "green";
                    case Concepts.PURPLE: return CookingIds.DyePrefix + "purple";
                    case Concepts.PINK: return CookingIds.DyePrefix + "pink";
                    case Concepts.ORANGE: return CookingIds.DyePrefix + "orange";
                    case Concepts.WHITE: return CookingIds.DyePrefix + "white";
                    case Concepts.BLACK: return CookingIds.DyePrefix + "black";
                }
            }
            return CookingIds.DyePrefix + "green";
        }

        // =================================================================
        // ロケット用の加工品（ロケット仕様 §9.2。登録担当：Cooking）
        // =================================================================

        private static void RegisterRocketParts()
        {
            Reg(SharedItemIds.PartConductiveWire, "導電部材", "Conductive wire", "導電性鉱石を製錬して引きのばした部材。補助電源の修理に使う。", "Smelted conductive material for the auxiliary power unit.",
                ItemCategory.RocketPart, 180, new Color(0.95f, 0.65f, 0.3f), PrimitiveType.Cylinder, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Conductive, ItemTags.Metal, ItemTags.NotSellable);
            Reg(SharedItemIds.PartHullPlate, "強化船体板", "Reinforced hull plate", "高強度金属を鍛えた板。船体外装の修理に使う。", "A forged plate for the hull.",
                ItemCategory.RocketPart, 420, new Color(0.6f, 0.65f, 0.75f), PrimitiveType.Cube, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Metal, ItemTags.NotSellable);
            Reg(SharedItemIds.PartCoolingMembrane, "冷却膜", "Cooling membrane", "温度差に強い魚の膜を洗って形を整え、冷やして固めた膜。", "A membrane from a frost-fin fish.",
                ItemCategory.RocketPart, 380, new Color(0.6f, 0.9f, 1f), PrimitiveType.Cube, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Cooling, ItemTags.NotSellable);
            Reg(SharedItemIds.FuelStarHoney, "星蜜燃料", "Star-honey fuel", "星蜜果をしぼり、発酵させ、熱して精製した宇宙船用燃料。", "Spacecraft fuel refined from star-honey fruit.",
                ItemCategory.RocketPart, 300, new Color(1f, 0.75f, 0.2f), PrimitiveType.Cylinder, Concepts.FUEL, ItemTags.RocketMaterial, ItemTags.Fuel, ItemTags.Glowing, ItemTags.NotSellable);
            Reg(SharedItemIds.PartHeatproofFrame, "耐熱構造材", "Heatproof frame", "耐熱金属を熱して形作り、冷やして締めた構造材。主エンジン用。", "A heat-resistant frame for the main engine.",
                ItemCategory.RocketPart, 650, new Color(0.75f, 0.45f, 0.35f), PrimitiveType.Cube, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Heatproof, ItemTags.Metal, ItemTags.NotSellable);
            Reg(SharedItemIds.PartCrystalCore, "結晶エネルギーコア", "Crystal energy core", "高密度の結晶を精密に磨き上げたコア。", "A precisely polished crystal core.",
                ItemCategory.RocketPart, 900, new Color(0.65f, 0.55f, 1f), PrimitiveType.Sphere, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Crystal, ItemTags.Glowing, ItemTags.Precision, ItemTags.NotSellable);
            Reg(SharedItemIds.PartRippleSensor, "揺らぎセンサー素材", "Ripple sensor", "揺らぎを感じる魚の器官を洗って精密に仕上げた素材。", "A precise sensor from a ripple-eye fish.",
                ItemCategory.RocketPart, 800, new Color(0.55f, 1f, 0.85f), PrimitiveType.Sphere, Concepts.PART, ItemTags.RocketMaterial, ItemTags.Sensor, ItemTags.Precision, ItemTags.NotSellable);
        }

        // =================================================================
        // 調理器具（§4.1。家具として置き、[E] で OpenStation(Kitchen, instanceId)）
        // =================================================================

        private static void RegisterDevices()
        {
            var sellersAll = new[] { ResidentJob.Carpenter, ResidentJob.Merchant };
            var sellersCook = new[] { ResidentJob.Carpenter, ResidentJob.Merchant, ResidentJob.Cook };
            AddDevice(CookingIds.DevHearth, "ぽかぽかかまど", "Toasty hearth", "温める生き物が住むかまど。焼く・煮る・保温。", "A hearth where a warming creature lives.",
                CreatureAbility.Heat, null, 0, 600, new Color(0.75f, 0.45f, 0.35f), PrimitiveType.Cube, sellersCook);
            AddDevice(CookingIds.DevWaterStand, "みずだし調理台", "Water counter", "水を出す生き物が遊ぶ調理台。洗う・水を加える・蒸す。", "A counter for a water creature.",
                CreatureAbility.Water, null, 0, 500, new Color(0.45f, 0.7f, 0.95f), PrimitiveType.Cube, sellersCook);
            AddDevice(CookingIds.DevGrindTable, "ころころ製粉台", "Rolling mill table", "転がる生き物が粉を挽く台。製粉・粉砕・こねる。", "A table where a rolling creature grinds.",
                CreatureAbility.Grind, null, 0, 650, new Color(0.85f, 0.75f, 0.55f), PrimitiveType.Cylinder, sellersAll);
            AddDevice(CookingIds.DevBubbleBowl, "あわあわボウル", "Bubble bowl", "泡を作る生き物が入るボウル。泡立て・混ぜる。", "A bowl for a bubbly creature.",
                CreatureAbility.Bubble, null, 0, 450, new Color(0.8f, 0.95f, 1f), PrimitiveType.Sphere, sellersCook);
            AddDevice(CookingIds.DevPress, "ぎゅっと圧搾台", "Squeeze press", "しぼる生き物が働く圧搾台。ジュース・植物の汁。", "A press for a squeezing creature.",
                CreatureAbility.Press, null, 0, 700, new Color(0.9f, 0.55f, 0.65f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevFermentBarrel, "ぷくぷく発酵樽", "Bubbly ferment barrel", "時間をかけて発酵させる樽。発酵させる生き物がいると早く仕上がる。", "A barrel for slow fermentation.",
                CreatureAbility.Ferment, CraftStep.Ferment, CookingTuning.DeviceTimedSlots, 550, new Color(0.6f, 0.42f, 0.28f), PrimitiveType.Cylinder, sellersCook);
            AddDevice(CookingIds.DevDryRack, "ふわふわ乾燥棚", "Fluffy drying rack", "干し魚や乾燥野菜を作る棚。晴れの日やふわふわ浮く生き物で早く乾く。", "A rack for drying food.",
                CreatureAbility.Float, CraftStep.Dry, CookingTuning.DeviceTimedSlots, 400, new Color(0.9f, 0.85f, 0.65f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevSmoker, "もくもく燻製箱", "Smoky smoker box", "燻製を作る箱。影の生き物や温める生き物が手伝うと早い。", "A box for smoking food.",
                CreatureAbility.Shade, CraftStep.Smoke, CookingTuning.DeviceTimedSlots, 650, new Color(0.4f, 0.38f, 0.4f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevCoolBox, "ひんやり冷却箱", "Chilly cool box", "冷やす生き物が涼む箱。冷たい菓子・飲み物。", "A box for a chilling creature.",
                CreatureAbility.Cool, null, 0, 750, new Color(0.65f, 0.9f, 1f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevStackTable, "つみつみ盛り付け台", "Stacking plating table", "積み重ねる生き物が盛り付ける台。ケーキ・盛り合わせ。", "A plating table for a stacking creature.",
                CreatureAbility.Stack, null, 0, 450, new Color(0.7f, 0.85f, 0.6f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevCarryShelf, "運び屋の搬送棚", "Carrier shelf", "運ぶ生き物が材料と完成品を運ぶ棚。まとめて作れる数が増える。", "A shelf for a carrying creature. Bigger batches.",
                CreatureAbility.Carry, null, 0, 500, new Color(0.95f, 0.8f, 0.45f), PrimitiveType.Cube, sellersAll);
            AddDevice(CookingIds.DevPerch, "台所の止まり木", "Kitchen perch", "まねる生き物の止まり木。見せてもらった料理の実演を見直せる。", "A perch for a mimic. Replays cooking demos you saw.",
                CreatureAbility.Mimic, null, 0, 350, new Color(0.9f, 0.7f, 0.95f), PrimitiveType.Cylinder, sellersAll);
            var refiner = AddDevice(CookingIds.DevRefiner, "ちいさな精製器", "Small refinery", "家で使える小さな精製器。しぼる・発酵・加熱をつないで燃料を作る。", "A home refinery for fuel.",
                null, CraftStep.Ferment, 2, 1800, new Color(0.85f, 0.65f, 0.3f), PrimitiveType.Cylinder, new[] { ResidentJob.Carpenter });
            refiner.Station = CraftStationType.Refinery;
            var bench = AddDevice(CookingIds.DevWorkbench, "加工作業台", "Workbench", "鉱石・結晶・繊維などを加工する作業台。", "A bench for processing materials.",
                null, null, 0, 800, new Color(0.65f, 0.5f, 0.35f), PrimitiveType.Cube, new[] { ResidentJob.Carpenter });
            bench.Station = CraftStationType.Workbench;
        }

        private static DeviceDef AddDevice(string id, string ja, string en, string dja, string den, CreatureAbility? ability, CraftStep? timed, int slots,
            int price, Color c, PrimitiveType shape, ResidentJob[] sellers)
        {
            var dev = new DeviceDef
            {
                ItemId = id, NameJa = ja, NameEn = en, DescJa = dja, DescEn = den, Ability = ability, TimedStep = timed, TimedSlots = slots,
                Price = price, Color = c, Shape = shape, Sellers = sellers
            };
            devices[id] = dev;
            var item = Reg(id, ja, en, dja, den, ItemCategory.Furniture, price, c, shape, Concepts.FURNITURE,
                ItemTags.Furniture, ItemTags.Kitchen, CookingTags.KitchenDevice, CookingTags.CraftStation, ItemTags.CreatureHabitat);
            if (ability.HasValue) item.Tags.Add(CookingTags.AbilityTag(ability.Value));
            item.ModuleData = dev;
            return dev;
        }

        // =================================================================
        // レシピ
        // =================================================================

        private static RecipeDef R(string id, string ja, string en, string output, RecipeKind kind, CraftStationType[] stations)
        {
            var r = new RecipeDef { Id = id, NameJa = ja, NameEn = en, OutputItemId = output, Kind = kind, Stations = stations };
            if (kind == RecipeKind.Dish || kind == RecipeKind.Processed) r.Demonstrable = true;
            RecipeBook.Add(r);
            return r;
        }

        private static void RegisterRecipes()
        {
            // ---- 共用調理場で最初からできる基本料理（§2.1）----
            var r = R("rcp.grilled_fish", "焼き魚", "Grilled fish", CookingIds.GrilledFish, RecipeKind.Dish, Food).Need(Ing.Fish()).Do(CraftStep.Heat);
            r.StartKnown = true; r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 120; r.Demonstrable = false;
            r = R("rcp.grain_porridge", "麦がゆ", "Grain porridge", "cook.dish.grain_porridge", RecipeKind.Dish, Food).Need(Ing.Grain()).Do(CraftStep.Water, CraftStep.Heat);
            r.StartKnown = true; r.FoodEffectId = CookingTables.EffectWarm; r.Demonstrable = false;
            r = R("rcp.flatbread", "麦の薄焼き", "Flatbread", "cook.dish.flatbread", RecipeKind.Dish, Food).Need(Ing.Grain()).Do(CraftStep.Water, CraftStep.Shape, CraftStep.Heat);
            r.StartKnown = true; r.FoodEffectId = CookingTables.EffectSwift; r.Demonstrable = false;
            r = R("rcp.grilled_veg", "焼き野菜", "Grilled vegetables", "cook.dish.grilled_veg", RecipeKind.Dish, Food).Need(Ing.Veg()).Do(CraftStep.Heat);
            r.StartKnown = true; r.FoodEffectId = CookingTables.EffectKeenEye; r.Demonstrable = false;
            r = R("rcp.star_salad", "星屑サラダ", "Stardust salad", "cook.dish.star_salad", RecipeKind.Dish, Hand).Need(Ing.Veg(), Ing.Fruit()).Do(CraftStep.Shape);
            r.StartKnown = true; r.FoodEffectId = CookingTables.EffectSwift; r.Demonstrable = false;

            // ---- 同じ魚から違う商品（§5.2）----
            r = R("rcp.salt_grilled_fish", "塩焼き魚", "Salt-grilled fish", "cook.dish.salt_grilled_fish", RecipeKind.Dish, Food).Need(Ing.Fish(), Ing.Item(CookingIds.StarSalt)).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 180;
            r = R("rcp.steamed_fish", "ぷるぷる蒸し魚", "Jiggly steamed fish", "cook.dish.steamed_fish", RecipeKind.Dish, Food).Need(Ing.Fish()).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectCalm;
            r = R("rcp.fish_veg_soup", "魚と野菜のスープ", "Fish & veggie soup", "cook.dish.fish_veg_soup", RecipeKind.Dish, Food).Need(Ing.Fish(), Ing.Veg()).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectWarm; r.FoodEffectMinutes = 180;
            r = R("rcp.herb_fish", "香草包み焼き", "Herb-wrapped fish", "cook.dish.herb_fish", RecipeKind.Dish, Food).Need(Ing.Fish(), Ing.Herb()).Do(CraftStep.Shape, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 240; r.BonusPhenomenon = AlienPhenomenon.StarWind;
            r = R("rcp.fish_paste", "すり身", "Fish paste", CookingIds.FishPaste, RecipeKind.Processed, Food).Need(Ing.Fish()).Do(CraftStep.Grind);
            r = R("rcp.surimi_grill", "すり身焼き", "Fish cake", "cook.dish.surimi_grill", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.FishPaste)).Do(CraftStep.Shape, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.charred_fish", "こんがり焦げ魚", "Charred fish", "cook.dish.charred_fish", RecipeKind.Accidental, Food).Need(Ing.Fish()).Do(CraftStep.Heat, CraftStep.Heat, CraftStep.Heat);
            r.Demonstrable = false;
            r = R("rcp.dried_fish", "干し魚", "Dried fish", CookingIds.DriedFish, RecipeKind.Processed, Timed).Need(Ing.Fish()).Do(CraftStep.Dry);
            r.SunnyBonus = true; r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 300;
            r = R("rcp.smoked_fish", "もくもく燻製魚", "Smoky fish", CookingIds.SmokedFish, RecipeKind.Processed, HomeTimed).Need(Ing.Fish()).Do(CraftStep.Smoke);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 360; r.BonusPhenomenon = AlienPhenomenon.StarWind;
            r = R("rcp.seared_dried_fish", "あぶり干し魚", "Seared dried fish", "cook.dish.seared_dried_fish", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.DriedFish)).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 240;
            r = R("rcp.fish_veg_grill", "魚と野菜の焼き合わせ", "Fish & veggie grill", "cook.dish.fish_veg_grill", RecipeKind.Dish, Food).Need(Ing.Fish(), Ing.Veg()).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectKeenEye;

            // ---- 野菜・穀物・豆 ----
            r = R("rcp.veg_stirfry", "野菜炒め", "Veggie stir-fry", "cook.dish.veg_stirfry", RecipeKind.Dish, Food).Need(Ing.Veg(2)).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectKeenEye; r.FoodEffectMinutes = 180;
            r = R("rcp.bean_stew", "ころころ豆の煮込み", "Bean stew", "cook.dish.bean_stew", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.RoundBean), Ing.Item(CookingIds.StarSalt)).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectWarm;
            r = R("rcp.herb_tea", "香り草のお茶", "Herb tea", "cook.dish.herb_tea", RecipeKind.Dish, Food).Need(Ing.Herb()).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectCalm; r.BonusPhenomenon = AlienPhenomenon.StarWind;
            r = R("rcp.candied_fruit", "果実の蜜煮", "Candied fruit", "cook.dish.candied_fruit", RecipeKind.Dish, Food).Need(Ing.Fruit(), Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.fruit_platter", "果物の盛り合わせ", "Fruit platter", "cook.dish.fruit_platter", RecipeKind.Dish, Hand).Need(Ing.Fruit(2)).Do(CraftStep.Shape);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.veg_puree", "野菜ピューレ", "Vegetable purée", CookingIds.VegPuree, RecipeKind.Processed, Food).Need(Ing.Veg()).Do(CraftStep.Water, CraftStep.Heat, CraftStep.Grind);
            r = R("rcp.whole_veg_soup", "まるごと野菜スープ", "Hearty veggie soup", "cook.dish.whole_veg_soup", RecipeKind.Dish, Food).Need(Ing.Veg(2)).Do(CraftStep.Water, CraftStep.Heat, CraftStep.Grind);
            r.FoodEffectId = CookingTables.EffectWarm; r.FoodEffectMinutes = 240;
            r = R("rcp.potage", "野菜ポタージュ", "Vegetable potage", "cook.dish.potage", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.VegPuree)).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectWarm;
            r = R("rcp.dried_veg", "乾燥野菜", "Dried vegetables", CookingIds.DriedVeg, RecipeKind.Processed, Timed).Need(Ing.Veg()).Do(CraftStep.Dry);
            r.SunnyBonus = true;
            r = R("rcp.sun_dried_fruit", "ほし果実", "Dried fruit", CookingIds.SunDriedFruit, RecipeKind.Processed, Timed).Need(Ing.Fruit()).Do(CraftStep.Dry);
            r.SunnyBonus = true; r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.pickles", "星の漬け物", "Star pickles", CookingIds.Pickles, RecipeKind.Processed, Timed).Need(Ing.Veg(), Ing.Item(CookingIds.StarSalt)).Do(CraftStep.Ferment);
            r.RainBonus = true;
            r = R("rcp.ferment_sauce", "発酵だれ", "Fermented sauce", CookingIds.FermentSauce, RecipeKind.Processed, Timed).Need(Ing.Item(CookingIds.RoundBean), Ing.Item(CookingIds.StarSalt)).Do(CraftStep.Grind, CraftStep.Ferment);
            r.RainBonus = true;
            r = R("rcp.pickle_porridge", "漬け物そえ麦がゆ", "Porridge with pickles", "cook.dish.pickle_porridge", RecipeKind.Dish, Food).Need(Ing.Grain(), Ing.Item(CookingIds.Pickles)).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectWarm;
            r = R("rcp.simmered_fish", "魚の煮付け", "Simmered fish", "cook.dish.simmered_fish", RecipeKind.Dish, Food).Need(Ing.Fish(), Ing.Item(CookingIds.FermentSauce)).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 240;
            r = R("rcp.ferment_hotpot", "発酵鍋", "Fermented hotpot", "cook.dish.ferment_hotpot", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.FermentSauce), Ing.Fish(), Ing.Veg()).Do(CraftStep.Water, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectWarm; r.FoodEffectMinutes = 360;
            r = R("rcp.herb_salt", "香り塩", "Herb salt", CookingIds.HerbSalt, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.StarSalt), Ing.Herb()).Do(CraftStep.Grind);
            r = R("rcp.starwind_herb_salt", "星風ハーブ塩", "Star-wind herb salt", CookingIds.StarWindHerbSalt, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.StarSalt), Ing.Herb()).Do(CraftStep.Grind);
            r.RequiredPhenomenon = AlienPhenomenon.StarWind; r.Priority = 10; r.Demonstrable = false;

            // ---- 穀物 → 粉 → 生地 → パン（§5.3 加工の連鎖）----
            r = R("rcp.flour", "星の粉", "Star flour", CookingIds.Flour, RecipeKind.Processed, Food).Need(Ing.Grain()).Do(CraftStep.Grind);
            r = R("rcp.dough", "こね生地", "Dough", CookingIds.Dough, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.Flour)).Do(CraftStep.Water, CraftStep.Grind);
            r = R("rcp.bread", "ふつうのパン", "Plain bread", CookingIds.Bread, RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Dough)).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.star_dumpling", "星形だんご", "Star dumplings", "cook.dish.star_dumpling", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Flour)).Do(CraftStep.Water, CraftStep.Shape, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.pancake", "ふわふわパンケーキ", "Fluffy pancake", "cook.dish.pancake", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Flour), Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Bubble, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift; r.FoodEffectMinutes = 180;
            r = R("rcp.jam_bread", "ジャムパン", "Jam bread", "cook.dish.jam_bread", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Dough), Ing.Item(CookingIds.Jam)).Do(CraftStep.Shape, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift; r.FoodEffectMinutes = 180;
            r = R("rcp.honey_toast", "蜜トースト", "Nectar toast", "cook.dish.honey_toast", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Bread), Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.fish_sandwich", "焼き魚サンド", "Fish sandwich", "cook.dish.fish_sandwich", RecipeKind.Dish, Hand).Need(Ing.Item(CookingIds.Bread), Ing.Item(CookingIds.GrilledFish)).Do(CraftStep.Shape);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 240;
            r = R("rcp.veg_roll", "ぐるぐる野菜ロール", "Swirl veggie roll", "cook.dish.veg_roll", RecipeKind.Dish, Food).Need(Ing.Veg(), Ing.Item(CookingIds.Dough)).Do(CraftStep.Shape, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectKeenEye; r.FoodEffectMinutes = 180;

            // ---- 果実 → ジュース・ジャム（§5.3）----
            r = R("rcp.juice", "果実ジュース", "Fruit juice", CookingIds.Juice, RecipeKind.Processed, Food).Need(Ing.Fruit()).Do(CraftStep.Press);
            r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.chilled_juice", "ひんやりジュース", "Chilled juice", CookingIds.ChilledJuice, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.Juice)).Do(CraftStep.Cool);
            r.FoodEffectId = CookingTables.EffectSwift; r.FoodEffectMinutes = 180;
            r = R("rcp.foam_drink", "あわあわドリンク", "Foam drink", "cook.dish.foam_drink", RecipeKind.Dish, Food).Need(Ing.Item(CookingIds.Juice)).Do(CraftStep.Bubble);
            r.FoodEffectId = CookingTables.EffectCalm;
            r = R("rcp.jam", "果実ジャム", "Fruit jam", CookingIds.Jam, RecipeKind.Processed, Food).Need(Ing.Fruit()).Do(CraftStep.Grind, CraftStep.Heat);
            r = R("rcp.lightrain_jam", "光雨ジャム", "Light-rain jam", CookingIds.LightRainJam, RecipeKind.Processed, Food).Need(Ing.Fruit()).Do(CraftStep.Grind, CraftStep.Heat);
            r.RequiredPhenomenon = AlienPhenomenon.LightRain; r.Priority = 10; r.Demonstrable = false; r.FoodEffectId = CookingTables.EffectGlow;
            r = R("rcp.fruit_candy", "果実キャンディ", "Fruit candy", CookingIds.FruitCandy, RecipeKind.Accidental, Food).Need(Ing.Fruit()).Do(CraftStep.Grind, CraftStep.Heat, CraftStep.Heat);
            r.Demonstrable = false;
            r = R("rcp.star_ferment_juice", "星の発酵ジュース", "Star fizz", CookingIds.StarFermentJuice, RecipeKind.Processed, Timed).Need(Ing.Item(CookingIds.Juice)).Do(CraftStep.Ferment);
            r.FoodEffectId = CookingTables.EffectGlow; r.RainBonus = true;
            r = R("rcp.foam_cream", "あわあわクリーム", "Foam cream", CookingIds.FoamCream, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Bubble);
            r = R("rcp.cream_fruit", "泡クリームの果実盛り", "Fruit with foam", "cook.dish.cream_fruit", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.FoamCream), Ing.Fruit()).Do(CraftStep.Stack);
            r.FoodEffectId = CookingTables.EffectCalm;
            r = R("rcp.honey_candy", "蜜あめ", "Nectar drops", CookingIds.HoneyCandy, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Water, CraftStep.Heat, CraftStep.Cool);
            r = R("rcp.reverse_rain_drops", "逆雨しずく飴", "Upward-drop candy", CookingIds.ReverseRainDrops, RecipeKind.Processed, Food).Need(Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Water, CraftStep.Heat, CraftStep.Cool);
            r.RequiredPhenomenon = AlienPhenomenon.ReverseRain; r.Priority = 10; r.Demonstrable = false; r.FoodEffectId = CookingTables.EffectSwift;
            r = R("rcp.sherbet", "ひんやり果実シャーベット", "Fruit sherbet", "cook.dish.sherbet", RecipeKind.Dish, Food).Need(Ing.Fruit(), Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Grind, CraftStep.Cool);
            r.FoodEffectId = CookingTables.EffectSwift;

            // ---- 特殊料理（§5.4。自宅のキッチンで生き物と器具を揃えて）----
            r = R("rcp.fluffy_star_bread", "ふわふわ星パン", "Fluffy star bread", "cook.dish.fluffy_star_bread", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.Flour)).Do(CraftStep.Water, CraftStep.Bubble, CraftStep.Heat);
            r.FoodEffectId = CookingTables.EffectSwift; r.FoodEffectMinutes = 300;
            r = R("rcp.fruit_jelly", "キラキラ果実ゼリー", "Sparkle fruit jelly", "cook.dish.fruit_jelly", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.Juice)).Do(CraftStep.Cool, CraftStep.Glow);
            r.FoodEffectId = CookingTables.EffectGlow; r.FoodEffectMinutes = 300; r.BonusPhenomenon = AlienPhenomenon.LightRain;
            r = R("rcp.rainbow_cake", "にじいろ重ねケーキ", "Rainbow layer cake", "cook.dish.rainbow_cake", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.Flour), Ing.Item(CookingIds.TreeNectar), Ing.Fruit()).Do(CraftStep.Bubble, CraftStep.Heat, CraftStep.Stack);
            r.FoodEffectId = CookingTables.EffectCalm; r.FoodEffectMinutes = 360;
            r = R("rcp.cloud_pudding", "ぷるぷる雲プリン", "Cloud pudding", "cook.dish.cloud_pudding", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.TreeNectar)).Do(CraftStep.Bubble, CraftStep.Water, CraftStep.Heat, CraftStep.Cool);
            r.FoodEffectId = CookingTables.EffectCalm; r.FoodEffectMinutes = 240;
            r = R("rcp.glow_soup", "光るスープ", "Glowing soup", "cook.dish.glow_soup", RecipeKind.Dish, Food).Need(Ing.Veg()).Do(CraftStep.Water, CraftStep.Heat, CraftStep.Glow);
            r.FoodEffectId = CookingTables.EffectGlow; r.FoodEffectMinutes = 360; r.BonusPhenomenon = AlienPhenomenon.LightRain;
            r = R("rcp.smoked_platter", "燻製の盛り合わせ", "Smoked platter", "cook.dish.smoked_platter", RecipeKind.Dish, HomeOnly).Need(Ing.Item(CookingIds.SmokedFish), Ing.Item(CookingIds.DriedVeg)).Do(CraftStep.Stack);
            r.FoodEffectId = CookingTables.EffectFishingBite; r.FoodEffectMinutes = 360;
            r = R("rcp.feast_platter", "ごちそう盛り", "Feast platter", "cook.dish.feast_platter", RecipeKind.Dish, HomeOnly).Need(Ing.AnyDish(2)).Do(CraftStep.Stack);
            r.FoodEffectId = CookingTables.EffectCalm; r.Demonstrable = false; r.Priority = -5;
            r = R("rcp.gift_assort", "料理の詰め合わせ", "Dish assortment", "cook.dish.gift_assort", RecipeKind.Dish, Hand).Need(Ing.AnyDish(2)).Do(CraftStep.Pack);
            r.Demonstrable = false; r.Priority = -5;

            // ---- 非食品の素材加工（§9）----
            r = R("rcp.metal_ingot", "金属インゴット", "Metal ingot", CookingIds.MetalIngot, RecipeKind.Material, new[] { CraftStationType.Forge, CraftStationType.Workbench }).Need(Ing.MetalOre(2)).Do(CraftStep.Heat);
            r.StartKnown = true; r.CommissionFee = 40;
            r = R("rcp.polished_crystal", "磨いた結晶", "Polished crystal", CookingIds.PolishedCrystal, RecipeKind.Material, new[] { CraftStationType.Workbench, CraftStationType.CarpenterBench, CraftStationType.RocketWorkshop }).Need(Ing.Crystal()).Do(CraftStep.Polish);
            r.CommissionFee = 50; r.BonusPhenomenon = AlienPhenomenon.CrystalHaze;
            r = R("rcp.star_glass", "星ガラス", "Star glass", CookingIds.StarGlass, RecipeKind.Material, new[] { CraftStationType.Workbench, CraftStationType.Forge }).Need(Ing.AnyOre(2)).Do(CraftStep.Grind, CraftStep.Heat, CraftStep.Cool);
            r.CommissionFee = 60;
            r = R("rcp.mineral_powder", "鉱石粉", "Mineral powder", CookingIds.MineralPowder, RecipeKind.Material, Bench).Need(Ing.AnyOre()).Do(CraftStep.Grind);
            r = R("rcp.plant_fiber", "植物繊維", "Plant fiber", CookingIds.PlantFiber, RecipeKind.Material, new[] { CraftStationType.Workbench, CraftStationType.CarpenterBench, CraftStationType.Kitchen }).Need(Ing.Plant()).Do(CraftStep.Press, CraftStep.Grind);
            r.StartKnown = true;
            r = R("rcp.cloth", "ふわ布", "Soft cloth", CookingIds.Cloth, RecipeKind.Material, Bench).Need(Ing.Item(CookingIds.PlantFiber, 2)).Do(CraftStep.Grind, CraftStep.Stack);
            r.CommissionFee = 40;
            r = R("rcp.fiber_board", "繊維ボード", "Fiber board", CookingIds.FiberBoard, RecipeKind.Material, Bench).Need(Ing.Item(CookingIds.PlantFiber, 3)).Do(CraftStep.Press, CraftStep.Heat);
            r.StartKnown = true; r.CommissionFee = 40;
            r = R("rcp.stone_tile", "加工石", "Stone tile", CookingIds.StoneTile, RecipeKind.Material, Bench).Need(Ing.AnyOre(2)).Do(CraftStep.Shape, CraftStep.Polish);
            r.StartKnown = true; r.CommissionFee = 30;
            r = R("rcp.dye", "染料", "Dye", CookingIds.DyePrefix + "green", RecipeKind.Material, new[] { CraftStationType.Workbench, CraftStationType.Kitchen, CraftStationType.CommunityKitchen }).Need(Ing.Colorful()).Do(CraftStep.Press, CraftStep.Water);
            r.OutputSelector = DyeFromIngredients; r.BonusPhenomenon = AlienPhenomenon.BloomTide;
            foreach (var key in new[] { "red", "blue", "yellow", "green", "purple", "pink", "orange", "white", "black" }) r.AlsoProduces.Add(CookingIds.DyePrefix + key);
            r.NoteJa = "材料の色で染料の色が変わる"; r.NoteEn = "The dye color follows the ingredient";

            // ---- ロケット用（ロケット仕様 §9.2。自分で作る／職人に頼む の複数手段 §9.3）----
            var forgeWs = new[] { CraftStationType.Forge, CraftStationType.RocketWorkshop };
            r = R("rcp.part.conductive_wire", "導電部材", "Conductive wire", SharedItemIds.PartConductiveWire, RecipeKind.RocketPart, forgeWs)
                .Need(Ing.Item(SharedItemIds.OreConductive, 3)).Do(CraftStep.Heat, CraftStep.Shape);
            r.StartKnown = true; r.CommissionFee = 120;
            r = R("rcp.part.hull_plate", "強化船体板", "Reinforced hull plate", SharedItemIds.PartHullPlate, RecipeKind.RocketPart, new[] { CraftStationType.Forge })
                .Need(Ing.Item(SharedItemIds.OreSturdyMetal, 4)).Do(CraftStep.Heat, CraftStep.Shape, CraftStep.Shape);
            r.StartKnown = true; r.CommissionFee = 300;
            r = R("rcp.part.cooling_membrane", "冷却膜", "Cooling membrane", SharedItemIds.PartCoolingMembrane, RecipeKind.RocketPart, new[] { CraftStationType.RocketWorkshop, CraftStationType.Workbench })
                .Need(Ing.Item(SharedItemIds.FishFrostFin, 2)).Do(CraftStep.Water, CraftStep.Shape, CraftStep.Cool);
            r.StartKnown = true; r.CommissionFee = 250;
            r = R("rcp.part.fuel", "星蜜燃料", "Star-honey fuel", SharedItemIds.FuelStarHoney, RecipeKind.RocketPart, new[] { CraftStationType.Refinery })
                .Need(Ing.Item(SharedItemIds.CropStarHoneyFruit, 3)).Do(CraftStep.Press, CraftStep.Ferment, CraftStep.Heat);
            r.StartKnown = true; r.CommissionFee = 200;
            r = R("rcp.part.heatproof_frame", "耐熱構造材", "Heatproof frame", SharedItemIds.PartHeatproofFrame, RecipeKind.RocketPart, new[] { CraftStationType.Forge })
                .Need(Ing.Item(SharedItemIds.OreHeatproofMetal, 3)).Do(CraftStep.Heat, CraftStep.Shape, CraftStep.Cool);
            r.StartKnown = true; r.CommissionFee = 450;
            r = R("rcp.part.crystal_core", "結晶エネルギーコア", "Crystal energy core", SharedItemIds.PartCrystalCore, RecipeKind.RocketPart, new[] { CraftStationType.RocketWorkshop })
                .Need(Ing.Item(SharedItemIds.OreEnergyCrystal, 2)).Do(CraftStep.Polish, CraftStep.Shape);
            r.StartKnown = true; r.CommissionFee = 600; r.BonusPhenomenon = AlienPhenomenon.CrystalHaze;
            r = R("rcp.part.ripple_sensor", "揺らぎセンサー素材", "Ripple sensor", SharedItemIds.PartRippleSensor, RecipeKind.RocketPart, new[] { CraftStationType.RocketWorkshop })
                .Need(Ing.Item(SharedItemIds.FishRippleEye, 1)).Do(CraftStep.Water, CraftStep.Polish);
            r.StartKnown = true; r.CommissionFee = 500;

            // ---- 調理器具（家具職人の作業台・自宅の作業台。家具職人に頼むこともできる）----
            var devBench = new[] { CraftStationType.CarpenterBench, CraftStationType.Workbench };
            DeviceRecipe(CookingIds.DevHearth, devBench, 180, Ing.Item(CookingIds.StoneTile, 2), Ing.Item(CookingIds.MetalIngot, 1));
            DeviceRecipe(CookingIds.DevWaterStand, devBench, 150, Ing.Item(CookingIds.FiberBoard, 1), Ing.Item(CookingIds.StarGlass, 1));
            DeviceRecipe(CookingIds.DevGrindTable, devBench, 190, Ing.Item(CookingIds.StoneTile, 2), Ing.Item(CookingIds.FiberBoard, 1));
            DeviceRecipe(CookingIds.DevBubbleBowl, devBench, 130, Ing.Item(CookingIds.StarGlass, 2));
            DeviceRecipe(CookingIds.DevPress, devBench, 200, Ing.Item(CookingIds.FiberBoard, 1), Ing.Item(CookingIds.MetalIngot, 2));
            DeviceRecipe(CookingIds.DevFermentBarrel, devBench, 160, Ing.Item(CookingIds.FiberBoard, 3));
            DeviceRecipe(CookingIds.DevDryRack, devBench, 120, Ing.Item(CookingIds.FiberBoard, 1), Ing.Item(CookingIds.Cloth, 1));
            DeviceRecipe(CookingIds.DevSmoker, devBench, 190, Ing.Item(CookingIds.MetalIngot, 1), Ing.Item(CookingIds.StoneTile, 1), Ing.Item(CookingIds.FiberBoard, 1));
            DeviceRecipe(CookingIds.DevCoolBox, devBench, 220, Ing.Item(CookingIds.StarGlass, 1), Ing.Item(CookingIds.MetalIngot, 1), Ing.Item(CookingIds.FiberBoard, 1));
            DeviceRecipe(CookingIds.DevStackTable, devBench, 130, Ing.Item(CookingIds.StoneTile, 1), Ing.Item(CookingIds.FiberBoard, 1));
            DeviceRecipe(CookingIds.DevCarryShelf, devBench, 150, Ing.Item(CookingIds.FiberBoard, 2));
            DeviceRecipe(CookingIds.DevPerch, devBench, 100, Ing.Item(CookingIds.FiberBoard, 1), Ing.Item(CookingIds.PolishedCrystal, 1));
            DeviceRecipe(CookingIds.DevRefiner, new[] { CraftStationType.CarpenterBench }, 500, Ing.Item(CookingIds.MetalIngot, 2), Ing.Item(CookingIds.StarGlass, 1), Ing.Item(CookingIds.PolishedCrystal, 1));
            DeviceRecipe(CookingIds.DevWorkbench, new[] { CraftStationType.CarpenterBench }, 240, Ing.Item(CookingIds.FiberBoard, 2), Ing.Item(CookingIds.MetalIngot, 1));
        }

        private static void DeviceRecipe(string deviceId, CraftStationType[] stations, int fee, params IngredientSpec[] specs)
        {
            var dev = Device(deviceId);
            var r = R("rcp.dev." + deviceId.Substring("cook.dev.".Length), dev.NameJa, dev.NameEn, deviceId, RecipeKind.Device, stations).Need(specs).Do(CraftStep.Shape);
            r.StartKnown = true;
            r.CommissionFee = fee;
            r.Demonstrable = false;
        }
    }
}
