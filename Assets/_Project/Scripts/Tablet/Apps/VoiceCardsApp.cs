using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 顔付き音声カード（タブレット仕様 §5）。
    /// ・原本（聞いた発話のコピー：顔・宇宙語・表情・身振り・日時・場所・天気・その場の物）と、プレイヤーの注釈を分けて表示・保存する（§5.3）。
    /// ・再生（主人公の近くで吹き出し）、単語を押すと辞書へ、話し手・場所・タグで絞り込み、並べ替え。
    /// ・同じカードを辞書・住民辞典・文法ノートから参照できる。外しても原本は消えない。
    /// </summary>
    internal sealed class VoiceCardsApp : TabletAppBase
    {
        public const string Id = "voicecards";

        private VoiceCard current;
        private string speakerFilter;
        private string placeFilter;
        private string tagFilter;
        private string textFilter = "";
        private int sortMode; // 0=新しい順 1=古い順 2=話し手
        private string tagsBuffer = "";
        private string tagsFor;

        internal VoiceCardsApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("音声カード", "Voice Cards");
        public override int Order => 20;
        public override string IconGlyph => Loc.T("声", "Vo");
        public override Color IconColor => new Color(0.85f, 0.5f, 0.62f);

        public override void ResetState()
        {
            base.ResetState();
            current = null;
            speakerFilter = placeFilter = tagFilter = null;
            textFilter = "";
        }

        public override bool TryGoBack()
        {
            if (current != null)
            {
                current = null;
                return true;
            }
            return false;
        }

        public void ShowCard(VoiceCard card)
        {
            current = card;
            scroll = Vector2.zero;
        }

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            if (current != null && Sys.Know.cards.Contains(current)) DrawDetail(area);
            else
            {
                current = null;
                DrawList(area);
            }
        }

        // ---------------- 一覧 ----------------

        private void DrawList(Rect area)
        {
            var cards = Sys.Know.cards;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("検索：", "Search:"), UIStyles.LabelSmall, GUILayout.Width(70));
            textFilter = GUILayout.TextField(textFilter ?? "", UIStyles.TextField, GUILayout.Width(220), GUILayout.Height(34));
            if (Button(Loc.Format("話し手：{0}", "Speaker: {0}", speakerFilter == null ? Loc.T("すべて", "all") : Sys.ResidentLabel(speakerFilter)), 260)) ChooseSpeaker(cards);
            if (Button(Loc.Format("場所：{0}", "Place: {0}", placeFilter ?? Loc.T("すべて", "all")), 220)) ChoosePlace(cards);
            if (Button(Loc.Format("タグ：{0}", "Tag: {0}", tagFilter ?? Loc.T("すべて", "all")), 200)) ChooseTag();
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("並び：", "Sort:"), UIStyles.LabelSmall, GUILayout.Width(60));
            if (TabletGui.ChipToggle(Loc.T("新しい順", "Newest"), sortMode == 0)) sortMode = 0;
            if (TabletGui.ChipToggle(Loc.T("古い順", "Oldest"), sortMode == 1)) sortMode = 1;
            if (TabletGui.ChipToggle(Loc.T("話し手ごと", "By speaker"), sortMode == 2)) sortMode = 2;
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.Format("{0}枚", "{0} cards", cards.Count), UIStyles.LabelDim);
            GUILayout.EndHorizontal();

            var list = new List<VoiceCard>();
            string q = (textFilter ?? "").Trim().ToLowerInvariant();
            foreach (var c in cards)
            {
                if (c.original == null) continue;
                var note = Sys.GetNote(c, false);
                if (speakerFilter != null && EffectiveSpeaker(c, note) != speakerFilter) continue;
                if (placeFilter != null && TabletText.PlaceOfRecord(c.original).ToString() != placeFilter) continue;
                if (tagFilter != null && (note == null || !note.tags.Contains(tagFilter))) continue;
                if (q.Length > 0)
                {
                    bool hit = (c.original.text ?? "").ToLowerInvariant().Contains(q)
                               || Sys.SpeakerLabel(c.original, note).ToLowerInvariant().Contains(q)
                               || (note != null && ((note.guessedMeaning ?? "").ToLowerInvariant().Contains(q) || (note.memo ?? "").ToLowerInvariant().Contains(q) || (note.residentGuess ?? "").ToLowerInvariant().Contains(q)));
                    if (!hit) continue;
                }
                list.Add(c);
            }
            if (sortMode == 0) list.Sort((a, b) => Stamp(b).CompareTo(Stamp(a)));
            else if (sortMode == 1) list.Sort((a, b) => Stamp(a).CompareTo(Stamp(b)));
            else list.Sort((a, b) => string.CompareOrdinal(Sys.SpeakerLabel(a.original, Sys.GetNote(a, false)), Sys.SpeakerLabel(b.original, Sys.GetNote(b, false))));

            scroll = GUILayout.BeginScrollView(scroll);
            if (cards.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ音声カードがありません。マネピの伝言や会話ログから「音声カードとして保存」できます。話しかけられた直後に [K] でもその場で保存できます。",
                    "No voice cards yet. Save them from Manepi messages or the conversation log, or press [K] right after someone speaks to you."), UIStyles.LabelWrap);
            }
            foreach (var c in list)
            {
                var note = Sys.GetNote(c, false);
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, c.original, 60, note);
                GUILayout.BeginVertical();
                GUILayout.Label(c.original.text, TabletGui.WordStatic);
                GUILayout.Label(Sys.SpeakerLabel(c.original, note) + " / " + TabletText.DayTime(c.original.day, c.original.minuteOfDay) + " / " + TabletText.PlaceOfRecord(c.original) + " / " + Loc.T(c.sourceJa, c.sourceEn), TabletGui.FactLabel);
                if (note != null && !string.IsNullOrEmpty(note.guessedMeaning)) GUILayout.Label("≈ " + note.guessedMeaning, TabletGui.PlayerText);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (note != null && note.pinned) GUILayout.Label("📌", UIStyles.Label, GUILayout.Width(30));
                if (Button("▶", 50)) Sys.ReplayNearPlayer(c.original);
                if (Button(Loc.T("開く", "Open"), 90)) ShowCard(c);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private static long Stamp(VoiceCard c) => (long)c.original.day * 1440 + c.original.minuteOfDay;

        private static string EffectiveSpeaker(VoiceCard c, CardNote note)
        {
            if (note != null && !string.IsNullOrEmpty(note.playerSpeakerId)) return note.playerSpeakerId;
            return c.original.speakerKind == "resident" || string.IsNullOrEmpty(c.original.speakerKind) ? c.original.speakerId : null;
        }

        private void ChooseSpeaker(List<VoiceCard> cards)
        {
            var opts = new List<ChoiceOption> { new ChoiceOption(Loc.T("すべて", "All"), () => speakerFilter = null) };
            var seen = new HashSet<string>();
            foreach (var c in cards)
            {
                var id = EffectiveSpeaker(c, Sys.GetNote(c, false));
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                string captured = id;
                opts.Add(new ChoiceOption(Sys.ResidentLabel(id), () => speakerFilter = captured));
            }
            UIManager.Choose(Loc.T("話し手で絞り込む", "Filter by speaker"), null, opts);
        }

        private void ChoosePlace(List<VoiceCard> cards)
        {
            var opts = new List<ChoiceOption> { new ChoiceOption(Loc.T("すべて", "All"), () => placeFilter = null) };
            var seen = new HashSet<string>();
            foreach (var c in cards)
            {
                var p = TabletText.PlaceOfRecord(c.original).ToString();
                if (!seen.Add(p)) continue;
                opts.Add(new ChoiceOption(p, () => placeFilter = p));
            }
            UIManager.Choose(Loc.T("場所で絞り込む", "Filter by place"), null, opts);
        }

        private void ChooseTag()
        {
            var opts = new List<ChoiceOption> { new ChoiceOption(Loc.T("すべて", "All"), () => tagFilter = null) };
            var seen = new HashSet<string>();
            foreach (var n in Sys.Know.cardNotes)
                foreach (var t in n.tags)
                {
                    if (!seen.Add(t)) continue;
                    string captured = t;
                    opts.Add(new ChoiceOption("#" + t, () => tagFilter = captured));
                }
            UIManager.Choose(Loc.T("タグで絞り込む", "Filter by tag"), null, opts);
        }

        // ---------------- 詳細 ----------------

        private void DrawDetail(Rect area)
        {
            var c = current;
            var r = c.original;
            var note = Sys.GetNote(c, true);
            if (tagsFor != c.id)
            {
                tagsFor = c.id;
                tagsBuffer = TabletText.JoinTags(note.tags);
            }
            float w = area.width - 50;
            scroll = GUILayout.BeginScrollView(scroll);

            // ---- 原本（観察できた事実） ----
            GUILayout.BeginVertical(TabletGui.CardBox);
            GUILayout.BeginHorizontal();
            TabletGui.SpeakerFace(Sys, r, 110, note);
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.T("原本（聞いたまま・変更されません）", "Original (as heard; never changes)"), UIStyles.LabelDim);
            GUILayout.Label(Sys.SpeakerLabel(r, note), UIStyles.LabelBold);
            GUILayout.Label(Loc.Format("{0} ／ {1} ／ {2}", "{0} / {1} / {2}", TabletText.DayTime(r.day, r.minuteOfDay), TabletText.PlaceOfRecord(r), TabletText.ChannelLabel(r.channel)), TabletGui.FactLabel);
            if (!string.IsNullOrEmpty(r.weatherSummary)) GUILayout.Label(Loc.Format("天気：{0}", "Weather: {0}", r.weatherSummary), TabletGui.FactLabel);
            GUILayout.Label(Loc.Format("様子：{0}", "Looked: {0}", TabletText.NonverbalSummary(r)), TabletGui.FactLabel);
            var related = new List<string>();
            if (r.relatedIds != null) foreach (var id in r.relatedIds) { var l = TabletText.RelatedLabel(id); if (!string.IsNullOrEmpty(l) && !related.Contains(l)) related.Add(l); }
            if (related.Count > 0) GUILayout.Label(Loc.Format("その場にあった物：{0}", "Things there: {0}", string.Join(Loc.T("、", ", "), related)), TabletGui.FactLabel);
            GUILayout.Label(Loc.Format("保存元：{0}（{1}に保存）", "Saved from: {0} ({1})", Loc.T(c.sourceJa, c.sourceEn), TabletText.DayTime(c.savedDay, c.savedMinute)), UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            TabletGui.RecordWords(r, w - 40, TabletSettings.GlossOverlay);
            if (TabletSettings.GlossOverlay) GUILayout.Label(Loc.T("（下の小さな字は自分が辞書に書いた推測訳です）", "(Small text below words = your own guesses from the dictionary)"), UIStyles.LabelDim);
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            if (Button(Loc.T("▶ 再生", "▶ Play"), 140, 44)) Sys.ReplayNearPlayer(r);
            if (TabletGui.ChipToggle(note.pinned ? Loc.T("📌ピン留め中", "📌Pinned") : Loc.T("ピン留め", "Pin"), note.pinned, GUILayout.Height(44))) note.pinned = !note.pinned;
            if (Button(Loc.T("住民辞典に貼る", "Attach to resident"), 220, 44)) ChooseResidentToAttach(c);
            if (Button(Loc.T("この声の主を指定", "Set speaker"), 210, 44)) ChooseSpeakerOverride(note);
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("カードを捨てる", "Discard card"), 180, 44))
            {
                var target = c;
                UIManager.Confirm(Loc.T("カードを捨てる", "Discard card"), Loc.T("この音声カードを捨てますか？辞書や住民辞典に貼ってあるリンクも外れます。", "Discard this card? Links from the dictionary and resident book will be removed."), () =>
                {
                    Sys.DeleteCard(target);
                    current = null;
                });
            }
            GUILayout.EndHorizontal();

            // ---- 注釈（プレイヤーの記入） ----
            TabletGui.SectionHeader(Loc.T("自分の注釈", "My notes"));
            note.guessedMeaning = PlayerField(Loc.T("推測した意味", "Guessed meaning"), note.guessedMeaning);
            note.residentGuess = PlayerField(Loc.T("住民について思ったこと", "About the speaker"), note.residentGuess);
            GUILayout.Label(Loc.T("メモ", "Memo"), UIStyles.LabelSmall);
            note.memo = TabletGui.Area(note.memo, 90);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("タグ（,区切り）", "Tags (comma)"), UIStyles.LabelSmall, GUILayout.Width(170));
            string tb = GUILayout.TextField(tagsBuffer ?? "", UIStyles.TextField, GUILayout.Height(34));
            if (tb != tagsBuffer)
            {
                tagsBuffer = tb;
                note.tags = TabletText.SplitTags(tb);
            }
            GUILayout.EndHorizontal();

            // ---- 参照されている場所 ----
            TabletGui.SectionHeader(Loc.T("このカードを貼っている場所", "Where this card is used"));
            GUILayout.BeginHorizontal();
            bool any = false;
            foreach (var e in Sys.Know.dictionary)
            {
                foreach (var ev in e.evidence)
                {
                    if (ev.kind != "card" || ev.refId != c.id) continue;
                    any = true;
                    if (GUILayout.Button(Loc.Format("辞書：{0}", "Dict: {0}", e.surface), TabletGui.Chip))
                    {
                        Sys.Window?.Navigate(DictionaryApp.Id);
                        Sys.DictApp.OpenEntry(e, false);
                    }
                }
            }
            foreach (var p in Sys.Know.residents)
            {
                if (!p.attachedCardIds.Contains(c.id)) continue;
                any = true;
                if (GUILayout.Button(Loc.Format("住民：{0}", "Resident: {0}", TabletSystem.ResidentLabel(p)), TabletGui.Chip))
                {
                    Sys.Window?.Navigate(ResidentsApp.Id);
                    Sys.ResidentsApp.ShowPage(p.residentId);
                }
            }
            foreach (var g in Sys.Know.grammarPages)
            {
                if (!g.cardIds.Contains(c.id) && g.compareA != c.id && g.compareB != c.id) continue;
                any = true;
                GUILayout.Label(Loc.Format("文法ノート：{0}", "Grammar: {0}", g.title), TabletGui.Chip);
            }
            if (!any) GUILayout.Label(Loc.T("（まだどこにも貼っていません。宇宙語の単語を押すと辞書のページから証拠にできます）", "(Not attached anywhere. Tap a word to use this card as dictionary evidence.)"), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        private static string PlayerField(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UIStyles.LabelSmall, GUILayout.Width(230));
            var style = new GUIStyle(UIStyles.TextField);
            style.normal.textColor = TabletGui.PlayerColor;
            value = GUILayout.TextField(value ?? "", style, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            return value;
        }

        private void ChooseResidentToAttach(VoiceCard card)
        {
            var opts = new List<ChoiceOption>();
            foreach (var p in Sys.Know.residents)
            {
                var page = p;
                bool already = page.attachedCardIds.Contains(card.id);
                opts.Add(new ChoiceOption((already ? "✓ " : "") + TabletSystem.ResidentLabel(page), () =>
                {
                    if (!page.attachedCardIds.Contains(card.id)) page.attachedCardIds.Add(card.id);
                    UIManager.Toast(Loc.Format("{0} のページに貼りました", "Attached to {0}", TabletSystem.ResidentLabel(page)));
                }));
            }
            if (opts.Count == 0)
            {
                UIManager.Message(Loc.T("住民辞典", "Resident book"), Loc.T("住民辞典にまだページがありません。住民に話しかけるとページができます。", "No resident pages yet. Talk to residents to create pages."));
                return;
            }
            UIManager.Choose(Loc.T("どの住民のページに貼る？", "Attach to which resident?"), null, opts);
        }

        /// <summary>話し手が分からない・違うと思った時に、本人が「この声は◯◯」と結び付ける（§5.2：無理に確定表示しない）。</summary>
        private void ChooseSpeakerOverride(CardNote note)
        {
            var opts = new List<ChoiceOption> { new ChoiceOption(Loc.T("指定しない（原本のまま）", "None (as recorded)"), () => note.playerSpeakerId = null) };
            foreach (var p in Sys.Know.residents)
            {
                var id = p.residentId;
                opts.Add(new ChoiceOption(TabletSystem.ResidentLabel(p), () => note.playerSpeakerId = id));
            }
            UIManager.Choose(Loc.T("この声の主だと思う住民", "Who do you think said this?"), null, opts);
        }
    }
}
