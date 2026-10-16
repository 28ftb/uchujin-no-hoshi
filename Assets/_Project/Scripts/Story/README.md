# Story モジュール（タイトル・キャラクターメイク・プロローグ・章・エンディング・クリア後）

`Uchujin.Story.StorySystem : GameSystemBase, IStoryService`（SaveKey `"story"`, DailyUpdateOrder 495）

参照した仕様：メインストーリー v0.2（全体）、基本決定 v0.2（§3 キャラクターメイク、§5 クリア後）、企画書 v0.1（§2・§3・§13）、
ロケット修理 v0.2（§2・§18・§19）、キャラクターデザイン v0.1（01 まんまるマスコット系）、住民 §11、マルチ §19、バランス §2・§4.4。

## ファイル構成

| ファイル | 内容 |
|---|---|
| `StorySystem.cs` | 本体。IStoryService、章の追跡、案内役と口癖、第1章の世話焼き、クリア後の選択、セーブ |
| `StoryData.cs` | セーブ用データ（星：`StoryPlanetState`、個人：`StoryProfileState`）、`EndingRecord`、`CarryOverPlan` |
| `StoryTuning.cs` | 調整用の【仮数値】すべて |
| `StoryUtterances.cs` | 演出で住民が話す意味データ（`Utterance`/`MeaningFrame`）。日本語の文は作らない |
| `Title/TitleWindow.cs` | タイトル画面（新しい星／続きから／友達の星へ／設定／クレジット）とロゴの重ね描き |
| `Title/TitleBackdrop.cs` | タイトル中だけの3D背景（星空・輪のある星・ロケット・流れ星・まんまるな宇宙人） |
| `Title/CreditsWindow.cs` | クレジット（タイトル用・エンディング用） |
| `CharacterMake/AppearanceCatalog.cs` | キャラメイクの選択肢・色見本・おまかせ |
| `CharacterMake/AvatarBuilder.cs` | `PlayerAvatarFactory.BuildVisual` の上書き（全項目を反映）＋歩き・まばたきの `AvatarAnimator` |
| `CharacterMake/AvatarPreviewStage.cs` | 3Dプレビュー（遠方の舞台＋専用カメラ＋RenderTexture） |
| `CharacterMake/CharacterMakeWindow.cs` | キャラクターメイク画面（新規・着替え・次の星） |
| `CharacterMake/AppearanceApp.cs` | タブレットアプリ「着替え」（AppId `appearance`） |
| `Cutscene/CutsceneDirector.cs` | 演出の進行役（カメラ・シネマ帯・宇宙語字幕・操作案内・スキップ・フラッシュ） |
| `Cutscene/CutsceneFx.cs` | 光跡・衝撃・煙・火花・炎・土煙（プリミティブ） |
| `Cutscene/StandIns.cs` | 代役（ロケット・新しい住民）、宇宙船の窓のセット、ロケット本体を一時的に隠す `RendererHider` |
| `Cutscene/StoryActors.cs` | 演出中の住民・主人公の動かし方 |
| `Cutscene/StoryPlaces.cs` | 演出で使う場所（ロケット・広場・商店・自宅・空き区画） |
| `Prologue/PrologueSequence.cs` | プロローグ（初回）と、次の星の短い到着演出 |
| `Ending/EndingSequence.cs` | エンディング「最後の引っ越し」 |
| `Ending/DictionaryPage.cs` | エンディングで映す「プレイヤーが書いた辞書」 |
| `Ending/PostEndingWindow.cs` | クリア後の選択（A/B）と次の星への引き継ぎ設定 |
| `Ending/MemoriesApp.cs` | タブレットアプリ「思い出」（AppId `memories`）と、エンディング後半の再生 |

## 実装した内容（仕様との対応）

