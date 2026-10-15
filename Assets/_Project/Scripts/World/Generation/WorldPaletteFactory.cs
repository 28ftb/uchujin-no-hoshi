using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 星ごとの配色（村仕様 §1.1「植生や配色」、§18「星ごとの植物色・地表の質感の変化」【追加提案】）。
    /// かわいく分かりやすい範囲で、草・葉・花の色相をシードで変える。
    /// </summary>
    public static class WorldPaletteFactory
    {
        private struct Base
        {
            public string ja;
            public float grassHue, grassSat, grassVal;
            public float leafHueA, leafHueB;
            public float skyHue;
        }

        // 【仮数値】草の色相の候補（緑・青緑・若草・ミント・ラベンダー寄り）
        private static readonly Base[] bases =
        {
            new Base { ja = "緑の星", grassHue = 0.29f, grassSat = 0.52f, grassVal = 0.72f, leafHueA = 0.27f, leafHueB = 0.36f, skyHue = 0.56f },
            new Base { ja = "青緑の星", grassHue = 0.40f, grassSat = 0.48f, grassVal = 0.70f, leafHueA = 0.43f, leafHueB = 0.50f, skyHue = 0.52f },
            new Base { ja = "若草の星", grassHue = 0.21f, grassSat = 0.55f, grassVal = 0.78f, leafHueA = 0.18f, leafHueB = 0.28f, skyHue = 0.58f },
            new Base { ja = "ミントの星", grassHue = 0.35f, grassSat = 0.38f, grassVal = 0.80f, leafHueA = 0.33f, leafHueB = 0.47f, skyHue = 0.54f },
            new Base { ja = "すみれの星", grassHue = 0.27f, grassSat = 0.42f, grassVal = 0.70f, leafHueA = 0.75f, leafHueB = 0.85f, skyHue = 0.66f },
            new Base { ja = "夕焼け葉の星", grassHue = 0.25f, grassSat = 0.50f, grassVal = 0.72f, leafHueA = 0.05f, leafHueB = 0.12f, skyHue = 0.60f },
        };

        public static WorldPalette Create(SeededRandom r)
        {
            var b = bases[r.Range(0, bases.Length)];
            float hueJitter = r.Range(-0.025f, 0.025f);
            var p = new WorldPalette();
            p.NameJa = b.ja;
            float gh = Mathf.Repeat(b.grassHue + hueJitter, 1f);
            p.Grass = Color.HSVToRGB(gh, b.grassSat, b.grassVal);
            p.GrassDark = Color.HSVToRGB(gh + 0.01f, b.grassSat + 0.08f, b.grassVal - 0.14f);
            p.GrassLight = Color.HSVToRGB(gh - 0.02f, b.grassSat - 0.1f, Mathf.Min(1f, b.grassVal + 0.1f));
            p.ForestFloor = Color.HSVToRGB(gh + 0.03f, b.grassSat + 0.1f, b.grassVal - 0.26f);
            p.Meadow = Color.HSVToRGB(gh - 0.04f, b.grassSat - 0.05f, Mathf.Min(1f, b.grassVal + 0.06f));
            p.Sand = Color.HSVToRGB(r.Range(0.09f, 0.13f), r.Range(0.22f, 0.34f), 0.92f);
            p.Rock = Color.HSVToRGB(r.Range(0.05f, 0.75f), r.Range(0.06f, 0.16f), 0.62f);
            p.RockDark = p.Rock * 0.72f;
            p.RockDark.a = 1f;
            p.Dirt = Color.HSVToRGB(0.08f, 0.38f, 0.66f);
            p.Stone = Color.HSVToRGB(r.Range(0.05f, 0.12f), 0.14f, 0.80f);
            p.Plaza = Color.HSVToRGB(r.Range(0.03f, 0.1f), 0.22f, 0.86f);
            p.Soil = Color.HSVToRGB(0.07f, 0.48f, 0.48f);
            p.Mud = Color.HSVToRGB(0.08f, 0.35f, 0.45f);
            p.River = new Color(0.35f, 0.72f, 0.92f, 0.72f);
            p.Pond = new Color(0.30f, 0.66f, 0.80f, 0.74f);
            p.Sea = new Color(0.22f, 0.55f, 0.85f, 0.80f);
            p.DeepSea = new Color(0.12f, 0.32f, 0.6f, 1f);
            p.Trunk = Color.HSVToRGB(r.Range(0.05f, 0.09f), 0.45f, 0.48f);
            p.Leaves = new Color[5];
            for (int i = 0; i < p.Leaves.Length; i++)
            {
                float h = Mathf.Lerp(b.leafHueA, b.leafHueB, i / 4f) + r.Range(-0.01f, 0.01f);
                p.Leaves[i] = Color.HSVToRGB(Mathf.Repeat(h, 1f), r.Range(0.45f, 0.65f), r.Range(0.6f, 0.82f));
            }
            p.Flowers = new Color[6];
            float fh = r.Range(0f, 1f);
            for (int i = 0; i < p.Flowers.Length; i++)
                p.Flowers[i] = Color.HSVToRGB(Mathf.Repeat(fh + i * 0.17f, 1f), r.Range(0.35f, 0.6f), 0.98f);
            p.Mushrooms = new[]
            {
                Color.HSVToRGB(r.Range(0f, 1f), 0.55f, 0.95f),
                Color.HSVToRGB(r.Range(0f, 1f), 0.45f, 0.95f),
                new Color(0.98f, 0.95f, 0.88f)
            };
            p.CrystalColor = Color.HSVToRGB(r.Range(0.45f, 0.85f), 0.45f, 1f);
            p.Roofs = new Color[6];
            p.Walls = new Color[6];
            float rh = r.Range(0f, 1f);
            for (int i = 0; i < 6; i++)
            {
                p.Roofs[i] = Color.HSVToRGB(Mathf.Repeat(rh + i * 0.13f, 1f), 0.5f, 0.78f);
                p.Walls[i] = Color.HSVToRGB(Mathf.Repeat(rh + 0.5f + i * 0.11f, 1f), 0.16f, 0.96f);
            }
            p.SkyTint = Color.HSVToRGB(Mathf.Repeat(b.skyHue + r.Range(-0.03f, 0.03f), 1f), 0.45f, 1f);
            return p;
        }
    }
}
