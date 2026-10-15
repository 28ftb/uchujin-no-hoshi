# Residents（住民）モジュール

`Uchujin.Residents.ResidentSystem : GameSystemBase, IResidentDirectory`（SaveKey `"residents"`、DailyUpdateOrder 400）

かわいい宇宙人10系統の住民を生成し、1日の生活・仕事・買い物・交流・引っ越しを行わせ、主人公とは宇宙語（意味データ）と身振りで交流させる。

## ファイル

| ファイル | 内容 |
|---|---|
| `ResidentTypes.cs` | 列挙（趣味・年齢・関係・転出理由・目標）、**仮数値 `ResidentTuning`**、職業テーブル `JobTable`（職場・賃金・好みタグ・概念）、外見・表情・身振りの説明文 |
| `ResidentData.cs` | 内部データ3層（§1）：`ResidentProfile`（個性）／`ResidentState`（状態＋記憶）／関係・親交・目標・予定・約束・評判・セーブ DTO |
| `Resident.cs` | 実行時の住民（`IResidentInfo`。`EvaluateItem` の算出、記憶・関係の補助） |
| `ResidentGenerator.cs` | 決定的生成（初期住民・新住民、性格・好み・夢・生活時間・外見） |
| `ResidentAvatarBuilder.cs` / `ResidentAvatar.cs` | プリミティブの見た目（10系統）、表情・身振り・移動のアニメーション、[E]で話す |
| `ResidentLines.cs` | 発話（意味データ）の組み立て：挨拶・話題・立ち話と返事・独り言・予報・贈り物/身振りへの反応・お知らせ・手紙 |
| `ResidentSystem.cs` | ライフサイクル、家・職場の割り当て、`IResidentDirectory`、演出用 API、セーブ／同期 |
| `ResidentSystem.Life.cs` | 予定づくり・移動・仕事・買い物・食事・料理・釣り・受け取った物の使い道 |
| `ResidentSystem.Social.cs` | イベント購読と記憶・噂、住民同士の交流（喧嘩・仲直り・約束・贈り物）、聞こえる会話、日々の変化、予報の伝言、観察記録 |
| `ResidentSystem.Moving.cs` | 転出（兆候・後継者・見送り・手紙・再訪）と転入、必須職の維持 |
| `ResidentConversation.cs` | 会話ウィンドウとコマンド（話す・渡す・見せる・身振り） |

## 実装した内容（仕様との対応）

- **生成（住民生活 §1・§18、キャラデザ §2・§3）**：初期住民 16 人（住宅区画が少なければ区画数-1、最低8）。全10系統を使い、修理屋は四本腕（MultiArm）。系統ごとに形・動き（まんまる/もちもちは跳ねる、浮遊系は漂う、四足、多腕、星の冠が感情で回り脈打つ、小物、大きな目、ミックス＝2テーマの掛け合わせ）＋色・大きさ・耳・しっぽ・触角・模様・小物の個体差。年齢（子ども・大人・年配）。
  - 性格の軸（社交・好奇心・のんびり・几帳面・負けず嫌い・照れ屋・節約・食いしん坊・気前・おしゃべり・いたずら・夜型・雨の好み）。
  - タグ嗜好・好きな色・苦手・カテゴリ嗜好・職業/趣味 → `EvaluateItem`（-1〜1）。
  - 職業：必須7職＋案内役（Courier）＋料理人（いつも食べ物をくれる）・旅に憧れる小さな Traveler・花を育てる年配の Florist＋その他。
