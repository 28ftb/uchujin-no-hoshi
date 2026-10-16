using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.Visual
{
    /// <summary>ResidentModelBuilder が組み立てたモデルの部品（表情・身振りで動かす軸）。</summary>
    public sealed class ResidentModelParts
    {
        /// <summary>FBX から作ったモデルの根元（Animation が付いている）。</summary>
        public GameObject Model;
        public Animation Animation;
        /// <summary>モデルの全高（m）。</summary>
        public float Height;
        /// <summary>頭の軸（首の位置。うなずき・首振り）。頭の骨が無いモデルは null。</summary>
        public Transform Head;
        public readonly List<Transform> ArmsLeft = new List<Transform>();
        public readonly List<Transform> ArmsRight = new List<Transform>();
        public readonly List<Transform> Legs = new List<Transform>();
        public readonly List<Transform> Ears = new List<Transform>();
        public Transform Tail;
        /// <summary>顔の部品の軸。スケール1が元の大きさで、軸はキャラクターの向き（Y が上・Z が正面）にそろえてある。</summary>
        public Transform EyeL, EyeR, Mouth, BrowL, BrowR, CheekL, CheekR;
        /// <summary>物を持つ位置（右手。無ければ null）。</summary>
        public Transform HandRight;
    }

    /// <summary>
    /// デザイン済み住民のモデル（Resources/Residents/&lt;4桁ID&gt;.fbx）を組み立てる（Residents と Housing の家の中の住民で共用）。
    /// ・FBX の部品は1本の骨に100%結び付いた硬い部品なので、SkinnedMeshRenderer を骨の子の MeshRenderer に置き換える（見た目は同じ）。
    /// ・頭・腕・脚・耳・しっぽの骨の子に「キャラクターの向きにそろえた軸」を作り、その骨の部品をその下へ移す。
    ///   骨はアクション（Idle / Signature / Sleep）が動かし、軸は表情・身振りの手続き的な動きが回す（両方が重なって動く）。
    /// ・目・口・眉・頬は部品ごとに軸を作る（まばたき・口の開き・眉の角度）。
    /// ・材質は取り込んだ色を使って MaterialCache の材質へ差し替える（URP でも Standard でも同じ見た目）。
    /// 正面は +Z、キャラクターの左は -X（Unity の座標）。
    /// </summary>
    public static class ResidentModelBuilder
    {
        public const string ResourceFolder = "Residents";
        /// <summary>
        /// 【仮数値】ゲーム内での住民モデルの大きさ（モデルの寸法に掛ける）。モデルはポルン 0.8 m を基準に作られているので、
        /// 以前のプリミティブの住民（約1.2 m）と同じくらいになるよう 1.5 倍にする。
        /// </summary>
        public const float GameScale = 1.5f;
        private static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();

        /// <summary>そのデザインのモデルがあるか。</summary>
        public static bool Exists(string designId) => LoadPrefab(designId) != null;

        private static GameObject LoadPrefab(string designId)
        {
            if (string.IsNullOrEmpty(designId)) return null;
            if (!prefabCache.TryGetValue(designId, out var prefab))
            {
                prefab = Resources.Load<GameObject>(ResourceFolder + "/" + designId);
                prefabCache[designId] = prefab;
                if (prefab == null) Debug.LogWarning("[ResidentModel] モデルが見つかりません: " + ResourceFolder + "/" + designId);
            }
            return prefab;
        }

        /// <summary>parent の下にモデルを作る（parent の原点が足元、+Z が正面）。モデルが無ければ null。</summary>
        public static ResidentModelParts Build(Transform parent, string designId)
        {
            var prefab = LoadPrefab(designId);
            if (prefab == null) return null;
            var model = Object.Instantiate(prefab, parent, false);
            model.name = "Model_" + designId;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * GameScale;

            var parts = new ResidentModelParts { Model = model, Animation = model.GetComponent<Animation>() };
            var bones = new Dictionary<string, Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            // ---- 骨ごとの軸（キャラクターの向きにそろえる） ----
            var pivotOf = new Dictionary<Transform, Transform>();
            Transform MakePivot(Transform bone, string label)
            {
                if (bone == null) return null;
                if (pivotOf.TryGetValue(bone, out var existing)) return existing;
                var p = new GameObject("Pivot_" + label).transform;
                p.SetParent(bone, false);
                p.position = bone.position;
                p.rotation = parent.rotation;
                pivotOf[bone] = p;
                return p;
            }
            parts.Head = MakePivot(Find(bones, "head"), "Head");
            foreach (var kv in bones)
            {
                string n = kv.Key;
                if (n.StartsWith("arm.L")) parts.ArmsLeft.Add(MakePivot(kv.Value, n));
                else if (n.StartsWith("arm.R")) parts.ArmsRight.Add(MakePivot(kv.Value, n));
                else if (n.StartsWith("leg.")) parts.Legs.Add(MakePivot(kv.Value, n));
                else if (n.StartsWith("ear.")) parts.Ears.Add(MakePivot(kv.Value, n));
            }
            parts.ArmsLeft.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            parts.ArmsRight.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            parts.Legs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            parts.Ears.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            parts.Tail = MakePivot(Find(bones, "tail.01"), "Tail");

            // ---- 硬い部品へ置き換え ----
            var faceGroups = new Dictionary<string, List<Transform>>();
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var part = ToRigidPart(smr, pivotOf);
                if (part == null) continue;
                string key = FaceKey(PartName(part.name));
                if (key == null) continue;
                if (!faceGroups.TryGetValue(key, out var list)) faceGroups[key] = list = new List<Transform>();
                list.Add(part);
            }

            // ---- 顔の部品の軸 ----
            parts.EyeL = FacePivot(faceGroups, "EyeL", parent);
            parts.EyeR = FacePivot(faceGroups, "EyeR", parent);
            parts.Mouth = FacePivot(faceGroups, "Mouth", parent);
            parts.BrowL = FacePivot(faceGroups, "BrowL", parent);
            parts.BrowR = FacePivot(faceGroups, "BrowR", parent);
            parts.CheekL = FacePivot(faceGroups, "CheekL", parent);
            parts.CheekR = FacePivot(faceGroups, "CheekR", parent);

            // ---- 右手（物を持つ位置） ----
            Transform rightArm = parts.ArmsRight.Count > 0 ? parts.ArmsRight[0] : null;
            if (rightArm != null)
            {
                Transform hand = null;
                foreach (Transform c in rightArm)
                {
                    string pn = PartName(c.name);
                    if (pn.StartsWith("Hand") || pn.StartsWith("Palm")) { hand = c; break; }
                }
                var hold = new GameObject("HoldPoint").transform;
                hold.SetParent(rightArm, false);
                hold.position = hand != null ? BoundsOf(hand).center : rightArm.position;
                hold.rotation = parent.rotation;
                parts.HandRight = hold;
            }

            // ---- 材質・高さ ----
            ReplaceMaterials(model);
            var all = BoundsOf(model.transform, true);
            parts.Height = Mathf.Max(0.3f, all.max.y - parent.position.y);

            if (parts.Animation != null)
            {
                parts.Animation.cullingType = AnimationCullingType.BasedOnRenderers;
                if (parts.Animation["Idle"] != null) parts.Animation.Play("Idle");
            }
            return parts;
        }

        // ------------------------------------------------------------

        private static Transform Find(Dictionary<string, Transform> bones, string name) => bones.TryGetValue(name, out var t) ? t : null;

        /// <summary>"ポルン_Eye_L" → "Eye_L"（住民名の接頭辞を外す）。</summary>
        private static string PartName(string objectName)
        {
            int i = objectName.IndexOf('_');
            return i >= 0 ? objectName.Substring(i + 1) : objectName;
        }

        private static string FaceKey(string part)
        {
            if (part == "Eye_L" || part == "EyeShine_L") return "EyeL";
            if (part == "Eye_R" || part == "EyeShine_R") return "EyeR";
            if (part == "Mouth" || part == "Smile") return "Mouth";
            if (part.EndsWith("Brow_L")) return "BrowL";
            if (part.EndsWith("Brow_R")) return "BrowR";
            if (part.EndsWith("Cheek_L") || part.StartsWith("Blush") && part.EndsWith("_L")) return "CheekL";
            if (part.EndsWith("Cheek_R") || part.StartsWith("Blush") && part.EndsWith("_R")) return "CheekR";
            return null;
        }

        /// <summary>
        /// 1本の骨だけに結び付いた SkinnedMeshRenderer を、その骨（の軸）の子の MeshRenderer に置き換える。
        /// 頂点の位置は「骨のワールド行列 × bindpose × 頂点」なので、子のローカル行列を bindpose にすれば見た目は変わらない。
        /// 複数の骨に結び付いた部品はそのまま残す。
        /// </summary>
        private static Transform ToRigidPart(SkinnedMeshRenderer smr, Dictionary<Transform, Transform> pivotOf)
        {
            var mesh = smr.sharedMesh;
            var smrBones = smr.bones;
            if (mesh == null || smrBones == null || smrBones.Length == 0) return null;
            int boneIndex = 0;
            if (mesh.isReadable)
            {
                var weights = mesh.boneWeights;
                if (weights != null && weights.Length > 0)
                {
                    boneIndex = weights[0].boneIndex0;
                    for (int i = 0; i < weights.Length; i++)
                        if (weights[i].boneIndex0 != boneIndex || weights[i].weight0 < 0.999f) return null;
                }
            }
            if (boneIndex < 0 || boneIndex >= smrBones.Length || boneIndex >= mesh.bindposes.Length) return null;
            var bone = smrBones[boneIndex];
            if (bone == null) return null;

            var go = new GameObject(smr.gameObject.name);
            go.transform.SetParent(bone, false);
            var bp = mesh.bindposes[boneIndex];
            go.transform.localPosition = bp.GetColumn(3);
            go.transform.localRotation = bp.rotation;
            go.transform.localScale = bp.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = smr.sharedMaterials;
            smr.enabled = false;
            if (Application.isPlaying) Object.Destroy(smr.gameObject);
            else Object.DestroyImmediate(smr.gameObject); // エディタの一覧撮影（再生していない時）
            if (pivotOf.TryGetValue(bone, out var pivot)) go.transform.SetParent(pivot, true);
            return go.transform;
        }

        /// <summary>
        /// 顔の部品の軸を作る。軸はキャラクターの向きにそろえ、中心に置き、大きさ（部品の高さ）を一様なスケールとして持つ。
        /// 子の部品は逆のスケールで元の大きさのまま。表情はこの軸のスケール・位置を「元の大きさに比例して」動かす。
        /// 部品が無い顔は、見えない空の軸を返す（表情の処理が null を扱わなくてよいように）。
        /// </summary>
        private static Transform FacePivot(Dictionary<string, List<Transform>> groups, string key, Transform character)
        {
            var pivot = new GameObject("Face_" + key).transform;
            if (!groups.TryGetValue(key, out var list) || list.Count == 0)
            {
                pivot.SetParent(character, false);
                pivot.localScale = Vector3.one * 0.05f;
                return pivot;
            }
            var b = BoundsOf(list[0]);
            for (int i = 1; i < list.Count; i++) b.Encapsulate(BoundsOf(list[i]));
            float size = Mathf.Max(0.01f, b.size.y);
            pivot.SetParent(list[0].parent, false);
            pivot.position = b.center;
            pivot.rotation = character.rotation;
            pivot.localScale = Vector3.one;
            foreach (var part in list) part.SetParent(pivot, true);
            // 一様なスケールを軸に持たせ、子は逆に縮めて元の大きさのまま
            pivot.localScale = Vector3.one * size;
            foreach (var part in list) part.localScale /= size;
            foreach (var part in list) part.localPosition /= size;
            return pivot;
        }

        private static Bounds BoundsOf(Transform t, bool meshRenderersOnly = false)
        {
            bool any = false;
            var b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (meshRenderersOnly && !(r is MeshRenderer) && !r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>取り込んだ材質の色（不透明度・発光を含む）から MaterialCache の材質を作って差し替える。</summary>
        private static void ReplaceMaterials(GameObject model)
        {
            var map = new Dictionary<Material, Material>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    if (!map.TryGetValue(src, out var dst))
                    {
                        Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src.HasProperty("_Color") ? src.color : Color.white;
                        // Blender の FBX はリニアの色をそのまま書き、Unity はそれを sRGB として読むので、sRGB の色に戻す
                        float alpha = c.a;
                        c = c.gamma;
                        c.a = alpha;
                        bool glow = false;
                        if (src.HasProperty("_EmissionColor"))
                        {
                            var e = src.GetColor("_EmissionColor");
                            glow = e.maxColorComponent > 0.05f;
                        }
                        dst = glow ? MaterialCache.GetEmissive(c) : MaterialCache.Get(c, 0.3f);
                        map[src] = dst;
                    }
                    mats[i] = dst;
                }
                r.sharedMaterials = mats;
            }
        }
    }
}
