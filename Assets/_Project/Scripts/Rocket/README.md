# Rocket モジュール（ロケット修理・診断・最終起動）

`Uchujin.Rocket.RocketSystem : GameSystemBase, IRocketService`（SaveKey `"rocket"`、DailyUpdateOrder 490）

仕様：`uchujin_no_hoshi_rocket_repair_spec_v0_2.md`（以下 §）、メインストーリー v0.2（第1章・第5章・最終章）、バランス §4.3、基本決定 §5、クエスト §15、マルチ §19、村・マップ §9。

## ファイル

| ファイル | 内容 |
|---|---|
| `RocketDefinitions.cs` | 装置 R01〜R08・購入部品・素材の「性質」ヒント・修理屋の加工代行レシピ・調整用の仮数値（`RocketTables`） |
| `RocketState.cs` | セーブDTO（Planet／Knowledge）と WorldCommand 一式 |
| `RocketSystem.cs` | 本体：状態・コマンド処理・住民の見物・修理屋・最終起動・帰還・クリア後 |
| `RocketShipView.cs` | ロケットと修理作業場の3D表示（プリミティブ＋パーティクル）と演出 |
| `RocketWindows.cs` | 修理台・部品置き場・加工依頼・宇宙船の表示・最終試験の計器オーバーレイ |
| `RocketDiagramWindow.cs` | 修理屋の「図」（文字を使わない絵だけの説明）と絵の部品 `Picto` |
| `RocketDiagnosticsApp.cs` | タブレットの宇宙船診断アプリ（AppId `"rocket"`）／タブレット無し時の代替ウィンドウ |
| `RocketSpeech.cs` | 修理屋・住民の発話（意味データ）と修理屋の会話メニュー |
| `RocketInteractable.cs` | 状態で表示文が変わる [E] 対象 |

## 実装した内容（仕様との対応）

- **ロケットの3D（§11・§12・§12.1）**：`Layout.CrashSite` に不時着。本体・船首・ひれ・脚・窓・操作画面・ハッチ・外装板6枚・冷却配管（背面を一周）・燃料タンクとゲージ・燃料ライン・主エンジンのノズル・アンテナ・位相リング・状態ランプ。
  - 故障中：黒ずんだ船体、大きな傾き（修理数に応じて起き上がる）、剥がれて地面に落ちた外装板、亀裂、垂れ下がった配管、傾いて焦げたノズル、煙（ParticleSystem）、折れ曲がったアンテナ、歪んで震える位相リング、赤い状態ランプ、散らばった残骸。
  - 修理ごとの変化：R01 窓と画面が点灯／R02 外装板が付き亀裂が消え船体がきれいに／R03 透明配管を光る冷却液が循環／R04 燃料ラインが光る（タンクのゲージは給油量で上がる）／R05 ノズルがきれいになり煙が止まる、試運転で炎と振動／R06 アンテナが立って回る、試運転で空へ光の柱／R07 リングが水平になり光って回転／最終：ハッチが開き、状態ランプが緑、飾りが付く。
  - 小さな進捗：外装板は納品した枚数だけ仮付けされる、納品が始まった装置に作業灯が点滅、部品置き場の棚に納品済みの部品（アイテムの色）が並ぶ、納品時のきらめき。
- **修理作業場（§11・§11.1）**：修理台（[E] 修理画面）、加工台（`ICraftingService.OpenStation(CraftStationType.RocketWorkshop, "rocket.workshop")`）、燃料精製器（`Refinery`, `"rocket.refinery"`）、部品置き場、生物の作業スペース。進行で足場→街灯→見物ベンチ→飾りと変化し、残骸が片付く。
- **修理装置 R01〜R08（§3.2・§3.3）**：状態は 故障中→調査中→素材収集中→組立可能→修理済み→動作確認済み。段階は強制しない（素材を持っていればいきなり納品・修理できる）。
  - 必要なもの（`RocketTables.Devices`）：R01 導電部材×3＋小型制御部品×1＋作業費／R02 強化船体板×6＋作業費／R03 冷却膜×3＋接続部品×2＋作業費／R04 宇宙船用燃料×3（燃料ライン充填）＋接続部品×1＋作業費／R05 耐熱構造材×4＋結晶コア×2＋精密部品×2＋作業費／R06 揺らぎセンサー×2＋観測部品×2＋作業費／R07 位相制御部品×1＋**Stabilize か Echo を持つ生物を作業場に配置**＋作業費／R08 全装置修理済み＋燃料タンク10＋最終試験。
  - 現金費用（購入部品の代金＋作業費）はバランス §4.3 の 1,200／4,800／6,500／8,000／16,000／14,500／9,000G（計60,000G）に一致させてある。
  - 生物は消費しない（§7.1）：`FindOwnedWithAbility` で選び `SetPlacement(uid, RocketWorkshop, deviceId)`。修理後は「連れて帰る」で `Companion` に戻せる。各装置に任意の「手伝い生物」（例：R02 温める、R03 冷やす／水、R04 圧搾／発酵／温める）を置くと作業費が20%安くなる【提案】。
