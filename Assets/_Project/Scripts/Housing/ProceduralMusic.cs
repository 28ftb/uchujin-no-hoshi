using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Housing
{
    /// <summary>
    /// 楽器・音のなる床の音（音素材を使わず、その星の音階でその場で合成する）。
    /// 音階は星のシードで決まる（FurnitureCatalog.Culture.scale）。音色・音量は Unity 上で要調整。
    /// </summary>
    public static class ProceduralMusic
    {
        private const int SampleRate = 22050;
        private static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        public static void PlayNote(Vector3 pos, int degree)
        {
            Play(pos, new[] { degree }, 0.4f, 0);
        }

        public static void PlayTune(Vector3 pos, FurnitureStyle style, ulong seed)
        {
            var rng = new SeededRandom(seed == 0 ? 1UL : seed);
            int n = 8;
            var notes = new int[n];
            int cur = rng.Range(0, 5);
            for (int i = 0; i < n; i++)
            {
                cur = Mathf.Clamp(cur + rng.Range(-2, 3), 0, 9);
                notes[i] = cur;
            }
            int timbre = style == FurnitureStyle.Drum ? 2 : style == FurnitureStyle.Xylophone || style == FurnitureStyle.SoundTiles ? 1 : style == FurnitureStyle.Piano || style == FurnitureStyle.Gramophone ? 3 : 0;
            Play(pos, notes, style == FurnitureStyle.Drum ? 0.22f : 0.28f, timbre);
        }

        private static void Play(Vector3 pos, int[] degrees, float noteLen, int timbre)
        {
            AudioClip clip;
            try { clip = GetClip(degrees, noteLen, timbre); }
            catch (System.Exception e) { Debug.LogException(e); return; }
            if (clip == null) return;
            var go = new GameObject("HousingSound");
            if (Game.Instance != null && Game.Instance.SceneRoot != null) go.transform.SetParent(Game.Instance.SceneRoot, false);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 0.7f;
            src.volume = 0.5f;
            src.minDistance = 2f;
            src.maxDistance = 25f;
            src.Play();
            Object.Destroy(go, clip.length + 0.2f);
        }

        private static AudioClip GetClip(int[] degrees, float noteLen, int timbre)
        {
            var c = FurnitureCatalog.Culture;
            var scale = c != null && c.scale != null && c.scale.Length > 0 ? c.scale : new[] { 0, 2, 4, 7, 9 };
            int baseNote = c != null ? c.baseNote : 0;
            string key = string.Join(",", degrees) + "|" + noteLen + "|" + timbre + "|" + baseNote + "|" + string.Join(".", scale);
            if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
            if (cache.Count > 64) cache.Clear();
            int perNote = Mathf.RoundToInt(SampleRate * noteLen);
            int tail = SampleRate / 3;
            var data = new float[perNote * degrees.Length + tail];
            uint noise = 2463534242u;
            for (int n = 0; n < degrees.Length; n++)
            {
                int deg = degrees[n];
                int semitone = baseNote + scale[deg % scale.Length] + 12 * (deg / scale.Length);
                float freq = 330f * Mathf.Pow(2f, semitone / 12f);
                if (timbre == 2) freq *= 0.35f;
                int start = n * perNote;
                int len = perNote + tail;
                for (int i = 0; i < len && start + i < data.Length; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-t * (timbre == 2 ? 14f : timbre == 1 ? 9f : 5f)) * Mathf.Min(1f, t * 200f);
                    float s;
                    switch (timbre)
                    {
                        case 1: s = Mathf.Sin(2 * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(2 * Mathf.PI * freq * 3.9f * t); break;
                        case 2:
                            noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                            s = Mathf.Sin(2 * Mathf.PI * freq * t) * 0.8f + ((noise & 0xFFFF) / 65535f - 0.5f) * 0.4f * Mathf.Exp(-t * 30f);
                            break;
                        case 3: s = Mathf.Sin(2 * Mathf.PI * freq * t) + 0.5f * Mathf.Sin(4 * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(6 * Mathf.PI * freq * t); break;
                        default: s = Mathf.Sin(2 * Mathf.PI * freq * t) + 0.15f * Mathf.Sin(4 * Mathf.PI * freq * t); break;
                    }
                    data[start + i] += s * env * 0.25f;
                }
            }
            var clip = AudioClip.Create("HousingTune", data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            cache[key] = clip;
            return clip;
        }
    }
}
