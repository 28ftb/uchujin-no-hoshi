using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// プラットフォーム間で結果が変わらない決定的乱数（SplitMix64）。
    /// ワールドシード・宇宙語・地形など「同じシードで同じ結果」が必要な処理では
    /// UnityEngine.Random / System.Random を使わず、必ずこれを使う。
    /// </summary>
    [Serializable]
    public sealed class SeededRandom
    {
        [SerializeField] private ulong state;

        public SeededRandom(ulong seed)
        {
            state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
        }

        /// <summary>シードと用途キーから独立した乱数列を作る。</summary>
        public static SeededRandom For(ulong seed, string key) => new SeededRandom(StableHash.Derive(seed, key));

        public ulong State { get => state; set => state = value; }

        public ulong NextULong()
        {
            ulong z = state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public uint NextUInt() => (uint)(NextULong() >> 32);

        /// <summary>[0,1) の float。</summary>
        public float NextFloat() => (NextULong() >> 40) * (1f / 16777216f);

        /// <summary>[0,1) の double。</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>[minInclusive, maxExclusive) の整数。max&lt;=min なら min を返す。</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong span = (ulong)((long)maxExclusive - minInclusive);
            return (int)((long)minInclusive + (long)(NextULong() % span));
        }

        /// <summary>[min, max) の float。</summary>
        public float Range(float min, float max) => min + (max - min) * NextFloat();

        public bool Chance(float probability) => NextFloat() < probability;

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) return default;
            return list[Range(0, list.Count)];
        }

        /// <summary>重み付き抽選。全重みが0以下なら -1。</summary>
        public int WeightedIndex(IList<float> weights)
        {
            float total = 0f;
            for (int i = 0; i < weights.Count; i++) if (weights[i] > 0f) total += weights[i];
            if (total <= 0f) return -1;
            float r = NextFloat() * total;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                r -= weights[i];
                if (r < 0f) return i;
            }
            for (int i = weights.Count - 1; i >= 0; i--) if (weights[i] > 0f) return i;
            return -1;
        }

        public T WeightedPick<T>(IList<T> items, Func<T, float> weight)
        {
            if (items == null || items.Count == 0) return default;
            var w = new float[items.Count];
            for (int i = 0; i < items.Count; i++) w[i] = weight(items[i]);
            int idx = WeightedIndex(w);
            return idx < 0 ? default : items[idx];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        public Vector2 InsideUnitCircle()
        {
            float a = NextFloat() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(NextFloat());
            return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
        }

        public Color ColorHSV(float hMin, float hMax, float sMin, float sMax, float vMin, float vMax)
        {
            return Color.HSVToRGB(Range(hMin, hMax), Range(sMin, sMax), Range(vMin, vMax));
        }

        /// <summary>この乱数列から派生した独立の乱数列。</summary>
        public SeededRandom Fork(string key) => new SeededRandom(StableHash.Derive(NextULong(), key));
    }

    /// <summary>実行環境に依存しない安定ハッシュ（string.GetHashCodeは環境で変わるため使わない）。</summary>
    public static class StableHash
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static ulong Fnv1a64(string text)
        {
            ulong h = FnvOffset;
            if (text == null) return h;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                h ^= (byte)(c & 0xFF);
                h *= FnvPrime;
                h ^= (byte)(c >> 8);
                h *= FnvPrime;
            }
            return h;
        }

        public static ulong Mix(ulong z)
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong Derive(ulong seed, string key) => Mix(seed ^ Mix(Fnv1a64(key) + 0x632BE59BD9B4E019UL));

        public static ulong Derive(ulong seed, string key, int index) => Derive(Derive(seed, key), index.ToString());

        /// <summary>0〜1の決定的な値（ノイズ・個体差用）。</summary>
        public static float Unit(ulong seed, string key) => (Derive(seed, key) >> 40) * (1f / 16777216f);

        public static int Int(ulong seed, string key, int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return (int)(minInclusive + (long)(Derive(seed, key) % (ulong)(maxExclusive - minInclusive)));
        }
    }
}
