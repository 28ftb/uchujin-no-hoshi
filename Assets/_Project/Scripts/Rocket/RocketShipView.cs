using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Rocket
{
    /// <summary>表示に必要な状態の要約（RocketSystem が作って RocketShipView.Apply に渡す）。</summary>
    public sealed class RocketViewModel
    {
        public const int DeviceSlots = 8;
        public bool[] Repaired = new bool[DeviceSlots];
        public bool[] Verified = new bool[DeviceSlots];
        /// <summary>納品の進み具合 0〜1（取り付け前の小さな進捗 §12.1）。</summary>
        public float[] Partial = new float[DeviceSlots];
        public bool[] CreaturePlaced = new bool[DeviceSlots];
        public int HullPlates;
        public int HullPlatesNeeded = 6;
        public float FuelRatio;
        public bool LaunchReady;
        public bool HasLaunched;
        public int RepairedCount;
        public int RepairableCount = 7;
        public bool AnyProgress;
        /// <summary>部品置き場に並ぶ（納品済みで未取り付けの）物の色。</summary>
        public readonly List<Color> StoredColors = new List<Color>();
    }

    /// <summary>
    /// 不時着したロケットと修理作業場の3D表示（プリミティブ）。ロケット仕様 §11・§12。
    /// 修理するたびに部位の見た目と機能が変わる：照明・外装・冷却液の循環・燃料ゲージ・エンジンの炎・アンテナ・位相リング。
    /// </summary>
    public sealed class RocketShipView : MonoBehaviour
    {
        // ---- 色 ----
        private static readonly Color HullClean = new Color(0.93f, 0.94f, 0.98f);
        private static readonly Color HullScorched = new Color(0.33f, 0.3f, 0.29f);
        private static readonly Color AccentClean = new Color(0.98f, 0.5f, 0.38f);
        private static readonly Color AccentScorched = new Color(0.42f, 0.27f, 0.23f);
        private static readonly Color MetalDark = new Color(0.25f, 0.26f, 0.3f);
        private static readonly Color MetalClean = new Color(0.55f, 0.62f, 0.75f);
        private static readonly Color WindowOff = new Color(0.12f, 0.14f, 0.18f);
        private static readonly Color WindowOn = new Color(1f, 0.92f, 0.55f);
        private static readonly Color PipeDull = new Color(0.45f, 0.55f, 0.6f, 0.55f);
        private static readonly Color PipeOn = new Color(0.5f, 0.95f, 1f, 0.5f);
        private static readonly Color Coolant = new Color(0.35f, 1f, 1f);
        private static readonly Color Fuel = new Color(1f, 0.72f, 0.2f);
        private static readonly Color RingDull = new Color(0.35f, 0.28f, 0.42f);
        private static readonly Color RingOn = new Color(0.78f, 0.5f, 1f);
        private static readonly Color Wood = new Color(0.6f, 0.43f, 0.28f);

        /// <summary>各装置の作業灯・演出の位置（船のローカル座標）。</summary>
        private static readonly Vector3[] DeviceAnchors =
        {
            new Vector3(0.9f, 3.2f, 1.35f),  // R01 補助電源（画面）
            new Vector3(1.4f, 5.6f, 0.6f),   // R02 船体外装
            new Vector3(1.6f, 6.8f, -0.9f),  // R03 冷却循環
            new Vector3(-2.1f, 3.7f, -0.9f), // R04 燃料ライン
            new Vector3(0f, 0.3f, 1.9f),     // R05 主エンジン
            new Vector3(0.4f, 9.4f, 0.4f),   // R06 次元観測
            new Vector3(2.15f, 4.9f, 0f),    // R07 位相安定
            new Vector3(0f, 6f, 1.6f)        // R08 最終起動
        };

        // ---- 構造 ----
        public Transform SiteRoot { get; private set; }
        public Transform ShipPivot { get; private set; }
        public Transform ShipRoot { get; private set; }
        public GameObject Workbench { get; private set; }
        public GameObject ProcessingTable { get; private set; }
        public GameObject Refinery { get; private set; }
        public GameObject Storage { get; private set; }
        public GameObject CreaturePad { get; private set; }

        private Func<float, float, float> ground;
        private readonly List<Renderer> hullRenderers = new List<Renderer>();
        private readonly List<Renderer> accentRenderers = new List<Renderer>();
        private readonly List<Renderer> windows = new List<Renderer>();
        private Renderer screenPanel;
        private readonly List<GameObject> hullPanels = new List<GameObject>();
        private readonly List<GameObject> groundPanels = new List<GameObject>();
        private readonly List<GameObject> cracks = new List<GameObject>();
        private readonly List<Renderer> pipes = new List<Renderer>();
        private Transform hangingPipe;
        private Vector3 hangingPipeFixedPos, hangingPipeBrokenPos;
        private Quaternion hangingPipeFixedRot, hangingPipeBrokenRot;
        private readonly List<Transform> coolantBlobs = new List<Transform>();
        private readonly List<Vector3> coolantPath = new List<Vector3>();
        private float coolantPathLength;
        private Transform fuelFillPivot;
        private Renderer fuelLine, fuelFill;
        private Transform nozzle;
        private Renderer nozzleRenderer, nozzleGlow;
        private GameObject flame;
        private ParticleSystem flamePs, smokePs, sparklePs;
        private Transform mastPivot, dishPivot;
        private GameObject beam;
        private Transform ringPivot;
        private readonly List<Renderer> ringSegments = new List<Renderer>();
        private Renderer statusLamp;
        private Transform hatchPivot;
        private readonly List<Renderer> workLights = new List<Renderer>();
        private Renderer refineryLight, padGlow;
        private readonly List<Renderer> storedItems = new List<Renderer>();
        private GameObject scaffold, lampPosts, bench, garland;
        private readonly List<GameObject> debris = new List<GameObject>();

        // ---- 状態 ----
        private RocketViewModel vm = new RocketViewModel();
        private float tilt = RocketTables.BaseTiltDegrees, targetTilt = RocketTables.BaseTiltDegrees;
        private float fuelShown;
        private readonly float[] testUntil = new float[RocketViewModel.DeviceSlots];
        private float ignitionUntil, shakeUntil, ringSpin, dishSpin, coolantPhase, beamTimer;
        private bool liftingOff;
        private float liftHeight;

        // ============================================================
        // 生成
        // ============================================================

        public static RocketShipView Build(Transform parent, Vector3 position, float yaw, Func<float, float, float> groundHeight, ulong seed)
        {
            var site = PrimitiveBuilder.Empty("RocketSite", parent, position);
            site.transform.position = position;
            site.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var view = site.AddComponent<RocketShipView>();
            view.ground = groundHeight ?? ((x, z) => position.y);
            view.SiteRoot = site.transform;
            view.BuildShip();
            view.BuildWorkshop(seed);
            view.Apply(new RocketViewModel());
            view.tilt = view.targetTilt;
            return view;
        }

        private GameObject P(Transform parent, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, string name, Vector3? euler = null)
            => PrimitiveBuilder.Part(parent, t, pos, scale, c, name, euler);

        /// <summary>2点の間に円柱を渡す（配管・脚・支柱）。</summary>
        private static GameObject Segment(Transform parent, Vector3 a, Vector3 b, float thickness, Color c, string name)
        {
            var go = PrimitiveBuilder.Part(parent, PrimitiveType.Cylinder, (a + b) * 0.5f, Vector3.one, c, name);
            var dir = b - a;
            float len = dir.magnitude;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
            go.transform.localScale = new Vector3(thickness, len * 0.5f, thickness);
            return go;
        }

        private void BuildShip()
        {
            ShipPivot = PrimitiveBuilder.Empty("ShipPivot", SiteRoot, Vector3.zero).transform;
            // 傾きは前後に倒れる（不時着）
            ShipRoot = PrimitiveBuilder.Empty("Ship", ShipPivot, Vector3.zero).transform;

            // 本体（コライダーあり：[E] で調べる）
            var body = PrimitiveBuilder.Create(PrimitiveType.Capsule, ShipRoot, new Vector3(0, 4.6f, 0), new Vector3(2.8f, 3.6f, 2.8f), HullScorched, "Body");
            hullRenderers.Add(body.GetComponent<Renderer>());
            var nose = P(ShipRoot, PrimitiveType.Sphere, new Vector3(0, 8.1f, 0), new Vector3(1.9f, 1.6f, 1.9f), AccentScorched, "Nose");
            accentRenderers.Add(nose.GetComponent<Renderer>());
            var belt = P(ShipRoot, PrimitiveType.Cylinder, new Vector3(0, 2.3f, 0), new Vector3(2.95f, 0.18f, 2.95f), AccentScorched, "Belt");
            accentRenderers.Add(belt.GetComponent<Renderer>());

            // ひれ・脚
            float[] finAngles = { 60f, 180f, 300f };
            foreach (var a in finAngles)
            {
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var fin = P(ShipRoot, PrimitiveType.Cube, dir * 1.65f + Vector3.up * 1.9f, new Vector3(0.22f, 2.2f, 1.3f), AccentScorched, "Fin", new Vector3(0, a + 90f, 0));
                accentRenderers.Add(fin.GetComponent<Renderer>());
                Segment(ShipRoot, dir * 1.15f + Vector3.up * 1.6f, dir * 2.2f + Vector3.up * 0.05f, 0.16f, MetalDark, "Leg");
                P(ShipRoot, PrimitiveType.Cylinder, dir * 2.2f + Vector3.up * 0.05f, new Vector3(0.5f, 0.05f, 0.5f), MetalDark, "Foot");
            }

            // 窓（R01：照明）
            float[] wh = { 5.7f, 6.5f };
            foreach (var h in wh)
            {
                var w = P(ShipRoot, PrimitiveType.Sphere, new Vector3(0, h, 1.33f), new Vector3(0.48f, 0.48f, 0.22f), WindowOff, "Window");
                windows.Add(w.GetComponent<Renderer>());
            }
            var port = P(ShipRoot, PrimitiveType.Sphere, new Vector3(0.95f, 6.1f, 0.98f), new Vector3(0.35f, 0.35f, 0.18f), WindowOff, "Porthole", new Vector3(0, 44f, 0));
            windows.Add(port.GetComponent<Renderer>());
            var screen = P(ShipRoot, PrimitiveType.Cube, new Vector3(0.9f, 3.2f, 1.1f), new Vector3(0.62f, 0.42f, 0.06f), WindowOff, "Screen", new Vector3(0, 40f, 0));
            screenPanel = screen.GetComponent<Renderer>();

            // ハッチ（帰還可能で開く）
            hatchPivot = PrimitiveBuilder.Empty("HatchPivot", ShipRoot, new Vector3(-0.5f, 2.55f, 1.41f)).transform;
            var hatch = P(hatchPivot, PrimitiveType.Cube, new Vector3(0.5f, 0f, 0f), new Vector3(1.0f, 1.5f, 0.1f), MetalDark, "Hatch");
            hullRenderers.Add(hatch.GetComponent<Renderer>());

            // 外装板（R02）：6枚。納品された分だけ仮付けされる（小さな進捗）
            float[] pa = { 30f, 150f, 270f };
            float[] ph = { 3.4f, 5.7f };
            foreach (var h in ph)
                foreach (var a in pa)
                {
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    var panel = P(ShipRoot, PrimitiveType.Cube, dir * 1.43f + Vector3.up * h, new Vector3(0.95f, 1.25f, 0.09f), HullClean, "HullPanel", new Vector3(0, a, 0));
                    hullPanels.Add(panel);
                }
            // 剥がれて地面に落ちている外装
            for (int i = 0; i < 3; i++)
            {
                var gp = P(SiteRoot, PrimitiveType.Cube, Vector3.zero, new Vector3(0.95f, 0.09f, 1.25f), HullScorched * 1.2f, "FallenPanel", new Vector3(8f * i, 37f * i + 20f, 12f - 7f * i));
                PlaceOnGround(gp.transform, new Vector3(-2.6f + i * 0.9f, 0.06f, 2.6f + i * 0.5f));
                groundPanels.Add(gp);
            }
            // 亀裂
            Vector3[] crackPos = { new Vector3(0.6f, 4.4f, 1.3f), new Vector3(-0.8f, 6.9f, 1.15f), new Vector3(1.25f, 2.9f, -0.6f), new Vector3(-1.3f, 5.0f, -0.4f) };
            foreach (var cp in crackPos)
            {
                var dir = new Vector3(cp.x, 0, cp.z).normalized;
                float a = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                var c = P(ShipRoot, PrimitiveType.Cube, cp, new Vector3(0.08f, 0.9f, 0.05f), new Color(0.08f, 0.07f, 0.07f), "Crack", new Vector3(0, a, 25f));
                cracks.Add(c);
            }

            // 冷却配管（R03）：背中側を一周する透明な管。冷却液の粒が循環する
            coolantPath.Add(new Vector3(1.35f, 1.6f, -0.85f));
            coolantPath.Add(new Vector3(1.35f, 6.6f, -0.85f));
            coolantPath.Add(new Vector3(0f, 6.6f, -1.62f));
            coolantPath.Add(new Vector3(-1.35f, 6.6f, -0.85f));
            coolantPath.Add(new Vector3(-1.35f, 1.6f, -0.85f));
            coolantPath.Add(new Vector3(0f, 1.6f, -1.62f));
            coolantPathLength = 0f;
            for (int i = 0; i < coolantPath.Count; i++)
            {
                var a = coolantPath[i];
                var b = coolantPath[(i + 1) % coolantPath.Count];
                coolantPathLength += Vector3.Distance(a, b);
                var seg = Segment(ShipRoot, a, b, 0.2f, PipeDull, "CoolantPipe");
                pipes.Add(seg.GetComponent<Renderer>());
                if (i == 1)
                {
                    hangingPipe = seg.transform;
                    hangingPipeFixedPos = seg.transform.localPosition;
                    hangingPipeFixedRot = seg.transform.localRotation;
                    hangingPipeBrokenPos = new Vector3(1.1f, 5.6f, -1.6f);
                    hangingPipeBrokenRot = Quaternion.Euler(10f, 20f, 25f);
                }
            }
            for (int i = 0; i < 7; i++)
            {
                var blob = PrimitiveBuilder.EmissivePart(ShipRoot, PrimitiveType.Sphere, coolantPath[0], Vector3.one * 0.2f, Coolant, "Coolant");
                coolantBlobs.Add(blob.transform);
            }

            // 燃料タンク・ゲージ・燃料ライン（R04）
            P(ShipRoot, PrimitiveType.Cylinder, new Vector3(-2.1f, 2.2f, -0.9f), new Vector3(0.95f, 1.3f, 0.95f), MetalClean * 0.85f, "FuelTank");
            P(ShipRoot, PrimitiveType.Cube, new Vector3(-2.1f, 2.2f, -0.4f), new Vector3(0.2f, 2.05f, 0.05f), new Color(0.08f, 0.08f, 0.1f), "Gauge");
            fuelFillPivot = PrimitiveBuilder.Empty("FuelFill", ShipRoot, new Vector3(-2.1f, 1.2f, -0.37f)).transform;
            var fill = PrimitiveBuilder.EmissivePart(fuelFillPivot, PrimitiveType.Cube, new Vector3(0, 0.5f, 0), new Vector3(0.13f, 1f, 0.04f), Fuel, "FuelFillBar");
            fuelFill = fill.GetComponent<Renderer>();
            var line = Segment(ShipRoot, new Vector3(-1.75f, 0.95f, -0.7f), new Vector3(-0.45f, 0.6f, -0.35f), 0.14f, MetalDark, "FuelLine");
            fuelLine = line.GetComponent<Renderer>();

            // 主エンジン（R05）
            nozzle = PrimitiveBuilder.Empty("NozzlePivot", ShipRoot, new Vector3(0, 0.95f, 0)).transform;
            var nz = P(nozzle, PrimitiveType.Cylinder, new Vector3(0, -0.45f, 0), new Vector3(1.75f, 0.45f, 1.75f), MetalDark, "Nozzle");
            nozzleRenderer = nz.GetComponent<Renderer>();
            var glow = P(nozzle, PrimitiveType.Cylinder, new Vector3(0, -0.9f, 0), new Vector3(1.2f, 0.03f, 1.2f), new Color(0.15f, 0.1f, 0.08f), "NozzleGlow");
            nozzleGlow = glow.GetComponent<Renderer>();
            flame = PrimitiveBuilder.EmissivePart(ShipRoot, PrimitiveType.Capsule, new Vector3(0, -0.9f, 0), new Vector3(1.0f, 1.3f, 1.0f), new Color(1f, 0.55f, 0.15f, 0.75f), "Flame");
            flame.SetActive(false);
            flamePs = CreateParticles("FlameParticles", ShipRoot, new Vector3(0, 0.1f, 0), new Vector3(90f, 0, 0), new Color(1f, 0.6f, 0.2f), 0.6f, 6f, 0.9f, 25f, 0f);
            smokePs = CreateParticles("Smoke", ShipRoot, new Vector3(0.4f, 1.2f, 0.6f), new Vector3(-90f, 0, 0), new Color(0.35f, 0.33f, 0.33f, 0.7f), 3.2f, 1.1f, 1.1f, 18f, 6f);
            sparklePs = CreateParticles("Sparkles", SiteRoot, Vector3.zero, new Vector3(-90f, 0, 0), new Color(1f, 1f, 0.7f), 1.2f, 2.5f, 0.25f, 60f, 0f);
            var sparkleMain = sparklePs.main;
            sparkleMain.gravityModifier = 0.4f;

            // 次元観測アンテナ（R06）
            mastPivot = PrimitiveBuilder.Empty("MastPivot", ShipRoot, new Vector3(0, 8.85f, 0)).transform;
            P(mastPivot, PrimitiveType.Cylinder, new Vector3(0, 0.7f, 0), new Vector3(0.09f, 0.7f, 0.09f), MetalClean, "Mast");
            dishPivot = PrimitiveBuilder.Empty("DishPivot", mastPivot, new Vector3(0, 1.45f, 0)).transform;
            P(dishPivot, PrimitiveType.Sphere, new Vector3(0, 0, 0.1f), new Vector3(1.0f, 0.18f, 1.0f), HullClean, "Dish", new Vector3(-35f, 0, 0));
            PrimitiveBuilder.EmissivePart(dishPivot, PrimitiveType.Sphere, new Vector3(0, 0.25f, 0.35f), Vector3.one * 0.14f, new Color(0.5f, 0.85f, 1f), "DishTip");
            beam = PrimitiveBuilder.EmissivePart(dishPivot, PrimitiveType.Cylinder, new Vector3(0, 8f, 0), new Vector3(0.18f, 8f, 0.18f), new Color(0.55f, 0.85f, 1f, 0.45f), "RouteBeam");
            beam.SetActive(false);

            // 位相リング（R07）
            ringPivot = PrimitiveBuilder.Empty("RingPivot", ShipRoot, new Vector3(0, 4.9f, 0)).transform;
            for (int i = 0; i < 16; i++)
            {
                float a = i * 22.5f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var seg = P(ringPivot, PrimitiveType.Cube, dir * 2.05f, new Vector3(0.78f, 0.18f, 0.24f), RingDull, "RingSeg", new Vector3(0, a, 0));
                ringSegments.Add(seg.GetComponent<Renderer>());
            }
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0, 45f + i * 90f, 0) * Vector3.forward;
                Segment(ringPivot, dir * 1.35f, dir * 1.95f, 0.08f, MetalDark, "RingStrut");
            }

            // 状態ランプ（赤点滅 → 全部直ると緑）
            statusLamp = PrimitiveBuilder.EmissivePart(ShipRoot, PrimitiveType.Sphere, new Vector3(1.2f, 7.6f, 0.75f), Vector3.one * 0.26f, Color.red, "StatusLamp").GetComponent<Renderer>();

            // 作業灯（納品が始まった装置で点滅）
            for (int i = 0; i < 7; i++)
            {
                var wl = PrimitiveBuilder.EmissivePart(ShipRoot, PrimitiveType.Sphere, DeviceAnchors[i], Vector3.one * 0.16f, new Color(1f, 0.85f, 0.3f), "WorkLight");
                wl.SetActive(false);
                workLights.Add(wl.GetComponent<Renderer>());
            }
        }

        private void PlaceOnGround(Transform t, Vector3 siteLocal)
        {
            var w = SiteRoot.TransformPoint(new Vector3(siteLocal.x, 0f, siteLocal.z));
            float h = ground(w.x, w.z);
            t.position = new Vector3(w.x, h + siteLocal.y, w.z);
        }

        private GameObject Prop(string name, Vector3 siteLocal, float localYaw)
        {
            var root = PrimitiveBuilder.Empty(name, SiteRoot, Vector3.zero);
            PlaceOnGround(root.transform, siteLocal);
            root.transform.localRotation = Quaternion.Euler(0, localYaw, 0);
            return root;
        }

        private void BuildWorkshop(ulong seed)
        {
            // 修理台（作業台）：[E] で修理画面
            Workbench = Prop("RepairBench", new Vector3(5.6f, 0, 3f), -70f);
            var wt = Workbench.transform;
            PrimitiveBuilder.Create(PrimitiveType.Cube, wt, new Vector3(0, 0.9f, 0), new Vector3(2.2f, 0.14f, 1.1f), Wood, "Top");
            for (int i = 0; i < 4; i++)
                P(wt, PrimitiveType.Cube, new Vector3(i % 2 == 0 ? -0.95f : 0.95f, 0.42f, i < 2 ? -0.42f : 0.42f), new Vector3(0.12f, 0.84f, 0.12f), Wood * 0.8f, "Leg");
            P(wt, PrimitiveType.Cube, new Vector3(-0.4f, 0.98f, 0f), new Vector3(0.9f, 0.02f, 0.6f), new Color(0.45f, 0.65f, 1f), "Blueprint");
            P(wt, PrimitiveType.Cube, new Vector3(0.6f, 1.05f, -0.2f), new Vector3(0.35f, 0.2f, 0.25f), new Color(0.8f, 0.25f, 0.2f), "Toolbox");
            P(wt, PrimitiveType.Cylinder, new Vector3(0.75f, 1.05f, 0.25f), new Vector3(0.05f, 0.18f, 0.05f), MetalClean, "Wrench", new Vector3(0, 0, 80f));

            // 加工台（料理・加工モジュールの RocketWorkshop 設備）
            ProcessingTable = Prop("ProcessingTable", new Vector3(5.6f, 0, -1.6f), -100f);
            var pt = ProcessingTable.transform;
            PrimitiveBuilder.Create(PrimitiveType.Cube, pt, new Vector3(0, 0.45f, 0), new Vector3(1.0f, 0.9f, 0.8f), MetalDark, "Base");
            P(pt, PrimitiveType.Cube, new Vector3(0, 0.98f, 0), new Vector3(1.3f, 0.16f, 0.6f), MetalClean, "Anvil");
            P(pt, PrimitiveType.Cylinder, new Vector3(0.9f, 0.6f, 0), new Vector3(0.6f, 0.6f, 0.6f), new Color(0.4f, 0.3f, 0.28f), "Furnace");
            PrimitiveBuilder.EmissivePart(pt, PrimitiveType.Sphere, new Vector3(0.9f, 0.85f, 0.28f), Vector3.one * 0.22f, new Color(1f, 0.5f, 0.15f), "FurnaceFire");

            // 燃料精製器
            Refinery = Prop("Refinery", new Vector3(-5.6f, 0, 1.8f), 80f);
            var rt = Refinery.transform;
            PrimitiveBuilder.Create(PrimitiveType.Cylinder, rt, new Vector3(0, 1.0f, 0), new Vector3(1.2f, 1.0f, 1.2f), MetalClean * 0.9f, "Vat");
            P(rt, PrimitiveType.Sphere, new Vector3(0, 2.0f, 0), new Vector3(1.2f, 0.5f, 1.2f), MetalClean, "Lid");
            P(rt, PrimitiveType.Cube, new Vector3(0, 1.0f, 0.58f), new Vector3(0.35f, 0.9f, 0.05f), Fuel * 0.6f, "Window");
            refineryLight = PrimitiveBuilder.EmissivePart(rt, PrimitiveType.Sphere, new Vector3(0, 2.35f, 0), Vector3.one * 0.22f, new Color(0.3f, 0.3f, 0.3f), "Light").GetComponent<Renderer>();
            Segment(rt, new Vector3(0.6f, 1.6f, 0), new Vector3(1.4f, 1.6f, 0.2f), 0.12f, MetalDark, "Pipe");

            // 部品置き場（納品済みの物が並ぶ）
            Storage = Prop("PartsStorage", new Vector3(-5f, 0, -3.2f), 60f);
            var st = Storage.transform;
            P(st, PrimitiveType.Cube, new Vector3(0, 0.05f, 0), new Vector3(1.8f, 0.1f, 0.7f), Wood, "Bottom");
            PrimitiveBuilder.AddBoxCollider(Storage, new Vector3(0, 0.8f, 0), new Vector3(1.9f, 1.6f, 0.75f));
            P(st, PrimitiveType.Cube, new Vector3(0, 0.75f, 0), new Vector3(1.8f, 0.07f, 0.7f), Wood, "Shelf1");
            P(st, PrimitiveType.Cube, new Vector3(0, 1.45f, 0), new Vector3(1.8f, 0.07f, 0.7f), Wood, "Shelf2");
            P(st, PrimitiveType.Cube, new Vector3(-0.88f, 0.8f, 0), new Vector3(0.07f, 1.6f, 0.7f), Wood * 0.85f, "SideL");
            P(st, PrimitiveType.Cube, new Vector3(0.88f, 0.8f, 0), new Vector3(0.07f, 1.6f, 0.7f), Wood * 0.85f, "SideR");
            P(st, PrimitiveType.Cube, new Vector3(1.4f, 0.3f, 0.3f), new Vector3(0.6f, 0.6f, 0.6f), Wood * 0.9f, "Crate");
            for (int i = 0; i < 12; i++)
            {
                int shelf = i / 6;
                int slot = i % 6;
                var item = P(st, PrimitiveType.Cube, new Vector3(-0.68f + slot * 0.27f, 0.25f + shelf * 0.7f, 0), Vector3.one * 0.2f, Color.white, "StoredItem");
                item.SetActive(false);
                storedItems.Add(item.GetComponent<Renderer>());
            }

            // 生物の作業スペース（位相安定装置などで生物が協力する）
            CreaturePad = Prop("CreaturePad", new Vector3(0f, 0, -6.2f), 0f);
            var cp = CreaturePad.transform;
            P(cp, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0), new Vector3(2.2f, 0.06f, 2.2f), new Color(0.75f, 0.72f, 0.85f), "Pad");
            // 上に乗れるよう、操作用の当たり判定はトリガーにする（円柱のカプセル判定は盛り上がってしまうため使わない）
            PrimitiveBuilder.AddBoxCollider(CreaturePad, new Vector3(0, 0.4f, 0), new Vector3(2.2f, 0.8f, 2.2f), true);
            padGlow = P(cp, PrimitiveType.Cylinder, new Vector3(0, 0.13f, 0), new Vector3(1.6f, 0.02f, 1.6f), new Color(0.3f, 0.3f, 0.35f), "PadGlow").GetComponent<Renderer>();
            for (int i = 0; i < 6; i++)
            {
                var dir = Quaternion.Euler(0, i * 60f, 0) * Vector3.forward;
                P(cp, PrimitiveType.Cylinder, dir * 1.15f + Vector3.up * 0.25f, new Vector3(0.12f, 0.25f, 0.12f), new Color(0.6f, 0.5f, 0.8f), "Post");
            }

            // 足場（何か納品が始まると現れる §11.1）
            scaffold = PrimitiveBuilder.Empty("Scaffold", SiteRoot, Vector3.zero);
            for (int i = 0; i < 4; i++)
            {
                var a = 120f + i * 40f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var pole = P(scaffold.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(0.12f, 6f, 0.12f), Wood, "Pole");
                PlaceOnGround(pole.transform, dir * 3.0f + Vector3.up * 3f);
            }
            for (int i = 0; i < 3; i++)
            {
                var a = 140f + i * 40f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                var plank = P(scaffold.transform, PrimitiveType.Cube, Vector3.zero, new Vector3(2.1f, 0.08f, 0.5f), Wood * 1.1f, "Plank");
                PlaceOnGround(plank.transform, dir * 3.0f + Vector3.up * (2.2f + (i % 2) * 2f));
                plank.transform.localRotation = Quaternion.Euler(0, a + 90f, 0);
            }

            // 街灯（最初の修理の後）
            lampPosts = PrimitiveBuilder.Empty("LampPosts", SiteRoot, Vector3.zero);
            for (int i = 0; i < 2; i++)
            {
                var post = PrimitiveBuilder.Empty("Lamp", lampPosts.transform, Vector3.zero);
                PlaceOnGround(post.transform, new Vector3(i == 0 ? 3.6f : -3.6f, 0, 5.2f));
                P(post.transform, PrimitiveType.Cylinder, new Vector3(0, 1.3f, 0), new Vector3(0.12f, 1.3f, 0.12f), MetalDark, "Pole");
                PrimitiveBuilder.EmissivePart(post.transform, PrimitiveType.Sphere, new Vector3(0, 2.75f, 0), Vector3.one * 0.38f, new Color(1f, 0.92f, 0.65f), "Bulb");
            }

            // 見物用のベンチ（修理が半分ほど進むと）
            bench = Prop("SpectatorBench", new Vector3(0, 0, 10.5f), 0f);
            P(bench.transform, PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(2.4f, 0.12f, 0.6f), Wood, "Seat");
            P(bench.transform, PrimitiveType.Cube, new Vector3(-1f, 0.2f, 0), new Vector3(0.15f, 0.4f, 0.5f), Wood * 0.8f, "LegL");
            P(bench.transform, PrimitiveType.Cube, new Vector3(1f, 0.2f, 0), new Vector3(0.15f, 0.4f, 0.5f), Wood * 0.8f, "LegR");

            // 飾り（帰還可能になったら）
            garland = PrimitiveBuilder.Empty("Garland", SiteRoot, Vector3.zero);
            Color[] gc = { new Color(1f, 0.5f, 0.5f), new Color(1f, 0.9f, 0.4f), new Color(0.5f, 0.9f, 1f), new Color(0.7f, 1f, 0.6f), new Color(0.9f, 0.6f, 1f) };
            for (int i = 0; i < 13; i++)
            {
                float t = i / 12f;
                float x = Mathf.Lerp(3.6f, -3.6f, t);
                float sag = Mathf.Sin(t * Mathf.PI) * 0.9f;
                var bulb = PrimitiveBuilder.EmissivePart(garland.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.2f, gc[i % gc.Length], "GarlandBulb");
                PlaceOnGround(bulb.transform, new Vector3(x, 2.6f - sag, 5.2f));
            }

            // 散らばった残骸（修理が進むと片付く）
            var rng = SeededRandom.For(seed, "rocket.debris");
            for (int i = 0; i < 14; i++)
            {
                float a = rng.Range(0f, 360f);
                float r = rng.Range(3.2f, 7.5f);
                var local = Quaternion.Euler(0, a, 0) * Vector3.forward * r;
                var d = P(SiteRoot, i % 3 == 0 ? PrimitiveType.Cylinder : PrimitiveType.Cube, Vector3.zero,
                    new Vector3(rng.Range(0.2f, 0.6f), rng.Range(0.1f, 0.3f), rng.Range(0.2f, 0.7f)), HullScorched * rng.Range(0.8f, 1.3f), "Debris",
                    new Vector3(rng.Range(0f, 40f), rng.Range(0f, 360f), rng.Range(0f, 40f)));
                PlaceOnGround(d.transform, local + Vector3.up * 0.08f);
                debris.Add(d);
            }
        }

        // ============================================================
        // パーティクル
        // ============================================================

        private static Material particleMaterial;

        private static Material ParticleMaterial
        {
            get
            {
                if (particleMaterial != null) return particleMaterial;
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
                if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = MaterialCache.LitShader;
                particleMaterial = new Material(sh) { name = "RocketParticle" };
                var tex = SoftCircle();
                particleMaterial.mainTexture = tex;
                if (particleMaterial.HasProperty("_BaseMap")) particleMaterial.SetTexture("_BaseMap", tex);
                if (particleMaterial.HasProperty("_Surface")) particleMaterial.SetFloat("_Surface", 1f);
                return particleMaterial;
            }
        }

        private static Texture2D SoftCircle()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01(1f - d);
                    t.SetPixel(x, y, new Color(1, 1, 1, a * a));
                }
            t.Apply();
            return t;
        }

        private static ParticleSystem CreateParticles(string name, Transform parent, Vector3 localPos, Vector3 localEuler, Color color,
            float lifetime, float speed, float size, float coneAngle, float rate)
        {
            var go = PrimitiveBuilder.Empty(name, parent, localPos);
            go.transform.localEulerAngles = localEuler;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = rate;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = coneAngle;
            sh.radius = 0.25f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.6f));
            var r = go.GetComponent<ParticleSystemRenderer>();
            if (r != null) r.sharedMaterial = ParticleMaterial;
            return ps;
        }

        // ============================================================
        // 状態の反映（冪等）
        // ============================================================

        public void Apply(RocketViewModel model)
        {
            if (model == null) return;
            vm = model;
            int repaired = model.RepairedCount;
            float frac = model.RepairableCount > 0 ? repaired / (float)model.RepairableCount : 0f;
            targetTilt = model.LaunchReady ? 0f : RocketTables.BaseTiltDegrees * (1f - frac);

            bool power = model.Repaired[0];
            bool hull = model.Repaired[1];
            bool cooling = model.Repaired[2];
            bool fuelLineOk = model.Repaired[3];
            bool engine = model.Repaired[4];
            bool antenna = model.Repaired[5];
            bool phase = model.Repaired[6];

            // 船体の汚れ（修理が進むほど明るく、外装が直るときれいに）
            float clean = Mathf.Clamp01(frac * 0.45f + (hull ? 0.55f : 0f));
            clean = Mathf.Round(clean * 10f) / 10f; // マテリアルの種類を抑える
            var hc = Color.Lerp(HullScorched, HullClean, clean);
            var ac = Color.Lerp(AccentScorched, AccentClean, clean);
            foreach (var r in hullRenderers) if (r != null) r.sharedMaterial = MaterialCache.Get(hc, 0.35f);
            foreach (var r in accentRenderers) if (r != null) r.sharedMaterial = MaterialCache.Get(ac, 0.35f);

            // R01：照明
            foreach (var w in windows) if (w != null) w.sharedMaterial = power ? MaterialCache.GetEmissive(WindowOn) : MaterialCache.Get(WindowOff, 0.8f);
            if (screenPanel != null) screenPanel.sharedMaterial = power ? MaterialCache.GetEmissive(new Color(0.4f, 1f, 0.7f)) : MaterialCache.Get(WindowOff, 0.8f);

            // R02：外装（納品された枚数だけ仮付け）
            int plates = hull ? hullPanels.Count : Mathf.Clamp(model.HullPlates, 0, hullPanels.Count);
            for (int i = 0; i < hullPanels.Count; i++)
            {
                hullPanels[i].SetActive(i < plates);
                var r = hullPanels[i].GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = MaterialCache.Get(hull ? HullClean : new Color(0.75f, 0.76f, 0.8f), 0.5f);
            }
            for (int i = 0; i < groundPanels.Count; i++) groundPanels[i].SetActive(!hull && plates < hullPanels.Count - i * 2);
            foreach (var c in cracks) c.SetActive(!hull);

            // R03：冷却配管
            foreach (var p in pipes) if (p != null) p.sharedMaterial = MaterialCache.Get(cooling ? PipeOn : PipeDull, 0.8f);
            if (hangingPipe != null)
            {
                hangingPipe.localPosition = cooling || model.Partial[2] >= 1f ? hangingPipeFixedPos : hangingPipeBrokenPos;
                hangingPipe.localRotation = cooling || model.Partial[2] >= 1f ? hangingPipeFixedRot : hangingPipeBrokenRot;
            }
            foreach (var b in coolantBlobs) b.gameObject.SetActive(cooling);

            // R04：燃料ライン
            if (fuelLine != null) fuelLine.sharedMaterial = fuelLineOk ? MaterialCache.GetEmissive(Fuel) : MaterialCache.Get(MetalDark);
            if (refineryLight != null) refineryLight.sharedMaterial = fuelLineOk ? MaterialCache.GetEmissive(Fuel) : power ? MaterialCache.GetEmissive(new Color(0.5f, 0.9f, 0.6f)) : MaterialCache.Get(new Color(0.3f, 0.3f, 0.3f));

            // R05：エンジン
            if (nozzleRenderer != null) nozzleRenderer.sharedMaterial = MaterialCache.Get(engine ? MetalClean : MetalDark, engine ? 0.7f : 0.1f);
            if (nozzleGlow != null) nozzleGlow.sharedMaterial = engine ? MaterialCache.GetEmissive(new Color(1f, 0.55f, 0.2f)) : MaterialCache.Get(new Color(0.15f, 0.1f, 0.08f));
            if (nozzle != null) nozzle.localRotation = engine ? Quaternion.identity : Quaternion.Euler(12f, 0f, 9f);
            if (smokePs != null)
            {
                var em = smokePs.emission;
                em.rateOverTime = engine ? 0f : Mathf.Lerp(7f, 2f, frac);
            }

            // R06：アンテナ
            if (mastPivot != null) mastPivot.localRotation = antenna ? Quaternion.identity : Quaternion.Euler(0f, 0f, 78f);

            // R07：位相リング
            foreach (var s in ringSegments) if (s != null) s.sharedMaterial = phase ? MaterialCache.GetEmissive(RingOn) : MaterialCache.Get(RingDull);

            // 作業灯（納品が始まったが未修理）
            for (int i = 0; i < workLights.Count; i++)
                workLights[i].gameObject.SetActive(!model.Repaired[i] && model.Partial[i] > 0f);

            // 作業場の変化（§11.1）
            if (scaffold != null) scaffold.SetActive(model.AnyProgress && !model.LaunchReady);
            if (lampPosts != null) lampPosts.SetActive(repaired >= 1);
            if (bench != null) bench.SetActive(repaired >= 3);
            if (garland != null) garland.SetActive(model.LaunchReady);
            int debrisLeft = Mathf.CeilToInt((model.RepairableCount - repaired) * 2f);
            for (int i = 0; i < debris.Count; i++) debris[i].SetActive(i < debrisLeft);

            bool anyCreature = false;
            foreach (var c in model.CreaturePlaced) anyCreature |= c;
            if (padGlow != null) padGlow.sharedMaterial = anyCreature ? MaterialCache.GetEmissive(new Color(0.75f, 0.55f, 1f)) : MaterialCache.Get(new Color(0.3f, 0.3f, 0.35f));

            for (int i = 0; i < storedItems.Count; i++)
            {
                bool on = i < model.StoredColors.Count;
                storedItems[i].gameObject.SetActive(on);
                if (on) storedItems[i].sharedMaterial = MaterialCache.Get(model.StoredColors[i]);
            }
        }

        // ============================================================
        // 演出
        // ============================================================

        public Vector3 DeviceWorldPosition(int index)
        {
            if (ShipRoot == null) return transform.position;
            index = Mathf.Clamp(index, 0, DeviceAnchors.Length - 1);
            return ShipRoot.TransformPoint(DeviceAnchors[index]);
        }

        public Vector3 SitePoint(Vector3 local)
        {
            var w = SiteRoot.TransformPoint(new Vector3(local.x, 0f, local.z));
            return new Vector3(w.x, ground(w.x, w.z) + local.y, w.z);
        }

        /// <summary>修理した瞬間のきらめき。</summary>
        public void PlayRepairBurst(int index, Color color)
        {
            if (sparklePs == null) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = DeviceWorldPosition(index),
                startColor = color,
                applyShapeToPosition = true
            };
            sparklePs.Emit(ep, 50);
            shakeUntil = Mathf.Max(shakeUntil, UnityEngine.Time.time + 0.4f);
        }

        /// <summary>納品した時の小さな手応え。</summary>
        public void PlayDeliverPop(int index, Color color)
        {
            if (sparklePs == null) return;
            var ep = new ParticleSystem.EmitParams { position = DeviceWorldPosition(index), startColor = color, applyShapeToPosition = true };
            sparklePs.Emit(ep, 12);
        }

        /// <summary>試運転の演出（装置ごと）。</summary>
        public void PlayTest(int index)
        {
            if (index < 0 || index >= testUntil.Length) return;
            float dur = index == 4 ? 4.5f : 3.5f;
            testUntil[index] = UnityEngine.Time.time + dur;
            if (index == 4) Ignite(dur);
            if (index == 5) beamTimer = 0f;
            PlayRepairBurst(index, new Color(0.7f, 1f, 1f));
        }

        public void Ignite(float seconds)
        {
            ignitionUntil = Mathf.Max(ignitionUntil, UnityEngine.Time.time + seconds);
            shakeUntil = Mathf.Max(shakeUntil, UnityEngine.Time.time + seconds);
        }

        public bool IsTesting(int index) => index >= 0 && index < testUntil.Length && testUntil[index] > UnityEngine.Time.time;

        /// <summary>簡易エンディング用：離陸して空へ上がる。</summary>
        public IEnumerator LiftOff(float seconds, float height)
        {
            liftingOff = true;
            float t = 0f;
            while (t < seconds)
            {
                t += UnityEngine.Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                liftHeight = height * k * k;
                ignitionUntil = UnityEngine.Time.time + 0.5f;
                yield return null;
            }
        }

        /// <summary>出発直前（ロケット前）の状態へ戻す。</summary>
        public void Land()
        {
            liftingOff = false;
            liftHeight = 0f;
            ignitionUntil = 0f;
            if (ShipRoot != null) ShipRoot.localPosition = Vector3.zero;
        }

        // ============================================================
        // 毎フレームの動き
        // ============================================================

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            float now = UnityEngine.Time.time;

            tilt = Mathf.MoveTowards(tilt, targetTilt, dt * 4f);
            if (ShipPivot != null) ShipPivot.localRotation = Quaternion.Euler(tilt, 0f, tilt * 0.35f);

            // 揺れ（試運転・修理）と離陸
            if (ShipRoot != null)
            {
                Vector3 shake = Vector3.zero;
                if (shakeUntil > now) shake = new Vector3(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-0.5f, 0.5f), UnityEngine.Random.Range(-1f, 1f)) * 0.04f;
                ShipRoot.localPosition = new Vector3(0f, liftHeight, 0f) + shake;
            }

            // 炎
            bool flameOn = ignitionUntil > now || liftingOff || (vm.HasLaunched && !liftingOff);
            if (flame != null)
            {
                flame.SetActive(flameOn);
                if (flameOn)
                {
                    float f = 1f + Mathf.Sin(now * 30f) * 0.12f;
                    float big = (ignitionUntil > now || liftingOff) ? 1f : 0.45f;
                    flame.transform.localScale = new Vector3(1.0f * big, 1.3f * f * big, 1.0f * big);
                }
            }
            if (flamePs != null)
            {
                var em = flamePs.emission;
                em.rateOverTime = (ignitionUntil > now || liftingOff) ? 70f : 0f;
            }

            // 状態ランプ
            if (statusLamp != null)
            {
                bool allOk = vm.LaunchReady;
                bool blink = Mathf.Repeat(now, allOk ? 3f : 1f) < (allOk ? 2.6f : 0.5f);
                statusLamp.sharedMaterial = blink ? MaterialCache.GetEmissive(allOk ? new Color(0.3f, 1f, 0.45f) : new Color(1f, 0.25f, 0.2f)) : MaterialCache.Get(new Color(0.2f, 0.12f, 0.12f));
            }

            // 窓の点滅（照明試験）
            if (IsTesting(0))
            {
                bool on = Mathf.Repeat(now, 0.3f) < 0.15f;
                foreach (var w in windows) if (w != null) w.sharedMaterial = on ? MaterialCache.GetEmissive(WindowOn) : MaterialCache.Get(WindowOff, 0.8f);
            }
            else if (vm.Repaired[0] && windows.Count > 0 && windows[0] != null && windows[0].sharedMaterial != MaterialCache.GetEmissive(WindowOn))
            {
                foreach (var w in windows) if (w != null) w.sharedMaterial = MaterialCache.GetEmissive(WindowOn);
            }

            // 冷却液の循環
            if (vm.Repaired[2] && coolantBlobs.Count > 0 && coolantPathLength > 0f)
            {
                coolantPhase += dt * (IsTesting(2) ? 3.5f : 1.1f);
                for (int i = 0; i < coolantBlobs.Count; i++)
                {
                    float d = Mathf.Repeat(coolantPhase + i * coolantPathLength / coolantBlobs.Count, coolantPathLength);
                    coolantBlobs[i].localPosition = PointOnPath(d);
                }
            }

            // 燃料ゲージ
            fuelShown = Mathf.MoveTowards(fuelShown, vm.FuelRatio, dt * 0.5f);
            if (fuelFillPivot != null) fuelFillPivot.localScale = new Vector3(1f, Mathf.Max(0.001f, 2.0f * fuelShown), 1f);
            if (fuelLine != null && IsTesting(3))
                fuelLine.sharedMaterial = Mathf.Repeat(now, 0.4f) < 0.2f ? MaterialCache.GetEmissive(Color.white) : MaterialCache.GetEmissive(Fuel);

            // アンテナ
            if (vm.Repaired[5] && dishPivot != null)
            {
                dishSpin += dt * (IsTesting(5) ? 180f : 35f);
                dishPivot.localRotation = Quaternion.Euler(0f, dishSpin, 0f);
            }
            if (beam != null)
            {
                bool showBeam = IsTesting(5) || vm.LaunchReady;
                if (!showBeam && vm.Verified[5])
                {
                    beamTimer += dt;
                    showBeam = Mathf.Repeat(beamTimer, 9f) < 1.2f;
                }
                beam.SetActive(showBeam && vm.Repaired[5]);
            }

            // 位相リング
            if (ringPivot != null)
            {
                if (vm.Repaired[6])
                {
                    ringSpin += dt * (IsTesting(6) ? 220f : 25f);
                    ringPivot.localRotation = Quaternion.Euler(0f, ringSpin, 0f);
                }
                else
                {
                    // 歪んで小刻みに揺れる
                    float j = Mathf.Sin(now * 13f) * 1.5f;
                    ringPivot.localRotation = Quaternion.Euler(16f + j, 0f, 9f - j);
                }
            }

            // 作業灯の点滅
            for (int i = 0; i < workLights.Count; i++)
            {
                if (!workLights[i].gameObject.activeSelf) continue;
                float s = Mathf.Repeat(now + i * 0.37f, 1.2f) < 0.6f ? 0.18f : 0.1f;
                workLights[i].transform.localScale = Vector3.one * s;
            }

            // ハッチ（帰還可能で開く）
            if (hatchPivot != null)
            {
                float target = vm.LaunchReady && !liftingOff ? -100f : 0f;
                var cur = hatchPivot.localEulerAngles.y;
                if (cur > 180f) cur -= 360f;
                hatchPivot.localRotation = Quaternion.Euler(0f, Mathf.MoveTowards(cur, target, dt * 90f), 0f);
            }
        }

        private Vector3 PointOnPath(float distance)
        {
            for (int i = 0; i < coolantPath.Count; i++)
            {
                var a = coolantPath[i];
                var b = coolantPath[(i + 1) % coolantPath.Count];
                float len = Vector3.Distance(a, b);
                if (distance <= len) return Vector3.Lerp(a, b, len > 0 ? distance / len : 0f);
                distance -= len;
            }
            return coolantPath.Count > 0 ? coolantPath[0] : Vector3.zero;
        }
    }
}
