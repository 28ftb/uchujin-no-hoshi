using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 住民辞典（タブレット仕様 §7、宇宙語仕様 §8.5）。
    /// ・会った住民のページ。名前は最初は分からない前提で、あだ名・聞き取った名前メモはプレイヤーが書く。
    /// ・自動で入るのは観察できた事実だけ（初めて会った日・場所、購入記録、贈り物、写真など §7.3）。
    /// ・好み・性格・仕事・日常はプレイヤーが書く欄（推測）。ゲームは訂正しない。
    /// ・写真（§7.4）、人間関係図（§7.5：プレイヤーが線とラベルを書く）、引っ越した住民の印（§7.6）。
    /// </summary>
    internal sealed class ResidentsApp : TabletAppBase
    {
        public const string Id = "residents";

        private enum View { List, Page, Diagram }
        private enum PageTab { Notes, Facts, Voices, Photos, Relations }

        private View view = View.List;
        private PageTab tab = PageTab.Notes;
        private string currentId;
        private string search = "";
        private string tagsBuffer = "";
        private string tagsFor;

        // 関係図
        private bool linkMode;
        private string linkFrom;
        private string dragging;
        private Vector2 dragOffset;

        private static readonly Color[] LinkColors =
        {
            new Color(0.55f, 0.85f, 1f), new Color(1f, 0.65f, 0.75f), new Color(0.6f, 0.95f, 0.6f),
            new Color(1f, 0.85f, 0.45f), new Color(0.75f, 0.65f, 1f), new Color(0.75f, 0.75f, 0.75f)
        };

        internal ResidentsApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("住民辞典", "Resident Book");
        public override int Order => 30;
        public override string IconGlyph => Loc.T("住", "Res");
        public override Color IconColor => new Color(0.95f, 0.66f, 0.42f);

        public override void ResetState()
        {
            base.ResetState();
            view = View.List;
            currentId = null;
            linkFrom = null;
            dragging = null;
        }

        public override bool TryGoBack()
        {
            if (view == View.Page || view == View.Diagram)
            {
                view = View.List;
                linkFrom = null;
                return true;
            }
            return false;
        }

        public void ShowPage(string residentId)
        {
            if (Sys.FindResidentPage(residentId) == null) return;
            currentId = residentId;
            view = View.Page;
            tab = PageTab.Notes;
            scroll = Vector2.zero;
        }

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            switch (view)
            {
                case View.Page:
                    var page = Sys.FindResidentPage(currentId);
                    if (page == null) view = View.List;
                    else DrawPage(area, page);
                    break;
                case View.Diagram:
                    DrawDiagram(area);
                    break;
                default:
                    DrawList(area);
                    break;
            }
        }

        // ---------------- 一覧 ----------------

        private void DrawList(Rect area)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("検索：", "Search:"), UIStyles.LabelSmall, GUILayout.Width(70));
            search = GUILayout.TextField(search ?? "", UIStyles.TextField, GUILayout.Width(260), GUILayout.Height(34));
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("人間関係図", "Relationship map"), 200)) { view = View.Diagram; linkFrom = null; }
            if (Button(Loc.T("📷 写真を撮る", "📷 Take photo"), 200)) Sys.PhotoCamera.Begin(null, Id);
            GUILayout.EndHorizontal();

            var met = new List<ResidentPage>();
            var moved = new List<ResidentPage>();
            var unmet = new List<ResidentPage>();
            string q = (search ?? "").Trim().ToLowerInvariant();
            foreach (var p in Sys.Know.residents)
            {
                if (q.Length > 0 && !Matches(p, q)) continue;
                if (p.movedOut) moved.Add(p);
                else if (p.metDay >= 0) met.Add(p);
                else unmet.Add(p);
            }
            met.Sort((a, b) => a.number.CompareTo(b.number));
            scroll = GUILayout.BeginScrollView(scroll);
            if (Sys.Know.residents.Count == 0)
                GUILayout.Label(Loc.T("まだだれにも会っていません。住民に話しかけると、その住民のページができます。名前は分からないので、自分で呼び名を付けましょう。",
                    "You haven't met anyone yet. Talk to a resident to start a page. You won't know their name, so give them a nickname."), UIStyles.LabelWrap);
            DrawGrid(Loc.Format("会った住民（{0}）", "Met ({0})", met.Count), met, area.width);
            if (moved.Count > 0) DrawGrid(Loc.Format("引っ越した住民（{0}）", "Moved away ({0})", moved.Count), moved, area.width);
            if (unmet.Count > 0) DrawGrid(Loc.Format("記録だけある住民（{0}）", "Known only from records ({0})", unmet.Count), unmet, area.width);
            GUILayout.EndScrollView();
        }

        private static bool Matches(ResidentPage p, string q)
        {
            bool Has(string s) => !string.IsNullOrEmpty(s) && s.ToLowerInvariant().Contains(q);
            if (Has(p.nickname) || Has(p.heardName) || Has(p.job) || Has(p.likes) || Has(p.personality) || Has(p.memo)) return true;
            foreach (var t in p.tags) if (Has(t)) return true;
            return p.number.ToString("000").Contains(q);
        }

        private void DrawGrid(string header, List<ResidentPage> pages, float width)
        {
            if (pages.Count == 0) return;
            TabletGui.SectionHeader(header);
            const float cell = 190f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((width - 40f) / cell));
            for (int i = 0; i < pages.Count; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(pages.Count, i + cols); j++)
                {
                    var p = pages[j];
                    GUILayout.BeginVertical(TabletGui.CardBox, GUILayout.Width(cell - 10));
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    TabletGui.FaceLayout(84, TabletSystem.PageColor(p), p.lineage, Expression.Happy, p.movedOut);
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Label(TabletSystem.ResidentLabel(p), new GUIStyle(UIStyles.LabelBold) { alignment = TextAnchor.MiddleCenter, fontSize = 18 });
                    string sub = "No." + p.number.ToString("000");
                    if (p.movedOut) sub += Loc.T(" ・引っ越した", " · moved");
                    else if (p.metDay >= 0) sub += " · " + TabletText.DayLabel(p.metDay);
                    GUILayout.Label(sub, new GUIStyle(UIStyles.LabelDim) { alignment = TextAnchor.MiddleCenter });
                    if (GUILayout.Button(Loc.T("ページを開く", "Open"), UIStyles.ButtonSmall)) ShowPage(p.residentId);
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        // ---------------- 住民ページ ----------------

        private void DrawPage(Rect area, ResidentPage p)
        {
            if (tagsFor != p.residentId)
            {
                tagsFor = p.residentId;
                tagsBuffer = TabletText.JoinTags(p.tags);
            }
            float w = area.width - 50;
            // ヘッダー（顔・名前）
            GUILayout.BeginHorizontal();
            var faceRect = GUILayoutUtility.GetRect(130, 130, GUILayout.Width(130), GUILayout.Height(130));
            TabletGui.DrawFace(faceRect, TabletSystem.PageColor(p), p.lineage, Expression.Happy, p.movedOut);
            if (p.movedOut)
            {
                var stamp = new Rect(faceRect.x - 6, faceRect.yMax - 36, faceRect.width + 12, 30);
                UIStyles.FillRect(stamp, new Color(0.35f, 0.1f, 0.12f, 0.85f));
                GUI.Label(stamp, Loc.T("引っ越した", "MOVED AWAY"), TabletGui.Stamp);
            }
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.Format("住民 No.{0}", "Resident No.{0}", p.number.ToString("000")), UIStyles.LabelDim);
            p.nickname = PlayerLine(Loc.T("自分が付けた呼び名", "My nickname"), p.nickname);
            p.heardName = PlayerLine(Loc.T("聞き取った名前（メモ）", "Name I heard (memo)"), p.heardName);
            string facts = p.metDay >= 0
                ? Loc.Format("はじめて会った日：{0}（{1}）", "First met: {0} ({1})", TabletText.DayTime(p.metDay, p.metMinute), Loc.T(p.metPlaceJa, p.metPlaceEn))
                : Loc.T("まだ直接会っていません（商店の記録などだけがあります）", "Not met in person yet (only records such as shop sales)");
            if (p.movedOut) facts += Loc.Format(" ／ 引っ越した日：{0}", " / Moved away: {0}", TabletText.DayLabel(p.movedOutDay));
            GUILayout.Label(facts, TabletGui.FactLabel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (TabletGui.ChipToggle(Loc.T("メモ", "Notes"), tab == PageTab.Notes)) tab = PageTab.Notes;
            if (TabletGui.ChipToggle(Loc.Format("観察した事実（{0}）", "Facts ({0})", p.observations.Count), tab == PageTab.Facts)) tab = PageTab.Facts;
            if (TabletGui.ChipToggle(Loc.T("声と資料", "Voices & materials"), tab == PageTab.Voices)) tab = PageTab.Voices;
            if (TabletGui.ChipToggle(Loc.T("写真", "Photos"), tab == PageTab.Photos)) tab = PageTab.Photos;
            if (TabletGui.ChipToggle(Loc.T("関係", "Relations"), tab == PageTab.Relations)) tab = PageTab.Relations;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case PageTab.Notes: DrawNotes(p); break;
                case PageTab.Facts: DrawFacts(p); break;
                case PageTab.Voices: DrawVoices(p, w); break;
                case PageTab.Photos: DrawPhotos(p, w); break;
                case PageTab.Relations: DrawRelations(p); break;
            }
            GUILayout.EndScrollView();
        }

        private void DrawNotes(ResidentPage p)
        {
            GUILayout.Label(Loc.T("ここに書くのは自分の推測です。間違っていても、ゲームが勝手に直すことはありません。", "These are your guesses. The game never corrects them."), TabletGui.SmallDim);
            p.job = PlayerLine(Loc.T("仕事（推測）", "Job (guess)"), p.job);
            p.personality = PlayerLine(Loc.T("性格（推測）", "Personality (guess)"), p.personality);
            p.likes = PlayerLine(Loc.T("好きそうな物", "Seems to like"), p.likes);
            p.dislikes = PlayerLine(Loc.T("苦手そうな物", "Seems to dislike"), p.dislikes);
            p.routine = PlayerLine(Loc.T("よくいる場所・時間", "Usual place / time"), p.routine);
            GUILayout.Label(Loc.T("自由メモ", "Free notes"), UIStyles.LabelSmall);
            p.memo = TabletGui.Area(p.memo, 140);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("タグ（,区切り）", "Tags (comma)"), UIStyles.LabelSmall, GUILayout.Width(220));
            string tb = GUILayout.TextField(tagsBuffer ?? "", UIStyles.TextField, GUILayout.Height(34));
            if (tb != tagsBuffer)
            {
                tagsBuffer = tb;
                p.tags = TabletText.SplitTags(tb);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawFacts(ResidentPage p)
        {
            GUILayout.Label(Loc.T("実際に見聞きした出来事だけが自動で記録されます。", "Only things you actually saw or that were objectively recorded are added automatically."), TabletGui.SmallDim);
            if (p.observations.Count == 0) GUILayout.Label(Loc.T("（まだありません）", "(none yet)"), UIStyles.LabelDim);
            for (int i = p.observations.Count - 1; i >= 0; i--)
            {
                var o = p.observations[i];
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.Label(TabletText.DayTime(o.day, o.minute), UIStyles.LabelDim, GUILayout.Width(170));
                GUILayout.Label(Loc.T(o.factJa, o.factEn), TabletGui.FactLabel);
                GUILayout.FlexibleSpace();
                if (!string.IsNullOrEmpty(o.linkedRecordId))
                {
                    var rec = Sys.ResolveRecord(o.linkedRecordId);
                    if (rec != null)
                    {
                        if (Button("▶", 50)) Sys.ReplayNearPlayer(rec);
                        if (Sys.FindCardByRecord(rec.recordId) == null && Button(Loc.T("カードに保存", "Save card"), 160)) Sys.SaveVoiceCard(rec, "住民辞典", "Resident book");
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawVoices(ResidentPage p, float w)
        {
            // 顔が一致する声（原本の話し手、または自分で「この声の主」と指定したもの）
            TabletGui.SectionHeader(Loc.T("この顔の声（音声カード）", "Voices with this face (cards)"));
            bool any = false;
            foreach (var c in Sys.Know.cards)
            {
                var note = Sys.GetNote(c, false);
                bool mine = note != null && note.playerSpeakerId == p.residentId;
                bool byFace = (note == null || string.IsNullOrEmpty(note.playerSpeakerId)) && c.original.speakerId == p.residentId;
                if (!mine && !byFace && !p.attachedCardIds.Contains(c.id)) continue;
                any = true;
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, c.original, 44, note);
                GUILayout.BeginVertical();
                TabletGui.RecordWords(c.original, w - 480, TabletSettings.GlossOverlay);
                string how = p.attachedCardIds.Contains(c.id) ? Loc.T("自分で貼った", "attached by me") : mine ? Loc.T("この住民の声だと思う", "I think it's theirs") : Loc.T("顔が同じ", "same face");
                GUILayout.Label(TabletText.DayTime(c.original.day, c.original.minuteOfDay) + " / " + Loc.T(c.sourceJa, c.sourceEn) + " / " + how, TabletGui.FactLabel);
                if (note != null && !string.IsNullOrEmpty(note.residentGuess)) GUILayout.Label("≈ " + note.residentGuess, TabletGui.PlayerText);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button("▶", 50)) Sys.ReplayNearPlayer(c.original);
                if (Button(Loc.T("開く", "Open"), 80))
                {
                    Sys.CardsApp.ShowCard(c);
                    Sys.Window?.Navigate(VoiceCardsApp.Id);
                }
                if (p.attachedCardIds.Contains(c.id) && Button(Loc.T("外す", "Remove"), 80)) p.attachedCardIds.Remove(c.id);
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("（まだありません）", "(none yet)"), UIStyles.LabelDim);

            TabletGui.SectionHeader(Loc.T("関係する資料", "Related materials"));
            bool anyMat = false;
            foreach (var m in Sys.Know.materials)
            {
                bool linked = m.linkedResidentId == p.residentId;
                bool attached = p.attachedMaterialIds.Contains(m.id);
                if (!linked && !attached) continue;
                anyMat = true;
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.BeginVertical();
                GUILayout.Label($"[{TabletText.KindLabel(m.kind)}] {Loc.T(m.titleJa, m.titleEn)}", UIStyles.LabelBold);
                GUILayout.Label(Loc.T(m.factJa, m.factEn), TabletGui.FactLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(TabletText.DayLabel(m.day), UIStyles.LabelDim, GUILayout.Width(100));
                if (Button(Loc.T("開く", "Open"), 80))
                {
                    Sys.MaterialsApp.ShowMaterial(m.id);
                    Sys.Window?.Navigate(MaterialsApp.Id);
                }
                if (attached && Button(Loc.T("外す", "Remove"), 80)) p.attachedMaterialIds.Remove(m.id);
                GUILayout.EndHorizontal();
            }
            if (!anyMat) GUILayout.Label(Loc.T("（まだありません）", "(none yet)"), UIStyles.LabelDim);
        }

        private void DrawPhotos(ResidentPage p, float w)
        {
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("📷 写真を撮りに行く", "📷 Go take a photo"), 260, 44)) Sys.PhotoCamera.Begin(p.residentId, Id);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            bool any = false;
            foreach (var ph in Sys.Know.photos)
            {
                if (!ph.residentIds.Contains(p.residentId) && !p.photoIds.Contains(ph.id)) continue;
                any = true;
                GUILayout.BeginVertical(TabletGui.CardBox);
                TabletGui.PhotoLayout(ph, Mathf.Min(w - 40, 640), 360);
                GUILayout.Label(TabletText.DayTime(ph.day, ph.minute) + " / " + Loc.T(ph.placeJa, ph.placeEn), TabletGui.FactLabel);
                if (!string.IsNullOrEmpty(ph.caption)) GUILayout.Label(ph.caption, TabletGui.PlayerText);
                GUILayout.EndVertical();
            }
            if (!any) GUILayout.Label(Loc.T("（まだ写真はありません）", "(no photos yet)"), UIStyles.LabelDim);
        }

        private void DrawRelations(ResidentPage p)
        {
            GUILayout.Label(Loc.T("住民どうしの関係は、自分で線を引いてラベルを書きます（正解は表示されません）。", "Draw lines between residents and label them yourself (no answers are shown)."), TabletGui.SmallDim);
            bool any = false;
            foreach (var l in Sys.Know.relations)
            {
                if (l.a != p.residentId && l.b != p.residentId) continue;
                any = true;
                string other = l.a == p.residentId ? l.b : l.a;
                var op = Sys.FindResidentPage(other);
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                if (op != null) TabletGui.FaceLayout(40, TabletSystem.PageColor(op), op.lineage, Expression.Neutral, op.movedOut);
                GUILayout.Label(Sys.ResidentLabel(other), UIStyles.Label, GUILayout.Width(240));
                GUILayout.Label(string.IsNullOrEmpty(l.label) ? Loc.T("（ラベルなし）", "(no label)") : l.label, TabletGui.PlayerText);
                GUILayout.FlexibleSpace();
                if (op != null && Button(Loc.T("ページへ", "Go"), 100)) ShowPage(other);
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("（まだ線を引いていません）", "(no lines yet)"), UIStyles.LabelDim);
            if (Button(Loc.T("人間関係図を開く", "Open relationship map"), 260, 44)) { view = View.Diagram; linkFrom = null; }
        }

        private static string PlayerLine(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UIStyles.LabelSmall, GUILayout.Width(220));
            var style = new GUIStyle(UIStyles.TextField);
            style.normal.textColor = TabletGui.PlayerColor;
            value = GUILayout.TextField(value ?? "", style, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            return value;
        }

        // ---------------- 人間関係図（§7.5） ----------------

        private void DrawDiagram(Rect area)
        {
            GUILayout.BeginHorizontal();
            if (TabletGui.ChipToggle(Loc.T("動かす", "Move"), !linkMode)) { linkMode = false; linkFrom = null; }
            if (TabletGui.ChipToggle(Loc.T("線をつなぐ", "Connect"), linkMode)) linkMode = true;
            GUILayout.Space(16);
            GUILayout.Label(linkMode
                    ? (linkFrom == null ? Loc.T("1人目の顔を押してください", "Tap the first face") : Loc.T("2人目の顔を押してください", "Tap the second face"))
                    : Loc.T("顔をドラッグで動かせます。線のラベルを押すと書き直せます。", "Drag faces to move. Tap a label to edit it."), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var nodes = new List<ResidentPage>();
            foreach (var p in Sys.Know.residents) if (p.metDay >= 0 || p.movedOut) nodes.Add(p);
            var canvas = GUILayoutUtility.GetRect(area.width - 20, area.height - 70, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            UIStyles.FillRect(canvas, new Color(0.07f, 0.09f, 0.14f, 0.9f));
            if (nodes.Count == 0)
            {
                GUI.Label(canvas, Loc.T("会った住民がいません", "No residents met yet"), UIStyles.LabelCenter);
                return;
            }
            const float face = 64f;
            var pos = new Dictionary<string, Vector2>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = Sys.GetNode(nodes[i].residentId, i, nodes.Count);
                pos[nodes[i].residentId] = new Vector2(canvas.x + face * 0.5f + n.x * (canvas.width - face), canvas.y + face * 0.5f + n.y * (canvas.height - face - 24f));
            }
            var ev = Event.current;

            // 線とラベル
            RelationLink clicked = null;
            foreach (var l in Sys.Know.relations)
            {
                if (!pos.TryGetValue(l.a, out var pa) || !pos.TryGetValue(l.b, out var pb)) continue;
                var col = LinkColors[Mathf.Abs(l.colorIndex) % LinkColors.Length];
                TabletGui.DrawLine(pa, pb, col, 4f);
                var mid = (pa + pb) * 0.5f;
                string text = string.IsNullOrEmpty(l.label) ? "?" : l.label;
                var size = TabletGui.Small.CalcSize(new GUIContent(text));
                var lr = new Rect(mid.x - size.x * 0.5f - 6, mid.y - 14, size.x + 12, 28);
                UIStyles.FillRect(lr, new Color(0.05f, 0.06f, 0.1f, 0.9f));
                var st = new GUIStyle(TabletGui.Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
                st.normal.textColor = col;
                GUI.Label(lr, text, st);
                if (ev.type == EventType.MouseDown && ev.button == 0 && lr.Contains(ev.mousePosition)) clicked = l;
            }

            // 顔
            foreach (var p in nodes)
            {
                var c = pos[p.residentId];
                var r = new Rect(c.x - face * 0.5f, c.y - face * 0.5f, face, face);
                if (linkFrom == p.residentId) UIStyles.FillRect(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(1f, 0.9f, 0.4f, 0.6f));
                TabletGui.DrawFace(r, TabletSystem.PageColor(p), p.lineage, Expression.Neutral, p.movedOut);
                var nameStyle = new GUIStyle(TabletGui.Small) { alignment = TextAnchor.UpperCenter, wordWrap = false, fontSize = 15 };
                GUI.Label(new Rect(c.x - 80, r.yMax, 160, 22), TabletSystem.ResidentLabel(p), nameStyle);

                if (ev.type == EventType.MouseDown && ev.button == 0 && r.Contains(ev.mousePosition) && clicked == null)
                {
                    if (linkMode) OnLinkClick(p.residentId);
                    else
                    {
                        dragging = p.residentId;
                        dragOffset = ev.mousePosition - c;
                    }
                    ev.Use();
                }
            }

            if (dragging != null && ev.type == EventType.MouseDrag)
            {
                var node = Sys.GetNode(dragging, 0, 1);
                var target = ev.mousePosition - dragOffset;
                node.x = Mathf.Clamp01((target.x - canvas.x - face * 0.5f) / Mathf.Max(1f, canvas.width - face));
                node.y = Mathf.Clamp01((target.y - canvas.y - face * 0.5f) / Mathf.Max(1f, canvas.height - face - 24f));
                ev.Use();
            }
            if (ev.type == EventType.MouseUp || ev.rawType == EventType.MouseUp) dragging = null;

            if (clicked != null)
            {
                ev.Use();
                EditLink(clicked);
            }
        }

        private void OnLinkClick(string residentId)
        {
            if (linkFrom == null)
            {
                linkFrom = residentId;
                return;
            }
            if (linkFrom == residentId)
            {
                linkFrom = null;
                return;
            }
            string a = linkFrom, b = residentId;
            linkFrom = null;
            UIManager.InputText(Loc.T("関係のラベル", "Relationship label"),
                Loc.Format("{0} と {1} の関係は？（例：友達？ 家族？ 仲が悪い？）", "How are {0} and {1} related? (e.g. friends? family? rivals?)", Sys.ResidentLabel(a), Sys.ResidentLabel(b)),
                "", label =>
                {
                    foreach (var l in Sys.Know.relations)
                    {
                        if ((l.a == a && l.b == b) || (l.a == b && l.b == a))
                        {
                            l.label = label;
                            return;
                        }
                    }
                    Sys.Know.relations.Add(new RelationLink { id = Ids.New(), a = a, b = b, label = label, colorIndex = Sys.Know.relations.Count % LinkColors.Length });
                });
        }

        private void EditLink(RelationLink l)
        {
            var link = l;
            UIManager.Choose(Loc.T("関係の線", "Relationship line"), Loc.Format("{0} ― {1}：{2}", "{0} - {1}: {2}", Sys.ResidentLabel(l.a), Sys.ResidentLabel(l.b), l.label), new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("ラベルを書き直す", "Edit label"), () => UIManager.InputText(Loc.T("関係のラベル", "Relationship label"), null, link.label, s => link.label = s)),
                new ChoiceOption(Loc.T("色を変える", "Change color"), () => link.colorIndex = (link.colorIndex + 1) % LinkColors.Length),
                new ChoiceOption(Loc.T("線を消す", "Delete line"), () => Sys.Know.relations.Remove(link)),
                new ChoiceOption(Loc.T("やめる", "Cancel"), null)
            });
        }
    }
}
