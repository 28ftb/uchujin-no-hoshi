# 釣りモジュール（Uchujin.Fishing）

`Uchujin.Fishing.FishingSystem : GameSystemBase, IFishingService`（SaveKey `"fishing"`、DailyUpdateOrder 300）。
仕様：`uchujin_no_hoshi_fishing_spec_v0_2.md`（以下 §）、ロケット §6、バランス §9.1、時間天候 §8、マルチ §9.1、宇宙語 §9.2。

## ファイル

| ファイル | 内容 |
|---|---|
| `FishingSystem.cs` | システム本体。ライフサイクル、釣りの開始（ToolUseRouter）、抽選、釣果処理、魚影、小屋、コマンド、セーブ |
| `FishCatalog.cs` | 宇宙魚50種（F001〜F050）と餌・仕掛け5種の定義・ItemRegistry 登録・旧案41種の ID の読み替え |
| `FishSpawnModel.cs` | 出現モデル（水域・釣り場タグ・時間帯・天候/宇宙現象・餌・日替わり傾向）と水面判定 |
| `FishFight.cs` | ミニゲームの中身（張力・距離・魚の体力・行動タイプ） |
| `FishingSession.cs` | 1回の釣り（狙う→着水→待つ→アタリ→やり取り→釣果）と IMGUI 表示 |
| `FishingProgress.cs` | 調整用の仮数値（`FishingTuning`）、竿の強化表、技の情報・実演手順 |
| `FishingMasterInteractions.cs` | 釣り名人の会話メニュー、小屋のメニュー、竿の強化画面、技の実演画面、村の釣果ボード |
| `FishLogApp.cs` | タブレットの釣果帳アプリ（AppId `"fishlog"`） |
| `FishVisuals.cs` | 魚・浮き・竿・糸のプリミティブ表示、魚影（`FishSpotView`）、水しぶき |
| `FishingTypes.cs` | 列挙・セーブDTO・WorldCommand |

## 実装した内容（仕様との対応）

- **宇宙魚50種**（2026-10-16 から。正：`Docs/Specs/uchujin_no_hoshi_fish_50_design_v0_2.md` ＝モデル制作中の50種 F001〜F050）：一般魚20・個性派魚15・希少魚10・伝説魚5。ID は `fish.f001_marukoro` の形（モデルのフォルダ名と同じ）。
  名前・外見・生息候補・行動タイプ・修理の候補は原本どおり。価格・希少度・難しさ・大きさ(cm。サイズ区分 XS〜XL から)は【仮数値】。
  生息候補は、地形生成が付ける釣り場のタグ（`FishCatalog` 冒頭の一覧：瀬・淵・湖の最深部・静かな岸・地下湖の浅い岸など）に置き換えた（マップ設計 v0.2 §8.3）。
  伝説魚の固有の行動は、今のミニゲームにある5つの伝説の動きのうち原本の説明に一番近いものを当てた【提案】（カガミヌシ＝向きが読めない、ライメイウオ＝持久＋暴走＋潜行、ウズシロ＝潮のように寄せて返す、ガンテイオウ＝固まる、ソラワタリ＝連続ジャンプ）。
  旧案41種の ID は `FishCatalog.LegacyIds` で一番近い魚へ読み替える（`ItemRegistry.RegisterAlias`。古いセーブの持ち物が消えないように）。釣果帳の旧案の記録は読み替えない（観察した事実を別の魚の記録にしない）。
  Category Fish、GenericConceptId `Concepts.FISH`、タグ `ItemTags.Fish/Ingredient/Edible`＋色タグ＋発光・大きい・小さい・かわいい等。`ModuleData` に `FishSpecies`。
- **修理用の魚（原本 §9 の候補魚）**：冷却膜 R03 ＝ F008 シモマダラ（2匹）・F044 ヒヤシウオ（1匹で上質）、振動センサー R06 ＝ F030 ハモリウオ・F041 イワナキ・F043 オトナシ・F045 サザナミメ（どれでも1匹。料理の材料は `Ing.SensorFish`）。ID は `SharedItemIds.FishCoolingCandidates` / `FishSensorCandidates`。
  - 低確率待ちにしない手掛かり（ロケット §6, バランス §9.1）：①地形生成が候補魚の住める釣り場を必ず作る（冷たい湧水の淵2水域・静かな岸・早瀬など）、②釣り場に**魚影**（冷却の魚は霜のきらめき／振動の魚と伝説魚は輪の波紋）、③名人が宇宙語＋身振りで候補魚の話をして、住める釣り場を指さす、④餌・仕掛けで狙える、⑤地下湖という天候に左右されない別ルート（ヒヤシウオ・イワナキ）、⑥魚の説明文（主人公のメモ）に生態を記載。
