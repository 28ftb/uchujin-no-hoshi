# Residents（住民）モジュール

> **2026-10-16 の変更**：ユーザーの指定で、初期住民10人・同時居住の上限15人（初期＋引っ越しで5人まで増える。`ResidentTuning`）。静かな高台（`AreaRegion.quiet`）は住民の行き先に選ばない（`WorldLayout.GetVisitableAreas`。星の光帯の夜も見晴らし台へ行く）。

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
| `ResidentSystem.Sync.cs` | マルチの同期（軽い状態 `residents.live`／全体状態の間引き／表情・身振りの中継）、家の中の代理の姿、プレイヤー個人への手紙・観察記録（不在なら参加時に） |
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
- **聞こえる会話（言語学習の主材料）**：立ち話は `Speak(..., SpeechChannel.Overheard, relayToGuests:false)` で、話し手の本当の状態から話題を選び（昨日買った/食べた/釣った物、明日の天気（予報を知っている場合だけ）、明日は休み、仕事、目標、夢、約束、喧嘩、引っ越す友達、新しい住民、主人公の噂）、返事は返す側の好み・記憶に基づく（「〜好き？」→ 実際の `EvaluateItem` で YES/NO）。独り言、遠くから手を振る（親しい住民）。ゲストも同期された状態から同じ規則で生成する。
  - **発話の中継の整理**：各端末で生成する立ち話・独り言・遠くから手を振るは `relayToGuests:false`（ホストでも中継しない）。ホストだけが生成する発話（目撃の反応・贈り物を渡す言葉・予報・新住民の挨拶・家への訪問の挨拶・会話ウィンドウ・他モジュールの `IResidentDirectory.Speak`）だけが中継されるので、同じ場面の発話は重ならない（以前の「ホストの主人公が近い時はゲストで生成しない」暫定対処は削除）。ホストだけが生成する発話は、ホスト・ゲストどちらの主人公が近くにいても生成する（`AnyPlayerNear`）。
  - 主人公へ直接話す発話（会話ウィンドウの全発話・遠くから手を振る・身振りへの返事・釣果への反応・家への訪問の挨拶・手紙）には `Utterance.addresseeId = playerId` を入れる。
- **天気読み（時間天候 §14）**：毎朝高台で空を見て、伝言広場で `GetPlanned(day+1)` に基づく予報を意味データで作り、`IManepiService.LeaveMessage("plaza", speech.CreateRecord(...))`。10時までに出せなかった日は保険で預ける。新住民の挨拶・引っ越しのお知らせ・元住民の近況も伝言広場へ。
- **主人公との親交（§6・§15・§16・§7、マルチ §14.1）**：プレイヤーIDごとの親しさ・面識・贈り物回数・最後に話した日。釣果・採掘・収穫・捕獲・商店の売上・ロケット修理・贈り物・クエスト達成を `EventBus` で受け、近くで見ていた住民に記憶（鉱山は入口近く・鉱山で働く住民が見る。連日通うと「毎日」）。一緒に釣る/作る/捕まえると思い出＋親しさ。商店で好きな物を買えた住民は常連に。
- **会話ウィンドウ**：[E]で開く（ソロでは時間停止、画面下部）。挨拶（初対面は自己紹介と「空から来たの？」）→（お別れの贈り物／食べ物のおすそ分け／贈り物）→ 本当の状態に基づく話題 → `IQuestService.GetRequestTalk` の依頼。主人公は話さず「身振りで返す」「物を渡す」「物を見せる」「他モジュールの項目」「立ち去る」。宇宙語の単語はボタンで、押すと `DictionaryLookupRequestEvent`（辞書を開く）。プレイヤー自身の推測訳（`GetPlayerGloss`）があれば併記。表情・身振りは言葉以外の手掛かりとして文で表示（訳は出さない）。
  - 身振り「考える」＝住民がもう一度同じことを言う。「手招き」＝暇で親しければ1時間ついて来る。「首を振る」＝引っ越し予定の住民を引き留める（親しさと理由次第）。
- **タブレット**：初めて話した時 `RecordResidentMet`。観察できた事実（場所＋行動、「◯◯を渡したら、にこにこしていた」、引っ越していった日）を住民辞典へ（内部の好みは書かない。1日1回/種類/プレイヤー）。会話・贈り物・見せた物は話した本人の端末で `RecordResidentObservation`。引っ越していった日はホストが、その住民を知っていたプレイヤー全員へ `RecordResidentObservationFor`（ローカルは直接）。手紙はローカルなら `AddMaterial(Letter)`、リモートなら `AddMaterialFor(playerId, 資料, 発話の原本)`。
  - その時に接続していないプレイヤー（ソロに戻した後のゲストなど）への手紙・観察記録は `ResidentsSaveData.pendingRecords` に保存し、`PlayerJoinedEvent`（ホスト）で届ける（手紙は届ける時に発話の原本を作る）。
