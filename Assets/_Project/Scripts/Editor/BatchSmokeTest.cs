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
    /// - バッチモード：Unity.exe -batchmode -projectPath ... -executeMethod Uchujin.EditorTools.BatchSmokeTest.Run（-quit は付けない。URP では -nographics を付けると RenderTexture が作れずエラーになる。通常は Tools/Unity-Check.ps1 -Mode Smoke）
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

        /// <summary>村の外のマップのゲートに入り、移った先を記録して引いた斜めから撮る（3段：入る・記録・撮影）。</summary>
        private static void TourGate(int step, float t, string gateName, string shot)
        {
            var g = Game.Instance;
            if (At(step, t))
            {
                if (CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                var gate = GameObject.Find(gateName);
                if (gate != null) g.LocalPlayer.Position = gate.transform.position + Vector3.up * 0.3f;
                else Log("ゲートが見つからない：" + gateName);
            }
            if (At(step + 1, t + 2.5f))
            {
                var wild = GameObject.Find("WildCreatures");
                Log(gateName + " に入った後のエリア：" + g.LocalPlayer.CurrentAreaId + "・野生の生き物：" + (wild != null ? wild.transform.childCount : -1) + "匹");
                if (CameraRig.Instance != null) { CameraRig.Instance.pitch = 30f; CameraRig.Instance.distance = 50f; }
            }
            if (At(step + 2, t + 5f) && shot != null) Shot(shot);
        }

        private static void Update()
        {
            float limit = Stage == 2 ? 400f : Stage == 3 ? 200f : 120f; // プロローグは操作区間の自動進行を待つので長め。自由行動は村の外のマップを回るので長め
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
                        // 住民の家（入居済みの区画）の前。家のモデル（Resources/Art/Houses）の見た目の確認用
                        var home = g.Layout?.GetFacilities(Uchujin.World.FacilityType.ResidentHouse).FirstOrDefault(f => !string.IsNullOrEmpty(f.ownerResidentId));
                        if (home != null) g.LocalPlayer.Position = home.entrance + Quaternion.Euler(0f, home.rotationY, 0f) * new Vector3(1.5f, 0.5f, 3f);
                        else Log("入居済みの家が見つからない");
                    }
                    if (At(2, 7f)) Shot("04b_house");
                    if (At(3, 8f))
                    {
                        var rocket = g.Service<IRocketService>();
                        if (rocket != null) g.LocalPlayer.Position = rocket.RocketPosition + new Vector3(6f, 0.5f, 6f);
                    }
                    if (At(4, 11f)) Shot("05_rocket");
                    // 生成バージョン3：村の端のゲート → 花の野道 → 湖の森（ゲートの範囲に入ると移るか）
                    if (At(5, 12f))
                    {
                        var gate = GameObject.Find("Gate_gate.r1.t1");
                        if (gate != null)
                        {
                            g.LocalPlayer.Position = gate.transform.position - gate.transform.forward * 9f + Vector3.up * 0.3f;
                            g.LocalPlayer.Avatar.rotation = Quaternion.LookRotation(gate.transform.forward);
                        }
                        else Log("村のゲートが見つからない");
                    }
                    if (At(6, 14.5f)) Shot("04c_village_gate");
                    if (At(7, 15f))
                    {
                        var gate = GameObject.Find("Gate_gate.r1.t1");
                        if (gate != null) g.LocalPlayer.Position = gate.transform.position + Vector3.up * 0.3f;
                    }
                    if (At(8, 17f))
                    {
                        Log("村のゲートに入った後のエリア：" + g.LocalPlayer.CurrentAreaId);
                        // 起伏が分かるように、引いて斜めから撮る
                        if (CameraRig.Instance != null) { CameraRig.Instance.pitch = 30f; CameraRig.Instance.distance = 46f; }
                    }
                    if (At(9, 24f))
                    {
                        Shot("04d_t1");
                        var wild = GameObject.Find("WildCreatures");
                        Log("花の野道の野生の生き物：" + (wild != null ? wild.transform.childCount : -1) + "匹（見えている数）・小川の釣り場の魚影：" + (GameObject.Find("FishSpot_spot.stream.t1.0") != null ? "あり" : "なし"));
                    }
                    if (At(10, 24.5f))
                    {
                        if (CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                        var gate = GameObject.Find("Gate_gate.t1.r2");
                        if (gate != null) g.LocalPlayer.Position = gate.transform.position + Vector3.up * 0.3f;
                        else Log("花の野道の湖の森側のゲートが見つからない");
                    }
                    if (At(11, 27f))
                    {
                        Log("花の野道の先のゲートに入った後のエリア：" + g.LocalPlayer.CurrentAreaId);
                        if (CameraRig.Instance != null) { CameraRig.Instance.pitch = 30f; CameraRig.Instance.distance = 50f; }
                    }
                    if (At(12, 29f)) Shot("04e_r2");
                    if (At(13, 29.5f))
                    {
                        if (CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                        var pier = GameObject.Find("LakePier");
                        if (pier != null)
                        {
                            g.LocalPlayer.Position = pier.transform.position + pier.transform.forward * 6f + Vector3.up * 0.5f;
                            g.LocalPlayer.Avatar.rotation = Quaternion.LookRotation(pier.transform.forward);
                        }
                    }
                    if (At(14, 35f))
                    {
                        Shot("04f_r2_lake");
                        var wild = GameObject.Find("WildCreatures");
                        var lp = g.LocalPlayer.Position;
                        var area = g.Layout != null ? g.Layout.GetAreaAt(lp) : null;
                        Log("湖の森の野生の生き物：" + (wild != null ? wild.transform.childCount : -1) + "匹・岬の先の釣り場の魚影：" + (GameObject.Find("FishSpot_spot.r2.lake.tip") != null ? "あり" : "なし")
                            + "・今いるエリア：" + (area != null ? area.nameJa : "なし") + "・足元の水深：" + (g.Layout != null ? g.Layout.WaterDepthAt(lp + g.LocalPlayer.Avatar.forward * 4f).ToString("0.0") : "?") + "m");
                    }
                    if (At(15, 35.5f) && g.LocalPlayer.CurrentAreaId != AreaIds.Overworld) g.Areas.Transition(g.LocalPlayer, AreaIds.Overworld);
                    // 村 → 渓流の坂道 → 滝の山
                    if (At(16, 37f))
                    {
                        var gate = GameObject.Find("Gate_gate.r1.t3");
                        if (gate != null) g.LocalPlayer.Position = gate.transform.position - gate.transform.forward * 9f + Vector3.up * 0.3f;
                        else Log("村の渓流の坂道のゲートが見つからない");
                    }
                    if (At(17, 39f))
                    {
                        var gate = GameObject.Find("Gate_gate.r1.t3");
                        if (gate != null) g.LocalPlayer.Position = gate.transform.position + Vector3.up * 0.3f;
                    }
                    if (At(18, 41.5f))
                    {
                        Log("渓流の坂道のゲートに入った後のエリア：" + g.LocalPlayer.CurrentAreaId);
                        if (CameraRig.Instance != null) { CameraRig.Instance.pitch = 30f; CameraRig.Instance.distance = 46f; }
                    }
                    if (At(19, 44f)) Shot("04g_t3");
                    if (At(20, 44.5f))
                    {
                        var gate = GameObject.Find("Gate_gate.t3.r3");
                        if (gate != null) g.LocalPlayer.Position = gate.transform.position + Vector3.up * 0.3f;
                        else Log("渓流の坂道の滝の山側のゲートが見つからない");
                    }
                    if (At(21, 47f)) Log("渓流の坂道の先のゲートに入った後のエリア：" + g.LocalPlayer.CurrentAreaId);
                    if (At(22, 50f)) Shot("04h_r3");
                    if (At(23, 50.5f))
                    {
                        var fall = GameObject.Find("FieldWaterfall");
                        if (fall != null)
                        {
                            var near = fall.transform.position + fall.transform.forward * 14f + fall.transform.right * 12f;
                            g.LocalPlayer.Position = g.Layout.OnGround(near) + Vector3.up * 0.5f;
                            var flat = fall.transform.position - g.LocalPlayer.Position;
                            flat.y = 0f;
                            if (flat.sqrMagnitude > 0.1f) g.LocalPlayer.Avatar.rotation = Quaternion.LookRotation(flat);
                        }
                        else Log("滝が見つからない");
                    }
                    if (At(24, 54f)) Shot("04i_r3_falls");
                    // 残りのマップ：滝の山 → 尾根道 → 星見の高原、村 → 川下りの湿原 → 浜と岬 → T6（海風の林か崖の小道。シードで1本）→ 苔の谷
                    if (At(25, 54.5f) && CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                    TourGate(26, 55f, "Gate_gate.r3.t5", "04j_t5");
                    TourGate(29, 62f, "Gate_gate.t5.r5", "04k_r5");
                    if (At(32, 69f))
                    {
                        if (CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                        if (g.LocalPlayer.CurrentAreaId != AreaIds.Overworld) g.Areas.Transition(g.LocalPlayer, AreaIds.Overworld);
                    }
                    TourGate(33, 70.5f, "Gate_gate.r1.t2", "04l_t2");
                    TourGate(36, 77.5f, "Gate_gate.t2.r4", "04m_r4");
                    bool t6a = GameObject.Find("Gate_gate.r4.t6a") != null;
                    TourGate(39, 84.5f, t6a ? "Gate_gate.r4.t6a" : "Gate_gate.r4.t6b", "04n_t6");
                    TourGate(42, 91.5f, t6a ? "Gate_gate.t6a.r2" : "Gate_gate.t6b.r3", null);
                    TourGate(45, 98.5f, t6a ? "Gate_gate.r2.t4" : "Gate_gate.r3.t4", "04o_t4");
                    if (At(48, 105.5f))
                    {
                        if (CameraRig.Instance != null) CameraRig.Instance.ApplyModeDefaults();
                        if (g.LocalPlayer.CurrentAreaId != AreaIds.Overworld) g.Areas.Transition(g.LocalPlayer, AreaIds.Overworld);
                    }
                    if (At(49, 107f)) Stage = 4;
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
