using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 調理・加工の実行（ローカル操作）。材料は呼び出したプレイヤー自身の持ち物から取る。
    /// 器具に仕込む時間の加工・職人への依頼・使用料は WorldCommand でホストへ送る。
    /// </summary>
    public static class CraftActions
    {
        /// <summary>
        /// 知っているレシピを作る。初めてなら短い操作（§6.1）、2回目以降はまとめて作る（§6.2）。
        /// manual=true なら2回目以降でも手で仕上げる（品質にこだわる）。
        /// </summary>
        public static void CraftRecipe(StationContext ctx, RecipeDef recipe, int batch, bool preferBest, bool manual, Action onDone = null)
        {
            var sys = CookingSystem.Instance;
            var player = ctx.Player;
            if (sys == null || player == null || recipe == null) return;
            var plan = CraftEngine.Plan(ctx, recipe);
            if (!plan.StationAllowed) { UIManager.Toast(Loc.T("ここでは作れないようだ", "Can't make that here")); return; }
            if (!plan.WeatherOk) { UIManager.Toast(Loc.T("今の天気では作れないようだ", "The weather isn't right for this")); return; }
            if (plan.Missing.Count > 0) { UIManager.Toast(Loc.T("必要な力が足りない", "Missing an ability")); return; }
            bool first = !sys.HasMade(player.PlayerId, recipe.Id);
            bool needGame = (first || manual) && HasMinigame(plan);
            if (needGame) batch = 1;
            batch = Mathf.Clamp(batch, 1, Mathf.Max(1, ctx.BatchMax));
            var takes = CraftEngine.AutoSelect(player, recipe, batch, preferBest, null);
            if (takes == null) { UIManager.Toast(Loc.T("材料が足りない", "Not enough ingredients")); return; }
            if (!CheckFee(ctx)) return;
            if (plan.IsTimed && !CheckTimedSpace(plan)) return;

            if (needGame)
            {
                UIManager.Open(new CookingMinigameWindow(ctx, plan, recipe, score =>
                {
                    // ミニゲーム中に持ち物が変わっていたら選び直す
                    var again = CraftEngine.AutoSelect(player, recipe, 1, preferBest, null);
                    if (again == null) { UIManager.Toast(Loc.T("材料が足りない", "Not enough ingredients")); return; }
                    Execute(ctx, plan, recipe, again, 1, score, null, recipe.Steps);
                    onDone?.Invoke();
                }));
                return;
            }
            float auto = CookingTuning.AutoCookScore;
            Execute(ctx, plan, recipe, takes, batch, auto, null, recipe.Steps);
            onDone?.Invoke();
        }

        /// <summary>実験（自由調理）：選んだ材料と手順で試す（§5.1・§5.5）。</summary>
        public static void Experiment(StationContext ctx, List<ChosenIngredient> chosen, List<CraftStep> steps, Action onDone = null)
        {
            var sys = CookingSystem.Instance;
            var player = ctx.Player;
            if (sys == null || player == null) return;
            if (chosen == null || chosen.Count == 0) { UIManager.Toast(Loc.T("材料を選んでください", "Choose some ingredients")); return; }
            if (steps == null || steps.Count == 0)
            {
                UIManager.Toast(Loc.T("手順を選んでください（何もしないと何も起きない）", "Choose some steps (nothing happens otherwise)"));
                return;
            }
            // 持ち物の確認
            foreach (var c in chosen)
            {
                var s = player.Inventory.GetSlot(c.SlotIndex);
                if (s == null || s.IsEmpty || s.itemId != c.ItemId || s.count < c.Count)
                {
                    UIManager.Toast(Loc.T("持ち物が変わったようだ。選び直してください", "Your items changed; please choose again"));
                    return;
                }
            }
            var plan = CraftEngine.PlanSteps(ctx, steps);
            if (plan.Missing.Count > 0) { UIManager.Toast(Loc.T("ここではできない手順が入っている", "Some steps can't be done here")); return; }

            var match = RecipeBook.Match(ctx.Type, chosen, steps, ctx.Weather);
            if (match != null && match.WrongStation)
            {
                // 材料と手順は合っているが、ここでは仕上がらない（別の場所なら？）。材料は使わない。
                UIManager.Message(Loc.T("うまくいかない…", "Hmm..."),
                    Loc.T("材料も手順も良さそうなのに、ここでは仕上がらないようだ。別の場所や器具なら作れるかもしれない。",
                          "The ingredients and steps feel right, but it won't come together here. Maybe somewhere else?"));
                return;
            }
            if (!CheckFee(ctx)) return;
            if (plan.IsTimed && !CheckTimedSpace(plan)) return;

            RecipeDef recipe = match != null ? match.Recipe : null;
            if (recipe != null)
            {
                plan = CraftEngine.Plan(ctx, recipe);
            }
            var takes = new List<TakePlan>();
            if (recipe != null)
            {
                // 材料指定ごとに必要数だけ使う（余りは使わない）
                var need = new int[recipe.Ingredients.Count];
                for (int i = 0; i < need.Length; i++) need[i] = recipe.Ingredients[i].Count;
                for (int i = 0; i < chosen.Count; i++)
                {
                    int spec = match.Assignment[i];
                    int take = Mathf.Min(need[spec], chosen[i].Count);
                    if (take <= 0) continue;
                    need[spec] -= take;
                    var slot = player.Inventory.GetSlot(chosen[i].SlotIndex);
                    takes.Add(new TakePlan { SlotIndex = chosen[i].SlotIndex, ItemId = chosen[i].ItemId, Count = take, Quality = slot != null ? slot.quality : 0 });
                }
            }
            else
            {
                foreach (var c in chosen)
                {
                    var slot = player.Inventory.GetSlot(c.SlotIndex);
                    takes.Add(new TakePlan { SlotIndex = c.SlotIndex, ItemId = c.ItemId, Count = c.Count, Quality = slot != null ? slot.quality : 0 });
                }
            }
            string mystery = recipe == null ? CraftEngine.MysteryFor(chosen, steps) : null;
            var stepCopy = new List<CraftStep>(steps);
            if (HasMinigame(plan))
            {
                UIManager.Open(new CookingMinigameWindow(ctx, plan, recipe, score =>
                {
                    Execute(ctx, plan, recipe, takes, 1, score, mystery, stepCopy);
                    onDone?.Invoke();
                }));
            }
            else
            {
                Execute(ctx, plan, recipe, takes, 1, 0.5f, mystery, stepCopy);
                onDone?.Invoke();
            }
        }

        public static bool HasMinigame(CraftPlan plan)
        {
            foreach (var s in plan.Steps)
            {
                if (s.Mode == StepMode.Timed || s.Mode == StepMode.Slow || s.Mode == StepMode.Missing) continue;
                if (CookingTables.Step(s.Step).Minigame != MinigameKind.None) return true;
            }
            return false;
        }

        private static bool CheckFee(StationContext ctx)
        {
            int fee = ctx.Profile.UseFee;
            if (fee <= 0) return true;
            if (ctx.Player.Wallet.CanAfford(fee)) return true;
            UIManager.Toast(Loc.Format("炉の使用料 {0} が足りない", "You need {0} for the forge fee", UIStyles.Money(fee)));
            return false;
        }

        private static bool CheckTimedSpace(CraftPlan plan)
        {
            var sys = CookingSystem.Instance;
            if (plan.TimedKey == null) { UIManager.Toast(Loc.T("仕込む場所がない", "Nowhere to leave it")); return false; }
            if (sys.CountSlots(plan.TimedKey) >= Mathf.Max(1, plan.TimedCapacity))
            {
                UIManager.Toast(Loc.T("仕込む場所がいっぱいだ。できあがった物を取り出そう", "It's full. Collect finished items first"));
                return false;
            }
            return true;
        }

        /// <summary>材料を使って完成させる（または器具に仕込む）。</summary>
        private static void Execute(StationContext ctx, CraftPlan plan, RecipeDef recipe, List<TakePlan> takes, int batch, float score, string mysteryOutput, List<CraftStep> steps)
        {
            var sys = CookingSystem.Instance;
            var player = ctx.Player;
            var inv = player.Inventory;
            // 取る前に全部確認
            foreach (var t in takes)
            {
                var s = inv.GetSlot(t.SlotIndex);
                if (s == null || s.IsEmpty || s.itemId != t.ItemId || s.count < t.Count)
                {
                    UIManager.Toast(Loc.T("持ち物が変わったようだ", "Your items changed"));
                    return;
                }
            }
            if (!CheckFee(ctx)) return;
            var used = new List<string>();
            float qSum = 0f;
            int qN = 0;
            var refunds = new List<ItemStack>();
            foreach (var t in takes)
            {
                var taken = inv.TakeFromSlot(t.SlotIndex, t.Count, "cooking");
                if (taken == null) continue;
                refunds.Add(taken);
                if (!used.Contains(taken.itemId)) used.Add(taken.itemId);
                qSum += taken.quality * taken.count;
                qN += taken.count;
            }
            if (ctx.Profile.UseFee > 0)
                WorldAuthority.Submit(new CookPayFeeCommand { job = (int)ctx.Profile.CommissionJob, amount = ctx.Profile.UseFee, reason = "cooking.use_fee" });

            float avgQ = qN > 0 ? qSum / qN : 0f;
            var profile = sys.Profile(player.PlayerId);
            int quality = CraftEngine.ComputeQuality(ctx, plan, avgQ, score, profile);
            string output = recipe != null ? recipe.ResolveOutput(used) : mysteryOutput;
            if (recipe == null) quality = Mathf.Max(0, quality - 1);
            int count = recipe != null ? recipe.OutputCount * Mathf.Max(1, batch) : 1;
            var sources = new List<AbilitySource>(plan.UsedSources);

            var tr = new TryRecord
            {
                day = Game.Instance.Time != null ? Game.Instance.Time.Day : 0,
                minute = Game.Instance.Time != null ? Game.Instance.Time.MinuteOfDay : 0,
                station = (int)ctx.Type,
                resultItemId = output,
                recipeId = recipe != null ? recipe.Id : null,
                quality = quality,
                mystery = recipe == null,
                weatherNote = ctx.Weather.Summary()
            };
            tr.ingredients.AddRange(used);
            foreach (var s in steps) tr.steps.Add((int)s);
            sys.AddTry(player.PlayerId, tr);

            CookingFx.PlayAt(ctx, plan);

            if (plan.IsTimed)
            {
                var slot = new TimedSlotData
                {
                    slotId = Ids.New(),
                    stationKey = plan.TimedKey,
                    stationNameJa = plan.TimedNameJa,
                    stationNameEn = plan.TimedNameEn,
                    stationType = (int)ctx.Type,
                    recipeId = recipe != null ? recipe.Id : null,
                    outputItemId = output,
                    count = count,
                    quality = quality,
                    ownerId = player.PlayerId,
                    ownerName = player.DisplayName,
                    hoursRequired = plan.TimedHours,
                    outdoor = ctx.Outdoor
                };
                slot.ingredients.AddRange(used);
                foreach (var s in steps)
                {
                    slot.steps.Add((int)s);
                    if (s == CraftStep.Dry) slot.hasDry = true;
                    if (s == CraftStep.Ferment) slot.hasFerment = true;
                    if (s == CraftStep.Smoke) slot.hasSmoke = true;
                }
                foreach (var src in sources) if (src != null && src.SpeciesId != null && !slot.creatureSpecies.Contains(src.SpeciesId)) slot.creatureSpecies.Add(src.SpeciesId);
                int capacity = plan.TimedCapacity;
                WorldAuthority.Submit(new CookStartTimedCommand { slot = slot, capacity = capacity }, r =>
                {
                    if (r != null && r.Success)
                    {
                        UIManager.Toast(Loc.Format("{0}に仕込んだ（あと約{1}時間）", "Left it at {0} (about {1}h)", Loc.T(plan.TimedNameJa, plan.TimedNameEn), Mathf.CeilToInt(plan.TimedHours)));
                        // 習熟は仕込んだ時点で付ける
                        foreach (var src in sources)
                            if (src != null && src.CreatureUid != null && !src.Borrowed) sys.AddMastery(player.PlayerId, src.CreatureUid, src.Ability);
                    }
                    else
                    {
                        foreach (var st in refunds) player.GiveOrDrop(st, "cooking.refund");
                    }
                });
                return;
            }

            player.GiveOrDrop(new ItemStack(output, count, quality), "cooking");
            sys.OnCrafted(player, recipe, output, count, quality, ctx.Type, used, steps, sources, ctx.Weather, false);
            ShowResult(ctx, recipe, output, count, quality, sources, score);
        }

        private static void ShowResult(StationContext ctx, RecipeDef recipe, string output, int count, int quality, List<AbilitySource> sources, float score)
        {
            var def = ItemRegistry.GetOrPlaceholder(output);
            string stars = ItemStack.QualityStars(quality);
            string helper = "";
            foreach (var s in sources)
            {
                if (s == null || s.DisplayName == null) continue;
                helper = Loc.Format("{0}は満足そうだ。", "{0} looks satisfied. ", s.DisplayName);
                break;
            }
            string msg;
            if (recipe == null)
                msg = Loc.Format("思っていたのと違う物ができた…：{0}\nでも、これはこれで誰かが気に入るかもしれない。", "Something unexpected came out: {0}\nMaybe someone will like it anyway.", def.Name);
            else if (recipe.Kind == RecipeKind.Accidental)
                msg = Loc.Format("やりすぎた…？ {0} ができた。\n固くて香ばしいのが好きな人もいるかも。", "Overdid it? Made {0}.\nSome might like it this way.", def.Name);
            else
                msg = Loc.Format("{0} ×{1} {2}", "{0} x{1} {2}", def.Name, count, stars);
            if (!string.IsNullOrEmpty(helper)) msg += "\n" + helper;
            UIManager.Toast(msg, 4.5f);
        }

        /// <summary>職人に依頼する（ロケット仕様 §9.3：修理屋・鍛冶屋・家具職人）。品質は「良」で一定、手数料がかかる。</summary>
        public static void Commission(StationContext ctx, RecipeDef recipe, int batch)
        {
            var player = ctx.Player;
            if (player == null || recipe == null || recipe.CommissionFee <= 0) return;
            var job = ctx.Profile.CommissionJob;
            if (job == ResidentJob.None) return;
            batch = Mathf.Clamp(batch, 1, Mathf.Max(1, ctx.BatchMax));
            var takes = CraftEngine.AutoSelect(player, recipe, batch, false, null);
            if (takes == null) { UIManager.Toast(Loc.T("材料が足りない", "Not enough materials")); return; }
            int fee = recipe.CommissionFee * batch;
            if (!player.Wallet.CanAfford(fee)) { UIManager.Toast(Loc.T("お金が足りない", "Not enough money")); return; }
            var used = new List<string>();
            var cmd = new CookCommissionCommand
            {
                recipeId = recipe.Id,
                batch = batch,
                station = (int)ctx.Type,
                job = (int)job,
                fee = fee,
                outputCount = recipe.OutputCount * batch
            };
            foreach (var t in takes)
            {
                if (!used.Contains(t.ItemId)) used.Add(t.ItemId);
                bool merged = false;
                foreach (var p in cmd.items) if (p.key == t.ItemId) { p.value += t.Count; merged = true; }
                if (!merged) cmd.items.Add(new StringIntPair(t.ItemId, t.Count));
            }
            cmd.outputItemId = recipe.ResolveOutput(used);
            var sys = CookingSystem.Instance;
            var weather = ctx.Weather;
            var type = ctx.Type;
            WorldAuthority.Submit(cmd, r =>
            {
                if (r == null || !r.Success) return;
                sys.OnCrafted(player, recipe, cmd.outputItemId, cmd.outputCount, Mathf.RoundToInt(CookingTuning.CommissionQuality), type, used, recipe.Steps, null, weather, true);
                UIManager.Toast(Loc.Format("{0} ×{1} を作ってもらった", "Got {0} x{1} made", ItemRegistry.NameOf(cmd.outputItemId), cmd.outputCount));
            });
        }

        /// <summary>できあがった時間加工を取り出す。</summary>
        public static void Collect(PlayerState player, TimedSlotData slot)
        {
            if (player == null || slot == null) return;
            var sys = CookingSystem.Instance;
            var copy = slot;
            WorldAuthority.Submit(new CookCollectTimedCommand { slotId = slot.slotId }, r =>
            {
                if (r == null || !r.Success) return;
                var recipe = RecipeBook.Get(copy.recipeId);
                var steps = new List<CraftStep>();
                foreach (var s in copy.steps) steps.Add((CraftStep)s);
                var weather = WeatherState.Fine;
                var ws = Game.Instance != null ? Game.Instance.Service<IWeatherService>() : null;
                if (ws != null) weather = ws.Current;
                sys.OnCrafted(player, recipe, copy.outputItemId, copy.count, copy.quality, (CraftStationType)copy.stationType, copy.ingredients, steps, null, weather, false);
                UIManager.Toast(Loc.Format("{0} ×{1} {2} を取り出した", "Collected {0} x{1} {2}", ItemRegistry.NameOf(copy.outputItemId), copy.count, ItemStack.QualityStars(copy.quality)));
            });
        }
    }
}
