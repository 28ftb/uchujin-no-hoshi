using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Farming
{
    /// <summary>
    /// 農場の見た目（プリミティブ）：区画の柵、畑のマス（段階ごとに変わる作物）、住み場所、収穫かご・倉庫・種箱、看板、種屋の棚。
    /// RestoreState(Planet) から呼ばれても冪等になるよう、マスは「見た目の署名」が変わった時だけ作り直す。
    /// </summary>
    public sealed partial class FarmingSystem
    {
        private Transform farmRoot;
        private Transform plotRoot;
        private Transform habitatRoot;
        private readonly Dictionary<int, GameObject> plotViews = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, string> plotSignatures = new Dictionary<int, string>();
        private bool lastNight;

        public Vector3 WarehousePosition() => OutsidePoint(0.5f);
        public Vector3 BasketPosition() => OutsidePoint(2.2f);
        public Vector3 SeedBoxPosition() => OutsidePoint(3.6f);
        public Vector3 SignPosition() => OutsidePoint(5f);

        /// <summary>区画の西側の外（倉庫・かご・種箱・看板を並べる）。</summary>
        private Vector3 OutsidePoint(float along)
        {
            var b = UnlockedBounds();
            var p = new Vector3(farmRect.xMin + b.xMin - 1.6f, 0, farmRect.yMin + b.yMin + along);
            var layout = Game != null ? Game.Layout : null;
            if (layout != null) p.y = layout.HeightAt(p.x, p.z);
            return p;
        }

        private void BuildFarmScene()
        {
            if (Game == null || Game.SceneRoot == null) return;
            DestroyFarmScene();
            farmRoot = PrimitiveBuilder.Empty("Farm", Game.SceneRoot, Vector3.zero).transform;
            plotRoot = PrimitiveBuilder.Empty("Plots", farmRoot, Vector3.zero).transform;
            habitatRoot = PrimitiveBuilder.Empty("Habitats", farmRoot, Vector3.zero).transform;
            BuildFence();
            BuildFixtures();
            BuildSeedShopCounter();
            BuildHabitats();
            RefreshPlotViews();
        }

        private void DestroyFarmScene()
        {
            if (farmRoot != null) Object.Destroy(farmRoot.gameObject);
            farmRoot = null;
            plotRoot = null;
            habitatRoot = null;
            plotViews.Clear();
            plotSignatures.Clear();
        }

        public void RebuildFarmScene()
        {
            if (!sceneBuilt) return;
            BuildFarmScene();
        }

        private void BuildFence()
        {
            var b = UnlockedBounds();
            var fence = PrimitiveBuilder.Empty("Fence", farmRoot, Vector3.zero).transform;
            var wood = new Color(0.62f, 0.46f, 0.3f);
            float x0 = farmRect.xMin + b.xMin - 0.1f, x1 = farmRect.xMin + b.xMax + 0.1f;
            float z0 = farmRect.yMin + b.yMin - 0.1f, z1 = farmRect.yMin + b.yMax + 0.1f;
            var layout = Game.Layout;
            System.Func<float, float, float> h = (x, z) => layout != null ? layout.HeightAt(x, z) : 0f;
            for (float x = x0; x <= x1 + 0.01f; x += 2f)
            {
                PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x, h(x, z0) + 0.3f, z0), new Vector3(0.12f, 0.6f, 0.12f), wood, "Post", false);
                PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x, h(x, z1) + 0.3f, z1), new Vector3(0.12f, 0.6f, 0.12f), wood, "Post", false);
            }
            for (float z = z0; z <= z1 + 0.01f; z += 2f)
            {
                PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x0, h(x0, z) + 0.3f, z), new Vector3(0.12f, 0.6f, 0.12f), wood, "Post", false);
                PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x1, h(x1, z) + 0.3f, z), new Vector3(0.12f, 0.6f, 0.12f), wood, "Post", false);
            }
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(cx, h(cx, z0) + 0.42f, z0), new Vector3(x1 - x0, 0.06f, 0.05f), wood, "Rail", false);
            PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(cx, h(cx, z1) + 0.42f, z1), new Vector3(x1 - x0, 0.06f, 0.05f), wood, "Rail", false);
            PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x1, h(x1, cz) + 0.42f, cz), new Vector3(0.05f, 0.06f, z1 - z0), wood, "Rail", false);
            // 西側は倉庫などへの出入り口として低い横木だけ
            PrimitiveBuilder.Create(PrimitiveType.Cube, fence, new Vector3(x0, h(x0, cz) + 0.12f, cz), new Vector3(0.05f, 0.04f, z1 - z0), wood, "Rail", false);
        }

        private void BuildFixtures()
        {
            // 倉庫（Carry の生き物が運び込む）
            var wp = WarehousePosition();
            var shed = PrimitiveBuilder.Building(farmRoot, wp + new Vector3(-0.8f, 0, 0), 90f, new Vector3(1.6f, 1.4f, 1.4f), new Color(0.75f, 0.6f, 0.42f), new Color(0.6f, 0.3f, 0.25f), "FarmWarehouse");
            SimpleInteractable.Attach(shed, "倉庫を開ける", "Open storage", p => UIManager.Open(new FarmStorageWindow(this, p, 1)));
            // 収穫かご
            var bp = BasketPosition();
            var basketGo = PrimitiveBuilder.Create(PrimitiveType.Cylinder, farmRoot, bp + Vector3.up * 0.25f, new Vector3(0.8f, 0.25f, 0.8f), new Color(0.8f, 0.6f, 0.35f), "HarvestBasket", false);
            PrimitiveBuilder.Part(basketGo.transform, PrimitiveType.Sphere, new Vector3(0.1f, 0.9f, 0f), new Vector3(0.35f, 0.6f, 0.35f), new Color(1f, 0.5f, 0.35f), "Fruit");
            PrimitiveBuilder.Part(basketGo.transform, PrimitiveType.Sphere, new Vector3(-0.15f, 0.85f, 0.12f), new Vector3(0.3f, 0.5f, 0.3f), new Color(0.6f, 0.85f, 0.4f), "Fruit");
            PrimitiveBuilder.AddBoxCollider(basketGo, Vector3.zero, new Vector3(1.4f, 3f, 1.4f), true);
            SimpleInteractable.Attach(basketGo, "収穫かごを見る", "Check harvest basket", p => UIManager.Open(new FarmStorageWindow(this, p, 0)));
            // 種箱（Sow の生き物がここから種を取る）
            var sp = SeedBoxPosition();
            var seedGo = PrimitiveBuilder.Create(PrimitiveType.Cube, farmRoot, sp + Vector3.up * 0.25f, new Vector3(0.7f, 0.5f, 0.5f), new Color(0.55f, 0.75f, 0.45f), "SeedBox", false);
            PrimitiveBuilder.Part(seedGo.transform, PrimitiveType.Cube, new Vector3(0, 0.55f, 0), new Vector3(1.05f, 0.1f, 1.05f), new Color(0.4f, 0.55f, 0.3f), "Lid");
            PrimitiveBuilder.AddBoxCollider(seedGo, Vector3.zero, new Vector3(1.2f, 2f, 1.4f), true);
            SimpleInteractable.Attach(seedGo, "種箱を見る", "Check seed box", p => UIManager.Open(new FarmStorageWindow(this, p, 2)));
            // 看板
            var sg = SignPosition();
            var sign = PrimitiveBuilder.Empty("FarmSign", farmRoot, sg);
            sign.transform.position = sg;
            PrimitiveBuilder.Part(sign.transform, PrimitiveType.Cube, new Vector3(0, 0.6f, 0), new Vector3(0.1f, 1.2f, 0.1f), new Color(0.55f, 0.4f, 0.28f), "Post");
            PrimitiveBuilder.Part(sign.transform, PrimitiveType.Cube, new Vector3(0, 1.1f, 0), new Vector3(0.08f, 0.5f, 0.9f), new Color(0.85f, 0.75f, 0.55f), "Board");
            PrimitiveBuilder.Part(sign.transform, PrimitiveType.Sphere, new Vector3(0.06f, 1.15f, 0f), new Vector3(0.05f, 0.22f, 0.22f), new Color(0.4f, 0.75f, 0.35f), "Sprout");
            PrimitiveBuilder.AddBoxCollider(sign, new Vector3(0, 0.8f, 0), new Vector3(0.8f, 1.6f, 1.2f), true);
            SimpleInteractable.Attach(sign, "農場の看板", "Farm sign", p => OpenFarmApp());
        }

        public void OpenFarmApp()
        {
            var tablet = Game.Service<ITabletService>();
            if (tablet != null) tablet.Open("farm");
            else UIManager.Open(new FarmAppWindow(this));
        }

        private void BuildSeedShopCounter()
        {
            var layout = Game.Layout;
            if (layout == null) return;
            var f = layout.GetFacility(FacilityType.SeedShop);
            if (f == null) return;
            var rot = Quaternion.Euler(0, f.rotationY, 0);
            var pos = f.entrance + rot * new Vector3(1.8f, 0, 0.6f);
            pos.y = layout.HeightAt(pos.x, pos.z);
            var counter = PrimitiveBuilder.Empty("SeedShopCounter", farmRoot, pos);
            counter.transform.position = pos;
            counter.transform.rotation = rot;
            PrimitiveBuilder.Part(counter.transform, PrimitiveType.Cube, new Vector3(0, 0.45f, 0), new Vector3(1.6f, 0.9f, 0.6f), new Color(0.7f, 0.52f, 0.34f), "Counter");
            var crops = FarmCatalog.Crops;
            for (int i = 0; i < 6 && i < crops.Count; i++)
                PrimitiveBuilder.Part(counter.transform, PrimitiveType.Cube, new Vector3(-0.6f + i * 0.24f, 1.0f, 0), new Vector3(0.18f, 0.22f, 0.08f), crops[i].Color, "SeedBag");
            PrimitiveBuilder.AddBoxCollider(counter, new Vector3(0, 0.6f, 0), new Vector3(1.8f, 1.4f, 1.2f), true);
            var si = counter.AddComponent<SeedShopCounter>();
            si.System = this;
        }

        private void BuildHabitats()
        {
            if (habitatRoot == null) return;
            foreach (Transform c in habitatRoot) Object.Destroy(c.gameObject);
            foreach (var h in habitats)
            {
                var def = FarmCatalog.Habitat(h.itemId);
                var pos = CellCenter(h.x, h.z);
                var go = PrimitiveBuilder.Empty("Habitat_" + h.id, habitatRoot, pos);
                go.transform.position = pos;
                BuildHabitatModel(go.transform, h.itemId, def != null ? def.Color : Color.white);
                PrimitiveBuilder.AddBoxCollider(go, new Vector3(0, 0.4f, 0), new Vector3(0.9f, 0.8f, 0.9f), true);
                var hi = go.AddComponent<FarmHabitatInteractable>();
                hi.System = this;
                hi.HabitatId = h.id;
            }
        }

        private static void BuildHabitatModel(Transform t, string itemId, Color c)
        {
            switch (itemId)
            {
                case "farmhab.pond":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(0.95f, 0.02f, 0.95f), new Color(0.55f, 0.5f, 0.45f), "Rim");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.04f, 0), new Vector3(0.8f, 0.02f, 0.8f), c, "Water");
                    break;
                case "farmhab.burrow":
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.05f, 0), new Vector3(0.8f, 0.35f, 0.8f), c, "Mound");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.18f, 0.12f, 0.25f), new Vector3(0.22f, 0.18f, 0.1f), new Color(0.15f, 0.1f, 0.08f), "Hole");
                    break;
                case "farmhab.perch":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.6f, 0), new Vector3(0.08f, 0.6f, 0.08f), c, "Pole");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 1.15f, 0), new Vector3(0.05f, 0.35f, 0.05f), c, "Bar", new Vector3(0, 0, 90));
                    break;
                case "farmhab.fruit_basket":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.15f, 0), new Vector3(0.6f, 0.15f, 0.6f), c, "Basket");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.1f, 0.32f, 0), Vector3.one * 0.18f, new Color(1f, 0.45f, 0.35f), "Fruit");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.1f, 0.3f, 0.08f), Vector3.one * 0.16f, new Color(1f, 0.8f, 0.3f), "Fruit");
                    break;
                case "farmhab.flower_bed":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.1f, 0), new Vector3(0.9f, 0.2f, 0.9f), new Color(0.55f, 0.4f, 0.28f), "Bed");
                    for (int i = 0; i < 5; i++)
                        PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.3f + i * 0.15f, 0.32f, (i % 2) * 0.2f - 0.1f), Vector3.one * 0.14f, Color.HSVToRGB(i * 0.18f, 0.5f, 1f), "Flower");
                    break;
                case "farmhab.night_pot":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.2f, 0), new Vector3(0.4f, 0.2f, 0.4f), new Color(0.6f, 0.45f, 0.35f), "Pot");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0, 0.5f, 0), Vector3.one * 0.18f, c, "Glow");
                    PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0.12f, 0.45f, 0.05f), Vector3.one * 0.1f, c, "Glow");
                    break;
                case "farmhab.hive_box":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.4f, 0), new Vector3(0.06f, 0.4f, 0.06f), new Color(0.5f, 0.38f, 0.25f), "Pole");
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.95f, 0), new Vector3(0.5f, 0.4f, 0.4f), c, "Box");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.95f, 0.2f), new Vector3(0.12f, 0.12f, 0.04f), new Color(0.2f, 0.15f, 0.1f), "Hole");
                    break;
                case "farmhab.warm_bed":
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.08f, 0), new Vector3(0.8f, 0.18f, 0.8f), c, "Bed");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.14f, 0), new Vector3(0.5f, 0.1f, 0.5f), new Color(1f, 0.85f, 0.7f), "Cushion");
                    break;
                case "farmhab.cool_stone":
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.2f, 0), new Vector3(0.6f, 0.4f, 0.5f), c, "Stone", new Vector3(5, 25, 8));
                    break;
                default:
                    PrimitiveBuilder.Building(t, Vector3.zero, 0f, new Vector3(0.9f, 0.6f, 0.8f), c, new Color(0.7f, 0.4f, 0.3f), "Hut");
                    break;
            }
        }

        // ---------------- 畑のマス ----------------

        private string Signature(FarmPlot p, bool night)
        {
            if (p == null) return "";
            var def = FarmCatalog.Get(p.cropKey);
            int stage = def != null ? def.StageOf(p.growth, p.ripe) : -1;
            bool glow = def != null && def.GlowsAtNight && night;
            return (p.tilled ? "T" : "-") + (p.wasFarmed ? "R" : "-") + (p.watered ? "W" : "-") + (p.fertilizer != null ? "F" : "-") + Mathf.FloorToInt(p.soil)
                   + "|" + (p.cropKey ?? "") + "|" + stage + (glow ? "g" : "") + (p.litDay >= 0 && night ? "l" : "");
        }

        public void RefreshPlotViews()
        {
            if (!sceneBuilt || plotRoot == null) return;
            bool night = Clock != null && Clock.IsNight;
            lastNight = night;
            var remove = new List<int>();
            foreach (var k in plotViews.Keys) if (!plots.ContainsKey(k)) remove.Add(k);
            foreach (var k in remove)
            {
                if (plotViews[k] != null) Object.Destroy(plotViews[k]);
                plotViews.Remove(k);
                plotSignatures.Remove(k);
            }
            foreach (var p in plots.Values)
            {
                string sig = Signature(p, night);
                if (plotSignatures.TryGetValue(p.Key, out var old) && old == sig && plotViews.TryGetValue(p.Key, out var existing) && existing != null) continue;
                if (plotViews.TryGetValue(p.Key, out var go) && go != null) Object.Destroy(go);
                plotViews[p.Key] = BuildPlotView(p, night);
                plotSignatures[p.Key] = sig;
            }
        }

        private void UpdateNightGlowVisuals()
        {
            bool night = Clock != null && Clock.IsNight;
            if (night != lastNight) RefreshPlotViews();
        }

        private GameObject BuildPlotView(FarmPlot p, bool night)
        {
            var pos = CellCenter(p.x, p.z);
            var root = PrimitiveBuilder.Empty("Plot_" + p.x + "_" + p.z, plotRoot, pos);
            root.transform.position = pos;
            var t = root.transform;
            if (p.tilled || p.HasCrop)
            {
                float rich = Mathf.Clamp01(p.soil / 3f);
                Color dry = Color.Lerp(new Color(0.58f, 0.43f, 0.28f), new Color(0.46f, 0.32f, 0.2f), rich);
                Color wet = Color.Lerp(new Color(0.34f, 0.24f, 0.15f), new Color(0.25f, 0.17f, 0.1f), rich);
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.04f, 0), new Vector3(0.92f, 0.08f, 0.92f), p.watered ? wet : dry, "Soil");
                for (int i = -1; i <= 1; i += 2)
                    PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.085f, i * 0.22f), new Vector3(0.86f, 0.02f, 0.08f), (p.watered ? wet : dry) * 0.85f, "Furrow");
                if (p.fertilizer != null)
                {
                    var f = FarmCatalog.Fertilizer(p.fertilizer);
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.3f, 0.09f, 0.3f), new Vector3(0.12f, 0.03f, 0.12f), f != null ? f.Color : Color.gray, "Fert");
                }
            }
            else if (p.wasFarmed)
            {
                PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, 0.02f, 0), new Vector3(0.8f, 0.04f, 0.8f), new Color(0.52f, 0.44f, 0.32f), "Rough");
            }
            if (p.HasCrop)
            {
                var def = FarmCatalog.Get(p.cropKey);
                if (def != null) BuildCropModel(t, def, def.StageOf(p.growth, p.ripe), night);
                PrimitiveBuilder.AddBoxCollider(root, new Vector3(0, 0.35f, 0), new Vector3(0.85f, 0.7f, 0.85f), true);
                var pi = root.AddComponent<FarmPlotInteractable>();
                pi.System = this;
                pi.X = p.x;
                pi.Z = p.z;
            }
            return root;
        }

        private static void BuildCropModel(Transform t, CropDef def, int stage, bool night)
        {
            Color leaf = def.Leaf;
            switch (stage)
            {
                case 0:
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.1f, 0), Vector3.one * 0.08f, new Color(0.5f, 0.38f, 0.25f), "Seed");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.13f, 0), new Vector3(0.06f, 0.04f, 0.06f), leaf, "Bud");
                    return;
                case 1:
                    PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.16f, 0), new Vector3(0.03f, 0.08f, 0.03f), leaf, "Stem");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.06f, 0.24f, 0), new Vector3(0.12f, 0.03f, 0.07f), leaf, "Leaf", new Vector3(0, 0, 20));
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(-0.06f, 0.24f, 0), new Vector3(0.12f, 0.03f, 0.07f), leaf, "Leaf", new Vector3(0, 0, -20));
                    return;
            }
            bool tree = def.Kind == CropKind.FruitTree;
            float h = tree ? (stage == 3 ? 1.2f : 0.8f) : stage == 3 ? 0.45f : 0.32f;
            PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.08f + h * 0.5f, 0), new Vector3(tree ? 0.1f : 0.04f, h * 0.5f, tree ? 0.1f : 0.04f), tree ? new Color(0.5f, 0.36f, 0.24f) : leaf * 0.85f, "Stem");
            if (tree) PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, h + 0.15f, 0), new Vector3(0.75f, 0.55f, 0.75f), leaf, "Canopy");
            else
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 120f;
                    var dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, dir * 0.12f + Vector3.up * (0.12f + h * 0.4f), new Vector3(0.2f, 0.04f, 0.12f), leaf, "Leaf", new Vector3(0, a, 15));
                }
            if (stage < 3) return;
            bool glow = def.GlowsAtNight && night;
            if (def.Kind == CropKind.Flower)
            {
                var head = PrimitiveBuilder.Empty("Flower", t, new Vector3(0, h + 0.12f, 0)).transform;
                for (int i = 0; i < 5; i++)
                {
                    var dir = Quaternion.Euler(0, i * 72f, 0) * Vector3.forward;
                    PrimitiveBuilder.Part(head, PrimitiveType.Sphere, dir * 0.09f, new Vector3(0.12f, 0.04f, 0.12f), def.Color, "Petal");
                }
                PrimitiveBuilder.Part(head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.08f, new Color(1f, 0.9f, 0.4f), "Center");
                return;
            }
            bool root = def.FruitShape == PrimitiveType.Capsule && def.Kind == CropKind.Vegetable;
            if (root)
            {
                // 根菜：土から頭が見えている
                PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.12f, 0), new Vector3(0.22f, 0.12f, 0.22f), def.Color, "Root");
                if (glow) PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, new Vector3(0, 0.14f, 0), Vector3.one * 0.12f, def.Color, "Glow");
                return;
            }
            float fy = def.FloatsWhenRipe ? h + 0.35f : tree ? h - 0.05f : h * 0.7f + 0.1f;
            float spread = tree ? 0.32f : 0.14f;
            for (int i = 0; i < 3; i++)
            {
                var dir = Quaternion.Euler(0, i * 120f + 30f, 0) * Vector3.forward;
                var pos = dir * spread + Vector3.up * (fy + (i == 1 ? 0.06f : 0f));
                var scale = def.FruitShape == PrimitiveType.Capsule ? new Vector3(0.08f, 0.12f, 0.08f) : Vector3.one * (def.Kind == CropKind.Grain ? 0.08f : 0.13f);
                if (glow) PrimitiveBuilder.EmissivePart(t, def.FruitShape, pos, scale, def.Color, "Fruit");
                else PrimitiveBuilder.Part(t, def.FruitShape, pos, scale, def.Color, "Fruit");
            }
            if (def.FloatsWhenRipe) t.gameObject.AddComponent<FloatBob>();
        }
    }

    /// <summary>浮く作物のふわふわ（見た目だけ）。</summary>
    public class FloatBob : MonoBehaviour
    {
        private readonly List<Transform> fruits = new List<Transform>();
        private readonly List<Vector3> bases = new List<Vector3>();
        private float phase;

        private void Start()
        {
            foreach (Transform c in transform)
            {
                if (c.name != "Fruit") continue;
                fruits.Add(c);
                bases.Add(c.localPosition);
            }
            phase = Random.Range(0f, 6f);
        }

        private void Update()
        {
            float t = Time.time + phase;
            for (int i = 0; i < fruits.Count; i++)
                if (fruits[i] != null) fruits[i].localPosition = bases[i] + Vector3.up * Mathf.Sin(t * 1.8f + i) * 0.04f;
        }
    }
}