- **引っ越し（§11、第2章・第4章、バランス §12.2）**：
  - 転出：12日目以降、入居14日以上、転出間隔8日以上、同時に1人まで。夢の達成度・親しさ（主人公と親しいほど出ていきにくい）・性格で決定。理由は夢・家族・友達・別の場所・仕事。22日目までに一度も無ければ夢に最も近い住民が考え始める（第4章）。
  - 兆候：決めた日に伝言広場へお知らせ（日数を指で示す）、友達に知れ渡る、荷造り（Packing・家の外に箱）、前日は挨拶回りとお別れ会、友達が贈り物を買って届ける、親しい主人公にはお別れの贈り物。当日朝、友達に見送られて村の外へ歩いて去り、11時に `FormerResidents` へ。
  - 必須サービス職は消えない：転出決定時に弟子（後継者）を決め、出ていく日に職業を引き継ぐ。弟子が見つからなければ転出しない。毎朝 `EnsureEssentialJobs` で不変条件を修復。`FindByJob` は必須職なら必ず誰かを返す。
  - 転出後：数日後に親しかったプレイヤー全員へ手紙（`SharedItemIds.Letter`、`data`＝記録ID。ローカルは持ち物＋`AddMaterial`、接続中のリモートは `Bridge.GrantItemToRemote`＋`AddMaterialFor`＋通知、不在なら参加時に）、伝言広場に近況の声、友達は「寂しい」と話す。10日以上後、まれに1日だけ村へ遊びに来る。
  - 転入：4日目に最初の新住民（第2章）。以後、空き区画があり人数が目標（初期＋3）未満なら数日おきに。転出の後は2〜5日で新住民。新住民は空いた必須職・村にいない職業・少ない系統を優先。村の外から歩いてきて荷ほどき（箱）→ 広場で挨拶。
  - `ResidentMovedInEvent` / `ResidentMoveOutDecidedEvent` / `ResidentMovedOutEvent` を Publish。
- **マルチ**：AI・移動・交流はホストだけ。親しさ・面識・記憶はプレイヤーIDごと。同期は2つに分ける（`ResidentSystem.Sync.cs`）：
  - **軽い状態 `"residents.live"`**（`SyncSubKeys`）：位置・向き・行動・表示/非表示・基本の表情・手に持つ物・箱を運ぶ・立ち話の相手・演出中・一時的な行き先の有無・歩いている時の次の経由点・ホストで家の中の代理を表示中のエリア、と AI が見せた表情・身振り（直近16件、通し番号）。ホストは約0.5秒ごとに `MarkDirtySub`。ゲストは `RestoreSyncState` で補間目標を更新（歩いている時は次の更新までの分を先読み）し、表情・身振りを再生する（参加直後は過去分を再生しない）。中継される発話の表情・身振りもここで届く。
  - **全体状態 `"residents"`**（SaveKey）：記憶・関係・予定・お金・持ち物・引っ越しなど。クラス内の `MarkDirty()` は変更の印を付けるだけで、`TickSync` が最短10秒間隔でまとめて送る（`ResidentTuning.FullSyncMinInterval`）。引っ越しの決定・取りやめ・転出・転入・日付の更新・プレイ開始は `MarkDirtyNow()` ですぐ送る。参加時のスナップショット・セーブは全体状態だけで完全に復元できる（位置も含む）。
