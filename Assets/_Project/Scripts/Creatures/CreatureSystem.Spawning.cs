using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Creatures
{
    /// <summary>置いた餌（餌で誘う：生き物仕様 §5.2）。各端末の見た目と野生の動きにだけ影響する。</summary>
    public sealed class BaitSpot
    {
        public GameObject Go;
        public Vector3 Position;
        public ItemDefinition Def;
        public float Until;
        public float Radius;
        public string AreaId;
        public WildCreature Eater;
        public bool Summoned;
    }

    /// <summary>
    /// 野生の出現（生き物仕様 §4：四季なし。場所・時間帯・天候・宇宙固有現象・周りの物で変わる）。
    /// ホスト（IsAuthority）だけがプレイヤーの周りに決定的〜準ランダムに出し、星の状態として同期する。
    /// 地下の種は鉱山の階に入った時（AreaChangedEvent "mine:N"）にプレイヤー周辺の床へ出す。
    /// </summary>
    public sealed partial class CreatureSystem
    {
        // ---------------- 【仮数値】出現 ----------------
        public const float SpawnInterval = 3f;
        public const int MaxWildNearPlayer = 9;
        public const float SpawnMinDist = 14f;
        public const float SpawnMaxDist = 42f;
        public const float NearCountRadius = 45f;
        public const float DespawnDistance = 95f;
        public const int WildLifetimeMinutes = 150;
        public const int MaxSameSpeciesNear = 2;
        public static readonly float[] RarityWeight = { 10f, 6f, 3f, 1.2f, 0.5f, 0.15f };
        public const float BaitSeconds = 45f;
        public const float BaitRadius = 12f;

        private Transform wildRoot;
        private Transform ownedRoot;
        private readonly Dictionary<string, WildCreature> wildViews = new Dictionary<string, WildCreature>();
        private SeededRandom spawnRng;
        private float spawnTimer;
        private int spawnCounter;
        private readonly List<BaitSpot> baits = new List<BaitSpot>();

        private class PendingMineSpawn
        {
            public string AreaId;
            public float Timer;
            public int Count;
            public string Reason;
            public string OnlySpecies;
            public Vector3 Center;
            public bool UseCurrentPosition;
        }
        private PendingMineSpawn pendingMineSpawn;

        private struct SpawnCandidate
        {
            public Vector3 Pos;
            public AreaType Area;
            public bool HasArea;
            public string Feature;
            public string Reason;
            /// <summary>静かな場所か（静かな高台の中で、近くに住民がいない：マップ設計 v0.2 §2.6）。</summary>
            public bool Quiet;
        }

        /// <summary>静かな高台の中で、半径40m【仮数値】に住民がいなければ静か。</summary>
        private bool IsQuietSpot(WorldLayout layout, Vector3 p)
        {
            bool inQuiet = false;
            foreach (var a in layout.Areas)
                if (a.quiet && a.Contains(p))
                {
                    inQuiet = true;
                    break;
                }
            if (!inQuiet) return false;
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null)
                foreach (var r in dir.ActiveResidents)
                    if (r != null && (r.Position - p).sqrMagnitude < 40f * 40f) return false;
            return true;
        }

        public IEnumerable<WildCreature> WildViews => wildViews.Values;
        public IReadOnlyList<BaitSpot> Baits => baits;

        private void EnsureRoots()
        {
            if (Game == null || Game.SceneRoot == null) return;
            if (wildRoot == null) wildRoot = PrimitiveBuilder.Empty("WildCreatures", Game.SceneRoot, Vector3.zero).transform;
            if (ownedRoot == null) ownedRoot = PrimitiveBuilder.Empty("OwnedCreatures", Game.SceneRoot, Vector3.zero).transform;
        }

        private void DestroyViews()
        {
            foreach (var v in wildViews.Values) if (v != null) Object.Destroy(v.gameObject);
            wildViews.Clear();
            foreach (var v in avatars.Values) if (v != null) Object.Destroy(v.gameObject);
            avatars.Clear();
            foreach (var b in baits) if (b.Go != null) Object.Destroy(b.Go);
            baits.Clear();
            if (wildRoot != null) Object.Destroy(wildRoot.gameObject);
            if (ownedRoot != null) Object.Destroy(ownedRoot.gameObject);
            wildRoot = null;
            ownedRoot = null;
        }

        private SeededRandom SpawnRng
        {
            get
            {
                if (spawnRng == null && Game != null && Game.Planet != null && Clock != null)
                    spawnRng = SeededRandom.For(Game.Planet.seed, "creatures.spawn." + Clock.Day + "." + Clock.Hour);
                return spawnRng ?? new SeededRandom(12345);
            }
        }

        // =====================================================================
        // 地上の出現
        // =====================================================================

        private void UpdateSpawning(float dt)
        {
            spawnTimer -= dt;
            if (spawnTimer > 0f) return;
            spawnTimer = SpawnInterval;
            var layout = Game.Layout;
            if (layout == null || Clock == null) return;
            bool changed = false;

            // 遠くなった個体は消える（全プレイヤーから離れている時だけ）
            var remove = new List<string>();
            foreach (var w in wilds.Values)
            {
                bool near = false;
                bool anyInArea = false;
                foreach (var p in Game.Players.All)
                {
                    if (p.CurrentAreaId != w.areaId) continue;
                    anyInArea = true;
                    if ((p.Position - w.Position).sqrMagnitude < DespawnDistance * DespawnDistance) { near = true; break; }
                }
                if (AreaIds.IsMine(w.areaId) ? !anyInArea : !near) remove.Add(w.wildId);
            }
            foreach (var id in remove)
            {
                wilds.Remove(id);
                RemoveWildView(id);
                changed = true;
            }

            foreach (var p in Game.Players.All)
            {
                // 村と、村の外の地域・道中マップ（生成バージョン3）。いるマップの生息地点から出す
                if (!AreaIds.IsSurface(p.CurrentAreaId)) continue;
                if (SpawnAround(p, layout.LayoutAt(p.Position))) changed = true;
            }
            if (changed)
            {
                MarkDirty();
                RefreshWildViews();
            }
        }

        private bool SpawnAround(PlayerState player, WorldLayout layout)
        {
            Vector3 pp = player.Position;
            int near = 0;
            foreach (var w in wilds.Values)
                if (w.areaId == player.CurrentAreaId && (w.Position - pp).sqrMagnitude < NearCountRadius * NearCountRadius) near++;
            if (near >= MaxWildNearPlayer) return false;

            var rng = SpawnRng;
            var candidates = GatherCandidates(player, layout, rng);
            if (candidates.Count == 0) return false;

            var weather = Game.Service<IWeatherService>();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var cand = candidates[rng.Range(0, candidates.Count)];
                cand.Quiet = IsQuietSpot(layout, cand.Pos);
                var ws = weather != null ? weather.GetAt(cand.Pos) : WeatherState.Fine;
                bool onWater = layout.IsWater(cand.Pos);
                var weights = new List<float>(CreatureDatabase.All.Count);
                float total = 0f;
                foreach (var sp in CreatureDatabase.All)
                {
                    float wgt = SpeciesWeight(sp, cand, ws, onWater, pp);
                    weights.Add(wgt);
                    total += wgt;
                }
                if (total <= 0f) continue;
                int idx = rng.WeightedIndex(weights);
                if (idx < 0) continue;
                var chosen = CreatureDatabase.All[idx];
                var pos = cand.Pos + new Vector3(rng.Range(-1.2f, 1.2f), 0, rng.Range(-1.2f, 1.2f));
                pos = PlaceOnSurface(layout, pos, chosen);
                AddWild(chosen, pos, player.CurrentAreaId, cand.Reason);
                return true;
            }
            return false;
        }

        private List<SpawnCandidate> GatherCandidates(PlayerState player, WorldLayout layout, SeededRandom rng)
        {
            var list = new List<SpawnCandidate>();
            Vector3 pp = player.Position;
            float min2 = SpawnMinDist * SpawnMinDist, max2 = SpawnMaxDist * SpawnMaxDist;
            foreach (var h in layout.HabitatPoints)
            {
                float d = (h.position - pp).sqrMagnitude;
                if (d < min2 || d > max2) continue;
                list.Add(new SpawnCandidate { Pos = h.position, Area = h.area, HasArea = true, Feature = h.feature, Reason = "normal" });
            }
            // 生息地点が少ない場所でも出会えるよう、周囲の地面も候補にする
            for (int i = 0; i < 6; i++)
            {
                var c = rng.InsideUnitCircle().normalized * rng.Range(SpawnMinDist, SpawnMaxDist);
                var p = layout.OnGround(pp + new Vector3(c.x, 0, c.y));
                var reg = layout.GetAreaAt(p);
                bool inFarm = layout.FarmRect.width > 0 && layout.FarmRect.Contains(new Vector2(p.x, p.z));
                list.Add(new SpawnCandidate { Pos = p, Area = inFarm ? AreaType.Farm : reg != null ? reg.type : AreaType.Meadow, HasArea = inFarm || reg != null, Feature = null, Reason = "normal" });
            }
            // 農場への自然誘致（生き物仕様 §9）：作物や生き物がいる農場に寄ってくる
            if (layout.FarmRect.width > 2f)
            {
                var fc = new Vector3(layout.FarmRect.center.x, 0, layout.FarmRect.center.y);
                float fd = (new Vector3(pp.x, 0, pp.z) - fc).sqrMagnitude;
                if (fd < 60f * 60f)
                {
                    var farm = Game.Service<IFarmService>();
                    int planted = farm != null ? farm.PlantedPlotCount : 0;
                    for (int i = 0; i < 2 + Mathf.Min(4, planted / 6); i++)
                    {
                        var p = layout.OnGround(new Vector3(rng.Range(layout.FarmRect.xMin, layout.FarmRect.xMax), 0, rng.Range(layout.FarmRect.yMin, layout.FarmRect.yMax)));
                        if ((p - pp).sqrMagnitude < 6f * 6f) continue;
                        list.Add(new SpawnCandidate { Pos = p, Area = AreaType.Farm, HasArea = true, Feature = planted > 0 ? (i % 2 == 0 ? "flower" : "tree") : "grass", Reason = "farm" });
                    }
                }
            }
            // 自宅の庭の家具・装飾に寄ってくる（家具仕様 §10）
            var housing = Game.Service<IHousingService>();
            if (housing != null)
            {
                foreach (var f in housing.GetPlayerHomeFurniture(player.PlayerId))
                {
                    if (f == null || f.Location != "garden") continue;
                    float d = (f.Position - pp).sqrMagnitude;
                    if (d < 6f * 6f || d > max2) continue;
                    var def = ItemRegistry.Get(f.ItemId);
                    string feat = FeatureFromFurniture(def);
                    if (feat == null) continue;
                    list.Add(new SpawnCandidate { Pos = f.Position, Area = feat == "water" ? AreaType.Pond : AreaType.Residential, HasArea = true, Feature = feat, Reason = "home" });
                }
            }
            return list;
        }

        private static string FeatureFromFurniture(ItemDefinition def)
        {
            if (def == null) return null;
            if (def.HasTag(ItemTags.Aquarium)) return "water";
            if (def.HasTag(ItemTags.Light)) return "streetlight";
            if (def.HasTag(ItemTags.Flower) || def.HasTag(ItemTags.Plant)) return "flower";
            if (def.HasTag(ItemTags.Storage)) return "box";
            if (def.HasTag(ItemTags.Music)) return "roof";
            if (def.HasTag(ItemTags.CreatureHabitat)) return "grass";
            return null;
        }

        private float SpeciesWeight(CreatureSpecies sp, SpawnCandidate cand, WeatherState ws, bool onWater, Vector3 playerPos)
        {
            if (sp.Underground && !sp.AlsoOverworld) return 0f;
            var part = Clock.CurrentDayPart;
            if ((sp.Active & CreatureSpecies.ToMask(part)) == 0) return 0f;

            // 水の上には水に浮かぶ生き物だけ
            bool waterFloater = sp.Behavior == WildBehavior.Float && (sp.Group == CreatureGroup.Waterside || sp.No == 1);
            if (onWater && !waterFloater) return 0f;

            float w = RarityWeight[Mathf.Clamp(sp.Rarity, 0, 5)];
            if (cand.HasArea)
            {
                // 湖の岸は池と同じに扱う（生き物のデータは変えない：マップ設計 v0.2 §2.3）
                var area = cand.Area == AreaType.Lake ? AreaType.Pond : cand.Area;
                bool areaOk = System.Array.IndexOf(sp.Areas, area) >= 0;
                if (area == AreaType.Pond || area == AreaType.Riverside || area == AreaType.Coast)
                    areaOk |= onWater && waterFloater && System.Array.IndexOf(sp.Areas, AreaType.Pond) >= 0;
                if (!areaOk) return 0f;
            }
            else w *= 0.3f;

            if (!string.IsNullOrEmpty(cand.Feature))
            {
                if (System.Array.IndexOf(sp.Features, cand.Feature) >= 0) w *= 2.5f;
                else if (sp.Features.Length > 0) w *= 0.6f;
            }
            if (sp.Catch == CatchStyle.Lure && (cand.Feature == null || System.Array.IndexOf(sp.Features, cand.Feature) < 0)) w *= 0.3f;

            // 天候（時間・天候仕様 §9）
            bool raining = ws.IsRaining;
            if ((sp.Weather & WeatherLike.Rain) != 0) w *= raining ? 2.5f : (sp.No == 26 ? 0.12f : 0.7f);
            if ((sp.Weather & WeatherLike.Dry) != 0 && raining) w *= 0.2f;
            if ((sp.Weather & WeatherLike.Sunny) != 0 && !raining && ws.sky <= SkyState.Sunny) w *= 1.6f;
            if ((sp.Weather & WeatherLike.Wind) != 0) w *= ws.wind == WindLevel.Strong ? 2.5f : ws.wind == WindLevel.Breeze ? 1.6f : 0.8f;
            if ((sp.Weather & WeatherLike.Fog) != 0 && ws.IsFoggy) w *= 2f;
            if ((sp.Weather & WeatherLike.AfterRain) != 0 && IsAfterRain()) w *= 2.2f;
            if (sp.Phenomena != AlienPhenomenon.None && (ws.phenomena & sp.Phenomena) != 0) w *= 3f;
            if (sp.Underground)
            {
                w *= 0.35f;
                if (ws.Has(AlienPhenomenon.CrystalHaze)) w *= 4f; // 結晶霞：鉱物系の生物が地表に出る
            }

            // 同じ種が近くに多すぎない
            int same = 0;
            foreach (var x in wilds.Values)
                if (x.speciesId == sp.Id && AreaIds.IsSurface(x.areaId) && (x.Position - playerPos).sqrMagnitude < NearCountRadius * NearCountRadius) same++;
            if (same >= MaxSameSpeciesNear) return 0f;

            // 静かな高台（ユメボシ「夜の静かな高台」・ホシノコ「生き物たちが静かに集まる場所」）
            if ((sp.No == 49 || sp.No == 50) && cand.Quiet) w *= 2.5f;

            // ホシノコ：生き物たちが静かに集まる夜、または星の光帯の下で（修理の必須条件ではない）
            if (sp.No == 50)
            {
                int gathered = 0;
                foreach (var x in wilds.Values) if ((x.Position - cand.Pos).sqrMagnitude < 30f * 30f) gathered++;
                int farmCreatures = 0;
                if (cand.Area == AreaType.Farm) foreach (var c in placed.Values) if (c.placement == CreaturePlacement.Farm) farmCreatures++;
                bool ok = ws.Has(AlienPhenomenon.StarBand) || gathered >= 6 || farmCreatures >= 5 || cand.Quiet;
                if (!ok) return 0f;
            }
            return w;
        }

        private Vector3 PlaceOnSurface(WorldLayout layout, Vector3 pos, CreatureSpecies sp)
        {
            var g = layout.OnGround(pos);
            if (layout.IsWater(g))
            {
                float best = float.MaxValue, surf = g.y;
                foreach (var wb in layout.Waters)
                {
                    foreach (var pt in wb.points)
                    {
                        float d = (new Vector2(pt.x, pt.z) - new Vector2(g.x, g.z)).sqrMagnitude;
                        if (d < best) { best = d; surf = wb.surfaceHeight; }
                    }
                }
                g.y = Mathf.Max(g.y, surf);
            }
            return g;
        }

        public WildRecord AddWild(CreatureSpecies sp, Vector3 pos, string areaId, string reason)
        {
            long now = Clock != null ? Clock.TotalMinutes : 0;
            spawnCounter++;
            var w = new WildRecord
            {
                wildId = Ids.New(),
                speciesId = sp.Id,
                areaId = areaId,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                spawnMinute = now,
                expireMinute = now + WildLifetimeMinutes + (spawnCounter % 5) * 15,
                reason = reason,
                individualSeed = (Game.Planet != null ? Game.Planet.seedText : "") + ":" + now + ":" + spawnCounter + ":" + sp.Id
            };
            wilds[w.wildId] = w;
            return w;
        }

        private void ExpireWilds(bool hourly)
        {
            if (Clock == null) return;
            long now = Clock.TotalMinutes;
            var remove = new List<string>();
            foreach (var w in wilds.Values)
            {
                if (w.expireMinute > now) continue;
                bool near = false;
                foreach (var p in Game.Players.All)
                    if (p.CurrentAreaId == w.areaId && (p.Position - w.Position).sqrMagnitude < 20f * 20f) { near = true; break; }
                if (near) w.expireMinute = now + 30; // 目の前では消さない
                else remove.Add(w.wildId);
            }
            // 活動時間外になった種は帰っていく（夜の生き物は朝に、昼の生き物は夜に）
            if (hourly)
            {
                var part = Clock.CurrentDayPart;
                foreach (var w in wilds.Values)
                {
                    var sp = CreatureDatabase.Get(w.speciesId);
                    if (sp == null || AreaIds.IsMine(w.areaId)) continue;
                    if ((sp.Active & CreatureSpecies.ToMask(part)) == 0 && !remove.Contains(w.wildId)) w.expireMinute = System.Math.Min(w.expireMinute, now + 20);
                }
            }
            foreach (var id in remove)
            {
                wilds.Remove(id);
                RemoveWildView(id);
            }
            if (remove.Count > 0) MarkDirty();
        }

        // =====================================================================
        // 鉱山の出現
        // =====================================================================

        private void OnAreaChanged(AreaChangedEvent e)
        {
            var local = LocalPlayer;
            if (local != null && e.PlayerId == local.PlayerId)
            {
                if (IsAuthority && AreaIds.IsMine(e.ToAreaId))
                {
                    pendingMineSpawn = new PendingMineSpawn { AreaId = e.ToAreaId, Timer = 0.8f, Count = -1, Reason = "mine", UseCurrentPosition = true };
                }
                if (IsAuthority && AreaIds.IsMine(e.FromAreaId))
                {
                    // 誰もいなくなった階の生き物は片付ける（日付が変われば鉱石と同じく入れ替わる）
                    bool someone = false;
                    foreach (var p in Game.Players.All) if (p.CurrentAreaId == e.FromAreaId) someone = true;
                    if (!someone)
                    {
                        var rm = new List<string>();
                        foreach (var w in wilds.Values) if (w.areaId == e.FromAreaId) rm.Add(w.wildId);
                        foreach (var id in rm) { wilds.Remove(id); RemoveWildView(id); }
                        if (rm.Count > 0) MarkDirty();
                    }
                }
                RefreshWildViews();
                RefreshOwnedAvatars();
            }
        }

        private void UpdatePendingMineSpawn(float dt)
        {
            if (pendingMineSpawn == null) return;
            pendingMineSpawn.Timer -= dt;
            if (pendingMineSpawn.Timer > 0f) return;
            var pm = pendingMineSpawn;
            pendingMineSpawn = null;
            var local = LocalPlayer;
            if (local == null || local.CurrentAreaId != pm.AreaId || !IsAuthority) return;
            int floor = AreaIds.MineFloorOf(pm.AreaId);
            var rng = SeededRandom.For(Game.Planet.seed, "creatures.mine." + (Clock != null ? Clock.Day : 0) + "." + floor + "." + spawnCounter);
            int count = pm.Count > 0 ? pm.Count : 2 + rng.Range(0, 3);
            Vector3 center = pm.UseCurrentPosition ? local.Position : pm.Center;
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                var sp = pm.OnlySpecies != null ? CreatureDatabase.Get(pm.OnlySpecies) : PickMineSpecies(floor, rng);
                if (sp == null) continue;
                Vector3 pos = center;
                bool found = pm.Reason != "dust" && FindCaveHabitatPoint(floor, center, rng, out pos);
                if (!found && !FindMineFloorPoint(center, rng, pm.Reason == "dust" ? 1.5f : 4f, pm.Reason == "dust" ? 4f : 12f, out pos)) continue;
                AddWild(sp, pos, pm.AreaId, pm.Reason);
                spawned++;
            }
            if (spawned > 0)
            {
                MarkDirty();
                RefreshWildViews();
            }
        }

        private CreatureSpecies PickMineSpecies(int floor, SeededRandom rng)
        {
            var list = new List<CreatureSpecies>();
            var w = new List<float>();
            foreach (var sp in CreatureDatabase.All)
            {
                if (!sp.Underground || floor < sp.MinFloor || floor > sp.MaxFloor) continue;
                float weight = RarityWeight[Mathf.Clamp(sp.Rarity, 0, 5)] * (1f + floor * 0.08f * sp.Rarity);
                if (sp.No == 46) weight *= 0.3f; // ホコリフは主に採掘の後に出る
                list.Add(sp);
                w.Add(weight);
            }
            int idx = rng.WeightedIndex(w);
            return idx >= 0 ? list[idx] : null;
        }

        /// <summary>
        /// Mining が PlanetContext.Shared に入れた洞窟の生息地点（"mining.caveHabitats" と "mining.floorOfPosition"）から、
        /// 今の階のプレイヤー近くの地点を選ぶ。無ければ false（床へのレイキャストにフォールバック）。
        /// </summary>
        private bool FindCaveHabitatPoint(int floor, Vector3 center, SeededRandom rng, out Vector3 pos)
        {
            pos = center;
            var ctx = Game != null ? Game.PlanetContext : null;
            if (ctx == null) return false;
            if (!ctx.Shared.TryGetValue("mining.caveHabitats", out var habObj) || !(habObj is List<HabitatPoint> habs) || habs.Count == 0) return false;
            System.Func<Vector3, int> floorOf = null;
            if (ctx.Shared.TryGetValue("mining.floorOfPosition", out var fo)) floorOf = fo as System.Func<Vector3, int>;
            var near = new List<HabitatPoint>();
            foreach (var h in habs)
            {
                if (h == null) continue;
                if (floorOf != null)
                {
                    int f;
                    try { f = floorOf(h.position); } catch { continue; }
                    if (f != floor) continue;
                }
                float d = (h.position - center).sqrMagnitude;
                if (d < 3f * 3f || d > 26f * 26f) continue;
                if (floorOf == null && Mathf.Abs(h.position.y - center.y) > 3f) continue;
                bool taken = false;
                foreach (var w in wilds.Values) if (w.areaId != AreaIds.Overworld && (w.Position - h.position).sqrMagnitude < 1f) { taken = true; break; }
                if (!taken) near.Add(h);
            }
            if (near.Count == 0) return false;
            pos = near[rng.Range(0, near.Count)].position;
            return true;
        }

        private static bool FindMineFloorPoint(Vector3 center, SeededRandom rng, float minR, float maxR, out Vector3 pos)
        {
            for (int i = 0; i < 14; i++)
            {
                var c = rng.InsideUnitCircle().normalized * rng.Range(minR, maxR);
                var origin = center + new Vector3(c.x, 1.5f, c.y);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - center.y) > 1.6f) continue;
                if (hit.collider != null && hit.collider.GetComponent<CharacterController>() != null) continue;
                // 壁の向こうに出さない
                if (Physics.Linecast(center + Vector3.up * 0.8f, hit.point + Vector3.up * 0.4f, out var block, ~0, QueryTriggerInteraction.Ignore)
                    && block.collider != null && block.collider.GetComponent<CharacterController>() == null) continue;
                pos = hit.point;
                return true;
            }
            pos = center;
            return false;
        }

        private void OnOreMined(OreMinedEvent e)
        {
            var local = LocalPlayer;
            if (local == null || e.PlayerId != local.PlayerId) return;
            // 採掘の音で近くのイワネムが目を覚ます
            foreach (var v in wildViews.Values)
                if (v != null && v.Species.No == 43 && (v.transform.position - local.Position).sqrMagnitude < 7f * 7f) v.Wake();
            if (!IsAuthority || !AreaIds.IsMine(local.CurrentAreaId)) return;
            // 採掘の後、舞い上がった粉からホコリフが現れることがある【仮数値 25%】
            var rng = SeededRandom.For(Game.Planet.seed, "creatures.dust." + (Clock != null ? Clock.TotalMinutes : 0) + "." + spawnCounter);
            if (rng.Chance(0.25f))
            {
                pendingMineSpawn = new PendingMineSpawn { AreaId = local.CurrentAreaId, Timer = 0.3f, Count = 1, Reason = "dust", OnlySpecies = CreatureDatabase.MakeId(46, "hokorifu"), UseCurrentPosition = true };
                spawnCounter++;
            }
        }

        // =====================================================================
        // 野生の見た目
        // =====================================================================

        public void RefreshWildViews()
        {
            if (!sceneBuilt) return;
            EnsureRoots();
            string area = LocalPlayer != null ? LocalPlayer.CurrentAreaId : AreaIds.Overworld;
            var remove = new List<string>();
            foreach (var kv in wildViews)
            {
                if (kv.Value == null || !wilds.TryGetValue(kv.Key, out var w) || w.areaId != area) remove.Add(kv.Key);
            }
            foreach (var id in remove) RemoveWildView(id);
            foreach (var w in wilds.Values)
            {
                if (w.areaId != area || wildViews.ContainsKey(w.wildId)) continue;
                var sp = CreatureDatabase.Get(w.speciesId);
                if (sp == null) continue;
                var view = WildCreature.Create(this, w, sp, wildRoot);
                if (view != null) wildViews[w.wildId] = view;
            }
        }

        public void RemoveWildView(string wildId)
        {
            if (wildId != null && wildViews.TryGetValue(wildId, out var v))
            {
                wildViews.Remove(wildId);
                if (v != null) Object.Destroy(v.gameObject);
            }
        }

        public bool WildExists(string wildId) => wildId != null && wilds.ContainsKey(wildId);

        /// <summary>網の届く範囲で前方にいる野生の個体（最も近いもの）。</summary>
        public WildCreature FindWildInFront(Vector3 origin, Vector3 forward, float reach)
        {
            WildCreature best = null;
            float bestD = float.MaxValue;
            var f = new Vector3(forward.x, 0, forward.z).normalized;
            foreach (var v in wildViews.Values)
            {
                if (v == null || !v.gameObject.activeInHierarchy) continue;
                var d = v.transform.position - origin;
                float vertical = Mathf.Abs(d.y + 0.6f);
                d.y = 0;
                float dist = d.magnitude;
                if (dist > reach || vertical > 2.5f) continue;
                if (dist > 0.6f && Vector3.Angle(f, d) > 55f) continue;
                if (dist < bestD) { bestD = dist; best = v; }
            }
            return best;
        }

        private void OnPlayerGesture(PlayerGestureEvent e)
        {
            var local = LocalPlayer;
            if (local == null || e.PlayerId != local.PlayerId) return;
            foreach (var v in wildViews.Values)
                if (v != null && (v.transform.position - local.Position).sqrMagnitude < 10f * 10f) v.OnPlayerGesture(e.Gesture);
            foreach (var a in avatars.Values)
                if (a != null && (a.transform.position - local.Position).sqrMagnitude < 10f * 10f) a.OnPlayerGesture(e.Gesture);
        }

        // =====================================================================
        // 餌
        // =====================================================================

        /// <summary>持ち物の餌（または作物）を地面に置いて生き物を誘う。</summary>
        public bool PlaceBait(int slot, Vector3 groundPoint)
        {
            var p = LocalPlayer;
            if (p == null) return false;
            var stack = p.Inventory.GetSlot(slot);
            if (stack == null || stack.IsEmpty) return false;
            var def = stack.Definition;
            var taken = p.Inventory.TakeFromSlot(slot, 1, "creature_bait");
            if (taken == null) return false;
            bool tech = HasTechnique(TechBait);
            var go = PrimitiveBuilder.Create(PrimitiveType.Sphere, Game.SceneRoot, groundPoint + Vector3.up * 0.08f, Vector3.one * 0.18f, def.Color, "Bait", false);
            go.transform.position = groundPoint + Vector3.up * 0.08f;
            var b = new BaitSpot
            {
                Go = go,
                Position = groundPoint,
                Def = def,
                Until = UnityEngine.Time.time + BaitSeconds * (tech ? 2f : 1f),
                Radius = BaitRadius * (tech ? 1.5f : 1f),
                AreaId = p.CurrentAreaId
            };
            baits.Add(b);
            UIManager.Toast(Loc.Format("{0}を置いた。少し離れて待ってみよう", "Placed {0}. Step back and wait", def.Name));
            return true;
        }

        public bool LikesBait(CreatureSpecies sp, ItemDefinition def)
        {
            if (sp == null || def == null) return false;
            if (CreatureDatabase.FoodMatches(def, sp.FavoriteFood)) return true;
            if (def.Id == CreatureDatabase.FoodBasic && (sp.Catch == CatchStyle.Bait || sp.Catch == CatchStyle.Lure || sp.Catch == CatchStyle.Normal)) return true;
            return sp.Catch == CatchStyle.Bait && def.Category == ItemCategory.CreatureFood;
        }

        private float baitTick;

        private void UpdateBaits(float dt)
        {
            baitTick -= dt;
            if (baitTick > 0f) return;
            baitTick = 1f;
            var local = LocalPlayer;
            for (int i = baits.Count - 1; i >= 0; i--)
            {
                var b = baits[i];
                if (b.Go == null || UnityEngine.Time.time > b.Until || local == null || local.CurrentAreaId != b.AreaId)
                {
                    if (b.Go != null) Object.Destroy(b.Go);
                    baits.RemoveAt(i);
                    continue;
                }
                if (b.Eater != null) continue;
                WildCreature best = null;
                float bestD = b.Radius * b.Radius;
                foreach (var v in wildViews.Values)
                {
                    if (v == null || !LikesBait(v.Species, b.Def)) continue;
                    float d = (v.transform.position - b.Position).sqrMagnitude;
                    if (d < bestD) { bestD = d; best = v; }
                }
                if (best != null)
                {
                    b.Eater = best;
                    best.AttractToBait(b);
                }
                else if (!b.Summoned && IsAuthority && UnityEngine.Time.time > b.Until - BaitSeconds * 0.6f)
                {
                    // 匂いにつられて、餌の好きな生き物がやって来ることがある【仮数値 35%】
                    b.Summoned = true;
                    var rng = SeededRandom.For(Game.Planet.seed, "creatures.bait." + (Clock != null ? Clock.TotalMinutes : 0) + "." + spawnCounter++);
                    if (rng.Chance(0.35f) && AreaIds.IsSurface(b.AreaId) && Game.Layout != null) SummonForBait(b, rng);
                }
            }
        }

        private void SummonForBait(BaitSpot b, SeededRandom rng)
        {
            var layout = Game.Layout.LayoutAt(b.Position);
            var reg = layout.GetAreaAt(b.Position);
            bool inFarm = layout.FarmRect.width > 0 && layout.FarmRect.Contains(new Vector2(b.Position.x, b.Position.z));
            var area = inFarm ? AreaType.Farm : reg != null ? reg.type : AreaType.Meadow;
            var part = Clock.CurrentDayPart;
            var list = new List<CreatureSpecies>();
            var w = new List<float>();
            foreach (var sp in CreatureDatabase.All)
            {
                if (sp.Underground && !sp.AlsoOverworld) continue;
                if ((sp.Active & CreatureSpecies.ToMask(part)) == 0) continue;
                if (System.Array.IndexOf(sp.Areas, area) < 0) continue;
                if (!LikesBait(sp, b.Def)) continue;
                if (sp.Rarity >= 4) continue;
                list.Add(sp);
                w.Add(RarityWeight[sp.Rarity] * (sp.Catch == CatchStyle.Bait ? 3f : 1f));
            }
            int idx = rng.WeightedIndex(w);
            if (idx < 0) return;
            var dir = rng.InsideUnitCircle().normalized;
            var pos = layout.OnGround(b.Position + new Vector3(dir.x, 0, dir.y) * rng.Range(8f, 11f));
            if (layout.IsWater(pos)) return;
            AddWild(list[idx], pos, b.AreaId, "bait");
            MarkDirty();
            RefreshWildViews();
        }

        public void ConsumeBait(BaitSpot b)
        {
            if (b == null) return;
            if (b.Go != null)
            {
                CreatureFx.Puff(b.Go.transform.position, b.Def != null ? b.Def.Color : Color.white, 4, 0.6f, 0.05f);
                Object.Destroy(b.Go);
            }
            baits.Remove(b);
        }
    }
}
