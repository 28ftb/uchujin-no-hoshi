#if UCHUJIN_EOS
// =====================================================================
// EOS P2P トランスポート（基本決定 v0.2 §6：通信は Epic Online Services の P2P）。
// Scripting Define Symbols に UCHUJIN_EOS を定義した時だけコンパイルされる。
//
// 前提：PlayEveryWare の「EOS Plugin for Unity」（EOS C# SDK 1.15 以降：Options を ref の struct で渡す版）。
//  ・EOSManager が Platform の初期化と Tick を行う（設定は README 参照）。
//  ・ログインは Connect インターフェースの DeviceId（Epic アカウント不要）。
//  ・ロビー（Lobby）で星を公開・検索・参加し、P2P（SocketId = NetConfig.EosSocketName）でデータを送る。
//
// ※ この環境には EOS SDK が無く型チェックされていない。API 名・シグネチャは SDK 1.16 を想定して書いており、
//   「Unity上で要確認」と書いた箇所は特に、導入する SDK の版に合わせて確認・修正すること。
// =====================================================================
using System;
using System.Collections.Generic;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.P2P;
using PlayEveryWare.EpicOnlineServices;

namespace Uchujin.Network
{
    public sealed class EosP2PTransport : INetTransport, ILobbyTransport
    {
        // チャンネル：0=reliable（分割あり）、1=unreliable、2=制御（参加の挨拶）
        private const byte ChannelReliable = 0;
        private const byte ChannelUnreliable = 1;
        private const byte ChannelControl = 2;

        private const byte CtrlHello = 1;
        private const byte CtrlWelcome = 2;
        private const byte CtrlReject = 3;
        private const byte CtrlBye = 4;

        private const int MaxSendsPerPoll = 256;
        private const int MaxReceivesPerPoll = 2048;
        private const float HandshakeTimeout = 20f;
        private const float HelloRetryInterval = 1f;

        // ロビーの属性キー
        private const string AttrGame = "GAME";
        private const string AttrGameValue = "uchujin";
        private const string AttrVersion = "VER";
        private const string AttrCode = "CODE";
        private const string AttrHostName = "HOSTNAME";
        private const string AttrPlanet = "PLANET";

        private sealed class Peer
        {
            public string Id;
            public ProductUserId User;
            public bool Connected;
            public uint NextMessageId = 1;
            public readonly Queue<byte[]> SendQueue = new Queue<byte[]>();
            public readonly FragmentAssembler Assembler = new FragmentAssembler();
        }

        private enum LoginState { None, LoggingIn, LoggedIn, Failed }

        public string Kind => "eos";
        public bool IsHost { get; private set; }
        public TransportState State { get; private set; } = TransportState.Idle;
        public string StatusText { get; private set; } = "";
        public string HostPeerId { get; private set; }
        public string InviteCode { get; private set; }

        public event Action<string> PeerConnected;
        public event Action<string, string> PeerDisconnected;
        public event Action<string, byte[]> Received;

        private Epic.OnlineServices.Platform.PlatformInterface platform;
        private ConnectInterface connect;
        private P2PInterface p2p;
        private LobbyInterface lobby;
        private ProductUserId localUser;
        private SocketId socketId;
        private ulong notifyRequest;
        private ulong notifyClosed;
        private ulong notifyEstablished;
        private ulong notifyAuthExpiration;
        private bool p2pRegistered;

        private LoginState loginState = LoginState.None;
        private readonly List<Action> afterLogin = new List<Action>();
        private readonly List<Action<string>> loginFailed = new List<Action<string>>();

        private string lobbyId;
        private LobbySearch currentSearch;
        private readonly Dictionary<string, LobbyDetails> searchDetails = new Dictionary<string, LobbyDetails>();
        private Action<List<LobbyListing>> searchDone;
        private Action<string> searchError;
        private bool joinAfterSearch;

        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        private TransportHostOptions hostOptions;
        private float now;
        private float handshakeStarted;
        private float lastHelloSent;
        private bool waitingWelcome;

