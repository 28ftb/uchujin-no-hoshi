using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>地域・道中マップの地表の塗り分け（色と、自然物を置けるかの判定に使う）。</summary>
    public enum FieldPaint : byte { Grass = 0, Meadow, Flowers, Forest, WetForest, Road, Trail, Sand, Mud, Rock }

    /// <summary>地域・道中マップの水の種類。</summary>
    public enum FieldWater : byte { None = 0, Stream = 1, Pond = 2, Lake = 3 }

    /// <summary>マップの中の小川（点の列と、点ごとの水面・半径・深さ）。座標はマップのローカル（m）。</summary>
    public sealed class FieldStream
    {
        public string Id;
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Surface = new List<float>();
        public readonly List<float> Radius = new List<float>();
        public readonly List<float> Depth = new List<float>();
        /// <summary>歩いて渡れる浅瀬（飛び石）の点の番号。</summary>
        public readonly List<int> Fords = new List<int>();
        /// <summary>小さな滝（水面が段で落ちる所）の、落ちた先＝滝つぼの点の番号。</summary>
        public readonly List<int> Falls = new List<int>();
        /// <summary>冷たい湧水の淵の点の番号（無ければ -1）。</summary>
        public int SpringIndex = -1;
    }

    /// <summary>マップの中の目印の場所（寄り道ポケット・東屋・隠れた場所など。検証と配置の確認に使う）。</summary>
    public sealed class FieldPlace
    {
        public string Id;
        public string NameJa;
        public Vector2 Local;
        /// <summary>寄り道ポケットか（§3.3）。</summary>
        public bool Pocket;
        /// <summary>隠れた場所か（§3.3）。</summary>
        public bool Hidden;
    }

    public enum FieldStructureKind { Gazebo, Pier, SteppingStone, Bench, Crystal, Signpost, Lantern, Ruin, CaveMouth, Waterfall }

    /// <summary>マップに置く小さな建て物（東屋・桟橋・飛び石・道標など）。座標はマップのローカル。</summary>
    public sealed class FieldStructure
    {
        public FieldStructureKind Kind;
        public Vector2 Local;
        public float Y;
        public float Yaw;
        public float Size = 1f;
        /// <summary>道標の行き先（マップの ID）と向き。</summary>
        public string Destination;
    }

    /// <summary>マップの境目のゲート（歩いて入るだけでとなりのマップへ移る。§10.1）。</summary>
    public sealed class FieldGateInfo
    {
        public string Id;
        public string MapId;
        public string OtherMapId;
        /// <summary>このゲートの、外（となりのマップ）へ向かう向き（Unity の Y 回転、度）。</summary>
        public float Yaw;
        /// <summary>ゲートの位置（ワールド座標。地面の上）。</summary>
        public Vector3 Position;
        /// <summary>となりのマップからこのゲートを通って来た時に立つ位置と向き（ワールド座標）。</summary>
        public Vector3 Arrival;
        public float ArrivalYaw;
        public string PartnerId => "gate." + OtherMapId + "." + MapId;
        public string AreaId => MapId == StarSkeleton.Village ? AreaIds.Overworld : AreaIds.FieldMap(MapId);
        public string OtherAreaId => OtherMapId == StarSkeleton.Village ? AreaIds.Overworld : AreaIds.FieldMap(OtherMapId);
    }

    /// <summary>
    /// 村の外の地域マップ・道中マップ1枚（生成バージョン3の試作。マップ設計 v0.3 §2・§3・§10.1）。
    /// 屋外（村）から離れた座標 Origin に置く。高さグリッドはマップのローカル座標（Origin が (0,0)）で持ち、
    /// 遊べる範囲 [0, Width] × [0, Depth] のまわりに見た目だけの縁（Margin）を付ける。高さは星全体でそろえた絶対値。
    /// </summary>
    public sealed class FieldMap
    {
        public const float Step = 2f;
        public const float Margin = 36f;

        public string Id;
        public string NameJa;
        public string NameEn;
        public SkeletonMapKind Kind;
        public int Type;
        public Vector3 Origin;
        public float Width;
        public float Depth;
        public int NX, NZ;
        public float[] H;
        public float[] WaterY;
        public FieldWater[] Water;
        public FieldPaint[] Paint;
        public WorldPalette Palette;
        public int Attempt;
        public bool Valid;

        /// <summary>本道（ゲートからゲート・行き先まで）。ローカル座標の点の列。</summary>
        public readonly List<Vector2> Road = new List<Vector2>();
        /// <summary>脇道（細い道）。</summary>
        public readonly List<List<Vector2>> Trails = new List<List<Vector2>>();
        /// <summary>枝道（本道と同じ幅。地域マップで本道から別のゲートへ向かう道など）。</summary>
        public readonly List<List<Vector2>> Branches = new List<List<Vector2>>();
        /// <summary>本道の坂の目安（道を均す時の上限。坂道の道中は少しきつい）。</summary>
        public float RoadGrade = 0.08f;
        public readonly List<FieldStream> Streams = new List<FieldStream>();
        public readonly List<PondInfo> Ponds = new List<PondInfo>();
        public LakeInfo Lake;
        public readonly List<FieldPlace> Places = new List<FieldPlace>();
        public readonly List<FieldStructure> Structures = new List<FieldStructure>();
        public readonly List<FieldGateInfo> Gates = new List<FieldGateInfo>();
        /// <summary>自然物（位置はワールド座標）。</summary>
        public readonly List<NatureItem> Nature = new List<NatureItem>();
        public readonly List<string> Notes = new List<string>();

        public string AreaId => AreaIds.FieldMap(Id);

        public void Allocate()
        {
            NX = Mathf.RoundToInt((Width + Margin * 2f) / Step) + 1;
            NZ = Mathf.RoundToInt((Depth + Margin * 2f) / Step) + 1;
            int n = NX * NZ;
            H = new float[n];
            WaterY = new float[n];
            Water = new FieldWater[n];
            Paint = new FieldPaint[n];
            for (int i = 0; i < n; i++) WaterY[i] = float.NaN;
        }

        public int Index(int ix, int iz) => iz * NX + ix;
        public float XOf(int ix) => -Margin + ix * Step;
        public float ZOf(int iz) => -Margin + iz * Step;

        public void ToGrid(float lx, float lz, out int ix, out int iz)
        {
            ix = Mathf.Clamp(Mathf.RoundToInt((lx + Margin) / Step), 0, NX - 1);
            iz = Mathf.Clamp(Mathf.RoundToInt((lz + Margin) / Step), 0, NZ - 1);
        }

        public Vector2 ToLocal(float wx, float wz) => new Vector2(wx - Origin.x, wz - Origin.z);
        public Vector3 ToWorld(Vector2 local, float y) => new Vector3(Origin.x + local.x, y, Origin.z + local.y);
        public Vector3 OnGroundWorld(Vector2 local) => ToWorld(local, SampleTerrain(local.x, local.y));

        /// <summary>ワールド座標がこのマップ（見た目の縁を含む）の中か。</summary>
        public bool ContainsWorld(float wx, float wz)
        {
            var l = ToLocal(wx, wz);
            return l.x >= -Margin && l.x <= Width + Margin && l.y >= -Margin && l.y <= Depth + Margin;
        }

        public bool InsidePlayable(Vector2 l, float inset = 0f) => l.x >= inset && l.x <= Width - inset && l.y >= inset && l.y <= Depth - inset;

        /// <summary>地形の高さ（ローカル座標、双一次補間）。</summary>
        public float SampleTerrain(float lx, float lz)
        {
            float gx = Mathf.Clamp((lx + Margin) / Step, 0f, NX - 1.001f);
            float gz = Mathf.Clamp((lz + Margin) / Step, 0f, NZ - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            int i = z0 * NX + x0;
            float a = H[i], b = H[i + 1], c = H[i + NX], d = H[i + NX + 1];
            return (a * (1 - fx) + b * fx) * (1 - fz) + (c * (1 - fx) + d * fx) * fz;
        }

        public float WaterSurfaceAt(float lx, float lz)
        {
            ToGrid(lx, lz, out int ix, out int iz);
            return WaterY[Index(ix, iz)];
        }

        public FieldWater WaterAt(float lx, float lz)
        {
            ToGrid(lx, lz, out int ix, out int iz);
            int i = Index(ix, iz);
            return float.IsNaN(WaterY[i]) ? FieldWater.None : Water[i];
        }

        /// <summary>水深（水面 − 地形。水でなければ 0）。</summary>
        public float WaterDepth(float lx, float lz)
        {
            float w = WaterSurfaceAt(lx, lz);
            if (float.IsNaN(w)) return 0f;
            return Mathf.Max(0f, w - SampleTerrain(lx, lz));
        }

        public bool WaterNear(float lx, float lz, float radius)
        {
            int r = Mathf.CeilToInt(radius / Step);
            ToGrid(lx, lz, out int cx, out int cz);
            for (int dz = -r; dz <= r; dz++)
            {
                int iz = cz + dz;
                if (iz < 0 || iz >= NZ) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    int ix = cx + dx;
                    if (ix < 0 || ix >= NX) continue;
                    if ((dx * dx + dz * dz) * Step * Step > radius * radius) continue;
                    if (!float.IsNaN(WaterY[Index(ix, iz)])) return true;
                }
            }
            return false;
        }

        public FieldPaint PaintAt(float lx, float lz)
        {
            ToGrid(lx, lz, out int ix, out int iz);
            return Paint[Index(ix, iz)];
        }

        public Vector3 Normal(int ix, int iz)
        {
            int xl = Mathf.Max(0, ix - 1), xr = Mathf.Min(NX - 1, ix + 1);
            int zl = Mathf.Max(0, iz - 1), zr = Mathf.Min(NZ - 1, iz + 1);
            float dx = (H[Index(xr, iz)] - H[Index(xl, iz)]) / ((xr - xl) * Step);
            float dz = (H[Index(ix, zr)] - H[Index(ix, zl)]) / ((zr - zl) * Step);
            return new Vector3(-dx, 1f, -dz).normalized;
        }

        /// <summary>斜面の角度（度）。</summary>
        public float Slope(float lx, float lz)
        {
            ToGrid(lx, lz, out int ix, out int iz);
            return Vector3.Angle(Normal(ix, iz), Vector3.up);
        }

        public FieldGateInfo GetGate(string id)
        {
            foreach (var g in Gates) if (g.Id == id) return g;
            return null;
        }

        public FieldPlace GetPlace(string id)
        {
            foreach (var p in Places) if (p.Id == id) return p;
            return null;
        }

        /// <summary>本道の長さ（m）。</summary>
        public float RoadLength()
        {
            float len = 0f;
            for (int i = 1; i < Road.Count; i++) len += Vector2.Distance(Road[i - 1], Road[i]);
            return len;
        }

        /// <summary>点から本道・脇道までの距離（m）。</summary>
        public float DistToPaths(Vector2 p, bool includeTrails = true)
        {
            float best = DistToPolyline(p, Road);
            foreach (var b in Branches) best = Mathf.Min(best, DistToPolyline(p, b));
            if (includeTrails)
                foreach (var t in Trails) best = Mathf.Min(best, DistToPolyline(p, t));
            return best;
        }

        public static float DistToPolyline(Vector2 p, List<Vector2> line)
        {
            float best = float.MaxValue;
            for (int i = 1; i < line.Count; i++) best = Mathf.Min(best, GeoUtil.DistToSegment(p, line[i - 1], line[i], out _));
            return best;
        }
    }
}
