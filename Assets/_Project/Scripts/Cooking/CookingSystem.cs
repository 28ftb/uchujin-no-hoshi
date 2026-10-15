using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 料理・加工システム（料理・加工仕様 v0.2）。
    /// 「宇宙生物と一緒に料理する。生き物の能力を発見すると、料理や加工の可能性が増える」。
    ///
    /// ・宇宙生物と調理器具の3方式（§2）：A 器具に住む生物／B 生物自身（同行・CreatureDevice）／C 複数生物の協力（同じ台所の器具をつなぐ）
    /// ・村の共用調理場（§2.1）：生物なしで基本料理。自宅キッチン：生物と器具で多くの料理・まとめ調理・特殊加工
    /// ・レシピ（料理・加工食品・素材加工・ロケット部品・器具）と自由な実験（§5）。謎料理も発見（§5.5）
    /// ・初めての料理は短い操作（§6.1）、2回目以降はまとめて快適に（§6.2）。生物の調理習熟（§6.3）
    /// ・時間経過の加工（§8.1）と天候（§8.2）。住民の料理人の実演（§11・§12）。料理・加工記録アプリ（§12.2）
    /// ・ロケット用の加工レシピ（ロケット仕様 §9）と職人への依頼（§9.3）
    /// </summary>
    public sealed class CookingSystem : GameSystemBase, ICraftingService, IFoodEffectService
    {
        public override string SaveKey => "cooking";
        public override int DailyUpdateOrder => 350;

        public static CookingSystem Instance { get; private set; }

        private CookingPlanetData planet = new CookingPlanetData();
        private readonly Dictionary<string, CookingKnowledgeData> knowledge = new Dictionary<string, CookingKnowledgeData>();
        private readonly Dictionary<string, CookingProfileData> profiles = new Dictionary<string, CookingProfileData>();

        /// <summary>この星でよく見る料理（§14：星ごとの食文化【提案】。料理人の実演に出やすい）。</summary>
        public readonly List<string> LocalSpecialties = new List<string>();
        public string CommunityKitchenId { get; private set; }
        public Vector3? CommunityKitchenPosition { get; private set; }

        private CommunityKitchenView communityView;
        private RecipeRecordApp tabletApp;
        private FoodEatHandler eatHandler;
        private FoodEffectOverlay effectOverlay;
        private CookingResidentMenus residentMenus;
        private IResidentDirectory registeredDirectory;
        private bool subscribed;

        public CookingPlanetData PlanetData => planet;

        public CookingSystem()
        {
            Instance = this;
        }

        // =================================================================
        // ライフサイクル
        // =================================================================

        public override void RegisterContent()
        {
            Instance = this;
            CookingContent.RegisterAll();
            WorldAuthority.Register<CookStartTimedCommand>(HandleStartTimed);
            WorldAuthority.Register<CookCollectTimedCommand>(HandleCollectTimed);
            WorldAuthority.Register<CookCommissionCommand>(HandleCommission);
            WorldAuthority.Register<CookPayFeeCommand>(HandlePayFee);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            Instance = this;
            // §14：星ごとの郷土料理（シードから決定的に3品）
            LocalSpecialties.Clear();
            var rng = ctx.Rng("cooking.local_specialties");
            var cands = new List<string>();
            foreach (var r in RecipeBook.All) if (r.Demonstrable && (r.Kind == RecipeKind.Dish || r.Kind == RecipeKind.Processed)) cands.Add(r.Id);
            rng.Shuffle(cands);
            for (int i = 0; i < Mathf.Min(3, cands.Count); i++) LocalSpecialties.Add(cands[i]);

            CommunityKitchenId = null;
            CommunityKitchenPosition = null;
            var layout = ctx.Layout;
            if (layout != null)
            {
                var fac = layout.GetFacility(FacilityType.CommunityKitchen);
                if (fac != null)
                {
                    CommunityKitchenId = fac.id;
                    CommunityKitchenPosition = fac.position;
                }
                else
                {
                    // 共用調理場の施設が無い星：広場のそばに仮設の調理台を置く（序盤から料理できることを保証）
                    var plaza = layout.GetFacility(FacilityType.Plaza);
                    CommunityKitchenId = "cooking.community_kitchen";
                    CommunityKitchenPosition = plaza != null ? plaza.position + new Vector3(7f, 0f, 4f) : layout.PlayerSpawn + new Vector3(6f, 0f, 6f);
                }
            }
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            planet = new CookingPlanetData();
            knowledge.Clear();
            var local = Game?.LocalPlayerId;
            // 最初の星から始める新しいゲームなら、持ち歩きの記録（習熟・食の記録・持ち越しレシピ）も新しくする
            bool freshGame = ctx.Planet != null && ctx.Planet.planetIndex <= 1 && string.IsNullOrEmpty(ctx.Planet.previousWorldId);
            if (freshGame && local != null) profiles[local] = new CookingProfileData();
        }

        public override void BuildScene()
        {
            BuildCommunityKitchen();
        }

        public override void OnPlayStarted()
        {
            Instance = this;
            if (!subscribed)
            {
                EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
                subscribed = true;
            }
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                if (tabletApp == null) tabletApp = new RecipeRecordApp(this);
                try { tablet.RegisterApp(tabletApp); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (eatHandler == null) eatHandler = new FoodEatHandler(this);
            ToolUseRouter.Register(eatHandler);
            if (effectOverlay == null) effectOverlay = new FoodEffectOverlay(this);
            UIManager.RegisterOverlay(effectOverlay);

            var dir = Game.Service<IResidentDirectory>();
            if (dir != null && dir != registeredDirectory)
            {
                if (residentMenus == null) residentMenus = new CookingResidentMenus(this);
                try { dir.RegisterInteractionProvider(residentMenus); }
                catch (Exception e) { Debug.LogException(e); }
                registeredDirectory = dir;
            }
            FoodEffects.EnsureRunner(this);
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
                subscribed = false;
            }
            if (eatHandler != null) ToolUseRouter.Unregister(eatHandler);
            if (effectOverlay != null) UIManager.UnregisterOverlay(effectOverlay);
            FoldCarriedRecipes();
            planet = new CookingPlanetData();
            knowledge.Clear();
            communityView = null;
            LocalSpecialties.Clear();
        }

        public override void OnGameMinutesAdvanced(int minutes)
        {
            FoodEffects.Advance(this, minutes);
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (!IsAuthority) return;
            var weather = Game.Service<IWeatherService>();
            var w = weather != null ? weather.Current : WeatherState.Fine;
            bool changed = false;
            foreach (var s in planet.slots)
            {
                if (s.ready) continue;
                float rate = 1f;
                // §8.2：晴れは天日干しに向く、強風は屋外乾燥が早い、雨・霧は発酵が進む【仮数値】
                if (s.hasDry && s.outdoor)
                {
                    if (!w.IsRaining && w.sky <= SkyState.Sunny) rate *= 1.5f;
                    if (w.wind == WindLevel.Strong) rate *= 1.25f;
                    if (w.IsRaining) rate *= 0.6f;
                }
                if (s.hasFerment && (w.IsRaining || w.IsFoggy)) rate *= 1.2f;
                s.progressHours += rate;
                changed = true;
                if (s.progressHours >= s.hoursRequired)
                {
                    s.ready = true;
                    NotifyReady(s);
                }
            }
            if (changed)
            {
                MarkDirty();
                RefreshVisuals();
            }
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            var weather = Game.Service<IWeatherService>();
            if (weather == null) return;
            var w = weather.Current;
            bool changed = false;
            foreach (var s in planet.slots)
            {
                if (s.ready || s.weatherBonusApplied) continue;
                // §8.2：雨の日をまたいだ発酵、晴れの日の天日干しは味が良くなる
                bool bonus = (s.hasFerment && (w.IsRaining || w.IsFoggy)) || (s.hasDry && s.outdoor && !w.IsRaining && w.sky <= SkyState.Sunny);
                if (!bonus) continue;
                s.quality = Mathf.Min(3, s.quality + 1);
                s.weatherBonusApplied = true;
                changed = true;
            }
            if (changed) MarkDirty();
        }

        private void OnWeatherChanged(WeatherChangedEvent e)
        {
            // 共用調理場の見た目（雨の日は屋根の下に寄せる等）は今は変えない。将来の演出用フック。
        }

        private void NotifyReady(TimedSlotData s)
        {
            if (s.notified) return;
            s.notified = true;
            if (Game.LocalPlayer != null && s.ownerId == Game.LocalPlayerId)
                UIManager.Toast(Loc.Format("{0}の{1}ができあがったようだ", "{1} at {0} seems ready", Loc.T(s.stationNameJa, s.stationNameEn), ItemRegistry.NameOf(s.outputItemId)));
        }

        // =================================================================
        // セーブ
        // =================================================================

        /// <summary>§14【提案】：この星で作れるようになったレシピを、持ち歩く記録（Profile）へまとめて次の星へ持ち越す。</summary>
        private void FoldCarriedRecipes()
        {
            if (!CookingTuning.CarryRecipesAcrossPlanets) return;
            foreach (var kv in knowledge)
            {
                var prof = Profile(kv.Key);
                foreach (var rec in kv.Value.recipes)
                    if (rec.timesMade > 0 && !prof.carriedRecipes.Contains(rec.recipeId)) prof.carriedRecipes.Add(rec.recipeId);
            }
        }

        public override string CaptureState(SaveScope scope, string playerId)
        {
            // 訪問先から帰る時（ゲストの保存は Teardown より前）にも持ち越しが失われないよう、保存の度にまとめる
            if (scope == SaveScope.PlayerProfile) FoldCarriedRecipes();
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(planet);
                case SaveScope.PlayerPlanetKnowledge:
                    if (playerId != null && knowledge.TryGetValue(playerId, out var k)) return JsonUtility.ToJson(k);
                    return null;
                case SaveScope.PlayerProfile:
                    if (playerId != null && profiles.TryGetValue(playerId, out var p)) return JsonUtility.ToJson(p);
                    return null;
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                switch (scope)
                {
                    case SaveScope.Planet:
                        planet = JsonUtility.FromJson<CookingPlanetData>(json) ?? new CookingPlanetData();
                        if (planet.slots == null) planet.slots = new List<TimedSlotData>();
                        RefreshVisuals();
                        break;
                    case SaveScope.PlayerPlanetKnowledge:
                        if (playerId == null) return;
                        var k = JsonUtility.FromJson<CookingKnowledgeData>(json) ?? new CookingKnowledgeData();
                        Sanitize(k);
                        knowledge[playerId] = k;
                        break;
                    case SaveScope.PlayerProfile:
                        if (playerId == null) return;
                        var p = JsonUtility.FromJson<CookingProfileData>(json) ?? new CookingProfileData();
                        if (p.carriedRecipes == null) p.carriedRecipes = new List<string>();
                        if (p.mastery == null) p.mastery = new List<MasteryEntry>();
                        if (p.foodLog == null) p.foodLog = new List<FoodLogEntry>();
                        profiles[playerId] = p;
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void Sanitize(CookingKnowledgeData k)
        {
            if (k.recipes == null) k.recipes = new List<RecipeRecord>();
            if (k.tries == null) k.tries = new List<TryRecord>();
            if (k.demos == null) k.demos = new List<DemoRecord>();
            if (k.lastDemoDay == null) k.lastDemoDay = new List<StringIntPair>();
        }

        // =================================================================
        // ICraftingService
        // =================================================================

        public void OpenStation(CraftStationType type, string stationInstanceId, PlayerState player)
        {
            if (player == null) player = LocalPlayer;
            if (player == null || !player.IsLocal) return;
            var existing = UIManager.Find<StationWindow>();
            if (existing != null) existing.Close();
            StationContext ctx;
            try { ctx = CraftEngine.BuildContext(type, stationInstanceId, player); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            UIManager.Open(new StationWindow(this, ctx));
        }

        public IEnumerable<string> RecipesProducing(string itemId)
        {
            var list = new List<string>();
            foreach (var r in RecipeBook.Producing(itemId)) list.Add(r.Id);
            return list;
        }

        public bool IsRecipeKnown(string playerId, string recipeId)
        {
            var r = RecipeBook.Get(recipeId);
            return r != null && IsKnown(playerId, r);
        }

        /// <summary>レシピの呼び名。ローカルプレイヤーが自分で名前を付けていればそれを返す（主人公の呼び名）。</summary>
        public string GetRecipeName(string recipeId)
        {
            var r = RecipeBook.Get(recipeId);
            if (r == null) return recipeId;
            var local = Game?.LocalPlayerId;
            if (local != null && knowledge.TryGetValue(local, out var k))
            {
                foreach (var rec in k.recipes)
                    if (rec.recipeId == recipeId && !string.IsNullOrEmpty(rec.customName)) return rec.customName;
            }
            return r.Name;
        }

        // =================================================================
        // 他モジュール向けの公開API（コア契約の追加待ち：IFoodEffectService 案）
        // =================================================================

        /// <summary>そのプレイヤーが食べ物の効果を受けているか（例：釣りモジュールが "fishing_bite" を参照）。</summary>
        public bool HasFoodEffect(string playerId, string effectId)
        {
            if (!CookingTuning.FoodEffectsEnabled || playerId == null) return false;
            if (!profiles.TryGetValue(playerId, out var p)) return false;
            return p.effectId == effectId && p.effectMinutes > 0;
        }

        public string GetActiveFoodEffect(string playerId)
        {
            if (!CookingTuning.FoodEffectsEnabled || playerId == null) return null;
            if (!profiles.TryGetValue(playerId, out var p) || p.effectMinutes <= 0) return null;
            return p.effectId;
        }

        // =================================================================
        // 記録（知識・持ち歩き）
        // =================================================================

        public CookingKnowledgeData Knowledge(string playerId)
        {
            if (playerId == null) playerId = Game?.LocalPlayerId ?? "local";
            if (!knowledge.TryGetValue(playerId, out var k))
            {
                k = new CookingKnowledgeData();
                knowledge[playerId] = k;
            }
            return k;
        }

        public CookingProfileData Profile(string playerId)
        {
            if (playerId == null) playerId = Game?.LocalPlayerId ?? "local";
            if (!profiles.TryGetValue(playerId, out var p))
            {
                p = new CookingProfileData();
                profiles[playerId] = p;
            }
            return p;
        }

        public RecipeRecord FindRecord(string playerId, string recipeId)
        {
            foreach (var r in Knowledge(playerId).recipes) if (r.recipeId == recipeId) return r;
            return null;
        }

        public RecipeRecord GetOrCreateRecord(string playerId, string recipeId)
        {
            var rec = FindRecord(playerId, recipeId);
            if (rec != null) return rec;
            rec = new RecipeRecord { recipeId = recipeId, firstDay = Clock != null ? Clock.Day : 0 };
            Knowledge(playerId).recipes.Add(rec);
            return rec;
        }

        public bool HasMade(string playerId, string recipeId)
        {
            var rec = FindRecord(playerId, recipeId);
            return rec != null && rec.timesMade > 0;
        }

        public bool IsCarried(string playerId, string recipeId)
        {
            if (!CookingTuning.CarryRecipesAcrossPlanets) return false;
            return Profile(playerId).carriedRecipes.Contains(recipeId);
        }

        /// <summary>作り方を知っている（最初から・自分で作った・前の星で覚えた）。</summary>
        public bool IsKnown(string playerId, RecipeDef r)
        {
            if (r == null) return false;
            return r.StartKnown || HasMade(playerId, r.Id) || IsCarried(playerId, r.Id);
        }

        /// <summary>住民の実演で手掛かりを見た（作ったことはない）。</summary>
        public DemoRecord FindDemo(string playerId, string recipeId)
        {
            foreach (var d in Knowledge(playerId).demos) if (d.recipeId == recipeId) return d;
            return null;
        }

        public void AddTry(string playerId, TryRecord t)
        {
            var k = Knowledge(playerId);
            k.tries.Add(t);
            while (k.tries.Count > CookingTuning.MaxTryRecords) k.tries.RemoveAt(0);
        }

        public void AddMastery(string playerId, string uid, CreatureAbility ability)
        {
            var p = Profile(playerId);
            foreach (var m in p.mastery)
            {
                if (m.uid == uid && m.ability == (int)ability)
                {
                    int before = m.uses / CookingTuning.MasteryUsesPerLevel;
                    m.uses++;
                    int after = m.uses / CookingTuning.MasteryUsesPerLevel;
                    if (after > before && after <= CookingTuning.MasteryMaxLevel && playerId == Game?.LocalPlayerId)
                        UIManager.Toast(Loc.Format("生き物が「{0}」に慣れてきたようだ", "Your creature is getting better at \"{0}\"", CookingTables.Ability(ability).Name));
                    return;
                }
            }
            p.mastery.Add(new MasteryEntry { uid = uid, ability = (int)ability, uses = 1 });
        }

        /// <summary>
        /// 完成した（即時の調理・時間加工の取り出し・依頼）時の記録とイベント発行。
        /// </summary>
        public void OnCrafted(PlayerState player, RecipeDef recipe, string outputItemId, int count, int quality, CraftStationType station,
            IList<string> usedItemIds, IList<CraftStep> steps, IList<AbilitySource> sources, WeatherState weather, bool commissioned)
        {
            if (player == null) return;
            string pid = player.PlayerId;
            bool first = false;
            if (recipe != null)
            {
                var rec = GetOrCreateRecord(pid, recipe.Id);
                if (commissioned) rec.timesCommissioned++;
                else
                {
                    first = rec.timesMade == 0;
                    rec.timesMade++;
                }
                rec.lastDay = Clock != null ? Clock.Day : 0;
                if (rec.firstDay == 0) rec.firstDay = rec.lastDay;
                rec.bestQuality = Mathf.Max(rec.bestQuality, quality);
                rec.lastStation = (int)station;
                rec.lastOutputItemId = outputItemId;
                if (usedItemIds != null) rec.lastIngredients = new List<string>(usedItemIds);
                if (steps != null)
                {
                    rec.lastSteps = new List<int>();
                    foreach (var s in steps) rec.lastSteps.Add((int)s);
                }
                rec.weatherNote = weather.Summary();
                if (sources != null)
                {
                    foreach (var src in sources)
                        if (src != null && !string.IsNullOrEmpty(src.SpeciesId) && !rec.creatureSpecies.Contains(src.SpeciesId)) rec.creatureSpecies.Add(src.SpeciesId);
                }
                if (first && !commissioned) OnFirstDiscovery(player, recipe, outputItemId, rec);
            }
            if (sources != null && !commissioned)
            {
                var seen = new HashSet<string>();
                foreach (var src in sources)
                {
                    if (src == null || src.CreatureUid == null || src.Borrowed) continue;
                    if (!seen.Add(src.CreatureUid + ":" + src.Ability)) continue;
                    AddMastery(pid, src.CreatureUid, src.Ability);
                }
            }
            EventBus.Publish(new ItemCraftedEvent
            {
                PlayerId = pid,
                ItemId = outputItemId,
                Count = count,
                Quality = quality,
                RecipeId = recipe != null ? recipe.Id : null,
                Station = station,
                FirstTime = first
            });
        }

        private void OnFirstDiscovery(PlayerState player, RecipeDef recipe, string outputItemId, RecipeRecord rec)
        {
            if (!player.IsLocal) return;
            UIManager.Toast(Loc.Format("新しい作り方を見つけた：{0}", "New recipe discovered: {0}", ItemRegistry.NameOf(outputItemId)), 5f);
            var tablet = Game.Service<ITabletService>();
            if (tablet == null) return;
            // 資料ボックスへ：自分で実際に行った手順・使った物の事実だけを書く（§12.2）
            var m = new TabletMaterial
            {
                id = "cooking.recipe." + recipe.Id,
                kind = TabletMaterialKind.Recipe,
                titleJa = ItemRegistry.Get(outputItemId) != null ? ItemRegistry.Get(outputItemId).NameJa : recipe.NameJa,
                titleEn = ItemRegistry.Get(outputItemId) != null ? ItemRegistry.Get(outputItemId).NameEn : recipe.NameEn,
                factJa = "自分で作った。使った物：" + JoinNames(rec.lastIngredients, true) + "／手順：" + StepsJoin(rec.lastSteps, true),
                factEn = "Made it myself. Used: " + JoinNames(rec.lastIngredients, false) + " / Steps: " + StepsJoin(rec.lastSteps, false),
                day = Clock != null ? Clock.Day : 0,
                minuteOfDay = Clock != null ? Clock.MinuteOfDay : 0
            };
            m.relatedIds.Add(outputItemId);
            if (rec.lastIngredients != null) m.relatedIds.AddRange(rec.lastIngredients);
            m.tags.Add("cooking");
            try { tablet.AddMaterial(m); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private static string JoinNames(List<string> ids, bool ja)
        {
            if (ids == null) return "";
            var parts = new List<string>();
            foreach (var id in ids)
            {
                var d = ItemRegistry.Get(id);
                parts.Add(d == null ? id : (ja ? d.NameJa : d.NameEn));
            }
            return string.Join(ja ? "、" : ", ", parts.ToArray());
        }

        private static string StepsJoin(List<int> steps, bool ja)
        {
            if (steps == null) return "";
            var parts = new List<string>();
            foreach (var s in steps)
            {
                var info = CookingTables.Step((CraftStep)s);
                parts.Add(ja ? info.NameJa : info.NameEn);
            }
            return string.Join(" → ", parts.ToArray());
        }

        // =================================================================
        // 時間経過の加工（Planet スコープ）
        // =================================================================

        public IEnumerable<TimedSlotData> SlotsAt(string key)
        {
            foreach (var s in planet.slots) if (s.stationKey == key) yield return s;
        }

        public int CountSlots(string key)
        {
            int n = 0;
            foreach (var s in planet.slots) if (s.stationKey == key) n++;
            return n;
        }

        public IEnumerable<TimedSlotData> SlotsOwnedBy(string playerId)
        {
            foreach (var s in planet.slots) if (s.ownerId == playerId) yield return s;
        }

        private CommandResult HandleStartTimed(CookStartTimedCommand cmd, CommandContext ctx)
        {
            if (cmd.slot == null || string.IsNullOrEmpty(cmd.slot.stationKey)) return CommandResult.Fail("仕込めませんでした", "Could not start");
            if (CountSlots(cmd.slot.stationKey) >= Mathf.Max(1, cmd.capacity))
                return CommandResult.Fail("いっぱいで仕込めません", "It's full");
            var s = cmd.slot;
            if (string.IsNullOrEmpty(s.slotId)) s.slotId = Ids.New();
            s.ownerId = ctx.IssuerId;
            s.startDay = Clock != null ? Clock.Day : 0;
            s.startMinute = Clock != null ? Clock.MinuteOfDay : 0;
            s.progressHours = 0f;
            s.ready = s.hoursRequired <= 0f;
            s.notified = false;
            planet.slots.Add(s);
            MarkDirty();
            RefreshVisuals();
            return CommandResult.Ok();
        }

        private CommandResult HandleCollectTimed(CookCollectTimedCommand cmd, CommandContext ctx)
        {
            TimedSlotData slot = null;
            foreach (var s in planet.slots) if (s.slotId == cmd.slotId) slot = s;
            if (slot == null) return CommandResult.Fail("もう取り出されています", "Already collected");
            if (!slot.ready) return CommandResult.Fail("まだできあがっていません", "Not ready yet");
            if (slot.ownerId != ctx.IssuerId && !ctx.HasPermission(PermissionLevel.CoLiving))
                return CommandResult.Fail("ほかの人の仕込みです", "That belongs to someone else");
            ctx.GiveItem(new ItemStack(slot.outputItemId, Mathf.Max(1, slot.count), slot.quality), "cooking.timed");
            planet.slots.Remove(slot);
            MarkDirty();
            RefreshVisuals();
            return CommandResult.Ok();
        }

        private CommandResult HandleCommission(CookCommissionCommand cmd, CommandContext ctx)
        {
            var recipe = RecipeBook.Get(cmd.recipeId);
            if (recipe == null || recipe.CommissionFee <= 0) return CommandResult.Fail("頼めない品です", "Can't commission that");
            foreach (var it in cmd.items)
                if (!ctx.HasItem(it.key, it.value)) return CommandResult.Fail("材料が足りません", "Not enough materials");
            int fee = Mathf.Max(0, cmd.fee);
            if (!ctx.TryCharge(fee, "cooking.commission")) return CommandResult.Fail("お金が足りません", "Not enough money");
            foreach (var it in cmd.items)
            {
                if (!ctx.TakeItem(it.key, it.value, "cooking.commission"))
                {
                    ctx.PayMoney(fee, "cooking.commission.refund");
                    return CommandResult.Fail("材料が足りません", "Not enough materials");
                }
            }
            var output = string.IsNullOrEmpty(cmd.outputItemId) ? recipe.OutputItemId : cmd.outputItemId;
            ctx.GiveItem(new ItemStack(output, Mathf.Max(1, cmd.outputCount), Mathf.RoundToInt(CookingTuning.CommissionQuality)), "cooking.commission");
            var dir = Game.Service<IResidentDirectory>();
            var resident = dir != null ? dir.FindByJob((ResidentJob)cmd.job) : null;
            if (resident != null)
            {
                try
                {
                    if (fee > 0) dir.GiveMoney(resident.Id, fee, "commission:" + recipe.Id);
                    dir.ChangeFriendship(resident.Id, ctx.IssuerId, 1, "commission");
                    dir.AddMemory(resident.Id, new ResidentMemoryEntry { kind = "made_for_player", playerId = ctx.IssuerId, itemId = output, day = Clock != null ? Clock.Day : 0, importance = 0.3f });
                    // できた物を差し出して「作った」と伝える（意味データ。言葉が分からなくても身振りで分かる）
                    var u = new Utterance(resident.Id, "cooking.commission_done")
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.Item(output))).Past())
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.Item(output))).Recipient(Concepts.YOU))
                        .Face(Expression.Happy).With(Gesture.OfferItem, output).Related(output);
                    dir.Speak(resident.Id, u);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            return CommandResult.Ok();
        }

        private CommandResult HandlePayFee(CookPayFeeCommand cmd, CommandContext ctx)
        {
            if (cmd.amount <= 0) return CommandResult.Ok();
            if (!ctx.TryCharge(cmd.amount, cmd.reason ?? "cooking.fee")) return CommandResult.Fail("お金が足りません", "Not enough money");
            var dir = Game.Service<IResidentDirectory>();
            var resident = dir != null ? dir.FindByJob((ResidentJob)cmd.job) : null;
            if (resident != null)
            {
                try { dir.GiveMoney(resident.Id, cmd.amount, cmd.reason ?? "cooking.fee"); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return CommandResult.Ok();
        }

        // =================================================================
        // 共用調理場（§2.1）
        // =================================================================

        private void BuildCommunityKitchen()
        {
            communityView = null;
            if (Game.SceneRoot == null || !CommunityKitchenPosition.HasValue) return;
            var layout = Game.Layout;
            Vector3 pos = CommunityKitchenPosition.Value;
            float yaw = 0f;
            var fac = layout != null ? layout.GetFacilityById(CommunityKitchenId) : null;
            if (fac != null)
            {
                // 施設の建物は World が作るので、調理台は入口の前（屋外）に置く
                var dir = fac.entrance - fac.position;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
                dir.Normalize();
                pos = fac.entrance + dir * 2.2f;
                yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f;
            }
            if (layout != null) pos = layout.OnGround(pos);
            communityView = CommunityKitchenView.Build(this, Game.SceneRoot, pos, yaw, CommunityKitchenId);
            CommunityKitchenPosition = pos;
            RefreshVisuals();
        }

        public void RefreshVisuals()
        {
            if (communityView != null && CommunityKitchenId != null)
            {
                int n = 0;
                var colors = new List<Color>();
                foreach (var s in SlotsAt("community:" + CommunityKitchenId))
                {
                    n++;
                    colors.Add(ItemRegistry.GetOrPlaceholder(s.outputItemId).Color);
                }
                communityView.ShowSlots(colors);
            }
            var w = UIManager.Find<StationWindow>();
            if (w != null) w.MarkRefresh();
        }
    }
}