        // ------------------------------------------------------------
        // 初期化・ログイン（Connect / DeviceId）
        // ------------------------------------------------------------

        private bool EnsureInterfaces()
        {
            if (platform != null) return true;
            try
            {
                // シーンやプレハブを使わない構成なので、EOSManager（MonoBehaviour：初期化と Tick を担当）が無ければ作る。
                // EOSManager は Awake で設定ファイルを読んで Platform を初期化する（プラグインの版により挙動が違う。Unity上で要確認）
                if (UnityEngine.Object.FindFirstObjectByType<EOSManager>() == null)
                {
                    var go = new GameObject("EOSManager");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    go.AddComponent<EOSManager>();
                }
                // PlayEveryWare EOS Plugin：EOSManager が初期化した PlatformInterface を取得（Unity上で要確認）
                platform = EOSManager.Instance.GetEOSPlatformInterface();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (platform == null)
            {
                Fail("EOS が初期化されていません（EOSManager / 設定を確認してください）");
                return false;
            }
            connect = platform.GetConnectInterface();
            p2p = platform.GetP2PInterface();
            lobby = platform.GetLobbyInterface();
            socketId = new SocketId { SocketName = NetConfig.EosSocketName };
            return connect != null && p2p != null && lobby != null;
        }

        private void EnsureLogin(Action onOk, Action<string> onFail)
        {
            if (!EnsureInterfaces())
            {
                onFail?.Invoke(StatusText);
                return;
            }
            if (loginState == LoginState.LoggedIn && localUser != null)
            {
                onOk?.Invoke();
                return;
            }
            if (onOk != null) afterLogin.Add(onOk);
            if (onFail != null) loginFailed.Add(onFail);
            if (loginState == LoginState.LoggingIn) return;
            loginState = LoginState.LoggingIn;
            StatusText = "EOS にログイン中…";

            // 1. 端末IDを作る（既にあれば DuplicateNotAllowed が返る）
            var opts = new CreateDeviceIdOptions { DeviceModel = Truncate(SystemInfo.deviceModel, 60) };
            connect.CreateDeviceId(ref opts, null, OnCreateDeviceId);
        }

        private void OnCreateDeviceId(ref CreateDeviceIdCallbackInfo info)
        {
            if (info.ResultCode == Result.Success || info.ResultCode == Result.DuplicateNotAllowed) DoConnectLogin();
            else LoginFailed("端末IDを作成できません: " + info.ResultCode);
        }

        private void DoConnectLogin()
        {
            string name = "Traveler";
            try
            {
                var g = Uchujin.Core.Game.Instance;
                if (g != null && g.LocalPlayer != null) name = g.LocalPlayer.DisplayName;
            }
            catch { }
            // DeviceId ログインでは UserLoginInfo（表示名・32文字以内）が必須
            var opts = new Epic.OnlineServices.Connect.LoginOptions
            {
                Credentials = new Epic.OnlineServices.Connect.Credentials
                {
                    Type = ExternalCredentialType.DeviceidAccessToken,
                    Token = null
                },
                UserLoginInfo = new UserLoginInfo { DisplayName = Truncate(name, 32) }
            };
            connect.Login(ref opts, null, OnConnectLogin);
        }

        private void OnConnectLogin(ref Epic.OnlineServices.Connect.LoginCallbackInfo info)
        {
            if (info.ResultCode == Result.Success)
            {
                LoginSucceeded(info.LocalUserId);
                return;
            }
            if (info.ResultCode == Result.InvalidUser)
            {
                // 初回：Product User を作る
                var opts = new CreateUserOptions { ContinuanceToken = info.ContinuanceToken };
                connect.CreateUser(ref opts, null, OnCreateUser);
                return;
            }
            LoginFailed("EOS ログイン失敗: " + info.ResultCode);
        }

        private void OnCreateUser(ref CreateUserCallbackInfo info)
        {
            if (info.ResultCode == Result.Success) LoginSucceeded(info.LocalUserId);
            else LoginFailed("EOS ユーザー作成失敗: " + info.ResultCode);
        }

        private void LoginSucceeded(ProductUserId user)
        {
            localUser = user;
            loginState = LoginState.LoggedIn;
            StatusText = "EOS ログイン済み";
            if (notifyAuthExpiration == 0)
            {
                // Connect のトークンは約1時間で切れるので再ログインする（Unity上で要確認）
                var o = new AddNotifyAuthExpirationOptions();
                notifyAuthExpiration = connect.AddNotifyAuthExpiration(ref o, null, OnAuthExpiration);
            }
            var list = new List<Action>(afterLogin);
            afterLogin.Clear();
            loginFailed.Clear();
            foreach (var a in list)
            {
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void OnAuthExpiration(ref AuthExpirationCallbackInfo info)
        {
            loginState = LoginState.LoggingIn;
            DoConnectLogin();
        }

        private void LoginFailed(string reason)
        {
            loginState = LoginState.Failed;
            var list = new List<Action<string>>(loginFailed);
            afterLogin.Clear();
            loginFailed.Clear();
            Fail(reason);
            foreach (var a in list)
            {
                try { a(reason); } catch (Exception e) { Debug.LogException(e); }
            }
            loginState = LoginState.None; // 次の操作で再試行できるように
        }

        private void Fail(string reason)
        {
            State = TransportState.Failed;
            StatusText = reason;
            Debug.LogWarning("[EosP2PTransport] " + reason);
        }

        // ------------------------------------------------------------
        // P2P の通知登録
        // ------------------------------------------------------------

        private void RegisterP2P()
        {
            if (p2pRegistered) return;
            p2pRegistered = true;
            var reqOpts = new AddNotifyPeerConnectionRequestOptions { LocalUserId = localUser, SocketId = socketId };
            notifyRequest = p2p.AddNotifyPeerConnectionRequest(ref reqOpts, null, OnIncomingConnectionRequest);
            var closedOpts = new AddNotifyPeerConnectionClosedOptions { LocalUserId = localUser, SocketId = socketId };
            notifyClosed = p2p.AddNotifyPeerConnectionClosed(ref closedOpts, null, OnRemoteConnectionClosed);
            var estOpts = new AddNotifyPeerConnectionEstablishedOptions { LocalUserId = localUser, SocketId = socketId };
            notifyEstablished = p2p.AddNotifyPeerConnectionEstablished(ref estOpts, null, OnPeerConnectionEstablished);
        }

        private void UnregisterP2P()
        {
            if (!p2pRegistered || p2p == null) return;
            p2pRegistered = false;
            if (notifyRequest != 0) p2p.RemoveNotifyPeerConnectionRequest(notifyRequest);
            if (notifyClosed != 0) p2p.RemoveNotifyPeerConnectionClosed(notifyClosed);
            if (notifyEstablished != 0) p2p.RemoveNotifyPeerConnectionEstablished(notifyEstablished);
            notifyRequest = notifyClosed = notifyEstablished = 0;
        }

        private void OnIncomingConnectionRequest(ref OnIncomingConnectionRequestInfo info)
        {
            string id = UserKey(info.RemoteUserId);
            // ホストは誰からの接続も受け付ける（人数は挨拶 CtrlHello で確認）。ゲストはホストからのみ。
            if (!IsHost && id != HostPeerId) return;
            var opts = new AcceptConnectionOptions { LocalUserId = localUser, RemoteUserId = info.RemoteUserId, SocketId = socketId };
            var r = p2p.AcceptConnection(ref opts);
            if (r != Result.Success) Debug.LogWarning("[EosP2PTransport] AcceptConnection: " + r);
        }

        private void OnPeerConnectionEstablished(ref OnPeerConnectionEstablishedInfo info)
        {
            // 実際の参加確定は制御チャンネルの挨拶で行う（人数制限のため）
        }

        private void OnRemoteConnectionClosed(ref OnRemoteConnectionClosedInfo info)
        {
            string id = UserKey(info.RemoteUserId);
            if (peers.TryGetValue(id, out var p)) DropPeer(p, "closed:" + info.Reason, false);
        }

        // ------------------------------------------------------------
        // ホスト：ロビー作成
        // ------------------------------------------------------------

        public void StartHost(TransportHostOptions options)
        {
            IsHost = true;
            hostOptions = options ?? new TransportHostOptions();
            State = TransportState.Starting;
            EnsureLogin(CreateLobby, null);
        }

        private void CreateLobby()
        {
            RegisterP2P();
            StatusText = "ロビーを作成中…";
            var opts = new CreateLobbyOptions
            {
                LocalUserId = localUser,
                MaxLobbyMembers = (uint)Math.Max(2, hostOptions.MaxPeers + 1),
                PermissionLevel = LobbyPermissionLevel.Publicadvertised, // 列挙子名は Unity上で要確認
                PresenceEnabled = false,
                AllowInvites = true,
                BucketId = NetConfig.EosBucketId,
                DisableHostMigration = true, // ホスト移行はしない（マルチ §4.4・§20.5）
                EnableRTCRoom = false
            };
            lobby.CreateLobby(ref opts, null, OnCreateLobby);
        }

        private void OnCreateLobby(ref CreateLobbyCallbackInfo info)
        {
            if (info.ResultCode != Result.Success)
            {
                Fail("ロビーを作成できません: " + info.ResultCode);
                return;
            }
            lobbyId = info.LobbyId;
            InviteCode = MakeInviteCode();
            var mOpts = new UpdateLobbyModificationOptions { LocalUserId = localUser, LobbyId = lobbyId };
            if (lobby.UpdateLobbyModification(ref mOpts, out LobbyModification mod) != Result.Success || mod == null)
            {
                Fail("ロビーの設定に失敗しました");
                return;
            }
            AddAttribute(mod, AttrGame, AttrGameValue);
            AddAttribute(mod, AttrVersion, NetConfig.ProtocolVersion.ToString());
            AddAttribute(mod, AttrCode, InviteCode);
            AddAttribute(mod, AttrHostName, Truncate(hostOptions.HostName ?? "", 64));
            AddAttribute(mod, AttrPlanet, Truncate(hostOptions.PlanetName ?? "", 64));
            var upd = new UpdateLobbyOptions { LobbyModificationHandle = mod };
            lobby.UpdateLobby(ref upd, null, OnUpdateLobby);
            mod.Release();
        }

        private void OnUpdateLobby(ref UpdateLobbyCallbackInfo info)
        {
            if (info.ResultCode != Result.Success)
            {
                Fail("ロビーの属性を設定できません: " + info.ResultCode);
                return;
            }
            State = TransportState.Hosting;
            StatusText = "EOS ロビー公開中　招待コード: " + InviteCode;
        }

        private static void AddAttribute(LobbyModification mod, string key, string value)
        {
            var opts = new LobbyModificationAddAttributeOptions
            {
                Attribute = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = value } },
                Visibility = LobbyAttributeVisibility.Public
            };
            var r = mod.AddAttribute(ref opts);
            if (r != Result.Success) Debug.LogWarning($"[EosP2PTransport] AddAttribute {key}: {r}");
        }

        private static string MakeInviteCode()
        {
            // 見間違えやすい文字を除く。乱数は決定的生成ではないので Guid から作る（決定的生成の規約の対象外）。
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = Guid.NewGuid().ToByteArray();
            var c = new char[NetConfig.InviteCodeLength];
            for (int i = 0; i < c.Length; i++) c[i] = chars[bytes[i] % chars.Length];
            return new string(c);
        }

        // ------------------------------------------------------------
        // ゲスト：ロビー検索・参加
        // ------------------------------------------------------------

        public void RefreshLobbies(Action<List<LobbyListing>> onDone, Action<string> onError)
        {
            EnsureLogin(() => StartSearch(AttrGame, AttrGameValue, onDone, onError, false), onError);
        }

        public void JoinByInviteCode(string code)
        {
            IsHost = false;
            State = TransportState.Connecting;
            code = (code ?? "").Trim().ToUpperInvariant();
            EnsureLogin(() => StartSearch(AttrCode, code, null, err => Fail(err), true), err => Fail(err));
        }

        public void Connect(string target)
        {
            if (string.IsNullOrEmpty(target))
            {
                Fail("接続先が空です");
                return;
            }
            if (target.Length == NetConfig.InviteCodeLength) JoinByInviteCode(target);
            else JoinLobby(target);
        }

        private void StartSearch(string key, string value, Action<List<LobbyListing>> onDone, Action<string> onError, bool joinFirst)
        {
            ReleaseSearch();
            var cOpts = new CreateLobbySearchOptions { MaxResults = 50 };
            if (lobby.CreateLobbySearch(ref cOpts, out currentSearch) != Result.Success || currentSearch == null)
            {
                onError?.Invoke("ロビー検索を開始できません");
                return;
            }
            var p = new LobbySearchSetParameterOptions
            {
                Parameter = new AttributeData { Key = key, Value = new AttributeDataValue { AsUtf8 = value } },
                ComparisonOp = ComparisonOp.Equal
            };
            currentSearch.SetParameter(ref p);
            if (key != AttrVersion)
            {
                var pv = new LobbySearchSetParameterOptions
                {
                    Parameter = new AttributeData { Key = AttrVersion, Value = new AttributeDataValue { AsUtf8 = NetConfig.ProtocolVersion.ToString() } },
                    ComparisonOp = ComparisonOp.Equal
                };
                currentSearch.SetParameter(ref pv);
            }
            searchDone = onDone;
            searchError = onError;
            joinAfterSearch = joinFirst;
            var f = new LobbySearchFindOptions { LocalUserId = localUser };
            currentSearch.Find(ref f, null, OnLobbySearchFind);
        }

        private void OnLobbySearchFind(ref LobbySearchFindCallbackInfo info)
        {
            var list = new List<LobbyListing>();
            if (info.ResultCode != Result.Success && info.ResultCode != Result.NotFound)
            {
                searchError?.Invoke("ロビー検索に失敗: " + info.ResultCode);
                return;
            }
            if (currentSearch != null && info.ResultCode == Result.Success)
            {
                var cnt = new LobbySearchGetSearchResultCountOptions();
                uint n = currentSearch.GetSearchResultCount(ref cnt);
                for (uint i = 0; i < n; i++)
                {
                    var c = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = i };
                    if (currentSearch.CopySearchResultByIndex(ref c, out LobbyDetails details) != Result.Success || details == null) continue;
                    var io = new LobbyDetailsCopyInfoOptions();
                    if (details.CopyInfo(ref io, out LobbyDetailsInfo? dinfo) != Result.Success || !dinfo.HasValue)
                    {
                        details.Release();
                        continue;
                    }
                    string id = dinfo.Value.LobbyId;
                    if (searchDetails.TryGetValue(id, out var old) && old != null) old.Release();
                    searchDetails[id] = details;
                    list.Add(new LobbyListing
                    {
                        LobbyId = id,
                        HostName = ReadAttribute(details, AttrHostName),
                        PlanetName = ReadAttribute(details, AttrPlanet),
                        InviteCode = ReadAttribute(details, AttrCode),
                        MaxMembers = (int)dinfo.Value.MaxMembers,
                        Members = (int)(dinfo.Value.MaxMembers - dinfo.Value.AvailableSlots)
                    });
                }
            }
            if (joinAfterSearch)
            {
                joinAfterSearch = false;
                if (list.Count == 0)
                {
                    Fail("招待コードの星が見つかりません");
                    return;
                }
                JoinLobby(list[0].LobbyId);
                return;
            }
            searchDone?.Invoke(list);
        }

