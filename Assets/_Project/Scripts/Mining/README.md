# 鉱山モジュール（Uchujin.Mining）

> **2026-10-16 の変更（マップ設計 v0.2 §2.11）**：地下湖の釣り場は `MiningSystem.LakeSpot`（ワールド生成の検査ツールも使う）。魚50種の生息条件に合わせて、岸のタグに 浅い岸 `shore_shallow`（〜4階）・鉱石の多い岸 `ore_shore`（4〜8階）・最深部 `deepest`（最下階）を足した（従来の cold / deep / crystal はそのまま）。

`Uchujin.Mining.MiningSystem : GameSystemBase, IMiningService`（SaveKey `"mining"`、DailyUpdateOrder 100）。
仕様：`uchujin_no_hoshi_mining_spec_v0_4.md`（以下 §）、時間天候 §4.3【決定】、ロケット §4、バランス §9.2、マルチ §8、宇宙語 §9.3。

## ファイル

| ファイル | 内容 |
|---|---|
| `MiningSystem.cs` | システム本体。階層の登録、日替わりの再配置、採掘（コマンド）、移動、記録、強化・買い取り、セーブ |
| `MineCatalog.cs` | 調整用の仮数値（`MiningTuning`）、鉱石・宝石・発見物22種の定義と登録、ツルハシ強化表 |
| `MineGenerator.cs` | 1階分の地形（部屋・通路・壁・岩・結晶・明かり・地下湖・昇降機・階段）をシードから決定的に生成 |
| `MineNodes.cs` | 日替わりの採掘ノード生成、ドロップ（宝石の個体差）、ノードの表示（出現・消失・破壊の演出） |
| `MineFloorBuilder.cs` | 各階・入口のプリミティブ表示、昇降機・階段の `IInteractable` |
| `BlacksmithInteractions.cs` | 鍛冶屋・採掘師の会話メニュー、金床（呼び鈴）、ツルハシ強化画面、買い取り |
| `MineLogApp.cs` | タブレットの鉱山記録アプリ（AppId `"minelog"`） |
| `MiningTypes.cs` | 列挙・セーブDTO・WorldCommand |

## 実装した内容（仕様との対応）

- **10階層**（§2）：各階は屋外から離れた座標（x=6000＋120m×階）に生成し、`AreaManager` に `"mine:N"` で登録（`IsUnderground=true`, `ShowWeather=false`, 深さで色が変わる `AmbientOverride`）。
  地形（部屋・通路・壁・岩）は**シードと階数だけから決定的**（§3.4）。下り階段は入口から最も遠い部屋。
- **入口と昇降機**（§2.2）：`Layout.MineEntrance`（無ければ `FacilityType.MineEntrance` の入口）に坑道の門。入口と各階の昇降機から**到達済みの階へ直接**行ける。昇降機から地上へ戻れる。
- **鉱石・宝石**（§2, §6）：鉱石11種＋宝石5種＋発見物5種（化石2・古い歯車・古いツルハシの飾り・星図の石板）＋洞窟キノコ。深いほど高価・硬い。
  - 必須ID：`SharedItemIds.OreConductive`（1〜2階, 硬さ1）、`OreSturdyMetal`（3〜5階, 硬さ2）、`OreHeatproofMetal`（6〜8階, 硬さ3）、`OreEnergyCrystal`（9〜10階, 硬さ4）。いずれも `ItemTags.RocketMaterial` 付き。
  - 宝石の個体差（§6.1）：大きさ（cm）を `ItemStack.size`、透明度を品質（0〜3）に。
  - 化石・遺物は岩を壊した時に低確率で見つかる。
- **日付更新で即座に再配置**（時間天候 §4.3【決定】、マルチ §8.2）：`OnDayStarted` で全階の配置を作り直し、鉱山内のプレイヤーの目の前でも古い鉱石は光って消え、新しい鉱石が光って現れる。延期・最低保証なし。
  配置は「シード＋日付＋階」から決定的に作るため、セーブ・同期は**日付と採掘済み／途中まで叩いた差分だけ**。「今日は○○が多い」日、「宝石が多い」日も確率で起きる（§3.1）。
