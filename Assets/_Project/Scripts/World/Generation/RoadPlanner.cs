using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.World
{
    /// <summary>
    /// 道路の経路探索（4m格子の A*）。村仕様 §3.2：住民は生成された実際の経路で移動する。
    /// 勾配・水・建物を避け、既存の道を再利用しやすくすることで、自然な道路網と合流点ができる。
    /// 川は横断できる（橋になる）が、池と海は通らない。
    /// </summary>
    public sealed class RoadPlanner
    {
        private readonly Heightfield field;
        public readonly int G;
        public readonly float Cell;
        private readonly float min;
        private readonly float[] cost;
        private readonly bool[] blocked;
        private readonly bool[] river;
        private readonly bool[] road;

        // 【仮数値】経路探索のコスト
        private const float SlopeWeight = 45f;
        private const float RiverCrossCost = 28f;
        private const float RoadDiscount = 0.38f;
        private const float EdgeCost = 12f;

        public RoadPlanner(Heightfield field, float cell)
        {
            this.field = field;
            Cell = cell;
            G = Mathf.RoundToInt(WorldTuning.WorldSize / cell);
            min = -WorldTuning.Half;
            cost = new float[G * G];
            blocked = new bool[G * G];
            river = new bool[G * G];
            road = new bool[G * G];
            Refresh(null, null);
        }

        public Vector2 CenterOf(int idx) => new Vector2(min + (idx % G + 0.5f) * Cell, min + (idx / G + 0.5f) * Cell);

        public int IndexOf(Vector2 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - min) / Cell), 0, G - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.y - min) / Cell), 0, G - 1);
            return z * G + x;
        }

        public bool IsRiverCell(int idx) => river[idx];

        /// <summary>地形・水・建物・予約地からコストを作り直す。</summary>
        public void Refresh(List<PadInfo> footprints, System.Func<Vector2, float> extraCost)
        {
            for (int z = 0; z < G; z++)
            {
                for (int x = 0; x < G; x++)
                {
                    int i = z * G + x;
                    var c = CenterOf(i);
                    float c0 = 1f;
                    var kind = field.KindAt(c.x, c.y);
                    bool wet = kind != WaterKind.None && field.WaterSurfaceAt(c.x, c.y) > field.SampleTerrain(c.x, c.y) + 0.05f;
                    river[i] = wet && kind == WaterKind.River;
                    blocked[i] = wet && kind != WaterKind.River;
                    float edgeDist = WorldTuning.Half - Mathf.Max(Mathf.Abs(c.x), Mathf.Abs(c.y));
                    if (edgeDist < 10f) c0 += EdgeCost;
                    if (extraCost != null) c0 += extraCost(c);
                    cost[i] = c0;
                }
            }
            if (footprints != null)
            {
                foreach (var fp in footprints)
                {
                    if (!fp.BlocksRoads) continue;
                    float r = fp.Radius + 2f;
                    int x0 = Mathf.Max(0, Mathf.FloorToInt((fp.Center.x - r - min) / Cell));
                    int x1 = Mathf.Min(G - 1, Mathf.FloorToInt((fp.Center.x + r - min) / Cell));
                    int z0 = Mathf.Max(0, Mathf.FloorToInt((fp.Center.y - r - min) / Cell));
                    int z1 = Mathf.Min(G - 1, Mathf.FloorToInt((fp.Center.y + r - min) / Cell));
                    for (int z = z0; z <= z1; z++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var c = CenterOf(z * G + x);
                            if (fp.SignedDistance(c.x, c.y) < 1.2f) blocked[z * G + x] = true;
                        }
                }
            }
        }

        public void MarkRoad(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude;
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                road[IndexOf(p)] = true;
            }
        }

        public void AddCost(Vector2 center, float radius, float amount)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt((center.x - radius - min) / Cell));
            int x1 = Mathf.Min(G - 1, Mathf.FloorToInt((center.x + radius - min) / Cell));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((center.y - radius - min) / Cell));
            int z1 = Mathf.Min(G - 1, Mathf.FloorToInt((center.y + radius - min) / Cell));
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = z * G + x;
                    if ((CenterOf(i) - center).magnitude <= radius) cost[i] += amount;
                }
        }

        public void AddCostRect(Rect r, float amount)
        {
            for (int i = 0; i < cost.Length; i++)
                if (r.Contains(CenterOf(i))) cost[i] += amount;
        }

        private float H(int i)
        {
            var c = CenterOf(i);
            return field.SampleTerrain(c.x, c.y);
        }

        /// <summary>A*。経路のセル列（始点・終点を含む）。見つからなければ null。</summary>
        public List<int> FindPath(Vector2 from, Vector2 to, float trailDiscount = 1f)
        {
            int s = IndexOf(from), g = IndexOf(to);
            if (s == g) return new List<int> { s };
            var gScore = new Dictionary<int, float>(1024);
            var came = new Dictionary<int, int>(1024);
            var closed = new HashSet<int>();
            var heap = new MinHeap();
            gScore[s] = 0f;
            heap.Push(s, Heuristic(s, g));
            int guard = 0;
            while (heap.Count > 0 && guard++ < 60000)
            {
                int cur = heap.Pop();
                if (cur == g) break;
                if (!closed.Add(cur)) continue;
                int cx = cur % G, cz = cur / G;
                float hc = H(cur);
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= G || nz >= G) continue;
                        int n = nz * G + nx;
                        if (closed.Contains(n)) continue;
                        if (blocked[n] && n != g && n != s) continue;
                        // 斜めに建物の角をすり抜けない
                        if (dx != 0 && dz != 0 && (blocked[cz * G + nx] || blocked[nz * G + cx])) continue;
                        float dist = (dx != 0 && dz != 0 ? 1.4142f : 1f) * Cell;
                        float slope = Mathf.Abs(H(n) - hc) / dist;
                        float step = dist * cost[n] * (1f + slope * slope * SlopeWeight);
                        if (slope > 0.7f) step += 60f;
                        if (river[n]) step += RiverCrossCost;
                        if (road[n]) step *= RoadDiscount * trailDiscount;
                        float tentative = gScore[cur] + step;
                        if (gScore.TryGetValue(n, out var old) && tentative >= old) continue;
                        gScore[n] = tentative;
                        came[n] = cur;
                        heap.Push(n, tentative + Heuristic(n, g));
                    }
                }
            }
            if (!came.ContainsKey(g)) return null;
            var path = new List<int>();
            int c = g;
            path.Add(c);
            int guard2 = 0;
            while (c != s && guard2++ < 100000)
            {
                c = came[c];
                path.Add(c);
            }
            path.Reverse();
            return path;
        }

        private float Heuristic(int a, int b)
        {
            int ax = a % G, az = a / G, bx = b % G, bz = b / G;
            float dx = Mathf.Abs(ax - bx), dz = Mathf.Abs(az - bz);
            return (Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz)) * Cell * RoadDiscount;
        }

        /// <summary>単純な二分ヒープ（優先度付きキュー）。</summary>
        private sealed class MinHeap
        {
            private readonly List<int> items = new List<int>();
            private readonly List<float> keys = new List<float>();
            public int Count => items.Count;

            public void Push(int item, float key)
            {
                items.Add(item);
                keys.Add(key);
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (keys[p] <= keys[i]) break;
                    Swap(i, p);
                    i = p;
                }
            }

            public int Pop()
            {
                int top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                keys[0] = keys[last];
                items.RemoveAt(last);
                keys.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && keys[l] < keys[m]) m = l;
                    if (r < items.Count && keys[r] < keys[m]) m = r;
                    if (m == i) break;
                    Swap(i, m);
                    i = m;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                (items[a], items[b]) = (items[b], items[a]);
                (keys[a], keys[b]) = (keys[b], keys[a]);
            }
        }
    }
}