- **案内役**：`Lineage==Round` かつ `MovedInDay==0`（初期住民）のうち Id（序数比較）が最小の住民。生成時に案内役（Courier）より Id が小さい住民へ Round を割り当てないことで保証。案内役は引っ越さない。挨拶に `IStoryService.GuideCatchphraseConcept` を含める。
- **住居**：`Layout.HousePlots` をシードで並べて割り当て、`occupied/facilityId`、`FacilityInfo(ResidentHouse, plotIndex).ownerResidentId` を設定。職場施設（鍛冶屋等）の `ownerResidentId` も設定（引っ越し予定者より残る人を優先）。ロード・ゲスト同期時は保存データから冪等に再適用。
- **1日の生活（§2〜§4・§19、時間天候 §10）**：起床・朝食・仕事（午前/昼食/午後）・自由時間（趣味・買い物・広場・友達訪問・休息・雨の散歩・宇宙船見物・主人公の家への訪問）・夕食・夜（広場・星見・料理）・就寝。休みの日（`IsRestDay` と同じ週の最終日）、夜型、子どもは早寝。予定の天気（`GetPlanned`）で雨嫌いは屋外趣味を家の作業に変更。降り始めたら雨嫌いは近くの軒下へ雨宿り（同じ軒下で会話が起きやすい）。星の光帯の夜は好奇心のある住民が高台（Lookout）へ。道路網 `FindPath` で歩き、高さは `HeightAt`。
- **目標（§3）**：中期（家具購入・新しい料理・生き物・大物・最高傑作・家を飾る・貯金・修行）と長期（夢）。自分の行動（仕事・買い物・釣り）で進み、主人公を待たない。達成すると記憶に残り次の目標へ。夢の進み具合が引っ越しの理由になる。
- **経済（§4、バランス §7）**：仕事のある日は賃金（`JobTable.DailyWage`）、無い日は基本収入。買い物は主人公の商店へ行って **`IShopService.ResidentVisit(resident, intent)` を呼ぶだけ**（支払い・受け取り・反応・マネピは Shop）。意図（空腹・料理・模様替え・贈り物・最高傑作・珍品）と予算（節約度）を渡す。村の雑貨屋（ありふれた品のみ、代金は雑貨屋の住民の収入）でも買う。共用調理場での外食は料理人の収入。
- **買った物を使う（§4・家具 §15）**：`GiveItem` で持ち物へ → 家具・飾り・花・宝石は家に置く（`IHousingService.AddFurnitureToResidentHome`＋`homeItems`）、食べ物は食べる（好みで気分が変わり「おいしかった」話題になる）、料理好きは材料に使う、贈り物用は相手に渡すまで持つ（友達の誕生日・お別れに買って届ける）。
- **住民同士（§5）**：初期関係（家族・仕事仲間・ライバル・友達・苦手・恋人は1組だけ控えめに）。5分ごとに近くの住民が立ち話 → 親しさ変化、噂の伝播（「見た」→「聞いた」記憶）、予報の伝達、喧嘩（数日距離を置き、出会うと片方が離れる）、仲直り（時間・再会）、約束（翌日、同じ時刻・同じ場所で一緒に趣味）、贈り物・おすそ分け。友達の家を訪ねる・誕生日に集まる。
- **聞こえる会話（言語学習の主材料）**：立ち話は `Speak(..., SpeechChannel.Overheard)` で、話し手の本当の状態から話題を選び（昨日買った/食べた/釣った物、明日の天気（予報を知っている場合だけ）、明日は休み、仕事、目標、夢、約束、喧嘩、引っ越す友達、新しい住民、主人公の噂）、返事は返す側の好み・記憶に基づく（「〜好き？」→ 実際の `EvaluateItem` で YES/NO）。独り言、遠くから手を振る（親しい住民）。ゲストも同期された状態から同じ規則で生成する。
- **天気読み（時間天候 §14）**：毎朝高台で空を見て、伝言広場で `GetPlanned(day+1)` に基づく予報を意味データで作り、`IManepiService.LeaveMessage("plaza", speech.CreateRecord(...))`。10時までに出せなかった日は保険で預ける。新住民の挨拶・引っ越しのお知らせ・元住民の近況も伝言広場へ。
- **主人公との親交（§6・§15・§16・§7、マルチ §14.1）**：プレイヤーIDごとの親しさ・面識・贈り物回数・最後に話した日。釣果・採掘・収穫・捕獲・商店の売上・ロケット修理・贈り物・クエスト達成を `EventBus` で受け、近くで見ていた住民に記憶（鉱山は入口近く・鉱山で働く住民が見る。連日通うと「毎日」）。一緒に釣る/作る/捕まえると思い出＋親しさ。商店で好きな物を買えた住民は常連に。
- **会話ウィンドウ**：[E]で開く（ソロでは時間停止、画面下部）。挨拶（初対面は自己紹介と「空から来たの？」）→（お別れの贈り物／食べ物のおすそ分け／贈り物）→ 本当の状態に基づく話題 → `IQuestService.GetRequestTalk` の依頼。主人公は話さず「身振りで返す」「物を渡す」「物を見せる」「他モジュールの項目」「立ち去る」。宇宙語の単語はボタンで、押すと `DictionaryLookupRequestEvent`（辞書を開く）。プレイヤー自身の推測訳（`GetPlayerGloss`）があれば併記。表情・身振りは言葉以外の手掛かりとして文で表示（訳は出さない）。
  - 身振り「考える」＝住民がもう一度同じことを言う。「手招き」＝暇で親しければ1時間ついて来る。「首を振る」＝引っ越し予定の住民を引き留める（親しさと理由次第）。
