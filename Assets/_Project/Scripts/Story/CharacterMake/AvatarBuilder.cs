using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// キャラクターメイクの全項目を反映した主人公の見た目（プリミティブ構成）。
    /// PlayerAvatarFactory.BuildVisual をこれで上書きする（マルチの他プレイヤーにも同じ関数が使われる）。
    /// 約束：ルートの名前は "Visual"、その直下に "ToolHolder" を置く（コアの PlayerController / PlayerInteractor が使う）。
    /// 頭身は大きめ（かわいさ重視）。正面は +Z。
    /// </summary>
    public static class AvatarBuilder
    {
        private struct BodyShape
        {
            public float torsoW, torsoD, torsoH, legLen, legT, armLen, armT;
            public bool round;
            public BodyShape(float w, float d, float h, float leg, float legT, float arm, float armT, bool round)
            {
                torsoW = w; torsoD = d; torsoH = h; legLen = leg; this.legT = legT; armLen = arm; this.armT = armT; this.round = round;
            }
        }

        // 体型ごとの寸法【仮数値】（AppearanceCatalog.BodyTypes と同じ順）
        private static readonly BodyShape[] shapes =
        {
            new BodyShape(0.48f, 0.32f, 0.50f, 0.42f, 0.16f, 0.42f, 0.13f, false), // ふつう
            new BodyShape(0.46f, 0.34f, 0.40f, 0.30f, 0.17f, 0.34f, 0.13f, false), // ちんまり
            new BodyShape(0.40f, 0.26f, 0.54f, 0.46f, 0.13f, 0.45f, 0.11f, false), // ほっそり
            new BodyShape(0.60f, 0.40f, 0.52f, 0.42f, 0.19f, 0.44f, 0.16f, false), // がっしり
            new BodyShape(0.62f, 0.55f, 0.55f, 0.32f, 0.17f, 0.36f, 0.14f, true),  // まんまる
            new BodyShape(0.56f, 0.46f, 0.50f, 0.36f, 0.17f, 0.38f, 0.14f, true),  // ふわふわ
        };

        private const float FootH = 0.08f;
        private static readonly Color Dark = new Color(0.12f, 0.1f, 0.14f);
        private static readonly Color White = new Color(0.98f, 0.98f, 1f);
        private static readonly Color MouthColor = new Color(0.55f, 0.18f, 0.22f);
        private static readonly Color Cheek = new Color(1f, 0.55f, 0.62f);

        /// <summary>PlayerAvatarFactory.BuildVisual の差し替え。</summary>
        public static GameObject Build(CharacterAppearance source, Transform parent)
        {
            var a = AppearanceCatalog.Sanitize(source != null ? source.Clone() : AppearanceCatalog.Default());
            var visual = PrimitiveBuilder.Empty("Visual", parent, Vector3.zero);
            var s = shapes[Mathf.Clamp(a.bodyType, 0, shapes.Length - 1)];
            float h = a.height;
            float hs = a.headSize;

            float legLen = s.legLen * h;
            float hipY = FootH + legLen;
            float torsoH = s.torsoH * h;
            float neckY = hipY + torsoH;
            float shoulderY = hipY + torsoH * 0.82f;
            float headR = 0.30f * hs * (a.bodyType == 1 ? 1.08f : 1f);
            float headY = neckY + headR * 0.88f;

            var anim = visual.AddComponent<AvatarAnimator>();

            // ---- 胴体 ----
            var body = PrimitiveBuilder.Empty("Body", visual.transform, new Vector3(0, hipY, 0));
            anim.body = body.transform;
            BuildTorso(body.transform, a, s, torsoH);

            // ---- 脚 ----
            float legX = s.torsoW * 0.24f;
            anim.legL = BuildLeg(visual.transform, "LegL", new Vector3(-legX, hipY, 0), a, s, legLen);
            anim.legR = BuildLeg(visual.transform, "LegR", new Vector3(legX, hipY, 0), a, s, legLen);
            BuildSkirtOrDress(body.transform, a, s, torsoH, legLen);

            // ---- 腕 ----
            float armX = s.torsoW * 0.5f + s.armT * 0.35f;
            anim.armL = BuildArm(visual.transform, "ArmL", new Vector3(-armX, shoulderY, 0), a, s, -1f);
            anim.armR = BuildArm(visual.transform, "ArmR", new Vector3(armX, shoulderY, 0), a, s, 1f);

            // ---- 頭 ----
            var head = PrimitiveBuilder.Empty("Head", visual.transform, new Vector3(0, headY, 0));
            anim.head = head.transform;
            PrimitiveBuilder.Part(head.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * headR * 2f, a.skinColor, "Skull");
            BuildEyes(head.transform, a, headR, anim);
            BuildFace(head.transform, a, headR);
            BuildHair(head.transform, a, headR);
            BuildAccessory(head.transform, visual.transform, a, headR, neckY, s);
            BuildTopExtras(head.transform, body.transform, a, s, torsoH, headR);

            // ---- 道具を持つ位置（コアとの約束：Visual 直下の ToolHolder）----
            var holder = PrimitiveBuilder.Empty("ToolHolder", visual.transform,
                new Vector3(armX + 0.02f, shoulderY - s.armLen * h * 0.95f, 0.12f));
            var handle = PrimitiveBuilder.Part(holder.transform, PrimitiveType.Cube, new Vector3(0, 0.22f, 0.08f), new Vector3(0.05f, 0.44f, 0.05f),
                new Color(0.55f, 0.38f, 0.22f), "Handle");
            var headPiece = PrimitiveBuilder.Part(holder.transform, PrimitiveType.Cube, new Vector3(0, 0.45f, 0.12f), new Vector3(0.16f, 0.08f, 0.1f),
                new Color(0.7f, 0.72f, 0.78f), "ToolHead");
            anim.toolVisual = holder.transform;
            anim.toolParts = new[] { handle, headPiece };
            return visual;
        }

        // ------------------------------------------------------------
        // 胴体・服
        // ------------------------------------------------------------

        private static void BuildTorso(Transform body, CharacterAppearance a, BodyShape s, float torsoH)
        {
            Color top = a.topColor;
            Color shirt = a.outfitTop == 6 || a.outfitTop == 8 ? Color.Lerp(White, a.topColor, 0.25f) : top;
            Color cape = top;
            if (a.outfitTop == 7) shirt = Color.Lerp(a.topColor, White, 0.55f);

            var torsoType = s.round ? PrimitiveType.Sphere : PrimitiveType.Capsule;
            var torsoScale = s.round
                ? new Vector3(s.torsoW, torsoH * 1.05f, s.torsoD)
                : new Vector3(s.torsoW, torsoH * 0.62f, s.torsoD);
            PrimitiveBuilder.Part(body, torsoType, new Vector3(0, torsoH * 0.5f, 0), torsoScale, shirt, "Torso");

            if (a.bodyType == 5)
            {
                // ふわふわ：体のまわりの綿毛
                for (int i = 0; i < 8; i++)
                {
                    float ang = i * Mathf.PI * 2f / 8f;
                    var p = new Vector3(Mathf.Cos(ang) * s.torsoW * 0.45f, torsoH * (0.25f + 0.1f * (i % 2)), Mathf.Sin(ang) * s.torsoD * 0.45f);
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, p, Vector3.one * 0.16f, Color.Lerp(shirt, White, 0.3f), "Fluff");
                }
            }

            switch (a.outfitTop)
            {
                case 1: // パーカー：ポケット・ひも
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.28f, s.torsoD * 0.48f), new Vector3(s.torsoW * 0.6f, torsoH * 0.18f, 0.04f), top * 0.85f, "Pocket");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(-0.05f, torsoH * 0.75f, s.torsoD * 0.5f), new Vector3(0.02f, torsoH * 0.22f, 0.02f), White, "String");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0.05f, torsoH * 0.75f, s.torsoD * 0.5f), new Vector3(0.02f, torsoH * 0.22f, 0.02f), White, "String");
                    break;
                case 2: // ジャケット：前の開き・襟
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.5f, s.torsoD * 0.47f), new Vector3(s.torsoW * 0.22f, torsoH * 0.85f, 0.04f), White, "Inner");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(-s.torsoW * 0.16f, torsoH * 0.9f, s.torsoD * 0.42f), new Vector3(0.12f, 0.06f, 0.08f), top * 0.8f, "Collar", new Vector3(0, 0, 25));
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(s.torsoW * 0.16f, torsoH * 0.9f, s.torsoD * 0.42f), new Vector3(0.12f, 0.06f, 0.08f), top * 0.8f, "Collar", new Vector3(0, 0, -25));
                    break;
                case 3: // 宇宙服：首のリング・胸のパネル（光る）
                    PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, torsoH * 0.98f, 0), new Vector3(s.torsoW * 0.62f, 0.04f, s.torsoD * 0.9f), new Color(0.75f, 0.78f, 0.85f), "NeckRing");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.55f, s.torsoD * 0.46f), new Vector3(s.torsoW * 0.42f, torsoH * 0.26f, 0.05f), new Color(0.3f, 0.32f, 0.4f), "Panel");
                    PrimitiveBuilder.EmissivePart(body, PrimitiveType.Cube, new Vector3(-0.06f, torsoH * 0.58f, s.torsoD * 0.5f), Vector3.one * 0.045f, new Color(0.4f, 1f, 0.6f), "Led");
                    PrimitiveBuilder.EmissivePart(body, PrimitiveType.Cube, new Vector3(0.06f, torsoH * 0.58f, s.torsoD * 0.5f), Vector3.one * 0.045f, new Color(1f, 0.5f, 0.3f), "Led");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, torsoH * 0.06f, 0), new Vector3(s.torsoW * 1.02f, 0.03f, s.torsoD * 1.02f), new Color(0.75f, 0.78f, 0.85f), "Belt");
                    break;
                case 5: // セーター：タートルネック
                    PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, torsoH * 0.98f, 0), new Vector3(s.torsoW * 0.48f, 0.07f, s.torsoD * 0.7f), top * 0.9f, "Turtleneck");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.5f, s.torsoD * 0.47f), new Vector3(s.torsoW * 0.7f, 0.04f, 0.02f), Color.Lerp(top, White, 0.5f), "Stripe");
                    break;
                case 6: // オーバーオール（下衣の色で胸当て・肩ひも）
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.42f, s.torsoD * 0.44f), new Vector3(s.torsoW * 0.55f, torsoH * 0.45f, 0.06f), a.topColor, "Bib");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(-s.torsoW * 0.2f, torsoH * 0.78f, 0), new Vector3(0.05f, 0.04f, s.torsoD * 1.02f), a.topColor, "Strap");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(s.torsoW * 0.2f, torsoH * 0.78f, 0), new Vector3(0.05f, 0.04f, s.torsoD * 1.02f), a.topColor, "Strap");
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(-s.torsoW * 0.2f, torsoH * 0.62f, s.torsoD * 0.48f), Vector3.one * 0.04f, AppearanceCatalog.HairColors[3], "Button");
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(s.torsoW * 0.2f, torsoH * 0.62f, s.torsoD * 0.48f), Vector3.one * 0.04f, AppearanceCatalog.HairColors[3], "Button");
                    break;
                case 7: // マント
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, torsoH * 0.35f, -s.torsoD * 0.6f), new Vector3(s.torsoW * 1.15f, torsoH * 1.25f, 0.04f), cape, "Cape", new Vector3(-8, 0, 0));
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0, torsoH * 0.95f, s.torsoD * 0.4f), Vector3.one * 0.07f, AppearanceCatalog.HairColors[3], "Clasp");
                    break;
                case 8: // ベスト：前の2枚
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(-s.torsoW * 0.18f, torsoH * 0.52f, s.torsoD * 0.45f), new Vector3(s.torsoW * 0.3f, torsoH * 0.75f, 0.05f), a.topColor, "VestL");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(s.torsoW * 0.18f, torsoH * 0.52f, s.torsoD * 0.45f), new Vector3(s.torsoW * 0.3f, torsoH * 0.75f, 0.05f), a.topColor, "VestR");
                    break;
            }
        }

        private static void BuildSkirtOrDress(Transform body, CharacterAppearance a, BodyShape s, float torsoH, float legLen)
        {
            // ワンピース（上衣）：上衣の色で広がる裾
            if (a.outfitTop == 4)
            {
                PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, -legLen * 0.18f, 0), new Vector3(s.torsoW * 1.35f, legLen * 0.22f, s.torsoD * 1.45f), a.topColor, "DressHem");
                return;
            }
            switch (a.outfitBottom)
            {
                case 2: // スカート
                    PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, -legLen * 0.12f, 0), new Vector3(s.torsoW * 1.2f, legLen * 0.2f, s.torsoD * 1.3f), a.bottomColor, "Skirt");
                    break;
                case 3: // ふんわりスカート
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0, -legLen * 0.08f, 0), new Vector3(s.torsoW * 1.45f, legLen * 0.62f, s.torsoD * 1.55f), a.bottomColor, "PuffySkirt");
                    PrimitiveBuilder.Part(body, PrimitiveType.Cylinder, new Vector3(0, -legLen * 0.3f, 0), new Vector3(s.torsoW * 1.38f, 0.02f, s.torsoD * 1.48f), Color.Lerp(a.bottomColor, White, 0.6f), "Frill");
                    break;
                default: // ズボン類の腰まわり
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0, 0.02f, 0), new Vector3(s.torsoW * 0.98f, 0.22f, s.torsoD * 0.98f), a.bottomColor, "Hips");
                    break;
            }
        }

        private static Transform BuildLeg(Transform visual, string name, Vector3 hip, CharacterAppearance a, BodyShape s, float legLen)
        {
            var pivot = PrimitiveBuilder.Empty(name, visual, hip);
            Color skin = a.skinColor;
            Color pants = a.bottomColor;
            bool skirtLike = a.outfitTop == 4 || a.outfitBottom == 2 || a.outfitBottom == 3;
            Color legColor;
            switch (a.outfitBottom)
            {
                case 0: legColor = skirtLike ? skin : pants; break;   // ズボン
                case 4: legColor = pants; break;                       // 宇宙服パンツ
                case 5: legColor = pants * 0.85f + new Color(0, 0, 0, 0.15f); break; // レギンス
                default: legColor = skin; break;
            }
            if (a.outfitTop == 4 && a.outfitBottom != 5) legColor = skin;
            float t = s.legT * (a.outfitBottom == 4 ? 1.25f : 1f);
            PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Capsule, new Vector3(0, -legLen * 0.5f, 0), new Vector3(t, legLen * 0.55f, t), legColor, "Leg");
            if (a.outfitBottom == 1 && !skirtLike)
            {
                // 半ズボン
                PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Capsule, new Vector3(0, -legLen * 0.2f, 0), new Vector3(t * 1.25f, legLen * 0.25f, t * 1.25f), pants, "Shorts");
            }
            if (a.outfitBottom == 4)
            {
                PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Cylinder, new Vector3(0, -legLen * 0.55f, 0), new Vector3(t * 1.08f, 0.02f, t * 1.08f), new Color(0.75f, 0.78f, 0.85f), "KneeRing");
            }
            BuildShoe(pivot.transform, a, s, legLen);
            return pivot.transform;
        }

        private static void BuildShoe(Transform pivot, CharacterAppearance a, BodyShape s, float legLen)
        {
            float y = -legLen - FootH * 0.4f;
            float w = s.legT * 1.15f;
            Color c = a.shoesColor;
            switch (a.shoes)
            {
                case 0: // スニーカー
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, y + 0.03f, 0.05f), new Vector3(w, FootH * 1.2f, w * 1.6f), c, "Shoe");
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Cube, new Vector3(0, y - 0.02f, 0.05f), new Vector3(w * 0.95f, 0.025f, w * 1.55f), White, "Sole");
                    break;
                case 1: // ブーツ
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Cylinder, new Vector3(0, y + legLen * 0.22f, 0), new Vector3(w * 1.05f, legLen * 0.22f, w * 1.05f), c, "BootShaft");
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, y + 0.02f, 0.06f), new Vector3(w * 1.05f, FootH * 1.3f, w * 1.6f), c, "Boot");
                    break;
                case 2: // サンダル
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, y + 0.03f, 0.05f), new Vector3(w * 0.9f, FootH, w * 1.4f), a.skinColor, "Foot");
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Cube, new Vector3(0, y - 0.015f, 0.05f), new Vector3(w * 1.05f, 0.03f, w * 1.6f), c, "SandalSole");
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Cube, new Vector3(0, y + 0.05f, 0.08f), new Vector3(w * 1.0f, 0.02f, 0.03f), c, "Strap");
                    break;
                case 3: // 宇宙ブーツ（大きくてかわいい）
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, y + 0.06f, 0.04f), new Vector3(w * 1.5f, FootH * 2.2f, w * 1.9f), c, "MoonBoot");
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Cylinder, new Vector3(0, y + 0.12f, 0), new Vector3(w * 1.25f, 0.02f, w * 1.25f), White, "BootRing");
                    break;
                default: // はだし
                    PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, y + 0.03f, 0.05f), new Vector3(w * 0.9f, FootH, w * 1.4f), a.skinColor, "Foot");
                    break;
            }
        }

        private static Transform BuildArm(Transform visual, string name, Vector3 shoulder, CharacterAppearance a, BodyShape s, float side)
        {
            var pivot = PrimitiveBuilder.Empty(name, visual, shoulder);
            pivot.transform.localEulerAngles = new Vector3(0, 0, side * 8f);
            float len = s.armLen * a.height;
            bool shortSleeve = a.outfitTop == 0 || a.outfitTop == 8 || a.outfitTop == 4;
            Color sleeve = a.outfitTop == 6 ? Color.Lerp(White, a.topColor, 0.25f) : a.outfitTop == 7 ? Color.Lerp(a.topColor, White, 0.55f) : a.topColor;
            if (a.outfitTop == 8) sleeve = Color.Lerp(White, a.topColor, 0.25f);
            Color armColor = shortSleeve ? a.skinColor : sleeve;
            PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Capsule, new Vector3(0, -len * 0.5f, 0), new Vector3(s.armT, len * 0.55f, s.armT), armColor, "Arm");
            if (shortSleeve)
                PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Sphere, new Vector3(0, -len * 0.08f, 0), new Vector3(s.armT * 1.4f, len * 0.35f, s.armT * 1.4f), sleeve, "Sleeve");
            if (a.outfitTop == 3)
                PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Cylinder, new Vector3(0, -len * 0.88f, 0), new Vector3(s.armT * 1.2f, 0.02f, s.armT * 1.2f), new Color(0.75f, 0.78f, 0.85f), "Cuff");
            Color hand = a.outfitTop == 3 ? new Color(0.92f, 0.93f, 0.96f) : a.skinColor;
            PrimitiveBuilder.Part(pivot.transform, PrimitiveType.Sphere, new Vector3(0, -len, 0), Vector3.one * s.armT * 1.25f, hand, "Hand");
            return pivot.transform;
        }

        private static void BuildTopExtras(Transform head, Transform body, CharacterAppearance a, BodyShape s, float torsoH, float r)
        {
            if (a.outfitTop == 1 && a.accessory != 9)
            {
                // パーカーのフード（頭の後ろ）
                PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0, torsoH * 1.0f, -s.torsoD * 0.42f), new Vector3(s.torsoW * 0.9f, 0.22f, 0.26f), a.topColor * 0.9f, "Hood");
            }
        }

        // ------------------------------------------------------------
        // 顔
        // ------------------------------------------------------------

        /// <summary>頭の球面上の正面寄りの点。</summary>
        private static Vector3 OnFace(float r, float x, float y, float lift = 0f)
        {
            float zz = r * r - x * x - y * y;
            float z = zz > 0 ? Mathf.Sqrt(zz) : 0f;
            return new Vector3(x, y, z + lift);
        }

        private static void BuildEyes(Transform head, CharacterAppearance a, float r, AvatarAnimator anim)
        {
            float ex = r * 0.36f;
            float ey = -r * 0.04f;
            Color iris = a.eyeColor;
            Color pupil = Color.Lerp(a.eyeColor, Dark, 0.7f);
            var eyes = new List<Transform>();
            switch (a.eyeType)
            {
                case 7: // ひとつ目
                    eyes.Add(EyeBall(head, OnFace(r, 0, ey + r * 0.05f, -0.01f), r * 0.42f, iris, pupil, true, "EyeC"));
                    break;
                case 8: // みっつ目
                    eyes.Add(EyeBall(head, OnFace(r, -ex, ey, -0.01f), r * 0.24f, iris, pupil, true, "EyeL"));
                    eyes.Add(EyeBall(head, OnFace(r, ex, ey, -0.01f), r * 0.24f, iris, pupil, true, "EyeR"));
                    eyes.Add(EyeBall(head, OnFace(r, 0, r * 0.38f, -0.01f), r * 0.18f, iris, pupil, true, "EyeT"));
                    break;
                default:
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var p = OnFace(r, ex * i, ey, -0.01f);
                        string n = i < 0 ? "EyeL" : "EyeR";
                        switch (a.eyeType)
                        {
                            case 1: // たれ目
                                eyes.Add(EyeBall(head, p, r * 0.26f, iris, pupil, true, n));
                                PrimitiveBuilder.Part(head, PrimitiveType.Cube, p + new Vector3(0, r * 0.15f, 0.01f), new Vector3(r * 0.3f, r * 0.04f, r * 0.04f), Dark, "Lid", new Vector3(0, 0, -i * 18f));
                                break;
                            case 2: // つり目
                                eyes.Add(EyeBall(head, p, r * 0.24f, iris, pupil, true, n));
                                PrimitiveBuilder.Part(head, PrimitiveType.Cube, p + new Vector3(0, r * 0.14f, 0.01f), new Vector3(r * 0.32f, r * 0.04f, r * 0.04f), Dark, "Lid", new Vector3(0, 0, i * 20f));
                                break;
                            case 3: // きらきら
                                {
                                    var e = EyeBall(head, p, r * 0.32f, iris, pupil, true, n);
                                    eyes.Add(e);
                                    PrimitiveBuilder.Part(e, PrimitiveType.Sphere, new Vector3(0.2f, -0.25f, 0.4f), Vector3.one * 0.18f, White, "Glint2");
                                    break;
                                }
                            case 4: // にっこり目（弧）
                                {
                                    var arc = PrimitiveBuilder.Empty(n, head, p + new Vector3(0, 0, 0.01f)).transform;
                                    PrimitiveBuilder.Part(arc, PrimitiveType.Cube, new Vector3(-r * 0.06f, 0, 0), new Vector3(r * 0.14f, r * 0.045f, r * 0.04f), Dark, "ArcA", new Vector3(0, 0, 30));
                                    PrimitiveBuilder.Part(arc, PrimitiveType.Cube, new Vector3(r * 0.06f, 0, 0), new Vector3(r * 0.14f, r * 0.045f, r * 0.04f), Dark, "ArcB", new Vector3(0, 0, -30));
                                    eyes.Add(arc);
                                    break;
                                }
                            case 5: // 点目
                                eyes.Add(PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p, new Vector3(r * 0.1f, r * 0.12f, r * 0.05f), Dark, n).transform);
                                break;
                            case 6: // 大きな瞳
                                eyes.Add(EyeBall(head, p, r * 0.36f, iris, pupil, true, n));
                                break;
                            case 9: // ねむそう
                                {
                                    var e = EyeBall(head, p, r * 0.24f, iris, pupil, false, n);
                                    e.localScale = new Vector3(e.localScale.x, e.localScale.y * 0.45f, e.localScale.z);
                                    eyes.Add(e);
                                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, p + new Vector3(0, r * 0.06f, 0.012f), new Vector3(r * 0.3f, r * 0.03f, r * 0.03f), Dark, "Lid");
                                    break;
                                }
                            default: // まる目
                                eyes.Add(EyeBall(head, p, r * 0.24f, iris, pupil, true, n));
                                break;
                        }
                    }
                    break;
            }
            anim.eyes = eyes.ToArray();
        }

        /// <summary>白目なしのかわいい黒目（虹彩＋瞳＋ハイライト）。</summary>
        private static Transform EyeBall(Transform head, Vector3 pos, float size, Color iris, Color pupil, bool glint, string name)
        {
            var root = PrimitiveBuilder.Part(head, PrimitiveType.Sphere, pos, new Vector3(size * 0.82f, size, size * 0.45f), iris, name);
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(0, -0.05f, 0.25f), new Vector3(0.62f, 0.62f, 0.5f), pupil, "Pupil");
            if (glint) PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(-0.2f, 0.22f, 0.45f), Vector3.one * 0.32f, White, "Glint");
            return root.transform;
        }

        private static void BuildFace(Transform head, CharacterAppearance a, float r)
        {
            var mouthPos = OnFace(r, 0, -r * 0.42f, -0.005f);
            Color hairC = a.hairColor;
            switch (a.faceType)
            {
                case 1: // ぽかん
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, mouthPos, new Vector3(r * 0.14f, r * 0.16f, r * 0.06f), MouthColor, "Mouth");
                    break;
                case 2: // すまし顔
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, mouthPos, new Vector3(r * 0.2f, r * 0.03f, r * 0.03f), MouthColor, "Mouth");
                    break;
                case 3: // ねこ口
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, mouthPos + new Vector3(-r * 0.05f, 0, 0), new Vector3(r * 0.12f, r * 0.03f, r * 0.03f), MouthColor, "MouthA", new Vector3(0, 0, -25));
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, mouthPos + new Vector3(r * 0.05f, 0, 0), new Vector3(r * 0.12f, r * 0.03f, r * 0.03f), MouthColor, "MouthB", new Vector3(0, 0, 25));
                    break;
                case 4: // にやり
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, mouthPos + new Vector3(r * 0.03f, 0, 0), new Vector3(r * 0.22f, r * 0.035f, r * 0.03f), MouthColor, "Mouth", new Vector3(0, 0, 12));
                    break;
                case 7: // キリッと眉
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, mouthPos, new Vector3(r * 0.18f, r * 0.035f, r * 0.03f), MouthColor, "Mouth");
                    for (int i = -1; i <= 1; i += 2)
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, OnFace(r, r * 0.36f * i, r * 0.26f, -0.005f), new Vector3(r * 0.26f, r * 0.06f, r * 0.04f), hairC, "Brow", new Vector3(0, 0, -i * 15f));
                    break;
                case 8: // おちょぼ口
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, mouthPos, new Vector3(r * 0.08f, r * 0.07f, r * 0.05f), new Color(0.95f, 0.45f, 0.55f), "Mouth");
                    break;
                default: // にっこり（0, 5, 6, 9）
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, mouthPos + new Vector3(0, r * 0.02f, 0), new Vector3(r * 0.24f, r * 0.08f, r * 0.05f), MouthColor, "Mouth");
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, mouthPos + new Vector3(0, r * 0.06f, 0.004f), new Vector3(r * 0.26f, r * 0.05f, r * 0.05f), a.skinColor, "MouthTop");
                    break;
            }
            if (a.faceType == 5 || a.faceType == 0)
            {
                for (int i = -1; i <= 1; i += 2)
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, OnFace(r, r * 0.5f * i, -r * 0.25f, -0.01f), new Vector3(r * 0.2f, r * 0.11f, r * 0.05f),
                        a.faceType == 5 ? Cheek : Color.Lerp(a.skinColor, Cheek, 0.35f), "Cheek");
            }
            if (a.faceType == 6)
            {
                Color fr = Color.Lerp(a.skinColor, new Color(0.55f, 0.32f, 0.2f), 0.6f);
                for (int i = -1; i <= 1; i += 2)
                    for (int k = 0; k < 3; k++)
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, OnFace(r, r * (0.42f + 0.08f * k) * i, -r * (0.2f + 0.05f * (k % 2)), -0.005f), Vector3.one * r * 0.05f, fr, "Freckle");
            }
            if (a.faceType == 9)
            {
                for (int i = -1; i <= 1; i += 2)
                {
                    var p = OnFace(r, r * 0.52f * i, -r * 0.22f, -0.005f);
                    PrimitiveBuilder.EmissivePart(head, PrimitiveType.Cube, p, new Vector3(r * 0.1f, r * 0.1f, r * 0.03f), new Color(1f, 0.85f, 0.35f), "StarA").transform.localEulerAngles = new Vector3(0, 0, 45);
                    PrimitiveBuilder.EmissivePart(head, PrimitiveType.Cube, p, new Vector3(r * 0.1f, r * 0.1f, r * 0.03f), new Color(1f, 0.85f, 0.35f), "StarB");
                }
            }
        }

        // ------------------------------------------------------------
        // 髪
        // ------------------------------------------------------------

        private static GameObject Cap(Transform head, float r, Color c)
        {
            return PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.18f, -r * 0.12f), Vector3.one * r * 2.0f, c, "HairCap");
        }

        private static void BuildHair(Transform head, CharacterAppearance a, float r)
        {
            Color c = a.hairColor;
            // 帽子・ヘルメット類の時は頭頂部の盛り上がりを控えめに
            bool hat = a.accessory == 2 || a.accessory == 5 || a.accessory == 9 || a.accessory == 11;
            switch (a.hairStyle)
            {
                case 0: // なし
                    return;
                case 1: // ショート
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(r * 0.1f, r * 0.58f, r * 0.68f), new Vector3(r * 1.3f, r * 0.45f, r * 0.5f), c, "Bangs", new Vector3(0, 0, -10));
                    break;
                case 2: // ボブ
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.55f, r * 0.66f), new Vector3(r * 1.5f, r * 0.45f, r * 0.5f), c, "Bangs");
                    for (int i = -1; i <= 1; i += 2)
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(r * 0.82f * i, -r * 0.2f, -r * 0.08f), new Vector3(r * 0.55f, r * 1.25f, r * 0.95f), c, "Side");
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, -r * 0.25f, -r * 0.55f), new Vector3(r * 1.7f, r * 1.1f, r * 0.9f), c, "Back");
                    break;
                case 3: // ロング
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.56f, r * 0.66f), new Vector3(r * 1.4f, r * 0.42f, r * 0.5f), c, "Bangs");
                    PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(0, -r * 1.0f, -r * 0.55f), new Vector3(r * 1.7f, r * 1.25f, r * 0.6f), c, "LongBack");
                    for (int i = -1; i <= 1; i += 2)
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(r * 0.85f * i, -r * 0.7f, -r * 0.1f), new Vector3(r * 0.42f, r * 0.9f, r * 0.5f), c, "LongSide");
                    break;
                case 4: // ツインテール
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.56f, r * 0.66f), new Vector3(r * 1.4f, r * 0.42f, r * 0.5f), c, "Bangs");
                    for (int i = -1; i <= 1; i += 2)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(r * 1.12f * i, -r * 0.45f, -r * 0.25f), new Vector3(r * 0.45f, r * 0.85f, r * 0.45f), c, "Tail", new Vector3(0, 0, i * 12f));
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(r * 0.98f * i, r * 0.3f, -r * 0.2f), Vector3.one * r * 0.22f, a.accessoryColor, "Tie");
                    }
                    break;
                case 5: // ポニーテール
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.56f, r * 0.66f), new Vector3(r * 1.4f, r * 0.42f, r * 0.5f), c, "Bangs");
                    PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(0, -r * 0.1f, -r * 1.15f), new Vector3(r * 0.5f, r * 0.9f, r * 0.5f), c, "Pony", new Vector3(-28, 0, 0));
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.55f, -r * 0.98f), Vector3.one * r * 0.25f, a.accessoryColor, "Tie");
                    break;
                case 6: // おだんご
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.56f, r * 0.66f), new Vector3(r * 1.4f, r * 0.42f, r * 0.5f), c, "Bangs");
                    if (!hat) PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 1.2f, -r * 0.2f), Vector3.one * r * 0.75f, c, "Bun");
                    else PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, -r * 0.1f, -r * 1.05f), Vector3.one * r * 0.65f, c, "Bun");
                    break;
                case 7: // つんつん
                    Cap(head, r, c);
                    if (!hat)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            float ang = -60f + i * 24f;
                            var rot = Quaternion.Euler(-20f - (i % 2) * 15f, 0, ang);
                            var pos = rot * new Vector3(0, r * 1.05f, 0) + new Vector3(0, 0, -r * 0.1f);
                            PrimitiveBuilder.Part(head, PrimitiveType.Cube, pos, new Vector3(r * 0.3f, r * 0.5f, r * 0.3f), c, "Spike", new Vector3(-20f - (i % 2) * 15f, 45f, ang));
                        }
                    }
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(r * 0.25f, r * 0.62f, r * 0.72f), new Vector3(r * 0.35f, r * 0.35f, r * 0.2f), c, "BangSpike", new Vector3(0, 0, 45));
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(-r * 0.25f, r * 0.62f, r * 0.72f), new Vector3(r * 0.35f, r * 0.35f, r * 0.2f), c, "BangSpike", new Vector3(0, 0, 45));
                    break;
                case 8: // もこもこ
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.4f, -r * 0.3f), new Vector3(r * 2.6f, r * 2.2f, r * 2.3f), c, "Fluffy");
                    for (int i = 0; i < 6; i++)
                    {
                        float ang = i * Mathf.PI * 2f / 6f;
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(Mathf.Cos(ang) * r * 1.1f, r * 0.55f + Mathf.Sin(ang) * r * 0.5f, -r * 0.35f), Vector3.one * r * 0.8f, c, "Puff");
                    }
                    break;
                case 9: // ぱっつん
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.38f, r * 0.76f), new Vector3(r * 1.6f, r * 0.55f, r * 0.36f), c, "BluntBangs");
                    for (int i = -1; i <= 1; i += 2)
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(r * 0.86f * i, -r * 0.35f, -r * 0.1f), new Vector3(r * 0.3f, r * 1.4f, r * 1.0f), c, "Straight");
                    break;
                case 10: // アホ毛
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(r * 0.1f, r * 0.58f, r * 0.68f), new Vector3(r * 1.3f, r * 0.45f, r * 0.5f), c, "Bangs");
                    if (!hat)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(0, r * 1.35f, 0), new Vector3(r * 0.08f, r * 0.25f, r * 0.08f), c, "Ahoge1", new Vector3(0, 0, -20));
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(r * 0.14f, r * 1.6f, 0), new Vector3(r * 0.07f, r * 0.18f, r * 0.07f), c, "Ahoge2", new Vector3(0, 0, -70));
                    }
                    break;
                case 11: // 触角ヘア
                    Cap(head, r, c);
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.56f, r * 0.66f), new Vector3(r * 1.4f, r * 0.42f, r * 0.5f), c, "Bangs");
                    for (int i = -1; i <= 1; i += 2)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(r * 0.55f * i, r * 0.1f, r * 0.75f), new Vector3(r * 0.12f, r * 0.55f, r * 0.12f), c, "Lock", new Vector3(-15, 0, i * 15));
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(r * 0.66f * i, -r * 0.42f, r * 0.82f), Vector3.one * r * 0.18f, c, "LockTip");
                    }
                    break;
            }
        }

        // ------------------------------------------------------------
        // アクセサリー
        // ------------------------------------------------------------

        private static void BuildAccessory(Transform head, Transform visual, CharacterAppearance a, float r, float neckY, BodyShape s)
        {
            Color c = a.accessoryColor;
            switch (a.accessory)
            {
                case 1: // アンテナ
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var stalk = PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(r * 0.42f * i, r * 1.15f, 0), new Vector3(r * 0.06f, r * 0.32f, r * 0.06f), Color.Lerp(c, Dark, 0.3f), "Antenna", new Vector3(0, 0, -i * 18f));
                        PrimitiveBuilder.EmissivePart(head, PrimitiveType.Sphere, new Vector3(r * 0.62f * i, r * 1.48f, 0), Vector3.one * r * 0.26f, c, "AntennaTip");
                    }
                    break;
                case 2: // 帽子（キャップ）
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.55f, -r * 0.05f), new Vector3(r * 2.12f, r * 1.25f, r * 2.12f), c, "CapDome");
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(0, r * 0.52f, r * 0.95f), new Vector3(r * 1.3f, r * 0.08f, r * 0.75f), c * 0.85f, "CapBrim", new Vector3(-8, 0, 0));
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 1.17f, -r * 0.05f), Vector3.one * r * 0.16f, White, "CapButton");
                    break;
                case 3: // ゴーグル
                    PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(0, r * 0.4f, 0), new Vector3(r * 2.08f, r * 0.08f, r * 2.08f), Color.Lerp(c, Dark, 0.5f), "Strap");
                    for (int i = -1; i <= 1; i += 2)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(r * 0.32f * i, r * 0.45f, r * 0.86f), new Vector3(r * 0.42f, r * 0.08f, r * 0.42f), c, "GoggleRim", new Vector3(80, 0, 0));
                        PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(r * 0.32f * i, r * 0.46f, r * 0.9f), new Vector3(r * 0.32f, r * 0.05f, r * 0.32f), new Color(0.55f, 0.85f, 1f), "GoggleLens", new Vector3(80, 0, 0));
                    }
                    break;
                case 4: // リボン
                    {
                        var p = new Vector3(r * 0.55f, r * 0.85f, r * 0.1f);
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p + new Vector3(-r * 0.2f, 0, 0), new Vector3(r * 0.4f, r * 0.3f, r * 0.14f), c, "RibbonL", new Vector3(0, 0, 20));
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p + new Vector3(r * 0.2f, 0, 0), new Vector3(r * 0.4f, r * 0.3f, r * 0.14f), c, "RibbonR", new Vector3(0, 0, -20));
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p, Vector3.one * r * 0.16f, c * 0.85f, "RibbonKnot");
                        break;
                    }
                case 5: // ニット帽
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.45f, -r * 0.05f), new Vector3(r * 2.12f, r * 1.5f, r * 2.12f), c, "Beanie");
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 1.25f, -r * 0.05f), Vector3.one * r * 0.38f, Color.Lerp(c, White, 0.6f), "PomPom");
                    break;
                case 6: // メガネ（レンズは半透明）
                    for (int i = -1; i <= 1; i += 2)
                    {
                        var p = OnFace(r, r * 0.36f * i, -r * 0.04f, 0.02f);
                        PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, p, new Vector3(r * 0.42f, r * 0.01f, r * 0.42f), new Color(0.8f, 0.92f, 1f, 0.3f), "Lens", new Vector3(90, 0, 0));
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, p + new Vector3(0, r * 0.2f, 0), new Vector3(r * 0.42f, r * 0.04f, r * 0.04f), c, "Frame");
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, p + new Vector3(0, -r * 0.2f, 0), new Vector3(r * 0.42f, r * 0.03f, r * 0.03f), c, "Frame");
                    }
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, OnFace(r, 0, r * 0.02f, 0.03f), new Vector3(r * 0.18f, r * 0.03f, r * 0.03f), c, "Bridge");
                    break;
                case 7: // ヘッドホン
                    PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(0, r * 1.08f, 0), new Vector3(r * 0.9f, r * 0.1f, r * 0.18f), c, "Band");
                    for (int i = -1; i <= 1; i += 2)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(r * 0.78f * i, r * 0.75f, 0), new Vector3(r * 0.1f, r * 0.6f, r * 0.18f), c, "BandSide", new Vector3(0, 0, i * 35f));
                        PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(r * 0.98f * i, 0, 0), new Vector3(r * 0.5f, r * 0.12f, r * 0.5f), c * 0.85f, "EarCup", new Vector3(0, 0, 90));
                    }
                    break;
                case 8: // 花かざり
                    {
                        var p = new Vector3(-r * 0.6f, r * 0.72f, r * 0.42f);
                        for (int i = 0; i < 5; i++)
                        {
                            float ang = i * Mathf.PI * 2f / 5f;
                            PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p + new Vector3(Mathf.Cos(ang) * r * 0.16f, Mathf.Sin(ang) * r * 0.16f, 0), Vector3.one * r * 0.18f, c, "Petal");
                        }
                        PrimitiveBuilder.Part(head, PrimitiveType.Sphere, p + new Vector3(0, 0, 0.01f), Vector3.one * r * 0.14f, new Color(1f, 0.9f, 0.35f), "FlowerCenter");
                        break;
                    }
                case 9: // ヘルメット（透明なドーム）
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, new Vector3(0, r * 0.1f, 0), Vector3.one * r * 2.7f, new Color(0.75f, 0.9f, 1f, 0.25f), "Helmet");
                    PrimitiveBuilder.Part(visual, PrimitiveType.Cylinder, new Vector3(0, neckY, 0), new Vector3(s.torsoW * 0.8f, 0.04f, s.torsoD * 1.1f), c, "HelmetCollar");
                    break;
                case 10: // ネコ耳
                    for (int i = -1; i <= 1; i += 2)
                    {
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(r * 0.55f * i, r * 0.88f, -r * 0.05f), new Vector3(r * 0.42f, r * 0.42f, r * 0.12f), c, "CatEar", new Vector3(0, 0, 45f + i * 12f));
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(r * 0.55f * i, r * 0.86f, r * 0.02f), new Vector3(r * 0.24f, r * 0.24f, r * 0.04f), Cheek, "CatEarIn", new Vector3(0, 0, 45f + i * 12f));
                    }
                    break;
                case 11: // 王冠
                    PrimitiveBuilder.Part(head, PrimitiveType.Cylinder, new Vector3(0, r * 1.02f, 0), new Vector3(r * 0.95f, r * 0.14f, r * 0.95f), c, "Crown");
                    for (int i = 0; i < 5; i++)
                    {
                        float ang = i * Mathf.PI * 2f / 5f;
                        PrimitiveBuilder.Part(head, PrimitiveType.Cube, new Vector3(Mathf.Cos(ang) * r * 0.4f, r * 1.25f, Mathf.Sin(ang) * r * 0.4f), Vector3.one * r * 0.16f, c, "CrownPoint", new Vector3(45, ang * Mathf.Rad2Deg, 45));
                    }
                    PrimitiveBuilder.EmissivePart(head, PrimitiveType.Sphere, new Vector3(0, r * 1.05f, r * 0.47f), Vector3.one * r * 0.14f, new Color(1f, 0.3f, 0.5f), "Gem");
                    break;
                case 12: // マフラー
                    PrimitiveBuilder.Part(visual, PrimitiveType.Cylinder, new Vector3(0, neckY - 0.02f, 0), new Vector3(s.torsoW * 0.75f, 0.06f, s.torsoD * 1.1f), c, "Scarf");
                    PrimitiveBuilder.Part(visual, PrimitiveType.Cube, new Vector3(s.torsoW * 0.18f, neckY - 0.16f, s.torsoD * 0.52f), new Vector3(0.1f, 0.24f, 0.03f), c * 0.9f, "ScarfTail", new Vector3(0, 0, 8));
                    break;
                case 13: // 星のヘアピン
                    {
                        var p = new Vector3(r * 0.58f, r * 0.62f, r * 0.62f);
                        PrimitiveBuilder.EmissivePart(head, PrimitiveType.Cube, p, new Vector3(r * 0.22f, r * 0.22f, r * 0.06f), c, "StarPinA").transform.localEulerAngles = new Vector3(0, 0, 45);
                        PrimitiveBuilder.EmissivePart(head, PrimitiveType.Cube, p, new Vector3(r * 0.22f, r * 0.22f, r * 0.06f), c, "StarPinB");
                        break;
                    }
                case 14: // つの
                    for (int i = -1; i <= 1; i += 2)
                        PrimitiveBuilder.Part(head, PrimitiveType.Capsule, new Vector3(r * 0.45f * i, r * 0.95f, r * 0.1f), new Vector3(r * 0.16f, r * 0.24f, r * 0.16f), c, "Horn", new Vector3(10, 0, -i * 25f));
                    break;
            }
        }
    }

    /// <summary>
    /// アバターの簡単な動き（歩くと手足が揺れる・まばたき・道具を持つ時だけ柄を出す）。
    /// 親オブジェクトの移動量から歩きを判定するので、マルチの他プレイヤーのアバターでも動く。
    /// コアの身振り（PlayerController.PlayGesture）は Visual ルートを動かすので、ここでは子だけを動かす。
    /// </summary>
    public class AvatarAnimator : MonoBehaviour
    {
        public Transform body, head, legL, legR, armL, armR, toolVisual;
        public Transform[] eyes;
        public GameObject[] toolParts;
        /// <summary>演出用：外部から歩きの強さを指定（-1 なら移動量から自動）。</summary>
        public float forcedWalk = -1f;

        private Vector3 lastPos;
        private float phase;
        private float walk;
        private float blinkTimer;
        private float blinkT = -1f;
        private Vector3[] eyeScales;
        private Quaternion armLBase, armRBase;
        private PlayerController controller;

        private void Start()
        {
            lastPos = transform.position;
            blinkTimer = Random.Range(1.5f, 4f);
            if (eyes != null)
            {
                eyeScales = new Vector3[eyes.Length];
                for (int i = 0; i < eyes.Length; i++) if (eyes[i] != null) eyeScales[i] = eyes[i].localScale;
            }
            if (armL != null) armLBase = armL.localRotation;
            if (armR != null) armRBase = armR.localRotation;
            controller = GetComponentInParent<PlayerController>();
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            var pos = transform.position;
            var d = pos - lastPos;
            d.y = 0f;
            lastPos = pos;
            float speed = d.magnitude / dt;
            float target = forcedWalk >= 0f ? forcedWalk : Mathf.Clamp01(speed / 5f);
            walk = Mathf.MoveTowards(walk, target, dt * 4f);
            phase += dt * (6f + 6f * walk);

            float swing = Mathf.Sin(phase) * 38f * walk;
            if (legL != null) legL.localRotation = Quaternion.Euler(swing, 0, 0);
            if (legR != null) legR.localRotation = Quaternion.Euler(-swing, 0, 0);
            if (armL != null) armL.localRotation = armLBase * Quaternion.Euler(-swing * 0.8f, 0, 0);
            if (armR != null) armR.localRotation = armRBase * Quaternion.Euler(swing * 0.8f, 0, 0);
            if (body != null)
            {
                float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.04f * walk + Mathf.Sin(Time.time * 2f) * 0.006f * (1f - walk);
                var lp = body.localPosition;
                body.localPosition = new Vector3(lp.x, lp.y, lp.z) + Vector3.up * (bob - lastBob);
                lastBob = bob;
            }
            if (head != null) head.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(phase * 0.5f) * 4f * walk);

            // まばたき
            if (eyes != null && eyeScales != null)
            {
                blinkTimer -= dt;
                if (blinkTimer <= 0f && blinkT < 0f) blinkT = 0f;
                if (blinkT >= 0f)
                {
                    blinkT += dt;
                    float k = blinkT < 0.07f ? 1f - blinkT / 0.07f * 0.9f : blinkT < 0.14f ? 0.1f + (blinkT - 0.07f) / 0.07f * 0.9f : 1f;
                    for (int i = 0; i < eyes.Length; i++)
                        if (eyes[i] != null) eyes[i].localScale = new Vector3(eyeScales[i].x, eyeScales[i].y * k, eyeScales[i].z);
                    if (blinkT >= 0.14f)
                    {
                        blinkT = -1f;
                        blinkTimer = Random.Range(2f, 5f);
                    }
                }
            }

            // 道具の柄：道具を構えている時だけ表示
            if (toolParts != null)
            {
                bool show = false;
                if (controller != null && controller.Player != null)
                {
                    var p = controller.Player;
                    show = p.SelectedItem == null && p.Tools.equipped != ToolType.Hand;
                }
                for (int i = 0; i < toolParts.Length; i++)
                    if (toolParts[i] != null && toolParts[i].activeSelf != show) toolParts[i].SetActive(show);
            }
        }

        private float lastBob;
    }
}
