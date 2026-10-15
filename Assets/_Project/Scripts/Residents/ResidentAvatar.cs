using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.Visual;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// 住民のシーン上の姿。表情（目・口・眉・頬の形）と身振り（Gesture の各種）を手続き的にアニメーションし、
    /// 系統ごとの移動のしかた（跳ねる・浮かぶ・四足で歩く）を再生する。[E] で話しかけられる。
    /// 位置はシステムが動かす（ホストはAI、ゲストは同期位置への補間）。
    /// </summary>
    public sealed class ResidentAvatar : MonoBehaviour, IInteractable
    {
        public Resident Resident;
        public ResidentSystem System;
        public ResidentRig Rig;

        // ---- 表情 ----
        private Expression baseExpression = Expression.Neutral;
        private Expression overrideExpression = Expression.Neutral;
        private float overrideUntil;
        private float blinkTimer;
        private float blinkPhase = -1f;

        // ---- 身振り ----
        private readonly Queue<GestureCue> gestureQueue = new Queue<GestureCue>();
        private GestureCue currentGesture;
        private float gestureTime;
        private float gestureDuration;
        private GameObject gestureProp;
        private readonly List<GameObject> overheadMarks = new List<GameObject>();
        private Vector3 gestureLookTarget;
        private bool hasLookTarget;

        // ---- 移動・活動 ----
        private Vector3 lastPos;
        private float moveSpeed;
        private float walkPhase;
        private GameObject jobProp;
        private GameObject heldProp;
        private string heldPropId;
        private bool jobPropVisible;
        private float idleTimer;
        private float tearTimer;

        /// <summary>ゲスト側の補間目標。</summary>
        public Vector3 NetTarget;
        public float NetYaw;
        public bool HasNetTarget;

        private bool visible = true;
        private Renderer[] renderers;
        private Collider col;

        public void Init(Resident resident, ResidentSystem system)
        {
            Resident = resident;
            System = system;
            Rig = ResidentAvatarBuilder.Build(transform, resident.Profile, resident.State.job);
            var cap = gameObject.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            float h = Mathf.Max(0.8f, Rig.Height);
            cap.height = h + 0.2f;
            cap.radius = Mathf.Clamp(0.45f * resident.Profile.appearance.size, 0.3f, 0.6f);
            cap.center = new Vector3(0, h * 0.5f, 0);
            col = cap;
            RebuildJobProp();
            renderers = GetComponentsInChildren<Renderer>(true);
            lastPos = transform.position;
            blinkTimer = Random.Range(1.5f, 4f);
        }

        public void RebuildJobProp()
        {
            if (jobProp != null) Destroy(jobProp);
            jobProp = null;
            if (Rig == null || Rig.HandRight == null) return;
            jobProp = ResidentAvatarBuilder.BuildJobProp(Rig.HandRight, Resident.State.job, Mathf.Clamp(Resident.Profile.appearance.size, 0.6f, 1.3f));
            if (jobProp != null) jobProp.SetActive(false);
            jobPropVisible = false;
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        // ================= IInteractable =================

        public string GetInteractionPrompt(PlayerState player)
        {
            if (!visible || System == null) return null;
            if (Resident.State.activity == ResidentActivity.Sleeping) return Loc.T("眠っている", "Sleeping");
            return Loc.T("話す", "Talk");
        }

        public bool CanInteract(PlayerState player) => visible && System != null && System.CanTalk(Resident, player);

        public void Interact(PlayerState player) => System?.BeginConversation(Resident, player);

        // ================= 表情・身振り =================

        public void SetBaseExpression(Expression e) => baseExpression = e;

        public void ShowExpression(Expression e, float seconds = 3.5f)
        {
            overrideExpression = e;
            overrideUntil = UnityEngine.Time.time + seconds;
            if (e == Expression.Crying) tearTimer = seconds;
            if (e == Expression.Love) SpawnHearts();
        }

        public Expression CurrentExpression => UnityEngine.Time.time < overrideUntil ? overrideExpression : baseExpression;

        public void PlayGesture(GestureCue cue)
        {
            if (cue == null || cue.gesture == Gesture.None) return;
            if (gestureQueue.Count > 4) return;
            gestureQueue.Enqueue(cue);
        }

        public void LookAt(Vector3 worldPos)
        {
            gestureLookTarget = worldPos;
            hasLookTarget = true;
        }

        public void SetVisible(bool v)
        {
            if (visible == v) return;
            visible = v;
            if (renderers == null) renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers) if (r != null) r.enabled = v;
            if (col != null) col.enabled = v;
        }

        public bool IsVisible => visible;

        /// <summary>持っている物（抱える・食べる）。null で手放す。</summary>
        public void SetHeldItem(string itemId)
        {
            if (heldPropId == itemId) return;
            heldPropId = itemId;
            if (heldProp != null) Destroy(heldProp);
            heldProp = null;
            if (string.IsNullOrEmpty(itemId) || Rig == null) return;
            var anchor = Rig.HandRight != null ? Rig.HandRight : Rig.Visual;
            if (itemId == "box")
            {
                heldProp = PrimitiveBuilder.Part(Rig.Visual, PrimitiveType.Cube, new Vector3(0, Rig.Height * 0.5f, 0.42f), Vector3.one * 0.38f, new Color(0.78f, 0.6f, 0.38f), "CarryBox");
            }
            else
            {
                heldProp = ResidentAvatarBuilder.BuildItemProp(anchor, itemId, 0.2f);
            }
            renderers = GetComponentsInChildren<Renderer>(true);
            if (!visible) foreach (var r in heldProp.GetComponentsInChildren<Renderer>()) r.enabled = false;
        }

        // ================= 毎フレーム =================

        private void Update()
        {
            if (Rig == null || Resident == null) return;
            float dt = UnityEngine.Time.deltaTime;

            // ゲスト：同期された位置へ補間
            if (HasNetTarget)
            {
                var p = transform.position;
                float d = Vector3.Distance(p, NetTarget);
                if (d > 25f) transform.position = NetTarget;
                else transform.position = Vector3.MoveTowards(p, NetTarget, Mathf.Max(ResidentTuning.WalkSpeed * 1.4f, d * 2.5f) * dt);
                var rot = Quaternion.Euler(0, NetYaw, 0);
                if (Resident.State.activity != ResidentActivity.Chatting || string.IsNullOrEmpty(Resident.State.chatPartnerId))
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, rot, 360f * dt);
            }

            var delta = transform.position - lastPos;
            delta.y = 0;
            float inst = dt > 0 ? delta.magnitude / dt : 0;
            moveSpeed = Mathf.Lerp(moveSpeed, inst, 10f * dt);
            lastPos = transform.position;
            if (!visible) return;

            AnimateBody(dt);
            AnimateFace(dt);
            AnimateGesture(dt);
            UpdateProps();
        }

        private void AnimateBody(float dt)
        {
            var a = Resident.Profile.appearance;
            bool moving = moveSpeed > 0.25f;
            walkPhase += dt * (moving ? 9f * Mathf.Clamp(moveSpeed / 2f, 0.6f, 1.6f) : 2f);
            float y = Rig.BaseVisualY;
            Vector3 bodyScale = Vector3.one;
            float rollZ = 0f;

            if (Rig.Floats)
            {
                // ぷかぷか：いつも上下に漂う。話すと揺れる
                y += 0.12f * Mathf.Sin(UnityEngine.Time.time * 1.8f + a.size * 3f);
                for (int i = 0; i < Rig.Tentacles.Count; i++)
                    Rig.Tentacles[i].localEulerAngles = new Vector3(Mathf.Sin(UnityEngine.Time.time * 2.5f + i) * 18f, 0, Mathf.Cos(UnityEngine.Time.time * 2f + i) * 12f);
            }
            if (moving)
            {
                if (Rig.Bouncy)
                {
                    // まんまる・もちもち：ぴょこぴょこ跳ねる、着地でつぶれる
                    float hop = Mathf.Abs(Mathf.Sin(walkPhase * 0.6f));
                    y += hop * 0.22f;
                    float squash = a.lineage == AlienLineage.Mochi || a.secondary == AlienLineage.Mochi ? 0.25f : 0.1f;
                    bodyScale = new Vector3(1f + (1f - hop) * squash, 1f - (1f - hop) * squash + hop * squash * 0.5f, 1f + (1f - hop) * squash);
                }
                else if (!Rig.Floats)
                {
                    y += Mathf.Abs(Mathf.Sin(walkPhase)) * 0.05f;
                    rollZ = Mathf.Sin(walkPhase) * 4f;
                }
                for (int i = 0; i < Rig.Legs.Count; i++)
                {
                    float sign = (i % 2 == 0 ? 1 : -1) * (Rig.Legs.Count == 4 && i >= 2 ? -1 : 1);
                    Rig.Legs[i].localEulerAngles = new Vector3(Mathf.Sin(walkPhase) * 30f * sign, 0, 0);
                }
            }
            else
            {
                for (int i = 0; i < Rig.Legs.Count; i++) Rig.Legs[i].localEulerAngles = Vector3.zero;
                // 呼吸
                float br = Mathf.Sin(UnityEngine.Time.time * 2f + a.size) * 0.02f;
                bodyScale = new Vector3(1f - br * 0.5f, 1f + br, 1f - br * 0.5f);
            }

            // しっぽ・耳：気持ちで動く（ちび動物・ぬいぐるみ）
            var face = CurrentExpression;
            float happy = face == Expression.Happy || face == Expression.VeryHappy || face == Expression.Excited || face == Expression.Love ? 1f : 0f;
            float sad = face == Expression.Sad || face == Expression.Crying || face == Expression.Worried ? 1f : 0f;
            if (Rig.Tail != null)
                Rig.Tail.localEulerAngles = new Vector3(sad * 30f, Mathf.Sin(UnityEngine.Time.time * (3f + happy * 9f)) * (10f + happy * 25f), 0);
            for (int i = 0; i < Rig.Ears.Count; i++)
            {
                float side = i % 2 == 0 ? -1 : 1;
                Rig.Ears[i].localRotation = Quaternion.Euler(sad * 25f, 0, side * (sad * 35f + Mathf.Sin(UnityEngine.Time.time * 3f + i) * 4f * (1 + happy)));
            }
            // 星の冠：嬉しいと速く回り、明るく脈打つ
            for (int i = 0; i < Rig.Antennae.Count; i++)
            {
                var t = Rig.Antennae[i];
                if (t.name == "Crown") t.localRotation = Quaternion.Euler(0, UnityEngine.Time.time * (20f + happy * 90f), 0);
                else if (face == Expression.Surprised) t.localScale = Vector3.one * 1.25f;
                else t.localScale = Vector3.Lerp(t.localScale, Vector3.one * (sad > 0 ? 0.8f : 1f), 5f * dt);
            }
            float pulse = 1f + (0.12f + happy * 0.25f) * Mathf.Sin(UnityEngine.Time.time * (3f + happy * 5f));
            foreach (var g in Rig.GlowParts) if (g != null) g.localScale = Vector3.one * GlowBase(g) * pulse;

            // 活動ごとの姿勢
            float pitch = 0f;
            float headPitch = 0f;
            float headYaw = 0f;
            float headRoll = 0f;
            idleTimer += dt;
            switch (Resident.State.activity)
            {
                case ResidentActivity.Gardening:
                case ResidentActivity.Farming:
                    pitch = 18f + Mathf.Sin(idleTimer * 2f) * 5f;
                    break;
                case ResidentActivity.WatchingSky:
                    headPitch = -28f;
                    break;
                case ResidentActivity.Playing:
                case ResidentActivity.Celebrating:
                    if (!moving) y += Mathf.Abs(Mathf.Sin(idleTimer * 4f)) * 0.15f;
                    break;
                case ResidentActivity.Resting:
                    rollZ += Mathf.Sin(idleTimer * 0.8f) * 3f;
                    break;
                case ResidentActivity.Chatting:
                    if (!moving) headPitch = Mathf.Max(0, Mathf.Sin(idleTimer * 1.7f)) * 6f;
                    break;
                case ResidentActivity.ShelteringFromRain:
                    rollZ += Mathf.Sin(idleTimer * 12f) * 0.8f;
                    break;
            }
            if (face == Expression.Confused || face == Expression.Thinking) headRoll = 14f;
            if (face == Expression.Shy) headPitch += 12f;
            if (face == Expression.Sad || face == Expression.Crying) { headPitch += 10f; pitch += 4f; }

            var vp = Rig.Visual.localPosition;
            Rig.Visual.localPosition = new Vector3(vp.x, Mathf.Lerp(vp.y, y, 15f * dt), vp.z);
            Rig.Body.localScale = Vector3.Lerp(Rig.Body.localScale, bodyScale, 15f * dt);
            Rig.Visual.localRotation = Quaternion.Slerp(Rig.Visual.localRotation, Quaternion.Euler(pitch, 0, rollZ), 8f * dt);
            if (Rig.Head != null && !(currentGesture != null && GestureUsesHead(currentGesture.gesture)))
            {
                if (hasLookTarget)
                {
                    var dir = transform.InverseTransformPoint(gestureLookTarget);
                    headYaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -50f, 50f);
                }
                var target = Quaternion.Euler(headPitch, headYaw, headRoll);
                Rig.Head.localRotation = Quaternion.Slerp(Rig.Head.localRotation, target, 8f * dt);
            }
            // 腕：活動中の動き
            if (currentGesture == null && Rig.HasArms)
            {
                float armSwing = moving ? Mathf.Sin(walkPhase) * 25f : 0f;
                float work = 0f;
                if (Resident.State.activity == ResidentActivity.Crafting || Resident.State.activity == ResidentActivity.Working || Resident.State.activity == ResidentActivity.Mining)
                    work = Mathf.Max(0, Mathf.Sin(idleTimer * 5f)) * 60f;
                bool carry = Resident.State.carrying || heldProp != null;
                SetArms(carry ? -70f : armSwing, carry ? -70f : -armSwing - work, 12f, carry ? 25f : 12f, dt);
            }
        }

        private readonly Dictionary<Transform, float> glowBase = new Dictionary<Transform, float>();

        private float GlowBase(Transform t)
        {
            if (!glowBase.TryGetValue(t, out var b))
            {
                b = t.localScale.x;
                glowBase[t] = b;
            }
            return b;
        }

        private void SetArms(float leftPitch, float rightPitch, float leftRoll, float rightRoll, float dt)
        {
            for (int i = 0; i < Rig.ArmsLeft.Count; i++)
                Rig.ArmsLeft[i].localRotation = Quaternion.Slerp(Rig.ArmsLeft[i].localRotation, Quaternion.Euler(leftPitch, 0, -leftRoll - i * 15f), 10f * dt);
            for (int i = 0; i < Rig.ArmsRight.Count; i++)
                Rig.ArmsRight[i].localRotation = Quaternion.Slerp(Rig.ArmsRight[i].localRotation, Quaternion.Euler(rightPitch, 0, rightRoll + i * 15f), 10f * dt);
        }

        private void AnimateFace(float dt)
        {
            var e = CurrentExpression;
            if (Resident.State.activity == ResidentActivity.Sleeping) e = Expression.Sleepy;
            Vector3 eye = Rig.EyeBaseScale;
            Vector3 eyeOff = Vector3.zero;
            Vector3 mouth = Rig.MouthBaseScale;
            Vector3 mouthOff = Vector3.zero;
            bool brows = false, cheeks = false;
            float browAngle = 0f;
            float browY = 0f;
            switch (e)
            {
                case Expression.Happy:
                    eye = new Vector3(eye.x, eye.y * 0.38f, eye.z); eyeOff.y = eye.y * 0.3f;
                    mouth = new Vector3(mouth.x * 1.35f, mouth.y * 1.4f, mouth.z); cheeks = true; break;
                case Expression.VeryHappy:
                case Expression.Excited:
                    eye = new Vector3(eye.x, eye.y * 0.3f, eye.z); eyeOff.y = eye.y * 0.4f;
                    mouth = new Vector3(mouth.x * 1.3f, mouth.y * 2.6f, mouth.z); cheeks = true; break;
                case Expression.Sad:
                    eye = new Vector3(eye.x, eye.y * 0.75f, eye.z); eyeOff.y = -eye.y * 0.2f;
                    mouth = new Vector3(mouth.x * 0.6f, mouth.y * 0.7f, mouth.z); mouthOff.y = -mouth.y * 0.3f;
                    brows = true; browAngle = 22f; break;
                case Expression.Crying:
                    eye = new Vector3(eye.x, eye.y * 0.3f, eye.z);
                    mouth = new Vector3(mouth.x * 0.9f, mouth.y * 1.8f, mouth.z);
                    brows = true; browAngle = 25f; break;
                case Expression.Angry:
                    eye = new Vector3(eye.x, eye.y * 0.65f, eye.z);
                    mouth = new Vector3(mouth.x * 0.7f, mouth.y * 0.8f, mouth.z);
                    brows = true; browAngle = -28f; browY = -0.03f; break;
                case Expression.Surprised:
                    eye *= 1.3f;
                    mouth = new Vector3(mouth.x * 0.55f, mouth.y * 2.8f, mouth.z);
                    brows = true; browY = 0.04f; break;
                case Expression.Confused:
                case Expression.Thinking:
                    mouth = new Vector3(mouth.x * 0.5f, mouth.y, mouth.z); mouthOff.x = mouth.x * 0.4f;
                    brows = true; browAngle = 8f; browY = 0.02f; break;
                case Expression.Shy:
                    eye = new Vector3(eye.x, eye.y * 0.55f, eye.z); eyeOff.y = -eye.y * 0.2f;
                    mouth = new Vector3(mouth.x * 0.5f, mouth.y, mouth.z); cheeks = true; break;
                case Expression.Sleepy:
                    eye = new Vector3(eye.x, eye.y * 0.1f, eye.z);
                    mouth = new Vector3(mouth.x * 0.4f, mouth.y * 1.3f, mouth.z); break;
                case Expression.Love:
                    eye = new Vector3(eye.x, eye.y * 0.35f, eye.z); eyeOff.y = eye.y * 0.3f;
                    mouth = new Vector3(mouth.x * 1.1f, mouth.y * 1.4f, mouth.z); cheeks = true; break;
                case Expression.Worried:
                    mouth = new Vector3(mouth.x * 0.8f, mouth.y * 0.6f, mouth.z);
                    brows = true; browAngle = 18f; browY = 0.02f; break;
                case Expression.Disgusted:
                    eye = new Vector3(eye.x, eye.y * 0.5f, eye.z);
                    mouth = new Vector3(mouth.x * 1.1f, mouth.y * 0.5f, mouth.z); mouthOff.x = -mouth.x * 0.2f;
                    brows = true; browAngle = -12f; break;
            }

            // まばたき
            blinkTimer -= dt;
            if (blinkTimer <= 0 && blinkPhase < 0) blinkPhase = 0f;
            if (blinkPhase >= 0)
            {
                blinkPhase += dt / 0.14f;
                float k = 1f - Mathf.Sin(Mathf.Clamp01(blinkPhase) * Mathf.PI);
                eye = new Vector3(eye.x, eye.y * Mathf.Max(0.08f, k), eye.z);
                if (blinkPhase >= 1f)
                {
                    blinkPhase = -1f;
                    blinkTimer = Random.Range(2f, 5f);
                }
            }

            float sp = 18f * dt;
            Rig.EyeL.localScale = Vector3.Lerp(Rig.EyeL.localScale, eye, sp);
            Rig.EyeR.localScale = Vector3.Lerp(Rig.EyeR.localScale, eye, sp);
            Rig.EyeL.localPosition = Vector3.Lerp(Rig.EyeL.localPosition, Rig.EyeLBasePos + eyeOff, sp);
            Rig.EyeR.localPosition = Vector3.Lerp(Rig.EyeR.localPosition, Rig.EyeRBasePos + eyeOff, sp);
            Rig.Mouth.localScale = Vector3.Lerp(Rig.Mouth.localScale, mouth, sp);
            Rig.Mouth.localPosition = Vector3.Lerp(Rig.Mouth.localPosition, Rig.MouthBasePos + mouthOff, sp);
            if (Rig.BrowL.gameObject.activeSelf != brows)
            {
                Rig.BrowL.gameObject.SetActive(brows);
                Rig.BrowR.gameObject.SetActive(brows);
            }
            if (brows)
            {
                var bl = Rig.BrowL.localPosition;
                Rig.BrowL.localPosition = new Vector3(bl.x, Rig.EyeLBasePos.y + Rig.EyeBaseScale.y * 0.9f + browY, bl.z);
                Rig.BrowR.localPosition = new Vector3(-bl.x, Rig.EyeRBasePos.y + Rig.EyeBaseScale.y * 0.9f + browY, bl.z);
                Rig.BrowL.localEulerAngles = new Vector3(0, 0, -browAngle);
                Rig.BrowR.localEulerAngles = new Vector3(0, 0, browAngle);
            }
            if (Rig.CheekL.gameObject.activeSelf != cheeks)
            {
                Rig.CheekL.gameObject.SetActive(cheeks);
                Rig.CheekR.gameObject.SetActive(cheeks);
            }

            // 涙
            if (tearTimer > 0f)
            {
                tearTimer -= dt;
                if (Random.value < dt * 5f) SpawnTear();
            }
        }

        private static bool GestureUsesHead(Gesture g)
        {
            switch (g)
            {
                case Gesture.Nod:
                case Gesture.ShakeHead:
                case Gesture.LookUp:
                case Gesture.Think:
                case Gesture.Bow:
                    return true;
            }
            return false;
        }

        private void AnimateGesture(float dt)
        {
            if (currentGesture == null)
            {
                if (gestureQueue.Count == 0) return;
                currentGesture = gestureQueue.Dequeue();
                gestureTime = 0f;
                gestureDuration = GestureDuration(currentGesture.gesture);
                BeginGesture(currentGesture);
            }
            gestureTime += dt;
            float t = Mathf.Clamp01(gestureTime / gestureDuration);
            float osc = Mathf.Sin(gestureTime * 12f);
            float env = Mathf.Sin(t * Mathf.PI);
            var g = currentGesture.gesture;
            Transform head = Rig.HeadIsBody ? Rig.Visual : Rig.Head;
            switch (g)
            {
                case Gesture.Wave:
                case Gesture.Beckon:
                case Gesture.Shoo:
                    if (Rig.HasArms)
                    {
                        float raise = g == Gesture.Wave ? -150f : -80f;
                        float wob = g == Gesture.Beckon ? osc * 25f : osc * 20f;
                        foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(g == Gesture.Wave ? 0 : raise + wob, 0, g == Gesture.Wave ? 150f * env + wob : 10f);
                    }
                    else Rig.Visual.localRotation = Quaternion.Euler(0, 0, osc * 15f * env);
                    break;
                case Gesture.Nod:
                    head.localRotation = Quaternion.Euler(Mathf.Abs(Mathf.Sin(gestureTime * 9f)) * 22f * env, 0, 0);
                    break;
                case Gesture.Bow:
                    Rig.Visual.localRotation = Quaternion.Euler(32f * env, 0, 0);
                    break;
                case Gesture.ShakeHead:
                    head.localRotation = Quaternion.Euler(0, Mathf.Sin(gestureTime * 14f) * 28f * env, 0);
                    break;
                case Gesture.LookUp:
                    head.localRotation = Quaternion.Euler(-32f * env, 0, 0);
                    break;
                case Gesture.Think:
                    head.localRotation = Quaternion.Euler(0, 0, 16f * env);
                    if (Rig.HasArms) foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(-120f * env, 0, -30f * env);
                    break;
                case Gesture.PointAt:
                    if (Rig.HasArms) foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(-90f * env, 0, 0);
                    else Rig.Visual.localRotation = Quaternion.Euler(-12f * env, 0, 0);
                    break;
                case Gesture.ShowNumber:
                case Gesture.Stretch:
                    if (Rig.HasArms)
                    {
                        foreach (var arm in Rig.ArmsLeft) arm.localRotation = Quaternion.Euler(0, 0, -150f * env);
                        foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(0, 0, 150f * env);
                    }
                    if (g == Gesture.Stretch) Rig.Body.localScale = new Vector3(1f, 1f + 0.12f * env, 1f);
                    break;
                case Gesture.Jump:
                case Gesture.Clap:
                case Gesture.Dance:
                {
                    float hop = Mathf.Abs(Mathf.Sin(gestureTime * (g == Gesture.Dance ? 6f : 8f))) * (g == Gesture.Clap ? 0.12f : 0.35f) * env;
                    var vp = Rig.Visual.localPosition;
                    Rig.Visual.localPosition = new Vector3(vp.x, Rig.BaseVisualY + hop, vp.z);
                    if (Rig.HasArms)
                    {
                        float clap = g == Gesture.Clap ? Mathf.Abs(osc) * 40f : 0f;
                        foreach (var arm in Rig.ArmsLeft) arm.localRotation = Quaternion.Euler(-70f * env, clap, -20f - (g == Gesture.Dance ? osc * 40f : 0f));
                        foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(-70f * env, -clap, 20f + (g == Gesture.Dance ? osc * 40f : 0f));
                    }
                    if (g == Gesture.Dance) Rig.Visual.localRotation = Quaternion.Euler(0, Mathf.Sin(gestureTime * 3f) * 30f, osc * 8f);
                    break;
                }
                case Gesture.Spin:
                    Rig.Visual.localRotation = Quaternion.Euler(0, t * 360f, 0);
                    break;
                case Gesture.Shrug:
                    if (Rig.HasArms)
                    {
                        foreach (var arm in Rig.ArmsLeft) arm.localRotation = Quaternion.Euler(-40f * env, 0, -50f * env);
                        foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(-40f * env, 0, 50f * env);
                    }
                    head.localRotation = Quaternion.Euler(0, 0, 10f * env);
                    break;
                case Gesture.Hug:
                case Gesture.Hold:
                case Gesture.OfferItem:
                case Gesture.Eat:
                    if (Rig.HasArms)
                    {
                        foreach (var arm in Rig.ArmsLeft) arm.localRotation = Quaternion.Euler(-80f * env, g == Gesture.Hug ? 30f : 10f, 0);
                        foreach (var arm in Rig.ArmsRight) arm.localRotation = Quaternion.Euler(-80f * env, g == Gesture.Hug ? -30f : -10f, 0);
                    }
                    if (g == Gesture.Eat && gestureProp != null)
                        gestureProp.transform.localScale = Vector3.one * 0.2f * (1f - t * 0.9f);
                    break;
                case Gesture.Cry:
                    if (Random.value < dt * 8f) SpawnTear();
                    break;
                case Gesture.Shiver:
                    Rig.Visual.localPosition = new Vector3(Mathf.Sin(gestureTime * 60f) * 0.03f, Rig.Visual.localPosition.y, 0);
                    break;
            }
            if (gestureTime >= gestureDuration) EndGesture();
        }

        private float GestureDuration(Gesture g)
        {
            switch (g)
            {
                case Gesture.ShowNumber: return 2.8f;
                case Gesture.OfferItem: return 2.4f;
                case Gesture.Eat: return 2.0f;
                case Gesture.Dance: return 2.8f;
                case Gesture.Cry: return 2.5f;
                case Gesture.Spin: return 1.0f;
                case Gesture.Wave: return 1.6f;
                default: return 1.4f;
            }
        }

        private void BeginGesture(GestureCue cue)
        {
            switch (cue.gesture)
            {
                case Gesture.ShowNumber:
                    SpawnNumberDots(Mathf.Clamp(cue.number, 1, 12));
                    break;
                case Gesture.OfferItem:
                case Gesture.Eat:
                case Gesture.Hold:
                    if (!string.IsNullOrEmpty(cue.target) && ItemRegistry.Exists(cue.target))
                    {
                        gestureProp = ResidentAvatarBuilder.BuildItemProp(Rig.Visual, cue.target, 0.22f);
                        gestureProp.transform.localPosition = new Vector3(0, Rig.Height * (cue.gesture == Gesture.Eat ? 0.7f : 0.5f), 0.45f);
                    }
                    break;
                case Gesture.PointAt:
                    var p = System != null ? System.ResolvePointTarget(cue.target, transform.position) : (Vector3?)null;
                    if (p.HasValue) LookAt(p.Value);
                    break;
                case Gesture.Cry:
                    ShowExpression(Expression.Crying, 2.5f);
                    break;
            }
        }

        private void EndGesture()
        {
            if (gestureProp != null) Destroy(gestureProp);
            gestureProp = null;
            currentGesture = null;
            Rig.Body.localScale = Vector3.one;
            var vp = Rig.Visual.localPosition;
            Rig.Visual.localPosition = new Vector3(0, vp.y, 0);
        }

        private void SpawnNumberDots(int n)
        {
            ClearOverhead();
            float spacing = 0.18f;
            float start = -(n - 1) * spacing * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var dot = PrimitiveBuilder.EmissivePart(Rig.OverHead, PrimitiveType.Sphere, new Vector3(start + i * spacing, 0, 0), Vector3.one * 0.13f, new Color(1f, 0.9f, 0.4f), "NumberDot");
                overheadMarks.Add(dot);
            }
            Invoke(nameof(ClearOverhead), 3f);
        }

        private void SpawnHearts()
        {
            ClearOverhead();
            for (int i = 0; i < 3; i++)
            {
                var h = PrimitiveBuilder.EmissivePart(Rig.OverHead, PrimitiveType.Sphere, new Vector3((i - 1) * 0.2f, i % 2 * 0.12f, 0), Vector3.one * 0.12f, new Color(1f, 0.5f, 0.7f), "Heart");
                overheadMarks.Add(h);
            }
            Invoke(nameof(ClearOverhead), 2.5f);
        }

        private void ClearOverhead()
        {
            foreach (var m in overheadMarks) if (m != null) Destroy(m);
            overheadMarks.Clear();
        }

        private void SpawnTear()
        {
            if (Rig == null || Rig.EyeL == null) return;
            var src = Random.value < 0.5f ? Rig.EyeL : Rig.EyeR;
            var tear = PrimitiveBuilder.Create(PrimitiveType.Sphere, null, src.position + transform.forward * 0.05f, Vector3.one * 0.06f, new Color(0.55f, 0.8f, 1f, 0.9f), "Tear", false);
            if (Game.Instance != null && Game.Instance.SceneRoot != null) tear.transform.SetParent(Game.Instance.SceneRoot, true);
            var fall = tear.AddComponent<FallingDrop>();
            fall.life = 0.8f;
        }

        private void UpdateProps()
        {
            var act = Resident.State.activity;
            bool showJob = act == ResidentActivity.Working || act == ResidentActivity.Crafting || act == ResidentActivity.Fishing
                           || act == ResidentActivity.Gardening || act == ResidentActivity.Cooking || act == ResidentActivity.Mining
                           || act == ResidentActivity.Playing && Resident.State.job == ResidentJob.Musician;
            if (act == ResidentActivity.Fishing && Resident.State.job != ResidentJob.FishingMaster)
            {
                // 趣味の釣りでも竿を持つ
                if (jobProp == null || jobProp.name != "HobbyRod")
                {
                    if (jobProp != null) Destroy(jobProp);
                    jobProp = ResidentAvatarBuilder.BuildJobProp(Rig.HandRight != null ? Rig.HandRight : Rig.Visual, ResidentJob.FishingMaster, Resident.Profile.appearance.size);
                    if (jobProp != null) jobProp.name = "HobbyRod";
                    renderers = GetComponentsInChildren<Renderer>(true);
                }
            }
            else if (jobProp != null && jobProp.name == "HobbyRod")
            {
                RebuildJobProp();
            }
            if (jobProp != null && jobPropVisible != (showJob && heldProp == null))
            {
                jobPropVisible = showJob && heldProp == null;
                jobProp.SetActive(jobPropVisible);
            }
        }

        private void OnDestroy()
        {
            ClearOverhead();
        }
    }

    /// <summary>涙などの小さな落下演出（見た目だけ）。</summary>
    public sealed class FallingDrop : MonoBehaviour
    {
        public float life = 1f;
        private float vy;

        private void Update()
        {
            vy -= 6f * UnityEngine.Time.deltaTime;
            transform.position += new Vector3(0, vy * UnityEngine.Time.deltaTime, 0);
            life -= UnityEngine.Time.deltaTime;
            if (life <= 0) Destroy(gameObject);
        }
    }
}
