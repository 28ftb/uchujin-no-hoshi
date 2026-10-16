using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.Visual
{
    /// <summary>
    /// Blender で作った静止モデル（Resources/Art/&lt;分類&gt;/&lt;名前&gt;.fbx：住民の家・村の小物・タブレット・ロケット）を置く。
    /// ・取り込み設定は Editor/ArtModelImporter（住民100人のモデルと同じく Y が上・正面が +Z・スケール1）。
    /// ・材質は取り込んだ色から MaterialCache の材質へ差し替える（ResidentModelBuilder と同じ。URP でもビルドで色が抜けない）。
    /// ・モデルが無ければ null を返すので、呼び出し側は従来のプリミティブで作る。
    /// 素材の集め方は Tools/VillageArt/collect_village_art.py。
    /// </summary>
    public static class ArtModels
    {
        public const string ResourceFolder = "Art";
        private static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        /// <summary>そのモデルがあるか（path は "Houses/house_01_0101" のように Art からの相対）。</summary>
        public static bool Exists(string path) => Load(path) != null;

        private static GameObject Load(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!prefabCache.TryGetValue(path, out var prefab))
            {
                prefab = Resources.Load<GameObject>(ResourceFolder + "/" + path);
                prefabCache[path] = prefab;
                if (prefab == null) Debug.LogWarning("[ArtModels] モデルが見つかりません: " + ResourceFolder + "/" + path);
            }
            return prefab;
        }

        /// <summary>parent の下にモデルを作る（parent の原点が足元、+Z が正面）。モデルが無ければ null。</summary>
        public static GameObject Build(Transform parent, string path, float scale = 1f)
        {
            var prefab = Load(path);
            if (prefab == null) return null;
            var model = Object.Instantiate(prefab, parent, false);
            model.name = "Model_" + path.Replace('/', '_');
            // 根元に Blender の Z 上 → Unity の Y 上の回転・大きさが付いているので、位置だけ0にして残す
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = prefab.transform.localRotation;
            model.transform.localScale = prefab.transform.localScale * scale;
            ResidentModelBuilder.ReplaceMaterials(model);
            return model;
        }

        /// <summary>
        /// preferredScale 倍で置き、parent のローカルで幅（x）・高さ（y）・奥行き（z）が maxSize を超える時は一様に縮める。
        /// maxSize の成分が 0 以下なら、その向きは制限しない。
        /// </summary>
        public static GameObject BuildFitted(Transform parent, string path, Vector3 maxSize, float preferredScale = 1f)
        {
            var model = Build(parent, path, preferredScale);
            if (model == null) return null;
            var size = LocalBounds(model, parent).size;
            float fit = 1f;
            for (int i = 0; i < 3; i++)
                if (maxSize[i] > 0f && size[i] > maxSize[i]) fit = Mathf.Min(fit, maxSize[i] / size[i]);
            model.transform.localScale *= fit;
            return model;
        }

        /// <summary>モデルのメッシュを space のローカル座標で囲む箱。</summary>
        public static Bounds LocalBounds(GameObject model, Transform space)
        {
            bool any = false;
            var b = new Bounds();
            var toSpace = space.worldToLocalMatrix;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = toSpace * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>モデルの中で発光している部品（夜の点灯の切り替えに使う）。</summary>
        public static Renderer[] GlowRenderers(GameObject model)
        {
            var list = new List<Renderer>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null && r.sharedMaterial.IsKeywordEnabled("_EMISSION")) list.Add(r);
            return list.ToArray();
        }

        /// <summary>名前が name で始まる子（モデルの部品）を探す。</summary>
        public static Transform FindPart(GameObject model, string name)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith(name)) return t;
            return null;
        }
    }
}
