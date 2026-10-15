using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Quests
{
    /// <summary>UIの小物（顔・絵・身振り・表情の「見たまま」の表示）。正解の訳は出さない。</summary>
    internal static class QuestUI
    {
        private static GUIStyle glyph;
        private static GUIStyle ink;

        public static GUIStyle Ink
        {
            get
            {
                if (ink == null)
                {
                    ink = new GUIStyle(UIStyles.LabelAlien) { wordWrap = true, fontSize = 22 };
                    ink.normal.textColor = new Color(0.15f, 0.2f, 0.38f);
                }
                return ink;
            }
        }

        private static GUIStyle Glyph(int size, Color c)
        {
            if (glyph == null) glyph = new GUIStyle(UIStyles.LabelCenter) { alignment = TextAnchor.MiddleCenter };
            glyph.fontSize = size;
            glyph.normal.textColor = c;
            return glyph;
        }

        public static string LineageGlyph(AlienLineage l)
        {
            switch (l)
            {
                case AlienLineage.Round: return "●";
                case AlienLineage.Mochi: return "◆";
                case AlienLineage.Plush: return "★";
                case AlienLineage.ChibiAnimal: return "▲";
                case AlienLineage.Floating: return "◎";
                case AlienLineage.MultiArm: return "※";
                case AlienLineage.StarChild: return "☆";
                case AlienLineage.Lifestyle: return "■";
                case AlienLineage.Expressive: return "◇";
                default: return "△";
            }
        }

        /// <summary>住民の顔（色＋形）。名前は自動では分からない（住民 §12）。</summary>
        public static void Face(Rect r, Color c, AlienLineage l)
        {
            UIStyles.FillRect(r, new Color(0f, 0f, 0f, 0.25f));
            var inner = new Rect(r.x + 3, r.y + 3, r.width - 6, r.height - 6);
            UIStyles.FillRect(inner, c);
            GUI.Label(inner, LineageGlyph(l), Glyph(Mathf.RoundToInt(r.height * 0.55f), Color.white));
        }

        public static string ShapeGlyph(PrimitiveType t)
        {
            switch (t)
            {
                case PrimitiveType.Sphere: return "●";
                case PrimitiveType.Cube: return "■";
                case PrimitiveType.Capsule: return "◆";
                case PrimitiveType.Cylinder: return "▲";
                default: return "□";
            }
        }

        /// <summary>住民が描いた絵（色と形だけ。何の絵かはプレイヤーが判断する：§6.2）。</summary>
        public static void Picture(Rect r, ItemDefinition def)
        {
            UIStyles.FillRect(r, new Color(1f, 0.98f, 0.9f));
            if (def == null) return;
            GUI.Label(r, ShapeGlyph(def.Shape), Glyph(Mathf.RoundToInt(r.height * 0.7f), def.Color));
        }

        public static string FaceLabel(Expression e)
        {
            switch (e)
            {
                case Expression.Happy: return Loc.T("にこにこ", "smiling");
                case Expression.VeryHappy: return Loc.T("大喜び", "overjoyed");
                case Expression.Sad: return Loc.T("しょんぼり", "drooping");
                case Expression.Crying: return Loc.T("涙ぐむ", "teary");
                case Expression.Angry: return Loc.T("むっとした", "cross");
                case Expression.Surprised: return Loc.T("目を丸く", "wide-eyed");
                case Expression.Confused: return Loc.T("首をかしげる", "head tilted");
                case Expression.Shy: return Loc.T("もじもじ", "bashful");
                case Expression.Sleepy: return Loc.T("眠そう", "sleepy");
                case Expression.Love: return Loc.T("うっとり", "starry-eyed");
                case Expression.Worried: return Loc.T("困り顔", "troubled");
                case Expression.Excited: return Loc.T("わくわく", "excited");
                case Expression.Disgusted: return Loc.T("顔をしかめる", "grimacing");
                case Expression.Thinking: return Loc.T("考え込む", "thinking");
                default: return Loc.T("ふつう", "plain");
            }
        }

        /// <summary>身振りの「見たまま」の説明（意味は書かない）。</summary>
        public static string GestureLabel(GestureCue g, QuestSystem sys)
        {
            switch (g.gesture)
            {
                case Gesture.Wave: return Loc.T("手を振った", "waved");
                case Gesture.Nod: return Loc.T("うなずいた", "nodded");
                case Gesture.ShakeHead: return Loc.T("首を振った", "shook head");
                case Gesture.PointAt: return Loc.Format("指さした（{0}）", "pointed ({0})", sys.PointedLabel(g.target));
                case Gesture.ShowNumber: return Loc.Format("指で {0} を示した", "showed {0} with fingers", g.number);
                case Gesture.Jump: return Loc.T("跳ねた", "jumped");
                case Gesture.Bow: return Loc.T("お辞儀した", "bowed");
                case Gesture.Shrug: return Loc.T("肩をすくめた", "shrugged");
                case Gesture.Hug: return Loc.T("抱きついた", "hugged");
                case Gesture.OfferItem: return Loc.Format("差し出して見せた（{0}）", "held out ({0})", ItemRegistry.GetOrPlaceholder(g.target).Name);
                case Gesture.Eat: return Loc.T("食べるしぐさ", "mimed eating");
                case Gesture.Cry: return Loc.T("泣いた", "cried");
                case Gesture.Clap: return Loc.T("手をたたいた", "clapped");
                case Gesture.Spin: return Loc.T("くるっと回った", "spun around");
                case Gesture.Think: return Loc.T("考えるしぐさ", "looked thoughtful");
                case Gesture.Beckon: return Loc.T("手招きした", "beckoned");
                case Gesture.Shoo: return Loc.T("あっちへのしぐさ", "shooed");
                case Gesture.Hold: return Loc.T("何かを抱えるしぐさ", "mimed holding");
                case Gesture.LookUp: return Loc.T("空を見上げた", "looked up at the sky");
                case Gesture.Shiver: return Loc.T("震えた", "shivered");
                case Gesture.Stretch: return Loc.T("伸びをした", "stretched");
                case Gesture.Dance: return Loc.T("踊った", "danced");
                default: return "";
            }
        }

        public static string KindLabel(QuestKind k)
        {
            switch (k)
            {
                case QuestKind.Board: return Loc.T("掲示板", "Board");
                case QuestKind.Spontaneous: return Loc.T("手伝い", "Helping out");
                case QuestKind.Story: return Loc.T("出来事", "Episode");
                case QuestKind.VillageEvent: return Loc.T("集まり", "Gathering");
                default: return Loc.T("お願い", "Request");
            }
        }

        /// <summary>進行状態（翻訳の正しさとは別：§11.1）。</summary>
        public static string StatusLabel(QuestStatus s, int outcome, QuestKind kind, bool heardOnly)
        {
            if (kind == QuestKind.Spontaneous) return s == QuestStatus.Completed ? Loc.T("手伝った", "Helped") : Loc.T("気になっている", "On my mind");
            if (kind == QuestKind.VillageEvent) return s == QuestStatus.Completed ? Loc.T("終わった", "Over") : Loc.T("これから／その最中", "Upcoming / ongoing");
            switch (s)
            {
                case QuestStatus.Hinted: return heardOnly ? Loc.T("まだ返事をしていない", "Not answered yet") : Loc.T("気配がある", "Something's up");
                case QuestStatus.Active: return Loc.T("進行中", "In progress");
                case QuestStatus.Completed: return outcome == 3 ? Loc.T("住民が自分で何とかしたようだ", "They seem to have managed on their own") : Loc.T("終わった", "Done");
                case QuestStatus.Expired: return Loc.T("住民が自分で何とかしたようだ", "They seem to have managed on their own");
                case QuestStatus.Declined: return Loc.T("断った／やめた", "Declined / stopped");
                case QuestStatus.Failed: return Loc.T("うまくいかなかった", "Didn't work out");
                default: return "";
            }
        }
    }

    /// <summary>
    /// 日替わり掲示板（§6.2）。紙には宇宙人の文字（ISpeechService.Render、看板と同様に翻訳しない）、
    /// 依頼主の顔（色）、依頼主が描いた場合だけ対象物の絵。紙を取ると引き受け。
    /// </summary>
    internal sealed class BulletinBoardWindow : UIWindow
    {
        private readonly QuestSystem sys;
        private readonly Dictionary<string, string> textCache = new Dictionary<string, string>();

        public BulletinBoardWindow(QuestSystem sys) { this.sys = sys; }

        public override string Title => Loc.T("掲示板", "Bulletin board");
        public override Vector2 Size => new Vector2(1180, 760);
        public override bool PausesTimeInSolo => true;

        private string TextOf(BoardPaper p)
        {
            if (textCache.TryGetValue(p.id, out var t)) return t;
            var speech = sys.Speech;
            if (speech == null) t = "……";
            else
            {
                var r = speech.Render(sys.PaperUtterance(p));
                t = r != null ? r.text : "……";
                // 署名（宇宙語の名前。名前だと分かるかどうかはプレイヤー次第）
                var name = speech.ResidentName(p.residentId);
                if (!string.IsNullOrEmpty(name)) t += "\n　　― " + name;
            }
            textCache[p.id] = t;
            return t;
        }

        public override void DrawContents(Rect content)
        {
            var papers = new List<BoardPaper>();
            foreach (var p in sys.S.board) if (string.IsNullOrEmpty(p.takenBy)) papers.Add(p);
            if (papers.Count == 0)
            {
                GUILayout.Label(Loc.T("今は何も貼られていない。", "Nothing is posted right now."), UIStyles.LabelDim);
                return;
            }
            scroll = GUILayout.BeginScrollView(scroll);
            const int cols = 3;
            for (int i = 0; i < papers.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(papers.Count, i + cols); j++) DrawPaper(papers[j]);
                GUILayout.EndHorizontal();
                GUILayout.Space(10);
            }
            GUILayout.EndScrollView();
        }

        private void DrawPaper(BoardPaper p)
        {
            var rect = GUILayoutUtility.GetRect(360, 330, GUILayout.Width(360), GUILayout.Height(330));
            UIStyles.FillRect(rect, p.isNotice ? new Color(1f, 0.88f, 0.84f) : QuestTuning.PaperColor);
            UIStyles.FillRect(new Rect(rect.x + rect.width * 0.5f - 8, rect.y + 4, 16, 16), new Color(0.8f, 0.25f, 0.25f)); // 画びょう
            var r = sys.R(p.residentId);
            QuestUI.Face(new Rect(rect.x + 12, rect.y + 24, 48, 48), r != null ? r.MainColor : Color.gray, r != null ? r.Lineage : AlienLineage.Round);
            float textTop = rect.y + 80;
            float textHeight = 170;
            if (p.drewPicture && !string.IsNullOrEmpty(p.pictureItemId))
            {
                QuestUI.Picture(new Rect(rect.xMax - 92, rect.y + 24, 80, 80), ItemRegistry.Get(p.pictureItemId));
            }
            GUI.Label(new Rect(rect.x + 14, textTop, rect.width - 28, textHeight), TextOf(p), QuestUI.Ink);
            if (!p.isNotice)
            {
                if (GUI.Button(new Rect(rect.x + rect.width - 170, rect.yMax - 54, 156, 44), Loc.T("紙を取る", "Take the notice"), UIStyles.Button))
                    TakePaper(p);
            }
        }

        private void TakePaper(BoardPaper p)
        {
            var paper = p;
            var speech = sys.Speech;
            var u = sys.PaperUtterance(paper);
            WorldAuthority.Submit(new QuestTakePaperCommand { paperId = paper.id }, res =>
            {
                if (res == null || !res.Success) return;
                string qid = QuestSystem.BoardQuestId(paper.id);
                var entry = sys.BookNew(qid, QuestKind.Board, paper.residentId, qid);
                if (speech != null)
                {
                    var rec = speech.CreateRecord(u, SpeechChannel.Sign, "sign", sys.BoardPosition());
                    sys.BookAttach(entry, rec, BookScene.Board);
                }
                if (paper.drewPicture && !string.IsNullOrEmpty(paper.pictureItemId) && !entry.drawnItemIds.Contains(paper.pictureItemId))
                    entry.drawnItemIds.Add(paper.pictureItemId);
                sys.AddQuestMaterial(new QuestInstance { id = qid, kind = QuestKind.Board, residentId = paper.residentId }, entry);
                UIManager.Toast(Loc.T("紙を取った。依頼帳に写しておいた。", "Took the notice and copied it into the request book."));
            });
        }
    }

    /// <summary>
    /// 依頼帳アプリ（§11、AppId "requests"）。宇宙語のまま記録し、自動翻訳しない。
    /// 原文・依頼主の顔・日時・場所・見せられた物・身振り・状態＋プレイヤーのメモ。音声カードへの保存。
    /// </summary>
    public sealed class RequestBookApp : ITabletApp
    {
        private readonly QuestSystem sys;
        private string selectedId;
        private int filter;
        private Vector2 listScroll, detailScroll;

        public RequestBookApp(QuestSystem sys) { this.sys = sys; }

        public string AppId => "requests";
        public string Title => Loc.T("依頼帳", "Request book");
        public int Order => 40;
        public string IconGlyph => Loc.T("依", "Rq");
        public Color IconColor => new Color(0.95f, 0.75f, 0.45f);
        public bool IsAvailable => true;

        public void OnOpen()
        {
            if (selectedId == null && sys.K.entries.Count > 0) selectedId = sys.K.entries[sys.K.entries.Count - 1].id;
        }

        private bool PassFilter(RequestBookEntry e)
        {
            var s = sys.BookStatus(e, out _);
            bool open = s == QuestStatus.Active || s == QuestStatus.Hinted;
            switch (filter)
            {
                case 0: return open && !e.archived;
                case 1: return !open && !e.archived;
                case 2: return true;
                case 3: return e.starred;
                default: return true;
            }
        }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            string[] tabs = { Loc.T("進行中", "Open"), Loc.T("終わった", "Closed"), Loc.T("すべて", "All"), "★" };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], filter == i ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(34))) filter = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            DrawList(area.width * 0.34f, area.height - 50);
            GUILayout.Space(8);
            DrawDetail(area.height - 50);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawList(float width, float height)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(height));
            bool any = false;
            for (int i = sys.K.entries.Count - 1; i >= 0; i--)
            {
                var e = sys.K.entries[i];
                if (!PassFilter(e)) continue;
                any = true;
                var s = sys.BookStatus(e, out int outcome);
                GUILayout.BeginHorizontal(selectedId == e.id ? UIStyles.Box : GUIStyle.none);
                var fr = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                QuestUI.Face(fr, e.faceColor, e.lineage);
                string name = string.IsNullOrEmpty(e.playerNickname) ? Loc.T("（呼び名なし）", "(no nickname)") : e.playerNickname;
                string line = Loc.Format("{0}日目・{1}", "Day {0} · {1}", e.day, QuestUI.KindLabel(e.kind));
                if (GUILayout.Button(name + (e.starred ? " ★" : "") + "\n" + line + "  " + QuestUI.StatusLabel(s, outcome, e.kind, e.heardOnly), UIStyles.ButtonSmall))
                {
                    selectedId = e.id;
                    detailScroll = Vector2.zero;
                }
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("まだ記録がない。住民に話しかけてみよう。", "Nothing yet. Try talking to the residents."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawDetail(float height)
        {
            GUILayout.BeginVertical();
            var e = sys.BookGet(selectedId);
            if (e == null)
            {
                GUILayout.Label(Loc.T("左の一覧から選んでください。", "Choose an entry on the left."), UIStyles.LabelDim);
                GUILayout.EndVertical();
                return;
            }
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(height));

            // ---- 依頼主（顔・自分で付けた呼び名）----
            GUILayout.BeginHorizontal();
            var fr = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72), GUILayout.Height(72));
            QuestUI.Face(fr, e.faceColor, e.lineage);
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.T("呼び名（自分で付ける）", "Nickname (your own)"), UIStyles.LabelDim);
            e.playerNickname = GUILayout.TextField(e.playerNickname ?? "", UIStyles.TextField, GUILayout.Height(36));
            GUILayout.EndVertical();
            if (GUILayout.Button(e.starred ? "★" : "☆", UIStyles.Button, GUILayout.Width(56), GUILayout.Height(56))) e.starred = !e.starred;
            GUILayout.EndHorizontal();

            var status = sys.BookStatus(e, out int outcome);
            GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}　{3}　（{4}）", "Day {0} {1:00}:{2:00}  {3}  ({4})",
                e.day, e.minute / 60, e.minute % 60, Loc.T(e.placeJa, e.placeEn), QuestUI.KindLabel(e.kind)), UIStyles.Label);
            GUILayout.Label(Loc.T("状態：", "Status: ") + QuestUI.StatusLabel(status, outcome, e.kind, e.heardOnly), UIStyles.LabelBold);

            // ---- 実際に見せられた手掛かり（§4.2）----
            if (e.shownItemIds.Count > 0)
            {
                GUILayout.Label(Loc.T("見せられた物", "Things they showed me"), UIStyles.LabelDim);
                GUILayout.BeginHorizontal();
                foreach (var id in e.shownItemIds)
                {
                    var r = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
                    UIStyles.ItemIcon(r, new ItemStack(id, 1));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (e.drawnItemIds.Count > 0)
            {
                GUILayout.Label(Loc.T("添えてあった絵", "The drawing attached"), UIStyles.LabelDim);
                GUILayout.BeginHorizontal();
                foreach (var id in e.drawnItemIds)
                {
                    var r = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                    QuestUI.Picture(r, ItemRegistry.Get(id));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            if (e.shownNumbers.Count > 0)
                GUILayout.Label(Loc.T("指で示された数：", "Numbers shown with fingers: ") + string.Join(", ", e.shownNumbers), UIStyles.Label);
            if (e.pointedTargets.Count > 0)
            {
                var labels = new List<string>();
                foreach (var t in e.pointedTargets) labels.Add(sys.PointedLabel(t));
                GUILayout.Label(Loc.T("指さした先：", "Pointed at: ") + string.Join("、", labels), UIStyles.Label);
            }
            if (e.givenItemIds.Count > 0)
            {
                GUILayout.Label(Loc.T("自分が渡した物", "What I handed over"), UIStyles.LabelDim);
                GUILayout.BeginHorizontal();
                foreach (var id in e.givenItemIds)
                {
                    var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                    UIStyles.ItemIcon(r, new ItemStack(id, 1));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            // ---- 原文（宇宙語のまま）----
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("聞いた言葉・書かれていた文字", "What I heard / what was written"), UIStyles.LabelBold);
            var tablet = sys.Tablet;
            foreach (var bu in e.records)
            {
                GUILayout.BeginVertical(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}日目 {1:00}:{2:00}　{3}", "Day {0} {1:00}:{2:00}  {3}", bu.day, bu.minute / 60, bu.minute % 60, BookScene.Label(bu.scene)), UIStyles.LabelDim);
                DrawTokens(bu, tablet);
                var notes = new List<string>();
                notes.Add(Loc.T("表情：", "Face: ") + QuestUI.FaceLabel(bu.expression));
                foreach (var g in bu.gestures)
                {
                    var gl = QuestUI.GestureLabel(g, sys);
                    if (!string.IsNullOrEmpty(gl)) notes.Add(gl);
                }
                GUILayout.Label(string.Join(" / ", notes), UIStyles.LabelSmall);
                if (tablet != null && bu.record != null && !string.IsNullOrEmpty(bu.record.text))
                {
                    if (GUILayout.Button(Loc.T("音声カードに保存", "Save as voice card"), UIStyles.ButtonSmall, GUILayout.Width(220)))
                    {
                        tablet.SaveVoiceCard(bu.record, "依頼帳", "Request book");
                        UIManager.Toast(Loc.T("音声カードに保存しました", "Saved as a voice card"));
                    }
                }
                GUILayout.EndVertical();
            }
            if (e.records.Count == 0) GUILayout.Label(Loc.T("（言葉の記録はない）", "(no recorded words)"), UIStyles.LabelDim);

            // ---- 自分のメモ（推測。ゲームは訂正しない）----
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("自分のメモ（推測・仮説）", "My notes (guesses)"), UIStyles.LabelBold);
            e.playerMemo = GUILayout.TextArea(e.playerMemo ?? "", UIStyles.TextArea, GUILayout.MinHeight(110));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(e.archived ? Loc.T("一覧に戻す", "Unarchive") : Loc.T("しまう", "Archive"), UIStyles.ButtonSmall, GUILayout.Width(160))) e.archived = !e.archived;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        /// <summary>宇宙語の語ごとのボタン（押すと辞書の該当ページへ）。自分の推測訳だけを小さく添える。</summary>
        private void DrawTokens(BookUtterance bu, ITabletService tablet)
        {
            if (bu.surfaces == null || bu.surfaces.Count == 0)
            {
                GUILayout.Label(bu.text ?? "", UIStyles.LabelAlien);
                return;
            }
            bool gloss = tablet != null && tablet.ShowGlossOverlay;
            const int perLine = 6;
            for (int i = 0; i < bu.surfaces.Count; i += perLine)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(bu.surfaces.Count, i + perLine); j++)
                {
                    string s = bu.surfaces[j];
                    if (string.IsNullOrEmpty(s)) continue;
                    GUILayout.BeginVertical(GUILayout.ExpandWidth(false));
                    if (GUILayout.Button(s, UIStyles.LabelAlien))
                        EventBus.Publish(new DictionaryLookupRequestEvent { Surface = s, RecordId = bu.recordId });
                    if (gloss)
                    {
                        var g = tablet.GetPlayerGloss(s);
                        GUILayout.Label(string.IsNullOrEmpty(g) ? " " : g, UIStyles.LabelDim);
                    }
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
    }
}
