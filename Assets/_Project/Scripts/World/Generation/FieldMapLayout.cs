using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 地域・道中マップ1枚の配置データ（WorldLayout）を作る（生成バージョン3。マップ設計 v0.3 §10.2）。
    /// エリア（生き物の出現と天候の局所差・地名に使う）、水域、釣り場（魚50種の生息環境のタグ。§6）、生息地点（生物50種。§7）、
    /// 高さ・水・水深の判定。座標はすべてワールド座標。
    /// 釣り場のタグは村（版2）と同じ言葉を使う（瀬 riffle・浅場 shallow・中層 mid・深場 deep・最深部 deepest・泥底 mud・砂泥底 sandmud・
    /// 濁り turbid・静かな岸 quiet・冷たい湧水 spring/cold など）。
    /// </summary>
    public static class FieldMapLayout
    {
        public static WorldLayout Build(FieldMap m)
        {
            var L = new WorldLayout
            {
                WorldSize = Mathf.Max(m.Width, m.Depth),
                MapId = m.Id,
                MapAreaId = m.AreaId,
                HasBounds = true,
                BoundsMin = new Vector2(m.Origin.x - FieldMap.Margin, m.Origin.z - FieldMap.Margin),
                BoundsMax = new Vector2(m.Origin.x + m.Width + FieldMap.Margin, m.Origin.z + m.Depth + FieldMap.Margin)
            };
            L.HeightSampler = (x, z) =>
            {
                var l = m.ToLocal(x, z);
                return m.SampleTerrain(l.x, l.y);
            };
            L.WaterTester = (x, z) =>
            {
                var l = m.ToLocal(x, z);
                return m.WaterAt(l.x, l.y) != FieldWater.None && m.WaterDepth(l.x, l.y) > 0.03f;
            };
            L.WaterDepthSampler = (x, z) =>
            {
                var l = m.ToLocal(x, z);
                return m.WaterDepth(l.x, l.y);
            };
            if (m.Gates.Count > 0) L.PlayerSpawn = m.Gates[0].Arrival;

            BuildWaters(m, L);
            if (m.Id == StarSkeleton.FlowerPath) AreasFlowerPath(m, L);
            else if (m.Id == StarSkeleton.LakeForest) AreasLakeForest(m, L);
            else if (m.Id == StarSkeleton.StreamSlope) AreasStreamSlope(m, L);
            BuildSpots(m, L);
            BuildHabitats(m, L);
            return L;
        }

        private static Vector3 W(FieldMap m, Vector2 local, float y) => m.ToWorld(local, y);
        private static Vector3 G(FieldMap m, Vector2 local) => m.OnGroundWorld(local);

        private static AreaRegion Area(WorldLayout L, string id, AreaType type, Vector3 center, float radius, string ja, string en, bool quiet = false)
        {
            var a = new AreaRegion { id = id, type = type, center = center, radius = radius, nameJa = ja, nameEn = en, quiet = quiet };
            L.Areas.Add(a);
            return a;
        }

        // =================================================================
        // 水域
        // =================================================================

        private static void BuildWaters(FieldMap m, WorldLayout L)
        {
            for (int s = 0; s < m.Streams.Count; s++)
            {
                var st = m.Streams[s];
                var wb = new WaterBody { id = "water." + st.Id, type = WaterType.River, radius = 1.6f };
                float sum = 0f;
                for (int i = 0; i < st.Points.Count; i++)
                {
                    wb.points.Add(W(m, st.Points[i], st.Surface[i]));
                    sum += st.Surface[i];
                }
                wb.surfaceHeight = st.Points.Count > 0 ? sum / st.Points.Count : 0f;
                L.Waters.Add(wb);
            }
            for (int p = 0; p < m.Ponds.Count; p++)
            {
                var pd = m.Ponds[p];
                var wb = new WaterBody { id = "water." + m.Id + ".pond" + p, type = WaterType.Pond, radius = pd.Radius, surfaceHeight = pd.Surface };
                wb.points.Add(W(m, pd.Center, pd.Surface));
                L.Waters.Add(wb);
            }
            if (m.Lake != null)
            {
                var lk = m.Lake;
                var wb = new WaterBody { id = "water." + m.Id + ".lake", type = WaterType.Lake, radius = lk.BaseRadius, surfaceHeight = lk.Surface };
                wb.points.Add(W(m, lk.Center, lk.Surface));
                L.Waters.Add(wb);
            }
        }

        // =================================================================
        // エリア（GetAreaAt は小さいエリアを優先する）
        // =================================================================

        private static void StreamAreas(FieldMap m, WorldLayout L, FieldStream st, string ja, string en)
        {
            var cum = 0f;
            for (int i = 1; i < st.Points.Count; i++)
            {
                cum += Vector2.Distance(st.Points[i - 1], st.Points[i]);
                if (cum < 18f) continue;
                cum = 0f;
                if (!m.InsidePlayable(st.Points[i], -4f)) continue;
                Area(L, $"area.{st.Id}.{i}", AreaType.Riverside, G(m, st.Points[i]), 8f, ja, en);
            }
        }

        private static void AreasFlowerPath(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.t1.meadow", AreaType.Meadow, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "花の野道の草原", "Flower path meadow");
            Area(L, "area.t1.woods", AreaType.Forest, G(m, new Vector2(m.Width * 0.92f, m.Depth * 0.5f)), 36f, "湖の森に近い林", "Woods near the lake forest");
            var fl = m.GetPlace("t1.flowers");
            if (fl != null) Area(L, "area.t1.flowers", AreaType.Meadow, G(m, fl.Local), 24f, "花畑", "Flower field");
            var ter = m.GetPlace("t1.terrace");
            if (ter != null) Area(L, "area.t1.terrace", AreaType.Meadow, G(m, ter.Local), 11f, "果樹の段", "Fruit terrace");
            var hill = m.GetPlace("t1.hilltop");
            if (hill != null) Area(L, "area.t1.hilltop", AreaType.Hill, G(m, hill.Local), 11f, "丘の上", "Hilltop");
            foreach (var st in m.Streams) StreamAreas(m, L, st, "花の野道の小川", "Meadow stream");
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, "area.t1.pond" + p, AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 5f, "野の池", "Meadow pond");
        }

        private static void AreasStreamSlope(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.t3.valley", AreaType.Forest, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "渓流の谷", "Stream valley");
            foreach (var st in m.Streams) StreamAreas(m, L, st, "渓流", "Mountain stream");
            var ruins = m.GetPlace("t3.ruins");
            if (ruins != null) Area(L, "area.t3.ruins", AreaType.Mountain, G(m, ruins.Local), 9f, "石積みの跡", "Old stonework");
            var shelf = m.GetPlace("t3.shelf");
            if (shelf != null) Area(L, "area.t3.shelf", AreaType.Mountain, G(m, shelf.Local), 10f, "滝の上の岩棚", "Rock shelf");
        }

        private static void AreasLakeForest(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.r2.forest", AreaType.Forest, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "湖の森", "Lake forest");
            var lk = m.Lake;
            if (lk != null)
            {
                Area(L, "area.r2.lake", AreaType.Lake, W(m, lk.Center, lk.Surface), lk.BaseRadius * 1.12f + 6f, "湖", "Lake");
                var quiet = m.GetPlace("r2.quiet");
                if (quiet != null) Area(L, "area.r2.quiet", AreaType.Lake, G(m, quiet.Local), 18f, "湖の静かな岸", "Quiet lakeshore", true);
                var cape = m.GetPlace("r2.cape");
                if (cape != null) Area(L, "area.r2.cape", AreaType.Lake, G(m, cape.Local), 9f, "湖の岬", "Lake cape");
            }
            foreach (var st in m.Streams)
                StreamAreas(m, L, st, st.Id.EndsWith("inlet") ? "湖への流れ込み" : "湖からの流れ出し", st.Id.EndsWith("inlet") ? "Lake inlet" : "Lake outlet");
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, "area.r2.pond" + p, AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 5f, "森の池", "Forest pond");
        }

        // =================================================================
        // 釣り場
        // =================================================================

        private static FishingSpot Spot(WorldLayout L, string id, string waterId, WaterType type, Vector3 stand, Vector3 water)
        {
            var s = new FishingSpot { id = id, waterId = waterId, waterType = type, standPosition = stand, waterPosition = water };
            L.FishingSpots.Add(s);
            return s;
        }

        /// <summary>水辺の、水でない立ち位置（水の中心から外へ向かって探す）。</summary>
        private static bool StandNear(FieldMap m, Vector2 waterPoint, Vector2 outward, float from, float to, out Vector2 stand)
        {
            for (float off = from; off <= to; off += 0.5f)
            {
                var q = waterPoint + outward * off;
                if (!m.InsidePlayable(q, 2f)) continue;
                if (m.WaterAt(q.x, q.y) != FieldWater.None && m.WaterDepth(q.x, q.y) > 0.03f) continue;
                stand = q;
                return true;
            }
            stand = Vector2.zero;
            return false;
        }

        private static void BuildSpots(FieldMap m, WorldLayout L)
        {
            // 小川・渓流：流れの勾配で 急流・早瀬・瀬・よどみ を分け、滝つぼと湧水の淵には必ず釣り場を置く（§6.2）
            foreach (var st in m.Streams)
            {
                foreach (var f in st.Falls) StreamSpot(m, L, st, f, "fall" + f, "waterfall", "pool", "deep", "rocky");
                if (st.SpringIndex >= 0) StreamSpot(m, L, st, st.SpringIndex, "spring", "spring", "cold", "pool", "deep");
                int n = 0;
                float acc = 99f;
                for (int i = 1; i < st.Points.Count - 1; i++)
                {
                    acc += Vector2.Distance(st.Points[i - 1], st.Points[i]);
                    if (acc < 16f) continue;
                    var p = st.Points[i];
                    if (!m.InsidePlayable(p, 6f)) continue;
                    if (m.Lake != null && m.Lake.K(p) < 1.1f) continue;
                    var dir = (st.Points[i + 1] - st.Points[i - 1]).normalized;
                    var side = new Vector2(-dir.y, dir.x) * (n % 2 == 0 ? 1f : -1f);
                    if (!StandNear(m, p, side, st.Radius[i] * 0.8f, st.Radius[i] + 4f, out var stand) && !StandNear(m, p, -side, st.Radius[i] * 0.8f, st.Radius[i] + 4f, out stand)) continue;
                    acc = 0f;
                    // 小川は場所で水面の高さが違うので、水域の平均の高さで上書きされないよう水域の ID は付けない（水面は waterPosition.y）
                    var s = Spot(L, $"spot.{st.Id}.{n++}", null, WaterType.River, G(m, stand), W(m, p, st.Surface[i]));
                    s.tags.Add("river");
                    s.tags.Add("stream");
                    s.tags.Add(st.Depth[i] >= 1f ? "deep" : "shallow");
                    float grad = Gradient(st, i);
                    if (st.Fords.Contains(i)) s.tags.Add("riffle");
                    else if (grad >= 0.06f) s.tags.Add("rapids");
                    else if (grad >= 0.04f) s.tags.Add("fast");
                    else if (grad >= 0.02f || n % 3 != 0) s.tags.Add("riffle");
                    else s.tags.Add("slack");
                    if (grad < 0.04f && n % 4 == 1) s.tags.Add("weed");
                    if (m.Slope(stand.x, stand.y) > 24f || m.PaintAt(stand.x, stand.y) == FieldPaint.Rock) s.tags.Add("rocky");
                    if (m.PaintAt(stand.x, stand.y) == FieldPaint.Forest || m.PaintAt(stand.x, stand.y) == FieldPaint.WetForest) s.tags.Add("forest");
                    if (m.Lake != null && st.Id.EndsWith("inlet") && Vector2.Distance(p, m.Lake.Inlet) < 26f) s.tags.Add("turbid");
                }
            }
            // 池：野の池（T1）・森の池（R2：湧水口・泥底・濁り）
            for (int pi = 0; pi < m.Ponds.Count; pi++)
            {
                var pd = m.Ponds[pi];
                for (int k = 0; k < 3; k++)
                {
                    float a = k / 3f * Mathf.PI * 2f + 0.4f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    if (!StandNear(m, pd.Center, dir, pd.Radius - 1f, pd.Radius + 5f, out var stand)) continue;
                    var s = Spot(L, $"spot.{m.Id}.pond{pi}.{k}", $"water.{m.Id}.pond{pi}", WaterType.Pond, G(m, stand), W(m, pd.Center + dir * pd.Radius * 0.35f, pd.Surface));
                    s.tags.Add("pond");
                    s.tags.Add("still");
                    s.tags.Add(k == 0 ? "shallow" : "mid");
                    if (m.Id == StarSkeleton.FlowerPath)
                    {
                        s.tags.Add("meadow");
                        if (k == 1) s.tags.Add("weed");
                    }
                    else
                    {
                        s.tags.Add("forest");
                        if (pi == 0 && k == 0)
                        {
                            s.tags.Add("spring");
                            s.tags.Add("cold");
                        }
                        if (pi == Mathf.Min(1, m.Ponds.Count - 1) && k == 1) s.tags.Add("mud");
                        if (pi == Mathf.Min(1, m.Ponds.Count - 1) && k == 2) s.tags.Add("turbid");
                        // 人の来ない池（住民はまだ村の外へ来ない。本道から離れていれば静か）
                        if (m.DistToPaths(stand, false) > 40f) s.tags.Add("quiet");
                    }
                }
            }
            if (m.Lake != null) LakeSpots(m, L);
        }

        /// <summary>小川の点のまわりの水面の勾配（上流と下流の4点ずつで見る）。</summary>
        private static float Gradient(FieldStream st, int i)
        {
            int a = Mathf.Max(0, i - 4), b = Mathf.Min(st.Points.Count - 1, i + 4);
            float dist = 0f;
            for (int k = a + 1; k <= b; k++) dist += Vector2.Distance(st.Points[k - 1], st.Points[k]);
            return dist < 1f ? 0f : Mathf.Abs(st.Surface[a] - st.Surface[b]) / dist;
        }

        /// <summary>小川の決まった点（滝つぼ・湧水の淵）の釣り場。</summary>
        private static void StreamSpot(FieldMap m, WorldLayout L, FieldStream st, int i, string key, params string[] tags)
        {
            if (i <= 0 || i >= st.Points.Count - 1) return;
            var p = st.Points[i];
            var dir = (st.Points[i + 1] - st.Points[i - 1]).normalized;
            var side = new Vector2(-dir.y, dir.x);
            if (!StandNear(m, p, side, st.Radius[i] * 0.8f, st.Radius[i] + 5f, out var stand) && !StandNear(m, p, -side, st.Radius[i] * 0.8f, st.Radius[i] + 5f, out stand)) return;
            var s = Spot(L, $"spot.{st.Id}.{key}", null, WaterType.River, G(m, stand), W(m, p, st.Surface[i]));
            s.tags.Add("river");
            s.tags.Add("stream");
            foreach (var t in tags) s.tags.Add(t);
        }

        /// <summary>湖：岸のまわり24方向＋岬の先（最深部）と岬の脇（v0.2 の湖の釣り場と同じ作り）。</summary>
        private static void LakeSpots(FieldMap m, WorldLayout L)
        {
            var lk = m.Lake;
            string wid = "water." + m.Id + ".lake";
            int n = 0;
            for (int k = 0; k < 24; k++)
            {
                float a = k / 24f * Mathf.PI * 2f;
                if (LakeInfo.AngleDiff(a, lk.SpitAngle) < 0.2f) continue;
                var dir = lk.Dir(a);
                float rr = lk.RadiusAt(a);
                if (!StandNear(m, lk.Center, dir, rr - 4f, rr + 6f, out var stand)) continue;
                bool shelf = LakeInfo.AngleDiff(a, lk.ShelfAngle) < 0.95f;
                var water = stand - dir * (shelf ? 5f : 8.5f);
                var s = Spot(L, $"spot.{m.Id}.lake.{n++}", wid, WaterType.Lake, G(m, stand), W(m, water, lk.Surface));
                LakeTags(m, s, lk, water, a, stand);
            }
            var sd = lk.Dir(lk.SpitAngle);
            var tipStand = lk.SpitTip + sd * 1f;
            var tipWater = lk.SpitTip - sd * 6f;
            var tip = Spot(L, $"spot.{m.Id}.lake.tip", wid, WaterType.Lake, G(m, tipStand), W(m, tipWater, lk.Surface));
            LakeTags(m, tip, lk, tipWater, lk.SpitAngle, tipStand);
            if (!tip.tags.Contains("deep")) tip.tags.Add("deep");
            tip.tags.Add("deepest");
            var perp = new Vector2(-sd.y, sd.x);
            var midStand = Vector2.Lerp(lk.SpitTip, lk.SpitBase, 0.4f);
            for (int s = -1; s <= 1; s += 2)
            {
                var w = midStand + perp * (s * (LakeInfo.SpitHalfWidth + 5.5f));
                var st = midStand + perp * (s * (LakeInfo.SpitHalfWidth - 0.8f));
                var side = Spot(L, $"spot.{m.Id}.lake.spit{(s < 0 ? "L" : "R")}", wid, WaterType.Lake, G(m, st), W(m, w, lk.Surface));
                LakeTags(m, side, lk, w, lk.SpitAngle, st);
            }
            // 桟橋の先（湖畔の東屋の前）
            foreach (var stc in m.Structures)
            {
                if (stc.Kind != FieldStructureKind.Pier) continue;
                float yawR = stc.Yaw * Mathf.Deg2Rad;
                var fwd = new Vector2(Mathf.Sin(yawR), Mathf.Cos(yawR));
                var end = stc.Local + fwd * 10.5f;
                var ps = Spot(L, $"spot.{m.Id}.lake.pier", wid, WaterType.Lake, W(m, end, stc.Y + 0.1f), W(m, end + fwd * 6f, lk.Surface));
                LakeTags(m, ps, lk, end + fwd * 6f, LakeInfo.Angle(end - lk.Center), end);
                ps.tags.Add("pier");
            }
        }

        private static void LakeTags(FieldMap m, FishingSpot spot, LakeInfo lk, Vector2 water, float angle, Vector2 stand)
        {
            spot.tags.Add("lake");
            spot.tags.Add("wide");
            spot.tags.Add("foggy");
            spot.tags.Add("still");
            float depth = lk.DepthAt(water);
            spot.tags.Add(depth <= 1.5f ? "shallow" : depth <= 5f ? "mid" : "deep");
            if (LakeInfo.AngleDiff(angle, lk.MudAngle) < 0.6f) spot.tags.Add("mud");
            if (LakeInfo.AngleDiff(angle, lk.SandMudAngle) < 0.5f) spot.tags.Add("sandmud");
            bool turbid = lk.HasInlet ? Vector2.Distance(stand, lk.Inlet) < 22f : LakeInfo.AngleDiff(angle, lk.MudAngle) < 0.4f;
            if (turbid) spot.tags.Add("turbid");
            var paint = m.PaintAt(stand.x, stand.y);
            if (paint == FieldPaint.Forest || paint == FieldPaint.WetForest) spot.tags.Add("forest");
            // 静かな岸：本道・東屋から 40m 以上（マップ設計 v0.2 §2.3）
            bool quiet = m.DistToPaths(stand, false) >= WorldTuning.QuietShoreClearance;
            foreach (var s in m.Structures)
                if (s.Kind == FieldStructureKind.Gazebo && Vector2.Distance(s.Local, stand) < WorldTuning.QuietShoreClearance) quiet = false;
            if (quiet) spot.tags.Add("quiet");
        }

        // =================================================================
        // 生息地点（§7。自然物から種類ごとに数を抑えて取り、水辺の泥・水面を足す）
        // =================================================================

        private static void Habitat(WorldLayout L, Vector3 p, string feature)
        {
            var reg = L.GetAreaAt(p);
            L.HabitatPoints.Add(new HabitatPoint { position = p, area = reg != null ? reg.type : AreaType.Meadow, feature = feature });
        }

        private static void BuildHabitats(FieldMap m, WorldLayout L)
        {
            var limit = new Dictionary<NatureKind, int>();
            foreach (var it in m.Nature)
            {
                var l = m.ToLocal(it.Position.x, it.Position.z);
                if (!m.InsidePlayable(l, 2f)) continue;
                string feature;
                int cap;
                switch (it.Kind)
                {
                    case NatureKind.TreeRound:
                    case NatureKind.TreeTall:
                    case NatureKind.TreeMushroom:
                    case NatureKind.TreeLollipop:
                    case NatureKind.TreeCrystal: feature = "tree"; cap = 80; break;
                    case NatureKind.Bush: feature = "bush"; cap = 25; break;
                    case NatureKind.Rock:
                    case NatureKind.Boulder: feature = "rock"; cap = 40; break;
                    case NatureKind.Flower: feature = "flower"; cap = 70; break;
                    case NatureKind.Grass: feature = "grass"; cap = 50; break;
                    case NatureKind.Mushroom:
                    case NatureKind.GlowMushroom: feature = "mushroom"; cap = 30; break;
                    case NatureKind.FallenLog: feature = "fallen_log"; cap = 14; break;
                    case NatureKind.Reed: feature = "reed"; cap = 30; break;
                    case NatureKind.Crystal: feature = "crystal"; cap = 10; break;
                    default: continue;
                }
                limit.TryGetValue(it.Kind, out int have);
                if (have >= cap) continue;
                limit[it.Kind] = have + 1;
                Habitat(L, it.Position, feature);
            }
            foreach (var s in m.Structures)
                if (s.Kind == FieldStructureKind.Crystal) Habitat(L, m.ToWorld(s.Local, s.Y), "crystal");
            // 水辺の泥と水面
            foreach (var st in m.Streams)
                for (int i = 2; i < st.Points.Count; i += 4)
                {
                    if (!m.InsidePlayable(st.Points[i], 3f)) continue;
                    var dir = (st.Points[i] - st.Points[i - 1]).normalized;
                    var nrm = new Vector2(-dir.y, dir.x) * ((i / 4) % 2 == 0 ? 1f : -1f);
                    var p = st.Points[i] + nrm * (st.Radius[i] + 1.3f);
                    if (m.WaterAt(p.x, p.y) == FieldWater.None) Habitat(L, G(m, p), "mud");
                    if (i % 8 == 2) Habitat(L, W(m, st.Points[i], st.Surface[i]), "water");
                }
            foreach (var pd in m.Ponds)
                for (int k = 0; k < 6; k++)
                {
                    float a = k / 6f * Mathf.PI * 2f + 0.3f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var p = pd.Center + dir * (pd.Radius + 1.4f);
                    if (m.WaterAt(p.x, p.y) == FieldWater.None) Habitat(L, G(m, p), "mud");
                    if (k % 2 == 0) Habitat(L, W(m, pd.Center + dir * pd.Radius * 0.5f, pd.Surface), "water");
                }
            if (m.Lake != null)
            {
                var lk = m.Lake;
                for (int k = 0; k < 20; k++)
                {
                    float a = k / 20f * Mathf.PI * 2f;
                    var dir = lk.Dir(a);
                    if (StandNear(m, lk.Center, dir, lk.RadiusAt(a) - 2f, lk.RadiusAt(a) + 5f, out var shore))
                        Habitat(L, G(m, shore), LakeInfo.AngleDiff(a, lk.MudAngle) < 0.6f ? "mud" : k % 3 == 0 ? "reed" : "grass");
                    if (k % 2 == 0) Habitat(L, W(m, lk.Center + dir * lk.RadiusAt(a) * 0.75f, lk.Surface), "water");
                }
            }
        }
    }
}
