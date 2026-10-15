#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// URP（Universal Render Pipeline）の設定アセットを作り、プロジェクトに割り当てる（1回だけ実行すればよい）。
    /// メニュー「宇宙人の星 → URP を設定する」、またはバッチモードで
    /// -executeMethod Uchujin.EditorTools.UrpProjectSetup.Setup
    /// - Assets/_Project/Settings/URP_Asset.asset と URP_Renderer.asset（SSAO 付き）を作成
    /// - Graphics Settings と全品質レベルに割り当て
    /// - 色空間を Linear に
    /// - Scripting Define Symbols に UCHUJIN_URP を追加（実行時の後処理コードを有効にする）
    /// </summary>
    public static class UrpProjectSetup
    {
        private const string Dir = "Assets/_Project/Settings";
        private const string AssetPath = Dir + "/URP_Asset.asset";
        private const string RendererPath = Dir + "/URP_Renderer.asset";
        private const string PostProcessDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
        private const string Define = "UCHUJIN_URP";

        [MenuItem("宇宙人の星/URP を設定する", priority = 60)]
        public static void Setup()
        {
            Directory.CreateDirectory(Dir);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostProcessDataPath);
                AssetDatabase.CreateAsset(renderer, RendererPath);
                // 接地感を出す環境光の遮蔽（丸い住民・生物の足元が浮いて見えないように）
                var ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "SSAO";
                AssetDatabase.AddObjectToAsset(ssao, renderer);
                renderer.rendererFeatures.Add(ssao);
                EditorUtility.SetDirty(renderer);
                Debug.Log("[URP] レンダラーを作成: " + RendererPath);
            }

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                asset.supportsHDR = true;
                asset.msaaSampleCount = 4;
                asset.shadowDistance = 70f;
                asset.shadowCascadeCount = 2;
                AssetDatabase.CreateAsset(asset, AssetPath);
                Debug.Log("[URP] パイプラインアセットを作成: " + AssetPath);
            }
            AssetDatabase.SaveAssets();

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.colorSpace = ColorSpace.Linear;

            var target = NamedBuildTarget.Standalone;
            var defines = PlayerSettings.GetScriptingDefineSymbols(target);
            if (!(";" + defines + ";").Contains(";" + Define + ";"))
                PlayerSettings.SetScriptingDefineSymbols(target, string.IsNullOrEmpty(defines) ? Define : defines + ";" + Define);

            AssetDatabase.SaveAssets();
            Debug.Log("[URP] 設定完了：Graphics/Quality に割り当て、色空間 Linear、定義 " + Define);
        }
    }
}
#endif
