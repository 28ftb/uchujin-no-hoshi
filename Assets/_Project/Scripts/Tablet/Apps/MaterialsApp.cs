using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 資料ボックス（タブレット仕様 §8）。売上の事実・依頼・手紙・看板・写真・予報などを種類別に一元管理する。
    /// ・原記録は保持し、プレイヤーのメモ・関連付け（住民・単語）は別に保存する（§8.3）。
    /// ・発言者が判別できない資料はそのまま未分類で保管できる。
    /// 音声カードは「音声カード」アプリの原本を参照して並べる（二重に複製しない §8.1）。
    /// </summary>
    internal sealed class MaterialsApp : TabletAppBase
    {
        public const string Id = "materials";

        private int kindFilter = -1; // -1=すべて、-2=音声カード（カードアプリの原本）
        private string currentId;
        private string newSurface = "";

        internal MaterialsApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("資料ボックス", "Materials");
        public override int Order => 40;
        public override string IconGlyph => Loc.T("箱", "Box");
        public override Color IconColor => new Color(0.82f, 0.62f, 0.32f);

        public override void ResetState()
        {
            base.ResetState();
            kindFilter = -1;
            currentId = null;
        }

        public override bool TryGoBack()
        {
            if (currentId != null)
            {
                currentId = null;
                return true;
            }
            return false;
        }

        public void ShowMaterial(string materialId)
        {
            currentId = materialId;
            scroll = Vector2.zero;
        }

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            var m = Sys.FindMaterial(currentId);
            if (m != null) DrawDetail(area, m);
            else
            {
                currentId = null;
                DrawList(area);
            }
        }

        private void DrawList(Rect area)
        {
            GUILayout.BeginHorizontal();
            if (TabletGui.ChipToggle(Loc.T("すべて", "All"), kindFilter == -1)) kindFilter = -1;
            if (TabletGui.ChipToggle(Loc.Format("音声カード（{0}）", "Voice cards ({0})", Sys.Know.cards.Count), kindFilter == -2)) kindFilter = -2;
            foreach (TabletMaterialKind k in Enum.GetValues(typeof(TabletMaterialKind)))
            {
                if (k == TabletMaterialKind.VoiceCard) continue;
                int n = 0;
                foreach (var x in Sys.Know.materials) if (x.kind == k) n++;
                if (n == 0) continue;
                if (TabletGui.ChipToggle($"{TabletText.KindLabel(k)}（{n}）", kindFilter == (int)k)) kindFilter = (int)k;
            }
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("📷 写真を撮る", "📷 Take photo"), 200)) Sys.PhotoCamera.Begin(null, Id);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            if (kindFilter == -2)
            {
                DrawCardsList();
                GUILayout.EndScrollView();
                return;
            }
            var list = new List<TabletMaterial>();
            foreach (var x in Sys.Know.materials)
                if (kindFilter == -1 || (int)x.kind == kindFilter) list.Add(x);
            list.Sort((a, b) => ((long)b.day * 1440 + b.minuteOfDay).CompareTo((long)a.day * 1440 + a.minuteOfDay));
            if (list.Count == 0)
                GUILayout.Label(Loc.T("（資料はまだありません。商店の売上・依頼・写真などがここに集まります）", "(No materials yet. Sales, requests, photos and more collect here.)"), UIStyles.LabelWrap);
            foreach (var x in list)
            {
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                if (!string.IsNullOrEmpty(x.linkedResidentId))
                {
                    var page = Sys.FindResidentPage(x.linkedResidentId);
                    TabletGui.FaceLayout(48, page != null ? TabletSystem.PageColor(page) : Sys.SpeakerColor(x.linkedResidentId), page != null ? page.lineage : 0, Expression.Neutral);
                }
                else
                {
                    var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                    UIStyles.FillRect(r, new Color(0.3f, 0.34f, 0.45f));
                    GUI.Label(r, KindGlyph(x.kind), new GUIStyle(TabletGui.Glyph) { fontSize = 22 });
                }
                GUILayout.BeginVertical();
                GUILayout.Label($"[{TabletText.KindLabel(x.kind)}] {Loc.T(x.titleJa, x.titleEn)}", UIStyles.LabelBold);
                GUILayout.Label(TabletText.Truncate(Loc.T(x.factJa, x.factEn), 110), TabletGui.FactLabel);
                var note = Sys.GetMaterialNote(x.id, false);
                if (note != null && !string.IsNullOrEmpty(note.memo)) GUILayout.Label(TabletText.Truncate(note.memo, 80), TabletGui.PlayerText);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUILayout.Label(TabletText.DayTime(x.day, x.minuteOfDay), UIStyles.LabelDim, GUILayout.Width(170));
                if (Button(Loc.T("開く", "Open"), 90)) ShowMaterial(x.id);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawCardsList()
        {
            for (int i = Sys.Know.cards.Count - 1; i >= 0; i--)
            {
                var c = Sys.Know.cards[i];
                var note = Sys.GetNote(c, false);
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(Sys, c.original, 48, note);
                GUILayout.BeginVertical();
                GUILayout.Label(c.original.text, TabletGui.WordStatic);
                GUILayout.Label(Sys.SpeakerLabel(c.original, note) + " / " + TabletText.DayTime(c.original.day, c.original.minuteOfDay) + " / " + Loc.T(c.sourceJa, c.sourceEn), TabletGui.FactLabel);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button(Loc.T("開く", "Open"), 90))
                {
                    Sys.CardsApp.ShowCard(c);
                    Sys.Window?.Navigate(VoiceCardsApp.Id);
                }
                GUILayout.EndHorizontal();
            }
        }

        private static string KindGlyph(TabletMaterialKind k)
        {
            switch (k)
            {
                case TabletMaterialKind.Photo: return "📷";
                case TabletMaterialKind.SalesRecord: return "G";
                case TabletMaterialKind.QuestRequest: return "!";
                case TabletMaterialKind.Letter: return "✉";
                case TabletMaterialKind.Sign: return "□";
                case TabletMaterialKind.Recipe: return "♨";
                case TabletMaterialKind.Forecast: return "☂";
                case TabletMaterialKind.Conversation: return "…";
                default: return "✎";
            }
        }

        private void DrawDetail(Rect area, TabletMaterial m)
        {
            float w = area.width - 50;
            var note = Sys.GetMaterialNote(m.id, true);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginVertical(TabletGui.CardBox);
            GUILayout.Label($"[{TabletText.KindLabel(m.kind)}] {Loc.T(m.titleJa, m.titleEn)}", TabletGui.Header);
            GUILayout.Label(TabletText.DayTime(m.day, m.minuteOfDay), UIStyles.LabelDim);
            if (!string.IsNullOrEmpty(m.factJa) || !string.IsNullOrEmpty(m.factEn)) GUILayout.Label(Loc.T(m.factJa, m.factEn), TabletGui.FactLabel);

            // 写真
            if (m.kind == TabletMaterialKind.Photo)
            {
                var photo = PhotoOf(m);
                if (photo != null)
                {
                    TabletGui.PhotoLayout(photo, Mathf.Min(w - 40, 900), 480);
                    if (photo.residentIds.Count > 0)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(Loc.T("写っていた住民：", "In the photo:"), UIStyles.LabelSmall, GUILayout.Width(170));
                        foreach (var rid in photo.residentIds)
                        {
                            var page = Sys.FindResidentPage(rid);
                            if (page == null) continue;
                            TabletGui.FaceLayout(36, TabletSystem.PageColor(page), page.lineage, Expression.Neutral, page.movedOut);
                            if (GUILayout.Button(TabletSystem.ResidentLabel(page), TabletGui.Chip)) OpenResident(rid);
                        }
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    photo.caption = PlayerLine(Loc.T("写真の説明", "Caption"), photo.caption);
                }
            }

            // 結び付いた発話
            if (!string.IsNullOrEmpty(m.linkedRecordId))
            {
                var rec = Sys.ResolveRecord(m.linkedRecordId);
                if (rec != null)
                {
                    GUILayout.Space(6);
                    GUILayout.BeginHorizontal();
                    TabletGui.SpeakerFace(Sys, rec, 56);
                    GUILayout.BeginVertical();
                    TabletGui.RecordWords(rec, w - 400, TabletSettings.GlossOverlay);
                    GUILayout.Label(Sys.SpeakerLabel(rec) + " / " + TabletText.NonverbalSummary(rec), TabletGui.FactLabel);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    if (Button("▶", 50)) Sys.ReplayNearPlayer(rec);
                    var card = Sys.FindCardByRecord(rec.recordId);
                    if (card == null && Button(Loc.T("音声カードとして保存", "Save as voice card"), 240)) Sys.SaveVoiceCard(rec, string.IsNullOrEmpty(m.titleJa) ? "資料ボックス" : m.titleJa, string.IsNullOrEmpty(m.titleEn) ? "Materials" : m.titleEn);
                    GUILayout.EndHorizontal();
                }
            }
            if (!string.IsNullOrEmpty(m.linkedResidentId))
            {
                GUILayout.BeginHorizontal();
                var page = Sys.FindResidentPage(m.linkedResidentId);
                TabletGui.FaceLayout(40, page != null ? TabletSystem.PageColor(page) : Sys.SpeakerColor(m.linkedResidentId), page != null ? page.lineage : 0, Expression.Neutral, page != null && page.movedOut);
                GUILayout.Label(Sys.ResidentLabel(m.linkedResidentId), UIStyles.Label);
                if (Button(Loc.T("住民辞典を開く", "Open resident page"), 220)) OpenResident(m.linkedResidentId);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            var rel = new List<string>();
            foreach (var id in m.relatedIds) { var l = TabletText.RelatedLabel(id); if (!string.IsNullOrEmpty(l) && !rel.Contains(l)) rel.Add(l); }
            if (rel.Count > 0) GUILayout.Label(Loc.Format("関係する物：{0}", "Related: {0}", string.Join(Loc.T("、", ", "), rel)), TabletGui.FactLabel);
            if (m.tags.Count > 0) GUILayout.Label("#" + string.Join(" #", m.tags), UIStyles.LabelDim);
            GUILayout.EndVertical();

            // ---- プレイヤーのメモと関連付け ----
            TabletGui.SectionHeader(Loc.T("自分のメモ", "My notes"));
            note.memo = TabletGui.Area(note.memo, 90);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("関係する住民：", "Residents:"), UIStyles.LabelSmall, GUILayout.Width(160));
            string removeRes = null;
            foreach (var rid in note.residentIds)
            {
                if (GUILayout.Button(Sys.ResidentLabel(rid), TabletGui.Chip)) OpenResident(rid);
                if (GUILayout.Button("×", TabletGui.Chip, GUILayout.Width(30))) removeRes = rid;
            }
            if (Button(Loc.T("＋ 住民", "+ Resident"), 130)) ChooseResident(m, note);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (removeRes != null)
            {
                note.residentIds.Remove(removeRes);
                Sys.FindResidentPage(removeRes)?.attachedMaterialIds.Remove(m.id);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("関係する単語：", "Words:"), UIStyles.LabelSmall, GUILayout.Width(160));
            string removeWord = null;
            foreach (var s in note.surfaces)
            {
                if (GUILayout.Button(s, TabletGui.Chip)) EventBus.Publish(new DictionaryLookupRequestEvent { Surface = s, RecordId = m.linkedRecordId });
                if (GUILayout.Button("×", TabletGui.Chip, GUILayout.Width(30))) removeWord = s;
            }
            newSurface = GUILayout.TextField(newSurface ?? "", UIStyles.TextField, GUILayout.Width(180), GUILayout.Height(34));
            GUI.enabled = !string.IsNullOrEmpty(TabletText.CleanSurface(newSurface));
            if (Button(Loc.T("＋ 単語の証拠にする", "+ Use as word evidence"), 240))
            {
                var s = TabletText.CleanSurface(newSurface);
                if (!note.surfaces.Contains(s)) note.surfaces.Add(s);
                var e = Sys.FindEntry(s) ?? Sys.CreateEntry(s, DictEntryKind.Word);
                Sys.AddEvidence(e, "material", m.id);
                newSurface = "";
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            if (removeWord != null)
            {
                note.surfaces.Remove(removeWord);
                var e = Sys.FindEntry(removeWord);
                if (e != null) e.evidence.RemoveAll(ev => ev.kind == "material" && ev.refId == m.id);
            }

            GUILayout.Space(10);
            if (Button(Loc.T("この資料を捨てる", "Discard this material"), 240))
            {
                var target = m;
                UIManager.Confirm(Loc.T("資料を捨てる", "Discard material"), Loc.T("この資料を捨てますか？", "Discard this material?"), () =>
                {
                    Sys.DeleteMaterial(target);
                    currentId = null;
                });
            }
            GUILayout.EndScrollView();
        }

        private PhotoInfo PhotoOf(TabletMaterial m)
        {
            if (m.id != null && m.id.StartsWith("photo:")) return Sys.FindPhoto(m.id.Substring(6));
            foreach (var id in m.relatedIds) { var p = Sys.FindPhoto(id); if (p != null) return p; }
            return null;
        }

        private void OpenResident(string residentId)
        {
            Sys.Window?.Navigate(ResidentsApp.Id);
            Sys.ResidentsApp.ShowPage(residentId);
        }

        private void ChooseResident(TabletMaterial m, MaterialNote note)
        {
            var opts = new List<ChoiceOption>();
            foreach (var p in Sys.Know.residents)
            {
                var page = p;
                opts.Add(new ChoiceOption(TabletSystem.ResidentLabel(page), () =>
                {
                    if (!note.residentIds.Contains(page.residentId)) note.residentIds.Add(page.residentId);
                    if (!page.attachedMaterialIds.Contains(m.id)) page.attachedMaterialIds.Add(m.id);
                }));
            }
            if (opts.Count == 0)
            {
                UIManager.Message(Loc.T("住民辞典", "Resident book"), Loc.T("住民辞典にまだページがありません。", "No resident pages yet."));
                return;
            }
            UIManager.Choose(Loc.T("どの住民に関係する？", "Which resident?"), null, opts);
        }

        private static string PlayerLine(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, UIStyles.LabelSmall, GUILayout.Width(160));
            var style = new GUIStyle(UIStyles.TextField);
            style.normal.textColor = TabletGui.PlayerColor;
            value = GUILayout.TextField(value ?? "", style, GUILayout.Height(34));
            GUILayout.EndHorizontal();
            return value;
        }
    }
}
