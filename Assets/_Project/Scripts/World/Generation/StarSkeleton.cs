using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    public enum SkeletonMapKind { Region, Route }

    /// <summary>星の骨格の1マップ（地域マップか道中マップ。マップ設計 v0.3 §2・§3・§8.1）。</summary>
    public sealed class SkeletonMap
    {
        public string Id;
        public string NameJa;
        public string NameEn;
        public SkeletonMapKind Kind;
        /// <summary>マップの広さ（m）。道中は「本道の長さ × 幅」（ゲートは長さの両端。§3.1 の本道の長さに合わせる）。</summary>
        public Vector2 Size;
        /// <summary>マップの型（§8.2。0〜2）。</summary>
        public int Type;
        /// <summary>接続図の上の位置（村を原点にした方角と距離。画像と方角の決定に使う。単位は任意）。</summary>
        public Vector2 StarPos;
        /// <summary>この試作で実際にマップを作るか（試作では村・花の野道・湖の森だけ）。</summary>
        public bool Built;
        public readonly List<SkeletonGate> Gates = new List<SkeletonGate>();

        public string AreaId => Id == StarSkeleton.Village ? AreaIds.Overworld : AreaIds.FieldMap(Id);
    }

    /// <summary>マップの境目のゲート（両側のマップに1つずつ。高さは両側でそろえる）。</summary>
    public sealed class SkeletonGate
    {
        /// <summary>"gate.&lt;このマップ&gt;.&lt;相手のマップ&gt;"。</summary>
        public string Id;
        public string MapId;
        public string OtherMapId;
        /// <summary>相手のマップの対になるゲート。</summary>
        public string PartnerId => "gate." + OtherMapId + "." + MapId;
        /// <summary>ゲートの地面の高さ（m）。村側のゲートは村の地形を作った後に決まる（NaN）。</summary>
        public float Height = float.NaN;
        /// <summary>接続図の上の向き（このマップから相手のマップへ。ラジアン）。</summary>
        public float Bearing;
    }

    /// <summary>
    /// 星の骨格（マップ設計 v0.3 §8.1 段階1、シードの "world.gen3.skeleton"）。
    /// 5つの地域と6つの道中（T6 はシードで1本）の方角・つながり・ゲートの高さを、各マップの地形より先に決める。
    /// 地理の約束：山（滝の山）と海（浜と岬）は村の反対側、湖の森は山と村の脇のあいだ、星見の高原は山の奥。
    /// </summary>
    public sealed class StarSkeleton
    {
        public const string Village = "r1";
        public const string LakeForest = "r2";
        public const string WaterfallMountain = "r3";
        public const string BeachCape = "r4";
        public const string Highland = "r5";
        public const string FlowerPath = "t1";
        public const string MarshRiver = "t2";
        public const string StreamSlope = "t3";
        public const string MossValley = "t4";
        public const string Ridge = "t5";
        public const string SeaWindWood = "t6a";
        public const string CliffPath = "t6b";

        /// <summary>湖の森の湖の水面（m。§5）。</summary>
        public const float LakeSurface = 24f;

        public ulong Seed;
        /// <summary>村から見た海（浜と岬）・山（滝の山）・湖の森・星見の高原の方角（ラジアン。x=cos, z=sin）。</summary>
        public float SeaBearing, MountainBearing, LakeBearing, HighlandBearing;
        /// <summary>T6 をどちらにしたか（"t6a" 海風の林 ／ "t6b" 崖の小道）。</summary>
        public string T6;
        /// <summary>高原から湖の森への一方通行の大すべり台があるか（§3.4）。</summary>
        public bool HasHighlandSlide;
        public readonly List<SkeletonMap> Maps = new List<SkeletonMap>();

        public SkeletonMap Get(string id)
        {
            foreach (var m in Maps) if (m.Id == id) return m;
            return null;
        }

        public SkeletonGate GetGate(string gateId)
        {
            foreach (var m in Maps)
                foreach (var g in m.Gates)
                    if (g.Id == gateId) return g;
            return null;
        }

        public static Vector2 Dir(float bearing) => new Vector2(Mathf.Cos(bearing), Mathf.Sin(bearing));

        public static StarSkeleton Create(ulong seed)
        {
            var r = SeededRandom.For(seed, "world.gen3.skeleton");
            var s = new StarSkeleton { Seed = seed };
            s.SeaBearing = r.Range(0f, Mathf.PI * 2f);
            s.MountainBearing = s.SeaBearing + Mathf.PI + r.Range(-0.35f, 0.35f);
            float side = r.Chance(0.5f) ? 1f : -1f;
            s.LakeBearing = s.MountainBearing + side * r.Range(0.95f, 1.35f);
            s.HighlandBearing = s.MountainBearing - side * r.Range(0.25f, 0.55f);
            s.T6 = r.Chance(0.5f) ? SeaWindWood : CliffPath;
            s.HasHighlandSlide = r.Chance(0.6f);

            var v = s.Add(Village, "暮らしの谷", "Living Valley", SkeletonMapKind.Region, new Vector2(400f, 400f), r.Range(0, 3), Vector2.zero);
            v.Built = true;
            var lake = s.Add(LakeForest, "湖の森", "Lake Forest", SkeletonMapKind.Region, new Vector2(320f, 300f), r.Range(0, 3), Dir(s.LakeBearing) * 1f);
            lake.Built = true;
            var mtn = s.Add(WaterfallMountain, "滝の山", "Waterfall Mountain", SkeletonMapKind.Region, new Vector2(300f, 340f), r.Range(0, 3), Dir(s.MountainBearing) * 1.05f);
            var sea = s.Add(BeachCape, "浜と岬", "Beach and Cape", SkeletonMapKind.Region, new Vector2(360f, 220f), r.Range(0, 3), Dir(s.SeaBearing) * 1f);
            s.Add(Highland, "星見の高原", "Stargazing Highland", SkeletonMapKind.Region, new Vector2(240f, 240f), r.Range(0, 3), mtn.StarPos + Dir(s.HighlandBearing) * 0.9f);

            var t1 = s.AddRoute(FlowerPath, "花の野道", "Flower Path", new Vector2(240f, 120f), r, Village, LakeForest);
            t1.Built = true;
            s.AddRoute(MarshRiver, "川下りの湿原", "Downriver Marsh", new Vector2(260f, 130f), r, Village, BeachCape);
            s.AddRoute(StreamSlope, "渓流の坂道", "Stream Slope", new Vector2(260f, 110f), r, Village, WaterfallMountain);
            s.AddRoute(MossValley, "苔の谷", "Moss Valley", new Vector2(240f, 100f), r, LakeForest, WaterfallMountain);
            s.AddRoute(Ridge, "尾根道", "Ridge Path", new Vector2(280f, 90f), r, WaterfallMountain, Highland);
            if (s.T6 == SeaWindWood) s.AddRoute(SeaWindWood, "海風の林", "Sea Wind Wood", new Vector2(280f, 110f), r, BeachCape, LakeForest);
            else s.AddRoute(CliffPath, "崖の小道", "Cliff Path", new Vector2(280f, 110f), r, BeachCape, WaterfallMountain);

            // ゲートの高さ（§5 の標高の範囲から。村側は村の地形が決まってから入れる）
            s.SetGateHeight(FlowerPath, LakeForest, LakeSurface - r.Range(1.6f, 2.4f));
            s.SetGateHeight(MossValley, LakeForest, r.Range(31f, 35f));
            s.SetGateHeight(MossValley, WaterfallMountain, r.Range(52f, 58f));
            s.SetGateHeight(StreamSlope, WaterfallMountain, r.Range(50f, 55f));
            s.SetGateHeight(Ridge, WaterfallMountain, r.Range(88f, 95f));
            s.SetGateHeight(Ridge, Highland, r.Range(100f, 106f));
            s.SetGateHeight(MarshRiver, BeachCape, r.Range(2f, 4f));
            if (s.T6 == SeaWindWood)
            {
                s.SetGateHeight(SeaWindWood, BeachCape, r.Range(4f, 8f));
                s.SetGateHeight(SeaWindWood, LakeForest, r.Range(27f, 30f));
            }
            else
            {
                s.SetGateHeight(CliffPath, BeachCape, r.Range(10f, 16f));
                s.SetGateHeight(CliffPath, WaterfallMountain, r.Range(50f, 58f));
            }
            return s;
        }

        private SkeletonMap Add(string id, string ja, string en, SkeletonMapKind kind, Vector2 size, int type, Vector2 pos)
        {
            var m = new SkeletonMap { Id = id, NameJa = ja, NameEn = en, Kind = kind, Size = size, Type = type, StarPos = pos };
            Maps.Add(m);
            return m;
        }

        private SkeletonMap AddRoute(string id, string ja, string en, Vector2 size, SeededRandom r, string a, string b)
        {
            var ma = Get(a);
            var mb = Get(b);
            var m = Add(id, ja, en, SkeletonMapKind.Route, size, r.Range(0, 3), (ma.StarPos + mb.StarPos) * 0.5f);
            Link(ma, m);
            Link(m, mb);
            return m;
        }

        private static void Link(SkeletonMap a, SkeletonMap b)
        {
            var d = b.StarPos - a.StarPos;
            float bearing = Mathf.Atan2(d.y, d.x);
            a.Gates.Add(new SkeletonGate { Id = "gate." + a.Id + "." + b.Id, MapId = a.Id, OtherMapId = b.Id, Bearing = bearing });
            b.Gates.Add(new SkeletonGate { Id = "gate." + b.Id + "." + a.Id, MapId = b.Id, OtherMapId = a.Id, Bearing = bearing + Mathf.PI });
        }

        /// <summary>道中 route と地域 region のあいだのゲートの高さを、両側にそろえて入れる。</summary>
        public void SetGateHeight(string route, string region, float h)
        {
            var g1 = GetGate("gate." + route + "." + region);
            var g2 = GetGate("gate." + region + "." + route);
            if (g1 != null) g1.Height = h;
            if (g2 != null) g2.Height = h;
        }
    }
}
