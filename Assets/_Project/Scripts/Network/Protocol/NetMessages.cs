using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Network
{
    /// <summary>
    /// メッセージ種別。ネットワーク構成はスター型（ゲストはホストとだけ繋がる）。ゲスト同士の物（辞書・交換・位置）はホストが中継する。
    /// 1バイトで送るので値を変えたら NetConfig.ProtocolVersion を上げること。
    /// </summary>
    public enum NetMsg : byte
    {
        // 参加（マルチ §4.1, §20.3）
        Hello = 1,            // ゲスト→ホスト：参加要求（プレイヤーID・名前・見た目・持ち物の要約）
        JoinAccepted = 2,     // ホスト→ゲスト
        JoinRejected = 3,
        SnapshotBegin = 4,    // PlanetInfo（WorldId・シード・生成バージョン）
        SnapshotEntries = 5,  // 星の状態（SaveEntry の束）
        SnapshotEnd = 6,
        GuestReady = 7,       // ゲスト→ホスト：星の読み込み完了

        // 状態・コマンド（マルチ §20.2, §20.4）
        StateDelta = 10,      // ホスト→ゲスト：MarkDirty されたキーの状態
        Command = 11,         // ゲスト→ホスト：WorldCommand
        CommandResult = 12,
        Tx = 13,              // ホスト→ゲスト：付与・課金・取り去り・交換の確定（取引ID付き）
        TxAck = 14,
        Notify = 15,
        ProfileUpdate = 16,   // ゲスト→ホスト：持ち物・所持金の要約（ホストのミラー用）

        // プレイヤー
        PlayerList = 20,
        Transform = 21,       // unreliable 約10Hz
        Gesture = 22,
        Kick = 23,

        // 睡眠（時間天候 §16、マルチ §18.2）
        SleepRequest = 30,
        SleepVoteStart = 31,
        SleepVote = 32,
        SleepResult = 33,

        // 辞書の手渡し（マルチ §13：自動配布しない）
        DictionaryOffer = 40,
        DictionaryReply = 41,

        // 直接交換（マルチ §12.2）
        TradeRequest = 50,
        TradeReply = 51,
        TradeOffer = 52,
        TradeReady = 53,
        TradeCancel = 54,
        TradeDepositRequest = 55,
        TradeDeposit = 56,
        TradeState = 57,

        // 退出
        Leave = 60,
        HostClosing = 61
    }

    [Serializable]
    public class HelloMsg
    {
        public int protocol;
        public string playerId;
        public string displayName;
        /// <summary>PlayerState.ProfileData の JSON（見た目・持ち物・所持金・道具）。ホストのミラー用。</summary>
        public string profileJson;
        public int worldGenVersion;
        public int languageGenVersion;
    }

    [Serializable]
    public class JoinAcceptedMsg
    {
        public string hostPlayerId;
        public string hostName;
        /// <summary>ホストが割り当てたID（通常は Hello の playerId と同じ。同一PCテストで重複した時だけ別ID）。</summary>
        public string assignedPlayerId;
        public int permission;
        public int maxPlayers;
    }

    [Serializable]
    public class JoinRejectedMsg
    {
        public string reasonJa;
        public string reasonEn;
    }

    [Serializable]
    public class SnapshotBeginMsg
    {
        public PlanetInfo planet;
        public int entryCount;
    }

    [Serializable]
    public class StateEntriesMsg
    {
        public List<SaveEntry> entries = new List<SaveEntry>();
    }

    [Serializable]
    public class EmptyMsg
    {
        public int dummy;
    }

    [Serializable]
    public class CommandMsg
    {
        public string commandId;
        public string typeName;
        public string json;
    }

    [Serializable]
    public class CommandResultMsg
    {
        public string commandId;
        public bool success;
        public string ja;
        public string en;
        /// <summary>CommandResult.Data（モジュールが結果に付ける追加データ）。</summary>
        public string data;
    }

    public enum TxKind
    {
        GrantItem = 1,
        TakeItem = 2,
        Charge = 3,
        PayMoney = 4,
        TradeCommit = 5,
        TradeAbort = 6
    }

    /// <summary>
    /// ホスト→ゲストの持ち物の変更（取引ID付き）。ゲストは受領済み txId を記録し、再送を二重受領しない（マルチ §20.5）。
    /// </summary>
    [Serializable]
    public class TxMsg
    {
        public string txId;
        /// <summary>このセッション内の通し番号（ホストのミラー計算用）。</summary>
        public int seq;
        public TxKind kind;
        public ItemStack stack;
        public string itemId;
        public int count;
        public int amount;
        public string reason;
        // 交換
        public string tradeId;
        public string partnerName;
        /// <summary>受け取る物（相手の出した品物）。</summary>
        public List<ItemStack> give = new List<ItemStack>();
        public int giveMoney;
        /// <summary>自分が出した物（預け済み。巻き戻し後の遅れた確定でだけ使う）。</summary>
        public List<ItemStack> take = new List<ItemStack>();
        public int takeMoney;

        public TxMsg Clone()
        {
            var c = (TxMsg)MemberwiseClone();
            c.give = new List<ItemStack>();
            if (give != null) foreach (var s in give) c.give.Add(s?.Clone());
            c.take = new List<ItemStack>();
            if (take != null) foreach (var s in take) c.take.Add(s?.Clone());
            c.stack = stack?.Clone();
            return c;
        }
    }

    [Serializable]
    public class TxAckMsg
    {
        public string txId;
        public bool ok;
    }

    [Serializable]
    public class NotifyMsg
    {
        public string ja;
        public string en;
    }

    [Serializable]
    public class ProfileUpdateMsg
    {
        public string profileJson;
        /// <summary>このセッションで最後に適用した Tx の seq（ホストのミラー計算用）。</summary>
        public int lastTxSeq;
    }

    [Serializable]
    public class PlayerInfoDto
    {
        public string playerId;
        public string displayName;
        public int permission;
        public string appearanceJson;
        public bool isHost;
    }

    [Serializable]
    public class PlayerListMsg
    {
        public List<PlayerInfoDto> players = new List<PlayerInfoDto>();
    }

    [Serializable]
    public class GestureMsg
    {
        public string playerId;
        public int gesture;
    }

    [Serializable]
    public class KickMsg
    {
        public string reasonJa;
        public string reasonEn;
    }

    [Serializable]
    public class SleepRequestMsg
    {
        public int wakeHour;
    }

    [Serializable]
    public class SleepVoteStartMsg
    {
        public string voteId;
        public string requesterId;
        public string requesterName;
        public int wakeHour;
        public float seconds;
    }

    [Serializable]
    public class SleepVoteMsg
    {
        public string voteId;
        public bool agree;
    }

    [Serializable]
    public class SleepResultMsg
    {
        public string voteId;
        public bool success;
        public int wakeHour;
        public string requesterId;
    }

    public enum DictionaryReplyChoice
    {
        Declined = 0,
        SeparateVolume = 1,
        Merged = 2,
        NoTablet = 3
    }

    [Serializable]
    public class DictionaryOfferMsg
    {
        public string offerId;
        public string fromPlayerId;
        public string fromName;
        public string toPlayerId;
        public bool all;
        public int wordCount;
        public string json;
    }

    [Serializable]
    public class DictionaryReplyMsg
    {
        public string offerId;
        public string responderId;
        public string responderName;
        /// <summary>辞書を渡した人（返事の宛先）。</summary>
        public string toPlayerId;
        public int choice;
    }

    public enum TradePhase
    {
        Inviting = 0,
        Negotiating = 1,
        Depositing = 2,
        Committed = 3,
        Aborted = 4
    }

    [Serializable]
    public class TradeRequestMsg
    {
        public string tradeId;
        public string fromId;
        public string fromName;
        public string toId;
    }

    [Serializable]
    public class TradeReplyMsg
    {
        public string tradeId;
        public string fromId;
        public bool accept;
    }

    [Serializable]
    public class TradeOfferMsg
    {
        public string tradeId;
        public string fromId;
        public List<ItemStack> items = new List<ItemStack>();
        public int money;
    }

    [Serializable]
    public class TradeReadyMsg
    {
        public string tradeId;
        public string fromId;
        /// <summary>この版の提示内容に同意する（提示が変わったら同意は外れる）。</summary>
        public int version;
        public bool ready;
    }

    [Serializable]
    public class TradeCancelMsg
    {
        public string tradeId;
        public string fromId;
        public string reasonJa;
        public string reasonEn;
    }

    [Serializable]
    public class TradeDepositRequestMsg
    {
        public string tradeId;
        public List<ItemStack> items = new List<ItemStack>();
        public int money;
    }

    [Serializable]
    public class TradeDepositMsg
    {
        public string tradeId;
        public bool ok;
    }

    [Serializable]
    public class TradeStateMsg
    {
        public string tradeId;
        public string aId;
        public string aName;
        public string bId;
        public string bName;
        public List<ItemStack> aItems = new List<ItemStack>();
        public int aMoney;
        public List<ItemStack> bItems = new List<ItemStack>();
        public int bMoney;
        public bool aReady;
        public bool bReady;
        public int version;
        public TradePhase phase;
        public string messageJa;
        public string messageEn;
    }

    [Serializable]
    public class LeaveMsg
    {
        public string reason;
    }
}
