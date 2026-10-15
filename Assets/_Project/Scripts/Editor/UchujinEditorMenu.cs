#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// 開発用のエディタメニュー（メニューバー「宇宙人の星」）。
    /// ※ このファイルは Unity Editor 専用。クラウド環境の型チェック対象外のため、Unity 上でコンパイル確認が必要。
    /// </summary>
    public static class UchujinEditorMenu
    {
        private const string ScenePath = "Assets/_Project/Scenes/Main.unity";

        [MenuItem("宇宙人の星/メインシーンを作成して開く", priority = 1)]
        public static void CreateMainScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Game オブジェクトは GameBootstrap が再生時に自動生成するため、シーンは空でよい。
            // 明示的に置きたい場合は下の行を有効にする。
            // new GameObject("Game").AddComponent<Uchujin.Core.Game>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!list.Exists(s => s.path == ScenePath)) list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("メインシーンを作成しました: " + ScenePath + "（再生するとタイトル画面が始まります）");
        }

        [MenuItem("宇宙人の星/セーブフォルダを開く", priority = 20)]
        public static void OpenSaveFolder()
        {
            var path = Path.Combine(Application.persistentDataPath, "Saves");
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("宇宙人の星/すべてのセーブを削除", priority = 21)]
        public static void DeleteAllSaves()
        {
            var path = Path.Combine(Application.persistentDataPath, "Saves");
            if (!Directory.Exists(path)) return;
            if (!EditorUtility.DisplayDialog("セーブ削除", "すべてのセーブデータを削除します。よろしいですか？", "削除", "やめる")) return;
            Directory.Delete(path, true);
            Debug.Log("セーブデータを削除しました: " + path);
        }

        [MenuItem("宇宙人の星/自動起動を切り替える（空シーン再生時）", priority = 40)]
        public static void ToggleAutoBoot()
        {
            int v = PlayerPrefs.GetInt("uchujin.disableAutoBoot", 0) == 1 ? 0 : 1;
            PlayerPrefs.SetInt("uchujin.disableAutoBoot", v);
            PlayerPrefs.Save();
            Debug.Log(v == 1 ? "自動起動を無効にしました" : "自動起動を有効にしました");
        }

        [MenuItem("宇宙人の星/ローカルプレイヤーIDをリセット（マルチの2起動テスト用）", priority = 41)]
        public static void ResetLocalPlayerId()
        {
            PlayerPrefs.DeleteKey("uchujin.localPlayerId");
            PlayerPrefs.Save();
            Debug.Log("ローカルプレイヤーIDをリセットしました");
        }
    }
}
#endif
