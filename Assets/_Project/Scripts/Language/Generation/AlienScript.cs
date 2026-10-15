using System.Collections.Generic;
using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 宇宙人の書き文字（タブレット仕様 §3「宇宙人の文字と、聞こえる宇宙語を区別する」、宇宙語仕様 §11.1【未決定】）。
    ///
    /// 【仮の実装案】発音表記（a〜z）の1文字＝1音素を、星ごとに決まる「宇宙文字」1字へ写す音素文字。
    /// ・文字の字形の組（ギリシャ風・キリル風・図形記号・混合）と割り当ては星のシードで変わる。
    /// ・多くの日本語フォント（JIS X 0208）と欧文フォント（WGL4）の両方にある文字だけを使い、文字化けしにくくする。
    /// ・看板などを読み方と見比べれば文字そのものも解読できる（文字解読を別の謎解きとして楽しめる）。
    /// ・宇宙文字を見ただけで読みや訳を自動開示しない。読み方の併記は設定（LanguageSettings.ShowSignReading）。
    /// </summary>
    public sealed class AlienScript
    {
        // ---- 【仮】字形の組（必要なら差し替え・追加） ----
        public static readonly string[] GlyphSets =
        {
            // 0: ギリシャ風（ラテン文字と同形のものは除外）
            "ΓΔΘΛΞΠΣΦΨΩαβγδεζηθλμξπσφψω",
            // 1: キリル風
            "БГДЖЗИЛПФЦЧШЩЪЫЭЮЯбвгджзилп",
            // 2: 図形記号
            "△▲▽▼□■◇◆○●◎☆★†‡∀∃∇∂∞≡⊥∠¶§⌒√∫♯♭",
            // 3: 混合
            "ΓΔΘΛΞΠΣΦΨΩБЖИЛФШЭЮЯ△▲▽▼◇◆☆★∀∇∞"
        };
        public static readonly string[] SetNames = { "ギリシャ風", "キリル風", "図形記号", "混合" };
        /// <summary>文末記号の写し（. ! ?）。</summary>
        public static readonly string[] Punctuation = { "∴", "∵", "∽" };

        public readonly int SetIndex;
        private readonly Dictionary<char, string> map = new Dictionary<char, string>();

        public AlienScript(SeededRandom rng)
        {
            SetIndex = rng.Range(0, GlyphSets.Length);
            var glyphs = new List<string>();
            var set = GlyphSets[SetIndex];
            for (int i = 0; i < set.Length; i++)
            {
                string gch = set[i].ToString();
                if (!glyphs.Contains(gch)) glyphs.Add(gch);
            }
            rng.Shuffle(glyphs);
            for (int i = 0; i < 26; i++) map[(char)('a' + i)] = glyphs[i % glyphs.Count];
        }

        public string Glyph(char c)
        {
            c = char.ToLowerInvariant(c);
            if (map.TryGetValue(c, out var g)) return g;
            switch (c)
            {
                case '.': return Punctuation[0];
                case '!': return Punctuation[1];
                case '?': return Punctuation[2];
                default: return c.ToString();
            }
        }

        /// <summary>語1つを宇宙文字にする。</summary>
        public string Word(string surface)
        {
            if (string.IsNullOrEmpty(surface)) return string.Empty;
            var sb = new StringBuilder(surface.Length * 2);
            foreach (var ch in surface) sb.Append(Glyph(ch));
            return sb.ToString();
        }

        /// <summary>発音表記の文全体（text）をそのまま宇宙文字へ写す（語の区切り・接辞の連結も保つ）。</summary>
        public string Text(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text.Length * 2);
            foreach (var ch in text)
            {
                if (ch == ' ') sb.Append(' ');
                else sb.Append(Glyph(ch));
            }
            return sb.ToString();
        }

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append("字形: ").Append(SetNames[SetIndex]).Append('\n');
            for (char c = 'a'; c <= 'z'; c++) sb.Append(c).Append('=').Append(map[c]).Append(c == 'm' ? "\n" : "  ");
            return sb.ToString();
        }
    }
}
