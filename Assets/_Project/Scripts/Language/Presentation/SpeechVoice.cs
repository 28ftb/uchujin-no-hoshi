using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 簡単な「声」（宇宙語仕様 §11.2【提案】、タブレット仕様 §13.2）。
    /// 発音表記の音節ごとに短い音を AudioClip.Create で手続き生成して鳴らす。
    /// ・話者ごとに音程が違う（住民の系統で高め・低めに寄せる）。マネピの再生は元の話者の声を真似る。
    /// ・母音で音色、子音で立ち上がり（破裂・摩擦）を変え、表示された綴りと音の対応を崩さない。
    /// ・疑問文は語尾が上がり、平叙文は下がる。
    /// 設定（LanguageSettings.VoiceEnabled）でオフにできる。音の作りは【仮】。
    /// </summary>
    public sealed class SpeechVoice
    {
        // ---- 【仮数値】 ----
        public const int SampleRate = 22050;
        public const float SyllableBase = 0.075f;
        public const float SyllablePerLetter = 0.012f;
        public const float WordGap = 0.06f;
        public const float SentenceGap = 0.2f;
        public const float MaxSeconds = 9f;
        public const int PoolSize = 6;
        public const float MinDistance = 2.5f;
        public const float MaxDistance = 24f;

        private readonly List<AudioSource> pool = new List<AudioSource>();
        private readonly List<float> lastUsed = new List<float>();
        private GameObject root;
        private readonly LanguageSystem owner;

        public SpeechVoice(LanguageSystem owner)
        {
            this.owner = owner;
        }

        public void Build(Transform parent)
        {
            Dispose();
            if (parent == null) return;
            root = new GameObject("LanguageVoices");
            root.transform.SetParent(parent, false);
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(root.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = MinDistance;
                src.maxDistance = MaxDistance;
                src.dopplerLevel = 0f;
                pool.Add(src);
                lastUsed.Add(-999f);
            }
        }

        public void Dispose()
        {
            pool.Clear();
            lastUsed.Clear();
            if (root != null) Object.Destroy(root);
            root = null;
        }

        /// <summary>音程（Hz）。話者IDから決定的に。</summary>
        public static float PitchFor(string speakerId, string speakerKind)
        {
            float u = StableHash.Unit(0x5EEDUL, "voice:" + (speakerId ?? "?"));
            float f = 190f + 230f * u;
            var dir = Game.Instance != null ? Game.Instance.Service<IResidentDirectory>() : null;
            var info = dir != null && speakerId != null ? dir.Get(speakerId) : null;
            if (info != null)
            {
                switch (info.Lineage)
                {
                    case AlienLineage.Round: f *= 1.2f; break;
                    case AlienLineage.Mochi: f *= 1.12f; break;
                    case AlienLineage.Plush: f *= 0.95f; break;
                    case AlienLineage.ChibiAnimal: f *= 1.1f; break;
                    case AlienLineage.Floating: f *= 1.25f; break;
                    case AlienLineage.MultiArm: f *= 0.78f; break;
                    case AlienLineage.StarChild: f *= 1.3f; break;
                    case AlienLineage.Lifestyle: f *= 0.92f; break;
                    case AlienLineage.Expressive: f *= 1.05f; break;
                }
            }
            if (speakerKind == "sign" || speakerKind == "letter") f *= 0.9f;
            return f;
        }

        /// <summary>記録の声を鳴らす。position が null なら画面全体（非3D）。</summary>
        public void Play(UtteranceRecord record, Vector3? position, bool mimic)
        {
            if (record == null || record.tokens == null || record.tokens.Count == 0) return;
            if (!LanguageSettings.VoiceEnabled || pool.Count == 0) return;
            if (record.channel == SpeechChannel.Sign || record.channel == SpeechChannel.Letter) return; // 文字は声に出さない
            float pitch = PitchFor(record.speakerId, record.speakerKind) * (mimic ? 1.04f : 1f);
            var clip = Synthesize(record, pitch);
            if (clip == null) return;
            int idx = 0;
            for (int i = 1; i < pool.Count; i++) if (lastUsed[i] < lastUsed[idx]) idx = i;
            var src = pool[idx];
            if (src == null) return;
            lastUsed[idx] = Time.unscaledTime;
            src.Stop();
            if (position.HasValue)
            {
                src.transform.position = position.Value;
                src.spatialBlend = 0.85f;
            }
            else src.spatialBlend = 0f;
            src.volume = LanguageSettings.VoiceVolume * (record.channel == SpeechChannel.Overheard ? 0.75f : 1f);
            src.clip = clip;
            src.Play();
            Object.Destroy(clip, clip.length + 0.5f);
        }

        // ------------------------------------------------------------
        // 合成
        // ------------------------------------------------------------

        private struct Syl
        {
            public string text;
            public float gapBefore;
            public bool sentenceEnd;
            public bool rising;
        }

        private AudioClip Synthesize(UtteranceRecord rec, float pitch)
        {
            var syls = new List<Syl>();
            var spans = SentenceRenderer.SplitSentences(rec.text, rec.tokens);
            if (spans.Count == 0) spans.Add(new SentenceRenderer.SentenceSpan { Start = 0, End = rec.tokens.Count, Punctuation = "." });
            var lang = owner != null ? owner.Language : null;
            for (int s = 0; s < spans.Count; s++)
            {
                bool rising = spans[s].Punctuation != null && spans[s].Punctuation.Contains("?");
                for (int i = spans[s].Start; i < spans[s].End; i++)
                {
                    var tok = rec.tokens[i];
                    string[] parts = null;
                    var lw = lang != null ? lang.Lexicon.WordOfSurface(tok.surface) : null;
                    if (lw != null && lw.Syllables != null && lw.Syllables.Length > 0) parts = lw.Syllables;
                    if (parts == null) parts = Phonology.SplitSyllables(tok.surface).ToArray();
                    for (int k = 0; k < parts.Length; k++)
                    {
                        float gap = 0f;
                        if (k == 0 && syls.Count > 0) gap = i == spans[s].Start ? SentenceGap : tok.attachedToPrevious ? 0f : WordGap;
                        syls.Add(new Syl { text = parts[k].ToLowerInvariant(), gapBefore = gap });
                    }
                }
                if (syls.Count > 0)
                {
                    var last = syls[syls.Count - 1];
                    last.sentenceEnd = true;
                    last.rising = rising;
                    syls[syls.Count - 1] = last;
                }
            }
            if (syls.Count == 0) return null;

            // 長さを見積もる
            float total = 0.05f;
            foreach (var sy in syls) total += sy.gapBefore + SylDuration(sy.text);
            total = Mathf.Min(total, MaxSeconds);
            int n = Mathf.Max(1, (int)(total * SampleRate));
            var data = new float[n];
            uint noise = (uint)(StableHash.Fnv1a64(rec.recordId ?? rec.text ?? "x") & 0xFFFFFFFF) | 1u;
            int cursor = (int)(0.02f * SampleRate);
            float declination = 1f;
            foreach (var sy in syls)
            {
                cursor += (int)(sy.gapBefore * SampleRate);
                if (sy.gapBefore >= SentenceGap) declination = 1f;
                float dur = SylDuration(sy.text);
                int len = (int)(dur * SampleRate);
                if (cursor + len >= n) break;
                RenderSyllable(data, cursor, len, sy, pitch * declination, ref noise);
                cursor += len;
                declination = Mathf.Max(0.86f, declination - 0.012f);
            }
            var clip = AudioClip.Create("speech", n, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float SylDuration(string s)
        {
            float d = SyllableBase + SyllablePerLetter * s.Length;
            return Mathf.Min(d, 0.22f);
        }

        private static bool IsVowel(char c) => c == 'a' || c == 'i' || c == 'u' || c == 'e' || c == 'o';

        private static void RenderSyllable(float[] data, int start, int len, Syl sy, float f0, ref uint noise)
        {
            string s = sy.text;
            int vi = -1;
            for (int i = 0; i < s.Length; i++) if (IsVowel(s[i])) { vi = i; break; }
            char vowel = vi >= 0 ? s[vi] : 'a';
            string onset = vi > 0 ? s.Substring(0, vi) : "";
            int vEnd = vi;
            while (vEnd >= 0 && vEnd < s.Length && IsVowel(s[vEnd])) vEnd++;
            string coda = vEnd >= 0 && vEnd < s.Length ? s.Substring(vEnd) : "";

            // 母音の音程（母音で少し変える）と倍音の重み（簡易的な音色の違い）
            float vf, h2, h3;
            switch (vowel)
            {
                case 'i': vf = 1.18f; h2 = 0.2f; h3 = 0.45f; break;
                case 'u': vf = 0.9f; h2 = 0.5f; h3 = 0.1f; break;
                case 'e': vf = 1.1f; h2 = 0.35f; h3 = 0.4f; break;
                case 'o': vf = 0.95f; h2 = 0.65f; h3 = 0.2f; break;
                default: vf = 1.0f; h2 = 0.55f; h3 = 0.35f; break;
            }
            int onsetLen = 0;
            float onsetNoise = 0f, onsetTone = 0f;
            if (onset.Length > 0)
            {
                char c = onset[0];
                if ("ptk".IndexOf(c) >= 0) { onsetLen = (int)(0.012f * SampleRate); onsetNoise = 0.4f; }
                else if ("bdg".IndexOf(c) >= 0) { onsetLen = (int)(0.014f * SampleRate); onsetNoise = 0.2f; onsetTone = 0.25f; }
                else if ("sfh".IndexOf(c) >= 0) { onsetLen = (int)(0.04f * SampleRate); onsetNoise = c == 'h' ? 0.12f : 0.22f; }
                else if ("zvj".IndexOf(c) >= 0) { onsetLen = (int)(0.035f * SampleRate); onsetNoise = 0.15f; onsetTone = 0.25f; }
                else { onsetLen = (int)(0.025f * SampleRate); onsetTone = 0.35f; } // m n r l w y
                if (onset.Length > 1) onsetLen += (int)(0.015f * SampleRate);
                onsetLen = Mathf.Min(onsetLen, len / 2);
            }
            int codaLen = coda.Length > 0 ? Mathf.Min((int)(0.03f * SampleRate), len / 3) : 0;
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)len;
                float f = f0 * vf;
                if (sy.sentenceEnd) f *= sy.rising ? 1f + 0.35f * t : 1f - 0.18f * t;
                phase += 2f * Mathf.PI * f / SampleRate;
                float tone = Mathf.Sin(phase) + h2 * Mathf.Sin(2f * phase) + h3 * Mathf.Sin(3f * phase);
                tone *= 0.32f;
                float sample;
                if (i < onsetLen)
                {
                    noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                    float nz = (noise / (float)uint.MaxValue) * 2f - 1f;
                    sample = nz * onsetNoise + tone * onsetTone;
                }
                else if (i >= len - codaLen)
                {
                    sample = tone * 0.35f;
                }
                else sample = tone;
                // 包絡線
                float env = Mathf.Min(1f, i / (0.008f * SampleRate)) * Mathf.Min(1f, (len - i) / (0.025f * SampleRate));
                int idx = start + i;
                if (idx >= 0 && idx < data.Length) data[idx] += sample * env * 0.6f;
            }
        }
    }
}
