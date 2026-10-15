using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Story
{
    /// <summary>
    /// 演出用の代役オブジェクト（プリミティブ）。ロケット本体は Rocket 担当の持ち物なので、
    /// 離陸・着陸・到着の演出ではここで作る簡易な代役を動かす（指示どおり「簡易な代役オブジェクトでも可」）。
    /// </summary>
    public class RocketProxy : MonoBehaviour
    {
        public Transform hatch;
        public FlameEmitter flame;
        public Light glow;

        public static RocketProxy Build(Transform parent, Vector3 position, float yaw)
        {
            var root = PrimitiveBuilder.Empty("RocketProxy", parent, Vector3.zero);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var t = root.transform;
            Color hull = new Color(0.93f, 0.94f, 0.97f);
            Color accent = new Color(1f, 0.55f, 0.35f);
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 3.2f, 0), new Vector3(2.6f, 2.6f, 2.6f), hull, "Body");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 5.9f, 0), new Vector3(2.2f, 0.4f, 2.2f), hull, "Neck");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 6.6f, 0), new Vector3(2.1f, 2.4f, 2.1f), accent, "Nose");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.0f, 0), new Vector3(2.7f, 0.25f, 2.7f), accent, "Band");
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f;
                var fin = PrimitiveBuilder.Part(t, PrimitiveType.Cube, Quaternion.Euler(0, a, 0) * new Vector3(0, 1.1f, 1.5f), new Vector3(0.2f, 1.8f, 1.2f), accent, "Fin");
                fin.transform.localRotation = Quaternion.Euler(0, a, 0);
            }
            PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0, 4.6f, 1.25f), new Vector3(0.8f, 0.8f, 0.3f), new Color(0.55f, 0.85f, 1f), "Window");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.25f, 0), new Vector3(1.4f, 0.3f, 1.4f), new Color(0.35f, 0.36f, 0.42f), "Nozzle");

            // ハッチ（扉）：蝶番を軸に回して閉じる
            var hinge = PrimitiveBuilder.Empty("HatchHinge", t, new Vector3(-0.55f, 2.4f, 1.32f));
            PrimitiveBuilder.Part(hinge.transform, PrimitiveType.Cube, new Vector3(0.55f, 0, 0), new Vector3(1.1f, 1.7f, 0.12f), new Color(0.8f, 0.82f, 0.88f), "Hatch");
            hinge.transform.localRotation = Quaternion.Euler(0, -100f, 0);

            var flameGo = PrimitiveBuilder.Empty("Flame", t, new Vector3(0, -0.2f, 0));
            var flame = flameGo.AddComponent<FlameEmitter>();
            flame.enabled = false;

            var lightGo = PrimitiveBuilder.Empty("FlameLight", t, new Vector3(0, -1f, 0));
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 30f;
            light.intensity = 0f;
            light.color = new Color(1f, 0.65f, 0.35f);

            var proxy = root.AddComponent<RocketProxy>();
            proxy.hatch = hinge.transform;
            proxy.flame = flame;
            proxy.glow = light;
            return proxy;
        }

        public IEnumerator CloseHatch(float seconds = 1.2f)
        {
            if (hatch == null) yield break;
            float t = 0f;
            var from = hatch.localRotation;
            var to = Quaternion.identity;
            while (t < seconds)
            {
                t += Time.deltaTime;
                if (hatch == null) yield break;
                hatch.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
        }

        public void Ignite(float power)
        {
            if (flame != null)
            {
                flame.enabled = power > 0f;
                flame.power = power;
            }
            if (glow != null) glow.intensity = power * 4f;
        }
    }

    /// <summary>
    /// エピローグで「数日後に引っ越してくる新しい住民」の代役（01 まんまるマスコット系）。
    /// 本物の住民は Residents 担当が生成するため、ここでは演出用の見た目だけを作る。
    /// </summary>
    public static class NewcomerStandIn
    {
        public static GameObject Build(Transform parent, Vector3 position, Color bodyColor)
        {
            var root = PrimitiveBuilder.Empty("NewcomerStandIn", parent, Vector3.zero);
            root.transform.position = position;
            var t = root.transform;
            var body = PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.75f, 0), new Vector3(1.1f, 1.05f, 1.0f), bodyColor, "Body");
            var dark = new Color(0.12f, 0.1f, 0.14f);
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.2f, 0.9f, 0.45f), new Vector3(0.14f, 0.2f, 0.08f), dark, "EyeL");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.2f, 0.9f, 0.45f), new Vector3(0.14f, 0.2f, 0.08f), dark, "EyeR");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.32f, 0.72f, 0.4f), new Vector3(0.16f, 0.08f, 0.06f), new Color(1f, 0.6f, 0.65f), "Cheek");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.32f, 0.72f, 0.4f), new Vector3(0.16f, 0.08f, 0.06f), new Color(1f, 0.6f, 0.65f), "Cheek");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.22f, 0.12f, 0.1f), new Vector3(0.28f, 0.2f, 0.36f), bodyColor * 0.85f, "FootL");
            PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.22f, 0.12f, 0.1f), new Vector3(0.28f, 0.2f, 0.36f), bodyColor * 0.85f, "FootR");
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.38f, 0), new Vector3(0.05f, 0.18f, 0.05f), dark, "Antenna");
            PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0, 1.6f, 0), Vector3.one * 0.16f, new Color(1f, 0.9f, 0.4f), "AntennaTip");
            // 引っ越しの荷物（箱）を抱えている
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.75f, 0.62f), new Vector3(0.7f, 0.5f, 0.45f), new Color(0.78f, 0.6f, 0.4f), "Box");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.75f, 0.85f), new Vector3(0.72f, 0.06f, 0.02f), new Color(0.9f, 0.85f, 0.7f), "Tape");
            root.AddComponent<HopWalker>();
            return root;
        }

        /// <summary>引っ越しの荷物の山。</summary>
        public static GameObject BoxPile(Transform parent, Vector3 position)
        {
            var root = PrimitiveBuilder.Empty("MovingBoxes", parent, Vector3.zero);
            root.transform.position = position;
            var c = new Color(0.78f, 0.6f, 0.4f);
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.6f), c, "Box1");
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0.9f, 0.25f, 0.1f), new Vector3(0.6f, 0.5f, 0.6f), c * 0.9f, "Box2", new Vector3(0, 15, 0));
            PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, new Vector3(0.3f, 0.82f, 0f), new Vector3(0.55f, 0.45f, 0.5f), c * 1.05f, "Box3", new Vector3(0, -10, 0));
            return root;
        }
    }

    /// <summary>代役のぴょこぴょこ歩き。target に向かって進む。</summary>
    public class HopWalker : MonoBehaviour
    {
        public Vector3? target;
        public float speed = 2.2f;
        private float phase;
        private float baseY;
        private bool init;

        private void Update()
        {
            if (!init)
            {
                init = true;
                baseY = transform.position.y;
            }
            if (!target.HasValue) return;
            var p = transform.position;
            var to = target.Value - p;
            to.y = 0;
            if (to.magnitude < 0.2f)
            {
                target = null;
                return;
            }
            var step = to.normalized * Mathf.Min(speed * Time.deltaTime, to.magnitude);
            phase += Time.deltaTime * 10f;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            float ground = layout != null ? layout.HeightAt(p + step) : baseY;
            transform.position = new Vector3(p.x + step.x, ground + Mathf.Abs(Mathf.Sin(phase)) * 0.18f, p.z + step.z);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), Time.deltaTime * 8f);
        }
    }

    /// <summary>
    /// 宇宙船の窓から故郷の星が見える場面のセット。星のワールドから遠く離れた場所に作る。
    /// </summary>
    public class CabinSet : MonoBehaviour
    {
        public Transform cameraPoint;
        public Transform lookPoint;
        public Transform homePlanet;
        public Transform avatarSeat;
        public Transform starfield;

        private static readonly Vector3 SetPosition = new Vector3(12000f, 6000f, 12000f);

        public static CabinSet Build(Transform parent, CharacterAppearance appearance)
        {
            var root = PrimitiveBuilder.Empty("CabinSet", parent, Vector3.zero);
            root.transform.position = SetPosition;
            var t = root.transform;
            var set = root.AddComponent<CabinSet>();
            Color wall = new Color(0.82f, 0.85f, 0.92f);
            Color frame = new Color(0.55f, 0.6f, 0.7f);

            // 窓枠（中央が空いている）
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 4.4f, 6f), new Vector3(10f, 2.2f, 0.4f), wall, "WallTop");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, -0.6f, 6f), new Vector3(10f, 2.6f, 0.4f), wall, "WallBottom");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-4f, 1.9f, 6f), new Vector3(2.2f, 3f, 0.4f), wall, "WallL");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(4f, 1.9f, 6f), new Vector3(2.2f, 3f, 0.4f), wall, "WallR");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 3.35f, 5.85f), new Vector3(6.2f, 0.15f, 0.2f), frame, "FrameTop");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.45f, 5.85f), new Vector3(6.2f, 0.15f, 0.2f), frame, "FrameBottom");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(-2.95f, 1.9f, 5.85f), new Vector3(0.15f, 3f, 0.2f), frame, "FrameL");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(2.95f, 1.9f, 5.85f), new Vector3(0.15f, 3f, 0.2f), frame, "FrameR");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, -1.2f, 2.5f), new Vector3(10f, 0.3f, 8f), new Color(0.45f, 0.48f, 0.56f), "Floor");
            PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, -0.2f, 3.6f), new Vector3(2.2f, 0.25f, 1.2f), new Color(0.3f, 0.45f, 0.75f), "Seat");
            PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, new Vector3(-3f, 0.25f, 5.6f), new Vector3(1.4f, 0.1f, 0.3f), new Color(0.4f, 1f, 0.7f), "Console");
            PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, new Vector3(3f, 0.25f, 5.6f), new Vector3(1.4f, 0.1f, 0.3f), new Color(1f, 0.7f, 0.4f), "Console");

            // 窓の外：星空と故郷の星
            var stars = PrimitiveBuilder.Empty("Starfield", t, new Vector3(0, 0, 0));
            for (int i = 0; i < 140; i++)
            {
                var dir = Random.onUnitSphere;
                if (dir.z < 0.15f) dir.z = Mathf.Abs(dir.z) + 0.15f;
                var p = dir.normalized * Random.Range(160f, 220f);
                PrimitiveBuilder.EmissivePart(stars.transform, PrimitiveType.Sphere, p, Vector3.one * Random.Range(0.4f, 1.1f), Random.value < 0.8f ? Color.white : new Color(1f, 0.9f, 0.6f), "Star");
            }
            var planet = PrimitiveBuilder.Empty("HomePlanet", t, new Vector3(-8f, 6f, 120f));
            PrimitiveBuilder.Part(planet.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 30f, new Color(0.3f, 0.55f, 0.95f), "Ocean");
            for (int i = 0; i < 7; i++)
                PrimitiveBuilder.Part(planet.transform, PrimitiveType.Sphere, Random.onUnitSphere * 12f, new Vector3(Random.Range(8f, 14f), Random.Range(5f, 9f), Random.Range(8f, 14f)), new Color(0.4f, 0.75f, 0.4f), "Land");
            for (int i = 0; i < 6; i++)
                PrimitiveBuilder.Part(planet.transform, PrimitiveType.Sphere, Random.onUnitSphere * 13.5f, new Vector3(Random.Range(6f, 10f), 2f, Random.Range(6f, 10f)), new Color(1f, 1f, 1f, 0.85f), "Cloud");
            PrimitiveBuilder.Part(planet.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 33f, new Color(0.6f, 0.85f, 1f, 0.18f), "Atmosphere");

            var sunGo = PrimitiveBuilder.Empty("CabinLight", t, new Vector3(0, 3.5f, 2f));
            var l = sunGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 14f;
            l.intensity = 1.2f;
            l.color = new Color(0.8f, 0.88f, 1f);

            // 主人公（後ろ姿）
            var seat = PrimitiveBuilder.Empty("AvatarSeat", t, new Vector3(0, -0.05f, 3.6f));
            seat.transform.localRotation = Quaternion.identity;
            GameObject visual = null;
            try { visual = PlayerAvatarFactory.BuildVisual(appearance, seat.transform); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (visual == null) visual = AvatarBuilder.Build(appearance, seat.transform);

            set.cameraPoint = PrimitiveBuilder.Empty("CamPoint", t, new Vector3(0.8f, 1.8f, -1.5f)).transform;
            set.lookPoint = PrimitiveBuilder.Empty("LookPoint", t, new Vector3(-0.5f, 2f, 10f)).transform;
            set.homePlanet = planet.transform;
            set.avatarSeat = seat.transform;
            set.starfield = stars.transform;
            return set;
        }

        private void Update()
        {
            if (homePlanet != null)
            {
                homePlanet.Rotate(0, 4f * Time.deltaTime, 0, Space.Self);
            }
            if (starfield != null) starfield.Rotate(0, 0, 1.5f * Time.deltaTime, Space.Self);
        }

        /// <summary>故郷の星へ近づく（大きく見えるようにする）。</summary>
        public IEnumerator Approach(float seconds)
        {
            if (homePlanet == null) yield break;
            var from = homePlanet.localPosition;
            var to = new Vector3(-2f, 1.5f, 52f);
            float t = 0f;
            while (t < seconds)
            {
                t += Time.deltaTime;
                if (homePlanet == null) yield break;
                homePlanet.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds));
                yield return null;
            }
        }
    }

    /// <summary>
    /// ロケット本体（Rocket 担当）の見た目を離陸演出の間だけ隠す。
    /// 地形・住民・主人公など大きな物や他の物は対象外にする。演出が終わったら必ず Restore する。
    /// </summary>
    public sealed class RendererHider
    {
        private readonly List<Renderer> hidden = new List<Renderer>();

        public void HideNear(Vector3 center, float radius, float maxSize, IEnumerable<Transform> exclude)
        {
            var root = Game.Instance != null ? Game.Instance.SceneRoot : null;
            if (root == null) return;
            var ex = new List<Transform>();
            if (exclude != null) foreach (var t in exclude) if (t != null) ex.Add(t);
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                var b = r.bounds;
                if (b.size.magnitude > maxSize) continue;
                var c = b.center;
                var d = c - center;
                d.y = 0;
                if (d.magnitude > radius) continue;
                if (c.y < center.y - 2f || c.y > center.y + 16f) continue;
                bool skip = false;
                foreach (var t in ex)
                {
                    if (r.transform == t || r.transform.IsChildOf(t)) { skip = true; break; }
                }
                if (skip) continue;
                r.enabled = false;
                hidden.Add(r);
            }
        }

        public void Restore()
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
        }
    }
}
