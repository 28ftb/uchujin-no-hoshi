using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 宇宙生物のプリミティブ模型（アート制作時に差し替える前提）。かわいく・分かりやすく：大きな目、丸い体、種族ごとの特徴パーツ。
    /// 生成物の構成：root（コライダー）→ "Model"（CreatureBody がアニメーション）→ 各パーツ。
    /// "Eye" という名前のパーツは擬態中に隠れる。"WingL"/"WingR" は羽ばたく。"Glow" は発光。"Ring" は回転。
    /// </summary>
    public static class CreatureVisualBuilder
    {
        private static readonly Color EyeColor = new Color(0.08f, 0.08f, 0.12f);

        /// <summary>個体差（色違い）を反映した色。</summary>
        public static Color Vary(Color c, int variant, float hueShift)
        {
            float a = c.a;
            Color.RGBToHSV(c, out var h, out var s, out var v);
            h = Mathf.Repeat(h + hueShift, 1f);
            switch (variant)
            {
                case 1: s *= 0.6f; v = Mathf.Min(1f, v * 1.1f); break;          // 淡い
                case 2: s = Mathf.Min(1f, s * 1.3f); v *= 0.8f; break;          // 濃い
                case 3: h = Mathf.Repeat(h + 0.45f, 1f); s = Mathf.Min(1f, s * 1.2f + 0.1f); break; // 珍しい色違い
            }
            var r = Color.HSVToRGB(h, s, v);
            r.a = a;
            return r;
        }

        public static GameObject Build(CreatureSpecies sp, Transform parent, Vector3 worldPos, int variant, float hueShift, float scale, int level, string name)
        {
            var root = new GameObject(name);
            if (parent != null) root.transform.SetParent(parent, false);
            root.transform.position = worldPos;
            var model = PrimitiveBuilder.Empty("Model", root.transform, Vector3.zero);
            Color main = Vary(sp.Main, variant, hueShift);
            Color accent = Vary(sp.Accent, variant, hueShift * 0.5f);
            BuildShape(sp, model.transform, main, accent, level);
            if (variant == 3)
            {
                // 珍しい色違いは小さなきらめきを付ける
                PrimitiveBuilder.EmissivePart(model.transform, PrimitiveType.Sphere, new Vector3(0.18f, 0.62f, 0f), Vector3.one * 0.06f, new Color(1f, 1f, 0.7f), "Sparkle");
            }
            model.transform.localScale = Vector3.one * Mathf.Max(0.2f, scale);
            var col = root.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.3f * scale, 0);
            col.radius = Mathf.Max(0.35f, 0.38f * scale);
            return root;
        }

        private static GameObject P(Transform t, PrimitiveType type, float x, float y, float z, float sx, float sy, float sz, Color c, string name = null, float rx = 0, float ry = 0, float rz = 0)
        {
            return PrimitiveBuilder.Part(t, type, new Vector3(x, y, z), new Vector3(sx, sy, sz), c, name, new Vector3(rx, ry, rz));
        }

        private static void Eyes(Transform t, float y, float z, float spread = 0.1f, float size = 0.08f, bool emissive = false, Color? color = null)
        {
            var c = color ?? EyeColor;
            for (int i = -1; i <= 1; i += 2)
            {
                GameObject e;
                if (emissive) e = PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(spread * i, y, z), Vector3.one * size, c, "Eye");
                else e = P(t, PrimitiveType.Sphere, spread * i, y, z, size, size, size, c, "Eye");
                if (!emissive) P(e.transform, PrimitiveType.Sphere, 0.18f, 0.25f, 0.35f, 0.35f, 0.35f, 0.35f, Color.white, "Eye");
            }
        }

        private static void Glow(Transform t, float x, float y, float z, float size, Color c)
        {
            PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(x, y, z), Vector3.one * size, c, "Glow");
        }

        private static void BuildShape(CreatureSpecies sp, Transform m, Color main, Color accent, int level)
        {
            Color white = new Color(1f, 1f, 1f, 0.55f);
            switch (sp.Shape)
            {
                case BodyShape.Round:
                    {
                        bool flat = sp.No == 29; // ヌマプニはぷにぷに平たい
                        P(m, PrimitiveType.Sphere, 0, flat ? 0.18f : 0.28f, 0, 0.56f, flat ? 0.36f : 0.52f, 0.56f, main, "Body");
                        P(m, PrimitiveType.Sphere, -0.17f, 0.24f, 0.2f, 0.1f, 0.06f, 0.04f, accent, "Cheek");
                        P(m, PrimitiveType.Sphere, 0.17f, 0.24f, 0.2f, 0.1f, 0.06f, 0.04f, accent, "Cheek");
                        if (sp.No == 16 || sp.No == 5) // 苔・葉っぱをまとう
                        {
                            P(m, PrimitiveType.Sphere, 0.05f, 0.5f, -0.05f, 0.3f, 0.12f, 0.3f, accent, "Moss");
                            P(m, PrimitiveType.Sphere, -0.12f, 0.46f, 0.05f, 0.18f, 0.08f, 0.18f, accent, "Moss");
                        }
                        if (sp.No == 1) P(m, PrimitiveType.Sphere, 0, 0.52f, 0, 0.12f, 0.08f, 0.12f, accent, "Drop");
                        if (sp.No == 35) { P(m, PrimitiveType.Sphere, -0.15f, 0.48f, 0, 0.12f, 0.1f, 0.1f, main, "Ear"); P(m, PrimitiveType.Sphere, 0.15f, 0.48f, 0, 0.12f, 0.1f, 0.1f, main, "Ear"); }
                        if (sp.No == 30) Glow(m, 0, 0.36f, 0, 0.18f, accent);
                        Eyes(m, flat ? 0.24f : 0.34f, flat ? 0.22f : 0.24f);
                        P(m, PrimitiveType.Sphere, -0.13f, 0.04f, 0.08f, 0.12f, 0.06f, 0.14f, accent, "Foot");
                        P(m, PrimitiveType.Sphere, 0.13f, 0.04f, 0.08f, 0.12f, 0.06f, 0.14f, accent, "Foot");
                        break;
                    }
                case BodyShape.Mole:
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0, 0.5f, 0.42f, 0.62f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0.33f, 0.11f, 0.09f, 0.09f, accent, "Nose");
                    P(m, PrimitiveType.Sphere, -0.24f, 0.09f, 0.24f, 0.22f, 0.08f, 0.2f, accent, "Paw");
                    P(m, PrimitiveType.Sphere, 0.24f, 0.09f, 0.24f, 0.22f, 0.08f, 0.2f, accent, "Paw");
                    Eyes(m, 0.33f, 0.27f, 0.09f, 0.06f);
                    break;
                case BodyShape.Bird:
                    P(m, PrimitiveType.Sphere, 0, 0.3f, 0, 0.4f, 0.38f, 0.46f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.52f, 0.12f, 0.3f, 0.3f, 0.3f, main, "Head");
                    P(m, PrimitiveType.Cube, 0, 0.5f, 0.3f, 0.08f, 0.06f, 0.12f, accent, "Beak");
                    P(m, PrimitiveType.Sphere, -0.21f, 0.33f, 0, 0.08f, 0.22f, 0.32f, main * 0.85f, "WingL");
                    P(m, PrimitiveType.Sphere, 0.21f, 0.33f, 0, 0.08f, 0.22f, 0.32f, main * 0.85f, "WingR");
                    P(m, PrimitiveType.Sphere, 0, 0.3f, -0.25f, 0.18f, 0.06f, 0.16f, accent, "Tail");
                    Eyes(m, 0.56f, 0.24f, 0.08f, 0.06f);
                    break;
                case BodyShape.LongArm:
                    P(m, PrimitiveType.Sphere, 0, 0.32f, 0, 0.44f, 0.46f, 0.4f, main, "Body");
                    P(m, PrimitiveType.Cylinder, -0.28f, 0.28f, 0.05f, 0.06f, 0.24f, 0.06f, accent, "ArmL", 0, 0, 30);
                    P(m, PrimitiveType.Cylinder, 0.28f, 0.28f, 0.05f, 0.06f, 0.24f, 0.06f, accent, "ArmR", 0, 0, -30);
                    P(m, PrimitiveType.Sphere, -0.4f, 0.08f, 0.05f, 0.1f, 0.1f, 0.1f, main, "Hand");
                    P(m, PrimitiveType.Sphere, 0.4f, 0.08f, 0.05f, 0.1f, 0.1f, 0.1f, main, "Hand");
                    P(m, PrimitiveType.Sphere, -0.14f, 0.55f, 0, 0.1f, 0.14f, 0.06f, accent, "Ear");
                    P(m, PrimitiveType.Sphere, 0.14f, 0.55f, 0, 0.1f, 0.14f, 0.06f, accent, "Ear");
                    Eyes(m, 0.38f, 0.19f);
                    break;
                case BodyShape.Bug:
                    {
                        float y = 0.55f;
                        P(m, PrimitiveType.Sphere, 0, y, 0, 0.28f, 0.26f, 0.3f, main, "Body");
                        P(m, PrimitiveType.Sphere, -0.19f, y + 0.08f, -0.02f, 0.3f, 0.03f, 0.18f, white, "WingL", 0, 0, 15);
                        P(m, PrimitiveType.Sphere, 0.19f, y + 0.08f, -0.02f, 0.3f, 0.03f, 0.18f, white, "WingR", 0, 0, -15);
                        if (sp.Glows) Glow(m, 0, y - 0.04f, -0.18f, 0.17f, accent);
                        else P(m, PrimitiveType.Sphere, 0, y - 0.04f, -0.18f, 0.17f, 0.15f, 0.17f, accent, "Tail");
                        if (sp.No == 8) P(m, PrimitiveType.Cube, 0, y - 0.24f, 0.02f, 0.2f, 0.16f, 0.2f, new Color(0.85f, 0.7f, 0.45f), "Bundle");
                        P(m, PrimitiveType.Cylinder, -0.05f, y + 0.18f, 0.08f, 0.015f, 0.07f, 0.015f, accent, "Antenna", -20, 0, 10);
                        P(m, PrimitiveType.Cylinder, 0.05f, y + 0.18f, 0.08f, 0.015f, 0.07f, 0.015f, accent, "Antenna", -20, 0, -10);
                        Eyes(m, y + 0.04f, 0.13f, 0.07f, 0.06f);
                        break;
                    }
                case BodyShape.Fluffy:
                    P(m, PrimitiveType.Sphere, 0, 0.3f, 0, 0.52f, 0.5f, 0.52f, main, "Body");
                    for (int i = 0; i < 7; i++)
                    {
                        float a = i / 7f * Mathf.PI * 2f;
                        P(m, PrimitiveType.Sphere, Mathf.Cos(a) * 0.22f, 0.32f + Mathf.Sin(a * 2f) * 0.08f, Mathf.Sin(a) * 0.22f, 0.26f, 0.26f, 0.26f, main, "Fur");
                    }
                    P(m, PrimitiveType.Sphere, 0, 0.26f, 0.27f, 0.07f, 0.05f, 0.05f, accent, "Nose");
                    Eyes(m, 0.36f, 0.25f);
                    break;
                case BodyShape.Nut:
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0, 0.42f, 0.46f, 0.42f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.45f, 0, 0.47f, 0.17f, 0.47f, accent, "Cap");
                    P(m, PrimitiveType.Cylinder, 0, 0.56f, 0, 0.04f, 0.05f, 0.04f, accent * 0.8f, "Stem");
                    if (sp.No == 20) P(m, PrimitiveType.Sphere, 0, 0.16f, 0.2f, 0.22f, 0.14f, 0.08f, accent, "Pouch");
                    Eyes(m, 0.3f, 0.19f, 0.09f, 0.07f);
                    break;
                case BodyShape.Leaf:
                    P(m, PrimitiveType.Sphere, 0, 0.12f, 0, 0.42f, 0.04f, 0.62f, main, "Body", 0, 0, 8);
                    P(m, PrimitiveType.Cube, 0, 0.14f, 0, 0.02f, 0.02f, 0.58f, accent, "Rib");
                    P(m, PrimitiveType.Cylinder, 0, 0.12f, -0.36f, 0.015f, 0.06f, 0.015f, accent, "Stalk", 90, 0, 0);
                    Eyes(m, 0.16f, 0.2f, 0.06f, 0.05f);
                    break;
                case BodyShape.Hopper:
                    P(m, PrimitiveType.Sphere, 0, 0.42f, 0, 0.38f, 0.36f, 0.38f, main, "Body");
                    P(m, PrimitiveType.Cylinder, -0.1f, 0.15f, 0, 0.05f, 0.14f, 0.05f, accent, "Spring");
                    P(m, PrimitiveType.Cylinder, 0.1f, 0.15f, 0, 0.05f, 0.14f, 0.05f, accent, "Spring");
                    P(m, PrimitiveType.Sphere, -0.1f, 0.02f, 0.03f, 0.1f, 0.04f, 0.14f, accent, "Foot");
                    P(m, PrimitiveType.Sphere, 0.1f, 0.02f, 0.03f, 0.1f, 0.04f, 0.14f, accent, "Foot");
                    if (sp.No == 39)
                    {
                        P(m, PrimitiveType.Cube, -0.12f, 0.62f, 0, 0.1f, 0.12f, 0.04f, main, "Ear", 0, 0, 20);
                        P(m, PrimitiveType.Cube, 0.12f, 0.62f, 0, 0.1f, 0.12f, 0.04f, main, "Ear", 0, 0, -20);
                        P(m, PrimitiveType.Cylinder, 0, 0.4f, -0.25f, 0.04f, 0.15f, 0.04f, accent, "Tail", 50, 0, 0);
                    }
                    else
                    {
                        P(m, PrimitiveType.Sphere, -0.09f, 0.66f, 0, 0.08f, 0.18f, 0.05f, main, "Ear");
                        P(m, PrimitiveType.Sphere, 0.09f, 0.66f, 0, 0.08f, 0.18f, 0.05f, main, "Ear");
                    }
                    Eyes(m, 0.47f, 0.17f);
                    break;
                case BodyShape.Roller:
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0, 0.46f, 0.44f, 0.5f, main, "Body");
                    for (int i = -1; i <= 1; i++) P(m, PrimitiveType.Cylinder, 0, 0.24f, i * 0.12f, 0.47f, 0.012f, 0.47f, accent, "Band", 90, 0, 0);
                    if (sp.No == 24) P(m, PrimitiveType.Sphere, 0, 0.12f, 0.38f, 0.24f, 0.24f, 0.24f, accent, "SandBall");
                    Eyes(m, 0.3f, 0.23f, 0.08f, 0.06f);
                    break;
                case BodyShape.LongEar:
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0, 0.38f, 0.36f, 0.38f, main, "Body");
                    P(m, PrimitiveType.Cylinder, -0.09f, 0.62f, 0, 0.04f, 0.34f, 0.04f, main, "EarL", 0, 0, 14);
                    P(m, PrimitiveType.Cylinder, 0.09f, 0.62f, 0, 0.04f, 0.34f, 0.04f, main, "EarR", 0, 0, -14);
                    P(m, PrimitiveType.Cylinder, -0.2f, 0.78f, 0, 0.025f, 0.08f, 0.025f, main, "Twig", 0, 0, 60);
                    P(m, PrimitiveType.Cylinder, 0.2f, 0.82f, 0, 0.025f, 0.08f, 0.025f, main, "Twig", 0, 0, -60);
                    P(m, PrimitiveType.Sphere, 0, 0.2f, 0.17f, 0.18f, 0.12f, 0.06f, accent, "Belly");
                    Eyes(m, 0.32f, 0.17f, 0.08f, 0.06f);
                    break;
                case BodyShape.Seedfluff:
                    {
                        float y = 0.62f;
                        if (sp.Glows) Glow(m, 0, y, 0, 0.16f, accent);
                        else P(m, PrimitiveType.Sphere, 0, y, 0, 0.14f, 0.14f, 0.14f, accent, "Core");
                        for (int i = 0; i < 10; i++)
                        {
                            float a = i / 10f * 360f;
                            var spoke = PrimitiveBuilder.Empty("Spoke", m, new Vector3(0, y, 0));
                            spoke.transform.localEulerAngles = new Vector3(i % 2 == 0 ? 30f : -20f, a, 0);
                            P(spoke.transform, PrimitiveType.Sphere, 0, 0, 0.2f, 0.06f, 0.06f, 0.28f, new Color(main.r, main.g, main.b, 0.75f), "Fluff");
                        }
                        Eyes(m, y + 0.01f, 0.07f, 0.04f, 0.04f);
                        break;
                    }
                case BodyShape.Mushroom:
                    P(m, PrimitiveType.Cylinder, 0, 0.18f, 0, 0.24f, 0.18f, 0.24f, accent, "Stem");
                    P(m, PrimitiveType.Sphere, 0, 0.4f, 0, 0.58f, 0.28f, 0.58f, main, "Cap");
                    P(m, PrimitiveType.Sphere, 0.14f, 0.5f, 0.12f, 0.08f, 0.04f, 0.08f, accent, "Dot");
                    P(m, PrimitiveType.Sphere, -0.16f, 0.5f, -0.06f, 0.07f, 0.04f, 0.07f, accent, "Dot");
                    P(m, PrimitiveType.Sphere, 0, 0.54f, -0.15f, 0.06f, 0.03f, 0.06f, accent, "Dot");
                    Eyes(m, 0.2f, 0.12f, 0.06f, 0.06f);
                    break;
                case BodyShape.Shell:
                    P(m, PrimitiveType.Sphere, 0, 0.12f, 0.08f, 0.3f, 0.2f, 0.5f, main, "Body");
                    if (sp.No == 44) PrimitiveBuilder.EmissivePart(m, PrimitiveType.Sphere, new Vector3(0, 0.32f, -0.04f), new Vector3(0.38f, 0.36f, 0.4f), accent, "Glow");
                    else P(m, PrimitiveType.Sphere, 0, 0.32f, -0.04f, 0.4f, 0.38f, 0.42f, accent, "Shell");
                    P(m, PrimitiveType.Sphere, 0, 0.36f, 0.05f, 0.18f, 0.18f, 0.2f, sp.No == 27 ? new Color(0.35f, 0.65f, 0.3f) : accent * 0.85f, "Spiral");
                    P(m, PrimitiveType.Cylinder, -0.06f, 0.26f, 0.3f, 0.015f, 0.08f, 0.015f, main, "Stalk");
                    P(m, PrimitiveType.Cylinder, 0.06f, 0.26f, 0.3f, 0.015f, 0.08f, 0.015f, main, "Stalk");
                    Eyes(m, 0.35f, 0.3f, 0.06f, 0.05f);
                    break;
                case BodyShape.Lizard:
                    P(m, PrimitiveType.Sphere, 0, 0.08f, 0, 0.32f, 0.12f, 0.5f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.1f, 0.3f, 0.22f, 0.14f, 0.2f, main, "Head");
                    P(m, PrimitiveType.Cylinder, 0, 0.06f, -0.34f, 0.04f, 0.14f, 0.04f, accent, "Tail", 90, 0, 0);
                    for (int i = -1; i <= 1; i += 2)
                    {
                        P(m, PrimitiveType.Sphere, 0.17f * i, 0.03f, 0.12f, 0.1f, 0.03f, 0.08f, accent, "Leg");
                        P(m, PrimitiveType.Sphere, 0.17f * i, 0.03f, -0.12f, 0.1f, 0.03f, 0.08f, accent, "Leg");
                    }
                    Eyes(m, 0.17f, 0.34f, 0.07f, 0.07f);
                    break;
                case BodyShape.Bubble:
                    P(m, PrimitiveType.Sphere, 0, 0.26f, 0, 0.42f, 0.4f, 0.42f, new Color(main.r, main.g, main.b, 0.7f), "Body");
                    P(m, PrimitiveType.Sphere, 0.26f, 0.42f, 0.05f, 0.12f, 0.12f, 0.12f, white, "Bubble");
                    P(m, PrimitiveType.Sphere, -0.22f, 0.5f, -0.08f, 0.09f, 0.09f, 0.09f, white, "Bubble");
                    P(m, PrimitiveType.Sphere, 0.05f, 0.58f, 0.1f, 0.07f, 0.07f, 0.07f, white, "Bubble");
                    Eyes(m, 0.32f, 0.18f);
                    break;
                case BodyShape.Fishy:
                    P(m, PrimitiveType.Sphere, 0, 0.24f, 0, 0.28f, 0.3f, 0.5f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.26f, -0.3f, 0.04f, 0.26f, 0.2f, accent, "Tail");
                    if (sp.No == 28)
                    {
                        P(m, PrimitiveType.Cube, -0.2f, 0.2f, -0.05f, 0.25f, 0.01f, 0.08f, accent, "WingL", 0, 0, -20);
                        P(m, PrimitiveType.Cube, 0.2f, 0.2f, -0.05f, 0.25f, 0.01f, 0.08f, accent, "WingR", 0, 0, 20);
                    }
                    else
                    {
                        P(m, PrimitiveType.Sphere, -0.16f, 0.2f, 0.02f, 0.12f, 0.03f, 0.14f, accent, "WingL");
                        P(m, PrimitiveType.Sphere, 0.16f, 0.2f, 0.02f, 0.12f, 0.03f, 0.14f, accent, "WingR");
                    }
                    Eyes(m, 0.3f, 0.2f, 0.09f, 0.06f);
                    break;
                case BodyShape.Umbrella:
                    P(m, PrimitiveType.Sphere, 0, 0.18f, 0, 0.32f, 0.32f, 0.32f, accent, "Body");
                    P(m, PrimitiveType.Cylinder, 0.06f, 0.4f, 0, 0.025f, 0.16f, 0.025f, main * 0.8f, "Stem");
                    P(m, PrimitiveType.Sphere, 0.06f, 0.58f, 0, 0.66f, 0.1f, 0.66f, main, "Umbrella");
                    Eyes(m, 0.22f, 0.14f, 0.07f, 0.06f);
                    break;
                case BodyShape.Box:
                    P(m, PrimitiveType.Cube, 0, 0.18f, 0, 0.52f, 0.36f, 0.5f, accent, "Box");
                    P(m, PrimitiveType.Sphere, 0, 0.42f, 0.02f, 0.34f, 0.3f, 0.32f, main, "Head");
                    P(m, PrimitiveType.Cube, -0.1f, 0.58f, 0, 0.08f, 0.1f, 0.04f, main, "Ear", 0, 0, 20);
                    P(m, PrimitiveType.Cube, 0.1f, 0.58f, 0, 0.08f, 0.1f, 0.04f, main, "Ear", 0, 0, -20);
                    P(m, PrimitiveType.Cube, -0.2f, 0.37f, 0.25f, 0.12f, 0.02f, 0.02f, accent * 0.8f, "Flap", 0, 0, -25);
                    Eyes(m, 0.44f, 0.15f, 0.07f, 0.06f);
                    break;
                case BodyShape.Mimic:
                    P(m, PrimitiveType.Sphere, 0, 0.3f, 0, 0.48f, 0.46f, 0.46f, main, "Body");
                    P(m, PrimitiveType.Sphere, -0.27f, 0.48f, 0, 0.08f, 0.24f, 0.24f, accent, "EarL", 0, 0, 20);
                    P(m, PrimitiveType.Sphere, 0.27f, 0.48f, 0, 0.08f, 0.24f, 0.24f, accent, "EarR", 0, 0, -20);
                    P(m, PrimitiveType.Sphere, 0, 0.22f, 0.22f, 0.12f, 0.06f, 0.04f, accent, "Mouth");
                    Eyes(m, 0.36f, 0.21f, 0.1f, 0.1f);
                    break;
                case BodyShape.Drummer:
                    P(m, PrimitiveType.Sphere, 0, 0.28f, 0, 0.42f, 0.42f, 0.4f, main, "Body");
                    P(m, PrimitiveType.Cylinder, -0.24f, 0.36f, 0.12f, 0.025f, 0.14f, 0.025f, accent, "ArmL", 40, 0, 30);
                    P(m, PrimitiveType.Cylinder, 0.24f, 0.36f, 0.12f, 0.025f, 0.14f, 0.025f, accent, "ArmR", 40, 0, -30);
                    P(m, PrimitiveType.Sphere, -0.3f, 0.47f, 0.2f, 0.07f, 0.07f, 0.07f, accent, "Stick");
                    P(m, PrimitiveType.Sphere, 0.3f, 0.47f, 0.2f, 0.07f, 0.07f, 0.07f, accent, "Stick");
                    Eyes(m, 0.34f, 0.19f);
                    break;
                case BodyShape.Stacker:
                    P(m, PrimitiveType.Sphere, 0, 0.18f, 0, 0.34f, 0.32f, 0.34f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.38f, 0, 0.2f, 0.1f, 0.18f, accent, "Pebble");
                    P(m, PrimitiveType.Sphere, 0.01f, 0.46f, 0, 0.14f, 0.08f, 0.13f, accent * 0.9f, "Pebble");
                    P(m, PrimitiveType.Sphere, -0.01f, 0.52f, 0, 0.09f, 0.06f, 0.09f, accent * 1.1f, "Pebble");
                    Eyes(m, 0.22f, 0.16f, 0.07f, 0.06f);
                    break;
                case BodyShape.Rock:
                    P(m, PrimitiveType.Cube, 0, 0.26f, 0, 0.66f, 0.46f, 0.58f, main, "Body", 8, 20, 6);
                    P(m, PrimitiveType.Cube, 0.18f, 0.48f, -0.05f, 0.3f, 0.2f, 0.3f, accent, "Lump", 20, 40, 10);
                    P(m, PrimitiveType.Cube, -0.2f, 0.12f, 0.2f, 0.24f, 0.16f, 0.22f, accent * 0.9f, "Lump", 5, 10, 25);
                    Eyes(m, 0.32f, 0.3f, 0.1f, 0.07f);
                    break;
                case BodyShape.Echo:
                    P(m, PrimitiveType.Sphere, 0, 0.3f, 0, 0.46f, 0.44f, 0.44f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0, 0.22f, 0.2f, 0.2f, 0.14f, 0.06f, new Color(0.2f, 0.15f, 0.25f), "Mouth");
                    P(m, PrimitiveType.Sphere, -0.24f, 0.5f, -0.02f, 0.06f, 0.28f, 0.2f, accent, "EarL", 0, 0, 30);
                    P(m, PrimitiveType.Sphere, 0.24f, 0.5f, -0.02f, 0.06f, 0.28f, 0.2f, accent, "EarR", 0, 0, -30);
                    Eyes(m, 0.38f, 0.2f, 0.09f, 0.07f);
                    break;
                case BodyShape.Shadow:
                    P(m, PrimitiveType.Sphere, 0, 0.32f, 0, 0.46f, 0.5f, 0.46f, main, "Body");
                    P(m, PrimitiveType.Sphere, 0.12f, 0.1f, 0.05f, 0.2f, 0.16f, 0.2f, main, "Wisp");
                    P(m, PrimitiveType.Sphere, -0.14f, 0.12f, -0.06f, 0.18f, 0.14f, 0.18f, main, "Wisp");
                    Eyes(m, 0.38f, 0.21f, 0.09f, 0.08f, true, accent);
                    break;
                case BodyShape.Cocoon:
                    if (level < 3)
                    {
                        P(m, PrimitiveType.Capsule, 0, 0.32f, 0, 0.28f, 0.22f, 0.28f, main, "Cocoon");
                        P(m, PrimitiveType.Cylinder, 0, 0.36f, 0, 0.29f, 0.01f, 0.29f, accent, "Band");
                        P(m, PrimitiveType.Cylinder, 0, 0.24f, 0, 0.29f, 0.01f, 0.29f, accent, "Band");
                        Eyes(m, 0.42f, 0.13f, 0.06f, 0.05f);
                    }
                    else
                    {
                        P(m, PrimitiveType.Capsule, 0, 0.45f, 0, 0.12f, 0.16f, 0.12f, main, "Body");
                        Color[] rainbow = { new Color(1f, 0.5f, 0.6f), new Color(1f, 0.85f, 0.4f), new Color(0.5f, 0.9f, 0.6f), new Color(0.5f, 0.7f, 1f) };
                        P(m, PrimitiveType.Sphere, -0.2f, 0.52f, 0, 0.32f, 0.02f, 0.24f, rainbow[0], "WingL", 0, 0, 15);
                        P(m, PrimitiveType.Sphere, 0.2f, 0.52f, 0, 0.32f, 0.02f, 0.24f, rainbow[1], "WingR", 0, 0, -15);
                        P(m, PrimitiveType.Sphere, -0.16f, 0.4f, -0.04f, 0.22f, 0.02f, 0.16f, rainbow[2], "WingL", 0, 0, 10);
                        P(m, PrimitiveType.Sphere, 0.16f, 0.4f, -0.04f, 0.22f, 0.02f, 0.16f, rainbow[3], "WingR", 0, 0, -10);
                        Eyes(m, 0.55f, 0.09f, 0.04f, 0.04f);
                    }
                    break;
                case BodyShape.Star:
                    {
                        float y = 0.6f;
                        PrimitiveBuilder.EmissivePart(m, PrimitiveType.Sphere, new Vector3(0, y, 0), Vector3.one * 0.38f, main, "Glow");
                        var ring = PrimitiveBuilder.Empty("Ring", m, new Vector3(0, y, 0));
                        ring.transform.localEulerAngles = new Vector3(20f, 0, 10f);
                        for (int i = 0; i < 14; i++)
                        {
                            float a = i / 14f * Mathf.PI * 2f;
                            P(ring.transform, PrimitiveType.Sphere, Mathf.Cos(a) * 0.36f, 0, Mathf.Sin(a) * 0.36f, 0.08f, 0.04f, 0.08f, accent, "RingBit");
                        }
                        Eyes(m, y + 0.03f, 0.17f, 0.08f, 0.07f);
                        break;
                    }
            }
        }
    }

    /// <summary>見た目だけの小さなエフェクト（水しぶき・粉・ハートなど）。UnityEngine.Random 可。</summary>
    public class CreatureFx : MonoBehaviour
    {
        private Vector3 velocity;
        private float life;
        private float age;

        public static void Puff(Vector3 pos, Color color, int count, float speed = 1.2f, float size = 0.08f, bool emissive = false)
        {
            var root = Game.Instance != null ? Game.Instance.SceneRoot : null;
            for (int i = 0; i < count; i++)
            {
                var go = PrimitiveBuilder.Create(PrimitiveType.Sphere, root, pos, Vector3.one * size, color, "Fx", false);
                if (go.transform.parent != null) go.transform.position = pos;
                if (emissive)
                {
                    var r = go.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(color);
                }
                var fx = go.AddComponent<CreatureFx>();
                fx.velocity = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(0.6f, 1.4f), UnityEngine.Random.Range(-1f, 1f)) * speed;
                fx.life = UnityEngine.Random.Range(0.6f, 1.1f);
            }
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            age += dt;
            velocity += Vector3.down * 2.5f * dt;
            transform.position += velocity * dt;
            transform.localScale *= 1f - dt * 0.8f;
            if (age >= life) Destroy(gameObject);
        }
    }

    /// <summary>
    /// 生物の体のアニメーション（模型 "Model" を動かす）。移動そのものは WildCreature / OwnedCreatureAvatar が行う。
    /// </summary>
    public class CreatureBody : MonoBehaviour
    {
        public enum Pose { Idle, Walk, Hop, Roll, Sleep, Act, Fly, Hidden, Float }

        public CreatureSpecies Species;
        public Pose CurrentPose = Pose.Idle;
        /// <summary>頭上に出す記号（♪ ♥ z ! ? ～）。</summary>
        public string Emote;
        public Color EmoteColor = Color.white;

        private Transform model;
        private readonly List<Transform> wingsL = new List<Transform>();
        private readonly List<Transform> wingsR = new List<Transform>();
        private readonly List<GameObject> eyes = new List<GameObject>();
        private readonly List<Renderer> glowRenderers = new List<Renderer>();
        private readonly List<Renderer> allRenderers = new List<Renderer>();
        private readonly Dictionary<Renderer, Material> originalMaterials = new Dictionary<Renderer, Material>();
        private Transform ring;
        private float emoteUntil;
        private float t;
        private float actUntil;
        private float gestureUntil;
        private PlayerGesture gesture;
        private bool camouflaged;
        private bool glowOn = true;
        private float baseHover;
        private float phase;

        public bool IsActing => UnityEngine.Time.time < actUntil;

        public void Init(CreatureSpecies sp)
        {
            Species = sp;
            model = transform.Find("Model");
            phase = UnityEngine.Random.Range(0f, 10f);
            if (model == null) return;
            foreach (Transform c in model.GetComponentsInChildren<Transform>(true))
            {
                if (c.name == "WingL") wingsL.Add(c);
                else if (c.name == "WingR") wingsR.Add(c);
                else if (c.name == "Eye") eyes.Add(c.gameObject);
                else if (c.name == "Ring") ring = c;
                var r = c.GetComponent<Renderer>();
                if (r != null)
                {
                    allRenderers.Add(r);
                    originalMaterials[r] = r.sharedMaterial;
                    if (c.name == "Glow") glowRenderers.Add(r);
                }
            }
            baseHover = sp.Behavior == WildBehavior.Flyer || sp.Behavior == WildBehavior.Drift || sp.Behavior == WildBehavior.Swarm ? 0.6f
                : sp.Behavior == WildBehavior.Float && sp.Shape != BodyShape.Round && sp.Shape != BodyShape.Bubble && sp.Shape != BodyShape.Fishy ? 0.5f : 0f;
            WorldLabels.Add(transform, () => UnityEngine.Time.time < emoteUntil ? Emote : null, Vector3.up * (1.0f + baseHover), new Color(1f, 0.95f, 0.7f), 28f);
        }

        private void OnDestroy()
        {
            WorldLabels.Remove(transform);
        }

        public void ShowEmote(string symbol, float seconds = 2f)
        {
            Emote = symbol;
            emoteUntil = UnityEngine.Time.time + seconds;
        }

        public void PlayAct(float seconds = 1.6f)
        {
            actUntil = UnityEngine.Time.time + seconds;
        }

        public void PlayGesture(PlayerGesture g, float seconds = 1.6f)
        {
            gesture = g;
            gestureUntil = UnityEngine.Time.time + seconds;
        }

        public void SetEyesVisible(bool visible)
        {
            foreach (var e in eyes) if (e != null) e.SetActive(visible);
        }

        /// <summary>擬態：体の色を周りの色にする（null で元へ）。</summary>
        public void SetCamouflage(Color? envColor)
        {
            camouflaged = envColor.HasValue;
            int shade = 0; // 部位ごとの濃淡（Unity 6 で GetInstanceID が使えないため並び順で決める）
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (envColor.HasValue)
                {
                    if (r.gameObject.name == "Eye" || r.transform.parent != null && r.transform.parent.name == "Eye") continue;
                    r.sharedMaterial = MaterialCache.Get(Color.Lerp(envColor.Value, envColor.Value * 0.85f, (shade++ & 3) / 3f));
                }
                else if (originalMaterials.TryGetValue(r, out var m)) r.sharedMaterial = m;
            }
        }

        public bool IsCamouflaged => camouflaged;

        public void SetGlow(bool on)
        {
            if (glowOn == on) return;
            glowOn = on;
            foreach (var r in glowRenderers)
            {
                if (r == null || !originalMaterials.TryGetValue(r, out var m)) continue;
                r.sharedMaterial = on ? m : MaterialCache.Get(Species != null ? Species.Accent * 0.5f : Color.gray);
            }
        }

        public void SetVisible(bool visible)
        {
            if (model != null) model.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (model == null) return;
            float dt = UnityEngine.Time.deltaTime;
            t += dt;
            float tt = t + phase;
            Vector3 pos = new Vector3(0, baseHover, 0);
            Vector3 euler = Vector3.zero;
            Vector3 scale = Vector3.one;
            float flap = 0f;

            switch (CurrentPose)
            {
                case Pose.Idle:
                    scale = new Vector3(1f + Mathf.Sin(tt * 2.2f) * 0.03f, 1f - Mathf.Sin(tt * 2.2f) * 0.03f, 1f);
                    pos.y += baseHover > 0 ? Mathf.Sin(tt * 2f) * 0.08f : 0f;
                    flap = baseHover > 0 ? Mathf.Sin(tt * 18f) : 0f;
                    break;
                case Pose.Walk:
                    pos.y += Mathf.Abs(Mathf.Sin(tt * 9f)) * 0.06f;
                    euler.z = Mathf.Sin(tt * 9f) * 6f;
                    flap = baseHover > 0 ? Mathf.Sin(tt * 22f) : 0f;
                    break;
                case Pose.Fly:
                    pos.y += 0.3f + Mathf.Sin(tt * 3f) * 0.12f;
                    flap = Mathf.Sin(tt * 26f);
                    break;
                case Pose.Float:
                    pos.y += Mathf.Sin(tt * 1.6f) * 0.07f;
                    euler.z = Mathf.Sin(tt * 1.1f) * 5f;
                    flap = Mathf.Sin(tt * 4f) * 0.3f;
                    break;
                case Pose.Hop:
                    pos.y += Mathf.Abs(Mathf.Sin(tt * 6f)) * 0.55f;
                    scale = new Vector3(1f, 1f + Mathf.Sin(tt * 12f) * 0.1f, 1f);
                    break;
                case Pose.Roll:
                    euler.x = tt * 720f;
                    scale = Vector3.one * 0.92f;
                    break;
                case Pose.Sleep:
                    scale = new Vector3(1.06f + Mathf.Sin(tt * 1.2f) * 0.03f, 0.88f, 1.06f);
                    pos.y = 0f;
                    if (UnityEngine.Time.time > emoteUntil && Mathf.Repeat(tt, 4f) < 0.05f) ShowEmote("z", 2.5f);
                    break;
                case Pose.Hidden:
                    pos.y = -0.35f;
                    scale = new Vector3(1f, 0.4f, 1f);
                    break;
            }

            if (UnityEngine.Time.time < actUntil)
            {
                // 特徴的な行動：ぷるぷる・ぴょん・くるっ
                float k = Mathf.Sin(tt * 14f);
                scale = new Vector3(1f + k * 0.08f, 1f - k * 0.08f, 1f + k * 0.08f);
                pos.y += Mathf.Abs(Mathf.Sin(tt * 7f)) * 0.15f;
                flap = Mathf.Sin(tt * 30f);
            }
            if (UnityEngine.Time.time < gestureUntil)
            {
                float k = Mathf.Sin(tt * 8f);
                switch (gesture)
                {
                    case PlayerGesture.Nod:
                    case PlayerGesture.Bow: euler.x = k * 18f; break;
                    case PlayerGesture.ShakeHead: euler.y = k * 25f; break;
                    case PlayerGesture.Wave:
                    case PlayerGesture.Beckon: euler.z = k * 15f; break;
                    case PlayerGesture.Sad: euler.x = 15f; scale *= 0.95f; break;
                    case PlayerGesture.Think: euler.z = 12f; break;
                    default: pos.y += Mathf.Abs(k) * 0.3f; break;
                }
            }

            model.localPosition = Vector3.Lerp(model.localPosition, pos, 1f - Mathf.Exp(-12f * dt));
            model.localRotation = CurrentPose == Pose.Roll ? Quaternion.Euler(euler) : Quaternion.Slerp(model.localRotation, Quaternion.Euler(euler), 1f - Mathf.Exp(-10f * dt));
            var baseScale = model.localScale.x / Mathf.Max(0.0001f, lastScaleMul.x);
            lastScaleMul = scale;
            model.localScale = new Vector3(baseScale * scale.x, baseScale * scale.y, baseScale * scale.z);

            foreach (var w in wingsL) if (w != null) w.localRotation = Quaternion.Euler(0, 0, 15f + flap * 35f);
            foreach (var w in wingsR) if (w != null) w.localRotation = Quaternion.Euler(0, 0, -15f - flap * 35f);
            if (ring != null) ring.Rotate(0f, 40f * dt, 0f, Space.Self);

            // 発光の明滅
            if (Species != null && Species.Glows && glowRenderers.Count > 0)
            {
                bool shouldGlow = !Species.GlowsOnlyAtNight || IsDark();
                if (Species.No == 7 || Species.No == 47) shouldGlow &= Mathf.Repeat(tt, 2.4f) < 1.6f; // 点滅
                SetGlow(shouldGlow && !camouflaged);
            }
        }

        private Vector3 lastScaleMul = Vector3.one;

        /// <summary>見た目の基本倍率（成長に伴う大きさ）を変える。</summary>
        public void SetBaseScale(float s)
        {
            if (model == null) return;
            model.localScale = new Vector3(s * lastScaleMul.x, s * lastScaleMul.y, s * lastScaleMul.z);
        }

        public static bool IsDark()
        {
            var clock = Game.Instance != null ? Game.Instance.Time : null;
            if (clock == null) return false;
            return clock.IsNight;
        }
    }
}
