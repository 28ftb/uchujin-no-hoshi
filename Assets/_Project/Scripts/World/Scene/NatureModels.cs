using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.World
{
    /// <summary>
    /// 自然物のモデル（Resources/Art/Nature/&lt;名前&gt;.fbx。Blender で作った1メッシュの軽いモデル）を、NatureBuilder の結合メッシュへ加える。
    /// ・モデルの材質名は「役割」（Leaf・Trunk・Flower・Rock・Crystal など）。役割ごとに、その星のパレットの色を塗る（星ごとに葉や花の色が変わる）。
    /// ・Crystal と Glow の役割は発光の結合メッシュへ入れる。Accent と Glow はモデルの色のまま。
    /// ・モデルは NatureBuilder の従来のプリミティブと同じ大きさ（Scale=1 の時）に作ってある。当たり判定は NatureBuilder が従来どおり付ける。
    /// ・草・貝・倒木はモデルが無いので従来どおり。モデルが無い・読めない時も従来のプリミティブに戻る。
    /// 素材の集め方は Tools/VillageArt/collect_village_art.py。読み込みには取り込み設定の Read/Write が要る（Editor/ArtModelImporter）。
    /// </summary>
    public static class NatureModels
    {
        public const string ResourceFolder = "Art/Nature/";

        private sealed class Part
        {
            public string Role;
            public Vector3[] V, N;
            public int[] T;
            public Color ModelColor;
            public bool Glow;
        }

        private sealed class Model
        {
            public readonly List<Part> Parts = new List<Part>();
        }

        private static readonly Dictionary<NatureKind, Model> cache = new Dictionary<NatureKind, Model>();

        private static string FileOf(NatureKind kind)
        {
            switch (kind)
            {
                case NatureKind.TreeRound: return "tree_round";
                case NatureKind.TreeTall: return "tree_tall";
                case NatureKind.TreeMushroom: return "tree_mushroom";
                case NatureKind.TreeLollipop: return "tree_lollipop";
                case NatureKind.TreeCrystal: return "tree_crystal";
                case NatureKind.Bush: return "bush";
                case NatureKind.Rock: return "rock";
                case NatureKind.Boulder: return "boulder";
                case NatureKind.Flower: return "flower";
                case NatureKind.Mushroom: return "mushroom";
                case NatureKind.GlowMushroom: return "glow_mushroom";
                case NatureKind.Reed: return "reed";
                case NatureKind.Crystal: return "crystal";
                default: return null; // 草・貝・倒木は従来のプリミティブ
            }
        }

        /// <summary>モデルで描けたら true（従来のプリミティブは描かない）。</summary>
        public static bool Add(NatureItem it, WorldPalette P, MeshBatcher batch, MeshBatcher glowBatch)
        {
            var model = Get(it.Kind);
            if (model == null) return false;
            var m = Matrix4x4.TRS(it.Position, Quaternion.Euler(0f, it.Yaw, 0f), Vector3.one * it.Scale);
            foreach (var part in model.Parts)
            {
                var color = RoleColor(part, it, P);
                (part.Glow ? glowBatch : batch).AddMesh(part.V, part.N, part.T, m, it.Position, color);
            }
            return true;
        }

        /// <summary>役割の色（従来のプリミティブと同じパレットの使い方）。</summary>
        private static Color RoleColor(Part part, NatureItem it, WorldPalette P)
        {
            Color leaf = P.Leaves[it.Variant % P.Leaves.Length];
            switch (part.Role)
            {
                case "Trunk": return it.Kind == NatureKind.TreeLollipop ? Color.Lerp(P.Trunk, P.Sand, 0.3f) : P.Trunk;
                case "Leaf": return it.Kind == NatureKind.TreeLollipop ? P.Leaves[(it.Variant + 2) % P.Leaves.Length] : leaf;
                case "LeafLight": return Color.Lerp(leaf, Color.white, 0.15f);
                case "Flower": return P.Flowers[it.Variant % P.Flowers.Length];
                case "FlowerCenter": return new Color(1f, 0.92f, 0.45f);
                case "Grass": return it.Kind == NatureKind.Reed ? Color.Lerp(P.GrassDark, P.Sand, 0.2f) : P.GrassDark;
                case "Stem": return new Color(0.96f, 0.94f, 0.88f);
                case "Cap": return P.Mushrooms[it.Variant % P.Mushrooms.Length];
                case "Rock": return Color.Lerp(P.Rock, P.RockDark, (it.Variant % 3) / 3f);
                case "RockDark": return P.RockDark;
                case "Crystal": return Color.Lerp(P.CrystalColor, Color.white, it.Kind == NatureKind.TreeCrystal ? 0.2f : 0f);
                default: return part.ModelColor; // Glow・Accent・知らない役割はモデルの色のまま
            }
        }

        private static Model Get(NatureKind kind)
        {
            if (cache.TryGetValue(kind, out var model)) return model;
            model = Load(kind);
            cache[kind] = model;
            return model;
        }

        private static Model Load(NatureKind kind)
        {
            string file = FileOf(kind);
            if (file == null) return null;
            var prefab = Resources.Load<GameObject>(ResourceFolder + file);
            if (prefab == null)
            {
                Debug.LogWarning("[NatureModels] モデルが見つかりません: " + ResourceFolder + file);
                return null;
            }
            var mf = prefab.GetComponentInChildren<MeshFilter>();
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null || mr == null || !mesh.isReadable)
            {
                Debug.LogWarning("[NatureModels] メッシュを読めません（取り込み設定の Read/Write を確認）: " + file);
                return null;
            }
            // 部品の頂点 → モデルの根元（足元・Y 上）への変換。根元の軸の回転（Blender の Z 上 → Unity の Y 上）も含む
            var local = mf.transform.localToWorldMatrix;
            var nlocal = local.inverse.transpose;
            var verts = mesh.vertices;
            var norms = mesh.normals;
            var mats = mr.sharedMaterials;
            var result = new Model();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                var src = mesh.GetTriangles(s);
                if (src.Length == 0) continue;
                // 部品ごとに使う頂点だけに詰める
                var remap = new Dictionary<int, int>();
                var v = new List<Vector3>();
                var n = new List<Vector3>();
                var t = new int[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    int k = src[i];
                    if (!remap.TryGetValue(k, out int j))
                    {
                        j = v.Count;
                        remap[k] = j;
                        v.Add(local.MultiplyPoint3x4(verts[k]));
                        n.Add(nlocal.MultiplyVector(norms[k]).normalized);
                    }
                    t[i] = j;
                }
                var mat = s < mats.Length ? mats[s] : null;
                string role = RoleOf(mat != null ? mat.name : "");
                result.Parts.Add(new Part
                {
                    Role = role,
                    V = v.ToArray(),
                    N = n.ToArray(),
                    T = t,
                    ModelColor = ModelColor(mat),
                    Glow = role == "Crystal" || role == "Glow",
                });
            }
            return result.Parts.Count > 0 ? result : null;
        }

        /// <summary>"Leaf (Instance)" や "Leaf.001" のような名前から役割名を取り出す。</summary>
        private static string RoleOf(string materialName)
        {
            string n = materialName;
            int cut = n.IndexOfAny(new[] { ' ', '.' });
            return cut > 0 ? n.Substring(0, cut) : n;
        }

        /// <summary>モデルの材質の色（Blender の FBX はリニアの色を書くので sRGB に戻す。ResidentModelBuilder と同じ）。</summary>
        private static Color ModelColor(Material mat)
        {
            if (mat == null) return Color.white;
            Color c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.color : Color.white;
            c = c.gamma;
            c.a = 1f;
            return c;
        }
    }
}
