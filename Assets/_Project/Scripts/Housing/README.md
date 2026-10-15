# Housing（家・家具・装飾）

`Uchujin.Housing.HousingSystem : GameSystemBase, IHousingService`（SaveKey `"housing"`、DailyUpdateOrder 380）。
仕様：`uchujin_no_hoshi_house_furniture_decor_spec_v0_2.md`（以下「家具仕様」）。部分クラスに分けています。

| ファイル | 内容 |
|---|---|
| `HousingSystem.cs` | ライフサイクル、セーブ（3スコープ）、`IHousingService`、入手元（思い出）の記録、イベント購読、家具を「使う」道具ハンドラ |
| `HousingSystem.Scene.cs` | 家の中（部屋・壁・窓・階段・出口・模様替えボード・留守番マネピ）、庭の範囲、外の扉、見た目の差分同期、壁の切り取り表示、家具を使う（座る・眠る・灯り・音楽・本・窓・鏡…）、音のなる床 |
| `HousingCommands.cs` | WorldCommand 一式とハンドラ（置く・動かす・しまう・壁紙/床・灯り・収納・展示・増築/制作依頼・生き物の住処・思い出メモ・個人収納・個室・訪問設定・届いた物） |
| `HousingSystem.Visits.cs` | 住民の会話メニュー、招待・訪問の進行、家具ごとの反応（意味データ）、複数人の会話・演奏会・食事、留守中の伝言、ふらっと来る訪問、庭のベンチへの立ち寄り |
| `HousingSystem.ResidentHomes.cs` | 住民の家の扉・室内（決定的な間取り＋増えた家具）、住人の姿、引っ越し・入居・購入・贈り物の反映 |
| `HousingSystem.Creatures.cs` | 家・庭の生物（Creatures の姿を家具へ誘導。無い時だけ簡易な姿） |
| `HousingSystem.Carpenter.cs` | 家具職人の店（その日の品揃え・親しさ割引・特別デザイン） |
| `PlacementMode.cs` | 配置モードと操作パネル |
| `HousingWindows.cs` | 家具の詳細と思い出／収納／展示／増築（間取りプレビュー）／制作依頼／家の記録（タブレットアプリ） |
| `FurnitureCatalog.cs` | 家具仕様・カタログ登録（家具95点＋壁紙・床13点＋素材3点）・星の家具文化 |
| `FurnitureVisuals.cs` | プリミティブの家具モデル（約60形）、動き（FurnitureFx）、`FurnitureObject`（[E]） |
| `HouseGeometry.cs` | 間取り・格子・配置可否（GameObject を作らない計算） |
| `ResidentProxy.cs` | 家の中に出す住民の簡易な姿（10系統で形と家具の使い方が違う） |
| `ProceduralMusic.cs` | 楽器の音をその星の音階で合成（音素材なし） |
| `HousingData.cs` | セーブ用データ・【仮数値】定数（`HousingConst`） |

## 実装した内容（仕様との対応）

