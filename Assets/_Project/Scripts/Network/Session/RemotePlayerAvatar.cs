using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Network
{
    /// <summary>
    /// ほかのプレイヤーのアバター。見た目は PlayerAvatarFactory.BuildVisual（キャラメイクの見た目）で作り、
    /// 約10Hzで届く位置を NetConfig.InterpolationDelay 遅らせて補間する。名前は WorldLabels で頭上に表示。
    /// 自分と違うエリア（家の中・鉱山の階など）にいる時は表示しない。
    /// </summary>
    public sealed class RemotePlayerAvatar : MonoBehaviour
    {
        private struct Sample
        {
            public float Time;
            public Vector3 Position;
            public float RotY;
            public bool Moving;
        }

        public string PlayerId { get; private set; }
        public Transform LabelAnchor { get; private set; }
        public Transform GestureAnchor { get; private set; }
        public string AreaId => areaId;

        private PlayerState state;
        private Func<string> nameFunc;
        private bool isHost;
        private readonly List<Sample> samples = new List<Sample>();
        private string areaId;
        private Transform visual;
        private Vector3 visualBasePos;
        private Quaternion visualBaseRot;
        private float bobPhase;
        private bool moving;
        private string gestureText;
        private float gestureUntil;
        private Coroutine gestureRoutine;
        private bool visible = true;

        public void Init(string playerId, PlayerState ps, Func<string> name, bool host)
        {
            PlayerId = playerId;
            state = ps;
            nameFunc = name;
            isHost = host;
            areaId = ps != null ? ps.CurrentAreaId : AreaIds.Overworld;
            RebuildVisual(ps != null ? ps.Appearance : null);

            float h = ps != null && ps.Appearance != null ? Mathf.Clamp(ps.Appearance.height, 0.8f, 1.2f) : 1f;
            LabelAnchor = PrimitiveBuilder.Empty("NameLabel", transform, new Vector3(0f, 2.15f * h, 0f)).transform;
            GestureAnchor = PrimitiveBuilder.Empty("GestureLabel", transform, new Vector3(0f, 2.6f * h, 0f)).transform;
            WorldLabels.Add(LabelAnchor, () => (isHost ? "★" : "") + (nameFunc != null ? nameFunc() : ""), Vector3.zero,
                isHost ? UIStyles.AccentWarm : UIStyles.Accent, 35f);
            WorldLabels.Add(GestureAnchor, () => UnityEngine.Time.unscaledTime < gestureUntil ? gestureText : null, Vector3.zero, Color.white, 35f);
        }

        public void RebuildVisual(CharacterAppearance appearance)
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = null;
            GameObject v = null;
            try
            {
                v = PlayerAvatarFactory.BuildVisual(appearance, transform);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (v == null) v = PlayerAvatarFactory.BuildDefault(appearance, transform);
            v.name = "Visual";
            // ほかのプレイヤーにぶつかって動けなくならないよう、見た目のコライダーは外す
            foreach (var c in v.GetComponentsInChildren<Collider>()) Destroy(c);
            visual = v.transform;
            visualBasePos = visual.localPosition;
            visualBaseRot = visual.localRotation;
            visual.gameObject.SetActive(visible);
        }

        public void PushSample(Vector3 position, float rotY, bool isMoving, string area)
        {
            float now = UnityEngine.Time.unscaledTime;
            if (string.IsNullOrEmpty(area)) area = AreaIds.Overworld;
            if (samples.Count == 0 || area != areaId || (position - transform.position).sqrMagnitude > NetConfig.TeleportDistance * NetConfig.TeleportDistance)
            {
                samples.Clear();
                transform.position = position;
                transform.rotation = Quaternion.Euler(0f, rotY, 0f);
            }
            areaId = area;
            samples.Add(new Sample { Time = now, Position = position, RotY = rotY, Moving = isMoving });
            if (samples.Count > 40) samples.RemoveAt(0);
        }

        private void Update()
        {
            float now = UnityEngine.Time.unscaledTime;
            float dt = UnityEngine.Time.unscaledDeltaTime;
            UpdateVisibility();
            if (samples.Count == 0) return;

            float renderT = now - NetConfig.InterpolationDelay;
            Vector3 pos;
            float rotY;
            if (samples.Count == 1 || renderT <= samples[0].Time)
            {
                pos = samples[0].Position;
                rotY = samples[0].RotY;
                moving = samples[0].Moving;
            }
            else
            {
                int i = samples.Count - 2;
                while (i > 0 && samples[i].Time > renderT) i--;
                var a = samples[i];
                var b = samples[i + 1];
                if (renderT <= b.Time)
                {
                    float k = Mathf.InverseLerp(a.Time, b.Time, renderT);
                    pos = Vector3.Lerp(a.Position, b.Position, k);
                    rotY = Mathf.LerpAngle(a.RotY, b.RotY, k);
                    moving = b.Moving;
                }
                else
                {
                    // 最新より先：短い間だけ外挿し、それ以上は止まる
                    float span = Mathf.Max(0.001f, b.Time - a.Time);
                    float ex = Mathf.Min(renderT - b.Time, NetConfig.MaxExtrapolation);
                    var vel = (b.Position - a.Position) / span;
                    pos = b.Position + (b.Moving ? vel * ex : Vector3.zero);
                    rotY = b.RotY;
                    moving = b.Moving && renderT - b.Time < NetConfig.MaxExtrapolation;
                }
                // 補間に使い終わった古いサンプルを捨てる
                while (samples.Count > 2 && samples[1].Time < renderT) samples.RemoveAt(0);
            }
            transform.position = pos;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, rotY, 0f), 1f - Mathf.Exp(-14f * dt));

            // 歩いている時の小さな弾み（かわいく）
            if (visual != null && gestureRoutine == null)
            {
                if (moving)
                {
                    bobPhase += dt * 12f;
                    visual.localPosition = visualBasePos + Vector3.up * Mathf.Abs(Mathf.Sin(bobPhase)) * 0.07f;
                }
                else
                {
                    visual.localPosition = Vector3.Lerp(visual.localPosition, visualBasePos, 1f - Mathf.Exp(-10f * dt));
                }
            }
        }

        private void UpdateVisibility()
        {
            var game = Game.Instance;
            var local = game != null ? game.LocalPlayer : null;
            bool v = local == null || string.IsNullOrEmpty(areaId) || local.CurrentAreaId == areaId;
            if (v == visible) return;
            visible = v;
            if (visual != null) visual.gameObject.SetActive(v);
            if (LabelAnchor != null) LabelAnchor.gameObject.SetActive(v);
            if (GestureAnchor != null) GestureAnchor.gameObject.SetActive(v);
        }

        public void PlayGesture(PlayerGesture g)
        {
            gestureText = Loc.Format("（{0}）", "({0})", PlayerGestureUtil.Label(g));
            gestureUntil = UnityEngine.Time.unscaledTime + 2.2f;
            if (gestureRoutine != null) StopCoroutine(gestureRoutine);
            if (visual != null)
            {
                visual.localPosition = visualBasePos;
                visual.localRotation = visualBaseRot;
            }
            gestureRoutine = StartCoroutine(GestureRoutine(g));
        }

        /// <summary>PlayerController.GestureRoutine と同じ簡易な身振り（見た目担当が差し替える想定）。</summary>
        private IEnumerator GestureRoutine(PlayerGesture g)
        {
            if (visual == null)
            {
                gestureRoutine = null;
                yield break;
            }
            float t = 0f;
            while (t < 0.8f && visual != null)
            {
                t += UnityEngine.Time.deltaTime;
                float k = Mathf.Sin(t / 0.8f * Mathf.PI * 2f);
                switch (g)
                {
                    case PlayerGesture.Nod:
                    case PlayerGesture.Bow:
                        visual.localRotation = visualBaseRot * Quaternion.Euler(k * (g == PlayerGesture.Bow ? 25f : 12f), 0, 0);
                        break;
                    case PlayerGesture.ShakeHead:
                        visual.localRotation = visualBaseRot * Quaternion.Euler(0, k * 20f, 0);
                        break;
                    case PlayerGesture.Happy:
                    case PlayerGesture.Clap:
                    case PlayerGesture.Surprised:
                        visual.localPosition = visualBasePos + Vector3.up * Mathf.Abs(k) * 0.3f;
                        break;
                    case PlayerGesture.Sad:
                        visual.localRotation = visualBaseRot * Quaternion.Euler(15f, 0, 0);
                        break;
                    default:
                        visual.localRotation = visualBaseRot * Quaternion.Euler(0, 0, k * 10f);
                        break;
                }
                yield return null;
            }
            if (visual != null)
            {
                visual.localRotation = visualBaseRot;
                visual.localPosition = visualBasePos;
            }
            gestureRoutine = null;
        }

        private void OnDestroy()
        {
            if (LabelAnchor != null) WorldLabels.Remove(LabelAnchor);
            if (GestureAnchor != null) WorldLabels.Remove(GestureAnchor);
        }
    }
}
