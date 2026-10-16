using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイクの3Dプレビュー。ワールドから遠く離れた位置に小さな舞台と専用カメラを置き、
    /// RenderTexture に描いて IMGUI で表示する（回転・顔のアップ）。閉じたら必ず Dispose する。
    /// </summary>
    public sealed class AvatarPreviewStage
    {
        // 星のワールド・タイトル背景と重ならない遠い場所【仮】
        private static readonly Vector3 StagePosition = new Vector3(-9000f, 9000f, -9000f);

        public RenderTexture Texture { get; private set; }
        public float Yaw = 200f;
        public bool CloseUp;
        public bool AutoRotate = true;

        private GameObject root;
        private GameObject avatarHolder;
        private Camera cam;
        private float camBlend;

        public AvatarPreviewStage(int width = 640, int height = 900)
        {
            root = new GameObject("StoryAvatarPreview");
            Object.DontDestroyOnLoad(root);
            root.transform.position = StagePosition;

            // 床（丸い台）と背景
            PrimitiveBuilder.Create(PrimitiveType.Cylinder, root.transform, new Vector3(0, -0.05f, 0), new Vector3(2.4f, 0.05f, 2.4f), new Color(0.22f, 0.26f, 0.42f), "Pedestal", false);
            PrimitiveBuilder.Create(PrimitiveType.Cylinder, root.transform, new Vector3(0, -0.02f, 0), new Vector3(2.0f, 0.05f, 2.0f), new Color(0.35f, 0.42f, 0.66f), "PedestalTop", false);
            for (int i = 0; i < 24; i++)
            {
                float a = i / 24f * Mathf.PI * 2f;
                var p = new Vector3(Mathf.Cos(a) * 9f, 0.5f + (i % 5) * 0.9f, Mathf.Sin(a) * 9f);
                var star = PrimitiveBuilder.Create(PrimitiveType.Sphere, root.transform, p, Vector3.one * (0.08f + (i % 3) * 0.05f), Color.white, "Star", false);
                var r = star.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(new Color(0.9f, 0.95f, 1f));
            }

            var lightGo = new GameObject("PreviewLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(1.5f, 3.2f, 2.5f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 12f;
            light.intensity = 1.6f;
            light.color = new Color(1f, 0.96f, 0.9f);

            var fillGo = new GameObject("PreviewFill");
            fillGo.transform.SetParent(root.transform, false);
            fillGo.transform.localPosition = new Vector3(-2f, 1.5f, -1.5f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Point;
            fill.range = 10f;
            fill.intensity = 0.8f;
            fill.color = new Color(0.6f, 0.75f, 1f);

            Texture = new RenderTexture(width, height, 16) { name = "AvatarPreviewRT", antiAliasing = 2 };
            Texture.Create();

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(root.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.08f, 0.16f, 1f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 40f;
            cam.targetTexture = Texture;
            cam.depth = -50;
        }

        /// <summary>見た目を作り直す。</summary>
        public void SetAppearance(CharacterAppearance a)
        {
            if (root == null) return;
            if (avatarHolder != null) Object.Destroy(avatarHolder);
            avatarHolder = new GameObject("AvatarHolder");
            avatarHolder.transform.SetParent(root.transform, false);
            GameObject visual = null;
            try { visual = PlayerAvatarFactory.BuildVisual(a, avatarHolder.transform); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (visual == null) visual = AvatarBuilder.Build(a, avatarHolder.transform);
            visual.name = "Visual";
        }

        /// <summary>毎フレーム（IMGUI の描画前）呼ぶ：回転とカメラの寄り。</summary>
        public void Update(float dt)
        {
            if (root == null || cam == null) return;
            if (AutoRotate) Yaw += dt * 25f;
            if (avatarHolder != null) avatarHolder.transform.localRotation = Quaternion.Euler(0, Yaw, 0);
            camBlend = Mathf.MoveTowards(camBlend, CloseUp ? 1f : 0f, dt * 3f);
            // NOKA の頭身のキット（AvatarBuilder.KitScale）：頭の中心が約1.2m、帽子を入れて約1.8m
            var far = new Vector3(0, 1.0f, 5.0f);
            var near = new Vector3(0, 1.32f, 2.3f);
            var lookFar = new Vector3(0, 0.88f, 0);
            var lookNear = new Vector3(0, 1.22f, 0);
            cam.transform.localPosition = Vector3.Lerp(far, near, camBlend);
            cam.transform.LookAt(root.transform.TransformPoint(Vector3.Lerp(lookFar, lookNear, camBlend)));
        }

        public void Dispose()
        {
            if (cam != null) cam.targetTexture = null;
            if (Texture != null)
            {
                Texture.Release();
                Object.Destroy(Texture);
                Texture = null;
            }
            if (root != null) Object.Destroy(root);
            root = null;
            cam = null;
        }
    }
}
