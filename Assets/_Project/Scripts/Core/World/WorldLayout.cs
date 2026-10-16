using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.World
{
    // 村・マップ・ワールド生成仕様 v0.3 に基づく「生成されたワールドの配置データ」。
    // World モジュールが PreparePlanet で生成し、PlanetContext.Layout と WorldSystem.Layout で公開する。
    // 他のモジュール（住民・釣り・生き物・農業・鉱山・ロケット等）はここから場所を参照する。
    // 地形そのもの（高さ）はシードから決定的に再生成し、生活による変化は各モジュールの状態として保存する。

    /// <summary>概念上の7分類＋細分（村仕様 §2.2）。</summary>
    public enum AreaType
    {
        VillageCenter,
        Residential,
        PlayerHome,
        Farm,
        Forest,
        Meadow,
        Riverside,
        Pond,
        Coast,
        Mountain,
        MineEntrance,
        CrashSite,
        Hill,       // 高台（星の光帯を見る・ユメボシ等）。静かな高台は AreaRegion.quiet
        Lake        // 湖（生成バージョン2〜。生き物の出現では池と同じに扱う）
    }

    public enum FacilityType
    {
        Plaza,            // 広場
        PlayerShop,       // 主人公の商店（セルフ販売）
        MessagePlaza,     // 伝言広場（マネピ）
        BulletinBoard,    // 掲示板（日替わりのお願い）
        Blacksmith,       // 鍛冶屋（ツルハシ強化）
        RepairWorkshop,   // 修理屋（ロケット部品・四本腕の住民）
        SeedShop,         // 種屋
        GeneralStore,     // 雑貨屋（住民の店）
        CarpenterWorkshop,// 家具職人・増築
        FishingHut,       // 釣り名人の小屋
        CommunityKitchen, // 共用調理場
        Lookout,          // 高台の見晴らし台
        ResidentHouse,
        PlayerHouse,
        MineEntrance,
        RocketSite,       // 不時着地・修理作業場
        Well,
        Bench,
        Streetlight
    }

    /// <summary>水の種類。Lake は生成バージョン2から（マップ設計 v0.2 §2.3）。</summary>
    public enum WaterType { River, Pond, Sea, Lake }

    [Serializable]
    public class FacilityInfo
    {
        public string id;
        public FacilityType type;
        public Vector3 position;
        public float rotationY;
        public Vector3 size = new Vector3(6, 4, 6);
        /// <summary>入口（住民・プレイヤーが向かう地点）。</summary>
        public Vector3 entrance;
        /// <summary>担当住民（鍛冶屋・修理屋など）。住民モジュールが割り当てる。</summary>
        public string ownerResidentId;
        /// <summary>住宅区画の番号（ResidentHouse/PlayerHouse）。</summary>
        public int plotIndex = -1;
    }

    [Serializable]
    public class AreaRegion
    {
        public string id;
        public AreaType type;
        public Vector3 center;
        public float radius;
        /// <summary>表示名（主人公の呼び名。宇宙語の地名は Language 側で "PLACE:"+id から生成）。</summary>
        public string nameJa;
        public string nameEn;
        /// <summary>静かな場所（住民の予定では行かない。マップ設計 v0.2 §2.6 の静かな高台など）。</summary>
        public bool quiet;

        public bool Contains(Vector3 p)
        {
            var d = p - center;
            d.y = 0;
            return d.sqrMagnitude <= radius * radius;
        }
    }

    [Serializable]
    public class WaterBody
    {
        public string id;
        public WaterType type;
        /// <summary>川は中心線の点列、池・海は輪郭の中心。</summary>
        public List<Vector3> points = new List<Vector3>();
        /// <summary>川幅の半分／池の半径。</summary>
        public float radius;
        public float surfaceHeight;
    }

    /// <summary>釣り場（水辺の立ち位置）。釣りモジュールが魚の出現テーブルに使う。</summary>
    [Serializable]
    public class FishingSpot
    {
        public string id;
        public string waterId;
        public WaterType waterType;
        public Vector3 standPosition;
        public Vector3 waterPosition;
        /// <summary>特徴タグ（"deep" "rocky" "shallow" "waterfall" "estuary" など）。</summary>
        public List<string> tags = new List<string>();
    }

    /// <summary>生き物の生息地点（生き物モジュールの出現位置候補）。</summary>
    [Serializable]
    public class HabitatPoint
    {
        public Vector3 position;
        public AreaType area;
        /// <summary>"tree" "flower" "rock" "mud" "grass" "fallen_log" "roof" "window" "streetlight" "box" など。</summary>
        public string feature;
    }

    [Serializable]
    public class HousePlot
    {
        public int index;
        public Vector3 position;
        public float rotationY;
        public bool occupied;
        public string facilityId;
    }

    /// <summary>道路網（住民の移動経路）。</summary>
    [Serializable]
    public class RoadGraph
    {
        public List<Vector3> nodes = new List<Vector3>();
        public List<Vector2Int> edges = new List<Vector2Int>();
        /// <summary>橋の上にある辺のインデックス。</summary>
        public List<int> bridgeEdges = new List<int>();

        [NonSerialized] private List<List<int>> adjacency;

        public void RebuildAdjacency()
        {
            adjacency = new List<List<int>>();
            for (int i = 0; i < nodes.Count; i++) adjacency.Add(new List<int>());
            foreach (var e in edges)
            {
                if (e.x < 0 || e.y < 0 || e.x >= nodes.Count || e.y >= nodes.Count) continue;
                adjacency[e.x].Add(e.y);
                adjacency[e.y].Add(e.x);
            }
        }

        public int NearestNode(Vector3 p)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                var d = nodes[i] - p;
                d.y = 0;
                float sq = d.sqrMagnitude;
                if (sq < bestD) { bestD = sq; best = i; }
            }
            return best;
        }

        /// <summary>A*で経路を求める（始点・終点は最寄りノード経由）。見つからなければ直線。</summary>
        public List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            var result = new List<Vector3>();
            if (adjacency == null || adjacency.Count != nodes.Count) RebuildAdjacency();
            int s = NearestNode(from), g = NearestNode(to);
            if (s < 0 || g < 0 || s == g)
            {
                result.Add(to);
                return result;
            }
            var open = new List<int> { s };
            var came = new Dictionary<int, int>();
            var gScore = new Dictionary<int, float> { [s] = 0f };
            var closed = new HashSet<int>();
            int guard = 0;
            while (open.Count > 0 && guard++ < 10000)
            {
                int cur = open[0];
                float bestF = float.MaxValue;
                foreach (var n in open)
                {
                    float f = gScore[n] + Vector3.Distance(nodes[n], nodes[g]);
                    if (f < bestF) { bestF = f; cur = n; }
                }
                if (cur == g) break;
                open.Remove(cur);
                closed.Add(cur);
                foreach (var nb in adjacency[cur])
                {
                    if (closed.Contains(nb)) continue;
                    float tentative = gScore[cur] + Vector3.Distance(nodes[cur], nodes[nb]);
                    if (!gScore.TryGetValue(nb, out var old) || tentative < old)
                    {
                        gScore[nb] = tentative;
                        came[nb] = cur;
                        if (!open.Contains(nb)) open.Add(nb);
                    }
                }
            }
            if (!came.ContainsKey(g))
            {
                result.Add(to);
                return result;
            }
            var stack = new Stack<Vector3>();
            int c = g;
            while (c != s)
            {
                stack.Push(nodes[c]);
                c = came[c];
            }
            stack.Push(nodes[s]);
            result.AddRange(stack);
            result.Add(to);
            return result;
        }
    }

    /// <summary>
    /// 生成済みワールドの配置データ。高さ・水判定の関数は World モジュールが設定する。
    /// 生成バージョン3（マップ設計 v0.3 §10.2）：星の配置データ（＝村 R1 のマップ）の下に、村の外の地域・道中マップの配置データ（Maps）を持つ。
    /// マップは互いに離れた座標にあるので、位置からマップが決まる（LayoutAt）。村の配置データの高さ・水・エリアの問い合わせは、
    /// 位置が村の外のマップなら、そのマップに回す（生き物の足元・天候などがそのまま動く）。釣り場・生息地点・エリアの一覧はマップごと。
    /// 住民の行き先などは今までどおり村の一覧だけを見る。
    /// </summary>
    public class WorldLayout
    {
        /// <summary>ワールドの一辺（m）。原点中心に ±WorldSize/2。</summary>
        public float WorldSize = 400f;
        /// <summary>マップの ID（村は "r1"。マップ設計 v0.3 の骨格の ID）。</summary>
        public string MapId = "r1";
        /// <summary>このマップのエリアの ID（村は屋外 "overworld"、村の外は "map:&lt;ID&gt;"）。</summary>
        public string MapAreaId = Core.AreaIds.Overworld;
        /// <summary>村の外のマップの範囲（XZ。見た目の縁を含む）。村は WorldSize で判定する。</summary>
        public bool HasBounds;
        public Vector2 BoundsMin, BoundsMax;
        /// <summary>村の外の地域・道中マップ（星の配置データにだけ入る）。</summary>
        public readonly List<WorldLayout> Maps = new List<WorldLayout>();

        /// <summary>このマップの範囲の中か（村は端から10mの外側まで）。</summary>
        public bool ContainsXZ(float x, float z)
        {
            if (HasBounds) return x >= BoundsMin.x && x <= BoundsMax.x && z >= BoundsMin.y && z <= BoundsMax.y;
            float half = WorldSize * 0.5f + 10f;
            return Mathf.Abs(x) <= half && Mathf.Abs(z) <= half;
        }

        /// <summary>位置のあるマップの配置データ（村の外のマップでなければ自分）。</summary>
        public WorldLayout LayoutAt(Vector3 p)
        {
            for (int i = 0; i < Maps.Count; i++)
                if (Maps[i].ContainsXZ(p.x, p.z)) return Maps[i];
            return this;
        }

        /// <summary>エリアの ID のマップの配置データ（無ければ null）。</summary>
        public WorldLayout LayoutOfArea(string areaId)
        {
            if (areaId == MapAreaId) return this;
            foreach (var m in Maps) if (m.MapAreaId == areaId) return m;
            return null;
        }

        /// <summary>自分と、村の外のマップすべて。</summary>
        public IEnumerable<WorldLayout> AllMaps
        {
            get
            {
                yield return this;
                foreach (var m in Maps) yield return m;
            }
        }
        public readonly List<FacilityInfo> Facilities = new List<FacilityInfo>();
        public readonly List<AreaRegion> Areas = new List<AreaRegion>();
        public readonly List<WaterBody> Waters = new List<WaterBody>();
        public readonly List<FishingSpot> FishingSpots = new List<FishingSpot>();
        public readonly List<HabitatPoint> HabitatPoints = new List<HabitatPoint>();
        public readonly List<HousePlot> HousePlots = new List<HousePlot>();
        public RoadGraph Roads = new RoadGraph();
        /// <summary>農場の区画（XZ平面の矩形、ワールド座標）。</summary>
        public Rect FarmRect;
        public Vector3 PlayerSpawn;
        public Vector3 CrashSite;
        public Vector3 MineEntrance;

        /// <summary>地表の高さ。World モジュールが設定。</summary>
        public Func<float, float, float> HeightSampler = (x, z) => 0f;
        /// <summary>水面かどうか。World モジュールが設定。</summary>
        public Func<float, float, bool> WaterTester = (x, z) => false;
        /// <summary>水深（水面 − 地面。水でなければ 0）。World モジュールが設定（マップ設計 v0.2 §2.7：深さ0.6mまでの浅瀬だけ歩ける）。</summary>
        public Func<float, float, float> WaterDepthSampler = (x, z) => 0f;
        /// <summary>歩いて入れる水深の上限（m）【仮数値】。</summary>
        public const float WadeableDepth = 0.6f;

        public float HeightAt(float x, float z) => HeightSampler(x, z);
        public float HeightAt(Vector3 p) => HeightSampler(p.x, p.z);
        public Vector3 OnGround(Vector3 p) => new Vector3(p.x, HeightSampler(p.x, p.z), p.z);
        public bool IsWater(Vector3 p) => WaterTester(p.x, p.z);
        public float WaterDepthAt(Vector3 p) => WaterDepthSampler(p.x, p.z);

        public FacilityInfo GetFacility(FacilityType type)
        {
            foreach (var f in Facilities) if (f.type == type) return f;
            return null;
        }

        public FacilityInfo GetFacilityById(string id)
        {
            foreach (var f in Facilities) if (f.id == id) return f;
            return null;
        }

        public IEnumerable<FacilityInfo> GetFacilities(FacilityType type)
        {
            foreach (var f in Facilities) if (f.type == type) yield return f;
        }

        public AreaRegion GetAreaAt(Vector3 p)
        {
            if (Maps.Count > 0)
            {
                var m = LayoutAt(p);
                if (m != this) return m.GetAreaAt(p);
            }
            AreaRegion best = null;
            float bestR = float.MaxValue;
            foreach (var a in Areas)
            {
                if (a.Contains(p) && a.radius < bestR)
                {
                    best = a;
                    bestR = a.radius;
                }
            }
            return best;
        }

        public IEnumerable<AreaRegion> GetAreas(AreaType type)
        {
            foreach (var a in Areas) if (a.type == type) yield return a;
        }

        /// <summary>住民などが行き先に選んでよいエリア（静かな場所は除く。無ければ全部）。</summary>
        public IEnumerable<AreaRegion> GetVisitableAreas(AreaType type)
        {
            bool any = false;
            foreach (var a in Areas)
                if (a.type == type && !a.quiet)
                {
                    any = true;
                    yield return a;
                }
            if (any) yield break;
            foreach (var a in Areas) if (a.type == type) yield return a;
        }

        /// <summary>そのエリアの種類の中の適当な地点（住民の行き先用。静かな場所は選ばない）。</summary>
        public Vector3 RandomPointInArea(AreaType type, Core.SeededRandom rng)
        {
            var list = new List<AreaRegion>(GetVisitableAreas(type));
            if (list.Count == 0) return PlayerSpawn;
            var a = rng.Pick(list);
            for (int i = 0; i < 12; i++)
            {
                var c = rng.InsideUnitCircle() * a.radius;
                var p = OnGround(a.center + new Vector3(c.x, 0, c.y));
                if (!IsWater(p)) return p;
            }
            return OnGround(a.center);
        }

        public IEnumerable<HabitatPoint> HabitatsIn(AreaType area)
        {
            foreach (var h in HabitatPoints) if (h.area == area) yield return h;
        }
    }
}
