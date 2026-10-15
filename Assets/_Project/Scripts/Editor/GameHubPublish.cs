#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using Debug = UnityEngine.Debug;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// できあがった Builds/Windows を、友達のゲームランチャー（Friends GameHub の公開キット）へ渡す。
    /// キットは Unity の終了を待ってから公開するので、裏で起動してすぐ戻る。
    /// 結果は C:\GameHubPublisher\work\publish-after-build-uchujin-no-hoshi.json / .log に書かれる。
    /// </summary>
    public static class GameHubPublish
    {
        public const string GameId = "uchujin-no-hoshi";
        public const string DefaultKit = @"C:\GameHubPublisher";
        public static string KitRoot => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GAMEHUB_KIT")) ? DefaultKit : Environment.GetEnvironmentVariable("GAMEHUB_KIT");
        private static string Hub => Path.Combine(KitRoot, "outputs", "GameHub");
        private static string Work => Path.Combine(KitRoot, "work");
        public static string StartScript => Path.Combine(Hub, "Start-PublishAfterBuild.ps1");
        public static string LogPath => Path.Combine(Work, "publish-after-build-" + GameId + ".log");

        // Windows PowerShell 5.1 で -File 実行すると、キットの param の既定値に使う $PSScriptRoot が空になるため、
        // 起動スクリプトと Publish-AfterBuild.ps1 のパスはすべて明示して渡す。
        private static string Arguments()
        {
            string Q(string path) => "\"" + path + "\"";
            return "-NoProfile -ExecutionPolicy Bypass -File " + Q(StartScript) + " -Game " + GameId + " -LogRoot " + Q(Work)
                + " -SourceFile " + Q(Path.Combine(Hub, "sources.json")) + " -Repository " + Q(Path.Combine(Hub, "Distribution"))
                + " -StatePath " + Q(Path.Combine(Work, "publication-state.json")) + " -StageRoot " + Q(Work)
                + " -R2ConfigPath " + Q(Path.Combine(Hub, "R2", "r2.json")) + " -R2StatePath " + Q(Path.Combine(Work, "r2-publication-state.json"))
                + " -R2SyncScript " + Q(Path.Combine(Hub, "R2", "sync.py"));
        }

        /// <summary>公開しない理由（公開してよいなら null）。正本の checkout の Builds/Windows だけを配る。</summary>
        public static string SkipReason(string output)
        {
            if (Environment.GetEnvironmentVariable("GAMEHUB_SKIP_PUBLISH") == "1") return "GAMEHUB_SKIP_PUBLISH=1";
            if (!string.Equals(Path.GetFullPath(output).TrimEnd('\\', '/'), Path.GetFullPath("Builds/Windows").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return "出力先が Builds/Windows ではない";
            if (!Directory.Exists(".git")) return "正本の checkout ではない（worktree かコピー）";
            if (!File.Exists(StartScript)) return "公開キットが見つからない: " + StartScript;
            return null;
        }

        public static void AfterBuild(string output)
        {
            var skip = SkipReason(output);
            if (skip != null) { Debug.Log("GAMEHUB PUBLISH: SKIP (" + skip + ")"); return; }
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"), Arguments()) { UseShellExecute = false, CreateNoWindow = true };
                using (var process = Process.Start(start))
                {
                    if (!process.WaitForExit(30000)) Debug.LogWarning("GAMEHUB PUBLISH: 起動スクリプトが30秒以内に戻りませんでした。ログ: " + LogPath);
                    else if (process.ExitCode != 0) Debug.LogWarning("GAMEHUB PUBLISH: FAIL to start (exit " + process.ExitCode + "). ログ: " + LogPath);
                    else Debug.Log("GAMEHUB PUBLISH: STARTED（Unity の終了後に裏で公開します）ログ: " + LogPath);
                }
            }
            catch (Exception e) { Debug.LogWarning("GAMEHUB PUBLISH: FAIL to start: " + e.Message); }
        }
    }
}
#endif