- **家の中の代理の姿**：`SetIndoorProxy(id, proxy, areaId, untilMinuteOfDay)` は各端末のローカル（保存・同期しない）。設定中は本体を隠し、`IResidentInfo.Position`／`Transform`（＝吹き出しの位置・中継された発話の位置・`Game.PerformLocalGesture` の距離判定・目撃の判定）と会話ウィンドウの距離判定は代理になる。生活AIの移動・到着・立ち話の距離は本体の位置（`Resident.BodyPosition`）のまま。代理中は外での立ち話を始めない。自動解除：期限、代理の破棄、転出、設定時の一時的な行き先（訪問）の終了、本体が設定時の居場所（行き先／家の前／その場）から10m以上離れた（予定が変わって出かけた）、別の場所への `RequestGoTo`。`OpenConversation(id, player)` は既存の会話ウィンドウを開く（本体が隠れていても代理中なら話せる）。身振りへの反応も代理中は代理の位置で行う。主人公の操作は `ResidentTalkCommand` / `ResidentGiftCommand` / `ResidentGestureCommand` / `ResidentEndTalkCommand`（`WorldAuthority`）。
- **外見データの保存の分け方**：初期住民はシードから再生成（セーブには `genKey` と状態だけ）。新住民はその時点の村の状態（空いた職業・系統の偏り）で作るため、プロフィールごと保存（`regenerable=false`）。
- **演出用 API（統括の依頼）**：`HoldForCutscene(id, hold)`（AI・移動・自動発話・立ち話を止めてその場に留める。`RequestGoTo`・`Speak` は効く）、`Warp(id, pos, yaw)`（内部位置・経路・ゲスト補間目標まで同期）。他モジュールが Transform を直接動かした場合も、AI が最後に書いた位置との差で検出し、その位置を正として経路を作り直す（巻き戻さない）。
- **RequestGoTo**：指定位置へ向かい `untilMinuteOfDay`（過ぎていれば翌日の同時刻）まで留まる。理由タグに ending/farewell/celebr/event を含むと Celebrating。

## 統括との取り決めへの対応

- 物を渡す：①`ctx.TakeItem` で主人公の持ち物から**先に取り除く** → ②`TryHandleItemGiven`。true ならクエスト側に任せて住民は何もしない／false なら贈り物として受け取る（`HandleGiftCommand`）。
- 話しかけた時は `GetRequestTalk` を**先に**取得し、その後に他モジュールのメニュー項目（`GetOptions`）を集める（`OnConversationOpened`）。
- `Gesture.PointAt` の特別な target：`"rocket"`（ロケット位置）、`"hill"`（高台／丘）、`"river"`（最寄りの川）は向きを合わせる。`"cart"` など不明なものは無視。

## 未実装・簡略化

- 住民の家の**室内**での表示：住民側は代理の受け口（`SetIndoorProxy`）だけ。室内の姿・配置は Housing が作り、家に入った端末でだけ代理を設定する（他の端末では本体が家の前に立つ・夜は非表示のまま）。代理の姿の表情・身振りのアニメーションは Housing の姿次第（住民側の `ShowExpression/PlayGesture` は隠した本体に掛かる）。
- 宇宙生物を住民がペットとして飼う（§9）：`ICreatureService` に住民の所有を表す手段が無いため、「生き物を迎える」目標は観察（CatchingCreatures）で進むだけ。
- 料理：料理人は登録済みの安い料理（Food）を毎日「作った」として持つ。レシピとの対応はしていない。家庭料理は材料を消費して記憶に残すだけ。
- 誕生日の暦は仮（28日周期）。恋愛・結婚は【未決定】のため恋人関係は1組のみ・贈り物が増える程度。
- 住民が主人公の家に遊びに来る：家の入口まで来て挨拶するだけ（中に入る・家具を使うのは Housing 側の拡張が必要）。
- 立ち話の「聞こえる会話」は、その端末の主人公が近く（18m）にいる時だけ生成する（遠くの会話は内部の関係変化だけ起きる）。ホストとゲストが同じ立ち話の近くにいると、それぞれ別の言葉が聞こえる（同じ発話を共有はしない）。
- 全体状態は最短10秒間隔なので、ゲストで親しさ・記憶・予定の変化（会話の話題・手を振る判定など）が見えるのは最大10秒ほど遅れる。
- 未接続のプレイヤーへの記録は最大200件（古いものから捨てる）。全体状態に含まれるのでゲストにも送られる（ゲストは使わない）。
- 住民同士の会話の **割り込み**（主人公と同時に話す等）、ゲスト同士の同時会話の調停は未対応。

## Unity 上で確認が必要な点

- 見た目：各系統のプリミティブ構成・大きさ・顔の位置（特に Mixed、四足、浮遊）。表情（目・口・眉・頬）の形が読み取れるか。身振り（手のない系統は体の揺れで代用）。半透明マテリアル（もちもち）。
- 移動：道路ノードへの経路の自然さ、到着判定、坂での高さ、橋。住民同士の重なり。
- 会話ウィンドウ：宇宙語の単語ボタンの折り返し、推測訳の併記、吹き出し（Language）とウィンドウが重ならないか。
- 数値バランス：賃金・予算・買い物頻度（商店の売上）、立ち話の頻度、発話の頻度（うるさくないか）、引っ越しの頻度、新住民の間隔。
- パフォーマンス：住民20〜26人の毎フレーム処理、`FindPath`（A*、List 実装）の頻度、マルチ同期（`residents.live` は数KBを約0.5秒ごと、全体状態（数十KB）は最短10秒ごと）。
- マルチ：ゲストでの住民の歩き方（先読みの補間で滑らかか、曲がり角での行き過ぎ）、AI の表情・身振りがゲストでも見えるか、新住民・転出がゲストにすぐ反映されるか。
- 家の中の代理：代理への [E]・身振り・吹き出しの位置、会話ウィンドウが家の中で閉じないか、訪問の終わり・在宅住人が出かけた時に本体が戻るか（10m の判定）、代理中に家の前に本体が残って見えないか。
- 手紙：ゲストに手紙の品物・資料・通知が届くか、不在だったゲストが参加した時に届くか（Tablet の `AddMaterialFor` の実装に依存）。