- **順序は原則自由（§10）**：どの装置にもいつでも納品・支払い可能。燃料はいつでもタンクへ貯められる。**試運転だけ依存**：R03/R04/R06/R07 は補助電源、R05 は電源＋冷却＋燃料ライン＋タンクの燃料、R04 はタンクの燃料。
- **納品・修理は WorldCommand**（`RocketDeliverCommand` など）。ホストが確定し、装置ごとに誰が貢献したか（点数）と修理履歴を記録（§17）。ゲストは状態差分から修理・試運転・最終起動の演出を手元で再生。
- **購入部品**（`SharedItemIds.PartSmallControlUnit/ConnectorKit/PrecisionParts/ObservationParts/PartPhaseControlUnit` を `ItemCategory.RocketPart` で登録。500→1,200→3,000→3,500→4,000G と後半ほど高額 §8.4）。修理屋の会話メニュー（`RegisterInteractionProvider`）と、`FacilityType.RepairWorkshop` の入口脇のカウンターで `VendorWindow` 販売。代金と作業費は修理屋の財布へ（`GiveMoney`）。
- **修理屋への作業依頼（§9.3）**：原料＋お金を預けると翌日（結晶コア・センサーは2日）に部品を受け取れる（`RocketTables.ProxyRecipes`）。自分で加工するより割高。料理・加工モジュールのレシピが無い構成でも修理が完結する保険。
- **修理屋の後継（§8.3）**：常に `FindByJob(Mechanic)` で探す（後継者にもメニューが出る）。修理屋がいなくてもカウンターは「無人販売」として使え、施設が無い星では作業場の脇にカウンターを置く。注文は星の状態なので引っ越し後も受け取れる。
- **第1章の導入**：初めて修理屋に話しかけると（`ResidentTalkedEvent`）「図」ウィンドウが開く。ロケットの絵で故障箇所が赤く点滅（納品途中は黄、修理済みは緑✔）、必要な素材は**アイテムの色と形の絵**、数は丸（納品済みは塗りつぶし）、入手先は**魚・作物・鉱石・生物・お金・修理屋（四本腕）・加工（金槌）の絵**、原料→加工→部品を矢印で示す。文字の和訳は出さない。修理屋は意味データで話す（`BROKEN ROCKET`、`I REPAIR ROCKET (future)`、`YOU BRING PART`、数を ShowNumber で示す、選んだ箇所について `YOU BRING <item> ×n`、購入品なら `I SELL <item>`）。
- **宇宙船診断アプリ（§13）**：各装置の状態・故障内容・修理後の機能・取り付け位置・必要なもの（未発見の素材は「温度差に強い膜」などの**性質**と入手の手がかりだけ表示し、実際に手に入れる／見ると名前が出る §2.1・§13.2）・納品数／持っている数・`ICraftingService.RecipesProducing` による加工のヒント（知っている作り方かどうか）・修理屋に頼める加工・試運転の条件・修理履歴（誰が何を納品・修理したか）。宇宙語の辞書とは混ぜない。
- **住民の反応（§14.2）**：修理の瞬間に近くの住民（大きな修理は村中から）を `RequestGoTo` で集め、その場にいた住民だけが状態に合った発話（意味データ）をする。修理屋は `WOW / ROCKET GOOD / WE REPAIRED ROCKET TOGETHER` ＋ VeryHappy ＋ Jump・Clap、親しさ＋2。見た住民に `saw_repair` の記憶。エンジン試運転でも住民が集まる。普段も修理屋が9時・14時に作業場へ通い、10〜18時は職業ごとの興味に応じて見物人が来て一言話す（決定的乱数）。
- **完成・最終起動（§18）**：全装置修理＋燃料10で最終起動試験 → 住民が集まり、計器表示（素材の由来を振り返る §18.2）→ エンジン点火 → 「宇宙船の修理が完了しました。故郷の星への帰還が可能です。」→ `RocketLaunchReadyEvent`。**強制離陸しない**。帰還可能でも生活は続く。もう一度試験を見ることもできる。
- **帰還**：ロケットに [E] →「帰還する」→ 確認文 → `RocketBeginReturnCommand`（星の持ち主のみ）→ `IStoryService.BeginEnding()`。Story が無い構成では簡易エンディング（住民が集まる→離陸→トースト→ロケット前へ戻る）。
- **クリア後（基本決定 §5）**：`EndingFinishedEvent` を購読。`ReturnBeforeLaunch` なら `HasLaunched=false`、修理状態は保持、ローカルプレイヤーをハッチの前（ロケット前）へ移動。`NextPlanet` は Story に任せる。
- **ワールドシード（§15）**：場所は `CrashSite`、向きは村の方向＋`ctx.Rng("rocket.site")` の揺らぎ。残骸の配置も決定的。装置構成は全星共通。生物の仕組み（`ICreatureService`）が無い構成では R07 の生物条件を省略して帰還不能にしない。
- **マルチ（§17・§19）**：訪問者（Helper 以上）も素材・お金・生物で協力でき、記録は訪問先の星に残る。帰還はホスト（Owner）だけが決める。共同帰還は未決定のため「ホストの星のロケット」として扱う。
- **フラグ**：`Game.Flags` に `rocket.repaired.R0x`、`rocket.repaired_count`、`rocket.launch_ready`（クエスト・ストーリー用）。

