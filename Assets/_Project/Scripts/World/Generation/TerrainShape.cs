using UnityEngine;
using Uchujin.Core;

namespace Uchujin.World
{
    /// <summary>
    /// 星の大まかな地形（海岸・山・高台・平地のうねり）を表す解析関数。村仕様 §3.1：
    /// 山から流れた川が森や村を通り海へつながる、といった地理的なつながりを持たせる。
    /// 川・池・建物の土台などの加工は Heightfield 側で行い、ここは「加工前の地形」とする。
    /// 周囲の見た目だけの地形（ワールドの外側）にもこの関数を使う。
    /// </summary>
    public sealed class TerrainShape
    {
        public Vector2 SeaDir;
        public Vector2 Perp;
        public float CoastDist;
        public float CoastWobble;
        public Vector2 MountainCenter;
        public float MountainRadius;
        public float MountainPeak;
        public Vector2 HillCenter;
        public float HillRadius;
        public float HillHeight;
        public float FlatAmp;
        public float ForestThreshold;
        public float FlowerThreshold;
        public float ForestScale;
        /// <summary>生成バージョン（2 から静かな高台が加わる）。</summary>
        public int Version = 1;
        /// <summary>静かな高台（生成バージョン2。マップ設計 v0.2 §2.6）：村から遠い、上が平らな高い丘。</summary>
        public bool HasQuiet;
        public Vector2 QuietCenter;
        public float QuietRadius;
        public float QuietHeight;
        /// <summary>
        /// 生成バージョン3：村の段丘（マップ設計 v0.3 §2.3・§5：川沿いの低い段・住宅の段・山側の段）。海岸と平行な段の崖2本。
        /// 崖の所々にゆるい坂の切れ目がある。距離は海岸線からの内陸距離。
        /// </summary>
        public bool HasTerraces;
        public float[] TerraceD = new float[2], TerraceH = new float[2], TerraceWob = new float[2], TerraceFreq = new float[2], TerracePhase = new float[2], TerracePass = new float[2];
        public float TerracePassSpacing;

        private WorldNoise baseN, coastN, ridgeN, forestN, flowerN, rockN, detailN;

