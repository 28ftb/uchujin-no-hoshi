using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    // =====================================================================
    // T6a 海風の林（浜と岬 ⇄ 湖の森。§3.1・§5・§8.3。シードで T6 がこちらの星だけ）
    // 浜の側は砂丘（砂の段）、上るほど風で傾いた林。浜の池、砂丘の上（隠れた場所）。
    // =====================================================================

    internal sealed class SeaWindWoodGen : RouteGenBase
    {
        protected override string EndA => StarSkeleton.BeachCape;
        protected override string EndB => StarSkeleton.LakeForest;
        protected override float RoadComfort => 0.12f;
        protected override float MinRelief => 26f;
        protected override float MinCliff => 1.5f;
        private WorldNoise duneN, woodN;
        private readonly List<(float x, float h, float wob, float ph)> steps = new List<(float, float, float, float)>();
        private Vector2 pondC, duneTop, duneFront, windTree;
        private float duneH;

        protected override void Shape(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            duneN = Noise("dune");
            woodN = Noise("wood");
            int n = r.Range(2, 4);
            for (int k = 0; k < n; k++) steps.Add((W * Mathf.Lerp(0.3f, 0.8f, n == 1 ? 0.5f : k / (float)(n - 1)) + r.Range(-10f, 10f), r.Range(3f, 5f), r.Range(4f, 9f), r.Range(0f, 6.28f)));
            pondC = new Vector2(W * r.Range(0.12f, 0.25f), D * 0.5f + (r.Chance(0.5f) ? 1f : -1f) * r.Range(22f, 32f));
            duneTop = new Vector2(W * r.Range(0.25f, 0.45f), D * 0.5f - (pondC.y - D * 0.5f) * 0.8f);
            duneH = r.Range(6f, 9f);
        }

        private float StepsAt(Vector2 l)
        {
            float h = 0f;
            foreach (var s in steps)
            {
                float xs = s.x + Mathf.Sin(l.y * 0.05f + s.ph) * s.wob;
                // 段の切れ目（砂の斜面）を真ん中あたりに
                float pass = Mathf.Exp(-Mathf.Pow((l.y - M.Depth * 0.5f) / 14f, 2f));
                float w = Mathf.Lerp(3f, s.h / 0.12f, pass);
                h += s.h * GeoUtil.SmoothStep(xs - w * 0.5f, xs + w * 0.5f, l.x);
            }
            return h;
        }

        protected override float HeightAt(Vector2 l, float baseH, float t)
        {
            float total = 0f;
            foreach (var s in steps) total += s.h;
            float h = Mathf.Lerp(hA, hB - total, GeoUtil.SmoothStep(0f, 1f, t)) + StepsAt(l);
            // 浜の側の砂丘（うねりが大きい）→ 林の側はなだらか
            float duneZone = 1f - GeoUtil.SmoothStep(0.25f, 0.55f, t);
            h += (duneN.Ridged(l.x * 0.035f, l.y * 0.035f, 3) - 0.4f) * 5f * duneZone * Mathf.Sin(t * Mathf.PI * 0.5f + 0.4f);
            h += (woodN.Fbm(l.x * 0.015f, l.y * 0.015f, 3) - 0.5f) * 6f * (1f - duneZone);
            // 砂丘の上（隠れた場所）：本道側は砂の崖
            float kd = Vector2.Distance(l, duneTop) / 16f;
            if (kd < 1f) h += duneH * (1f - GeoUtil.SmoothStep(0.3f, 1f, kd));
            return h;
        }

        protected override void BeforeRoadHeights(SeededRandom r)
        {
            if (M.DistToPaths(pondC) < 12f) pondC.y += Mathf.Sign(pondC.y - M.Depth * 0.5f) * 10f;
            pendingPonds.Add((pondC, r.Range(6.5f, 9f), r.Range(1.2f, 1.6f)));
            windTree = new Vector2(M.Width * r.Range(0.6f, 0.85f), M.Depth * 0.5f + (r.Chance(0.5f) ? 1f : -1f) * r.Range(24f, 34f));
        }

        protected override void AfterFlatten(SeededRandom r)
        {
            Trail(pondC + (M.Road[M.Road.Count / 4] - pondC).normalized * 10f);
            Trail(duneTop);
            Trail(windTree);
        }

        protected override void Places(SeededRandom r)
        {
            AddPlace("t6a.pond", "浜の池のほとり", NearestDry(pondC + (M.Road[M.Road.Count / 4] - pondC).normalized * 10f), pocket: true);
            AddPlace("t6a.dune", "砂丘の上（隠れた場所）", NearestDry(duneTop, 6f), hidden: true);
            AddPlace("t6a.tree", "風の大木", NearestDry(windTree, 8f), pocket: true);
            AddPlace("t6a.view", "海の見える段", NearestDry(new Vector2(steps[0].x + 8f, M.Depth * 0.5f)), pocket: true);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float t = lx / M.Width;
            if (t < 0.35f) return noise > 0.62f ? FieldPaint.Grass : FieldPaint.Sand;
            if (t < 0.55f) return noise > 0.5f ? FieldPaint.Meadow : FieldPaint.Sand;
            return noise > 0.45f ? FieldPaint.Forest : FieldPaint.Grass;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            // 風で傾いた林（同じ向きにそろえる）
            float lean = r.Range(0f, 360f);
            for (int k = 0; k < 1500; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(-FieldMap.Margin + 4f, D + FieldMap.Margin - 4f));
                float t = Mathf.Clamp01(l.x / W);
                bool outside = !M.InsidePlayable(l, 0f);
                float density = outside ? 0.75f : GeoUtil.SmoothStep(0.3f, 0.75f, t) * 0.5f;
                if (!r.Chance(density)) continue;
                if (!NatureOk(l, 4.5f, 2.5f, 38f, true)) continue;
                if (outside && (l.x < 0f && Mathf.Abs(l.y - gateA.y) < 7f || l.x > W && Mathf.Abs(l.y - gateB.y) < 7f)) continue;
                M.Nature.Add(new NatureItem { Kind = r.Chance(0.5f) ? NatureKind.TreeLollipop : NatureKind.TreeRound, Position = M.OnGroundWorld(l), Yaw = lean + r.Range(-12f, 12f), Scale = r.Range(0.8f, 1.2f), Variant = r.Range(0, 16), Area = AreaType.Coast });
            }
            M.Nature.Add(new NatureItem { Kind = NatureKind.TreeRound, Position = M.OnGroundWorld(windTree), Yaw = lean, Scale = 2.1f, Variant = 3, Area = AreaType.Coast });
            for (int k = 0; k < 1200; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.6f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.4f), r, AreaType.Coast);
            }
            for (int k = 0; k < 160; k++)
            {
                var l = new Vector2(r.Range(0f, W * 0.4f), r.Range(0f, D));
                if (!NatureOk(l, 2f, 1f, 35f)) continue;
                AddNature(NatureKind.Shell, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
            for (int k = 0; k < 200; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.Bush : NatureKind.Flower, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
            for (int k = 0; k < 60; k++)
            {
                float a = r.Range(0f, Mathf.PI * 2f);
                var l = pondC + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r.Range(7f, 11f);
                if (M.WaterAt(l.x, l.y) != FieldWater.None || M.DistToPaths(l) < 2f) continue;
                AddNature(NatureKind.Reed, l, r.Range(0.8f, 1.2f), r, AreaType.Pond);
            }
        }

        protected override void ValidateExtra()
        {
            if (M.Ponds.Count < 1) M.Notes.Add("浜の池がない");
        }
    }

    // =====================================================================
    // T6b 崖の小道（浜と岬 ⇄ 滝の山。§3.1・§5・§8.3。シードで T6 がこちらの星だけ）
    // 海側は崖で、下に入り江（海）。崖の上の道を登っていく。入り江へ下りる道、入り江の洞穴（隠れた場所）、崖の見晴らし。
    // =====================================================================

    internal sealed class CliffPathGen : RouteGenBase
    {
        protected override string EndA => StarSkeleton.BeachCape;
        protected override string EndB => StarSkeleton.WaterfallMountain;
        protected override float RoadComfort => 0.14f;
        protected override float GateSpread => 0.06f;
        protected override float MinRelief => 35f;
        protected override float MinCliff => 4f;
        private float cliffZ, coveX, coveHalf, coveDepth;
        private float rampSide, rampX0, rampLen, rampTop; // 入り江へ下りる岩棚の坂（崖の面に沿う）
        private WorldNoise hillN;
        private Vector2 coveBeach, view, caveSpot, ledgeMid;

        protected override void Shape(SeededRandom r)
        {
            float W = M.Width, D = M.Depth;
            hillN = Noise("hill");
            cliffZ = D * r.Range(0.3f, 0.38f);
            coveX = W * r.Range(0.3f, 0.5f);
            coveHalf = r.Range(26f, 38f);
            coveDepth = r.Range(14f, 22f);
            // 入り江へ下りる岩棚の坂：入り江の浜の端から崖の面に沿って崖の上まで登る。坂がゆるくなる側に作る
            float bestGrade = float.MaxValue;
            for (int side = -1; side <= 1; side += 2)
            {
                float x0 = coveX + side * coveHalf * 0.6f;
                float room = side > 0 ? W - 12f - x0 : x0 - 12f;
                if (room < 30f) continue;
                float len = room;
                for (int k = 0; k < 3; k++)
                {
                    float top = LandAt(new Vector2(x0 + side * len, CliffEdge(x0 + side * len) + 3f));
                    len = Mathf.Clamp((top - 1.4f) / 0.22f, 30f, room);
                }
                float topH = LandAt(new Vector2(x0 + side * len, CliffEdge(x0 + side * len) + 3f));
                float grade = (topH - 1.4f) / len;
                if (grade < bestGrade) { bestGrade = grade; rampSide = side; rampX0 = x0; rampLen = len; rampTop = topH; }
            }
            // ゲートは崖の上の道（海から離れた側）
            gateA.y = Mathf.Max(gateA.y, cliffZ + 24f);
            gateB.y = Mathf.Max(gateB.y, cliffZ + 24f);
        }

        private float CliffEdge(float x)
        {
            float u = (x - coveX) / coveHalf;
            return cliffZ + coveDepth * Mathf.Exp(-u * u); // 入り江は崖が陸へ食い込む
        }

        private float LandAt(Vector2 l)
        {
            float baseH = Mathf.Lerp(hA, hB, GeoUtil.SmoothStep(0f, 1f, Mathf.Clamp01(l.x / M.Width)));
            float s = l.y - CliffEdge(l.x);
            return baseH + (hillN.Fbm(l.x * 0.02f, l.y * 0.02f, 3) - 0.5f) * 6f + GeoUtil.SmoothStep(0f, 40f, s) * 6f;
        }

        /// <summary>岩棚の坂の上の重み（0〜1）と高さ。</summary>
        private float Ledge(Vector2 l, out float h)
        {
            h = 0f;
            if (rampLen <= 0f) return 0f;
            float u = (l.x - rampX0) * rampSide; // 入り江の側 0 → 崖の上 rampLen
            if (u < -4f || u > rampLen + 6f) return 0f;
            float s = l.y - CliffEdge(l.x);
            float band = 1f - GeoUtil.SmoothStep(3f, 4.5f, Mathf.Abs(s - 1f)); // 崖の面に幅 約6m の棚
            float along = GeoUtil.SmoothStep(-4f, 0f, u) * (1f - GeoUtil.SmoothStep(rampLen, rampLen + 6f, u));
            h = Mathf.Lerp(1.4f, rampTop, Mathf.Clamp01(u / rampLen));
            return band * along;
        }

        protected override float HeightAt(Vector2 l, float baseH, float t)
        {
            float edge = CliffEdge(l.x);
            float s = l.y - edge;
            float land = LandAt(l);
            float h;
            if (s > 3f) h = land;
            else
            {
                // 崖の下：入り江は海（水面 0m）と小さな浜、ほかは崖の根もとの岩場
                float below = Mathf.Abs(l.x - coveX) < coveHalf * 0.7f ? Mathf.Lerp(-3f, 1.2f, GeoUtil.SmoothStep(-14f, -2f, s)) : Mathf.Lerp(-4f, 2f, GeoUtil.SmoothStep(-10f, 0f, s));
                h = Mathf.Lerp(below, land, GeoUtil.SmoothStep(-2f, 3f, s));
            }
            float w = Ledge(l, out float lh);
            return Mathf.Lerp(h, lh, w);
        }

        protected override float PathExtra(int j)
        {
            var l = LocalOf(j);
            float s = l.y - CliffEdge(l.x);
            return s < 8f ? 12f : 0f; // 崖の縁から少し離れる
        }

        protected override void AfterFlatten(SeededRandom r)
        {
            // 海（水面 0m）
            for (int i = 0; i < N; i++)
            {
                if (M.H[i] >= -0.03f) continue;
                var l = LocalOf(i);
                if (l.y > CliffEdge(l.x) + 2f) continue;
                M.WaterY[i] = 0f;
                M.Water[i] = FieldWater.Sea;
            }
            coveBeach = new Vector2(coveX, CliffEdge(coveX) - 6f);
            for (float z = CliffEdge(coveX); z > 2f; z -= 0.5f)
                if (M.SampleTerrain(coveX, z) < 0.6f) { coveBeach = new Vector2(coveX, z + 2.5f); break; }
            caveSpot = new Vector2(coveX - rampSide * coveHalf * 0.5f, 0f); // 岩棚の坂と反対の端
            caveSpot.y = CliffEdge(caveSpot.x) - 3f;
            if (M.SampleTerrain(caveSpot.x, caveSpot.y) < 0.3f) caveSpot.y += 2f;
            float vx = coveX + (r.Chance(0.5f) ? 1f : -1f) * (coveHalf + r.Range(20f, 40f));
            view = new Vector2(Mathf.Clamp(vx, 20f, M.Width - 20f), 0f);
            view.y = CliffEdge(view.x) + 5f;
            ledgeMid = new Vector2(rampX0 + rampSide * rampLen * 0.55f, 0f);
            ledgeMid.y = CliffEdge(ledgeMid.x) + 1f;
            Trail(coveBeach);
            Trail(view);
            Trail(caveSpot);
            AddStructure(FieldStructureKind.CaveMouth, caveSpot + Vector2.down * 1.5f, 180f, 1f);
        }

        protected override void Places(SeededRandom r)
        {
            AddPlace("t6b.cove", "入り江の浜", NearestDry(coveBeach, 8f), pocket: true);
            AddPlace("t6b.view", "崖の見晴らし", NearestDry(view, 6f), pocket: true);
            AddPlace("t6b.cave", "入り江の洞穴（隠れた場所）", NearestDry(caveSpot, 6f), hidden: true);
            AddPlace("t6b.ledge", "崖の岩棚", NearestDry(ledgeMid, 4f), pocket: true);
            AddPlace("t6b.rocks", "崖の根もとの岩場", NearestDry(new Vector2(coveX - coveHalf, CliffEdge(coveX - coveHalf) - 2f), 10f), pocket: true);
        }

        protected override FieldPaint BasePaint(float lx, float lz, float noise)
        {
            float s = lz - CliffEdge(lx);
            if (s < 3f) return Mathf.Abs(lx - coveX) < coveHalf * 0.7f && s < -2f ? FieldPaint.Sand : FieldPaint.Rock;
            if (s < 12f) return noise > 0.4f ? FieldPaint.Rock : FieldPaint.Grass;
            return noise > 0.55f ? FieldPaint.Forest : FieldPaint.Meadow;
        }

        protected override void PlaceNature()
        {
            var r = R("nature");
            float W = M.Width, D = M.Depth;
            for (int k = 0; k < 1100; k++)
            {
                var l = new Vector2(r.Range(-FieldMap.Margin + 4f, W + FieldMap.Margin - 4f), r.Range(0f, D + FieldMap.Margin - 4f));
                float s = l.y - CliffEdge(Mathf.Clamp(l.x, 0f, W));
                if (s < 14f) continue;
                bool outside = !M.InsidePlayable(l, 0f);
                if (!r.Chance(outside ? 0.7f : 0.3f)) continue;
                if (!NatureOk(l, 4.5f, 2f, 40f, true)) continue;
                if (outside && (l.x < 0f && Mathf.Abs(l.y - gateA.y) < 7f || l.x > W && Mathf.Abs(l.y - gateB.y) < 7f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.TreeTall : NatureKind.TreeRound, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
            for (int k = 0; k < 500; k++)
            {
                float x = r.Range(0f, W);
                var l = new Vector2(x, CliffEdge(x) + r.Range(-8f, 10f));
                if (!M.InsidePlayable(l, 1f) || M.DistToPaths(l) < 2.5f || Ledge(l, out _) > 0.05f) continue;
                if (M.WaterAt(l.x, l.y) != FieldWater.None && M.WaterDepth(l.x, l.y) > 0.4f) continue;
                AddNature(r.Chance(0.35f) ? NatureKind.Boulder : NatureKind.Rock, l, r.Range(0.8f, 1.6f), r, AreaType.Coast);
            }
            for (int k = 0; k < 1100; k++)
            {
                var l = new Vector2(r.Range(-4f, W + 4f), r.Range(-4f, D + 4f));
                if (!NatureOk(l, 1.6f, 0.8f, 40f, true)) continue;
                AddNature(NatureKind.Grass, l, r.Range(0.8f, 1.3f), r, AreaType.Coast);
            }
            for (int k = 0; k < 200; k++)
            {
                var l = new Vector2(r.Range(0f, W), r.Range(0f, D));
                if (l.y < CliffEdge(l.x) + 6f) continue;
                if (!NatureOk(l, 2.5f, 1.5f, 32f)) continue;
                AddNature(r.Chance(0.5f) ? NatureKind.Flower : NatureKind.Bush, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
            for (int k = 0; k < 60; k++)
            {
                var l = coveBeach + r.InsideUnitCircle() * 10f;
                if (!NatureOk(l, 1.5f, 0.6f, 30f)) continue;
                AddNature(NatureKind.Shell, l, r.Range(0.8f, 1.2f), r, AreaType.Coast);
            }
        }
    }
}
