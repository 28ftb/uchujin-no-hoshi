using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 生成バージョン3の試作（マップ設計 v0.3 §8.1・§13.1 の1〜2）。
    ///  1. 星の骨格（地域・道中・ゲート・高さ）を先に決める（StarSkeleton。シードの "world.gen3.skeleton"）。
    ///  2. 村（R1）は版2の作り方のまま作り、海の方角だけ骨格に合わせる。村の端に、骨格の方角へ向くゲート（T1 花の野道）を置き、広場から道でつなぐ。
    ///  3. 村ができたら、ゲートの地面の高さを骨格に入れ、T1 花の野道・R2 湖の森を別のマップとして作る（マップごとに別の乱数キー）。
    /// 試作の割り切り：村には版2の湖・滝・静かな高台が残る（100種の住める場所を減らさないため）。T2〜T6・R3〜R5 は骨格だけ。
    /// </summary>
    public sealed partial class WorldGenerator
    {
        private StarSkeleton skel;
        private bool V3 => ver >= 3;

        /// <summary>骨格の方角へ向く、村の端のゲートの場所を選ぶ（海・水・急な斜面・山・施設を避ける）。</summary>
        private void PlaceVillageGatesV3()
        {
            foreach (var sg in skel.Get(StarSkeleton.Village).Gates)
            {
                var other = skel.Get(sg.OtherMapId);
                if (other == null || !other.Built) continue;
                // 骨格の方角から60°以内 → 100°以内 → どこでも（試作の村には版2の湖・山・静かな高台があり、その方角の端がふさがることがある）
                if (!TryGateSite(sg.Bearing, 1.05f, out var site, out var inward) && !TryGateSite(sg.Bearing, 1.75f, out site, out inward)
                    && !TryGateSite(sg.Bearing, Mathf.PI, out site, out inward))
                {
                    W.Notes.Add($"村のゲート（{other.NameJa}）を置く場所がない");
                    continue;
                }
                float y = AverageHeight(site, 4f);
                AddPad(PadKind.Generic, site, new Vector2(6.5f, 0f), 0f, true, 4.5f, null, Mathf.Max(y, WorldTuning.SeaLevel + 1.2f));
                reserved.Add((site, 12f));
                float yawOut = YawOf(-inward);
                var arrive = site + inward * WorldTuning.FieldGateArrival;
                W.VillageGates.Add(new FieldGateInfo
                {
                    Id = sg.Id,
                    MapId = StarSkeleton.Village,
                    OtherMapId = sg.OtherMapId,
                    Yaw = yawOut,
                    Position = new Vector3(site.x, 0f, site.y),
                    Arrival = new Vector3(arrive.x, 0f, arrive.y),
                    ArrivalYaw = yawOut + 180f
                });
                W.VillageGateAngleError = Mathf.Max(W.VillageGateAngleError, LakeInfo.AngleDiff(Mathf.Atan2(site.y, site.x), sg.Bearing) * Mathf.Rad2Deg);
            }
        }

        /// <summary>
        /// 骨格の方角に近い端の候補を集め、広場からの道のり（道を作る時と同じ A* で見積もる）が短い所を選ぶ。
        /// 湖や山の向こうの端を選んで、道が大回りにならないように。
        /// </summary>
        private bool TryGateSite(float bearing, float maxAngle, out Vector2 best, out Vector2 inward)
        {
            best = Vector2.zero;
            inward = Vector2.zero;
            float h = WorldTuning.Half - WorldTuning.VillageGateInset;
            var cands = new List<(Vector2 p, Vector2 n, float score)>();
            for (int side = 0; side < 4; side++)
            {
                for (float t = -h + 24f; t <= h - 24f; t += 4f)
                {
                    Vector2 p, n;
                    switch (side)
                    {
                        case 0: p = new Vector2(h, t); n = Vector2.left; break;
                        case 1: p = new Vector2(-h, t); n = Vector2.right; break;
                        case 2: p = new Vector2(t, h); n = Vector2.down; break;
                        default: p = new Vector2(t, -h); n = Vector2.up; break;
                    }
                    float ang = LakeInfo.AngleDiff(Mathf.Atan2(p.y, p.x), bearing);
                    if (ang > maxAngle) continue;
                    if (S.CoastD(p.x, p.y) < 34f) continue;
                    if (S.MountainFactor(p.x, p.y) < 1.0f) continue;
                    if (F.WaterNear(p.x, p.y, 10f)) continue;
                    float rough = F.Roughness(p.x, p.y, 7f);
                    if (rough > 4f) continue;
                    if (V2 && !QuietClearOk(p)) continue;
                    if (V2 && W.Lakes.Count > 0 && W.Lakes[0].K(p) < 1.3f) continue;
                    bool blocked = false;
                    foreach (var pad in W.Pads) if (pad.SignedDistance(p.x, p.y) < 12f) blocked = true;
                    foreach (var rs in reserved) if (Vector2.Distance(rs.c, p) < rs.r + 10f) blocked = true;
                    if (W.FarmReserve.width > 0f && W.FarmReserve.Contains(p)) blocked = true;
                    if (blocked) continue;
                    cands.Add((p, n, ang * 40f + rough * 3f));
                }
            }
            if (cands.Count == 0) return false;
            cands.Sort((a, b) => a.score.CompareTo(b.score));
            var tp = new RoadPlanner(F, WorldTuning.PathCell);
            tp.Refresh(footprints, ExtraCost);
            float bestScore = float.MaxValue, shortest = float.MaxValue;
            Vector2 shortP = Vector2.zero, shortN = Vector2.zero;
            var tried = new List<Vector2>();
            foreach (var c in cands)
            {
                // 近い候補どうしは1つだけ調べる（4m おきの候補が並ぶため）
                bool near = false;
                foreach (var t in tried) if (Vector2.Distance(t, c.p) < 14f) near = true;
                if (near) continue;
                if (tried.Count >= 14) break;
                tried.Add(c.p);
                var cells = tp.FindPath(plaza, c.p);
                if (cells == null || cells.Count == 0) continue;
                float len = 0f;
                var prev = plaza;
                foreach (var ci in cells)
                {
                    var q = tp.CenterOf(ci);
                    len += Vector2.Distance(prev, q);
                    prev = q;
                }
                if (len < shortest)
                {
                    shortest = len;
                    shortP = c.p;
                    shortN = c.n;
                }
                if (len > WorldTuning.MaxPathPlazaToGate * 0.92f) continue;
                float score = c.score + len * 0.12f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c.p;
                    inward = c.n;
                }
            }
            if (bestScore < float.MaxValue) return true;
            if (shortest < float.MaxValue && maxAngle >= Mathf.PI)
            {
                best = shortP;
                inward = shortN;
                return true;
            }
            return false;
        }

        /// <summary>地形を仕上げた後、村のゲートの高さを地面に合わせる。</summary>
        private void SettleVillageGatesV3()
        {
            foreach (var g in W.VillageGates)
            {
                g.Position.y = F.SampleGround(g.Position.x, g.Position.z);
                g.Arrival.y = F.SampleGround(g.Arrival.x, g.Arrival.z) + 0.2f;
            }
        }

        private void ValidateV3()
        {
            if (W.VillageGates.Count == 0) W.Notes.Add("村のゲートなし");
            foreach (var g in W.VillageGates) CheckPathTo(new Vector2(g.Position.x, g.Position.z), "村のゲート " + g.OtherMapId, WorldTuning.MaxPathPlazaToGate);
        }

        /// <summary>
        /// 村ができた後に、村のゲートの高さを骨格に入れ、村の外のマップを作って g に付ける。
        /// 水深の判定（深さ0.6mまでしか入れない）は村の外のマップにも広げる。
        /// </summary>
        private static void AttachFieldMaps(GeneratedWorld g, ulong seed)
        {
            if (g == null || g.Skeleton == null) return;
            foreach (var vg in g.VillageGates)
            {
                g.Skeleton.SetGateHeight(vg.OtherMapId, StarSkeleton.Village, vg.Position.y);
                // 渓流の坂道：滝の山の入口は村のゲートより SlopeRise だけ高く（坂 11% で45秒以内に登れる高さ）
                if (vg.OtherMapId == StarSkeleton.StreamSlope)
                    g.Skeleton.SetGateHeight(StarSkeleton.StreamSlope, StarSkeleton.WaterfallMountain, vg.Position.y + g.Skeleton.SlopeRise);
            }
            foreach (var sm in g.Skeleton.Maps)
            {
                if (!sm.Built || sm.Id == StarSkeleton.Village) continue;
                var m = FieldMapGenerator.Generate(seed, g.Skeleton, sm.Id, g.Palette);
                if (m != null) g.FieldMaps.Add(m);
            }
            // マップごとの配置データ（エリア・水域・釣り場・生息地点・高さと水の判定）を、星の配置データの下に付ける
            var L = g.Layout;
            foreach (var m in g.FieldMaps) L.Maps.Add(FieldMapLayout.Build(m));
            // 村の配置データの高さ・水・水深の問い合わせは、位置が村の外のマップならそのマップに回す
            // （生き物の足元・連れて歩く子・水深の制限などが、呼び出し側を変えずに村の外のマップでも動く）
            var vh = L.HeightSampler;
            var vw = L.WaterTester;
            var vd = L.WaterDepthSampler;
            var subs = L.Maps;
            L.HeightSampler = (x, z) =>
            {
                for (int i = 0; i < subs.Count; i++) if (subs[i].ContainsXZ(x, z)) return subs[i].HeightSampler(x, z);
                return vh(x, z);
            };
            L.WaterTester = (x, z) =>
            {
                for (int i = 0; i < subs.Count; i++) if (subs[i].ContainsXZ(x, z)) return subs[i].WaterTester(x, z);
                return vw(x, z);
            };
            L.WaterDepthSampler = (x, z) =>
            {
                for (int i = 0; i < subs.Count; i++) if (subs[i].ContainsXZ(x, z)) return subs[i].WaterDepthSampler(x, z);
                return vd(x, z);
            };
        }

        /// <summary>ワールド座標のある地域・道中マップ（無ければ null）。</summary>
        public static FieldMap FieldMapAt(GeneratedWorld g, Vector3 p)
        {
            if (g == null) return null;
            foreach (var m in g.FieldMaps) if (m.ContainsWorld(p.x, p.z)) return m;
            return null;
        }
    }
}
