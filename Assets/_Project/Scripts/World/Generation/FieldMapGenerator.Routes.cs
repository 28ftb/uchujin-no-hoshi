using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 道中マップの共通の作り（ゲートは長さの両端。地形 → 本道 → 小川・池 → 道をならす → 脇道・東屋・道標 → 検証）。
    /// 先に起伏のある地形を作り、道は通る所だけ削って坂にする（ユーザー指示 2026-10-17）。
    /// </summary>
    internal abstract class RouteGenBase : FieldMapGenBase
    {
        protected Vector2 gateA, gateB;
        protected float hA, hB;
        protected List<Vector2> streamPts;
        protected readonly List<(Vector2 c, float r, float depth)> pendingPonds = new List<(Vector2, float, float)>();

        protected abstract string EndA { get; }
        protected abstract string EndB { get; }
        protected virtual float RoadComfort => 0.08f;
        protected virtual float GateSpread => 0.15f;
        protected abstract void Shape(SeededRandom r);
        protected abstract float HeightAt(Vector2 l, float baseH, float t);
        protected virtual float PathExtra(int j) => 0f;
        protected virtual void BeforeRoadHeights(SeededRandom r) { }
        protected virtual void AfterFlatten(SeededRandom r) { }
        protected virtual float StreamRadius(SeededRandom r) => r.Range(1.2f, 1.6f);
        protected virtual float StreamDepth => 0.4f;
        protected virtual bool WantGazebo => true;
        protected virtual float MinRelief => 12f;
        protected virtual float MinCliff => 1f;
        protected virtual void ValidateExtra() { }

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            M.RoadGrade = RoadComfort;
            hA = GateHeight(EndA);
            hB = GateHeight(EndB);
            gateA = new Vector2(0f, D * 0.5f + r.Range(-GateSpread, GateSpread) * D);
            gateB = new Vector2(W, D * 0.5f + r.Range(-GateSpread, GateSpread) * D);
            Shape(r);
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float t = Mathf.Clamp01(l.x / W);
                float baseH = Mathf.Lerp(hA, hB, GeoUtil.SmoothStep(0f, 1f, t));
                M.H[i] = HeightAt(l, baseH, t) + Rim(l);
            }
            FlattenDisc(gateA, 10f, 10f, hA);
            FlattenDisc(gateB, 10f, 10f, hB);

            float[] wet = null;
            if (streamPts != null) Nearest(streamPts, 10f, out wet, out _);
            var road = FindPath(gateA, gateB, j =>
            {
                float extra = PathExtra(j);
                if (wet != null && wet[j] != float.MaxValue) extra += wet[j] < 4f ? 12f : (10f - wet[j]) * 0.15f;
                float z = M.ZOf(j / M.NX);
                float edge = Mathf.Min(z, D - z);
                if (edge < 12f) extra += (12f - edge) * 0.35f;
                return extra;
            }, RoadComfort);
            if (road == null)
            {
                M.Notes.Add("本道の経路が見つからない");
                road = CatmullRom(new List<Vector2> { gateA, new Vector2(W * 0.5f, D * 0.5f), gateB }, 3f);
            }
            M.Road.AddRange(road);

            FieldStream stream = null;
            if (streamPts != null) stream = CarveStream("stream." + id, streamPts, StreamRadius(r), StreamDepth);
            BeforeRoadHeights(r);
            var roadT = PlanRoadHeights(M.Road, hA, hB, RoadComfort, M.Streams, r);
            foreach (var st in M.Streams) ApplyStream(st);
            SettleStones();
            foreach (var p in pendingPonds) CarvePond(p.c, p.r, p.depth);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);
            AfterFlatten(r);

            if (WantGazebo)
            {
                var rcum = Cumulative(M.Road);
                float midAlong = rcum[rcum.Length - 1] * r.Range(0.4f, 0.6f);
                var mid = PointOn(M.Road, rcum, midAlong);
                var mdir = (PointOn(M.Road, rcum, midAlong + 2f) - mid).normalized;
                var g1 = mid + new Vector2(-mdir.y, mdir.x) * 6.5f;
                var g2 = mid - new Vector2(-mdir.y, mdir.x) * 6.5f;
                float s1 = M.Slope(g1.x, g1.y) + (M.WaterNear(g1.x, g1.y, 4f) ? 90f : 0f);
                float s2 = M.Slope(g2.x, g2.y) + (M.WaterNear(g2.x, g2.y, 4f) ? 90f : 0f);
                var gz = s1 <= s2 ? g1 : g2;
                FlattenDisc(gz, 3.6f, 2.5f, M.SampleTerrain(gz.x, gz.y));
                AddStructure(FieldStructureKind.Gazebo, gz, YawOf(mid - gz));
                AddPlace(id + ".gazebo", "東屋", gz);
            }
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            AddGate(EndA, new Vector2(0f, gateA.y), Vector2.right);
            AddGate(EndB, new Vector2(W, gateB.y), Vector2.left);
            AddStructure(FieldStructureKind.Signpost, gateA + new Vector2(14f, 4.2f), 0f, 1f, EndB);
            AddStructure(FieldStructureKind.Signpost, gateB + new Vector2(-14f, -4.2f), 0f, 1f, EndA);
            SettleHeights();
            Places(r);
        }

        protected virtual void Places(SeededRandom r) { }

        protected virtual float Rim(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dz = Mathf.Max(0f, Mathf.Max(8f - l.y, l.y - (D - 8f)));
            float dx = Mathf.Max(0f, Mathf.Max(-l.x, l.x - W));
            float corridor = 1f;
            if (l.x < 0f) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateA.y));
            else if (l.x > W) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateB.y));
            if (streamPts != null) corridor = Mathf.Min(corridor, GeoUtil.SmoothStep(5f, 14f, FieldMap.DistToPolyline(l, streamPts)) + (M.InsidePlayable(l) ? 1f : 0f));
            return GeoUtil.SmoothStep(0f, 30f, dz) * 16f + GeoUtil.SmoothStep(0f, 26f, dx) * 14f * Mathf.Clamp01(corridor);
        }

        /// <summary>本道（か脇道）から target への脇道。両端を地面の高さに固定し、間をならす（急でもよい。歩いて登れる 45% まで）。</summary>
        protected void Trail(Vector2 target)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            if (bd < 5f) return;
            // まず歩いて登れる斜面だけで探し（崖を削って登らない）、無ければ段差を坂にして通す
            var pts = FindPath(near, target, j => !float.IsNaN(M.WaterY[j]) && M.WaterY[j] > M.H[j] + 0.4f ? 20f : 0f, 0.2f, 3f, true)
                ?? FindPath(near, target, j => !float.IsNaN(M.WaterY[j]) && M.WaterY[j] > M.H[j] + 0.4f ? 20f : 0f, 0.2f);
            if (pts == null) return;
            var t = RoadTargets(pts, 0.45f, M.SampleTerrain(near.x, near.y), M.SampleTerrain(target.x, target.y), 6f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.6f);
            M.Trails.Add(pts);
        }

        protected Vector2 NearestDry(Vector2 p, float max = 30f)
        {
            for (float rr = 0f; rr <= max; rr += 1f)
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (M.InsidePlayable(q, 3f) && M.WaterAt(q.x, q.y) == FieldWater.None && M.Slope(q.x, q.y) < 30f) return q;
                }
            return p;
        }

        /// <summary>流れの線（外はまっすぐ、中で蛇行）。from は x=W 側（R2 側など）の端の z、to は x=0 側。</summary>
        protected List<Vector2> MeanderLine(float zAtW, float zAt0, float amp, float waves, float phase, float zMin, float zMax)
        {
            var pts = new List<Vector2>();
            float W = M.Width;
            for (float x = W + FieldMap.Margin; x >= -FieldMap.Margin - 0.01f; x -= 3f)
            {
                float u = Mathf.Clamp01((W - x) / W);
                float y = Mathf.Lerp(zAtW, zAt0, GeoUtil.SmoothStep(0f, 1f, u)) + Mathf.Sin(u * Mathf.PI * waves + phase) * amp * Mathf.Sin(u * Mathf.PI);
                pts.Add(new Vector2(x, Mathf.Clamp(y, zMin, zMax)));
            }
            return pts;
        }

        protected override void Validate()
        {
            float len = M.RoadLength();
            if (len / WorldTuning.RunSpeed > WorldTuning.FieldRouteMaxSeconds) M.Notes.Add($"本道が長い（{len:0}m）");
            int pockets = 0, hidden = 0;
            foreach (var p in M.Places) { if (p.Pocket) pockets++; if (p.Hidden) hidden++; }
            if (pockets < 3) M.Notes.Add($"寄り道ポケットが{pockets}か所");
            if (hidden < 1) M.Notes.Add("隠れた場所がない");
            RequireRelief(MinRelief, MinCliff);
            ValidateExtra();
        }
    }

    // =====================================================================
    // T4 苔の谷（湖の森 ⇄ 滝の山。§3.1・§5・§8.3）
    // 両側に高さ 18〜25m の岩壁が立つ深い谷。谷底を沢が下り、湧き水の窪地（冷たい湧水）・静かな池・苔の倒木・光るキノコ・
    // こだまの岩壁。岩壁の切れ目から谷の上へ上がれる（谷の上の岩＝隠れた場所）。
    // =====================================================================

    internal sealed class MossValleyGen : RouteGenBase
    {
        protected override string EndA => StarSkeleton.LakeForest;
        protected override string EndB => StarSkeleton.WaterfallMountain;
        protected override float MinRelief => 22f;
        protected override float MinCliff => 3f;
        private float wallH, floorHalf, wallWidth;
        private List<Vector2> axis;
        private float gapX, gapSide;
        private Vector2 springC, quietC, echo, topRock;
        private WorldNoise floorNoise;

        private float AxisZ(float x)
        {
            float best = float.MaxValue, z = M.Depth * 0.5f;
            foreach (var p in axis) { float d = Mathf.Abs(p.x - x); if (d < best) { best = d; z = p.y; } }
            return z;
        }

        protected override void Shape(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            wallH = r.Range(18f, 25f);
            floorHalf = r.Range(16f, 22f);
            wallWidth = r.Range(6f, 9f);
            axis = MeanderLine(gateB.y, gateA.y, r.Range(6f, 12f), r.Range(1.2f, 2.2f), r.Range(0f, Mathf.PI * 2f), D * 0.35f, D * 0.65f);
            // 沢：谷底を R3 側から R2 側へ下る（谷の軸から少しずれる）
            streamPts = new List<Vector2>();
            // 谷の軸から片側へずらす（ゲートは谷の軸の上なので、ゲートの所では 10m 以上離す）
            float side = r.Chance(0.5f) ? 1f : -1f;
            float off = side * r.Range(10f, 13f), amp = r.Range(3f, 6f), ph = r.Range(0f, 6.28f);
            foreach (var p in axis) streamPts.Add(new Vector2(p.x, Mathf.Clamp(p.y + off + Mathf.Sin(p.x * 0.05f + ph) * amp * Mathf.Sin(Mathf.Clamp01(p.x / M.Width) * Mathf.PI), 10f, D - 10f)));
            if (hA > hB) streamPts.Reverse();
            gapX = W * r.Range(0.3f, 0.7f);
            gapSide = r.Chance(0.5f) ? 1f : -1f;
        }

        protected override float HeightAt(Vector2 l, float baseH, float t)
        {
            float d = l.y - AxisZ(l.x);
            float ad = Mathf.Abs(d);
            float rise = wallH * GeoUtil.SmoothStep(floorHalf, floorHalf + wallWidth, ad);
            // 岩壁の切れ目（片側だけ、ゆるい坂で谷の上へ）
            if (Mathf.Sign(d) == gapSide)
            {
                float u = (l.x - gapX) / 9f;
                float g = Mathf.Exp(-u * u);
                rise = Mathf.Lerp(rise, wallH * GeoUtil.SmoothStep(floorHalf, floorHalf + wallH / 0.4f, ad), g);
            }
            if (floorNoise == null) floorNoise = Noise("floor");
            return baseH + rise + (floorNoise.Fbm(l.x * 0.03f, l.y * 0.03f, 3) - 0.5f) * 3f;
        }

        protected override void BeforeRoadHeights(SeededRandom r)
        {
            float W = M.Width;
            // 湧き水の窪地（冷たい湧水）と静かな池：谷底の、本道から離れた所
            for (int k = 0; k < 300 && springC == Vector2.zero; k++)
            {
                var c = new Vector2(r.Range(W * 0.15f, W * 0.45f), AxisZ(W * 0.3f) + r.Range(-floorHalf, floorHalf) * 0.7f);
                c.y = AxisZ(c.x) + (c.y - AxisZ(W * 0.3f));
                if (M.DistToPaths(c) < 9f || FieldMap.DistToPolyline(c, streamPts) < 9f) continue;
                springC = c;
            }
            for (int k = 0; k < 300 && quietC == Vector2.zero; k++)
            {
                var c = new Vector2(r.Range(W * 0.55f, W * 0.85f), 0f);
                c.y = AxisZ(c.x) + r.Range(-floorHalf, floorHalf) * 0.7f;
                if (M.DistToPaths(c) < 11f || FieldMap.DistToPolyline(c, streamPts) < 9f) continue;
                quietC = c;
            }
            if (springC != Vector2.zero) pendingPonds.Add((springC, r.Range(4f, 5.5f), 1.4f));
            else M.Notes.Add("湧き水の窪地の場所がない");
            if (quietC != Vector2.zero) pendingPonds.Add((quietC, r.Range(5.5f, 7f), 1.8f));
            else M.Notes.Add("静かな池の場所がない");
        }

        protected override void AfterFlatten(SeededRandom r)
        {
            float W = M.Width;
            // こだまの岩壁：一番高い壁の根もと。谷の上の岩：切れ目から上がった先
            float ex = r.Range(W * 0.2f, W * 0.8f);
            echo = NearestDry(new Vector2(ex, AxisZ(ex) - gapSide * (floorHalf - 2f)), 10f);
            var top = new Vector2(gapX + r.Range(14f, 24f) * (r.Chance(0.5f) ? 1f : -1f), 0f);
            top.y = AxisZ(top.x) + gapSide * (floorHalf + wallWidth + r.Range(6f, 12f));
            topRock = new Vector2(Mathf.Clamp(top.x, 12f, M.Width - 12f), Mathf.Clamp(top.y, 10f, M.Depth - 10f));
            if (springC != Vector2.zero) Trail(springC + (M.Road[M.Road.Count / 3] - springC).normalized * 7f);
            Trail(echo);
            Trail(topRock);
            AddStructure(FieldStructureKind.Crystal, topRock, 0f, 1f);
        }

        protected override void Places(SeededRandom r)
        {
            if (springC != Vector2.zero) AddPlace("t4.spring", "湧き水の窪地", NearestDry(springC + (M.Road[M.Road.Count / 3] - springC).normalized * 7f), pocket: true);
            if (quietC != Vector2.zero)
            {
                Vector2 near = M.Road[0];
                float bd = float.MaxValue;
                foreach (var q in M.Road) { float d = Vector2.Distance(q, quietC); if (d < bd) { bd = d; near = q; } }
                AddPlace("t4.pond", "静かな池のほとり", NearestDry(quietC + (near - quietC).normalized * 9f), pocket: true);
            }
            AddPlace("t4.echo", "こだまの岩壁", echo, pocket: true);
            AddPlace("t4.top", "谷の上の岩（隠れた場所）", topRock, hidden: true);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float ad = Mathf.Abs(lz - AxisZ(lx));
            if (ad < floorHalf) return noise > 0.4f ? FieldPaint.WetForest : FieldPaint.Forest;
            if (ad < floorHalf + wallWidth) return FieldPaint.Rock;
            return FieldPaint.Forest;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 1700; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                float ad = Mathf.Abs(l.y - AxisZ(Mathf.Clamp(l.x, 0f, W)));
                bool outside = !M.InsidePlayable(l, 0f);
                float density = outside ? 0.85f : ad < floorHalf ? 0.3f : ad < floorHalf + wallWidth ? 0.05f : 0.6f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 40f, true)) continue;
                if (outside && (l.x < 0f && Mathf.Abs(l.y - gateA.y) < 7f || l.x > W && Mathf.Abs(l.y - gateB.y) < 7f)) continue;
                AddNature(r.Chance(0.25f) ? NatureKind.TreeMushroom : NatureKind.TreeTall, l, r.Range(0.9f, 1.4f), r, AreaType.Forest);
            }
            // 苔の倒木（5以上）・キノコ・光るキノコ
            int logs = 0;
            for (int k = 0; k < 400 && logs < 8; k++)
            {
                var l = new Vector2(r.Range(8f, W - 8f), 0f);
                l.y = AxisZ(l.x) + r.Range(-floorHalf, floorHalf) * 0.85f;
                if (!NatureOk(l, 3.5f, 2f, 25f)) continue;
                AddNature(NatureKind.FallenLog, l, r.Range(0.9f, 1.3f), r, AreaType.Forest);
                logs++;
            }
            for (int k = 0; k < 420; k++)
            {
                var l = new Vector2(r.Range(4f, W - 4f), 0f);
                l.y = AxisZ(l.x) + r.Range(-floorHalf, floorHalf);
                if (!NatureOk(l, 2f, 1f, 35f)) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.GlowMushroom : NatureKind.Mushroom, l, r.Range(0.8f, 1.3f), r, AreaType.Forest);
            }
            for (int k = 0; k < 1300; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.8f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Forest);
            }
            for (int k = 0; k < 260; k++)
            {
                var l = new Vector2(r.Range(0f, W), 0f);
                l.y = AxisZ(l.x) + (r.Chance(0.5f) ? 1f : -1f) * (floorHalf + r.Range(-3f, wallWidth + 3f));
                if (!NatureOk(l, 3f, 1.5f, 70f)) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.5f), r, AreaType.Mountain);
            }
            for (int k = 0; k < 120; k++)
            {
                var l = streamPts[r.Range(0, streamPts.Count)] + r.InsideUnitCircle() * 4f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2.5f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.1f), r, AreaType.Riverside);
            }
        }

        protected override void ValidateExtra()
        {
            if (CountNature(NatureKind.FallenLog) < 5) M.Notes.Add("苔の倒木が5未満");
            if (CountNature(NatureKind.GlowMushroom) < 8) M.Notes.Add("光るキノコが少ない");
            if (M.Ponds.Count < 2) M.Notes.Add("湧き水の窪地か静かな池がない");
        }
    }

    // =====================================================================
    // T5 尾根道（滝の山 ⇄ 星見の高原。§3.1・§5・§8.3）
    // 細い尾根の上を本道が通る。両側は急斜面。峰のこぶと鞍部、岩の露頭、結晶の露頭、尾根の草地。
    // =====================================================================

    internal sealed class RidgeGen : RouteGenBase
    {
        protected override string EndA => StarSkeleton.WaterfallMountain;
        protected override string EndB => StarSkeleton.Highland;
        protected override float RoadComfort => 0.12f;
        protected override float GateSpread => 0.08f;
        protected override float MinRelief => 25f;
        protected override float MinCliff => 2f;
        private List<Vector2> crest;
        private float crestHalf, sideDrop;
        private readonly List<(float x, float h, float w)> knobs = new List<(float, float, float)>();
        private Vector2 crystalC, knobTop, view;
        private WorldNoise rockNoise;

        private float CrestZ(float x)
        {
            float best = float.MaxValue, z = M.Depth * 0.5f;
            foreach (var p in crest) { float d = Mathf.Abs(p.x - x); if (d < best) { best = d; z = p.y; } }
            return z;
        }

        protected override void Shape(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            crest = MeanderLine(gateB.y, gateA.y, r.Range(8f, 14f), r.Range(1.5f, 2.5f), r.Range(0f, 6.28f), D * 0.3f, D * 0.7f);
            crestHalf = r.Range(6f, 9f);
            sideDrop = r.Range(28f, 38f);
            int n = r.Range(2, 4);
            for (int k = 0; k < n; k++) knobs.Add((W * Mathf.Lerp(0.25f, 0.75f, n == 1 ? 0.5f : k / (float)(n - 1)) + r.Range(-12f, 12f), r.Range(8f, 16f), r.Range(14f, 24f)));
        }

        private float KnobAt(float x)
        {
            float h = 0f;
            foreach (var k in knobs) { float u = (x - k.x) / k.w; h += k.h * Mathf.Exp(-u * u); }
            return h;
        }

        protected override float HeightAt(Vector2 l, float baseH, float t)
        {
            float env = Mathf.Sin(t * Mathf.PI);
            float crestH = baseH + KnobAt(l.x) * env - 3f * Mathf.Sin(t * Mathf.PI * 3f) * env;
            float d = Mathf.Abs(l.y - CrestZ(l.x));
            float fall = sideDrop * Mathf.Pow(GeoUtil.SmoothStep(crestHalf, crestHalf + 30f, d), 0.8f);
            if (rockNoise == null) rockNoise = Noise("rocks");
            return crestH - fall + (rockNoise.Ridged(l.x * 0.05f, l.y * 0.05f, 3) - 0.5f) * 2.5f;
        }

        protected override float PathExtra(int j)
        {
            var l = LocalOf(j);
            return Mathf.Abs(l.y - CrestZ(l.x)) * 0.05f; // 尾根の上を通る
        }

        protected override void AfterFlatten(SeededRandom r)
        {
            float W = M.Width;
            // 結晶の露頭・峰のこぶの上（隠れた場所）・見晴らし
            var kb = knobs[r.Range(0, knobs.Count)];
            knobTop = new Vector2(kb.x, CrestZ(kb.x) + (r.Chance(0.5f) ? 1f : -1f) * (crestHalf + 4f));
            if (M.DistToPaths(knobTop) < 8f) knobTop.y += Mathf.Sign(knobTop.y - CrestZ(kb.x)) * 6f;
            float cx = r.Range(W * 0.2f, W * 0.8f);
            crystalC = new Vector2(cx, CrestZ(cx) + (r.Chance(0.5f) ? 1f : -1f) * (crestHalf + 10f));
            float vx = r.Range(W * 0.3f, W * 0.7f);
            view = new Vector2(vx, CrestZ(vx) + (r.Chance(0.5f) ? 1f : -1f) * (crestHalf + 3f));
            Trail(crystalC);
            Trail(knobTop);
            for (int k = 0; k < 5; k++)
            {
                var p = crystalC + r.InsideUnitCircle() * 6f;
                AddStructure(FieldStructureKind.Crystal, p, r.Range(0f, 360f), r.Range(0.8f, 1.4f));
            }
        }

        protected override void Places(SeededRandom r)
        {
            AddPlace("t5.crystal", "結晶の露頭", NearestDry(crystalC + Vector2.right * 7f), pocket: true);
            AddPlace("t5.view", "尾根の見晴らし", NearestDry(view), pocket: true);
            AddPlace("t5.meadow", "尾根の草地", NearestDry(new Vector2(M.Width * 0.2f, CrestZ(M.Width * 0.2f))), pocket: true);
            AddPlace("t5.knob", "峰のこぶの上（隠れた場所）", NearestDry(knobTop, 8f), hidden: true);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float d = Mathf.Abs(lz - CrestZ(lx));
            if (d < crestHalf + 3f) return noise > 0.55f ? FieldPaint.Rock : FieldPaint.Meadow;
            return noise > 0.5f ? FieldPaint.Rock : FieldPaint.Grass;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 900; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                float d = Mathf.Abs(l.y - CrestZ(Mathf.Clamp(l.x, 0f, W)));
                if (d < crestHalf + 6f) continue; // 尾根の上は木がない（風）
                if (!r.Chance(0.25f)) continue;
                if (!NatureOk(l, 4f, 2f, 45f, true)) continue;
                AddNature(NatureKind.TreeTall, l, r.Range(0.7f, 1.1f), r, AreaType.Mountain);
            }
            for (int k = 0; k < 700; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1f, 65f)) continue;
                AddNature(r.Chance(0.3f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.6f), r, AreaType.Mountain);
            }
            for (int k = 0; k < 1300; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.6f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Hill);
            }
            for (int k = 0; k < 360; k++)
            {
                var l = new Vector2(r.Range(0f, W), 0f);
                l.y = CrestZ(l.x) + r.Range(-crestHalf - 8f, crestHalf + 8f);
                if (!NatureOk(l, 2f, 1f, 35f)) continue;
                AddNature(NatureKind.Flower, l, r.Range(0.8f, 1.2f), r, AreaType.Hill);
            }
            for (int k = 0; k < 40; k++)
            {
                var l = crystalC + r.InsideUnitCircle() * 9f;
                if (!NatureOk(l, 1.5f, 1f, 50f)) continue;
                AddNature(NatureKind.Crystal, l, r.Range(0.8f, 1.3f), r, AreaType.Mountain);
            }
        }
    }

    // =====================================================================
    // R5 星見の高原（§2.3 R5・§5・§8.3）
    // 上が平らな台地。縁は崖。高原の池（静か）、木立（5本以上）、花と風の草地、星見の岩（こぶ）。
    // 型：0＝平らな台地、1＝段々の花畑、2＝池の多い台地。住民は予定では来ない（静かな場所）。
    // =====================================================================

    internal sealed class HighlandGen : FieldMapGenBase
    {
        private Vector2 gateS;
        private float hS, plateau;
        private Vector2 rockC, groveC;
        private readonly List<Vector2> pondCs = new List<Vector2>();
        private float rockH;

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            M.RoadGrade = 0.1f;
            hS = GateHeight(StarSkeleton.Ridge);
            plateau = hS + r.Range(4f, 9f);
            gateS = new Vector2(W * r.Range(0.4f, 0.6f), 0f);
            rockC = new Vector2(W * r.Range(0.3f, 0.7f), D * r.Range(0.6f, 0.8f));
            rockH = r.Range(7f, 11f);
            groveC = new Vector2(W * r.Range(0.2f, 0.8f), D * r.Range(0.3f, 0.6f));
            int ponds = M.Type == 2 ? r.Range(3, 5) : 1;
            for (int k = 0; k < 200 && pondCs.Count < ponds; k++)
            {
                var c = new Vector2(r.Range(40f, W - 40f), r.Range(50f, D - 40f));
                if (Vector2.Distance(c, rockC) < 40f || Vector2.Distance(c, groveC) < 30f) continue;
                bool far = true;
                foreach (var o in pondCs) if (Vector2.Distance(o, c) < 45f) far = false;
                if (far) pondCs.Add(c);
            }
            var roll = Noise("roll");
            float stepH = r.Range(1.8f, 2.6f);
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float h = plateau + (roll.Fbm(l.x * 0.015f, l.y * 0.015f, 3) - 0.5f) * 5f;
                // 段々の花畑（型1）：北へ向かって低い段が続く
                if (M.Type == 1) h += Mathf.Floor(Mathf.Max(0f, l.y - 40f) / 45f) * stepH + GeoUtil.SmoothStep(0.85f, 1f, Mathf.Repeat(Mathf.Max(0f, l.y - 40f) / 45f, 1f)) * stepH;
                // 星見の岩
                float kd = Vector2.Distance(l, rockC) / 14f;
                if (kd < 1f) h += rockH * (1f - GeoUtil.SmoothStep(0.35f, 1f, kd));
                // 南の縁：尾根道から上がってくる坂（ゲートの前は尾根の高さ）
                h = Mathf.Lerp(hS, h, GeoUtil.SmoothStep(4f, 40f, l.y));
                // 台地の縁は崖で落ちる（遊べる範囲の外）
                float edge = Mathf.Min(Mathf.Min(l.x, W - l.x), Mathf.Min(l.y, D - l.y));
                if (edge < 0f)
                {
                    bool ridge = l.y < 0f && Mathf.Abs(l.x - gateS.x) < 12f;
                    h -= ridge ? 0f : GeoUtil.SmoothStep(0f, 10f, -edge) * 26f;
                }
                M.H[i] = h;
            }
            FlattenDisc(gateS, 10f, 10f, hS);
            // 見えない壁の内側に低い縁（落ちない）
            // 本道：南のゲート → 高原の池 → 星見の岩のふもと（行き止まりの地域）
            var pond = pondCs.Count > 0 ? pondCs[0] : new Vector2(W * 0.5f, D * 0.5f);
            var rockFoot = rockC + (gateS - rockC).normalized * 18f;
            var leg = FindPath(gateS, rockFoot, j => 0f, M.RoadGrade);
            if (leg == null) { M.Notes.Add("本道の経路が見つからない"); leg = new List<Vector2> { gateS, rockFoot }; }
            M.Road.AddRange(leg);
            foreach (var c in pondCs) pendingPond(c, r);
            var roadT = PlanRoadHeights(M.Road, hS, M.SampleTerrain(rockFoot.x, rockFoot.y), M.RoadGrade, M.Streams, r);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);
            foreach (var c in pondCs) TrailTo(c + (gateS - c).normalized * 12f);
            TrailTo(groveC);
            TrailTo(rockC);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);
            AddGate(StarSkeleton.Ridge, gateS, Vector2.up);
            AddStructure(FieldStructureKind.Signpost, gateS + Vector2.up * 14f + Vector2.right * 4.2f, 0f, 1f, StarSkeleton.Ridge);
            AddStructure(FieldStructureKind.Crystal, rockC, 0f, 1.4f);
            SettleHeights();
            AddPlace("r5.rock", "星見の岩", rockC, pocket: true);
            AddPlace("r5.grove", "木立", groveC, pocket: true);
            for (int k = 0; k < M.Ponds.Count; k++)
                AddPlace("r5.pond" + k, "高原の池のほとり", M.Ponds[k].Center + (gateS - M.Ponds[k].Center).normalized * (M.Ponds[k].Radius + 3f), pocket: true);
        }

        private void pendingPond(Vector2 c, SeededRandom r) => CarvePond(c, r.Range(7f, 10f), r.Range(1.6f, 2.2f));

        private void TrailTo(Vector2 target)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            if (bd < 5f) return;
            var pts = (FindPath(near, target, j => 0f, 0.2f, 3f, true) ?? FindPath(near, target, j => 0f, 0.2f));
            if (pts == null) return;
            var t = RoadTargets(pts, 0.45f, M.SampleTerrain(near.x, near.y), M.SampleTerrain(target.x, target.y), 6f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.6f);
            M.Trails.Add(pts);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            var l = new Vector2(lx, lz);
            if (Vector2.Distance(l, rockC) < 12f) return FieldPaint.Rock;
            if (Vector2.Distance(l, groveC) < 18f) return FieldPaint.Forest;
            return noise > 0.55f ? FieldPaint.Flowers : FieldPaint.Meadow;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            int grove = 0;
            for (int k = 0; k < 200 && grove < 9; k++)
            {
                var l = groveC + r.InsideUnitCircle() * 16f;
                if (!NatureOk(l, 3f, 2f, 30f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.TreeTall : NatureKind.TreeRound, l, r.Range(1f, 1.4f), r, AreaType.Hill);
                grove++;
            }
            for (int k = 0; k < 700; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2f, 1f, 30f)) continue;
                AddNature(NatureKind.Flower, l, r.Range(0.8f, 1.3f), r, AreaType.Hill);
            }
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.6f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.4f), r, AreaType.Hill);
            }
            for (int k = 0; k < 160; k++)
            {
                var l = rockC + r.InsideUnitCircle() * 16f;
                if (!NatureOk(l, 2f, 1f, 60f)) continue;
                AddNature(r.Chance(0.3f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.5f), r, AreaType.Hill);
            }
            foreach (var pd in M.Ponds)
                for (int k = 0; k < 30; k++)
                {
                    float a = r.Range(0f, Mathf.PI * 2f);
                    var l = pd.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (pd.Radius + r.Range(0.6f, 2.5f));
                    if (M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2f) continue;
                    AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.1f), r, AreaType.Pond);
                }
        }

        protected override void Validate()
        {
            if (M.Ponds.Count < 1) M.Notes.Add("高原の池がない");
            if (CountNature(NatureKind.TreeTall) + CountNature(NatureKind.TreeRound) < 5) M.Notes.Add("木立が5本未満");
            // 平らな台地でも、星見の岩と縁・段で起伏を持たせる
            RequireRelief(9f, 0.3f);
        }
    }
}
