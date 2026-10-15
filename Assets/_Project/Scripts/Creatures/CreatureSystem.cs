using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 宇宙生物システム（生き物仕様 v0.2 全体）。
    ///  ・50種の種族データ、野生の出現（場所・時間帯・天候・宇宙固有現象。四季なし）と行動、捕獲（網・忍び足・餌・タイミング・発見）
    ///  ・所有個体（固有ID・個体差・成長・なつき度・お世話・配置）と星間移動（同行／預ける／譲る：マルチ §10）
    ///  ・生き物図鑑（タブレット "bestiary"）、生き物取りレベル・名人の技・網の強化
    /// 保存：
    ///  ・Planet …… 星に置いた個体（農場・自宅・器具・作業場など。持ち主は誰でも）、野生の個体
    ///  ・PlayerProfile …… 持ち歩く個体（同行・ケージ）、預けた個体の控え、生き物取りレベル・技
    ///  ・PlayerPlanetKnowledge …… 生き物図鑑（見た・捕まえた・観察した事実・自由メモ）
    /// </summary>
    public sealed partial class CreatureSystem : GameSystemBase, ICreatureService
    {
        public override string SaveKey => "creatures";
        public override int DailyUpdateOrder => 250;

        /// <summary>モジュール内の UI から参照する（他モジュールは ICreatureService を使うこと）。</summary>
        public static CreatureSystem Instance { get; private set; }

        /// <summary>GetOwned に渡すと、この星にいる全員分＋自分の持ち歩く個体を返す（Farming など星全体を見る用途）。</summary>
        public const string AnyOwner = "*";

        // ---------------- 【仮数値】 ----------------
        public const int MaxCompanions = 3;
        public const int MaxPlacedPerPlanet = 80;
        public static readonly int[] ExpForLevel = { 0, 0, 30, 80, 160, 300 }; // index=level
        public const int MaxCreatureLevel = 5;
        public static readonly int[] CatchExpForLevel = { 0, 0, 40, 120, 250, 450, 700, 1000, 1400, 1900, 2500 };
        public const int MaxCatchLevel = 10;
        public const float BreedChancePerDay = 0.04f;

        // ---------------- 状態 ----------------
        /// <summary>ローカルプレイヤーが持ち歩く個体（＋預けた個体の控え：placement=Deposited）。</summary>
        private readonly Dictionary<string, OwnedCreature> carried = new Dictionary<string, OwnedCreature>();
        /// <summary>この星に置かれている個体（持ち主は問わない）。</summary>
        private readonly Dictionary<string, OwnedCreature> placed = new Dictionary<string, OwnedCreature>();
        private readonly Dictionary<string, WildRecord> wilds = new Dictionary<string, WildRecord>();
        private readonly Dictionary<string, BestiaryEntry> bestiary = new Dictionary<string, BestiaryEntry>();

        public int CatchLevel { get; private set; } = 1;
        public int CatchExp { get; private set; }
        public int TotalCaught { get; private set; }
        private readonly HashSet<string> techniques = new HashSet<string>();
        private readonly Dictionary<string, int> dailyShown = new Dictionary<string, int>();

        private BestiaryApp bestiaryApp;
        private CreatureToolHandler toolHandler;
        private CreatureExpertProvider expertProvider;
        private bool sceneBuilt;
        private float adoptScanTimer;

        public CreatureSystem()
        {
            Instance = this;
        }

        // =====================================================================
        // ライフサイクル
        // =====================================================================

        public override void RegisterContent()
        {
            Instance = this;
            CreatureDatabase.RegisterItems();
            RegisterCommands();
            foreach (var sp in CreatureDatabase.All) Concepts.Register(Concepts.Species(sp.Id), ConceptKind.Noun);
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            placed.Clear();
            wilds.Clear();
            bestiary.Clear();
            // ロード時は持ち歩く個体をセーブから復元するので一旦空にする（ゲスト訪問時は自分の持ち物をそのまま持ち込む）
            if (!ctx.IsNewPlanet && WorldAuthority.Role != NetRole.Guest) ResetProfile();
            spawnRng = null;
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            // 最初の星の新規ゲームなら持ち歩く個体・技能も初期化（「次の星へ」は引き継ぐ）
            if (ctx.Planet.planetIndex <= 1 && string.IsNullOrEmpty(ctx.Planet.previousWorldId)) ResetProfile();
        }

        private void ResetProfile()
        {
            carried.Clear();
            CatchLevel = 1;
            CatchExp = 0;
            TotalCaught = 0;
            techniques.Clear();
            dailyShown.Clear();
        }

        public override void BuildScene()
        {
            sceneBuilt = true;
            EnsureRoots();
            RefreshWildViews();
            RefreshOwnedAvatars();
        }

        public override void OnPlayStarted()
        {
            CleanupStubsForThisWorld();
            EventBus.Subscribe<AreaChangedEvent>(OnAreaChanged);
            EventBus.Subscribe<OreMinedEvent>(OnOreMined);
            EventBus.Subscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Subscribe<CreatureObservedEvent>(OnCreatureObservedExternal);
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Subscribe<EndingFinishedEvent>(OnEndingFinished);

            toolHandler = toolHandler ?? new CreatureToolHandler(this);
            ToolUseRouter.Register(toolHandler);

            var tablet = Game.Service<ITabletService>();
            if (tablet != null)
            {
                bestiaryApp = bestiaryApp ?? new BestiaryApp(this);
                tablet.RegisterApp(bestiaryApp);
            }
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null)
            {
                expertProvider = expertProvider ?? new CreatureExpertProvider(this);
                dir.RegisterInteractionProvider(expertProvider);
            }
            RefreshOwnedAvatars();
        }

        public override void Teardown()
        {
            EventBus.Unsubscribe<AreaChangedEvent>(OnAreaChanged);
            EventBus.Unsubscribe<OreMinedEvent>(OnOreMined);
            EventBus.Unsubscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Unsubscribe<CreatureObservedEvent>(OnCreatureObservedExternal);
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Unsubscribe<EndingFinishedEvent>(OnEndingFinished);
            if (toolHandler != null) ToolUseRouter.Unregister(toolHandler);
            DestroyViews();
            sceneBuilt = false;
            // 持ち歩く個体（carried）と技能はプレイヤーの資産なので消さない（次の星へ持っていく）
            placed.Clear();
            wilds.Clear();
            bestiary.Clear();
            baits.Clear();
            pendingMineSpawn = null;
        }

        public override void Tick(float deltaTime)
        {
            if (Game == null || Game.State != GameState.Playing) return;
            if (IsAuthority) UpdateSpawning(deltaTime);
            UpdatePendingMineSpawn(deltaTime);
            UpdatePairInteractions(deltaTime);
            UpdateBaits(deltaTime);
            adoptScanTimer -= deltaTime;
            if (adoptScanTimer <= 0f)
            {
                adoptScanTimer = 0.5f;
                AdoptIncomingCases();
            }
        }

        public override void OnHourChanged(int day, int hour)
        {
            if (!IsAuthority) return;
            spawnRng = null; // 時間帯が変わったら出現の乱数列を切り替える
            ExpireWilds(true);
        }

        public override void OnDayStarted(int day)
        {
            dailyShown.Clear();
            // 持ち歩く個体（ローカル）
            foreach (var c in carried.Values)
            {
                if (c.placement == CreaturePlacement.Deposited) continue;
                DailyGrowth(c, day, c.placement == CreaturePlacement.Companion ? 4 : 1, c.placement == CreaturePlacement.Companion ? 1 : 0);
            }
            if (IsAuthority)
            {
                foreach (var c in placed.Values)
                {
                    bool working = c.placement == CreaturePlacement.Farm || c.placement == CreaturePlacement.Device || c.placement == CreaturePlacement.RocketWorkshop;
                    bool cozy = c.placement == CreaturePlacement.Home || c.placement == CreaturePlacement.Habitat;
                    DailyGrowth(c, day, working ? 6 : 4, cozy ? 2 : 1);
                }
                TryBreeding(day);
                ExpireWilds(false);
                MarkDirty();
            }
            RefreshOwnedAvatars();
        }

        private void DailyGrowth(OwnedCreature c, int day, int exp, int affection)
        {
            if (c.lastDailyDay == day) return;
            c.lastDailyDay = day;
            c.fedCountToday = 0;
            c.affection = Mathf.Clamp(c.affection + affection, 0, 100);
            AddCreatureExp(c, exp, false);
        }

        // =====================================================================
        // ICreatureService
        // =====================================================================

        public IReadOnlyList<CreatureView> GetOwned(string ownerId)
        {
            var list = new List<CreatureView>();
            string local = LocalPlayer != null ? LocalPlayer.PlayerId : null;
            foreach (var c in placed.Values)
            {
                if (ownerId == AnyOwner || c.ownerId == ownerId) list.Add(ToView(c));
            }
            if (ownerId == AnyOwner || ownerId == local)
            {
                foreach (var c in carried.Values)
                {
                    if (placed.ContainsKey(c.uid)) continue; // 星に置いた個体は星の記録を優先
                    if (c.placement == CreaturePlacement.Deposited && ownerId == AnyOwner) continue;
                    list.Add(ToView(c));
                }
            }
            return list;
        }

        public CreatureView Get(string uid)
        {
            var c = Find(uid);
            return c != null ? ToView(c) : null;
        }

        public IEnumerable<CreatureView> FindOwnedWithAbility(string ownerId, CreatureAbility ability, int minLevel = 1)
        {
            foreach (var v in GetOwned(ownerId))
            {
                if (v.Placement == CreaturePlacement.Deposited) continue;
                if (v.AbilityLevel(ability) >= minLevel) yield return v;
            }
        }

        public bool SetPlacement(string uid, CreaturePlacement placement, string targetId)
        {
            return RequestPlacement(uid, placement, targetId, null);
        }

        public string GetSpeciesName(string speciesId)
        {
            var e = GetEntry(speciesId, false);
            if (e != null && !string.IsNullOrEmpty(e.playerName)) return e.playerName;
            var sp = CreatureDatabase.Get(speciesId);
            return sp != null ? sp.Name : speciesId;
        }

        public IReadOnlyList<CreatureAbility> GetSpeciesAbilities(string speciesId)
        {
            var sp = CreatureDatabase.Get(speciesId);
            return sp != null ? (IReadOnlyList<CreatureAbility>)sp.Abilities : new CreatureAbility[0];
        }

        public bool IsSpeciesDiscovered(string playerId, string speciesId)
        {
            if (LocalPlayer != null && playerId == LocalPlayer.PlayerId)
            {
                var e = GetEntry(speciesId, false);
                if (e != null && e.Seen) return true;
            }
            foreach (var v in GetOwned(playerId)) if (v.SpeciesId == speciesId) return true;
            return false;
        }

        public string GrantCreature(string ownerId, string speciesId, string source)
        {
            var sp = CreatureDatabase.Get(speciesId);
            if (sp == null || string.IsNullOrEmpty(ownerId)) return null;
            var c = CreateIndividual(sp, ownerId, "grant:" + Ids.New(), "gift", source);
            c.affection = 30;
            if (LocalPlayer != null && ownerId == LocalPlayer.PlayerId)
            {
                AdoptLocal(c, null);
                var e = GetEntry(sp.Id, true);
                e.obtainedCount++;
                if (e.firstSeenDay < 0) e.firstSeenDay = Clock != null ? Clock.Day : 0;
                UIManager.Toast(Loc.Format("{0}を迎えました！", "{0} joined you!", sp.Name));
                return c.uid;
            }
            if (IsAuthority && WorldAuthority.Bridge != null)
            {
                WorldAuthority.Bridge.GrantItemToRemote(ownerId, MakeCaseStack(c, true, null), "creature_grant:" + source);
                return c.uid;
            }
            return null;
        }

        public IEnumerable<string> AllSpeciesIds
        {
            get { foreach (var s in CreatureDatabase.All) yield return s.Id; }
        }

        // =====================================================================
        // 個体
        // =====================================================================

        public OwnedCreature Find(string uid)
        {
            if (string.IsNullOrEmpty(uid)) return null;
            if (placed.TryGetValue(uid, out var p)) return p;
            if (carried.TryGetValue(uid, out var c)) return c;
            return null;
        }

        public bool IsPlaced(string uid) => uid != null && placed.ContainsKey(uid);
        public bool IsCarried(string uid) => uid != null && carried.ContainsKey(uid) && !placed.ContainsKey(uid);

        /// <summary>ローカルプレイヤーの個体一覧（UI用。星に置いた個体は星の記録を優先）。</summary>
        public List<OwnedCreature> LocalOwnedList()
        {
            var list = new List<OwnedCreature>();
            string local = LocalPlayer != null ? LocalPlayer.PlayerId : null;
            foreach (var c in placed.Values) if (c.ownerId == local) list.Add(c);
            foreach (var c in carried.Values) if (!placed.ContainsKey(c.uid)) list.Add(c);
            list.Sort((a, b) =>
            {
                int pa = (int)a.placement, pb = (int)b.placement;
                if (pa != pb) return pa.CompareTo(pb);
                return string.CompareOrdinal(a.speciesId, b.speciesId);
            });
            return list;
        }

        public IEnumerable<OwnedCreature> PlacedCreatures => placed.Values;

        public int CompanionCount()
        {
            int n = 0;
            foreach (var c in carried.Values) if (c.placement == CreaturePlacement.Companion && !placed.ContainsKey(c.uid)) n++;
            return n;
        }

        public string DisplayName(OwnedCreature c)
        {
            if (c == null) return "";
            if (!string.IsNullOrEmpty(c.nickname)) return c.nickname;
            return GetSpeciesName(c.speciesId);
        }

        /// <summary>
        /// 個体の見た目の個体差（大きさ・色合い・色違い）を種の文字列から決定的に作る。
        /// 野生で見えていた姿（色違いも）と、捕まえた個体の姿が一致する。
        /// </summary>
        public static SeededRandom IndividualLook(string individualSeed, out float size, out float hue, out int variant)
        {
            var rng = SeededRandom.For(StableHash.Fnv1a64(individualSeed ?? "x"), "creature.individual");
            size = rng.Range(0.85f, 1.2f);
            hue = rng.Range(-0.04f, 0.04f);
            float v = rng.NextFloat();
            variant = v < 0.02f ? 3 : v < 0.15f ? 1 : v < 0.28f ? 2 : 0; // 【仮数値】色違いの確率
            return rng;
        }

        /// <summary>個体を生成（個体差は種の文字列から決定的に作る）。</summary>
        public OwnedCreature CreateIndividual(CreatureSpecies sp, string ownerId, string individualSeed, string origin, string originDetail)
        {
            var rng = IndividualLook(individualSeed ?? Ids.New(), out var size, out var hue, out var variant);
            var c = new OwnedCreature
            {
                uid = Ids.New(),
                speciesId = sp.Id,
                ownerId = ownerId,
                size = size,
                hueShift = hue,
                colorVariant = variant,
                personality = rng.Range(0, 8),
                origin = origin,
                originDetail = originDetail,
                caughtDay = Clock != null ? Clock.Day : 0,
                caughtWorldId = Game != null && Game.Planet != null ? Game.Planet.worldId : null,
                affection = 10,
                level = 1
            };
            if (sp.IsWorker && rng.Chance(0.35f)) c.talent = (int)sp.Abilities[rng.Range(0, sp.Abilities.Length)];
            string[] foods = { "fruit", "flower", "seed", "glow", "veg", "sweet", "mineral", "water", "moss" };
            c.favoriteFood = rng.Chance(0.75f) ? sp.FavoriteFood : foods[rng.Range(0, foods.Length)];
            return c;
        }

        public List<CreatureAbilityLevel> ComputeAbilities(OwnedCreature c)
        {
            var list = new List<CreatureAbilityLevel>();
            var sp = CreatureDatabase.Get(c.speciesId);
            if (sp == null) return list;
            foreach (var a in sp.Abilities)
            {
                int lv = 1;
                if (c.level >= 3) lv++;
                if (c.level >= 5) lv++;
                if (c.talent == (int)a) lv++;
                list.Add(new CreatureAbilityLevel { ability = a, level = Mathf.Clamp(lv, 1, 4) });
            }
            return list;
        }

        public CreatureView ToView(OwnedCreature c)
        {
            return new CreatureView
            {
                Uid = c.uid,
                SpeciesId = c.speciesId,
                Nickname = DisplayName(c),
                OwnerId = c.ownerId,
                Placement = c.placement,
                PlacementTargetId = c.targetId,
                Abilities = ComputeAbilities(c),
                Affection = c.affection,
                Level = c.level
            };
        }

        /// <summary>成長（経験値）。なつき度が高いと伸びやすい。</summary>
        public void AddCreatureExp(OwnedCreature c, int amount, bool notify)
        {
            if (c == null || amount <= 0) return;
            if (c.affection >= 70) amount = Mathf.CeilToInt(amount * 1.5f);
            c.exp += amount;
            int before = c.level;
            while (c.level < MaxCreatureLevel && c.exp >= ExpForLevel[c.level + 1]) c.level++;
            if (c.level != before)
            {
                if (notify || (LocalPlayer != null && c.ownerId == LocalPlayer.PlayerId))
                    UIManager.Toast(Loc.Format("{0}が大きくなった！（Lv{1}）", "{0} grew! (Lv{1})", DisplayName(c), c.level));
                RefreshAvatar(c.uid, true);
            }
        }

        /// <summary>自分の個体として迎える（持ち歩く）。</summary>
        public void AdoptLocal(OwnedCreature c, string placementAfter)
        {
            if (c == null || LocalPlayer == null) return;
            c.ownerId = LocalPlayer.PlayerId;
            CreaturePlacement p = CreaturePlacement.Companion;
            if (!string.IsNullOrEmpty(placementAfter) && Enum.TryParse(placementAfter, out CreaturePlacement parsed) && PlacementText.IsCarried(parsed)) p = parsed;
            // 控え（預けた個体の記録）があれば置き換える
            carried.Remove(c.uid);
            if (p == CreaturePlacement.Companion && CompanionCount() >= MaxCompanions) p = CreaturePlacement.Storage;
            c.placement = p;
            c.targetId = null;
            c.residentWorldId = null;
            carried[c.uid] = c;
            RefreshOwnedAvatars();
        }

        private void CleanupStubsForThisWorld()
        {
            if (Game == null || Game.Planet == null) return;
            var remove = new List<string>();
            foreach (var c in carried.Values)
            {
                if (c.placement != CreaturePlacement.Deposited) continue;
                if (c.residentWorldId == Game.Planet.worldId && !placed.ContainsKey(c.uid) && IsAuthority) remove.Add(c.uid);
            }
            foreach (var id in remove) carried.Remove(id);
        }

        // =====================================================================
        // 生き物図鑑（PlayerPlanetKnowledge）
        // =====================================================================

        public BestiaryEntry GetEntry(string speciesId, bool create)
        {
            if (string.IsNullOrEmpty(speciesId)) return null;
            if (bestiary.TryGetValue(speciesId, out var e)) return e;
            if (!create) return null;
            e = new BestiaryEntry { speciesId = speciesId };
            bestiary[speciesId] = e;
            return e;
        }

        public int DiscoveredSpeciesCount()
        {
            int n = 0;
            foreach (var e in bestiary.Values) if (e.Seen) n++;
            return n;
        }

        public int CaughtSpeciesCount()
        {
            int n = 0;
            foreach (var e in bestiary.Values) if (e.caughtCount > 0 || e.obtainedCount > 0) n++;
            return n;
        }

        /// <summary>見かけた（客観記録：場所・時刻・天候）。初めてなら true。</summary>
        public bool RecordSeen(string speciesId, Vector3 position, string areaId)
        {
            var e = GetEntry(speciesId, true);
            bool first = !e.Seen;
            e.seenCount++;
            int day = Clock != null ? Clock.Day : 0;
            if (e.firstSeenDay < 0) e.firstSeenDay = day;
            if (Clock != null) e.hourMask |= 1 << Mathf.Clamp(Clock.Hour, 0, 23);
            string place = DescribePlace(position, areaId);
            if (!string.IsNullOrEmpty(place) && !e.areas.Contains(place) && e.areas.Count < 12) e.areas.Add(place);
            var weather = Game.Service<IWeatherService>();
            if (weather != null)
            {
                string w = weather.Current.Summary();
                if (!string.IsNullOrEmpty(w) && !e.weathers.Contains(w) && e.weathers.Count < 10) e.weathers.Add(w);
            }
            if (first)
            {
                var sp = CreatureDatabase.Get(speciesId);
                UIManager.Toast(Loc.Format("見たことのない生き物を見つけた！（図鑑 No.{0}）", "Found a creature you've never seen! (Bestiary No.{0})", sp != null ? sp.NumberLabel : "?"));
                AddCatchExp(6);
                EventBus.Publish(new CreatureObservedEvent { PlayerId = LocalPlayer?.PlayerId, SpeciesId = speciesId, Behavior = "seen" });
            }
            return first;
        }

        public string DescribePlace(Vector3 position, string areaId)
        {
            if (AreaIds.IsMine(areaId)) return Loc.Format("鉱山{0}階", "Mine floor {0}", AreaIds.MineFloorOf(areaId));
            var layout = Game != null ? Game.Layout : null;
            if (layout != null)
            {
                if (layout.FarmRect.width > 0 && layout.FarmRect.Contains(new Vector2(position.x, position.z))) return Loc.T("農場", "Farm");
                var reg = layout.GetAreaAt(position);
                if (reg != null) return Loc.T(reg.nameJa, reg.nameEn);
            }
            return null;
        }

        /// <summary>行動を観察した（初めてなら記録し、得意そうなことが分かる）。</summary>
        public bool RecordBehavior(string speciesId, string key, bool toast = true)
        {
            if (string.IsNullOrEmpty(speciesId) || string.IsNullOrEmpty(key)) return false;
            var e = GetEntry(speciesId, true);
            bool isNew = !e.HasBehavior(key);
            if (isNew)
            {
                e.behaviors.Add(new StringIntPair(key, Clock != null ? Clock.Day : 0));
                var fact = CreatureDatabase.GetFact(key);
                CreatureAbility[] reveals = fact != null ? fact.Reveals : ParseAbilityKey(key);
                foreach (var a in reveals) if (!e.revealedAbilities.Contains((int)a)) e.revealedAbilities.Add((int)a);
                if (toast)
                {
                    string name = GetSpeciesName(speciesId);
                    string text = fact != null ? fact.Text : Loc.T("新しい様子を見た", "saw something new");
                    UIManager.Toast(Loc.Format("観察：{0}が{1}", "Observed: {0} — {1}", name, text), 4.5f);
                }
                AddCatchExp(4);
            }
            // 同じ行動の観察イベントは1時間（ゲーム内）に1回まで（クエスト等への通知が多すぎないように）
            string pk = speciesId + "|" + key;
            long now = Clock != null ? Clock.TotalMinutes : 0;
            if (isNew || !lastObservedPublish.TryGetValue(pk, out var last) || now - last >= 60)
            {
                lastObservedPublish[pk] = now;
                EventBus.Publish(new CreatureObservedEvent { PlayerId = LocalPlayer?.PlayerId, SpeciesId = speciesId, Behavior = key });
            }
            return isNew;
        }

        private static CreatureAbility[] ParseAbilityKey(string key)
        {
            // 他モジュール向けの約束："ability.<CreatureAbility名>" を発行すると、その能力が図鑑で分かる
            if (key != null && key.StartsWith("ability.") && Enum.TryParse(key.Substring(8), out CreatureAbility a)) return new[] { a };
            return new CreatureAbility[0];
        }

        private bool publishingObservation;
        private readonly Dictionary<string, long> lastObservedPublish = new Dictionary<string, long>();

        /// <summary>他モジュール（Farming の作業・Cooking の器具など）が発行した観察を図鑑に記録する。</summary>
        private void OnCreatureObservedExternal(CreatureObservedEvent e)
        {
            if (publishingObservation || LocalPlayer == null) return;
            if (e.PlayerId != LocalPlayer.PlayerId || string.IsNullOrEmpty(e.SpeciesId) || string.IsNullOrEmpty(e.Behavior)) return;
            if (e.Behavior == "seen") return;
            var entry = GetEntry(e.SpeciesId, false);
            if (entry != null && entry.HasBehavior(e.Behavior)) return;
            publishingObservation = true;
            try { RecordBehavior(e.SpeciesId, e.Behavior, true); }
            finally { publishingObservation = false; }
        }

        public void RecordCaught(string speciesId, float size)
        {
            var e = GetEntry(speciesId, true);
            if (!e.Seen) e.seenCount = 1;
            e.caughtCount++;
            int day = Clock != null ? Clock.Day : 0;
            if (e.firstCaughtDay < 0) e.firstCaughtDay = day;
            if (e.firstSeenDay < 0) e.firstSeenDay = day;
            if (size > e.biggestSize) e.biggestSize = size;
        }

        // =====================================================================
        // 生き物取りレベル・技（生き物仕様 §5.4）
        // =====================================================================

        public bool HasTechnique(string id) => techniques.Contains(id);

        public void LearnTechnique(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            techniques.Add(id);
        }

        public void AddCatchExp(int amount)
        {
            if (amount <= 0) return;
            CatchExp += amount;
            int before = CatchLevel;
            while (CatchLevel < MaxCatchLevel && CatchExp >= CatchExpForLevel[CatchLevel + 1]) CatchLevel++;
            if (CatchLevel != before) UIManager.Toast(Loc.Format("生き物取りが上達した！（Lv{0}）", "Creature catching improved! (Lv{0})", CatchLevel));
        }

        public int CatchExpToNext() => CatchLevel >= MaxCatchLevel ? 0 : CatchExpForLevel[CatchLevel + 1] - CatchExp;

        public bool MarkShownToday(string residentId)
        {
            if (dailyShown.ContainsKey(residentId)) return false;
            dailyShown[residentId] = Clock != null ? Clock.Day : 0;
            return true;
        }

        // =====================================================================
        // 天候・エンディング
        // =====================================================================

        private long lastRainMinute = -100000;

        private void OnWeatherChanged(WeatherChangedEvent e)
        {
            if (e.Previous.IsRaining && !e.Current.IsRaining && Clock != null) lastRainMinute = Clock.TotalMinutes;
        }

        public bool IsAfterRain()
        {
            if (Clock == null) return false;
            return Clock.TotalMinutes - lastRainMinute < 6 * 60;
        }

        /// <summary>「次の星へ」を選んだ時、この星に置いていた自分の個体はケージに入れて連れていく（置き去りにしない）。</summary>
        private void OnEndingFinished(EndingFinishedEvent e)
        {
            if (e.Choice != PostEndingChoice.NextPlanet || LocalPlayer == null) return;
            var mine = new List<OwnedCreature>();
            foreach (var c in placed.Values) if (c.ownerId == LocalPlayer.PlayerId) mine.Add(c);
            foreach (var c in mine)
            {
                placed.Remove(c.uid);
                c.placement = CreaturePlacement.Storage;
                c.targetId = null;
                c.residentWorldId = null;
                carried[c.uid] = c;
            }
            if (mine.Count > 0)
            {
                MarkDirty();
                UIManager.Toast(Loc.Format("{0}匹の生き物がケージに入って一緒に来ます", "{0} creatures will travel with you in cages", mine.Count));
            }
        }

        // =====================================================================
        // セーブ
        // =====================================================================

        public override string CaptureState(SaveScope scope, string playerId)
        {
            switch (scope)
            {
                case SaveScope.Planet:
                    {
                        var d = new CreaturePlanetSave();
                        d.placed.AddRange(placed.Values);
                        d.wilds.AddRange(wilds.Values);
                        return JsonUtility.ToJson(d);
                    }
                case SaveScope.PlayerProfile:
                    {
                        if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                        var d = new CreatureProfileSave
                        {
                            catchLevel = CatchLevel,
                            catchExp = CatchExp,
                            totalCaught = TotalCaught,
                            techniques = new List<string>(techniques),
                            dailyShown = PairListUtil.ToPairs(dailyShown)
                        };
                        foreach (var c in carried.Values)
                        {
                            // 自分の星に置いた個体は「預けている」控えとして持つ（次の星から見ても分かる）
                            d.carried.Add(c);
                        }
                        // 星に置いた自分の個体の控えを最新の状態で作り直す
                        foreach (var p in placed.Values)
                        {
                            if (p.ownerId != playerId) continue;
                            bool exists = false;
                            for (int i = 0; i < d.carried.Count; i++)
                            {
                                if (d.carried[i].uid != p.uid) continue;
                                d.carried[i] = MakeStub(p);
                                exists = true;
                                break;
                            }
                            if (!exists) d.carried.Add(MakeStub(p));
                        }
                        return JsonUtility.ToJson(d);
                    }
                case SaveScope.PlayerPlanetKnowledge:
                    {
                        if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return null;
                        var d = new CreatureKnowledgeSave();
                        d.entries.AddRange(bestiary.Values);
                        return JsonUtility.ToJson(d);
                    }
            }
            return null;
        }

        private OwnedCreature MakeStub(OwnedCreature p)
        {
            var s = p.Clone();
            s.placement = CreaturePlacement.Deposited;
            s.targetId = p.placement.ToString(); // 控えには「星で何をしているか」を残す
            s.residentWorldId = Game != null && Game.Planet != null ? Game.Planet.worldId : p.residentWorldId;
            return s;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            switch (scope)
            {
                case SaveScope.Planet:
                    {
                        var d = JsonUtility.FromJson<CreaturePlanetSave>(json);
                        if (d == null) return;
                        placed.Clear();
                        if (d.placed != null) foreach (var c in d.placed) if (c != null && !string.IsNullOrEmpty(c.uid)) placed[c.uid] = c;
                        wilds.Clear();
                        if (d.wilds != null) foreach (var w in d.wilds) if (w != null && !string.IsNullOrEmpty(w.wildId)) wilds[w.wildId] = w;
                        if (sceneBuilt)
                        {
                            RefreshWildViews();
                            RefreshOwnedAvatars();
                        }
                        break;
                    }
                case SaveScope.PlayerProfile:
                    {
                        if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                        var d = JsonUtility.FromJson<CreatureProfileSave>(json);
                        if (d == null) return;
                        carried.Clear();
                        if (d.carried != null) foreach (var c in d.carried) if (c != null && !string.IsNullOrEmpty(c.uid)) carried[c.uid] = c;
                        CatchLevel = Mathf.Max(1, d.catchLevel);
                        CatchExp = d.catchExp;
                        TotalCaught = d.totalCaught;
                        techniques.Clear();
                        if (d.techniques != null) foreach (var t in d.techniques) techniques.Add(t);
                        dailyShown.Clear();
                        foreach (var kv in PairListUtil.ToDict(d.dailyShown)) dailyShown[kv.Key] = kv.Value;
                        if (sceneBuilt) RefreshOwnedAvatars();
                        break;
                    }
                case SaveScope.PlayerPlanetKnowledge:
                    {
                        if (LocalPlayer == null || playerId != LocalPlayer.PlayerId) return;
                        var d = JsonUtility.FromJson<CreatureKnowledgeSave>(json);
                        if (d == null) return;
                        bestiary.Clear();
                        if (d.entries != null) foreach (var e in d.entries) if (e != null && !string.IsNullOrEmpty(e.speciesId)) bestiary[e.speciesId] = e;
                        break;
                    }
            }
        }
    }
}
