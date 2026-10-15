using System;
using System.Collections.Generic;

namespace Uchujin.Core
{
    // =====================================================================
    // 宇宙語の「意味」データ（宇宙語仕様 §14：意味と表面の文章を分離）
    //
    // 住民・商店・クエスト・ロケット等は「何を言いたいか」を MeaningFrame で組み立て、
    // ISpeechService（Language モジュール）がその星の語彙・語順・文法で宇宙語の文へ変換する。
    // 日本語の文を単語置換する方式は採らない。
    // 内部の意味は「正解」としてプレイヤーに表示してはならない（辞書はプレイヤーの推測の記録）。
    // =====================================================================

    /// <summary>発話行為。</summary>
    public enum SpeechAct
    {
        Statement,   // 〜だ／〜する
        Question,    // 〜か？（はい・いいえ疑問）
        WhQuestion,  // 何／誰／どこ…（Question 概念を引数に置く）
        Request,     // 〜してほしい（依頼）
        Desire,      // 〜したい／〜が欲しい（希望）
        Greeting,
        Farewell,
        Thanks,
        Apology,
        Offer,       // どうぞ（差し出す）
        Invitation,  // 一緒に〜しよう
        Exclamation  // わあ！など
    }

    public enum Tense
    {
        Present,
        Past,
        Future
    }

    /// <summary>概念の品詞的な分類（語彙生成・文法処理に使う）。</summary>
    public enum ConceptKind
    {
        Noun,
        Pronoun,
        Verb,
        Adjective,
        Adverb,
        TimeWord,
        Place,
        Name,        // 固有名詞（住民名・地名・星の名前）
        Number,
        Interjection,
        QuestionWord,
        Color
    }

    /// <summary>
    /// 文中の名詞句。Concept は概念ID（Concepts の定数、"ITEM:fish.xxx"、"NAME:residentId"、"PLACE:facilityId" など）。
    /// </summary>
    [Serializable]
    public class MeaningArg
    {
        public string concept;
        /// <summary>形容詞（概念ID）。例：Concepts.BLUE, Concepts.BIG</summary>
        public List<string> adjectives = new List<string>();
        /// <summary>数量（0=指定なし）。</summary>
        public int quantity;
        /// <summary>所有者（概念ID）。例：Concepts.I →「私の〜」</summary>
        public string possessor;
        public bool plural;

        public MeaningArg() { }
        public MeaningArg(string concept) { this.concept = concept; }

        public static MeaningArg Of(string concept, params string[] adjectives)
        {
            var a = new MeaningArg(concept);
            if (adjectives != null) a.adjectives.AddRange(adjectives);
            return a;
        }

        public MeaningArg Qty(int n) { quantity = n; return this; }
        public MeaningArg OwnedBy(string possessorConcept) { possessor = possessorConcept; return this; }
        public MeaningArg Plural() { plural = true; return this; }
        public MeaningArg Adj(string adjective) { adjectives.Add(adjective); return this; }

        public MeaningArg Clone()
        {
            return new MeaningArg { concept = concept, adjectives = new List<string>(adjectives), quantity = quantity, possessor = possessor, plural = plural };
        }
    }

    /// <summary>
    /// 1つの文の意味。「誰が(agent) 何を(patient) 誰に(recipient) どこで(location) いつ(time) どうする(predicate)」。
    /// predicate は動詞または形容詞の概念ID（形容詞なら「〜は〜だ」）。
    /// </summary>
    [Serializable]
    public class MeaningFrame
    {
        public SpeechAct act = SpeechAct.Statement;
        public Tense tense = Tense.Present;
        public bool negative;
        public string predicate;
        public MeaningArg agent;
        public MeaningArg patient;
        public MeaningArg recipient;
        public MeaningArg location;
        /// <summary>時の言葉（Concepts.TODAY, TOMORROW 等）。</summary>
        public string timeWord;
        /// <summary>副詞（Concepts.VERY 等）。</summary>
        public List<string> adverbs = new List<string>();
        /// <summary>理由の文（「〜だから」）。入れ子は1段まで推奨。</summary>
        public MeaningFrameLite reason;

        public MeaningFrame() { }

        public static MeaningFrame Make(SpeechAct act, string predicate)
        {
            return new MeaningFrame { act = act, predicate = predicate };
        }

        public MeaningFrame Agent(string concept, params string[] adjectives) { agent = MeaningArg.Of(concept, adjectives); return this; }
        public MeaningFrame Agent(MeaningArg arg) { agent = arg; return this; }
        public MeaningFrame Patient(string concept, params string[] adjectives) { patient = MeaningArg.Of(concept, adjectives); return this; }
        public MeaningFrame Patient(MeaningArg arg) { patient = arg; return this; }
        public MeaningFrame Recipient(string concept) { recipient = MeaningArg.Of(concept); return this; }
        public MeaningFrame At(string placeConcept) { location = MeaningArg.Of(placeConcept); return this; }
        public MeaningFrame When(string timeConcept) { timeWord = timeConcept; return this; }
        public MeaningFrame Past() { tense = Tense.Past; return this; }
        public MeaningFrame Future() { tense = Tense.Future; return this; }
        public MeaningFrame Not() { negative = true; return this; }
        public MeaningFrame Adverb(string adverbConcept) { adverbs.Add(adverbConcept); return this; }
        public MeaningFrame Because(MeaningFrameLite why) { reason = why; return this; }
    }

    /// <summary>理由節などに使う、入れ子にならない簡易フレーム（JsonUtility の循環制限回避）。</summary>
    [Serializable]
    public class MeaningFrameLite
    {
        public Tense tense = Tense.Present;
        public bool negative;
        public string predicate;
        public MeaningArg agent;
        public MeaningArg patient;
        public string timeWord;

