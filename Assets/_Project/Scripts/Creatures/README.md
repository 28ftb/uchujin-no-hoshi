# Creatures（宇宙生物・生き物取り）— `Uchujin.Creatures.CreatureSystem`

SaveKey `"creatures"` / DailyUpdateOrder 250 / `ICreatureService` を実装。Core 以外のモジュールを直接参照しない。

## 1. 実装した内容（仕様との対応）

| 仕様 | 実装 |
|---|---|
| 生き物 §2・§3 50種 | `CreatureDatabase.cs`：No.001〜050 すべて。ID は `creature.001_mizupuku` 形式（`SharedItemIds.Species*` と一致）。名前・習性・発見場所・捕獲の手掛かり・捕獲後の魅力・活動時間・好む天候・宇宙固有現象・見た目（形・色）・好物・観察できる行動 |
| 能力（料理 §3.1・§3.2、ロケット §7） | 農業10種は農業仕様どおり。料理仕様の派生能力（Grind/Bubble/Splash/Pack/Mimic/ColorShift/Stack/MineralDust/Echo/Stick/Shade/Float/Sound）を割り当て。**未決定の能力は「提案」として下表のとおり割り当て**（`HasProposedAbility=true`） |
| 全種を労働要員にしない | 能力なし：ハッパリ・ピョコロ・モリマル・ナミパタ・シオリボ・モフピ・ヤネコロ・マルマド・ニジマユ（観察・かわいさ重視）。ホシノコは Glow のみ（象徴的な希少種。修理必須能力は持たせない） |
| 出現 §4（四季なし） | `CreatureSystem.Spawning.cs`：ホストのみ。プレイヤー周囲 14〜42m の `Layout.HabitatPoints`＋周辺の地面から、エリア種別・feature・時間帯・天候（雨/乾き/晴れ/風/霧/雨上がり）・宇宙固有現象・希少度で重み付け抽選。乱数は `SeededRandom.For(seed, "creatures.spawn.日.時")`（時間ごとに決定的〜準ランダム）。農場への自然誘致（作物があると花・果樹系が来る）、自宅の庭の家具（`IHousingService`）への誘致（§9） |
| 地下の種 | `AreaChangedEvent` で `mine:N` に入ると、Mining が `PlanetContext.Shared["mining.caveHabitats"]`/`["mining.floorOfPosition"]` を入れていればその階の洞窟生息地点、無ければプレイヤー周辺の床へのレイキャストで 2〜4匹出す（階の深さで種と希少度が変わる）。`OreMinedEvent` の後にホコリフが舞い上がる（25%）・近くのイワネムが起きる |
| 野生の行動 §3 | `WildCreature.cs`：跳ねて休む／驚くと転がって止まる／擬態（周りの色・目を隠す・時々小さく動く）／浮かぶ・風に乗って漂う／飛んで止まる／地中に潜って顔を出す／眠る（イワネムは岩を叩くと起きる）／マネピは主人公の身振り（G）を真似る／ムレピカは群れで光をそろえる／音（♪）で居場所が分かる種 |
| 捕獲の型 §5.2 | 網（`ToolType.CatchNet`）の `ToolUseRouter` ハンドラ（`CreatureToolHandler`）。通常（走ると逃げる）／忍び寄り（走ると11m先から逃げる・歩けば近づける）／タイミング（休憩・着地・夢中な瞬間＝隙1.0）／餌で誘う（餌・好物の作物を置くと寄って食べる。食事中が狙い目。匂いで寄ってくることも）／発見型（[E]でよく見る、近くでじっと立つと見抜ける。網が当たると驚いて姿を現す）／環境誘致（灯り・箱などの近く） |
| 二重捕獲防止 | `CatchCreatureCommand`（ホストが先着1名で確定、成功率もホストの決定的乱数） |
| 観察 §13 例B | 近くで見た／[E]観察／特徴的な行動を見る／名人に見せる で図鑑に「事実」を記録し `CreatureObservedEvent` を発行（同じ行動はゲーム内1時間に1回まで） |
| 所有個体 §6.3・マルチ §10.4 | 固有ID・大きさ・色違い（淡い/濃い/珍しい色：野生で見た色と一致）・性格・得意能力（その能力+1）・好物・来歴。成長 Lv1〜5（Lv3/5で能力Lv+1、ニジマユはLv3で繭→羽）・なつき度（70以上で成長1.5倍）。お世話（なでる1日1回、餌1日3回まで。好物なら大きく効く。義務ではない） |
| 配置 | 同行（後ろをついて歩く、最大3匹）・ケージ・農場・自宅の庭・住処家具・器具・商店・伝言広場（マネピ）・修理作業場。`SetPlacement` で他モジュールから設定可 |
| 星間移動 マルチ §10 | 同行＝PlayerProfile。星に置く＝預ける（`PlaceCreatureCommand`、Planet に保存、持ち主の Profile には「控え（Deposited）」が残る）。譲る＝ケースに入れて（`case.<speciesId>` アイテム、`ItemStack.data` に個体データ）渡す・取引・商店で売る・贈る |
| 交流 §7 | 飼っている子同士が近いと固有の行動（9組：§7の7組＋ポカリス×ネムリコ、タネコロ×タネパタ）。近くで見ると図鑑に記録 |
| 繁殖 §6.3【提案】 | 同じ場所に置いた同種・同じ飼い主・なつき60以上・Lv2以上の2匹で1日4%。親と違う色が出ることがある |
| 生き物取りレベル・名人 §5.4 | Lv1〜10（捕獲・新発見・観察で上がる。成功率・警戒距離に影響）。`ResidentJob.CreatureExpert` に `RegisterInteractionProvider`：技の実演（忍び足・網さばき・餌の使い方・観察のコツ。身振りと意味データで伝え、日本語訳は出さない）、網の強化（Lv2〜5）、餌を買う、見せた生き物の得意なことを身振りで実演（図鑑に記録）。全住民に「連れている生き物を見せる」（`EvaluateItem` で反応、住民辞典に事実を記録、親しさ+1〜2/日） |
| 図鑑 §10 | タブレット `"bestiary"`（`BestiaryApp`）。PlayerPlanetKnowledge。見た/捕まえた/迎えた数、初めての日、見た場所・時間帯・天気、観察した行動（事実）、行動から分かった得意そうなこと、名人から分かった手掛かり、自分で付けた呼び名、自由メモ。宇宙語の呼び名は書かない |
| 管理画面 | `CreatureManagerWindow`（図鑑アプリのタブ・飼っている子の[E]メニューからも）：一覧・詳細・配置変更・なでる・餌・名前・ケースに入れる・逃がす |
| GrantCreature | ローカルなら即迎える。リモートのプレイヤーにはホストが「自動で迎える」ケースを `Bridge.GrantItemToRemote` で渡す |

