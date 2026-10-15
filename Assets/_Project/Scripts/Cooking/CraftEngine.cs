using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>選んだ持ち物から実際に取る量（スロット単位）。</summary>
    public sealed class TakePlan
    {
        public int SlotIndex;
        public string ItemId;
        public int Count;
        public int Quality;
    }

    /// <summary>
    /// 調理・加工の計算部分（能力の集め方・計画・品質・材料の自動選択・謎料理の判定）。
    /// 状態は持たない。記録や持ち物の操作は CraftActions が行う。
    /// </summary>
    public static class CraftEngine
    {
        // =================================================================
        // ステーションの状況を作る
        // =================================================================

        public static StationContext BuildContext(CraftStationType type, string instanceId, PlayerState player)
        {
            var game = Game.Instance;
            var ctx = new StationContext { Type = type, InstanceId = instanceId ?? "", Player = player, OwnerId = player != null ? player.PlayerId : null };
            ctx.Profile = CookingTables.Station(type);
            ctx.Position = player != null ? player.Position : Vector3.zero;

            // ---- 自宅の器具（Housing が置いた家具） ----
            if (type == CraftStationType.Kitchen || type == CraftStationType.Workbench || type == CraftStationType.Refinery)
            {
                var f = FindFurniture(instanceId, player, out var ownerList);
                if (f != null)
                {
                    ctx.Furniture = f;
                    ctx.Device = CookingContent.Device(f.ItemId);
                    ctx.OwnerId = f.OwnerId;
                    ctx.Position = f.Position;
                    ctx.Outdoor = f.Location == "garden";
                    if (ctx.Device != null && ctx.Device.Station != ctx.Type)
                    {
                        ctx.Type = ctx.Device.Station;
                        ctx.Profile = CookingTables.Station(ctx.Type);
                    }
                    // 同じ台所（同じ場所・近く）の器具をつなぐ（方式C：複数生物の協力）
                    if (ownerList != null)
                    {
                        foreach (var other in ownerList)
                        {
                            if (other == null || CookingContent.Device(other.ItemId) == null) continue;
                            if (other.Location != f.Location) continue;
                            var d = other.Position - f.Position;
                            d.y = 0;
                            if (other.InstanceId != f.InstanceId && d.magnitude > CookingTuning.KitchenLinkRadius) continue;
                            ctx.LinkedDevices.Add(other);
                        }
                    }
                }
            }
            else if (type == CraftStationType.CommunityKitchen)
            {
                ctx.Outdoor = true;
                var fac = game != null && game.Layout != null ? game.Layout.GetFacilityById(instanceId) : null;
                if (fac != null) ctx.Position = fac.position;
                else if (CookingSystem.Instance != null && CookingSystem.Instance.CommunityKitchenPosition.HasValue) ctx.Position = CookingSystem.Instance.CommunityKitchenPosition.Value;
            }
            else ctx.Outdoor = ctx.Profile.Outdoor;

            // ---- 天候 ----
            var weather = game != null ? game.Service<IWeatherService>() : null;
            if (weather != null)
            {
                try { ctx.Weather = weather.GetAt(ctx.Position); }
                catch { ctx.Weather = weather.Current; }
            }

            CollectSources(ctx);
            CollectTimedTargets(ctx);

            ctx.HasCarry = ctx.HasAbility(CreatureAbility.Carry);
            ctx.HasMimic = ctx.HasAbility(CreatureAbility.Mimic);
            ctx.HasSense = ctx.HasAbility(CreatureAbility.Sense) || ctx.HasAbility(CreatureAbility.Sound);
            ctx.BatchMax = ctx.Profile.BatchMax + (ctx.HasCarry ? CookingTuning.CarryBatchBonus : 0);
            return ctx;
        }

        /// <summary>家具インスタンスIDから置かれた器具を探す（自分の家→星の持ち主の家→参加者の家）。</summary>
        public static PlacedFurnitureView FindFurniture(string instanceId, PlayerState player, out List<PlacedFurnitureView> ownerList)
        {
            ownerList = null;
            if (string.IsNullOrEmpty(instanceId) || Game.Instance == null) return null;
            var housing = Game.Instance.Service<IHousingService>();
            if (housing == null) return null;
            var owners = new List<string>();
            if (player != null) owners.Add(player.PlayerId);
            if (Game.Instance.Planet != null && !string.IsNullOrEmpty(Game.Instance.Planet.hostPlayerId)) owners.Add(Game.Instance.Planet.hostPlayerId);
            foreach (var p in Game.Instance.Players.All) if (!owners.Contains(p.PlayerId)) owners.Add(p.PlayerId);
            foreach (var owner in owners)
            {
                IEnumerable<PlacedFurnitureView> list = null;
                try { list = housing.GetPlayerHomeFurniture(owner); }
                catch (System.Exception e) { Debug.LogException(e); }
                if (list == null) continue;
                var arr = new List<PlacedFurnitureView>(list);
                foreach (var f in arr)
                {
                    if (f != null && f.InstanceId == instanceId)
                    {
                        ownerList = arr;
                        return f;
                    }
                }
            }
            return null;
        }

        private static void CollectSources(StationContext ctx)
        {
            var game = Game.Instance;
            var cs = game != null ? game.Service<ICreatureService>() : null;
            if (cs == null) return;
            var seen = new HashSet<string>();
            bool canUseOthers = ctx.Player == null || ctx.OwnerId == ctx.Player.PlayerId || ctx.Player.Permission >= PermissionLevel.Helper;

            // 方式A：器具に住む生物
            if (canUseOthers)
            {
                foreach (var f in ctx.LinkedDevices)
                {
                    var dev = CookingContent.Device(f.ItemId);
                    if (!string.IsNullOrEmpty(f.ResidentCreatureUid))
                    {
                        var c = SafeGet(cs, f.ResidentCreatureUid);
                        if (c != null && seen.Add(c.Uid)) AddCreature(ctx, cs, c, dev, f.InstanceId, f.ItemId, false, false);
                    }
                    foreach (var c in SafeOwned(cs, f.OwnerId))
                    {
                        if (c.Placement != CreaturePlacement.Device || c.PlacementTargetId != f.InstanceId) continue;
                        if (seen.Add(c.Uid)) AddCreature(ctx, cs, c, dev, f.InstanceId, f.ItemId, false, false);
                    }
                }
            }

            // 方式B：生物そのもの（stationInstanceId に生物UID）
            if (ctx.Type == CraftStationType.CreatureDevice)
            {
                var c = SafeGet(cs, ctx.InstanceId);
                if (c != null && seen.Add(c.Uid)) AddCreature(ctx, cs, c, null, null, null, false, false, true);
            }

            // 修理作業場・精製器で手伝う生物
            if (ctx.Profile.AcceptsWorkshopCreatures && ctx.Player != null)
            {
                var owners = new List<string> { ctx.Player.PlayerId };
                if (game.Planet != null && !string.IsNullOrEmpty(game.Planet.hostPlayerId) && !owners.Contains(game.Planet.hostPlayerId)) owners.Add(game.Planet.hostPlayerId);
                foreach (var owner in owners)
                {
                    foreach (var c in SafeOwned(cs, owner))
                    {
                        if (c.Placement != CreaturePlacement.RocketWorkshop) continue;
                        if (seen.Add(c.Uid)) AddCreature(ctx, cs, c, null, null, null, owner != ctx.Player.PlayerId, true);
                    }
                }
            }

            // 同行している自分の生物（方式B：生物自身が器具）
            if (ctx.Player != null)
            {
                foreach (var c in SafeOwned(cs, ctx.Player.PlayerId))
                {
                    if (c.Placement != CreaturePlacement.Companion) continue;
                    if (seen.Add(c.Uid)) AddCreature(ctx, cs, c, null, null, null, false, false);
                }
                // 共同調理：近くにいる他のプレイヤーの同行生物の能力を借りる【提案】
                foreach (var other in game.Players.All)
                {
                    if (other == null || other.PlayerId == ctx.Player.PlayerId) continue;
                    if ((other.Position - ctx.Player.Position).sqrMagnitude > CookingTuning.CoopRadius * CookingTuning.CoopRadius) continue;
                    foreach (var c in SafeOwned(cs, other.PlayerId))
                    {
                        if (c.Placement != CreaturePlacement.Companion) continue;
                        if (seen.Add(c.Uid)) AddCreature(ctx, cs, c, null, null, null, true, false);
                    }
                }
            }
        }

        private static CreatureView SafeGet(ICreatureService cs, string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            try { return cs.Get(uid); }
            catch (System.Exception e) { Debug.LogException(e); return null; }
        }

        private static IEnumerable<CreatureView> SafeOwned(ICreatureService cs, string owner)
        {
            if (string.IsNullOrEmpty(owner)) return new CreatureView[0];
            try { return cs.GetOwned(owner) ?? (IEnumerable<CreatureView>)new CreatureView[0]; }
            catch (System.Exception e) { Debug.LogException(e); return new CreatureView[0]; }
        }

        private static void AddCreature(StationContext ctx, ICreatureService cs, CreatureView c, DeviceDef dev, string devInstance, string devItem,
            bool borrowed, bool workshop, bool selfDevice = false)
        {
            if (c == null || c.Abilities == null) return;
            string name = !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : SafeSpeciesName(cs, c.SpeciesId);
            foreach (var al in c.Abilities)
            {
                ctx.Sources.Add(new AbilitySource
                {
                    Ability = al.ability,
                    Level = Mathf.Max(1, al.level),
                    CreatureUid = c.Uid,
                    SpeciesId = c.SpeciesId,
                    DisplayName = name,
                    InDevice = selfDevice || (dev != null && dev.Ability.HasValue && dev.Ability.Value == al.ability),
                    DeviceInstanceId = devInstance,
                    DeviceItemId = devItem,
                    OwnerId = c.OwnerId,
                    Borrowed = borrowed,
                    AtWorkshop = workshop
                });
            }
        }

        public static string SafeSpeciesName(ICreatureService cs, string speciesId)
        {
            if (cs == null || string.IsNullOrEmpty(speciesId)) return Loc.T("生き物", "Creature");
            try
            {
                var n = cs.GetSpeciesName(speciesId);
                return string.IsNullOrEmpty(n) ? speciesId : n;
            }
            catch { return speciesId; }
        }

        private static void CollectTimedTargets(StationContext ctx)
        {
            var p = ctx.Profile;
            if (ctx.Type == CraftStationType.CommunityKitchen)
            {
                var t = new TimedTarget { Key = "community:" + ctx.InstanceId, Capacity = p.TimedSlots, NameJa = "共用の干し棚と樽", NameEn = "Shared rack & barrel", Outdoor = true };
                foreach (var s in p.TimedSteps) ctx.TimedTargets[s] = t;
                return;
            }
            if (ctx.Type == CraftStationType.Kitchen || ctx.Type == CraftStationType.Workbench)
            {
                // 開いた器具自身を優先し、同じ台所の器具も使う
                var ordered = new List<PlacedFurnitureView>();
                if (ctx.Furniture != null) ordered.Add(ctx.Furniture);
                foreach (var f in ctx.LinkedDevices) if (!ordered.Contains(f)) ordered.Add(f);
                foreach (var f in ordered)
                {
                    var dev = CookingContent.Device(f.ItemId);
                    if (dev == null || !dev.TimedStep.HasValue || dev.Station != CraftStationType.Kitchen) continue;
                    if (ctx.TimedTargets.ContainsKey(dev.TimedStep.Value)) continue;
                    ctx.TimedTargets[dev.TimedStep.Value] = new TimedTarget
                    {
                        Key = f.InstanceId, Capacity = Mathf.Max(1, dev.TimedSlots), NameJa = dev.NameJa, NameEn = dev.NameEn, Outdoor = f.Location == "garden"
                    };
                }
                return;
            }
            if (ctx.Type == CraftStationType.Refinery || ctx.Type == CraftStationType.RocketWorkshop)
            {
                int cap = ctx.Device != null && ctx.Device.TimedSlots > 0 ? ctx.Device.TimedSlots : p.TimedSlots;
                var key = (ctx.Type == CraftStationType.Refinery ? "refinery:" : "workshop:") + ctx.InstanceId;
                var t = new TimedTarget { Key = key, Capacity = Mathf.Max(1, cap), NameJa = p.NameJa, NameEn = p.NameEn, Outdoor = ctx.Outdoor };
                foreach (var s in p.TimedSteps) ctx.TimedTargets[s] = t;
                ctx.OwnTimed = t;
            }
        }

        // =================================================================
        // 計画（どの工程を誰が・どうやって行うか）
        // =================================================================

        public static CraftPlan Plan(StationContext ctx, RecipeDef recipe)
        {
            var plan = PlanSteps(ctx, recipe.Steps);
            plan.Recipe = recipe;
            plan.StationAllowed = recipe.AllowsStation(ctx.Type);
            plan.WeatherOk = RecipeBook.WeatherOk(recipe, ctx.Weather);
            return plan;
        }

        public static CraftPlan PlanSteps(StationContext ctx, IList<CraftStep> steps)
        {
            var plan = new CraftPlan();
            foreach (var s in steps)
            {
                var sp = PlanStep(ctx, s);
                plan.Steps.Add(sp);
                switch (sp.Mode)
                {
                    case StepMode.Missing:
                        plan.Missing.Add(s);
                        break;
                    case StepMode.Timed:
                        plan.TimedHours += sp.Hours;
                        if (plan.TimedKey == null && ctx.TimedTargets.TryGetValue(s, out var t))
                        {
                            plan.TimedKey = t.Key; plan.TimedCapacity = t.Capacity; plan.TimedNameJa = t.NameJa; plan.TimedNameEn = t.NameEn;
                        }
                        break;
                    case StepMode.Slow:
                        plan.UsesSlow = true;
                        plan.TimedHours += sp.Hours;
                        break;
                }
            }
            if (plan.UsesSlow && plan.TimedKey == null && ctx.OwnTimed != null)
            {
                plan.TimedKey = ctx.OwnTimed.Key; plan.TimedCapacity = ctx.OwnTimed.Capacity; plan.TimedNameJa = ctx.OwnTimed.NameJa; plan.TimedNameEn = ctx.OwnTimed.NameEn;
            }
            if (plan.IsTimed && plan.TimedKey == null)
            {
                // 置き場所が無い時間の工程は「できない」扱い
                foreach (var sp in plan.Steps) if ((sp.Mode == StepMode.Timed || sp.Mode == StepMode.Slow) && !plan.Missing.Contains(sp.Step)) plan.Missing.Add(sp.Step);
            }
            return plan;
        }

        public static StepPlan PlanStep(StationContext ctx, CraftStep step)
        {
            var info = CookingTables.Step(step);
            var sp = new StepPlan { Step = step, Mode = StepMode.Missing };
            if (info.Timed)
            {
                if (ctx.TimedTargets.ContainsKey(step))
                {
                    sp.Source = info.Abilities.Length > 0 ? ctx.BestSource(info.Abilities) : null;
                    sp.Mode = StepMode.Timed;
                    sp.Hours = sp.Source != null ? info.FastHours : info.BaseHours;
                }
                return sp;
            }
            var src = info.Abilities.Length > 0 ? ctx.BestSource(info.Abilities) : null;
            if (src != null)
            {
                sp.Mode = StepMode.Creature;
                sp.Source = src;
                return sp;
            }
            if (ctx.HasBuiltIn(step))
            {
                sp.Mode = StepMode.BuiltIn;
                sp.Helper = HelperFor(ctx, step);
                return sp;
            }
            // 天候の力（§8.2【提案】：屋外なら光雨の光・逆雨の水を借りられる）
            if (ctx.Outdoor)
            {
                if (step == CraftStep.Glow && ctx.Weather.Has(AlienPhenomenon.LightRain)) { sp.Mode = StepMode.Weather; return sp; }
                if (step == CraftStep.Water && (ctx.Weather.Has(AlienPhenomenon.ReverseRain) || ctx.Weather.precipitation >= Precipitation.Rain)) { sp.Mode = StepMode.Weather; return sp; }
            }
            if (ctx.IsSlowCapable(step))
            {
                sp.Mode = StepMode.Slow;
                sp.Hours = CookingTuning.SlowHoursPerStep;
            }
            return sp;
        }

        /// <summary>手作業の工程を上手にしてくれる生物（研磨粉・感知・箱詰め）。</summary>
        private static AbilitySource HelperFor(StationContext ctx, CraftStep step)
        {
            switch (step)
            {
                case CraftStep.Polish: return ctx.BestSource(new[] { CreatureAbility.MineralDust, CreatureAbility.Sense });
                case CraftStep.Pack: return ctx.BestSource(new[] { CreatureAbility.Pack });
                case CraftStep.Shape: return ctx.BestSource(new[] { CreatureAbility.Stick });
                case CraftStep.Heat: return ctx.BestSource(new[] { CreatureAbility.Heat });
                case CraftStep.Cool: return ctx.BestSource(new[] { CreatureAbility.Cool, CreatureAbility.Water });
            }
            return null;
        }

        // =================================================================
        // 材料の自動選択（レシピ調理・まとめ調理）
        // =================================================================

        /// <summary>
        /// レシピの材料を持ち物から選ぶ。足りなければ null を返し、shortage に材料指定ごとの所持数を入れる。
        /// 具体的な指定（固有ID）から先に確保する。
        /// </summary>
        public static List<TakePlan> AutoSelect(PlayerState player, RecipeDef recipe, int batch, bool preferBest, int[] haveCounts)
        {
            if (player == null) return null;
            var inv = player.Inventory;
            inv.EnsureSlots();
            var remaining = new int[inv.slots.Count];
            for (int i = 0; i < inv.slots.Count; i++) remaining[i] = inv.slots[i] == null || inv.slots[i].IsEmpty ? 0 : inv.slots[i].count;
            var order = new List<int>();
            for (int i = 0; i < recipe.Ingredients.Count; i++) order.Add(i);
            order.Sort((a, b) => recipe.Ingredients[b].Specificity.CompareTo(recipe.Ingredients[a].Specificity));
            var result = new List<TakePlan>();
            bool ok = true;
            foreach (int si in order)
            {
                var spec = recipe.Ingredients[si];
                int need = spec.Count * Mathf.Max(1, batch);
                var cands = new List<int>();
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var s = inv.slots[i];
                    if (s == null || s.IsEmpty || remaining[i] <= 0) continue;
                    if (spec.Matches(s.Definition)) cands.Add(i);
                }
                cands.Sort((a, b) =>
                {
                    int qa = inv.slots[a].quality, qb = inv.slots[b].quality;
                    return preferBest ? qb.CompareTo(qa) : qa.CompareTo(qb);
                });
                int have = 0;
                foreach (var i in cands) have += remaining[i];
                if (haveCounts != null && si < haveCounts.Length) haveCounts[si] = have;
                foreach (var i in cands)
                {
                    if (need <= 0) break;
                    int take = Mathf.Min(need, remaining[i]);
                    remaining[i] -= take;
                    need -= take;
                    result.Add(new TakePlan { SlotIndex = i, ItemId = inv.slots[i].itemId, Count = take, Quality = inv.slots[i].quality });
                }
                if (need > 0) ok = false;
            }
            return ok ? result : null;
        }

        /// <summary>作れる最大のまとめ数。</summary>
        public static int MaxBatch(PlayerState player, RecipeDef recipe, int cap)
        {
            int best = 0;
            for (int b = 1; b <= cap; b++)
            {
                if (AutoSelect(player, recipe, b, false, null) == null) break;
                best = b;
            }
            return best;
        }

        // =================================================================
        // 品質（§7.1：食材の品質・調理の仕上がり・習熟。好みとは別に扱う）
        // =================================================================

        public static int ComputeQuality(StationContext ctx, CraftPlan plan, float ingredientAvgQuality, float score, CookingProfileData profile)
        {
            float v = ingredientAvgQuality * 0.6f + Mathf.Clamp01(score) * CookingTuning.MinigameWeight + ctx.Profile.QualityOffset;
            float creature = 0f;
            int n = 0;
            bool device = false;
            var seen = new HashSet<string>();
            foreach (var src in plan.UsedSources)
            {
                if (src == null || src.CreatureUid == null) continue;
                if (!seen.Add(src.CreatureUid + ":" + src.Ability)) continue;
                creature += CookingTuning.CreatureLevelBonus * (src.Level - 1) + CookingTuning.MasteryBonus * MasteryLevel(profile, src.CreatureUid, src.Ability);
                n++;
                if (src.InDevice) device = true;
            }
            if (n > 0) v += creature / n;
            if (device) v += CookingTuning.DeviceBonus;
            if (ctx.HasSense) v += 0.1f;
            var r = plan.Recipe;
            if (r != null)
            {
                if (r.BonusPhenomenon != AlienPhenomenon.None && ctx.Weather.Has(r.BonusPhenomenon)) v += CookingTuning.WeatherBonus;
                if (r.SunnyBonus && ctx.Weather.sky <= SkyState.Sunny && !ctx.Weather.IsRaining) v += CookingTuning.WeatherBonus;
                if (r.RainBonus && (ctx.Weather.IsRaining || ctx.Weather.IsFoggy)) v += CookingTuning.WeatherBonus;
            }
            return Mathf.Clamp(Mathf.RoundToInt(v), 0, 3);
        }

        public static int MasteryLevel(CookingProfileData profile, string uid, CreatureAbility ability)
        {
            if (profile == null || uid == null) return 0;
            foreach (var m in profile.mastery)
            {
                if (m.uid == uid && m.ability == (int)ability)
                    return Mathf.Min(CookingTuning.MasteryMaxLevel, m.uses / CookingTuning.MasteryUsesPerLevel);
            }
            return 0;
        }

        // =================================================================
        // 謎料理（§5.5：意図と違う結果も発見にする）
        // =================================================================

        public static string MysteryFor(IList<ChosenIngredient> chosen, IList<CraftStep> steps)
        {
            bool edible = false, mineral = false;
            foreach (var c in chosen)
            {
                var d = c.Def;
                if (Ing.IsFoodish(d)) edible = true;
                if (Ing.IsOre(d) || d.HasTag(ItemTags.Metal) || Ing.IsCrystal(d)) mineral = true;
            }
            var has = new HashSet<CraftStep>(steps);
            if (!edible) return has.Contains(CraftStep.Heat) && mineral ? CookingIds.MysteryMetal : CookingIds.MysteryLump;
            if (has.Contains(CraftStep.Glow)) return CookingIds.MysteryGlowing;
            if (has.Contains(CraftStep.Ferment)) return CookingIds.MysteryPickle;
            if (has.Contains(CraftStep.Dry) || has.Contains(CraftStep.Smoke)) return CookingIds.MysteryDried;
            if (has.Contains(CraftStep.Water) && has.Contains(CraftStep.Heat)) return CookingIds.MysteryStew;
            if (has.Contains(CraftStep.Cool)) return CookingIds.MysteryChilled;
            if (has.Contains(CraftStep.Bubble)) return CookingIds.MysteryFoam;
            if (has.Contains(CraftStep.Press)) return CookingIds.MysteryDrink;
            if (has.Contains(CraftStep.Grind)) return CookingIds.MysteryPaste;
            if (has.Contains(CraftStep.Heat)) return CookingIds.MysteryGrill;
            return CookingIds.MysteryMix;
        }

        // =================================================================
        // 表示用
        // =================================================================

        public static string StepsText(IList<CraftStep> steps)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                if (i > 0) sb.Append(" → ");
                sb.Append(CookingTables.Step(steps[i]).Name);
            }
            return sb.ToString();
        }

        public static string StepsText(IList<int> steps)
        {
            var list = new List<CraftStep>();
            if (steps != null) foreach (var s in steps) list.Add((CraftStep)s);
            return StepsText(list);
        }

        public static string ItemsText(IList<string> ids)
        {
            var sb = new System.Text.StringBuilder();
            if (ids == null) return "";
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(Loc.T("、", ", "));
                sb.Append(ItemRegistry.NameOf(ids[i]));
            }
            return sb.ToString();
        }

        public static string WeatherNote(WeatherState w) => w.Summary();

        /// <summary>その能力を持つ、プレイヤーが見つけたことのある生物の呼び名（足りない能力の案内用）。</summary>
        public static List<string> KnownSpeciesWith(CreatureAbility ability, string playerId, int max = 3)
        {
            var result = new List<string>();
            var cs = Game.Instance != null ? Game.Instance.Service<ICreatureService>() : null;
            if (cs == null) return result;
            try
            {
                foreach (var sid in cs.AllSpeciesIds)
                {
                    var abil = cs.GetSpeciesAbilities(sid);
                    if (abil == null) continue;
                    bool has = false;
                    foreach (var a in abil) if (a == ability) has = true;
                    if (!has || !cs.IsSpeciesDiscovered(playerId, sid)) continue;
                    result.Add(SafeSpeciesName(cs, sid));
                    if (result.Count >= max) break;
                }
            }
            catch (System.Exception e) { Debug.LogException(e); }
            return result;
        }
    }
}
