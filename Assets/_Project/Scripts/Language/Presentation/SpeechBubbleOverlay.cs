using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Language
{
    /// <summary>表示中の吹き出し1つ。</summary>
    public sealed class SpeechBubble
    {
        public UtteranceRecord Record;
        public Transform Anchor;
        public Vector3 Position;
        /// <summary>話者の位置が無い（画面下の字幕として出す）。</summary>
        public bool Subtitle;
        public SpeechChannel Channel;
        public float Radius;
        public float Born;
        public float Until;
        public float AnchorHeight = 2.2f;
        public Color Accent = UIStyles.Accent;
        /// <summary>会話ログに入れたか（ローカルプレイヤーが聞こえる距離に入った）。</summary>
        public bool Logged;
        public bool IsReplay;
        public bool IsRelay;
        /// <summary>宇宙文字で表示する（看板・手紙）。</summary>
        public bool Script;
        public string ScriptText;

        public Vector3 WorldPoint
        {
            get
            {
                if (Anchor != null) return Anchor.position + Vector3.up * AnchorHeight;
                return Position + Vector3.up * AnchorHeight;
            }
        }
    }

    /// <summary>
    /// 頭上の吹き出し（IUIOverlay）。宇宙語テキスト（発音表記）のみを表示し、
    /// 表情・身振りは文字化けしにくい記号（！？…♪ など）で添える。内部の意味（internalId / meaning）は表示しない。
    /// ・各単語をクリック → DictionaryLookupRequestEvent（タブレットが辞書を開く）。接辞も別の語として選べる。
    /// ・ITabletService.ShowGlossOverlay が true なら、各語の下にプレイヤー自身の推測訳を小さく併記（無い語は空欄。正解は補完しない）。
    /// ・住民同士の会話（Overheard）は小さめ。看板・手紙は宇宙文字（設定で読み方を併記）。
    /// </summary>
    public sealed class SpeechBubbleOverlay : IUIOverlay
    {
        // ---- 【仮数値】見た目 ----
        public const float MaxWidthDirect = 560f;
        public const float MaxWidthOverheard = 400f;
        public const int FontDirect = 24;
        public const int FontOverheard = 18;
        public const int FontGloss = 14;
        public const int FontSymbols = 20;
        public const int FontScript = 28;
        public const float Padding = 10f;
        public const float WordSpacing = 9f;
        public const float FadeSeconds = 0.35f;

        public static readonly Color BubbleBg = new Color(0.07f, 0.09f, 0.16f, 0.9f);
        public static readonly Color ScriptBg = new Color(0.20f, 0.16f, 0.10f, 0.92f);
        public static readonly Color HoverBg = new Color(0.35f, 0.55f, 0.85f, 0.55f);
        public static readonly Color GlossColor = new Color(1f, 0.82f, 0.5f, 1f);
        public static readonly Color ScriptColor = new Color(1f, 0.92f, 0.72f, 1f);

        public int OverlayOrder => 100;

        private readonly List<SpeechBubble> bubbles = new List<SpeechBubble>();
        private readonly List<Rect> placed = new List<Rect>();
        /// <summary>このフレームに描いた吹き出しの領域（仮想座標）。クリックが道具の使用と重ならないかの判定用。</summary>
        private static readonly List<Rect> lastFrameRects = new List<Rect>();

        private GUIStyle wordStyle, glossStyle, symbolStyle, scriptStyle;
        private int styleFont = -1;
        private float styleScale = -1f;

        public IReadOnlyList<SpeechBubble> Bubbles => bubbles;

        public void Add(SpeechBubble b)
        {
            if (b == null) return;
            // 同じ話者の古い吹き出しは置き換える
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                var o = bubbles[i];
                bool sameSpeaker = (b.Anchor != null && o.Anchor == b.Anchor)
                                   || (!string.IsNullOrEmpty(b.Record?.speakerId) && o.Record?.speakerId == b.Record.speakerId && o.Subtitle == b.Subtitle);
                if (sameSpeaker) bubbles.RemoveAt(i);
            }
            bubbles.Add(b);
            if (bubbles.Count > 24) bubbles.RemoveAt(0);
        }

        public void Clear()
        {
            bubbles.Clear();
            lastFrameRects.Clear();
        }

        public void RemoveExpired(float now)
        {
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                if (now > bubbles[i].Until) bubbles.RemoveAt(i);
            }
        }

        /// <summary>画面座標（Input.mousePosition 形式：左下原点）が吹き出しの上か。</summary>
        public static bool IsPointerOver(Vector2 screenPosition)
        {
            float s = UIStyles.Scale;
            var v = new Vector2(screenPosition.x / s, (Screen.height - screenPosition.y) / s);
            foreach (var r in lastFrameRects) if (r.Contains(v)) return true;
            return false;
        }

        // ------------------------------------------------------------
        // 描画
        // ------------------------------------------------------------

        public void DrawOverlay()
        {
            var game = Game.Instance;
            if (game == null) return;
            if (game.State != GameState.Playing && game.State != GameState.Cutscene && game.State != GameState.Ending) return;
            if (bubbles.Count == 0)
            {
                if (Event.current.type == EventType.Repaint) lastFrameRects.Clear();
                return;
            }
            EnsureStyles(FontDirect);
            var player = game.LocalPlayer;
            var cam = Camera.main;
            Vector3 listener = player != null ? player.Position : (cam != null ? cam.transform.position : Vector3.zero);
            float now = Time.unscaledTime;

            // 遠い順に描いて、近い吹き出しを上に重ねる
            var order = new List<SpeechBubble>(bubbles);
            order.Sort((a, b) =>
            {
                float da = a.Subtitle ? -1f : (a.WorldPoint - listener).sqrMagnitude;
                float db = b.Subtitle ? -1f : (b.WorldPoint - listener).sqrMagnitude;
                return db.CompareTo(da);
            });
            placed.Clear();
            if (Event.current.type == EventType.Repaint) lastFrameRects.Clear();
            float subtitleY = UIStyles.VirtualHeight - 170f;
            foreach (var b in order)
            {
                if (b.Record == null) continue;
                Vector2 anchorPt;
                if (b.Subtitle)
                {
                    anchorPt = new Vector2(UIStyles.VirtualWidth * 0.5f, subtitleY);
                }
                else
                {
                    var wp = b.WorldPoint;
                    if ((wp - listener).sqrMagnitude > b.Radius * b.Radius) continue; // 聞こえない距離では見えない（＝記録とも一致）
                    var vp = UIStyles.WorldToVirtual(wp);
                    if (!vp.HasValue) continue;
                    anchorPt = vp.Value;
                }
                float alpha = Mathf.Clamp01((b.Until - now) / FadeSeconds) * Mathf.Clamp01((now - b.Born) / 0.12f + 0.2f);
                var r = DrawBubble(b, anchorPt, alpha);
                if (b.Subtitle) subtitleY = r.y - 8f;
            }
        }

        private void EnsureStyles(int font)
        {
            float scale = LanguageSettings.BubbleScale;
            if (wordStyle != null && styleFont == font && Mathf.Approximately(styleScale, scale)) return;
            styleFont = font;
            styleScale = scale;
            wordStyle = new GUIStyle(UIStyles.LabelAlien) { wordWrap = false, alignment = TextAnchor.MiddleCenter, richText = false };
            wordStyle.padding = new RectOffset(2, 2, 1, 1);
            wordStyle.margin = new RectOffset(0, 0, 0, 0);
            wordStyle.hover.background = UIStyles.Tex(HoverBg);
            wordStyle.hover.textColor = Color.white;
            wordStyle.active.background = UIStyles.Tex(HoverBg);
            wordStyle.active.textColor = Color.white;
            wordStyle.normal.background = null;
            glossStyle = new GUIStyle(UIStyles.LabelSmall) { wordWrap = false, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Italic, richText = false };
            glossStyle.normal.textColor = GlossColor;
            glossStyle.padding = new RectOffset(1, 1, 0, 0);
            symbolStyle = new GUIStyle(UIStyles.LabelBold) { wordWrap = false, alignment = TextAnchor.MiddleLeft, richText = false };
            symbolStyle.normal.textColor = UIStyles.AccentWarm;
            scriptStyle = new GUIStyle(UIStyles.LabelAlienLarge) { wordWrap = false, alignment = TextAnchor.MiddleCenter, richText = false };
            scriptStyle.normal.textColor = ScriptColor;
            scriptStyle.hover.background = UIStyles.Tex(HoverBg);
        }

        /// <summary>1語（主語幹＋連結した接辞）のまとまり。各部分は別々に選べる。</summary>
        private sealed class WordUnit
        {
            public readonly List<int> Parts = new List<int>();
            public readonly List<float> PartWidths = new List<float>();
            public readonly List<string> Glosses = new List<string>();
            public string Trailing; // 文末記号
            public float Width;
            public float ScriptWidth;
            public string ScriptText;
        }

        private Rect DrawBubble(SpeechBubble b, Vector2 anchor, float alpha)
        {
            var rec = b.Record;
            bool small = b.Channel == SpeechChannel.Overheard;
            float scale = LanguageSettings.BubbleScale;
            int fontWord = Mathf.RoundToInt((small ? FontOverheard : FontDirect) * scale);
            int fontGloss = Mathf.RoundToInt((small ? FontGloss - 2 : FontGloss) * scale);
            wordStyle.fontSize = fontWord;
            glossStyle.fontSize = fontGloss;
            symbolStyle.fontSize = Mathf.RoundToInt((small ? FontSymbols - 4 : FontSymbols) * scale);
            scriptStyle.fontSize = Mathf.RoundToInt(FontScript * scale * (small ? 0.8f : 1f));
            float maxWidth = (small ? MaxWidthOverheard : MaxWidthDirect) * scale;

            var tablet = Game.Instance.Service<ITabletService>();
            bool showGloss = tablet != null && tablet.ShowGlossOverlay && !b.Script;
            bool showReading = !b.Script || LanguageSettings.ShowSignReading;
            var lang = LanguageSystem.Instance != null ? LanguageSystem.Instance.Language : null;

            // 語のまとまりを作る
            var units = new List<WordUnit>();
            var tokens = rec.tokens ?? new List<SurfaceToken>();
            var spans = SentenceRenderer.SplitSentences(rec.text, tokens);
            float wordH = wordStyle.CalcSize(new GUIContent("Ag")).y;
            float glossH = showGloss ? glossStyle.CalcSize(new GUIContent("Ag")).y : 0f;
            foreach (var span in spans)
            {
                WordUnit cur = null;
                for (int i = span.Start; i < span.End; i++)
                {
                    var tok = tokens[i];
                    if (cur == null || !tok.attachedToPrevious || i == span.Start)
                    {
                        cur = new WordUnit();
                        units.Add(cur);
                    }
                    cur.Parts.Add(i);
                    float w = wordStyle.CalcSize(new GUIContent(tok.surface)).x;
                    string gloss = null;
                    if (showGloss)
                    {
                        gloss = tablet.GetPlayerGloss(tok.surface);
                        if (!string.IsNullOrEmpty(gloss)) w = Mathf.Max(w, glossStyle.CalcSize(new GUIContent(gloss)).x);
                    }
                    cur.PartWidths.Add(w);
                    cur.Glosses.Add(gloss);
                    cur.Width += w;
                }
                if (cur != null && !string.IsNullOrEmpty(span.Punctuation))
                {
                    cur.Trailing = span.Punctuation;
                    cur.Width += wordStyle.CalcSize(new GUIContent(span.Punctuation)).x;
                }
            }
            if (b.Script && lang != null)
            {
                foreach (var u in units)
                {
                    var sb = new StringBuilder();
                    foreach (var p in u.Parts) sb.Append(lang.Script.Word(tokens[p].surface));
                    if (!string.IsNullOrEmpty(u.Trailing)) sb.Append(lang.Script.Text(u.Trailing));
                    u.ScriptText = sb.ToString();
                    u.ScriptWidth = scriptStyle.CalcSize(new GUIContent(u.ScriptText)).x;
                }
            }

            // 記号（表情・身振り）
            string symbols = LanguageSettings.ShowSymbols ? SpeechSymbols.Build(rec.expression, rec.gestures) : string.Empty;
            var itemColors = LanguageSettings.ShowSymbols ? SpeechSymbols.ItemColors(rec.gestures) : new List<Color>();
            float symH = 0f, symW = 0f;
            if (!string.IsNullOrEmpty(symbols) || itemColors.Count > 0)
            {
                var sz = string.IsNullOrEmpty(symbols) ? Vector2.zero : symbolStyle.CalcSize(new GUIContent(symbols));
                symW = sz.x + itemColors.Count * (symbolStyle.fontSize + 4f);
                symH = Mathf.Max(sz.y, symbolStyle.fontSize + 4f);
            }

            // 行に流し込む
            var lines = new List<List<WordUnit>>();
            var lineWidths = new List<float>();
            float scriptLineH = b.Script ? scriptStyle.CalcSize(new GUIContent("Ж")).y : 0f;
            float readingBlockH = showReading ? wordH + glossH : 0f;
            float unitH = (b.Script ? scriptLineH : 0f) + readingBlockH;
            {
                List<WordUnit> line = null;
                float lw = 0f;
                foreach (var u in units)
                {
                    float uw = Mathf.Max(showReading ? u.Width : 0f, b.Script ? u.ScriptWidth : 0f);
                    if (line == null || (lw + WordSpacing + uw > maxWidth && line.Count > 0))
                    {
                        if (line != null) { lines.Add(line); lineWidths.Add(lw); }
                        line = new List<WordUnit>();
                        lw = 0f;
                    }
                    if (line.Count > 0) lw += WordSpacing;
                    line.Add(u);
                    lw += uw;
                }
                if (line != null) { lines.Add(line); lineWidths.Add(lw); }
            }
            float contentW = symW;
            foreach (var w in lineWidths) contentW = Mathf.Max(contentW, w);
            float contentH = symH + lines.Count * (unitH + 4f);
            if (lines.Count == 0 && symH <= 0f) return new Rect(anchor.x, anchor.y, 0, 0); // 何も無い
            float bw = Mathf.Max(contentW + Padding * 2f, 60f);
            float bh = contentH + Padding * 2f;
            var rect = new Rect(anchor.x - bw * 0.5f, anchor.y - bh - 14f, bw, bh);
            rect = Avoid(rect);
            rect.x = Mathf.Clamp(rect.x, 8f, UIStyles.VirtualWidth - rect.width - 8f);
            rect.y = Mathf.Clamp(rect.y, 8f, UIStyles.VirtualHeight - rect.height - 8f);
            placed.Add(rect);
            if (Event.current.type == EventType.Repaint) lastFrameRects.Add(rect);

            // 背景・縁・しっぽ
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            var bg = b.Script ? ScriptBg : BubbleBg;
            UIStyles.FillRect(rect, bg);
            var accent = new Color(b.Accent.r, b.Accent.g, b.Accent.b, 0.95f * alpha);
            UIStyles.FillRect(new Rect(rect.x, rect.y, rect.width, 2f), accent);
            UIStyles.FillRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), accent);
            UIStyles.FillRect(new Rect(rect.x, rect.y, 2f, rect.height), accent);
            UIStyles.FillRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), accent);
            if (!b.Subtitle)
            {
                float tx = Mathf.Clamp(anchor.x, rect.x + 14f, rect.xMax - 14f);
                for (int i = 0; i < 4; i++)
                {
                    float w = 14f - i * 3.5f;
                    UIStyles.FillRect(new Rect(tx - w * 0.5f, rect.yMax + i * 3f, w, 3f), bg);
                }
            }

            float y = rect.y + Padding;
            // 記号の行
            if (symH > 0f)
            {
                float x = rect.x + Padding;
                if (!string.IsNullOrEmpty(symbols))
                {
                    var sz = symbolStyle.CalcSize(new GUIContent(symbols));
                    GUI.Label(new Rect(x, y, sz.x, symH), symbols, symbolStyle);
                    x += sz.x + 4f;
                }
                foreach (var c in itemColors)
                {
                    float s = symbolStyle.fontSize;
                    var box = new Rect(x, y + (symH - s) * 0.5f, s, s);
                    UIStyles.FillRect(new Rect(box.x - 1, box.y - 1, box.width + 2, box.height + 2), new Color(1, 1, 1, 0.8f * alpha));
                    UIStyles.FillRect(box, new Color(c.r, c.g, c.b, alpha));
                    x += s + 4f;
                }
                y += symH;
            }

            // 単語（クリックで辞書）
            for (int li = 0; li < lines.Count; li++)
            {
                float x = rect.x + (rect.width - lineWidths[li]) * 0.5f;
                foreach (var u in lines[li])
                {
                    float uw = Mathf.Max(showReading ? u.Width : 0f, b.Script ? u.ScriptWidth : 0f);
                    float yy = y;
                    if (b.Script)
                    {
                        var sr = new Rect(x + (uw - u.ScriptWidth) * 0.5f, yy, u.ScriptWidth, scriptLineH);
                        if (showReading)
                        {
                            if (GUI.Button(sr, u.ScriptText, scriptStyle)) Lookup(tokens[u.Parts[0]].surface, rec.recordId);
                        }
                        else GUI.Label(sr, u.ScriptText, scriptStyle); // 読み方を出さない設定では文字から読みを開示しない
                        yy += scriptLineH;
                    }
                    if (showReading)
                    {
                        float px = x + (uw - u.Width) * 0.5f;
                        for (int k = 0; k < u.Parts.Count; k++)
                        {
                            var tok = tokens[u.Parts[k]];
                            float pw = u.PartWidths[k];
                            var wr = new Rect(px, yy, pw, wordH);
                            if (GUI.Button(wr, tok.surface, wordStyle)) Lookup(tok.surface, rec.recordId);
                            if (showGloss && !string.IsNullOrEmpty(u.Glosses[k]))
                                GUI.Label(new Rect(px, yy + wordH - 2f, pw, glossH), u.Glosses[k], glossStyle);
                            px += pw;
                        }
                        if (!string.IsNullOrEmpty(u.Trailing))
                        {
                            float tw = wordStyle.CalcSize(new GUIContent(u.Trailing)).x;
                            GUI.Label(new Rect(px, yy, tw, wordH), u.Trailing, wordStyle);
                        }
                    }
                    x += uw + WordSpacing;
                }
                y += unitH + 4f;
            }
            GUI.color = prevColor;
            return rect;
        }

        private Rect Avoid(Rect r)
        {
            for (int tries = 0; tries < 4; tries++)
            {
                bool moved = false;
                foreach (var p in placed)
                {
                    if (p.Overlaps(r))
                    {
                        r.y = p.y - r.height - 6f;
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            return r;
        }

        private static void Lookup(string surface, string recordId)
        {
            if (string.IsNullOrEmpty(surface)) return;
            EventBus.Publish(new DictionaryLookupRequestEvent { Surface = surface, RecordId = recordId });
        }
    }

    /// <summary>
    /// 表情・身振りを表す記号（文字化けしにくいもの。WGL4 と JIS X 0208 の両方にある文字を中心に）。
    /// 言葉の意味ではなく「言葉以外の手掛かり」を補う（宇宙語仕様 §6.1 観察、企画仕様 §9.2）。記号の対応は【仮】。
    /// </summary>
    public static class SpeechSymbols
    {
        public static string ExpressionSymbol(Expression e)
        {
            switch (e)
            {
                case Expression.Happy: return "♪";
                case Expression.VeryHappy: return "♪♪";
                case Expression.Sad: return "…";
                case Expression.Crying: return "…;;";
                case Expression.Angry: return "#!";
                case Expression.Surprised: return "!!";
                case Expression.Confused: return "?";
                case Expression.Shy: return "///";
                case Expression.Sleepy: return "zzz";
                case Expression.Love: return "♥";
                case Expression.Worried: return ";…";
                case Expression.Excited: return "!♪";
                case Expression.Disgusted: return "×";
                case Expression.Thinking: return "…?";
                default: return "";
            }
        }

        public static string GestureSymbol(GestureCue g)
        {
            if (g == null) return "";
            switch (g.gesture)
            {
                case Gesture.Wave: return "~";
                case Gesture.Nod: return "↓↑";
                case Gesture.ShakeHead: return "←→";
                case Gesture.PointAt: return "→";
                case Gesture.ShowNumber:
                {
                    // 指で数を示す：●を並べる（10を超えたら ●×n）
                    int n = Mathf.Max(0, g.number);
                    if (n == 0) return "○";
                    if (n <= 10) return new string('●', n);
                    return "●×" + n;
                }
                case Gesture.Jump: return "↑!";
                case Gesture.Bow: return "↓";
                case Gesture.Shrug: return "~?";
                case Gesture.Hug: return "♥";
                case Gesture.OfferItem: return "→";
                case Gesture.Eat: return "♪";
                case Gesture.Cry: return ";;";
                case Gesture.Clap: return "!!";
                case Gesture.Spin: return "@";
                case Gesture.Think: return "…";
                case Gesture.Beckon: return "←";
                case Gesture.Shoo: return "→→";
                case Gesture.Hold: return "□";
                case Gesture.LookUp: return "↑";
                case Gesture.Shiver: return "≈";
                case Gesture.Stretch: return "~~";
                case Gesture.Dance: return "♪~";
                default: return "";
            }
        }

        public static string Build(Expression e, List<GestureCue> gestures)
        {
            var sb = new StringBuilder();
            sb.Append(ExpressionSymbol(e));
            if (gestures != null)
            {
                foreach (var g in gestures)
                {
                    var s = GestureSymbol(g);
                    if (string.IsNullOrEmpty(s)) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        /// <summary>差し出す・指さす対象がアイテムなら、その色の四角を示す（名前は出さない）。</summary>
        public static List<Color> ItemColors(List<GestureCue> gestures)
        {
            var list = new List<Color>();
            if (gestures == null) return list;
            foreach (var g in gestures)
            {
                if (g == null || string.IsNullOrEmpty(g.target)) continue;
                if (g.gesture != Gesture.OfferItem && g.gesture != Gesture.PointAt && g.gesture != Gesture.Hold) continue;
                var def = ItemRegistry.Get(g.target);
                if (def != null) list.Add(def.Color);
            }
            return list;
        }
    }
}
