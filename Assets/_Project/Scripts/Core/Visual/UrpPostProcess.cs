#if UCHUJIN_URP
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Uchujin.Core.Visual
{
    /// <summary>後処理の調整値【仮数値】。</summary>
    public static class PostFxTuning
    {
        public const float BloomThreshold = 0.95f;
        public const float BloomIntensity = 0.35f;
        public const float BloomScatter = 0.65f;
        public const float PostExposure = 0f;
        public const float Contrast = 8f;
        public const float Saturation = 10f;
        public const float VignetteIntensity = 0.16f;
    }

    /// <summary>
    /// URP の後処理（トーンマップ・ブルーム・色調整・周辺減光）を、シーンやアセットを用意せずにコードで設定する。
    /// URP を使う時だけ有効（Scripting Define Symbols の UCHUJIN_URP。Editor の「宇宙人の星 → URP を設定する」で追加される）。
    /// コード主体の構成のため、実行時に作られるカメラにも描画直前に後処理を有効にする。
    /// </summary>
    public static class UrpPostProcess
    {
        private static Volume volume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            RenderPipelineManager.beginContextRendering -= OnBeginFrame;
            RenderPipelineManager.beginContextRendering += OnBeginFrame;
            EnsureVolume();
        }

        private static void OnBeginFrame(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
        {
            if (Application.isPlaying) UrpLightCompat.Update();
        }

        private static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam.cameraType != CameraType.Game) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
            if (volume == null) EnsureVolume();
        }

        private static void EnsureVolume()
        {
            if (volume != null) return;
            var go = new GameObject("PostProcessVolume");
            Object.DontDestroyOnLoad(go);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "UchujinPostFx";
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(PostFxTuning.BloomThreshold);
            bloom.intensity.Override(PostFxTuning.BloomIntensity);
            bloom.scatter.Override(PostFxTuning.BloomScatter);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(PostFxTuning.PostExposure);
            color.contrast.Override(PostFxTuning.Contrast);
            color.saturation.Override(PostFxTuning.Saturation);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(PostFxTuning.VignetteIntensity);
            volume.sharedProfile = profile;
        }
    }
}
#endif
