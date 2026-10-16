using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 地域マップ・道中マップの地形生成（生成バージョン3の試作。マップ設計 v0.3 §8.1 段階2）。
    /// 乱数のキーはマップごとに分ける（"world.gen3.map.&lt;ID&gt;.aN.*"）。あるマップの作り直しが他のマップを変えない。
    /// 骨格が決めたゲートの高さを境界の条件にする。検証に通らなければ、そのマップだけ別の乱数系列で作り直す。
    /// 試作で作るのは T1 花の野道 と R2 湖の森。ほかのマップは骨格だけ（§13.1 の順番）。
    /// </summary>
    public static class FieldMapGenerator
    {
        /// <summary>マップを置く座標の順番（屋外・鉱山・家・滝の裏の洞から離れた所に横に並べる）。</summary>
        private static readonly string[] OriginOrder =
        {
            StarSkeleton.LakeForest, StarSkeleton.WaterfallMountain, StarSkeleton.BeachCape, StarSkeleton.Highland,
            StarSkeleton.FlowerPath, StarSkeleton.MarshRiver, StarSkeleton.StreamSlope, StarSkeleton.MossValley,
            StarSkeleton.Ridge, StarSkeleton.SeaWindWood, StarSkeleton.CliffPath
        };

        public static Vector3 OriginOf(string mapId)
        {
            int i = Array.IndexOf(OriginOrder, mapId);
            if (i < 0) i = OriginOrder.Length;
            return WorldTuning.FieldMapOriginBase + new Vector3(i * WorldTuning.FieldMapSpacing, 0f, 0f);
        }

        public static bool CanBuild(string mapId) => mapId == StarSkeleton.FlowerPath || mapId == StarSkeleton.LakeForest;

        /// <summary>1枚のマップを作る（検証に通るまで決定的に作り直す）。作れない種類なら null。</summary>
        public static FieldMap Generate(ulong seed, StarSkeleton sk, string mapId, WorldPalette palette)
        {
            if (!CanBuild(mapId) || sk == null) return null;
            FieldMap best = null;
            for (int a = 0; a < WorldTuning.FieldMapMaxAttempts; a++)
            {
                FieldMap m = null;
                try
                {
                    FieldMapGenBase gen = mapId == StarSkeleton.FlowerPath ? (FieldMapGenBase)new FlowerPathGen() : new LakeForestGen();
                    m = gen.Run(seed, sk, mapId, a, palette);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[World] マップ {mapId} の生成 試行{a} で例外: {e}");
                    continue;
                }
                if (m.Valid) return m;
                Debug.Log($"[World] マップ {mapId} 試行{a} は検証で不採用: {string.Join(" / ", m.Notes)}");
                if (best == null || m.Notes.Count < best.Notes.Count) best = m;
            }
            if (best != null)
            {
                Debug.LogWarning($"[World] マップ {mapId}：すべての試行で検証に問題がありました。最も問題の少ない結果を使います。");
                best.Valid = true;
            }
            return best;
        }
    }

    /// <summary>地域・道中マップの生成の共通部分（高さ・道・小川・池・塗り分け・自然物・到達の検証）。</summary>
    internal abstract class FieldMapGenBase
    {
        protected ulong seed;
        protected string id;
        protected int attempt;
        protected StarSkeleton sk;
        protected FieldMap M;
        protected WorldPalette P;

        protected string Key => "world.gen3.map." + id + ".a" + attempt;
        protected SeededRandom R(string key) => SeededRandom.For(seed, Key + "." + key);
        protected WorldNoise Noise(string key) => new WorldNoise(seed, Key + "." + key);

        public FieldMap Run(ulong seed, StarSkeleton sk, string id, int attempt, WorldPalette palette)
        {
            this.seed = seed;
            this.id = id;
            this.attempt = attempt;
            this.sk = sk;
            P = palette;
            var sm = sk.Get(id);
            M = new FieldMap
            {
                Id = id,
                NameJa = sm.NameJa,
                NameEn = sm.NameEn,
                Kind = sm.Kind,
                Type = sm.Type,
                Width = sm.Size.x,
                Depth = sm.Size.y,
                Origin = FieldMapGenerator.OriginOf(id),
                Palette = palette,
                Attempt = attempt
            };
            M.Allocate();
            Build();
            PaintBase();
            PaintFeatures();
            PlaceNature();
            ValidateCommon();
            Validate();
            M.Valid = M.Notes.Count == 0;
            return M;
        }

        protected abstract void Build();
        protected abstract void Validate();
        /// <summary>地表の基本の塗り分け（草原・森など）。</summary>
        protected abstract FieldPaint BasePaint(float lx, float lz, float noise);
        protected abstract void PlaceNature();

        protected int N => M.NX * M.NZ;
        protected Vector2 LocalOf(int i) => new Vector2(M.XOf(i % M.NX), M.ZOf(i / M.NX));

        protected float GateHeight(string other)
        {
            var g = sk.GetGate("gate." + id + "." + other);
            return g != null && !float.IsNaN(g.Height) ? g.Height : 20f;
        }

        // =================================================================
        // 線（道・小川）からの距離
        // =================================================================

        protected static float[] Cumulative(List<Vector2> line)
        {
            var c = new float[line.Count];
            for (int i = 1; i < line.Count; i++) c[i] = c[i - 1] + Vector2.Distance(line[i - 1], line[i]);
            return c;
        }

        /// <summary>各グリッド点から線までの距離と、線上の最も近い点の「始点からの道のり」（maxDist より遠い点は MaxValue）。</summary>
        protected void Nearest(List<Vector2> line, float maxDist, out float[] dist, out float[] along)
        {
            dist = new float[N];
            along = new float[N];
            for (int i = 0; i < dist.Length; i++) dist[i] = float.MaxValue;
            var cum = Cumulative(line);
            for (int s = 1; s < line.Count; s++)
            {
                var a = line[s - 1];
                var b = line[s];
                float len = Vector2.Distance(a, b);
                M.ToGrid(Mathf.Min(a.x, b.x) - maxDist, Mathf.Min(a.y, b.y) - maxDist, out int x0, out int z0);
                M.ToGrid(Mathf.Max(a.x, b.x) + maxDist, Mathf.Max(a.y, b.y) + maxDist, out int x1, out int z1);
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                    {
                        int i = M.Index(ix, iz);
                        var p = new Vector2(M.XOf(ix), M.ZOf(iz));
                        float d = GeoUtil.DistToSegment(p, a, b, out float t);
                        if (d > maxDist || d >= dist[i]) continue;
                        dist[i] = d;
                        along[i] = cum[s - 1] + t * len;
                    }
            }
        }

        /// <summary>点ごとの値を、道のり along の位置で補間する。</summary>
        protected static float ValueAlong(float[] cum, IList<float> values, float along)
        {
            if (values.Count == 0) return 0f;
            if (along <= 0f) return values[0];
            for (int i = 1; i < cum.Length; i++)
            {
                if (along > cum[i]) continue;
                float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                return Mathf.Lerp(values[i - 1], values[i], (along - cum[i - 1]) / seg);
            }
            return values[values.Count - 1];
        }

        protected static List<Vector2> CatmullRom(List<Vector2> ctrl, float spacing)
        {
            var outPts = new List<Vector2>();
            for (int i = 0; i < ctrl.Count - 1; i++)
            {
                var p0 = ctrl[Mathf.Max(0, i - 1)];
                var p1 = ctrl[i];
                var p2 = ctrl[i + 1];
                var p3 = ctrl[Mathf.Min(ctrl.Count - 1, i + 2)];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(p1, p2) / spacing));
                for (int k = 0; k < steps; k++)
                {
                    float t = k / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    outPts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            outPts.Add(ctrl[ctrl.Count - 1]);
            return outPts;
        }

        // =================================================================
        // 道の経路探し（地形に沿う。坂がきついほど遠回りを選ぶ）
        // =================================================================

        private sealed class MinHeap
        {
            private readonly List<(int id, float key)> items = new List<(int, float)>();
            public int Count => items.Count;

            public void Push(int id, float key)
            {
                items.Add((id, key));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (items[p].key <= items[i].key) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }

            public int Pop()
            {
                var top = items[0].id;
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return top;
                items[0] = last;
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < items.Count && items[l].key < items[m].key) m = l;
                    if (r < items.Count && items[r].key < items[m].key) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
        }

        /// <summary>
        /// グリッドの上で from から to への道を探す（8方向）。坂の勾配が大きいほど高くつき、maxGrade を超える所（段・崖）は通らない。
        /// extraCost は1mあたりの追加の重み（水・縁など）。見つかった経路は 8m おきに間引いて、なめらかな曲線にして返す。
        /// </summary>
        protected List<Vector2> FindPath(Vector2 from, Vector2 to, Func<int, float> extraCost, float maxGrade = 0.32f, float inset = 3f)
        {
            int n = N;
            var g = new float[n];
            var prev = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; prev[i] = -1; }
            M.ToGrid(from.x, from.y, out int sx, out int sz);
            M.ToGrid(to.x, to.y, out int tx, out int tz);
            int s0 = M.Index(sx, sz), t0 = M.Index(tx, tz);
            var target = new Vector2(M.XOf(tx), M.ZOf(tz));
            var heap = new MinHeap();
            g[s0] = 0f;
            heap.Push(s0, Vector2.Distance(new Vector2(M.XOf(sx), M.ZOf(sz)), target));
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dz = { 0, 0, 1, -1, 1, -1, 1, -1 };
            while (heap.Count > 0)
            {
                int i = heap.Pop();
                if (closed[i]) continue;
                closed[i] = true;
                if (i == t0) break;
                int ix = i % M.NX, iz = i / M.NX;
                for (int k = 0; k < 8; k++)
                {
                    int nx = ix + dx[k], nz = iz + dz[k];
                    if (nx < 0 || nz < 0 || nx >= M.NX || nz >= M.NZ) continue;
                    int j = M.Index(nx, nz);
                    if (closed[j]) continue;
                    var l = new Vector2(M.XOf(nx), M.ZOf(nz));
                    // 遊べる範囲の縁は通らない（始まりと終わりのゲートの近くだけは別）
                    if (!M.InsidePlayable(l, inset) && Vector2.Distance(l, from) > 9f && Vector2.Distance(l, target) > 9f) continue;
                    float dist = k < 4 ? FieldMap.Step : FieldMap.Step * 1.41421f;
                    float grade = Mathf.Abs(M.H[j] - M.H[i]) / dist;
                    if (grade > maxGrade) continue;
                    float w = 1f + 25f * grade * grade + (grade > 0.1f ? (grade - 0.1f) * 40f : 0f) + extraCost(j);
                    float ng = g[i] + dist * w;
                    if (ng >= g[j]) continue;
                    g[j] = ng;
                    prev[j] = i;
                    heap.Push(j, ng + Vector2.Distance(l, target));
                }
            }
            if (prev[t0] < 0 && t0 != s0) return null;
            var cells = new List<int>();
            for (int c = t0; c >= 0; c = prev[c]) cells.Add(c);
            cells.Reverse();
            var ctrl = new List<Vector2> { from };
            for (int k = 4; k < cells.Count - 4; k += 4) ctrl.Add(LocalOf(cells[k]));
            ctrl.Add(to);
            return CatmullRom(ctrl, 3f);
        }

        /// <summary>本道の一番急な坂（4m の区間ごとの勾配の最大）。</summary>
        protected float MaxRoadGrade(out Vector2 at)
        {
            at = Vector2.zero;
            var cum = Cumulative(M.Road);
            float len = cum[cum.Length - 1];
            float worst = 0f;
            for (float s = 0f; s + 4f <= len; s += 2f)
            {
                var a = PointOn(M.Road, cum, s);
                var b = PointOn(M.Road, cum, s + 4f);
                if (!M.InsidePlayable(a, 1f) || !M.InsidePlayable(b, 1f)) continue;
                float gr = Mathf.Abs(M.SampleTerrain(b.x, b.y) - M.SampleTerrain(a.x, a.y)) / 4f;
                if (gr > worst)
                {
                    worst = gr;
                    at = a;
                }
            }
            return worst;
        }

        protected static Vector2 PointOn(List<Vector2> line, float[] cum, float along)
        {
            along = Mathf.Clamp(along, 0f, cum[cum.Length - 1]);
            for (int i = 1; i < cum.Length; i++)
            {
                if (along > cum[i]) continue;
                float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                return Vector2.Lerp(line[i - 1], line[i], (along - cum[i - 1]) / seg);
            }
            return line[line.Count - 1];
        }

        // =================================================================
        // 道（本道・脇道）を均す
        // =================================================================

        /// <summary>道の各点の高さ（今の地形をならし、坂を maxGrade 以下にする。両端は pinStart / pinEnd に固定）。</summary>
        protected float[] RoadTargets(List<Vector2> line, float maxGrade, float pinStart = float.NaN, float pinEnd = float.NaN, float window = 12f)
        {
            var cum = Cumulative(line);
            int n = line.Count;
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = M.SampleTerrain(line[i].x, line[i].y);
            var t = new float[n];
            for (int i = 0; i < n; i++)
            {
                float sum = 0f, wsum = 0f;
                for (int j = 0; j < n; j++)
                {
                    float d = Mathf.Abs(cum[j] - cum[i]);
                    if (d > window) continue;
                    float w = 1f - d / window;
                    sum += raw[j] * w;
                    wsum += w;
                }
                t[i] = sum / Mathf.Max(1e-4f, wsum);
            }
            bool pinS = !float.IsNaN(pinStart), pinE = !float.IsNaN(pinEnd);
            if (pinS) t[0] = pinStart;
            if (pinE) t[n - 1] = pinEnd;
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 1; i < n; i++)
                {
                    if (pinE && i == n - 1) continue;
                    float ds = cum[i] - cum[i - 1];
                    t[i] = Mathf.Clamp(t[i], t[i - 1] - maxGrade * ds, t[i - 1] + maxGrade * ds);
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    if (pinS && i == 0) continue;
                    float ds = cum[i + 1] - cum[i];
                    t[i] = Mathf.Clamp(t[i], t[i + 1] - maxGrade * ds, t[i + 1] + maxGrade * ds);
                }
            }
            return t;
        }

        /// <summary>2本の線が交わる所（それぞれの始点からの道のり）。</summary>
        protected static List<(float, float)> Crossings(List<Vector2> a, List<Vector2> b)
        {
            var res = new List<(float, float)>();
            var ca = Cumulative(a);
            var cb = Cumulative(b);
            for (int i = 1; i < a.Count; i++)
                for (int j = 1; j < b.Count; j++)
                {
                    if (!SegIntersect(a[i - 1], a[i], b[j - 1], b[j], out float t, out float u)) continue;
                    float ra = ca[i - 1] + t * (ca[i] - ca[i - 1]);
                    float rb = cb[j - 1] + u * (cb[j] - cb[j - 1]);
                    bool dup = false;
                    foreach (var x in res) if (Mathf.Abs(x.Item1 - ra) < 10f) dup = true;
                    if (!dup) res.Add((ra, rb));
                }
            return res;
        }

        protected static bool SegIntersect(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out float t, out float u)
        {
            var r = p2 - p;
            var s = q2 - q;
            float den = r.x * s.y - r.y * s.x;
            t = u = 0f;
            if (Mathf.Abs(den) < 1e-6f) return false;
            var qp = q - p;
            t = (qp.x * s.y - qp.y * s.x) / den;
            u = (qp.x * r.y - qp.y * r.x) / den;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        /// <summary>
        /// 本道と小川の交わり（浅瀬）の高さを合わせる。道の方が高い時は小川の水面をその所だけ上げ（浅瀬の小さな段になる。
        /// 上流は元から高いので逆流しない）、低い時は道を水面の少し下まで上げる（前後の坂は ClampGrade で広げる）。
        /// roadT は両端だけを固定して坂をならした後の道の高さ。
        /// </summary>
        protected void SettleFords(FieldStream st, List<(float road, float stream)> crossings, float[] roadT, float[] rcum, bool[] pinned)
        {
            var scum = Cumulative(st.Points);
            foreach (var (ri, si) in crossings)
            {
                float rt = ValueAlong(rcum, roadT, ri);
                float surf = ValueAlong(scum, st.Surface, si);
                float want = rt + 0.15f;
                if (want > surf)
                {
                    for (int k = 0; k < st.Points.Count; k++)
                    {
                        float d = scum[k] - si;
                        if (Mathf.Abs(d) < 4.5f || (d < 0f && st.Surface[k] < want))
                        {
                            if (st.Surface[k] < want) st.Surface[k] = want;
                            fill.Add(st.Points[k]);
                        }
                    }
                }
                else
                {
                    for (int k = 0; k < roadT.Length; k++)
                        if (Mathf.Abs(rcum[k] - ri) < 2.6f)
                        {
                            roadT[k] = surf - 0.15f;
                            pinned[k] = true;
                        }
                }
            }
        }

        /// <summary>水面を上げた浅瀬の所（川底と岸を盛って、水が地面から浮かないようにする）。</summary>
        protected readonly List<Vector2> fill = new List<Vector2>();

        /// <summary>ゲートの前（道の始まり・終わりから len m）は骨格の高さに固定する（ゲートの高さを両側でそろえる）。</summary>
        protected static void PinEnds(float[] t, float[] cum, bool[] pinned, float startH, float endH, float len = 12f)
        {
            float total = cum[cum.Length - 1];
            for (int k = 0; k < t.Length; k++)
            {
                if (!float.IsNaN(startH) && cum[k] <= len) { t[k] = startH; pinned[k] = true; }
                if (!float.IsNaN(endH) && cum[k] >= total - len) { t[k] = endH; pinned[k] = true; }
            }
        }

        /// <summary>道の高さの列の坂を maxGrade 以下にする（pinned の点は動かさない。浅瀬・ゲートなど）。</summary>
        protected static void ClampGrade(float[] t, float[] cum, float maxGrade, bool[] pinned)
        {
            int n = t.Length;
            for (int pass = 0; pass < 6; pass++)
            {
                for (int i = 1; i < n; i++)
                {
                    if (pinned[i]) continue;
                    float ds = cum[i] - cum[i - 1];
                    t[i] = Mathf.Clamp(t[i], t[i - 1] - maxGrade * ds, t[i - 1] + maxGrade * ds);
                }
                for (int i = n - 2; i >= 0; i--)
                {
                    if (pinned[i]) continue;
                    float ds = cum[i + 1] - cum[i];
                    t[i] = Mathf.Clamp(t[i], t[i + 1] - maxGrade * ds, t[i + 1] + maxGrade * ds);
                }
            }
        }

        /// <summary>道の帯を targets の高さに均す（half の内側はそのまま、blend の幅でなじませる）。</summary>
        protected void FlattenAlong(List<Vector2> line, float[] targets, float half, float blend)
        {
            Nearest(line, half + blend, out var dist, out var along);
            var cum = Cumulative(line);
            for (int i = 0; i < N; i++)
            {
                if (dist[i] == float.MaxValue) continue;
                float target = ValueAlong(cum, targets, along[i]);
                float w = dist[i] <= half ? 1f : 1f - GeoUtil.SmoothStep(half, half + blend, dist[i]);
                M.H[i] = Mathf.Lerp(M.H[i], target, w);
            }
        }

        /// <summary>点のまわりを高さ y に均す（円）。</summary>
        protected void FlattenDisc(Vector2 c, float radius, float blend, float y)
        {
            M.ToGrid(c.x - radius - blend, c.y - radius - blend, out int x0, out int z0);
            M.ToGrid(c.x + radius + blend, c.y + radius + blend, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    int i = M.Index(ix, iz);
                    float d = Vector2.Distance(new Vector2(M.XOf(ix), M.ZOf(iz)), c);
                    if (d > radius + blend) continue;
                    float w = d <= radius ? 1f : 1f - GeoUtil.SmoothStep(radius, radius + blend, d);
                    M.H[i] = Mathf.Lerp(M.H[i], y, w);
                }
        }

        // =================================================================
        // 小川と池
        // =================================================================

        /// <summary>
        /// 小川を掘る。水面は上流から下流へ下がるだけにする（逆流しない）。startSurface を与えると上流端の水面をそれにする（湖からの流れ出し）。
        /// endSurface を与えると下流端をそれ以上に保つ（湖へ注ぐ所）。
        /// </summary>
        protected FieldStream CarveStream(string sid, List<Vector2> pts, float radius, float depth, float startSurface = float.NaN, float endSurface = float.NaN)
        {
            var st = new FieldStream { Id = sid };
            st.Points.AddRange(pts);
            float s = float.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                float ground = M.SampleTerrain(pts[i].x, pts[i].y);
                float want = ground - 0.32f;
                s = i == 0 && !float.IsNaN(startSurface) ? startSurface : Mathf.Min(s, want);
                st.Surface.Add(s);
                st.Radius.Add(radius);
                st.Depth.Add(depth);
            }
            if (!float.IsNaN(endSurface))
                for (int i = 0; i < st.Surface.Count; i++) st.Surface[i] = Mathf.Max(st.Surface[i], endSurface);
            M.Streams.Add(st);
            return st;
        }

        /// <summary>小川の川底と水面をグリッドに入れる（浅瀬の深さを決めた後に呼ぶ）。</summary>
        protected void ApplyStream(FieldStream st)
        {
            float maxR = 0f;
            foreach (var r in st.Radius) maxR = Mathf.Max(maxR, r);
            Nearest(st.Points, maxR + 2.2f, out var dist, out var along);
            var cum = Cumulative(st.Points);
            for (int i = 0; i < N; i++)
            {
                if (dist[i] == float.MaxValue) continue;
                float surf = ValueAlong(cum, st.Surface, along[i]);
                float rad = ValueAlong(cum, st.Radius, along[i]);
                float dep = ValueAlong(cum, st.Depth, along[i]);
                float d = dist[i];
                float bed = surf - dep * (1f - GeoUtil.SmoothStep(rad * 0.45f, rad + 0.3f, d));
                float bank = Mathf.Lerp(bed, surf + 0.45f, GeoUtil.SmoothStep(rad * 0.6f, rad + 2.2f, d));
                if (d < rad + 2.2f) M.H[i] = Mathf.Min(M.H[i], d < rad ? bed : bank);
                // 水面を上げた浅瀬：川底と岸を盛る（岸はまわりの地面へなだらかに下ろす）
                if (fill.Count > 0 && d < rad + 5f)
                {
                    var l = LocalOf(i);
                    bool near = false;
                    foreach (var f in fill) if ((f - l).sqrMagnitude < 36f) { near = true; break; }
                    if (near)
                    {
                        float floorH = d < rad ? bed : Mathf.Lerp(surf + 0.12f, M.H[i], GeoUtil.SmoothStep(rad + 0.8f, rad + 5f, d));
                        M.H[i] = Mathf.Max(M.H[i], floorH);
                    }
                }
                if (d < rad + 0.35f && (float.IsNaN(M.WaterY[i]) || M.Water[i] == FieldWater.Stream))
                {
                    M.WaterY[i] = surf;
                    M.Water[i] = FieldWater.Stream;
                }
            }
        }

        /// <summary>池（器の形に掘る）。水面は縁の地面の一番低い所より少し下。</summary>
        protected PondInfo CarvePond(Vector2 c, float radius, float maxDepth)
        {
            float rim = float.MaxValue;
            for (int k = 0; k < 16; k++)
            {
                float a = k / 16f * Mathf.PI * 2f;
                var p = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + 2.5f);
                rim = Mathf.Min(rim, M.SampleTerrain(p.x, p.y));
            }
            float surface = rim - 0.25f;
            var pd = new PondInfo { WaterIndex = -1, Center = c, Radius = radius, Surface = surface };
            M.ToGrid(c.x - radius * 1.6f, c.y - radius * 1.6f, out int x0, out int z0);
            M.ToGrid(c.x + radius * 1.6f, c.y + radius * 1.6f, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    int i = M.Index(ix, iz);
                    float k = Vector2.Distance(new Vector2(M.XOf(ix), M.ZOf(iz)), c) / radius;
                    if (k < 1f)
                    {
                        // 縁へ向かって浅くなり、岸の地面へ段差なくつながる
                        float depth = (maxDepth + 0.1f) * (1f - GeoUtil.SmoothStep(0.2f, 1f, k));
                        M.H[i] = Mathf.Min(M.H[i], surface + 0.1f - depth);
                    }
                    else if (k < 1.6f)
                    {
                        float bank = Mathf.Lerp(surface + 0.1f, M.H[i], GeoUtil.SmoothStep(1f, 1.6f, k));
                        M.H[i] = Mathf.Min(M.H[i], bank);
                    }
                    if (k < 1.05f)
                    {
                        M.WaterY[i] = surface;
                        M.Water[i] = FieldWater.Pond;
                    }
                }
            M.Ponds.Add(pd);
            return pd;
        }

        // =================================================================
        // 塗り分け
        // =================================================================

        private void PaintBase()
        {
            var n = Noise("paint");
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                M.Paint[i] = BasePaint(l.x, l.y, n.Fbm(l.x * 0.03f, l.y * 0.03f, 3));
            }
        }

        private void PaintFeatures()
        {
            for (int i = 0; i < N; i++)
            {
                int ix = i % M.NX, iz = i / M.NX;
                if (Vector3.Angle(M.Normal(ix, iz), Vector3.up) > 36f) M.Paint[i] = FieldPaint.Rock;
            }
            // 水辺：池は泥、小川と湖は砂
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                if (!float.IsNaN(M.WaterY[i])) continue;
                if (M.WaterNear(l.x, l.y, 2.4f))
                {
                    var near = NearestWaterKind(l, 2.4f);
                    M.Paint[i] = near == FieldWater.Pond ? FieldPaint.Mud : FieldPaint.Sand;
                }
            }
            PaintLine(M.Road, WorldTuning.FieldRoadHalfWidth + 0.3f, FieldPaint.Road);
            foreach (var t in M.Trails) PaintLine(t, WorldTuning.FieldTrailHalfWidth + 0.2f, FieldPaint.Trail);
        }

        private FieldWater NearestWaterKind(Vector2 l, float radius)
        {
            int r = Mathf.CeilToInt(radius / FieldMap.Step);
            M.ToGrid(l.x, l.y, out int cx, out int cz);
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int ix = Mathf.Clamp(cx + dx, 0, M.NX - 1), iz = Mathf.Clamp(cz + dz, 0, M.NZ - 1);
                    int i = M.Index(ix, iz);
                    if (!float.IsNaN(M.WaterY[i])) return M.Water[i];
                }
            return FieldWater.None;
        }

        protected void PaintLine(List<Vector2> line, float half, FieldPaint paint)
        {
            if (line.Count < 2) return;
            Nearest(line, half, out var dist, out _);
            for (int i = 0; i < N; i++)
                if (dist[i] <= half && float.IsNaN(M.WaterY[i])) M.Paint[i] = paint;
        }

        protected void PaintDisc(Vector2 c, float radius, FieldPaint paint)
        {
            M.ToGrid(c.x - radius, c.y - radius, out int x0, out int z0);
            M.ToGrid(c.x + radius, c.y + radius, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    int i = M.Index(ix, iz);
                    if (Vector2.Distance(new Vector2(M.XOf(ix), M.ZOf(iz)), c) <= radius && float.IsNaN(M.WaterY[i])) M.Paint[i] = paint;
                }
        }

        // =================================================================
        // 自然物
        // =================================================================

        /// <summary>自然物を置ける所か（道・水・建て物・急斜面を避ける）。</summary>
        protected bool NatureOk(Vector2 l, float roadGap, float waterGap, float maxSlope = 32f, bool allowOutside = false)
        {
            if (!allowOutside && !M.InsidePlayable(l, 1f)) return false;
            if (l.x < -FieldMap.Margin + 2f || l.x > M.Width + FieldMap.Margin - 2f || l.y < -FieldMap.Margin + 2f || l.y > M.Depth + FieldMap.Margin - 2f) return false;
            if (M.WaterNear(l.x, l.y, waterGap)) return false;
            if (roadGap > 0f && M.DistToPaths(l) < roadGap) return false;
            if (M.Slope(l.x, l.y) > maxSlope) return false;
            foreach (var s in M.Structures)
                if (Vector2.Distance(s.Local, l) < 2.2f * s.Size + 1.2f) return false;
            foreach (var g in M.Gates)
                if (Vector2.Distance(M.ToLocal(g.Position.x, g.Position.z), l) < 9f) return false;
            return true;
        }

        protected void AddNature(NatureKind kind, Vector2 l, float scale, SeededRandom r, AreaType area = AreaType.Forest)
        {
            M.Nature.Add(new NatureItem
            {
                Kind = kind,
                Position = M.OnGroundWorld(l),
                Yaw = r.Range(0f, 360f),
                Scale = scale,
                Variant = r.Range(0, 16),
                Area = area
            });
        }

        protected int CountNature(NatureKind kind, Func<Vector2, bool> where = null)
        {
            int c = 0;
            foreach (var n in M.Nature)
                if (n.Kind == kind && (where == null || where(M.ToLocal(n.Position.x, n.Position.z)))) c++;
            return c;
        }

        // =================================================================
        // ゲートと建て物
        // =================================================================

        /// <summary>ゲートを置く（local は遊べる範囲の端の点、inward は内向きの単位ベクトル）。</summary>
        protected FieldGateInfo AddGate(string other, Vector2 local, Vector2 inward)
        {
            var gp = local + inward * WorldTuning.FieldGateInset;
            var arrive = local + inward * (WorldTuning.FieldGateInset + WorldTuning.FieldGateArrival);
            float yawOut = Mathf.Atan2(-inward.x, -inward.y) * Mathf.Rad2Deg;
            var g = new FieldGateInfo
            {
                Id = "gate." + id + "." + other,
                MapId = id,
                OtherMapId = other,
                Yaw = yawOut,
                Position = M.OnGroundWorld(gp),
                Arrival = M.OnGroundWorld(arrive) + Vector3.up * 0.2f,
                ArrivalYaw = yawOut + 180f
            };
            M.Gates.Add(g);
            return g;
        }

        /// <summary>地形を仕上げた後、ゲートと建て物の高さを地面に合わせる。</summary>
        protected void SettleHeights()
        {
            foreach (var g in M.Gates)
            {
                var gl = M.ToLocal(g.Position.x, g.Position.z);
                var al = M.ToLocal(g.Arrival.x, g.Arrival.z);
                g.Position = M.OnGroundWorld(gl);
                g.Arrival = M.OnGroundWorld(al) + Vector3.up * 0.2f;
            }
            foreach (var s in M.Structures)
                if (s.Kind != FieldStructureKind.SteppingStone && s.Kind != FieldStructureKind.Pier) s.Y = M.SampleTerrain(s.Local.x, s.Local.y);
        }

        protected void AddStructure(FieldStructureKind kind, Vector2 l, float yaw, float size = 1f, string destination = null, float y = float.NaN)
        {
            M.Structures.Add(new FieldStructure { Kind = kind, Local = l, Yaw = yaw, Size = size, Destination = destination, Y = float.IsNaN(y) ? M.SampleTerrain(l.x, l.y) : y });
        }

        protected void AddPlace(string pid, string nameJa, Vector2 l, bool pocket = false, bool hidden = false)
        {
            M.Places.Add(new FieldPlace { Id = pid, NameJa = nameJa, Local = l, Pocket = pocket, Hidden = hidden });
        }

        protected static float YawOf(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        // =================================================================
        // 検証（歩いて行けるか）
        // =================================================================

        private bool[] reach;

        /// <summary>start から歩いて行けるグリッド点（隣との高さの差 1.7m 以下＝坂 40°まで、水深 0.6m 以下、遊べる範囲の中）。</summary>
        protected bool[] Reachable(Vector2 start)
        {
            var seen = new bool[N];
            M.ToGrid(start.x, start.y, out int sx, out int sz);
            var q = new Queue<int>();
            int s0 = M.Index(sx, sz);
            seen[s0] = true;
            q.Enqueue(s0);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int ix = i % M.NX, iz = i / M.NX;
                for (int k = 0; k < 4; k++)
                {
                    int nx = ix + (k == 0 ? 1 : k == 1 ? -1 : 0);
                    int nz = iz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= M.NX || nz >= M.NZ) continue;
                    int j = M.Index(nx, nz);
                    if (seen[j]) continue;
                    var l = new Vector2(M.XOf(nx), M.ZOf(nz));
                    if (!M.InsidePlayable(l, 0.5f)) continue;
                    if (Mathf.Abs(M.H[j] - M.H[i]) > WorldTuning.FieldWalkMaxStep) continue;
                    float w = M.WaterY[j];
                    if (!float.IsNaN(w) && w - M.H[j] > WorldLayout.WadeableDepth) continue;
                    seen[j] = true;
                    q.Enqueue(j);
                }
            }
            return seen;
        }

        /// <summary>歩いて行ける点のうち、target から radius 以内に1つでもあるか。</summary>
        protected bool CanReach(Vector2 target, float radius = 3f)
        {
            if (reach == null) return false;
            M.ToGrid(target.x - radius, target.y - radius, out int x0, out int z0);
            M.ToGrid(target.x + radius, target.y + radius, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    if (!reach[M.Index(ix, iz)]) continue;
                    if (Vector2.Distance(new Vector2(M.XOf(ix), M.ZOf(iz)), target) <= radius) return true;
                }
            return false;
        }

        private void ValidateCommon()
        {
            if (M.Gates.Count == 0)
            {
                M.Notes.Add("ゲートがない");
                return;
            }
            var first = M.ToLocal(M.Gates[0].Arrival.x, M.Gates[0].Arrival.z);
            reach = Reachable(first);
            foreach (var g in M.Gates)
            {
                var gl = M.ToLocal(g.Arrival.x, g.Arrival.z);
                if (!CanReach(gl)) M.Notes.Add($"{g.Id} へ歩いて行けない");
                var sg = sk.GetGate(g.Id);
                if (sg != null && !float.IsNaN(sg.Height) && Mathf.Abs(g.Position.y - sg.Height) > WorldTuning.FieldGateHeightTolerance)
                    M.Notes.Add($"{g.Id} の高さが骨格と違う（{g.Position.y:0.0} / {sg.Height:0.0}）");
            }
            foreach (var p in M.Places)
                if (!CanReach(p.Local, 4.5f)) M.Notes.Add($"{p.NameJa} へ歩いて行けない");
            // 本道の急な坂（§3.3：坂はゆるく）
            float worst = MaxRoadGrade(out var at);
            if (worst > WorldTuning.FieldRoadMaxGrade)
            {
                float dg = float.MaxValue, ds = float.MaxValue, dt = float.MaxValue;
                foreach (var g in M.Gates) dg = Mathf.Min(dg, Vector2.Distance(M.ToLocal(g.Position.x, g.Position.z), at));
                foreach (var st in M.Streams) ds = Mathf.Min(ds, FieldMap.DistToPolyline(at, st.Points));
                foreach (var tr in M.Trails) dt = Mathf.Min(dt, FieldMap.DistToPolyline(at, tr));
                float along = 0f, best = float.MaxValue;
                var cum = Cumulative(M.Road);
                for (int i = 0; i < M.Road.Count; i++) { float d = Vector2.Distance(M.Road[i], at); if (d < best) { best = d; along = cum[i]; } }
                var prof = new System.Text.StringBuilder();
                for (float s = along - 6f; s <= along + 8f; s += 2f)
                {
                    var q = PointOn(M.Road, cum, s);
                    prof.Append(M.SampleTerrain(q.x, q.y).ToString("0.0")).Append(' ');
                }
                M.Notes.Add($"本道に急な坂（{worst * 100f:0}%・{at.x:0},{at.y:0}・道のり{along:0}/{cum[cum.Length - 1]:0}・ゲート{dg:0}・小川{ds:0}・脇道{dt:0}・高さ {prof}）");
            }
        }
    }

    // =====================================================================
    // T1 花の野道（村 ⇄ 湖の森。§3.1・§5・§8.3）
    // =====================================================================

    internal sealed class FlowerPathGen : FieldMapGenBase
    {
        private Vector2 gateA, gateB;
        private float hA, hB;
        private Vector2 hillCenter, hillFront;
        private float hillHeight, hillBack, hillFrontR;
        private Vector2 flowerCenter;
        private float flowerRadius;
        private Vector2 terraceCenter;
        private float terraceYaw, terraceY;
        private Vector2 pondCenter;
        private List<Vector2> streamPts;

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            hA = GateHeight(StarSkeleton.Village);
            hB = GateHeight(StarSkeleton.LakeForest);
            gateA = new Vector2(0f, D * 0.5f + r.Range(-24f, 24f));
            gateB = new Vector2(W, D * 0.5f + r.Range(-24f, 24f));

            // 小川：湖の森からの流れ出し（R2 側の端、本道の脇）から村の側の端へ。先に流れの線を決め、そのまわりを谷にする
            float sideB = gateB.y > D * 0.5f ? -1f : 1f;
            var s0 = new Vector2(W + FieldMap.Margin, gateB.y + sideB * 13f);
            var s1 = new Vector2(-FieldMap.Margin, Mathf.Clamp(gateA.y - sideB * 16f, 16f, D - 16f));
            // 大きく蛇行させる（本道と並んで走り続けず、離れたり近づいたりして、浅瀬で何度か交わる）
            float amp = r.Range(20f, 32f), waves = r.Range(1.8f, 3.2f), phase = r.Range(0f, Mathf.PI * 2f);
            streamPts = new List<Vector2>();
            // 遊べる範囲の外はまっすぐ（本道との交わりが必ずマップの中で起きるように）。中で大きく蛇行する
            for (float x = s0.x; x >= s1.x - 0.01f; x -= 3f)
            {
                float u = Mathf.Clamp01((W - x) / W);
                float y = Mathf.Lerp(s0.y, s1.y, GeoUtil.SmoothStep(0f, 1f, u));
                float env = Mathf.Sin(u * Mathf.PI);
                y = Mathf.Clamp(y + Mathf.Sin(u * Mathf.PI * waves + phase) * amp * env, 14f, D - 14f);
                streamPts.Add(new Vector2(x, y));
            }
            // 村の側が高い星では、小川は村の側から湖の森の側へ流れる（水は上へ流れない）
            if (hA > hB - 2f) streamPts.Reverse();

            // 道中の型（§8.2）：0＝開けた野の一本道、1＝丘を回り込む道、2＝段丘を折り返して登る道
            PlanScarps(r);
            if (M.Type == 1) PlanRoundHills(r);

            // 高さ：全体の上り下り ＋ 段丘の段（2〜4m の崖。切れ目はゆるい坂）＋ うねり ＋ 小川の谷 ＋ 回り込む丘
            var roll = Noise("roll");
            Nearest(streamPts, 26f, out var sDist, out _);
            float floorH = Mathf.Min(hA, hB) - 2.5f;
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float t = Mathf.Clamp01(l.x / W);
                float baseH = Mathf.Lerp(hA, hB - stepSign * stepTotal, GeoUtil.SmoothStep(0f, 1f, t)) + ScarpsAt(l);
                float h = baseH + (roll.Fbm(l.x * 0.017f, l.y * 0.017f, 4) - 0.5f) * 8f;
                if (sDist[i] != float.MaxValue)
                {
                    float w = 1f - GeoUtil.SmoothStep(5f, 26f, sDist[i]);
                    h = Mathf.Lerp(h, baseH - 0.6f + sDist[i] * 0.05f, w);
                }
                h += RoundHillsAt(l);
                h = Mathf.Max(h, floorH);
                h += Rim(l);
                M.H[i] = h;
            }
            // ゲートのまわりは骨格の高さに
            FlattenDisc(gateA, 10f, 10f, hA);
            FlattenDisc(gateB, 10f, 10f, hB);

            // 本道：地形に沿って探す（坂がきつい所・段・丘を避ける）。型によって、まっすぐ寄り・丘を回り込む・段の切れ目を折り返す、と曲がり方が変わる
            // 小川の中は高くつき（渡るのは短く）、小川のすぐ脇を長く走るのも少し高くつく（谷沿いばかりにならないように）
            Nearest(streamPts, 14f, out var wetDist, out _);
            var road = FindPath(gateA, gateB, j =>
            {
                float wd = wetDist[j];
                float nearStream = M.Type == 0 ? 0.08f : 0.22f;
                float extra = wd == float.MaxValue ? 0f : wd < 4f ? 14f : (14f - wd) * nearStream;
                float z = M.ZOf(j / M.NX);
                float edge = Mathf.Min(z, D - z);
                if (edge < 18f) extra += (18f - edge) * 0.35f;
                // 型0（開けた野の一本道）：門と門を結ぶ線から離れるほど少し高くつく＝まっすぐ寄り
                if (M.Type == 0) extra += GeoUtil.DistToSegment(LocalOf(j), gateA, gateB, out _) * 0.045f;
                return extra;
            });
            if (road == null)
            {
                M.Notes.Add("本道の経路が見つからない");
                road = CatmullRom(new List<Vector2> { gateA, new Vector2(W * 0.5f, D * 0.5f), gateB }, 3f);
            }
            M.Road.AddRange(road);

            // 丘（隠れた場所：本道側は急な崖、裏はなだらか。見えているのに回り込まないと登れない）
            hillHeight = r.Range(6.5f, 8.5f);
            hillBack = r.Range(28f, 34f);
            hillFrontR = r.Range(7f, 8.5f);
            bool hillOk = false;
            for (int k = 0; k < 400 && !hillOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.15f, W * 0.85f), r.Range(16f, D - 16f));
                float dr = FieldMap.DistToPolyline(c, M.Road);
                if (dr < 22f || dr > 48f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 16f) continue;
                if (NearScarp(c, 8f)) continue;
                bool nearRound = false;
                foreach (var rh in roundHills) if (Vector2.Distance(rh.c, c) < rh.r + 14f) nearRound = true;
                if (nearRound) continue;
                hillCenter = c;
                Vector2 nearest = M.Road[0];
                float bd = float.MaxValue;
                foreach (var q in M.Road) { float d = Vector2.Distance(q, c); if (d < bd) { bd = d; nearest = q; } }
                hillFront = (nearest - c).normalized;
                hillOk = true;
            }
            if (!hillOk) M.Notes.Add("丘の場所がない");
            else
                for (int i = 0; i < N; i++) M.H[i] += HillAt(LocalOf(i));

            // 果樹の段（本道から少し離れた、ひと段高い平地）
            terraceYaw = 0f;
            bool terOk = false;
            for (int k = 0; k < 400 && !terOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.15f, W * 0.85f), r.Range(18f, D - 18f));
                float dr = FieldMap.DistToPolyline(c, M.Road);
                if (dr < 16f || dr > 34f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 16f) continue;
                if (hillOk && Vector2.Distance(c, hillCenter) < hillBack + 14f) continue;
                if (NearScarp(c, 11f) || RoundHillsAt(c) > 0.5f) continue;
                terraceCenter = c;
                terOk = true;
            }
            if (!terOk) M.Notes.Add("果樹の段の場所がない");
            else
            {
                terraceY = M.SampleTerrain(terraceCenter.x, terraceCenter.y) + r.Range(2.2f, 2.8f);
                FlattenDisc(terraceCenter, 9f, 5.5f, terraceY);
            }

            // 花畑（開けた草原の中）
            flowerRadius = r.Range(20f, 26f);
            bool flOk = false;
            for (int k = 0; k < 400 && !flOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.2f, W * 0.8f), r.Range(20f, D - 20f));
                if (FieldMap.DistToPolyline(c, M.Road) < 10f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 10f) continue;
                if (hillOk && Vector2.Distance(c, hillCenter) < hillBack + flowerRadius * 0.5f) continue;
                if (terOk && Vector2.Distance(c, terraceCenter) < flowerRadius + 14f) continue;
                if (NearScarp(c, flowerRadius * 0.6f) || RoundHillsAt(c) > 0.5f) continue;
                flowerCenter = c;
                flOk = true;
            }
            if (!flOk) M.Notes.Add("花畑の場所がない");

            // 野の池（湧き水の池。小川とは別）
            bool pondOk = false;
            for (int k = 0; k < 400 && !pondOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.22f, W * 0.78f), r.Range(18f, D - 18f));
                float dr = FieldMap.DistToPolyline(c, M.Road);
                if (dr < 14f || dr > 44f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 14f) continue;
                if (hillOk && Vector2.Distance(c, hillCenter) < hillBack + 8f) continue;
                if (terOk && Vector2.Distance(c, terraceCenter) < 26f) continue;
                if (flOk && Vector2.Distance(c, flowerCenter) < flowerRadius + 12f) continue;
                if (NearScarp(c, 10f) || RoundHillsAt(c) > 0.5f) continue;
                pondCenter = c;
                pondOk = true;
            }
            if (!pondOk) M.Notes.Add("野の池の場所がない");

            // 小川との交わりは浅瀬（飛び石）。小川・池・果樹の段を先に掘ってから、本道を最後に均す（道の脇を後から削らない）
            var crossings = Crossings(M.Road, streamPts);
            var stream = CarveStream("stream.t1", streamPts, r.Range(1.3f, 1.7f), 0.42f);
            var rcum = Cumulative(M.Road);
            var scum = Cumulative(streamPts);
            var fordSurf = new List<float>();
            foreach (var (ri, si) in crossings)
            {
                float surf = ValueAlong(scum, stream.Surface, si);
                fordSurf.Add(surf);
                for (int k = 0; k < streamPts.Count; k++)
                    if (Mathf.Abs(scum[k] - si) < 4.5f)
                    {
                        stream.Depth[k] = 0.2f;
                        if (!stream.Fords.Contains(k)) stream.Fords.Add(k);
                    }
                var cp = PointAlong(M.Road, rcum, ri);
                var dir = (PointAlong(M.Road, rcum, ri + 1f) - cp).normalized;
                for (int k = -2; k <= 2; k++)
                    AddStructure(FieldStructureKind.SteppingStone, cp + dir * k * 1.15f + new Vector2(-dir.y, dir.x) * 1.2f, r.Range(0f, 360f), 1f, null, surf + 0.1f);
            }
            if (crossings.Count == 0) M.Notes.Add("小川が本道と交わらない（浅瀬なし）");
            // 本道の高さ（坂は 8% まで、両端はゲートの高さ）を決めてから、浅瀬で道と小川の高さを合わせる
            var roadT = RoadTargets(M.Road, 0.08f, hA, hB);
            var pinned = new bool[M.Road.Count];
            PinEnds(roadT, rcum, pinned, hA, hB);
            ClampGrade(roadT, rcum, 0.08f, pinned);
            SettleFords(stream, crossings, roadT, rcum, pinned);
            ClampGrade(roadT, rcum, 0.08f, pinned);
            // 飛び石の高さを合わせ直す（浅瀬の水面を上げた所がある）
            foreach (var stn in M.Structures)
                if (stn.Kind == FieldStructureKind.SteppingStone)
                {
                    float sAlong = 0f, bd = float.MaxValue;
                    for (int k = 0; k < streamPts.Count; k++) { float d = Vector2.Distance(streamPts[k], stn.Local); if (d < bd) { bd = d; sAlong = scum[k]; } }
                    stn.Y = ValueAlong(scum, stream.Surface, sAlong) + 0.1f;
                }
            ApplyStream(stream);
            if (pondOk) CarvePond(pondCenter, r.Range(7f, 9.5f), r.Range(1.3f, 1.6f));
            if (terOk) FlattenDisc(terraceCenter, 8.5f, 0f, terraceY);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 脇道（本道から池・果樹の段・丘の裏へ）
            if (pondOk) AddTrail(pondCenter, 9.5f);
            if (terOk) AddTrail(terraceCenter, 6f);
            if (hillOk)
            {
                // 丘の裏（なだらかな側）へ回り込む小道
                var back = hillCenter - hillFront * (hillBack * 0.9f);
                back = new Vector2(Mathf.Clamp(back.x, 8f, W - 8f), Mathf.Clamp(back.y, 8f, D - 8f));
                AddTrail(back, 0f);
            }

            // 東屋（本道の中ほど、雨宿り）
            float midAlong = rcum[rcum.Length - 1] * r.Range(0.42f, 0.58f);
            var mid = PointAlong(M.Road, rcum, midAlong);
            var mdir = (PointAlong(M.Road, rcum, midAlong + 2f) - mid).normalized;
            var gz = mid + new Vector2(-mdir.y, mdir.x) * (r.Chance(0.5f) ? 6.5f : -6.5f);
            if (M.WaterNear(gz.x, gz.y, 4f)) gz = mid - (gz - mid);
            FlattenDisc(gz, 3.6f, 2.5f, M.SampleTerrain(gz.x, gz.y));
            AddStructure(FieldStructureKind.Gazebo, gz, YawOf(mid - gz));
            // 脇道・東屋で本道の脇を触ったので、本道の上だけもう一度均す
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            // ゲートと道標
            AddGate(StarSkeleton.Village, new Vector2(0f, gateA.y), Vector2.right);
            AddGate(StarSkeleton.LakeForest, new Vector2(W, gateB.y), Vector2.left);
            AddStructure(FieldStructureKind.Signpost, gateA + new Vector2(14f, 4.2f), 0f, 1f, StarSkeleton.LakeForest);
            AddStructure(FieldStructureKind.Signpost, gateB + new Vector2(-14f, -4.2f), 0f, 1f, StarSkeleton.Village);
            SettleHeights();

            // 目印の場所（寄り道ポケット 5 ＋ 隠れた場所 1）
            AddPlace("t1.gazebo", "東屋", gz);
            if (flOk) AddPlace("t1.flowers", "花畑", flowerCenter, pocket: true);
            if (pondOk) AddPlace("t1.pond", "野の池のほとり", pondCenter + (M.Road[M.Road.Count / 2] - pondCenter).normalized * 11.5f, pocket: true);
            if (terOk) AddPlace("t1.terrace", "果樹の段", terraceCenter, pocket: true);
            if (crossings.Count > 0) AddPlace("t1.ford", "飛び石の浅瀬", PointAlong(M.Road, rcum, crossings[0].Item1), pocket: true);
            AddPlace("t1.meadow", "開けた草原", OpenMeadow(), pocket: true);
            if (hillOk)
            {
                AddPlace("t1.hilltop", "丘の上（隠れた場所）", hillCenter, hidden: true);
                AddStructure(FieldStructureKind.Crystal, hillCenter, r.Range(0f, 360f), 1.2f);
            }
        }

        // ---- 段丘の段（道中を横切る崖。切れ目はゆるい坂）----
        private sealed class Scarp
        {
            public float X, Wobble, Freq, Phase, Height;
            public readonly List<float> Passes = new List<float>();
            public float PassWidth, RampLength;
        }

        private readonly List<Scarp> scarps = new List<Scarp>();
        private float stepSign = 1f, stepTotal;
        private readonly List<(Vector2 c, float r, float h)> roundHills = new List<(Vector2, float, float)>();

        /// <summary>
        /// 段丘の段を決める。型0：段1本（切れ目は2つの門を結ぶ線の近く＝道はまっすぐ寄り）。型1：段1〜2本（切れ目はばらばら）。
        /// 型2：段3本（切れ目が左右に互い違い＝道は折り返して登る）。段の高さ 2〜4m は飛び降りられる（帰りの近道）。
        /// </summary>
        private void PlanScarps(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            int count = M.Type == 0 ? 1 : M.Type == 1 ? r.Range(1, 3) : 3;
            stepSign = hB >= hA ? 1f : -1f;
            stepTotal = 0f;
            float side = r.Chance(0.5f) ? 1f : -1f;
            for (int k = 0; k < count; k++)
            {
                float u = count == 1 ? r.Range(0.4f, 0.6f) : Mathf.Lerp(0.24f, 0.8f, k / (float)(count - 1)) + r.Range(-0.04f, 0.04f);
                var s = new Scarp
                {
                    X = W * u,
                    Wobble = r.Range(5f, 12f),
                    Freq = r.Range(0.025f, 0.05f),
                    Phase = r.Range(0f, Mathf.PI * 2f),
                    Height = r.Range(2.2f, 4f),
                    PassWidth = r.Range(7f, 10f)
                };
                s.RampLength = s.Height / 0.085f;
                if (M.Type == 0)
                {
                    float onLine = Mathf.Lerp(gateA.y, gateB.y, u);
                    s.Passes.Add(Mathf.Clamp(onLine + r.Range(-6f, 6f), 20f, D - 20f));
                }
                else if (M.Type == 1)
                {
                    s.Passes.Add(r.Range(22f, D - 22f));
                    if (r.Chance(0.5f)) s.Passes.Add(r.Range(22f, D - 22f));
                }
                else
                {
                    s.Passes.Add(Mathf.Clamp(D * 0.5f + side * r.Range(28f, 34f), 18f, D - 18f));
                    side = -side;
                }
                scarps.Add(s);
                stepTotal += s.Height;
            }
        }

        private float ScarpsAt(Vector2 l)
        {
            float h = 0f;
            foreach (var s in scarps)
            {
                float xs = s.X + Mathf.Sin(l.y * s.Freq + s.Phase) * s.Wobble;
                float pass = 0f;
                foreach (var pz in s.Passes)
                {
                    float d = (l.y - pz) / s.PassWidth;
                    pass = Mathf.Max(pass, Mathf.Exp(-d * d));
                }
                float width = Mathf.Lerp(1.8f, s.RampLength, pass);
                h += stepSign * s.Height * GeoUtil.SmoothStep(xs - width * 0.5f, xs + width * 0.5f, l.x);
            }
            return h;
        }

        private bool NearScarp(Vector2 l, float gap)
        {
            foreach (var s in scarps)
            {
                float xs = s.X + Mathf.Sin(l.y * s.Freq + s.Phase) * s.Wobble;
                if (Mathf.Abs(l.x - xs) < gap) return true;
            }
            return false;
        }

        /// <summary>型1：門と門を結ぶ線の上に、道が回り込む丘を2〜3つ置く。</summary>
        private void PlanRoundHills(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            int count = r.Range(2, 4);
            for (int k = 0; k < count; k++)
            {
                float u = Mathf.Lerp(0.25f, 0.75f, count == 1 ? 0.5f : k / (float)(count - 1)) + r.Range(-0.05f, 0.05f);
                var c = new Vector2(W * u, Mathf.Clamp(Mathf.Lerp(gateA.y, gateB.y, u) + r.Range(-10f, 10f), 30f, D - 30f));
                if (FieldMap.DistToPolyline(c, streamPts) < 16f) c.y = Mathf.Clamp(c.y + (c.y > D * 0.5f ? -18f : 18f), 26f, D - 26f);
                roundHills.Add((c, r.Range(17f, 24f), r.Range(6f, 10f)));
            }
        }

        private float RoundHillsAt(Vector2 l)
        {
            float h = 0f;
            foreach (var rh in roundHills)
            {
                float k = Vector2.Distance(l, rh.c) / rh.r;
                if (k < 1f) h += rh.h * (1f - GeoUtil.SmoothStep(0.15f, 1f, k));
            }
            return h;
        }

        private float HillAt(Vector2 l)
        {
            var v = l - hillCenter;
            float d = v.magnitude;
            if (d > hillBack * 1.2f) return 0f;
            float cosA = d > 1e-3f ? Vector2.Dot(v / d, hillFront) : 0f;
            // 本道に向く側（cosA>0）は半径が小さい＝急な崖
            float rad = Mathf.Lerp(hillBack, hillFrontR, GeoUtil.SmoothStep(0.15f, 0.85f, cosA));
            float k = d / rad;
            return hillHeight * (1f - GeoUtil.SmoothStep(0.3f, 1f, k));
        }

        /// <summary>遊べる範囲のまわりは丘が立ち上がる（谷のような道中の見た目。ゲートの先は道が続いて見えるように低く）。</summary>
        private float Rim(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dz = Mathf.Max(0f, Mathf.Max(9f - l.y, l.y - (D - 9f)));
            float dx = Mathf.Max(0f, Mathf.Max(-l.x, l.x - W));
            float corridor = 1f;
            if (l.x < 0f) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateA.y));
            else if (l.x > W) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateB.y));
            return GeoUtil.SmoothStep(0f, 30f, dz) * 15f + GeoUtil.SmoothStep(0f, 26f, dx) * 12f * corridor;
        }

        private Vector2 OpenMeadow()
        {
            var r = R("meadow");
            Vector2 best = M.Road[M.Road.Count / 3];
            float bestScore = -1f;
            for (int k = 0; k < 120; k++)
            {
                var c = new Vector2(r.Range(M.Width * 0.12f, M.Width * 0.88f), r.Range(16f, M.Depth - 16f));
                if (M.WaterNear(c.x, c.y, 8f)) continue;
                float score = Mathf.Min(FieldMap.DistToPolyline(c, streamPts), 30f);
                foreach (var p in M.Places) score = Mathf.Min(score, Vector2.Distance(p.Local, c));
                if (Vector2.Distance(c, hillCenter) < hillBack + 6f) continue;
                if (M.DistToPaths(c) < 6f || M.DistToPaths(c) > 24f) continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        private void AddTrail(Vector2 target, float stopShort)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road)
            {
                float d = Vector2.Distance(q, target);
                if (d < bd) { bd = d; near = q; }
            }
            var dir = (target - near).normalized;
            var end = target - dir * stopShort;
            var mid = Vector2.Lerp(near, end, 0.5f) + new Vector2(-dir.y, dir.x) * (bd * 0.12f);
            var pts = CatmullRom(new List<Vector2> { near, mid, end }, 2.5f);
            var t = RoadTargets(pts, 0.14f, M.SampleTerrain(near.x, near.y), float.NaN, 6f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.8f);
            M.Trails.Add(pts);
        }

        private static Vector2 PointAlong(List<Vector2> line, float[] cum, float along)
        {
            along = Mathf.Clamp(along, 0f, cum[cum.Length - 1]);
            for (int i = 1; i < cum.Length; i++)
            {
                if (along > cum[i]) continue;
                float seg = Mathf.Max(1e-4f, cum[i] - cum[i - 1]);
                return Vector2.Lerp(line[i - 1], line[i], (along - cum[i - 1]) / seg);
            }
            return line[line.Count - 1];
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            var l = new Vector2(lx, lz);
            if (flowerRadius > 0f && Vector2.Distance(l, flowerCenter) < flowerRadius) return FieldPaint.Flowers;
            if (lx > M.Width * 0.8f && noise > 0.4f) return FieldPaint.Forest;
            if (lz < 6f || lz > M.Depth - 6f || lx < -2f || lx > M.Width + 2f) return noise > 0.5f ? FieldPaint.Forest : FieldPaint.Grass;
            return noise > 0.56f ? FieldPaint.Meadow : FieldPaint.Grass;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            // 木：縁の林と、湖の森に近づくほど増える林。草原の中にはまばら
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                bool outside = !M.InsidePlayable(l, 0f);
                float edge = Mathf.Min(l.y, D - l.y);
                float density = outside ? 0.85f : edge < 14f ? 0.55f : 0.05f + 0.5f * GeoUtil.SmoothStep(W * 0.62f, W, l.x);
                if (Vector2.Distance(l, flowerCenter) < flowerRadius + 3f) density *= 0.05f;
                if (Vector2.Distance(l, hillCenter) < hillBack * 0.5f) density *= 0.2f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 34f, true)) continue;
                if (outside && Mathf.Abs(l.y - gateA.y) < 7f && l.x < 0f) continue;
                if (outside && Mathf.Abs(l.y - gateB.y) < 7f && l.x > W) continue;
                float forest = GeoUtil.SmoothStep(W * 0.6f, W, l.x);
                var kind = r.Chance(forest * 0.6f) ? NatureKind.TreeTall : r.Chance(0.12f) ? NatureKind.TreeLollipop : NatureKind.TreeRound;
                AddNature(kind, l, r.Range(0.85f, 1.3f), r, forest > 0.5f ? AreaType.Forest : AreaType.Meadow);
            }
            // 果樹（果樹の段に4〜5本）
            if (terraceY > 0f)
            {
                int fruit = r.Range(4, 6);
                for (int k = 0; k < fruit; k++)
                {
                    float a = k / (float)fruit * Mathf.PI * 2f + r.Range(-0.2f, 0.2f);
                    var l = terraceCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r.Range(3.5f, 6.5f);
                    AddNature(NatureKind.TreeLollipop, l, r.Range(1f, 1.2f), r, AreaType.Meadow);
                }
            }
            // 花：花畑はぎっしり、ほかはまばら
            for (int k = 0; k < 900; k++)
            {
                var l = flowerCenter + r.InsideUnitCircle() * flowerRadius;
                if (!NatureOk(l, 1.6f, 1.2f, 30f)) continue;
                AddNature(NatureKind.Flower, l, r.Range(0.8f, 1.25f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 500; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2f, 1.2f, 30f)) continue;
                AddNature(NatureKind.Flower, l, r.Range(0.7f, 1.1f), r, AreaType.Meadow);
            }
            // 丘の上の花の輪（隠れた場所）
            for (int k = 0; k < 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f;
                var l = hillCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 3.2f;
                AddNature(NatureKind.Flower, l, 1.1f, r, AreaType.Meadow);
            }
            // 草・茂み・岩
            for (int k = 0; k < 1300; k++)
            {
                var l = new Vector2(r.Range(-6f, W + 6f), r.Range(-6f, D + 6f));
                if (!NatureOk(l, 1.8f, 0.8f, 36f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 200; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 3f, 1.5f, 32f)) continue;
                if (Vector2.Distance(l, flowerCenter) < flowerRadius) continue;
                AddNature(NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 160; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                bool nearStream = FieldMap.DistToPolyline(l, streamPts) < 6f;
                if (!nearStream && !r.Chance(0.3f)) continue;
                if (!NatureOk(l, 3f, 1.1f, 40f)) continue;
                AddNature(r.Chance(0.2f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.7f, 1.3f), r, AreaType.Riverside);
            }
            // 水辺の葦
            for (int k = 0; k < 160; k++)
            {
                Vector2 l;
                if (k < 70 && M.Ponds.Count > 0)
                {
                    var pd = M.Ponds[0];
                    float a = r.Range(0f, Mathf.PI * 2f);
                    l = pd.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (pd.Radius + r.Range(0.6f, 2f));
                }
                else l = streamPts[r.Range(0, streamPts.Count)] + r.InsideUnitCircle() * 3.4f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None) continue;
                if (M.DistToPaths(l) < 2.5f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.2f), r, AreaType.Riverside);
            }
            // 湖の森に近い林のキノコと倒木
            for (int k = 0; k < 60; k++)
            {
                var l = new Vector2(r.Range(W * 0.78f, W), r.Range(6f, D - 6f));
                if (!NatureOk(l, 3f, 1.5f, 30f)) continue;
                AddNature(k < 4 ? NatureKind.FallenLog : NatureKind.Mushroom, l, r.Range(0.8f, 1.2f), r, AreaType.Forest);
            }
        }

        protected override void Validate()
        {
            float len = M.RoadLength();
            float secs = len / WorldTuning.RunSpeed;
            if (secs > WorldTuning.FieldRouteMaxSeconds) M.Notes.Add($"本道が長い（{len:0}m・{secs:0}秒）");
            if (M.Streams.Count == 0 || M.Streams[0].Fords.Count == 0) M.Notes.Add("小川の浅瀬がない");
            if (M.Ponds.Count < 1) M.Notes.Add("野の池がない");
            if (CountNature(NatureKind.TreeLollipop, l => Vector2.Distance(l, terraceCenter) < 10f) < 3) M.Notes.Add("果樹が3本未満");
            if (CountNature(NatureKind.Flower, l => Vector2.Distance(l, flowerCenter) < flowerRadius) < 120) M.Notes.Add("花畑の花が少ない");
            int pockets = 0, hidden = 0;
            foreach (var p in M.Places)
            {
                if (p.Pocket) pockets++;
                if (p.Hidden) hidden++;
            }
            if (pockets < 3) M.Notes.Add($"寄り道ポケットが{pockets}か所");
            if (hidden < 1) M.Notes.Add("隠れた場所がない");
            // 隠れた場所は本道側から直接は登れない（崖）こと：本道に向く側の斜面が急か
            float steep = 0f;
            for (int k = -1; k <= 1; k++)
            {
                var dir = (Vector2)(Quaternion.Euler(0f, 0f, k * 18f) * hillFront);
                var q = hillCenter + dir * (hillFrontR * 0.7f);
                steep += M.Slope(q.x, q.y) / 3f;
            }
            if (steep < 40f) M.Notes.Add($"丘の崖がゆるい（{steep:0}°）");
            // 段丘の段（§5：2〜4m の飛び降りられる段）と高低差
            int n = 0, cliff = 0;
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                if (!M.InsidePlayable(l, 4f)) continue;
                n++;
                mn = Mathf.Min(mn, M.H[i]);
                mx = Mathf.Max(mx, M.H[i]);
                if (Vector3.Angle(M.Normal(i % M.NX, i / M.NX), Vector3.up) >= 45f) cliff++;
            }
            if (cliff < n * 0.004f) M.Notes.Add($"段丘の段が少ない（{100f * cliff / Mathf.Max(1, n):0.0}%）");
            if (mx - mn < 12f) M.Notes.Add($"高低差が小さい（{mx - mn:0}m）");
        }
    }

    // =====================================================================
    // R2 湖の森（§2.3 R2・§5・§8.3。v0.2 の湖の条件をここに置く）
    // =====================================================================

    internal sealed class LakeForestGen : FieldMapGenBase
    {
        private Vector2 gate;
        private float hG;
        private LakeInfo lake;
        private float south;
        private float sgn;
        private Vector2 outletShore;
        private List<Vector2> outletPts;
        private List<Vector2> inletPts;
        private List<Vector2> valleyPts;
        private Vector2 gazeboShore;
        private Vector2 quietShore;

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            hG = GateHeight(StarSkeleton.FlowerPath);
            gate = new Vector2(W * 0.5f + r.Range(-48f, 48f), 0f);

            // 湖（差し渡し90m以上・最深部9〜11.5m・水面は骨格の値）
            float surface = StarSkeleton.LakeSurface;
            var c = new Vector2(W * 0.5f + r.Range(-22f, 22f), D * 0.5f + r.Range(-8f, 12f));
            lake = new LakeInfo
            {
                WaterIndex = -1,
                Center = c,
                BaseRadius = r.Range(55f, 60f),
                A1 = r.Range(0.05f, 0.09f),
                P1 = r.Range(0f, Mathf.PI * 2f),
                A2 = r.Range(0.02f, 0.05f),
                P2 = r.Range(0f, Mathf.PI * 2f),
                Surface = surface,
                MaxDepth = r.Range(9.2f, 11.2f)
            };
            M.Lake = lake;
            south = LakeInfo.Angle(gate - c);
            sgn = r.Chance(0.5f) ? 1f : -1f;
            lake.SpitAngle = south + sgn * r.Range(0.42f, 0.62f);
            lake.ShelfAngle = south + sgn * 1.7f;
            lake.QuietAngle = south + Mathf.PI;
            lake.SteepAngle = lake.QuietAngle - sgn * 0.95f;
            lake.MudAngle = south - sgn * 1.75f;
            lake.SandMudAngle = lake.QuietAngle + sgn * 0.85f;
            // 台地の崖の切れ目（ゆるい坂で台地へ上がれる所）：流れ出しの谷のほかに2か所
            cliffNoise = Noise("cliff");
            gaps.Add(south);
            for (int k = 0; k < 40 && gaps.Count < 3; k++)
            {
                float a = r.Range(0f, Mathf.PI * 2f);
                bool far = true;
                foreach (var gpa in gaps) if (LakeInfo.AngleDiff(a, gpa) < 1.1f) far = false;
                if (far) gaps.Add(a);
            }
            float spitR = lake.RadiusAt(lake.SpitAngle);
            lake.SpitBase = c + lake.Dir(lake.SpitAngle) * (spitR * 1.08f);
            lake.SpitTip = c + lake.Dir(lake.SpitAngle) * (spitR * 0.3f);
            outletShore = c + lake.Dir(south) * (lake.RadiusAt(south) - 1.5f);
            gazeboShore = c + lake.Dir(south - sgn * 0.8f) * (lake.RadiusAt(south - sgn * 0.8f) + 5.5f);
            quietShore = c + lake.Dir(lake.QuietAngle) * (lake.RadiusAt(lake.QuietAngle) + 3f);

            // 流れ出し（湖の南の岸 → 花の野道のゲートの脇）と、流れ込み（苔の谷側の北の端 → 湖）
            var outletEnd = new Vector2(gate.x + sgn * -8f, -FieldMap.Margin);
            outletPts = CatmullRom(new List<Vector2> { outletShore, Vector2.Lerp(outletShore, outletEnd, 0.5f) + new Vector2(r.Range(-10f, 10f), 0f), new Vector2(outletEnd.x, 6f), outletEnd }, 3f);
            lake.HasInlet = true;
            float inletAng = lake.QuietAngle + sgn * 0.55f;
            lake.Inlet = c + lake.Dir(inletAng) * (lake.RadiusAt(inletAng) - 1f);
            var inletStart = new Vector2(Mathf.Clamp(lake.Inlet.x + r.Range(-30f, 30f), 20f, W - 20f), D + FieldMap.Margin);
            inletPts = CatmullRom(new List<Vector2> { inletStart, new Vector2(inletStart.x, D - 8f), Vector2.Lerp(new Vector2(inletStart.x, D - 8f), lake.Inlet, 0.5f) + new Vector2(r.Range(-8f, 8f), 0f), lake.Inlet }, 3f);

            // 高さ：森の台地（35〜50m）、湖の盆地、南の流れ出しの谷
            var plateau = Noise("plateau");
            // 流れ出しの谷：湖の岸からゲートまで、底の平らな谷（幅24m）。小川と本道はこの谷の底を通る
            valleyPts = CatmullRom(new List<Vector2> { outletShore, Vector2.Lerp(outletShore, new Vector2(gate.x, 10f), 0.5f), new Vector2(gate.x, 10f), new Vector2(gate.x, -FieldMap.Margin) }, 3f);
            Nearest(valleyPts, 70f, out var oDist, out var oAlong);
            var ocum = Cumulative(valleyPts);
            float oLen = ocum[ocum.Length - 1];
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float h;
                var v = l - c;
                float ang = LakeInfo.Angle(v);
                float rr = lake.RadiusAt(ang);
                float dShore = v.magnitude - rr;
                float pl = 42f + (plateau.Fbm(l.x * 0.012f, l.y * 0.012f, 4) - 0.5f) * 12f;
                // 岸で段差を作らない（水際の線がぎざぎざにならないよう、岸の内外でなめらかにつなぐ）
                if (dShore < 0f) h = surface + 0.05f - (lake.DepthAt(l) + 0.05f) * GeoUtil.SmoothStep(0f, 6f, -dShore);
                else
                {
                    // 湖のまわりの低地（岸から崖のふもとまで）→ 台地の崖（高さ8〜20m。切れ目はゆるい坂）→ 森の台地（36〜48m。§5：台地の縁に崖）
                    float cliffD = CliffDistance(ang);
                    float lowTop = surface + 3.5f;
                    float low = surface + 0.05f + (lowTop - surface - 0.05f) * GeoUtil.SmoothStep(0f, cliffD, dShore);
                    float width = Mathf.Lerp(3f, 40f, GapAt(ang));
                    h = low + Mathf.Max(0f, pl - lowTop) * GeoUtil.SmoothStep(cliffD - width * 0.5f, cliffD + width * 0.5f, dShore);
                }
                if (lake.InSpit(l))
                {
                    float d = GeoUtil.DistToSegment(l, lake.SpitBase, lake.SpitTip, out _);
                    h = Mathf.Max(h, surface + 0.75f - GeoUtil.SmoothStep(LakeInfo.SpitHalfWidth * 0.55f, LakeInfo.SpitHalfWidth, d) * 1.6f);
                }
                // 流れ出しの谷（湖の水面から花の野道のゲートの高さへ下る）
                if (oDist[i] != float.MaxValue && dShore > -2f)
                {
                    float t = Mathf.Clamp01(oAlong[i] / oLen);
                    float floor = Mathf.Lerp(surface + 0.35f, hG, GeoUtil.SmoothStep(0f, 1f, t));
                    h = Mathf.Min(h, floor + Mathf.Max(0f, oDist[i] - 12f) * 0.42f);
                }
                h += RimAt(l);
                M.H[i] = h;
            }
            FlattenDisc(gate, 10f, 10f, hG);

            // 本道：ゲート → 谷 → 湖の南の岸 → 岸沿い → 岬の付け根 → 岬の先
            var shoreA = c + lake.Dir(south + sgn * 0.12f) * (lake.RadiusAt(south) + 8f);
            var road = new List<Vector2> { new Vector2(gate.x, 0f), new Vector2(gate.x + sgn * 4f, 14f) };
            var valleyMid = Vector2.Lerp(new Vector2(gate.x, 14f), shoreA, 0.5f) + new Vector2(sgn * 6f, 0f);
            road.Add(valleyMid);
            road.Add(shoreA);
            int arcN = 4;
            float a0 = south + sgn * 0.12f, a1 = lake.SpitAngle - sgn * 0.12f;
            for (int k = 1; k <= arcN; k++)
            {
                float a = Mathf.Lerp(a0, a1, k / (float)arcN);
                road.Add(c + lake.Dir(a) * (lake.RadiusAt(a) + 8.5f));
            }
            road.Add(lake.SpitBase);
            road.Add(Vector2.Lerp(lake.SpitBase, lake.SpitTip, 0.55f));
            road.Add(Vector2.Lerp(lake.SpitBase, lake.SpitTip, 0.88f));
            M.Road.AddRange(CatmullRom(road, 3f));
            // 流れ出しの小川（湖の水面から下る）と、流れ込みの小川（湖の水面より下がらない）。水面は道を均す前の地形で決める
            var outlet = CarveStream("stream.r2.outlet", outletPts, r.Range(1.4f, 1.8f), 0.45f, surface - 0.05f);
            var inlet = CarveStream("stream.r2.inlet", inletPts, r.Range(1.1f, 1.4f), 0.4f, float.NaN, surface);
            // 流れ出しが本道と交わる所は浅瀬。小川を先に掘ってから、本道を最後に均す
            var rcum = Cumulative(M.Road);
            var ocum2 = Cumulative(outletPts);
            var outCross = Crossings(M.Road, outletPts);
            foreach (var (ri, si) in outCross)
                for (int k = 0; k < outletPts.Count; k++)
                    if (Mathf.Abs(ocum2[k] - si) < 4.5f)
                    {
                        outlet.Depth[k] = 0.2f;
                        if (!outlet.Fords.Contains(k)) outlet.Fords.Add(k);
                    }
            var roadT = RoadTargets(M.Road, 0.08f, hG, surface + 0.75f);
            var pinned = new bool[M.Road.Count];
            pinned[pinned.Length - 1] = true;
            PinEnds(roadT, rcum, pinned, hG, float.NaN);
            // 湖のそばと岬の上は水面より上に保つ（谷の中はゲートの高さへ下がってよい）
            for (int k = 0; k < M.Road.Count; k++)
                if (lake.K(M.Road[k]) < 1.3f)
                {
                    roadT[k] = Mathf.Max(roadT[k], surface + 0.6f);
                    pinned[k] = true;
                }
            ClampGrade(roadT, rcum, 0.08f, pinned);
            // 浅瀬で道と小川の高さを合わせる
            SettleFords(outlet, outCross, roadT, rcum, pinned);
            ClampGrade(roadT, rcum, 0.08f, pinned);
            ApplyStream(outlet);
            ApplyStream(inlet);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 湖の水面（岬の陸は水面より上なので水にならない）
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                if (lake.K(l) < 1.03f && M.H[i] < surface - 0.02f)
                {
                    M.WaterY[i] = surface;
                    M.Water[i] = FieldWater.Lake;
                }
            }

            // 東屋と桟橋（湖畔。静かな岸は東屋の反対側）
            var gzDir = (c - gazeboShore).normalized;
            FlattenDisc(gazeboShore, 3.8f, 2.6f, Mathf.Max(surface + 0.7f, M.SampleTerrain(gazeboShore.x, gazeboShore.y)));
            AddStructure(FieldStructureKind.Gazebo, gazeboShore, YawOf(gzDir));
            var pierStart = c + lake.Dir(south - sgn * 0.8f) * (lake.RadiusAt(south - sgn * 0.8f) + 1.5f);
            AddStructure(FieldStructureKind.Pier, pierStart, YawOf(gzDir), 1f, null, surface + 0.55f);
            AddTrail(gazeboShore + (gazeboShore - c).normalized * 1.5f);

            // 森の池2（湧水口＝ゲート側の明るい林、泥底＝湖の向こうの湿った森）
            int ponds = 0;
            for (int k = 0; k < 400 && ponds < 2; k++)
            {
                var pc = new Vector2(r.Range(26f, W - 26f), r.Range(26f, D - 26f));
                if (North(pc) != (ponds == 1)) continue;
                var v = pc - c;
                // 台地の上（崖のふもとより 10m 以上奥）
                if (v.magnitude - lake.RadiusAt(LakeInfo.Angle(v)) < CliffDistance(LakeInfo.Angle(v)) + 10f) continue;
                if (M.DistToPaths(pc) < 18f) continue;
                if (FieldMap.DistToPolyline(pc, inletPts) < 16f || FieldMap.DistToPolyline(pc, outletPts) < 16f) continue;
                bool far = true;
                foreach (var o in M.Ponds) if (Vector2.Distance(o.Center, pc) < 45f) far = false;
                if (!far) continue;
                if (M.Slope(pc.x, pc.y) > 14f) continue;
                CarvePond(pc, r.Range(6f, 8.5f), r.Range(1.2f, 1.6f));
                ponds++;
            }
            if (ponds < 2) M.Notes.Add($"森の池が{ponds}つ");
            if (M.Ponds.Count > 0) AddTrail(M.Ponds[0].Center + (M.Road[M.Road.Count / 3] - M.Ponds[0].Center).normalized * (M.Ponds[0].Radius + 2.5f));

            // 脇道・東屋で本道の脇を触ったので、本道の上だけもう一度均す
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            AddGate(StarSkeleton.FlowerPath, new Vector2(gate.x, 0f), Vector2.up);
            AddStructure(FieldStructureKind.Signpost, new Vector2(gate.x - sgn * 5f, 13f), 0f, 1f, StarSkeleton.FlowerPath);
            SettleHeights();

            AddPlace("r2.cape", "湖の岬の先", Vector2.Lerp(lake.SpitBase, lake.SpitTip, 0.88f));
            AddPlace("r2.gazebo", "湖畔の東屋", gazeboShore);
            AddPlace("r2.quiet", "静かな岸", quietShore, pocket: true);
            AddPlace("r2.inlet", "流れ込みの口", lake.Inlet + (lake.Inlet - c).normalized * 4f, pocket: true);
            for (int k = 0; k < M.Ponds.Count; k++)
            {
                var pd = M.Ponds[k];
                var toward = (gate - pd.Center).normalized;
                AddPlace("r2.pond" + (k + 1), k == 0 ? "森の池（湧水口）" : "森の池（泥底）", pd.Center + toward * (pd.Radius + 2.8f), pocket: true);
            }
        }

        private WorldNoise cliffNoise;
        private readonly List<float> gaps = new List<float>();

        /// <summary>岸から台地の崖のふもとまでの距離（遠浅の側は広い低地、急深の側は崖が岸に迫る）。</summary>
        private float CliffDistance(float ang)
        {
            float d = 30f + (cliffNoise.Fbm(Mathf.Cos(ang) * 1.3f + 5f, Mathf.Sin(ang) * 1.3f + 5f, 3) - 0.5f) * 16f;
            if (LakeInfo.AngleDiff(ang, lake.ShelfAngle) < 0.9f) d += 18f * (1f - LakeInfo.AngleDiff(ang, lake.ShelfAngle) / 0.9f);
            if (LakeInfo.AngleDiff(ang, lake.SteepAngle) < 0.8f) d -= 12f * (1f - LakeInfo.AngleDiff(ang, lake.SteepAngle) / 0.8f);
            return Mathf.Max(14f, d);
        }

        private float GapAt(float ang)
        {
            float best = 0f;
            foreach (var a in gaps)
            {
                float d = LakeInfo.AngleDiff(ang, a) / 0.2f;
                best = Mathf.Max(best, Mathf.Exp(-d * d));
            }
            return best;
        }

        private float RimAt(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dx = Mathf.Max(0f, Mathf.Max(6f - l.x, l.x - (W - 6f)));
            float dz = Mathf.Max(0f, Mathf.Max(-l.y, l.y - (D - 6f)));
            float corridor = 1f;
            if (l.y < 0f) corridor = GeoUtil.SmoothStep(6f, 22f, Mathf.Abs(l.x - gate.x));
            return GeoUtil.SmoothStep(0f, 34f, dx) * 18f + GeoUtil.SmoothStep(0f, 30f, dz) * 16f * corridor;
        }

        private void AddTrail(Vector2 target)
        {
            // 脇道は湖の外の本道からだけ分かれる（岬の先から湖を横切る道にしない）
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road)
            {
                if (lake.K(q) < 1.12f || lake.InSpit(q)) continue;
                float d = Vector2.Distance(q, target);
                if (d < bd) { bd = d; near = q; }
            }
            var dir = (target - near).normalized;
            var mid = Vector2.Lerp(near, target, 0.5f) + new Vector2(-dir.y, dir.x) * (bd * 0.1f);
            var pts = CatmullRom(new List<Vector2> { near, mid, target }, 2.5f);
            var t = RoadTargets(pts, 0.14f, M.SampleTerrain(near.x, near.y), float.NaN, 6f);
            for (int k = 0; k < t.Length; k++) t[k] = Mathf.Max(t[k], StarSkeleton.LakeSurface + 0.5f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.8f);
            M.Trails.Add(pts);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            var l = new Vector2(lx, lz);
            float k = lake.K(l);
            if (k < 1.12f && LakeInfo.AngleDiff(LakeInfo.Angle(l - lake.Center), lake.MudAngle) < 0.5f) return FieldPaint.Mud;
            if (k < 1.1f) return FieldPaint.Sand;
            // 北（湖の向こう）は湿った森、南（ゲート側）は明るい林（境目は波打たせる）
            bool north = North(l);
            if (north) return noise > 0.35f ? FieldPaint.WetForest : FieldPaint.Forest;
            return noise > 0.62f ? FieldPaint.Forest : noise > 0.45f ? FieldPaint.Meadow : FieldPaint.Grass;
        }

        private WorldNoise edgeNoise;

        /// <summary>湖の向こう（湿った森）側か。境目はまっすぐにせず、ゆるく波打たせる。</summary>
        private bool North(Vector2 l)
        {
            if (edgeNoise == null) edgeNoise = Noise("forest_edge");
            var q = l - lake.Center;
            var along = new Vector2(-lake.Dir(lake.QuietAngle).y, lake.Dir(lake.QuietAngle).x);
            float wobble = (edgeNoise.Fbm(Vector2.Dot(q, along) * 0.02f, 0.37f, 3) - 0.5f) * 70f;
            return Vector2.Dot(q, lake.Dir(lake.QuietAngle)) > wobble;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 2600; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                bool outside = !M.InsidePlayable(l, 0f);
                float shore = (l - lake.Center).magnitude - lake.RadiusAt(LakeInfo.Angle(l - lake.Center));
                if (shore < 4f) continue;
                bool north = North(l);
                float density = outside ? 0.9f : north ? 0.5f : 0.28f;
                if (shore < 12f) density *= 0.35f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 34f, true)) continue;
                if (outside && l.y < 0f && Mathf.Abs(l.x - gate.x) < 9f) continue;
                NatureKind kind = north ? (r.Chance(0.2f) ? NatureKind.TreeMushroom : NatureKind.TreeTall) : (r.Chance(0.2f) ? NatureKind.TreeTall : NatureKind.TreeRound);
                AddNature(kind, l, r.Range(0.9f, 1.4f), r, AreaType.Forest);
            }
            // 明るい林の果樹（4本以上）
            int fruit = 0;
            for (int k = 0; k < 200 && fruit < 6; k++)
            {
                var l = new Vector2(r.Range(14f, W - 14f), r.Range(14f, D * 0.5f));
                if (North(l)) continue;
                if (!NatureOk(l, 4f, 3f, 24f)) continue;
                if (M.DistToPaths(l) > 22f) continue;
                AddNature(NatureKind.TreeLollipop, l, r.Range(1f, 1.2f), r, AreaType.Forest);
                fruit++;
            }
            // 湿った森の倒木（5以上）・キノコ・光るキノコ
            int logs = 0;
            for (int k = 0; k < 300 && logs < 7; k++)
            {
                var l = new Vector2(r.Range(10f, W - 10f), r.Range(D * 0.45f, D - 10f));
                if (!North(l)) continue;
                if (!NatureOk(l, 4f, 3f, 20f)) continue;
                AddNature(NatureKind.FallenLog, l, r.Range(0.9f, 1.2f), r, AreaType.Forest);
                logs++;
            }
            for (int k = 0; k < 260; k++)
            {
                var l = new Vector2(r.Range(6f, W - 6f), r.Range(6f, D - 6f));
                if (!North(l) && !r.Chance(0.15f)) continue;
                if (!NatureOk(l, 2f, 1.5f, 30f)) continue;
                AddNature(r.Chance(0.18f) ? NatureKind.GlowMushroom : NatureKind.Mushroom, l, r.Range(0.8f, 1.2f), r, AreaType.Forest);
            }
            // 下草・茂み・花・岩
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.8f, 0.9f, 36f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Forest);
            }
            for (int k = 0; k < 320; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                if (North(l)) AddNature(NatureKind.Bush, l, r.Range(0.8f, 1.3f), r, AreaType.Forest);
                else AddNature(r.Chance(0.6f) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 160; k++)
            {
                float a = lake.SteepAngle + r.Range(-0.8f, 0.8f);
                var l = k < 90 ? lake.Center + lake.Dir(a) * (lake.RadiusAt(a) + r.Range(1.5f, 10f)) : new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 3f, 0.9f, 45f)) continue;
                AddNature(r.Chance(0.25f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.4f), r, AreaType.Riverside);
            }
            // 遠浅と泥の入り江の葦
            for (int k = 0; k < 240; k++)
            {
                float a = (k % 2 == 0 ? lake.ShelfAngle : lake.MudAngle) + r.Range(-0.5f, 0.5f);
                var l = lake.Center + lake.Dir(a) * (lake.RadiusAt(a) + r.Range(0.4f, 3f));
                if (M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2.5f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.25f), r, AreaType.Riverside);
            }
        }

        protected override void Validate()
        {
            float minR = float.MaxValue;
            for (int k = 0; k < 72; k++) minR = Mathf.Min(minR, lake.RadiusAt(k / 72f * Mathf.PI * 2f));
            if (minR * 2f < WorldTuning.LakeMinDiameter) M.Notes.Add($"湖の差し渡しが{minR * 2f:0}m");
            if (lake.MaxDepth < 9f || lake.MaxDepth > 11.5f) M.Notes.Add($"湖の最深部が{lake.MaxDepth:0.0}m");
            if (Vector2.Distance(lake.SpitTip, lake.Center) > lake.BaseRadius * 0.4f) M.Notes.Add("岬が最深部に届かない");
            if (M.Ponds.Count < 2) M.Notes.Add("森の池が2つ未満");
            if (CountNature(NatureKind.FallenLog) < 5) M.Notes.Add("倒木が5未満");
            if (CountNature(NatureKind.TreeLollipop) < 4) M.Notes.Add("果樹が4本未満");
            if (M.DistToPaths(quietShore, false) < WorldTuning.QuietShoreClearance) M.Notes.Add("静かな岸が本道に近い");
            float len = M.RoadLength();
            if (len / WorldTuning.RunSpeed > WorldTuning.FieldRegionMaxSeconds) M.Notes.Add($"ゲートから岬まで遠い（{len:0}m）");
            // 岬の先から最深部の近くが狙えるか（岬の先の前の水深）
            var probe = Vector2.Lerp(lake.SpitTip, lake.Center, 0.6f);
            if (lake.DepthAt(probe) < lake.MaxDepth * 0.8f) M.Notes.Add("岬の先の前が浅い");
            // 台地の崖（§5）：遊べる範囲の 0.8% 以上が 45°以上の斜面
            int n = 0, steep = 0;
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                if (!M.InsidePlayable(l, 4f)) continue;
                n++;
                if (Vector3.Angle(M.Normal(i % M.NX, i / M.NX), Vector3.up) >= 45f) steep++;
            }
            if (steep < n * 0.008f) M.Notes.Add($"台地の崖が少ない（{100f * steep / Mathf.Max(1, n):0.0}%）");
        }
    }
}
