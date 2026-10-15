using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>文法標識（付属語・接辞）の内部ID。SurfaceToken.internalId に入る（表示禁止）。</summary>
    public static class GramIds
    {
        public const string Prefix = "GRAM:";
        public const string NEG = "GRAM:NEG";
        public const string QUESTION = "GRAM:Q";
        public const string PAST = "GRAM:PAST";
        public const string FUTURE = "GRAM:FUT";
        public const string DESIRE = "GRAM:DESIRE";
        public const string REQUEST = "GRAM:REQUEST";
        public const string INVITE = "GRAM:INVITE";
        public const string OFFER = "GRAM:OFFER";
        public const string AGENT = "GRAM:AGENT";
        public const string PATIENT = "GRAM:PATIENT";
        public const string RECIPIENT = "GRAM:RECIPIENT";
        public const string LOCATIVE = "GRAM:LOC";
        public const string POSSESSIVE = "GRAM:POSS";
        public const string PLURAL = "GRAM:PLURAL";
        public const string BECAUSE = "GRAM:BECAUSE";
        public const string COPULA = "GRAM:COPULA";
        public const string NUM_AND = "GRAM:NUM_AND";

        /// <summary>生成順（この順で語形を決めるので、並びを変えると既存の星の言葉が変わる。追加は末尾に）。</summary>
        public static readonly string[] All =
        {
            NEG, QUESTION, PAST, FUTURE, DESIRE, REQUEST, INVITE, OFFER, AGENT, PATIENT, RECIPIENT, LOCATIVE,
            POSSESSIVE, PLURAL, BECAUSE, COPULA, NUM_AND
        };
    }

    /// <summary>語彙の1語。</summary>
    public sealed class LexWord
    {
        public string Concept;
        /// <summary>表示形（名前は先頭大文字）。</summary>
        public string Surface;
        public string[] Syllables;
        public bool IsBase;
        public int Attempt;
        public bool IsName;
        public string Key => Surface.ToLowerInvariant();
    }

    /// <summary>
    /// 語彙（宇宙語仕様 §4）。概念IDごとに星のシードから決定的に語を作る。
    /// ・基本語彙（コアの Concepts 定数・文法標識・数詞）は星の準備時に固定順で一括生成する。
    /// ・"ITEM:" "NAME:" "PLACE:" "SPECIES:" やモジュール独自概念などは初めて使われた時に生成する（必ず語ができる）。
    /// ・偶然の衝突（同じ綴り）は検出して回避する（§4.2：偶然の衝突と意図した多義性を分ける）。
    /// </summary>
    public sealed class Lexicon
    {
        /// <summary>【仮数値】短い基本語で「1文字違い」を避ける長さの上限と、その回避を試す回数。</summary>
        public const int ConfusableMaxLength = 4;
        public const int ConfusableAttempts = 4;
        public const int MaxAttempts = 60;

        private readonly ulong seed;
        private readonly Phonology phonology;
        private readonly Dictionary<string, LexWord> byConcept = new Dictionary<string, LexWord>();
        private readonly Dictionary<string, LexWord> byKey = new Dictionary<string, LexWord>();
        private readonly List<LexWord> baseWords = new List<LexWord>();

        /// <summary>動的語どうしの衝突で再試行した語（ホストが星の状態として保存・同期し、全員で同じ語にする）。</summary>
        public readonly List<StringStringPair> Bumps = new List<StringStringPair>();
        /// <summary>検出した衝突の件数（検証用）。</summary>
        public int CollisionCount { get; private set; }
        public int ConfusableRejectCount { get; private set; }

        public Lexicon(ulong languageSeed, Phonology phonology)
        {
            seed = languageSeed;
            this.phonology = phonology;
        }

        public IReadOnlyList<LexWord> BaseWords => baseWords;
        public IEnumerable<LexWord> AllWords => byConcept.Values;
        public int Count => byConcept.Count;

        /// <summary>コアの Concepts に定数として定義された概念ID（モジュールの追加登録に影響されない固定集合）。</summary>
        public static List<string> CoreConceptIds()
        {
            var list = new List<string>();
            foreach (var f in typeof(Concepts).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                var v = f.GetRawConstantValue() as string;
                if (string.IsNullOrEmpty(v) || v.EndsWith(":")) continue; // 接頭辞定数は除く
                if (!list.Contains(v)) list.Add(v);
            }
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>基本語彙を固定順で生成する。</summary>
        public void BuildBase(IList<string> numberMorphemes)
        {
            foreach (var g in GramIds.All) AddBase(g);
            foreach (var n in numberMorphemes) AddBase(n);
            foreach (var c in CoreConceptIds()) AddBase(c);
        }

        private void AddBase(string concept)
        {
            if (byConcept.ContainsKey(concept)) return;
            var w = Generate(concept, true);
            baseWords.Add(w);
        }

        public bool TryGet(string concept, out LexWord word) => byConcept.TryGetValue(concept ?? string.Empty, out word);

        /// <summary>概念の語（無ければ生成）。</summary>
        public LexWord Get(string concept)
        {
            if (string.IsNullOrEmpty(concept)) concept = Concepts.THING;
            if (byConcept.TryGetValue(concept, out var w)) return w;
            return Generate(concept, false);
        }

        public string ConceptOfSurface(string surface)
        {
            if (string.IsNullOrEmpty(surface)) return null;
            return byKey.TryGetValue(surface.ToLowerInvariant(), out var w) ? w.Concept : null;
        }

        public LexWord WordOfSurface(string surface)
        {
            if (string.IsNullOrEmpty(surface)) return null;
            return byKey.TryGetValue(surface.ToLowerInvariant(), out var w) ? w : null;
        }

        public static Phonology.Shape ShapeOf(string concept)
        {
            if (concept.StartsWith(GramIds.Prefix)) return Phonology.Shape.Grammatical;
            if (concept.StartsWith(NumberSystem.Prefix)) return Phonology.Shape.Numeral;
            if (concept.StartsWith(Concepts.NamePrefix)) return Phonology.Shape.Name;
            if (concept.StartsWith(Concepts.PlacePrefix) || concept.StartsWith(Concepts.ItemPrefix) || concept.StartsWith(Concepts.SpeciesPrefix))
                return Phonology.Shape.Proper;
            switch (Concepts.KindOf(concept))
            {
                case ConceptKind.Pronoun: return Phonology.Shape.Pronoun;
                case ConceptKind.Interjection: return Phonology.Shape.Interjection;
                case ConceptKind.QuestionWord: return Phonology.Shape.QuestionWord;
                case ConceptKind.Verb: return Phonology.Shape.Verb;
                case ConceptKind.Adjective:
                case ConceptKind.Color: return Phonology.Shape.Adjective;
                case ConceptKind.Name: return Phonology.Shape.Name;
                default: return Phonology.Shape.Content;
            }
        }

        private LexWord Generate(string concept, bool isBase)
        {
            var shape = ShapeOf(concept);
            bool isName = shape == Phonology.Shape.Name;
            bool bumpedByDynamic = false;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var rng = new SeededRandom(StableHash.Derive(seed, "w:" + concept, attempt));
                var syl = phonology.MakeWord(rng, shape, attempt);
                string key = string.Concat(syl);
                if (byKey.TryGetValue(key, out var owner))
                {
                    CollisionCount++;
                    if (!owner.IsBase && !isBase) bumpedByDynamic = true;
                    continue;
                }
                if (isBase && attempt < ConfusableAttempts && key.Length <= ConfusableMaxLength && IsConfusable(key))
                {
                    ConfusableRejectCount++;
                    continue;
                }
                var w = new LexWord
                {
                    Concept = concept,
                    Syllables = syl,
                    Surface = isName ? Capitalize(key) : key,
                    IsBase = isBase,
                    Attempt = attempt,
                    IsName = isName
                };
                Register(w);
                if (bumpedByDynamic) Bumps.Add(new StringStringPair(concept, w.Surface));
                return w;
            }
            // ここに来ることは事実上無いが、必ず語を返す
            var fallback = new LexWord { Concept = concept, Syllables = new[] { "zu", StableHash.Derive(seed, concept).ToString("x4").Substring(0, 4) }, IsBase = isBase, IsName = isName };
            fallback.Surface = string.Concat(fallback.Syllables);
            Register(fallback);
            return fallback;
        }

        private void Register(LexWord w)
        {
            byConcept[w.Concept] = w;
            byKey[w.Key] = w;
        }

        /// <summary>短い基本語どうしが1文字違いで聞き分けにくくならないか（§3.6）。</summary>
        private bool IsConfusable(string key)
        {
            foreach (var b in baseWords)
            {
                var k = b.Key;
                if (k.Length != key.Length) continue;
                int diff = 0;
                for (int i = 0; i < k.Length && diff < 2; i++) if (k[i] != key[i]) diff++;
                if (diff == 1) return true;
            }
            return false;
        }

        /// <summary>ホストが記録した再試行語を適用する（ゲスト・ロード時）。</summary>
        public void ApplyBumps(List<StringStringPair> bumps)
        {
            if (bumps == null) return;
            foreach (var p in bumps)
            {
                if (p == null || string.IsNullOrEmpty(p.key) || string.IsNullOrEmpty(p.value)) continue;
                string key = p.value.ToLowerInvariant();
                if (byConcept.TryGetValue(p.key, out var existing))
                {
                    if (existing.Key == key) continue;
                    if (existing.IsBase) continue;
                    byKey.Remove(existing.Key);
                    byConcept.Remove(p.key);
                }
                if (byKey.ContainsKey(key)) continue;
                var w = new LexWord
                {
                    Concept = p.key,
                    Surface = p.value,
                    Syllables = Phonology.SplitSyllables(key).ToArray(),
                    IsBase = false,
                    Attempt = -1,
                    IsName = p.key.StartsWith(Concepts.NamePrefix)
                };
                Register(w);
                bool found = false;
                foreach (var b in Bumps) if (b.key == p.key) { b.value = p.value; found = true; }
                if (!found) Bumps.Add(new StringStringPair(p.key, p.value));
            }
        }

        public static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        /// <summary>基本語彙の指紋（生成アルゴリズムが変わっていないかの確認用）。</summary>
        public string BaseFingerprint()
        {
            var sb = new StringBuilder();
            foreach (var w in baseWords) sb.Append(w.Concept).Append('=').Append(w.Surface).Append(';');
            return StableHash.Fnv1a64(sb.ToString()).ToString("x16");
        }
    }
}