- **自宅 §2**：`Layout.GetFacility(PlayerHouse).entrance` に扉（ドアマット＋[E]「家に入る」）。家の中はエリア `"house:player"`（IsIndoor・ShowWeather=false）を屋外から離れた座標（`HouseOrigin` = (-6000,0,-6000)）に生成。初期状態：7m×6m の部屋、ベッド（[E]→確認→`SleepHelper.RequestSleep`。時間送りのみ）、収納箱（36枠・Planet スコープ）、机と椅子。出口・留守番マネピ（`IManepiService.SpawnManepi("home:"+家主ID)`）・模様替えボード。部屋ごとに控えめな灯り。
- **増築 §3**：家具職人の会話メニュー「増築・改装を頼む」。部屋の追加（ふつう／ガラス張り／水辺（奥が水場）／テラス（屋外家具可）／2階・ロフト（階段）／地下室（階段））、部屋を横・奥に広げる、庭を広げる。お金＋素材（素材の種類：素材／石・鉱石／結晶・宝石・板ガラス／花）、工期は日数で `OnDayStarted` に完成。完成予想の間取りプレビュー、取り消し（全額返金）。部屋の用途は固定しない（§3.1）。
- **家具の分類・カタログ §4, §7, §8, §9, §17**：寝具・座る・テーブル・収納・照明・趣味（楽器・本棚・釣り具置き場）・展示（水槽・宝石台・化石台・魚拓）・装飾小物・壁紙/床・屋外・生物用。シリーズ12種（ナチュラル／カントリー／レトロ／まんまる／もちもち／星空／クリスタル／浮遊／高級／生物／アンティーク／宇宙人の伝統）＋庭。
  - §7 特殊家具：かたちあわせソファ（座る住民の系統で形が変わる）、ふわふわ浮遊ベッド、ぷるぷるクッション、浮遊展示棚（飾った物が回る）、星結晶ランプ（飾った鉱石で光の色が変わる）、水玉水槽、お天気窓（天気・光雨・星風・逆雨・星の光帯・花潮で色が変わる）、マネピの止まり木（伝言を聞く）、ころころ遊具、ふわもこ寝床、光る花のカーテン（夜だけ光る）、音のなる床（踏むと星の音階が鳴る）。
  - §8 10系統：まんまる族のころがり座布団／もちもち族ののびる椅子／ふわふわ族の毛布かご／ちび動物族の低いクッション／ぷかぷか族の天井ブランコ／四つ腕族の多鍵盤オルガン／星の子族の星図テーブル／小物好きの道具掛け／表情族の大きな鏡／ミックス（かたちあわせソファ）。訪問者の系統で好み・使い方（転がる・伸び縮み・丸くなる・浮く・多腕で演奏）が変わる。
  - §9 住処：ハコネの空き箱、ランプルの灯り、アワリンの水槽、ネムリコの日だまりクッション、ヤネコロの高い棚、ツミツミの石飾り、ハナピカの花瓶、ミズプクの小さな池、マルマドの丸窓、トントンの太鼓、カラポンの色見本棚、モフピのかご、巣箱。種族番号（"031" など）と能力の両方で判定。
  - `ItemCategory.Furniture/Decor` で登録し `ItemTags`（Seating, Table, Bed, Light, Storage, Aquarium, Plant, Music, Toy, Outdoor, CreatureHabitat ほか "soft" "floating" "series_*" "lineage_*"）を付与。
