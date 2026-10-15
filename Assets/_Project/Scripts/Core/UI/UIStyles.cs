using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.UI
{
    /// <summary>
    /// IMGUI のスタイル。プロトタイプ段階では全UIを IMGUI（OnGUI）で描画する。
    /// 仮想解像度（高さ1080基準）で描画し、画面サイズに合わせて拡大縮小する。
    /// 日本語表示のため OS のフォントを動的に読み込む（Unity上で表示確認が必要）。
    /// 本格的なUIは後で UI Toolkit / uGUI に置き換える想定。
    /// </summary>
    public static class UIStyles
    {
        public const float ReferenceHeight = 1080f;

        public static float Scale => Mathf.Max(0.5f, Screen.height / ReferenceHeight);
        public static float VirtualWidth => Screen.width / Scale;
        public static float VirtualHeight => Screen.height / Scale;

        public static readonly Color Panel = new Color(0.10f, 0.12f, 0.20f, 0.92f);
        public static readonly Color PanelLight = new Color(0.18f, 0.21f, 0.32f, 0.95f);
        public static readonly Color Accent = new Color(0.55f, 0.85f, 1f, 1f);
        public static readonly Color AccentWarm = new Color(1f, 0.8f, 0.45f, 1f);
        public static readonly Color TextMain = new Color(0.95f, 0.96f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.7f, 0.74f, 0.85f, 1f);
        public static readonly Color Good = new Color(0.55f, 0.95f, 0.6f, 1f);
        public static readonly Color Bad = new Color(1f, 0.55f, 0.55f, 1f);
        public static readonly Color AlienText = new Color(0.8f, 1f, 0.85f, 1f);

        private static bool initialized;
        private static Font font;
        public static GUIStyle Label, LabelSmall, LabelBold, LabelTitle, LabelCenter, LabelAlien, LabelAlienLarge, LabelDim, LabelWrap;
        public static GUIStyle Button, ButtonSmall, ButtonSelected, Box, PanelBox, TextField, TextArea, Toggle, Toast, Slot, SlotSelected;

        private static readonly Dictionary<Color, Texture2D> textures = new Dictionary<Color, Texture2D>();

        public static Texture2D Tex(Color c)
        {
            if (textures.TryGetValue(c, out var t) && t != null) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            textures[c] = t;
            return t;
        }

        /// <summary>OnGUI の中で毎回呼ぶ。初回だけスタイルを作る。</summary>
        public static void Ensure()
        {
            if (initialized) return;
            initialized = true;
            font = Font.CreateDynamicFontFromOSFont(new[]
            {
                "Yu Gothic UI", "Meiryo UI", "Meiryo", "Hiragino Sans", "Hiragino Kaku Gothic ProN",
                "Noto Sans CJK JP", "Noto Sans JP", "MS Gothic", "Arial Unicode MS", "Arial"
            }, 22);

            var skin = GUI.skin;
            if (font != null) skin.font = font;

            Label = Make(skin.label, 22, TextMain);
            LabelSmall = Make(skin.label, 18, TextMain);
            LabelBold = Make(skin.label, 22, TextMain, FontStyle.Bold);
            LabelTitle = Make(skin.label, 30, Accent, FontStyle.Bold);
            LabelCenter = Make(skin.label, 22, TextMain);
            LabelCenter.alignment = TextAnchor.MiddleCenter;
            LabelDim = Make(skin.label, 18, TextDim);
            LabelWrap = Make(skin.label, 20, TextMain);
            LabelWrap.wordWrap = true;
            LabelAlien = Make(skin.label, 24, AlienText, FontStyle.Bold);
            LabelAlien.wordWrap = true;
            LabelAlienLarge = Make(skin.label, 30, AlienText, FontStyle.Bold);
            LabelAlienLarge.wordWrap = true;

            Button = Make(skin.button, 21, TextMain);
            Button.normal.background = Tex(new Color(0.22f, 0.27f, 0.42f, 1f));
            Button.hover.background = Tex(new Color(0.30f, 0.38f, 0.58f, 1f));
            Button.active.background = Tex(new Color(0.16f, 0.2f, 0.32f, 1f));
            Button.hover.textColor = Color.white;
            Button.padding = new RectOffset(12, 12, 8, 8);
            Button.margin = new RectOffset(4, 4, 4, 4);
            ButtonSmall = new GUIStyle(Button) { fontSize = 17, padding = new RectOffset(8, 8, 4, 4) };
            ButtonSelected = new GUIStyle(Button);
            ButtonSelected.normal.background = Tex(new Color(0.35f, 0.55f, 0.75f, 1f));
            ButtonSelected.normal.textColor = Color.white;

            Box = new GUIStyle(skin.box) { fontSize = 20 };
            Box.normal.background = Tex(PanelLight);
            Box.normal.textColor = TextMain;
            PanelBox = new GUIStyle(skin.box);
            PanelBox.normal.background = Tex(Panel);
            PanelBox.padding = new RectOffset(16, 16, 12, 12);

            TextField = Make(skin.textField, 21, Color.white);
            TextField.normal.background = Tex(new Color(0.05f, 0.06f, 0.1f, 1f));
            TextField.focused.background = Tex(new Color(0.08f, 0.1f, 0.16f, 1f));
            TextField.focused.textColor = Color.white;
            TextField.padding = new RectOffset(8, 8, 6, 6);
            TextArea = new GUIStyle(TextField) { wordWrap = true };
            Toggle = Make(skin.toggle, 20, TextMain);
            Toggle.onNormal.textColor = Accent;

            Toast = Make(skin.box, 20, Color.white);
            Toast.normal.background = Tex(new Color(0.08f, 0.1f, 0.16f, 0.88f));
            Toast.alignment = TextAnchor.MiddleLeft;
            Toast.padding = new RectOffset(14, 14, 8, 8);
            Toast.wordWrap = true;

            Slot = new GUIStyle(skin.box) { fontSize = 15, alignment = TextAnchor.LowerRight };
            Slot.normal.background = Tex(new Color(0.14f, 0.16f, 0.25f, 0.9f));
            Slot.normal.textColor = Color.white;
            SlotSelected = new GUIStyle(Slot);
            SlotSelected.normal.background = Tex(new Color(0.4f, 0.6f, 0.85f, 0.95f));
        }

        private static GUIStyle Make(GUIStyle baseStyle, int size, Color color, FontStyle fs = FontStyle.Normal)
        {
            var s = new GUIStyle(baseStyle) { fontSize = size, fontStyle = fs, richText = true };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            return s;
        }

        /// <summary>仮想座標系に切り替える（OnGUI の最初で呼ぶ）。</summary>
        public static void BeginVirtual()
        {
            Ensure();
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        public static Vector2 ScreenToVirtual(Vector2 screen) => screen / Scale;

        /// <summary>ワールド座標 → 仮想GUI座標（カメラの後ろなら null）。</summary>
        public static Vector2? WorldToVirtual(Vector3 world)
        {
            var cam = Camera.main;
            if (cam == null) return null;
            var sp = cam.WorldToScreenPoint(world);
            if (sp.z < 0.1f) return null;
            return new Vector2(sp.x, Screen.height - sp.y) / Scale;
        }

        public static void FillRect(Rect r, Color c)
        {
            var prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Tex(Color.white));
            GUI.color = prev;
        }

        public static void ProgressBar(Rect r, float value01, Color fill)
        {
            FillRect(r, new Color(0, 0, 0, 0.5f));
            FillRect(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(value01), r.height - 4), fill);
        }

        /// <summary>アイテムの簡易アイコン（色付き四角＋頭文字）。</summary>
        public static void ItemIcon(Rect r, ItemStack stack, bool selected = false)
        {
            GUI.Box(r, GUIContent.none, selected ? SlotSelected : Slot);
            if (stack == null || stack.IsEmpty) return;
            var def = stack.Definition;
            var inner = new Rect(r.x + 6, r.y + 6, r.width - 12, r.height - 12);
            FillRect(inner, def.Color * 0.85f + new Color(0, 0, 0, 0.15f));
            string name = def.Name;
            string initial = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, Mathf.Min(2, name.Length));
            var c = new GUIStyle(LabelSmall) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            c.normal.textColor = Color.white;
            GUI.Label(new Rect(inner.x + 2, inner.y, inner.width, inner.height), initial, c);
            if (stack.count > 1) GUI.Label(new Rect(r.x, r.y, r.width - 4, r.height - 2), stack.count.ToString(), Slot);
            if (stack.quality > 0)
            {
                var q = new GUIStyle(LabelSmall) { fontSize = 12, alignment = TextAnchor.LowerLeft };
                q.normal.textColor = AccentWarm;
                GUI.Label(new Rect(r.x + 4, r.y, r.width, r.height - 2), ItemStack.QualityStars(stack.quality), q);
            }
        }

        public static string Money(int amount) => Wallet.Format(amount);
    }
}
