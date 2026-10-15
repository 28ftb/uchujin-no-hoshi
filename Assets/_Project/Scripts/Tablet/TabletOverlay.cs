using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 画面に重ねる小さな表示：
    /// ・ピン留めした単語・カード（§11.1 ピン留め）
    /// ・話しかけられた直後の「[K] 音声カードに保存」の案内（§8.4）
    /// ・カメラのファインダー枠とフラッシュ（§11.3）
    /// </summary>
    internal sealed class TabletOverlay : IUIOverlay
    {
        private readonly TabletSystem sys;

        public TabletOverlay(TabletSystem sys) { this.sys = sys; }

        public int OverlayOrder => 120;

        public void DrawOverlay()
        {
            var g = Game.Instance;
            if (g == null || g.State != GameState.Playing) return;
            TabletGui.Ensure();
            float vw = UIStyles.VirtualWidth, vh = UIStyles.VirtualHeight;

            var cam = sys.PhotoCamera;
            if (cam != null && cam.Active) DrawViewfinder(vw, vh, cam);
            if (cam != null && UnityEngine.Time.unscaledTime < cam.FlashUntil)
                UIStyles.FillRect(new Rect(0, 0, vw, vh), new Color(1, 1, 1, Mathf.Clamp01((cam.FlashUntil - UnityEngine.Time.unscaledTime) / 0.25f) * 0.8f));
            if (sys.IsOpen || (cam != null && cam.Active)) return;

            if (TabletSettings.ShowPinnedOverlay) DrawPinned();

            if (sys.LastHeard != null && TabletSettings.QuickSaveKeyEnabled && !UIManager.AnyModalOpen)
            {
                string text = Loc.Format("[{0}] 今の声を音声カードに保存", "[{0}] Save that voice as a card", TabletTuning.QuickSaveKey);
                var size = UIStyles.LabelSmall.CalcSize(new GUIContent(text));
                var r = new Rect((vw - size.x) * 0.5f - 12, vh - 180, size.x + 24, 34);
                UIStyles.FillRect(r, new Color(0.05f, 0.07f, 0.12f, 0.75f));
                GUI.Label(new Rect(r.x + 12, r.y + 4, size.x + 4, 28), text, UIStyles.LabelSmall);
            }
        }

        private void DrawPinned()
        {
            float x = 16f, y = 230f, w = 330f;
            int shown = 0;
            foreach (var e in sys.Know.dictionary)
            {
                if (!e.pinned || shown >= TabletTuning.PinnedOverlayMax) continue;
                shown++;
                var r = new Rect(x, y, w, 52);
                UIStyles.FillRect(r, new Color(0.05f, 0.07f, 0.12f, 0.7f));
                GUI.Label(new Rect(x + 8, y + 2, w - 16, 28), e.surface, new GUIStyle(TabletGui.WordStatic) { fontSize = 20, wordWrap = false });
                string gloss = TabletSystem.GlossOf(e);
                GUI.Label(new Rect(x + 8, y + 26, w - 16, 24), string.IsNullOrEmpty(gloss) ? "…" : "≈ " + gloss, TabletGui.PlayerText);
                y += 56;
            }
            foreach (var n in sys.Know.cardNotes)
            {
                if (!n.pinned || shown >= TabletTuning.PinnedOverlayMax) continue;
                var card = sys.FindCard(n.cardId);
                if (card?.original == null) continue;
                shown++;
                var r = new Rect(x, y, w, 52);
                UIStyles.FillRect(r, new Color(0.08f, 0.05f, 0.1f, 0.7f));
                TabletGui.DrawFace(new Rect(x + 4, y + 6, 40, 40), sys.SpeakerColor(card.original.speakerId), sys.SpeakerLineage(card.original.speakerId), card.original.expression);
                GUI.Label(new Rect(x + 50, y + 2, w - 56, 28), TabletText.Truncate(card.original.text, 28), new GUIStyle(TabletGui.WordStatic) { fontSize = 18, wordWrap = false });
                GUI.Label(new Rect(x + 50, y + 26, w - 56, 24), string.IsNullOrEmpty(n.guessedMeaning) ? "…" : "≈ " + n.guessedMeaning, TabletGui.PlayerText);
                y += 56;
            }
        }

        private void DrawViewfinder(float vw, float vh, TabletCamera cam)
        {
            var c = new Color(1f, 1f, 1f, 0.85f);
            float m = 60f, len = 70f, t = 4f;
            // 四隅の枠
            UIStyles.FillRect(new Rect(m, m, len, t), c); UIStyles.FillRect(new Rect(m, m, t, len), c);
            UIStyles.FillRect(new Rect(vw - m - len, m, len, t), c); UIStyles.FillRect(new Rect(vw - m - t, m, t, len), c);
            UIStyles.FillRect(new Rect(m, vh - m - t, len, t), c); UIStyles.FillRect(new Rect(m, vh - m - len, t, len), c);
            UIStyles.FillRect(new Rect(vw - m - len, vh - m - t, len, t), c); UIStyles.FillRect(new Rect(vw - m - t, vh - m - len, t, len), c);
            // 中央の十字
            UIStyles.FillRect(new Rect(vw * 0.5f - 14, vh * 0.5f - 1, 28, 2), c);
            UIStyles.FillRect(new Rect(vw * 0.5f - 1, vh * 0.5f - 14, 2, 28), c);
            // 録画っぽい赤い点
            TabletGui.Disc(new Rect(vw - m - 30, m + 14, 14, 14), new Color(1f, 0.3f, 0.3f, 0.9f));
            if (!string.IsNullOrEmpty(cam.TargetResidentId))
            {
                string s = Loc.Format("{0} を撮ろう", "Photograph {0}", sys.ResidentLabel(cam.TargetResidentId));
                GUI.Label(new Rect(m + 10, vh - m - 44, 600, 32), s, UIStyles.Label);
            }
        }
    }
}
