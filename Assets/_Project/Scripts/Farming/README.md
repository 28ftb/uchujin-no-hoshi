# Farming（農業・農場）— `Uchujin.Farming.FarmingSystem`

SaveKey `"farming"` / DailyUpdateOrder 200 / `IFarmService` を実装。生物は `ICreatureService` 経由でのみ扱い、Creatures のクラスは参照しない。

## 1. 実装した内容（仕様との対応）

| 仕様 | 実装 |
|---|---|
| 農業 §2.1 基本の流れ | `Layout.FarmRect` 内の1m格子。クワで耕す→種（手に持った種）を植える→じょうろで水やり（雨・光雨 `WeatherState.WatersCrops` で自動）→日ごとに成長（水がある日だけ1日分）→収穫（手・[E]・カマ）。作業はすべて `WorldCommand`。`CropPlantedEvent`/`CropHarvestedEvent` |
| 見た目 | `FarmingSystem.View.cs`：乾いた土／湿った土、種→芽→育ち中→実り（花・果樹・根菜・浮く実・夜に光る実）、肥料、土の豊かさで色が変わる。柵、倉庫、収穫かご、種箱、看板、住み場所 |
| 作物 §9.1・§9.2（約20種） | `FarmCrops.cs`：21種（ふわふわトマト・ホシベリー・しましまイモ・ホシピーマン・ニジニンジン・ベルフラワー・**星蜜果**・クモマメ・ツキムギ・ヒカリダイコン・プルプルキャベツ・アワイチゴ・カゼナスビ・キリタケ・ネツコショウ・ユキレタス・ハモンメロン・ホシトウモロコシ・ユメアサガオ・ヒナタソウ・ギンカジュ）。分類（一度きり／連続収穫／果樹／穀物／花／特殊）・色/味タグ・好む天候 |
| 星蜜果（必須） | `SharedItemIds.CropStarHoneyFruit`(`crop.star_honey_fruit`)・`SeedStarHoneyFruit`(`seed.star_honey_fruit`)。連続収穫・6日（再収穫3日）・四季なしでいつでも栽培可。タグ Fuel/RocketMaterial |
| 天候 時間・天候 §7 | 雨・光雨で自動水やり、光雨を浴びると品質+、作物ごとに雨/風/霧/晴れで成長が早まる |
| 肥料 §9.4 | ふつう（品質）・成長（速さ）・保水（翌日まで水が残る）・実り（収穫量） |
| 品質・サイズ §7.2 | 品質0〜3（土の豊かさ・肥料・毎日の水やり・水やり＋土づくりの協力・光雨・農業Lv）。巨大な作物（しましまイモ・プルプルキャベツ・ハモンメロン） |
| 生物による変異 §7.1 | 光る生物の夜の光を2晩以上→光るホシベリー／光るユメアサガオ。夜の受粉＋光→虹色ベルフラワー。ニジニンジンは抜くまで色が分からない。受粉で収穫量+、ポカリスの暖かさで成長+、冷やす生物でユキレタス、光でヒカリダイコンが早く育つ |
| 生物の自動化 §3・バランス §8.2 | `FarmingSystem.Creatures.cs`：`GetOwned("*")` から Placement==Farm を集め、住み場所の周り（(2r+1)²、r=能力Lv＋好きな住み場所なら+1）で働く。毎時：水やり(6-9時)・耕す(収穫後の畑)・種まき(種箱の種)・収穫(かご／運ぶ子がいれば倉庫)・運搬(かご→倉庫)・夜の光(19-2時)。毎日：土壌改良・受粉・温める・冷やす。作業量はLvと能力Lvで増える。**手作業を毎日残さなくてよい強さ**にしてある |
| 種集め | 収穫した畑が種集めの子の範囲なら種箱に種が増える |
| 住み場所 §8.1 | 10種（小さな池・巣穴の土山・止まり木・果物かご・花壇・夜光草の鉢・巣箱・ぽかぽか寝床・ひんやり石・生き物小屋）。手に持って農場の空き地で使う。[E]で住まわせる・片付ける。住み場所未指定の子は好みに合う所へ自動で引っ越す |
| 協力作業の発見 §6 | 水やり＋土づくり／耕す→種まき／収穫→運搬／夜の受粉＋光。初めて起きると農場アプリに記録し、近くにいれば `CreatureObservedEvent("combo.*")` |
| 働く様子 | 生物の見た目へ SendMessage（`UchujinCreatureWork` で作業した区画へ行く、`UchujinCreatureAnchor` で住み場所の周りに暮らす）。近くで見ると `CreatureObservedEvent("work.*")`→図鑑に「働いていた」事実が残る |
| 勝手に売らない §3.3 | 収穫物はかご・倉庫に入るだけ。商店には出さない |
| 農地拡張 §8.2 | 8×6 → 12×8 → 16×10 → 20×14 → 区画全体（お金。農場アプリ・看板・農業名人から） |
| 農具 §10.1 | クワ・じょうろ・カマ：Lv1 1マス／Lv2 前方3／Lv3 3×3／Lv4 3×5／Lv5 5×5。**鍛冶屋（`ResidentJob.Blacksmith`）に「農具を強化してもらう」を追加**（鉱石＋お金） |
| 農業レベル §10.2 | Lv1〜10（作業・収穫で上がる。品質・収穫量・種屋の品ぞろえに影響。スタミナ要素なし） |
| 農業名人 §10.3 | `ResidentJob.FarmingMaster`：技の実演（生育を見極める・連続種まき・広範囲の収穫・肥料の使い方・特別な栽培技術）。身振り＋意味データで伝える。農地拡張の相談 |
| 種屋 §11.1 | `ResidentJob.SeedSeller` の会話メニューと `FacilityType.SeedShop` 前の棚（店の人が不在でも買える）で `VendorWindow`。種・肥料・住み場所・基本の生き物の餌 |
| 遊びに来る住民 §14.2 | 10時・15時に、生き物や実りがあれば住民（4割で農業名人）を `RequestGoTo` で呼び、着いたら意味データで話す（生き物がかわいい・花がきれい・たくさん実った／名人は乾いた畑を指さす or うなずく）。住民辞典に事実、住民の記憶 `visited_farm` |
| タブレット §12 | AppId `"farm"`：区画の地図（状態を色分け）、農場の生き物と住み場所変更・作業記録・見つけた協力作業、作物の記録（数・最高品質・巨大・光る・珍しい色・生物の収穫）、農場（Lv・農具・拡張・収納・共有設定・技） |
| マルチ §15・マルチ §7 | すべての作業がホスト確定のコマンド（ゲストも Helper 以上で耕す・まく・水やり・収穫）。植えた人を記録し、**他人の作物は収穫者へ移さず農場の倉庫へ**（ホストの共有設定で収穫者が持ち帰れる）。倉庫から受け取るのは共同生活権限以上。ゲストが植えた作物は星に残って育つ |

