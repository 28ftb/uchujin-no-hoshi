# Shop モジュール（セルフ販売の商店）

`Uchujin.Shop.ShopSystem : GameSystemBase, IShopService`（SaveKey `"shop"`、DailyUpdateOrder `450`）。
Core だけに依存し、他モジュールとは `Game.Instance.Service<IXxx>()`（null チェック済み）と `EventBus` で連携する。

## ファイル構成

| ファイル | 内容 |
|---|---|
| `ShopSystem.cs` | ライフサイクル・セーブ・`IShopService`（IsOpen / EntrancePosition / OfferCount）・日次処理・看板による立ち寄り・店の配置（`ShopFrame`） |
| `ShopSystem.Purchase.cs` | `ResidentVisit`：棚を見て回る → 購買判断 → 取引 → 反応の発話 → マネピの記録 → 売上記録 |
| `ShopSystem.Commands.cs` | `WorldCommand` とハンドラ（並べる・下げる・値段・自動補充・入替・倉庫・営業時間・改装の依頼・委託の精算） |
| `ShopReactions.cs` | 住民の反応を **意味データ**（`Utterance`/`MeaningFrame`）で組み立てる |
| `ShopRules.cs` | 陳列台の可否・鮮度・目的との一致・職業の関心・天気/時間帯・概念の対応表・住民の買い物傾向（`ShopTraits`） |
| `ShopData.cs` | セーブ用 DTO（JsonUtility 対応） |
| `ShopConfig.cs` | **調整用の仮数値と改装テーブル（すべてここ）** |
| `ShopVisuals.cs` | プリミティブの店構え・棚・商品・値札・マネピ・倉庫箱・営業時間の看板・要望ボード・改装の見た目・演出 |
| `ShopReportView.cs` | 売上レポート・来店履歴・値段の比較・棚・要望・未精算（帳簿とタブレットで共通）＋顔/形アイコン |
| `ShopWindows.cs` | 帳簿（管理画面）・並べる/値段/倉庫ダイアログ・要望ボード・改装の依頼・代替マネピ画面 |
| `ShopTabletApp.cs` | タブレットアプリ（AppId `"shop"`）、家具職人への「お店の改装を頼む」メニュー |

## 実装した内容（仕様との対応）

- **セルフ販売**（商店 §1【決定】・バランス §7.1）：棚に並べて値段を付ければ、営業時間中は住民が自分で買う。店番・レジなし。プレイヤーが出かけていてもホストの星の時間中に売れる。
- **店舗**（商店 §8・村マップ §4.2）：`WorldLayout.GetFacility(PlayerShop)` の位置・向き（入口方向）に、低い側壁と柱だけの開放的な店を建てる（中で品定めする住民が外から見える）。しましまのひさし、屋根の星、カウンター、帳簿、マネピの止まり木、倉庫箱、営業時間の看板（営業中/準備中＋時間）、要望ボード。施設が無い場合は出現位置の近くに建てる保険あり。
- **棚**：初期6枠。枠ごとに陳列台の種類（ふつう・冷蔵棚・水槽・家具ステージ・特売コーナー）。商品の簡易モデル（アイテムの Shape/Color、最大3個＋山積み、水槽の魚は泳ぐ、光る物は発光）、値札（主人公のUI言語の数字・品質の★・委託品は◆・特売は SALE）。枠番号が小さいほど入口に近く目に留まりやすい（レイアウト効果）。入替コマンドで配置を変えられる。
- **棚の操作（WorldCommand）**：持ち物/倉庫から並べる（個数・値段。基準価格×品質の「目安」と比率を表示）、値段を変える、下げる（持ち物/倉庫へ）、自動補充のON/OFF、枠の入替、倉庫の出し入れ、営業時間の設定、改装の依頼、委託売上の受け取り。
- **委託販売**（マルチ §12.3）：ゲスト（お手伝い以上）が自分の物を並べると `ownerId` 付きの委託品になる。売上は持ち主ごとの未精算（`SettlementData`）に積まれ、持ち主が店で受け取る（`ShopCollectSettlementCommand` → `ctx.PayMoney`）。店主が委託品を下げた場合は持ち主への返品として預かる。ホスト自身の売上は即座にホストの `Wallet` へ入金。
  - 委託品が売れた来店は、その来店の事実（`ShopReportView.BuildMaterial`、ID `shop.visit.<来店ID>`、タグ `consigned`）とマネピの記録（原本の発話）を `ITabletService.AddMaterialFor(持ち主, …)` で持ち主（ゲスト）の資料ボックスへ届ける（`DeliverConsignedSaleRecord`）。住民辞典の購入の事実は Tablet が `ShopItemSoldEvent` から持ち主へ転送する。
