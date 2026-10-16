using System;
using System.Collections.Generic;
using Uchujin.Core;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 住民の内部データ（住民生活仕様 §1「内部データの3層」）
    //   1. 基本的な個性（ResidentProfile）：系統・外見・性格・好み・趣味・夢。変化しにくい。
    //      初期住民はワールドシードから決定的に再生成できるので、セーブには入れない（genKey だけ保存）。
    //      引っ越して来た新住民は、その時点の村の状態（空いた職業など）で作られるため、プロフィールごと保存する。
    //   2. 現在の状態（ResidentState）：所持金・持ち物・気分・予定・目標・位置・行動。
    //   3. 経験・記憶（ResidentState.memories / relations / bonds）。
    // すべて JsonUtility で直列化できる形（public フィールド・List・Dictionary 不使用）。
    // =====================================================================

    /// <summary>外見（キャラクターデザイン案 §2：10系統すべてを活用）。</summary>
    [Serializable]
    public class ResidentAppearance
    {
        public AlienLineage lineage;
        /// <summary>ミックス系の2つ目の特徴（Mixed のときだけ使う）。</summary>
        public AlienLineage secondary;
        public Color bodyColor = Color.white;
        public Color accentColor = Color.white;
        public Color eyeColor = new Color(0.12f, 0.12f, 0.2f);
        public Color patternColor = Color.white;
        /// <summary>全体の大きさ倍率。</summary>
        public float size = 1f;
        /// <summary>頭の大きさ倍率。</summary>
        public float headScale = 1f;
        /// <summary>体の縦横比（もちもち・まんまるの差分）。</summary>
        public float squash = 1f;
        /// <summary>耳の形 0=なし 1=丸耳 2=長耳 3=三角耳 4=垂れ耳。</summary>
        public int earType;
        /// <summary>しっぽ 0=なし 1=丸 2=長い 3=ふさふさ。</summary>
        public int tailType;
        /// <summary>触角・角 0=なし 1=触角 2=小さな角 3=月の角。</summary>
        public int antennaType;
        /// <summary>模様 0=なし 1=水玉 2=おなか 3=星。</summary>
        public int pattern;
        /// <summary>小物 0=なし 1=帽子 2=エプロン 3=かばん 4=マフラー 5=眼鏡 6=リボン。</summary>
        public int accessory;
        public Color accessoryColor = Color.white;
        /// <summary>四足歩行（ちび動物系の一部）。</summary>
        public bool fourLegged;
        /// <summary>浮遊する高さ（浮遊系）。</summary>
        public float floatHeight;
        /// <summary>腕の本数（2 or 4。多腕系は4）。</summary>
        public int armCount = 2;
        /// <summary>体が半透明（もちもち系）。</summary>
        public bool translucent;
        /// <summary>感情で光る部位がある（星の子系）。</summary>
        public bool glows;
        /// <summary>目の大きさ倍率（表情豊か系は大きい）。</summary>
        public float eyeScale = 1f;
    }

    /// <summary>性格の軸（キャラデザ §3、商店 §3.2 の購買傾向）。0〜1。</summary>
    [Serializable]
    public class ResidentPersonality
    {
        public float sociability;   // 社交的
        public float curiosity;     // 好奇心
        public float easygoing;     // のんびり
        public float tidiness;      // 几帳面
        public float competitive;   // 負けず嫌い
        public float shyness;       // 照れ屋・人見知り
        public float thrift;        // 節約家（高いと財布の紐が固い）
        public float gluttony;      // 食いしん坊
        public float generosity;    // 気前がいい（物をくれる）
        public float talkativeness; // おしゃべり
        public float mischief;      // いたずら好き
        public float nightOwl;      // 夜型
        /// <summary>雨の好み -1（嫌い）〜1（好き）。</summary>
        public float rainLike;
    }

    [Serializable]
    public class TagWeight
    {
        public string tag;
        public float weight;

        public TagWeight() { }
        public TagWeight(string tag, float weight) { this.tag = tag; this.weight = weight; }
    }

    /// <summary>基本的な個性（第1層）。</summary>
    [Serializable]
    public class ResidentProfile
    {
        public string id;
        /// <summary>生成元のキー（"initial:3" "newcomer:2"）。</summary>
        public string genKey;
        /// <summary>デザイン済み住民のID（ResidentDesigns の "0101" など）。空なら従来のプリミティブの見た目（古いセーブの新住民）。</summary>
        public string designId;
        public AlienLineage lineage;
        public ResidentAge age = ResidentAge.Adult;
        public ResidentJob originalJob;
        public ResidentHobby hobby;
        public ResidentHobby hobby2;
        public ResidentAppearance appearance = new ResidentAppearance();
        public ResidentPersonality personality = new ResidentPersonality();
        /// <summary>タグの好み（-1〜1）。EvaluateItem の根拠（内部の真実。UIには出さない）。</summary>
        public List<TagWeight> likes = new List<TagWeight>();
        /// <summary>カテゴリの好み（ItemCategory 名 → -1〜1）。</summary>
        public List<TagWeight> categoryLikes = new List<TagWeight>();
        /// <summary>好きな色タグ（"color_blue" 等）。</summary>
        public string favoriteColorTag;
        /// <summary>苦手なタグ。</summary>
        public string dislikedTag;
        /// <summary>長期目標（夢）。</summary>
        public GoalKind dream;
        /// <summary>誕生日（BirthdayCycleDays 周期の何日目か）。</summary>
        public int birthdayIndex;
        /// <summary>家族の住民ID（初期生成で決まる。いなければ空）。</summary>
        public string familyId;
        /// <summary>仕事の開始・終了（分）。</summary>
        public int workStartMinute = 9 * 60;
        public int workEndMinute = 17 * 60;
        public int wakeMinute = 7 * 60;
        public int sleepMinute = 22 * 60;
    }

    /// <summary>住民同士の関係（住民生活仕様 §5）。</summary>
    [Serializable]
    public class RelationEntry
    {
        public string otherId;
        public RelationKind kind;
        /// <summary>親しさ -100〜100。</summary>
        public float affinity;
        /// <summary>信頼 0〜100。</summary>
        public float trust;
        /// <summary>相性 -1〜1（性格・趣味から。変化しにくい）。</summary>
        public float compatibility;
        public int lastMetDay = -1;
        /// <summary>喧嘩中ならこの日まで距離を置く（-1=喧嘩していない）。</summary>
        public int quarrelUntilDay = -1;
        public int timesTogether;
    }

    /// <summary>主人公（プレイヤーごと）との関係（住民生活仕様 §6、マルチ仕様 §14.1）。</summary>
    [Serializable]
    public class PlayerBond
    {
        public string playerId;
        public int friendship;
        public bool met;
        public int firstMetDay = -1;
        public int lastTalkDay = -1;
        public int totalTalks;
        public int lastGiftDay = -1;
        public int giftsToday;
        public int totalGifts;
        public int lastGestureBonusDay = -1;
        public int lastSharedActivityDay = -1;
        public int lastOfferDay = -1;
        public int farewellGiftDay = -1;
        /// <summary>引っ越しを引き留められた（首を振られた）日。</summary>
        public int persuadedDay = -1;
        /// <summary>引っ越しを応援された（うなずかれた）日。</summary>
        public int encouragedDay = -1;
        public int lastObservationDay = -1;
        public string lastObservationKey;
    }

    /// <summary>中期・長期目標の進み具合（住民生活仕様 §3）。</summary>
    [Serializable]
    public class GoalState
    {
        public GoalKind kind;
        /// <summary>関係する物のタグ（家具のタグ、料理の材料など）。</summary>
        public string tag;
        public float progress;
        public float target = 1f;
        public int startedDay;
        public int completedCount;

        public bool IsDone => kind != GoalKind.None && progress >= target;
        public float Ratio => target <= 0 ? 1 : Mathf.Clamp01(progress / target);
    }

    /// <summary>1日の予定の1区間（住民生活仕様 §2）。</summary>
    [Serializable]
    public class ScheduleBlock
    {
        public int start;   // 分（0〜1440）
        public int end;
        public ResidentActivity activity;
        /// <summary>行き先の種類："home" "work" "facility" "spot" "area" "resident" "shop" "pos" "player_home"。</summary>
        public string destKind;
        /// <summary>行き先のID（施設ID・住民ID・釣り場ID など）。</summary>
        public string destId;
        public float dx, dy, dz;
        /// <summary>補足（買い物の目的、約束の相手など）。</summary>
        public string note;

        public Vector3 DestPos => new Vector3(dx, dy, dz);
    }

    /// <summary>住民同士の約束（「明日一緒に釣りに行こう」）。会話の意味と行動を一致させる。</summary>
    [Serializable]
    public class Promise
    {
        public string partnerId;
        public int day;
        public ResidentActivity activity;
        public string placeId;
        /// <summary>待ち合わせの時刻（分）。2人とも同じ時刻にする。</summary>
        public int start;
        public int end;
    }

    /// <summary>現在の状態（第2層）＋記憶（第3層）。</summary>
    [Serializable]
    public class ResidentState
    {
        public string id;
        public bool active = true;
        public int movedInDay;
        public int movedOutDay = -1;
        public ResidentJob job;
        /// <summary>弟子として引き継ぐ予定の職業（None=なし）。</summary>
        public ResidentJob apprenticeFor = ResidentJob.None;
        public string masterId;
        public int homePlotIndex = -1;
        public string homeFacilityId;

        // ---- お金・持ち物 ----
        public int money;
        public List<ItemStack> items = new List<ItemStack>();
        /// <summary>家に飾っている物（家具・花・宝石・記念の魚など）。</summary>
        public List<string> homeItems = new List<string>();

        // ---- 欲求・気分 ----
        public float mood;      // -1〜1
        public float hunger;    // 0〜1
        public float social;    // 0〜1（人恋しさ）

        // ---- 位置・行動（ホストが確定し、ゲストへ同期） ----
        public float px, py, pz, rotY;
        public ResidentActivity activity = ResidentActivity.AtHome;
        public string activityTarget;
        public string chatPartnerId;
        public Expression face;
        public bool hidden;
        public bool carrying;
        public string heldItemId;

        // ---- 予定 ----
        public int planDay = -1;
        public List<ScheduleBlock> plan = new List<ScheduleBlock>();
        public int blockIndex = -1;
        public bool arrived;
        public bool blockActionDone;
        /// <summary>
        /// 到着時の行動（買い物・食事・訪問など）を済ませた予定の識別（日:開始分:行動）。
        /// 会話・RequestGoTo・手招き・雨宿りの後に同じ予定をやり直しても、買い物などを二重に行わないため。
        /// </summary>
        public string actionDoneKey;
        public List<Promise> promises = new List<Promise>();

        // ---- 目標 ----
        public string shortGoal;
        public GoalState midGoal = new GoalState();
        public GoalState longGoal = new GoalState();

        // ---- 関係・記憶 ----
        public List<RelationEntry> relations = new List<RelationEntry>();
        public List<PlayerBond> bonds = new List<PlayerBond>();
        public List<ResidentMemoryEntry> memories = new List<ResidentMemoryEntry>();

        // ---- 生活の痕跡（会話の話題の裏付け） ----
        public string lastBoughtItemId;
        public int lastBoughtDay = -1;
        public bool lastBoughtFromPlayer;
        public float lastBoughtSatisfaction;
        public string lastAteItemId;
        public int lastAteDay = -1;
        public float lastAteLiking;
        public string lastCaughtItemId;
        public int lastCaughtDay = -1;
        public int forecastKnownDay = -1;
        public int lastShopVisitDay = -1;
        public int lastWorkDay = -1;
        public int workDaysTotal;
        public int fishingDays;

        // ---- 引っ越し ----
        public int moveOutDecidedDay = -1;
        public int moveOutDay = -1;
        public MoveReason moveReason;
        public bool moveNoticePosted;
        public int lettersSent;
        public int nextLetterDay = -1;
        /// <summary>引っ越した住民が遊びに戻ってくる日（-1=なし）。</summary>
        public int visitDay = -1;

        // ---- 一時的な指示 ----
        public bool hasGoTo;
        public float goX, goY, goZ;
        public long goUntil;
        public string goReason;
        public string followPlayerId;
        public long followUntil;
        /// <summary>会話中などで行動を止める（総分）。</summary>
        public long holdUntil;
        public string holdForPlayerId;

        public int birthdayCelebratedDay = -1;
        /// <summary>演出（プロローグ・エンディング）のため、生活AI・移動・自動の発話を止めている。</summary>
        public bool cutsceneHold;
    }

    /// <summary>村の住民が知っている主人公の評判（住民生活仕様 §7）。出来事の回数だけを数える。</summary>
    [Serializable]
    public class PlayerReputation
    {
        public string playerId;
        public int fishCount;
        public int rareFishCount;
        public int fishDays;
        public int lastFishDay = -1;
        public int oreCount;
        public int mineDays;
        public int lastMineDay = -1;
        public int mineStreak;
        public int deepestFloor;
        public int harvestCount;
        public int bigCropCount;
        public int lastHarvestDay = -1;
        public int creatureCount;
        public int rareCreatureCount;
        public int shopSales;
        public int shopSalesRecentDay = -1;
        public int shopSalesRecent;
        public int repairs;
        public int giftsGiven;
        public int firstSeenDay = -1;
    }

    [Serializable]
    public class ResidentSaveEntry
    {
        public string id;
        public string genKey;
        /// <summary>シードから再生成できるか。false ならプロフィールを保存している（新住民など）。</summary>
        public bool regenerable;
        public ResidentProfile profile;
        public ResidentState state;
    }

    [Serializable]
    public class ResidentsSaveData
    {
        public int version = 1;
        public List<ResidentSaveEntry> residents = new List<ResidentSaveEntry>();
        public int newcomerCounter;
        public int nextMoveInDay = -1;
        public int lastMoveOutDay = -999;
        public int moveOutsTotal;
        public int moveInsTotal;
        public List<PlayerReputation> reputation = new List<PlayerReputation>();
        public int lastSimDay = -1;
        public int lastWeatherForecastDay = -1;
        /// <summary>接続していないプレイヤーへ後で届ける記録（手紙・観察）。参加した時にホストが届ける。</summary>
        public List<PendingPlayerRecord> pendingRecords = new List<PendingPlayerRecord>();
    }

    /// <summary>その時に接続していなかったプレイヤーへ、次に参加した時に届ける記録。</summary>
    [Serializable]
    public class PendingPlayerRecord
    {
        public string playerId;
        /// <summary>"letter"（引っ越した住民からの手紙）／"observation"（住民辞典の観察記録）。</summary>
        public string kind;
        public string residentId;
        public int day;
        public int minuteOfDay;
        public string factJa;
        public string factEn;
    }

    // =====================================================================
    // 同期専用の軽い状態（副キー "residents.live"）。セーブ・参加時のスナップショットには含めない。
    // 位置・向き・行動・表示・表情など見た目に必要な最小限だけを、ホストが約0.5秒ごとに送る。
    // フィールド名は通信量を減らすため短くしている。
    // =====================================================================

    [Serializable]
    public class ResidentLiveEntry
    {
        public string id;
        /// <summary>位置と向き。</summary>
        public float x, y, z, r;
        /// <summary>歩いている時の次の経由点（ゲストの補間の先読み）。</summary>
        public float tx, tz;
        /// <summary>行動（ResidentActivity）。</summary>
        public int a;
        /// <summary>基本の表情（Expression）。</summary>
        public int f;
        /// <summary>ビットフラグ（ResidentLiveFlags）。</summary>
        public int fl;
        /// <summary>立ち話の相手。</summary>
        public string c;
        /// <summary>手に持っている物。</summary>
        public string h;
        /// <summary>家の中の代理の姿がいるエリア（ホストで代理を表示中の時だけ。空なら外）。</summary>
        public string ar;
    }

    public static class ResidentLiveFlags
    {
        public const int Hidden = 1;
        public const int Carrying = 2;
        public const int Moving = 4;
        public const int CutsceneHold = 8;
        public const int GoTo = 16;
    }

    /// <summary>ホストで住民の AI が見せた表情・身振り（発話を伴わないもの）。ゲストでも再生する。</summary>
    [Serializable]
    public class ResidentCueEntry
    {
        public int s;
        public string id;
        /// <summary>0 = 表情、1 = 身振り。</summary>
        public int k;
        /// <summary>Expression または Gesture の値。</summary>
        public int v;
        public string t;
        public int n;
        public float d;
    }

    [Serializable]
    public class ResidentsLiveData
    {
        public List<ResidentLiveEntry> e = new List<ResidentLiveEntry>();
        public List<ResidentCueEntry> q = new List<ResidentCueEntry>();
    }
}
