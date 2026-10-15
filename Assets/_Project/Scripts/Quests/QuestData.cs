using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Quests
{
    // =====================================================================
    // クエストの状態データ（JsonUtility で保存・同期するため public フィールドのみ）。
    // 星の状態（Planet スコープ）：依頼の実体（プレイヤーごとのインスタンス）、掲示板、お手伝いの出来事、
    //   個別ストーリーの進行、村の行事、生活の変化。
    // 個人の知識（PlayerPlanetKnowledge）：依頼帳（宇宙語の原文・見せられた物・自分のメモ）。
    // =====================================================================

    /// <summary>クエストの系統（クエスト仕様 §2 の5系統＋掲示板・お手伝いの入口）。</summary>
    public enum QuestKind
    {
        DailyFavor,    // ① 日常のお願い（§6）
        Board,         // 日替わり掲示板（§6.2）
        Spontaneous,   // ③ 自発的なお手伝い（§7）
        LifeBorn,      // 住民の暮らしから生まれる依頼（§8）
        Story,         // ② 住民固有の個別ストーリー（§9）
        VillageEvent   // ④ 村の行事（§13）
    }

    public enum GoalType
    {
        DeliverItem,        // 物を渡す（釣っても買っても手持ちでも良い：§5.2）
        DeliverToResident,  // 預かった物を別の住民へ届ける（配達・青い花）
        GiftFor,            // 誰かへの贈り物を一緒に選ぶ（何を渡しても成立し、反応が変わる：§10）
        CatchCreature,      // 生き物を捕まえて見せる（生き物仕様 §11.2）
        Escort,             // 一緒に行く（行動そのものが目的：§5.2 の例外）
        Attend              // その場に居合わせる
    }

    [Serializable]
    public class QuestGoal
    {
        public GoalType type;
        /// <summary>特定のアイテム（空ならカテゴリ＋タグで判定）。</summary>
        public string itemId;
        public bool useCategory;
        public ItemCategory category;
        /// <summary>必要なタグ（"color_blue" "glowing" など。空なら不問）。</summary>
        public string tag;
        public int count = 1;
        public int minQuality;
        public float minSize;
        /// <summary>届け先の住民（DeliverToResident / GiftFor の相手）。</summary>
        public string targetResidentId;
        public string speciesId;
        public Vector3 place;
        /// <summary>進捗（渡した数）。</summary>
        public int progress;
        /// <summary>見本（見せた物・描いた絵）。カテゴリ指定の時の例。</summary>
        public string sampleItemId;

        public int Remaining => Mathf.Max(0, count - progress);
        public bool IsDone => progress >= count;

        public QuestGoal Clone()
        {
            return (QuestGoal)MemberwiseClone();
        }
    }

    /// <summary>1つの依頼（プレイヤーごとのインスタンス：マルチ仕様 §15、クエスト仕様 §17）。</summary>
    [Serializable]
    public class QuestInstance
    {
        public string id;
        public QuestKind kind;
        public string playerId;
        public string residentId;
        public QuestStatus status = QuestStatus.Hinted;
        public List<QuestGoal> goals = new List<QuestGoal>();
        /// <summary>理由の概念（Concepts.COOK など）。発話の「〜だから」に使う。</summary>
        public string reasonConcept;
        /// <summary>理由の目的語（ITEM/NAME など）。</summary>
        public string reasonPatient;
        public string lifeChangeKind;
        public int createdDay;
        public int acceptedDay;
        public int finishedDay;
        /// <summary>期限の日（-1 なし）。発話では TODAY/TOMORROW などの時の言葉で表す。</summary>
        public int deadlineDay = -1;
        public int rewardMoney;
        public string rewardItemId;
        public int rewardItemCount;
        public string rewardSpeciesId;
        // ---- 住民が実際に出す手掛かり（§4.2）----
        public bool showSample;
        public bool showNumber;
        /// <summary>指さす対象（住民ID・施設ID）。</summary>
        public string pointTarget;
        public Expression face = Expression.Worried;
        // ---- 由来 ----
        public string storyId;
        public int storyStage;
        public string sourceId;
        /// <summary>結果：0=未, 1=達成, 2=好みに合った達成, 3=住民が自分で解決, 4=別の反応で達成。</summary>
        public int outcome;
        public string givenItemId;

        public QuestGoal MainGoal => goals.Count > 0 ? goals[0] : null;

        public bool IsOpen => status == QuestStatus.Hinted || status == QuestStatus.Active;

        public bool AllGoalsDone
        {
            get
            {
                foreach (var g in goals) if (!g.IsDone) return false;
                return goals.Count > 0;
            }
        }
    }

    /// <summary>掲示板の紙（星で共通。取った人の依頼になる）。</summary>
    [Serializable]
    public class BoardPaper
    {
        public string id;
        public string residentId;
        public QuestInstance template;
        public int postedDay;
        public int expireDay;
        /// <summary>依頼主が絵を描いたか（描いた時だけ絵を掲示：§6.2）。</summary>
        public bool drewPicture;
        public string pictureItemId;
        /// <summary>行事のお知らせ（取れない紙）。</summary>
        public bool isNotice;
        public string noticeEventId;
        public string takenBy;
    }

    public enum HelpKind
    {
        CarryLoad,    // 重い荷物を運ぶ
        LostItem,     // 道に落ちている物を持ち主へ返す
        LostCreature, // 迷子の生き物を飼い主の所へ
        Laundry       // 雨の日の洗濯物を取り込む
    }

    /// <summary>自発的なお手伝いの「状況」（受注なし：§7）。星で共通。</summary>
    [Serializable]
    public class HelpEvent
    {
        public string id;
        public HelpKind kind;
        public string residentId;
        public string itemId;
        public string speciesId;
        public Vector3 position;
        public Vector3 target;
        public string claimedBy;
        public int day;
        public bool done;
        /// <summary>引っ越し・入居に伴う荷物運びか。</summary>
        public string reasonTag;
        public Color color = Color.white;
    }

    /// <summary>個別ストーリーの進行（プレイヤーごと）。</summary>
    [Serializable]
    public class StoryState
    {
        public string storyId;
        public string playerId;
        public string residentId;
        public string partnerResidentId;
        public int stage;
        public int stageDay;
        public long stageMinute;
        public string itemId;
        public string itemId2;
        public int counter;
        public float value;
        public float value2;
        public int outcome;
        public bool finished;
        public string questId;
        public bool talkedThisStage;
        public List<string> flags = new List<string>();

        public bool HasFlag(string f) => flags.Contains(f);
        public void SetFlag(string f) { if (!flags.Contains(f)) flags.Add(f); }
    }

    public enum VillageEventKind
    {
        Birthday,
        MarketDay,
        StarBandViewing,
        FarewellParty,
        WelcomeParty,
        CookParty,
        NightSky
    }

    [Serializable]
    public class MarketStall
    {
        public string residentId;
        public Vector3 position;
        public List<string> itemIds = new List<string>();
        public List<int> prices = new List<int>();
        public List<int> stock = new List<int>();
    }

    /// <summary>村の行事（星で共通：マルチ仕様 §15.1）。</summary>
    [Serializable]
    public class VillageEvent
    {
        public string id;
        public VillageEventKind kind;
        public string hostResidentId;
        public int day;
        public int startMinute;
        public int endMinute;
        public Vector3 position;
        public bool announced;
        public bool started;
        public bool finished;
        public bool cancelled;
        public List<string> attendees = new List<string>();
        public List<string> gatherers = new List<string>();
        public List<MarketStall> stalls = new List<MarketStall>();
        /// <summary>関連する個別ストーリー（料理会など）。</summary>
        public string storyPlayerId;
    }

    /// <summary>クエストの結果として暮らしに残った変化（§8.2：工房に作品、家に花、ペット等）。</summary>
    [Serializable]
    public class LifeMark
    {
        public string id;
        public string kind;
        public string residentId;
        public string itemId;
        public Vector3 position;
        public Color color = Color.white;
        public int day;
    }

    /// <summary>次に話しかけた時に住民が伝えること（お礼・自分で解決した報告など）。</summary>
    [Serializable]
    public class PendingTalk
    {
        public string id;
        public string playerId;
        public string residentId;
        public string kind;
        public string itemId;
        public string otherResidentId;
        public int day;
    }

    [Serializable]
    public class QuestPlanetSave
    {
        public int version = 1;
        public List<QuestInstance> quests = new List<QuestInstance>();
        public List<BoardPaper> board = new List<BoardPaper>();
        public List<HelpEvent> helps = new List<HelpEvent>();
        public List<StoryState> stories = new List<StoryState>();
        public List<VillageEvent> events = new List<VillageEvent>();
        public List<LifeMark> marks = new List<LifeMark>();
        public List<PendingTalk> pendings = new List<PendingTalk>();
        /// <summary>プレイヤーごとの「最後にお願いを生成した日」。</summary>
        public List<StringIntPair> generatedDay = new List<StringIntPair>();
        public int lastDayProcessed;
        public List<StringIntPair> repairHintDay = new List<StringIntPair>();
        /// <summary>魚ごとの「ふつうの大きさ」（大きい魚の依頼の目安）。</summary>
        public List<StringFloatPair> fishSizes = new List<StringFloatPair>();
    }

    // ---------------- 依頼帳（個人の知識） ----------------

    /// <summary>依頼帳に残す「実際に見聞きした」発話の写し（正解訳は持たない）。</summary>
    [Serializable]
    public class BookUtterance
    {
        public string recordId;
        public string text;
        public List<string> surfaces = new List<string>();
        public Expression expression;
        public List<GestureCue> gestures = new List<GestureCue>();
        public SpeechChannel channel;
        public string speakerId;
        public int day;
        public int minute;
        /// <summary>どの場面か（依頼・お礼・困った反応など）を UI 言語で表す区分（プレイヤーが見た場面の種類）。</summary>
        public string scene;
        /// <summary>音声カード保存用の原本の写し（meaning は保存しない）。</summary>
        public UtteranceRecord record;
    }

    [Serializable]
    public class RequestBookEntry
    {
        public string id;
        public string questId;
        public QuestKind kind;
        public string residentId;
        public Color faceColor = Color.white;
        public AlienLineage lineage;
        public int day;
        public int minute;
        public string placeJa;
        public string placeEn;
        public List<BookUtterance> records = new List<BookUtterance>();
        /// <summary>住民が実際に見せた物（OfferItem の見本）。</summary>
        public List<string> shownItemIds = new List<string>();
        /// <summary>掲示板の絵など、描かれた絵だけ（名前は分からない）。</summary>
        public List<string> drawnItemIds = new List<string>();
        public List<int> shownNumbers = new List<int>();
        /// <summary>指さした先（住民ID・施設ID・"rocket" など）。</summary>
        public List<string> pointedTargets = new List<string>();
        /// <summary>主人公が渡した物（実際の行動の記録）。</summary>
        public List<string> givenItemIds = new List<string>();
        public string playerMemo = "";
        public string playerNickname = "";
        public QuestStatus lastStatus = QuestStatus.Hinted;
        public bool heardOnly;
        public bool starred;
        public bool archived;
    }

    [Serializable]
    public class QuestKnowledgeSave
    {
        public int version = 1;
        public List<RequestBookEntry> entries = new List<RequestBookEntry>();
    }
}
