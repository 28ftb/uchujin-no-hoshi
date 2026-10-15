# 『宇宙人の星』コード構成・開発規約

このドキュメントは、Unity プロジェクト内の C# コードの全体構成と、各モジュールを実装・拡張するときの規約をまとめたものです。
仕様書の原本は `Docs/Specs/` にあります。仕様書間で矛盾がある場合は **`uchujin_no_hoshi_basic_decisions_v0_2.md`（最新の確定事項）** を優先します。

## 1. 基本方針

- **Unity / 3D / EOS P2P / キャラクターメイクあり / スタミナなし / クリア後は「ロケット前に戻って生活継続」or「次の星へ」**（基本決定 v0.2）。
- **コード主体の構成**：シーン・プレハブ・アセットを用意しなくても、空のシーンで再生すればゲームが起動する（`GameBootstrap`）。
  - 3D表示はプリミティブ図形の組み合わせ（`Core/Visual/PrimitiveBuilder`）。アート制作時に差し替える。
  - UI は IMGUI（`OnGUI`）で描画（`Core/UI`）。本格的な UI は後で UI Toolkit / uGUI に置き換える想定。
- **単一アセンブリ**（Assembly-CSharp）。asmdef は使わない。Editor 拡張は `Editor` フォルダに置く。
- **ゲーム全体として連携する**：各システムは独立したサンプルではなく、`Core/Contracts` のインターフェースとイベントで繋がる。

## 2. フォルダ構成

```
Assets/_Project/Scripts/
  Core/                コア（全モジュールが依存してよい唯一の場所）
    Boot/              Game（統括）, GameSystemBase, SystemManifest, GameBootstrap
    Util/              SeededRandom（決定的乱数）, StableHash, Loc（UI多言語）, 直列化ヘルパー
    Events/            EventBus, CoreEvents（全体共有のイベント型）
    Time/              TimeSystem（時刻・日付・睡眠・一時停止）
    Items/             ItemDefinition, ItemRegistry, ItemStack, Inventory, SharedItemIds
    Player/            PlayerState, Wallet, ToolBelt, CharacterAppearance, 入力, 操作, カメラ, PlayerSystem
    Save/              SaveSystem（星・個人・星ごとの知識を分けて保存）
    Planet/            PlanetInfo（WorldId・シード・生成バージョン）, PlanetContext
    Interaction/       IInteractable, ToolUseRouter, AreaManager（エリア移動）, DroppedItem, FlagStore
    Network/           WorldAuthority（ホスト権威のコマンド実行）
    Language/          意味データ（MeaningFrame, Utterance, UtteranceRecord）, Concepts（概念ID）
    Contracts/         モジュール間の連携インターフェース（ServiceContracts, WeatherContracts）
    World/             WorldLayout（生成されたワールドの配置データ）
    Visual/            MaterialCache, PrimitiveBuilder
    UI/                UIManager, UIWindow, UIStyles, HUD, 共通ダイアログ, メニュー
  World/      ワールド生成・地形・村・道路・建物・自然（Uchujin.World）
  Weather/    天候・気候・宇宙固有現象・昼夜の光（Uchujin.Weather）
  Language/   宇宙語の生成（語彙・語順・文法・数詞・名前）・発話表示・会話ログ（Uchujin.Language）
  Tablet/     タブレット・宇宙語辞書・住民辞典・音声カード・資料ボックス・地図・マネピの伝言（Uchujin.Tablet）
  Residents/  住民（10系統の宇宙人）・生活AI・関係・引っ越し・会話（Uchujin.Residents）
  Shop/       セルフ販売商店・値付け・購買判断・売上記録・マネピ（Uchujin.Shop）
  Fishing/    釣り（Uchujin.Fishing）
  Mining/     鉱山・ツルハシ強化・鍛冶屋（Uchujin.Mining）
  Farming/    農業・農場・農業用生物の自動化（Uchujin.Farming）
  Creatures/  宇宙生物50種・捕獲・飼育・能力・図鑑（Uchujin.Creatures）
  Cooking/    料理・加工・設備・レシピ（Uchujin.Cooking）
  Housing/    家・家具・装飾・増築・訪問（Uchujin.Housing）
  Quests/     クエスト・住民イベント・依頼帳・村の行事（Uchujin.Quests）
  Rocket/     ロケット修理・診断・最終起動（Uchujin.Rocket）
  Story/      タイトル・キャラクターメイク・プロローグ・章・エンディング・クリア後（Uchujin.Story）
  Network/    EOS P2P・ロビー・同期・権限・辞書の手渡し・取引（Uchujin.Network）
```

