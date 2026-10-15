# Quests モジュール（クエスト・住民イベント・依頼帳・村の行事）

`Uchujin.Quests.QuestSystem : GameSystemBase, IQuestService, IResidentInteractionProvider`
（SaveKey `"quests"`、DailyUpdateOrder `480`）。Core のみに依存する。

> 中心思想（クエスト仕様 v0.2）：**クエストをクリアするために住民と話すのではなく、住民と暮らしていたら自然とクエストが生まれる。**
> 宇宙語が分からなくても、住民が実際に出した身振り・見本・数・指さし・表情で手探りに進められる。正解の訳は出さない。

## ファイル

| ファイル | 内容 |
|---|---|
| `QuestSystem.cs` | システム本体（ライフサイクル・セーブ・共通アクセサ・共有イベントの購読） |
| `QuestSystem.Generation.cs` | 日次処理、日常のお願い／暮らしから生まれる依頼の生成、日替わり掲示板、物語の開始、修理の手掛かり |
| `QuestSystem.Delivery.cs` | `IQuestService` の実装（話す・身振り・物を渡す）、達成・報酬・生活の変化、話しかけメニュー |
| `QuestSystem.Help.cs` | 自発的なお手伝い（荷物・落とし物・迷子の生き物・洗濯物） |
| `QuestSystem.Events.cs` | 村の行事（誕生日・市場の日・星の光帯の観察会・お別れ会・引っ越し祝い） |
| `QuestSystem.Book.cs` | 依頼帳のデータ操作（宇宙語の原文の写し・手掛かり・資料ボックス連携） |
| `QuestSystem.Stories.cs` / `QuestStories.cs` | 個別ストーリーの窓口と5つの物語 |
| `QuestSystem.Techniques.cs` | 釣り名人に教わった技 → 釣りモジュールの技の対応表（`QuestFishingTechniques`）と反映 |
| `QuestSpeech.cs` | 依頼・反応・告知の意味データ（`Utterance`/`MeaningFrame`）の組み立て |
| `QuestContent.cs` | クエスト用アイテムの登録、この星で入手可能な物の収集と判定 |
| `QuestData.cs` / `QuestCommands.cs` | 保存・同期データ、`WorldCommand` |
| `QuestSceneView.cs` | 3D表示（掲示板・お手伝いの状況・生活の変化・行事の飾り） |
| `QuestUI.cs` | 掲示板ウィンドウ、依頼帳アプリ（タブレット）、UI小物 |
| `QuestTuning.cs` | 調整用の仮数値 |

## 実装した内容（仕様の節との対応）

### 5系統＋行事（§2）
- **① 日常のお願い（§6.1）**：毎朝プレイヤーごとに1〜3件。住民の職業・今の行動（`Activity`）・天気（雨は魚）・好み（`EvaluateItem`）から対象を選ぶ。
  対象は**この星で実際に入手可能な物だけ**（`IFishingService.CatchableFishIds`、`IMiningService.OreIdsOnFloor`（到達階＋1まで）、`IFarmService.PlantableCropIds`（種IDは作物IDへ読み替え）、`ICreatureService.AllSpeciesIds`、`ItemRegistry` の料理・加工品）。
  バリエーション：特定の物／「青い魚」のような種類＋色（`category+tag`）／大きい魚（釣果の平均×1.15）／品質／配達（預かった包みを別の住民へ）／生き物を捕まえて見せる。
  まず「気配（Hinted）」として生まれ、話しかけると宇宙語＋身振りでお願いする。
- **日替わり掲示板（§6.2）**：`FacilityType.BulletinBoard`（無ければ伝言広場・広場の横）に掲示板を建てる。紙は宇宙人の文字（`ISpeechService.Render`、翻訳しない）＋依頼主の顔（色と形）＋**依頼主が絵を描いた時だけ**対象物の絵（色と形のみ・名前なし）＋署名（宇宙語の名前）。「紙を取る」で引き受け（星で共通の紙。取った人の依頼になる）。行事のお知らせの紙も貼られる（取れない）。
- **③ 自発的なお手伝い（§7）**：受注ボタンなし。世界に状況として出現（`Game.Instance.SceneRoot` 配下）。
  - 重い荷物：住民が荷物のそばで困っている（箱が揺れる）。[E]で持つ→住民が先に立って目的地へ歩く→家の前の敷物（引っ越しなら村の外れの荷車）に着けば完了。引っ越し入居日・荷造り中にも発生。
  - 落とし物：道に光る物。拾うと「誰かの〜」が持ち物に入る。持ち主に渡すとお礼。別の住民に渡すと首を振り、持ち主を知っていれば指さす（知らなければ肩をすくめる）。
  - 迷子の生き物：近づくとついてくる。飼い主の近くまで連れて行けば完了。
  - 雨の日の洗濯物：雨が降り始めると留守の家の物干しに洗濯物。取り込むと、次に会った時にお礼。