- **タブレット**：初めて話した時 `RecordResidentMet`。観察できた事実（場所＋行動、「◯◯を渡したら、にこにこしていた」、引っ越していった日）を `RecordResidentObservation`（内部の好みは書かない。1日1回/種類）。手紙は `AddMaterial(Letter)`。
- **引っ越し（§11、第2章・第4章、バランス §12.2）**：
  - 転出：12日目以降、入居14日以上、転出間隔8日以上、同時に1人まで。夢の達成度・親しさ（主人公と親しいほど出ていきにくい）・性格で決定。理由は夢・家族・友達・別の場所・仕事。22日目までに一度も無ければ夢に最も近い住民が考え始める（第4章）。
  - 兆候：決めた日に伝言広場へお知らせ（日数を指で示す）、友達に知れ渡る、荷造り（Packing・家の外に箱）、前日は挨拶回りとお別れ会、友達が贈り物を買って届ける、親しい主人公にはお別れの贈り物。当日朝、友達に見送られて村の外へ歩いて去り、11時に `FormerResidents` へ。
  - 必須サービス職は消えない：転出決定時に弟子（後継者）を決め、出ていく日に職業を引き継ぐ。弟子が見つからなければ転出しない。毎朝 `EnsureEssentialJobs` で不変条件を修復。`FindByJob` は必須職なら必ず誰かを返す。
  - 転出後：数日後に親しかったプレイヤーへ手紙（`SharedItemIds.Letter`、`data`＝記録ID、`AddMaterial(Letter)`）、伝言広場に近況の声、友達は「寂しい」と話す。10日以上後、まれに1日だけ村へ遊びに来る。
  - 転入：4日目に最初の新住民（第2章）。以後、空き区画があり人数が目標（初期＋3）未満なら数日おきに。転出の後は2〜5日で新住民。新住民は空いた必須職・村にいない職業・少ない系統を優先。村の外から歩いてきて荷ほどき（箱）→ 広場で挨拶。
  - `ResidentMovedInEvent` / `ResidentMoveOutDecidedEvent` / `ResidentMovedOutEvent` を Publish。
- **マルチ**：AI・移動・交流はホストだけ。状態（位置含む）は Planet スコープで、ホストは約1秒ごとに `MarkDirty()`。ゲストは受け取った位置へ補間。親しさ・面識・記憶はプレイヤーIDごと。主人公の操作は `ResidentTalkCommand` / `ResidentGiftCommand` / `ResidentGestureCommand` / `ResidentEndTalkCommand`（`WorldAuthority`）。
- **外見データの保存の分け方**：初期住民はシードから再生成（セーブには `genKey` と状態だけ）。新住民はその時点の村の状態（空いた職業・系統の偏り）で作るため、プロフィールごと保存（`regenerable=false`）。
- **演出用 API（統括の依頼）**：`HoldForCutscene(id, hold)`（AI・移動・自動発話・立ち話を止めてその場に留める。`RequestGoTo`・`Speak` は効く）、`Warp(id, pos, yaw)`（内部位置・経路・ゲスト補間目標まで同期）。他モジュールが Transform を直接動かした場合も、AI が最後に書いた位置との差で検出し、その位置を正として経路を作り直す（巻き戻さない）。
- **RequestGoTo**：指定位置へ向かい `untilMinuteOfDay`（過ぎていれば翌日の同時刻）まで留まる。理由タグに ending/farewell/celebr/event を含むと Celebrating。

## 統括との取り決めへの対応

- 物を渡す：①`ctx.TakeItem` で主人公の持ち物から**先に取り除く** → ②`TryHandleItemGiven`。true ならクエスト側に任せて住民は何もしない／false なら贈り物として受け取る（`HandleGiftCommand`）。
- 話しかけた時は `GetRequestTalk` を**先に**取得し、その後に他モジュールのメニュー項目（`GetOptions`）を集める（`OnConversationOpened`）。
- `Gesture.PointAt` の特別な target：`"rocket"`（ロケット位置）、`"hill"`（高台／丘）、`"river"`（最寄りの川）は向きを合わせる。`"cart"` など不明なものは無視。

## 未実装・簡略化

- 住民の家の**室内**での表示：在宅中は家の前（庭）に立つ。夜の在宅・睡眠中は非表示（家の中にいる扱い）。Housing が `house:<residentId>` の室内エリアを作る場合の室内配置は未対応。
- 宇宙生物を住民がペットとして飼う（§9）：`ICreatureService` に住民の所有を表す手段が無いため、「生き物を迎える」目標は観察（CatchingCreatures）で進むだけ。
- 料理：料理人は登録済みの安い料理（Food）を毎日「作った」として持つ。レシピとの対応はしていない。家庭料理は材料を消費して記憶に残すだけ。
- 誕生日の暦は仮（28日周期）。恋愛・結婚は【未決定】のため恋人関係は1組のみ・贈り物が増える程度。
- 住民が主人公の家に遊びに来る：家の入口まで来て挨拶するだけ（中に入る・家具を使うのは Housing 側の拡張が必要）。
- 手紙・観察記録のタブレット登録はローカルのプレイヤー分のみ（リモートのプレイヤーには手紙アイテムだけ `Bridge.GrantItemToRemote`）。
- 立ち話の「聞こえる会話」は、ローカルの主人公が近く（18m）にいる時だけ生成する（遠くの会話は内部の関係変化だけ起きる）。
- 住民同士の会話の **割り込み**（主人公と同時に話す等）、ゲスト同士の同時会話の調停は未対応。

