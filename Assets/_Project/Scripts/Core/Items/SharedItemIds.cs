namespace Uchujin.Core
{
    /// <summary>
    /// 複数のモジュールにまたがって参照されるアイテムID。
    /// 「登録担当」のモジュールが RegisterContent() で必ず登録し、入手手段を用意する。
    /// ロケット修理（ロケット仕様 §3〜§9）の5系統（釣り・農業・鉱山・生き物・お金）＋加工に対応する。
    /// 名称・数値は仕様上すべて【仮】。
    /// </summary>
    public static class SharedItemIds
    {
        // ===== 鉱山（登録担当：Mining）=====
        /// <summary>導電性鉱石（1〜2階）。補助電源 R01。</summary>
        public const string OreConductive = "ore.conductive";
        /// <summary>高強度金属鉱石（3〜5階）。船体外装 R02。</summary>
        public const string OreSturdyMetal = "ore.sturdy_metal";
        /// <summary>耐熱金属鉱石（6〜8階）。主エンジン R05。</summary>
        public const string OreHeatproofMetal = "ore.heatproof_metal";
        /// <summary>高密度エネルギー結晶（9〜10階）。主エンジン R05 の中核。</summary>
        public const string OreEnergyCrystal = "ore.energy_crystal";

        // ===== 釣り（登録担当：Fishing。魚50種の原本 F001〜F050：Fish/design/fish_50_design_v0_2.md §9）=====
        /// <summary>F008 シモマダラ：冷たい湧き水の淵の一般魚。冷却膜（標準品質）の候補（冷却循環装置 R03）。</summary>
        public const string FishShimomadara = "fish.f008_shimomadara";
        /// <summary>F044 ヒヤシウオ：湖・地下湖の深場の希少魚。冷却膜（高品質）の候補（R03）。</summary>
        public const string FishHiyashiuo = "fish.f044_hiyashiuo";
        /// <summary>F030 ハモリウオ：音を重ねて鳴く。振動センサーの候補（次元観測装置 R06）。</summary>
        public const string FishHamoriuo = "fish.f030_hamoriuo";
        /// <summary>F041 イワナキ：地下湖の低音の魚。振動センサーの候補（R06）。</summary>
        public const string FishIwanaki = "fish.f041_iwanaki";
        /// <summary>F043 オトナシ：静かな岸の敏感な魚。振動センサーの候補（R06）。</summary>
        public const string FishOtonashi = "fish.f043_otonashi";
        /// <summary>F045 サザナミメ：水の振動を感じる感覚器。振動センサーの候補（R06）。</summary>
        public const string FishSazanamime = "fish.f045_sazanamime";
        /// <summary>冷却膜（R03）の候補魚。どれでも修理を進められる（原本 §9.1）。</summary>
        public static readonly string[] FishCoolingCandidates = { FishShimomadara, FishHiyashiuo };
        /// <summary>振動センサー（R06）の候補魚。感知の仕方が異なる4種から選べる（原本 §9.1）。</summary>
        public static readonly string[] FishSensorCandidates = { FishHamoriuo, FishOtonashi, FishSazanamime, FishIwanaki };
        /// <summary>旧案（魚41種）の修理用の魚の ID。古いセーブの読み替えにだけ使う。</summary>
        public const string LegacyFishFrostFin = "fish.frost_fin";
        public const string LegacyFishRippleEye = "fish.ripple_eye";

        // ===== 農業（登録担当：Farming）=====
        /// <summary>星蜜果（エネルギー特性を持つ作物。燃料 R04）。</summary>
        public const string CropStarHoneyFruit = "crop.star_honey_fruit";
        public const string SeedStarHoneyFruit = "seed.star_honey_fruit";

        // ===== 加工品（登録担当：Cooking/Processing。レシピも同モジュールが用意）=====
        /// <summary>導電部材（導電性鉱石を製錬）。R01。</summary>
        public const string PartConductiveWire = "part.conductive_wire";
        /// <summary>強化船体板（高強度金属を鍛冶屋で鍛造）。R02。</summary>
        public const string PartHullPlate = "part.hull_plate";
        /// <summary>冷却膜（シモマダラ・ヒヤシウオの膜を洗浄・加工）。R03。</summary>
        public const string PartCoolingMembrane = "part.cooling_membrane";
        /// <summary>宇宙船用燃料（星蜜果を圧搾・発酵・加熱で精製）。R04/R08。</summary>
        public const string FuelStarHoney = "fuel.star_honey";
        /// <summary>耐熱構造材（耐熱金属を加熱・成形・冷却）。R05。</summary>
        public const string PartHeatproofFrame = "part.heatproof_frame";
        /// <summary>結晶エネルギーコア（高密度結晶を加工）。R05。</summary>
        public const string PartCrystalCore = "part.crystal_core";
        /// <summary>揺らぎセンサー素材（振動を感じる魚の器官を精密加工）。R06。</summary>
        public const string PartRippleSensor = "part.ripple_sensor";

        // ===== 購入部品（登録担当：Rocket。修理屋の住民から購入）=====
        public const string PartSmallControlUnit = "part.small_control_unit";
        public const string PartConnectorKit = "part.connector_kit";
        public const string PartPrecisionParts = "part.precision_parts";
        public const string PartObservationParts = "part.observation_parts";
        public const string PartPhaseControlUnit = "part.phase_control_unit";

        // ===== 宇宙生物（登録担当：Creatures）=====
        /// <summary>マネピ（No.033）の種族ID。伝言・音声再現。商店・伝言広場・自宅に置ける。</summary>
        public const string SpeciesManepi = "creature.033_manepi";
        public const string SpeciesMizupuku = "creature.001_mizupuku";
        public const string SpeciesPokaris = "creature.009_pokaris";
        public const string SpeciesKodamaru = "creature.042_kodamaru";
        public const string SpeciesHikarimushi = "creature.007_hikarimushi";
        public const string SpeciesHakobachi = "creature.008_hakobachi";
        public const string SpeciesHoshinoko = "creature.050_hoshinoko";

        // ===== 汎用（登録担当：Core）=====
        public const string Letter = "misc.letter";
        public const string GiftBox = "misc.gift_box";
    }
}
