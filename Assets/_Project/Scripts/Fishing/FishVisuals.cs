using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Fishing
{
    /// <summary>魚・浮き・竿などのプリミティブ表示（アート制作時に差し替える）。</summary>
    public static class FishVisuals
    {
        /// <summary>かわいい魚の簡易モデル（楕円の体＋尾びれ＋目）。</summary>
        public static GameObject BuildFish(FishSpecies s, Transform parent, Vector3 localPos, float length)
        {
            var root = PrimitiveBuilder.Empty("Fish_" + (s != null ? s.Id : "?"), parent, localPos);
            Color c = s != null ? s.Color : Color.gray;
            float L = Mathf.Max(0.15f, length);
            var body = s != null && s.Glowing
                ? PrimitiveBuilder.EmissivePart(root.transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(L * 0.45f, L * 0.38f, L), c, "Body")
                : PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(L * 0.45f, L * 0.38f, L), c, "Body");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, 0, -L * 0.55f), new Vector3(L * 0.05f, L * 0.35f, L * 0.25f), c * 0.85f, "Tail", new Vector3(0, 0, 0));
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(L * 0.17f, L * 0.08f, L * 0.3f), Vector3.one * L * 0.09f, Color.white, "EyeL");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(-L * 0.17f, L * 0.08f, L * 0.3f), Vector3.one * L * 0.09f, Color.white, "EyeR");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(L * 0.2f, L * 0.08f, L * 0.33f), Vector3.one * L * 0.045f, Color.black, "PupilL");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(-L * 0.2f, L * 0.08f, L * 0.33f), Vector3.one * L * 0.045f, Color.black, "PupilR");
            if (s != null && s.Behavior == FishBehavior.Turner)
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, L * 0.25f, 0), new Vector3(L * 0.03f, L * 0.2f, L * 0.5f), c * 1.1f, "Fin");
            if (s != null && s.IsLegend)
                PrimitiveBuilder.EmissivePart(root.transform, PrimitiveType.Sphere, new Vector3(0, L * 0.3f, L * 0.1f), Vector3.one * L * 0.12f, Color.white, "Crown");
            return root;
        }

        public static GameObject BuildBobber(Transform parent)
        {
            var root = PrimitiveBuilder.Empty("Bobber", parent, Vector3.zero);
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(0, 0.06f, 0), new Vector3(0.22f, 0.16f, 0.22f), new Color(1f, 0.3f, 0.3f), "Top");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(0, -0.04f, 0), new Vector3(0.2f, 0.14f, 0.2f), Color.white, "Bottom");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.03f, 0.08f, 0.03f), new Color(1f, 0.9f, 0.3f), "Stick");
            return root;
        }

        public static GameObject BuildLine(Transform parent)
        {
            return PrimitiveBuilder.Part(parent, PrimitiveType.Cube, Vector3.zero, new Vector3(0.012f, 0.012f, 1f), new Color(0.95f, 0.95f, 1f, 0.8f), "FishingLine");
        }

        public static void StretchLine(GameObject line, Vector3 from, Vector3 to)
        {
            if (line == null) return;
            var d = to - from;
            float len = d.magnitude;
            if (len < 0.01f) return;
            line.transform.position = from + d * 0.5f;
            line.transform.rotation = Quaternion.LookRotation(d / len, Vector3.up);
            line.transform.localScale = new Vector3(0.012f, 0.012f, len);
        }

        public static GameObject BuildRod(Transform parent, int tier)
        {
            Color[] colors =
            {
                new Color(0.6f, 0.45f, 0.3f), new Color(0.85f, 0.5f, 0.3f), new Color(0.7f, 0.75f, 0.8f),
                new Color(0.6f, 0.9f, 1f), new Color(1f, 0.85f, 0.4f)
            };
            var root = PrimitiveBuilder.Empty("FishingRodVisual", parent, Vector3.zero);
            root.transform.localEulerAngles = new Vector3(-35f, 0f, 0f);
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cylinder, new Vector3(0, 0.75f, 0), new Vector3(0.04f, 0.8f, 0.04f), colors[Mathf.Clamp(tier - 1, 0, 4)], "Pole");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3(0.05f, 0.15f, 0), Vector3.one * 0.1f, new Color(0.3f, 0.3f, 0.35f), "Reel");
            return root;
        }
    }

    /// <summary>
    /// 釣り場の「魚影」表示（観察の手掛かり：釣り仕様 §2「魚影などを観察する」、バランス §9.1）。
    /// 目立つ魚がいる時だけ、水面下に影・霜のきらめき・輪の波紋・光が見える。何の魚かは表示しない。
    /// </summary>
    public class FishSpotView : MonoBehaviour
    {
        public string spotId;
        public string kind;       // null / "shadow" / "frost" / "ripple" / "glow"
        public Color color = Color.black;

        private GameObject shadow;
        private readonly List<Transform> rings = new List<Transform>();
        private readonly List<Transform> sparkles = new List<Transform>();
        private float angle;
        private float ringTimer;
        private float sparkleTimer;

        public void Init(string id)
        {
            spotId = id;
            shadow = PrimitiveBuilder.Part(transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.7f, 0.04f, 1.5f), new Color(0.05f, 0.08f, 0.12f, 0.55f), "Shadow");
            shadow.SetActive(false);
            angle = Random.Range(0f, 360f);
        }

        public void SetNotable(string newKind, Color c)
        {
            kind = newKind;
            color = c;
            if (shadow == null) return;
            shadow.SetActive(!string.IsNullOrEmpty(kind));
            if (string.IsNullOrEmpty(kind)) return;
            if (kind == "glow") PrimitiveBuilder.SetColor(shadow, new Color(c.r, c.g, c.b, 0.6f));
            else PrimitiveBuilder.SetColor(shadow, new Color(c.r * 0.15f, c.g * 0.15f, c.b * 0.2f, 0.55f));
            if (kind == "glow")
            {
                var r = shadow.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(new Color(c.r, c.g, c.b, 1f));
            }
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            if (string.IsNullOrEmpty(kind))
            {
                ClearFx();
                return;
            }
            angle += dt * 35f;
            var off = Quaternion.Euler(0, angle, 0) * new Vector3(0, 0, 1.2f);
            shadow.transform.localPosition = off;
            shadow.transform.localRotation = Quaternion.Euler(0, angle + 90f, 0);

            if (kind == "ripple")
            {
                ringTimer -= dt;
                if (ringTimer <= 0f)
                {
                    ringTimer = 0.9f;
                    var ring = PrimitiveBuilder.Part(transform, PrimitiveType.Cylinder, new Vector3(0, 0.16f, 0), new Vector3(0.2f, 0.005f, 0.2f), new Color(0.75f, 1f, 0.95f, 0.5f), "Ring");
                    rings.Add(ring.transform);
                }
            }
            if (kind == "frost" || kind == "glow")
            {
                sparkleTimer -= dt;
                if (sparkleTimer <= 0f)
                {
                    sparkleTimer = kind == "frost" ? 0.25f : 0.5f;
                    var p = Random.insideUnitCircle * 1.6f;
                    var sp = PrimitiveBuilder.EmissivePart(transform, PrimitiveType.Cube, new Vector3(p.x, 0.18f, p.y), Vector3.one * 0.07f,
                        kind == "frost" ? new Color(0.9f, 0.97f, 1f) : color, "Sparkle");
                    sp.transform.localEulerAngles = new Vector3(45, 45, 0);
                    sparkles.Add(sp.transform);
                }
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                if (r == null) { rings.RemoveAt(i); continue; }
                float s = r.localScale.x + dt * 1.6f;
                r.localScale = new Vector3(s, 0.005f, s);
                if (s > 3.2f)
                {
                    Destroy(r.gameObject);
                    rings.RemoveAt(i);
                }
            }
            for (int i = sparkles.Count - 1; i >= 0; i--)
            {
                var sp = sparkles[i];
                if (sp == null) { sparkles.RemoveAt(i); continue; }
                sp.localPosition += Vector3.up * dt * 0.4f;
                sp.localScale *= 1f - dt * 1.2f;
                if (sp.localScale.x < 0.015f)
                {
                    Destroy(sp.gameObject);
                    sparkles.RemoveAt(i);
                }
            }
        }

        private void ClearFx()
        {
            if (rings.Count == 0 && sparkles.Count == 0) return;
            foreach (var r in rings) if (r != null) Destroy(r.gameObject);
            foreach (var s in sparkles) if (s != null) Destroy(s.gameObject);
            rings.Clear();
            sparkles.Clear();
        }
    }

    /// <summary>しばらく表示して消える小物（釣った魚を掲げる・水しぶき）。</summary>
    public class FishingTransientFx : MonoBehaviour
    {
        public float life = 2f;
        public Vector3 velocity;
        public float spin;
        public bool gravity;

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            life -= dt;
            if (gravity) velocity += Vector3.down * 9f * dt;
            transform.position += velocity * dt;
            if (spin != 0f) transform.Rotate(0, spin * dt, 0, Space.World);
            if (life <= 0f) Destroy(gameObject);
        }

        public static void Splash(Vector3 at, int count, Color color)
        {
            var parent = Game.Instance != null ? Game.Instance.SceneRoot : null;
            for (int i = 0; i < count; i++)
            {
                var go = PrimitiveBuilder.Create(PrimitiveType.Sphere, parent, at, Vector3.one * 0.1f, color, "Splash", false);
                var fx = go.AddComponent<FishingTransientFx>();
                fx.life = 0.7f;
                fx.gravity = true;
                var r = Random.insideUnitCircle * 1.5f;
                fx.velocity = new Vector3(r.x, Random.Range(2.5f, 4f), r.y);
            }
        }
    }
}
