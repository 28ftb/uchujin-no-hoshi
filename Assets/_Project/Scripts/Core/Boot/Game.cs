using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core.UI;

namespace Uchujin.Core
{
    /// <summary>新しい星を始める時の設定。</summary>
    public class NewGameSettings
    {
        public string SeedText;
        public CharacterAppearance Appearance;
        public string PlanetNickname;
        /// <summary>プロローグ（不時着〜村への案内）を飛ばす（デバッグ・2つ目以降の星用）。</summary>
        public bool SkipPrologue;
        /// <summary>true なら持ち物・お金・道具を引き継ぐ（次の星へ進む場合）。false なら初期化。</summary>
        public bool KeepProfile;
        public int PlanetIndex = 1;
        public string PreviousWorldId;
    }

    /// <summary>
    /// ゲーム全体の統括。シーンに1つだけ存在する（GameBootstrap が自動生成）。
    /// 各システムは SystemManifest の順に生成され、型名から反射で生成するため
    /// コアは各モジュールにコンパイル時依存しない。
    /// </summary>
    public sealed class Game : MonoBehaviour
    {
        public static Game Instance { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;
        public TimeSystem Time { get; private set; }
        public PlayerSystem PlayerSys { get; private set; }
        public PlayerRegistry Players { get; } = new PlayerRegistry();
        public PlayerState LocalPlayer => Players.Local;
        public AreaManager Areas { get; } = new AreaManager();
        public FlagStore Flags { get; private set; } = new FlagStore();
        public SaveSystem Saves { get; } = new SaveSystem();
        public PlanetInfo Planet { get; private set; }
        public PlanetContext PlanetContext { get; private set; }
        public World.WorldLayout Layout => PlanetContext?.Layout;
        public string CurrentSlotId { get; private set; }
        /// <summary>星のシーン上の生成物の親。星を離れると丸ごと破棄される。</summary>
        public Transform SceneRoot { get; private set; }
        public IReadOnlyList<GameSystemBase> Systems => systems;
        public bool IsBusy { get; private set; }

        /// <summary>タイトル画面の差し替え口（Story モジュールが設定）。null ならコアの簡易タイトル。</summary>
        public static Func<UIWindow> TitleScreenFactory;
        /// <summary>新しい星の生成後に呼ばれる（Story モジュールがプロローグを始める）。</summary>
        public static Action<NewGameSettings> OnNewPlanetStarted;

        private readonly List<GameSystemBase> systems = new List<GameSystemBase>();
        private readonly List<GameSystemBase> dailyOrder = new List<GameSystemBase>();
        private readonly Dictionary<Type, object> serviceCache = new Dictionary<Type, object>();
        private PauseMenuWindow pauseMenu;
        private InventoryWindow inventoryWindow;

        public string LocalPlayerId { get; private set; }

        // ------------------------------------------------------------
        // 初期化
        // ------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Application.targetFrameRate = 60;

            LocalPlayerId = PlayerPrefs.GetString("uchujin.localPlayerId", "");
            if (string.IsNullOrEmpty(LocalPlayerId))
            {
                LocalPlayerId = Ids.New();
                PlayerPrefs.SetString("uchujin.localPlayerId", LocalPlayerId);
                PlayerPrefs.Save();
            }
            if (PlayerPrefs.HasKey("uchujin.uiLanguage")) Loc.Current = (UILanguage)PlayerPrefs.GetInt("uchujin.uiLanguage");
            Players.CreateLocal(LocalPlayerId);
            WorldAuthority.LocalPlayerId = LocalPlayerId;
            WorldAuthority.PlayerLookup = id => Players.Get(id);

            CreateSystems();
            foreach (var s in systems)
            {
                try { s.RegisterContent(); }
                catch (Exception e) { Debug.LogError($"{s.SystemName}.RegisterContent で例外: {e}"); }
            }
            EnsureSun();
        }

        private void Start()
        {
            ShowTitle();
        }

