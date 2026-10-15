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
        // URP のシェーダーは Always Included にすると全バリアントが入ってビルドが極端に遅くなるため、
        // 使う組み合わせ（不透明・半透明・発光）のマテリアルを Resources に置いて含める（IncludeUrpShaderRefs）。
        private static readonly string[] RequiredShaders = { };
        private static readonly string[] OptionalShaders = { "Unlit/Color", "Unlit/Transparent", "Sprites/Default", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended" };
        private const string UrpRefDir = "Assets/_Project/Resources/ShaderRefs";

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
            IncludeUrpShaderRefs();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// URP の Lit / Particles Unlit を、MaterialCache などが実行時に作るのと同じキーワードの組み合わせでマテリアル化して Resources に置く。
        /// （Resources 内のマテリアルが参照するシェーダーとそのバリアントはビルドに含まれる）
        /// </summary>
        public static void IncludeUrpShaderRefs()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return; // URP でなければ不要
            Directory.CreateDirectory(UrpRefDir);
            void Make(string name, Shader shader, Action<Material> setup)
            {
                if (shader == null) return;
                var path = UrpRefDir + "/" + name + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
                var m = new Material(shader) { name = name };
                setup?.Invoke(m);
                AssetDatabase.CreateAsset(m, path);
            }
            Make("UrpLit_Opaque", lit, null);
            Make("UrpLit_Emissive", lit, m => m.EnableKeyword("_EMISSION"));
            // MaterialCache.MakeTransparent（URP）と同じ設定
            Make("UrpLit_Transparent", lit, m =>
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            });
            Make("UrpParticlesUnlit", Shader.Find("Universal Render Pipeline/Particles/Unlit"), null);
        }
    }
}
#endif