- **暮らしから生まれる依頼（§8）**：鍛冶屋の鉱石（作品が工房に飾られる）、料理人の材料（料理が家に）、花好きの花（家に鉢植え）、農業名人の作物（畑の列）、生き物名人（ペット）、家具職人（家具＝`IHousingService.AddFurnitureToResidentHome`）、修理屋（装置）。
  **誕生日の贈り物探し（§10）**：誕生日が近い住民の友達が「贈り物を一緒に探して」と相談。何を渡しても成立し、当日に依頼主が相手へ渡す（`IResidentDirectory.GiveItem`）。相手の好みで反応が変わり、後日依頼主がその様子を報告する。手伝わなければ依頼主が自分で用意する。
- **② 個別ストーリー（§9、5本・多段階）**：プレイヤーごとに進行（§17【未決定】→個人の関係として扱う【仮】）、暮らしの変化（花・作品・模型）は星で共通。
  1. **青い花**（§9.2。`Florist`→`Retired`→`FarmingMaster`）：0 青い花を大事そうに見せる／主人公の店に青い花があれば実際に買う（`IShopService.ResidentVisit`）→ 1 友達の家の前でためらって帰る（17:30頃、近くにいれば聞こえる）→ 2 花束を友達に届けてほしい（見本＋指さし。依頼主に返すと困った顔で友達を指さす：§4.3）→ 3 二人がベンチで一緒に過ごす・友達の家に青い花 → 4 花の意味（理由の文で「昔の友達を覚えているから」）。不参加なら自分で届ける。友達が先に引っ越したら自分の庭に植える。
  2. **釣り名人**（§9.3）：一緒に釣る → 釣り勝負（名人の大きさは決定的、結果は勝っても負けても良い。名人が指で大きさを示す）→ 思い出の魚（絵を見せる。最も希少な魚）→ 技を教わる（`Game.Flags` に `quest.fishing_master.technique.<playerId>` を立て、`IFishingService.GrantTechnique` で釣りの技として覚えさせ、餌を渡す）→ 釣りを続ける理由。
     - 対応表（`QuestFishingTechniques`）：名人の竿さばきのこつ（`fishing_master.rod_knack`）→ 釣りの「魚の誘導 `Guide`」（無ければ `LineHandling` → `MasterSecret` → 釣り側の最初の技）。`IFishingService.TechniqueIds` にある技から選ぶ。
     - リモートのゲストには釣りモジュールが本人の端末へ届ける。さらに各端末が自分のフラグ（ゲストはホストから同期されたフラグ）を見て、まだなら自分で覚える（既存セーブのフラグもロード後に反映。釣り側は覚えていれば何もしない）。
  3. **料理人になりたい住民**（住民仕様 §10。Cook 以外の生活感系の住民を優先）：練習作をくれる → 材料（作物＋魚の2目標）→ 味見（喜ぶ/首を振るで分岐。首を振るともう一度材料から）→ 広場で料理会（行事）→ 夢（自分の店）。材料を渡すと、資料ボックスに「材料を抱えて調理場へ急いだ」という観察事実（レシピの手掛かり）。不参加でも自分で材料を手に入れて進む。
  4. **旅に憧れる小さな住民**（`Traveler`/`Child`/星の子系）：宇宙船を見に来る → 一緒に宇宙船まで行く（行動そのものが目的：§5.2 例外）→ 光る物（`glowing` タグ、無ければ宝石）→ 星の光帯の夜に高台へ（出なければ夜空を見に誘う）→ 「いつか星へ行く」と宇宙船の絵をくれる。
  5. **宇宙船の修理屋の夢**（ロケット仕様 §14）：宇宙船に夢中 → 金属集め → 精密な部品 → 工房に小さな宇宙船の模型（見に行く）→ 修理が半分以上進むと「いつか空へ」と修理部品（`PartConnectorKit`）をくれる。修理の必須条件にはしない。
