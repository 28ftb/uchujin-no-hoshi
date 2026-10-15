# 宇宙人の星：AI エージェント共通の作業ルール

Codex・Claude Code など、このリポジトリで作業するエージェントは最初にこのファイルを読むこと。
ユーザーへの報告は途中経過も結果も日本語で行う。

## 1. まず読むもの
- `README.md`、`Docs/ARCHITECTURE.md`（設計と開発規約）、`Docs/IMPLEMENTATION_STATUS.md`（実装状況とローカルで直したこと）。
- 仕様書の原本は `Docs/Specs/`。仕様書どうしが食い違う時は `uchujin_no_hoshi_basic_decisions_v0_2.md`（最新の確定事項）を優先する。
- Editor での確認手順と過去の確認結果は `Docs/PLAYTEST_CHECKLIST.md`。

## 2. 開発環境（このPC）
| もの | 場所・版 | 注意 |
|---|---|---|
| Unity Editor | **6000.6.4f1**：`C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe` | `ProjectSettings/ProjectVersion.txt` と同じ版だけを使う。`Documents\Codex\UnityDevelopment\Editors\6000.3.24f1` など**別の版で開かない**（プロジェクトが別の版へ変換され、URP 17.6 も壊れる）。 |
| 描画 | URP 17.6.0・Linear 色空間・Scripting Define `UCHUJIN_URP` | 設定アセットは `Assets/_Project/Settings/`。後処理と点光源の補正はコード（`Core/Visual/Urp*.cs`）。 |
| 入力 | Active Input Handling = Both（Input System パッケージは未導入） | `GameInput` は旧 Input Manager の経路で動く。 |
| dotnet SDK | 8 以上（`%LOCALAPPDATA%\Microsoft\dotnet` の 8.0、または `Documents\Codex\UnityDevelopment\Toolchains\dotnet-10.0.400`） | 型チェック専用。`C:\Program Files\dotnet` はランタイムだけで SDK が無い。 |
| Git Bash | `C:\Program Files\Git\bin\bash.exe` | `check.sh` の実行に使う。 |
| Blender | 5.2：`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` | 宇宙生物モデルの制作用（`ArtSource/`）。 |

環境を診断する時の注意：
- `Documents\Codex\UnityDevelopment\Tools\Test-UnityDevelopment.ps1` の `unityEditorVersion` / `unityEditorPath` は、**診断スクリプト自身の既定の Unity（6000.3.24f1）**を報告するもので、このプロジェクトが使う版ではない。プロジェクトの版は `ProjectSettings/ProjectVersion.txt`（同スクリプトの `project.unityVersion`）で判断し、実際に使えるかは `Tools/Unity-Check.ps1 -Mode Compile` で確かめる。
- Unity CLI（`Tools\unity.exe`）の **サインイン状態（`auth status`）とライセンス状態（`license status`）は別々に判断する**。バッチモードの実行に必要なのは有効なライセンスで、ログに「アクセストークンが無い」と出ていてもライセンスが有効ならコンパイル・スモークテストは動く。報告ではこの2つを分けて書く。

## 3. 確認コマンド（PowerShell。リポジトリのルートで実行）
```powershell
pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode TypeCheck   # Unity 不要の型チェック（数秒）
pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode Compile     # Unity でコンパイル（error CS を一覧）
pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode Smoke       # ウィンドウなしで起動〜セーブ〜ロードの通し確認（約4分）
pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode Visual      # 画面ありで通し確認＋各段階を Logs/Smoke/*.png に撮影（約6分）
```
- コードを変えたら最低でも **TypeCheck と Compile** を通す。起動や見た目に関わる変更は **Smoke**、見た目の変更は **Visual** で撮影し、画像を自分で見て確かめる。
- 型チェック（`check.sh`）は NuGet の Unity 2021.3 参照に対するもので、Editor フォルダと `#if UCHUJIN_URP` / `#if UCHUJIN_EOS` の中は対象外。Unity でのコンパイルの代わりにはならない。
- **Unity Editor でこのプロジェクトを開いている間はバッチモードが使えない**（同時に1つしか開けない。スクリプトがロックを検出して止まる）。
- `-nographics` は使わない（URP では RenderTexture が作れずキャラメイクのプレビューでエラーになる）。`Unity-Check.ps1` は使っていない。
- 既知の事象：Unity の起動直後に **`UnityEditor.Search`（`SearchDatabase` … `ArgumentOutOfRangeException`）の例外**がログに出ることがある。Unity Editor 自身の検索インデックス処理のもので、ゲームのコードとは無関係。スモークテストの集計からは除外している（`BatchSmokeTest.OnLog`）。
- Editor で初めて再生した直後は、シェーダーを裏でコンパイル中の水色一色の仮表示が写ることがある。2回目以降の撮影で判断する。