- **出現条件**（§6, 時間天候 §8）：`Layout.FishingSpots`（水の種類＝川・池・**湖**・海・地下湖、タグ）、時間帯（`DayPart`）、天候（`IWeatherService.GetAt`、雨・霧・強風・無風・晴れ・曇り・**雨上がり（雨がやんで5時間以内）・荒天（強風＋雨）・晴れた夜**＋6種の宇宙固有現象）。四季なし。
  **日替わりの傾向**：日付とシードから決定的に、各釣り場で「今日活発な魚」2種（×2.2）と「大物の日」の釣り場1つを決める。
- **操作**：竿を装備して水辺で道具使用（左クリック/F）、または**餌を手に持って**使用 → `ToolUseRouter` のハンドラ。前方が水でなければ近く（12m）の釣り場の方を自動で向く。
  1. 狙う：往復するメーターをタイミングよく止める（緑の範囲＝魚影の方。狙いどおりだと食いつきが早い）。
  2. 待つ：浮きのフェイント。Space で誘う（技「誘い釣り」で効果大）。Q でやめる。
  3. アタリ：「！」が出たら猶予内に合わせる（猶予はレベル・竿で伸びる）。
  4. やり取り（§3 ライン操作型）：Space/左クリック長押しで巻く（張力↑）、離す/S で糸を出す（張力↓・魚が離れる）、A/D で竿を魚と反対へ倒す、W で竿を立てる（潜る魚）。跳ねている間・固まっている間に巻くと張力が跳ね上がる。張りすぎ→糸切れ、緩めすぎ・離されすぎ→外れる。
     魚の行動タイプ（§3.2）ごとに行動の並びが違う：おとなしい／暴走／潜行／跳躍／反転／持久／変則＋プクフグの膨張＋伝説5種の固有行動（波・揺らぎ・硬化・連続ジャンプ・ヌシ）。
  5. 釣果（§8）：サイズ（cm。`ItemStack.size`）と品質（張りすぎなかったか・早さ・大きさ）。釣った魚を頭上に掲げる演出。`FishCaughtEvent` を Publish。
- **成長**（§4）：
  - 竿の段階1〜5（糸の強さ・巻く速さ・アタリの猶予・大物の出やすさ）。強化は釣り名人のメニュー or 釣り名人の小屋の「呼び鈴」。素材（魚＋鉱石）＋お金。代金の6割は名人の財布へ（村の経済循環）。`ToolUpgradedEvent`。段階名は `ToolTypeUtil.TierLabel`。
  - 釣りレベル1〜10（引きが軽く・魚が疲れやすく・アタリの猶予が長く・珍しい魚が寄りやすく）。
  - **名人から教わる技**7種（§4.3, §5。クエスト（釣り名人の物語）で教わった技も `GrantTechnique` で同じ技として覚える）：名人が宇宙語で話しながら**竿の動きを実演**→プレイヤーが同じ動き（A/D/W/Space/S）を真似できたら習得。宇宙語の理解は不要。覚えた技は PlayerProfile に残るので名人が引っ越しても失われない。名人に会えない場合は小屋の「見本の模型」で練習できる（必要レベル+2）。
- **名人との交流**（§5, §10）：会話メニューに「釣りの技を見せてもらう」「竿の強化を頼む」「釣り場の話を聞く」「餌・仕掛けを買う」を `RegisterInteractionProvider` で追加。話は意味データ（`Utterance`）で、今日の傾向・修理用の魚の生態など**正しい内容だけ**を話す。住民辞典には観察した事実のみ記録。
- **大物を釣った時の住民の反応**（§11、宇宙語 §9.2「大きい」の語）：近くの住民が「わあ」「（その魚）とても大きい／珍しい」と言って拍手（ホスト/ソロのみ）。
- **釣果帳アプリ "fishlog"**（PlayerPlanetKnowledge）：魚ごとの数・最大サイズ・最高品質・釣れた場所・時間帯・天気、見かけた魚影の記録、腕前（レベル・竿・覚えた技）、村の釣果ボードの写し。住民の呼び名は記録しない。伝説・修理用の魚を初めて釣った時はタブレットの資料に事実を追加。
- **マルチ**（§13, マルチ §9.1）：釣りはローカル操作で、釣った魚は釣った人の持ち物。`ReportCatchCommand` でホストの「村の釣果ボード」（種ごとの最大記録と釣った人の名前）と伝説の魚の休息を更新。ゲストの釣果はホスト側でも `FishCaughtEvent` を発行。
- **伝説の魚**（§9）：雨の夕方の川の深み／星の光帯の夜の海／逆雨の池／鉱山10階の地下湖／光雨の朝。釣られると星全体で7日休む（再捕獲可）。

## 未実装・簡略化（理由）

