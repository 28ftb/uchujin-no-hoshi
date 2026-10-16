using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Weather
{
    /// <summary>1日の中の天候の区間（startMinute から次の区間まで）。</summary>
    [Serializable]
    public class WeatherSegment
    {
        public int startMinute;
        public WeatherState state;
    }

    [Serializable]
    public class DayPlan
    {
        public int day;
        public List<WeatherSegment> segments = new List<WeatherSegment>();
    }

    /// <summary>天候の予定（星の状態として保存。ホストが正本）。</summary>
    [Serializable]
    public class PlannerState
    {
        public List<DayPlan> days = new List<DayPlan>();
        /// <summary>現象ごとに最後に起きた日（ClimateProfile.Kinds の順）。</summary>
        public List<int> lastPhenomenonDay = new List<int>();
        /// <summary>天候の条件ごとに最後に起きた日（WeatherPlanner.Conditions の順。最低頻度の保証用）。</summary>
        public List<int> lastConditionDay = new List<int>();
        public int generatedThrough;
    }

    /// <summary>
    /// 天候の予定を作る（時間・天候仕様 §5：一日の途中で変わる天候、複数要素の組み合わせ）。
    /// 日ごとに独立した乱数（"weather.day.N"）を使い、前日の最後の天気と現象の間隔から順に決める。
    /// 数日先まで生成して保存するので、住民の天気読みは「生成済みの予定」を予報に使える。
    /// </summary>
    public sealed class WeatherPlanner
    {
        private readonly ulong seed;
        private readonly ClimateProfile climate;
        public PlannerState State = new PlannerState();

        public WeatherPlanner(ulong seed, ClimateProfile climate)
        {
            this.seed = seed;
            this.climate = climate;
            EnsureCounters();
        }

        private void EnsureCounters()
        {
            if (State.lastPhenomenonDay == null) State.lastPhenomenonDay = new List<int>();
            while (State.lastPhenomenonDay.Count < ClimateProfile.Kinds.Length) State.lastPhenomenonDay.Add(0);
            if (State.lastConditionDay == null) State.lastConditionDay = new List<int>();
            while (State.lastConditionDay.Count < ConditionCount) State.lastConditionDay.Add(0);
            if (State.days == null) State.days = new List<DayPlan>();
        }

        public void Reset()
        {
            State = new PlannerState();
            EnsureCounters();
        }

        public void Load(PlannerState s)
        {
            State = s ?? new PlannerState();
            EnsureCounters();
            State.days.Sort((a, b) => a.day.CompareTo(b.day));
            if (State.days.Count > 0) State.generatedThrough = Mathf.Max(State.generatedThrough, State.days[State.days.Count - 1].day);
        }

        public DayPlan Find(int day)
        {
            foreach (var d in State.days) if (d.day == day) return d;
            return null;
        }

        /// <summary>day まで順に生成する（ホストのみ呼ぶ）。</summary>
        public void EnsureThrough(int day)
        {
            EnsureCounters();
            int start = State.generatedThrough + 1;
            if (day < start) return;
            day = Mathf.Min(day, start + WeatherTuning.MaxExtendDays);
            for (int d = start; d <= day; d++)
            {
                var prev = Find(d - 1);
                WeatherState prevState = prev != null && prev.segments.Count > 0 ? prev.segments[prev.segments.Count - 1].state : WeatherState.Fine;
                State.days.Add(GenerateDay(d, prevState));
                State.generatedThrough = d;
            }
        }

        public void Prune(int currentDay)
        {
            State.days.RemoveAll(d => d.day < currentDay - WeatherTuning.DaysKeptBehind);
        }

        public static WeatherState StateAt(DayPlan plan, int minute)
        {
            if (plan == null || plan.segments.Count == 0) return WeatherState.Fine;
            var s = plan.segments[0].state;
            foreach (var seg in plan.segments)
            {
                if (seg.startMinute <= minute) s = seg.state;
                else break;
            }
            return s;
        }

        /// <summary>予定が無い日の目安（ゲストが先の日を問われた時など）。決定的。</summary>
        public WeatherState Typical(int day, int minute)
        {
            var r = SeededRandom.For(seed, "weather.typical." + day + "." + (minute / 180));
            return Evolve(WeatherState.Fine, r, minute, day);
        }

        // =================================================================
        // 1日分の生成
        // =================================================================

        private DayPlan GenerateDay(int day, WeatherState prev)
        {
            var r = SeededRandom.For(seed, "weather.day." + day);
            var plan = new DayPlan { day = day };
            var starts = new List<int> { 0 };
            foreach (var w in WeatherTuning.SegmentWindows)
                if (r.Chance(WeatherTuning.SegmentWindowChance)) starts.Add(Mathf.RoundToInt(r.Range(w.x, w.y) / 30f) * 30);
            var cur = prev;
            cur.phenomena = AlienPhenomenon.None;
            for (int i = 0; i < starts.Count; i++)
            {
                // 0:00 の区間は前日の夜の続き（急に変わりすぎない）
                cur = i == 0 && r.Chance(0.6f) ? Recalc(cur, starts[i], day, r) : Evolve(cur, r, starts[i], day);
                if (day == 1 && starts[i] < WeatherTuning.CalmUntilMinuteOnDay1)
                {
                    cur.sky = SkyState.Sunny;
                    cur.precipitation = Precipitation.None;
                    cur.visibility = VisibilityLevel.Normal;
                    if (cur.wind == WindLevel.Strong) cur.wind = WindLevel.Breeze;
                }
                plan.segments.Add(new WeatherSegment { startMinute = starts[i], state = cur });
            }
            AddPhenomena(plan, day, r);
            GuaranteeConditions(plan, day);
            Merge(plan);
            return plan;
        }

        // =================================================================
        // 天候の最低頻度（マップ設計 v0.2 §8.5）
        // 雨の魚・霧の魚・強風の魚・無風の伝説魚・荒天の伝説魚・晴れた夜の生き物などが、星の気候のせいで会えなくならないよう、
        // 条件ごとに間隔の上限を決め、超えそうな日は決まった時間帯にその天気を入れる（宇宙固有現象の最大間隔と同じ考え方）。
        // =================================================================

        private const int CondRain = 0, CondClearDay = 1, CondStrongWind = 2, CondFog = 3, CondCalm = 4, CondStorm = 5, CondClearNight = 6;
        private const int ConditionCount = 7;

        private void GuaranteeConditions(DayPlan plan, int day)
        {
            EnsureCounters();
            if (day <= 1) return; // 1日目（不時着の日）は穏やかなまま
            var gaps = WeatherTuning.ConditionMaxGap;
            bool[] force = new bool[ConditionCount];
            for (int c = 0; c < ConditionCount; c++)
            {
                if (Occurs(plan, c)) continue;
                int gap = day - State.lastConditionDay[c];
                if (gap >= gaps[Mathf.Min(c, gaps.Length - 1)]) force[c] = true;
            }
            if (force[CondStorm]) force[CondRain] = false; // 荒天は雨でもある
            if (force[CondFog])
                SetRange(plan, 5 * 60, 8 * 60, s =>
                {
                    s.visibility = VisibilityLevel.LightFog;
                    s.wind = WindLevel.Calm;
                    if (s.precipitation > Precipitation.Drizzle) s.precipitation = Precipitation.Drizzle;
                    return s;
                });
            if (force[CondCalm])
                SetRange(plan, force[CondFog] ? 5 * 60 : 18 * 60, force[CondFog] ? 8 * 60 : 20 * 60, s =>
                {
                    s.wind = WindLevel.Calm;
                    if (s.precipitation > Precipitation.Drizzle) s.precipitation = Precipitation.None;
                    return s;
                });
            if (force[CondClearDay])
                SetRange(plan, 9 * 60, 13 * 60, s =>
                {
                    s.sky = SkyState.Sunny;
                    s.precipitation = Precipitation.None;
                    if (s.visibility == VisibilityLevel.DenseFog) s.visibility = VisibilityLevel.Normal;
                    return s;
                });
            if (force[CondStrongWind])
                SetRange(plan, 11 * 60, 14 * 60, s =>
                {
                    s.wind = WindLevel.Strong;
                    s.visibility = VisibilityLevel.Normal;
                    return s;
                });
            if (force[CondRain])
                SetRange(plan, 14 * 60, 17 * 60, s =>
                {
                    if (s.sky < SkyState.Cloudy) s.sky = SkyState.Overcast;
                    s.precipitation = Precipitation.Rain;
                    return s;
                });
            if (force[CondStorm])
                SetRange(plan, 15 * 60, 17 * 60 + 30, s =>
                {
                    s.sky = SkyState.Overcast;
                    s.precipitation = Precipitation.HeavyRain;
                    s.wind = WindLevel.Strong;
                    s.visibility = VisibilityLevel.Normal;
                    return s;
                });
            if (force[CondClearNight])
                SetRange(plan, 21 * 60, 1440, s =>
                {
                    if (s.sky > SkyState.Sunny) s.sky = SkyState.Clear;
                    s.precipitation = Precipitation.None;
                    s.visibility = VisibilityLevel.Normal;
                    return s;
                });
            for (int c = 0; c < ConditionCount; c++)
                if (Occurs(plan, c)) State.lastConditionDay[c] = day;
        }

        /// <summary>
        /// その日に条件が「1時間以上続けて」成り立つか（30分だけの荒天などは数えない。遊ぶ側が出会える長さにする）。
        /// 晴れ（昼）は 7〜17時、晴れた夜は 21〜24時と 0〜4時の中で数える。
        /// </summary>
        private static bool Occurs(DayPlan plan, int cond)
        {
            int run = 0;
            for (int m = 0; m < 1440; m += 30)
            {
                var s = StateAt(plan, m);
                bool ok;
                switch (cond)
                {
                    case CondRain: ok = s.IsRaining; break;
                    case CondClearDay: ok = m >= 7 * 60 && m < 17 * 60 && s.sky <= SkyState.Sunny && !s.IsRaining; break;
                    case CondStrongWind: ok = s.wind == WindLevel.Strong; break;
                    case CondFog: ok = s.IsFoggy; break;
                    case CondCalm: ok = s.wind == WindLevel.Calm && s.precipitation <= Precipitation.Drizzle; break;
                    case CondStorm: ok = s.wind == WindLevel.Strong && s.precipitation >= Precipitation.Rain; break;
                    default: ok = (m >= 21 * 60 || m < 4 * 60) && ((s.sky <= SkyState.Sunny && !s.IsRaining) || s.Has(AlienPhenomenon.StarBand)); break;
                }
                run = ok ? run + 30 : 0;
                if (run >= 60) return true;
            }
            return false;
        }

        private WeatherState Recalc(WeatherState s, int minute, int day, SeededRandom r)
        {
            s.phenomena = AlienPhenomenon.None;
            s.warmth = WarmthOf(s, minute, r);
            return s;
        }

        private float WarmthOf(WeatherState s, int minute, SeededRandom r)
        {
            float h = minute / 60f;
            bool night = h < 5f || h >= 20f;
            float w = climate.Warmth + (s.sky <= SkyState.Sunny ? 0.2f : -0.1f) + (night ? -0.25f : 0f) + (s.IsRaining ? -0.15f : 0f) + r.Range(-0.1f, 0.1f);
            return Mathf.Clamp(w, -1f, 1f);
        }

        private WeatherState Evolve(WeatherState prev, SeededRandom r, int minute, int day)
        {
            var s = prev;
            s.phenomena = AlienPhenomenon.None;
            // 空模様
            var weights = new float[]
            {
                climate.SunnyBias * 0.45f + 0.08f,
                0.34f,
                (1f - climate.SunnyBias) * 0.35f + 0.1f,
                climate.RainChance * 0.55f
            };
            int target = r.WeightedIndex(weights);
            if (!r.Chance(WeatherTuning.SkyPersistence))
            {
                int cur = (int)s.sky;
                if (r.Chance(0.3f)) cur = target;
                else cur += Math.Sign(target - cur);
                s.sky = (SkyState)Mathf.Clamp(cur, 0, 3);
            }
            // 降水
            if (s.sky >= SkyState.Cloudy)
            {
                float chance = climate.RainChance * (s.sky == SkyState.Overcast ? 1.3f : 0.45f);
                if (prev.IsRaining) chance += 0.25f;
                if (r.Chance(chance))
                {
                    float roll = r.NextFloat();
                    if (roll < 0.45f) s.precipitation = Precipitation.Drizzle;
                    else if (roll < 0.85f || s.sky != SkyState.Overcast) s.precipitation = Precipitation.Rain;
                    else s.precipitation = Precipitation.HeavyRain;
                }
                else s.precipitation = Precipitation.None;
            }
            else s.precipitation = Precipitation.None;
            // 風
            float w = r.NextFloat() * (0.55f + climate.Windiness);
            s.wind = w < 0.45f ? WindLevel.Calm : w < 1.0f ? WindLevel.Breeze : WindLevel.Strong;
            // 霧（森の朝霧などの局所差は GetAt で加える）
            float h = minute / 60f;
            bool morning = h >= 3.5f && h < 9.5f;
            float fog = climate.Fogginess * (morning ? 0.85f : 0.22f) * (s.IsRaining ? 1.2f : 1f);
            if (s.wind == WindLevel.Strong) fog = 0f;
            s.visibility = VisibilityLevel.Normal;
            if (r.Chance(fog)) s.visibility = r.Chance(0.3f) ? VisibilityLevel.DenseFog : VisibilityLevel.LightFog;
            s.warmth = WarmthOf(s, minute, r);
            return s;
        }

        // =================================================================
        // 宇宙固有の現象（時間・天候仕様 §12）
        // =================================================================

        private void AddPhenomena(DayPlan plan, int day, SeededRandom r)
        {
            if (day <= 1) return;
            var kinds = ClimateProfile.Kinds;
            // 最も間隔の空いた現象が上限を超えていれば必ず起こす（季節待ちで止まらない）
            int forced = -1;
            int worstGap = 0;
            for (int i = 0; i < kinds.Length; i++)
            {
                int gap = day - State.lastPhenomenonDay[i];
                if (gap > WeatherTuning.PhenomenonMaxGap && gap > worstGap)
                {
                    worstGap = gap;
                    forced = i;
                }
            }
            var chosen = new List<int>();
            if (forced >= 0) chosen.Add(forced);
            if (r.Chance(climate.PhenomenonRate))
            {
                int k = r.WeightedIndex(climate.PhenomenonWeights);
                if (k >= 0 && !chosen.Contains(k)) chosen.Add(k);
            }
            foreach (var k in chosen)
            {
                ApplyPhenomenon(plan, kinds[k], r);
                State.lastPhenomenonDay[k] = day;
            }
        }

        private void ApplyPhenomenon(DayPlan plan, AlienPhenomenon p, SeededRandom r)
        {
            int start, end;
            switch (p)
            {
                case AlienPhenomenon.LightRain:
                    start = Round30(r.Range(17 * 60, 21 * 60));
                    end = Mathf.Min(1440, start + Round30(r.Range(120, 210)));
                    SetRange(plan, start, end, s =>
                    {
                        if (s.sky < SkyState.Cloudy) s.sky = SkyState.Cloudy;
                        s.precipitation = Precipitation.None;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
                case AlienPhenomenon.StarWind:
                    start = Round30(r.Range(12 * 60, 16 * 60));
                    end = Mathf.Min(1440, start + Round30(r.Range(120, 240)));
                    bool strong = r.Chance(0.4f);
                    SetRange(plan, start, end, s =>
                    {
                        s.wind = strong ? WindLevel.Strong : WindLevel.Breeze;
                        s.visibility = VisibilityLevel.Normal;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
                case AlienPhenomenon.ReverseRain:
                    start = Round30(r.Range(6 * 60, 9 * 60));
                    end = Mathf.Min(1440, start + Round30(r.Range(60, 150)));
                    // 少し前に小雨（地面の水滴が空へ昇る）
                    SetRange(plan, Mathf.Max(0, start - 120), start, s =>
                    {
                        if (s.sky < SkyState.Cloudy) s.sky = SkyState.Cloudy;
                        if (!s.IsRaining) s.precipitation = Precipitation.Drizzle;
                        return s;
                    });
                    SetRange(plan, start, end, s =>
                    {
                        s.sky = SkyState.Cloudy;
                        s.precipitation = Precipitation.None;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
                case AlienPhenomenon.StarBand:
                    start = Round30(r.Range(20 * 60, 21 * 60 + 30));
                    end = 1440;
                    SetRange(plan, start, end, s =>
                    {
                        s.sky = SkyState.Clear;
                        s.precipitation = Precipitation.None;
                        s.visibility = VisibilityLevel.Normal;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
                case AlienPhenomenon.CrystalHaze:
                    start = Round30(r.Range(4 * 60, 6 * 60 + 30));
                    end = Mathf.Min(1440, start + Round30(r.Range(120, 200)));
                    SetRange(plan, start, end, s =>
                    {
                        s.visibility = VisibilityLevel.LightFog;
                        s.wind = WindLevel.Calm;
                        s.precipitation = Precipitation.None;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
                case AlienPhenomenon.BloomTide:
                    start = Round30(r.Range(8 * 60, 11 * 60));
                    end = Mathf.Min(1440, start + Round30(r.Range(120, 240)));
                    SetRange(plan, start, end, s =>
                    {
                        if (s.sky > SkyState.Sunny) s.sky = SkyState.Sunny;
                        s.precipitation = Precipitation.None;
                        s.wind = WindLevel.Breeze;
                        s.phenomena |= p;
                        return s;
                    });
                    break;
            }
        }

        private static int Round30(float minutes) => Mathf.RoundToInt(minutes / 30f) * 30;

        /// <summary>[a,b) の区間の天気を書き換える（必要なら区間を分割）。</summary>
        private static void SetRange(DayPlan plan, int a, int b, Func<WeatherState, WeatherState> modify)
        {
            if (b <= a) return;
            Split(plan, a);
            if (b < 1440) Split(plan, b);
            for (int i = 0; i < plan.segments.Count; i++)
            {
                var seg = plan.segments[i];
                if (seg.startMinute >= a && seg.startMinute < b) seg.state = modify(seg.state);
            }
        }

        private static void Split(DayPlan plan, int minute)
        {
            int idx = -1;
            for (int i = 0; i < plan.segments.Count; i++)
            {
                if (plan.segments[i].startMinute == minute) return;
                if (plan.segments[i].startMinute < minute) idx = i;
            }
            if (idx < 0) return;
            plan.segments.Insert(idx + 1, new WeatherSegment { startMinute = minute, state = plan.segments[idx].state });
        }

        private static void Merge(DayPlan plan)
        {
            plan.segments.Sort((x, y) => x.startMinute.CompareTo(y.startMinute));
            for (int i = plan.segments.Count - 1; i > 0; i--)
            {
                if (plan.segments[i].startMinute == plan.segments[i - 1].startMinute || Same(plan.segments[i].state, plan.segments[i - 1].state))
                    plan.segments.RemoveAt(i);
            }
        }

        /// <summary>見た目・生活に関わる要素が同じか（気温は除く）。</summary>
        public static bool Same(WeatherState a, WeatherState b)
        {
            return a.sky == b.sky && a.precipitation == b.precipitation && a.wind == b.wind && a.visibility == b.visibility && a.phenomena == b.phenomena;
        }
    }
}
