using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Farming
{
    /// <summary>作物の分類（農業仕様 §9.1）。</summary>
    public enum CropKind
    {
        Vegetable, // 一度きりの野菜
        Regrow,    // 連続収穫作物
        FruitTree, // 果物・果樹（育成期間が長く、繰り返し実がなる）
        Grain,     // 穀物
        Flower,    // 花
        Special    // 特殊作物（光る・浮く・色が変わる）
    }

    /// <summary>作物が喜ぶ条件（天候・生物の能力）。時間・天候仕様 §7、農業仕様 §7.1。</summary>
    [System.Flags]
    public enum CropLikes
    {
        None = 0,
        Rain = 1 << 0,
        Wind = 1 << 1,
        Fog = 1 << 2,
        Sunny = 1 << 3,
        Glow = 1 << 4,   // 光（ヒカリムシなど）
        Heat = 1 << 5,   // 暖かさ（ポカリス）
        Cool = 1 << 6,   // 涼しさ（ツユツユなど）
        Pollen = 1 << 7  // 受粉で実りが増えやすい（ハナピカ）
    }

    /// <summary>作物1種の定義。数値はすべて【仮数値】。四季なし：いつでも植えられる。</summary>
    public sealed class CropDef
    {
        public string Key;
        public string CropId;
        public string SeedId;
        public string NameJa, NameEn;
        public string DescJa, DescEn;
        public CropKind Kind;
        public int GrowDays;
        /// <summary>再収穫までの日数（0なら一度きり）。</summary>
        public int RegrowDays;
        public int YieldMin = 1, YieldMax = 1;
        public int SellPrice;
        public int SeedPrice;
        public Color Color = Color.white;
        public Color Leaf = new Color(0.35f, 0.7f, 0.3f);
        public PrimitiveType FruitShape = PrimitiveType.Sphere;
        public CropLikes Likes;
        public bool GlowsAtNight;
        public bool FloatsWhenRipe;
        public string[] Tags = new string[0];
        /// <summary>光の影響で生まれる変種（光る実）。</summary>
        public string GlowVariantId;
        /// <summary>巨大な変種。</summary>
        public string GiantVariantId;
        /// <summary>夜の受粉＋光で生まれる珍しい花色。</summary>
        public string RareColorVariantId;
        /// <summary>色違い（ニジニンジン）。</summary>
        public string[] ColorVariantIds = new string[0];
        /// <summary>種屋に並ぶ条件：農業Lv またはその日数以降。</summary>
        public int UnlockLevel = 1;
        public int UnlockDay = 1;

        public string Name => Loc.T(NameJa, NameEn);
        public bool IsRegrow => RegrowDays > 0;
        /// <summary>見た目の段階（0=種 1=芽 2=育ち中 3=実り）。</summary>
        public int StageOf(float growth, bool ripe)
        {
            if (ripe) return 3;
            float r = GrowDays <= 0 ? 1f : growth / GrowDays;
            return r < 0.2f ? 0 : r < 0.6f ? 1 : 2;
        }
    }

    /// <summary>農場の住み場所（農業仕様 §8.1）。好きな能力の生き物が住むと作業範囲が広がる。</summary>
    public sealed class HabitatDef
    {
        public string ItemId;
        public string NameJa, NameEn;
        public CreatureAbility[] Favored = new CreatureAbility[0];
        public int Price;
        public Color Color;
        public int UnlockLevel = 1;
        public bool AnyCreature;

        public bool Favors(CreatureAbility a)
        {
            if (AnyCreature) return false;
            foreach (var f in Favored) if (f == a) return true;
            return false;
        }
    }

    /// <summary>肥料（農業仕様 §9.4）。</summary>
    public sealed class FertilizerDef
    {
        public string ItemId;
        public string NameJa, NameEn, DescJa, DescEn;
        public int QualityBonus;
        public float SpeedBonus;
        public int RetainDays;
        public int YieldBonus;
        public int Price;
        public int UnlockLevel = 1;
        public Color Color;
    }

    /// <summary>作物・種・肥料・住み場所のテーブル（約20種の宇宙作物：農業仕様 §9.2 の候補＋追加案）。</summary>
    public static class FarmCatalog
    {
        private static readonly List<CropDef> crops = new List<CropDef>();
        private static readonly Dictionary<string, CropDef> byKey = new Dictionary<string, CropDef>();
        private static readonly Dictionary<string, CropDef> bySeed = new Dictionary<string, CropDef>();
        private static readonly Dictionary<string, CropDef> byCrop = new Dictionary<string, CropDef>();
        private static readonly List<HabitatDef> habitats = new List<HabitatDef>();
        private static readonly List<FertilizerDef> fertilizers = new List<FertilizerDef>();
        private static bool built;

        public static IReadOnlyList<CropDef> Crops { get { Build(); return crops; } }
        public static IReadOnlyList<HabitatDef> Habitats { get { Build(); return habitats; } }
        public static IReadOnlyList<FertilizerDef> Fertilizers { get { Build(); return fertilizers; } }

        public static CropDef Get(string key) { Build(); return key != null && byKey.TryGetValue(key, out var c) ? c : null; }
        public static CropDef BySeed(string seedId) { Build(); return seedId != null && bySeed.TryGetValue(seedId, out var c) ? c : null; }
        /// <summary>作物（変種を含む）から定義を引く。</summary>
        public static CropDef ByCrop(string cropId) { Build(); return cropId != null && byCrop.TryGetValue(cropId, out var c) ? c : null; }

        public static HabitatDef Habitat(string itemId)
        {
            Build();
            foreach (var h in habitats) if (h.ItemId == itemId) return h;
            return null;
        }

        public static FertilizerDef Fertilizer(string itemId)
        {
            Build();
            foreach (var f in fertilizers) if (f.ItemId == itemId) return f;
            return null;
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        private static CropDef Crop(string key, string ja, string en, CropKind kind, int grow, int regrow, int ymin, int ymax, int price, int seedPrice, Color color, params string[] tags)
        {
            var c = new CropDef
            {
                Key = key,
                CropId = "crop." + key,
                SeedId = "seed." + key,
                NameJa = ja,
                NameEn = en,
                Kind = kind,
                GrowDays = grow,
                RegrowDays = regrow,
                YieldMin = ymin,
                YieldMax = ymax,
                SellPrice = price,
                SeedPrice = seedPrice,
                Color = color,
                Tags = tags
            };
            crops.Add(c);
            byKey[key] = c;
            return c;
        }

        private static void Build()
        {
            if (built) return;
            built = true;
            CropDef c;
            c = Crop("fuwafuwa_tomato", "ふわふわトマト", "Fluffy tomato", CropKind.Regrow, 4, 2, 1, 3, 45, 30, C(1f, 0.38f, 0.32f), ItemTags.Vegetable, ItemTags.Sweet, ItemTags.Sour, ItemTags.ColorRed);
            c.DescJa = "成熟すると実が少し浮く。何度も収穫できる。"; c.DescEn = "Ripe fruit floats a little. Harvest again and again."; c.FloatsWhenRipe = true; c.Likes = CropLikes.Pollen;
            c = Crop("hoshiberry", "ホシベリー", "Starberry", CropKind.Regrow, 5, 2, 2, 4, 40, 40, C(0.35f, 0.55f, 1f), ItemTags.Fruit, ItemTags.Sweet, ItemTags.ColorBlue);
            c.DescJa = "夜になると青く光る果実。"; c.DescEn = "A berry that glows blue at night."; c.GlowsAtNight = true; c.Likes = CropLikes.Glow | CropLikes.Pollen;
            c.GlowVariantId = "crop.hoshiberry_glow"; c.UnlockLevel = 2; c.UnlockDay = 4;
            c = Crop("shimashima_imo", "しましまイモ", "Stripe potato", CropKind.Vegetable, 5, 0, 1, 3, 70, 35, C(0.62f, 0.42f, 0.7f), ItemTags.Vegetable, ItemTags.Savory, ItemTags.ColorPurple);
            c.DescJa = "模様と大きさに個体差がある根菜。土が豊かだと大きく育つ。"; c.DescEn = "A root with varied stripes and sizes; rich soil grows big ones."; c.FruitShape = PrimitiveType.Capsule;
            c.GiantVariantId = "crop.shimashima_imo_giant"; c.UnlockLevel = 2; c.UnlockDay = 4;
            c = Crop("hoshi_piman", "ホシピーマン", "Star pepper", CropKind.Regrow, 5, 3, 2, 3, 40, 35, C(0.35f, 0.8f, 0.35f), ItemTags.Vegetable, ItemTags.Savory, ItemTags.ColorGreen);
            c.DescJa = "星形の実がなる野菜。"; c.DescEn = "A vegetable with star-shaped fruit."; c.FruitShape = PrimitiveType.Cube; c.UnlockLevel = 2; c.UnlockDay = 4;
            c = Crop("niji_ninjin", "ニジニンジン", "Rainbow carrot", CropKind.Vegetable, 4, 0, 1, 2, 55, 30, C(1f, 0.55f, 0.2f), ItemTags.Vegetable, ItemTags.Sweet, ItemTags.ColorOrange);
            c.DescJa = "抜くまで何色か分からない根菜。"; c.DescEn = "You won't know its color until you pull it."; c.FruitShape = PrimitiveType.Capsule;
            c.ColorVariantIds = new[] { "crop.niji_ninjin_red", "crop.niji_ninjin_yellow", "crop.niji_ninjin_purple" };
            c = Crop("bell_flower", "ベルフラワー", "Bellflower", CropKind.Flower, 4, 0, 1, 1, 80, 40, C(0.85f, 0.8f, 1f), ItemTags.Flower, ItemTags.ColorWhite, ItemTags.Music);
            c.DescJa = "風に揺れると音が鳴る花。"; c.DescEn = "A flower that rings when the wind blows."; c.Likes = CropLikes.Wind | CropLikes.Pollen;
            c.RareColorVariantId = "crop.bell_flower_rainbow"; c.UnlockLevel = 2; c.UnlockDay = 4;
            c = Crop("star_honey_fruit", "星蜜果", "Star honey fruit", CropKind.Special, 6, 3, 1, 2, 150, 120, C(1f, 0.82f, 0.3f), ItemTags.Fruit, ItemTags.Sweet, ItemTags.Fuel, ItemTags.RocketMaterial, ItemTags.ColorYellow);
            c.DescJa = "エネルギー特性を持つ甘い実。圧搾・発酵・加熱で宇宙船の燃料になる。いつでも育てられる。"; c.DescEn = "A sweet fruit with energy properties; pressed, fermented and heated it becomes ship fuel. Grows any time.";
            c.Likes = CropLikes.Heat | CropLikes.Pollen; c.UnlockLevel = 2; c.UnlockDay = 3;
            c = Crop("kumomame", "クモマメ", "Cloud bean", CropKind.Grain, 6, 3, 3, 5, 25, 30, C(0.95f, 0.95f, 1f), ItemTags.Grain, ItemTags.ColorWhite);
            c.DescJa = "雲のようにふわっと浮く豆。何度も収穫できる。"; c.DescEn = "Fluffy beans that float like clouds; harvest repeatedly."; c.FloatsWhenRipe = true; c.Likes = CropLikes.Fog; c.UnlockLevel = 3; c.UnlockDay = 8;
            c = Crop("tsukimugi", "ツキムギ", "Moon wheat", CropKind.Grain, 4, 0, 3, 5, 22, 20, C(0.9f, 0.9f, 0.82f), ItemTags.Grain, ItemTags.ColorWhite);
            c.DescJa = "銀色に揺れる麦。粉にしてパンなどに使える。"; c.DescEn = "Silvery wheat; can be milled into flour."; c.FruitShape = PrimitiveType.Capsule;
            c = Crop("hikari_daikon", "ヒカリダイコン", "Glow radish", CropKind.Vegetable, 5, 0, 1, 1, 75, 40, C(0.95f, 1f, 0.92f), ItemTags.Vegetable, ItemTags.Glowing, ItemTags.ColorWhite);
            c.DescJa = "夜にぼんやり光る大根。光を浴びると早く育つ。"; c.DescEn = "A radish that glows faintly at night; grows faster in light."; c.GlowsAtNight = true; c.Likes = CropLikes.Glow; c.FruitShape = PrimitiveType.Capsule; c.UnlockLevel = 3; c.UnlockDay = 8;
            c = Crop("purupuru_cabbage", "プルプルキャベツ", "Wobble cabbage", CropKind.Vegetable, 6, 0, 1, 1, 95, 45, C(0.6f, 0.9f, 0.5f), ItemTags.Vegetable, ItemTags.ColorGreen);
            c.DescJa = "ぷるぷる揺れるキャベツ。たまに巨大に育つ。"; c.DescEn = "A cabbage that wobbles. Sometimes grows huge."; c.GiantVariantId = "crop.purupuru_cabbage_giant";
            c = Crop("awa_ichigo", "アワイチゴ", "Bubble strawberry", CropKind.Regrow, 5, 2, 2, 4, 35, 45, C(1f, 0.55f, 0.7f), ItemTags.Fruit, ItemTags.Sweet, ItemTags.ColorPink);
            c.DescJa = "泡のような粒をまとったいちご。"; c.DescEn = "A strawberry covered in bubble-like seeds."; c.Likes = CropLikes.Pollen | CropLikes.Rain; c.UnlockLevel = 2; c.UnlockDay = 4;
            c = Crop("kaze_nasubi", "カゼナスビ", "Wind eggplant", CropKind.Regrow, 5, 3, 2, 3, 45, 35, C(0.5f, 0.3f, 0.7f), ItemTags.Vegetable, ItemTags.ColorPurple);
            c.DescJa = "風の日によく育つなす。"; c.DescEn = "An eggplant that thrives on windy days."; c.Likes = CropLikes.Wind; c.FruitShape = PrimitiveType.Capsule; c.UnlockLevel = 3; c.UnlockDay = 8;
            c = Crop("kiritake", "キリタケ", "Mist mushroom", CropKind.Special, 3, 0, 2, 4, 50, 40, C(0.92f, 0.92f, 0.96f), ItemTags.Vegetable, ItemTags.Savory, ItemTags.ColorWhite);
            c.DescJa = "霧や雨の日にぐんぐん伸びるキノコ。"; c.DescEn = "A mushroom that shoots up on misty or rainy days."; c.Likes = CropLikes.Fog | CropLikes.Rain;
            c = Crop("netsu_kosho", "ネツコショウ", "Ember pepper", CropKind.Regrow, 6, 3, 3, 5, 30, 40, C(0.95f, 0.25f, 0.2f), ItemTags.Vegetable, ItemTags.Spicy, ItemTags.ColorRed);
            c.DescJa = "ぴりっと辛い実。暖かいとよく育つ。"; c.DescEn = "Hot little peppers; grow well when warm."; c.Likes = CropLikes.Heat; c.UnlockLevel = 3; c.UnlockDay = 8;
            c = Crop("yuki_lettuce", "ユキレタス", "Snow lettuce", CropKind.Vegetable, 4, 0, 1, 1, 60, 35, C(0.85f, 1f, 0.85f), ItemTags.Vegetable, ItemTags.ColorWhite);
            c.DescJa = "ひんやりした場所を好むレタス。"; c.DescEn = "Lettuce that likes cool spots."; c.Likes = CropLikes.Cool;
            c = Crop("hamon_melon", "ハモンメロン", "Ripple melon", CropKind.FruitTree, 9, 0, 1, 1, 260, 90, C(0.8f, 0.9f, 0.45f), ItemTags.Fruit, ItemTags.Sweet, ItemTags.ColorGreen);
            c.DescJa = "表面に波紋の模様がある大きなメロン。まれに巨大になる。"; c.DescEn = "A big melon with ripple patterns; rarely grows huge."; c.GiantVariantId = "crop.hamon_melon_giant"; c.Likes = CropLikes.Pollen | CropLikes.Sunny; c.UnlockLevel = 5; c.UnlockDay = 14;
            c = Crop("hoshi_corn", "ホシトウモロコシ", "Star corn", CropKind.Grain, 6, 0, 2, 3, 40, 30, C(1f, 0.9f, 0.35f), ItemTags.Grain, ItemTags.Sweet, ItemTags.ColorYellow);
            c.DescJa = "粒が星のように光る穀物。"; c.DescEn = "Grain whose kernels sparkle like stars."; c.FruitShape = PrimitiveType.Capsule; c.Likes = CropLikes.Sunny; c.UnlockLevel = 3; c.UnlockDay = 8;
            c = Crop("yume_asagao", "ユメアサガオ", "Dream glory", CropKind.Flower, 5, 0, 1, 1, 90, 50, C(0.6f, 0.45f, 0.95f), ItemTags.Flower, ItemTags.ColorPurple, ItemTags.Fragrant);
            c.DescJa = "夜に咲く花。光を浴びると光る花びらになることがある。"; c.DescEn = "Blooms at night; with light it may grow glowing petals."; c.Likes = CropLikes.Glow | CropLikes.Pollen;
            c.GlowVariantId = "crop.yume_asagao_glow"; c.UnlockLevel = 5; c.UnlockDay = 14;
            c = Crop("hinata_sou", "ヒナタソウ", "Sunbloom", CropKind.Flower, 4, 0, 1, 1, 60, 30, C(1f, 0.85f, 0.25f), ItemTags.Flower, ItemTags.ColorYellow);
            c.DescJa = "晴れた日にぐんと伸びる黄色い花。"; c.DescEn = "A yellow flower that shoots up on sunny days."; c.Likes = CropLikes.Sunny | CropLikes.Pollen;
            c = Crop("ginka_ju", "ギンカジュ", "Silverfruit tree", CropKind.FruitTree, 12, 4, 3, 5, 70, 300, C(0.82f, 0.85f, 0.92f), ItemTags.Fruit, ItemTags.Sweet, ItemTags.Shiny);
            c.DescJa = "銀色の実がなる木。育つまで長いが、何度も実る。"; c.DescEn = "A tree with silver fruit; slow to grow but fruits many times."; c.Likes = CropLikes.Pollen; c.UnlockLevel = 5; c.UnlockDay = 14;

            foreach (var x in crops)
            {
                bySeed[x.SeedId] = x;
                byCrop[x.CropId] = x;
                if (x.GlowVariantId != null) byCrop[x.GlowVariantId] = x;
                if (x.GiantVariantId != null) byCrop[x.GiantVariantId] = x;
                if (x.RareColorVariantId != null) byCrop[x.RareColorVariantId] = x;
                foreach (var v in x.ColorVariantIds) byCrop[v] = x;
            }

            // 住み場所（農業仕様 §8.1）
            habitats.Add(new HabitatDef { ItemId = "farmhab.pond", NameJa = "小さな池", NameEn = "Little pond", Favored = new[] { CreatureAbility.Water, CreatureAbility.Splash, CreatureAbility.Bubble }, Price = 600, Color = C(0.35f, 0.6f, 0.95f) });
            habitats.Add(new HabitatDef { ItemId = "farmhab.burrow", NameJa = "巣穴の土山", NameEn = "Burrow mound", Favored = new[] { CreatureAbility.Till, CreatureAbility.SoilImprove }, Price = 400, Color = C(0.5f, 0.36f, 0.24f) });
            habitats.Add(new HabitatDef { ItemId = "farmhab.perch", NameJa = "止まり木", NameEn = "Perch", Favored = new[] { CreatureAbility.Sow, CreatureAbility.Float }, Price = 450, Color = C(0.6f, 0.45f, 0.3f) });
            habitats.Add(new HabitatDef { ItemId = "farmhab.fruit_basket", NameJa = "果物かご", NameEn = "Fruit basket", Favored = new[] { CreatureAbility.Harvest, CreatureAbility.SeedCollect }, Price = 450, Color = C(0.8f, 0.55f, 0.3f), UnlockLevel = 2 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.flower_bed", NameJa = "花壇", NameEn = "Flower bed", Favored = new[] { CreatureAbility.Pollinate }, Price = 500, Color = C(1f, 0.6f, 0.8f), UnlockLevel = 2 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.night_pot", NameJa = "夜光草の鉢", NameEn = "Glowgrass pot", Favored = new[] { CreatureAbility.Glow }, Price = 500, Color = C(0.6f, 1f, 0.6f), UnlockLevel = 2 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.hive_box", NameJa = "巣箱", NameEn = "Hive box", Favored = new[] { CreatureAbility.Carry, CreatureAbility.Pack }, Price = 550, Color = C(1f, 0.85f, 0.35f), UnlockLevel = 2 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.warm_bed", NameJa = "ぽかぽか寝床", NameEn = "Warm bed", Favored = new[] { CreatureAbility.Heat }, Price = 550, Color = C(1f, 0.6f, 0.4f), UnlockLevel = 2 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.cool_stone", NameJa = "ひんやり石", NameEn = "Cool stone", Favored = new[] { CreatureAbility.Cool, CreatureAbility.Shade }, Price = 500, Color = C(0.7f, 0.9f, 1f), UnlockLevel = 3 });
            habitats.Add(new HabitatDef { ItemId = "farmhab.hut", NameJa = "生き物小屋", NameEn = "Creature hut", AnyCreature = true, Price = 800, Color = C(0.85f, 0.75f, 0.6f) });

            // 肥料（農業仕様 §9.4）
            fertilizers.Add(new FertilizerDef { ItemId = "fert.basic", NameJa = "ふつうの肥料", NameEn = "Basic fertilizer", DescJa = "作物の品質が上がりやすくなる。", DescEn = "Raises crop quality.", QualityBonus = 1, Price = 30, Color = C(0.55f, 0.45f, 0.3f) });
            fertilizers.Add(new FertilizerDef { ItemId = "fert.growth", NameJa = "成長の肥料", NameEn = "Growth fertilizer", DescJa = "作物が早く育つ。", DescEn = "Crops grow faster.", SpeedBonus = 0.25f, Price = 60, UnlockLevel = 2, Color = C(0.45f, 0.65f, 0.3f) });
            fertilizers.Add(new FertilizerDef { ItemId = "fert.retain", NameJa = "保水の肥料", NameEn = "Moisture fertilizer", DescJa = "水やりした水分が翌日まで残る。", DescEn = "Watering lasts into the next day.", RetainDays = 1, Price = 50, UnlockLevel = 3, Color = C(0.4f, 0.55f, 0.7f) });
            fertilizers.Add(new FertilizerDef { ItemId = "fert.yield", NameJa = "実りの肥料", NameEn = "Harvest fertilizer", DescJa = "収穫量が増える。", DescEn = "Increases harvest yield.", YieldBonus = 1, Price = 90, UnlockLevel = 4, Color = C(0.85f, 0.6f, 0.3f) });
        }

        /// <summary>アイテム登録（RegisterContent から）。必須：SharedItemIds の星蜜果と種。</summary>
        public static void RegisterItems()
        {
            Build();
            foreach (var c in crops)
            {
                var crop = new ItemDefinition
                {
                    Id = c.CropId,
                    NameJa = c.NameJa,
                    NameEn = c.NameEn,
                    DescriptionJa = c.DescJa,
                    DescriptionEn = c.DescEn,
                    Category = ItemCategory.Crop,
                    BasePrice = c.SellPrice,
                    MaxStack = 99,
                    Color = c.Color,
                    Shape = c.FruitShape,
                    Consumable = true,
                    GenericConceptId = c.Kind == CropKind.Flower ? Concepts.FLOWER : c.Tags != null && System.Array.IndexOf(c.Tags, ItemTags.Fruit) >= 0 ? Concepts.FRUIT : c.Kind == CropKind.Grain ? Concepts.CROP : Concepts.VEGETABLE,
                    SourceActivity = "farming",
                    ModuleData = c
                };
                crop.WithTags(c.Tags);
                crop.WithTags(ItemTags.Ingredient);
                if (c.Kind != CropKind.Flower) crop.WithTags(ItemTags.Food, ItemTags.Edible);
                else crop.WithTags(ItemTags.Gift, ItemTags.Plant);
                if (c.GlowsAtNight) crop.WithTags(ItemTags.Glowing);
                ItemRegistry.Register(crop);

                var seed = new ItemDefinition
                {
                    Id = c.SeedId,
                    NameJa = c.NameJa + (c.Kind == CropKind.FruitTree ? "の苗" : "の種"),
                    NameEn = c.NameEn + (c.Kind == CropKind.FruitTree ? " sapling" : " seeds"),
                    DescriptionJa = "畑に植えると" + c.NameJa + "が育つ（約" + c.GrowDays + "日）。" + (c.IsRegrow ? "何度も収穫できる。" : ""),
                    DescriptionEn = "Plant in a tilled plot to grow " + c.NameEn + " (~" + c.GrowDays + " days)." + (c.IsRegrow ? " Regrows after harvest." : ""),
                    Category = ItemCategory.Seed,
                    BasePrice = c.SeedPrice,
                    MaxStack = 99,
                    Color = Color.Lerp(c.Color, new Color(0.7f, 0.6f, 0.4f), 0.5f),
                    Shape = PrimitiveType.Cube,
                    GenericConceptId = Concepts.SEED,
                    SourceActivity = "farming",
                    ModuleData = c
                };
                seed.WithTags(ItemTags.Seed);
                ItemRegistry.Register(seed);
            }

            // 変種（農業仕様 §7.1・§7.2）
            Variant("hoshiberry", "crop.hoshiberry_glow", "光るホシベリー", "Glowing starberry", "夜光る生き物のそばで育ち、昼でも光る珍しい実。", "A rare berry grown near glowing creatures; glows even by day.", 120, new Color(0.45f, 0.8f, 1f), ItemTags.Glowing, ItemTags.Rare);
            Variant("yume_asagao", "crop.yume_asagao_glow", "光るユメアサガオ", "Glowing dream glory", "花びらが淡く光る珍しいユメアサガオ。", "A rare dream glory with glowing petals.", 260, new Color(0.8f, 0.6f, 1f), ItemTags.Glowing, ItemTags.Rare);
            Variant("bell_flower", "crop.bell_flower_rainbow", "虹色ベルフラワー", "Rainbow bellflower", "夜の受粉と光が重なって咲いた、虹色の花。", "A rainbow flower born from night pollination and light.", 300, new Color(1f, 0.7f, 0.9f), ItemTags.Rare, ItemTags.Shiny);
            Variant("shimashima_imo", "crop.shimashima_imo_giant", "巨大しましまイモ", "Giant stripe potato", "抱えるほど大きく育ったしましまイモ。", "A stripe potato grown huge.", 260, new Color(0.62f, 0.42f, 0.7f), ItemTags.Big, ItemTags.Rare);
            Variant("purupuru_cabbage", "crop.purupuru_cabbage_giant", "巨大プルプルキャベツ", "Giant wobble cabbage", "ぷるんぷるんに大きく育ったキャベツ。", "A huge, extra-wobbly cabbage.", 320, new Color(0.6f, 0.9f, 0.5f), ItemTags.Big, ItemTags.Rare);
            Variant("hamon_melon", "crop.hamon_melon_giant", "巨大ハモンメロン", "Giant ripple melon", "波紋の模様がくっきり出た、巨大なメロン。", "A giant melon with vivid ripples.", 700, new Color(0.8f, 0.9f, 0.45f), ItemTags.Big, ItemTags.Rare);
            Variant("niji_ninjin", "crop.niji_ninjin_red", "赤いニジニンジン", "Red rainbow carrot", "赤く染まったニジニンジン。", "A red rainbow carrot.", 60, new Color(0.95f, 0.3f, 0.3f), ItemTags.ColorRed);
            Variant("niji_ninjin", "crop.niji_ninjin_yellow", "黄色いニジニンジン", "Yellow rainbow carrot", "黄色く染まったニジニンジン。", "A yellow rainbow carrot.", 60, new Color(1f, 0.9f, 0.3f), ItemTags.ColorYellow);
            Variant("niji_ninjin", "crop.niji_ninjin_purple", "紫のニジニンジン", "Purple rainbow carrot", "紫に染まったニジニンジン。", "A purple rainbow carrot.", 75, new Color(0.6f, 0.35f, 0.85f), ItemTags.ColorPurple);

            foreach (var f in fertilizers)
            {
                var def = new ItemDefinition
                {
                    Id = f.ItemId, NameJa = f.NameJa, NameEn = f.NameEn, DescriptionJa = f.DescJa, DescriptionEn = f.DescEn,
                    Category = ItemCategory.Fertilizer, BasePrice = f.Price, MaxStack = 99, Color = f.Color, Shape = PrimitiveType.Cube, SourceActivity = "farming"
                };
                ItemRegistry.Register(def);
            }
            foreach (var h in habitats)
            {
                var def = new ItemDefinition
                {
                    Id = h.ItemId, NameJa = h.NameJa, NameEn = h.NameEn,
                    DescriptionJa = "農場に置く生き物の住み場所。手に持って農場の空いた地面で使う。好きな生き物が住むとよく働く。",
                    DescriptionEn = "A creature home for the farm. Hold it and use on open farm ground. Creatures that like it work better.",
                    Category = ItemCategory.CreatureItem, BasePrice = h.Price, MaxStack = 10, Color = h.Color, Shape = PrimitiveType.Cylinder, SourceActivity = "farming"
                };
                def.WithTags(ItemTags.CreatureHabitat, ItemTags.Outdoor, "farm_habitat");
                ItemRegistry.Register(def);
            }
        }

        private static void Variant(string baseKey, string id, string ja, string en, string dja, string den, int price, Color color, params string[] extraTags)
        {
            var b = Get(baseKey);
            var def = new ItemDefinition
            {
                Id = id, NameJa = ja, NameEn = en, DescriptionJa = dja, DescriptionEn = den,
                Category = ItemCategory.Crop, BasePrice = price, MaxStack = 99, Color = color, Shape = b != null ? b.FruitShape : PrimitiveType.Sphere,
                Consumable = true, ConceptId = null, GenericConceptId = b != null && b.Kind == CropKind.Flower ? Concepts.FLOWER : Concepts.CROP, SourceActivity = "farming", ModuleData = b
            };
            if (b != null)
            {
                foreach (var t in b.Tags)
                {
                    if (t.StartsWith("color_") && System.Array.Exists(extraTags, x => x.StartsWith("color_"))) continue;
                    def.WithTags(t);
                }
                if (b.Kind != CropKind.Flower) def.WithTags(ItemTags.Food, ItemTags.Edible);
            }
            def.WithTags(ItemTags.Ingredient);
            def.WithTags(extraTags);
            ItemRegistry.Register(def);
        }
    }
}
