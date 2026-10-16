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
            else if (m.Id == StarSkeleton.WaterfallMountain) AreasWaterfallMountain(m, L);
            else if (m.Id == StarSkeleton.MarshRiver) AreasMarsh(m, L);
            else if (m.Id == StarSkeleton.BeachCape) AreasBeach(m, L);
            else if (m.Id == StarSkeleton.MossValley) AreasSimple(m, L, AreaType.Forest, "苔の谷", "Moss valley", "谷の沢", "Valley brook");
            else if (m.Id == StarSkeleton.Ridge) AreasSimple(m, L, AreaType.Hill, "尾根道", "Ridge path", null, null);
            else if (m.Id == StarSkeleton.Highland) AreasHighland(m, L);
            else if (m.Id == StarSkeleton.SeaWindWood) AreasSimple(m, L, AreaType.Coast, "海風の林", "Sea wind wood", null, null);
            else if (m.Id == StarSkeleton.CliffPath) AreasSimple(m, L, AreaType.Coast, "崖の小道", "Cliff path", null, null);
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
            bool sea = false;
            foreach (var w in m.Water) if (w == FieldWater.Sea) { sea = true; break; }
            if (sea)
            {
                var wb = new WaterBody { id = "water." + m.Id + ".sea", type = WaterType.Sea, radius = 9999f, surfaceHeight = 0f };
                wb.points.Add(W(m, new Vector2(m.Width * 0.5f, -FieldMap.Margin), 0f));
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

        private static void AreasSimple(FieldMap m, WorldLayout L, AreaType type, string ja, string en, string streamJa, string streamEn)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area." + m.Id + ".all", type, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, ja, en);
            if (streamJa != null) foreach (var st in m.Streams) StreamAreas(m, L, st, streamJa, streamEn);
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, $"area.{m.Id}.pond{p}", AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 5f, p == 0 ? "湧き水の窪地" : "静かな池", p == 0 ? "Spring hollow" : "Quiet pond", p > 0);
            foreach (var pl in m.Places)
                if (pl.Pocket || pl.Hidden) Area(L, "area." + pl.Id, type == AreaType.Hill ? AreaType.Mountain : type, G(m, pl.Local), 10f, pl.NameJa.Replace("（隠れた場所）", ""), pl.Id);
        }

        private static void AreasHighland(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            // 住民は予定では来ない静かな場所（ユメボシ・ホシノコが出やすい）
            Area(L, "area.r5.highland", AreaType.Hill, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "星見の高原", "Stargazing highland", true);
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, "area.r5.pond" + p, AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 5f, "高原の池", "Highland pond", true);
            var grove = m.GetPlace("r5.grove");
            if (grove != null) Area(L, "area.r5.grove", AreaType.Forest, G(m, grove.Local), 18f, "高原の木立", "Highland grove", true);
            var rock = m.GetPlace("r5.rock");
            if (rock != null) Area(L, "area.r5.rock", AreaType.Hill, G(m, rock.Local), 14f, "星見の岩", "Stargazing rock", true);
        }

        private static void AreasMarsh(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.t2.marsh", AreaType.Riverside, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "川下りの湿原", "Downriver marsh");
            foreach (var st in m.Streams) StreamAreas(m, L, st, "下流の川", "Lower river");
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, "area.t2.pond" + p, AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 6f, "湿原の池", "Marsh pond");
            var bluff = m.GetPlace("t2.bluff");
            if (bluff != null) Area(L, "area.t2.bluff", AreaType.Meadow, G(m, bluff.Local), 26f, "崖の上の草地", "Bluff meadow");
        }

        private static void AreasBeach(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.r4.coast", AreaType.Coast, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "浜と岬", "Beach and cape");
            Area(L, "area.r4.inland", AreaType.Meadow, G(m, new Vector2(m.Width * 0.5f, m.Depth * 0.85f)), m.Width * 0.38f, "浜の後ろの丘", "Dunes and hills");
            foreach (var st in m.Streams) StreamAreas(m, L, st, "河口の川", "Estuary river");
            foreach (var p in m.Places)
            {
                if (p.Id == "r4.cape") Area(L, "area.r4.cape", AreaType.Hill, G(m, p.Local), 24f, "岬", "Cape");
                else if (p.Id == "r4.estuary") Area(L, "area.r4.estuary", AreaType.Riverside, G(m, p.Local), 16f, "河口", "Estuary");
                else if (p.Id == "r4.pools") Area(L, "area.r4.pools", AreaType.Coast, G(m, p.Local), 14f, "岩場の潮だまり", "Rock pools");
            }
        }

        /// <summary>海：岸沿いに 18m おき。砂底・岩場・海底近く・砂泥底（河口のそば）・河口・沖（岬の先と桟橋の先）。</summary>
        private static void SeaSpots(FieldMap m, WorldLayout L)
        {
            string wid = "water." + m.Id + ".sea";
            int n = 0;
            for (float x = 10f; x < m.Width - 10f; x += 18f)
            {
                // 岸の立ち位置：南（海）から北へ、初めて水でなくなる所
                Vector2 stand = Vector2.zero;
                bool ok = false;
                for (float z = 2f; z < m.Depth * 0.7f; z += 1f)
                {
                    if (m.WaterAt(x, z) == FieldWater.Sea && m.WaterDepth(x, z) > 0.05f) continue;
                    stand = new Vector2(x, z);
                    ok = z > 2f;
                    break;
                }
                if (!ok || m.Slope(stand.x, stand.y) > 40f) continue;
                var water = stand + Vector2.down * 9f;
                if (m.WaterAt(water.x, water.y) != FieldWater.Sea) continue;
                var s = Spot(L, $"spot.{m.Id}.sea.{n}", wid, WaterType.Sea, G(m, stand), W(m, water, 0f));
                s.tags.Add("sea");
                var paint = m.PaintAt(stand.x, stand.y + 2f);
                bool rocky = paint == FieldPaint.Rock || m.Slope(stand.x, stand.y + 3f) > 20f;
                s.tags.Add(rocky ? "rocky" : "beach");
                if (n % 2 == 0) s.tags.Add("bottom");
                float depth = m.WaterDepth(water.x, water.y);
                s.tags.Add(depth < 1.2f ? "shallow" : "mid");
                foreach (var st in m.Streams)
                    if (st.Points.Count > 0 && Vector2.Distance(st.Points[st.Points.Count - 1], stand) < 40f)
                    {
                        s.tags.Add("estuary");
                        s.tags.Add("sandmud");
                    }
                n++;
            }
            // 岬の先（沖・岩場）
            var cape = m.GetPlace("r4.cape");
            if (cape != null)
            {
                var tip = cape.Local;
                for (float z = cape.Local.y; z > 2f; z -= 1f)
                {
                    if (m.WaterAt(cape.Local.x, z) == FieldWater.Sea) break;
                    tip = new Vector2(cape.Local.x, z);
                }
                var s = Spot(L, $"spot.{m.Id}.sea.capetip", wid, WaterType.Sea, G(m, tip), W(m, tip + Vector2.down * 16f, 0f));
                s.tags.Add("sea");
                s.tags.Add("offshore");
                s.tags.Add("rocky");
                s.tags.Add("deep");
                s.tags.Add("bottom");
            }
        }

        private static void AreasWaterfallMountain(FieldMap m, WorldLayout L)
        {
            var center = new Vector2(m.Width * 0.5f, m.Depth * 0.5f);
            Area(L, "area.r3.mountain", AreaType.Mountain, G(m, center), new Vector2(m.Width, m.Depth).magnitude * 0.5f + 12f, "滝の山", "Waterfall mountain");
            Area(L, "area.r3.foot", AreaType.Forest, G(m, new Vector2(m.Width * 0.5f, m.Depth * 0.12f)), m.Width * 0.42f, "山のふもとの森", "Foothill forest");
            foreach (var st in m.Streams) StreamAreas(m, L, st, "山の川", "Mountain river");
            foreach (var p in m.Places)
            {
                if (p.Id == "r3.summit") Area(L, "area.r3.summit", AreaType.Hill, G(m, p.Local), 20f, "山頂", "Summit");
                else if (p.Id == "r3.crystal") Area(L, "area.r3.crystal", AreaType.Mountain, G(m, p.Local), 22f, "結晶の岩場", "Crystal rocks");
                else if (p.Id == "r3.falls") Area(L, "area.r3.falls", AreaType.Riverside, G(m, p.Local), 16f, "滝つぼ", "Plunge pool");
                else if (p.Id == "r3.canyon") Area(L, "area.r3.canyon", AreaType.Riverside, G(m, p.Local), 14f, "こだまの渓谷", "Echo canyon");
                else if (p.Id.StartsWith("r3.cave")) Area(L, "area." + p.Id, AreaType.Mountain, G(m, p.Local), 7f, "崖の洞穴", "Cliff cave");
            }
            for (int p = 0; p < m.Ponds.Count; p++)
                Area(L, "area.r3.spring" + p, AreaType.Pond, G(m, m.Ponds[p].Center), m.Ponds[p].Radius + 5f, "山の泉", "Mountain spring");
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
                    else if (m.Id == StarSkeleton.MarshRiver)
                    {
                        // 湿原の池（泥・濁り・水草）
                        s.tags.Add("mud");
                        if (k == 0) s.tags.Add("turbid");
                        if (k == 1) s.tags.Add("weed");
                        s.tags.Add("foggy");
                    }
                    else if (m.Id == StarSkeleton.MossValley)
                    {
                        // 苔の谷：湧き水の窪地（冷たい湧水）と静かな池
                        s.tags.Add("forest");
                        if (pi == 0) { s.tags.Add("spring"); s.tags.Add("cold"); }
                        else { s.tags.Add("quiet"); s.tags.Add("deep"); }
                    }
                    else if (m.Id == StarSkeleton.SeaWindWood)
                    {
                        // 浜の池（砂泥底・浅い）
                        s.tags.Add("sandmud");
                        if (k == 1) s.tags.Add("weed");
                    }
                    else if (m.Id == StarSkeleton.Highland)
                    {
                        // 星見の高原の池（静かな高台の池）
                        s.tags.Add("highland");
                        s.tags.Add("quiet");
                        if (k == 2) s.tags.Add("deep");
                    }
                    else if (m.Id == StarSkeleton.WaterfallMountain)
                    {
                        // 山の泉（冷たい湧水。§8.3 R3 の湧水の淵）
                        s.tags.Add("spring");
                        s.tags.Add("cold");
                        s.tags.Add("rocky");
                        s.tags.Add("quiet");
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
            foreach (var w in m.Water) if (w == FieldWater.Sea) { SeaSpots(m, L); break; }
            if (m.Id == StarSkeleton.CliffPath) CoveSpots(m, L);
            // 海の桟橋（湖の桟橋は LakeSpots の中）
            if (m.Lake == null)
                foreach (var stc in m.Structures)
                {
                    if (stc.Kind != FieldStructureKind.Pier) continue;
                    float yawR = stc.Yaw * Mathf.Deg2Rad;
                    var fwd = new Vector2(Mathf.Sin(yawR), Mathf.Cos(yawR));
                    var end = stc.Local + fwd * (10.5f * Mathf.Max(0.5f, stc.Size));
                    var ps = Spot(L, $"spot.{m.Id}.sea.pier", "water." + m.Id + ".sea", WaterType.Sea, W(m, end, stc.Y + 0.1f), W(m, end + fwd * 8f, 0f));
                    ps.tags.Add("sea");
                    ps.tags.Add("pier");
                    ps.tags.Add("offshore");
                    ps.tags.Add("bottom");
                    ps.tags.Add("deep");
                }
        }

        /// <summary>崖の小道の入り江：崖の根もとは立てないので、入り江の浜・岩棚・岩場から海へ投げる釣り場。</summary>
        private static void CoveSpots(FieldMap m, WorldLayout L)
        {
            string wid = "water." + m.Id + ".sea";
            string[] ids = { "t6b.cove", "t6b.ledge", "t6b.rocks" };
            foreach (var pid in ids)
            {
                var pl = m.GetPlace(pid);
                if (pl == null) continue;
                Vector2 water = pl.Local;
                bool found = false;
                for (float z = pl.Local.y - 4f; z > 2f; z -= 1f)
                    if (m.WaterAt(pl.Local.x, z) == FieldWater.Sea && m.WaterDepth(pl.Local.x, z) > 0.8f) { water = new Vector2(pl.Local.x, z - 2f); found = true; break; }
                if (!found) continue;
                var s = Spot(L, $"spot.{m.Id}.{pid}", wid, WaterType.Sea, G(m, pl.Local), W(m, water, 0f));
                s.tags.Add("sea");
                s.tags.Add(pid == "t6b.cove" ? "beach" : "rocky");
                s.tags.Add("bottom");
                s.tags.Add(m.WaterDepth(water.x, water.y) < 1.2f ? "shallow" : "mid");
                if (pid == "t6b.ledge") s.tags.Add("deep");
            }
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
                var end = stc.Local + fwd * (10.5f * Mathf.Max(0.5f, stc.Size));
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
