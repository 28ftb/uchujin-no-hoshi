using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Network
{
    public enum SessionState
    {
        None,
        HostStarting,          // EOS のログイン・ロビー作成中
        Hosting,
        GuestConnecting,       // トランスポート接続中
        GuestAwaitingSnapshot, // 参加承認〜スナップショット受信中
        GuestLoading,          // Game.BeginGuestSession の読み込み中
        GuestPlaying
    }

    /// <summary>セッションに参加している1人（ホスト・ゲスト双方の視点で使う）。</summary>
    public sealed class SessionPlayer
    {
        public string PlayerId;
        public string DisplayName;
        public PermissionLevel Permission;
        public bool IsHost;
        public bool IsLocal;
        public string AppearanceJson;

        // ---- ホスト側でだけ使う ----
        public string NetId;
        /// <summary>ゲストが星の読み込みを終えた（位置・身振りを送り始める）。</summary>
        public bool Ready;
        public int TxSeq;
        public GuestMirror Mirror;
        public readonly CommandResultCache Commands = new CommandResultCache();
        public float KickAt = -1f;

        // ---- 表示 ----
        public RemotePlayerAvatar Avatar;
        public uint LastTransformSeq;
    }

    /// <summary>
    /// ホストが持つゲストの持ち物・所持金のミラー（INetworkBridge の RemoteHasItem / TryChargeRemote の判定用）。
    /// ゲストから届いた要約（summary）に、まだ要約へ反映されていない送信済み Tx（pending）を重ねて有効値を求める。
    /// ゲスト自身の持ち物の正本はゲスト側（マルチ §5：携帯資産は各プレイヤー）。
    /// </summary>
    public sealed class GuestMirror
    {
        public PlayerState.ProfileData Summary;
        /// <summary>要約に反映済みの Tx の seq。</summary>
        public int SummarySeq;
        public readonly List<TxMsg> Pending = new List<TxMsg>();

        public void SetSummary(PlayerState.ProfileData d, int lastSeq)
        {
            if (d == null) return;
            Summary = d;
            SummarySeq = Math.Max(SummarySeq, lastSeq);
            Pending.RemoveAll(t => t.seq <= SummarySeq);
        }

        public int EffectiveCount(string itemId)
        {
            int n = 0;
            if (Summary != null && Summary.inventory != null) n = Summary.inventory.CountOf(itemId);
            foreach (var t in Pending)
            {
                switch (t.kind)
                {
                    case TxKind.GrantItem:
                        if (t.stack != null && t.stack.itemId == itemId) n += t.stack.count;
                        break;
                    case TxKind.TakeItem:
                    case TxKind.TakeItemExact:
                        if (t.itemId == itemId) n -= t.count;
                        break;
                    case TxKind.TradeCommit:
                        if (t.give != null) foreach (var s in t.give) if (s != null && s.itemId == itemId) n += s.count;
                        break;
                }
            }
            return Math.Max(0, n);
        }

        /// <summary>品質・データまで一致する数（交換の提示の確認用）。</summary>
        public int EffectiveCountExact(ItemStack like)
        {
            int n = 0;
            if (Summary != null && Summary.inventory != null) n = InventoryUtil.CountExact(Summary.inventory, like);
            foreach (var t in Pending)
            {
                if (t.kind == TxKind.GrantItem && t.stack != null && t.stack.CanStackWith(like)) n += t.stack.count;
                else if (t.kind == TxKind.TakeItem && t.itemId == like.itemId) n -= t.count;
                else if (t.kind == TxKind.TakeItemExact && t.stack != null && t.stack.CanStackWith(like)) n -= t.count;
                else if (t.kind == TxKind.TradeCommit && t.give != null)
                    foreach (var s in t.give) if (s != null && s.CanStackWith(like)) n += s.count;
            }
            return Math.Max(0, n);
        }

        public int EffectiveMoney
        {
            get
            {
                int m = Summary != null ? Summary.money : 0;
                foreach (var t in Pending)
                {
                    switch (t.kind)
                    {
                        case TxKind.Charge: m -= t.amount; break;
                        case TxKind.PayMoney: m += t.amount; break;
                        case TxKind.TradeCommit: m += t.giveMoney; break;
                    }
                }
                return Math.Max(0, m);
            }
        }
    }

    /// <summary>同じ commandId の再送に、もう一度実行せず同じ結果を返すためのキャッシュ（マルチ §20.5）。</summary>
    public sealed class CommandResultCache
    {
        private readonly Dictionary<string, CommandResultMsg> map = new Dictionary<string, CommandResultMsg>();
        private readonly Queue<string> order = new Queue<string>();

        public bool TryGet(string id, out CommandResultMsg r) => map.TryGetValue(id ?? "", out r);

        public void Add(string id, CommandResultMsg r)
        {
            if (string.IsNullOrEmpty(id) || map.ContainsKey(id)) return;
            map[id] = r;
            order.Enqueue(id);
            while (order.Count > NetConfig.CommandResultCacheSize) map.Remove(order.Dequeue());
        }
    }

    // ---------------- セーブ（SaveKey "network"） ----------------

    /// <summary>この星に来たことのあるゲスト（再訪時に権限を復元する：マルチ §4.3）。</summary>
    [Serializable]
    public class KnownGuest
    {
        public string playerId;
        public string displayName;
        public int permission;
        public int firstDay;
        public int lastDay;
        public int visits;
    }

    /// <summary>まだゲストが受領を確認していない Tx（切断・再訪時に再送する）。</summary>
    [Serializable]
    public class OutboxEntry
    {
        public string playerId;
        public TxMsg tx;
    }

    /// <summary>Planet スコープ（ホストの星のセーブに入る）。スナップショットではゲストへ送らない。</summary>
    [Serializable]
    public class NetPlanetSave
    {
        public List<KnownGuest> guests = new List<KnownGuest>();
        public List<OutboxEntry> outbox = new List<OutboxEntry>();
    }

    /// <summary>交換で預けた（自分の持ち物から外した）品物。確定・中止の知らせが来るまで保持する。</summary>
    [Serializable]
    public class EscrowRecord
    {
        public string tradeId;
        public List<ItemStack> items = new List<ItemStack>();
        public int money;
        public string partnerName;
    }

    /// <summary>PlayerProfile スコープ（持ち物と一緒に自分のスロットへ保存）。受領済み取引IDの台帳。</summary>
    [Serializable]
    public class NetProfileSave
    {
        public List<string> appliedTxIds = new List<string>();
        public List<EscrowRecord> escrows = new List<EscrowRecord>();
        public List<string> rolledBackTrades = new List<string>();
    }

    public static class InventoryUtil
    {
        /// <summary>品質・データ・所有者まで一致する数。</summary>
        public static int CountExact(Inventory inv, ItemStack like)
        {
            if (inv == null || like == null || string.IsNullOrEmpty(like.itemId)) return 0;
            var probe = like.CloneWithCount(1);
            int n = 0;
            foreach (var s in inv.slots) if (s != null && !s.IsEmpty && s.CanStackWith(probe)) n += s.count;
            return n;
        }

        /// <summary>完全一致する物を count 個取り除き、取り除いた数を返す。</summary>
        public static int RemoveExact(Inventory inv, ItemStack like, int count, string reason)
        {
            if (inv == null || like == null || count <= 0) return 0;
            var probe = like.CloneWithCount(1);
            int removed = 0;
            for (int i = 0; i < inv.slots.Count && removed < count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.IsEmpty || !s.CanStackWith(probe)) continue;
                var taken = inv.TakeFromSlot(i, count - removed, reason);
                if (taken != null) removed += taken.count;
            }
            return removed;
        }

        /// <summary>同じ物をまとめる（交換の提示用）。</summary>
        public static List<ItemStack> Merge(List<ItemStack> list)
        {
            var result = new List<ItemStack>();
            if (list == null) return result;
            foreach (var s in list)
            {
                if (s == null || s.IsEmpty) continue;
                bool merged = false;
                foreach (var r in result)
                {
                    if (r.CanStackWith(s))
                    {
                        r.count += s.count;
                        merged = true;
                        break;
                    }
                }
                if (!merged) result.Add(s.Clone());
            }
            return result;
        }

        public static string Describe(List<ItemStack> items, int money)
        {
            var parts = new List<string>();
            if (items != null) foreach (var s in items) if (s != null && !s.IsEmpty) parts.Add($"{s.DisplayName}×{s.count}");
            if (money > 0) parts.Add(Wallet.Format(money));
            return parts.Count == 0 ? Loc.T("（なし）", "(nothing)") : string.Join(Loc.T("、", ", "), parts);
        }
    }
}