- **④ 村の行事（§13、四季なし）**：星で共通。
  - 誕生日：住民IDから決定的に周期（28日【仮】）上の日を割り当て、当日17〜20時に近所の住民が家に集まる（風船）。前日にマネピの伝言広場（`IManepiService.LeaveMessage("plaza")`）と掲示板で宇宙語の告知。当日に物を渡すと誕生日の贈り物として反応。
  - 市場の日：週1回（曜日はシードで決定）9〜15時、広場に職業ごとの屋台（雑貨・種・作物・料理・餌・家具…）。`VendorWindow` で買える。売上は住民へ（`GiveMoney`）。
  - 星の光帯の観察会：`WeatherChangedEvent` で `StarBand` が出た夜、住民が高台（`Lookout`→`Hill` エリア）へ集まる（`RequestGoTo`）。居合わせると皆と親しさ＋1。
  - お別れ会：`ResidentMoveOutDecidedEvent` で予定日の前日夕方。親しさが一定以上なら住民のお気に入りの物をもらえる（メインストーリー 第4章）。荷造りの荷物運びも発生。
  - 引っ越し祝い：`ResidentMovedInEvent` の翌日夕方に広場で。入居日は荷物運び。
  - 料理会・夜空の観察（物語から生まれる行事）。

### 宇宙語が分からなくても成り立つ（§4）
- お願いは意味データ（`SpeechAct.Request`/`Desire`、対象物＋色・大きさ・数量・理由・期限の時の言葉）で組み立てる。
- 手掛かりは**住民が実際に出したものだけ**：`OfferItem`（見本を見せる。系統や序盤で確率が変わる）、`ShowNumber`（数）、`PointAt`（相手・家・宇宙船）、`Beckon`、表情。
- 「考える／驚く」身振りで再説明（段階的に見本・数を足し、最後は短い文で言い直す）。
- 誤解（§4.3）：全く違う物→困った顔で見本を見せ直し、でも贈り物として受け取る（親しさ＋1、罰なし）。惜しい物（大きさ・品質・色違い）→首を振って返し、見本を見せる。届け先違い→指さしで正しい相手を示し、返す。

### 受け答え（§5）
- `GetRequestTalk`：優先順は「伝えたいこと（お礼・自分で解決した報告）→ 物語の大事な場面 → 引き受けたお願い（もう一度）→ 気配のお願いを伝える → 今日の行事 → 落とし物・迷子・荷物 → ほかの住民の困りごと（§3）→ 修理屋の手掛かり（§15）」。何も無ければ `null`。
- `TryHandleGesture`：うなずく／喜ぶ／拍手／お辞儀＝引き受ける、首を振る＝断る、考える＝もう一度。引き受けた後に首を振る→「やめる？」と聞かれ、もう一度首を振るとやめる。持ち物を見せる＝渡す物を選ぶ。行事の場で拍手等に反応。
- `TryHandleItemGiven`：目標（物・数・品質・大きさ）を判定。部分的に渡すこともできる（「もっと」と残りの数を指で示す）。気配だけのお願いでも、ぴったりの物を渡せば達成（言葉なしの「見れば分かる」）。目標と方法は分ける（釣っても買っても手持ちでも良い：§5.2）。
- 話しかけメニュー（`IResidentInteractionProvider`）：お願いを聞いた直後に「うなずく／首を振る／首をかしげる（考える）」、関係する時に「持ち物を見せて渡す」。表記は身振りの名前だけで、意味は先回りして明かさない。
- 期限・不参加：引き受けないお願いは2日で住民が自分で解決、期限を過ぎた依頼は「もう自分で見つけた」と報告される（罰なし）。

### 依頼帳（§11、タブレットアプリ `AppId "requests"`、PlayerPlanetKnowledge）
- 依頼主の顔（色と形）、**自分で付ける呼び名**（名前は自動で分からない）、日時、場所（主人公の呼び名の地名）、状態（進行の情報。翻訳の正しさとは別）。
- 宇宙語の原文（語ごとのボタン→`DictionaryLookupRequestEvent` で辞書へ。推測訳表示がONなら自分の推測だけ添える）、表情と身振りの「見たまま」の説明、場面（話しかけた時／うなずいた後／物を渡した後…＝主人公の行動だけ）。
- 実際に見せられた物、描かれた絵（名前なし）、指で示された数、指さした先、自分が渡した物。
- 自由メモ（ゲームは訂正しない）、★、しまう。音声カードに保存（`ITabletService.SaveVoiceCard`）。
- 引き受けた時に資料ボックスへ `TabletMaterialKind.QuestRequest`（観察できた事実だけ）。達成時などに住民辞典へ「渡した時の表情」の観察事実（`RecordResidentObservation`）。
- 個別ストーリーは物語ごとに1ページへ時系列でまとまる。お手伝い・行事も「出来事」として後から記録される（§3 受注なしでも記録）。

