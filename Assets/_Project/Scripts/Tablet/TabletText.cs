using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 文字列・記録まわりの補助。
    /// 宇宙語は「聞こえた綴り（surface）」だけを扱い、内部ID・意味データには触れない。
    /// 状況の説明（表情・身振り・場所・日時）は主人公が見た事実として主人公の言語で書いてよい（タブレット仕様 §3.2, §5.2）。
    /// </summary>
    internal static class TabletText
    {
        private const string Punct = ".,!?;:。、！？・…「」『』()（）\"'~〜-";

        /// <summary>辞書の見出し照合用（前後の記号を除き、小文字化）。</summary>
        public static string Normalize(string s)
        {
            var c = CleanSurface(s);
            return string.IsNullOrEmpty(c) ? c : c.ToLowerInvariant();
        }

        /// <summary>前後の空白・記号を取り除いた綴り。</summary>
        public static string CleanSurface(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            int a = 0, b = s.Length - 1;
            while (a <= b && (Punct.IndexOf(s[a]) >= 0 || char.IsWhiteSpace(s[a]))) a++;
            while (b >= a && (Punct.IndexOf(s[b]) >= 0 || char.IsWhiteSpace(s[b]))) b--;
            return a > b ? "" : s.Substring(a, b - a + 1);
        }

        /// <summary>
        /// 発話を「聞こえた語のまとまり」に分ける。接辞（attachedToPrevious）は前の語とつなげ、
        /// 画面の綴りと同じ区切りにする（文法の区切りを勝手に明かさない）。トークンが無ければ空白で区切る。
        /// </summary>
        public static List<string> Words(UtteranceRecord r)
        {
            var list = new List<string>();
            if (r == null) return list;
            if (r.tokens != null && r.tokens.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var t in r.tokens)
                {
                    if (t == null || string.IsNullOrEmpty(t.surface)) continue;
                    if (!t.attachedToPrevious && sb.Length > 0)
                    {
                        list.Add(sb.ToString());
                        sb.Length = 0;
                    }
                    sb.Append(t.surface);
                }
                if (sb.Length > 0) list.Add(sb.ToString());
                return list;
            }
            if (!string.IsNullOrEmpty(r.text))
            {
                foreach (var w in r.text.Split(new[] { ' ', '\t', '\n', '　' }, System.StringSplitOptions.RemoveEmptyEntries)) list.Add(w);
            }
            return list;
        }

        /// <summary>0=含まない 1=語として一致 2=語の一部として含む。</summary>
        public static int MatchKind(UtteranceRecord r, string normalizedKey, bool allowPartial)
        {
            int best = 0;
            foreach (var w in Words(r))
            {
                var n = Normalize(w);
                if (n == normalizedKey) return 1;
                if (allowPartial && !string.IsNullOrEmpty(n) && n.Contains(normalizedKey)) best = 2;
            }
            return best;
        }

        public static UtteranceRecord CloneRecord(UtteranceRecord r)
        {
            if (r == null) return null;
            var c = JsonUtility.FromJson<UtteranceRecord>(JsonUtility.ToJson(r));
            if (c.tokens == null) c.tokens = new List<SurfaceToken>();
            if (c.gestures == null) c.gestures = new List<GestureCue>();
            if (c.relatedIds == null) c.relatedIds = new List<string>();
            return c;
        }

        /// <summary>
        /// 単語列の差分（LCS）。a の各語が b と共通の並びに入っていなければ true（比較で強調表示する語）。
        /// </summary>
        public static bool[] DiffMask(List<string> a, List<string> b)
        {
            int n = a.Count, m = b.Count;
            var dp = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
                for (int j = m - 1; j >= 0; j--)
                    dp[i, j] = Normalize(a[i]) == Normalize(b[j]) ? dp[i + 1, j + 1] + 1 : Mathf.Max(dp[i + 1, j], dp[i, j + 1]);
            var diff = new bool[n];
            for (int k = 0; k < n; k++) diff[k] = true;
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (Normalize(a[x]) == Normalize(b[y]))
                {
                    diff[x] = false;
                    x++;
                    y++;
                }
                else if (dp[x + 1, y] >= dp[x, y + 1]) x++;
                else y++;
            }
            return diff;
        }

        public static List<string> SplitTags(string s)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(s)) return list;
            foreach (var t in s.Split(new[] { ',', '、', '，', ' ', '　' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var x = t.Trim();
                if (x.Length > 0 && !list.Contains(x)) list.Add(x);
            }
            return list;
        }

        public static string JoinTags(List<string> tags) => tags == null ? "" : string.Join(", ", tags);

        // ---- 日時・場所（主人公の記録） ----

        public static string DayLabel(int day) => Loc.Format("{0}日目", "Day {0}", day);

        public static string DayTime(int day, int minuteOfDay)
        {
            return Loc.Format("{0}日目 {1:00}:{2:00}", "Day {0} {1:00}:{2:00}", day, minuteOfDay / 60, minuteOfDay % 60);
        }

        public struct PlaceName
        {
            public string ja;
            public string en;
            public override string ToString() => Loc.T(ja, en);
        }

        public static PlaceName PlaceOf(string areaId, Vector3 pos)
        {
            var g = Game.Instance;
            if (g != null && !string.IsNullOrEmpty(areaId) && areaId != AreaIds.Overworld)
            {
                var a = g.Areas.Get(areaId);
                if (a != null) return new PlaceName { ja = a.NameJa, en = a.NameEn };
                if (areaId == AreaIds.PlayerHouse) return new PlaceName { ja = "自宅", en = "Home" };
                if (areaId == AreaIds.Shop) return new PlaceName { ja = "商店", en = "Shop" };
                if (AreaIds.IsMine(areaId)) return new PlaceName { ja = $"鉱山 {AreaIds.MineFloorOf(areaId)}階", en = $"Mine F{AreaIds.MineFloorOf(areaId)}" };
                if (areaId.StartsWith("house:")) return new PlaceName { ja = "住民の家", en = "A resident's house" };
            }
            var layout = g != null ? g.Layout : null;
            if (layout != null)
            {
                var region = layout.GetAreaAt(pos);
                if (region != null && (!string.IsNullOrEmpty(region.nameJa) || !string.IsNullOrEmpty(region.nameEn)))
                    return new PlaceName { ja = region.nameJa, en = region.nameEn };
            }
            return new PlaceName { ja = "村の外", en = "Outdoors" };
        }

        public static PlaceName PlaceOfRecord(UtteranceRecord r)
        {
            if (r == null) return new PlaceName { ja = "?", en = "?" };
            return PlaceOf(r.areaId, new Vector3(r.posX, r.posY, r.posZ));
        }

        // ---- 表情・身振り（見た様子の記述。意味の解釈はしない） ----

        public static string ExpressionLabel(Expression e)
        {
            switch (e)
            {
                case Expression.Happy: return Loc.T("にこにこ", "smiling");
                case Expression.VeryHappy: return Loc.T("満面の笑み", "beaming");
                case Expression.Sad: return Loc.T("しょんぼり", "downcast");
                case Expression.Crying: return Loc.T("涙を流していた", "in tears");
                case Expression.Angry: return Loc.T("むっとした顔", "frowning hard");
                case Expression.Surprised: return Loc.T("目を丸くしていた", "wide-eyed");
                case Expression.Confused: return Loc.T("きょとんとしていた", "puzzled look");
                case Expression.Shy: return Loc.T("もじもじしていた", "fidgeting");
                case Expression.Sleepy: return Loc.T("眠そう", "sleepy-eyed");
                case Expression.Love: return Loc.T("うっとりしていた", "dreamy look");
                case Expression.Worried: return Loc.T("困ったような顔", "brow furrowed");
                case Expression.Excited: return Loc.T("そわそわ・わくわく", "bouncing");
                case Expression.Disgusted: return Loc.T("顔をしかめていた", "grimacing");
                case Expression.Thinking: return Loc.T("考えこむ顔", "pondering look");
                default: return Loc.T("ふつうの顔", "plain face");
            }
        }

        public static string GestureLabel(GestureCue g)
        {
            if (g == null) return "";
            switch (g.gesture)
            {
                case Gesture.Wave: return Loc.T("手を振った", "waved");
                case Gesture.Nod: return Loc.T("うなずいた", "nodded");
                case Gesture.ShakeHead: return Loc.T("首を横に振った", "shook head");
                case Gesture.PointAt: return Loc.Format("{0}を指さした", "pointed at {0}", TargetLabel(g.target));
                case Gesture.ShowNumber: return Loc.Format("指で{0}を示した", "showed {0} with fingers", g.number);
                case Gesture.Jump: return Loc.T("ぴょんと跳ねた", "hopped");
                case Gesture.Bow: return Loc.T("お辞儀した", "bowed");
                case Gesture.Shrug: return Loc.T("肩をすくめた", "shrugged");
                case Gesture.Hug: return Loc.T("抱きしめるしぐさ", "hugging motion");
                case Gesture.OfferItem: return Loc.Format("{0}を差し出した", "held out {0}", TargetLabel(g.target));
                case Gesture.Eat: return Loc.T("食べるしぐさ", "eating motion");
                case Gesture.Cry: return Loc.T("泣いた", "cried");
                case Gesture.Clap: return Loc.T("手を叩いた", "clapped");
                case Gesture.Spin: return Loc.T("くるっと回った", "spun around");
                case Gesture.Think: return Loc.T("首をかしげた", "tilted head");
                case Gesture.Beckon: return Loc.T("手招きした", "beckoned");
                case Gesture.Shoo: return Loc.T("手で払うしぐさ", "shooing motion");
                case Gesture.Hold: return Loc.T("何かを抱えるしぐさ", "holding motion");
                case Gesture.LookUp: return Loc.T("空を見上げた", "looked up at the sky");
                case Gesture.Shiver: return Loc.T("ぶるぶる震えた", "shivered");
                case Gesture.Stretch: return Loc.T("伸びをした", "stretched");
                case Gesture.Dance: return Loc.T("踊った", "danced");
                default: return "";
            }
        }

        /// <summary>身振りの対象（見えた物）。アイテムなら名前、知っている住民なら辞典の呼び名。</summary>
        public static string TargetLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return Loc.T("何か", "something");
            if (ItemRegistry.Exists(id)) return ItemRegistry.NameOf(id);
            var sys = TabletSystem.Instance;
            if (sys != null && sys.FindResidentPage(id) != null) return sys.ResidentLabel(id);
            if (Game.Instance != null && Game.Instance.Service<IResidentDirectory>()?.Get(id) != null) return Loc.T("だれか", "someone");
            if (Game.Instance != null && id == Game.Instance.LocalPlayerId) return Loc.T("自分", "me");
            return Loc.T("何か", "something");
        }

        /// <summary>記録の「その場にあった物」（アイテムは名前、住民は辞典の呼び名。分からない物は出さない）。</summary>
        public static string RelatedLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (ItemRegistry.Exists(id)) return ItemRegistry.NameOf(id);
            var sys = TabletSystem.Instance;
            if (sys != null && sys.FindResidentPage(id) != null) return sys.ResidentLabel(id);
            return null;
        }

        public static string ChannelLabel(SpeechChannel c)
        {
            switch (c)
            {
                case SpeechChannel.Direct: return Loc.T("話しかけられた", "spoken to me");
                case SpeechChannel.Overheard: return Loc.T("聞こえてきた", "overheard");
                case SpeechChannel.ManepiPlayback: return Loc.T("マネピの再現", "Manepi's mimicry");
                case SpeechChannel.Sign: return Loc.T("看板・掲示", "sign");
                case SpeechChannel.Letter: return Loc.T("手紙", "letter");
                case SpeechChannel.Broadcast: return Loc.T("村への呼びかけ", "village announcement");
                default: return "";
            }
        }

        /// <summary>表情・身振りなど見えた様子のまとめ（主人公の記録）。</summary>
        public static string NonverbalSummary(UtteranceRecord r)
        {
            if (r == null) return "";
            var sb = new StringBuilder();
            sb.Append(ExpressionLabel(r.expression));
            if (r.gestures != null)
            {
                foreach (var g in r.gestures)
                {
                    var s = GestureLabel(g);
                    if (string.IsNullOrEmpty(s)) continue;
                    sb.Append(Loc.T("・", ", "));
                    sb.Append(s);
                }
            }
            return sb.ToString();
        }

        public static string KindLabel(TabletMaterialKind k)
        {
            switch (k)
            {
                case TabletMaterialKind.VoiceCard: return Loc.T("音声カード", "Voice cards");
                case TabletMaterialKind.Conversation: return Loc.T("会話", "Conversations");
                case TabletMaterialKind.Photo: return Loc.T("写真", "Photos");
                case TabletMaterialKind.SalesRecord: return Loc.T("売上の記録", "Sales");
                case TabletMaterialKind.QuestRequest: return Loc.T("依頼", "Requests");
                case TabletMaterialKind.Letter: return Loc.T("手紙", "Letters");
                case TabletMaterialKind.Sign: return Loc.T("看板", "Signs");
                case TabletMaterialKind.Recipe: return Loc.T("レシピ", "Recipes");
                case TabletMaterialKind.Forecast: return Loc.T("天気の伝言", "Forecasts");
                default: return Loc.T("メモ", "Notes");
            }
        }

        public static string MarkLabel(ConfidenceMark m)
        {
            switch (m)
            {
                case ConfidenceMark.Probably: return Loc.T("たぶん", "Probably");
                case ConfidenceMark.Confident: return Loc.T("自信あり", "Confident");
                case ConfidenceMark.Unsure: return Loc.T("？", "?");
                case ConfidenceMark.Rejected: return Loc.T("ちがった", "Rejected");
                default: return Loc.T("仮説", "Guess");
            }
        }

        public static Color MarkColor(ConfidenceMark m)
        {
            switch (m)
            {
                case ConfidenceMark.Probably: return new Color(0.55f, 0.8f, 1f);
                case ConfidenceMark.Confident: return new Color(0.5f, 0.95f, 0.55f);
                case ConfidenceMark.Unsure: return new Color(1f, 0.85f, 0.4f);
                case ConfidenceMark.Rejected: return new Color(0.65f, 0.6f, 0.65f);
                default: return new Color(0.9f, 0.9f, 1f);
            }
        }

        /// <summary>品詞の推測の選択肢（本人用）。</summary>
        public static readonly string[] PosKeys = { "", "noun", "verb", "adj", "adv", "affix", "name", "number", "greeting", "other" };

        public static string PosLabel(string key)
        {
            switch (key)
            {
                case "noun": return Loc.T("名詞？", "Noun?");
                case "verb": return Loc.T("動詞？", "Verb?");
                case "adj": return Loc.T("形容詞？", "Adjective?");
                case "adv": return Loc.T("副詞？", "Adverb?");
                case "affix": return Loc.T("語尾・接辞？", "Affix?");
                case "name": return Loc.T("名前？", "Name?");
                case "number": return Loc.T("数？", "Number?");
                case "greeting": return Loc.T("あいさつ？", "Greeting?");
                case "other": return Loc.T("その他", "Other");
                default: return Loc.T("未定", "Unknown");
            }
        }

        public static string EntryKindLabel(DictEntryKind k)
        {
            switch (k)
            {
                case DictEntryKind.Part: return Loc.T("語の一部", "Word part");
                case DictEntryKind.Phrase: return Loc.T("文章・言い回し", "Phrase");
                default: return Loc.T("単語", "Word");
            }
        }

        public static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }
    }
}
