using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Uchujin.Core;

namespace Uchujin.Weather
{
    /// <summary>
    /// 昼夜と天候の演出（時間・天候仕様 §5、§12）。
    ///  ・太陽（Game が作る "Sun" の Directional Light）の向き・色・強さ、月明かり
    ///  ・環境光・フォグ・カメラ背景色（空の色）
    ///  ・夜空の星と月、星の光帯
    ///  ・雨・霧雨・大雨・光雨・逆雨・星風・結晶霞・花潮のパーティクル（カメラ周辺に追従）
    ///  ・屋内・地下では天候の演出を消し、環境光を切り替える（AreaDescriptor.ShowWeather / AmbientOverride / IsUnderground）
    /// 見た目だけの揺らぎなので UnityEngine.Random を使ってよい。
    /// </summary>
    public sealed class WeatherVisuals : MonoBehaviour
    {
        private enum Mode { Outdoor, Indoor, Underground }

        private WeatherSystem sys;
        private Light sun;
        private bool createdSun;
        private float sunDefaultIntensity;
        private Quaternion sunDefaultRotation;
        private Color sunDefaultColor;
        private LightShadows sunDefaultShadows;
        private Mode mode = Mode.Outdoor;
        private Color? ambientOverride;

        private Texture2D dotTex;
        private readonly List<Material> mats = new List<Material>();
        private ParticleSystem rain, lightRain, reverseRain, starWind, crystal, bloom;
        private Transform starDome;
        private Material starMat, bandMat, moonMat;
        private GameObject band;

        // 平滑化した現在値
        private Color curSky, curAmbient, curFog, curSunColor;
        private float curSunIntensity, curFogStart = 160f, curFogEnd = 560f;
        private float curNight, curBand, curRain, curLightRain, curReverse, curStarWind, curCrystal, curBloom;
        private bool initialized;

        public void Init(WeatherSystem system)
        {
            sys = system;
            FindSun();
            dotTex = MakeDot(32);
            BuildParticles();
            BuildSky();
            var area = Game.Instance != null && Game.Instance.LocalPlayer != null ? Game.Instance.Areas.Get(Game.Instance.LocalPlayer.CurrentAreaId) : null;
            SetArea(area);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.ambientMode = AmbientMode.Flat;
            Snap();
        }

        private void FindSun()
        {
#if UNITY_6000_6_OR_NEWER
            foreach (var l in FindObjectsByType<Light>())
#else
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
            {
                if (l.type != LightType.Directional) continue;
                if (sun == null || l.name == "Sun") sun = l;
            }
            if (sun == null)
            {
                var go = new GameObject("Sun");
                go.transform.SetParent(transform, false);
                sun = go.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.shadows = LightShadows.Soft;
                createdSun = true;
            }
            sunDefaultIntensity = sun.intensity;
            sunDefaultRotation = sun.transform.rotation;
            sunDefaultColor = sun.color;
            sunDefaultShadows = sun.shadows;
        }

        /// <summary>エリア移動：屋内・地下では天候演出を消して環境光を切り替える。</summary>
        public void SetArea(AreaDescriptor area)
        {
            if (area == null || (area.ShowWeather && !area.IsIndoor && !area.IsUnderground))
            {
                mode = Mode.Outdoor;
                ambientOverride = null;
            }
            else
            {
                mode = area.IsUnderground ? Mode.Underground : Mode.Indoor;
                ambientOverride = area.AmbientOverride;
            }
            bool outdoor = mode == Mode.Outdoor;
            SetActive(rain, outdoor);
            SetActive(lightRain, outdoor);
            SetActive(reverseRain, outdoor);
            SetActive(starWind, outdoor);
            SetActive(crystal, outdoor);
            SetActive(bloom, outdoor);
            if (starDome != null) starDome.gameObject.SetActive(outdoor);
            if (initialized) Snap();
        }

        private static void SetActive(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (!on) ps.Clear();
            ps.gameObject.SetActive(on);
        }