        /// <param name="seaAngle">海の方角（ラジアン）。生成バージョン3は星の骨格の値を渡す（乱数の並びは変えない）。</param>
        public static TerrainShape Create(ulong seed, string attemptKey, SeededRandom r, int version = 1, float? seaAngle = null)
        {
            var s = new TerrainShape();
            float ang = r.Range(0f, Mathf.PI * 2f);
            if (seaAngle.HasValue) ang = seaAngle.Value;
            s.SeaDir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            s.Perp = new Vector2(-s.SeaDir.y, s.SeaDir.x);
            s.CoastDist = r.Range(WorldTuning.CoastDistMin, WorldTuning.CoastDistMax);
            s.CoastWobble = r.Range(8f, 22f);
            float mDist = r.Range(WorldTuning.MountainDistMin, WorldTuning.MountainDistMax);
            float mLat = r.Range(-75f, 75f);
            s.MountainCenter = -s.SeaDir * mDist + s.Perp * mLat;
            s.MountainRadius = r.Range(WorldTuning.MountainRadiusMin, WorldTuning.MountainRadiusMax);
            s.MountainPeak = r.Range(WorldTuning.MountainPeakMin, WorldTuning.MountainPeakMax);
            s.FlatAmp = r.Range(1.1f, 2.9f);
            s.ForestThreshold = r.Range(0.5f, 0.6f);
            s.FlowerThreshold = r.Range(0.58f, 0.68f);
            s.ForestScale = r.Range(0.012f, 0.02f);
            s.HillRadius = r.Range(WorldTuning.HillRadiusMin, WorldTuning.HillRadiusMax);
            s.HillHeight = r.Range(WorldTuning.HillHeightMin, WorldTuning.HillHeightMax);

            s.baseN = new WorldNoise(seed, attemptKey + ".base");
            s.coastN = new WorldNoise(seed, attemptKey + ".coast");
            s.ridgeN = new WorldNoise(seed, attemptKey + ".ridge");
            s.forestN = new WorldNoise(seed, attemptKey + ".forest");
            s.flowerN = new WorldNoise(seed, attemptKey + ".flower");
            s.rockN = new WorldNoise(seed, attemptKey + ".rock");
            s.detailN = new WorldNoise(seed, attemptKey + ".detail");

            // 高台（見晴らし台）：海から離れ、山とも少し離れた場所
            s.HillCenter = s.MountainCenter * 0.2f;
            for (int i = 0; i < 200; i++)
            {
                var c = new Vector2(r.Range(-150f, 150f), r.Range(-150f, 150f));
                if (s.CoastD(c.x, c.y) < 70f) continue;
                if (Vector2.Distance(c, s.MountainCenter) < s.MountainRadius + 35f) continue;
                if (c.magnitude < 45f) continue;
                s.HillCenter = c;
                break;
            }

            // 生成バージョン2：静かな高台（にぎやかな見晴らし台とは別。海から遠く、丘・山から離れた外寄りの場所）
            s.Version = version;
            if (version >= 2)
            {
                s.QuietRadius = r.Range(24f, 30f);
                s.QuietHeight = r.Range(17f, 22f);
                for (int pass = 0; pass < 3 && !s.HasQuiet; pass++)
                {
                    float hillGap = pass == 0 ? 115f : pass == 1 ? 100f : 85f;
                    float minFromCenter = pass == 0 ? 95f : pass == 1 ? 80f : 65f;
                    for (int i = 0; i < 300; i++)
                    {
                        var c = new Vector2(r.Range(-150f, 150f), r.Range(-150f, 150f));
                        float lim = WorldTuning.Half - s.QuietRadius - 28f;
                        if (Mathf.Abs(c.x) > lim || Mathf.Abs(c.y) > lim) continue;
                        if (s.CoastD(c.x, c.y) < 90f) continue;
                        if (Vector2.Distance(c, s.HillCenter) < hillGap) continue;
                        if (c.magnitude < minFromCenter) continue;
                        // 山からも離す（鉱山への本道が静かな高台の近くを通らないように）
                        if (Vector2.Distance(c, s.MountainCenter) < s.MountainRadius * (pass == 2 ? 1.1f : 1.3f) + s.QuietRadius) continue;
                        s.QuietCenter = c;
                        s.HasQuiet = true;
                        break;
                    }
                }
            }
            // 生成バージョン3：村の段丘と、一段高い見晴らし台の丘（乱数は版2までの並びの後ろで引く）
            if (version >= 3)
            {
                s.HasTerraces = true;
                s.TerraceD[0] = r.Range(38f, 52f);
                s.TerraceD[1] = r.Range(132f, 158f);
                for (int k = 0; k < 2; k++)
                {
                    s.TerraceH[k] = r.Range(4f, 6.5f);
                    s.TerraceWob[k] = r.Range(6f, 14f);
                    s.TerraceFreq[k] = r.Range(0.012f, 0.025f);
                    s.TerracePhase[k] = r.Range(0f, Mathf.PI * 2f);
                    s.TerracePass[k] = r.Range(0f, 120f);
                }
                s.TerracePassSpacing = r.Range(85f, 115f);
                s.HillHeight = r.Range(14f, 19f);
            }
            return s;
        }

        /// <summary>段丘の段の高さの合計（内陸距離 d・海岸に沿った座標 t の所）。</summary>
        public float TerraceAt(float d, float t)
        {
            if (!HasTerraces) return 0f;
            float h = 0f;
            for (int k = 0; k < 2; k++)
            {
                float dk = TerraceD[k] + Mathf.Sin(t * TerraceFreq[k] + TerracePhase[k]) * TerraceWob[k];
                float pass = 0f;
                for (int j = -5; j <= 5; j++)
                {
                    float u = (t - (TerracePass[k] + j * TerracePassSpacing)) / 10f;
                    pass = Mathf.Max(pass, Mathf.Exp(-u * u));
                }
                float width = Mathf.Lerp(3.5f, 50f, pass);
                h += TerraceH[k] * GeoUtil.SmoothStep(dk - width * 0.5f, dk + width * 0.5f, d);
            }
            return h;
        }

        /// <summary>海岸線からの内陸方向の距離（負なら海）。</summary>
        public float CoastD(float x, float z)
        {
            var p = new Vector2(x, z);
            float s = Vector2.Dot(p, SeaDir);
            float t = Vector2.Dot(p, Perp);
            return CoastLineS(t) - s;
        }

        public float CoastLineS(float t) => CoastDist + coastN.Line(t * 0.011f) * CoastWobble;

