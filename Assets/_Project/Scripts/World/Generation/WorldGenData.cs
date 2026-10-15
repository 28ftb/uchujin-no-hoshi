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
