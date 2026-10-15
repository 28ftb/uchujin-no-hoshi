using System.Collections.Generic;

namespace Uchujin.Language
{
    /// <summary>
    /// 数詞の体系（宇宙語仕様 §3.3「数・複数」、§4.1「数量」）。
    /// 星ごとに基数（10・12・8・6・5進）と合成規則が変わる。
    /// 例（10進・大きい位から・係数→位・1を省略しない）：23 = [2][10の位][3]。
    /// 数詞は「NUM:d」（0〜基数-1）と「NUM:Pk」（基数のk乗）の語から合成する。
    /// </summary>
    public sealed class NumberSystem
    {
        public const string Prefix = "NUM:";
        /// <summary>【仮数値】語を持つ位の最大（基数^9 まで）。</summary>
        public const int MaxPower = 9;

        public readonly int Base;
        private readonly GrammarParams g;

        public NumberSystem(GrammarParams grammar)
        {
            g = grammar;
            Base = grammar.NumberBase < 2 ? 10 : grammar.NumberBase;
        }

        public static string Digit(int d) => Prefix + d;
        public static string Power(int k) => Prefix + "P" + k;

        /// <summary>語彙として必要な数の語（基本語彙として固定順に生成する）。</summary>
        public List<string> Morphemes()
        {
            var list = new List<string>();
            for (int d = 0; d < Base; d++) list.Add(Digit(d));
            for (int k = 1; k <= MaxPower; k++) list.Add(Power(k));
            return list;
        }

        /// <summary>数を語（概念ID）の列にする。</summary>
        public List<string> Compose(long n)
        {
            var result = new List<string>();
            if (n < 0) n = -n;
            if (n == 0)
            {
                result.Add(Digit(0));
                return result;
            }
            // 位ごとの数字
            var digits = new List<int>();
            long max = 1;
            for (int k = 0; k <= MaxPower; k++) max *= Base;
            if (n >= max) n = max - 1;
            while (n > 0)
            {
                digits.Add((int)(n % Base));
                n /= Base;
            }
            var terms = new List<List<string>>();
            for (int k = digits.Count - 1; k >= 0; k--)
            {
                int d = digits[k];
                if (d == 0) continue;
                var term = new List<string>();
                if (k == 0) term.Add(Digit(d));
                else
                {
                    bool showMult = !(d == 1 && g.NumberOmitOne);
                    if (g.NumberMultiplierFirst)
                    {
                        if (showMult) term.Add(Digit(d));
                        term.Add(Power(k));
                    }
                    else
                    {
                        term.Add(Power(k));
                        if (showMult) term.Add(Digit(d));
                    }
                }
                terms.Add(term);
            }
            if (!g.NumberBigFirst) terms.Reverse();
            for (int i = 0; i < terms.Count; i++)
            {
                if (i > 0 && g.NumberConnector) result.Add(GramIds.NUM_AND);
                result.AddRange(terms[i]);
            }
            return result;
        }
    }
}