- 浮き・重り・リール等の**個別装備**（§4.1）は「竿の段階」にまとめ、餌・仕掛け（5種）だけ個別アイテムにした（操作を増やしすぎないため）。
- **釣り大会**（§8【提案・未決定】）は未実装。村の釣果ボードで記録を見せ合う形のみ。
- 名人が実際に水辺へ移動して釣る実演（住民AIの制御）は Residents 側の担当のため、実演はウィンドウ内の簡易図で表現。名人の種類（川・海・ルアー職人・伝説の釣り人 §5.2）は区別せず、`ResidentJob.FishingMaster` の全員が同じ技を教える。
- 他プレイヤーの浮き・竿の表示同期（Network モジュールの担当）。
- `GrantTechnique` の一時メッセージは保存・再送しない（宛先が退出中なら届かない。クエストの技は Quests 側が同期されたフラグから本人の端末で付け直す）。
- 住民の顔付き付箋（古い案）は最新決定どおり扱わない（マネピ・音声カードは Shop/Tablet 側）。

## Unity 上で確認が必要な点

- ミニゲームの手触り（`FishFight` の係数・`FishingTuning`）：張力の上がり方、糸切れ・外れまでの猶予、1匹あたりの時間（目安10〜25秒）、伝説の魚の難しさ。
- 魚影（`FishSpotView`）の見え方：水面下の影・霜のきらめき・輪の波紋の大きさ・透明度。水面の高さ（`WaterBody.surfaceHeight`）と浮きの位置。
- 竿の見た目（`ToolHolder` の下に付く）と糸（細い箱）の向き。
- キャストの方向・距離（3〜10m）と World の水面判定（`Layout.IsWater`）の整合。地下湖での投げやすさ。
- 出現バランス（`FishSpawnModel` の倍率、`FishCatalog` の希少度）と修理用の魚の入手しやすさ。
- IMGUI の配置（ホットバーと重ならないか、英語表示時の長さ）。

## 他モジュールとの連携点

- **呼ぶ契約**：`IWeatherService.GetAt/Current`、`IResidentDirectory`（`RegisterInteractionProvider`, `Speak`, `FindByJob`, `ActiveResidents`, `GiveMoney`, `ChangeFriendship`, `AddMemory`）、`ITabletService`（`RegisterApp`, `AddMaterial`, `RecordResidentObservation`）。どれも null なら機能を省略。
- **発行するイベント**：`FishCaughtEvent`（釣った人の端末で。ゲストの釣果はホストでも）、`ToolUpgradedEvent`（竿）。
- **購読するイベント**：`WeatherChangedEvent`（魚影の更新）、`AreaChangedEvent`（エリア移動で釣りを中止）。
- **登録するアイテムID**：魚50種（`fish.f001_marukoro` 〜 `fish.f050_sorawatari`。修理用は `SharedItemIds.FishCoolingCandidates` / `FishSensorCandidates`）、餌 `bait.star_worm` `bait.dough` `bait.glow_lure` `bait.deep_sinker` `bait.chime_lure`。
- **使う他モジュールのID**：竿の強化素材に `SharedItemIds.OreConductive/OreSturdyMetal/OreHeatproofMetal`（未登録なら条件から外す）。
- **WorldCommand**：`ReportCatchCommand`, `UpgradeRodCommand`, `LessonLearnedCommand`。
- **鉱山の地下湖**：Mining が `Layout.FishingSpots` に `tags = underground, cave, floor:N, radius:R, (cold/deep/crystal)` の釣り場を追加し、釣りは半径で水面判定する（クラスの直接参照なし）。
- **IFishingService**：`CatchableFishIds`（伝説を除き、その星にある水域の魚）、`GetFishingLevel`、`IsFishing`、
  `TechniqueIds`（技7種の ID＝`FishingTechnique` の名前：`PrecisionCast` `LineHandling` `Lure` `PowerReel` `Guide` `ReadFish` `MasterSecret`）、`GetTechniqueName`（主人公の呼び名・UI 言語）、
  `GrantTechnique(playerId, techniqueId)`（釣り名人のクエストなど。ローカルならその場で覚えて主人公の言語でトースト、既に覚えていれば false。リモートのゲストなら一時メッセージ `"fishing.technique"`（`TechniqueGrantMessage`）で本人の端末へ届け、受信側で覚える。送った時点で true）。
- **一時メッセージ**：`"fishing.technique"`（ホスト → 本人）。ホストはゲストからの付与を受け付けない。
- **AppId**：`"fishlog"`。

## 調整用の仮数値の場所

- `FishingProgress.cs` の `FishingTuning`（レベル表・竿の性能・キャスト・待ち時間・ミニゲーム・伝説の休息日数・住民の反応）、`RodUpgrades.Table`（竿の強化費用）、`TechniqueInfo`（技の必要レベル・実演手順）。
- `FishSpawnModel.cs` の定数（日替わり・時間外・天候・餌の倍率、希少度ごとの重み）。
- `FishCatalog.cs`（魚ごとの値段・希少度・難しさ・大きさ・出現条件）。
- `FishFight.cs`（行動タイプごとの行動の重み・引きの強さ・張力の式）。