### 未決定能力の割り当て（提案）
| 能力 | 種 | 理由（既存の特徴） |
|---|---|---|
| 圧搾 Press | スナコロ(024)、イワネム(043) | 砂玉をぎゅっと押し固める／重い体で石をぺしゃんこにする |
| 発酵 Ferment | トビカサ(017)、ヌマプニ(029) | キノコの傘から胞子／泥の中でぷくぷく泡を立てる |
| 冷却 Cool | ツユツユ(030)、スミカゲ(045) | 朝露で触れた葉が冷たい／影の中がひんやり |
| 安定化 Stabilize | イワネム(043)、ムレピカ(047) | 眠ると少しも揺れない／群れで光の間隔をそろえる（位相をそろえる） |
| 感知 Sense | エダミミ(014)、コダマル(042) | 長い耳で遠くの足音を聞く／反響で探る |
| 加熱 Heat（追加） | ネムリコ(035) | 日なたで眠った体がぽかぽか（ポカリスに加えて燃料用の加熱役を増やす） |

燃料精製（Press/Ferment/Heat）に必要な能力は、いずれも序盤〜中盤に地上で捕まえられる種が持つ（ポカリス・スナコロ・トビカサ・ヌマプニ）。

## 2. 未実装・簡略化
- 野生の個体の**動きはネット同期しない**（出現・消滅・捕獲だけホストが確定）。端末ごとに少し違う位置に見える。
- ゲストの「同行」生物はホストから見えない（Profile は同期されないため）。預けた個体は見える。
- 鉱山の出現はホスト本人が階に入った時だけ（ゲストだけが入った階には出ない）。
- 罠・生物探知道具・捕獲容器の種類（§5.3）は未実装（網と餌のみ）。
- 寿命・空腹・疲労なし（仕様で未決定、義務的にしない方針）。
- 器具（Device）に入った個体の見た目は出さない（Cooking 側の担当）。商店・伝言広場のマネピも Tablet/Shop 側が出すので出さない。
- 図鑑の観察記録は「行動のキー＋日」。証拠写真の貼り付けは無し。
- 「次の星へ」選択時（`EndingFinishedEvent`）は、星に置いた自分の個体をケージに入れて連れて行く。星に残したい場合の選択UIは無い。