        private static string ReadAttribute(LobbyDetails details, string key)
        {
            var o = new LobbyDetailsCopyAttributeByKeyOptions { AttrKey = key };
            if (details.CopyAttributeByKey(ref o, out Epic.OnlineServices.Lobby.Attribute? attr) != Result.Success || !attr.HasValue) return "";
            if (!attr.Value.Data.HasValue) return "";
            string s = attr.Value.Data.Value.Value.AsUtf8; // Utf8String → string（Unity上で要確認）
            return s ?? "";
        }

        public void JoinLobby(string id)
        {
            IsHost = false;
            State = TransportState.Connecting;
            if (id == null || !searchDetails.TryGetValue(id, out var details) || details == null)
            {
                Fail("ロビー情報がありません。一覧を更新してください");
                return;
            }
            EnsureLogin(() =>
            {
                RegisterP2P();
                StatusText = "ロビーに参加中…";
                var j = new JoinLobbyOptions { LobbyDetailsHandle = details, LocalUserId = localUser, PresenceEnabled = false };
                lobby.JoinLobby(ref j, null, OnJoinLobby);
            }, err => Fail(err));
        }

        private void OnJoinLobby(ref JoinLobbyCallbackInfo info)
        {
            if (info.ResultCode != Result.Success)
            {
                Fail("ロビーに参加できません: " + info.ResultCode);
                return;
            }
            lobbyId = info.LobbyId;
            if (!searchDetails.TryGetValue(lobbyId, out var details) || details == null)
            {
                Fail("ロビー情報を取得できません");
                return;
            }
            var oo = new LobbyDetailsGetLobbyOwnerOptions();
            var owner = details.GetLobbyOwner(ref oo);
            if (owner == null)
            {
                Fail("ホストが見つかりません");
                return;
            }
            HostPeerId = UserKey(owner);
            var hp = GetOrCreatePeer(owner);
            hp.Connected = false;
            waitingWelcome = true;
            handshakeStarted = now;
            lastHelloSent = -10f;
            StatusText = "ホストに接続中…";
        }