### タイトル（ARCHITECTURE §3、基本決定 §1）
- `RegisterContent` で `Game.TitleScreenFactory = () => new TitleWindow()`。
- 新しい星で始める：シード入力／ランダム、星の呼び名（任意）、プロローグを飛ばす → キャラクターメイク → `Game.Instance.StartNewPlanet`。
- 続きから：`Saves.ListSlots()`、各スロットに「遊ぶ」「削除（確認付き）」。
- 友達の星へ行く：`INetworkService.OpenMultiplayerMenu()`（サービスが無ければボタン非表示）。
- 設定（言語 `Loc.SetLanguage`＋PlayerPrefs 保存、音量、時間の速さ、操作説明・時計）、クレジット、終了。
- 背景：`TitleBackdrop`（専用カメラ、MainCamera タグなし）。`GameStateChangedEvent` で Title／CharacterCreation 以外になったら破棄。

### キャラクターメイク（基本決定 §3【確定】、項目数は【未決定】→充実）
- 項目：体型6、身長・頭の大きさ（スライダー）、**宇宙人のしるし8**（ひれ耳・ながい耳・光る触角・ちいさなつの・星のしっぽ・光るもよう・まる耳）、
  肌（人に近い色8＋別の宇宙の色10＋HSV微調整）、顔10、目の形10（ひとつ目・みっつ目など異星らしい案を含む）、目の色、髪型16、髪色、
  上着12・下衣6（形と色）、靴5、こもの24（帽子類10・ゴーグル・リボン・メガネ・ヘッドホン・花かんむり・宇宙ヘルメット・ネコ耳・王冠・マフラー・星のヘアピン・アンテナ・つの）、名前（任意）。おまかせ・最初に戻す。
- **見た目は NOKA の人物モデル（ちび頭身・パステル・ふっくら）を元にした部品キット**（2026-10-16）。
  `Tools/Avatar/build_avatar_kit.py`（Blender 5.2）が NOKA の chibi()/face_parts()/townsperson() の形・寸法を使って部品ごとの FBX を
  `Assets/_Project/Resources/Avatar/` に書き出す（体型5・目10・顔10・髪15・上着12・下衣6・靴5・こもの22・しるし7＝91部品）。
  `AvatarBuilder` が体型の骨組み（Rig_Body / Rig_Head / Rig_Hip_P,N / Rig_Shoulder_P,N）に各部品を付け替え、材質名 `AvTint_役目` をキャラメイクの色で塗る。
  胴まわりの服は体型ファイルの `Fit_x_y_z` の下に入れて体型に合わせる。帽子の時は高く盛った髪（つんつん・モヒカン・おだんご）を隠し、もこもこ・くるくるの上に持ち上げる。
  取り込み設定は `Editor/AvatarKitImporter.cs`（軸の変換を焼き込む）。全項目の見本を並べた撮影はメニュー「宇宙人の星 → キャラメイクの部品を一覧撮影」（`Editor/AvatarKitGallery.cs` → `Logs/AvatarGallery/`）。
  部品を足す時は、スクリプトに部品を足して書き出し、`AppearanceCatalog` の一覧と `*Files` に同じ順で足す。
- 名前の説明に「住民はあなたの名前を知らない。宇宙語で呼ばれることはない」を明記。
- 古いセーブの番号はおおむね同じ項目を指すよう並びを保った（髪型の「ぱっつん」「アホ毛」は無くなり、9・10番は「ふたつおだんご」「くるくる」になる）。
- 3Dプレビュー：遠方（-9000, 9000, -9000）の舞台を専用カメラで RenderTexture に描画。ドラッグ回転・自動回転・顔のアップ。
- `PlayerAvatarFactory.BuildVisual = AvatarBuilder.Build`：ルート "Visual"、直下に "ToolHolder"（コアとの約束どおり）。マルチの他プレイヤーにも同じ関数が使われる前提で、ローカル専用の情報に依存しない。
  `AvatarAnimator`：移動量から手足を振る、まばたき、道具を構えている時だけ柄を表示（ローカルの PlayerController がある時のみ）。
- 作成後の変更（§3【未決定】の仮）：タブレットアプリ「着替え」（`appearance`）→ キャラメイク画面で編集 → `PlayerSys.RebuildLocalVisual()`。お気に入りの服装を最大6件保存（PlayerProfile）。