- **星の家具文化 §18**：シードで好まれるシリーズ2つ（品揃えに並びやすく15%安い）・珍しいシリーズ1つ（出にくく30%高い）・木と布の色味・形の丸さ・音階を決定。家具職人の品揃えは日ごとに決定的。
- **配置・模様替え §5**：家具を持って [F]/左クリック、または模様替えボード／タブレットから配置モードへ。WASD で視点移動、マウスで格子（0.5m）、R 回転、クリックで置く／家具を選んで動かす、X でしまう、Q で持つのをやめる、Esc で終了。床・敷物・壁掛け（近い壁に自動で沿う）・天井・テーブルの上（机に載せる）・庭。壁紙・床材はパネルから部屋に貼る（前の物は持ち物に戻る）。**Furniture/Decor カテゴリ（と Furniture タグ、生物の住処）のどのアイテムでも置ける**。他モジュールの家具は `FurnitureCatalog.Resolve` がタグから大きさ・使い方を決める。しまうと持ち物に `data="furn:<instanceId>"` 付きで戻り、置き直しても思い出・中身が保たれる。`FurniturePlacedEvent` を発行。
- **家具を使う §6**：[E] で使う、Shift+[E] で調べる。座る（くつろぐ演出のみ。スタミナなし）、眠る、灯りの点灯／消灯、演奏、水槽をのぞく（魚を入れて泳ぐ）、展示、食卓に料理を並べる、収納、遊ぶ（生物が集まる）、本を読む（宇宙語のまま表示）、窓の外、鏡、時計、作業台。**`ItemTags.Kitchen` の器具は `ICraftingService.OpenStation(CraftStationType.Kitchen, instanceId, player)`**、"workbench" タグは Workbench。
- **家具を増やすと行動が増える §6.1**：訪問者は座れる数・家具の種類・好みで行動を選ぶ。
- **生物との暮らし §9**：家具の詳細から「ここに住まわせる」→ `AssignCreatureCommand` のホスト処理で `ICreatureService.SetPlacementFor(ctx.IssuerId, uid, Habitat, instanceId)`（発行者の個体だけ。先住の他人の子は追い出さない／家主は可）。**ゲストが持ち歩いている子**は、UI がコマンドを送る前に `SetPlacement(uid, Habitat|Home, …)` で個体データごとホストへ預ける（`HousingSystem.SubmitAssignCreature`／`PrepareCreatureForPlanet`。コマンドは順序どおりホストで実行される）。家具を片付けて住処が無くなった子は、その子の飼い主の操作として `Home` へ移す。家・庭の生物の表示は `GetAllOnPlanet()`（今いないゲストが預けた子も出る）。家の中の生物は Creatures モジュールの姿（`OwnedCreatures/Creature_<uid>`）へ公開 SendMessage（`UchujinCreatureAnchor`/`UchujinCreatureWork`）で家具を指示して歩き回らせる（能力・時間帯：光る生物は夜の灯り、眠り好きは寝床、箱好きは箱、水の生物は水槽、花の生物は花、音の生物は楽器、ヤネコロは高い棚、マルマドは窓）。姿が見つからない時だけ本モジュールの簡易な姿（同じ寝床で一緒に丸くなる・箱に隠れた仲間を眺める・カラポンが家具の色になる等）。
- **庭 §10**：家の右側の決められた範囲（杭で表示、増築で拡張）に屋外の家具を置ける。住民（花好き・花壇の好み）が日中ベンチの前へ立ち寄る（`RequestGoTo`）。野生生物の誘致は Creatures モジュールが `GetPlayerHomeFurniture` の庭家具を見て行う（重複を避けるため本モジュールは出さない）。
- **住民の招待 §11**：会話メニュー「家に招待する」（親しさ10以上）→ 時刻を選ぶ → 住民がうなずく／首を振る（時刻を指で示す）。約束の45分前に `RequestGoTo` で家の前へ。家に誰かいれば家の中に住民の姿（`ResidentProxy`）が入り、`IResidentDirectory.SetIndoorProxy(residentId, 姿, "house:player", 訪問の終了時刻)` で本体の代わりになる（本体は隠れ、吹き出し・身振りの対象・位置が家の中の姿に。帰り始める・後片付けで `ClearIndoorProxy`）。姿に [E] で `OpenConversation`（いつもの会話メニュー）、身振りの返事は住民モジュールが返し、返事の身振りは `UtteranceSpokenEvent` を見て姿でも演じる。15分ごとに家具を選んで使い、家具・住民の好み（`EvaluateItem`）・職業・系統・贈り物・生物で反応（意味データで `ISpeechService.Speak`／`IResidentDirectory.Speak`）。食卓の料理を食べる（おいしい／まずいは本当の好み）、楽器を演奏し他の訪問者が拍手、複数人で会話（§11.3、`AddMemory` で住民同士の記憶）。帰り際に親しさ変化・記憶。留守・睡眠中なら家庭用マネピへ伝言（§13）。親しい住民は休みなくても時々ふらっと来る。代理として登録できなかった時だけ、姿に [E]／身振りで挨拶すると本モジュールが返す。
- **家庭用マネピ §13**：家の入口に留守番マネピ、止まり木家具でも伝言を聞ける。家に入った時に新しい伝言があれば知らせる。家具職人の完成連絡も伝言で届く。
- **家具の思い出 §14**：入手元（家具職人から買った／作ってもらった／住民からもらった（`ItemAddedEvent.Source` の住民ID）／自分で作った／お礼）、置いた日・場所・その日の天気（光雨の日など）、座った住民・演奏・食事・生物が住み始めた・贈り主が見に来た・贈り主が引っ越した、友達（マルチ）が座った、自由メモ。発話の記録IDを持ち「声を聞く」「音声カードに保存」。★印（自動では消えない）。`RecordFurnitureMemory` で他モジュールからも追加可。
- **住民の家 §15・村仕様 §5**：`FacilityType.ResidentHouse` の入口に扉。在宅時だけ入れる（寝ていれば返事がない／留守）。間取り・壁紙・床・基本家具は住民ID・職業・系統・シードから決定的。`AddFurnitureToResidentHome` と購入／贈り物イベントで家具が増える（重複しないよう対応付け）。荷造り中は箱が積まれ家具が減る、引っ越すと家具も無くなり、贈り物の思い出が残る。住人の姿が家具を使い（ソファ・楽器・夜は寝床）、在宅中はこの姿も `SetIndoorProxy(residentId, 姿, AreaIds.ResidentHouse(id), 1時間ごとに延長)` で本体の代わりにする（[E] で会話。この端末だけの登録）。住民が出かける行動（仕事・広場・訪問など）になったら姿は出口から出ていき `ClearIndoorProxy`。増えた家具・自分が贈った家具を住民辞典に事実として記録。
- **マルチ §20**：配置系はすべて WorldCommand（CoLiving 以上）。家具の所有者（片付けて持ち帰れるのは所有者だけ、動かすのは可）、個人用収納（所有者だけ開ける）、個室（家主が割り当て）、ゲストの立ち入り設定、友達が座った思い出。
- **経済 §21**：家具 80G〜15,000G、増築 3,000G〜18,000G＋素材。帰還条件とは無関係。家具職人の売上は `GiveMoney` で職人の収入に。
- **タブレット**：アプリID **`"house"`**（家具の一覧と思い出、招待・注文の予定、生き物、家の設定と「模様替えを始める」）。

