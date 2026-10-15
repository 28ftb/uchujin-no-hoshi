using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    public enum GuestExit
    {
        None,   // 画面遷移しない（Teardown・終了処理の中から）
        Title,  // タイトルへ（ホスト終了時の基本案：マルチ §20.5）
        Home    // 自分の星（持ち物を保存したスロット）へ帰る
    }

    /// <summary>ゲスト側：友達の星へ行く・読み込み・状態の適用・Tx の適用と受領台帳・退出と持ち物の保存。</summary>
    public sealed partial class NetworkSystem
    {
        private NetProfileSave profileSave = new NetProfileSave();
        private HashSet<string> appliedTxSet;

        private string homeSlotId;
        private bool suppressTeardownShutdown;
        private bool guestBeginRequested;
        private bool closingGuest;
        private float joinStartedAt;
        private string hostPlayerId;
        private string hostName;
        private PermissionLevel assignedPermission = NetConfig.DefaultGuestPermission;
        private PlanetInfo snapshotPlanet;
        private List<SaveEntry> snapshotEntries = new List<SaveEntry>();
        private readonly List<SaveEntry> bufferedDeltas = new List<SaveEntry>();
        private readonly List<TxMsg> bufferedTx = new List<TxMsg>();
        private PlayerListMsg pendingPlayerList;
        private string pendingAbortJa;
        private string pendingAbortEn;
        private bool pendingAbort;
        private bool placeNearHostPending;

        private bool profileDirty;
        private float profileTimer;
        private float autosaveTimer;
        private int lastAppliedSeq;

        /// <summary>EOS のロビー一覧を見るためだけに作ったトランスポート（参加時にそのまま使う）。</summary>
        private INetTransport browseTransport;

        public string HostName => hostName;
        public string HomeSlotId => homeSlotId;

        // ------------------------------------------------------------
        // 参加を始める
        // ------------------------------------------------------------

        public void JoinUdp(string address, string slotFromTitle)
        {
            PlayerPrefs.SetString("uchujin.net.lastAddress", address ?? "");
            BeginJoin("udp", t => t.Connect(address), slotFromTitle, null);
        }

        public void JoinEosLobby(string lobbyId, string slotFromTitle)
        {
            BeginJoin("eos", t => (t as ILobbyTransport)?.JoinLobby(lobbyId), slotFromTitle, browseTransport);
        }

        public void JoinEosCode(string code, string slotFromTitle)
        {
            BeginJoin("eos", t => (t as ILobbyTransport)?.JoinByInviteCode(code), slotFromTitle, browseTransport);
        }

        /// <summary>EOS のロビー一覧を取得（ログインを含む）。</summary>
        public void RefreshEosLobbies(Action<List<LobbyListing>> onDone, Action<string> onError)
        {
            if (!TransportFactory.EosAvailable)
            {
                onError?.Invoke(Loc.T("EOS は未設定です", "EOS is not configured"));
                return;
            }
            if (browseTransport == null) browseTransport = TransportFactory.Create("eos");
            var lt = browseTransport as ILobbyTransport;
            if (lt == null)
            {
                onError?.Invoke("EOS");
                return;
            }
            lt.RefreshLobbies(onDone, onError);
        }

        /// <summary>
        /// 友達の星へ行く。プレイ中（ソロ）なら自分の星を保存してから接続し、自分のスロットを持ち物の保存先にする。
        /// タイトルからなら slotFromTitle（選んだセーブの主人公）を保存先にする。
        /// </summary>
        private void BeginJoin(string kind, Action<INetTransport> connect, string slotFromTitle, INetTransport reuse)
        {
            if (Game == null || Game.IsBusy) return;
            if (Session != SessionState.None || WorldAuthority.Role != NetRole.Offline)
            {
                UIManager.Toast(Loc.T("すでにマルチプレイ中です", "Already in a multiplayer session"));
                return;
            }
            if (Game.State == GameState.Playing)
            {
                if (Game.CurrentSlotId == null)
                {
                    UIManager.Toast(Loc.T("この状態では出かけられません", "You cannot travel right now"));
                    return;
                }
                Game.SaveGame(false);
                homeSlotId = Game.CurrentSlotId;
            }
            else
            {
                homeSlotId = slotFromTitle;
                if (string.IsNullOrEmpty(homeSlotId))
                {
                    UIManager.Message(Loc.T("友達の星へ行く", "Visit a friend"), Loc.T("先に自分のセーブ（主人公）を選んでください。", "Choose your save (character) first."));
                    return;
                }
            }
            var t = reuse != null && reuse.Kind == kind ? reuse : TransportFactory.Create(kind);
            if (t == null)
            {
                UIManager.Message(Loc.T("マルチプレイ", "Multiplayer"), Loc.T("EOS は未設定です（UCHUJIN_EOS が定義されていません）。", "EOS is not configured (UCHUJIN_EOS is not defined)."));
                return;
            }
            if (t == browseTransport) browseTransport = null;
            ShutdownTransport();
            AttachTransport(t);
            LastError = null;
            ResetGuestState();
            Session = SessionState.GuestConnecting;
            joinStartedAt = clock;
            try
            {
                connect(t);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                FailJoin(e.Message);
                return;
            }
            if (t.State == TransportState.Failed) FailJoin(t.StatusText);
        }

        private void ResetGuestState()
        {
            players.Clear();
            snapshotPlanet = null;
            snapshotEntries = new List<SaveEntry>();
            bufferedDeltas.Clear();
            bufferedTx.Clear();
            pendingPlayerList = null;
            pendingAbort = false;
            guestBeginRequested = false;
            suppressTeardownShutdown = false;
            lastAppliedSeq = 0;
            profileDirty = false;
            autosaveTimer = 0f;
            hostPlayerId = null;
            hostName = null;
        }

        private void GuestConnectingUpdate()
        {
            if (transport == null) return;
            if (transport.State == TransportState.Failed)
            {
                FailJoin(transport.StatusText);
                return;
            }
            if (clock - joinStartedAt > NetConfig.JoinTimeoutSeconds)
                FailJoin(Loc.T("時間内に星の情報が届きませんでした", "Timed out while receiving the planet"));
        }

        /// <summary>読み込み前の失敗：何も変えずに元の画面へ戻る。</summary>
        private void FailJoin(string reason)
        {
            ShutdownTransport();
            Session = SessionState.None;
            suppressTeardownShutdown = false;
            LastError = reason;
            players.Clear();
            if (!WorldAuthority.IsGuest) WorldAuthority.Bridge = null;
            UIManager.Message(Loc.T("友達の星に行けませんでした", "Could not reach your friend's planet"), reason ?? "");
        }

        private void GuestOnConnected(string netId)
        {
            if (Session != SessionState.GuestConnecting) return;
            var lp = Game.LocalPlayer;
            SendToHost(NetMsg.Hello, new HelloMsg
            {
                protocol = NetConfig.ProtocolVersion,
                playerId = LocalId,
                displayName = lp.DisplayName,
                profileJson = JsonUtility.ToJson(lp.CaptureProfile()),
                worldGenVersion = PlanetInfo.CurrentWorldGenVersion,
                languageGenVersion = PlanetInfo.CurrentLanguageGenVersion
            });
        }

        private void GuestOnHostDisconnected(string reason)
        {
            if (Session == SessionState.GuestConnecting || Session == SessionState.GuestAwaitingSnapshot || (Session == SessionState.GuestLoading && !guestBeginRequested))
            {
                FailJoin(Loc.T("接続が切れました", "Disconnected") + (string.IsNullOrEmpty(reason) ? "" : $" ({reason})"));
                return;
            }
            EndGuestSessionFromHost("接続が切れました。持ち物は自分のセーブに保存しました。", "Disconnected. Your belongings were saved to your own save.", false);
        }

        /// <summary>ホスト都合で終わる（切断・ホスト終了・追い出し）。読み込み中なら読み込み完了後に処理する。</summary>
        private void EndGuestSessionFromHost(string ja, string en, bool notifyHost)
        {
            if (Session == SessionState.GuestLoading && guestBeginRequested)
            {
                pendingAbort = true;
                pendingAbortJa = ja;
                pendingAbortEn = en;
                return;
            }
            GuestShutdown(ja, en, notifyHost, GuestExit.Title);
        }

        // ------------------------------------------------------------
        // 受信
        // ------------------------------------------------------------

        private void GuestHandle(NetMsg type, byte[] body)
        {
            switch (type)
            {
                case NetMsg.JoinAccepted:
                {
                    var m = NetCodec.Parse<JoinAcceptedMsg>(body);
                    if (m == null || Session != SessionState.GuestConnecting) return;
                    hostPlayerId = m.hostPlayerId;
                    hostName = m.hostName;
                    assignedPermission = (PermissionLevel)Mathf.Clamp(m.permission, 0, 2);
                    Session = SessionState.GuestAwaitingSnapshot;
                    joinStartedAt = clock;
                    return;
                }
                case NetMsg.JoinRejected:
                {
                    var m = NetCodec.Parse<JoinRejectedMsg>(body);
                    FailJoin(m != null ? Loc.T(m.reasonJa, m.reasonEn) : Loc.T("参加を断られました", "Join rejected"));
                    return;
                }
                case NetMsg.SnapshotBegin:
                {
                    var m = NetCodec.Parse<SnapshotBeginMsg>(body);
                    if (m == null || Session != SessionState.GuestAwaitingSnapshot) return;
                    snapshotPlanet = m.planet;
                    snapshotEntries = new List<SaveEntry>(Math.Max(4, m.entryCount));
                    return;
                }
                case NetMsg.SnapshotEntries:
                {
                    var m = NetCodec.Parse<StateEntriesMsg>(body);
                    if (m?.entries != null && Session == SessionState.GuestAwaitingSnapshot) snapshotEntries.AddRange(m.entries);
                    return;
                }
                case NetMsg.SnapshotEnd:
                    OnSnapshotComplete();
                    return;
                case NetMsg.StateDelta:
                {
                    var m = NetCodec.Parse<StateEntriesMsg>(body);
                    if (m?.entries == null) return;
                    if (Session == SessionState.GuestPlaying) ApplyStateEntries(m.entries);
                    else bufferedDeltas.AddRange(m.entries); // 読み込み中は溜めておき、読み込み後に順に適用
                    return;
                }
                case NetMsg.CommandResult:
                {
                    var m = NetCodec.Parse<CommandResultMsg>(body);
                    if (m == null) return;
                    var r = m.success ? CommandResult.Ok(m.ja, m.en) : CommandResult.Fail(m.ja, m.en);
                    WorldAuthority.CompletePending(m.commandId, r);
                    return;
                }
                case NetMsg.Tx:
                {
                    var m = NetCodec.Parse<TxMsg>(body);
                    if (m == null) return;
                    if (Session == SessionState.GuestPlaying) GuestApplyTx(m);
                    else bufferedTx.Add(m);
                    return;
                }
                case NetMsg.Notify:
                {
                    var m = NetCodec.Parse<NotifyMsg>(body);
                    if (m != null) UIManager.Toast(Loc.T(m.ja, m.en));
                    return;
                }
                case NetMsg.PlayerList:
                {
                    var m = NetCodec.Parse<PlayerListMsg>(body);
                    if (m == null) return;
                    if (Session == SessionState.GuestPlaying) ApplyPlayerList(m);
                    else pendingPlayerList = m;
                    return;
                }
                case NetMsg.Gesture:
                {
                    var m = NetCodec.Parse<GestureMsg>(body);
                    if (m != null && Session == SessionState.GuestPlaying) ShowRemoteGesture(m.playerId, (PlayerGesture)m.gesture);
                    return;
                }
                case NetMsg.Kick:
                {
                    var m = NetCodec.Parse<KickMsg>(body);
                    EndGuestSessionFromHost(m != null ? m.reasonJa : "退出させられました", m != null ? m.reasonEn : "You were removed", false);
                    return;
                }
                case NetMsg.HostClosing:
                    EndGuestSessionFromHost("ホストが星を閉じました。持ち物は自分のセーブに保存しました。", "The host closed the planet. Your belongings were saved.", false);
                    return;
                case NetMsg.SleepVoteStart:
                    GuestOnSleepVoteStart(NetCodec.Parse<SleepVoteStartMsg>(body));
                    return;
                case NetMsg.SleepResult:
                    OnSleepResult(NetCodec.Parse<SleepResultMsg>(body));
                    return;
                case NetMsg.DictionaryOffer:
                    OnDictionaryOfferReceived(NetCodec.Parse<DictionaryOfferMsg>(body));
                    return;
                case NetMsg.DictionaryReply:
                    OnDictionaryReplyReceived(NetCodec.Parse<DictionaryReplyMsg>(body));
                    return;
                case NetMsg.TradeRequest:
                    OnTradeInvite(NetCodec.Parse<TradeRequestMsg>(body));
                    return;
                case NetMsg.TradeState:
                    OnTradeState(NetCodec.Parse<TradeStateMsg>(body));
                    return;
                case NetMsg.TradeDepositRequest:
                    GuestOnDepositRequest(NetCodec.Parse<TradeDepositRequestMsg>(body));
                    return;
            }
        }

        private void OnSnapshotComplete()
        {
            if (Session != SessionState.GuestAwaitingSnapshot || snapshotPlanet == null) return;
            if (snapshotPlanet.worldGenVersion > PlanetInfo.CurrentWorldGenVersion || snapshotPlanet.languageGenVersion > PlanetInfo.CurrentLanguageGenVersion)
            {
                SendToHost(NetMsg.Leave, new LeaveMsg { reason = "version" });
                FailJoin(Loc.T("この星の生成規則に対応していません。ゲームを更新してください。", "Your game cannot generate this planet. Please update."));
                return;
            }
            Session = SessionState.GuestLoading;
            TryBeginGuestSession();
        }

        private void TryBeginGuestSession()
        {
            if (guestBeginRequested || Session != SessionState.GuestLoading) return;
            if (Game.IsBusy) return; // 次のフレームで再試行
            // 自分の星から出かける場合、直前の状態をもう一度保存する
            if (Game.State == GameState.Playing && WorldAuthority.Role == NetRole.Offline && Game.CurrentSlotId != null) Game.SaveGame(false);

            FlagStore flags = null;
            var entries = new List<SaveEntry>();
            foreach (var e in snapshotEntries)
            {
                if (e == null) continue;
                if (e.key == "core.flags")
                {
                    try { flags = JsonUtility.FromJson<FlagStore>(e.json); } catch { }
                    continue;
                }
                if (e.key == SaveKey) continue;
                entries.Add(e);
            }
            guestBeginRequested = true;
            suppressTeardownShutdown = true; // 自分の星の Teardown で通信を切らない
            WorldAuthority.Bridge = this;
            Game.LocalPlayer.Permission = assignedPermission;
            Game.BeginGuestSession(snapshotPlanet, entries, flags, OnGuestSessionReady);
        }

        private void OnGuestSessionReady()
        {
            suppressTeardownShutdown = false;
            snapshotEntries = new List<SaveEntry>();
            Session = SessionState.GuestPlaying;
            WorldAuthority.Bridge = this;
            Game.LocalPlayer.Permission = assignedPermission;
            if (pendingAbort)
            {
                pendingAbort = false;
                GuestShutdown(pendingAbortJa, pendingAbortEn, false, GuestExit.Title);
                return;
            }
            if (bufferedDeltas.Count > 0)
            {
                ApplyStateEntries(bufferedDeltas);
                bufferedDeltas.Clear();
            }
            if (pendingPlayerList != null)
            {
                ApplyPlayerList(pendingPlayerList);
                pendingPlayerList = null;
            }
            foreach (var tx in bufferedTx) GuestApplyTx(tx);
            bufferedTx.Clear();
            Subscribe();
            UIManager.RegisterOverlay(overlay);
            RebuildAllAvatars();
            placeNearHostPending = true;
            SendToHost(NetMsg.GuestReady, new EmptyMsg());
            SendProfileUpdate();
            UIManager.Toast(Loc.Format("{0}さんの星にやってきました（{1}）", "You arrived at {0}'s planet ({1})",
                hostName ?? "?", PermissionLabel(assignedPermission)));
        }

        private void ApplyStateEntries(List<SaveEntry> entries)
        {
            foreach (var e in entries)
            {
                if (e == null || e.key == SaveKey || string.IsNullOrEmpty(e.json)) continue;
                Game.ApplyRemoteState(e.key, e.json);
            }
        }

        private void ApplyPlayerList(PlayerListMsg m)
        {
            var present = new HashSet<string>();
            foreach (var dto in m.players)
            {
                if (dto == null || string.IsNullOrEmpty(dto.playerId)) continue;
                present.Add(dto.playerId);
                bool isNew = !players.ContainsKey(dto.playerId);
                if (dto.playerId == LocalId)
                {
                    var newPerm = (PermissionLevel)Mathf.Clamp(dto.permission, 0, 2);
                    if (newPerm != assignedPermission && Session == SessionState.GuestPlaying)
                        UIManager.Toast(Loc.Format("権限が「{0}」になりました", "Your permission is now \"{0}\"", PermissionLabel(newPerm)));
                    assignedPermission = newPerm;
                    Game.LocalPlayer.Permission = newPerm;
                }
                AddOrUpdateSessionPlayer(dto);
                if (isNew && dto.playerId != LocalId)
                {
                    EventBus.Publish(new PlayerJoinedEvent { PlayerId = dto.playerId, DisplayName = dto.displayName });
                    if (!dto.isHost) UIManager.Toast(Loc.Format("{0}さんが来ました", "{0} arrived", dto.displayName));
                }
            }
            foreach (var id in new List<string>(players.Keys))
            {
                if (present.Contains(id) || id == LocalId) continue;
                string name = players[id].DisplayName;
                RemoveSessionPlayer(id, true);
                UIManager.Toast(Loc.Format("{0}さんが帰りました", "{0} left", name));
            }
        }

        /// <summary>着いた直後、ホストが屋外にいればその近くに立つ（迷子にならないように）。</summary>
        internal void MaybePlaceNearHost(SessionPlayer sp, NetCodec.TransformSample s)
        {
            if (!placeNearHostPending || !sp.IsHost) return;
            placeNearHostPending = false;
            var lp = Game.LocalPlayer;
            if (lp == null || s.AreaId != AreaIds.Overworld || lp.CurrentAreaId != AreaIds.Overworld) return;
            var offset = Quaternion.Euler(0f, s.RotY, 0f) * new Vector3(1.6f, 0f, 1.2f);
            var layout = Game.Layout;
            var pos = s.Position + offset;
            if (layout != null) pos = layout.OnGround(pos) + Vector3.up * 0.2f;
            lp.Position = pos;
        }

        // ------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------

        private void GuestUpdate(float dt)
        {
            if (profileDirty)
            {
                profileTimer += dt;
                if (profileTimer >= NetConfig.ProfileUpdateDebounce) SendProfileUpdate();
            }
            autosaveTimer += dt;
            if (autosaveTimer >= NetConfig.GuestProfileAutosaveSeconds)
            {
                autosaveTimer = 0f;
                WriteHomeProfile();
            }
        }

        private void MarkProfileDirty()
        {
            if (Session != SessionState.GuestPlaying) return;
            if (!profileDirty) profileTimer = 0f;
            profileDirty = true;
        }

        private void SendProfileUpdate()
        {
            profileDirty = false;
            profileTimer = 0f;
            if (Session != SessionState.GuestPlaying) return;
            SendToHost(NetMsg.ProfileUpdate, new ProfileUpdateMsg
            {
                profileJson = JsonUtility.ToJson(Game.LocalPlayer.CaptureProfile()),
                lastTxSeq = lastAppliedSeq
            });
        }

        // ------------------------------------------------------------
        // Tx の適用（取引ID付き：確定済みの再送は二重受領しない。マルチ §20.5）
        // ------------------------------------------------------------

        private bool IsTxApplied(string txId)
        {
            if (appliedTxSet == null) appliedTxSet = new HashSet<string>(profileSave.appliedTxIds);
            return appliedTxSet.Contains(txId);
        }

        private void RecordTxApplied(string txId)
        {
            if (appliedTxSet == null) appliedTxSet = new HashSet<string>(profileSave.appliedTxIds);
            if (appliedTxSet.Add(txId)) profileSave.appliedTxIds.Add(txId);
            TrimLedger();
        }

        private void TrimLedger()
        {
            int over = profileSave.appliedTxIds.Count - NetConfig.LedgerSize;
            if (over > 0)
            {
                profileSave.appliedTxIds.RemoveRange(0, over);
                appliedTxSet = null;
            }
            if (profileSave.rolledBackTrades.Count > 200) profileSave.rolledBackTrades.RemoveRange(0, profileSave.rolledBackTrades.Count - 200);
        }

        private void GuestApplyTx(TxMsg tx)
        {
            if (tx == null || string.IsNullOrEmpty(tx.txId)) return;
            if (tx.seq > lastAppliedSeq) lastAppliedSeq = tx.seq;
            bool ok = true;
            if (!IsTxApplied(tx.txId))
            {
                try { ok = ApplyTxToLocal(tx); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    ok = false;
                }
                RecordTxApplied(tx.txId);
            }
            SendToHost(NetMsg.TxAck, new TxAckMsg { txId = tx.txId, ok = ok });
            SendProfileUpdate();
        }

        private bool ApplyTxToLocal(TxMsg tx)
        {
            var lp = Game.LocalPlayer;
            switch (tx.kind)
            {
                case TxKind.GrantItem:
                    if (tx.stack == null || tx.stack.IsEmpty) return true;
                    lp.GiveOrDrop(tx.stack.Clone(), tx.reason ?? "net");
                    return true;
                case TxKind.TakeItem:
                    return lp.Inventory.Remove(tx.itemId, tx.count, 0, tx.reason) == tx.count;
                case TxKind.Charge:
                    return lp.Wallet.TrySpend(tx.amount, tx.reason);
                case TxKind.PayMoney:
                    lp.Wallet.Add(tx.amount, tx.reason);
                    return true;
                case TxKind.TradeCommit:
                    return ApplyTradeCommitLocal(tx.tradeId, tx.give, tx.giveMoney, tx.take, tx.takeMoney, tx.partnerName);
                case TxKind.TradeAbort:
                    ApplyTradeAbortLocal(tx.tradeId);
                    return true;
            }
            return true;
        }

        // ------------------------------------------------------------
        // 退出
        // ------------------------------------------------------------

        /// <summary>ゲストが自分から帰る（マルチメニューの「退出」）。</summary>
        public void LeaveAsGuest(bool goHome)
        {
            if (!IsGuestSession) return;
            if (Session != SessionState.GuestPlaying)
            {
                SendToHost(NetMsg.Leave, new LeaveMsg { reason = "cancel" });
                FailJoinSilently();
                return;
            }
            GuestShutdown(null, null, true, goHome ? GuestExit.Home : GuestExit.Title);
        }

        /// <summary>接続中の取りやめ（読み込み前）。</summary>
        public void CancelJoin()
        {
            if (Session == SessionState.GuestConnecting || Session == SessionState.GuestAwaitingSnapshot)
            {
                SendToHost(NetMsg.Leave, new LeaveMsg { reason = "cancel" });
                FailJoinSilently();
            }
        }

        private void FailJoinSilently()
        {
            ShutdownTransport();
            Session = SessionState.None;
            players.Clear();
            if (!WorldAuthority.IsGuest) WorldAuthority.Bridge = null;
        }

        /// <summary>
        /// ゲストの終了処理：訪問先の知識を保存 → 交換の預け物を巻き戻し → 持ち物を自分のスロットへ保存 → 切断 → 画面遷移。
        /// </summary>
        private void GuestShutdown(string messageJa, string messageEn, bool notifyHost, GuestExit exit)
        {
            if (closingGuest) return;
            closingGuest = true;
            bool loaded = Session == SessionState.GuestPlaying || (Session == SessionState.GuestLoading && guestBeginRequested);
            try
            {
                // 1. 訪問先での自分の知識（辞書など）は訪問先ごとに保存（Role がまだ Guest のうちに）
                if (loaded && WorldAuthority.IsGuest && Game.Planet != null) Game.SaveGame(false);
                // 2. 交換の途中なら不成立として預け物を戻す（マルチ §20.5）
                CloseLocalTradeUI();
                RollbackAllEscrows();
                // 3. 持ち物を自分のスロットへ
                if (loaded) WriteHomeProfile();
                // 4. 退出を伝えて切断
                if (notifyHost) SendToHost(NetMsg.Leave, new LeaveMsg { reason = "leave" });
                CloseSleepVoteUI();
                ShutdownTransport();
                ClearSessionPlayers(true);
                Session = SessionState.None;
                suppressTeardownShutdown = false;
                guestBeginRequested = false;
                WorldAuthority.Bridge = null;
                if (Game.LocalPlayer != null) Game.LocalPlayer.Permission = PermissionLevel.Owner;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                closingGuest = false;
            }

            if (exit == GuestExit.None) return;
            if (exit == GuestExit.Home && !string.IsNullOrEmpty(homeSlotId) && Game.Saves.SlotExists(homeSlotId))
            {
                Game.LoadSlot(homeSlotId);
            }
            else
            {
                Game.ReturnToTitle(false);
            }
            if (!string.IsNullOrEmpty(messageJa)) UIManager.Message(Loc.T("マルチプレイ", "Multiplayer"), Loc.T(messageJa, messageEn));
        }

        /// <summary>持ち物（PlayerProfile）を自分のスロットへ保存する。そのスロットの星の知識は残す。</summary>
        private void WriteHomeProfile()
        {
            if (string.IsNullOrEmpty(homeSlotId) || Game == null || Game.LocalPlayer == null) return;
            try
            {
                var file = Game.Saves.ReadPlayer(homeSlotId, LocalId) ?? new PlayerSaveFile { playerId = LocalId };
                file.playerId = LocalId;
                file.profile = Game.CaptureLocalProfileEntries();
                Game.Saves.WritePlayer(homeSlotId, file);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                UIManager.Toast(Loc.T("持ち物の保存に失敗しました", "Failed to save your belongings"));
            }
        }

        /// <summary>タイトルから参加する時：選んだセーブの主人公（持ち物・見た目）を読み込む。</summary>
        public bool LoadProfileFromSlot(string slotId)
        {
            if (Game == null || string.IsNullOrEmpty(slotId)) return false;
            try
            {
                var file = Game.Saves.ReadPlayer(slotId, LocalId);
                if (file == null || file.profile == null) return false;
                Game.RestoreLocalProfileEntries(file.profile);
                RollbackStaleEscrows(); // 前回の異常終了で残った交換の預け物を戻す
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }
    }
}
