using System.Text;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 1つの星の言語定義（宇宙語仕様 §14.1「星の言語定義：語彙・語順・文法・発音・生成バージョン」）。
    /// 星のシードと言語生成バージョンだけから決定的に作る。ホストとゲストで同じシード＋同じバージョンなら同じ宇宙語になる。
    /// </summary>
    public sealed class LanguageDefinition
    {
        /// <summary>このコードが生成できる最新の言語生成バージョン。</summary>
        public const int LatestVersion = 1;

        public ulong PlanetSeed { get; private set; }
        public ulong LanguageSeed { get; private set; }
        public int Version { get; private set; }
        public Phonology Phonology { get; private set; }
        public GrammarParams Grammar { get; private set; }
        public NumberSystem Numbers { get; private set; }
        public Lexicon Lexicon { get; private set; }
        public AlienScript Script { get; private set; }
        public string Fingerprint { get; private set; }

        /// <summary>
        /// 生成。PlanetInfo.languageGenVersion で分岐する（将来アルゴリズムを変えても既存の星の言葉が変わらないように、
        /// 旧バージョンの生成関数は残しておく）。
        /// </summary>
        public static LanguageDefinition Generate(ulong planetSeed, int version)
        {
            int v = version;
            if (v <= 0) v = 1;
            if (v > LatestVersion)
            {
                Warn($"[Language] 言語生成バージョン {version} はこのビルドより新しいため、v{LatestVersion} で生成します（ホストとゲストのバージョン差に注意）");
                v = LatestVersion;
            }
            switch (v)
            {
                case 1:
                default:
                    return GenerateV1(planetSeed);
            }
        }

        // Unity の Debug を別メソッドに分ける（生成ロジックを Unity 外の検証ハーネスでも動かせるように）
        private static void Warn(string message) => Debug.LogWarning(message);

        private static LanguageDefinition GenerateV1(ulong planetSeed)
        {
            var def = new LanguageDefinition();
            def.PlanetSeed = planetSeed;
            def.Version = 1;
            def.LanguageSeed = StableHash.Derive(planetSeed, "language.v1");
            def.Phonology = Phonology.GenerateV1(SeededRandom.For(def.LanguageSeed, "phonology"));
            def.Grammar = GrammarParams.GenerateV1(SeededRandom.For(def.LanguageSeed, "grammar"));
            def.Numbers = new NumberSystem(def.Grammar);
            def.Lexicon = new Lexicon(def.LanguageSeed, def.Phonology);
            def.Lexicon.BuildBase(def.Numbers.Morphemes());
            def.Script = new AlienScript(SeededRandom.For(def.LanguageSeed, "script"));
            def.Fingerprint = StableHash.Fnv1a64(def.Lexicon.BaseFingerprint() + "|" + def.Grammar.Describe() + "|" + def.Phonology.Describe()).ToString("x16");
            return def;
        }

        public string Word(string concept) => Lexicon.Get(concept).Surface;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append($"言語生成バージョン: {Version} / 指紋: {Fingerprint}\n");
            sb.Append($"基本語彙: {Lexicon.BaseWords.Count}語 / 全語彙: {Lexicon.Count}語 / 衝突回避: {Lexicon.CollisionCount}回 / 紛らわしさ回避: {Lexicon.ConfusableRejectCount}回\n\n");
            sb.Append("[音韻]\n").Append(Phonology.Describe()).Append("\n\n");
            sb.Append("[文法]\n").Append(Grammar.Describe()).Append("\n\n");
            sb.Append("[文字]\n").Append(Script.Describe());
            return sb.ToString();
        }
    }
}