### プロローグ（メインストーリー：プロローグ、ロケット §2）
- `Game.OnNewPlanetStarted` に登録。`SkipPrologue` なら飛ばして第1章へ。
- 流れ：空から落ちる光跡（メテオ）→ 衝撃（フラッシュ・揺れ・煙・火花・焦げ跡、煙は約2分立ちのぼる）→ 主人公が脱出（驚く身振り）
  → タブレットの診断（主人公の装置なので主人公の言語。「故障箇所が N か所見つかりました。航路データは無事です」）
  → 案内役（`ActiveResidents` の `Lineage==Round` を Id 順で最初の1人）が背後から現れる → **操作できる区間**：近づくと驚いて逃げる（14秒で自動で歩く）
  → 修理屋（`FindByJob(Mechanic)`）がロケットを調べ、料理好き（`FindByJob(Cook)`）が料理を差し出す（料理アイテムを1つ渡す。Cooking の安い料理を決定的に選び、無ければ `story.welcome_snack`）
  → 皆が宇宙語で話す（翻訳なし。意味データ）→ 案内役が口癖つきで挨拶 → 「一緒に村へ」→ **ついていく区間**（`RequestGoTo` で先導、止まっていると自動で歩く、75秒で暗転して村へ）→ 自由行動。
- スキップ：右上のボタン、または演出中に Esc／Enter／Space を2回。スキップしても料理は受け取り、村の入口から始まる。
- 住民モジュールが無い・住民がいない場合：診断までで終了（落ちない）。途中でセーブ＆ロードした場合は再生せず完了扱い。
- 次の星（`PlanetIndex>1` または `PreviousWorldId` あり）は短い到着版：着陸の光 → 診断 → 案内役の挨拶 → 村へ。

### 案内役の口癖（IStoryService.GuideCatchphraseConcept）
- 星のシードから決定的に選ぶ（用途キー `"story.catchphrase"`、候補 `StoryTuning.CatchphraseCandidates`＝FRIEND/WELCOME/TOGETHER/FAMILY/NEIGHBOR）。意味はゲームが説明しない。
- どのシステムの `PreparePlanet`/`OnNewPlanet` から問い合わせても、現在の星の値を返す（シードから都度計算）。
- 使う場面：プロローグの挨拶、第1章の世話焼き、エンディングの最後、エピローグで新しい住民を迎える場面（主人公も迎えられた一人だった）。
- `GuideResidentId` は住民の生成が終わってから確定・保存する（住民が後で引っ越しても、思い出の相手は変わらない）。

### 章（メインストーリーの未決定事項「明示するか」→ 明示しない）
- 0 プロローグ → 1 プロローグ後 → 2 最初の転入（`ResidentMovedInEvent`）→ 3 第2章から3日以上・星に来て5日以上・誰かと親しさ30以上
  → 4 最初の転出（`ResidentMovedOutEvent`）→ 5 最初の修理（`RocketDeviceRepairedEvent`）→ 6 帰還を選択。
- 節目は起きた順に記録し、章は1回の判定で1段階ずつ進める（イベントの順が前後しても体験の流れを飛ばさない）。判定は節目のイベント時と毎朝。
- `Game.Instance.Flags.Set("story.chapter", n)`、`"story.chapterN.day"`、`"story.launch_ready"`、`"story.ending_seen"` を立てる。
- 章が変わった時の小さな演出：案内役が様子を見に来ていつもの言葉をかけ、食べ物（`story.guide_snack`）をくれる。第1〜3章の間は3日おきにも来る（第1章「世話を焼かれる」）。

### エンディング「最後の引っ越し」（`BeginEnding()`、最終章・ロケット §19）
- `EndingStartedEvent` を発行。`GameState.Ending`、入力ロック、HUD 非表示。見送り〜村の場面は `TimeSystem.AddClockHold("story.ending")` で時計だけを止め（`AddGlobalPause` だと住民の移動まで止まる）、参加する住民を `HoldForCutscene(true)` で生活AIから外す（演出の終わり・中断・次の星へ進む前のセーブより先に必ず解除）。宇宙船の窓・辞書・クレジットの間は `AddGlobalPause("story.ending")`。
- 住民がロケットの周りに集まる：親しさ60以上＝近くでプレゼント（80以上は泣く）、20以上・常連客・修理屋／料理好き／案内役＝近くで見送り、それ以外＝遠くから見ているだけ。
- 修理屋の最後の点検（「いつか私もロケットを作りたい」＝第3章の夢）→ 料理好きの旅のお弁当（`story.travel_bento`）→ 仲の良い住民のプレゼント（`SharedItemIds.GiftBox`、data に送り主）
  → 引っ越した住民の手紙（`FormerResidents`、`SharedItemIds.Letter`、`ISpeechService.CreateRecord(... SpeechChannel.Letter ...)` の記録IDを data に入れ、タブレットの資料と住民辞典にも事実だけを記録）
  → 住民たちが次々に言葉をかける（熱中した活動の名人／常連客／子ども／旅に憧れる住民／その他で意味を変える）→ 遠くの住民のカット → 最後に案内役が口癖をもう一度。