- **営業**：開店・閉店時間（初期 9〜18時）。店の灯りの改装で設定範囲が 8〜20 → 6〜22 → 5〜24 時に広がる。
- **`ResidentVisit(resident, intent)`**（バランス §7.2〜§7.6・商店 §3）：
  - 判断要素：所持金・予算（指定予算／所持金・節約度から・1日の使用上限＝所持金×0.55）、好み `EvaluateItem`（贈り物なら相手の好みも混ぜる）、目的（`need`・`preferredTag`・`reasonTag`・`forResidentId`）、値段÷「その住民が感じる価値」（基準価格×品質×鮮度×好み×珍しさ）と、性格で個体差のある「高い」境界（なめらかな受け入れ曲線。絶対に買わない境界は作らない）、在庫・品揃え（目に留まる確率：入口からの距離・看板・内装・特売・水槽・家具ステージ）、時間帯・天気（`IWeatherService`：雨・暑さ寒さ・光雨/星の光帯/花潮/結晶霞）、職業の関心、新商品、鮮度、消耗品と耐久品（§7.3：持っている家具は買わない／同じ日に同じ食べ物は控える）。
  - 住民の買い物傾向（商店 §3.2 の表）：節約家・お金持ち・食いしん坊・収集家・流行好き・こだわり派・気まぐれを、シードと住民IDから決定的に作る（職業・系統で補正）。
  - 無限購買の防止：所持金、1日の上限、満腹（同日同品）、耐久品の重複回避。見えない販売数上限は使わない（バランス §7.5）。
  - 取引：`IResidentDirectory.TrySpend` → `GiveItem` → 棚から減らす → 持ち主へ売上 → `ShopItemSoldEvent` と `ResidentPurchaseEvent`（`Where="player_shop"`）を Publish → `AddMemory(kind="bought_from_shop")` → 売上記録。
  - まとめ買い：隣の枠の関連商品をときどき一緒に買う。
  - 要望を叶えた時：その住民の要望が満たされ、店主への親しさ +2（`ChangeFriendship`）。
  - 来店後に棚の前まで歩いて品定めする（`RequestGoTo`、`ShopConfig.BrowseWalkEnabled` で切替）。
  - `ShopVisitResult` を全項目埋める（`price` は支払った合計額）。
