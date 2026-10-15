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

        // ===== 釣り（登録担当：Fishing）=====
        /// <summary>温度差に強い特殊な膜を持つ魚（冷却循環装置 R03）。</summary>
        public const string FishFrostFin = "fish.frost_fin";
        /// <summary>波・振動・空間の揺らぎを感知する器官を持つ珍しい魚（次元観測装置 R06）。</summary>
        public const string FishRippleEye = "fish.ripple_eye";

        // ===== 農業（登録担当：Farming）=====
        /// <summary>星蜜果（エネルギー特性を持つ作物。燃料 R04）。</summary>
        public const string CropStarHoneyFruit = "crop.star_honey_fruit";
        public const string SeedStarHoneyFruit = "seed.star_honey_fruit";

        // ===== 加工品（登録担当：Cooking/Processing。レシピも同モジュールが用意）=====
        /// <summary>導電部材（導電性鉱石を製錬）。R01。</summary>
        public const string PartConductiveWire = "part.conductive_wire";
        /// <summary>強化船体板（高強度金属を鍛冶屋で鍛造）。R02。</summary>
        public const string PartHullPlate = "part.hull_plate";
        /// <summary>冷却膜（フロストフィンを洗浄・加工）。R03。</summary>
        public const string PartCoolingMembrane = "part.cooling_membrane";
        /// <summary>宇宙船用燃料（星蜜果を圧搾・発酵・加熱で精製）。R04/R08。</summary>
        public const string FuelStarHoney = "fuel.star_honey";
        /// <summary>耐熱構造材（耐熱金属を加熱・成形・冷却）。R05。</summary>
        public const string PartHeatproofFrame = "part.heatproof_frame";
        /// <summary>結晶エネルギーコア（高密度結晶を加工）。R05。</summary>
        public const string PartCrystalCore = "part.crystal_core";
        /// <summary>揺らぎセンサー素材（リップルアイの器官を精密加工）。R06。</summary>
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
