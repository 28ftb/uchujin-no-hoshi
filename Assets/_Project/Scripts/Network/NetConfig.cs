using Uchujin.Core;

namespace Uchujin.Network
{
    /// <summary>
    /// マルチプレイの調整用定数（【仮数値】）。
    /// 人数・同期方式の詳細は未決定（基本決定 v0.2 §6、マルチ §2.1）なので、ここで変更できるようにまとめる。
    /// </summary>
    public static class NetConfig
    {
        /// <summary>通信プロトコルの版。互換性の無い変更をしたら上げる（参加時に照合）。</summary>
        public const int ProtocolVersion = 2; // 2：一時メッセージ（Transient）を追加、発話の中継を状態から外した

        /// <summary>最大人数（ホストを含む）。マルチ §2.1：初期は4人を仮の既定とし、確定上限とはしない。</summary>
        public const int MaxPlayers = 4;

        /// <summary>LAN／同一PC検証用 UDP の既定ポート【仮】。</summary>
        public const int DefaultUdpPort = 47777;

        // ---- 同期間隔 ----
        /// <summary>MarkDirty されたキーをまとめて送る最短間隔（秒）。0.3〜0.5 秒。</summary>
        public const float StateSyncInterval = 0.4f;
        /// <summary>同じキーを再送する最短間隔（秒）。頻繁に MarkDirty されるシステムの帯域を抑える【仮】。</summary>
        public const float PerKeyMinInterval = 0.4f;
        /// <summary>
        /// 大きなキーの間引き：1つのキーに使ってよい帯域の目安（圧縮前の JSON バイト／秒）【仮】。
        /// キーの間隔 = 前回の JSON の大きさ ÷ この値（PerKeyMinInterval〜PerKeyMaxInterval に収める）。
        /// 例：住民（全員の状態を約1秒ごとに MarkDirty）が 200KB なら約1.6秒ごと。小さいキーは従来どおり 0.4 秒ごと。
        /// </summary>
        public const float LargeKeyBytesPerSecond = 128f * 1024f;
        /// <summary>大きなキーでも、これより長くは待たせない（秒）【仮】。</summary>
        public const float PerKeyMaxInterval = 2.5f;
        /// <summary>時刻（"core.time"）を定期的に送る間隔（秒）。時間天候 §16：ホストの時刻を共有。</summary>
        public const float TimeSyncInterval = 5f;
        /// <summary>星のフラグ（"core.flags"）の変化を確認する間隔（秒）。FlagStore には変更通知が無いため比較で検出。</summary>
        public const float FlagsCheckInterval = 2f;
        /// <summary>位置の送信間隔（秒）。約10Hz・unreliable。</summary>
        public const float TransformSendInterval = 0.1f;
        /// <summary>位置補間の遅延（秒）。受信した位置をこの時間だけ遅らせて滑らかに補間する。</summary>
        public const float InterpolationDelay = 0.15f;
        /// <summary>最後の受信からこの秒数までは外挿する。</summary>
        public const float MaxExtrapolation = 0.25f;
        /// <summary>これ以上離れたら補間せず瞬間移動（エリア移動など）。</summary>
        public const float TeleportDistance = 12f;

        // ---- 一時メッセージ ----
        /// <summary>ゲストの読み込み中に届いた一時メッセージを溜めておく上限（超えたら古い物から捨てる）【仮】。</summary>
        public const int MaxBufferedTransients = 256;

        // ---- ゲストの持ち物 ----
        /// <summary>ゲストの持ち物・所持金の要約をホストへ送る間隔（変化があった時だけ）。</summary>
        public const float ProfileUpdateDebounce = 0.5f;
        /// <summary>訪問中、ゲストの持ち物を自分のスロットへ自動保存する間隔（実時間秒）。異常終了時の損失を減らす。</summary>
        public const float GuestProfileAutosaveSeconds = 120f;
        /// <summary>
        /// 付与・課金（Tx）を受け取ってから、持ち物を自分のスロットへ保存して受領確認を返すまでの最長の待ち（実時間秒）【仮】。
        /// 受領確認は保存後に返すので、保存前に異常終了してもホストが再送する（受け取り損ね・二重受領を防ぐ）。
        /// </summary>
        public const float TxAckSaveDelay = 10f;

        // ---- 睡眠・交換・参加 ----
        /// <summary>睡眠の投票の受付時間（実時間秒）【仮】。</summary>
        public const float SleepVoteSeconds = 30f;
        /// <summary>交換の申し込みへの返事を待つ時間（秒）。</summary>
        public const float TradeRequestTimeout = 30f;
        /// <summary>交換で品物を預ける（確定処理）の待ち時間（秒）。</summary>
        public const float TradeDepositTimeout = 15f;
        /// <summary>接続〜スナップショット受信の待ち時間（秒）。</summary>
        public const float JoinTimeoutSeconds = 45f;
        /// <summary>個人の休息（睡眠の同意が揃わなかった時）の暗転時間（秒）。</summary>
        public const float PersonalRestSeconds = 1.6f;

        // ---- 冪等性 ----
        /// <summary>ホストが覚えておくコマンド結果（同じ commandId の再送に同じ結果を返す）。</summary>
        public const int CommandResultCacheSize = 256;
        /// <summary>ゲストが覚えておく受領済み取引ID（二重受領防止：マルチ §20.5）。</summary>
        public const int LedgerSize = 2000;

        // ---- 権限・その他 ----
        /// <summary>初めて来たゲストの権限（マルチ §6.2）。再訪時は前回の権限を復元する。</summary>
        public const PermissionLevel DefaultGuestPermission = PermissionLevel.Helper;
        /// <summary>
        /// ゲストの身振りをホスト側で IQuestService.TryHandleGesture にも渡すか。
        /// ゲスト端末でも Game.PerformLocalGesture が Quest を呼ぶため、二重処理を避けて既定は false。
        /// </summary>
        public const bool ForwardGuestGestureToQuests = false;
        /// <summary>タイトル画面の右下に「友達の星へ行く」ボタンを出す（Story のタイトルに入口が無い場合の暫定）。</summary>
        public const bool ShowTitleJoinButton = false; // Story とコアのタイトル画面に入口があるため無効（統合時に変更）

        // ---- 符号化 ----
        /// <summary>これより大きいメッセージは GZip 圧縮する（バイト）。</summary>
        public const int CompressThresholdBytes = 768;
        /// <summary>unreliable 送信の最大サイズ（EOS のパケット上限 1170 からヘッダ分を引いた値）。</summary>
        public const int MaxUnreliableBytes = 1100;

        // ---- EOS ----
        /// <summary>EOS P2P のソケット名（英数字32文字以内）。</summary>
        public const string EosSocketName = "UCHUJINP2P";
        /// <summary>EOS ロビーのバケットID。</summary>
        public const string EosBucketId = "uchujin:v1";
        /// <summary>EOS P2P の1パケットの上限（EOS_P2P_MAX_PACKET_SIZE = 1170 バイト）。</summary>
        public const int EosMaxPacketSize = 1170;
        /// <summary>招待コードの文字数。</summary>
        public const int InviteCodeLength = 6;
    }
}
