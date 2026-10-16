using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// コードで作る低ポリゴンの基本形（木・岩・花などを軽くするため）。
    /// </summary>
    public static class LowPoly
    {
        public enum Shape { Cube, Sphere, Cylinder, Cone }

        private static Mesh cube, sphere, cylinder, cone;

        public static Mesh Get(Shape s)
        {
            switch (s)
            {
                case Shape.Cube: return cube != null ? cube : (cube = MakeCube());
                case Shape.Sphere: return sphere != null ? sphere : (sphere = MakeSphere(10, 6));
                case Shape.Cylinder: return cylinder != null ? cylinder : (cylinder = MakeCylinder(10, 1f));
                default: return cone != null ? cone : (cone = MakeCylinder(8, 0f));
            }
        }

        /// <summary>中心原点・大きさ1の立方体。</summary>
        private static Mesh MakeCube()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            Vector3[] dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };
            foreach (var d in dirs)
            {
                var u = Mathf.Abs(d.y) > 0.5f ? Vector3.right : Vector3.up;
                var w = Vector3.Cross(d, u);
                int b = v.Count;
                v.Add((d - u - w) * 0.5f);
                v.Add((d + u - w) * 0.5f);
                v.Add((d + u + w) * 0.5f);
                v.Add((d - u + w) * 0.5f);
                for (int i = 0; i < 4; i++) n.Add(d);
                // 外向きの面になるよう巻き順を法線で決める
                var nn = Vector3.Cross(v[b + 1] - v[b], v[b + 2] - v[b]);
                if (Vector3.Dot(nn, d) > 0f)
                {
                    t.Add(b); t.Add(b + 2); t.Add(b + 1);
                    t.Add(b); t.Add(b + 3); t.Add(b + 2);
                }
                else
                {
                    t.Add(b); t.Add(b + 1); t.Add(b + 2);
                    t.Add(b); t.Add(b + 2); t.Add(b + 3);
                }
            }
            return Build("lp_cube", v, n, t);
        }

        /// <summary>中心原点・直径1の球。</summary>
        private static Mesh MakeSphere(int seg, int rings)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float th = Mathf.PI * 2f * s / seg;
                    var p = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    v.Add(p * 0.5f);
                    n.Add(p);
                }
            }
            int row = seg + 1;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * row + s, b = a + row;
                    t.Add(a); t.Add(a + 1); t.Add(b);
                    t.Add(a + 1); t.Add(b + 1); t.Add(b);
                }
            return Build("lp_sphere", v, n, t);
        }

        /// <summary>底面中心が原点、高さ1、底の直径1。topScale=0 なら円錐。</summary>
        private static Mesh MakeCylinder(int seg, float topScale)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float slope = (1f - topScale) * 0.5f;
            for (int s = 0; s <= seg; s++)
            {
                float th = Mathf.PI * 2f * s / seg;
                var dir = new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th));
                var nrm = new Vector3(dir.x, slope, dir.z).normalized;
                v.Add(dir * 0.5f);
                n.Add(nrm);
                v.Add(dir * 0.5f * topScale + Vector3.up);
                n.Add(nrm);
            }
            for (int s = 0; s < seg; s++)
            {
                int a = s * 2;
                t.Add(a); t.Add(a + 1); t.Add(a + 2);
                t.Add(a + 1); t.Add(a + 3); t.Add(a + 2);
            }
            // 底
            int cb = v.Count;
            v.Add(Vector3.zero);
            n.Add(Vector3.down);
            for (int s = 0; s <= seg; s++)
            {
                float th = Mathf.PI * 2f * s / seg;
                v.Add(new Vector3(Mathf.Cos(th), 0f, Mathf.Sin(th)) * 0.5f);
                n.Add(Vector3.down);
            }
            for (int s = 0; s < seg; s++)
            {
                t.Add(cb); t.Add(cb + 1 + s); t.Add(cb + 2 + s);
            }
            if (topScale > 0.01f)
            {
                int ct = v.Count;
                v.Add(Vector3.up);
                n.Add(Vector3.up);
                for (int s = 0; s <= seg; s++)
                {
                    float th = Mathf.PI * 2f * s / seg;
                    v.Add(new Vector3(Mathf.Cos(th) * 0.5f * topScale, 1f, Mathf.Sin(th) * 0.5f * topScale));
                    n.Add(Vector3.up);
                }
                for (int s = 0; s < seg; s++)
                {
                    t.Add(ct); t.Add(ct + 2 + s); t.Add(ct + 1 + s);
                }
            }
            FixWinding(v, n, t);
            return Build(topScale > 0.01f ? "lp_cylinder" : "lp_cone", v, n, t);
        }

        /// <summary>三角形の向きを法線に合わせる（裏返りを防ぐ）。</summary>
        private static void FixWinding(List<Vector3> v, List<Vector3> n, List<int> t)
        {
            for (int i = 0; i < t.Count; i += 3)
            {
                var a = v[t[i]];
                var b = v[t[i + 1]];
                var c = v[t[i + 2]];
                var fn = Vector3.Cross(b - a, c - a);
                var avg = n[t[i]] + n[t[i + 1]] + n[t[i + 2]];
                if (Vector3.Dot(fn, avg) < 0f)
                {
                    int tmp = t[i + 1];
                    t[i + 1] = t[i + 2];
                    t[i + 2] = tmp;
                }
            }
        }

        private static Mesh Build(string name, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            // 球は内側を向いた面が無いよう巻き順を修正
            FixWinding(v, n, t);
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// 色のパレット・テクスチャ。結合メッシュの各頂点の UV をパレットの色に向けることで、
    /// 1つのマテリアルで多色を描ける（URP Lit / Built-in Standard の両方で動く）。
    /// </summary>
    public sealed class PaletteAtlas
    {
        public const int Size = 32;
        public readonly Texture2D Texture;
        private readonly Dictionary<int, int> index = new Dictionary<int, int>();
        private readonly Color32[] pixels = new Color32[Size * Size];
        private bool dirty;

        public PaletteAtlas()
        {
            Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "WorldPalette"
            };
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 0, 255, 255);
        }

        public Vector2 UV(Color c)
        {
            Color32 c32 = c;
            c32.a = 255;
            int key = (c32.r << 16) | (c32.g << 8) | c32.b;
            if (!index.TryGetValue(key, out int i))
            {
                i = index.Count;
                if (i >= Size * Size) i = Size * Size - 1;
                else
                {
                    index[key] = i;
                    pixels[i] = c32;
                    dirty = true;
                }
            }
            return new Vector2((i % Size + 0.5f) / Size, (i / Size + 0.5f) / Size);
        }

        public void Apply()
        {
            if (!dirty) return;
            Texture.SetPixels32(pixels);
            Texture.Apply(false, false);
            dirty = false;
        }
    }

    /// <summary>
    /// 多数の小物（木・岩・花など）をチャンクごとに1メッシュへ結合する。
    /// 描画負荷を抑えるための仕組み。コライダーは別に付ける。
    /// </summary>
    public sealed class MeshBatcher
    {
        private sealed class Bucket
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> UV = new List<Vector2>();
            public readonly List<int> T = new List<int>();
        }

        private readonly Dictionary<int, Bucket> buckets = new Dictionary<int, Bucket>();
        private readonly PaletteAtlas atlas;
        private readonly float chunkSize;
        private readonly bool discard;

        /// <summary>これまでに加えた三角形の数（確認用のログに使う）。</summary>
        public int TriangleCount { get; private set; }

        /// <summary>何も加えない結合器（モデルで描く自然物の、従来のプリミティブを捨てる時に使う）。</summary>
        public static readonly MeshBatcher Discard = new MeshBatcher(null, 1f, true);

        public MeshBatcher(PaletteAtlas atlas, float chunkSize) : this(atlas, chunkSize, false) { }

        private MeshBatcher(PaletteAtlas atlas, float chunkSize, bool discard)
        {
            this.atlas = atlas;
            this.chunkSize = chunkSize;
            this.discard = discard;
        }

        private Bucket BucketAt(Vector3 pos)
        {
            int key = Key(pos);
            if (!buckets.TryGetValue(key, out var b))
            {
                b = new Bucket();
                buckets[key] = b;
            }
            return b;
        }

        /// <summary>
        /// 任意のメッシュの部品（頂点・法線・三角形）を1色で加える。m は部品の頂点からワールドへの変換。
        /// チャンクは pos（置く位置）で決める。モデルで描く自然物（NatureModels）で使う。
        /// </summary>
        public void AddMesh(Vector3[] verts, Vector3[] norms, int[] tris, Matrix4x4 m, Vector3 pos, Color color)
        {
            if (discard) return;
            var nm = m.inverse.transpose;
            var b = BucketAt(pos);
            var uv = atlas.UV(color);
            int baseIndex = b.V.Count;
            for (int i = 0; i < verts.Length; i++)
            {
                b.V.Add(m.MultiplyPoint3x4(verts[i]));
                b.N.Add(nm.MultiplyVector(norms[i]).normalized);
                b.UV.Add(uv);
            }
            for (int i = 0; i < tris.Length; i++) b.T.Add(baseIndex + tris[i]);
            TriangleCount += tris.Length / 3;
        }

        private int Key(Vector3 p)
        {
            int cx = Mathf.FloorToInt(p.x / chunkSize) + 100;
            int cz = Mathf.FloorToInt(p.z / chunkSize) + 100;
            return cx * 1000 + cz;
        }

        public void Add(LowPoly.Shape shape, Vector3 pos, Quaternion rot, Vector3 scale, Color color)
        {
            if (discard) return;
            var mesh = LowPoly.Get(shape);
            var m = Matrix4x4.TRS(pos, rot, scale);
            var nm = m.inverse.transpose;
            var b = BucketAt(pos);
            var uv = atlas.UV(color);
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var tris = mesh.triangles;
            int baseIndex = b.V.Count;
            for (int i = 0; i < verts.Length; i++)
            {
                b.V.Add(m.MultiplyPoint3x4(verts[i]));
                b.N.Add(nm.MultiplyVector(norms[i]).normalized);
                b.UV.Add(uv);
            }
            for (int i = 0; i < tris.Length; i++) b.T.Add(baseIndex + tris[i]);
            TriangleCount += tris.Length / 3;
        }

        /// <summary>結合メッシュの GameObject を作る。</summary>
        public void Build(Transform parent, Material material, string name)
        {
            atlas.Apply();
            foreach (var kv in buckets)
            {
                var b = kv.Value;
                if (b.V.Count == 0) continue;
                var mesh = new Mesh { name = name + "_" + kv.Key };
                if (b.V.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(b.V);
                mesh.SetNormals(b.N);
                mesh.SetUVs(0, b.UV);
                mesh.SetTriangles(b.T, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(name + "_" + kv.Key);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.On;
            }
            buckets.Clear();
        }
    }

    /// <summary>テクスチャ付きマテリアルの作成（URP/Built-in 両対応）。</summary>
    public static class WorldMaterials
    {
        public static Material Textured(Texture tex, float smoothness = 0.05f, bool emissive = false, float emission = 0.9f)
        {
            var m = new Material(MaterialCache.LitShader) { name = "World_" + (tex != null ? tex.name : "tex") };
            MaterialCache.ApplyColor(m, Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", tex);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.white * emission);
            }
            return m;
        }

        /// <summary>頂点カラー・透明に対応した無照明マテリアル（星空・パーティクル・滝など）。</summary>
        public static Material Unlit(Color color, Texture tex = null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = MaterialCache.LitShader;
            var m = new Material(sh) { name = "WorldUnlit" };
            if (tex != null)
            {
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            }
            MaterialCache.ApplyColor(m, color);
            m.renderQueue = 3000;
            return m;
        }
    }
}
