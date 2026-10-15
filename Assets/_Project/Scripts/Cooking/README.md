# Cooking（料理・加工）モジュール

`Uchujin.Cooking.CookingSystem : GameSystemBase, ICraftingService`（SaveKey `"cooking"`、DailyUpdateOrder 350）

中心コンセプト：**「宇宙生物と一緒に料理する。生き物の能力を発見すると、料理や加工の可能性が増える」**（料理・加工仕様 v0.2）。
工程（`CraftStep`）を生物の能力（`CreatureAbility`）で満たす。生物は材料として消費しない。

## ファイル

| ファイル | 内容 |
|---|---|
| `CookingSystem.cs` | システム本体。ライフサイクル、セーブ（3スコープ）、`ICraftingService`、WorldCommand のハンドラ、時間加工の進行、共用調理場の生成 |
| `CookingTypes.cs` | 型（工程・レシピ・材料指定・器具・ステーション・効果）、独自タグ、**調整用の仮数値 `CookingTuning`** |
| `CookingTables.cs` | 工程・能力・ステーション・食べ物の効果の表（**仮数値・割当はここ**） |
| `CookingIngredients.cs` | 材料指定（「魚なら何でも」などタグ・分類での指定） |
| `CookingContent.cs` | アイテム・器具・レシピの登録（ID は `CookingIds`） |
| `RecipeBook.cs` | レシピの登録先と、自由調理の照合 |
| `StationContext.cs` / `CraftEngine.cs` | ステーションの状況（使える能力・時間加工の置き場）、計画、品質、材料の自動選択、謎料理の判定 |
| `CraftActions.cs` | 調理の実行（ローカル操作）、仕込み・取り出し・依頼・使用料（WorldCommand） |
| `CookingCommands.cs` | WorldCommand 定義 |
| `StationWindow.cs` | 調理・加工画面（作り方／実験／仕込み／職人に頼む／生き物） |
| `CookingMinigameWindow.cs` | 初めての料理の短い操作（能力ごとに手触りが違う） |
| `CookingResidentMenus.cs` | 住民メニュー（料理人の実演・食材販売・器具販売・修理屋への依頼）と実演画面 |
| `RecipeRecordApp.cs` | タブレットの「料理・加工記録」アプリ（AppId `"recipes"`） |
| `FoodEffects.cs` | 食べる操作・能力補助（提案）・食の記録・効果表示 |
| `CookingVisuals.cs` | 共用調理場の3D（プリミティブ）・調理の湯気演出 |
| `CookingUI.cs` | 画面部品（能力アイコン・工程札など） |

## 実装した内容（仕様との対応）

- **§2 宇宙生物と調理器具の3方式**
  - A 生物と器具の一体化：器具（家具）に住む生物（`Placement==Device && PlacementTargetId==家具ID`、または `PlacedFurnitureView.ResidentCreatureUid`）の能力を使う。対応する器具に住んでいると品質ボーナス。
  - B 生物自身が器具：同行中（`Companion`）の生物の能力をそのまま使える。`OpenStation(CreatureDevice, 生物UID)` でその生物だけを相手に調理もできる。
  - C 複数生物の協力：自宅の同じ場所・半径 `KitchenLinkRadius` 内の器具をひとつの台所としてつなぎ、複数の能力を連結する。