## 未実装・簡略化

- 外観改装・増築部分の外観：家の外観は World が作るため変更していない（家の中だけ広がる）。
- 2階・地下は別の場所に並べ、階段で移動（立体的に積み重ねていない）。
- 家具の色違い・家具ごとの色変更、取り消し（Undo）、配置の保存・呼び出しは未実装。
- 写真（§14 写真）は未実装（写真の仕組みが Core に無い）。
- 訪問中の住民本体は家の前に立ったまま（`RequestGoTo`）で、表示・会話・身振りは代理の姿へ振り替えている。本体を隠す・吹き出しを代理に出す・身振りの対象にするのは Residents 側の `SetIndoorProxy` の実装に依存する。
- 訪問者の発話（家具への反応など）は従来どおり `ISpeechService.Speak(u, 姿の Transform, …)` で出している（ゲストへの中継は Language 側の扱い）。
- 住民の家の代理は入った端末だけの登録（他のプレイヤーからは本体が見える）。ホストが住民の家にいる間は、その住民の `IResidentInfo.Position` が家の中の位置になる。
- 庭の野生生物の出現は Creatures に任せている。
- 家具の当たり判定は足跡の箱。通路の確保は「入口・階段・部屋の出入口の前」を予約しているだけ。

## Unity 上で確認が必要な点

- 家の中の見た目（壁の切り取り表示の角度しきい値、壁の当たり判定 1.1m の高さでジャンプで越えないか、カメラの寄り）、窓・灯り（点光源の数とパフォーマンス）。
- 配置モードの操作感（マウスの平面判定、庭の地形の高低差、パネル上のクリック判定）。
- 座る演出（見た目の縮み・持ち上げ）、訪問者の歩き（部屋をまたぐ経路）、住民の家の家具の並び。
- 合成音の音量・音色。
- 訪問者・住民の家の住人の姿に [E] で会話が開くか、本体が隠れて吹き出しが姿の頭上に出るか（Residents の `SetIndoorProxy` 実装と合わせて）。身振り（G）の返事が二重にならないか。住民の家で住人が出かける時に姿が出口へ歩いて消えるか。
- マルチ：ゲストが持ち歩いている生き物を住処・家の庭に置けるか（預ける→配置の2コマンド）、連れて行くと手元に戻るか。
- 数値バランス（価格・増築費・工期・親しさの増え方・招待の成功率・ふらっと訪問の頻度）。

## 他モジュールとの連携

