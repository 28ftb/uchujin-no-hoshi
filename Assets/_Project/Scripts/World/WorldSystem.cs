using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// ワールド生成・地形・村・道路・建物・自然・地図（村・マップ・ワールド生成仕様 v0.3）。
    ///  ・PreparePlanet：シードから決定的に WorldLayout を生成（村仕様 §1、§15）。ctx.Layout に設定。
    ///  ・BuildScene：地形・水・道・橋・施設の外観・看板・自然物・街灯・柵・不時着地・鉱山入口を生成。
    ///  ・住民の引っ越しで家の外観を作り直す（村仕様 §5.3、§11）。
    ///  ・生活による変化：踏み跡・公共施設の整備（村仕様 §11）を星の状態として保存。
    ///  ・地図アプリ（AppId "map"、村仕様 §13）：探索範囲・ピン・メモ・看板で見た宇宙語。個人の星ごとの知識として保存。
    /// </summary>
    public sealed class WorldSystem : GameSystemBase
    {
        public override string SaveKey => "world";
        public override int DailyUpdateOrder => 20;

        public WorldLayout Layout { get; private set; }
        public GeneratedWorld Generated { get; private set; }
        public MapKnowledge Knowledge { get; private set; } = new MapKnowledge();
        public Texture2D MapTexture { get; private set; }
        public Texture2D FogTexture { get; private set; }

        private TerrainBuilder terrain;
        private PaletteAtlas atlas;
        private Material natureMat, natureGlowMat;
        private Transform worldRoot, housesRoot, projectsRoot;
        private WearGrid wear = new WearGrid();
        private WorldPlanetState state = new WorldPlanetState();
        private MapApp mapApp;
        private MapWindow mapWindow;
        private readonly Dictionary<int, (GameObject go, string sig)> houses = new Dictionary<int, (GameObject, string)>();
        private readonly Dictionary<string, GameObject> projectObjects = new Dictionary<string, GameObject>();
        private readonly List<(SignInfo info, SignBoard board)> signs = new List<(SignInfo, SignBoard)>();
        private readonly Dictionary<string, string> placeWordCache = new Dictionary<string, string>();
        /// <summary>村の外のマップ（生成バージョン3）のために作ったテクスチャ・マテリアル（Teardown で捨てる）。</summary>
        private readonly List<UnityEngine.Object> fieldOwned = new List<UnityEngine.Object>();
        private bool subscribed;
        private bool appRegistered;
        private bool colorMapDirty;
        private bool fogDirty;
        private float exploreTimer, signTimer, wearTimer, houseTimer, applyTimer;
        private int wearSampleCount;
        private int wearChangesSinceSync;

        // =================================================================
        // ライフサイクル
        // =================================================================

        public override void RegisterContent()
        {
            WorldAuthority.Register<ContributeToProjectCommand>(HandleContribute);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            // 星に保存された生成バージョンで作る（版1の星は版1のまま。村仕様 §1.3：生成済みの星の地形を勝手に変えない）
            int version = Mathf.Clamp(ctx.Planet.worldGenVersion, 1, WorldTuning.GeneratorVersion);
            if (ctx.Planet.worldGenVersion > WorldTuning.GeneratorVersion)
                Debug.LogWarning($"[World] この星の生成バージョン {ctx.Planet.worldGenVersion} は、このゲームの生成コード（{WorldTuning.GeneratorVersion}）より新しいです。版{version}で作ります（地形が違う可能性があります）。");
            GeneratedWorld g = null;
            try { g = WorldGenerator.Generate(ctx.Seed, version); }
            catch (Exception e) { Debug.LogError("[World] 生成に失敗: " + e); }
            if (g == null) g = FallbackWorld.Create(ctx.Seed);
            Generated = g;
            Layout = g.Layout;
            ctx.Layout = Layout;
            ctx.Shared["world.generated"] = g;

            wear = new WearGrid();
            state = NewState();
            Knowledge = new MapKnowledge();
            Knowledge.Clear();
            placeWordCache.Clear();
        }

        private WorldPlanetState NewState()
        {
            var s = new WorldPlanetState { generatorVersion = Generated != null ? Generated.Version : WorldTuning.GeneratorVersion };
            if (Generated != null)
                foreach (var site in Generated.ProjectSites) s.projects.Add(new ProjectProgress { id = site.Id });
            return s;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            state = NewState();
            wear = new WearGrid();
        }

        public override void BuildScene()
        {
            if (Generated == null || Game.SceneRoot == null) return;
            var g = Generated;
            worldRoot = new GameObject("World").transform;
            worldRoot.SetParent(Game.SceneRoot, false);

            terrain = new TerrainBuilder(g);
            terrain.BuildColorMap();
            ApplyAllWear(false);
            terrain.ApplyColorMapChanges();
            terrain.BuildTerrain(worldRoot);
            terrain.BuildOuterRing(worldRoot);
            terrain.BuildWater(worldRoot);
            terrain.BuildWalls(worldRoot);

            atlas = new PaletteAtlas();
            natureMat = WorldMaterials.Textured(atlas.Texture, 0.1f);
            natureGlowMat = WorldMaterials.Textured(atlas.Texture, 0.3f, true, 1.1f);
            NatureBuilder.Build(g, worldRoot, atlas, natureMat, natureGlowMat);

            var structures = new GameObject("Structures").transform;
            structures.SetParent(worldRoot, false);
            foreach (var f in Layout.Facilities)
            {
                if (f.type == FacilityType.ResidentHouse) continue;
                try { StructureBuilder.BuildFacility(f, structures, g.Palette, Game.Planet != null ? Game.Planet.seed : 0UL); }
                catch (Exception e) { Debug.LogError($"[World] {f.id} の外観生成に失敗: {e}"); }
            }
            StructureBuilder.PlazaLandmark(g.PlazaLandmark, structures, g.Palette);
            foreach (var br in g.Field.Bridges) StructureBuilder.Bridge(br, structures, g.Palette);
            if (g.HasPier) StructureBuilder.Pier(g.PierStart, g.PierEnd, structures, g.Palette);
            var home = Layout.GetFacility(FacilityType.PlayerHouse);
            StructureBuilder.FarmFence(g.FarmReserve, home != null ? home.position : Layout.PlayerSpawn, structures, (x, z) => Layout.HeightAt(x, z));

            signs.Clear();
            foreach (var s in g.Signs)
            {
                var board = s.IsSignpost ? StructureBuilder.Signpost(s, structures, PlaceWord) : StructureBuilder.Sign(s, structures, PlaceWord);
                board.onRead = OnSignRead;
                signs.Add((s, board));
            }

            housesRoot = new GameObject("Houses").transform;
            housesRoot.SetParent(worldRoot, false);
            houses.Clear();
            RefreshHouses(true);

            projectsRoot = new GameObject("Projects").transform;
            projectsRoot.SetParent(worldRoot, false);
            RebuildProjects();

            MapTexture = terrain.BuildMapTexture(WorldTuning.MapTextureSize);
            BuildFogTexture();

            RegisterOverworld();
            // 滝の裏の洞（生成バージョン2）
            if (g.Cave != null)
            {
                try
                {
                    WaterfallCaveBuilder.Build(worldRoot, g);
                    WaterfallCaveBuilder.RegisterArea();
                }
                catch (Exception e) { Debug.LogError("[World] 滝の裏の洞の生成に失敗: " + e); }
            }
            // 村の端のゲートと、村の外の地域・道中マップ（生成バージョン3の試作。マップ設計 v0.3）
            if (g.VillageGates.Count > 0 || g.FieldMaps.Count > 0)
            {
                try { FieldMapBuilder.BuildAll(g, worldRoot, atlas, natureMat, natureGlowMat, PlaceWord, fieldOwned); }
                catch (Exception e) { Debug.LogError("[World] 村の外のマップの生成に失敗: " + e); }
            }
        }

        private void RegisterOverworld()
        {
            var home = Layout.GetFacility(FacilityType.PlayerHouse);
            Game.Areas.Register(new AreaDescriptor
            {
                Id = AreaIds.Overworld,
                // 表示名は空にして、HUD には地域名（AreaRegion）を出す
                NameJa = "",
                NameEn = "",
                IsIndoor = false,
                IsUnderground = false,
                ShowWeather = true,
                DefaultSpawn = home != null ? home.entrance + Vector3.up * 0.2f : Layout.OnGround(Layout.PlayerSpawn) + Vector3.up * 0.2f,
                DefaultYaw = home != null ? home.rotationY : 0f
            });
        }

        public override void OnPlayStarted()
        {
            if (!subscribed)
            {
                EventBus.Subscribe<ResidentMovedInEvent>(OnMovedIn);
                EventBus.Subscribe<ResidentMovedOutEvent>(OnMovedOut);
                EventBus.Subscribe<ShopItemSoldEvent>(OnShopSold);
                EventBus.Subscribe<QuestStateChangedEvent>(OnQuestChanged);
                EventBus.Subscribe<GiftGivenEvent>(OnGift);
                EventBus.Subscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
                subscribed = true;
            }
            mapApp = new MapApp(this);
            mapWindow = null;
            // タブレットへの登録は全システムの OnPlayStarted が終わった後（最初の Tick）に行う
            appRegistered = false;
            RefreshHouses(false);
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<ResidentMovedInEvent>(OnMovedIn);
                EventBus.Unsubscribe<ResidentMovedOutEvent>(OnMovedOut);
                EventBus.Unsubscribe<ShopItemSoldEvent>(OnShopSold);
                EventBus.Unsubscribe<QuestStateChangedEvent>(OnQuestChanged);
                EventBus.Unsubscribe<GiftGivenEvent>(OnGift);
                EventBus.Unsubscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
                subscribed = false;
            }
            terrain?.Dispose();
            terrain = null;
            if (MapTexture != null) UnityEngine.Object.Destroy(MapTexture);
            if (FogTexture != null) UnityEngine.Object.Destroy(FogTexture);
            if (natureMat != null) UnityEngine.Object.Destroy(natureMat);
            if (natureGlowMat != null) UnityEngine.Object.Destroy(natureGlowMat);
            if (atlas != null) UnityEngine.Object.Destroy(atlas.Texture);
            foreach (var o in fieldOwned) if (o != null) UnityEngine.Object.Destroy(o);
            fieldOwned.Clear();
            MapTexture = null;
            FogTexture = null;
            atlas = null;
            houses.Clear();
            projectObjects.Clear();
            signs.Clear();
            placeWordCache.Clear();
            worldRoot = null;
            mapApp = null;
            mapWindow = null;
            NightLight.LampsOn = false;
        }

        // =================================================================
        // 毎フレーム
        // =================================================================

        public override void Tick(float dt)
        {
            if (Layout == null || worldRoot == null) return;
            if (!appRegistered && mapApp != null)
            {
                appRegistered = true;
                Game.Service<ITabletService>()?.RegisterApp(mapApp);
            }
            UpdateLamps();
            var player = LocalPlayer;
            bool outside = player != null && player.CurrentAreaId == AreaIds.Overworld && player.Avatar != null;

            exploreTimer += dt;
            if (exploreTimer > 0.4f && outside)
            {
                exploreTimer = 0f;
                Explore(player.Position);
            }
            signTimer += dt;
            if (signTimer > 0.5f && outside)
            {
                signTimer = 0f;
                CheckSignsNearby(player);
            }
            wearTimer += dt;
            if (wearTimer > WorldTuning.WearSampleInterval)
            {
                wearTimer = 0f;
                if (IsAuthority) SampleWear();
            }
            houseTimer += dt;
            if (houseTimer > 2f)
            {
                houseTimer = 0f;
                RefreshHouses(false);
            }
            applyTimer += dt;
            if (applyTimer > 2.5f)
            {
                applyTimer = 0f;
                if (colorMapDirty)
                {
                    terrain?.ApplyColorMapChanges();
                    colorMapDirty = false;
                }
            }
            if (fogDirty && FogTexture != null)
            {
                FogTexture.Apply(false, false);
                fogDirty = false;
            }
            // タブレットが無い場合の予備（M キー）
            if (Game.Service<ITabletService>() == null && Game.State == GameState.Playing && GameInput.Map && !UIManager.AnyModalOpen && !UIManager.TextFieldFocused && mapApp != null)
            {
                if (mapWindow == null) mapWindow = new MapWindow(mapApp);
                UIManager.Toggle(mapWindow);
            }
        }

        /// <summary>夜（と暗い天気）に街灯・窓を灯す（【仮数値】18時〜6時）。</summary>
        private void UpdateLamps()
        {
            var clock = Clock;
            if (clock == null) return;
            int h = clock.Hour;
            bool night = h >= WorldTuning.LampOnHour || h < WorldTuning.LampOffHour;
            var weather = Game.Service<IWeatherService>();
            if (!night && weather != null && weather.Daylight < 0.32f) night = true;
            NightLight.LampsOn = night;
        }

        // =================================================================
        // 宇宙語の地名（看板）
        // =================================================================

        /// <summary>"PLACE:"+facilityId の宇宙語（Language モジュール）。翻訳しない。</summary>
        public string PlaceWord(string facilityId)
        {
            if (string.IsNullOrEmpty(facilityId)) return null;
            if (placeWordCache.TryGetValue(facilityId, out var w)) return w;
            var speech = Game != null ? Game.Service<ISpeechService>() : null;
            if (speech == null) return null;
            try { w = speech.RenderWord(Concepts.Place(facilityId)); }
            catch (Exception e)
            {
                Debug.LogWarning("[World] 地名の生成に失敗: " + e.Message);
                w = null;
            }
            if (!string.IsNullOrEmpty(w)) placeWordCache[facilityId] = w;
            return w;
        }

        private string SignSurface(SignInfo s)
        {
            if (!s.IsSignpost) return PlaceWord(s.FacilityId);
            var parts = new List<string>();
            foreach (var d in s.Destinations)
            {
                var w = PlaceWord(d);
                if (!string.IsNullOrEmpty(w)) parts.Add(w);
            }
            return parts.Count > 0 ? string.Join(" / ", parts) : null;
        }

        private void CheckSignsNearby(PlayerState player)
        {
            var p = player.Position;
            foreach (var (info, board) in signs)
            {
                if ((info.Position - p).sqrMagnitude > WorldTuning.SignReadDistance * WorldTuning.SignReadDistance) continue;
                RecordSign(info, false);
            }
        }

        private SeenPlaceName RecordSign(SignInfo info, bool fromRead)
        {
            string surface = SignSurface(info);
            if (string.IsNullOrEmpty(surface)) return null;
            var seen = Knowledge.FindSeen(info.Id);
            int day = Clock != null ? Clock.Day : 0;
            if (seen == null)
            {
                seen = new SeenPlaceName
                {
                    signId = info.Id,
                    facilityId = info.IsSignpost ? null : info.FacilityId,
                    surface = surface,
                    x = info.Position.x,
                    z = info.Position.z,
                    day = day,
                    timesSeen = 1,
                    signpost = info.IsSignpost
                };
                Knowledge.seen.Add(seen);
                if (!fromRead) UIManager.Toast(Loc.T("看板の文字を地図に書き留めた", "Noted the sign's words on your map"), 2.5f);
            }
            else if (fromRead) seen.timesSeen++;
            return seen;
        }

        private void OnSignRead(SignBoard board, PlayerState player)
        {
            SignInfo info = null;
            foreach (var (i, b) in signs) if (b == board) info = i;
            if (info == null) return;
            var seen = RecordSign(info, true);
            string surface = seen != null ? seen.surface : null;
            if (string.IsNullOrEmpty(surface))
            {
                UIManager.Message(Loc.T("看板", "Sign"), Loc.T("見たことのない文字が書かれている。", "Unfamiliar letters are written here."));
                return;
            }
            // 資料ボックスへ（観察した事実だけを記録。意味は書かない）
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                var mat = new TabletMaterial
                {
                    id = "map.sign." + info.Id,
                    kind = TabletMaterialKind.Sign,
                    titleJa = info.IsSignpost ? "道標の文字" : "看板の文字",
                    titleEn = info.IsSignpost ? "Signpost words" : "Sign words",
                    factJa = $"{(Clock != null ? Clock.Day : 0)}日目に見た{(info.IsSignpost ? "道標（矢印つき）" : "看板")}の文字：「{surface}」",
                    factEn = $"Words on a {(info.IsSignpost ? "signpost (with arrows)" : "sign")} seen on day {(Clock != null ? Clock.Day : 0)}: \"{surface}\"",
                    day = Clock != null ? Clock.Day : 0,
                    minuteOfDay = Clock != null ? Clock.MinuteOfDay : 0
                };
                mat.tags.Add("sign");
                try { tablet.AddMaterial(mat); }
                catch (Exception e) { Debug.LogWarning(e.Message); }
            }
            UIManager.Message(info.IsSignpost ? Loc.T("道標", "Signpost") : Loc.T("看板", "Sign"),
                "「" + surface + "」\n\n" + Loc.T("（地図アプリに書き留めた）", "(Noted in your map app)"));
        }

        // =================================================================
        // 地図の探索
        // =================================================================

        private void BuildFogTexture()
        {
            int g = WorldTuning.ExploreGrid;
            FogTexture = new Texture2D(g, g, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "MapFog"
            };
            var px = new Color32[g * g];
            for (int i = 0; i < px.Length; i++) px[i] = Knowledge.IsExplored(i) ? new Color32(20, 24, 40, 0) : new Color32(20, 24, 40, 255);
            FogTexture.SetPixels32(px);
            FogTexture.Apply(false, false);
            fogDirty = false;
        }

        private void Explore(Vector3 pos)
        {
            int g = WorldTuning.ExploreGrid;
            float cell = WorldTuning.WorldSize / g;
            float r = WorldTuning.ExploreRevealRadius;
            int cr = Mathf.CeilToInt(r / cell);
            int c = Knowledge.CellOf(pos.x, pos.z);
            if (c < 0) return;
            int cx = c % g, cz = c / g;
            bool changed = false;
            for (int dz = -cr; dz <= cr; dz++)
                for (int dx = -cr; dx <= cr; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (x < 0 || z < 0 || x >= g || z >= g) continue;
                    if ((dx * dx + dz * dz) * cell * cell > r * r) continue;
                    int idx = z * g + x;
                    if (Knowledge.SetExplored(idx))
                    {
                        changed = true;
                        FogTexture?.SetPixel(x, z, new Color32(20, 24, 40, 0));
                    }
                }
            if (!changed) return;
            fogDirty = true;
            foreach (var f in Layout.Facilities)
            {
                if (Knowledge.IsDiscovered(f.id)) continue;
                if (Knowledge.IsExploredAt(f.position.x, f.position.z)) Knowledge.Discover(f.id);
            }
        }

        // =================================================================
        // 住宅（引っ越しで外観が変わる）
        // =================================================================

        private void OnMovedIn(ResidentMovedInEvent e) => RefreshHouses(false);
        private void OnMovedOut(ResidentMovedOutEvent e) => RefreshHouses(false);

        private void RefreshHouses(bool force)
        {
            if (housesRoot == null || Layout == null) return;
            var dir = Game.Service<IResidentDirectory>();
            foreach (var plot in Layout.HousePlots)
            {
                var f = Layout.GetFacilityById(plot.facilityId);
                if (f == null) continue;
                string owner = f.ownerResidentId;
                IResidentInfo info = null;
                bool packing = false;
                if (!string.IsNullOrEmpty(owner) && dir != null)
                {
                    try { info = dir.Get(owner); }
                    catch (Exception) { info = null; }
                    if (info != null)
                    {
                        if (!info.IsActive) owner = null;
                        else packing = info.Activity == ResidentActivity.Packing;
                    }
                }
                if (!plot.occupied && string.IsNullOrEmpty(f.ownerResidentId)) owner = null;
                string sig = (owner ?? "") + (packing ? "#packing" : "") + (info != null ? "#i" : "");
                if (!force && houses.TryGetValue(plot.index, out var cur) && cur.sig == sig && cur.go != null) continue;
                if (houses.TryGetValue(plot.index, out var old) && old.go != null)
                {
                    WorldLabels.Remove(old.go.transform);
                    foreach (var t in old.go.GetComponentsInChildren<Transform>()) WorldLabels.Remove(t);
                    UnityEngine.Object.Destroy(old.go);
                }
                GameObject go = null;
                try { go = HouseBuilder.Build(plot, f, info, owner, packing, Generated.Palette, housesRoot); }
                catch (Exception ex) { Debug.LogError($"[World] 家 {plot.index} の外観生成に失敗: {ex}"); }
                houses[plot.index] = (go, sig);
            }
        }

        // =================================================================
        // 生活による変化：踏み跡（村仕様 §11.1 日常の痕跡）
        // =================================================================

        private void SampleWear()
        {
            if (terrain == null || Game == null) return;
            wearSampleCount++;
            foreach (var p in Game.Players.All)
            {
                if (p.Avatar == null || p.CurrentAreaId != AreaIds.Overworld) continue;
                StepWear(p.Position, WorldTuning.WearPerPlayerSample);
            }
            if (wearSampleCount % 3 == 0)
            {
                var dir = Game.Service<IResidentDirectory>();
                if (dir != null)
                {
                    try
                    {
                        foreach (var r in dir.ActiveResidents) StepWear(r.Position, WorldTuning.WearPerResidentSample);
                    }
                    catch (Exception) { }
                }
            }
            if (wearChangesSinceSync > 40)
            {
                wearChangesSinceSync = 0;
                MarkDirty();
            }
        }

        private void StepWear(Vector3 pos, int amount)
        {
            if (Mathf.Abs(pos.x) > WorldTuning.Half || Mathf.Abs(pos.z) > WorldTuning.Half) return;
            if (Mathf.Abs(pos.y - Layout.HeightAt(pos)) > 3f) return; // 屋内・地下（離れた座標）や空中は除く
            int idx = wear.IndexOf(pos);
            if (idx < 0) return;
            if (wear.Step(idx, amount))
            {
                terrain.ApplyWearCell(idx % wear.G, idx / wear.G, wear.Cells[idx]);
                colorMapDirty = true;
                wearChangesSinceSync++;
            }
        }

        private void ApplyAllWear(bool resetFirst)
        {
            if (terrain == null) return;
            for (int i = 0; i < wear.Cells.Length; i++)
            {
                int v = wear.Cells[i];
                if (v == 0 && !resetFirst) continue;
                terrain.ApplyWearCell(i % wear.G, i / wear.G, v);
            }
        }

        // =================================================================
        // 生活による変化：公共施設の整備（村仕様 §11.2【提案】）
        // =================================================================

        private ProjectProgress ActiveProject()
        {
            foreach (var p in state.projects) if (!p.completed) return p;
            return null;
        }

        private ProjectSite SiteOf(string id)
        {
            if (Generated == null) return null;
            foreach (var s in Generated.ProjectSites) if (s.Id == id) return s;
            return null;
        }

        private void AddVitality(int amount)
        {
            if (!IsAuthority) return;
            state.pendingVitality = Mathf.Min(40, state.pendingVitality + amount);
        }

        private void OnShopSold(ShopItemSoldEvent e) => AddVitality(1);
        private void OnGift(GiftGivenEvent e) => AddVitality(1);
        private void OnRocketRepaired(RocketDeviceRepairedEvent e) => AddVitality(3);
        private void OnQuestChanged(QuestStateChangedEvent e)
        {
            if (e.Status == QuestStatus.Completed) AddVitality(4);
        }

        private void RebuildProjects()
        {
            if (projectsRoot == null || Generated == null) return;
            foreach (var kv in projectObjects) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            projectObjects.Clear();
            var active = ActiveProject();
            foreach (var p in state.projects)
            {
                var site = SiteOf(p.id);
                if (site == null) continue;
                if (p.completed)
                {
                    var holder = new GameObject("Done_" + p.id).transform;
                    holder.SetParent(projectsRoot, false);
                    try { ProjectBuilder.BuildCompleted(site, holder, Generated); }
                    catch (Exception e) { Debug.LogError($"[World] 整備 {p.id} の表示に失敗: {e}"); }
                    projectObjects[p.id] = holder.gameObject;
                }
                else if (p == active)
                {
                    var holder = new GameObject("Marker_" + p.id).transform;
                    holder.SetParent(projectsRoot, false);
                    var box = ProjectBuilder.BuildMarker(site, holder, p.progress / (float)Mathf.Max(1, site.Required));
                    var marker = box.AddComponent<ProjectMarker>();
                    marker.projectId = p.id;
                    marker.onInteract = OnMarkerInteract;
                    projectObjects[p.id] = holder.gameObject;
                }
            }
        }

        private void OnMarkerInteract(ProjectMarker marker, PlayerState player)
        {
            var p = state.projects.Find(x => x.id == marker.projectId);
            var site = SiteOf(marker.projectId);
            if (p == null || site == null) return;
            int pct = Mathf.Clamp(Mathf.RoundToInt(p.progress * 100f / Mathf.Max(1, site.Required)), 0, 100);
            string msg = Loc.Format("村の人たちが何かを作っているようだ。（{0}）\n進み具合：{1}%\n\n材料やお金を置いていくと、早く完成するかもしれない。",
                "The villagers seem to be building something. ({0})\nProgress: {1}%\n\nLeaving materials or money may speed it up.", Loc.T(site.NameJa, site.NameEn), pct);
            var opts = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("材料を置いていく", "Leave materials"), () =>
                {
                    UIManager.PickItem(player, Loc.T("置いていく物を選ぶ", "Choose what to leave"), s =>
                    {
                        if (s == null || s.IsEmpty) return false;
                        var d = s.Definition;
                        return d != null && d.Category != ItemCategory.KeyItem && d.Category != ItemCategory.Letter && d.Category != ItemCategory.RocketPart;
                    }, (slot, stack) =>
                    {
                        if (stack == null) return;
                        WorldAuthority.Submit(new ContributeToProjectCommand { projectId = p.id, itemId = stack.itemId, count = 1 });
                    });
                }),
                new ChoiceOption(Loc.T("お金を置いていく（100G）", "Leave money (100G)"), () =>
                    WorldAuthority.Submit(new ContributeToProjectCommand { projectId = p.id, money = 100 }), player.Wallet.money >= 100),
                new ChoiceOption(Loc.T("見るだけ", "Just look"), null)
            };
            UIManager.Choose(Loc.T("工事中の場所", "Work site"), msg, opts);
        }

        // 【仮数値】寄付の換算
        private const int ItemPointsDivisor = 15;
        private const int MoneyPointsDivisor = 20;

        private CommandResult HandleContribute(ContributeToProjectCommand cmd, CommandContext ctx)
        {
            var p = state.projects.Find(x => x.id == cmd.projectId);
            var site = SiteOf(cmd.projectId);
            if (p == null || site == null || p.completed || p != ActiveProject())
                return CommandResult.Fail("今は受け付けていないようだ", "Not accepting right now");
            int points = 0;
            if (!string.IsNullOrEmpty(cmd.itemId) && cmd.count > 0)
            {
                if (!ctx.HasItem(cmd.itemId, cmd.count) || !ctx.TakeItem(cmd.itemId, cmd.count, "world.project"))
                    return CommandResult.Fail("持ち物が足りません", "You don't have that");
                var def = ItemRegistry.Get(cmd.itemId);
                points += Mathf.Max(1, (def != null ? def.BasePrice : 10) / ItemPointsDivisor) * cmd.count;
            }
            if (cmd.money > 0)
            {
                if (!ctx.TryCharge(cmd.money, "world.project"))
                    return CommandResult.Fail("お金が足りません", "Not enough money");
                points += Mathf.Max(1, cmd.money / MoneyPointsDivisor);
            }
            if (points <= 0) return CommandResult.Fail("何も置かなかった", "Nothing was left");
            p.progress += points;
            bool done = TryComplete(p, site);
            MarkDirty();
            RebuildProjects();
            if (done) ctx.Notify($"{site.NameJa}ができあがった！", $"The {site.NameEn} is finished!");
            else ctx.Notify("材料を置いていった。村の人が使ってくれそうだ。", "You left it at the work site.");
            return CommandResult.Ok();
        }

        private bool TryComplete(ProjectProgress p, ProjectSite site)
        {
            if (p.progress < site.Required) return false;
            p.completed = true;
            p.completedDay = Clock != null ? Clock.Day : 0;
            return true;
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority || Layout == null) return;
            // 草が少しずつ戻る
            for (int i = 0; i < wear.Cells.Length; i++)
            {
                int v = wear.Cells[i];
                if (v == 0) continue;
                int nv = Mathf.Max(0, v - WorldTuning.WearDailyRecovery);
                if (WearGrid.VisualLevel(v) != WearGrid.VisualLevel(nv) && terrain != null)
                {
                    terrain.ApplyWearCell(i % wear.G, i / wear.G, nv);
                    colorMapDirty = true;
                }
                wear.Cells[i] = (byte)nv;
            }
            // 公共施設の整備は住民自身も少しずつ進める
            var p = ActiveProject();
            if (p != null)
            {
                var site = SiteOf(p.id);
                p.progress += WorldTuning.ProjectDailyProgress + Mathf.Min(20, state.pendingVitality);
                state.pendingVitality = 0;
                if (site != null && TryComplete(p, site))
                    UIManager.Toast(Loc.Format("村に{0}ができたようだ", "It seems the village now has a {0}", Loc.T(site.NameJa, site.NameEn)), 5f);
                RebuildProjects();
            }
            MarkDirty();
        }

        // =================================================================
        // セーブ
        // =================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope == SaveScope.Planet)
            {
                state.generatorVersion = Generated != null ? Generated.Version : WorldTuning.GeneratorVersion;
                state.wear = wear.Pack();
                return JsonUtility.ToJson(state);
            }
            if (scope == SaveScope.PlayerPlanetKnowledge)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                return JsonUtility.ToJson(Knowledge);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (scope == SaveScope.Planet)
            {
                WorldPlanetState s = null;
                try { s = JsonUtility.FromJson<WorldPlanetState>(json); }
                catch (Exception e) { Debug.LogWarning("[World] 状態の読み込みに失敗: " + e.Message); }
                if (s == null) return;
                // 生成済みの整備予定地と照合（足りない分は追加）
                var merged = NewState();
                foreach (var mp in merged.projects)
                {
                    var saved = s.projects != null ? s.projects.Find(x => x != null && x.id == mp.id) : null;
                    if (saved == null) continue;
                    mp.progress = saved.progress;
                    mp.completed = saved.completed;
                    mp.completedDay = saved.completedDay;
                }
                merged.pendingVitality = s.pendingVitality;
                state = merged;
                wear.Unpack(s.wear);
                // ゲストはプレイ中にも呼ばれる：表示まで冪等に作り直す
                if (terrain != null)
                {
                    ApplyAllWear(true);
                    terrain.ApplyColorMapChanges();
                    colorMapDirty = false;
                }
                if (projectsRoot != null) RebuildProjects();
            }
            else if (scope == SaveScope.PlayerPlanetKnowledge)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                MapKnowledge k = null;
                try { k = JsonUtility.FromJson<MapKnowledge>(json); }
                catch (Exception e) { Debug.LogWarning("[World] 地図の記録の読み込みに失敗: " + e.Message); }
                if (k == null) return;
                k.AfterLoad();
                Knowledge = k;
                if (FogTexture != null)
                {
                    UnityEngine.Object.Destroy(FogTexture);
                    BuildFogTexture();
                }
            }
        }
    }
}
