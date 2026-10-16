using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 滝の裏の洞（生成バージョン2。マップ設計 v0.2 §2.10）。
    /// 地上の入口（滝つぼの脇の崖）と、離れた座標に作る小さな洞の中（暗い淵・水のカーテン・光るキノコ）。
    /// 洞の淵の釣り場は生成時に Layout.FishingSpots に入っている（"spot.waterfall_cave"、タグ behind_waterfall / dark）。
    /// 天井は作らない（斜め見下ろしのカメラで中が見えるように。鉱山と同じ考え方）。
    /// </summary>
    public static class WaterfallCaveBuilder
    {
        public const string AreaId = "cave:waterfall";
        public static readonly Color Ambient = new Color(0.12f, 0.16f, 0.24f);

        /// <summary>洞の中の出現位置（入口の水のカーテンの内側）。</summary>
        public static Vector3 InsideSpawn => WorldTuning.WaterfallCaveOrigin + new Vector3(0f, 0.2f, -4.6f);

        public static void Build(Transform parent, GeneratedWorld g)
        {
            if (g == null || g.Cave == null) return;
            var o = WorldTuning.WaterfallCaveOrigin;
            var root = new GameObject("WaterfallCave").transform;
            root.SetParent(parent, false);

            var rock = new Color(0.34f, 0.36f, 0.42f);
            var rockDark = new Color(0.24f, 0.26f, 0.32f);
            // 床（上面が y=0）
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(0f, -0.5f, 0f), new Vector3(15f, 1f, 15f), rockDark, "Floor");
            // 壁（入口側は水のカーテン）
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(0f, 2.5f, 7.5f), new Vector3(15f, 5f, 1f), rock, "WallBack");
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(-7.5f, 2.5f, 0f), new Vector3(1f, 5f, 15f), rock, "WallL");
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(7.5f, 2.5f, 0f), new Vector3(1f, 5f, 15f), rock, "WallR");
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(-5f, 2.5f, -7.5f), new Vector3(5f, 5f, 1f), rock, "WallFrontL");
            PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(5f, 2.5f, -7.5f), new Vector3(5f, 5f, 1f), rock, "WallFrontR");
            // 水のカーテン（滝の裏側から見た滝）
            var curtain = PrimitiveBuilder.Create(PrimitiveType.Cube, root, o + new Vector3(0f, 2.4f, -7.6f), new Vector3(5.2f, 4.8f, 0.3f), new Color(0.7f, 0.88f, 1f, 0.55f), "WaterCurtain");
            curtain.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(new Color(0.7f, 0.88f, 1f, 0.55f), 0.9f, true);
            // 暗い淵（縁の石は膝の高さで、歩いて淵に落ちないように）
            var pool = o + new Vector3(0f, 0f, 2.4f);
            var water = PrimitiveBuilder.Create(PrimitiveType.Cylinder, root, pool + new Vector3(0f, 0.03f, 0f), new Vector3(6.4f, 0.02f, 6.4f), new Color(0.08f, 0.16f, 0.3f), "DarkPool", false);
            water.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(new Color(0.08f, 0.16f, 0.3f), 0.95f);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                var p = pool + new Vector3(Mathf.Cos(a) * 3.5f, 0.3f, Mathf.Sin(a) * 3.5f);
                PrimitiveBuilder.Create(PrimitiveType.Cube, root, p, new Vector3(1.3f, 0.65f, 0.9f), i % 2 == 0 ? rock : rockDark, "Rim" + i)
                    .transform.rotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
            }
            // 光るキノコと、ほのかな灯り（暗いが真っ暗ではない）
            for (int i = 0; i < 7; i++)
            {
                float a = i / 7f * Mathf.PI * 2f + 0.3f;
                var p = o + new Vector3(Mathf.Cos(a) * 6.2f, 0.25f, Mathf.Sin(a) * 6.2f);
                var m = PrimitiveBuilder.Create(PrimitiveType.Sphere, root, p, new Vector3(0.5f, 0.35f, 0.5f), new Color(0.45f, 0.95f, 1f), "GlowMushroom" + i, false);
                m.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetEmissive(new Color(0.45f, 0.95f, 1f));
            }
            var lightGo = new GameObject("CaveLight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.position = o + new Vector3(0f, 4f, 0f);
            var lt = lightGo.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.range = 14f;
            lt.intensity = 0.9f;
            lt.color = new Color(0.55f, 0.75f, 1f);

            // 出入口（[E]で移動）
            var cave = g.Cave;
            var fwd = Quaternion.Euler(0f, cave.Yaw, 0f) * Vector3.forward;
            var outside = cave.Entrance + fwd * 1.6f + Vector3.up * 0.2f;
            AreaPortal.Create(root, o + new Vector3(0f, 1.1f, -6.6f), new Vector3(2.2f, 2.2f, 0.6f), AreaIds.Overworld, outside, cave.Yaw,
                "外へ出る", "Go outside", new Color(0.6f, 0.82f, 1f, 0.5f));
            var mouth = AreaPortal.Create(parent, cave.Entrance + Vector3.up * 1.1f, new Vector3(1.6f, 2.2f, 1.6f), AreaId, InsideSpawn, 0f,
                "滝の裏へ入る", "Go behind the waterfall", new Color(0.06f, 0.08f, 0.12f));
            mouth.name = "WaterfallCaveMouth";
            mouth.transform.rotation = Quaternion.Euler(0f, cave.Yaw, 0f);
        }

        public static void RegisterArea()
        {
            Game.Instance.Areas.Register(new AreaDescriptor
            {
                Id = AreaId,
                NameJa = "滝の裏",
                NameEn = "Behind the waterfall",
                IsIndoor = false,
                IsUnderground = true,
                ShowWeather = false,
                AmbientOverride = Ambient,
                DefaultSpawn = InsideSpawn,
                DefaultYaw = 0f
            });
        }
    }
}
