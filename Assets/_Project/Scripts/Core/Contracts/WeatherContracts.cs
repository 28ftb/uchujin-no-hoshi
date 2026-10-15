using System;
using UnityEngine;

namespace Uchujin.Core
{
    // 天候は排他的な1状態ではなく、複数要素の組み合わせ（時間・天候仕様 §5）。四季は無い。

    public enum SkyState { Clear, Sunny, Cloudy, Overcast }
    public enum Precipitation { None, Drizzle, Rain, HeavyRain }
    public enum WindLevel { Calm, Breeze, Strong }
    public enum VisibilityLevel { Normal, LightFog, DenseFog }

    /// <summary>宇宙固有の自然現象（時間・天候仕様 §12。名称・効果は【追加提案】）。</summary>
    [Flags]
    public enum AlienPhenomenon
    {
        None = 0,
        LightRain = 1 << 0,    // 光雨：光る雨粒。畑に水分、発光生物・魚が活発に
        StarWind = 1 << 1,     // 星風：光の粒子を含む風。飛行生物、種や胞子が運ばれる
        ReverseRain = 1 << 2,  // 逆雨：水滴が空へ昇る。水辺の生物の活動が変化
        StarBand = 1 << 3,     // 星の光帯：夜空の巨大な光の帯。住民が高台に集まる
        CrystalHaze = 1 << 4,  // 結晶霞（追加案）：細かな結晶がきらめく霞。鉱物系の生物が地表に出る
        BloomTide = 1 << 5     // 花潮（追加案）：一斉に花粉が光る。受粉・花の生物が活発に
    }

    [Serializable]
    public struct WeatherState
    {
        public SkyState sky;
        public Precipitation precipitation;
        public WindLevel wind;
        public VisibilityLevel visibility;
        public AlienPhenomenon phenomena;
        /// <summary>気温の目安（-1=寒い 0=ふつう 1=暑い）。</summary>
        public float warmth;

        public bool IsRaining => precipitation != Precipitation.None;
        public bool Has(AlienPhenomenon p) => (phenomena & p) != 0;
        /// <summary>畑が自然に潤うか（雨・光雨）。</summary>
        public bool WatersCrops => precipitation >= Precipitation.Rain || Has(AlienPhenomenon.LightRain);
        public bool IsFoggy => visibility != VisibilityLevel.Normal;

        public string Summary()
        {
            string s;
            switch (sky)
            {
                case SkyState.Clear: s = Loc.T("快晴", "Clear"); break;
                case SkyState.Sunny: s = Loc.T("晴れ", "Sunny"); break;
                case SkyState.Cloudy: s = Loc.T("曇り", "Cloudy"); break;
                default: s = Loc.T("厚い雲", "Overcast"); break;
            }
            switch (precipitation)
            {
                case Precipitation.Drizzle: s += Loc.T("・小雨", " / drizzle"); break;
                case Precipitation.Rain: s += Loc.T("・雨", " / rain"); break;
                case Precipitation.HeavyRain: s += Loc.T("・大雨", " / heavy rain"); break;
            }
            if (wind == WindLevel.Strong) s += Loc.T("・強風", " / strong wind");
            else if (wind == WindLevel.Breeze) s += Loc.T("・微風", " / breeze");
            if (visibility == VisibilityLevel.LightFog) s += Loc.T("・薄霧", " / mist");
            else if (visibility == VisibilityLevel.DenseFog) s += Loc.T("・濃霧", " / dense fog");
            if (Has(AlienPhenomenon.LightRain)) s += Loc.T("・光雨", " / light-rain");
            if (Has(AlienPhenomenon.StarWind)) s += Loc.T("・星風", " / star wind");
            if (Has(AlienPhenomenon.ReverseRain)) s += Loc.T("・逆雨", " / reverse rain");
            if (Has(AlienPhenomenon.StarBand)) s += Loc.T("・星の光帯", " / star band");
            if (Has(AlienPhenomenon.CrystalHaze)) s += Loc.T("・結晶霞", " / crystal haze");
            if (Has(AlienPhenomenon.BloomTide)) s += Loc.T("・花潮", " / bloom tide");
            return s;
        }

        /// <summary>天気の主要概念（住民の発話用）。</summary>
        public string MainConcept()
        {
            if (Has(AlienPhenomenon.LightRain)) return Concepts.LIGHT_RAIN;
            if (Has(AlienPhenomenon.ReverseRain)) return Concepts.REVERSE_RAIN;
            if (Has(AlienPhenomenon.StarWind)) return Concepts.STAR_WIND;
            if (Has(AlienPhenomenon.StarBand)) return Concepts.STAR_BAND;
            if (IsRaining) return Concepts.RAIN;
            if (visibility == VisibilityLevel.DenseFog) return Concepts.FOG;
            if (wind == WindLevel.Strong) return Concepts.WIND;
            if (sky >= SkyState.Cloudy) return Concepts.CLOUD;
            return Concepts.SUNNY;
        }

        public static WeatherState Fine => new WeatherState { sky = SkyState.Sunny };
    }

    /// <summary>天候システム（World/Weather モジュール）が実装する。</summary>
    public interface IWeatherService
    {
        WeatherState Current { get; }
        /// <summary>指定日時の天候（予報・生成済みの範囲）。予報の精度は未決定のため、実装は「生成済みの予定」を返す。</summary>
        WeatherState GetPlanned(int day, int hour);
        /// <summary>場所ごとの局所差（海岸の風、森の朝霧など）を反映した天候。</summary>
        WeatherState GetAt(Vector3 position);
        /// <summary>太陽光の強さ 0〜1（昼夜・雲）。</summary>
        float Daylight { get; }
    }
}
