using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Housing
{
    /// <summary>住民の家の扉（World は外観だけ作る。入口はこのモジュールが置く。村仕様 §5.4）。</summary>
    public class ResidentDoor : MonoBehaviour, IInteractable
    {
        public HousingSystem System;
        public string FacilityId;
        public int Index;

        public string GetInteractionPrompt(PlayerState player) => System?.ResidentDoorPrompt(this);
        public bool CanInteract(PlayerState player) => System != null;
        public void Interact(PlayerState player) => System?.EnterResidentHome(this, player);
    }

    // 住民の家：決定的な間取り＋住民が買った・もらった家具（家具仕様 §15、住民生活仕様 §8、村仕様 §5.2〜§5.4）。
    public sealed partial class HousingSystem
    {
        private Transform residentDoorsRoot;
        private Transform residentInteriorRoot;
        private string residentInteriorResident;
        private string residentInteriorFacility;
        private int residentInteriorIndex;
        private ResidentProxy residentHomeProxy;
        private float residentProxyTimer;
        private readonly List<FurnitureObject> residentInteriorFurniture = new List<FurnitureObject>();
        private readonly List<WallSeg> residentInteriorWalls = new List<WallSeg>();
        private readonly HashSet<string> residentObservedToday = new HashSet<string>();
        private const int MaxResidentHomeItems = 20;

        // ------------------------------------------------------------
        // データ
        // ------------------------------------------------------------

        public ResidentHomeData FindResidentHome(string residentId)
        {
            foreach (var h in state.residentHomes) if (h.residentId == residentId) return h;
            return null;
        }

        private ResidentHomeData GetOrCreateResidentHome(string residentId)
        {
            var h = FindResidentHome(residentId);
            if (h != null) return h;
            h = new ResidentHomeData { residentId = residentId };
            state.residentHomes.Add(h);
            return h;
        }

        private void AddResidentHomeItem(string residentId, ResidentHomeItem item)
        {
            var home = GetOrCreateResidentHome(residentId);
            home.items.Add(item);
            while (home.items.Count > MaxResidentHomeItems) home.items.RemoveAt(0);
            MarkDirty();
            OnResidentHomeChanged(residentId);
        }

        private void EnsureResidentHomes()
        {
            if (!IsAuthority) return;
            var dir = Game?.Service<IResidentDirectory>();
            if (dir == null) return;
            foreach (var r in dir.ActiveResidents) GetOrCreateResidentHome(r.Id);
        }

        private void CleanupResidentHomes()
        {
            var dir = Game?.Service<IResidentDirectory>();
            if (dir == null) return;
            for (int i = state.residentHomes.Count - 1; i >= 0; i--)
            {
                var r = dir.Get(state.residentHomes[i].residentId);
                if (r == null || !r.IsActive) state.residentHomes.RemoveAt(i);
            }
        }

        /// <summary>住民の家の初期家具（シードと住民の職業・系統から決定的に決まる）。</summary>
        public List<string> BaseResidentFurniture(string residentId)
        {
            var list = new List<string>();
            var info = Game?.Service<IResidentDirectory>()?.Get(residentId);
            var rng = SeededRandom.For(planetSeed, "housing.residenthome." + residentId);
            var lineage = info != null ? info.Lineage : AlienLineage.Round;
            var job = info != null ? info.Job : ResidentJob.None;

            // 寝床（体型で選ぶ）
            switch (lineage)
            {
                case AlienLineage.Round: list.Add("furniture.manmaru_bed"); break;
                case AlienLineage.Floating: list.Add(rng.Chance(0.5f) ? "furniture.floating_bed" : "furniture.wood_bed"); break;
                case AlienLineage.Plush: list.Add("furniture.trad_plush_basket"); break;
                case AlienLineage.ChibiAnimal: list.Add("furniture.trad_low_cushion"); break;
                case AlienLineage.StarChild: list.Add("furniture.starry_bed"); break;
                default: list.Add(rng.Chance(0.5f) ? "furniture.wood_bed" : FurnitureCatalog.StarterBedId); break;
            }
            // 食卓と椅子
            list.Add(lineage == AlienLineage.StarChild ? "furniture.trad_star_chart_table" : lineage == AlienLineage.Round || lineage == AlienLineage.Mochi ? "furniture.manmaru_table" : "furniture.wood_table");
            string seat = null;
            foreach (var def in FurnitureCatalog.OwnFurniture)
            {
                var spec = (FurnitureSpec)def.ModuleData;
                if (spec.lineage == (int)lineage && spec.use == FurnitureUse.Sit) { seat = def.Id; break; }
            }
            list.Add(seat ?? (rng.Chance(0.5f) ? "furniture.wood_chair" : "furniture.log_stool"));
            if (rng.Chance(0.5f)) list.Add("furniture.wood_chair");
            // 収納・灯り
            list.Add(rng.Chance(0.4f) ? "furniture.old_wardrobe" : rng.Chance(0.5f) ? "furniture.cupboard" : FurnitureCatalog.StarterChestId);
            list.Add(rng.Chance(0.5f) ? "furniture.manmaru_lamp" : "furniture.moon_lamp");
            // 系統にちなむ家具（§8）
            foreach (var def in FurnitureCatalog.OwnFurniture)
            {
                var spec = (FurnitureSpec)def.ModuleData;
                if (spec.lineage == (int)lineage && spec.use != FurnitureUse.Sit && !list.Contains(def.Id)) { list.Add(def.Id); break; }
            }
            // 職業・趣味（住民生活仕様 §8：釣り好きの部屋、花好きの部屋…）
            switch (job)
            {
                case ResidentJob.FishingMaster: list.Add("furniture.fishing_rack"); list.Add("furniture.small_aquarium"); list.Add("furniture.fish_print"); break;
                case ResidentJob.FarmingMaster:
                case ResidentJob.Florist: list.Add("furniture.potted_plant"); list.Add("furniture.big_plant"); list.Add("furniture.potted_plant"); break;
                case ResidentJob.CreatureExpert: list.Add("furniture.fluffy_nest"); list.Add("furniture.hakone_box"); list.Add("furniture.lampuru_light"); break;
                case ResidentJob.Carpenter:
                case ResidentJob.Builder:
                case ResidentJob.Mechanic: list.Add("furniture.workbench"); list.Add("furniture.wood_shelf"); break;
                case ResidentJob.Cook: list.Add("furniture.cupboard"); list.Add("furniture.flower_vase"); break;
                case ResidentJob.Musician: list.Add(lineage == AlienLineage.MultiArm ? "furniture.trad_multi_organ" : rng.Chance(0.5f) ? "furniture.harp" : "furniture.xylophone"); break;
                case ResidentJob.Artist: list.Add("furniture.landscape_painting"); list.Add("furniture.fish_print"); break;
                case ResidentJob.Scholar:
                case ResidentJob.Teacher: list.Add("furniture.antique_bookshelf"); list.Add("furniture.antique_desk"); break;
                case ResidentJob.Child: list.Add("furniture.toy_ball"); list.Add("furniture.toy_blocks"); break;
                case ResidentJob.Miner:
                case ResidentJob.Blacksmith: list.Add("furniture.fossil_stand"); list.Add("furniture.crystal_pedestal"); break;
                case ResidentJob.WeatherReader: list.Add("furniture.weather_window"); break;
                case ResidentJob.Merchant: list.Add("furniture.wood_shelf"); list.Add("furniture.figurine"); break;
                case ResidentJob.Retired: list.Add("furniture.rocking_chair"); list.Add("furniture.pendulum_clock"); break;
                case ResidentJob.Traveler: list.Add("furniture.landscape_painting"); list.Add("furniture.photo_frame"); break;
                case ResidentJob.Doctor: list.Add("furniture.big_plant"); list.Add("furniture.antique_bookshelf"); break;
                default: list.Add("furniture.potted_plant"); break;
            }
            if (rng.Chance(0.5f)) list.Add(rng.Chance(0.5f) ? "furniture.knit_rug" : "furniture.constellation_rug");
            list.RemoveAll(id => !ItemRegistry.Exists(id));
            return list;
        }

        // ------------------------------------------------------------
        // 扉
        // ------------------------------------------------------------

        private void RebuildResidentDoors()
        {
            if (residentDoorsRoot == null || Layout == null) return;
            foreach (Transform c in residentDoorsRoot) UnityEngine.Object.Destroy(c.gameObject);
            int index = 0;
            foreach (var f in Layout.GetFacilities(FacilityType.ResidentHouse))
            {
                var pos = Layout.OnGround(f.entrance);
                var go = PrimitiveBuilder.Empty("ResidentDoor_" + f.id, residentDoorsRoot, pos);
                go.transform.rotation = Quaternion.Euler(0, f.rotationY, 0);
                PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 0.012f, 0.2f), new Vector3(1.1f, 0.02f, 0.6f), new Color(0.7f, 0.55f, 0.4f), "Doormat");
                // 当たりは建物の壁側へ寄せる（家の前＝入口の少し先で過ごす住民と [E] を取り合わないように）
                PrimitiveBuilder.AddBoxCollider(go, new Vector3(0, 1.1f, -0.5f), new Vector3(1.6f, 2.2f, 1.0f), true);
                var door = go.AddComponent<ResidentDoor>();
                door.System = this;
                door.FacilityId = f.id;
                door.Index = index++;
            }
        }

        private IResidentInfo OwnerOf(FacilityInfo f)
        {
            var dir = Game?.Service<IResidentDirectory>();
            if (dir == null || f == null) return null;
            if (!string.IsNullOrEmpty(f.ownerResidentId))
            {
                var r = dir.Get(f.ownerResidentId);
                if (r != null && r.IsActive) return r;
            }
            foreach (var r in dir.ActiveResidents) if (r.HomeFacilityId == f.id) return r;
            return null;
        }

        public string ResidentDoorPrompt(ResidentDoor door)
        {
            var f = Layout?.GetFacilityById(door.FacilityId);
            var r = OwnerOf(f);
            if (r == null) return Loc.T("空き家をのぞく", "Peek into the empty house");
            return Loc.Format("{0}の家を訪ねる", "Visit {0}'s home", ResidentName(r.Id));
        }

        private static bool IsAtHomeActivity(ResidentActivity a)
        {
            switch (a)
            {
                case ResidentActivity.AtHome:
                case ResidentActivity.Resting:
                case ResidentActivity.Cooking:
                case ResidentActivity.Packing:
                case ResidentActivity.Crafting:
                case ResidentActivity.Playing:
                case ResidentActivity.Eating:
                case ResidentActivity.ShelteringFromRain:
                    return true;
            }
            return false;
        }

        public bool IsResidentHomeArea(string areaId) => areaId != null && areaId.StartsWith("house:") && areaId != HousingConst.LocHouse;

        public void EnterResidentHome(ResidentDoor door, PlayerState player)
        {
            if (player == null || Layout == null) return;
            var f = Layout.GetFacilityById(door.FacilityId);
            if (f == null) return;
            var r = OwnerOf(f);
            if (r != null)
            {
                if (r.Activity == ResidentActivity.Sleeping)
                {
                    UIManager.Toast(Loc.T("トントン……返事がない。眠っているようだ。", "Knock knock... no answer. They seem to be asleep."));
                    return;
                }
                bool home = IsAtHomeActivity(r.Activity) || (r.Position - f.position).sqrMagnitude < 64f;
                if (!home)
                {
                    UIManager.Toast(Loc.T("トントン……留守のようだ。", "Knock knock... nobody's home."));
                    return;
                }
            }
            string areaId = r != null ? AreaIds.ResidentHouse(r.Id) : "house:vacant:" + f.id;
            var spawn = BuildResidentInterior(f, door.Index, r);
            Game.Areas.Register(new AreaDescriptor
            {
                Id = areaId,
                NameJa = r != null ? ResidentName(r.Id) + "の家" : "空き家",
                NameEn = r != null ? ResidentName(r.Id) + "'s home" : "Empty house",
                IsIndoor = true,
                ShowWeather = false,
                DefaultSpawn = spawn,
                AmbientOverride = new Color(0.75f, 0.7f, 0.65f)
            });
            Game.Areas.Transition(player, areaId, spawn, 0f);
            CameraRig.Instance?.SnapToTarget();
            if (r == null)
            {
                UIManager.Toast(Loc.T("がらんとしている。新しい住民が来るのを待っているようだ。", "It's empty, waiting for a new resident."));
                return;
            }
            OnVisitedResidentHome(r, player);
        }

        private void OnVisitedResidentHome(IResidentInfo r, PlayerState player)
        {
            var dir = Game.Service<IResidentDirectory>();
            var tablet = Game.Service<ITabletService>();
            string key = r.Id + ":" + Today;
            if (!residentObservedToday.Contains("visit:" + key))
            {
                residentObservedToday.Add("visit:" + key);
                try
                {
                    dir?.AddMemory(r.Id, new ResidentMemoryEntry { kind = "player_visited_home", playerId = player.PlayerId, day = Today, importance = 0.4f });
                    dir?.ChangeFriendship(r.Id, player.PlayerId, 1, "housing.home_visit");
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            // 増えていた家具を住民辞典に事実として残す（好みの推測の手掛かり §5.4）
            var home = FindResidentHome(r.Id);
            int count = home != null ? home.items.Count : 0;
            int seen = 0;
            StringIntPair entry = null;
            foreach (var p in knowledge.seenResidentHomeItems) if (p.key == r.Id) { entry = p; seen = p.value; }
            if (entry == null)
            {
                entry = new StringIntPair(r.Id, 0);
                knowledge.seenResidentHomeItems.Add(entry);
                // 初めて：目立つ家具を3つまで記録
                var baseItems = BaseResidentFurniture(r.Id);
                int n = 0;
                foreach (var id in baseItems)
                {
                    var def = ItemRegistry.Get(id);
                    if (def == null || n >= 3) continue;
                    var spec = FurnitureCatalog.Resolve(def);
                    if (spec.use == FurnitureUse.Sleep || spec.use == FurnitureUse.Storage) continue;
                    RecordObservation(tablet, r.Id, string.Format("{0}日目：家に{1}があった。", Today, def.NameJa), string.Format("Day {0}: there was a {1} in their home.", Today, def.NameEn));
                    n++;
                }
            }
            if (home != null && count > seen)
            {
                for (int i = Mathf.Max(seen, count - 3); i < count; i++)
                {
                    var def = ItemRegistry.Get(home.items[i].itemId);
                    if (def == null) continue;
                    if (home.items[i].fromPlayerGift && home.items[i].giftPlayerId == player.PlayerId)
                        RecordObservation(tablet, r.Id, string.Format("{0}日目：私が贈った{1}が家に置いてあった。", Today, def.NameJa), string.Format("Day {0}: the {1} I gave was in their home.", Today, def.NameEn));
                    else
                        RecordObservation(tablet, r.Id, string.Format("{0}日目：家に{1}が増えていた。", Today, def.NameJa), string.Format("Day {0}: a new {1} had appeared in their home.", Today, def.NameEn));
                }
            }
            entry.value = count;
            if (home != null && home.packing)
                RecordObservation(tablet, r.Id, string.Format("{0}日目：家の中に箱がたくさん積まれていた。", Today), string.Format("Day {0}: many boxes were piled up in their home.", Today));
        }

        private static void RecordObservation(ITabletService tablet, string residentId, string ja, string en)
        {
            if (tablet == null) return;
            try { tablet.RecordResidentObservation(residentId, ja, en); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------
        // 室内（入った時だけ作り、出たら壊す）
        // ------------------------------------------------------------

        private Vector3 BuildResidentInterior(FacilityInfo facility, int index, IResidentInfo r)
        {
            DestroyResidentInterior();
            residentInteriorRoot = PrimitiveBuilder.Empty("ResidentInterior", housingRoot, Vector3.zero).transform;
            residentInteriorFacility = facility.id;
            residentInteriorResident = r?.Id;
            residentInteriorIndex = index;
            var origin = HousingConst.ResidentHomesOrigin + new Vector3(index * HousingConst.ResidentHomeSpacing, 0f, 0f);
            var key = r != null ? r.Id : "vacant." + facility.id;
            var rng = SeededRandom.For(planetSeed, "housing.residentroom." + key);

            var temp = new HousingPlanetState { useCustomOrigin = true, customOrigin = origin };
            var room = new RoomData
            {
                index = 0,
                kind = RoomKind.Normal,
                widthCells = rng.Range(14, 19),
                depthCells = rng.Range(11, 15),
                wallpaperId = "",
                floorId = ""
            };
            // 星の家具文化と住民の系統で内装を変える（§18 住宅の内装）
            var walls = new List<ItemDefinition>();
            var floors = new List<ItemDefinition>();
            foreach (var d in FurnitureCatalog.OwnDecor) (FurnitureCatalog.IsSurfaceDecor(d, true) ? walls : floors).Add(d);
            if (r != null && rng.Chance(0.75f))
            {
                room.wallpaperId = walls[rng.Range(0, walls.Count)].Id;
                room.floorId = floors[rng.Range(0, floors.Count)].Id;
                if (r.Lineage == AlienLineage.StarChild) room.wallpaperId = "decor.wall_starry";
                if (r.Job == ResidentJob.Florist) room.wallpaperId = "decor.wall_flower";
            }
            temp.rooms.Add(room);

            // 壁と床（自宅と同じ組み立てを、一時的に状態を差し替えて使う）
            var savedState = state;
            var savedShell = shellRoot;
            int wallStart = this.walls.Count;
            try
            {
                state = temp;
                shellRoot = residentInteriorRoot;
                BuildRoom(room);
            }
            finally
            {
                state = savedState;
                shellRoot = savedShell;
            }
            for (int i = wallStart; i < this.walls.Count; i++) residentInteriorWalls.Add(this.walls[i]);
            this.walls.RemoveRange(wallStart, this.walls.Count - wallStart);
            lastCutawayKey = int.MinValue;

            float W = room.widthCells * HousingConst.Cell, D = room.depthCells * HousingConst.Cell;
            PrimitiveBuilder.Create(PrimitiveType.Cube, residentInteriorRoot, origin + new Vector3(W * 0.5f, -0.35f, D * 0.5f), new Vector3(W + 30f, 0.2f, D + 30f), new Color(0.45f, 0.62f, 0.38f), "Yard");

            // 出口
            var exitRot = Quaternion.Euler(0, facility.rotationY, 0);
            var exitPos = Layout.OnGround(facility.entrance + exitRot * Vector3.forward * 1.0f) + Vector3.up * 0.2f;
            AreaPortal.Create(residentInteriorRoot, origin + new Vector3(W * 0.5f, 1.1f, -0.12f), new Vector3(1.3f, 2.2f, 0.1f), AreaIds.Overworld, exitPos, facility.rotationY, "外に出る", "Go outside", DoorColor);

            // 家具
            var ids = new List<KeyValuePair<string, ResidentHomeItem>>();
            if (r != null)
            {
                foreach (var id in BaseResidentFurniture(r.Id)) ids.Add(new KeyValuePair<string, ResidentHomeItem>(id, null));
                var home = FindResidentHome(r.Id);
                if (home != null) foreach (var it in home.items) ids.Add(new KeyValuePair<string, ResidentHomeItem>(it.itemId, it));
            }
            var homeData = r != null ? FindResidentHome(r.Id) : null;
            bool packing = homeData != null && homeData.packing || (r != null && r.Activity == ResidentActivity.Packing);
            bool tidy = rng.Chance(0.5f); // 几帳面／おおらか（家具の並べ方の違い）
            int k = 0;
            foreach (var kv in ids)
            {
                k++;
                if (packing && k % 2 == 0) continue; // 荷造り中：家具が片付いていく
                var def = ItemRegistry.Get(kv.Key);
                if (def == null) continue;
                var spec = FurnitureCatalog.Resolve(def);
                int rot = spec.surface == FurnitureSurface.Wall ? 2 : tidy ? 2 : rng.Range(0, 4);
                if (!TryPlaceTemp(temp, def, spec, rot, rng, out var pf)) continue;
                var pos = HouseGeometry.FurniturePosition(temp, null, pf, spec, out float yaw);
                var res = FurnitureVisuals.Build(def, spec, residentInteriorRoot, pos, yaw, "ResFurniture_" + def.Id);
                var fo = res.Root.AddComponent<FurnitureObject>();
                fo.System = this;
                fo.InstanceId = "res:" + key + ":" + k;
                fo.ItemId = def.Id;
                fo.ResidentHomeOwner = r != null ? r.Id : "vacant";
                fo.Visual = res;
                residentInteriorFurniture.Add(fo);
            }
            if (packing)
            {
                // 段ボール箱（引っ越しの準備 §15.2）
                var boxDef = ItemRegistry.Get("furniture.hakone_box");
                for (int i = 0; i < 6 && boxDef != null; i++)
                {
                    var spec = FurnitureCatalog.Resolve(boxDef);
                    if (!TryPlaceTemp(temp, boxDef, spec, 0, rng, out var pf)) break;
                    var pos = HouseGeometry.FurniturePosition(temp, null, pf, spec, out float yaw);
                    var box = PrimitiveBuilder.Create(PrimitiveType.Cube, residentInteriorRoot, pos + new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f), new Color(0.82f, 0.66f, 0.45f), "PackedBox");
                    PrimitiveBuilder.Part(box.transform, PrimitiveType.Cube, new Vector3(0, 0.51f, 0), new Vector3(0.2f, 0.02f, 1.01f), new Color(0.9f, 0.85f, 0.6f), "Tape");
                }
            }

            // 住人の姿
            if (r != null)
            {
                var proxyPos = origin + new Vector3(W * 0.5f, 0f, D * 0.6f);
                residentHomeProxy = ResidentProxy.Create(residentInteriorRoot, proxyPos, r, r.Id, this);
                residentHomeExit = origin + new Vector3(W * 0.5f, 0f, 0.5f);
                WorldLabels.Add(residentHomeProxy.transform, () => ResidentName(r.Id), new Vector3(0, 1.5f, 0), UIStyles.AlienText, 14f, true);
                // 在宅中は、この姿を住民本体の代わりにする（[E] で会話・身振りの対象・吹き出し）。この端末の表示だけの登録
                RegisterIndoorProxy(residentHomeProxy, AreaIds.ResidentHouse(r.Id), ResidentHomeProxyUntil());
                residentProxyTimer = 1.5f;
                var u = new Utterance(r.Id, "housing.home_welcome")
                    .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.WELCOME))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).At(Concepts.HERE))
                    .Face(Expression.Happy)
                    .With(Gesture.Wave);
                residentWelcome = u;
            }
            return origin + new Vector3(W * 0.5f, 0.05f, 1.3f);
        }

        private Utterance residentWelcome;
        private Vector3 residentHomeExit;

        /// <summary>住民の家の代理の有効期限（1時間ごとに延ばす。日をまたがない）。</summary>
        private int residentHomeProxyUntil;

        private int ResidentHomeProxyUntil()
        {
            int now = Clock != null ? Clock.MinuteOfDay : 0;
            residentHomeProxyUntil = Mathf.Min(TimeSystem.MinutesPerDay - 1, now + 60);
            return residentHomeProxyUntil;
        }

        /// <summary>はっきり外出している行動（家の中の姿は出かけていく）。</summary>
        private static bool IsOutActivity(ResidentActivity a)
        {
            switch (a)
            {
                case ResidentActivity.Working:
                case ResidentActivity.Shopping:
                case ResidentActivity.Fishing:
                case ResidentActivity.Farming:
                case ResidentActivity.Exploring:
                case ResidentActivity.Visiting:
                case ResidentActivity.AtPlaza:
                case ResidentActivity.Celebrating:
                case ResidentActivity.CatchingCreatures:
                case ResidentActivity.Mining:
                case ResidentActivity.WatchingSky:
                    return true;
            }
            return false;
        }

        /// <summary>住民の家の住人が出かける：代理を外し、出口へ歩いて消える。</summary>
        private void ResidentHomeProxyLeaves()
        {
            var proxy = residentHomeProxy;
            residentHomeProxy = null;
            if (proxy == null) return;
            ReleaseIndoorProxy(proxy);
            proxy.Leaving = true;
            proxy.PlayGesture(Gesture.Wave);
            proxy.OnArrive = p => { if (p != null) UnityEngine.Object.Destroy(p.gameObject); };
            proxy.GoTo(new List<Vector3> { residentHomeExit }, 180f, "idle", null);
        }

        private static bool TryPlaceTemp(HousingPlanetState temp, ItemDefinition def, FurnitureSpec spec, int rot, SeededRandom rng, out PlacedFurniture pf)
        {
            pf = null;
            var room = temp.rooms[0];
            int gx = 0, gz = 0;
            bool found = false;
            // 星や住民ごとに位置を揺らす：ランダムな候補を何度か試してから順番に探す
            for (int tries = 0; tries < 12 && !found; tries++)
            {
                int x = rng.Range(0, room.widthCells), z = rng.Range(0, room.depthCells);
                if (spec.surface == FurnitureSurface.Wall) HouseGeometry.SnapToWall(room, spec, rot, ref x, ref z);
                if (HouseGeometry.CanPlace(temp, null, HousingConst.LocHouse, 0, spec, x, z, rot, null, out _, out _)) { gx = x; gz = z; found = true; }
            }
            if (!found && !HouseGeometry.FindFreeSpot(temp, 0, spec, rot, out gx, out gz)) return false;
            pf = new PlacedFurniture { instanceId = "tmp" + temp.furniture.Count, itemId = def.Id, location = HousingConst.LocHouse, room = 0, gx = gx, gz = gz, rot = rot };
            temp.furniture.Add(pf);
            return true;
        }

        private void DestroyResidentInterior()
        {
            ReleaseIndoorProxy(residentHomeProxy);
            if (residentInteriorRoot != null) UnityEngine.Object.Destroy(residentInteriorRoot.gameObject);
            residentInteriorRoot = null;
            residentInteriorResident = null;
            residentInteriorFacility = null;
            residentHomeProxy = null;
            residentWelcome = null;
            residentInteriorFurniture.Clear();
            residentInteriorWalls.Clear();
        }

        private void OnResidentHomeChanged(string residentId)
        {
            if (residentInteriorRoot == null || residentInteriorResident != residentId) return;
            var p = LocalPlayer;
            var f = Layout?.GetFacilityById(residentInteriorFacility);
            var r = Game.Service<IResidentDirectory>()?.Get(residentId);
            if (f == null || p == null) return;
            var keepPos = p.Position;
            BuildResidentInterior(f, residentInteriorIndex, r);
            residentWelcome = null;
            p.Position = keepPos;
        }

        /// <summary>住民の家の住人が、自分の家具を使って過ごす（§15.1）。</summary>
        private void TickResidentHomeProxy(float dt)
        {
            if (residentHomeProxy == null) return;
            if (residentWelcome != null)
            {
                var u = residentWelcome;
                residentWelcome = null;
                residentHomeProxy.PlayGesture(Gesture.Wave);
                var speech = Game.Service<ISpeechService>();
                try { speech?.Speak(u, residentHomeProxy.transform, SpeechChannel.Direct, "resident"); } catch (Exception e) { Debug.LogException(e); }
            }
            residentProxyTimer -= dt;
            if (residentProxyTimer > 0f || !residentHomeProxy.Arrived) return;
            residentProxyTimer = UnityEngine.Random.Range(8f, 16f);
            var r = Game.Service<IResidentDirectory>()?.Get(residentHomeProxy.ResidentId);
            // 出かける時間になった（仕事・広場・訪問など）：家の中の姿は出口から出ていき、本体の表示に戻る
            if (r == null || !r.IsActive || IsOutActivity(r.Activity))
            {
                ResidentHomeProxyLeaves();
                return;
            }
            // 代理の期限を延ばす（在宅のあいだ）
            int nowMinute = Clock != null ? Clock.MinuteOfDay : 0;
            bool nearEnd = nowMinute >= residentHomeProxyUntil - 15 && residentHomeProxyUntil < TimeSystem.MinutesPerDay - 1;
            bool wrapped = nowMinute < residentHomeProxyUntil - 75; // 0:00 をまたいだ
            if (residentHomeProxy.IndoorRegistered && (nearEnd || wrapped))
                RegisterIndoorProxy(residentHomeProxy, AreaIds.ResidentHouse(residentHomeProxy.ResidentId), ResidentHomeProxyUntil());
            if (residentInteriorFurniture.Count == 0) return;
            bool night = Clock != null && Clock.IsNight;
            FurnitureObject pick = null;
            float best = float.MinValue;
            foreach (var fo in residentInteriorFurniture)
            {
                if (fo == null) continue;
                var spec = FurnitureCatalog.Resolve(fo.Def);
                float s = UnityEngine.Random.Range(0f, 0.6f);
                if (spec.use == FurnitureUse.Sit) s += 0.5f;
                if (spec.use == FurnitureUse.Music && r != null && (r.Job == ResidentJob.Musician || r.Lineage == AlienLineage.MultiArm)) s += 0.8f;
                if (spec.use == FurnitureUse.Sleep) s += night ? 1.2f : -0.5f;
                if (spec.lineage >= 0 && r != null && spec.lineage == (int)r.Lineage) s += 0.5f;
                if (spec.surface == FurnitureSurface.Wall || spec.surface == FurnitureSurface.Rug || spec.surface == FurnitureSurface.Tabletop || spec.surface == FurnitureSurface.Ceiling) s -= 1f;
                if (s > best) { best = s; pick = fo; }
            }
            if (pick == null) return;
            var ps = FurnitureCatalog.Resolve(pick.Def);
            float floorY = HousingConst.ResidentHomesOrigin.y;
            if ((ps.use == FurnitureUse.Sit || ps.use == FurnitureUse.Sleep) && pick.Visual?.SeatPoint != null)
            {
                var seat = pick.Visual.SeatPoint.position;
                residentHomeProxy.GoTo(new List<Vector3> { new Vector3(seat.x, floorY, seat.z) }, pick.transform.eulerAngles.y, ps.use == FurnitureUse.Sleep ? "lie" : "sit", null, Mathf.Max(0f, seat.y - floorY - 0.05f));
                if (pick.Visual.Fx != null)
                {
                    pick.Visual.Fx.occupiedUntil = UnityEngine.Time.time + 14f;
                    if (r != null) pick.Visual.Fx.occupantLineage = (int)r.Lineage;
                }
            }
            else
            {
                var dest = HouseGeometry.UsePoint(pick.transform.position, floorY, pick.transform.eulerAngles.y, ps);
                var look = pick.transform.position - dest;
                look.y = 0;
                float yaw = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look).eulerAngles.y : 0f;
                residentHomeProxy.GoTo(new List<Vector3> { dest }, yaw, ps.use == FurnitureUse.Music ? "play" : "look", null);
                if (ps.use == FurnitureUse.Music) ProceduralMusic.PlayTune(pick.transform.position, ps.style, StableHash.Derive(planetSeed, residentHomeProxy.ResidentId) ^ (ulong)UnityEngine.Random.Range(0, 1000));
            }
        }

        public void InspectResidentFurniture(FurnitureObject fo, PlayerState player)
        {
            var def = fo.Def;
            var tablet = Game.Service<ITabletService>();
            string residentId = fo.ResidentHomeOwner;
            var home = FindResidentHome(residentId);
            bool fromMe = false;
            if (home != null) foreach (var it in home.items) if (it.itemId == def.Id && it.fromPlayerGift && it.giftPlayerId == player.PlayerId) fromMe = true;
            if (fromMe) UIManager.Toast(Loc.Format("自分が贈った{0}が、大切そうに置かれている。", "The {0} you gave is kept with care.", def.Name));
            else UIManager.Toast(Loc.Format("{0}が置いてある。", "There is a {0}.", def.Name));
            if (residentId == "vacant") return;
            string key = residentId + ":" + def.Id + ":" + Today;
            if (residentObservedToday.Contains(key)) return;
            residentObservedToday.Add(key);
            RecordObservation(tablet, residentId,
                fromMe ? string.Format("{0}日目：私が贈った{1}が家に置いてあった。", Today, def.NameJa) : string.Format("{0}日目：家に{1}があった。", Today, def.NameJa),
                fromMe ? string.Format("Day {0}: the {1} I gave was in their home.", Today, def.NameEn) : string.Format("Day {0}: there was a {1} in their home.", Today, def.NameEn));
        }

        // ------------------------------------------------------------
        // 住民のイベント
        // ------------------------------------------------------------

        /// <summary>住民モジュールが先に AddFurnitureToResidentHome していれば、それをイベントと対応付けて true。</summary>
        private bool MatchExplicitEntry(string residentId, string itemId, bool gift, string playerId)
        {
            var home = FindResidentHome(residentId);
            if (home == null) return false;
            for (int i = home.items.Count - 1; i >= 0; i--)
            {
                var it = home.items[i];
                if (it.autoFromEvent || it.eventMatched || it.itemId != itemId || Today - it.day > 1) continue;
                it.eventMatched = true;
                if (gift) { it.fromPlayerGift = true; it.giftPlayerId = playerId; }
                MarkDirty();
                return true;
            }
            return false;
        }

        private void OnGiftGiven(GiftGivenEvent e)
        {
            if (!IsAuthority) return;
            var def = ItemRegistry.Get(e.ItemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return;
            if (MatchExplicitEntry(e.ResidentId, e.ItemId, true, e.PlayerId)) return;
            // 住民は贈られた家具を家に置く（住民モジュールが明示的に登録した場合は重複させない）
            AddResidentHomeItem(e.ResidentId, new ResidentHomeItem { itemId = e.ItemId, day = Today, fromPlayerGift = true, giftPlayerId = e.PlayerId, autoFromEvent = true });
        }

        private void OnResidentPurchase(ResidentPurchaseEvent e)
        {
            if (!IsAuthority) return;
            var def = ItemRegistry.Get(e.ItemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return;
            if (MatchExplicitEntry(e.ResidentId, e.ItemId, false, null)) return;
            AddResidentHomeItem(e.ResidentId, new ResidentHomeItem { itemId = e.ItemId, day = Today, autoFromEvent = true });
        }

        private void OnResidentMoveOutDecided(ResidentMoveOutDecidedEvent e)
        {
            if (!IsAuthority) return;
            var h = GetOrCreateResidentHome(e.ResidentId);
            h.packing = true;
            MarkDirty();
            OnResidentHomeChanged(e.ResidentId);
        }

        private void OnResidentMovedOut(ResidentMovedOutEvent e)
        {
            if (IsAuthority)
            {
                // 引っ越すと家具も無くなる（住民が持っていく）
                state.residentHomes.RemoveAll(h => h.residentId == e.ResidentId);
                string name = ResidentName(e.ResidentId);
                foreach (var f in state.furniture)
                {
                    // 贈り主が引っ越しても、家具とその記録は残る（§14.2）
                    if (f.giftFromResidentId != e.ResidentId) continue;
                    AddMemory(f, "gift_giver_left", string.Format("{0}日目、これを贈ってくれた{1}が引っ越していった。", e.Day, name), string.Format("Day {0}: {1}, who gave this, moved away.", e.Day, name), e.ResidentId);
                }
                foreach (var inv in state.invitations)
                    if (inv.residentId == e.ResidentId && (inv.status == InvitationStatus.Planned || inv.status == InvitationStatus.Arriving))
                        inv.status = InvitationStatus.Cancelled;
                MarkDirty();
            }
            if (residentInteriorResident == e.ResidentId && LocalPlayer != null && IsResidentHomeArea(LocalPlayer.CurrentAreaId))
            {
                var f = Layout?.GetFacilityById(residentInteriorFacility);
                if (f != null) Game.Areas.Transition(LocalPlayer, AreaIds.Overworld, Layout.OnGround(f.entrance) + Vector3.up * 0.2f, f.rotationY);
            }
            RebuildResidentDoors();
        }

        private void OnResidentMovedIn(ResidentMovedInEvent e)
        {
            if (IsAuthority)
            {
                var h = GetOrCreateResidentHome(e.ResidentId);
                h.packing = false;
                MarkDirty();
            }
            RebuildResidentDoors();
        }
    }
}
