# Codex への初回の依頼文（開発環境の確認）

Codex で『宇宙人の星』の開発を始める時に、下の枠の中をそのまま渡す。
作業ルールは `AGENTS.md`（リポジトリのルート）にあり、Codex は毎回自動で読む。この依頼文は初回の環境確認用。

---

```text
あなたは Unity 向けゲーム『宇宙人の星』の開発に参加します。報告は途中経過も結果もすべて日本語で行ってください。
リポジトリの正本は C:\Users\81901\Documents\ChatGPT\uchujin-no-hoshi です（GitHub: 28ftb/uchujin-no-hoshi）。
今回は開発環境の確認だけを行い、ゲームのコードは変更しないでください。

## 0. 最初に読む
- リポジトリのルートの AGENTS.md を全部読み、そのルールに従ってください。
- 続けて README.md、Docs/ARCHITECTURE.md、Docs/IMPLEMENTATION_STATUS.md を読んでください。

## 1. 環境の確認
- 次を実行し、結果の要点を報告してください。
  pwsh -NoProfile -File C:\Users\81901\Documents\Codex\UnityDevelopment\Tools\Test-UnityDevelopment.ps1 -ProjectPath C:\Users\81901\Documents\ChatGPT\uchujin-no-hoshi
- このプロジェクトの Unity は 6000.6.4f1 です（ProjectSettings/ProjectVersion.txt）。
  C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Unity.exe があることを確かめてください。
  UnityDevelopment の既定の 6000.3.24f1（Enter-UnityDevelopment.ps1 の UNITY_EDITOR_PATH）では絶対に開かないでください。
  必要なら $env:UCHUJIN_UNITY に 6000.6.4f1 のパスを設定してください。
- dotnet SDK 8 以上（UnityDevelopment の dotnet 10.0.400 で可）と Git Bash（C:\Program Files\Git\bin\bash.exe）があることを確かめてください。

## 2. 自分用の作業フォルダ（worktree）を作る
- 正本の checkout ではブランチを切り替えないでください（Claude Code が同じ checkout で作業しています）。
- 正本の checkout で git status、git branch -a、git worktree list を確認したうえで、最新の開発ライン visual/urp から worktree を作ってください。
  git -C C:\Users\81901\Documents\ChatGPT\uchujin-no-hoshi worktree add -b codex/env-check C:\Users\81901\Documents\Codex\UnityDevelopment\Worktrees\uchujin-no-hoshi-env-check visual/urp
- 以降の作業はこの worktree の中だけで行ってください。

## 3. 確認コマンドを通す（worktree の中で）
- pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode TypeCheck
- pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode Compile
  （worktree では初回に Library/ を作るので数分かかります）
- pwsh -NoProfile -File Tools/Unity-Check.ps1 -Mode Smoke
- 3つとも「OK」になることを確認してください。どれかが失敗したら、原因を調べて報告し、修正する前に止まって私に聞いてください。
- ビルドとランチャーへの公開は今回は行わないでください（worktree からは公開されない作りです）。

## 4. 報告すること
- 使った Unity・dotnet・Git のパスと版
- Test-UnityDevelopment.ps1 の結果の要点（Unity のサインイン・ライセンスの状態を含む）
- 作った worktree のパスとブランチ
- TypeCheck / Compile / Smoke の結果（Smoke は [SMOKE] の行）
- 環境で困ったこと、AGENTS.md に足したほうがよいこと（提案として。AGENTS.md は勝手に書き換えない）
```

---

## 補足（人間向け）
- Codex 用の開発ツール（`Documents\Codex\UnityDevelopment`）の既定の Unity は 6000.3.24f1 だが、このプロジェクトは 6000.6.4f1（URP 17.6）に上げてある。別の版で開くとプロジェクトが変換されて壊れるので、依頼文と AGENTS.md の両方で禁止している。
- Unity は同じプロジェクトフォルダを同時に1つしか開けない。Claude と Codex が同時に Unity を動かす時は、Codex を worktree で作業させればぶつからない。
- 2回目以降は、普段の依頼に「AGENTS.md に従って」と書けば足りる。
