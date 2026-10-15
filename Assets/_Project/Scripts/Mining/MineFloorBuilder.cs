using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;
using Uchujin.World;

namespace Uchujin.Mining
{
    /// <summary>
    /// 鉱山の各階の見た目（プリミティブ）。床・壁（行ごとにまとめた箱）・岩・結晶・明かり・地下湖・昇降機・下り階段。
    /// 壁は高さ控えめ（カメラが壁に隠れにくいように）。天井は付けない（斜め見下ろしカメラのため）。
    /// </summary>
    public static class MineFloorBuilder
    {
        public static Color FloorColor(int floor)
        {
            float t = (floor - 1) / 9f;
            if (t < 0.5f) return Color.Lerp(new Color(0.45f, 0.36f, 0.28f), new Color(0.38f, 0.38f, 0.42f), t * 2f);
            return Color.Lerp(new Color(0.38f, 0.38f, 0.42f), new Color(0.25f, 0.2f, 0.4f), (t - 0.5f) * 2f);
        }

        public static Color WallColor(int floor) => FloorColor(floor) * 0.8f + new Color(0.05f, 0.05f, 0.05f, 0.2f);

        public static Color LightColor(int floor)
        {
            if (floor <= 3) return new Color(1f, 0.82f, 0.55f);
            if (floor <= 6) return new Color(0.75f, 0.9f, 1f);
            if (floor <= 9) return new Color(0.75f, 0.6f, 1f);
            return new Color(1f, 0.95f, 0.8f);
        }

        public static Color Ambient(int floor)
        {
            float t = (floor - 1) / 9f;
            return Color.Lerp(new Color(0.42f, 0.38f, 0.34f), new Color(0.22f, 0.2f, 0.34f), t);
        }

        public static GameObject Build(MiningSystem sys, MineFloorLayout L, Transform parent)
        {
            int f = L.FloorNumber;
            var root = PrimitiveBuilder.Empty("MineFloor_" + f, parent, Vector3.zero);
            root.transform.position = L.Origin;
            float extent = L.Size * MiningTuning.Cell;

            // 床（1枚）
            PrimitiveBuilder.Create(PrimitiveType.Cube, root.transform, new Vector3(extent * 0.5f, -0.5f, extent * 0.5f), new Vector3(extent, 1f, extent), FloorColor(f), "Ground");

            // 壁：床・水に接している壁マスだけを、行ごとに連続した箱にまとめる
            var wallCol = WallColor(f);
            for (int y = 0; y < L.Size; y++)
            {
                int runStart = -1;
                for (int x = 0; x <= L.Size; x++)
                {
                    bool edge = x < L.Size && IsEdgeWall(L, x, y);
                    if (edge && runStart < 0) runStart = x;
                    if ((!edge || x == L.Size) && runStart >= 0)
                    {
                        int len = x - runStart;
                        var center = new Vector3((runStart + len * 0.5f) * MiningTuning.Cell, MiningTuning.WallHeight * 0.5f, (y + 0.5f) * MiningTuning.Cell);
                        PrimitiveBuilder.Create(PrimitiveType.Cube, root.transform, center, new Vector3(len * MiningTuning.Cell, MiningTuning.WallHeight, MiningTuning.Cell), wallCol, "Wall");
                        // 壁の上のでこぼこ（かわいい岩肌）
                        for (int k = 0; k < len; k += 2)
                        {
                            var bump = new Vector3((runStart + k + 0.5f) * MiningTuning.Cell, MiningTuning.WallHeight, (y + 0.5f) * MiningTuning.Cell);
                            PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, bump, new Vector3(2.4f, 0.9f, 2.4f), wallCol * 1.08f, "Bump");
                        }
                        runStart = -1;
                    }
                }
            }

            // 岩（固い障害物。毎日は変わらない）
            foreach (var b in L.Boulders)
            {
                var go = PrimitiveBuilder.Create(PrimitiveType.Sphere, root.transform, b - L.Origin + Vector3.up * 0.35f, new Vector3(1.0f, 0.8f, 0.9f), wallCol * 1.1f, "Boulder");
                go.transform.localEulerAngles = new Vector3(0, (b.x * 37f) % 360f, 0);
            }
            // 壁際の結晶（深い階ほど多い・光る）
            foreach (var c in L.Crystals)
            {
                var col = LightColor(f);
                var cr = PrimitiveBuilder.EmissivePart(root.transform, PrimitiveType.Cube, c - L.Origin + new Vector3(0.9f, 0.4f, 0.9f), new Vector3(0.22f, 0.7f, 0.22f), col * 0.9f, "Crystal");
                cr.transform.localEulerAngles = new Vector3(15, (c.z * 53f) % 360f, 10);
            }
            // 明かり（ランタン）
            int li = 0;
            foreach (var lp in L.Lights)
            {
                if (li++ >= 6) break;
                var lantern = PrimitiveBuilder.Empty("Lantern", root.transform, lp - L.Origin);
                PrimitiveBuilder.EmissivePart(lantern.transform, PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.35f, LightColor(f), "Bulb");
                var light = lantern.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 13f;
                light.intensity = 1.6f;
                light.color = LightColor(f);
                light.shadows = LightShadows.None;
            }

