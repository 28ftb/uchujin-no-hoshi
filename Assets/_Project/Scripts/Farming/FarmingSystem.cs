using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Farming
{
    /// <summary>
    /// 農業・農場システム（農業仕様 v0.2）。
    ///  ・Layout.FarmRect 内の格子（1マス約1m）：クワで耕す → 種を植える → じょうろで水やり（雨・光雨で自動）→ 日ごとに成長 → 収穫（手・カマ）
    ///  ・作物 21種（宇宙作物）・肥料・品質・巨大作物・生物による変異（光る実・虹色の花）。四季なし（星蜜果もいつでも栽培可）
    ///  ・農業用宇宙生物による自動化（ICreatureService 経由。Placement==Farm の個体が住み場所の周りで毎時〜毎日働く）
    ///  ・住み場所の配置、協力作業の発見、収穫かご・倉庫（Carry で運ばれる）・種箱（Sow が使う）
    ///  ・農地拡張・農具強化（鍛冶屋）・農業レベル・農業名人の技・種屋
    ///  ・タブレット「農場」アプリ（区画・生物の作業記録・作物の記録）
    ///  ・マルチ：作業はすべて WorldCommand。他人の作物は収穫者へ自動で移さず、農場の倉庫へ（共有設定で変更可）
    /// </summary>
    public sealed partial class FarmingSystem : GameSystemBase, IFarmService
    {
        public override string SaveKey => "farming";
        public override int DailyUpdateOrder => 200;

        public static FarmingSystem Instance { get; private set; }

        /// <summary>ICreatureService.GetOwned に渡すと星にいる全員分の生物を返す（Creatures 側の約束。コアへの追加要望あり）。</summary>
        public const string AnyOwner = "*";

        // ---------------- 【仮数値】農地・成長 ----------------
        /// <summary>拡張段階ごとの耕せる広さ（マス）。最後は農場の区画全体。</summary>
        public static readonly Vector2Int[] ExpansionSizes = { new Vector2Int(8, 6), new Vector2Int(12, 8), new Vector2Int(16, 10), new Vector2Int(20, 14), new Vector2Int(999, 999) };
        public static readonly int[] ExpansionPrices = { 0, 1500, 4000, 9000, 20000 };
        public static readonly int[] LevelExp = { 0, 0, 50, 150, 300, 500, 800, 1200, 1700, 2300, 3000 };
        public const int MaxFarmLevel = 10;
        public const int BasketSlots = 30;
        public const int WarehouseSlots = 80;
        public const int SeedBoxSlots = 20;
        public const int MaxLogEntries = 80;

        // ---------------- 状態（Planet） ----------------
        private readonly Dictionary<int, FarmPlot> plots = new Dictionary<int, FarmPlot>();
        private readonly List<FarmHabitat> habitats = new List<FarmHabitat>();
        private Inventory basket = new Inventory(BasketSlots, "farm.basket");
        private Inventory warehouse = new Inventory(WarehouseSlots, "farm.warehouse");
        private Inventory seedBox = new Inventory(SeedBoxSlots, "farm.seedbox");
        private readonly List<FarmWorkLog> workLog = new List<FarmWorkLog>();
        private readonly Dictionary<string, CropRecord> records = new Dictionary<string, CropRecord>();
        private readonly List<string> combos = new List<string>();
        public int Expansion { get; private set; }
        public bool ShareHarvest { get; private set; }
        private int habitatCounter;
        private int lastVisitDay = -1;

        // ---------------- 状態（PlayerProfile：ローカルのみ） ----------------
        public int Level { get; private set; } = 1;
        public int Exp { get; private set; }
        public int TotalHarvested { get; private set; }
        private readonly HashSet<string> techniques = new HashSet<string>();

        // ---------------- 格子 ----------------
        private Rect farmRect;
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public Rect FarmRect => farmRect;

        // 天候の記録（「昨日」の天気で成長を補正）
        private bool rainedToday, windToday, fogToday, sunnyToday;

        private FarmToolHandler toolHandler;
        private FarmTabletApp app;
        private FarmResidentProvider residentProvider;
        private bool sceneBuilt;

        public FarmingSystem() { Instance = this; }

        // =====================================================================
        // ライフサイクル
        // =====================================================================

        public override void RegisterContent()
        {
            Instance = this;
            FarmCatalog.RegisterItems();
            RegisterCommands();
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            ClearPlanetState();
            var layout = ctx.Layout;
            farmRect = layout != null ? layout.FarmRect : new Rect(0, 0, 0, 0);
            if (farmRect.width < 4f || farmRect.height < 4f)
            {
                // 生成が農場を用意しなかった場合の保険：出現地点のそばに区画を作る
                var sp = layout != null ? layout.PlayerSpawn : Vector3.zero;
                farmRect = new Rect(sp.x + 6f, sp.z + 4f, 20f, 14f);
            }
            Cols = Mathf.Max(4, Mathf.FloorToInt(farmRect.width));
            Rows = Mathf.Max(4, Mathf.FloorToInt(farmRect.height));
            // ロード時は個人の記録をセーブから復元するので一旦初期化（ゲスト訪問時は自分の技能をそのまま持ち込む）
            if (!ctx.IsNewPlanet && WorldAuthority.Role != NetRole.Guest) ResetProfile();
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            if (ctx.Planet.planetIndex <= 1 && string.IsNullOrEmpty(ctx.Planet.previousWorldId)) ResetProfile();
            // 最初から少しだけ耕してある（プロローグ後すぐ遊べるように）
            var b = UnlockedBounds();
            for (int x = b.xMin + 1; x < b.xMin + 4 && x < b.xMax; x++)
                for (int z = b.yMin + 1; z < b.yMin + 3 && z < b.yMax; z++)
                    GetOrCreatePlot(x, z).tilled = true;
            seedBox.Add(new ItemStack("seed.fuwafuwa_tomato", 3), "farm.start");
        }

        private void ClearPlanetState()
        {
            plots.Clear();
            habitats.Clear();
            basket = new Inventory(BasketSlots, "farm.basket");
            warehouse = new Inventory(WarehouseSlots, "farm.warehouse");
            seedBox = new Inventory(SeedBoxSlots, "farm.seedbox");
            workLog.Clear();
            records.Clear();
            combos.Clear();
            Expansion = 0;
            ShareHarvest = false;
            habitatCounter = 0;
            lastVisitDay = -1;
            pendingVisit = null;
        }

        private void ResetProfile()
        {
            Level = 1;
            Exp = 0;
            TotalHarvested = 0;
            techniques.Clear();
        }

        public override void BuildScene()
        {
            sceneBuilt = true;
            BuildFarmScene();
        }

        public override void OnPlayStarted()
        {
            toolHandler = toolHandler ?? new FarmToolHandler(this);
            ToolUseRouter.Register(toolHandler);
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                app = app ?? new FarmTabletApp(this);
                tablet.RegisterApp(app);
            }
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null)
            {
                residentProvider = residentProvider ?? new FarmResidentProvider(this);
                dir.RegisterInteractionProvider(residentProvider);
            }
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
            RefreshWeatherWatering();
        }

        public override void Teardown()
        {
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            if (toolHandler != null) ToolUseRouter.Unregister(toolHandler);
            DestroyFarmScene();
            sceneBuilt = false;
            ClearPlanetState();
        }

        private float anchorTimer;

        public override void Tick(float deltaTime)
        {
            if (Game == null || Game.State != GameState.Playing) return;
            anchorTimer -= deltaTime;
            if (anchorTimer <= 0f)
            {
                anchorTimer = 8f;
                SendCreatureAnchors();
                UpdateNightGlowVisuals();
            }
        }

        public override void OnHourChanged(int day, int hour)
        {
            TrackWeather();
            if (!IsAuthority) return;
            bool changed = RefreshWeatherWatering();
            changed |= RunCreatureWork(day, hour);
            UpdateFarmVisitors(day, hour);
            if (changed)
            {
                MarkDirty();
                RefreshPlotViews();
            }
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            GrowAll(day);
            RunDailyCreatureWork(day);
            rainedToday = windToday = fogToday = sunnyToday = false;
            TrackWeather();
            RefreshWeatherWatering();
            MarkDirty();
            RefreshPlotViews();
        }

        // =====================================================================
        // IFarmService
        // =====================================================================

        public IEnumerable<string> PlantableCropIds
        {
            get { foreach (var c in FarmCatalog.Crops) yield return c.CropId; }
        }

        public int GetFarmingLevel(string playerId)
        {
            if (LocalPlayer != null && playerId == LocalPlayer.PlayerId) return Level;
            return 1;
        }

        public int PlantedPlotCount
        {
            get
            {
                int n = 0;
                foreach (var p in plots.Values) if (p.HasCrop) n++;
                return n;
            }
        }

        // =====================================================================
        // 格子
        // =====================================================================

        public static int CellKey(int x, int z) => x * 1000 + z;

        public Vector3 CellCenter(int x, int z)
        {
            var p = new Vector3(farmRect.xMin + x + 0.5f, 0, farmRect.yMin + z + 0.5f);
            var layout = Game != null ? Game.Layout : null;
            if (layout != null) p.y = layout.HeightAt(p.x, p.z);
            return p;
        }

        public bool CellAt(Vector3 world, out int x, out int z)
        {
            x = Mathf.FloorToInt(world.x - farmRect.xMin);
            z = Mathf.FloorToInt(world.z - farmRect.yMin);
            return x >= 0 && z >= 0 && x < Cols && z < Rows;
        }

        /// <summary>今耕せる範囲（拡張段階に応じて区画の中央から広がる）。</summary>
        public RectInt UnlockedBounds()
        {
            var s = ExpansionSizes[Mathf.Clamp(Expansion, 0, ExpansionSizes.Length - 1)];
            int w = Mathf.Min(s.x, Cols), h = Mathf.Min(s.y, Rows);
            int x0 = (Cols - w) / 2, z0 = (Rows - h) / 2;
            return new RectInt(x0, z0, w, h);
        }

        public bool InUnlocked(int x, int z)
        {
            var b = UnlockedBounds();
            return x >= b.xMin && x < b.xMax && z >= b.yMin && z < b.yMax;
        }

        public FarmPlot GetPlot(int x, int z)
        {
            plots.TryGetValue(CellKey(x, z), out var p);
            return p;
        }

        private FarmPlot GetOrCreatePlot(int x, int z)
        {
            int k = CellKey(x, z);
            if (!plots.TryGetValue(k, out var p))
            {
                p = new FarmPlot { x = x, z = z };
                plots[k] = p;
            }
            return p;
        }

        private void CleanupPlot(FarmPlot p)
        {
            if (p != null && !p.tilled && !p.HasCrop && !p.wasFarmed && p.soil <= 0.01f) plots.Remove(p.Key);
        }

        public FarmHabitat HabitatAt(int x, int z)
        {
            foreach (var h in habitats) if (h.x == x && h.z == z) return h;
            return null;
        }

        public FarmHabitat GetHabitat(string id)
        {
            foreach (var h in habitats) if (h.id == id) return h;
            return null;
        }

        public IReadOnlyList<FarmHabitat> Habitats => habitats;
        public IEnumerable<FarmPlot> Plots => plots.Values;
        public Inventory Basket => basket;
        public Inventory Warehouse => warehouse;
        public Inventory SeedBox => seedBox;
        public IReadOnlyList<FarmWorkLog> WorkLog => workLog;
        public IEnumerable<CropRecord> Records => records.Values;
        public IReadOnlyList<string> Combos => combos;

        public Inventory GetStorage(string id)
        {
            switch (id)
            {
                case "basket": return basket;
                case "warehouse": return warehouse;
                case "seedbox": return seedBox;
            }
            return null;
        }

        // =====================================================================
        // 成長（日次）
        // =====================================================================

        private void TrackWeather()
        {
            var ws = Game.Service<IWeatherService>();
            if (ws == null) return;
            var w = ws.GetAt(new Vector3(farmRect.center.x, 0, farmRect.center.y));
            if (w.WatersCrops || w.IsRaining) rainedToday = true;
            if (w.wind >= WindLevel.Breeze) windToday = true;
            if (w.IsFoggy) fogToday = true;
            if (!w.IsRaining && w.sky <= SkyState.Sunny) sunnyToday = true;
        }

        private void OnWeatherChanged(WeatherChangedEvent e)
        {
            TrackWeather();
            if (IsAuthority && RefreshWeatherWatering())
            {
                MarkDirty();
                RefreshPlotViews();
            }
        }

        /// <summary>雨・光雨の日は畑が自然に潤う（時間・天候仕様 §7）。</summary>
        private bool RefreshWeatherWatering()
        {
            if (!IsAuthority) return false;
            var ws = Game.Service<IWeatherService>();
            if (ws == null) return false;
            var w = ws.GetAt(new Vector3(farmRect.center.x, 0, farmRect.center.y));
            if (!w.WatersCrops) return false;
            bool changed = false;
            bool light = w.Has(AlienPhenomenon.LightRain);
            foreach (var p in plots.Values)
            {
                if (!p.tilled && !p.HasCrop) continue;
                if (!p.watered) { WaterPlot(p); changed = true; }
                if (light && p.HasCrop && !p.lightRain) { p.lightRain = true; changed = true; }
            }
            return changed;
        }

        private void WaterPlot(FarmPlot p)
        {
            p.watered = true;
            var f = FarmCatalog.Fertilizer(p.fertilizer);
            if (f != null && f.RetainDays > 0) p.retainDays = Mathf.Max(p.retainDays, f.RetainDays);
        }

        private void GrowAll(int day)
        {
            foreach (var p in plots.Values)
            {
                if (!p.HasCrop)
                {
                    p.watered = false;
                    continue;
                }
                var def = FarmCatalog.Get(p.cropKey);
                if (def == null) continue;
                bool wet = p.watered || p.retainDays > 0;
                if (wet)
                {
                    float g = 1f;
                    var f = FarmCatalog.Fertilizer(p.fertilizer);
                    if (f != null) g += f.SpeedBonus * (HasTechnique(TechFertilizer) ? 1.5f : 1f);
                    var likes = def.Likes;
                    if ((likes & CropLikes.Rain) != 0 && rainedToday) g += 0.5f;
                    if ((likes & CropLikes.Wind) != 0 && windToday) g += 0.5f;
                    if ((likes & CropLikes.Fog) != 0 && fogToday) g += 1f;
                    if ((likes & CropLikes.Sunny) != 0 && sunnyToday) g += 0.5f;
                    if ((likes & CropLikes.Glow) != 0 && p.litDay >= day - 1) g += 0.5f;
                    if ((likes & CropLikes.Heat) != 0 && p.heatDay >= day - 1) g += 0.5f;
                    if ((likes & CropLikes.Cool) != 0 && p.coolDay >= day - 1) g += 0.5f;
                    if (p.heatDay >= day - 1) g += 0.15f; // ポカリスの暖かさは作物全般を少し助ける
                    p.growth += g;
                    p.daysWatered++;
                    if (!p.ripe && p.growth >= def.GrowDays) p.ripe = true;
                }
                else p.daysDry++;
                if (!p.watered && p.retainDays > 0) p.retainDays--;
                p.watered = false;
                p.wateredByCreature = false;
            }
        }

        // =====================================================================
        // 収穫（プレイヤー・生物共通）
        // =====================================================================

        public sealed class HarvestOutcome
        {
            public List<ItemStack> Stacks = new List<ItemStack>();
            public int Quality;
            public bool Giant, Glow, RareColor;
            public CropDef Def;
        }

        /// <summary>実った作物を収穫して中身を決める（決定的）。畑の状態も更新する。</summary>
        public HarvestOutcome HarvestPlot(FarmPlot p, string harvesterId, bool byCreature, int harvesterLevel)
        {
            if (p == null || !p.HasCrop || !p.ripe) return null;
            var def = FarmCatalog.Get(p.cropKey);
            if (def == null) return null;
            var rng = SeededRandom.For(Game.Planet.seed, "farm.harvest." + p.x + "." + p.z + "." + p.plantedDay + "." + p.harvests);
            var o = new HarvestOutcome { Def = def };
            int count = rng.Range(def.YieldMin, def.YieldMax + 1);
            var fert = FarmCatalog.Fertilizer(p.fertilizer);
            if (fert != null) count += fert.YieldBonus;
            if (p.pollinated) count += (def.Likes & CropLikes.Pollen) != 0 ? 1 : rng.Chance(0.5f) ? 1 : 0;
            if (harvesterLevel >= 6 && rng.Chance(0.25f)) count++;

            // 品質（農業仕様 §7.2）
            float pts = Mathf.Floor(p.soil);
            if (fert != null) pts += fert.QualityBonus * (HasTechnique(TechFertilizer) && !byCreature ? 1.5f : 1f);
            if (p.daysDry == 0 && p.daysWatered > 0) pts += 1f;
            if (p.waterSoilComboDay >= 0) pts += 1f;
            if (p.lightRain) pts += 1f;
            pts += harvesterLevel / 4;
            pts += rng.Range(0, 2);
            o.Quality = pts >= 6 ? 3 : pts >= 4 ? 2 : pts >= 2 ? 1 : 0;

            string itemId = def.CropId;
            if (def.ColorVariantIds.Length > 0)
            {
                int pick = rng.Range(0, def.ColorVariantIds.Length + 1);
                if (pick > 0) itemId = def.ColorVariantIds[pick - 1];
            }
            if (def.GiantVariantId != null && rng.Chance(0.03f + p.soil * 0.04f + (HasTechnique(TechSpecial) && !byCreature ? 0.06f : 0f)))
            {
                o.Giant = true;
                o.Stacks.Add(new ItemStack(def.GiantVariantId, 1, Mathf.Max(1, o.Quality)) { size = rng.Range(1.8f, 2.6f) });
                count = Mathf.Max(0, count - 1);
            }
            if (def.RareColorVariantId != null && p.pollinated && p.glowNights >= 1 && rng.Chance(0.3f + (HasTechnique(TechSpecial) ? 0.1f : 0f)))
            {
                o.RareColor = true;
                o.Stacks.Add(new ItemStack(def.RareColorVariantId, 1, o.Quality));
                count = Mathf.Max(0, count - 1);
            }
            if (def.GlowVariantId != null && p.glowNights >= 2 && rng.Chance(0.25f + 0.05f * Mathf.Min(6, p.glowNights)))
            {
                o.Glow = true;
                itemId = def.GlowVariantId;
            }
            if (count > 0) o.Stacks.Add(new ItemStack(itemId, count, o.Quality));

            // 畑の状態
            p.harvests++;
            if (def.IsRegrow)
            {
                p.ripe = false;
                p.growth = Mathf.Max(0f, def.GrowDays - def.RegrowDays);
                p.pollinated = false;
                p.glowNights = 0;
            }
            else
            {
                p.cropKey = null;
                p.ripe = false;
                p.growth = 0f;
                p.tilled = false;
                p.wasFarmed = true;
                p.fertilizer = null;
                p.pollinated = false;
                p.glowNights = 0;
                p.daysDry = 0;
                p.daysWatered = 0;
                p.lightRain = false;
                p.waterSoilComboDay = -1;
            }

            // 記録
            if (!records.TryGetValue(def.Key, out var rec)) records[def.Key] = rec = new CropRecord { cropKey = def.Key, firstDay = Clock != null ? Clock.Day : 0 };
            int total = 0;
            foreach (var s in o.Stacks) total += s.count;
            rec.harvested += total;
            rec.bestQuality = Mathf.Max(rec.bestQuality, o.Quality);
            if (o.Giant) rec.giants++;
            if (o.Glow) rec.glowing++;
            if (o.RareColor) rec.rareColors++;
            if (byCreature) rec.byCreatures += total;

            foreach (var s in o.Stacks)
                EventBus.Publish(new CropHarvestedEvent { PlayerId = harvesterId, ItemId = s.itemId, Count = s.count, Quality = s.quality, ByCreature = byCreature });
            return o;
        }

        // =====================================================================
        // 農業レベル・技（ローカルプレイヤーの PlayerProfile）
        // =====================================================================

        public const string TechInsight = "insight";       // 生育状態を見極める
        public const string TechSowLine = "sow_line";      // 連続種まき
        public const string TechWideHarvest = "wide_harvest"; // 広範囲の収穫
        public const string TechFertilizer = "fertilizer"; // 肥料の使い方
        public const string TechSpecial = "special";       // 特別な栽培技術

        public bool HasTechnique(string id) => techniques.Contains(id);
        public void LearnTechnique(string id) { if (!string.IsNullOrEmpty(id)) techniques.Add(id); }

        public void AddExp(int amount)
        {
            if (amount <= 0) return;
            Exp += amount;
            int before = Level;
            while (Level < MaxFarmLevel && Exp >= LevelExp[Level + 1]) Level++;
            if (Level != before) UIManager.Toast(Loc.Format("農業が上達した！（Lv{0}）", "Farming improved! (Lv{0})", Level));
        }

        public int ExpToNext() => Level >= MaxFarmLevel ? 0 : LevelExp[Level + 1] - Exp;

        public void AddHarvestCount(int n) { TotalHarvested += n; }

        // =====================================================================
        // 作業記録
        // =====================================================================

        private void Log(CreatureView c, string action, int count, int day, int hour)
        {
            workLog.Add(new FarmWorkLog
            {
                day = day,
                hour = hour,
                creatureUid = c.Uid,
                speciesId = c.SpeciesId,
                creatureName = c.Nickname,
                action = action,
                count = count
            });
            while (workLog.Count > MaxLogEntries) workLog.RemoveAt(0);
        }

        // =====================================================================
        // セーブ
        // =====================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope == SaveScope.Planet)
            {
                var d = new FarmPlanetSave
                {
                    expansion = Expansion,
                    basket = basket,
                    warehouse = warehouse,
                    seedBox = seedBox,
                    shareHarvest = ShareHarvest,
                    habitatCounter = habitatCounter,
                    lastVisitDay = lastVisitDay
                };
                d.plots.AddRange(plots.Values);
                d.habitats.AddRange(habitats);
                d.log.AddRange(workLog);
                d.records.AddRange(records.Values);
                d.combos.AddRange(combos);
                return JsonUtility.ToJson(d);
            }
            if (scope == SaveScope.PlayerProfile)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                var d = new FarmProfileSave { level = Level, exp = Exp, totalHarvested = TotalHarvested, techniques = new List<string>(techniques) };
                return JsonUtility.ToJson(d);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (scope == SaveScope.Planet)
            {
                var d = JsonUtility.FromJson<FarmPlanetSave>(json);
                if (d == null) return;
                plots.Clear();
                if (d.plots != null) foreach (var p in d.plots) if (p != null) plots[p.Key] = p;
                habitats.Clear();
                if (d.habitats != null) habitats.AddRange(d.habitats);
                basket = RestoreInv(d.basket, BasketSlots, "farm.basket");
                warehouse = RestoreInv(d.warehouse, WarehouseSlots, "farm.warehouse");
                seedBox = RestoreInv(d.seedBox, SeedBoxSlots, "farm.seedbox");
                workLog.Clear();
                if (d.log != null) workLog.AddRange(d.log);
                records.Clear();
                if (d.records != null) foreach (var r in d.records) if (r != null && r.cropKey != null) records[r.cropKey] = r;
                combos.Clear();
                if (d.combos != null) combos.AddRange(d.combos);
                Expansion = Mathf.Clamp(d.expansion, 0, ExpansionSizes.Length - 1);
                ShareHarvest = d.shareHarvest;
                habitatCounter = d.habitatCounter;
                lastVisitDay = d.lastVisitDay;
                if (sceneBuilt) RebuildFarmScene();
            }
            else if (scope == SaveScope.PlayerProfile)
            {
                if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                var d = JsonUtility.FromJson<FarmProfileSave>(json);
                if (d == null) return;
                Level = Mathf.Max(1, d.level);
                Exp = d.exp;
                TotalHarvested = d.totalHarvested;
                techniques.Clear();
                if (d.techniques != null) foreach (var t in d.techniques) techniques.Add(t);
            }
        }

        private static Inventory RestoreInv(Inventory inv, int cap, string owner)
        {
            if (inv == null) return new Inventory(cap, owner);
            inv.ownerId = owner;
            inv.capacity = Mathf.Max(inv.capacity, cap);
            inv.EnsureSlots();
            return inv;
        }
    }
}
