using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Rocket
{
    // =====================================================================
    // ロケット修理の静的データ（ロケット修理仕様 §3.2 の仮構成）。
    // 装置数・必要素材・数量・費用はすべて【仮数値】。ここだけ変えれば調整できるようにテーブル化している。
    // =====================================================================

    /// <summary>装置の進行状態（ロケット仕様 §3.3）。段階は強制しない（素材を先に持っていればそのまま修理できる）。</summary>
    public enum DeviceStatus
    {
        Broken,          // 故障中
        Investigating,   // 調査中（診断・図で必要なものを調べた）
        Collecting,      // 素材収集中（何かを納品した／持っている）
        ReadyToAssemble, // 組立・取付可能
        Repaired,        // 修理済み
        Verified         // 動作確認済み
    }

    /// <summary>素材を手に入れる活動（図・診断のヒント用）。§3.1 の5系統＋加工・購入。</summary>
    public enum SourceKind
    {
        Fishing,
        Farming,
        Mining,
        Creatures,
        Money,
        Processing,
        Mechanic // 修理屋から買う
    }

    public sealed class ItemRequirement
    {
        public string ItemId;
        public int Count;

        public ItemRequirement(string itemId, int count)
        {
            ItemId = itemId;
            Count = count;
        }
    }

    /// <summary>修理装置1つの定義。</summary>
    public sealed class DeviceDef
    {
        public string Id;
        public int Index;
        public string NameJa, NameEn;
        /// <summary>故障の内容（宇宙船の診断が知っていること §13.2）。</summary>
        public string BrokenJa, BrokenEn;
        /// <summary>修理後に起きること（§3.2・§12）。</summary>
        public string FunctionJa, FunctionEn;
        /// <summary>試運転の名前。</summary>
        public string TestJa, TestEn;
        public readonly List<ItemRequirement> Items = new List<ItemRequirement>();
        /// <summary>作業費（修理屋への支払い）【仮数値】。購入部品の代金とあわせてバランス §4.3 の現金費用になる。</summary>
        public int LaborFee;
        /// <summary>必須の生物の能力（どれか1つ）。空なら不要。生物は消費しない（§7.1）。</summary>
        public CreatureAbility[] RequiredAbilities = new CreatureAbility[0];
        /// <summary>任意で手伝える生物の能力（作業費が少し安くなる）【提案】。</summary>
        public CreatureAbility[] HelperAbilities = new CreatureAbility[0];
        /// <summary>試運転に必要な他装置（§10.2：機能の試験だけ依存する）。</summary>
        public string[] TestDependsOn = new string[0];
        /// <summary>試運転にタンクの燃料が必要か。</summary>
        public bool TestNeedsFuel;
        /// <summary>最終起動（R08）。</summary>
        public bool IsFinal;
        /// <summary>大きな修理（住民が大勢集まる §14.2）。</summary>
        public bool BigEvent;
        /// <summary>図の上の位置（0〜1。ロケットの絵の中の故障箇所）。</summary>
        public Vector2 DiagramPos;
        public Color DiagramColor = Color.white;

        public string Name => Loc.T(NameJa, NameEn);
        public string Broken => Loc.T(BrokenJa, BrokenEn);
        public string Function => Loc.T(FunctionJa, FunctionEn);
        public string TestName => Loc.T(TestJa, TestEn);
        public bool RequiresCreature => RequiredAbilities != null && RequiredAbilities.Length > 0;

        public DeviceDef Need(string itemId, int count)
        {
            Items.Add(new ItemRequirement(itemId, count));
            return this;
        }
    }

    /// <summary>
    /// 素材の「性質」の説明（宇宙船が知っている必要性能 §2.1・§13.2）。
    /// 現地での名前・入手場所は、実際に手に入れるまで診断画面に出さない。
    /// </summary>
    public sealed class MaterialHint
    {
        public string ItemId;
        public string PropertyJa, PropertyEn;
        public string WhereJa, WhereEn;
        public SourceKind[] Sources;
        /// <summary>元になる素材（加工品の場合）。図で「原料 → 加工 → 部品」を絵で示す。</summary>
        public string RawItemId;

        public string Property => Loc.T(PropertyJa, PropertyEn);
        public string Where => Loc.T(WhereJa, WhereEn);
    }

    /// <summary>修理屋が売る部品（§8.4：後半ほど高額）。</summary>
    public sealed class PurchasedPartDef
    {
        public string ItemId;
        public string NameJa, NameEn, DescJa, DescEn;
        public int Price;
        public Color Color;
        public PrimitiveType Shape;
    }

    /// <summary>修理屋への作業依頼（加工の代行 §9.3）。料理・加工モジュールのレシピが無くても修理できる保険も兼ねる。</summary>
    public sealed class ProxyRecipe
    {
        public string Id;
        public string InputItemId;
        public int InputCount;
        public int Fee;
        public string OutputItemId;
        public int OutputCount;
        /// <summary>出来上がりまでの日数（翌朝なら1）。</summary>
        public int Days;
    }

    public static class RocketTables
    {
        // ---------------- 調整用の仮数値 ----------------
        /// <summary>最終起動に必要な燃料タンクの量（宇宙船用燃料の個数）【仮数値】。</summary>
        public const int FuelRequiredForLaunch = 10;
        /// <summary>手伝いの生物がいる時の作業費の割引率【仮数値】。</summary>
        public const float HelperDiscount = 0.2f;
        /// <summary>修理屋が作業場へ通う時刻【仮数値】。</summary>
        public static readonly int[] MechanicWorkHours = { 9, 14 };
        /// <summary>修理屋が作業場にいる時間（分）。</summary>
        public const int MechanicWorkMinutes = 120;
        /// <summary>見物の住民が来る確率（1時間ごと）の基礎値と修理1つあたりの増分【仮数値】。</summary>
        public const float WatcherBaseChance = 0.12f;
        public const float WatcherChancePerRepair = 0.05f;
        public const int WatcherMinutes = 45;
        /// <summary>修理の瞬間に集まる住民の数（通常／大きな修理）。</summary>
        public const int GatherCountSmall = 3;
        public const int GatherCountBig = 10;
        public const float GatherRadiusSmall = 45f;
        public const float GatherRadiusBig = 400f;
        public const int GatherMinutes = 60;
        /// <summary>最初は大きく傾いている（修理が進むと起き上がる）。</summary>
        public const float BaseTiltDegrees = 16f;
        public const int HistoryMax = 200;

        public const string FinalDeviceId = "R08";
        public const string TankTargetId = "tank";

        public static readonly List<DeviceDef> Devices = new List<DeviceDef>();
        public static readonly Dictionary<string, MaterialHint> Hints = new Dictionary<string, MaterialHint>();
        public static readonly List<PurchasedPartDef> PurchasedParts = new List<PurchasedPartDef>();
        public static readonly List<ProxyRecipe> ProxyRecipes = new List<ProxyRecipe>();

        static RocketTables()
        {
            BuildDevices();
            BuildPurchasedParts();
            BuildHints();
            BuildProxyRecipes();
        }

        public static DeviceDef Get(string id)
        {
            foreach (var d in Devices) if (d.Id == id) return d;
            return null;
        }

        public static int RepairableCount
        {
            get
            {
                int n = 0;
                foreach (var d in Devices) if (!d.IsFinal) n++;
                return n;
            }
        }

        public static MaterialHint Hint(string itemId)
        {
            if (itemId != null && Hints.TryGetValue(itemId, out var h)) return h;
            return null;
        }

        public static ProxyRecipe GetProxy(string id)
        {
            foreach (var r in ProxyRecipes) if (r.Id == id) return r;
            return null;
        }

        public static PurchasedPartDef GetPurchased(string itemId)
        {
            foreach (var p in PurchasedParts) if (p.ItemId == itemId) return p;
            return null;
        }

        /// <summary>修理に関わる全アイテムID（発見の記録・図の表示に使う）。</summary>
        public static IEnumerable<string> TrackedItemIds
        {
            get
            {
                var set = new HashSet<string>();
                foreach (var d in Devices)
                    foreach (var r in d.Items)
                        set.Add(r.ItemId);
                set.Add(SharedItemIds.FuelStarHoney);
                foreach (var h in Hints.Values)
                {
                    set.Add(h.ItemId);
                    if (!string.IsNullOrEmpty(h.RawItemId)) set.Add(h.RawItemId);
                }
                return set;
            }
        }

        // ------------------------------------------------------------
        // 装置（§3.2・バランス §4.3。現金費用＝購入部品＋作業費が表の金額になるよう配分）
        // ------------------------------------------------------------
        private static void BuildDevices()
        {
            // R01 補助電源：1,200G ＝ 小型制御部品500G ＋ 作業費700G
            Devices.Add(new DeviceDef
            {
                Id = "R01", Index = 0,
                NameJa = "補助電源", NameEn = "Auxiliary Power",
                BrokenJa = "電源回路が焼き切れている。船内の照明と診断装置の一部が止まっている。",
                BrokenEn = "The power circuit is burnt out. Cabin lights and some diagnostics are offline.",
                FunctionJa = "船内の照明と一部の診断装置が復旧する。",
                FunctionEn = "Cabin lights and some diagnostics come back on.",
                TestJa = "照明の点灯試験", TestEn = "Lighting test",
                LaborFee = 700,
                HelperAbilities = new[] { CreatureAbility.Glow },
                BigEvent = true, // 最初の明かり（§20.4：修理屋や住民が興味を示す）
                DiagramPos = new Vector2(0.5f, 0.42f),
                DiagramColor = new Color(1f, 0.9f, 0.4f)
            }.Need(SharedItemIds.PartConductiveWire, 3).Need(SharedItemIds.PartSmallControlUnit, 1));

            // R02 船体外装：4,800G ＝ 作業費（鍛冶加工・取り付け）
            Devices.Add(new DeviceDef
            {
                Id = "R02", Index = 1,
                NameJa = "船体外装", NameEn = "Hull Plating",
                BrokenJa = "外装板が剥がれ、船体に亀裂が入っている。",
                BrokenEn = "Hull plates are torn off and the hull is cracked.",
                FunctionJa = "船体の亀裂と欠けた外装が元に戻る。",
                FunctionEn = "Cracks and missing plates are restored.",
                TestJa = "気密試験", TestEn = "Pressure test",
                LaborFee = 4800,
                HelperAbilities = new[] { CreatureAbility.Heat, CreatureAbility.Stick },
                DiagramPos = new Vector2(0.38f, 0.55f),
                DiagramColor = new Color(0.85f, 0.85f, 0.95f)
            }.Need(SharedItemIds.PartHullPlate, 6));

            // R03 冷却循環装置：6,500G ＝ 接続部品1,200G×2 ＋ 作業費4,100G
            Devices.Add(new DeviceDef
            {
                Id = "R03", Index = 2,
                NameJa = "冷却循環装置", NameEn = "Coolant Loop",
                BrokenJa = "冷却膜が破れ、冷却液が循環しない。",
                BrokenEn = "The cooling membrane is torn; coolant cannot circulate.",
                FunctionJa = "冷却液が配管を循環する。",
                FunctionEn = "Coolant flows through the pipes.",
                TestJa = "循環ポンプ試験", TestEn = "Pump test",
                LaborFee = 4100,
                HelperAbilities = new[] { CreatureAbility.Cool, CreatureAbility.Water },
                TestDependsOn = new[] { "R01" },
                DiagramPos = new Vector2(0.64f, 0.6f),
                DiagramColor = new Color(0.4f, 0.9f, 1f)
            }.Need(SharedItemIds.PartCoolingMembrane, 3).Need(SharedItemIds.PartConnectorKit, 2));

            // R04 燃料精製装置（燃料ライン）：8,000G ＝ 接続部品1,200G ＋ 作業費6,800G
            Devices.Add(new DeviceDef
            {
                Id = "R04", Index = 3,
                NameJa = "燃料精製装置", NameEn = "Fuel Refining Line",
                BrokenJa = "燃料ラインが空で、精製装置が止まっている。この星の燃料に合わせて調整が必要。",
                BrokenEn = "The fuel line is empty and the refiner is offline. It needs tuning for local fuel.",
                FunctionJa = "燃料がラインを満たし、主エンジンへ供給できるようになる。",
                FunctionEn = "Fuel fills the line and can be fed to the main engine.",
                TestJa = "燃料供給試験", TestEn = "Fuel feed test",
                LaborFee = 6800,
                HelperAbilities = new[] { CreatureAbility.Press, CreatureAbility.Ferment, CreatureAbility.Heat },
                TestDependsOn = new[] { "R01" },
                TestNeedsFuel = true,
                DiagramPos = new Vector2(0.78f, 0.72f),
                DiagramColor = new Color(1f, 0.7f, 0.25f)
            }.Need(SharedItemIds.FuelStarHoney, 3).Need(SharedItemIds.PartConnectorKit, 1));

            // R05 主エンジン：16,000G ＝ 精密部品3,000G×2 ＋ 作業費10,000G
            Devices.Add(new DeviceDef
            {
                Id = "R05", Index = 4,
                NameJa = "主エンジン", NameEn = "Main Engine",
                BrokenJa = "燃焼室が割れ、エネルギー中核が失われている。",
                BrokenEn = "The combustion chamber is cracked and the energy core is lost.",
                FunctionJa = "地上でエンジンの起動試験ができる。",
                FunctionEn = "The engine can be test-fired on the ground.",
                TestJa = "エンジン試運転", TestEn = "Engine test fire",
                LaborFee = 10000,
                HelperAbilities = new[] { CreatureAbility.Heat },
                TestDependsOn = new[] { "R01", "R03", "R04" }, // §10.2：電源と冷却、燃料供給には燃料ライン
                TestNeedsFuel = true,
                BigEvent = true,
                DiagramPos = new Vector2(0.5f, 0.86f),
                DiagramColor = new Color(1f, 0.5f, 0.3f)
            }.Need(SharedItemIds.PartHeatproofFrame, 4).Need(SharedItemIds.PartCrystalCore, 2).Need(SharedItemIds.PartPrecisionParts, 2));

            // R06 次元観測装置：14,500G ＝ 観測部品3,500G×2 ＋ 作業費7,500G
            Devices.Add(new DeviceDef
            {
                Id = "R06", Index = 5,
                NameJa = "次元観測装置", NameEn = "Dimensional Observer",
                BrokenJa = "アンテナが折れ、揺らぎセンサーが失われている。航路データ自体は無事。",
                BrokenEn = "The antenna is snapped and the ripple sensor is lost. Route data is intact.",
                FunctionJa = "保存された航路データから、故郷への経路を検出できる。",
                FunctionEn = "The saved route data can be used to find the way home.",
                TestJa = "経路検出試験", TestEn = "Route detection test",
                LaborFee = 7500,
                HelperAbilities = new[] { CreatureAbility.Sense, CreatureAbility.Echo },
                TestDependsOn = new[] { "R01" },
                BigEvent = true,
                DiagramPos = new Vector2(0.5f, 0.1f),
                DiagramColor = new Color(0.6f, 0.8f, 1f)
            }.Need(SharedItemIds.PartRippleSensor, 2).Need(SharedItemIds.PartObservationParts, 2));

            // R07 位相安定装置：9,000G ＝ 位相制御部品4,000G ＋ 作業費5,000G。生物の能力が必須（§7.2）
            Devices.Add(new DeviceDef
            {
                Id = "R07", Index = 6,
                NameJa = "位相安定装置", NameEn = "Phase Stabilizer",
                BrokenJa = "位相リングが歪み、次元航行中の船体を安定させられない。",
                BrokenEn = "The phase ring is warped; the hull cannot be stabilized in transit.",
                FunctionJa = "次元航行中の船体を安定させられる。",
                FunctionEn = "The hull can be stabilized during dimensional travel.",
                TestJa = "位相同調試験", TestEn = "Phase tuning test",
                LaborFee = 5000,
                RequiredAbilities = new[] { CreatureAbility.Stabilize, CreatureAbility.Echo },
                TestDependsOn = new[] { "R01" },
                BigEvent = true,
                DiagramPos = new Vector2(0.5f, 0.32f),
                DiagramColor = new Color(0.8f, 0.55f, 1f)
            }.Need(SharedItemIds.PartPhaseControlUnit, 1));

            // R08 最終起動：全装置の修理＋燃料の必要量＋最終試験
            Devices.Add(new DeviceDef
            {
                Id = FinalDeviceId, Index = 7,
                NameJa = "最終起動・帰還準備", NameEn = "Final Startup",
                BrokenJa = "全装置が復旧し、燃料が満ちれば最終起動試験ができる。",
                BrokenEn = "Once every system is restored and fuel is full, the final startup test can run.",
                FunctionJa = "帰還操作が有効になる。",
                FunctionEn = "The return sequence becomes available.",
                TestJa = "最終起動試験", TestEn = "Final startup test",
                IsFinal = true,
                BigEvent = true,
                DiagramPos = new Vector2(0.5f, 0.5f),
                DiagramColor = new Color(0.5f, 1f, 0.6f)
            });
        }

        // ------------------------------------------------------------
        // 購入部品（登録担当：Rocket。後半ほど高額 §8.4）
        // ------------------------------------------------------------
        private static void BuildPurchasedParts()
        {
            PurchasedParts.Add(new PurchasedPartDef
            {
                ItemId = SharedItemIds.PartSmallControlUnit, Price = 500,
                NameJa = "小型制御部品", NameEn = "Small Control Unit",
                DescJa = "修理屋が組んだ小さな制御回路。補助電源に使える。",
                DescEn = "A small control circuit assembled by the mechanic. Fits the auxiliary power.",
                Color = new Color(0.35f, 0.8f, 0.45f), Shape = PrimitiveType.Cube
            });
            PurchasedParts.Add(new PurchasedPartDef
            {
                ItemId = SharedItemIds.PartConnectorKit, Price = 1200,
                NameJa = "接続部品セット", NameEn = "Connector Kit",
                DescJa = "配管や配線をつなぐ継ぎ手の詰め合わせ。",
                DescEn = "A set of joints for connecting pipes and wiring.",
                Color = new Color(0.75f, 0.75f, 0.8f), Shape = PrimitiveType.Cylinder
            });
            PurchasedParts.Add(new PurchasedPartDef
            {
                ItemId = SharedItemIds.PartPrecisionParts, Price = 3000,
                NameJa = "精密部品", NameEn = "Precision Parts",
                DescJa = "四本の腕で組み上げた、とても細かい部品。エンジンに使う。",
                DescEn = "Very fine parts assembled with four arms. Used in the engine.",
                Color = new Color(0.95f, 0.75f, 0.3f), Shape = PrimitiveType.Sphere
            });
            PurchasedParts.Add(new PurchasedPartDef
            {
                ItemId = SharedItemIds.PartObservationParts, Price = 3500,
                NameJa = "観測部品", NameEn = "Observation Parts",
                DescJa = "レンズと受信器の組み合わせ。次元観測装置に使う。",
                DescEn = "Lenses and receivers. Used in the dimensional observer.",
                Color = new Color(0.45f, 0.65f, 1f), Shape = PrimitiveType.Capsule
            });
            PurchasedParts.Add(new PurchasedPartDef
            {
                ItemId = SharedItemIds.PartPhaseControlUnit, Price = 4000,
                NameJa = "位相制御部品", NameEn = "Phase Control Unit",
                DescJa = "揺らぎを整える不思議な制御部品。位相安定装置に使う。",
                DescEn = "A curious unit that evens out fluctuations. Used in the phase stabilizer.",
                Color = new Color(0.75f, 0.45f, 1f), Shape = PrimitiveType.Sphere
            });
        }

        // ------------------------------------------------------------
        // 性質のヒント（§13：診断は性質を知っていて、現地の名前は知らない）
        // ------------------------------------------------------------
        private static void AddHint(string itemId, string pja, string pen, string wja, string wen, string raw, params SourceKind[] sources)
        {
            Hints[itemId] = new MaterialHint
            {
                ItemId = itemId,
                PropertyJa = pja, PropertyEn = pen,
                WhereJa = wja, WhereEn = wen,
                RawItemId = raw,
                Sources = sources
            };
        }

        private static void BuildHints()
        {
            // 加工品（登録担当：Cooking）
            AddHint(SharedItemIds.PartConductiveWire,
                "電気をよく通す細い導線", "A thin wire that conducts electricity well",
                "浅い地層の電気を通す鉱物を製錬すれば作れそう", "Could be smelted from a conductive mineral in shallow strata",
                SharedItemIds.OreConductive, SourceKind.Mining, SourceKind.Processing);
            AddHint(SharedItemIds.PartHullPlate,
                "衝撃に強い硬い金属の板", "A hard metal plate that withstands impact",
                "中くらいの深さの丈夫な金属を、鍛えて板にする", "Forge a sturdy metal from mid-depth strata into plates",
                SharedItemIds.OreSturdyMetal, SourceKind.Mining, SourceKind.Processing);
            AddHint(SharedItemIds.PartCoolingMembrane,
                "温度差に強い膜", "A membrane that tolerates temperature differences",
                "温度差に強い膜を持つ水中の生物素材を、洗って加工する", "Wash and shape a membrane from an aquatic creature that resists temperature change",
                SharedItemIds.FishShimomadara, SourceKind.Fishing, SourceKind.Processing);
            AddHint(SharedItemIds.FuelStarHoney,
                "高いエネルギーを蓄えた液体燃料", "A liquid fuel holding a lot of energy",
                "エネルギーを蓄える植物の実を、圧搾・発酵・加熱で精製する（生物の力を借りられる）", "Refine an energy-rich fruit by pressing, fermenting and heating (creatures can help)",
                SharedItemIds.CropStarHoneyFruit, SourceKind.Farming, SourceKind.Creatures, SourceKind.Processing);
            AddHint(SharedItemIds.PartHeatproofFrame,
                "高温に耐える構造材", "A structural member that withstands high heat",
                "深い地層の熱に強い金属を、加熱・成形・冷却する", "Heat, shape and cool a heat-resistant metal from deep strata",
                SharedItemIds.OreHeatproofMetal, SourceKind.Mining, SourceKind.Processing);
            AddHint(SharedItemIds.PartCrystalCore,
                "高密度のエネルギーを蓄える結晶体", "A crystal body storing dense energy",
                "最も深い地層の結晶を加工する", "Process a crystal from the deepest strata",
                SharedItemIds.OreEnergyCrystal, SourceKind.Mining, SourceKind.Processing);
            AddHint(SharedItemIds.PartRippleSensor,
                "空間の揺らぎを感じ取る感覚素材", "A sensory material that feels ripples in space",
                "波や揺らぎを感じる器官を持つ、珍しい水中の生物素材を精密加工する", "Precisely process an organ from a rare aquatic creature that senses ripples",
                SharedItemIds.FishSazanamime, SourceKind.Fishing, SourceKind.Processing);

            // 購入部品
            foreach (var p in PurchasedParts)
            {
                AddHint(p.ItemId,
                    "規格の合う精密な部品", "A precise part of compatible spec",
                    "この星の機械に詳しい職人から手に入りそう", "Probably obtainable from a local machine expert",
                    null, SourceKind.Mechanic, SourceKind.Money);
            }

            // 原料（登録担当：Mining / Fishing / Farming）
            AddHint(SharedItemIds.OreConductive, "電気を通す鉱物", "A conductive mineral", "鉱山の浅い階", "Shallow mine floors", null, SourceKind.Mining);
            AddHint(SharedItemIds.OreSturdyMetal, "丈夫な金属を含む鉱物", "A mineral with sturdy metal", "鉱山の中ほどの階", "Middle mine floors", null, SourceKind.Mining);
            AddHint(SharedItemIds.OreHeatproofMetal, "熱に強い金属を含む鉱物", "A mineral with heat-resistant metal", "鉱山の深い階", "Deep mine floors", null, SourceKind.Mining);
            AddHint(SharedItemIds.OreEnergyCrystal, "エネルギーを蓄えた結晶", "An energy-storing crystal", "鉱山の最深部", "The deepest mine floors", null, SourceKind.Mining);
            foreach (var id in SharedItemIds.FishCoolingCandidates)
                AddHint(id, "温度差に強い膜を持つ水中の生き物", "An aquatic creature with a temperature-proof membrane", "冷たい水辺で釣れそう", "Probably caught near cold water", null, SourceKind.Fishing);
            foreach (var id in SharedItemIds.FishSensorCandidates)
                AddHint(id, "揺らぎを感じる器官を持つ水中の生き物", "An aquatic creature that senses ripples", "水辺で釣れそう（場所や時間で変わるかも）", "Probably caught at the water (place or time may matter)", null, SourceKind.Fishing);
            AddHint(SharedItemIds.CropStarHoneyFruit, "エネルギーを蓄える植物の実", "An energy-storing fruit", "畑で育てられそう", "Probably grown on a farm", null, SourceKind.Farming);
        }

        // ------------------------------------------------------------
        // 修理屋への加工依頼【仮数値】（自分で加工するより割高。翌朝に受け取り）
        // ------------------------------------------------------------
        private static void BuildProxyRecipes()
        {
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.wire", InputItemId = SharedItemIds.OreConductive, InputCount = 2, Fee = 150, OutputItemId = SharedItemIds.PartConductiveWire, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.hull", InputItemId = SharedItemIds.OreSturdyMetal, InputCount = 3, Fee = 400, OutputItemId = SharedItemIds.PartHullPlate, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.membrane", InputItemId = SharedItemIds.FishShimomadara, InputCount = 2, Fee = 500, OutputItemId = SharedItemIds.PartCoolingMembrane, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.membrane.fine", InputItemId = SharedItemIds.FishHiyashiuo, InputCount = 1, Fee = 500, OutputItemId = SharedItemIds.PartCoolingMembrane, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.fuel", InputItemId = SharedItemIds.CropStarHoneyFruit, InputCount = 3, Fee = 300, OutputItemId = SharedItemIds.FuelStarHoney, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.frame", InputItemId = SharedItemIds.OreHeatproofMetal, InputCount = 3, Fee = 900, OutputItemId = SharedItemIds.PartHeatproofFrame, OutputCount = 1, Days = 1 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.core", InputItemId = SharedItemIds.OreEnergyCrystal, InputCount = 2, Fee = 1500, OutputItemId = SharedItemIds.PartCrystalCore, OutputCount = 1, Days = 2 });
            // 振動センサー：候補魚4種のどれでも頼める（魚50種原本 §9.1）
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.sensor", InputItemId = SharedItemIds.FishSazanamime, InputCount = 1, Fee = 1800, OutputItemId = SharedItemIds.PartRippleSensor, OutputCount = 1, Days = 2 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.sensor.otonashi", InputItemId = SharedItemIds.FishOtonashi, InputCount = 1, Fee = 1800, OutputItemId = SharedItemIds.PartRippleSensor, OutputCount = 1, Days = 2 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.sensor.hamoriuo", InputItemId = SharedItemIds.FishHamoriuo, InputCount = 1, Fee = 1800, OutputItemId = SharedItemIds.PartRippleSensor, OutputCount = 1, Days = 2 });
            ProxyRecipes.Add(new ProxyRecipe { Id = "proxy.sensor.iwanaki", InputItemId = SharedItemIds.FishIwanaki, InputCount = 1, Fee = 1800, OutputItemId = SharedItemIds.PartRippleSensor, OutputCount = 1, Days = 2 });
        }

        public static string StatusLabel(DeviceStatus s)
        {
            switch (s)
            {
                case DeviceStatus.Broken: return Loc.T("故障中", "Broken");
                case DeviceStatus.Investigating: return Loc.T("調査中", "Investigating");
                case DeviceStatus.Collecting: return Loc.T("素材収集中", "Collecting");
                case DeviceStatus.ReadyToAssemble: return Loc.T("組立可能", "Ready to install");
                case DeviceStatus.Repaired: return Loc.T("修理済み", "Repaired");
                case DeviceStatus.Verified: return Loc.T("動作確認済み", "Verified");
                default: return s.ToString();
            }
        }

        public static Color StatusColor(DeviceStatus s)
        {
            switch (s)
            {
                case DeviceStatus.Broken: return new Color(1f, 0.45f, 0.45f);
                case DeviceStatus.Investigating: return new Color(1f, 0.7f, 0.45f);
                case DeviceStatus.Collecting: return new Color(1f, 0.9f, 0.45f);
                case DeviceStatus.ReadyToAssemble: return new Color(0.55f, 0.85f, 1f);
                case DeviceStatus.Repaired: return new Color(0.55f, 0.95f, 0.6f);
                case DeviceStatus.Verified: return new Color(0.4f, 1f, 0.85f);
                default: return Color.white;
            }
        }

        public static string SourceLabel(SourceKind k)
        {
            switch (k)
            {
                case SourceKind.Fishing: return Loc.T("釣り", "Fishing");
                case SourceKind.Farming: return Loc.T("農業", "Farming");
                case SourceKind.Mining: return Loc.T("鉱山", "Mining");
                case SourceKind.Creatures: return Loc.T("生き物", "Creatures");
                case SourceKind.Money: return Loc.T("お金", "Money");
                case SourceKind.Processing: return Loc.T("加工", "Processing");
                case SourceKind.Mechanic: return Loc.T("修理屋", "Mechanic");
                default: return k.ToString();
            }
        }

        public static string AbilityLabel(CreatureAbility a)
        {
            switch (a)
            {
                case CreatureAbility.Stabilize: return Loc.T("安定させる", "Stabilize");
                case CreatureAbility.Echo: return Loc.T("反響させる", "Echo");
                case CreatureAbility.Glow: return Loc.T("光る", "Glow");
                case CreatureAbility.Heat: return Loc.T("温める", "Heat");
                case CreatureAbility.Cool: return Loc.T("冷やす", "Cool");
                case CreatureAbility.Water: return Loc.T("水を出す", "Water");
                case CreatureAbility.Press: return Loc.T("圧搾", "Press");
                case CreatureAbility.Ferment: return Loc.T("発酵", "Ferment");
                case CreatureAbility.Sense: return Loc.T("感知", "Sense");
                case CreatureAbility.Stick: return Loc.T("張り付く", "Stick");
                default: return a.ToString();
            }
        }
    }
}
