using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Tablet
{
    // =====================================================================
    // タブレットのセーブ用データ（JsonUtility で直列化するため public フィールドのみ・Dictionary 不可）。
    //
    // スコープの分け方（マルチ仕様 §5、タブレット仕様 §12.2, §14）：
    //   ・KnowledgeData  … SaveScope.PlayerPlanetKnowledge（星ごと・プレイヤー個人）。辞書・音声カード・資料・住民辞典・写真。
    //   ・ManepiPlanetData … SaveScope.Planet（星の状態）。マネピが預かっている伝言。
    //
    // 原則（タブレット仕様 §5.3, §8.3, §14.1）：
    //   原資料（聞いた発話のコピー・観察できた事実）と、プレイヤーの解釈（推測・メモ）は別のクラスに分けて保存する。
    //   ゲームが解釈の欄を正解へ書き換えることはない。
    // =====================================================================

    /// <summary>本人用の確信度マーク（宇宙語仕様 §7.2「自信の印」）。ゲームは正誤を判定しない。</summary>
    public enum ConfidenceMark
    {
        Hypothesis, // 仮説
        Probably,   // たぶん
        Confident,  // 自信あり
        Unsure,     // ？（よく分からない）
        Rejected    // 否定された（自分で違うと思った。消さずに残す §6.1）
    }

    /// <summary>辞書の記録単位（タブレット仕様 §6.2）。</summary>
    public enum DictEntryKind
    {
        Word,   // 単語（聞こえた語のまとまり）
        Part,   // 語の一部（接頭辞・接尾辞らしき音）
        Phrase  // 文章・言い回し
    }

    /// <summary>意味の候補（1語に複数並べられる §6.1）。</summary>
    [Serializable]
    public class Hypothesis
    {
        public string id;
        public string text;
        public ConfidenceMark mark = ConfidenceMark.Hypothesis;
        /// <summary>他のプレイヤーから受け取った仮説なら、その人の名前（マルチ仕様 §13.3「誰から受け取った情報か」）。</summary>
        public string source;
        public int day;
    }

    /// <summary>証拠リンク。kind = "card"（音声カード） / "material"（資料ボックス）。</summary>
    [Serializable]
    public class EvidenceLink
    {
        public string kind;
        public string refId;
        /// <summary>プレイヤーの一言メモ（「値下げ後は別の語だった」など）。</summary>
        public string note;
        public int day;
    }

    /// <summary>宇宙語辞書の1ページ（表面形ごと）。すべてプレイヤーの記述（§6.1）。</summary>
    [Serializable]
    public class DictEntry
    {
        public string id;
        /// <summary>綴り（聞こえた音の表記）。</summary>
        public string surface;
        public DictEntryKind kind = DictEntryKind.Word;
        public List<Hypothesis> hypotheses = new List<Hypothesis>();
        /// <summary>推測訳の補助表示に使う仮説（本人が選んだもの §6.5）。</summary>
        public string glossHypothesisId;
        /// <summary>品詞の推測（本人の選択。"noun" "verb" ...）。</summary>
        public string posGuess;
        public string memo = "";
        public List<string> tags = new List<string>();
        /// <summary>関連語（綴り）。</summary>
        public List<string> related = new List<string>();
        public List<EvidenceLink> evidence = new List<EvidenceLink>();
        /// <summary>他のプレイヤーから受け取った例文など（受け取った内容は正誤判定しない）。</summary>
        public List<string> examples = new List<string>();
        /// <summary>本人の編集の足跡（「なぜ当時こう考えたのか」を振り返る §5.3）。</summary>
        public List<string> history = new List<string>();
        public int createdDay;
        public int updatedDay;
        public bool pinned;
        /// <summary>統合で取り込んだ時の送り主（自分で作ったページなら空）。</summary>
        public string importedFrom;
    }

    /// <summary>文法研究ページ（§6.4）。自由記述＋2つの記録の並列比較。</summary>
    [Serializable]
    public class GrammarPage
    {
        public string id;
        public string title = "";
        public string body = "";
        /// <summary>根拠として貼った音声カード。</summary>
        public List<string> cardIds = new List<string>();
        /// <summary>比較する2枚の音声カード。</summary>
        public string compareA;
        public string compareB;
        /// <summary>プレイヤーが付けた印。key = "A" / "B"、value = 語の位置。</summary>
        public List<StringIntPair> marks = new List<StringIntPair>();
        public int createdDay;
        public int updatedDay;
    }

    /// <summary>受け取った辞書の別冊（読み取り専用。自分の辞書と混ぜない §12.3）。</summary>
    [Serializable]
    public class DictVolume
    {
        public string id;
        public string fromPlayerName;
        public int receivedDay;
        public string sourceWorldId;
        public List<DictEntry> entries = new List<DictEntry>();
        public List<GrammarPage> pages = new List<GrammarPage>();
    }

    /// <summary>辞書の書き出し形式（マルチでの任意の手渡し用）。</summary>
    [Serializable]
    public class DictionaryExportData
    {
        public int format = 1;
        public string worldId;
        public string exporterName;
        public int exportedDay;
        public List<DictEntry> entries = new List<DictEntry>();
        public List<GrammarPage> pages = new List<GrammarPage>();
    }

    /// <summary>顔付き音声カード（§5）。original は聞いた発話の原本コピーで、保存後に変更しない。</summary>
    [Serializable]
    public class VoiceCard
    {
        public string id;
        public UtteranceRecord original;
        /// <summary>どこから保存したか（「マネピ（商店）」「会話ログ」など。主人公側の記録）。</summary>
        public string sourceJa;
        public string sourceEn;
        public string boardId;
        public int savedDay;
        public int savedMinute;
    }

    /// <summary>音声カードへのプレイヤーの注釈（原本とは別に保存 §5.3）。</summary>
    [Serializable]
    public class CardNote
    {
        public string cardId;
        /// <summary>推測した意味（「高い？」など）。</summary>
        public string guessedMeaning = "";
        /// <summary>住民に関する推測（「値段に厳しい？」など）。</summary>
        public string residentGuess = "";
        public string memo = "";
        public List<string> tags = new List<string>();
        public bool pinned;
        /// <summary>プレイヤーが「この声は◯◯だと思う」と結び付けた住民（空なら表示上は原本の顔のみ）。</summary>
        public string playerSpeakerId;
    }

    /// <summary>資料ボックスの資料へのプレイヤーのメモと関連付け（§8.3）。</summary>
    [Serializable]
    public class MaterialNote
    {
        public string materialId;
        public string memo = "";
        public List<string> residentIds = new List<string>();
        public List<string> surfaces = new List<string>();
    }

    /// <summary>住民辞典に自動で入る「観察できた事実」（§7.3）。解釈は含めない。</summary>
    [Serializable]
    public class ResidentObservation
    {
        public int day;
        public int minute;
        public string factJa;
        public string factEn;
        public string linkedRecordId;
        /// <summary>"met" "observed" "purchase" "gift" "quest" "moved_out" "moved_in" "photo" など。</summary>
        public string kind;
    }

    /// <summary>住民辞典の1ページ（§7.2）。内部の名前・性格は書かない。</summary>
    [Serializable]
    public class ResidentPage
    {
        public string residentId;
        /// <summary>辞典の通し番号（「住民 No.014」など、名前が分からない間の識別 §8.5）。</summary>
        public int number;
        /// <summary>プレイヤーが付けたあだ名。</summary>
        public string nickname = "";
        /// <summary>聞き取った名前のメモ（宇宙語の綴りをプレイヤーが書く）。</summary>
        public string heardName = "";
        /// <summary>初めて会った日（-1 ならまだ直接会っていない：商店の記録などだけがある）。</summary>
        public int metDay = -1;
        public int metMinute;
        public string metPlaceJa;
        public string metPlaceEn;
        /// <summary>顔アイコン用の色（引っ越し後も表示できるよう保存）。</summary>
        public float colorR = 0.8f, colorG = 0.8f, colorB = 0.9f;
        public int lineage;
        public bool movedOut;
        public int movedOutDay;
        // ---- プレイヤーが書く欄（推測） ----
        public string job = "";
        public string personality = "";
        public string likes = "";
        public string dislikes = "";
        public string routine = "";
        public string memo = "";
        public List<string> tags = new List<string>();
        // ---- 事実と貼った資料 ----
        public List<ResidentObservation> observations = new List<ResidentObservation>();
        public List<string> attachedCardIds = new List<string>();
        public List<string> attachedMaterialIds = new List<string>();
        public List<string> photoIds = new List<string>();
    }

    /// <summary>人間関係図の線（§7.5）。ラベルはプレイヤーの自由記入。</summary>
    [Serializable]
    public class RelationLink
    {
        public string id;
        public string a;
        public string b;
        public string label = "";
        public int colorIndex;
    }

    [Serializable]
    public class DiagramNode
    {
        public string residentId;
        public float x;
        public float y;
    }

    /// <summary>タブレットのカメラで撮った写真（§7.4, §11.3）。画像は persistentDataPath 配下の PNG。</summary>
    [Serializable]
    public class PhotoInfo
    {
        public string id;
        public string fileName;
        public int day;
        public int minute;
        public string areaId;
        public string placeJa;
        public string placeEn;
        /// <summary>撮影時に写っていた住民（事実）。</summary>
        public List<string> residentIds = new List<string>();
        public string caption = "";
    }

    /// <summary>星ごと・プレイヤー個人の知識（SaveScope.PlayerPlanetKnowledge）。</summary>
    [Serializable]
    public class KnowledgeData
    {
        public int version = 1;
        public string worldId;
        public List<DictEntry> dictionary = new List<DictEntry>();
        public List<GrammarPage> grammarPages = new List<GrammarPage>();
        public List<DictVolume> volumes = new List<DictVolume>();
        public List<VoiceCard> cards = new List<VoiceCard>();
        public List<CardNote> cardNotes = new List<CardNote>();
        public List<TabletMaterial> materials = new List<TabletMaterial>();
        public List<MaterialNote> materialNotes = new List<MaterialNote>();
        public List<ResidentPage> residents = new List<ResidentPage>();
        public List<RelationLink> relations = new List<RelationLink>();
        public List<DiagramNode> diagramNodes = new List<DiagramNode>();
        public List<PhotoInfo> photos = new List<PhotoInfo>();
        /// <summary>自分が再生して聞いたマネピの伝言（新着表示用）。</summary>
        public List<string> heardMessageIds = new List<string>();
        /// <summary>
        /// 資料・住民辞典の観察に結び付いた発話の原本の写し（AddMaterialFor で届いたもの等）。
        /// 言語モジュールの会話ログ（ISpeechService.GetRecord）から消えても、音声カード化・辞書の証拠に使えるよう自前で持つ。
        /// </summary>
        public List<UtteranceRecord> receivedRecords = new List<UtteranceRecord>();
        public int nextResidentNumber = 1;
    }

    /// <summary>
    /// 個人への記録の一時メッセージ（チャンネル <see cref="TabletSystem.RecordChannel"/>。保存しない）。
    /// ホストで起きた事実（商店の委託品の売上・依頼の達成・贈り物など）を、ゲスト本人のタブレットへ届ける。
    /// JsonUtility は null の入れ子クラスも既定値で書き出すため、有無は has* で表す。
    /// </summary>
    [Serializable]
    public class TabletRecordMessage
    {
        public bool hasMaterial;
        public TabletMaterial material;
        /// <summary>資料・観察に結び付いた発話の原本（受信側の会話ログに無いことが多いので一緒に送る）。</summary>
        public bool hasRecord;
        public UtteranceRecord record;
        /// <summary>住民辞典の観察（観察できた事実だけ）。</summary>
        public bool hasObservation;
        /// <summary>観察の種類（"observed" "purchase" "gift" "quest" など）。</summary>
        public string observationKind;
        public string residentId;
        public string factJa;
        public string factEn;
        public string linkedRecordId;
        public int day;
        public int minute;
        /// <summary>はじめて話した住民（受信側で住民辞典のページを作る。場所は受信側の主人公の位置）。</summary>
        public string metResidentId;
    }

    /// <summary>マネピの伝言板1つ分。</summary>
    [Serializable]
    public class ManepiBoardData
    {
        public string boardId;
        public List<UtteranceRecord> messages = new List<UtteranceRecord>();
    }

    /// <summary>星の状態として保存するマネピの伝言（SaveScope.Planet。ホストが正本）。</summary>
    [Serializable]
    public class ManepiPlanetData
    {
        public List<ManepiBoardData> boards = new List<ManepiBoardData>();
    }

    /// <summary>マネピの伝言を消す（自宅のマネピなど。星の状態を変えるのでホスト権威のコマンド）。</summary>
    [Serializable]
    public class ManepiDeleteMessageCommand : WorldCommand
    {
        public string boardId;
        public string recordId;
    }
}
