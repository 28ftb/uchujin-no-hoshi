using System;
using System.Collections.Generic;
using System.Text;
using Uchujin.Core;

namespace Uchujin.Language
{
    /// <summary>
    /// 言語生成の自己検証（宇宙語仕様 §15.1、企画仕様 §11「言語生成」）。Unity 上（デバッグ窓 F12 の「自己テスト」）で実行して確認する。
    /// ・同じシード＋同じ生成バージョン → 同じ語彙・文法・文になる。
    /// ・異なるシード → 語彙だけでなく文法も変わる。
    /// ・主語/目的語の入れ替え・否定・時制・疑問・希望/依頼・受け手を文で区別できる。
    /// ・語の偶然の衝突が無い（全語彙で綴りが一意）。
    /// ・同じ意味からは同じ文ができる（比較で文法を推理できる）。
    /// Unity の API に依存しないので、Unity 外の検証ハーネスからも呼べる。
    /// </summary>
    public static class LanguageSelfTest
    {
        public static readonly string[] SampleSeeds = { "ALPHA", "BETA", "GAMMA", "DELTA", "OMEGA", "KUMO", "HOSHI", "12345" };

        public static bool RunAll(out string report, int seedCount = 40)
        {
            var sb = new StringBuilder();
            bool ok = true;
            ok &= Check(sb, "決定性（同じシード→同じ言語）", TestDeterminism);
            ok &= Check(sb, "シード差（文法も変わる）", w => TestVariation(w, seedCount));
            ok &= Check(sb, "綴りの一意性（偶然の衝突なし）", TestUniqueness);
            ok &= Check(sb, "意味の区別（主語/目的語・否定・時制・疑問・希望/依頼）", TestDistinctions);
            ok &= Check(sb, "一貫性（同じ意味→同じ文・同じ語）", TestConsistency);
            ok &= Check(sb, "数詞（1〜300が全て異なる）", TestNumbers);
            sb.Insert(0, ok ? "== 自己テスト：すべて成功 ==\n" : "== 自己テスト：失敗あり ==\n");
            report = sb.ToString();
            return ok;
        }

        private static bool Check(StringBuilder sb, string name, Func<StringBuilder, bool> test)
        {
            var w = new StringBuilder();
            bool ok;
            try { ok = test(w); }
            catch (Exception e)
            {
                ok = false;
                w.Append("  例外: ").Append(e.Message).Append('\n');
            }
            sb.Append(ok ? "[OK] " : "[NG] ").Append(name).Append('\n').Append(w);
            return ok;
        }

        private static ulong Seed(string text) => PlanetInfo.SeedFromText(text);

