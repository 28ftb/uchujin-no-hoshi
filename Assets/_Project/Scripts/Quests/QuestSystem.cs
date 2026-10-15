using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Quests
{
    /// <summary>
    /// クエスト・住民イベント・依頼帳・村の行事（クエスト仕様 v0.2）。
    ///
    /// 中心思想：「クエストをクリアするために住民と話すのではなく、住民と暮らしていたら自然とクエストが生まれる」。
    /// ・5系統：日常のお願い（§6）／日替わり掲示板（§6.2）／自発的なお手伝い（§7）／暮らしから生まれる依頼（§8）／個別ストーリー（§9）
    ///   ＋村の行事（§13、四季なし）。メインのロケット修理（§15）は Rocket 担当。ここでは修理屋の手掛かり・夢の物語のみ。
    /// ・宇宙語が分からなくても成り立つ（§4）：手掛かりは住民が実際に出した身振り・見本・数・表情だけ（§4.2）。
    /// ・主人公は話さない（§5.1）：うなずく・首を振る・考える・物を渡す・運ぶ で応える。
    /// ・依頼帳（§11）：宇宙語のまま記録し、自動翻訳しない。
    /// ・マルチ（§17）：依頼はプレイヤーごとのインスタンス、行事・掲示板・お手伝いの状況は星で共通。
    /// </summary>
    public sealed partial class QuestSystem : GameSystemBase, IQuestService, IResidentInteractionProvider
    {
        public override string SaveKey => "quests";
        public override int DailyUpdateOrder => 480;

        /// <summary>星の状態（ホストが正本）。</summary>
        internal QuestPlanetSave S = new QuestPlanetSave();
        /// <summary>ローカルプレイヤーの依頼帳。</summary>
        internal QuestKnowledgeSave K = new QuestKnowledgeSave();

        private ulong seed;
        private RequestBookApp bookApp;
        internal QuestSceneView View { get; private set; }
        internal readonly List<ResidentStory> Stories = new List<ResidentStory>();
        private IResidentDirectory providerRegisteredWith;
        private bool subscribed;
        private float localCheckTimer;

        // ---- ローカル（保存しない）----
        private class PendingOffer
        {
            public string questId;
            public float time;
            public int clue;
        }

        private class AwaitRecord
        {
            public string residentId;
            public string contextTag;
            public string entryId;
            public string scene;
            public float time;
        }

        private readonly Dictionary<string, PendingOffer> pendingOffers = new Dictionary<string, PendingOffer>();
        private readonly Dictionary<string, float> abandonAsks = new Dictionary<string, float>();
        private readonly List<AwaitRecord> awaiting = new List<AwaitRecord>();
        private readonly HashSet<string> attendSent = new HashSet<string>();
        private bool handlingGive;

        public QuestSystem()
        {
            Stories.Add(new BlueFlowerStory());
            Stories.Add(new FishingMasterStory());
            Stories.Add(new AspiringCookStory());
            Stories.Add(new LittleTravelerStory());
            Stories.Add(new MechanicDreamStory());
        }

        // ------------------------------------------------------------
        // 共通アクセサ
        // ------------------------------------------------------------

        internal IResidentDirectory Dir => Game != null ? Game.Service<IResidentDirectory>() : null;
        internal ISpeechService Speech => Game != null ? Game.Service<ISpeechService>() : null;
        internal ITabletService Tablet => Game != null ? Game.Service<ITabletService>() : null;
        internal int Today => Clock != null ? Clock.Day : 1;
        internal int Minute => Clock != null ? Clock.MinuteOfDay : 8 * 60;
        internal long TotalMinutes => Clock != null ? Clock.TotalMinutes : 0;
        internal WorldLayout Layout => Game != null ? Game.Layout : null;
        internal string LocalId => Game != null ? Game.LocalPlayerId : null;
        internal bool Authority => IsAuthority;

        internal SeededRandom Rng(string key) => SeededRandom.For(seed, key);
        internal ulong Seed => seed;

        internal IResidentInfo R(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return null;
            return Dir?.Get(residentId);
        }

        internal bool IsActiveResident(string residentId)
        {
            var r = R(residentId);
            return r != null && r.IsActive;
        }

        internal List<IResidentInfo> ActiveResidents()
        {
            var list = new List<IResidentInfo>();
            var d = Dir;
            if (d == null) return list;
            foreach (var r in d.ActiveResidents) if (r != null && r.IsActive) list.Add(r);
            // ID順で並べて決定的にする（辞書の列挙順に依存しない）
            list.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return list;
        }

        internal Vector3 Ground(Vector3 p) => Layout != null ? Layout.OnGround(p) : p;

        internal Vector3 HomeOf(IResidentInfo r)
        {
            if (r == null) return Vector3.zero;
            var f = Layout?.GetFacilityById(r.HomeFacilityId);
            if (f != null) return Ground(f.entrance != Vector3.zero ? f.entrance : f.position);
            return r.Position;
        }

        /// <summary>住民の仕事場（ownerResidentId で割り当てられた施設）。無ければ家。</summary>
        internal Vector3 WorkplaceOf(IResidentInfo r)
        {
            if (r == null) return Vector3.zero;
            var l = Layout;
            if (l != null)
            {
                foreach (var f in l.Facilities)
                {
                    if (f.ownerResidentId == r.Id && f.type != FacilityType.ResidentHouse)
                        return Ground(f.entrance != Vector3.zero ? f.entrance : f.position);
                }
            }
            return HomeOf(r);
        }

        internal Vector3 FacilityPos(FacilityType t, Vector3 fallback)
        {
            var f = Layout?.GetFacility(t);
            if (f == null) return fallback;
            return Ground(f.entrance != Vector3.zero ? f.entrance : f.position);
        }

        internal Vector3 PlazaPos()
        {
            var l = Layout;
            if (l == null) return Vector3.zero;
            var f = l.GetFacility(FacilityType.Plaza);
            if (f != null) return Ground(f.position);
            foreach (var a in l.GetAreas(AreaType.VillageCenter)) return Ground(a.center);
            return Ground(l.PlayerSpawn);
        }

        /// <summary>高台（星の光帯を見る場所）。</summary>
        internal Vector3 HillPos()
        {
            var l = Layout;
            if (l == null) return Vector3.zero;
            var f = l.GetFacility(FacilityType.Lookout);
            if (f != null) return Ground(f.position);
            foreach (var a in l.GetAreas(AreaType.Hill)) return Ground(a.center);
            foreach (var a in l.GetAreas(AreaType.Mountain)) return Ground(a.center);
            return PlazaPos() + new Vector3(12f, 0f, 12f);
        }

        internal Vector3 RocketPos()
        {
            var rocket = Game?.Service<IRocketService>();
            if (rocket != null) return Ground(rocket.RocketPosition);
            var l = Layout;
            return l != null ? Ground(l.CrashSite) : Vector3.zero;
        }

        internal PlayerState LocalPlayerState => LocalPlayer;

        internal UtteranceRecord Say(string residentId, Utterance u, SpeechChannel ch = SpeechChannel.Direct)
        {
            if (u == null || string.IsNullOrEmpty(residentId)) return null;
            var d = Dir;
            if (d == null) return null;
            try { return d.Speak(residentId, u, ch); }
            catch (Exception e) { Debug.LogException(e); return null; }
        }

        internal void GoTo(string residentId, Vector3 pos, int untilMinute, string tag)
        {
            if (!IsActiveResident(residentId)) return;
            // 日をまたぐ時刻は翌日の同時刻として渡す（RequestGoTo は「今より前の時刻」を翌日扱いにする。
            // 上限 23:59 に丸めると、23:59 に呼んだ時に丸1日留まってしまう）
            int until = ((untilMinute % TimeSystem.MinutesPerDay) + TimeSystem.MinutesPerDay) % TimeSystem.MinutesPerDay;
            try { Dir?.RequestGoTo(residentId, Ground(pos), until, tag); }
            catch (Exception e) { Debug.LogException(e); }
        }

        internal void Memory(string residentId, string kind, string playerId, string itemId = null, string other = null, float importance = 0.5f, string detail = null)
        {
            if (string.IsNullOrEmpty(residentId)) return;
            Dir?.AddMemory(residentId, new ResidentMemoryEntry
            {
                kind = kind,
                playerId = playerId,
                itemId = itemId,
                otherResidentId = other,
                day = Today,
                importance = importance,
                detail = detail
            });
        }

        internal void Friendship(string residentId, string playerId, int delta, string reason)
        {
            if (string.IsNullOrEmpty(residentId) || string.IsNullOrEmpty(playerId) || delta == 0) return;
            Dir?.ChangeFriendship(residentId, playerId, delta, reason);
        }

        internal int FriendshipOf(string residentId, string playerId)
        {
            var r = R(residentId);
            return r != null ? r.GetFriendship(playerId) : 0;
        }

        internal QuestInstance FindQuest(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var q in S.quests) if (q.id == id) return q;
            return null;
        }

        internal StoryState FindStory(string storyId, string playerId)
        {
            foreach (var s in S.stories) if (s.storyId == storyId && s.playerId == playerId) return s;
            return null;
        }

        internal ResidentStory StoryDef(string storyId)
        {
            foreach (var s in Stories) if (s.Id == storyId) return s;
            return null;
        }

        internal VillageEvent FindEvent(string id)
        {
            foreach (var e in S.events) if (e.id == id) return e;
            return null;
        }

        internal HelpEvent FindHelp(string id)
        {
            foreach (var h in S.helps) if (h.id == id) return h;
            return null;
        }

        internal void SetStatus(QuestInstance q, QuestStatus status)
        {
            if (q == null || q.status == status) return;
            q.status = status;
            if (status == QuestStatus.Completed || status == QuestStatus.Expired || status == QuestStatus.Declined || status == QuestStatus.Failed)
                q.finishedDay = Today;
            EventBus.Publish(new QuestStateChangedEvent { QuestId = q.id, ResidentId = q.residentId, Status = status, PlayerId = q.playerId });
        }

        internal void Changed()
        {
            MarkDirty();
            View?.Refresh();
        }

        internal string NewId(string prefix) => prefix + Ids.New().Substring(0, 12);

        internal static string PlayerLabel(string playerId)
        {
            var net = Game.Instance != null ? Game.Instance.Service<INetworkService>() : null;
            if (net != null && !string.IsNullOrEmpty(playerId))
            {
                var n = net.GetPlayerName(playerId);
                if (!string.IsNullOrEmpty(n)) return n;
            }
            return Loc.T("だれか", "someone");
        }

        // ------------------------------------------------------------
        // ライフサイクル
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            QuestCatalog.RegisterItems();
            WorldAuthority.Register<QuestRespondCommand>(HandleRespond);
            WorldAuthority.Register<QuestGiveCommand>(HandleGive);
            WorldAuthority.Register<QuestTakePaperCommand>(HandleTakePaper);
            WorldAuthority.Register<QuestHelpCommand>(HandleHelp);
            WorldAuthority.Register<QuestAttendCommand>(HandleAttend);
            WorldAuthority.Register<QuestMarketBuyCommand>(HandleMarketBuy);
            WorldAuthority.Register<QuestStoryCommand>(HandleStoryCommand);
            WorldAuthority.Register<QuestAckCommand>(HandleAck);
            WorldAuthority.Register<QuestProgressCommand>(HandleProgress);
            WorldAuthority.Register<QuestReportCommand>(HandleReport);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            seed = ctx.Seed;
            S = new QuestPlanetSave();
            K = new QuestKnowledgeSave();
            ClearTransient();
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            S = new QuestPlanetSave();
        }

        public override void BuildScene()
        {
            View?.Destroy();
            View = new QuestSceneView(this);
            View.Build();
        }

        public override void OnPlayStarted()
        {
            if (!subscribed)
            {
                EventBus.Subscribe<UtteranceSpokenEvent>(OnUtteranceSpoken);
                EventBus.Subscribe<FishCaughtEvent>(OnFishCaught);
                EventBus.Subscribe<CreatureCaughtEvent>(OnCreatureCaught);
                EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
                EventBus.Subscribe<ResidentMovedInEvent>(OnResidentMovedIn);
                EventBus.Subscribe<ResidentMoveOutDecidedEvent>(OnResidentMoveOutDecided);
                EventBus.Subscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
                EventBus.Subscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
                EventBus.Subscribe<PlayerJoinedEvent>(OnPlayerJoined);
                EventBus.Subscribe<ShopItemSoldEvent>(OnShopItemSold);
                subscribed = true;
            }

            var tablet = Tablet;
            if (tablet != null)
            {
                if (bookApp == null) bookApp = new RequestBookApp(this);
                tablet.RegisterApp(bookApp);
            }

            var dir = Dir;
            if (dir != null && !ReferenceEquals(dir, providerRegisteredWith))
            {
                dir.RegisterInteractionProvider(this);
                providerRegisteredWith = dir;
            }

            // 1日目は OnDayStarted が呼ばれないので、ここでその日の分を用意する
            if (IsAuthority && S.lastDayProcessed < Today) DailyProcess(Today);
            View?.Refresh();
        }

        public override void Teardown()
        {
            if (subscribed)
            {
                EventBus.Unsubscribe<UtteranceSpokenEvent>(OnUtteranceSpoken);
                EventBus.Unsubscribe<FishCaughtEvent>(OnFishCaught);
                EventBus.Unsubscribe<CreatureCaughtEvent>(OnCreatureCaught);
                EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
                EventBus.Unsubscribe<ResidentMovedInEvent>(OnResidentMovedIn);
                EventBus.Unsubscribe<ResidentMoveOutDecidedEvent>(OnResidentMoveOutDecided);
                EventBus.Unsubscribe<ResidentMovedOutEvent>(OnResidentMovedOut);
                EventBus.Unsubscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
                EventBus.Unsubscribe<PlayerJoinedEvent>(OnPlayerJoined);
                EventBus.Unsubscribe<ShopItemSoldEvent>(OnShopItemSold);
                subscribed = false;
            }
            View?.Destroy();
            View = null;
            ClearTransient();
            S = new QuestPlanetSave();
            K = new QuestKnowledgeSave();
        }

        private void ClearTransient()
        {
            pendingOffers.Clear();
            abandonAsks.Clear();
            awaiting.Clear();
            attendSent.Clear();
            handlingGive = false;
        }

        public override void Tick(float deltaTime)
        {
            View?.Tick(deltaTime);
            localCheckTimer -= deltaTime;
            if (localCheckTimer > 0f) return;
            localCheckTimer = 0.5f;
            try
            {
                LocalHelpCheck();
                LocalEventCheck();
                LocalStoryCheck();
                CleanupAwaiting();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public override void OnGameMinutesAdvanced(int minutes)
        {
            if (!IsAuthority) return;
            UpdateEvents();
            foreach (var st in S.stories.ToArray())
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnMinutes(this, st);
            }
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (!IsAuthority) return;
            HourlyHelp(day, hour);
            HourlyEvents(day, hour);
            foreach (var st in S.stories.ToArray())
            {
                if (st.finished) continue;
                if (!IsActiveResident(st.residentId)) continue;
                StoryDef(st.storyId)?.OnHour(this, st, day, hour);
            }
            Changed();
        }

        public override void OnDayStarted(int day)
        {
            if (!IsAuthority) return;
            DailyProcess(day);
        }

        // ------------------------------------------------------------
        // セーブ（Planet：依頼の実体・行事など ／ PlayerPlanetKnowledge：依頼帳）
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    return JsonUtility.ToJson(S);
                case SaveScope.PlayerPlanetKnowledge:
                    if (playerId != null && playerId != LocalId) return null;
                    return JsonUtility.ToJson(K);
                default:
                    return null;
            }
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                if (scope == SaveScope.Planet)
                {
                    var s = JsonUtility.FromJson<QuestPlanetSave>(json);
                    if (s == null) return;
                    Normalize(s);
                    S = s;
                    View?.Refresh();
                }
                else if (scope == SaveScope.PlayerPlanetKnowledge)
                {
                    if (playerId != null && playerId != LocalId) return;
                    var k = JsonUtility.FromJson<QuestKnowledgeSave>(json);
                    if (k == null) return;
                    if (k.entries == null) k.entries = new List<RequestBookEntry>();
                    K = k;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static void Normalize(QuestPlanetSave s)
        {
            if (s.quests == null) s.quests = new List<QuestInstance>();
            if (s.board == null) s.board = new List<BoardPaper>();
            if (s.helps == null) s.helps = new List<HelpEvent>();
            if (s.stories == null) s.stories = new List<StoryState>();
            if (s.events == null) s.events = new List<VillageEvent>();
            if (s.marks == null) s.marks = new List<LifeMark>();
            if (s.pendings == null) s.pendings = new List<PendingTalk>();
            if (s.generatedDay == null) s.generatedDay = new List<StringIntPair>();
            if (s.repairHintDay == null) s.repairHintDay = new List<StringIntPair>();
            if (s.fishSizes == null) s.fishSizes = new List<StringFloatPair>();
            foreach (var q in s.quests) if (q.goals == null) q.goals = new List<QuestGoal>();
            foreach (var p in s.board) if (p.template != null && p.template.goals == null) p.template.goals = new List<QuestGoal>();
            foreach (var st in s.stories) if (st.flags == null) st.flags = new List<string>();
            foreach (var e in s.events)
            {
                if (e.attendees == null) e.attendees = new List<string>();
                if (e.gatherers == null) e.gatherers = new List<string>();
                if (e.stalls == null) e.stalls = new List<MarketStall>();
            }
        }

        // ------------------------------------------------------------
        // 共有イベントへの反応
        // ------------------------------------------------------------

        private void OnPlayerJoined(PlayerJoinedEvent e)
        {
            if (!IsAuthority) return;
            GenerateFavorsFor(e.PlayerId, Today);
            StartStoriesFor(e.PlayerId, Today);
            Changed();
        }

        private void OnFishCaught(FishCaughtEvent e)
        {
            // 魚の大きさの記録（「大きい魚」の依頼の目安：§12.2 一定以上のサイズ）
            if (IsAuthority && e.SizeCm > 0f) RecordFishSize(e.ItemId, e.SizeCm);
            if (IsAuthority) ApplyProgress(e.PlayerId, "fish", e.ItemId, null, e.SizeCm);
            else if (e.PlayerId == LocalId)
                WorldAuthority.Submit(new QuestProgressCommand { kind = "fish", itemId = e.ItemId, size = e.SizeCm });
            if (e.PlayerId == LocalId) LocalStoryFish(e);
        }

        private void OnCreatureCaught(CreatureCaughtEvent e)
        {
            if (IsAuthority) ApplyProgress(e.PlayerId, "creature", null, e.SpeciesId, 0f);
            else if (e.PlayerId == LocalId)
                WorldAuthority.Submit(new QuestProgressCommand { kind = "creature", speciesId = e.SpeciesId });
        }

        private CommandResult HandleProgress(QuestProgressCommand c, CommandContext ctx)
        {
            if (c.kind == "fish" && c.size > 0f) RecordFishSize(c.itemId, c.size);
            ApplyProgress(ctx.IssuerId, c.kind, c.itemId, c.speciesId, c.size);
            return CommandResult.Ok();
        }

        /// <summary>捕獲・釣果で進む目標（生き物を捕まえて見せる、釣り勝負など）。</summary>
        private void ApplyProgress(string playerId, string kind, string itemId, string speciesId, float size)
        {
            bool changed = false;
            foreach (var q in S.quests)
            {
                if (q.playerId != playerId || q.status != QuestStatus.Active) continue;
                foreach (var g in q.goals)
                {
                    if (kind == "creature" && g.type == GoalType.CatchCreature && g.speciesId == speciesId && !g.IsDone)
                    {
                        g.progress = g.count;
                        changed = true;
                    }
                }
            }
            foreach (var st in S.stories)
            {
                if (st.playerId != playerId || st.finished) continue;
                if (StoryDef(st.storyId)?.OnProgress(this, st, kind, itemId, speciesId, size) == true) changed = true;
            }
            if (changed) Changed();
        }

        private void RecordFishSize(string itemId, float size)
        {
            if (string.IsNullOrEmpty(itemId)) return;
            foreach (var p in S.fishSizes)
            {
                if (p.key == itemId)
                {
                    // 指数移動平均（だいたいの「ふつうの大きさ」）
                    p.value = p.value * 0.8f + size * 0.2f;
                    return;
                }
            }
            S.fishSizes.Add(new StringFloatPair(itemId, size));
        }

        internal float TypicalFishSize(string itemId)
        {
            foreach (var p in S.fishSizes) if (p.key == itemId) return p.value;
            return 0f;
        }

        private void OnShopItemSold(ShopItemSoldEvent e)
        {
            if (!IsAuthority) return;
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnShopSold(this, st, e);
            }
        }

        private void OnRocketRepaired(RocketDeviceRepairedEvent e)
        {
            if (!IsAuthority) return;
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnRocketRepaired(this, st, e);
            }
            Changed();
        }
    }
}
