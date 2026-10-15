using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 非常用の最小ワールド（通常の生成がすべて例外で失敗した場合だけ使う）。
    /// 平らな土地・池・必要な施設・住宅区画・釣り場を必ず用意し、ゲームが進行不能にならないようにする。
    /// </summary>
    public static class FallbackWorld
    {
        public static GeneratedWorld Create(ulong seed)
        {
            Debug.LogWarning("[World] 非常用の簡易ワールドを使います。");
            var w = new GeneratedWorld { Valid = true };
            var L = new WorldLayout { WorldSize = WorldTuning.WorldSize };
            w.Layout = L;
            w.Shape = TerrainShape.Create(seed, "world.fallback", SeededRandom.For(seed, "world.fallback.shape"));
            w.Palette = WorldPaletteFactory.Create(SeededRandom.For(seed, "world.palette"));
            var F = new Heightfield(WorldTuning.WorldSize, WorldTuning.GridStep);
            w.Field = F;
            var pond = new Vector2(70f, 70f);
            const float pr = 12f;
            for (int iz = 0; iz < F.N; iz++)
                for (int ix = 0; ix < F.N; ix++)
                {
                    int i = F.Index(ix, iz);
                    float d = Vector2.Distance(new Vector2(F.XOf(ix), F.ZOf(iz)), pond);
                    F.H[i] = d < pr ? 3f - 1.6f * (1f - (d / pr) * (d / pr)) : 3f;
                    if (d < pr + 1f)
                    {
                        F.WaterY[i] = 2.6f;
                        F.Kind[i] = WaterKind.Pond;
                        F.WaterIndex[i] = 0;
                    }
                }
            var body = new WaterBody { id = "water.pond.0", type = WaterType.Pond, radius = pr, surfaceHeight = 2.6f };
            body.points.Add(new Vector3(pond.x, 2.6f, pond.y));
            L.Waters.Add(body);
            w.Ponds.Add(new PondInfo { Center = pond, Radius = pr, Surface = 2.6f, WaterIndex = 0 });

            w.PlazaCenter = new Vector3(0f, 3f, 0f);
            w.PlazaLandmark = new Vector3(6f, 3f, 6f);
            L.Roads.nodes.Add(w.PlazaCenter);
            void Fac(FacilityType t, Vector2 c)
            {
                var size = WorldGenerator.SizeOf(t);
                float yaw = Mathf.Atan2(-c.x, -c.y) * Mathf.Rad2Deg;
                var fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                var f = new FacilityInfo
                {
                    id = WorldGenerator.IdOf(t),
                    type = t,
                    position = new Vector3(c.x, 3f, c.y),
                    rotationY = yaw,
                    size = size,
                    entrance = new Vector3(c.x, 3f, c.y) + fwd * (size.z * 0.5f + 1.8f)
                };
                L.Facilities.Add(f);
                L.Roads.nodes.Add(f.entrance);
                L.Roads.edges.Add(new Vector2Int(0, L.Roads.nodes.Count - 1));
                w.RoadSegments.Add(new RoadSegment { A = Vector2.zero, B = new Vector2(f.entrance.x, f.entrance.z), Paved = true });
            }
            Fac(FacilityType.Plaza, new Vector2(0.01f, 0.01f));
            L.Facilities[0].entrance = w.PlazaCenter;
            FacilityType[] ring =
            {
                FacilityType.PlayerShop, FacilityType.MessagePlaza, FacilityType.Blacksmith, FacilityType.RepairWorkshop, FacilityType.SeedShop,
                FacilityType.GeneralStore, FacilityType.CarpenterWorkshop, FacilityType.CommunityKitchen
            };
            for (int i = 0; i < ring.Length; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Fac(ring[i], new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 30f);
            }
            Fac(FacilityType.BulletinBoard, new Vector2(0f, -11f));
            Fac(FacilityType.Well, new Vector2(-7f, 4f));
            Fac(FacilityType.FishingHut, new Vector2(50f, 50f));
            Fac(FacilityType.Lookout, new Vector2(-120f, 100f));
            Fac(FacilityType.PlayerHouse, new Vector2(-70f, -40f));
            Fac(FacilityType.MineEntrance, new Vector2(-140f, -140f));
            Fac(FacilityType.RocketSite, new Vector2(120f, -110f));
            L.FarmRect = new Rect(-100f, -50f, WorldTuning.FarmWidth, WorldTuning.FarmDepth);
            w.FarmReserve = new Rect(L.FarmRect.xMin - 12f, L.FarmRect.yMin - 12f, L.FarmRect.width + 24f, L.FarmRect.height + 24f);
            L.CrashSite = new Vector3(120f, 3f, -110f);
            L.MineEntrance = new Vector3(-140f, 3f, -140f);
            L.PlayerSpawn = new Vector3(110f, 3f, -95f);

            for (int i = 0; i < 24; i++)
            {
                float a = (i + 0.5f) * 15f * Mathf.Deg2Rad;
                var c = new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * 75f;
                if (Vector2.Distance(c, pond) < 25f) c *= 1.35f;
                float yaw = Mathf.Atan2(-c.x, -c.y) * Mathf.Rad2Deg;
                var size = WorldGenerator.SizeOf(FacilityType.ResidentHouse);
                var fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
                var f = new FacilityInfo
                {
                    id = "facility.house." + i,
                    type = FacilityType.ResidentHouse,
                    position = new Vector3(c.x, 3f, c.y),
                    rotationY = yaw,
                    size = size,
                    entrance = new Vector3(c.x, 3f, c.y) + fwd * (size.z * 0.5f + 1.6f),
                    plotIndex = i
                };
                L.Facilities.Add(f);
                L.HousePlots.Add(new HousePlot { index = i, position = f.position, rotationY = yaw, facilityId = f.id });
                L.Roads.nodes.Add(f.entrance);
                L.Roads.edges.Add(new Vector2Int(0, L.Roads.nodes.Count - 1));
            }
            for (int i = 0; i < 24; i++)
            {
                float a = i * 15f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var stand = pond + dir * (pr + 2f);
                var spot = new FishingSpot
                {
                    id = "spot.pond0." + i,
                    waterId = body.id,
                    waterType = WaterType.Pond,
                    standPosition = new Vector3(stand.x, 3f, stand.y),
                    waterPosition = new Vector3(pond.x + dir.x * pr * 0.4f, 2.6f, pond.y + dir.y * pr * 0.4f)
                };
                spot.tags.Add("pond");
                spot.tags.Add(i % 3 == 0 ? "deep" : "shallow");
                L.FishingSpots.Add(spot);
            }
            L.Areas.Add(new AreaRegion { id = "area.village", type = AreaType.VillageCenter, center = w.PlazaCenter, radius = 45f, nameJa = "村の広場", nameEn = "Village plaza" });
            L.Areas.Add(new AreaRegion { id = "area.pond.0", type = AreaType.Pond, center = new Vector3(pond.x, 3f, pond.y), radius = pr + 9f, nameJa = "池", nameEn = "Pond" });
            L.Areas.Add(new AreaRegion { id = "area.meadow.0", type = AreaType.Meadow, center = new Vector3(-60f, 3f, 80f), radius = 60f, nameJa = "草原", nameEn = "Meadow" });
            L.Areas.Add(new AreaRegion { id = "area.forest.0", type = AreaType.Forest, center = new Vector3(100f, 3f, 120f), radius = 50f, nameJa = "森", nameEn = "Woods" });
            var r = SeededRandom.For(seed, "world.fallback.nature");
            for (int i = 0; i < 260; i++)
            {
                var p = new Vector2(r.Range(-180f, 180f), r.Range(-180f, 180f));
                if (p.magnitude < 95f || Vector2.Distance(p, pond) < pr + 3f) continue;
                var kind = i % 3 == 0 ? NatureKind.Flower : i % 3 == 1 ? NatureKind.TreeRound : NatureKind.Rock;
                var pos = new Vector3(p.x, 3f, p.y);
                w.Nature.Add(new NatureItem { Kind = kind, Position = pos, Scale = 1f, Yaw = r.Range(0f, 360f), Area = AreaType.Meadow });
                L.HabitatPoints.Add(new HabitatPoint { position = pos, area = AreaType.Meadow, feature = kind == NatureKind.Flower ? "flower" : kind == NatureKind.Rock ? "rock" : "tree" });
            }
            var field = F;
            L.HeightSampler = (x, z) => field.SampleGround(x, z);
            L.WaterTester = (x, z) => field.IsWater(x, z);
            L.Roads.RebuildAdjacency();
            return w;
        }
    }
}
