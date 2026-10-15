using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// プレイヤー同士の直接交換（マルチ §12.2：直接交換・直接販売・プレゼント）。
    ///
    /// 流れ（ホストが取引を確定する。取引IDで管理：マルチ §20.5）：
    ///  1. 申し込み → 相手が受ける → 双方が品物・お金を出す（提示が変わるたびに同意は外れる）
    ///  2. 双方が「これでOK」→ ゲストは自分の出した物を持ち物から外して預ける（自分の台帳に記録）
    ///  3. 全員の預けが揃ったら確定：ホストが TradeCommit の Tx（取引ID付き・未受領は保存して再送）を送る
    ///  4. 途中でやめた・時間切れ・切断 → 不成立：預けた物は戻る（TradeAbort）
    ///  ・ゲストは切断・退出時に、確定の知らせが来ていない預け物を自分で戻す（巻き戻し）。
    ///    その後で確定の知らせが届いた場合は、出した物を改めて外してから受け取る（遅れた確定）。
    ///  ・ホスト自身は預けず、確定の瞬間に自分の持ち物から外す（ホストの終了で品物が宙に浮かないように）。
    /// 委託販売は Shop モジュール（棚の ownerId）が担当。
    /// </summary>
    public sealed partial class NetworkSystem
    {
        private sealed class HostTrade
        {
            public string TradeId;
            public string AId;
            public string BId;
            public TradePhase Phase;
            public List<ItemStack> AItems = new List<ItemStack>();
            public List<ItemStack> BItems = new List<ItemStack>();
            public int AMoney;
            public int BMoney;
            public bool AReady;
            public bool BReady;
            public int Version;
            public bool ADepositRequested;
            public bool BDepositRequested;
            public bool ADeposited;
            public bool BDeposited;
            public float Deadline;
        }

        private readonly Dictionary<string, HostTrade> trades = new Dictionary<string, HostTrade>();
        private TradeStateMsg localTrade;
        private TradeWindow tradeWindow;

        /// <summary>自分が関わっている交換の最新状態（UI 用）。</summary>
        public TradeStateMsg CurrentTrade => localTrade;

        // ------------------------------------------------------------
        // 自分の操作（ホストなら直接、ゲストならホストへ送る）
        // ------------------------------------------------------------

        public void RequestTrade(string partnerId)
        {
            if (!IsOnline || string.IsNullOrEmpty(partnerId) || partnerId == LocalId) return;
            if (localTrade != null && localTrade.phase != TradePhase.Committed && localTrade.phase != TradePhase.Aborted)
            {
                UIManager.Toast(Loc.T("ほかの交換の途中です", "You are already trading"));
                return;
            }
            if (IsHostSession) HostTradeRequest(LocalId, partnerId);
            else SendToHost(NetMsg.TradeRequest, new TradeRequestMsg { fromId = LocalId, toId = partnerId });
        }

        public void TradeSetOffer(string tradeId, List<ItemStack> items, int money)
        {
            var merged = InventoryUtil.Merge(items);
            if (IsHostSession) HostTradeOffer(LocalId, tradeId, merged, money);
            else SendToHost(NetMsg.TradeOffer, new TradeOfferMsg { tradeId = tradeId, fromId = LocalId, items = merged, money = money });
        }

        public void TradeSetReady(string tradeId, int version, bool ready)
        {
            if (IsHostSession) HostTradeReady(LocalId, tradeId, version, ready);
            else SendToHost(NetMsg.TradeReady, new TradeReadyMsg { tradeId = tradeId, fromId = LocalId, version = version, ready = ready });
        }

        public void TradeCancel(string tradeId)
        {
            if (IsHostSession) HostTradeCancel(LocalId, tradeId);
            else SendToHost(NetMsg.TradeCancel, new TradeCancelMsg { tradeId = tradeId, fromId = LocalId });
        }

        private void ReplyTradeInvite(string tradeId, bool accept)
        {
            if (IsHostSession) HostTradeReply(LocalId, tradeId, accept);
            else SendToHost(NetMsg.TradeReply, new TradeReplyMsg { tradeId = tradeId, fromId = LocalId, accept = accept });
        }

        // ------------------------------------------------------------
        // ホスト：取引の管理
        // ------------------------------------------------------------

        private HostTrade ActiveTradeOf(string playerId)
        {
            foreach (var t in trades.Values)
                if ((t.AId == playerId || t.BId == playerId) && t.Phase != TradePhase.Committed && t.Phase != TradePhase.Aborted) return t;
            return null;
        }

        private void HostTradeRequest(string fromId, string toId)
        {
            if (!players.TryGetValue(fromId, out var from) || !players.TryGetValue(toId, out var to) || fromId == toId)
            {
                TellPlayer(fromId, "相手が見つかりません", "That player is not here");
                return;
            }
            if (!from.IsLocal && !from.Ready || !to.IsLocal && !to.Ready)
            {
                TellPlayer(fromId, "相手はまだ星に着いていません", "That player has not arrived yet");
                return;
            }
            if (ActiveTradeOf(fromId) != null || ActiveTradeOf(toId) != null)
            {
                TellPlayer(fromId, "どちらかがほかの交換の途中です", "One of you is already trading");
                return;
            }
            var t = new HostTrade
            {
                TradeId = Ids.New(),
                AId = fromId,
                BId = toId,
                Phase = TradePhase.Inviting,
                Deadline = clock + NetConfig.TradeRequestTimeout
            };
            trades[t.TradeId] = t;
            var invite = new TradeRequestMsg { tradeId = t.TradeId, fromId = fromId, fromName = GetPlayerName(fromId), toId = toId };
            if (to.IsLocal) OnTradeInvite(invite);
            else SendTo(to, NetMsg.TradeRequest, invite);
            SendTradeStateTo(t, fromId, "返事を待っています…", "Waiting for an answer...");
        }

        private void HostTradeReply(string fromId, string tradeId, bool accept)
        {
            if (tradeId == null || !trades.TryGetValue(tradeId, out var t) || t.BId != fromId || t.Phase != TradePhase.Inviting) return;
            if (!accept)
            {
                AbortTrade(t, string.Format("{0}さんは交換を断りました", GetPlayerName(fromId)), string.Format("{0} declined", GetPlayerName(fromId)));
                return;
            }
            t.Phase = TradePhase.Negotiating;
            t.Deadline = float.MaxValue;
            BroadcastTradeState(t, null, null);
        }

        private void HostTradeOffer(string fromId, string tradeId, List<ItemStack> items, int money)
        {
            if (tradeId == null || !trades.TryGetValue(tradeId, out var t) || t.Phase != TradePhase.Negotiating) return;
            if (fromId != t.AId && fromId != t.BId) return;
            // 持っている範囲に収める（ゲストはミラー、ホストは実際の持ち物で確認）
            var clean = new List<ItemStack>();
            foreach (var s in InventoryUtil.Merge(items))
            {
                if (s == null || s.IsEmpty) continue;
                int have = AvailableExact(fromId, s);
                int n = Mathf.Min(s.count, have);
                if (n > 0) clean.Add(s.CloneWithCount(n));
            }
            money = Mathf.Clamp(money, 0, AvailableMoney(fromId));
            if (fromId == t.AId)
            {
                t.AItems = clean;
                t.AMoney = money;
            }
            else
            {
                t.BItems = clean;
                t.BMoney = money;
            }
            t.Version++;
            t.AReady = t.BReady = false;
            BroadcastTradeState(t, null, null);
        }

        private void HostTradeReady(string fromId, string tradeId, int version, bool ready)
        {
            if (tradeId == null || !trades.TryGetValue(tradeId, out var t) || t.Phase != TradePhase.Negotiating) return;
            if (version != t.Version) return; // 古い提示への同意は無効
            if (fromId == t.AId) t.AReady = ready;
            else if (fromId == t.BId) t.BReady = ready;
            else return;
            bool empty = t.AItems.Count == 0 && t.AMoney == 0 && t.BItems.Count == 0 && t.BMoney == 0;
            if (t.AReady && t.BReady && !empty) StartDeposit(t);
            else BroadcastTradeState(t, null, null);
        }

        private void HostTradeCancel(string fromId, string tradeId)
        {
            if (tradeId == null || !trades.TryGetValue(tradeId, out var t)) return;
            if (fromId != t.AId && fromId != t.BId) return;
            if (t.Phase == TradePhase.Depositing || t.Phase == TradePhase.Committed || t.Phase == TradePhase.Aborted) return;
            AbortTrade(t, string.Format("{0}さんが交換をやめました", GetPlayerName(fromId)), string.Format("{0} cancelled the trade", GetPlayerName(fromId)));
        }

        private void StartDeposit(HostTrade t)
        {
            t.Phase = TradePhase.Depositing;
            t.Deadline = clock + NetConfig.TradeDepositTimeout;
            // ゲストには預けてもらう。ホスト自身は確定の瞬間に外す。
            RequestDeposit(t, t.AId, t.AItems, t.AMoney, true);
            RequestDeposit(t, t.BId, t.BItems, t.BMoney, false);
            BroadcastTradeState(t, "受け渡し中…", "Exchanging...");
            TryCommit(t);
        }

        private void RequestDeposit(HostTrade t, string partyId, List<ItemStack> items, int money, bool isA)
        {
            if (partyId == LocalId)
            {
                if (isA) t.ADeposited = true;
                else t.BDeposited = true;
                return;
            }
            if (!players.TryGetValue(partyId, out var sp)) return;
            if (isA) t.ADepositRequested = true;
            else t.BDepositRequested = true;
            SendTo(sp, NetMsg.TradeDepositRequest, new TradeDepositRequestMsg { tradeId = t.TradeId, items = CloneList(items), money = money });
        }

        private void HostTradeDeposit(string fromId, string tradeId, bool ok)
        {
            if (tradeId == null || !trades.TryGetValue(tradeId, out var t) || t.Phase != TradePhase.Depositing) return;
            if (!ok)
            {
                AbortTrade(t, string.Format("{0}さんの持ち物が足りませんでした", GetPlayerName(fromId)), string.Format("{0} no longer has the items", GetPlayerName(fromId)));
                return;
            }
            if (fromId == t.AId) t.ADeposited = true;
            else if (fromId == t.BId) t.BDeposited = true;
            TryCommit(t);
        }

        private void TryCommit(HostTrade t)
        {
            if (t.Phase != TradePhase.Depositing || !t.ADeposited || !t.BDeposited) return;
            // 確定の直前に、両者がまだ星にいることを確認（いなければ不成立。預けた物は戻る）
            if (!players.ContainsKey(t.AId) || !players.ContainsKey(t.BId))
            {
                AbortTrade(t, "相手がいなくなりました", "The other player left");
                return;
            }
            // ホストが当事者なら、ここで自分の持ち物から外す（足りなければ不成立）
            if (t.AId == LocalId || t.BId == LocalId)
            {
                bool hostIsA = t.AId == LocalId;
                var mine = hostIsA ? t.AItems : t.BItems;
                int myMoney = hostIsA ? t.AMoney : t.BMoney;
                var lp = Game.LocalPlayer;
                foreach (var s in mine)
                {
                    if (InventoryUtil.CountExact(lp.Inventory, s) < s.count)
                    {
                        AbortTrade(t, "ホストの持ち物が足りませんでした", "The host no longer has the items");
                        return;
                    }
                }
                if (lp.Wallet.money < myMoney)
                {
                    AbortTrade(t, "ホストのお金が足りませんでした", "The host does not have enough money");
                    return;
                }
                foreach (var s in mine) InventoryUtil.RemoveExact(lp.Inventory, s, s.count, "trade:" + t.TradeId);
                if (myMoney > 0) lp.Wallet.TrySpend(myMoney, "trade:" + t.TradeId);
            }

            t.Phase = TradePhase.Committed;
            DeliverTradeResult(t, t.AId, t.BItems, t.BMoney, t.AItems, t.AMoney, GetPlayerName(t.BId));
            DeliverTradeResult(t, t.BId, t.AItems, t.AMoney, t.BItems, t.BMoney, GetPlayerName(t.AId));
            BroadcastTradeState(t, "交換が成立しました！", "Trade complete!");
            trades.Remove(t.TradeId);
        }

        private void DeliverTradeResult(HostTrade t, string partyId, List<ItemStack> give, int giveMoney, List<ItemStack> take, int takeMoney, string partnerName)
        {
            if (partyId == LocalId)
            {
                var lp = Game.LocalPlayer;
                foreach (var s in give) lp.GiveOrDrop(s.Clone(), "trade");
                if (giveMoney > 0) lp.Wallet.Add(giveMoney, "trade:" + t.TradeId);
                return;
            }
            QueueTx(partyId, new TxMsg
            {
                txId = "trade-commit:" + t.TradeId + ":" + partyId,
                kind = TxKind.TradeCommit,
                tradeId = t.TradeId,
                partnerName = partnerName,
                give = CloneList(give),
                giveMoney = giveMoney,
                take = CloneList(take),
                takeMoney = takeMoney,
                reason = "trade"
            });
        }

        private void AbortTrade(HostTrade t, string messageJa, string messageEn)
        {
            if (t == null || t.Phase == TradePhase.Committed || t.Phase == TradePhase.Aborted) return;
            t.Phase = TradePhase.Aborted;
            // 預けを頼んだゲストには「戻して」と伝える（受領済み台帳で重複しない。届かなくてもゲストは切断時に自分で戻す）
            if (t.ADepositRequested && t.AId != LocalId) QueueTx(t.AId, new TxMsg { txId = "trade-abort:" + t.TradeId + ":" + t.AId, kind = TxKind.TradeAbort, tradeId = t.TradeId });
            if (t.BDepositRequested && t.BId != LocalId) QueueTx(t.BId, new TxMsg { txId = "trade-abort:" + t.TradeId + ":" + t.BId, kind = TxKind.TradeAbort, tradeId = t.TradeId });
            BroadcastTradeState(t, messageJa, messageEn);
            trades.Remove(t.TradeId);
        }

        private void AbortTradesWith(string playerId)
        {
            foreach (var t in new List<HostTrade>(trades.Values))
                if (t.AId == playerId || t.BId == playerId) AbortTrade(t, string.Format("{0}さんがいなくなったので交換は不成立です", GetPlayerName(playerId)), string.Format("{0} left - trade cancelled", GetPlayerName(playerId)));
        }

        private void AbortAllTrades(string ja, string en)
        {
            foreach (var t in new List<HostTrade>(trades.Values)) AbortTrade(t, ja, en);
            trades.Clear();
        }

        private void TradeUpdate()
        {
            if (trades.Count == 0) return;
            foreach (var t in new List<HostTrade>(trades.Values))
            {
                if (clock <= t.Deadline) continue;
                if (t.Phase == TradePhase.Inviting) AbortTrade(t, "返事がなかったので交換は取りやめました", "No answer - trade cancelled");
                else if (t.Phase == TradePhase.Depositing) AbortTrade(t, "受け渡しが間に合わなかったので不成立です", "The exchange timed out");
            }
        }

        private void BroadcastTradeState(HostTrade t, string ja, string en)
        {
            SendTradeStateTo(t, t.AId, ja, en);
            SendTradeStateTo(t, t.BId, ja, en);
        }

        private void SendTradeStateTo(HostTrade t, string partyId, string ja, string en)
        {
            var msg = new TradeStateMsg
            {
                tradeId = t.TradeId,
                aId = t.AId,
                aName = GetPlayerName(t.AId),
                bId = t.BId,
                bName = GetPlayerName(t.BId),
                aItems = CloneList(t.AItems),
                aMoney = t.AMoney,
                bItems = CloneList(t.BItems),
                bMoney = t.BMoney,
                aReady = t.AReady,
                bReady = t.BReady,
                version = t.Version,
                phase = t.Phase,
                messageJa = ja,
                messageEn = en ?? ja
            };
            if (partyId == LocalId) OnTradeState(msg);
            else if (players.TryGetValue(partyId, out var sp)) SendTo(sp, NetMsg.TradeState, msg);
        }

        private int AvailableExact(string playerId, ItemStack s)
        {
            if (playerId == LocalId) return InventoryUtil.CountExact(Game.LocalPlayer.Inventory, s);
            return players.TryGetValue(playerId, out var sp) && sp.Mirror != null ? sp.Mirror.EffectiveCountExact(s) : 0;
        }

        private int AvailableMoney(string playerId)
        {
            if (playerId == LocalId) return Game.LocalPlayer.Wallet.money;
            return players.TryGetValue(playerId, out var sp) && sp.Mirror != null ? sp.Mirror.EffectiveMoney : 0;
        }

        private void TellPlayer(string playerId, string ja, string en)
        {
            if (playerId == LocalId) UIManager.Toast(Loc.T(ja, en));
            else NotifyRemote(playerId, ja, en);
        }

        private static List<ItemStack> CloneList(List<ItemStack> list)
        {
            var r = new List<ItemStack>();
            if (list != null) foreach (var s in list) if (s != null) r.Add(s.Clone());
            return r;
        }

        // ------------------------------------------------------------
        // 当事者側（ホスト自身・ゲスト共通）
        // ------------------------------------------------------------

        private void OnTradeInvite(TradeRequestMsg m)
        {
            if (m == null) return;
            UIManager.Choose(Loc.T("交換のお誘い", "Trade request"),
                Loc.Format("{0}さんが交換をしたがっています。", "{0} would like to trade with you.", m.fromName),
                new List<ChoiceOption>
                {
                    new ChoiceOption(Loc.T("交換する", "Trade"), () => ReplyTradeInvite(m.tradeId, true)),
                    new ChoiceOption(Loc.T("今はやめておく", "Not now"), () => ReplyTradeInvite(m.tradeId, false))
                });
        }

        private void OnTradeState(TradeStateMsg m)
        {
            if (m == null) return;
            localTrade = m;
            bool finished = m.phase == TradePhase.Committed || m.phase == TradePhase.Aborted;
            if (tradeWindow == null || !tradeWindow.IsOpen)
            {
                if (finished)
                {
                    string msg = Loc.T(m.messageJa, m.messageEn);
                    if (!string.IsNullOrEmpty(msg)) UIManager.Toast(msg);
                    return;
                }
                tradeWindow = new TradeWindow(this);
                UIManager.Open(tradeWindow);
            }
        }

        /// <summary>ゲスト：ホストから「品物を預けて」と頼まれた。持ち物から外し、台帳に記録してから返事をする。</summary>
        private void GuestOnDepositRequest(TradeDepositRequestMsg m)
        {
            if (m == null || string.IsNullOrEmpty(m.tradeId)) return;
            if (FindEscrow(m.tradeId) != null)
            {
                SendToHost(NetMsg.TradeDeposit, new TradeDepositMsg { tradeId = m.tradeId, ok = true });
                return;
            }
            var lp = Game.LocalPlayer;
            bool ok = lp.Wallet.money >= m.money;
            if (m.items != null)
                foreach (var s in m.items)
                    if (s != null && InventoryUtil.CountExact(lp.Inventory, s) < s.count) ok = false;
            if (ok)
            {
                var esc = new EscrowRecord { tradeId = m.tradeId, money = m.money, partnerName = PartnerNameOf(m.tradeId) };
                if (m.items != null)
                    foreach (var s in m.items)
                    {
                        if (s == null || s.IsEmpty) continue;
                        int removed = InventoryUtil.RemoveExact(lp.Inventory, s, s.count, "trade:" + m.tradeId);
                        if (removed > 0) esc.items.Add(s.CloneWithCount(removed));
                    }
                if (m.money > 0) lp.Wallet.TrySpend(m.money, "trade:" + m.tradeId);
                profileSave.escrows.Add(esc);
            }
            SendToHost(NetMsg.TradeDeposit, new TradeDepositMsg { tradeId = m.tradeId, ok = ok });
            SendProfileUpdate();
        }

        private string PartnerNameOf(string tradeId)
        {
            if (localTrade == null || localTrade.tradeId != tradeId) return null;
            return localTrade.aId == LocalId ? localTrade.bName : localTrade.aName;
        }

        private EscrowRecord FindEscrow(string tradeId)
        {
            foreach (var e in profileSave.escrows) if (e.tradeId == tradeId) return e;
            return null;
        }

        /// <summary>ゲスト：交換の確定（Tx）。</summary>
        private bool ApplyTradeCommitLocal(string tradeId, List<ItemStack> give, int giveMoney, List<ItemStack> take, int takeMoney, string partnerName)
        {
            var lp = Game.LocalPlayer;
            var esc = FindEscrow(tradeId);
            bool ok = true;
            if (esc != null)
            {
                profileSave.escrows.Remove(esc);
            }
            else
            {
                // 切断で自分の出した物を戻した後に確定が届いた（遅れた確定）：出した物を改めて外す
                if (take != null)
                    foreach (var s in take)
                        if (s != null && InventoryUtil.RemoveExact(lp.Inventory, s, s.count, "trade:" + tradeId) < s.count) ok = false;
                if (takeMoney > 0)
                {
                    int pay = Mathf.Min(takeMoney, lp.Wallet.money);
                    if (pay < takeMoney) ok = false;
                    if (pay > 0) lp.Wallet.TrySpend(pay, "trade:" + tradeId);
                }
                profileSave.rolledBackTrades.Remove(tradeId);
            }
            if (give != null) foreach (var s in give) if (s != null && !s.IsEmpty) lp.GiveOrDrop(s.Clone(), "trade");
            if (giveMoney > 0) lp.Wallet.Add(giveMoney, "trade:" + tradeId);
            UIManager.Toast(Loc.Format("{0}さんとの交換が成立しました", "Trade with {0} complete", partnerName ?? "?"));
            return ok;
        }

        /// <summary>ゲスト：交換の不成立（Tx）。預けた物を戻す。</summary>
        private void ApplyTradeAbortLocal(string tradeId)
        {
            var esc = FindEscrow(tradeId);
            if (esc == null) return;
            RestoreEscrow(esc);
            profileSave.escrows.Remove(esc);
        }

        private void RestoreEscrow(EscrowRecord esc)
        {
            var lp = Game?.LocalPlayer;
            if (lp == null || esc == null) return;
            foreach (var s in esc.items)
            {
                if (s == null || s.IsEmpty) continue;
                int left = lp.Inventory.Add(s.Clone(), "trade_refund");
                if (left > 0)
                {
                    // 持ち物がいっぱいでも消さない【仮】：退出直前で足元に落とすと失われるので、入る分だけ持ち物の枠を広げる
                    int max = Mathf.Max(1, s.Definition.MaxStack);
                    lp.Inventory.SetCapacity(lp.Inventory.capacity + (left + max - 1) / max);
                    lp.Inventory.Add(s.CloneWithCount(left), "trade_refund");
                }
            }
            if (esc.money > 0) lp.Wallet.Add(esc.money, "trade_refund:" + esc.tradeId);
        }

        /// <summary>切断・退出時：確定の知らせが来ていない預け物をすべて戻す（不成立で巻き戻し）。</summary>
        private void RollbackAllEscrows()
        {
            if (profileSave.escrows.Count == 0) return;
            foreach (var esc in profileSave.escrows)
            {
                RestoreEscrow(esc);
                if (!profileSave.rolledBackTrades.Contains(esc.tradeId)) profileSave.rolledBackTrades.Add(esc.tradeId);
            }
            profileSave.escrows.Clear();
            UIManager.Toast(Loc.T("途中だった交換は不成立になり、出した物は戻りました", "An unfinished trade was cancelled and your items returned"));
        }

        /// <summary>前回の異常終了などで残っていた預け物を戻す（ゲストとして訪問中でない時）。</summary>
        private void RollbackStaleEscrows()
        {
            if (IsGuestSession) return;
            RollbackAllEscrows();
        }

        private void CloseLocalTradeUI()
        {
            if (tradeWindow != null)
            {
                tradeWindow.SuppressCancelOnClose = true;
                UIManager.Close(tradeWindow);
                tradeWindow = null;
            }
            localTrade = null;
        }

        internal void OnTradeWindowClosed(TradeWindow w)
        {
            if (w == tradeWindow) tradeWindow = null;
            if (localTrade != null && (localTrade.phase == TradePhase.Committed || localTrade.phase == TradePhase.Aborted)) localTrade = null;
        }
    }
}
