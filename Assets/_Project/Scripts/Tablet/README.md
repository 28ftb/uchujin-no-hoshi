# Tablet モジュール（タブレット・宇宙語辞書・住民辞典・音声カード・資料ボックス・マネピの伝言板）

システム：`Uchujin.Tablet.TabletSystem : GameSystemBase, ITabletService, IManepiService`（SaveKey `"tablet"`、DailyUpdateOrder 720）

主な仕様：`uchujin_no_hoshi_tablet_dictionary_residents_spec_v0_3.md`（以下「タ§」）、`uchujin_no_hoshi_language_spec_v0_2.md`（「言§」）。
最新の決定に従い、旧案の「顔付き付箋」は使わず **マネピの声・表情・仕草の再現＋音声カード** で実装しています。

## ファイル構成

| ファイル | 内容 |
|---|---|
| `TabletSystem.cs` | ライフサイクル、セーブ（スコープ分け）、イベント購読、`ITabletService`（開閉・アプリ登録・推測訳・辞書の書き出し/取り込み） |
| `TabletSystem.Knowledge.cs` | 辞書・音声カード・資料・住民辞典のデータ操作（`SaveVoiceCard` `AddMaterial` `RecordResidentMet` `RecordResidentObservation`、自動索引） |
| `TabletSystem.Remote.cs` | 個人への記録（マルチ）：`AddMaterialFor` `RecordResidentObservationFor`、一時メッセージ `"tablet.record"` の送受信、ホストでしか起きない出来事のゲスト本人への転送、発話の原本の自前保持 |
| `TabletSystem.Manepi.cs` | `IManepiService`（伝言板・`LeaveMessage`・`SpawnManepi`・`OpenBoard`）、伝言広場のマネピ設置、身振りの真似、伝言消去コマンド |
| `TabletData.cs` | セーブ用 DTO（JsonUtility 用。Dictionary 不使用） |
| `TabletTuning.cs` | 【仮数値】の定数と、端末設定（PlayerPrefs） |
| `TabletWindow.cs` | タブレット本体の画面（ホーム・戻る・アプリ表示）、内蔵アプリ基底 `TabletAppBase` |
| `TabletGui.cs` / `TabletText.cs` | 顔アイコン・宇宙語の単語の並び・線描画・写真読み込み／綴りの正規化・語の区切り・差分・日時・場所・表情・身振りの記述 |
| `TabletCamera.cs` | タブレットのカメラ（撮影モード・PNG 保存・写っていた住民の記録） |
| `TabletOverlay.cs` | ピン留め表示、[K] 保存の案内、ファインダー枠 |
| `Apps/DictionaryApp.cs` | 宇宙語辞書（単語ページ・文法ノート・別冊・渡す/受け取る） |
| `Apps/VoiceCardsApp.cs` | 音声カード |
| `Apps/ConversationLogApp.cs` | 会話ログ |
| `Apps/MaterialsApp.cs` | 資料ボックス |
| `Apps/ResidentsApp.cs` | 住民辞典（ページ・写真・人間関係図） |
| `Apps/SettingsApp.cs` | 設定 |
| `Manepi/ManepiBehaviour.cs` | マネピの見た目（プリミティブ）・待機演出・真似・再現演技・[E] 操作 |
| `Manepi/ManepiBoardWindow.cs` | 伝言一覧（再生・音声カードとして保存・消す） |

## 実装した内容（仕様との対応）

### タブレット本体（タ§2）
- Tab で開閉（コアが `Open/Close` を呼ぶ）。`UIWindow` のモーダル。ホームに登録アプリを `IconGlyph`/`IconColor` のアイコンで並べ、「ホーム」「戻る」（アプリ内の戻る→前のアプリ→ホーム）。
- `Open(appId)` で直接アプリへ（"map" 等が未登録ならホーム）。
- ソロでは `TimeSystem.PauseInSoloWhenTabletOpen` が true の間、開いている間は時間停止（`UIWindow.PausesTimeInSolo`。マルチではコアが個人ポーズを無視）。設定アプリから切り替えると開いたままでも即反映。
- `TabletOpenedEvent{AppId}` / `TabletClosedEvent` を Publish。
- 外部アプリは `RegisterApp`（同じ AppId は置き換え、内蔵と同じ ID なら外部優先）。`Teardown` で外部アプリを消去。

