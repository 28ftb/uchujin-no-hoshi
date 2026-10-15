using System;

namespace Uchujin.Core
{
    /// <summary>
    /// アイテムの束。品質（0〜3の星）、サイズ（魚・宝石など）、所有者、モジュール固有データを持てる。
    /// JsonUtility で直列化できるよう public フィールドのみで構成する。
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        public string itemId;
        public int count;
        /// <summary>品質 0=普通, 1=良, 2=優, 3=最高。</summary>
        public int quality;
        /// <summary>サイズ（魚のcm、宝石の大きさ等）。0なら未使用。束ねた場合は最大値を保持する。</summary>
        public float size;
        /// <summary>所有者のプレイヤーID（マルチの委託販売・共有収納で使用）。空なら持ち主の持ち物。</summary>
        public string ownerId;
        /// <summary>モジュール固有のデータ（JSON文字列など）。空でない場合は同じデータ同士しか束ねない。</summary>
        public string data;

        public ItemStack() { }

        public ItemStack(string itemId, int count, int quality = 0)
        {
            this.itemId = itemId;
            this.count = count;
            this.quality = quality;
        }

        public bool IsEmpty => string.IsNullOrEmpty(itemId) || count <= 0;
        public ItemDefinition Definition => ItemRegistry.GetOrPlaceholder(itemId);

        public bool CanStackWith(ItemStack other)
        {
            if (other == null || IsEmpty || other.IsEmpty) return false;
            return itemId == other.itemId && quality == other.quality && (data ?? "") == (other.data ?? "") && (ownerId ?? "") == (other.ownerId ?? "");
        }

        public ItemStack Clone() => new ItemStack(itemId, count, quality) { size = size, ownerId = ownerId, data = data };

        public ItemStack CloneWithCount(int newCount)
        {
            var c = Clone();
            c.count = newCount;
            return c;
        }

        public static string QualityStars(int quality)
        {
            switch (quality)
            {
                case 1: return "★";
                case 2: return "★★";
                case 3: return "★★★";
                default: return "";
            }
        }

        public string DisplayName
        {
            get
            {
                string n = Definition.Name;
                string q = QualityStars(quality);
                return string.IsNullOrEmpty(q) ? n : n + " " + q;
            }
        }

        public override string ToString() => $"{itemId} x{count} q{quality}";
    }
}
