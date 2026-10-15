using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>内蔵アプリの基底（戻る操作・状態リセットを持つ）。</summary>
    public abstract class TabletAppBase : ITabletApp
    {
        internal readonly TabletSystem Sys;
        protected Vector2 scroll;

        internal TabletAppBase(TabletSystem sys) { Sys = sys; }

        public abstract string AppId { get; }
        public abstract string Title { get; }
        public abstract int Order { get; }
        public abstract string IconGlyph { get; }
        public abstract Color IconColor { get; }
        public virtual bool IsAvailable => true;
        public virtual void OnOpen() { }
        public abstract void Draw(Rect area);

        /// <summary>アプリ内の「戻る」。処理したら true（false ならホームへ戻る）。</summary>
        public virtual bool TryGoBack() => false;
        /// <summary>星を離れる時に画面状態を初期化。</summary>
        public virtual void ResetState() { scroll = Vector2.zero; }

        protected static bool Button(string label, float width = 0f, float height = 40f)
        {
            return width > 0 ? GUILayout.Button(label, UIStyles.ButtonSmall, GUILayout.Width(width), GUILayout.Height(height))
                             : GUILayout.Button(label, UIStyles.ButtonSmall, GUILayout.Height(height));
        }
    }

    /// <summary>
    /// タブレット本体の画面（モーダル）。ホーム画面にアプリのアイコンを並べ、戻る・ホーム操作を持つ（§2.2）。
    /// ソロでは開いている間は時間が止まる（TimeSystem.PauseInSoloWhenTabletOpen。マルチでは止めない）。
    /// 外部アプリの Draw(area) には (0,0) 起点のローカル矩形を渡し、GUILayout.BeginArea で囲んで呼ぶ。
    /// </summary>
    public sealed class TabletWindow : UIWindow
    {
        private readonly TabletSystem sys;
        private ITabletApp current;
        private readonly List<string> history = new List<string>();
        private Vector2 homeScroll;

        internal TabletWindow(TabletSystem sys) { this.sys = sys; }

        public string CurrentAppId => current?.AppId;

        public override string Title => current != null ? Loc.Format("タブレット ― {0}", "Tablet - {0}", current.Title) : Loc.T("タブレット", "Tablet");
        public override bool IsModal => true;
        public override bool PausesTimeInSolo => Game.Instance?.Time == null || Game.Instance.Time.PauseInSoloWhenTabletOpen;
        public override Vector2 Size => new Vector2(Mathf.Clamp(UIStyles.VirtualWidth - 60f, 900f, 1660f), Mathf.Clamp(UIStyles.VirtualHeight - 40f, 640f, 990f));

        public override void OnClose()
        {
            sys.NotifyClosed();
        }

        /// <summary>アプリを開く。未登録・利用不可ならホーム。</summary>
        public void Navigate(string appId)
        {
            var app = sys.FindApp(appId);
            if (app == null || !app.IsAvailable)
            {
                if (!string.IsNullOrEmpty(appId) && appId != "home" && app == null)
                    Debug.Log($"[Tablet] アプリ {appId} は登録されていないのでホームを開きます");
                GoHome();
                return;
            }
            if (current == app)
            {
                SafeOnOpen(app);
                return;
            }
            history.Add(current != null ? current.AppId : null);
            if (history.Count > 20) history.RemoveAt(0);
            current = app;
            scroll = Vector2.zero;
            SafeOnOpen(app);
        }

        private static void SafeOnOpen(ITabletApp app)
        {
            try { app.OnOpen(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void GoHome()
        {
            current = null;
            history.Clear();
        }

        public void GoBack()
        {
            if (current is TabletAppBase b && b.TryGoBack()) return;
            while (history.Count > 0)
            {
                var id = history[history.Count - 1];
                history.RemoveAt(history.Count - 1);
                if (id == null)
                {
                    current = null;
                    return;
                }
                var app = sys.FindApp(id);
                if (app != null && app.IsAvailable && app != current)
                {
                    current = app;
                    return;
                }
            }
            current = null;
        }

        public override void DrawContents(Rect content)
        {
            TabletGui.Ensure();
            // ---- 上部バー ----
            GUILayout.BeginHorizontal(GUILayout.Height(44));
            if (GUILayout.Button(Loc.T("⌂ ホーム", "⌂ Home"), UIStyles.ButtonSmall, GUILayout.Width(130), GUILayout.Height(40))) GoHome();
            GUI.enabled = current != null;
            if (GUILayout.Button(Loc.T("← 戻る", "← Back"), UIStyles.ButtonSmall, GUILayout.Width(120), GUILayout.Height(40))) GoBack();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            var clock = Game.Instance?.Time;
            if (clock != null)
            {
                string paused = clock.IsPaused ? Loc.T("（時間停止中）", " (paused)") : "";
                GUILayout.Label($"{clock.FormatDate()} {clock.FormatClock()}{paused}", UIStyles.LabelDim, GUILayout.Height(40));
            }
            GUILayout.EndHorizontal();

            var area = new Rect(0, 50, content.width, content.height - 50);
            if (current == null)
            {
                DrawHome(area);
                return;
            }
            GUILayout.BeginArea(area);
            try
            {
                current.Draw(new Rect(0, 0, area.width, area.height));
            }
            catch (Exception e)
            {
                if (e is ExitGUIException) throw;
                Debug.LogException(e);
            }
            finally
            {
                GUILayout.EndArea();
            }
        }

        private void DrawHome(Rect area)
        {
            GUILayout.BeginArea(area);
            homeScroll = GUILayout.BeginScrollView(homeScroll);
            var apps = sys.GetApps();
            const float cell = 168f;
            const float icon = 128f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((area.width - 30f) / cell));
            int rows = Mathf.CeilToInt(apps.Count / (float)cols);
            var grid = GUILayoutUtility.GetRect(cols * cell, rows * (cell + 26f));
            for (int i = 0; i < apps.Count; i++)
            {
                var app = apps[i];
                float x = grid.x + (i % cols) * cell + (cell - icon) * 0.5f;
                float y = grid.y + (i / cols) * (cell + 26f) + 6f;
                var r = new Rect(x, y, icon, icon);
                bool avail = SafeAvailable(app);
                var col = SafeColor(app);
                if (!avail) col = Color.Lerp(col, Color.gray, 0.7f);
                UIStyles.FillRect(new Rect(r.x + 4, r.y + 6, r.width, r.height), new Color(0, 0, 0, 0.35f));
                UIStyles.FillRect(r, col);
                UIStyles.FillRect(new Rect(r.x, r.y, r.width, r.height * 0.18f), new Color(1, 1, 1, 0.12f));
                GUI.Label(r, SafeGlyph(app), TabletGui.Glyph);
                GUI.Label(new Rect(x - 20, y + icon + 4, icon + 40, 48), SafeTitle(app), TabletGui.AppTitle);
                GUI.enabled = avail;
                if (GUI.Button(new Rect(r.x, r.y, r.width, r.height + 40), GUIContent.none, GUIStyle.none)) Navigate(app.AppId);
                GUI.enabled = true;
            }
            GUILayout.Space(16);
            var k = sys.Know;
            GUILayout.Label(Loc.Format("辞書 {0}語 ／ 音声カード {1}枚 ／ 住民辞典 {2}人 ／ 資料 {3}件 ／ 写真 {4}枚",
                "Dictionary {0} / Voice cards {1} / Residents {2} / Materials {3} / Photos {4}",
                k.dictionary.Count, k.cards.Count, k.residents.Count, k.materials.Count, k.photos.Count), UIStyles.LabelDim);
            GUILayout.Label(Loc.T("このタブレットは翻訳機ではありません。聞いた言葉や見たことを記録して、意味は自分で考えます。宇宙語の単語を押すと辞書が開きます。",
                "This tablet is not a translator. Record what you hear and see, and work out the meanings yourself. Tap an alien word to open the dictionary."), TabletGui.SmallDim);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static bool SafeAvailable(ITabletApp a) { try { return a.IsAvailable; } catch { return false; } }
        private static Color SafeColor(ITabletApp a) { try { return a.IconColor; } catch { return Color.gray; } }
        private static string SafeGlyph(ITabletApp a) { try { return a.IconGlyph ?? "?"; } catch { return "?"; } }
        private static string SafeTitle(ITabletApp a) { try { return a.Title ?? a.AppId; } catch { return a.AppId; } }
    }
}