## 他モジュールとの連携点

- 呼ぶ契約：`ISpeechService`（Speak（`relayToGuests`）/CreateRecord）、`IManepiService.LeaveMessage("plaza")`、`IShopService.ResidentVisit/IsOpen/EntrancePosition`、`IHousingService.AddFurnitureToResidentHome/PlayerHomeEntrance`、`IQuestService.GetRequestTalk/TryHandleItemGiven/TryHandleGesture`、`IStoryService.GuideResidentId/GuideCatchphraseConcept`、`ITabletService.RecordResidentMet/RecordResidentObservation(For)/AddMaterial(For)/GetPlayerGloss/ShowGlossOverlay`、`INetworkBridge.GrantItemToRemote/NotifyRemote`、`IWeatherService.Current/GetPlanned`、`IFishingService.CatchableFishIds`、`IMiningService.OreIdsOnFloor/FloorCount`、`IRocketService.RocketPosition`。
- Publish：`ResidentMovedInEvent`、`ResidentMoveOutDecidedEvent`、`ResidentMovedOutEvent`、`FriendshipChangedEvent`、`GiftGivenEvent`、`ResidentTalkedEvent`、`ResidentPurchaseEvent`（村の雑貨屋, Where="village_store"）、`PlayerGestureEvent`（会話中の身振り）、`DictionaryLookupRequestEvent`（単語ボタン）。
- 提供する契約（家の中）：`SetIndoorProxy` / `ClearIndoorProxy` / `OpenConversation`（Housing）。同期の副キー `"residents.live"`。
- Subscribe：`PlayerJoinedEvent`（ホスト：不在の間の手紙・観察記録を届ける）、`FishCaughtEvent`、`OreMinedEvent`、`CropHarvestedEvent`、`CreatureCaughtEvent`、`ShopItemSoldEvent`、`RocketDeviceRepairedEvent`、`PlayerGestureEvent`（ローカルの主人公の会話外の身振り）、`QuestStateChangedEvent`（完了→記憶）、`WeatherChangedEvent`（雨宿り）、`ItemCraftedEvent`。
- WorldCommand：`ResidentTalkCommand`、`ResidentGiftCommand`、`ResidentGestureCommand`、`ResidentEndTalkCommand`。
- アイテム：新規登録なし。`SharedItemIds.Letter`（手紙）・`SharedItemIds.GiftBox`（お別れの贈り物の代わり）を使用。
- 記憶の種類（`ResidentMemoryEntry.kind`）：`gift` `quest_done` `met_player` `saw_fishing` `saw_rare_fish` `saw_mining` `saw_harvest` `saw_big_crop` `saw_creature` `saw_repair` `saw_gift` `bought_from_shop` `fished_together` `heard_*`（噂）ほか。他モジュールは `AddMemory` で追加できる（`quest_done` は会話の話題になる）。
- 案内役の規則（Story と共有）：`Lineage==Round && MovedInDay==0` のうち Id 最小。新住民の `MovedInDay` は転入日（≥1）。
- 施設：World が区画ごとの `ResidentHouse` 施設を作っていない場合、住民側で `id="house.plot.<index>"` の `FacilityInfo` を `Layout.Facilities` に追加する。

## 調整用の仮数値の場所

- `ResidentTypes.cs` の `ResidentTuning`（人数・引っ越しの間隔と確率・お金・空腹・親しさの増減・記憶・移動速度・会話頻度・誕生日周期）と `JobTable`（賃金・職場・好みタグ）。
- 生成の分布：`ResidentGenerator.cs`（職業の構成、性格の補正、好みの数、生活時間、外見の幅）。
- 引っ越しの細部：`ResidentSystem.Moving.cs` 冒頭の `DepartureMinute` など、夢の目標値 `DreamTarget`（`ResidentSystem.Social.cs`）。