**依存ルール**：各モジュールは `Core` だけに依存する。他モジュールのクラスを直接参照しない。
他モジュールの機能は `Game.Instance.Service<IXxxService>()`（null の可能性あり）か `EventBus` を通して使う。
これにより `Tools/CompileCheck/check.sh <Module>` で「Core＋そのモジュール」だけを単独で型チェックできる。

## 3. ゲームの流れ

1. `GameBootstrap` が `Game` を生成 → `SystemManifest` の順に各 `XxxSystem` を反射で生成 → `RegisterContent()`。
2. タイトル（`Game.TitleScreenFactory`、Story が設定。無ければコアの簡易タイトル）。
3. 新しい星：`Game.StartNewPlanet(NewGameSettings)` →
   `PreparePlanet`（シードからの決定的生成）→ `OnNewPlanet`（初期状態）→ `BuildScene`（GameObject 生成）→ `OnPlayStarted`。
4. ロード：`PreparePlanet` → `RestoreState(scope, ...)` → `BuildScene` → `OnPlayStarted`。
5. プレイ中：`Tick(dt)`、`OnGameMinutesAdvanced`、`OnHourChanged`、`OnDayEnded`、`OnDayStarted`（`DailyUpdateOrder` 順）。
6. 星を離れる：`Teardown()`。`Game.SceneRoot` 以下は自動で破棄される。

## 4. システムの書き方（GameSystemBase）

```csharp
namespace Uchujin.Fishing
{
    public sealed class FishingSystem : GameSystemBase, IFishingService
    {
        public override string SaveKey => "fishing";
        public override int DailyUpdateOrder => 300;
        public override void RegisterContent() { /* ItemRegistry.Register(...), WorldAuthority.Register<...>(...) */ }
        public override void PreparePlanet(PlanetContext ctx) { /* シードから魚の分布など */ }
        public override void OnNewPlanet(PlanetContext ctx) { }
        public override void BuildScene() { /* Game.SceneRoot の下に生成 */ }
        public override void OnPlayStarted() { /* EventBus.Subscribe, ToolUseRouter.Register, タブレットにアプリ登録 */ }
        public override void Teardown() { /* Unsubscribe, 状態クリア */ }
        public override string CaptureState(SaveScope scope, string playerId) { ... }
        public override void RestoreState(SaveScope scope, string playerId, string json) { ... }
    }
}
```

- システムの型名は `SystemManifest.SystemTypeNames` と一致させる（例：`Uchujin.Fishing.FishingSystem`）。
- `RegisterContent()` は起動時に1回だけ。アイテム定義、コマンドハンドラ（`WorldAuthority.Register`）を登録する。
- `ToolUseRouter.Register` / `UIManager.RegisterOverlay` / `EventBus.Subscribe` / タブレットのアプリ登録は `OnPlayStarted` で行い、`Teardown` で解除する（`ToolUseRouter` と UI は Game がまとめてクリアする）。

### セーブのスコープ（マルチ仕様 §5）
| スコープ | 内容 | 例 |
|---|---|---|
| `SaveScope.Planet` | 星の永続状態（ホストが正本） | 住民、農場、商店棚・売上、鉱石、家具、クエスト、ロケット、時刻・天候 |
| `SaveScope.PlayerProfile` | プレイヤーが持ち歩く資産 | 見た目、持ち物、お金、道具、同行生物、技能レベル |
| `SaveScope.PlayerPlanetKnowledge` | 星ごとの個人の知識 | 宇宙語辞書、住民辞典のメモ、資料、地図の探索、釣果記録 |

- JSON は `JsonUtility` を使う（Dictionary 不可 → `Core/Util/SerializableKeyValue.cs` のペア型を使う）。
- `RestoreState(Planet)` はマルチのゲストではプレイ中にも呼ばれる（ホストからのスナップショット）。表示の更新まで冪等に行うこと。

## 5. 決定的な生成（ワールドシード）

- シードから作るものは必ず `SeededRandom` / `StableHash` を使う（`UnityEngine.Random`・`System.Random`・`string.GetHashCode` は環境で結果が変わるので禁止）。
- 用途ごとに `ctx.Rng("fishing.spots")` のようにキーを分ける。他のシステムの乱数消費に影響されない。
- 同じシードなら同じ地形・言語・住民の初期構成・魚の分布になること。

