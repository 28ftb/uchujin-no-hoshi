using UnityEngine;

namespace Uchujin.Core
{
    public enum GameState
    {
        Boot,
        Title,
        CharacterCreation,
        Loading,
        Playing,
        Cutscene,
        Ending,
        PostEndingChoice
    }

    /// <summary>
    /// セーブ・同期データの所属先（マルチプレイ仕様 §5）。
    /// </summary>
    public enum SaveScope
    {
        /// <summary>星の永続世界状態（地形の変化、住民、農場、商店、鉱山、家具、クエスト、ロケット、時刻・天候）。ホストが正本。</summary>
        Planet,
        /// <summary>プレイヤーが持ち歩く資産（見た目、持ち物、お金、道具、同行生物、技能）。</summary>
        PlayerProfile,
        /// <summary>プレイヤー個人の、星ごとの知識（宇宙語辞書、住民辞典メモ、資料、地図探索）。星ごとに分離し混同しない。</summary>
        PlayerPlanetKnowledge
    }

    /// <summary>
    /// 各ゲームシステムの基底クラス。Game が生成・順序管理する。
    ///
    /// ライフサイクル：
    ///   起動時 1回：RegisterContent()（アイテム・レシピ等の静的データ登録）
    ///   星の開始時：PreparePlanet(ctx)（シードからの決定的生成）
    ///             → 新規なら OnNewPlanet(ctx) / ロードなら RestoreState(...)
    ///             → BuildScene()（GameObject生成）→ OnPlayStarted()
    ///   プレイ中  ：Tick(dt) 毎フレーム、OnGameMinutesAdvanced、OnHourChanged、OnDayEnded、OnDayStarted
    ///   星を離れる：Teardown()（生成物の破棄・購読解除・状態クリア）
    ///
    /// マルチプレイ：
    ///   ・ゲストでは IsAuthority == false。住民AI・成長・出現判定などのシミュレーションはホストだけが行う。
    ///   ・ホストは状態変更後 MarkDirty() を呼ぶ。ゲストには CaptureState(Planet) のスナップショットが送られ、
    ///     RestoreState(Planet, ...) がプレイ中にも呼ばれる。RestoreState は表示の再構築まで冪等に行うこと。
    /// </summary>
    public abstract class GameSystemBase
    {
        public Game Game { get; internal set; }

        /// <summary>一覧表示・デバッグ用の名前。</summary>
        public virtual string SystemName => GetType().Name;

        /// <summary>セーブ・同期データのキー。null ならセーブしない。</summary>
        public virtual string SaveKey => null;

        /// <summary>OnDayStarted を呼ぶ順番（小さいほど先）。天候→鉱山→農業→生物→釣り→住民→商店→クエスト の順を推奨。</summary>
        public virtual int DailyUpdateOrder => 500;

        protected bool IsAuthority => WorldAuthority.IsAuthority;
        protected TimeSystem Clock => Game != null ? Game.Time : null;
        protected PlayerState LocalPlayer => Game != null ? Game.LocalPlayer : null;

        /// <summary>起動時に1回。アイテム定義・レシピ・種族データなどを静的レジストリへ登録する。</summary>
        public virtual void RegisterContent() { }

        /// <summary>星の決定的生成（シードのみから再現できるもの。地形・言語定義など）。新規・ロード共通。</summary>
        public virtual void PreparePlanet(PlanetContext ctx) { }

        /// <summary>新しい星の初期状態を作る（ロード時は呼ばれない）。</summary>
        public virtual void OnNewPlanet(PlanetContext ctx) { }

        /// <summary>シーンにGameObjectを生成する。状態（新規 or ロード済み）に基づいて表示を作る。</summary>
        public virtual void BuildScene() { }

        /// <summary>全システムのシーン構築が終わり、プレイが始まる直前。</summary>
        public virtual void OnPlayStarted() { }

        public virtual void Tick(float deltaTime) { }

        /// <summary>ゲーム内時間が進んだ（通常は1分ずつ、睡眠時はまとめて）。</summary>
        public virtual void OnGameMinutesAdvanced(int minutes) { }

        public virtual void OnHourChanged(int day, int hour) { }

        /// <summary>日付が終わる直前（売上の締めなど）。</summary>
        public virtual void OnDayEnded(int day) { }

        /// <summary>日付更新（0:00）。DailyUpdateOrder の順に呼ばれる。</summary>
        public virtual void OnDayStarted(int day) { }

        /// <summary>星を離れる・タイトルに戻る。GameObject破棄・EventBus購読解除・状態クリア。</summary>
        public virtual void Teardown() { }

        /// <summary>状態をJSON化。該当スコープに保存するものが無ければ null。</summary>
        public virtual string CaptureState(SaveScope scope, string playerId) => null;

        /// <summary>CaptureState の結果を復元。マルチのゲストではプレイ中にも呼ばれる。</summary>
        public virtual void RestoreState(SaveScope scope, string playerId, string json) { }

        /// <summary>状態が変わったことをネットワーク同期に知らせる（ソロでは何もしない）。</summary>
        protected void MarkDirty()
        {
            if (SaveKey != null) WorldAuthority.MarkDirty(SaveKey);
        }

        protected static void Log(string message) => Debug.Log(message);
    }
}
