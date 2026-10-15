using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    // =====================================================================
    // モジュール間の連携契約。各モジュールの System クラスが該当インターフェースを実装し、
    // 他モジュールは Game.Service<IXxx>() で取得して呼ぶ（未実装なら null を返すので null チェックすること）。
    // =====================================================================

    // ---------------- 言語（Language モジュール） ----------------

    public interface ISpeechService
    {
        /// <summary>意味データをこの星の宇宙語へ変換する。</summary>
        RenderedUtterance Render(Utterance utterance);
        /// <summary>概念1語の表面形（住民名 "NAME:id"、地名 "PLACE:id"、星の名前など）。</summary>
        string RenderWord(string conceptId);
        /// <summary>住民の宇宙語の名前。</summary>
        string ResidentName(string residentId);
        /// <summary>その星の呼び名（宇宙語）。</summary>
        string PlanetName { get; }
        /// <summary>話す：頭上に吹き出しを出し、聞こえた範囲なら記録して UtteranceSpokenEvent を発行する。</summary>
        UtteranceRecord Speak(Utterance utterance, Transform speaker, SpeechChannel channel, string speakerKind = "resident");
        /// <summary>
        /// relayToGuests=false の時、ホストでもゲストへ中継しない（各端末がそれぞれ生成する独り言・立ち話など、二重表示を避けたい発話用）。
        /// </summary>
        UtteranceRecord Speak(Utterance utterance, Transform speaker, SpeechChannel channel, string speakerKind, bool relayToGuests);
        /// <summary>表示せずに記録だけ作る（マネピが預かる伝言、手紙、看板など）。</summary>
        UtteranceRecord CreateRecord(Utterance utterance, SpeechChannel channel, string speakerKind, Vector3 position);
        /// <summary>既存記録をもう一度吹き出し表示する（マネピの再生など）。原本は変えない。</summary>
        void Replay(UtteranceRecord record, Transform at);
        /// <summary>表示の扱いを指定して再生（例：マネピの再現として見せる）。原本は変えない。</summary>
        void Replay(UtteranceRecord record, Transform at, SpeechChannel shownAs);
        /// <summary>宇宙人の書き文字（看板・手紙・掲示用）。聞こえる読み方とは別の表記（タブレット仕様 §3）。</summary>
        string RenderScript(RenderedUtterance rendered);
        /// <summary>看板などで宇宙文字に読み方を添えるか（設定）。</summary>
        bool ShowScriptReading { get; }
        /// <summary>最近聞いた発話（会話ログ）。</summary>
        IReadOnlyList<UtteranceRecord> RecentRecords { get; }
        UtteranceRecord GetRecord(string recordId);
        /// <summary>数値を宇宙語の数詞で表す（星ごとに数の体系が異なる場合がある）。</summary>
        string RenderNumber(int n);
    }

    // ---------------- タブレット（Tablet モジュール） ----------------

    /// <summary>タブレットのアプリ。各モジュールが自分の記録画面（図鑑・釣果記録・宇宙船診断など）を登録する。</summary>
    public interface ITabletApp
    {
        string AppId { get; }
        string Title { get; }
        /// <summary>ホーム画面での並び順。</summary>
        int Order { get; }
        /// <summary>ホームに表示するアイコン用の1〜2文字。</summary>
        string IconGlyph { get; }
        Color IconColor { get; }
        bool IsAvailable { get; }
        void OnOpen();
        /// <summary>
        /// IMGUI で描画（GUILayout 可）。タブレット側が GUILayout.BeginArea を済ませた中で、(0,0) 起点のローカル矩形を渡す。
        /// アプリ側でさらに GUILayout.BeginArea(area) してもよい（ローカル矩形なので位置はずれない）。
        /// </summary>
        void Draw(Rect area);
    }

    public enum TabletMaterialKind
    {
        VoiceCard,     // 顔付き音声カード（マネピの再現・会話の録音）
        Conversation,  // 会話の記録
        Photo,
        SalesRecord,   // 売上・購入の事実
        QuestRequest,  // 依頼帳
        Letter,
        Sign,
        Recipe,
        Forecast,      // 天気予報の伝言
        Note
    }

    [Serializable]
    public class TabletMaterial
    {
        public string id;
        public TabletMaterialKind kind;
        public string titleJa;
        public string titleEn;
        /// <summary>事実として観察できた内容（例：「◯◯が青い魚を120Gで購入」）。正解の訳は書かない。</summary>
        public string factJa;
        public string factEn;
        public int day;
        public int minuteOfDay;
        public string linkedRecordId;
        public string linkedResidentId;
        public List<string> relatedIds = new List<string>();
        public List<string> tags = new List<string>();
    }

    public interface ITabletService
    {
        /// <summary>アプリを登録（同じ AppId は置き換え）。</summary>
        void RegisterApp(ITabletApp app);
        void UnregisterApp(string appId);
        /// <summary>住民辞典の、その住民のページを開く。</summary>
        void OpenResidentPage(string residentId);
        /// <summary>辞書に書いた単語（表面形）の一覧。</summary>
        IReadOnlyList<string> GetDictionarySurfaces();
        /// <summary>プレイヤーが選んだ推測訳の一覧（表面形 → 訳）。エンディングで辞書を映す等に使う。</summary>
        IReadOnlyList<KeyValuePair<string, string>> GetPlayerGlossEntries();
        void Open(string appId = null);
        void Close();
        bool IsOpen { get; }
        /// <summary>発話記録を音声カードとして保存（原本は変更しない）。カードIDを返す。</summary>
        string SaveVoiceCard(UtteranceRecord record, string sourceLabelJa, string sourceLabelEn);
        /// <summary>資料ボックスへ追加（重複IDは無視）。</summary>
        void AddMaterial(TabletMaterial material);
        /// <summary>住民辞典に「観察できた事実」を記録（内部の好み・正解は書かない）。</summary>
        void RecordResidentObservation(string residentId, string factJa, string factEn, string linkedRecordId = null);
        void RecordResidentMet(string residentId);
        /// <summary>
        /// 特定のプレイヤーの資料ボックスへ記録する（マルチ：ホストで起きた事実をゲスト本人のタブレットへ届ける）。
        /// playerId がローカルなら AddMaterial と同じ。linkedRecord を渡すと、その発話の原本も一緒に届く。
        /// </summary>
        void AddMaterialFor(string playerId, TabletMaterial material, UtteranceRecord linkedRecord = null);
        /// <summary>特定のプレイヤーの住民辞典へ観察した事実を記録する（ローカルなら RecordResidentObservation と同じ）。</summary>
        void RecordResidentObservationFor(string playerId, string residentId, string factJa, string factEn, string linkedRecordId = null);
        /// <summary>プレイヤー自身が辞書に書いた推測訳（無ければ null）。ゲームが正解へ書き換えることはない。</summary>
        string GetPlayerGloss(string surface);
        /// <summary>吹き出しに推測訳を併記する設定。</summary>
        bool ShowGlossOverlay { get; }
        /// <summary>辞書の書き出し（マルチでの任意の手渡し用）。onlySurfaces が null なら全部。</summary>
        string ExportDictionary(IList<string> onlySurfaces = null);
        /// <summary>受け取った辞書を取り込む（asSeparateVolume=true なら別冊として保存し、自分の記述と混ぜない）。</summary>
        void ImportDictionary(string json, string fromPlayerName, bool asSeparateVolume);
    }

    // ---------------- マネピ（伝言生物）の伝言板（Tablet モジュールが実装） ----------------

    /// <summary>
    /// 伝言生物マネピ（No.033）が声・表情・仕草を記憶して再現する仕組み（タブレット仕様 §4）。
    /// 伝言板ID："plaza"（伝言広場）、"shop"（主人公の商店）、"home:&lt;playerId&gt;"（自宅）など。
    /// 全会話を自動記録する存在ではない。住民が預けた伝言・商店での反応などを覚える。
    /// </summary>
    public interface IManepiService
    {
        void LeaveMessage(string boardId, UtteranceRecord record);
        IReadOnlyList<UtteranceRecord> GetMessages(string boardId);
        void ClearMessagesOlderThan(string boardId, int day);
        /// <summary>マネピの見た目と操作（再生・音声カードとして保存）を配置する。</summary>
        GameObject SpawnManepi(string boardId, Transform parent, Vector3 localPosition, string labelJa, string labelEn);
        /// <summary>伝言の一覧・再生画面を開く。</summary>
        void OpenBoard(string boardId, PlayerState player);
    }

    // ---------------- 住民（Residents モジュール） ----------------

    /// <summary>かわいい宇宙人デザイン10系統（キャラクターデザイン案 v0.1：全案を活用）。</summary>
    public enum AlienLineage
    {
        Round,        // 01 まんまるマスコット系
        Mochi,        // 02 もちもちスライム系
        Plush,        // 03 ふわふわぬいぐるみ系
        ChibiAnimal,  // 04 ちび動物×宇宙人系
        Floating,     // 05 ぷかぷか浮遊系
        MultiArm,     // 06 四つ腕・多腕系
        StarChild,    // 07 星の子・天体系
        Lifestyle,    // 08 生活感のある小物重視系
        Expressive,   // 09 表情・リアクション重視系
        Mixed         // 10 ミックス・派生系
    }

    public enum ResidentJob
    {
        None,
        Blacksmith,       // 鍛冶屋（ツルハシ強化・金属加工）
        Mechanic,         // 修理屋（ロケット修理・精密部品）
        SeedSeller,       // 種屋
        FishingMaster,    // 釣り名人
        FarmingMaster,    // 農業名人
        CreatureExpert,   // 生き物名人
        Carpenter,        // 家具職人・増築
        Cook,             // 料理人
        Merchant,         // 雑貨屋
        WeatherReader,    // 天気読み（マネピに予報を預ける）
        Florist,          // 花の世話
        Courier,          // 配達・伝言係
        Miner,
        Builder,
        Artist,
        Musician,
        Scholar,
        Doctor,
        Teacher,
        Child,
        Retired,
        Traveler          // 旅に憧れる
    }

    public enum ResidentActivity
    {
        Sleeping,
        AtHome,
        Working,
        Shopping,
        Walking,
        Eating,
        Chatting,
        Fishing,
        Farming,
        Gardening,
        Exploring,
        Visiting,
        AtPlaza,
        Resting,
        Cooking,
        Packing,          // 引っ越し準備
        Celebrating,
        ShelteringFromRain,
        WatchingSky,
        CatchingCreatures,
        Mining,
        Crafting,
        Playing
    }

    /// <summary>住民1人の読み取り専用情報。</summary>
    public interface IResidentInfo
    {
        string Id { get; }
        AlienLineage Lineage { get; }
        ResidentJob Job { get; }
        /// <summary>村に住んでいるか（引っ越し済みなら false）。</summary>
        bool IsActive { get; }
        Vector3 Position { get; }
        Transform Transform { get; }
        ResidentActivity Activity { get; }
        int Money { get; }
        string HomeFacilityId { get; }
        int MovedInDay { get; }
        /// <summary>アイテムへの好み -1〜1（内部の真実。UIに直接表示してはならない）。</summary>
        float EvaluateItem(ItemDefinition item);
        /// <summary>主人公（プレイヤー個人）との親しさ 0〜100。マルチではプレイヤーごと。</summary>
        int GetFriendship(string playerId);
        /// <summary>そのプレイヤーを知っているか（会ったことがあるか）。</summary>
        bool Knows(string playerId);
        /// <summary>見た目の主な色（UIの顔アイコン等）。</summary>
        Color MainColor { get; }
    }

    [Serializable]
    public class ResidentMemoryEntry
    {
        /// <summary>"gift" "helped" "bought_from_shop" "fished_together" "visited_home" "quest_done" "moved_in" "saw_repair" など。</summary>
        public string kind;
        public string playerId;
        public string otherResidentId;
        public string itemId;
        public int day;
        public float importance = 0.5f;
        public string detail;
    }

    /// <summary>住民に話しかけた時のメニュー項目を追加する（鍛冶屋の強化、種屋、釣りの技、修理依頼、増築、家への招待など）。</summary>
    public interface IResidentInteractionProvider
    {
        IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player);
    }

    public class ResidentInteractionOption
    {
        public string LabelJa;
        public string LabelEn;
        public int Order = 100;
        public Action Execute;
        public string Label => Loc.T(LabelJa, LabelEn);

        public ResidentInteractionOption(string ja, string en, Action execute, int order = 100)
        {
            LabelJa = ja;
            LabelEn = en;
            Execute = execute;
            Order = order;
        }
    }

    public interface IResidentDirectory
    {
        IEnumerable<IResidentInfo> ActiveResidents { get; }
        IEnumerable<IResidentInfo> FormerResidents { get; }
        IResidentInfo Get(string residentId);
        /// <summary>その職業の住民（引っ越しで不在なら後継者。必須サービスは失われない）。</summary>
        IResidentInfo FindByJob(ResidentJob job);
        bool TrySpend(string residentId, int amount, string reason);
        void GiveMoney(string residentId, int amount, string reason);
        /// <summary>住民が物を受け取る（購入・贈り物）。住民はそれを食べる・飾る・使う。</summary>
        void GiveItem(string residentId, ItemStack stack, string reason);
        void ChangeFriendship(string residentId, string playerId, int delta, string reason);
        void AddMemory(string residentId, ResidentMemoryEntry entry);
        /// <summary>住民に今すぐ発話させる（吹き出し・記録）。</summary>
        UtteranceRecord Speak(string residentId, Utterance utterance, SpeechChannel channel = SpeechChannel.Direct);
        void RegisterInteractionProvider(IResidentInteractionProvider provider);
        /// <summary>住民を一時的に特定の場所へ向かわせる（イベント・見送り・クエスト用）。</summary>
        void RequestGoTo(string residentId, Vector3 position, int untilMinuteOfDay, string reasonTag);
        /// <summary>演出中は生活AI・移動・自動の発話を止めてその場に留める（RequestGoTo と Speak は効く）。</summary>
        void HoldForCutscene(string residentId, bool hold);
        /// <summary>住民を即座に移動する（AIの内部位置も同期）。</summary>
        void Warp(string residentId, Vector3 position, float yawDegrees);
        /// <summary>
        /// 家の中などで住民の代わりに表示する姿（代理）を設定する。設定中は本体を隠し、発話の吹き出し・身振りの対象・
        /// IResidentInfo.Position は代理の位置になる。untilMinuteOfDay を過ぎるか ClearIndoorProxy で元に戻る。
        /// 代理の GameObject の [E] は OpenConversation を呼ぶこと。
        /// </summary>
        void SetIndoorProxy(string residentId, Transform proxy, string areaId, int untilMinuteOfDay);
        void ClearIndoorProxy(string residentId);
        /// <summary>住民との会話ウィンドウを開く（代理の姿などから話しかける時）。</summary>
        void OpenConversation(string residentId, PlayerState player);
        /// <summary>住民同士の親しさ 0〜100。</summary>
        int GetResidentAffinity(string residentId, string otherResidentId);
        /// <summary>親しい住民（親しさの高い順）。</summary>
        IEnumerable<string> GetFriends(string residentId, int max = 5);
    }

    // ---------------- 商店（Shop モジュール） ----------------

    public enum ShoppingNeed
    {
        Any,
        Food,         // すぐ食べる
        Ingredient,   // 料理に使う
        Fish,
        Crop,
        Furniture,
        Decor,
        Gift,         // 誰かへの贈り物
        Tool,
        Material,
        Curiosity     // 珍しいもの
    }

    public class ShoppingIntent
    {
        public ShoppingNeed need = ShoppingNeed.Any;
        public int budget;
        /// <summary>欲しい特徴タグ（"color_blue" "sweet" など）。</summary>
        public string preferredTag;
        /// <summary>贈り物の相手。</summary>
        public string forResidentId;
        /// <summary>理由（発話・記録用）："cooking" "party" "redecorate" "hungry" など。</summary>
        public string reasonTag;
    }

    public class ShopVisitResult
    {
        public bool bought;
        public string itemId;
        public int quality;
        public int price;
        /// <summary>満足度 -1〜1。</summary>
        public float satisfaction;
        public bool thoughtTooExpensive;
        public bool wantedButNotFound;
        public List<string> lookedAtItemIds = new List<string>();
        /// <summary>反応として話した発話の記録（マネピが再現できる）。</summary>
        public string reactionRecordId;
    }

    public interface IShopService
    {
        bool IsOpen { get; }
        Vector3 EntrancePosition { get; }
        int OfferCount { get; }
        /// <summary>
        /// 住民が来店して商品を見て、買うかどうか決め、取引（住民の支払い・受け取り・売上計上・反応の発話・マネピの記録）まで行う。
        /// 住民モジュールは来店時にこれを呼ぶだけでよい。
        /// </summary>
        ShopVisitResult ResidentVisit(IResidentInfo resident, ShoppingIntent intent);
    }

    // ---------------- 宇宙生物（Creatures モジュール） ----------------

    /// <summary>
    /// 宇宙生物の能力（生き物・農業・料理仕様の共通能力）。活動ごとに分断せず、同じ能力を農業・料理・加工・修理で使う。
    /// 圧搾・発酵・冷却などの担当生物は仕様上未決定のため、Creatures モジュールが既存50種の特徴から割り当てる（提案扱い）。
    /// </summary>
    public enum CreatureAbility
    {
        Water,        // 水を出す（ミズプク）
        Till,         // 耕す（ホリモグ）
        Sow,          // 種まき（タネパタ）
        Harvest,      // 収穫（モギモギ）
        SoilImprove,  // 土壌改良（ツチポン）
        Pollinate,    // 受粉（ハナピカ）
        Glow,         // 光る（ヒカリムシ、ランプル等）
        Carry,        // 運ぶ（ハコバチ）
        Heat,         // 温める（ポカリス）
        SeedCollect,  // 種集め（タネコロ）
        Grind,        // 回転・粉砕・こね（クルマリ）【派生案】
        Sound,        // 音を鳴らす（オトツム、トントン）
        Bubble,       // 泡（アワリン）
        Splash,       // 水を飛ばす（カワハネ）
        Pack,         // 箱詰め・収納（ハコネ）
        Mimic,        // まねる・伝言（マネピ）
        ColorShift,   // 色を変える（イロポポ、カラポン）
        Stack,        // 積み重ねる（ツミツミ）
        MineralDust,  // 鉱石粉（ホコリフ）
        Echo,         // 反響（コダマル）
        Stick,        // 張り付く・固定（イシペタ、カベピタ）
        Shade,        // 影・暗がり（スミカゲ）
        Float,        // 浮く・漂う（フワタネ、ユメボシ）
        Press,        // 圧搾【未決定→割当提案】
        Ferment,      // 発酵【未決定→割当提案】
        Cool,         // 冷やす【未決定→割当提案】
        Stabilize,    // 安定化（位相安定装置）【追加提案】
        Sense         // 感知（振動・状態を知らせる）【追加提案】
    }

    public enum CreaturePlacement
    {
        Companion,      // 同行
        Farm,           // 農場で働く・暮らす
        Home,           // 自宅で暮らす
        Habitat,        // 特定の家具・住処に住む
        Device,         // 調理器具・加工設備と一体化
        Shop,           // 商店（マネピ等）
        MessagePlaza,   // 伝言広場
        RocketWorkshop, // 修理作業場
        Storage,        // 預かり（ケージ）
        Deposited       // 他の星に預けた
    }

    [Serializable]
    public class CreatureAbilityLevel
    {
        public CreatureAbility ability;
        public int level = 1;
    }

    /// <summary>所有している生物個体の読み取り専用ビュー。</summary>
    public class CreatureView
    {
        public string Uid;
        public string SpeciesId;
        public string Nickname;
        public string OwnerId;
        public CreaturePlacement Placement;
        public string PlacementTargetId;
        public List<CreatureAbilityLevel> Abilities = new List<CreatureAbilityLevel>();
        public int Affection;
        public int Level;

        public int AbilityLevel(CreatureAbility a)
        {
            foreach (var x in Abilities) if (x.ability == a) return x.level;
            return 0;
        }
    }

    /// <summary>
    /// 宇宙生物の窓口（Creatures が実装）。
    /// ownerId の約束：<see cref="CreatureOwners.Any"/>（"*"）を渡すと「全所有者」とみなし、
    /// この星に置かれた全員の個体（農場・作業場などで働く個体）と、ローカルプレイヤーの手持ちの個体をまとめて返す。
    /// （他の星に預けた個体は "*" では返さない。）プレイヤーID を渡すとその人の個体だけを返す。
    /// </summary>
    public interface ICreatureService
    {
        /// <summary>ownerId の個体。"*"（<see cref="CreatureOwners.Any"/>）なら全所有者。</summary>
        IReadOnlyList<CreatureView> GetOwned(string ownerId);
        CreatureView Get(string uid);
        /// <summary>能力を持つ個体（預けた個体は除く）。ownerId に "*" を渡すと全所有者から探す。</summary>
        IEnumerable<CreatureView> FindOwnedWithAbility(string ownerId, CreatureAbility ability, int minLevel = 1);
        bool SetPlacement(string uid, CreaturePlacement placement, string targetId);
        /// <summary>
        /// ホストのコマンド処理の中で、発行者（ゲストを含む）の個体の配置を変える。
        /// ゲストが持ち歩いている個体を星に置く場合は、その個体のデータがゲストから預けられて星の所有になる（元の飼い主は保持）。
        /// 戻り値 false の時は、まだ預かっていない等で配置できなかった。
        /// </summary>
        bool SetPlacementFor(string issuerId, string uid, CreaturePlacement placement, string targetId);
        /// <summary>この星にいる全員分の個体（GetOwned("*") と同じ）。</summary>
        IReadOnlyList<CreatureView> GetAllOnPlanet();
        string GetSpeciesName(string speciesId);
        IReadOnlyList<CreatureAbility> GetSpeciesAbilities(string speciesId);
        bool IsSpeciesDiscovered(string playerId, string speciesId);
        /// <summary>生物を渡す（住民からの贈り物・クエスト報酬など）。新しい個体UIDを返す。</summary>
        string GrantCreature(string ownerId, string speciesId, string source);
        IEnumerable<string> AllSpeciesIds { get; }
    }

    /// <summary>ICreatureService の ownerId に使う特別な値。</summary>
    public static class CreatureOwners
    {
        /// <summary>全所有者（星に置かれた全員の個体＋ローカルプレイヤーの手持ち）。Creatures/Farming の AnyOwner と同じ値。</summary>
        public const string Any = "*";
    }

    // ---------------- 料理・加工（Cooking モジュール） ----------------

    public enum CraftStationType
    {
        Hand,             // その場で
        Kitchen,          // 自宅のキッチン（生物と器具の組み合わせ）
        CommunityKitchen, // 村の共用調理場（序盤から料理できる）
        Workbench,        // 作業台（素材加工・家具）
        Forge,            // 鍛冶屋（金属加工）
        Refinery,         // 精製設備（燃料）
        RocketWorkshop,   // 修理作業場
        CreatureDevice,   // 生物そのもの／生物が住む器具
        CarpenterBench    // 家具職人
    }

    public interface ICraftingService
    {
        /// <summary>加工・調理画面を開く。</summary>
        void OpenStation(CraftStationType type, string stationInstanceId, PlayerState player);
        /// <summary>そのアイテムを作れるレシピID（タブレットの宇宙船診断のヒント等）。</summary>
        IEnumerable<string> RecipesProducing(string itemId);
        bool IsRecipeKnown(string playerId, string recipeId);
        string GetRecipeName(string recipeId);
    }

    // ---------------- 活動系（Fishing / Mining / Farming） ----------------

    public interface IFishingService
    {
        /// <summary>この星で釣れる魚のアイテムID。</summary>
        IEnumerable<string> CatchableFishIds { get; }
        int GetFishingLevel(string playerId);
        bool IsFishing(string playerId);
        /// <summary>釣りの技の一覧（ID）。</summary>
        IEnumerable<string> TechniqueIds { get; }
        string GetTechniqueName(string techniqueId);
        /// <summary>技を覚えさせる（釣り名人のクエストなど）。既に覚えていれば false。ゲストへはネット経由で反映する。</summary>
        bool GrantTechnique(string playerId, string techniqueId);
    }

    public interface IMiningService
    {
        int FloorCount { get; }
        int GetDeepestFloorReached(string playerId);
        IEnumerable<string> OreIdsOnFloor(int floor);
        int GetMiningLevel(string playerId);
    }

    public interface IFarmService
    {
        IEnumerable<string> PlantableCropIds { get; }
        int GetFarmingLevel(string playerId);
        int PlantedPlotCount { get; }
    }

    /// <summary>料理を食べた時の短時間の能力補助（料理仕様 §7.2【提案】。Cooking が実装。スタミナ回復ではない）。</summary>
    public interface IFoodEffectService
    {
        /// <summary>effectId："fishing_bite" "keen_eye" "calm" "swift" "glow" "warm" など。</summary>
        bool HasFoodEffect(string playerId, string effectId);
        string GetActiveFoodEffect(string playerId);
    }

    /// <summary>Gesture.PointAt の target に使う、場所を表す特別な値。</summary>
    public static class PointTargets
    {
        public const string Rocket = "rocket";
        public const string Hill = "hill";
        public const string River = "river";
        public const string Cart = "cart";
    }

    // ---------------- 家・家具（Housing モジュール） ----------------

    public class PlacedFurnitureView
    {
        public string InstanceId;
        public string ItemId;
        public string OwnerId;
        public string Location; // "house:player" "garden" "house:residentId" "shop"
        public Vector3 Position;
        public float RotationY;
        public string ResidentCreatureUid;
    }

    public interface IHousingService
    {
        Vector3 PlayerHomeEntrance { get; }
        /// <summary>住民が買った家具を住民の家に置く（住民の生活が家に残る）。</summary>
        void AddFurnitureToResidentHome(string residentId, string itemId);
        IEnumerable<string> GetResidentHomeFurniture(string residentId);
        IEnumerable<PlacedFurnitureView> GetPlayerHomeFurniture(string ownerId);
        /// <summary>家具に思い出を記録（「誰からもらった」「誰が座った」など）。</summary>
        void RecordFurnitureMemory(string furnitureInstanceId, string memoryJa, string memoryEn);
        /// <summary>寝床の位置（睡眠）。</summary>
        Vector3 PlayerBedPosition { get; }
    }

    // ---------------- クエスト（Quests モジュール） ----------------

    public enum QuestStatus
    {
        Hinted,     // 気配がある（困っている様子）
        Active,     // 引き受けた
        Completed,
        Failed,
        Expired,
        Declined
    }

    public interface IQuestService
    {
        /// <summary>住民に話しかけた時、そのプレイヤーへのお願いがあれば発話を返す（無ければ null）。</summary>
        Utterance GetRequestTalk(string residentId, string playerId);
        /// <summary>
        /// 住民へ物を渡した時、依頼の達成として処理したら true（報酬・反応もクエスト側で行う）。
        /// stack は呼び出し側が既に主人公の持ち物から取り除いた物。true ならその物の行き先はクエスト側が決める
        /// （受け取らない物はクエスト側が主人公に返す）。false なら通常の贈り物として扱う。
        /// </summary>
        bool TryHandleItemGiven(string residentId, string playerId, ItemStack stack);
        /// <summary>身振り（うなずく＝引き受ける、首を振る＝断る など）を依頼への返事として処理したら true。</summary>
        bool TryHandleGesture(string residentId, string playerId, PlayerGesture gesture);
        bool HasActiveRequest(string residentId, string playerId);
    }

    // ---------------- ロケット（Rocket モジュール） ----------------

    public interface IRocketService
    {
        int RepairedDeviceCount { get; }
        int TotalDeviceCount { get; }
        bool IsLaunchReady { get; }
        bool HasLaunched { get; }
        float RepairProgress01 { get; }
        Vector3 RocketPosition { get; }
        bool IsDeviceRepaired(string deviceId);
    }

    // ---------------- ネットワーク（Network モジュール） ----------------

    public interface INetworkService
    {
        bool IsOnline { get; }
        NetRole Role { get; }
        IEnumerable<string> ConnectedPlayerIds { get; }
        /// <summary>睡眠の要求（マルチでは全員の同意で翌朝へ）。</summary>
        void RequestSleep(PlayerState player, int targetHour);
        void OpenMultiplayerMenu();
        string GetPlayerName(string playerId);
    }

    // ---------------- ストーリー（Story モジュール） ----------------

    public interface IStoryService
    {
        /// <summary>メインストーリーの章（0=プロローグ〜5=帰り道、6=最終章）。章は明示表示しない体験の流れとして扱う。</summary>
        int Chapter { get; }
        bool IsPrologueDone { get; }
        /// <summary>最初に主人公を村へ案内した、まんまるな宇宙人の住民ID（エンディングで最後に話しかける）。</summary>
        string GuideResidentId { get; }
        /// <summary>主人公を世話する住民が、出会いから何度も使う言葉（概念ID）。エンディングで最後にもう一度話す。</summary>
        string GuideCatchphraseConcept { get; }
        /// <summary>帰還を選んだ時にロケット側から呼ぶ。エンディング「最後の引っ越し」→ クリア後の選択へ進む。</summary>
        void BeginEnding();
        bool IsEndingInProgress { get; }
    }
}
