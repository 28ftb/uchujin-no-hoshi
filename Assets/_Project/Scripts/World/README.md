# World モジュール（ワールド生成・地形・村・地図）

`Uchujin.World.WorldSystem`（SaveKey `"world"`、DailyUpdateOrder 20）。仕様：`uchujin_no_hoshi_village_map_spec_v0_3.md`。

## ファイル
| ファイル | 内容 |
|---|---|
| `WorldSystem.cs` | システム本体（生成→表示→引っ越し反映→踏み跡・公共施設→地図→セーブ） |
| `WorldTuning.cs` | 調整用の【仮数値】すべて |
| `VillageChanges.cs` | 生活による変化（踏み跡・公共施設の整備）、寄付コマンド、整備物の外観 |
| `Generation/WorldGenerator*.cs` | 決定的なワールド生成（地形・水系・村・道路・区画・自然・釣り場・エリア・生息地点・検証） |
| `Generation/TerrainShape.cs` | 海岸・山・高台・平地のうねりの解析関数 |
| `Generation/Heightfield.cs` | 高さ・水面グリッド（HeightSampler / WaterTester の正本、橋の上は橋板の高さ） |
| `Generation/RoadPlanner.cs` | 道路の A*（勾配・水・建物を避け、既存の道を再利用） |
| `Generation/WorldPaletteFactory.cs` | 星ごとの配色 |
| `Generation/FallbackWorld.cs` | 生成がすべて例外で失敗した時だけ使う非常用ワールド |
| `Scene/TerrainBuilder.cs` | カラーマップ地形（チャンク＋MeshCollider）、外周の景色、水面・滝、外周の壁、地図テクスチャ |
| `Scene/MeshBatcher.cs` | 低ポリ形状、パレットテクスチャ、チャンク結合（自然物を軽く描く） |
| `Scene/NatureBuilder.cs` | 木・岩・花・草・キノコ・倒木・葦・貝・結晶 |
| `Scene/StructureBuilder.cs` | 施設の外観、橋、桟橋、農場の柵、看板・道標 |
| `Scene/HouseBuilder.cs` | 住民の家（区画・住民ごとに形と色が違う）／空き地 |
| `Scene/WorldBehaviours.cs` | 夜の灯り、看板、工事中の目印、ゆれる小物 |
| `Map/MapKnowledge.cs`・`Map/MapApp.cs` | 地図アプリ（AppId `"map"`）と個人の記録 |

