using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // 村の中心・生活施設・主人公の拠点・不時着地・鉱山入口・高台・釣り小屋・桟橋の配置（村仕様 §4〜§9）
    public sealed partial class WorldGenerator
    {
        private Vector2 plaza;
        private bool satelliteBlacksmith;
        private bool satelliteRepair;
        private readonly List<FacilityInfo> ringFacilities = new List<FacilityInfo>();
        private readonly bool[] ringSlotUsed = new bool[RingSlots];
        private float ringBaseAngle;
        private const int RingSlots = 11;
        private Vector2 farmCenter;
        private Vector2 crashCenter;
        private Vector2 mineMouth;
        private Vector2 lookoutCenter;
        private readonly List<(Vector2 c, float r)> reserved = new List<(Vector2, float)>();

        // ---------------- 施設の大きさ・ID（【仮数値】） ----------------

        public static Vector3 SizeOf(FacilityType t)
        {
            switch (t)
            {
                case FacilityType.Plaza: return new Vector3(WorldTuning.PlazaRadius * 2f, 0f, WorldTuning.PlazaRadius * 2f);
                case FacilityType.PlayerShop: return new Vector3(10f, 4.5f, 8f);
                case FacilityType.MessagePlaza: return new Vector3(7f, 3.6f, 7f);
                case FacilityType.BulletinBoard: return new Vector3(3f, 2.6f, 0.6f);
                case FacilityType.Blacksmith: return new Vector3(8f, 4.5f, 7f);
                case FacilityType.RepairWorkshop: return new Vector3(9f, 5f, 8f);
                case FacilityType.SeedShop: return new Vector3(7f, 4f, 6f);
                case FacilityType.GeneralStore: return new Vector3(8f, 4.5f, 7f);
                case FacilityType.CarpenterWorkshop: return new Vector3(9f, 4.5f, 7f);
                case FacilityType.FishingHut: return new Vector3(6f, 3.6f, 5f);
                case FacilityType.CommunityKitchen: return new Vector3(8f, 3.6f, 7f);
                case FacilityType.Lookout: return new Vector3(8f, 1f, 8f);
                case FacilityType.ResidentHouse: return new Vector3(7f, 4f, 6f);
                case FacilityType.PlayerHouse: return new Vector3(8f, 4.5f, 7f);
                case FacilityType.MineEntrance: return new Vector3(6f, 5f, 4f);
                case FacilityType.RocketSite: return new Vector3(22f, 0f, 22f);
                case FacilityType.Well: return new Vector3(2.2f, 2.6f, 2.2f);
                case FacilityType.Bench: return new Vector3(2f, 1f, 0.8f);
                case FacilityType.Streetlight: return new Vector3(0.5f, 3.6f, 0.5f);
                default: return new Vector3(6f, 4f, 6f);
            }
        }

        /// <summary>施設の安定ID（同じシードなら同じ）。</summary>
        public static string IdOf(FacilityType t)
        {
            switch (t)
            {
                case FacilityType.Plaza: return "facility.plaza";
                case FacilityType.PlayerShop: return "facility.player_shop";
                case FacilityType.MessagePlaza: return "facility.message_plaza";
                case FacilityType.BulletinBoard: return "facility.bulletin_board";
                case FacilityType.Blacksmith: return "facility.blacksmith";
                case FacilityType.RepairWorkshop: return "facility.repair_workshop";
                case FacilityType.SeedShop: return "facility.seed_shop";
                case FacilityType.GeneralStore: return "facility.general_store";
                case FacilityType.CarpenterWorkshop: return "facility.carpenter";
                case FacilityType.FishingHut: return "facility.fishing_hut";
                case FacilityType.CommunityKitchen: return "facility.community_kitchen";
                case FacilityType.Lookout: return "facility.lookout";
                case FacilityType.PlayerHouse: return "facility.player_house";
                case FacilityType.MineEntrance: return "facility.mine_entrance";
                case FacilityType.RocketSite: return "facility.rocket_site";
                case FacilityType.Well: return "facility.well";
                default: return "facility." + t.ToString().ToLowerInvariant();
            }
        }

        private static Vector2 Fwd2(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        }

        private static Vector2 Right2(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), -Mathf.Sin(r));
        }

        private static float YawOf(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg;

        private static float FootRadius(Vector3 size) => new Vector2(size.x, size.z).magnitude * 0.5f;

        /// <summary>建物を置ける場所か（水・急斜面・他の区画・道路を避ける）。</summary>
        private bool SiteOk(Vector2 c, float radius, bool allowWaterNear = false, float maxRough = 4.5f, bool checkRoads = false, float padGap = 2.5f)
        {
            if (!GeoUtil.InsideWorld(c, 14f + radius * 0.5f)) return false;
            if (!allowWaterNear && F.WaterNear(c.x, c.y, radius + 3f)) return false;
            if (allowWaterNear && F.WaterNear(c.x, c.y, radius * 0.7f)) return false;
            if (F.Roughness(c.x, c.y, radius) > maxRough) return false;
            if (!allowWaterNear && S.CoastD(c.x, c.y) < 16f + radius) return false;
            if (S.MountainFactor(c.x, c.y) < 0.78f) return false;
            foreach (var pad in W.Pads)
                if (pad.SignedDistance(c.x, c.y) < radius + padGap) return false;
            foreach (var fp in footprints)
                if (fp.SignedDistance(c.x, c.y) < radius + 1.5f) return false;
            foreach (var rs in reserved)
                if (Vector2.Distance(rs.c, c) < rs.r + radius) return false;
            if (!QuietClearOk(c)) return false;
            if (W.FarmReserve.width > 0f)
            {
                var rr = W.FarmReserve;
                rr.xMin -= radius;
                rr.yMin -= radius;
                rr.xMax += radius;
                rr.yMax += radius;
                if (rr.Contains(c)) return false;
            }
            if (checkRoads)
            {
                foreach (var seg in W.RoadSegments)
                    if (GeoUtil.DistToSegment(c, seg.A, seg.B, out _) < radius + seg.HalfWidth + 0.8f) return false;
            }
            return true;
        }

        private FacilityInfo AddFacility(FacilityType type, string id, Vector2 c, float yaw, Vector3 size, Vector2 entrance)
        {
            var f = new FacilityInfo
            {
                id = id,
                type = type,
                position = new Vector3(c.x, 0f, c.y),
                rotationY = yaw,
                size = size,
                entrance = new Vector3(entrance.x, 0f, entrance.y)
            };
            L.Facilities.Add(f);
            return f;
        }

        /// <summary>建物（土台・通行不可の範囲つき）を追加する。入口は建物の前（+Z側）の地面。</summary>
        private FacilityInfo AddBuilding(FacilityType type, string id, Vector2 c, float yaw, Vector3 size, float? targetY = null, PadKind padKind = PadKind.Building)
        {
            var fwd = Fwd2(yaw);
            var entrance = c + fwd * (size.z * 0.5f + 1.8f);
            var f = AddFacility(type, id, c, yaw, size, entrance);
            AddPad(padKind, c + fwd * 1.0f, new Vector2(size.x * 0.5f + 1.6f, size.z * 0.5f + 2.6f), yaw, false, 4f, id, targetY);
            AddFootprint(c, new Vector2(size.x * 0.5f + 0.3f, size.z * 0.5f + 0.3f), yaw);
            return f;
        }

        // =================================================================
        // 村の中心（広場）
        // =================================================================

        private void PlaceVillageCenter()
        {
            var r = R("village");
            Vector2 best = Vector2.zero;
            float bestScore = float.MinValue;
            for (int k = 0; k < 500; k++)
            {
                var c = new Vector2(r.Range(-115f, 115f), r.Range(-115f, 115f));
                float d = S.CoastD(c.x, c.y);
                if (d < 48f) continue;
                if (S.MountainFactor(c.x, c.y) < 1.45f) continue;
                if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + 26f) continue;
                if (F.WaterNear(c.x, c.y, 26f)) continue;
                if (V2 && S.HasQuiet && Vector2.Distance(c, S.QuietCenter) < 140f) continue;
                float rough = F.Roughness(c.x, c.y, 30f);
                float river = DistToRivers(c);
                float score = -rough * 3f - Mathf.Abs(d - 85f) * 0.12f - c.magnitude * 0.07f + r.Range(0f, 3f);
                if (river > 30f && river < 75f) score += 6f; // 商店街から川が見える（村仕様 §2.2）
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (bestScore == float.MinValue)
            {
                W.Notes.Add("広場の候補なし");
                best = -S.SeaDir * 10f;
            }
            plaza = best;
            W.PlazaCenter = new Vector3(plaza.x, 0f, plaza.y);
            if (V2) DecideQuietShore();
            AddPad(PadKind.Plaza, plaza, new Vector2(WorldTuning.PlazaRadius + 2f, 0f), 0f, true, 6f, IdOf(FacilityType.Plaza));
            var pf = AddFacility(FacilityType.Plaza, IdOf(FacilityType.Plaza), plaza, 0f, SizeOf(FacilityType.Plaza), plaza);
            pf.entrance = pf.position;
        }

        // =================================================================
        // 広場を囲む生活施設（村仕様 §2.2 ①、§4）
        // =================================================================

        private void PlaceRingFacilities()
        {
            var r = R("ring");
            satelliteBlacksmith = r.Chance(WorldTuning.SatelliteFacilityChance);
            satelliteRepair = r.Chance(WorldTuning.SatelliteFacilityChance);
            var others = new List<FacilityType>
            {
                FacilityType.SeedShop, FacilityType.GeneralStore, FacilityType.CarpenterWorkshop, FacilityType.CommunityKitchen
            };
            if (!satelliteBlacksmith) others.Add(FacilityType.Blacksmith);
            if (!satelliteRepair) others.Add(FacilityType.RepairWorkshop);
            r.Shuffle(others);
            ringBaseAngle = r.Range(0f, 360f);

            int shopSlot = PlaceInRing(FacilityType.PlayerShop, r.Range(0, RingSlots));
            // 伝言広場は商店の隣（村仕様 §4.3：商店や住宅の近く）
            PlaceInRing(FacilityType.MessagePlaza, shopSlot >= 0 ? (shopSlot + 1) % RingSlots : 0);
            foreach (var t in others) PlaceInRing(t, r.Range(0, RingSlots));
        }

        private int PlaceInRing(FacilityType type, int startSlot)
        {
            var size = SizeOf(type);
            for (int n = 0; n < RingSlots; n++)
            {
                int k = (startSlot + n) % RingSlots;
                if (ringSlotUsed[k]) continue;
                float ang = ringBaseAngle + k * (360f / RingSlots);
                var dir = Fwd2(ang);
                float baseR = WorldTuning.FacilityRingRadius + size.z * 0.5f - 2f;
                for (int ring = 0; ring < 2; ring++)
                {
                    float rad = baseR + ring * 11f;
                    var c = plaza + dir * rad;
                    if (!SiteOk(c, FootRadius(size) * 0.85f, false, 5f, false, 1.5f)) continue;
                    float yaw = YawOf(plaza - c);
                    var f = AddBuilding(type, IdOf(type), c, yaw, size);
                    ringFacilities.Add(f);
                    ringSlotUsed[k] = true;
                    return k;
                }
            }
            // 予備：広場の周り（少し遠く）のどこか
            var rr = R("ring.fallback." + type);
            for (int i = 0; i < 300; i++)
            {
                float ang = rr.Range(0f, 360f);
                float rad = rr.Range(34f, 75f);
                var c = plaza + Fwd2(ang) * rad;
                if (!SiteOk(c, FootRadius(size) * 0.85f, false, relaxed ? 7f : 5f)) continue;
                var f = AddBuilding(type, IdOf(type), c, YawOf(plaza - c), size);
                ringFacilities.Add(f);
                return -1;
            }
            W.Notes.Add("施設を置けない: " + type);
            return -1;
        }

        // =================================================================
        // 主人公の自宅と農場（村仕様 §6）
        // =================================================================

        private void PlaceHomeAndFarm()
        {
            var r = R("home");
            float m = WorldTuning.FarmExpansionMargin;
            float hw = WorldTuning.FarmWidth * 0.5f, hd = WorldTuning.FarmDepth * 0.5f;
            Vector2 best = Vector2.zero;
            float bestScore = float.MinValue;
            for (int k = 0; k < 600; k++)
            {
                float ang = r.Range(0f, Mathf.PI * 2f);
                float dist = r.Range(WorldTuning.HomeDistMin, WorldTuning.HomeDistMax);
                var c = plaza + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                var reserve = new Rect(c.x - hw - m, c.y - hd - m, (hw + m) * 2f, (hd + m) * 2f);
                if (!GeoUtil.InsideWorld(reserve.min, 10f) || !GeoUtil.InsideWorld(reserve.max, 10f)) continue;
                if (S.CoastD(c.x, c.y) < 45f) continue;
                if (S.MountainFactor(c.x, c.y) < 1.15f) continue;
                if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + 30f) continue;
                if (RectHasWater(reserve, 2f)) continue;
                if (!QuietClearOk(c)) continue;
                float rough = RectRoughness(new Rect(c.x - hw, c.y - hd, hw * 2f, hd * 2f));
                if (rough > (relaxed ? 7f : 4f)) continue;
                float halfDiag = new Vector2(hw + m, hd + m).magnitude;
                bool overlap = false;
                foreach (var pad in W.Pads) if (pad.SignedDistance(c.x, c.y) < halfDiag + 3f) overlap = true;
                foreach (var fp in footprints) if (fp.SignedDistance(c.x, c.y) < halfDiag + 3f) overlap = true;
                if (overlap) continue;
                float score = -Mathf.Abs(dist - 80f) * 0.4f - rough * 3f + r.Range(0f, 2f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (bestScore == float.MinValue)
            {
                W.Notes.Add("農場の候補なし");
                best = plaza + (plaza.magnitude > 1f ? -plaza.normalized : Vector2.right) * 80f;
            }
            farmCenter = best;
            L.FarmRect = new Rect(best.x - hw, best.y - hd, hw * 2f, hd * 2f);
            var farmPad = AddPad(PadKind.Farm, best, new Vector2(hw + m, hd + m), 0f, false, 6f, "farm");
            W.FarmReserve = new Rect(best.x - hw - m, best.y - hd - m, (hw + m) * 2f, (hd + m) * 2f);

            // 家は農場の村側に建てる（残り3方向へ農場を広げられる）
            var v = plaza - best;
            Vector2 houseC;
            float yaw;
            if (Mathf.Abs(v.x) > Mathf.Abs(v.y))
            {
                float sx = Mathf.Sign(v.x);
                houseC = best + new Vector2(sx * (hw + 6.5f), 0f);
                yaw = sx > 0 ? 90f : -90f;
            }
            else
            {
                float sz = Mathf.Sign(v.y);
                houseC = best + new Vector2(0f, sz * (hd + 6.5f));
                yaw = sz > 0 ? 0f : 180f;
            }
            AddBuilding(FacilityType.PlayerHouse, IdOf(FacilityType.PlayerHouse), houseC, yaw, SizeOf(FacilityType.PlayerHouse), farmPad.TargetY);
        }

        private bool RectHasWater(Rect r, float margin)
        {
            for (float x = r.xMin - margin; x <= r.xMax + margin; x += 2f)
                for (float z = r.yMin - margin; z <= r.yMax + margin; z += 2f)
                {
                    var k = F.KindAt(x, z);
                    if (k != WaterKind.None) return true;
                }
            return false;
        }

        private float RectRoughness(Rect r)
        {
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    float h = F.SampleTerrain(Mathf.Lerp(r.xMin, r.xMax, i / 4f), Mathf.Lerp(r.yMin, r.yMax, j / 4f));
                    mn = Mathf.Min(mn, h);
                    mx = Mathf.Max(mx, h);
                }
            return mx - mn;
        }

        // =================================================================
        // 不時着地（村仕様 §9、ロケット仕様 §11・§15）
        // =================================================================

        private void PlaceCrashSite()
        {
            var r = R("crash");
            Vector2 best = Vector2.zero;
            float bestScore = float.MinValue;
            for (int k = 0; k < 600; k++)
            {
                float ang = r.Range(0f, Mathf.PI * 2f);
                float dist = r.Range(WorldTuning.CrashDistMin, WorldTuning.CrashDistMax);
                var c = plaza + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                if (!GeoUtil.InsideWorld(c, 26f)) continue;
                if (S.CoastD(c.x, c.y) < 30f) continue;
                if (S.MountainFactor(c.x, c.y) < 1.08f) continue;
                if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + 16f) continue;
                if (F.WaterNear(c.x, c.y, 20f)) continue;
                if (Vector2.Distance(c, farmCenter) < 58f) continue;
                if (V2 && S.HasQuiet && Vector2.Distance(c, S.QuietCenter) < WorldTuning.QuietHouseClearance) continue;
                float rough = F.Roughness(c.x, c.y, 13f);
                if (rough > (relaxed ? 8f : 5f)) continue;
                bool overlap = false;
                foreach (var pad in W.Pads) if (pad.SignedDistance(c.x, c.y) < 18f) overlap = true;
                if (overlap) continue;
                float score = -Mathf.Abs(dist - 130f) * 0.15f - rough * 2f + r.Range(0f, 3f);
                if (S.IsForest(c.x, c.y)) score -= 6f; // 開けた場所
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (bestScore == float.MinValue)
            {
                W.Notes.Add("不時着地の候補なし");
                best = ClampInside(plaza + S.Perp * 120f, 30f);
            }
            crashCenter = best;
            AddPad(PadKind.Crater, best, new Vector2(WorldTuning.CrashCraterRadius + 4f, 0f), 0f, true, 6f, IdOf(FacilityType.RocketSite));
            var toPlaza = (plaza - best).normalized;
            var entrance = best + toPlaza * (WorldTuning.CrashCraterRadius + 3f);
            AddFacility(FacilityType.RocketSite, IdOf(FacilityType.RocketSite), best, YawOf(toPlaza), SizeOf(FacilityType.RocketSite), entrance);
            L.CrashSite = new Vector3(best.x, 0f, best.y);
            var spawn = best + toPlaza * (WorldTuning.CrashCraterRadius + 6f);
            L.PlayerSpawn = new Vector3(spawn.x, 0f, spawn.y);
        }

        // =================================================================
        // 鉱山入口（村仕様 §8）
        // =================================================================

        private void PlaceMineEntrance()
        {
            var baseDir = (plaza - S.MountainCenter).normalized;
            float[] offsets = { 0f, 18f, -18f, 36f, -36f, 54f, -54f, 72f, -72f };
            Vector2 found = Vector2.zero;
            bool ok = false;
            foreach (var off in offsets)
            {
                var d = Rotate(baseDir, off);
                var foot = S.MountainCenter + d * S.MountainRadius * 1.5f;
                float footH = F.SampleTerrain(foot.x, foot.y);
                for (float t = S.MountainRadius * 0.3f; t < S.MountainRadius * 1.4f; t += 2f)
                {
                    var p = S.MountainCenter + d * t;
                    if (!GeoUtil.InsideWorld(p, 18f)) continue;
                    float h = F.SampleTerrain(p.x, p.y);
                    if (h - footH > S.MountainPeak * 0.34f) continue;
                    if (V2 && S.HasQuiet && Vector2.Distance(p, S.QuietCenter) < WorldTuning.QuietRoadClearance + 15f) continue;
                    if (DistToRivers(p) < 18f) break;
                    if (F.WaterNear(p.x, p.y, 10f)) break;
                    bool overlap = false;
                    foreach (var pad in W.Pads) if (pad.SignedDistance(p.x, p.y) < 16f) overlap = true;
                    if (overlap) break;
                    found = p;
                    ok = true;
                    break;
                }
                if (ok) break;
            }
            if (!ok)
            {
                W.Notes.Add("鉱山入口の候補なし");
                found = ClampInside(S.MountainCenter + baseDir * S.MountainRadius * 0.9f, 20f);
            }
            mineMouth = found;
            var dirOut = (plaza - S.MountainCenter).normalized;
            // 山の斜面の「外向き」（山頂から離れる向き）を入口の正面にする
            var outward = (found - S.MountainCenter).normalized;
            if (outward.sqrMagnitude < 0.01f) outward = dirOut;
            float yaw = YawOf(outward);
            float h0 = F.SampleTerrain(found.x, found.y);
            AddPad(PadKind.MineMouth, found + outward * 5.5f, new Vector2(7f, 0f), 0f, true, 4.5f, IdOf(FacilityType.MineEntrance), h0);
            var entrance = found + outward * 4f;
            AddFacility(FacilityType.MineEntrance, IdOf(FacilityType.MineEntrance), found, yaw, SizeOf(FacilityType.MineEntrance), entrance);
            AddFootprint(found - outward * 0.5f, new Vector2(3.6f, 1.6f), yaw);
            L.MineEntrance = new Vector3(found.x, 0f, found.y);
        }

        private static Vector2 Rotate(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // =================================================================
        // 高台の見晴らし台（村仕様 §3.1、時間天候仕様 §12：星の光帯を見に住民が集まる）
        // =================================================================

        private void PlaceLookout()
        {
            Vector2 best = S.HillCenter;
            float bestH = float.MinValue;
            for (int i = 0; i < 49; i++)
            {
                var p = S.HillCenter + new Vector2((i % 7 - 3) * 3f, (i / 7 - 3) * 3f);
                if (F.WaterNear(p.x, p.y, 9f)) continue;
                bool overlap = false;
                foreach (var pad in W.Pads) if (pad.SignedDistance(p.x, p.y) < 10f) overlap = true;
                if (overlap) continue;
                float h = F.SampleTerrain(p.x, p.y);
                if (h > bestH)
                {
                    bestH = h;
                    best = p;
                }
            }
            if (bestH == float.MinValue)
            {
                // 丘が使えない時は、村から離れた高い場所を探す
                var r = R("lookout");
                for (int k = 0; k < 300; k++)
                {
                    var p = new Vector2(r.Range(-160f, 160f), r.Range(-160f, 160f));
                    if (!SiteOk(p, 7f, false, 8f)) continue;
                    float h = F.SampleTerrain(p.x, p.y);
                    if (h > bestH)
                    {
                        bestH = h;
                        best = p;
                    }
                }
                if (bestH == float.MinValue) W.Notes.Add("高台の候補なし");
            }
            lookoutCenter = best;
            AddPad(PadKind.Lookout, best, new Vector2(7f, 0f), 0f, true, 5f, IdOf(FacilityType.Lookout), F.SampleTerrain(best.x, best.y));
            var toPlaza = (plaza - best).normalized;
            AddFacility(FacilityType.Lookout, IdOf(FacilityType.Lookout), best, YawOf(toPlaza), SizeOf(FacilityType.Lookout), best + toPlaza * 6f);
        }

        // =================================================================
        // 釣り名人の小屋（水辺）
        // =================================================================

        private Vector2 fishingHutDockDir;

        private void PlaceFishingHut()
        {
            var r = R("fishinghut");
            var cands = new List<Vector2>();
            foreach (var rv in W.Rivers)
            {
                int start = Mathf.RoundToInt(rv.Points.Count * 0.2f);
                for (int i = start; i < rv.Points.Count - 1; i++)
                {
                    var dir = (rv.Points[i + 1] - rv.Points[i]).normalized;
                    var n = new Vector2(-dir.y, dir.x);
                    cands.Add(rv.Points[i] + n * (rv.Radius[i] + 7.5f));
                    cands.Add(rv.Points[i] - n * (rv.Radius[i] + 7.5f));
                }
            }
            foreach (var pd in W.Ponds)
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.PI * 2f;
                    cands.Add(pd.Center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (pd.Radius + 7.5f));
                }
            for (float t = -170f; t <= 170f; t += 10f) cands.Add(S.CoastPoint(t, 13f));

            Vector2 best = Vector2.zero;
            float bestScore = float.MinValue;
            foreach (var c in cands)
            {
                float d = Vector2.Distance(c, plaza);
                if (d < 30f || d > 125f) continue;
                if (!GeoUtil.InsideWorld(c, 16f)) continue;
                if (F.WaterNear(c.x, c.y, 3.5f)) continue;
                if (!F.WaterNear(c.x, c.y, 12f)) continue;
                if (!SiteOk(c, 4.2f, true, 3.8f)) continue;
                float score = -d + r.Range(0f, 8f);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            if (bestScore == float.MinValue)
            {
                // 予備：一番近い水辺
                foreach (var c in cands)
                {
                    if (!GeoUtil.InsideWorld(c, 12f) || F.WaterNear(c.x, c.y, 3f)) continue;
                    if (!SiteOk(c, 3.5f, true, 7f, false, 1f)) continue;
                    float score = -Vector2.Distance(c, plaza);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                if (bestScore == float.MinValue)
                {
                    W.Notes.Add("釣り小屋の候補なし");
                    best = plaza + Vector2.one * 40f;
                }
            }
            // 水のある方向
            var waterDir = Vector2.zero;
            for (float dist = 4f; dist <= 13f && waterDir == Vector2.zero; dist += 1.5f)
                for (int k = 0; k < 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    var dd = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    var p = best + dd * dist;
                    if (F.KindAt(p.x, p.y) != WaterKind.None)
                    {
                        waterDir = dd;
                        break;
                    }
                }
            if (waterDir == Vector2.zero) waterDir = (best - plaza).normalized;
            fishingHutDockDir = waterDir;
            // 扉は陸側（水を背にする）
            float yaw = YawOf(-waterDir);
            AddBuilding(FacilityType.FishingHut, IdOf(FacilityType.FishingHut), best, yaw, SizeOf(FacilityType.FishingHut));
        }

        // =================================================================
        // 桟橋（海岸・村仕様 §2.2 ⑤）
        // =================================================================

        private void PlacePier()
        {
            if (V2)
            {
                PlacePierV2();
                return;
            }
            float bestD = float.MaxValue;
            for (float t = -165f; t <= 165f; t += 6f)
            {
                var start = S.CoastPoint(t, 6f);
                var end = S.CoastPoint(t, -15f);
                if (!GeoUtil.InsideWorld(start, 16f) || !GeoUtil.InsideWorld(end, 6f)) continue;
                if (DistToRivers(start) < 26f || DistToRivers(end) < 22f) continue;
                if (F.KindAt(end.x, end.y) != WaterKind.Sea) continue;
                bool overlap = false;
                foreach (var pad in W.Pads) if (pad.SignedDistance(start.x, start.y) < 8f) overlap = true;
                foreach (var fp in footprints) if (fp.SignedDistance(start.x, start.y) < 6f) overlap = true;
                if (overlap) continue;
                float d = Vector2.Distance(start, plaza);
                if (d > 200f) continue;
                if (d < bestD)
                {
                    bestD = d;
                    W.HasPier = true;
                    W.PierStart = new Vector3(start.x, 0f, start.y);
                    W.PierEnd = new Vector3(end.x, 0f, end.y);
                }
            }
            if (W.HasPier) reserved.Add((GeoUtil.XZ(W.PierStart), 4f));
        }
    }
}