### 宇宙語辞書（AppId `dictionary`、タ§6・言§7）
- 記録単位は表面形（綴り）。種類：単語／語の一部（接辞らしき音）／文章。
- 単語ページ：意味の候補を複数（各候補に確信度マーク：仮説／たぶん／自信あり／？／ちがった）、「表示に使う訳」◉の選択、品詞の推測、自由メモ、タグ、関連語、語の一部を別ページとして登録、**証拠リンク**（音声カード／資料。一言メモ付き）、他人から受け取った例文、**その語が出てきた記録の自動索引**（保存した音声カード・資料に結び付いた発話・最近の会話ログから同じ綴りを含むものを並べる。意味は示さない）、編集の足跡（いつ何を書いたか）、ピン留め。
- **ゲームは正解へ書き換えない／誤訳もそのまま**。「ちがった」にした候補も消さずに残せる（タ§6.1）。
- 単語を押す → `DictionaryLookupRequestEvent` を発行 → 購読してページを開く（無ければ「単語／語の一部／文章として作る」画面）。`RecordId` 付きなら「この記録を証拠に追加」ボタンを出す（未保存の会話は自動で音声カード化して証拠にする）。
- 文法ノート（タ§6.4）：自由ページ＋2枚の音声カードの並列比較。LCS による「もう一方に無い語」を橙で自動強調、単語を押して自分の印（紫）を付けられる（または辞書を引くモードに切替）。根拠カードを貼れる。
- 検索：綴り・自分の訳・メモ・タグ。並び替え（更新順／綴り順／作成順）、タグ絞り込み。**内部の正解での逆引きはしない**。
- `DictionaryEntryEditedEvent` を編集ごとに Publish。
- 推測訳の補助表示（タ§6.5）：設定 `ShowGlossOverlay`（既定オフ、PlayerPrefs）。`GetPlayerGloss(surface)` は本人が◉で選んだ候補、未選択なら「ちがった」以外の最初の候補（どちらも本人の記述）。タブレット内の宇宙語表示でも、オンなら単語の下に自分の訳を別色・斜体で添える。
- 辞書の書き出し／取り込み（言§12、マルチ§13）：`ExportDictionary(onlySurfaces)` は JSON（仮説・メモ・タグ・関連語・品詞＋証拠カードの原文を例文として添付。内部の意味データは含めない）。`ImportDictionary(json, from, asSeparateVolume)`：
  - 別冊：読み取り専用で保管、1語ずつ「自分の辞書へ写す／自分のページに追記」。
  - 統合：自分の記述は上書きせず、相手の仮説を「◯◯さんの説」として追記、メモ・タグ・例文を追記。
  - 正誤は判定しない（嘘の辞書も成立）。別の星の辞書らしい時は確認を出す。自動配布はしない。
  - タブレット内でもクリップボード経由で書き出し・取り込みできる（Network の手渡し UI が無くても試せる）。

### 音声カード（AppId `voicecards`、タ§5）
- 原本（`UtteranceRecord` のコピー。保存後は変更しない）と、注釈（推測した意味／住民について思ったこと／メモ／タグ／ピン／「この声の主」指定）を別クラスで保存（タ§5.3）。
- 表示：話し手の顔アイコン（`IResidentDirectory.Get(id).MainColor`・系統の特徴・その時の表情）、宇宙語（単語クリックで辞書）、表情・身振りの様子、日時・場所・天気・その場にあった物（アイテム名・辞典の呼び名）・保存元。
- 再生（`ISpeechService.Replay` を主人公の位置で）、住民辞典に貼る、話し手・場所・タグで絞り込み、テキスト検索、新しい順／古い順／話し手ごと。
- 話し手が分からない／違うと思う場合は本人が「この声の主」を指定でき、原本の顔は変えない（タ§5.2）。
- このカードが貼られている辞書・住民ページ・文法ノートを一覧。捨てると各リンクも外れる（原本以外は残る）。
- **[K]（その場で保存）**：話しかけられた／聞こえた直後 8 秒以内に [K] で直前の発話を音声カードに保存（タ§8.4「ワンボタンで保存」）。画面下に案内を出す。設定でオフにできる。