        /// <summary>海岸線に沿った座標 t と内陸距離 d から位置を求める。</summary>
        public Vector2 CoastPoint(float t, float d)
        {
            float s = CoastLineS(t) - d;
            return SeaDir * s + Perp * t;
        }

        public float AlongCoast(float x, float z) => Vector2.Dot(new Vector2(x, z), Perp);

        public float BaseHeight(float x, float z)
        {
            var p = new Vector2(x, z);
            float d = CoastD(x, z);
            float plain = 3.2f + Mathf.Max(0f, d) * 0.014f + baseN.Fbm(x * 0.011f, z * 0.011f, 4) * FlatAmp + detailN.Fbm(x * 0.06f, z * 0.06f, 2) * 0.3f;
            plain = Mathf.Max(plain, 1.6f);
            float h;
            if (d < -3f) h = Mathf.Max(-6f, (d + 3f) * 0.22f);
            else if (d < 12f) h = 0.25f + d * 0.08f;
            else h = Mathf.Lerp(1.21f, plain, GeoUtil.SmoothStep(12f, 36f, d));
            if (HasTerraces && d > 0f) h += TerraceAt(d, AlongCoast(x, z));

            // 山（鉱山の入口がある）
            float dm = Vector2.Distance(p, MountainCenter) / MountainRadius;
            if (dm < 1.7f)
            {
                float b = Mathf.Max(0f, 1f - dm * dm);
                b *= b;
                float ridge = ridgeN.Ridged(x * 0.028f, z * 0.028f, 4);
                h += MountainPeak * b * (0.7f + 0.6f * ridge) + 5f * (1f - GeoUtil.SmoothStep(0.7f, 1.7f, dm));
            }

            // 高台（上が平らな丘）
            float dh = Vector2.Distance(p, HillCenter) / HillRadius;
            if (dh < 1.3f) h += HillHeight * (1f - GeoUtil.SmoothStep(0.32f, 1.15f, dh));

            // 静かな高台（上が広く平らで、木立・草地・小さな池を置ける）
            if (HasQuiet)
            {
                float dq = Vector2.Distance(p, QuietCenter) / QuietRadius;
                if (dq < 1.35f) h += QuietHeight * (1f - GeoUtil.SmoothStep(0.6f, 1.25f, dq));
            }

            // 内陸側の外周は丘で囲む（ワールドの外の景色）
            float edge = WorldTuning.Half - Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
            float inland = GeoUtil.SmoothStep(25f, 70f, d);
            if (inland > 0f)
            {
                float rise = 1f - GeoUtil.SmoothStep(-60f, 40f, edge);
                h += rise * inland * (16f + 14f * ridgeN.Value(x * 0.01f + 50f, z * 0.01f));
            }
            return h;
        }

        /// <summary>山の中心からの距離（半径で正規化）。</summary>
        public float MountainFactor(float x, float z) => Vector2.Distance(new Vector2(x, z), MountainCenter) / MountainRadius;

        /// <summary>森の濃さ 0〜1（しきい値 ForestThreshold を超えると森）。</summary>
        public float Forest01(float x, float z)
        {
            float v = forestN.Fbm(x * ForestScale, z * ForestScale, 3) * 0.5f + 0.5f;
            float dm = MountainFactor(x, z);
            v += 0.16f * (1f - GeoUtil.SmoothStep(0.55f, 1.45f, dm));
            v -= 0.3f * (1f - GeoUtil.SmoothStep(14f, 45f, CoastD(x, z)));
            return Mathf.Clamp01(v);
        }

        public bool IsForest(float x, float z) => Forest01(x, z) > ForestThreshold;

        /// <summary>花の多さ 0〜1。</summary>
        public float Flower01(float x, float z) => flowerN.Fbm(x * 0.03f + 7f, z * 0.03f - 3f, 3) * 0.5f + 0.5f;

        /// <summary>海岸の岩場らしさ 0〜1（海岸線に沿って変化）。</summary>
        public float Rocky01(float x, float z) => rockN.Line(AlongCoast(x, z) * 0.02f, 2) * 0.5f + 0.5f;

        /// <summary>地表の細かな色むら -1〜1。</summary>
        public float Detail(float x, float z) => detailN.Fbm(x * 0.09f, z * 0.09f, 2);
    }
}