        /// <summary>検証用の代表的な意味（日本語訳はコメントのみ。ゲーム中には出さない）。</summary>
        public static List<MeaningFrame> SampleFrames()
        {
            return new List<MeaningFrame>
            {
                MeaningFrame.Make(SpeechAct.Statement, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISH, Concepts.RED)),          // 私は赤い魚が欲しい
                MeaningFrame.Make(SpeechAct.Statement, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISH, Concepts.RED)).Not(),    // 欲しくない
                MeaningFrame.Make(SpeechAct.Statement, Concepts.EAT).Agent(Concepts.I).Patient(Concepts.FRUIT).Past().When(Concepts.YESTERDAY),      // 昨日果物を食べた
                MeaningFrame.Make(SpeechAct.Question, Concepts.LIKE).Agent(Concepts.YOU).Patient(Concepts.FLOWER),                                  // 花が好き？
                MeaningFrame.Make(SpeechAct.WhQuestion, Concepts.EAT).Agent(Concepts.YOU).Patient(Concepts.WHAT),                                   // 何を食べる？
                MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Patient(MeaningArg.Of(Concepts.FISH, Concepts.BLUE).Qty(3)).Recipient(Concepts.I), // 青い魚を3匹持ってきて
                MeaningFrame.Make(SpeechAct.Desire, Concepts.GO).Agent(Concepts.I).At(Concepts.SEA).When(Concepts.TOMORROW),                        // 明日海へ行きたい
                MeaningFrame.Make(SpeechAct.Statement, Concepts.PRICE_EXPENSIVE).Agent(Concepts.THIS).Adverb(Concepts.VERY),                        // これはとても高い
                MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.FISH, Concepts.BIG).OwnedBy(Concepts.I).Plural()).At(Concepts.RIVER),
                MeaningFrame.Make(SpeechAct.Greeting, null).Recipient(Concepts.Name("guide")).When(Concepts.MORNING),
                MeaningFrame.Make(SpeechAct.Thanks, null).Adverb(Concepts.VERY),
                MeaningFrame.Make(SpeechAct.Invitation, Concepts.CATCH_FISH).Agent(Concepts.WE).Adverb(Concepts.TOGETHER),
                MeaningFrame.Make(SpeechAct.Statement, Concepts.REST).Agent(Concepts.I).Because(MeaningFrameLite.Make(Concepts.RAIN_VERB)),
                MeaningFrame.Make(SpeechAct.Offer, null).Patient(Concepts.GIFT).Recipient(Concepts.YOU),
            };
        }

        private static string RenderAll(LanguageDefinition def)
        {
            var r = new SentenceRenderer(def);
            var sb = new StringBuilder();
            foreach (var f in SampleFrames()) sb.Append(r.RenderFrame(f).text).Append('\n');
            sb.Append(def.Word(Concepts.Item("fish.sample"))).Append(' ').Append(def.Word(Concepts.Name("res_1"))).Append(' ').Append(def.Word(Concepts.Species("manepi")));
            return sb.ToString();
        }

        private static bool TestDeterminism(StringBuilder w)
        {
            bool ok = true;
            foreach (var s in SampleSeeds)
            {
                var a = LanguageDefinition.Generate(Seed(s), 1);
                var b = LanguageDefinition.Generate(Seed(s), 1);
                if (a.Fingerprint != b.Fingerprint || RenderAll(a) != RenderAll(b))
                {
                    ok = false;
                    w.Append("  不一致: ").Append(s).Append('\n');
                }
            }
            // 動的語の生成順が違っても同じ綴りになる
            var x = LanguageDefinition.Generate(Seed("ORDER"), 1);
            var y = LanguageDefinition.Generate(Seed("ORDER"), 1);
            var ids = new List<string>();
            for (int i = 0; i < 200; i++) ids.Add(Concepts.Item("test.item" + i));
            foreach (var id in ids) x.Lexicon.Get(id);
            for (int i = ids.Count - 1; i >= 0; i--) y.Lexicon.Get(ids[i]);
            int diff = 0;
            foreach (var id in ids) if (x.Word(id) != y.Word(id)) diff++;
            w.Append($"  生成順を逆にした動的語の不一致: {diff}/200（動的語どうしの偶然の衝突時のみ起こり得る。ホストの記録で同期される）\n");
            if (diff > 2) ok = false;
            return ok;
        }

        private static bool TestVariation(StringBuilder w, int seedCount)
        {
            var orders = new HashSet<WordOrder>();
            var cases = new HashSet<CaseStrategy>();
            var negs = new HashSet<NegationStyle>();
            var qs = new HashSet<QuestionStyle>();
            var tenses = new HashSet<TenseStyle>();
            var moods = new HashSet<MoodStyle>();
            var plurals = new HashSet<PluralStyle>();
            var bases = new HashSet<int>();
            var fish = new HashSet<string>();
            var counts = new Dictionary<WordOrder, int>();
            for (int i = 0; i < seedCount; i++)
            {
                var def = LanguageDefinition.Generate(Seed("VAR" + i), 1);
                var g = def.Grammar;
                orders.Add(g.Order);
                cases.Add(g.Case);
                negs.Add(g.Negation);
                qs.Add(g.Question);
                tenses.Add(g.Tense);
                moods.Add(g.Mood);
                plurals.Add(g.Plural);
                bases.Add(g.NumberBase);
                fish.Add(def.Word(Concepts.FISH));
                counts.TryGetValue(g.Order, out int c);
                counts[g.Order] = c + 1;
            }
            var sbo = new StringBuilder();
            foreach (var kv in counts) sbo.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            w.Append($"  {seedCount}個の星：語順{orders.Count}種（{sbo}）/ 格{cases.Count} / 否定{negs.Count} / 疑問{qs.Count} / 時制{tenses.Count} / 希望依頼{moods.Count} / 複数{plurals.Count} / 基数{bases.Count} / 「魚」の語{fish.Count}種\n");
            var a = LanguageDefinition.Generate(Seed("ALPHA"), 1);
            var b = LanguageDefinition.Generate(Seed("BETA"), 1);
            bool grammarDiffers = a.Grammar.Describe() != b.Grammar.Describe();
            w.Append("  ALPHA と BETA の文法の違い: ").Append(grammarDiffers ? "あり" : "なし").Append('\n');
            return orders.Count >= 3 && cases.Count >= 2 && negs.Count >= 3 && tenses.Count >= 2 && fish.Count >= seedCount * 0.8f && grammarDiffers;
        }

        private static bool TestUniqueness(StringBuilder w)
        {
            bool ok = true;
            foreach (var s in SampleSeeds)
            {
                var def = LanguageDefinition.Generate(Seed(s), 1);
                for (int i = 0; i < 300; i++) def.Lexicon.Get(Concepts.Name("resident_" + i));
                for (int i = 0; i < 300; i++) def.Lexicon.Get(Concepts.Item("item_" + i));
                for (int i = 0; i < 60; i++) def.Lexicon.Get(Concepts.Species("sp" + i));
                var seen = new Dictionary<string, string>();
                int dup = 0;
                foreach (var word in def.Lexicon.AllWords)
                {
                    if (seen.ContainsKey(word.Key)) dup++;
                    else seen[word.Key] = word.Concept;
                }
                w.Append($"  {s}: {def.Lexicon.Count}語 / 衝突検出→回避 {def.Lexicon.CollisionCount}回 / 重複 {dup}\n");
                if (dup > 0) ok = false;
            }
            return ok;
        }

        private static bool TestDistinctions(StringBuilder w)
        {
            bool ok = true;
            foreach (var s in SampleSeeds)
            {
                var def = LanguageDefinition.Generate(Seed(s), 1);
                var r = new SentenceRenderer(def);
                string T(MeaningFrame f) => r.RenderFrame(f).text;
                var basic = MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.CHILD).Patient(Concepts.FRIEND);
                var pairs = new List<KeyValuePair<string, MeaningFrame>>
                {
                    new KeyValuePair<string, MeaningFrame>("主語/目的語の入れ替え", MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.FRIEND).Patient(Concepts.CHILD)),
                    new KeyValuePair<string, MeaningFrame>("否定", MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.CHILD).Patient(Concepts.FRIEND).Not()),
                    new KeyValuePair<string, MeaningFrame>("過去", MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.CHILD).Patient(Concepts.FRIEND).Past()),
                    new KeyValuePair<string, MeaningFrame>("未来", MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.CHILD).Patient(Concepts.FRIEND).Future()),
                    new KeyValuePair<string, MeaningFrame>("疑問", MeaningFrame.Make(SpeechAct.Question, Concepts.SEE).Agent(Concepts.CHILD).Patient(Concepts.FRIEND)),
                    new KeyValuePair<string, MeaningFrame>("受け手", MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.CHILD).Recipient(Concepts.FRIEND)),
                };
                string baseText = StripPunct(T(basic));
                foreach (var p in pairs)
                {
                    if (StripPunct(T(p.Value)) == baseText)
                    {
                        // 疑問は語順変化型で主語が文頭に無い場合などに区別が文末記号（＝抑揚）だけになることがある
                        if (p.Key == "疑問" && def.Grammar.Question == QuestionStyle.VerbFronting) continue;
                        ok = false;
                        w.Append($"  {s}: 「{p.Key}」が区別できない ({baseText})\n");
                    }
                }
                var desire = T(MeaningFrame.Make(SpeechAct.Desire, Concepts.EAT).Agent(Concepts.I).Patient(Concepts.FISH));
                var request = T(MeaningFrame.Make(SpeechAct.Request, Concepts.EAT).Agent(Concepts.I).Patient(Concepts.FISH));
                if (StripPunct(desire) == StripPunct(request))
                {
                    ok = false;
                    w.Append($"  {s}: 希望と依頼が区別できない\n");
                }
            }
            return ok;
        }

        private static string StripPunct(string s) => s.TrimEnd('.', '!', '?');

        private static bool TestConsistency(StringBuilder w)
        {
            bool ok = true;
            foreach (var s in SampleSeeds)
            {
                var def = LanguageDefinition.Generate(Seed(s), 1);
                var r = new SentenceRenderer(def);
                var f1 = MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.FISH);
                var f2 = MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.FISH);
                var f3 = MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(Concepts.FRUIT);
                var a = r.RenderFrame(f1);
                var b = r.RenderFrame(f2);
                var c = r.RenderFrame(f3);
                if (a.text != b.text) { ok = false; w.Append($"  {s}: 同じ意味で文が変わる\n"); }
                // 「魚」→「果物」の差し替えで、変わるのはその語だけ（比較で語の意味を推理できる §6.2）
                int changed = 0;
                if (a.tokens.Count != c.tokens.Count) changed = 99;
                else for (int i = 0; i < a.tokens.Count; i++) if (a.tokens[i].surface != c.tokens[i].surface) changed++;
                if (changed != 1) { ok = false; w.Append($"  {s}: 名詞の差し替えで{changed}語が変化\n"); }
            }
            return ok;
        }

        private static bool TestNumbers(StringBuilder w)
        {
            bool ok = true;
            foreach (var s in SampleSeeds)
            {
                var def = LanguageDefinition.Generate(Seed(s), 1);
                var r = new SentenceRenderer(def);
                var seen = new HashSet<string>();
                for (int n = 1; n <= 300; n++)
                {
                    if (!seen.Add(r.RenderNumber(n)))
                    {
                        ok = false;
                        w.Append($"  {s}: {n} が他の数と同じ表記\n");
                        break;
                    }
                }
                w.Append($"  {s}: {def.Grammar.NumberBase}進法  7={r.RenderNumber(7)} / 42={r.RenderNumber(42)} / 250={r.RenderNumber(250)}\n");
            }
            return ok;
        }
    }
}
