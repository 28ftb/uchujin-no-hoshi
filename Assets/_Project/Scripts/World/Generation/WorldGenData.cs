using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>平らに均す区画の種類（地図・地表の色分けにも使う）。</summary>
    public enum PadKind
    {
        Plaza,
        Building,
        Farm,
        HousePlot,
        Crater,
        Lookout,
        MineMouth,
        Generic
    }

    /// <summary>平らに均す区画（建物の土台・広場・農場など）。</summary>
    public sealed class PadInfo
    {
        public PadKind Kind;
        public Vector2 Center;
        /// <summary>矩形の半分の大きさ（ローカルX, ローカルZ）。円なら x を半径に使う。</summary>
        public Vector2 HalfSize;
        public float Yaw;
        public bool Circle;
        public float TargetY;
        public float Blend = 4f;
        /// <summary>道路が通れない（建物本体）。</summary>
        public bool BlocksRoads;
        public string FacilityId;

        /// <summary>区画内なら 1、ぼかし帯で 0〜1、外なら 0。</summary>
        public float Weight(float x, float z)
        {
            float d = SignedDistance(x, z);
            if (d <= 0f) return 1f;
            if (d >= Blend) return 0f;
            return 1f - GeoUtil.SmoothStep(0f, Blend, d);
        }

        /// <summary>区画の縁からの距離（内側は負）。</summary>
        public float SignedDistance(float x, float z)
        {
            var p = new Vector2(x, z) - Center;
            if (Circle) return p.magnitude - HalfSize.x;
            // ワールド→ローカル（Yaw の逆回転。Unity の Y 回転：右=(cos,-sin) 前=(sin,cos)）
            float cs = Mathf.Cos(Yaw * Mathf.Deg2Rad), sn = Mathf.Sin(Yaw * Mathf.Deg2Rad);
            float lx = p.x * cs - p.y * sn;
            float lz = p.x * sn + p.y * cs;
            float qx = Mathf.Abs(lx) - HalfSize.x;
            float qz = Mathf.Abs(lz) - HalfSize.y;
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qz, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(qx, qz), 0f);
            return outside + inside;
        }

        public float Radius => Circle ? HalfSize.x : HalfSize.magnitude;
    }

    public sealed class RiverPath
    {
        public int WaterIndex;
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Surface = new List<float>();
        public readonly List<float> Radius = new List<float>();
        public bool EndsInSea;
        public int EndPondIndex = -1;
        public int WaterfallIndex = -1;
        public float WaterfallDrop;
        /// <summary>点ごとの川底の追加の深さ（生成バージョン2：滝つぼを深く掘る）。空なら0。</summary>
        public readonly List<float> ExtraDepth = new List<float>();
        /// <summary>歩いて渡れる浅瀬（瀬）の点の番号（生成バージョン2。水深0.6m以下）。</summary>
        public readonly List<int> Fords = new List<int>();
        public float ExtraDepthAt(int i) => i >= 0 && i < ExtraDepth.Count ? ExtraDepth[i] : 0f;
    }

    /// <summary>
    /// 湖（生成バージョン2。マップ設計 v0.2 §2.3）。岸は角度ごとに少し波打ち、湖底は岸からの距離と方角（遠浅・急深）で深さが変わる。
    /// 最深部の近くまで細い岬（陸）が突き出し、そこから最深部を狙える（§2.3 P17）。
    /// </summary>
    public sealed class LakeInfo
    {
        public int WaterIndex;
        public Vector2 Center;
        /// <summary>基準の半径（岸までの距離はこれを角度ごとに揺らす）。</summary>
        public float BaseRadius;
        public float A1, P1, A2, P2;
        public float Surface;
        public float MaxDepth;
        /// <summary>方角（ラジアン）：岬・遠浅・急深・泥底・砂泥底・静かな岸。</summary>
        public float SpitAngle, ShelfAngle, SteepAngle, MudAngle, SandMudAngle, QuietAngle;
        public Vector2 SpitBase, SpitTip;
        public const float SpitHalfWidth = 3.2f;
        /// <summary>小川の流れ込み口（濁り）。無ければ泥の入り江を濁りにする。</summary>
        public bool HasInlet;
        public Vector2 Inlet;

        public float RadiusAt(float angle) => BaseRadius * (1f + A1 * Mathf.Sin(angle + P1) + A2 * Mathf.Sin(2f * angle + P2));

        public static float Angle(Vector2 v) => Mathf.Atan2(v.y, v.x);

        public static float AngleDiff(float a, float b)
        {
            float d = Mathf.Repeat(a - b + Mathf.PI, Mathf.PI * 2f) - Mathf.PI;
            return Mathf.Abs(d);
        }

        public Vector2 Dir(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        /// <summary>岸からの割合（0=中心、1=岸）。</summary>
        public float K(Vector2 p)
        {
            var v = p - Center;
            float d = v.magnitude;
            if (d < 1e-3f) return 0f;
            return d / Mathf.Max(1f, RadiusAt(Angle(v)));
        }

        public bool InSpit(Vector2 p) => GeoUtil.DistToSegment(p, SpitBase, SpitTip, out _) < SpitHalfWidth;

        /// <summary>湖底の深さ（水面から）。遠浅の方角は岸から深くなるのが遅く、急深の方角は早い。</summary>
        public float DepthAt(Vector2 p)
        {
            float k = K(p);
            if (k >= 1f) return 0f;
            float ang = Angle(p - Center);
            float k0 = 0.38f;
            if (AngleDiff(ang, ShelfAngle) < 0.95f) k0 = 0.25f;
            else if (AngleDiff(ang, SteepAngle) < 0.8f) k0 = 0.6f;
            return 0.35f + (MaxDepth - 0.35f) * (1f - GeoUtil.SmoothStep(k0, 0.97f, k));
        }
    }

    /// <summary>滝の裏の洞（生成バージョン2。マップ設計 v0.2 §2.10）。入口は滝つぼの脇の崖。</summary>
    public sealed class WaterfallCaveInfo
    {
        /// <summary>入口（地上の立ち位置）。</summary>
        public Vector3 Entrance;
        /// <summary>入口が向く向き（洞から外へ）。</summary>
        public float Yaw;
    }

    public sealed class PondInfo
    {
        public int WaterIndex;
        public Vector2 Center;
        public float Radius;
        public float Surface;
    }

    /// <summary>道路の描画用の線分。</summary>
    public sealed class RoadSegment
    {
        public Vector2 A;
        public Vector2 B;
        public float HalfWidth = WorldTuning.RoadHalfWidth;
        /// <summary>村の中心部は石畳。</summary>
        public bool Paved;
        public bool Bridge;
        /// <summary>小道（静かな高台への道など。本道ではない：マップ設計 v0.2 §5.3）。</summary>
        public bool Trail;
    }

    public enum NatureKind
    {
        TreeRound,
        TreeTall,
        TreeMushroom,
        TreeLollipop,
        TreeCrystal,
        Bush,
        Rock,
        Boulder,
        Flower,
        Grass,
        Mushroom,
        GlowMushroom,
        FallenLog,
        Reed,
        Shell,
        Crystal
    }

    /// <summary>自然物1つ（シードから決定的に配置）。</summary>
    public struct NatureItem
    {
        public NatureKind Kind;
        public Vector3 Position;
        public float Yaw;
        public float Scale;
        public int Variant;
        public AreaType Area;
    }

    /// <summary>宇宙語の看板（"PLACE:"+facilityId を表示）。</summary>
    public sealed class SignInfo
    {
        public string Id;
        public string FacilityId;
        public Vector3 Position;
        public float Yaw;
        /// <summary>道標（矢印付き）。</summary>
        public bool IsSignpost;
        public List<string> Destinations = new List<string>();
        public List<float> DestinationYaws = new List<float>();
    }

    /// <summary>公共施設の整備予定地（村仕様 §11.2【提案】）。</summary>
    public sealed class ProjectSite
    {
        public string Id;
        public string NameJa;
        public string NameEn;
        public Vector3 Position;
        public float Yaw;
        public int Required;
        public string Kind;
    }

    public sealed class WaterfallInfo
    {
        public Vector3 Top;
        public Vector3 Bottom;
        public float Yaw;
        public float Width;
    }

    /// <summary>星ごとの配色（村仕様 §1.1「植生や配色」【提案】）。</summary>
    public sealed class WorldPalette
    {
        public Color Grass, GrassDark, GrassLight, ForestFloor, Meadow, Sand, Rock, RockDark, Dirt, Stone, Plaza, Soil, Mud;
        public Color River, Pond, Sea, DeepSea;
        public Color Trunk;
        public Color[] Leaves;
        public Color[] Flowers;
        public Color[] Mushrooms;
        public Color CrystalColor;
        public Color[] Roofs;
        public Color[] Walls;
        public Color SkyTint;
        public string NameJa;
    }

    /// <summary>生成結果一式（WorldLayout ＋ 見た目に必要な内部データ）。</summary>
    public sealed class GeneratedWorld
    {
        public WorldLayout Layout;
        public Heightfield Field;
        public TerrainShape Shape;
        public WorldPalette Palette;
        public readonly List<RiverPath> Rivers = new List<RiverPath>();
        public readonly List<PondInfo> Ponds = new List<PondInfo>();
        public readonly List<RoadSegment> RoadSegments = new List<RoadSegment>();
        public readonly List<PadInfo> Pads = new List<PadInfo>();
        public readonly List<NatureItem> Nature = new List<NatureItem>();
        public readonly List<SignInfo> Signs = new List<SignInfo>();
        public readonly List<ProjectSite> ProjectSites = new List<ProjectSite>();
        public readonly List<string> Notes = new List<string>();
        public WaterfallInfo Waterfall;
        /// <summary>生成バージョン（1 or 2）。</summary>
        public int Version = 1;
        /// <summary>湖（生成バージョン2）。</summary>
        public readonly List<LakeInfo> Lakes = new List<LakeInfo>();
        /// <summary>静かな高台（生成バージョン2）。</summary>
        public bool HasQuietHighland;
        public Vector2 QuietCenter;
        public float QuietRadius;
        /// <summary>静かな高台の池（W.Ponds の番号。無ければ -1）。</summary>
        public int HighlandPondIndex = -1;
        /// <summary>滝の裏の洞（生成バージョン2）。</summary>
        public WaterfallCaveInfo Cave;
        /// <summary>星の骨格（生成バージョン3。マップ設計 v0.3 §8.1）。</summary>
        public StarSkeleton Skeleton;
        /// <summary>村の端のゲート（生成バージョン3）。</summary>
        public readonly List<FieldGateInfo> VillageGates = new List<FieldGateInfo>();
        /// <summary>村のゲートの方角と、骨格の方角のずれ（度。検査用）。</summary>
        public float VillageGateAngleError;
        /// <summary>村の外の地域・道中マップ（生成バージョン3。試作では T1 花の野道と R2 湖の森）。</summary>
        public readonly List<FieldMap> FieldMaps = new List<FieldMap>();
        public bool HasPier;
        public Vector3 PierStart;
        public Vector3 PierEnd;
        public Rect FarmReserve;
        public Vector3 PlazaCenter;
        /// <summary>広場の目印の大きな木。</summary>
        public Vector3 PlazaLandmark;
        public int Attempt;
        public bool Valid;
        /// <summary>道路ノードのうち村の中心（石畳）に属するもの。</summary>
        public float PavedRadius = 42f;
    }
}
