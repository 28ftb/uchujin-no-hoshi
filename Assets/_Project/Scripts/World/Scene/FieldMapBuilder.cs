using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.World
{
    /// <summary>
    /// 地域・道中マップの見た目と当たり判定を作る（生成バージョン3の試作。マップ設計 v0.3 §10.1 の方式A）。
    /// 地形メッシュ（チャンクごとのコライダー付き）・水面・見えない壁・自然物・東屋や桟橋・道標・ゲートを作り、
    /// マップをエリアとして登録する。ゲートは歩いて入るだけでとなりのマップへ移る（[E] なし。短い暗転）。
    /// </summary>
    public static class FieldMapBuilder
    {
        private const int ChunkCells = 25;
        private static readonly Color Wood = new Color(0.55f, 0.38f, 0.24f);
        private static readonly Color DarkWood = new Color(0.36f, 0.25f, 0.17f);
        private static readonly Color Lantern = new Color(1f, 0.85f, 0.55f);

        /// <summary>村のゲートと、村の外のマップをすべて作る。作ったテクスチャ・マテリアルは owned に入れる（後で捨てる）。</summary>
        public static void BuildAll(GeneratedWorld g, Transform parent, PaletteAtlas atlas, Material lit, Material glow,
            Func<string, string> placeWord, List<UnityEngine.Object> owned)
        {
            if (g == null) return;
            var gates = new Dictionary<string, FieldGateInfo>();
            foreach (var vg in g.VillageGates) gates[vg.Id] = vg;
            foreach (var m in g.FieldMaps)
                foreach (var mg in m.Gates) gates[mg.Id] = mg;

            // 村の端のゲート（となりのマップができている時だけ通れる）
            var villageRoot = new GameObject("VillageGates").transform;
            villageRoot.SetParent(parent, false);
            foreach (var vg in g.VillageGates)
            {
                if (!gates.TryGetValue(vg.PartnerId, out var partner)) continue;
                BuildGate(villageRoot, vg, partner, g.Palette);
                var sign = new SignInfo { Id = "sign." + vg.Id, Position = vg.Arrival + Right(vg.Yaw) * 3.4f - Vector3.up * 0.2f, IsSignpost = true };
                sign.Destinations.Add("region." + vg.OtherMapId);
                sign.DestinationYaws.Add(vg.Yaw);
                BuildSignpost(villageRoot, sign, placeWord, g.Skeleton);
            }

            foreach (var m in g.FieldMaps)
            {
                try
                {
                    Build(m, parent, atlas, lit, glow, placeWord, owned, gates, g.Skeleton);
                    RegisterArea(m);
                }
                catch (Exception e) { Debug.LogError($"[World] マップ {m.Id} の生成に失敗: {e}"); }
            }
        }

        private static void RegisterArea(FieldMap m)
        {
            var first = m.Gates.Count > 0 ? m.Gates[0] : null;
            Game.Instance.Areas.Register(new AreaDescriptor
            {
                Id = m.AreaId,
                NameJa = m.NameJa,
                NameEn = m.NameEn,
                IsIndoor = false,
                IsUnderground = false,
                ShowWeather = true,
                DefaultSpawn = first != null ? first.Arrival : m.Origin + new Vector3(m.Width * 0.5f, 30f, m.Depth * 0.5f),
                DefaultYaw = first != null ? first.ArrivalYaw : 0f
            });
        }

        public static void Build(FieldMap m, Transform parent, PaletteAtlas atlas, Material lit, Material glow,
            Func<string, string> placeWord, List<UnityEngine.Object> owned, Dictionary<string, FieldGateInfo> gates, StarSkeleton sk)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var root = new GameObject("Map_" + m.Id).transform;
            root.SetParent(parent, false);
            BuildTerrain(m, root, owned);
            BuildWater(m, root);
            BuildWalls(m, root);

            var gw = new GeneratedWorld { Palette = m.Palette };
            gw.Nature.AddRange(m.Nature);
            NatureBuilder.Build(gw, root, atlas, lit, glow);

            var structures = new GameObject("Structures").transform;
            structures.SetParent(root, false);
            foreach (var s in m.Structures)
            {
                var pos = m.ToWorld(s.Local, s.Y);
                switch (s.Kind)
                {
                    case FieldStructureKind.Gazebo: Gazebo(structures, pos, s.Yaw, m.Palette); break;
                    case FieldStructureKind.Pier: Pier(structures, pos, s.Yaw); break;
                    case FieldStructureKind.SteppingStone:
                    {
                        var st = PrimitiveBuilder.Create(PrimitiveType.Cylinder, structures, pos, new Vector3(0.95f, 0.14f, 0.85f), Color.Lerp(m.Palette.Rock, Color.white, 0.12f), "SteppingStone");
                        st.transform.rotation = Quaternion.Euler(0f, s.Yaw, 0f);
                        break;
                    }
                    case FieldStructureKind.Ruin:
                    {
                        // 石積みの跡（苔むした四角い石）
                        var stone = PrimitiveBuilder.Create(PrimitiveType.Cube, structures, pos + Vector3.up * 0.35f * s.Size, new Vector3(1.2f, 0.7f, 0.9f) * s.Size, Color.Lerp(m.Palette.Stone, m.Palette.GrassDark, 0.25f), "RuinStone");
                        stone.transform.rotation = Quaternion.Euler(0f, s.Yaw, 0f);
                        break;
                    }
                    case FieldStructureKind.CaveMouth:
                    {
                        // 崖の洞穴の口（暗い穴と、まわりの岩）
                        var rootC = StructureBuilder.Root(structures, "CaveMouth", pos, s.Yaw);
                        StructureBuilder.Box(rootC, new Vector3(0f, 1.1f, -0.4f), new Vector3(2.4f, 2.2f, 1.2f), new Color(0.05f, 0.05f, 0.08f), false, "CaveDark");
                        StructureBuilder.Box(rootC, new Vector3(-1.5f, 1.2f, -0.2f), new Vector3(0.8f, 2.6f, 1.4f), m.Palette.RockDark, true, "CaveRockL");
                        StructureBuilder.Box(rootC, new Vector3(1.5f, 1.2f, -0.2f), new Vector3(0.8f, 2.6f, 1.4f), m.Palette.RockDark, true, "CaveRockR");
                        StructureBuilder.Box(rootC, new Vector3(0f, 2.55f, -0.2f), new Vector3(3.8f, 0.7f, 1.4f), m.Palette.Rock, false, "CaveLintel");
                        break;
                    }
                    case FieldStructureKind.Waterfall:
                    {
                        // 滝の水の幕（崖の上から滝つぼまで）
                        var rootW = StructureBuilder.Root(structures, "FieldWaterfall", pos, s.Yaw);
                        var curtain = PrimitiveBuilder.Create(PrimitiveType.Cube, rootW, new Vector3(0f, -s.Size * 0.5f, 1.5f), new Vector3(5.5f, Mathf.Max(1f, s.Size), 0.4f), new Color(0.78f, 0.92f, 1f), "FallCurtain", false);
                        curtain.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(new Color(0.78f, 0.92f, 1f), 0.9f, true);
                        var foam = PrimitiveBuilder.Create(PrimitiveType.Cylinder, rootW, new Vector3(0f, -s.Size + 0.1f, 3.5f), new Vector3(6f, 0.15f, 4f), new Color(0.92f, 0.97f, 1f), "FallFoam", false);
                        foam.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(new Color(0.92f, 0.97f, 1f), 0.6f, true);
                        break;
                    }
                    case FieldStructureKind.Crystal:
                    {
                        var c = PrimitiveBuilder.Create(PrimitiveType.Cylinder, structures, pos + Vector3.up * 0.9f * s.Size, new Vector3(0.5f, 0.9f, 0.5f) * s.Size, m.Palette.CrystalColor, "HiddenCrystal", false);
                        c.GetComponent<Renderer>().sharedMaterial = MaterialCache.GetEmissive(m.Palette.CrystalColor);
                        c.transform.rotation = Quaternion.Euler(8f, s.Yaw, 6f);
                        break;
                    }
                    case FieldStructureKind.Signpost:
                    {
                        var sign = new SignInfo { Id = "sign." + m.Id + "." + s.Destination, Position = pos, IsSignpost = true };
                        sign.Destinations.Add("region." + s.Destination);
                        var toGate = GateToward(m, s.Destination);
                        sign.DestinationYaws.Add(toGate != null ? GeoUtil.FacingYaw(pos, toGate.Position) : 0f);
                        BuildSignpost(structures, sign, placeWord, sk);
                        break;
                    }
                }
            }

            var gatesRoot = new GameObject("Gates").transform;
            gatesRoot.SetParent(root, false);
            foreach (var mg in m.Gates)
            {
                if (gates.TryGetValue(mg.PartnerId, out var partner)) BuildGate(gatesRoot, mg, partner, m.Palette);
                else BuildClosedGate(gatesRoot, mg);
            }
            Debug.Log($"[World] マップ {m.Id}（{m.NameJa}）を作成：{m.Width:0}×{m.Depth:0}m・自然物 {m.Nature.Count}・{watch.ElapsedMilliseconds} ms（試行{m.Attempt}）");
        }

        private static FieldGateInfo GateToward(FieldMap m, string other)
        {
            foreach (var g in m.Gates) if (g.OtherMapId == other) return g;
            return null;
        }

        // =================================================================
        // 地形
        // =================================================================

        private static Color PaintColor(FieldMap m, int i)
        {
            var P = m.Palette;
            float jitter = (StableHash.Derive((ulong)i, m.Id) >> 40) * (1f / 16777216f) - 0.5f;
            Color c;
            switch (m.Paint[i])
            {
                case FieldPaint.Meadow: c = P.Meadow; break;
                case FieldPaint.Flowers: c = Color.Lerp(P.Meadow, P.Flowers[i % P.Flowers.Length], 0.22f); break;
                case FieldPaint.Forest: c = P.ForestFloor; break;
                case FieldPaint.WetForest: c = Color.Lerp(P.ForestFloor, P.GrassDark, 0.45f) * 0.9f; break;
                case FieldPaint.Road: c = P.Dirt; break;
                case FieldPaint.Trail: c = Color.Lerp(P.Dirt, P.Grass, 0.4f); break;
                case FieldPaint.Sand: c = P.Sand; break;
                case FieldPaint.Mud: c = P.Mud; break;
                case FieldPaint.Rock: c = Color.Lerp(P.Rock, P.RockDark, 0.3f); break;
                default: c = P.Grass; break;
            }
            float w = m.WaterY[i];
            if (!float.IsNaN(w) && w > m.H[i]) c = Color.Lerp(P.Sand, P.RockDark, Mathf.Clamp01((w - m.H[i]) / 4f));
            c = Color.Lerp(c, Color.black, Mathf.Max(0f, jitter) * 0.08f);
            c = Color.Lerp(c, Color.white, Mathf.Max(0f, -jitter) * 0.05f);
            c.a = 1f;
            return c;
        }

        private static void BuildTerrain(FieldMap m, Transform root, List<UnityEngine.Object> owned)
        {
            var tex = new Texture2D(m.NX, m.NZ, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = "FieldMapColor_" + m.Id
            };
            var px = new Color32[m.NX * m.NZ];
            for (int i = 0; i < px.Length; i++) px[i] = PaintColor(m, i);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            owned.Add(tex);
            var mat = WorldMaterials.Textured(tex, 0.04f);
            owned.Add(mat);

            var tr = new GameObject("Terrain").transform;
            tr.SetParent(root, false);
            for (int z0 = 0; z0 < m.NZ - 1; z0 += ChunkCells)
                for (int x0 = 0; x0 < m.NX - 1; x0 += ChunkCells)
                {
                    int cx = Mathf.Min(ChunkCells, m.NX - 1 - x0);
                    int cz = Mathf.Min(ChunkCells, m.NZ - 1 - z0);
                    BuildChunk(m, tr, mat, x0, z0, cx, cz);
                }
        }

        private static void BuildChunk(FieldMap m, Transform parent, Material mat, int ix0, int iz0, int cx, int cz)
        {
            int vx = cx + 1, vz = cz + 1;
            var verts = new Vector3[vx * vz];
            var norms = new Vector3[vx * vz];
            var uvs = new Vector2[vx * vz];
            for (int z = 0; z < vz; z++)
                for (int x = 0; x < vx; x++)
                {
                    int ix = ix0 + x, iz = iz0 + z;
                    int i = z * vx + x;
                    verts[i] = new Vector3(m.Origin.x + m.XOf(ix), m.H[m.Index(ix, iz)], m.Origin.z + m.ZOf(iz));
                    norms[i] = m.Normal(ix, iz);
                    uvs[i] = new Vector2((ix + 0.5f) / m.NX, (iz + 0.5f) / m.NZ);
                }
            var tris = new int[cx * cz * 6];
            int t = 0;
            for (int z = 0; z < cz; z++)
                for (int x = 0; x < cx; x++)
                {
                    int a = z * vx + x, b = a + 1, c = a + vx, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var mesh = new Mesh { name = $"FieldChunk_{m.Id}_{ix0}_{iz0}" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // =================================================================
        // 水面（グリッドのマスごと。種類ごとに1メッシュ）
        // =================================================================

        private static void BuildWater(FieldMap m, Transform root)
        {
            var wr = new GameObject("Water").transform;
            wr.SetParent(root, false);
            var P = m.Palette;
            BuildWaterKind(m, wr, FieldWater.Stream, P.River, "Stream");
            BuildWaterKind(m, wr, FieldWater.Pond, P.Pond, "Pond");
            BuildWaterKind(m, wr, FieldWater.Lake, Color.Lerp(P.Pond, P.DeepSea, 0.35f), "Lake");
        }

        private static void BuildWaterKind(FieldMap m, Transform parent, FieldWater kind, Color color, string name)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var corner = new float[4];
            for (int iz = 0; iz < m.NZ - 1; iz++)
                for (int ix = 0; ix < m.NX - 1; ix++)
                {
                    int i0 = m.Index(ix, iz), i1 = m.Index(ix + 1, iz), i2 = m.Index(ix, iz + 1), i3 = m.Index(ix + 1, iz + 1);
                    bool any = false;
                    float level = float.MinValue;
                    int[] ids = { i0, i1, i2, i3 };
                    for (int k = 0; k < 4; k++)
                    {
                        int id = ids[k];
                        if (!float.IsNaN(m.WaterY[id]) && m.Water[id] == kind)
                        {
                            any = true;
                            level = Mathf.Max(level, m.WaterY[id]);
                        }
                    }
                    if (!any) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int id = ids[k];
                        // 水の無い角は地面の下へ沈める（水際の線が地形との交わりになり、マスの角ばった縁が見えない）
                        corner[k] = !float.IsNaN(m.WaterY[id]) && m.Water[id] == kind ? m.WaterY[id] : Mathf.Min(level, m.H[id]) - 0.6f;
                    }
                    int b = verts.Count;
                    float x0 = m.Origin.x + m.XOf(ix), x1 = m.Origin.x + m.XOf(ix + 1);
                    float z0 = m.Origin.z + m.ZOf(iz), z1 = m.Origin.z + m.ZOf(iz + 1);
                    verts.Add(new Vector3(x0, corner[0] + 0.03f, z0));
                    verts.Add(new Vector3(x1, corner[1] + 0.03f, z0));
                    verts.Add(new Vector3(x0, corner[2] + 0.03f, z1));
                    verts.Add(new Vector3(x1, corner[3] + 0.03f, z1));
                    tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                    tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
                }
            if (verts.Count == 0) return;
            var norms = new List<Vector3>(verts.Count);
            for (int i = 0; i < verts.Count; i++) norms.Add(Vector3.up);
            var mesh = new Mesh { name = "FieldWater_" + m.Id + "_" + name, indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialCache.Get(color, 0.85f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>遊べる範囲の外へ出ない見えない壁。</summary>
        private static void BuildWalls(FieldMap m, Transform root)
        {
            var wr = new GameObject("Walls").transform;
            wr.SetParent(root, false);
            float y = 0f;
            int n = 0;
            for (int i = 0; i < m.H.Length; i += 37) { y += m.H[i]; n++; }
            y = n > 0 ? y / n : 0f;
            const float thick = 2f, height = 160f;
            AddWall(wr, m.Origin + new Vector3(m.Width * 0.5f, y, -thick * 0.5f), new Vector3(m.Width + thick * 2f, height, thick));
            AddWall(wr, m.Origin + new Vector3(m.Width * 0.5f, y, m.Depth + thick * 0.5f), new Vector3(m.Width + thick * 2f, height, thick));
            AddWall(wr, m.Origin + new Vector3(-thick * 0.5f, y, m.Depth * 0.5f), new Vector3(thick, height, m.Depth));
            AddWall(wr, m.Origin + new Vector3(m.Width + thick * 0.5f, y, m.Depth * 0.5f), new Vector3(thick, height, m.Depth));
        }

        private static void AddWall(Transform parent, Vector3 center, Vector3 size)
        {
            var go = new GameObject("Wall");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.AddComponent<BoxCollider>().size = size;
        }

        // =================================================================
        // 建て物
        // =================================================================

        private static Vector3 Right(float yaw) => Quaternion.Euler(0f, yaw, 0f) * Vector3.right;

        private static void Gazebo(Transform parent, Vector3 pos, float yaw, WorldPalette P)
        {
            var root = StructureBuilder.Root(parent, "Gazebo", pos, yaw);
            StructureBuilder.Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(4.6f, 0.24f, 4.6f), Color.Lerp(P.Stone, P.Sand, 0.3f), true, "Floor");
            for (int k = 0; k < 4; k++)
            {
                float sx = (k % 2 == 0 ? -1f : 1f) * 1.95f, sz = (k < 2 ? -1f : 1f) * 1.95f;
                StructureBuilder.Cyl(root, new Vector3(sx, 1.35f, sz), new Vector3(0.22f, 1.25f, 0.22f), Wood, true, "Post");
            }
            StructureBuilder.Box(root, new Vector3(0f, 2.68f, 0f), new Vector3(4.9f, 0.18f, 4.9f), DarkWood, false, "Eave");
            StructureBuilder.GabledRoof(root, 2.75f, 5f, 5f, P.Roofs != null && P.Roofs.Length > 0 ? P.Roofs[0] : DarkWood);
            StructureBuilder.Box(root, new Vector3(0f, 0.5f, 1.2f), new Vector3(2.6f, 0.12f, 0.6f), Wood, true, "Bench");
            StructureBuilder.Box(root, new Vector3(-1.1f, 0.3f, 1.2f), new Vector3(0.14f, 0.4f, 0.5f), DarkWood, false, "BenchLeg");
            StructureBuilder.Box(root, new Vector3(1.1f, 0.3f, 1.2f), new Vector3(0.14f, 0.4f, 0.5f), DarkWood, false, "BenchLeg");
        }

        private static void Pier(Transform parent, Vector3 start, float yaw)
        {
            var root = StructureBuilder.Root(parent, "LakePier", start, yaw);
            const float len = 11f;
            StructureBuilder.Box(root, new Vector3(0f, 0f, len * 0.5f), new Vector3(2f, 0.2f, len), Wood, true, "Deck");
            for (int k = 0; k <= 3; k++)
            {
                float z = k * len / 3f;
                StructureBuilder.Cyl(root, new Vector3(-0.85f, -0.9f, z), new Vector3(0.18f, 0.9f, 0.18f), DarkWood, false, "Pile");
                StructureBuilder.Cyl(root, new Vector3(0.85f, -0.9f, z), new Vector3(0.18f, 0.9f, 0.18f), DarkWood, false, "Pile");
            }
        }

        private static void BuildSignpost(Transform parent, SignInfo sign, Func<string, string> placeWord, StarSkeleton sk)
        {
            var board = StructureBuilder.Signpost(sign, parent, placeWord);
            // 地図アプリには書き留めない（試作：地図アプリは村の地図だけ）。宇宙語の行き先だけ見せる
            board.onRead = (b, player) =>
            {
                var w = sign.Destinations.Count > 0 ? placeWord(sign.Destinations[0]) : null;
                UIManager.Message(Loc.T("道標", "Signpost"), string.IsNullOrEmpty(w) ? Loc.T("見たことのない文字が書かれている。", "Unfamiliar letters are written here.") : "→ 「" + w + "」");
            };
        }

        // =================================================================
        // ゲート
        // =================================================================

        /// <summary>ゲートの見た目（木の門と灯り、向こう側のかすみ）と、入ると移る仕掛け。</summary>
        private static void BuildGate(Transform parent, FieldGateInfo gate, FieldGateInfo partner, WorldPalette P)
        {
            var root = StructureBuilder.Root(parent, "Gate_" + gate.Id, gate.Position, gate.Yaw);
            for (int s = -1; s <= 1; s += 2)
            {
                StructureBuilder.Cyl(root, new Vector3(s * 2.9f, 1.8f, 0f), new Vector3(0.36f, 1.8f, 0.36f), Wood, true, "GatePost");
                StructureBuilder.Glow(root, PrimitiveType.Sphere, new Vector3(s * 2.9f, 3.85f, 0.3f), Vector3.one * 0.34f, Lantern, "GateLamp");
            }
            StructureBuilder.Box(root, new Vector3(0f, 3.55f, 0f), new Vector3(6.6f, 0.32f, 0.4f), DarkWood, false, "GateBeam");
            StructureBuilder.Box(root, new Vector3(0f, 3.95f, 0f), new Vector3(7.2f, 0.22f, 0.55f), Wood, false, "GateTop");
            // 門の布（星の花の色）と、両脇の低い柵（ここが別のマップへの出入口だと分かるように）
            var cloth = P.Flowers != null && P.Flowers.Length > 0 ? P.Flowers[0] : new Color(0.9f, 0.5f, 0.6f);
            StructureBuilder.Box(root, new Vector3(0f, 3.05f, 0.05f), new Vector3(3.2f, 0.75f, 0.06f), cloth, false, "GateCloth");
            StructureBuilder.Box(root, new Vector3(0f, 3.05f, 0.09f), new Vector3(2.2f, 0.12f, 0.02f), Color.Lerp(cloth, Color.white, 0.6f), false, "GateClothStripe");
            for (int s = -1; s <= 1; s += 2)
            {
                StructureBuilder.Box(root, new Vector3(s * 5.2f, 0.55f, -0.4f), new Vector3(4.2f, 0.12f, 0.12f), Wood, false, "Rail");
                StructureBuilder.Cyl(root, new Vector3(s * 7.2f, 0.45f, -0.4f), new Vector3(0.18f, 0.45f, 0.18f), DarkWood, true, "RailPost");
            }

            var trigger = root.gameObject.AddComponent<FieldGate>();
            trigger.ownAreaId = gate.AreaId;
            trigger.targetAreaId = partner.AreaId;
            trigger.targetPosition = partner.Arrival;
            trigger.targetYaw = partner.ArrivalYaw;
            trigger.center = gate.Position;
            trigger.yaw = gate.Yaw;
        }

        /// <summary>となりのマップがまだ無いゲート（試作では作らない。倒木でふさいでおく）。</summary>
        private static void BuildClosedGate(Transform parent, FieldGateInfo gate)
        {
            var root = StructureBuilder.Root(parent, "ClosedGate_" + gate.Id, gate.Position, gate.Yaw);
            StructureBuilder.Cyl(root, new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 3.2f, 0.6f), DarkWood, true, "FallenTrunk", new Vector3(0f, 0f, 90f));
        }
    }

    /// <summary>マップのゲート：ローカルプレイヤーが範囲に入ると、短く暗転してとなりのマップのゲートの前へ移る。</summary>
    public sealed class FieldGate : MonoBehaviour
    {
        public string ownAreaId;
        public string targetAreaId;
        public Vector3 targetPosition;
        public float targetYaw;
        public Vector3 center;
        public float yaw;

        private bool armed;
        private bool going;

        private void Update()
        {
            var game = Game.Instance;
            if (game == null || going) return;
            var p = game.LocalPlayer;
            if (p == null || p.Avatar == null || p.CurrentAreaId != ownAreaId)
            {
                armed = false;
                return;
            }
            if (!Inside(p.Position))
            {
                // 一度範囲の外に出てから入った時だけ移る（着いた直後やロード直後に勝手に移らない）
                armed = true;
                return;
            }
            if (!armed || game.State != GameState.Playing || UIManager.BlocksGameplay || p.InputLocked) return;
            StartCoroutine(Go(p));
        }

        private bool Inside(Vector3 pos)
        {
            var d = pos - center;
            if (Mathf.Abs(d.y) > 6f) return false;
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var right = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            float f = Vector3.Dot(d, fwd), r = Vector3.Dot(d, right);
            return Mathf.Abs(r) <= WorldTuning.FieldGateHalfWidth && f >= -WorldTuning.FieldGateHalfDepth && f <= WorldTuning.FieldGateHalfDepth + 4f;
        }

        private IEnumerator Go(PlayerState p)
        {
            going = true;
            ScreenFader.FadeOut(0.18f);
            yield return new WaitForSecondsRealtime(0.2f);
            if (p != null && p.CurrentAreaId == ownAreaId && Game.Instance != null)
                Game.Instance.Areas.Transition(p, targetAreaId, targetPosition, targetYaw);
            armed = false;
            going = false;
        }
    }
}
