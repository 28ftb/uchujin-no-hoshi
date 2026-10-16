using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // 生活による地図の変化（村仕様 §11）
    //  ・日常の痕跡：プレイヤーや住民がよく通った所が踏み固められて道になる（草は日ごとに少し戻る）。
    //  ・公共の場所：住民の暮らし（買い物・依頼・引っ越し）と主人公の寄付で、花壇・東屋・灯りなどが少しずつ整備される。
    //    【提案】公共施設の発展はロケット修理の必須条件にしない（村仕様 §11.2）。
    // いずれも星の状態（SaveScope.Planet、ホストが正本）。
    // =====================================================================

    [Serializable]
    public class ProjectProgress
    {
        public string id;
        public int progress;
        public bool completed;
        public int completedDay;
    }

    [Serializable]
    public class WorldPlanetState
    {
        public int generatorVersion;
        /// <summary>踏み跡：index*256+値 の詰め合わせ（0 のセルは省略）。</summary>
        public List<int> wear = new List<int>();
        public List<ProjectProgress> projects = new List<ProjectProgress>();
        /// <summary>前日からの村の活気（整備の進み具合に加算）。</summary>
        public int pendingVitality;
    }

    /// <summary>公共施設の整備に寄付する（材料 or お金）。ホスト権威。</summary>
    [Serializable]
    public class ContributeToProjectCommand : WorldCommand
    {
        public string projectId;
        public string itemId;
        public int count;
        public int money;
    }

    /// <summary>踏み跡の格子（WorldTuning.WearCell 角）。</summary>
    public sealed class WearGrid
    {
        public readonly int G;
        public readonly byte[] Cells;

        public WearGrid()
        {
            G = Mathf.RoundToInt(WorldTuning.WorldSize / WorldTuning.WearCell);
            Cells = new byte[G * G];
        }

        public int IndexOf(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x + WorldTuning.Half) / WorldTuning.WearCell);
            int z = Mathf.FloorToInt((p.z + WorldTuning.Half) / WorldTuning.WearCell);
            if (x < 0 || z < 0 || x >= G || z >= G) return -1;
            return z * G + x;
        }

        /// <summary>踏む。見た目の段階が変わったら true。</summary>
        public bool Step(int idx, int amount)
        {
            if (idx < 0) return false;
            int before = Cells[idx];
            int after = Mathf.Min(255, before + amount);
            Cells[idx] = (byte)after;
            return VisualLevel(before) != VisualLevel(after);
        }

        public static int VisualLevel(int v) => v < WorldTuning.WearVisibleThreshold ? 0 : 1 + (v - WorldTuning.WearVisibleThreshold) / 22;

        public List<int> Pack()
        {
            var list = new List<int>();
            for (int i = 0; i < Cells.Length; i++) if (Cells[i] > 0) list.Add(i * 256 + Cells[i]);
            return list;
        }

        public void Unpack(List<int> packed)
        {
            Array.Clear(Cells, 0, Cells.Length);
            if (packed == null) return;
            foreach (var v in packed)
            {
                int i = v >> 8;
                if (i >= 0 && i < Cells.Length) Cells[i] = (byte)(v & 255);
            }
        }
    }

    /// <summary>公共施設の整備の見た目（完成した物・工事中の目印）。</summary>
    public static class ProjectBuilder
    {
        private static readonly Color Wood = new Color(0.62f, 0.44f, 0.28f);
        private static readonly Color DarkWood = new Color(0.42f, 0.29f, 0.18f);
        private static readonly Color LampOn = new Color(1f, 0.86f, 0.55f);

        public static GameObject BuildMarker(ProjectSite site, Transform parent, float progress01)
        {
            var root = StructureBuilder.Root(parent, "ProjectMarker_" + site.Id, MarkerPosition(site), site.Yaw);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f * Mathf.Deg2Rad + 0.78f;
                StructureBuilder.Cyl(root, new Vector3(Mathf.Cos(a) * 1.2f, 0.45f, Mathf.Sin(a) * 1.2f), new Vector3(0.08f, 0.45f, 0.08f), Wood, false, "Stake");
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (i + 0.5f) * 90f;
                StructureBuilder.Box(root, Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.75f, 0.85f), new Vector3(1.7f, 0.05f, 0.02f), new Color(1f, 0.55f, 0.3f), false, "Ribbon", new Vector3(0f, a, 0f));
            }
            var crate = StructureBuilder.Box(root, new Vector3(0f, 0.35f, 0f), new Vector3(0.8f, 0.7f, 0.7f), new Color(0.82f, 0.68f, 0.45f), true, "MaterialBox");
            // 積まれた材料（進み具合で増える）
            int stacks = Mathf.Clamp(Mathf.RoundToInt(progress01 * 5f), 0, 5);
            for (int i = 0; i < stacks; i++)
                StructureBuilder.Box(root, new Vector3(-0.9f + (i % 3) * 0.2f, 0.1f + (i / 3) * 0.2f, -0.6f), new Vector3(0.9f, 0.18f, 0.25f), Wood, false, "Plank", new Vector3(0f, 20f * i, 0f));
            return crate;
        }

        public static Vector3 MarkerPosition(ProjectSite site)
        {
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            Vector3 p = site.Position;
            switch (site.Kind)
            {
                case "flowerbeds": p += new Vector3(-WorldTuning.PlazaRadius * 0.45f, 0f, -WorldTuning.PlazaRadius * 0.45f); break;
                case "garland": p += Quaternion.Euler(0f, site.Yaw, 0f) * new Vector3(-4.8f, 0f, 2f); break;
                case "gazebo": p += new Vector3(2.5f, 0f, 1.5f); break;
                case "bridge_lanterns": p += new Vector3(4f, 0f, 4f); break;
                case "pier": p += Quaternion.Euler(0f, site.Yaw, 0f) * new Vector3(0f, 0f, -6f); break;
            }
            if (layout != null && site.Kind != "pier") p.y = layout.HeightAt(p);
            return p;
        }

        public static void BuildCompleted(ProjectSite site, Transform parent, GeneratedWorld w)
        {
            var P = w.Palette;
            var root = StructureBuilder.Root(parent, "Project_" + site.Id, site.Position, site.Yaw);
            var layout = w.Layout;
            switch (site.Kind)
            {
                case "flowerbeds":
                {
                    // 広場の縁の花壇
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * 45f + 22.5f;
                        var lp = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, WorldTuning.PlazaRadius - 4.5f);
                        var wp = site.Position + lp;
                        float y = layout.HeightAt(wp) - site.Position.y;
                        var bed = StructureBuilder.Root(root, "Bed", wp + Vector3.up * 0f, a);
                        bed.position = new Vector3(wp.x, site.Position.y + y, wp.z);
                        // 花びらの縁の丸い花壇のモデル（直径約 2.5 m）。無ければ従来の箱の花壇
                        if (Uchujin.Core.Visual.ArtModels.Build(bed, "Village/flowerbed", StructureBuilder.VillageModelScale) != null) continue;
                        StructureBuilder.Box(bed, new Vector3(0f, 0.2f, 0f), new Vector3(2f, 0.4f, 0.9f), P.Stone, false, "Edge");
                        StructureBuilder.Box(bed, new Vector3(0f, 0.36f, 0f), new Vector3(1.8f, 0.1f, 0.7f), P.Soil, false, "Soil");
                        for (int k = 0; k < 5; k++)
                            StructureBuilder.Ball(bed, new Vector3(-0.7f + k * 0.35f, 0.55f, (k % 2) * 0.2f - 0.1f), Vector3.one * 0.3f, P.Flowers[(i + k) % P.Flowers.Length]);
                    }
                    break;
                }
                case "garland":
                {
                    // 伝言広場のまわりの旗飾り
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i * 36f * Mathf.Deg2Rad;
                        var flag = StructureBuilder.Box(root, new Vector3(Mathf.Cos(a) * 4.6f, 3.2f - Mathf.Abs(Mathf.Sin(a * 2.5f)) * 0.3f, Mathf.Sin(a) * 4.6f), new Vector3(0.35f, 0.45f, 0.03f), P.Flowers[i % P.Flowers.Length], false, "Pennant", new Vector3(0f, -a * Mathf.Rad2Deg + 90f, 0f));
                        var gm = flag.AddComponent<GentleMotion>();
                        gm.bobAmplitude = 0.04f;
                        gm.bobSpeed = 2f + i * 0.2f;
                    }
                    break;
                }
                case "gazebo":
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f * Mathf.Deg2Rad + 0.78f;
                        StructureBuilder.Cyl(root, new Vector3(Mathf.Cos(a) * 2.6f, 1.3f, Mathf.Sin(a) * 2.6f), new Vector3(0.2f, 1.3f, 0.2f), Wood, true, "Pillar");
                    }
                    StructureBuilder.GabledRoof(root, 2.6f, 4.4f, 4.4f, P.Roofs[1], 28f);
                    var lamp = StructureBuilder.Glow(root, PrimitiveType.Sphere, new Vector3(0f, 2.4f, 0f), Vector3.one * 0.3f, LampOn, "Lamp");
                    NightLight.Attach(root.gameObject, StructureBuilder.AddPointLight(root, new Vector3(0f, 2.2f, 0f), LampOn, 9f, 1f), LampOn, new Color(0.7f, 0.65f, 0.5f), lamp.GetComponent<Renderer>());
                    break;
                }
                case "bridge_lanterns":
                {
                    var lamps = new List<Renderer>();
                    foreach (var br in w.Field.Bridges)
                    {
                        var dir = (br.B - br.A).normalized;
                        var n = new Vector2(-dir.y, dir.x);
                        foreach (var end in new[] { br.A, br.B })
                            foreach (int side in new[] { -1, 1 })
                            {
                                var p = end + n * side * (br.HalfWidth - 0.1f);
                                var lr = StructureBuilder.Root(root, "BridgeLantern", new Vector3(p.x, br.DeckY, p.y), 0f);
                                StructureBuilder.Cyl(lr, new Vector3(0f, 0.7f, 0f), new Vector3(0.1f, 0.7f, 0.1f), DarkWood, false, "Pole");
                                lamps.Add(StructureBuilder.Glow(lr, PrimitiveType.Cube, new Vector3(0f, 1.55f, 0f), new Vector3(0.3f, 0.35f, 0.3f), LampOn, "Lantern").GetComponent<Renderer>());
                            }
                    }
                    NightLight.Attach(root.gameObject, null, LampOn, new Color(0.75f, 0.7f, 0.55f), lamps.ToArray());
                    break;
                }
                case "pier":
                {
                    if (w.HasPier) StructureBuilder.Pier(w.PierEnd - (w.PierEnd - w.PierStart).normalized * 0.5f, w.PierEnd + (w.PierEnd - w.PierStart).normalized * 8f, root, P, 0f);
                    var lamp = StructureBuilder.Glow(root, PrimitiveType.Sphere, new Vector3(0.9f, 1.6f, 7f), Vector3.one * 0.3f, LampOn, "PierLamp");
                    StructureBuilder.Cyl(root, new Vector3(0.9f, 0.8f, 7f), new Vector3(0.1f, 0.8f, 0.1f), DarkWood, false, "Pole");
                    NightLight.Attach(root.gameObject, StructureBuilder.AddPointLight(root, new Vector3(0.9f, 1.5f, 7f), LampOn, 9f, 1f), LampOn, new Color(0.7f, 0.65f, 0.5f), lamp.GetComponent<Renderer>());
                    break;
                }
                case "pond_deck":
                {
                    for (int i = 0; i < 5; i++)
                        StructureBuilder.Box(root, new Vector3(0f, 0.25f, 0.4f + i * 0.75f), new Vector3(2f, 0.12f, 0.7f), Wood, true, "Deck");
                    StructureBuilder.Box(root, new Vector3(-0.6f, 0.65f, 3.6f), new Vector3(0.9f, 0.1f, 0.4f), Wood, false, "Seat");
                    break;
                }
                case "forest_rest":
                {
                    StructureBuilder.Cyl(root, new Vector3(0f, 0.3f, 0f), new Vector3(1.2f, 0.3f, 1.2f), Wood, true, "Stump");
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 120f;
                        var lp = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.25f, 2.2f);
                        StructureBuilder.Cyl(root, lp, new Vector3(0.5f, 1.6f, 0.5f), Wood, false, "LogSeat", new Vector3(90f, a, 0f));
                    }
                    var lamp = StructureBuilder.Glow(root, PrimitiveType.Sphere, new Vector3(1.6f, 1.6f, -1.2f), Vector3.one * 0.28f, new Color(0.6f, 1f, 0.8f), "Lantern");
                    StructureBuilder.Cyl(root, new Vector3(1.6f, 0.75f, -1.2f), new Vector3(0.08f, 0.75f, 0.08f), DarkWood, false, "Pole");
                    NightLight.Attach(root.gameObject, StructureBuilder.AddPointLight(root, new Vector3(1.6f, 1.5f, -1.2f), new Color(0.6f, 1f, 0.8f), 8f, 1f), new Color(0.6f, 1f, 0.8f), new Color(0.6f, 0.7f, 0.6f), lamp.GetComponent<Renderer>());
                    break;
                }
                case "park":
                {
                    // ブランコ・すべり台・木
                    StructureBuilder.Box(root, new Vector3(-2f, 1.4f, 0f), new Vector3(0.12f, 2.8f, 0.12f), new Color(0.9f, 0.4f, 0.35f), true, "SwingPost");
                    StructureBuilder.Box(root, new Vector3(1f, 1.4f, 0f), new Vector3(0.12f, 2.8f, 0.12f), new Color(0.9f, 0.4f, 0.35f), true, "SwingPost");
                    StructureBuilder.Box(root, new Vector3(-0.5f, 2.8f, 0f), new Vector3(3.2f, 0.12f, 0.12f), new Color(0.9f, 0.4f, 0.35f), false, "SwingBar");
                    var seat = StructureBuilder.Box(root, new Vector3(-0.5f, 0.7f, 0f), new Vector3(0.7f, 0.08f, 0.35f), P.Roofs[2], false, "SwingSeat");
                    var gm = seat.AddComponent<GentleMotion>();
                    gm.bobAmplitude = 0.08f;
                    gm.bobSpeed = 1.6f;
                    StructureBuilder.Box(root, new Vector3(3f, 0.9f, 2f), new Vector3(0.9f, 0.12f, 3f), new Color(0.4f, 0.7f, 0.95f), true, "Slide", new Vector3(28f, 0f, 0f));
                    StructureBuilder.Box(root, new Vector3(3f, 0.9f, 0.4f), new Vector3(0.9f, 1.8f, 0.6f), P.Roofs[4], true, "SlideTower");
                    StructureBuilder.Cyl(root, new Vector3(-3f, 1.1f, 3f), new Vector3(0.4f, 1.1f, 0.4f), P.Trunk, true, "Tree");
                    StructureBuilder.Ball(root, new Vector3(-3f, 3f, 3f), new Vector3(2.8f, 2.4f, 2.8f), P.Leaves[2], "Canopy");
                    StructureBuilder.Box(root, new Vector3(0f, 0.45f, -3f), new Vector3(1.8f, 0.1f, 0.5f), Wood, true, "Bench");
                    break;
                }
            }
        }
    }
}