- **反応 → マネピ**（タブレット §4〜§5、宇宙語 §5.2）：買った／安くて驚いた／珍しい物に興奮／（前に食べたことがあるので）おいしい／要望が叶った／高くて戻した／予算が足りない／鮮度が悪い／欲しい物が無かった／いい物が無かった、を意味データで作る（例：`PRICE_EXPENSIVE` ＋商品＋`VERY`/`A_LITTLE`、`WANT` ＋欲しい物の概念・色・数量、`DELICIOUS`、`PRICE_CHEAP`、`RARE`、`HAVE MONEY` の否定、`FRESH` の否定、目的の文「料理する」「贈る」など）。表情・身振り（首を振る・指さす・数を示す・抱える・拍手・跳ねる…）を必ず添える。プレイヤーが近くにいれば `IResidentDirectory.Speak`（吹き出し）、そして**毎回** `ISpeechService.CreateRecord(…, ManepiPlayback, "resident", マネピの位置)` → `IManepiService.LeaveMessage("shop", record)`。記録IDは来店履歴と要望に紐付く。意味はその住民が実際に感じたこと・知っていることと矛盾させない。
- **売上レポート・来店履歴**（商店 §9）：日ごとの売上・販売個数・来店者数・買った人数・戻された数・マネピの声の数・商品別の表・売り切れ・持ち主別の売上。来店履歴は時刻・**顔アイコン**（住民の色＋系統の特徴。名前は出さない）・見ていった物・買った物と金額・**行動の事実**（「値札を見て棚に戻した」「財布をのぞいて戻した」「においをかいで戻した」「棚を見回して何も持たずに帰った」）。住民辞典アプリを開くボタンあり。
  - 「高くて買わなかった」は、レポートでは観察できる行動（値札を見て戻した）として表示し、理由（＝発言の意味）を断定・翻訳しない（タブレット §4.5「買わなかった理由をゲームが自動で確定させない」）。
- **価格研究の流れ**（タブレット §10.1）：来店履歴・値段の比較の行から「▶ マネピの再現」（`ISpeechService.Replay`、店のマネピの前にいる時だけ）→「音声カードに保存」（`ITabletService.SaveVoiceCard`。ラベルに日時・商品・当時の値段・購入したか＝商店の客観的記録）→「事実を資料へ」（`AddMaterial(kind=SalesRecord)`、行動の事実・関連アイテム・`price:<item>:<値段>` タグ・記録ID・住民ID）。**値段の比較**タブで、同じ商品の反応を値段ごとに並べ、直近7日の販売推移と一緒に聞き比べられる。
- **要望ボード**（商店 §7 の最新版）：欲しい物が無かった住民の要望をマネピが覚え、店の外のボードに **形と色の絵**（その住民が思い浮かべた品の形・色、または総称の形＋色）・顔・数の点で貼る。画面でも絵・顔・数・マネピの再現だけを見せ、文字の訳は出さない。売り切れた好物の「また欲しい」もある。5日で消える。要望に合う品を並べると、その住民が立ち寄りやすくなる。
- **店舗拡張**（商店 §8）：棚の追加・冷蔵棚・水槽・家具ステージ・特売コーナー・倉庫の拡張・看板（立ち寄り＋注目度）・店の灯り（営業時間の延長）・補充の仕掛け（倉庫から同じ物を同じ値段で補充。何をいくらで売るかは主人公が決める：料理 §13.1）・内装（満足度・注目度）。**家具職人（`ResidentJob.Carpenter`）に話しかけて依頼**（`RegisterInteractionProvider`）し、翌朝に完成。費用の 60% は家具職人の収入（`GiveMoney`、経済循環 §7.6）。家具職人がいない時は帳簿から直接注文できる保険あり。見た目（鉢植え・ラグ・旗・灯り・看板・風船）も変わる。
- **看板による立ち寄り**：営業中、店の近くを歩いている住民が看板の段階に応じて立ち寄る（`RequestGoTo` で入口へ→着いたら来店処理）。住民モジュールからの `ResidentVisit` 呼び出しが2日以上無い場合は、保険として村全体から立ち寄りを起こす（住民モジュール未接続でも商店で稼げるように）。
- **1日の締め**（`OnDayEnded`）：日別集計を確定し、`UIManager.Toast` で「今日の売上：◯G（◯個・来店◯人）／マネピの伝言◯件」。
  - 集計の正本はホスト。ゲストは自分の端末の集計（同期待ちで締め直前の売上が欠けることがある）では通知せず、ホストが締めた値を委託品が売れた持ち主ごとに一時メッセージ `"shop.day_summary"`（`ShopDaySummaryMessage`：その人の委託品の売上・個数、店全体の来店者数・伝言数）で送り、受け取ったゲストが通知する（売れなかった日は通知なし）。