- **§2.1 序盤から料理できる経路**：`FacilityType.CommunityKitchen` の入口前に共用調理場（調理台・かまど・水場・干し棚・発酵樽）を生成。生物なしで「温める・水を入れる・形を作る」と、乾燥・発酵の仕込みができる。施設が無い星では広場の近くに仮設で置く。料理人・雑貨屋が基本食材（星の塩・甘い樹蜜・香り草・村の麦粒・ころころ豆）を売る。
- **§3 能力を活動ごとに分断しない**：同じ「温める」で焼き魚・製錬・燃料精製を行う。担当が未決定の圧搾・発酵・冷却・乾燥・燻煙は能力（Press / Ferment / Cool / Float / Shade）で指定し、どの種族が持つかは Creatures の割当に従う。
- **§4 器具**：かまど・給水台・製粉台・泡立てボウル・圧搾台・発酵樽・乾燥棚・燻製箱・冷却箱・盛り付け台・搬送棚（まとめ数↑）・止まり木（実演の見直し）・小さな精製器（Refinery）・加工作業台（Workbench）。`ItemCategory.Furniture`、タグ `kitchen` と能力タグ（`ability_heat` など）。家具職人・雑貨屋・料理人が販売、家具職人の作業台で素材から作ることもできる。
- **§4.3 生物は機械ではない**：「生き物」タブで器具に住む生物の様子（眠る・遊ぶ。夜は眠る）を表示。住んでもらう／連れて行くを切り替えられる（`ICreatureService.SetPlacement`）。
- **§5 レシピ**：料理約40品＋加工食品（ジュース・ジャム・干物・燻製・漬け物・発酵だれ・発酵ジュース等）＋素材（インゴット・磨いた結晶・星ガラス・鉱石粉・植物繊維・布・繊維ボード・加工石・9色の染料）＋ロケット部品7種＋器具14種。
  - §5.2 同じ魚から焼き魚・塩焼き・蒸し魚・スープ・香草包み・すり身焼き・干し魚・燻製・煮付けなど。
  - §5.3 加工の連鎖（麦→粉→生地→パン→ジャムパン、果実→ジュース→ゼリー／発酵ジュース 等）。
  - §5.4 特殊料理（ふわふわ星パン、キラキラ果実ゼリー、ぐるぐる野菜ロール、燻製、にじいろ重ねケーキ、星の発酵ジュース、まるごと野菜スープ）。
  - §5.5 意図と違う結果：焼きすぎ（こんがり焦げ魚）・煮詰めすぎ（果実キャンディ）は別の料理、合わない組み合わせは「なぞの◯◯」（工程に応じて12種）として手に入る。
  - 材料はタグ・分類指定（魚／果実／野菜／穀物／香り草／鉱石／結晶 等）と固有ID指定を併用。染料は材料の色で完成品が変わる。
- **自由調理（実験）**：持ち物から材料（最大5枠）と手順（最大6、順番も一致）を選んで試す。材料と手順が合っているのに場所が違う場合は「ここでは仕上がらない」と伝えて材料を使わない。
- **§6.1 初めての料理は短い操作**：工程ごとに手触りが違うミニゲーム（長押しで保つ／ちょうどで止める／連打（泡はあふれる）／拍に合わせる）。感知・音の生物がいると範囲が広がる。閉じれば中止（材料は減らない）。
- **§6.2 2回目以降は快適に**：「まとめて作る ×N」（上限はステーションごと、搬送の生物で増加）、「最大」ボタン、「手で仕上げる」で品質にこだわれる。
- **§6.3 生物の調理習熟【提案】**：個体UID×能力ごとの使用回数で★が上がり品質に加算（PlayerProfile に保存）。
- **§7.1 品質・味・食感**：品質＝材料の品質＋操作の出来＋器具ボーナス＋生物の能力レベル＋習熟＋天候。味・食感・温度・発光はタグ（`sweet`, `texture_fluffy`, `temp_hot` など）で持たせ、画面に表示。住民の好みはタグから Residents が判断できる。
- **§7.2 食事の用途【提案扱い】**：手に持って [F]／クリックで食べる。短時間の能力補助（足取りかるい＝歩く速さ+15%、ほんのり発光＝体が光る、ぽかぽか＝湯気）と食の記録。`CookingTuning.FoodEffectsEnabled / FoodLogEnabled` で無効化可。スタミナ回復は不採用。鮮度・腐敗は実装しない。
- **§8.1 時間経過の加工**：乾燥・発酵・燻製・設備まかせの工程は器具に仕込み、ゲーム内時間（1時間ごと）で進む。仕込んで別の活動へ行ける。完成後に取り出し忘れても失敗しない。器具の中身は Planet スコープ（ホスト権威の WorldCommand）。
- **§8.2 天候**：晴れ＝屋外の干し物が早く・出来が良い、強風＝屋外乾燥が早い、雨・霧＝発酵が進み出来が良い、光雨＝屋外で「光らせる」を天気の力で代用＋光雨ジャム、逆雨＝屋外で「水を入れる」を代用＋逆雨しずく飴、星風＝星風ハーブ塩・香りの料理に品質ボーナス、結晶霞＝研磨品にボーナス、花潮＝染料にボーナス。四季の条件は無い。
- **§9 非食品の素材加工**（上記）。ツルハシ強化は扱わない（鍛冶屋の役割を維持）。
- **§11 住民の料理・食文化**：料理人（`ResidentJob.Cook`）の「料理を見せてもらう」で、材料を指さし・工程の仕草・完成品を差し出す実演（宇宙語は意味データ `Utterance` で話す。訳は出さない）。見た手順は作り方一覧・実験に手掛かりとして載る。1日1回（`DemoIntervalDays`）、親しさ+1、住民の記憶に「一緒に料理」。資料ボックスと住民辞典には観察できた事実だけを記録。声は音声カードに保存できる。
- **§12 料理・加工記録アプリ**：作った物（自分で付ける料理名・メモ・使った物・行った手順・手伝った生物・天気）、見せてもらった実演（声の再生）、試した組み合わせ（謎料理も）、仕込み中、食の記録。初めて作った物は資料ボックス（`TabletMaterialKind.Recipe`）へ。
- **§14 星ごとの食文化【提案】**：シードから「この星でよく見る料理」3品を決め、料理人が優先して見せる。作れるようになったレシピは次の星へ持ち越す（`CarryRecipesAcrossPlanets`）。
- **§15 マルチ**：材料は呼び出したプレイヤー自身の持ち物から取る（ゲストも自分の材料で作れる）。近くにいる他プレイヤーの同行生物の能力を借りる共同調理【提案】。他人の家の器具は権限「お手伝い」以上で利用、他人の仕込みの取り出しは「共同生活」以上。
- **ロケット用加工（ロケット仕様 §9）**：導電部材（導電性鉱石×3：温める→形・Forge/修理作業場）、強化船体板（高強度金属×4：Forge）、冷却膜（フロストフィン×2：水→形→冷やす・修理作業場/作業台）、星蜜燃料（星蜜果×3：しぼる→発酵→温める・Refinery）、耐熱構造材（耐熱金属×3：温める→形→冷やす・Forge）、結晶エネルギーコア（エネルギー結晶×2：磨く→形・修理作業場）、揺らぎセンサー素材（リップルアイ×1：水→磨く・修理作業場）。
  - 最初から作り方が分かる（`StartKnown`）。**生物がいなくても作れる複数手段**：鍛冶屋の炉（温める・形・冷やす が設備で可、使用料30G）、修理作業場（冷却試験台・研磨、水と熱は時間をかければ設備まかせ）、精製器（しぼる・温めるは設備まかせ、発酵は時間）、**職人への依頼**（鍛冶屋・修理屋に手数料、品質「良」）。生物がいれば早く・良く作れる。

