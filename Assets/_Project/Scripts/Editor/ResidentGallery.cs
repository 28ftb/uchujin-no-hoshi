#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Uchujin.Core.Visual;
using Uchujin.Residents;

namespace Uchujin.EditorTools
{
    /// <summary>
    /// デザイン済み住民100人のモデルの確認用：ゲームと同じ組み立て（ResidentModelBuilder）で全員を並べ、
    /// 部品・アクション・顔の軸・高さ・材質を検査して Logs/ResidentGallery/report.txt に書き、
    /// 系統ごとに待機・特徴的な動き・寝姿の3枚を撮影する。
    /// メニュー「宇宙人の星 → 住民100人を一覧撮影・検査」、またはバッチモード（-nographics なし）で
    /// -executeMethod Uchujin.EditorTools.ResidentGallery.Capture
    /// </summary>
    public static class ResidentGallery
    {
        private const float Spacing = 2.2f;

        [MenuItem("宇宙人の星/住民100人を一覧撮影・検査", priority = 71)]
        public static void Capture()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "ResidentGallery");
            Directory.CreateDirectory(dir);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var root = new GameObject("ResidentGallery");
            var report = new StringBuilder();
            int problems = 0;
            try
            {
                var rows = new List<GameObject>();
                var models = new List<KeyValuePair<ResidentDesign, ResidentModelParts>>();
                foreach (var d in ResidentDesigns.All)
                {
                    int row = int.Parse(d.Id.Substring(0, 2)) - 1, col = int.Parse(d.Id.Substring(2, 2)) - 1;
                    while (rows.Count <= row)
                    {
                        var r = new GameObject("Row" + (rows.Count + 1));
                        r.transform.SetParent(root.transform, false);
                        rows.Add(r);
                    }
                    var holder = new GameObject(d.Id);
                    holder.transform.SetParent(rows[row].transform, false);
                    holder.transform.localPosition = new Vector3((col - 4.5f) * Spacing, 0f, 0f);
                    var m = ResidentModelBuilder.Build(holder.transform, d.Id);
                    var issues = Inspect(d, m);
                    problems += issues.Count;
                    report.AppendLine(d.Id + (issues.Count == 0 ? " OK" : " NG: " + string.Join(" / ", issues)) +
                                      (m != null ? $"  height={m.Height:F3} arms={m.ArmsLeft.Count}+{m.ArmsRight.Count} legs={m.Legs.Count}" : ""));
                    if (m != null) models.Add(new KeyValuePair<ResidentDesign, ResidentModelParts>(d, m));
                }

                var lightGo = new GameObject("Sun");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.rotation = Quaternion.Euler(40f, 150f, 0f);
                var sun = lightGo.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.3f;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.57f, 0.65f);

