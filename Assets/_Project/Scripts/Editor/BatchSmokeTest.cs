#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// 起動スモークテスト（検証用）。
    /// 再生 → タイトル → キャラメイク → 新しい星（プロローグ）→ 自由行動 → 住民と会話 → 商店 → タブレット → 睡眠 → セーブ → ロード を自動で行い、
    /// エラー・例外を集計して終了コードで返す。作ったテスト用セーブは最後に削除する。
    /// - バッチモード：Unity.exe -batchmode -nographics -projectPath ... -executeMethod Uchujin.EditorTools.BatchSmokeTest.Run（-quit は付けない）
    /// - 画面あり（-batchmode なし）で起動すると、各段階の Game ビューを Logs/Smoke/*.png に保存する（見た目の確認用）。
    /// </summary>
    [InitializeOnLoad]
    public static class BatchSmokeTest
    {
        private const string ActiveKey = "uchujin.smoke.active";
        private const string StageKey = "uchujin.smoke.stage";
        private const string StageStartKey = "uchujin.smoke.stageStart";
        private const string SlotKey = "uchujin.smoke.slot";
        private const string ErrorsKey = "uchujin.smoke.errors";
        private const string StepKey = "uchujin.smoke.step";
        private const string ShotDir = "Logs/Smoke";

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
            SessionState.SetInt(StepKey, 0);
            SessionState.SetString(ErrorsKey, "");
            PlayerPrefs.SetInt("uchujin.disableAutoBoot", 0);
            if (!Application.isBatchMode)
            {
                if (Directory.Exists(ShotDir)) Directory.Delete(ShotDir, true);
                Directory.CreateDirectory(ShotDir);
            }
            Log("開始：再生モードに入ります（撮影 " + (Application.isBatchMode ? "なし" : "あり") + "）");
            SessionState.SetFloat(StageStartKey, (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        private static void Log(string msg) => Debug.Log("[SMOKE] " + msg);

        private static void Shot(string name)
        {
            if (Application.isBatchMode || Game.Instance == null) return;
            Game.Instance.StartCoroutine(ShotRoutine(name));
        }

        // そのフレームの描画（IMGUI を含む）を確実に撮るため、フレームの終わりに取り込む
        private static System.Collections.IEnumerator ShotRoutine(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
            Object.Destroy(tex);
            Log("撮影 " + name);
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log || type == LogType.Warning) return;
            if (condition.StartsWith("[SMOKE]")) return;
            if (stackTrace != null && stackTrace.Contains("UnityEditor.Search.")) return; // Editor 自身の検索インデックスの例外は対象外
            var first = condition.Split('\n')[0];
            var errs = SessionState.GetString(ErrorsKey, "");
            SessionState.SetString(ErrorsKey, errs + "[段階" + Stage + "] " + type + ": " + first + "\n    at " + FirstFrame(stackTrace, condition) + "\n");
        }

        private static string FirstFrame(string stackTrace, string condition)
        {
            var src = string.IsNullOrEmpty(stackTrace) ? condition : stackTrace;
            foreach (var line in src.Split('\n'))
                if (line.Contains("Uchujin.")) return line.Trim();
            return "(不明)";
        }

        private static int Stage { get => SessionState.GetInt(StageKey, 0); set { SessionState.SetInt(StageKey, value); SessionState.SetInt(StepKey, 0); SessionState.SetFloat(StageStartKey, (float)EditorApplication.timeSinceStartup); } }
        private static int Step { get => SessionState.GetInt(StepKey, 0); set => SessionState.SetInt(StepKey, value); }
        private static float Elapsed => (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StageStartKey, 0f);

        /// <summary>段階内の「t 秒たったら1回だけ」。</summary>
        private static bool At(int step, float t)
        {
            if (Step != step || Elapsed < t) return false;
            Step = step + 1;
            return true;
        }

        private static void Update()
        {
            float limit = Stage == 2 ? 400f : 120f; // プロローグは操作区間の自動進行を待つので長め
            if (Elapsed > limit) { Finish("タイムアウト（段階 " + Stage + "）", 2); return; }
            if (!EditorApplication.isPlaying) return;
            var g = Game.Instance;
            switch (Stage)
            {
                case 0: // タイトル
                    if (g != null && g.State == GameState.Title && !g.IsBusy)
                    {
                        if (At(0, 3f)) Shot("01_title");
                        if (At(1, 7f))
                        {
                            Log("タイトル表示を確認。キャラクターメイクを開きます");
                            UIManager.Open(new Uchujin.Story.CharacterMakeWindow(null, Uchujin.Story.CharacterMakeWindow.Mode.NewGame, _ => { }, null));
                            Stage = 1;
                        }
                    }
                    break;
                case 1: // キャラクターメイク
                    if (At(0, 3f)) Shot("02_charmake");
                    if (At(1, 6f))
                    {
                        UIManager.CloseAll();
                        Log("新しい星を開始します（プロローグあり）");
                        g.StartNewPlanet(new NewGameSettings { SeedText = "smoke-test", PlanetNickname = "スモーク" });
                        Stage = 2;
                    }
                    break;
                case 2: // プロローグ
                    if (At(0, 6f)) Shot("03_prologue_a");
                    if (At(1, 14f)) Shot("03_prologue_b");
                    if (At(2, 26f)) Shot("03_prologue_c");
                    if (At(3, 45f)) Shot("03_prologue_d");
                    if (At(4, 90f)) Shot("03_prologue_e");
                    // プロローグの操作区間も State は Playing になるので、物語側の完了フラグで判定する
                    if (Step >= 4 && g != null && !g.IsBusy && g.Planet != null && g.State == GameState.Playing
                        && (g.Service<IStoryService>()?.IsPrologueDone ?? true))
                    {
                        Log("プロローグ終了：State=" + g.State + " 日=" + g.Time?.Day + "（" + Mathf.RoundToInt(Elapsed) + "秒）");
                        Stage = 3;
                    }
                    break;
                case 3: // 自由行動
                    if (At(0, 3f)) Shot("04_village");
                    if (At(1, 4f))
                    {
                        var rocket = g.Service<IRocketService>();
                        if (rocket != null) g.LocalPlayer.Position = rocket.RocketPosition + new Vector3(6f, 0.5f, 6f);
                    }
                    if (At(2, 7f)) Shot("05_rocket");
                    if (At(3, 8f)) Stage = 4;
                    break;
                case 4: // 住民と会話
                {
                    var dir = g.Service<IResidentDirectory>();
                    var r = dir?.ActiveResidents?.FirstOrDefault();
                    if (r == null) { Log("住民が見つからない"); Stage = 5; break; }
                    if (At(0, 0f)) g.LocalPlayer.Position = r.Position + new Vector3(1.8f, 0.3f, 0f);
                    if (At(1, 3f)) Shot("06_resident");
                    if (At(2, 3.5f)) dir.OpenConversation(r.Id, g.LocalPlayer);
                    if (At(3, 6f)) Shot("07_conversation");
                    if (At(4, 7f)) { UIManager.CloseAll(); Stage = 5; }
                    break;
                }
                case 5: // 商店・タブレット
                {
                    var shop = g.Service<IShopService>();
                    if (At(0, 0f) && shop != null) g.LocalPlayer.Position = shop.EntrancePosition + new Vector3(0f, 0.3f, 0f);
                    if (At(1, 3f)) Shot("08_shop");
                    if (At(2, 3.5f)) g.Service<ITabletService>()?.Open();
                    if (At(3, 5.5f)) Shot("09_tablet");
                    if (At(4, 6.5f)) { g.Service<ITabletService>()?.Close(); UIManager.CloseAll(); Stage = 6; }
                    break;
                }
                case 6: // 睡眠
                {
                    var home = g.Service<IHousingService>();
                    if (At(0, 0f) && home != null) g.LocalPlayer.Position = home.PlayerHomeEntrance + new Vector3(0f, 0.3f, 0f);
                    if (At(1, 3f)) Shot("10_home");
                    // 屋内・鉱山（点光源の照明）も撮る
                    if (At(2, 3.5f)) g.Areas.Transition(g.LocalPlayer, AreaIds.PlayerHouse);
                    if (At(3, 6.5f)) Shot("10b_house_inside");
                    if (At(4, 7f) && g.Areas.Get(AreaIds.MineFloor(1)) != null) g.Areas.Transition(g.LocalPlayer, AreaIds.MineFloor(1));
                    if (At(5, 10f)) Shot("10c_mine");
                    if (At(6, 10.5f)) g.Areas.Transition(g.LocalPlayer, AreaIds.PlayerHouse);
                    if (At(7, 12f)) { Log("睡眠前：日=" + g.Time?.Day); g.StartCoroutine(g.SleepRoutine(g.LocalPlayer, 6)); }
                    if (Step >= 8 && !g.IsSleeping && Elapsed > 15f)
                    {
                        Log("睡眠後：日=" + g.Time?.Day);
                        Stage = 7;
                    }
                    break;
                }
                case 7: // セーブ → タイトル
                    if (At(0, 3f)) Shot("11_morning");
                    if (At(1, 3.5f))
                    {
                        bool ok = g.SaveGame(true);
                        SessionState.SetString(SlotKey, g.CurrentSlotId ?? "");
                        Log("セーブ：" + (ok ? "成功" : "失敗") + " スロット=" + g.CurrentSlotId);
                    }
                    if (At(2, 4.5f)) Shot("12_saved");
                    if (At(3, 5.5f)) { g.ReturnToTitle(false); Stage = 8; }
                    break;
                case 8: // ロード
                    if (Step == 0 && g.State == GameState.Title && !g.IsBusy && Elapsed > 1f)
                    {
                        Log("タイトルへ戻った。ロードします");
                        g.LoadSlot(SessionState.GetString(SlotKey, ""));
                        Step = 1;
                    }
                    if (Step == 1 && !g.IsBusy && g.Planet != null && g.State == GameState.Playing)
                    {
                        Log("ロード完了：State=" + g.State + " 日=" + g.Time?.Day);
                        Stage = 9;
                    }
                    break;
                case 9:
                    if (At(0, 2f)) Shot("13_loaded");
                    if (At(1, 4f))
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
