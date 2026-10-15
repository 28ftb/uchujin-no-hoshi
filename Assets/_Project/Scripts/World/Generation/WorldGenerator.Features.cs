using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // 自然物・釣り場・エリア・生息地点・検証（村仕様 §7、§15 手順9・11、§16）
    public sealed partial class WorldGenerator
    {
        // =================================================================
        // 自然物（村仕様 §7：自然を採集用の背景にしない。生息地点の元にもなる）
        // =================================================================

        private readonly Dictionary<long, List<Vector2>> spacing = new Dictionary<long, List<Vector2>>();

        private static long CellKey(Vector2 p)
        {
            long x = Mathf.FloorToInt(p.x / 6f) + 1000;
            long z = Mathf.FloorToInt(p.y / 6f) + 1000;
            return x * 10000 + z;
        }

        private bool SpacingOk(Vector2 p, float minDist)
        {
            long cx = Mathf.FloorToInt(p.x / 6f) + 1000;
            long cz = Mathf.FloorToInt(p.y / 6f) + 1000;
            int reach = Mathf.CeilToInt(minDist / 6f);
            for (long dx = -reach; dx <= reach; dx++)
                for (long dz = -reach; dz <= reach; dz++)
                {
                    if (!spacing.TryGetValue((cx + dx) * 10000 + (cz + dz), out var list)) continue;
                    foreach (var q in list)
                        if ((q - p).sqrMagnitude < minDist * minDist) return false;
                }
            return true;
        }

        private void MarkSpacing(Vector2 p)
        {
            long k = CellKey(p);
            if (!spacing.TryGetValue(k, out var list))
            {
                list = new List<Vector2>();
                spacing[k] = list;
            }
            list.Add(p);
        }

        /// <summary>自然物を置ける場所か（道路・区画・水・施設を避ける）。</summary>
        private bool NatureClear(Vector2 p, float radius, bool allowNearWater = false)
        {
            if (!GeoUtil.InsideWorld(p, 3f)) return false;
            if (!allowNearWater && F.WaterNear(p.x, p.y, radius + 0.8f)) return false;
            if (allowNearWater && F.IsWater(p.x, p.y)) return false;
            foreach (var seg in W.RoadSegments)
            {
                var mn = Vector2.Min(seg.A, seg.B);
                var mx = Vector2.Max(seg.A, seg.B);
                float m = seg.HalfWidth + radius + 1f;
                if (p.x < mn.x - m || p.x > mx.x + m || p.y < mn.y - m || p.y > mx.y + m) continue;
                if (GeoUtil.DistToSegment(p, seg.A, seg.B, out _) < m) return false;
            }
            foreach (var pad in W.Pads)
            {
                if (pad.Kind == PadKind.Generic && pad.FacilityId == null) continue;
                if (pad.SignedDistance(p.x, p.y) < radius + 0.5f) return false;
            }
            foreach (var fp in footprints)
                if (fp.SignedDistance(p.x, p.y) < radius + 0.8f) return false;
            foreach (var rs in reserved)
                if ((rs.c - p).sqrMagnitude < (rs.r + radius) * (rs.r + radius)) return false;
            if (Vector2.Distance(p, plaza) < 40f) return false;
            return true;
        }

        private void AddNature(NatureKind kind, Vector2 p, float yaw, float scale, int variant)
        {
            W.Nature.Add(new NatureItem
            {
                Kind = kind,
                Position = new Vector3(p.x, F.SampleTerrain(p.x, p.y), p.y),
                Yaw = yaw,
                Scale = scale,
                Variant = variant
            });
        }

        private void PlaceNature()
        {
            var r = R("nature");
            float peakLine = S.MountainPeak * 0.62f;

            // 木
            for (int k = 0; k < WorldTuning.TreeAttempts; k++)
            {
                var p = new Vector2(r.Range(-197f, 197f), r.Range(-197f, 197f));
                float forest = S.Forest01(p.x, p.y);
                bool isForest = forest > S.ForestThreshold;
                float dm = S.MountainFactor(p.x, p.y);
                float d = S.CoastD(p.x, p.y);
                float h = F.SampleTerrain(p.x, p.y);
                float chance = isForest ? 0.9f : 0.05f;
                if (dm < 1f && h < peakLine + 6f) chance = Mathf.Max(chance, 0.32f);
                bool beachTree = d > 6f && d < 24f && r.Chance(0.12f);
                if (d < 16f && !beachTree) continue;
                if (!r.Chance(beachTree ? 0.6f : chance)) continue;
                if (F.Slope(p.x, p.y) > 36f) continue;
                float spacingDist = isForest ? 3.3f : 5.5f;
                if (!SpacingOk(p, spacingDist)) continue;
                if (!NatureClear(p, 1.2f)) continue;
                NatureKind kind;
                float roll = r.NextFloat();
                if (beachTree) kind = NatureKind.TreeLollipop;
                else if (dm < 1f) kind = h > peakLine - 4f && roll < 0.3f ? NatureKind.TreeCrystal : roll < 0.65f ? NatureKind.TreeTall : NatureKind.TreeRound;
                else if (isForest) kind = roll < 0.42f ? NatureKind.TreeRound : roll < 0.67f ? NatureKind.TreeTall : roll < 0.84f ? NatureKind.TreeMushroom : NatureKind.TreeLollipop;
                else kind = roll < 0.5f ? NatureKind.TreeRound : roll < 0.78f ? NatureKind.TreeLollipop : NatureKind.TreeMushroom;
                AddNature(kind, p, r.Range(0f, 360f), r.Range(0.8f, 1.35f), r.Range(0, 5));
                MarkSpacing(p);
            }

            // 茂み（森の縁）
            for (int k = 0; k < 420; k++)
            {
                var p = new Vector2(r.Range(-195f, 195f), r.Range(-195f, 195f));
                float forest = S.Forest01(p.x, p.y);
                if (Mathf.Abs(forest - S.ForestThreshold) > 0.09f) continue;
                if (S.CoastD(p.x, p.y) < 18f) continue;
                if (!SpacingOk(p, 2.4f) || !NatureClear(p, 0.9f)) continue;
                AddNature(NatureKind.Bush, p, r.Range(0f, 360f), r.Range(0.7f, 1.3f), r.Range(0, 5));
                MarkSpacing(p);
            }

            // 岩：山・岩場の海岸・草原
            for (int k = 0; k < WorldTuning.RockAttempts; k++)
            {
                Vector2 p;
                NatureKind kind;
                int mode = k % 4;
                if (mode == 0 || mode == 1)
                {
                    var off = r.InsideUnitCircle() * S.MountainRadius * 1.1f;
                    p = S.MountainCenter + off;
                    kind = r.Chance(0.35f) ? NatureKind.Boulder : NatureKind.Rock;
                }
                else if (mode == 2)
                {
                    float t = r.Range(-190f, 190f);
                    p = S.CoastPoint(t, r.Range(-1f, 9f));
                    if (S.Rocky01(p.x, p.y) < 0.55f) continue;
                    kind = r.Chance(0.6f) ? NatureKind.Boulder : NatureKind.Rock;
                }
                else
                {
                    p = new Vector2(r.Range(-190f, 190f), r.Range(-190f, 190f));
                    kind = NatureKind.Rock;
                    if (!r.Chance(0.5f)) continue;
                }
                if (!GeoUtil.InsideWorld(p, 4f)) continue;
                if (!SpacingOk(p, kind == NatureKind.Boulder ? 4f : 2.5f)) continue;
                if (!NatureClear(p, kind == NatureKind.Boulder ? 1.6f : 0.8f, mode == 2)) continue;
                if (F.IsWater(p.x, p.y)) continue;
                AddNature(kind, p, r.Range(0f, 360f), r.Range(0.7f, 1.5f), r.Range(0, 5));
                MarkSpacing(p);
            }

            // 花（花畑：ハナピカなど花へ集まる生物）
            for (int k = 0; k < WorldTuning.FlowerAttempts; k++)
            {
                var p = new Vector2(r.Range(-195f, 195f), r.Range(-195f, 195f));
                if (S.Flower01(p.x, p.y) < S.FlowerThreshold) continue;
                if (S.IsForest(p.x, p.y) && !r.Chance(0.2f)) continue;
                if (S.CoastD(p.x, p.y) < 16f || S.MountainFactor(p.x, p.y) < 0.85f) continue;
                if (!SpacingOk(p, 1.1f) || !NatureClear(p, 0.3f)) continue;
                AddNature(NatureKind.Flower, p, r.Range(0f, 360f), r.Range(0.8f, 1.3f), r.Range(0, P.Flowers.Length));
                MarkSpacing(p);
            }

            // 草むら
            for (int k = 0; k < WorldTuning.GrassAttempts; k++)
            {
                var p = new Vector2(r.Range(-196f, 196f), r.Range(-196f, 196f));
                if (S.CoastD(p.x, p.y) < 15f) continue;
                if (S.MountainFactor(p.x, p.y) < 0.75f) continue;
                if (!SpacingOk(p, 1.4f) || !NatureClear(p, 0.3f)) continue;
                AddNature(NatureKind.Grass, p, r.Range(0f, 360f), r.Range(0.7f, 1.4f), r.Range(0, 3));
                MarkSpacing(p);
            }

            // キノコ・光るキノコ（夜の暗がり：ヒカリムシ等）
            for (int k = 0; k < 360; k++)
            {
                var p = new Vector2(r.Range(-195f, 195f), r.Range(-195f, 195f));
                if (!S.IsForest(p.x, p.y)) continue;
                if (!SpacingOk(p, 1.6f) || !NatureClear(p, 0.4f)) continue;
                AddNature(r.Chance(0.22f) ? NatureKind.GlowMushroom : NatureKind.Mushroom, p, r.Range(0f, 360f), r.Range(0.7f, 1.4f), r.Range(0, 3));
                MarkSpacing(p);
            }

            // 倒木
            int logs = 0;
            for (int k = 0; k < 200 && logs < 14; k++)
            {
                var p = new Vector2(r.Range(-190f, 190f), r.Range(-190f, 190f));
                if (!S.IsForest(p.x, p.y)) continue;
                if (!SpacingOk(p, 4f) || !NatureClear(p, 2.2f)) continue;
                AddNature(NatureKind.FallenLog, p, r.Range(0f, 360f), r.Range(0.85f, 1.2f), 0);
                MarkSpacing(p);
                logs++;
            }

            // 水辺の葦
            for (int k = 0; k < 260; k++)
            {
                Vector2 p;
                if (W.Ponds.Count > 0 && (k % 3 == 0))
                {
                    var pd = W.Ponds[r.Range(0, W.Ponds.Count)];
                    float a = r.Range(0f, Mathf.PI * 2f);
                    p = pd.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (pd.Radius + r.Range(0.8f, 2.4f));
                }
                else if (W.Rivers.Count > 0)
                {
                    var rv = W.Rivers[r.Range(0, W.Rivers.Count)];
                    int i = r.Range(1, rv.Points.Count);
                    var dir = (rv.Points[i] - rv.Points[i - 1]).normalized;
                    var n = new Vector2(-dir.y, dir.x) * (r.Chance(0.5f) ? 1f : -1f);
                    p = rv.Points[i] + n * (rv.Radius[i] + r.Range(0.8f, 2.6f));
                }
                else break;
                if (!SpacingOk(p, 1.2f) || !NatureClear(p, 0.3f, true)) continue;
                AddNature(NatureKind.Reed, p, r.Range(0f, 360f), r.Range(0.8f, 1.3f), r.Range(0, 3));
                MarkSpacing(p);
            }

            // 浜の貝
            for (int k = 0; k < 120; k++)
            {
                float t = r.Range(-190f, 190f);
                var p = S.CoastPoint(t, r.Range(0.5f, 9f));
                if (!GeoUtil.InsideWorld(p, 3f) || F.IsWater(p.x, p.y)) continue;
                if (!SpacingOk(p, 2f) || !NatureClear(p, 0.3f, true)) continue;
                AddNature(NatureKind.Shell, p, r.Range(0f, 360f), r.Range(0.8f, 1.3f), r.Range(0, 3));
                MarkSpacing(p);
            }

            // 鉱山の近くの結晶（鉱物系の生物・結晶霞）
            for (int k = 0; k < 120; k++)
            {
                var p = mineMouth + r.InsideUnitCircle() * 32f;
                if (S.MountainFactor(p.x, p.y) > 1.15f) continue;
                if (!SpacingOk(p, 2.4f) || !NatureClear(p, 0.6f)) continue;
                AddNature(NatureKind.Crystal, p, r.Range(0f, 360f), r.Range(0.6f, 1.3f), r.Range(0, 3));
                MarkSpacing(p);
            }
        }

        // =================================================================
        // 釣り場（釣り仕様 §6：村の川・森の池・海岸・桟橋・滝など）
        // =================================================================

        private void BuildFishingSpots()
        {
            var bridges = F.Bridges;
            for (int ri = 0; ri < W.Rivers.Count; ri++)
            {
                var rv = W.Rivers[ri];
                float acc = 4f;
                int n = 0;
                for (int i = 1; i < rv.Points.Count; i++)
                {
                    float segLen = (rv.Points[i] - rv.Points[i - 1]).magnitude;
                    acc += segLen;
                    if (acc < 13f && i != rv.WaterfallIndex) continue;
                    acc = 0f;
                    var dir = (rv.Points[i] - rv.Points[i - 1]).normalized;
                    var nrm = new Vector2(-dir.y, dir.x) * ((n % 2 == 0) ? 1f : -1f);
                    var p = rv.Points[i];
                    float rr = rv.Radius[i];
                    Vector2 stand = Vector2.zero;
                    bool ok = false;
                    for (int s = 0; s < 2 && !ok; s++)
                    {
                        var side = s == 0 ? nrm : -nrm;
                        for (float off = rr + 1.6f; off < rr + 5f; off += 0.8f)
                        {
                            var q = p + side * off;
                            if (!GeoUtil.InsideWorld(q, 2f) || F.IsWater(q.x, q.y)) continue;
                            stand = q;
                            nrm = side;
                            ok = true;
                            break;
                        }
                    }
                    if (!ok) continue;
                    float u = i / (float)(rv.Points.Count - 1);
                    var spot = NewSpot("spot.river" + ri + "." + n, rv.WaterIndex, WaterType.River, stand, p + nrm * rr * 0.2f, rv.Surface[i]);
                    spot.tags.Add("river");
                    if (u < 0.3f) spot.tags.Add("upstream");
                    else if (u > 0.75f) spot.tags.Add("downstream");
                    spot.tags.Add(rr > 3.8f ? "deep" : "shallow");
                    if (Vector2.Distance(p, plaza) < 70f) spot.tags.Add("village");
                    if (ri > 0) spot.tags.Add("stream");
                    foreach (var br in bridges)
                        if (Vector2.Distance(GeoUtil.XZ(br.Mid), p) < 12f && !spot.tags.Contains("bridge")) spot.tags.Add("bridge");
                    if (rv.WaterfallIndex > 0 && Mathf.Abs(i - rv.WaterfallIndex) <= 1)
                    {
                        spot.tags.Add("waterfall");
                        if (!spot.tags.Contains("deep")) spot.tags.Add("deep");
                    }
                    if (rv.EndsInSea && u > 0.88f) spot.tags.Add("estuary");
                    if (S.IsForest(stand.x, stand.y)) spot.tags.Add("forest");
                    if (F.Roughness(stand.x, stand.y, 3f) > 1.8f) spot.tags.Add("rocky");
                    n++;
                }
            }

            for (int pi = 0; pi < W.Ponds.Count; pi++)
            {
                var pd = W.Ponds[pi];
                int count = pd.Radius > 9f ? 4 : 3;
                float a0 = pi * 0.7f;
                for (int k = 0; k < count; k++)
                {
                    float a = a0 + k / (float)count * Mathf.PI * 2f;
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    Vector2 stand = pd.Center + dir * (pd.Radius + 1.8f);
                    for (float off = pd.Radius + 1.2f; off < pd.Radius + 5f; off += 0.6f)
                    {
                        var q = pd.Center + dir * off;
                        if (!F.IsWater(q.x, q.y))
                        {
                            stand = q;
                            break;
                        }
                    }
                    var spot = NewSpot("spot.pond" + pi + "." + k, pd.WaterIndex, WaterType.Pond, stand, pd.Center + dir * pd.Radius * 0.35f, pd.Surface);
                    spot.tags.Add("pond");
                    spot.tags.Add("still");
                    if (k == 0) spot.tags.Add("deep");
                    else spot.tags.Add("shallow");
                    if (S.IsForest(stand.x, stand.y) || S.Forest01(pd.Center.x, pd.Center.y) > S.ForestThreshold - 0.05f) spot.tags.Add("forest");
                    if (Vector2.Distance(pd.Center, plaza) < 80f) spot.tags.Add("village");
                }
            }

            // 海岸
            int sn = 0;
            Vector2? mouth = null;
            foreach (var rv in W.Rivers) if (rv.EndsInSea) mouth = rv.Points[rv.Points.Count - 1];
            for (float t = -180f; t <= 180f; t += 21f)
            {
                var stand = S.CoastPoint(t, 4.5f);
                var water = S.CoastPoint(t, -7f);
                if (!GeoUtil.InsideWorld(stand, 4f) || !GeoUtil.InsideWorld(water, 2f)) continue;
                if (F.IsWater(stand.x, stand.y) || !F.IsWater(water.x, water.y)) continue;
                if (F.KindAt(water.x, water.y) != WaterKind.Sea) continue;
                var spot = NewSpot("spot.sea." + sn++, seaWaterIndex, WaterType.Sea, stand, water, WorldTuning.SeaLevel);
                spot.tags.Add("sea");
                spot.tags.Add(S.Rocky01(stand.x, stand.y) > 0.58f ? "rocky" : "beach");
                if (mouth.HasValue && Vector2.Distance(mouth.Value, stand) < 30f) spot.tags.Add("estuary");
            }
            if (W.HasPier)
            {
                var s = GeoUtil.XZ(W.PierStart);
                var e = GeoUtil.XZ(W.PierEnd);
                var dir = (e - s).normalized;
                var spot = NewSpot("spot.pier.0", seaWaterIndex, WaterType.Sea, e - dir * 1.2f, e + dir * 4f, WorldTuning.SeaLevel);
                spot.standPosition = new Vector3(spot.standPosition.x, W.PierStart.y + 0.35f, spot.standPosition.z);
                spot.tags.Add("sea");
                spot.tags.Add("pier");
                spot.tags.Add("deep");
                var side = new Vector2(-dir.y, dir.x);
                var spot2 = NewSpot("spot.pier.1", seaWaterIndex, WaterType.Sea, Vector2.Lerp(s, e, 0.6f) + side * 0.9f, Vector2.Lerp(s, e, 0.6f) + side * 5f, WorldTuning.SeaLevel);
                spot2.standPosition = new Vector3(spot2.standPosition.x, W.PierStart.y + 0.35f, spot2.standPosition.z);
                spot2.tags.Add("sea");
                spot2.tags.Add("pier");
            }
        }

        private FishingSpot NewSpot(string id, int waterIndex, WaterType type, Vector2 stand, Vector2 water, float surface)
        {
            var spot = new FishingSpot
            {
                id = id,
                waterId = waterIndex >= 0 && waterIndex < L.Waters.Count ? L.Waters[waterIndex].id : null,
                waterType = type,
                standPosition = new Vector3(stand.x, F.SampleGround(stand.x, stand.y), stand.y),
                waterPosition = new Vector3(water.x, surface, water.y)
            };
            L.FishingSpots.Add(spot);
            return spot;
        }

        // =================================================================
        // エリア（村仕様 §2.2 の7分類＋細分。表示名は主人公の呼び名）
        // =================================================================

        private void AddArea(string id, AreaType type, Vector2 c, float radius, string ja, string en)
        {
            L.Areas.Add(new AreaRegion
            {
                id = id,
                type = type,
                center = new Vector3(c.x, F.SampleGround(c.x, c.y), c.y),
                radius = radius,
                nameJa = ja,
                nameEn = en
            });
        }

        private void Dir(Vector2 p, out string ja, out string en)
        {
            GeoUtil.DirectionName(GeoUtil.X0Z(plaza), GeoUtil.X0Z(p), out ja, out en);
        }

        private void BuildAreas()
        {
            AddArea("area.village", AreaType.VillageCenter, plaza, 44f, "村の広場", "Village plaza");
            var home = L.GetFacility(FacilityType.PlayerHouse);
            if (home != null) AddArea("area.home", AreaType.PlayerHome, GeoUtil.XZ(home.position), 11f, "自宅", "Home");
            AddArea("area.farm", AreaType.Farm, farmCenter, 17f, "自分の農場", "My farm");
            AddArea("area.crash", AreaType.CrashSite, crashCenter, 24f, "不時着した場所", "Crash site");
            AddArea("area.mine", AreaType.MineEntrance, mineMouth, 14f, "鉱山の入口", "Mine entrance");
            AddArea("area.mountain", AreaType.Mountain, ClampInside(S.MountainCenter, 30f), S.MountainRadius * 0.95f, "山", "Mountain");
            AddArea("area.hill", AreaType.Hill, lookoutCenter, S.HillRadius, "見晴らしの丘", "Lookout hill");

            // 住宅地（区画のまとまりごと）
            var used = new bool[L.HousePlots.Count];
            int rn = 0;
            for (int i = 0; i < L.HousePlots.Count; i++)
            {
                if (used[i]) continue;
                var c = GeoUtil.XZ(L.HousePlots[i].position);
                var members = new List<Vector2> { c };
                used[i] = true;
                for (int j = i + 1; j < L.HousePlots.Count; j++)
                {
                    if (used[j]) continue;
                    var q = GeoUtil.XZ(L.HousePlots[j].position);
                    if (Vector2.Distance(q, c) < 40f)
                    {
                        used[j] = true;
                        members.Add(q);
                    }
                }
                var center = Vector2.zero;
                foreach (var m in members) center += m;
                center /= members.Count;
                float rad = 14f;
                foreach (var m in members) rad = Mathf.Max(rad, Vector2.Distance(m, center) + 10f);
                Dir(center, out var ja, out var en);
                AddArea("area.residential." + rn++, AreaType.Residential, center, rad, ja + "の住宅地", en + " houses");
            }

            // 森（森の濃い所を代表点に）
            var r = R("areas");
            var forestCenters = new List<Vector2>();
            for (int k = 0; k < 800 && forestCenters.Count < 5; k++)
            {
                var p = new Vector2(r.Range(-180f, 180f), r.Range(-180f, 180f));
                if (S.Forest01(p.x, p.y) < S.ForestThreshold + 0.06f) continue;
                if (F.IsWater(p.x, p.y)) continue;
                bool far = true;
                foreach (var q in forestCenters) if (Vector2.Distance(p, q) < 70f) far = false;
                if (!far) continue;
                forestCenters.Add(p);
            }
            for (int i = 0; i < forestCenters.Count; i++)
            {
                var c = forestCenters[i];
                float rad = 22f;
                for (float t = 22f; t < 60f; t += 4f)
                {
                    int inside = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k / 8f * Mathf.PI * 2f;
                        if (S.IsForest(c.x + Mathf.Cos(a) * t, c.y + Mathf.Sin(a) * t)) inside++;
                    }
                    if (inside < 5) break;
                    rad = t;
                }
                Dir(c, out var ja, out var en);
                AddArea("area.forest." + i, AreaType.Forest, c, rad, ja + "の森", en + " woods");
            }
            if (hasForestTrail) AddArea("area.forest.trail", AreaType.Forest, forestTrailEnd, 16f, "森の小道", "Forest trail");

            // 草原
            int mn = 0;
            for (int k = 0; k < 600 && mn < 3; k++)
            {
                var p = new Vector2(r.Range(-175f, 175f), r.Range(-175f, 175f));
                if (S.IsForest(p.x, p.y) || F.WaterNear(p.x, p.y, 10f)) continue;
                if (S.CoastD(p.x, p.y) < 30f || S.MountainFactor(p.x, p.y) < 1.1f) continue;
                if (Vector2.Distance(p, plaza) < 55f) continue;
                if (S.Flower01(p.x, p.y) < S.FlowerThreshold - 0.05f) continue;
                bool far = true;
                foreach (var a in L.Areas) if (a.type == AreaType.Meadow && Vector2.Distance(GeoUtil.XZ(a.center), p) < 60f) far = false;
                if (!far) continue;
                Dir(p, out var ja, out var en);
                AddArea("area.meadow." + mn, AreaType.Meadow, p, 26f, ja + "の花の草原", en + " flower meadow");
                mn++;
            }

            // 川辺
            for (int ri = 0; ri < W.Rivers.Count; ri++)
            {
                var rv = W.Rivers[ri];
                float acc = 25f;
                int n = 0;
                for (int i = 1; i < rv.Points.Count; i++)
                {
                    acc += (rv.Points[i] - rv.Points[i - 1]).magnitude;
                    if (acc < 45f) continue;
                    acc = 0f;
                    var p = rv.Points[i];
                    if (!GeoUtil.InsideWorld(p, 2f)) continue;
                    float u = i / (float)(rv.Points.Count - 1);
                    string ja, en;
                    if (ri > 0) { ja = "小川のほとり"; en = "Brookside"; }
                    else if (u < 0.33f) { ja = "川の上流"; en = "Upper river"; }
                    else if (u < 0.7f) { ja = "川辺"; en = "Riverside"; }
                    else { ja = "川の下流"; en = "Lower river"; }
                    AddArea("area.river" + ri + "." + n++, AreaType.Riverside, p, 16f + rv.Radius[i], ja, en);
                }
            }
            for (int pi = 0; pi < W.Ponds.Count; pi++)
            {
                var pd = W.Ponds[pi];
                Dir(pd.Center, out var ja, out var en);
                AddArea("area.pond." + pi, AreaType.Pond, pd.Center, pd.Radius + 9f, ja + "の池", en + " pond");
            }
            // 海岸
            int cn = 0;
            for (float t = -170f; t <= 170f; t += 55f)
            {
                var p = S.CoastPoint(t, 2f);
                if (!GeoUtil.InsideWorld(p, 10f)) continue;
                Dir(p, out var ja, out var en);
                bool rocky = S.Rocky01(p.x, p.y) > 0.58f;
                AddArea("area.coast." + cn++, AreaType.Coast, p, 32f, ja + (rocky ? "の岩場" : "の浜辺"), en + (rocky ? " rocky shore" : " beach"));
            }
            if (W.HasPier) AddArea("area.pier", AreaType.Coast, GeoUtil.XZ(W.PierStart), 14f, "桟橋", "Pier");
        }

        // =================================================================
        // 生息地点（生き物仕様 §4・§7.3：生息場所・物や住み場所）
        // =================================================================

        private AreaType AreaFor(Vector3 p)
        {
            var a = L.GetAreaAt(p);
            if (a != null) return a.type;
            if (S.MountainFactor(p.x, p.z) < 1f) return AreaType.Mountain;
            if (S.CoastD(p.x, p.z) < 18f) return AreaType.Coast;
            if (S.IsForest(p.x, p.z)) return AreaType.Forest;
            return AreaType.Meadow;
        }

        private void AddHabitat(Vector3 p, string feature)
        {
            L.HabitatPoints.Add(new HabitatPoint { position = p, area = AreaFor(p), feature = feature });
        }

        private void BuildHabitats()
        {
            // 自然物から（種類ごとに数を抑える）
            var caps = new Dictionary<NatureKind, int>();
            int[] limit = new int[32];
            for (int i = 0; i < W.Nature.Count; i++)
            {
                var it = W.Nature[i];
                it.Area = AreaFor(it.Position);
                W.Nature[i] = it;
                string feature;
                int cap;
                switch (it.Kind)
                {
                    case NatureKind.TreeRound:
                    case NatureKind.TreeTall:
                    case NatureKind.TreeMushroom:
                    case NatureKind.TreeLollipop:
                    case NatureKind.TreeCrystal: feature = "tree"; cap = 140; break;
                    case NatureKind.Bush: feature = "bush"; cap = 40; break;
                    case NatureKind.Rock:
                    case NatureKind.Boulder: feature = "rock"; cap = 70; break;
                    case NatureKind.Flower: feature = "flower"; cap = 90; break;
                    case NatureKind.Grass: feature = "grass"; cap = 80; break;
                    case NatureKind.Mushroom:
                    case NatureKind.GlowMushroom: feature = "mushroom"; cap = 35; break;
                    case NatureKind.FallenLog: feature = "fallen_log"; cap = 20; break;
                    case NatureKind.Reed: feature = "reed"; cap = 40; break;
                    case NatureKind.Shell: feature = "shell"; cap = 25; break;
                    case NatureKind.Crystal: feature = "crystal"; cap = 25; break;
                    default: continue;
                }
                int k = (int)it.Kind;
                if (limit[k] >= cap) continue;
                limit[k]++;
                AddHabitat(it.Position, feature);
            }
            // 水辺の泥
            foreach (var rv in W.Rivers)
                for (int i = 2; i < rv.Points.Count; i += 3)
                {
                    var dir = (rv.Points[i] - rv.Points[i - 1]).normalized;
                    var n = new Vector2(-dir.y, dir.x) * ((i / 3) % 2 == 0 ? 1f : -1f);
                    var p = rv.Points[i] + n * (rv.Radius[i] + 1.3f);
                    if (F.IsWater(p.x, p.y)) continue;
                    AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "mud");
                    if (i % 6 == 2) AddHabitat(new Vector3(rv.Points[i].x, rv.Surface[i], rv.Points[i].y), "water");
                }
            foreach (var pd in W.Ponds)
                for (int k = 0; k < 6; k++)
                {
                    float a = k / 6f * Mathf.PI * 2f + 0.3f;
                    var p = pd.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (pd.Radius + 1.4f);
                    if (F.IsWater(p.x, p.y)) continue;
                    AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "mud");
                    if (k % 2 == 0) AddHabitat(new Vector3(pd.Center.x + Mathf.Cos(a) * pd.Radius * 0.5f, pd.Surface, pd.Center.y + Mathf.Sin(a) * pd.Radius * 0.5f), "water");
                }
            // 砂浜
            for (float t = -180f; t <= 180f; t += 24f)
            {
                var p = S.CoastPoint(t, 6f);
                if (!GeoUtil.InsideWorld(p, 3f) || F.IsWater(p.x, p.y)) continue;
                AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "sand");
            }
            // 建物・村の物
            foreach (var f in L.Facilities)
            {
                switch (f.type)
                {
                    case FacilityType.ResidentHouse:
                    case FacilityType.PlayerHouse:
                        AddHabitat(f.position + Vector3.up * (f.size.y + 1.2f), "roof");
                        AddHabitat(f.position + GeoUtil.Forward(f.rotationY) * (f.size.z * 0.5f + 0.2f) + GeoUtil.Right(f.rotationY) * (f.size.x * 0.3f) + Vector3.up * 1.6f, "window");
                        AddHabitat(f.position + GeoUtil.Right(f.rotationY) * (f.size.x * 0.5f + 1.2f), "garden");
                        break;
                    case FacilityType.Streetlight:
                        AddHabitat(f.position + Vector3.up * 3.3f, "streetlight");
                        break;
                    case FacilityType.Bench:
                        AddHabitat(f.position, "bench");
                        break;
                    case FacilityType.Well:
                        AddHabitat(f.position, "well");
                        break;
                    case FacilityType.GeneralStore:
                    case FacilityType.SeedShop:
                    case FacilityType.CarpenterWorkshop:
                    case FacilityType.RepairWorkshop:
                    case FacilityType.FishingHut:
                    case FacilityType.PlayerShop:
                        AddHabitat(f.position + GeoUtil.Forward(f.rotationY) * (f.size.z * 0.5f + 0.8f) - GeoUtil.Right(f.rotationY) * (f.size.x * 0.5f - 0.6f), "box");
                        AddHabitat(f.position + Vector3.up * (f.size.y + 1f), "roof");
                        break;
                    case FacilityType.RocketSite:
                        AddHabitat(f.position + new Vector3(7f, 0f, -5f), "box");
                        AddHabitat(f.position + new Vector3(-6f, 0f, 6f), "debris");
                        break;
                    case FacilityType.MineEntrance:
                        AddHabitat(f.entrance, "cave");
                        break;
                    case FacilityType.Lookout:
                        AddHabitat(f.position, "lookout");
                        break;
                }
            }
            // 農場
            var fr = L.FarmRect;
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(Mathf.Lerp(fr.xMin, fr.xMax, (i % 2) * 0.6f + 0.2f), 0f, Mathf.Lerp(fr.yMin, fr.yMax, (i / 2) * 0.6f + 0.2f));
                AddHabitat(OnGround(p), "field");
            }
            AddHabitat(OnGround(W.PlazaLandmark), "tree");
        }

        // =================================================================
        // 検証（村仕様 §16：主要施設が欠落せず、孤立せず、移動が無理のないものか）
        // =================================================================

        private void Validate()
        {
            FacilityType[] required =
            {
                FacilityType.Plaza, FacilityType.PlayerShop, FacilityType.MessagePlaza, FacilityType.BulletinBoard, FacilityType.Blacksmith,
                FacilityType.RepairWorkshop, FacilityType.SeedShop, FacilityType.GeneralStore, FacilityType.CarpenterWorkshop,
                FacilityType.FishingHut, FacilityType.CommunityKitchen, FacilityType.Lookout, FacilityType.PlayerHouse,
                FacilityType.MineEntrance, FacilityType.RocketSite, FacilityType.Well
            };
            foreach (var t in required)
                if (L.GetFacility(t) == null) W.Notes.Add("施設なし: " + t);

            var roads = L.Roads;
            roads.RebuildAdjacency();
            var reach = new bool[roads.nodes.Count];
            if (plazaNode >= 0 && plazaNode < reach.Length)
            {
                var queue = new Queue<int>();
                queue.Enqueue(plazaNode);
                reach[plazaNode] = true;
                var adj = new List<int>[roads.nodes.Count];
                for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
                foreach (var e in roads.edges)
                {
                    adj[e.x].Add(e.y);
                    adj[e.y].Add(e.x);
                }
                while (queue.Count > 0)
                {
                    int c = queue.Dequeue();
                    foreach (var nb in adj[c])
                    {
                        if (reach[nb]) continue;
                        reach[nb] = true;
                        queue.Enqueue(nb);
                    }
                }
            }
            foreach (var f in L.Facilities)
            {
                if (f.type == FacilityType.Bench || f.type == FacilityType.Streetlight || f.type == FacilityType.Well || f.type == FacilityType.Plaza || f.type == FacilityType.BulletinBoard) continue;
                int n = roads.NearestNode(f.entrance);
                if (n < 0 || !reach[n])
                {
                    W.Notes.Add("道でつながらない: " + f.id);
                    continue;
                }
                if (Vector3.Distance(roads.nodes[n], f.entrance) > 9f) W.Notes.Add("入口が道から遠い: " + f.id);
            }

            CheckPath(FacilityType.PlayerHouse, WorldTuning.MaxPathHomeToPlaza);
            CheckPath(FacilityType.MineEntrance, WorldTuning.MaxPathPlazaToMine);
            CheckPath(FacilityType.RocketSite, WorldTuning.MaxPathPlazaToCrash);
            CheckPath(FacilityType.FishingHut, WorldTuning.MaxPathPlazaToFishingHut);

            if (L.HousePlots.Count < WorldTuning.HousePlotMinimum) W.Notes.Add("住宅区画が少ない");
            if (L.FishingSpots.Count < WorldTuning.MinFishingSpots) W.Notes.Add("釣り場が少ない: " + L.FishingSpots.Count);
            if (L.HabitatPoints.Count < WorldTuning.MinHabitatPoints) W.Notes.Add("生息地点が少ない: " + L.HabitatPoints.Count);
            if (RectHasWater(L.FarmRect, 0f)) W.Notes.Add("農場に水がある");
            if (RectRoughness(L.FarmRect) > 1.2f) W.Notes.Add("農場が平らでない");
            if (L.IsWater(L.PlayerSpawn)) W.Notes.Add("開始地点が水の中");

            W.Valid = W.Notes.Count == 0;
            if (!W.Valid && relaxed)
            {
                // 最後の試行：致命的でない問題は許容する
                bool fatal = false;
                foreach (var note in W.Notes) if (note.StartsWith("施設なし") || note.StartsWith("道でつながらない")) fatal = true;
                W.Valid = !fatal;
            }
        }

        private void CheckPath(FacilityType type, float maxLen)
        {
            var f = L.GetFacility(type);
            if (f == null) return;
            var path = L.Roads.FindPath(W.PlazaCenter, f.entrance);
            float len = 0f;
            var prev = W.PlazaCenter;
            foreach (var p in path)
            {
                len += Vector3.Distance(prev, p);
                prev = p;
            }
            if (len > maxLen) W.Notes.Add($"{type} まで遠すぎる ({len:0}m)");
        }
    }
}
