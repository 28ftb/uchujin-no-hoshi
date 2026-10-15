using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>ホスト側：星を開く・参加受付・スナップショット・差分同期・コマンド実行・ゲストの持ち物ミラー（INetworkBridge）。</summary>
    public sealed partial class NetworkSystem
    {
        private NetPlanetSave planetSave = new NetPlanetSave();
        /// <summary>挨拶（Hello）前の接続：ピアID → 接続時刻。</summary>
        private readonly Dictionary<string, float> pendingPeers = new Dictionary<string, float>();
        private readonly HashSet<string> dirtyKeys = new HashSet<string>();
        private readonly Dictionary<string, ulong> lastSentHash = new Dictionary<string, ulong>();
        private readonly Dictionary<string, float> lastSentTime = new Dictionary<string, float>();
        private float syncTimer;
        private float timeSyncTimer;
        private float flagsTimer;
        private ulong lastFlagsHash;

        /// <summary>挨拶が来ないまま待つ秒数。</summary>
        private const float HelloTimeout = 20f;
        /// <summary>1つのスナップショットメッセージにまとめる目安のバイト数。</summary>
        private const int SnapshotBatchBytes = 48 * 1024;

        // ------------------------------------------------------------
        // 星を開く／閉じる
        // ------------------------------------------------------------

        /// <summary>プレイ中の自分の星を開く（Esc メニュー → マルチプレイ → 星を開く）。</summary>
        public bool StartHosting(string kind, int udpPort)
        {
            if (Game == null || Game.State != GameState.Playing || Game.Planet == null || Game.IsBusy)
            {
                UIManager.Toast(Loc.T("星で遊んでいる時に開けます", "You can open your planet while playing"));
                return false;
            }
            if (WorldAuthority.Role != NetRole.Offline || Session != SessionState.None)
            {
                UIManager.Toast(Loc.T("すでにマルチプレイ中です", "Already in a multiplayer session"));
                return false;
            }
            var t = TransportFactory.Create(kind);
            if (t == null)
            {
                UIManager.Message(Loc.T("マルチプレイ", "Multiplayer"), Loc.T("EOS は未設定です（UCHUJIN_EOS が定義されていません）。", "EOS is not configured (UCHUJIN_EOS is not defined)."));
                return false;
            }
            ShutdownTransport();
            AttachTransport(t);
            LastError = null;
            players.Clear();
            pendingPeers.Clear();
            dirtyKeys.Clear();
            lastSentHash.Clear();
            lastSentTime.Clear();
            var lp = Game.LocalPlayer;
            lp.Permission = PermissionLevel.Owner;
            var self = new SessionPlayer
            {
                PlayerId = LocalId,
                DisplayName = lp.DisplayName,
                Permission = PermissionLevel.Owner,
                IsHost = true,
                IsLocal = true,
                AppearanceJson = JsonUtility.ToJson(lp.Appearance),
                Ready = true
            };
            players[self.PlayerId] = self;

            WorldAuthority.Role = NetRole.Host;
            WorldAuthority.Bridge = this;
            Session = SessionState.HostStarting;
            var speech = Game.Service<ISpeechService>();
            transport.StartHost(new TransportHostOptions
            {
                MaxPeers = NetConfig.MaxPlayers - 1,
                Port = udpPort > 0 ? udpPort : NetConfig.DefaultUdpPort,
                HostName = lp.DisplayName,
                PlanetName = !string.IsNullOrEmpty(Game.Planet.playerGivenName) ? Game.Planet.playerGivenName : speech?.PlanetName,
                WorldId = Game.Planet.worldId
            });
            HostStartingUpdate();
            return Session != SessionState.None;
        }

        private void HostStartingUpdate()
        {
            if (transport == null) return;
            if (transport.State == TransportState.Hosting)
            {
                Session = SessionState.Hosting;
                Game.SaveGame(false);
                UIManager.Toast(Loc.T("星を開きました。友達を待っています。", "Your planet is open. Waiting for friends."));
            }
            else if (transport.State == TransportState.Failed || transport.State == TransportState.Closed)
            {
                LastError = transport.StatusText;
                string msg = transport.StatusText;
                HostShutdown(false, true);
                UIManager.Message(Loc.T("星を開けませんでした", "Could not open your planet"), msg);
            }
        }

        /// <summary>マルチを終える。stayOnPlanet=true なら星はソロで続く（Esc メニューから閉じた場合）。</summary>
        public void HostShutdown(bool notifyGuests, bool stayOnPlanet)
        {
            AbortAllTrades("星が閉じられました", "The planet was closed");
            CancelSleepVote();
            if (notifyGuests) BroadcastToGuests(NetMsg.HostClosing, new LeaveMsg { reason = "closed" });
            ShutdownTransport();
            foreach (var id in new List<string>(players.Keys))
            {
                if (players[id].IsLocal) continue;
                RemoveSessionPlayer(id, true);
            }
            players.Clear();
            pendingPeers.Clear();
            dirtyKeys.Clear();
            Session = SessionState.None;
            WorldAuthority.Bridge = null;
            WorldAuthority.Role = NetRole.Offline;
            if (stayOnPlanet && Game != null && Game.State == GameState.Playing)
            {
                Game.SaveGame(false);
                UIManager.Toast(Loc.T("マルチプレイを終了しました", "Multiplayer session ended"));
            }
        }

        // ------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------

        private void HostUpdate(float dt)
        {
            // 挨拶の来ない接続を切る
            if (pendingPeers.Count > 0)
            {
                List<string> expired = null;
                foreach (var kv in pendingPeers)
                    if (clock - kv.Value > HelloTimeout) (expired ?? (expired = new List<string>())).Add(kv.Key);
                if (expired != null)
                    foreach (var id in expired)
                    {
                        pendingPeers.Remove(id);
                        transport?.DisconnectPeer(id);
                    }
            }
            // 追い出し予定
            foreach (var p in new List<SessionPlayer>(players.Values))
            {
                if (p.KickAt > 0f && clock >= p.KickAt)
                {
                    p.KickAt = -1f;
                    string netId = p.NetId;
                    RemoveGuest(p, Loc.T("退出させました", "removed"));
                    if (netId != null) transport?.DisconnectPeer(netId);
                }
            }
            UpdateRejectedPeers();
            HostSyncUpdate(dt);
        }

        /// <summary>MarkDirty されたキーを 0.3〜0.5 秒ごとにまとめて送る（キー単位の状態。完全な差分ではない）。</summary>
        private void HostSyncUpdate(float dt)
        {
            syncTimer += dt;
            timeSyncTimer += dt;
            flagsTimer += dt;
            if (timeSyncTimer >= NetConfig.TimeSyncInterval)
            {
                timeSyncTimer = 0f;
                dirtyKeys.Add("core.time"); // 時間天候 §16：ホストの時刻・日付を共有
            }
            if (flagsTimer >= NetConfig.FlagsCheckInterval)
            {
                flagsTimer = 0f;
                var fj = Game.CapturePlanetState("core.flags");
                ulong h = fj != null ? StableHash.Fnv1a64(fj) : 0;
                if (h != lastFlagsHash)
                {
                    lastFlagsHash = h;
                    dirtyKeys.Add("core.flags");
                }
            }
            if (syncTimer < NetConfig.StateSyncInterval) return;
            syncTimer = 0f;
            if (dirtyKeys.Count == 0) return;

            bool anyGuest = false;
            foreach (var p in players.Values) if (!p.IsLocal && p.NetId != null) anyGuest = true;
            if (!anyGuest)
            {
                dirtyKeys.Clear();
                return;
            }

            var msg = new StateEntriesMsg();
            var keep = new List<string>();
            foreach (var key in dirtyKeys)
            {
                if (key == SaveKey) continue;
                if (lastSentTime.TryGetValue(key, out var t) && clock - t < NetConfig.PerKeyMinInterval)
                {
                    keep.Add(key);
                    continue;
                }
                string json = Game.CapturePlanetState(key);
                if (json == null) continue;
                ulong h = StableHash.Fnv1a64(json);
                lastSentTime[key] = clock;
                if (lastSentHash.TryGetValue(key, out var prev) && prev == h) continue; // 変化なし
                lastSentHash[key] = h;
                msg.entries.Add(new SaveEntry { key = key, json = json });
            }
            dirtyKeys.Clear();
            foreach (var k in keep) dirtyKeys.Add(k);
            if (msg.entries.Count > 0) BroadcastToGuests(NetMsg.StateDelta, msg);
        }

        // ------------------------------------------------------------
        // 接続・参加
        // ------------------------------------------------------------

        private void HostOnPeerConnected(string netId)
        {
            pendingPeers[netId] = clock;
        }

        private void HostOnPeerDisconnected(string netId, string reason)
        {
            pendingPeers.Remove(netId);
            var sp = FindPlayerByNet(netId);
            if (sp != null) RemoveGuest(sp, Loc.T("接続が切れました", "disconnected"));
        }

        private void HostHandle(string netId, NetMsg type, byte[] body)
        {
            if (type == NetMsg.Hello)
            {
                HandleHello(netId, NetCodec.Parse<HelloMsg>(body));
                return;
            }
            var sp = FindPlayerByNet(netId);
            if (sp == null) return; // 参加前のピアからのメッセージは受け付けない

            switch (type)
            {
                case NetMsg.GuestReady:
                    HostOnGuestReady(sp);
                    break;
                case NetMsg.Command:
                    HostHandleCommand(sp, NetCodec.Parse<CommandMsg>(body));
                    break;
                case NetMsg.TxAck:
                    HostHandleTxAck(sp, NetCodec.Parse<TxAckMsg>(body));
                    break;
                case NetMsg.ProfileUpdate:
                    HostHandleProfileUpdate(sp, NetCodec.Parse<ProfileUpdateMsg>(body));
                    break;
                case NetMsg.Gesture:
                {
                    var g = NetCodec.Parse<GestureMsg>(body);
                    if (g == null) break;
                    g.playerId = sp.PlayerId;
                    ShowRemoteGesture(sp.PlayerId, (PlayerGesture)g.gesture);
                    PublishGuestGesture(sp, (PlayerGesture)g.gesture);
                    BroadcastToGuests(NetMsg.Gesture, g, sp.PlayerId, true);
                    break;
                }
                case NetMsg.SleepRequest:
                {
                    var m = NetCodec.Parse<SleepRequestMsg>(body);
                    if (m != null) HostBeginSleepVote(sp.PlayerId, m.wakeHour);
                    break;
                }
                case NetMsg.SleepVote:
                {
                    var m = NetCodec.Parse<SleepVoteMsg>(body);
                    if (m != null) HostReceiveSleepVote(sp.PlayerId, m.voteId, m.agree);
                    break;
                }
                case NetMsg.DictionaryOffer:
                    HostRouteDictionaryOffer(sp, NetCodec.Parse<DictionaryOfferMsg>(body));
                    break;
                case NetMsg.DictionaryReply:
                    HostRouteDictionaryReply(sp, NetCodec.Parse<DictionaryReplyMsg>(body));
                    break;
                case NetMsg.TradeRequest:
                {
                    var m = NetCodec.Parse<TradeRequestMsg>(body);
                    if (m != null) HostTradeRequest(sp.PlayerId, m.toId);
                    break;
                }
                case NetMsg.TradeReply:
                {
                    var m = NetCodec.Parse<TradeReplyMsg>(body);
                    if (m != null) HostTradeReply(sp.PlayerId, m.tradeId, m.accept);
                    break;
                }
                case NetMsg.TradeOffer:
                {
                    var m = NetCodec.Parse<TradeOfferMsg>(body);
                    if (m != null) HostTradeOffer(sp.PlayerId, m.tradeId, m.items, m.money);
                    break;
                }
                case NetMsg.TradeReady:
                {
                    var m = NetCodec.Parse<TradeReadyMsg>(body);
                    if (m != null) HostTradeReady(sp.PlayerId, m.tradeId, m.version, m.ready);
                    break;
                }
                case NetMsg.TradeCancel:
                {
                    var m = NetCodec.Parse<TradeCancelMsg>(body);
                    if (m != null) HostTradeCancel(sp.PlayerId, m.tradeId);
                    break;
                }
                case NetMsg.TradeDeposit:
                {
                    var m = NetCodec.Parse<TradeDepositMsg>(body);
                    if (m != null) HostTradeDeposit(sp.PlayerId, m.tradeId, m.ok);
                    break;
                }
                case NetMsg.Leave:
                {
                    string netIdLeaving = sp.NetId;
                    RemoveGuest(sp, Loc.T("帰りました", "went home"));
                    transport?.DisconnectPeer(netIdLeaving);
                    break;
                }
            }
        }

        private void Reject(string netId, string ja, string en)
        {
            transport.Send(netId, NetCodec.Encode(NetMsg.JoinRejected, new JoinRejectedMsg { reasonJa = ja, reasonEn = en }), NetDelivery.Reliable);
            pendingPeers.Remove(netId);
            // 断りのメッセージが届くよう少し待ってから切る
            rejectedPeers[netId] = clock + 1.5f;
        }

        private readonly Dictionary<string, float> rejectedPeers = new Dictionary<string, float>();

        private void HandleHello(string netId, HelloMsg hello)
        {
            if (hello == null) return;
            if (!pendingPeers.ContainsKey(netId) && FindPlayerByNet(netId) != null) return; // 重複
            if (hello.protocol != NetConfig.ProtocolVersion)
            {
                Reject(netId, "ゲームのバージョンが違います", "Game version mismatch");
                return;
            }
            if (Game.Planet.worldGenVersion > hello.worldGenVersion || Game.Planet.languageGenVersion > hello.languageGenVersion)
            {
                Reject(netId, "相手のゲームがこの星の生成規則に対応していません（更新してください）", "Your game cannot generate this planet (please update)");
                return;
            }
            if (string.IsNullOrEmpty(hello.playerId))
            {
                Reject(netId, "プレイヤー情報がありません", "Missing player info");
                return;
            }
            if (hello.playerId == LocalId)
            {
                Reject(netId, "ホストと同じプレイヤーIDです（同じPCで試す時は起動引数 -uchujinTestSlot 2 を付けてください）",
                    "Same player ID as the host (use -uchujinTestSlot 2 when testing on one PC)");
                return;
            }
            // 同じプレイヤーが繋ぎ直した（古い接続が残っている）
            if (players.TryGetValue(hello.playerId, out var old) && !old.IsLocal)
            {
                string oldNet = old.NetId;
                RemoveGuest(old, Loc.T("繋ぎ直しました", "reconnected"));
                if (oldNet != null && oldNet != netId) transport.DisconnectPeer(oldNet);
            }
            int guests = 0;
            foreach (var p in players.Values) if (!p.IsLocal) guests++;
            if (guests >= NetConfig.MaxPlayers - 1)
            {
                Reject(netId, "この星は満員です", "This planet is full");
                return;
            }

            pendingPeers.Remove(netId);
            var known = FindKnownGuest(hello.playerId);
            var profile = ParseProfile(hello.profileJson);
            string name = !string.IsNullOrEmpty(hello.displayName) ? hello.displayName : (profile?.appearance?.displayName ?? Loc.T("旅人", "Traveler"));
            var perm = known != null ? (PermissionLevel)Mathf.Clamp(known.permission, 0, 2) : NetConfig.DefaultGuestPermission;
            var sp = new SessionPlayer
            {
                PlayerId = hello.playerId,
                DisplayName = name,
                Permission = perm,
                IsHost = false,
                IsLocal = false,
                NetId = netId,
                AppearanceJson = profile?.appearance != null ? JsonUtility.ToJson(profile.appearance) : null,
                Mirror = new GuestMirror()
            };
            sp.Mirror.SetSummary(profile ?? new PlayerState.ProfileData(), 0);
            players[sp.PlayerId] = sp;

            // ホスト側の PlayerState（コマンドの発行者・住民の認識・位置）
            var ps = Game.Players.AddRemote(sp.PlayerId);
            ps.Permission = perm;
            if (profile != null)
            {
                var copy = CloneProfile(profile);
                if (copy != null) ps.RestoreProfile(copy);
            }
            if (ps.Appearance != null && string.IsNullOrEmpty(ps.Appearance.displayName)) ps.Appearance.displayName = name;

            // 記録（再訪時の権限復元：マルチ §4.3）
            int day = Game.Time != null ? Game.Time.Day : 0;
            if (known == null)
            {
                known = new KnownGuest { playerId = sp.PlayerId, firstDay = day, permission = (int)perm };
                planetSave.guests.Add(known);
            }
            known.displayName = name;
            known.lastDay = day;
            known.visits++;

            // 1. 参加承認
            SendTo(sp, NetMsg.JoinAccepted, new JoinAcceptedMsg
            {
                hostPlayerId = LocalId,
                hostName = Game.LocalPlayer.DisplayName,
                assignedPlayerId = sp.PlayerId,
                permission = (int)perm,
                maxPlayers = NetConfig.MaxPlayers
            });
            // 2. 星の情報と現在のスナップショット（マルチ §20.3：シードだけでは今の世界は復元できない）
            SendSnapshot(sp);
            // 3. 参加者一覧
            BroadcastPlayerList();
            // 4. 未受領の Tx（前回の訪問で届かなかった付与など）を再送
            ResendOutbox(sp);

            EnsureAvatar(sp);
            EventBus.Publish(new PlayerJoinedEvent { PlayerId = sp.PlayerId, DisplayName = name });
            UIManager.Toast(Loc.Format("{0}さんが星にやってきました（{1}）", "{0} arrived ({1})", name, PermissionLabel(perm)));
        }

        private void SendSnapshot(SessionPlayer sp)
        {
            var entries = Game.CaptureAllPlanetEntries();
            entries.RemoveAll(e => e.key == SaveKey);
            var flags = Game.CapturePlanetState("core.flags");
            if (flags != null) entries.Add(new SaveEntry { key = "core.flags", json = flags });
            SendTo(sp, NetMsg.SnapshotBegin, new SnapshotBeginMsg { planet = Game.Planet, entryCount = entries.Count });
            var batch = new StateEntriesMsg();
            int bytes = 0;
            foreach (var e in entries)
            {
                batch.entries.Add(e);
                bytes += (e.json != null ? e.json.Length : 0) + 32;
                if (bytes >= SnapshotBatchBytes)
                {
                    SendTo(sp, NetMsg.SnapshotEntries, batch);
                    batch = new StateEntriesMsg();
                    bytes = 0;
                }
            }
            if (batch.entries.Count > 0) SendTo(sp, NetMsg.SnapshotEntries, batch);
            SendTo(sp, NetMsg.SnapshotEnd, new EmptyMsg());
            // スナップショット以降の変化は差分で届けるため、「前回送った内容」の記録を捨てて次の差分を必ず送る
            lastSentHash.Clear();
        }

        private void HostOnGuestReady(SessionPlayer sp)
        {
            sp.Ready = true;
            BroadcastPlayerList();
            EnsureAvatar(sp);
        }

        private void BroadcastPlayerList()
        {
            var msg = new PlayerListMsg();
            foreach (var p in players.Values)
            {
                if (p.IsLocal)
                {
                    msg.players.Add(LocalPlayerInfo(true));
                    continue;
                }
                msg.players.Add(new PlayerInfoDto
                {
                    playerId = p.PlayerId,
                    displayName = p.DisplayName,
                    permission = (int)p.Permission,
                    appearanceJson = p.AppearanceJson,
                    isHost = false
                });
            }
            BroadcastToGuests(NetMsg.PlayerList, msg);
        }

        /// <summary>ゲストを取り除く（切断・退出・追い出し）。品物はゲストの物なので星には残らない（マルチ §4.3）。</summary>
        private void RemoveGuest(SessionPlayer sp, string why)
        {
            if (sp == null || sp.IsLocal) return;
            if (!players.ContainsKey(sp.PlayerId)) return;
            AbortTradesWith(sp.PlayerId);
            SleepParticipantLeft(sp.PlayerId);
            string name = sp.DisplayName;
            RemoveSessionPlayer(sp.PlayerId, true);
            BroadcastPlayerList();
            UIManager.Toast(Loc.Format("{0}さん：{1}", "{0}: {1}", name, why));
        }

        // ------------------------------------------------------------
        // 権限（マルチ §6.2：観光・お手伝い・共同生活）
        // ------------------------------------------------------------

        public void SetPermission(string playerId, PermissionLevel level)
        {
            if (!IsHostSession) return;
            if (!players.TryGetValue(playerId, out var sp) || sp.IsLocal) return;
            if (level == PermissionLevel.Owner) level = PermissionLevel.CoLiving;
            sp.Permission = level;
            var ps = Game.Players.Get(playerId);
            if (ps != null) ps.Permission = level;
            var known = FindKnownGuest(playerId);
            if (known != null) known.permission = (int)level;
            BroadcastPlayerList();
        }

        public void KickPlayer(string playerId)
        {
            if (!IsHostSession || !players.TryGetValue(playerId, out var sp) || sp.IsLocal) return;
            SendTo(sp, NetMsg.Kick, new KickMsg { reasonJa = "ホストが退出させました", reasonEn = "The host removed you" });
            sp.KickAt = clock + 1f;
        }

        private KnownGuest FindKnownGuest(string playerId)
        {
            foreach (var g in planetSave.guests) if (g.playerId == playerId) return g;
            return null;
        }

        public IReadOnlyList<KnownGuest> KnownGuests => planetSave.guests;

        // ------------------------------------------------------------
        // コマンド（ゲスト → ホストで実行 → 結果を返す）
        // ------------------------------------------------------------

        private void HostHandleCommand(SessionPlayer sp, CommandMsg cmd)
        {
            if (cmd == null || string.IsNullOrEmpty(cmd.typeName)) return;
            // 同じ commandId の再送には実行せず前回の結果を返す（二重実行防止）
            if (sp.Commands.TryGet(cmd.commandId, out var cached))
            {
                SendTo(sp, NetMsg.CommandResult, cached);
                return;
            }
            CommandResult result;
            try
            {
                result = WorldAuthority.ExecuteRemote(cmd.typeName, cmd.json, sp.PlayerId);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                result = CommandResult.Fail("操作中にエラーが発生しました", "An error occurred");
            }
            var msg = new CommandResultMsg
            {
                commandId = cmd.commandId,
                success = result != null && result.Success,
                ja = result?.MessageJa,
                en = result?.MessageEn
            };
            sp.Commands.Add(cmd.commandId, msg);
            SendTo(sp, NetMsg.CommandResult, msg);
        }

        // ------------------------------------------------------------
        // ゲストの持ち物のミラー
        // ------------------------------------------------------------

        private void HostHandleProfileUpdate(SessionPlayer sp, ProfileUpdateMsg m)
        {
            if (m == null || sp.Mirror == null) return;
            var d = ParseProfile(m.profileJson);
            if (d == null) return;
            sp.Mirror.SetSummary(d, m.lastTxSeq);
            var ps = Game.Players.Get(sp.PlayerId);
            if (ps != null)
            {
                var copy = CloneProfile(d);
                if (copy != null) ps.RestoreProfile(copy); // イベントを出さずに置き換える
            }
            // 見た目が変わっていれば表示を更新
            if (d.appearance != null)
            {
                string aj = JsonUtility.ToJson(d.appearance);
                if (aj != sp.AppearanceJson)
                {
                    sp.AppearanceJson = aj;
                    if (sp.Avatar != null && ps != null) sp.Avatar.RebuildVisual(ps.Appearance);
                    BroadcastPlayerList();
                }
            }
        }

        private static PlayerState.ProfileData ParseProfile(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var d = JsonUtility.FromJson<PlayerState.ProfileData>(json);
                d?.inventory?.EnsureSlots();
                return d;
            }
            catch
            {
                return null;
            }
        }

        private static PlayerState.ProfileData CloneProfile(PlayerState.ProfileData d)
        {
            if (d == null) return null;
            return ParseProfile(JsonUtility.ToJson(d));
        }

        // ------------------------------------------------------------
        // Tx（付与・課金・取り去り）：取引ID付き・未受領は保存して再送
        // ------------------------------------------------------------

        private void QueueTx(string playerId, TxMsg tx)
        {
            if (tx == null || string.IsNullOrEmpty(playerId)) return;
            if (string.IsNullOrEmpty(tx.txId)) tx.txId = Ids.New();
            planetSave.outbox.Add(new OutboxEntry { playerId = playerId, tx = tx });
            if (players.TryGetValue(playerId, out var sp) && !sp.IsLocal && sp.NetId != null) SendTx(sp, tx);
        }

        private void SendTx(SessionPlayer sp, TxMsg tx)
        {
            tx.seq = ++sp.TxSeq;
            sp.Mirror?.Pending.Add(tx.Clone());
            SendTo(sp, NetMsg.Tx, tx);
        }

        private void ResendOutbox(SessionPlayer sp)
        {
            foreach (var e in planetSave.outbox)
                if (e.playerId == sp.PlayerId && e.tx != null) SendTx(sp, e.tx);
        }

        private void HostHandleTxAck(SessionPlayer sp, TxAckMsg ack)
        {
            if (ack == null) return;
            planetSave.outbox.RemoveAll(e => e.playerId == sp.PlayerId && e.tx != null && e.tx.txId == ack.txId);
            if (!ack.ok) Debug.LogWarning($"[Network] {sp.DisplayName} の持ち物への反映が一部できませんでした（tx {ack.txId}）");
        }

        // ------------------------------------------------------------
        // INetworkBridge（ホスト側で CommandContext から呼ばれる）
        // ------------------------------------------------------------

        public void SendCommandToHost(string typeName, string json, string commandId)
        {
            if (!IsGuestSession || transport == null || transport.HostPeerId == null)
            {
                WorldAuthority.CompletePending(commandId, CommandResult.Fail("ホストと繋がっていません", "Not connected to the host"));
                return;
            }
            SendToHost(NetMsg.Command, new CommandMsg { commandId = commandId, typeName = typeName, json = json });
        }

        public void MarkDirty(string stateKey)
        {
            if (string.IsNullOrEmpty(stateKey) || stateKey == SaveKey) return;
            if (Session != SessionState.Hosting) return;
            dirtyKeys.Add(stateKey);
        }

        public void GrantItemToRemote(string playerId, ItemStack stack, string source)
        {
            if (stack == null || stack.IsEmpty) return;
            QueueTx(playerId, new TxMsg { kind = TxKind.GrantItem, stack = stack.Clone(), reason = source });
        }

        public bool TakeItemFromRemote(string playerId, string itemId, int count, string reason)
        {
            if (count <= 0) return true;
            if (!players.TryGetValue(playerId, out var sp) || sp.Mirror == null) return false;
            if (sp.Mirror.EffectiveCount(itemId) < count) return false;
            QueueTx(playerId, new TxMsg { kind = TxKind.TakeItem, itemId = itemId, count = count, reason = reason });
            return true;
        }

        public bool RemoteHasItem(string playerId, string itemId, int count)
        {
            if (!players.TryGetValue(playerId, out var sp) || sp.Mirror == null) return false;
            return sp.Mirror.EffectiveCount(itemId) >= count;
        }

        public bool TryChargeRemote(string playerId, int amount, string reason)
        {
            if (amount <= 0) return true;
            if (!players.TryGetValue(playerId, out var sp) || sp.Mirror == null) return false;
            if (sp.Mirror.EffectiveMoney < amount) return false;
            QueueTx(playerId, new TxMsg { kind = TxKind.Charge, amount = amount, reason = reason });
            return true;
        }

        public void GrantMoneyToRemote(string playerId, int amount, string reason)
        {
            if (amount <= 0) return;
            QueueTx(playerId, new TxMsg { kind = TxKind.PayMoney, amount = amount, reason = reason });
        }

        public void NotifyRemote(string playerId, string ja, string en)
        {
            if (players.TryGetValue(playerId, out var sp) && !sp.IsLocal) SendTo(sp, NetMsg.Notify, new NotifyMsg { ja = ja, en = en });
        }

        /// <summary>UI 表示用：ゲストの持ち物の要約（ミラー）。</summary>
        internal int MirrorMoney(string playerId) => players.TryGetValue(playerId, out var sp) && sp.Mirror != null ? sp.Mirror.EffectiveMoney : 0;

        internal string DebugSummary()
        {
            var sb = new StringBuilder();
            sb.Append(Session).Append(' ');
            if (transport != null) sb.Append(transport.Kind).Append(' ').Append(transport.State);
            sb.Append(" outbox=").Append(planetSave.outbox.Count);
            return sb.ToString();
        }

        private void UpdateRejectedPeers()
        {
            if (rejectedPeers.Count == 0) return;
            List<string> done = null;
            foreach (var kv in rejectedPeers) if (clock >= kv.Value) (done ?? (done = new List<string>())).Add(kv.Key);
            if (done == null) return;
            foreach (var id in done)
            {
                rejectedPeers.Remove(id);
                transport?.DisconnectPeer(id);
            }
        }
    }
}