        private void CreateSystems()
        {
            systems.Clear();
            foreach (var typeName in SystemManifest.SystemTypeNames)
            {
                var t = SystemManifest.FindType(typeName);
                if (t == null)
                {
                    Debug.LogWarning($"システム {typeName} が見つかりません（未実装のモジュール）");
                    continue;
                }
                try
                {
                    var sys = (GameSystemBase)Activator.CreateInstance(t);
                    sys.Game = this;
                    systems.Add(sys);
                    if (sys is TimeSystem ts) Time = ts;
                    if (sys is PlayerSystem ps) PlayerSys = ps;
                }
                catch (Exception e)
                {
                    Debug.LogError($"システム {typeName} の生成に失敗: {e}");
                }
            }
            dailyOrder.Clear();
            dailyOrder.AddRange(systems);
            dailyOrder.Sort((a, b) => a.DailyUpdateOrder.CompareTo(b.DailyUpdateOrder));
        }

        public T Get<T>() where T : GameSystemBase
        {
            foreach (var s in systems) if (s is T t) return t;
            return null;
        }

        /// <summary>連携契約インターフェースの実装を探す（無ければ null）。</summary>
        public T Service<T>() where T : class
        {
            if (serviceCache.TryGetValue(typeof(T), out var cached)) return (T)cached;
            T found = null;
            foreach (var s in systems)
            {
                if (s is T t)
                {
                    found = t;
                    break;
                }
            }
            if (found != null) serviceCache[typeof(T)] = found;
            return found;
        }