        // ------------------------------------------------------------
        // 送受信
        // ------------------------------------------------------------

        public void Poll(float realDeltaTime)
        {
            now += Mathf.Max(0f, realDeltaTime);
            if (p2p == null || localUser == null) return;

            // ゲスト：ホストへ挨拶（P2P は最初の送信で接続が開く）
            if (!IsHost && waitingWelcome && HostPeerId != null && peers.TryGetValue(HostPeerId, out var host))
            {
                if (now - handshakeStarted > HandshakeTimeout)
                {
                    waitingWelcome = false;
                    Fail("ホストから応答がありません");
                    peers.Remove(HostPeerId);
                    PeerDisconnected?.Invoke(HostPeerId, "timeout");
                }
                else if (now - lastHelloSent > HelloRetryInterval)
                {
                    lastHelloSent = now;
                    SendRaw(host.User, ChannelControl, new[] { CtrlHello, (byte)NetConfig.ProtocolVersion }, true);
                }
            }

            ReceiveAll();

            int sends = 0;
            foreach (var p in peers.Values)
            {
                if (!p.Connected) continue;
                while (p.SendQueue.Count > 0 && sends < MaxSendsPerPoll)
                {
                    var r = SendRaw(p.User, ChannelReliable, p.SendQueue.Peek(), true);
                    if (r == Result.LimitExceeded) break; // 送信キューが満杯 → 次のフレームで再試行
                    p.SendQueue.Dequeue();
                    sends++;
                }
                p.Assembler.Cleanup(now);
            }
        }