- 字幕は宇宙語の表面形のみ。`ITabletService.ShowGlossOverlay` が on の時だけ、各語に**プレイヤー自身の推測訳**（`GetPlayerGloss`）を［ ］で添える（無い語は「…」）。正解訳は出さない。
- ハッチが閉まり離陸：ロケット本体（Rocket 担当）の見た目を一時的に隠し、代役 `RocketProxy` を `IRocketService.RocketPosition` に置いて上昇。地上から見上げ → 上から小さくなる村を見下ろす。
- 主人公がいない村：商店に客（常連優先）→ 広場で子どもが遊び、住民が主人公のことを話す（「STRANGE_ONE は故郷へ行った」「さびしい」「いつも覚えている」）
  → 主人公の家の前で住民が立ち止まる → 「数日後」新しい住民（代役のまんまる宇宙人）が荷物を抱えて来て、案内役がいつもの口癖で迎える。
- 宇宙船の窓に故郷の星（近づいていく）→ **プレイヤーが書いた辞書**のページ（表面形＋プレイヤーの推測訳、最後に「あの言葉」＝口癖の表面形とプレイヤーの訳 or「……」）→ 着陸 → クレジット（住民の宇宙語の名前、暮らしの記録）。
- 暮らしの反映：常連客の数、手紙、熱中した活動（釣り・農業・採掘・生き物・料理・販売の回数を星ごとに記録）、親しさ。交流が少なくても同じように帰還できる。言語理解の割合で分岐しない。
- 贈り物・手紙は**初めての帰還の時だけ**受け取る（A を選んで何度も帰還すると増えるのを防ぐ）。見送りをスキップしても受け取る物は受け取る。

### クリア後の選択（基本決定 §5【確定】）
- A「ロケットの前に戻って、この星で暮らしを続ける」：`EndingFinishedEvent{ReturnBeforeLaunch}` → 代役を片付け、ロケット本体の表示を戻し、主人公をロケット前へ → プレイ継続（強制終了しない）→ セーブ。
  再鑑賞：タブレット「思い出」でこの星の節目・帰還の日の記録を見返し、窓と辞書とクレジットをもう一度再生できる（暮らしは変化しない）。
- B「次の星へ進む」：引き継ぎ設定（**仮・選択式**：見た目そのまま／作り直す、持ち物、お金、道具の強化、到着の場面を見るか、シード、星の呼び名）
  → `EndingFinishedEvent{NextPlanet}` → 現在の星をセーブ → `StartNewPlanet{ KeepProfile=true, PlanetIndex=+1, PreviousWorldId=今の星, SkipPrologue=!到着の場面 }`。
  持ち物を置いていく場合も、手紙・贈り物・大事な物（`StoryTuning.KeepsakeCategories`）は持っていく。持ち物・お金・道具の取捨は Story の `OnNewPlanet` でローカルプレイヤーに適用（コアの KeepProfile は全部か無しかのため）。
- **元の星のセーブは残り、タイトルの「続きから」で再訪できる**（メインストーリー未決定事項「再訪できるか」の暫定扱い）。辞書は星ごとのセーブ（PlayerPlanetKnowledge）に分かれ、混同しない。
- 旅の記録（星ごとの滞在日数・出会った住民数・旅立ったか）を PlayerProfile に持ち歩き、「思い出」に表示する。