- **呼ぶ契約**：`IResidentDirectory`（RegisterInteractionProvider, RequestGoTo, Speak, SetIndoorProxy, ClearIndoorProxy, OpenConversation, ChangeFriendship, AddMemory, GiveMoney, FindByJob, Get, ActiveResidents）、`IResidentInfo.EvaluateItem/GetFriendship/Knows/Lineage/Job/Activity/MainColor`、`ISpeechService`（Speak, CreateRecord, Render, ResidentName, GetRecord, Replay）、`IManepiService`（SpawnManepi, LeaveMessage, GetMessages, OpenBoard）、`ITabletService`（RegisterApp, RecordResidentObservation, SaveVoiceCard, Open/Close）、`ICreatureService`（GetOwned, GetAllOnPlanet, Get, SetPlacementFor（コマンド処理内）, SetPlacement（ゲストの UI で預ける時だけ）, GetSpeciesAbilities, GetSpeciesName）、`ICraftingService.OpenStation`（Kitchen / Workbench / CarpenterBench）、`IWeatherService`、`INetworkService.GetPlayerName/ConnectedPlayerIds`、`SleepHelper.RequestSleep`。
- **発行**：`FurniturePlacedEvent`、`CreatureObservedEvent`（簡易な姿をなでた時）。
- **購読**：`UtteranceSpokenEvent`（代理の姿の身振り）, `GiftGivenEvent`, `ResidentPurchaseEvent`, `ResidentMovedInEvent`, `ResidentMovedOutEvent`, `ResidentMoveOutDecidedEvent`, `ItemAddedEvent`, `ItemCraftedEvent`, `AreaChangedEvent`, `SleepStartedEvent`, `SleepEndedEvent`, `PlayerGestureEvent`。
- **住民の記憶の kind**：`gift_placed_at_home` `ate_at_player_home` `chatted_at_player_home` `visited_home` `visited_home_together` `visit_missed` `player_visited_home`。
- **発話の contextTag**：`housing.invite_accept/decline`、`housing.visit_greet/furniture/room/listen/chat/end/missed`、`housing.construction_accept/done`、`housing.home_welcome`、`housing.book` など。
- **アイテムID**：家具 `furniture.*`（95点）、壁紙・床 `decor.wall_*` `decor.floor_*`（13点）、素材 `housing.lumber` `housing.stone_block` `housing.glass_pane`。
- **コマンド**：`PlaceFurnitureCommand` `MoveFurnitureCommand` `StoreFurnitureCommand` `SetRoomSurfaceCommand` `ToggleFurnitureLightCommand` `StorageTransferCommand` `DisplayItemCommand` `OrderConstructionCommand` `CancelConstructionCommand` `InviteResidentCommand` `CancelInvitationCommand` `AssignCreatureCommand` `FurnitureNoteCommand` `SetStoragePrivateCommand` `SetRoomOwnerCommand` `SetGuestEntryCommand` `TakeDeliveryCommand`。
- **エリアID**：`house:player`、住民の家 `AreaIds.ResidentHouse(residentId)`、空き家 `house:vacant:<facilityId>`。
- `GetPlayerHomeFurniture(ownerId)` は共同生活の1軒を共有するため、どの ownerId でも家全体の家具を返す（`OwnerId` で持ち主が分かる）。

## 調整用の仮数値

- `HousingData.cs` の `HousingConst`（格子 0.5m、部屋の大きさ・上限、庭、収納36枠、訪問の時間・間隔・人数、招待に必要な親しさ、ふらっと訪問、思い出の上限、家具職人の品揃え・割引・特別デザイン、制作依頼の価格率・日数、屋内の座標）。
- `FurnitureCatalog.cs`：各家具の価格・大きさ・座れる数・収納枠・展示枠、星の文化（`PrepareCulture`、`PriceRate`）。
- `HousingCommands.cs` の `ConstructionCatalog.Options`（増築の価格・工期・素材）。
- `HousingSystem.Visits.cs`：招待の成功率（0.55＋親しさ/150）、親しさの増え方、家具の興味（`Affinity`）。
