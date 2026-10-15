using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// エンディングで映し出す「プレイヤーが書いた辞書」（メインストーリー最終章）。
    /// ゲームは正解を書かない。表示するのは、宇宙語の表面形と、プレイヤー自身が辞書に書いた推測訳だけ。
    /// 推測訳は ITabletService.GetPlayerGloss から取得し、語の一覧は辞書の書き出しJSONと最近聞いた発話から集める。
    /// </summary>
    public sealed class DictionaryPage
    {
        public struct Entry
        {
            public string surface;
            public string gloss;
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public readonly List<string> HeardOnly = new List<string>();
        public int TotalGlossed;
        public string CatchphraseSurface;
        public string CatchphraseGloss;
        public string PlanetName;
        public float Alpha;

        private static readonly Regex SurfaceRegex = new Regex("\"(?:surface|word|spelling)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        public static DictionaryPage Collect(IEnumerable<UtteranceRecord> extraRecords, string catchphraseConcept)
        {
            var page = new DictionaryPage();
            var game = Game.Instance;
            var tablet = game != null ? game.Service<ITabletService>() : null;
            var speech = game != null ? game.Service<ISpeechService>() : null;
            page.PlanetName = game != null && game.Planet != null && !string.IsNullOrEmpty(game.Planet.playerGivenName) ? game.Planet.playerGivenName : null;
            if (string.IsNullOrEmpty(page.PlanetName) && speech != null)
            {
                try { page.PlanetName = speech.PlanetName; } catch (System.Exception) { }
            }

            var surfaces = new List<string>();
            var seen = new HashSet<string>();
            void AddSurface(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                s = s.Trim();
                if (s.Length == 0 || !seen.Add(s)) return;
                surfaces.Add(s);
            }

            // 1. 辞書の書き出し（形式は Tablet 担当の実装による。"surface" 等のキーを拾う）
            if (tablet != null)
            {
                string json = null;
                try { json = tablet.ExportDictionary(null); } catch (System.Exception e) { Debug.LogException(e); }
                if (!string.IsNullOrEmpty(json))
                {
                    foreach (Match m in SurfaceRegex.Matches(json)) AddSurface(Regex.Unescape(m.Groups[1].Value));
                }
            }
            // 2. 見送りの言葉など、この場面で聞いた発話
            if (extraRecords != null)
                foreach (var rec in extraRecords) AddTokens(rec, AddSurface);
            // 3. 最近聞いた発話
            if (speech != null)
            {
                try
                {
                    var recent = speech.RecentRecords;
                    if (recent != null) for (int i = recent.Count - 1; i >= 0; i--) AddTokens(recent[i], AddSurface);
                }
                catch (System.Exception) { }
            }

            foreach (var s in surfaces)
            {
                string g = null;
                if (tablet != null)
                {
                    try { g = tablet.GetPlayerGloss(s); } catch (System.Exception) { }
                }
                if (!string.IsNullOrEmpty(g))
                {
                    page.TotalGlossed++;
                    if (page.Entries.Count < StoryTuning.EndingDictionaryWords) page.Entries.Add(new Entry { surface = s, gloss = g });
                }
                else if (page.HeardOnly.Count < StoryTuning.EndingDictionaryWords)
                {
                    page.HeardOnly.Add(s);
                }
            }

            if (speech != null && !string.IsNullOrEmpty(catchphraseConcept))
            {
                try { page.CatchphraseSurface = speech.RenderWord(catchphraseConcept); } catch (System.Exception) { }
                if (!string.IsNullOrEmpty(page.CatchphraseSurface) && tablet != null)
                {
                    try { page.CatchphraseGloss = tablet.GetPlayerGloss(page.CatchphraseSurface); } catch (System.Exception) { }
                }
            }
            return page;
        }

        private static void AddTokens(UtteranceRecord rec, System.Action<string> add)
        {
            if (rec == null || rec.tokens == null) return;
            foreach (var t in rec.tokens)
            {
                if (t == null || t.grammatical) continue;
                add(t.surface);
            }
        }

        /// <summary>手帳のページとして描く（主人公の手元の辞書）。</summary>
        public void Draw()
        {
            if (Alpha <= 0.001f) return;
            float vw = UIStyles.VirtualWidth;
            float vh = UIStyles.VirtualHeight;
            float w = Mathf.Min(980f, vw - 120f);
            float h = Mathf.Min(760f, vh - 200f);
            var r = new Rect((vw - w) * 0.5f, (vh - h) * 0.5f, w, h);
            var prev = GUI.color;
            GUI.color = new Color(1, 1, 1, Alpha);
            UIStyles.FillRect(new Rect(r.x + 10, r.y + 12, r.width, r.height), new Color(0, 0, 0, 0.35f * Alpha));
            UIStyles.FillRect(r, new Color(0.97f, 0.94f, 0.86f, Alpha));
            UIStyles.FillRect(new Rect(r.x + 70, r.y, 3, r.height), new Color(0.9f, 0.55f, 0.55f, 0.6f * Alpha));
            for (float y = r.y + 90; y < r.yMax - 20; y += 44)
                UIStyles.FillRect(new Rect(r.x + 20, y, r.width - 40, 1), new Color(0.55f, 0.65f, 0.85f, 0.35f * Alpha));

            var ink = new Color(0.18f, 0.2f, 0.32f, Alpha);
            var title = new GUIStyle(UIStyles.LabelTitle) { fontSize = 30 };
            title.normal.textColor = ink;
            GUI.Label(new Rect(r.x + 90, r.y + 24, w - 120, 44),
                string.IsNullOrEmpty(PlanetName) ? Loc.T("わたしの辞書", "My dictionary") : Loc.Format("わたしの辞書 ― {0}", "My dictionary - {0}", PlanetName), title);

            var alien = new GUIStyle(UIStyles.LabelAlien) { wordWrap = false };
            alien.normal.textColor = new Color(0.15f, 0.42f, 0.38f, Alpha);
            var gloss = new GUIStyle(UIStyles.Label) { wordWrap = false };
            gloss.normal.textColor = ink;
            var dim = new GUIStyle(UIStyles.LabelSmall) { wordWrap = true };
            dim.normal.textColor = new Color(0.35f, 0.35f, 0.42f, Alpha);

            float rowY = r.y + 96;
            float colW = (w - 120) * 0.5f;
            if (Entries.Count > 0)
            {
                for (int i = 0; i < Entries.Count; i++)
                {
                    int col = i % 2;
                    int row = i / 2;
                    float x = r.x + 90 + col * colW;
                    float y = rowY + row * 44;
                    if (y > r.yMax - 150) break;
                    GUI.Label(new Rect(x, y, colW * 0.48f, 40), Entries[i].surface, alien);
                    GUI.Label(new Rect(x + colW * 0.5f, y + 4, colW * 0.5f - 10, 40), "… " + Entries[i].gloss, gloss);
                }
            }
            else
            {
                GUI.Label(new Rect(r.x + 90, rowY, w - 140, 90),
                    Loc.T("まだ白いページが多い。それでも、聞いた言葉の響きは覚えている。", "Many pages are still blank. Still, you remember how the words sounded."), dim);
                float x = r.x + 90;
                float y = rowY + 96;
                int n = 0;
                foreach (var s in HeardOnly)
                {
                    if (n >= 8) break;
                    GUI.Label(new Rect(x + (n % 2) * colW, y + (n / 2) * 44, colW - 10, 40), s, alien);
                    n++;
                }
            }

            // あの言葉（案内役の口癖）
            if (!string.IsNullOrEmpty(CatchphraseSurface))
            {
                float y = r.yMax - 128;
                UIStyles.FillRect(new Rect(r.x + 80, y - 6, w - 120, 54), new Color(1f, 0.85f, 0.55f, 0.35f * Alpha));
                var big = new GUIStyle(UIStyles.LabelAlienLarge) { wordWrap = false };
                big.normal.textColor = new Color(0.55f, 0.3f, 0.1f, Alpha);
                GUI.Label(new Rect(r.x + 96, y, colW, 44), CatchphraseSurface, big);
                GUI.Label(new Rect(r.x + 96 + colW, y + 6, colW - 20, 40), "… " + (string.IsNullOrEmpty(CatchphraseGloss) ? "……" : CatchphraseGloss), gloss);
            }
            GUI.Label(new Rect(r.x + 90, r.yMax - 56, w - 120, 36),
                Loc.Format("この星で書いた言葉：{0}語", "Words you wrote on this planet: {0}", TotalGlossed), dim);
            GUI.color = prev;
        }
    }
}
