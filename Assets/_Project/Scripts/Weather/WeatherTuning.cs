using UnityEngine;

namespace Uchujin.Weather
{
    /// <summary>
    /// 天候・昼夜の調整用【仮数値】（時間・天候仕様 §5、§12、§13 は詳細が未決定のため、ここで一括調整する）。
    /// </summary>
    public static class WeatherTuning
    {
        // ---- 予定の生成 ----
        /// <summary>何日先まで天気の予定を作っておくか（住民の天気読みが使う）。</summary>
        public const int DaysAhead = 4;
        /// <summary>過去の予定を何日分残すか（「昨日の天気」を話す住民のため）。</summary>
        public const int DaysKeptBehind = 2;
        /// <summary>生成済みの範囲より先を問われた時、どこまで延長生成するか。</summary>
        public const int MaxExtendDays = 14;
        /// <summary>1日の途中で天気が切り替わり得る時間帯（分）。それぞれ出現確率 SegmentWindowChance。</summary>
        public static readonly Vector2Int[] SegmentWindows =
        {
            new Vector2Int(300, 540), new Vector2Int(600, 840), new Vector2Int(900, 1140), new Vector2Int(1200, 1380)
        };
        public const float SegmentWindowChance = 0.72f;
        /// <summary>前の空模様をそのまま続ける確率。</summary>
        public const float SkyPersistence = 0.45f;

        // ---- 宇宙固有の現象（時間・天候仕様 §12【追加提案】） ----
        /// <summary>現象ごとの最大間隔（日）。季節待ちで進行が止まらないよう、これより空くと必ず起こす。</summary>
        public const int PhenomenonMaxGap = 12;

        // ---- 天候の最低頻度（マップ設計 v0.2 §8.5：天候しだいの魚・生き物に会えなくならないように）----
        /// <summary>雨・晴れ（昼）・強風・霧・無風・荒天・晴れた夜 が、それぞれこの日数より空いたら必ず起こす。</summary>
        public static readonly int[] ConditionMaxGap = { 5, 3, 6, 7, 6, 14, 3 };
        /// <summary>1日目は穏やかな天気（不時着の日）。</summary>
        public const int CalmUntilMinuteOnDay1 = 15 * 60;

        // ---- 昼夜（時間・天候仕様 §2.3） ----
        public const float SunriseStart = 5.0f;
        public const float SunriseEnd = 7.0f;
        public const float SunsetStart = 17.5f;
        public const float SunsetEnd = 19.6f;
        public const float SunMaxElevation = 62f;
        public const float SunIntensity = 1.15f;
        /// <summary>夜の月明かり（夜の暗さはプレイに支障のない程度に）。</summary>
        public const float MoonIntensity = 0.34f;
        public static readonly Color NightAmbient = new Color(0.27f, 0.3f, 0.44f);
        public static readonly Color DayAmbient = new Color(0.62f, 0.63f, 0.66f);
        public static readonly Color NightSky = new Color(0.035f, 0.05f, 0.12f);
        public static readonly Color TwilightSky = new Color(1f, 0.62f, 0.48f);

        // ---- 屋内・地下 ----
        public static readonly Color IndoorAmbient = new Color(0.58f, 0.54f, 0.5f);
        public const float IndoorSunIntensity = 0.35f;
        public static readonly Color UndergroundAmbient = new Color(0.16f, 0.15f, 0.19f);
        /// <summary>地下では太陽光をほぼ消す（鉱山の洞窟には天井がないため）。</summary>
        public const float UndergroundSunIntensity = 0.02f;

        // ---- 演出 ----
        public const float VisualBlendSpeed = 0.5f;
        public const int RainMaxParticles = 4000;
        public static readonly float[] RainRates = { 0f, 260f, 900f, 2200f };
        public const float LightRainRate = 420f;
        public const float ReverseRainRate = 300f;
        public const float StarWindRate = 160f;
        public const float CrystalHazeRate = 140f;
        public const float BloomTideRate = 120f;
        public const int StarCount = 750;
        public const float SkyRadius = 420f;

        // ---- 記録アプリ ----
        public const int LogKeepDays = 40;
    }
}