## 実装した内容（仕様との対応）
- **シードからの決定的生成**（§1.1〜1.4、§15）：用途ごとに独立した乱数キー（`world.gen1.aN.*`）。同じシード・同じ生成バージョンなら同じ結果（コンソール検証で確認）。生成バージョンは `WorldTuning.GeneratorVersion`、星の `worldGenVersion` と違えば警告。
- **地形**（§3.1）：海岸の向き・海岸線のゆらぎ・山（鉱山）・高台（見晴らし台）・平地のうねり・外周の丘。シードで海の方角・山の位置・川の流れ・池の数・村の場所・配色が変わる。
- **水系**：山の中腹から海へ蛇行する本流（上流で落差の大きい所を滝に）、55%で池へ注ぐ小川、池2〜4個、片側の海。
- **村**（§2.2、§4）：広場（目印の大きな木・井戸・掲示板・ベンチ・街灯・看板）、広場を囲む主人公の商店・伝言広場（商店の隣）・種屋・雑貨屋・家具職人・共用調理場・鍛冶屋・修理屋。40%ずつ鍛冶屋は鉱山への道沿い、修理屋は不時着地への道沿いに建つ（ロケット仕様 §15.2）。釣り小屋は村に近い水辺（裏に小さな桟橋）。海岸が近ければ桟橋。
- **主人公の拠点**（§6）：家＋農場 `FarmRect`（24m×18m、平坦）＋周囲 12m の拡張用の余地（平坦・木や家を置かない・道が避ける）。柵は余地の3辺（家の側は開ける、コライダーなし）。
- **不時着地**（§9）：村から 100〜165m の開けた場所。クレーター（中央は平ら）・残骸・工具箱・煙。`PlayerSpawn` はクレーターの外の村側。
- **鉱山入口**（§8）：山の斜面を均した前庭、岩肌・木枠・暗い入口・レール・トロッコ・ランタン。
- **道路**（§3.2）：広場から各目的地へ A*、さらに近道を2本、森の小道。川を渡る所は橋（`bridgeEdges`、橋の上は `HeightAt` が橋板を返す）。施設の入口・各住宅の入口を道路グラフに接続。
- **住宅区画**（§5.1）：道沿いに最大24区画（最低20、足りなければ外周に追加）、各区画に `FacilityInfo(ResidentHouse, plotIndex)`（`id = facility.house.N`、`HousePlot.facilityId` も設定済み）。
- **釣り場**（釣り仕様 §6）：川（`river` `upstream/downstream` `deep/shallow` `bridge` `waterfall` `estuary` `forest` `rocky` `village` `stream`）、池（`pond` `still` `deep/shallow` `forest` `village`）、海（`sea` `beach/rocky` `estuary`）、桟橋（`sea` `pier` `deep`）。通常 35〜60 箇所。
- **生息地点**（生き物仕様 §4・§7.3）：`tree` `bush` `rock` `flower` `grass` `mushroom` `fallen_log` `reed` `shell` `crystal` `mud` `water` `sand` `roof` `window` `garden` `streetlight` `bench` `well` `box` `debris` `cave` `lookout` `field`。通常 900〜1200 点。
- **エリア**：方角つきの名前（例「東の森」「南の浜辺」「川の上流」「北西の住宅地」）。HUD の地域名に使われる。
- **検証と再試行**（§16）：必須施設・道でつながるか・入口が道に近いか・家/鉱山/不時着地/釣り小屋までの道のり・区画数・釣り場数・生息地点数・農場の平坦さ・開始地点。問題があれば別の乱数系列で決定的に再試行（最大6回）。200シードで全件成立（約1割が再試行）。
- **表示**：ランタイム生成のカラーマップ（草・森の床・花畑・砂浜・岩肌・道・石畳・農場の土・クレーター）を貼ったチャンク地形と MeshCollider、外周の見た目の地形、水面（海・川・池・滝）、外周の見えない壁。建物は施設ごとに特徴のある外観。街灯（Light付き）・窓・ランタンは夜（18〜6時、暗い天気）に灯る。
- **看板**（宇宙語）：`ISpeechService.RenderWord("PLACE:"+facilityId)` を `WorldLabels`（宇宙語スタイル）で表示。翻訳しない。広場の出口には矢印つきの道標。
- **引っ越しで村が変わる**（§5.3、§11）：`ResidentMovedInEvent/OutEvent` と2秒ごとの確認で、区画の外観を作り直す（住民の系統・色で家の形と色が変わる。空き地は「空き家」の看板。引っ越し準備中は荷物の箱）。
- **生活による変化**（§11）【提案を実装】：
  - 踏み跡：プレイヤー（マルチは全員）と住民がよく通る所の草が踏まれて土の道の色になる。毎日少し戻る。`SaveScope.Planet`。
  - 公共施設の整備：広場の花壇・伝言広場の飾り・見晴らし台の東屋・橋の灯り・桟橋の延長・池の木の桟橋・森の休憩所・小さな公園を、シードの順に1つずつ。住民の暮らし（商店の売上・依頼の達成・贈り物・修理）と毎日の進み、主人公の寄付（材料・100G、`ContributeToProjectCommand`）で完成。ロケット修理の必須条件ではない。