### 会話ログ（AppId `log`、タ§8.4）
- `ISpeechService.RecentRecords` を新しい順に表示。単語クリックで辞書、▶再生、「音声カードとして保存」（保存済みならカードを開く）。

### 資料ボックス（AppId `materials`、タ§8）
- `AddMaterial` で入った資料（売上・依頼・手紙・看板・写真・予報・メモ等）を種類別タブで。音声カードはカードの原本を参照して並べる（二重複製しない）。
- 資料詳細：事実（`factJa/En`）・日時・結び付いた発話（再生・カード化）・関係する住民（顔・呼び名）・関係する物・写真。
- プレイヤーのメモと関連付け（住民・単語）は `MaterialNote` に別保存。単語に関連付けると辞書の証拠（material）にもなる。

### 個人への記録（マルチ：ホストで起きた事実をゲスト本人へ）
- `AddMaterialFor(playerId, material, linkedRecord)` / `RecordResidentObservationFor(playerId, residentId, factJa, factEn, linkedRecordId)`：宛先がローカルなら `AddMaterial` / `RecordResidentObservation` と同じ。リモートなら（ホスト側のみ）`WorldAuthority.SendTransient(playerId, "tablet.record", json)` で `TabletRecordMessage`（資料・観察・結び付いた発話の原本・はじめて話した住民）を送り、受信側の `RegisterTransientHandler("tablet.record")` が自分の知識（PlayerPlanetKnowledge）へ入れる。
- 受信した発話の原本は `KnowledgeData.receivedRecords` に保持し、`ResolveRecord`（カード→マネピ→会話ログ→この写し）で引けるので、ゲストの会話ログに無い発話でも資料から再生・音声カード化・辞書の証拠にできる。ローカルの `AddMaterialFor` で原本を渡した時も写しを持つ（会話ログから消えても引ける）。上限 `TabletTuning.MaxReceivedRecords`（参照されていない古いものから整理）。
- 受信した資料は「資料ボックスに記録が届きました（題名）」とトースト。ホスト（権威側）はゲストからの `"tablet.record"` を受け付けない（ゲストからホストの記録を書き換えさせない）。
- ホストでしか発行されない共有イベントを、本人がリモートのゲストなら本人へ転送する：`ResidentTalkedEvent`（住民辞典のページ作成。場所は受信側の主人公の位置）、`GiftGivenEvent`（贈った事実）、`ShopItemSoldEvent`（委託品の持ち主へ購入の事実）、`QuestStateChangedEvent`（依頼の達成）。同じ事実が続けて起きた時（まとめ買い）は1回だけ送る。

### 住民辞典（AppId `residents`、タ§7・言§8.5）
- `RecordResidentMet` でページ作成（通し番号 No.xxx、初めて会った日・場所を事実として記録、トースト）。`ResidentTalkedEvent`（自分）でも自動で呼ぶ。
- 名前は分からない前提：あだ名・聞き取った名前メモはプレイヤーが書く。内部名・本名は表示しない（表示名はあだ名→聞き取った名前→No.xxx）。
- 自動で入るのは**観察できた事実だけ**（タ§7.3）：`RecordResidentObservation`、初対面、商店での購入（`ShopItemSoldEvent`）、自分の贈り物（`GiftGivenEvent`）、依頼の達成（`QuestStateChangedEvent`）、写真、引っ越し／再入居。同じ日の同じ事実は重複しない。
- プレイヤーの欄：仕事・性格・好き・苦手・よくいる場所/時間・自由メモ・タグ（推測。訂正されない）。
- 声と資料：顔が同じ音声カード（本人が話し手指定したものを含む）、手動で貼ったカード・資料、その住民に結び付いた資料（売上記録など）。
- 写真（タ§7.4）：カメラで撮影（下記）。写っていた住民のページに自動で貼られる。
- 人間関係図（タ§7.5）：顔をドラッグで配置、「線をつなぐ」で2人を選んでラベル入力、ラベルを押すと書き直し・色変更・削除。正解は表示しない。
- 引っ越し（タ§7.6）：`ResidentMovedOutEvent` で「引っ越した」印（顔を暗く＋スタンプ）。ページ・声・写真は残る。再入居で印を外す。会っていない住民の記録（商店の売上など）は「記録だけある住民」に分類。

