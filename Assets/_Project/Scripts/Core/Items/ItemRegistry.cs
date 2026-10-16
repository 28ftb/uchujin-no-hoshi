using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>全アイテム定義の登録先。各システムの RegisterContent() から登録する。</summary>
    public static class ItemRegistry
    {
        private static readonly Dictionary<string, ItemDefinition> items = new Dictionary<string, ItemDefinition>();
        private static readonly List<ItemDefinition> ordered = new List<ItemDefinition>();
        /// <summary>古いID → 今のID（作り直したアイテムの古いセーブ用。例：旧案の魚41種 → 魚50種）。</summary>
        private static readonly Dictionary<string, string> aliases = new Dictionary<string, string>();

        public static IReadOnlyList<ItemDefinition> All => ordered;

        /// <summary>古いIDを今のIDとして読めるようにする（Get・GetOrPlaceholder が今の定義を返す）。</summary>
        public static void RegisterAlias(string oldId, string newId)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId) || oldId == newId) return;
            aliases[oldId] = newId;
        }

        /// <summary>古いIDなら今のIDを返す（そうでなければそのまま）。</summary>
        public static string Resolve(string id)
        {
            if (id != null && !items.ContainsKey(id) && aliases.TryGetValue(id, out var to)) return to;
            return id;
        }

        public static ItemDefinition Register(ItemDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                Debug.LogError("ItemRegistry: Id の無い定義は登録できません");
                return def;
            }
            if (items.ContainsKey(def.Id))
            {
                Debug.LogWarning($"ItemRegistry: {def.Id} は登録済みです（上書きします）");
                ordered.Remove(items[def.Id]);
            }
            if (def.Tags.Count == 0 || !def.Tags.Contains(ItemTags.NotSellable)) def.Tags.Add(ItemTags.Sellable);
            items[def.Id] = def;
            ordered.Add(def);
            return def;
        }

        /// <summary>簡易登録ヘルパー。</summary>
        public static ItemDefinition Register(string id, string nameJa, string nameEn, ItemCategory category, int basePrice,
            Color color, string genericConcept = null, params string[] tags)
        {
            var def = new ItemDefinition
            {
                Id = id,
                NameJa = nameJa,
                NameEn = nameEn,
                Category = category,
                BasePrice = basePrice,
                Color = color,
                GenericConceptId = genericConcept
            };
            def.WithTags(tags);
            return Register(def);
        }

        public static bool Exists(string id) => id != null && (items.ContainsKey(id) || aliases.ContainsKey(id));

        public static ItemDefinition Get(string id)
        {
            if (id != null && items.TryGetValue(id, out var def)) return def;
            if (id != null && aliases.TryGetValue(id, out var to) && items.TryGetValue(to, out def)) return def;
            return null;
        }

        /// <summary>未登録IDでも落ちないように仮の定義を返す（他モジュール未実装時の保険）。</summary>
        public static ItemDefinition GetOrPlaceholder(string id)
        {
            var def = Get(id);
            if (def != null) return def;
            return new ItemDefinition { Id = id ?? "unknown", NameJa = id ?? "？", NameEn = id ?? "?", Category = ItemCategory.Misc, BasePrice = 1, Color = Color.magenta };
        }

        public static string NameOf(string id) => GetOrPlaceholder(id).Name;

        public static IEnumerable<ItemDefinition> ByCategory(ItemCategory category)
        {
            foreach (var d in ordered) if (d.Category == category) yield return d;
        }

        public static IEnumerable<ItemDefinition> WithTag(string tag)
        {
            foreach (var d in ordered) if (d.Tags.Contains(tag)) yield return d;
        }

        public static void Clear()
        {
            items.Clear();
            ordered.Clear();
            aliases.Clear();
        }
    }
}