## 未実装・簡略化

- 修理のミニゲーム（§9.4 未決定）は無し。取り付けはボタン1つ、試運転は装置ごとの演出のみ。
- 船内の探索・装飾（§18.3 未決定）は無し。
- 宇宙船の離陸演出は Story が無い時の簡易エンディングだけ（Story のエンディング中はエンジンの小さな炎を出すのみ）。下記「コアへの追加要望」参照。
- マネピの音声カード・住民辞典への診断アプリからのリンク（§13.1）は未実装（タブレットに該当する契約が無いため）。
- 修理屋の依頼は「翌朝以降に部品置き場・修理屋で受け取る」方式。依頼中の修理屋の作業風景は、作業場へ通う行動で代用。
- 生物の作業スペースでは生物の3Dは出さず、パッドが光るだけ（生物の表示は Creatures 側の担当と重複しないように）。
- 「完成した部品の由来（どの魚・どの畑）」は品目単位の記録のみで、個体（釣った場所など）までは追跡しない。

## Unity 上で確認が必要な点

- 見た目：ロケット・作業場のスケール、傾き、地形の起伏での小物の浮き・埋まり、外装板・配管・リングが本体にめり込まないか、カウンターの位置（`RepairWorkshop` の入口脇）。
- パーティクル：シェーダー探索（URP Particles/Unlit → Particles/Standard Unlit → Legacy → Sprites/Default）で煙・炎・きらめきが表示されるか。ビルドに含めるシェーダー設定。
- [E] の当たり判定：本体（カプセル）・修理台・加工台・精製器・部品置き場（箱型）・生物スペース（トリガー）が近づいて選べるか。
- IMGUI：図ウィンドウの絵（手続き生成テクスチャ）の見え方、診断アプリをタブレット内に描く時の座標（`GUILayout.BeginArea(area)` を前提にしている）。
- 演出：最終起動の計器表示の長さ（約20秒）、入力ロック解除、住民が集まる位置と人数。
- バランス：必要個数・作業費・部品価格・燃料10・手伝い割引20%・加工代行の料金と日数。

## 他モジュールとの連携点