### カメラ（タ§7.4, §11.3）
- 住民辞典／資料ボックスの「写真を撮る」→ タブレットを閉じて撮影モード（非モーダルの小窓＋画面のファインダー枠）。歩いたりカメラを回して構図を決め、[Enter] か「撮影」で `Camera.main` を RenderTexture に描画 → PNG を `Application.persistentDataPath/photos/<worldId先頭16字>/<id>.png` に保存。
- 撮影時に画面内（端4%を除く・30m以内・遮蔽物レイキャストなし）に写っていた住民 ID を記録。資料ボックス（Photo）に追加し、住民ページに貼る。写真から名前・感情・文字を解析しない。
- Esc／やめる でタブレットに戻る。Tab でもタブレットに戻る。

### 設定（AppId `settings`）
- 推測訳の併記、宇宙語の文字サイズ（小/標準/大）、[K] 保存、ピン留め表示、マネピの真似、ソロ時の時間停止。

### マネピの伝言板（IManepiService、タ§4・§9、家具§13、天候§14）
- 伝言板 ID：`"plaza"`（伝言広場）、`"shop"`（商店）、`"home:<playerId>"`（自宅）ほか任意。
- `LeaveMessage(boardId, record)`：原本をコピーして保存（Planet スコープ・ホストが正本、`MarkDirty`）。同じ recordId は無視。1板最大 24 件、日数で整理（広場7日・商店7日・自宅21日・他10日、`OnDayStarted` でホストのみ）。`ClearMessagesOlderThan` も実装。
- `SpawnManepi(boardId, parent, localPos, labelJa, labelEn)`：止まり木にとまった丸いラベンダー色のマネピ（耳・目・ほっぺ・くちばし・しっぽ）をプリミティブで生成。[E] で `OpenBoard`。頭上に呼び名ラベル（新着数）と、未聴の伝言があると「！」が浮かぶ。近くの主人公の方を向き、まばたき・揺れ。
- 待機中は `PlayerGestureEvent` を購読し、10m 以内なら主人公の身振りを小さく真似する（うなずく・首振り・手を振る→耳を振る・お辞儀・拍手→耳を合わせる 等）。
- `OpenBoard`：伝言一覧（預けた住民の顔・日時・【新】）→ ▶再生（マネピ位置で `ISpeechService.Replay`、マネピが話し手の色に染まり系統の特徴（星・多腕・耳）を少し真似て、表情と身振りを再現。指で示した数は頭上に光の玉を数だけ並べる、差し出した物はその色の箱を持つ）→「音声カードとして保存」。未聴の伝言は文字も伏せる。「新しい伝言を続けて聞く」。消す（自宅は持ち主、共有板は共同生活以上の権限。`ManepiDeleteMessageCommand` でホスト権威）。
- 伝言広場：`Game.Instance.Layout.GetFacility(FacilityType.MessagePlaza)` の近く（施設中心から回転方向に (1.4, 0, 0.6)）に `BuildScene` で設置。
- 全会話を自動記録しない（タ§4.3）：マネピに入るのは他モジュールが `LeaveMessage` したものだけ。

### 表示の原則
- 主人公の言語で表示するのは、タブレットの UI と主人公自身の記録（日時・場所・見えた表情や身振りの記述・自分のメモ）だけ。住民の言葉の訳は出さない。`internalId`／`meaning` は表示しない。
- 宇宙語（原音の綴り）は緑系の太字、プレイヤーの推測は橙の斜体、観察できた事実は水色で区別（タ§11.2）。