- **採掘と入力の同時発生への対処**：ノードの当たり判定はトリガー（目の前に出現してもプレイヤーが挟まらない）。`MineHitCommand` に配置日を入れ、ホストは日付が違えば失敗にする（再配置後の世界で判定）。消えかけのノードは叩けない。
- **採掘**（§4, マルチ §8.3）：ツルハシでノードを叩く（`IToolTarget`＋`ToolUseRouter` で前方の近いノードも拾う）。叩くたびに `MineHitCommand` をホストへ送り、ホストが累計ダメージ・採掘成立・獲得者を確定（同じ鉱石の二重取得なし）。
  硬さ＞ツルハシの段階なら「カキン！」と弾かれて掘れない。段階が上がるほど1回のダメージが増え、以前の岩も少ない回数で壊せる（§9.2）。`OreMinedEvent`（鉱石・宝石）を発行。
- **採掘レベル**（§5【提案】、スタミナなし）：1〜10。ダメージ、追加ドロップ、良い品質・大きい宝石の出やすさに効く。
- **ツルハシ強化は鍛冶屋**（§4【既存決定】）：前段階の鉱石＋お金。`ResidentJob.Blacksmith` の会話メニュー（`RegisterInteractionProvider`）と、鍛冶屋の施設（`FacilityType.Blacksmith`）の**金床（呼び鈴）**の両方から頼める（鍛冶屋が引っ越しても機能を失わない）。鍛冶屋は必要な鉱石を指さし、数を指で示す（宇宙語が分からなくても伝わる）。代金の6割は鍛冶屋の財布へ。`ToolUpgradedEvent`。段階名は `ToolTypeUtil.TierLabel`。
- **金属加工**：会話メニュー・金床から `ICraftingService.OpenStation(CraftStationType.Forge, 施設ID, player)`（加工レシピは Cooking 担当）。
- **鉱石を見せる**（§8.1, 宇宙語 §9.3）：鍛冶屋が鉱石を叩いて「硬い／とても硬い」「きれい」「古い」「珍しい」「（これで）強いツルハシを作る」を、実際の性質どおりに宇宙語で話す。
- **鉱山の話**：採掘師（`ResidentJob.Miner`）と鍛冶屋が今日の鉱脈（多い鉱石・宝石の多い日・深い階は硬い）を、下を指さし階数を指で示しながら話す（正しい内容のみ）。
- **直接売却（§11.2）の判断**：**低価格で採用**。鍛冶屋が目安価格の35%で買い取り、1日20個まで。代金は鍛冶屋の財布から出る（お金が無ければ断られる）。商店のセルフ販売の方が高く売れるようにして、商店の価値を損なわない。
- **最下層**（§12【提案】）：10階に「星の結晶の広間」。初めて近づくと短い演出とタブレット資料。
- **地下湖**（釣り仕様 §6, §12.3）：2・4・6・8・10階に地下湖。`Layout.FishingSpots` に地下湖の釣り場を追加し、釣りモジュールが地下の魚を出す。
- **タブレットアプリ "minelog"**：到達階・各階の特徴、見つけた物（数・初めての日と階・最大の宝石・最高品質）、その日に各階で見かけた鉱石、腕前と掘れる物の一覧（未発見は ？？？）。
- **マルチ**（§13）：同じ鉱山を一緒に掘れる。再配置は誰がいても日付更新で即時（ゲストも同じ日付なら同じ配置を決定的に再生成し、ホストのスナップショットで採掘済みが確定）。ゲストの最深到達はホストへ `ReachFloorCommand` で報告。観光（Sightseeing）権限では採掘不可。

## 未実装・簡略化（理由）

- **戦闘・HP・スタミナ**（§7【未決定】）：入れない。地下の生物（No.041〜050）は Creatures モジュールが出す。
- 鍛冶屋が実際にツルハシを打つ様子（住民の移動・アニメ）は Residents 側の担当のため、強化画面の「カン、カン」演出のみ。強化は即時（加工時間は未決定のため）。
- 階段は下りのみ（上りは昇降機で代用）。鉱山の広さ・階段の見た目は仮。
- 鍛冶屋の個別エピソード・依頼（§4.3, §9）は Quests モジュールの担当。

## Unity 上で確認が必要な点

