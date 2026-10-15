using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Housing
{
    /// <summary>
    /// 家・家具・装飾システム（家具仕様 v0.2）。
    /// ・自宅（§2）：PlayerHouse の入口に扉を作り、屋外から離れた座標に家の中のエリアを生成する。
    /// ・家具カタログ（§4, §7, §8, §9, §17）、星のシードによる家具文化（§18）。
    /// ・配置・模様替え（§5）、家具を使う（§6）、生物の住処（§9）、庭（§10）、増築（§3）。
    /// ・住民の招待と訪問（§11）、家庭用マネピ（§13）、家具の思い出（§14）、住民の家（§15）。
    /// ・マルチの共同生活（§20）：配置は WorldCommand（CoLiving 以上）、家具の所有者、個人収納。
    /// 部分クラスに分けている（Scene / Commands / Visits / ResidentHomes / Creatures）。
    /// </summary>
    public sealed partial class HousingSystem : GameSystemBase, IHousingService
    {
        public override string SaveKey => "housing";
        public override int DailyUpdateOrder => 380;

        public const string AppId = "house";

        private HousingPlanetState state = new HousingPlanetState();
        private HousingProfileState profile = new HousingProfileState();
        private HousingKnowledgeState knowledge = new HousingKnowledgeState();
        private ulong planetSeed;
        private bool subscribed;
        private bool sleeping;
        private HousingToolHandler toolHandler;
        private HousingResidentProvider residentProvider;
        private HomeTabletApp tabletApp;
        private bool providerRegistered;

        public HousingPlanetState State => state;
        public string HouseOwnerId => !string.IsNullOrEmpty(state.houseOwnerId) ? state.houseOwnerId : (Game != null ? Game.LocalPlayerId : "");
        public string ManepiBoardId => "home:" + HouseOwnerId;
        public WorldLayout Layout => Game != null ? Game.Layout : null;
        public ulong Seed => planetSeed;
        public int Today => Clock != null ? Clock.Day : 1;

        // ------------------------------------------------------------
        // 起動・星の準備
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            FurnitureCatalog.RegisterAll();
            RegisterCommands();
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            planetSeed = ctx.Seed;
            FurnitureCatalog.PrepareCulture(ctx);
            state = new HousingPlanetState
            {
                houseOwnerId = ctx.Planet.hostPlayerId,
                gardenWidthCells = HousingConst.InitialGardenWidth,
                gardenDepthCells = HousingConst.InitialGardenDepth
            };
            knowledge = new HousingKnowledgeState();
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            CreateInitialHouse();
        }

        /// <summary>初期状態：小さな部屋、ベッド、収納箱、机と椅子（§2.1）。</summary>
        private void CreateInitialHouse()
        {
            state.rooms.Clear();
            state.furniture.Clear();
            state.storages.Clear();
            state.rooms.Add(new RoomData
            {
                index = 0,
                kind = RoomKind.Normal,
                widthCells = HousingConst.InitialRoomWidth,
                depthCells = HousingConst.InitialRoomDepth,
                wallpaperId = "",
                floorId = "",
                ownerId = ""
            });
            AddInitialFurniture(FurnitureCatalog.StarterBedId, 1, 7, 2);
            AddInitialFurniture(FurnitureCatalog.StarterChestId, 4, 10, 2);
            AddInitialFurniture(FurnitureCatalog.StarterDeskId, 9, 10, 2);
            AddInitialFurniture(FurnitureCatalog.StarterChairId, 10, 8, 0);
        }

        private void AddInitialFurniture(string itemId, int gx, int gz, int rot)
        {
            var def = ItemRegistry.Get(itemId);
            if (def == null) return;
            var spec = FurnitureCatalog.Resolve(def);
            if (!HouseGeometry.CanPlace(state, null, HousingConst.LocHouse, 0, spec, gx, gz, rot, null, out _, out _))
            {
                if (!HouseGeometry.FindFreeSpot(state, 0, spec, rot, out gx, out gz)) return;
            }
            var f = new PlacedFurniture
            {
                instanceId = Ids.New(),
                itemId = itemId,
                ownerId = HouseOwnerId,
                location = HousingConst.LocHouse,
                room = 0,
                gx = gx,
                gz = gz,
                rot = rot,
                acquiredDay = 1,
                placedDay = 1
            };
            AddMemory(f, "origin", "この家に最初からあった家具。", "Was in this house from the very start.");
            state.furniture.Add(f);
            if (spec.storageSlots > 0) GetOrCreateStorage(f, spec);
        }

        // ------------------------------------------------------------
        // シーン・プレイ開始・後片付け
        // ------------------------------------------------------------

        public override void BuildScene()
        {
            BuildSceneObjects();
        }

        public override void OnPlayStarted()
        {
            if (!subscribed)
            {
                EventBus.Subscribe<GiftGivenEvent>(OnGiftGiven);
                EventBus.Subscribe<ResidentPurchaseEvent>(OnResidentPurchase);
                EventBus.Subscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
                EventBus.Subscribe<ResidentMovedInEvent>(OnResidentMovedIn);
                EventBus.Subscribe<ResidentMoveOutDecidedEvent>(OnResidentMoveOutDecided);
                EventBus.Subscribe<ItemAddedEvent>(OnItemAdded);
                EventBus.Subscribe<ItemCraftedEvent>(OnItemCrafted);
                EventBus.Subscribe<AreaChangedEvent>(OnAreaChanged);
                EventBus.Subscribe<SleepStartedEvent>(OnSleepStarted);
                EventBus.Subscribe<SleepEndedEvent>(OnSleepEnded);
                EventBus.Subscribe<PlayerGestureEvent>(OnPlayerGesture);
                subscribed = true;
            }
            if (toolHandler == null) toolHandler = new HousingToolHandler(this);
            ToolUseRouter.Register(toolHandler);

            var dir = Game.Service<IResidentDirectory>();
            if (dir != null)
            {
                // 住民モジュールが星ごとに登録を消すか不明なため毎回登録する。
                // 二重登録された場合でも、同じフレーム・同じ住民への2回目以降は空を返す（HousingResidentProvider）。
                if (residentProvider == null) residentProvider = new HousingResidentProvider(this);
                dir.RegisterInteractionProvider(residentProvider);
                providerRegistered = true;
            }

            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                if (tabletApp == null) tabletApp = new HomeTabletApp(this);
                tablet.RegisterApp(tabletApp);
            }

            // 住民の家の扉は住民の割り当て後に作り直す（村仕様 §5.4）
            RebuildResidentDoors();
            SpawnHomeManepi();
            EnsureResidentHomes();
            visualsDirty = true;
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<GiftGivenEvent>(OnGiftGiven);
                EventBus.Unsubscribe<ResidentPurchaseEvent>(OnResidentPurchase);
                EventBus.Unsubscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
                EventBus.Unsubscribe<ResidentMovedInEvent>(OnResidentMovedIn);
                EventBus.Unsubscribe<ResidentMoveOutDecidedEvent>(OnResidentMoveOutDecided);
                EventBus.Unsubscribe<ItemAddedEvent>(OnItemAdded);
                EventBus.Unsubscribe<ItemCraftedEvent>(OnItemCrafted);
                EventBus.Unsubscribe<AreaChangedEvent>(OnAreaChanged);
                EventBus.Unsubscribe<SleepStartedEvent>(OnSleepStarted);
                EventBus.Unsubscribe<SleepEndedEvent>(OnSleepEnded);
                EventBus.Unsubscribe<PlayerGestureEvent>(OnPlayerGesture);
                subscribed = false;
            }
            if (toolHandler != null) ToolUseRouter.Unregister(toolHandler);
            ExitPlacementMode(false);
            StandUp();
            TeardownScene();
            sleeping = false;
            minuteAccumulator = 0;
            state = new HousingPlanetState();
            knowledge = new HousingKnowledgeState();
        }

        // ------------------------------------------------------------
        // 時間
        // ------------------------------------------------------------

        public override void Tick(float deltaTime)
        {
            if (visualsDirty)
            {
                visualsDirty = false;
                SyncVisuals();
            }
            TickPlacement(deltaTime);
            TickSitting();
            TickCutaway();
            TickSoundFloor();
            TickVisitors(deltaTime);
            TickResidentHomeProxy(deltaTime);
            TickCreatures(deltaTime);
        }

        private int minuteAccumulator;

        public override void OnGameMinutesAdvanced(int minutes)
        {
            if (!IsAuthority) return;
            minuteAccumulator += minutes;
            // 睡眠の時間送り（まとめて進む）でも、訪問の段取りは順に処理する
            while (minuteAccumulator >= 5)
            {
                minuteAccumulator -= 5;
                UpdateVisits();
            }
        }

        public override void OnHourChanged(int day, int hour)
        {
            refreshCreaturesSoon = true;
            if (IsAuthority) MaybeResidentGardenStroll(day, hour);
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            CompleteConstruction(day);
            ExpireInvitations(day);
            PlanSpontaneousVisits(day);
            CleanupResidentHomes();
        }

        // ------------------------------------------------------------
        // セーブ・同期
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(state);
                case SaveScope.PlayerProfile:
                    if (Game == null || playerId != Game.LocalPlayerId) return null;
                    return JsonUtility.ToJson(profile);
                case SaveScope.PlayerPlanetKnowledge:
                    if (Game == null || playerId != Game.LocalPlayerId) return null;
                    return JsonUtility.ToJson(knowledge);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                {
                    var s = JsonUtility.FromJson<HousingPlanetState>(json);
                    if (s == null) return;
                    Normalize(s);
                    state = s;
                    // ゲストはプレイ中にも呼ばれる：表示は Tick でまとめて作り直す（冪等）
                    visualsDirty = true;
                    break;
                }
                case SaveScope.PlayerProfile:
                {
                    var p = JsonUtility.FromJson<HousingProfileState>(json);
                    if (p != null)
                    {
                        if (p.pending == null) p.pending = new List<ProvenanceEntry>();
                        profile = p;
                    }
                    break;
                }
                case SaveScope.PlayerPlanetKnowledge:
                {
                    var k = JsonUtility.FromJson<HousingKnowledgeState>(json);
                    if (k != null)
                    {
                        if (k.seenResidentHomeItems == null) k.seenResidentHomeItems = new List<StringIntPair>();
                        knowledge = k;
                    }
                    break;
                }
            }
        }

        private static void Normalize(HousingPlanetState s)
        {
            if (s.rooms == null) s.rooms = new List<RoomData>();
            if (s.furniture == null) s.furniture = new List<PlacedFurniture>();
            if (s.storages == null) s.storages = new List<FurnitureStorageData>();
            if (s.orders == null) s.orders = new List<ConstructionOrder>();
            if (s.invitations == null) s.invitations = new List<Invitation>();
            if (s.residentHomes == null) s.residentHomes = new List<ResidentHomeData>();
            if (s.deliveries == null) s.deliveries = new List<ItemStack>();
            if (s.gardenWidthCells <= 0) s.gardenWidthCells = HousingConst.InitialGardenWidth;
            if (s.gardenDepthCells <= 0) s.gardenDepthCells = HousingConst.InitialGardenDepth;
            if (s.rooms.Count == 0)
                s.rooms.Add(new RoomData { index = 0, kind = RoomKind.Normal, widthCells = HousingConst.InitialRoomWidth, depthCells = HousingConst.InitialRoomDepth });
            foreach (var f in s.furniture)
            {
                if (f.displayed == null) f.displayed = new List<ItemStack>();
                if (f.memories == null) f.memories = new List<FurnitureMemory>();
            }
            foreach (var st in s.storages)
            {
                if (st.inventory == null) st.inventory = new Inventory();
                st.inventory.ownerId = "storage:" + st.instanceId;
                st.inventory.EnsureSlots();
            }
            foreach (var h in s.residentHomes) if (h.items == null) h.items = new List<ResidentHomeItem>();
            foreach (var o in s.orders) if (o.materials == null) o.materials = new List<StringIntPair>();
            foreach (var i in s.invitations) if (i.usedInstances == null) i.usedInstances = new List<string>();
        }

        // ------------------------------------------------------------
        // IHousingService
        // ------------------------------------------------------------

        public Vector3 PlayerHomeEntrance
        {
            get
            {
                var layout = Layout;
                var f = layout?.GetFacility(FacilityType.PlayerHouse);
                if (f != null) return f.entrance;
                return layout != null ? layout.PlayerSpawn : Vector3.zero;
            }
        }

        public Vector3 PlayerBedPosition
        {
            get
            {
                foreach (var f in state.furniture)
                {
                    if (f.location != HousingConst.LocHouse) continue;
                    var spec = HouseGeometry.Resolve(f);
                    if (spec.use != FurnitureUse.Sleep) continue;
                    return HouseGeometry.FurniturePosition(state, Layout, f, spec, out _);
                }
                return HouseInteriorSpawn;
            }
        }

        public void AddFurnitureToResidentHome(string residentId, string itemId)
        {
            if (string.IsNullOrEmpty(residentId) || string.IsNullOrEmpty(itemId)) return;
            if (!IsAuthority) return;
            var home = GetOrCreateResidentHome(residentId);
            // 住民の購入・贈り物イベントから自動で入れた物があれば、それを確定扱いにする（重複させない）
            foreach (var it in home.items)
            {
                if (it.autoFromEvent && !it.claimed && it.itemId == itemId && Today - it.day <= 1)
                {
                    it.claimed = true;
                    MarkDirty();
                    return;
                }
            }
            home.items.Add(new ResidentHomeItem { itemId = itemId, day = Today, claimed = true });
            MarkDirty();
            OnResidentHomeChanged(residentId);
        }

        public IEnumerable<string> GetResidentHomeFurniture(string residentId)
        {
            var list = new List<string>();
            foreach (var b in BaseResidentFurniture(residentId)) list.Add(b);
            var home = FindResidentHome(residentId);
            if (home != null) foreach (var it in home.items) list.Add(it.itemId);
            return list;
        }

        public IEnumerable<PlacedFurnitureView> GetPlayerHomeFurniture(string ownerId)
        {
            var list = new List<PlacedFurnitureView>();
            foreach (var f in state.furniture)
            {
                if (f.location != HousingConst.LocHouse && f.location != HousingConst.LocGarden) continue;
                // 家は共同生活で1軒を共有するため、どの持ち主IDでも家全体の家具を返す（各家具の OwnerId で持ち主が分かる）
                var spec = HouseGeometry.Resolve(f);
                var pos = HouseGeometry.FurniturePosition(state, Layout, f, spec, out float yaw);
                list.Add(new PlacedFurnitureView
                {
                    InstanceId = f.instanceId,
                    ItemId = f.itemId,
                    OwnerId = f.ownerId,
                    Location = f.location,
                    Position = pos,
                    RotationY = yaw,
                    ResidentCreatureUid = f.creatureUid
                });
            }
            return list;
        }

        public void RecordFurnitureMemory(string furnitureInstanceId, string memoryJa, string memoryEn)
        {
            var f = FindFurniture(furnitureInstanceId);
            if (f == null) return;
            if (!IsAuthority) return;
            AddMemory(f, "event", memoryJa, memoryEn);
            MarkDirty();
        }

        // ------------------------------------------------------------
        // 共通の小道具
        // ------------------------------------------------------------

        public PlacedFurniture FindFurniture(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            foreach (var f in state.furniture) if (f.instanceId == instanceId) return f;
            return null;
        }

        public FurnitureStorageData FindStorage(string instanceId)
        {
            foreach (var s in state.storages) if (s.instanceId == instanceId) return s;
            return null;
        }

        private FurnitureStorageData GetOrCreateStorage(PlacedFurniture f, FurnitureSpec spec)
        {
            var s = FindStorage(f.instanceId);
            if (s != null) return s;
            s = new FurnitureStorageData { instanceId = f.instanceId, inventory = new Inventory(Mathf.Max(1, spec.storageSlots), "storage:" + f.instanceId) };
            state.storages.Add(s);
            return s;
        }

        /// <summary>家具に思い出を1件足す（自動の記録は上限を超えたら古い物から消す。印付き・メモ・入手元は残す）。</summary>
        public FurnitureMemory AddMemory(PlacedFurniture f, string kind, string ja, string en, string residentId = null, string playerId = null, string recordId = null)
        {
            var m = new FurnitureMemory
            {
                day = Clock != null ? Clock.Day : 1,
                minute = Clock != null ? Clock.MinuteOfDay : 0,
                kind = kind,
                ja = ja,
                en = en,
                residentId = residentId,
                playerId = playerId,
                recordId = recordId
            };
            f.memories.Add(m);
            int auto = 0;
            foreach (var x in f.memories) if (IsAutoMemory(x)) auto++;
            for (int i = 0; i < f.memories.Count && auto > HousingConst.MaxAutoMemoriesPerFurniture; i++)
            {
                if (!IsAutoMemory(f.memories[i])) continue;
                f.memories.RemoveAt(i);
                i--;
                auto--;
            }
            return m;
        }

        private static bool IsAutoMemory(FurnitureMemory m) => !m.pinned && m.kind != "note" && m.kind != "origin";

        public string ResidentName(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return "?";
            var speech = Game?.Service<ISpeechService>();
            string n = null;
            try { n = speech?.ResidentName(residentId); } catch (Exception e) { Debug.LogException(e); }
            if (!string.IsNullOrEmpty(n)) return n;
            return Loc.T("住民", "resident") + "#" + residentId.Substring(0, Mathf.Min(4, residentId.Length));
        }

        public string PlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "?";
            var p = Game?.Players.Get(playerId);
            if (p != null) return p.DisplayName;
            var net = Game?.Service<INetworkService>();
            var n = net?.GetPlayerName(playerId);
            return string.IsNullOrEmpty(n) ? Loc.T("旅人", "Traveler") : n;
        }

        public static string RoomName(RoomData r)
        {
            if (r == null) return "";
            string kind;
            switch (r.kind)
            {
                case RoomKind.Glass: kind = Loc.T("ガラスの部屋", "Glass room"); break;
                case RoomKind.Waterside: kind = Loc.T("水辺の部屋", "Waterside room"); break;
                case RoomKind.Terrace: kind = Loc.T("テラス", "Terrace"); break;
                case RoomKind.Loft: kind = Loc.T("ロフト", "Loft"); break;
                case RoomKind.Basement: kind = Loc.T("地下室", "Basement"); break;
                default: kind = Loc.T("部屋", "Room"); break;
            }
            return r.index == 0 ? Loc.T("最初の部屋", "First room") : kind + " " + (r.index + 1);
        }

        public static string RoomNameJa(RoomData r)
        {
            if (r == null) return "";
            if (r.index == 0) return "最初の部屋";
            switch (r.kind)
            {
                case RoomKind.Glass: return "ガラスの部屋";
                case RoomKind.Waterside: return "水辺の部屋";
                case RoomKind.Terrace: return "テラス";
                case RoomKind.Loft: return "ロフト";
                case RoomKind.Basement: return "地下室";
                default: return "部屋" + (r.index + 1);
            }
        }

        public static string RoomNameEn(RoomData r)
        {
            if (r == null) return "";
            if (r.index == 0) return "the first room";
            return r.kind == RoomKind.Normal ? "room " + (r.index + 1) : r.kind.ToString().ToLowerInvariant();
        }

        /// <summary>この星で家の中にいるプレイヤーがいるか。</summary>
        public bool AnyPlayerInHouse()
        {
            if (Game == null) return false;
            foreach (var p in Game.Players.All) if (p.CurrentAreaId == HousingConst.LocHouse) return true;
            return false;
        }

        public bool PlayerInHouse(string playerId)
        {
            var p = Game?.Players.Get(playerId);
            return p != null && p.CurrentAreaId == HousingConst.LocHouse;
        }

        /// <summary>そのプレイヤーが家を編集できるか（§20：共同生活以上）。</summary>
        public bool CanEdit(PlayerState p)
        {
            if (p == null) return false;
            if (!WorldAuthority.IsMultiplayer) return true;
            return p.Permission >= PermissionLevel.CoLiving;
        }

        // ------------------------------------------------------------
        // 入手元の記録（§14 入手元・贈り主）
        // ------------------------------------------------------------

        public void AddProvenance(string itemId, string kind, string residentId)
        {
            var def = ItemRegistry.Get(itemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return;
            profile.pending.Add(new ProvenanceEntry { itemId = itemId, kind = kind, residentId = residentId, day = Today });
            while (profile.pending.Count > 60) profile.pending.RemoveAt(0);
        }

        public ProvenanceEntry TakeProvenance(string itemId)
        {
            for (int i = profile.pending.Count - 1; i >= 0; i--)
            {
                if (profile.pending[i].itemId != itemId) continue;
                var p = profile.pending[i];
                profile.pending.RemoveAt(i);
                return p;
            }
            return null;
        }

        private void OnItemAdded(ItemAddedEvent e)
        {
            if (Game == null || e.Stack == null || e.PlayerId != Game.LocalPlayerId) return;
            var def = ItemRegistry.Get(e.Stack.itemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return;
            if (!string.IsNullOrEmpty(e.Stack.data) && e.Stack.data.StartsWith("furn:")) return;
            string src = e.Source ?? "";
            if (src == "housing" || src == "vendor" || src == "pickup" || src.StartsWith("housing")) return;
            // Source に住民IDが含まれていれば「その住民からもらった」とみなす（"gift:<id>" "quest:<id>" など）
            var dir = Game.Service<IResidentDirectory>();
            string resident = null;
            if (dir != null)
            {
                foreach (var token in src.Split(':', '/', ' ', ',', '|'))
                {
                    if (token.Length < 4) continue;
                    if (dir.Get(token) != null) { resident = token; break; }
                }
            }
            string kind = src.StartsWith("quest") ? "quest" : resident != null ? "gift" : src.StartsWith("shop") ? "shop" : null;
            if (kind == null) return;
            AddProvenance(def.Id, kind, resident);
        }

        private void OnItemCrafted(ItemCraftedEvent e)
        {
            if (Game == null || e.PlayerId != Game.LocalPlayerId) return;
            var def = ItemRegistry.Get(e.ItemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return;
            if (e.Station == CraftStationType.CarpenterBench)
            {
                var carpenter = Game.Service<IResidentDirectory>()?.FindByJob(ResidentJob.Carpenter);
                AddProvenance(def.Id, "commission", carpenter?.Id);
            }
            else AddProvenance(def.Id, "craft", null);
        }

        /// <summary>入手元の思い出の文。</summary>
        public void DescribeOrigin(string kind, string residentId, int day, out string ja, out string en)
        {
            string name = residentId != null ? ResidentName(residentId) : null;
            switch (kind)
            {
                case "gift":
                    ja = string.Format("{0}日目、{1}からもらった。", day, name);
                    en = string.Format("A gift from {1} on day {0}.", day, name);
                    break;
                case "carpenter":
                    ja = string.Format("{0}日目、家具職人{1}から買った。", day, name != null ? "の" + name : "");
                    en = string.Format("Bought from the carpenter{1} on day {0}.", day, name != null ? " " + name : "");
                    break;
                case "commission":
                    ja = string.Format("{0}日目、家具職人{1}に作ってもらった。", day, name != null ? "の" + name : "");
                    en = string.Format("Made to order by the carpenter{1} on day {0}.", day, name != null ? " " + name : "");
                    break;
                case "craft":
                    ja = string.Format("{0}日目、自分で作った。", day);
                    en = string.Format("Made it myself on day {0}.", day);
                    break;
                case "quest":
                    ja = string.Format("{0}日目、お願いを手伝ったお礼{1}。", day, name != null ? "（" + name + "）" : "");
                    en = string.Format("A thank-you for helping{1}, day {0}.", day, name != null ? " " + name : "");
                    break;
                case "shop":
                    ja = string.Format("{0}日目に手に入れた。", day);
                    en = string.Format("Obtained on day {0}.", day);
                    break;
                default:
                    ja = string.Format("{0}日目に手に入れた。", day);
                    en = string.Format("Obtained on day {0}.", day);
                    break;
            }
        }

        // ------------------------------------------------------------
        // イベント
        // ------------------------------------------------------------

        private void OnSleepStarted(SleepStartedEvent e) { sleeping = true; }
        private void OnSleepEnded(SleepEndedEvent e) { sleeping = false; }

        private void OnAreaChanged(AreaChangedEvent e)
        {
            var local = LocalPlayer;
            if (local == null || e.PlayerId != local.PlayerId) return;
            bool ours = e.ToAreaId == HousingConst.LocHouse || e.FromAreaId == HousingConst.LocHouse || IsResidentHomeArea(e.ToAreaId) || IsResidentHomeArea(e.FromAreaId);
            if (ours) CameraRig.Instance?.SnapToTarget();
            if (e.ToAreaId == HousingConst.LocHouse)
            {
                refreshCreaturesSoon = true;
                NotifyHomeMessages();
            }
            if (e.FromAreaId != null && e.FromAreaId != e.ToAreaId && IsResidentHomeArea(e.FromAreaId)) DestroyResidentInterior();
            if (e.ToAreaId != HousingConst.LocHouse && placementActive) ExitPlacementMode(false);
        }

        private void NotifyHomeMessages()
        {
            var manepi = Game?.Service<IManepiService>();
            if (manepi == null) return;
            IReadOnlyList<UtteranceRecord> msgs = null;
            try { msgs = manepi.GetMessages(ManepiBoardId); } catch (Exception ex) { Debug.LogException(ex); }
            int n = msgs?.Count ?? 0;
            if (n > knowledge.seenHomeMessages)
            {
                UIManager.Toast(Loc.T("留守番マネピが、何か覚えているようだ。", "The home Manepi seems to remember something."));
                knowledge.seenHomeMessages = n;
            }
            else if (n < knowledge.seenHomeMessages) knowledge.seenHomeMessages = n;
        }
    }

    /// <summary>持ち物の家具を「使う」と配置モードに入る（ToolUseRouter）。</summary>
    public sealed class HousingToolHandler : IToolHandler
    {
        private readonly HousingSystem sys;
        public HousingToolHandler(HousingSystem sys) { this.sys = sys; }
        public int Priority => 40;

        public bool TryUseTool(ToolUseContext ctx)
        {
            if (ctx?.SelectedItem == null || ctx.Player == null || !ctx.Player.IsLocal) return false;
            var def = ctx.SelectedItem.Definition;
            if (FurnitureCatalog.IsSurfaceDecor(def, true) || FurnitureCatalog.IsSurfaceDecor(def, false))
            {
                if (ctx.AreaId != HousingConst.LocHouse) return false;
                sys.StartPlacementMode(-1);
                return true;
            }
            if (!FurnitureCatalog.IsPlaceable(def)) return false;
            if (ctx.AreaId == HousingConst.LocHouse || sys.IsInGarden(ctx.Player.Position, ctx.AreaId))
            {
                sys.StartPlacementMode(ctx.SelectedSlot);
                return true;
            }
            return false;
        }
    }
}