### 報酬（§12）
お金（住民が所持金から払う。足りなければ払える分＋村のお礼【仮】）、物（職業に関わる種・餌・料理・家具・素材・肥料）、生き物（生き物名人：`ICreatureService.GrantCreature`）、家具（家具職人）、レシピの手掛かり（観察事実の資料）、親しさ（`ChangeFriendship`）、思い出（`AddMemory`）、修理部品（修理屋の物語）。宇宙語の正解は報酬にしない。

### マルチ（§17、マルチ仕様 §15）
- 依頼はプレイヤーごとのインスタンス（Planet スコープにホストが全員分を保存）。掲示板の紙・お手伝いの状況・行事・生活の変化は星で共通。依頼帳は各自の PlayerPlanetKnowledge。
- 状態変更はすべて `WorldCommand`（`QuestRespond/Give/TakePaper/Help/Attend/MarketBuy/Story/Ack/Progress/Report`）。生成・行事の進行・物語の時間経過はホストのみ。
- 住民の反応の発話は、その操作をした画面で話す（ゲストのローカルでも即座に見える）。居合わせ・到着の判定は各自のローカルで行いコマンドで送る。

## 未実装・簡略化した内容
- 住民同士の関係（友達）を取得する契約が無いため、誕生日に集まる住民は「家の近い住民」、青い花の友達は決定的な選択【仮】。
- 依頼の対象選びの「到達階」は、掲示板ではホストを基準にしている【仮】。
- 料理人を目指す住民の「転職」は契約が無いため、夢の発話と屋台の飾り（生活の変化）で表現。
- 「ほかの住民から聞く」入口は簡易（困っている住民の名前と必要な物を伝え、指さす）。商店の常連の要望（§3）・村の共同作業・釣り大会・収穫祭（§13【未決定】）は未実装。
- 荷物を運ぶのをやめる操作は無い（その日のうちに住民が片付ける）。
- 引っ越した住民からの手紙・再訪（§14【提案】）は未実装（記録は依頼帳に残る）。
- 期限・失敗・同時受注数は仮の値（§21【未決定】）。

## Unity 上で確認が必要な点
- 掲示板・荷物・落とし物・迷子・洗濯物・生活の変化・行事の飾りの見た目、大きさ、地形への接地（`Layout.OnGround`）。
- 運んでいる箱の頭上の位置と、運搬中に住民へ話しかけられるか（箱のコライダーは無効化している）。
- 迷子の生き物のついてくる動き、到着判定の距離（`GuideArriveRadius`）。
- 依頼帳・掲示板の IMGUI レイアウト（宇宙語の文字のボタン、折り返し、フォント。□や★等の記号が出るか）。タブレットの `Draw(Rect)` 内で `GUILayout.BeginArea(area)` を使っている（現在の Tablet 実装は `(0,0,w,h)` を渡すので入れ子で問題ない想定）。
- 話しかけた時のお願い（`GetRequestTalk`）と話しかけメニュー（`GetOptions`）の呼ばれる順番（先にメニューが作られると、初回は身振りの選択肢が出ない。G の身振りメニューでは常に返事できる）。
- 1日の依頼数・報酬額・期限・お手伝いの発生頻度などのバランス、行事の時間帯と住民の移動（`RequestGoTo`）の見え方。
- 青い花の住民が主人公の店で青い花を買う挙動（`IShopService.ResidentVisit` を直接呼ぶ）。
- 釣り名人に技を教わった時（ソロ・ホスト・ゲスト）に「技『魚の誘導』を覚えた！」が1回だけ出て、釣りの釣果帳の「覚えた技」に載るか。既存セーブ（フラグだけある）をロードした時に技が付くか。

