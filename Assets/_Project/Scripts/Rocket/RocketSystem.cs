using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Rocket
{
    /// <summary>
    /// ロケット修理・診断・最終起動（ロケット修理仕様 v0.2）。
    /// ・不時着したロケットと修理作業場の3D表示（§11・§12）
    /// ・修理装置 R01〜R08 の状態・納品・取り付け・試運転・最終起動（§3・§10・§18）
    /// ・修理屋（四本腕）の図・部品販売・作業依頼（§8・§9.3・§14、メインストーリー第1章）
    /// ・タブレットの宇宙船診断アプリ（§13）
    /// ・帰還の選択とクリア後（基本決定 §5）
    /// 星の状態はホスト権威（WorldCommand）で変更し、誰が貢献したかを記録する（§17）。
    /// </summary>
    public sealed class RocketSystem : GameSystemBase, IRocketService
    {
        public override string SaveKey => "rocket";
        public override int DailyUpdateOrder => 490;

        /// <summary>モジュール内のUIから参照する（他モジュールは IRocketService を使う）。</summary>
        internal static RocketSystem Instance { get; private set; }

        private RocketPlanetSave state = new RocketPlanetSave();
        private RocketKnowledgeSave knowledge = new RocketKnowledgeSave();
        private Vector3 sitePosition;
        private float siteYaw;
        private ulong planetSeed;
        private RocketShipView view;
        private GameObject counterRoot;
        private RocketDiagnosticsApp app;
        private MechanicInteractionProvider provider;
        private ShipDisplayOverlay displayOverlay;
        private HashSet<string> trackedItems;
        private bool sceneBuilt;
        private bool cutscenePlaying;

        // 見物に来た住民（ホストのみ）
        private sealed class WatchVisit
        {
            public string residentId;
            public long untilTotalMinutes;
            public bool spoke;
            public bool isMechanic;
        }
        private readonly List<WatchVisit> watchers = new List<WatchVisit>();
        private float watcherCheckTimer;

        // 修理屋に初めて話しかけた時に図を開く（第1章）
        private string pendingDiagramResident;
        private float pendingDiagramAt = -1f;

        internal RocketPlanetSave State => state;
        internal RocketKnowledgeSave Knowledge => knowledge;
        internal RocketShipView View => view;

        // ============================================================
        // IRocketService
        // ============================================================

        public int RepairedDeviceCount
        {
            get
            {
                int n = 0;
                foreach (var def in RocketTables.Devices)
                {
                    if (def.IsFinal) { if (state.launchReady) n++; }
                    else if (state.Device(def.Id)?.repaired == true) n++;
                }
                return n;
            }
        }

        public int TotalDeviceCount => RocketTables.Devices.Count;
        public bool IsLaunchReady => state.launchReady;
        public bool HasLaunched => state.hasLaunched;
        public float RepairProgress01 => TotalDeviceCount > 0 ? RepairedDeviceCount / (float)TotalDeviceCount : 0f;
        public Vector3 RocketPosition => sitePosition;

        public bool IsDeviceRepaired(string deviceId)
        {
            var def = RocketTables.Get(deviceId);
            if (def == null) return false;
            if (def.IsFinal) return state.launchReady;
            return state.Device(deviceId)?.repaired == true;
        }

        // ============================================================
        // ライフサイクル
        // ============================================================

        public override void RegisterContent()
        {
            Instance = this;
            // 購入部品（登録担当：Rocket）
            foreach (var p in RocketTables.PurchasedParts)
            {
                ItemRegistry.Register(new ItemDefinition
                {
                    Id = p.ItemId,
                    NameJa = p.NameJa,
                    NameEn = p.NameEn,
                    DescriptionJa = p.DescJa,
                    DescriptionEn = p.DescEn,
                    Category = ItemCategory.RocketPart,
                    BasePrice = p.Price,
                    MaxStack = 20,
                    Rarity = 2,
                    Durable = true,
                    Color = p.Color,
                    Shape = p.Shape,
                    GenericConceptId = Concepts.PART,
                    SourceActivity = "rocket"
                }.WithTags(ItemTags.Precision, ItemTags.RocketMaterial, ItemTags.NotSellable));
            }

            WorldAuthority.Register<RocketInspectCommand>(HandleInspect);
            WorldAuthority.Register<RocketDeliverCommand>(HandleDeliver);
            WorldAuthority.Register<RocketPayCommand>(HandlePay);
            WorldAuthority.Register<RocketAssignCreatureCommand>(HandleAssignCreature);
            WorldAuthority.Register<RocketInstallCommand>(HandleInstall);
            WorldAuthority.Register<RocketTestCommand>(HandleTest);
            WorldAuthority.Register<RocketFinalTestCommand>(HandleFinalTest);
            WorldAuthority.Register<RocketBeginReturnCommand>(HandleBeginReturn);
            WorldAuthority.Register<RocketOrderCommand>(HandleOrder);
            WorldAuthority.Register<RocketCollectOrdersCommand>(HandleCollectOrders);
            WorldAuthority.Register<RocketMechanicIncomeCommand>(HandleMechanicIncome);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            Instance = this;
            planetSeed = ctx.Seed;
            state = new RocketPlanetSave();
            state.EnsureDevices();
            knowledge = new RocketKnowledgeSave();
            watchers.Clear();

            // 不時着地（ワールドシードで場所が変わる §15）
            var layout = ctx.Layout;
            var p = layout != null ? layout.CrashSite : new Vector3(25f, 0f, 25f);
            sitePosition = layout != null ? layout.OnGround(p) : p;
            var rng = ctx.Rng("rocket.site");
            // 村（原点付近）の方を向けてハッチが見えるようにし、少しだけ揺らぎを入れる
            var toVillage = (layout != null ? layout.PlayerSpawn : Vector3.zero) - sitePosition;
            toVillage.y = 0f;
            float baseYaw = toVillage.sqrMagnitude > 0.01f ? Mathf.Atan2(toVillage.x, toVillage.z) * Mathf.Rad2Deg : 0f;
            siteYaw = baseYaw + rng.Range(-25f, 25f);
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            state = new RocketPlanetSave();
            state.EnsureDevices();
        }

        public override void BuildScene()
        {
            var root = Game.Instance != null ? Game.Instance.SceneRoot : null;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            Func<float, float, float> h = layout != null ? (Func<float, float, float>)((x, z) => layout.HeightAt(x, z)) : ((x, z) => sitePosition.y);
            view = RocketShipView.Build(root, sitePosition, siteYaw, h, planetSeed);
            AttachInteractables();
            BuildMechanicCounter(root, layout);
            WorldLabels.Add(view.ShipRoot, () => state.launchReady ? Loc.T("宇宙船（帰還可能）", "Spaceship (ready)") : Loc.T("宇宙船", "Spaceship"),
                new Vector3(0f, 10.6f, 0f), new Color(0.75f, 0.95f, 1f), 40f);
            sceneBuilt = true;
            RefreshVisuals();
        }

        public override void OnPlayStarted()
        {
            Instance = this;
            EventBus.Subscribe<ItemAddedEvent>(OnItemAdded);
            EventBus.Subscribe<ResidentTalkedEvent>(OnResidentTalked);
            EventBus.Subscribe<EndingFinishedEvent>(OnEndingFinished);
            EventBus.Subscribe<ResidentMovedOutEvent>(OnResidentMovedOut);

            provider = new MechanicInteractionProvider(this);
            Game.Service<IResidentDirectory>()?.RegisterInteractionProvider(provider);

            app = new RocketDiagnosticsApp(this);
            Game.Service<ITabletService>()?.RegisterApp(app);

            displayOverlay = new ShipDisplayOverlay();
            UIManager.RegisterOverlay(displayOverlay);

            ScanInventoryForDiscoveries();

            // 「出発中」のまま保存された星（次の星へ進む直前のセーブ・エンディング途中のセーブ）を読み込んだ場合は、
            // エンディングはもう進行していないので、出発直前（帰還可能）の状態に戻す。戻さないと宇宙船を調べられず、二度と帰還できない。
            if (IsAuthority && state.hasLaunched)
            {
                state.hasLaunched = false;
                view?.Land();
                MarkDirty();
            }
            RefreshVisuals();
        }

        public override void Teardown()
        {
            EventBus.Unsubscribe<ItemAddedEvent>(OnItemAdded);
            EventBus.Unsubscribe<ResidentTalkedEvent>(OnResidentTalked);
            EventBus.Unsubscribe<EndingFinishedEvent>(OnEndingFinished);
            EventBus.Unsubscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
            if (displayOverlay != null) UIManager.UnregisterOverlay(displayOverlay);
            displayOverlay = null;
            if (view != null && view.ShipRoot != null) WorldLabels.Remove(view.ShipRoot);
            if (Game.Instance != null && Game.Instance.LocalPlayer != null) Game.Instance.LocalPlayer.UnlockInput("rocket.cutscene");
            view = null;
            counterRoot = null;
            app = null;
            provider = null;
            sceneBuilt = false;
            cutscenePlaying = false;
            watchers.Clear();
            pendingDiagramResident = null;
            pendingDiagramAt = -1f;
            state = new RocketPlanetSave();
            state.EnsureDevices();
            knowledge = new RocketKnowledgeSave();
        }

        // ============================================================
        // セーブ（Planet：修理状態／Knowledge：個人の発見）
        // ============================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(state);
                case SaveScope.PlayerPlanetKnowledge:
                    if (Game.Instance != null && playerId == Game.Instance.LocalPlayerId) return JsonUtility.ToJson(knowledge);
                    return null;
                default:
                    return null;
            }
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (scope == SaveScope.Planet)
            {
                RocketPlanetSave loaded = null;
                try { loaded = JsonUtility.FromJson<RocketPlanetSave>(json); }
                catch (Exception e) { Debug.LogException(e); }
                if (loaded == null) return;
                loaded.EnsureDevices();
                var old = state;
                state = loaded;
                // ゲスト：ホストで起きた修理・試運転の演出を手元でも再生する（状態は冪等に反映）
                if (sceneBuilt && view != null && old != null) PlayRemoteDiffEffects(old, state);
                RefreshVisuals();
            }
            else if (scope == SaveScope.PlayerPlanetKnowledge)
            {
                if (Game.Instance != null && playerId != Game.Instance.LocalPlayerId) return;
                try
                {
                    var k = JsonUtility.FromJson<RocketKnowledgeSave>(json);
                    if (k != null)
                    {
                        if (k.discovered == null) k.discovered = new List<string>();
                        knowledge = k;
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void PlayRemoteDiffEffects(RocketPlanetSave before, RocketPlanetSave after)
        {
            if (IsAuthority) return;
            foreach (var def in RocketTables.Devices)
            {
                if (def.IsFinal) continue;
                var a = before.Device(def.Id);
                var b = after.Device(def.Id);
                if (b != null && b.repaired && (a == null || !a.repaired)) view.PlayRepairBurst(def.Index, def.DiagramColor);
            }
            if (after.testSerial != before.testSerial)
            {
                var d = RocketTables.Get(after.lastTestDevice);
                if (d != null) view.PlayTest(d.Index);
            }
            if (after.finalSerial != before.finalSerial && after.launchReady) StartFinalSequenceLocal(null);
        }

        // ============================================================
        // 毎フレーム・時間
        // ============================================================

        public override void Tick(float deltaTime)
        {
            if (pendingDiagramAt >= 0f && UnityEngine.Time.unscaledTime >= pendingDiagramAt)
            {
                pendingDiagramAt = -1f;
                var rid = pendingDiagramResident;
                pendingDiagramResident = null;
                OpenDiagram(rid);
            }

            if (!IsAuthority || !sceneBuilt) return;
            // 帰還（エンディング）中は見物の住民の独り言を出さない（見送りの演出に割り込まないように）
            if (state.hasLaunched) return;
            watcherCheckTimer -= deltaTime;
            if (watcherCheckTimer > 0f) return;
            watcherCheckTimer = 2f;
            UpdateWatchers();
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (!IsAuthority || !sceneBuilt || state.hasLaunched) return;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || Clock == null) return;

            // 修理屋が普段から作業場へ通う（§11.2）
            if (Array.IndexOf(RocketTables.MechanicWorkHours, hour) >= 0)
            {
                var mech = FindMechanic();
                if (mech != null && mech.IsActive && mech.Activity != ResidentActivity.Sleeping && mech.Activity != ResidentActivity.Packing)
                {
                    var pos = view != null ? view.SitePoint(new Vector3(4.4f, 0f, 3.6f)) : sitePosition;
                    int until = Mathf.Min(23 * 60 + 50, Clock.MinuteOfDay + RocketTables.MechanicWorkMinutes);
                    dir.RequestGoTo(mech.Id, pos, until, "rocket.work");
                    AddWatcher(mech.Id, RocketTables.MechanicWorkMinutes, true);
                }
            }

            // 見物に来る住民（§14.2：普段から修理地点を見に来る宇宙人がいる）
            if (hour >= 10 && hour <= 18)
            {
                var rng = SeededRandom.For(planetSeed, "rocket.watch." + day + "." + hour);
                float chance = RocketTables.WatcherBaseChance + RocketTables.WatcherChancePerRepair * CountRepaired();
                if (!rng.Chance(chance)) return;
                var candidates = new List<IResidentInfo>();
                foreach (var r in dir.ActiveResidents)
                {
                    if (r.Activity == ResidentActivity.Sleeping || r.Activity == ResidentActivity.Packing || r.Activity == ResidentActivity.Working) continue;
                    if (r.Job == ResidentJob.Mechanic) continue;
                    candidates.Add(r);
                }
                if (candidates.Count == 0) return;
                var who = rng.WeightedPick(candidates, WatchInterest);
                if (who == null) return;
                var spot = SpectatorSpot(rng.Range(0, 7), 7);
                int until2 = Mathf.Min(23 * 60 + 50, Clock.MinuteOfDay + RocketTables.WatcherMinutes);
                dir.RequestGoTo(who.Id, spot, until2, "rocket.watch");
                AddWatcher(who.Id, RocketTables.WatcherMinutes, false);
            }
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            // 前日の見物の記録を片付ける（加工依頼は readyDay と日付の比較で受け取れるようになる）
            watchers.Clear();
            if (Game.Instance != null && Game.Instance.LocalPlayer != null && HasReadyOrders(Game.Instance.LocalPlayerId))
                UIManager.Toast(Loc.T("修理屋に頼んだ物が出来上がっている（部品置き場・修理屋で受け取れる）", "Your order from the mechanic is ready (collect at the parts storage or the mechanic)"), 5f);
        }

        /// <summary>住民ごとの「宇宙船への興味」（職業による違い §12）。</summary>
        private float WatchInterest(IResidentInfo r)
        {
            switch (r.Job)
            {
                case ResidentJob.Traveler: return IsDeviceRepaired("R06") ? 4f : 2f; // 旅に憧れる住民
                case ResidentJob.Builder:
                case ResidentJob.Blacksmith:
                case ResidentJob.Scholar: return 2.5f; // 機械好き
                case ResidentJob.Child: return IsDeviceRepaired("R05") ? 3f : 1.5f;
                case ResidentJob.FishingMaster: return IsDeviceRepaired("R03") ? 2f : 1f;
                case ResidentJob.FarmingMaster: return IsDeviceRepaired("R04") ? 2f : 1f;
                case ResidentJob.CreatureExpert: return IsDeviceRepaired("R07") ? 2.5f : 1f;
                default: return 1f;
            }
        }

        private void AddWatcher(string residentId, int minutes, bool isMechanic)
        {
            if (Clock == null || string.IsNullOrEmpty(residentId)) return;
            watchers.RemoveAll(w => w.residentId == residentId);
            watchers.Add(new WatchVisit { residentId = residentId, untilTotalMinutes = Clock.TotalMinutes + minutes, isMechanic = isMechanic });
        }

        private void UpdateWatchers()
        {
            if (watchers.Count == 0 || Clock == null) return;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return;
            long now = Clock.TotalMinutes;
            for (int i = watchers.Count - 1; i >= 0; i--)
            {
                var w = watchers[i];
                if (w.untilTotalMinutes < now)
                {
                    watchers.RemoveAt(i);
                    continue;
                }
                if (w.spoke) continue;
                var r = dir.Get(w.residentId);
                if (r == null || !r.IsActive) continue;
                var d = r.Position - sitePosition;
                d.y = 0f;
                if (d.sqrMagnitude > 12f * 12f) continue;
                w.spoke = true;
                var u = w.isMechanic ? RocketSpeech.MechanicWorking(r.Id, this) : RocketSpeech.WatcherRemark(r.Id, this, r.Job);
                if (u != null) dir.Speak(r.Id, u, SpeechChannel.Overheard);
            }
        }

        private Vector3 SpectatorSpot(int index, int count)
        {
            float t = count <= 1 ? 0.5f : index / (float)(count - 1);
            float a = Mathf.Lerp(-65f, 65f, t);
            var local = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (9.5f + (index % 2) * 1.2f);
            return view != null ? view.SitePoint(local) : sitePosition + local;
        }

        // ============================================================
        // 状態の問い合わせ
        // ============================================================

        internal DeviceState DS(string id) => state.Device(id);

        internal int CountRepaired()
        {
            int n = 0;
            foreach (var d in state.devices)
            {
                var def = RocketTables.Get(d.id);
                if (def != null && !def.IsFinal && d.repaired) n++;
            }
            return n;
        }

        internal bool AllRepairableRepaired()
        {
            foreach (var def in RocketTables.Devices)
            {
                if (def.IsFinal) continue;
                if (state.Device(def.Id)?.repaired != true) return false;
            }
            return true;
        }

        internal int EffectiveFee(DeviceDef def)
        {
            var ds = DS(def.Id);
            if (def.LaborFee <= 0) return 0;
            if (ds != null && HelperActive(def, ds)) return Mathf.RoundToInt(def.LaborFee * (1f - RocketTables.HelperDiscount));
            return def.LaborFee;
        }

        /// <summary>生物の能力の確認。生物の仕組みが無い構成では修理を止めない（§15.1 帰還不能にしない）。</summary>
        internal bool CreatureSatisfied(DeviceDef def, DeviceState ds)
        {
            if (!def.RequiresCreature) return true;
            var svc = Game.Service<ICreatureService>();
            if (svc == null) return true;
            return CreatureValid(svc, ds?.creatureUid, def.Id, def.RequiredAbilities);
        }

        internal bool HelperActive(DeviceDef def, DeviceState ds)
        {
            if (def.HelperAbilities == null || def.HelperAbilities.Length == 0 || ds == null) return false;
            var svc = Game.Service<ICreatureService>();
            if (svc == null) return false;
            return CreatureValid(svc, ds.helperUid, def.Id, def.HelperAbilities);
        }

        private static bool CreatureValid(ICreatureService svc, string uid, string deviceId, CreatureAbility[] abilities)
        {
            if (string.IsNullOrEmpty(uid)) return false;
            var c = svc.Get(uid);
            if (c == null) return false;
            if (c.Placement != CreaturePlacement.RocketWorkshop || c.PlacementTargetId != deviceId) return false;
            foreach (var a in abilities) if (c.AbilityLevel(a) > 0) return true;
            return false;
        }

        internal bool ItemsSatisfied(DeviceDef def, DeviceState ds)
        {
            foreach (var r in def.Items) if (ds.Delivered(r.ItemId) < r.Count) return false;
            return true;
        }

        internal bool RequirementsMet(DeviceDef def)
        {
            var ds = DS(def.Id);
            if (ds == null) return false;
            if (def.IsFinal) return AllRepairableRepaired() && state.fuelInTank >= RocketTables.FuelRequiredForLaunch;
            return ItemsSatisfied(def, ds) && ds.paid >= EffectiveFee(def) && CreatureSatisfied(def, ds);
        }

        internal float PartialProgress(DeviceDef def)
        {
            var ds = DS(def.Id);
            if (ds == null) return 0f;
            float total = 0f, have = 0f;
            foreach (var r in def.Items)
            {
                total += r.Count;
                have += Mathf.Min(r.Count, ds.Delivered(r.ItemId));
            }
            int fee = EffectiveFee(def);
            if (fee > 0)
            {
                total += 1f;
                have += Mathf.Clamp01(ds.paid / (float)fee);
            }
            if (def.RequiresCreature)
            {
                total += 1f;
                if (CreatureSatisfied(def, ds)) have += 1f;
            }
            return total > 0f ? have / total : 0f;
        }

        /// <summary>装置の状態（§3.3）。viewer の持ち物も「素材収集中」の判定に使う。</summary>
        internal DeviceStatus GetStatus(DeviceDef def, PlayerState viewer)
        {
            var ds = DS(def.Id);
            if (ds == null) return DeviceStatus.Broken;
            if (def.IsFinal)
            {
                if (state.launchReady) return DeviceStatus.Verified;
                if (RequirementsMet(def)) return DeviceStatus.ReadyToAssemble;
                if (CountRepaired() > 0 || state.fuelInTank > 0) return DeviceStatus.Collecting;
                return ds.inspected ? DeviceStatus.Investigating : DeviceStatus.Broken;
            }
            if (ds.verified) return DeviceStatus.Verified;
            if (ds.repaired) return DeviceStatus.Repaired;
            if (RequirementsMet(def)) return DeviceStatus.ReadyToAssemble;
            if (ds.AnyProgress) return DeviceStatus.Collecting;
            if (viewer != null)
                foreach (var r in def.Items)
                    if (viewer.Inventory.Has(r.ItemId)) return DeviceStatus.Collecting;
            return ds.inspected ? DeviceStatus.Investigating : DeviceStatus.Broken;
        }

        /// <summary>試運転に足りない装置（§10.2）。</summary>
        internal List<DeviceDef> MissingTestDeps(DeviceDef def)
        {
            var list = new List<DeviceDef>();
            foreach (var id in def.TestDependsOn)
            {
                var d = RocketTables.Get(id);
                if (d != null && !IsDeviceRepaired(id)) list.Add(d);
            }
            return list;
        }

        internal string PlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "?";
            var p = Game.Instance != null ? Game.Instance.Players.Get(playerId) : null;
            if (p != null) return p.DisplayName;
            var net = Game.Service<INetworkService>();
            var n = net?.GetPlayerName(playerId);
            if (!string.IsNullOrEmpty(n)) return n;
            return state.NameOf(playerId) ?? Loc.T("旅人", "Traveler");
        }

        internal IResidentInfo FindMechanic()
        {
            var dir = Game.Service<IResidentDirectory>();
            return dir?.FindByJob(ResidentJob.Mechanic);
        }

        internal bool IsMechanic(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return false;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return false;
            var r = dir.Get(residentId);
            if (r != null && r.Job == ResidentJob.Mechanic) return true;
            var m = dir.FindByJob(ResidentJob.Mechanic);
            return m != null && m.Id == residentId;
        }

        // ---- 発見（§2.1：現地の素材名は手に入れて初めて分かる） ----

        private HashSet<string> Tracked
        {
            get
            {
                if (trackedItems == null) trackedItems = new HashSet<string>(RocketTables.TrackedItemIds);
                return trackedItems;
            }
        }

        internal bool IsDiscovered(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return false;
            if (knowledge.discovered.Contains(itemId)) return true;
            var p = LocalPlayer;
            if (p != null && p.Inventory.Has(itemId)) return true;
            foreach (var d in state.devices) if (d.Delivered(itemId) > 0) return true;
            if (itemId == SharedItemIds.FuelStarHoney && state.fuelInTank > 0) return true;
            return false;
        }

        private void MarkDiscovered(string itemId)
        {
            if (string.IsNullOrEmpty(itemId) || !Tracked.Contains(itemId)) return;
            if (knowledge.discovered.Contains(itemId)) return;
            knowledge.discovered.Add(itemId);
        }

        private void ScanInventoryForDiscoveries()
        {
            var p = LocalPlayer;
            if (p == null) return;
            foreach (var s in p.Inventory.NonEmpty()) MarkDiscovered(s.itemId);
        }

        private void OnItemAdded(ItemAddedEvent e)
        {
            if (Game.Instance == null || e.Stack == null) return;
            if (e.PlayerId != Game.Instance.LocalPlayerId) return;
            MarkDiscovered(e.Stack.itemId);
        }

        // ============================================================
        // 表示の更新
        // ============================================================

        internal void RefreshVisuals()
        {
            if (view == null) return;
            var vm = new RocketViewModel
            {
                FuelRatio = Mathf.Clamp01(state.fuelInTank / (float)RocketTables.FuelRequiredForLaunch),
                LaunchReady = state.launchReady,
                HasLaunched = state.hasLaunched,
                RepairedCount = CountRepaired(),
                RepairableCount = RocketTables.RepairableCount
            };
            var csvc = Game.Service<ICreatureService>();
            foreach (var def in RocketTables.Devices)
            {
                if (def.Index < 0 || def.Index >= RocketViewModel.DeviceSlots) continue;
                var ds = DS(def.Id);
                if (ds == null) continue;
                if (def.IsFinal)
                {
                    vm.Repaired[def.Index] = state.launchReady;
                    vm.Verified[def.Index] = state.launchReady;
                    continue;
                }
                vm.Repaired[def.Index] = ds.repaired;
                vm.Verified[def.Index] = ds.verified;
                vm.Partial[def.Index] = ds.repaired ? 1f : PartialProgress(def);
                vm.CreaturePlaced[def.Index] = csvc != null && (CreatureValid(csvc, ds.creatureUid, def.Id, def.RequiredAbilities) || CreatureValid(csvc, ds.helperUid, def.Id, def.HelperAbilities));
                if (ds.AnyProgress) vm.AnyProgress = true;
                if (def.Id == "R02")
                {
                    vm.HullPlates = ds.Delivered(SharedItemIds.PartHullPlate);
                    vm.HullPlatesNeeded = def.Items.Count > 0 ? def.Items[0].Count : 6;
                }
                if (!ds.repaired)
                {
                    foreach (var r in def.Items)
                    {
                        int n = Mathf.Min(ds.Delivered(r.ItemId), r.Count);
                        for (int i = 0; i < n && vm.StoredColors.Count < 12; i++) vm.StoredColors.Add(ItemRegistry.GetOrPlaceholder(r.ItemId).Color);
                    }
                }
            }
            if (state.fuelInTank > 0) vm.AnyProgress = true;
            view.Apply(vm);
        }

        // ============================================================
        // シーン上の操作対象
        // ============================================================

        private void AttachInteractables()
        {
            if (view == null) return;
            RocketInteractable.Attach(view.ShipRoot.gameObject, ShipPrompt, OnShipInteract, p => !cutscenePlaying);
            RocketInteractable.Attach(view.Workbench, p => Loc.T("修理台（修理する）", "Repair bench"), p => OpenRepairWindow(null));
            RocketInteractable.Attach(view.ProcessingTable, p => Loc.T("加工台", "Processing table"), p => OpenProcessing(CraftStationType.RocketWorkshop, "rocket.workshop", p));
            RocketInteractable.Attach(view.Refinery, p => Loc.T("燃料精製器", "Fuel refiner"), p => OpenProcessing(CraftStationType.Refinery, "rocket.refinery", p));
            RocketInteractable.Attach(view.Storage, p => HasReadyOrders(p.PlayerId) ? Loc.T("部品置き場（頼んだ物が届いています）", "Parts storage (order ready)") : Loc.T("部品置き場", "Parts storage"),
                p => UIManager.Open(new PartsStorageWindow(this, p)));
            RocketInteractable.Attach(view.CreaturePad, p => Loc.T("生物の作業スペース", "Creature workspace"), p => OpenCreaturePad(p));
        }

        private string ShipPrompt(PlayerState p)
        {
            if (state.hasLaunched) return null;
            if (state.launchReady) return Loc.T("宇宙船（帰還できます）", "Spaceship (ready to return)");
            return Loc.T("宇宙船を調べる", "Inspect the spaceship");
        }

        private void OnShipInteract(PlayerState p)
        {
            if (state.hasLaunched || cutscenePlaying) return;
            if (state.launchReady)
            {
                UIManager.Choose(Loc.T("宇宙船", "Spaceship"), Loc.T("全装置正常。帰還が可能です。", "All systems nominal. Ready to return."), new List<ChoiceOption>
                {
                    new ChoiceOption(Loc.T("帰還する", "Return home"), () => ConfirmReturn(p)),
                    new ChoiceOption(Loc.T("診断を見る", "View diagnostics"), () => OpenDiagnostics()),
                    new ChoiceOption(Loc.T("まだ村で暮らす", "Stay a while longer"), null)
                });
                return;
            }
            OpenDiagnostics();
        }

        private void ConfirmReturn(PlayerState p)
        {
            if (WorldAuthority.IsGuest || (p != null && p.Permission < PermissionLevel.Owner))
            {
                UIManager.Message(Loc.T("帰還", "Return"), Loc.T("この宇宙船の出発は、星の持ち主が決めます。", "The owner of this planet decides when the ship departs."));
                return;
            }
            UIManager.Confirm(Loc.T("帰還する", "Return home"),
                Loc.T("帰還すると エンディングになります。その後、ロケット前へ戻って生活を続けるか、次の星へ進むかを選べます",
                      "Returning leads to the ending. Afterwards you can come back to the rocket and keep living here, or move on to the next planet."),
                () => WorldAuthority.Submit(new RocketBeginReturnCommand()));
        }

        internal void OpenDiagnostics()
        {
            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                tablet.Open(RocketDiagnosticsApp.Id);
                return;
            }
            UIManager.Open(new DiagnosticsWindow(this));
        }

        internal void OpenRepairWindow(string deviceId)
        {
            var w = UIManager.Find<RepairWindow>();
            if (w == null) UIManager.Open(new RepairWindow(this, LocalPlayer, deviceId));
            else
            {
                if (deviceId != null) w.Select(deviceId);
                UIManager.Open(w);
            }
        }

        internal void OpenProcessing(CraftStationType type, string stationId, PlayerState p)
        {
            var crafting = Game.Service<ICraftingService>();
            if (crafting != null)
            {
                crafting.OpenStation(type, stationId, p);
                return;
            }
            UIManager.Choose(Loc.T("加工", "Processing"),
                Loc.T("この設備はまだ使えないようだ。修理屋に加工を頼むことはできる。", "This equipment isn't usable yet. You can ask the mechanic to process materials."),
                new List<ChoiceOption>
                {
                    new ChoiceOption(Loc.T("修理屋に頼む", "Ask the mechanic"), () => OpenOrders(null)),
                    new ChoiceOption(Loc.T("やめる", "Cancel"), null)
                });
        }

        internal void OpenCreaturePad(PlayerState p)
        {
            var svc = Game.Service<ICreatureService>();
            if (svc == null)
            {
                UIManager.Message(Loc.T("生物の作業スペース", "Creature workspace"), Loc.T("ここで生物に手伝ってもらえそうだ。", "Creatures could help out here."));
                return;
            }
            var opts = new List<ChoiceOption>();
            foreach (var def in RocketTables.Devices)
            {
                if (def.IsFinal) continue;
                bool needs = def.RequiresCreature;
                bool helps = def.HelperAbilities != null && def.HelperAbilities.Length > 0;
                if (!needs && !helps) continue;
                var d = def;
                string label = needs ? Loc.Format("{0}（必須）", "{0} (required)", def.Name) : Loc.Format("{0}（手伝い）", "{0} (helper)", def.Name);
                opts.Add(new ChoiceOption(label, () => ChooseCreatureFor(d, needs, p)));
            }
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(Loc.T("生物の作業スペース", "Creature workspace"), Loc.T("どの装置を手伝ってもらう？（生物は消費しません）", "Which device should a creature help with? (Creatures are not consumed.)"), opts);
        }

        /// <summary>生物を選んで作業場へ配置する（UIから）。</summary>
        internal void ChooseCreatureFor(DeviceDef def, bool required, PlayerState p)
        {
            var svc = Game.Service<ICreatureService>();
            if (svc == null || p == null) return;
            var abilities = required ? def.RequiredAbilities : def.HelperAbilities;
            var ds = DS(def.Id);
            string current = required ? ds?.creatureUid : ds?.helperUid;
            var opts = new List<ChoiceOption>();
            var seen = new HashSet<string>();
            foreach (var a in abilities)
            {
                foreach (var c in svc.FindOwnedWithAbility(p.PlayerId, a))
                {
                    if (c == null || !seen.Add(c.Uid)) continue;
                    var uid = c.Uid;
                    string name = !string.IsNullOrEmpty(c.Nickname) ? c.Nickname : svc.GetSpeciesName(c.SpeciesId);
                    string abil = RocketTables.AbilityLabel(a);
                    opts.Add(new ChoiceOption(Loc.Format("{0}（{1} Lv{2}）", "{0} ({1} Lv{2})", name, abil, c.AbilityLevel(a)), () =>
                        WorldAuthority.Submit(new RocketAssignCreatureCommand { deviceId = def.Id, creatureUid = uid, asHelper = !required }, r =>
                        {
                            if (r != null && r.Success) UIManager.Toast(Loc.Format("{0}が作業場で手伝い始めた", "{0} started helping at the workshop", name));
                        })));
                }
            }
            if (!string.IsNullOrEmpty(current))
            {
                opts.Add(new ChoiceOption(Loc.T("今の生物を連れて帰る", "Take the current creature back"), () =>
                    WorldAuthority.Submit(new RocketAssignCreatureCommand { deviceId = def.Id, creatureUid = "", asHelper = !required })));
            }
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            var abilityNames = new List<string>();
            foreach (var a in abilities) abilityNames.Add(RocketTables.AbilityLabel(a));
            string msg = seen.Count == 0
                ? Loc.Format("「{0}」の能力を持つ生物を連れていない。", "You have no creature with: {0}.", string.Join(Loc.T("・", ", "), abilityNames))
                : Loc.Format("「{0}」の能力を持つ生物に手伝ってもらう。", "Ask a creature with: {0}.", string.Join(Loc.T("・", ", "), abilityNames));
            UIManager.Choose(def.Name, msg, opts);
        }

        /// <summary>修理屋の施設（RepairWorkshop）のカウンター。修理屋の施設が無い星でも作業場の脇に置く（手段を失わない §8.3）。</summary>
        private void BuildMechanicCounter(Transform root, WorldLayout layout)
        {
            Vector3 pos;
            float yaw = 0f;
            var fac = layout?.GetFacility(FacilityType.RepairWorkshop);
            if (fac != null)
            {
                var toward = fac.entrance - fac.position;
                toward.y = 0f;
                if (toward.sqrMagnitude < 0.01f) toward = Quaternion.Euler(0, fac.rotationY, 0) * Vector3.forward;
                pos = fac.entrance + toward.normalized * 1.2f + Quaternion.Euler(0, 90f, 0) * toward.normalized * 1.6f;
                yaw = Mathf.Atan2(toward.x, toward.z) * Mathf.Rad2Deg;
            }
            else
            {
                pos = view != null ? view.SitePoint(new Vector3(7.5f, 0f, 6.5f)) : sitePosition + new Vector3(7f, 0, 6f);
                yaw = siteYaw;
            }
            if (layout != null) pos = layout.OnGround(pos);
            counterRoot = PrimitiveBuilder.Empty("MechanicCounter", root, Vector3.zero);
            counterRoot.transform.position = pos;
            counterRoot.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var t = counterRoot.transform;
            PrimitiveBuilder.Create(PrimitiveType.Cube, t, new Vector3(0, 0.55f, 0), new Vector3(1.8f, 1.1f, 0.7f), new Color(0.5f, 0.55f, 0.65f), "Counter");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 1.13f, 0), new Vector3(1.9f, 0.08f, 0.8f), new Color(0.75f, 0.6f, 0.4f), "Top");
            // 看板：歯車と四本腕の絵（文字は使わない）
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0.6f, 1.75f, 0f), new Vector3(0.08f, 0.6f, 0.08f), new Color(0.3f, 0.3f, 0.35f), "SignPole");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0.6f, 2.4f, 0f), new Vector3(0.7f, 0.06f, 0.7f), new Color(1f, 0.75f, 0.3f), "Gear", new Vector3(90f, 0, 0));
            for (int i = 0; i < 4; i++)
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0.6f, 2.4f, 0f) + Quaternion.Euler(0, 0, i * 45f) * new Vector3(0.42f, 0, 0), new Vector3(0.18f, 0.12f, 0.08f), new Color(1f, 0.75f, 0.3f), "Tooth", new Vector3(0, 0, i * 45f));
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-0.4f, 1.25f, 0f), new Vector3(0.25f, 0.18f, 0.25f), new Color(0.35f, 0.8f, 0.45f), "SampleUnit");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.05f, 1.27f, 0f), Vector3.one * 0.2f, new Color(0.95f, 0.75f, 0.3f), "SampleParts");
            RocketInteractable.Attach(counterRoot, p => Loc.T("修理屋のカウンター", "Mechanic's counter"), p => OpenCounterMenu(p));
        }

        internal void OpenCounterMenu(PlayerState p)
        {
            var mech = FindMechanic();
            var opts = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("部品を買う", "Buy parts"), () => OpenVendor(mech?.Id)),
                new ChoiceOption(Loc.T("加工を頼む", "Request processing"), () => OpenOrders(mech?.Id)),
            };
            if (HasReadyOrders(p.PlayerId)) opts.Add(new ChoiceOption(Loc.T("頼んだ物を受け取る", "Collect orders"), () => CollectOrders()));
            if (knowledge.diagramSeen) opts.Add(new ChoiceOption(Loc.T("宇宙船の図を見る", "Look at the ship diagram"), () => OpenDiagram(mech?.Id)));
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            string msg = mech == null
                ? Loc.T("誰もいないが、部品と値札、代金を入れる箱が置いてある。", "Nobody is here, but there are parts, price tags and a box for payment.")
                : Loc.T("修理屋の店先。部品が並んでいる。", "The mechanic's storefront. Parts are on display.");
            UIManager.Choose(Loc.T("修理屋", "Mechanic"), msg, opts);
        }

        // ============================================================
        // 修理屋（図・部品・作業依頼）
        // ============================================================

        /// <summary>修理屋の図（第1章：身振りと図で修理に必要なものを教える）。</summary>
        internal void OpenDiagram(string mechanicId)
        {
            bool first = !knowledge.diagramSeen;
            knowledge.diagramSeen = true;
            WorldAuthority.Submit(new RocketInspectCommand { deviceId = "*" });
            UIManager.Open(new RocketDiagramWindow(this, mechanicId));
            if (!string.IsNullOrEmpty(mechanicId))
            {
                var dir = Game.Service<IResidentDirectory>();
                int broken = RocketTables.RepairableCount - CountRepaired();
                dir?.Speak(mechanicId, RocketSpeech.MechanicDiagramIntro(mechanicId, broken, first));
            }
        }

        internal void OpenVendor(string mechanicId)
        {
            var p = LocalPlayer;
            if (p == null) return;
            var entries = new List<VendorEntry>();
            foreach (var part in RocketTables.PurchasedParts)
            {
                entries.Add(new VendorEntry { ItemId = part.ItemId, Price = part.Price });
                MarkDiscovered(part.ItemId); // 店先で現物と値札を見た
            }
            var dir = Game.Service<IResidentDirectory>();
            if (!string.IsNullOrEmpty(mechanicId)) dir?.Speak(mechanicId, RocketSpeech.MechanicSellOffer(mechanicId));
            UIManager.Open(new VendorWindow(Loc.T("修理屋の部品", "Mechanic's parts"), p, entries, (e, amount) =>
            {
                MarkDiscovered(e.ItemId);
                WorldAuthority.Submit(new RocketMechanicIncomeCommand { amount = e.Price * amount, itemId = e.ItemId, count = amount });
                if (!string.IsNullOrEmpty(mechanicId)) dir?.Speak(mechanicId, RocketSpeech.MechanicThanks(mechanicId, e.ItemId, amount));
            }));
        }

        internal void OpenOrders(string mechanicId)
        {
            UIManager.Open(new MechanicOrderWindow(this, LocalPlayer, mechanicId));
        }

        internal bool HasReadyOrders(string playerId)
        {
            if (Clock == null) return false;
            foreach (var o in state.orders) if (o.playerId == playerId && o.readyDay <= Clock.Day) return true;
            return false;
        }

        internal void CollectOrders()
        {
            WorldAuthority.Submit(new RocketCollectOrdersCommand(), r =>
            {
                if (r != null && r.Success) UIManager.Toast(r.Message);
            });
        }

        private void OnResidentTalked(ResidentTalkedEvent e)
        {
            if (Game.Instance == null || e.PlayerId != Game.Instance.LocalPlayerId) return;
            if (knowledge.diagramSeen || !IsMechanic(e.ResidentId)) return;
            // 会話メニューの上に図を出すため、少し遅らせて開く
            pendingDiagramResident = e.ResidentId;
            pendingDiagramAt = UnityEngine.Time.unscaledTime + 0.35f;
        }

        private void OnResidentMovedOut(ResidentMovedOutEvent e)
        {
            // 修理屋が引っ越しても後継者（FindByJob）が引き継ぐので、ここでは見物の記録だけ消す
            watchers.RemoveAll(w => w.residentId == e.ResidentId);
        }

        // ============================================================
        // コマンドハンドラ（ホストで実行）
        // ============================================================

        private void Remember(CommandContext ctx)
        {
            if (ctx.Issuer != null) state.RememberName(ctx.IssuerId, ctx.Issuer.DisplayName);
        }

        private void AddHistory(string playerId, string kind, string deviceId, string itemId = null, int count = 0)
        {
            state.history.Add(new RepairHistoryEntry
            {
                day = Clock != null ? Clock.Day : 0,
                minute = Clock != null ? Clock.MinuteOfDay : 0,
                playerId = playerId,
                kind = kind,
                deviceId = deviceId,
                itemId = itemId,
                count = count
            });
            while (state.history.Count > RocketTables.HistoryMax) state.history.RemoveAt(0);
        }

        private void Changed()
        {
            MarkDirty();
            RefreshVisuals();
        }

        private CommandResult HandleInspect(RocketInspectCommand cmd, CommandContext ctx)
        {
            bool changed = false;
            foreach (var d in state.devices)
            {
                if (cmd.deviceId != "*" && d.id != cmd.deviceId) continue;
                if (!d.inspected)
                {
                    d.inspected = true;
                    changed = true;
                }
            }
            if (changed) MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleDeliver(RocketDeliverCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            if (state.hasLaunched) return CommandResult.Fail("出発中です", "The ship is departing");
            if (cmd.count <= 0 || string.IsNullOrEmpty(cmd.itemId)) return CommandResult.Fail("納品する物がありません", "Nothing to deliver");
            Remember(ctx);

            // 燃料タンクへの給油（燃料はいつでも貯められる。供給の試験だけ燃料ラインが必要 §10.2）
            if (cmd.deviceId == RocketTables.TankTargetId)
            {
                if (cmd.itemId != SharedItemIds.FuelStarHoney) return CommandResult.Fail("この燃料は使えません", "This fuel doesn't fit");
                int space = RocketTables.FuelRequiredForLaunch - state.fuelInTank;
                int n = Mathf.Min(space, cmd.count);
                if (n <= 0) return CommandResult.Fail("燃料タンクは満タンです", "The fuel tank is full");
                if (!ctx.HasItem(cmd.itemId, n) || !ctx.TakeItem(cmd.itemId, n, "rocket.fuel")) return CommandResult.Fail("燃料が足りません", "Not enough fuel");
                state.fuelInTank += n;
                state.Device(RocketTables.FinalDeviceId)?.AddContribution(ctx.IssuerId, n * 10);
                AddHistory(ctx.IssuerId, "fuel", RocketTables.FinalDeviceId, cmd.itemId, n);
                Changed();
                if (view != null) view.PlayDeliverPop(3, ItemRegistry.GetOrPlaceholder(cmd.itemId).Color);
                return CommandResult.Ok(Loc.Format("燃料を{0}入れた（{1}/{2}）", "Added {0} fuel ({1}/{2})", n, state.fuelInTank, RocketTables.FuelRequiredForLaunch),
                    null);
            }

            var def = RocketTables.Get(cmd.deviceId);
            var ds = DS(cmd.deviceId);
            if (def == null || ds == null || def.IsFinal) return CommandResult.Fail("その装置はありません", "No such device");
            if (ds.repaired) return CommandResult.Fail("その装置は修理済みです", "That device is already repaired");
            ItemRequirement req = null;
            foreach (var r in def.Items) if (r.ItemId == cmd.itemId) req = r;
            if (req == null) return CommandResult.Fail("その装置には使わない物です", "That device doesn't use this");
            int remaining = req.Count - ds.Delivered(req.ItemId);
            int count = Mathf.Min(remaining, cmd.count);
            if (count <= 0) return CommandResult.Fail("もう十分あります", "Already enough");
            if (!ctx.HasItem(req.ItemId, count) || !ctx.TakeItem(req.ItemId, count, "rocket.deliver")) return CommandResult.Fail("持ち物が足りません", "You don't have enough");
            ds.AddDelivered(req.ItemId, count);
            ds.AddContribution(ctx.IssuerId, count * 10);
            if (!ds.inspected) ds.inspected = true;
            AddHistory(ctx.IssuerId, "deliver", def.Id, req.ItemId, count);
            Changed();
            if (view != null) view.PlayDeliverPop(def.Index, ItemRegistry.GetOrPlaceholder(req.ItemId).Color);
            return CommandResult.Ok(Loc.Format("{0}を{1}個 納品した", "Delivered {1} x {0}", ItemRegistry.NameOf(req.ItemId), count), null);
        }

        private CommandResult HandlePay(RocketPayCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            var def = RocketTables.Get(cmd.deviceId);
            var ds = DS(cmd.deviceId);
            if (def == null || ds == null || def.IsFinal) return CommandResult.Fail("その装置はありません", "No such device");
            if (ds.repaired) return CommandResult.Fail("その装置は修理済みです", "That device is already repaired");
            int fee = EffectiveFee(def);
            int amount = Mathf.Min(cmd.amount, fee - ds.paid);
            if (amount <= 0) return CommandResult.Fail("作業費は支払い済みです", "The labor fee is already paid");
            if (!ctx.TryCharge(amount, "rocket.labor:" + def.Id)) return CommandResult.Fail("お金が足りません", "Not enough money");
            Remember(ctx);
            ds.paid += amount;
            ds.AddContribution(ctx.IssuerId, Mathf.Max(1, amount / 100));
            AddHistory(ctx.IssuerId, "pay", def.Id, null, amount);
            // 作業費は修理屋の収入になる（住民の財布と経済循環）
            var mech = FindMechanic();
            if (mech != null) Game.Service<IResidentDirectory>()?.GiveMoney(mech.Id, amount, "rocket_labor");
            Changed();
            return CommandResult.Ok(Loc.Format("作業費 {0} を支払った", "Paid {0} for labor", Wallet.Format(amount)), null);
        }

        private CommandResult HandleAssignCreature(RocketAssignCreatureCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            var svc = Game.Service<ICreatureService>();
            if (svc == null) return CommandResult.Fail("生物に手伝ってもらう仕組みが使えません", "Creature help isn't available");
            var def = RocketTables.Get(cmd.deviceId);
            var ds = DS(cmd.deviceId);
            if (def == null || ds == null || def.IsFinal) return CommandResult.Fail("その装置はありません", "No such device");
            string current = cmd.asHelper ? ds.helperUid : ds.creatureUid;

            // 連れて帰る（生物は消費しない §7.1）
            if (string.IsNullOrEmpty(cmd.creatureUid))
            {
                if (string.IsNullOrEmpty(current)) return CommandResult.Ok();
                var cur = svc.Get(current);
                if (cur != null && cur.OwnerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner)
                    return CommandResult.Fail("他の人の生物です", "That creature belongs to someone else");
                // この装置に置かれている時だけ手元へ戻す（別の装置へ移した個体はそのまま）
                if (cur != null && cur.Placement == CreaturePlacement.RocketWorkshop && cur.PlacementTargetId == def.Id) svc.SetPlacement(current, CreaturePlacement.Companion, null);
                if (cmd.asHelper) ds.helperUid = null;
                else ds.creatureUid = null;
                Changed();
                return CommandResult.Ok();
            }

            var c = svc.Get(cmd.creatureUid);
            if (c == null) return CommandResult.Fail("その生物が見つかりません", "Creature not found");
            if (c.OwnerId != ctx.IssuerId) return CommandResult.Fail("自分の生物だけ配置できます", "You can only place your own creatures");
            var abilities = cmd.asHelper ? def.HelperAbilities : def.RequiredAbilities;
            bool ok = false;
            foreach (var a in abilities) if (c.AbilityLevel(a) > 0) ok = true;
            if (!ok) return CommandResult.Fail("その生物はこの作業に向いていないようだ", "That creature doesn't suit this job");
            if (!string.IsNullOrEmpty(current) && current != cmd.creatureUid)
            {
                var prev = svc.Get(current);
                bool prevStillHere = prev != null && prev.Placement == CreaturePlacement.RocketWorkshop && prev.PlacementTargetId == def.Id;
                if (prevStillHere && prev.OwnerId != ctx.IssuerId) return CommandResult.Fail("別の人の生物が手伝っています", "Another player's creature is already helping");
                if (prevStillHere) svc.SetPlacement(current, CreaturePlacement.Companion, null);
            }
            if (!svc.SetPlacement(cmd.creatureUid, CreaturePlacement.RocketWorkshop, def.Id)) return CommandResult.Fail("生物を配置できませんでした", "Could not place the creature");
            Remember(ctx);
            if (cmd.asHelper) ds.helperUid = cmd.creatureUid;
            else ds.creatureUid = cmd.creatureUid;
            ds.AddContribution(ctx.IssuerId, 20);
            AddHistory(ctx.IssuerId, "creature", def.Id, c.SpeciesId, 1);
            Changed();
            if (view != null) view.PlayDeliverPop(def.Index, new Color(0.8f, 0.6f, 1f));
            return CommandResult.Ok();
        }

        private CommandResult HandleInstall(RocketInstallCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            var def = RocketTables.Get(cmd.deviceId);
            var ds = DS(cmd.deviceId);
            if (def == null || ds == null || def.IsFinal) return CommandResult.Fail("その装置はありません", "No such device");
            if (ds.repaired) return CommandResult.Fail("その装置は修理済みです", "Already repaired");
            if (!RequirementsMet(def)) return CommandResult.Fail("まだ必要なものが揃っていません", "Requirements are not met yet");
            Remember(ctx);
            ds.repaired = true;
            ds.inspected = true;
            ds.repairedDay = Clock != null ? Clock.Day : 0;
            ds.repairedBy = ctx.IssuerId;
            ds.AddContribution(ctx.IssuerId, 30);
            AddHistory(ctx.IssuerId, "install", def.Id);
            if (Game.Instance != null)
            {
                Game.Instance.Flags.Set("rocket.repaired." + def.Id);
                Game.Instance.Flags.Set("rocket.repaired_count", CountRepaired());
                WorldAuthority.MarkDirty("core.flags");
            }
            Changed();
            EventBus.Publish(new RocketDeviceRepairedEvent { DeviceId = def.Id, RepairedCount = RepairedDeviceCount, TotalCount = TotalDeviceCount, PlayerId = ctx.IssuerId });
            OnDeviceRepairedLocal(def, ctx.IssuerId);
            return CommandResult.Ok(Loc.Format("{0}を修理した！", "Repaired the {0}!", def.Name), null);
        }

        private CommandResult HandleTest(RocketTestCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            var def = RocketTables.Get(cmd.deviceId);
            var ds = DS(cmd.deviceId);
            if (def == null || ds == null || def.IsFinal) return CommandResult.Fail("その装置はありません", "No such device");
            if (!ds.repaired) return CommandResult.Fail("先に修理が必要です", "Repair it first");
            var missing = MissingTestDeps(def);
            if (missing.Count > 0)
            {
                var ja = new List<string>();
                var en = new List<string>();
                foreach (var m in missing)
                {
                    ja.Add(m.NameJa);
                    en.Add(m.NameEn);
                }
                return CommandResult.Fail("試運転には " + string.Join("・", ja) + " が必要です", "The test needs: " + string.Join(", ", en));
            }
            if (def.TestNeedsFuel && state.fuelInTank <= 0) return CommandResult.Fail("燃料タンクが空です", "The fuel tank is empty");
            Remember(ctx);
            bool first = !ds.verified;
            ds.verified = true;
            state.testSerial++;
            state.lastTestDevice = def.Id;
            if (first) AddHistory(ctx.IssuerId, "test", def.Id);
            Changed();
            if (view != null) view.PlayTest(def.Index);
            // エンジンの試運転は音で住民が集まる（§12）
            if (def.Id == "R05") GatherResidents(def, false, ctx.IssuerId, true);
            return CommandResult.Ok(Loc.Format("{0}：正常", "{0}: OK", def.TestName), null);
        }

        private CommandResult HandleFinalTest(RocketFinalTestCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では修理を手伝う権限がありません", "You don't have permission to help with repairs here");
            if (!AllRepairableRepaired()) return CommandResult.Fail("まだ修理の終わっていない装置があります", "Some devices are not repaired yet");
            if (state.fuelInTank < RocketTables.FuelRequiredForLaunch)
                return CommandResult.Fail("燃料が足りません（" + state.fuelInTank + "/" + RocketTables.FuelRequiredForLaunch + "）", "Not enough fuel (" + state.fuelInTank + "/" + RocketTables.FuelRequiredForLaunch + ")");
            Remember(ctx);
            bool first = !state.launchReady;
            foreach (var d in state.devices) d.verified = true;
            state.finalTestDone = true;
            state.launchReady = true;
            if (first) state.launchReadyDay = Clock != null ? Clock.Day : 0;
            state.finalSerial++;
            var final = DS(RocketTables.FinalDeviceId);
            if (final != null)
            {
                final.repaired = true;
                final.inspected = true;
                final.repairedDay = state.launchReadyDay;
                final.repairedBy = ctx.IssuerId;
                final.AddContribution(ctx.IssuerId, 50);
            }
            if (first) AddHistory(ctx.IssuerId, "final", RocketTables.FinalDeviceId);
            if (Game.Instance != null)
            {
                Game.Instance.Flags.Set("rocket.launch_ready");
                WorldAuthority.MarkDirty("core.flags");
            }
            Changed();
            if (first)
            {
                EventBus.Publish(new RocketDeviceRepairedEvent { DeviceId = RocketTables.FinalDeviceId, RepairedCount = RepairedDeviceCount, TotalCount = TotalDeviceCount, PlayerId = ctx.IssuerId });
                EventBus.Publish(new RocketLaunchReadyEvent());
            }
            StartFinalSequenceLocal(ctx.IssuerId);
            return CommandResult.Ok();
        }

        private CommandResult HandleBeginReturn(RocketBeginReturnCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Owner)) return CommandResult.Fail("出発は星の持ち主が決めます", "The planet owner decides the departure");
            if (!state.launchReady) return CommandResult.Fail("まだ帰還できません", "Not ready to return yet");
            if (state.hasLaunched) return CommandResult.Fail("出発中です", "Already departing");
            state.hasLaunched = true;
            state.launchCount++;
            AddHistory(ctx.IssuerId, "launch", RocketTables.FinalDeviceId);
            Changed();
            if (ctx.IsLocalIssuer) StartEnding();
            return CommandResult.Ok();
        }

        private CommandResult HandleOrder(RocketOrderCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では依頼する権限がありません", "You don't have permission to place orders here");
            var r = RocketTables.GetProxy(cmd.recipeId);
            if (r == null) return CommandResult.Fail("その加工はできません", "That processing isn't available");
            int times = Mathf.Clamp(cmd.count, 1, 20);
            int need = r.InputCount * times;
            int fee = r.Fee * times;
            if (!ctx.HasItem(r.InputItemId, need)) return CommandResult.Fail("材料が足りません", "Not enough materials");
            if (!ctx.TryCharge(fee, "rocket.order:" + r.Id)) return CommandResult.Fail("お金が足りません", "Not enough money");
            if (!ctx.TakeItem(r.InputItemId, need, "rocket.order"))
            {
                ctx.PayMoney(fee, "rocket.order.refund");
                return CommandResult.Fail("材料が足りません", "Not enough materials");
            }
            Remember(ctx);
            int today = Clock != null ? Clock.Day : 0;
            state.orders.Add(new WorkOrder { id = Ids.New(), playerId = ctx.IssuerId, recipeId = r.Id, count = times, orderedDay = today, readyDay = today + Mathf.Max(1, r.Days) });
            var mech = FindMechanic();
            if (mech != null) Game.Service<IResidentDirectory>()?.GiveMoney(mech.Id, fee, "rocket_order");
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleCollectOrders(RocketCollectOrdersCommand cmd, CommandContext ctx)
        {
            int today = Clock != null ? Clock.Day : 0;
            int got = 0;
            for (int i = state.orders.Count - 1; i >= 0; i--)
            {
                var o = state.orders[i];
                if (o.playerId != ctx.IssuerId || o.readyDay > today) continue;
                var r = RocketTables.GetProxy(o.recipeId);
                if (r != null)
                {
                    ctx.GiveItem(new ItemStack(r.OutputItemId, r.OutputCount * o.count), "rocket.order");
                    got += r.OutputCount * o.count;
                }
                state.orders.RemoveAt(i);
            }
            if (got <= 0) return CommandResult.Fail("まだ出来上がっていません", "Nothing is ready yet");
            MarkDirty();
            return CommandResult.Ok(Loc.Format("頼んだ物を{0}個 受け取った", "Collected {0} item(s)", got), null);
        }

        private CommandResult HandleMechanicIncome(RocketMechanicIncomeCommand cmd, CommandContext ctx)
        {
            if (cmd.amount <= 0) return CommandResult.Ok();
            var mech = FindMechanic();
            if (mech != null) Game.Service<IResidentDirectory>()?.GiveMoney(mech.Id, cmd.amount, "rocket_parts");
            return CommandResult.Ok();
        }

        // ============================================================
        // 修理の瞬間の演出・住民の反応（§12・§14.2）
        // ============================================================

        private void OnDeviceRepairedLocal(DeviceDef def, string playerId)
        {
            if (view != null) view.PlayRepairBurst(def.Index, def.DiagramColor);
            GatherResidents(def, def.BigEvent, playerId, false);
            if (Game.Instance != null && playerId == Game.Instance.LocalPlayerId)
            {
                UIManager.Toast(Loc.Format("{0}を修理した！ {1}", "Repaired the {0}! {1}", def.Name, def.Function), 5f);
                if (AllRepairableRepaired() && !state.launchReady)
                    UIManager.Toast(Loc.T("全装置が直った。燃料を満たして最終起動試験をしよう。", "Every system is repaired. Fill the tank and run the final startup test."), 6f);
            }
        }

        /// <summary>近くの住民が集まって見る。修理屋は喜ぶ（意味データ＋Jump）。</summary>
        internal void GatherResidents(DeviceDef def, bool big, string playerId, bool isEngineTest)
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || Clock == null || view == null) return;
            var mech = FindMechanic();
            float radius = big ? RocketTables.GatherRadiusBig : RocketTables.GatherRadiusSmall;
            int max = big ? RocketTables.GatherCountBig : RocketTables.GatherCountSmall;
            var list = new List<IResidentInfo>();
            foreach (var r in dir.ActiveResidents)
            {
                if (r.Activity == ResidentActivity.Sleeping || r.Activity == ResidentActivity.Packing) continue;
                var d = r.Position - sitePosition;
                d.y = 0f;
                if (d.sqrMagnitude > radius * radius && (mech == null || r.Id != mech.Id)) continue;
                list.Add(r);
            }
            list.Sort((a, b) => (a.Position - sitePosition).sqrMagnitude.CompareTo((b.Position - sitePosition).sqrMagnitude));
            var chosen = new List<string>();
            if (mech != null && mech.IsActive) chosen.Add(mech.Id);
            foreach (var r in list)
            {
                if (chosen.Count >= max + 1) break;
                if (!chosen.Contains(r.Id)) chosen.Add(r.Id);
            }
            int until = Mathf.Min(23 * 60 + 50, Clock.MinuteOfDay + RocketTables.GatherMinutes);
            for (int i = 0; i < chosen.Count; i++)
            {
                var pos = mech != null && chosen[i] == mech.Id ? view.SitePoint(new Vector3(2.6f, 0f, 3.8f)) : SpectatorSpot(i, Mathf.Max(2, chosen.Count));
                dir.RequestGoTo(chosen[i], pos, until, "rocket.repair");
            }
            view.StartCoroutine(ReactionRoutine(def, chosen, mech != null ? mech.Id : null, playerId, big, isEngineTest));
        }

        private IEnumerator ReactionRoutine(DeviceDef def, List<string> residentIds, string mechanicId, string playerId, bool big, bool isEngineTest)
        {
            // 集まるまで少し待ち、その場にいる住民だけが反応する（見ていない住民は知らない）
            yield return new WaitForSeconds(isEngineTest ? 2.5f : 4f);
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) yield break;
            int day = Clock != null ? Clock.Day : 0;
            foreach (var id in residentIds)
            {
                var r = dir.Get(id);
                if (r == null || !r.IsActive) continue;
                var d = r.Position - sitePosition;
                d.y = 0f;
                bool near = d.sqrMagnitude < 25f * 25f;
                bool isMech = id == mechanicId;
                if (!near && !isMech) continue;
                if (isMech)
                {
                    if (near) dir.Speak(id, isEngineTest ? RocketSpeech.EngineTestReaction(id, true) : RocketSpeech.MechanicCelebrate(id, def), SpeechChannel.Direct);
                    if (!isEngineTest && !string.IsNullOrEmpty(playerId)) dir.ChangeFriendship(id, playerId, 2, "rocket_repair");
                }
                else if (near)
                {
                    dir.Speak(id, isEngineTest ? RocketSpeech.EngineTestReaction(id, false) : RocketSpeech.RepairReaction(id, def, r.Job), SpeechChannel.Overheard);
                }
                if (near && !isEngineTest)
                {
                    dir.AddMemory(id, new ResidentMemoryEntry
                    {
                        kind = "saw_repair",
                        playerId = playerId,
                        day = day,
                        importance = big ? 0.7f : 0.4f,
                        detail = def.Id
                    });
                }
                yield return new WaitForSeconds(0.8f);
            }
        }

        // ============================================================
        // 最終起動（§18）
        // ============================================================

        /// <summary>最終起動試験の演出：エンジン点火・住民が集まる → 帰還可能の表示。強制離陸はしない。</summary>
        private void StartFinalSequenceLocal(string playerId)
        {
            if (view == null || cutscenePlaying) return;
            view.StartCoroutine(FinalSequence(playerId));
        }

        private IEnumerator FinalSequence(string playerId)
        {
            cutscenePlaying = true;
            var p = LocalPlayer;
            p?.LockInput("rocket.cutscene");
            UIManager.CloseAll();
            var finalDef = RocketTables.Get(RocketTables.FinalDeviceId);
            if (IsAuthority && finalDef != null) GatherResidents(finalDef, true, playerId, false);

            displayOverlay?.Begin(Loc.T("最終起動試験", "FINAL STARTUP TEST"));
            yield return new WaitForSeconds(1.2f);
            // §18.2 の流れ。素材の由来を振り返る
            var steps = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, Loc.T("補助電源 …… 再起動　OK（鉱山の導線）", "Aux power ..... restart OK (mined wiring)")),
                new KeyValuePair<int, string>(3, Loc.T("燃料ライン …… 充填　OK（畑の実の燃料）", "Fuel line ..... filled OK (farm-grown fuel)")),
                new KeyValuePair<int, string>(2, Loc.T("冷却循環 …… 正常（魚の膜）", "Coolant loop .. nominal (fish membrane)")),
                new KeyValuePair<int, string>(6, Loc.T("位相安定 …… 正常値（生き物の協力）", "Phase stab. ... nominal (creature assist)")),
                new KeyValuePair<int, string>(1, Loc.T("船体外装 …… 気密　OK", "Hull ..... pressure OK")),
                new KeyValuePair<int, string>(4, Loc.T("主エンジン …… 点火", "Main engine ... IGNITION")),
                new KeyValuePair<int, string>(5, Loc.T("次元観測 …… 帰還経路を捕捉", "Observer ... route home ACQUIRED")),
            };
            foreach (var s in steps)
            {
                displayOverlay?.AddLine(s.Value);
                if (view != null)
                {
                    if (s.Key == 4) view.Ignite(5f);
                    else view.PlayTest(s.Key);
                }
                yield return new WaitForSeconds(s.Key == 4 ? 3f : 1.8f);
            }
            displayOverlay?.AddLine(Loc.T("全装置正常・帰還可能", "ALL SYSTEMS NOMINAL - RETURN AVAILABLE"), true);
            yield return new WaitForSeconds(2.5f);
            displayOverlay?.End();
            p?.UnlockInput("rocket.cutscene");
            cutscenePlaying = false;
            // 主人公の装置の表示（§2.1：主人公の言語でよい）
            UIManager.Open(new ShipMessageWindow(
                Loc.T("宇宙船の修理が完了しました。\n故郷の星への帰還が可能です。", "Spaceship repairs are complete.\nReturning to your home planet is now possible."),
                Loc.T("帰還はいつでも、宇宙船の前で選べます。それまで村での暮らしを続けられます。", "You can choose to return any time at the spaceship. Until then, life in the village goes on.")));
        }

        // ============================================================
        // 帰還・エンディング・クリア後（基本決定 §5）
        // ============================================================

        private void StartEnding()
        {
            var story = Game.Service<IStoryService>();
            if (story != null)
            {
                if (!story.IsEndingInProgress) story.BeginEnding();
                // エンディングが始まらなかった（別の演出中など）場合は、出発中のまま固まらないよう元に戻す
                if (!story.IsEndingInProgress && state.hasLaunched)
                {
                    state.hasLaunched = false;
                    Changed();
                }
                return;
            }
            // Story モジュールが無い構成：簡易エンディング（トースト → そのまま継続）
            if (view != null) view.StartCoroutine(FallbackEnding());
            else EventBus.Publish(new EndingFinishedEvent { Choice = PostEndingChoice.ReturnBeforeLaunch });
        }

        private IEnumerator FallbackEnding()
        {
            cutscenePlaying = true;
            var p = LocalPlayer;
            p?.LockInput("rocket.cutscene");
            UIManager.CloseAll();
            EventBus.Publish(new EndingStartedEvent());
            var finalDef = RocketTables.Get(RocketTables.FinalDeviceId);
            if (finalDef != null) GatherResidents(finalDef, true, p?.PlayerId, false);
            yield return new WaitForSeconds(3f);
            if (view != null) yield return view.LiftOff(6f, 60f);
            ScreenFader.FadeOut(1.2f);
            yield return new WaitForSeconds(1.4f);
            UIManager.Toast(Loc.T("最後の引っ越し ― 宇宙船は故郷へ帰っていった。", "The Last Move - the ship headed home."), 6f);
            if (view != null) view.Land();
            yield return new WaitForSeconds(1f);
            ScreenFader.FadeIn(1.2f);
            p?.UnlockInput("rocket.cutscene");
            cutscenePlaying = false;
            EventBus.Publish(new EndingFinishedEvent { Choice = PostEndingChoice.ReturnBeforeLaunch });
            UIManager.Toast(Loc.T("ロケットの前に戻ってきた。この星での暮らしは続く。", "You're back in front of the rocket. Life on this planet continues."), 6f);
        }

        private void OnEndingFinished(EndingFinishedEvent e)
        {
            if (e.Choice != PostEndingChoice.ReturnBeforeLaunch) return; // 次の星は Story が処理する
            if (IsAuthority)
            {
                // 出発直前の状態へ戻す。修理状態は保持（基本決定 §5-A）
                state.hasLaunched = false;
                AddHistory(Game.Instance != null ? Game.Instance.LocalPlayerId : null, "return", RocketTables.FinalDeviceId);
                Changed();
            }
            if (view != null) view.Land();
            var p = LocalPlayer;
            if (p != null && view != null && p.CurrentAreaId == AreaIds.Overworld)
            {
                p.Position = view.SitePoint(new Vector3(0f, 0.1f, 7f));
                var look = sitePosition - p.Position;
                look.y = 0f;
                if (p.Avatar != null && look.sqrMagnitude > 0.01f) p.Avatar.rotation = Quaternion.LookRotation(look, Vector3.up);
            }
            RefreshVisuals();
        }
    }
}
