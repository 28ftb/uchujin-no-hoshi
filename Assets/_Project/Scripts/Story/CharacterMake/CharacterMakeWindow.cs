using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイク画面（基本決定 §3【確定】）。新しい星を始める時と、タブレットの「着替え」で使う。
    /// 左に回転する3Dプレビュー（専用カメラ＋RenderTexture）、右に項目のタブ。
    /// 名前は任意。住民は主人公の名前を知らない（宇宙語では呼ばれない）。
    /// </summary>
    public class CharacterMakeWindow : UIWindow
    {
        public enum Mode { NewGame, ChangeClothes, NextPlanet }

        private const int NameMaxLength = 16;

        private readonly Mode mode;
        private readonly Action<CharacterAppearance> onDone;
        private readonly Action onCancel;
        private readonly CharacterAppearance original;
        private CharacterAppearance current;
        private AvatarPreviewStage preview;
        private bool dirty = true;
        private int tab;
        private readonly HashSet<string> expandedColor = new HashSet<string>();
        private Vector2 tabScroll;

        private static readonly LocText[] TabNames =
        {
            new LocText("からだ", "Body"),
            new LocText("かお", "Face"),
            new LocText("かみ", "Hair"),
            new LocText("ふく", "Clothes"),
            new LocText("こもの", "Accessory"),
            new LocText("なまえ", "Name"),
        };

        public CharacterMakeWindow(CharacterAppearance initial, Mode mode, Action<CharacterAppearance> onDone, Action onCancel)
        {
            this.mode = mode;
            this.onDone = onDone;
            this.onCancel = onCancel;
            original = AppearanceCatalog.Sanitize((initial ?? AppearanceCatalog.Default()).Clone());
            current = original.Clone();
        }

        public override string Title => mode == Mode.ChangeClothes ? Loc.T("着替え", "Change outfit") : Loc.T("キャラクターメイク", "Character creation");
        public override bool PausesTimeInSolo => true;
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => mode == Mode.ChangeClothes;
        public override Vector2 Size => new Vector2(Mathf.Min(1560f, UIStyles.VirtualWidth - 40f), Mathf.Min(940f, UIStyles.VirtualHeight - 40f));

        public override void OnOpen()
        {
            preview = new AvatarPreviewStage();
            dirty = true;
            if (mode != Mode.ChangeClothes && Game.Instance != null && Game.Instance.State == GameState.Title)
                Game.Instance.SetState(GameState.CharacterCreation);
        }

        public override void OnClose()
        {
            preview?.Dispose();
            preview = null;
        }

        public override void Update()
        {
            if (preview == null) return;
            if (dirty)
            {
                dirty = false;
                preview.SetAppearance(current);
            }
            preview.Update(UnityEngine.Time.unscaledDeltaTime);
        }

        public override void DrawContents(Rect content)
        {
            GUILayout.BeginHorizontal();

            // ---- 左：プレビュー ----
            float pw = Mathf.Min(520f, content.width * 0.36f);
            GUILayout.BeginVertical(GUILayout.Width(pw));
            float ph = content.height - 130f;
            var pr = GUILayoutUtility.GetRect(pw, ph, GUILayout.Width(pw), GUILayout.Height(ph));
            UIStyles.FillRect(pr, new Color(0.05f, 0.06f, 0.12f, 1f));
            if (preview != null && preview.Texture != null) GUI.DrawTexture(pr, preview.Texture, ScaleMode.ScaleToFit, false);
            var e = Event.current;
            if (preview != null && e.type == EventType.MouseDrag && pr.Contains(e.mousePosition))
            {
                preview.Yaw -= e.delta.x * 0.8f;
                preview.AutoRotate = false;
                e.Use();
            }
            GUI.Label(new Rect(pr.x + 10, pr.yMax - 30, pr.width - 20, 26), Loc.T("ドラッグで回転", "Drag to rotate"), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", UIStyles.Button, GUILayout.Height(42)) && preview != null) { preview.Yaw += 45f; preview.AutoRotate = false; }
            if (GUILayout.Button("▶", UIStyles.Button, GUILayout.Height(42)) && preview != null) { preview.Yaw -= 45f; preview.AutoRotate = false; }
            if (preview != null)
            {
                if (GUILayout.Button(preview.CloseUp ? Loc.T("全身", "Full body") : Loc.T("顔を見る", "Face"), UIStyles.Button, GUILayout.Height(42))) preview.CloseUp = !preview.CloseUp;
                if (GUILayout.Button(preview.AutoRotate ? Loc.T("回転を止める", "Stop") : Loc.T("自動回転", "Auto spin"), UIStyles.Button, GUILayout.Height(42))) preview.AutoRotate = !preview.AutoRotate;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(12);

            // ---- 右：項目 ----
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                if (GUILayout.Button(TabNames[i].ToString(), i == tab ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(44)))
                {
                    tab = i;
                    if (preview != null) preview.CloseUp = i == 1 || i == 2;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            tabScroll = GUILayout.BeginScrollView(tabScroll, GUILayout.Height(content.height - 120f));
            switch (tab)
            {
                case 0: DrawBodyTab(); break;
                case 1: DrawFaceTab(); break;
                case 2: DrawHairTab(); break;
                case 3: DrawClothesTab(); break;
                case 4: DrawAccessoryTab(); break;
                default: DrawNameTab(); break;
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("おまかせ", "Randomize"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(180)))
            {
                current = AppearanceCatalog.Randomize(current.displayName);
                dirty = true;
            }
            if (GUILayout.Button(Loc.T("最初に戻す", "Reset"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(180)))
            {
                current = original.Clone();
                dirty = true;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(160)))
            {
                Close();
                onCancel?.Invoke();
            }
            string ok = mode == Mode.ChangeClothes ? Loc.T("これに着替える", "Wear this") : Loc.T("この姿で出発", "Begin as this");
            if (GUILayout.Button(ok, UIStyles.ButtonSelected, GUILayout.Height(52), GUILayout.Width(260)))
            {
                var result = AppearanceCatalog.Sanitize(current.Clone());
                result.displayName = (result.displayName ?? "").Trim();
                Close();
                onDone?.Invoke(result);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------
        // タブ
        // ------------------------------------------------------------

        private void DrawBodyTab()
        {
            OptionRow(Loc.T("体型", "Body type"), AppearanceCatalog.BodyTypes, ref current.bodyType);
            SliderRow(Loc.T("身長", "Height"), ref current.height, 0.85f, 1.15f);
            SliderRow(Loc.T("頭の大きさ", "Head size"), ref current.headSize, 0.8f, 1.4f);
            OptionRow(Loc.T("宇宙人のしるし", "Alien feature"), AppearanceCatalog.AlienTraits, ref current.alienTrait);
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("肌の色（別の宇宙から来たあなたは、どんな色でも）", "Skin color (you come from another universe - any color works)"), UIStyles.LabelBold);
            GUILayout.Label(Loc.T("人に近い色", "Human-like"), UIStyles.LabelDim);
            Swatches(ref current.skinColor, AppearanceCatalog.SkinHuman);
            GUILayout.Label(Loc.T("別の宇宙の色", "Other-universe"), UIStyles.LabelDim);
            Swatches(ref current.skinColor, AppearanceCatalog.SkinAlien);
            FineTune("skin", ref current.skinColor);
        }

        private void DrawFaceTab()
        {
            OptionRow(Loc.T("顔", "Face"), AppearanceCatalog.FaceTypes, ref current.faceType);
            OptionRow(Loc.T("目の形", "Eye shape"), AppearanceCatalog.EyeTypes, ref current.eyeType);
            ColorRow(Loc.T("目の色", "Eye color"), "eye", ref current.eyeColor, AppearanceCatalog.EyeColors);
        }

        private void DrawHairTab()
        {
            OptionRow(Loc.T("髪型", "Hairstyle"), AppearanceCatalog.HairStyles, ref current.hairStyle);
            ColorRow(Loc.T("髪の色", "Hair color"), "hair", ref current.hairColor, AppearanceCatalog.HairColors);
        }

        private void DrawClothesTab()
        {
            OptionRow(Loc.T("上着", "Top"), AppearanceCatalog.Tops, ref current.outfitTop);
            ColorRow(Loc.T("上着の色", "Top color"), "top", ref current.topColor, AppearanceCatalog.ClothColors);
            GUILayout.Space(8);
            OptionRow(Loc.T("下衣", "Bottom"), AppearanceCatalog.Bottoms, ref current.outfitBottom);
            if (current.outfitTop == 4)
                GUILayout.Label(Loc.T("※ ワンピースの時は、すそは上着の色になります（レギンスは見えます）", "* With a dress, the hem uses the top color (leggings stay visible)"), UIStyles.LabelDim);
            ColorRow(Loc.T("下衣の色", "Bottom color"), "bottom", ref current.bottomColor, AppearanceCatalog.ClothColors);
            GUILayout.Space(8);
            OptionRow(Loc.T("靴", "Shoes"), AppearanceCatalog.Shoes, ref current.shoes);
            ColorRow(Loc.T("靴の色", "Shoe color"), "shoes", ref current.shoesColor, AppearanceCatalog.ClothColors);
        }

        private void DrawAccessoryTab()
        {
            OptionRow(Loc.T("アクセサリー", "Accessory"), AppearanceCatalog.Accessories, ref current.accessory);
            ColorRow(Loc.T("アクセサリーの色", "Accessory color"), "acc", ref current.accessoryColor, AppearanceCatalog.ClothColors);
            GUILayout.Label(Loc.T("※ ポニーテールやツインテールの結び目にもこの色が使われます", "* Hair ties also use this color"), UIStyles.LabelDim);
        }

        private void DrawNameTab()
        {
            GUILayout.Label(Loc.T("名前（任意）", "Name (optional)"), UIStyles.LabelBold);
            string before = current.displayName ?? "";
            string after = GUILayout.TextField(before, NameMaxLength, UIStyles.TextField, GUILayout.Height(44), GUILayout.Width(480));
            if (after != before) current.displayName = after;
            GUILayout.Space(10);
            GUILayout.Label(Loc.T(
                "この名前はセーブデータやマルチプレイの表示に使われます。\n星の住民たちはあなたの名前を知りません。宇宙語で名前を呼ばれることはありません。",
                "This name is shown in save data and to other players.\nThe residents of the planet do not know your name and will never call you by it."), UIStyles.LabelWrap);
        }

        // ------------------------------------------------------------
        // 部品
        // ------------------------------------------------------------

        private void OptionRow(string label, LocText[] list, ref int index)
        {
            GUILayout.BeginHorizontal(UIStyles.Box);
            GUILayout.Label(label, UIStyles.LabelBold, GUILayout.Width(220), GUILayout.Height(44));
            if (GUILayout.Button("◀", UIStyles.Button, GUILayout.Width(56), GUILayout.Height(44)))
            {
                index = AppearanceCatalog.Wrap(index - 1, list.Length);
                dirty = true;
            }
            GUILayout.Label(AppearanceCatalog.Label(list, index), UIStyles.LabelCenter, GUILayout.Width(260), GUILayout.Height(44));
            if (GUILayout.Button("▶", UIStyles.Button, GUILayout.Width(56), GUILayout.Height(44)))
            {
                index = AppearanceCatalog.Wrap(index + 1, list.Length);
                dirty = true;
            }
            GUILayout.Label($"{index + 1}/{list.Length}", UIStyles.LabelDim, GUILayout.Width(70), GUILayout.Height(44));
            GUILayout.EndHorizontal();
        }

        private void SliderRow(string label, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal(UIStyles.Box);
            GUILayout.Label(label, UIStyles.LabelBold, GUILayout.Width(220), GUILayout.Height(40));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(420), GUILayout.Height(40));
            GUILayout.Label(Mathf.RoundToInt(v * 100f) + "%", UIStyles.LabelDim, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(v - value) > 0.0005f)
            {
                value = v;
                dirty = true;
            }
        }

        private void ColorRow(string label, string key, ref Color c, Color[] palette)
        {
            GUILayout.Label(label, UIStyles.LabelBold);
            Swatches(ref c, palette);
            FineTune(key, ref c);
        }

        private void Swatches(ref Color c, Color[] palette)
        {
            const float size = 44f;
            int perRow = 12;
            for (int i = 0; i < palette.Length; i += perRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(palette.Length, i + perRow); j++)
                {
                    var r = GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size));
                    bool selected = Approximately(c, palette[j]);
                    UIStyles.FillRect(r, selected ? Color.white : new Color(0, 0, 0, 0.6f));
                    UIStyles.FillRect(new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6), palette[j]);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                    {
                        c = palette[j];
                        dirty = true;
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void FineTune(string key, ref Color c)
        {
            bool open = expandedColor.Contains(key);
            if (GUILayout.Button(open ? Loc.T("▲ 細かい調整を閉じる", "▲ Hide fine tuning") : Loc.T("▼ 色を細かく調整", "▼ Fine-tune color"), UIStyles.ButtonSmall, GUILayout.Width(300)))
            {
                if (open) expandedColor.Remove(key);
                else expandedColor.Add(key);
                open = !open;
            }
            if (!open) return;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("色合い", "Hue"), UIStyles.LabelSmall, GUILayout.Width(100));
            float nh = GUILayout.HorizontalSlider(h, 0f, 1f, GUILayout.Width(400));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("あざやかさ", "Saturation"), UIStyles.LabelSmall, GUILayout.Width(100));
            float ns = GUILayout.HorizontalSlider(s, 0f, 1f, GUILayout.Width(400));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("明るさ", "Brightness"), UIStyles.LabelSmall, GUILayout.Width(100));
            float nv = GUILayout.HorizontalSlider(v, 0.05f, 1f, GUILayout.Width(400));
            var sw = GUILayoutUtility.GetRect(60, 28, GUILayout.Width(60), GUILayout.Height(28));
            UIStyles.FillRect(sw, c);
            GUILayout.EndHorizontal();
            if (Mathf.Abs(nh - h) > 0.001f || Mathf.Abs(ns - s) > 0.001f || Mathf.Abs(nv - v) > 0.001f)
            {
                var nc = Color.HSVToRGB(nh, ns, nv);
                nc.a = 1f;
                c = nc;
                dirty = true;
            }
        }

        private static bool Approximately(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;
        }
    }
}
