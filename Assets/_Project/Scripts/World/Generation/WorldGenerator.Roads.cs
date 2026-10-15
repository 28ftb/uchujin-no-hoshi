using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // 道路・橋・近道、住宅区画、ベンチ・街灯・井戸、看板、地形の仕上げ（村仕様 §3.2、§5、§15 手順4〜7）
    public sealed partial class WorldGenerator
    {
        private sealed class RoadPathInfo
        {
            public string DestId;
            public readonly List<Vector2> Points = new List<Vector2>();
            public bool Residential = true;
        }

        private readonly List<RoadPathInfo> roadPaths = new List<RoadPathInfo>();
        private int plazaNode = -1;
        private const float NodeMergeDist = 3.0f;
        private readonly List<BridgeInfo> pendingBridges = new List<BridgeInfo>();
        private Vector2 forestTrailEnd;
        private bool hasForestTrail;

        // =================================================================
        // 道路網（A*）
        // =================================================================

        private int AddNode(Vector2 p, bool allowMerge = true)
        {
            var nodes = L.Roads.nodes;
            if (allowMerge)
            {
                int best = -1;
                float bestD = NodeMergeDist;
                for (int i = 0; i < nodes.Count; i++)
                {
                    float d = Vector2.Distance(new Vector2(nodes[i].x, nodes[i].z), p);
                    if (d < bestD)
                    {
                        bestD = d;
                        best = i;
                    }
                }
                if (best >= 0) return best;
            }
            nodes.Add(new Vector3(p.x, 0f, p.y));
            return nodes.Count - 1;
        }

        private int AddEdge(int a, int b, bool bridge = false, float halfWidth = WorldTuning.RoadHalfWidth)
        {
            if (a == b || a < 0 || b < 0) return -1;
            var edges = L.Roads.edges;
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                if ((e.x == a && e.y == b) || (e.x == b && e.y == a)) return i;
            }
            edges.Add(new Vector2Int(a, b));
            int idx = edges.Count - 1;
            var pa = NodeXZ(a);
            var pb = NodeXZ(b);
            if (bridge) L.Roads.bridgeEdges.Add(idx);
            planner?.MarkRoad(pa, pb);
            bool paved = Vector2.Distance(pa, plaza) < W.PavedRadius && Vector2.Distance(pb, plaza) < W.PavedRadius;
            W.RoadSegments.Add(new RoadSegment { A = pa, B = pb, Paved = paved, Bridge = bridge, HalfWidth = halfWidth });
            return idx;
        }

        private Vector2 NodeXZ(int i) => new Vector2(L.Roads.nodes[i].x, L.Roads.nodes[i].z);

        private float ExtraCost(Vector2 c)
        {
            float extra = 0f;
            if (W.FarmReserve.width > 0f && W.FarmReserve.Contains(c)) extra += 25f;
            if (Vector2.Distance(c, crashCenter) < WorldTuning.CrashCraterRadius + 1f) extra += 30f;
            if (S.IsForest(c.x, c.y)) extra += 0.5f;
            if (S.CoastD(c.x, c.y) < 10f) extra += 1.5f;
            return extra;
        }

        private void BuildRoads()
        {
            planner = new RoadPlanner(F, WorldTuning.PathCell);
            planner.Refresh(footprints, ExtraCost);
            plazaNode = AddNode(plaza, false);

            // 広場の施設：入口から広場の中心へ
            foreach (var f in ringFacilities)
            {
                var e = GeoUtil.XZ(f.entrance);
                if (Vector2.Distance(e, plaza) < 45f)
                {
                    int n = AddNode(e);
                    AddEdge(plazaNode, n);
                }
                else Route(plaza, e, f.id, false);
            }

            // 主要な目的地
            var dests = new List<(Vector2 p, string id)>();
            foreach (var f in L.Facilities)
            {
                switch (f.type)
                {
                    case FacilityType.PlayerHouse:
                    case FacilityType.RocketSite:
                    case FacilityType.MineEntrance:
                    case FacilityType.FishingHut:
                    case FacilityType.Lookout:
                        dests.Add((GeoUtil.XZ(f.entrance), f.id));
                        break;
                }
            }
            if (W.HasPier) dests.Add((GeoUtil.XZ(W.PierStart), "pier"));
            dests.Sort((a, b) => Vector2.Distance(a.p, plaza).CompareTo(Vector2.Distance(b.p, plaza)));
            foreach (var d in dests) Route(plaza, d.p, d.id, true);

            // 近道・別ルート（村仕様 §3.2：一本道にせず、複数の経路・近道を作る）
            var r = R("roads.loops");
            var pairs = new List<(Vector2, Vector2, string)>();
            for (int i = 0; i < dests.Count; i++)
                for (int j = i + 1; j < dests.Count; j++)
                {
                    float dd = Vector2.Distance(dests[i].p, dests[j].p);
                    if (dd > 40f && dd < 190f) pairs.Add((dests[i].p, dests[j].p, dests[i].id + "~" + dests[j].id));
                }
            r.Shuffle(pairs);
            int loops = 0;
            foreach (var pr in pairs)
            {
                if (loops >= 2) break;
                if (Route(pr.Item1, pr.Item2, pr.Item3, true) != null) loops++;
            }

            // 森の小道（遠回りには生き物との出会いという別の価値：村仕様 §3.2）
            Vector2 bestF = Vector2.zero;
            float bestV = 0f;
            for (int k = 0; k < 300; k++)
            {
                var p = new Vector2(r.Range(-170f, 170f), r.Range(-170f, 170f));
                float d = Vector2.Distance(p, plaza);
                if (d < 60f || d > 160f) continue;
                if (F.WaterNear(p.x, p.y, 8f)) continue;
                if (S.MountainFactor(p.x, p.y) < 0.95f) continue;
                if (!SiteOk(p, 5f, false, 6f)) continue;
                float v = S.Forest01(p.x, p.y);
                if (v > bestV)
                {
                    bestV = v;
                    bestF = p;
                }
            }
            if (bestV > S.ForestThreshold)
            {
                int near = NearestNode(bestF);
                if (near >= 0)
                {
                    var trail = Route(NodeXZ(near), bestF, "forest_trail", false, WorldTuning.RoadHalfWidth * 0.7f);
                    if (trail != null)
                    {
                        hasForestTrail = true;
                        forestTrailEnd = bestF;
                        reserved.Add((bestF, 6f));
                    }
                }
            }
        }

        private int NearestNode(Vector2 p, int exclude = -1)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < L.Roads.nodes.Count; i++)
            {
                if (i == exclude) continue;
                float d = Vector2.Distance(NodeXZ(i), p);
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>A* で経路を求め、道路グラフへ追加する。川を渡る所は橋になる。</summary>
        private RoadPathInfo Route(Vector2 from, Vector2 to, string destId, bool residential, float halfWidth = WorldTuning.RoadHalfWidth)
        {
            var cells = planner.FindPath(from, to);
            if (cells == null || cells.Count == 0)
            {
                W.Notes.Add("経路なし: " + destId);
                return null;
            }
            var pts = new List<Vector2>(cells.Count);
            for (int i = 0; i < cells.Count; i++) pts.Add(planner.CenterOf(cells[i]));
            pts[0] = from;
            pts[pts.Count - 1] = to;

            var info = new RoadPathInfo { DestId = destId, Residential = residential };
            int prev = AddNode(from);
            info.Points.Add(NodeXZ(prev));
            int lastKept = 0;
            int idx = 1;
            while (idx < cells.Count)
            {
                bool wet = planner.IsRiverCell(cells[idx]) && idx < cells.Count - 1;
                if (wet)
                {
                    int j = idx;
                    while (j < cells.Count && planner.IsRiverCell(cells[j])) j++;
                    if (j >= cells.Count) j = cells.Count - 1;
                    var a = pts[idx - 1];
                    var b = pts[j];
                    int na = AddNode(a);
                    if (na != prev) AddEdge(prev, na, false, halfWidth);
                    int nb = AddNode(b, false);
                    int e = AddEdge(na, nb, true, halfWidth);
                    float maxS = WorldTuning.SeaLevel;
                    for (int s = 0; s <= 10; s++)
                    {
                        var q = Vector2.Lerp(a, b, s / 10f);
                        float ws = F.WaterSurfaceAt(q.x, q.y);
                        if (!float.IsNaN(ws)) maxS = Mathf.Max(maxS, ws);
                    }
                    pendingBridges.Add(new BridgeInfo { A = NodeXZ(na), B = NodeXZ(nb), DeckY = maxS + 0.8f, EdgeIndex = e });
                    info.Points.Add(NodeXZ(na));
                    info.Points.Add(NodeXZ(nb));
                    prev = nb;
                    lastKept = j;
                    idx = j + 1;
                    continue;
                }
                bool isLast = idx == cells.Count - 1;
                bool nextWet = idx + 1 < cells.Count && planner.IsRiverCell(cells[idx + 1]);
                if (isLast || nextWet || idx - lastKept >= 2)
                {
                    int n = AddNode(pts[idx]);
                    if (n != prev) AddEdge(prev, n, false, halfWidth);
                    prev = n;
                    lastKept = idx;
                    info.Points.Add(NodeXZ(n));
                }
                idx++;
            }
            roadPaths.Add(info);
            return info;
        }

        // =================================================================
        // 広場から離れた施設（ロケット仕様 §15.2：修理屋が鉱山に近い星もある）
        // =================================================================

        private void PlaceSatelliteFacilities()
        {
            if (satelliteBlacksmith) PlaceAlongRoad(FacilityType.Blacksmith, IdOf(FacilityType.MineEntrance));
            if (satelliteRepair) PlaceAlongRoad(FacilityType.RepairWorkshop, IdOf(FacilityType.RocketSite));
        }

        private void PlaceAlongRoad(FacilityType type, string destId)
        {
            var size = SizeOf(type);
            RoadPathInfo path = null;
            foreach (var rp in roadPaths) if (rp.DestId == destId) path = rp;
            if (path != null && path.Points.Count >= 4)
            {
                float[] fracs = { 0.5f, 0.6f, 0.4f, 0.7f, 0.35f };
                foreach (var frac in fracs)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt((path.Points.Count - 1) * frac), 1, path.Points.Count - 2);
                    var p = path.Points[i];
                    var dir = (path.Points[i + 1] - path.Points[i - 1]).normalized;
                    var n = new Vector2(-dir.y, dir.x);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var c = p + n * side * (size.z * 0.5f + 6.5f);
                        if (Vector2.Distance(c, plaza) < 40f) continue;
                        if (!SiteOk(c, FootRadius(size) * 0.85f, false, 4.5f, true)) continue;
                        var f = AddBuilding(type, IdOf(type), c, YawOf(p - c), size);
                        ConnectToRoad(GeoUtil.XZ(f.entrance));
                        RefreshPlannerFootprints();
                        return;
                    }
                }
            }
            // 予備：広場の空き枠
            PlaceInRing(type, 0);
            var placed = L.GetFacility(type);
            if (placed != null) ConnectToRoad(GeoUtil.XZ(placed.entrance), true);
            RefreshPlannerFootprints();
        }

        private void RefreshPlannerFootprints()
        {
            // 経路探索のコストは道路の記録を保ったまま、建物の通行不可だけを追加する
            planner.Refresh(footprints, ExtraCost);
            foreach (var seg in W.RoadSegments) planner.MarkRoad(seg.A, seg.B);
        }

        /// <summary>入口から最寄りの道路ノードへつなぐ（遠ければ A*）。</summary>
        private void ConnectToRoad(Vector2 entrance, bool viaPlaza = false, float halfWidth = 1.3f)
        {
            int n = AddNode(entrance);
            int near = NearestNode(entrance, n);
            if (near < 0) return;
            var np = NodeXZ(near);
            if (viaPlaza && Vector2.Distance(entrance, plaza) < 45f)
            {
                AddEdge(plazaNode, n);
                return;
            }
            if (Vector2.Distance(np, entrance) < 16f && !SegmentCrossesWater(np, entrance)) AddEdge(near, n, false, halfWidth);
            else
            {
                RefreshPlannerFootprints();
                Route(np, entrance, "spur", false, halfWidth);
            }
        }

        private bool SegmentCrossesWater(Vector2 a, Vector2 b)
        {
            float len = (b - a).magnitude;
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / 1.5f));
            for (int i = 0; i <= steps; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)steps);
                if (F.IsWater(p.x, p.y)) return true;
            }
            return false;
        }

        // =================================================================
        // 住宅区画（村仕様 §5.1：広場周辺・川沿い・森の近く・高台など地形に応じて配置）
        // =================================================================

        private void PlaceHousePlots()
        {
            var r = R("plots");
            var size = SizeOf(FacilityType.ResidentHouse);
            float plotRadius = 6.2f;
            var order = new List<RoadPathInfo>();
            foreach (var rp in roadPaths) if (rp.Residential) order.Add(rp);
            r.Shuffle(order);
            int target = WorldTuning.HousePlotTarget;

            foreach (var rp in order)
            {
                if (L.HousePlots.Count >= target) break;
                float acc = r.Range(0f, WorldTuning.HousePlotSpacing);
                for (int i = 1; i < rp.Points.Count && L.HousePlots.Count < target; i++)
                {
                    var a = rp.Points[i - 1];
                    var b = rp.Points[i];
                    float segLen = (b - a).magnitude;
                    if (segLen < 0.01f) continue;
                    var dir = (b - a) / segLen;
                    var n = new Vector2(-dir.y, dir.x);
                    float t = acc;
                    while (t < segLen && L.HousePlots.Count < target)
                    {
                        var p = a + dir * t;
                        float dPlaza = Vector2.Distance(p, plaza);
                        if (dPlaza > 38f && dPlaza < WorldTuning.HousePlotMaxDistFromPlaza)
                        {
                            int firstSide = r.Chance(0.5f) ? 1 : -1;
                            bool added = false;
                            for (int s = 0; s < 4 && !added; s++)
                            {
                                int side = (s % 2 == 0) ? firstSide : -firstSide;
                                float off = WorldTuning.HousePlotRoadOffset + (s / 2) * 2.5f;
                                var c = p + n * side * off;
                                added = TryAddPlot(c, YawOf(p - c), plotRadius);
                            }
                        }
                        t += WorldTuning.HousePlotSpacing;
                    }
                    acc = t - segLen;
                }
            }

            // 足りなければ広場の外周に追加（私道をつなぐ）
            int guard = 0;
            while (L.HousePlots.Count < WorldTuning.HousePlotTarget && guard++ < 1500)
            {
                float ang = r.Range(0f, 360f);
                float rad = r.Range(45f, 150f);
                var c = plaza + Fwd2(ang) * rad;
                int near = NearestNode(c);
                if (near < 0) break;
                var np = NodeXZ(near);
                var c2 = np + (c - np).normalized * WorldTuning.HousePlotRoadOffset;
                if (Vector2.Distance(np, c) < 3f) continue;
                TryAddPlot(c2, YawOf(np - c2), plotRadius);
            }
            if (L.HousePlots.Count < WorldTuning.HousePlotMinimum) W.Notes.Add("住宅区画が不足: " + L.HousePlots.Count);
        }

        private bool TryAddPlot(Vector2 c, float yaw, float radius)
        {
            if (!SiteOk(c, radius, false, 5.2f, true, 1.0f)) return false;
            if (Vector2.Distance(c, crashCenter) < WorldTuning.CrashCraterRadius + 14f) return false;
            if (Vector2.Distance(c, mineMouth) < 16f) return false;
            if (Vector2.Distance(c, lookoutCenter) < 12f) return false;
            int index = L.HousePlots.Count;
            string id = "facility.house." + index;
            var size = SizeOf(FacilityType.ResidentHouse);
            var fwd = Fwd2(yaw);
            var entrance = c + fwd * (size.z * 0.5f + 1.6f);
            var f = AddFacility(FacilityType.ResidentHouse, id, c, yaw, size, entrance);
            f.plotIndex = index;
            AddPad(PadKind.HousePlot, c + fwd * 0.6f, new Vector2(5.4f, 5.2f), yaw, false, 3.5f, id);
            AddFootprint(c, new Vector2(size.x * 0.5f + 0.3f, size.z * 0.5f + 0.3f), yaw);
            L.HousePlots.Add(new HousePlot
            {
                index = index,
                position = new Vector3(c.x, 0f, c.y),
                rotationY = yaw,
                occupied = false,
                facilityId = id
            });
            ConnectToRoad(entrance, false, 1.2f);
            return true;
        }

        // =================================================================
        // 井戸・ベンチ・街灯・広場の目印
        // =================================================================

        private int benchCount;
        private int lightCount;

        private List<float> PlazaGapAngles(int count)
        {
            var angs = new List<float>();
            foreach (var e in L.Roads.edges)
            {
                int other = e.x == plazaNode ? e.y : e.y == plazaNode ? e.x : -1;
                if (other < 0) continue;
                var d = NodeXZ(other) - plaza;
                angs.Add(Mathf.Repeat(YawOf(d), 360f));
            }
            angs.Sort();
            var gaps = new List<(float mid, float size)>();
            if (angs.Count == 0)
            {
                for (int i = 0; i < count; i++) gaps.Add((i * 360f / count, 360f / count));
            }
            else
            {
                for (int i = 0; i < angs.Count; i++)
                {
                    float a = angs[i];
                    float b = i + 1 < angs.Count ? angs[i + 1] : angs[0] + 360f;
                    gaps.Add((Mathf.Repeat((a + b) * 0.5f, 360f), b - a));
                }
            }
            gaps.Sort((x, y) => y.size.CompareTo(x.size));
            var result = new List<float>();
            for (int i = 0; i < count; i++) result.Add(gaps[i % gaps.Count].mid + (i / gaps.Count) * 17f);
            return result;
        }

        private void AddSmallFacility(FacilityType type, string id, Vector2 c, float yaw)
        {
            var size = SizeOf(type);
            var f = AddFacility(type, id, c, yaw, size, c + Fwd2(yaw) * (size.z * 0.5f + 0.9f));
            if (type == FacilityType.Well) AddFootprint(c, new Vector2(1.3f, 1.3f), yaw, true);
        }

        private void PlaceStreetFurniture()
        {
            var r = R("furniture");
            var gaps = PlazaGapAngles(6);
            // 広場の目印の大きな木（村仕様 §4.1【提案】）・井戸・掲示板
            W.PlazaLandmark = GeoUtil.X0Z(plaza + Fwd2(gaps[0]) * 7.5f);
            reserved.Add((GeoUtil.XZ(W.PlazaLandmark), 3f));
            AddFootprint(GeoUtil.XZ(W.PlazaLandmark), new Vector2(1.6f, 1.6f), 0f, true);
            var wellPos = plaza + Fwd2(gaps[1]) * 7f;
            AddSmallFacility(FacilityType.Well, IdOf(FacilityType.Well), wellPos, YawOf(plaza - wellPos));
            var boardPos = plaza + Fwd2(gaps[2]) * (WorldTuning.PlazaRadius - 2.5f);
            var board = AddFacility(FacilityType.BulletinBoard, IdOf(FacilityType.BulletinBoard), boardPos, YawOf(plaza - boardPos), SizeOf(FacilityType.BulletinBoard),
                boardPos + (plaza - boardPos).normalized * 1.6f);
            AddFootprint(boardPos, new Vector2(1.6f, 0.5f), board.rotationY);

            // 広場のベンチ
            for (int i = 3; i < 6; i++)
            {
                var c = plaza + Fwd2(gaps[i]) * (WorldTuning.PlazaRadius - 2f);
                AddBench(c, YawOf(plaza - c));
            }
            // 広場の街灯
            for (int i = 0; i < 6; i++)
            {
                float a = gaps[i % gaps.Count] + 12f;
                AddLight(plaza + Fwd2(a) * (WorldTuning.PlazaRadius + 0.6f));
            }

            // 道沿いの街灯（村から 150m 以内）
            float acc = 0f;
            int sideFlip = 1;
            foreach (var seg in W.RoadSegments)
            {
                if (lightCount >= 34) break;
                if (seg.Bridge) continue;
                float len = (seg.B - seg.A).magnitude;
                acc += len;
                if (acc < 26f) continue;
                acc = 0f;
                var mid = (seg.A + seg.B) * 0.5f;
                if (Vector2.Distance(mid, plaza) < WorldTuning.PlazaRadius + 6f || Vector2.Distance(mid, plaza) > 150f) continue;
                var dir = (seg.B - seg.A).normalized;
                var p = mid + new Vector2(-dir.y, dir.x) * sideFlip * (seg.HalfWidth + 0.9f);
                sideFlip = -sideFlip;
                if (F.WaterNear(p.x, p.y, 1.5f)) continue;
                bool blockedSpot = false;
                foreach (var fp in footprints) if (fp.SignedDistance(p.x, p.y) < 1f) blockedSpot = true;
                if (blockedSpot) continue;
                AddLight(p);
            }
            // 橋のたもと・水辺のベンチ
            foreach (var br in pendingBridges)
            {
                var dir = (br.B - br.A).normalized;
                var n = new Vector2(-dir.y, dir.x);
                AddLight(br.A - dir * 1.5f + n * 2.4f);
                var benchPos = br.A - dir * 3.5f - n * 4f;
                if (!F.WaterNear(benchPos.x, benchPos.y, 1.2f) && benchCount < 14) AddBench(benchPos, YawOf(br.Mid.x - benchPos.x, br.Mid.z - benchPos.y));
            }
            foreach (var pd in W.Ponds)
            {
                var dir = (plaza - pd.Center).normalized;
                var c = pd.Center + dir * (pd.Radius + 3.2f);
                if (!F.WaterNear(c.x, c.y, 1.2f)) AddBench(c, YawOf(pd.Center - c));
            }
            {
                var dir = (plaza - lookoutCenter).normalized;
                var c = lookoutCenter - dir * 3.5f;
                AddBench(c, YawOf(-dir));
            }
            if (W.HasPier)
            {
                var s = GeoUtil.XZ(W.PierStart);
                var e = GeoUtil.XZ(W.PierEnd);
                var n = new Vector2(-(e - s).normalized.y, (e - s).normalized.x);
                AddBench(s + n * 3.5f - (e - s).normalized * 1.5f, YawOf(e - s));
                AddLight(s - n * 2.4f);
            }
            AddLight(mineMouth + (plaza - mineMouth).normalized * 3.5f + Right2(YawOf(plaza - mineMouth)) * 3.2f);
        }

        private static float YawOf(float dx, float dz) => Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;

        private void AddBench(Vector2 c, float yaw)
        {
            string id = "facility.bench." + benchCount;
            benchCount++;
            AddSmallFacility(FacilityType.Bench, id, c, yaw);
            reserved.Add((c, 1.4f));
        }

        private void AddLight(Vector2 c)
        {
            string id = "facility.light." + lightCount;
            lightCount++;
            AddSmallFacility(FacilityType.Streetlight, id, c, 0f);
            reserved.Add((c, 1f));
        }

        // =================================================================
        // 看板と道標（宇宙語の地名：Language が "PLACE:"+facilityId から生成）
        // =================================================================

        private void PlaceSigns()
        {
            int n = 0;
            foreach (var f in L.Facilities)
            {
                bool named;
                switch (f.type)
                {
                    case FacilityType.PlayerShop:
                    case FacilityType.MessagePlaza:
                    case FacilityType.Blacksmith:
                    case FacilityType.RepairWorkshop:
                    case FacilityType.SeedShop:
                    case FacilityType.GeneralStore:
                    case FacilityType.CarpenterWorkshop:
                    case FacilityType.FishingHut:
                    case FacilityType.CommunityKitchen:
                    case FacilityType.Lookout:
                    case FacilityType.MineEntrance:
                        named = true;
                        break;
                    default:
                        named = false;
                        break;
                }
                if (!named) continue;
                var c = GeoUtil.XZ(f.position);
                var e = GeoUtil.XZ(f.entrance);
                var right = Right2(f.rotationY);
                Vector2 pos;
                if (f.type == FacilityType.MineEntrance) pos = e + right * 3.4f;
                else if (f.type == FacilityType.Lookout) pos = e + right * 1.8f;
                else pos = c + Fwd2(f.rotationY) * (f.size.z * 0.5f + 1.2f) + right * (f.size.x * 0.5f - 0.4f);
                W.Signs.Add(new SignInfo { Id = "sign." + n++, FacilityId = f.id, Position = GeoUtil.X0Z(pos), Yaw = f.rotationY });
            }
            // 広場の看板
            var gaps = PlazaGapAngles(7);
            var plazaSign = plaza + Fwd2(gaps[6]) * (WorldTuning.PlazaRadius - 0.8f);
            W.Signs.Add(new SignInfo { Id = "sign." + n++, FacilityId = IdOf(FacilityType.Plaza), Position = GeoUtil.X0Z(plazaSign), Yaw = YawOf(plazaSign - plaza) });

            // 道標：広場から出る道の分かれ目
            var groups = new List<SignInfo>();
            string[] destIds =
            {
                IdOf(FacilityType.MineEntrance), IdOf(FacilityType.RocketSite), IdOf(FacilityType.FishingHut), IdOf(FacilityType.Lookout)
            };
            foreach (var did in destIds)
            {
                RoadPathInfo path = null;
                foreach (var rp in roadPaths) if (rp.DestId == did && path == null) path = rp;
                if (path == null) continue;
                int exitIdx = -1;
                for (int i = 0; i < path.Points.Count; i++)
                    if (Vector2.Distance(path.Points[i], plaza) > WorldTuning.PlazaRadius + 5f)
                    {
                        exitIdx = i;
                        break;
                    }
                if (exitIdx < 0) continue;
                var exitP = path.Points[exitIdx];
                int ahead = Mathf.Min(path.Points.Count - 1, exitIdx + 3);
                float arrowYaw = YawOf(path.Points[ahead] - exitP);
                SignInfo g = null;
                foreach (var s in groups)
                    if (Vector2.Distance(GeoUtil.XZ(s.Position), exitP) < 10f) g = s;
                if (g == null)
                {
                    var dir = (exitP - plaza).normalized;
                    var p = exitP + new Vector2(-dir.y, dir.x) * 2.6f;
                    g = new SignInfo { Id = "sign." + n++, IsSignpost = true, Position = GeoUtil.X0Z(p), Yaw = YawOf(dir) };
                    groups.Add(g);
                }
                if (g.Destinations.Count < 3)
                {
                    g.Destinations.Add(did);
                    g.DestinationYaws.Add(arrowYaw);
                }
            }
            W.Signs.AddRange(groups);
            foreach (var s in W.Signs) reserved.Add((GeoUtil.XZ(s.Position), 1.2f));
        }

        // =================================================================
        // 公共施設の整備予定地（村仕様 §11.2【提案】：ロケット修理の必須条件にはしない）
        // =================================================================

        private void PlanProjects()
        {
            var r = R("projects");
            var sites = new List<ProjectSite>();
            sites.Add(new ProjectSite { Id = "project.plaza_flowerbeds", NameJa = "広場の花壇", NameEn = "Plaza flowerbeds", Position = GeoUtil.X0Z(plaza), Kind = "flowerbeds" });
            var msg = L.GetFacility(FacilityType.MessagePlaza);
            if (msg != null)
                sites.Add(new ProjectSite { Id = "project.message_garland", NameJa = "伝言広場の飾り", NameEn = "Message plaza garland", Position = msg.position, Yaw = msg.rotationY, Kind = "garland" });
            sites.Add(new ProjectSite { Id = "project.lookout_gazebo", NameJa = "見晴らし台の東屋", NameEn = "Lookout gazebo", Position = GeoUtil.X0Z(lookoutCenter), Kind = "gazebo" });
            if (pendingBridges.Count > 0)
                sites.Add(new ProjectSite { Id = "project.bridge_lanterns", NameJa = "橋の灯り", NameEn = "Bridge lanterns", Position = pendingBridges[0].Mid, Kind = "bridge_lanterns" });
            if (W.HasPier)
                sites.Add(new ProjectSite { Id = "project.pier_extension", NameJa = "桟橋の延長", NameEn = "Pier extension", Position = W.PierEnd, Yaw = YawOf(GeoUtil.XZ(W.PierEnd) - GeoUtil.XZ(W.PierStart)), Kind = "pier" });
            if (W.Ponds.Count > 0)
            {
                var pd = W.Ponds[0];
                var dir = (plaza - pd.Center).normalized;
                var side = new Vector2(-dir.y, dir.x);
                var c = pd.Center + side * (pd.Radius + 2.5f);
                if (!F.WaterNear(c.x, c.y, 1.0f))
                {
                    sites.Add(new ProjectSite { Id = "project.pond_deck", NameJa = "池の木の桟橋", NameEn = "Pond deck", Position = GeoUtil.X0Z(c), Yaw = YawOf(pd.Center - c), Kind = "pond_deck" });
                    reserved.Add((c, 4f));
                }
            }
            if (hasForestTrail)
                sites.Add(new ProjectSite { Id = "project.forest_rest", NameJa = "森の休憩所", NameEn = "Forest rest spot", Position = GeoUtil.X0Z(forestTrailEnd), Kind = "forest_rest" });
            // 住宅地の小さな公園
            for (int k = 0; k < 200; k++)
            {
                if (L.HousePlots.Count == 0) break;
                var hp = r.Pick(L.HousePlots);
                var c = GeoUtil.XZ(hp.position) + new Vector2(r.Range(-22f, 22f), r.Range(-22f, 22f));
                if (!SiteOk(c, 6.5f, false, 3f, true, 1.5f)) continue;
                sites.Add(new ProjectSite { Id = "project.park", NameJa = "小さな公園", NameEn = "Little park", Position = GeoUtil.X0Z(c), Yaw = r.Range(0f, 360f), Kind = "park" });
                reserved.Add((c, 7f));
                AddPad(PadKind.Generic, c, new Vector2(6.5f, 0f), 0f, true, 3f, "project.park");
                break;
            }
            // 広場の花壇を最初に、残りはシードで順番を変える
            var first = sites[0];
            sites.RemoveAt(0);
            r.Shuffle(sites);
            sites.Insert(0, first);
            for (int i = 0; i < sites.Count; i++)
            {
                sites[i].Required = 24 + i * 8; // 【仮数値】必要な整備量
                W.ProjectSites.Add(sites[i]);
            }
        }

        // =================================================================
        // 地形の仕上げ：橋のたもと・道路の平坦化・クレーター・高さの確定
        // =================================================================

        private void FinalizeTerrain()
        {
            // 橋
            foreach (var br in pendingBridges)
            {
                float ha = F.SampleTerrain(br.A.x, br.A.y);
                float hb = F.SampleTerrain(br.B.x, br.B.y);
                br.DeckY = Mathf.Max(br.DeckY, Mathf.Max(ha, hb) + 0.05f);
                AddPad(PadKind.Generic, br.A, new Vector2(2.4f, 0f), 0f, true, 5f, null, br.DeckY);
                AddPad(PadKind.Generic, br.B, new Vector2(2.4f, 0f), 0f, true, 5f, null, br.DeckY);
                F.Bridges.Add(br);
            }
            // 道路
            var edges = L.Roads.edges;
            for (int i = 0; i < edges.Count; i++)
            {
                if (L.Roads.bridgeEdges.Contains(i)) continue;
                var seg = W.RoadSegments[i];
                FlattenRoad(seg.A, seg.B, seg.HalfWidth);
            }
            // クレーター（村仕様 §9：壊れた宇宙船が残る場所）
            ShapeCrater();
            // 高さの確定
            var nodes = L.Roads.nodes;
            for (int i = 0; i < nodes.Count; i++) nodes[i] = OnGround(nodes[i]);
            foreach (var f in L.Facilities)
            {
                f.position = OnGround(f.position);
                f.entrance = OnGround(f.entrance);
            }
            foreach (var hp in L.HousePlots) hp.position = OnGround(hp.position);
            L.CrashSite = OnGround(L.CrashSite);
            L.MineEntrance = OnGround(L.MineEntrance);
            L.PlayerSpawn = OnGround(L.PlayerSpawn);
            W.PlazaCenter = OnGround(W.PlazaCenter);
            W.PlazaLandmark = OnGround(W.PlazaLandmark);
            if (W.HasPier)
            {
                W.PierStart = OnGround(W.PierStart);
                W.PierEnd = new Vector3(W.PierEnd.x, W.PierStart.y, W.PierEnd.z);
            }
            foreach (var s in W.Signs) s.Position = OnGround(s.Position);
            foreach (var ps in W.ProjectSites) ps.Position = OnGround(ps.Position);
            if (W.Rivers.Count > 0 && W.Rivers[0].WaterfallIndex > 0)
            {
                var rv = W.Rivers[0];
                int i = rv.WaterfallIndex;
                var top = rv.Points[i - 1];
                var bot = rv.Points[i];
                W.Waterfall = new WaterfallInfo
                {
                    Top = new Vector3(top.x, rv.Surface[i - 1], top.y),
                    Bottom = new Vector3(bot.x, rv.Surface[i], bot.y),
                    Yaw = YawOf(bot - top),
                    Width = rv.Radius[i] * 2f
                };
            }
        }

        private Vector3 OnGround(Vector3 p) => new Vector3(p.x, F.SampleGround(p.x, p.z), p.z);

        private void FlattenRoad(Vector2 a, Vector2 b, float half)
        {
            float ha = F.SampleTerrain(a.x, a.y);
            float hb = F.SampleTerrain(b.x, b.y);
            float reach = half + WorldTuning.RoadBlend;
            var mn = Vector2.Min(a, b) - Vector2.one * reach;
            var mx = Vector2.Max(a, b) + Vector2.one * reach;
            F.ToGrid(mn.x, mn.y, out int x0, out int z0);
            F.ToGrid(mx.x, mx.y, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(F.XOf(ix), F.ZOf(iz));
                    float d = GeoUtil.DistToSegment(p, a, b, out float t);
                    if (d > reach) continue;
                    int idx = F.Index(ix, iz);
                    if (!float.IsNaN(F.WaterY[idx]) && F.WaterY[idx] > F.H[idx] - 0.2f) continue;
                    float target = Mathf.Lerp(ha, hb, t);
                    float w = d < half + 0.6f ? 1f : 1f - GeoUtil.SmoothStep(half + 0.6f, reach, d);
                    F.H[idx] = Mathf.Lerp(F.H[idx], target, w * 0.9f);
                }
            }
        }

        private void ShapeCrater()
        {
            float R0 = WorldTuning.CrashCraterRadius;
            F.ToGrid(crashCenter.x - R0 - 6f, crashCenter.y - R0 - 6f, out int x0, out int z0);
            F.ToGrid(crashCenter.x + R0 + 6f, crashCenter.y + R0 + 6f, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float d = Vector2.Distance(new Vector2(F.XOf(ix), F.ZOf(iz)), crashCenter);
                    int idx = F.Index(ix, iz);
                    float k = d / R0;
                    // 中央は平ら（ロケットが置かれる）、外へ向かって上がり、縁が少し盛り上がる
                    if (k < 0.45f) F.H[idx] -= 0.8f;
                    else if (k < 0.8f) F.H[idx] -= 0.8f * (1f - GeoUtil.SmoothStep(0.45f, 0.8f, k));
                    else if (k < 1.3f) F.H[idx] += 0.5f * Mathf.Sin((k - 0.8f) / 0.5f * Mathf.PI);
                }
        }
    }
}