## セーブのスコープ
| スコープ | 内容 |
|---|---|
| `Planet` | `ManepiPlanetData`（各伝言板の伝言） |
| `PlayerPlanetKnowledge` | `KnowledgeData`（辞書・文法ノート・別冊・音声カード＋注釈・資料＋メモ・住民辞典・関係図・写真情報・既聴の伝言ID・資料や観察に結び付いた発話の原本の写し `receivedRecords`） |
| `PlayerProfile` | なし（端末の好みは PlayerPrefs） |

- 星ごとの分離はコアの Knowledge スコープ（訪問先は `ReadVisitKnowledge`）に任せている。写真ファイルも worldId ごとのフォルダ。
- `RestoreState(Planet)` はデータを置き換えるだけ（マネピの新着表示は毎フレームデータを読むので冪等）。

## 未実装・簡略化
- **音声そのもの**：音声の生成・再生は Language 側の `Replay`（吹き出し）に任せている。声色の模倣（【提案】）は色の変化と系統の特徴で表現。
- **自由配置スクラップブック**（タ§7.4 の自由配置ページ）：一覧＋関連付け方式のみ。ドラッグでカードを貼る UI は未実装（ボタン／選択ダイアログで貼る）。
- **住民辞典の部分共有・音声カードの共有**（マルチ§13.2 拡張提案）：辞書のみ書き出し/取り込みを実装。カード原文は例文として添付。
- **文字（宇宙文字）の画像資料**：看板・手紙は他モジュールが `AddMaterial(Sign/Letter)` で事実と `linkedRecordId` を渡す前提。画像としての宇宙文字は扱わない。
- 文法ノートの「部分文字列への印」は単語単位（語の一部は辞書の「語の一部」ページで扱う）。
- 伝言の再生順・件数・保持日数は【未決定】のため仮数値。
- マネピの捕獲・飼育と伝言能力の関係（Creatures モジュール側）は未連携。
- 個人への記録の一時メッセージは保存・再送しない。宛先のゲストが接続していない時（退出中）や、参加直後に自分の知識を読み込む前に届いた分は失われる（商店の売上レポート・依頼帳など、星のデータから後で見られる記録は残る）。

## Unity 上で確認が必要な点
- IMGUI の配置（1080p 以外の解像度・横長画面）、文字サイズ、長い宇宙語の折り返し（`TabletGui.WordFlow`）、スクロール量。
- 日本語 IME での TextField/TextArea 入力、入力中に Tab/Esc を押した時の挙動（Esc はタブレットごと閉じる）。
- 人間関係図のドラッグ（`Event.current` の MouseDrag/MouseUp）と `GUIUtility.RotateAroundPivot` による線描画が `GUILayout.BeginArea` 内で正しくずれずに描けるか。
- カメラ：URP で `Camera.Render()` + `ReadPixels` が正しく撮れるか（URP のバージョンによっては `RenderPipeline.SubmitRenderRequest` が必要）。写っていた住民の判定（レイキャストが住民のコライダーや地形に当たるか）。非モーダル小窓の「撮影」ボタンを押した時に道具使用（左クリック）も同時に起きないか。
- マネピの見た目（大きさ・色・止まり木の高さ・ラベル位置）、再現演技のテンポ、`MaterialPropertyBlock` での色変更（URP/Standard 両方）。
- `ISpeechService.Replay(record, マネピのVoiceAnchor)` で吹き出しがマネピの頭上に出るか。
- ピン留め・[K] 案内の表示位置が HUD と重ならないか。
- 大量のカード・辞書項目（数百件）での IMGUI の重さ（各フレームで一覧を作り直している）。
- マルチ：ゲストの委託品が売れた時・ゲストが依頼を達成した時・ゲストが住民に贈り物をした時に、ゲストのタブレットへ資料・住民辞典の観察が届くか（`"tablet.record"`。Network の `SendTransientTo` → `DispatchTransient`）。届いた資料の「結び付いた発話」を再生・音声カード化できるか。

