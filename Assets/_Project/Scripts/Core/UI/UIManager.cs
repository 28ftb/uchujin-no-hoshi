using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.UI
{
    /// <summary>
    /// IMGUI の全体管理。Game.OnGUI から DrawAll() が呼ばれる。
    /// ・ウィンドウ（スタック）・HUD・オーバーレイ・トースト・操作プロンプト・ワールドラベル・フェード
    /// </summary>
    public static class UIManager
    {
        private static readonly List<UIWindow> windows = new List<UIWindow>();
        private static readonly List<IUIOverlay> overlays = new List<IUIOverlay>();
        private static readonly List<ToastEntry> toasts = new List<ToastEntry>();
        private static string prompt;
        private static int promptFrame = -1;

        /// <summary>HUD を表示するか（タイトル画面・カットシーンでは false）。</summary>
        public static bool HudVisible = true;
        public static HudRenderer Hud = new HudRenderer();

        private class ToastEntry
        {
            public string text;
            public float until;
        }

        public static IReadOnlyList<UIWindow> OpenWindows => windows;

        public static bool AnyModalOpen
        {
            get
            {
                foreach (var w in windows) if (w.IsModal) return true;
                return false;
            }
        }

        /// <summary>ゲーム操作（移動・道具・インタラクト）を止めるべきか。</summary>
        public static bool BlocksGameplay => AnyModalOpen || TextFieldFocused;

        public static bool TextFieldFocused => GUIUtility.keyboardControl != 0;

        public static void Open(UIWindow w)
        {
            if (w == null) return;
            if (windows.Contains(w))
            {
                windows.Remove(w);
                windows.Add(w);
                return;
            }
            windows.Add(w);
            w.IsOpen = true;
            if (w.PausesTimeInSolo) Game.Instance?.Time?.AddPersonalPause("ui:" + w.GetHashCode());
            try { w.OnOpen(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void Close(UIWindow w)
        {
            if (w == null || !windows.Remove(w)) return;
            w.IsOpen = false;
            Game.Instance?.Time?.RemovePersonalPause("ui:" + w.GetHashCode());
            GUIUtility.keyboardControl = 0;
            try { w.OnClose(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void Toggle(UIWindow w)
        {
            if (w == null) return;
            if (w.IsOpen) Close(w);
            else Open(w);
        }

        public static void CloseTop()
        {
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                if (windows[i].CloseOnEscape)
                {
                    Close(windows[i]);
                    return;
                }
            }
        }

        public static void CloseAll()
        {
            for (int i = windows.Count - 1; i >= 0; i--) Close(windows[i]);
        }

        public static T Find<T>() where T : UIWindow
        {
            foreach (var w in windows) if (w is T t) return t;
            return null;
        }

        public static void RegisterOverlay(IUIOverlay overlay)
        {
            if (overlay == null || overlays.Contains(overlay)) return;
            overlays.Add(overlay);
            overlays.Sort((a, b) => a.OverlayOrder.CompareTo(b.OverlayOrder));
        }

        public static void UnregisterOverlay(IUIOverlay overlay) => overlays.Remove(overlay);

        public static void Toast(string text, float seconds = 3.5f)
        {
            if (string.IsNullOrEmpty(text)) return;
            toasts.Add(new ToastEntry { text = text, until = UnityEngine.Time.unscaledTime + seconds });
            if (toasts.Count > 6) toasts.RemoveAt(0);
            Debug.Log("[Toast] " + text);
        }

        /// <summary>そのフレームの操作プロンプト（PlayerInteractor が毎フレーム設定）。</summary>
        public static void SetPrompt(string text)
        {
            prompt = text;
            promptFrame = UnityEngine.Time.frameCount;
        }

        // ---- ダイアログのショートカット ----

        public static void Confirm(string title, string message, Action onYes, Action onNo = null)
        {
            Open(new ChoiceDialog(title, message, new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("はい", "Yes"), onYes),
                new ChoiceOption(Loc.T("いいえ", "No"), onNo)
            }));
        }

        public static void Choose(string title, string message, List<ChoiceOption> options)
        {
            Open(new ChoiceDialog(title, message, options));
        }

        public static void Message(string title, string message)
        {
            Open(new ChoiceDialog(title, message, new List<ChoiceOption> { new ChoiceOption("OK", null) }));
        }

        public static void PickItem(PlayerState player, string title, Func<ItemStack, bool> filter, Action<int, ItemStack> onPicked)
        {
            Open(new ItemPickerWindow(player, title, filter, onPicked));
        }

        public static void InputText(string title, string message, string initial, Action<string> onDone, bool multiline = false)
        {
            Open(new TextInputDialog(title, message, initial, onDone, multiline));
        }

        // ---- 描画 ----

        public static void DrawAll()
        {
            UIStyles.BeginVirtual();
            var e = Event.current;

            if (HudVisible && Game.Instance != null && Game.Instance.State == GameState.Playing)
            {
                WorldLabels.Draw();
                Hud.Draw(promptFrame >= UnityEngine.Time.frameCount - 1 ? prompt : null);
            }

            for (int i = 0; i < overlays.Count; i++)
            {
                try { overlays[i].DrawOverlay(); }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            if (windows.Count > 0)
            {
                // モーダル背景
                if (AnyModalOpen) UIStyles.FillRect(new Rect(0, 0, UIStyles.VirtualWidth, UIStyles.VirtualHeight), new Color(0, 0, 0, 0.35f));
                var snapshot = windows.ToArray();
                for (int i = 0; i < snapshot.Length; i++)
                {
                    var w = snapshot[i];
                    if (!w.IsOpen) continue;
                    bool isTop = i == snapshot.Length - 1;
                    GUI.enabled = isTop || !w.IsModal;
                    DrawWindow(w);
                    GUI.enabled = true;
                }
            }

            DrawToasts();
            ScreenFader.Draw();
            // Esc による閉じる操作は Game.Update 側で処理する（開く・閉じるの二重処理を防ぐ）
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) e.Use();
        }

        private static void DrawWindow(UIWindow w)
        {
            var r = w.GetRect();
            GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
            var titleRect = new Rect(r.x + 16, r.y + 8, r.width - 80, 40);
            if (!string.IsNullOrEmpty(w.Title)) GUI.Label(titleRect, w.Title, UIStyles.LabelTitle);
            if (w.ShowCloseButton && GUI.Button(new Rect(r.xMax - 56, r.y + 10, 44, 36), "×", UIStyles.Button)) Close(w);
            float top = string.IsNullOrEmpty(w.Title) ? 12 : 52;
            var content = new Rect(r.x + 16, r.y + top, r.width - 32, r.height - top - 12);
            GUILayout.BeginArea(content);
            try
            {
                w.DrawContents(new Rect(0, 0, content.width, content.height));
            }
            catch (Exception ex)
            {
                if (ex is ExitGUIException) throw;
                Debug.LogException(ex);
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private static void DrawToasts()
        {
            float now = UnityEngine.Time.unscaledTime;
            toasts.RemoveAll(t => t.until < now);
            float y = 140;
            float w = 520;
            float x = UIStyles.VirtualWidth - w - 20;
            foreach (var t in toasts)
            {
                float h = UIStyles.Toast.CalcHeight(new GUIContent(t.text), w);
                GUI.Box(new Rect(x, y, w, h), t.text, UIStyles.Toast);
                y += h + 6;
            }
        }

        public static void UpdateWindows()
        {
            foreach (var w in windows.ToArray())
            {
                try { w.Update(); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        public static void ResetAll()
        {
            CloseAll();
            overlays.Clear();
            toasts.Clear();
            WorldLabels.Clear();
        }
    }

    /// <summary>画面のフェード（エリア移動・睡眠・カットシーン）。</summary>
    public static class ScreenFader
    {
        private static float alpha;
        private static float target;
        private static float speed = 2f;

        public static float Alpha => alpha;

        public static void FadeOut(float seconds) { target = 1f; speed = 1f / Mathf.Max(0.01f, seconds); }
        public static void FadeIn(float seconds) { alpha = Mathf.Max(alpha, 0.85f); target = 0f; speed = 1f / Mathf.Max(0.01f, seconds); }
        public static void SetBlack() { alpha = 1f; target = 1f; }

        public static void Draw()
        {
            if (Event.current.type == EventType.Repaint) alpha = Mathf.MoveTowards(alpha, target, speed * UnityEngine.Time.unscaledDeltaTime);
            if (alpha <= 0.001f) return;
            UIStyles.FillRect(new Rect(0, 0, UIStyles.VirtualWidth, UIStyles.VirtualHeight), new Color(0, 0, 0, alpha));
        }
    }

    /// <summary>ワールド上の文字ラベル（看板・プレイヤー名・住民辞典のあだ名など）。</summary>
    public static class WorldLabels
    {
        private class Entry
        {
            public Transform target;
            public Func<string> text;
            public Vector3 offset;
            public Color color;
            public float maxDistance;
            public GUIStyle style;
        }

        private static readonly List<Entry> entries = new List<Entry>();

        public static void Add(Transform target, Func<string> text, Vector3 offset, Color color, float maxDistance = 25f, bool alien = false)
        {
            entries.Add(new Entry { target = target, text = text, offset = offset, color = color, maxDistance = maxDistance, style = alien ? UIStyles.LabelAlien : null });
        }

        public static void Remove(Transform target) => entries.RemoveAll(e => e.target == target);
        public static void Clear() => entries.Clear();

        public static void Draw()
        {
            var cam = Camera.main;
            if (cam == null) return;
            entries.RemoveAll(e => e.target == null);
            foreach (var e in entries)
            {
                if (!e.target.gameObject.activeInHierarchy) continue;
                var wp = e.target.position + e.offset;
                if ((wp - cam.transform.position).sqrMagnitude > e.maxDistance * e.maxDistance) continue;
                var vp = UIStyles.WorldToVirtual(wp);
                if (!vp.HasValue) continue;
                string t = e.text?.Invoke();
                if (string.IsNullOrEmpty(t)) continue;
                var style = new GUIStyle(e.style ?? UIStyles.LabelSmall) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                style.normal.textColor = e.color;
                var size = style.CalcSize(new GUIContent(t));
                var rect = new Rect(vp.Value.x - size.x * 0.5f - 6, vp.Value.y - size.y * 0.5f, size.x + 12, size.y + 2);
                UIStyles.FillRect(rect, new Color(0, 0, 0, 0.45f));
                GUI.Label(rect, t, style);
            }
        }
    }
}
