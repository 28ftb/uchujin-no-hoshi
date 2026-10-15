using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.Visual
{
    /// <summary>
    /// コードだけで動作するプロトタイプ用のマテリアル管理。
    /// URP（Universal Render Pipeline/Lit）→ Built-in（Standard）→ Unlit の順でシェーダーを探す。
    /// 本格的なアート制作時は、ここで返すマテリアルを差し替えればよい。
    /// 注意：ビルドに含めるには Graphics Settings の Always Included Shaders への追加、または
    ///       Resources にマテリアルを置くなどの対応が必要（Unity上で要確認）。
    /// </summary>
    public static class MaterialCache
    {
        private static readonly Dictionary<int, Material> cache = new Dictionary<int, Material>();
        private static Shader litShader;
        private static Shader transparentShader;
        private static bool isUrp;

        public static Shader LitShader
        {
            get
            {
                if (litShader == null)
                {
                    litShader = Shader.Find("Universal Render Pipeline/Lit");
                    isUrp = litShader != null;
                    if (litShader == null) litShader = Shader.Find("Standard");
                    if (litShader == null) litShader = Shader.Find("Unlit/Color");
                    if (litShader == null) litShader = Shader.Find("Sprites/Default");
                }
                return litShader;
            }
        }

        public static bool IsUrp
        {
            get
            {
                var _ = LitShader;
                return isUrp;
            }
        }

        public static Material Get(Color color, float smoothness = 0.2f, bool emissive = false)
        {
            int key = ColorKey(color) ^ (emissive ? 0x5bd1e995 : 0) ^ (Mathf.RoundToInt(smoothness * 10) << 28);
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(LitShader) { name = "Mat_" + ColorUtility.ToHtmlStringRGBA(color) };
            ApplyColor(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (emissive)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * 1.6f);
            }
            if (color.a < 0.99f) MakeTransparent(m);
            cache[key] = m;
            return m;
        }

        public static Material GetEmissive(Color color) => Get(color, 0.5f, true);

        public static void ApplyColor(Material m, Color color)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        }

        private static void MakeTransparent(Material m)
        {
            if (IsUrp)
            {
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = 3000;
            }
            else
            {
                if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.renderQueue = 3000;
            }
        }

        private static int ColorKey(Color c)
        {
            Color32 c32 = c;
            return (c32.r << 24) | (c32.g << 16) | (c32.b << 8) | c32.a;
        }

        public static void Clear() => cache.Clear();
    }

    /// <summary>プリミティブ図形で簡易モデルを組み立てるヘルパー。</summary>
    public static class PrimitiveBuilder
    {
        public static GameObject Create(PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color, string name = null, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            if (!string.IsNullOrEmpty(name)) go.name = name;
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            SetColor(go, color);
            if (!keepCollider) RemoveCollider(go);
            return go;
        }

        /// <summary>parent のローカル座標で子パーツを作る（コライダーなし）。</summary>
        public static GameObject Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color, string name = null, Vector3? localEuler = null)
        {
            var go = Create(type, parent, localPos, localScale, color, name, false);
            if (localEuler.HasValue) go.transform.localEulerAngles = localEuler.Value;
            return go;
        }

        public static GameObject EmissivePart(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color, string name = null)
        {
            var go = Part(parent, type, localPos, localScale, color, name);
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(color);
            return go;
        }

        public static GameObject Empty(string name, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go;
        }

        public static void SetColor(GameObject go, Color color)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = MaterialCache.Get(color);
        }

        public static void SetColorRecursive(GameObject go, Color color)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = MaterialCache.Get(color);
        }

        public static void RemoveCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Object.Destroy(c);
        }

        public static BoxCollider AddBoxCollider(GameObject go, Vector3 center, Vector3 size, bool trigger = false)
        {
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
            bc.isTrigger = trigger;
            return bc;
        }

        /// <summary>直方体の簡易建物（屋根付き）。</summary>
        public static GameObject Building(Transform parent, Vector3 position, float rotY, Vector3 size, Color wall, Color roof, string name)
        {
            var root = Empty(name, parent, position);
            root.transform.localEulerAngles = new Vector3(0, rotY, 0);
            var body = Create(PrimitiveType.Cube, root.transform, new Vector3(0, size.y * 0.5f, 0), size, wall, "Body");
            var r = Part(root.transform, PrimitiveType.Cube, new Vector3(0, size.y + 0.4f, 0), new Vector3(size.x + 0.6f, 0.8f, size.z + 0.6f), roof, "Roof");
            Part(root.transform, PrimitiveType.Cube, new Vector3(0, 1.1f, size.z * 0.5f + 0.02f), new Vector3(1.2f, 2.2f, 0.1f), wall * 0.6f, "Door");
            return root;
        }
    }
}