            // 地下湖
            if (L.HasLake)
            {
                var lake = L.Lake;
                var size = new Vector3(lake.width * MiningTuning.Cell, 0.1f, lake.height * MiningTuning.Cell);
                var center = new Vector3((lake.x + lake.width * 0.5f) * MiningTuning.Cell, 0.05f, (lake.y + lake.height * 0.5f) * MiningTuning.Cell);
                PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, center + Vector3.down * 0.3f, new Vector3(size.x, 0.2f, size.z), new Color(0.05f, 0.08f, 0.15f), "LakeBed");
                var water = PrimitiveBuilder.Part(root.transform, PrimitiveType.Cube, center, size, f >= 8 ? new Color(0.45f, 0.35f, 0.85f, 0.75f) : new Color(0.2f, 0.45f, 0.8f, 0.75f), "LakeWater");
                if (f >= 8)
                {
                    var r = water.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = MaterialCache.Get(new Color(0.45f, 0.35f, 0.85f, 0.75f), 0.9f, true);
                }
                // 歩いて入れないように（見えない柵）
                var block = new GameObject("LakeBlocker");
                block.transform.SetParent(root.transform, false);
                block.transform.localPosition = center + Vector3.up * 0.75f;
                var bc = block.AddComponent<BoxCollider>();
                bc.size = new Vector3(size.x - 0.4f, 1.5f, size.z - 0.4f);
                // 縁石
                for (int x = lake.x; x < lake.xMax; x++)
                {
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3((x + 0.5f) * MiningTuning.Cell, 0.12f, lake.y * MiningTuning.Cell), new Vector3(1.1f, 0.35f, 0.6f), wallCol, "Rim");
                    PrimitiveBuilder.Part(root.transform, PrimitiveType.Sphere, new Vector3((x + 0.5f) * MiningTuning.Cell, 0.12f, lake.yMax * MiningTuning.Cell), new Vector3(1.1f, 0.35f, 0.6f), wallCol, "Rim");
                }
            }

            // 最下層：星の結晶の広間（鉱山仕様 §12【提案】）
            if (L.HasDeepHall)
            {
                var hall = L.DeepHall;
                var p = L.CellCenter(hall.xMax - 2, hall.y + hall.height / 2) - L.Origin;
                var cluster = PrimitiveBuilder.Empty("StarCrystal", root.transform, p);
                var col = new Color(0.85f, 0.75f, 1f);
                PrimitiveBuilder.EmissivePart(cluster.transform, PrimitiveType.Cube, new Vector3(0, 1.8f, 0), new Vector3(0.9f, 3.6f, 0.9f), col, "Core").transform.localEulerAngles = new Vector3(0, 45, 5);
                PrimitiveBuilder.EmissivePart(cluster.transform, PrimitiveType.Cube, new Vector3(0.9f, 1.0f, 0.3f), new Vector3(0.5f, 2.0f, 0.5f), col * 0.9f, "Side1").transform.localEulerAngles = new Vector3(0, 20, -25);
                PrimitiveBuilder.EmissivePart(cluster.transform, PrimitiveType.Cube, new Vector3(-0.8f, 0.9f, -0.4f), new Vector3(0.45f, 1.7f, 0.45f), col * 0.85f, "Side2").transform.localEulerAngles = new Vector3(0, 70, 25);
                PrimitiveBuilder.EmissivePart(cluster.transform, PrimitiveType.Cube, new Vector3(0.2f, 0.6f, 0.9f), new Vector3(0.35f, 1.2f, 0.35f), new Color(1f, 0.95f, 0.75f), "Side3").transform.localEulerAngles = new Vector3(20, 10, 15);
                var l = cluster.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 18f;
                l.intensity = 2.2f;
                l.color = col;
                PrimitiveBuilder.AddBoxCollider(cluster, new Vector3(0, 1.5f, 0), new Vector3(2f, 3f, 2f));
                cluster.AddComponent<MineSpin>().speed = 6f;
            }

            BuildElevator(sys, L, root.transform);
            if (L.HasStairs) BuildStairs(sys, L, root.transform);
            return root;
        }

        private static bool IsEdgeWall(MineFloorLayout L, int x, int y)
        {
            if (L.At(x, y) != MineFloorLayout.Wall) return false;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if ((dx != 0 || dy != 0) && L.InBounds(x + dx, y + dy) && L.Cells[x + dx, y + dy] != MineFloorLayout.Wall) return true;
            return false;
        }

        private static void BuildElevator(MiningSystem sys, MineFloorLayout L, Transform root)
        {
            var p = L.CellCenter(L.Elevator) - L.Origin;
            var go = PrimitiveBuilder.Empty("Elevator", root, p);
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0, 0.05f, 0), new Vector3(1.8f, 0.05f, 1.8f), new Color(0.55f, 0.45f, 0.3f), "Platform");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(-0.85f, 1.4f, 0), new Vector3(0.12f, 2.8f, 0.12f), new Color(0.5f, 0.4f, 0.3f), "PoleL");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0.85f, 1.4f, 0), new Vector3(0.12f, 2.8f, 0.12f), new Color(0.5f, 0.4f, 0.3f), "PoleR");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 2.8f, 0), new Vector3(1.9f, 0.15f, 0.25f), new Color(0.5f, 0.4f, 0.3f), "Beam");
            PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Cube, new Vector3(0.85f, 1.1f, 0.15f), new Vector3(0.25f, 0.35f, 0.1f), new Color(0.6f, 1f, 0.7f), "Panel");
            PrimitiveBuilder.AddBoxCollider(go, new Vector3(0, 1f, 0), new Vector3(1.8f, 2f, 1.8f), true);
            int floor = L.FloorNumber;
            SimpleInteractable.Attach(go, "昇降機", "Elevator", pl => sys.OpenElevator(pl, floor));
            WorldLabels.Add(go.transform, () => Loc.Format("地下{0}階", "B{0}F", floor), new Vector3(0, 3.3f, 0), new Color(0.85f, 1f, 0.9f), 18f);
        }

        private static void BuildStairs(MiningSystem sys, MineFloorLayout L, Transform root)
        {
            var p = L.CellCenter(L.Stairs) - L.Origin;
            var go = PrimitiveBuilder.Empty("StairsDown", root, p);
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(2.0f, 0.02f, 2.0f), new Color(0.05f, 0.05f, 0.08f), "Hole");
            for (int i = 0; i < 4; i++)
                PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 0.03f + i * 0.001f, 0.6f - i * 0.35f), new Vector3(1.3f, 0.05f, 0.3f), new Color(0.4f, 0.32f, 0.25f) * (1f - i * 0.15f), "Step" + i);
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(1.1f, 0.7f, 0), new Vector3(0.1f, 1.4f, 0.1f), new Color(0.5f, 0.4f, 0.3f), "SignPole");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(1.1f, 1.45f, 0), new Vector3(0.6f, 0.35f, 0.08f), new Color(0.85f, 0.75f, 0.55f), "Sign");
            PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Cube, new Vector3(1.1f, 1.45f, 0.05f), new Vector3(0.18f, 0.18f, 0.02f), new Color(1f, 0.6f, 0.3f), "Arrow").transform.localEulerAngles = new Vector3(0, 0, 45);
            PrimitiveBuilder.AddBoxCollider(go, new Vector3(0, 0.8f, 0), new Vector3(2f, 1.6f, 2f), true);
            int floor = L.FloorNumber;
            SimpleInteractable.Attach(go, "下の階へ降りる", "Go down", pl => sys.GoToFloor(pl, floor + 1, true));
        }

        /// <summary>鉱山の入口（屋外。Layout.MineEntrance）。</summary>
        public static void BuildEntrance(MiningSystem sys, Transform parent, Vector3 pos, float yaw)
        {
            var go = PrimitiveBuilder.Empty("MineEntranceGate", parent, pos);
            go.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var wood = new Color(0.5f, 0.38f, 0.26f);
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(-1.4f, 1.4f, 0), new Vector3(0.35f, 2.8f, 0.35f), wood, "PostL");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(1.4f, 1.4f, 0), new Vector3(0.35f, 2.8f, 0.35f), wood, "PostR");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 2.9f, 0), new Vector3(3.4f, 0.4f, 0.4f), wood, "Lintel");
            PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 1.3f, 0.25f), new Vector3(2.5f, 2.6f, 0.1f), new Color(0.06f, 0.05f, 0.07f), "Dark");
            PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Sphere, new Vector3(-1.4f, 2.4f, -0.3f), Vector3.one * 0.3f, new Color(1f, 0.8f, 0.5f), "LampL");
            PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Sphere, new Vector3(1.4f, 2.4f, -0.3f), Vector3.one * 0.3f, new Color(1f, 0.8f, 0.5f), "LampR");
            // ツルハシの看板（字が読めなくても鉱山だと分かる）
            var sign = PrimitiveBuilder.Part(go.transform, PrimitiveType.Cube, new Vector3(0, 3.4f, -0.1f), new Vector3(1.2f, 0.5f, 0.08f), new Color(0.85f, 0.75f, 0.55f), "Sign");
            PrimitiveBuilder.Part(sign.transform, PrimitiveType.Cube, new Vector3(0, 0, -0.6f), new Vector3(0.6f, 0.12f, 0.5f), new Color(0.45f, 0.45f, 0.5f), "PickHead").transform.localEulerAngles = new Vector3(0, 0, 20);
            PrimitiveBuilder.AddBoxCollider(go, new Vector3(0, 1.3f, -0.2f), new Vector3(2.6f, 2.6f, 1.2f), true);
            SimpleInteractable.Attach(go, "鉱山に入る", "Enter the mine", pl => sys.OpenEntrance(pl));
        }
    }

    public class MineSpin : MonoBehaviour
    {
        public float speed = 10f;
        private void Update() => transform.Rotate(0f, speed * UnityEngine.Time.deltaTime, 0f);
    }
}
