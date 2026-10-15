using UnityEngine;

namespace Uchujin.Tablet
{
    /// <summary>
    /// タブレット・マネピの調整用の【仮数値】。仕様上「未決定」の項目（記憶件数・消去ルール・操作キーなど）はここにまとめる。
    /// </summary>
    public static class TabletTuning
    {
        // ---- マネピの伝言（タブレット仕様 §4.3「記憶件数・消去ルールは未決定」→仮） ----
        /// <summary>1つの伝言板が覚えておける伝言の数（超えたら古いものから忘れる）。</summary>
        public const int ManepiMaxMessagesPerBoard = 24;
        /// <summary>伝言を覚えている日数。</summary>
        public const int ManepiRetentionDaysPlaza = 7;
        public const int ManepiRetentionDaysShop = 7;
        public const int ManepiRetentionDaysHome = 21;
        public const int ManepiRetentionDaysDefault = 10;
        /// <summary>主人公の身振りをマネピが真似する距離（m）。</summary>
        public const float ManepiMimicRange = 10f;
        /// <summary>真似を始めるまでの遅れ（秒）。</summary>
        public const float ManepiMimicDelay = 0.55f;
        /// <summary>主人公の方を向く距離（m）。</summary>
        public const float ManepiLookRange = 6f;

        // ---- 会話の「その場で保存」（§8.4「ワンボタンで発話を保存」） ----
        /// <summary>直前に聞いた発話を保存できる時間（実時間の秒）。</summary>
        public const float QuickSaveWindowSeconds = 8f;
        public const KeyCode QuickSaveKey = KeyCode.K;

        // ---- カメラ（§7.4, §11.3） ----
        public const KeyCode CameraShutterKey = KeyCode.Return;
        public const int PhotoWidth = 960;
        /// <summary>写真に「写っていた」とみなす住民までの最大距離（m）。</summary>
        public const float PhotoResidentMaxDistance = 30f;
        /// <summary>画面端からこの割合以内なら写っていない扱い。</summary>
        public const float PhotoViewportMargin = 0.04f;

        // ---- 表示 ----
        public const int PinnedOverlayMax = 4;
        public const int MaxHistoryLines = 40;
        /// <summary>辞書の「その語が出てきた記録」に並べる最大数。</summary>
        public const int MaxIndexHits = 40;
    }

    /// <summary>
    /// タブレットの設定（プレイヤーの端末の好み。星や進行とは関係ないので PlayerPrefs に保存）。
    /// </summary>
    public static class TabletSettings
    {
        private const string KeyGloss = "uchujin.tablet.glossOverlay";
        private const string KeyAlienScale = "uchujin.tablet.alienTextScale";
        private const string KeyQuickSave = "uchujin.tablet.quickSaveKey";
        private const string KeyPinned = "uchujin.tablet.pinnedOverlay";
        private const string KeyMimic = "uchujin.tablet.manepiMimic";

        private static int Get(string k, int fallback)
        {
            try { return PlayerPrefs.GetInt(k, fallback); }
            catch { return fallback; }
        }

        private static void Set(string k, int v)
        {
            try
            {
                PlayerPrefs.SetInt(k, v);
                PlayerPrefs.Save();
            }
            catch { }
        }

        /// <summary>推測訳の補助表示（§6.5。既定オフ）。</summary>
        public static bool GlossOverlay
        {
            get => Get(KeyGloss, 0) == 1;
            set => Set(KeyGloss, value ? 1 : 0);
        }

        /// <summary>タブレット内の宇宙語の文字の大きさ（0=小 1=標準 2=大）。</summary>
        public static int AlienTextScale
        {
            get => Mathf.Clamp(Get(KeyAlienScale, 1), 0, 2);
            set => Set(KeyAlienScale, Mathf.Clamp(value, 0, 2));
        }

        /// <summary>[K] で直前の発話を音声カードに保存する機能。</summary>
        public static bool QuickSaveKeyEnabled
        {
            get => Get(KeyQuickSave, 1) == 1;
            set => Set(KeyQuickSave, value ? 1 : 0);
        }

        /// <summary>ピン留めした語・カードを画面端に表示する（§11.1 ピン留め）。</summary>
        public static bool ShowPinnedOverlay
        {
            get => Get(KeyPinned, 1) == 1;
            set => Set(KeyPinned, value ? 1 : 0);
        }

        /// <summary>マネピが主人公の身振りを真似する演出。</summary>
        public static bool ManepiMimic
        {
            get => Get(KeyMimic, 1) == 1;
            set => Set(KeyMimic, value ? 1 : 0);
        }
    }
}