        private void OnDestroy()
        {
            if (sun != null)
            {
                if (createdSun) Destroy(sun.gameObject);
                else
                {
                    sun.intensity = sunDefaultIntensity;
                    sun.transform.rotation = sunDefaultRotation;
                    sun.color = sunDefaultColor;
                    sun.shadows = sunDefaultShadows;
                }
            }
            RenderSettings.fog = false;
            foreach (var m in mats) if (m != null) Destroy(m);
            if (dotTex != null) Destroy(dotTex);
        }

        // =================================================================
        // 生成
        // =================================================================

        private static Texture2D MakeDot(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "WeatherDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n - 0.5f, dy = (y + 0.5f) / n - 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (3f - 2f * a);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        private Material UnlitMat(Color c)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
            if (sh == null) sh = Shader.Find("Unlit/Transparent");
            if (sh == null) sh = Uchujin.Core.Visual.MaterialCache.LitShader;
            var m = new Material(sh) { name = "WeatherUnlit" };
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", dotTex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", dotTex);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.renderQueue = 3000;
            mats.Add(m);
            return m;
        }

        private ParticleSystem MakePS(string name, int max, Vector2 lifetime, Vector2 speed, Vector2 size, Color a, Color b, float gravity,
            bool stretch, Vector3 box, Vector3 euler, float lengthScale = 1.5f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = box;
            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(0f);
            vol.y = new ParticleSystem.MinMaxCurve(0f);
            vol.z = new ParticleSystem.MinMaxCurve(0f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = UnlitMat(Color.white);
            r.renderMode = stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            if (stretch)
            {
                r.velocityScale = 0.05f;
                r.lengthScale = lengthScale;
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private void BuildParticles()
        {
            var down = new Vector3(90f, 0f, 0f);
            var up = new Vector3(-90f, 0f, 0f);
            rain = MakePS("Rain", WeatherTuning.RainMaxParticles, new Vector2(1.2f, 1.6f), new Vector2(15f, 19f), new Vector2(0.04f, 0.07f),
                new Color(0.75f, 0.85f, 1f, 0.55f), new Color(0.85f, 0.9f, 1f, 0.75f), 0f, true, new Vector3(48f, 48f, 1f), down, 2.2f);
            lightRain = MakePS("LightRain", 2000, new Vector2(2.2f, 3f), new Vector2(6f, 8f), new Vector2(0.1f, 0.18f),
                new Color(0.6f, 1f, 0.95f, 0.9f), new Color(1f, 0.75f, 0.95f, 0.9f), 0f, true, new Vector3(44f, 44f, 1f), down, 1.3f);
            reverseRain = MakePS("ReverseRain", 1500, new Vector2(2.5f, 3.5f), new Vector2(4f, 7f), new Vector2(0.06f, 0.1f),
                new Color(0.7f, 0.85f, 1f, 0.7f), new Color(0.9f, 0.95f, 1f, 0.8f), 0f, true, new Vector3(40f, 40f, 1f), up, 1.6f);
            starWind = MakePS("StarWind", 1500, new Vector2(3f, 4.5f), new Vector2(0f, 0.2f), new Vector2(0.1f, 0.22f),
                new Color(0.5f, 0.95f, 1f, 0.95f), new Color(1f, 0.9f, 0.5f, 0.95f), 0f, true, new Vector3(60f, 9f, 60f), Vector3.zero, 4f);
            crystal = MakePS("CrystalHaze", 1500, new Vector2(3f, 6f), new Vector2(0.05f, 0.3f), new Vector2(0.06f, 0.16f),
                new Color(0.85f, 0.8f, 1f, 0.9f), new Color(1f, 1f, 1f, 1f), -0.01f, false, new Vector3(40f, 10f, 40f), Vector3.zero);
            var cs = crystal.sizeOverLifetime;
            cs.enabled = true;
            cs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(0.5f, 0.3f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));
            var csh = crystal.shape;
            csh.randomDirectionAmount = 1f;
            bloom = MakePS("BloomTide", 1500, new Vector2(5f, 8f), new Vector2(0.3f, 0.9f), new Vector2(0.08f, 0.18f),
                new Color(1f, 0.7f, 0.85f, 0.95f), new Color(1f, 0.95f, 0.5f, 0.95f), 0f, false, new Vector3(40f, 40f, 2f), up);
            var bs = bloom.sizeOverLifetime;
            bs.enabled = true;
            bs.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f)));
        }

        private void BuildSky()
        {
            var dome = new GameObject("StarDome");
            dome.transform.SetParent(transform, false);
            starDome = dome.transform;
            var c = sys.Climate;
            // 星
            {
                var verts = new List<Vector3>();
                var cols = new List<Color>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int i = 0; i < WeatherTuning.StarCount; i++)
                {
                    var dir = UnityEngine.Random.onUnitSphere;
                    if (dir.y < 0.02f) dir.y = -dir.y + 0.05f;
                    dir.Normalize();
                    float s = UnityEngine.Random.Range(0.9f, 2.6f) * (UnityEngine.Random.value < 0.06f ? 2f : 1f);
                    var col = Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.92f, 0.75f), UnityEngine.Random.value);
                    col.a = UnityEngine.Random.Range(0.5f, 1f);
                    AddQuad(verts, cols, uvs, tris, dir * WeatherTuning.SkyRadius, s, col);
                }
                // 光帯に沿って星を多めに
                var n = c.StarBandNormal.normalized;
                var a = Vector3.Cross(n, Vector3.up).normalized;
                if (a.sqrMagnitude < 0.01f) a = Vector3.right;
                var b = Vector3.Cross(n, a).normalized;
                for (int i = 0; i < 400; i++)
                {
                    float t = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                    var dir = (Mathf.Cos(t) * a + Mathf.Sin(t) * b + n * UnityEngine.Random.Range(-0.12f, 0.12f)).normalized;
                    if (dir.y < 0.02f) continue;
                    var col = Color.Lerp(new Color(0.8f, 0.7f, 1f), new Color(0.6f, 1f, 1f), UnityEngine.Random.value);
                    col.a = UnityEngine.Random.Range(0.4f, 0.9f);
                    AddQuad(verts, cols, uvs, tris, dir * WeatherTuning.SkyRadius, UnityEngine.Random.Range(0.7f, 1.8f), col);
                }
                starMat = UnlitMat(new Color(1f, 1f, 1f, 0f));
                MakeMeshObject("Stars", dome.transform, verts, cols, uvs, tris, starMat);
            }
            // 月（宇宙らしく 1〜2個）
            {
                var verts = new List<Vector3>();
                var cols = new List<Color>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                for (int i = 0; i < c.MoonCount; i++)
                {
                    var col = c.MoonColors[i];
                    col.a = 1f;
                    AddQuad(verts, cols, uvs, tris, c.MoonDirs[i] * (WeatherTuning.SkyRadius - 10f), c.MoonSizes[i], col);
                    var glow = col;
                    glow.a = 0.25f;
                    AddQuad(verts, cols, uvs, tris, c.MoonDirs[i] * (WeatherTuning.SkyRadius - 5f), c.MoonSizes[i] * 2.4f, glow);
                }
                moonMat = UnlitMat(new Color(1f, 1f, 1f, 0f));
                MakeMeshObject("Moons", dome.transform, verts, cols, uvs, tris, moonMat);
            }
            // 星の光帯（大きな光の帯）
            {
                var n = c.StarBandNormal.normalized;
                var a = Vector3.Cross(n, Vector3.up).normalized;
                if (a.sqrMagnitude < 0.01f) a = Vector3.right;
                var b = Vector3.Cross(n, a).normalized;
                var verts = new List<Vector3>();
                var cols = new List<Color>();
                var uvs = new List<Vector2>();
                var tris = new List<int>();
                int seg = 72;
                float[] offs = { -0.22f, -0.08f, 0f, 0.08f, 0.22f };
                float[] alphas = { 0f, 0.55f, 0.85f, 0.55f, 0f };
                for (int i = 0; i <= seg; i++)
                {
                    float t = i / (float)seg * Mathf.PI * 2f;
                    var mid = Mathf.Cos(t) * a + Mathf.Sin(t) * b;
                    var hue = Color.HSVToRGB(Mathf.Repeat(0.6f + Mathf.Sin(t * 2f) * 0.12f, 1f), 0.55f, 1f);
                    for (int k = 0; k < offs.Length; k++)
                    {
                        var dir = (mid + n * offs[k]).normalized;
                        verts.Add(dir * (WeatherTuning.SkyRadius + 5f));
                        var col = hue;
                        col.a = alphas[k];
                        cols.Add(col);
                        uvs.Add(new Vector2(0.5f, 0.5f));
                    }
                    if (i > 0)
                    {
                        int r0 = (i - 1) * offs.Length, r1 = i * offs.Length;
                        for (int k = 0; k < offs.Length - 1; k++)
                        {
                            tris.Add(r0 + k); tris.Add(r1 + k); tris.Add(r0 + k + 1);
                            tris.Add(r0 + k + 1); tris.Add(r1 + k); tris.Add(r1 + k + 1);
                            // 両面
                            tris.Add(r0 + k); tris.Add(r0 + k + 1); tris.Add(r1 + k);
                            tris.Add(r0 + k + 1); tris.Add(r1 + k + 1); tris.Add(r1 + k);
                        }
                    }
                }
                bandMat = UnlitMat(new Color(1f, 1f, 1f, 0f));
                band = MakeMeshObject("StarBand", dome.transform, verts, cols, uvs, tris, bandMat);
            }
        }

        private static void AddQuad(List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> t, Vector3 center, float size, Color col)
        {
            var n = center.normalized;
            var right = Vector3.Cross(n, Mathf.Abs(n.y) > 0.95f ? Vector3.forward : Vector3.up).normalized * size * 0.5f;
            var up = Vector3.Cross(right, n).normalized * size * 0.5f;
            int b = v.Count;
            v.Add(center - right - up);
            v.Add(center + right - up);
            v.Add(center + right + up);
            v.Add(center - right + up);
            for (int i = 0; i < 4; i++) c.Add(col);
            uv.Add(new Vector2(0, 0));
            uv.Add(new Vector2(1, 0));
            uv.Add(new Vector2(1, 1));
            uv.Add(new Vector2(0, 1));
            // 両面
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            t.Add(b); t.Add(b + 2); t.Add(b + 3);
            t.Add(b); t.Add(b + 2); t.Add(b + 1);
            t.Add(b); t.Add(b + 3); t.Add(b + 2);
        }

        private static GameObject MakeMeshObject(string name, Transform parent, List<Vector3> v, List<Color> c, List<Vector2> uv, List<int> t, Material m)
        {
            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * WeatherTuning.SkyRadius * 2.5f);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        // =================================================================
        // 毎フレーム
        // =================================================================

        private void Snap()
        {
            Compute(out var t);
            curSky = t.sky;
            curAmbient = t.ambient;
            curFog = t.fog;
            curSunColor = t.sunColor;
            curSunIntensity = t.sunIntensity;
            curFogStart = t.fogStart;
            curFogEnd = t.fogEnd;
            curNight = t.night;
            curBand = t.band;
            initialized = true;
            Apply(t, 1f);
        }

        private struct Targets
        {
            public Color sky, ambient, fog, sunColor;
            public float sunIntensity, fogStart, fogEnd, night, band;
            public float rain, lightRain, reverse, starWind, crystal, bloom;
            public Quaternion sunRot;
            public bool fogOn;
            public Vector2 wind;
        }

        private void LateUpdate()
        {
            if (sys == null || sun == null) return;
            Compute(out var t);
            float k = 1f - Mathf.Exp(-WeatherTuning.VisualBlendSpeed * 4f * UnityEngine.Time.deltaTime);
            Apply(t, k);
        }

        private Vector3 Focus()
        {
            var game = Game.Instance;
            var p = game != null ? game.LocalPlayer : null;
            if (p != null && p.Avatar != null) return p.Position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        private void Compute(out Targets t)
        {
            t = new Targets();
            var clock = Game.Instance != null ? Game.Instance.Time : null;
            float hour = clock != null ? clock.HourFloat : 12f;
            var focus = Focus();
            var w = mode == Mode.Outdoor ? sys.GetAt(focus) : sys.Current;
            var c = sys.Climate;

            // 太陽・月
            float dayFrac = (hour - 6f) / 12f;
            float elev = Mathf.Sin(dayFrac * Mathf.PI) * WeatherTuning.SunMaxElevation;
            float yaw = 270f + dayFrac * 180f;
            float dayness = WeatherSystem.Dayness(hour);
            float cloud = WeatherSystem.CloudFactor(w);
            t.night = 1f - dayness;
            if (dayness > 0.5f) t.sunRot = Quaternion.Euler(Mathf.Max(6f, elev), yaw, 0f);
            else t.sunRot = Quaternion.Euler(38f, yaw + 180f + 30f, 0f);
            float sunI = WeatherTuning.SunIntensity * cloud * GeoSmooth(-2f, 12f, elev);
            float moonI = WeatherTuning.MoonIntensity * t.night;
            t.sunIntensity = Mathf.Max(sunI, moonI);
            var warm = new Color(1f, 0.62f, 0.4f);
            var day = new Color(1f, 0.97f, 0.9f);
            var moon = new Color(0.62f, 0.72f, 1f);
            t.sunColor = dayness > 0.5f ? Color.Lerp(warm, day, GeoSmooth(0f, 25f, elev)) : moon;

            // 空の色
            Color skyDay;
            switch (w.sky)
            {
                case SkyState.Clear: skyDay = new Color(0.42f, 0.68f, 1f); break;
                case SkyState.Sunny: skyDay = new Color(0.52f, 0.75f, 0.98f); break;
                case SkyState.Cloudy: skyDay = new Color(0.63f, 0.69f, 0.77f); break;
                default: skyDay = new Color(0.5f, 0.53f, 0.59f); break;
            }
            skyDay = Color.Lerp(skyDay, c.SkyTint * 0.85f, 0.18f);
            if (w.IsRaining) skyDay *= w.precipitation == Precipitation.HeavyRain ? 0.72f : 0.86f;
            if (w.Has(AlienPhenomenon.LightRain)) skyDay = Color.Lerp(skyDay, new Color(0.6f, 0.85f, 0.95f), 0.25f);
            if (w.Has(AlienPhenomenon.CrystalHaze)) skyDay = Color.Lerp(skyDay, new Color(0.82f, 0.76f, 1f), 0.3f);
            if (w.Has(AlienPhenomenon.BloomTide)) skyDay = Color.Lerp(skyDay, new Color(1f, 0.82f, 0.9f), 0.22f);
            var nightSky = WeatherTuning.NightSky;
            if (w.Has(AlienPhenomenon.StarBand)) nightSky = Color.Lerp(nightSky, new Color(0.08f, 0.04f, 0.17f), 0.7f);
            if (w.Has(AlienPhenomenon.LightRain)) nightSky = Color.Lerp(nightSky, new Color(0.05f, 0.12f, 0.16f), 0.6f);
            float twilight = Mathf.Clamp01(1f - Mathf.Abs(elev - 2f) / 12f) * (w.sky >= SkyState.Overcast ? 0.4f : 0.85f);
            var sky = Color.Lerp(nightSky, skyDay, dayness);
            sky = Color.Lerp(sky, WeatherTuning.TwilightSky, twilight * 0.55f);
            t.sky = sky;
            t.ambient = Color.Lerp(WeatherTuning.NightAmbient, WeatherTuning.DayAmbient * Mathf.Lerp(0.85f, 1.05f, cloud), dayness);
            t.ambient = Color.Lerp(t.ambient, new Color(1f, 0.75f, 0.6f), twilight * 0.2f);
            if (w.Has(AlienPhenomenon.LightRain) || w.Has(AlienPhenomenon.StarBand)) t.ambient += new Color(0.05f, 0.05f, 0.08f);

            // フォグ（見通し）
            t.fogOn = true;
            t.fog = Color.Lerp(sky, Color.white, dayness * 0.12f);
            switch (w.visibility)
            {
                case VisibilityLevel.LightFog: t.fogStart = 25f; t.fogEnd = 170f; break;
                case VisibilityLevel.DenseFog: t.fogStart = 6f; t.fogEnd = 70f; break;
                default: t.fogStart = 160f; t.fogEnd = 560f; break;
            }
            if (w.precipitation == Precipitation.HeavyRain) { t.fogStart = Mathf.Min(t.fogStart, 20f); t.fogEnd = Mathf.Min(t.fogEnd, 160f); }
            else if (w.precipitation == Precipitation.Rain) { t.fogStart = Mathf.Min(t.fogStart, 60f); t.fogEnd = Mathf.Min(t.fogEnd, 300f); }
            if (w.Has(AlienPhenomenon.CrystalHaze)) t.fog = Color.Lerp(t.fog, new Color(0.85f, 0.8f, 1f), 0.4f * dayness + 0.2f);

            // パーティクル
            t.rain = WeatherTuning.RainRates[(int)w.precipitation];
            t.lightRain = w.Has(AlienPhenomenon.LightRain) ? WeatherTuning.LightRainRate : 0f;
            t.reverse = w.Has(AlienPhenomenon.ReverseRain) ? WeatherTuning.ReverseRainRate : 0f;
            t.starWind = w.Has(AlienPhenomenon.StarWind) ? WeatherTuning.StarWindRate * (w.wind == WindLevel.Strong ? 1.6f : 1f) : 0f;
            t.crystal = w.Has(AlienPhenomenon.CrystalHaze) ? WeatherTuning.CrystalHazeRate : 0f;
            t.bloom = w.Has(AlienPhenomenon.BloomTide) ? WeatherTuning.BloomTideRate : 0f;
            t.band = w.Has(AlienPhenomenon.StarBand) ? 1f : 0f;
            float windSpeed = w.wind == WindLevel.Strong ? 7f : w.wind == WindLevel.Breeze ? 2.5f : 0.4f;
            t.wind = c.PrevailingWind * windSpeed;

            // 屋内・地下
            if (mode == Mode.Indoor)
            {
                t.sunIntensity = WeatherTuning.IndoorSunIntensity;
                t.sunColor = new Color(1f, 0.95f, 0.88f);
                t.sunRot = Quaternion.Euler(55f, 30f, 0f);
                t.ambient = ambientOverride ?? WeatherTuning.IndoorAmbient;
                t.fogOn = false;
                t.sky = new Color(0.06f, 0.06f, 0.08f);
                t.rain = t.lightRain = t.reverse = t.starWind = t.crystal = t.bloom = 0f;
                t.night = 0f;
                t.band = 0f;
            }
            else if (mode == Mode.Underground)
            {
                t.sunIntensity = WeatherTuning.UndergroundSunIntensity;
                t.sunColor = new Color(0.6f, 0.65f, 0.8f);
                t.sunRot = Quaternion.Euler(80f, 0f, 0f);
                t.ambient = ambientOverride ?? WeatherTuning.UndergroundAmbient;
                t.fogOn = true;
                t.fog = new Color(0.02f, 0.02f, 0.03f);
                t.fogStart = 14f;
                t.fogEnd = 70f;
                t.sky = new Color(0.01f, 0.01f, 0.015f);
                t.rain = t.lightRain = t.reverse = t.starWind = t.crystal = t.bloom = 0f;
                t.night = 0f;
                t.band = 0f;
            }
        }

        private static float GeoSmooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        private void Apply(Targets t, float k)
        {
            curSky = Color.Lerp(curSky, t.sky, k);
            curAmbient = Color.Lerp(curAmbient, t.ambient, k);
            curFog = Color.Lerp(curFog, t.fog, k);
            curSunColor = Color.Lerp(curSunColor, t.sunColor, k);
            curSunIntensity = Mathf.Lerp(curSunIntensity, t.sunIntensity, k);
            curFogStart = Mathf.Lerp(curFogStart, t.fogStart, k);
            curFogEnd = Mathf.Lerp(curFogEnd, t.fogEnd, k);
            curNight = Mathf.Lerp(curNight, t.night, k);
            curBand = Mathf.Lerp(curBand, t.band, k * 0.5f);
            curRain = Mathf.Lerp(curRain, t.rain, k);
            curLightRain = Mathf.Lerp(curLightRain, t.lightRain, k);
            curReverse = Mathf.Lerp(curReverse, t.reverse, k);
            curStarWind = Mathf.Lerp(curStarWind, t.starWind, k);
            curCrystal = Mathf.Lerp(curCrystal, t.crystal, k);
            curBloom = Mathf.Lerp(curBloom, t.bloom, k);

            // 太陽
            sun.transform.rotation = Quaternion.Slerp(sun.transform.rotation, t.sunRot, k >= 1f ? 1f : Mathf.Min(1f, k * 2f));
            sun.intensity = curSunIntensity;
            sun.color = curSunColor;
            sun.shadows = mode == Mode.Underground ? LightShadows.None : sunDefaultShadows == LightShadows.None ? LightShadows.Soft : sunDefaultShadows;
            sun.shadowStrength = Mathf.Lerp(0.85f, 0.4f, curNight);

            // 環境光
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = curAmbient;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(curAmbient);
            RenderSettings.ambientProbe = sh;

            // フォグ
            RenderSettings.fog = t.fogOn;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = curFog;
            RenderSettings.fogStartDistance = curFogStart;
            RenderSettings.fogEndDistance = curFogEnd;

            // 空（カメラ背景）
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = curSky;
                if (starDome != null) starDome.position = cam.transform.position;
            }
            if (starDome != null)
            {
                starDome.rotation = Quaternion.Euler(0f, (UnityEngine.Time.time * 0.15f) % 360f, 0f);
                float starA = Mathf.Clamp01(curNight * 1.2f - 0.15f) * (mode == Mode.Outdoor ? 1f : 0f);
                var w = sys.Current;
                if (w.sky == SkyState.Overcast || w.IsRaining) starA *= 0.15f;
                else if (w.sky == SkyState.Cloudy) starA *= 0.5f;
                SetAlpha(starMat, starA);
                SetAlpha(moonMat, starA);
                SetAlpha(bandMat, starA * curBand * 0.9f);
                if (band != null) band.SetActive(curBand > 0.01f);
            }

            // パーティクル（カメラ周辺に追従）
            var focus = Focus();
            UpdatePS(rain, curRain, focus + Vector3.up * 16f, t.wind);
            UpdatePS(lightRain, curLightRain, focus + Vector3.up * 14f, t.wind * 0.5f);
            UpdatePS(reverseRain, curReverse, focus + Vector3.up * 0.3f, t.wind * 0.3f);
            var sw = t.wind.sqrMagnitude > 0.01f ? t.wind.normalized * 9f : sys.Climate.PrevailingWind * 9f;
            UpdatePS(starWind, curStarWind, focus + Vector3.up * 4f - new Vector3(sw.x, 0f, sw.y) * 2f, sw);
            UpdatePS(crystal, curCrystal, focus + Vector3.up * 3f, t.wind * 0.1f);
            UpdatePS(bloom, curBloom, focus + Vector3.up * 0.4f, t.wind * 0.2f);
        }

        private static void SetAlpha(Material m, float a)
        {
            if (m == null) return;
            var c = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        private static void UpdatePS(ParticleSystem ps, float rate, Vector3 pos, Vector2 wind)
        {
            if (ps == null || !ps.gameObject.activeSelf) return;
            ps.transform.position = pos;
            var em = ps.emission;
            em.rateOverTime = rate < 1f ? 0f : rate;
            var vol = ps.velocityOverLifetime;
            vol.x = new ParticleSystem.MinMaxCurve(wind.x);
            vol.z = new ParticleSystem.MinMaxCurve(wind.y);
        }
    }
}
