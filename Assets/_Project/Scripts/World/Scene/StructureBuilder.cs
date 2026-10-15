using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 施設の外観（プリミティブでかわいく分かりやすく）。建物ごとに特徴のある見た目にする。
    /// ・主人公の家：外観のみ（扉の移動は Housing）。・主人公の商店：土台と看板のみ（店舗は Shop）。
    /// ・鉱山入口：外観のみ（中への移動は Mining）。・不時着地：クレーターと残骸のみ（ロケットは Rocket）。
    /// </summary>
    public static class StructureBuilder
    {
        // ---------------- 基本パーツ ----------------

        public static Transform Root(Transform parent, string name, Vector3 pos, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            return go.transform;
        }

        public static GameObject Box(Transform root, Vector3 lp, Vector3 scale, Color c, bool collider = false, string name = null, Vector3? euler = null, PrimitiveType type = PrimitiveType.Cube)
        {
            GameObject go;
            if (collider)
            {
                go = PrimitiveBuilder.Create(type, root, lp, scale, c, name, true);
                if (euler.HasValue) go.transform.localEulerAngles = euler.Value;
            }
            else go = PrimitiveBuilder.Part(root, type, lp, scale, c, name, euler);
            return go;
        }

        public static GameObject Cyl(Transform root, Vector3 lp, Vector3 scale, Color c, bool collider = false, string name = null, Vector3? euler = null)
            => Box(root, lp, scale, c, collider, name, euler, PrimitiveType.Cylinder);

        public static GameObject Ball(Transform root, Vector3 lp, Vector3 scale, Color c, string name = null)
            => Box(root, lp, scale, c, false, name, null, PrimitiveType.Sphere);

        public static GameObject Glow(Transform root, PrimitiveType type, Vector3 lp, Vector3 scale, Color c, string name = null)
            => PrimitiveBuilder.EmissivePart(root, type, lp, scale, c, name);

        /// <summary>切妻屋根。baseY は壁の上端。</summary>
        public static void GabledRoof(Transform root, float baseY, float width, float depth, Color c, float pitch = 30f, float overhang = 0.45f)
        {
            float halfW = width * 0.5f + overhang;
            float rad = pitch * Mathf.Deg2Rad;
            float slopeLen = halfW / Mathf.Cos(rad);
            float rise = halfW * Mathf.Tan(rad);
            Box(root, new Vector3(-halfW * 0.5f, baseY + rise * 0.5f, 0f), new Vector3(slopeLen + 0.1f, 0.22f, depth + overhang * 2f), c, false, "RoofL", new Vector3(0f, 0f, pitch));
            Box(root, new Vector3(halfW * 0.5f, baseY + rise * 0.5f, 0f), new Vector3(slopeLen + 0.1f, 0.22f, depth + overhang * 2f), c, false, "RoofR", new Vector3(0f, 0f, -pitch));
            // 妻壁（屋根の下の三角の隙間を埋める）
            Box(root, new Vector3(0f, baseY + rise * 0.28f, 0f), new Vector3(width * 0.62f, rise * 0.56f, depth - 0.05f), Color.Lerp(c, Color.white, 0.55f), false, "Gable");
            Box(root, new Vector3(0f, baseY + rise * 0.62f, 0f), new Vector3(width * 0.3f, rise * 0.3f, depth - 0.05f), Color.Lerp(c, Color.white, 0.55f), false, "Gable2");
        }

        /// <summary>壁・扉・窓・屋根の基本の家。窓の Renderer を返す（夜に灯す）。</summary>
        public static List<Renderer> BasicBuilding(Transform root, Vector3 size, Color wall, Color roof, bool gabled, Color door, int windowsPerSide = 1)
        {
            Box(root, new Vector3(0f, size.y * 0.5f, 0f), size, wall, true, "Body");
            Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(size.x + 0.3f, 0.24f, size.z + 0.3f), Color.Lerp(wall, Color.gray, 0.5f), false, "Base");
            if (gabled) GabledRoof(root, size.y, size.x, size.z, roof);
            else
            {
                Box(root, new Vector3(0f, size.y + 0.18f, 0f), new Vector3(size.x + 0.7f, 0.36f, size.z + 0.7f), roof, false, "Roof");
                Box(root, new Vector3(0f, size.y + 0.45f, 0f), new Vector3(size.x * 0.6f, 0.3f, size.z * 0.6f), Color.Lerp(roof, Color.white, 0.2f), false, "RoofTop");
            }
            float fz = size.z * 0.5f + 0.03f;
            Box(root, new Vector3(0f, 1.1f, fz), new Vector3(1.2f, 2.2f, 0.12f), door, false, "Door");
            Ball(root, new Vector3(0.4f, 1.1f, fz + 0.08f), Vector3.one * 0.12f, new Color(0.95f, 0.8f, 0.3f), "Knob");
            var glow = new List<Renderer>();
            var glass = new Color(0.55f, 0.6f, 0.68f);
            for (int i = 0; i < windowsPerSide; i++)
            {
                float x = size.x * 0.5f * (0.45f + 0.35f * i / Mathf.Max(1, windowsPerSide));
                foreach (float sx in new[] { -1f, 1f })
                {
                    var wgo = Box(root, new Vector3(sx * x, size.y * 0.55f, fz), new Vector3(0.9f, 0.9f, 0.1f), glass, false, "Window");
                    glow.Add(wgo.GetComponent<Renderer>());
                    Box(root, new Vector3(sx * x, size.y * 0.55f - 0.52f, fz + 0.05f), new Vector3(1.1f, 0.12f, 0.2f), Color.Lerp(wall, Color.white, 0.4f), false, "Sill");
                }
            }
            // 側面の窓
            float sxw = size.x * 0.5f + 0.03f;
            var sw = Box(root, new Vector3(sxw, size.y * 0.55f, 0f), new Vector3(0.1f, 0.9f, 0.9f), glass, false, "WindowSide");
            glow.Add(sw.GetComponent<Renderer>());
            var sw2 = Box(root, new Vector3(-sxw, size.y * 0.55f, 0f), new Vector3(0.1f, 0.9f, 0.9f), glass, false, "WindowSide");
            glow.Add(sw2.GetComponent<Renderer>());
            return glow;
        }

        /// <summary>しま模様のひさし。</summary>
        public static void Awning(Transform root, float width, float y, float z, Color a, Color b, int stripes = 6)
        {
            float sw = width / stripes;
            for (int i = 0; i < stripes; i++)
            {
                float x = -width * 0.5f + sw * (i + 0.5f);
                Box(root, new Vector3(x, y, z + 0.55f), new Vector3(sw + 0.01f, 0.12f, 1.2f), i % 2 == 0 ? a : b, false, "Awning", new Vector3(-18f, 0f, 0f));
                Ball(root, new Vector3(x, y - 0.25f, z + 1.12f), new Vector3(sw, 0.2f, 0.12f), i % 2 == 0 ? a : b, "AwningEdge");
            }
        }

        public static void Crate(Transform root, Vector3 lp, float s, Color c, bool collider = true)
        {
            Box(root, lp + Vector3.up * s * 0.5f, Vector3.one * s, c, collider, "Crate");
            Box(root, lp + new Vector3(0f, s * 0.5f, s * 0.51f), new Vector3(s * 0.9f, s * 0.12f, 0.02f), c * 0.75f, false, "Slat");
        }

        public static void Barrel(Transform root, Vector3 lp, Color c)
        {
            Cyl(root, lp + Vector3.up * 0.5f, new Vector3(0.8f, 0.5f, 0.8f), c, true, "Barrel");
            Cyl(root, lp + Vector3.up * 0.75f, new Vector3(0.84f, 0.04f, 0.84f), c * 0.6f, false, "Hoop");
            Cyl(root, lp + Vector3.up * 0.25f, new Vector3(0.84f, 0.04f, 0.84f), c * 0.6f, false, "Hoop");
        }

        public static void Pot(Transform root, Vector3 lp, Color pot, Color plant)
        {
            Cyl(root, lp + Vector3.up * 0.2f, new Vector3(0.5f, 0.2f, 0.5f), pot, false, "Pot");
            Ball(root, lp + Vector3.up * 0.55f, new Vector3(0.6f, 0.5f, 0.6f), plant, "Plant");
        }

        public static Light AddPointLight(Transform root, Vector3 lp, Color c, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(root, false);
            go.transform.localPosition = lp;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }

        // =================================================================
        // 施設ごとの外観
        // =================================================================

        public static void BuildFacility(FacilityInfo f, Transform parent, WorldPalette P, ulong seed)
        {
            switch (f.type)
            {
                case FacilityType.PlayerHouse: PlayerHouse(f, parent, P); break;
                case FacilityType.PlayerShop: PlayerShopLot(f, parent, P); break;
                case FacilityType.MessagePlaza: MessagePlaza(f, parent, P); break;
                case FacilityType.BulletinBoard: BulletinBoard(f, parent, P); break;
                case FacilityType.Blacksmith: Blacksmith(f, parent, P); break;
                case FacilityType.RepairWorkshop: RepairWorkshop(f, parent, P); break;
                case FacilityType.SeedShop: SeedShop(f, parent, P); break;
                case FacilityType.GeneralStore: GeneralStore(f, parent, P); break;
                case FacilityType.CarpenterWorkshop: Carpenter(f, parent, P); break;
                case FacilityType.FishingHut: FishingHut(f, parent, P); break;
                case FacilityType.CommunityKitchen: Kitchen(f, parent, P); break;
                case FacilityType.Lookout: Lookout(f, parent, P); break;
                case FacilityType.MineEntrance: MineEntrance(f, parent, P); break;
                case FacilityType.RocketSite: CrashSite(f, parent, P, seed); break;
                case FacilityType.Well: Well(f, parent, P); break;
                case FacilityType.Bench: Bench(f, parent, P); break;
                case FacilityType.Streetlight: Streetlight(f, parent, P); break;
            }
        }

        private static readonly Color Wood = new Color(0.62f, 0.44f, 0.28f);
        private static readonly Color DarkWood = new Color(0.42f, 0.29f, 0.18f);
        private static readonly Color Metal = new Color(0.55f, 0.58f, 0.62f);
        private static readonly Color DarkMetal = new Color(0.25f, 0.26f, 0.3f);
        private static readonly Color LampOn = new Color(1f, 0.86f, 0.55f);
        private static readonly Color WindowOn = new Color(1f, 0.85f, 0.5f);
        private static readonly Color Glass = new Color(0.55f, 0.6f, 0.68f);

        private static void PlayerHouse(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "PlayerHouse", f.position, f.rotationY);
            var s = f.size;
            var windows = BasicBuilding(root, s, P.Walls[0], P.Roofs[0], true, Wood, 1);
            Box(root, new Vector3(s.x * 0.28f, s.y + 1.6f, -s.z * 0.15f), new Vector3(0.8f, 2f, 0.8f), Color.Lerp(P.Rock, Color.red, 0.15f), false, "Chimney");
            // ポーチ・郵便受け・植木鉢
            Box(root, new Vector3(0f, 0.15f, s.z * 0.5f + 0.9f), new Vector3(2.6f, 0.3f, 1.6f), Wood, false, "Porch");
            Cyl(root, new Vector3(s.x * 0.5f + 0.8f, 0.6f, s.z * 0.5f + 1.6f), new Vector3(0.12f, 0.6f, 0.12f), DarkWood, false, "MailPost");
            Box(root, new Vector3(s.x * 0.5f + 0.8f, 1.3f, s.z * 0.5f + 1.6f), new Vector3(0.5f, 0.38f, 0.7f), P.Roofs[0], false, "Mailbox");
            Pot(root, new Vector3(-1.2f, 0.3f, s.z * 0.5f + 1.3f), new Color(0.75f, 0.42f, 0.3f), P.Flowers[0]);
            Pot(root, new Vector3(1.2f, 0.3f, s.z * 0.5f + 1.3f), new Color(0.75f, 0.42f, 0.3f), P.Flowers[2]);
            var lamp = Glow(root, PrimitiveType.Sphere, new Vector3(-0.95f, 2.5f, s.z * 0.5f + 0.25f), Vector3.one * 0.3f, LampOn, "PorchLamp");
            var light = AddPointLight(root, new Vector3(-0.95f, 2.4f, s.z * 0.5f + 0.8f), LampOn, 7f, 1.1f);
            var rs = new List<Renderer>(windows) { lamp.GetComponent<Renderer>() };
            NightLight.Attach(root.gameObject, light, WindowOn, Glass, rs.ToArray());
        }

        private static void PlayerShopLot(FacilityInfo f, Transform parent, WorldPalette P)
        {
            // 店舗は Shop モジュールが建てる。ここは土台（石の基礎）だけ。
            var root = Root(parent, "PlayerShopLot", f.position, f.rotationY);
            var s = f.size;
            Box(root, new Vector3(0f, 0.08f, 0f), new Vector3(s.x + 0.6f, 0.16f, s.z + 0.6f), P.Stone, false, "Foundation");
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Box(root, new Vector3(c.x * (s.x * 0.5f + 0.2f), 0.25f, c.y * (s.z * 0.5f + 0.2f)), new Vector3(0.5f, 0.3f, 0.5f), Color.Lerp(P.Stone, Color.gray, 0.4f), false, "Corner");
        }

        private static void MessagePlaza(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "MessagePlaza", f.position, f.rotationY);
            float r = f.size.x * 0.5f;
            Cyl(root, new Vector3(0f, 0.1f, 0f), new Vector3(r * 2f, 0.1f, r * 2f), Color.Lerp(P.Plaza, Color.white, 0.2f), true, "Platform");
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad + 0.52f;
                var lp = new Vector3(Mathf.Cos(a) * (r - 0.5f), 1.6f, Mathf.Sin(a) * (r - 0.5f));
                if (lp.z > r * 0.6f) continue; // 正面は開ける
                Cyl(root, lp, new Vector3(0.28f, 1.6f, 0.28f), P.Walls[2], true, "Pillar");
            }
            var dome = Ball(root, new Vector3(0f, 3.2f, 0f), new Vector3(r * 2.1f, 1.6f, r * 2.1f), P.Roofs[2], "Dome");
            Ball(root, new Vector3(0f, 4.05f, 0f), Vector3.one * 0.5f, P.Flowers[1], "Finial");
            // マネピの止まり木と伝言の札
            Cyl(root, new Vector3(0f, 0.75f, -0.4f), new Vector3(0.18f, 0.75f, 0.18f), Wood, false, "Perch");
            Cyl(root, new Vector3(0f, 1.52f, -0.4f), new Vector3(0.9f, 0.04f, 0.9f), Wood, false, "PerchRing");
            Box(root, new Vector3(0f, 1.3f, -r + 0.8f), new Vector3(2.2f, 1.3f, 0.12f), DarkWood, false, "Board");
            for (int i = 0; i < 4; i++)
                Box(root, new Vector3(-0.7f + i * 0.45f, 1.35f + (i % 2) * 0.2f, -r + 0.88f), new Vector3(0.32f, 0.38f, 0.02f), P.Flowers[i % P.Flowers.Length] * 0.4f + Color.white * 0.6f, false, "Note");
        }

        private static void BulletinBoard(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "BulletinBoard", f.position, f.rotationY);
            Box(root, new Vector3(-1.3f, 1.2f, 0f), new Vector3(0.18f, 2.4f, 0.18f), DarkWood, true, "PostL");
            Box(root, new Vector3(1.3f, 1.2f, 0f), new Vector3(0.18f, 2.4f, 0.18f), DarkWood, true, "PostR");
            Box(root, new Vector3(0f, 1.5f, 0f), new Vector3(2.6f, 1.4f, 0.12f), Wood, true, "Board");
            Box(root, new Vector3(0f, 2.55f, 0f), new Vector3(3f, 0.16f, 0.7f), P.Roofs[3], false, "Cap", new Vector3(-8f, 0f, 0f));
            for (int i = 0; i < 5; i++)
                Box(root, new Vector3(-0.9f + i * 0.45f, 1.45f + ((i * 37) % 3) * 0.12f, 0.08f), new Vector3(0.36f, 0.46f, 0.02f), i % 2 == 0 ? new Color(1f, 0.98f, 0.9f) : new Color(1f, 0.92f, 0.65f), false, "Paper");
        }

        private static void Blacksmith(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Blacksmith", f.position, f.rotationY);
            var s = f.size;
            var stone = Color.Lerp(P.Rock, Color.white, 0.15f);
            var windows = BasicBuilding(root, s, stone, new Color(0.3f, 0.3f, 0.36f), false, DarkWood, 1);
            // 煙突と炉の火
            Box(root, new Vector3(-s.x * 0.3f, s.y + 1.4f, -s.z * 0.2f), new Vector3(1.1f, 2.6f, 1.1f), P.RockDark, false, "Chimney");
            Glow(root, PrimitiveType.Cube, new Vector3(-s.x * 0.3f, s.y + 2.75f, -s.z * 0.2f), new Vector3(0.8f, 0.12f, 0.8f), new Color(1f, 0.5f, 0.15f), "Ember");
            for (int i = 0; i < 3; i++)
            {
                var smoke = Ball(root, new Vector3(-s.x * 0.3f + i * 0.3f, s.y + 3.4f + i * 0.8f, -s.z * 0.2f), Vector3.one * (0.6f + i * 0.25f), new Color(0.75f, 0.75f, 0.78f, 0.55f), "Smoke");
                var gm = smoke.AddComponent<GentleMotion>();
                gm.bobAmplitude = 0.25f;
                gm.bobSpeed = 0.8f + i * 0.3f;
            }
            Glow(root, PrimitiveType.Cube, new Vector3(s.x * 0.5f + 0.6f, 0.6f, s.z * 0.2f), new Vector3(1f, 0.5f, 1f), new Color(1f, 0.45f, 0.1f), "Forge");
            Box(root, new Vector3(s.x * 0.5f + 0.6f, 0.2f, s.z * 0.2f), new Vector3(1.4f, 0.4f, 1.4f), P.RockDark, true, "ForgeBase");
            // 金床
            var anvil = new Vector3(s.x * 0.15f, 0f, s.z * 0.5f + 2.2f);
            Box(root, anvil + new Vector3(2.2f, 0.3f, 0f), new Vector3(0.5f, 0.6f, 0.5f), DarkMetal, true, "AnvilBase");
            Box(root, anvil + new Vector3(2.2f, 0.72f, 0f), new Vector3(1.1f, 0.25f, 0.45f), DarkMetal, false, "AnvilTop");
            Ball(root, anvil + new Vector3(2.75f, 0.74f, 0f), new Vector3(0.4f, 0.2f, 0.3f), DarkMetal, "AnvilHorn");
            Barrel(root, new Vector3(-s.x * 0.5f - 0.8f, 0f, s.z * 0.3f), new Color(0.45f, 0.5f, 0.6f));
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void RepairWorkshop(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "RepairWorkshop", f.position, f.rotationY);
            var s = f.size;
            var wall = Color.Lerp(new Color(0.45f, 0.75f, 0.78f), P.Walls[4], 0.3f);
            var windows = BasicBuilding(root, s, wall, new Color(0.35f, 0.42f, 0.5f), false, DarkMetal, 1);
            // 大きなガレージ扉
            Box(root, new Vector3(-s.x * 0.28f, 1.5f, s.z * 0.5f + 0.04f), new Vector3(2.6f, 3f, 0.1f), new Color(0.6f, 0.62f, 0.66f), false, "Garage");
            for (int i = 0; i < 5; i++)
                Box(root, new Vector3(-s.x * 0.28f, 0.4f + i * 0.6f, s.z * 0.5f + 0.1f), new Vector3(2.6f, 0.06f, 0.04f), DarkMetal, false, "GarageLine");
            // 歯車（ゆっくり回る）
            var gear = new GameObject("Gear");
            gear.transform.SetParent(root, false);
            gear.transform.localPosition = new Vector3(s.x * 0.3f, s.y - 0.9f, s.z * 0.5f + 0.15f);
            gear.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Cyl(gear.transform, Vector3.zero, new Vector3(1.2f, 0.06f, 1.2f), new Color(0.95f, 0.75f, 0.3f), false, "Disc");
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                Box(gear.transform, new Vector3(Mathf.Cos(a) * 0.68f, 0f, Mathf.Sin(a) * 0.68f), new Vector3(0.22f, 0.12f, 0.22f), new Color(0.95f, 0.75f, 0.3f), false, "Tooth");
            }
            var gm = gear.AddComponent<GentleMotion>();
            gm.spinAxis = Vector3.up;
            gm.spinSpeed = 25f;
            // アンテナ
            Cyl(root, new Vector3(s.x * 0.35f, s.y + 1.4f, -s.z * 0.3f), new Vector3(0.08f, 1.4f, 0.08f), Metal, false, "Antenna");
            Glow(root, PrimitiveType.Sphere, new Vector3(s.x * 0.35f, s.y + 2.85f, -s.z * 0.3f), Vector3.one * 0.28f, new Color(0.3f, 1f, 0.6f), "AntennaTip");
            Crate(root, new Vector3(s.x * 0.5f + 0.9f, 0f, s.z * 0.3f), 0.8f, Wood);
            Crate(root, new Vector3(s.x * 0.5f + 0.9f, 0.8f, s.z * 0.3f), 0.6f, Color.Lerp(Wood, Color.white, 0.2f), false);
            Box(root, new Vector3(s.x * 0.5f + 0.7f, 0.45f, -s.z * 0.15f), new Vector3(0.8f, 0.9f, 1.6f), Metal, true, "Toolbench");
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void SeedShop(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "SeedShop", f.position, f.rotationY);
            var s = f.size;
            var wall = Color.Lerp(new Color(0.82f, 0.95f, 0.78f), P.Walls[1], 0.25f);
            var roof = Color.Lerp(new Color(0.35f, 0.65f, 0.35f), P.Leaves[1], 0.4f);
            var windows = BasicBuilding(root, s, wall, roof, true, Wood, 1);
            Awning(root, s.x - 0.4f, s.y * 0.72f, s.z * 0.5f, new Color(0.4f, 0.75f, 0.4f), Color.white);
            for (int i = 0; i < 4; i++)
                Pot(root, new Vector3(-s.x * 0.5f + 0.6f + i * 0.7f, 0f, s.z * 0.5f + 1.7f), new Color(0.75f, 0.45f, 0.3f), P.Flowers[i % P.Flowers.Length]);
            for (int i = 0; i < 3; i++)
                Ball(root, new Vector3(s.x * 0.5f - 0.4f - i * 0.6f, 0.35f, s.z * 0.5f + 1.5f), new Vector3(0.55f, 0.7f, 0.5f), new Color(0.85f, 0.75f, 0.55f), "SeedSack");
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void GeneralStore(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "GeneralStore", f.position, f.rotationY);
            var s = f.size;
            var wall = Color.Lerp(new Color(1f, 0.93f, 0.8f), P.Walls[3], 0.3f);
            var roof = Color.Lerp(new Color(0.85f, 0.35f, 0.35f), P.Roofs[3], 0.3f);
            var windows = BasicBuilding(root, s, wall, roof, true, Wood, 1);
            Awning(root, s.x - 0.4f, s.y * 0.72f, s.z * 0.5f, roof, Color.white, 7);
            Barrel(root, new Vector3(-s.x * 0.5f + 0.3f, 0f, s.z * 0.5f + 1.6f), Wood);
            Crate(root, new Vector3(s.x * 0.5f - 0.6f, 0f, s.z * 0.5f + 1.7f), 0.75f, Color.Lerp(Wood, Color.white, 0.25f));
            Crate(root, new Vector3(s.x * 0.5f - 1.5f, 0f, s.z * 0.5f + 1.8f), 0.6f, Wood, false);
            Ball(root, new Vector3(s.x * 0.5f - 0.6f, 0.95f, s.z * 0.5f + 1.7f), Vector3.one * 0.3f, P.Flowers[3], "Fruit");
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void Carpenter(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Carpenter", f.position, f.rotationY);
            var s = f.size;
            var windows = BasicBuilding(root, s, Color.Lerp(Wood, Color.white, 0.25f), DarkWood, true, DarkWood, 1);
            // 丸太の山
            for (int i = 0; i < 3; i++)
                Cyl(root, new Vector3(s.x * 0.5f + 1.2f, 0.3f + (i == 2 ? 0.5f : 0f), -s.z * 0.2f + (i == 2 ? 0.25f : i * 0.55f)), new Vector3(0.5f, 1.4f, 0.5f), Wood, i == 0, "Log", new Vector3(90f, 0f, 0f));
            // 木馬（のこぎり台）と見本の椅子
            Box(root, new Vector3(-s.x * 0.5f - 1f, 0.5f, s.z * 0.3f), new Vector3(1.6f, 0.12f, 0.25f), Wood, true, "Sawhorse");
            Box(root, new Vector3(-s.x * 0.5f - 1.6f, 0.25f, s.z * 0.3f), new Vector3(0.1f, 0.5f, 0.5f), DarkWood, false, "Leg");
            Box(root, new Vector3(-s.x * 0.5f - 0.4f, 0.25f, s.z * 0.3f), new Vector3(0.1f, 0.5f, 0.5f), DarkWood, false, "Leg");
            var chair = new Vector3(s.x * 0.25f, 0f, s.z * 0.5f + 1.9f);
            Box(root, chair + new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.08f, 0.6f), P.Roofs[4], false, "Seat");
            Box(root, chair + new Vector3(0f, 0.8f, -0.27f), new Vector3(0.6f, 0.7f, 0.08f), P.Roofs[4], false, "Back");
            for (int i = 0; i < 4; i++) Box(root, chair + new Vector3((i % 2 - 0.5f) * 0.5f, 0.22f, (i / 2 - 0.5f) * 0.5f), new Vector3(0.07f, 0.45f, 0.07f), DarkWood, false, "ChairLeg");
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void FishingHut(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "FishingHut", f.position, f.rotationY);
            var s = f.size;
            var wall = Color.Lerp(new Color(0.55f, 0.68f, 0.8f), P.Walls[5], 0.2f);
            var windows = BasicBuilding(root, s, wall, new Color(0.3f, 0.45f, 0.6f), true, DarkWood, 1);
            // 魚の看板
            var fish = Box(root, new Vector3(0f, s.y * 0.92f, s.z * 0.5f + 0.15f), new Vector3(1.4f, 0.55f, 0.15f), new Color(1f, 0.6f, 0.35f), false, "FishSign", null, PrimitiveType.Sphere);
            Box(root, new Vector3(0.82f, s.y * 0.92f, s.z * 0.5f + 0.15f), new Vector3(0.4f, 0.5f, 0.1f), new Color(1f, 0.6f, 0.35f), false, "FishTail", new Vector3(0f, 0f, 45f));
            // 網と竿
            Box(root, new Vector3(-s.x * 0.5f - 0.05f, 1.6f, 0f), new Vector3(0.05f, 1.6f, 2.2f), new Color(0.85f, 0.85f, 0.75f, 0.6f), false, "Net");
            for (int i = 0; i < 2; i++)
                Cyl(root, new Vector3(s.x * 0.5f + 0.3f, 1.3f, s.z * 0.2f + i * 0.4f), new Vector3(0.05f, 1.3f, 0.05f), DarkWood, false, "Rod", new Vector3(12f, 0f, -10f));
            // 裏の小さな桟橋（水へ向かう）
            float dockLen = 6f;
            for (int i = 0; i < 6; i++)
            {
                float z = -s.z * 0.5f - 0.6f - i * (dockLen / 6f);
                Box(root, new Vector3(0f, 0.25f, z), new Vector3(1.8f, 0.12f, dockLen / 6f - 0.08f), Wood, true, "Plank");
            }
            Cyl(root, new Vector3(0.85f, -0.4f, -s.z * 0.5f - dockLen), new Vector3(0.22f, 0.8f, 0.22f), DarkWood, false, "DockPost");
            Cyl(root, new Vector3(-0.85f, -0.4f, -s.z * 0.5f - dockLen), new Vector3(0.22f, 0.8f, 0.22f), DarkWood, false, "DockPost");
            Barrel(root, new Vector3(s.x * 0.5f + 0.6f, 0f, -s.z * 0.3f), new Color(0.45f, 0.6f, 0.75f));
            NightLight.Attach(root.gameObject, null, WindowOn, Glass, windows.ToArray());
        }

        private static void Kitchen(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "CommunityKitchen", f.position, f.rotationY);
            var s = f.size;
            Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(s.x, 0.24f, s.z), P.Stone, true, "Floor");
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                Box(root, new Vector3(c.x * (s.x * 0.5f - 0.3f), s.y * 0.5f, c.y * (s.z * 0.5f - 0.3f)), new Vector3(0.35f, s.y, 0.35f), Wood, true, "Post");
            GabledRoof(root, s.y, s.x, s.z, Color.Lerp(new Color(0.95f, 0.6f, 0.3f), P.Roofs[1], 0.3f), 26f);
            // 奥の壁とかまど
            Box(root, new Vector3(0f, 1.2f, -s.z * 0.5f + 0.2f), new Vector3(s.x - 0.6f, 2.4f, 0.25f), Color.Lerp(P.Walls[1], Color.white, 0.3f), true, "BackWall");
            Box(root, new Vector3(0f, 0.55f, -s.z * 0.5f + 1.0f), new Vector3(s.x - 1.2f, 0.9f, 1.0f), new Color(0.8f, 0.78f, 0.74f), true, "Counter");
            for (int i = 0; i < 3; i++)
            {
                float x = -s.x * 0.3f + i * s.x * 0.3f;
                Glow(root, PrimitiveType.Cylinder, new Vector3(x, 1.02f, -s.z * 0.5f + 1.0f), new Vector3(0.5f, 0.02f, 0.5f), new Color(1f, 0.45f, 0.2f), "Stove");
                Cyl(root, new Vector3(x, 1.25f, -s.z * 0.5f + 1.0f), new Vector3(0.55f, 0.2f, 0.55f), i == 1 ? new Color(0.85f, 0.4f, 0.35f) : Metal, false, "Pot");
            }
            Box(root, new Vector3(s.x * 0.3f, s.y + 1.2f, -s.z * 0.35f), new Vector3(0.7f, 1.8f, 0.7f), P.RockDark, false, "Chimney");
            // 共用のテーブル
            Box(root, new Vector3(0f, 0.75f, s.z * 0.15f), new Vector3(2.6f, 0.12f, 1.2f), Wood, true, "Table");
            for (int i = 0; i < 2; i++) Box(root, new Vector3(0f, 0.45f, s.z * 0.15f + (i == 0 ? -1f : 1f)), new Vector3(2.4f, 0.1f, 0.4f), DarkWood, false, "TableBench");
            var lamp = Glow(root, PrimitiveType.Sphere, new Vector3(0f, s.y - 0.4f, 0f), Vector3.one * 0.35f, LampOn, "Lantern");
            NightLight.Attach(root.gameObject, AddPointLight(root, new Vector3(0f, s.y - 0.7f, 0f), LampOn, 8f, 1.1f), LampOn, new Color(0.7f, 0.65f, 0.5f), lamp.GetComponent<Renderer>());
        }

        private static void Lookout(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Lookout", f.position, f.rotationY);
            Cyl(root, new Vector3(0f, 0.08f, 0f), new Vector3(8f, 0.08f, 8f), Color.Lerp(P.Stone, Color.white, 0.15f), false, "Platform");
            for (int i = 0; i < 16; i++)
            {
                float a = i / 16f * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 90f)) < 20f) continue; // 正面（入口側）を開ける
                Cyl(root, new Vector3(Mathf.Cos(a) * 3.9f, 0.5f, Mathf.Sin(a) * 3.9f), new Vector3(0.14f, 0.5f, 0.14f), Wood, false, "RailPost");
                float a2 = (i + 0.5f) / 16f * Mathf.PI * 2f;
                Box(root, new Vector3(Mathf.Cos(a2) * 3.9f, 0.95f, Mathf.Sin(a2) * 3.9f), new Vector3(0.12f, 0.1f, 1.6f), Wood, false, "Rail", new Vector3(0f, -a2 * Mathf.Rad2Deg, 0f));
            }
            // 望遠鏡
            var scope = new Vector3(0f, 0f, -1.5f);
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                Cyl(root, scope + Quaternion.Euler(0f, a, 0f) * new Vector3(0.3f, 0.55f, 0f), new Vector3(0.06f, 0.6f, 0.06f), DarkWood, false, "Tripod", new Vector3(0f, a, 15f));
            }
            Cyl(root, scope + new Vector3(0f, 1.3f, 0f), new Vector3(0.22f, 0.6f, 0.22f), new Color(0.85f, 0.7f, 0.35f), false, "Telescope", new Vector3(-60f, 0f, 0f));
            // 旗
            Cyl(root, new Vector3(-2.8f, 1.8f, -2f), new Vector3(0.08f, 1.8f, 0.08f), Metal, false, "FlagPole");
            var flag = Box(root, new Vector3(-2.3f, 3.2f, -2f), new Vector3(1f, 0.6f, 0.04f), P.Flowers[4], false, "Flag");
            var gm = flag.AddComponent<GentleMotion>();
            gm.bobAmplitude = 0.05f;
            gm.bobSpeed = 3f;
        }

        private static void MineEntrance(FacilityInfo f, Transform parent, WorldPalette P)
        {
            // 中への移動ポータルは Mining モジュールが作る。ここは外観だけ。
            var root = Root(parent, "MineEntrance", f.position, f.rotationY);
            // 岩肌（入口の後ろ）
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 1.9f;
                Ball(root, new Vector3(x, 1.6f + (i % 2) * 0.8f, -1.6f - Mathf.Abs(i - 2) * 0.3f), new Vector3(3.2f, 4.2f, 3f), Color.Lerp(P.Rock, P.RockDark, (i % 3) / 2f), "RockFace");
            }
            Ball(root, new Vector3(0f, 4.3f, -1.8f), new Vector3(6f, 2.6f, 3.4f), P.RockDark, "RockTop");
            // 暗い入口
            Box(root, new Vector3(0f, 1.5f, -0.45f), new Vector3(2.8f, 3f, 0.3f), new Color(0.04f, 0.04f, 0.06f), false, "Opening");
            // 木枠
            Box(root, new Vector3(-1.6f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 0.4f), DarkWood, true, "FrameL");
            Box(root, new Vector3(1.6f, 1.6f, 0f), new Vector3(0.4f, 3.2f, 0.4f), DarkWood, true, "FrameR");
            Box(root, new Vector3(0f, 3.35f, 0f), new Vector3(3.8f, 0.45f, 0.5f), DarkWood, false, "Lintel");
            // レールとトロッコ
            for (int side = -1; side <= 1; side += 2)
                Box(root, new Vector3(side * 0.45f, 0.08f, 3f), new Vector3(0.08f, 0.08f, 6.5f), Metal, false, "Rail");
            for (int i = 0; i < 7; i++) Box(root, new Vector3(0f, 0.04f, 0.2f + i * 0.95f), new Vector3(1.3f, 0.06f, 0.25f), DarkWood, false, "Sleeper");
            var cart = new Vector3(0f, 0f, 4.4f);
            Box(root, cart + new Vector3(0f, 0.65f, 0f), new Vector3(1.2f, 0.7f, 1.5f), new Color(0.5f, 0.38f, 0.3f), true, "Cart");
            for (int i = 0; i < 4; i++)
                Cyl(root, cart + new Vector3((i % 2 == 0 ? -1 : 1) * 0.62f, 0.22f, (i / 2 == 0 ? -1 : 1) * 0.5f), new Vector3(0.36f, 0.05f, 0.36f), DarkMetal, false, "Wheel", new Vector3(0f, 0f, 90f));
            for (int i = 0; i < 3; i++) Ball(root, cart + new Vector3(-0.3f + i * 0.3f, 1.05f, 0f), Vector3.one * 0.35f, Color.Lerp(P.RockDark, P.CrystalColor, i * 0.3f), "Ore");
            // ランタン
            var lamp = Glow(root, PrimitiveType.Sphere, new Vector3(1.6f, 2.7f, 0.35f), Vector3.one * 0.32f, LampOn, "Lantern");
            NightLight.Attach(root.gameObject, AddPointLight(root, new Vector3(1.4f, 2.5f, 0.9f), LampOn, 8f, 1.2f), LampOn, new Color(0.6f, 0.55f, 0.45f), lamp.GetComponent<Renderer>());
        }

        private static void CrashSite(FacilityInfo f, Transform parent, WorldPalette P, ulong seed)
        {
            // ロケット本体は Rocket モジュールが作る。ここはクレーターの残骸だけ（ロケット仕様 §11.1 初期）。
            var root = Root(parent, "CrashDebris", f.position, 0f);
            var r = SeededRandom.For(seed, "world.crash.debris");
            for (int i = 0; i < 12; i++)
            {
                float a = r.Range(0f, 360f);
                float d = r.Range(7.5f, 12f);
                var lp = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, d);
                var ground = OnGround(root, lp);
                int kind = i % 4;
                if (kind == 0)
                    Box(root, ground + Vector3.up * 0.15f, new Vector3(r.Range(0.8f, 1.8f), 0.08f, r.Range(0.6f, 1.4f)), Color.Lerp(Metal, Color.white, r.Range(0f, 0.4f)), false, "Plate", new Vector3(r.Range(-25f, 25f), r.Range(0f, 360f), r.Range(-25f, 25f)));
                else if (kind == 1)
                {
                    Box(root, ground + Vector3.up * 0.3f, new Vector3(0.9f, 0.6f, 0.6f), new Color(0.85f, 0.85f, 0.9f), false, "Panel", new Vector3(0f, r.Range(0f, 360f), r.Range(-20f, 20f)));
                    var blink = Glow(root, PrimitiveType.Sphere, ground + Vector3.up * 0.7f, Vector3.one * 0.14f, new Color(1f, 0.35f, 0.3f), "Blink");
                    var gm = blink.AddComponent<GentleMotion>();
                    gm.bobAmplitude = 0.05f;
                    gm.bobSpeed = 6f;
                }
                else if (kind == 2)
                    Ball(root, ground + Vector3.up * 0.2f, new Vector3(r.Range(0.6f, 1.3f), r.Range(0.4f, 0.8f), r.Range(0.6f, 1.2f)), new Color(0.25f, 0.23f, 0.23f), "ScorchedRock");
                else
                    Cyl(root, ground + Vector3.up * 0.5f, new Vector3(0.1f, 0.8f, 0.1f), Metal, false, "BentPipe", new Vector3(r.Range(20f, 70f), r.Range(0f, 360f), 0f));
            }
            // 工具箱（最低限の工具：ロケット仕様 §11.1）
            var toPlaza = Quaternion.Euler(0f, f.rotationY, 0f) * new Vector3(2.8f, 0f, WorldTuning.CrashCraterRadius - 1.5f);
            var tb = OnGround(root, toPlaza);
            Box(root, tb + Vector3.up * 0.25f, new Vector3(1f, 0.5f, 0.55f), new Color(0.9f, 0.35f, 0.3f), true, "Toolbox");
            Box(root, tb + Vector3.up * 0.56f, new Vector3(0.6f, 0.08f, 0.1f), DarkMetal, false, "Handle");
            // 立ちのぼる煙
            for (int i = 0; i < 4; i++)
            {
                var sm = Ball(root, new Vector3(r.Range(-3f, 3f), 2f + i * 1.2f, r.Range(-3f, 3f)), Vector3.one * (0.8f + i * 0.3f), new Color(0.7f, 0.7f, 0.72f, 0.35f), "Smoke");
                var gm = sm.AddComponent<GentleMotion>();
                gm.bobAmplitude = 0.4f;
                gm.bobSpeed = 0.4f + i * 0.15f;
            }
        }

        private static Vector3 OnGround(Transform root, Vector3 local)
        {
            var w = root.TransformPoint(local);
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            float y = layout != null ? layout.HeightAt(w) : w.y;
            return root.InverseTransformPoint(new Vector3(w.x, y, w.z));
        }

        private static void Well(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Well", f.position, f.rotationY);
            Cyl(root, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.45f, 2.2f), Color.Lerp(P.Stone, Color.gray, 0.3f), true, "Ring");
            Cyl(root, new Vector3(0f, 0.86f, 0f), new Vector3(1.7f, 0.03f, 1.7f), new Color(0.3f, 0.55f, 0.75f), false, "Water");
            Box(root, new Vector3(-0.95f, 1.4f, 0f), new Vector3(0.15f, 1.9f, 0.15f), DarkWood, false, "Post");
            Box(root, new Vector3(0.95f, 1.4f, 0f), new Vector3(0.15f, 1.9f, 0.15f), DarkWood, false, "Post");
            Box(root, new Vector3(0f, 2.45f, 0f), new Vector3(2.5f, 0.12f, 1.6f), P.Roofs[5], false, "Roof", new Vector3(0f, 0f, 0f));
            Cyl(root, new Vector3(0f, 1.9f, 0f), new Vector3(0.12f, 0.95f, 0.12f), Wood, false, "Axle", new Vector3(0f, 0f, 90f));
            Cyl(root, new Vector3(0.3f, 1.3f, 0f), new Vector3(0.35f, 0.18f, 0.35f), Wood, false, "Bucket");
        }

        private static void Bench(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Bench", f.position, f.rotationY);
            Box(root, new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.1f, 0.5f), Wood, true, "Seat");
            Box(root, new Vector3(0f, 0.8f, -0.22f), new Vector3(1.8f, 0.45f, 0.08f), Wood, false, "Back", new Vector3(-10f, 0f, 0f));
            for (int i = 0; i < 2; i++) Box(root, new Vector3((i == 0 ? -1 : 1) * 0.75f, 0.22f, 0f), new Vector3(0.1f, 0.45f, 0.45f), DarkMetal, false, "Leg");
        }

        private static void Streetlight(FacilityInfo f, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "Streetlight", f.position, f.rotationY);
            Cyl(root, new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, 0.1f, 0.4f), DarkMetal, false, "Foot");
            Cyl(root, new Vector3(0f, 1.6f, 0f), new Vector3(0.14f, 1.6f, 0.14f), DarkMetal, true, "Pole");
            Box(root, new Vector3(0f, 3.25f, 0f), new Vector3(0.55f, 0.08f, 0.55f), DarkMetal, false, "Cap");
            var bulb = Glow(root, PrimitiveType.Sphere, new Vector3(0f, 3.0f, 0f), new Vector3(0.42f, 0.5f, 0.42f), LampOn, "Bulb");
            var light = AddPointLight(root, new Vector3(0f, 2.9f, 0f), LampOn, 11f, 1.3f);
            NightLight.Attach(root.gameObject, light, LampOn, new Color(0.85f, 0.85f, 0.78f), bulb.GetComponent<Renderer>());
        }

        // =================================================================
        // 広場の目印・橋・桟橋・柵
        // =================================================================

        public static void PlazaLandmark(Vector3 pos, Transform parent, WorldPalette P)
        {
            var root = Root(parent, "PlazaLandmark", pos, 0f);
            Cyl(root, new Vector3(0f, 0.15f, 0f), new Vector3(3.6f, 0.15f, 3.6f), Color.Lerp(P.Stone, Color.white, 0.2f), false, "Planter");
            Cyl(root, new Vector3(0f, 0f, 0f), new Vector3(1.1f, 3.2f, 1.1f), P.Trunk, true, "Trunk");
            Cyl(root, new Vector3(0.3f, 2.8f, 0f), new Vector3(0.6f, 1.8f, 0.6f), P.Trunk, false, "Branch", new Vector3(0f, 0f, -25f));
            Cyl(root, new Vector3(-0.3f, 2.8f, 0.2f), new Vector3(0.55f, 1.6f, 0.55f), P.Trunk, false, "Branch", new Vector3(20f, 0f, 22f));
            var canopy = Color.Lerp(P.Leaves[4], P.Flowers[5], 0.25f);
            Ball(root, new Vector3(0f, 6.6f, 0f), new Vector3(7f, 4.6f, 7f), canopy, "Canopy");
            Ball(root, new Vector3(2.2f, 5.6f, 1f), new Vector3(4f, 3f, 4f), Color.Lerp(canopy, Color.white, 0.12f), "Canopy2");
            Ball(root, new Vector3(-2f, 5.9f, -1.2f), new Vector3(4.2f, 3.2f, 4.2f), Color.Lerp(canopy, Color.black, 0.08f), "Canopy3");
            var fruits = new List<Renderer>();
            var r = SeededRandom.For((ulong)(pos.x * 1000f + pos.z * 7f + 12345f), "landmark");
            for (int i = 0; i < 14; i++)
            {
                var d = Quaternion.Euler(r.Range(-30f, 60f), r.Range(0f, 360f), 0f) * Vector3.forward;
                var fr = Ball(root, new Vector3(0f, 6.2f, 0f) + d * r.Range(3f, 3.7f), Vector3.one * 0.42f, new Color(1f, 0.9f, 0.6f), "Fruit");
                fruits.Add(fr.GetComponent<Renderer>());
            }
            NightLight.Attach(root.gameObject, AddPointLight(root, new Vector3(0f, 4.5f, 0f), new Color(1f, 0.85f, 0.6f), 14f, 0.9f), new Color(1f, 0.85f, 0.55f), new Color(1f, 0.92f, 0.7f), fruits.ToArray());
        }

        public static void Bridge(BridgeInfo br, Transform parent, WorldPalette P)
        {
            var a = new Vector3(br.A.x, br.DeckY, br.A.y);
            var b = new Vector3(br.B.x, br.DeckY, br.B.y);
            var mid = (a + b) * 0.5f;
            float len = Vector3.Distance(a, b) + 1.6f;
            float yaw = Mathf.Atan2(b.x - a.x, b.z - a.z) * Mathf.Rad2Deg;
            var root = Root(parent, "Bridge", mid, yaw);
            float w = br.HalfWidth * 2f;
            Box(root, new Vector3(0f, -0.15f, 0f), new Vector3(w, 0.3f, len), Wood, true, "Deck");
            int planks = Mathf.CeilToInt(len / 0.6f);
            for (int i = 0; i < planks; i += 2)
                Box(root, new Vector3(0f, 0.01f, -len * 0.5f + i * 0.6f + 0.3f), new Vector3(w - 0.1f, 0.02f, 0.5f), Color.Lerp(Wood, Color.white, 0.12f), false, "Plank");
            for (int side = -1; side <= 1; side += 2)
            {
                Box(root, new Vector3(side * (w * 0.5f - 0.08f), 0.85f, 0f), new Vector3(0.12f, 0.1f, len), DarkWood, true, "Rail");
                for (int i = 0; i <= 4; i++)
                    Box(root, new Vector3(side * (w * 0.5f - 0.08f), 0.45f, -len * 0.5f + i * len / 4f), new Vector3(0.16f, 0.9f, 0.16f), DarkWood, false, "RailPost");
            }
            // 橋脚
            Box(root, new Vector3(0f, -1.4f, 0f), new Vector3(w * 0.8f, 2.4f, 0.5f), P.Stone, false, "Pier");
        }

        public static void Pier(Vector3 start, Vector3 end, Transform parent, WorldPalette P, float extraLength = 0f)
        {
            float yaw = Mathf.Atan2(end.x - start.x, end.z - start.z) * Mathf.Rad2Deg;
            float len = Vector3.Distance(new Vector3(start.x, 0f, start.z), new Vector3(end.x, 0f, end.z)) + extraLength;
            float y = Mathf.Max(start.y, WorldTuning.SeaLevel + 0.6f) + 0.25f;
            var root = Root(parent, extraLength > 0f ? "PierExtension" : "Pier", new Vector3(start.x, y, start.z), yaw);
            Box(root, new Vector3(0f, -0.1f, len * 0.5f), new Vector3(2.4f, 0.2f, len), Wood, true, "Deck");
            for (int i = 0; i < len; i += 3)
                for (int side = -1; side <= 1; side += 2)
                    Cyl(root, new Vector3(side * 1.1f, -1.6f, i + 1f), new Vector3(0.25f, 1.6f, 0.25f), DarkWood, false, "Post");
            Cyl(root, new Vector3(0.9f, 0.3f, len - 0.4f), new Vector3(0.3f, 0.3f, 0.3f), DarkMetal, false, "Bollard");
        }

        /// <summary>農場の柵（農場の予約地の3辺。家の側は開ける）。コライダーなし（農場の拡張を妨げない）。</summary>
        public static void FarmFence(Rect reserve, Vector3 housePos, Transform parent, Func<float, float, float> ground)
        {
            var root = new GameObject("FarmFence").transform;
            root.SetParent(parent, false);
            var c = reserve.center;
            var toHouse = new Vector2(housePos.x - c.x, housePos.z - c.y);
            int skip; // 0:xMin 1:xMax 2:yMin 3:yMax
            if (Mathf.Abs(toHouse.x) > Mathf.Abs(toHouse.y)) skip = toHouse.x > 0 ? 1 : 0;
            else skip = toHouse.y > 0 ? 3 : 2;
            var corners = new[]
            {
                (new Vector2(reserve.xMin, reserve.yMin), new Vector2(reserve.xMin, reserve.yMax), 0),
                (new Vector2(reserve.xMax, reserve.yMin), new Vector2(reserve.xMax, reserve.yMax), 1),
                (new Vector2(reserve.xMin, reserve.yMin), new Vector2(reserve.xMax, reserve.yMin), 2),
                (new Vector2(reserve.xMin, reserve.yMax), new Vector2(reserve.xMax, reserve.yMax), 3)
            };
            foreach (var (a, b, side) in corners)
            {
                if (side == skip) continue;
                float len = (b - a).magnitude;
                int n = Mathf.CeilToInt(len / 3f);
                for (int i = 0; i <= n; i++)
                {
                    var p = Vector2.Lerp(a, b, i / (float)n);
                    var go = PrimitiveBuilder.Part(root, PrimitiveType.Cube, new Vector3(p.x, ground(p.x, p.y) + 0.45f, p.y), new Vector3(0.18f, 0.9f, 0.18f), Wood, "FencePost");
                    if (i == n) continue;
                    var q = Vector2.Lerp(a, b, (i + 0.5f) / n);
                    float yaw = Mathf.Atan2(b.x - a.x, b.y - a.y) * Mathf.Rad2Deg;
                    float gy = ground(q.x, q.y);
                    PrimitiveBuilder.Part(root, PrimitiveType.Cube, new Vector3(q.x, gy + 0.7f, q.y), new Vector3(0.08f, 0.1f, len / n), Color.Lerp(Wood, Color.white, 0.15f), "FenceRail", new Vector3(0f, yaw, 0f));
                    PrimitiveBuilder.Part(root, PrimitiveType.Cube, new Vector3(q.x, gy + 0.35f, q.y), new Vector3(0.08f, 0.1f, len / n), Color.Lerp(Wood, Color.white, 0.15f), "FenceRail", new Vector3(0f, yaw, 0f));
                }
            }
        }

        // =================================================================
        // 看板・道標
        // =================================================================

        public static SignBoard Sign(SignInfo s, Transform parent, Func<string, string> placeWord)
        {
            var root = Root(parent, "Sign_" + s.FacilityId, s.Position, s.Yaw);
            Cyl(root, new Vector3(0f, 0.8f, 0f), new Vector3(0.14f, 0.8f, 0.14f), DarkWood, false, "Post");
            var board = Box(root, new Vector3(0f, 1.75f, 0f), new Vector3(1.4f, 0.7f, 0.1f), new Color(0.93f, 0.86f, 0.7f), true, "Board");
            Box(root, new Vector3(0f, 1.75f, 0.06f), new Vector3(1.2f, 0.12f, 0.02f), DarkWood, false, "Stroke");
            Box(root, new Vector3(-0.2f, 1.6f, 0.06f), new Vector3(0.6f, 0.08f, 0.02f), DarkWood, false, "Stroke");
            var sb = board.AddComponent<SignBoard>();
            sb.signId = s.Id;
            sb.facilityId = s.FacilityId;
            string fid = s.FacilityId;
            WorldLabels.Add(board.transform, () => placeWord(fid), new Vector3(0f, 0.75f, 0f), UIStyles.AlienText, 24f, true);
            return sb;
        }

        public static SignBoard Signpost(SignInfo s, Transform parent, Func<string, string> placeWord)
        {
            var root = Root(parent, "Signpost", s.Position, 0f);
            var post = Cyl(root, new Vector3(0f, 1.4f, 0f), new Vector3(0.18f, 1.4f, 0.18f), DarkWood, true, "Post");
            for (int i = 0; i < s.Destinations.Count; i++)
            {
                float yaw = s.DestinationYaws[i];
                var arm = new GameObject("Arrow");
                arm.transform.SetParent(root, false);
                arm.transform.localPosition = new Vector3(0f, 2.4f - i * 0.5f, 0f);
                arm.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                Box(arm.transform, new Vector3(0f, 0f, 0.75f), new Vector3(0.08f, 0.36f, 1.3f), new Color(0.93f, 0.86f, 0.7f), false, "Plank");
                Box(arm.transform, new Vector3(0f, 0f, 1.45f), new Vector3(0.08f, 0.26f, 0.26f), new Color(0.93f, 0.86f, 0.7f), false, "Tip", new Vector3(45f, 0f, 0f));
                var tip = new GameObject("LabelAnchor");
                tip.transform.SetParent(arm.transform, false);
                tip.transform.localPosition = new Vector3(0f, 0.1f, 1.1f);
                string fid = s.Destinations[i];
                WorldLabels.Add(tip.transform, () => "→ " + placeWord(fid), Vector3.zero, UIStyles.AlienText, 18f, true);
            }
            var sb = post.AddComponent<SignBoard>();
            sb.signId = s.Id;
            sb.facilityId = s.Destinations.Count > 0 ? s.Destinations[0] : null;
            sb.isSignpost = true;
            return sb;
        }
    }
}
