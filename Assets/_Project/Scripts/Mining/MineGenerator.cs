using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Mining
{
    /// <summary>
    /// 1階分の地形（通路・部屋・壁・岩・地下湖）。シードと階数だけから決定的に生成する（鉱山仕様 §3.4：地形は維持し、鉱石だけ日替わり）。
    /// </summary>
    public sealed class MineFloorLayout
    {
        public const byte Wall = 0, Floor = 1, Water = 2;

        public int FloorNumber;
        public int Size;
        public byte[,] Cells;
        public Vector3 Origin;
        public readonly List<RectInt> Rooms = new List<RectInt>();
        public Vector2Int Entry;
        public Vector2Int Elevator;
        public Vector2Int Stairs;
        public bool HasStairs;
        public bool HasLake;
        public RectInt Lake;
        public Vector2Int LakeStand;
        public bool HasDeepHall;
        public RectInt DeepHall;
        public readonly List<Vector2Int> NodeSlots = new List<Vector2Int>();
        public readonly List<Vector3> Boulders = new List<Vector3>();
        public readonly List<Vector3> Crystals = new List<Vector3>();
        public readonly List<Vector3> Lights = new List<Vector3>();
        public readonly List<Vector2Int> WallAdjacent = new List<Vector2Int>();

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;
        public byte At(int x, int y) => InBounds(x, y) ? Cells[x, y] : Wall;

        public Vector3 CellCenter(Vector2Int c) => CellCenter(c.x, c.y);
        public Vector3 CellCenter(int x, int y) => Origin + new Vector3((x + 0.5f) * MiningTuning.Cell, 0f, (y + 0.5f) * MiningTuning.Cell);
        public Vector3 Center => Origin + new Vector3(Size * MiningTuning.Cell * 0.5f, 0f, Size * MiningTuning.Cell * 0.5f);
        public Vector3 RectCenter(RectInt r) => Origin + new Vector3((r.x + r.width * 0.5f) * MiningTuning.Cell, 0f, (r.y + r.height * 0.5f) * MiningTuning.Cell);

        public bool ContainsWorld(Vector3 p)
        {
            var local = p - Origin;
            float extent = Size * MiningTuning.Cell;
            return local.x >= -5f && local.z >= -5f && local.x <= extent + 5f && local.z <= extent + 5f && Mathf.Abs(p.y - Origin.y) < 60f;
        }
    }

    public static class MineGenerator
    {
        public static Vector3 FloorOrigin(int floor) => MiningTuning.MineOrigin + new Vector3((floor - 1) * MiningTuning.FloorSpacing, 0f, 0f);

        public static MineFloorLayout Generate(PlanetContext ctx, int floor)
        {
            var rng = ctx.Rng("mine.floor." + floor);
            int n = MiningTuning.GridSize;
            var L = new MineFloorLayout { FloorNumber = floor, Size = n, Cells = new byte[n, n], Origin = FloorOrigin(floor) };
            bool lakeFloor = System.Array.IndexOf(MiningTuning.LakeFloors, floor) >= 0;
            bool deepest = floor >= MiningTuning.FloorCount;

            // ---- 部屋 ----
            int target = rng.Range(6, 9);
            for (int attempt = 0; attempt < 80 && L.Rooms.Count < target; attempt++)
            {
                int w, h;
                if (L.Rooms.Count == 1 && (lakeFloor || deepest)) { w = rng.Range(6, 8); h = rng.Range(6, 8); }
                else { w = rng.Range(3, 7); h = rng.Range(3, 7); }
                int x = rng.Range(1, n - w - 1);
                int y = rng.Range(1, n - h - 1);
                var r = new RectInt(x, y, w, h);
                bool overlap = false;
                foreach (var o in L.Rooms)
                {
                    var grown = new RectInt(o.x - 1, o.y - 1, o.width + 2, o.height + 2);
                    if (grown.Overlaps(r)) { overlap = true; break; }
                }
                if (overlap) continue;
                L.Rooms.Add(r);
            }
            if (L.Rooms.Count < 2)
            {
                L.Rooms.Clear();
                L.Rooms.Add(new RectInt(2, 2, 5, 5));
                L.Rooms.Add(new RectInt(n - 9, n - 9, 7, 7));
            }
            foreach (var r in L.Rooms) Carve(L, r);

            // ---- 通路（最寄りの部屋を順につなぐ＋輪を少し）----
            var connected = new List<int> { 0 };
            var rest = new List<int>();
            for (int i = 1; i < L.Rooms.Count; i++) rest.Add(i);
            while (rest.Count > 0)
            {
                int bestA = -1, bestB = -1;
                float bestD = float.MaxValue;
                foreach (var a in connected)
                {
                    foreach (var b in rest)
                    {
                        float d = Vector2.Distance(L.Rooms[a].center, L.Rooms[b].center);
                        if (d < bestD) { bestD = d; bestA = a; bestB = b; }
                    }
                }
                Corridor(L, Center(L.Rooms[bestA]), Center(L.Rooms[bestB]), rng.Chance(0.5f));
                connected.Add(bestB);
                rest.Remove(bestB);
            }
            int loops = rng.Range(1, 3);
            for (int i = 0; i < loops && L.Rooms.Count > 2; i++)
            {
                int a = rng.Range(0, L.Rooms.Count), b = rng.Range(0, L.Rooms.Count);
                if (a != b) Corridor(L, Center(L.Rooms[a]), Center(L.Rooms[b]), rng.Chance(0.5f));
            }

            // ---- 入口（到着地点）と昇降機 ----
            var entryRoom = L.Rooms[0];
            L.Entry = Center(entryRoom);
            L.Elevator = new Vector2Int(Mathf.Clamp(L.Entry.x - 1, entryRoom.x, entryRoom.xMax - 1), Mathf.Clamp(L.Entry.y + 1, entryRoom.y, entryRoom.yMax - 1));
            if (L.Elevator == L.Entry) L.Elevator = new Vector2Int(entryRoom.x, entryRoom.y);

            // ---- 入口から最も遠い部屋：下り階段（最下層は「星の結晶の広間」）----
            var dist = Bfs(L, L.Entry);
            int far = 0;
            int farD = -1;
            for (int i = 1; i < L.Rooms.Count; i++)
            {
                var c = Center(L.Rooms[i]);
                int d = dist[c.x, c.y];
                if (d > farD) { farD = d; far = i; }
            }
            if (deepest)
            {
                // 最深部：大きな部屋（2番目に作った大部屋があればそれ）を広間にする
                int hall = L.Rooms.Count > 1 ? 1 : far;
                L.HasDeepHall = true;
                L.DeepHall = L.Rooms[hall];
                far = hall;
            }
            else
            {
                L.HasStairs = true;
                L.Stairs = Center(L.Rooms[far]);
            }

            // ---- 地下湖（部屋の内側。周りの1マスは歩ける縁として残すので、通路のつながりは壊れない）----
            if (lakeFloor)
            {
                int lakeRoom = -1;
                int bestArea = 0;
                for (int i = 1; i < L.Rooms.Count; i++)
                {
                    if (i == far && !deepest) continue;
                    var r = L.Rooms[i];
                    if (r.width < 4 || r.height < 4) continue;
                    if (r.width * r.height > bestArea) { bestArea = r.width * r.height; lakeRoom = i; }
                }
                if (lakeRoom >= 0)
                {
                    var r = L.Rooms[lakeRoom];
                    var lake = new RectInt(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
                    if (deepest && L.HasDeepHall && lakeRoom == far)
                    {
                        // 広間の片側だけを湖にする
                        lake = new RectInt(r.x + 1, r.y + 1, Mathf.Max(2, (r.width - 2) / 2), r.height - 2);
                    }
                    for (int x = lake.x; x < lake.xMax; x++)
                        for (int y = lake.y; y < lake.yMax; y++)
                            L.Cells[x, y] = MineFloorLayout.Water;
                    L.HasLake = true;
                    L.Lake = lake;
                    L.LakeStand = new Vector2Int(lake.x - 1, lake.y + lake.height / 2);
                }
            }

            // ---- 壁沿いのマス・岩・結晶・明かり ----
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    if (L.Cells[x, y] != MineFloorLayout.Floor) continue;
                    if (L.At(x + 1, y) == MineFloorLayout.Wall || L.At(x - 1, y) == MineFloorLayout.Wall ||
                        L.At(x, y + 1) == MineFloorLayout.Wall || L.At(x, y - 1) == MineFloorLayout.Wall)
                        L.WallAdjacent.Add(new Vector2Int(x, y));
                }
            }
            var reserved = new HashSet<Vector2Int> { L.Entry, L.Elevator };
            if (L.HasStairs) reserved.Add(L.Stairs);
            if (L.HasLake) reserved.Add(L.LakeStand);
            var boulderCells = new HashSet<Vector2Int>();
            for (int i = 0; i < L.Rooms.Count; i++)
            {
                var r = L.Rooms[i];
                var corners = new[] { new Vector2Int(r.x, r.y), new Vector2Int(r.xMax - 1, r.y), new Vector2Int(r.x, r.yMax - 1), new Vector2Int(r.xMax - 1, r.yMax - 1) };
                foreach (var c in corners)
                {
                    if (reserved.Contains(c) || L.At(c.x, c.y) != MineFloorLayout.Floor || !rng.Chance(0.45f)) continue;
                    // 角の壁寄りに置き、通り道をふさがない
                    var center = L.CellCenter(c);
                    var toCorner = new Vector3(c.x == r.x ? -1f : 1f, 0, c.y == r.y ? -1f : 1f) * (MiningTuning.Cell * 0.28f);
                    L.Boulders.Add(center + toCorner);
                    boulderCells.Add(c);
                }
                L.Lights.Add(L.RectCenter(r) + Vector3.up * 2.3f);
            }
            foreach (var c in L.WallAdjacent)
            {
                if (!rng.Chance(floor >= 6 ? 0.12f : 0.06f)) continue;
                L.Crystals.Add(L.CellCenter(c));
            }

            // ---- 鉱石を置けるマス（毎日この中から選ぶ）----
            for (int x = 0; x < n; x++)
            {
                for (int y = 0; y < n; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (L.Cells[x, y] != MineFloorLayout.Floor || reserved.Contains(c) || boulderCells.Contains(c)) continue;
                    if (Mathf.Abs(x - L.Elevator.x) + Mathf.Abs(y - L.Elevator.y) <= 1) continue;
                    if (L.HasLake && Mathf.Abs(x - L.LakeStand.x) + Mathf.Abs(y - L.LakeStand.y) <= 0) continue;
                    L.NodeSlots.Add(c);
                }
            }
            return L;
        }

        private static Vector2Int Center(RectInt r) => new Vector2Int(r.x + r.width / 2, r.y + r.height / 2);

        private static void Carve(MineFloorLayout L, RectInt r)
        {
            for (int x = r.x; x < r.xMax; x++)
                for (int y = r.y; y < r.yMax; y++)
                    if (L.InBounds(x, y)) L.Cells[x, y] = MineFloorLayout.Floor;
        }

        private static void Corridor(MineFloorLayout L, Vector2Int a, Vector2Int b, bool horizontalFirst)
        {
            var p = a;
            void Mark(Vector2Int c)
            {
                if (c.x <= 0 || c.y <= 0 || c.x >= L.Size - 1 || c.y >= L.Size - 1) return;
                if (L.Cells[c.x, c.y] == MineFloorLayout.Wall) L.Cells[c.x, c.y] = MineFloorLayout.Floor;
            }
            Mark(p);
            if (horizontalFirst)
            {
                while (p.x != b.x) { p.x += p.x < b.x ? 1 : -1; Mark(p); }
                while (p.y != b.y) { p.y += p.y < b.y ? 1 : -1; Mark(p); }
            }
            else
            {
                while (p.y != b.y) { p.y += p.y < b.y ? 1 : -1; Mark(p); }
                while (p.x != b.x) { p.x += p.x < b.x ? 1 : -1; Mark(p); }
            }
        }

        private static int[,] Bfs(MineFloorLayout L, Vector2Int start)
        {
            var d = new int[L.Size, L.Size];
            for (int x = 0; x < L.Size; x++) for (int y = 0; y < L.Size; y++) d[x, y] = -1;
            var q = new Queue<Vector2Int>();
            d[start.x, start.y] = 0;
            q.Enqueue(start);
            var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (q.Count > 0)
            {
                var c = q.Dequeue();
                foreach (var dir in dirs)
                {
                    var nb = c + dir;
                    if (!L.InBounds(nb.x, nb.y) || L.Cells[nb.x, nb.y] == MineFloorLayout.Wall || d[nb.x, nb.y] >= 0) continue;
                    d[nb.x, nb.y] = d[c.x, c.y] + 1;
                    q.Enqueue(nb);
                }
            }
            return d;
        }
    }
}
