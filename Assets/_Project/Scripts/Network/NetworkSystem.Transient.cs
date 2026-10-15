using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Network
{
    /// <summary>
    /// 一時メッセージ（保存しない通知：発話の中継・個人の記録など）。INetworkBridge.BroadcastTransient / SendTransientTo の実装。
    ///
    /// ・ホスト → 全ゲスト：BroadcastTransient（参加済みで接続中の全ゲスト。読み込み中のゲストにも送り、ゲスト側で溜める）。
    /// ・ホスト → 特定のゲスト：SendTransientTo。
    /// ・ゲスト → ホスト：BroadcastTransient、または SendTransientTo(ホストのID)。別のゲスト宛てはホストが中継する。
    /// ・送信元IDは受信側のホストが接続から決める（ゲストの申告は使わない＝なりすまし防止）。ゲストはホストの付けたIDを使う。
    /// ・大きな JSON も NetCodec（圧縮）と各トランスポートの分割でそのまま送れる。順序は reliable の到着順。
    /// ・ゲストの読み込み中に届いた物は全件（上限 NetConfig.MaxBufferedTransients）を溜め、読み込み完了後に届いた順で処理する。
    /// </summary>
    public sealed partial class NetworkSystem
    {
        /// <summary>ゲスト：読み込み中に届いた一時メッセージ（届いた順）。</summary>
        private readonly List<TransientMsg> bufferedTransients = new List<TransientMsg>();

        // ------------------------------------------------------------
        // INetworkBridge
        // ------------------------------------------------------------

        public void BroadcastTransient(string channel, string json)
        {
            if (string.IsNullOrEmpty(channel) || transport == null) return;
            if (IsHostSession)
            {
                BroadcastToGuests(NetMsg.Transient, new TransientMsg { channel = channel, fromPlayerId = LocalId, json = json });
            }
            else if (Session == SessionState.GuestPlaying)
            {
                SendToHost(NetMsg.Transient, new TransientMsg { channel = channel, fromPlayerId = LocalId, json = json });
            }
        }

        public void SendTransientTo(string playerId, string channel, string json)
        {
            if (string.IsNullOrEmpty(channel) || string.IsNullOrEmpty(playerId)) return;
            if (playerId == LocalId)
            {
                WorldAuthority.DispatchTransient(channel, LocalId, json);
                return;
            }
            if (transport == null) return;
            if (IsHostSession)
            {
                if (players.TryGetValue(playerId, out var sp) && !sp.IsLocal)
                    SendTo(sp, NetMsg.Transient, new TransientMsg { channel = channel, fromPlayerId = LocalId, toPlayerId = playerId, json = json });
            }
            else if (Session == SessionState.GuestPlaying)
            {
                // ホスト宛ては toPlayerId を空にする（ホスト側で「自分宛て」として処理）。それ以外はホストが中継する。
                string to = playerId == hostPlayerId ? null : playerId;
                SendToHost(NetMsg.Transient, new TransientMsg { channel = channel, fromPlayerId = LocalId, toPlayerId = to, json = json });
            }
        }

        // ------------------------------------------------------------
        // 受信
        // ------------------------------------------------------------

        /// <summary>ホスト：ゲストから届いた一時メッセージ。送信元は接続から決める。</summary>
        private void HostHandleTransient(SessionPlayer sender, TransientMsg m)
        {
            if (m == null || sender == null || string.IsNullOrEmpty(m.channel)) return;
            m.fromPlayerId = sender.PlayerId; // なりすまし防止：申告された送信元は使わない
            string to = m.toPlayerId;
            if (string.IsNullOrEmpty(to) || to == LocalId)
            {
                WorldAuthority.DispatchTransient(m.channel, sender.PlayerId, m.json);
                return;
            }
            if (to == sender.PlayerId) return; // 自分宛て（送り返さない）
            // 別のゲスト宛て：中継
            if (players.TryGetValue(to, out var target) && !target.IsLocal) SendTo(target, NetMsg.Transient, m);
        }

        /// <summary>ゲスト：ホストから届いた一時メッセージ。読み込み中は溜める。</summary>
        private void GuestHandleTransient(TransientMsg m)
        {
            if (m == null || string.IsNullOrEmpty(m.channel)) return;
            if (string.IsNullOrEmpty(m.fromPlayerId)) m.fromPlayerId = hostPlayerId;
            if (Session == SessionState.GuestPlaying)
            {
                DispatchTransientSafe(m);
                return;
            }
            bufferedTransients.Add(m);
            int over = bufferedTransients.Count - NetConfig.MaxBufferedTransients;
            if (over > 0)
            {
                bufferedTransients.RemoveRange(0, over);
                Debug.LogWarning($"[Network] 読み込み中の一時メッセージが上限を超えたため古い {over} 件を捨てました");
            }
        }

        /// <summary>ゲスト：読み込み完了後、溜めた一時メッセージを届いた順に処理する。</summary>
        private void FlushBufferedTransients()
        {
            if (bufferedTransients.Count == 0) return;
            var list = new List<TransientMsg>(bufferedTransients);
            bufferedTransients.Clear();
            foreach (var m in list) DispatchTransientSafe(m);
        }

        private static void DispatchTransientSafe(TransientMsg m)
        {
            // DispatchTransient はハンドラの例外を握るが、念のため
            try { WorldAuthority.DispatchTransient(m.channel, m.fromPlayerId, m.json); }
            catch (System.Exception e) { Debug.LogException(e); }
        }
    }
}
