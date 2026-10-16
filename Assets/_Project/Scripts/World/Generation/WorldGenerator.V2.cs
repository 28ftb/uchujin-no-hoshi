using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // 生成バージョン2（マップ設計 v0.2 §2 の設計決定）
    //  ・湖を必ず作る（大きさ・最深部・浅場・中層・泥底・砂泥底・濁り・静かな岸・霧・風が弱まる地形・最深部へ届く岬）§2.3
    //  ・川を区間に分ける（急流・早瀬・瀬・淵・よどみ・水草帯・岩場・冷たい湧水）§2.4
    //  ・静かな高台（家・本道・見晴らし台から離れ、木立・草地・小さな池がある）§2.6
    //  ・8m以上の滝を必ず作り、滝つぼと滝の裏の洞を付ける §2.10
    //  ・桟橋を必ず作り、沖まで届かせる §2.9
    //  ・鍛冶屋・修理屋は暮らしの谷の中（広場から本道で25秒以内）§2.12
    //  ・種ごとの検証に必要な地形の条件を確かめる §12
    // 版1の星では、ここの処理は呼ばれない（V2 が false）。乱数のキーも版ごとに別（"world.gen2..."）。
    // =====================================================================
    public sealed partial class WorldGenerator
    {
        private readonly List<FishingSpot> riverSpotsV2 = new List<FishingSpot>();
        private readonly Dictionary<FishingSpot, float> spotGradient = new Dictionary<FishingSpot, float>();
        private readonly Dictionary<FishingSpot, float> spotBend = new Dictionary<FishingSpot, float>();
        private readonly Dictionary<FishingSpot, int> spotRiver = new Dictionary<FishingSpot, int>();
        private readonly List<Vector2> highlandTrees = new List<Vector2>();
        private Vector2 quietShorePoint;
        private bool hasQuietShore;

        // =================================================================
        // 湖（§2.3）
        // =================================================================

        private void PlaceLake()
        {
            var r = R("lake");
            // 岸の波打ち（先に決め、一番狭い所でも差し渡し LakeMinDiameter 以上になる半径にする）
            float a1 = r.Range(0.04f, 0.1f), p1 = r.Range(0f, Mathf.PI * 2f);
            float a2 = r.Range(0.02f, 0.06f), p2 = r.Range(0f, Mathf.PI * 2f);
            float minBase = (WorldTuning.LakeMinDiameter * 0.5f + 0.5f) / (1f - a1 - a2);
            float baseR = Mathf.Max(minBase, r.Range(WorldTuning.LakeMinDiameter * 0.5f + 1f, 56f));
            float wobble = 1f + a1 + a2;
            Vector2 best = Vector2.zero;
            float bestScore = float.MinValue;
            float usedR = baseR;
            float[] radii = { baseR, Mathf.Max(minBase, baseR * 0.95f), minBase, minBase };
            for (int pass = 0; pass < radii.Length; pass++)
            {
                float R0 = radii[pass];
                // 最後の段は条件を少しゆるめる（海・山・丘に少し近くてもよい）
                bool loose = pass == radii.Length - 1;
                for (int k = 0; k < 1000; k++)
                {
                    var c = new Vector2(r.Range(-150f, 150f), r.Range(-150f, 150f));
                    if (!GeoUtil.InsideWorld(c, R0 * wobble + (loose ? 10f : 16f))) continue;
                    if (S.CoastD(c.x, c.y) < R0 * wobble + (loose ? 22f : 30f)) continue;
                    if (Vector2.Distance(c, S.MountainCenter) < S.MountainRadius * (loose ? 0.55f : 0.7f) + R0) continue;
                    if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + R0 * wobble + (loose ? 6f : 14f)) continue;
                    // 湖の岸と静かな高台の間をあける（湖へ行く本道は岬へ向かい、岬は高台の反対側に向ける）
                    if (S.HasQuiet && Vector2.Distance(c, S.QuietCenter) < S.QuietRadius + R0 * wobble + 25f) continue;
                    if (DistToRivers(c) < R0 * wobble + 10f) continue;
                    float score = -F.Roughness(c.x, c.y, R0 * 0.7f) + r.Range(0f, 4f);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = c;
                    }
                }
                if (bestScore > float.MinValue)
                {
                    usedR = R0;
                    break;
                }
            }
            if (bestScore == float.MinValue)
            {
                W.Notes.Add("湖の候補なし");
                return;
            }
            var lk = new LakeInfo
            {
                Center = best,
                BaseRadius = usedR,
                A1 = a1,
                P1 = p1,
                A2 = a2,
                P2 = p2,
                MaxDepth = r.Range(WorldTuning.LakeMinMaxDepth + 1f, WorldTuning.LakeMinMaxDepth + 3.5f)
            };
            // 岬（陸）は星の中心の側へ突き出す（村から行きやすい側）。反対側が道から遠い静かな岸になりやすい。
            float spit = best.sqrMagnitude > 1f ? LakeInfo.Angle(-best) + r.Range(-0.6f, 0.6f) : r.Range(0f, Mathf.PI * 2f);
            // 岬（本道の行き先）は静かな高台の方へ向けない
            if (S.HasQuiet && LakeInfo.AngleDiff(spit, LakeInfo.Angle(S.QuietCenter - best)) < 1.2f) spit += Mathf.PI * 0.6f;
            lk.SpitAngle = spit;
            lk.ShelfAngle = spit + 1.9f;
            lk.SteepAngle = spit - 1.9f;
            lk.MudAngle = spit + Mathf.PI;
            lk.SandMudAngle = spit + 1.0f;
            lk.QuietAngle = spit + Mathf.PI;
            float rs = lk.RadiusAt(spit);
            lk.SpitBase = best + lk.Dir(spit) * (rs + 3f);
            lk.SpitTip = best + lk.Dir(spit) * (rs * 0.42f);
            // 水面：岸のすぐ外の一番低い地面より少し下
            float rim = float.MaxValue;
            for (int i = 0; i < 48; i++)
            {
                float a = i / 48f * Mathf.PI * 2f;
                var q = best + lk.Dir(a) * (lk.RadiusAt(a) + 2f);
                rim = Mathf.Min(rim, F.SampleTerrain(q.x, q.y));
            }
            lk.Surface = Mathf.Max(WorldTuning.SeaLevel + 0.8f, Mathf.Min(rim - 0.35f, F.SampleTerrain(best.x, best.y) - 0.3f));
            lk.WaterIndex = L.Waters.Count;
            var body = new WaterBody { id = "water.lake.0", type = WaterType.Lake, radius = usedR, surfaceHeight = lk.Surface };
            body.points.Add(new Vector3(best.x, lk.Surface, best.y));
            L.Waters.Add(body);
            W.Lakes.Add(lk);
        }

        /// <summary>山から湖へ注ぐ小川（流れ込み口は濁り）。</summary>
        private void GenerateLakeInlet(SeededRandom r)
        {
            if (W.Lakes.Count == 0 || W.Rivers.Count == 0) return;
            var lk = W.Lakes[0];
            var main = W.Rivers[0];
            var toLake = (lk.Center - S.MountainCenter).normalized;
            for (int tries = 0; tries < 16; tries++)
            {
                var d2 = Rotate(toLake, r.Range(-38f, 38f));
                var src2 = ClampInside(S.MountainCenter + d2 * S.MountainRadius * r.Range(0.5f, 0.68f), 20f);
                var dirIn = (src2 - lk.Center).normalized;
                float ang = LakeInfo.Angle(dirIn);
                if (LakeInfo.AngleDiff(ang, lk.SpitAngle) < 0.55f) continue;
                var end = lk.Center + dirIn * (lk.RadiusAt(ang) * 0.85f);
                if (Vector2.Distance(src2, end) < 40f) continue;
                var trib = MakeRiver(src2, end, r, 0.7f, false, -1, lk.Surface);
                if (MinDistBetweenRivers(trib, main) < 22f)
                {
                    L.Waters.RemoveAt(L.Waters.Count - 1);
                    continue;
                }
                W.Rivers.Add(trib);
                lk.HasInlet = true;
                lk.Inlet = lk.Center + dirIn * lk.RadiusAt(ang);
                if (LakeInfo.AngleDiff(lk.MudAngle, ang) < 0.7f) lk.MudAngle = ang + 2.2f;
                if (LakeInfo.AngleDiff(lk.SandMudAngle, ang) < 0.6f) lk.SandMudAngle = ang - 1.4f;
                return;
            }
        }

        private void CarveLake(LakeInfo lk)
        {
            float reach = lk.BaseRadius * 1.15f + 9f;
            F.ToGrid(lk.Center.x - reach, lk.Center.y - reach, out int x0, out int z0);
            F.ToGrid(lk.Center.x + reach, lk.Center.y + reach, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(F.XOf(ix), F.ZOf(iz));
                    var v = p - lk.Center;
                    float d = v.magnitude;
                    float rr = lk.RadiusAt(LakeInfo.Angle(v));
                    int idx = F.Index(ix, iz);
                    float h = F.H[idx];
                    bool spit = lk.InSpit(p) && d < rr + 1.5f;
                    if (spit)
                    {
                        // 岬：水面より少し高い細い陸（最深部の近くまで歩いて行ける）
                        h = lk.Surface + 0.55f;
                    }
                    else if (d < rr)
                    {
                        h = Mathf.Min(h, lk.Surface - lk.DepthAt(p) - 0.1f);
                    }
                    else if (d < rr + 8f)
                    {
                        float target = lk.Surface + 0.3f + (d - rr) * 0.4f;
                        if (h > target) h = target + (h - target) * GeoUtil.SmoothStep(rr, rr + 8f, d);
                        if (d < rr + 2.5f && h < lk.Surface + 0.3f) h = lk.Surface + 0.3f;
                    }
                    F.H[idx] = h;
                    if (!spit && d < rr + 1.2f)
                    {
                        F.WaterY[idx] = lk.Surface;
                        F.Kind[idx] = WaterKind.Lake;
                        F.WaterIndex[idx] = (short)lk.WaterIndex;
                    }
                }
            }
        }

        // =================================================================
        // 池（森の池2〜4か所＋静かな高台の池）
        // =================================================================

        private void GenerateForestPondsV2()
        {
            var r = R("ponds");
            int target = r.Range(3, 5);
            int guard = 0;
            while (W.Ponds.Count < target && guard++ < 600)
            {
                var c = new Vector2(r.Range(-165f, 165f), r.Range(-165f, 165f));
                float radius = r.Range(WorldTuning.PondRadiusMin + 1f, WorldTuning.PondRadiusMax + 2f);
                if (S.CoastD(c.x, c.y) < 34f) continue;
                if (S.MountainFactor(c.x, c.y) < 1.0f) continue;
                if (Vector2.Distance(c, S.HillCenter) < S.HillRadius + radius + 8f) continue;
                if (S.HasQuiet && Vector2.Distance(c, S.QuietCenter) < S.QuietRadius + radius + 10f) continue;
                if (DistToRivers(c) < radius + 16f) continue;
                bool near = false;
                foreach (var p in W.Ponds) if (Vector2.Distance(p.Center, c) < p.Radius + radius + 24f) near = true;
                foreach (var lk in W.Lakes) if (Vector2.Distance(lk.Center, c) < lk.BaseRadius * 1.12f + radius + 22f) near = true;
                if (near) continue;
                if (F.Roughness(c.x, c.y, radius) > 3.2f) continue;
                // 最初の2つは森の池（森の中か森のすぐそば）
                if (W.Ponds.Count < 2 && guard < 450 && S.Forest01(c.x, c.y) < S.ForestThreshold - 0.08f) continue;
                MakePond(c, radius);
            }
        }

        private void PlaceHighlandPond()
        {
            if (!S.HasQuiet) return;
            var r = R("highland");
            for (int k = 0; k < 40; k++)
            {
                float a = r.Range(0f, Mathf.PI * 2f);
                var c = S.QuietCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * S.QuietRadius * r.Range(0.12f, 0.32f);
                float pr = r.Range(6.5f, 8.5f);
                if (F.Roughness(c.x, c.y, pr) > 2.6f) continue;
                MakePond(c, pr);
                W.HighlandPondIndex = W.Ponds.Count - 1;
                return;
            }
            W.Notes.Add("静かな高台の池を置けない");
        }

        // =================================================================
        // 8m以上の滝（§2.10）：上流側を崖として盛り、落ち口で切る。滝つぼは深く掘る。
        // =================================================================

        private void ForceWaterfall(RiverPath rp)
        {
            int n = rp.Points.Count;
            int limit = Mathf.Min(n - 2, Mathf.Max(4, Mathf.RoundToInt(n * 0.6f)));
            int f = -1;
            float bestDrop = float.MinValue;
            for (int i = 3; i < limit; i++)
            {
                var a = rp.Points[i - 2];
                var b = rp.Points[i + 1];
                float drop = F.SampleTerrain(a.x, a.y) - F.SampleTerrain(b.x, b.y);
                if (drop > bestDrop)
                {
                    bestDrop = drop;
                    f = i;
                }
            }
            if (f < 1) return;
            float bottom = rp.Surface[f];
            float top = bottom + WorldTuning.WaterfallMinDrop + 1f;
            rp.Surface[f - 1] = Mathf.Max(rp.Surface[f - 1], top);
            for (int j = f - 2; j >= 0; j--) rp.Surface[j] = Mathf.Max(rp.Surface[j], rp.Surface[j + 1]);
            // 上流側の崖：落ち口の線より上流の地面を水面より高く盛る（落ち口でくっきり切れる）
            var flow = (rp.Points[f] - rp.Points[f - 1]).normalized;
            var lip = rp.Points[f - 1] + flow * 1.0f;
            for (int j = 0; j < f; j++)
            {
                var p = rp.Points[j];
                if (F.SampleTerrain(p.x, p.y) >= rp.Surface[j] + 1.4f && j < f - 2) continue;
                RaiseLedge(p, rp.Radius[j] + 12f, rp.Surface[j] + 1.4f, lip, flow);
            }
            while (rp.ExtraDepth.Count < n) rp.ExtraDepth.Add(0f);
            for (int i = f; i <= Mathf.Min(n - 1, f + 2); i++)
            {
                rp.ExtraDepth[i] = WorldTuning.PlungePoolExtraDepth;
                rp.Radius[i] *= 1.45f;
            }
            rp.WaterfallIndex = f;
            rp.WaterfallDrop = rp.Surface[f - 1] - rp.Surface[f];
        }

        /// <summary>
        /// 歩いて渡れる浅瀬（瀬）を作る（§2.7）。勾配2〜4%の所の川底を浅くする。本流で足りなければ中流に2か所足す。
        /// 浅瀬は隣り合う2点にまたがる（幅6m前後）。
        /// </summary>
        private void MakeFords(RiverPath rp, bool isMain)
        {
            int n = rp.Points.Count;
            while (rp.ExtraDepth.Count < n) rp.ExtraDepth.Add(0f);
            const float shallow = -(WorldTuning.RiverDepth - WorldLayout.WadeableDepth + 0.25f); // 川底の余分な0.15mも含めて最深0.5m前後
            System.Func<int, bool> nearFall = i => rp.WaterfallIndex > 0 && i >= rp.WaterfallIndex - 2 && i <= rp.WaterfallIndex + 4;
            for (int i = 2; i < n - 2; i++)
            {
                if (nearFall(i) || rp.Fords.Contains(i - 1)) continue;
                float len = (rp.Points[i + 1] - rp.Points[i - 1]).magnitude;
                float grad = len > 0.1f ? (rp.Surface[i - 1] - rp.Surface[i + 1]) / len : 0f;
                if (grad < 0.02f || grad >= 0.04f) continue;
                rp.ExtraDepth[i] = shallow;
                rp.ExtraDepth[i + 1] = shallow;
                rp.Fords.Add(i);
            }
            if (!isMain || rp.Fords.Count >= 2) return;
            int lo = Mathf.RoundToInt(n * 0.3f), hi = Mathf.RoundToInt(n * 0.8f);
            for (int k = 0; k < 2 && rp.Fords.Count < 2; k++)
            {
                int bestI = -1;
                float bestScore = float.MinValue;
                for (int i = Mathf.Max(2, lo); i < Mathf.Min(n - 2, hi); i++)
                {
                    if (nearFall(i)) continue;
                    bool clash = false;
                    foreach (var f in rp.Fords) if (Mathf.Abs(f - i) < 8) clash = true;
                    if (clash) continue;
                    float score = -Mathf.Abs(i - (lo + (hi - lo) * (k == 0 ? 0.35f : 0.7f)));
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestI = i;
                    }
                }
                if (bestI < 0) break;
                rp.ExtraDepth[bestI] = shallow;
                rp.ExtraDepth[bestI + 1] = shallow;
                rp.Fords.Add(bestI);
            }
        }

        /// <summary>浅瀬に飛び石を並べる（見た目。小さい石なので通行の邪魔にならない）。</summary>
        private void PlaceSteppingStonesV2()
        {
            foreach (var rv in W.Rivers)
            {
                foreach (int i in rv.Fords)
                {
                    if (i + 1 >= rv.Points.Count) continue;
                    var c = Vector2.Lerp(rv.Points[i], rv.Points[i + 1], 0.5f);
                    var dir = (rv.Points[i + 1] - rv.Points[i]).normalized;
                    var nrm = new Vector2(-dir.y, dir.x);
                    float rr = rv.Radius[i];
                    for (float t = -rr; t <= rr + 0.01f; t += 1.5f)
                        AddNature(NatureKind.Rock, c + nrm * t, t * 37f, 0.45f, 0);
                }
            }
        }

        private void RaiseLedge(Vector2 c, float radius, float top, Vector2 lip, Vector2 flow)
        {
            F.ToGrid(c.x - radius, c.y - radius, out int x0, out int z0);
            F.ToGrid(c.x + radius, c.y + radius, out int x1, out int z1);
            for (int iz = z0; iz <= z1; iz++)
            {
                for (int ix = x0; ix <= x1; ix++)
                {
                    var p = new Vector2(F.XOf(ix), F.ZOf(iz));
                    if (Vector2.Dot(p - lip, flow) > 0f) continue;
                    float d = Vector2.Distance(p, c);
                    if (d > radius) continue;
                    float w = 1f - GeoUtil.SmoothStep(radius - 6f, radius, d);
                    int idx = F.Index(ix, iz);
                    float h = F.H[idx];
                    if (h < top) F.H[idx] = Mathf.Lerp(h, top, w);
                }
            }
        }

        /// <summary>滝の裏の洞の入口（滝つぼの脇の崖の下）。</summary>
        private void PlaceWaterfallCave()
        {
            if (W.Rivers.Count == 0 || W.Rivers[0].WaterfallIndex < 1) return;
            var rv = W.Rivers[0];
            int i = rv.WaterfallIndex;
            var flow = (rv.Points[i] - rv.Points[i - 1]).normalized;
            var side = new Vector2(-flow.y, flow.x);
            var bottom = rv.Points[i];
            float rr = rv.Radius[i];
            for (int s = 0; s < 2; s++)
            {
                var sd = s == 0 ? side : -side;
                for (float off = rr + 1.8f; off < rr + 7f; off += 0.8f)
                {
                    var p = bottom + sd * off - flow * 1.2f;
                    if (!GeoUtil.InsideWorld(p, 4f) || F.IsWater(p.x, p.y)) continue;
                    W.Cave = new WaterfallCaveInfo
                    {
                        Entrance = new Vector3(p.x, F.SampleGround(p.x, p.y), p.y),
                        Yaw = YawOf(flow)
                    };
                    reserved.Add((p, 2.5f));
                    return;
                }
            }
        }

        // =================================================================
        // 村の配置の追加条件（静かな高台・静かな岸から離す）
        // =================================================================

        /// <summary>建物・家を置いてよいか（静かな高台・湖の静かな岸から離す）。</summary>
        private bool QuietClearOk(Vector2 c)
        {
            if (!V2) return true;
            if (S.HasQuiet && Vector2.Distance(c, S.QuietCenter) < WorldTuning.QuietHouseClearance) return false;
            if (hasQuietShore && Vector2.Distance(c, quietShorePoint) < WorldTuning.QuietShoreClearance + 8f) return false;
            return true;
        }

        /// <summary>広場が決まったら、湖の静かな岸を広場の反対側に決め直す。</summary>
        private void DecideQuietShore()
        {
            if (W.Lakes.Count == 0) return;
            var lk = W.Lakes[0];
            var away = lk.Center - plaza;
            if (away.sqrMagnitude > 1f)
            {
                float a = LakeInfo.Angle(away);
                if (LakeInfo.AngleDiff(a, lk.SpitAngle) < 0.9f) a += 1.2f;
                lk.QuietAngle = a;
            }
            quietShorePoint = lk.Center + lk.Dir(lk.QuietAngle) * lk.RadiusAt(lk.QuietAngle);
            hasQuietShore = true;
        }

        private float ExtraCostV2(Vector2 c)
        {
            float extra = 0f;
            if (S.HasQuiet)
            {
                float d = Vector2.Distance(c, S.QuietCenter);
                if (d < WorldTuning.QuietRoadClearance + 6f) extra += 30f;
                else if (d < WorldTuning.QuietRoadClearance + 26f) extra += 6f;
            }
            if (hasQuietShore && Vector2.Distance(c, quietShorePoint) < WorldTuning.QuietShoreClearance + 10f) extra += 5f;
            return extra;
        }

        /// <summary>静かな高台への小道（本道ではない。住民の予定では使わない）。</summary>
        private void BuildQuietTrail()
        {
            if (!S.HasQuiet) return;
            int near = NearestNode(S.QuietCenter);
            if (near < 0) return;
            var from = NodeXZ(near);
            var dir = (from - S.QuietCenter).normalized;
            var to = S.QuietCenter + dir * S.QuietRadius * 0.5f;
            int before = W.RoadSegments.Count;
            var path = Route(from, to, "quiet_trail", false, 0.9f);
            for (int i = before; i < W.RoadSegments.Count; i++) W.RoadSegments[i].Trail = true;
            if (path == null) W.Notes.Add("静かな高台への小道なし");
        }

        /// <summary>道のりで広場から dist 以内の点だけを使う（鍛冶屋・修理屋の離れすぎを防ぐ）。</summary>
        private static float PathDistanceTo(RoadPathInfo path, int index)
        {
            float d = 0f;
            for (int i = 1; i <= index && i < path.Points.Count; i++) d += (path.Points[i] - path.Points[i - 1]).magnitude;
            return d;
        }

        // =================================================================
        // 桟橋（§2.9）：必ず作り、沖まで届かせる
        // =================================================================

        private void PlacePierV2()
        {
            for (int pass = 0; pass < 2 && !W.HasPier; pass++)
            {
                float riverGap = pass == 0 ? 22f : 10f;
                float bestD = float.MaxValue;
                for (float t = -190f; t <= 190f; t += 5f)
                {
                    var start = S.CoastPoint(t, 6f);
                    var end = S.CoastPoint(t, -WorldTuning.PierReachV2);
                    if (!GeoUtil.InsideWorld(start, 14f) || !GeoUtil.InsideWorld(end, pass == 0 ? 5f : 2f)) continue;
                    if (DistToRivers(start) < riverGap || DistToRivers(end) < riverGap * 0.8f) continue;
                    if (F.KindAt(end.x, end.y) != WaterKind.Sea) continue;
                    bool overlap = false;
                    foreach (var pad in W.Pads) if (pad.SignedDistance(start.x, start.y) < 8f) overlap = true;
                    foreach (var fp in footprints) if (fp.SignedDistance(start.x, start.y) < 6f) overlap = true;
                    if (overlap) continue;
                    float d = Vector2.Distance(start, plaza);
                    if (d < bestD)
                    {
                        bestD = d;
                        W.HasPier = true;
                        W.PierStart = new Vector3(start.x, 0f, start.y);
                        W.PierEnd = new Vector3(end.x, 0f, end.y);
                    }
                }
            }
            if (W.HasPier) reserved.Add((GeoUtil.XZ(W.PierStart), 4f));
            else W.Notes.Add("桟橋なし");
        }

        // =================================================================
        // 静かな高台の自然（木立・草地・花）
        // =================================================================

        private void PlaceHighlandNature()
        {
            PlaceSteppingStonesV2();
            if (!S.HasQuiet) return;
            var r = R("highland.nature");
            // すでに生えている木も木立に数える（森の木の配置と重ならないように）
            int trees = 0;
            foreach (var it in W.Nature)
            {
                if (it.Kind != NatureKind.TreeRound && it.Kind != NatureKind.TreeTall && it.Kind != NatureKind.TreeMushroom && it.Kind != NatureKind.TreeLollipop) continue;
                var q = GeoUtil.XZ(it.Position);
                if (Vector2.Distance(q, S.QuietCenter) > S.QuietRadius * 0.95f) continue;
                highlandTrees.Add(q);
                trees++;
            }
            float groveAng = r.Range(0f, Mathf.PI * 2f);
            for (int k = 0; k < 400 && trees < 8; k++)
            {
                float a = groveAng + r.Range(-1.1f, 1.1f) * (1f + k / 200f);
                var p = S.QuietCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * S.QuietRadius * r.Range(0.3f, 0.9f);
                if (F.WaterNear(p.x, p.y, 1.6f) || !SpacingOk(p, 2.8f)) continue;
                if (F.Slope(p.x, p.y) > 44f) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.TreeRound : NatureKind.TreeTall, p, r.Range(0f, 360f), r.Range(0.9f, 1.3f), r.Range(0, 5));
                MarkSpacing(p);
                highlandTrees.Add(p);
                trees++;
            }
            if (trees < 5) W.Notes.Add("静かな高台の木立が少ない: " + trees);
            for (int k = 0; k < 140; k++)
            {
                var off = r.InsideUnitCircle() * S.QuietRadius * 0.8f;
                var p = S.QuietCenter + off;
                if (F.WaterNear(p.x, p.y, 1.2f) || !SpacingOk(p, 1.3f)) continue;
                bool flower = r.Chance(0.35f);
                AddNature(flower ? NatureKind.Flower : NatureKind.Grass, p, r.Range(0f, 360f), r.Range(0.8f, 1.3f), flower ? r.Range(0, P.Flowers.Length) : r.Range(0, 3));
                MarkSpacing(p);
            }
        }

        // =================================================================
        // 釣り場のタグ（§2.3・§2.4・§2.9）
        // =================================================================

        /// <summary>川の釣り場1つに区間のタグを付ける（勾配・曲がり・滝からの距離）。</summary>
        private void TagRiverSpotV2(FishingSpot spot, RiverPath rv, int ri, int i, Vector2 standSide)
        {
            int n = rv.Points.Count;
            int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
            float len = 0f;
            for (int k = a + 1; k <= b; k++) len += (rv.Points[k] - rv.Points[k - 1]).magnitude;
            float grad = len > 0.1f ? (rv.Surface[a] - rv.Surface[b]) / len : 0f;
            var d1 = rv.Points[i] - rv.Points[a];
            var d2 = rv.Points[b] - rv.Points[i];
            float cross = d1.x * d2.y - d1.y * d2.x;
            var left = new Vector2(-d1.y, d1.x).normalized;
            float sideSign = Vector2.Dot(standSide, left);
            // 左に曲がる川では右岸が外側（淵）、右に曲がる川では左岸が外側
            bool bend = Mathf.Abs(cross) > 0.5f;
            bool outside = bend && ((cross > 0f && sideSign < 0f) || (cross < 0f && sideSign > 0f));
            bool belowFall = rv.WaterfallIndex > 0 && i >= rv.WaterfallIndex && AlongRiver(rv, rv.WaterfallIndex, i) <= 30f;
            bool atFall = rv.WaterfallIndex > 0 && Mathf.Abs(i - rv.WaterfallIndex) <= 1;
            bool ford = rv.Fords.Contains(i) || rv.Fords.Contains(i - 1);
            if (ford && !atFall)
            {
                // 浅瀬（瀬）：歩いて渡れる。
                spot.tags.Add("riffle");
                spot.tags.Remove("deep");
                if (!spot.tags.Contains("shallow")) spot.tags.Add("shallow");
            }
            else if (atFall || belowFall || grad >= 0.06f) spot.tags.Add("rapids");
            else if (grad >= 0.04f) spot.tags.Add("fast");
            else if (grad >= 0.02f) spot.tags.Add("riffle");
            else if (outside) spot.tags.Add("pool");
            else if (bend) spot.tags.Add("slack");
            else if (rv.Radius[i] < 3.8f && i % 2 == 0) spot.tags.Add("weed");
            else spot.tags.Add("slack");
            riverSpotsV2.Add(spot);
            spotGradient[spot] = grad;
            spotBend[spot] = outside ? Mathf.Abs(cross) : -Mathf.Abs(cross);
            spotRiver[spot] = ri;
        }

        private static float AlongRiver(RiverPath rv, int from, int to)
        {
            float d = 0f;
            for (int i = from + 1; i <= to && i < rv.Points.Count; i++) d += (rv.Points[i] - rv.Points[i - 1]).magnitude;
            return d;
        }

        /// <summary>川の区間がそれぞれ2か所以上あるようにする（足りなければ一番近い条件の釣り場に付け足す）。冷たい湧水の淵も決める。</summary>
        private void GuaranteeRiverSectionsV2()
        {
            Ensure("pool", s => spotGradient[s] < 0.03f ? spotBend[s] + 5f : -10f);
            Ensure("slack", s => spotGradient[s] < 0.03f ? -spotBend[s] + 5f : -10f);
            Ensure("riffle", s => -Mathf.Abs(spotGradient[s] - 0.03f) * 100f);
            Ensure("fast", s => s.tags.Contains("rapids") ? -5f : spotGradient[s] * 100f);
            Ensure("weed", s => spotGradient[s] < 0.025f ? 5f - spotGradient[s] * 100f : -10f);
            Ensure("rocky", s => F.Roughness(s.standPosition.x, s.standPosition.z, 3f), true);

            // 冷たい湧水の淵（シモマダラ）：上流側の淵を1か所。森の池の湧水口と合わせて2つの水域にする（§8.3）
            FishingSpot spring = null;
            float best = float.MinValue;
            foreach (var s in riverSpotsV2)
            {
                if (s.tags.Contains("rapids")) continue;
                float score = (s.tags.Contains("pool") ? 10f : 0f) + (s.tags.Contains("upstream") ? 4f : 0f) - spotGradient[s] * 50f + (spotRiver[s] == 0 ? 2f : 0f);
                if (score > best)
                {
                    best = score;
                    spring = s;
                }
            }
            if (spring != null)
            {
                spring.tags.Add("spring");
                spring.tags.Add("cold");
                if (!spring.tags.Contains("pool")) spring.tags.Add("pool");
            }
        }

        private void Ensure(string tag, System.Func<FishingSpot, float> score, bool addRocks = false)
        {
            int have = 0;
            foreach (var s in riverSpotsV2) if (s.tags.Contains(tag)) have++;
            if (have >= 2) return;
            var cands = new List<FishingSpot>();
            foreach (var s in riverSpotsV2) if (!s.tags.Contains(tag) && !s.tags.Contains("waterfall")) cands.Add(s);
            cands.Sort((x, y) => score(y).CompareTo(score(x)));
            for (int i = 0; i < cands.Count && have < 2; i++)
            {
                cands[i].tags.Add(tag);
                have++;
                if (addRocks)
                {
                    // 岩場：岸に岩を置いて見た目も合わせる（地形を直す操作 §9.7）
                    var sp = GeoUtil.XZ(cands[i].standPosition);
                    var wp = GeoUtil.XZ(cands[i].waterPosition);
                    var along = new Vector2(-(wp - sp).y, (wp - sp).x).normalized;
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var p = sp + along * (2.2f * k);
                        if (!F.IsWater(p.x, p.y)) AddNature(NatureKind.Rock, p, k * 47f, 1.1f, k + 1);
                    }
                }
            }
        }

        /// <summary>森の池のタグ（湧水口・泥底・濁り）と、静かな高台の池。</summary>
        private void TagPondSpotV2(FishingSpot spot, int pi, int k)
        {
            if (pi == W.HighlandPondIndex)
            {
                spot.tags.Add("highland");
                return;
            }
            int forest = pi > W.HighlandPondIndex && W.HighlandPondIndex >= 0 ? pi - 1 : pi;
            int count = W.Ponds.Count - (W.HighlandPondIndex >= 0 ? 1 : 0);
            if (forest == 0 && k == 0)
            {
                spot.tags.Add("spring");
                spot.tags.Add("cold");
            }
            if (forest == Mathf.Min(1, count - 1) && k == 1) spot.tags.Add("mud");
            if (forest == Mathf.Min(2, count - 1) && k == 2) spot.tags.Add("turbid");
        }

        private void BuildLakeSpotsV2()
        {
            for (int li = 0; li < W.Lakes.Count; li++)
            {
                var lk = W.Lakes[li];
                int n = 0;
                for (int k = 0; k < 24; k++)
                {
                    float a = k / 24f * Mathf.PI * 2f;
                    if (LakeInfo.AngleDiff(a, lk.SpitAngle) < 0.2f) continue;
                    var dir = lk.Dir(a);
                    float rr = lk.RadiusAt(a);
                    Vector2 stand = Vector2.zero;
                    bool ok = false;
                    for (float off = rr - 1f; off < rr + 6f; off += 0.6f)
                    {
                        var q = lk.Center + dir * off;
                        if (!GeoUtil.InsideWorld(q, 2f) || F.IsWater(q.x, q.y)) continue;
                        stand = q;
                        ok = true;
                        break;
                    }
                    if (!ok) continue;
                    bool shelf = LakeInfo.AngleDiff(a, lk.ShelfAngle) < 0.95f;
                    var water = stand - dir * (shelf ? 5f : 8.5f);
                    var spot = NewSpot("spot.lake" + li + "." + n++, lk.WaterIndex, WaterType.Lake, stand, water, lk.Surface);
                    AddLakeTags(spot, lk, water, a);
                }
                // 岬の先（最深部の近く：カガミヌシ・ウズシロ）と岬の脇
                var sd = lk.Dir(lk.SpitAngle);
                var tipStand = lk.SpitTip + sd * 1.0f;
                var tip = NewSpot("spot.lake" + li + ".tip", lk.WaterIndex, WaterType.Lake, tipStand, lk.SpitTip - sd * 6f, lk.Surface);
                AddLakeTags(tip, lk, lk.SpitTip - sd * 6f, lk.SpitAngle);
                if (!tip.tags.Contains("deep")) tip.tags.Add("deep");
                tip.tags.Add("deepest");
                var perp = new Vector2(-sd.y, sd.x);
                var midStand = Vector2.Lerp(lk.SpitTip, lk.SpitBase, 0.4f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var w = midStand + perp * (s * (LakeInfo.SpitHalfWidth + 5.5f));
                    var side = NewSpot("spot.lake" + li + ".spit" + (s < 0 ? "L" : "R"), lk.WaterIndex, WaterType.Lake, midStand + perp * (s * (LakeInfo.SpitHalfWidth - 0.8f)), w, lk.Surface);
                    AddLakeTags(side, lk, w, lk.SpitAngle);
                }
            }
        }

        private void AddLakeTags(FishingSpot spot, LakeInfo lk, Vector2 water, float angle)
        {
            spot.tags.Add("lake");
            spot.tags.Add("wide");
            spot.tags.Add("foggy");
            spot.tags.Add("still");
            float depth = lk.DepthAt(water);
            spot.tags.Add(depth <= 1.5f ? "shallow" : depth <= 5f ? "mid" : "deep");
            if (LakeInfo.AngleDiff(angle, lk.MudAngle) < 0.6f) spot.tags.Add("mud");
            if (LakeInfo.AngleDiff(angle, lk.SandMudAngle) < 0.5f) spot.tags.Add("sandmud");
            bool turbid = lk.HasInlet ? Vector2.Distance(GeoUtil.XZ(spot.standPosition), lk.Inlet) < 22f : LakeInfo.AngleDiff(angle, lk.MudAngle) < 0.4f;
            if (turbid) spot.tags.Add("turbid");
            if (S.IsForest(spot.standPosition.x, spot.standPosition.z)) spot.tags.Add("forest");
        }

        /// <summary>海の釣り場：砂底・海底近く・砂泥底・沖（桟橋の先と岩の突端）を必ずそろえる。</summary>
        private void TagSeaSpotsV2()
        {
            var sea = new List<FishingSpot>();
            foreach (var s in L.FishingSpots) if (s.waterType == WaterType.Sea && !s.tags.Contains("pier")) sea.Add(s);
            for (int i = 0; i < sea.Count; i++)
            {
                if (i % 2 == 0) sea[i].tags.Add("bottom");
                if (sea[i].tags.Contains("estuary")) sea[i].tags.Add("sandmud");
            }
            EnsureSea(sea, "beach", s => -S.Rocky01(s.standPosition.x, s.standPosition.z));
            EnsureSea(sea, "rocky", s => S.Rocky01(s.standPosition.x, s.standPosition.z));
            EnsureSea(sea, "sandmud", s =>
            {
                float best = 0f;
                foreach (var rv in W.Rivers)
                    if (rv.EndsInSea) best = -Vector2.Distance(rv.Points[rv.Points.Count - 1], GeoUtil.XZ(s.standPosition));
                return s.tags.Contains("rocky") ? best - 100f : best;
            }, 1);
            // 岩の突端から沖を狙える釣り場（桟橋の先とは別の、沖へのもう一つの立ち位置）
            FishingSpot point = null;
            float bestR = float.MinValue;
            foreach (var s in sea)
            {
                float v = S.Rocky01(s.standPosition.x, s.standPosition.z);
                if (v > bestR)
                {
                    bestR = v;
                    point = s;
                }
            }
            if (point != null)
            {
                var sp = point.standPosition;
                var dir = (point.waterPosition - sp);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f) point.waterPosition = new Vector3(sp.x, point.waterPosition.y, sp.z) + dir.normalized * 14f;
                point.tags.Add("offshore");
                if (!point.tags.Contains("rocky")) point.tags.Add("rocky");
            }
            foreach (var s in L.FishingSpots)
            {
                if (s.id != "spot.pier.0") continue;
                s.tags.Add("offshore");
                s.tags.Add("bottom");
            }
        }

        private void EnsureSea(List<FishingSpot> sea, string tag, System.Func<FishingSpot, float> score, int need = 2)
        {
            int have = 0;
            foreach (var s in sea) if (s.tags.Contains(tag)) have++;
            if (have >= need) return;
            var c = new List<FishingSpot>(sea);
            c.Sort((x, y) => score(y).CompareTo(score(x)));
            for (int i = 0; i < c.Count && have < need; i++)
            {
                if (c[i].tags.Contains(tag)) continue;
                c[i].tags.Add(tag);
                have++;
            }
        }

        /// <summary>人の来ない静かな岸（湖・池）に "quiet" を付ける。</summary>
        private void TagQuietV2()
        {
            foreach (var s in L.FishingSpots)
            {
                if (s.waterType != WaterType.Lake && s.waterType != WaterType.Pond) continue;
                if (s.tags.Contains("underground")) continue;
                if (IsQuietPoint(GeoUtil.XZ(s.standPosition), WorldTuning.QuietShoreClearance)) s.tags.Add("quiet");
            }
        }

        private bool IsQuietPoint(Vector2 p, float clearance)
        {
            foreach (var seg in W.RoadSegments)
            {
                if (seg.Trail) continue;
                if (GeoUtil.DistToSegment(p, seg.A, seg.B, out _) < clearance) return false;
            }
            foreach (var f in L.Facilities)
            {
                if (f.type != FacilityType.ResidentHouse && f.type != FacilityType.PlayerHouse && f.type != FacilityType.Lookout) continue;
                if (Vector2.Distance(GeoUtil.XZ(f.position), p) < clearance) return false;
            }
            return true;
        }

        /// <summary>滝の裏の洞の釣り場（別エリアの暗い淵。シルベフデ）。</summary>
        private void AddCaveSpotV2()
        {
            if (W.Cave == null) return;
            var o = WorldTuning.WaterfallCaveOrigin;
            var spot = new FishingSpot
            {
                id = "spot.waterfall_cave",
                waterId = null,
                waterType = WaterType.River,
                standPosition = o + new Vector3(0f, 0f, -2.2f),
                waterPosition = o + new Vector3(0f, -0.15f, 2.4f)
            };
            spot.tags.Add("river");
            spot.tags.Add("cave");
            spot.tags.Add("behind_waterfall");
            spot.tags.Add("dark");
            spot.tags.Add("deep");
            spot.tags.Add("pool");
            spot.tags.Add("radius_only");
            spot.tags.Add("radius:3.2");
            L.FishingSpots.Add(spot);
        }

        // =================================================================
        // エリアと生息地点
        // =================================================================

        private void BuildAreasV2()
        {
            foreach (var lk in W.Lakes)
            {
                Dir(lk.Center, out var ja, out var en);
                AddArea("area.lake." + W.Lakes.IndexOf(lk), AreaType.Lake, lk.Center, lk.BaseRadius * 1.1f + 10f, ja + "の湖", en + " lake");
            }
            if (S.HasQuiet)
            {
                AddArea("area.highland.quiet", AreaType.Hill, S.QuietCenter, S.QuietRadius + 6f, "静かな高台", "Quiet highland");
                L.Areas[L.Areas.Count - 1].quiet = true;
            }
            if (W.Rivers.Count > 0 && W.Rivers[0].WaterfallIndex > 0)
            {
                var rv = W.Rivers[0];
                AddArea("area.waterfall", AreaType.Riverside, rv.Points[rv.WaterfallIndex], 20f, "滝", "Waterfall");
            }
        }

        private void BuildHabitatsV2()
        {
            // 静かな高台：木立（ニジマユ）・草地（ユメボシ・ホシノコ）・池（ムレピカ）
            if (S.HasQuiet)
            {
                foreach (var t in highlandTrees) AddHabitat(new Vector3(t.x, F.SampleGround(t.x, t.y), t.y), "tree");
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f + 0.4f;
                    var p = S.QuietCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * S.QuietRadius * (k % 2 == 0 ? 0.45f : 0.7f);
                    if (F.IsWater(p.x, p.y)) continue;
                    AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "grass");
                }
                AddHabitat(new Vector3(S.QuietCenter.x, F.SampleGround(S.QuietCenter.x, S.QuietCenter.y), S.QuietCenter.y), "lookout");
            }
            // 湖：水面（浮かぶ生き物）・泥の岸・急深の岸の岩
            foreach (var lk in W.Lakes)
            {
                for (int k = 0; k < 8; k++)
                {
                    float a = k / 8f * Mathf.PI * 2f + 0.2f;
                    if (LakeInfo.AngleDiff(a, lk.SpitAngle) < 0.3f) continue;
                    var w = lk.Center + lk.Dir(a) * lk.RadiusAt(a) * 0.82f;
                    AddHabitat(new Vector3(w.x, lk.Surface, w.y), "water");
                }
                for (int k = -1; k <= 1; k++)
                {
                    float a = lk.MudAngle + k * 0.25f;
                    var p = lk.Center + lk.Dir(a) * (lk.RadiusAt(a) + 1.6f);
                    if (!F.IsWater(p.x, p.y)) AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "mud");
                    a = lk.SteepAngle + k * 0.25f;
                    p = lk.Center + lk.Dir(a) * (lk.RadiusAt(a) + 2.2f);
                    if (!F.IsWater(p.x, p.y)) AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "rock");
                }
            }
            // 川の砂州（よどみの岸）：スナコロ（砂に残った跡をたどる）
            int sand = 0;
            foreach (var s in riverSpotsV2)
            {
                if (sand >= 6) break;
                if (!s.tags.Contains("slack")) continue;
                AddHabitat(s.standPosition, "sand");
                sand++;
            }
            // 滝：岩と水しぶき
            if (W.Rivers.Count > 0 && W.Rivers[0].WaterfallIndex > 0)
            {
                var rv = W.Rivers[0];
                int i = rv.WaterfallIndex;
                var side = new Vector2(-(rv.Points[i] - rv.Points[i - 1]).y, (rv.Points[i] - rv.Points[i - 1]).x).normalized;
                for (int k = -1; k <= 1; k += 2)
                {
                    var p = rv.Points[i] + side * k * (rv.Radius[i] + 2.5f);
                    if (!F.IsWater(p.x, p.y)) AddHabitat(new Vector3(p.x, F.SampleGround(p.x, p.y), p.y), "rock");
                }
                AddHabitat(new Vector3(rv.Points[i].x, rv.Surface[i], rv.Points[i].y), "water");
            }
        }

        // =================================================================
        // 検証（§12.2：v0.2 で決めた地形の条件）
        // =================================================================

        private void ValidateV2()
        {
            // 湖
            if (W.Lakes.Count == 0) W.Notes.Add("湖なし");
            else
            {
                var lk = W.Lakes[0];
                float minR = float.MaxValue;
                for (int i = 0; i < 36; i++) minR = Mathf.Min(minR, lk.RadiusAt(i / 36f * Mathf.PI * 2f));
                if (minR * 2f < WorldTuning.LakeMinDiameter - 0.5f) W.Notes.Add("湖が小さい");
                if (lk.MaxDepth < WorldTuning.LakeMinMaxDepth) W.Notes.Add("湖が浅い");
                foreach (var tag in new[] { "shallow", "mid", "deepest", "mud", "sandmud" })
                    if (CountSpots(WaterType.Lake, tag) < 1) W.Notes.Add("湖の釣り場に " + tag + " がない");
                if (CountSpots(WaterType.Lake, "shallow") < 2 || CountSpots(WaterType.Lake, "mid") < 2) W.Notes.Add("湖の浅場・中層が少ない");
            }
            if (CountSpots(WaterType.Lake, "turbid") + CountSpots(WaterType.Pond, "turbid") < 1) W.Notes.Add("濁った釣り場なし");
            if (CountSpots(WaterType.Lake, "quiet") + CountSpots(WaterType.Pond, "quiet") < 2) W.Notes.Add("静かな岸が少ない");
            if (CountSpots(WaterType.Pond, "mud") + CountSpots(WaterType.Lake, "mud") < 2) W.Notes.Add("泥底の釣り場が少ない");
            // 川
            foreach (var tag in new[] { "pool", "slack", "riffle", "fast", "weed", "rocky", "rapids" })
                if (CountSpots(WaterType.River, tag) < (tag == "rapids" ? 1 : 2)) W.Notes.Add("川の区間が足りない: " + tag);
            int springWaters = 0;
            if (CountSpots(WaterType.River, "spring") > 0) springWaters++;
            if (CountSpots(WaterType.Pond, "spring") > 0) springWaters++;
            if (springWaters < 2) W.Notes.Add("冷たい湧水の水域が少ない");
            // 滝
            if (W.Rivers.Count == 0 || W.Rivers[0].WaterfallIndex < 1 || W.Rivers[0].WaterfallDrop < WorldTuning.WaterfallMinDrop - 0.01f)
                W.Notes.Add("8m以上の滝なし");
            if (W.Cave == null) W.Notes.Add("滝の裏の洞の入口なし");
            if (CountSpots(WaterType.River, "waterfall") < 1) W.Notes.Add("滝の釣り場なし");
            // 海・桟橋
            if (!W.HasPier) W.Notes.Add("桟橋なし");
            if (CountSpots(WaterType.Sea, "offshore") < 2) W.Notes.Add("沖へ届く釣り場が少ない");
            foreach (var tag in new[] { "beach", "rocky", "bottom", "pier" })
                if (CountSpots(WaterType.Sea, tag) < 1) W.Notes.Add("海の釣り場に " + tag + " がない");
            if (CountSpots(WaterType.Sea, "sandmud") + CountSpots(WaterType.Lake, "sandmud") < 2) W.Notes.Add("砂泥底の釣り場が少ない");
            // 静かな高台
            if (!S.HasQuiet) W.Notes.Add("静かな高台なし");
            else
            {
                foreach (var seg in W.RoadSegments)
                {
                    if (seg.Trail) continue;
                    if (GeoUtil.DistToSegment(S.QuietCenter, seg.A, seg.B, out _) < WorldTuning.QuietRoadClearance)
                    {
                        W.Notes.Add("静かな高台の近くに本道");
                        break;
                    }
                }
                foreach (var f in L.Facilities)
                {
                    if (f.type != FacilityType.ResidentHouse && f.type != FacilityType.PlayerHouse && f.type != FacilityType.Lookout) continue;
                    if (Vector2.Distance(GeoUtil.XZ(f.position), S.QuietCenter) < WorldTuning.QuietHouseClearance)
                    {
                        W.Notes.Add("静かな高台の近くに家・見晴らし台: " + f.id);
                        break;
                    }
                }
                if (W.HighlandPondIndex < 0) W.Notes.Add("静かな高台の池なし");
            }
            // 鍛冶屋・修理屋（暮らしの谷の中）
            CheckPath(FacilityType.Blacksmith, WorldTuning.SatelliteMaxPath);
            CheckPath(FacilityType.RepairWorkshop, WorldTuning.SatelliteMaxPath);
            // 湖・海（桟橋）の入口まで、広場から本道で走って45秒以内（§5.5）
            if (W.Lakes.Count > 0) CheckPathTo(W.Lakes[0].SpitBase, "湖", WorldTuning.MaxPathRegionV2);
            if (W.HasPier) CheckPathTo(GeoUtil.XZ(W.PierStart), "桟橋", WorldTuning.MaxPathRegionV2);
        }

        private void CheckPathTo(Vector2 target, string label, float maxLen)
        {
            var to = new Vector3(target.x, F.SampleGround(target.x, target.y), target.y);
            var path = L.Roads.FindPath(W.PlazaCenter, to);
            float len = 0f;
            var prev = W.PlazaCenter;
            foreach (var p in path)
            {
                len += Vector3.Distance(prev, p);
                prev = p;
            }
            if (path.Count == 0 || len > maxLen) W.Notes.Add($"{label} まで遠すぎる ({len:0}m)");
        }

        private int CountSpots(WaterType type, string tag)
        {
            int n = 0;
            foreach (var s in L.FishingSpots) if (s.waterType == type && s.tags.Contains(tag)) n++;
            return n;
        }
    }
}
