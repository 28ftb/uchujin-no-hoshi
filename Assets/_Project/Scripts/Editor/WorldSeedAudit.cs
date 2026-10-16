#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Creatures;
using Uchujin.Fishing;
using Uchujin.Mining;
using Uchujin.Weather;
using Uchujin.World;
using Debug = UnityEngine.Debug;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// ワールド生成の大量シード検査（マップ設計 v0.2 §12.3）。再生しないで、生成のコードだけを多数のシードで動かす。
    ///  ・生成の検証（必須施設・道・湖・川の区間・滝・桟橋・静かな高台など）で問題が残ったシードの数と内容
    ///  ・魚50種：シードごとに住める釣り場の数（地上＋鉱山の地下湖）と、時間帯・天候の条件が最初に揃う日
    ///  ・宇宙生物50種：シードごとの生息地点の数（場所が合う点／場所も地形要素も合う点）
    ///  ・天候：雨・晴れ・強風・霧・無風・荒天・晴れた夜 の最大の間隔（日）
    ///  ・道のり：広場から自宅・鉱山・不時着地・釣り小屋・湖・桟橋・鍛冶屋・修理屋
    ///  ・再現性：同じシードを2回作って同じになるか
    /// 結果は Logs/WorldAudit/report.md と per_seed.csv。ログには "[AUDIT]" で始まる行を出す。
    /// バッチ：Tools/Unity-Check.ps1 -Mode SeedAudit（シード数は環境変数 UCHUJIN_AUDIT_SEEDS、既定 200）。
    /// </summary>
    public static class WorldSeedAudit
    {
        private const string OutDir = "Logs/WorldAudit";
        private const int WeatherDays = 60;
        private static int debugDumps;

        [MenuItem("宇宙人の星/検証/ワールド生成の大量シード検査（50シード）")]
        private static void Menu() => RunAudit(50);

        /// <summary>バッチモード用（終わったら Unity を終了する。問題があれば終了コード 1）。</summary>
        public static void Run()
        {
            int code;
            try
            {
                int n = 200;
                var env = Environment.GetEnvironmentVariable("UCHUJIN_AUDIT_SEEDS");
                if (!string.IsNullOrEmpty(env) && int.TryParse(env, out var v) && v > 0) n = v;
                code = RunAudit(n);
            }
            catch (Exception e)
            {
                Debug.LogError("[AUDIT] 例外: " + e);
                code = 2;
            }
            EditorApplication.Exit(code);
        }

        private sealed class SeedResult
        {
            public ulong Seed;
            public int Attempt;
            public bool Valid;
            public List<string> Notes = new List<string>();
            public double Ms;
            public bool Deterministic = true;
            public int[] FishSpots;
            public int[] FishFirstDay;
            public int[] CreatureArea;
            public int[] CreaturePreferred;
            public int[] WeatherMaxGap;
            public Dictionary<string, float> Paths = new Dictionary<string, float>();
            public float LakeDiameter, LakeDepth, WaterfallDrop;
            public int Ponds, Spots, Habitats, Houses, Fords;
            public bool QuietHighland;
            // 生成バージョン3（地域・道中マップの試作）
            public Dictionary<string, int> MapAttempts = new Dictionary<string, int>();
            public float T1Seconds, R2Seconds, R2LakeDiameter, GateAngle;
            // 道の曲がり方と高低差（マップごと）
            public Dictionary<string, float[]> MapShape = new Dictionary<string, float[]>();
            public bool MapIndependent = true;
            public float VillageRelief, VillageRoadGrade, VillageCliff, VillageSteepShare, V2RoadGrade = -1f, V2SteepShare = -1f;
            public string VillageSteepAt;
            /// <summary>村の外のマップ（ID ごと）で住める魚の種類・生息地点のある生き物の種類。</summary>
            public Dictionary<string, int> MapFish = new Dictionary<string, int>();
            public Dictionary<string, int> MapCreatures = new Dictionary<string, int>();
            public Dictionary<string, int> MapSpots = new Dictionary<string, int>();
            public Dictionary<string, int> MapHabitats = new Dictionary<string, int>();
        }

        private static readonly string[] CondNames = { "雨", "晴れ（昼）", "強風", "霧", "無風", "荒天", "晴れた夜" };

        public static int RunAudit(int count)
        {
            Directory.CreateDirectory(OutDir);
            foreach (var old in Directory.GetFiles(OutDir, "weather_debug_*.txt")) File.Delete(old);
            debugDumps = 0;
            var fish = FishCatalog.All;
            var creatures = CreatureDatabase.All;
            var results = new List<SeedResult>();
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < count; i++)
            {
                ulong seed = StableHash.Derive(0xA5D17UL, "audit", i);
                results.Add(AuditSeed(seed, i < 20, fish, creatures));
                if ((i + 1) % 25 == 0) Debug.Log($"[AUDIT] {i + 1}/{count} シード（{sw.Elapsed.TotalSeconds:0} 秒）");
            }
            int problems = WriteReport(results, fish, creatures);
            WriteV1Fingerprints();
            WriteMaps(6);
            Debug.Log($"[AUDIT] 終了 シード {count}・問題 {problems} 件（{sw.Elapsed.TotalSeconds:0} 秒）。報告: {OutDir}/report.md");
            return problems == 0 ? 0 : 1;
        }

        // =================================================================
        // 1シード
        // =================================================================

        private static SeedResult AuditSeed(ulong seed, bool checkDeterminism, IReadOnlyList<FishSpecies> fish, IReadOnlyList<CreatureSpecies> creatures)
        {
            var res = new SeedResult { Seed = seed };
            var sw = Stopwatch.StartNew();
            var g = WorldGenerator.Generate(seed, WorldTuning.GeneratorVersion);
            res.Ms = sw.Elapsed.TotalMilliseconds;
            res.Attempt = g.Attempt;
            res.Valid = g.Valid;
            res.Notes.AddRange(g.Notes);
            if (checkDeterminism)
            {
                var g2 = WorldGenerator.Generate(seed, WorldTuning.GeneratorVersion);
                res.Deterministic = Fingerprint(g) == Fingerprint(g2);
            }
            var L = g.Layout;
            res.Spots = L.FishingSpots.Count;
            res.Habitats = L.HabitatPoints.Count;
            res.Houses = L.HousePlots.Count;
            res.Ponds = g.Ponds.Count;
            res.QuietHighland = g.HasQuietHighland;
            foreach (var rv in g.Rivers) res.Fords += rv.Fords.Count;
            if (g.Lakes.Count > 0)
            {
                var lk = g.Lakes[0];
                float minR = float.MaxValue;
                for (int k = 0; k < 36; k++) minR = Mathf.Min(minR, lk.RadiusAt(k / 36f * Mathf.PI * 2f));
                res.LakeDiameter = minR * 2f;
                res.LakeDepth = lk.MaxDepth;
            }
            if (g.Rivers.Count > 0) res.WaterfallDrop = g.Rivers[0].WaterfallDrop;
            if (g.Version >= 3) AuditFieldMaps(seed, g, res, checkDeterminism);
            // 村の起伏（陸の高低差・45°以上の斜面の割合）と道の一番急な坂
            {
                var F = g.Field;
                float mn = float.MaxValue, mx = float.MinValue;
                int n = 0, cliff = 0;
                for (int iz = 2; iz < F.N - 2; iz++)
                    for (int ix = 2; ix < F.N - 2; ix++)
                    {
                        int i = F.Index(ix, iz);
                        if (!float.IsNaN(F.WaterY[i]) && F.WaterY[i] > F.H[i]) continue;
                        float edge = WorldTuning.Half - Mathf.Max(Mathf.Abs(F.XOf(ix)), Mathf.Abs(F.ZOf(iz)));
                        if (edge < 30f) continue;
                        n++;
                        mn = Mathf.Min(mn, F.H[i]);
                        mx = Mathf.Max(mx, F.H[i]);
                        if (Vector3.Angle(F.Normal(ix, iz), Vector3.up) >= 45f) cliff++;
                    }
                res.VillageRelief = mx - mn;
                res.VillageCliff = 100f * cliff / Mathf.Max(1, n);
                VillageRoadSteep(g, out res.VillageRoadGrade, out res.VillageSteepShare, out res.VillageSteepAt);
                if (checkDeterminism)
                {
                    var g2v = WorldGenerator.Generate(seed, 2);
                    VillageRoadSteep(g2v, out res.V2RoadGrade, out res.V2SteepShare, out _);
                }
            }

            // 鉱山の地下湖（MiningSystem と同じ釣り場を足す）
            var ctx = new PlanetContext(new PlanetInfo { seed = seed }, true) { Layout = L };
            for (int f = 1; f <= MiningTuning.FloorCount; f++)
            {
                var ml = MineGenerator.Generate(ctx, f);
                if (ml.HasLake) L.FishingSpots.Add(MiningSystem.LakeSpot(ml, f));
            }
            var spawn = new FishSpawnModel();
            spawn.Prepare(seed, L);

            // 天候（星の気候で60日分の予定を作る）
            var climate = ClimateProfile.Create(SeededRandom.For(seed, "weather.climate"));
            var planner = new WeatherPlanner(seed, climate);
            for (int d = 1; d <= WeatherDays; d += WeatherTuning.MaxExtendDays) planner.EnsureThrough(Mathf.Min(WeatherDays, d + WeatherTuning.MaxExtendDays));
            var hours = new WeatherState[WeatherDays + 1, 24];
            for (int d = 1; d <= WeatherDays; d++)
            {
                var plan = planner.Find(d);
                for (int h = 0; h < 24; h++) hours[d, h] = WeatherPlanner.StateAt(plan, h * 60 + 30);
            }
            res.WeatherMaxGap = WeatherGaps(hours);
            for (int c = 0; c < res.WeatherMaxGap.Length; c++)
            {
                if (res.WeatherMaxGap[c] <= WeatherTuning.ConditionMaxGap[c] + 1 || debugDumps >= 3) continue;
                debugDumps++;
                var sb = new StringBuilder($"seed {seed} 条件 {CondNames[c]} 最大間隔 {res.WeatherMaxGap[c]}\n");
                for (int d = 1; d <= WeatherDays; d++)
                {
                    var plan = planner.Find(d);
                    sb.Append("day ").Append(d).Append(": ");
                    if (plan == null) { sb.Append("(予定なし)\n"); continue; }
                    foreach (var seg in plan.segments)
                        sb.Append(seg.startMinute / 60).Append(':').Append((seg.startMinute % 60).ToString("00")).Append(' ')
                          .Append(seg.state.sky).Append('/').Append(seg.state.precipitation).Append('/').Append(seg.state.wind).Append('/').Append(seg.state.visibility).Append(seg.state.phenomena != AlienPhenomenon.None ? "/" + seg.state.phenomena : "").Append("  ");
                    sb.Append('\n');
                }
                File.WriteAllText(Path.Combine(OutDir, "weather_debug_" + debugDumps + ".txt"), sb.ToString(), new UTF8Encoding(false));
                break;
            }

            // 魚50種
            res.FishSpots = new int[fish.Count];
            res.FishFirstDay = new int[fish.Count];
            for (int i = 0; i < fish.Count; i++)
            {
                var s = fish[i];
                bool underground = false, surface = false;
                foreach (var sp in spawn.Spots)
                {
                    if (!FishSpawnModel.CanLiveIn(s, sp)) continue;
                    res.FishSpots[i]++;
                    if (sp.IsUnderground) underground = true;
                    else surface = true;
                }
                res.FishFirstDay[i] = res.FishSpots[i] == 0 ? -1 : FirstDay(s, hours, surface, underground);
            }

            // 宇宙生物50種
            res.CreatureArea = new int[creatures.Count];
            res.CreaturePreferred = new int[creatures.Count];
            for (int i = 0; i < creatures.Count; i++)
            {
                var c = creatures[i];
                if (c.Underground && !c.AlsoOverworld)
                {
                    // 鉱山だけの種（ホコリフ）：出る階があれば鉱山で保証
                    res.CreatureArea[i] = res.CreaturePreferred[i] = c.MinFloor <= MiningTuning.FloorCount ? 1 : 0;
                    continue;
                }
                foreach (var map in L.AllMaps)
                    foreach (var h in map.HabitatPoints)
                    {
                        var area = h.area == AreaType.Lake ? AreaType.Pond : h.area;
                        if (Array.IndexOf(c.Areas, area) < 0) continue;
                        res.CreatureArea[i]++;
                        if (c.Features.Length == 0 || Array.IndexOf(c.Features, h.feature) >= 0) res.CreaturePreferred[i]++;
                    }
                if (c.Underground && res.CreatureArea[i] == 0) res.CreatureArea[i] = res.CreaturePreferred[i] = 1; // 地上で少なくても鉱山にいる
            }

            // 村の外のマップで住める魚・生き物の種類
            foreach (var map in L.Maps)
            {
                res.MapSpots[map.MapId] = map.FishingSpots.Count;
                res.MapHabitats[map.MapId] = map.HabitatPoints.Count;
                var ids = new HashSet<string>();
                foreach (var fs in map.FishingSpots) ids.Add(fs.id);
                int nf = 0;
                foreach (var s in fish)
                {
                    bool ok = false;
                    foreach (var sp in spawn.Spots) if (ids.Contains(sp.Id) && FishSpawnModel.CanLiveIn(s, sp)) { ok = true; break; }
                    if (ok) nf++;
                }
                res.MapFish[map.MapId] = nf;
                int nc = 0;
                foreach (var c in creatures)
                {
                    if (c.Underground && !c.AlsoOverworld) continue;
                    bool ok = false;
                    foreach (var h in map.HabitatPoints)
                    {
                        var area = h.area == AreaType.Lake ? AreaType.Pond : h.area;
                        if (Array.IndexOf(c.Areas, area) >= 0 && (c.Features.Length == 0 || Array.IndexOf(c.Features, h.feature) >= 0)) { ok = true; break; }
                    }
                    if (ok) nc++;
                }
                res.MapCreatures[map.MapId] = nc;
            }

            // 道のり
            var plaza = L.GetFacility(FacilityType.Plaza);
            if (plaza != null)
            {
                foreach (var t in new[] { FacilityType.PlayerHouse, FacilityType.MineEntrance, FacilityType.RocketSite, FacilityType.FishingHut, FacilityType.Blacksmith, FacilityType.RepairWorkshop, FacilityType.PlayerShop })
                {
                    var f = L.GetFacility(t);
                    if (f != null) res.Paths[t.ToString()] = PathLength(L, plaza.position, f.entrance);
                }
                if (g.Lakes.Count > 0) res.Paths["Lake"] = PathLength(L, plaza.position, new Vector3(g.Lakes[0].SpitBase.x, 0f, g.Lakes[0].SpitBase.y));
                if (g.HasPier) res.Paths["Pier"] = PathLength(L, plaza.position, g.PierStart);
                foreach (var vg in g.VillageGates) res.Paths["Gate." + vg.OtherMapId] = PathLength(L, plaza.position, vg.Position);
            }
            return res;
        }

        /// <summary>生成バージョン3：星の骨格と、地域・道中マップの検査（マップ設計 v0.3 §10.5 のうち試作で作る分）。</summary>
        private static void AuditFieldMaps(ulong seed, GeneratedWorld g, SeedResult res, bool checkIndependence)
        {
            var sk = g.Skeleton;
            if (sk == null)
            {
                res.Notes.Add("骨格なし");
                return;
            }
            // 骨格：5地域・6道中、村からすべての地域へつながる
            int regions = 0, routes = 0;
            foreach (var m in sk.Maps)
            {
                if (m.Kind == SkeletonMapKind.Region) regions++;
                else routes++;
                if (m.Kind == SkeletonMapKind.Route && m.Gates.Count != 2) res.Notes.Add($"骨格: 道中 {m.Id} のゲートが {m.Gates.Count}");
            }
            if (regions != 5 || routes != 6) res.Notes.Add($"骨格: 地域{regions}・道中{routes}");
            var seen = new HashSet<string> { StarSkeleton.Village };
            var q = new Queue<string>();
            q.Enqueue(StarSkeleton.Village);
            while (q.Count > 0)
            {
                var m = sk.Get(q.Dequeue());
                foreach (var gt in m.Gates)
                    if (seen.Add(gt.OtherMapId)) q.Enqueue(gt.OtherMapId);
            }
            if (seen.Count != sk.Maps.Count) res.Notes.Add($"骨格: 村からつながらないマップがある（{seen.Count}/{sk.Maps.Count}）");
            if (g.VillageGates.Count == 0) res.Notes.Add("村のゲートなし");
            res.GateAngle = g.VillageGateAngleError;

            foreach (var m in g.FieldMaps)
            {
                res.MapAttempts[m.Id] = m.Attempt;
                res.MapShape[m.Id] = MeasureShape(m);
                foreach (var n in m.Notes) res.Notes.Add($"[{m.Id}] {n}");
                if (m.Id == StarSkeleton.FlowerPath) res.T1Seconds = m.RoadLength() / WorldTuning.RunSpeed;
                if (m.Id == StarSkeleton.LakeForest && m.Lake != null)
                {
                    res.R2Seconds = m.RoadLength() / WorldTuning.RunSpeed;
                    float minR = float.MaxValue;
                    for (int k = 0; k < 72; k++) minR = Mathf.Min(minR, m.Lake.RadiusAt(k / 72f * Mathf.PI * 2f));
                    res.R2LakeDiameter = minR * 2f;
                }
                // ゲートの両側の高さ（§10.5：差 0.2m 程度）
                foreach (var gt in m.Gates)
                {
                    FieldGateInfo partner = null;
                    foreach (var vg in g.VillageGates) if (vg.Id == gt.PartnerId) partner = vg;
                    foreach (var om in g.FieldMaps) if (om.GetGate(gt.PartnerId) != null) partner = om.GetGate(gt.PartnerId);
                    if (partner == null) continue;
                    if (Mathf.Abs(partner.Position.y - gt.Position.y) > WorldTuning.FieldGateHeightTolerance)
                        res.Notes.Add($"[{m.Id}] ゲート {gt.Id} の両側の高さが違う（{gt.Position.y:0.0} / {partner.Position.y:0.0}）");
                }
                // あるマップだけ作り直しても同じになる（マップごとの乱数キー）
                if (checkIndependence)
                {
                    var again = FieldMapGenerator.Generate(seed, sk, m.Id, g.Palette);
                    if (again == null || MapFingerprint(again) != MapFingerprint(m)) res.MapIndependent = false;
                }
            }
            if (g.FieldMaps.Count < 2) res.Notes.Add($"村の外のマップが{g.FieldMaps.Count}枚");
        }

        /// <summary>
        /// [0] 本道の長さ ÷ 端から端までの直線距離、[1] 直線からの最大のずれ(m)、[2] 曲がり角（30m で向きが20°以上変わる所）の数、
        /// [3] 遊べる範囲の最低の高さ、[4] 最高の高さ、[5] 坂25°以上の地面の割合(%)、[6] 坂45°以上（登れない段・崖）の割合(%)、[7] 本道の上の最大の坂(%)
        /// </summary>
        private static float[] MeasureShape(FieldMap m)
        {
            var r = new float[9];
            r[8] = m.Type;
            var road = m.Road;
            float len = m.RoadLength();
            var a = road[0];
            var b = road[road.Count - 1];
            r[0] = len / Mathf.Max(1f, Vector2.Distance(a, b));
            foreach (var p in road) r[1] = Mathf.Max(r[1], GeoUtil.DistToSegment(p, a, b, out _));
            int turns = 0;
            float lastTurnAt = -999f, along = 0f;
            for (int i = 1; i < road.Count; i++)
            {
                along += Vector2.Distance(road[i - 1], road[i]);
                int j = i;
                float d = 0f;
                while (j < road.Count - 1 && d < 30f) { d += Vector2.Distance(road[j], road[j + 1]); j++; }
                if (d < 30f) break;
                var d0 = (road[i] - road[i - 1]).normalized;
                var d1 = (road[j] - road[j - 1]).normalized;
                if (Vector2.Angle(d0, d1) >= 20f && along - lastTurnAt > 30f)
                {
                    turns++;
                    lastTurnAt = along;
                }
            }
            r[2] = turns;
            float mn = float.MaxValue, mx = float.MinValue;
            int n = 0, s25 = 0, s45 = 0;
            for (int iz = 0; iz < m.NZ; iz++)
                for (int ix = 0; ix < m.NX; ix++)
                {
                    var l = new Vector2(m.XOf(ix), m.ZOf(iz));
                    if (!m.InsidePlayable(l, 2f)) continue;
                    float h = m.H[m.Index(ix, iz)];
                    mn = Mathf.Min(mn, h);
                    mx = Mathf.Max(mx, h);
                    float sl = Vector3.Angle(m.Normal(ix, iz), Vector3.up);
                    n++;
                    if (sl >= 25f) s25++;
                    if (sl >= 45f) s45++;
                }
            r[3] = mn;
            r[4] = mx;
            r[5] = 100f * s25 / Mathf.Max(1, n);
            r[6] = 100f * s45 / Mathf.Max(1, n);
            float maxGrade = 0f;
            for (int i = 1; i < road.Count; i++)
            {
                float dist = Vector2.Distance(road[i - 1], road[i]);
                if (dist < 0.5f) continue;
                float dh = m.SampleTerrain(road[i].x, road[i].y) - m.SampleTerrain(road[i - 1].x, road[i - 1].y);
                maxGrade = Mathf.Max(maxGrade, Mathf.Abs(dh) / dist * 100f);
            }
            r[7] = maxGrade;
            return r;
        }

        /// <summary>村の道の一番急な坂（4m の区間、%）と、15% を超える道の割合（%）。</summary>
        private static void VillageRoadSteep(GeneratedWorld g, out float worst, out float share, out string at)
        {
            var F = g.Field;
            worst = 0f;
            at = "";
            float total = 0f, steep = 0f;
            var nodes = g.Layout.Roads.nodes;
            for (int ei = 0; ei < g.Layout.Roads.edges.Count; ei++)
            {
                var e = g.Layout.Roads.edges[ei];
                var a = nodes[e.x];
                var b = nodes[e.y];
                float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
                for (float s = 0f; s + 4f <= len; s += 2f)
                {
                    var pa = Vector3.Lerp(a, b, s / len);
                    var pb = Vector3.Lerp(a, b, (s + 4f) / len);
                    float gr = Mathf.Abs(F.SampleGround(pb.x, pb.z) - F.SampleGround(pa.x, pa.z)) / 4f * 100f;
                    total += 2f;
                    if (gr > 15f) steep += 2f;
                    if (gr > worst)
                    {
                        worst = gr;
                        string pad = "";
                        float pd = float.MaxValue;
                        foreach (var pi in g.Pads)
                        {
                            float sd = pi.SignedDistance(pa.x, pa.z);
                            if (sd < pd) { pd = sd; pad = pi.Kind + "(" + (pi.FacilityId ?? "") + ")"; }
                        }
                        at = $"{pa.x:0},{pa.z:0} 橋{(g.Layout.Roads.bridgeEdges.Contains(ei) ? "上" : "外")}・辺の長さ{len:0}m・ノードの高さ {a.y:0.0}/{b.y:0.0}・地面 {F.SampleGround(pa.x, pa.z):0.0}→{F.SampleGround(pb.x, pb.z):0.0}・近い土台 {pad} {pd:0.0}m・水{(F.IsWater(pa.x, pa.z) || F.IsWater(pb.x, pb.z) ? "あり" : "なし")}";
                    }
                }
            }
            share = total > 0f ? 100f * steep / total : 0f;
        }

        public static string MapFingerprint(FieldMap m)
        {
            var sb = new StringBuilder(m.Id).Append(':').Append(m.Attempt).Append('/').Append(m.Nature.Count).Append('/').Append(m.Road.Count);
            long hsum = 0;
            for (int i = 0; i < m.H.Length; i += 29) hsum += Mathf.RoundToInt(m.H[i] * 100);
            sb.Append("/h").Append(hsum);
            foreach (var gt in m.Gates) sb.Append('/').Append(Mathf.RoundToInt(gt.Position.x * 10)).Append(',').Append(Mathf.RoundToInt(gt.Position.z * 10));
            return StableHash.Fnv1a64(sb.ToString()).ToString("X16");
        }

        private static float PathLength(WorldLayout L, Vector3 from, Vector3 to)
        {
            var path = L.Roads.FindPath(from, to);
            float len = 0f;
            var prev = from;
            foreach (var p in path)
            {
                len += Vector3.Distance(new Vector3(prev.x, 0f, prev.z), new Vector3(p.x, 0f, p.z));
                prev = p;
            }
            return len;
        }

        /// <summary>時間帯・天候の条件が最初に揃う日（1〜60。揃わなければ 99）。地下湖は天候なし・時間帯なし。</summary>
        private static int FirstDay(FishSpecies s, WeatherState[,] hours, bool surface, bool underground)
        {
            if (underground && !surface) return 1;
            for (int d = 1; d <= WeatherDays; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    if (s.IsLegend && (s.Times & FishSpawnModel.TimeOf(h)) == 0) continue;
                    if ((s.Times & FishSpawnModel.TimeOf(h)) == 0 && s.RequireWeather != WeatherNeed.Any) continue;
                    var w = hours[d, h];
                    bool afterRain = false;
                    if (!w.IsRaining)
                        for (int b = 1; b <= 5 && !afterRain; b++)
                        {
                            int hh = h - b, dd = d;
                            if (hh < 0) { hh += 24; dd--; }
                            if (dd >= 1 && hours[dd, hh].IsRaining) afterRain = true;
                        }
                    if (FishSpawnModel.WeatherMatches(s.RequireWeather, w, afterRain)) return d;
                }
            }
            return underground ? 1 : 99;
        }

        private static int[] WeatherGaps(WeatherState[,] hours)
        {
            var gaps = new int[CondNames.Length];
            var last = new int[CondNames.Length];
            for (int d = 1; d <= WeatherDays; d++)
            {
                var occ = new bool[CondNames.Length];
                for (int h = 0; h < 24; h++)
                {
                    var w = hours[d, h];
                    if (w.IsRaining) occ[0] = true;
                    if (h >= 7 && h < 17 && w.sky <= SkyState.Sunny && !w.IsRaining) occ[1] = true;
                    if (w.wind == WindLevel.Strong) occ[2] = true;
                    if (w.IsFoggy) occ[3] = true;
                    if (w.wind == WindLevel.Calm && w.precipitation <= Precipitation.Drizzle) occ[4] = true;
                    if (w.wind == WindLevel.Strong && w.precipitation >= Precipitation.Rain) occ[5] = true;
                    if ((h >= 21 || h < 4) && ((w.sky <= SkyState.Sunny && !w.IsRaining) || w.Has(AlienPhenomenon.StarBand))) occ[6] = true;
                }
                for (int c = 0; c < occ.Length; c++)
                {
                    if (!occ[c]) continue;
                    gaps[c] = Mathf.Max(gaps[c], d - last[c]);
                    last[c] = d;
                }
            }
            for (int c = 0; c < gaps.Length; c++) gaps[c] = Mathf.Max(gaps[c], WeatherDays + 1 - last[c]);
            return gaps;
        }

        /// <summary>生成結果の要約（同じかどうかの比較用）。</summary>
        public static string Fingerprint(GeneratedWorld g)
        {
            var sb = new StringBuilder();
            var L = g.Layout;
            foreach (var f in L.Facilities) sb.Append(f.id).Append(':').Append(Mathf.RoundToInt(f.position.x * 10)).Append(',').Append(Mathf.RoundToInt(f.position.z * 10)).Append(';');
            foreach (var s in L.FishingSpots) sb.Append(s.id).Append(':').Append(string.Join("|", s.tags)).Append(';');
            sb.Append(L.HabitatPoints.Count).Append('/').Append(g.Nature.Count).Append('/').Append(L.Roads.nodes.Count);
            foreach (var rv in g.Rivers) sb.Append('/').Append(rv.Points.Count).Append(':').Append(Mathf.RoundToInt(rv.Surface[0] * 100));
            long hsum = 0;
            for (int i = 0; i < g.Field.H.Length; i += 37) hsum += Mathf.RoundToInt(g.Field.H[i] * 100);
            sb.Append("/h").Append(hsum);
            foreach (var m in g.FieldMaps) sb.Append('/').Append(MapFingerprint(m));
            return StableHash.Fnv1a64(sb.ToString()).ToString("X16");
        }

        /// <summary>
        /// 上から見た地図（人の目での確認用：§12.3）。地図アプリと同じ絵に、湖の岬（白）・滝（水色）・滝の裏の洞（紫）・
        /// 静かな高台（黄）・見晴らし台（橙）・広場（赤）・本道（黒）・小道（灰）の印を描く。Logs/WorldAudit/map_N.png。
        /// </summary>
        private static void WriteMaps(int n)
        {
            for (int i = 0; i < n; i++)
            {
                ulong seed = StableHash.Derive(0xA5D17UL, "audit", i);
                var g = WorldGenerator.Generate(seed, WorldTuning.GeneratorVersion);
                var tb = new TerrainBuilder(g);
                tb.BuildColorMap();
                const int size = 512;
                var tex = tb.BuildMapTexture(size);
                System.Action<Vector2, Color, int> dot = (p, c, r) =>
                {
                    int cx = Mathf.RoundToInt((p.x + WorldTuning.Half) / WorldTuning.WorldSize * size);
                    int cy = Mathf.RoundToInt((p.y + WorldTuning.Half) / WorldTuning.WorldSize * size);
                    for (int dy = -r; dy <= r; dy++)
                        for (int dx = -r; dx <= r; dx++)
                            if (dx * dx + dy * dy <= r * r && cx + dx >= 0 && cy + dy >= 0 && cx + dx < size && cy + dy < size) tex.SetPixel(cx + dx, cy + dy, c);
                };
                foreach (var seg in g.RoadSegments)
                    for (float t = 0f; t <= 1f; t += 0.05f) dot(Vector2.Lerp(seg.A, seg.B, t), seg.Trail ? new Color(0.55f, 0.55f, 0.55f) : new Color(0.12f, 0.1f, 0.08f), 1);
                if (g.HasQuietHighland) dot(g.QuietCenter, new Color(1f, 0.9f, 0.1f), 6);
                foreach (var lk in g.Lakes) dot(lk.SpitTip, Color.white, 4);
                if (g.Waterfall != null) dot(new Vector2(g.Waterfall.Bottom.x, g.Waterfall.Bottom.z), new Color(0.3f, 0.95f, 1f), 5);
                if (g.Cave != null) dot(new Vector2(g.Cave.Entrance.x, g.Cave.Entrance.z), new Color(0.7f, 0.3f, 0.9f), 3);
                var look = g.Layout.GetFacility(FacilityType.Lookout);
                if (look != null) dot(new Vector2(look.position.x, look.position.z), new Color(1f, 0.55f, 0.1f), 5);
                dot(new Vector2(g.PlazaCenter.x, g.PlazaCenter.z), Color.red, 6);
                foreach (var vg in g.VillageGates) dot(new Vector2(vg.Position.x, vg.Position.z), Color.magenta, 6);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, "map_" + i + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                tb.Dispose();
                foreach (var m in g.FieldMaps)
                {
                    WriteFieldMap(m, Path.Combine(OutDir, $"map_{i}_{m.Id}.png"));
                    // 作り直しになった試行も見る（原因の調べ用）
                    for (int a = 0; a < m.Attempt && a < 3; a++)
                    {
                        var fa = FieldMapGenerator.GenerateAttempt(seed, g.Skeleton, m.Id, g.Palette, a);
                        if (fa != null) WriteFieldMap(fa, Path.Combine(OutDir, $"map_{i}_{m.Id}_try{a}.png"));
                    }
                }
                if (g.Skeleton != null) WriteSkeleton(g.Skeleton, Path.Combine(OutDir, $"skeleton_{i}.png"));
            }
        }

        /// <summary>地域・道中マップの上から見た絵（高さの陰影・塗り分け・水・本道（黒）・脇道（灰）・ゲート（赤）・目印の場所（黄）・隠れた場所（紫））。</summary>
        private static void WriteFieldMap(FieldMap m, string path)
        {
            const int s = 3;
            int w = m.NX * s, h = m.NZ * s;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var P = m.Palette;
            var px = new Color[w * h];
            for (int iz = 0; iz < m.NZ; iz++)
                for (int ix = 0; ix < m.NX; ix++)
                {
                    int i = m.Index(ix, iz);
                    Color c;
                    switch (m.Paint[i])
                    {
                        case FieldPaint.Meadow: c = P.Meadow; break;
                        case FieldPaint.Flowers: c = Color.Lerp(P.Meadow, new Color(1f, 0.55f, 0.75f), 0.45f); break;
                        case FieldPaint.Forest: c = Color.Lerp(P.ForestFloor, P.GrassDark, 0.3f); break;
                        case FieldPaint.WetForest: c = Color.Lerp(P.ForestFloor, P.GrassDark, 0.7f); break;
                        case FieldPaint.Road: c = new Color(0.15f, 0.12f, 0.1f); break;
                        case FieldPaint.Trail: c = new Color(0.55f, 0.55f, 0.52f); break;
                        case FieldPaint.Sand: c = P.Sand; break;
                        case FieldPaint.Mud: c = P.Mud; break;
                        case FieldPaint.Rock: c = P.Rock; break;
                        default: c = P.Grass; break;
                    }
                    if (!float.IsNaN(m.WaterY[i]) && m.WaterY[i] > m.H[i])
                        c = Color.Lerp(new Color(0.45f, 0.75f, 0.95f), new Color(0.05f, 0.2f, 0.45f), Mathf.Clamp01((m.WaterY[i] - m.H[i]) / 8f));
                    var n = m.Normal(ix, iz);
                    float shade = Mathf.Clamp(0.75f + Vector3.Dot(n, new Vector3(-0.5f, 0.75f, 0.4f).normalized) * 0.35f, 0.55f, 1.15f);
                    c = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                    // 2m ごとの等高線（起伏が分かるように。10m ごとは濃く）
                    if (ix + 1 < m.NX && iz + 1 < m.NZ)
                    {
                        int b0 = Mathf.FloorToInt(m.H[i] / 2f);
                        int b1 = Mathf.FloorToInt(m.H[m.Index(ix + 1, iz)] / 2f), b2 = Mathf.FloorToInt(m.H[m.Index(ix, iz + 1)] / 2f);
                        if (b0 != b1 || b0 != b2)
                        {
                            bool major = Mathf.FloorToInt(m.H[i] / 10f) != Mathf.FloorToInt(m.H[m.Index(ix + 1, iz)] / 10f) || Mathf.FloorToInt(m.H[i] / 10f) != Mathf.FloorToInt(m.H[m.Index(ix, iz + 1)] / 10f);
                            c = Color.Lerp(c, new Color(0.25f, 0.18f, 0.1f), major ? 0.55f : 0.25f);
                        }
                    }
                    bool outside = !m.InsidePlayable(new Vector2(m.XOf(ix), m.ZOf(iz)));
                    if (outside) c = Color.Lerp(c, Color.gray, 0.45f);
                    for (int dy = 0; dy < s; dy++)
                        for (int dx = 0; dx < s; dx++) px[(iz * s + dy) * w + ix * s + dx] = c;
                }
            // 最初のゲートから歩いて行けない、遊べる範囲の地面を赤く（隣との高さの差 1.7m 超・深さ0.6m 超の水は通れない）
            if (m.Gates.Count > 0)
            {
                var seen = new bool[m.NX * m.NZ];
                var start = m.ToLocal(m.Gates[0].Arrival.x, m.Gates[0].Arrival.z);
                m.ToGrid(start.x, start.y, out int sx, out int sz);
                var q = new Queue<int>();
                seen[m.Index(sx, sz)] = true;
                q.Enqueue(m.Index(sx, sz));
                while (q.Count > 0)
                {
                    int i = q.Dequeue();
                    int ix = i % m.NX, iz = i / m.NX;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = ix + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = iz + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (nx < 0 || nz < 0 || nx >= m.NX || nz >= m.NZ) continue;
                        int j = m.Index(nx, nz);
                        if (seen[j] || !m.InsidePlayable(new Vector2(m.XOf(nx), m.ZOf(nz)), 0.5f)) continue;
                        if (Mathf.Abs(m.H[j] - m.H[i]) > WorldTuning.FieldWalkMaxStep) continue;
                        if (!float.IsNaN(m.WaterY[j]) && m.WaterY[j] - m.H[j] > WorldLayout.WadeableDepth) continue;
                        seen[j] = true;
                        q.Enqueue(j);
                    }
                }
                for (int iz = 0; iz < m.NZ; iz++)
                    for (int ix = 0; ix < m.NX; ix++)
                    {
                        int i = m.Index(ix, iz);
                        if (seen[i] || !m.InsidePlayable(new Vector2(m.XOf(ix), m.ZOf(iz)), 0.5f)) continue;
                        if (!float.IsNaN(m.WaterY[i]) && m.WaterY[i] > m.H[i]) continue;
                        for (int dy = 0; dy < s; dy++)
                            for (int dx = 0; dx < s; dx++)
                            {
                                int pi = (iz * s + dy) * w + ix * s + dx;
                                px[pi] = Color.Lerp(px[pi], Color.red, 0.45f);
                            }
                    }
            }
            tex.SetPixels(px);
            System.Action<Vector2, Color, int> dot = (l, c, r) =>
            {
                int cx = Mathf.RoundToInt((l.x + FieldMap.Margin) / FieldMap.Step * s);
                int cy = Mathf.RoundToInt((l.y + FieldMap.Margin) / FieldMap.Step * s);
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy <= r * r && cx + dx >= 0 && cy + dy >= 0 && cx + dx < w && cy + dy < h) tex.SetPixel(cx + dx, cy + dy, c);
            };
            // 本道の急な所（4m で 13% を超える所）をオレンジで
            var rc = new float[m.Road.Count];
            for (int i = 1; i < m.Road.Count; i++) rc[i] = rc[i - 1] + Vector2.Distance(m.Road[i - 1], m.Road[i]);
            for (int i = 0; i < m.Road.Count; i++)
                for (int j = i + 1; j < m.Road.Count; j++)
                {
                    if (rc[j] - rc[i] < 4f) continue;
                    float gr = Mathf.Abs(m.SampleTerrain(m.Road[j].x, m.Road[j].y) - m.SampleTerrain(m.Road[i].x, m.Road[i].y)) / (rc[j] - rc[i]);
                    if (gr > WorldTuning.FieldRoadMaxGrade && m.InsidePlayable(m.Road[i], 1f)) dot(m.Road[i], new Color(1f, 0.5f, 0f), 3);
                    break;
                }
            foreach (var gt in m.Gates) dot(m.ToLocal(gt.Position.x, gt.Position.z), Color.red, 7);
            foreach (var p in m.Places) dot(p.Local, p.Hidden ? new Color(0.7f, 0.3f, 0.9f) : new Color(1f, 0.9f, 0.1f), 5);
            foreach (var st in m.Structures)
                if (st.Kind == FieldStructureKind.Gazebo || st.Kind == FieldStructureKind.Pier) dot(st.Local, Color.white, 4);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>星の骨格の接続図（地域＝大きな丸、道中＝小さな丸、作ったマップは明るい色。村＝赤）。</summary>
        private static void WriteSkeleton(StarSkeleton sk, string path)
        {
            const int size = 512;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(0.08f, 0.09f, 0.14f);
            tex.SetPixels(px);
            System.Func<Vector2, Vector2Int> toPx = p => new Vector2Int(Mathf.RoundToInt(size * 0.5f + p.x * 140f), Mathf.RoundToInt(size * 0.5f + p.y * 140f));
            System.Action<Vector2Int, Color, int> dot = (c, col, r) =>
            {
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if (dx * dx + dy * dy <= r * r && c.x + dx >= 0 && c.y + dy >= 0 && c.x + dx < size && c.y + dy < size) tex.SetPixel(c.x + dx, c.y + dy, col);
            };
            foreach (var m in sk.Maps)
                foreach (var gt in m.Gates)
                {
                    var a = toPx(m.StarPos);
                    var b = toPx(sk.Get(gt.OtherMapId).StarPos);
                    for (float t = 0f; t <= 1f; t += 0.004f) dot(Vector2Int.RoundToInt(Vector2.Lerp(a, b, t)), new Color(0.5f, 0.5f, 0.55f), 1);
                }
            foreach (var m in sk.Maps)
            {
                Color col = m.Id == StarSkeleton.Village ? Color.red
                    : m.Id == StarSkeleton.LakeForest ? new Color(0.3f, 0.75f, 1f)
                    : m.Id == StarSkeleton.WaterfallMountain ? new Color(0.65f, 0.55f, 0.45f)
                    : m.Id == StarSkeleton.BeachCape ? new Color(0.95f, 0.85f, 0.55f)
                    : m.Id == StarSkeleton.Highland ? new Color(1f, 0.9f, 0.2f)
                    : new Color(0.55f, 0.85f, 0.5f);
                if (!m.Built) col = Color.Lerp(col, new Color(0.3f, 0.3f, 0.35f), 0.55f);
                dot(toPx(m.StarPos), col, m.Kind == SkeletonMapKind.Region ? 16 : 8);
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>生成バージョン1の要約（既存の星の地形が変わっていないかを、以前のコードの結果と比べるため）。</summary>
        private static void WriteV1Fingerprints()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 20; i++)
            {
                ulong seed = StableHash.Derive(0xA5D17UL, "audit", i);
                var g = WorldGenerator.Generate(seed, 1);
                sb.Append(seed).Append('\t').Append(Fingerprint(g)).Append('\n');
            }
            File.WriteAllText(Path.Combine(OutDir, "v1_fingerprints.txt"), sb.ToString(), new UTF8Encoding(false));
        }

        // =================================================================
        // 報告
        // =================================================================

        private static int WriteReport(List<SeedResult> results, IReadOnlyList<FishSpecies> fish, IReadOnlyList<CreatureSpecies> creatures)
        {
            int problems = 0;
            var md = new StringBuilder();
            md.Append("# ワールド生成の大量シード検査\n\n");
            md.Append($"- 生成バージョン：{WorldTuning.GeneratorVersion}\n- シード数：{results.Count}\n- 天候の確認：各シード {WeatherDays} 日分\n\n");

            int invalid = 0, retried = 0, nonDet = 0, mapDep = 0;
            var noteCount = new Dictionary<string, int>();
            double ms = 0, msMax = 0;
            foreach (var r in results)
            {
                if (!r.Valid || r.Notes.Count > 0) invalid++;
                if (r.Attempt > 0) retried++;
                if (!r.Deterministic) nonDet++;
                if (!r.MapIndependent) mapDep++;
                ms += r.Ms;
                msMax = Math.Max(msMax, r.Ms);
                foreach (var n in r.Notes)
                {
                    string key = n.Split(':')[0];
                    noteCount[key] = noteCount.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
            problems += invalid + nonDet + mapDep;
            md.Append("## 1. 生成の検証\n\n");
            md.Append($"| 項目 | 結果 |\n|---|---|\n| 問題が残ったシード | {invalid} |\n| 作り直しが必要だったシード | {retried}（{100.0 * retried / results.Count:0.0}%） |\n| 同じシードで結果が違った（最初の20シード） | {nonDet} |\n| マップだけ作り直すと違った（最初の20シード） | {mapDep} |\n| 生成時間（平均／最大） | {ms / results.Count:0} ms ／ {msMax:0} ms |\n\n");
            var mapRetry = new Dictionary<string, int>();
            foreach (var r in results)
                foreach (var kv in r.MapAttempts)
                    if (kv.Value > 0) mapRetry[kv.Key] = mapRetry.TryGetValue(kv.Key, out var c0) ? c0 + 1 : 1;
            if (results.Count > 0 && results[0].MapAttempts.Count > 0)
            {
                md.Append("村の外のマップの作り直し：\n\n| マップ | 作り直しが必要だったシード |\n|---|---:|\n");
                foreach (var id in results[0].MapAttempts.Keys) md.Append($"| {id} | {(mapRetry.TryGetValue(id, out var c1) ? c1 : 0)} |\n");
                md.Append('\n');
            }
            if (noteCount.Count > 0)
            {
                md.Append("残った問題の内訳：\n\n| 問題 | シード数 |\n|---|---|\n");
                foreach (var kv in noteCount) md.Append($"| {kv.Key} | {kv.Value} |\n");
                md.Append('\n');
            }

            md.Append("## 2. 魚50種（住める釣り場の数・条件が最初に揃う日）\n\n| ID | 名前 | 釣り場0のシード | 釣り場（最小／平均） | 条件が揃う日（最悪） |\n|---|---|---:|---|---:|\n");
            for (int i = 0; i < fish.Count; i++)
            {
                int zero = 0, mn = int.MaxValue, worst = 0;
                double sum = 0;
                foreach (var r in results)
                {
                    int n = r.FishSpots[i];
                    if (n == 0) zero++;
                    mn = Math.Min(mn, n);
                    sum += n;
                    worst = Math.Max(worst, r.FishFirstDay[i] < 0 ? 99 : r.FishFirstDay[i]);
                }
                if (zero > 0 || worst >= 99) problems++;
                md.Append($"| {fish[i].No} | {fish[i].NameJa} | {zero} | {mn}／{sum / results.Count:0.0} | {(worst >= 99 ? "揃わない" : worst + "日目")} |\n");
            }
            md.Append('\n');

            md.Append("## 3. 宇宙生物50種（生息地点の数）\n\n「場所が合う」は生息場所の種類が合う地点、「好む地点」は地形要素（木・花・岩など）も合う地点。地下の種は鉱山でも出る。\n\n| No. | 名前 | 場所が合う地点0のシード | 好む地点0のシード | 場所が合う地点（最小／平均） |\n|---:|---|---:|---:|---|\n");
            for (int i = 0; i < creatures.Count; i++)
            {
                int zeroA = 0, zeroP = 0, mn = int.MaxValue;
                double sum = 0;
                foreach (var r in results)
                {
                    if (r.CreatureArea[i] == 0) zeroA++;
                    if (r.CreaturePreferred[i] == 0) zeroP++;
                    mn = Math.Min(mn, r.CreatureArea[i]);
                    sum += r.CreatureArea[i];
                }
                if (zeroA > 0) problems++;
                md.Append($"| {creatures[i].No:000} | {creatures[i].NameJa} | {zeroA} | {zeroP} | {mn}／{sum / results.Count:0.0} |\n");
            }
            md.Append('\n');

            md.Append("## 4. 天候の最大の間隔（日、各シード60日間で）\n\n| 条件 | 上限 | 最悪のシード | 平均 |\n|---|---:|---:|---:|\n");
            for (int c = 0; c < CondNames.Length; c++)
            {
                int worst = 0;
                double sum = 0;
                foreach (var r in results)
                {
                    worst = Math.Max(worst, r.WeatherMaxGap[c]);
                    sum += r.WeatherMaxGap[c];
                }
                int limit = WeatherTuning.ConditionMaxGap[c];
                if (worst > limit + 1) problems++;
                md.Append($"| {CondNames[c]} | {limit} | {worst} | {sum / results.Count:0.0} |\n");
            }
            md.Append('\n');

            md.Append("## 5. 広場からの道のり（m）\n\n| 行き先 | 平均 | 最大 |\n|---|---:|---:|\n");
            var keys = new List<string>();
            foreach (var r in results) foreach (var k in r.Paths.Keys) if (!keys.Contains(k)) keys.Add(k);
            foreach (var k in keys)
            {
                double sum = 0, mx = 0;
                int n = 0;
                foreach (var r in results)
                    if (r.Paths.TryGetValue(k, out var v))
                    {
                        sum += v;
                        mx = Math.Max(mx, v);
                        n++;
                    }
                md.Append($"| {k} | {(n > 0 ? sum / n : 0):0} | {mx:0} |\n");
            }
            md.Append('\n');

            md.Append("## 6. 地形の数値の分布\n\n| 項目 | 最小 | 平均 | 最大 |\n|---|---:|---:|---:|\n");
            Stat(md, "湖の差し渡し（最も狭い所, m）", results, r => r.LakeDiameter);
            Stat(md, "湖の最深部（m）", results, r => r.LakeDepth);
            Stat(md, "滝の落差（m）", results, r => r.WaterfallDrop);
            Stat(md, "池の数（森の池＋高台の池）", results, r => r.Ponds);
            Stat(md, "歩いて渡れる浅瀬の数", results, r => r.Fords);
            Stat(md, "釣り場の数", results, r => r.Spots);
            Stat(md, "生息地点の数", results, r => r.Habitats);
            Stat(md, "住宅区画の数", results, r => r.Houses);
            Stat(md, "村：陸の高低差(m。端から30m より内側)", results, r => r.VillageRelief);
            Stat(md, "村：坂45°以上の地面(%)", results, r => r.VillageCliff);
            Stat(md, "村：道の一番急な坂(%)", results, r => r.VillageRoadGrade);
            Stat(md, "村：15% を超える道の割合(%)", results, r => r.VillageSteepShare);
            var v2 = results.FindAll(x => x.V2RoadGrade >= 0f);
            if (v2.Count > 0)
            {
                Stat(md, $"（比べる）版2の村：道の一番急な坂(%)（{v2.Count}シード）", v2, r => r.V2RoadGrade);
                Stat(md, "（比べる）版2の村：15% を超える道の割合(%)", v2, r => r.V2SteepShare);
            }
            foreach (var r in results) if (r.VillageRoadGrade > 30f) Debug.Log($"[AUDIT] 村の急な坂 seed {r.Seed}：{r.VillageRoadGrade:0}%（{r.VillageSteepAt}）");
            if (results.Count > 0 && results[0].MapAttempts.Count > 0)
            {
                Stat(md, "T1 花の野道：本道を走って抜ける秒数", results, r => r.T1Seconds);
                Stat(md, "R2 湖の森：ゲートから岬の先まで走る秒数", results, r => r.R2Seconds);
                Stat(md, "R2 湖の森：湖の差し渡し（最も狭い所, m）", results, r => r.R2LakeDiameter);
                Stat(md, "村のゲートの向きと骨格の方角のずれ（度）", results, r => r.GateAngle);
                // T6 はシードで t6a / t6b のどちらか1本なので、全シードのマップを集め、そのマップがあるシードだけで数える
                var mapIds = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var x in results) foreach (var id in x.MapFish.Keys) mapIds.Add(id);
                foreach (var id in mapIds)
                {
                    string mid = id;
                    var has = results.FindAll(x => x.MapFish.ContainsKey(mid));
                    string label = has.Count == results.Count ? mid : $"{mid}（{has.Count}シード）";
                    Stat(md, $"{label}：釣り場の数", has, r => r.MapSpots.TryGetValue(mid, out var v) ? v : 0);
                    Stat(md, $"{label}：住める魚の種類", has, r => r.MapFish.TryGetValue(mid, out var v) ? v : 0);
                    Stat(md, $"{label}：生息地点の数", has, r => r.MapHabitats.TryGetValue(mid, out var v) ? v : 0);
                    Stat(md, $"{label}：好む地点のある生き物の種類", has, r => r.MapCreatures.TryGetValue(mid, out var v) ? v : 0);
                }
                string[] shapeNames = { "本道の長さ÷直線距離", "本道の直線からの最大のずれ(m)", "本道の曲がり角の数", "最低の高さ(m)", "最高の高さ(m)", "坂25°以上の地面(%)", "坂45°以上の地面(%)", "本道の最大の坂(%)" };
                var shapeIds = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var x in results) foreach (var id in x.MapShape.Keys) shapeIds.Add(id);
                foreach (var id in shapeIds)
                {
                    string mid = id;
                    var has = results.FindAll(x => x.MapShape.ContainsKey(mid));
                    string label = has.Count == results.Count ? mid : $"{mid}（{has.Count}シード）";
                    for (int k = 0; k < shapeNames.Length; k++)
                    {
                        int kk = k;
                        Stat(md, $"{label}：{shapeNames[k]}", has, r => r.MapShape.TryGetValue(mid, out var v) ? v[kk] : 0f);
                    }
                }
                Stat(md, "t1：高低差(m)", results, r => r.MapShape.TryGetValue("t1", out var v) ? v[4] - v[3] : 0f);
                // 道中の型ごとの本道の曲がり方（型0＝開けた野の一本道、1＝丘を回り込む道、2＝段丘を折り返して登る道）
                for (int ty = 0; ty < 3; ty++)
                {
                    var sub = results.FindAll(x => x.MapShape.TryGetValue("t1", out var v) && Mathf.RoundToInt(v[8]) == ty);
                    if (sub.Count == 0) continue;
                    Stat(md, $"t1 型{ty}（{sub.Count}シード）：本道の長さ÷直線距離", sub, x => x.MapShape["t1"][0]);
                    Stat(md, $"t1 型{ty}：本道の直線からの最大のずれ(m)", sub, x => x.MapShape["t1"][1]);
                    Stat(md, $"t1 型{ty}：本道を走る秒数", sub, x => x.T1Seconds);
                }
                Stat(md, "r2：高低差(m)", results, r => r.MapShape.TryGetValue("r2", out var v) ? v[4] - v[3] : 0f);
            }
            md.Append($"\n**問題の合計：{problems}**（0なら合格）\n");
            File.WriteAllText(Path.Combine(OutDir, "report.md"), md.ToString(), new UTF8Encoding(false));

            var csv = new StringBuilder("seed,attempt,valid,notes,ms,lakeDiameter,lakeDepth,waterfall,ponds,fords,spots,habitats,houses\n");
            foreach (var r in results)
                csv.Append($"{r.Seed},{r.Attempt},{r.Valid},\"{string.Join(" / ", r.Notes).Replace("\"", "'")}\",{r.Ms:0},{r.LakeDiameter:0.0},{r.LakeDepth:0.0},{r.WaterfallDrop:0.0},{r.Ponds},{r.Fords},{r.Spots},{r.Habitats},{r.Houses}\n");
            File.WriteAllText(Path.Combine(OutDir, "per_seed.csv"), csv.ToString(), new UTF8Encoding(true));
            foreach (var line in md.ToString().Split('\n'))
                if (line.StartsWith("| ") && (line.Contains("揃わない") || line.Contains("問題が残った"))) Debug.Log("[AUDIT] " + line);
            return problems;
        }

        private static void Stat(StringBuilder md, string name, List<SeedResult> rs, Func<SeedResult, float> f)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            double sum = 0;
            foreach (var r in rs)
            {
                float v = f(r);
                mn = Mathf.Min(mn, v);
                mx = Mathf.Max(mx, v);
                sum += v;
            }
            md.Append($"| {name} | {mn:0.#} | {sum / rs.Count:0.#} | {mx:0.#} |\n");
        }
    }
}
#endif
