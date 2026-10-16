using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // T3 渓流の坂道（村 ⇄ 滝の山。§3.1・§5・§8.3）
    // 谷底を渓流が下り、谷を横切る段（2〜4m）で小さな滝になる。本道は段の切れ目を左右に折り返して登る（つづら折り）。
    // 谷の壁・渓流沿いの崖・湧水の淵（シモマダラ）・石積みの跡・滝の上の岩棚（隠れた場所）。
    // =====================================================================

    internal sealed class StreamSlopeGen : FieldMapGenBase
    {
        private Vector2 gateA, gateB;
        private float hA, hB;
        private List<Vector2> streamPts;
        private float sideSlope;

        private sealed class Ledge
        {
            public float X, Wobble, Freq, Phase, Height, PassZ, PassWidth, Ramp;
        }

        private readonly List<Ledge> ledges = new List<Ledge>();
        private float stepTotal;
        private float gorgeX0, gorgeX1;
        private Vector2 shelfCenter, shelfFront;
        private float shelfHeight, shelfBack, shelfFrontR;
        private bool shelfOk;

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            M.RoadGrade = WorldTuning.FieldSlopeRoadGrade;
            hA = GateHeight(StarSkeleton.Village);
            hB = GateHeight(StarSkeleton.WaterfallMountain);
            gateA = new Vector2(0f, D * 0.5f + r.Range(-20f, 20f));
            gateB = new Vector2(W, D * 0.5f + r.Range(-20f, 20f));

            // 渓流：滝の山の側から村の側へ下る。外はまっすぐ、中で蛇行
            float sideB = gateB.y > D * 0.5f ? -1f : 1f;
            var s0 = new Vector2(W + FieldMap.Margin, gateB.y + sideB * 14f);
            var s1 = new Vector2(-FieldMap.Margin, Mathf.Clamp(gateA.y - sideB * 16f, 16f, D - 16f));
            float amp = r.Range(12f, 20f), waves = r.Range(1.5f, 2.5f), phase = r.Range(0f, Mathf.PI * 2f);
            streamPts = new List<Vector2>();
            for (float x = s0.x; x >= s1.x - 0.01f; x -= 3f)
            {
                float u = Mathf.Clamp01((W - x) / W);
                float y = Mathf.Lerp(s0.y, s1.y, GeoUtil.SmoothStep(0f, 1f, u));
                y = Mathf.Clamp(y + Mathf.Sin(u * Mathf.PI * waves + phase) * amp * Mathf.Sin(u * Mathf.PI), 16f, D - 16f);
                streamPts.Add(new Vector2(x, y));
            }
            if (hA > hB - 2f) streamPts.Reverse();

            // 谷を横切る段（小さな滝 2〜3）。切れ目（ゆるい坂）は左右に互い違い＝本道は折り返して登る
            int count = r.Range(2, 4);
            float side = r.Chance(0.5f) ? 1f : -1f;
            stepTotal = 0f;
            for (int k = 0; k < count; k++)
            {
                float u = Mathf.Lerp(0.26f, 0.78f, count == 1 ? 0.5f : k / (float)(count - 1)) + r.Range(-0.03f, 0.03f);
                var lg = new Ledge
                {
                    X = W * u,
                    Wobble = r.Range(3f, 7f),
                    Freq = r.Range(0.03f, 0.05f),
                    Phase = r.Range(0f, Mathf.PI * 2f),
                    Height = r.Range(3f, 5f),
                    PassZ = Mathf.Clamp(D * 0.5f + side * r.Range(24f, 32f), 18f, D - 18f),
                    PassWidth = r.Range(8f, 11f)
                };
                lg.Ramp = lg.Height / 0.13f;
                // 切れ目が渓流の真上にあると滝ができないので、渓流から 16m 以上離す
                float sy = StreamYAt(lg.X);
                if (Mathf.Abs(lg.PassZ - sy) < 16f) lg.PassZ = Mathf.Clamp(sy + (lg.PassZ > sy ? 1f : -1f) * 22f, 16f, D - 16f);
                if (Mathf.Abs(lg.PassZ - sy) < 16f) lg.PassZ = Mathf.Clamp(sy - (lg.PassZ > sy ? 1f : -1f) * 22f, 16f, D - 16f);
                side = -side;
                ledges.Add(lg);
                stepTotal += lg.Height;
            }
            if (hB < hA) stepTotal = -stepTotal;
            // 渓流沿いの崖（狭い谷）の区間
            float g0 = r.Range(0.15f, 0.55f);
            gorgeX0 = W * g0;
            gorgeX1 = gorgeX0 + W * r.Range(0.18f, 0.28f);
            sideSlope = r.Range(0.32f, 0.45f);

            // 高さ：全体の登り ＋ 段 ＋ 谷の壁（渓流から離れるほど高い）＋ 狭い谷の崖 ＋ うねり
            var roll = Noise("roll");
            Nearest(streamPts, 200f, out var sDist, out _);
            float floorH = Mathf.Min(hA, hB) - 2f;
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float t = Mathf.Clamp01(l.x / W);
                float baseH = Mathf.Lerp(hA, hB - stepTotal, GeoUtil.SmoothStep(0f, 1f, t)) + LedgesAt(l);
                float d = sDist[i] == float.MaxValue ? 60f : sDist[i];
                float wall = sideSlope * Mathf.Max(0f, d - 5f);
                bool gorge = l.x > gorgeX0 && l.x < gorgeX1;
                // 狭い谷の崖。真ん中に切れ目（渡れる所）を残す
                if (gorge)
                {
                    float gx = (l.x - (gorgeX0 + gorgeX1) * 0.5f) / 7f;
                    wall += 6f * GeoUtil.SmoothStep(3.5f, 7f, d) * (1f - Mathf.Exp(-gx * gx));
                }
                float h = baseH + wall + (roll.Fbm(l.x * 0.02f, l.y * 0.02f, 3) - 0.5f) * 5f * GeoUtil.SmoothStep(4f, 14f, d);
                h = Mathf.Max(h, floorH);
                h += Rim(l);
                M.H[i] = h;
            }
            FlattenDisc(gateA, 10f, 10f, hA);
            FlattenDisc(gateB, 10f, 10f, hB);

            // 本道：地形に沿って探す（段の切れ目を通る＝つづら折り）
            Nearest(streamPts, 12f, out var wetDist, out _);
            var road = FindPath(gateA, gateB, j =>
            {
                float wd = wetDist[j];
                float extra = wd == float.MaxValue ? 0f : wd < 4f ? 14f : (12f - wd) * 0.2f;
                float z = M.ZOf(j / M.NX);
                float edge = Mathf.Min(z, D - z);
                if (edge < 14f) extra += (14f - edge) * 0.35f;
                return extra;
            }, M.RoadGrade);
            if (road == null)
            {
                M.Notes.Add("本道の経路が見つからない");
                road = CatmullRom(new List<Vector2> { gateA, new Vector2(W * 0.5f, D * 0.5f), gateB }, 3f);
            }
            M.Road.AddRange(road);

            // 滝の上の岩棚（隠れた場所：本道側は崖、裏から回り込む）
            shelfHeight = r.Range(4.5f, 6f);
            shelfBack = r.Range(22f, 28f);
            shelfFrontR = r.Range(6.5f, 8f);
            for (int k = 0; k < 400 && !shelfOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.15f, W * 0.85f), r.Range(14f, D - 14f));
                float dr = FieldMap.DistToPolyline(c, M.Road);
                if (dr < 18f || dr > 44f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 12f) continue;
                if (NearLedge(c, 10f)) continue;
                shelfCenter = c;
                Vector2 nearest = M.Road[0];
                float bd = float.MaxValue;
                foreach (var q in M.Road) { float dd = Vector2.Distance(q, c); if (dd < bd) { bd = dd; nearest = q; } }
                shelfFront = (nearest - c).normalized;
                shelfOk = true;
            }
            if (!shelfOk) M.Notes.Add("岩棚の場所がない");
            else for (int i = 0; i < N; i++) M.H[i] += ShelfAt(LocalOf(i));

            // 石積みの跡（昔の人の道の跡）
            Vector2 ruins = Vector2.zero;
            bool ruinsOk = false;
            for (int k = 0; k < 400 && !ruinsOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.12f, W * 0.88f), r.Range(14f, D - 14f));
                float dr = FieldMap.DistToPolyline(c, M.Road);
                if (dr < 10f || dr > 30f) continue;
                if (FieldMap.DistToPolyline(c, streamPts) < 10f) continue;
                if (NearLedge(c, 9f) || (shelfOk && Vector2.Distance(c, shelfCenter) < shelfBack + 8f)) continue;
                if (M.Slope(c.x, c.y) > 20f) continue;
                ruins = c;
                ruinsOk = true;
            }
            if (!ruinsOk) M.Notes.Add("石積みの跡の場所がない");
            else
            {
                FlattenDisc(ruins, 6f, 3f, M.SampleTerrain(ruins.x, ruins.y));
                for (int k = 0; k < 6; k++)
                {
                    float a = k / 6f * Mathf.PI * 2f + r.Range(-0.2f, 0.2f);
                    AddStructure(FieldStructureKind.Ruin, ruins + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r.Range(3f, 4.6f), a * Mathf.Rad2Deg, r.Range(0.8f, 1.2f));
                }
            }

            // 渓流：滝つぼ（段の下）と湧水の淵
            var stream = CarveStream("stream.t3", streamPts, r.Range(1.6f, 2.0f), 0.55f);
            var scum = Cumulative(streamPts);
            for (int k = 1; k < streamPts.Count; k++)
            {
                float drop = stream.Surface[k - 1] - stream.Surface[k];
                if (drop < 1.1f || !M.InsidePlayable(streamPts[k], 4f)) continue;
                // 落ちた先を滝つぼに（数点先まで深く広く）
                int pool = Mathf.Min(streamPts.Count - 1, k + 1);
                bool dup = false;
                foreach (var f in stream.Falls) if (Mathf.Abs(scum[f] - scum[pool]) < 12f) dup = true;
                if (dup) continue;
                stream.Falls.Add(pool);
                for (int q = pool; q < Mathf.Min(streamPts.Count, pool + 3); q++)
                {
                    stream.Depth[q] = 1.3f;
                    stream.Radius[q] = 2.6f;
                }
            }
            if (stream.Falls.Count < 2) M.Notes.Add($"小さな滝が{stream.Falls.Count}つ");
            // 湧水の淵：滝つぼから離れた、まっすぐな所
            for (int k = 0; k < 200 && stream.SpringIndex < 0; k++)
            {
                int idx = r.Range(streamPts.Count / 5, streamPts.Count * 4 / 5);
                if (!M.InsidePlayable(streamPts[idx], 10f)) continue;
                bool nearFall = false;
                foreach (var f in stream.Falls) if (Mathf.Abs(scum[f] - scum[idx]) < 18f) nearFall = true;
                if (nearFall || FieldMap.DistToPolyline(streamPts[idx], M.Road) < 8f) continue;
                stream.SpringIndex = idx;
                for (int q = Mathf.Max(0, idx - 1); q <= Mathf.Min(streamPts.Count - 1, idx + 1); q++)
                {
                    stream.Depth[q] = 1.4f;
                    stream.Radius[q] = 3f;
                }
            }
            if (stream.SpringIndex < 0) M.Notes.Add("湧水の淵がない");

            // 本道の高さ（坂 11% まで）→ 渓流を掘る → 本道を均す
            var roadT = PlanRoadHeights(M.Road, hA, hB, M.RoadGrade, M.Streams, r);
            ApplyStream(stream);
            SettleStones();
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 脇道（石積みの跡・湧水の淵・岩棚の裏）
            if (ruinsOk) AddTrail(ruins, 5f);
            if (stream.SpringIndex >= 0)
            {
                var sp = streamPts[stream.SpringIndex];
                var dir = (FieldMap.DistToPolyline(sp + Vector2.up * 4f, M.Road) < FieldMap.DistToPolyline(sp - Vector2.up * 4f, M.Road)) ? Vector2.up : Vector2.down;
                AddTrail(sp + dir * 5.5f, 0f);
            }
            if (shelfOk)
            {
                var back = shelfCenter - shelfFront * (shelfBack * 0.85f);
                back = new Vector2(Mathf.Clamp(back.x, 8f, W - 8f), Mathf.Clamp(back.y, 8f, D - 8f));
                AddTrail(back, 0f);
            }

            // 東屋（本道の中ほど）
            var rcum = Cumulative(M.Road);
            float midAlong = rcum[rcum.Length - 1] * r.Range(0.4f, 0.6f);
            var mid = PointOn(M.Road, rcum, midAlong);
            var mdir = (PointOn(M.Road, rcum, midAlong + 2f) - mid).normalized;
            // 東屋は本道の脇の、なるべく平らな所（谷の壁の急な所には建てない）
            var gz = mid + new Vector2(-mdir.y, mdir.x) * 6.5f;
            var gz2 = mid - new Vector2(-mdir.y, mdir.x) * 6.5f;
            if (M.WaterNear(gz.x, gz.y, 4f) || M.Slope(gz2.x, gz2.y) + (M.WaterNear(gz2.x, gz2.y, 4f) ? 90f : 0f) < M.Slope(gz.x, gz.y)) gz = gz2;
            FlattenDisc(gz, 3.6f, 2.5f, M.SampleTerrain(gz.x, gz.y));
            AddStructure(FieldStructureKind.Gazebo, gz, YawOf(mid - gz));
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            AddGate(StarSkeleton.Village, new Vector2(0f, gateA.y), Vector2.right);
            AddGate(StarSkeleton.WaterfallMountain, new Vector2(W, gateB.y), Vector2.left);
            AddStructure(FieldStructureKind.Signpost, gateA + new Vector2(14f, 4.2f), 0f, 1f, StarSkeleton.WaterfallMountain);
            AddStructure(FieldStructureKind.Signpost, gateB + new Vector2(-14f, -4.2f), 0f, 1f, StarSkeleton.Village);
            if (shelfOk) AddStructure(FieldStructureKind.Crystal, shelfCenter, r.Range(0f, 360f), 1.1f);
            SettleHeights();

            AddPlace("t3.gazebo", "東屋", gz);
            if (ruinsOk) AddPlace("t3.ruins", "石積みの跡", ruins, pocket: true);
            if (stream.SpringIndex >= 0)
            {
                var sp = streamPts[stream.SpringIndex];
                AddPlace("t3.spring", "湧水の淵のほとり", NearestDry(sp, 7f), pocket: true);
            }
            if (stream.Falls.Count > 0)
            {
                var fp = streamPts[stream.Falls[0]];
                AddPlace("t3.falls", "小さな滝のそば", NearestDry(fp, 8f), pocket: true);
            }
            var ford = M.Streams[0].Fords.Count > 0 ? streamPts[M.Streams[0].Fords[0]] : Vector2.zero;
            if (M.Streams[0].Fords.Count > 0) AddPlace("t3.ford", "飛び石の浅瀬", NearestDry(ford, 6f), pocket: true);
            if (shelfOk) AddPlace("t3.shelf", "滝の上の岩棚（隠れた場所）", shelfCenter, hidden: true);
        }

        private float StreamYAt(float x)
        {
            float best = float.MaxValue, y = M.Depth * 0.5f;
            foreach (var p in streamPts)
            {
                float d = Mathf.Abs(p.x - x);
                if (d < best) { best = d; y = p.y; }
            }
            return y;
        }

        private Vector2 NearestDry(Vector2 p, float max)
        {
            for (float rr = 2f; rr <= max; rr += 1f)
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (M.InsidePlayable(q, 2f) && M.WaterAt(q.x, q.y) == FieldWater.None && M.Slope(q.x, q.y) < 30f) return q;
                }
            return p;
        }

        private float LedgesAt(Vector2 l)
        {
            float h = 0f;
            float sign = hB >= hA ? 1f : -1f;
            foreach (var lg in ledges)
            {
                float xs = lg.X + Mathf.Sin(l.y * lg.Freq + lg.Phase) * lg.Wobble;
                float dz = (l.y - lg.PassZ) / lg.PassWidth;
                float pass = Mathf.Exp(-dz * dz);
                float width = Mathf.Lerp(1.8f, lg.Ramp, pass);
                h += sign * lg.Height * GeoUtil.SmoothStep(xs - width * 0.5f, xs + width * 0.5f, l.x);
            }
            return h;
        }

        private bool NearLedge(Vector2 l, float gap)
        {
            foreach (var lg in ledges)
            {
                float xs = lg.X + Mathf.Sin(l.y * lg.Freq + lg.Phase) * lg.Wobble;
                if (Mathf.Abs(l.x - xs) < gap) return true;
            }
            return false;
        }

        private float ShelfAt(Vector2 l)
        {
            var v = l - shelfCenter;
            float d = v.magnitude;
            if (d > shelfBack * 1.2f) return 0f;
            float cosA = d > 1e-3f ? Vector2.Dot(v / d, shelfFront) : 0f;
            float rad = Mathf.Lerp(shelfBack, shelfFrontR, GeoUtil.SmoothStep(0.15f, 0.85f, cosA));
            return shelfHeight * (1f - GeoUtil.SmoothStep(0.3f, 1f, d / rad));
        }

        private float Rim(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dz = Mathf.Max(0f, Mathf.Max(9f - l.y, l.y - (D - 9f)));
            float dx = Mathf.Max(0f, Mathf.Max(-l.x, l.x - W));
            float corridor = 1f;
            if (l.x < 0f) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateA.y));
            else if (l.x > W) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateB.y));
            return GeoUtil.SmoothStep(0f, 30f, dz) * 16f + GeoUtil.SmoothStep(0f, 26f, dx) * 14f * corridor;
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

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float d = FieldMap.DistToPolyline(new Vector2(lx, lz), streamPts);
            if (d < 5f) return FieldPaint.Sand;
            if (lz < 6f || lz > M.Depth - 6f || lx < -2f || lx > M.Width + 2f) return FieldPaint.Forest;
            return noise > 0.5f ? FieldPaint.Forest : noise > 0.38f ? FieldPaint.Grass : FieldPaint.Meadow;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            // 谷の壁の林（渓流から離れるほど濃い）
            for (int k = 0; k < 1700; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                bool outside = !M.InsidePlayable(l, 0f);
                float ds = FieldMap.DistToPolyline(l, streamPts);
                float density = outside ? 0.9f : 0.12f + 0.5f * GeoUtil.SmoothStep(8f, 34f, ds);
                if (shelfOk && Vector2.Distance(l, shelfCenter) < shelfBack * 0.5f) density *= 0.2f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 3f, 40f, true)) continue;
                if (outside && Mathf.Abs(l.y - gateA.y) < 7f && l.x < 0f) continue;
                if (outside && Mathf.Abs(l.y - gateB.y) < 7f && l.x > W) continue;
                AddNature(r.Chance(0.6f) ? NatureKind.TreeTall : NatureKind.TreeRound, l, r.Range(0.9f, 1.35f), r, AreaType.Forest);
            }
            // 渓流の岩（岩場）
            for (int k = 0; k < 420; k++)
            {
                var l = streamPts[r.Range(0, streamPts.Count)] + r.InsideUnitCircle() * 7f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None) continue;
                if (M.DistToPaths(l) < 3f) continue;
                AddNature(r.Chance(0.3f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.7f, 1.5f), r, AreaType.Riverside);
            }
            for (int k = 0; k < 160; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 3f, 1.5f, 50f)) continue;
                AddNature(r.Chance(0.25f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.4f), r, AreaType.Mountain);
            }
            // 下草・茂み・花・キノコ・倒木
            for (int k = 0; k < 1300; k++)
            {
                var l = new Vector2(r.Range(-6f, W + 6f), r.Range(-6f, D + 6f));
                if (!NatureOk(l, 1.8f, 0.8f, 38f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Forest);
            }
            for (int k = 0; k < 260; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(r.Chance(0.4f) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Forest);
            }
            for (int k = 0; k < 120; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 3f, 1.5f, 30f)) continue;
                AddNature(k < 6 ? NatureKind.FallenLog : NatureKind.Mushroom, l, r.Range(0.8f, 1.2f), r, AreaType.Forest);
            }
            for (int k = 0; k < 90; k++)
            {
                var l = streamPts[r.Range(0, streamPts.Count)] + r.InsideUnitCircle() * 4f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2.5f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.1f), r, AreaType.Riverside);
            }
        }

        protected override void Validate()
        {
            float len = M.RoadLength();
            float secs = len / WorldTuning.RunSpeed;
            if (secs > WorldTuning.FieldRouteMaxSeconds) M.Notes.Add($"本道が長い（{len:0}m・{secs:0}秒）");
            if (M.Streams[0].Fords.Count == 0) M.Notes.Add("渓流の浅瀬がない");
            int pockets = 0, hidden = 0;
            foreach (var p in M.Places)
            {
                if (p.Pocket) pockets++;
                if (p.Hidden) hidden++;
            }
            if (pockets < 3) M.Notes.Add($"寄り道ポケットが{pockets}か所");
            if (hidden < 1) M.Notes.Add("隠れた場所がない");
            // §5：12〜55m の深い谷。つづら折りの坂・渓流沿いの崖・小さな滝
            RequireRelief(38f, 3f);
        }
    }
}
