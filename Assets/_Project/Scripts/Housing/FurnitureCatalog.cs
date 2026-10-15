using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Housing
{
    /// <summary>家具を使った時の行動（家具仕様 §6）。</summary>
    public enum FurnitureUse
    {
        None,
        Sit,        // 座る・くつろぐ
        Sleep,      // 眠る（翌朝への時間送りだけ。時間・天候仕様 §3）
        Light,      // 灯りを点ける・消す
        Music,      // 演奏する
        Aquarium,   // 水槽を眺める・魚を入れる
        Storage,    // 収納
        Display,    // 展示（鉱石・化石・小物）
        Table,      // 食卓・机（料理や小物を置く）
        Toy,        // 遊ぶ
        Plant,      // 植物を眺める
        Book,       // 本を読む
        Window,     // 外を眺める（お天気窓）
        Perch,      // マネピの止まり木（伝言を聞く）
        Habitat,    // 生物の住処
        Mirror,     // 鏡（表情を映す）
        SoundFloor, // 音のなる床
        Clock,      // 時計（時刻を見る）
        Workbench   // 作業台（加工）
    }

    /// <summary>置ける場所（家具仕様 §5.1：床・壁・天井・テーブルの上・庭）。</summary>
    public enum FurnitureSurface
    {
        Floor,
        Rug,      // 床に敷く（他の家具の下に置ける）
        Wall,     // 壁に掛ける
        Tabletop, // テーブル・机の上
        Ceiling   // 天井から吊る
    }

    /// <summary>家具シリーズ（家具仕様 §17）。</summary>
    public enum FurnitureSeries
    {
        Basic,
        Natural,
        Country,
        Retro,
        Manmaru,
        Mochi,
        Starry,
        Crystal,
        Floating,
        Luxury,
        Creature,
        Antique,
        AlienTradition,
        Garden,
        Interior
    }

    /// <summary>簡易モデルの形（FurnitureVisuals が組み立てる）。</summary>
    public enum FurnitureStyle
    {
        Generic, Chair, Stool, Sofa, Cushion, Bed, Table, RoundTable, Desk, Shelf, Cabinet, Wardrobe, Chest, Box,
        Lamp, FloorLamp, Chandelier, Mobile, Aquarium, WaterOrb, Plant, BigPlant, Tree, FlowerBed, Rug, Painting,
        Clock, Window, RoundWindow, Curtain, Piano, Harp, Xylophone, Drum, Gramophone, Ball, Blocks, Bench, Fence,
        Path, Pond, Streetlight, Sign, Rocks, Perch, Mirror, Swing, Pedestal, Bookshelf, Vase, Photo, Figurine,
        Rocker, Playground, Nest, WindChime, SoundTiles, Birdhouse, ToolRack, Workbench
    }

    /// <summary>飾れる物の種類。</summary>
    public enum DisplayKind
    {
        None,
        Fish,     // 水槽：魚
        Mineral,  // 鉱石・宝石・化石
        Food,     // 食卓：料理
        Any,      // 何でも
        Plant     // 花・作物
    }

    /// <summary>
    /// 家具の仕様（ItemDefinition.ModuleData に入れる）。他モジュールが登録した家具（Cooking の調理器具など）は
    /// FurnitureCatalog.Resolve がタグから既定の仕様を作るので、どの Furniture/Decor アイテムでも置ける。
    /// </summary>
    public class FurnitureSpec
    {
        public int w = 2;
        public int d = 2;
        public float height = 0.8f;
        public FurnitureSurface surface = FurnitureSurface.Floor;
        public FurnitureUse use = FurnitureUse.None;
        public FurnitureStyle style = FurnitureStyle.Generic;
        public FurnitureSeries series = FurnitureSeries.Basic;
        /// <summary>訪問者が座れる数。</summary>
        public int seats;
        public float seatHeight = 0.45f;
        public int storageSlots;
        public int displaySlots;
        public DisplayKind displayKind = DisplayKind.None;
        /// <summary>上に小物（Tabletop）を置ける。</summary>
        public bool tabletopHost;
        public bool outdoorOnly;
        public bool allowOutdoor;
        public bool allowWater;
        public bool floating;
        public bool glowing;
        public bool soft;
        public bool wobble;
        public bool shapeShift;
        public Color accent = Color.white;
        /// <summary>住処になる生物の番号（"031" など。生物仕様の No.）。</summary>
        public string[] habitatSpecies = new string[0];
        /// <summary>住処として好む能力（種族IDが分からない場合の判定）。</summary>
        public CreatureAbility[] habitatAbilities = new CreatureAbility[0];
        /// <summary>10系統の宇宙人にちなむ家具（§8）。-1 ならなし。</summary>
        public int lineage = -1;
        /// <summary>他モジュールの家具からタグで作った既定仕様か。</summary>
        public bool derived;

        public bool IsHabitat => habitatSpecies.Length > 0 || habitatAbilities.Length > 0;
        public FurnitureSpec Seats(int n) { seats = n; return this; }
        public FurnitureSpec Soft() { soft = true; return this; }
        public FurnitureSpec Wobble() { wobble = true; soft = true; return this; }
        public FurnitureSpec Float() { floating = true; return this; }
        public FurnitureSpec Glow() { glowing = true; return this; }
        public FurnitureSpec Host() { tabletopHost = true; return this; }
        public FurnitureSpec Outdoor(bool only = false) { allowOutdoor = true; outdoorOnly = only; return this; }
        public FurnitureSpec Water() { allowWater = true; return this; }
        public FurnitureSpec Store(int slots) { storageSlots = slots; return this; }
        public FurnitureSpec Shows(DisplayKind k, int slots) { displayKind = k; displaySlots = slots; return this; }
        public FurnitureSpec Accent(Color c) { accent = c; return this; }
        public FurnitureSpec Lineage(AlienLineage l) { lineage = (int)l; return this; }
        public FurnitureSpec SeatH(float h) { seatHeight = h; return this; }
        public FurnitureSpec Habitat(string[] species, params CreatureAbility[] abilities)
        {
            habitatSpecies = species ?? new string[0];
            habitatAbilities = abilities ?? new CreatureAbility[0];
            return this;
        }
    }

    /// <summary>壁紙・床材（家具仕様 §4 壁・床、§5.1）。</summary>
    public class DecorSpec
    {
        public bool isWallpaper;
        public Color baseColor;
        public Color accent;
        /// <summary>"plain" "stripe" "dots" "stars" "flowers" "crystal" "brick" "planks" "tiles" "water" "grass" "fluffy"</summary>
        public string pattern = "plain";
        public bool glow;
    }

    /// <summary>
    /// 家具カタログの登録と、星のシードによる家具文化（§18）。
    /// 数値（価格・大きさ）はすべて【仮数値】。
    /// </summary>
    public static class FurnitureCatalog
    {
        public const string LumberId = "housing.lumber";
        public const string StoneBlockId = "housing.stone_block";
        public const string GlassPaneId = "housing.glass_pane";
        public const string StarterBedId = "furniture.starter_bed";
        public const string StarterChestId = "furniture.starter_chest";
        public const string StarterDeskId = "furniture.starter_desk";
        public const string StarterChairId = "furniture.starter_chair";

        private static readonly List<ItemDefinition> furniture = new List<ItemDefinition>();
        private static readonly List<ItemDefinition> decor = new List<ItemDefinition>();
        private static readonly Dictionary<string, FurnitureSpec> derivedCache = new Dictionary<string, FurnitureSpec>();
        private static bool registered;

        public static IReadOnlyList<ItemDefinition> OwnFurniture => furniture;
        public static IReadOnlyList<ItemDefinition> OwnDecor => decor;

        // ------------------------------------------------------------
        // 判定
        // ------------------------------------------------------------

        /// <summary>家に置けるアイテムか（Furniture/Decor の全アイテム。他モジュールの調理器具・生物の住処も含む）。</summary>
        public static bool IsPlaceable(ItemDefinition def)
        {
            if (def == null) return false;
            if (def.ModuleData is DecorSpec) return false; // 壁紙・床材は「貼る」もの
            if (def.Category == ItemCategory.Furniture || def.Category == ItemCategory.Decor) return true;
            if (def.HasTag(ItemTags.Furniture)) return true;
            if (def.Category == ItemCategory.CreatureItem && def.HasTag(ItemTags.CreatureHabitat)) return true;
            return false;
        }

        public static bool IsSurfaceDecor(ItemDefinition def, bool wallpaper)
        {
            return def != null && def.ModuleData is DecorSpec ds && ds.isWallpaper == wallpaper;
        }

        public static DecorSpec GetDecor(string itemId) => ItemRegistry.Get(itemId)?.ModuleData as DecorSpec;

        /// <summary>家具の仕様。自分で登録した物はそのまま、他モジュールの物はタグから既定値を作る。</summary>
        public static FurnitureSpec Resolve(ItemDefinition def)
        {
            if (def == null) return new FurnitureSpec { derived = true };
            if (def.ModuleData is FurnitureSpec fs) return fs;
            if (derivedCache.TryGetValue(def.Id, out var cached)) return cached;
            var s = new FurnitureSpec { derived = true, series = FurnitureSeries.Basic };
            if (def.HasTag(ItemTags.Kitchen)) { s.use = FurnitureUse.None; s.style = FurnitureStyle.Workbench; s.w = 2; s.d = 2; s.height = 0.9f; s.tabletopHost = true; }
            else if (def.HasTag(ItemTags.Bed)) { s.use = FurnitureUse.Sleep; s.style = FurnitureStyle.Bed; s.w = 3; s.d = 4; s.height = 0.5f; }
            else if (def.HasTag(ItemTags.Seating)) { s.use = FurnitureUse.Sit; s.style = FurnitureStyle.Chair; s.w = 1; s.d = 1; s.height = 0.9f; s.seats = 1; }
            else if (def.HasTag(ItemTags.Table)) { s.use = FurnitureUse.Table; s.style = FurnitureStyle.Table; s.w = 4; s.d = 3; s.height = 0.75f; s.tabletopHost = true; s.displayKind = DisplayKind.Food; s.displaySlots = 4; }
            else if (def.HasTag(ItemTags.Aquarium)) { s.use = FurnitureUse.Aquarium; s.style = FurnitureStyle.Aquarium; s.w = 3; s.d = 2; s.height = 1.0f; s.displayKind = DisplayKind.Fish; s.displaySlots = 4; s.allowWater = true; }
            else if (def.HasTag(ItemTags.Light)) { s.use = FurnitureUse.Light; s.style = FurnitureStyle.Lamp; s.w = 1; s.d = 1; s.height = 1.0f; s.glowing = true; }
            else if (def.HasTag(ItemTags.Storage)) { s.use = FurnitureUse.Storage; s.style = FurnitureStyle.Chest; s.storageSlots = 24; s.w = 2; s.d = 2; s.height = 0.7f; }
            else if (def.HasTag(ItemTags.Music)) { s.use = FurnitureUse.Music; s.style = FurnitureStyle.Drum; s.w = 2; s.d = 2; s.height = 0.8f; }
            else if (def.HasTag(ItemTags.Toy)) { s.use = FurnitureUse.Toy; s.style = FurnitureStyle.Ball; s.w = 1; s.d = 1; s.height = 0.35f; }
            else if (def.HasTag(ItemTags.Plant) || def.HasTag(ItemTags.Flower)) { s.use = FurnitureUse.Plant; s.style = FurnitureStyle.Plant; s.w = 1; s.d = 1; s.height = 0.8f; s.allowOutdoor = true; }
            else if (def.HasTag(ItemTags.CreatureHabitat)) { s.use = FurnitureUse.Habitat; s.style = FurnitureStyle.Nest; s.w = 2; s.d = 2; s.height = 0.4f; s.allowOutdoor = true; }
            else { s.use = FurnitureUse.None; s.style = FurnitureStyle.Generic; s.w = 2; s.d = 2; s.height = 0.8f; }
            if (def.HasTag(ItemTags.Outdoor)) s.allowOutdoor = true;
            if (def.HasTag(ItemTags.Glowing)) s.glowing = true;
            if (def.HasTag(ItemTags.CreatureHabitat) && !s.IsHabitat)
                s.habitatAbilities = new[] { CreatureAbility.Pack, CreatureAbility.Heat, CreatureAbility.Water, CreatureAbility.Glow };
            if (def.HasTag(ItemTags.Big)) { s.w += 1; s.d += 1; }
            derivedCache[def.Id] = s;
            return s;
        }

        public static string SeriesLabel(FurnitureSeries s)
        {
            switch (s)
            {
                case FurnitureSeries.Natural: return Loc.T("ナチュラル", "Natural");
                case FurnitureSeries.Country: return Loc.T("カントリー", "Country");
                case FurnitureSeries.Retro: return Loc.T("レトロ", "Retro");
                case FurnitureSeries.Manmaru: return Loc.T("まんまる", "Roundy");
                case FurnitureSeries.Mochi: return Loc.T("もちもち", "Mochi");
                case FurnitureSeries.Starry: return Loc.T("星空", "Starry");
                case FurnitureSeries.Crystal: return Loc.T("クリスタル", "Crystal");
                case FurnitureSeries.Floating: return Loc.T("浮遊", "Floating");
                case FurnitureSeries.Luxury: return Loc.T("高級", "Luxury");
                case FurnitureSeries.Creature: return Loc.T("生物", "Creature");
                case FurnitureSeries.Antique: return Loc.T("アンティーク", "Antique");
                case FurnitureSeries.AlienTradition: return Loc.T("宇宙人の伝統", "Alien tradition");
                case FurnitureSeries.Garden: return Loc.T("庭", "Garden");
                case FurnitureSeries.Interior: return Loc.T("内装", "Interior");
                default: return Loc.T("ふつう", "Basic");
            }
        }

        public static string GenericConcept(FurnitureSpec s, ItemDefinition def)
        {
            switch (s.use)
            {
                case FurnitureUse.Sit: return Concepts.CHAIR;
                case FurnitureUse.Sleep: return Concepts.BED;
                case FurnitureUse.Table: return Concepts.TABLE;
                case FurnitureUse.Light: return Concepts.LAMP;
                case FurnitureUse.Storage: return Concepts.BOX;
                case FurnitureUse.Music: return Concepts.MUSIC;
                case FurnitureUse.Plant: return Concepts.FLOWER;
            }
            if (s.style == FurnitureStyle.Painting || s.style == FurnitureStyle.Photo) return Concepts.PICTURE;
            if (s.style == FurnitureStyle.Box) return Concepts.BOX;
            if (s.style == FurnitureStyle.Tree) return Concepts.TREE;
            return Concepts.FURNITURE;
        }

        // ------------------------------------------------------------
        // 登録（RegisterContent から1回）
        // ------------------------------------------------------------

        public static void RegisterAll()
        {
            if (registered) return;
            registered = true;
            furniture.Clear();
            decor.Clear();

            RegisterMaterials();

            var wood = new Color(0.72f, 0.52f, 0.32f);
            var lightWood = new Color(0.86f, 0.70f, 0.48f);
            var darkWood = new Color(0.45f, 0.30f, 0.20f);
            var cream = new Color(0.97f, 0.92f, 0.82f);
            var pink = new Color(1f, 0.72f, 0.80f);
            var mint = new Color(0.62f, 0.92f, 0.80f);
            var sky = new Color(0.55f, 0.75f, 1f);
            var night = new Color(0.18f, 0.20f, 0.42f);
            var gold = new Color(1f, 0.84f, 0.35f);
            var crystal = new Color(0.70f, 0.90f, 1f);
            var lilac = new Color(0.78f, 0.66f, 0.95f);
            var leaf = new Color(0.35f, 0.70f, 0.38f);
            var stone = new Color(0.62f, 0.62f, 0.66f);
            var water = new Color(0.40f, 0.70f, 0.95f, 0.55f);

            // ---- 初期家具（§2：寝具・収納・机と椅子）----
            F(StarterBedId, "小さなベッド", "Small bed", "村の宇宙人が貸してくれた小さな寝床。", "A small bed lent by a villager.", 600, cream,
                FurnitureSeries.Basic, FurnitureStyle.Bed, FurnitureUse.Sleep, 2, 4, 0.5f, ItemTags.Bed, ItemTags.ColorWhite).SeatH(0.5f);
            F(StarterChestId, "収納箱", "Storage chest", "たくさん入る木の箱。家の共有の収納。", "A roomy wooden chest.", 400, wood,
                FurnitureSeries.Basic, FurnitureStyle.Chest, FurnitureUse.Storage, 2, 2, 0.6f, ItemTags.Storage).Store(HousingConst.StarterChestSlots);
            F(StarterDeskId, "小さな机", "Small desk", "物を置いたり書き物をしたりする机。", "A desk for writing and small things.", 350, lightWood,
                FurnitureSeries.Basic, FurnitureStyle.Desk, FurnitureUse.Table, 3, 2, 0.75f, ItemTags.Table).Host().Shows(DisplayKind.Food, 2);
            F(StarterChairId, "木の丸椅子", "Wooden stool", "素朴な丸椅子。", "A simple round stool.", 200, wood,
                FurnitureSeries.Basic, FurnitureStyle.Stool, FurnitureUse.Sit, 1, 1, 0.45f, ItemTags.Seating, ItemTags.Small).Seats(1);

            // ---- ナチュラル ----
            F("furniture.wood_chair", "木の椅子", "Wooden chair", "背もたれ付きの木の椅子。", "A wooden chair with a back.", 450, wood,
                FurnitureSeries.Natural, FurnitureStyle.Chair, FurnitureUse.Sit, 1, 1, 0.95f, ItemTags.Seating).Seats(1);
            F("furniture.wood_table", "木のテーブル", "Wooden table", "家族で囲める木のテーブル。料理を並べられる。", "A wooden table for meals.", 900, wood,
                FurnitureSeries.Natural, FurnitureStyle.Table, FurnitureUse.Table, 4, 3, 0.75f, ItemTags.Table).Host().Shows(DisplayKind.Food, 4);
            F("furniture.wood_bed", "木のベッド", "Wooden bed", "ぐっすり眠れる木のベッド。", "A cozy wooden bed.", 1500, lightWood,
                FurnitureSeries.Natural, FurnitureStyle.Bed, FurnitureUse.Sleep, 3, 4, 0.55f, ItemTags.Bed).Accent(new Color(0.6f, 0.8f, 0.6f));
            F("furniture.wood_shelf", "木の飾り棚", "Wooden display shelf", "小物や鉱石を並べて飾れる棚。", "A shelf to display small things.", 700, wood,
                FurnitureSeries.Natural, FurnitureStyle.Shelf, FurnitureUse.Display, 3, 1, 1.4f).Shows(DisplayKind.Any, 4);
            F("furniture.potted_plant", "鉢植え", "Potted plant", "小さな葉の鉢植え。", "A small potted plant.", 250, leaf,
                FurnitureSeries.Natural, FurnitureStyle.Plant, FurnitureUse.Plant, 1, 1, 0.7f, ItemTags.Plant, ItemTags.ColorGreen).Outdoor();
            F("furniture.big_plant", "大きな観葉植物", "Big houseplant", "天井まで届きそうな葉。", "Leaves reaching for the ceiling.", 600, leaf,
                FurnitureSeries.Natural, FurnitureStyle.BigPlant, FurnitureUse.Plant, 2, 2, 1.6f, ItemTags.Plant, ItemTags.Big, ItemTags.ColorGreen).Outdoor();

            // ---- カントリー ----
            F("furniture.country_sofa", "カントリーソファ", "Country sofa", "チェック柄の3人掛けソファ。", "A checkered three-seat sofa.", 1800, new Color(0.85f, 0.45f, 0.35f),
                FurnitureSeries.Country, FurnitureStyle.Sofa, FurnitureUse.Sit, 4, 2, 0.85f, ItemTags.Seating, ItemTags.ColorRed).Seats(3).Soft();
            F("furniture.log_stool", "丸太のスツール", "Log stool", "丸太を切っただけの腰掛け。", "A stool cut from a log.", 180, darkWood,
                FurnitureSeries.Country, FurnitureStyle.Stool, FurnitureUse.Sit, 1, 1, 0.45f, ItemTags.Seating).Seats(1).Outdoor();
            F("furniture.knit_rug", "手編みのラグ", "Knitted rug", "やわらかい手編みの敷物。", "A soft knitted rug.", 500, new Color(0.95f, 0.75f, 0.45f),
                FurnitureSeries.Country, FurnitureStyle.Rug, FurnitureUse.None, 4, 3, 0.03f, ItemTags.ColorOrange).Soft();
            F("furniture.rocking_chair", "ロッキングチェア", "Rocking chair", "ゆらゆら揺れる椅子。", "A chair that rocks.", 1200, wood,
                FurnitureSeries.Country, FurnitureStyle.Rocker, FurnitureUse.Sit, 2, 2, 1.0f, ItemTags.Seating).Seats(1);
            F("furniture.cupboard", "食器棚", "Cupboard", "食器や食材をしまえる棚。", "A cupboard for dishes and food.", 1100, lightWood,
                FurnitureSeries.Country, FurnitureStyle.Cabinet, FurnitureUse.Storage, 3, 1, 1.6f, ItemTags.Storage).Store(24);

            // ---- レトロ ----
            F("furniture.retro_lamp", "レトロなランプ", "Retro lamp", "机に置く、橙色の灯り。", "An orange desk lamp.", 650, new Color(1f, 0.7f, 0.35f),
                FurnitureSeries.Retro, FurnitureStyle.Lamp, FurnitureUse.Light, 1, 1, 0.5f, ItemTags.Light, ItemTags.ColorOrange).Glow();
            Last.surface = FurnitureSurface.Tabletop;
            F("furniture.pendulum_clock", "振り子時計", "Pendulum clock", "コチコチと時を刻む壁掛け時計。", "A ticking wall clock.", 1400, darkWood,
                FurnitureSeries.Retro, FurnitureStyle.Clock, FurnitureUse.Clock, 2, 1, 0.9f);
            Last.surface = FurnitureSurface.Wall;
            F("furniture.gramophone", "蓄音機", "Gramophone", "大きなラッパから音楽が流れる。", "Music flows from a big horn.", 2200, gold,
                FurnitureSeries.Retro, FurnitureStyle.Gramophone, FurnitureUse.Music, 2, 2, 1.0f, ItemTags.Music, ItemTags.Shiny);
            F("furniture.old_wardrobe", "古いタンス", "Old wardrobe", "引き出しがたくさんあるタンス。", "A wardrobe with many drawers.", 1600, darkWood,
                FurnitureSeries.Retro, FurnitureStyle.Wardrobe, FurnitureUse.Storage, 3, 2, 1.8f, ItemTags.Storage).Store(30);

            // ---- まんまる ----
            F("furniture.manmaru_cushion", "まんまるクッション", "Round cushion", "ころんとした丸いクッション。", "A round, plump cushion.", 380, pink,
                FurnitureSeries.Manmaru, FurnitureStyle.Cushion, FurnitureUse.Sit, 2, 2, 0.3f, ItemTags.Seating, ItemTags.Cute, ItemTags.ColorPink).Seats(1).Soft().SeatH(0.3f).Lineage(AlienLineage.Round);
            F("furniture.manmaru_table", "まんまるテーブル", "Round table", "角のない丸いテーブル。", "A table with no corners.", 950, cream,
                FurnitureSeries.Manmaru, FurnitureStyle.RoundTable, FurnitureUse.Table, 3, 3, 0.7f, ItemTags.Table, ItemTags.Cute).Host().Shows(DisplayKind.Food, 4);
            F("furniture.manmaru_lamp", "まんまるランプ", "Round lamp", "まんまるの灯りがぽわっと光る。", "A round glowing lamp.", 700, new Color(1f, 0.95f, 0.7f),
                FurnitureSeries.Manmaru, FurnitureStyle.FloorLamp, FurnitureUse.Light, 1, 1, 1.2f, ItemTags.Light, ItemTags.Cute).Glow();
            F("furniture.manmaru_bed", "まんまるベッド", "Round bed", "丸い形のふかふかベッド。", "A round, fluffy bed.", 2400, pink,
                FurnitureSeries.Manmaru, FurnitureStyle.Bed, FurnitureUse.Sleep, 4, 4, 0.5f, ItemTags.Bed, ItemTags.Cute, ItemTags.ColorPink).Soft();

            // ---- もちもち ----
            F("furniture.purupuru_cushion", "ぷるぷるクッション", "Jiggly cushion", "座ると大きく沈んでぷるんと揺れる。", "Sinks deep and jiggles when you sit.", 900, mint,
                FurnitureSeries.Mochi, FurnitureStyle.Cushion, FurnitureUse.Sit, 2, 2, 0.35f, ItemTags.Seating, ItemTags.Cute, ItemTags.ColorGreen).Seats(1).Wobble().SeatH(0.25f);
            F("furniture.mochi_sofa", "もちもちソファ", "Mochi sofa", "おもちのように伸び縮みするソファ。", "A sofa that stretches like mochi.", 2600, new Color(0.98f, 0.95f, 0.9f),
                FurnitureSeries.Mochi, FurnitureStyle.Sofa, FurnitureUse.Sit, 4, 2, 0.8f, ItemTags.Seating, ItemTags.ColorWhite).Seats(3).Wobble().Lineage(AlienLineage.Mochi);
            F("furniture.mochi_stool", "もちもちスツール", "Mochi stool", "ぷにっとした腰掛け。", "A squishy stool.", 420, new Color(1f, 0.85f, 0.9f),
                FurnitureSeries.Mochi, FurnitureStyle.Stool, FurnitureUse.Sit, 1, 1, 0.42f, ItemTags.Seating, ItemTags.Cute).Seats(1).Wobble();

            // ---- 星空 ----
            F("furniture.starry_bed", "星空のベッド", "Starry bed", "布団に星座が瞬く。", "Constellations twinkle on the quilt.", 3800, night,
                FurnitureSeries.Starry, FurnitureStyle.Bed, FurnitureUse.Sleep, 3, 4, 0.55f, ItemTags.Bed, ItemTags.Glowing, ItemTags.ColorBlue).Glow().Accent(gold);
            F("furniture.moon_lamp", "月のランプ", "Moon lamp", "まるい月が静かに光る。", "A round moon glows softly.", 1300, new Color(1f, 0.97f, 0.8f),
                FurnitureSeries.Starry, FurnitureStyle.FloorLamp, FurnitureUse.Light, 1, 1, 1.3f, ItemTags.Light, ItemTags.Glowing).Glow();
            F("furniture.constellation_rug", "星座のラグ", "Constellation rug", "星の並びが刺繍された敷物。", "A rug embroidered with stars.", 1500, night,
                FurnitureSeries.Starry, FurnitureStyle.Rug, FurnitureUse.None, 5, 4, 0.03f, ItemTags.ColorBlue).Glow().Accent(gold);
            F("furniture.star_mobile", "星のつり飾り", "Star mobile", "天井でくるくる回る星の飾り。", "Stars spinning from the ceiling.", 1100, gold,
                FurnitureSeries.Starry, FurnitureStyle.Mobile, FurnitureUse.None, 2, 2, 0.6f, ItemTags.Shiny, ItemTags.ColorYellow).Glow();
            Last.surface = FurnitureSurface.Ceiling;

            // ---- クリスタル ----
            F("furniture.star_crystal_lamp", "星結晶ランプ", "Star crystal lamp", "鉱山の結晶を使った灯り。飾った鉱石で光の色が変わる。", "A lamp of mine crystal; its color follows the mineral you set in it.", 2800, crystal,
                FurnitureSeries.Crystal, FurnitureStyle.Lamp, FurnitureUse.Light, 1, 1, 0.9f, ItemTags.Light, ItemTags.Crystal, ItemTags.Glowing, ItemTags.Shiny).Glow().Shows(DisplayKind.Mineral, 1);
            F("furniture.crystal_pedestal", "結晶の展示台", "Crystal pedestal", "宝石や鉱石を一つ飾る台。", "A stand for one gem or ore.", 1800, crystal,
                FurnitureSeries.Crystal, FurnitureStyle.Pedestal, FurnitureUse.Display, 2, 2, 1.0f, ItemTags.Crystal, ItemTags.Shiny).Shows(DisplayKind.Mineral, 1);
            F("furniture.crystal_table", "結晶のテーブル", "Crystal table", "透き通った天板のテーブル。", "A table with a clear crystal top.", 4200, crystal,
                FurnitureSeries.Crystal, FurnitureStyle.Table, FurnitureUse.Table, 4, 3, 0.75f, ItemTags.Table, ItemTags.Crystal, ItemTags.Shiny, ItemTags.Rare).Host().Shows(DisplayKind.Food, 4);

            // ---- 浮遊 ----
            F("furniture.floating_bed", "ふわふわ浮遊ベッド", "Floating bed", "少し宙に浮いていて、寝るとゆっくり揺れる。", "Hovers slightly and sways as you sleep.", 6500, new Color(0.85f, 0.9f, 1f),
                FurnitureSeries.Floating, FurnitureStyle.Bed, FurnitureUse.Sleep, 3, 4, 0.5f, ItemTags.Bed, ItemTags.Rare).Float().Soft().SeatH(0.8f);
            F("furniture.floating_shelf", "浮遊展示棚", "Floating shelf", "飾った物がゆっくり回りながら浮く。", "Displayed things float and spin.", 3200, lilac,
                FurnitureSeries.Floating, FurnitureStyle.Shelf, FurnitureUse.Display, 3, 1, 1.5f, ItemTags.Rare, ItemTags.ColorPurple).Float().Shows(DisplayKind.Any, 3);
            F("furniture.floating_lantern", "浮遊ランタン", "Floating lantern", "ふわふわ漂う小さな灯り。", "A small drifting light.", 1600, new Color(1f, 0.85f, 0.6f),
                FurnitureSeries.Floating, FurnitureStyle.Lamp, FurnitureUse.Light, 1, 1, 1.4f, ItemTags.Light, ItemTags.Glowing).Glow().Float().Outdoor();

            // ---- 高級 ----
            F("furniture.luxury_sofa", "高級ソファ", "Luxury sofa", "金の縁取りの大きなソファ。", "A large sofa with gold trim.", 9800, new Color(0.55f, 0.18f, 0.30f),
                FurnitureSeries.Luxury, FurnitureStyle.Sofa, FurnitureUse.Sit, 5, 2, 0.9f, ItemTags.Seating, ItemTags.Rare, ItemTags.Big, ItemTags.ColorRed).Seats(4).Soft().Accent(gold);
            F("furniture.grand_dining_table", "大きな食卓", "Grand dining table", "みんなで食事ができる長い食卓。", "A long table for big meals.", 7600, darkWood,
                FurnitureSeries.Luxury, FurnitureStyle.Table, FurnitureUse.Table, 6, 4, 0.78f, ItemTags.Table, ItemTags.Big).Host().Shows(DisplayKind.Food, 6);
            F("furniture.chandelier", "豪華なシャンデリア", "Chandelier", "天井できらめく灯り。", "A sparkling ceiling light.", 12000, gold,
                FurnitureSeries.Luxury, FurnitureStyle.Chandelier, FurnitureUse.Light, 3, 3, 0.9f, ItemTags.Light, ItemTags.Shiny, ItemTags.Rare).Glow();
            Last.surface = FurnitureSurface.Ceiling;
            F("furniture.large_aquarium", "大型水槽", "Large aquarium", "釣った魚をたくさん飼える大きな水槽。", "A big tank for your catches.", 15000, water,
                FurnitureSeries.Luxury, FurnitureStyle.Aquarium, FurnitureUse.Aquarium, 5, 2, 1.4f, ItemTags.Aquarium, ItemTags.Big, ItemTags.Rare).Shows(DisplayKind.Fish, 8).Water();

            // ---- アンティーク ----
            F("furniture.antique_bookshelf", "アンティークの本棚", "Antique bookshelf", "古い本がぎっしり。宇宙人の文字の本も。", "Packed with old books in alien script.", 2600, darkWood,
                FurnitureSeries.Antique, FurnitureStyle.Bookshelf, FurnitureUse.Book, 3, 1, 1.9f);
            F("furniture.antique_chair", "アンティークの椅子", "Antique chair", "長く使われてきた彫刻入りの椅子。", "A carved chair with long history.", 1300, darkWood,
                FurnitureSeries.Antique, FurnitureStyle.Chair, FurnitureUse.Sit, 1, 1, 1.0f, ItemTags.Seating).Seats(1).Accent(new Color(0.5f, 0.2f, 0.25f));
            F("furniture.antique_desk", "アンティークの書き物机", "Antique writing desk", "引き出し付きの重厚な机。", "A heavy desk with drawers.", 2400, darkWood,
                FurnitureSeries.Antique, FurnitureStyle.Desk, FurnitureUse.Table, 3, 2, 0.78f, ItemTags.Table).Host().Shows(DisplayKind.Any, 2);

            // ---- 趣味・展示・おもちゃ ----
            F("furniture.xylophone", "木琴", "Xylophone", "叩くとこの星の音階が鳴る。", "Plays this planet's scale.", 1200, lightWood,
                FurnitureSeries.Natural, FurnitureStyle.Xylophone, FurnitureUse.Music, 3, 2, 0.6f, ItemTags.Music);
            F("furniture.harp", "星の竪琴", "Star harp", "弦が星明かりのように光る竪琴。", "A harp with starlit strings.", 2600, gold,
                FurnitureSeries.Starry, FurnitureStyle.Harp, FurnitureUse.Music, 2, 1, 1.4f, ItemTags.Music, ItemTags.Shiny).Glow();
            F("furniture.toy_ball", "ころころボール", "Rolling ball", "転がして遊ぶボール。生き物も大好き。", "A ball to roll. Creatures love it.", 150, new Color(1f, 0.5f, 0.4f),
                FurnitureSeries.Basic, FurnitureStyle.Ball, FurnitureUse.Toy, 1, 1, 0.3f, ItemTags.Toy, ItemTags.Cute, ItemTags.ColorRed).Outdoor();
            F("furniture.toy_blocks", "積み木", "Building blocks", "色とりどりの積み木。", "Colorful blocks.", 220, new Color(0.5f, 0.75f, 1f),
                FurnitureSeries.Basic, FurnitureStyle.Blocks, FurnitureUse.Toy, 1, 1, 0.35f, ItemTags.Toy, ItemTags.Cute);
            F("furniture.fishing_rack", "釣り具置き場", "Fishing rack", "竿と自慢の釣果を飾る台。", "A rack for rods and trophy catches.", 900, wood,
                FurnitureSeries.Country, FurnitureStyle.ToolRack, FurnitureUse.Display, 2, 1, 1.6f).Shows(DisplayKind.Fish, 2);
            F("furniture.small_aquarium", "小さな水槽", "Small aquarium", "魚を数匹飼える水槽。", "A tank for a few fish.", 1800, water,
                FurnitureSeries.Basic, FurnitureStyle.Aquarium, FurnitureUse.Aquarium, 2, 1, 0.8f, ItemTags.Aquarium).Shows(DisplayKind.Fish, 3).Water();
            F("furniture.fossil_stand", "化石台", "Fossil stand", "鉱山で見つけた物を飾る台。", "A stand for finds from the mine.", 1500, stone,
                FurnitureSeries.Antique, FurnitureStyle.Pedestal, FurnitureUse.Display, 2, 2, 0.9f).Shows(DisplayKind.Mineral, 2);
            F("furniture.fish_print", "魚拓の額", "Fish print frame", "釣った魚の姿を写した額。", "A framed print of a catch.", 500, cream,
                FurnitureSeries.Country, FurnitureStyle.Painting, FurnitureUse.None, 2, 1, 0.7f);
            Last.surface = FurnitureSurface.Wall;
            F("furniture.workbench", "小さな作業台", "Small workbench", "素材を加工できる作業台。", "A bench for crafting materials.", 2000, wood,
                FurnitureSeries.Country, FurnitureStyle.Workbench, FurnitureUse.Workbench, 3, 2, 0.85f, "workbench").Host();

            // ---- 装飾小物 ----
            F("furniture.flower_vase", "花瓶", "Flower vase", "花を一輪飾る花瓶。", "A vase for a flower.", 300, new Color(0.6f, 0.75f, 1f),
                FurnitureSeries.Basic, FurnitureStyle.Vase, FurnitureUse.Plant, 1, 1, 0.45f, ItemTags.Plant, ItemTags.Flower, ItemTags.Fragrant).Shows(DisplayKind.Plant, 1);
            Last.surface = FurnitureSurface.Tabletop;
            F("furniture.photo_frame", "写真立て", "Photo frame", "思い出の写真を飾る。", "Holds a memorable photo.", 260, wood,
                FurnitureSeries.Basic, FurnitureStyle.Photo, FurnitureUse.None, 1, 1, 0.35f);
            Last.surface = FurnitureSurface.Tabletop;
            F("furniture.landscape_painting", "風景画", "Landscape painting", "この星の景色を描いた絵。", "A painting of this planet's scenery.", 1200, sky,
                FurnitureSeries.Basic, FurnitureStyle.Painting, FurnitureUse.None, 3, 1, 1.0f, ItemTags.ColorBlue);
            Last.surface = FurnitureSurface.Wall;
            F("furniture.figurine", "宇宙人の置物", "Alien figurine", "まんまるな宇宙人の小さな置物。", "A tiny round alien figure.", 480, new Color(0.7f, 0.9f, 0.6f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Figurine, FurnitureUse.None, 1, 1, 0.3f, ItemTags.Cute);
            Last.surface = FurnitureSurface.Tabletop;

            // ---- この星ならではの特殊家具（§7）----
            F("furniture.shape_sofa", "かたちあわせソファ", "Shape-fit sofa", "座る人の体型に合わせて形が変わる。", "Changes shape to fit whoever sits.", 5400, lilac,
                FurnitureSeries.AlienTradition, FurnitureStyle.Sofa, FurnitureUse.Sit, 4, 2, 0.8f, ItemTags.Seating, ItemTags.Rare, ItemTags.ColorPurple).Seats(3).Soft().Lineage(AlienLineage.Mixed);
            Last.shapeShift = true;
            F("furniture.water_orb_tank", "水玉水槽", "Water-orb tank", "空中に浮く水の球の中を魚が泳ぐ。", "Fish swim in a floating ball of water.", 8800, water,
                FurnitureSeries.Floating, FurnitureStyle.WaterOrb, FurnitureUse.Aquarium, 2, 2, 1.5f, ItemTags.Aquarium, ItemTags.Rare).Float().Shows(DisplayKind.Fish, 4)
                .Habitat(new[] { "021", "025" }, CreatureAbility.Bubble, CreatureAbility.Splash);
            F("furniture.weather_window", "お天気窓", "Weather window", "外の天気に合わせて表情が変わる窓。", "A window whose mood follows the weather.", 3000, sky,
                FurnitureSeries.AlienTradition, FurnitureStyle.Window, FurnitureUse.Window, 3, 1, 1.2f).Glow();
            Last.surface = FurnitureSurface.Wall;
            F("furniture.manepi_perch", "マネピの止まり木", "Manepi perch", "伝言生物の居場所。預かった声を聞ける。", "A perch for the message creature.", 1400, wood,
                FurnitureSeries.Creature, FurnitureStyle.Perch, FurnitureUse.Perch, 2, 2, 1.4f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "033" }, CreatureAbility.Mimic, CreatureAbility.Echo);
            F("furniture.roly_playground", "ころころ遊具", "Rolling playground", "丸い生き物が転がって遊ぶ。", "Round creatures roll around in it.", 1600, new Color(1f, 0.8f, 0.4f),
                FurnitureSeries.Creature, FurnitureStyle.Playground, FurnitureUse.Toy, 4, 4, 0.6f, ItemTags.Toy, ItemTags.CreatureHabitat, ItemTags.Cute)
                .Habitat(new[] { "013", "024", "010" }, CreatureAbility.Grind).Outdoor();
            F("furniture.fluffy_nest", "ふわもこ寝床", "Fluffy nest", "生き物たちが集まって眠る柔らかい寝床。", "A soft bed where creatures cuddle.", 1500, cream,
                FurnitureSeries.Creature, FurnitureStyle.Nest, FurnitureUse.Habitat, 3, 3, 0.35f, ItemTags.CreatureHabitat, ItemTags.Cute)
                .Habitat(new[] { "035", "037", "009" }, CreatureAbility.Heat).Soft();
            F("furniture.glowflower_curtain", "光る花のカーテン", "Glowflower curtain", "夜だけ花模様が淡く光る。", "Its flower pattern glows at night.", 2200, new Color(0.8f, 0.95f, 0.85f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Curtain, FurnitureUse.None, 3, 1, 1.8f, ItemTags.Glowing, ItemTags.Flower).Glow();
            Last.surface = FurnitureSurface.Wall;
            F("furniture.sound_floor", "音のなる床", "Singing floor", "踏む場所で違う音が鳴る。", "Each tile sings a different note.", 2600, new Color(0.9f, 0.85f, 1f),
                FurnitureSeries.AlienTradition, FurnitureStyle.SoundTiles, FurnitureUse.SoundFloor, 4, 4, 0.05f, ItemTags.Music, ItemTags.Toy);
            Last.surface = FurnitureSurface.Rug;

            // ---- 生物の住処（§9）----
            F("furniture.hakone_box", "ハコネの空き箱", "Hakone box", "箱が大好きな生き物がもぐり込む。", "A box a box-loving creature hides in.", 200, new Color(0.85f, 0.7f, 0.5f),
                FurnitureSeries.Creature, FurnitureStyle.Box, FurnitureUse.Habitat, 2, 2, 0.5f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "031" }, CreatureAbility.Pack).Outdoor();
            F("furniture.lampuru_light", "ランプルの灯り", "Lampuru light", "灯りの周りを光る生き物が飛ぶ。", "Glowing creatures circle this light.", 1500, new Color(1f, 0.9f, 0.55f),
                FurnitureSeries.Creature, FurnitureStyle.FloorLamp, FurnitureUse.Light, 1, 1, 1.5f, ItemTags.Light, ItemTags.CreatureHabitat, ItemTags.Glowing)
                .Glow().Habitat(new[] { "032", "007", "047" }, CreatureAbility.Glow).Outdoor();
            F("furniture.awarin_tank", "アワリンの水槽", "Awarin tank", "泡を作る生き物のための水槽。", "A tank for a bubble-making creature.", 3200, water,
                FurnitureSeries.Creature, FurnitureStyle.Aquarium, FurnitureUse.Aquarium, 3, 2, 1.0f, ItemTags.Aquarium, ItemTags.CreatureHabitat)
                .Shows(DisplayKind.Fish, 2).Water().Habitat(new[] { "021", "028", "001" }, CreatureAbility.Bubble, CreatureAbility.Water);
            F("furniture.nemuriko_cushion", "ネムリコの日だまりクッション", "Nemuriko sun cushion", "お日さまの匂いがするクッション。", "A cushion that smells of sunshine.", 900, new Color(1f, 0.88f, 0.5f),
                FurnitureSeries.Creature, FurnitureStyle.Cushion, FurnitureUse.Habitat, 2, 2, 0.25f, ItemTags.CreatureHabitat, "soft", ItemTags.ColorYellow)
                .Habitat(new[] { "035", "037" }, CreatureAbility.Heat).Soft();
            F("furniture.yanekoro_shelf", "ヤネコロの高い棚", "Yanekoro tall shelf", "高い所が好きな生き物の特等席。", "A high perch for a creature that loves heights.", 1900, wood,
                FurnitureSeries.Creature, FurnitureStyle.Shelf, FurnitureUse.Habitat, 2, 1, 2.2f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "039", "041" }, CreatureAbility.Stick).Shows(DisplayKind.Any, 2);
            F("furniture.tsumitsumi_stones", "ツミツミの石飾り", "Tsumitsumi stones", "小石を積むのが好きな生き物の遊び場。", "Pebbles for a stacking creature.", 350, stone,
                FurnitureSeries.Creature, FurnitureStyle.Rocks, FurnitureUse.Habitat, 2, 2, 0.4f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "038", "020", "043" }, CreatureAbility.Stack).Outdoor();
            F("furniture.hanapika_vase", "ハナピカの花瓶", "Hanapika vase", "花の周りを光る生き物が飛ぶ。", "Glowing creatures flit around its flowers.", 800, new Color(1f, 0.75f, 0.9f),
                FurnitureSeries.Creature, FurnitureStyle.Vase, FurnitureUse.Plant, 1, 1, 0.8f, ItemTags.Plant, ItemTags.Flower, ItemTags.CreatureHabitat, ItemTags.ColorPink)
                .Habitat(new[] { "006", "015" }, CreatureAbility.Pollinate).Outdoor();
            F("furniture.mizupuku_pond", "ミズプクの小さな池", "Mizupuku pond", "庭に置く小さな池。水浴びが好きな生き物が来る。", "A little garden pond for water-loving creatures.", 2400, water,
                FurnitureSeries.Creature, FurnitureStyle.Pond, FurnitureUse.Habitat, 4, 4, 0.2f, ItemTags.Outdoor, ItemTags.CreatureHabitat)
                .Habitat(new[] { "001", "021", "023", "026" }, CreatureAbility.Water, CreatureAbility.Splash).Outdoor(true).Water();
            Last.surface = FurnitureSurface.Rug;
            F("furniture.marumado_window", "マルマドの丸窓", "Marumado round window", "窓辺が好きな丸い生き物がやって来る。", "A round window loved by a round creature.", 1700, sky,
                FurnitureSeries.Creature, FurnitureStyle.RoundWindow, FurnitureUse.Window, 2, 1, 1.0f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "040" }, CreatureAbility.ColorShift);
            Last.surface = FurnitureSurface.Wall;
            F("furniture.tonton_drum", "トントンの太鼓", "Tonton drum", "リズムを刻む生き物のための太鼓。", "A drum for a rhythm-tapping creature.", 1100, new Color(0.85f, 0.35f, 0.3f),
                FurnitureSeries.Creature, FurnitureStyle.Drum, FurnitureUse.Music, 2, 2, 0.6f, ItemTags.Music, ItemTags.CreatureHabitat, ItemTags.ColorRed)
                .Habitat(new[] { "034", "018" }, CreatureAbility.Sound);
            F("furniture.karapon_palette", "カラポンの色見本棚", "Karapon color shelf", "色を変える生き物が楽しめる、色とりどりの棚。", "A shelf of colors for a color-changing creature.", 1300, new Color(0.95f, 0.6f, 0.9f),
                FurnitureSeries.Creature, FurnitureStyle.Shelf, FurnitureUse.Habitat, 3, 1, 1.3f, ItemTags.CreatureHabitat)
                .Habitat(new[] { "036", "019" }, CreatureAbility.ColorShift).Shows(DisplayKind.Any, 3);
            F("furniture.mofupi_basket", "モフピのふわふわかご", "Mofupi basket", "ふわふわの生き物が丸くなるかご。", "A basket for a fluffy creature to curl up in.", 1000, new Color(0.95f, 0.85f, 0.75f),
                FurnitureSeries.Creature, FurnitureStyle.Nest, FurnitureUse.Habitat, 2, 2, 0.35f, ItemTags.CreatureHabitat, ItemTags.Cute)
                .Habitat(new[] { "037", "035" }, CreatureAbility.Heat).Soft();
            F("furniture.birdhouse", "巣箱", "Birdhouse", "小鳥のような生き物が種をついばみに来る。", "Little bird-like creatures visit for seeds.", 500, wood,
                FurnitureSeries.Creature, FurnitureStyle.Birdhouse, FurnitureUse.Habitat, 1, 1, 1.6f, ItemTags.Outdoor, ItemTags.CreatureHabitat)
                .Habitat(new[] { "003", "010" }, CreatureAbility.Sow, CreatureAbility.SeedCollect).Outdoor(true);

            // ---- 宇宙人の伝統：10系統にちなむ家具（§8, §17）----
            F("furniture.trad_round_zabuton", "まんまる族のころがり座布団", "Rolling cushion of the round folk", "まんまるな宇宙人がころんと転がる座布団。", "Round aliens roll about on it.", 900, new Color(1f, 0.8f, 0.6f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Cushion, FurnitureUse.Sit, 2, 2, 0.25f, ItemTags.Seating, ItemTags.Cute).Seats(1).Soft().SeatH(0.25f).Lineage(AlienLineage.Round);
            F("furniture.trad_mochi_chair", "もちもち族ののびる椅子", "Stretchy chair of the mochi folk", "座る相手に合わせてのびる椅子。", "Stretches to fit whoever sits.", 1400, new Color(0.9f, 1f, 0.95f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Chair, FurnitureUse.Sit, 1, 1, 0.9f, ItemTags.Seating).Seats(1).Wobble().Lineage(AlienLineage.Mochi);
            F("furniture.trad_plush_basket", "ふわふわ族の毛布かご", "Blanket basket of the plush folk", "毛布にくるまって眠れる大きなかご。", "A big basket to snuggle in.", 1200, new Color(0.95f, 0.88f, 0.8f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Nest, FurnitureUse.Sit, 3, 3, 0.4f, ItemTags.Seating, ItemTags.Cute).Seats(1).Soft().SeatH(0.3f).Lineage(AlienLineage.Plush);
            F("furniture.trad_low_cushion", "ちび動物族の低いクッション", "Low cushion of the critter folk", "四つ足の宇宙人が丸くなって眠る低いクッション。", "Four-legged aliens curl up on it.", 800, new Color(0.75f, 0.85f, 0.65f),
                FurnitureSeries.AlienTradition, FurnitureStyle.Cushion, FurnitureUse.Sit, 3, 2, 0.18f, ItemTags.Seating).Seats(1).Soft().SeatH(0.18f).Lineage(AlienLineage.ChibiAnimal);
            F("furniture.trad_ceiling_swing", "ぷかぷか族の天井ブランコ", "Ceiling swing of the floating folk", "浮かぶ宇宙人が揺られてくつろぐ。", "Floating aliens lounge on it.", 2600, sky,
                FurnitureSeries.AlienTradition, FurnitureStyle.Swing, FurnitureUse.Sit, 2, 2, 1.0f, ItemTags.Seating).Seats(1).SeatH(1.1f).Float().Lineage(AlienLineage.Floating);
            F("furniture.trad_multi_organ", "四つ腕族の多鍵盤オルガン", "Multi-keyboard organ", "腕がたくさんあると楽しい鍵盤楽器。", "A keyboard best played with many arms.", 5200, darkWood,
                FurnitureSeries.AlienTradition, FurnitureStyle.Piano, FurnitureUse.Music, 4, 2, 1.2f, ItemTags.Music, ItemTags.Big).Lineage(AlienLineage.MultiArm);
            F("furniture.trad_star_chart_table", "星の子族の星図テーブル", "Star-chart table", "天板に星図が光るテーブル。", "A table with a glowing star chart.", 3600, night,
                FurnitureSeries.AlienTradition, FurnitureStyle.RoundTable, FurnitureUse.Table, 3, 3, 0.72f, ItemTags.Table, ItemTags.Glowing).Host().Glow().Accent(gold).Shows(DisplayKind.Food, 3).Lineage(AlienLineage.StarChild);
            F("furniture.trad_tool_rack", "小物好きの道具掛け", "Trinket rack", "帽子やかばん、道具を掛けて飾る。", "Hang hats, bags and tools.", 900, wood,
                FurnitureSeries.AlienTradition, FurnitureStyle.ToolRack, FurnitureUse.Display, 3, 1, 1.0f).Shows(DisplayKind.Any, 3).Lineage(AlienLineage.Lifestyle);
            Last.surface = FurnitureSurface.Wall;
            F("furniture.trad_big_mirror", "表情族の大きな鏡", "Big mirror of the expressive folk", "表情を映して楽しむ大きな鏡。", "A big mirror to make faces in.", 1800, crystal,
                FurnitureSeries.AlienTradition, FurnitureStyle.Mirror, FurnitureUse.Mirror, 2, 1, 1.9f, ItemTags.Shiny).Lineage(AlienLineage.Expressive);

            // ---- 庭・屋外（§10）----
            F("furniture.garden_bench", "庭のベンチ", "Garden bench", "庭で休む・話すためのベンチ。", "A bench to rest and chat.", 900, wood,
                FurnitureSeries.Garden, FurnitureStyle.Bench, FurnitureUse.Sit, 4, 2, 0.8f, ItemTags.Seating, ItemTags.Outdoor).Seats(2).Outdoor();
            F("furniture.garden_fence", "木の柵", "Wooden fence", "区画を分ける低い柵。", "A low fence.", 120, lightWood,
                FurnitureSeries.Garden, FurnitureStyle.Fence, FurnitureUse.None, 2, 1, 0.8f, ItemTags.Outdoor).Outdoor(true);
            F("furniture.stone_path", "石畳の道", "Stone path", "庭に敷く石の道。", "Stepping stones.", 80, stone,
                FurnitureSeries.Garden, FurnitureStyle.Path, FurnitureUse.None, 2, 2, 0.04f, ItemTags.Outdoor).Outdoor(true);
            Last.surface = FurnitureSurface.Rug;
            F("furniture.flower_bed", "花壇", "Flower bed", "色とりどりの花が咲く花壇。", "A bed of colorful flowers.", 600, new Color(0.95f, 0.5f, 0.6f),
                FurnitureSeries.Garden, FurnitureStyle.FlowerBed, FurnitureUse.Plant, 3, 2, 0.5f, ItemTags.Plant, ItemTags.Flower, ItemTags.Outdoor, ItemTags.Fragrant)
                .Outdoor(true).Habitat(new[] { "006", "015" }, CreatureAbility.Pollinate, CreatureAbility.Float);
            F("furniture.street_lamp", "庭の街灯", "Garden lamp post", "夜の庭を照らす灯り。", "Lights the garden at night.", 1400, new Color(0.3f, 0.3f, 0.35f),
                FurnitureSeries.Garden, FurnitureStyle.Streetlight, FurnitureUse.Light, 1, 1, 2.6f, ItemTags.Light, ItemTags.Outdoor).Glow().Outdoor(true)
                .Habitat(new[] { "032", "007" }, CreatureAbility.Glow);
            F("furniture.garden_sign", "看板", "Signboard", "家の名前を描く看板。", "A sign for your home.", 300, wood,
                FurnitureSeries.Garden, FurnitureStyle.Sign, FurnitureUse.None, 2, 1, 1.2f, ItemTags.Outdoor).Outdoor(true);
            F("furniture.rock_pile", "石の山", "Rock pile", "岩場のような景観を作る。", "Makes a rocky corner.", 250, stone,
                FurnitureSeries.Garden, FurnitureStyle.Rocks, FurnitureUse.None, 3, 3, 0.7f, ItemTags.Outdoor).Outdoor(true)
                .Habitat(new[] { "038", "043", "046" }, CreatureAbility.Stack, CreatureAbility.MineralDust);
            F("furniture.garden_tree", "大きな木", "Big tree", "木陰を作る大きな木。止まり木にもなる。", "A shady tree, also a perch.", 1800, leaf,
                FurnitureSeries.Garden, FurnitureStyle.Tree, FurnitureUse.Plant, 3, 3, 3.5f, ItemTags.Plant, ItemTags.Outdoor, ItemTags.Big)
                .Outdoor(true).Habitat(new[] { "003", "014", "048" }, CreatureAbility.Sow);
            F("furniture.wind_chime", "星の風鈴", "Star wind chime", "星風が吹くと澄んだ音が鳴る。", "Rings clearly in the star wind.", 700, crystal,
                FurnitureSeries.Garden, FurnitureStyle.WindChime, FurnitureUse.Music, 1, 1, 1.8f, ItemTags.Music, ItemTags.Outdoor, ItemTags.Shiny).Outdoor();

            // ---- 壁紙・床材（§4 壁・床、§5.1 壁紙・床材の変更）----
            D("decor.wall_wood", "木の板の壁", "Wooden planks wall", 400, true, new Color(0.80f, 0.64f, 0.45f), new Color(0.65f, 0.5f, 0.35f), "planks");
            D("decor.wall_flower", "花柄の壁紙", "Floral wallpaper", 600, true, new Color(0.98f, 0.92f, 0.90f), new Color(1f, 0.6f, 0.7f), "flowers");
            D("decor.wall_starry", "星空の壁紙", "Starry wallpaper", 1500, true, new Color(0.15f, 0.17f, 0.38f), new Color(1f, 0.95f, 0.6f), "stars", true);
            D("decor.wall_crystal", "結晶の壁", "Crystal wall", 2400, true, new Color(0.75f, 0.88f, 0.98f), new Color(0.9f, 1f, 1f), "crystal", true);
            D("decor.wall_polka", "水玉の壁紙", "Polka-dot wallpaper", 500, true, new Color(0.85f, 0.95f, 1f), new Color(0.5f, 0.7f, 1f), "dots");
            D("decor.wall_brick", "レンガの壁", "Brick wall", 800, true, new Color(0.75f, 0.42f, 0.32f), new Color(0.85f, 0.8f, 0.75f), "brick");
            D("decor.wall_moss", "苔の壁", "Moss wall", 700, true, new Color(0.45f, 0.62f, 0.40f), new Color(0.3f, 0.5f, 0.3f), "dots");
            D("decor.floor_wood", "木の床", "Wooden floor", 400, false, new Color(0.70f, 0.52f, 0.34f), new Color(0.6f, 0.43f, 0.28f), "planks");
            D("decor.floor_stone", "石畳の床", "Flagstone floor", 600, false, new Color(0.62f, 0.62f, 0.64f), new Color(0.5f, 0.5f, 0.52f), "tiles");
            D("decor.floor_grass", "草の床", "Grass floor", 700, false, new Color(0.45f, 0.72f, 0.40f), new Color(0.6f, 0.85f, 0.45f), "grass");
            D("decor.floor_star", "星のタイル", "Star tiles", 1600, false, new Color(0.22f, 0.24f, 0.45f), new Color(1f, 0.9f, 0.5f), "stars", true);
            D("decor.floor_fluffy", "ふわふわ絨毯", "Fluffy carpet", 1200, false, new Color(0.97f, 0.88f, 0.92f), new Color(1f, 0.95f, 0.97f), "fluffy");
            D("decor.floor_water", "水面の床", "Water-surface floor", 3000, false, new Color(0.45f, 0.72f, 0.95f), new Color(0.8f, 0.95f, 1f), "water", true);

            // 仕様から分かる性質をタグにも反映（住民の好み・クエスト条件で使えるように）
            foreach (var def in furniture)
            {
                var spec = (FurnitureSpec)def.ModuleData;
                if (spec.soft) def.Tags.Add("soft");
                if (spec.glowing) def.Tags.Add(ItemTags.Glowing);
                if (spec.floating) def.Tags.Add("floating");
                if (spec.allowOutdoor) def.Tags.Add(ItemTags.Outdoor);
                if (spec.IsHabitat) def.Tags.Add(ItemTags.CreatureHabitat);
                if (spec.use == FurnitureUse.Sit) def.Tags.Add(ItemTags.Seating);
                def.Tags.Add("series_" + spec.series.ToString().ToLowerInvariant());
                if (spec.lineage >= 0) def.Tags.Add("lineage_" + ((AlienLineage)spec.lineage).ToString().ToLowerInvariant());
            }
        }

        private static ItemDefinition lastDef;
        private static FurnitureSpec Last => lastDef.ModuleData as FurnitureSpec;

        private static FurnitureSpec F(string id, string ja, string en, string dja, string den, int price, Color color,
            FurnitureSeries series, FurnitureStyle style, FurnitureUse use, int w, int d, float h, params string[] tags)
        {
            var spec = new FurnitureSpec { w = w, d = d, height = h, series = series, style = style, use = use };
            if (use == FurnitureUse.Sleep && spec.seats == 0) spec.seats = 1;
            var def = new ItemDefinition
            {
                Id = id,
                NameJa = ja,
                NameEn = en,
                DescriptionJa = dja,
                DescriptionEn = den,
                Category = ItemCategory.Furniture,
                BasePrice = price,
                MaxStack = 10,
                Rarity = price >= 8000 ? 4 : price >= 4000 ? 3 : price >= 1500 ? 2 : price >= 600 ? 1 : 0,
                Durable = true,
                Color = color,
                Shape = PrimitiveType.Cube,
                SourceActivity = "housing",
                ModuleData = spec
            };
            def.Tags.Add(ItemTags.Furniture);
            def.WithTags(tags);
            if (series == FurnitureSeries.Garden) def.Tags.Add(ItemTags.Outdoor);
            def.GenericConceptId = GenericConcept(spec, def);
            ItemRegistry.Register(def);
            furniture.Add(def);
            lastDef = def;
            return spec;
        }

        private static void D(string id, string ja, string en, int price, bool wall, Color baseColor, Color accent, string pattern, bool glow = false)
        {
            var def = new ItemDefinition
            {
                Id = id,
                NameJa = ja,
                NameEn = en,
                DescriptionJa = wall ? "部屋の壁に貼る。模様替えモードで使う。" : "部屋の床に敷く。模様替えモードで使う。",
                DescriptionEn = wall ? "Apply to a room's walls in decorate mode." : "Lay on a room's floor in decorate mode.",
                Category = ItemCategory.Decor,
                BasePrice = price,
                MaxStack = 10,
                Rarity = price >= 2000 ? 2 : price >= 1000 ? 1 : 0,
                Durable = true,
                Color = baseColor,
                Shape = PrimitiveType.Quad,
                SourceActivity = "housing",
                GenericConceptId = Concepts.FURNITURE,
                ModuleData = new DecorSpec { isWallpaper = wall, baseColor = baseColor, accent = accent, pattern = pattern, glow = glow }
            };
            if (glow) def.Tags.Add(ItemTags.Glowing);
            ItemRegistry.Register(def);
            decor.Add(def);
        }

        private static void RegisterMaterials()
        {
            ItemRegistry.Register(new ItemDefinition
            {
                Id = LumberId, NameJa = "木材", NameEn = "Lumber",
                DescriptionJa = "家具や増築に使う板材。家具職人が扱っている。", DescriptionEn = "Boards for furniture and building.",
                Category = ItemCategory.Material, BasePrice = 40, Color = new Color(0.75f, 0.55f, 0.35f), SourceActivity = "housing",
                GenericConceptId = Concepts.TREE
            }.WithTags("wood"));
            ItemRegistry.Register(new ItemDefinition
            {
                Id = StoneBlockId, NameJa = "石材", NameEn = "Stone block",
                DescriptionJa = "切り出した石。地下室や水辺の部屋に使う。", DescriptionEn = "Cut stone for basements and water rooms.",
                Category = ItemCategory.Material, BasePrice = 60, Color = new Color(0.6f, 0.6f, 0.62f), SourceActivity = "housing",
                GenericConceptId = Concepts.STONE
            }.WithTags("stone"));
            ItemRegistry.Register(new ItemDefinition
            {
                Id = GlassPaneId, NameJa = "板ガラス", NameEn = "Glass pane",
                DescriptionJa = "結晶を溶かして作った透明な板。", DescriptionEn = "A clear pane made from melted crystal.",
                Category = ItemCategory.Material, BasePrice = 400, Color = new Color(0.8f, 0.95f, 1f), SourceActivity = "housing",
                GenericConceptId = Concepts.STONE
            }.WithTags(ItemTags.Crystal, "glass"));
        }

        // ------------------------------------------------------------
        // 星のシードと家具文化（§18）【提案】
        // ------------------------------------------------------------

        public static PlanetFurnitureCulture Culture { get; private set; } = new PlanetFurnitureCulture();

        public static void PrepareCulture(PlanetContext ctx)
        {
            var rng = ctx.Rng("housing.culture");
            var c = new PlanetFurnitureCulture();
            var pool = new List<FurnitureSeries>
            {
                FurnitureSeries.Natural, FurnitureSeries.Country, FurnitureSeries.Retro, FurnitureSeries.Manmaru,
                FurnitureSeries.Mochi, FurnitureSeries.Starry, FurnitureSeries.Crystal, FurnitureSeries.Floating,
                FurnitureSeries.Antique, FurnitureSeries.AlienTradition, FurnitureSeries.Creature
            };
            rng.Shuffle(pool);
            c.favored.Add(pool[0]);
            c.favored.Add(pool[1]);
            c.rare = pool[pool.Count - 1];
            // 木の色・布の色・形の丸さ（色や形の傾向）
            c.woodTint = Color.HSVToRGB(rng.Range(0.04f, 0.12f), rng.Range(0.35f, 0.65f), rng.Range(0.45f, 0.9f));
            c.fabricTint = Color.HSVToRGB(rng.NextFloat(), rng.Range(0.25f, 0.55f), rng.Range(0.8f, 1f));
            c.roundness = rng.Range(0f, 1f);
            c.tintStrength = rng.Range(0.15f, 0.4f);
            // この星の音階（楽器・音のなる床）
            var scales = new[]
            {
                new[] { 0, 2, 4, 7, 9 }, new[] { 0, 3, 5, 7, 10 }, new[] { 0, 2, 3, 7, 8 }, new[] { 0, 1, 5, 7, 10 }, new[] { 0, 2, 4, 6, 9 }
            };
            c.scale = scales[rng.Range(0, scales.Length)];
            c.baseNote = rng.Range(-5, 6);
            Culture = c;
        }

        /// <summary>星の文化で色を寄せる（見た目だけ）。</summary>
        public static Color Tint(Color baseColor, FurnitureSpec spec)
        {
            var c = Culture;
            if (c == null || spec == null) return baseColor;
            Color target;
            switch (spec.series)
            {
                case FurnitureSeries.Basic:
                case FurnitureSeries.Natural:
                case FurnitureSeries.Country:
                case FurnitureSeries.Antique:
                case FurnitureSeries.Garden:
                    target = c.woodTint;
                    break;
                case FurnitureSeries.Mochi:
                case FurnitureSeries.Manmaru:
                    target = c.fabricTint;
                    break;
                default:
                    return baseColor;
            }
            var r = Color.Lerp(baseColor, target, c.tintStrength * (spec.soft ? 0.6f : 1f));
            r.a = baseColor.a;
            return r;
        }

        public static float PriceRate(FurnitureSpec spec)
        {
            var c = Culture;
            if (c == null || spec == null) return 1f;
            if (c.favored.Contains(spec.series)) return 0.85f;
            if (c.rare == spec.series) return 1.3f;
            return 1f;
        }

        /// <summary>家具職人のその日の品揃え（決定的）。</summary>
        public static List<ItemDefinition> DailyStock(ulong seed, int day, int count)
        {
            var result = new List<ItemDefinition>();
            var rng = SeededRandom.For(seed, "housing.stock." + day);
            var c = Culture;
            var candidates = new List<ItemDefinition>();
            foreach (var def in furniture)
            {
                var spec = def.ModuleData as FurnitureSpec;
                if (spec == null) continue;
                if (def.Id == StarterBedId || def.Id == StarterChestId) continue;
                candidates.Add(def);
            }
            rng.Shuffle(candidates);
            // 星で好まれるシリーズは並びやすい
            candidates.Sort((a, b) => Weight(b, c).CompareTo(Weight(a, c)));
            foreach (var def in candidates)
            {
                if (result.Count >= count) break;
                var spec = (FurnitureSpec)def.ModuleData;
                float chance = c.favored.Contains(spec.series) ? 0.95f : c.rare == spec.series ? 0.12f : spec.series == FurnitureSeries.Luxury ? 0.35f : 0.55f;
                if (rng.Chance(chance)) result.Add(def);
            }
            // 壁紙・床材を2つ
            var dec = new List<ItemDefinition>(decor);
            rng.Shuffle(dec);
            for (int i = 0; i < Mathf.Min(2, dec.Count); i++) result.Add(dec[i]);
            return result;
        }

        private static int Weight(ItemDefinition def, PlanetFurnitureCulture c)
        {
            var spec = def.ModuleData as FurnitureSpec;
            if (spec == null || c == null) return 0;
            return c.favored.Contains(spec.series) ? 2 : c.rare == spec.series ? 0 : 1;
        }

        /// <summary>生物の住処にぴったりか（種族番号か能力で判定）。</summary>
        public static bool HabitatSuits(FurnitureSpec spec, string speciesId, IReadOnlyList<CreatureAbility> abilities)
        {
            if (spec == null || !spec.IsHabitat) return false;
            string num = SpeciesNumber(speciesId);
            if (num != null) foreach (var s in spec.habitatSpecies) if (s == num) return true;
            if (abilities != null)
                foreach (var a in spec.habitatAbilities)
                    for (int i = 0; i < abilities.Count; i++) if (abilities[i] == a) return true;
            return false;
        }

        /// <summary>"creature.031_hakone" → "031"。</summary>
        public static string SpeciesNumber(string speciesId)
        {
            if (string.IsNullOrEmpty(speciesId)) return null;
            int dot = speciesId.IndexOf('.');
            int start = dot >= 0 ? dot + 1 : 0;
            int len = 0;
            while (start + len < speciesId.Length && char.IsDigit(speciesId[start + len])) len++;
            if (len == 0) return null;
            var n = speciesId.Substring(start, len);
            while (n.Length < 3) n = "0" + n;
            return n;
        }
    }

    /// <summary>星ごとの家具文化（§18）。</summary>
    public class PlanetFurnitureCulture
    {
        public readonly List<FurnitureSeries> favored = new List<FurnitureSeries>();
        public FurnitureSeries rare = FurnitureSeries.Luxury;
        public Color woodTint = new Color(0.7f, 0.5f, 0.3f);
        public Color fabricTint = Color.white;
        public float roundness = 0.5f;
        public float tintStrength = 0.25f;
        public int[] scale = { 0, 2, 4, 7, 9 };
        public int baseNote;
    }
}