        private void ReceiveAll()
        {
            var sizeOpts = new GetNextReceivedPacketSizeOptions { LocalUserId = localUser, RequestedChannel = null };
            for (int i = 0; i < MaxReceivesPerPoll; i++)
            {
                if (p2p.GetNextReceivedPacketSize(ref sizeOpts, out uint size) != Result.Success) break;
                var buffer = new byte[Math.Max(1, (int)size)];
                var recvOpts = new ReceivePacketOptions { LocalUserId = localUser, MaxDataSizeBytes = size, RequestedChannel = null };
                ProductUserId peerUser = null;
                SocketId sock = default;
                // SDK 1.15/1.16 のシグネチャ（Unity上で要確認）：
                //   Result ReceivePacket(ref ReceivePacketOptions, ref ProductUserId, ref SocketId, out byte, ArraySegment<byte>, out uint)
                var r = p2p.ReceivePacket(ref recvOpts, ref peerUser, ref sock, out byte channel, new ArraySegment<byte>(buffer), out uint written);
                if (r != Result.Success || peerUser == null) break;
                HandlePacket(peerUser, channel, buffer, (int)written);
            }
        }

        private void HandlePacket(ProductUserId user, byte channel, byte[] data, int length)
        {
            string id = UserKey(user);
            peers.TryGetValue(id, out var peer);

            if (channel == ChannelControl)
            {
                if (length < 1) return;
                byte ctrl = data[0];
                if (IsHost && ctrl == CtrlHello)
                {
                    int ver = length > 1 ? data[1] : 0;
                    if (ver != NetConfig.ProtocolVersion)
                    {
                        SendRaw(user, ChannelControl, new[] { CtrlReject, (byte)1 }, true);
                        return;
                    }
                    if (peer != null && peer.Connected)
                    {
                        SendRaw(user, ChannelControl, new[] { CtrlWelcome }, true);
                        return;
                    }
                    int connected = 0;
                    foreach (var p in peers.Values) if (p.Connected) connected++;
                    if (connected >= Math.Max(1, hostOptions != null ? hostOptions.MaxPeers : NetConfig.MaxPlayers - 1))
                    {
                        SendRaw(user, ChannelControl, new[] { CtrlReject, (byte)2 }, true);
                        return;
                    }
                    peer = GetOrCreatePeer(user);
                    peer.Connected = true;
                    SendRaw(user, ChannelControl, new[] { CtrlWelcome }, true);
                    PeerConnected?.Invoke(peer.Id);
                }
                else if (!IsHost && ctrl == CtrlWelcome && peer != null && id == HostPeerId && !peer.Connected)
                {
                    waitingWelcome = false;
                    peer.Connected = true;
                    State = TransportState.Connected;
                    StatusText = "EOS 接続済み";
                    PeerConnected?.Invoke(peer.Id);
                }
                else if (!IsHost && ctrl == CtrlReject && id == HostPeerId)
                {
                    waitingWelcome = false;
                    string reason = length > 1 && data[1] == 2 ? "full" : "version";
                    Fail("参加を断られました: " + reason);
                    peers.Remove(id);
                    PeerDisconnected?.Invoke(id, "rejected:" + reason);
                }
                else if (ctrl == CtrlBye && peer != null)
                {
                    DropPeer(peer, "remote_closed", false);
                }
                return;
            }

            if (peer == null || !peer.Connected) return;
            if (channel == ChannelUnreliable)
            {
                var payload = new byte[length];
                Buffer.BlockCopy(data, 0, payload, 0, length);
                Received?.Invoke(peer.Id, payload);
            }
            else if (channel == ChannelReliable)
            {
                var msg = peer.Assembler.Accept(data, 0, length, now);
                if (msg != null) Received?.Invoke(peer.Id, msg);
            }
        }

