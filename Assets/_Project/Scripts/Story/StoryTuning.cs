using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>
    /// ストーリー関連の調整用【仮数値】をまとめた場所。
    /// メインストーリー v0.2 の「未決定事項」（章の扱い・引き継ぎ・演出の長さ等）は、ここの値で調整する。
    /// </summary>
    public static class StoryTuning
    {
        // ------------------------------------------------------------
        // 章（メインストーリー：章は明示表示しない体験の流れとして内部で追跡）
        // ------------------------------------------------------------
        public const int ChapterPrologue = 0;
        public const int Chapter1 = 1;   // はじめての宇宙生活（プロローグ後）
        public const int Chapter2 = 2;   // 村の新しい住民（最初の転入）
        public const int Chapter3 = 3;   // それぞれの夢（数日経過＋親しさ）
        public const int Chapter4 = 4;   // 誰かの旅立ち（最初の転出）
        public const int Chapter5 = 5;   // 帰り道（最初の修理）
        public const int ChapterFinal = 6; // 最後の引っ越し（帰還を選択）

        /// <summary>第3章へ進むのに必要な、第2章に入ってからの最低日数【仮数値】。</summary>
        public const int Chapter3MinDaysAfterChapter2 = 3;
        /// <summary>第3章へ進むのに必要な、星に来てからの最低日数【仮数値】。</summary>
        public const int Chapter3MinDaysTotal = 5;
        /// <summary>第3章：誰か1人とこの親しさ以上になっていること【仮数値】（0〜100）。</summary>
        public const int Chapter3FriendshipThreshold = 30;
        /// <summary>章は1回の判定で1段階だけ進める（体験の流れを飛ばしすぎない）。</summary>
        public const bool OneChapterPerEvaluation = true;

        // ------------------------------------------------------------
        // 案内役の口癖（星ごとに決定的に選ぶ概念）
        // ------------------------------------------------------------
        /// <summary>案内役の口癖の候補【提案】。意味はゲームが説明しない。</summary>
        public static readonly string[] CatchphraseCandidates =
        {
            Concepts.FRIEND,
            Concepts.WELCOME,
            Concepts.TOGETHER,
            Concepts.FAMILY,
            Concepts.NEIGHBOR,
        };

        // ------------------------------------------------------------
        // 第1章：案内役の世話焼き（何かと食べ物をくれる・同じ言葉で話しかける）
        // ------------------------------------------------------------
        /// <summary>案内役が様子を見に来る間隔（日）【仮数値】。</summary>
        public const int GuideVisitIntervalDays = 3;
        /// <summary>案内役の世話焼きが続く最後の章【仮数値】。</summary>
        public const int GuideVisitLastChapter = 3;
        /// <summary>案内役が主人公に気付いて近寄ってくる距離（m）【仮数値】。</summary>
        public const float GuideNoticeDistance = 22f;
        /// <summary>案内役が話しかける距離（m）。</summary>
        public const float GuideTalkDistance = 3.2f;
        /// <summary>案内役が世話焼きに来る時間帯（時）【仮数値】。</summary>
        public const int GuideVisitFromHour = 8;
        public const int GuideVisitToHour = 18;

        // ------------------------------------------------------------
        // エンディング「最後の引っ越し」（ロケット §19、メインストーリー最終章）
        // ------------------------------------------------------------
        /// <summary>この親しさ以上は「仲の良い住民」：プレゼントを渡し、泣くこともある【仮数値】。</summary>
        public const int EndingCloseFriendship = 60;
        /// <summary>この親しさ以上で泣く【仮数値】。</summary>
        public const int EndingCryingFriendship = 80;
        /// <summary>この親しさ以上は近くまで見送りに来る【仮数値】。未満は遠くから見ているだけ。</summary>
        public const int EndingNearFriendship = 20;
        /// <summary>主人公の商店でこの回数以上買った住民は「常連客」として見送りに来る【仮数値】。</summary>
        public const int EndingRegularPurchases = 4;
        /// <summary>プレゼントを渡す住民の上限【仮数値】。</summary>
        public const int EndingMaxGiftGivers = 3;
        /// <summary>言葉をかける住民の上限（演出の長さ調整）【仮数値】。</summary>
        public const int EndingMaxFarewellSpeakers = 8;
        /// <summary>引っ越した住民から手紙が届く親しさ【仮数値】。会ったことがあれば最低限届く。</summary>
        public const int EndingLetterFriendship = 15;
        public const int EndingMaxLetters = 5;
        /// <summary>見送りの輪の半径（m）。</summary>
        public const float EndingRingNear = 6.5f;
        public const float EndingRingMid = 10f;
        public const float EndingRingFar = 20f;
        /// <summary>辞書の映し出しで表示する語数【仮数値】。</summary>
        public const int EndingDictionaryWords = 12;
        /// <summary>住民が指定地点に着くのを待つ最大秒数。超えたら位置を直接合わせる。</summary>
        public const float ActorArriveTimeout = 7f;

        // ------------------------------------------------------------
        // プロローグ
        // ------------------------------------------------------------
        /// <summary>プロローグで主人公が案内役に近づくのを待つ秒数（超えたら自動で歩く）【仮数値】。</summary>
        public const float PrologueApproachTimeout = 14f;
        /// <summary>主人公が案内役に近づいたとみなす距離。</summary>
        public const float PrologueApproachDistance = 4.2f;
        /// <summary>案内役についていく区間の制限時間（超えたら暗転して村へ）【仮数値】。</summary>
        public const float PrologueFollowTimeout = 75f;
        /// <summary>村に着いたとみなす距離。</summary>
        public const float PrologueArriveDistance = 9f;

        // ------------------------------------------------------------
        // 次の星へ（クリア後の選択 B）の引き継ぎ既定値【未決定→仮・選択式】
        // ------------------------------------------------------------
        public const bool CarryDefaultKeepAppearance = true;
        public const bool CarryDefaultKeepItems = true;
        public const bool CarryDefaultKeepMoney = true;
        public const bool CarryDefaultKeepTools = true;
        /// <summary>持ち物を置いていく場合でも持っていく「思い出の品」の分類。</summary>
        public static readonly ItemCategory[] KeepsakeCategories = { ItemCategory.Letter, ItemCategory.Gift, ItemCategory.KeyItem };

        // ------------------------------------------------------------
        // 見た目
        // ------------------------------------------------------------
        public static readonly Color CutsceneLetterbox = new Color(0f, 0f, 0f, 0.92f);
        public const float LetterboxHeight = 96f;

        /// <summary>ストーリーが登録する料理アイテム（他モジュールの料理が無い場合の代わり）。</summary>
        public const string ItemWelcomeSnack = "story.welcome_snack";
        public const string ItemGuideSnack = "story.guide_snack";
        public const string ItemTravelBento = "story.travel_bento";

        /// <summary>タブレットのアプリID。</summary>
        public const string AppAppearance = "appearance";
        public const string AppMemories = "memories";
        /// <summary>辞書アプリのID（Tablet 担当の実装に合わせて要確認）。</summary>
        public const string AppDictionaryGuess = "dictionary";
    }
}
