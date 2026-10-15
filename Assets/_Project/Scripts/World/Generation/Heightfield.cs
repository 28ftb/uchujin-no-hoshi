using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.World
{
    public enum WaterKind : byte { None = 0, River = 1, Pond = 2, Sea = 3 }

    /// <summary>
    /// 高さグリッド（地形の正本）。WorldLayout.HeightSampler / WaterTester はこれを参照する。
    /// 地形メッシュ・コライダーも同じグリッドから作るので、住民の足元と見た目が一致する。
    /// 橋の上は橋板の高さを返す（住民が橋を渡れるように）。
    /// </summary>
    public sealed class Heightfield
    {
        public readonly int N;
        public readonly float Step;
        public readonly float Min;
        public readonly float[] H;
        /// <summary>水面の高さ（水が無い点は NaN）。</summary>
        public readonly float[] WaterY;
        public readonly WaterKind[] Kind;
        /// <summary>WorldLayout.Waters のインデックス（無ければ -1）。</summary>
        public readonly short[] WaterIndex;
        public readonly List<BridgeInfo> Bridges = new List<BridgeInfo>();

        public Heightfield(float worldSize, float step)
        {
            Step = step;
            N = Mathf.RoundToInt(worldSize / step) + 1;
            Min = -worldSize * 0.5f;
            H = new float[N * N];
            WaterY = new float[N * N];
            Kind = new WaterKind[N * N];
            WaterIndex = new short[N * N];
            for (int i = 0; i < WaterY.Length; i++)
            {
                WaterY[i] = float.NaN;
                WaterIndex[i] = -1;
            }
        }

        public int Index(int ix, int iz) => iz * N + ix;
        public float XOf(int ix) => Min + ix * Step;
        public float ZOf(int iz) => Min + iz * Step;

        /// <summary>範囲外でも端の続きとして扱う幅（m）。これより外は水なし。</summary>
        private const float OutOfBoundsMargin = 16f;

        public void ToGrid(float x, float z, out int ix, out int iz)
        {
            ix = Mathf.Clamp(Mathf.RoundToInt((x - Min) / Step), 0, N - 1);
            iz = Mathf.Clamp(Mathf.RoundToInt((z - Min) / Step), 0, N - 1);
        }

        /// <summary>地形そのものの高さ（双一次補間、橋は含まない）。</summary>
        public float SampleTerrain(float x, float z)
        {
            float gx = Mathf.Clamp((x - Min) / Step, 0f, N - 1.001f);
            float gz = Mathf.Clamp((z - Min) / Step, 0f, N - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int i = z0 * N + x0;
            float a = H[i], b = H[i + 1], c = H[i + N], d = H[i + N + 1];
            return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
        }

        /// <summary>立てる地面の高さ（橋の上なら橋板）。</summary>
        public float SampleGround(float x, float z)
        {
            float h = SampleTerrain(x, z);
            for (int i = 0; i < Bridges.Count; i++)
            {
                var br = Bridges[i];
                if (br.Contains(x, z, out float deck) && deck > h) return deck;
            }
            return h;
        }

        public bool OnBridge(float x, float z)
        {
            for (int i = 0; i < Bridges.Count; i++)
                if (Bridges[i].Contains(x, z, out _)) return true;
            return false;
        }

        /// <summary>水面か（地形より水面が高く、橋の上ではない）。</summary>
        public bool IsWater(float x, float z)
        {
            // 地形の範囲から大きく外れた座標（家の中・鉱山など屋外から離れた場所）は水ではない。
            // ToGrid は範囲外を端のマスに丸めるため、端が海だと屋内でも「水」と判定されてしまう。
            float max = Min + (N - 1) * Step;
            if (x < Min - OutOfBoundsMargin || x > max + OutOfBoundsMargin || z < Min - OutOfBoundsMargin || z > max + OutOfBoundsMargin) return false;
            ToGrid(x, z, out int ix, out int iz);
            float w = WaterY[Index(ix, iz)];
            if (float.IsNaN(w)) return false;
            if (w <= SampleTerrain(x, z) + 0.05f) return false;
            return !OnBridge(x, z);
        }

        public WaterKind KindAt(float x, float z)
        {
            ToGrid(x, z, out int ix, out int iz);
            int i = Index(ix, iz);
            if (float.IsNaN(WaterY[i])) return WaterKind.None;
            return Kind[i];
        }

        public float WaterSurfaceAt(float x, float z)
        {
            ToGrid(x, z, out int ix, out int iz);
            return WaterY[Index(ix, iz)];
        }

        public int WaterIndexAt(float x, float z)
        {
            ToGrid(x, z, out int ix, out int iz);
            return WaterIndex[Index(ix, iz)];
        }

        /// <summary>周囲 radius の高低差（平らさの目安）。</summary>
        public float Roughness(float x, float z, float radius)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int k = 0; k < 9; k++)
            {
                float ox = (k % 3 - 1) * radius, oz = (k / 3 - 1) * radius;
                float h = SampleTerrain(x + ox, z + oz);
                if (h < mn) mn = h;
                if (h > mx) mx = h;
            }
            return mx - mn;
        }

        /// <summary>周囲に水があるか（radius 内のグリッド点を調べる）。</summary>
        public bool WaterNear(float x, float z, float radius)
        {
            int r = Mathf.CeilToInt(radius / Step);
            ToGrid(x, z, out int cx, out int cz);
            float r2 = radius * radius;
            for (int dz = -r; dz <= r; dz++)
            {
                int iz = cz + dz;
                if (iz < 0 || iz >= N) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int ix = cx + dx;
                    if (ix < 0 || ix >= N) continue;
                    if ((dx * dx + dz * dz) * Step * Step > r2) continue;
                    if (!float.IsNaN(WaterY[Index(ix, iz)])) return true;
                }
            }
            return false;
        }

        /// <summary>中心差分の法線。</summary>
        public Vector3 Normal(int ix, int iz)
        {
            int xl = Mathf.Max(0, ix - 1), xr = Mathf.Min(N - 1, ix + 1);
            int zl = Mathf.Max(0, iz - 1), zr = Mathf.Min(N - 1, iz + 1);
            float dx = (H[Index(xr, iz)] - H[Index(xl, iz)]) / ((xr - xl) * Step);
            float dz = (H[Index(ix, zr)] - H[Index(ix, zl)]) / ((zr - zl) * Step);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        public float Slope(float x, float z)
        {
            ToGrid(x, z, out int ix, out int iz);
            var n = Normal(ix, iz);
            return Vector3.Angle(n, Vector3.up);
        }
    }

    /// <summary>橋（道路の辺が川を渡る所）。</summary>
    public sealed class BridgeInfo
    {
        public Vector2 A;
        public Vector2 B;
        public float DeckY;
        public float HalfWidth = WorldTuning.BridgeWidth * 0.5f;
        public int EdgeIndex = -1;

        public bool Contains(float x, float z, out float deck)
        {
            deck = DeckY;
            var p = new Vector2(x, z);
            float d = GeoUtil.DistToSegment(p, A, B, out float t);
            return d <= HalfWidth && t > 0.001f && t < 0.999f;
        }

        public Vector3 Mid => new Vector3((A.x + B.x) * 0.5f, DeckY, (A.y + B.y) * 0.5f);
        public float Length => (B - A).magnitude;
    }
}
