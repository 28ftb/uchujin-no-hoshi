using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Housing
{
    /// <summary>
    /// 家の中に出す住民の簡易な姿（訪問者・住民の家の住人）。本体の住民は Residents モジュールが屋外で動かしている。
    /// 10系統の宇宙人（キャラクターデザイン案 v0.1）ごとに形と家具の使い方を変える（家具仕様 §8）。
    /// </summary>
    public class ResidentProxy : MonoBehaviour, IInteractable
    {
        public HousingSystem System;
        public string ResidentId;
        public string InvitationId;
        public AlienLineage Lineage;
        public Color MainColor = Color.white;

        /// <summary>"walk" "sit" "look" "play" "eat" "sleep" "idle" "leave" "lie"</summary>
        public string Action = "idle";
        public string TargetInstanceId;
        public Action<ResidentProxy> OnArrive;
        public bool Leaving;
        public float WalkSpeed = 1.7f;

        private readonly List<Vector3> path = new List<Vector3>();
        private float targetYaw;
        private float seatLift;
        private bool arrived = true;
        private float arriveTimeout;
        private Transform visual;
        private readonly List<Transform> arms = new List<Transform>();
        private float gestureUntil;
        private Gesture gesture;
        private float seed;

        public bool Arrived => arrived;

        public static ResidentProxy Create(Transform parent, Vector3 pos, IResidentInfo info, string residentId, HousingSystem sys)
        {
            var go = new GameObject("ResidentProxy_" + residentId);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<ResidentProxy>();
            p.System = sys;
            p.ResidentId = residentId;
            p.Lineage = info != null ? info.Lineage : AlienLineage.Round;
            p.MainColor = info != null ? info.MainColor : new Color(0.7f, 0.85f, 1f);
            p.BuildBody();
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0, 0.6f, 0);
            col.radius = 0.4f;
            col.height = 1.2f;
            col.isTrigger = true;
            p.seed = UnityEngine.Random.Range(0f, 50f);
            return p;
        }

        // ------------------------------------------------------------
        // 見た目（10系統）
        // ------------------------------------------------------------

        private void BuildBody()
        {
            visual = PrimitiveBuilder.Empty("Visual", transform, Vector3.zero).transform;
            var v = visual;
            var c = MainColor;
            var light = Color.Lerp(c, Color.white, 0.45f);
            var dark = Color.Lerp(c, Color.black, 0.3f);
            float headY = 0.75f;
            switch (Lineage)
            {
                case AlienLineage.Round:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.45f, 0), Vector3.one * 0.8f, c, "Body");
                    Feet(v, dark);
                    PrimitiveBuilder.Part(v, PrimitiveType.Cylinder, new Vector3(0, 0.95f, 0), new Vector3(0.03f, 0.1f, 0.03f), dark, "Antenna");
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 1.07f, 0), Vector3.one * 0.1f, light, "AntennaTip");
                    headY = 0.55f;
                    break;
                case AlienLineage.Mochi:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.32f, 0), new Vector3(0.9f, 0.62f, 0.85f), new Color(c.r, c.g, c.b, 0.85f), "Body");
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.15f, 0), new Vector3(0.6f, 0.2f, 0.6f), light, "Core");
                    headY = 0.4f;
                    break;
                case AlienLineage.Plush:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.45f, 0), new Vector3(0.75f, 0.85f, 0.7f), c, "Body");
                    foreach (var sx in new[] { -1f, 1f })
                        arms.Add(PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(sx * 0.22f, 0.98f, 0), new Vector3(0.2f, 0.3f, 0.15f), light, "Ear").transform);
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.25f, -0.35f), Vector3.one * 0.22f, light, "Tail");
                    Feet(v, dark);
                    headY = 0.6f;
                    break;
                case AlienLineage.ChibiAnimal:
                    PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(0, 0.38f, -0.05f), new Vector3(0.45f, 0.35f, 0.45f), c, "Body", new Vector3(90, 0, 0));
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.62f, 0.3f), Vector3.one * 0.45f, c, "Head");
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(sx * 0.13f, 0.95f, 0.26f), new Vector3(0.08f, 0.2f, 0.06f), light, "Ear");
                        foreach (var sz in new[] { -1f, 1f })
                            PrimitiveBuilder.Part(v, PrimitiveType.Cylinder, new Vector3(sx * 0.15f, 0.1f, sz * 0.2f - 0.05f), new Vector3(0.09f, 0.1f, 0.09f), dark, "Leg");
                    }
                    PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(0, 0.45f, -0.42f), new Vector3(0.07f, 0.18f, 0.07f), light, "Tail", new Vector3(-50, 0, 0));
                    headY = 0.68f;
                    break;
                case AlienLineage.Floating:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.85f, 0), Vector3.one * 0.6f, new Color(c.r, c.g, c.b, 0.85f), "Body");
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f;
                        PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(Mathf.Cos(a) * 0.18f, 0.48f, Mathf.Sin(a) * 0.18f), new Vector3(0.06f, 0.15f, 0.06f), light, "Frill");
                    }
                    PrimitiveBuilder.EmissivePart(v, PrimitiveType.Sphere, new Vector3(0, 1.18f, 0), Vector3.one * 0.08f, light, "Glow");
                    headY = 0.9f;
                    break;
                case AlienLineage.MultiArm:
                    PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(0, 0.55f, 0), new Vector3(0.6f, 0.5f, 0.55f), c, "Body");
                    for (int i = 0; i < 4; i++)
                    {
                        float sx = i % 2 == 0 ? -1f : 1f;
                        float y = i < 2 ? 0.7f : 0.45f;
                        var pivot = PrimitiveBuilder.Empty("Arm", v, new Vector3(sx * 0.3f, y, 0)).transform;
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Capsule, new Vector3(sx * 0.12f, 0, 0.02f), new Vector3(0.09f, 0.14f, 0.09f), dark, "ArmPart", new Vector3(0, 0, 90));
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(sx * 0.27f, 0, 0.03f), Vector3.one * 0.12f, light, "Hand");
                        arms.Add(pivot);
                    }
                    Feet(v, dark);
                    headY = 0.85f;
                    break;
                case AlienLineage.StarChild:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.5f, 0), Vector3.one * 0.75f, c, "Body");
                    foreach (var sx in new[] { -1f, 0f, 1f })
                    {
                        var horn = PrimitiveBuilder.EmissivePart(v, PrimitiveType.Cube, new Vector3(sx * 0.2f, 0.95f - Mathf.Abs(sx) * 0.08f, 0), Vector3.one * 0.13f, new Color(1f, 0.92f, 0.5f), "StarHorn");
                        horn.transform.localEulerAngles = new Vector3(0, 0, 45);
                    }
                    PrimitiveBuilder.Part(v, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(1.05f, 0.01f, 1.05f), light, "Ring", new Vector3(12, 0, 0));
                    Feet(v, dark);
                    headY = 0.6f;
                    break;
                case AlienLineage.Lifestyle:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.45f, 0), new Vector3(0.7f, 0.8f, 0.65f), c, "Body");
                    PrimitiveBuilder.Part(v, PrimitiveType.Cylinder, new Vector3(0, 0.92f, 0), new Vector3(0.45f, 0.03f, 0.45f), dark, "HatBrim");
                    PrimitiveBuilder.Part(v, PrimitiveType.Cylinder, new Vector3(0, 1.02f, 0), new Vector3(0.28f, 0.1f, 0.28f), dark, "HatTop");
                    PrimitiveBuilder.Part(v, PrimitiveType.Cube, new Vector3(0.36f, 0.4f, 0), new Vector3(0.12f, 0.2f, 0.25f), new Color(0.75f, 0.5f, 0.3f), "Bag");
                    PrimitiveBuilder.Part(v, PrimitiveType.Cube, new Vector3(0, 0.35f, 0.3f), new Vector3(0.4f, 0.35f, 0.04f), new Color(1f, 0.95f, 0.85f), "Apron");
                    Feet(v, dark);
                    headY = 0.6f;
                    break;
                case AlienLineage.Expressive:
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.5f, 0.45f), dark, "Body");
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.75f, 0), Vector3.one * 0.72f, c, "Head");
                    foreach (var sx in new[] { -1f, 1f })
                        arms.Add(PrimitiveBuilder.Part(v, PrimitiveType.Capsule, new Vector3(sx * 0.3f, 1.12f, 0), new Vector3(0.06f, 0.14f, 0.06f), light, "Feeler").transform);
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.62f, 0.33f), new Vector3(0.18f, 0.06f, 0.05f), new Color(0.6f, 0.25f, 0.3f), "Mouth");
                    headY = 0.82f;
                    break;
                default: // Mixed：まんまる＋多腕＋浮遊の掛け合わせ
                    PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(0, 0.62f, 0), Vector3.one * 0.7f, c, "Body");
                    foreach (var sx in new[] { -1f, 1f })
                    {
                        var pivot = PrimitiveBuilder.Empty("Arm", v, new Vector3(sx * 0.33f, 0.6f, 0)).transform;
                        PrimitiveBuilder.Part(pivot, PrimitiveType.Sphere, new Vector3(sx * 0.12f, 0, 0), Vector3.one * 0.16f, light, "Hand");
                        arms.Add(pivot);
                    }
                    PrimitiveBuilder.EmissivePart(v, PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.5f, 0.02f, 0.5f), light, "HoverRing");
                    headY = 0.7f;
                    break;
            }
            // 目
            float eyeZ = Lineage == AlienLineage.ChibiAnimal ? 0.52f : Lineage == AlienLineage.Expressive ? 0.34f : 0.36f;
            float eyeY = headY + (Lineage == AlienLineage.ChibiAnimal ? 0f : 0.05f);
            float eyeS = Lineage == AlienLineage.Expressive ? 0.14f : 0.08f;
            float zOff = Lineage == AlienLineage.ChibiAnimal ? 0f : 0f;
            foreach (var sx in new[] { -1f, 1f })
            {
                PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(sx * 0.11f, eyeY, eyeZ + zOff), Vector3.one * eyeS, Color.white, "EyeWhite");
                PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(sx * 0.11f, eyeY, eyeZ + eyeS * 0.35f), Vector3.one * eyeS * 0.55f, new Color(0.1f, 0.1f, 0.15f), "Pupil");
                PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(sx * 0.2f, eyeY - 0.08f, eyeZ - 0.02f), new Vector3(0.08f, 0.04f, 0.02f), new Color(1f, 0.6f, 0.65f), "Cheek");
            }
        }

        private static void Feet(Transform v, Color c)
        {
            foreach (var sx in new[] { -1f, 1f })
                PrimitiveBuilder.Part(v, PrimitiveType.Sphere, new Vector3(sx * 0.16f, 0.06f, 0.05f), new Vector3(0.18f, 0.12f, 0.24f), c, "Foot");
        }

        // ------------------------------------------------------------
        // 移動と使い方
        // ------------------------------------------------------------

        public void GoTo(List<Vector3> waypoints, float yaw, string action, string instanceId, float lift = 0f)
        {
            path.Clear();
            if (waypoints != null) path.AddRange(waypoints);
            targetYaw = yaw;
            Action = path.Count > 0 ? "walk" : action;
            pendingAction = action;
            TargetInstanceId = instanceId;
            seatLift = lift;
            arrived = path.Count == 0;
            arriveTimeout = UnityEngine.Time.time + 10f;
            if (arrived) FinishArrive();
        }

        private string pendingAction;

        public void Teleport(Vector3 p)
        {
            transform.position = p;
        }

        public void PlayGesture(Gesture g)
        {
            gesture = g;
            gestureUntil = UnityEngine.Time.time + 1.2f;
        }

        private void FinishArrive()
        {
            arrived = true;
            Action = pendingAction ?? "idle";
            transform.rotation = Quaternion.Euler(0, targetYaw, 0);
            var cb = OnArrive;
            OnArrive = null;
            try { cb?.Invoke(this); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            float t = UnityEngine.Time.time + seed;
            if (!arrived)
            {
                if (path.Count == 0 || UnityEngine.Time.time > arriveTimeout)
                {
                    if (path.Count > 0) transform.position = path[path.Count - 1];
                    path.Clear();
                    FinishArrive();
                }
                else
                {
                    var target = path[0];
                    var d = target - transform.position;
                    d.y = 0;
                    float step = WalkSpeed * dt;
                    if (d.magnitude <= step)
                    {
                        transform.position = new Vector3(target.x, target.y, target.z);
                        path.RemoveAt(0);
                        if (path.Count == 0) FinishArrive();
                    }
                    else
                    {
                        transform.position += d.normalized * step;
                        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d.normalized), dt * 8f);
                    }
                }
            }
            Animate(t);
        }

        private void Animate(float t)
        {
            if (visual == null) return;
            Vector3 pos = Vector3.zero;
            Vector3 euler = Vector3.zero;
            Vector3 scale = Vector3.one;
            bool floaty = Lineage == AlienLineage.Floating || Lineage == AlienLineage.Mixed;
            if (floaty) pos.y += 0.12f + Mathf.Sin(t * 2f) * 0.06f;
            switch (Action)
            {
                case "walk":
                    if (Lineage == AlienLineage.Mochi) scale = new Vector3(1f + Mathf.Sin(t * 10f) * 0.08f, 1f - Mathf.Sin(t * 10f) * 0.1f, 1f);
                    else if (!floaty) pos.y += Mathf.Abs(Mathf.Sin(t * 9f)) * 0.08f;
                    break;
                case "sit":
                    pos.y += seatLift;
                    switch (Lineage)
                    {
                        case AlienLineage.Round: euler.z = Mathf.Sin(t * 1.2f) * 14f; break; // クッションでころんと揺れる
                        case AlienLineage.Mochi: scale = new Vector3(1.15f + Mathf.Sin(t * 2f) * 0.05f, 0.8f, 1.15f); break; // 柔らかい家具と一緒に伸び縮み
                        case AlienLineage.ChibiAnimal: scale = new Vector3(1.05f, 0.7f, 0.95f); break; // 丸くなる
                        case AlienLineage.Plush: scale = new Vector3(1.05f, 0.88f, 1.05f); break;
                        case AlienLineage.Floating: pos.y += 0.2f; break; // 椅子の上で浮いてくつろぐ
                    }
                    break;
                case "sleep":
                case "lie":
                    pos.y += seatLift;
                    euler.x = -75f;
                    scale = new Vector3(1f, 1f + Mathf.Sin(t * 1.5f) * 0.03f, 1f);
                    break;
                case "play":
                    pos.y += Mathf.Abs(Mathf.Sin(t * 6f)) * 0.06f;
                    break;
                case "eat":
                    euler.x = Mathf.Sin(t * 8f) * 8f;
                    break;
                case "look":
                    euler.z = Mathf.Sin(t * 0.8f) * 6f;
                    break;
            }
            // 腕・耳（多腕は演奏で全部の手が動く）
            for (int i = 0; i < arms.Count; i++)
            {
                var a = arms[i];
                if (a == null) continue;
                float amp = Action == "play" ? 40f : Action == "walk" ? 15f : 5f;
                a.localRotation = Quaternion.Euler(Mathf.Sin(t * (Action == "play" ? 12f : 4f) + i * 1.3f) * amp, 0, 0);
            }
            if (gestureUntil > UnityEngine.Time.time)
            {
                float k = Mathf.Sin((gestureUntil - UnityEngine.Time.time) * 10f);
                switch (gesture)
                {
                    case Gesture.Jump:
                    case Gesture.Clap:
                    case Gesture.Dance:
                        pos.y += Mathf.Abs(k) * 0.25f;
                        break;
                    case Gesture.Nod:
                    case Gesture.Bow:
                    case Gesture.Eat:
                        euler.x += k * 15f;
                        break;
                    case Gesture.ShakeHead:
                        euler.y += k * 18f;
                        break;
                    case Gesture.Wave:
                        euler.z += k * 10f;
                        break;
                    case Gesture.Spin:
                        euler.y += (gestureUntil - UnityEngine.Time.time) * 300f;
                        break;
                    case Gesture.Shrug:
                        scale.y *= 1f - Mathf.Abs(k) * 0.08f;
                        break;
                }
            }
            visual.localPosition = pos;
            visual.localEulerAngles = euler;
            visual.localScale = scale;
        }

        // ------------------------------------------------------------
        // [E]：あいさつ（主人公は話さない。身振りで挨拶すると相手も返す）
        // ------------------------------------------------------------

        public string GetInteractionPrompt(PlayerState player) => Loc.T("手を振ってあいさつする", "Wave hello");
        public bool CanInteract(PlayerState player) => System != null && !Leaving;
        public void Interact(PlayerState player) => System?.GreetProxy(this, player);
    }
}
