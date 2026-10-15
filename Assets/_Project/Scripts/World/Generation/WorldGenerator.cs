using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// ワールドシードから星を決定的に生成する（村仕様 §15 の処理フロー）。
    ///  1. 地形・標高・海岸（TerrainShape）
    ///  2. 水系（川・池・海）
    ///  3. 村の広場と生活施設、主人公の家と農場、不時着地、鉱山入口、高台
    ///  4. 道路・橋・近道（A*）
    ///  5. 住宅区画、ベンチ・街灯・井戸、看板
    ///  6. 地形の仕上げ（土台・道路を平らに均す）
    ///  7. 自然物、釣り場、生息地点、エリア
    ///  8. 到達性・施設間距離の検証（村仕様 §16）。問題があれば別の乱数系列で決定的に再試行する。
    /// 乱数は用途ごとに独立（村仕様 §1.4）。同じシード・同じ生成バージョンなら同じ結果になる。
    /// </summary>
    public sealed partial class WorldGenerator
    {
        private readonly ulong seed;
        private readonly int attempt;
        private readonly bool relaxed;
        private GeneratedWorld W;
        private WorldLayout L;
        private Heightfield F;
        private TerrainShape S;
        private WorldPalette P;
        private RoadPlanner planner;
        private readonly List<PadInfo> footprints = new List<PadInfo>();
        private int seaWaterIndex = -1;

        private string KeyPrefix => "world.gen" + WorldTuning.GeneratorVersion + ".a" + attempt;
        private SeededRandom R(string key) => SeededRandom.For(seed, KeyPrefix + "." + key);

        private WorldGenerator(ulong seed, int attempt, bool relaxed)
        {
            this.seed = seed;
            this.attempt = attempt;
            this.relaxed = relaxed;
        }

        /// <summary>生成（検証に失敗したら決定的に再試行）。</summary>
        public static GeneratedWorld Generate(ulong seed)
        {
            GeneratedWorld best = null;
            for (int a = 0; a < WorldTuning.MaxGenerationAttempts; a++)
            {
                bool last = a == WorldTuning.MaxGenerationAttempts - 1;
                GeneratedWorld g = null;
                try
                {
                    g = new WorldGenerator(seed, a, last).Run();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[World] 生成 試行{a} で例外: {e}");
                    continue;
                }
                if (g.Valid) return g;
                Debug.Log($"[World] 生成 試行{a} は検証で不採用: {string.Join(" / ", g.Notes)}");
                if (best == null || g.Notes.Count < best.Notes.Count) best = g;
            }
            if (best != null)
            {
                Debug.LogWarning("[World] すべての試行で検証に問題がありました。最も問題の少ない結果を使います。");
                best.Valid = true;
            }
            return best;
        }

        private GeneratedWorld Run()
        {
            W = new GeneratedWorld { Attempt = attempt };
            L = new WorldLayout { WorldSize = WorldTuning.WorldSize };
            W.Layout = L;
            S = TerrainShape.Create(seed, KeyPrefix, R("shape"));
            W.Shape = S;
            P = WorldPaletteFactory.Create(SeededRandom.For(seed, "world.palette"));
            W.Palette = P;
            F = new Heightfield(WorldTuning.WorldSize, WorldTuning.GridStep);
            W.Field = F;

            FillBase();
            GenerateRivers();
            GeneratePonds();
            CarveWater();
            AddSeaBody();

            PlaceVillageCenter();
            PlaceRingFacilities();
            PlaceHomeAndFarm();
            PlaceCrashSite();
            PlaceMineEntrance();
            PlaceLookout();
            PlaceFishingHut();
            PlacePier();

            BuildRoads();
            PlaceSatelliteFacilities();
            PlaceHousePlots();
            PlaceStreetFurniture();
            PlaceSigns();
            PlanProjects();
            FinalizeTerrain();

            PlaceNature();
            BuildFishingSpots();
            BuildAreas();
            BuildHabitats();
            SetSamplers();
            Validate();
            return W;
        }

        // =================================================================
        // 1. 基本地形
        // =================================================================

        private void FillBase()
        {
            for (int iz = 0; iz < F.N; iz++)
                for (int ix = 0; ix < F.N; ix++)
                    F.H[F.Index(ix, iz)] = S.BaseHeight(F.XOf(ix), F.ZOf(iz));
        }

        private Vector2 ClampInside(Vector2 p, float margin)
        {
            float h = WorldTuning.Half - margin;
            return new Vector2(Mathf.Clamp(p.x, -h, h), Mathf.Clamp(p.y, -h, h));
        }

        // =================================================================
        // 2. 水系（村仕様 §3.1：山から流れた川が森や村を通り、池や海へつながる）
        // =================================================================

        private void GenerateRivers()
        {
            var r = R("rivers");
            // 本流：山の中腹から海へ
            float lat = r.Range(-0.55f, 0.55f);
            var dirOut = (S.SeaDir + S.Perp * lat).normalized;
            var src = ClampInside(S.MountainCenter + dirOut * S.MountainRadius * r.Range(0.42f, 0.58f), 22f);
            float tMouth = Mathf.Clamp(Vector2.Dot(src, S.Perp) + r.Range(-80f, 80f), -150f, 150f);
            var mouth = S.CoastPoint(tMouth, -16f);
            mouth = ClampInside(mouth, 4f);
            var main = MakeRiver(src, mouth, r, 1f, true, -1);
            W.Rivers.Add(main);

            // 支流（池へ注ぐ小川）【提案：川1〜2本】
            if (r.Chance(WorldTuning.SecondRiverChance))
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    float side = r.Chance(0.5f) ? 1f : -1f;
                    var d2 = (S.SeaDir + S.Perp * side * r.Range(0.9f, 1.6f)).normalized;
                    var src2 = ClampInside(S.MountainCenter + d2 * S.MountainRadius * r.Range(0.5f, 0.65f), 20f);
                    var pondC = ClampInside(src2 + d2 * r.Range(70f, 110f) + S.SeaDir * r.Range(10f, 40f), 30f);
                    if (S.CoastD(pondC.x, pondC.y) < 40f) continue;
                    if (DistToRivers(pondC) < 34f) continue;
                    if (Vector2.Distance(pondC, S.HillCenter) < S.HillRadius + 12f) continue;
                    float pr = r.Range(10f, 14f);
                    var pond = MakePond(pondC, pr);
                    // 小川の終点は池の縁
                    var end = pondC + (src2 - pondC).normalized * (pr * 0.6f);
                    var trib = MakeRiver(src2, end, r, 0.7f, false, W.Ponds.Count - 1);
                    if (MinDistBetweenRivers(trib, main) < 22f)
                    {
                        // 本流と近すぎるなら取りやめ
                        W.Ponds.RemoveAt(W.Ponds.Count - 1);
                        L.Waters.RemoveAt(L.Waters.Count - 1);
                        L.Waters.RemoveAt(L.Waters.Count - 1);
                        continue;
                    }
                    W.Rivers.Add(trib);
                    break;
                }
            }
        }

        private RiverPath MakeRiver(Vector2 src, Vector2 end, SeededRandom r, float widthScale, bool endsInSea, int endPond)
        {
            var rp = new RiverPath { EndsInSea = endsInSea, EndPondIndex = endPond };
            var d = end - src;
            float len = d.magnitude;
            var dir = d / Mathf.Max(0.01f, len);
            var n = new Vector2(-dir.y, dir.x);
            int count = Mathf.Max(6, Mathf.CeilToInt(len / 6f));
            float amp = r.Range(12f, 26f);
            float freq = r.Range(1.2f, 2.4f);
            float phase = r.Range(0f, Mathf.PI * 2f);
            var noise = new WorldNoise(seed, KeyPrefix + ".river" + W.Rivers.Count);
            for (int i = 0; i <= count; i++)
            {
                float u = i / (float)count;
                float taper = GeoUtil.SmoothStep(0f, 0.15f, u) * (1f - GeoUtil.SmoothStep(0.86f, 1f, u));
                float off = (Mathf.Sin(u * freq * Mathf.PI * 2f + phase) * amp + noise.Line(u * 4f) * 9f) * taper;
                var p = ClampInside(src + d * u + n * off, 5f);
                rp.Points.Add(p);
            }
            float endSurface = 0f;
            if (endPond >= 0) endSurface = W.Ponds[endPond].Surface;
            float prev = float.MaxValue;
            for (int i = 0; i < rp.Points.Count; i++)
            {
                var p = rp.Points[i];
                float h = F.SampleTerrain(p.x, p.y);
                float s = Mathf.Min(prev - 0.03f, h - 1.0f);
                s = Mathf.Max(s, endSurface);
                rp.Surface.Add(s);
                prev = s;
                float u = i / (float)(rp.Points.Count - 1);
                rp.Radius.Add(Mathf.Lerp(WorldTuning.RiverRadiusStart, WorldTuning.RiverRadiusEnd, u) * widthScale);
            }
            // 滝：上流で最も落差の大きい所を強調する（釣り仕様 §6「滝の裏など」）
            int best = -1;
            float bestDrop = 0f;
            int limit = Mathf.Max(3, Mathf.RoundToInt(rp.Points.Count * 0.45f));
            for (int i = 2; i < limit; i++)
            {
                float drop = rp.Surface[i - 1] - rp.Surface[i];
                if (drop > bestDrop)
                {
                    bestDrop = drop;
                    best = i;
                }
            }
            if (best > 0 && widthScale >= 1f)
            {
                for (int j = 0; j < best; j++)
                {
                    var p = rp.Points[j];
                    float h = F.SampleTerrain(p.x, p.y);
                    rp.Surface[j] = Mathf.Min(h - 0.7f, rp.Surface[j] + 1.8f);
                    if (j > 0) rp.Surface[j] = Mathf.Min(rp.Surface[j], rp.Surface[j - 1]);
                }
                rp.WaterfallIndex = best;
                rp.WaterfallDrop = rp.Surface[best - 1] - rp.Surface[best];
            }

            var body = new WaterBody
            {
                id = "water.river." + W.Rivers.Count,
                type = WaterType.River,
                radius = rp.Radius[rp.Radius.Count / 2],
                surfaceHeight = rp.Surface[rp.Surface.Count / 2]
            };
            for (int i = 0; i < rp.Points.Count; i++) body.points.Add(new Vector3(rp.Points[i].x, rp.Surface[i], rp.Points[i].y));
            rp.WaterIndex = L.Waters.Count;
            L.Waters.Add(body);
            return rp;
        }

        private PondInfo MakePond(Vector2 c, float radius)
        {
            float rim = float.MaxValue;
            for (int k = 0; k < 12; k++)
            {
                float a = k / 12f * Mathf.PI * 2f;
                rim = Mathf.Min(rim, F.SampleTerrain(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius));
            }
            float surface = Mathf.Min(rim - 0.35f, F.SampleTerrain(c.x, c.y) - 0.3f);
            var pond = new PondInfo { Center = c, Radius = radius, Surface = surface, WaterIndex = L.Waters.Count };
            var body = new WaterBody { id = "water.pond." + W.Ponds.Count, type = WaterType.Pond, radius = radius, surfaceHeight = surface };
            body.points.Add(new Vector3(c.x, surface, c.y));
            L.Waters.Add(body);
            W.Ponds.Add(pond);
            return pond;
        }

        private void GeneratePonds()
        {
            var r = R("ponds");
            int target = r.Range(WorldTuning.PondCountMin, WorldTuning.PondCountMax + 1);
            int guard = 0;
            while (W.Ponds.Count < target && guard++ < 400)
            {
                var c = new Vector2(r.Range(-165f, 165f), r.Range(-165f, 165f));
                float radius = r.Range(WorldTuning.PondRadiusMin, WorldTuning.PondRadiusMax);
                if (S.CoastD(c.x, c.y) < 34f) continue;
                if (S.MountainFactor(c.x, c.y) < 1.0f) continue;
                if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + radius + 8f) continue;
                if (DistToRivers(c) < radius + 16f) continue;
                bool near = false;
                foreach (var p in W.Ponds) if (Vector2.Distance(p.Center, c) < p.Radius + radius + 24f) near = true;
                if (near) continue;
                if (F.Roughness(c.x, c.y, radius) > 3.2f) continue;
                MakePond(c, radius);
            }
        }

        private float DistToRivers(Vector2 p)
        {
            float best = float.MaxValue;
            foreach (var rv in W.Rivers)
                for (int i = 1; i < rv.Points.Count; i++)
                    best = Mathf.Min(best, GeoUtil.DistToSegment(p, rv.Points[i - 1], rv.Points[i], out _));
            return best;
        }

        private static float MinDistBetweenRivers(RiverPath a, RiverPath b)
        {
            float best = float.MaxValue;
            foreach (var p in a.Points)
                for (int i = 1; i < b.Points.Count; i++)
                    best = Mathf.Min(best, GeoUtil.DistToSegment(p, b.Points[i - 1], b.Points[i], out _));
            return best;
        }

        /// <summary>川・池を掘り、水面マスクを作る。海は高さ0より低い海岸側。</summary>
        private void CarveWater()
        {
            foreach (var rv in W.Rivers) CarveRiver(rv);
            foreach (var pd in W.Ponds) CarvePond(pd);
        }

        private void CarveRiver(RiverPath rv)
        {
            float rMax = 0f;
            Vector2 bmin = new Vector2(float.MaxValue, float.MaxValue), bmax = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < rv.Points.Count; i++)
            {
                rMax = Mathf.Max(rMax, rv.Radius[i]);
                bmin = Vector2.Min(bmin, rv.Points[i]);
                bmax = Vector2.Max(bmax, rv.Points[i]);
            }
            float margin = rMax + 9f;
            F.ToGrid(bmin.x - margin, bmin.y - margin, out int x0, out int z0);
            F.ToGrid(bmax.x + margin, bmax.y + margin, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(F.XOf(ix), F.ZOf(iz));
                    float best = float.MaxValue;
                    int seg = -1;
                    float segT = 0f;
                    for (int i = 1; i < rv.Points.Count; i++)
                    {
                        float dd = GeoUtil.DistToSegment(p, rv.Points[i - 1], rv.Points[i], out float t);
                        if (dd < best)
                        {
                            best = dd;
                            seg = i;
                            segT = t;
                        }
                    }
                    if (seg < 0) continue;
                    float s = Mathf.Lerp(rv.Surface[seg - 1], rv.Surface[seg], segT);
                    float rr = Mathf.Lerp(rv.Radius[seg - 1], rv.Radius[seg], segT);
                    int idx = F.Index(ix, iz);
                    float h = F.H[idx];
                    if (best < rr)
                    {
                        float bed = s - WorldTuning.RiverDepth * (1f - (best / rr) * (best / rr)) - 0.15f;
                        h = Mathf.Min(h, bed);
                    }
                    else if (best < rr + 8f)
                    {
                        float target = s + 0.35f + (best - rr) * 0.45f;
                        if (h > target) h = target + (h - target) * GeoUtil.SmoothStep(rr, rr + 8f, best);
                        if (best < rr + 2.5f && h < s + 0.3f) h = s + 0.3f;
                    }
                    F.H[idx] = h;
                    if (best < rr + 1.2f)
                    {
                        if (float.IsNaN(F.WaterY[idx]) || F.WaterY[idx] < s)
                        {
                            F.WaterY[idx] = s;
                            F.Kind[idx] = WaterKind.River;
                            F.WaterIndex[idx] = (short)rv.WaterIndex;
                        }
                    }
                }
            }
        }

        private void CarvePond(PondInfo pd)
        {
            float margin = pd.Radius + 8f;
            F.ToGrid(pd.Center.x - margin, pd.Center.y - margin, out int x0, out int z0);
            F.ToGrid(pd.Center.x + margin, pd.Center.y + margin, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(F.XOf(ix), F.ZOf(iz));
                    float d = Vector2.Distance(p, pd.Center);
                    int idx = F.Index(ix, iz);
                    float h = F.H[idx];
                    if (d < pd.Radius)
                    {
                        float k = d / pd.Radius;
                        h = Mathf.Min(h, pd.Surface - 1.9f * (1f - k * k) - 0.15f);
                    }
                    else if (d < pd.Radius + 8f)
                    {
                        float target = pd.Surface + 0.3f + (d - pd.Radius) * 0.4f;
                        if (h > target) h = target + (h - target) * GeoUtil.SmoothStep(pd.Radius, pd.Radius + 8f, d);
                        if (d < pd.Radius + 2.5f && h < pd.Surface + 0.3f) h = pd.Surface + 0.3f;
                    }
                    F.H[idx] = h;
                    if (d < pd.Radius + 1.2f)
                    {
                        F.WaterY[idx] = pd.Surface;
                        F.Kind[idx] = WaterKind.Pond;
                        F.WaterIndex[idx] = (short)pd.WaterIndex;
                    }
                }
            }
        }

        private void AddSeaBody()
        {
            seaWaterIndex = L.Waters.Count;
            var body = new WaterBody { id = "water.sea", type = WaterType.Sea, radius = 0f, surfaceHeight = WorldTuning.SeaLevel };
            for (float t = -190f; t <= 190f; t += 20f)
            {
                var c = S.CoastPoint(t, 0f);
                if (Mathf.Abs(c.x) > WorldTuning.Half || Mathf.Abs(c.y) > WorldTuning.Half) continue;
                body.points.Add(new Vector3(c.x, 0f, c.y));
            }
            L.Waters.Add(body);
            for (int iz = 0; iz < F.N; iz++)
            {
                for (int ix = 0; ix < F.N; ix++)
                {
                    int idx = F.Index(ix, iz);
                    if (!float.IsNaN(F.WaterY[idx])) continue;
                    float x = F.XOf(ix), z = F.ZOf(iz);
                    if (S.CoastD(x, z) < 8f && F.H[idx] < WorldTuning.SeaLevel + 0.4f)
                    {
                        F.WaterY[idx] = WorldTuning.SeaLevel;
                        F.Kind[idx] = WaterKind.Sea;
                        F.WaterIndex[idx] = (short)seaWaterIndex;
                    }
                }
            }
        }

        // =================================================================
        // 平坦化の共通処理
        // =================================================================

        private float AverageHeight(Vector2 c, float radius)
        {
            float sum = F.SampleTerrain(c.x, c.y);
            int n = 1;
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * Mathf.PI * 2f;
                sum += F.SampleTerrain(c.x + Mathf.Cos(a) * radius, c.y + Mathf.Sin(a) * radius);
                n++;
            }
            return sum / n;
        }

        private void ApplyPad(PadInfo pad)
        {
            float r = pad.Radius + pad.Blend + 1f;
            F.ToGrid(pad.Center.x - r, pad.Center.y - r, out int x0, out int z0);
            F.ToGrid(pad.Center.x + r, pad.Center.y + r, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    float w = pad.Weight(F.XOf(ix), F.ZOf(iz));
                    if (w <= 0f) continue;
                    int idx = F.Index(ix, iz);
                    // 水面は埋めない（川・池を土台でふさがない）
                    if (!float.IsNaN(F.WaterY[idx]) && F.WaterY[idx] > F.H[idx]) continue;
                    F.H[idx] = Mathf.Lerp(F.H[idx], pad.TargetY, w);
                }
            }
        }

        private PadInfo AddPad(PadKind kind, Vector2 center, Vector2 half, float yaw, bool circle, float blend, string facilityId, float? targetY = null)
        {
            var pad = new PadInfo
            {
                Kind = kind,
                Center = center,
                HalfSize = half,
                Yaw = yaw,
                Circle = circle,
                Blend = blend,
                FacilityId = facilityId
            };
            pad.TargetY = targetY ?? Mathf.Max(AverageHeight(center, circle ? half.x * 0.8f : half.magnitude * 0.7f), WorldTuning.SeaLevel + 0.9f);
            W.Pads.Add(pad);
            ApplyPad(pad);
            return pad;
        }

        private PadInfo AddFootprint(Vector2 center, Vector2 half, float yaw, bool circle = false)
        {
            var fp = new PadInfo { Kind = PadKind.Building, Center = center, HalfSize = half, Yaw = yaw, Circle = circle, BlocksRoads = true, Blend = 0f };
            footprints.Add(fp);
            return fp;
        }

        // =================================================================
        // 設定：高さ・水判定
        // =================================================================

        private void SetSamplers()
        {
            var field = F;
            L.HeightSampler = (x, z) => field.SampleGround(x, z);
            L.WaterTester = (x, z) => field.IsWater(x, z);
            L.Roads.RebuildAdjacency();
        }
    }
}