- **地図アプリ**（§13、AppId `"map"`、M キー）：上から見た地図（陰影・等高線・建物の形）、探索した範囲だけ表示（霧）、プレイヤーと他のプレイヤーの位置、自分の拠点（家・畑・自分の店・宇宙船）だけ自分の言葉で表示、ピン（名前・自由メモ・色）、看板で見た宇宙語（見た場所に表示・自分の推測を書き添え可能）。施設の用途や和名は出さない。看板を調べると資料ボックスに「看板の文字」（事実のみ）を追加。`SaveScope.PlayerPlanetKnowledge`（星ごと・個人ごと）。タブレットが無い場合は M キーで予備ウィンドウ。

## 未実装・簡略化
- 住民が置いた物（日常の痕跡のうち「物」）は未実装（踏み跡で代替）。住民モジュール側のイベント・契約が無いため。
- 地図の「写真」「住民の家に呼び名を関連付け」「生物・魚の発見場所の自動記録」（§13.1）は未実装（ピンとメモで代用可能）。
- 川の上を歩いて渡れる（泳ぎは無く、川底を歩く）。橋の外を渡ることは禁止していない。
- 地形のセルフシャドウ（地形メッシュは影を落とさない設定）。
- 鍛冶屋・修理屋の「担当住民」は住民モジュールが `ownerResidentId` に設定（World は設定しない）。

## Unity 上で確認が必要な点
- 見た目全般（配色・建物の大きさ・看板ラベルの位置・夜の灯りの明るさ・水の透明度）。URP では `MaterialCache` の透明設定、`Sprites/Default`（滝などには未使用、Weather 側）の可否。
- 読み込み時間（生成はコンソールで 0.15〜0.4 秒。Unity 上ではカラーマップ作成・メッシュ結合が加わる）。
- 点光源の数（街灯 30 前後＋家の灯り）。Built-in の Pixel Light Count / URP の追加ライト上限で見え方が変わる。
- 住民の歩行：道路グラフ・橋の高さ・入口の位置（`FacilityInfo.entrance` は建物前の地面）。
- 鉱山入口の外観と Mining のポータル位置（`Layout.MineEntrance` は入口の開口部の地面、正面は `rotationY`）。
- 主人公の商店の土台（Shop の店舗が `facility.player_shop` の position/rotationY/size(10×4.5×8) に合うか）。

## 他モジュールとの連携
- 設定する：`ctx.Layout` / `WorldSystem.Layout`（`Game.Layout`）、`ctx.Shared["world.generated"]`（内部データ）。
- 施設 ID（同じシードなら同じ）：`facility.plaza` `facility.player_shop` `facility.message_plaza` `facility.bulletin_board` `facility.blacksmith` `facility.repair_workshop` `facility.seed_shop` `facility.general_store` `facility.carpenter` `facility.fishing_hut` `facility.community_kitchen` `facility.lookout` `facility.player_house` `facility.mine_entrance` `facility.rocket_site` `facility.well` `facility.bench.N` `facility.light.N` `facility.house.N`。
- 呼ぶ契約：`ISpeechService.RenderWord`、`ITabletService.RegisterApp/AddMaterial`、`IResidentDirectory.Get/ActiveResidents`、`IWeatherService.Daylight`。
- 購読：`ResidentMovedInEvent` `ResidentMovedOutEvent` `ShopItemSoldEvent` `QuestStateChangedEvent` `GiftGivenEvent` `RocketDeviceRepairedEvent`。
- コマンド：`ContributeToProjectCommand`（ホスト権威）。
- エリア：`AreaIds.Overworld` を登録（表示名は空にして HUD に地域名を出す。既定の出現位置は自宅の入口）。
- 作らないもの（他担当）：ロケット本体、鉱山内部へのポータル、主人公の家の扉、商店の店舗、マネピ。

## 調整用の仮数値
`WorldTuning.cs`（大きさ・地形・水・村・区画数・道路・検証のしきい値・自然物の数・踏み跡・整備・地図・灯りの時刻）。
寄付の換算は `WorldSystem.ItemPointsDivisor / MoneyPointsDivisor`、整備に必要な量は `WorldGenerator.PlanProjects`（`24 + i*8`）、道路探索のコストは `RoadPlanner` 先頭の定数。