## 未実装・簡略化した内容（理由）
- 住民の演出中の移動は `RequestGoTo` ＋ 時間切れ時に `IResidentDirectory.Warp` で位置を合わせる方式（`StoryActors.Warp`）。
- 住民の泣く・手を振るなどは `Utterance` の表情・身振り（`Expression.Crying`／`Gesture.Wave` 等）で伝え、実際のアニメーションは Residents／Language 側の表示に任せる。
- ロケット本体は Rocket 担当の持ち物なので、離陸は代役（`RocketProxy`）。本体の見た目は「ロケット位置の半径5.5m以内・28m未満の Renderer を一時的に無効化」で隠す（簡略化）。
- エピローグの「数日後に引っ越してくる新しい住民」は演出用の代役で、実際の住民の生成は行わない（実際の引っ越しは Residents のシステムが行う）。
- 辞書の語一覧は `ITabletService.ExportDictionary()` の JSON から `"surface"`/`"word"`/`"spelling"` キーを正規表現で拾い、さらに最近聞いた発話の語を `GetPlayerGloss` で照会して作る（書き出し形式が未確定のため）。
- マルチ：プロローグとエンディングはホストのローカル演出。ゲストが `BeginEnding` を呼んでも「持ち主が決める」と通知するだけ（マルチ §19.1 の「友達が見送る」演出は未実装。ゲストの画面への同期は Network 担当と要相談）。
- 次の星へ進む時、他モジュールの PlayerProfile 状態（同行生物・技能など）はメモリ上の状態がそのまま引き継がれる想定（KeepProfile=true）。個別の取捨は未対応。
- クレジットのスタッフ名は仮。BGM・効果音は無し。

## Unity 上で確認が必要な点
- **見た目**：アバターの各パーツの位置（特に髪・帽子と目が重ならないか、大きい頭身でのバランス、ヘルメット・メガネの半透明表示、URP での発光パーツ）。
- 3Dプレビューの RenderTexture 表示（IMGUI の `GUI.DrawTexture`）、プレビュー用ポイントライトの明るさ。
- タイトル背景の見え方（半透明の星雲・輪、カメラの depth=20 で星のカメラより前に出ること）。
- 演出のカメラ位置・速さ・長さ（地形によって建物や木に隠れる可能性）。プロローグ・エンディングの全体の尺（それぞれ約2〜4分＋操作区間）。
- 住民の `RequestGoTo` と直接移動の相性（Residents の移動方式次第）。演出中に住民の自律行動が割り込まないか。
- ロケット本体を隠す `RendererHider` が、近くの木や看板まで隠しすぎないか。
- 宇宙船の窓の場面で `RenderSettings.fog` とカメラの背景色を一時的に変えている（Weather が毎フレーム霧を設定している場合は上書きされる）。
- タブレットのアプリ `Draw(Rect area)` の中で `GUILayout.BeginArea(area)` を使っている。Tablet 側が既に area で BeginArea している場合は位置がずれるので要調整。
- 字幕の文字（宇宙語の表面形）の読みやすさ、推測訳の併記の見た目。
- 数値バランス：章の条件（日数・親しさ）、案内役の世話焼きの頻度、見送りの親しさのしきい値、話す住民の人数。

## 他モジュールとの連携点
- **呼ぶ契約**：`IResidentDirectory`（ActiveResidents／FormerResidents／Get／FindByJob／RequestGoTo／Speak／AddMemory／GetFriendship／Knows）、
  `ISpeechService`（CreateRecord／RenderWord／ResidentName／PlanetName／RecentRecords／GetRecord）、
  `ITabletService`（RegisterApp／Open／Close／IsOpen／AddMaterial／RecordResidentObservation／GetPlayerGloss／ShowGlossOverlay／ExportDictionary）、
  `IRocketService`（RocketPosition／TotalDeviceCount／RepairedDeviceCount）、`IShopService.EntrancePosition`、`IHousingService.PlayerHomeEntrance`、`INetworkService.OpenMultiplayerMenu`。