## Unity 上で確認が必要な点

- 見た目：各系統のプリミティブ構成・大きさ・顔の位置（特に Mixed、四足、浮遊）。表情（目・口・眉・頬）の形が読み取れるか。身振り（手のない系統は体の揺れで代用）。半透明マテリアル（もちもち）。
- 移動：道路ノードへの経路の自然さ、到着判定、坂での高さ、橋。住民同士の重なり。
- 会話ウィンドウ：宇宙語の単語ボタンの折り返し、推測訳の併記、吹き出し（Language）とウィンドウが重ならないか。
- 数値バランス：賃金・予算・買い物頻度（商店の売上）、立ち話の頻度、発話の頻度（うるさくないか）、引っ越しの頻度、新住民の間隔。
- パフォーマンス：住民20〜26人の毎フレーム処理、`FindPath`（A*、List 実装）の頻度、マルチ同期（住民全員の状態を約1秒ごとに送る。記憶・予定を含むため数十KB）。

## 他モジュールとの連携点

- 呼ぶ契約：`ISpeechService`（Speak/CreateRecord）、`IManepiService.LeaveMessage("plaza")`、`IShopService.ResidentVisit/IsOpen/EntrancePosition`、`IHousingService.AddFurnitureToResidentHome/PlayerHomeEntrance`、`IQuestService.GetRequestTalk/TryHandleItemGiven/TryHandleGesture`、`IStoryService.GuideResidentId/GuideCatchphraseConcept`、`ITabletService.RecordResidentMet/RecordResidentObservation/AddMaterial/GetPlayerGloss/ShowGlossOverlay`、`IWeatherService.Current/GetPlanned`、`IFishingService.CatchableFishIds`、`IMiningService.OreIdsOnFloor/FloorCount`、`IRocketService.RocketPosition`。
- Publish：`ResidentMovedInEvent`、`ResidentMoveOutDecidedEvent`、`ResidentMovedOutEvent`、`FriendshipChangedEvent`、`GiftGivenEvent`、`ResidentTalkedEvent`、`ResidentPurchaseEvent`（村の雑貨屋, Where="village_store"）、`PlayerGestureEvent`（会話中の身振り）、`DictionaryLookupRequestEvent`（単語ボタン）。
- Subscribe：`FishCaughtEvent`、`OreMinedEvent`、`CropHarvestedEvent`、`CreatureCaughtEvent`、`ShopItemSoldEvent`、`RocketDeviceRepairedEvent`、`PlayerGestureEvent`（ローカルの主人公の会話外の身振り）、`QuestStateChangedEvent`（完了→記憶）、`WeatherChangedEvent`（雨宿り）、`ItemCraftedEvent`。
- WorldCommand：`ResidentTalkCommand`、`ResidentGiftCommand`、`ResidentGestureCommand`、`ResidentEndTalkCommand`。
- アイテム：新規登録なし。`SharedItemIds.Letter`（手紙）・`SharedItemIds.GiftBox`（お別れの贈り物の代わり）を使用。
- 記憶の種類（`ResidentMemoryEntry.kind`）：`gift` `quest_done` `met_player` `saw_fishing` `saw_rare_fish` `saw_mining` `saw_harvest` `saw_big_crop` `saw_creature` `saw_repair` `saw_gift` `bought_from_shop` `fished_together` `heard_*`（噂）ほか。他モジュールは `AddMemory` で追加できる（`quest_done` は会話の話題になる）。
- 案内役の規則（Story と共有）：`Lineage==Round && MovedInDay==0` のうち Id 最小。新住民の `MovedInDay` は転入日（≥1）。
- 施設：World が区画ごとの `ResidentHouse` 施設を作っていない場合、住民側で `id="house.plot.<index>"` の `FacilityInfo` を `Layout.Facilities` に追加する。

## 調整用の仮数値の場所

- `ResidentTypes.cs` の `ResidentTuning`（人数・引っ越しの間隔と確率・お金・空腹・親しさの増減・記憶・移動速度・会話頻度・誕生日周期）と `JobTable`（賃金・職場・好みタグ）。
- 生成の分布：`ResidentGenerator.cs`（職業の構成、性格の補正、好みの数、生活時間、外見の幅）。
- 引っ越しの細部：`ResidentSystem.Moving.cs` 冒頭の `DepartureMinute` など、夢の目標値 `DreamTarget`（`ResidentSystem.Social.cs`）。
