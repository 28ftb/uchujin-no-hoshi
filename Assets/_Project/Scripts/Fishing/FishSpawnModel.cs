using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Fishing
{
    /// <summary>キャスト先の釣り場情報（Layout.FishingSpots の1件、または水面から推定した仮の釣り場）。</summary>
    public sealed class SpotInfo
    {
        public string Id;
        public FishWater Water;
        public Vector3 WaterPosition;
        public Vector3 StandPosition;
        public float SurfaceY;
        public readonly HashSet<string> Tags = new HashSet<string>();
        public int Floor;          // 地下湖の階（0=地上）
        public float Radius = 3f;  // 地下湖など、水面判定に使う半径
        public bool IsUnderground => Water == FishWater.Underground;

        public string Label()
        {
            if (IsUnderground) return Loc.Format("鉱山{0}階の地下湖", "Underground lake (mine F{0})", Floor);
            string area = null;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            var reg = layout?.GetAreaAt(StandPosition);
            if (reg != null) area = Loc.T(reg.nameJa, reg.nameEn);
            string w = WaterLabel(Water);
            return string.IsNullOrEmpty(area) ? w : area + "・" + w;
        }

        public static string WaterLabel(FishWater w)
        {
            switch (w)
            {
                case FishWater.River: return Loc.T("川", "River");
                case FishWater.Pond: return Loc.T("池", "Pond");
                case FishWater.Sea: return Loc.T("海", "Sea");
                case FishWater.Underground: return Loc.T("地下湖", "Underground lake");
                default: return Loc.T("水辺", "Water");
            }
        }

        public string WaterConcept()
        {
            switch (Water)
            {
                case FishWater.River: return Concepts.RIVER;
                case FishWater.Sea: return Concepts.SEA;
                case FishWater.Underground: return Concepts.MINE;
                default: return Concepts.POND;
            }
        }
    }

    /// <summary>
    /// 魚の出現モデル（釣り仕様 §6、時間・天候仕様 §8）。
    /// 水域・釣り場の特徴・時間帯・天候/宇宙固有現象・餌・日替わりの傾向（決定的）・釣りの腕から重みを計算する。
    /// 四季は無い（時間・天候仕様 §6）。
    /// </summary>
    public sealed class FishSpawnModel
    {
        private ulong seed;
        private readonly List<SpotInfo> spots = new List<SpotInfo>();
        private readonly Dictionary<string, SpotInfo> spotById = new Dictionary<string, SpotInfo>();
        private int trendDay = -1;
        private readonly Dictionary<string, List<string>> activeBySpot = new Dictionary<string, List<string>>();
        private string bigFishSpotId;
        private FishWater planetWaters;

        // ---- 日替わりの倍率【仮数値】----
        public const float TrendActiveMultiplier = 2.2f;
        public const int TrendActivePerSpot = 2;
        public const float OutOfTimeMultiplier = 0.1f;
        public const float LikedTagMultiplier = 2f;
        public const float LikedWeatherMultiplier = 2.5f;
        public const float BaitPreferredMultiplier = 3f;
        public const float FallbackSpotRadius = 2.5f;

        public IReadOnlyList<SpotInfo> Spots => spots;
        public FishWater PlanetWaters => planetWaters;
        public string BigFishSpotId => bigFishSpotId;

        public static float RarityWeight(int rarity)
        {
            switch (rarity)
            {
                case 0: return 10f;
                case 1: return 6f;
                case 2: return 3.2f;
                case 3: return 1.6f;
                case 4: return 0.9f;
                default: return 0.6f;
            }
        }

        public void Prepare(ulong worldSeed, WorldLayout layout)
        {
            seed = worldSeed;
            trendDay = -1;
            RefreshSpots(layout);
        }

        /// <summary>Layout.FishingSpots から釣り場を読み直す（鉱山の地下湖は鉱山モジュールが後から追加するため、BuildScene でも呼ぶ）。</summary>
        public void RefreshSpots(WorldLayout layout)
        {
            spots.Clear();
            spotById.Clear();
            planetWaters = FishWater.None;
            if (layout == null) return;
            foreach (var fs in layout.FishingSpots)
            {
                if (fs == null || string.IsNullOrEmpty(fs.id)) continue;
                var info = FromLayoutSpot(fs, layout);
                spots.Add(info);
                spotById[info.Id] = info;
                planetWaters |= info.Water;
            }
            foreach (var w in layout.Waters) planetWaters |= ToFishWater(w.type);
            trendDay = -1;
        }

        public static FishWater ToFishWater(WaterType t)
        {
            switch (t)
            {
                case WaterType.River: return FishWater.River;
                case WaterType.Sea: return FishWater.Sea;
                default: return FishWater.Pond;
            }
        }

        private static SpotInfo FromLayoutSpot(FishingSpot fs, WorldLayout layout)
        {
            var info = new SpotInfo
            {
                Id = fs.id,
                Water = ToFishWater(fs.waterType),
                WaterPosition = fs.waterPosition,
                StandPosition = fs.standPosition,
                SurfaceY = fs.waterPosition.y
            };
            if (fs.tags != null)
            {
                foreach (var t in fs.tags)
                {
                    if (string.IsNullOrEmpty(t)) continue;
                    info.Tags.Add(t);
                    if (t.StartsWith("floor:")) int.TryParse(t.Substring(6), out info.Floor);
                    if (t.StartsWith("radius:"))
                        float.TryParse(t.Substring(7), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out info.Radius);
                }
            }
            if (info.Tags.Contains("underground")) info.Water = FishWater.Underground;
            if (!info.IsUnderground && layout != null)
            {
                foreach (var w in layout.Waters)
                {
                    if (w.id == fs.waterId)
                    {
                        info.SurfaceY = w.surfaceHeight;
                        break;
                    }
                }
            }
            return info;
        }

        public SpotInfo GetSpot(string id)
        {
            if (id != null && spotById.TryGetValue(id, out var s)) return s;
            return null;
        }

        /// <summary>
        /// 位置が水面かどうか判定し、対応する釣り場情報を返す（水面でなければ null）。
        /// 地上は Layout.IsWater、地下湖は釣り場の半径で判定する。
        /// </summary>
        public SpotInfo ResolveWaterAt(Vector3 point, WorldLayout layout)
        {
            // 地下湖など半径で判定する釣り場
            foreach (var s in spots)
            {
                if (!s.IsUnderground && !s.Tags.Contains("radius_only")) continue;
                var d = point - s.WaterPosition;
                d.y = 0;
                if (d.magnitude <= s.Radius) return s;
            }
            if (layout == null) return null;
            // 地形の範囲外（家の中・鉱山など屋外から離れた座標）は水面ではない。
            // 高さ場は範囲外を端のマスに丸めるため、端が海の星では家の中や鉱山でも「水」と判定されてしまう。
            float half = layout.WorldSize * 0.5f + 10f;
            if (Mathf.Abs(point.x) > half || Mathf.Abs(point.z) > half) return null;
            if (!layout.IsWater(point))
            {
                // 水面判定が無い／ずれている場合の保険：釣り場の水面位置のすぐ近くなら水とみなす
                foreach (var s in spots)
                {
                    if (s.IsUnderground) continue;
                    var d = point - s.WaterPosition;
                    d.y = 0;
                    if (d.magnitude <= FallbackSpotRadius) return s;
                }
                return null;
            }
            // 最寄りの地上の釣り場
            SpotInfo best = null;
            float bestD = 30f * 30f;
            foreach (var s in spots)
            {
                if (s.IsUnderground) continue;
                var d = point - s.WaterPosition;
                d.y = 0;
                float sq = d.sqrMagnitude;
                if (sq < bestD)
                {
                    bestD = sq;
                    best = s;
                }
            }
            if (best != null) return best;
            // 近くに釣り場が無い水面：水域の種類だけで仮の釣り場を作る
            var water = NearestWater(point, layout, out var surface);
            var info = new SpotInfo
            {
                Id = "free:" + Mathf.RoundToInt(point.x / 20f) + "_" + Mathf.RoundToInt(point.z / 20f),
                Water = water,
                WaterPosition = new Vector3(point.x, surface, point.z),
                StandPosition = point,
                SurfaceY = surface
            };
            info.Tags.Add("shallow");
            return info;
        }

        private static FishWater NearestWater(Vector3 p, WorldLayout layout, out float surface)
        {
            surface = layout.HeightAt(p);
            FishWater result = FishWater.Pond;
            float best = float.MaxValue;
            foreach (var w in layout.Waters)
            {
                if (w.points == null || w.points.Count == 0) continue;
                float d;
                if (w.type == WaterType.River && w.points.Count >= 2)
                {
                    d = float.MaxValue;
                    for (int i = 0; i < w.points.Count - 1; i++) d = Mathf.Min(d, DistToSegmentXZ(p, w.points[i], w.points[i + 1]));
                }
                else
                {
                    var c = w.points[0] - p;
                    c.y = 0;
                    d = Mathf.Max(0f, c.magnitude - w.radius);
                }
                if (d < best)
                {
                    best = d;
                    result = ToFishWater(w.type);
                    surface = w.surfaceHeight;
                }
            }
            return result;
        }

        private static float DistToSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            var ap = new Vector2(p.x - a.x, p.z - a.z);
            var ab = new Vector2(b.x - a.x, b.z - a.z);
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(ap, ab) / ab.sqrMagnitude);
            var proj = new Vector2(a.x, a.z) + ab * t;
            return Vector2.Distance(new Vector2(p.x, p.z), proj);
        }

        // ---------------- 条件判定 ----------------

        public static FishTime TimeOf(int hour)
        {
            switch (DayPartUtil.FromHour(hour))
            {
                case DayPart.LateNight: return FishTime.LateNight;
                case DayPart.EarlyMorning: return FishTime.EarlyMorning;
                case DayPart.Morning: return FishTime.Morning;
                case DayPart.Afternoon: return FishTime.Afternoon;
                case DayPart.Evening: return FishTime.Evening;
                default: return FishTime.Night;
            }
        }

        public static bool WeatherMatches(WeatherNeed need, WeatherState w)
        {
            switch (need)
            {
                case WeatherNeed.Any: return true;
                case WeatherNeed.Rain: return w.IsRaining;
                case WeatherNeed.HeavyRain: return w.precipitation == Precipitation.HeavyRain;
                case WeatherNeed.NoRain: return !w.IsRaining;
                case WeatherNeed.Clear: return w.sky <= SkyState.Sunny && !w.IsRaining;
                case WeatherNeed.Cloudy: return w.sky >= SkyState.Cloudy;
                case WeatherNeed.Fog: return w.IsFoggy;
                case WeatherNeed.StrongWind: return w.wind == WindLevel.Strong;
                case WeatherNeed.Calm: return w.wind == WindLevel.Calm && w.precipitation <= Precipitation.Drizzle;
                case WeatherNeed.LightRain: return w.Has(AlienPhenomenon.LightRain);
                case WeatherNeed.StarWind: return w.Has(AlienPhenomenon.StarWind);
                case WeatherNeed.ReverseRain: return w.Has(AlienPhenomenon.ReverseRain);
                case WeatherNeed.StarBand: return w.Has(AlienPhenomenon.StarBand);
                case WeatherNeed.CrystalHaze: return w.Has(AlienPhenomenon.CrystalHaze);
                case WeatherNeed.BloomTide: return w.Has(AlienPhenomenon.BloomTide);
                default: return false;
            }
        }

        /// <summary>地下湖の環境（風も雨も無く、いつも暗く静か）。</summary>
        public static WeatherState CaveWeather => new WeatherState { sky = SkyState.Overcast, wind = WindLevel.Calm, precipitation = Precipitation.None };

        public static bool CanLiveIn(FishSpecies s, SpotInfo spot)
        {
            if ((s.Waters & spot.Water) == 0) return false;
            if (spot.IsUnderground)
            {
                if (s.MinFloor > 0 && spot.Floor > 0 && spot.Floor < s.MinFloor) return false;
                if (s.MaxFloor > 0 && spot.Floor > 0 && spot.Floor > s.MaxFloor) return false;
            }
            if (s.RequireSpotTags.Length > 0)
            {
                bool any = false;
                foreach (var t in s.RequireSpotTags) if (spot.Tags.Contains(t)) any = true;
                if (!any) return false;
            }
            return true;
        }

        // ---------------- 日替わりの傾向（決定的）----------------

        public void EnsureTrend(int day)
        {
            if (trendDay == day) return;
            trendDay = day;
            activeBySpot.Clear();
            var rng = SeededRandom.For(seed, "fishing.trend.d" + day);
            foreach (var s in spots)
            {
                var candidates = new List<string>();
                foreach (var f in FishCatalog.All) if (!f.IsLegend && CanLiveIn(f, s)) candidates.Add(f.Id);
                candidates.Sort(System.StringComparer.Ordinal);
                var local = rng.Fork(s.Id);
                local.Shuffle(candidates);
                var act = new List<string>();
                for (int i = 0; i < candidates.Count && act.Count < TrendActivePerSpot; i++) act.Add(candidates[i]);
                activeBySpot[s.Id] = act;
            }
            bigFishSpotId = spots.Count > 0 ? spots[rng.Range(0, spots.Count)].Id : null;
        }

        public bool IsTrendActive(int day, string spotId, string fishId)
        {
            EnsureTrend(day);
            return spotId != null && activeBySpot.TryGetValue(spotId, out var list) && list.Contains(fishId);
        }

        public IReadOnlyList<string> TrendFor(int day, string spotId)
        {
            EnsureTrend(day);
            if (spotId != null && activeBySpot.TryGetValue(spotId, out var list)) return list;
            return new List<string>();
        }

        public bool IsBigFishDay(int day, string spotId)
        {
            EnsureTrend(day);
            return spotId != null && spotId == bigFishSpotId;
        }

        // ---------------- 重み ----------------

        public sealed class WeightContext
        {
            public int Day;
            public int Hour;
            public WeatherState Weather;
            public string BaitId;
            public int Level = 1;
            public bool PrecisionBonus;
            public System.Func<string, bool> LegendResting;
        }

        public float Weight(FishSpecies s, SpotInfo spot, WeightContext c)
        {
            if (!CanLiveIn(s, spot)) return 0f;
            var weather = spot.IsUnderground ? CaveWeather : c.Weather;
            if (!WeatherMatches(s.RequireWeather, weather)) return 0f;
            if (s.IsLegend && c.LegendResting != null && c.LegendResting(s.Id)) return 0f;

            float w = RarityWeight(s.Rarity);
            // 時間帯（地下湖は昼夜の区別が無い）
            if (!spot.IsUnderground && (s.Times & TimeOf(c.Hour)) == 0)
            {
                if (s.IsLegend) return 0f;
                w *= OutOfTimeMultiplier;
            }
            foreach (var t in s.LikeSpotTags)
            {
                if (spot.Tags.Contains(t))
                {
                    w *= LikedTagMultiplier;
                    break;
                }
            }
            foreach (var lw in s.LikeWeather)
            {
                if (WeatherMatches(lw, weather))
                {
                    w *= LikedWeatherMultiplier;
                    break;
                }
            }
            if (!string.IsNullOrEmpty(c.BaitId) && c.BaitId == s.BaitId) w *= BaitPreferredMultiplier;
            if (IsTrendActive(c.Day, spot.Id, s.Id)) w *= TrendActiveMultiplier;
            if (s.Rarity >= 2)
            {
                w *= 1f + FishingTuning.LevelRareBonus * (c.Level - 1);
                if (c.PrecisionBonus) w *= FishingTuning.PrecisionRareMultiplier;
            }
            return w;
        }

        /// <summary>その場・その時に釣れる魚の重み一覧（重み&gt;0のみ）。</summary>
        public List<KeyValuePair<FishSpecies, float>> Candidates(SpotInfo spot, WeightContext c)
        {
            var list = new List<KeyValuePair<FishSpecies, float>>();
            foreach (var s in FishCatalog.All)
            {
                float w = Weight(s, spot, c);
                if (w > 0f) list.Add(new KeyValuePair<FishSpecies, float>(s, w));
            }
            return list;
        }

        public FishSpecies Pick(SpotInfo spot, WeightContext c, SeededRandom rng)
        {
            var list = Candidates(spot, c);
            if (list.Count == 0) return null;
            var weights = new float[list.Count];
            for (int i = 0; i < list.Count; i++) weights[i] = list[i].Value;
            int idx = rng.WeightedIndex(weights);
            return idx < 0 ? null : list[idx].Key;
        }

        /// <summary>
        /// 今その釣り場で「目立つ魚影」を作っている魚（観察の手掛かり）。希少度2以上で、全体の重みのうち一定以上を占める魚。
        /// </summary>
        public FishSpecies NotableNow(SpotInfo spot, WeightContext c)
        {
            var list = Candidates(spot, c);
            float total = 0f;
            foreach (var kv in list) total += kv.Value;
            if (total <= 0f) return null;
            FishSpecies best = null;
            float bestShare = 0f;
            foreach (var kv in list)
            {
                if (kv.Key.Rarity < 2) continue;
                float share = kv.Value / total;
                // 修理用の魚・伝説の魚は少ない割合でも魚影を見せる（低確率待ちにしないための手掛かり）
                float threshold = kv.Key.IsRocketMaterial || kv.Key.IsLegend ? 0.04f : 0.1f;
                if (share >= threshold && share > bestShare)
                {
                    bestShare = share;
                    best = kv.Key;
                }
            }
            return best;
        }
    }
}