- **日付更新**（`OnDayStarted`）：改装の完成、古い要望・来店履歴（14日）・日別集計（60日）の整理、`IManepiService.ClearMessagesOlderThan("shop", day-7)`。
- **タブレットアプリ**（AppId `"shop"`）：今日・日別・来店履歴・値段の比較・棚・要望・未精算。どこでも事実を見られ、マネピの再現と音声カード保存は店でのみ（マネピが覚えている声は店にあるため：タブレット §4.4）。未確認の伝言数（♪）を表示。
- **住民の生活への影響**（商店 §10）：住民側が実施するため、`GiveItem`・`ResidentPurchaseEvent`・`ShopItemSoldEvent`・`AddMemory` を正しく出す。購入の事実は住民辞典へ客観的観察として記録（`RecordResidentObservation`）。

## 未実装・簡略化した内容（理由）

- **宇宙生物の販売**（生き物 §11.3）：生物はアイテムではなく個体（`CreatureView`）で、住民へ渡す契約が無いため未実装。
- **鮮度**：仕様で未決定のため、生鮮品を普通の棚に3日以上置くと「売れにくくなる・においをかいで戻される」だけにした（腐って消えない）。冷蔵棚・水槽では落ちない。
- **仕入れ販売・直接買い取り店**（商店 §2【提案】）：仕入れ先・買い取り店の仕様が無いため未実装（住民の店は Core の `VendorWindow` で他モジュールが作る想定）。
- **村の外からの客・星間取引**（バランス §7.5【採用未確定】）：未実装。
- **店の内装の自由配置**：家具モジュールとの連携契約が無いため、改装の段階に応じた固定の見た目にした。
- **住民の買い物傾向**：住民モジュールの内部の性格は契約で取れないため、商店専用にシードから決定的に作っている（`ShopTraits`）。
- **住民辞典の特定ページを開く**：契約が無いため、住民辞典アプリ（AppId を `"residents"` と仮定）を開くだけ。
- **リモートのゲストの委託品の品質**：`CommandContext.TakeItem` が品質を指定できないため、品質はゲスト側の事前確認を信頼している（サイズ・モジュール固有データも引き継げない）。
- 来店に関する発話は「その場で話した」記録とマネピの記録の2つができる（`Speak` が会話ログ用、`CreateRecord` がマネピ用）。
- 委託品の売上記録・日次の売上通知は一時メッセージなので、持ち主が退出中だった分は届かない（売上は未精算として星に残り、店の帳簿・タブレットの商店アプリでは後から見られる）。

## Unity 上で確認が必要な点

- 店の大きさ・向き（施設の `size`・`entrance` から決める。幅8〜12m・奥行7〜10m）が村の他の建物・道路と重ならないか。斜面での土台の埋まり具合。
- 棚の並び（壁沿い・奥・中央の島・店頭の台）が通路をふさがないか。プレイヤーが [E] で狙いやすいか（棚・カウンター・マネピが近い）。
- 値札（`WorldLabels`）が重なって読みにくくないか。要望ボードの絵の大きさ。
- 住民の品定めの歩き（`RequestGoTo`）と住民モジュールの行動が衝突しないか（不自然なら `ShopConfig.BrowseWalkEnabled=false`）。
- タブレットアプリの `Draw(area)` で `GUILayout.BeginArea(area)` を使っている（area がタブレット画面内の相対座標である前提）。
- マルチ：ゲストの委託品が売れた時に、ゲストのタブレットに売上記録（マネピの声つき）が届くか。日付が変わった時に、ゲストの「今日の売上」がホストの確定値（締め直前の売上を含む）で1回だけ出るか。
- **数値バランス**：1日の売上（バランス §6.1 の 500〜3,500G/日の目安）、「高い」と感じる比率（`AcceptCurveSteepness`・`ShopTraits.Tolerance`）、予算・1日の上限、立ち寄り確率、改装費。
- パフォーマンス：来店ごとに全棚を評価（最大約26枠）。表示更新はシグネチャ比較で変化した部分だけ作り直す。

