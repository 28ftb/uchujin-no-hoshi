using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>時間帯（時間・天候仕様 §2.3 の仮区分）。</summary>
    public enum DayPart
    {
        LateNight,    // 0:00-5:00 深夜
        EarlyMorning, // 5:00-8:00 早朝
        Morning,      // 8:00-12:00 午前
        Afternoon,    // 12:00-17:00 午後
        Evening,      // 17:00-20:00 夕方
        Night         // 20:00-24:00 夜
    }

    public static class DayPartUtil
    {
        public static DayPart FromHour(int hour)
        {
            if (hour < 5) return DayPart.LateNight;
            if (hour < 8) return DayPart.EarlyMorning;
            if (hour < 12) return DayPart.Morning;
            if (hour < 17) return DayPart.Afternoon;
            if (hour < 20) return DayPart.Evening;
            return DayPart.Night;
        }

        public static string Label(DayPart p)
        {
            switch (p)
            {
                case DayPart.LateNight: return Loc.T("深夜", "Late night");
                case DayPart.EarlyMorning: return Loc.T("早朝", "Early morning");
                case DayPart.Morning: return Loc.T("午前", "Morning");
                case DayPart.Afternoon: return Loc.T("午後", "Afternoon");
                case DayPart.Evening: return Loc.T("夕方", "Evening");
                default: return Loc.T("夜", "Night");
            }
        }

        public static bool IsDark(int hour) => hour < 5 || hour >= 19;
    }

    /// <summary>
    /// ゲーム内時間。仕様：
    ///  ・現実約30分＝ゲーム内1日（設定で速度変更可能）【仮数値】
    ///  ・日付切替は0:00。眠らなくても日付は進む。スタミナは無い（基本決定 v0.2）。
    ///  ・睡眠は「翌朝まで時間を送る」だけ。強制帰宅・気絶は無い。
    ///  ・ソロではタブレット等で時間停止できる。マルチでは個人の操作で全員の時間を止めない。
    ///  ・四季は無い。週（星ごとに長さが異なる）と経過日数で暦を表す。
    /// </summary>
    public sealed class TimeSystem : GameSystemBase
    {
        public const int MinutesPerDay = 24 * 60;
        public const int DefaultStartHour = 8;

        public override string SaveKey => "core.time";
        public override int DailyUpdateOrder => 0;

        /// <summary>1日目0:00からの経過分。</summary>
        public long TotalMinutes { get; private set; }
        /// <summary>現実の秒数／ゲーム内1日（基準1800秒＝30分）。</summary>
        public float RealSecondsPerGameDay { get; set; } = 1800f;
        /// <summary>プレイヤー設定の時間速度倍率（0.5〜3）。</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>星ごとの1週間の日数（シードで5〜8日）。</summary>
        public int WeekLength { get; private set; } = 7;

        public int Day => (int)(TotalMinutes / MinutesPerDay) + 1;
        public int MinuteOfDay => (int)(TotalMinutes % MinutesPerDay);
        public int Hour => MinuteOfDay / 60;
        public int Minute => MinuteOfDay % 60;
        public float HourFloat => MinuteOfDay / 60f + minuteAccumulator / 60f;
        public DayPart CurrentDayPart => DayPartUtil.FromHour(Hour);
        public int DayOfWeek => (Day - 1) % Mathf.Max(1, WeekLength);
        /// <summary>週の最終日を「休みの日」とする（住民の仕事予定などに使う）。</summary>
        public bool IsRestDay => DayOfWeek == WeekLength - 1;
        public bool IsNight => DayPartUtil.IsDark(Hour);

        private float minuteAccumulator;
        private readonly HashSet<string> personalPauseTokens = new HashSet<string>();
        private readonly HashSet<string> globalPauseTokens = new HashSet<string>();

        /// <summary>現在時間が止まっているか。</summary>
        public bool IsPaused
        {
            get
            {
                if (globalPauseTokens.Count > 0) return true;
                // マルチプレイ中は個人の一時停止（タブレット等）では時間を止めない（時間・天候仕様 §3.2, §16）
                return !WorldAuthority.IsMultiplayer && personalPauseTokens.Count > 0;
            }
        }

        public bool PauseInSoloWhenTabletOpen { get; set; } = true;

        public override void PreparePlanet(PlanetContext ctx)
        {
            WeekLength = ctx.Rng("calendar.week").Range(5, 9);
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            TotalMinutes = DefaultStartHour * 60;
            minuteAccumulator = 0f;
        }

        public override void Teardown()
        {
            personalPauseTokens.Clear();
            globalPauseTokens.Clear();
            minuteAccumulator = 0f;
        }

        /// <summary>個人用の一時停止（ソロのみ有効）。タブレット・会話の読み込み中など。</summary>
        public void AddPersonalPause(string token) => personalPauseTokens.Add(token);
        public void RemovePersonalPause(string token) => personalPauseTokens.Remove(token);
        /// <summary>全員の時間を止める（メニュー、ホストのカットシーン等）。</summary>
        public void AddGlobalPause(string token) => globalPauseTokens.Add(token);
        public void RemoveGlobalPause(string token) => globalPauseTokens.Remove(token);

        public override void Tick(float deltaTime)
        {
            if (IsPaused) return;
            // ゲストはホストのスナップショットで補正されるが、表示の滑らかさのためローカルでも進める。
            float secondsPerMinute = RealSecondsPerGameDay / MinutesPerDay / Mathf.Clamp(SpeedMultiplier, 0.1f, 10f);
            minuteAccumulator += deltaTime / secondsPerMinute;
            int whole = 0;
            while (minuteAccumulator >= 1f && whole < 120)
            {
                minuteAccumulator -= 1f;
                whole++;
            }
            if (whole > 0) AdvanceMinutes(whole);
        }

        /// <summary>
        /// 時間を進める。日付・時刻の境界ごとにイベントを発行し、各システムへ通知する。
        /// 睡眠の時間送りもこれを使うため、0:00 を跨げば必ず日次更新が走る。
        /// </summary>
        public void AdvanceMinutes(int minutes)
        {
            if (minutes <= 0) return;
            int remaining = minutes;
            while (remaining > 0)
            {
                int toNextHour = 60 - Minute;
                int step = Mathf.Min(remaining, toNextHour);
                int prevHour = Hour;
                int prevDay = Day;
                DayPart prevPart = CurrentDayPart;

                TotalMinutes += step;
                remaining -= step;

                if (Game != null) Game.NotifyMinutesAdvanced(step);
                EventBus.Publish(new MinuteTickEvent { Day = Day, Hour = Hour, Minute = Minute, TotalMinutes = TotalMinutes });

                if (Day != prevDay)
                {
                    if (Game != null) Game.RunDayChange(prevDay, Day);
                }
                if (Hour != prevHour)
                {
                    EventBus.Publish(new HourChangedEvent { Day = Day, Hour = Hour });
                    if (Game != null) Game.NotifyHourChanged(Day, Hour);
                    MarkDirty();
                }
                if (CurrentDayPart != prevPart)
                {
                    EventBus.Publish(new DayPartChangedEvent { Day = Day, Previous = prevPart, Current = CurrentDayPart });
                }
            }
        }

        /// <summary>次に hour 時になるまで時間を進める（睡眠）。ソロ用。マルチではネットワーク側の合意処理から呼ぶ。</summary>
        public void SleepUntilHour(int hour, string playerId)
        {
            hour = Mathf.Clamp(hour, 0, 23);
            int target = hour * 60;
            int diff = target - MinuteOfDay;
            if (diff <= 0) diff += MinutesPerDay;
            EventBus.Publish(new SleepStartedEvent { PlayerId = playerId, TargetHour = hour });
            AdvanceMinutes(diff);
            minuteAccumulator = 0f;
            EventBus.Publish(new SleepEndedEvent { PlayerId = playerId });
        }

        /// <summary>ホストからの同期で時刻を合わせる（ゲスト用）。日付が跨ぐ場合は通常どおりイベントを発行する。</summary>
        public void SyncTo(long totalMinutes)
        {
            long diff = totalMinutes - TotalMinutes;
            if (diff > 0 && diff < MinutesPerDay * 2) AdvanceMinutes((int)diff);
            else TotalMinutes = totalMinutes;
        }

        public string FormatClock() => $"{Hour:00}:{Minute:00}";

        public string FormatDate() => Loc.Format("{0}日目", "Day {0}", Day);

        /// <summary>経過日数＋週内の日（宇宙語の曜日名はLanguage側で生成）。</summary>
        public string FormatDateLong()
        {
            return Loc.Format("{0}日目（週の{1}日目/{2}）", "Day {0} (weekday {1}/{2})", Day, DayOfWeek + 1, WeekLength);
        }

        public static long ToTotalMinutes(int day, int hour, int minute) => (long)(day - 1) * MinutesPerDay + hour * 60 + minute;

        [Serializable]
        private class SaveData
        {
            public long totalMinutes;
            public float realSecondsPerGameDay;
            public float speedMultiplier;
            public int weekLength;
        }

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope != SaveScope.Planet) return null;
            return JsonUtility.ToJson(new SaveData
            {
                totalMinutes = TotalMinutes,
                realSecondsPerGameDay = RealSecondsPerGameDay,
                speedMultiplier = SpeedMultiplier,
                weekLength = WeekLength
            });
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (scope != SaveScope.Planet || string.IsNullOrEmpty(json)) return;
            var d = JsonUtility.FromJson<SaveData>(json);
            if (d == null) return;
            if (Game != null && Game.State == GameState.Playing && !WorldAuthority.IsAuthority)
            {
                SyncTo(d.totalMinutes);
            }
            else
            {
                TotalMinutes = d.totalMinutes;
            }
            if (d.realSecondsPerGameDay > 1f) RealSecondsPerGameDay = d.realSecondsPerGameDay;
            if (d.speedMultiplier > 0f) SpeedMultiplier = d.speedMultiplier;
            if (d.weekLength > 0) WeekLength = d.weekLength;
        }
    }
}