- 洞窟の見た目と暗さ：`AmbientOverride` を Weather 側が反映するか、太陽光（Directional Light）が地下でも当たる問題（天井なし）。ランタンの点光源（1階最大6個）の明るさ・負荷。
- カメラ：壁（高さ2.4m）でカメラが寄りすぎないか。斜め見下ろし・三人称での見やすさ。
- 生成の遊びやすさ：部屋と通路の広さ（24×24マス・1マス3m）、行き止まり、地下湖の縁で釣りがしやすいか、岩の配置が通路をふさがないか。
- 再配置の演出（光って消える／現れる）の見え方と、日付更新の瞬間の負荷（全10階・1階あたり約17〜33ノード）。
- ノードのトリガー当たり判定とツルハシのレイ（高さ1m・3m）の当たりやすさ。
- 数値バランス：ノード数・岩の割合・宝石の出現・強化費用・硬さとダメージ・買い取り価格。

## 他モジュールとの連携点

- **呼ぶ契約**：`IResidentDirectory`（`RegisterInteractionProvider`, `Speak`, `FindByJob`, `Get`, `GiveMoney`, `TrySpend`, `GiveItem`, `ChangeFriendship`, `AddMemory`）、`ICraftingService.OpenStation(Forge)`、`ITabletService`（`RegisterApp`, `AddMaterial`, `RecordResidentObservation`）。null なら省略。
- **発行するイベント**：`OreMinedEvent`（採掘した人の端末で。ゲストの採掘はホストでも）、`ToolUpgradedEvent`（ツルハシ）。`AreaChangedEvent` は AreaManager が発行。
- **登録するアイテムID**：`ore.stone` `ore.rosy_copper` `ore.warm_coal` `ore.moon_silver` `ore.bubble_quartz` `ore.sky_gold` `ore.star_core`、`SharedItemIds.OreConductive/OreSturdyMetal/OreHeatproofMetal/OreEnergyCrystal`、`gem.dew_drop` `gem.leaf_jade` `gem.ember_heart` `gem.twilight_opal` `gem.star_tear`、`find.fossil_shell` `find.fossil_wing` `find.ancient_gear` `find.old_tool_charm` `find.star_map_tablet` `find.cave_mushroom`。
- **WorldCommand**：`MineHitCommand`, `UpgradePickaxeCommand`, `SellToBlacksmithCommand`, `ReachFloorCommand`。
  `MineHitCommand` の結果（採れた物）は `CommandResult.MessageEn` に `"mined:id|数|品質|大きさ;..."` で入れて返している（成功時は表示されない）。
- **エリアID**：`AreaIds.MineFloor(1..10)`（`"mine:1"`〜`"mine:10"`）。
- **他モジュールへの受け渡し（`PlanetContext.Shared`、PreparePlanet で設定）**：
  - `"mining.caveHabitats"`：`List<HabitatPoint>`（feature: `cave_wall` `dark` `rock` `crystal` `cave_lake` `deep_hall`、area は `AreaType.MineEntrance`）→ Creatures の地下生物用。Creatures は自分の BuildScene／OnPlayStarted 以降で参照してください（PreparePlanet は鉱山より先に呼ばれるため）。
  - `"mining.floorOfPosition"`：`Func<Vector3,int>`（座標→階。地上は0）、`"mining.floorCount"`：`int`。
  - 地下湖の釣り場を `Layout.FishingSpots` に追加（id `mine.lake.fN`、tags `underground, cave, floor:N, radius:R, cold(〜4階)/deep(6階〜)/crystal(8階〜)`）。
- **IMiningService**：`FloorCount`=10、`GetDeepestFloorReached`（ローカルは知識、他プレイヤーはホストの記録）、`OreIdsOnFloor`（その階に出る鉱石・宝石）、`GetMiningLevel`。
- **AppId**：`"minelog"`。

## 調整用の仮数値の場所

- `MineCatalog.cs` の `MiningTuning`（グリッド・座標・地下湖の階・ノード数・岩/採集物/宝石の割合・「多い日」の倍率・HP・ダメージ式・レベル表・買い取り率と上限・鍛冶屋の取り分・岩の硬さ帯）。
- `MineCatalog.Build()`（各鉱石の硬さ・出現階・重み・宝石の大きさ）、`MineCatalog.RegisterItems()`（値段・希少度）、`PickaxeUpgrade.Table`（強化費用）。
- `MineNodes.cs` の `RollDrops`（追加ドロップ・発見物・宝石の確率）。
