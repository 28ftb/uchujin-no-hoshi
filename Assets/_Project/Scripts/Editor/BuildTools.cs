#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// Windows ビルド。成功すると Builds/Windows をゲームランチャーへ公開する（GameHubPublish）。
    /// - メニュー：宇宙人の星 → Windows ビルド（ランチャーへ公開）
    /// - バッチ：Tools/Build-Windows.ps1（-executeMethod Uchujin.EditorTools.BuildTools.BuildWindows）
    /// - 検証用のビルドは環境変数 GAMEHUB_SKIP_PUBLISH=1 を付ける（公開しない）。
    /// </summary>
    public static class BuildTools
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        public const string OutputDir = "Builds/Windows";
        public const string ExeName = "UchujinNoHoshi.exe";

        // マテリアルはコードで作る（MaterialCache ほか）ので、Shader.Find で探すシェーダーがビルドで消えないようにする。
        private static readonly string[] RequiredShaders = { "Standard" };
        private static readonly string[] OptionalShaders = { "Unlit/Color", "Unlit/Transparent", "Sprites/Default", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended" };

        [MenuItem("宇宙人の星/Windows ビルド（ランチャーへ公開）", priority = 60)]
        public static void BuildWindows()
        {
            if (!File.Exists(ScenePath)) throw new Exception("メインシーンがありません: " + ScenePath + "（メニュー「宇宙人の星 → メインシーンを作成して開く」を先に実行）");
            PlayerSettings.companyName = "UchujinNoHoshi";
            PlayerSettings.productName = "宇宙人の星";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // マルチプレイ中にウィンドウを切り替えても止めない
            IncludeShaders();

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            var line = summary.result + " | errors=" + summary.totalErrors + " | warnings=" + summary.totalWarnings + " | bytes=" + summary.totalSize;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/build-results.txt", line);
            if (summary.result != BuildResult.Succeeded) throw new Exception("Windows ビルド失敗: " + line);
            Debug.Log("UCHUJIN_BUILD_OK " + line);
            GameHubPublish.AfterBuild(OutputDir);
        }

        private static void IncludeShaders()
        {
            var graphics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var list = graphics.FindProperty("m_AlwaysIncludedShaders");
            void Add(string name, bool required)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    if (required) throw new Exception("シェーダーが見つかりません: " + name);
                    Debug.LogWarning("シェーダーが見つからないので含めません: " + name);
                    return;
                }
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
                int index = list.arraySize;
                list.InsertArrayElementAtIndex(index);
                list.GetArrayElementAtIndex(index).objectReferenceValue = shader;
            }
            foreach (var name in RequiredShaders) Add(name, true);
            foreach (var name in OptionalShaders) Add(name, false);
            graphics.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
