using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    // =====================================================================
    // 星ごとの文法パラメータ（宇宙語仕様 §3.2〜§3.3、企画仕様 §5.2）。
    // 採用しないもの（§3.4）：終助詞の細かなニュアンス、冠詞、文法上の性。
    // どのパラメータも「その星では常にその規則」で、例外を作らない（§2.2）。
    // =====================================================================

    public enum WordOrder { SOV, SVO, VSO, VOS, OVS, OSV }

    /// <summary>格（誰が・何を）の示し方。受け手・場所・所有は語順だけでは示さず、常に標識を使う。</summary>
    public enum CaseStrategy
    {
        WordOrderOnly, // 主語・目的語は語順のみ（受け手・場所は付属語）
        Particles,     // 独立した付属語（後置詞／前置詞）
        Affixes        // 語に直接付く接辞
    }

    public enum NegationStyle { ParticleBeforeVerb, ParticleAfterVerb, Prefix, Suffix }

    public enum QuestionStyle { ParticleInitial, ParticleFinal, VerbFronting }

    public enum TenseStyle
    {
        Particle,     // 時制を表す独立した語
        Affix,        // 動詞の接辞
        TimeWordOnly  // 時制標識なし。時の言葉が無ければ「前に／後で」に当たる語を添える
    }

    /// <summary>希望（Desire）・依頼（Request）・誘い（Invitation）の標識の置き方。</summary>
    public enum MoodStyle { ParticleBeforeVerb, ParticleAfterVerb, SentenceInitial, SentenceFinal, Affix }

    public enum PluralStyle { None, Reduplication, Affix, Particle }

    public enum TimePosition { Initial, BeforeVerb, Final }

    public sealed class GrammarParams
    {
        // ---- 【仮数値】語順の出やすさ（自然言語で多いものほど出やすく） ----
        public static readonly float[] WordOrderWeights = { 40f, 35f, 12f, 6f, 4f, 3f };
        /// <summary>数の体系（基数）とその出やすさ。</summary>
        public static readonly int[] NumberBases = { 10, 12, 8, 6, 5 };
        public static readonly float[] NumberBaseWeights = { 45f, 16f, 14f, 13f, 12f };

        public WordOrder Order;
        public bool AdjectiveAfterNoun;
        public CaseStrategy Case;
        /// <summary>付属語・接辞を名詞句の後ろに置くか（後置詞・接尾辞）。false なら前置詞・接頭辞。</summary>
        public bool MarkersAfter;
        public bool MarkAgent;
        public bool MarkPatient;
        public bool PossessorFirst;
        /// <summary>所有に専用の付属語（属格）を使うか。false なら並べるだけ。</summary>
        public bool PossessiveMarked;
        public NegationStyle Negation;
        public QuestionStyle Question;
        public bool WhFronting;
        public bool WhUsesParticle;
        public TenseStyle Tense;
        /// <summary>時制の付属語・接辞を動詞の前に置くか。</summary>
        public bool TenseBeforeVerb;
        public MoodStyle Mood;
        public PluralStyle Plural;
        /// <summary>複数の接辞・付属語を名詞の後ろに置くか。</summary>
        public bool PluralAfter;
        /// <summary>数詞があるときも複数標識を付けるか。</summary>
        public bool PluralWithNumerals;
        public bool NumeralBeforeNoun;
        public bool AdverbBeforeVerb;
        public TimePosition TimePos;
        /// <summary>形容詞・名詞が述語のとき、つなぎの語（コピュラ）を使うか。false なら形容詞をそのまま述語にする。</summary>
        public bool Copula;
        public bool ReasonClauseFirst;
        public bool VocativeFirst;
        public bool GreetingTimeFirst;
        /// <summary>否定と時制が両方とも接辞のとき、否定が内側（語幹に近い）か。</summary>
        public bool NegationInsideTense;

        // 数詞
        public int NumberBase;
        public bool NumberBigFirst;
        public bool NumberMultiplierFirst;
        public bool NumberOmitOne;
        public bool NumberConnector;

        /// <summary>目的語が動詞より前に来る（OV型）か。</summary>
        public bool IsOV => Order == WordOrder.SOV || Order == WordOrder.OSV || Order == WordOrder.OVS;
        public bool IsVerbInitial => Order == WordOrder.VSO || Order == WordOrder.VOS;

        /// <summary>言語生成バージョン1の文法生成。語順に応じて「自然にありがちな組み合わせ」へ寄せ、筋の通った類型にする。</summary>
        public static GrammarParams GenerateV1(SeededRandom rng)
        {
            var g = new GrammarParams();
            g.Order = (WordOrder)rng.WeightedIndex(WordOrderWeights);
            bool ov = g.IsOV;

            g.MarkersAfter = rng.Chance(ov ? 0.85f : 0.2f);
            g.AdjectiveAfterNoun = rng.Chance(ov ? 0.4f : 0.6f);
            float c = rng.NextFloat();
            g.Case = c < 0.3f ? CaseStrategy.WordOrderOnly : c < 0.72f ? CaseStrategy.Particles : CaseStrategy.Affixes;
            // SVO 以外は語順だけだと主語・目的語の区別が難しいので、標識を使う側に寄せる
            if (g.Case == CaseStrategy.WordOrderOnly && g.Order != WordOrder.SVO && rng.Chance(0.6f)) g.Case = CaseStrategy.Particles;
            g.MarkPatient = g.Case != CaseStrategy.WordOrderOnly;
            g.MarkAgent = g.Case != CaseStrategy.WordOrderOnly && rng.Chance(0.4f);
            g.PossessorFirst = rng.Chance(ov ? 0.85f : 0.4f);
            g.PossessiveMarked = rng.Chance(0.75f);

            float n = rng.NextFloat();
            g.Negation = n < 0.45f ? NegationStyle.ParticleBeforeVerb : n < 0.65f ? NegationStyle.ParticleAfterVerb : n < 0.8f ? NegationStyle.Prefix : NegationStyle.Suffix;

            float q = rng.NextFloat();
            if (g.IsVerbInitial) g.Question = q < 0.5f ? QuestionStyle.ParticleInitial : QuestionStyle.ParticleFinal;
            else g.Question = q < (ov ? 0.15f : 0.35f) ? QuestionStyle.ParticleInitial : q < 0.8f ? QuestionStyle.ParticleFinal : QuestionStyle.VerbFronting;
            g.WhFronting = rng.Chance(ov ? 0.3f : 0.65f);
            g.WhUsesParticle = rng.Chance(0.45f);

            float t = rng.NextFloat();
            g.Tense = t < 0.4f ? TenseStyle.Particle : t < 0.78f ? TenseStyle.Affix : TenseStyle.TimeWordOnly;
            g.TenseBeforeVerb = rng.Chance(ov ? 0.25f : 0.6f);
            float m = rng.NextFloat();
            g.Mood = m < 0.22f ? MoodStyle.ParticleBeforeVerb : m < 0.44f ? MoodStyle.ParticleAfterVerb : m < 0.6f ? MoodStyle.SentenceInitial : m < 0.78f ? MoodStyle.SentenceFinal : MoodStyle.Affix;

            float p = rng.NextFloat();
            g.Plural = p < 0.25f ? PluralStyle.None : p < 0.45f ? PluralStyle.Reduplication : p < 0.75f ? PluralStyle.Affix : PluralStyle.Particle;
            g.PluralAfter = rng.Chance(0.7f);
            g.PluralWithNumerals = rng.Chance(0.4f);
            g.NumeralBeforeNoun = rng.Chance(0.65f);
            g.AdverbBeforeVerb = rng.Chance(ov ? 0.75f : 0.45f);
            float tp = rng.NextFloat();
            g.TimePos = tp < 0.5f ? TimePosition.Initial : tp < 0.8f ? TimePosition.BeforeVerb : TimePosition.Final;
            g.Copula = rng.Chance(0.45f);
            g.ReasonClauseFirst = rng.Chance(ov ? 0.8f : 0.3f);
            g.VocativeFirst = rng.Chance(0.5f);
            g.GreetingTimeFirst = rng.Chance(0.5f);
            g.NegationInsideTense = rng.Chance(0.6f);

            g.NumberBase = NumberBases[rng.WeightedIndex(NumberBaseWeights)];
            g.NumberBigFirst = rng.Chance(0.75f);
            g.NumberMultiplierFirst = rng.Chance(0.7f);
            g.NumberOmitOne = rng.Chance(0.5f);
            g.NumberConnector = rng.Chance(0.35f);
            // 「位→係数」で大きい位から、または「係数→位」で小さい位から並べると、一の位の数字が隣の位の係数と
            // 区別できなくなる（例：5進で 7=[5][2] と 10=[5][2]）。その組み合わせでは必ずつなぎ語を入れる。
            if (g.NumberBigFirst != g.NumberMultiplierFirst) g.NumberConnector = true;
            return g;
        }

        /// <summary>開発者向けの説明（デバッグ窓専用。通常プレイでは表示しない）。</summary>
        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("語順: ").Append(Order).Append(IsOV ? "（OV型）" : "（VO型）").Append('\n');
            sb.Append("形容詞: ").Append(AdjectiveAfterNoun ? "名詞の後" : "名詞の前").Append('\n');
            sb.Append("格: ").Append(Case).Append(" / 標識の位置: ").Append(MarkersAfter ? "後ろ（後置詞・接尾辞）" : "前（前置詞・接頭辞）")
              .Append(" / 主語標識: ").Append(MarkAgent ? "あり" : "なし").Append(" / 目的語標識: ").Append(MarkPatient ? "あり" : "なし").Append('\n');
            sb.Append("所有: ").Append(PossessorFirst ? "所有者が先" : "所有者が後").Append(PossessiveMarked ? "＋属格標識" : "（並べるだけ）").Append('\n');
            sb.Append("否定: ").Append(Negation).Append('\n');
            sb.Append("疑問: ").Append(Question).Append(" / 疑問詞の前置: ").Append(WhFronting ? "する" : "しない").Append(" / 疑問詞文に疑問標識: ").Append(WhUsesParticle ? "付ける" : "付けない").Append('\n');
            sb.Append("時制: ").Append(Tense).Append(Tense != TenseStyle.TimeWordOnly ? (TenseBeforeVerb ? "（動詞の前）" : "（動詞の後）") : "").Append(" / 否定と時制の接辞順: ").Append(NegationInsideTense ? "語幹-否定-時制" : "語幹-時制-否定").Append('\n');
            sb.Append("希望・依頼・誘い: ").Append(Mood).Append('\n');
            sb.Append("複数: ").Append(Plural).Append(Plural == PluralStyle.Affix || Plural == PluralStyle.Particle ? (PluralAfter ? "（後ろ）" : "（前）") : "")
              .Append(" / 数詞と併用: ").Append(PluralWithNumerals ? "する" : "しない").Append(" / 数詞: ").Append(NumeralBeforeNoun ? "名詞の前" : "名詞の後").Append('\n');
            sb.Append("副詞: ").Append(AdverbBeforeVerb ? "動詞の前" : "動詞の後").Append(" / 時の言葉: ").Append(TimePos).Append('\n');
            sb.Append("コピュラ: ").Append(Copula ? "あり" : "なし（形容詞がそのまま述語）").Append(" / 理由節: ").Append(ReasonClauseFirst ? "前" : "後").Append('\n');
            sb.Append("呼びかけ: ").Append(VocativeFirst ? "前" : "後").Append(" / 挨拶の時の言葉: ").Append(GreetingTimeFirst ? "前" : "後").Append('\n');
            sb.Append($"数: {NumberBase}進法 / {(NumberBigFirst ? "大きい位から" : "小さい位から")} / {(NumberMultiplierFirst ? "係数→位" : "位→係数")} / 1の省略: {(NumberOmitOne ? "する" : "しない")} / つなぎ語: {(NumberConnector ? "あり" : "なし")}");
            return sb.ToString();
        }
    }
}
