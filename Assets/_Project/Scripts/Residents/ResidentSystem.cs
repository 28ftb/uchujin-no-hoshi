using System;
using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.Visual;
using Uchujin.World;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// 住民システム（住民生活仕様 v0.2 全体）。
    /// ・初期住民の決定的生成、家と職場の割り当て
    /// ・1日の生活AI（予定・移動・仕事・買い物・食事・趣味・交流）※ホストだけがシミュレーションする
    /// ・住民同士の関係と会話（意味データで生成し、宇宙語で聞こえる）
    /// ・主人公との親交・記憶・噂、会話ウィンドウ（主人公は話さず身振りと物で応える）
    /// ・引っ越し（転出の兆候・後継者・新住民・手紙）
    /// partial：Life（生活AI）／Social（交流・記憶・イベント）／Moving（引っ越し）／Conversation（会話）
    /// </summary>
    public sealed partial class ResidentSystem : GameSystemBase, IResidentDirectory
    {
        public override string SaveKey => "residents";
        public override int DailyUpdateOrder => 400;

        private readonly Dictionary<string, Resident> residents = new Dictionary<string, Resident>();
        private readonly List<Resident> ordered = new List<Resident>();
        private readonly List<IResidentInteractionProvider> providers = new List<IResidentInteractionProvider>();
        private List<ResidentProfile> initialProfiles = new List<ResidentProfile>();
        private ResidentsSaveData meta = new ResidentsSaveData();
        private Transform sceneRoot;
        private readonly Dictionary<string, List<GameObject>> boxes = new Dictionary<string, List<GameObject>>();
        private ulong seed;
        private bool subscribed;
        private SeededRandom fxRng = new SeededRandom(1);

        public IReadOnlyList<Resident> All => ordered;
        public int Today => Clock != null ? Clock.Day : 1;
        public int MinuteOfDay => Clock != null ? Clock.MinuteOfDay : 8 * 60;
        public long Now => Clock != null ? Clock.TotalMinutes : 0;
        public bool IsNight => Clock != null && Clock.IsNight;
        internal WorldLayout Layout => Game != null ? Game.Layout : null;

        public bool IsRestDayOn(int day)
        {
            int week = Clock != null ? Mathf.Max(1, Clock.WeekLength) : 7;
            return (day - 1) % week == week - 1;
        }

        // =====================================================================
        // ライフサイクル
        // =====================================================================

        public override void RegisterContent()
        {
            WorldAuthority.Register<ResidentTalkCommand>(HandleTalkCommand);
            WorldAuthority.Register<ResidentGiftCommand>(HandleGiftCommand);
            WorldAuthority.Register<ResidentGestureCommand>(HandleGestureCommand);
            WorldAuthority.Register<ResidentEndTalkCommand>(HandleEndTalkCommand);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            ClearAll();
            seed = ctx.Seed;
            fxRng = SeededRandom.For(seed, "residents.fx");
            initialProfiles = ResidentGenerator.GenerateInitial(seed, InitialCount(ctx.Layout));
        }

        /// <summary>初期住民数【仮数値】。住宅区画が足りなければ、新住民用に1区画は空けておく（第2章）。</summary>
        private static int InitialCount(WorldLayout layout)
        {
            int count = ResidentTuning.InitialResidentCount;
            if (layout != null && layout.HousePlots.Count > 0)
                count = Mathf.Min(count, Mathf.Max(8, layout.HousePlots.Count - 1));
            return count;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            meta = new ResidentsSaveData
            {
                nextMoveInDay = ResidentTuning.FirstNewcomerDay,
                lastMoveOutDay = -999
            };
            var rng = SeededRandom.For(seed, "residents.initial.state");
            foreach (var p in initialProfiles)
            {
                var st = new ResidentState
                {
                    id = p.id,
                    active = true,
                    movedInDay = 0,
                    job = p.originalJob,
                    money = rng.Range(ResidentTuning.StartMoneyMin, ResidentTuning.StartMoneyMax),
                    mood = rng.Range(0f, 0.4f),
                    hunger = rng.Range(0f, 0.3f),
                    social = rng.Range(0.2f, 0.6f)
                };
                var r = new Resident(p, st, true);
                AddResident(r);
            }
            UpdateGuideFlag();
            // 家（住宅区画）を割り当てる
            var plotOrder = new List<HousePlot>();
            if (ctx.Layout != null) plotOrder.AddRange(ctx.Layout.HousePlots);
            var plotRng = SeededRandom.For(seed, "residents.houses");
            plotRng.Shuffle(plotOrder);
            // 職場の近くに住みたい住民もいるが、ここでは区画の並びをシードで混ぜて割り当てる
            int pi = 0;
            foreach (var r in ordered)
            {
                while (pi < plotOrder.Count && plotOrder[pi].occupied) pi++;
                if (pi < plotOrder.Count) AssignHome(r, plotOrder[pi++]);
                PlaceAtHome(r);
            }
            InitRelations(ordered, SeededRandom.For(seed, "residents.relations"));
            foreach (var r in ordered) InitGoals(r, SeededRandom.For(seed, "residents.goals." + r.Id));
            EnsureEssentialJobs();
            AssignWorkplaceOwners();
        }

        public override void BuildScene()
        {
            var parent = Game.SceneRoot;
            sceneRoot = PrimitiveBuilder.Empty("Residents", parent, Vector3.zero).transform;
            foreach (var r in ordered) SyncAvatar(r);
            RefreshBoxes();
        }

        public override void OnPlayStarted()
        {
            SubscribeEvents();
            if (IsAuthority)
            {
                foreach (var r in ordered)
                    if (r.State.active && r.State.planDay != Today) PlanDay(r, Today);
                foreach (var r in ordered) if (r.State.active) UpdateBehavior(r, true);
                MarkDirtyNow();
            }
        }

        public override void Teardown()
        {
            UnsubscribeEvents();
            ClearAll();
        }

        private void ClearAll()
        {
            foreach (var r in ordered) if (r.Avatar != null) UnityEngine.Object.Destroy(r.Avatar.gameObject);
            residents.Clear();
            ordered.Clear();
            foreach (var kv in boxes) foreach (var b in kv.Value) if (b != null) UnityEngine.Object.Destroy(b);
            boxes.Clear();
            if (sceneRoot != null) UnityEngine.Object.Destroy(sceneRoot.gameObject);
            sceneRoot = null;
            meta = new ResidentsSaveData();
            ResetSyncState();
            observedKeys.Clear();
            questSpokeFrame.Clear();
            conversationResidentId = null;
        }

        private void AddResident(Resident r)
        {
            residents[r.Id] = r;
            ordered.Add(r);
            ordered.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>案内役：Round の初期住民のうち Id 順で最初の1人（Story と同じ規則）。引っ越さない。</summary>
        private void UpdateGuideFlag()
        {
            Resident guide = null;
            foreach (var r in ordered)
            {
                r.IsGuide = false;
                if (guide == null && r.Profile.lineage == AlienLineage.Round && r.State.movedInDay <= 0) guide = r;
            }
            if (guide != null) guide.IsGuide = true;
        }

        public Resident Guide
        {
            get
            {
                foreach (var r in ordered) if (r.IsGuide) return r;
                return null;
            }
        }

        // =====================================================================
        // 家・職場の割り当て
        // =====================================================================

        private void AssignHome(Resident r, HousePlot plot)
        {
            var layout = Layout;
            if (plot == null) return;
            plot.occupied = true;
            FacilityInfo fac = null;
            if (layout != null)
            {
                foreach (var f in layout.Facilities)
                {
                    if (f.type == FacilityType.ResidentHouse && f.plotIndex == plot.index) { fac = f; break; }
                }
                if (fac == null)
                {
                    // World が区画ごとの施設を作っていない場合は住民側で作る（README に記載）
                    float rad = plot.rotationY * Mathf.Deg2Rad;
                    var fwd = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                    fac = new FacilityInfo
                    {
                        id = "house.plot." + plot.index,
                        type = FacilityType.ResidentHouse,
                        position = plot.position,
                        rotationY = plot.rotationY,
                        entrance = plot.position + fwd * 3.5f,
                        plotIndex = plot.index
                    };
                    layout.Facilities.Add(fac);
                }
                fac.ownerResidentId = r.Id;
                plot.facilityId = fac.id;
            }
            r.State.homePlotIndex = plot.index;
            r.State.homeFacilityId = fac != null ? fac.id : plot.facilityId;
        }

        private void ReleaseHome(Resident r)
        {
            var layout = Layout;
            if (layout == null || r.State.homePlotIndex < 0) return;
            foreach (var plot in layout.HousePlots)
            {
                if (plot.index != r.State.homePlotIndex) continue;
                plot.occupied = false;
            }
            var fac = layout.GetFacilityById(r.State.homeFacilityId);
            if (fac != null && fac.ownerResidentId == r.Id) fac.ownerResidentId = null;
        }

        /// <summary>保存された住民の家を、再生成されたレイアウトへ反映する（ロード・ゲストの同期のたびに冪等に）。</summary>
        private void ReapplyHomes()
        {
            var layout = Layout;
            if (layout == null) return;
            foreach (var plot in layout.HousePlots) plot.occupied = false;
            foreach (var f in layout.Facilities) if (f.type == FacilityType.ResidentHouse) f.ownerResidentId = null;
            foreach (var r in ordered)
            {
                if (!r.State.active || r.State.homePlotIndex < 0) continue;
                HousePlot plot = null;
                foreach (var p in layout.HousePlots) if (p.index == r.State.homePlotIndex) { plot = p; break; }
                if (plot != null) AssignHome(r, plot);
            }
            AssignWorkplaceOwners();
        }

        private void AssignWorkplaceOwners()
        {
            var layout = Layout;
            if (layout == null) return;
            foreach (var f in layout.Facilities)
            {
                if (f.type == FacilityType.ResidentHouse || f.type == FacilityType.PlayerHouse || f.type == FacilityType.PlayerShop) continue;
                Resident owner = null;
                foreach (var r in ordered)
                {
                    if (!r.State.active) continue;
                    var wp = JobTable.Workplace(r.State.job);
                    if (!wp.HasValue || wp.Value != f.type) continue;
                    if (f.type == FacilityType.Plaza || f.type == FacilityType.MessagePlaza || f.type == FacilityType.Lookout && r.State.job != ResidentJob.WeatherReader) continue;
                    // 引っ越し予定の住民より、残る住民（後継者）を優先
                    if (owner == null || owner.IsMovingOut && !r.IsMovingOut) owner = r;
                }
                if (owner != null) f.ownerResidentId = owner.Id;
                else if (f.type == FacilityType.Blacksmith || f.type == FacilityType.RepairWorkshop || f.type == FacilityType.SeedShop
                         || f.type == FacilityType.FishingHut || f.type == FacilityType.CarpenterWorkshop || f.type == FacilityType.GeneralStore)
                    f.ownerResidentId = null;
            }
        }

        internal Vector3 HomeYard(Resident r)
        {
            var layout = Layout;
            if (layout != null)
            {
                var fac = layout.GetFacilityById(r.State.homeFacilityId);
                if (fac != null)
                {
                    float rad = fac.rotationY * Mathf.Deg2Rad;
                    var fwd = new Vector3(Mathf.Sin(rad), 0, Mathf.Cos(rad));
                    var basePos = fac.entrance != Vector3.zero ? fac.entrance : fac.position + fwd * 3.5f;
                    return layout.OnGround(basePos + fwd * 1.2f + Jitter(r.Id, 0.8f));
                }
                foreach (var p in layout.HousePlots)
                    if (p.index == r.State.homePlotIndex) return layout.OnGround(p.position + Jitter(r.Id, 1.5f));
                var plaza = layout.GetFacility(FacilityType.Plaza);
                if (plaza != null) return layout.OnGround(plaza.position + Jitter(r.Id, 8f));
                return layout.OnGround(layout.PlayerSpawn + Jitter(r.Id, 10f));
            }
            return Jitter(r.Id, 10f);
        }

        private void PlaceAtHome(Resident r)
        {
            var p = HomeYard(r);
            r.StatePosition = p;
            r.State.activity = ResidentActivity.AtHome;
        }

        /// <summary>住民ごとの決定的なずれ（同じ場所に重ならない）。</summary>
        internal static Vector3 Jitter(string key, float radius)
        {
            float a = StableHash.Unit(0x5eedUL, key) * Mathf.PI * 2f;
            float rr = radius * (0.6f + 0.4f * StableHash.Unit(0xbeefUL, key));
            return new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr);
        }

        // =====================================================================
        // アバター
        // =====================================================================

        /// <summary>住民の状態に合わせてアバターを作る・消す・見せる（冪等）。</summary>
        private void SyncAvatar(Resident r)
        {
            if (sceneRoot == null) return;
            bool shouldExist = r.State.active || r.IsVisitingFormer;
            if (!shouldExist)
            {
                if (r.Avatar != null) UnityEngine.Object.Destroy(r.Avatar.gameObject);
                r.Avatar = null;
                return;
            }
            if (r.Avatar == null)
            {
                var go = new GameObject("Resident_" + r.Id);
                go.transform.SetParent(sceneRoot, false);
                go.transform.position = r.StatePosition;
                go.transform.rotation = Quaternion.Euler(0, r.State.rotY, 0);
                var av = go.AddComponent<ResidentAvatar>();
                av.Init(r, this);
                r.Avatar = av;
            }
            // この端末で家の中の代理を表示中は本体を隠す（代理は家に入った端末だけが作るローカルの姿）
            r.Avatar.SetVisible(!r.State.hidden && !r.HasIndoorProxy);
            r.Avatar.SetBaseExpression(r.State.face);
            r.Avatar.SetHeldItem(r.State.carrying ? "box" : r.State.heldItemId);
        }

        internal void RebuildJobProp(Resident r)
        {
            if (r.Avatar != null) r.Avatar.RebuildJobProp();
        }

        /// <summary>引っ越しの荷物の箱（転出の兆候・新住民の荷物：住民生活仕様 §11、第2章・第4章）。</summary>
        private void RefreshBoxes()
        {
            if (sceneRoot == null) return;
            var want = new HashSet<string>();
            foreach (var r in ordered)
            {
                if (!r.State.active) continue;
                bool movingOutSoon = r.IsMovingOut && r.State.moveOutDay - Today <= ResidentTuning.MoveOutNoticeDays - 1;
                bool justMovedIn = r.State.movedInDay > 0 && Today - r.State.movedInDay <= 0;
                if (movingOutSoon || justMovedIn) want.Add(r.Id);
            }
            var remove = new List<string>();
            foreach (var kv in boxes) if (!want.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                foreach (var b in boxes[id]) if (b != null) UnityEngine.Object.Destroy(b);
                boxes.Remove(id);
            }
            foreach (var id in want)
            {
                if (boxes.ContainsKey(id)) continue;
                var r = residents[id];
                var list = new List<GameObject>();
                var yard = HomeYard(r);
                var brng = SeededRandom.For(seed, "boxes." + id);
                int n = brng.Range(3, 6);
                for (int i = 0; i < n; i++)
                {
                    var off = new Vector3(brng.Range(-1.6f, 1.6f), 0, brng.Range(-1.2f, 1.2f)) + Vector3.right * 1.5f;
                    var pos = Layout != null ? Layout.OnGround(yard + off) : yard + off;
                    float sc = brng.Range(0.45f, 0.75f);
                    list.Add(ResidentAvatarBuilder.BuildBox(sceneRoot, pos, brng.Range(0f, 90f), sc, r.Profile.appearance.accentColor));
                    if (i == 0 && brng.Chance(0.6f))
                        list.Add(ResidentAvatarBuilder.BuildBox(sceneRoot, pos + Vector3.up * sc, brng.Range(0f, 90f), sc * 0.8f, r.Profile.appearance.accentColor));
                }
                boxes[id] = list;
            }
        }

        // =====================================================================
        // 毎フレーム
        // =====================================================================

        public override void Tick(float deltaTime)
        {
            if (ordered.Count == 0) return;
            bool paused = Clock != null && Clock.IsPaused;
            if (IsAuthority && !paused) MoveResidents(deltaTime);
            TickIndoorProxies();
            if (!paused) TickLocalSpeech();
            // 住民の位置・行動はホストが確定し、軽い状態（residents.live）を約0.5秒ごと、
            // 全体状態（記憶・関係・予定）は変化があった時だけ最短10秒間隔でゲストへ送る（マルチ仕様 §14）
            if (IsAuthority) TickSync(deltaTime);
        }

        public override void OnGameMinutesAdvanced(int minutes)
        {
            if (!IsAuthority) return;
            bool snap = minutes >= ResidentTuning.SnapMinutes;
            foreach (var r in ordered)
            {
                if (!r.State.active && !r.IsVisitingFormer) continue;
                r.State.hunger = Mathf.Clamp01(r.State.hunger + ResidentTuning.HungerPerHour * minutes / 60f);
                UpdateBehavior(r, snap);
            }
            if (snap) foreach (var r in ordered) if (r.State.active) SnapToTarget(r);
            SocialTick(minutes);
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (!IsAuthority) return;
            HourlySocial(day, hour);
            FinalizeDepartures(day, hour);
            // 予報の保険：天気読みが何かで広場に行けなかった日も、9時までに伝言を残す（時間・天候仕様 §14）
            if (hour == 10 && meta.lastWeatherForecastDay != day) PostForecast(FindByJobInternal(ResidentJob.WeatherReader));
            MarkDirty();
        }

        public override void OnDayEnded(int day)
        {
            if (!IsAuthority) return;
            DailyIncome(day);
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            meta.lastSimDay = day;
            DailyMaintenance(day);
            ProcessMoving(day);
            EnsureEssentialJobs();
            AssignWorkplaceOwners();
            foreach (var r in ordered) if (r.State.active || r.State.visitDay == day) PlanDay(r, day);
            foreach (var r in ordered) SyncAvatar(r);
            RefreshBoxes();
            MarkDirtyNow(); // 1日の予定・引っ越し・再訪がまとめて変わる
        }

        // =====================================================================
        // IResidentDirectory
        // =====================================================================

        public IEnumerable<IResidentInfo> ActiveResidents
        {
            get
            {
                for (int i = 0; i < ordered.Count; i++) if (ordered[i].State.active) yield return ordered[i];
            }
        }

        public IEnumerable<IResidentInfo> FormerResidents
        {
            get
            {
                for (int i = 0; i < ordered.Count; i++) if (!ordered[i].State.active && ordered[i].State.movedOutDay >= 0) yield return ordered[i];
            }
        }

        public IResidentInfo Get(string residentId) => GetResident(residentId);

        public Resident GetResident(string residentId)
        {
            if (residentId != null && residents.TryGetValue(residentId, out var r)) return r;
            return null;
        }

        public IResidentInfo FindByJob(ResidentJob job) => FindByJobInternal(job);

        internal Resident FindByJobInternal(ResidentJob job)
        {
            Resident best = null;
            foreach (var r in ordered)
            {
                if (!r.State.active || r.State.job != job) continue;
                if (best == null || best.IsMovingOut && !r.IsMovingOut) best = r;
            }
            if (best != null) return best;
            foreach (var r in ordered) if (r.State.active && r.State.apprenticeFor == job) return r;
            if (JobTable.IsEssential(job))
            {
                // 不変条件が崩れていた場合の保険：誰かを必ず返す（呼び出し側の進行不能を防ぐ）
                foreach (var r in ordered) if (r.State.active && r.Profile.originalJob == job) return r;
                foreach (var r in ordered) if (!r.State.active && r.State.job == job) return r;
                foreach (var r in ordered) if (r.State.active) return r;
            }
            return null;
        }

        public bool TrySpend(string residentId, int amount, string reason)
        {
            var r = GetResident(residentId);
            if (r == null || amount < 0 || r.State.money < amount) return false;
            r.State.money -= amount;
            MarkDirty();
            return true;
        }

        public void GiveMoney(string residentId, int amount, string reason)
        {
            var r = GetResident(residentId);
            if (r == null || amount == 0) return;
            r.State.money = Mathf.Max(0, r.State.money + amount);
            MarkDirty();
        }

        public void GiveItem(string residentId, ItemStack stack, string reason)
        {
            var r = GetResident(residentId);
            if (r == null || stack == null || stack.IsEmpty) return;
            var copy = stack.Clone();
            if (pendingIntents.TryGetValue(residentId, out var intent) && intent != null && intent.need == ShoppingNeed.Gift && !string.IsNullOrEmpty(intent.forResidentId))
                copy.data = GiftTag + intent.forResidentId;
            r.AddItem(copy);
            HandleReceivedItem(r, copy, reason);
            MarkDirty();
        }

        public void ChangeFriendship(string residentId, string playerId, int delta, string reason)
        {
            var r = GetResident(residentId);
            if (r == null || string.IsNullOrEmpty(playerId) || delta == 0) return;
            var b = r.Bond(playerId);
            int before = b.friendship;
            b.friendship = Mathf.Clamp(b.friendship + delta, 0, 100);
            int applied = b.friendship - before;
            if (applied != 0)
                EventBus.Publish(new FriendshipChangedEvent { ResidentId = residentId, PlayerId = playerId, Delta = applied, Value = b.friendship, Reason = reason });
            MarkDirty();
        }

        public void AddMemory(string residentId, ResidentMemoryEntry entry)
        {
            var r = GetResident(residentId);
            if (r == null || entry == null) return;
            if (entry.day <= 0) entry.day = Today;
            // 商店の購入は ShopItemSoldEvent（1個ごと）でも記憶しているので、同じ日・同じ品の重複は増やさない
            if (entry.kind == "bought_from_shop" && HasShopMemory(r, entry.playerId, entry.itemId, entry.day)) return;
            if (entry.kind == "quest_done")
            {
                // QuestStateChangedEvent で先に入れた簡易の記憶（同じ日・同じプレイヤー）を、依頼側の詳しい記憶に置き換える（二重に覚えない）
                int idx = r.State.memories.FindLastIndex(m => m.kind == "quest_done" && m.playerId == entry.playerId && m.day == entry.day
                                                              && m.detail != null && m.detail.StartsWith(QuestEventMemoryPrefix));
                if (idx >= 0) r.State.memories.RemoveAt(idx);
            }
            r.AddMemory(entry);
            if (entry.kind == "quest_done" && !string.IsNullOrEmpty(entry.playerId))
            {
                // 困りごとを手伝ってもらった：信頼が深まる（住民生活仕様 §6）
                var b = r.Bond(entry.playerId);
                b.met = true;
            }
            MarkDirty();
        }

        public UtteranceRecord Speak(string residentId, Utterance utterance, SpeechChannel channel = SpeechChannel.Direct)
            => Speak(residentId, utterance, channel, true);

        /// <summary>
        /// relayToGuests=false：各端末がそれぞれ生成する発話（立ち話・独り言・遠くから手を振る）。ホストでもゲストへ中継しない。
        /// ホストだけが生成する発話（AI の反応・伝言・会話ウィンドウ・他モジュールの依頼）は true で中継され、二重にならない。
        /// </summary>
        internal UtteranceRecord Speak(string residentId, Utterance utterance, SpeechChannel channel, bool relayToGuests)
        {
            var r = GetResident(residentId);
            if (utterance == null) return null;
            if (r != null) utterance.speakerId = r.Id;
            // クエストが身振りへの返事として話したフレームを覚える（OnPlayerGesture で住民側の反応を重ねないため）
            if (r != null && utterance.contextTag != null && utterance.contextTag.StartsWith("quest."))
                questSpokeFrame[r.Id] = UnityEngine.Time.frameCount;
            var speech = Game != null ? Game.Service<ISpeechService>() : null;
            UtteranceRecord rec = null;
            // 吹き出しは今いる場所（家の中の代理を表示中なら代理）に出す
            var anchor = r != null ? r.Transform : null;
            if (r != null && anchor != null)
            {
                if (r.Avatar != null)
                {
                    r.Avatar.ShowExpression(utterance.expression, 3.5f);
                    foreach (var g in utterance.gestures) r.Avatar.PlayGesture(g);
                }
                if (speech != null) rec = speech.Speak(utterance, anchor, channel, "resident", relayToGuests);
                if (relayToGuests) AddSpeechCues(r, utterance);
            }
            else if (speech != null)
            {
                var pos = r != null ? r.StatePosition : Vector3.zero;
                rec = speech.CreateRecord(utterance, channel, "resident", pos);
            }
            return rec;
        }

        public void RegisterInteractionProvider(IResidentInteractionProvider provider)
        {
            if (provider == null) return;
            // 同じ型の古いインスタンス（前の星で登録されたもの）は置き換える
            providers.RemoveAll(p => p == null || p.GetType() == provider.GetType());
            providers.Add(provider);
        }

        public void RequestGoTo(string residentId, Vector3 position, int untilMinuteOfDay, string reasonTag)
        {
            var r = GetResident(residentId);
            if (r == null) return;
            // 演出で留めている住民を、商店の立ち寄り・品定め（理由タグ shop_*）で動かさない（演出の RequestGoTo は効く）
            if (r.State.cutsceneHold && reasonTag != null && reasonTag.StartsWith("shop_")) return;
            long dayStart = Now - MinuteOfDay;
            long until = dayStart + Mathf.Clamp(untilMinuteOfDay, 0, TimeSystem.MinutesPerDay);
            if (until <= Now) until += TimeSystem.MinutesPerDay;
            r.State.hasGoTo = true;
            r.State.goX = position.x;
            r.State.goY = position.y;
            r.State.goZ = position.z;
            r.State.goUntil = until;
            r.State.goReason = reasonTag;
            OnGoToRequested(r, position);
            r.State.hidden = false;
            r.State.chatPartnerId = null;
            if (IsAuthority)
            {
                SetDestination(r, position);
                SyncAvatar(r);
            }
            MarkDirty();
        }

        // =====================================================================
        // 演出用（Story：プロローグ・エンディング）。統括が IResidentDirectory に同じシグネチャを追加予定
        // =====================================================================

        /// <summary>hold=true の間はスケジュールAI・移動・自動の発話を止めてその場に留める（RequestGoTo と Speak は効く）。</summary>
        public int GetResidentAffinity(string residentId, string otherResidentId)
        {
            if (residentId == null || otherResidentId == null || !residents.TryGetValue(residentId, out var r)) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(r.Affinity(otherResidentId)), 0, 100);
        }

        public IEnumerable<string> GetFriends(string residentId, int max = 5)
        {
            var result = new List<string>();
            if (residentId == null || !residents.TryGetValue(residentId, out var r) || r.State.relations == null) return result;
            var rels = new List<RelationEntry>(r.State.relations);
            rels.Sort((a, b) => b.affinity.CompareTo(a.affinity));
            foreach (var rel in rels)
            {
                if (result.Count >= max) break;
                if (rel.affinity < 30f) break;
                if (!residents.TryGetValue(rel.otherId, out var o) || !o.State.active) continue;
                result.Add(rel.otherId);
            }
            return result;
        }

        public void HoldForCutscene(string residentId, bool hold)
        {
            var r = GetResident(residentId);
            if (r == null) return;
            r.State.cutsceneHold = hold;
            if (hold)
            {
                if (!string.IsNullOrEmpty(r.State.chatPartnerId)) EndChat(r);
                r.State.followPlayerId = null;
                if (!r.State.hasGoTo) { r.hasTarget = false; r.path.Clear(); }
                r.State.hidden = false;
                if (r.State.activity == ResidentActivity.Sleeping || r.State.activity == ResidentActivity.Mining) r.State.activity = ResidentActivity.Walking;
                SyncAvatar(r);
            }
            else
            {
                r.State.blockIndex = -1; // 通常の予定へ戻る
            }
            MarkDirty();
        }

        /// <summary>住民を即座にその位置・向きへ移動（AIの内部位置も同期し、次のフレームで戻されない）。</summary>
        public void Warp(string residentId, Vector3 position, float yawDegrees)
        {
            var r = GetResident(residentId);
            if (r == null) return;
            r.StatePosition = position;
            r.State.rotY = yawDegrees;
            r.path.Clear();
            r.pathIndex = 0;
            r.hasTarget = false;
            r.lastWrittenPos = position;
            r.hasWrittenPos = true;
            r.State.hidden = false;
            if (r.Avatar != null)
            {
                r.Avatar.transform.position = position;
                r.Avatar.transform.rotation = Quaternion.Euler(0, yawDegrees, 0);
                r.Avatar.NetTarget = position;
                r.Avatar.NetYaw = yawDegrees;
            }
            if (r.State.hasGoTo && ResidentUtil.FlatDistance(position, new Vector3(r.State.goX, r.State.goY, r.State.goZ)) > 1.2f && IsAuthority && !r.State.cutsceneHold)
                SetDestination(r, new Vector3(r.State.goX, r.State.goY, r.State.goZ));
            SyncAvatar(r);
            MarkDirty();
        }

        // =====================================================================
        // 補助（会話・アバターから使う）
        // =====================================================================

        internal Vector3? ResolvePointTarget(string target, Vector3 r0Pos = default(Vector3))
        {
            // "cart" など分からない特別な対象は無視（向きを変えないだけ）
            if (string.IsNullOrEmpty(target)) return null;
            var layout = Layout;
            var r = GetResident(target);
            if (r != null) return r.Position + Vector3.up;
            if (target == "sky") return null;
            if (layout != null)
            {
                var fac = layout.GetFacilityById(target);
                if (fac != null) return fac.position;
                if (target == "mine") return layout.MineEntrance;
                if (target == "hill")
                {
                    var look = layout.GetFacility(FacilityType.Lookout);
                    if (look != null) return look.position;
                    foreach (var a in layout.GetVisitableAreas(AreaType.Hill)) return a.center;
                }
                if (target == "river")
                {
                    foreach (var w in layout.Waters)
                    {
                        if (w.type != WaterType.River || w.points.Count == 0) continue;
                        Vector3 best = w.points[0];
                        foreach (var pt in w.points) if (ResidentUtil.FlatDistance(pt, r0Pos) < ResidentUtil.FlatDistance(best, r0Pos)) best = pt;
                        return best;
                    }
                }
                if (target == "rocket")
                {
                    var rocket = Game.Service<IRocketService>();
                    return rocket != null ? rocket.RocketPosition : layout.CrashSite;
                }
            }
            if (target == "shop")
            {
                var shop = Game.Service<IShopService>();
                if (shop != null) return shop.EntrancePosition;
            }
            return null;
        }

        internal Resident NewestResident()
        {
            Resident best = null;
            foreach (var r in ordered)
                if (r.State.active && r.State.movedInDay > 0 && (best == null || r.State.movedInDay > best.State.movedInDay)) best = r;
            return best;
        }

        internal PlayerReputation GetReputation(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return null;
            foreach (var p in meta.reputation) if (p.playerId == playerId) return p;
            return null;
        }

        internal PlayerReputation Reputation(string playerId)
        {
            var p = GetReputation(playerId);
            if (p != null) return p;
            p = new PlayerReputation { playerId = playerId, firstSeenDay = Today };
            meta.reputation.Add(p);
            return p;
        }

        /// <summary>話題にできる物（実際に関わった物だけ）。</summary>
        internal string PickTopicItem(Resident r, SeededRandom rng)
        {
            var c = new List<string>();
            if (!string.IsNullOrEmpty(r.State.lastBoughtItemId)) c.Add(r.State.lastBoughtItemId);
            if (!string.IsNullOrEmpty(r.State.lastAteItemId)) c.Add(r.State.lastAteItemId);
            if (!string.IsNullOrEmpty(r.State.lastCaughtItemId)) c.Add(r.State.lastCaughtItemId);
            foreach (var h in r.State.homeItems) c.Add(h);
            foreach (var s in r.State.items) if (s != null && !s.IsEmpty) c.Add(s.itemId);
            c.RemoveAll(id => !ItemRegistry.Exists(id));
            if (c.Count == 0) return null;
            return c[rng.Range(0, c.Count)];
        }

        // =====================================================================
        // セーブ・同期（SaveScope.Planet だけ。住民はホストが正本）
        // =====================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope != SaveScope.Planet) return null;
            foreach (var r in ordered)
            {
                if (r.Avatar != null && IsAuthority)
                {
                    var p = r.Avatar.transform.position;
                    r.StatePosition = p;
                    r.State.rotY = r.Avatar.transform.eulerAngles.y;
                }
            }
            var data = new ResidentsSaveData
            {
                version = 1,
                newcomerCounter = meta.newcomerCounter,
                nextMoveInDay = meta.nextMoveInDay,
                lastMoveOutDay = meta.lastMoveOutDay,
                moveOutsTotal = meta.moveOutsTotal,
                moveInsTotal = meta.moveInsTotal,
                reputation = meta.reputation,
                lastSimDay = meta.lastSimDay,
                lastWeatherForecastDay = meta.lastWeatherForecastDay,
                pendingRecords = meta.pendingRecords
            };
            foreach (var r in ordered)
            {
                data.residents.Add(new ResidentSaveEntry
                {
                    id = r.Id,
                    genKey = r.Profile.genKey,
                    regenerable = r.Regenerable,
                    // 初期住民はシードから再生成できるのでプロフィールを保存しない
                    profile = r.Regenerable ? null : r.Profile,
                    state = r.State
                });
            }
            return JsonUtility.ToJson(data);
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (scope != SaveScope.Planet || string.IsNullOrEmpty(json)) return;
            ResidentsSaveData data;
            try { data = JsonUtility.FromJson<ResidentsSaveData>(json); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (data == null) return;
            bool live = sceneRoot != null; // プレイ中（ゲストの同期）
            meta.newcomerCounter = data.newcomerCounter;
            meta.nextMoveInDay = data.nextMoveInDay;
            meta.lastMoveOutDay = data.lastMoveOutDay;
            meta.moveOutsTotal = data.moveOutsTotal;
            meta.moveInsTotal = data.moveInsTotal;
            meta.reputation = data.reputation ?? new List<PlayerReputation>();
            meta.lastSimDay = data.lastSimDay;
            meta.lastWeatherForecastDay = data.lastWeatherForecastDay;
            meta.pendingRecords = data.pendingRecords ?? new List<PendingPlayerRecord>();

            var seen = new HashSet<string>();
            bool membershipChanged = false;
            foreach (var e in data.residents)
            {
                if (e == null || e.state == null || string.IsNullOrEmpty(e.id)) continue;
                seen.Add(e.id);
                ResidentProfile profile = null;
                if (e.regenerable)
                {
                    profile = initialProfiles.Find(p => p.id == e.id);
                    if (profile == null && e.profile != null && !string.IsNullOrEmpty(e.profile.id)) profile = e.profile;
                }
                else profile = e.profile;
                if (profile == null || string.IsNullOrEmpty(profile.id))
                {
                    Debug.LogWarning("Residents: 住民 " + e.id + " のプロフィールを復元できません（生成規則の変更？）");
                    continue;
                }
                NormalizeState(e.state);
                // セーブからの読み込み（プレイ中のゲスト同期ではない）では演出の保持を解く。
                // 演出（Story 等）が解除を漏らしたまま保存されると、ロード後も永久に止まったままになるため。
                if (!live) e.state.cutsceneHold = false;
                if (residents.TryGetValue(e.id, out var existing))
                {
                    var oldJob = existing.State.job;
                    // 引っ越していった日の観察記録はホストが知っていたプレイヤー全員へ届ける（MoveOut → RecordObservationFor）
                    existing.State = e.state;
                    if (!e.regenerable) existing.Profile = profile;
                    if (live && oldJob != e.state.job) RebuildJobProp(existing);
                    if (live && existing.Avatar != null)
                    {
                        existing.Avatar.NetTarget = existing.StatePosition;
                        existing.Avatar.NetYaw = e.state.rotY;
                        existing.Avatar.HasNetTarget = !IsAuthority;
                        if (IsAuthority) existing.Avatar.transform.position = existing.StatePosition;
                    }
                }
                else
                {
                    AddResident(new Resident(profile, e.state, e.regenerable));
                    membershipChanged = true;
                }
            }
            for (int i = ordered.Count - 1; i >= 0; i--)
            {
                var r = ordered[i];
                if (seen.Contains(r.Id)) continue;
                if (r.Avatar != null) UnityEngine.Object.Destroy(r.Avatar.gameObject);
                residents.Remove(r.Id);
                ordered.RemoveAt(i);
                membershipChanged = true;
            }
            UpdateGuideFlag();
            ReapplyHomes();
            if (live)
            {
                foreach (var r in ordered)
                {
                    SyncAvatar(r);
                    if (r.Avatar != null && !IsAuthority)
                    {
                        r.Avatar.HasNetTarget = true;
                        r.Avatar.NetTarget = r.StatePosition;
                        r.Avatar.NetYaw = r.State.rotY;
                    }
                }
                RefreshBoxes();
            }
            if (membershipChanged && live && !IsAuthority)
            {
                // ゲスト側：家の見た目の作り直し等のため、増減をローカルにも知らせる必要はない（ホストのイベントで World が処理）
            }
        }

        private static void NormalizeState(ResidentState s)
        {
            if (s.items == null) s.items = new List<ItemStack>();
            s.items.RemoveAll(x => x == null || x.IsEmpty);
            if (s.homeItems == null) s.homeItems = new List<string>();
            if (s.plan == null) s.plan = new List<ScheduleBlock>();
            if (s.promises == null) s.promises = new List<Promise>();
            if (s.relations == null) s.relations = new List<RelationEntry>();
            if (s.bonds == null) s.bonds = new List<PlayerBond>();
            if (s.memories == null) s.memories = new List<ResidentMemoryEntry>();
            if (s.midGoal == null) s.midGoal = new GoalState();
            if (s.longGoal == null) s.longGoal = new GoalState();
        }
    }
}
