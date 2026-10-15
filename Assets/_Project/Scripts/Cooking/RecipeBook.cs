using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>実験（自由調理）で選んだ材料1枠。</summary>
    public sealed class ChosenIngredient
    {
        public int SlotIndex;
        public string ItemId;
        public int Count;
        public ItemDefinition Def => ItemRegistry.GetOrPlaceholder(ItemId);
    }

    /// <summary>照合の結果。</summary>
    public sealed class RecipeMatch
    {
        public RecipeDef Recipe;
        /// <summary>chosen[i] がどの材料指定に割り当てられたか。</summary>
        public int[] Assignment;
        /// <summary>このステーションでは作れないが、材料と手順は合っている。</summary>
        public bool WrongStation;
        /// <summary>材料と手順は合っているが、天候が合わない（天候限定のもの）。</summary>
        public bool WrongWeather;
    }

    /// <summary>レシピの登録先と照合（§5.1：レシピ調理＋自由調理）。</summary>
    public static class RecipeBook
    {
        private static readonly List<RecipeDef> all = new List<RecipeDef>();
        private static readonly Dictionary<string, RecipeDef> byId = new Dictionary<string, RecipeDef>();

        public static IReadOnlyList<RecipeDef> All => all;

        public static void Add(RecipeDef r)
        {
            if (r == null || string.IsNullOrEmpty(r.Id)) return;
            if (byId.ContainsKey(r.Id))
            {
                Debug.LogWarning("RecipeBook: 重複したレシピID " + r.Id);
                all.Remove(byId[r.Id]);
            }
            byId[r.Id] = r;
            all.Add(r);
        }

        public static RecipeDef Get(string id)
        {
            if (id != null && byId.TryGetValue(id, out var r)) return r;
            return null;
        }

        public static IEnumerable<RecipeDef> Producing(string itemId)
        {
            foreach (var r in all)
            {
                if (r.OutputItemId == itemId || r.AlsoProduces.Contains(itemId)) yield return r;
            }
        }

        /// <summary>同じ材料（固有指定のみ）・同じ手順で天候条件も同じレシピが無いか確認する（開発用）。</summary>
        public static void Validate()
        {
            var seen = new Dictionary<string, string>();
            foreach (var r in all)
            {
                var sb = new StringBuilder();
                var parts = new List<string>();
                foreach (var spec in r.Ingredients)
                {
                    if (!string.IsNullOrEmpty(spec.ItemId)) parts.Add(spec.ItemId + "x" + spec.Count);
                    else parts.Add((spec.LabelEn ?? "?") + "x" + spec.Count);
                }
                parts.Sort(System.StringComparer.Ordinal);
                foreach (var p in parts) sb.Append(p).Append('+');
                sb.Append('|');
                foreach (var s in r.Steps) sb.Append((int)s).Append(',');
                sb.Append('|').Append((int)r.RequiredPhenomenon).Append(r.RequiresSunny ? "S" : "");
                var key = sb.ToString();
                if (seen.TryGetValue(key, out var other))
                {
                    // 同じ材料・手順でも、作れる場所が重ならなければ問題ない
                    var o = Get(other);
                    bool overlap = false;
                    foreach (var st in r.Stations) if (o != null && o.AllowsStation(st)) overlap = true;
                    if (overlap) Debug.LogWarning($"RecipeBook: {r.Id} と {other} が同じ材料・手順です（優先度で決まります）");
                }
                else seen[key] = r.Id;
            }
        }

        /// <summary>
        /// 選んだ材料と手順に合うレシピを探す（§5.1 自由調理）。
        /// 材料の種類は過不足なく、手順は順番まで一致する必要がある。数は「必要数以上」なら成立（余りは使わない）。
        /// </summary>
        public static RecipeMatch Match(CraftStationType station, IList<ChosenIngredient> chosen, IList<CraftStep> steps, WeatherState weather)
        {
            RecipeMatch best = null;
            RecipeMatch fallback = null;
            foreach (var r in all)
            {
                if (!r.Experimentable) continue;
                if (!StepsEqual(r.Steps, steps)) continue;
                var assign = Assign(r, chosen);
                if (assign == null) continue;
                bool weatherOk = WeatherOk(r, weather);
                bool stationOk = r.AllowsStation(station);
                if (!weatherOk)
                {
                    // 天候限定レシピは、天候が合わなければ黙って候補から外す（通常版が成立する）
                    continue;
                }
                var m = new RecipeMatch { Recipe = r, Assignment = assign, WrongStation = !stationOk };
                if (!stationOk)
                {
                    if (fallback == null || Better(r, fallback.Recipe)) fallback = m;
                    continue;
                }
                if (best == null || Better(r, best.Recipe)) best = m;
            }
            return best ?? fallback;
        }

        private static bool Better(RecipeDef a, RecipeDef b)
        {
            if (a.Priority != b.Priority) return a.Priority > b.Priority;
            return a.TotalIngredientCount > b.TotalIngredientCount;
        }

        public static bool WeatherOk(RecipeDef r, WeatherState w)
        {
            if (r.RequiredPhenomenon != AlienPhenomenon.None && (w.phenomena & r.RequiredPhenomenon) == 0) return false;
            if (r.RequiresSunny && !(w.sky <= SkyState.Sunny && !w.IsRaining)) return false;
            return true;
        }

        public static bool StepsEqual(IList<CraftStep> a, IList<CraftStep> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>選んだ材料を材料指定へ割り当てる（小さな総当たり。枠は最大5）。失敗なら null。</summary>
        public static int[] Assign(RecipeDef r, IList<ChosenIngredient> chosen)
        {
            if (chosen == null || chosen.Count == 0 || r.Ingredients.Count == 0) return null;
            if (chosen.Count < r.Ingredients.Count) return null;
            var options = new List<int>[chosen.Count];
            for (int i = 0; i < chosen.Count; i++)
            {
                var def = chosen[i].Def;
                var list = new List<int>();
                for (int s = 0; s < r.Ingredients.Count; s++) if (r.Ingredients[s].Matches(def)) list.Add(s);
                if (list.Count == 0) return null; // 余計な材料が入っている
                list.Sort((x, y) => r.Ingredients[y].Specificity.CompareTo(r.Ingredients[x].Specificity));
                options[i] = list;
            }
            var assign = new int[chosen.Count];
            return Search(r, chosen, options, assign, 0) ? assign : null;
        }

        private static bool Search(RecipeDef r, IList<ChosenIngredient> chosen, List<int>[] options, int[] assign, int index)
        {
            if (index >= chosen.Count)
            {
                var sums = new int[r.Ingredients.Count];
                for (int i = 0; i < chosen.Count; i++) sums[assign[i]] += chosen[i].Count;
                for (int s = 0; s < sums.Length; s++) if (sums[s] < r.Ingredients[s].Count) return false;
                return true;
            }
            foreach (var opt in options[index])
            {
                assign[index] = opt;
                if (Search(r, chosen, options, assign, index + 1)) return true;
            }
            return false;
        }
    }
}