        public static MeaningFrameLite Make(string predicate, MeaningArg agent = null, MeaningArg patient = null)
        {
            return new MeaningFrameLite { predicate = predicate, agent = agent, patient = patient };
        }
    }

    /// <summary>表情（言葉以外の手掛かり）。</summary>
    public enum Expression
    {
        Neutral,
        Happy,
        VeryHappy,
        Sad,
        Crying,
        Angry,
        Surprised,
        Confused,
        Shy,
        Sleepy,
        Love,
        Worried,
        Excited,
        Disgusted,
        Thinking
    }

    /// <summary>身振り（住民・マネピが再現する）。</summary>
    public enum Gesture
    {
        None,
        Wave,
        Nod,
        ShakeHead,
        PointAt,      // target を指す
        ShowNumber,   // number を指・触手などで示す
        Jump,
        Bow,
        Shrug,
        Hug,
        OfferItem,    // itemId を差し出す
        Eat,
        Cry,
        Clap,
        Spin,
        Think,
        Beckon,       // 来て
        Shoo,         // あっちへ
        Hold,         // 物を抱える
        LookUp,       // 空を見上げる（天気）
        Shiver,       // 寒い・怖い
        Stretch,      // 伸び・眠い
        Dance
    }

    [Serializable]
    public class GestureCue
    {
        public Gesture gesture;
        /// <summary>ShowNumber の数。</summary>
        public int number;
        /// <summary>PointAt / OfferItem の対象（アイテムID・住民ID・施設IDなど）。</summary>
        public string target;

        public GestureCue() { }
        public GestureCue(Gesture g, string target = null, int number = 0)
        {
            gesture = g;
            this.target = target;
            this.number = number;
        }
    }

    /// <summary>発話チャンネル（どこで聞いたか）。</summary>
    public enum SpeechChannel
    {
        Direct,        // 主人公に直接話しかけた
        Overheard,     // 住民同士の会話・独り言を聞いた
        ManepiPlayback,// マネピが再現した
        Sign,          // 看板・掲示（宇宙人の文字）
        Letter,        // 手紙
        Broadcast      // 村全体への呼びかけ等
    }

    /// <summary>
    /// 1回の発話（複数の文＋表情＋身振り）。話し手が持っている情報・状態と矛盾しない意味で作ること（宇宙語仕様 §5.2）。
    /// </summary>
    [Serializable]
    public class Utterance
    {
        public string speakerId;
        public List<MeaningFrame> sentences = new List<MeaningFrame>();
        public Expression expression = Expression.Neutral;
        public List<GestureCue> gestures = new List<GestureCue>();
        /// <summary>場面のタグ（"shop.purchase" "quest.request" "weather.forecast" "greeting" 等）。記録・検索用。</summary>
        public string contextTag;
        /// <summary>場面に関わる物（アイテムID・住民ID等）。タブレットの資料に「その場にあった物」として残る。</summary>
        public List<string> relatedIds = new List<string>();
        /// <summary>話しかけた相手（プレイヤーIDや住民ID。空なら特定の相手なし）。中継先で「自分に向けた言葉か」を判定する。</summary>
        public string addresseeId;

        public Utterance() { }

        public Utterance(string speakerId, string contextTag = null)
        {
            this.speakerId = speakerId;
            this.contextTag = contextTag;
        }

        public Utterance Say(MeaningFrame frame) { sentences.Add(frame); return this; }
        public Utterance Face(Expression e) { expression = e; return this; }
        public Utterance With(Gesture g, string target = null, int number = 0) { gestures.Add(new GestureCue(g, target, number)); return this; }
        public Utterance Related(string id) { if (!string.IsNullOrEmpty(id)) relatedIds.Add(id); return this; }
    }

    /// <summary>宇宙語の表面形の1語。</summary>
    [Serializable]
    public class SurfaceToken
    {
        /// <summary>表示される綴り（プレイヤーが見るもの）。</summary>
        public string surface;
        /// <summary>内部の概念ID・文法標識ID。**プレイヤーに表示してはならない**（正解情報）。</summary>
        public string internalId;
        /// <summary>文法的な付属語か（否定・時制・格標識など）。</summary>
        public bool grammatical;
        /// <summary>直前の語と区切らずに繋がっているか（接辞）。</summary>
        public bool attachedToPrevious;
    }

    /// <summary>宇宙語へ変換した結果。</summary>
    [Serializable]
    public class RenderedUtterance
    {
        public string text;
        public List<SurfaceToken> tokens = new List<SurfaceToken>();
        /// <summary>文ごとの範囲（tokens のインデックス）。</summary>
        public List<int> sentenceStarts = new List<int>();
    }

    /// <summary>
    /// 実際に聞こえた発話の記録（タブレットの資料・音声カードの原本）。
    /// 原本はプレイヤーの注釈と分けて保存し、後から別の宇宙語に変わらない（タブレット仕様 §5.3, §14）。
    /// </summary>
    [Serializable]
    public class UtteranceRecord
    {
        public string recordId;
        public string speakerId;
        public string speakerKind; // "resident" "manepi" "sign" "player" "creature"
        public string text;
        public List<SurfaceToken> tokens = new List<SurfaceToken>();
        public Expression expression;
        public List<GestureCue> gestures = new List<GestureCue>();
        public SpeechChannel channel;
        public string contextTag;
        public List<string> relatedIds = new List<string>();
        public int day;
        public int minuteOfDay;
        public string areaId;
        public float posX, posY, posZ;
        public string weatherSummary;
        /// <summary>この発話の意味データ（内部用。表示禁止。整合性検証・再生成のためだけに保持）。</summary>
        public Utterance meaning;
    }
}
