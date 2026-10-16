using UnityEngine;

namespace Uchujin.World
{
    /// <summary>
    /// 木・岩・花・草・キノコ・倒木・葦・貝・結晶を、パレット結合メッシュで作る（村仕様 §7）。
    /// Blender で作ったモデルがある種類（木・茂み・岩・花・キノコ・結晶・葦）はモデルで描く（NatureModels）。
    /// 木と大きな岩・倒木にはコライダーを付ける。光るキノコ・結晶は発光マテリアルの別バッチ。
    /// </summary>
    public static class NatureBuilder
    {
        private const float ChunkSize = 100f;

        public static void Build(GeneratedWorld w, Transform parent, PaletteAtlas atlas, Material lit, Material glow)
        {
            var P = w.Palette;
            var batch = new MeshBatcher(atlas, ChunkSize);
            var glowBatch = new MeshBatcher(atlas, ChunkSize);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int modeledCount = 0;
            var colRoot = new GameObject("NatureColliders").transform;
            colRoot.SetParent(parent, false);

            foreach (var it in w.Nature)
            {
                // モデル（Resources/Art/Nature）がある種類はモデルで描き、従来のプリミティブは捨てる（当たり判定は下の各種類のまま）
                bool modeled = NatureModels.Add(it, P, batch, glowBatch);
                if (modeled) modeledCount++;
                var b = modeled ? MeshBatcher.Discard : batch;
                var gb = modeled ? MeshBatcher.Discard : glowBatch;
                var p = it.Position;
                var rot = Quaternion.Euler(0f, it.Yaw, 0f);
                float s = it.Scale;
                var leaf = P.Leaves[it.Variant % P.Leaves.Length];
                switch (it.Kind)
                {
                    case NatureKind.TreeRound:
                    {
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.45f, 2.2f, 0.45f) * s, P.Trunk);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 3.0f * s, rot, new Vector3(2.9f, 2.5f, 2.9f) * s, leaf);
                        b.Add(LowPoly.Shape.Sphere, p + rot * new Vector3(0.7f, 3.7f, 0.3f) * s, rot, new Vector3(1.8f, 1.6f, 1.8f) * s, Color.Lerp(leaf, Color.white, 0.12f));
                        AddTrunkCollider(colRoot, p, 0.35f * s, 4f * s);
                        break;
                    }
                    case NatureKind.TreeTall:
                    {
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.4f, 1.4f, 0.4f) * s, P.Trunk);
                        var dark = Color.Lerp(leaf, Color.black, 0.18f);
                        b.Add(LowPoly.Shape.Cone, p + Vector3.up * 1.0f * s, rot, new Vector3(2.8f, 2.4f, 2.8f) * s, dark);
                        b.Add(LowPoly.Shape.Cone, p + Vector3.up * 2.4f * s, rot, new Vector3(2.2f, 2.1f, 2.2f) * s, leaf);
                        b.Add(LowPoly.Shape.Cone, p + Vector3.up * 3.6f * s, rot, new Vector3(1.5f, 1.9f, 1.5f) * s, Color.Lerp(leaf, Color.white, 0.1f));
                        AddTrunkCollider(colRoot, p, 0.35f * s, 4.5f * s);
                        break;
                    }
                    case NatureKind.TreeMushroom:
                    {
                        var cap = P.Mushrooms[it.Variant % P.Mushrooms.Length];
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.7f, 2.6f, 0.7f) * s, new Color(0.95f, 0.92f, 0.84f));
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 2.7f * s, rot, new Vector3(3.2f, 1.5f, 3.2f) * s, cap);
                        for (int i = 0; i < 4; i++)
                        {
                            var o = Quaternion.Euler(0f, it.Yaw + i * 90f + 20f, 0f) * new Vector3(0.9f, 3.15f, 0f) * s;
                            b.Add(LowPoly.Shape.Sphere, p + o, rot, Vector3.one * 0.38f * s, Color.white);
                        }
                        AddTrunkCollider(colRoot, p, 0.4f * s, 3f * s);
                        break;
                    }
                    case NatureKind.TreeLollipop:
                    {
                        b.Add(LowPoly.Shape.Cylinder, p, Quaternion.Euler(4f, it.Yaw, 0f), new Vector3(0.3f, 3.6f, 0.3f) * s, Color.Lerp(P.Trunk, P.Sand, 0.3f));
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 3.9f * s, rot, new Vector3(2.1f, 1.9f, 2.1f) * s, P.Leaves[(it.Variant + 2) % P.Leaves.Length]);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 3.9f * s, rot, new Vector3(2.5f, 0.5f, 2.5f) * s, Color.Lerp(leaf, P.Flowers[it.Variant % P.Flowers.Length], 0.35f));
                        AddTrunkCollider(colRoot, p, 0.25f * s, 4f * s);
                        break;
                    }
                    case NatureKind.TreeCrystal:
                    {
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.35f, 1.6f, 0.35f) * s, P.RockDark);
                        gb.Add(LowPoly.Shape.Cone, p + Vector3.up * 1.2f * s, rot, new Vector3(1.6f, 3.2f, 1.6f) * s, Color.Lerp(P.CrystalColor, Color.white, 0.2f));
                        gb.Add(LowPoly.Shape.Cone, p + rot * new Vector3(0.6f, 0.6f, 0f) * s, Quaternion.Euler(0f, it.Yaw, -25f), new Vector3(0.7f, 1.6f, 0.7f) * s, P.CrystalColor);
                        AddTrunkCollider(colRoot, p, 0.5f * s, 4f * s);
                        break;
                    }
                    case NatureKind.Bush:
                    {
                        var c = Color.Lerp(leaf, Color.black, 0.1f);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.45f * s, rot, new Vector3(1.6f, 1.1f, 1.4f) * s, c);
                        b.Add(LowPoly.Shape.Sphere, p + rot * new Vector3(0.6f, 0.4f, 0.2f) * s, rot, new Vector3(1.1f, 0.9f, 1.1f) * s, leaf);
                        if (it.Variant % 2 == 0)
                            for (int i = 0; i < 3; i++)
                                b.Add(LowPoly.Shape.Sphere, p + rot * new Vector3(-0.4f + i * 0.4f, 0.95f, 0.5f) * s, rot, Vector3.one * 0.2f * s, P.Flowers[(it.Variant + i) % P.Flowers.Length]);
                        break;
                    }
                    case NatureKind.Rock:
                    {
                        var c = Color.Lerp(P.Rock, P.RockDark, (it.Variant % 3) / 3f);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.15f * s, Quaternion.Euler(it.Yaw * 0.3f, it.Yaw, 10f), new Vector3(1.1f, 0.7f, 0.9f) * s, c);
                        if (s > 1.15f) AddSphereCollider(colRoot, p + Vector3.up * 0.2f * s, 0.5f * s);
                        break;
                    }
                    case NatureKind.Boulder:
                    {
                        var c = Color.Lerp(P.Rock, P.RockDark, (it.Variant % 3) / 2.5f);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.5f * s, Quaternion.Euler(it.Yaw * 0.2f, it.Yaw, 15f), new Vector3(2.6f, 1.9f, 2.2f) * s, c);
                        b.Add(LowPoly.Shape.Sphere, p + rot * new Vector3(1.0f, 0.3f, 0.6f) * s, rot, new Vector3(1.2f, 0.9f, 1.1f) * s, Color.Lerp(c, Color.white, 0.08f));
                        AddSphereCollider(colRoot, p + Vector3.up * 0.6f * s, 1.15f * s);
                        break;
                    }
                    case NatureKind.Flower:
                    {
                        var fc = P.Flowers[it.Variant % P.Flowers.Length];
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.05f, 0.45f, 0.05f) * s, P.GrassDark);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.5f * s, rot, new Vector3(0.32f, 0.16f, 0.32f) * s, fc);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.56f * s, rot, Vector3.one * 0.11f * s, new Color(1f, 0.92f, 0.45f));
                        break;
                    }
                    case NatureKind.Grass:
                    {
                        var gc = Color.Lerp(P.GrassDark, P.Grass, (it.Variant % 3) / 3f);
                        for (int i = 0; i < 3; i++)
                        {
                            var o = Quaternion.Euler(0f, it.Yaw + i * 120f, 0f) * new Vector3(0.12f, 0f, 0f);
                            b.Add(LowPoly.Shape.Cone, p + o * s, Quaternion.Euler(8f * (i - 1), it.Yaw + i * 40f, 6f * (i - 1)), new Vector3(0.14f, 0.55f + i * 0.08f, 0.14f) * s, gc);
                        }
                        break;
                    }
                    case NatureKind.Mushroom:
                    {
                        var cap = P.Mushrooms[it.Variant % P.Mushrooms.Length];
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.12f, 0.3f, 0.12f) * s, new Color(0.96f, 0.94f, 0.88f));
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.32f * s, rot, new Vector3(0.4f, 0.22f, 0.4f) * s, cap);
                        break;
                    }
                    case NatureKind.GlowMushroom:
                    {
                        b.Add(LowPoly.Shape.Cylinder, p, rot, new Vector3(0.1f, 0.34f, 0.1f) * s, new Color(0.9f, 0.95f, 1f));
                        gb.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.36f * s, rot, new Vector3(0.36f, 0.2f, 0.36f) * s, Color.Lerp(P.CrystalColor, new Color(0.5f, 1f, 0.8f), 0.5f));
                        gb.Add(LowPoly.Shape.Sphere, p + rot * new Vector3(0.25f, 0.2f, 0.1f) * s, rot, new Vector3(0.22f, 0.13f, 0.22f) * s, new Color(0.55f, 1f, 0.85f));
                        break;
                    }
                    case NatureKind.FallenLog:
                    {
                        var lr = Quaternion.Euler(0f, it.Yaw, 90f);
                        b.Add(LowPoly.Shape.Cylinder, p + Vector3.up * 0.35f + rot * new Vector3(-1.8f, 0f, 0f) * s, Quaternion.Euler(0f, it.Yaw, -90f), new Vector3(0.7f, 3.6f, 0.7f) * s, Color.Lerp(P.Trunk, Color.gray, 0.25f));
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.7f + rot * new Vector3(0.6f, 0f, 0f) * s, rot, new Vector3(0.9f, 0.3f, 0.6f) * s, P.Leaves[0]);
                        var go = new GameObject("LogCol");
                        go.transform.SetParent(colRoot, false);
                        go.transform.position = p + Vector3.up * 0.35f;
                        go.transform.rotation = Quaternion.Euler(0f, it.Yaw, 0f);
                        var cc = go.AddComponent<CapsuleCollider>();
                        cc.direction = 0;
                        cc.radius = 0.35f * s;
                        cc.height = 3.6f * s;
                        break;
                    }
                    case NatureKind.Reed:
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            var o = Quaternion.Euler(0f, it.Yaw + i * 90f, 0f) * new Vector3(0.15f, 0f, 0f) * s;
                            float hh = (1.0f + 0.15f * i) * s;
                            b.Add(LowPoly.Shape.Cylinder, p + o, Quaternion.Euler(4f * (i - 1.5f), 0f, 3f), new Vector3(0.05f, hh, 0.05f), Color.Lerp(P.GrassDark, P.Sand, 0.2f));
                            if (i % 2 == 0) b.Add(LowPoly.Shape.Cylinder, p + o + Vector3.up * hh * 0.8f, Quaternion.identity, new Vector3(0.1f, 0.25f, 0.1f) * s, new Color(0.5f, 0.32f, 0.2f));
                        }
                        break;
                    }
                    case NatureKind.Shell:
                    {
                        var sc = Color.Lerp(new Color(1f, 0.85f, 0.85f), P.Flowers[it.Variant % P.Flowers.Length], 0.3f);
                        b.Add(LowPoly.Shape.Sphere, p + Vector3.up * 0.05f, rot, new Vector3(0.3f, 0.12f, 0.24f) * s, sc);
                        break;
                    }
                    case NatureKind.Crystal:
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            var o = Quaternion.Euler(0f, it.Yaw + i * 120f, 0f) * new Vector3(0.25f, 0f, 0f) * s;
                            gb.Add(LowPoly.Shape.Cone, p + o, Quaternion.Euler(12f * (i - 1), it.Yaw, 15f * (1 - i)), new Vector3(0.35f, 0.9f + i * 0.25f, 0.35f) * s, Color.Lerp(P.CrystalColor, Color.white, i * 0.15f));
                        }
                        break;
                    }
                }
            }
            int tris = batch.TriangleCount + glowBatch.TriangleCount;
            batch.Build(parent, lit, "Nature");
            glowBatch.Build(parent, glow, "NatureGlow");
            Debug.Log($"[World] 自然物 {w.Nature.Count}個（モデル {modeledCount}個）・三角形 {tris}・作成 {watch.ElapsedMilliseconds} ms");
        }

        private static void AddTrunkCollider(Transform root, Vector3 p, float radius, float height)
        {
            var go = new GameObject("TreeCol");
            go.transform.SetParent(root, false);
            go.transform.position = p;
            var cc = go.AddComponent<CapsuleCollider>();
            cc.radius = radius;
            cc.height = height;
            cc.center = new Vector3(0f, height * 0.5f, 0f);
        }

        private static void AddSphereCollider(Transform root, Vector3 p, float radius)
        {
            var go = new GameObject("RockCol");
            go.transform.SetParent(root, false);
            go.transform.position = p;
            go.AddComponent<SphereCollider>().radius = radius;
        }
    }
}
