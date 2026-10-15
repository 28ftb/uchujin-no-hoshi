using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Fishing
{
    /// <summary>
    /// 釣りシステム（魚釣りシステム仕様案 v0.2）。
    /// ・宇宙魚41種（§7, §9）を ItemRegistry に登録。修理用の ひやひれ（FishFrostFin）・ゆらめ（FishRippleEye）を含む。
    /// ・出現条件（§6）：釣り場の水域・特徴タグ、時間帯、天候・宇宙固有現象、餌、日替わりの傾向（決定的）。四季なし。
    /// ・操作：竿を装備して水辺で道具使用（または餌を手に持って使用）→ キャスト → 待つ → アタリ → ミニゲーム（§3）→ 釣果（§8）。
    /// ・成長（§4）：竿の段階1〜5、釣りレベル、名人から教わる技（§4.3, §5）。
    /// ・釣果帳アプリ "fishlog"（PlayerPlanetKnowledge）。技能は PlayerProfile、村の釣果ボードは Planet。
    /// ・マルチ（§13）：同じ釣り場で並んで釣れる。釣った魚は釣った人の持ち物。村の釣果ボードで大物を見せ合える。
    /// </summary>
    public sealed class FishingSystem : GameSystemBase, IFishingService
    {
        public override string SaveKey => "fishing";
        public override int DailyUpdateOrder => 300;

        /// <summary>技を本人の端末へ届ける一時メッセージのチャンネル（<see cref="TechniqueGrantMessage"/>）。</summary>
        public const string TechniqueChannel = "fishing.technique";

        public FishSpawnModel Spawn { get; } = new FishSpawnModel();
        public WorldLayout Layout => Game != null ? Game.Layout : null;
        public int CurrentDay => Clock != null ? Clock.Day : 1;
        public FishingPlanetSave PlanetData => planet;
        public FishLogSave FishLog => log;

        private readonly Dictionary<string, FishingProfileData> profiles = new Dictionary<string, FishingProfileData>();
        private FishLogSave log = new FishLogSave();
        private FishingPlanetSave planet = new FishingPlanetSave();
        private ulong seed;
        private int castCounter;
        private FishingSession session;
        private readonly List<FishSpotView> spotViews = new List<FishSpotView>();
        private readonly Dictionary<string, string> lastSightingKey = new Dictionary<string, string>();
        private float sightingTimer;
        private GameObject hutStation;

        private readonly ToolHandler toolHandler;
        private readonly Overlay overlay;
        private readonly FishingMasterInteractions interactions;
        private readonly FishLogApp app;

        public FishingSystem()
        {
            toolHandler = new ToolHandler(this);
            overlay = new Overlay(this);
            interactions = new FishingMasterInteractions(this);
            app = new FishLogApp(this);
        }

        // =====================================================================
        // ライフサイクル
        // =====================================================================

        public override void RegisterContent()
        {
            FishCatalog.RegisterItems();
            WorldAuthority.Register<ReportCatchCommand>(HandleReportCatch);
            WorldAuthority.Register<UpgradeRodCommand>(HandleUpgradeRod);
            WorldAuthority.Register<LessonLearnedCommand>(HandleLessonLearned);
            WorldAuthority.RegisterTransientHandler(TechniqueChannel, OnTechniqueMessage);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            seed = ctx.Seed;
            Spawn.Prepare(ctx.Seed, ctx.Layout);
            planet = new FishingPlanetSave();
            log = new FishLogSave();
            castCounter = 0;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            // 新規ゲーム、または次の星へ「技能を引き継がない」を選んだ時は技能を初期化する。
            if (ctx.ResetSkills && LocalPlayer != null)
                profiles[LocalPlayer.PlayerId] = new FishingProfileData { playerId = LocalPlayer.PlayerId };
        }

        public override void BuildScene()
        {
            // 鉱山モジュールが PreparePlanet で地下湖の釣り場を Layout に追加するため、ここで読み直す
            Spawn.RefreshSpots(Layout);
            spotViews.Clear();
            var root = PrimitiveBuilder.Empty("FishingSpots", Game.SceneRoot, Vector3.zero);
            foreach (var s in Spawn.Spots)
            {
                var go = PrimitiveBuilder.Empty("FishSpot_" + s.Id, root.transform, Vector3.zero);
                go.transform.position = new Vector3(s.WaterPosition.x, s.SurfaceY - 0.12f, s.WaterPosition.z);
                var view = go.AddComponent<FishSpotView>();
                view.Init(s.Id);
                spotViews.Add(view);
            }
            BuildHutStation();
            RefreshNotables();
        }

        public override void OnPlayStarted()
        {
            ToolUseRouter.Register(toolHandler);
            UIManager.RegisterOverlay(overlay);
            Game.Service<IResidentDirectory>()?.RegisterInteractionProvider(interactions);
            Game.Service<ITabletService>()?.RegisterApp(app);
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Subscribe<AreaChangedEvent>(OnAreaChanged);
        }

        public override void Teardown()
        {
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Unsubscribe<AreaChangedEvent>(OnAreaChanged);
            ToolUseRouter.Unregister(toolHandler);
            UIManager.UnregisterOverlay(overlay);
            if (session != null && session.IsActive) session.End(null);
            session = null;
            spotViews.Clear();
            lastSightingKey.Clear();
            hutStation = null;
        }

        public override void Tick(float deltaTime)
        {
            if (session != null)
            {
                session.Update(deltaTime);
                if (!session.IsActive) session = null;
            }
            sightingTimer -= deltaTime;
            if (sightingTimer <= 0f)
            {
                sightingTimer = 1f;
                CheckSightings();
            }
        }

        public override void OnHourChanged(int day, int hour) => RefreshNotables();

        public override void OnDayStarted(int day)
        {
            Spawn.EnsureTrend(day);
            RefreshNotables();
        }

        private void OnWeatherChanged(WeatherChangedEvent e) => RefreshNotables();

        private void OnAreaChanged(AreaChangedEvent e)
        {
            if (session != null && LocalPlayer != null && e.PlayerId == LocalPlayer.PlayerId) session.End(null);
        }

        // =====================================================================
        // IFishingService
        // =====================================================================

        public IEnumerable<string> CatchableFishIds
        {
            get
            {
                var waters = Spawn.PlanetWaters;
                foreach (var s in FishCatalog.All)
                {
                    if (s.IsLegend) continue;
                    if (waters == FishWater.None || (s.Waters & waters) != 0) yield return s.Id;
                }
            }
        }

        public int GetFishingLevel(string playerId) => GetProfile(playerId).level;

        public bool IsFishing(string playerId) =>
            session != null && session.IsActive && session.Player != null && session.Player.PlayerId == playerId;

        /// <summary>技の一覧（ID は <see cref="FishingTechnique"/> の名前。例："LineHandling"）。</summary>
        public IEnumerable<string> TechniqueIds
        {
            get { foreach (var t in TechniqueInfo.All) yield return t.ToString(); }
        }

        /// <summary>技の名前（主人公の呼び名。UI 言語で返す）。不明な ID は null。</summary>
        public string GetTechniqueName(string techniqueId) =>
            TryParseTechnique(techniqueId, out var t) ? TechniqueInfo.Name(t) : null;

        /// <summary>
        /// 技を覚えさせる（釣り名人のクエストなど）。技は PlayerProfile＝本人の端末が正本なので、
        /// ローカルのプレイヤーならその場で覚え（既に覚えていれば false）、リモートのプレイヤーなら一時メッセージで本人へ届けて
        /// 受信側で覚える（相手の習得状況はこちらで分からないため、送った時点で true）。
        /// </summary>
        public bool GrantTechnique(string playerId, string techniqueId)
        {
            if (!TryParseTechnique(techniqueId, out var t)) return false;
            string local = LocalId;
            if (string.IsNullOrEmpty(playerId) || playerId == local) return LearnTechnique(local, t);
            if (!WorldAuthority.IsMultiplayer) return false;
            WorldAuthority.SendTransient(playerId, TechniqueChannel, JsonUtility.ToJson(new TechniqueGrantMessage { technique = t.ToString() }));
            return true;
        }

        private string LocalId => LocalPlayer != null ? LocalPlayer.PlayerId : (Game != null ? Game.LocalPlayerId : null);

        internal static bool TryParseTechnique(string id, out FishingTechnique technique)
        {
            technique = FishingTechnique.LineHandling;
            if (string.IsNullOrEmpty(id)) return false;
            foreach (var t in TechniqueInfo.All)
            {
                if (string.Equals(t.ToString(), id.Trim(), System.StringComparison.OrdinalIgnoreCase))
                {
                    technique = t;
                    return true;
                }
            }
            return false;
        }

        /// <summary>ホストから届いた技の付与（自分の端末で覚える）。</summary>
        private void OnTechniqueMessage(string fromPlayerId, string json)
        {
            string local = LocalId;
            // 正本はホスト：ホスト（権威側）はゲストからの付与を受け付けない（自分宛ての直接呼び出しは除く）
            if (WorldAuthority.IsAuthority && fromPlayerId != local) return;
            if (string.IsNullOrEmpty(json)) return;
            TechniqueGrantMessage m = null;
            try { m = JsonUtility.FromJson<TechniqueGrantMessage>(json); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (m == null || !TryParseTechnique(m.technique, out var t)) return;
            LearnTechnique(local, t);
        }

        // =====================================================================
        // 技能（PlayerProfile）
        // =====================================================================

        public FishingProfileData GetProfile(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) playerId = LocalPlayer != null ? LocalPlayer.PlayerId : "local";
            if (!profiles.TryGetValue(playerId, out var p))
            {
                p = new FishingProfileData { playerId = playerId };
                profiles[playerId] = p;
            }
            return p;
        }

        public void LearnTechnique(PlayerState player, FishingTechnique t)
        {
            if (player == null) return;
            LearnTechnique(player.PlayerId, t);
        }

        /// <summary>技を覚える（新しく覚えたら true。覚えた時は主人公の言語でトースト）。</summary>
        public bool LearnTechnique(string playerId, FishingTechnique t)
        {
            var p = GetProfile(playerId);
            if (p.Knows(t)) return false;
            p.techniques.Add(t.ToString());
            if (string.IsNullOrEmpty(playerId) || playerId == LocalId)
                UIManager.Toast(Loc.Format("技「{0}」を覚えた！", "Learned the technique \"{0}\"!", TechniqueInfo.Name(t)), 5f);
            return true;
        }

        // =====================================================================
        // 釣りの開始・抽選・釣果
        // =====================================================================

        /// <summary>水辺で道具を使った（ToolUseRouter）。</summary>
        public bool TryStartFishing(ToolUseContext ctx)
        {
            var player = ctx.Player;
            if (player == null || !player.IsLocal) return false;
            string bait = null;
            bool rodEquipped = ctx.Tool == ToolType.FishingRod && ctx.SelectedItem == null;
            if (ctx.SelectedItem != null && FishCatalog.IsBait(ctx.SelectedItem.itemId)) bait = ctx.SelectedItem.itemId;
            if (!rodEquipped && bait == null) return false;
            if (!player.Tools.Has(ToolType.FishingRod))
            {
                UIManager.Toast(Loc.T("釣り竿を持っていません", "You don't have a fishing rod"));
                return true;
            }
            if (session != null && session.IsActive) return true;
            if (!IsNearWater(player)) TryFaceNearbyWater(player);
            if (!IsNearWater(player))
            {
                if (rodEquipped) UIManager.Toast(Loc.T("水辺の方を向いて使おう", "Face the water to fish"), 2f);
                return rodEquipped;
            }
            castCounter++;
            var rng = SeededRandom.For(seed, "fishing.cast." + player.PlayerId + "." + CurrentDay + "." + (Clock != null ? Clock.MinuteOfDay : 0) + "." + castCounter);
            session = new FishingSession(this, player, bait, rng);
            return true;
        }

        /// <summary>前方3〜10mのどこかが水面か。</summary>
        public bool IsNearWater(PlayerState player)
        {
            var fwd = player.Forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) return false;
            fwd.Normalize();
            for (float d = FishingTuning.CastMinDistance; d <= FishingTuning.CastMaxDistance; d += 1f)
                if (Spawn.ResolveWaterAt(player.Position + fwd * d, Layout) != null) return true;
            return false;
        }

        /// <summary>前方が水でなくても、すぐ近く（12m以内）に釣り場があればそちらを向く（操作の手助け）。</summary>
        private void TryFaceNearbyWater(PlayerState player)
        {
            if (player.Avatar == null) return;
            SpotInfo best = null;
            float bestD = 12f * 12f;
            foreach (var s in Spawn.Spots)
            {
                var d = s.WaterPosition - player.Position;
                d.y = 0;
                if (d.sqrMagnitude < bestD)
                {
                    bestD = d.sqrMagnitude;
                    best = s;
                }
            }
            if (best == null) return;
            var dir = best.WaterPosition - player.Position;
            dir.y = 0;
            if (dir.sqrMagnitude < 0.01f) return;
            var prev = player.Avatar.rotation;
            player.Avatar.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
            if (!IsNearWater(player)) player.Avatar.rotation = prev;
        }

        /// <summary>キャストの「狙いどころ」（近くの釣り場の魚影までの距離）。</summary>
        public float EstimateSweetSpot(PlayerState player)
        {
            var fwd = player.Forward;
            fwd.y = 0;
            fwd.Normalize();
            float best = float.MaxValue;
            float result = 0.65f;
            foreach (var s in Spawn.Spots)
            {
                var to = s.WaterPosition - player.Position;
                to.y = 0;
                float along = Vector3.Dot(to, fwd);
                if (along < FishingTuning.CastMinDistance || along > FishingTuning.CastMaxDistance + 2f) continue;
                float side = (to - fwd * along).magnitude;
                if (side > 6f || side > best) continue;
                best = side;
                result = Mathf.InverseLerp(FishingTuning.CastMinDistance, FishingTuning.CastMaxDistance, along);
            }
            return Mathf.Clamp(result, 0.12f, 0.92f);
        }

        public FishSpawnModel.WeightContext MakeContext(PlayerState player, SpotInfo spot, string bait, bool precision)
        {
            var weather = Game.Service<IWeatherService>();
            return new FishSpawnModel.WeightContext
            {
                Day = CurrentDay,
                Hour = Clock != null ? Clock.Hour : 12,
                Weather = weather != null ? weather.GetAt(spot != null ? spot.WaterPosition : player.Position) : WeatherState.Fine,
                BaitId = bait,
                Level = player != null ? GetProfile(player.PlayerId).level : 1,
                PrecisionBonus = precision,
                LegendResting = IsLegendResting
            };
        }

        public FishSpecies PickFish(PlayerState player, SpotInfo spot, string bait, bool precision, SeededRandom rng)
        {
            if (spot == null) return null;
            return Spawn.Pick(spot, MakeContext(player, spot, bait, precision), rng);
        }

        public bool IsLegendResting(string fishId)
        {
            foreach (var p in planet.legendRestUntil) if (p.key == fishId) return CurrentDay <= p.value;
            return false;
        }

        /// <summary>釣り上げた（ローカル）。持ち物・経験値・記録・イベント・星への報告。</summary>
        public FishingSession.CatchResult OnFishLanded(PlayerState player, FishSpecies s, float size, int quality, SpotInfo spot)
        {
            var res = new FishingSession.CatchResult { Species = s, Size = size, Quality = quality };
            player.GiveOrDrop(new ItemStack(s.Id, 1, quality) { size = size }, "fishing");

            // 経験値
            var prof = GetProfile(player.PlayerId);
            var entry = FindLog(s.Id);
            res.FirstTime = entry == null;
            res.NewRecord = entry != null && size > entry.maxSize;
            int before = prof.level;
            res.Xp = FishingTuning.XpForCatch(s, res.FirstTime);
            prof.xp += res.Xp;
            prof.totalCaught++;
            prof.level = FishingTuning.LevelForXp(prof.xp);
            res.LevelUp = prof.level > before;
            res.NewLevel = prof.level;

            // 釣果帳（観察した事実）
            var weather = spot.IsUnderground ? FishSpawnModel.CaveWeather : MakeContext(player, spot, null, false).Weather;
            RecordLog(s, size, quality, spot, weather);

            // 連携イベント（釣った人の端末で発行。ゲストの釣果はホストでも報告コマンドの中で発行）
            EventBus.Publish(new FishCaughtEvent
            {
                PlayerId = player.PlayerId, ItemId = s.Id, SizeCm = size, Quality = quality, SpotId = spot.Id, Position = player.Position
            });
            WorldAuthority.Submit(new ReportCatchCommand
            {
                itemId = s.Id, size = size, quality = quality, spotId = spot.Id, playerName = player.DisplayName,
                posX = player.Position.x, posY = player.Position.y, posZ = player.Position.z
            });

            // 伝説・修理用の魚は資料に残す（事実のみ）
            if (res.FirstTime && (s.IsLegend || s.IsRocketMaterial))
            {
                Game.Service<ITabletService>()?.AddMaterial(new TabletMaterial
                {
                    id = "fishing.first." + s.Id,
                    kind = TabletMaterialKind.Photo,
                    titleJa = "釣果：" + s.NameJa,
                    titleEn = "Catch: " + s.NameEn,
                    factJa = string.Format("{0}日目 {1}　{2}で {3:0.0}cm。天気：{4}", CurrentDay, Clock != null ? Clock.FormatClock() : "", spot.Label(), size, weather.Summary()),
                    factEn = string.Format("Day {0} {1} at {2}, {3:0.0}cm. Weather: {4}", CurrentDay, Clock != null ? Clock.FormatClock() : "", spot.Label(), size, weather.Summary()),
                    day = CurrentDay,
                    minuteOfDay = Clock != null ? Clock.MinuteOfDay : 0,
                    relatedIds = new List<string> { s.Id },
                    tags = new List<string> { "fishing" }
                });
            }

            ReactNearbyResident(player, s, size);
            return res;
        }

        private void ReactNearbyResident(PlayerState player, FishSpecies s, float size)
        {
            if (!FishingTuning.ResidentReactsToBigCatch || !IsAuthority) return;
            float pct = Mathf.InverseLerp(s.SizeMin, s.SizeMax, size);
            bool notable = s.Rarity >= 3 || pct >= 0.85f;
            if (!notable) return;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return;
            IResidentInfo near = null;
            float best = FishingTuning.ResidentReactRadius * FishingTuning.ResidentReactRadius;
            foreach (var r in dir.ActiveResidents)
            {
                float d = (r.Position - player.Position).sqrMagnitude;
                if (d < best) { best = d; near = r; }
            }
            if (near == null) return;
            // その住民が実際に見た事実：「わあ」「（その）魚、とても大きい」／珍しい
            var fishArg = MeaningArg.Of(Concepts.Item(s.Id));
            var c = Concepts.ColorFromTags(ItemRegistry.Get(s.Id));
            if (c != null) fishArg.Adj(c);
            var u = new Utterance(near.Id, "fishing.big_catch").Related(s.Id)
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                .Say(MeaningFrame.Make(SpeechAct.Statement, pct >= 0.85f ? Concepts.BIG : Concepts.RARE).Agent(fishArg).Adverb(Concepts.VERY))
                .Face(Expression.Surprised).With(Gesture.Clap).With(Gesture.PointAt, s.Id);
            dir.Speak(near.Id, u, SpeechChannel.Direct);
            dir.AddMemory(near.Id, new ResidentMemoryEntry { kind = "saw_big_catch", playerId = player.PlayerId, itemId = s.Id, day = CurrentDay, importance = s.IsLegend ? 0.9f : 0.5f });
        }

        // =====================================================================
        // 釣果帳（PlayerPlanetKnowledge）
        // =====================================================================

        public FishLogEntry FindLog(string itemId)
        {
            foreach (var e in log.entries) if (e.itemId == itemId) return e;
            return null;
        }

        private void RecordLog(FishSpecies s, float size, int quality, SpotInfo spot, WeatherState weather)
        {
            var e = FindLog(s.Id);
            if (e == null)
            {
                e = new FishLogEntry { itemId = s.Id, firstDay = CurrentDay };
                log.entries.Add(e);
            }
            e.count++;
            if (size > e.maxSize)
            {
                e.maxSize = size;
                e.maxSizeDay = CurrentDay;
                e.maxSizeSpotId = spot.Id;
            }
            e.bestQuality = Mathf.Max(e.bestQuality, quality);
            e.lastDay = CurrentDay;
            e.lastMinute = Clock != null ? Clock.MinuteOfDay : 0;
            e.lastSpotId = spot.Id;
            e.lastWeather = weather;
            if (Clock != null) e.dayPartMask |= 1 << (int)Clock.CurrentDayPart;
            if (!e.spots.Contains(spot.Id)) e.spots.Add(spot.Id);
            foreach (var tag in WeatherTags(weather, spot.IsUnderground)) if (!e.weatherTags.Contains(tag)) e.weatherTags.Add(tag);
        }

        public static IEnumerable<string> WeatherTags(WeatherState w, bool underground)
        {
            if (underground) { yield return "cave"; yield break; }
            if (w.IsRaining) yield return "rain";
            else if (w.sky <= SkyState.Sunny) yield return "sunny";
            else yield return "cloudy";
            if (w.IsFoggy) yield return "fog";
            if (w.wind == WindLevel.Strong) yield return "wind";
            if (w.wind == WindLevel.Calm) yield return "calm";
            if (w.Has(AlienPhenomenon.LightRain)) yield return "light_rain";
            if (w.Has(AlienPhenomenon.StarWind)) yield return "star_wind";
            if (w.Has(AlienPhenomenon.ReverseRain)) yield return "reverse_rain";
            if (w.Has(AlienPhenomenon.StarBand)) yield return "star_band";
            if (w.Has(AlienPhenomenon.CrystalHaze)) yield return "crystal_haze";
            if (w.Has(AlienPhenomenon.BloomTide)) yield return "bloom_tide";
        }

        public static string WeatherTagLabel(string tag)
        {
            switch (tag)
            {
                case "cave": return Loc.T("地下", "underground");
                case "rain": return Loc.T("雨", "rain");
                case "sunny": return Loc.T("晴れ", "sunny");
                case "cloudy": return Loc.T("曇り", "cloudy");
                case "fog": return Loc.T("霧", "fog");
                case "wind": return Loc.T("強風", "strong wind");
                case "calm": return Loc.T("無風", "no wind");
                case "light_rain": return Loc.T("光雨", "light-rain");
                case "star_wind": return Loc.T("星風", "star wind");
                case "reverse_rain": return Loc.T("逆雨", "reverse rain");
                case "star_band": return Loc.T("星の光帯", "star band");
                case "crystal_haze": return Loc.T("結晶霞", "crystal haze");
                case "bloom_tide": return Loc.T("花潮", "bloom tide");
                default: return tag;
            }
        }

        // =====================================================================
        // 魚影（観察の手掛かり）
        // =====================================================================

        public void RefreshNotables()
        {
            if (spotViews.Count == 0 || Clock == null) return;
            foreach (var v in spotViews)
            {
                if (v == null) continue;
                var spot = Spawn.GetSpot(v.spotId);
                if (spot == null) continue;
                var ctx = MakeContext(LocalPlayer, spot, null, false);
                var notable = Spawn.NotableNow(spot, ctx);
                if (notable == null)
                {
                    v.SetNotable(null, Color.black);
                    continue;
                }
                string kind;
                if (notable.Id == SharedItemIds.FishFrostFin) kind = "frost";
                else if (notable.Id == SharedItemIds.FishRippleEye || notable.IsLegend) kind = "ripple";
                else if (notable.Glowing && (spot.IsUnderground || DayPartUtil.IsDark(Clock.Hour))) kind = "glow";
                else kind = "shadow";
                v.SetNotable(kind, notable.Color);
            }
        }

        private void CheckSightings()
        {
            var p = LocalPlayer;
            if (p == null || Clock == null) return;
            foreach (var v in spotViews)
            {
                if (v == null || string.IsNullOrEmpty(v.kind)) continue;
                if ((v.transform.position - p.Position).sqrMagnitude > 14f * 14f) continue;
                string key = CurrentDay + ":" + Clock.Hour + ":" + v.kind;
                if (lastSightingKey.TryGetValue(v.spotId, out var k) && k == key) continue;
                lastSightingKey[v.spotId] = key;
                var spot = Spawn.GetSpot(v.spotId);
                var weather = spot != null && spot.IsUnderground ? FishSpawnModel.CaveWeather : MakeContext(p, spot, null, false).Weather;
                log.sightings.Add(new FishSighting { spotId = v.spotId, day = CurrentDay, minute = Clock.MinuteOfDay, kind = v.kind, color = v.color, weather = weather });
                while (log.sightings.Count > 80) log.sightings.RemoveAt(0);
                if (v.kind != "shadow") UIManager.Toast(SightingText(v.kind), 2.5f);
            }
        }

        public static string SightingText(string kind)
        {
            switch (kind)
            {
                case "frost": return Loc.T("水面に白い霜のようなきらめきが見える…", "Frosty glints sparkle on the water...");
                case "ripple": return Loc.T("風もないのに、水面に輪の波紋が広がっている…", "Rings ripple across the water, though there's no wind...");
                case "glow": return Loc.T("水の中で何かが光っている…", "Something is glowing under the water...");
                default: return Loc.T("大きな魚影が見える", "A big fish shadow");
            }
        }

        // =====================================================================
        // 釣り名人の小屋（名人が不在でも竿の強化・餌・技の見本・村の釣果ボードが使える）
        // =====================================================================

        private void BuildHutStation()
        {
            var layout = Layout;
            if (layout == null) return;
            var hut = layout.GetFacility(FacilityType.FishingHut);
            Vector3 pos;
            float rotY = 0f;
            if (hut != null)
            {
                var toDoor = hut.entrance - hut.position;
                toDoor.y = 0;
                var side = toDoor.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toDoor.normalized) : Vector3.right;
                // 入口の右側の前には World が施設の看板を立てるので、左側（-side）に置く（看板にめり込み [E] を取り合うのを避ける）
                pos = hut.entrance - side * 2.2f;
                rotY = hut.rotationY;
            }
            else if (layout.FishingSpots.Count > 0)
            {
                var s = layout.FishingSpots[0];
                var away = s.standPosition - s.waterPosition;
                away.y = 0;
                pos = s.standPosition + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.back) * 2.5f;
            }
            else return;
            pos = layout.OnGround(pos);
            hutStation = PrimitiveBuilder.Empty("FishingHutStation", Game.SceneRoot, pos);
            hutStation.transform.rotation = Quaternion.Euler(0, rotY, 0);
            var t = hutStation.transform;
            // 道具棚（竿立て・餌の箱・釣果ボード）
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.6f, 0), new Vector3(1.6f, 1.2f, 0.5f), new Color(0.55f, 0.4f, 0.28f), "Shelf");
            for (int i = 0; i < 3; i++)
                PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(-0.5f + i * 0.5f, 1.8f, -0.1f), new Vector3(0.04f, 0.7f, 0.04f), new Color(0.75f, 0.55f, 0.35f), "Rod" + i);
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0.95f, 0.25f, 0.3f), new Vector3(0.5f, 0.5f, 0.5f), new Color(0.4f, 0.6f, 0.75f), "BaitBox");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 2.0f, 0.3f), new Vector3(1.4f, 0.9f, 0.08f), new Color(0.9f, 0.85f, 0.7f), "RecordBoard");
            var fish = FishVisuals.BuildFish(FishCatalog.Get("fish.kurukuru"), t, new Vector3(0, 2.0f, 0.38f), 0.5f);
            fish.transform.localRotation = Quaternion.Euler(0, 90, 0);
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.95f, 1.0f, 0.2f), Vector3.one * 0.18f, new Color(1f, 0.85f, 0.3f), "Bell");
            PrimitiveBuilder.AddBoxCollider(hutStation, new Vector3(0, 1f, 0), new Vector3(2.4f, 2.2f, 1.2f));
            SimpleInteractable.Attach(hutStation, "釣り道具の棚", "Fishing gear shelf", p => interactions.OpenHutMenu(p, null));
        }

        // =====================================================================
        // コマンド（ホスト権威）
        // =====================================================================

        private CommandResult HandleReportCatch(ReportCatchCommand cmd, CommandContext ctx)
        {
            var s = FishCatalog.Get(cmd.itemId);
            if (s == null) return CommandResult.Fail("不明な魚です", "Unknown fish");
            planet.totalCatchesOnPlanet++;
            // 種ごとの最大記録
            VillageFishRecord rec = null;
            foreach (var r in planet.records) if (r.itemId == cmd.itemId) rec = r;
            if (rec == null || cmd.size > rec.size)
            {
                if (rec == null)
                {
                    rec = new VillageFishRecord();
                    planet.records.Add(rec);
                }
                rec.itemId = cmd.itemId;
                rec.size = cmd.size;
                rec.quality = cmd.quality;
                rec.playerId = ctx.IssuerId;
                rec.playerName = cmd.playerName;
                rec.day = CurrentDay;
            }
            if (s.IsLegend)
            {
                bool found = false;
                foreach (var p in planet.legendRestUntil)
                {
                    if (p.key != s.Id) continue;
                    p.value = CurrentDay + FishingTuning.LegendRestDays;
                    found = true;
                }
                if (!found) planet.legendRestUntil.Add(new StringIntPair(s.Id, CurrentDay + FishingTuning.LegendRestDays));
            }
            if (!ctx.IsLocalIssuer)
            {
                // ゲストの釣果：ホスト側のクエスト・住民も反応できるようにホストでも発行する
                EventBus.Publish(new FishCaughtEvent
                {
                    PlayerId = ctx.IssuerId, ItemId = cmd.itemId, SizeCm = cmd.size, Quality = cmd.quality, SpotId = cmd.spotId,
                    Position = new Vector3(cmd.posX, cmd.posY, cmd.posZ)
                });
            }
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleUpgradeRod(UpgradeRodCommand cmd, CommandContext ctx)
        {
            var req = RodUpgrades.ForTier(cmd.targetTier);
            if (req == null) return CommandResult.Fail("これ以上は強化できません", "Cannot upgrade further");
            if (cmd.money != req.Money || !RodUpgrades.ValidatePayment(req, cmd.payItemIds, cmd.payCounts))
                return CommandResult.Fail("素材が足りません", "Not enough materials");
            for (int i = 0; i < cmd.payItemIds.Count; i++)
                if (!ctx.HasItem(cmd.payItemIds[i], cmd.payCounts[i])) return CommandResult.Fail("素材が足りません", "Not enough materials");
            if (!ctx.TryCharge(req.Money, "fishing.rod_upgrade")) return CommandResult.Fail("お金が足りません", "Not enough money");
            for (int i = 0; i < cmd.payItemIds.Count; i++)
            {
                if (!ctx.TakeItem(cmd.payItemIds[i], cmd.payCounts[i], "fishing.rod_upgrade"))
                {
                    ctx.PayMoney(req.Money, "fishing.rod_upgrade_refund");
                    return CommandResult.Fail("素材を渡せませんでした", "Could not hand over materials");
                }
            }
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null && !string.IsNullOrEmpty(cmd.residentId) && dir.Get(cmd.residentId) != null)
            {
                dir.GiveMoney(cmd.residentId, Mathf.RoundToInt(req.Money * 0.6f), "fishing.rod_upgrade_fee");
                dir.ChangeFriendship(cmd.residentId, ctx.IssuerId, 2, "fishing.rod_upgrade");
                dir.AddMemory(cmd.residentId, new ResidentMemoryEntry { kind = "upgraded_rod", playerId = ctx.IssuerId, day = CurrentDay, importance = 0.4f });
            }
            if (!ctx.IsLocalIssuer)
                EventBus.Publish(new ToolUpgradedEvent { PlayerId = ctx.IssuerId, Tool = ToolType.FishingRod, NewLevel = cmd.targetTier });
            return CommandResult.Ok();
        }

        private CommandResult HandleLessonLearned(LessonLearnedCommand cmd, CommandContext ctx)
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || string.IsNullOrEmpty(cmd.residentId) || dir.Get(cmd.residentId) == null) return CommandResult.Ok();
            dir.ChangeFriendship(cmd.residentId, ctx.IssuerId, 3, "fishing.lesson");
            dir.AddMemory(cmd.residentId, new ResidentMemoryEntry { kind = "taught_fishing", playerId = ctx.IssuerId, day = CurrentDay, importance = 0.6f, detail = cmd.technique });
            return CommandResult.Ok();
        }

        /// <summary>竿の強化を依頼する（ローカルプレイヤー）。成功したら竿の段階を上げる。</summary>
        public void RequestRodUpgrade(PlayerState player, string residentId, System.Action<bool> done = null)
        {
            int current = Mathf.Max(1, player.Tools.GetLevel(ToolType.FishingRod));
            var req = RodUpgrades.ForTier(current + 1);
            if (req == null) { done?.Invoke(false); return; }
            var plan = RodUpgrades.PlanPayment(req, player.Inventory);
            if (plan == null || !player.Wallet.CanAfford(req.Money))
            {
                UIManager.Toast(Loc.T("素材かお金が足りません", "Not enough materials or money"));
                done?.Invoke(false);
                return;
            }
            var cmd = new UpgradeRodCommand { targetTier = req.TargetTier, residentId = residentId, money = req.Money };
            foreach (var kv in plan)
            {
                cmd.payItemIds.Add(kv.Key);
                cmd.payCounts.Add(kv.Value);
            }
            WorldAuthority.Submit(cmd, r =>
            {
                if (r != null && r.Success)
                {
                    player.Tools.SetLevel(ToolType.FishingRod, req.TargetTier);
                    EventBus.Publish(new ToolUpgradedEvent { PlayerId = player.PlayerId, Tool = ToolType.FishingRod, NewLevel = req.TargetTier });
                    UIManager.Toast(Loc.Format("釣り竿が「{0}」になった！", "Your rod is now \"{0}\"!", ToolTypeUtil.TierLabel(req.TargetTier)), 5f);
                }
                done?.Invoke(r != null && r.Success);
            });
        }

        // =====================================================================
        // セーブ
        // =====================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(planet);
                case SaveScope.PlayerProfile:
                {
                    var save = new FishingProfileSave();
                    save.players.Add(GetProfile(playerId));
                    return JsonUtility.ToJson(save);
                }
                case SaveScope.PlayerPlanetKnowledge:
                    if (LocalPlayer != null && playerId != LocalPlayer.PlayerId) return null;
                    return JsonUtility.ToJson(log);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    var d = JsonUtility.FromJson<FishingPlanetSave>(json);
                    if (d != null) planet = d;
                    break;
                }
                case SaveScope.PlayerProfile:
                {
                    var d = JsonUtility.FromJson<FishingProfileSave>(json);
                    if (d == null || d.players == null) break;
                    foreach (var p in d.players)
                    {
                        if (p == null) continue;
                        if (string.IsNullOrEmpty(p.playerId)) p.playerId = playerId;
                        if (p.techniques == null) p.techniques = new List<string>();
                        if (p.lessonsTried == null) p.lessonsTried = new List<string>();
                        p.level = FishingTuning.LevelForXp(p.xp);
                        profiles[playerId ?? p.playerId] = p;
                    }
                    break;
                }
                case SaveScope.PlayerPlanetKnowledge:
                {
                    var d = JsonUtility.FromJson<FishLogSave>(json);
                    if (d != null)
                    {
                        if (d.entries == null) d.entries = new List<FishLogEntry>();
                        if (d.sightings == null) d.sightings = new List<FishSighting>();
                        log = d;
                    }
                    break;
                }
            }
        }

        // =====================================================================
        // 内部クラス
        // =====================================================================

        private sealed class ToolHandler : IToolHandler
        {
            private readonly FishingSystem sys;
            public ToolHandler(FishingSystem sys) { this.sys = sys; }
            public int Priority => 40;
            public bool TryUseTool(ToolUseContext ctx) => sys.TryStartFishing(ctx);
        }

        private sealed class Overlay : IUIOverlay
        {
            private readonly FishingSystem sys;
            public Overlay(FishingSystem sys) { this.sys = sys; }
            public int OverlayOrder => 200;

            public void DrawOverlay()
            {
                if (sys.session == null || !sys.session.IsActive) return;
                if (Game.Instance == null || Game.Instance.State != GameState.Playing) return;
                sys.session.Draw();
            }
        }
    }
}