        public void Send(string peerId, byte[] data, NetDelivery delivery)
        {
            if (peerId == null || data == null || !peers.TryGetValue(peerId, out var p) || !p.Connected) return;
            if (delivery == NetDelivery.Unreliable)
            {
                if (data.Length > NetConfig.EosMaxPacketSize)
                {
                    Debug.LogWarning("[EosP2PTransport] unreliable データが大きすぎます: " + data.Length);
                    return;
                }
                SendRaw(p.User, ChannelUnreliable, data, false);
                return;
            }
            foreach (var frag in FragmentAssembler.Split(data, NetConfig.EosMaxPacketSize, p.NextMessageId++))
                p.SendQueue.Enqueue(frag);
        }

        private Result SendRaw(ProductUserId to, byte channel, byte[] data, bool reliable)
        {
            var opts = new SendPacketOptions
            {
                LocalUserId = localUser,
                RemoteUserId = to,
                SocketId = socketId,
                Channel = channel,
                Data = new ArraySegment<byte>(data), // 古い SDK では byte[]（Unity上で要確認）
                AllowDelayedDelivery = true,
                Reliability = reliable ? PacketReliability.ReliableOrdered : PacketReliability.UnreliableUnordered
            };
            var r = p2p.SendPacket(ref opts);
            if (r != Result.Success && r != Result.LimitExceeded) Debug.LogWarning("[EosP2PTransport] SendPacket: " + r);
            return r;
        }

