using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.Visual;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>アバターの部品への参照（表情・身振りのアニメーション用）。</summary>
    public sealed class ResidentRig
    {
        public Transform Root;
        public Transform Visual;      // 上下の揺れ・回転（身振り全体）
        public Transform Body;        // 体（伸び縮み）
        public Transform Head;        // 頭の向き（うなずき・首振り）。まんまる系は体と同じ
        public Transform Face;
        public Transform EyeL, EyeR, Mouth, BrowL, BrowR, CheekL, CheekR;
        public Vector3 EyeBaseScale, MouthBaseScale, EyeLBasePos, EyeRBasePos, MouthBasePos;
        public readonly List<Transform> ArmsLeft = new List<Transform>();
        public readonly List<Transform> ArmsRight = new List<Transform>();
        public readonly List<Transform> Legs = new List<Transform>();
        public readonly List<Transform> Ears = new List<Transform>();
        public readonly List<Transform> Antennae = new List<Transform>();
        public readonly List<Transform> Tentacles = new List<Transform>();
        public readonly List<Transform> GlowParts = new List<Transform>();
        public Transform Tail;
        public Transform HandRight;   // 物を持つ位置
        public Transform PropHolder;  // 仕事道具・持ち物
        public Transform OverHead;    // 数を示す点・ハートなどを出す位置
        public float Height = 1.2f;
        public float BaseVisualY;
        public bool HeadIsBody;
        public bool HasArms;
        public bool Bouncy;           // 跳ねて移動（まんまる・もちもち）
        public bool Floats;
    }

    /// <summary>
    /// プリミティブで住民の見た目を組み立てる（キャラデザ §2 の10テーマ。アート制作時に差し替える）。
    /// 正面は +Z。
    /// </summary>
    public static class ResidentAvatarBuilder
    {
        private static readonly Color Dark = new Color(0.08f, 0.08f, 0.12f);
        private static readonly Color Blush = new Color(1f, 0.55f, 0.65f, 1f);

        public static ResidentRig Build(Transform root, ResidentProfile profile, ResidentJob job)
        {
            var a = profile.appearance;
            var rig = new ResidentRig { Root = root };
            float s = Mathf.Clamp(a.size, 0.5f, 1.4f);
            rig.Visual = PrimitiveBuilder.Empty("Visual", root, Vector3.zero).transform;
            rig.Body = PrimitiveBuilder.Empty("BodyPivot", rig.Visual, Vector3.zero).transform;

            AlienLineage shape = a.lineage == AlienLineage.Mixed ? a.secondary : a.lineage;
            Color body = a.bodyColor;
            if (!a.translucent) body.a = 1f;

            float headRadius;
            switch (shape)
            {
                case AlienLineage.Round:
                case AlienLineage.Mochi:
                {
                    // まんまる／もちもち：体＝頭。短い足、跳ねて移動
                    float w = (shape == AlienLineage.Mochi ? 1.05f : 0.9f) * s;
                    float hgt = w * a.squash;
                    var b = PrimitiveBuilder.Part(rig.Body, PrimitiveType.Sphere, new Vector3(0, hgt * 0.5f + 0.05f * s, 0), new Vector3(w, hgt, w), body, "Body");
                    if (shape == AlienLineage.Mochi)
                    {
                        // ぷるぷるの中の模様
                        PrimitiveBuilder.Part(rig.Body, PrimitiveType.Sphere, new Vector3(0, hgt * 0.45f, 0.05f * s), new Vector3(w, hgt, w) * 0.45f, a.patternColor, "Core");
                    }
                    rig.Head = PrimitiveBuilder.Empty("HeadPivot", rig.Body, new Vector3(0, hgt * 0.58f + 0.05f * s, 0)).transform;
                    rig.HeadIsBody = true;
                    headRadius = w * 0.5f;
                    rig.Height = hgt + 0.1f * s;
                    rig.Bouncy = true;
                    if (shape == AlienLineage.Round)
                    {
                        for (int side = -1; side <= 1; side += 2)
                            rig.Legs.Add(PrimitiveBuilder.Part(rig.Visual, PrimitiveType.Sphere, new Vector3(side * 0.18f * s, 0.07f * s, 0.05f * s), new Vector3(0.18f, 0.12f, 0.24f) * s, body * 0.85f, "Foot").transform);
                        AddArms(rig, s, new Vector3(0.42f * w, hgt * 0.45f, 0), 0.16f * s, body, 2, true);
                    }
                    else
                    {
                        rig.HasArms = false;
                    }
                    break;
                }
                case AlienLineage.Floating:
                {
                    // ぷかぷか：足のない丸い体とひらひら
                    float w = 0.8f * s;
                    float baseY = Mathf.Max(0.35f, a.floatHeight) + w * 0.5f;
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Sphere, new Vector3(0, baseY, 0), new Vector3(w, w * 0.9f, w), body, "Body");
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Cylinder, new Vector3(0, baseY - w * 0.3f, 0), new Vector3(w * 1.05f, 0.03f, w * 1.05f), a.accentColor, "Skirt");
                    int n = 5;
                    for (int i = 0; i < n; i++)
                    {
                        float ang = i * Mathf.PI * 2f / n;
                        var pivot = PrimitiveBuilder.Empty("Tentacle", rig.Body, new Vector3(Mathf.Cos(ang) * w * 0.3f, baseY - w * 0.35f, Mathf.Sin(ang) * w * 0.3f)).transform;
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(0, -0.18f * s, 0), new Vector3(0.07f, 0.18f, 0.07f) * s, a.accentColor, "T");
                        rig.Tentacles.Add(pivot);
                    }
                    rig.Head = PrimitiveBuilder.Empty("HeadPivot", rig.Body, new Vector3(0, baseY + 0.05f * s, 0)).transform;
                    rig.HeadIsBody = true;
                    headRadius = w * 0.5f;
                    rig.Height = baseY + w * 0.5f;
                    rig.Floats = true;
                    break;
                }
                case AlienLineage.ChibiAnimal when a.fourLegged:
                {
                    // 四足のちび動物×宇宙人
                    float len = 0.75f * s;
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Capsule, new Vector3(0, 0.42f * s, 0), new Vector3(0.45f * s, len * 0.5f, 0.45f * s), body, "Body", new Vector3(90, 0, 0));
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (i % 2 == 0 ? -1 : 1) * 0.15f * s;
                        float z = (i < 2 ? 1 : -1) * 0.22f * s;
                        var leg = PrimitiveBuilder.Empty("Leg", rig.Visual, new Vector3(x, 0.3f * s, z)).transform;
                        PrimitiveBuilder.Part(leg, PrimitiveType.Cylinder, new Vector3(0, -0.13f * s, 0), new Vector3(0.11f, 0.13f, 0.11f) * s, body * 0.9f, "L");
                        rig.Legs.Add(leg);
                    }
                    float hr = 0.5f * s * a.headScale;
                    rig.Head = PrimitiveBuilder.Empty("HeadPivot", rig.Body, new Vector3(0, 0.72f * s, 0.38f * s)).transform;
                    PrimitiveBuilder.Part(rig.Head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * hr, body, "Head");
                    headRadius = hr * 0.5f;
                    rig.Height = 0.72f * s + hr * 0.5f;
                    rig.HasArms = false;
                    break;
                }
                default:
                {
                    // 二足：体＋頭（ふわふわ・多腕・星の子・小物・表情・二足のちび動物）
                    float bw = (shape == AlienLineage.Plush ? 0.62f : 0.55f) * s;
                    float bh = 0.42f * s;
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Capsule, new Vector3(0, 0.45f * s, 0), new Vector3(bw, bh, bw * 0.85f), body, "Body");
                    if (shape == AlienLineage.Plush)
                    {
                        // 毛並みのふくらみ
                        for (int i = 0; i < 4; i++)
                        {
                            float ang = i * Mathf.PI * 0.5f + 0.4f;
                            PrimitiveBuilder.Part(rig.Body, PrimitiveType.Sphere, new Vector3(Mathf.Cos(ang) * bw * 0.42f, 0.32f * s, Mathf.Sin(ang) * bw * 0.36f), Vector3.one * 0.22f * s, Color.Lerp(body, Color.white, 0.2f), "Fluff");
                        }
                    }
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var leg = PrimitiveBuilder.Empty("Leg", rig.Visual, new Vector3(side * 0.13f * s, 0.18f * s, 0)).transform;
                        PrimitiveBuilder.Part(leg, PrimitiveType.Capsule, new Vector3(0, -0.08f * s, 0.02f * s), new Vector3(0.14f, 0.1f, 0.16f) * s, body * 0.85f, "L");
                        rig.Legs.Add(leg);
                    }
                    float hr = 0.62f * s * a.headScale;
                    rig.Head = PrimitiveBuilder.Empty("HeadPivot", rig.Body, new Vector3(0, 0.82f * s + hr * 0.42f, 0)).transform;
                    PrimitiveBuilder.Part(rig.Head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * hr, body, "Head");
                    headRadius = hr * 0.5f;
                    rig.Height = 0.82f * s + hr * 0.92f;
                    int arms = Mathf.Max(2, a.armCount);
                    AddArms(rig, s, new Vector3(bw * 0.5f, 0.62f * s, 0), 0.22f * s, body, arms, false);
                    break;
                }
            }

            // ミックス系：2つ目の特徴（多腕・浮遊のひらひら・星の冠など）を上乗せ
            if (a.lineage == AlienLineage.Mixed)
            {
                if (a.armCount >= 4 && rig.ArmsLeft.Count < 2 && rig.HasArms)
                    AddArms(rig, s, new Vector3(0.3f * s, 0.4f * s, 0), 0.18f * s, body, 2, false);
                if (a.floatHeight > 0.1f && !rig.Floats)
                {
                    rig.Floats = true;
                    rig.Visual.localPosition = new Vector3(0, a.floatHeight * 0.6f, 0);
                }
            }

            BuildFace(rig, headRadius, a);
            BuildEars(rig, headRadius, a, body);
            BuildAntenna(rig, headRadius, a);
            BuildTail(rig, s, a, body, shape);
            BuildPattern(rig, s, a, shape);
            BuildAccessory(rig, headRadius, s, a, shape);

            rig.OverHead = PrimitiveBuilder.Empty("OverHead", rig.Visual, new Vector3(0, rig.Height + 0.35f, 0)).transform;
            rig.PropHolder = PrimitiveBuilder.Empty("Props", rig.Visual, new Vector3(0, 0, 0)).transform;
            if (rig.HandRight == null)
                rig.HandRight = PrimitiveBuilder.Empty("HoldPoint", rig.Visual, new Vector3(0, rig.Height * 0.45f, 0.42f * s)).transform;
            rig.BaseVisualY = rig.Visual.localPosition.y;
            if (rig.Floats && a.lineage != AlienLineage.Mixed) rig.BaseVisualY = 0f;
            return rig;
        }

        private static void AddArms(ResidentRig rig, float s, Vector3 shoulder, float length, Color color, int count, bool nubs)
        {
            rig.HasArms = true;
            int pairs = Mathf.Max(1, count / 2);
            for (int p = 0; p < pairs; p++)
            {
                float yOff = -p * 0.16f * s;
                for (int side = -1; side <= 1; side += 2)
                {
                    var pivot = PrimitiveBuilder.Empty(side < 0 ? "ArmL" : "ArmR", rig.Body, new Vector3(side * shoulder.x, shoulder.y + yOff, shoulder.z)).transform;
                    if (nubs)
                    {
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(side * 0.05f * s, -0.04f * s, 0), new Vector3(0.14f, 0.12f, 0.14f) * s, color * 0.92f, "Nub");
                    }
                    else
                    {
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(0, -length * 0.5f, 0), new Vector3(0.09f * s, length * 0.5f, 0.09f * s), color * 0.92f, "Arm");
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, -length, 0), Vector3.one * 0.12f * s, color * 0.98f, "Hand");
                    }
                    pivot.localEulerAngles = new Vector3(0, 0, side * 12f);
                    if (side < 0) rig.ArmsLeft.Add(pivot);
                    else
                    {
                        rig.ArmsRight.Add(pivot);
                        if (rig.HandRight == null)
                            rig.HandRight = PrimitiveBuilder.Empty("HoldPoint", pivot, new Vector3(0, -length - 0.05f * s, 0.08f * s)).transform;
                    }
                }
            }
        }

        private static void BuildFace(ResidentRig rig, float r, ResidentAppearance a)
        {
            rig.Face = PrimitiveBuilder.Empty("Face", rig.Head, Vector3.zero).transform;
            float z = r * 0.9f;
            float es = Mathf.Clamp(a.eyeScale, 0.7f, 2f);
            rig.EyeBaseScale = new Vector3(0.2f, 0.26f, 0.12f) * r * es;
            rig.EyeLBasePos = new Vector3(-0.36f * r, 0.12f * r, z);
            rig.EyeRBasePos = new Vector3(0.36f * r, 0.12f * r, z);
            rig.EyeL = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Sphere, rig.EyeLBasePos, rig.EyeBaseScale, a.eyeColor, "EyeL").transform;
            rig.EyeR = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Sphere, rig.EyeRBasePos, rig.EyeBaseScale, a.eyeColor, "EyeR").transform;
            // 瞳のハイライト（目の子なので表情で一緒に変形する）
            PrimitiveBuilder.Part(rig.EyeL, PrimitiveType.Sphere, new Vector3(0.2f, 0.25f, 0.4f), Vector3.one * 0.35f, Color.white, "Shine");
            PrimitiveBuilder.Part(rig.EyeR, PrimitiveType.Sphere, new Vector3(0.2f, 0.25f, 0.4f), Vector3.one * 0.35f, Color.white, "Shine");
            rig.MouthBaseScale = new Vector3(0.24f, 0.06f, 0.06f) * r * Mathf.Lerp(1f, 1.4f, es - 1f);
            rig.MouthBasePos = new Vector3(0, -0.3f * r, z * 0.97f);
            rig.Mouth = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Sphere, rig.MouthBasePos, rig.MouthBaseScale, new Color(0.45f, 0.12f, 0.18f), "Mouth").transform;
            rig.BrowL = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Cube, new Vector3(-0.36f * r, 0.42f * r, z * 0.95f), new Vector3(0.24f, 0.05f, 0.04f) * r * es, Dark, "BrowL").transform;
            rig.BrowR = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Cube, new Vector3(0.36f * r, 0.42f * r, z * 0.95f), new Vector3(0.24f, 0.05f, 0.04f) * r * es, Dark, "BrowR").transform;
            rig.CheekL = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Sphere, new Vector3(-0.58f * r, -0.12f * r, z * 0.82f), new Vector3(0.2f, 0.12f, 0.06f) * r, Blush, "CheekL").transform;
            rig.CheekR = PrimitiveBuilder.Part(rig.Face, PrimitiveType.Sphere, new Vector3(0.58f * r, -0.12f * r, z * 0.82f), new Vector3(0.2f, 0.12f, 0.06f) * r, Blush, "CheekR").transform;
            rig.BrowL.gameObject.SetActive(false);
            rig.BrowR.gameObject.SetActive(false);
            rig.CheekL.gameObject.SetActive(false);
            rig.CheekR.gameObject.SetActive(false);
        }

        private static void BuildEars(ResidentRig rig, float r, ResidentAppearance a, Color body)
        {
            if (a.earType <= 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                var pivot = PrimitiveBuilder.Empty("Ear", rig.Head, new Vector3(side * r * 0.55f, r * 0.75f, 0)).transform;
                switch (a.earType)
                {
                    case 1: // 丸耳
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(0, r * 0.1f, 0), new Vector3(0.45f, 0.45f, 0.2f) * r, body, "E");
                        break;
                    case 2: // 長耳
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(0, r * 0.45f, 0), new Vector3(0.25f, 0.5f, 0.14f) * r, body, "E");
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(0, r * 0.45f, 0.04f * r), new Vector3(0.13f, 0.38f, 0.08f) * r, a.accentColor, "In");
                        pivot.localEulerAngles = new Vector3(0, 0, -side * 10f);
                        break;
                    case 3: // 三角耳
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Cube, new Vector3(0, r * 0.15f, 0), new Vector3(0.32f, 0.32f, 0.1f) * r, body, "E", new Vector3(0, 0, 45));
                        break;
                    default: // 垂れ耳
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(side * r * 0.25f, -r * 0.35f, 0), new Vector3(0.24f, 0.45f, 0.12f) * r, body * 0.92f, "E", new Vector3(0, 0, side * 25f));
                        pivot.localPosition = new Vector3(side * r * 0.75f, r * 0.45f, 0);
                        break;
                }
                rig.Ears.Add(pivot);
            }
        }

        private static void BuildAntenna(ResidentRig rig, float r, ResidentAppearance a)
        {
            if (a.lineage == AlienLineage.StarChild || a.glows)
            {
                // 星の冠：感情で揺れて光る
                var crown = PrimitiveBuilder.Empty("Crown", rig.Head, new Vector3(0, r * 1.02f, 0)).transform;
                int n = 5;
                for (int i = 0; i < n; i++)
                {
                    float ang = i * Mathf.PI * 2f / n;
                    var star = PrimitiveBuilder.EmissivePart(crown, PrimitiveType.Cube, new Vector3(Mathf.Cos(ang) * r * 0.42f, 0.05f * r, Mathf.Sin(ang) * r * 0.42f), Vector3.one * 0.2f * r, new Color(1f, 0.92f, 0.45f), "Star");
                    star.transform.localEulerAngles = new Vector3(45, ang * Mathf.Rad2Deg, 45);
                    rig.GlowParts.Add(star.transform);
                }
                rig.Antennae.Add(crown);
            }
            if (a.antennaType <= 0) return;
            for (int side = -1; side <= 1; side += 2)
            {
                var pivot = PrimitiveBuilder.Empty("Antenna", rig.Head, new Vector3(side * r * 0.3f, r * 0.85f, 0)).transform;
                switch (a.antennaType)
                {
                    case 1:
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Cylinder, new Vector3(0, r * 0.25f, 0), new Vector3(0.05f, 0.25f, 0.05f) * r, Dark, "Stalk");
                        PrimitiveBuilder.EmissivePart(pivot, PrimitiveType.Sphere, new Vector3(0, r * 0.52f, 0), Vector3.one * 0.18f * r, a.accentColor, "Tip");
                        pivot.localEulerAngles = new Vector3(0, 0, -side * 15f);
                        break;
                    case 2:
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Cube, new Vector3(0, r * 0.12f, 0), new Vector3(0.12f, 0.24f, 0.12f) * r, a.accentColor, "Horn", new Vector3(0, 45, side * 10f));
                        break;
                    default:
                        // 月の角（三日月を3つの球で）
                        for (int k = 0; k < 3; k++)
                        {
                            float t = k / 2f;
                            var p = PrimitiveBuilder.EmissivePart(pivot, PrimitiveType.Sphere, new Vector3(side * Mathf.Sin(t * 1.2f) * r * 0.25f, r * (0.1f + t * 0.35f), 0), Vector3.one * (0.16f - t * 0.05f) * r, new Color(1f, 0.95f, 0.7f), "Moon");
                            rig.GlowParts.Add(p.transform);
                        }
                        break;
                }
                rig.Antennae.Add(pivot);
            }
        }

        private static void BuildTail(ResidentRig rig, float s, ResidentAppearance a, Color body, AlienLineage shape)
        {
            if (a.tailType <= 0) return;
            float z = shape == AlienLineage.ChibiAnimal && a.fourLegged ? -0.42f * s : -0.28f * s;
            float y = shape == AlienLineage.ChibiAnimal && a.fourLegged ? 0.5f * s : 0.35f * s;
            rig.Tail = PrimitiveBuilder.Empty("Tail", rig.Body, new Vector3(0, y, z)).transform;
            switch (a.tailType)
            {
                case 1:
                    PrimitiveBuilder.Part(rig.Tail, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.18f * s, a.accentColor, "T");
                    break;
                case 2:
                    PrimitiveBuilder.Part(rig.Tail, PrimitiveType.Capsule, new Vector3(0, 0.12f * s, -0.08f * s), new Vector3(0.08f, 0.18f, 0.08f) * s, body, "T", new Vector3(-35, 0, 0));
                    PrimitiveBuilder.Part(rig.Tail, PrimitiveType.Sphere, new Vector3(0, 0.27f * s, -0.18f * s), Vector3.one * 0.12f * s, a.accentColor, "Tip");
                    break;
                default:
                    PrimitiveBuilder.Part(rig.Tail, PrimitiveType.Sphere, new Vector3(0, 0.05f * s, -0.05f * s), new Vector3(0.3f, 0.34f, 0.3f) * s, Color.Lerp(body, Color.white, 0.35f), "T");
                    break;
            }
        }

        private static void BuildPattern(ResidentRig rig, float s, ResidentAppearance a, AlienLineage shape)
        {
            if (a.pattern <= 0) return;
            Transform anchor = rig.HeadIsBody ? rig.Head : rig.Body;
            float frontZ = rig.HeadIsBody ? 0.42f * s : 0.24f * s;
            float y = rig.HeadIsBody ? -0.22f * s : 0.45f * s;
            switch (a.pattern)
            {
                case 1:
                    for (int i = 0; i < 3; i++)
                        PrimitiveBuilder.Part(anchor, PrimitiveType.Sphere, new Vector3((i - 1) * 0.16f * s, y + (i % 2) * 0.08f * s, frontZ * 0.92f), Vector3.one * 0.08f * s, a.patternColor, "Dot");
                    break;
                case 2:
                    PrimitiveBuilder.Part(anchor, PrimitiveType.Sphere, new Vector3(0, y, frontZ * 0.8f), new Vector3(0.32f, 0.3f, 0.12f) * s, a.patternColor, "Belly");
                    break;
                default:
                    for (int i = 0; i < 3; i++)
                    {
                        var st = PrimitiveBuilder.EmissivePart(anchor, PrimitiveType.Cube, new Vector3((i - 1) * 0.15f * s, y + Mathf.Abs(i - 1) * 0.06f * s, frontZ * 0.92f), Vector3.one * 0.07f * s, new Color(1f, 0.95f, 0.6f), "StarMark");
                        st.transform.localEulerAngles = new Vector3(0, 0, 45);
                        rig.GlowParts.Add(st.transform);
                    }
                    break;
            }
        }

        private static void BuildAccessory(ResidentRig rig, float r, float s, ResidentAppearance a, AlienLineage shape)
        {
            var c = a.accessoryColor;
            switch (a.accessory)
            {
                case 1: // 帽子
                    PrimitiveBuilder.Part(rig.Head, PrimitiveType.Cylinder, new Vector3(0, r * 0.88f, 0), new Vector3(1.3f, 0.04f, 1.3f) * r, c, "Brim");
                    PrimitiveBuilder.Part(rig.Head, PrimitiveType.Cylinder, new Vector3(0, r * 1.12f, 0), new Vector3(0.8f, 0.25f, 0.8f) * r, c, "Hat");
                    break;
                case 2: // エプロン
                    if (rig.HeadIsBody) PrimitiveBuilder.Part(rig.Head, PrimitiveType.Cube, new Vector3(0, -r * 0.45f, r * 0.82f), new Vector3(0.7f, 0.55f, 0.06f) * r, c, "Apron");
                    else PrimitiveBuilder.Part(rig.Body, PrimitiveType.Cube, new Vector3(0, 0.42f * s, 0.24f * s), new Vector3(0.38f, 0.42f, 0.04f) * s, c, "Apron");
                    break;
                case 3: // かばん
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Cube, new Vector3(-0.36f * s, 0.38f * s, 0.05f * s), new Vector3(0.18f, 0.2f, 0.12f) * s, c, "Bag");
                    PrimitiveBuilder.Part(rig.Body, PrimitiveType.Cube, new Vector3(-0.12f * s, 0.62f * s, 0.12f * s), new Vector3(0.04f, 0.5f, 0.03f) * s, c * 0.8f, "Strap", new Vector3(0, 0, -40));
                    break;
                case 4: // マフラー
                    if (!rig.HeadIsBody)
                        PrimitiveBuilder.Part(rig.Body, PrimitiveType.Cylinder, new Vector3(0, 0.8f * s, 0), new Vector3(0.48f, 0.06f, 0.48f) * s, c, "Scarf");
                    else
                        PrimitiveBuilder.Part(rig.Head, PrimitiveType.Cylinder, new Vector3(0, -r * 0.35f, 0), new Vector3(2.05f, 0.12f, 2.05f) * r, c, "Scarf");
                    break;
                case 5: // 眼鏡
                    for (int side = -1; side <= 1; side += 2)
                        PrimitiveBuilder.Part(rig.Face, PrimitiveType.Cylinder, new Vector3(side * 0.36f * r, 0.12f * r, r * 0.98f), new Vector3(0.34f, 0.02f, 0.34f) * r, new Color(0.3f, 0.25f, 0.2f), "Glass", new Vector3(90, 0, 0));
                    PrimitiveBuilder.Part(rig.Face, PrimitiveType.Cube, new Vector3(0, 0.14f * r, r * 1.0f), new Vector3(0.22f, 0.03f, 0.03f) * r, new Color(0.3f, 0.25f, 0.2f), "Bridge");
                    break;
                case 6: // リボン
                    for (int side = -1; side <= 1; side += 2)
                        PrimitiveBuilder.Part(rig.Head, PrimitiveType.Cube, new Vector3(r * 0.45f + side * 0.12f * r, r * 0.8f, 0), new Vector3(0.22f, 0.16f, 0.06f) * r, c, "Ribbon", new Vector3(0, 0, side * 30f));
                    break;
            }
        }

        // ---------------- 小物（仕事道具・持ち物・箱） ----------------

        /// <summary>仕事道具の小物（生活感のある小物系：キャラデザ §2-08）。</summary>
        public static GameObject BuildJobProp(Transform parent, ResidentJob job, float s)
        {
            var root = PrimitiveBuilder.Empty("JobProp", parent, Vector3.zero);
            var t = root.transform;
            Color wood = new Color(0.55f, 0.38f, 0.22f);
            Color metal = new Color(0.7f, 0.72f, 0.78f);
            switch (job)
            {
                case ResidentJob.Blacksmith:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.12f, 0), new Vector3(0.04f, 0.14f, 0.04f) * s, wood, "Handle");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.27f * s, 0), new Vector3(0.18f, 0.08f, 0.08f) * s, metal, "Head");
                    break;
                case ResidentJob.Mechanic:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.12f * s, 0), new Vector3(0.04f, 0.24f, 0.03f) * s, metal, "Wrench");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.26f * s, 0), new Vector3(0.1f, 0.02f, 0.1f) * s, metal, "Jaw", new Vector3(90, 0, 0));
                    break;
                case ResidentJob.FishingMaster:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.5f * s, 0.25f * s), new Vector3(0.025f, 0.55f, 0.025f) * s, wood, "Rod", new Vector3(35, 0, 0));
                    break;
                case ResidentJob.Florist:
                case ResidentJob.SeedSeller:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.08f * s, 0), new Vector3(0.14f, 0.08f, 0.14f) * s, new Color(0.75f, 0.45f, 0.3f), "Pot");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.22f * s, 0), Vector3.one * 0.12f * s, new Color(1f, 0.55f, 0.75f), "Flower");
                    break;
                case ResidentJob.Cook:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.14f * s, 0), new Vector3(0.025f, 0.14f, 0.025f) * s, metal, "Ladle");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.0f, 0.04f * s), new Vector3(0.1f, 0.05f, 0.1f) * s, metal, "Bowl");
                    break;
                case ResidentJob.Carpenter:
                case ResidentJob.Builder:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.14f * s, 0), new Vector3(0.03f, 0.28f, 0.12f) * s, metal, "Saw");
                    break;
                case ResidentJob.WeatherReader:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.18f * s, 0), new Vector3(0.025f, 0.18f, 0.025f) * s, wood, "Stick");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0, 0.38f * s, 0), Vector3.one * 0.1f * s, new Color(0.6f, 0.85f, 1f), "Orb");
                    break;
                case ResidentJob.Courier:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.05f * s, 0), new Vector3(0.18f, 0.12f, 0.03f) * s, new Color(1f, 0.95f, 0.8f), "Letter");
                    break;
                case ResidentJob.Musician:
                    PrimitiveBuilder.Part(t, PrimitiveType.Capsule, new Vector3(0, 0.1f * s, 0), new Vector3(0.12f, 0.12f, 0.06f) * s, wood, "Instrument");
                    break;
                case ResidentJob.Miner:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.14f * s, 0), new Vector3(0.03f, 0.16f, 0.03f) * s, wood, "Handle");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.3f * s, 0), new Vector3(0.26f, 0.04f, 0.04f) * s, metal, "Pick");
                    break;
                default:
                    Object.Destroy(root);
                    return null;
            }
            return root;
        }

        /// <summary>アイテムの簡易モデル（差し出す・食べる・抱える）。</summary>
        public static GameObject BuildItemProp(Transform parent, string itemId, float size = 0.22f)
        {
            var def = ItemRegistry.GetOrPlaceholder(itemId);
            var go = PrimitiveBuilder.Part(parent, def.Shape == PrimitiveType.Plane || def.Shape == PrimitiveType.Quad ? PrimitiveType.Cube : def.Shape, Vector3.zero, Vector3.one * size, def.Color, "Item_" + itemId);
            return go;
        }

        /// <summary>引っ越しの荷物の箱。</summary>
        public static GameObject BuildBox(Transform parent, Vector3 worldPos, float rotY, float scale, Color tape)
        {
            var box = PrimitiveBuilder.Create(PrimitiveType.Cube, parent, worldPos + Vector3.up * scale * 0.5f, Vector3.one * scale, new Color(0.78f, 0.6f, 0.38f), "MovingBox");
            box.transform.localEulerAngles = new Vector3(0, rotY, 0);
            PrimitiveBuilder.Part(box.transform, PrimitiveType.Cube, new Vector3(0, 0.501f, 0), new Vector3(1.01f, 0.02f, 0.2f), tape, "Tape");
            PrimitiveBuilder.RemoveCollider(box);
            return box;
        }
    }
}
