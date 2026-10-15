using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    // =====================================================================
    // 料理・加工の基本データ型（料理・加工仕様 v0.2）
    // 「完成品 = 食材・素材 × 加工能力 × 器具・設備 × 加工手順（順番・程度）」（§5.1）
    // 工程（CraftStep）は宇宙生物の能力（CreatureAbility）で満たす（§2・§3.2：能力は活動ごとに分断しない）。
    // 数値はすべて【仮数値】。調整は CookingTuning とテーブル（StepInfo / StationProfiles）で行う。
    // =====================================================================

    /// <summary>加工の工程。生物の能力・設備・時間で満たす。</summary>
    public enum CraftStep
    {
        Heat,     // 焼く・煮る・製錬（ポカリス等の「温める」）
        Water,    // 水を加える・洗う・蒸す（ミズプク等の「水を出す」）
        Grind,    // 粉砕・製粉・こねる・紡ぐ（クルマリの回転）【派生案】
        Bubble,   // 泡立て（アワリン）【派生案】
        Press,    // しぼる（担当生物は Creatures が割当）【未決定→提案】
        Ferment,  // 発酵（時間がかかる）【未決定→提案】
        Cool,     // 冷やす・焼き入れ【未決定→提案】
        Dry,      // 乾燥（時間がかかる。ふわふわ浮く生物で早まる）【提案】
        Smoke,    // 燻煙（時間がかかる。影の生物・温める生物で早まる）【未決定→提案】
        Glow,     // 光らせる（ヒカリムシ等）
        Stack,    // 積む・盛り付け・織る（ツミツミ）
        Shape,    // 成形・和える・巻く（手作業）
        Polish,   // 研磨・精密加工（工具。鉱石粉の生物で上手になる）
        Pack      // 包む・詰め合わせ（手作業。ハコネで上手になる）
    }

    /// <summary>ミニゲームの手触りの種類（§6.1：能力ごとに違う手触り）。</summary>
    public enum MinigameKind
    {
        None,        // 仕込みだけ（時間の工程）
        HoldBand,    // 長押しで目盛りを保つ（火加減・しぼり加減）
        StopAtLine,  // ちょうどの所で止める（水量・成形・冷え具合）
        Mash,        // 連打で目標の範囲へ（回転・泡立て・研磨）
        Rhythm       // 光や積み上げの拍に合わせる
    }

    public enum RecipeKind
    {
        Dish,        // 料理
        Processed,   // 加工食品（ジュース・ジャム・干物・発酵品・調味料）
        Material,    // 非食品の素材加工（§9）
        RocketPart,  // ロケット修理部品・燃料（ロケット仕様 §9）
        Device,      // 調理器具（家具）
        Accidental   // 意図と違う結果（§5.5）
    }

    /// <summary>工程の静的情報。</summary>
    public sealed class StepInfo
    {
        public CraftStep Step;
        public string NameJa, NameEn;
        public string Glyph;
        public Color Color;
        /// <summary>この工程を満たす生物の能力（どれか1つ）。空なら手作業・設備のみ。</summary>
        public CreatureAbility[] Abilities = new CreatureAbility[0];
        /// <summary>時間経過で進む工程か（§8.1）。</summary>
        public bool Timed;
        /// <summary>生物なし（設備まかせ）の所要時間【仮数値・ゲーム内時間】。</summary>
        public float BaseHours;
        /// <summary>担当生物がいる時の所要時間【仮数値】。</summary>
        public float FastHours;
        public MinigameKind Minigame;
        /// <summary>ミニゲーム中の生物のようす（擬音・動作。宇宙語ではない）。</summary>
        public string ActionJa, ActionEn;
        public string Name => Loc.T(NameJa, NameEn);
        public string Action => Loc.T(ActionJa, ActionEn);
    }

    /// <summary>能力の見た目（アイコン代わりの1文字＋色）と、放置中のくらし（§4.3）。</summary>
    public sealed class AbilityInfo
    {
        public CreatureAbility Ability;
        public string Glyph;
        public Color Color;
        public string NameJa, NameEn;
        public string IdleJa, IdleEn;
        public string Name => Loc.T(NameJa, NameEn);
        public string Idle => Loc.T(IdleJa, IdleEn);
    }

    /// <summary>材料の指定。固有アイテム指定とタグ／分類指定を併用する（「魚なら何でも」）。</summary>
    public sealed class IngredientSpec
    {
        public string ItemId;
        public string[] AnyTags;
        public ItemCategory[] Categories;
        public Func<ItemDefinition, bool> Predicate;
        public string LabelJa, LabelEn;
        public int Count = 1;
        /// <summary>見本として表示するアイテム（実演・アイコン用）。null なら探す。</summary>
        public string SampleItemId;

        public string Label => Loc.T(LabelJa, LabelEn);

        /// <summary>具体的なほど大きい（照合の優先順位）。</summary>
        public int Specificity
        {
            get
            {
                if (!string.IsNullOrEmpty(ItemId)) return 100;
                if (Predicate != null) return 30;
                if (AnyTags != null && AnyTags.Length == 1) return 40;
                return 20;
            }
        }

        public bool Matches(ItemDefinition def)
        {
            if (def == null) return false;
            if (!string.IsNullOrEmpty(ItemId)) return def.Id == ItemId;
            bool any = false;
            if (Predicate != null)
            {
                if (Predicate(def)) return true;
                any = true;
            }
            if (AnyTags != null && AnyTags.Length > 0)
            {
                foreach (var t in AnyTags) if (def.HasTag(t)) return true;
                any = true;
            }
            if (Categories != null && Categories.Length > 0)
            {
                foreach (var c in Categories) if (def.Category == c) return true;
                any = true;
            }
            return !any;
        }

        public IngredientSpec Times(int n) { Count = Mathf.Max(1, n); return this; }

        /// <summary>見本のアイテムID（実演・レシピ表示用）。</summary>
        public string FindSample()
        {
            if (!string.IsNullOrEmpty(ItemId)) return ItemId;
            if (!string.IsNullOrEmpty(SampleItemId) && ItemRegistry.Exists(SampleItemId)) return SampleItemId;
            foreach (var d in ItemRegistry.All)
            {
                if (d.Category == ItemCategory.Furniture || d.Category == ItemCategory.KeyItem) continue;
                if (Matches(d)) return d.Id;
            }
            return null;
        }
    }

    /// <summary>レシピ（作り方）。</summary>
    public sealed class RecipeDef
    {
        public string Id;
        public string NameJa, NameEn;
        public string OutputItemId;
        public int OutputCount = 1;
        public RecipeKind Kind;
        public readonly List<IngredientSpec> Ingredients = new List<IngredientSpec>();
        public readonly List<CraftStep> Steps = new List<CraftStep>();
        public CraftStationType[] Stations = new CraftStationType[0];
        /// <summary>最初から知っている（村の共用調理場の基本料理、ロケット診断で分かる部品など）。</summary>
        public bool StartKnown;
        /// <summary>実験（自由調理）で発見できる。</summary>
        public bool Experimentable = true;
        /// <summary>料理人の住民が実演で見せてくれる（§11・§12.1）。</summary>
        public bool Demonstrable;
        /// <summary>職人へ依頼する時の手数料（0なら依頼不可）【仮数値】。</summary>
        public int CommissionFee;
        /// <summary>この天候・現象の時だけ成立する（§8.2：珍しい天候は追加の発見）。</summary>
        public AlienPhenomenon RequiredPhenomenon;
        /// <summary>晴れの時だけ成立する。</summary>
        public bool RequiresSunny;
        /// <summary>この現象の時は品質が上がる。</summary>
        public AlienPhenomenon BonusPhenomenon;
        public bool SunnyBonus;
        public bool RainBonus;
        /// <summary>食べた時の能力補助（§7.2【提案】）。null ならなし。</summary>
        public string FoodEffectId;
        public int FoodEffectMinutes = 120;
        /// <summary>材料によって完成品が変わる（染料の色など）。</summary>
        public Func<IList<string>, string> OutputSelector;
        /// <summary>OutputSelector で出る可能性のある完成品（RecipesProducing 用）。</summary>
        public readonly List<string> AlsoProduces = new List<string>();
        /// <summary>同時に成立した時の優先度。</summary>
        public int Priority;
        public string NoteJa, NoteEn;

        public string Name => Loc.T(NameJa, NameEn);

        public bool AllowsStation(CraftStationType t)
        {
            foreach (var s in Stations) if (s == t) return true;
            return false;
        }

        public bool HasTimedStep
        {
            get
            {
                foreach (var s in Steps) if (CookingTables.Step(s).Timed) return true;
                return false;
            }
        }

        public int TotalIngredientCount
        {
            get
            {
                int n = 0;
                foreach (var i in Ingredients) n += i.Count;
                return n;
            }
        }

        public string ResolveOutput(IList<string> usedItemIds)
        {
            if (OutputSelector != null && usedItemIds != null)
            {
                var o = OutputSelector(usedItemIds);
                if (!string.IsNullOrEmpty(o)) return o;
            }
            return OutputItemId;
        }

        public RecipeDef In(params CraftStationType[] stations) { Stations = stations; return this; }
        public RecipeDef Need(params IngredientSpec[] specs) { Ingredients.AddRange(specs); return this; }
        public RecipeDef Do(params CraftStep[] steps) { Steps.AddRange(steps); return this; }
    }

    /// <summary>調理器具（家具として置く。§4.1：器具は能力を引き出す生物の居場所・作業場）。</summary>
    public sealed class DeviceDef
    {
        public string ItemId;
        public string NameJa, NameEn, DescJa, DescEn;
        /// <summary>この器具に住む生物の能力（方式A：生物と器具の一体化）。</summary>
        public CreatureAbility? Ability;
        /// <summary>時間の工程を置ける器具（乾燥棚・発酵樽・燻製箱）。</summary>
        public CraftStep? TimedStep;
        public int TimedSlots;
        /// <summary>この器具を開いた時のステーション種別（既定 Kitchen。精製器は Refinery、作業台は Workbench）。</summary>
        public CraftStationType Station = CraftStationType.Kitchen;
        public int Price;
        public Color Color;
        public PrimitiveType Shape = PrimitiveType.Cube;
        public ResidentJob[] Sellers = new ResidentJob[0];
        public string Name => Loc.T(NameJa, NameEn);
    }

    /// <summary>ステーション種別ごとの性質。</summary>
    public sealed class StationProfile
    {
        public CraftStationType Type;
        public string NameJa, NameEn;
        /// <summary>生物なしで出来る工程（設備・手作業）。</summary>
        public CraftStep[] BuiltIn = new CraftStep[0];
        /// <summary>生物がいなくても、設備にまかせて時間をかければ出来る工程。</summary>
        public CraftStep[] SlowCapable = new CraftStep[0];
        /// <summary>時間の工程（乾燥・発酵など）を置ける。</summary>
        public CraftStep[] TimedSteps = new CraftStep[0];
        public int TimedSlots;
        public int BatchMax = 3;
        public bool Outdoor;
        /// <summary>職人に依頼できる（0=None なら依頼なし）。</summary>
        public ResidentJob CommissionJob = ResidentJob.None;
        /// <summary>自分で設備を借りる時の使用料（鍛冶屋の炉など）【仮数値】。</summary>
        public int UseFee;
        /// <summary>品質の補正（共用調理場は少し控えめ）。</summary>
        public float QualityOffset;
        /// <summary>生物を「作業場に置く」ことができる配置（修理作業場）。</summary>
        public bool AcceptsWorkshopCreatures;
        public string Name => Loc.T(NameJa, NameEn);

        public bool Has(CraftStep[] arr, CraftStep s)
        {
            foreach (var x in arr) if (x == s) return true;
            return false;
        }
    }

    /// <summary>食べた時の能力補助（§7.2【提案】。スタミナ回復は不採用：基本決定 §4）。</summary>
    public sealed class FoodEffectDef
    {
        public string Id;
        public string NameJa, NameEn, DescJa, DescEn;
        public string Glyph;
        public Color Color;
        /// <summary>このモジュール内だけで効果が出るもの（false は他モジュールの対応待ち）。</summary>
        public bool ImplementedHere;
        public string Name => Loc.T(NameJa, NameEn);
        public string Desc => Loc.T(DescJa, DescEn);
    }

    /// <summary>料理・加工モジュール独自のタグ（住民の好み判定・商店の需要に使える）。</summary>
    public static class CookingTags
    {
        public const string Dish = "dish";
        public const string Processed = "processed";
        public const string Mystery = "cooking_mystery";
        public const string Accidental = "cooking_accidental";
        public const string Hot = "temp_hot";
        public const string Cold = "temp_cold";
        public const string Fluffy = "texture_fluffy";
        public const string Jiggly = "texture_jiggly";
        public const string Crispy = "texture_crispy";
        public const string Chewy = "texture_chewy";
        public const string Smooth = "texture_smooth";
        public const string Fermented = "fermented";
        public const string Dried = "dried";
        public const string Smoked = "smoked";
        public const string Seasoning = "seasoning";
        public const string KitchenDevice = "kitchen_device";
        public const string CraftStation = "craft_station";
        public const string Dye = "dye";
        public const string Fiber = "fiber";
        public static string AbilityTag(CreatureAbility a) => "ability_" + a.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// 調整用の仮数値（【仮数値】すべて）。
    /// </summary>
    public static class CookingTuning
    {
        /// <summary>食べた時の能力補助を使うか（§7.2 は未決定。提案扱いなので無効化できる）。</summary>
        public static bool FoodEffectsEnabled = true;
        /// <summary>食の記録（コレクション）を付けるか【提案】。</summary>
        public static bool FoodLogEnabled = true;
        /// <summary>前の星で覚えたレシピを次の星へ持ち越す（§14【提案】）。</summary>
        public static bool CarryRecipesAcrossPlanets = true;

        /// <summary>キッチンの器具同士を「同じ台所」とみなす距離（m）。方式C：複数生物の協力。</summary>
        public const float KitchenLinkRadius = 7f;
        /// <summary>他プレイヤーの同行生物を借りられる距離（共同調理【提案】）。</summary>
        public const float CoopRadius = 6f;
        /// <summary>まとめ調理：ハコバチの搬送（Carry）があると増える上限。</summary>
        public const int CarryBatchBonus = 5;
        /// <summary>器具の時間加工スロット数（既定）。</summary>
        public const int DeviceTimedSlots = 3;
        /// <summary>設備まかせ（生物なし）で1工程に掛かる時間（時間）。</summary>
        public const float SlowHoursPerStep = 18f;

        // ---- 品質（0〜3）の計算 ----
        public const float MinigameWeight = 1.2f;       // ミニゲームの出来（0〜1）× この値
        public const float AutoCookScore = 0.45f;        // 2回目以降のまとめ調理での「いつもの出来」
        public const float DeviceBonus = 0.25f;          // 生物が対応する器具に住んでいる（方式A）
        public const float CreatureLevelBonus = 0.12f;   // 生物の能力レベル1あたり（Lv1を基準）
        public const float MasteryBonus = 0.08f;         // 調理習熟1段あたり（§6.3）
        public const float WeatherBonus = 0.35f;         // 天候の恵み（§8.2）
        public const float CommissionQuality = 1f;       // 職人に頼んだ時の品質
        public const int MasteryUsesPerLevel = 8;        // 習熟1段に必要な回数
        public const int MasteryMaxLevel = 5;

        // ---- 記録の上限 ----
        public const int MaxTryRecords = 150;
        public const int MaxExperimentIngredients = 5;
        public const int MaxExperimentSteps = 6;

        // ---- 住民 ----
        /// <summary>料理人の実演を見られる間隔（日）。</summary>
        public const int DemoIntervalDays = 1;
        public const int DemoFriendship = 1;

        // ---- 食べ物の効果 ----
        public const float SwiftMultiplier = 1.15f;
    }
}