- **公開する契約**：`IStoryService`（Chapter／IsPrologueDone／GuideResidentId／GuideCatchphraseConcept／BeginEnding／IsEndingInProgress）。
  - Residents：挨拶時に `GuideCatchphraseConcept` を含める（案内役）。`GuideResidentId` は「Round 系統の Id 順で最初の1人」で一致。
  - Rocket：帰還を選んだら `BeginEnding()` を呼ぶ。`EndingFinishedEvent{ReturnBeforeLaunch}` で出発直前の状態に戻す。
- **発行するイベント**：`EndingStartedEvent`、`EndingFinishedEvent{Choice}`。
- **購読するイベント**：`ResidentMovedInEvent`、`ResidentMovedOutEvent`、`RocketDeviceRepairedEvent`、`RocketLaunchReadyEvent`、`ShopItemSoldEvent`、
  `FishCaughtEvent`、`CropHarvestedEvent`、`OreMinedEvent`、`CreatureCaughtEvent`、`ItemCraftedEvent`、`GiftGivenEvent`、`GameStateChangedEvent`（タイトル背景）。
- **登録するアイテム**：`story.welcome_snack`（はじめての差し入れ）、`story.guide_snack`（まんまる焼き）、`story.travel_bento`（旅のお弁当・非売品）。
  使う共有アイテム：`SharedItemIds.Letter`（手紙、data＝発話記録ID）、`SharedItemIds.GiftBox`（data＝`from:<住民ID>`）。
- **タブレットのアプリ**：`appearance`（着替え）、`memories`（思い出）。`OnPlayStarted` ごとに `RegisterApp` する（同じ AppId の重複登録は Tablet 側で無視される前提）。
- **差し替え口**：`Game.TitleScreenFactory`、`PlayerAvatarFactory.BuildVisual`、`Game.OnNewPlanetStarted`。
- **フラグ**：`story.chapter`、`story.chapterN.day`、`story.launch_ready`、`story.ending_seen`。
- **WorldCommand**：なし（物語の状態はホストのイベント処理で更新し `MarkDirty()`）。
- **セーブ**：Planet＝`StoryPlanetState`（章・節目・案内役・口癖・常連／活動の回数・帰還の記録）、PlayerProfile＝`StoryProfileState`（旅の記録・お気に入りの服装）。

## 調整用の仮数値の場所
- `StoryTuning.cs`：章の条件、口癖の候補、世話焼きの間隔・距離・時間帯、見送りの親しさのしきい値・人数・輪の半径、手紙の条件・上限、辞書に映す語数、
  プロローグの制限時間・距離、引き継ぎの既定値・思い出の品の分類、アイテムID、アプリID。
- `AvatarBuilder.cs` 先頭の `shapes`（体型ごとの寸法）、`AppearanceCatalog.cs`（選択肢・色見本）。

## コアへの追加要望
1. **住民を演出用に確実に動かす API**（`IResidentDirectory`）
   ```csharp
   /// 演出の間、自律行動を止めて指定位置・向きに置く（hold=false で解除）。
   void HoldForCutscene(string residentId, bool hold);
   void Warp(string residentId, Vector3 position, float yawDegrees);
   ```
2. **ロケット本体の見た目の制御**（`IRocketService`）：`Transform RocketTransform { get; }` か `void SetVisualHidden(bool hidden)`。
   現在は位置の近くの Renderer を一時的に無効化して代役と入れ替えている。
3. **辞書の推測訳の一覧**（`ITabletService`）：`IEnumerable<KeyValuePair<string, string>> GetPlayerGlossEntries();`（表面形, プレイヤーの推測訳）。
   書き出し JSON の形式に依存しないため。あわせて `ITabletApp.Draw(Rect area)` の座標の約束（BeginArea 済みかどうか）の明記。
4. **次の星への引き継ぎを細かく指定**（`NewGameSettings`）：`KeepItems/KeepMoney/KeepTools/KeepCompanions` などの個別フラグと、
   各モジュールが PlayerProfile の取捨に反応できる `OnCarryOver(NewGameSettings)` のようなフック。現在は KeepProfile=true で全部残し、Story がローカルの持ち物・お金・道具だけを調整している。
5. （任意）`CharacterAppearance` の拡張欄（眉・ほっぺ・模様・2つ目のアクセサリー等）。現在は既存の欄（インデックス＋色）の範囲で選択肢を増やしている。