## 未実装・簡略化した内容（理由）

- 鮮度・腐敗（§7.3 未決定。負担を避ける方向のため実装しない）。
- 食べ物の効果のうち「釣りのアタリが分かりやすい」「鉱石・生き物に気づきやすい」「生き物が寄ってきやすい」は、効果の状態だけ持っている（`HasFoodEffect`）。実際の効果は各モジュールが参照する必要がある（下記コア要望）。
- 自宅の食卓で住民と食事する（§10.3）・持ち寄りイベント（§11.3）は Housing / Residents / Quests の領分のため未実装。料理アイテム（`dish` タグ、`ItemCraftedEvent`）で連携できる。
- 調理中に器具の場所で生物が実際に動くアニメーションは無い（Housing の家具の見た目を持たないため）。代わりに器具の位置に湯気・泡の演出と、画面内の跳ねる能力アイコン・擬音で表現。
- 仕込みの取り消し（材料を戻す）は無い。
- 職人への依頼は即時完成（制作日数は持たせていない）。
- ゲストの調理は `ItemCraftedEvent` がゲスト側だけで発行される（ホストのクエストは気づけない）。
- 料理人以外の住民が自分で料理する生活AI（§11.1）は Residents の領分。

## Unity 上で確認が必要な点

- 共用調理場の配置（施設の入口の前・屋外に置いている。World の建物と重ならないか、向き）。
- 画面のレイアウト（1320×880、IMGUI）。実験タブの3列、作り方タブの手順ブロックの折り返し。
- ミニゲームの手触り・長さ・判定範囲（`CookingMinigameWindow` 冒頭の定数）。スペースキーとゲージのクリックの両方で操作できるか。
- タブレットアプリの描画領域（`Draw(area)` の中で `GUILayout.BeginArea(area)` している。タブレット側が既に Area を始めている場合は二重になるので調整が必要）。
- 発光・ぽかぽか効果の Point Light の明るさ、歩く速さ+15% の体感。
- 数値バランス（価格・品質の計算・仕込み時間・依頼の手数料）。
- 星ごとに `OnHourChanged` で全仕込み枠を進める処理の負荷（枠数は少ない想定）。

## 他モジュールとの連携点