                var at = new Vector3(0f, 0.75f, 0f);
                var from = new Vector3(0f, 2.2f, 19.5f);
                Shoot(root, Path.Combine(dir, "_warmup.png"), from, at);
                File.Delete(Path.Combine(dir, "_warmup.png"));
                foreach (var pose in new[] { "Idle", "Signature", "Sleep" })
                {
                    foreach (var kv in models) Sample(kv.Value, pose);
                    for (int r = 0; r < rows.Count; r++)
                    {
                        for (int k = 0; k < rows.Count; k++) rows[k].SetActive(k == r);
                        Shoot(root, Path.Combine(dir, $"{r + 1:00}_{pose.ToLowerInvariant()}.png"), from, at);
                    }
                }
                foreach (var r in rows) r.SetActive(true);
                report.Insert(0, $"住民モデル検査：{ResidentDesigns.All.Length}人、問題 {problems}件\n");
                File.WriteAllText(Path.Combine(dir, "report.txt"), report.ToString(), new UTF8Encoding(false));
                Debug.Log($"[ResidentGallery] 問題 {problems}件。保存しました: {dir}");
            }
            finally
            {
                Object.DestroyImmediate(root);
                ShaderUtil.allowAsyncCompilation = async;
            }
        }

        /// <summary>
        /// 星ごとの住民の選び方の確認：シード200個で初期住民を作り、デザインの重複・案内役（Round で道案内）・
        /// 修理屋（多腕）・職業とデザインの合い方・系統の数を調べて Logs/ResidentGallery/selection.txt に書く。
        /// -executeMethod Uchujin.EditorTools.ResidentGallery.CheckSelection
        /// </summary>
        [MenuItem("宇宙人の星/住民の選び方を検査", priority = 72)]
        public static void CheckSelection()
        {
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "ResidentGallery");
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            int bad = 0, jobFit = 0, jobAll = 0, seeds = 200;
            var lineageTotal = new Dictionary<Uchujin.Core.AlienLineage, int>();
            var designUse = new Dictionary<string, int>();
            for (int s = 1; s <= seeds; s++)
            {
                ulong seed = (ulong)s * 2654435761UL;
                var list = ResidentGenerator.GenerateInitial(seed, 16);
                var seen = new HashSet<string>();
                var lineages = new HashSet<Uchujin.Core.AlienLineage>();
                foreach (var p in list)
                {
                    var d = ResidentDesigns.Get(p.designId);
                    if (d == null) { bad++; sb.AppendLine($"seed {s}: {p.genKey} デザインなし"); continue; }
                    if (!seen.Add(d.Id)) { bad++; sb.AppendLine($"seed {s}: {d.Id} が重複"); }
                    if (d.Lineage != p.lineage) { bad++; sb.AppendLine($"seed {s}: {d.Id} の系統が合わない"); }
                    jobAll++;
                    if (d.CanDo(p.originalJob)) jobFit++;
                    lineages.Add(d.Lineage);
                    designUse[d.Id] = designUse.TryGetValue(d.Id, out var u) ? u + 1 : 1;
                    if (p.lineage == Uchujin.Core.AlienLineage.Round && p != list[0] && string.CompareOrdinal(p.id, list[0].id) < 0)
                    { bad++; sb.AppendLine($"seed {s}: 案内役より Id が前の Round {d.Id}"); }
                    if (p.originalJob == Uchujin.Core.ResidentJob.Mechanic && p.lineage != Uchujin.Core.AlienLineage.MultiArm)
                    { bad++; sb.AppendLine($"seed {s}: 修理屋が多腕でない {d.Id}"); }
                }
                var g = ResidentDesigns.Get(list[0].designId);
                if (g == null || g.Lineage != Uchujin.Core.AlienLineage.Round || !g.CanDo(Uchujin.Core.ResidentJob.Courier))
                { bad++; sb.AppendLine($"seed {s}: 案内役が Round の道案内でない"); }
                lineageTotal[(Uchujin.Core.AlienLineage)lineages.Count] = lineageTotal.TryGetValue((Uchujin.Core.AlienLineage)lineages.Count, out var c) ? c + 1 : 1;
            }
            sb.Insert(0, $"シード {seeds}個 × 初期住民16人：問題 {bad}件、職業に合うデザイン {jobFit}/{jobAll}（{100f * jobFit / jobAll:F1}%）、" +
                         $"一度でも選ばれたデザイン {designUse.Count}/100\n" +
                         "1つの星にいる系統の数（星の数）：" + string.Join("、", SortedKeys(lineageTotal)) + "\n");
            File.WriteAllText(Path.Combine(dir, "selection.txt"), sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[ResidentGallery] 選び方の問題 {bad}件。{Path.Combine(dir, "selection.txt")}");
        }

        private static IEnumerable<string> SortedKeys(Dictionary<Uchujin.Core.AlienLineage, int> d)
        {
            var keys = new List<Uchujin.Core.AlienLineage>(d.Keys);
            keys.Sort();
            foreach (var k in keys) yield return $"{(int)k}系統:{d[k]}";
        }

        /// <summary>モデルの部品・アクション・顔・高さ・材質を調べる。</summary>
        private static List<string> Inspect(ResidentDesign d, ResidentModelParts m)
        {
            var issues = new List<string>();
            if (m == null)
            {
                issues.Add("モデルが読めない");
                return issues;
            }
            var anim = m.Animation;
            if (anim == null) issues.Add("Animation が無い");
            else
            {
                if (anim["Idle"] == null) issues.Add("Idle が無い");
                if (anim["Signature"] == null) issues.Add("Signature が無い");
                if (d.HasSleep && anim["Sleep"] == null) issues.Add("Sleep が無い");
            }
            if (m.EyeL.childCount == 0 || m.EyeR.childCount == 0) issues.Add("目が見つからない");
            if (m.Mouth.childCount == 0) issues.Add("口が見つからない");
            if (m.ArmsLeft.Count + m.ArmsRight.Count > 0 && m.HandRight == null) issues.Add("右手が無い");
            // 資料の高さはメッシュの上下の幅（浮遊する住民は地面からの高さではない）
            bool any = false;
            var b = new Bounds();
            foreach (var r in m.Model.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            float extent = b.size.y / ResidentModelBuilder.GameScale;
            if (!any) issues.Add("見える部品が無い");
            else if (Mathf.Abs(extent - d.Height) > 0.06f) issues.Add($"高さ {extent:F3} m（資料 {d.Height:F3} m）");
            int skinned = 0;
            foreach (var s in m.Model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.enabled) skinned++;
            if (skinned > 0) issues.Add($"硬い部品にできなかった部品 {skinned}個");
            foreach (var r in m.Model.GetComponentsInChildren<Renderer>(true))
                foreach (var mat in r.sharedMaterials)
                    if (mat == null || mat.shader == null || mat.shader.name.Contains("Error"))
                    {
                        issues.Add("材質が無い: " + r.name);
                        break;
                    }
            return issues;
        }

        /// <summary>撮影用に、アクションの途中（Signature は真ん中、Sleep は最初）の姿勢にする。無ければ待機。</summary>
        private static void Sample(ResidentModelParts m, string pose)
        {
            var anim = m.Animation;
            if (anim == null) return;
            var state = anim[pose] ?? anim["Idle"];
            if (state == null || state.clip == null) return;
            float t = pose == "Signature" ? state.length * 0.5f : 0f;
            state.clip.SampleAnimation(m.Model, t);
        }

        private static void Shoot(GameObject root, string path, Vector3 from, Vector3 at, float fov = 34f)
        {
            const int w = 2400, h = 700;
            var camGo = new GameObject("GalleryCamera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.position = from;
            camGo.transform.LookAt(at);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.18f, 0.28f);
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
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
