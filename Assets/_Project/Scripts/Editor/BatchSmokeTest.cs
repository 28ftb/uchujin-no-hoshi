#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// バッチモード用の起動スモークテスト（一時的な検証用）。
    /// 再生 → タイトル → 新しい星 → しばらく進行 → 睡眠 → セーブ → タイトル → ロード → 終了 を自動で行い、
    /// 例外・エラーログを集計して終了コードで返す。
    /// 使い方: Unity.exe -batchmode -nographics -projectPath ... -executeMethod Uchujin.EditorTools.BatchSmokeTest.Run
    /// </summary>
    [InitializeOnLoad]
    public static class BatchSmokeTest
    {
        private const string ActiveKey = "uchujin.smoke.active";
        private const string StageKey = "uchujin.smoke.stage";
        private const string StageStartKey = "uchujin.smoke.stageStart";
        private const string SlotKey = "uchujin.smoke.slot";
        private const string ErrorsKey = "uchujin.smoke.errors";

        static BatchSmokeTest()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Update;
        }

        public static void Run()
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetString(ErrorsKey, "");
            PlayerPrefs.SetInt("uchujin.disableAutoBoot", 0);
            Log("開始：再生モードに入ります");
            SessionState.SetFloat(StageStartKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        private static void Log(string msg) => Debug.Log("[SMOKE] " + msg);

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            if (condition.StartsWith("[SMOKE]")) return;
            var first = condition.Split('\n')[0];
            var errs = SessionState.GetString(ErrorsKey, "");
            SessionState.SetString(ErrorsKey, errs + type + ": " + first + "\n    at " + FirstFrame(stackTrace, condition) + "\n");
        }

        private static string FirstFrame(string stackTrace, string condition)
        {
            var src = string.IsNullOrEmpty(stackTrace) ? condition : stackTrace;
            foreach (var line in src.Split('\n'))
                if (line.Contains("Uchujin.")) return line.Trim();
            return "(不明)";
        }

        private static int Stage { get => SessionState.GetInt(StageKey, 0); set { SessionState.SetInt(StageKey, value); SessionState.SetFloat(StageStartKey, (float)EditorApplication.timeSinceStartup); } }
        private static float Elapsed => (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StageStartKey, 0f);

        private static void Update()
        {
            if (Elapsed > 180f) { Finish("タイムアウト（段階 " + Stage + "）", 2); return; }
            if (!EditorApplication.isPlaying) return;
            var g = Game.Instance;
            switch (Stage)
            {
                case 0:
                    if (g != null && g.State == GameState.Title && !g.IsBusy)
                    {
                        Log("タイトル表示を確認。新しい星を開始します");
                        g.StartNewPlanet(new NewGameSettings { SeedText = "smoke-test", PlanetNickname = "スモーク" });
                        Stage = 1;
                    }
                    break;
                case 1:
                    if (g != null && !g.IsBusy && g.Planet != null && Elapsed > 2f)
                    {
                        Log("新しい星の準備完了：State=" + g.State + " 日=" + g.Time?.Day + " 住民/システムの起動まで完了");
                        Time.timeScale = 20f; // 時間を進めて毎時・日付更新を通す
                        Stage = 2;
                    }
                    break;
                case 2:
                    if (Elapsed > 15f)
                    {
                        Time.timeScale = 1f;
                        Log("進行後：State=" + g.State + " 日=" + g.Time?.Day);
                        if (g.LocalPlayer != null) g.StartCoroutine(g.SleepRoutine(g.LocalPlayer, 6));
                        Stage = 3;
                    }
                    break;
                case 3:
                    if (!g.IsSleeping && Elapsed > 3f)
                    {
                        Log("睡眠後：日=" + g.Time?.Day);
                        bool ok = g.SaveGame(false);
                        SessionState.SetString(SlotKey, g.CurrentSlotId ?? "");
                        Log("セーブ：" + (ok ? "成功" : "失敗") + " スロット=" + g.CurrentSlotId);
                        g.ReturnToTitle(false);
                        Stage = 4;
                    }
                    break;
                case 4:
                    if (g.State == GameState.Title && !g.IsBusy && Elapsed > 1f)
                    {
                        Log("タイトルへ戻った。ロードします");
                        g.LoadSlot(SessionState.GetString(SlotKey, ""));
                        Stage = 5;
                    }
                    break;
                case 5:
                    if (!g.IsBusy && g.Planet != null && Elapsed > 2f)
                    {
                        Log("ロード完了：State=" + g.State + " 日=" + g.Time?.Day);
                        Stage = 6;
                    }
                    break;
                case 6:
                    if (Elapsed > 5f)
                    {
                        var slot = SessionState.GetString(SlotKey, "");
                        g.ReturnToTitle(false);
                        if (!string.IsNullOrEmpty(slot)) g.Saves.DeleteSlot(slot);
                        Finish("完了", 0);
                    }
                    break;
            }
        }

        private static void Finish(string why, int code)
        {
            EditorApplication.update -= Update;
            Application.logMessageReceived -= OnLog;
            var errs = SessionState.GetString(ErrorsKey, "");
            var lines = errs.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            var uniq = new List<string>();
            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                var e = lines[i] + "\n" + lines[i + 1];
                if (!uniq.Contains(e)) uniq.Add(e);
            }
            var sb = new StringBuilder();
            sb.AppendLine("[SMOKE] 終了：" + why + " エラー/例外 " + (lines.Length / 2) + " 件（重複除く " + uniq.Count + " 件）");
            foreach (var e in uniq) sb.AppendLine("[SMOKE-ERR] " + e);
            Debug.Log(sb.ToString());
            SessionState.SetBool(ActiveKey, false);
            if (code == 0 && uniq.Count > 0) code = 1;
            EditorApplication.Exit(code);
        }
    }
}
#endif
