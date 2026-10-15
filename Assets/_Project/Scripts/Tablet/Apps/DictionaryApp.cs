using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 宇宙語辞書（タブレット仕様 §6、宇宙語仕様 §7, §12）。
    /// ・自分でつくる研究ノート。ゲームは正解を書き込まない・誤訳もそのまま残す（§6.1）。
    /// ・単語ページ：綴り／意味の候補（複数・確信度マーク）／品詞の推測／メモ／タグ／関連語／証拠リンク／その語が出てきた記録の自動索引。
    /// ・文法ノート：自由ページ＋2枚の音声カードの並列比較（違う語を強調、自分で印を付けられる §6.4）。
    /// ・別冊（受け取った辞書）と、書き出し・取り込み（マルチでの任意の手渡し §12）。
    /// </summary>
    internal sealed class DictionaryApp : TabletAppBase
    {
        public const string Id = "dictionary";

        private enum View { List, Entry, Lookup, Grammar, GrammarPage, Volumes, Volume, Share }

        private View view = View.List;
        private DictEntry current;
        private readonly List<string> entryTrail = new List<string>();
        private string lookupSurface;
        private string pendingRecordId;
        private GrammarPage page;
        private DictVolume volume;

        // 一覧
        private string search = "";
        private string tagFilter;
        private int sortMode;
        private string newSurface = "";

        // 単語ページ編集用の入力欄
        private string newHypothesis = "";
        private string newRelated = "";
        private string newPart = "";
        private string tagsBuffer = "";
        private string tagsBufferFor;
        private bool showHistory;

        // 文法ノート
        private bool compareMarkMode = true;
        private int picking; // 0=なし 1=比較A 2=比較B 3=証拠
        private string pickFilter = "";

        // 共有
        private readonly HashSet<string> shareSelection = new HashSet<string>();
        private bool shareAll = true;
        private string importFromName = "";

        internal DictionaryApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("宇宙語辞書", "Alien Dictionary");
        public override int Order => 10;
        public override string IconGlyph => Loc.T("辞", "Dic");
        public override Color IconColor => new Color(0.32f, 0.62f, 0.5f);

        public override void ResetState()
        {
            base.ResetState();
            view = View.List;
            current = null;
            entryTrail.Clear();
            page = null;
            volume = null;
            pendingRecordId = null;
            search = "";
            tagFilter = null;
            shareSelection.Clear();
            picking = 0;
        }

        public override bool TryGoBack()
        {
            picking = 0;
            switch (view)
            {
                case View.Entry:
                    if (entryTrail.Count > 0)
                    {
                        var id = entryTrail[entryTrail.Count - 1];
                        entryTrail.RemoveAt(entryTrail.Count - 1);
                        var e = Sys.FindEntryById(id);
                        if (e != null)
                        {
                            OpenEntry(e, false);
                            return true;
                        }
                    }
                    view = View.List;
                    return true;
                case View.Lookup:
                case View.Grammar:
                case View.Volumes:
                case View.Share:
                    view = View.List;
                    return true;
                case View.GrammarPage:
                    view = View.Grammar;
                    return true;
                case View.Volume:
                    view = View.Volumes;
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------
        // 外からの入口
        // ------------------------------------------------------------

        /// <summary>単語が選ばれた（DictionaryLookupRequestEvent）：ページがあれば開き、無ければ新規作成画面。</summary>
        public void ShowLookup(string surface, string recordId)
        {
            pendingRecordId = recordId;
            var e = Sys.FindEntry(surface);
            if (e != null)
            {
                entryTrail.Clear();
                OpenEntry(e, false);
                return;
            }
            lookupSurface = TabletText.CleanSurface(surface);
            view = View.Lookup;
            scroll = Vector2.zero;
        }

        public void OpenEntry(DictEntry e, bool pushTrail)
        {
            if (e == null) return;
            if (pushTrail && current != null && view == View.Entry && current != e) entryTrail.Add(current.id);
            current = e;
            view = View.Entry;
            scroll = Vector2.zero;
            newHypothesis = "";
            newRelated = "";
            newPart = "";
            tagsBuffer = TabletText.JoinTags(e.tags);
            tagsBufferFor = e.id;
        }

        // ------------------------------------------------------------
        // 描画
        // ------------------------------------------------------------

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            if (view == View.List || view == View.Grammar || view == View.Volumes || view == View.Share) DrawTabs();
            switch (view)
            {
                case View.List: DrawList(area); break;
                case View.Entry: DrawEntry(area); break;
                case View.Lookup: DrawLookup(area); break;
                case View.Grammar: DrawGrammarList(area); break;
                case View.GrammarPage: DrawGrammarPage(area); break;
                case View.Volumes: DrawVolumes(area); break;
                case View.Volume: DrawVolume(area); break;
                case View.Share: DrawShare(area); break;
            }
        }

        private void DrawTabs()
        {
            GUILayout.BeginHorizontal();
            if (TabletGui.ChipToggle(Loc.T("単語", "Words"), view == View.List, GUILayout.Width(140))) view = View.List;
            if (TabletGui.ChipToggle(Loc.T("文法ノート", "Grammar notes"), view == View.Grammar, GUILayout.Width(170))) view = View.Grammar;
            if (TabletGui.ChipToggle(Loc.Format("別冊（{0}）", "Volumes ({0})", Sys.Know.volumes.Count), view == View.Volumes, GUILayout.Width(170))) view = View.Volumes;
            if (TabletGui.ChipToggle(Loc.T("辞書を渡す・受け取る", "Share"), view == View.Share, GUILayout.Width(220))) view = View.Share;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(4);
        }

        // ---------------- 一覧 ----------------

        private void DrawList(Rect area)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("検索：", "Search:"), UIStyles.Label, GUILayout.Width(80));
            search = GUILayout.TextField(search ?? "", UIStyles.TextField, GUILayout.Height(36), GUILayout.Width(320));
            GUILayout.Space(20);
            GUILayout.Label(Loc.T("新しい語：", "New word:"), UIStyles.Label, GUILayout.Width(110));
            newSurface = GUILayout.TextField(newSurface ?? "", UIStyles.TextField, GUILayout.Height(36), GUILayout.Width(220));
            GUI.enabled = !string.IsNullOrEmpty(TabletText.CleanSurface(newSurface));
            if (Button(Loc.T("ページを作る", "Create"), 150))
            {
                var e = Sys.CreateEntry(newSurface, newSurface.Trim().Contains(" ") ? DictEntryKind.Phrase : DictEntryKind.Word);
                newSurface = "";
                entryTrail.Clear();
                OpenEntry(e, false);
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // タグ・並び順
            var allTags = new List<string>();
            foreach (var e in Sys.Know.dictionary) foreach (var t in e.tags) if (!allTags.Contains(t)) allTags.Add(t);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("並び：", "Sort:"), UIStyles.LabelSmall, GUILayout.Width(60));
            if (TabletGui.ChipToggle(Loc.T("更新順", "Updated"), sortMode == 0)) sortMode = 0;
            if (TabletGui.ChipToggle(Loc.T("綴り順", "A-Z"), sortMode == 1)) sortMode = 1;
            if (TabletGui.ChipToggle(Loc.T("作った順", "Created"), sortMode == 2)) sortMode = 2;
            GUILayout.Space(16);
            if (allTags.Count > 0)
            {
                GUILayout.Label(Loc.T("タグ：", "Tag:"), UIStyles.LabelSmall, GUILayout.Width(60));
                if (TabletGui.ChipToggle(Loc.T("すべて", "All"), tagFilter == null)) tagFilter = null;
                int shown = 0;
                foreach (var t in allTags)
                {
                    if (shown++ > 10) break;
                    if (TabletGui.ChipToggle(t, tagFilter == t)) tagFilter = tagFilter == t ? null : t;
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            var list = new List<DictEntry>();
            string q = (search ?? "").Trim().ToLowerInvariant();
            foreach (var e in Sys.Know.dictionary)
            {
                if (tagFilter != null && !e.tags.Contains(tagFilter)) continue;
                if (q.Length > 0 && !Matches(e, q)) continue;
                list.Add(e);
            }
            if (sortMode == 0) list.Sort((a, b) => b.updatedDay.CompareTo(a.updatedDay));
            else if (sortMode == 1) list.Sort((a, b) => string.Compare(TabletText.Normalize(a.surface), TabletText.Normalize(b.surface), StringComparison.Ordinal));
            else list.Sort((a, b) => a.createdDay.CompareTo(b.createdDay));

            scroll = GUILayout.BeginScrollView(scroll);
            if (Sys.Know.dictionary.Count == 0)
            {
                GUILayout.Label(Loc.T("まだ何も書いていません。会話ログや音声カードの宇宙語を押すと、その語のページを作れます。",
                    "Nothing written yet. Tap an alien word in the conversation log or a voice card to make a page for it."), UIStyles.LabelWrap);
            }
            foreach (var e in list)
            {
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.Label(e.surface, TabletGui.WordStatic, GUILayout.Width(240));
                GUILayout.BeginVertical();
                string gloss = TabletSystem.GlossOf(e);
                GUILayout.Label(string.IsNullOrEmpty(gloss) ? Loc.T("（意味は未記入）", "(no meaning written)") : "≈ " + gloss, string.IsNullOrEmpty(gloss) ? UIStyles.LabelDim : TabletGui.PlayerText);
                string info = TabletText.EntryKindLabel(e.kind);
                if (!string.IsNullOrEmpty(e.posGuess)) info += " / " + TabletText.PosLabel(e.posGuess);
                if (e.tags.Count > 0) info += " / #" + string.Join(" #", e.tags);
                info += Loc.Format(" / 証拠 {0}", " / evidence {0}", e.evidence.Count);
                if (!string.IsNullOrEmpty(e.importedFrom)) info += Loc.Format(" / {0}さんから", " / from {0}", e.importedFrom);
                GUILayout.Label(info, UIStyles.LabelDim);
                GUILayout.EndVertical();
                if (e.pinned) GUILayout.Label("📌", UIStyles.Label, GUILayout.Width(30));
                if (Button(Loc.T("開く", "Open"), 100))
                {
                    entryTrail.Clear();
                    OpenEntry(e, false);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private static bool Matches(DictEntry e, string q)
        {
            if (!string.IsNullOrEmpty(e.surface) && e.surface.ToLowerInvariant().Contains(q)) return true;
            foreach (var h in e.hypotheses) if (!string.IsNullOrEmpty(h.text) && h.text.ToLowerInvariant().Contains(q)) return true;
            if (!string.IsNullOrEmpty(e.memo) && e.memo.ToLowerInvariant().Contains(q)) return true;
            foreach (var t in e.tags) if (t.ToLowerInvariant().Contains(q)) return true;
            return false;
        }

        // ---------------- 新規作成（ページが無い語） ----------------

        private void DrawLookup(Rect area)
        {
            GUILayout.Label(lookupSurface, TabletGui.WordStatic);
            GUILayout.Label(Loc.T("この語はまだ辞書にありません。ページを作りますか？", "This word is not in your dictionary yet. Create a page?"), UIStyles.LabelWrap);
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("単語として作る", "Create as word"), 220, 46)) CreateFromLookup(DictEntryKind.Word);
            if (Button(Loc.T("語の一部として作る", "Create as word part"), 250, 46)) CreateFromLookup(DictEntryKind.Part);
            if (Button(Loc.T("文章として作る", "Create as phrase"), 220, 46)) CreateFromLookup(DictEntryKind.Phrase);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            scroll = GUILayout.BeginScrollView(scroll);
            DrawIndex(lookupSurface, true, area.width - 60);
            GUILayout.EndScrollView();
        }

        private void CreateFromLookup(DictEntryKind kind)
        {
            var e = Sys.CreateEntry(lookupSurface, kind);
            if (!string.IsNullOrEmpty(pendingRecordId))
            {
                var card = Sys.EnsureCardForRecord(pendingRecordId, "辞書の証拠", "Dictionary evidence");
                if (card != null) Sys.AddEvidence(e, "card", card.id);
                pendingRecordId = null;
            }
            entryTrail.Clear();
            OpenEntry(e, false);
        }

        // ---------------- 単語ページ ----------------

        private void DrawEntry(Rect area)
        {
            var e = current;
            if (e == null || !Sys.Know.dictionary.Contains(e))
            {
                view = View.List;
                return;
            }
            if (tagsBufferFor != e.id)
            {
                tagsBuffer = TabletText.JoinTags(e.tags);
                tagsBufferFor = e.id;
            }
            float w = area.width - 50;
            scroll = GUILayout.BeginScrollView(scroll);

            // 見出し
            GUILayout.BeginHorizontal();
            GUILayout.Label(e.surface, new GUIStyle(TabletGui.WordStatic) { fontSize = TabletGui.WordStatic.fontSize + 8, wordWrap = false });
            GUILayout.Space(12);
            foreach (DictEntryKind k in Enum.GetValues(typeof(DictEntryKind)))
            {
                if (TabletGui.ChipToggle(TabletText.EntryKindLabel(k), e.kind == k) && e.kind != k)
                {
                    e.kind = k;
                    Sys.NotifyEntryEdited(e);
                }
            }
            GUILayout.FlexibleSpace();
            if (TabletGui.ChipToggle(e.pinned ? Loc.T("📌ピン留め中", "📌Pinned") : Loc.T("ピン留め", "Pin"), e.pinned)) e.pinned = !e.pinned;
            if (Button(Loc.T("ページを消す", "Delete page"), 160))
            {
                var target = e;
                UIManager.Confirm(Loc.T("ページを消す", "Delete page"), Loc.Format("「{0}」のページを消しますか？（音声カードなどの資料は残ります）", "Delete the page for \"{0}\"? (Voice cards and materials are kept.)", e.surface), () =>
                {
                    Sys.DeleteEntry(target);
                    view = View.List;
                    current = null;
                });
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(e.importedFrom))
                GUILayout.Label(Loc.Format("（{0}さんの辞書から写したページ。内容の正しさは分かりません）", "(Copied from {0}'s dictionary. Its accuracy is unknown.)", e.importedFrom), UIStyles.LabelDim);

            // 開いていた記録を証拠に
            if (!string.IsNullOrEmpty(pendingRecordId) && !IsLinkedRecord(e, pendingRecordId))
            {
                var rec = Sys.ResolveRecord(pendingRecordId);
                if (rec != null)
                {
                    GUILayout.BeginHorizontal(TabletGui.CardBox);
                    GUILayout.Label(Loc.T("さっき見ていた記録：", "Record you were viewing:"), UIStyles.LabelSmall, GUILayout.Width(200));
                    GUILayout.Label(TabletText.Truncate(rec.text, 60), TabletGui.WordStatic);
                    GUILayout.FlexibleSpace();
                    if (Button(Loc.T("この記録を証拠に追加", "Add as evidence"), 240))
                    {
                        var card = Sys.EnsureCardForRecord(pendingRecordId, "辞書の証拠", "Dictionary evidence");
                        if (card != null) Sys.AddEvidence(e, "card", card.id);
                        pendingRecordId = null;
                    }
                    if (Button("×", 44)) pendingRecordId = null;
                    GUILayout.EndHorizontal();
                }
            }

            // 意味の候補
            TabletGui.SectionHeader(Loc.T("意味の候補（自分の推測）", "Possible meanings (your guesses)"));
            GUILayout.Label(Loc.T("ゲームは正しいかどうかを教えません。違うと思ったら「ちがった」にして残しておけます。◉ は吹き出しの推測訳表示に使う候補です。",
                "The game never tells you if you're right. Mark guesses as Rejected instead of deleting them. ◉ is the guess shown as your gloss."), TabletGui.SmallDim);
            Hypothesis remove = null;
            foreach (var h in e.hypotheses)
            {
                GUILayout.BeginHorizontal();
                bool isGloss = e.glossHypothesisId == h.id;
                if (GUILayout.Button(isGloss ? "◉" : "○", UIStyles.ButtonSmall, GUILayout.Width(44), GUILayout.Height(36)))
                {
                    e.glossHypothesisId = isGloss ? null : h.id;
                    Sys.NotifyEntryEdited(e);
                }
                var style = new GUIStyle(UIStyles.TextField);
                style.normal.textColor = TabletGui.PlayerColor;
                style.fontStyle = h.mark == ConfidenceMark.Rejected ? FontStyle.Italic : FontStyle.Normal;
                string t = GUILayout.TextField(h.text ?? "", style, GUILayout.Height(36), GUILayout.Width(Mathf.Min(420, w * 0.4f)));
                if (t != h.text)
                {
                    h.text = t;
                    Sys.NotifyEntryEdited(e);
                }
                var prevColor = GUI.contentColor;
                GUI.contentColor = TabletText.MarkColor(h.mark);
                if (GUILayout.Button(TabletText.MarkLabel(h.mark), UIStyles.ButtonSmall, GUILayout.Width(120), GUILayout.Height(36)))
                {
                    h.mark = (ConfidenceMark)(((int)h.mark + 1) % Enum.GetValues(typeof(ConfidenceMark)).Length);
                    Sys.AddHistory(e, Loc.Format("「{0}」を〈{1}〉にした", "Marked \"{0}\" as {1}", h.text, TabletText.MarkLabel(h.mark)));
                    Sys.NotifyEntryEdited(e);
                }
                GUI.contentColor = prevColor;
                if (!string.IsNullOrEmpty(h.source)) GUILayout.Label(Loc.Format("（{0}さんの説）", "(from {0})", h.source), UIStyles.LabelDim, GUILayout.Width(200));
                if (h.day > 0) GUILayout.Label(TabletText.DayLabel(h.day), UIStyles.LabelDim, GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                if (Button("×", 44)) remove = h;
                GUILayout.EndHorizontal();
            }
            if (remove != null)
            {
                e.hypotheses.Remove(remove);
                if (e.glossHypothesisId == remove.id) e.glossHypothesisId = null;
                Sys.AddHistory(e, Loc.Format("候補「{0}」を消した", "Removed guess \"{0}\"", remove.text));
                Sys.NotifyEntryEdited(e);
            }
            GUILayout.BeginHorizontal();
            newHypothesis = GUILayout.TextField(newHypothesis ?? "", UIStyles.TextField, GUILayout.Height(36), GUILayout.Width(Mathf.Min(420, w * 0.4f)));
            GUI.enabled = !string.IsNullOrEmpty(newHypothesis?.Trim());
            if (Button(Loc.T("候補を追加", "Add guess"), 160))
            {
                var h = new Hypothesis { id = Ids.New(), text = newHypothesis.Trim(), day = Sys.Today };
                e.hypotheses.Add(h);
                Sys.AddHistory(e, Loc.Format("候補「{0}」を書いた", "Wrote guess \"{0}\"", h.text));
                Sys.NotifyEntryEdited(e);
                newHypothesis = "";
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 品詞の推測
            TabletGui.SectionHeader(Loc.T("品詞の推測", "Part of speech (guess)"));
            GUILayout.BeginHorizontal();
            foreach (var key in TabletText.PosKeys)
            {
                bool on = (e.posGuess ?? "") == key;
                if (TabletGui.ChipToggle(TabletText.PosLabel(key), on) && !on)
                {
                    e.posGuess = key;
                    Sys.NotifyEntryEdited(e);
                }
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // メモ・タグ
            TabletGui.SectionHeader(Loc.T("メモ", "Notes"));
            string memo = TabletGui.Area(e.memo, 110);
            if (memo != e.memo)
            {
                e.memo = memo;
                Sys.NotifyEntryEdited(e);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("タグ（,区切り）", "Tags (comma)"), UIStyles.LabelSmall, GUILayout.Width(170));
            string tb = GUILayout.TextField(tagsBuffer ?? "", UIStyles.TextField, GUILayout.Height(34));
            if (tb != tagsBuffer)
            {
                tagsBuffer = tb;
                e.tags = TabletText.SplitTags(tb);
                Sys.NotifyEntryEdited(e);
            }
            GUILayout.EndHorizontal();

            // 関連語・語の一部
            TabletGui.SectionHeader(Loc.T("関連語", "Related words"));
            GUILayout.BeginHorizontal();
            string removeRel = null;
            foreach (var r in e.related)
            {
                var target = Sys.FindEntry(r);
                if (GUILayout.Button(r + (target != null ? "" : Loc.T("（ページなし）", " (no page)")), TabletGui.Chip))
                {
                    if (target != null) OpenEntry(target, true);
                    else ShowLookup(r, null);
                }
                if (GUILayout.Button("×", TabletGui.Chip, GUILayout.Width(30))) removeRel = r;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (removeRel != null)
            {
                e.related.Remove(removeRel);
                Sys.NotifyEntryEdited(e);
            }
            GUILayout.BeginHorizontal();
            newRelated = GUILayout.TextField(newRelated ?? "", UIStyles.TextField, GUILayout.Height(34), GUILayout.Width(220));
            GUI.enabled = !string.IsNullOrEmpty(TabletText.CleanSurface(newRelated));
            if (Button(Loc.T("関連語に追加", "Link word"), 170))
            {
                var s = TabletText.CleanSurface(newRelated);
                if (!e.related.Contains(s)) e.related.Add(s);
                Sys.NotifyEntryEdited(e);
                newRelated = "";
            }
            GUI.enabled = true;
            GUILayout.Space(24);
            GUILayout.Label(Loc.T("語の一部を登録：", "Register a part:"), UIStyles.LabelSmall, GUILayout.Width(170));
            newPart = GUILayout.TextField(newPart ?? "", UIStyles.TextField, GUILayout.Height(34), GUILayout.Width(160));
            GUI.enabled = !string.IsNullOrEmpty(TabletText.CleanSurface(newPart));
            if (Button(Loc.T("一部のページを作る", "Create part page"), 210))
            {
                // 接辞らしき音のまとまりにも注釈できる（§6.4）。区切りはプレイヤー自身が決める。
                var part = Sys.CreateEntry(newPart, DictEntryKind.Part);
                var s = TabletText.CleanSurface(newPart);
                if (!e.related.Contains(s)) e.related.Add(s);
                if (!part.related.Contains(e.surface)) part.related.Add(e.surface);
                Sys.NotifyEntryEdited(e);
                newPart = "";
                OpenEntry(part, true);
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 証拠
            TabletGui.SectionHeader(Loc.Format("証拠（{0}）", "Evidence ({0})", e.evidence.Count));
            EvidenceLink removeEv = null;
            foreach (var ev in e.evidence)
            {
                GUILayout.BeginVertical(TabletGui.CardBox);
                GUILayout.BeginHorizontal();
                if (ev.kind == "card")
                {
                    var card = Sys.FindCard(ev.refId);
                    if (card == null) GUILayout.Label(Loc.T("（消えた音声カード）", "(deleted card)"), UIStyles.LabelDim);
                    else
                    {
                        TabletGui.SpeakerFace(Sys, card.original, 44, Sys.GetNote(card, false));
                        GUILayout.BeginVertical();
                        GUILayout.Label(card.original.text, TabletGui.WordStatic);
                        GUILayout.Label(Sys.SpeakerLabel(card.original, Sys.GetNote(card, false)) + " / " + TabletText.DayTime(card.original.day, card.original.minuteOfDay) + " / " + TabletText.PlaceOfRecord(card.original) + " / " + TabletText.NonverbalSummary(card.original), TabletGui.FactLabel);
                        GUILayout.EndVertical();
                        GUILayout.FlexibleSpace();
                        if (Button("▶", 50)) Sys.ReplayNearPlayer(card.original);
                        if (Button(Loc.T("カード", "Card"), 90)) { Sys.CardsApp.ShowCard(card); Sys.Window?.Navigate(VoiceCardsApp.Id); }
                    }
                }
                else
                {
                    var m = Sys.FindMaterial(ev.refId);
                    GUILayout.Label(m != null ? Loc.T(m.titleJa, m.titleEn) + "：" + Loc.T(m.factJa, m.factEn) : Loc.T("（消えた資料）", "(deleted material)"), TabletGui.FactLabel);
                    GUILayout.FlexibleSpace();
                    if (m != null && Button(Loc.T("資料", "Open"), 90)) { Sys.MaterialsApp.ShowMaterial(m.id); Sys.Window?.Navigate(MaterialsApp.Id); }
                }
                if (Button(Loc.T("外す", "Unlink"), 80)) removeEv = ev;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.T("一言メモ：", "Note:"), UIStyles.LabelSmall, GUILayout.Width(110));
                var style = new GUIStyle(UIStyles.TextField);
                style.normal.textColor = TabletGui.PlayerColor;
                string n = GUILayout.TextField(ev.note ?? "", style, GUILayout.Height(32));
                if (n != ev.note)
                {
                    ev.note = n;
                    Sys.NotifyEntryEdited(e);
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            if (removeEv != null)
            {
                e.evidence.Remove(removeEv);
                Sys.NotifyEntryEdited(e);
            }
            if (e.examples.Count > 0)
            {
                TabletGui.SectionHeader(Loc.T("受け取った例文", "Received examples"));
                foreach (var x in e.examples) GUILayout.Label(x, TabletGui.Small);
            }

            // 自動の索引（事実だけ）
            TabletGui.SectionHeader(Loc.T("この語が出てきた記録（自動の索引）", "Records containing this word (auto index)"));
            GUILayout.Label(Loc.T("保存した音声カード・資料・最近の会話から、同じ綴りが出てくるものを並べています。意味は示しません。",
                "Saved voice cards, materials and recent conversations that contain this spelling. Meanings are not shown."), TabletGui.SmallDim);
            DrawIndex(e.surface, true, w, e);

            // 編集の足跡
            GUILayout.Space(8);
            if (GUILayout.Button(showHistory ? Loc.T("▼ 編集の足跡", "▼ Edit history") : Loc.T("▶ 編集の足跡", "▶ Edit history"), UIStyles.ButtonSmall, GUILayout.Width(220))) showHistory = !showHistory;
            if (showHistory)
            {
                for (int i = e.history.Count - 1; i >= 0; i--) GUILayout.Label(e.history[i], UIStyles.LabelDim);
            }
            GUILayout.Label(Loc.Format("作成 {0} ／ 更新 {1}", "Created {0} / Updated {1}", TabletText.DayLabel(e.createdDay), TabletText.DayLabel(e.updatedDay)), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        private bool IsLinkedRecord(DictEntry e, string recordId)
        {
            var card = Sys.FindCardByRecord(recordId);
            if (card == null) return false;
            foreach (var ev in e.evidence) if (ev.kind == "card" && ev.refId == card.id) return true;
            return false;
        }

        private void DrawIndex(string surface, bool allowPartial, float width, DictEntry entry = null)
        {
            var hits = Sys.FindRecordsContaining(surface, true);
            if (hits.Count == 0)
            {
                GUILayout.Label(Loc.T("（まだ見つかりません）", "(nothing found yet)"), UIStyles.LabelDim);
                return;
            }
            // 単語ページでは完全一致を先に、「語の一部として含む」ものを後に並べる
            hits.Sort((a, b) => a.partial.CompareTo(b.partial));
            foreach (var h in hits)
            {
                if (h.partial && !allowPartial && entry != null) continue;
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, h.record, 40);
                GUILayout.BeginVertical();
                var words = TabletText.Words(h.record);
                string key = TabletText.Normalize(surface);
                TabletGui.WordFlow(words, width - 520, i =>
                {
                    var n = TabletText.Normalize(words[i]);
                    return n == key ? 2 : (n != null && n.Contains(key) ? 1 : 0);
                }, i => EventBus.Publish(new DictionaryLookupRequestEvent { Surface = TabletText.CleanSurface(words[i]), RecordId = h.record.recordId }), false);
                string src = Loc.T(h.sourceJa, h.sourceEn);
                GUILayout.Label(Sys.SpeakerLabel(h.record) + " / " + TabletText.DayTime(h.record.day, h.record.minuteOfDay) + " / " + TabletText.PlaceOfRecord(h.record) + " / " + src
                                + (h.partial ? Loc.T(" / 語の一部として", " / as part of a word") : ""), TabletGui.FactLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button("▶", 50)) Sys.ReplayNearPlayer(h.record);
                if (entry != null && Button(Loc.T("証拠にする", "Use as evidence"), 150))
                {
                    var card = h.cardId != null ? Sys.FindCard(h.cardId) : Sys.EnsureCardForRecord(h.record.recordId, "辞書の証拠", "Dictionary evidence");
                    if (card != null) Sys.AddEvidence(entry, "card", card.id);
                    else if (h.materialId != null) Sys.AddEvidence(entry, "material", h.materialId);
                }
                GUILayout.EndHorizontal();
            }
        }

        // ---------------- 文法ノート ----------------

        private void DrawGrammarList(Rect area)
        {
            GUILayout.Label(Loc.T("語順・否定・疑問・時制などの仮説を自由に書くノートです。2枚の音声カードを並べて、違う語を見比べられます。",
                "Free notes for hypotheses about word order, negation, questions, tense... Put two voice cards side by side to compare."), TabletGui.SmallDim);
            if (Button(Loc.T("＋ 新しい文法ノート", "+ New grammar note"), 260, 44))
            {
                page = Sys.CreateGrammarPage();
                view = View.GrammarPage;
                scroll = Vector2.zero;
                return;
            }
            scroll = GUILayout.BeginScrollView(scroll);
            var pages = new List<GrammarPage>(Sys.Know.grammarPages);
            pages.Sort((a, b) => b.updatedDay.CompareTo(a.updatedDay));
            foreach (var p in pages)
            {
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.BeginVertical();
                GUILayout.Label(string.IsNullOrEmpty(p.title) ? Loc.T("（無題）", "(untitled)") : p.title, UIStyles.LabelBold);
                GUILayout.Label(TabletText.Truncate((p.body ?? "").Replace("\n", " "), 80), TabletGui.PlayerText);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(TabletText.DayLabel(p.updatedDay), UIStyles.LabelDim, GUILayout.Width(110));
                if (Button(Loc.T("開く", "Open"), 100))
                {
                    page = p;
                    view = View.GrammarPage;
                    scroll = Vector2.zero;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawGrammarPage(Rect area)
        {
            var p = page;
            if (p == null || !Sys.Know.grammarPages.Contains(p))
            {
                view = View.Grammar;
                return;
            }
            if (picking != 0)
            {
                DrawCardPicker(area, card =>
                {
                    if (picking == 1) p.compareA = card.id;
                    else if (picking == 2) p.compareB = card.id;
                    else if (!p.cardIds.Contains(card.id)) p.cardIds.Add(card.id);
                    if (picking == 1 || picking == 2) p.marks.RemoveAll(m => m.key == (picking == 1 ? "A" : "B"));
                    p.updatedDay = Sys.Today;
                    picking = 0;
                });
                return;
            }
            float w = area.width - 50;
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("題：", "Title:"), UIStyles.Label, GUILayout.Width(60));
            string t = GUILayout.TextField(p.title ?? "", UIStyles.TextField, GUILayout.Height(36));
            if (t != p.title) { p.title = t; p.updatedDay = Sys.Today; }
            if (Button(Loc.T("ノートを消す", "Delete note"), 160))
            {
                var target = p;
                UIManager.Confirm(Loc.T("ノートを消す", "Delete note"), Loc.T("この文法ノートを消しますか？（貼ったカードは残ります）", "Delete this note? (The cards are kept.)"), () =>
                {
                    Sys.Know.grammarPages.Remove(target);
                    view = View.Grammar;
                });
            }
            GUILayout.EndHorizontal();
            string body = TabletGui.Area(p.body, 150);
            if (body != p.body) { p.body = body; p.updatedDay = Sys.Today; }

            TabletGui.SectionHeader(Loc.T("2つの記録を並べて比べる", "Compare two records"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("単語を押すと：", "Tapping a word:"), UIStyles.LabelSmall, GUILayout.Width(150));
            if (TabletGui.ChipToggle(Loc.T("自分で印を付ける", "Mark it"), compareMarkMode)) compareMarkMode = true;
            if (TabletGui.ChipToggle(Loc.T("辞書を引く", "Look it up"), !compareMarkMode)) compareMarkMode = false;
            GUILayout.Space(20);
            GUILayout.Label(Loc.T("橙＝もう一方に無い語（自動） 紫＝自分の印", "Orange = not in the other (auto)  Purple = your mark"), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            var cardA = Sys.FindCard(p.compareA);
            var cardB = Sys.FindCard(p.compareB);
            var wordsA = cardA != null ? TabletText.Words(cardA.original) : new List<string>();
            var wordsB = cardB != null ? TabletText.Words(cardB.original) : new List<string>();
            bool[] diffA = cardA != null && cardB != null ? TabletText.DiffMask(wordsA, wordsB) : null;
            bool[] diffB = cardA != null && cardB != null ? TabletText.DiffMask(wordsB, wordsA) : null;
            DrawCompareSide(p, "A", cardA, wordsA, diffA, w);
            DrawCompareSide(p, "B", cardB, wordsB, diffB, w);

            TabletGui.SectionHeader(Loc.Format("根拠として貼ったカード（{0}）", "Attached cards ({0})", p.cardIds.Count));
            string removeId = null;
            foreach (var id in p.cardIds)
            {
                var c = Sys.FindCard(id);
                if (c == null) continue;
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, c.original, 40, Sys.GetNote(c, false));
                GUILayout.BeginVertical();
                TabletGui.RecordWords(c.original, w - 400, TabletSettings.GlossOverlay);
                GUILayout.Label(TabletText.DayTime(c.original.day, c.original.minuteOfDay) + " / " + TabletText.NonverbalSummary(c.original), TabletGui.FactLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button("▶", 50)) Sys.ReplayNearPlayer(c.original);
                if (Button(Loc.T("外す", "Remove"), 80)) removeId = id;
                GUILayout.EndHorizontal();
            }
            if (removeId != null) p.cardIds.Remove(removeId);
            if (Button(Loc.T("＋ カードを貼る", "+ Attach card"), 220)) { picking = 3; pickFilter = ""; }
            GUILayout.EndScrollView();
        }

        private void DrawCompareSide(GrammarPage p, string side, VoiceCard card, List<string> words, bool[] diff, float w)
        {
            GUILayout.BeginVertical(TabletGui.CardBox);
            GUILayout.BeginHorizontal();
            GUILayout.Label(side, UIStyles.LabelBold, GUILayout.Width(30));
            if (card != null)
            {
                TabletGui.SpeakerFace(Sys, card.original, 40, Sys.GetNote(card, false));
                GUILayout.Label(Sys.SpeakerLabel(card.original, Sys.GetNote(card, false)) + " / " + TabletText.DayTime(card.original.day, card.original.minuteOfDay) + " / " + TabletText.NonverbalSummary(card.original), TabletGui.FactLabel);
                var note = Sys.GetNote(card, false);
                if (note != null && !string.IsNullOrEmpty(note.guessedMeaning)) GUILayout.Label("≈ " + note.guessedMeaning, TabletGui.PlayerText);
            }
            GUILayout.FlexibleSpace();
            if (card != null && Button("▶", 50)) Sys.ReplayNearPlayer(card.original);
            if (Button(Loc.T("カードを選ぶ", "Choose card"), 170)) { picking = side == "A" ? 1 : 2; pickFilter = ""; }
            GUILayout.EndHorizontal();
            if (card != null)
            {
                TabletGui.WordFlow(words, w - 60, i =>
                {
                    bool d = diff != null && i < diff.Length && diff[i];
                    bool m = HasMark(p, side, i);
                    return d && m ? 3 : m ? 2 : d ? 1 : 0;
                }, i =>
                {
                    if (compareMarkMode) ToggleMark(p, side, i);
                    else EventBus.Publish(new DictionaryLookupRequestEvent { Surface = TabletText.CleanSurface(words[i]), RecordId = card.original.recordId });
                }, TabletSettings.GlossOverlay);
            }
            else GUILayout.Label(Loc.T("（まだ選んでいません）", "(not chosen)"), UIStyles.LabelDim);
            GUILayout.EndVertical();
        }

        private static bool HasMark(GrammarPage p, string side, int index)
        {
            foreach (var m in p.marks) if (m.key == side && m.value == index) return true;
            return false;
        }

        private void ToggleMark(GrammarPage p, string side, int index)
        {
            int removed = p.marks.RemoveAll(m => m.key == side && m.value == index);
            if (removed == 0) p.marks.Add(new StringIntPair(side, index));
            p.updatedDay = Sys.Today;
        }

        /// <summary>音声カードを1枚選ぶ画面（文法ノート用）。</summary>
        private void DrawCardPicker(Rect area, Action<VoiceCard> onPick)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("音声カードを選んでください", "Choose a voice card"), UIStyles.LabelBold);
            GUILayout.Space(20);
            GUILayout.Label(Loc.T("絞り込み：", "Filter:"), UIStyles.LabelSmall, GUILayout.Width(100));
            pickFilter = GUILayout.TextField(pickFilter ?? "", UIStyles.TextField, GUILayout.Width(240), GUILayout.Height(34));
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("やめる", "Cancel"), 120)) picking = 0;
            GUILayout.EndHorizontal();
            if (Sys.Know.cards.Count == 0)
                GUILayout.Label(Loc.T("音声カードがありません。会話ログやマネピの伝言から保存できます。", "No voice cards yet. Save them from the conversation log or Manepi messages."), UIStyles.LabelWrap);
            scroll = GUILayout.BeginScrollView(scroll);
            string q = (pickFilter ?? "").Trim().ToLowerInvariant();
            for (int i = Sys.Know.cards.Count - 1; i >= 0; i--)
            {
                var c = Sys.Know.cards[i];
                var note = Sys.GetNote(c, false);
                if (q.Length > 0)
                {
                    bool hit = (c.original.text ?? "").ToLowerInvariant().Contains(q)
                               || (note != null && ((note.guessedMeaning ?? "").ToLowerInvariant().Contains(q) || (note.memo ?? "").ToLowerInvariant().Contains(q)))
                               || Sys.SpeakerLabel(c.original, note).ToLowerInvariant().Contains(q);
                    if (!hit) continue;
                }
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, c.original, 40, note);
                GUILayout.BeginVertical();
                GUILayout.Label(c.original.text, TabletGui.WordStatic);
                GUILayout.Label(Sys.SpeakerLabel(c.original, note) + " / " + TabletText.DayTime(c.original.day, c.original.minuteOfDay)
                                + (note != null && !string.IsNullOrEmpty(note.guessedMeaning) ? " / ≈ " + note.guessedMeaning : ""), TabletGui.FactLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button(Loc.T("これにする", "Choose"), 140)) onPick(c);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        // ---------------- 別冊 ----------------

        private void DrawVolumes(Rect area)
        {
            GUILayout.Label(Loc.T("ほかのプレイヤーから受け取った辞書です。自分の辞書とは混ざりません。内容が正しいかどうかは分かりません（嘘が書いてあるかも）。",
                "Dictionaries received from other players. They are kept apart from yours, and may be wrong (or even jokes)."), TabletGui.SmallDim);
            scroll = GUILayout.BeginScrollView(scroll);
            if (Sys.Know.volumes.Count == 0) GUILayout.Label(Loc.T("（まだ受け取っていません）", "(none received)"), UIStyles.LabelDim);
            DictVolume remove = null;
            foreach (var v in Sys.Know.volumes)
            {
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.BeginVertical();
                GUILayout.Label(Loc.Format("{0}さんの辞書", "{0}'s dictionary", v.fromPlayerName), UIStyles.LabelBold);
                string info = Loc.Format("{0}語 ／ 受け取った日：{1}", "{0} words / received {1}", v.entries.Count, TabletText.DayLabel(v.receivedDay));
                if (!string.IsNullOrEmpty(v.sourceWorldId) && Sys.Know.worldId != null && v.sourceWorldId != Sys.Know.worldId)
                    info += Loc.T(" ／ ⚠ 別の星の辞書のようです", " / ⚠ seems to be from another planet");
                GUILayout.Label(info, UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button(Loc.T("読む", "Read"), 100))
                {
                    volume = v;
                    view = View.Volume;
                    scroll = Vector2.zero;
                }
                if (Button(Loc.T("捨てる", "Discard"), 100)) remove = v;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (remove != null)
            {
                var target = remove;
                UIManager.Confirm(Loc.T("別冊を捨てる", "Discard volume"), Loc.T("この別冊を捨てますか？（自分の辞書に写した分は残ります）", "Discard this volume? (Copied pages stay in your dictionary.)"),
                    () => Sys.Know.volumes.Remove(target));
            }
        }

        private void DrawVolume(Rect area)
        {
            var v = volume;
            if (v == null || !Sys.Know.volumes.Contains(v))
            {
                view = View.Volumes;
                return;
            }
            GUILayout.Label(Loc.Format("{0}さんの辞書（読み取り専用）", "{0}'s dictionary (read-only)", v.fromPlayerName), TabletGui.Header);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var e in v.entries)
            {
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                GUILayout.Label(e.surface, TabletGui.WordStatic, GUILayout.Width(220));
                GUILayout.BeginVertical();
                foreach (var h in e.hypotheses)
                    GUILayout.Label($"[{TabletText.MarkLabel(h.mark)}] {h.text}" + (!string.IsNullOrEmpty(h.source) && h.source != v.fromPlayerName ? Loc.Format("（{0}さんの説）", " (from {0})", h.source) : ""), TabletGui.PlayerText);
                if (!string.IsNullOrEmpty(e.memo)) GUILayout.Label(TabletText.Truncate(e.memo, 120), TabletGui.Small);
                foreach (var x in e.examples) GUILayout.Label(x, UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                var mine = Sys.FindEntry(e.surface);
                if (Button(mine == null ? Loc.T("自分の辞書へ写す", "Copy to mine") : Loc.T("自分のページに追記", "Add to my page"), 220))
                {
                    if (mine == null) Sys.Know.dictionary.Add(Sys.CopyEntryFrom(e, v.fromPlayerName));
                    else Sys.MergeInto(mine, e, v.fromPlayerName);
                    var target = Sys.FindEntry(e.surface);
                    Sys.NotifyEntryEdited(target);
                    UIManager.Toast(Loc.Format("「{0}」を自分の辞書に写しました", "Copied \"{0}\" to your dictionary", e.surface));
                }
                GUILayout.EndHorizontal();
            }
            if (v.pages.Count > 0)
            {
                TabletGui.SectionHeader(Loc.T("文法ノート", "Grammar notes"));
                foreach (var p in v.pages)
                {
                    GUILayout.BeginVertical(TabletGui.CardBox);
                    GUILayout.Label(p.title, UIStyles.LabelBold);
                    GUILayout.Label(p.body, TabletGui.Small);
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndScrollView();
        }

        // ---------------- 渡す・受け取る ----------------

        private void DrawShare(Rect area)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(Loc.T("辞書は自動では配られません。渡すかどうか、どこまで渡すかは自分で決めます。渡すのはコピーなので、自分の辞書は減りません。受け取った辞書の正しさをゲームが判定することはありません。",
                "Dictionaries are never handed out automatically. You decide whether and how much to share. Sharing gives a copy; your own stays. The game never judges whether a received dictionary is correct."), UIStyles.LabelWrap);

            TabletGui.SectionHeader(Loc.T("書き出す（渡す）", "Export (give)"));
            GUILayout.BeginHorizontal();
            if (TabletGui.ChipToggle(Loc.T("全部", "Everything"), shareAll)) shareAll = true;
            if (TabletGui.ChipToggle(Loc.T("選んだ語だけ", "Selected words"), !shareAll)) shareAll = false;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (!shareAll)
            {
                GUILayout.BeginHorizontal();
                int col = 0;
                foreach (var e in Sys.Know.dictionary)
                {
                    bool on = shareSelection.Contains(e.surface);
                    if (TabletGui.ChipToggle((on ? "☑ " : "☐ ") + e.surface, on))
                    {
                        if (on) shareSelection.Remove(e.surface);
                        else shareSelection.Add(e.surface);
                    }
                    if (++col % 6 == 0)
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("書き出してクリップボードへ", "Export to clipboard"), 320, 44))
            {
                string json = Sys.ExportDictionary(shareAll ? null : new List<string>(shareSelection));
                GUIUtility.systemCopyBuffer = json;
                UIManager.Toast(Loc.T("辞書を書き出してクリップボードに入れました（マルチプレイの「辞書を渡す」からも渡せます）", "Exported to clipboard (you can also hand it over from the multiplayer menu)"));
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            TabletGui.SectionHeader(Loc.T("受け取る（クリップボードから）", "Import (from clipboard)"));
            importFromName = TabletGui.Field(Loc.T("送り主の名前", "From (name)"), importFromName);
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("別冊として受け取る", "Receive as separate volume"), 320, 44)) TryImport(true);
            if (Button(Loc.T("自分の辞書に統合する", "Merge into mine"), 300, 44)) TryImport(false);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("統合しても自分の書いた内容は上書きされず、相手の仮説が「◯◯さんの説」として追記されます。",
                "Merging never overwrites your own notes; the other player's guesses are added and labelled with their name."), TabletGui.SmallDim);
            GUILayout.EndScrollView();
        }

        private void TryImport(bool separate)
        {
            string json = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(json) || !json.TrimStart().StartsWith("{"))
            {
                UIManager.Toast(Loc.T("クリップボードに辞書のデータがありません", "No dictionary data in the clipboard"));
                return;
            }
            string worldId = null;
            try { worldId = JsonUtility.FromJson<DictionaryExportData>(json)?.worldId; }
            catch { }
            string name = importFromName;
            Action go = () => Sys.ImportDictionary(json, name, separate);
            if (!string.IsNullOrEmpty(worldId) && Sys.Know.worldId != null && worldId != Sys.Know.worldId)
            {
                UIManager.Confirm(Loc.T("別の星の辞書？", "Another planet's dictionary?"),
                    Loc.T("この辞書は別の星で書かれたもののようです。星が違うと言葉も違います。それでも受け取りますか？", "This dictionary seems to come from a different planet, where the language differs. Receive it anyway?"), go);
            }
            else go();
        }
    }
}
