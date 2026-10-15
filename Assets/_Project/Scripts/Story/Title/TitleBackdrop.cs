using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// タイトル画面の背景（3Dの簡単な宇宙：星空・輪のある星・小さなロケット・流れ星・まんまるな宇宙人）。
    /// タイトル中（とタイトルからのキャラクターメイク中）だけ表示し、ゲーム開始時に破棄する。
    /// 専用カメラを使い、MainCamera タグは付けない（星のシーンのカメラと混ざらないように）。
    /// </summary>
    public class TitleBackdrop : MonoBehaviour
    {
        private static TitleBackdrop instance;
        private static readonly Vector3 Origin = new Vector3(0f, 20000f, 0f);

        private Camera cam;
        private Transform planet;
        private Transform ring;
        private Transform rocket;
        private Transform mascot;
        private float time;
        private float shootTimer = 2f;
        private readonly List<Transform> twinkles = new List<Transform>();

        public static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("TitleBackdrop");
            DontDestroyOnLoad(go);
            go.transform.position = Origin;
            instance = go.AddComponent<TitleBackdrop>();
            instance.Build();
        }

        public static void DestroyIfAny()
        {
            if (instance == null) return;
            Destroy(instance.gameObject);
            instance = null;
        }

        private void Build()
        {
            var t = transform;
            var camGo = new GameObject("TitleCamera");
            camGo.transform.SetParent(t, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.025f, 0.07f);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            cam.depth = 20f;
            camGo.transform.localPosition = new Vector3(0, 6f, -60f);

            // 星空
            var stars = PrimitiveBuilder.Empty("Stars", t, Vector3.zero);
            for (int i = 0; i < 260; i++)
            {
                var p = Random.onUnitSphere * Random.Range(420f, 600f);
                var c = Random.value < 0.8f ? Color.white : (Random.value < 0.5f ? new Color(1f, 0.85f, 0.6f) : new Color(0.7f, 0.85f, 1f));
                var s = PrimitiveBuilder.EmissivePart(stars.transform, PrimitiveType.Sphere, p, Vector3.one * Random.Range(0.8f, 2.4f), c, "Star");
                if (i % 6 == 0) twinkles.Add(s.transform);
            }
            // 星雲（半透明の大きな球）
            for (int i = 0; i < 6; i++)
            {
                var c = Color.HSVToRGB(Random.Range(0.55f, 0.9f), 0.6f, 0.8f);
                c.a = 0.06f;
                PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(Random.Range(-260f, 260f), Random.Range(-80f, 140f), Random.Range(250f, 420f)), Vector3.one * Random.Range(120f, 220f), c, "Nebula");
            }

            // 輪のある星
            planet = PrimitiveBuilder.Empty("Planet", t, new Vector3(38f, -6f, 70f)).transform;
            PrimitiveBuilder.Part(planet, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 46f, new Color(0.55f, 0.75f, 0.95f), "Body");
            for (int i = 0; i < 8; i++)
                PrimitiveBuilder.Part(planet, PrimitiveType.Sphere, Random.onUnitSphere * 18f, new Vector3(Random.Range(10f, 18f), Random.Range(6f, 10f), Random.Range(10f, 18f)), new Color(0.6f, 0.9f, 0.6f), "Land");
            PrimitiveBuilder.Part(planet, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 50f, new Color(0.7f, 0.9f, 1f, 0.15f), "Atmosphere");
            ring = PrimitiveBuilder.Empty("RingPivot", planet, Vector3.zero).transform;
            ring.localRotation = Quaternion.Euler(18f, 0, 12f);
            PrimitiveBuilder.Part(ring, PrimitiveType.Cylinder, Vector3.zero, new Vector3(82f, 0.05f, 82f), new Color(1f, 0.85f, 0.6f, 0.35f), "Ring");
            PrimitiveBuilder.Part(ring, PrimitiveType.Cylinder, Vector3.zero, new Vector3(64f, 0.06f, 64f), new Color(0.02f, 0.025f, 0.07f, 0.9f), "RingGap");

            // 月
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-55f, 28f, 120f), Vector3.one * 12f, new Color(0.95f, 0.92f, 0.8f), "Moon");

            // 小さなロケット（旅の途中）
            var rp = RocketProxy.Build(t, Origin + new Vector3(-20f, 4f, 10f), 0f);
            rp.transform.localScale = Vector3.one * 0.6f;
            rp.transform.rotation = Quaternion.Euler(0, 0, -70f);
            rp.Ignite(0.4f);
            rocket = rp.transform;

            // 前景の小さな星と、まんまるな宇宙人
            var rock = PrimitiveBuilder.Empty("Planetoid", t, new Vector3(-16f, -6f, -30f)).transform;
            PrimitiveBuilder.Part(rock, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 7f, new Color(0.62f, 0.5f, 0.75f), "Rock");
            PrimitiveBuilder.Part(rock, PrimitiveType.Sphere, new Vector3(1.5f, 2.6f, -1.8f), Vector3.one * 1.4f, new Color(0.5f, 0.4f, 0.62f), "Crater");
            var m = NewcomerStandIn.Build(rock, Vector3.zero, new Color(1f, 0.75f, 0.82f));
            m.transform.localPosition = new Vector3(0, 3.3f, 0);
            m.transform.localRotation = Quaternion.Euler(0, 160f, 0);
            var box = m.transform.Find("Box");
            if (box != null) box.gameObject.SetActive(false);
            var tape = m.transform.Find("Tape");
            if (tape != null) tape.gameObject.SetActive(false);
            var walker = m.GetComponent<HopWalker>();
            if (walker != null) Destroy(walker);
            mascot = m.transform;

            var lightGo = PrimitiveBuilder.Empty("TitleLight", t, new Vector3(-40f, 40f, -40f));
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 400f;
            l.intensity = 1.4f;
            l.color = new Color(1f, 0.95f, 0.9f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            time += dt;
            if (cam != null)
            {
                var p = new Vector3(Mathf.Sin(time * 0.05f) * 8f, 6f + Mathf.Sin(time * 0.08f) * 2f, -60f);
                cam.transform.localPosition = p;
                cam.transform.LookAt(transform.position + new Vector3(4f, 2f, 40f));
            }
            if (planet != null) planet.Rotate(0, 3f * dt, 0, Space.Self);
            if (rocket != null)
            {
                float a = time * 0.12f;
                rocket.position = transform.position + new Vector3(Mathf.Cos(a) * 45f, 10f + Mathf.Sin(a * 2f) * 4f, 40f + Mathf.Sin(a) * 30f);
                var vel = new Vector3(-Mathf.Sin(a), Mathf.Cos(a * 2f) * 0.15f, Mathf.Cos(a)).normalized;
                rocket.rotation = Quaternion.FromToRotation(Vector3.up, vel);
            }
            if (mascot != null)
            {
                mascot.localPosition = new Vector3(0, 3.3f + Mathf.Abs(Mathf.Sin(time * 2.2f)) * 0.5f, 0);
                mascot.localRotation = Quaternion.Euler(0, 160f + Mathf.Sin(time * 0.7f) * 25f, Mathf.Sin(time * 2.2f) * 6f);
            }
            for (int i = 0; i < twinkles.Count; i++)
            {
                if (twinkles[i] == null) continue;
                float s = 1.2f + Mathf.Sin(time * (1.5f + i * 0.13f) + i) * 0.7f;
                twinkles[i].localScale = Vector3.one * s;
            }
            shootTimer -= dt;
            if (shootTimer <= 0f)
            {
                shootTimer = Random.Range(2.5f, 6f);
                StartCoroutine(ShootingStar());
            }
        }

        private System.Collections.IEnumerator ShootingStar()
        {
            var from = transform.position + new Vector3(Random.Range(-120f, 40f), Random.Range(60f, 110f), Random.Range(120f, 220f));
            var to = from + new Vector3(Random.Range(80f, 160f), Random.Range(-70f, -40f), 0);
            float t = 0f;
            float dur = Random.Range(0.8f, 1.4f);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                var p = Vector3.Lerp(from, to, t / dur);
                CutsceneFx.Particle(transform, p, new Color(0.9f, 0.95f, 1f), Random.Range(0.8f, 1.4f), Vector3.zero, 0.6f, 0f, 0.05f, true);
                yield return null;
            }
        }
    }
}