## 4. ビルドとランチャーへの公開
- ビルド：`powershell -NoProfile -ExecutionPolicy Bypass -File Tools\Build-Windows.ps1`（Editor を閉じて実行。成功すると `Builds/Windows` をゲームランチャーへ公開し、完了まで待つ）。設定は `Assets/_Project/Scripts/Editor/BuildTools.cs` と `GameHubPublish.cs`。
- 検証用のビルドは `$env:GAMEHUB_SKIP_PUBLISH='1'` を付ける。**公開は正本の checkout（`C:\Users\81901\Documents\ChatGPT\uchujin-no-hoshi`）の `Builds/Windows` からだけ**行われ、worktree からは公開されない。
- URP のシェーダーは `Assets/_Project/Resources/ShaderRefs/` のマテリアルでビルドに含めている（Always Included に URP/Lit を入れるとビルドが極端に遅くなる）。URP 移行後のビルドでの見た目は**まだ確認していない**。最初のビルドではピンク色・非表示の物がないか必ず確かめる。

## 5. 守ること
- **機能の削除・仕様の縮小・大規模な書き換えでエラーを消さない。** 最小限の修正で直し、迷ったら止まってユーザーに聞く。
- 依存ルール：各モジュールは `Core` だけに依存し、他モジュールとは `Game.Instance.Service<IXxx>()` と `EventBus` で連携する（`Docs/ARCHITECTURE.md` §2）。
- シードから作るものは `SeededRandom` / `StableHash`（`UnityEngine.Random` 禁止）。星の状態を変える操作は `WorldCommand` + `WorldAuthority.Submit`。宇宙語は意味データで組み立て、正解の訳を表示しない。
- Unity 6 の API：`FindObjectOfType` / `FindFirstObjectByType` / `GetInstanceID` は使わない（`FindAnyObjectByType` など）。版で分ける時は `#if UNITY_6000_6_OR_NEWER`。
- URP の型を使うコードは `#if UCHUJIN_URP` で囲む（型チェックが通るように）。`#if UCHUJIN_EOS` の中は EOS プラグインを入れるまで触らない。
- 数値の多くは【仮数値】で、各モジュールの `*Tuning.cs` / `*Config.cs` / `*Tables.cs` にある。

## 6. Git と、ほかのエージェントとの並行作業
- 作業は新しいブランチで行い、意味のある単位でコミットする。**main への直接 push と force push はしない。push はユーザーの確認後**。
- Claude Code も同じリポジトリで並行して作業している。**正本の checkout のブランチを勝手に切り替えたり、他人のブランチにコミットを積んだりしない。** 自分の作業は `git worktree add` で作った別フォルダ（例：`C:\Users\81901\Documents\Codex\UnityDevelopment\Worktrees\uchujin-no-hoshi-<作業名>`）で行う。worktree は初回に `Library/` を作るので最初の Unity 起動に数分かかる。
- 作業を始める前に `git status` と `git branch --show-current`、`git worktree list` で状況を確かめ、意図しないブランチにいないか確認する。
- コミットしない：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`Builds/`、`*.csproj`、`*.sln`（`.gitignore` 済み）。Unity が生成した `ProjectSettings/*.asset`・`Packages/packages-lock.json`・`.meta` はコミットする。
- 直した内容は `Docs/IMPLEMENTATION_STATUS.md` に追記する。
