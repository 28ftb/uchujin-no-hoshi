using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // T2 川下りの湿原（村 ⇄ 浜と岬。§3.1・§5・§8.3）
    // 川の下流が大きく蛇行し、川沿いに自然の土手、低い湿地に池（泥・葦・濁り）、内側の曲がりに砂州。
    // 片側に段丘の崖（6〜10m。所々に切れ目）があり、崖の上は草地（隠れた見晴らし）。
    // 本道の型：0＝土手道（川沿い・まっすぐ寄り）、1＝崖沿いの道、2＝湿地を縫う道。
    // =====================================================================

    internal sealed class MarshRiverGen : FieldMapGenBase
    {
        private Vector2 gateA, gateB;
        private float hA, hB;
        private List<Vector2> riverPts;
        private float bluffSide, bluffZ, bluffH, bluffWob, bluffFreq, bluffPhase;
        private readonly List<float> bluffPasses = new List<float>();
        private Vector2 sandbar;
        private bool sandOk;

        private float BluffEdge(float x) => bluffZ + Mathf.Sin(x * bluffFreq + bluffPhase) * bluffWob;
        private bool OnBluffSide(Vector2 l) => bluffSide > 0f ? l.y > BluffEdge(l.x) : l.y < BluffEdge(l.x);

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            hA = GateHeight(StarSkeleton.Village);
            hB = GateHeight(StarSkeleton.BeachCape);
            bluffSide = r.Chance(0.5f) ? 1f : -1f;
            bluffZ = bluffSide > 0f ? D - r.Range(30f, 40f) : r.Range(30f, 40f);
            bluffH = r.Range(6f, 10f);
            bluffWob = r.Range(5f, 10f);
            bluffFreq = r.Range(0.02f, 0.04f);
            bluffPhase = r.Range(0f, Mathf.PI * 2f);
            bluffPasses.Add(r.Range(W * 0.2f, W * 0.45f));
            bluffPasses.Add(r.Range(W * 0.55f, W * 0.85f));
            // ゲートは崖の下の低地の側
            float low = bluffSide > 0f ? bluffZ * 0.5f : (bluffZ + D) * 0.5f;
            gateA = new Vector2(0f, Mathf.Clamp(low + r.Range(-14f, 14f), 18f, D - 18f));
            gateB = new Vector2(W, Mathf.Clamp(low + r.Range(-14f, 14f), 18f, D - 18f));

            // 川：村の側から浜の側へ大きく蛇行（外はまっすぐ。崖の手前まで）
            float s0 = gateA.y + (r.Chance(0.5f) ? 1f : -1f) * 16f;
            float s1 = gateB.y + (r.Chance(0.5f) ? 1f : -1f) * 16f;
            float amp = r.Range(22f, 32f), waves = r.Range(1.6f, 2.6f), phase = r.Range(0f, Mathf.PI * 2f);
            float zMin = bluffSide > 0f ? 14f : bluffZ + 12f, zMax = bluffSide > 0f ? bluffZ - 12f : D - 14f;
            riverPts = new List<Vector2>();
            for (float x = -FieldMap.Margin; x <= W + FieldMap.Margin + 0.01f; x += 3f)
            {
                float u = Mathf.Clamp01(x / W);
                float y = Mathf.Lerp(s0, s1, GeoUtil.SmoothStep(0f, 1f, u)) + Mathf.Sin(u * Mathf.PI * waves + phase) * amp * Mathf.Sin(u * Mathf.PI);
                riverPts.Add(new Vector2(x, Mathf.Clamp(y, zMin, zMax)));
            }
            if (hB > hA + 1f) riverPts.Reverse();

            // 高さ：川の低地（湿地の窪み）＋ 川沿いの土手 ＋ 段丘の崖と崖の上 ＋ うねり
            var roll = Noise("roll");
            var marsh = Noise("marsh");
            Nearest(riverPts, 14f, out var rd, out _);
            float floorH = Mathf.Min(hA, hB) - 1.5f;
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float t = Mathf.Clamp01(l.x / W);
                float h = Mathf.Lerp(hA, hB, GeoUtil.SmoothStep(0f, 1f, t)) + (roll.Fbm(l.x * 0.02f, l.y * 0.02f, 3) - 0.5f) * 2.4f;
                float m = marsh.Fbm(l.x * 0.025f, l.y * 0.025f, 3);
                if (m < 0.45f) h -= (0.45f - m) * 5f; // 湿地の窪み
                if (rd[i] != float.MaxValue && rd[i] > 3f) h += 1.4f * Mathf.Exp(-Mathf.Pow((rd[i] - 6.5f) / 3f, 2f)); // 自然の土手
                // 段丘の崖（切れ目はゆるい坂）
                float edge = BluffEdge(l.x);
                float pass = 0f;
                foreach (var px in bluffPasses) { float u = (l.x - px) / 10f; pass = Mathf.Max(pass, Mathf.Exp(-u * u)); }
                float width = Mathf.Lerp(2.5f, bluffH / 0.12f, pass);
                float s = bluffSide > 0f ? l.y - edge : edge - l.y;
                h += bluffH * GeoUtil.SmoothStep(-width * 0.5f, width * 0.5f, s) + (s > 0f ? (roll.Fbm(l.x * 0.05f + 3f, l.y * 0.05f, 2) - 0.5f) * 3f * GeoUtil.SmoothStep(0f, 12f, s) : 0f);
                h = Mathf.Max(h, floorH);
                h += Rim(l);
                M.H[i] = h;
            }
            FlattenDisc(gateA, 10f, 10f, hA);
            FlattenDisc(gateB, 10f, 10f, hB);

            // 本道（型で通り方が変わる）
            Nearest(riverPts, 40f, out var wet, out _);
            var road = FindPath(gateA, gateB, j =>
            {
                float wd = wet[j];
                float extra = wd == float.MaxValue ? 0f : wd < 5f ? 14f : 0f;
                var l = LocalOf(j);
                float z = l.y;
                float edgeD = Mathf.Min(z, D - z);
                if (edgeD < 14f) extra += (14f - edgeD) * 0.35f;
                if (M.Type == 0) extra += (wd == float.MaxValue ? 30f : Mathf.Max(0f, wd - 9f)) * 0.03f; // 土手道
                else if (M.Type == 1) extra += Mathf.Abs((bluffSide > 0f ? BluffEdge(l.x) - z : z - BluffEdge(l.x)) - 8f) * 0.025f; // 崖沿い
                if (M.H[j] < floorH + 1.2f) extra += 0.6f; // 湿地の窪みは少し避ける
                return extra;
            });
            if (road == null) { M.Notes.Add("本道の経路が見つからない"); road = CatmullRom(new List<Vector2> { gateA, new Vector2(W * 0.5f, low), gateB }, 3f); }
            M.Road.AddRange(road);

            // 湿地の池 2〜3（泥・葦・濁り）
            int ponds = r.Range(2, 4);
            var pondC = new List<Vector2>();
            for (int k = 0; k < 400 && pondC.Count < ponds; k++)
            {
                var c = new Vector2(r.Range(W * 0.12f, W * 0.88f), r.Range(16f, D - 16f));
                if (OnBluffSide(c) || Mathf.Abs(c.y - BluffEdge(c.x)) < 14f) continue;
                if (M.DistToPaths(c) < 14f || FieldMap.DistToPolyline(c, riverPts) < 16f) continue;
                bool far = true;
                foreach (var o in pondC) if (Vector2.Distance(o, c) < 34f) far = false;
                if (far) pondC.Add(c);
            }
            if (pondC.Count < 2) M.Notes.Add($"湿原の池が{pondC.Count}つ");

            var river = CarveStream("stream.t2", riverPts, r.Range(3.2f, 4f), 1.2f);
            var roadT = PlanRoadHeights(M.Road, hA, hB, M.RoadGrade, M.Streams, r);
            ApplyStream(river);
            SettleStones();
            foreach (var c in pondC) CarvePond(c, r.Range(6f, 9.5f), r.Range(1f, 1.5f));
            // 砂州：一番大きな曲がりの内側（川の中に浅く出る）
            int bend = -1;
            float bestCurv = 0f;
            for (int k = 3; k < riverPts.Count - 3; k++)
            {
                if (!M.InsidePlayable(riverPts[k], 8f) || M.DistToPaths(riverPts[k]) < 9f) continue;
                var a = (riverPts[k] - riverPts[k - 3]).normalized;
                var b = (riverPts[k + 3] - riverPts[k]).normalized;
                float curv = Mathf.Abs(a.x * b.y - a.y * b.x);
                if (curv > bestCurv) { bestCurv = curv; bend = k; }
            }
            if (bend > 0)
            {
                var a = (riverPts[bend] - riverPts[bend - 3]).normalized;
                var b = (riverPts[bend + 3] - riverPts[bend]).normalized;
                float side = Mathf.Sign(a.x * b.y - a.y * b.x);
                var inner = new Vector2(-a.y, a.x) * side;
                sandbar = riverPts[bend] + inner * (river.Radius[bend] * 0.9f);
                float surf = river.Surface[bend];
                for (int i = 0; i < N; i++)
                {
                    var l = LocalOf(i);
                    float d = Vector2.Distance(l, sandbar);
                    if (d > 7f) continue;
                    float hh = surf + 0.25f - d * 0.08f;
                    if (hh > M.H[i])
                    {
                        M.H[i] = hh;
                        if (hh > surf) { M.WaterY[i] = float.NaN; M.Water[i] = FieldWater.None; }
                    }
                }
                sandOk = true;
            }
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 崖の上の見晴らし（隠れた場所：崖の切れ目から回り込む）
            Vector2 top = Vector2.zero;
            bool topOk = false;
            for (int k = 0; k < 300 && !topOk; k++)
            {
                var c = new Vector2(r.Range(W * 0.15f, W * 0.85f), bluffSide > 0f ? r.Range(BluffEdge(0f) + 8f, D - 10f) : r.Range(10f, BluffEdge(0f) - 8f));
                if (!OnBluffSide(c) || Mathf.Abs(c.y - BluffEdge(c.x)) < 6f) continue;
                bool nearPass = false;
                foreach (var px in bluffPasses) if (Mathf.Abs(c.x - px) < 30f) nearPass = true;
                if (nearPass) continue;
                top = c;
                topOk = true;
            }
            if (topOk) AddTrail(top, 0f);
            foreach (var c in pondC) AddTrail(c, 9f);
            if (sandOk) AddTrail(sandbar, 4f);

            var rcum = Cumulative(M.Road);
            float midAlong = rcum[rcum.Length - 1] * r.Range(0.4f, 0.6f);
            var mid = PointOn(M.Road, rcum, midAlong);
            var mdir = (PointOn(M.Road, rcum, midAlong + 2f) - mid).normalized;
            var gz = mid + new Vector2(-mdir.y, mdir.x) * 6.5f;
            if (M.WaterNear(gz.x, gz.y, 4f) || M.Slope(gz.x, gz.y) > 20f) gz = mid - new Vector2(-mdir.y, mdir.x) * 6.5f;
            FlattenDisc(gz, 3.6f, 2.5f, M.SampleTerrain(gz.x, gz.y));
            AddStructure(FieldStructureKind.Gazebo, gz, YawOf(mid - gz));
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            AddGate(StarSkeleton.Village, new Vector2(0f, gateA.y), Vector2.right);
            AddGate(StarSkeleton.BeachCape, new Vector2(W, gateB.y), Vector2.left);
            AddStructure(FieldStructureKind.Signpost, gateA + new Vector2(14f, 4.2f), 0f, 1f, StarSkeleton.BeachCape);
            AddStructure(FieldStructureKind.Signpost, gateB + new Vector2(-14f, -4.2f), 0f, 1f, StarSkeleton.Village);
            SettleHeights();

            AddPlace("t2.gazebo", "東屋", gz);
            for (int k = 0; k < M.Ponds.Count; k++)
                AddPlace("t2.pond" + k, "湿原の池のほとり", M.Ponds[k].Center + (mid - M.Ponds[k].Center).normalized * (M.Ponds[k].Radius + 3f), pocket: true);
            if (sandOk) AddPlace("t2.sandbar", "川の砂州", sandbar, pocket: true);
            if (topOk) AddPlace("t2.bluff", "崖の上の見晴らし（隠れた場所）", top, hidden: true);
        }

        private float Rim(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dz = Mathf.Max(0f, Mathf.Max(8f - l.y, l.y - (D - 8f)));
            float dx = Mathf.Max(0f, Mathf.Max(-l.x, l.x - W));
            float corridor = 1f;
            if (l.x < 0f) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateA.y));
            else if (l.x > W) corridor = GeoUtil.SmoothStep(5f, 14f, Mathf.Abs(l.y - gateB.y));
            // 川が出入りする所は低いまま
            float rv = FieldMap.DistToPolyline(l, riverPts);
            corridor = Mathf.Min(corridor, GeoUtil.SmoothStep(6f, 18f, rv));
            return GeoUtil.SmoothStep(0f, 30f, dz) * 12f + GeoUtil.SmoothStep(0f, 26f, dx) * 10f * corridor;
        }

        private void AddTrail(Vector2 target, float stopShort)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            var dir = (target - near).normalized;
            var end = target - dir * stopShort;
            var pts = FindPath(near, end, j => M.WaterY[j] > M.H[j] + 0.3f ? 20f : 0f, 0.2f);
            if (pts == null) return;
            var t = RoadTargets(pts, 0.45f, M.SampleTerrain(near.x, near.y), M.SampleTerrain(end.x, end.y), 6f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.6f);
            M.Trails.Add(pts);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            var l = new Vector2(lx, lz);
            if (sandOk && Vector2.Distance(l, sandbar) < 7f) return FieldPaint.Sand;
            if (OnBluffSide(l)) return noise > 0.55f ? FieldPaint.Forest : FieldPaint.Meadow;
            return noise < 0.42f ? FieldPaint.Mud : noise > 0.6f ? FieldPaint.Meadow : FieldPaint.Grass;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            // 葦（湿地・池・川岸にたくさん）
            for (int k = 0; k < 1400; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (OnBluffSide(l)) continue;
                var p = M.PaintAt(l.x, l.y);
                bool wetSide = p == FieldPaint.Mud || p == FieldPaint.Sand || M.WaterNear(l.x, l.y, 4f);
                if (!wetSide && !r.Chance(0.15f)) continue;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2.2f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.3f), r, AreaType.Riverside);
            }
            // 柳のような木（川沿い）・崖の上の林・外の林
            for (int k = 0; k < 1100; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                bool outside = !M.InsidePlayable(l, 0f);
                float density = outside ? 0.7f : OnBluffSide(l) ? 0.32f : FieldMap.DistToPolyline(l, riverPts) < 14f ? 0.12f : 0.03f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 36f, true)) continue;
                if (outside && Mathf.Abs(l.y - gateA.y) < 7f && l.x < 0f) continue;
                if (outside && Mathf.Abs(l.y - gateB.y) < 7f && l.x > W) continue;
                AddNature(OnBluffSide(l) && r.Chance(0.5f) ? NatureKind.TreeTall : NatureKind.TreeRound, l, r.Range(0.8f, 1.25f), r, OnBluffSide(l) ? AreaType.Meadow : AreaType.Riverside);
            }
            for (int k = 0; k < 1100; k++)
            {
                var l = new Vector2(r.Range(-6f, W + 6f), r.Range(-6f, D + 6f));
                if (!NatureOk(l, 1.8f, 0.8f, 38f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 260; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(OnBluffSide(l) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Meadow);
            }
            // 流木（川岸）・崖の岩
            for (int k = 0; k < 40; k++)
            {
                var l = riverPts[r.Range(0, riverPts.Count)] + r.InsideUnitCircle() * 7f;
                if (!NatureOk(l, 3f, 1f, 25f)) continue;
                AddNature(NatureKind.FallenLog, l, r.Range(0.7f, 1.1f), r, AreaType.Riverside);
            }
            for (int k = 0; k < 160; k++)
            {
                var l = new Vector2(r.Range(0f, W), BluffEdge(0f) + r.Range(-8f, 8f));
                l.y = BluffEdge(l.x) + r.Range(-6f, 6f);
                if (!NatureOk(l, 3f, 1.5f, 60f)) continue;
                AddNature(r.Chance(0.3f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.4f), r, AreaType.Meadow);
            }
        }

        protected override void Validate()
        {
            float len = M.RoadLength();
            if (len / WorldTuning.RunSpeed > WorldTuning.FieldRouteMaxSeconds) M.Notes.Add($"本道が長い（{len:0}m）");
            if (!sandOk) M.Notes.Add("砂州がない");
            int pockets = 0, hidden = 0;
            foreach (var p in M.Places) { if (p.Pocket) pockets++; if (p.Hidden) hidden++; }
            if (pockets < 3) M.Notes.Add($"寄り道ポケットが{pockets}か所");
            if (hidden < 1) M.Notes.Add("隠れた場所がない");
            // §5：2〜12m の低地。段丘の崖と土手で平坦にしない
            RequireRelief(8f, 0.6f);
        }
    }

    // =====================================================================
    // R4 浜と岬（§2.3 R4・§5・§8.3）
    // 南が海。砂浜・岩場・海に突き出た岬（崖の上 20〜28m、先は沖）・川下りの湿原から来る川の河口・沖へ延びる桟橋・浜の休憩所。
    // 内陸は砂丘と丘で上がる。
    // =====================================================================

    internal sealed class BeachCapeGen : FieldMapGenBase
    {
        private Vector2 gateW;
        private float hW;
        private readonly Dictionary<string, (Vector2 p, Vector2 inward, float h)> gates = new Dictionary<string, (Vector2, Vector2, float)>();
        private float coastZ, coastWob, coastFreq, coastPhase;
        private float capeX, capeHalf, capeTopH, capeTipZ;
        private List<Vector2> riverPts;
        private Vector2 pierBase;
        private float pierYaw;

        public float CoastAt(float x) => coastZ + Mathf.Sin(x * coastFreq + coastPhase) * coastWob;

        private float CapeAt(Vector2 l)
        {
            // 岬：海岸から南へ突き出た細長い台地（崖で囲む）
            float dx = Mathf.Abs(l.x - capeX);
            if (l.y < capeTipZ - 10f) return 0f;
            float along = Mathf.Clamp01((CoastAt(capeX) + 30f - l.y) / (CoastAt(capeX) + 30f - capeTipZ));
            float half = capeHalf * (1f - 0.35f * along);
            float k = dx / half;
            float tip = GeoUtil.SmoothStep(capeTipZ - 6f, capeTipZ + 2f, l.y);
            // 内陸側は丘へなだらかにつながる（岬の付け根から歩いて上がれる）
            float root = 1f - GeoUtil.SmoothStep(CoastAt(capeX) + 15f, CoastAt(capeX) + 70f, l.y);
            return capeTopH * (1f - GeoUtil.SmoothStep(0.8f, 1.05f, k)) * tip * root;
        }

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            M.RoadGrade = 0.1f;
            coastZ = D * r.Range(0.28f, 0.36f);
            coastWob = r.Range(6f, 14f);
            coastFreq = r.Range(0.012f, 0.025f);
            coastPhase = r.Range(0f, Mathf.PI * 2f);
            capeX = W * r.Range(0.58f, 0.75f);
            capeHalf = r.Range(16f, 22f);
            capeTopH = r.Range(20f, 27f);
            capeTipZ = r.Range(10f, 22f);

            // ゲート：川下りの湿原は西の辺（海より上）。T6 は骨格の向き（南＝海になる時は北）
            hW = GateHeight(StarSkeleton.MarshRiver);
            gateW = new Vector2(0f, Mathf.Clamp(CoastAt(0f) + r.Range(30f, 50f), 30f, D - 30f));
            gates[StarSkeleton.MarshRiver] = (gateW, Vector2.right, hW);
            string t6 = sk.T6;
            int e6 = (EdgeFor(StarSkeleton.MarshRiver, t6) + 3) % 4;
            if (e6 == 0 || e6 == 3) e6 = r.Chance(0.5f) ? 1 : 2;

            // 川（河口）：西の辺から海へ
            var rs = new Vector2(-FieldMap.Margin, gateW.y - 16f);
            var mouth = new Vector2(W * r.Range(0.18f, 0.32f), CoastAt(W * 0.25f) - 6f);
            riverPts = CatmullRom(new List<Vector2> { rs, new Vector2(10f, rs.y), Vector2.Lerp(new Vector2(10f, rs.y), mouth, 0.5f) + new Vector2(r.Range(-10f, 10f), 0f), mouth, mouth + new Vector2(0f, -20f) }, 3f);

            // 高さ：海底 → 砂浜（0〜3m）→ 砂丘（3〜8m）→ 内陸の丘（10〜20m）＋ 岬の台地 ＋ 岩場
            var dune = Noise("dune");
            var hill = Noise("hill");
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float s = l.y - CoastAt(l.x); // 海岸からの内陸距離（負は海）
                float h;
                if (s < 0f) h = Mathf.Max(-8f, s * 0.12f - 0.2f);
                else
                {
                    h = Mathf.Lerp(0.1f, 3f, GeoUtil.SmoothStep(0f, 24f, s));
                    h += GeoUtil.SmoothStep(18f, 50f, s) * (2f + dune.Fbm(l.x * 0.04f, l.y * 0.04f, 3) * 5f);
                    h += GeoUtil.SmoothStep(50f, 130f, s) * (7f + hill.Fbm(l.x * 0.012f, l.y * 0.012f, 3) * 10f);
                }
                h = Mathf.Max(h, CapeAt(l));
                M.H[i] = h;
            }
            // T6 のゲート：辺の上で地面の高さがゲートの高さに近い所
            {
                float gh = GateHeight(t6);
                float bestA = 0.5f, bestS = float.MaxValue;
                for (float a = 0.12f; a <= 0.88f; a += 0.01f)
                {
                    EdgePoint(e6, a, out var p, out var inward);
                    var q = p + inward * 12f;
                    if (q.y < CoastAt(q.x) + 12f) continue;
                    float sc = Mathf.Abs(M.SampleTerrain(q.x, q.y) - gh);
                    if (Mathf.Abs(q.x - capeX) < capeHalf + 10f) sc += 50f;
                    if (sc < bestS) { bestS = sc; bestA = a; }
                }
                EdgePoint(e6, bestA, out var gp, out var gin);
                gates[t6] = (gp, gin, gh);
            }
            for (int i = 0; i < N; i++) M.H[i] += RimAt(LocalOf(i));
            foreach (var kv in gates) FlattenDisc(kv.Value.p, 10f, 10f, kv.Value.h);

            // 桟橋：砂浜から沖へ（河口と岬のあいだ）
            float px = Mathf.Lerp(mouth.x + 20f, capeX - capeHalf - 20f, r.Range(0.3f, 0.7f));
            pierBase = new Vector2(px, CoastAt(px) + 1.5f);
            pierYaw = 180f;
            var hutC = new Vector2(px + r.Range(-16f, 16f), CoastAt(px) + r.Range(26f, 34f));

            // 本道：湿原のゲート → 桟橋のたもと → T6 のゲート。枝道：岬の上へ
            var pierHead = pierBase + Vector2.up * 7f;
            System.Func<int, float> cost = j =>
            {
                var l = LocalOf(j);
                float s = l.y - CoastAt(l.x);
                return s < 4f ? 20f : 0f;
            };
            // 本道はゲートからゲートへ（行って戻る道を作らない）。桟橋と岬へは枝道
            var main = FindPath(gateW, gates[t6].p, cost, M.RoadGrade);
            if (main == null) { M.Notes.Add("本道の経路が見つからない"); main = new List<Vector2> { gateW, gates[t6].p }; }
            M.Road.AddRange(main);
            var capeTop = new Vector2(capeX, Mathf.Lerp(CoastAt(capeX), capeTipZ, 0.55f));
            var branchEnds = new List<Vector2>();
            foreach (var target in new[] { pierHead, capeTop })
            {
                Vector2 near = M.Road[0];
                float bd = float.MaxValue;
                foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
                if (bd < 6f) continue;
                var br = FindPath(near, target, cost, 0.14f);
                if (br != null) { M.Branches.Add(br); branchEnds.Add(target); }
                else M.Notes.Add("枝道が見つからない");
            }

            var river = CarveStream("stream.r4", riverPts, r.Range(3f, 3.8f), 1.2f, float.NaN, 0f);
            var roadT = PlanRoadHeights(M.Road, hW, gates[t6].h, M.RoadGrade, M.Streams, r);
            var rcum = Cumulative(M.Road);
            var branchT = new List<float[]>();
            foreach (var br in M.Branches)
            {
                float along = 0f, bd = float.MaxValue;
                for (int k = 0; k < M.Road.Count; k++) { float d = Vector2.Distance(M.Road[k], br[0]); if (d < bd) { bd = d; along = rcum[k]; } }
                var end = br[br.Count - 1];
                branchT.Add(PlanRoadHeights(br, ValueAlong(rcum, roadT, along), M.SampleTerrain(end.x, end.y), 0.14f, M.Streams, r));
            }
            ApplyStream(river);
            SettleStones();
            // 海（水面 0m）
            for (int i = 0; i < N; i++)
            {
                if (M.H[i] >= -0.03f) continue;
                var l = LocalOf(i);
                if (l.y > CoastAt(l.x) + 8f && M.Water[i] == FieldWater.Stream) continue;
                M.WaterY[i] = 0f;
                M.Water[i] = FieldWater.Sea;
            }
            for (int b = 0; b < M.Branches.Count; b++) FlattenAlong(M.Branches[b], branchT[b], WorldTuning.FieldRoadHalfWidth, 3.5f);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 浜の休憩所・桟橋・岩場の潮だまり
            FlattenDisc(hutC, 4f, 3f, M.SampleTerrain(hutC.x, hutC.y));
            AddStructure(FieldStructureKind.Gazebo, hutC, 180f);
            AddStructure(FieldStructureKind.Pier, pierBase, pierYaw, 2.2f, null, 0.55f);
            var pools = new Vector2(capeX + capeHalf + r.Range(10f, 30f), CoastAt(capeX + capeHalf + 20f) + 2f);
            pools.x = Mathf.Min(pools.x, W - 20f);
            AddTrail(hutC);
            AddTrail(pools);
            for (int b = 0; b < M.Branches.Count; b++) FlattenAlong(M.Branches[b], branchT[b], WorldTuning.FieldRoadHalfWidth, 1.2f);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            foreach (var kv in gates)
            {
                AddGate(kv.Key, kv.Value.p, kv.Value.inward);
                AddStructure(FieldStructureKind.Signpost, kv.Value.p + kv.Value.inward * 14f + new Vector2(-kv.Value.inward.y, kv.Value.inward.x) * 4.2f, 0f, 1f, kv.Key);
            }
            SettleHeights();

            AddPlace("r4.pier", "桟橋のたもと", pierHead);
            AddPlace("r4.cape", "岬の上", capeTop, pocket: true);
            AddPlace("r4.hut", "浜の休憩所", hutC + Vector2.down * 4f);
            AddPlace("r4.estuary", "河口", NearestDry(mouth + new Vector2(8f, 6f)), pocket: true);
            AddPlace("r4.pools", "岩場の潮だまり", NearestDry(pools), pocket: true);
        }

        private Vector2 NearestDry(Vector2 p)
        {
            for (float rr = 0f; rr < 30f; rr += 1f)
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    var q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (M.InsidePlayable(q, 3f) && M.WaterAt(q.x, q.y) == FieldWater.None && M.Slope(q.x, q.y) < 28f) return q;
                }
            return p;
        }

        private float RimAt(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            if (l.y < CoastAt(Mathf.Clamp(l.x, 0f, W)) + 6f) return 0f; // 海の側は縁を作らない（海が続いて見える）
            float dx = Mathf.Max(0f, Mathf.Max(6f - l.x, l.x - (W - 6f)));
            float dz = Mathf.Max(0f, l.y - (D - 6f));
            float corridor = 1f;
            foreach (var kv in gates) corridor = Mathf.Min(corridor, GeoUtil.SmoothStep(8f, 24f, Vector2.Distance(l, kv.Value.p)));
            corridor = Mathf.Min(corridor, GeoUtil.SmoothStep(6f, 18f, FieldMap.DistToPolyline(l, riverPts)));
            return (GeoUtil.SmoothStep(0f, 30f, dx) * 16f + GeoUtil.SmoothStep(0f, 30f, dz) * 16f) * corridor;
        }

        private void AddTrail(Vector2 target)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            var pts = FindPath(near, target, j => 0f, 0.2f);
            if (pts == null) return;
            var t = RoadTargets(pts, 0.45f, M.SampleTerrain(near.x, near.y), M.SampleTerrain(target.x, target.y), 6f);
            for (int k = 0; k < t.Length; k++) t[k] = Mathf.Max(t[k], 0.3f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.6f);
            M.Trails.Add(pts);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float s = lz - CoastAt(lx);
            var l = new Vector2(lx, lz);
            if (CapeAt(l) > 3f) return noise > 0.5f ? FieldPaint.Rock : FieldPaint.Grass;
            if (s < 26f) return Mathf.Abs(lx - capeX) < capeHalf + 30f || lx > capeX + capeHalf ? (noise > 0.45f ? FieldPaint.Rock : FieldPaint.Sand) : FieldPaint.Sand;
            if (s < 55f) return noise > 0.5f ? FieldPaint.Sand : FieldPaint.Meadow;
            return noise > 0.55f ? FieldPaint.Forest : FieldPaint.Grass;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                float s = l.y - CoastAt(Mathf.Clamp(l.x, 0f, W));
                if (s < 40f) continue;
                bool outside = !M.InsidePlayable(l, 0f);
                float density = outside ? 0.8f : GeoUtil.SmoothStep(40f, 110f, s) * 0.45f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 36f, true)) continue;
                bool nearGate = false;
                foreach (var kv in gates) if (Vector2.Distance(kv.Value.p, l) < 10f) nearGate = true;
                if (nearGate) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.TreeLollipop : NatureKind.TreeRound, l, r.Range(0.85f, 1.3f), r, AreaType.Coast);
            }
            // 浜の貝・岩場の岩・砂丘の草
            for (int k = 0; k < 260; k++)
            {
                float x = r.Range(0f, W);
                var l = new Vector2(x, CoastAt(x) + r.Range(1f, 14f));
                if (!NatureOk(l, 2f, 0.6f, 30f)) continue;
                AddNature(NatureKind.Shell, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
            for (int k = 0; k < 420; k++)
            {
                float x = r.Range(0f, W);
                bool rocky = Mathf.Abs(x - capeX) < capeHalf + 40f || x > capeX + capeHalf;
                if (!rocky && !r.Chance(0.15f)) continue;
                var l = new Vector2(x, CoastAt(x) + r.Range(-3f, 18f));
                if (!M.InsidePlayable(l, 1f) || M.DistToPaths(l) < 2.5f) continue;
                if (M.WaterAt(l.x, l.y) != FieldWater.None && M.WaterDepth(l.x, l.y) > 0.5f) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.6f), r, AreaType.Coast);
            }
            for (int k = 0; k < 1300; k++)
            {
                var l = new Vector2(r.Range(-6f, W + 6f), r.Range(-6f, D + 6f));
                if (l.y < CoastAt(Mathf.Clamp(l.x, 0f, W)) + 14f) continue;
                if (!NatureOk(l, 1.8f, 0.8f, 38f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Coast);
            }
            for (int k = 0; k < 220; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (l.y < CoastAt(l.x) + 20f) continue;
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Meadow);
            }
            for (int k = 0; k < 140; k++)
            {
                var l = riverPts[r.Range(0, riverPts.Count)] + r.InsideUnitCircle() * 6f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2.5f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.2f), r, AreaType.Riverside);
            }
        }

        protected override void Validate()
        {
            int pockets = 0;
            foreach (var p in M.Places) if (p.Pocket) pockets++;
            if (pockets < 3) M.Notes.Add($"寄り道の場所が{pockets}か所");
            // §5：0〜28m（浜・砂丘・岬の崖）
            RequireRelief(18f, 1.2f);
        }
    }
}
