using System;
using System.Collections.Generic;
using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 星ごとの音韻（宇宙語仕様 §3.3「発音規則」・§3.6「音のまとまり」）。
    /// 子音・母音の目録、音節構造（V / CV / CVC / CCV）、単語長の傾向、音の偏り（濁音が多い等）を持ち、
    /// 「同じ星の言葉」として聞こえる統一感を作る。綴り（発音表記）は小文字ASCIIの1文字＝1音素で統一し、
    /// 辞書への記録・接辞の分割・宇宙文字への写しが一貫するようにする（タブレット仕様 §3.2）。
    /// </summary>
    public sealed class Phonology
    {
        // ---- 【仮数値】調整用テーブル ----
        /// <summary>子音の候補（1文字＝1音素）。c・q・x は読みが紛らわしいので使わない。</summary>
        public static readonly string[] MasterConsonants = { "p", "t", "k", "b", "d", "g", "m", "n", "s", "z", "h", "f", "v", "r", "l", "w", "y", "j" };
        public static readonly string[] MasterVowels = { "a", "i", "u", "e", "o" };
        public static readonly string[] VoicedObstruents = { "b", "d", "g", "z", "v", "j" };
        public static readonly string[] Stops = { "p", "t", "k", "b", "d", "g" };
        public static readonly string[] Sonorants = { "m", "n", "l", "r", "w", "y" };
        public static readonly string[] CodaCandidates = { "n", "m", "r", "l", "s", "k", "t" };

        /// <summary>音の傾向（§3.6 の「濁音が多めの星」「短い単語が多い星」など）。</summary>
        public enum SoundFlavor { Balanced, Voiced, Soft, Crisp }

        public SoundFlavor Flavor;
        public readonly List<string> Consonants = new List<string>();
        public readonly List<float> ConsonantWeights = new List<float>();
        public readonly List<string> Vowels = new List<string>();
        public readonly List<float> VowelWeights = new List<float>();
        public readonly List<string> Codas = new List<string>();
        public readonly List<float> CodaWeights = new List<float>();
        /// <summary>CCV の2番目の子音（r / l）。</summary>
        public readonly List<string> ClusterSeconds = new List<string>();

        // 音節構造の重み
        public float WeightV, WeightCV = 1f, WeightCVC, WeightCCV;
        /// <summary>内容語の平均音節数（単語長の傾向）。</summary>
        public float MeanSyllables;
        public float LongVowelChance;

        // 品詞ごとの形の傾向（推理の手掛かりになる規則性。§2.2 例外だらけにしない）
        public string VerbFinalVowel;
        public float VerbFinalChance;
        public string AdjectiveFinalVowel;
        public float AdjectiveFinalChance;
        public readonly List<string> NameEndings = new List<string>();
        public float NameEndingChance;
        public float InterjectionReduplicationChance;

        public bool HasConsonant(string c) => Consonants.Contains(c);
        public bool IsVowel(char c) => c == 'a' || c == 'i' || c == 'u' || c == 'e' || c == 'o';

        // ------------------------------------------------------------
        // 生成（言語生成バージョン1）
        // ------------------------------------------------------------

        public static Phonology GenerateV1(SeededRandom rng)
        {
            var p = new Phonology();
            float f = rng.NextFloat();
            p.Flavor = f < 0.40f ? SoundFlavor.Balanced : f < 0.62f ? SoundFlavor.Voiced : f < 0.82f ? SoundFlavor.Soft : SoundFlavor.Crisp;

            // 子音目録：9〜15個。m/n・破裂音2つ以上・流音1つ以上を保証する。
            int targetCount = rng.Range(9, 16);
            var pool = new List<string>(MasterConsonants);
            rng.Shuffle(pool);
            var chosen = new List<string>();
            void Take(string c) { if (!chosen.Contains(c)) chosen.Add(c); }
            Take(rng.Chance(0.5f) ? "m" : "n");
            if (rng.Chance(0.85f)) Take(chosen.Contains("m") ? "n" : "m");
            Take(rng.Chance(0.5f) ? "r" : "l");
            var stopPool = new List<string>(p.Flavor == SoundFlavor.Voiced ? new[] { "b", "d", "g" } : p.Flavor == SoundFlavor.Soft ? new[] { "p", "t", "b", "d" } : new[] { "p", "t", "k" });
            rng.Shuffle(stopPool);
            Take(stopPool[0]);
            Take(stopPool[1]);
            if (p.Flavor == SoundFlavor.Voiced) { Take("z"); if (rng.Chance(0.6f)) Take("g"); }
            if (p.Flavor == SoundFlavor.Crisp) { Take("k"); Take("s"); }
            if (p.Flavor == SoundFlavor.Soft) { Take("w"); if (rng.Chance(0.7f)) Take("y"); }
            foreach (var c in pool)
            {
                if (chosen.Count >= targetCount) break;
                if (p.Flavor == SoundFlavor.Soft && (c == "g" || c == "k" || c == "j" || c == "z") && rng.Chance(0.7f)) continue;
                if (p.Flavor == SoundFlavor.Crisp && (c == "j" || c == "v" || c == "z") && rng.Chance(0.6f)) continue;
                Take(c);
            }
            // 並びを安定させる（目録の順は表示用）
            chosen.Sort((a, b) => Array.IndexOf(MasterConsonants, a).CompareTo(Array.IndexOf(MasterConsonants, b)));
            foreach (var c in chosen)
            {
                p.Consonants.Add(c);
                // Zipf 風に偏った重み → その星らしい「よく出る音」ができる
                float w = 0.35f + rng.NextFloat() * 1.4f;
                if (p.Flavor == SoundFlavor.Voiced && Array.IndexOf(VoicedObstruents, c) >= 0) w *= 2.2f;
                if (p.Flavor == SoundFlavor.Soft && Array.IndexOf(Sonorants, c) >= 0) w *= 1.8f;
                if (p.Flavor == SoundFlavor.Crisp && (c == "k" || c == "t" || c == "p" || c == "s")) w *= 1.8f;
                if (c == "h" || c == "w" || c == "y" || c == "j" || c == "v" || c == "f") w *= 0.6f; // 聞き分けにくい音は控えめ
                p.ConsonantWeights.Add(w);
            }

            // 母音：3〜5個
            float vf = rng.NextFloat();
            p.Vowels.AddRange(new[] { "a", "i", "u" });
            if (vf >= 0.15f) p.Vowels.Add(rng.Chance(0.5f) ? "e" : "o");
            if (vf >= 0.40f) { if (!p.Vowels.Contains("e")) p.Vowels.Add("e"); if (!p.Vowels.Contains("o")) p.Vowels.Add("o"); }
            p.Vowels.Sort((a, b) => Array.IndexOf(MasterVowels, a).CompareTo(Array.IndexOf(MasterVowels, b)));
            foreach (var v in p.Vowels) p.VowelWeights.Add(0.6f + rng.NextFloat());

            // 音節構造
            p.WeightCV = 1f;
            p.WeightV = rng.Chance(0.6f) ? rng.Range(0.12f, 0.3f) : 0f;
            p.WeightCVC = rng.Chance(0.6f) ? rng.Range(0.18f, 0.55f) : 0f;
            bool hasLiquid = p.HasConsonant("r") || p.HasConsonant("l");
            p.WeightCCV = hasLiquid && rng.Chance(0.3f) ? rng.Range(0.08f, 0.22f) : 0f;
            if (p.HasConsonant("r")) p.ClusterSeconds.Add("r");
            if (p.HasConsonant("l")) p.ClusterSeconds.Add("l");

            if (p.WeightCVC > 0f)
            {
                var codaPool = new List<string>();
                foreach (var c in CodaCandidates) if (p.HasConsonant(c)) codaPool.Add(c);
                rng.Shuffle(codaPool);
                int n = Math.Min(codaPool.Count, rng.Range(1, 4));
                if (p.HasConsonant("n") && !codaPool.GetRange(0, n).Contains("n")) { p.Codas.Add("n"); p.CodaWeights.Add(1.4f); n--; }
                for (int i = 0; i < n; i++)
                {
                    if (p.Codas.Contains(codaPool[i])) continue;
                    p.Codas.Add(codaPool[i]);
                    p.CodaWeights.Add(0.5f + rng.NextFloat());
                }
                if (p.Codas.Count == 0) p.WeightCVC = 0f;
            }

            // 単語長の傾向：閉音節が多い星は短め
            p.MeanSyllables = rng.Range(1.7f, 2.6f) - p.WeightCVC * 0.6f;
            p.LongVowelChance = rng.Chance(0.35f) ? rng.Range(0.04f, 0.12f) : 0f;

            // 品詞の形の傾向
            p.VerbFinalVowel = rng.Pick(p.Vowels);
            p.VerbFinalChance = rng.Chance(0.6f) ? rng.Range(0.45f, 0.7f) : 0f;
            var adjVowels = new List<string>(p.Vowels);
            adjVowels.Remove(p.VerbFinalVowel);
            p.AdjectiveFinalVowel = rng.Pick(adjVowels);
            p.AdjectiveFinalChance = rng.Chance(0.5f) ? rng.Range(0.35f, 0.6f) : 0f;
            p.NameEndingChance = rng.Range(0.35f, 0.65f);
            for (int i = 0; i < 3; i++)
            {
                string end;
                int guard = 0;
                do { end = p.MakeSyllable(rng, false, i == 0 && p.WeightCVC > 0f ? 1 : 0); }
                while (p.NameEndings.Contains(end) && ++guard < 10);
                p.NameEndings.Add(end);
            }
            p.InterjectionReduplicationChance = rng.Range(0.15f, 0.45f);
            return p;
        }

        // ------------------------------------------------------------
        // 音節・単語の生成
        // ------------------------------------------------------------

        /// <summary>音節を1つ作る。forceShape: -1=重みで選ぶ, 0=CV, 1=CVC。</summary>
        public string MakeSyllable(SeededRandom rng, bool allowV, int forceShape = -1, string forceVowel = null, bool forbidCoda = false)
        {
            int shape;
            if (forceShape >= 0) shape = forceShape == 1 && WeightCVC > 0f && !forbidCoda ? 2 : 1;
            else
            {
                var w = new[] { allowV ? WeightV : 0f, WeightCV, forbidCoda ? 0f : WeightCVC, WeightCCV };
                shape = rng.WeightedIndex(w);
                if (shape < 0) shape = 1;
            }
            var sb = new StringBuilder(4);
            string vowel = forceVowel ?? PickVowel(rng);
            switch (shape)
            {
                case 0: // V
                    sb.Append(vowel);
                    break;
                case 3: // CCV（破裂音＋流音）
                {
                    var stops = new List<string>();
                    foreach (var c in Consonants) if (Array.IndexOf(Stops, c) >= 0) stops.Add(c);
                    if (stops.Count == 0 || ClusterSeconds.Count == 0) goto default;
                    sb.Append(rng.Pick(stops)).Append(rng.Pick(ClusterSeconds));
                    sb.Append(vowel);
                    break;
                }
                default: // CV / CVC
                {
                    string onset = PickConsonant(rng);
                    // 聞き取りにくい組み合わせ（yi, wu）を避ける
                    int guard = 0;
                    while (((onset == "y" && vowel == "i") || (onset == "w" && vowel == "u")) && guard++ < 6)
                        onset = PickConsonant(rng);
                    if ((onset == "y" && vowel == "i") || (onset == "w" && vowel == "u")) onset = Consonants[0];
                    sb.Append(onset).Append(vowel);
                    if (shape == 2 && Codas.Count > 0) sb.Append(Codas[rng.WeightedIndex(CodaWeights)]);
                    break;
                }
            }
            // 長母音（母音を重ねる）
            if (LongVowelChance > 0f && shape != 2 && rng.Chance(LongVowelChance)) sb.Append(vowel);
            return sb.ToString();
        }

        public string PickConsonant(SeededRandom rng) => Consonants[Math.Max(0, rng.WeightedIndex(ConsonantWeights))];
        public string PickVowel(SeededRandom rng) => Vowels[Math.Max(0, rng.WeightedIndex(VowelWeights))];

        /// <summary>語の形の種類（音節数・形の傾向の決定に使う）。</summary>
        public enum Shape { Grammatical, Numeral, Pronoun, Interjection, QuestionWord, Content, Verb, Adjective, Name, Proper }

        /// <summary>
        /// 1語を作る。attempt は衝突回避の再試行回数（増えるほど長くなりやすい）。
        /// 同じ (rng の状態, shape, attempt) からは必ず同じ語ができる。
        /// </summary>
        public string[] MakeWord(SeededRandom rng, Shape shape, int attempt)
        {
            int count;
            switch (shape)
            {
                case Shape.Grammatical: count = 1; break;
                case Shape.Numeral: count = rng.Chance(0.75f) ? 1 : 2; break;
                case Shape.Pronoun:
                case Shape.QuestionWord: count = rng.Chance(0.55f) ? 1 : 2; break;
                case Shape.Interjection: count = rng.Chance(0.5f) ? 1 : 2; break;
                case Shape.Name: count = rng.Chance(0.6f) ? 2 : 3; break;
                case Shape.Proper: count = rng.Chance(0.5f) ? 2 : 3; break;
                default:
                {
                    // 平均 MeanSyllables 付近（1〜4）
                    float g = (rng.NextFloat() + rng.NextFloat() + rng.NextFloat() - 1.5f) * 1.2f;
                    count = (int)Math.Round(MeanSyllables + g);
                    if (count < 1) count = 1;
                    if (count > 4) count = 4;
                    break;
                }
            }
            // 衝突が続く場合は1音節ずつ長くする（短い語の空間は狭いため）
            count += attempt / 5;

            var syl = new List<string>(count + 1);
            bool redup = shape == Shape.Interjection && rng.Chance(InterjectionReduplicationChance);
            for (int i = 0; i < count; i++)
            {
                bool last = i == count - 1;
                string forceVowel = null;
                bool forbidCoda = false;
                if (last && shape == Shape.Verb && VerbFinalChance > 0f && rng.Chance(VerbFinalChance)) { forceVowel = VerbFinalVowel; forbidCoda = true; }
                if (last && shape == Shape.Adjective && AdjectiveFinalChance > 0f && rng.Chance(AdjectiveFinalChance)) { forceVowel = AdjectiveFinalVowel; forbidCoda = true; }
                string s = MakeSyllable(rng, i == 0, -1, forceVowel, forbidCoda);
                // 同じ音節の連続は避ける（重複はその用途の文法・間投詞に取っておく）
                int guard = 0;
                while (i > 0 && s == syl[i - 1] && guard++ < 5) s = MakeSyllable(rng, false, -1, forceVowel, forbidCoda);
                // 母音で終わった後に母音で始まる音節は作らない（V は語頭のみ）
                syl.Add(s);
            }
            if (shape == Shape.Name && NameEndings.Count > 0 && rng.Chance(NameEndingChance))
            {
                string end = rng.Pick(NameEndings);
                if (syl.Count >= 2) syl[syl.Count - 1] = end;
                else syl.Add(end);
            }
            if (redup) syl.Add(syl[syl.Count - 1]);
            return syl.ToArray();
        }

        /// <summary>綴りを音節に分ける（記録の再生・声用。生成した語でなくても使える簡易版）。</summary>
        public static List<string> SplitSyllables(string surface)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(surface)) return result;
            string s = surface.ToLowerInvariant();
            var cur = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (!char.IsLetter(c)) continue;
                cur.Append(c);
                bool isV = "aiueo".IndexOf(c) >= 0;
                if (isV)
                {
                    // 母音の後：次が子音＋子音（または末尾の子音）なら子音を含めて閉じる
                    int j = i + 1;
                    if (j < s.Length && "aiueo".IndexOf(s[j]) < 0 && char.IsLetter(s[j]))
                    {
                        bool nextIsLast = j == s.Length - 1;
                        bool nextNextCons = j + 1 < s.Length && "aiueo".IndexOf(s[j + 1]) < 0;
                        if (nextIsLast || (nextNextCons && !(s[j + 1] == 'r' || s[j + 1] == 'l')))
                        {
                            cur.Append(s[j]);
                            i = j;
                        }
                    }
                    result.Add(cur.ToString());
                    cur.Length = 0;
                }
            }
            if (cur.Length > 0)
            {
                if (result.Count > 0) result[result.Count - 1] += cur.ToString();
                else result.Add(cur.ToString());
            }
            return result;
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("音の傾向: ").Append(Flavor).Append('\n');
            sb.Append("子音: ").Append(string.Join(" ", Consonants)).Append('\n');
            sb.Append("母音: ").Append(string.Join(" ", Vowels)).Append('\n');
            sb.Append($"音節: CV=1 V={WeightV:0.00} CVC={WeightCVC:0.00} CCV={WeightCCV:0.00}  語末子音: {string.Join(" ", Codas)}\n");
            sb.Append($"平均音節数: {MeanSyllables:0.0}  長母音: {LongVowelChance:0.00}\n");
            sb.Append($"動詞の語末: -{VerbFinalVowel} ({VerbFinalChance:0.00})  形容詞の語末: -{AdjectiveFinalVowel} ({AdjectiveFinalChance:0.00})\n");
            sb.Append($"名前の語尾: {string.Join(" / ", NameEndings)} ({NameEndingChance:0.00})");
            return sb.ToString();
        }
    }
}