## 3. Unity 上で確認が必要な点
- 50種の模型（`CreatureVisuals.cs`）の見た目・大きさ・かわいさ、擬態の見分けやすさ、夜の発光。
- 網の届く距離（2.3m＋0.35/Lv）、警戒距離、捕獲成功率の手触り。忍び寄り型（タネパタ・ネムリコ・ユメボシ・ホシノコ）の難しさ。
- 出現数（プレイヤー周囲最大9匹）とパフォーマンス（`WorldLabels` を各生物に1つ登録している）。
- 鉱山の床へのレイキャストが Mining の床コライダーで期待どおり当たるか。
- 同行の追従（段差・屋内・エリア移動時のワープ）。
- タブレット内の IMGUI レイアウト（`Draw(Rect)` 内で `GUILayout.BeginArea(area)` を使用）。

## 4. 他モジュールとの連携
- **呼ぶ契約**：`IWeatherService`（出現・図鑑の天気）、`IFarmService.PlantedPlotCount`（農場誘致）、`IHousingService`（庭の家具誘致、住処家具の位置、自宅の位置）、`IRocketService.RocketPosition`（作業場の位置）、`ITabletService.RegisterApp/RecordResidentObservation`、`IResidentDirectory`（Speak/ChangeFriendship/AddMemory/RegisterInteractionProvider）、`IResidentInfo.EvaluateItem`。
- **発行**：`CreatureCaughtEvent`、`CreatureObservedEvent`（Behavior：`seen`、行動キー、`pair.*`、`expert.<能力>`）、`ToolUpgradedEvent`（網）。
- **購読**：`AreaChangedEvent`、`OreMinedEvent`、`PlayerGestureEvent`、`WeatherChangedEvent`、`EndingFinishedEvent`、`CreatureObservedEvent`（他モジュールが発行した観察も図鑑へ。`work.*`・`combo.*`・`ability.<CreatureAbility名>` は能力が図鑑で分かる）。
- **約束**：
  - `GetOwned("*")`（`CreatureSystem.AnyOwner`）＝この星にいる全員分＋自分の持ち歩く個体（Farming が使用）。
  - 見た目の SendMessage：`SceneRoot/OwnedCreatures/Creature_<uid>` に `"UchujinCreatureAnchor"(Vector3)`（住み場所）／`"UchujinCreatureWork"(Vector3)`（そこへ行って働く仕草）。
  - Cooking は `FindOwnedWithAbility` → `SetPlacement(uid, Device, stationId)`。Housing は `SetPlacement(uid, Habitat, furnitureInstanceId)`（家具の位置は `GetPlayerHomeFurniture` から引く）。Rocket は `SetPlacement(uid, RocketWorkshop, ...)`。
- **登録アイテム**：餌 `cfood.basic` `cfood.fruit_pellet` `cfood.nectar_drop` `cfood.mineral_cookie` `cfood.glow_powder` `cfood.moss_cake` `cfood.water_jelly` `cfood.seed_mix`（`ItemCategory.CreatureFood`）、生物ケース `case.creature.NNN_xxx`（50種、`Misc`＋tag `creature_case`）。
- **コマンド**：`CatchCreatureCommand` `PlaceCreatureCommand` `MovePlacedCreatureCommand` `RetrieveCreatureCommand` `CareCreatureCommand` `ReleasePlacedCreatureCommand`。
- **概念**：`Concepts.Species(id)` を50種分 `Concepts.Register`。

## 5. 調整用の仮数値
- `CreatureDatabase.cs`：種ごとの希少度・価格・色・大きさ・活動時間・天候・能力、餌の価格。
- `CreatureSystem.cs` 先頭：同行上限3、星に置ける上限80、成長経験値、生き物取りレベル経験値、繁殖率。`IndividualLook`：色違いの確率。
- `CreatureSystem.Spawning.cs` 先頭：出現間隔・最大数・距離・寿命・希少度の重み・餌の時間と範囲。
- `CreatureSystem.Commands.cs`：`CatchBaseByRarity` と `ComputeCatchChance`。
- `WildCreature.cs`：`CheckAlert` の警戒距離、`CatchWindow` の隙。
- `CreatureToolHandler.cs`：網の距離。`CreatureExpertProvider.cs`：技の条件・網の強化費用。
