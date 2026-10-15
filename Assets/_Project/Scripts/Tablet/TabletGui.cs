using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// タブレット画面の IMGUI 部品（顔アイコン・宇宙語の単語の並び・線・写真の読み込み）。
    /// 宇宙語（原音の綴り）と、プレイヤーの推測訳は色・書体で必ず区別して表示する（タブレット仕様 §11.2）。
    /// </summary>
    internal static class TabletGui
    {
        public static GUIStyle Word, WordDiff, WordMarked, WordBoth, WordStatic, Gloss, Header, Small, SmallDim, Chip, ChipOn, CardBox, Glyph, AppTitle, FactLabel, PlayerText, Stamp;
        private static int stylesForScale = -1;
        private static Texture2D circle;

        public static readonly Color PlayerColor = new Color(1f, 0.82f, 0.5f);   // プレイヤーの記述の色
        public static readonly Color FactColor = new Color(0.72f, 0.86f, 1f);    // 観察できた事実の色
        public static readonly Color DiffColor = new Color(0.95f, 0.55f, 0.25f, 0.85f);
        public static readonly Color MarkColor = new Color(0.75f, 0.35f, 0.95f, 0.85f);

        public static void Ensure()
        {
            UIStyles.Ensure();
            int scale = TabletSettings.AlienTextScale;
            if (stylesForScale == scale && Word != null) return;
            stylesForScale = scale;
            int alienSize = scale == 0 ? 19 : scale == 2 ? 30 : 24;

            Word = new GUIStyle(UIStyles.LabelAlien) { wordWrap = false, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(6, 6, 2, 2), margin = new RectOffset(2, 2, 2, 2), fontSize = alienSize };
            Word.normal.background = UIStyles.Tex(new Color(1f, 1f, 1f, 0.04f));
            Word.hover.background = UIStyles.Tex(new Color(0.4f, 0.9f, 0.6f, 0.28f));
            Word.hover.textColor = Color.white;
            Word.active.background = UIStyles.Tex(new Color(0.4f, 0.9f, 0.6f, 0.45f));
            WordDiff = new GUIStyle(Word);
            WordDiff.normal.background = UIStyles.Tex(DiffColor * new Color(1, 1, 1, 0.55f));
            WordMarked = new GUIStyle(Word);
            WordMarked.normal.background = UIStyles.Tex(MarkColor * new Color(1, 1, 1, 0.6f));
            WordBoth = new GUIStyle(Word);
            WordBoth.normal.background = UIStyles.Tex(new Color(0.9f, 0.4f, 0.6f, 0.7f));
            WordStatic = new GUIStyle(UIStyles.LabelAlien) { fontSize = alienSize, wordWrap = true };

            Gloss = new GUIStyle(UIStyles.LabelSmall) { fontSize = 15, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Italic, wordWrap = false };
            Gloss.normal.textColor = PlayerColor;
            Header = new GUIStyle(UIStyles.LabelBold) { fontSize = 24 };
            Header.normal.textColor = UIStyles.Accent;
            Small = new GUIStyle(UIStyles.LabelSmall) { wordWrap = true };
            SmallDim = new GUIStyle(UIStyles.LabelDim) { wordWrap = true };
            Chip = new GUIStyle(UIStyles.ButtonSmall) { fontSize = 16, padding = new RectOffset(8, 8, 3, 3) };
            ChipOn = new GUIStyle(Chip);
            ChipOn.normal.background = UIStyles.Tex(new Color(0.35f, 0.55f, 0.75f, 1f));
            ChipOn.normal.textColor = Color.white;
            CardBox = new GUIStyle(UIStyles.Box) { padding = new RectOffset(10, 10, 8, 8), margin = new RectOffset(2, 2, 3, 3), alignment = TextAnchor.UpperLeft };
            Glyph = new GUIStyle(UIStyles.LabelBold) { fontSize = 46, alignment = TextAnchor.MiddleCenter };
            Glyph.normal.textColor = Color.white;
            AppTitle = new GUIStyle(UIStyles.LabelSmall) { alignment = TextAnchor.UpperCenter, wordWrap = true };
            FactLabel = new GUIStyle(UIStyles.LabelSmall) { wordWrap = true };
            FactLabel.normal.textColor = FactColor;
            PlayerText = new GUIStyle(UIStyles.LabelSmall) { wordWrap = true, fontStyle = FontStyle.Italic };
            PlayerText.normal.textColor = PlayerColor;
            Stamp = new GUIStyle(UIStyles.LabelBold) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            Stamp.normal.textColor = new Color(1f, 0.6f, 0.6f);
        }

        // ------------------------------------------------------------
        // 顔アイコン（住民の顔：観察できる見た目だけ）
        // ------------------------------------------------------------

        private static Texture2D Circle
        {
            get
            {
                if (circle != null) return circle;
                const int n = 64;
                circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                float r = n * 0.5f - 1f;
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = x + 0.5f - n * 0.5f, dy = y + 0.5f - n * 0.5f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(r - d + 0.5f);
                        circle.SetPixel(x, y, new Color(1, 1, 1, a));
                    }
                circle.Apply();
                return circle;
            }
        }

        public static void Disc(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Circle);
            GUI.color = prev;
        }

        /// <summary>住民の顔アイコン。系統（AlienLineage）ごとに耳・触角などの特徴を少し変える。</summary>
        public static void DrawFace(Rect r, Color body, int lineage, Expression expr, bool dim = false)
        {
            if (Event.current.type != EventType.Repaint) return;
            float s = Mathf.Min(r.width, r.height);
            var c = new Vector2(r.center.x, r.center.y);
            var dark = new Color(0.12f, 0.12f, 0.18f, 1f);
            var lighter = Color.Lerp(body, Color.white, 0.35f);
            // 系統ごとの特徴（キャラクターデザイン案の10系統）
            switch ((AlienLineage)lineage)
            {
                case AlienLineage.ChibiAnimal:
                    Disc(new Rect(c.x - s * 0.42f, c.y - s * 0.46f, s * 0.3f, s * 0.3f), body);
                    Disc(new Rect(c.x + s * 0.12f, c.y - s * 0.46f, s * 0.3f, s * 0.3f), body);
                    break;
                case AlienLineage.StarChild:
                    Disc(new Rect(c.x - s * 0.08f, c.y - s * 0.56f, s * 0.16f, s * 0.16f), new Color(1f, 0.92f, 0.45f));
                    break;
                case AlienLineage.Plush:
                    Disc(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), lighter);
                    break;
                case AlienLineage.MultiArm:
                    Disc(new Rect(c.x - s * 0.55f, c.y + s * 0.02f, s * 0.22f, s * 0.22f), body * 0.85f);
                    Disc(new Rect(c.x + s * 0.33f, c.y + s * 0.02f, s * 0.22f, s * 0.22f), body * 0.85f);
                    break;
                case AlienLineage.Floating:
                    Disc(new Rect(c.x - s * 0.3f, c.y + s * 0.36f, s * 0.6f, s * 0.12f), new Color(0, 0, 0, 0.25f));
                    break;
                case AlienLineage.Round:
                case AlienLineage.Expressive:
                    Disc(new Rect(c.x - s * 0.04f, c.y - s * 0.58f, s * 0.08f, s * 0.2f), body * 0.9f);
                    Disc(new Rect(c.x - s * 0.08f, c.y - s * 0.62f, s * 0.16f, s * 0.16f), lighter);
                    break;
                case AlienLineage.Lifestyle:
                    UIStyles.FillRect(new Rect(c.x - s * 0.3f, c.y - s * 0.48f, s * 0.6f, s * 0.12f), new Color(0.85f, 0.5f, 0.4f));
                    break;
            }
            float headW = (AlienLineage)lineage == AlienLineage.Mochi ? s * 0.92f : s * 0.8f;
            float headH = (AlienLineage)lineage == AlienLineage.Mochi ? s * 0.68f : s * 0.8f;
            Disc(new Rect(c.x - headW * 0.5f, c.y - headH * 0.5f + ((AlienLineage)lineage == AlienLineage.Mochi ? s * 0.08f : 0), headW, headH), body);

            // 目
            float ey = c.y - s * 0.06f;
            float ex = s * 0.15f;
            float es = s * 0.12f;
            switch (expr)
            {
                case Expression.Happy:
                case Expression.VeryHappy:
                case Expression.Sleepy:
                case Expression.Love:
                    UIStyles.FillRect(new Rect(c.x - ex - es * 0.5f, ey, es, s * 0.035f), dark);
                    UIStyles.FillRect(new Rect(c.x + ex - es * 0.5f, ey, es, s * 0.035f), dark);
                    break;
                case Expression.Surprised:
                    Disc(new Rect(c.x - ex - es * 0.7f, ey - es * 0.7f, es * 1.4f, es * 1.4f), Color.white);
                    Disc(new Rect(c.x + ex - es * 0.7f, ey - es * 0.7f, es * 1.4f, es * 1.4f), Color.white);
                    Disc(new Rect(c.x - ex - es * 0.35f, ey - es * 0.35f, es * 0.7f, es * 0.7f), dark);
                    Disc(new Rect(c.x + ex - es * 0.35f, ey - es * 0.35f, es * 0.7f, es * 0.7f), dark);
                    break;
                default:
                    Disc(new Rect(c.x - ex - es * 0.5f, ey - es * 0.5f, es, es), dark);
                    Disc(new Rect(c.x + ex - es * 0.5f, ey - es * 0.5f, es, es), dark);
                    break;
            }
            if (expr == Expression.Angry)
            {
                UIStyles.FillRect(new Rect(c.x - ex - es * 0.7f, ey - es * 1.1f, es * 1.4f, s * 0.03f), dark);
                UIStyles.FillRect(new Rect(c.x + ex - es * 0.7f, ey - es * 1.1f, es * 1.4f, s * 0.03f), dark);
            }
            // 頬
            if (expr == Expression.Shy || expr == Expression.Love || expr == Expression.VeryHappy)
            {
                Disc(new Rect(c.x - s * 0.3f, c.y + s * 0.02f, s * 0.12f, s * 0.08f), new Color(1f, 0.5f, 0.6f, 0.8f));
                Disc(new Rect(c.x + s * 0.18f, c.y + s * 0.02f, s * 0.12f, s * 0.08f), new Color(1f, 0.5f, 0.6f, 0.8f));
            }
            // 口
            float my = c.y + s * 0.14f;
            switch (expr)
            {
                case Expression.Happy:
                case Expression.VeryHappy:
                case Expression.Excited:
                    UIStyles.FillRect(new Rect(c.x - s * 0.12f, my, s * 0.24f, s * 0.06f), new Color(0.6f, 0.15f, 0.2f));
                    break;
                case Expression.Surprised:
                    Disc(new Rect(c.x - s * 0.05f, my - s * 0.02f, s * 0.1f, s * 0.1f), new Color(0.5f, 0.12f, 0.18f));
                    break;
                case Expression.Sad:
                case Expression.Crying:
                case Expression.Worried:
                case Expression.Disgusted:
                    UIStyles.FillRect(new Rect(c.x - s * 0.08f, my + s * 0.04f, s * 0.16f, s * 0.03f), dark);
                    break;
                default:
                    UIStyles.FillRect(new Rect(c.x - s * 0.06f, my, s * 0.12f, s * 0.03f), dark);
                    break;
            }
            if (expr == Expression.Crying || expr == Expression.Sad)
                Disc(new Rect(c.x - ex - s * 0.03f, ey + es * 0.6f, s * 0.07f, s * 0.11f), new Color(0.5f, 0.75f, 1f, 0.9f));
            if (dim) Disc(new Rect(c.x - s * 0.5f, c.y - s * 0.5f, s, s), new Color(0.1f, 0.1f, 0.15f, 0.45f));
        }

        /// <summary>GUILayout で顔アイコンを置く。</summary>
        public static void FaceLayout(float size, Color body, int lineage, Expression expr, bool dim = false)
        {
            var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
            DrawFace(r, body, lineage, expr, dim);
        }

        /// <summary>話し手の顔（住民以外は記号）。</summary>
        public static void SpeakerFace(TabletSystem sys, UtteranceRecord r, float size, CardNote note = null)
        {
            if (r == null) return;
            string who = note != null && !string.IsNullOrEmpty(note.playerSpeakerId) ? note.playerSpeakerId : r.speakerId;
            if (r.speakerKind == "resident" || r.speakerKind == "manepi" || string.IsNullOrEmpty(r.speakerKind))
            {
                FaceLayout(size, sys.SpeakerColor(who), sys.SpeakerLineage(who), r.expression);
            }
            else
            {
                var rect = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                UIStyles.FillRect(rect, new Color(0.3f, 0.35f, 0.45f));
                GUI.Label(rect, r.speakerKind == "sign" ? "□" : "?", Glyph);
            }
        }

        // ------------------------------------------------------------
        // 宇宙語の単語の並び（クリックで辞書へ）
        // ------------------------------------------------------------

        /// <summary>
        /// 単語を折り返して並べる。stateOf(i)：0=通常 1=差分 2=印 3=差分＋印。onClick(i)：単語が押された。
        /// showGloss：本人が辞書に書いた推測訳を下に添える（§6.5。誤訳もそのまま）。
        /// </summary>
        public static void WordFlow(List<string> words, float maxWidth, Func<int, int> stateOf, Action<int> onClick, bool showGloss)
        {
            Ensure();
            if (words == null || words.Count == 0)
            {
                GUILayout.Label(Loc.T("（聞き取れた言葉なし）", "(no words)"), UIStyles.LabelDim);
                return;
            }
            var sys = TabletSystem.Instance;
            float x = 0f;
            GUILayout.BeginHorizontal();
            for (int i = 0; i < words.Count; i++)
            {
                int st = stateOf != null ? stateOf(i) : 0;
                var style = st == 1 ? WordDiff : st == 2 ? WordMarked : st == 3 ? WordBoth : Word;
                var content = new GUIContent(words[i]);
                float w = style.CalcSize(content).x + 4f;
                string gloss = showGloss && sys != null ? sys.GetPlayerGloss(words[i]) : null;
                if (!string.IsNullOrEmpty(gloss)) w = Mathf.Max(w, Gloss.CalcSize(new GUIContent(gloss)).x + 6f);
                if (x > 0f && x + w > maxWidth)
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    x = 0f;
                }
                GUILayout.BeginVertical(GUILayout.Width(w));
                if (GUILayout.Button(content, style, GUILayout.Width(w))) onClick?.Invoke(i);
                if (showGloss) GUILayout.Label(gloss ?? " ", Gloss, GUILayout.Width(w));
                GUILayout.EndVertical();
                x += w + 6f;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        /// <summary>発話記録の宇宙語。単語クリックで DictionaryLookupRequestEvent を発行する。</summary>
        public static void RecordWords(UtteranceRecord r, float maxWidth, bool showGloss)
        {
            if (r == null) return;
            var words = TabletText.Words(r);
            string recordId = r.recordId;
            WordFlow(words, maxWidth, null, i =>
            {
                EventBus.Publish(new DictionaryLookupRequestEvent { Surface = TabletText.CleanSurface(words[i]), RecordId = recordId });
            }, showGloss);
        }

        // ------------------------------------------------------------
        // その他の部品
        // ------------------------------------------------------------

        public static void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            if (Event.current.type != EventType.Repaint) return;
            var d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var m = GUI.matrix;
            var prev = GUI.color;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.color = c;
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), Texture2D.whiteTexture);
            GUI.matrix = m;
            GUI.color = prev;
        }

        public static bool ChipToggle(string label, bool on, params GUILayoutOption[] opts)
        {
            Ensure();
            return GUILayout.Button(label, on ? ChipOn : Chip, opts);
        }

        public static void SectionHeader(string text)
        {
            Ensure();
            GUILayout.Space(6);
            GUILayout.Label(text, Header);
        }

        /// <summary>見出し付きの1行入力（プレイヤーの記述）。</summary>
        public static string Field(string label, string value, float labelWidth = 170f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UIStyles.LabelSmall, GUILayout.Width(labelWidth));
            value = GUILayout.TextField(value ?? "", UIStyles.TextField, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            return value;
        }

        public static string Area(string value, float height)
        {
            return GUILayout.TextArea(value ?? "", UIStyles.TextArea, GUILayout.Height(height));
        }

        // ------------------------------------------------------------
        // 写真（PNG を persistentDataPath 配下から読む）
        // ------------------------------------------------------------

        private static readonly Dictionary<string, Texture2D> photoCache = new Dictionary<string, Texture2D>();

        public static string PhotoFullPath(PhotoInfo p)
        {
            if (p == null || string.IsNullOrEmpty(p.fileName)) return null;
            return Path.Combine(Application.persistentDataPath, p.fileName);
        }

        public static Texture2D GetPhotoTexture(PhotoInfo p)
        {
            if (p == null) return null;
            if (photoCache.TryGetValue(p.id, out var t) && t != null) return t;
            try
            {
                var path = PhotoFullPath(p);
                if (path == null || !File.Exists(path)) return null;
                var bytes = File.ReadAllBytes(path);
                t = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave };
                if (!t.LoadImage(bytes)) return null;
                photoCache[p.id] = t;
                return t;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Tablet] 写真を読み込めません: " + e.Message);
                return null;
            }
        }

        public static void RememberPhotoTexture(string photoId, Texture2D tex)
        {
            if (string.IsNullOrEmpty(photoId) || tex == null) return;
            photoCache[photoId] = tex;
        }

        public static void ClearPhotoCache()
        {
            foreach (var kv in photoCache) if (kv.Value != null) UnityEngine.Object.Destroy(kv.Value);
            photoCache.Clear();
        }

        /// <summary>写真を枠内に縦横比を保って描く。</summary>
        public static void PhotoLayout(PhotoInfo p, float width, float maxHeight)
        {
            var tex = GetPhotoTexture(p);
            float h = tex != null ? Mathf.Min(maxHeight, width * tex.height / Mathf.Max(1f, tex.width)) : Mathf.Min(maxHeight, width * 0.56f);
            var r = GUILayoutUtility.GetRect(width, h, GUILayout.Width(width), GUILayout.Height(h));
            if (tex != null) GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit);
            else
            {
                UIStyles.FillRect(r, new Color(0.2f, 0.22f, 0.3f));
                GUI.Label(r, Loc.T("（写真ファイルが見つかりません）", "(photo file not found)"), UIStyles.LabelCenter);
            }
        }
    }
}