## 他モジュールとの連携点
### 呼ぶ契約
- `IResidentDirectory`：`ActiveResidents`/`Get`/`FindByJob`/`Speak`/`RequestGoTo`/`ChangeFriendship`/`AddMemory`/`GiveItem`/`GiveMoney`/`TrySpend`/`RegisterInteractionProvider`
- `IResidentInfo`：`EvaluateItem`（対象選び・反応）、`Job`、`Activity`、`Lineage`、`MainColor`、`Money`、`HomeFacilityId`、`GetFriendship`
- `ISpeechService`：`Render`（掲示板の紙）、`ResidentName`（署名）、`CreateRecord`（掲示板・告知の記録）
- `IManepiService.LeaveMessage("plaza", …)`（行事の告知）
- `ITabletService`：`RegisterApp`、`SaveVoiceCard`、`AddMaterial`、`RecordResidentObservation`、`GetPlayerGloss`、`ShowGlossOverlay`
- `IFishingService` / `IMiningService` / `IFarmService` / `ICreatureService`（対象選び、`IsSpeciesDiscovered`、`GrantCreature`）。`IFishingService.TechniqueIds` / `GrantTechnique`（釣り名人に教わった技）
- 依頼の達成・贈り物などホストで起きた事実のゲスト本人の住民辞典への記録は、Tablet が `QuestStateChangedEvent` / `GiftGivenEvent` から本人へ転送する（依頼帳・資料ボックスへの記録は、もともと本人の端末で行っている）。
- `IShopService.ResidentVisit`（青い花）、`IHousingService.AddFurnitureToResidentHome`、`IRocketService`（位置・修理状況・`IsDeviceRepaired`）、`IWeatherService.Current`
### 発行するイベント
- `QuestStateChangedEvent`（依頼の状態変化。お手伝いは `QuestId="help:<種類>"`、物語の完了は `"story:<id>"`）
- `GiftGivenEvent`（誕生日の贈り物）、`ResidentPurchaseEvent`（市場で主人公が買った：`Where="market_sold_to_player"`）、`DictionaryLookupRequestEvent`（依頼帳の語を押した）、`PlayerGestureEvent`（メニューからの身振り）
### 購読するイベント
`UtteranceSpokenEvent`（依頼帳への記録）、`FishCaughtEvent`、`CreatureCaughtEvent`、`WeatherChangedEvent`、`ResidentMovedInEvent`、`ResidentMoveOutDecidedEvent`、`ResidentMovedOutEvent`、`RocketDeviceRepairedEvent`、`PlayerJoinedEvent`、`ShopItemSoldEvent`
### 登録するアイテムID（`QuestItemIds`）
`quest.blue_bouquet` `quest.parcel` `quest.odd_dish` `quest.homemade_dish` `quest.child_drawing` `quest.moving_box`
`quest.lost_scarf` `quest.lost_key` `quest.lost_toy` `quest.lost_glasses` `quest.lost_bell`
（預かり物・落とし物は `ItemStack.data` に `parcel:<questId>` / `lost:<helpId>|<ownerId>` を入れる。表示しないこと）
### その他の約束
- **`TryHandleItemGiven` の `stack` は、渡した時点で主人公の持ち物から取り除かれている前提**。`true` を返した場合、その物の行き先はクエスト側が決める（受け取らない時はクエスト側が `ctx.GiveItem` で主人公へ返す。受け取った物は `IResidentDirectory.GiveItem` で住民の暮らしへ渡す）。`false` なら住民側の通常の贈り物として扱ってください。
- `PointAt` の対象に、住民ID・施設IDのほか特別な文字列 `"rocket"` `"hill"` `"cart"` `"river"` を使う（向きの演出用。分からなければ無視してよい）。
- `RequestGoTo` の理由タグは `quest.*` / `story.*`。
- `AddMemory` の kind：`quest_done` `quest_accepted` `solved_self` `helped` `gift` `birthday_gift` `received_delivery` `delivered_for_me` `fished_together` `fishing_contest` `taught_technique` `watched_sky_together` `event_*` など。
- `Game.Flags`：`quest.fishing_master.technique.<playerId>`（釣り名人に技を教わった。技そのものは釣りモジュールの技としても覚える）。

## 調整用の仮数値
すべて `QuestTuning.cs`（依頼数・期限・手掛かりの確率・報酬・掲示板・お手伝いの頻度・行事の時間帯・誕生日の周期・物語の日数・修理の手掛かり表）。
希少度の上限の伸び方は `QuestCatalog.RarityWeight`（6日ごと）。
