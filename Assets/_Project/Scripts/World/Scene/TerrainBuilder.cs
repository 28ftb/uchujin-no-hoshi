using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 地形の表示：ランタイム生成のカラーマップ（バイオームの色・道・広場・農場）を貼ったチャンク地形と MeshCollider、
    /// 周囲の見た目だけの地形、水面（川・池・海・滝）、見えない外周の壁。
    /// 踏み固められた道（村仕様 §11）はカラーマップを部分的に塗り直して表現する。
    /// </summary>
    public sealed class TerrainBuilder
    {
        public const int ColorMapSize = 512;
        private readonly GeneratedWorld W;
        private readonly Heightfield F;
        private readonly TerrainShape S;
        private readonly WorldPalette P;

        public Texture2D ColorMap { get; private set; }
        public Texture2D OuterMap { get; private set; }
        public Material TerrainMaterial { get; private set; }
        public Material OuterMaterial { get; private set; }
        private Color32[] basePixels;
        private Color32[] pixels;
        private readonly List<Material> ownedMaterials = new List<Material>();

        public TerrainBuilder(GeneratedWorld world)
        {
            W = world;
            F = world.Field;
            S = world.Shape;
            P = world.Palette;
        }

        private static float PixelToWorld(int i) => -WorldTuning.Half + (i + 0.5f) * WorldTuning.WorldSize / ColorMapSize;

        private static int WorldToPixel(float w) => Mathf.FloorToInt((w + WorldTuning.Half) / WorldTuning.WorldSize * ColorMapSize);

        /// <summary>地表の基本色（バイオーム）。</summary>
        public Color BiomeColor(float x, float z, float h, float slope)
        {
            float detail = S.Detail(x, z);
            var c = Color.Lerp(P.GrassDark, P.GrassLight, detail * 0.5f + 0.5f);
            float forest = S.Forest01(x, z);
            float fw = GeoUtil.SmoothStep(S.ForestThreshold - 0.06f, S.ForestThreshold + 0.1f, forest);
            c = Color.Lerp(c, P.ForestFloor, fw * 0.85f);
            float flower = S.Flower01(x, z);
            if (flower > S.FlowerThreshold - 0.04f && fw < 0.5f)
            {
                c = Color.Lerp(c, P.Meadow, 0.5f);
                // 花畑のぽつぽつ
                uint hsh = (uint)(Mathf.FloorToInt(x * 1.7f) * 73856093 ^ Mathf.FloorToInt(z * 1.7f) * 19349663);
                if (hsh % 9 == 0) c = Color.Lerp(c, P.Flowers[(int)(hsh / 9 % (uint)P.Flowers.Length)], 0.55f);
            }
            float dm = S.MountainFactor(x, z);
            float rock = (1f - GeoUtil.SmoothStep(0.55f, 1.0f, dm)) * 0.85f;
            rock = Mathf.Max(rock, GeoUtil.SmoothStep(27f, 40f, slope));
            if (rock > 0f) c = Color.Lerp(c, Color.Lerp(P.Rock, P.RockDark, detail * 0.5f + 0.5f), rock);
            float d = S.CoastD(x, z);
            if (d < 16f)
            {
                var sand = h < 0.15f ? P.Sand * 0.82f : P.Sand;
                sand.a = 1f;
                c = Color.Lerp(c, sand, GeoUtil.SmoothStep(16f, 9f, d));
            }
            c.a = 1f;
            return c;
        }

        // =================================================================
        // カラーマップ
        // =================================================================

        public void BuildColorMap()
        {
            int n = ColorMapSize;
            pixels = new Color32[n * n];
            for (int py = 0; py < n; py++)
            {
                float z = PixelToWorld(py);
                for (int px = 0; px < n; px++)
                {
                    float x = PixelToWorld(px);
                    float h = F.SampleTerrain(x, z);
                    float slope = F.Slope(x, z);
                    var c = BiomeColor(x, z, h, slope);
                    var kind = F.KindAt(x, z);
                    if (kind != WaterKind.None)
                    {
                        float ws = F.WaterSurfaceAt(x, z);
                        if (ws > h) c = Color.Lerp(kind == WaterKind.Sea ? P.Sand * 0.8f : P.Mud, P.DeepSea, Mathf.Clamp01((ws - h) / 3f) * 0.5f);
                        else c = Color.Lerp(c, P.Mud, 0.55f);
                    }
                    else if (F.WaterNear(x, z, 2.5f)) c = Color.Lerp(c, P.Mud, 0.35f);
                    c.a = 1f;
                    pixels[py * n + px] = c;
                }
            }
            PaintPads();
            PaintRoads();
            basePixels = (Color32[])pixels.Clone();
            ColorMap = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 4,
                name = "WorldColorMap"
            };
            ColorMap.SetPixels32(pixels);
            ColorMap.Apply(true, false);
        }

        private void Blend(int px, int py, Color c, float a)
        {
            if (px < 0 || py < 0 || px >= ColorMapSize || py >= ColorMapSize || a <= 0f) return;
            int i = py * ColorMapSize + px;
            pixels[i] = Color.Lerp(pixels[i], c, Mathf.Clamp01(a));
        }

        private void PaintPads()
        {
            var fr = W.Layout.FarmRect;
            foreach (var pad in W.Pads)
            {
                float r = pad.Radius + 2f;
                int x0 = WorldToPixel(pad.Center.x - r), x1 = WorldToPixel(pad.Center.x + r);
                int y0 = WorldToPixel(pad.Center.y - r), y1 = WorldToPixel(pad.Center.y + r);
                for (int py = y0; py <= y1; py++)
                {
                    float z = PixelToWorld(Mathf.Clamp(py, 0, ColorMapSize - 1));
                    for (int px = x0; px <= x1; px++)
                    {
                        float x = PixelToWorld(Mathf.Clamp(px, 0, ColorMapSize - 1));
                        float sd = pad.SignedDistance(x, z);
                        float cov = 1f - GeoUtil.SmoothStep(-0.6f, 0.9f, sd);
                        if (cov <= 0f) continue;
                        uint hsh = (uint)(Mathf.FloorToInt(x * 0.5f) * 73856093 ^ Mathf.FloorToInt(z * 0.5f) * 19349663);
                        float tile = (hsh % 7) / 7f;
                        switch (pad.Kind)
                        {
                            case PadKind.Plaza:
                            {
                                var c = Color.Lerp(P.Plaza, P.Stone, tile * 0.6f);
                                // 石畳の目地
                                if (Mathf.Repeat(x, 2f) < 0.15f || Mathf.Repeat(z, 2f) < 0.15f) c *= 0.88f;
                                float dc = Vector2.Distance(new Vector2(x, z), pad.Center);
                                if (Mathf.Abs(dc - WorldTuning.PlazaRadius * 0.55f) < 0.45f) c *= 0.85f;
                                c.a = 1f;
                                Blend(px, py, c, cov);
                                break;
                            }
                            case PadKind.Building:
                                Blend(px, py, Color.Lerp(P.Dirt, P.Stone, 0.45f + tile * 0.2f), cov * 0.85f);
                                break;
                            case PadKind.HousePlot:
                                Blend(px, py, Color.Lerp(P.GrassLight, P.Meadow, tile), cov * 0.6f);
                                break;
                            case PadKind.Crater:
                            {
                                float dc = Vector2.Distance(new Vector2(x, z), pad.Center) / WorldTuning.CrashCraterRadius;
                                var scorch = Color.Lerp(new Color(0.22f, 0.2f, 0.2f), P.Dirt * 0.7f, GeoUtil.SmoothStep(0.2f, 1.1f, dc) + tile * 0.15f);
                                scorch.a = 1f;
                                Blend(px, py, scorch, cov * (1f - GeoUtil.SmoothStep(0.9f, 1.4f, dc) * 0.7f));
                                break;
                            }
                            case PadKind.Lookout:
                                Blend(px, py, Color.Lerp(P.Stone, P.Plaza, tile), cov);
                                break;
                            case PadKind.MineMouth:
                                Blend(px, py, Color.Lerp(P.RockDark, P.Dirt, tile), cov * 0.9f);
                                break;
                            case PadKind.Farm:
                                if (fr.Contains(new Vector2(x, z)))
                                {
                                    var soil = Mathf.Repeat(x, 1.5f) < 0.3f ? P.Soil * 0.85f : P.Soil;
                                    soil.a = 1f;
                                    Blend(px, py, soil, 0.9f);
                                }
                                break;
                            case PadKind.Generic:
                                if (pad.FacilityId != null) Blend(px, py, P.GrassLight, cov * 0.5f);
                                break;
                        }
                    }
                }
            }
        }

        private void PaintRoads()
        {
            foreach (var seg in W.RoadSegments)
            {
                if (seg.Bridge) continue;
                float reach = seg.HalfWidth + 1f;
                var mn = Vector2.Min(seg.A, seg.B) - Vector2.one * reach;
                var mx = Vector2.Max(seg.A, seg.B) + Vector2.one * reach;
                int x0 = WorldToPixel(mn.x), x1 = WorldToPixel(mx.x);
                int y0 = WorldToPixel(mn.y), y1 = WorldToPixel(mx.y);
                bool trail = seg.HalfWidth < 1.5f;
                for (int py = y0; py <= y1; py++)
                {
                    float z = PixelToWorld(Mathf.Clamp(py, 0, ColorMapSize - 1));
                    for (int px = x0; px <= x1; px++)
                    {
                        float x = PixelToWorld(Mathf.Clamp(px, 0, ColorMapSize - 1));
                        float d = GeoUtil.DistToSegment(new Vector2(x, z), seg.A, seg.B, out _);
                        float cov = 1f - GeoUtil.SmoothStep(seg.HalfWidth - 0.5f, seg.HalfWidth + 0.6f, d);
                        if (cov <= 0f) continue;
                        uint hsh = (uint)(Mathf.FloorToInt(x * 0.8f) * 73856093 ^ Mathf.FloorToInt(z * 0.8f) * 19349663);
                        Color c;
                        if (seg.Paved) c = Color.Lerp(P.Stone, P.Plaza, (hsh % 5) / 5f);
                        else c = Color.Lerp(P.Dirt, P.Sand, (hsh % 4) / 10f + (trail ? 0.15f : 0f));
                        c.a = 1f;
                        Blend(px, py, c, cov * (trail ? 0.75f : 0.95f));
                    }
                }
            }
        }

        /// <summary>踏み跡を塗る（wear 0〜255、セルは WorldTuning.WearCell 角）。</summary>
        public void ApplyWearCell(int cellX, int cellZ, int wear)
        {
            if (basePixels == null) return;
            float cell = WorldTuning.WearCell;
            float x0w = -WorldTuning.Half + cellX * cell, z0w = -WorldTuning.Half + cellZ * cell;
            int x0 = WorldToPixel(x0w), x1 = WorldToPixel(x0w + cell);
            int y0 = WorldToPixel(z0w), y1 = WorldToPixel(z0w + cell);
            float k = Mathf.Clamp01((wear - WorldTuning.WearVisibleThreshold) / (float)(WorldTuning.WearFullThreshold - WorldTuning.WearVisibleThreshold)) * 0.6f;
            var dirt = Color.Lerp(P.Dirt, P.GrassDark, 0.25f);
            for (int py = Mathf.Max(0, y0); py < Mathf.Min(ColorMapSize, y1 + 1); py++)
                for (int px = Mathf.Max(0, x0); px < Mathf.Min(ColorMapSize, x1 + 1); px++)
                {
                    int i = py * ColorMapSize + px;
                    // ぼかし：セルの中心ほど濃い
                    float fx = (PixelToWorld(px) - x0w) / cell - 0.5f, fz = (PixelToWorld(py) - z0w) / cell - 0.5f;
                    float soft = 1f - Mathf.Clamp01(Mathf.Sqrt(fx * fx + fz * fz) * 1.2f);
                    pixels[i] = Color.Lerp(basePixels[i], dirt, k * (0.5f + soft * 0.5f));
                }
        }

        public void ApplyColorMapChanges()
        {
            if (ColorMap == null) return;
            ColorMap.SetPixels32(pixels);
            ColorMap.Apply(true, false);
        }

        // =================================================================
        // 地形メッシュ
        // =================================================================

        public void BuildTerrain(Transform parent)
        {
            TerrainMaterial = WorldMaterials.Textured(ColorMap, 0.04f);
            ownedMaterials.Add(TerrainMaterial);
            var root = new GameObject("Terrain");
            root.transform.SetParent(parent, false);
            int cells = WorldTuning.ChunkCells;
            int chunks = (F.N - 1) / cells;
            for (int cz = 0; cz < chunks; cz++)
                for (int cx = 0; cx < chunks; cx++)
                    BuildChunk(root.transform, cx * cells, cz * cells, cells);
        }

        private void BuildChunk(Transform parent, int ix0, int iz0, int cells)
        {
            int vn = cells + 1;
            var verts = new Vector3[vn * vn];
            var norms = new Vector3[vn * vn];
            var uvs = new Vector2[vn * vn];
            for (int z = 0; z < vn; z++)
                for (int x = 0; x < vn; x++)
                {
                    int ix = ix0 + x, iz = iz0 + z;
                    float wx = F.XOf(ix), wz = F.ZOf(iz);
                    int i = z * vn + x;
                    verts[i] = new Vector3(wx, F.H[F.Index(ix, iz)], wz);
                    norms[i] = F.Normal(ix, iz);
                    uvs[i] = new Vector2((wx + WorldTuning.Half) / WorldTuning.WorldSize, (wz + WorldTuning.Half) / WorldTuning.WorldSize);
                }
            var tris = new int[cells * cells * 6];
            int t = 0;
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int a = z * vn + x, b = a + 1, c = a + vn, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = $"TerrainChunk_{ix0}_{iz0}" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = TerrainMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        /// <summary>ワールドの外側の見た目（丘・海）。コライダーなし。</summary>
        public void BuildOuterRing(Transform parent)
        {
            float half = WorldTuning.Half;
            float outer = half + WorldTuning.OuterRingWidth;
            float step = WorldTuning.OuterRingStep;
            int n = Mathf.RoundToInt(outer * 2f / step) + 1;
            // 外側用の小さなカラーマップ
            int tn = 128;
            OuterMap = new Texture2D(tn, tn, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "WorldOuterMap" };
            var tp = new Color32[tn * tn];
            for (int y = 0; y < tn; y++)
                for (int x = 0; x < tn; x++)
                {
                    float wx = -outer + (x + 0.5f) / tn * outer * 2f;
                    float wz = -outer + (y + 0.5f) / tn * outer * 2f;
                    float h = OuterHeight(wx, wz);
                    tp[y * tn + x] = BiomeColor(wx, wz, h, 0f);
                }
            OuterMap.SetPixels32(tp);
            OuterMap.Apply(true, false);
            OuterMaterial = WorldMaterials.Textured(OuterMap, 0.03f);
            ownedMaterials.Add(OuterMaterial);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var heights = new float[n * n];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float wx = -outer + x * step, wz = -outer + z * step;
                    heights[z * n + x] = OuterHeight(wx, wz);
                    verts.Add(new Vector3(wx, heights[z * n + x], wz));
                    uvs.Add(new Vector2((wx + outer) / (outer * 2f), (wz + outer) / (outer * 2f)));
                }
            float inner = half - step * 0.6f;
            for (int z = 0; z < n - 1; z++)
                for (int x = 0; x < n - 1; x++)
                {
                    float wx0 = -outer + x * step, wz0 = -outer + z * step;
                    float wx1 = wx0 + step, wz1 = wz0 + step;
                    // 内側（ワールド内）は本物の地形があるので省く
                    if (wx0 >= -inner && wx1 <= inner && wz0 >= -inner && wz1 <= inner) continue;
                    int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            var mesh = new Mesh { name = "OuterRing" };
            if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var go = new GameObject("OuterRing");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = OuterMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        private float OuterHeight(float x, float z)
        {
            float half = WorldTuning.Half;
            float edge = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            if (edge <= half - 1f) return F.SampleTerrain(x, z) - 0.05f;
            // 境界付近は本物の地形とつなぐ
            float baseH = S.BaseHeight(x, z);
            float inside = F.SampleTerrain(Mathf.Clamp(x, -half, half), Mathf.Clamp(z, -half, half));
            float k = GeoUtil.SmoothStep(half - 1f, half + 25f, edge);
            return Mathf.Lerp(inside, baseH, k) - 0.05f;
        }

        // =================================================================
        // 水面
        // =================================================================

        public void BuildWater(Transform parent)
        {
            var root = new GameObject("Water");
            root.transform.SetParent(parent, false);
            // 海（ワールドの外まで続く大きな面）
            var sea = MakeQuad("Sea", new Vector3(0f, WorldTuning.SeaLevel, 0f), (WorldTuning.Half + WorldTuning.OuterRingWidth) * 2.2f, P.Sea);
            sea.transform.SetParent(root.transform, true);
            foreach (var rv in W.Rivers) BuildRiverMesh(root.transform, rv);
            foreach (var pd in W.Ponds) BuildPondMesh(root.transform, pd);
            foreach (var lk in W.Lakes) BuildLakeMesh(root.transform, lk);
            if (W.Waterfall != null) BuildWaterfall(root.transform, W.Waterfall);
        }

        private GameObject MakeQuad(string name, Vector3 center, float size, Color color)
        {
            var mesh = new Mesh { name = name };
            float h = size * 0.5f;
            mesh.vertices = new[] { center + new Vector3(-h, 0, -h), center + new Vector3(-h, 0, h), center + new Vector3(h, 0, h), center + new Vector3(h, 0, -h) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialCache.Get(color, 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        private void BuildRiverMesh(Transform parent, RiverPath rv)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i < rv.Points.Count; i++)
            {
                var prev = rv.Points[Mathf.Max(0, i - 1)];
                var next = rv.Points[Mathf.Min(rv.Points.Count - 1, i + 1)];
                var dir = (next - prev).normalized;
                var n = new Vector2(-dir.y, dir.x);
                float w = rv.Radius[i] + 0.7f;
                float y = rv.Surface[i] + 0.02f;
                var l = rv.Points[i] + n * w;
                var r = rv.Points[i] - n * w;
                verts.Add(new Vector3(l.x, y, l.y));
                verts.Add(new Vector3(r.x, y, r.y));
                norms.Add(Vector3.up);
                norms.Add(Vector3.up);
                uvs.Add(new Vector2(0f, i * 0.2f));
                uvs.Add(new Vector2(1f, i * 0.2f));
                if (i > 0)
                {
                    int a = (i - 1) * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
            FixUp(verts, tris);
            var mesh = new Mesh { name = "River" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("River");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialCache.Get(P.River, 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>上向きの面になるよう三角形の巻き順を揃える。</summary>
        private static void FixUp(List<Vector3> v, List<int> t)
        {
            for (int i = 0; i < t.Count; i += 3)
            {
                var n = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (n.y < 0f)
                {
                    int tmp = t[i + 1];
                    t[i + 1] = t[i + 2];
                    t[i + 2] = tmp;
                }
            }
        }

        private void BuildPondMesh(Transform parent, PondInfo pd)
        {
            int seg = 28;
            var verts = new List<Vector3> { new Vector3(pd.Center.x, pd.Surface + 0.02f, pd.Center.y) };
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                verts.Add(new Vector3(pd.Center.x + Mathf.Cos(a) * (pd.Radius + 0.8f), pd.Surface + 0.02f, pd.Center.y + Mathf.Sin(a) * (pd.Radius + 0.8f)));
                if (i > 0)
                {
                    tris.Add(0); tris.Add(i + 1); tris.Add(i);
                }
            }
            FixUp(verts, tris);
            var norms = new List<Vector3>();
            foreach (var _ in verts) norms.Add(Vector3.up);
            var mesh = new Mesh { name = "Pond" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Pond");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialCache.Get(P.Pond, 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>湖の水面（岸に沿って波打つ形。岬の陸は水面より高いので上に見える）。</summary>
        private void BuildLakeMesh(Transform parent, LakeInfo lk)
        {
            int seg = 72;
            float y = lk.Surface + 0.02f;
            var verts = new List<Vector3> { new Vector3(lk.Center.x, y, lk.Center.y) };
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                float r = lk.RadiusAt(a) + 0.8f;
                verts.Add(new Vector3(lk.Center.x + Mathf.Cos(a) * r, y, lk.Center.y + Mathf.Sin(a) * r));
                if (i > 0)
                {
                    tris.Add(0); tris.Add(i + 1); tris.Add(i);
                }
            }
            FixUp(verts, tris);
            var norms = new List<Vector3>();
            foreach (var _ in verts) norms.Add(Vector3.up);
            var mesh = new Mesh { name = "Lake" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject("Lake");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialCache.Get(Color.Lerp(P.Pond, P.DeepSea, 0.35f), 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildWaterfall(Transform parent, WaterfallInfo wf)
        {
            var root = new GameObject("Waterfall");
            root.transform.SetParent(parent, false);
            float drop = Mathf.Max(0.8f, wf.Top.y - wf.Bottom.y);
            var mid = (wf.Top + wf.Bottom) * 0.5f;
            root.transform.position = new Vector3(mid.x, wf.Bottom.y, mid.z);
            root.transform.rotation = Quaternion.Euler(0f, wf.Yaw, 0f);
            var sheet = PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0f, drop * 0.5f, 0f), new Vector3(wf.Width + 0.6f, drop + 0.3f, 0.35f), new Color(0.82f, 0.94f, 1f, 0.7f), "Fall");
            for (int i = 0; i < 6; i++)
            {
                float x = (i / 5f - 0.5f) * wf.Width;
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(x, 0.1f, 0.6f + (i % 2) * 0.3f), new Vector3(1.1f, 0.5f, 0.9f), new Color(0.96f, 0.98f, 1f, 0.85f), "Foam");
            }
            for (int i = 0; i < 3; i++)
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3((i - 1) * wf.Width * 0.6f, drop * 0.3f, -0.6f), new Vector3(1.4f, 1.2f, 1.4f) * (1f + i * 0.2f), P.RockDark, "Rock");
        }

        // =================================================================
        // 外周の壁
        // =================================================================

        public void BuildWalls(Transform parent)
        {
            var root = new GameObject("WorldBounds");
            root.transform.SetParent(parent, false);
            float h = WorldTuning.Half - WorldTuning.WallInset;
            float height = 200f;
            void Wall(Vector3 c, Vector3 size)
            {
                var go = new GameObject("Wall");
                go.transform.SetParent(root.transform, false);
                go.transform.position = c;
                var bc = go.AddComponent<BoxCollider>();
                bc.size = size;
            }
            Wall(new Vector3(h + 1f, 0f, 0f), new Vector3(2f, height, WorldTuning.WorldSize + 4f));
            Wall(new Vector3(-h - 1f, 0f, 0f), new Vector3(2f, height, WorldTuning.WorldSize + 4f));
            Wall(new Vector3(0f, 0f, h + 1f), new Vector3(WorldTuning.WorldSize + 4f, height, 2f));
            Wall(new Vector3(0f, 0f, -h - 1f), new Vector3(WorldTuning.WorldSize + 4f, height, 2f));
        }

        // =================================================================
        // 地図用のテクスチャ（上から見た図。陰影・水・建物）
        // =================================================================

        public Texture2D BuildMapTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "WorldMap"
            };
            var px = new Color32[size * size];
            var light = new Vector3(-0.5f, 0.75f, 0.45f).normalized;
            for (int y = 0; y < size; y++)
            {
                float z = -WorldTuning.Half + (y + 0.5f) / size * WorldTuning.WorldSize;
                for (int x = 0; x < size; x++)
                {
                    float wx = -WorldTuning.Half + (x + 0.5f) / size * WorldTuning.WorldSize;
                    int cx = Mathf.Clamp(Mathf.FloorToInt((wx + WorldTuning.Half) / WorldTuning.WorldSize * ColorMapSize), 0, ColorMapSize - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt((z + WorldTuning.Half) / WorldTuning.WorldSize * ColorMapSize), 0, ColorMapSize - 1);
                    Color c = basePixels != null ? (Color)basePixels[cz * ColorMapSize + cx] : Color.green;
                    if (F.IsWater(wx, z))
                    {
                        var k = F.KindAt(wx, z);
                        c = k == WaterKind.Sea ? new Color(0.25f, 0.5f, 0.82f) : new Color(0.38f, 0.7f, 0.92f);
                    }
                    else
                    {
                        F.ToGrid(wx, z, out int gx, out int gz);
                        float shade = Mathf.Clamp01(Vector3.Dot(F.Normal(gx, gz), light) * 1.15f);
                        c *= Mathf.Lerp(0.72f, 1.08f, shade);
                        // 等高線（5m ごと）
                        float h = F.SampleTerrain(wx, z);
                        if (Mathf.Repeat(h, 5f) < 0.18f && h > 1.5f) c *= 0.9f;
                    }
                    c.a = 1f;
                    px[y * size + x] = c;
                }
            }
            // 建物の形
            foreach (var f in W.Layout.Facilities)
            {
                if (f.type == FacilityType.Plaza || f.type == FacilityType.Streetlight || f.type == FacilityType.Bench || f.type == FacilityType.RocketSite) continue;
                var half = new Vector2(f.size.x * 0.5f, f.size.z * 0.5f);
                float r = half.magnitude;
                var pad = new PadInfo { Center = new Vector2(f.position.x, f.position.z), HalfSize = half, Yaw = f.rotationY };
                int x0 = Mathf.FloorToInt((f.position.x - r + WorldTuning.Half) / WorldTuning.WorldSize * size);
                int x1 = Mathf.CeilToInt((f.position.x + r + WorldTuning.Half) / WorldTuning.WorldSize * size);
                int y0 = Mathf.FloorToInt((f.position.z - r + WorldTuning.Half) / WorldTuning.WorldSize * size);
                int y1 = Mathf.CeilToInt((f.position.z + r + WorldTuning.Half) / WorldTuning.WorldSize * size);
                for (int y = Mathf.Max(0, y0); y <= Mathf.Min(size - 1, y1); y++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(size - 1, x1); x++)
                    {
                        float wx = -WorldTuning.Half + (x + 0.5f) / size * WorldTuning.WorldSize;
                        float wz = -WorldTuning.Half + (y + 0.5f) / size * WorldTuning.WorldSize;
                        float sd = pad.SignedDistance(wx, wz);
                        if (sd > 0f) continue;
                        Color c = sd > -0.6f ? new Color(0.35f, 0.28f, 0.25f) : new Color(0.86f, 0.78f, 0.68f);
                        px[y * size + x] = c;
                    }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        public void Dispose()
        {
            foreach (var m in ownedMaterials) if (m != null) Object.Destroy(m);
            ownedMaterials.Clear();
            if (ColorMap != null) Object.Destroy(ColorMap);
            if (OuterMap != null) Object.Destroy(OuterMap);
            ColorMap = null;
            OuterMap = null;
        }
    }
}
