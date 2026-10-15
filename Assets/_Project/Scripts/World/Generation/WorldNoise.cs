using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// シードから決定的に作る値ノイズ（Mathf.PerlinNoise は実装依存の可能性を避けて使わない）。
    /// 格子点の値を StableHash で求め、滑らかに補間する。
    /// </summary>
    public sealed class WorldNoise
    {
        private readonly ulong seed;

        public WorldNoise(ulong seed, string key)
        {
            this.seed = StableHash.Derive(seed, key);
        }

        private float Lattice(int x, int y)
        {
            unchecked
            {
                ulong h = seed ^ ((ulong)(uint)x * 0x9E3779B97F4A7C15UL) ^ ((ulong)(uint)y * 0xC2B2AE3D27D4EB4FUL);
                h = StableHash.Mix(h);
                return (h >> 40) * (1f / 16777216f);
            }
        }

        /// <summary>0〜1 の値ノイズ。</summary>
        public float Value(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0), b = Lattice(x0 + 1, y0);
            float c = Lattice(x0, y0 + 1), d = Lattice(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        /// <summary>-1〜1 程度のフラクタルノイズ。</summary>
        public float Fbm(float x, float y, int octaves = 4, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += (Value(x, y) * 2f - 1f) * amp;
                norm += amp;
                amp *= gain;
                x = x * lacunarity + 17.31f;
                y = y * lacunarity + 41.77f;
            }
            return sum / Mathf.Max(0.0001f, norm);
        }

        /// <summary>0〜1 の尾根状ノイズ（山肌）。</summary>
        public float Ridged(float x, float y, int octaves = 4)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Value(x, y) * 2f - 1f);
                sum += n * n * amp;
                norm += amp;
                amp *= 0.5f;
                x = x * 2.03f + 9.1f;
                y = y * 2.03f + 3.7f;
            }
            return sum / Mathf.Max(0.0001f, norm);
        }

        /// <summary>1次元ノイズ（海岸線のゆらぎなど）。-1〜1。</summary>
        public float Line(float t, int octaves = 3) => Fbm(t, 0.5f, octaves);
    }

    /// <summary>生成用の幾何ヘルパー。</summary>
    public static class GeoUtil
    {
        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static Vector2 XZ(Vector3 v) => new Vector2(v.x, v.z);
        public static Vector3 X0Z(Vector2 v, float y = 0f) => new Vector3(v.x, y, v.y);

        /// <summary>点 p と線分 ab の距離（XZ）と、線分上の位置 t(0〜1)。</summary>
        public static float DistToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            var ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f)
            {
                t = 0f;
                return (p - a).magnitude;
            }
            t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>前方（+Z が扉側）が target を向く Y 回転（度）。</summary>
        public static float FacingYaw(Vector3 from, Vector3 target)
        {
            var d = target - from;
            d.y = 0;
            if (d.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        public static Vector3 Forward(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        public static Vector3 Right(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, -Mathf.Sin(r));
        }

        /// <summary>方角（ワールド +Z を北とする）の日本語・英語。</summary>
        public static void DirectionName(Vector3 from, Vector3 to, out string ja, out string en)
        {
            var d = to - from;
            float ang = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; // 0=北, 90=東
            if (ang < 0) ang += 360f;
            int idx = Mathf.RoundToInt(ang / 45f) % 8;
            string[] jaNames = { "北", "北東", "東", "南東", "南", "南西", "西", "北西" };
            string[] enNames = { "North", "Northeast", "East", "Southeast", "South", "Southwest", "West", "Northwest" };
            ja = jaNames[idx];
            en = enNames[idx];
        }

        public static bool InsideWorld(Vector2 p, float margin)
        {
            float h = WorldTuning.Half - margin;
            return p.x > -h && p.x < h && p.y > -h && p.y < h;
        }
    }
}
