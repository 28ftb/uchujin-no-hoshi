using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Weather
{
    /// <summary>
    /// 星の気候傾向（時間・天候仕様 §13【提案】）。固定の4タイプから選ぶのではなく、
    /// 晴れ・降水・風・霧・特殊現象の発生傾向をシードで組み合わせる。
    /// </summary>
    public sealed class ClimateProfile
    {
        public float SunnyBias;
        public float RainChance;
        public float Windiness;
        public float Fogginess;
        public float PhenomenonRate;
        /// <summary>現象ごとの重み（AlienPhenomenon のビット順：光雨・星風・逆雨・星の光帯・結晶霞・花潮）。</summary>
        public readonly float[] PhenomenonWeights = new float[6];
        public float Warmth;
        /// <summary>空の色味（星ごとの差）。</summary>
        public Color SkyTint;
        /// <summary>この星でいつも吹く風の向き（XZ）。</summary>
        public Vector2 PrevailingWind;
        /// <summary>星の光帯が空を横切る向き。</summary>
        public Vector3 StarBandNormal;
        /// <summary>月の数と色（宇宙らしさ）。</summary>
        public int MoonCount;
        public Color[] MoonColors;
        public Vector3[] MoonDirs;
        public float[] MoonSizes;

        public static readonly AlienPhenomenon[] Kinds =
        {
            AlienPhenomenon.LightRain, AlienPhenomenon.StarWind, AlienPhenomenon.ReverseRain,
            AlienPhenomenon.StarBand, AlienPhenomenon.CrystalHaze, AlienPhenomenon.BloomTide
        };

        public static ClimateProfile Create(SeededRandom r)
        {
            var c = new ClimateProfile
            {
                SunnyBias = r.Range(0.2f, 0.85f),
                RainChance = r.Range(0.18f, 0.5f),
                Windiness = r.Range(0.1f, 0.95f),
                Fogginess = r.Range(0.1f, 0.8f),
                PhenomenonRate = r.Range(0.22f, 0.42f),
                Warmth = r.Range(-0.35f, 0.35f)
            };
            for (int i = 0; i < c.PhenomenonWeights.Length; i++) c.PhenomenonWeights[i] = r.Range(0.6f, 1.2f);
            // この星らしい現象（1〜2種類を多めに）
            int sig = r.Range(0, 6);
            c.PhenomenonWeights[sig] *= 3f;
            if (r.Chance(0.5f)) c.PhenomenonWeights[r.Range(0, 6)] *= 2f;
            // 雨の多い星は光雨、晴れの多い星は星風が少し多い（時間・天候仕様 §13 の例）
            c.PhenomenonWeights[0] *= 0.7f + c.RainChance;
            c.PhenomenonWeights[1] *= 0.7f + c.SunnyBias * 0.6f;
            c.SkyTint = Color.HSVToRGB(r.Range(0.5f, 0.72f), r.Range(0.25f, 0.5f), 1f);
            float wa = r.Range(0f, Mathf.PI * 2f);
            c.PrevailingWind = new Vector2(Mathf.Cos(wa), Mathf.Sin(wa));
            c.StarBandNormal = Quaternion.Euler(r.Range(-40f, 40f), r.Range(0f, 360f), 0f) * Vector3.forward;
            c.MoonCount = r.Range(1, 3);
            c.MoonColors = new Color[c.MoonCount];
            c.MoonDirs = new Vector3[c.MoonCount];
            c.MoonSizes = new float[c.MoonCount];
            for (int i = 0; i < c.MoonCount; i++)
            {
                c.MoonColors[i] = Color.HSVToRGB(r.Range(0f, 1f), r.Range(0.05f, 0.35f), 1f);
                c.MoonDirs[i] = (Quaternion.Euler(-r.Range(25f, 65f), r.Range(0f, 360f), 0f) * Vector3.forward).normalized;
                c.MoonSizes[i] = r.Range(14f, 34f);
            }
            return c;
        }

        public float WeightOf(AlienPhenomenon p)
        {
            for (int i = 0; i < Kinds.Length; i++) if (Kinds[i] == p) return PhenomenonWeights[i];
            return 0f;
        }

        public string DescribeJa()
        {
            string s = SunnyBias > 0.6f ? "晴れが多い" : RainChance > 0.38f ? "雨が多い" : "移り変わりやすい";
            if (Windiness > 0.7f) s += "・風が強い";
            if (Fogginess > 0.6f) s += "・霧が出やすい";
            return s;
        }
    }
}