        public void DisconnectPeer(string peerId)
        {
            if (peerId != null && peers.TryGetValue(peerId, out var p)) DropPeer(p, "local_closed", true);
        }

        private void DropPeer(Peer p, string reason, bool notifyRemote)
        {
            if (notifyRemote && p2p != null && localUser != null)
            {
                SendRaw(p.User, ChannelControl, new[] { CtrlBye }, true);
                var c = new CloseConnectionOptions { LocalUserId = localUser, RemoteUserId = p.User, SocketId = socketId };
                p2p.CloseConnection(ref c);
            }
            bool was = p.Connected;
            p.Connected = false;
            peers.Remove(p.Id);
            if (!IsHost)
            {
                State = TransportState.Closed;
                StatusText = "切断: " + reason;
            }
            if (was) PeerDisconnected?.Invoke(p.Id, reason);
        }

        public void Shutdown()
        {
            if (p2p != null && localUser != null)
            {
                foreach (var p in new List<Peer>(peers.Values))
                {
                    // 送信待ちを可能な範囲で送ってから閉じる（HostClosing / Leave を届けるため）
                    int guard = 0;
                    while (p.Connected && p.SendQueue.Count > 0 && guard++ < 512)
                    {
                        if (SendRaw(p.User, ChannelReliable, p.SendQueue.Peek(), true) == Result.LimitExceeded) break;
                        p.SendQueue.Dequeue();
                    }
                    if (p.Connected) SendRaw(p.User, ChannelControl, new[] { CtrlBye }, true);
                }
                var cc = new CloseConnectionsOptions { LocalUserId = localUser, SocketId = socketId };
                p2p.CloseConnections(ref cc);
            }
            peers.Clear();
            if (lobby != null && localUser != null && !string.IsNullOrEmpty(lobbyId))
            {
                if (IsHost)
                {
                    var d = new DestroyLobbyOptions { LocalUserId = localUser, LobbyId = lobbyId };
                    lobby.DestroyLobby(ref d, null, OnDestroyLobby);
                }
                else
                {
                    var l = new LeaveLobbyOptions { LocalUserId = localUser, LobbyId = lobbyId };
                    lobby.LeaveLobby(ref l, null, OnLeaveLobby);
                }
            }
            lobbyId = null;
            InviteCode = null;
            UnregisterP2P();
            ReleaseSearch();
            if (notifyAuthExpiration != 0 && connect != null)
            {
                connect.RemoveNotifyAuthExpiration(notifyAuthExpiration);
                notifyAuthExpiration = 0;
            }
            State = TransportState.Closed;
        }

        private void OnDestroyLobby(ref DestroyLobbyCallbackInfo info)
        {
            if (info.ResultCode != Result.Success) Debug.LogWarning("[EosP2PTransport] DestroyLobby: " + info.ResultCode);
        }

        private void OnLeaveLobby(ref LeaveLobbyCallbackInfo info)
        {
            if (info.ResultCode != Result.Success) Debug.LogWarning("[EosP2PTransport] LeaveLobby: " + info.ResultCode);
        }

        private void ReleaseSearch()
        {
            foreach (var d in searchDetails.Values) d?.Release();
            searchDetails.Clear();
            currentSearch?.Release();
            currentSearch = null;
        }

        public void Dispose() => Shutdown();

        private Peer GetOrCreatePeer(ProductUserId user)
        {
            string id = UserKey(user);
            if (!peers.TryGetValue(id, out var p))
            {
                p = new Peer { Id = id, User = user };
                peers[id] = p;
            }
            return p;
        }

        /// <summary>ProductUserId を文字列キーへ（ToString() は SDK で override されている想定。Unity上で要確認）。</summary>
        private static string UserKey(ProductUserId u) => u == null ? "" : u.ToString();

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max);
        }
    }
}
#endif
