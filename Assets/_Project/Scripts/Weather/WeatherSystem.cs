using System;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Weather
{
    /// <summary>
    /// 天候・気候・宇宙固有現象・昼夜の光（時間・天候仕様 v0.2）。
    ///  ・気候傾向をシードから生成（§13）。地形による局所差（海岸の風・森の朝霧・山の霧）を GetAt に反映。
    ///  ・一日の途中で変わる天候（§5）。数日先まで決定的に生成して保存（GetPlanned は住民の天気読みが使う）。
    ///  ・四季なし（§6）。光雨・星風・逆雨・星の光帯・結晶霞・花潮（§12）。季節待ちで止まらないよう適度な頻度。
    ///  ・天候が変わったら WeatherChangedEvent。
    ///  ・マルチ：天候はホストの状態（予定）を共有する（ゲストは RestoreState で受け取る：§16）。
    /// </summary>
    public sealed class WeatherSystem : GameSystemBase, IWeatherService
    {
        public override string SaveKey => "weather";
        public override int DailyUpdateOrder => 10;

        public ClimateProfile Climate { get; private set; }
        public WeatherLogData History { get; private set; } = new WeatherLogData();

        private WeatherPlanner planner;
        private WeatherState current = WeatherState.Fine;
        private bool hasCurrent;
        private WeatherVisuals visuals;
        private WeatherLogApp logApp;
        private bool subscribed;
        private bool appRegistered;

        // =================================================================
        // IWeatherService
        // =================================================================

        public WeatherState Current
        {
            get
            {
                if (!hasCurrent) RefreshCurrent(false);
                return current;
            }
        }

        public WeatherState GetPlanned(int day, int hour)
        {
            return PlannedAt(day, Mathf.Clamp(hour, 0, 23) * 60);
        }

        private WeatherState PlannedAt(int day, int minute)
        {
            if (planner == null) return WeatherState.Fine;
            var plan = planner.Find(day);
            if (plan == null && IsAuthority && day > planner.State.generatedThrough && day <= planner.State.generatedThrough + WeatherTuning.MaxExtendDays)
            {
                planner.EnsureThrough(day);
                MarkDirty();
                plan = planner.Find(day);
            }
            return plan != null ? WeatherPlanner.StateAt(plan, minute) : planner.Typical(day, minute);
        }

        /// <summary>場所ごとの局所差（時間・天候仕様 §13「地形との関係」【提案】）。複雑な気象シミュレーターにはしない。</summary>
        public WeatherState GetAt(Vector3 position)
        {
            var s = Current;
            var layout = Game != null ? Game.Layout : null;
            if (layout == null) return s;
            layout = layout.LayoutAt(position); // 村の外の地域・道中マップ（湖の森の湖の霧など）
            var area = layout.GetAreaAt(position);
            // 湖の範囲は、重なる小さなエリア（森など）より湖を優先する（湖の霧・風の弱まり：マップ設計 v0.2 §2.3）
            AreaRegion lake = null;
            foreach (var a in layout.Areas)
                if (a.type == AreaType.Lake && a.Contains(position))
                {
                    lake = a;
                    break;
                }
            if (lake != null) area = lake;
            if (area == null) return s;
            int hour = Clock != null ? Clock.Hour : 12;
            bool morning = hour >= 4 && hour < 9;
            switch (area.type)
            {
                case AreaType.Lake:
                    // 湖：周りより低く囲まれた水面なので、朝や曇りの日に霧が溜まり、風が一段弱まる（無風の日が増える）
                    if ((morning || s.sky >= SkyState.Cloudy) && s.wind != WindLevel.Strong && s.visibility == VisibilityLevel.Normal && Climate.Fogginess > 0.25f)
                        s.visibility = VisibilityLevel.LightFog;
                    if (s.wind == WindLevel.Strong) s.wind = WindLevel.Breeze;
                    else if (s.wind == WindLevel.Breeze && Climate.Windiness < 0.75f) s.wind = WindLevel.Calm;
                    break;
                case AreaType.Coast:
                    // 海岸は風が強い
                    if (s.wind == WindLevel.Calm) s.wind = WindLevel.Breeze;
                    else if (s.wind == WindLevel.Breeze && Climate.Windiness > 0.45f) s.wind = WindLevel.Strong;
                    break;
                case AreaType.Forest:
                    // 森の朝霧
                    if (morning && s.wind != WindLevel.Strong && s.visibility == VisibilityLevel.Normal && Climate.Fogginess > 0.2f) s.visibility = VisibilityLevel.LightFog;
                    if (s.wind == WindLevel.Strong) s.wind = WindLevel.Breeze;
                    break;
                case AreaType.Mountain:
                case AreaType.MineEntrance:
                    // 山の霧
                    if ((s.sky >= SkyState.Cloudy || morning) && s.wind != WindLevel.Strong && s.visibility < VisibilityLevel.DenseFog)
                        s.visibility = (VisibilityLevel)((int)s.visibility + 1);
                    break;
                case AreaType.Pond:
                case AreaType.Riverside:
                    if (morning && s.wind == WindLevel.Calm && s.visibility == VisibilityLevel.Normal && Climate.Fogginess > 0.45f) s.visibility = VisibilityLevel.LightFog;
                    break;
                case AreaType.Hill:
                    // 高台は見晴らしがよく、風が通る
                    if (s.visibility == VisibilityLevel.LightFog && !morning) s.visibility = VisibilityLevel.Normal;
                    if (s.wind == WindLevel.Calm) s.wind = WindLevel.Breeze;
                    break;
            }
            return s;
        }

        /// <summary>太陽光の強さ 0〜1（昼夜・雲）。</summary>
        public float Daylight
        {
            get
            {
                float h = Clock != null ? Clock.HourFloat : 12f;
                return Dayness(h) * CloudFactor(Current);
            }
        }

        /// <summary>昼らしさ 0〜1（日の出・日の入りでなめらかに変わる）。</summary>
        public static float Dayness(float hour)
        {
            float up = Smooth(WeatherTuning.SunriseStart, WeatherTuning.SunriseEnd, hour);
            float down = 1f - Smooth(WeatherTuning.SunsetStart, WeatherTuning.SunsetEnd, hour);
            return Mathf.Clamp01(up * down);
        }

        public static float CloudFactor(WeatherState w)
        {
            float f;
            switch (w.sky)
            {
                case SkyState.Clear: f = 1f; break;
                case SkyState.Sunny: f = 0.95f; break;
                case SkyState.Cloudy: f = 0.78f; break;
                default: f = 0.62f; break;
            }
            switch (w.precipitation)
            {
                case Precipitation.Drizzle: f *= 0.92f; break;
                case Precipitation.Rain: f *= 0.82f; break;
                case Precipitation.HeavyRain: f *= 0.7f; break;
            }
            if (w.visibility == VisibilityLevel.DenseFog) f *= 0.85f;
            return f;
        }

        private static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        // =================================================================
        // ライフサイクル
        // =================================================================

        public override void PreparePlanet(PlanetContext ctx)
        {
            Climate = ClimateProfile.Create(ctx.Rng("weather.climate"));
            planner = new WeatherPlanner(ctx.Seed, Climate);
            hasCurrent = false;
            History = new WeatherLogData();
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            planner.Reset();
            int day = Clock != null ? Clock.Day : 1;
            planner.EnsureThrough(day + WeatherTuning.DaysAhead);
            RefreshCurrent(false);
        }

        public override void BuildScene()
        {
            if (Game.SceneRoot == null) return;
            var go = new GameObject("WeatherVisuals");
            go.transform.SetParent(Game.SceneRoot, false);
            visuals = go.AddComponent<WeatherVisuals>();
            try { visuals.Init(this); }
            catch (Exception e) { Debug.LogError("[Weather] 演出の初期化に失敗: " + e); }
        }

        public override void OnPlayStarted()
        {
            if (!subscribed)
            {
                EventBus.Subscribe<AreaChangedEvent>(OnAreaChanged);
                subscribed = true;
            }
            if (IsAuthority && planner != null)
            {
                int day = Clock != null ? Clock.Day : 1;
                planner.EnsureThrough(day + WeatherTuning.DaysAhead);
            }
            RefreshCurrent(false);
            logApp = new WeatherLogApp(this);
            appRegistered = false;
            RecordExperience(true);
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<AreaChangedEvent>(OnAreaChanged);
                subscribed = false;
            }
            visuals = null;
            logApp = null;
            hasCurrent = false;
        }

        public override void Tick(float deltaTime)
        {
            // タブレットへの登録は全システムの OnPlayStarted が終わった後（最初の Tick）に行う
            if (!appRegistered && logApp != null)
            {
                appRegistered = true;
                Game.Service<ITabletService>()?.RegisterApp(logApp);
            }
        }

        private void OnAreaChanged(AreaChangedEvent e)
        {
            if (LocalPlayer == null || e.PlayerId != LocalPlayer.PlayerId || visuals == null) return;
            visuals.SetArea(Game.Areas.Get(e.ToAreaId));
            RecordExperience(false);
        }

        public override void OnGameMinutesAdvanced(int minutes)
        {
            RefreshCurrent(true);
            RecordExperience(false);
        }

        public override void OnDayStarted(int day)
        {
            if (planner == null) return;
            if (IsAuthority)
            {
                planner.EnsureThrough(day + WeatherTuning.DaysAhead);
                planner.Prune(day);
                MarkDirty();
            }
            History.Prune(day);
            RefreshCurrent(true);
        }

        /// <summary>現在の天気を予定から求め、変わったらイベントを出す。</summary>
        private void RefreshCurrent(bool publish)
        {
            if (planner == null || Clock == null) return;
            var next = PlannedAt(Clock.Day, Clock.MinuteOfDay);
            if (!hasCurrent)
            {
                current = next;
                hasCurrent = true;
                return;
            }
            if (WeatherPlanner.Same(current, next))
            {
                current.warmth = next.warmth;
                return;
            }
            var prev = current;
            current = next;
            if (publish) EventBus.Publish(new WeatherChangedEvent { Previous = prev, Current = next });
        }

        // =================================================================
        // 体験した天気の記録（PlayerPlanetKnowledge）
        // =================================================================

        private void RecordExperience(bool force)
        {
            var p = LocalPlayer;
            if (p == null || Clock == null || Game == null) return;
            var area = Game.Areas.Get(p.CurrentAreaId);
            if (area != null && (!area.ShowWeather || area.IsIndoor || area.IsUnderground)) return;
            if (p.CurrentAreaId != AreaIds.Overworld && area == null) return;
            var s = GetAt(p.Position);
            var last = History.Last;
            int part = (int)Clock.CurrentDayPart;
            if (!force && last != null && last.day == Clock.Day && last.dayPart == part && WeatherPlanner.Same(last.state, s)) return;
            if (last != null && last.day == Clock.Day && last.dayPart == part && WeatherPlanner.Same(last.state, s)) return;
            History.entries.Add(new WeatherLogEntry { day = Clock.Day, minute = Clock.MinuteOfDay, dayPart = part, state = s });
            if (History.entries.Count > 1200) History.entries.RemoveAt(0);
        }

        // =================================================================
        // セーブ
        // =================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope == SaveScope.Planet)
                return planner != null ? JsonUtility.ToJson(planner.State) : null;
            if (scope == SaveScope.PlayerPlanetKnowledge)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                return JsonUtility.ToJson(History);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (scope == SaveScope.Planet)
            {
                PlannerState s = null;
                try { s = JsonUtility.FromJson<PlannerState>(json); }
                catch (Exception e) { Debug.LogWarning("[Weather] 天気の予定を読み込めません: " + e.Message); }
                if (s == null || planner == null) return;
                planner.Load(s);
                // ゲストはプレイ中にも呼ばれる：現在の天気を作り直し、変わっていればイベントを出す
                bool playing = Game != null && Game.State == GameState.Playing;
                RefreshCurrent(playing);
            }
            else if (scope == SaveScope.PlayerPlanetKnowledge)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                WeatherLogData d = null;
                try { d = JsonUtility.FromJson<WeatherLogData>(json); }
                catch (Exception e) { Debug.LogWarning("[Weather] 天気の記録を読み込めません: " + e.Message); }
                if (d == null) return;
                if (d.entries == null) d.entries = new System.Collections.Generic.List<WeatherLogEntry>();
                if (d.notes == null) d.notes = new System.Collections.Generic.List<WeatherDayNote>();
                History = d;
            }
        }
    }
}
