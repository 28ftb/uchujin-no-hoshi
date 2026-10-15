using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Housing
{
    /// <summary>
    /// 家の間取り・格子・配置可否（家具仕様 §3, §5）。GameObject を作らない純粋な計算だけを置く。
    /// 家の中は屋外から離れた座標（HousingConst.HouseOrigin）に、部屋を横一列に並べる。
    /// 2階・ロフトと地下室は別の列に置き、階段でつなぐ（§3 増築項目）。
    /// </summary>
    public static class HouseGeometry
    {
        public const float DoorZ0 = 1.0f;
        public const float DoorZ1 = 2.3f;
        public const int StairCells = 3;

        public static int RowOf(RoomKind k) => k == RoomKind.Loft ? 1 : k == RoomKind.Basement ? 2 : 0;

        public static RoomData GetRoom(HousingPlanetState st, int index)
        {
            if (st == null || st.rooms == null) return null;
            foreach (var r in st.rooms) if (r.index == index) return r;
            return null;
        }

        /// <summary>部屋の床の左下（x最小・z最小）のワールド座標。</summary>
        public static Vector3 RoomOrigin(HousingPlanetState st, RoomData room)
        {
            var baseOrigin = st.useCustomOrigin ? st.customOrigin : HousingConst.HouseOrigin;
            int row = RowOf(room.kind);
            float x = 0f;
            foreach (var r in st.rooms)
            {
                if (r.index == room.index) break;
                if (RowOf(r.kind) == row) x += r.widthCells * HousingConst.Cell + HousingConst.WallThickness;
            }
            float z = row == 1 ? HousingConst.LoftRowOffsetZ : row == 2 ? HousingConst.BasementRowOffsetZ : 0f;
            return baseOrigin + new Vector3(x, 0f, z);
        }

        public static RoomData LeftNeighbor(HousingPlanetState st, RoomData room)
        {
            RoomData prev = null;
            foreach (var r in st.rooms)
            {
                if (r.index == room.index) return prev;
                if (RowOf(r.kind) == RowOf(room.kind)) prev = r;
            }
            return null;
        }

        public static RoomData RightNeighbor(HousingPlanetState st, RoomData room)
        {
            bool found = false;
            foreach (var r in st.rooms)
            {
                if (found && RowOf(r.kind) == RowOf(room.kind)) return r;
                if (r.index == room.index) found = true;
            }
            return null;
        }

        /// <summary>その列の最初の部屋（階段の到着先）。</summary>
        public static RoomData FirstOfRow(HousingPlanetState st, int row)
        {
            foreach (var r in st.rooms) if (RowOf(r.kind) == row) return r;
            return null;
        }

        public static int CountRow(HousingPlanetState st, int row)
        {
            int n = 0;
            foreach (var r in st.rooms) if (RowOf(r.kind) == row) n++;
            return n;
        }

        /// <summary>足跡（回転後の幅・奥行き）。</summary>
        public static void Footprint(FurnitureSpec spec, int rot, out int fw, out int fd)
        {
            int w = Mathf.Max(1, spec.w);
            int d = spec.surface == FurnitureSurface.Wall ? 1 : Mathf.Max(1, spec.d);
            if ((rot & 1) == 1) { fw = d; fd = w; }
            else { fw = w; fd = d; }
        }

        // ------------------------------------------------------------
        // 予約された格子（階段・入口の前）
        // ------------------------------------------------------------

        public static bool IsReserved(HousingPlanetState st, RoomData room, int cx, int cz)
        {
            // 入口の前（最初の部屋の手前中央）と留守番マネピの場所
            var first = st.rooms.Count > 0 ? st.rooms[0] : null;
            if (first != null && room.index == first.index)
            {
                int mid = room.widthCells / 2;
                if (cz <= 1 && cx >= mid - 2 && cx <= mid + 4) return true;
                // 2階への階段（奥の左）・地下への階段（奥の右）
                if (CountRow(st, 1) > 0 && cx < StairCells && cz >= room.depthCells - StairCells) return true;
                if (CountRow(st, 2) > 0 && cx >= room.widthCells - StairCells && cz >= room.depthCells - StairCells) return true;
            }
            // 2階・地下の到着地点（その列の最初の部屋の奥の左）
            int row = RowOf(room.kind);
            if (row != 0)
            {
                var f = FirstOfRow(st, row);
                if (f != null && f.index == room.index && cx < StairCells && cz >= room.depthCells - StairCells) return true;
            }
            // 隣の部屋への出入口の前
            if (LeftNeighbor(st, room) != null && cx == 0 && InDoorBand(cz)) return true;
            if (RightNeighbor(st, room) != null && cx == room.widthCells - 1 && InDoorBand(cz)) return true;
            return false;
        }

        private static bool InDoorBand(int cz)
        {
            float z0 = cz * HousingConst.Cell, z1 = z0 + HousingConst.Cell;
            return z1 > DoorZ0 && z0 < DoorZ1;
        }

        /// <summary>水辺の部屋の水場（奥の1/3）。</summary>
        public static bool IsPoolCell(RoomData room, int cx, int cz)
        {
            if (room.kind != RoomKind.Waterside) return false;
            return cz >= room.depthCells - Mathf.Max(3, room.depthCells / 3);
        }

        // ------------------------------------------------------------
        // ワールド座標
        // ------------------------------------------------------------

        public static Vector3 CellToWorldInRoom(HousingPlanetState st, RoomData room, float cx, float cz)
        {
            return RoomOrigin(st, room) + new Vector3(cx * HousingConst.Cell, 0f, cz * HousingConst.Cell);
        }

        /// <summary>ワールド座標 → 部屋と格子。どの部屋にも入らなければ false。</summary>
        public static bool WorldToRoomCell(HousingPlanetState st, Vector3 p, out RoomData room, out int cx, out int cz)
        {
            room = null;
            cx = cz = 0;
            if (st == null) return false;
            foreach (var r in st.rooms)
            {
                var o = RoomOrigin(st, r);
                float lx = p.x - o.x, lz = p.z - o.z;
                if (lx < 0 || lz < 0) continue;
                if (lx >= r.widthCells * HousingConst.Cell || lz >= r.depthCells * HousingConst.Cell) continue;
                room = r;
                cx = Mathf.FloorToInt(lx / HousingConst.Cell);
                cz = Mathf.FloorToInt(lz / HousingConst.Cell);
                return true;
            }
            return false;
        }

        // ---- 庭（家の横の決められた範囲 §10）----

        public static bool GardenFrame(WorldLayout layout, out Vector3 origin, out Quaternion rot)
        {
            origin = Vector3.zero;
            rot = Quaternion.identity;
            if (layout == null) return false;
            var f = layout.GetFacility(FacilityType.PlayerHouse);
            if (f != null)
            {
                rot = Quaternion.Euler(0f, f.rotationY, 0f);
                // 家の右側（ローカル +x）に、家の前面と揃えて庭を取る【仮】
                origin = f.position + rot * new Vector3(f.size.x * 0.5f + 1.5f, 0f, -f.size.z * 0.5f);
            }
            else
            {
                origin = layout.PlayerSpawn + new Vector3(4f, 0f, -4f);
            }
            return true;
        }

        public static Vector3 GardenCellToWorld(WorldLayout layout, float cx, float cz)
        {
            GardenFrame(layout, out var o, out var r);
            var p = o + r * new Vector3(cx * HousingConst.Cell, 0f, cz * HousingConst.Cell);
            return layout != null ? layout.OnGround(p) : p;
        }

        public static bool WorldToGardenCell(WorldLayout layout, HousingPlanetState st, Vector3 p, out int cx, out int cz)
        {
            cx = cz = 0;
            if (!GardenFrame(layout, out var o, out var r)) return false;
            var local = Quaternion.Inverse(r) * (p - o);
            if (local.x < 0 || local.z < 0) return false;
            cx = Mathf.FloorToInt(local.x / HousingConst.Cell);
            cz = Mathf.FloorToInt(local.z / HousingConst.Cell);
            return cx < st.gardenWidthCells && cz < st.gardenDepthCells;
        }

        public static float GardenYaw(WorldLayout layout)
        {
            GardenFrame(layout, out _, out var r);
            return r.eulerAngles.y;
        }

        /// <summary>家具の置かれる中心位置（床・地面の高さ）と向き。</summary>
        public static Vector3 FurniturePosition(HousingPlanetState st, WorldLayout layout, PlacedFurniture f, FurnitureSpec spec, out float yaw)
        {
            Footprint(spec, f.rot, out int fw, out int fd);
            float cx = f.gx + fw * 0.5f, cz = f.gz + fd * 0.5f;
            if (f.location == HousingConst.LocGarden)
            {
                yaw = GardenYaw(layout) + f.rot * 90f;
                var p = GardenCellToWorld(layout, cx, cz);
                if (spec.surface == FurnitureSurface.Tabletop)
                {
                    var host = FindHost(st, f, spec);
                    if (host != null) p.y += Resolve(host).height;
                }
                return p;
            }
            yaw = f.rot * 90f;
            var room = GetRoom(st, f.room);
            if (room == null) return HousingConst.HouseOrigin;
            var pos = CellToWorldInRoom(st, room, cx, cz);
            switch (spec.surface)
            {
                case FurnitureSurface.Wall:
                    pos = WallMountPosition(st, room, f, spec, fw, fd);
                    break;
                case FurnitureSurface.Ceiling:
                    pos.y = HousingConst.WallHeight - spec.height - 0.05f;
                    break;
                case FurnitureSurface.Tabletop:
                    var host = FindHost(st, f, spec);
                    if (host != null) pos.y += Resolve(host).height;
                    break;
            }
            return pos;
        }

        private static Vector3 WallMountPosition(HousingPlanetState st, RoomData room, PlacedFurniture f, FurnitureSpec spec, int fw, int fd)
        {
            float cx = f.gx + fw * 0.5f, cz = f.gz + fd * 0.5f;
            float y = Mathf.Max(1.35f, spec.height * 0.5f + 0.35f);
            switch (f.rot & 3)
            {
                case 2: cz = room.depthCells; break;          // 奥の壁（-z を向く）
                case 0: cz = 0f; break;                       // 手前の壁（+z を向く）
                case 1: cx = 0f; break;                       // 左の壁（+x を向く）
                default: cx = room.widthCells; break;         // 右の壁（-x を向く）
            }
            var p = CellToWorldInRoom(st, room, cx, cz);
            p.y = y;
            return p;
        }

        /// <summary>壁掛け家具の向き：カーソルに近い壁を選ぶ。</summary>
        public static int NearestWallRot(RoomData room, int cx, int cz)
        {
            int dBack = room.depthCells - 1 - cz, dFront = cz, dLeft = cx, dRight = room.widthCells - 1 - cx;
            int min = Mathf.Min(Mathf.Min(dBack, dFront), Mathf.Min(dLeft, dRight));
            if (min == dBack) return 2;
            if (min == dLeft) return 1;
            if (min == dRight) return 3;
            return 0;
        }

        /// <summary>壁掛け家具を壁に沿わせた格子位置へ補正する。</summary>
        public static void SnapToWall(RoomData room, FurnitureSpec spec, int rot, ref int gx, ref int gz)
        {
            Footprint(spec, rot, out int fw, out int fd);
            switch (rot & 3)
            {
                case 2: gz = room.depthCells - 1; break;
                case 0: gz = 0; break;
                case 1: gx = 0; break;
                default: gx = room.widthCells - 1; break;
            }
            gx = Mathf.Clamp(gx, 0, Mathf.Max(0, room.widthCells - fw));
            gz = Mathf.Clamp(gz, 0, Mathf.Max(0, room.depthCells - fd));
        }

        // ------------------------------------------------------------
        // 配置の可否
        // ------------------------------------------------------------

        public static FurnitureSpec Resolve(PlacedFurniture f) => FurnitureCatalog.Resolve(ItemRegistry.GetOrPlaceholder(f.itemId));

        private static bool Overlaps(int ax, int az, int aw, int ad, int bx, int bz, int bw, int bd)
        {
            return ax < bx + bw && bx < ax + aw && az < bz + bd && bz < az + ad;
        }

        public static PlacedFurniture FindHost(HousingPlanetState st, PlacedFurniture f, FurnitureSpec spec)
        {
            Footprint(spec, f.rot, out int fw, out int fd);
            return FindHostAt(st, f.location, f.room, f.gx, f.gz, fw, fd, f.instanceId);
        }

        public static PlacedFurniture FindHostAt(HousingPlanetState st, string location, int room, int gx, int gz, int fw, int fd, string ignore)
        {
            foreach (var o in st.furniture)
            {
                if (o.instanceId == ignore || o.location != location || o.room != room) continue;
                var os = Resolve(o);
                if (!os.tabletopHost) continue;
                Footprint(os, o.rot, out int ow, out int od);
                if (gx >= o.gx && gz >= o.gz && gx + fw <= o.gx + ow && gz + fd <= o.gz + od) return o;
            }
            return null;
        }

        public static bool HasItemsOnTop(HousingPlanetState st, PlacedFurniture host)
        {
            var hs = Resolve(host);
            if (!hs.tabletopHost) return false;
            Footprint(hs, host.rot, out int hw, out int hd);
            foreach (var o in st.furniture)
            {
                if (o == host || o.location != host.location || o.room != host.room) continue;
                var os = Resolve(o);
                if (os.surface != FurnitureSurface.Tabletop) continue;
                Footprint(os, o.rot, out int ow, out int od);
                if (Overlaps(o.gx, o.gz, ow, od, host.gx, host.gz, hw, hd)) return true;
            }
            return false;
        }

        /// <summary>そこに置けるか。理由は UI 表示用（日本語・英語）。</summary>
        public static bool CanPlace(HousingPlanetState st, WorldLayout layout, string location, int roomIndex, FurnitureSpec spec,
            int gx, int gz, int rot, string ignoreInstance, out string reasonJa, out string reasonEn)
        {
            reasonJa = reasonEn = null;
            if (st == null || spec == null) { reasonJa = "置けません"; reasonEn = "Cannot place"; return false; }
            Footprint(spec, rot, out int fw, out int fd);

            if (location == HousingConst.LocGarden)
            {
                if (!spec.allowOutdoor && !spec.outdoorOnly) { reasonJa = "家の中で使う物です"; reasonEn = "This is for indoors"; return false; }
                if (spec.surface == FurnitureSurface.Wall || spec.surface == FurnitureSurface.Ceiling) { reasonJa = "庭には掛ける場所がありません"; reasonEn = "Nothing to hang it on outside"; return false; }
                if (gx < 0 || gz < 0 || gx + fw > st.gardenWidthCells || gz + fd > st.gardenDepthCells) { reasonJa = "庭の範囲の外です"; reasonEn = "Outside the garden"; return false; }
                if (layout != null)
                {
                    for (int x = gx; x < gx + fw; x++)
                        for (int z = gz; z < gz + fd; z++)
                            if (layout.IsWater(GardenCellToWorld(layout, x + 0.5f, z + 0.5f)) && !spec.allowWater) { reasonJa = "水の上には置けません"; reasonEn = "Cannot place on water"; return false; }
                }
            }
            else if (location == HousingConst.LocHouse)
            {
                var room = GetRoom(st, roomIndex);
                if (room == null) { reasonJa = "部屋がありません"; reasonEn = "No room"; return false; }
                if (spec.outdoorOnly && room.kind != RoomKind.Terrace) { reasonJa = "庭やテラスに置く物です"; reasonEn = "This belongs in the garden or on a terrace"; return false; }
                if (spec.surface == FurnitureSurface.Ceiling && room.kind == RoomKind.Terrace) { reasonJa = "テラスには天井がありません"; reasonEn = "Terraces have no ceiling"; return false; }
                if (spec.surface == FurnitureSurface.Wall && room.kind == RoomKind.Terrace) { reasonJa = "テラスには壁がありません"; reasonEn = "Terraces have no walls"; return false; }
                if (gx < 0 || gz < 0 || gx + fw > room.widthCells || gz + fd > room.depthCells) { reasonJa = "部屋に収まりません"; reasonEn = "Does not fit in the room"; return false; }
                if (spec.surface == FurnitureSurface.Wall)
                {
                    bool ok;
                    switch (rot & 3)
                    {
                        case 2: ok = gz + fd == room.depthCells; break;
                        case 0: ok = gz == 0; break;
                        case 1: ok = gx == 0; break;
                        default: ok = gx + fw == room.widthCells; break;
                    }
                    if (!ok) { reasonJa = "壁に沿わせてください"; reasonEn = "Must be against a wall"; return false; }
                    if (room.kind == RoomKind.Basement && spec.use == FurnitureUse.Window) { reasonJa = "地下室に窓は付けられません"; reasonEn = "No windows in the basement"; return false; }
                }
                if (spec.surface == FurnitureSurface.Floor || spec.surface == FurnitureSurface.Rug || spec.surface == FurnitureSurface.Tabletop)
                {
                    for (int x = gx; x < gx + fw; x++)
                        for (int z = gz; z < gz + fd; z++)
                        {
                            if (IsReserved(st, room, x, z)) { reasonJa = "通り道や階段の前には置けません"; reasonEn = "Keep the doorway and stairs clear"; return false; }
                            if (IsPoolCell(room, x, z) && !spec.allowWater) { reasonJa = "水場には置けません"; reasonEn = "Not in the water"; return false; }
                        }
                }
            }
            else
            {
                reasonJa = "ここには置けません";
                reasonEn = "Cannot place here";
                return false;
            }

            if (spec.surface == FurnitureSurface.Tabletop)
            {
                var host = FindHostAt(st, location, roomIndex, gx, gz, fw, fd, ignoreInstance);
                if (host == null) { reasonJa = "机やテーブルの上に置く物です"; reasonEn = "Place it on a table or desk"; return false; }
            }

            foreach (var o in st.furniture)
            {
                if (o.instanceId == ignoreInstance || o.location != location) continue;
                if (location == HousingConst.LocHouse && o.room != roomIndex) continue;
                var os = Resolve(o);
                if (!SameLayer(spec.surface, os.surface)) continue;
                Footprint(os, o.rot, out int ow, out int od);
                if (spec.surface == FurnitureSurface.Wall && ((o.rot & 3) != (rot & 3))) continue;
                if (Overlaps(gx, gz, fw, fd, o.gx, o.gz, ow, od))
                {
                    reasonJa = "ほかの家具と重なっています";
                    reasonEn = "Overlaps other furniture";
                    return false;
                }
            }
            return true;
        }

        private static bool SameLayer(FurnitureSurface a, FurnitureSurface b)
        {
            if (a == FurnitureSurface.Tabletop || b == FurnitureSurface.Tabletop) return a == b;
            return a == b;
        }

        /// <summary>部屋の中で、置ける最初の位置を探す（初期家具・住民の家）。</summary>
        public static bool FindFreeSpot(HousingPlanetState st, int roomIndex, FurnitureSpec spec, int rot, out int gx, out int gz)
        {
            gx = gz = 0;
            var room = GetRoom(st, roomIndex);
            if (room == null) return false;
            for (int z = room.depthCells - 1; z >= 0; z--)
                for (int x = 0; x < room.widthCells; x++)
                {
                    int tx = x, tz = z;
                    if (spec.surface == FurnitureSurface.Wall) SnapToWall(room, spec, rot, ref tx, ref tz);
                    if (CanPlace(st, null, HousingConst.LocHouse, roomIndex, spec, tx, tz, rot, null, out _, out _))
                    {
                        gx = tx;
                        gz = tz;
                        return true;
                    }
                }
            return false;
        }

        /// <summary>家具の「使う位置」（座る・眺める位置）。訪問者・生き物が向かう。</summary>
        public static Vector3 UsePoint(Vector3 center, float floorY, float yaw, FurnitureSpec spec)
        {
            var fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            switch (spec.use)
            {
                case FurnitureUse.Sit:
                case FurnitureUse.Sleep:
                    return center;
                default:
                    float depth = Mathf.Max(spec.w, spec.d) * HousingConst.Cell * 0.5f + 0.6f;
                    return new Vector3(center.x, floorY, center.z) + fwd * depth;
            }
        }
    }
}
