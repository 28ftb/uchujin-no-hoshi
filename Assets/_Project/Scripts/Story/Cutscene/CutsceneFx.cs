using System.Collections;
using UnityEngine;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// プリミティブだけで作る簡単な演出効果（落下の光跡・衝撃・煙・火花・炎・土煙）。
    /// 見た目だけの揺らぎなので UnityEngine.Random を使う。アート制作時にパーティクルへ差し替える想定。
    /// </summary>
    public static class CutsceneFx
    {
        public static readonly Color SmokeColor = new Color(0.55f, 0.55f, 0.6f);
        public static readonly Color DarkSmoke = new Color(0.32f, 0.32f, 0.36f);
        public static readonly Color FireCore = new Color(1f, 0.95f, 0.7f);
        public static readonly Color FireOuter = new Color(1f, 0.55f, 0.2f);

        /// <summary>寿命つきの小さな粒（移動・重力・大きさの変化）。</summary>
        public static FxParticle Particle(Transform parent, Vector3 worldPos, Color color, float size, Vector3 velocity, float life,
            float gravity = 0f, float growTo = 1f, bool emissive = false, PrimitiveType shape = PrimitiveType.Sphere)
        {
            var go = PrimitiveBuilder.Create(shape, parent, Vector3.zero, Vector3.one * size, color, "Fx", false);
            go.transform.position = worldPos;
            if (emissive)
            {
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = MaterialCache.GetEmissive(color);
            }
            var p = go.AddComponent<FxParticle>();
            p.velocity = velocity;
            p.life = life;
            p.gravity = gravity;
            p.startSize = size;
            p.endSize = size * growTo;
            return p;
        }

        /// <summary>空から落ちてくる光（流れ星のような宇宙船）。</summary>
        public static IEnumerator Meteor(Transform parent, Vector3 from, Vector3 to, float duration, CutsceneDirector director, System.Action<Vector3> onMove = null, float scale = 1f)
        {
            var head = PrimitiveBuilder.Create(PrimitiveType.Sphere, parent, Vector3.zero, Vector3.one * 2.2f, FireCore, "MeteorHead", false);
            head.transform.position = from;
            var hr = head.GetComponent<Renderer>();
            if (hr != null) hr.sharedMaterial = MaterialCache.GetEmissive(FireCore);
            var lightGo = new GameObject("MeteorLight");
            lightGo.transform.SetParent(head.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 60f;
            light.intensity = 3f;
            light.color = new Color(1f, 0.75f, 0.45f);

            float t = 0f;
            float spawn = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = k * k; // だんだん速く
                var pos = Vector3.Lerp(from, to, eased) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 8f;
                if (head == null) yield break;
                head.transform.position = pos;
                head.transform.localScale = Vector3.one * Mathf.Lerp(2.2f, 3.4f, k) * scale;
                onMove?.Invoke(pos);
                spawn -= Time.deltaTime;
                if (spawn <= 0f)
                {
                    spawn = 0.025f;
                    Particle(parent, pos + Random.insideUnitSphere * 0.6f, Random.value < 0.5f ? FireOuter : FireCore, Random.Range(0.9f, 1.6f), Vector3.zero, 0.7f, 0f, 0.05f, true);
                    if (Random.value < 0.5f)
                        Particle(parent, pos + Random.insideUnitSphere, SmokeColor, Random.Range(1.2f, 2f), Random.insideUnitSphere * 0.5f + Vector3.up * 0.5f, 2.2f, 0f, 2.2f);
                }
                if (director != null && director.SkipRequested) break;
                yield return null;
            }
            if (head != null) Object.Destroy(head);
        }

        /// <summary>着地の衝撃：煙・火花・焦げ跡。長く残る煙（SmokeEmitter）を返す。</summary>
        public static SmokeEmitter Impact(Transform parent, Vector3 ground, float lingerSeconds = 90f)
        {
            for (int i = 0; i < 18; i++)
            {
                var dir = Random.insideUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.6f + 0.2f;
                Particle(parent, ground + Vector3.up * 0.5f, i % 3 == 0 ? DarkSmoke : SmokeColor, Random.Range(1.5f, 2.6f), dir * Random.Range(3f, 7f), Random.Range(2.5f, 4f), -0.5f, 2.8f);
            }
            for (int i = 0; i < 26; i++)
            {
                var dir = Random.insideUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.4f;
                Particle(parent, ground + Vector3.up * 0.6f, Random.value < 0.5f ? FireOuter : FireCore, Random.Range(0.12f, 0.25f), dir * Random.Range(6f, 12f), Random.Range(0.8f, 1.6f), 12f, 0.2f, true, PrimitiveType.Cube);
            }
            // 焦げ跡（しばらく残る）
            var scorch = PrimitiveBuilder.Create(PrimitiveType.Cylinder, parent, Vector3.zero, new Vector3(9f, 0.03f, 9f), new Color(0.18f, 0.16f, 0.15f), "Scorch", false);
            scorch.transform.position = ground + Vector3.up * 0.04f;
            Object.Destroy(scorch, lingerSeconds * 2f);

            var emitterGo = new GameObject("CrashSmoke");
            emitterGo.transform.SetParent(parent, false);
            emitterGo.transform.position = ground + Vector3.up * 1.2f;
            var em = emitterGo.AddComponent<SmokeEmitter>();
            em.lifetime = lingerSeconds;
            return em;
        }

        /// <summary>着陸の土煙（輪）。</summary>
        public static void DustRing(Transform parent, Vector3 ground, int count = 16)
        {
            for (int i = 0; i < count; i++)
            {
                float a = i / (float)count * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0.1f, Mathf.Sin(a));
                Particle(parent, ground + Vector3.up * 0.3f, new Color(0.75f, 0.7f, 0.62f), Random.Range(1f, 1.6f), dir * Random.Range(3f, 5f), 2.5f, 0f, 2.4f);
            }
        }
    }

    /// <summary>寿命つきの粒。</summary>
    public class FxParticle : MonoBehaviour
    {
        public Vector3 velocity;
        public float life = 1f;
        public float gravity;
        public float startSize = 1f;
        public float endSize = 1f;
        public float drag = 0.6f;
        private float age;

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            if (age >= life)
            {
                Destroy(gameObject);
                return;
            }
            velocity += Vector3.down * gravity * dt;
            velocity *= Mathf.Exp(-drag * dt);
            transform.position += velocity * dt;
            float k = age / life;
            // 膨らんでから消える
            float size = Mathf.Lerp(startSize, endSize, Mathf.Sqrt(k)) * (k > 0.75f ? 1f - (k - 0.75f) / 0.25f : 1f);
            transform.localScale = Vector3.one * Mathf.Max(0.001f, size);
        }
    }

    /// <summary>しばらく立ちのぼる煙（不時着の跡）。</summary>
    public class SmokeEmitter : MonoBehaviour
    {
        public float lifetime = 60f;
        public float interval = 0.35f;
        public float strength = 1f;
        private float age;
        private float timer;

        private void Update()
        {
            age += Time.deltaTime;
            if (age > lifetime)
            {
                Destroy(gameObject);
                return;
            }
            float fade = 1f - age / lifetime;
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = interval / Mathf.Max(0.2f, fade);
            var p = transform.position + new Vector3(Random.Range(-1.5f, 1.5f), 0, Random.Range(-1.5f, 1.5f));
            CutsceneFx.Particle(transform.parent, p, Random.value < 0.4f ? CutsceneFx.DarkSmoke : CutsceneFx.SmokeColor,
                Random.Range(0.8f, 1.4f) * strength * (0.5f + fade * 0.5f), new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(1.2f, 2.2f), Random.Range(-0.3f, 0.3f)), 4f, -0.1f, 3f);
        }
    }

    /// <summary>ロケットの炎（ちらつく光る粒）。</summary>
    public class FlameEmitter : MonoBehaviour
    {
        public float rate = 0.02f;
        public float power = 1f;
        private float timer;

        private void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = rate;
            var down = -transform.up;
            CutsceneFx.Particle(transform.parent != null ? transform.parent.parent : null, transform.position + Random.insideUnitSphere * 0.3f,
                Random.value < 0.5f ? CutsceneFx.FireCore : CutsceneFx.FireOuter, Random.Range(0.5f, 0.9f) * power,
                down * Random.Range(8f, 14f) * power + Random.insideUnitSphere, 0.45f, 0f, 0.2f, true);
            if (Random.value < 0.35f)
                CutsceneFx.Particle(transform.parent != null ? transform.parent.parent : null, transform.position + down * 1.5f,
                    CutsceneFx.SmokeColor, Random.Range(1f, 1.6f) * power, down * 3f + Random.insideUnitSphere * 2f, 2.5f, 0f, 3f);
        }
    }
}