### 呼ぶ契約
- `ICreatureService`：`GetOwned`, `Get`, `SetPlacement`（器具に住む `Device`／作業場 `RocketWorkshop`／同行 `Companion`）, `GetSpeciesName`, `GetSpeciesAbilities`, `IsSpeciesDiscovered`, `AllSpeciesIds`
- `IHousingService.GetPlayerHomeFurniture`：家具インスタンスIDから器具を探す（自分→星の持ち主→参加者の順）。`PlacedFurnitureView.Location` が `"garden"` なら屋外扱い。
- `IResidentDirectory`：`RegisterInteractionProvider`, `FindByJob`, `Speak`, `GiveMoney`, `ChangeFriendship`, `AddMemory`
- `ISpeechService`：`ResidentName`, `GetRecord`, `Replay`, `PlanetName`
- `ITabletService`：`RegisterApp`, `AddMaterial`, `RecordResidentObservation`, `SaveVoiceCard`, `Close`
- `IWeatherService`：`Current`, `GetAt`

### 呼ばれる契約（`ICraftingService`）
- `OpenStation(type, stationInstanceId, player)`：どこから開かれても動く。
  - `Kitchen`＋家具インスタンスID（Housing：`kitchen` タグの家具の [E]）。器具が精製器なら Refinery、作業台なら Workbench として開く。
  - `Forge`（Mining の鍛冶屋メニュー）、`Refinery`・`RocketWorkshop`（Rocket の作業場。施設IDなど任意の ID）、`CarpenterBench`（Housing の家具制作）、`CreatureDevice`＋生物UID（Creatures から使える）、`Hand`（タブレットの記録アプリ）、`CommunityKitchen`＋施設ID（自前の調理台）。
- `RecipesProducing(itemId)`, `IsRecipeKnown(playerId, recipeId)`, `GetRecipeName(recipeId)`（自分で付けた名前があればそれ）

### 発行するイベント
- `ItemCraftedEvent`（即時の調理・仕込みの取り出し・依頼のすべて。`FirstTime` は自分で初めて作った時）

### 購読するイベント
- `WeatherChangedEvent`（将来の演出用フック）

### 登録するアイテムID
- 共有ID（登録担当）：`SharedItemIds.PartConductiveWire`, `PartHullPlate`, `PartCoolingMembrane`, `FuelStarHoney`, `PartHeatproofFrame`, `PartCrystalCore`, `PartRippleSensor`（Category `RocketPart`、タグ `rocket_material`、`not_sellable`）
- 独自ID：`CookingIds`（`cook.ing.*` 基本食材、`cook.proc.*` 加工食品、`cook.dish.*` 料理、`cook.mystery.*` 謎料理、`cook.mat.*` 素材、`cook.dev.*` 器具）
- 器具は `ItemDefinition.ModuleData` に `DeviceDef` を入れている。

### WorldCommand
- `CookStartTimedCommand`（器具に仕込む）、`CookCollectTimedCommand`（取り出す）、`CookCommissionCommand`（職人に依頼。手数料は住民のお金になる）、`CookPayFeeCommand`（炉の使用料）

### 住民メニュー（`RegisterInteractionProvider`）
- 料理人：料理を見せてもらう／食材を買う。家具職人：調理器具を注文する／作業台で加工してもらう（`CarpenterBench`）。雑貨屋：台所用品を買う。修理屋：部品の加工を頼む（`RocketWorkshop`）。

### セーブ
- Planet：仕込み枠（`CookingPlanetData`）。ゲストではプレイ中にも復元され、共用調理場の干し棚の見た目まで更新する。
- PlayerPlanetKnowledge：作った物・試した組み合わせ・見た実演（`CookingKnowledgeData`）。
- PlayerProfile：持ち越したレシピ・生物の習熟・食の記録・食べ物の効果（`CookingProfileData`）。

## 調整用の仮数値の場所

- `CookingTypes.cs` の `CookingTuning`（品質の重み、器具・習熟・天候ボーナス、まとめ数、台所の連結半径、記録の上限、効果の有効/無効）
- `CookingTables.cs`（工程の時間 `BaseHours`/`FastHours`、ステーションごとの設備工程・まとめ上限・使用料・依頼職人、能力の割当、効果の定義）
- `CookingContent.cs`（アイテムの価格、レシピの材料・手順・手数料・効果時間、器具の価格・販売者）
- `CookingMinigameWindow.cs` 冒頭（ミニゲームの長さ）
- `CookingSystem.OnHourChanged`（天候による進み方の倍率）
- `CookingResidentMenus.OpenShop`（食材の販売価格倍率 1.2）