## 他モジュールとの連携点

- **実装する契約**：`IShopService`（`IsOpen`・`EntrancePosition`＝入口の地面上の点・`OfferCount`＝商品が並んでいる枠の数・`ResidentVisit`）。住民モジュールは来店時に `ResidentVisit` を呼ぶだけでよい（ホストのみ処理。ゲストでは空の結果）。
- **呼ぶ契約**：
  - `IResidentDirectory`：`TrySpend` / `GiveItem` / `AddMemory` / `ChangeFriendship` / `Speak` / `RequestGoTo`（"shop_visit" "shop_browse"）/ `ActiveResidents` / `Get` / `FindByJob(Carpenter)` / `GiveMoney` / `RegisterInteractionProvider`
  - `ISpeechService`：`CreateRecord` / `Replay` / `GetRecord`
  - `IManepiService`：`SpawnManepi("shop", …)` / `LeaveMessage("shop", …)` / `GetMessages("shop")` / `ClearMessagesOlderThan("shop", …)`（無い場合は商店内の代替マネピで伝言を聞ける）
  - `ITabletService`：`RegisterApp`（"shop"）/ `SaveVoiceCard` / `AddMaterial(SalesRecord)` / `AddMaterialFor(委託の持ち主, SalesRecord, マネピの記録)` / `RecordResidentObservation` / `Open("residents")`
  - `IWeatherService.Current`、`INetworkService.GetPlayerName`
- **発行するイベント**：`ShopItemSoldEvent`（1個につき1回。`Price`＝1個の値段、`ShopOwnerId`＝商品の持ち主）、`ResidentPurchaseEvent`（1個につき1回、`Where="player_shop"`）。インベントリ操作に伴う `ItemAddedEvent`/`ItemRemovedEvent`・`MoneyChangedEvent`（理由 `"shop_sale:<itemId>"` など）。
- **購読するイベント**：`PlayerLeftEvent`（表示更新のみ）。
- **コマンド**：`ShopStockCommand` `ShopUnstockCommand` `ShopSetPriceCommand` `ShopSetAutoRestockCommand` `ShopSwapSlotsCommand` `ShopStorageDepositCommand` `ShopStorageWithdrawCommand` `ShopSetHoursCommand` `ShopOrderUpgradeCommand` `ShopCollectSettlementCommand`（店主の操作は星の持ち主または共同生活の権限、委託はお手伝い以上）。
- **一時メッセージ**：`"shop.day_summary"`（ホスト → 委託の持ち主。`ShopDaySummaryMessage`）。
- **登録するアイテムID**：なし（並べられるのは `ItemTags.NotSellable` を持たず、KeyItem/Letter でない物。大きな家具は家具ステージ、魚は水槽にも置ける）。
- **セーブ**：`SaveScope.Planet`＝棚・倉庫・営業時間・改装・来店履歴・日別集計・未精算・要望・住民ごとの買い物の記憶。`PlayerPlanetKnowledge`＝伝言をどこまで確認したか。`PlayerProfile` は無し。`RestoreState(Planet)` は表示まで冪等。

## 調整用の仮数値の場所

すべて `ShopConfig.cs`（棚・倉庫・営業時間・記録の保存期間・購買判断・鮮度・立ち寄り・演出距離・家具職人の取り分・改装の段階と費用）。
購買判断の式の重み（欲しさの各項目）は `ShopSystem.Purchase.cs` の `RunVisit`、性格の補正と「高い」の境界は `ShopRules.cs` の `ShopTraits`、職業の関心・天気/時間帯の補正も `ShopRules.cs`。
