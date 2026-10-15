using System.Collections.Generic;
using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 意味データ（MeaningFrame）を、その星の文法で宇宙語の語の列へ変換する（宇宙語仕様 §3.1, §14.1, §14.4）。
    /// 日本語の文を単語置換する方式ではなく、意味の役割（誰が・何を・誰に・どこで・いつ・否定・時制・発話行為）から
    /// 星の規則（語順・格・否定・疑問・時制・希望/依頼・所有・複数・数詞）で組み立てる。
    /// 同じ意味からは必ず同じ文ができ（§6.2 比較で文法を推理できる）、規則に例外を作らない（§2.2）。
    /// </summary>
    public sealed class SentenceRenderer
    {
        private readonly LanguageDefinition lang;
        private GrammarParams G => lang.Grammar;

        public SentenceRenderer(LanguageDefinition language)
        {
            lang = language;
        }

        // ------------------------------------------------------------
        // 公開
        // ------------------------------------------------------------

        public RenderedUtterance Render(Utterance u)
        {
            var r = new RenderedUtterance();
            var sb = new StringBuilder();
            if (u?.sentences != null)
            {
                foreach (var f in u.sentences)
                {
                    if (f == null) continue;
                    var toks = RenderSentence(f);
                    if (toks.Count == 0) continue;
                    if (sb.Length > 0) sb.Append(' ');
                    r.sentenceStarts.Add(r.tokens.Count);
                    AppendText(sb, toks);
                    sb.Append(PunctuationOf(f.act));
                    r.tokens.AddRange(toks);
                }
            }
            r.text = sb.ToString();
            return r;
        }

        /// <summary>1文だけを変換（デバッグ・検証用）。</summary>
        public RenderedUtterance RenderFrame(MeaningFrame f)
        {
            var u = new Utterance("debug");
            u.Say(f);
            return Render(u);
        }

        public List<SurfaceToken> NumberTokens(long n)
        {
            var list = new List<SurfaceToken>();
            foreach (var m in lang.Numbers.Compose(n))
                list.Add(Tok(lang.Word(m), m, m.StartsWith(GramIds.Prefix), false));
            return list;
        }

        public string RenderNumber(long n)
        {
            var sb = new StringBuilder();
            AppendText(sb, NumberTokens(n));
            return sb.ToString();
        }

        public static string PunctuationOf(SpeechAct act)
        {
            switch (act)
            {
                case SpeechAct.Question:
                case SpeechAct.WhQuestion: return "?";
                case SpeechAct.Exclamation:
                case SpeechAct.Greeting:
                case SpeechAct.Farewell:
                case SpeechAct.Thanks:
                case SpeechAct.Invitation: return "!";
                default: return ".";
            }
        }

        private static void AppendText(StringBuilder sb, List<SurfaceToken> toks)
        {
            for (int i = 0; i < toks.Count; i++)
            {
                if (i > 0 && !toks[i].attachedToPrevious) sb.Append(' ');
                sb.Append(toks[i].surface);
            }
        }

        // ------------------------------------------------------------
        // 文
        // ------------------------------------------------------------

        /// <summary>MeaningFrame / MeaningFrameLite を同じように扱うための内部ビュー。</summary>
        private sealed class ClauseData
        {
            public SpeechAct act;
            public string predicate;
            public MeaningArg agent, patient, recipient, location;
            public string timeWord;
            public List<string> adverbs = new List<string>();
            public Tense tense;
            public bool negative;
        }

        private static bool Has(MeaningArg a) => a != null && !string.IsNullOrEmpty(a.concept);
        private static bool Has(string s) => !string.IsNullOrEmpty(s);

        private static ClauseData View(MeaningFrame f)
        {
            var c = new ClauseData
            {
                act = f.act,
                predicate = f.predicate,
                agent = Has(f.agent) ? f.agent : null,
                patient = Has(f.patient) ? f.patient : null,
                recipient = Has(f.recipient) ? f.recipient : null,
                location = Has(f.location) ? f.location : null,
                timeWord = Has(f.timeWord) ? f.timeWord : null,
                tense = f.tense,
                negative = f.negative
            };
            if (f.adverbs != null) foreach (var a in f.adverbs) if (Has(a)) c.adverbs.Add(a);
            return c;
        }

        private static ClauseData View(MeaningFrameLite f)
        {
            return new ClauseData
            {
                act = SpeechAct.Statement,
                predicate = f.predicate,
                agent = Has(f.agent) ? f.agent : null,
                patient = Has(f.patient) ? f.patient : null,
                timeWord = Has(f.timeWord) ? f.timeWord : null,
                tense = f.tense,
                negative = f.negative
            };
        }

        private List<SurfaceToken> RenderSentence(MeaningFrame f)
        {
            var c = View(f);
            List<SurfaceToken> main;
            switch (c.act)
            {
                case SpeechAct.Greeting:
                case SpeechAct.Farewell:
                case SpeechAct.Thanks:
                case SpeechAct.Apology:
                    main = Formula(c);
                    break;
                case SpeechAct.Offer:
                    main = OfferSentence(c);
                    break;
                default:
                    main = ContentSentence(c);
                    break;
            }
            // 理由節（「〜だから」）
            if (f.reason != null && Has(f.reason.predicate) && main.Count > 0)
            {
                var reason = Clause(View(f.reason));
                if (reason.Count > 0)
                {
                    var because = Gram(GramIds.BECAUSE);
                    if (G.MarkersAfter) reason.Add(because);
                    else reason.Insert(0, because);
                    if (G.ReasonClauseFirst) { reason.AddRange(main); main = reason; }
                    else main.AddRange(reason);
                }
            }
            if (main.Count > 0) main[0].attachedToPrevious = false;
            return main;
        }

        private List<SurfaceToken> ContentSentence(ClauseData c)
        {
            var result = new List<SurfaceToken>();
            if (!Has(c.predicate))
            {
                // 述語なし：物を示すだけの発話（「魚。」「魚？」）
                if (c.patient != null) result.AddRange(NP(c.patient));
                else if (c.agent != null) result.AddRange(NP(c.agent));
                else if (c.location != null) result.AddRange(NP(c.location));
                if (result.Count > 0 && c.act == SpeechAct.Question) AddEdge(result, Gram(GramIds.QUESTION), G.Question == QuestionStyle.ParticleInitial);
                if (result.Count == 0 && c.act == SpeechAct.Exclamation) result.Add(Word(Concepts.WOW));
                AddVocative(result, c.recipient);
                return result;
            }
            var kind = Concepts.KindOf(c.predicate);
            if (kind == ConceptKind.Interjection || kind == ConceptKind.QuestionWord)
            {
                // 「はい」「うん」「何？」など1語の発話（＋呼びかけ）
                result.Add(Word(c.predicate));
                AddVocative(result, c.recipient);
                return result;
            }
            return Clause(c);
        }

        /// <summary>挨拶・別れ・感謝・謝罪の定型（宇宙語仕様 §5.1、挨拶などの定型）。</summary>
        private List<SurfaceToken> Formula(ClauseData c)
        {
            string fixedWord;
            switch (c.act)
            {
                case SpeechAct.Farewell: fixedWord = Concepts.GOODBYE; break;
                case SpeechAct.Thanks: fixedWord = Concepts.THANKS; break;
                case SpeechAct.Apology: fixedWord = Concepts.SORRY; break;
                default: fixedWord = Concepts.HELLO; break;
            }
            bool predIsInterj = Has(c.predicate) && Concepts.KindOf(c.predicate) == ConceptKind.Interjection;
            var core = new List<SurfaceToken> { Word(predIsInterj ? c.predicate : fixedWord) };
            // 強調（「とても ありがとう」）
            foreach (var adv in c.adverbs)
            {
                if (G.AdverbBeforeVerb) core.Insert(0, Word(adv));
                else core.Add(Word(adv));
            }
            if (c.timeWord != null)
            {
                if (G.GreetingTimeFirst) core.Insert(0, Word(c.timeWord));
                else core.Add(Word(c.timeWord));
            }
            AddVocative(core, c.recipient);
            // 動詞などの述語があれば、続けて内容の文を言う（「ありがとう、手伝ってくれた」）
            if (Has(c.predicate) && !predIsInterj)
            {
                var inner = new ClauseData
                {
                    act = SpeechAct.Statement,
                    predicate = c.predicate,
                    agent = c.agent,
                    patient = c.patient,
                    location = c.location,
                    tense = c.tense,
                    negative = c.negative
                };
                var clause = Clause(inner);
                if (clause.Count > 0)
                {
                    clause[0].attachedToPrevious = false;
                    core.AddRange(clause);
                }
            }
            return core;
        }

        /// <summary>差し出す（「どうぞ」＋物）。</summary>
        private List<SurfaceToken> OfferSentence(ClauseData c)
        {
            var result = new List<SurfaceToken> { Gram(GramIds.OFFER) };
            if (Has(c.predicate) && Concepts.KindOf(c.predicate) != ConceptKind.Interjection)
            {
                var inner = new ClauseData
                {
                    act = SpeechAct.Statement,
                    predicate = c.predicate,
                    agent = c.agent,
                    patient = c.patient,
                    recipient = c.recipient,
                    location = c.location,
                    timeWord = c.timeWord,
                    tense = c.tense,
                    negative = c.negative,
                    adverbs = c.adverbs
                };
                result.AddRange(Clause(inner));
            }
            else
            {
                if (c.patient != null) result.AddRange(NP(c.patient));
                if (c.recipient != null) result.AddRange(Mark(NP(c.recipient), GramIds.RECIPIENT, true));
            }
            return result;
        }

        private void AddVocative(List<SurfaceToken> core, MeaningArg recipient)
        {
            if (recipient == null) return;
            var voc = NP(recipient);
            if (G.VocativeFirst) core.InsertRange(0, voc);
            else core.AddRange(voc);
        }

        // ------------------------------------------------------------
        // 節（語順・格・時・疑問・希望/依頼）
        // ------------------------------------------------------------

        private List<SurfaceToken> Clause(ClauseData c)
        {
            if (!Has(c.predicate)) return new List<SurfaceToken>();

            // 名詞句
            var S = c.agent != null ? Mark(NP(c.agent), GramIds.AGENT, G.MarkAgent) : null;
            var O = c.patient != null ? Mark(NP(c.patient), GramIds.PATIENT, G.MarkPatient) : null;
            var X = new List<SurfaceToken>();
            if (c.location != null) X.AddRange(Mark(NP(c.location), GramIds.LOCATIVE, true));
            if (c.recipient != null) X.AddRange(Mark(NP(c.recipient), GramIds.RECIPIENT, true));
            var V = VerbComplex(c);

            // 時の言葉（時制標識を持たない星では「前に／後で」に当たる語を補う）
            List<SurfaceToken> T = null;
            if (c.timeWord != null) T = new List<SurfaceToken> { Word(c.timeWord) };
            else if (G.Tense == TenseStyle.TimeWordOnly && c.tense != Tense.Present)
                T = new List<SurfaceToken> { Gram(c.tense == Tense.Past ? GramIds.PAST : GramIds.FUTURE) };

            // 基本語順に並べる。場所・受け手（X）は目的語の「動詞と反対側」に隣接させる。
            string order = G.Order.ToString();
            int iO = order.IndexOf('O'), iV = order.IndexOf('V');
            var segs = new List<Seg>();
            foreach (char ch in order)
            {
                if (ch == 'S') { if (S != null) segs.Add(new Seg('S', S)); }
                else if (ch == 'V') segs.Add(new Seg('V', V));
                else
                {
                    if (iO < iV && X.Count > 0) segs.Add(new Seg('X', X));
                    if (O != null) segs.Add(new Seg('O', O));
                    if (iO > iV && X.Count > 0) segs.Add(new Seg('X', X));
                }
            }

            // 時の言葉の位置
            if (T != null)
            {
                var seg = new Seg('T', T);
                switch (G.TimePos)
                {
                    case TimePosition.Initial: segs.Insert(0, seg); break;
                    case TimePosition.Final: segs.Add(seg); break;
                    default: segs.Insert(IndexOfSeg(segs, 'V'), seg); break;
                }
            }

            // 疑問詞の前置
            bool hasWh = IsQuestionWord(c.agent) || IsQuestionWord(c.patient) || IsQuestionWord(c.location) || IsQuestionWord(c.recipient)
                         || (c.timeWord != null && Concepts.KindOf(c.timeWord) == ConceptKind.QuestionWord);
            if (hasWh && G.WhFronting)
            {
                char which = IsQuestionWord(c.agent) ? 'S' : IsQuestionWord(c.patient) ? 'O' : (IsQuestionWord(c.location) || IsQuestionWord(c.recipient)) ? 'X' : 'T';
                int idx = IndexOfSeg(segs, which);
                if (idx > 0)
                {
                    var s = segs[idx];
                    segs.RemoveAt(idx);
                    segs.Insert(0, s);
                }
            }

            // はい・いいえ疑問の語順変化（動詞を文頭へ）
            bool isYesNo = c.act == SpeechAct.Question;
            if (isYesNo && G.Question == QuestionStyle.VerbFronting)
            {
                int idx = IndexOfSeg(segs, 'V');
                if (idx > 0)
                {
                    var s = segs[idx];
                    segs.RemoveAt(idx);
                    segs.Insert(0, s);
                }
            }

            var result = new List<SurfaceToken>();
            foreach (var s in segs) result.AddRange(s.Tokens);

            // 文頭・文末の希望／依頼／誘いの標識
            string mood = MoodOf(c);
            if (mood != null && (G.Mood == MoodStyle.SentenceInitial || G.Mood == MoodStyle.SentenceFinal))
                AddEdge(result, Gram(mood), G.Mood == MoodStyle.SentenceInitial);

            // 疑問標識
            bool addQ = (isYesNo && G.Question != QuestionStyle.VerbFronting) || (c.act == SpeechAct.WhQuestion && G.WhUsesParticle);
            if (addQ)
            {
                bool initial = G.Question == QuestionStyle.ParticleInitial;
                AddEdge(result, Gram(GramIds.QUESTION), initial);
            }
            if (result.Count > 0) result[0].attachedToPrevious = false;
            return result;
        }

        private static void AddEdge(List<SurfaceToken> list, SurfaceToken t, bool initial)
        {
            if (initial)
            {
                if (list.Count > 0) list[0].attachedToPrevious = false;
                list.Insert(0, t);
            }
            else list.Add(t);
        }

        private string MoodOf(ClauseData c)
        {
            switch (c.act)
            {
                case SpeechAct.Desire: return c.predicate == Concepts.WANT ? null : GramIds.DESIRE; // 「欲しい」自体が述語なら重ねない
                case SpeechAct.Request: return GramIds.REQUEST;
                case SpeechAct.Invitation: return GramIds.INVITE;
                default: return null;
            }
        }

        private static bool IsQuestionWord(MeaningArg a) => a != null && Concepts.KindOf(a.concept) == ConceptKind.QuestionWord;

        private sealed class Seg
        {
            public readonly char Role;
            public readonly List<SurfaceToken> Tokens;
            public Seg(char role, List<SurfaceToken> tokens) { Role = role; Tokens = tokens; }
        }

        private static int IndexOfSeg(List<Seg> segs, char role)
        {
            for (int i = 0; i < segs.Count; i++) if (segs[i].Role == role) return i;
            return segs.Count;
        }

        /// <summary>述語のまとまり（否定・時制・希望/依頼・副詞を含む）。</summary>
        private List<SurfaceToken> VerbComplex(ClauseData c)
        {
            var kind = Concepts.KindOf(c.predicate);
            bool isVerb = kind == ConceptKind.Verb;
            string mood = MoodOf(c);

            // 主要部（接辞が付く語）：動詞、コピュラ、または述語の形容詞・名詞
            SurfaceToken head;
            SurfaceToken complement = null;
            if (isVerb || !G.Copula) head = Word(c.predicate);
            else
            {
                head = Gram(GramIds.COPULA);
                complement = Word(c.predicate);
            }

            // 接辞（内側→外側）
            var affixes = new List<KeyValuePair<string, bool>>(); // (標識, 接頭辞か)
            var negAffix = c.negative && (G.Negation == NegationStyle.Prefix || G.Negation == NegationStyle.Suffix)
                ? new KeyValuePair<string, bool>(GramIds.NEG, G.Negation == NegationStyle.Prefix) : default(KeyValuePair<string, bool>);
            var tenseAffix = c.tense != Tense.Present && G.Tense == TenseStyle.Affix
                ? new KeyValuePair<string, bool>(c.tense == Tense.Past ? GramIds.PAST : GramIds.FUTURE, G.TenseBeforeVerb) : default(KeyValuePair<string, bool>);
            if (G.NegationInsideTense) { if (negAffix.Key != null) affixes.Add(negAffix); if (tenseAffix.Key != null) affixes.Add(tenseAffix); }
            else { if (tenseAffix.Key != null) affixes.Add(tenseAffix); if (negAffix.Key != null) affixes.Add(negAffix); }
            if (mood != null && G.Mood == MoodStyle.Affix) affixes.Add(new KeyValuePair<string, bool>(mood, !G.MarkersAfter));

            var word = new List<SurfaceToken>();
            // 接頭辞は外側から
            for (int i = affixes.Count - 1; i >= 0; i--) if (affixes[i].Value) word.Add(Gram(affixes[i].Key));
            word.Add(head);
            for (int i = 0; i < affixes.Count; i++) if (!affixes[i].Value) word.Add(Gram(affixes[i].Key));
            for (int i = 1; i < word.Count; i++) word[i].attachedToPrevious = true;

            var stem = new List<SurfaceToken>();
            if (complement != null)
            {
                if (G.IsOV) { stem.Add(complement); stem.AddRange(word); }
                else { stem.AddRange(word); stem.Add(complement); }
            }
            else stem.AddRange(word);

            // 独立した付属語（内側：時制 → 否定 → 希望/依頼）
            var before = new List<SurfaceToken>();
            var after = new List<SurfaceToken>();
            if (c.tense != Tense.Present && G.Tense == TenseStyle.Particle)
            {
                var t = Gram(c.tense == Tense.Past ? GramIds.PAST : GramIds.FUTURE);
                if (G.TenseBeforeVerb) before.Insert(0, t); else after.Add(t);
            }
            if (c.negative && G.Negation == NegationStyle.ParticleBeforeVerb) before.Insert(0, Gram(GramIds.NEG));
            if (c.negative && G.Negation == NegationStyle.ParticleAfterVerb) after.Add(Gram(GramIds.NEG));
            if (mood != null && G.Mood == MoodStyle.ParticleBeforeVerb) before.Insert(0, Gram(mood));
            if (mood != null && G.Mood == MoodStyle.ParticleAfterVerb) after.Add(Gram(mood));

            var result = new List<SurfaceToken>();
            if (G.AdverbBeforeVerb) foreach (var adv in c.adverbs) result.Add(Word(adv));
            result.AddRange(before);
            result.AddRange(stem);
            result.AddRange(after);
            if (!G.AdverbBeforeVerb) foreach (var adv in c.adverbs) result.Add(Word(adv));
            if (result.Count > 0) result[0].attachedToPrevious = false;
            return result;
        }

        // ------------------------------------------------------------
        // 名詞句（形容詞・数量・所有・複数）と格の標識
        // ------------------------------------------------------------

        private List<SurfaceToken> NP(MeaningArg a)
        {
            var kind = Concepts.KindOf(a.concept);
            var noun = new List<SurfaceToken>();
            var head = Word(a.concept);
            bool plural = a.plural || (a.quantity > 1 && G.PluralWithNumerals);
            bool canPlural = kind != ConceptKind.Pronoun && kind != ConceptKind.Name && kind != ConceptKind.QuestionWord;
            if (plural && canPlural && G.Plural != PluralStyle.None)
            {
                switch (G.Plural)
                {
                    case PluralStyle.Reduplication:
                    {
                        // 最初の音節を重ねる（1音節語は丸ごと）
                        var lw = lang.Lexicon.Get(a.concept);
                        string first = lw.Syllables != null && lw.Syllables.Length > 0 ? lw.Syllables[0] : head.surface;
                        noun.Add(Tok(first.ToLowerInvariant(), GramIds.PLURAL, true, false));
                        head.attachedToPrevious = true;
                        noun.Add(head);
                        break;
                    }
                    case PluralStyle.Affix:
                        if (G.PluralAfter) { noun.Add(head); noun.Add(Gram(GramIds.PLURAL, true)); }
                        else { noun.Add(Gram(GramIds.PLURAL)); head.attachedToPrevious = true; noun.Add(head); }
                        break;
                    default:
                        if (G.PluralAfter) { noun.Add(head); noun.Add(Gram(GramIds.PLURAL)); }
                        else { noun.Add(Gram(GramIds.PLURAL)); noun.Add(head); }
                        break;
                }
            }
            else noun.Add(head);

            // 形容詞
            if (a.adjectives != null && a.adjectives.Count > 0)
            {
                var adjs = new List<SurfaceToken>();
                foreach (var adj in a.adjectives) if (Has(adj)) adjs.Add(Word(adj));
                if (G.AdjectiveAfterNoun) noun.AddRange(adjs);
                else noun.InsertRange(0, adjs);
            }
            noun[0].attachedToPrevious = false;

            // 数量
            if (a.quantity > 0)
            {
                var num = NumberTokens(a.quantity);
                if (G.NumeralBeforeNoun) noun.InsertRange(0, num);
                else noun.AddRange(num);
            }

            // 所有
            if (Has(a.possessor))
            {
                var p = new List<SurfaceToken> { Word(a.possessor) };
                if (G.PossessiveMarked) p = Mark(p, GramIds.POSSESSIVE, true);
                if (G.PossessorFirst) noun.InsertRange(0, p);
                else noun.AddRange(p);
            }
            noun[0].attachedToPrevious = false;
            return noun;
        }

        /// <summary>格の標識を付ける（付属語＝独立した語、接辞＝名詞句の端に連結）。</summary>
        private List<SurfaceToken> Mark(List<SurfaceToken> np, string gram, bool required)
        {
            if (!required || np == null || np.Count == 0) return np;
            if (G.Case == CaseStrategy.Affixes)
            {
                if (G.MarkersAfter) np.Add(Gram(gram, true));
                else
                {
                    np[0].attachedToPrevious = true;
                    np.Insert(0, Gram(gram));
                }
            }
            else
            {
                if (G.MarkersAfter) np.Add(Gram(gram));
                else np.Insert(0, Gram(gram));
            }
            return np;
        }

        // ------------------------------------------------------------
        // 語
        // ------------------------------------------------------------

        private SurfaceToken Word(string concept) => Tok(lang.Word(concept), concept, false, false);
        private SurfaceToken Gram(string id, bool attached = false) => Tok(lang.Word(id), id, true, attached);

        private static SurfaceToken Tok(string surface, string id, bool grammatical, bool attached)
        {
            return new SurfaceToken { surface = surface, internalId = id, grammatical = grammatical, attachedToPrevious = attached };
        }

        // ------------------------------------------------------------
        // 表示用：記録（text + tokens）から文の区切りと文末記号を取り出す
        // UtteranceRecord は sentenceStarts を持たないため、原本の text と tokens を照合して求める（原本は変えない）。
        // ------------------------------------------------------------

        public struct SentenceSpan
        {
            public int Start;
            public int End; // 排他的
            public string Punctuation;
        }

        public static List<SentenceSpan> SplitSentences(string text, List<SurfaceToken> tokens)
        {
            var result = new List<SentenceSpan>();
            if (tokens == null || tokens.Count == 0) return result;
            text = text ?? string.Empty;
            int pos = 0;
            int start = 0;
            bool ok = true;
            for (int i = 0; i < tokens.Count; i++)
            {
                if (ok)
                {
                    while (pos < text.Length && text[pos] == ' ') pos++;
                    var surf = tokens[i].surface ?? string.Empty;
                    if (pos + surf.Length <= text.Length && string.CompareOrdinal(text, pos, surf, 0, surf.Length) == 0) pos += surf.Length;
                    else ok = false;
                }
                if (!ok) continue;
                // 直後が空白でも語の連結でもない文字なら、それが文末記号
                var sb = new StringBuilder();
                int p = pos;
                while (p < text.Length && text[p] != ' ' && IsPunct(text[p])) sb.Append(text[p++]);
                if (sb.Length > 0)
                {
                    pos = p;
                    result.Add(new SentenceSpan { Start = start, End = i + 1, Punctuation = sb.ToString() });
                    start = i + 1;
                }
            }
            if (start < tokens.Count) result.Add(new SentenceSpan { Start = start, End = tokens.Count, Punctuation = ok ? "" : "" });
            return result;
        }

        private static bool IsPunct(char c) => c == '.' || c == '!' || c == '?' || c == ',';
    }
}
