using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Shop
{
    /// <summary>陳列台の種類（商店仕様 §8 店舗拡張）。</summary>
    public enum ShopDisplayType
    {
        Normal,          // ふつうの棚
        Chilled,         // 冷蔵棚：魚・作物・料理の鮮度が落ちない
        Aquarium,        // 水槽：魚が生き生きと見え、目を引く
        FurnitureStage,  // 家具ステージ：大きな家具・装飾を置ける
        Specials         // 特売コーナー：基準より安い商品がとても目立つ
    }

    /// <summary>
    /// 商店の調整用の仮数値をまとめた場所。【仮数値】はすべてここで調整する。
    /// （商店仕様 §3・§8・§13、ゲームバランス仕様 §6〜§7）
    /// </summary>
    public static class ShopConfig
    {
        // ---------------- 棚・倉庫 ----------------
        /// <summary>初期の棚の枠数【仮数値】。</summary>
        public const int InitialSlotCount = 6;
        /// <summary>倉庫箱の初期容量【仮数値】。</summary>
        public const int InitialStorageCapacity = 24;
        /// <summary>1回の購入で買う最大個数【仮数値】。</summary>
        public const int MaxQtyPerPurchase = 4;
        public const int MaxPrice = 999999;

        public static int MaxPerSlot(ShopDisplayType t)
        {
            switch (t)
            {
                case ShopDisplayType.Aquarium: return 10;
                case ShopDisplayType.FurnitureStage: return 3;
                default: return 20;
            }
        }

        // ---------------- 営業時間 ----------------
        public const int DefaultOpenHour = 9;
        public const int DefaultCloseHour = 18;
        /// <summary>営業時間を設定できる範囲（灯りの改装段階ごと）【仮数値】。</summary>
        public static readonly int[] EarliestOpenByLights = { 8, 6, 5 };
        public static readonly int[] LatestCloseByLights = { 20, 22, 24 };
        public const int MinOpenSpanHours = 2;

        // ---------------- 記録の保存期間 ----------------
        /// <summary>マネピが商店の伝言を覚えている日数（タブレット仕様 §4.3：記憶件数は未決定）【仮数値】。</summary>
        public const int ManepiKeepDays = 7;
        public const int VisitLogKeepDays = 14;
        public const int MaxVisitLog = 400;
        public const int DailySummaryKeepDays = 60;
        public const int RequestExpireDays = 5;
        public const int MaxOpenRequests = 12;

        // ---------------- 購買判断（バランス仕様 §7.2）【仮数値】 ----------------
        /// <summary>この比率（値段÷その住民の感じる価値）以下だと「安い」と驚く。</summary>
        public const float CheapRatio = 0.65f;
        /// <summary>価格の受け入れ曲線の急さ。大きいほど「高い」と感じる境界がはっきりする。</summary>
        public const float AcceptCurveSteepness = 6f;
        /// <summary>この欲しさ以上の商品だけ、手に取って検討する。</summary>
        public const float InterestThreshold = 0.35f;
        /// <summary>1回の来店で手に取って検討する商品の数。</summary>
        public const int MaxConsiderPerVisit = 3;
        /// <summary>住民が1日に使う額の上限＝所持金×この割合（ただし下限あり）。無限購買を防ぐ（バランス §6.3, §7.5）。</summary>
        public const float DailySpendFraction = 0.55f;
        public const int MinDailySpendCap = 120;
        /// <summary>予算の指定が無い時：所持金×割合（節約家ほど低い）。</summary>
        public const float DefaultBudgetFractionFree = 0.38f;
        public const float DefaultBudgetFractionThrifty = 0.16f;
        public const int DefaultBudgetBase = 30;
        /// <summary>同じ日に同じ消耗品を買った後の欲しさの低下（満腹・在庫あり）。</summary>
        public const float SatiationPenalty = 0.35f;
        /// <summary>既に持っている耐久品（家具・道具）の欲しさの低下（バランス §7.3）。</summary>
        public const float OwnedDurablePenalty = 0.7f;
        /// <summary>夜（20時〜6時）の購入意欲の倍率。</summary>
        public const float NightBuyMultiplier = 0.85f;
        /// <summary>関連商品のまとめ買いの基本確率（商店仕様 §8 レイアウト効果）。</summary>
        public const float BundleChance = 0.35f;

        // ---------------- 鮮度（冷蔵棚の意味付け。鮮度の詳細は商店仕様 §2 で未決定）【仮数値】 ----------------
        public const int FreshDays = 2;
        public const float FreshDecayPerDay = 0.15f;
        public const float MinFreshness = 0.5f;
        public const float NotFreshThreshold = 0.8f;
        /// <summary>この日数以内に並べた物は「新商品」（流行好きが反応）。</summary>
        public const int NewArrivalDays = 1;

        // ---------------- 立ち寄り（看板の効果）【仮数値】 ----------------
        /// <summary>立ち寄り判定の間隔（ゲーム内分）。</summary>
        public const int DropInCheckMinutes = 30;
        public const float DropInBaseRadius = 25f;
        public const float DropInRadiusPerSign = 12f;
        public const float DropInChancePerSign = 0.05f;
        public const float DropInChancePerDecor = 0.01f;
        /// <summary>住民モジュールからの来店呼び出しが2日以上無い時の保険の立ち寄り確率。</summary>
        public const float DropInFallbackChance = 0.06f;
        public const float DropInRequestMatchBonus = 0.25f;
        public const int DropInMaxPerCheck = 2;
        /// <summary>同じ住民が再び立ち寄るまでの最短間隔（ゲーム内分）。</summary>
        public const int RevisitCooldownMinutes = 180;
        /// <summary>立ち寄りの住民が入口に着くのを待つ最長時間（ゲーム内分）。</summary>
        public const int DropInDeadlineMinutes = 60;
        /// <summary>来店後に棚の前へ歩いて品定めする演出（住民モジュールの RequestGoTo を使う）。</summary>
        public const bool BrowseWalkEnabled = true;
        public const int BrowseMinutes = 10;

        // ---------------- 演出 ----------------
        /// <summary>この距離以内にプレイヤーがいると住民の反応が吹き出しで見える。</summary>
        public const float HearingRadius = 20f;
        /// <summary>この距離以内なら「店にいる」（マネピの再生・音声カード保存ができる）。</summary>
        public const float AtShopRadius = 14f;

        // ---------------- 改装（家具職人への依頼） ----------------
        /// <summary>改装費のうち家具職人の住民へ支払われる割合（残りは材料費。経済循環：バランス §7.6）【仮数値】。</summary>
        public const float CarpenterShare = 0.6f;

        public static readonly List<ShopUpgradeDef> Upgrades = new List<ShopUpgradeDef>
        {
            new ShopUpgradeDef(ShopUpgradeIds.Shelf, "棚の追加", "Extra shelves",
                "ふつうの棚を2枠増やす。", "Adds two regular shelf slots.",
                new[] { 800, 1500, 2500, 4000, 6000 }) { AddsDisplay = ShopDisplayType.Normal, SlotsPerLevel = 2 },
            new ShopUpgradeDef(ShopUpgradeIds.Chilled, "冷蔵棚", "Chilled case",
                "魚・作物・料理の鮮度が落ちない棚を1枠増やす。", "Adds a case that keeps fish, crops and dishes fresh.",
                new[] { 1800, 2600, 3600 }) { AddsDisplay = ShopDisplayType.Chilled, SlotsPerLevel = 1 },
            new ShopUpgradeDef(ShopUpgradeIds.Aquarium, "水槽", "Aquarium",
                "魚を泳がせて並べる水槽を1枠増やす。魚がよく目に留まる。", "Adds a tank for fish. Fish draw more attention.",
                new[] { 2500, 3800 }) { AddsDisplay = ShopDisplayType.Aquarium, SlotsPerLevel = 1 },
            new ShopUpgradeDef(ShopUpgradeIds.Stage, "家具ステージ", "Furniture stage",
                "大きな家具・装飾を置ける台を1枠増やす。", "Adds a stage for large furniture and decor.",
                new[] { 2000, 3000, 4500 }) { AddsDisplay = ShopDisplayType.FurnitureStage, SlotsPerLevel = 1 },
            new ShopUpgradeDef(ShopUpgradeIds.Specials, "特売コーナー", "Bargain corner",
                "基準より安い商品がとても目立つ台を1枠増やす。", "Adds a corner where discounted goods stand out.",
                new[] { 1200, 2200 }) { AddsDisplay = ShopDisplayType.Specials, SlotsPerLevel = 1 },
            new ShopUpgradeDef(ShopUpgradeIds.Storage, "倉庫の拡張", "Bigger storage",
                "倉庫箱を12枠広げる。", "Adds 12 storage slots.",
                new[] { 600, 1200, 2400, 4000 }),
            new ShopUpgradeDef(ShopUpgradeIds.Sign, "看板", "Signboard",
                "通りがかりの住民が立ち寄りやすくなり、商品も目に留まりやすくなる。", "Passers-by drop in more often and notice more goods.",
                new[] { 500, 1500, 3500 }),
            new ShopUpgradeDef(ShopUpgradeIds.Lights, "店の灯り", "Shop lights",
                "営業時間を朝早く・夜遅くまで延ばせる。", "Lets you open earlier and close later.",
                new[] { 1000, 2500 }),
            new ShopUpgradeDef(ShopUpgradeIds.AutoRestock, "補充の仕掛け", "Restock rig",
                "選んだ棚に、倉庫の同じ商品を同じ値段で自動で補充する（何をいくらで売るかは主人公が決める）。",
                "Refills chosen slots from storage with the same item at the same price (you still decide what and how much).",
                new[] { 3000 }),
            new ShopUpgradeDef(ShopUpgradeIds.Decor, "内装", "Interior decor",
                "店の雰囲気がよくなり、来店した住民の満足度と注目度が少し上がる。", "A cosier shop: customers are a little happier and more attentive.",
                new[] { 1000, 2500, 5000 }),
        };

        public static ShopUpgradeDef GetUpgrade(string id)
        {
            foreach (var u in Upgrades) if (u.Id == id) return u;
            return null;
        }
    }

    public static class ShopUpgradeIds
    {
        public const string Shelf = "shelf";
        public const string Chilled = "chilled";
        public const string Aquarium = "aquarium";
        public const string Stage = "stage";
        public const string Specials = "specials";
        public const string Storage = "storage";
        public const string Sign = "sign";
        public const string Lights = "lights";
        public const string AutoRestock = "auto_restock";
        public const string Decor = "decor";
    }

    /// <summary>改装の定義（家具職人への依頼）。</summary>
    public sealed class ShopUpgradeDef
    {
        public readonly string Id;
        public readonly string NameJa, NameEn, DescJa, DescEn;
        /// <summary>段階ごとの費用（配列の長さ＝最大段階）【仮数値】。</summary>
        public readonly int[] Costs;
        /// <summary>棚の枠を増やす改装なら、その陳列台の種類。</summary>
        public ShopDisplayType? AddsDisplay;
        public int SlotsPerLevel;

        public ShopUpgradeDef(string id, string nameJa, string nameEn, string descJa, string descEn, int[] costs)
        {
            Id = id;
            NameJa = nameJa;
            NameEn = nameEn;
            DescJa = descJa;
            DescEn = descEn;
            Costs = costs;
        }

        public int MaxLevel => Costs.Length;
        public string Name => Loc.T(NameJa, NameEn);
        public string Description => Loc.T(DescJa, DescEn);
        public int CostForNextLevel(int currentLevel) => currentLevel < 0 || currentLevel >= Costs.Length ? -1 : Costs[currentLevel];
    }
}
