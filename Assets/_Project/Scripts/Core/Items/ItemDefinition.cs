using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    public enum ItemCategory
    {
        Misc,
        Fish,
        Crop,
        Seed,
        Ore,
        Gem,
        Material,      // 加工素材（金属インゴット、繊維、魚の膜など）
        Food,          // 料理
        Processed,     // 加工食品（ジュース、ジャム、燻製など）
        Furniture,
        Decor,         // 壁紙・床・庭の装飾
        Tool,
        Bait,
        Fertilizer,
        RocketPart,    // ロケット修理部品・燃料
        CreatureFood,  // 宇宙生物の餌
        CreatureItem,  // 生物の住処・器具
        Gift,
        Letter,
        KeyItem,
        Clothing
    }

    /// <summary>
    /// アイテムの静的定義。各モジュールが RegisterContent() で ItemRegistry に登録する。
    /// 表示名はプレイヤーのUI言語（主人公自身が付けた呼び名という扱い）。
    /// 住民が話すときはその星の宇宙語の語（ConceptId から生成）を使う。
    /// </summary>
    public sealed class ItemDefinition
    {
        public string Id;
        public string NameJa;
        public string NameEn;
        public string DescriptionJa;
        public string DescriptionEn;
        public ItemCategory Category;
        /// <summary>基準価格（G）。【仮数値】商店の値付けの目安、住民の価値判断の基準。</summary>
        public int BasePrice;
        public int MaxStack = 99;
        /// <summary>希少度 0（ありふれた）〜5（伝説）。</summary>
        public int Rarity;
        /// <summary>食べ物として消費されるか（住民の継続需要が生まれる）。</summary>
        public bool Consumable;
        /// <summary>長く残る耐久品（家具・道具）か。</summary>
        public bool Durable;
        /// <summary>アイコン・簡易モデルの色。</summary>
        public Color Color = Color.white;
        /// <summary>簡易表示の形（Primitive の種類）。</summary>
        public PrimitiveType Shape = PrimitiveType.Cube;
        /// <summary>個別の概念ID（宇宙語の固有の語）。既定は "ITEM:" + Id。</summary>
        public string ConceptId;
        /// <summary>上位概念（Concepts.FISH など）。住民は「魚」と総称で言うこともある。</summary>
        public string GenericConceptId;
        /// <summary>分類・需要判定・クエスト条件用のタグ。ItemTags 定数を推奨。</summary>
        public readonly HashSet<string> Tags = new HashSet<string>();
        /// <summary>入手元の活動（"fishing" "farming" "mining" "creatures" "cooking" "shop" "rocket" など）。</summary>
        public string SourceActivity;
        /// <summary>モジュール固有の追加データ（例：FishSpecies、CropDefinition への参照）。</summary>
        public object ModuleData;

        public string Name => Loc.T(NameJa, NameEn);
        public string Description => Loc.T(DescriptionJa, DescriptionEn);
        public string EffectiveConceptId => string.IsNullOrEmpty(ConceptId) ? "ITEM:" + Id : ConceptId;

        public bool HasTag(string tag) => Tags.Contains(tag);

        public ItemDefinition WithTags(params string[] tags)
        {
            foreach (var t in tags) if (!string.IsNullOrEmpty(t)) Tags.Add(t);
            return this;
        }

        public override string ToString() => $"{Id} ({NameJa})";
    }

    /// <summary>よく使うタグ。モジュールは独自タグを追加してよい。</summary>
    public static class ItemTags
    {
        public const string Food = "food";
        public const string Edible = "edible";
        public const string Drink = "drink";
        public const string Sweet = "sweet";
        public const string Spicy = "spicy";
        public const string Sour = "sour";
        public const string Savory = "savory";
        public const string Glowing = "glowing";
        public const string Rare = "rare";
        public const string Big = "big";
        public const string Small = "small";
        public const string Cute = "cute";
        public const string Shiny = "shiny";
        public const string Fragrant = "fragrant";
        public const string Flower = "flower";
        public const string Fruit = "fruit";
        public const string Vegetable = "vegetable";
        public const string Grain = "grain";
        public const string Fish = "fish";
        public const string Ore = "ore";
        public const string Gem = "gem";
        public const string Metal = "metal";
        public const string Crystal = "crystal";
        public const string Fuel = "fuel";
        public const string Conductive = "conductive";
        public const string Heatproof = "heatproof";
        public const string Cooling = "cooling";
        public const string Sensor = "sensor";
        public const string Furniture = "furniture";
        public const string Seating = "seating";
        public const string Table = "table";
        public const string Bed = "bed";
        public const string Light = "light";
        public const string Storage = "storage";
        public const string Kitchen = "kitchen";
        public const string Aquarium = "aquarium";
        public const string Plant = "plant";
        public const string Music = "music";
        public const string Toy = "toy";
        public const string Outdoor = "outdoor";
        public const string CreatureHabitat = "creature_habitat";
        public const string Gift = "gift";
        public const string Precision = "precision";
        public const string RocketMaterial = "rocket_material";
        public const string Sellable = "sellable";
        public const string NotSellable = "not_sellable";
        public const string Ingredient = "ingredient";
        public const string Seed = "seed";
        public const string Tool = "tool";

        // 色（宇宙語の形容詞・住民の好みと連動）
        public const string ColorRed = "color_red";
        public const string ColorBlue = "color_blue";
        public const string ColorGreen = "color_green";
        public const string ColorYellow = "color_yellow";
        public const string ColorWhite = "color_white";
        public const string ColorBlack = "color_black";
        public const string ColorPurple = "color_purple";
        public const string ColorOrange = "color_orange";
        public const string ColorPink = "color_pink";
    }
}
