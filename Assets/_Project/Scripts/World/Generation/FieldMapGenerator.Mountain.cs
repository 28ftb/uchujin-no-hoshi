using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // R3 滝の山（§2.3 R3・§5・§8.3）
    // 南（渓流の坂道のゲート）から北の山頂（125〜140m）へ登る山。山腹は等高線に沿った段（ベンチと崖）。
    // 山腹を横切る崖（10〜16m）を川が落ちる大きな滝、深さ3m以上の滝つぼ、両側が崖の渓谷（こだまの渓谷）、
    // 山頂近くの泉（冷たい湧水）、結晶の岩場、崖の洞穴、山頂の見晴らし岩。
    // 本道：渓流の坂道のゲート → 滝つぼのそば → 尾根道のゲート。苔の谷（と崖の小道）のゲートへは枝道。
    // =====================================================================

    internal sealed class WaterfallMountainGen : FieldMapGenBase
    {
        private readonly Dictionary<string, (Vector2 p, Vector2 inward, float h)> gates = new Dictionary<string, (Vector2, Vector2, float)>();
        private Vector2 peak;
        private float peakH, peakR;
        private float fallZ, fallDrop, fallWob, fallFreq, fallPhase;
        private readonly List<float> fallPasses = new List<float>();
        private float riverX;
        private List<Vector2> riverPts;
        private Vector2 springC;
        private Vector2 crystalC;
        private float terraceStep;
        private float canyonZ0, canyonZ1;

        protected override void Build()
        {
            var r = R("layout");
            float W = M.Width, D = M.Depth;
            M.RoadGrade = 0.18f;

            // ゲート：渓流の坂道は南。ほかは骨格の向きで東・北・西（同じ辺なら位置をずらす）
            var others = new List<string> { StarSkeleton.MossValley, StarSkeleton.Ridge };
            if (sk.T6 == StarSkeleton.CliffPath) others.Add(StarSkeleton.CliffPath);
            AddEdgeGateLocal(StarSkeleton.StreamSlope, 0, r.Range(0.4f, 0.6f));
            float gSouth = gates[StarSkeleton.StreamSlope].h;

            // 山頂と山の形
            peak = new Vector2(W * r.Range(0.4f, 0.6f), D * r.Range(0.74f, 0.84f));
            peakH = r.Range(125f, 140f);
            peakR = r.Range(110f, 140f);
            terraceStep = r.Range(11f, 15f);
            // 滝の崖（山腹を横切る）
            fallZ = D * r.Range(0.42f, 0.52f);
            fallDrop = r.Range(10f, 16f);
            fallWob = r.Range(6f, 14f);
            fallFreq = r.Range(0.015f, 0.03f);
            fallPhase = r.Range(0f, Mathf.PI * 2f);
            riverX = W * r.Range(0.35f, 0.65f);
            float passX = riverX + (r.Chance(0.5f) ? 1f : -1f) * r.Range(55f, 85f);
            fallPasses.Add(Mathf.Clamp(passX, 30f, W - 30f));
            if (r.Chance(0.6f)) fallPasses.Add(Mathf.Clamp(riverX - (passX - riverX) * r.Range(0.8f, 1.2f), 30f, W - 30f));

            // 川：泉（山頂の近く）→ 滝 → 渓谷 → 南の端（渓流の坂道の渓流につながる）
            springC = new Vector2(Mathf.Clamp(peak.x + r.Range(-35f, 35f), 40f, W - 40f), Mathf.Min(D - 40f, peak.y - r.Range(28f, 45f)));
            var southGate = gates[StarSkeleton.StreamSlope].p;
            var riverEnd = new Vector2(southGate.x + 12f, -FieldMap.Margin);
            var fallTop = new Vector2(riverX, fallZ + 6f);
            var fallBot = new Vector2(riverX + r.Range(-3f, 3f), fallZ - 8f);
            riverPts = CatmullRom(new List<Vector2>
            {
                springC,
                Vector2.Lerp(springC, fallTop, 0.5f) + new Vector2(r.Range(-20f, 20f), 0f),
                fallTop, fallBot,
                Vector2.Lerp(fallBot, new Vector2(riverEnd.x, 20f), 0.5f) + new Vector2(r.Range(-18f, 18f), 0f),
                new Vector2(riverEnd.x, 14f), riverEnd
            }, 3f);
            canyonZ1 = fallZ - 10f;
            canyonZ0 = canyonZ1 - r.Range(45f, 70f);

            // 高さ：南から北へ登る斜面 ＋ 山頂の盛り上がり ＋ 岩のうねり → 山腹の段（等高線に沿ったベンチと崖）→ 滝の崖 → 川の谷・渓谷
            var ridge = Noise("ridge");
            var roll = Noise("roll");
            Nearest(riverPts, 60f, out var rDist, out _);
            for (int i = 0; i < N; i++)
            {
                var l = LocalOf(i);
                float tz = Mathf.Clamp01(l.y / D);
                float h = gSouth + 40f * Mathf.Pow(tz, 1.2f);
                float dp = Vector2.Distance(l, peak) / peakR;
                if (dp < 1f) h += (peakH - 40f - gSouth) * Mathf.Pow(1f - GeoUtil.SmoothStep(0f, 1f, dp), 1.4f);
                h += (ridge.Ridged(l.x * 0.02f, l.y * 0.02f, 4) - 0.5f) * 10f * GeoUtil.SmoothStep(0.15f, 0.5f, tz);
                h += (roll.Fbm(l.x * 0.04f, l.y * 0.04f, 3) - 0.5f) * 2.5f;
                // 山腹の段（下の斜面と南の端の近くは段にしない。所々だけ段にし、ほかは自然な斜面で上の段とつながる）
                float terr = GeoUtil.SmoothStep(0.22f, 0.4f, tz) * GeoUtil.SmoothStep(0.45f, 0.62f, ridge.Fbm(l.x * 0.008f + 11f, l.y * 0.008f - 4f, 2));
                if (terr > 0f)
                {
                    float q = h / terraceStep;
                    float stepped = (Mathf.Floor(q) + GeoUtil.SmoothStep(0.72f, 1f, q - Mathf.Floor(q))) * terraceStep;
                    h = Mathf.Lerp(h, stepped, 0.7f * terr);
                }
                h += FallStepAt(l);
                // 川の谷（滝の崖より下は深い渓谷）
                if (rDist[i] != float.MaxValue)
                {
                    float d = rDist[i];
                    float depth = l.y < canyonZ1 && l.y > canyonZ0 ? 9f : 3.5f;
                    float wall = l.y < canyonZ1 && l.y > canyonZ0 ? 0.9f : 0.3f;
                    // 渓谷の真ん中に渡れる切れ目
                    float cz = (l.y - (canyonZ0 + canyonZ1) * 0.5f) / 6f;
                    if (l.y < canyonZ1 && l.y > canyonZ0) depth *= 1f - Mathf.Exp(-cz * cz) * 0.8f;
                    float floor = h - depth + Mathf.Max(0f, d - 5f) * wall;
                    h = Mathf.Min(h, floor);
                }
                M.H[i] = h;
            }
            // ほかのゲート：骨格の向きの辺の上で、地面の高さがゲートの高さに近い所に置く（ゲートのまわりを深い穴や高い盛り土にしない）
            foreach (var o in others)
            {
                int edge = EdgeFor(StarSkeleton.StreamSlope, o);
                if (edge == 0) edge = 1;
                float gh = GateHeight(o);
                float bestA = 0.5f, bestS = float.MaxValue;
                for (float a = 0.12f; a <= 0.88f; a += 0.01f)
                {
                    EdgePoint(edge, a, out var p, out var inward);
                    var q = p + inward * 12f;
                    float s = Mathf.Abs(M.SampleTerrain(q.x, q.y) - gh);
                    foreach (var kv in gates) if (Vector2.Distance(kv.Value.p, p) < 50f) s += 100f;
                    if (FieldMap.DistToPolyline(q, riverPts) < 20f) s += 100f;
                    if (s < bestS) { bestS = s; bestA = a; }
                }
                AddEdgeGateLocal(o, edge, bestA);
            }
            for (int i = 0; i < N; i++) M.H[i] += RimAt(LocalOf(i));
            foreach (var kv in gates) FlattenDisc(kv.Value.p, 10f, 10f, kv.Value.h);

            // 本道：南のゲート → 滝つぼのそば → 尾根道のゲート。枝道：苔の谷（・崖の小道）のゲートへ
            var view = fallBot + new Vector2((fallPasses[0] > riverX ? 1f : -1f) * 16f, -6f);
            view = new Vector2(Mathf.Clamp(view.x, 20f, W - 20f), Mathf.Clamp(view.y, 20f, D - 20f));
            Nearest(riverPts, 10f, out var wet, out _);
            System.Func<int, float> cost = j => wet[j] == float.MaxValue ? 0f : wet[j] < 4f ? 10f : (10f - wet[j]) * 0.15f;
            // 本道はゲートからゲートへ（行って戻る道を作らない）。滝つぼのそば・ほかのゲートへは枝道
            var main = FindPath(gates[StarSkeleton.StreamSlope].p, gates[StarSkeleton.Ridge].p, cost, M.RoadGrade);
            if (main == null) { M.Notes.Add("本道の経路が見つからない"); main = new List<Vector2> { gates[StarSkeleton.StreamSlope].p, gates[StarSkeleton.Ridge].p }; }
            M.Road.AddRange(main);
            var branchTargets = new List<Vector2> { view };
            foreach (var o in others) if (o != StarSkeleton.Ridge) branchTargets.Add(gates[o].p);
            foreach (var gp in branchTargets)
            {
                Vector2 near = M.Road[0];
                float bd = float.MaxValue;
                foreach (var q in M.Road) { float d = Vector2.Distance(q, gp); if (d < bd) { bd = d; near = q; } }
                if (bd < 6f) continue;
                var br = FindPath(near, gp, cost, M.RoadGrade);
                if (br == null) M.Notes.Add("枝道が見つからない");
                else M.Branches.Add(br);
            }

            // 泉（湧水の淵）・滝つぼ・川
            var spring = CarvePond(springC, r.Range(5.5f, 7f), 1.8f);
            var river = CarveStream("stream.r3", riverPts, r.Range(2f, 2.6f), 0.6f);
            var cum = Cumulative(riverPts);
            int plunge = -1;
            float bestDrop = 0f;
            for (int k = 1; k < riverPts.Count; k++)
            {
                float drop = river.Surface[k - 1] - river.Surface[k];
                if (drop > bestDrop && M.InsidePlayable(riverPts[k], 6f)) { bestDrop = drop; plunge = k; }
            }
            // 滝の落差は崖の段で一気に落ちる分と、その前後で数えた分を合わせる
            float totalDrop = 0f;
            if (plunge > 0)
            {
                for (int k = 1; k < riverPts.Count; k++)
                    if (Mathf.Abs(cum[k] - cum[plunge]) < 9f) totalDrop += Mathf.Max(0f, river.Surface[k - 1] - river.Surface[k]);
                int pool = Mathf.Min(riverPts.Count - 1, plunge + 1);
                river.Falls.Add(pool);
                for (int q = pool; q < Mathf.Min(riverPts.Count, pool + 4); q++)
                {
                    river.Depth[q] = 3.3f;
                    river.Radius[q] = 6f;
                }
                AddStructure(FieldStructureKind.Waterfall, riverPts[plunge - 1], YawOf(riverPts[plunge] - riverPts[plunge - 1]), totalDrop, null, river.Surface[plunge - 1]);
            }
            if (totalDrop < WorldTuning.WaterfallMinDrop) M.Notes.Add($"滝の落差が{totalDrop:0.0}m");

            var roadT = PlanRoadHeights(M.Road, gates[StarSkeleton.StreamSlope].h, gates[StarSkeleton.Ridge].h, M.RoadGrade, M.Streams, r);
            var branchT = new List<float[]>();
            var rcum = Cumulative(M.Road);
            foreach (var br in M.Branches)
            {
                var start = br[0];
                float along = 0f, bd = float.MaxValue;
                for (int k = 0; k < M.Road.Count; k++) { float d = Vector2.Distance(M.Road[k], start); if (d < bd) { bd = d; along = rcum[k]; } }
                float hStart = ValueAlong(rcum, roadT, along);
                float hEnd = GateEndHeight(br[br.Count - 1]);
                branchT.Add(PlanRoadHeights(br, hStart, hEnd, M.RoadGrade, M.Streams, r));
            }
            ApplyStream(river);
            SettleStones();
            // 枝道を先に、本道を最後に均す（分かれ目で枝道が本道を上書きしない）
            for (int b = 0; b < M.Branches.Count; b++) FlattenAlong(M.Branches[b], branchT[b], WorldTuning.FieldRoadHalfWidth, 3.5f);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 3.5f);

            // 結晶の岩場（山腹のベンチの上）
            bool cOk = false;
            for (int k = 0; k < 300 && !cOk; k++)
            {
                var c = new Vector2(r.Range(30f, W - 30f), r.Range(D * 0.3f, D * 0.85f));
                if (M.DistToPaths(c) < 14f || M.DistToPaths(c) > 45f) continue;
                if (FieldMap.DistToPolyline(c, riverPts) < 25f || Vector2.Distance(c, springC) < 30f) continue;
                if (M.Slope(c.x, c.y) > 25f) continue;
                crystalC = c;
                cOk = true;
            }
            if (!cOk) M.Notes.Add("結晶の岩場の場所がない");
            // 崖の洞穴（段や滝の崖の根もと、歩ける地面のそば）
            int caves = 0;
            for (int k = 0; k < 5000 && caves < 3; k++)
            {
                var c = new Vector2(r.Range(20f, W - 20f), r.Range(20f, D - 30f));
                if (M.Slope(c.x, c.y) > 26f || M.DistToPaths(c) < 6f || M.DistToPaths(c) > 60f) continue;
                if (M.WaterNear(c.x, c.y, 4f)) continue;
                // 2.5m 先が高い崖
                for (int a = 0; a < 8; a++)
                {
                    float ang = a / 8f * Mathf.PI * 2f;
                    var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    var wall = c + dir * 3f;
                    if (M.SampleTerrain(wall.x, wall.y) - M.SampleTerrain(c.x, c.y) < 2.6f) continue;
                    bool far = true;
                    foreach (var s in M.Structures) if (s.Kind == FieldStructureKind.CaveMouth && Vector2.Distance(s.Local, c) < 40f) far = false;
                    if (!far) break;
                    AddStructure(FieldStructureKind.CaveMouth, c + dir * 1.2f, YawOf(-dir), 1f);
                    AddPlace("r3.cave" + caves, "崖の洞穴", c, pocket: true);
                    caveSpots.Add(c);
                    caves++;
                    break;
                }
            }
            if (caves < 2) M.Notes.Add($"崖の洞穴が{caves}つ");

            // 脇道：泉・山頂・結晶の岩場
            AddTrailTo(springC + (view - springC).normalized * 9f);
            foreach (var cs in caveSpots) AddTrailTo(cs);
            AddTrailTo(peak);
            if (cOk) AddTrailTo(crystalC);
            // 滝つぼのそばの東屋
            var gz = view + new Vector2(0f, -7f);
            if (M.WaterNear(gz.x, gz.y, 4f)) gz = view + new Vector2(7f, 0f);
            FlattenDisc(gz, 3.6f, 2.5f, M.SampleTerrain(gz.x, gz.y));
            AddStructure(FieldStructureKind.Gazebo, gz, YawOf(fallBot - gz));
            for (int b = 0; b < M.Branches.Count; b++) FlattenAlong(M.Branches[b], branchT[b], WorldTuning.FieldRoadHalfWidth, 1.2f);
            FlattenAlong(M.Road, roadT, WorldTuning.FieldRoadHalfWidth, 1.2f);

            foreach (var kv in gates)
            {
                AddGate(kv.Key, kv.Value.p, kv.Value.inward);
                AddStructure(FieldStructureKind.Signpost, kv.Value.p + kv.Value.inward * 14f + new Vector2(-kv.Value.inward.y, kv.Value.inward.x) * 4.2f, 0f, 1f, kv.Key);
            }
            AddStructure(FieldStructureKind.Crystal, peak, 0f, 1.6f);
            SettleHeights();

            AddPlace("r3.falls", "滝つぼのそば", view);
            AddPlace("r3.spring", "泉のほとり", springC + (view - springC).normalized * 9f, pocket: true);
            AddPlace("r3.summit", "山頂の見晴らし岩", peak, pocket: true);
            if (cOk) AddPlace("r3.crystal", "結晶の岩場", crystalC, pocket: true);
            AddPlace("r3.canyon", "こだまの渓谷", NearestDryTo(riverPts, (canyonZ0 + canyonZ1) * 0.5f), pocket: true);
            spring.WaterIndex = -1;
        }

        private readonly List<Vector2> caveSpots = new List<Vector2>();

        private void AddEdgeGateLocal(string other, int edge, float along)
        {
            EdgePoint(edge, along, out var p, out var inward);
            gates[other] = (p, inward, GateHeight(other));
        }

        private float GateEndHeight(Vector2 end)
        {
            foreach (var kv in gates) if (Vector2.Distance(kv.Value.p, end) < 1f) return kv.Value.h;
            return M.SampleTerrain(end.x, end.y);
        }

        private float FallStepAt(Vector2 l)
        {
            float zs = fallZ + Mathf.Sin(l.x * fallFreq + fallPhase) * fallWob;
            float pass = 0f;
            foreach (var px in fallPasses)
            {
                float u = (l.x - px) / 12f;
                pass = Mathf.Max(pass, Mathf.Exp(-u * u));
            }
            float width = Mathf.Lerp(2.5f, fallDrop / 0.2f, pass);
            return fallDrop * GeoUtil.SmoothStep(zs - width * 0.5f, zs + width * 0.5f, l.y);
        }

        private float RimAt(Vector2 l)
        {
            float W = M.Width, D = M.Depth;
            float dx = Mathf.Max(0f, Mathf.Max(6f - l.x, l.x - (W - 6f)));
            float dz = Mathf.Max(0f, Mathf.Max(6f - l.y, l.y - (D - 6f)));
            float corridor = 1f;
            foreach (var kv in gates)
            {
                float d = Vector2.Distance(l, kv.Value.p);
                corridor = Mathf.Min(corridor, GeoUtil.SmoothStep(8f, 26f, d));
            }
            return (GeoUtil.SmoothStep(0f, 34f, dx) * 22f + GeoUtil.SmoothStep(0f, 34f, dz) * 22f) * corridor;
        }

        private Vector2 NearestDryTo(List<Vector2> line, float z)
        {
            Vector2 best = line[0];
            float bd = float.MaxValue;
            foreach (var p in line) { float d = Mathf.Abs(p.y - z); if (d < bd) { bd = d; best = p; } }
            for (float rr = 4f; rr < 30f; rr += 1f)
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f;
                    var q = best + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (M.InsidePlayable(q, 3f) && M.WaterAt(q.x, q.y) == FieldWater.None && M.Slope(q.x, q.y) < 28f) return q;
                }
            return best;
        }

        private void AddTrailTo(Vector2 target)
        {
            Vector2 near = M.Road[0];
            float bd = float.MaxValue;
            foreach (var q in M.Road) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            foreach (var b in M.Branches) foreach (var q in b) { float d = Vector2.Distance(q, target); if (d < bd) { bd = d; near = q; } }
            var pts = FindPath(near, target, j => 0f, 0.2f);
            if (pts == null) return;
            // 脇道は両端を地面の高さに固定し、間をならす（急でもよい。歩いて登れる 45% まで）
            var t = RoadTargets(pts, 0.45f, M.SampleTerrain(near.x, near.y), M.SampleTerrain(target.x, target.y), 6f);
            FlattenAlong(pts, t, WorldTuning.FieldTrailHalfWidth, 1.6f);
            M.Trails.Add(pts);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            var l = new Vector2(lx, lz);
            if (Vector2.Distance(l, crystalC) < 22f) return FieldPaint.Rock;
            float tz = lz / M.Depth;
            if (Vector2.Distance(l, peak) < peakR * 0.35f) return noise > 0.45f ? FieldPaint.Rock : FieldPaint.Meadow;
            if (tz < 0.35f) return noise > 0.4f ? FieldPaint.Forest : FieldPaint.Grass;
            return noise > 0.6f ? FieldPaint.Forest : noise > 0.4f ? FieldPaint.Grass : FieldPaint.Meadow;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 2400; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                bool outside = !M.InsidePlayable(l, 0f);
                float tz = l.y / D;
                float density = outside ? 0.85f : Mathf.Lerp(0.55f, 0.12f, GeoUtil.SmoothStep(0.25f, 0.85f, tz));
                if (Vector2.Distance(l, peak) < peakR * 0.3f) density *= 0.15f;
                if (Vector2.Distance(l, crystalC) < 24f) density *= 0.1f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 3f, 42f, true)) continue;
                bool nearGate = false;
                foreach (var kv in gates) if (Vector2.Distance(kv.Value.p, l) < 10f) nearGate = true;
                if (nearGate) continue;
                AddNature(r.Chance(0.7f) ? NatureKind.TreeTall : NatureKind.TreeRound, l, r.Range(0.85f, 1.4f), r, tz < 0.35f ? AreaType.Forest : AreaType.Mountain);
            }
            // 岩（山の上ほど多い）・大岩
            for (int k = 0; k < 700; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!r.Chance(0.25f + 0.6f * l.y / D)) continue;
                if (!NatureOk(l, 3f, 1.5f, 55f)) continue;
                AddNature(r.Chance(0.3f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.6f), r, AreaType.Mountain);
            }
            // 結晶の岩場
            for (int k = 0; k < 90; k++)
            {
                var l = crystalC + r.InsideUnitCircle() * 20f;
                if (!NatureOk(l, 2f, 1.5f, 40f)) continue;
                AddNature(k % 4 == 0 ? NatureKind.TreeCrystal : NatureKind.Crystal, l, r.Range(0.8f, 1.4f), r, AreaType.Mountain);
            }
            // 川の岩・草・花・キノコ・倒木
            for (int k = 0; k < 300; k++)
            {
                var l = riverPts[r.Range(0, riverPts.Count)] + r.InsideUnitCircle() * 9f;
                if (!M.InsidePlayable(l, 1f) || M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 3f) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.5f), r, AreaType.Riverside);
            }
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-6f, W + 6f), r.Range(-6f, D + 6f));
                if (!NatureOk(l, 1.8f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Mountain);
            }
            for (int k = 0; k < 300; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Mountain);
            }
            for (int k = 0; k < 120; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D * 0.5f));
                if (!NatureOk(l, 3f, 1.5f, 30f)) continue;
                AddNature(k < 6 ? NatureKind.FallenLog : NatureKind.Mushroom, l, r.Range(0.8f, 1.2f), r, AreaType.Forest);
            }
        }

        protected override void Validate()
        {
            if (M.Ponds.Count < 1) M.Notes.Add("泉がない");
            if (M.Streams.Count == 0 || M.Streams[0].Falls.Count == 0) M.Notes.Add("滝つぼがない");
            int pockets = 0;
            foreach (var p in M.Places) if (p.Pocket) pockets++;
            if (pockets < 4) M.Notes.Add($"寄り道の場所が{pockets}か所");
            // §5：50〜140m（試作では南の入口は 30〜50m）。山腹の段・滝の崖・渓谷
            RequireRelief(60f, 2.5f);
        }
    }
}
