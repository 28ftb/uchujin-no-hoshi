using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// 演出の進行役：カメラの制御・シネマ帯・宇宙語の字幕（翻訳なし）・身振りの案内・スキップ・画面のフラッシュ。
    /// プロローグとエンディングのコルーチンはこの MonoBehaviour 上で動く。
    /// 星のシーン（Game.SceneRoot）の下に置くので、星を離れると自動で破棄される。
    /// </summary>
    public class CutsceneDirector : MonoBehaviour, IUIOverlay
    {
        public int OverlayOrder => 150;

        // ---- カメラ ----
        private Camera cam;
        private bool cameraTaken;
        private Func<Vector3> camPos;
        private Func<Vector3> camLook;
        private float camSharpness = 3f;
        private float shakeAmount;
        private float shakeUntil;
        private bool snapNext;

        // ---- 画面 ----
        public bool Letterbox;
        private float letterbox01;
        private string caption;
        private float captionUntil;
        private string hint;
        private Transform marker;
        private float flash;
        private Color flashColor = Color.white;
        private string subtitleText;
        private string subtitleGloss;
        private Color subtitleColor = Color.white;
        private float subtitleUntil;
        /// <summary>演出固有の追加描画（タブレットの診断画面・辞書のページなど）。</summary>
        public Action ExtraDraw;

        // ---- スキップ ----
        public bool SkipEnabled;
        public bool SkipRequested { get; private set; }
        private float skipArmedUntil;

        public static CutsceneDirector Create(string name)
        {
            var go = new GameObject(name);
            if (Game.Instance != null && Game.Instance.SceneRoot != null) go.transform.SetParent(Game.Instance.SceneRoot, false);
            var d = go.AddComponent<CutsceneDirector>();
            UIManager.RegisterOverlay(d);
            return d;
        }

        private void OnDestroy()
        {
            UIManager.UnregisterOverlay(this);
            ReleaseCamera();
        }

        // ------------------------------------------------------------
        // カメラ
        // ------------------------------------------------------------

        public Camera Cam
        {
            get
            {
                if (cam == null) cam = Camera.main;
                return cam;
            }
        }

        /// <summary>プレイヤー追従カメラ（CameraRig）を止めて、演出側でカメラを動かす。</summary>
        public void TakeCamera()
        {
            if (CameraRig.Instance != null) CameraRig.Instance.enabled = false;
            cameraTaken = true;
            snapNext = true;
        }

        /// <summary>カメラをプレイヤー追従へ戻す。</summary>
        public void ReleaseCamera()
        {
            if (!cameraTaken) return;
            cameraTaken = false;
            camPos = null;
            camLook = null;
            if (CameraRig.Instance != null)
            {
                CameraRig.Instance.enabled = true;
                CameraRig.Instance.SnapToTarget();
            }
        }

        public bool CameraTaken => cameraTaken;

        public void Shot(Vector3 position, Vector3 lookAt, float sharpness = 3f, bool snap = false)
        {
            Shot(() => position, () => lookAt, sharpness, snap);
        }

        public void Shot(Func<Vector3> position, Func<Vector3> lookAt, float sharpness = 3f, bool snap = false)
        {
            if (!cameraTaken) TakeCamera();
            camPos = position;
            camLook = lookAt;
            camSharpness = sharpness;
            if (snap) snapNext = true;
        }

        public void Shake(float amount, float seconds)
        {
            shakeAmount = amount;
            shakeUntil = UnityEngine.Time.time + seconds;
        }

        private void LateUpdate()
        {
            if (!cameraTaken || camPos == null || Cam == null) return;
            Vector3 p, l;
            try
            {
                p = camPos();
                l = camLook != null ? camLook() : p + Vector3.forward;
            }
            catch (Exception)
            {
                return;
            }
            var t = Cam.transform;
            if (snapNext)
            {
                t.position = p;
                snapNext = false;
            }
            else
            {
                t.position = Vector3.Lerp(t.position, p, 1f - Mathf.Exp(-camSharpness * UnityEngine.Time.deltaTime));
            }
            var dir = l - t.position;
            if (dir.sqrMagnitude > 0.0001f)
            {
                var want = Quaternion.LookRotation(dir, Vector3.up);
                t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-camSharpness * 1.5f * UnityEngine.Time.deltaTime));
            }
            if (UnityEngine.Time.time < shakeUntil)
            {
                float k = (shakeUntil - UnityEngine.Time.time);
                t.position += UnityEngine.Random.insideUnitSphere * shakeAmount * Mathf.Clamp01(k);
            }
        }

        // ------------------------------------------------------------
        // 画面の表示
        // ------------------------------------------------------------

        /// <summary>短い説明（操作の案内・「数日後」など。会話の翻訳には使わない）。</summary>
        public void Caption(string text, float seconds = 3f)
        {
            caption = text;
            captionUntil = UnityEngine.Time.unscaledTime + seconds;
        }

        public void Hint(string text) => hint = text;
        public void Marker(Transform target) => marker = target;

        public void Flash(Color c, float strength = 1f)
        {
            flashColor = c;
            flash = Mathf.Clamp01(strength);
        }

        /// <summary>
        /// 宇宙語の字幕。翻訳はしない。タブレットで「推測訳を併記」が on のときだけ、
        /// プレイヤー自身が辞書に書いた推測をそのまま添える（誤っていてもそのまま）。
        /// </summary>
        public void ShowSpeech(UtteranceRecord rec, Color speakerColor, float seconds = 4.5f)
        {
            if (rec == null) return;
            subtitleText = rec.text;
            subtitleColor = speakerColor;
            subtitleUntil = UnityEngine.Time.unscaledTime + seconds;
            subtitleGloss = BuildPlayerGloss(rec);
        }

        public void ClearSpeech() => subtitleUntil = 0f;

        public static string BuildPlayerGloss(UtteranceRecord rec)
        {
            var tablet = Game.Instance != null ? Game.Instance.Service<ITabletService>() : null;
            if (tablet == null || !tablet.ShowGlossOverlay || rec == null || rec.tokens == null) return null;
            var sb = new StringBuilder();
            bool any = false;
            foreach (var tok in rec.tokens)
            {
                if (tok == null || string.IsNullOrEmpty(tok.surface)) continue;
                string g = null;
                try { g = tablet.GetPlayerGloss(tok.surface); }
                catch (Exception) { }
                if (sb.Length > 0 && !tok.attachedToPrevious) sb.Append(' ');
                if (!string.IsNullOrEmpty(g))
                {
                    any = true;
                    sb.Append('［').Append(g).Append('］');
                }
                else sb.Append('…');
            }
            return any ? sb.ToString() : null;
        }

        private void Update()
        {
            if (flash > 0f) flash = Mathf.MoveTowards(flash, 0f, UnityEngine.Time.unscaledDeltaTime * 1.4f);
            letterbox01 = Mathf.MoveTowards(letterbox01, Letterbox ? 1f : 0f, UnityEngine.Time.unscaledDeltaTime * 2f);

            if (SkipEnabled && !SkipRequested && !UIManager.AnyModalOpen && Game.Instance != null && Game.Instance.State != GameState.Playing)
            {
                if (GameInput.KeyDown(KeyCode.Escape) || GameInput.KeyDown(KeyCode.Return) || GameInput.KeyDown(KeyCode.Space))
                {
                    if (UnityEngine.Time.unscaledTime < skipArmedUntil) SkipRequested = true;
                    else skipArmedUntil = UnityEngine.Time.unscaledTime + 2f;
                }
            }
        }

        public void RequestSkip() => SkipRequested = true;
        public void ResetSkip() { SkipRequested = false; skipArmedUntil = 0f; }

        public void DrawOverlay()
        {
            float vw = UIStyles.VirtualWidth;
            float vh = UIStyles.VirtualHeight;

            if (letterbox01 > 0.001f)
            {
                float bh = StoryTuning.LetterboxHeight * letterbox01;
                UIStyles.FillRect(new Rect(0, 0, vw, bh), StoryTuning.CutsceneLetterbox);
                UIStyles.FillRect(new Rect(0, vh - bh, vw, bh), StoryTuning.CutsceneLetterbox);
            }

            if (marker != null && Game.Instance != null)
            {
                var vp = UIStyles.WorldToVirtual(marker.position + Vector3.up * 2.4f);
                if (vp.HasValue)
                {
                    float bob = Mathf.Sin(UnityEngine.Time.unscaledTime * 4f) * 6f;
                    var st = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 34 };
                    st.normal.textColor = UIStyles.AccentWarm;
                    GUI.Label(new Rect(vp.Value.x - 30, vp.Value.y - 30 + bob, 60, 50), "▼", st);
                }
            }

            try { ExtraDraw?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }

            if (!string.IsNullOrEmpty(subtitleText) && UnityEngine.Time.unscaledTime < subtitleUntil)
            {
                float w = Mathf.Min(1300f, vw - 80f);
                var main = new GUIStyle(UIStyles.LabelAlienLarge) { alignment = TextAnchor.MiddleCenter };
                float h1 = main.CalcHeight(new GUIContent(subtitleText), w - 40);
                float h2 = 0f;
                GUIStyle gs = null;
                if (!string.IsNullOrEmpty(subtitleGloss))
                {
                    gs = new GUIStyle(UIStyles.LabelSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
                    gs.normal.textColor = new Color(1f, 0.92f, 0.7f);
                    h2 = gs.CalcHeight(new GUIContent(subtitleGloss), w - 40) + 6;
                }
                float y = vh - StoryTuning.LetterboxHeight - h1 - h2 - 40;
                var r = new Rect((vw - w) * 0.5f, y, w, h1 + h2 + 24);
                UIStyles.FillRect(r, new Color(0, 0, 0, 0.55f));
                UIStyles.FillRect(new Rect(r.x, r.y, 8, r.height), subtitleColor);
                GUI.Label(new Rect(r.x + 20, r.y + 10, w - 40, h1), subtitleText, main);
                if (gs != null) GUI.Label(new Rect(r.x + 20, r.y + 14 + h1, w - 40, h2), subtitleGloss, gs);
            }

            if (!string.IsNullOrEmpty(caption) && UnityEngine.Time.unscaledTime < captionUntil)
            {
                float a = Mathf.Clamp01((captionUntil - UnityEngine.Time.unscaledTime) / 0.6f);
                var st = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 34 };
                st.normal.textColor = new Color(1f, 1f, 1f, a);
                GUI.Label(new Rect(0, StoryTuning.LetterboxHeight + 30, vw, 60), caption, st);
            }

            if (!string.IsNullOrEmpty(hint))
            {
                var st = new GUIStyle(UIStyles.LabelBold) { alignment = TextAnchor.MiddleCenter };
                var size = st.CalcSize(new GUIContent(hint));
                var r = new Rect((vw - size.x) * 0.5f - 18, vh - 210, size.x + 36, 46);
                GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
                GUI.Label(r, hint, st);
            }

            if (flash > 0.001f)
            {
                var c = flashColor;
                c.a = flash;
                UIStyles.FillRect(new Rect(0, 0, vw, vh), c);
            }

            if (SkipEnabled && !SkipRequested)
            {
                var r = new Rect(vw - 230, 20, 210, 44);
                if (GUI.Button(r, Loc.T("スキップ ▶▶", "Skip ▶▶"), UIStyles.ButtonSmall)) SkipRequested = true;
                if (UnityEngine.Time.unscaledTime < skipArmedUntil)
                    GUI.Label(new Rect(vw - 520, 70, 500, 30), Loc.T("もう一度押すとスキップします", "Press again to skip"), UIStyles.LabelDim);
            }
        }

        // ------------------------------------------------------------
        // 待機の補助（スキップされたら即座に抜ける）
        // ------------------------------------------------------------

        public IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds && !SkipRequested)
            {
                t += UnityEngine.Time.deltaTime;
                yield return null;
            }
        }

        public IEnumerator WaitUntil(Func<bool> condition, float timeout)
        {
            float t = 0f;
            while (t < timeout && !SkipRequested)
            {
                bool done = false;
                try { done = condition(); }
                catch (Exception) { done = true; }
                if (done) yield break;
                t += UnityEngine.Time.deltaTime;
                yield return null;
            }
        }

        public IEnumerator Fade(bool toBlack, float seconds)
        {
            if (toBlack) ScreenFader.FadeOut(seconds);
            else ScreenFader.FadeIn(seconds);
            float t = 0f;
            while (t < seconds)
            {
                t += UnityEngine.Time.unscaledDeltaTime;
                yield return null;
            }
        }
    }
}