## 他モジュールとの連携点
### 呼ぶ契約
- `ISpeechService`：`RecentRecords`、`GetRecord`、`Replay`（会話ログ・カード・マネピ再生）。
- `IResidentDirectory`：`Get(id).MainColor/Lineage`（顔アイコン）、`ActiveResidents`（写真判定）、`Get(id).Transform`（マネピの指さし）。
- `TimeSystem`：`Day/MinuteOfDay/PauseInSoloWhenTabletOpen/Add/RemovePersonalPause`。
- `WorldLayout.GetFacility(FacilityType.MessagePlaza)`、`GetAreaAt`、`HeightAt`、`AreaManager.Get`（場所名）。
- `ItemRegistry`（物の名前・色）。

### 提供する契約
- `ITabletService`：`RegisterApp` `Open` `Close` `IsOpen` `SaveVoiceCard` `AddMaterial` `RecordResidentObservation` `RecordResidentMet` `AddMaterialFor` `RecordResidentObservationFor` `GetPlayerGloss` `ShowGlossOverlay` `ExportDictionary` `ImportDictionary`。
  - ホストで起きた事実を本人へ記録する時は `AddMaterialFor(playerId, …)` / `RecordResidentObservationFor(playerId, …)` を使う（ローカルなら従来と同じ）。
- `IManepiService`：`LeaveMessage` `GetMessages` `ClearMessagesOlderThan` `SpawnManepi` `OpenBoard`。
- 利用例：Shop → `SpawnManepi("shop", …)`・`LeaveMessage("shop", rec)`・`AddMaterial(SalesRecord)`・`SaveVoiceCard`。Housing → `SpawnManepi("home:"+playerId, …)`。Residents → `RecordResidentMet`/`RecordResidentObservation`/`LeaveMessage("plaza", …)`（予報・引っ越しのお知らせ・行事の告知）。Quests → 依頼帳アプリを `RegisterApp`、`AddMaterial(QuestRequest)`。Network → `ExportDictionary`/`ImportDictionary`。Language → `ShowGlossOverlay`/`GetPlayerGloss`、単語クリックで `DictionaryLookupRequestEvent`。

### イベント
- 発行：`TabletOpenedEvent`、`TabletClosedEvent`、`DictionaryEntryEditedEvent`、`DictionaryLookupRequestEvent`（タブレット内で単語を押した時）。
- 購読：`DictionaryLookupRequestEvent`、`ResidentMovedOutEvent`、`ResidentMovedInEvent`、`ResidentTalkedEvent`、`GiftGivenEvent`、`ShopItemSoldEvent`、`QuestStateChangedEvent`、`PlayerGestureEvent`、`UtteranceSpokenEvent`。
  - 商店の購入・贈り物・依頼達成は Tablet が自動で住民辞典に事実として記録するので、Shop/Residents/Quests が同じ内容を `RecordResidentObservation` で重ねて送る必要はない（同じ日・同じ文なら重複しない）。

### コマンド
- `ManepiDeleteMessageCommand { boardId, recordId }`（伝言を消す。ホスト権威）。

### 一時メッセージ（`WorldAuthority.RegisterTransientHandler`）
- `"tablet.record"`：`TabletRecordMessage`（ホスト → 本人。資料・観察・発話の原本・はじめて話した住民）。

### アイテム
- 登録なし（マネピの生物種 `SharedItemIds.SpeciesManepi` は Creatures 担当）。

## 調整用の仮数値の場所
- `TabletTuning.cs`：伝言の最大件数・保持日数、真似の距離・遅れ、[K] 保存の受付時間とキー、シャッターキー、写真の幅・判定距離・余白、ピン表示数、編集履歴の行数、索引の最大件数。
- `TabletSettings`（同ファイル）：設定の既定値（推測訳オフ・文字サイズ標準など）。
- マネピの色・大きさ・動きの速さ：`Manepi/ManepiBehaviour.cs` の各定数・各動作関数。
- 伝言広場でのマネピの位置：`TabletSystem.Manepi.cs` の `BuildPlazaManepi`。
