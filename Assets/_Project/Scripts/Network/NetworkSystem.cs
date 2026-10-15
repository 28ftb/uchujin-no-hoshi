using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// マルチプレイ（EOS P2P・ホスト権威）。マルチプレイ仕様 v0.2、基本決定 v0.2 §6。
    ///
    /// ・ホスト：プレイ中の自分の星を開く（Esc メニュー → マルチプレイ）。星の状態の正本を持ち、ゲストのコマンドを実行する。
    /// ・ゲスト：自分の持ち物（Profile）を持って友達の星へ行く。星の状態はホストのスナップショット＋差分で受け取る。
    /// ・通信路は INetTransport（EOS P2P／開発用 LAN UDP）。構成はスター型でゲスト同士の物はホストが中継する。
    /// ・EOS が NPC やワールドセーブを自動同期するわけではない（基本決定 v0.2 §6）。同期はすべてこのシステムが行う。
    ///
    /// ファイル構成（partial）：
    ///   NetworkSystem.cs        … 共通（ライフサイクル・受信の振り分け・プレイヤー表示・位置・身振り）
    ///   NetworkSystem.Host.cs   … ホスト（参加受付・スナップショット・差分・コマンド実行・INetworkBridge）
    ///   NetworkSystem.Guest.cs  … ゲスト（参加・読み込み・Tx の適用と台帳・退出と持ち物の保存）
    ///   NetworkSystem.Sleep.cs / .Dictionary.cs / .Trade.cs … 睡眠の投票・辞書の手渡し・直接交換
    /// </summary>
    public sealed partial class NetworkSystem : GameSystemBase, INetworkService, INetworkBridge
    {
        public override string SaveKey => "network";
        public override int DailyUpdateOrder => 950;

        public static NetworkSystem Instance { get; private set; }

        public SessionState Session { get; private set; } = SessionState.None;
        public INetTransport Transport => transport;
        public string LastError { get; private set; }
        public IReadOnlyDictionary<string, SessionPlayer> SessionPlayers => players;

        private INetTransport transport;
        private NetworkRunner runner;
        /// <summary>実時間の経過秒（ネットワーク用の時計）。</summary>
        private float clock;
        private readonly Dictionary<string, SessionPlayer> players = new Dictionary<string, SessionPlayer>();
        private bool subscribed;
        private NetworkOverlay overlay;
        private TitleJoinOverlay titleOverlay;
        private float transformTimer;
        private uint transformSeq;

        // ------------------------------------------------------------
        // ライフサイクル
        // ------------------------------------------------------------

        public override void RegisterContent()
        {
            Instance = this;
            NetDevArgs.RestoreOriginalPlayerIdPref();
            if (Game != null && runner == null)
            {
                // Tick はプレイ中しか呼ばれないので、タイトル・読み込み中も通信を回すための常駐オブジェクトを作る
                var go = new GameObject("NetworkRunner");
                go.transform.SetParent(Game.transform, false);
                runner = go.AddComponent<NetworkRunner>();
                runner.System = this;
            }
            overlay = new NetworkOverlay(this);
            titleOverlay = new TitleJoinOverlay(this);
        }

        public override void OnPlayStarted()
        {
            Subscribe();
            UIManager.RegisterOverlay(overlay);
            if (Session == SessionState.Hosting || Session == SessionState.GuestPlaying) RebuildAllAvatars();
            if (!WorldAuthority.IsGuest) RollbackStaleEscrows();
        }

        public override void Teardown()
        {
            Unsubscribe();
            DestroyAllAvatars();
            if (suppressTeardownShutdown) return; // ゲスト参加の読み込み中（自分の星を片付けている）
            if (Session == SessionState.Hosting || Session == SessionState.HostStarting)
            {
                HostShutdown(true, false);
            }
            else if (IsGuestSession)
            {
                GuestShutdown(null, null, true, GuestExit.None);
            }
        }

        public override void OnDayStarted(int day)
        {
            if (Session == SessionState.GuestPlaying)
            {
                // 訪問先での知識と持ち物を日付ごとに保存（異常終了時の損失を減らす）
                Game.SaveGame(false);
                WriteHomeProfile();
            }
        }

        private void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            EventBus.Subscribe<PlayerGestureEvent>(OnLocalGesture);
            EventBus.Subscribe<ItemAddedEvent>(OnItemAdded);
            EventBus.Subscribe<ItemRemovedEvent>(OnItemRemoved);
            EventBus.Subscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Subscribe<ToolUpgradedEvent>(OnToolUpgraded);
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            EventBus.Unsubscribe<PlayerGestureEvent>(OnLocalGesture);
            EventBus.Unsubscribe<ItemAddedEvent>(OnItemAdded);
            EventBus.Unsubscribe<ItemRemovedEvent>(OnItemRemoved);
            EventBus.Unsubscribe<MoneyChangedEvent>(OnMoneyChanged);
            EventBus.Unsubscribe<ToolUpgradedEvent>(OnToolUpgraded);
        }

        /// <summary>NetworkRunner から毎フレーム（ゲームの状態に関係なく）呼ばれる。</summary>
        internal void FrameUpdate(float dt)
        {
            clock += dt;
            if (NetConfig.ShowTitleJoinButton && Game != null && Game.State == GameState.Title) UIManager.RegisterOverlay(titleOverlay);

            if (transport != null)
            {
                try { transport.Poll(dt); }
                catch (Exception e) { Debug.LogException(e); }
            }

            try
            {
                switch (Session)
                {
                    case SessionState.HostStarting:
                        HostStartingUpdate();
                        break;
                    case SessionState.Hosting:
                        HostUpdate(dt);
                        break;
                    case SessionState.GuestConnecting:
                    case SessionState.GuestAwaitingSnapshot:
                        GuestConnectingUpdate();
                        break;
                    case SessionState.GuestLoading:
                        TryBeginGuestSession();
                        break;
                    case SessionState.GuestPlaying:
                        GuestUpdate(dt);
                        break;
                }
                if (Session == SessionState.Hosting || Session == SessionState.GuestPlaying)
                {
                    TransformUpdate(dt);
                    SleepUpdate();
                }
                TradeUpdate();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        internal void OnApplicationQuitting()
        {
            if (Session == SessionState.Hosting || Session == SessionState.HostStarting)
            {
                BroadcastToGuests(NetMsg.HostClosing, new LeaveMsg { reason = "quit" });
                ShutdownTransport();
            }
            else if (IsGuestSession)
            {
                GuestShutdown(null, null, true, GuestExit.None);
            }
        }

        // ------------------------------------------------------------
        // セーブ（Planet：ホストの記録、PlayerProfile：受領台帳）
        // ------------------------------------------------------------

        public override string CaptureState(SaveScope scope, string playerId)
        {
            if (scope == SaveScope.Planet)
            {
                if (!WorldAuthority.IsAuthority) return null;
                if (planetSave.guests.Count == 0 && planetSave.outbox.Count == 0) return null;
                return JsonUtility.ToJson(planetSave);
            }
            if (scope == SaveScope.PlayerProfile)
            {
                if (Game == null || playerId != Game.LocalPlayerId) return null;
                TrimLedger();
                return JsonUtility.ToJson(profileSave);
            }
            return null;
        }

        public override void RestoreState(SaveScope scope, string playerId, string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            if (scope == SaveScope.Planet)
            {
                // ゲストにはスナップショットで送らない（Host 側で除外）が、念のためゲストでは無視する
                if (!WorldAuthority.IsAuthority || IsGuestSession) return;
                var d = JsonUtility.FromJson<NetPlanetSave>(json);
                planetSave = d ?? new NetPlanetSave();
                if (planetSave.guests == null) planetSave.guests = new List<KnownGuest>();
                if (planetSave.outbox == null) planetSave.outbox = new List<OutboxEntry>();
            }
            else if (scope == SaveScope.PlayerProfile)
            {
                if (Game == null || playerId != Game.LocalPlayerId) return;
                var d = JsonUtility.FromJson<NetProfileSave>(json);
                profileSave = d ?? new NetProfileSave();
                if (profileSave.appliedTxIds == null) profileSave.appliedTxIds = new List<string>();
                if (profileSave.escrows == null) profileSave.escrows = new List<EscrowRecord>();
                if (profileSave.rolledBackTrades == null) profileSave.rolledBackTrades = new List<string>();
                appliedTxSet = null;
            }
        }

        public override void OnNewPlanet(PlanetContext ctx)
        {
            // 新しい星：ホストとしての記録は空から（受領台帳は個人の物なのでそのまま）
            if (!IsGuestSession) planetSave = new NetPlanetSave();
        }

        public override void PreparePlanet(PlanetContext ctx)
        {
            // ロード時は RestoreState(Planet) で上書きされる。ゲスト参加中は使わない。
            if (!IsGuestSession && !suppressTeardownShutdown) planetSave = new NetPlanetSave();
        }

        // ------------------------------------------------------------
        // INetworkService
        // ------------------------------------------------------------

        public bool IsOnline => Session == SessionState.Hosting || Session == SessionState.GuestPlaying;
        public NetRole Role => WorldAuthority.Role;
        public bool IsGuestSession => Session == SessionState.GuestConnecting || Session == SessionState.GuestAwaitingSnapshot
                                      || Session == SessionState.GuestLoading || Session == SessionState.GuestPlaying;
        public bool IsHostSession => Session == SessionState.Hosting || Session == SessionState.HostStarting;

        /// <summary>セッション中の全員（自分を含む）。オフラインでは自分だけ。</summary>
        public IEnumerable<string> ConnectedPlayerIds
        {
            get
            {
                if (!IsOnline || players.Count == 0)
                {
                    if (Game != null && Game.LocalPlayerId != null) yield return Game.LocalPlayerId;
                    yield break;
                }
                foreach (var id in players.Keys) yield return id;
            }
        }

        public string GetPlayerName(string playerId)
        {
            if (string.IsNullOrEmpty(playerId)) return "?";
            if (players.TryGetValue(playerId, out var sp) && !string.IsNullOrEmpty(sp.DisplayName)) return sp.DisplayName;
            var ps = Game?.Players.Get(playerId);
            if (ps != null) return ps.DisplayName;
            foreach (var g in planetSave.guests) if (g.playerId == playerId && !string.IsNullOrEmpty(g.displayName)) return g.displayName;
            return Loc.T("旅人", "Traveler");
        }

        public void OpenMultiplayerMenu()
        {
            var existing = UIManager.Find<MultiplayerMenuWindow>();
            if (existing != null)
            {
                UIManager.Open(existing);
                return;
            }
            UIManager.Open(new MultiplayerMenuWindow(this));
        }

        // RequestSleep は NetworkSystem.Sleep.cs

        // ------------------------------------------------------------
        // トランスポート
        // ------------------------------------------------------------

        private void AttachTransport(INetTransport t)
        {
            if (transport == t) return;
            DetachTransport();
            transport = t;
            if (transport == null) return;
            transport.PeerConnected += OnPeerConnected;
            transport.PeerDisconnected += OnPeerDisconnected;
            transport.Received += OnReceived;
        }

        private void DetachTransport()
        {
            if (transport == null) return;
            transport.PeerConnected -= OnPeerConnected;
            transport.PeerDisconnected -= OnPeerDisconnected;
            transport.Received -= OnReceived;
            transport = null;
        }

        private void ShutdownTransport()
        {
            var t = transport;
            DetachTransport();
            if (t != null)
            {
                try { t.Shutdown(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // ロビー一覧のためだけに作ったトランスポートも閉じる（参加に使う物は BeginJoin で先に外してある）
            if (browseTransport != null && browseTransport != t)
            {
                try { browseTransport.Shutdown(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            browseTransport = null;
        }

        private void OnPeerConnected(string netId)
        {
            if (IsHostSession) HostOnPeerConnected(netId);
            else if (IsGuestSession) GuestOnConnected(netId);
        }

        private void OnPeerDisconnected(string netId, string reason)
        {
            if (IsHostSession) HostOnPeerDisconnected(netId, reason);
            else if (IsGuestSession) GuestOnHostDisconnected(reason);
        }

        private void OnReceived(string netId, byte[] data)
        {
            if (data == null || data.Length < 2) return;
            if ((NetMsg)data[0] == NetMsg.Transform)
            {
                if (NetCodec.TryDecodeTransform(data, out var sample)) HandleTransform(netId, sample);
                return;
            }
            if (!NetCodec.TryDecodeHeader(data, out var type, out var body)) return;
            try
            {
                if (IsHostSession) HostHandle(netId, type, body);
                else if (IsGuestSession) GuestHandle(type, body);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Network] {type} の処理中に例外: {e}");
            }
        }

        private void SendTo(SessionPlayer p, NetMsg type, object payload)
        {
            if (p == null || p.IsLocal || p.NetId == null || transport == null) return;
            transport.Send(p.NetId, NetCodec.Encode(type, payload), NetDelivery.Reliable);
        }

        private void SendToHost(NetMsg type, object payload)
        {
            if (transport == null || transport.HostPeerId == null) return;
            transport.Send(transport.HostPeerId, NetCodec.Encode(type, payload), NetDelivery.Reliable);
        }

        /// <summary>ホスト：参加済みの全ゲストへ送る。</summary>
        private void BroadcastToGuests(NetMsg type, object payload, string exceptPlayerId = null, bool readyOnly = false)
        {
            if (transport == null) return;
            byte[] data = null;
            foreach (var p in players.Values)
            {
                if (p.IsLocal || p.NetId == null) continue;
                if (exceptPlayerId != null && p.PlayerId == exceptPlayerId) continue;
                if (readyOnly && !p.Ready) continue;
                if (data == null) data = NetCodec.Encode(type, payload);
                transport.Send(p.NetId, data, NetDelivery.Reliable);
            }
        }

        private string LocalId => Game != null ? Game.LocalPlayerId : WorldAuthority.LocalPlayerId;

        // ------------------------------------------------------------
        // プレイヤーの表示（リモートのアバター）
        // ------------------------------------------------------------

        private void AddOrUpdateSessionPlayer(PlayerInfoDto dto)
        {
            if (dto == null || string.IsNullOrEmpty(dto.playerId)) return;
            bool isLocal = dto.playerId == LocalId;
            if (!players.TryGetValue(dto.playerId, out var sp))
            {
                sp = new SessionPlayer { PlayerId = dto.playerId, IsLocal = isLocal };
                players[dto.playerId] = sp;
            }
            sp.DisplayName = dto.displayName;
            sp.Permission = (PermissionLevel)dto.permission;
            sp.IsHost = dto.isHost;
            bool appearanceChanged = sp.AppearanceJson != dto.appearanceJson;
            sp.AppearanceJson = dto.appearanceJson;
            if (isLocal) return;

            var ps = Game.Players.AddRemote(dto.playerId);
            ps.Permission = sp.Permission;
            var app = ParseAppearance(dto.appearanceJson);
            if (app != null)
            {
                if (string.IsNullOrEmpty(app.displayName)) app.displayName = dto.displayName;
                ps.Appearance = app;
            }
            if (appearanceChanged && sp.Avatar != null) sp.Avatar.RebuildVisual(ps.Appearance);
            EnsureAvatar(sp);
        }

        private static CharacterAppearance ParseAppearance(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<CharacterAppearance>(json); }
            catch { return null; }
        }

        private void EnsureAvatar(SessionPlayer sp)
        {
            if (sp == null || sp.IsLocal || sp.Avatar != null) return;
            if (Game == null || Game.SceneRoot == null || Game.State != GameState.Playing) return;
            // 読み込み中のゲストも表示する（位置が届くまでは出現位置に立つ）
            var ps = Game.Players.Get(sp.PlayerId) ?? Game.Players.AddRemote(sp.PlayerId);
            var go = new GameObject("RemotePlayer_" + sp.DisplayName);
            go.transform.SetParent(Game.SceneRoot, false);
            var layout = Game.Layout;
            go.transform.position = layout != null ? layout.OnGround(layout.PlayerSpawn) : Vector3.zero;
            var av = go.AddComponent<RemotePlayerAvatar>();
            av.Init(sp.PlayerId, ps, () => GetPlayerName(sp.PlayerId), sp.IsHost);
            sp.Avatar = av;
            ps.Avatar = go.transform;
        }

        private void RebuildAllAvatars()
        {
            foreach (var sp in players.Values) EnsureAvatar(sp);
        }

        private void DestroyAvatar(SessionPlayer sp)
        {
            if (sp?.Avatar == null) return;
            WorldLabels.Remove(sp.Avatar.LabelAnchor);
            WorldLabels.Remove(sp.Avatar.GestureAnchor);
            UnityEngine.Object.Destroy(sp.Avatar.gameObject);
            var ps = Game?.Players.Get(sp.PlayerId);
            if (ps != null) ps.Avatar = null;
            sp.Avatar = null;
        }

        private void DestroyAllAvatars()
        {
            foreach (var sp in players.Values) DestroyAvatar(sp);
        }

        /// <summary>リモートのプレイヤーを取り除く（PlayerLeftEvent を発行）。</summary>
        private void RemoveSessionPlayer(string playerId, bool publish)
        {
            if (!players.TryGetValue(playerId, out var sp) || sp.IsLocal) return;
            DestroyAvatar(sp);
            players.Remove(playerId);
            Game?.Players.Remove(playerId);
            if (publish) EventBus.Publish(new PlayerLeftEvent { PlayerId = playerId });
        }

        private void ClearSessionPlayers(bool publish)
        {
            foreach (var id in new List<string>(players.Keys))
            {
                if (players[id].IsLocal) continue;
                RemoveSessionPlayer(id, publish);
            }
            players.Clear();
        }

        private PlayerInfoDto LocalPlayerInfo(bool isHost)
        {
            var lp = Game.LocalPlayer;
            return new PlayerInfoDto
            {
                playerId = LocalId,
                displayName = lp.DisplayName,
                permission = (int)(isHost ? PermissionLevel.Owner : lp.Permission),
                appearanceJson = JsonUtility.ToJson(lp.Appearance),
                isHost = isHost
            };
        }

        // ------------------------------------------------------------
        // 位置（unreliable 約10Hz、補間は RemotePlayerAvatar）
        // ------------------------------------------------------------

        private void TransformUpdate(float dt)
        {
            if (Game.State != GameState.Playing || transport == null) return;
            var lp = Game.LocalPlayer;
            if (lp == null || lp.Avatar == null) return;
            transformTimer += dt;
            if (transformTimer < NetConfig.TransformSendInterval) return;
            transformTimer = 0f;
            var ctrl = Game.PlayerSys != null ? Game.PlayerSys.LocalController : null;
            var sample = new NetCodec.TransformSample
            {
                PlayerId = LocalId,
                Position = lp.Avatar.position,
                RotY = lp.Avatar.eulerAngles.y,
                AreaId = lp.CurrentAreaId,
                Moving = ctrl != null && ctrl.IsMoving,
                Sequence = ++transformSeq
            };
            var data = NetCodec.EncodeTransform(sample);
            if (IsHostSession)
            {
                foreach (var p in players.Values)
                    if (!p.IsLocal && p.Ready && p.NetId != null) transport.Send(p.NetId, data, NetDelivery.Unreliable);
            }
            else
            {
                if (transport.HostPeerId != null) transport.Send(transport.HostPeerId, data, NetDelivery.Unreliable);
            }
        }

        private void HandleTransform(string netId, NetCodec.TransformSample s)
        {
            if (IsHostSession)
            {
                var sender = FindPlayerByNet(netId);
                if (sender == null || !sender.Ready) return;
                s.PlayerId = sender.PlayerId; // なりすまし防止
                ApplyTransform(sender, s);
                // 他のゲストへ中継
                var data = NetCodec.EncodeTransform(s);
                foreach (var p in players.Values)
                    if (!p.IsLocal && p.Ready && p.NetId != null && p.NetId != netId) transport.Send(p.NetId, data, NetDelivery.Unreliable);
            }
            else if (Session == SessionState.GuestPlaying)
            {
                if (s.PlayerId == LocalId) return;
                if (players.TryGetValue(s.PlayerId, out var sp)) ApplyTransform(sp, s);
            }
        }

        private void ApplyTransform(SessionPlayer sp, NetCodec.TransformSample s)
        {
            // 送信側の連番が戻ったもの（遅れて届いた古い位置）は捨てる
            if (s.Sequence != 0 && sp.LastTransformSeq != 0 && s.Sequence <= sp.LastTransformSeq && sp.LastTransformSeq - s.Sequence < 100000) return;
            sp.LastTransformSeq = s.Sequence;
            var ps = Game.Players.Get(sp.PlayerId);
            if (ps != null && !string.IsNullOrEmpty(s.AreaId)) ps.CurrentAreaId = s.AreaId;
            if (sp.Avatar == null) EnsureAvatar(sp);
            sp.Avatar?.PushSample(s.Position, s.RotY, s.Moving, s.AreaId);
            if (Session == SessionState.GuestPlaying) MaybePlaceNearHost(sp, s);
        }

        private SessionPlayer FindPlayerByNet(string netId)
        {
            if (netId == null) return null;
            foreach (var p in players.Values) if (p.NetId == netId) return p;
            return null;
        }

        // ------------------------------------------------------------
        // 身振り（主人公は話さない：身振りで意思表示）
        // ------------------------------------------------------------

        private void OnLocalGesture(PlayerGestureEvent e)
        {
            if (!IsOnline || e.PlayerId != LocalId) return;
            var msg = new GestureMsg { playerId = LocalId, gesture = (int)e.Gesture };
            if (IsHostSession) BroadcastToGuests(NetMsg.Gesture, msg, null, true);
            else SendToHost(NetMsg.Gesture, msg);
        }

        private void ShowRemoteGesture(string playerId, PlayerGesture g)
        {
            if (players.TryGetValue(playerId, out var sp) && sp.Avatar != null) sp.Avatar.PlayGesture(g);
        }

        /// <summary>ホスト：ゲストの身振りを星の出来事として住民などへ伝える。</summary>
        private void PublishGuestGesture(SessionPlayer sp, PlayerGesture g)
        {
            var ps = Game.Players.Get(sp.PlayerId);
            string target = null;
            var dir = Game.Service<IResidentDirectory>();
            if (dir != null && ps != null)
            {
                float best = 5f * 5f;
                foreach (var r in dir.ActiveResidents)
                {
                    float d = (r.Position - ps.Position).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        target = r.Id;
                    }
                }
            }
            if (NetConfig.ForwardGuestGestureToQuests && target != null) Game.Service<IQuestService>()?.TryHandleGesture(target, sp.PlayerId, g);
            EventBus.Publish(new PlayerGestureEvent { PlayerId = sp.PlayerId, Gesture = g, TargetResidentId = target });
        }

        // ------------------------------------------------------------
        // 持ち物の変化（ゲスト：ホストのミラーへ要約を送る）
        // ------------------------------------------------------------

        private void OnItemAdded(ItemAddedEvent e) { if (e.PlayerId == LocalId) MarkProfileDirty(); }
        private void OnItemRemoved(ItemRemovedEvent e) { if (e.PlayerId == LocalId) MarkProfileDirty(); }
        private void OnMoneyChanged(MoneyChangedEvent e) { if (e.PlayerId == LocalId) MarkProfileDirty(); }
        private void OnToolUpgraded(ToolUpgradedEvent e) { if (e.PlayerId == LocalId) MarkProfileDirty(); }

        // ------------------------------------------------------------
        // 共通ヘルパー
        // ------------------------------------------------------------

        internal static string PermissionLabel(PermissionLevel p)
        {
            switch (p)
            {
                case PermissionLevel.Sightseeing: return Loc.T("観光", "Sightseeing");
                case PermissionLevel.Helper: return Loc.T("お手伝い", "Helper");
                case PermissionLevel.CoLiving: return Loc.T("共同生活", "Co-living");
                default: return Loc.T("星の持ち主", "Owner");
            }
        }

        internal float NetClock => clock;
    }

    /// <summary>通信をゲームの状態に関係なく毎フレーム回す常駐コンポーネント（Game の子）。</summary>
    public sealed class NetworkRunner : MonoBehaviour
    {
        public NetworkSystem System;

        private void Update()
        {
            System?.FrameUpdate(UnityEngine.Time.unscaledDeltaTime);
        }

        private void OnApplicationQuit()
        {
            try { System?.OnApplicationQuitting(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
