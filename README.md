# 宇宙人の星（Unity / C#）

別宇宙から来た主人公が宇宙人の星に不時着し、言葉の分からない村で暮らしながら宇宙語を自分で解読し、
釣り・農業・鉱山・生き物取り・クエスト・セルフ販売の商店でロケットを修理して故郷へ帰る — 生活シミュレーションです。

> **重要（現状）**：このリポジトリのコードはクラウド環境で **コード制作のみ** を行ったものです。
> **Unity Editor でのコンパイル・実行・動作確認は一度も行っていません。**
> クラウドでは NuGet の Unity 参照アセンブリ（UnityEngine.Modules 2021.3.33）に対する C# の型チェックだけを行い、全体でエラー0を確認しています。
> 実装済み／未実装／Unity で確認が必要な点は **[`Docs/IMPLEMENTATION_STATUS.md`](Docs/IMPLEMENTATION_STATUS.md)** を参照してください。

## ローカルで開く

1. Unity Hub で **Unity 6（6000.0 LTS 以降）** をインストール（2022.3 LTS でも動く想定ですが未確認）。
   `ProjectSettings/ProjectVersion.txt` は `6000.0.58f1` にしてあります。手元の別バージョンで開いても構いません。
2. このリポジトリを clone し、Unity Hub の「追加 → ディスクから追加」でフォルダを選んで開く。
   初回は `ProjectSettings` の残りと `Library/` が自動生成されます（数分かかります）。
3. メニュー **「宇宙人の星 → メインシーンを作成して開く」** を実行（空のシーン `Assets/_Project/Scenes/Main.unity` ができます）。
   シーンは空で構いません。**再生すると `GameBootstrap` がゲームを自動生成し、タイトル画面から始まります。**
4. 描画パイプラインは Built-in を想定（URP でも `MaterialCache` が `Universal Render Pipeline/Lit` を探して動く作り）。
5. 入力：旧 Input Manager／新 Input System のどちらでも動くようにしてあります（Player Settings → Active Input Handling が「Both」なら確実）。

### マルチプレイ（EOS P2P）
- 既定では EOS を使わず、**LAN（UDP）** で同じPCの2起動テストができます（2つ目は起動引数 `-uchujinTestSlot 2`）。
- EOS を使うには PlayEveryWare の *EOS Plugin for Unity* を導入し、Scripting Define Symbols に `UCHUJIN_EOS` を追加。
  手順は `Assets/_Project/Scripts/Network/README.md`。EOS の API 呼び出しは SDK 無しで書いたため **Unity 上での確認が必須** です。

## 操作（既定）
| 操作 | キー |
|---|---|
| 移動 / 走る（スタミナなし） | WASD・矢印 / Shift |
| 調べる・話す | E |
| 道具を使う | 左クリック / F |
| 道具切替 | R（Shift+R で逆） |
| 持ち物を持つ / 持ち物画面 | 1〜0 / I |
| タブレット / 地図 | Tab / M |
| 身振り（主人公は話さない） | G |
| 聞いた言葉を音声カードに保存 | K |
| カメラ | 右ドラッグ回転、ホイール拡大、Z/C 回転、V 方式切替（斜め見下ろし／三人称／真上） |
| メニュー（セーブ・設定・マルチ） | Esc |
| クイックセーブ | F5 |

## 構成
- `Assets/_Project/Scripts/Core/` — 統括・時間・アイテム・セーブ・入力・UI・連携契約（全モジュールが依存してよい唯一の場所）
- `Assets/_Project/Scripts/<Module>/` — World / Weather / Language / Tablet / Residents / Shop / Fishing / Mining / Farming / Creatures / Cooking / Housing / Quests / Rocket / Story / Network
  - 各フォルダの `README.md` に、仕様との対応・未実装・Unity での確認点・仮数値の場所を記載。
- `Docs/ARCHITECTURE.md` — 設計と開発規約（必読）
- `Docs/IMPLEMENTATION_STATUS.md` — 実装状況の一覧
- `Docs/Specs/` — 仕様書の原本（最新の確定事項は `uchujin_no_hoshi_basic_decisions_v0_2.md`）
- `Tools/CompileCheck/check.sh` — Unity 不要の型チェック（`dotnet` が必要）

## 開発を続けるときの注意
- モジュールは Core にだけ依存し、他モジュールとは `Game.Instance.Service<IXxx>()` と `EventBus` で連携します。
- シードから作るものは `SeededRandom` / `StableHash` を使う（`UnityEngine.Random` 禁止）。
- 星の状態を変える操作は `WorldCommand` + `WorldAuthority.Submit`（ホスト権威）。
- 宇宙語は意味データ（`MeaningFrame`）で組み立て、正解の訳をプレイヤーに表示しない。
- 数値の多くは【仮数値】で、各モジュールの `*Tuning.cs` / `*Config.cs` / `*Tables.cs` にまとめています。
