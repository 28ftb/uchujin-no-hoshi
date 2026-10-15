#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Language
{
    /// <summary>
    /// 開発者向けの宇宙語デバッグ窓（F12。UNITY_EDITOR 限定＝通常プレイ・ビルドでは絶対に表示されない）。
    /// その星の文法パラメータ・語彙（概念ID→綴り）・任意の意味の変換結果・別シードとの比較・自己テストを確認できる。
    /// ここには内部の正解（概念ID）が表示されるため、プレイヤー向けの画面から呼んではならない。
    /// </summary>
    public sealed class LanguageDebugWindow : UIWindow
    {
        private static LanguageDebugWindow instance;

        private readonly LanguageSystem system;
        private int tab;
        private string filter = "";
        private string otherSeed = "BETA";
        private string selfTestReport;

        // 変換テスト用の入力
        private int actIndex;
        private string predicate = Concepts.WANT;
        private string agent = Concepts.I;
        private string patient = Concepts.FISH;
        private string patientAdj = Concepts.RED;
        private string recipient = "";
        private string location = "";
        private string timeWord = "";
        private string adverb = "";
        private string qtyText = "0";
        private bool negative, plural;
        private int tenseIndex;

        public static void Toggle(LanguageSystem sys)
        {
            if (instance != null && instance.IsOpen)
            {
                instance.Close();
                return;
            }
            instance = new LanguageDebugWindow(sys);
            UIManager.Open(instance);
        }

        private LanguageDebugWindow(LanguageSystem sys)
        {
            system = sys;
        }

        public override string Title => "宇宙語デバッグ（開発者専用 / Editor only）";
        public override Vector2 Size => new Vector2(1280, 900);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            var lang = system.Language;
            GUILayout.BeginHorizontal();
            string[] tabs = { "文法・音韻", "語彙", "変換テスト", "シード比較", "自己テスト", "会話ログ（内部）" };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], tab == i ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Label($"シード {lang.PlanetSeed} / 言語生成 v{lang.Version} / 指紋 {lang.Fingerprint} / 星の名前 {system.PlanetName}", UIStyles.LabelDim);
            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: GUILayout.Label(lang.Describe(), UIStyles.LabelWrap); break;
                case 1: DrawLexicon(lang); break;
                case 2: DrawRenderTest(lang); break;
                case 3: DrawCompare(lang); break;
                case 4: DrawSelfTest(); break;
                case 5: DrawLog(); break;
            }
            GUILayout.EndScrollView();
        }

        private void DrawLexicon(LanguageDefinition lang)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("絞り込み", UIStyles.Label, GUILayout.Width(100));
            filter = GUILayout.TextField(filter ?? "", UIStyles.TextField, GUILayout.Width(300));
            GUILayout.EndHorizontal();
            var list = new List<LexWord>(lang.Lexicon.AllWords);
            list.Sort((a, b) => string.CompareOrdinal(a.Concept, b.Concept));
            int shown = 0;
            foreach (var w in list)
            {
                if (!string.IsNullOrEmpty(filter) && w.Concept.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0
                    && w.Surface.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (++shown > 400) { GUILayout.Label("…（400件まで表示）", UIStyles.LabelDim); break; }
                GUILayout.Label($"{w.Concept,-28} {w.Surface,-14} [{string.Join("·", w.Syllables ?? new string[0])}] {(w.IsBase ? "基本" : "動的")} 試行{w.Attempt}  文字:{lang.Script.Word(w.Surface)}", UIStyles.LabelSmall);
            }
        }

        private static readonly SpeechAct[] Acts = (SpeechAct[])System.Enum.GetValues(typeof(SpeechAct));

        private void DrawRenderTest(LanguageDefinition lang)
        {
            GUILayout.Label("任意の意味（概念ID）を入力して変換結果を確認します。", UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("発話行為: " + Acts[actIndex], UIStyles.ButtonSmall)) actIndex = (actIndex + 1) % Acts.Length;
            if (GUILayout.Button("時制: " + (Tense)tenseIndex, UIStyles.ButtonSmall)) tenseIndex = (tenseIndex + 1) % 3;
            negative = GUILayout.Toggle(negative, " 否定", UIStyles.Toggle);
            plural = GUILayout.Toggle(plural, " 目的語を複数", UIStyles.Toggle);
            GUILayout.EndHorizontal();
            predicate = Field("述語", predicate);
            agent = Field("主語(agent)", agent);
            patient = Field("目的語(patient)", patient);
            patientAdj = Field("目的語の形容詞", patientAdj);
            qtyText = Field("目的語の数", qtyText);
            recipient = Field("受け手", recipient);
            location = Field("場所", location);
            timeWord = Field("時の言葉", timeWord);
            adverb = Field("副詞", adverb);

            var f = MeaningFrame.Make(Acts[actIndex], string.IsNullOrEmpty(predicate) ? null : predicate);
            f.tense = (Tense)tenseIndex;
            f.negative = negative;
            if (!string.IsNullOrEmpty(agent)) f.Agent(agent);
            if (!string.IsNullOrEmpty(patient))
            {
                var p = MeaningArg.Of(patient);
                if (!string.IsNullOrEmpty(patientAdj)) p.Adj(patientAdj);
                if (int.TryParse(qtyText, out int q) && q > 0) p.Qty(q);
                if (plural) p.Plural();
                f.Patient(p);
            }
            if (!string.IsNullOrEmpty(recipient)) f.Recipient(recipient);
            if (!string.IsNullOrEmpty(location)) f.At(location);
            if (!string.IsNullOrEmpty(timeWord)) f.When(timeWord);
            if (!string.IsNullOrEmpty(adverb)) f.Adverb(adverb);
            var r = system.Renderer.RenderFrame(f);
            GUILayout.Space(8);
            GUILayout.Label("宇宙語: " + r.text, UIStyles.LabelAlienLarge);
            GUILayout.Label("宇宙文字: " + lang.Script.Text(r.text), UIStyles.LabelAlien);
            var sb = new StringBuilder();
            foreach (var t in r.tokens) sb.Append(t.attachedToPrevious ? "+" : " ").Append(t.surface).Append('(').Append(t.internalId).Append(t.grammatical ? ",文法" : "").Append(')');
            GUILayout.Label("語の内訳:" + sb, UIStyles.LabelWrap);
            if (GUILayout.Button("この文を自分の頭上で話させる（表示テスト）", UIStyles.ButtonSmall, GUILayout.Width(460)))
            {
                var u = new Utterance("debug", "debug").Say(f).Face(Expression.Happy).With(Gesture.Wave);
                system.Speak(u, Game.Instance.LocalPlayer?.Avatar, SpeechChannel.Direct, "resident");
            }
            GUILayout.Space(10);
            GUILayout.Label("代表的な文：", UIStyles.LabelBold);
            foreach (var sf in LanguageSelfTest.SampleFrames())
                GUILayout.Label(system.Renderer.RenderFrame(sf).text, UIStyles.LabelAlien);
            GUILayout.Label("数: 1〜12 = " + Numbers(1, 12) + " / 100 = " + system.RenderNumber(100) + " / 1234 = " + system.RenderNumber(1234), UIStyles.LabelWrap);
        }

        private string Numbers(int from, int to)
        {
            var sb = new StringBuilder();
            for (int i = from; i <= to; i++) sb.Append(system.RenderNumber(i)).Append(i < to ? ", " : "");
            return sb.ToString();
        }

        private static string Field(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UIStyles.LabelSmall, GUILayout.Width(200));
            value = GUILayout.TextField(value ?? "", UIStyles.TextField, GUILayout.Width(380));
            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawCompare(LanguageDefinition lang)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("比較するシード", UIStyles.Label, GUILayout.Width(180));
            otherSeed = GUILayout.TextField(otherSeed ?? "", UIStyles.TextField, GUILayout.Width(300));
            GUILayout.EndHorizontal();
            var other = LanguageDefinition.Generate(PlanetInfo.SeedFromText(otherSeed), lang.Version);
            var same = LanguageDefinition.Generate(lang.PlanetSeed, lang.Version);
            GUILayout.Label("同じシードで再生成した指紋: " + same.Fingerprint + (same.Fingerprint == lang.Fingerprint ? "（一致）" : "（不一致！）"), UIStyles.LabelBold);
            var ra = new SentenceRenderer(lang);
            var rb = new SentenceRenderer(other);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(600));
            GUILayout.Label("この星", UIStyles.LabelBold);
            GUILayout.Label(lang.Grammar.Describe(), UIStyles.LabelSmall);
            foreach (var f in LanguageSelfTest.SampleFrames()) GUILayout.Label(ra.RenderFrame(f).text, UIStyles.LabelAlien);
            GUILayout.EndVertical();
            GUILayout.BeginVertical(GUILayout.Width(600));
            GUILayout.Label("シード " + otherSeed, UIStyles.LabelBold);
            GUILayout.Label(other.Grammar.Describe(), UIStyles.LabelSmall);
            foreach (var f in LanguageSelfTest.SampleFrames()) GUILayout.Label(rb.RenderFrame(f).text, UIStyles.LabelAlien);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawSelfTest()
        {
            if (GUILayout.Button("自己テストを実行（LanguageSelfTest.RunAll）", UIStyles.Button, GUILayout.Width(520)))
            {
                LanguageSelfTest.RunAll(out selfTestReport);
                Debug.Log(selfTestReport);
            }
            if (!string.IsNullOrEmpty(selfTestReport)) GUILayout.Label(selfTestReport, UIStyles.LabelWrap);
        }

        private void DrawLog()
        {
            var recs = system.RecentRecords;
            GUILayout.Label($"会話ログ {recs.Count}件（内部の意味付き。開発者専用）", UIStyles.LabelDim);
            for (int i = recs.Count - 1; i >= 0 && i >= recs.Count - 60; i--)
            {
                var r = recs[i];
                var sb = new StringBuilder();
                if (r.meaning?.sentences != null)
                    foreach (var s in r.meaning.sentences)
                        if (s != null) sb.Append('[').Append(s.act).Append(' ').Append(s.predicate).Append(s.negative ? " NEG" : "").Append(' ').Append(s.tense).Append("] ");
                GUILayout.Label($"D{r.day} {r.minuteOfDay / 60:00}:{r.minuteOfDay % 60:00} {r.channel} {r.speakerKind}:{r.speakerId}  「{r.text}」  {sb} tag={r.contextTag}", UIStyles.LabelSmall);
            }
        }
    }
}
#endif
