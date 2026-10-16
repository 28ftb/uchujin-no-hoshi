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
                foreach (var h in L.HabitatPoints)
                {
                    var area = h.area == AreaType.Lake ? AreaType.Pond : h.area;
                    if (Array.IndexOf(c.Areas, area) < 0) continue;
                    res.CreatureArea[i]++;
                    if (c.Features.Length == 0 || Array.IndexOf(c.Features, h.feature) >= 0) res.CreaturePreferred[i]++;
                }
                if (c.Underground && res.CreatureArea[i] == 0) res.CreatureArea[i] = res.CreaturePreferred[i] = 1; // 地上で少なくても鉱山にいる
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
            }
            return res;
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
                tex.Apply();
                File.WriteAllBytes(Path.Combine(OutDir, "map_" + i + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                tb.Dispose();
            }
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

            int invalid = 0, retried = 0, nonDet = 0;
            var noteCount = new Dictionary<string, int>();
            double ms = 0, msMax = 0;
            foreach (var r in results)
            {
                if (!r.Valid || r.Notes.Count > 0) invalid++;
                if (r.Attempt > 0) retried++;
                if (!r.Deterministic) nonDet++;
                ms += r.Ms;
                msMax = Math.Max(msMax, r.Ms);
                foreach (var n in r.Notes)
                {
                    string key = n.Split(':')[0];
                    noteCount[key] = noteCount.TryGetValue(key, out var c) ? c + 1 : 1;
                }
            }
            problems += invalid + nonDet;
            md.Append("## 1. 生成の検証\n\n");
            md.Append($"| 項目 | 結果 |\n|---|---|\n| 問題が残ったシード | {invalid} |\n| 作り直しが必要だったシード | {retried}（{100.0 * retried / results.Count:0.0}%） |\n| 同じシードで結果が違った（最初の20シード） | {nonDet} |\n| 生成時間（平均／最大） | {ms / results.Count:0} ms ／ {msMax:0} ms |\n\n");
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