- 呼ぶ契約：`IResidentDirectory`（FindByJob(Mechanic), Get, ActiveResidents, Speak, RequestGoTo, GiveMoney, ChangeFriendship, AddMemory, RegisterInteractionProvider）、`ICreatureService`（FindOwnedWithAbility, Get, SetPlacement, GetSpeciesName）、`ICraftingService`（OpenStation, RecipesProducing, IsRecipeKnown, GetRecipeName）、`ITabletService`（RegisterApp, Open）、`IStoryService`（BeginEnding, IsEndingInProgress）、`INetworkService`（GetPlayerName）。すべて null 可。
- 実装する契約：`IRocketService`。`TotalDeviceCount` は8（R08＝帰還可能で修理済み扱い）。
- 発行するイベント：`RocketDeviceRepairedEvent`（R01〜R07 の取り付け時、R08 は最初の最終試験成功時）、`RocketLaunchReadyEvent`、Story が無い時だけ `EndingStartedEvent`／`EndingFinishedEvent`。
- 購読するイベント：`ItemAddedEvent`（素材の発見）、`ResidentTalkedEvent`（修理屋の初回の図）、`EndingFinishedEvent`、`ResidentMovedOutEvent`。
- 登録するアイテム：購入部品5種（`ItemCategory.RocketPart`、`not_sellable`、`precision`、`rocket_material`）。他の素材は `ItemRegistry.GetOrPlaceholder` で参照（鉱石＝Mining、魚＝Fishing、星蜜果＝Farming、加工品と燃料＝Cooking）。
- **Cooking へのお願い**：`part.*` と `fuel.star_honey` のレシピを用意し、`CraftStationType.RocketWorkshop`（`"rocket.workshop"`）・`Refinery`（`"rocket.refinery"`）の `OpenStation` で開けるようにしてほしい（無くても修理屋の加工代行で進行可能）。
- コマンド：`RocketInspectCommand`、`RocketDeliverCommand`（deviceId `"tank"` で給油）、`RocketPayCommand`、`RocketAssignCreatureCommand`、`RocketInstallCommand`、`RocketTestCommand`、`RocketFinalTestCommand`、`RocketBeginReturnCommand`、`RocketOrderCommand`、`RocketCollectOrdersCommand`、`RocketMechanicIncomeCommand`。
- セーブ：Planet＝修理状態・燃料・注文・履歴・貢献者名／PlayerPlanetKnowledge＝図を見たか・発見した素材・最後に見た装置。

## 調整用の仮数値の場所

- `RocketDefinitions.cs` の `RocketTables`：先頭の `const`（燃料量、手伝い割引、修理屋の通う時刻、見物の確率、集まる人数・範囲、初期の傾き、履歴の上限）、`BuildDevices()`（必要素材・個数・作業費・試運転の依存・必須／手伝いの生物能力）、`BuildPurchasedParts()`（部品価格）、`BuildProxyRecipes()`（加工代行の材料・料金・日数）、`BuildHints()`（性質・手がかりの文）。
- 見た目の色・寸法：`RocketShipView.cs` の先頭の色定数と `BuildShip()`／`BuildWorkshop()`。

## コアへの追加要望

1. **エンディングでの離陸演出の受け口**：Story のエンディング中にロケットを実際に飛ばせるよう、`IRocketService` に以下の追加を提案（Rocket 側は実装済みの `RocketShipView.LiftOff` を呼ぶだけ）。
   ```csharp
   // ServiceContracts.cs の IRocketService に追加
   /// <summary>エンディング用：エンジン点火と離陸の演出（seconds 秒で height m 上昇）。状態は変えない。</summary>
   void PlayLiftOff(float seconds, float height);
   /// <summary>出発直前（ロケット前）の見た目へ戻す。</summary>
   void ResetToGround();
   /// <summary>見送りの住民が並ぶ位置（ロケットの前の弧）。</summary>
   Vector3 FarewellSpot(int index, int count);
   ```
2. **`ITabletApp.Draw(Rect area)` の座標の約束**を明記してほしい（現在は「area はタブレット内の絶対座標で、アプリ側が `GUILayout.BeginArea(area)` する」前提で実装）。
3. （任意）`UIStyles.ItemIcon` は名前の頭文字を描くため、文字を使わない図用に「色と形だけのアイコン」をコアに置くと他モジュール（クエストの絵の依頼など）でも使える。Rocket では `Picto.ItemShape` として自前で実装済み。