        private void EnsureSun()
        {
            if (FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        public void SetState(GameState s)
        {
            if (State == s) return;
            var prev = State;
            State = s;
            EventBus.Publish(new GameStateChangedEvent { Previous = prev, Current = s });
        }

        // ------------------------------------------------------------
        // タイトル・星の開始／ロード
        // ------------------------------------------------------------

        public void ShowTitle()
        {
            UIManager.CloseAll();
            SetState(GameState.Title);
            UIWindow title = null;
            try { title = TitleScreenFactory?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
            UIManager.Open(title ?? new FallbackTitleWindow());
            // 読み込み失敗・演出の途中などで画面が暗転したまま戻ってきた場合に、タイトルが見えなくならないようにする
            if (ScreenFader.Alpha > 0.01f) ScreenFader.FadeIn(0.6f);
        }

        public void StartNewPlanet(NewGameSettings settings)
        {
            if (IsBusy) return;
            StartCoroutine(NewPlanetRoutine(settings ?? new NewGameSettings()));
        }

        private IEnumerator NewPlanetRoutine(NewGameSettings settings)
        {
            IsBusy = true;
            UIManager.CloseAll();
            ScreenFader.SetBlack();
            SetState(GameState.Loading);
            yield return null;

            TeardownPlanet();
            if (WorldAuthority.Role == NetRole.Guest) WorldAuthority.ResetSession();

            if (!settings.KeepProfile) PlayerSys?.ResetLocalProfileForNewGame(settings.Appearance);
            else if (settings.Appearance != null) LocalPlayer.Appearance = settings.Appearance;

            Planet = PlanetInfo.CreateNew(settings.SeedText, LocalPlayerId, settings.PlanetIndex, settings.PreviousWorldId);
            Planet.playerGivenName = settings.PlanetNickname;
            CurrentSlotId = Planet.worldId.Substring(0, 12);
            Flags = new FlagStore();
            PlanetContext = new PlanetContext(Planet, true) { Settings = settings };
            CreateSceneRoot();

            foreach (var s in systems)
            {
                RunSafe(s, "PreparePlanet", () => s.PreparePlanet(PlanetContext));
                yield return null;
            }
            foreach (var s in systems) RunSafe(s, "OnNewPlanet", () => s.OnNewPlanet(PlanetContext));
            yield return null;
            BuildAllScenes();
            yield return null;

            SetState(GameState.Playing);
            EventBus.Publish(new PlanetReadyEvent { Planet = Planet, IsNewPlanet = true });
            foreach (var s in systems) RunSafe(s, "OnPlayStarted", () => s.OnPlayStarted());
            ScreenFader.FadeIn(1.2f);
            IsBusy = false;
            SaveGame(false);
            try { OnNewPlanetStarted?.Invoke(settings); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void LoadSlot(string slotId)
        {
            if (IsBusy) return;
            StartCoroutine(LoadRoutine(slotId));
        }

        private IEnumerator LoadRoutine(string slotId)
        {
            IsBusy = true;
            UIManager.CloseAll();
            ScreenFader.SetBlack();
            SetState(GameState.Loading);
            yield return null;

            PlanetSaveFile planetFile = null;
            PlayerSaveFile playerFile = null;
            try
            {
                planetFile = Saves.ReadPlanet(slotId);
                playerFile = Saves.ReadPlayer(slotId, LocalPlayerId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (planetFile == null || planetFile.planet == null)
            {
                IsBusy = false;
                // ShowTitle は開いているウィンドウを全部閉じるので、メッセージはタイトルを開いた後に出す。
                // プレイ中の星から呼ばれた場合は、その星を片付けてからタイトルへ（星が残ったままタイトルにならないように）
                if (Planet != null) ReturnToTitle(false);
                else ShowTitle();
                UIManager.Message(Loc.T("読み込み失敗", "Load failed"), Loc.T("セーブデータを読み込めませんでした。", "Could not read the save data."));
                yield break;
            }

            TeardownPlanet();
            WorldAuthority.ResetSession();
            Planet = planetFile.planet;
            CurrentSlotId = slotId;
            Flags = planetFile.flags ?? new FlagStore();
            PlanetContext = new PlanetContext(Planet, false);
            CreateSceneRoot();

            foreach (var s in systems)
            {
                RunSafe(s, "PreparePlanet", () => s.PreparePlanet(PlanetContext));
                yield return null;
            }
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                var json = SaveSystem.FindEntry(planetFile.entries, s.SaveKey);
                if (json != null) RunSafe(s, "RestoreState(Planet)", () => s.RestoreState(SaveScope.Planet, null, json));
                if (playerFile != null)
                {
                    var pj = SaveSystem.FindEntry(playerFile.profile, s.SaveKey);
                    if (pj != null) RunSafe(s, "RestoreState(Profile)", () => s.RestoreState(SaveScope.PlayerProfile, LocalPlayerId, pj));
                    var kj = SaveSystem.FindEntry(playerFile.knowledge, s.SaveKey);
                    if (kj != null) RunSafe(s, "RestoreState(Knowledge)", () => s.RestoreState(SaveScope.PlayerPlanetKnowledge, LocalPlayerId, kj));
                }
            }
            yield return null;
            BuildAllScenes();
            yield return null;

            SetState(GameState.Playing);
            EventBus.Publish(new PlanetReadyEvent { Planet = Planet, IsNewPlanet = false });
            foreach (var s in systems) RunSafe(s, "OnPlayStarted", () => s.OnPlayStarted());
            ScreenFader.FadeIn(1f);
            IsBusy = false;
            UIManager.Toast(Loc.Format("{0} を読み込みました", "Loaded {0}", Time.FormatDate()));
        }

        /// <summary>
        /// ゲストとして他人の星に入る（Network モジュールが呼ぶ）。
        /// planetEntries はホストから受信した星の状態スナップショット。自分の持ち物（Profile）はそのまま持ち込む。
        /// </summary>
        public void BeginGuestSession(PlanetInfo planet, List<SaveEntry> planetEntries, FlagStore flags, Action onReady)
        {
            if (IsBusy) return;
            StartCoroutine(GuestRoutine(planet, planetEntries, flags, onReady));
        }

        private IEnumerator GuestRoutine(PlanetInfo planet, List<SaveEntry> planetEntries, FlagStore flags, Action onReady)
        {
            IsBusy = true;
            UIManager.CloseAll();
            ScreenFader.SetBlack();
            SetState(GameState.Loading);
            yield return null;
            TeardownPlanet();
            WorldAuthority.Role = NetRole.Guest;
            Planet = planet;
            CurrentSlotId = null;
            Flags = flags ?? new FlagStore();
            PlanetContext = new PlanetContext(Planet, false);
            CreateSceneRoot();
            foreach (var s in systems)
            {
                RunSafe(s, "PreparePlanet", () => s.PreparePlanet(PlanetContext));
                yield return null;
            }
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                var json = SaveSystem.FindEntry(planetEntries, s.SaveKey);
                if (json != null) RunSafe(s, "RestoreState(Planet)", () => s.RestoreState(SaveScope.Planet, null, json));
            }
            // 訪問先の星についての自分の知識（辞書など）は、訪問用の保存場所から読む
            var knowledge = Saves.ReadVisitKnowledge(planet.worldId, LocalPlayerId);
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                var kj = SaveSystem.FindEntry(knowledge, s.SaveKey);
                if (kj != null) RunSafe(s, "RestoreState(Knowledge)", () => s.RestoreState(SaveScope.PlayerPlanetKnowledge, LocalPlayerId, kj));
            }
            BuildAllScenes();
            yield return null;
            SetState(GameState.Playing);
            EventBus.Publish(new PlanetReadyEvent { Planet = Planet, IsNewPlanet = false });
            foreach (var s in systems) RunSafe(s, "OnPlayStarted", () => s.OnPlayStarted());
            ScreenFader.FadeIn(1f);
            IsBusy = false;
            onReady?.Invoke();
        }

        /// <summary>ゲスト：ホストから届いたシステム状態を適用する。</summary>
        public void ApplyRemoteState(string saveKey, string json)
        {
            if (saveKey == "core.flags")
            {
                var f = JsonUtility.FromJson<FlagStore>(json);
                if (f != null) Flags = f;
                return;
            }
            foreach (var s in systems)
            {
                if (s.SaveKey != saveKey) continue;
                RunSafe(s, "RestoreState(Remote)", () => s.RestoreState(SaveScope.Planet, null, json));
            }
        }

        /// <summary>ホスト：星の状態を1システム分取り出す（同期用）。</summary>
        public string CapturePlanetState(string saveKey)
        {
            if (saveKey == "core.flags") return JsonUtility.ToJson(Flags);
            foreach (var s in systems)
            {
                if (s.SaveKey != saveKey) continue;
                try { return s.CaptureState(SaveScope.Planet, null); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return null;
        }

        public List<SaveEntry> CaptureAllPlanetEntries()
        {
            var list = new List<SaveEntry>();
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                string json = null;
                try { json = s.CaptureState(SaveScope.Planet, null); }
                catch (Exception e) { Debug.LogError($"{s.SystemName}.CaptureState(Planet): {e}"); }
                if (json != null) list.Add(new SaveEntry { key = s.SaveKey, json = json });
            }
            return list;
        }

        /// <summary>ローカルプレイヤーの持ち歩く資産（PlayerProfile スコープ）を取り出す。マルチの訪問時に使う。</summary>
        public List<SaveEntry> CaptureLocalProfileEntries() => CapturePlayerEntries(SaveScope.PlayerProfile, LocalPlayerId);

        /// <summary>PlayerProfile スコープを復元（自分のセーブから持ち物・見た目を読み込んで他人の星へ行く時など）。</summary>
        public void RestoreLocalProfileEntries(List<SaveEntry> entries)
        {
            if (entries == null) return;
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                var json = SaveSystem.FindEntry(entries, s.SaveKey);
                if (json != null) RunSafe(s, "RestoreState(Profile)", () => s.RestoreState(SaveScope.PlayerProfile, LocalPlayerId, json));
            }
        }

        public List<SaveEntry> CapturePlayerEntries(SaveScope scope, string playerId)
        {
            var list = new List<SaveEntry>();
            foreach (var s in systems)
            {
                if (s.SaveKey == null) continue;
                string json = null;
                try { json = s.CaptureState(scope, playerId); }
                catch (Exception e) { Debug.LogError($"{s.SystemName}.CaptureState({scope}): {e}"); }
                if (json != null) list.Add(new SaveEntry { key = s.SaveKey, json = json });
            }
            return list;
        }

        private void CreateSceneRoot()
        {
            var root = new GameObject("PlanetRoot");
            SceneRoot = root.transform;
        }

        private void BuildAllScenes()
        {
            foreach (var s in systems) RunSafe(s, "BuildScene", () => s.BuildScene());
        }

        /// <summary>現在の星を片付ける。</summary>
        public void TeardownPlanet()
        {
            if (Planet != null) EventBus.Publish(new PlanetTeardownEvent { Planet = Planet });
            for (int i = systems.Count - 1; i >= 0; i--)
            {
                var s = systems[i];
                RunSafe(s, "Teardown", () => s.Teardown());
            }
            ToolUseRouter.Clear();
            Areas.Clear();
            UIManager.ResetAll();
            if (SceneRoot != null) Destroy(SceneRoot.gameObject);
            SceneRoot = null;
            Players.ClearRemotes();
            if (LocalPlayer != null) LocalPlayer.ClearInputLocks();
            // 演出の途中で星を離れても、次の星で HUD が消えたままにならないように
            UIManager.HudVisible = true;
        }

        public void ReturnToTitle(bool save = true)
        {
            if (save && State == GameState.Playing) SaveGame(false);
            TeardownPlanet();
            Planet = null;
            PlanetContext = null;
            WorldAuthority.ResetSession();
            ShowTitle();
        }

        // ------------------------------------------------------------
        // セーブ
        // ------------------------------------------------------------

        public bool SaveGame(bool showToast = true)
        {
            if (Planet == null) return false;
            try
            {
                if (WorldAuthority.IsGuest)
                {
                    // ゲストは自分の知識（辞書など）だけを訪問先ごとに保存する。持ち物は自分のスロットに戻った時に保存される。
                    Saves.WriteVisitKnowledge(Planet.worldId, LocalPlayerId, CapturePlayerEntries(SaveScope.PlayerPlanetKnowledge, LocalPlayerId));
                    if (showToast) UIManager.Toast(Loc.T("この星での記録を保存しました", "Saved your notes for this planet"));
                    return true;
                }
                if (string.IsNullOrEmpty(CurrentSlotId)) return false;
                var planetFile = new PlanetSaveFile { planet = Planet, flags = Flags, entries = CaptureAllPlanetEntries() };
                var playerFile = new PlayerSaveFile
                {
                    playerId = LocalPlayerId,
                    worldId = Planet.worldId,
                    profile = CapturePlayerEntries(SaveScope.PlayerProfile, LocalPlayerId),
                    knowledge = CapturePlayerEntries(SaveScope.PlayerPlanetKnowledge, LocalPlayerId)
                };
                var rocket = Service<IRocketService>();
                var speech = Service<ISpeechService>();
                var meta = new SlotMeta
                {
                    slotId = CurrentSlotId,
                    worldId = Planet.worldId,
                    seedText = Planet.seedText,
                    planetName = !string.IsNullOrEmpty(Planet.playerGivenName) ? Planet.playerGivenName : speech?.PlanetName,
                    playerName = LocalPlayer.DisplayName,
                    day = Time.Day,
                    money = LocalPlayer.Wallet.money,
                    planetIndex = Planet.planetIndex,
                    lastSavedTicks = DateTime.UtcNow.Ticks,
                    repairedDevices = rocket?.RepairedDeviceCount ?? 0,
                    launched = rocket?.HasLaunched ?? false
                };
                Saves.WriteSlot(meta, planetFile, playerFile);
                EventBus.Publish(new GameSavedEvent { SlotId = CurrentSlotId });
                if (showToast) UIManager.Toast(Loc.T("セーブしました", "Game saved"));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                UIManager.Toast(Loc.T("セーブに失敗しました", "Save failed"));
                return false;
            }
        }

        // ------------------------------------------------------------
        // 時間通知（TimeSystem から呼ばれる）
        // ------------------------------------------------------------

        internal void NotifyMinutesAdvanced(int minutes)
        {
            foreach (var s in systems)
            {
                if (s == Time) continue;
                RunSafe(s, "OnGameMinutesAdvanced", () => s.OnGameMinutesAdvanced(minutes));
            }
        }

        internal void NotifyHourChanged(int day, int hour)
        {
            foreach (var s in systems)
            {
                if (s == Time) continue;
                RunSafe(s, "OnHourChanged", () => s.OnHourChanged(day, hour));
            }
        }

        internal void RunDayChange(int prevDay, int newDay)
        {
            foreach (var s in dailyOrder) RunSafe(s, "OnDayEnded", () => s.OnDayEnded(prevDay));
            EventBus.Publish(new DayEndedEvent { Day = prevDay });
            foreach (var s in dailyOrder) RunSafe(s, "OnDayStarted", () => s.OnDayStarted(newDay));
            EventBus.Publish(new DayStartedEvent { Day = newDay });
        }

        /// <summary>睡眠の暗転中か（この間は Esc メニューを開かない。途中でタイトルへ戻ると片付け済みの星の時間が進んでしまうため）。</summary>
        public bool IsSleeping { get; private set; }

        public IEnumerator SleepRoutine(PlayerState player, int wakeHour)
        {
            if (player == null || IsSleeping) yield break;
            var planet = Planet;
            IsSleeping = true;
            player.LockInput("sleep");
            ScreenFader.FadeOut(0.8f);
            yield return new WaitForSecondsRealtime(0.9f);
            // 暗転中に星を離れた（タイトルへ戻った・別の星へ移った）場合は、時間を進めずに終える
            if (Planet == null || Planet != planet || State != GameState.Playing)
            {
                IsSleeping = false;
                player.UnlockInput("sleep");
                ScreenFader.FadeIn(0.5f);
                yield break;
            }
            try { Time.SleepUntilHour(wakeHour, player.PlayerId); }
            catch (Exception e) { Debug.LogException(e); }
            yield return new WaitForSecondsRealtime(0.4f);
            IsSleeping = false;
            if (Planet == null || Planet != planet)
            {
                player.UnlockInput("sleep");
                ScreenFader.FadeIn(0.5f);
                yield break;
            }
            if (!WorldAuthority.IsGuest) SaveGame(false);
            ScreenFader.FadeIn(1f);
            player.UnlockInput("sleep");
            UIManager.Toast(Loc.Format("{0}の朝です", "Morning of {0}", Time.FormatDate()));
        }

        // ------------------------------------------------------------
        // フレーム処理
        // ------------------------------------------------------------

        private void Update()
        {
            UIManager.UpdateWindows();
            if (State == GameState.Playing || State == GameState.Cutscene || State == GameState.Ending)
            {
                float dt = UnityEngine.Time.deltaTime;
                foreach (var s in systems) RunSafe(s, "Tick", () => s.Tick(dt));
            }
            HandleGlobalInput();
        }

        private void HandleGlobalInput()
        {
            if (GameInput.Pause)
            {
                if (IsSleeping || IsBusy) return;
                if (UIManager.OpenWindows.Count > 0) UIManager.CloseTop();
                else if (State == GameState.Playing)
                {
                    if (pauseMenu == null) pauseMenu = new PauseMenuWindow();
                    UIManager.Open(pauseMenu);
                }
                return;
            }
            if (State != GameState.Playing || LocalPlayer == null) return;
            if (UIManager.TextFieldFocused) return;

            if (GameInput.Tablet)
            {
                var tablet = Service<ITabletService>();
                if (tablet != null)
                {
                    if (tablet.IsOpen) tablet.Close();
                    else if (!UIManager.AnyModalOpen) tablet.Open();
                }
                return;
            }
            if (UIManager.AnyModalOpen || LocalPlayer.InputLocked) return;

            if (GameInput.Inventory)
            {
                if (inventoryWindow == null) inventoryWindow = new InventoryWindow(LocalPlayer);
                UIManager.Toggle(inventoryWindow);
            }
            else if (GameInput.QuickSave) SaveGame();
            else if (GameInput.ToggleHelp) UIManager.Hud.ShowControlsHelp = !UIManager.Hud.ShowControlsHelp;
            else if (GameInput.Map) Service<ITabletService>()?.Open("map");
            else if (GameInput.GestureMenu)
            {
                UIManager.Open(new GestureMenu(LocalPlayer, PerformLocalGesture));
            }
        }

        /// <summary>主人公の身振り。近くの住民に向けた意思表示として各システムへ伝える。</summary>
        public void PerformLocalGesture(PlayerGesture g)
        {
            var p = LocalPlayer;
            PlayerSys?.LocalController?.PlayGesture(g);
            string target = null;
            var dir = Service<IResidentDirectory>();
            if (dir != null)
            {
                float best = 5f * 5f;
                foreach (var r in dir.ActiveResidents)
                {
                    float d = (r.Position - p.Position).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        target = r.Id;
                    }
                }
            }
            if (target != null) Service<IQuestService>()?.TryHandleGesture(target, p.PlayerId, g);
            EventBus.Publish(new PlayerGestureEvent { PlayerId = p.PlayerId, Gesture = g, TargetResidentId = target });
        }

        private void OnGUI()
        {
            UIManager.DrawAll();
        }

        private void OnApplicationQuit()
        {
            if (State == GameState.Playing) SaveGame(false);
        }

        private static void RunSafe(GameSystemBase s, string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Debug.LogError($"[{s.SystemName}.{what}] {e}"); }
        }
    }
}
