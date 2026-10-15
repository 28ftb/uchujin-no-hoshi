using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 伝言生物マネピ（No.033「ほかの生物や人物の動きを真似する」）の見た目と操作。
    /// ・止まり木にとまった、まるいラベンダー色の生き物（プリミティブ製）。[E] で伝言板を開く。
    /// ・待機中：ふわふわ揺れ・まばたき・近くの主人公の方を向く。主人公の身振りを小さく真似する（生き物仕様の「主人公の動作を真似する」）。
    /// ・再生時：話し手の色に少し染まり（外見の一部をデフォルメして模倣【提案】）、表情・身振りを再現する（§4.2）。
    ///   指で示した数（ShowNumber）は頭の上に光の玉を数だけ浮かべて再現する。
    /// </summary>
    public sealed class ManepiBehaviour : MonoBehaviour, IInteractable
    {
        public static readonly Color BaseColor = new Color(0.78f, 0.7f, 1f);
        private static readonly Color BellyColor = new Color(1f, 0.93f, 0.96f);
        private static readonly Color AccentColor = new Color(1f, 0.62f, 0.75f);

        private TabletSystem sys;
        public string BoardId { get; private set; }
        public Transform VoiceAnchor { get; private set; }
        public Transform LabelAnchor { get; private set; }

        private Transform pivot;      // 生き物全体（向き・跳ね）
        private Transform body;       // 胴体（伸び縮み）
        private Transform earL, earR;
        private Transform eyeL, eyeR;
        private Transform indicator;
        private GameObject star;      // 再現用：星の子の飾り
        private GameObject armL, armR; // 再現用：多腕
        private readonly List<Renderer> tintRenderers = new List<Renderer>();
        private MaterialPropertyBlock mpb;
        private Color tint = BaseColor;

        private Quaternion earLBase, earRBase;
        private Vector3 bodyBaseScale;
        private float blinkTimer;
        private float yaw;
        private bool busy;
        private Coroutine routine;
        private readonly List<GameObject> temp = new List<GameObject>();

        internal void Init(TabletSystem system, string boardId)
        {
            sys = system;
            BoardId = boardId;
            Build();
            var col = gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 1.05f, 0f);
            col.radius = 0.75f;
            blinkTimer = 2f + (StableHash.Fnv1a64(boardId) % 100) / 50f;
        }

        // ------------------------------------------------------------
        // 見た目
        // ------------------------------------------------------------

        private void Build()
        {
            var t = transform;
            var wood = new Color(0.58f, 0.42f, 0.28f);
            // 止まり木
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.03f, 0f), new Vector3(0.7f, 0.03f, 0.7f), new Color(0.75f, 0.68f, 0.6f), "PerchBase");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.45f, 0f), new Vector3(0.11f, 0.45f, 0.11f), wood, "PerchPost");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0f, 0.92f, 0f), new Vector3(0.07f, 0.38f, 0.07f), wood, "PerchBar", new Vector3(0f, 0f, 90f));
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.38f, 0.92f, 0f), Vector3.one * 0.1f, AccentColor, "PerchKnobR");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.38f, 0.92f, 0f), Vector3.one * 0.1f, AccentColor, "PerchKnobL");

            pivot = PrimitiveBuilder.Empty("Manepi", t, new Vector3(0f, 0.96f, 0f)).transform;
            body = PrimitiveBuilder.Empty("Body", pivot, Vector3.zero).transform;
            bodyBaseScale = Vector3.one;
            var b = PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0f, 0.3f, 0f), new Vector3(0.62f, 0.56f, 0.58f), BaseColor, "Round");
            tintRenderers.Add(b.GetComponent<Renderer>());
            PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0f, 0.25f, 0.15f), new Vector3(0.42f, 0.38f, 0.32f), BellyColor, "Belly");
            // 目（白目＋黒目＋光）
            eyeL = MakeEye(body, -0.13f);
            eyeR = MakeEye(body, 0.13f);
            // ほっぺ・くちばし
            PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(-0.22f, 0.28f, 0.2f), new Vector3(0.1f, 0.06f, 0.05f), AccentColor, "CheekL");
            PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0.22f, 0.28f, 0.2f), new Vector3(0.1f, 0.06f, 0.05f), AccentColor, "CheekR");
            PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0f, 0.31f, 0.29f), new Vector3(0.09f, 0.05f, 0.06f), new Color(1f, 0.7f, 0.35f), "Beak");
            // 耳（まねっこ用の長い耳）
            earL = MakeEar(body, -1f);
            earR = MakeEar(body, 1f);
            earLBase = earL.localRotation;
            earRBase = earR.localRotation;
            // しっぽ
            var tail = PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0f, 0.2f, -0.3f), new Vector3(0.16f, 0.16f, 0.16f), BaseColor, "Tail");
            tintRenderers.Add(tail.GetComponent<Renderer>());
            PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0f, 0.28f, -0.38f), new Vector3(0.1f, 0.1f, 0.1f), AccentColor, "TailTip");
            // 足
            PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(-0.1f, 0.02f, 0.06f), new Vector3(0.1f, 0.06f, 0.12f), new Color(1f, 0.7f, 0.35f), "FootL");
            PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0.1f, 0.02f, 0.06f), new Vector3(0.1f, 0.06f, 0.12f), new Color(1f, 0.7f, 0.35f), "FootR");
            // 再現用の飾り（ふだんは隠す）
            star = PrimitiveBuilder.EmissivePart(body, PrimitiveType.Sphere, new Vector3(0f, 0.78f, 0f), Vector3.one * 0.14f, new Color(1f, 0.92f, 0.45f), "MimicStar");
            star.SetActive(false);
            armL = PrimitiveBuilder.Part(body, PrimitiveType.Capsule, new Vector3(-0.36f, 0.22f, 0.02f), new Vector3(0.08f, 0.12f, 0.08f), BaseColor, "MimicArmL", new Vector3(0f, 0f, -50f));
            armR = PrimitiveBuilder.Part(body, PrimitiveType.Capsule, new Vector3(0.36f, 0.22f, 0.02f), new Vector3(0.08f, 0.12f, 0.08f), BaseColor, "MimicArmR", new Vector3(0f, 0f, 50f));
            tintRenderers.Add(armL.GetComponent<Renderer>());
            tintRenderers.Add(armR.GetComponent<Renderer>());
            armL.SetActive(false);
            armR.SetActive(false);

            // 新しい伝言の印（「！」）
            indicator = PrimitiveBuilder.Empty("NewMessage", t, new Vector3(0f, 2.05f, 0f)).transform;
            PrimitiveBuilder.EmissivePart(indicator, PrimitiveType.Capsule, new Vector3(0f, 0.12f, 0f), new Vector3(0.09f, 0.14f, 0.09f), new Color(1f, 0.85f, 0.3f), "Bar");
            PrimitiveBuilder.EmissivePart(indicator, PrimitiveType.Sphere, new Vector3(0f, -0.1f, 0f), Vector3.one * 0.09f, new Color(1f, 0.85f, 0.3f), "Dot");

            VoiceAnchor = PrimitiveBuilder.Empty("VoiceAnchor", t, new Vector3(0f, 1.75f, 0f)).transform;
            LabelAnchor = PrimitiveBuilder.Empty("LabelAnchor", t, new Vector3(0f, 2.45f, 0f)).transform;
            mpb = new MaterialPropertyBlock();
            ApplyTint(BaseColor);
        }

        private static Transform MakeEye(Transform parent, float x)
        {
            var eye = PrimitiveBuilder.Part(parent, PrimitiveType.Sphere, new Vector3(x, 0.42f, 0.23f), Vector3.one * 0.16f, Color.white, "Eye").transform;
            PrimitiveBuilder.Part(eye, PrimitiveType.Sphere, new Vector3(0f, 0f, 0.38f), Vector3.one * 0.55f, new Color(0.12f, 0.1f, 0.18f), "Pupil");
            PrimitiveBuilder.EmissivePart(eye, PrimitiveType.Sphere, new Vector3(0.15f, 0.18f, 0.55f), Vector3.one * 0.2f, Color.white, "Shine");
            return eye;
        }

        private Transform MakeEar(Transform parent, float side)
        {
            var root = PrimitiveBuilder.Empty(side < 0 ? "EarL" : "EarR", parent, new Vector3(0.16f * side, 0.52f, 0f)).transform;
            root.localEulerAngles = new Vector3(0f, 0f, -22f * side);
            var ear = PrimitiveBuilder.Part(root, PrimitiveType.Capsule, new Vector3(0f, 0.16f, 0f), new Vector3(0.1f, 0.16f, 0.08f), BaseColor, "Ear");
            tintRenderers.Add(ear.GetComponent<Renderer>());
            PrimitiveBuilder.Part(root, PrimitiveType.Sphere, new Vector3(0f, 0.32f, 0f), Vector3.one * 0.09f, AccentColor, "EarTip");
            return root;
        }

        private void ApplyTint(Color c)
        {
            tint = c;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            foreach (var r in tintRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", c);
                mpb.SetColor("_Color", c);
                r.SetPropertyBlock(mpb);
            }
        }

        // ------------------------------------------------------------
        // 毎フレーム
        // ------------------------------------------------------------

        private void Update()
        {
            float now = UnityEngine.Time.time;
            if (!busy && body != null)
            {
                float k = Mathf.Sin(now * 2.2f);
                body.localPosition = new Vector3(0f, 0.015f * k, 0f);
                body.localScale = new Vector3(1f + 0.02f * k, 1f - 0.02f * k, 1f + 0.02f * k);
            }
            // まばたき
            blinkTimer -= UnityEngine.Time.deltaTime;
            if (eyeL != null && eyeR != null)
            {
                float sy = blinkTimer < 0.12f ? 0.15f : 1f;
                eyeL.localScale = new Vector3(0.16f, 0.16f * sy, 0.16f);
                eyeR.localScale = new Vector3(0.16f, 0.16f * sy, 0.16f);
                if (blinkTimer < 0f) blinkTimer = 2.5f + Mathf.PerlinNoise(now, 0.3f) * 3f;
            }
            // 新しい伝言の印
            if (indicator != null && sys != null)
            {
                bool show = sys.UnheardCount(BoardId) > 0;
                if (indicator.gameObject.activeSelf != show) indicator.gameObject.SetActive(show);
                if (show)
                {
                    indicator.localPosition = new Vector3(0f, 2.05f + 0.08f * Mathf.Sin(now * 3f), 0f);
                    indicator.localRotation = Quaternion.Euler(0f, now * 90f, 0f);
                }
            }
            // 近くの主人公の方を向く
            if (!busy && pivot != null)
            {
                var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
                float target = 0f;
                if (p != null && p.Avatar != null)
                {
                    var d = p.Position - transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude < TabletTuning.ManepiLookRange * TabletTuning.ManepiLookRange && d.sqrMagnitude > 0.01f)
                    {
                        var local = transform.InverseTransformDirection(d.normalized);
                        target = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                    }
                }
                yaw = Mathf.LerpAngle(yaw, target, UnityEngine.Time.deltaTime * 3f);
                pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        private void OnDestroy()
        {
            StopTemp();
            if (sys != null) sys.UnregisterManepi(this);
            if (LabelAnchor != null) WorldLabels.Remove(LabelAnchor);
        }

        internal string LabelText()
        {
            if (sys == null) return null;
            int n = sys.UnheardCount(BoardId);
            string label = sys.BoardLabel(BoardId);
            return n > 0 ? label + Loc.Format("（新しい伝言 {0}）", " ({0} new)", n) : label;
        }

        // ------------------------------------------------------------
        // IInteractable
        // ------------------------------------------------------------

        public string GetInteractionPrompt(PlayerState player)
        {
            if (sys == null) return null;
            int all = sys.GetMessages(BoardId).Count;
            int n = sys.UnheardCount(BoardId);
            if (all == 0) return Loc.T("マネピ（伝言はないみたい）", "Manepi (no messages)");
            return n > 0 ? Loc.Format("マネピの伝言を聞く（新しい伝言 {0}）", "Listen to Manepi ({0} new)", n)
                         : Loc.Format("マネピの伝言を聞く（{0}件）", "Listen to Manepi ({0})", all);
        }

        public bool CanInteract(PlayerState player) => sys != null;

        public void Interact(PlayerState player)
        {
            if (sys == null) return;
            if (sys.GetMessages(BoardId).Count == 0) Play(HappyHop());
            sys.OpenBoard(BoardId, player);
        }

        // ------------------------------------------------------------
        // 真似（主人公の身振り）
        // ------------------------------------------------------------

        public void Mimic(PlayerGesture g)
        {
            if (busy || !isActiveAndEnabled) return;
            Play(MimicRoutine(g));
        }

        private IEnumerator MimicRoutine(PlayerGesture g)
        {
            yield return new WaitForSeconds(TabletTuning.ManepiMimicDelay);
            switch (g)
            {
                case PlayerGesture.Nod: yield return Nod(2, 14f); break;
                case PlayerGesture.ShakeHead: yield return Shake(2, 22f); break;
                case PlayerGesture.Wave: yield return EarWave(earR, 2); break;
                case PlayerGesture.Bow: yield return Bow(); break;
                case PlayerGesture.Clap: yield return EarClap(3); break;
                case PlayerGesture.Happy: yield return Hop(2, 0.22f); break;
                case PlayerGesture.Surprised: yield return Hop(1, 0.35f); break;
                case PlayerGesture.Sad: yield return Droop(1.0f); break;
                case PlayerGesture.Think: yield return Tilt(1.0f); break;
                case PlayerGesture.Beckon: yield return Lean(2); break;
                case PlayerGesture.Point: yield return EarPoint(earR, 0.8f); break;
                default: yield return Hop(1, 0.12f); break;
            }
        }

        // ------------------------------------------------------------
        // 伝言の再現（表情・身振り）
        // ------------------------------------------------------------

        /// <summary>伝言を再現する。color=話し手の色、lineage=話し手の系統（特徴を少し真似る）。</summary>
        public void Reenact(UtteranceRecord rec, Color speakerColor, int lineage)
        {
            if (rec == null || !isActiveAndEnabled) return;
            Play(ReenactRoutine(rec, speakerColor, lineage));
        }

        private IEnumerator ReenactRoutine(UtteranceRecord rec, Color speakerColor, int lineage)
        {
            // 話し手の色に少し染まる・特徴を出す
            var target = Color.Lerp(BaseColor, speakerColor, 0.75f);
            for (float t = 0; t < 0.3f; t += UnityEngine.Time.deltaTime)
            {
                ApplyTint(Color.Lerp(BaseColor, target, t / 0.3f));
                yield return null;
            }
            ApplyTint(target);
            var lin = (AlienLineage)lineage;
            star.SetActive(lin == AlienLineage.StarChild);
            armL.SetActive(lin == AlienLineage.MultiArm);
            armR.SetActive(lin == AlienLineage.MultiArm);
            if (lin == AlienLineage.ChibiAnimal) { earL.localScale = earR.localScale = new Vector3(1.4f, 0.8f, 1.4f); }

            // 表情
            yield return ExpressionMotion(rec.expression);
            // 身振り（記録された順に）
            if (rec.gestures != null)
            {
                foreach (var g in rec.gestures)
                {
                    if (g == null) continue;
                    yield return GestureCueRoutine(g);
                    yield return new WaitForSeconds(0.15f);
                }
            }
            yield return new WaitForSeconds(0.4f);

            // 元に戻る
            for (float t = 0; t < 0.4f; t += UnityEngine.Time.deltaTime)
            {
                ApplyTint(Color.Lerp(target, BaseColor, t / 0.4f));
                yield return null;
            }
            ApplyTint(BaseColor);
            star.SetActive(false);
            armL.SetActive(false);
            armR.SetActive(false);
            earL.localScale = earR.localScale = Vector3.one;
        }

        private IEnumerator ExpressionMotion(Expression e)
        {
            switch (e)
            {
                case Uchujin.Core.Expression.Happy:
                case Uchujin.Core.Expression.VeryHappy:
                case Uchujin.Core.Expression.Excited:
                    yield return Hop(e == Uchujin.Core.Expression.VeryHappy ? 3 : 2, 0.2f);
                    break;
                case Uchujin.Core.Expression.Sad:
                case Uchujin.Core.Expression.Crying:
                    if (e == Uchujin.Core.Expression.Crying) SpawnTears();
                    yield return Droop(1.2f);
                    break;
                case Uchujin.Core.Expression.Angry:
                case Uchujin.Core.Expression.Disgusted:
                    yield return Jitter(0.8f, 0.05f, 30f);
                    break;
                case Uchujin.Core.Expression.Surprised:
                    yield return Hop(1, 0.38f);
                    break;
                case Uchujin.Core.Expression.Confused:
                case Uchujin.Core.Expression.Thinking:
                    yield return Tilt(1.0f);
                    break;
                case Uchujin.Core.Expression.Shy:
                    yield return Turn(-50f, 0.9f);
                    break;
                case Uchujin.Core.Expression.Sleepy:
                    yield return Sway(1.4f, 8f, 1.5f);
                    break;
                case Uchujin.Core.Expression.Love:
                    yield return Pulse(1.2f);
                    break;
                case Uchujin.Core.Expression.Worried:
                    yield return Jitter(0.8f, 0.015f, 10f);
                    break;
                default:
                    yield return new WaitForSeconds(0.4f);
                    break;
            }
        }

        private IEnumerator GestureCueRoutine(GestureCue g)
        {
            switch (g.gesture)
            {
                case Gesture.Nod: return Nod(2, 16f);
                case Gesture.ShakeHead: return Shake(3, 25f);
                case Gesture.Wave: return EarWave(earR, 3);
                case Gesture.Jump: return Hop(2, 0.3f);
                case Gesture.Bow: return Bow();
                case Gesture.Shrug: return EarShrug();
                case Gesture.Hug: return Squeeze(1.0f);
                case Gesture.Clap: return EarClap(3);
                case Gesture.Spin: return Spin();
                case Gesture.Think: return Tilt(1.0f);
                case Gesture.Beckon: return Lean(2);
                case Gesture.Shoo: return EarWave(earL, 3);
                case Gesture.LookUp: return LookUp();
                case Gesture.Shiver: return Jitter(1.0f, 0.02f, 40f);
                case Gesture.Stretch: return Stretch();
                case Gesture.Dance: return Dance();
                case Gesture.Cry: SpawnTears(); return Droop(1.0f);
                case Gesture.Eat: return Nod(4, 8f);
                case Gesture.ShowNumber: return ShowNumber(g.number);
                case Gesture.PointAt: return PointAt(g.target);
                case Gesture.OfferItem:
                case Gesture.Hold: return ShowItem(g.target);
                default: return Hop(1, 0.1f);
            }
        }

        // ---- 動きの部品 ----

        private void Play(IEnumerator r)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                RestoreLook();
            }
            StopTemp();
            ResetPose();
            routine = StartCoroutine(Wrap(r));
        }

        /// <summary>
        /// 入れ子の IEnumerator を自前で平らに回す（途中で止めた時に子の動きまで確実に止めるため）。
        /// WaitForSeconds などはそのまま Unity に渡す。
        /// </summary>
        private IEnumerator Wrap(IEnumerator root)
        {
            busy = true;
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }
                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }
                yield return top.Current;
            }
            ResetPose();
            busy = false;
            routine = null;
        }

        /// <summary>再現の途中で止めた時に、色と飾りを元に戻す。</summary>
        private void RestoreLook()
        {
            ApplyTint(BaseColor);
            if (star != null) star.SetActive(false);
            if (armL != null) armL.SetActive(false);
            if (armR != null) armR.SetActive(false);
            if (earL != null) earL.localScale = Vector3.one;
            if (earR != null) earR.localScale = Vector3.one;
        }

        private void ResetPose()
        {
            if (body == null) return;
            body.localPosition = Vector3.zero;
            body.localRotation = Quaternion.identity;
            body.localScale = bodyBaseScale;
            earL.localRotation = earLBase;
            earR.localRotation = earRBase;
        }

        private void StopTemp()
        {
            foreach (var go in temp) if (go != null) Destroy(go);
            temp.Clear();
        }

        private IEnumerator Hop(int times, float height)
        {
            for (int i = 0; i < times; i++)
            {
                for (float t = 0; t < 0.32f; t += UnityEngine.Time.deltaTime)
                {
                    float k = t / 0.32f;
                    body.localPosition = new Vector3(0f, Mathf.Sin(k * Mathf.PI) * height, 0f);
                    body.localScale = new Vector3(1f - 0.08f * Mathf.Sin(k * Mathf.PI), 1f + 0.1f * Mathf.Sin(k * Mathf.PI), 1f);
                    yield return null;
                }
            }
            body.localPosition = Vector3.zero;
            body.localScale = bodyBaseScale;
        }

        private IEnumerator Nod(int times, float angle)
        {
            for (float t = 0; t < 0.35f * times; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(Mathf.Abs(Mathf.Sin(t / 0.35f * Mathf.PI)) * angle, 0f, 0f);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        private IEnumerator Shake(int times, float angle)
        {
            for (float t = 0; t < 0.3f * times; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, Mathf.Sin(t / 0.3f * Mathf.PI * 2f) * angle, 0f);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        private IEnumerator Bow()
        {
            for (float t = 0; t < 0.9f; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(Mathf.Sin(t / 0.9f * Mathf.PI) * 35f, 0f, 0f);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        private IEnumerator Droop(float seconds)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI);
                body.localScale = new Vector3(1f + 0.05f * k, 1f - 0.15f * k, 1f);
                earL.localRotation = earLBase * Quaternion.Euler(0f, 0f, 50f * k);
                earR.localRotation = earRBase * Quaternion.Euler(0f, 0f, -50f * k);
                body.localRotation = Quaternion.Euler(12f * k, 0f, 0f);
                yield return null;
            }
        }

        private IEnumerator Tilt(float seconds)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI) * 22f);
                yield return null;
            }
        }

        private IEnumerator Turn(float angle, float seconds)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI) * angle, 0f);
                yield return null;
            }
        }

        private IEnumerator Jitter(float seconds, float amount, float freq)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                body.localPosition = new Vector3(Mathf.Sin(t * freq) * amount, 0f, 0f);
                yield return null;
            }
            body.localPosition = Vector3.zero;
        }

        private IEnumerator Sway(float seconds, float angle, float freq)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * freq * Mathf.PI) * angle);
                yield return null;
            }
        }

        private IEnumerator Pulse(float seconds)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                float k = 1f + 0.08f * Mathf.Abs(Mathf.Sin(t * 6f));
                body.localScale = new Vector3(k, k, k);
                yield return null;
            }
        }

        private IEnumerator Squeeze(float seconds)
        {
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI);
                body.localScale = new Vector3(1f - 0.15f * k, 1f + 0.05f * k, 1f - 0.1f * k);
                earL.localRotation = earLBase * Quaternion.Euler(0f, 0f, -40f * k);
                earR.localRotation = earRBase * Quaternion.Euler(0f, 0f, 40f * k);
                yield return null;
            }
        }

        private IEnumerator Stretch()
        {
            for (float t = 0; t < 1.0f; t += UnityEngine.Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                body.localScale = new Vector3(1f - 0.08f * k, 1f + 0.22f * k, 1f);
                yield return null;
            }
        }

        private IEnumerator Spin()
        {
            for (float t = 0; t < 0.7f; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, t / 0.7f * 360f, 0f);
                yield return null;
            }
            body.localRotation = Quaternion.identity;
        }

        private IEnumerator Dance()
        {
            for (float t = 0; t < 1.6f; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 8f) * 25f, Mathf.Sin(t * 4f) * 12f);
                body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(t * 8f)) * 0.08f, 0f);
                yield return null;
            }
        }

        private IEnumerator LookUp()
        {
            for (float t = 0; t < 1.1f; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(-Mathf.Sin(Mathf.Clamp01(t / 1.1f) * Mathf.PI) * 30f, 0f, 0f);
                yield return null;
            }
        }

        private IEnumerator Lean(int times)
        {
            for (float t = 0; t < 0.45f * times; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(Mathf.Sin(t / 0.45f * Mathf.PI * 2f) * 14f, 0f, 0f);
                earR.localRotation = earRBase * Quaternion.Euler(Mathf.Sin(t / 0.45f * Mathf.PI * 2f) * 50f, 0f, 0f);
                yield return null;
            }
        }

        private IEnumerator EarWave(Transform ear, int times)
        {
            var baseRot = ear == earL ? earLBase : earRBase;
            float side = ear == earL ? 1f : -1f;
            for (float t = 0; t < 0.3f * times; t += UnityEngine.Time.deltaTime)
            {
                ear.localRotation = baseRot * Quaternion.Euler(0f, 0f, side * (30f + Mathf.Sin(t / 0.3f * Mathf.PI * 2f) * 35f));
                yield return null;
            }
            ear.localRotation = baseRot;
        }

        private IEnumerator EarClap(int times)
        {
            for (float t = 0; t < 0.25f * times; t += UnityEngine.Time.deltaTime)
            {
                float k = Mathf.Abs(Mathf.Sin(t / 0.25f * Mathf.PI));
                earL.localRotation = earLBase * Quaternion.Euler(0f, 0f, -30f * k);
                earR.localRotation = earRBase * Quaternion.Euler(0f, 0f, 30f * k);
                yield return null;
            }
        }

        private IEnumerator EarShrug()
        {
            for (float t = 0; t < 0.8f; t += UnityEngine.Time.deltaTime)
            {
                float k = Mathf.Sin(Mathf.Clamp01(t / 0.8f) * Mathf.PI);
                earL.localRotation = earLBase * Quaternion.Euler(0f, 0f, -25f * k);
                earR.localRotation = earRBase * Quaternion.Euler(0f, 0f, 25f * k);
                body.localPosition = new Vector3(0f, -0.04f * k, 0f);
                yield return null;
            }
        }

        private IEnumerator EarPoint(Transform ear, float seconds)
        {
            var baseRot = ear == earL ? earLBase : earRBase;
            float side = ear == earL ? 1f : -1f;
            for (float t = 0; t < seconds; t += UnityEngine.Time.deltaTime)
            {
                ear.localRotation = baseRot * Quaternion.Euler(-70f * Mathf.Sin(Mathf.Clamp01(t / seconds) * Mathf.PI), 0f, side * 20f);
                yield return null;
            }
            ear.localRotation = baseRot;
        }

        /// <summary>指で示した数：頭の上に光の玉を数だけ並べる（数の手掛かりを失わない）。</summary>
        private IEnumerator ShowNumber(int n)
        {
            n = Mathf.Clamp(n, 0, 12);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * 0.16f;
                var orb = PrimitiveBuilder.EmissivePart(pivot, PrimitiveType.Sphere, new Vector3(x, 1.05f, 0.1f), Vector3.one * 0.11f, new Color(0.6f, 1f, 0.85f), "NumberOrb");
                temp.Add(orb);
                yield return new WaitForSeconds(0.12f);
            }
            yield return Nod(1, 10f);
            yield return new WaitForSeconds(1.4f);
            StopTemp();
        }

        private IEnumerator PointAt(string target)
        {
            Vector3? where = null;
            var info = Game.Instance?.Service<IResidentDirectory>()?.Get(target);
            if (info != null && info.Transform != null) where = info.Position;
            float from = 0f, to = 0f;
            if (where.HasValue)
            {
                var d = where.Value - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f)
                {
                    var local = transform.InverseTransformDirection(d.normalized);
                    to = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg - yaw;
                }
            }
            for (float t = 0; t < 0.3f; t += UnityEngine.Time.deltaTime)
            {
                body.localRotation = Quaternion.Euler(0f, Mathf.LerpAngle(from, to, t / 0.3f), 0f);
                yield return null;
            }
            yield return EarPoint(earR, 0.9f);
            body.localRotation = Quaternion.identity;
            if (!where.HasValue && ItemRegistry.Exists(target)) yield return ShowItem(target);
        }

        /// <summary>差し出した物：その物の色の小さな箱を持って見せる。</summary>
        private IEnumerator ShowItem(string itemId)
        {
            var def = ItemRegistry.Get(itemId);
            var color = def != null ? def.Color : new Color(0.9f, 0.9f, 0.9f);
            var shape = def != null ? def.Shape : PrimitiveType.Cube;
            var obj = PrimitiveBuilder.Part(body, shape, new Vector3(0f, 0.2f, 0.42f), Vector3.one * 0.18f, color, "HeldItem");
            temp.Add(obj);
            yield return Nod(1, 18f);
            yield return new WaitForSeconds(1.0f);
            StopTemp();
        }

        private void SpawnTears()
        {
            var a = PrimitiveBuilder.EmissivePart(body, PrimitiveType.Sphere, new Vector3(-0.15f, 0.32f, 0.3f), new Vector3(0.05f, 0.08f, 0.05f), new Color(0.5f, 0.8f, 1f), "Tear");
            var b = PrimitiveBuilder.EmissivePart(body, PrimitiveType.Sphere, new Vector3(0.15f, 0.32f, 0.3f), new Vector3(0.05f, 0.08f, 0.05f), new Color(0.5f, 0.8f, 1f), "Tear");
            temp.Add(a);
            temp.Add(b);
        }

        private IEnumerator HappyHop()
        {
            yield return Hop(1, 0.15f);
            yield return EarWave(earR, 1);
        }
    }
}