## 6. マルチプレイを前提にした書き方（EOS P2P・ホスト権威）

- 星の状態を変えるプレイヤー操作は `WorldCommand` を定義し、`WorldAuthority.Submit(cmd)` で実行する。
  ソロ／ホストでは即実行、ゲストではホストへ送られる。ハンドラで報酬は `ctx.GiveItem`、支払いは `ctx.TryCharge` を使う。
- シミュレーション（住民AI、作物の成長、生物の出現、鉱石の再配置判定など）は `IsAuthority` のときだけ行う。
- 状態を変えたら `MarkDirty()` を呼ぶ（ホストはゲストへ差分スナップショットを送る）。
- 個人の持ち物（`PlayerState.Inventory`）を直接いじってよいのは、そのプレイヤー自身のローカル操作だけ。
- **辞書は自動配布しない**。共有はプレイヤーの明示的な操作のみ（`ITabletService.ExportDictionary/ImportDictionary`）。

## 7. 宇宙語（全モジュール共通の約束）

- 住民などに何か言わせるときは、日本語の文を作らず **意味データ** を組み立てる：
  ```csharp
  var u = new Utterance(residentId, "shop.too_expensive")
      .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PRICE_EXPENSIVE).Agent(MeaningArg.Of(Concepts.Item(itemId))).Adverb(Concepts.VERY))
      .Face(Expression.Worried).With(Gesture.ShakeHead);
  Game.Instance.Service<IResidentDirectory>()?.Speak(residentId, u);
  ```
- 意味は「その住民がその時点で本当に思っていること・知っていること」と矛盾しないこと（宇宙語仕様 §5.2）。
- `SurfaceToken.internalId` や `UtteranceRecord.meaning` は**プレイヤーに表示しない**（正解の自動翻訳はしない）。
- 主人公は普通のセリフを話さない。意思表示は身振り（`PlayerGesture`）で行う。
- 宇宙語が分からなくても、生活・金策・ロケット修理は最後まで進められること。表情・身振り・行動・物で状況が分かるようにする。

## 8. UI（IMGUI）

- 画面は `UIWindow` を継承して `UIManager.Open(window)`。`DrawContents` 内は `GUILayout` を使ってよい。
- 座標は仮想解像度（高さ1080）。スタイルは `UIStyles`。文字列は `Loc.T("日本語", "English")`。
- 毎フレームの重ね描き（吹き出し、ミニゲーム）は `IUIOverlay` を `UIManager.RegisterOverlay`。
- 汎用ダイアログ：`UIManager.Confirm / Choose / Message / PickItem / InputText`、`VendorWindow`（住民の店で買う）。

## 9. 操作（既定）

| 操作 | キー |
|---|---|
| 移動 / 走る | WASD・矢印 / Shift（スタミナなし） |
| 調べる・話す | E |
| 道具を使う | 左クリック / F |
| 道具切替 | R（Shift+Rで逆） |
| 持ち物を持つ | 1〜0 |
| 持ち物画面 | I |
| タブレット | Tab（地図：M） |
| 身振り | G |
| カメラ | 右ドラッグ回転、ホイール拡大、Z/C 回転、V 方式切替（斜め見下ろし／三人称／真上） |
| メニュー | Esc（セーブ・設定・マルチプレイ・タイトルへ） |
| クイックセーブ | F5 |

## 10. 型チェック（Unity 不要）

```
Tools/CompileCheck/check.sh            # 全体
Tools/CompileCheck/check.sh Fishing    # Core + Fishing のみ
```
NuGet の `UnityEngine.Modules 2021.3.33`（参照アセンブリ）に対して `dotnet build` する。**Unity Editor でのコンパイル・実行確認の代わりではない。**
Unity 6 との API 差に注意：`FindObjectOfType` ではなく `FindFirstObjectByType` / `FindObjectsByType` を使う。`Rigidbody.velocity` は使わない。

## 11. C# の注意（Unity のコンパイラ互換）

- `record`、`init` アクセサ、共変戻り値、既定インターフェースメソッド、`System.Text.Json`、`dynamic` は使わない。
- `async/await` より コルーチンを使う。
- LINQ は使ってよいが毎フレームの処理では避ける。
- `UnityEngine.Time` と `Game.Time`（TimeSystem）の名前の衝突に注意（GameSystemBase 内では `Clock` を使う）。
