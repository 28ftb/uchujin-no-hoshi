#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Uchujin.Core.Visual;
using Uchujin.Story;
using Uchujin.Tablet;
using Uchujin.World;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// 村の新しいモデル（Resources/Art）の確認用：ゲームと同じ置き方で並べ、Logs/VillageArtGallery/ に撮影し、report.txt に検査結果を書く。
    /// ・住民の家：HouseBuilder.BuildModel（区画 7×6 m）で100軒。系統ごとに1枚、住民本人（1.5倍）を扉の横に立たせる。
    /// ・村の小物：街灯・ベンチ・看板・矢印板・花壇・広場の目印は 1.5 倍、橋は川幅 7 m・幅 3.4 m に伸ばした形。
    /// ・ロケット：RocketProxy（タイトル＝修理前、エンディング＝修理後）。
    /// ・タブレット：主人公（キャラメイクの初期の見た目）の右手に TabletHeldView.Attach で持たせた形。
    /// メニュー「宇宙人の星 → 村の新しいモデルを一覧撮影・検査」、またはバッチモード（-nographics なし）で
    /// -executeMethod Uchujin.EditorTools.VillageArtGallery.Capture
    /// </summary>
    public static class VillageArtGallery
    {
        private static readonly Vector3 PlotSize = new Vector3(7f, 4f, 6f); // WorldGenerator.Village の SizeOf(ResidentHouse)
        private const float HouseSpacing = 9f;

        [MenuItem("宇宙人の星/村の新しいモデルを一覧撮影・検査", priority = 73)]
        public static void Capture()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "VillageArtGallery");
            Directory.CreateDirectory(dir);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var root = new GameObject("VillageArtGallery");
            var report = new StringBuilder();
            int problems = 0;
            try
            {
                Lights(root.transform);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(root.transform, false);
                ground.transform.localScale = new Vector3(60f, 1f, 60f);
                ground.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(new Color(0.78f, 0.9f, 0.76f));
                Shoot(root, Path.Combine(dir, "_warmup.png"), new Vector3(0f, 5f, 20f), Vector3.zero);
                File.Delete(Path.Combine(dir, "_warmup.png"));

                // ---- 住民の家（系統ごとに10軒） ----
                for (int family = 1; family <= 10; family++)
                {
                    var row = new GameObject("Houses" + family);
                    row.transform.SetParent(root.transform, false);
                    for (int i = 1; i <= 10; i++)
                    {
                        string design = $"{family:00}{i:00}";
                        var plot = new GameObject(design).transform;
                        plot.SetParent(row.transform, false);
                        plot.localPosition = new Vector3((i - 5.5f) * HouseSpacing, 0f, 0f);
                        if (!HouseBuilder.BuildModel(plot, design, PlotSize, out var size))
                        {
                            problems++;
                            report.AppendLine(design + " NG: 家のモデルがない");
                            continue;
                        }
                        var model = plot.Find("HouseModel");
                        var b = ArtModels.LocalBounds(model.gameObject, plot);
                        var issues = new List<string>();
                        if (b.size.x > PlotSize.x * 1.1f + 0.01f || b.size.z > PlotSize.z * 1.1f + 0.01f) issues.Add("区画からはみ出す");
                        if (Mathf.Abs(b.max.z - PlotSize.z * 0.5f) > 0.01f) issues.Add("正面が区画の線にそろっていない");
                        if (b.min.y < -0.12f) issues.Add("地面より下に出ている"); // 05 の浮く家は杭を 0.1 m 埋めている
                        issues.AddRange(MaterialIssues(model.gameObject));
                        problems += issues.Count;
                        float scale = model.GetChild(0).localScale.x;
                        report.AppendLine(design + (issues.Count == 0 ? " OK" : " NG: " + string.Join(" / ", issues)) +
                                          $"  scale={scale:F2} size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})");
                        // 住民本人を扉の横（正面の少し前）に立たせる
                        var who = new GameObject("Resident").transform;
                        who.SetParent(plot, false);
                        who.localPosition = new Vector3(1.6f, 0f, PlotSize.z * 0.5f + 0.9f);
                        ResidentModelBuilder.Build(who, design);
                    }
                }
                var houseRows = new List<GameObject>();
                foreach (Transform c in root.transform) if (c.name.StartsWith("Houses")) houseRows.Add(c.gameObject);
                for (int k = 0; k < houseRows.Count; k++)
                {
                    for (int j = 0; j < houseRows.Count; j++) houseRows[j].SetActive(j == k);
                    Shoot(root, Path.Combine(dir, $"houses_{k + 1:00}.png"), new Vector3(-6f, 16f, 52f), new Vector3(0f, 1.5f, 0f), 2400, 800, 46f);
                    Shoot(root, Path.Combine(dir, $"houses_{k + 1:00}_close.png"), new Vector3(-30.5f, 3.6f, 13f), new Vector3(-40.5f, 1.6f, 1f), 1200, 800, 45f);
                }
                foreach (var r in houseRows) r.SetActive(false);

                // ---- 村の小物 ----
                var props = new GameObject("Props").transform;
                props.SetParent(root.transform, false);
                float s = StructureBuilder.VillageModelScale;
                Place(props, "Village/lamp", new Vector3(-9f, 0f, 0f), s, ref problems, report);
                Place(props, "Village/bench", new Vector3(-6f, 0f, 0f), s, ref problems, report);
                Place(props, "Village/sign", new Vector3(-3f, 0f, 0f), s, ref problems, report);
                var arrow = Place(props, "Village/sign_arrow", new Vector3(-1f, 2f, 0f), 1f, ref problems, report);
                if (arrow != null)
                {
                    arrow.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
                    arrow.transform.localScale = new Vector3(3.6f, 2.2f, 2.2f);
                }
                Place(props, "Village/flowerbed", new Vector3(1.5f, 0f, 0f), s, ref problems, report);
                Place(props, "Village/plaza", new Vector3(7f, 0f, -1f), s, ref problems, report);
                var bridge = new GameObject("Bridge").transform;
                bridge.SetParent(props, false);
                bridge.localPosition = new Vector3(15f, 0.35f, 0f);
                bridge.localRotation = Quaternion.Euler(0f, 90f, 0f);
                var pod = Place(bridge, "Village/bridge", Vector3.zero, 1f, ref problems, report);
                if (pod != null) pod.transform.localScale = new Vector3(7f / 3.76f, 1f, 3.4f / 1.24f);
                var scaleRes = new GameObject("Resident").transform;
                scaleRes.SetParent(props, false);
                scaleRes.localPosition = new Vector3(-4.5f, 0f, 1.6f);
                ResidentModelBuilder.Build(scaleRes, "0101");
                Shoot(root, Path.Combine(dir, "props.png"), new Vector3(2f, 7f, 20f), new Vector3(3f, 1.2f, 0f), 2400, 1000, 50f);
                props.gameObject.SetActive(false);

                // ---- ロケット（修理前・修理後） ----
                var rockets = new GameObject("Rockets").transform;
                rockets.SetParent(root.transform, false);
                var before = RocketProxy.Build(rockets, new Vector3(-4f, 0f, 0f), 0f, repaired: false);
                var after = RocketProxy.Build(rockets, new Vector3(4f, 0f, 0f), 0f, repaired: true);
                after.Ignite(1f);
                foreach (var rp in new[] { before, after })
                {
                    var model = ArtModels.FindPart(rp.gameObject, "Model_Rocket");
                    if (model == null) { problems++; report.AppendLine(rp.name + " NG: ロケットのモデルがない"); continue; }
                    var b = ArtModels.LocalBounds(model.gameObject, rp.transform);
                    report.AppendLine($"{model.name} size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2}) top={b.max.y:F2} 噴射の部品={rp.exhaustParts.Count}");
                }
                Shoot(root, Path.Combine(dir, "rockets.png"), new Vector3(0f, 5f, 22f), new Vector3(0f, 3.4f, 0f), 1600, 1000, 40f);
                rockets.gameObject.SetActive(false);

                // ---- 自然物（NatureBuilder と同じ結合メッシュ。2つの星のパレットで色の違いを見る） ----
                var natureRoot = new GameObject("Nature").transform;
                natureRoot.SetParent(root.transform, false);
                var atlas = new PaletteAtlas();
                var lit = WorldMaterials.Textured(atlas.Texture, 0.1f);
                var glow = WorldMaterials.Textured(atlas.Texture, 0.3f, true, 1.1f);
                var kinds = new[]
                {
                    NatureKind.TreeRound, NatureKind.TreeTall, NatureKind.TreeMushroom, NatureKind.TreeLollipop, NatureKind.TreeCrystal,
                    NatureKind.Bush, NatureKind.Rock, NatureKind.Boulder, NatureKind.Crystal, NatureKind.Reed,
                    NatureKind.Flower, NatureKind.Mushroom, NatureKind.GlowMushroom,
                };
                for (int field = 0; field < 2; field++)
                {
                    var palette = Uchujin.World.WorldPaletteFactory.Create(new Uchujin.Core.SeededRandom((ulong)(1234 + field * 777)));
                    var batch = new MeshBatcher(atlas, 100f);
                    var glowBatch = new MeshBatcher(atlas, 100f);
                    for (int i = 0; i < kinds.Length; i++)
                    {
                        var it = new NatureItem
                        {
                            Kind = kinds[i],
                            Position = new Vector3((field == 0 ? -34f : 6f) + (i % 5) * 6.5f, 0f, -(i / 5) * 7f),
                            Yaw = i * 37f,
                            Scale = 1f,
                            Variant = i + field,
                        };
                        if (!NatureModels.Add(it, palette, batch, glowBatch))
                        {
                            problems++;
                            report.AppendLine(kinds[i] + " NG: 自然物のモデルを読めない");
                        }
                        else if (field == 0) report.AppendLine(kinds[i] + " OK（自然物のモデル）");
                    }
                    batch.Build(natureRoot, lit, "Nature" + field);
                    glowBatch.Build(natureRoot, glow, "NatureGlow" + field);
                }
                var natureRes = new GameObject("Resident").transform;
                natureRes.SetParent(natureRoot, false);
                natureRes.localPosition = new Vector3(-20f, 0f, 3f);
                ResidentModelBuilder.Build(natureRes, "0101");
                Shoot(root, Path.Combine(dir, "nature.png"), new Vector3(-6f, 16f, 26f), new Vector3(-6f, 1f, -6f), 2400, 1000, 55f);
                natureRoot.gameObject.SetActive(false);

                // ---- 主人公が持つタブレット（修理前・修理後） ----
                var players = new GameObject("Players").transform;
                players.SetParent(root.transform, false);
                for (int k = 0; k < 2; k++)
                {
                    var p = new GameObject(k == 0 ? "Before" : "After").transform;
                    p.SetParent(players, false);
                    p.localPosition = new Vector3(k == 0 ? -0.9f : 0.9f, 0f, 0f);
                    var visual = Uchujin.Story.AvatarBuilder.Build(AppearanceCatalog.Default(), p);
                    var hand = visual.transform.Find("ToolHolder");
                    if (hand == null) { problems++; report.AppendLine("主人公 NG: ToolHolder がない"); continue; }
                    foreach (var r in hand.GetComponentsInChildren<Renderer>(true)) r.enabled = false; // 道具の柄は隠す
                    var held = TabletHeldView.Attach(hand, k == 1);
                    if (held.transform.childCount == 0) { problems++; report.AppendLine("タブレット NG: モデルがない"); }
                }
                Shoot(root, Path.Combine(dir, "tablet_front.png"), new Vector3(-0.8f, 1.6f, 3.6f), new Vector3(0f, 0.9f, 0f), 1600, 1000, 40f);
                Shoot(root, Path.Combine(dir, "tablet_over_shoulder.png"), new Vector3(1.0f, 2.7f, -1.9f), new Vector3(0.7f, 0.55f, 0.5f), 1200, 900, 45f);

                report.Insert(0, $"村の新しいモデルの検査：問題 {problems}件\n");
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log($"[VillageArtGallery] 問題 {problems}件。保存しました: {dir}");
            }
            finally
            {
                Object.DestroyImmediate(root);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        private static GameObject Place(Transform parent, string path, Vector3 pos, float scale, ref int problems, StringBuilder report)
        {
            var holder = new GameObject(path).transform;
            holder.SetParent(parent, false);
            holder.localPosition = pos;
            var model = ArtModels.Build(holder, path, scale);
            if (model == null)
            {
                problems++;
                report.AppendLine(path + " NG: モデルがない");
                return null;
            }
            var b = ArtModels.LocalBounds(model, holder);
            var issues = MaterialIssues(model);
            problems += issues.Count;
            report.AppendLine(path + (issues.Count == 0 ? " OK" : " NG: " + string.Join(" / ", issues)) +
                              $"  size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2}) 発光={ArtModels.GlowRenderers(model).Length}");
            return model;
        }

        private static List<string> MaterialIssues(GameObject model)
        {
            var issues = new List<string>();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m == null || m.shader == null || m.shader.name.Contains("Error"))
                    {
                        issues.Add("材質がない: " + r.name);
                        return issues;
                    }
            return issues;
        }

        private static void Lights(Transform root)
        {
            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            var sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.68f);
        }

        private static void Shoot(GameObject root, string path, Vector3 from, Vector3 at, int w = 1600, int h = 900, float fov = 40f)
        {
            var camGo = new GameObject("GalleryCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = from;
            camGo.transform.LookAt(at);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.72f, 0.78f, 0.95f);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;
            var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            cam.targetTexture = null;
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(camGo);
        }
    }
}
#endif