### 星蜜果の種の入手条件（README 指定事項）
序盤〜少し進んでから：**農業Lv2 以上、または 3日目以降、またはロケットの装置を1つでも直した後**に種屋に並ぶ（`FarmingSystem.IsSeedUnlocked`、`FarmCrops.cs` の `UnlockLevel/UnlockDay`）。種 120G、実 150G【仮】。

## 2. 未実装・簡略化
- 枯れる・雑草・害虫は無し（仕様に無く、義務的にしない）。水が無い日は成長が止まるだけ。
- 繁殖・寿命など生物側の管理は Creatures 側。
- 作物の加工（ジャム等）は Cooking 担当。
- 生物の作業はホストだけで計算する（ゲストの画面では作業の SendMessage 演出は出ない。結果はスナップショットで反映）。
- 「農場に遊びに来る住民」の汎用イベント型が Core に無いため、Farming 内で `RequestGoTo` と発話を行っている（下記「コアへの要望」）。
- オノ（§10.1）は ToolType に無いので未実装。
- 種の指定（どの種をまくか）は「種箱に入っている順」で代用。

## 3. Unity 上で確認が必要な点
- 区画の位置（`FarmRect` が無い場合は出現地点そばに 20×14 を仮に作る）、倉庫・かご・種箱・看板が区画の西側の外に置かれるので、地形・道とぶつからないか。
- 道具の範囲（前方の判定：`ctx.GroundPoint` のマスから向きを軸にスナップ）と操作感。
- 作物の見た目（段階・浮く実・夜の発光）、マスごとの GameObject 数（全区画で数百）とパフォーマンス。
- 生物の作業量が強すぎ／弱すぎないか（`Quota` と範囲）。
- 収穫物・種・お金の数値バランス。

## 4. 他モジュールとの連携
- **呼ぶ契約**：`ICreatureService`（`GetOwned("*")`、`SetPlacement(uid, Farm, habitatId)`、`GetSpeciesName`）、`IWeatherService`、`IResidentDirectory`（RegisterInteractionProvider/Speak/RequestGoTo/AddMemory/ChangeFriendship/FindByJob）、`ITabletService`（RegisterApp/Open/RecordResidentObservation）、`IRocketService.RepairedDeviceCount`（星蜜果の解放）。
- **発行**：`CropPlantedEvent`、`CropHarvestedEvent`（ByCreature）、`CreatureObservedEvent`（`work.*`・`combo.*`）、`ToolUpgradedEvent`（クワ・じょうろ・カマ）。
- **購読**：`WeatherChangedEvent`。
- **登録アイテム**：作物 `crop.<key>`（21）、種 `seed.<key>`（21）、変種（`crop.hoshiberry_glow` `crop.yume_asagao_glow` `crop.bell_flower_rainbow` `crop.shimashima_imo_giant` `crop.purupuru_cabbage_giant` `crop.hamon_melon_giant` `crop.niji_ninjin_red/yellow/purple`）、肥料 `fert.basic/growth/retain/yield`、住み場所 `farmhab.*`（`ItemCategory.CreatureItem`＋tag `farm_habitat`）。
- **使う他モジュールのID**：鉱石 `SharedItemIds.Ore*`（農具強化）、`cfood.basic`（種屋で販売。無ければ出さない）。
- **コマンド**：`FarmTillCommand` `FarmWaterCommand` `FarmPlantCommand` `FarmHarvestCommand` `FarmFertilizeCommand` `FarmPlaceHabitatCommand` `FarmRemoveHabitatCommand` `FarmExpandCommand` `FarmStorageTakeCommand` `FarmSeedBoxPutCommand` `FarmSetShareCommand`。
- **ToolUseRouter**：優先度 50（農場の区画内だけ処理。外では false を返す）。Housing が `ItemCategory.CreatureItem` を家具として置く処理を持つ場合、`farmhab.*` は農場の区画内では Farming が先に処理する（Housing の優先度次第で要調整）。

## 5. 調整用の仮数値
- `FarmCrops.cs`：作物の日数・収穫量・価格・種の価格・解放条件、肥料、住み場所の価格。
- `FarmingSystem.cs` 先頭：拡張サイズと価格、レベル経験値、収納の大きさ。`GrowAll`（天候・生物の成長補正）、`HarvestPlot`（品質・変異の確率）。
- `FarmingSystem.Creatures.cs`：作業時間帯、範囲 `Range`、作業量 `Quota`、種集め確率、観察距離。
- `FarmUI.cs`：農具の強化費用 `ToolCosts`、技の条件 `Techniques`。
- `FarmInteraction.cs`：道具の範囲パターン `Pattern`。
