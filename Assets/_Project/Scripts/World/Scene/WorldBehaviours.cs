using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 夜に灯る物（街灯・家の窓・広場の木の実など）。WorldSystem が毎フレーム LampsOn を決め、
    /// 状態が変わった時だけ切り替える。
    /// </summary>
    public class NightLight : MonoBehaviour
    {
        public static bool LampsOn;
        public static float GlowLevel = 1f;

        public Light lightSource;
        public List<Renderer> glowRenderers = new List<Renderer>();
        public Color onColor = new Color(1f, 0.86f, 0.55f);
        public Color offColor = new Color(0.55f, 0.55f, 0.5f);
        /// <summary>false なら常に消灯（空き家の窓など）。</summary>
        public bool enabledByOwner = true;
        private bool? current;

        public static NightLight Attach(GameObject go, Light light, Color on, Color off, params Renderer[] renderers)
        {
            var nl = go.AddComponent<NightLight>();
            nl.lightSource = light;
            nl.onColor = on;
            nl.offColor = off;
            if (renderers != null) nl.glowRenderers.AddRange(renderers);
            nl.Apply(false);
            return nl;
        }

        private void Update()
        {
            bool want = LampsOn && enabledByOwner;
            if (current.HasValue && current.Value == want) return;
            Apply(want);
        }

        private void Apply(bool on)
        {
            current = on;
            if (lightSource != null) lightSource.enabled = on;
            var mat = on ? MaterialCache.GetEmissive(onColor) : MaterialCache.Get(offColor);
            foreach (var r in glowRenderers) if (r != null) r.sharedMaterial = mat;
        }
    }

    /// <summary>
    /// 宇宙語の看板。文字は "PLACE:"+facilityId を Language モジュールが宇宙語にしたもの（翻訳しない）。
    /// 調べると、看板の文字を地図に記録できる（プレイヤーが見たものだけ：村仕様 §13.2）。
    /// </summary>
    public class SignBoard : MonoBehaviour, IInteractable
    {
        public string signId;
        public string facilityId;
        public bool isSignpost;
        public Action<SignBoard, PlayerState> onRead;

        public string GetInteractionPrompt(PlayerState player) => Loc.T("看板を見る", "Read the sign");
        public bool CanInteract(PlayerState player) => true;
        public void Interact(PlayerState player) => onRead?.Invoke(this, player);
    }

    /// <summary>公共施設の整備予定地の目印（調べると材料・お金を寄付できる）。</summary>
    public class ProjectMarker : MonoBehaviour, IInteractable
    {
        public string projectId;
        public Action<ProjectMarker, PlayerState> onInteract;

        public string GetInteractionPrompt(PlayerState player) => Loc.T("工事中の場所を見る", "Look at the work site");
        public bool CanInteract(PlayerState player) => true;
        public void Interact(PlayerState player) => onInteract?.Invoke(this, player);
    }

    /// <summary>ゆっくり揺れる・回る小物（風見・のぼり・残骸の煙など）。</summary>
    public class GentleMotion : MonoBehaviour
    {
        public Vector3 spinAxis = Vector3.up;
        public float spinSpeed;
        public float bobAmplitude;
        public float bobSpeed = 1f;
        private Vector3 basePos;
        private float phase;

        private void Start()
        {
            basePos = transform.localPosition;
            phase = (transform.position.x * 0.37f + transform.position.z * 0.11f) % 6.28f;
        }

        private void Update()
        {
            float t = UnityEngine.Time.time;
            if (spinSpeed != 0f) transform.Rotate(spinAxis, spinSpeed * UnityEngine.Time.deltaTime, Space.Self);
            if (bobAmplitude != 0f) transform.localPosition = basePos + Vector3.up * Mathf.Sin(t * bobSpeed + phase) * bobAmplitude;
        }
    }
}
