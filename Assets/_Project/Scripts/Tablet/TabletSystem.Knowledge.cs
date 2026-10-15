using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 辞書・音声カード・資料ボックス・住民辞典のデータ操作（UI から呼ばれる）。
    /// ここで作るのは「観察できた事実」と「プレイヤーの記述」だけ。内部の意味・好み・名前は書かない。
    /// </summary>
    public sealed partial class TabletSystem
    {
        internal int Today => Clock != null ? Clock.Day : 0;
        internal int NowMinute => Clock != null ? Clock.MinuteOfDay : 0;

        // ============================================================
        // 宇宙語辞書
        // ============================================================

        // 綴り → 項目の索引（吹き出しの推測訳表示で毎フレーム引かれるため）。綴りは作成後に変わらないので、件数か辞書の入れ替わりで作り直す。
        private readonly Dictionary<string, DictEntry> entryIndex = new Dictionary<string, DictEntry>();
        private List<DictEntry> indexedList;
        private int indexedCount = -1;

        internal DictEntry FindEntry(string surface)
        {
            string key = TabletText.Normalize(surface);
            if (string.IsNullOrEmpty(key)) return null;
            if (indexedList != Know.dictionary || indexedCount != Know.dictionary.Count)
            {
                entryIndex.Clear();
                foreach (var e in Know.dictionary)
                {
                    var k = TabletText.Normalize(e.surface);
                    if (!string.IsNullOrEmpty(k) && !entryIndex.ContainsKey(k)) entryIndex[k] = e;
                }
                indexedList = Know.dictionary;
                indexedCount = Know.dictionary.Count;
            }
            return entryIndex.TryGetValue(key, out var found) ? found : null;
        }

        internal DictEntry FindEntryById(string id)
        {
            foreach (var e in Know.dictionary) if (e.id == id) return e;
            return null;
        }

        internal DictEntry CreateEntry(string surface, DictEntryKind kind)
        {
            var existing = FindEntry(surface);
            if (existing != null) return existing;
            var e = new DictEntry
            {
                id = Ids.New(),
                surface = TabletText.CleanSurface(surface),
                kind = kind,
                createdDay = Today,
                updatedDay = Today
            };
            AddHistory(e, Loc.T("ページを作った", "Created the page"));
            Know.dictionary.Add(e);
            NotifyEntryEdited(e);
            return e;
        }

        internal void DeleteEntry(DictEntry e)
        {
            if (e == null) return;
            Know.dictionary.Remove(e);
            indexedCount = -1;
            NotifyEntryEdited(e);
        }

        internal void AddHistory(DictEntry e, string text)
        {
            if (e == null || string.IsNullOrEmpty(text)) return;
            if (e.history == null) e.history = new List<string>();
            e.history.Add(Loc.Format("{0}：{1}", "{0}: {1}", TabletText.DayLabel(Today), text));
            while (e.history.Count > TabletTuning.MaxHistoryLines) e.history.RemoveAt(0);
        }

        internal void NotifyEntryEdited(DictEntry e)
        {
            if (e == null) return;
            e.updatedDay = Today;
            EventBus.Publish(new DictionaryEntryEditedEvent { PlayerId = LocalId, Surface = e.surface });
        }

        /// <summary>音声カードを辞書項目の証拠に結び付ける（重複は無視）。</summary>
        internal bool AddEvidence(DictEntry e, string kind, string refId)
        {
            if (e == null || string.IsNullOrEmpty(refId)) return false;
            foreach (var ev in e.evidence) if (ev.kind == kind && ev.refId == refId) return false;
            e.evidence.Add(new EvidenceLink { kind = kind, refId = refId, day = Today, note = "" });
            AddHistory(e, Loc.T("証拠を追加した", "Added evidence"));
            NotifyEntryEdited(e);
            return true;
        }

        /// <summary>記録IDから証拠に使えるカードを得る（カード未保存の会話なら、その場で音声カードとして保存する）。</summary>
        internal VoiceCard EnsureCardForRecord(string recordId, string labelJa, string labelEn)
        {
            if (string.IsNullOrEmpty(recordId)) return null;
            var card = FindCardByRecord(recordId);
            if (card != null) return card;
            var rec = ResolveRecord(recordId);
            if (rec == null) return null;
            string id = SaveVoiceCard(rec, labelJa, labelEn);
            return FindCard(id);
        }

        /// <summary>
        /// その語が出てきた保存済み記録の自動一覧（事実の索引。意味は示さない §6）。
        /// 音声カード・資料に結び付いた記録・最近の会話ログを対象に、綴りが一致する語を含むものを探す。
        /// </summary>
        internal List<IndexHit> FindRecordsContaining(string surface, bool allowPartial)
        {
            var hits = new List<IndexHit>();
            string key = TabletText.Normalize(surface);
            if (string.IsNullOrEmpty(key)) return hits;
            var seen = new HashSet<string>();
            foreach (var c in Know.cards)
            {
                if (hits.Count >= TabletTuning.MaxIndexHits) break;
                if (c.original == null) continue;
                int m = TabletText.MatchKind(c.original, key, allowPartial);
                if (m == 0) continue;
                if (!string.IsNullOrEmpty(c.original.recordId)) seen.Add(c.original.recordId);
                hits.Add(new IndexHit { record = c.original, cardId = c.id, partial = m == 2, sourceJa = c.sourceJa, sourceEn = c.sourceEn });
            }
            foreach (var mat in Know.materials)
            {
                if (hits.Count >= TabletTuning.MaxIndexHits) break;
                if (string.IsNullOrEmpty(mat.linkedRecordId) || seen.Contains(mat.linkedRecordId)) continue;
                var rec = ResolveRecord(mat.linkedRecordId);
                if (rec == null) continue;
                int m = TabletText.MatchKind(rec, key, allowPartial);
                if (m == 0) continue;
                seen.Add(mat.linkedRecordId);
                hits.Add(new IndexHit { record = rec, materialId = mat.id, partial = m == 2, sourceJa = mat.titleJa, sourceEn = mat.titleEn });
            }
            var speech = Uchujin.Core.Game.Instance?.Service<ISpeechService>();
            var recent = speech?.RecentRecords;
            if (recent != null)
            {
                for (int i = recent.Count - 1; i >= 0 && hits.Count < TabletTuning.MaxIndexHits; i--)
                {
                    var rec = recent[i];
                    if (rec == null || (!string.IsNullOrEmpty(rec.recordId) && seen.Contains(rec.recordId))) continue;
                    int m = TabletText.MatchKind(rec, key, allowPartial);
                    if (m == 0) continue;
                    if (!string.IsNullOrEmpty(rec.recordId)) seen.Add(rec.recordId);
                    hits.Add(new IndexHit { record = rec, partial = m == 2, sourceJa = "会話ログ", sourceEn = "Conversation log" });
                }
            }
            return hits;
        }

        internal class IndexHit
        {
            public UtteranceRecord record;
            public string cardId;
            public string materialId;
            public bool partial;
            public string sourceJa;
            public string sourceEn;
        }

        internal GrammarPage CreateGrammarPage()
        {
            var p = new GrammarPage { id = Ids.New(), title = Loc.T("新しい文法ノート", "New grammar note"), createdDay = Today, updatedDay = Today };
            Know.grammarPages.Add(p);
            return p;
        }

        // ============================================================
        // 音声カード
        // ============================================================

        internal VoiceCard FindCard(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var c in Know.cards) if (c.id == id) return c;
            return null;
        }

        internal VoiceCard FindCardByRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return null;
            foreach (var c in Know.cards) if (c.original != null && c.original.recordId == recordId) return c;
            return null;
        }

        internal CardNote GetNote(VoiceCard card, bool create)
        {
            if (card == null) return null;
            foreach (var n in Know.cardNotes) if (n.cardId == card.id) return n;
            if (!create) return null;
            var note = new CardNote { cardId = card.id };
            Know.cardNotes.Add(note);
            return note;
        }

        public string SaveVoiceCard(UtteranceRecord record, string sourceLabelJa, string sourceLabelEn)
        {
            if (record == null) return null;
            var existing = FindCardByRecord(record.recordId);
            if (existing != null)
            {
                UIManager.Toast(Loc.T("この声はもう音声カードに保存してあります", "This voice is already saved as a card"));
                return existing.id;
            }
            // 原本はコピーして保存し、以後変更しない（§5.3, §14）
            var card = new VoiceCard
            {
                id = Ids.New(),
                original = TabletText.CloneRecord(record),
                sourceJa = string.IsNullOrEmpty(sourceLabelJa) ? "記録" : sourceLabelJa,
                sourceEn = string.IsNullOrEmpty(sourceLabelEn) ? "Record" : sourceLabelEn,
                savedDay = Today,
                savedMinute = NowMinute
            };
            if (string.IsNullOrEmpty(card.original.recordId)) card.original.recordId = "card:" + card.id;
            Know.cards.Add(card);
            GetNote(card, true);
            UIManager.Toast(Loc.Format("音声カードとして保存しました（{0}）", "Saved as a voice card ({0})", Loc.T(card.sourceJa, card.sourceEn)));
            return card.id;
        }

        internal void DeleteCard(VoiceCard card)
        {
            if (card == null) return;
            Know.cards.Remove(card);
            Know.cardNotes.RemoveAll(n => n.cardId == card.id);
            foreach (var e in Know.dictionary) e.evidence.RemoveAll(ev => ev.kind == "card" && ev.refId == card.id);
            foreach (var r in Know.residents) r.attachedCardIds.Remove(card.id);
            foreach (var p in Know.grammarPages)
            {
                p.cardIds.Remove(card.id);
                if (p.compareA == card.id) p.compareA = null;
                if (p.compareB == card.id) p.compareB = null;
            }
        }

        /// <summary>記録IDから発話記録を探す（カード→資料→会話ログ→言語モジュール）。</summary>
        internal UtteranceRecord ResolveRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId)) return null;
            var card = FindCardByRecord(recordId);
            if (card != null) return card.original;
            foreach (var b in ManepiData.boards)
                foreach (var m in b.messages)
                    if (m != null && m.recordId == recordId) return m;
            var speech = Uchujin.Core.Game.Instance?.Service<ISpeechService>();
            return speech?.GetRecord(recordId);
        }

        /// <summary>音声カードを再生（主人公の近くで吹き出しを出す。原本は変えない）。</summary>
        internal void ReplayNearPlayer(UtteranceRecord rec)
        {
            if (rec == null) return;
            var speech = Uchujin.Core.Game.Instance?.Service<ISpeechService>();
            var player = Uchujin.Core.Game.Instance?.LocalPlayer;
            if (speech == null)
            {
                UIManager.Toast(Loc.T("再生できません（言語モジュールなし）", "Cannot play (no speech module)"));
                return;
            }
            speech.Replay(rec, player != null ? player.Avatar : null);
        }

        // ============================================================
        // 資料ボックス
        // ============================================================

        public void AddMaterial(TabletMaterial material)
        {
            if (material == null) return;
            if (string.IsNullOrEmpty(material.id)) material.id = Ids.New();
            foreach (var m in Know.materials) if (m.id == material.id) return;
            var copy = JsonUtility.FromJson<TabletMaterial>(JsonUtility.ToJson(material));
            if (copy.relatedIds == null) copy.relatedIds = new List<string>();
            if (copy.tags == null) copy.tags = new List<string>();
            if (copy.day <= 0 && Clock != null)
            {
                copy.day = Today;
                copy.minuteOfDay = NowMinute;
            }
            Know.materials.Add(copy);
            if (!string.IsNullOrEmpty(copy.linkedResidentId)) GetOrCreatePage(copy.linkedResidentId);
        }

        internal TabletMaterial FindMaterial(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var m in Know.materials) if (m.id == id) return m;
            return null;
        }

        internal MaterialNote GetMaterialNote(string materialId, bool create)
        {
            foreach (var n in Know.materialNotes) if (n.materialId == materialId) return n;
            if (!create) return null;
            var note = new MaterialNote { materialId = materialId };
            Know.materialNotes.Add(note);
            return note;
        }

        internal void DeleteMaterial(TabletMaterial m)
        {
            if (m == null) return;
            Know.materials.Remove(m);
            Know.materialNotes.RemoveAll(n => n.materialId == m.id);
            foreach (var e in Know.dictionary) e.evidence.RemoveAll(ev => ev.kind == "material" && ev.refId == m.id);
            foreach (var r in Know.residents) r.attachedMaterialIds.Remove(m.id);
        }

        // ============================================================
        // 住民辞典
        // ============================================================

        internal ResidentPage FindResidentPage(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return null;
            foreach (var p in Know.residents) if (p.residentId == residentId) return p;
            return null;
        }

        /// <summary>ページを用意する（会っていなくても、商店の記録などがあればページはできる。metDay=-1）。</summary>
        internal ResidentPage GetOrCreatePage(string residentId)
        {
            var page = FindResidentPage(residentId);
            if (page != null)
            {
                RefreshFace(page);
                return page;
            }
            page = new ResidentPage { residentId = residentId, number = Know.nextResidentNumber++ };
            RefreshFace(page);
            Know.residents.Add(page);
            return page;
        }

        /// <summary>顔アイコン用の見た目（観察できる外見）を更新。引っ越し後も残るよう保存しておく。</summary>
        private void RefreshFace(ResidentPage page)
        {
            var info = Uchujin.Core.Game.Instance?.Service<IResidentDirectory>()?.Get(page.residentId);
            if (info == null) return;
            var c = info.MainColor;
            page.colorR = c.r;
            page.colorG = c.g;
            page.colorB = c.b;
            page.lineage = (int)info.Lineage;
        }

        public void RecordResidentMet(string residentId)
        {
            if (string.IsNullOrEmpty(residentId)) return;
            var page = GetOrCreatePage(residentId);
            if (page.metDay >= 0) return;
            page.metDay = Today;
            page.metMinute = NowMinute;
            var player = Uchujin.Core.Game.Instance?.LocalPlayer;
            var place = TabletText.PlaceOf(player != null ? player.CurrentAreaId : null, player != null ? player.Position : Vector3.zero);
            page.metPlaceJa = place.ja;
            page.metPlaceEn = place.en;
            AddObservation(page, "met", $"はじめて会った（{place.ja}）", $"First met ({place.en})", null);
            UIManager.Toast(Loc.Format("住民辞典に新しいページができました（No.{0}）", "New page in the resident book (No.{0})", page.number.ToString("000")));
        }

        public void RecordResidentObservation(string residentId, string factJa, string factEn, string linkedRecordId = null)
        {
            if (string.IsNullOrEmpty(residentId) || (string.IsNullOrEmpty(factJa) && string.IsNullOrEmpty(factEn))) return;
            var page = GetOrCreatePage(residentId);
            AddObservation(page, "observed", factJa, factEn, linkedRecordId);
        }

        internal void AddObservation(ResidentPage page, string kind, string factJa, string factEn, string linkedRecordId, int day = -1, int minute = -1)
        {
            if (page == null) return;
            int d = day > 0 ? day : Today;
            int m = minute >= 0 ? minute : NowMinute;
            foreach (var o in page.observations)
            {
                // 同じ日に同じ事実が二重に入らないようにする（複数モジュールから同じ出来事が届いても1件）
                if (o.day == d && o.factJa == factJa && o.linkedRecordId == linkedRecordId) return;
            }
            page.observations.Add(new ResidentObservation { day = d, minute = m, factJa = factJa, factEn = factEn, linkedRecordId = linkedRecordId, kind = kind });
        }

        /// <summary>住民の表示名：プレイヤーが付けたあだ名 → 聞き取った名前メモ → 「No.xxx」。本名（内部名）は出さない。</summary>
        internal string ResidentLabel(string residentId)
        {
            var page = FindResidentPage(residentId);
            if (page == null) return Loc.T("（知らない顔）", "(unknown face)");
            return ResidentLabel(page);
        }

        internal static string ResidentLabel(ResidentPage page)
        {
            if (!string.IsNullOrEmpty(page.nickname)) return page.nickname;
            if (!string.IsNullOrEmpty(page.heardName)) return page.heardName;
            return Loc.Format("住民 No.{0}", "Resident No.{0}", page.number.ToString("000"));
        }

        internal static Color PageColor(ResidentPage page) => new Color(page.colorR, page.colorG, page.colorB, 1f);

        /// <summary>発話者の顔の色（住民辞典 → 住民ディレクトリ → 灰色）。</summary>
        internal Color SpeakerColor(string speakerId)
        {
            var page = FindResidentPage(speakerId);
            if (page != null) return PageColor(page);
            var info = Uchujin.Core.Game.Instance?.Service<IResidentDirectory>()?.Get(speakerId);
            if (info != null) return info.MainColor;
            return new Color(0.6f, 0.6f, 0.65f);
        }

        internal int SpeakerLineage(string speakerId)
        {
            var page = FindResidentPage(speakerId);
            if (page != null) return page.lineage;
            var info = Uchujin.Core.Game.Instance?.Service<IResidentDirectory>()?.Get(speakerId);
            return info != null ? (int)info.Lineage : 0;
        }

        /// <summary>発話の話し手の表示（住民ならあだ名等、看板なら「看板」など。内部名は出さない）。</summary>
        internal string SpeakerLabel(UtteranceRecord r, CardNote note = null)
        {
            if (r == null) return "";
            if (note != null && !string.IsNullOrEmpty(note.playerSpeakerId))
                return ResidentLabel(note.playerSpeakerId) + Loc.T("（と思う）", " (I think)");
            switch (r.speakerKind)
            {
                case "sign": return Loc.T("看板・掲示", "Sign");
                case "creature": return Loc.T("生き物", "Creature");
                case "player": return Loc.T("自分", "Me");
            }
            if (string.IsNullOrEmpty(r.speakerId)) return Loc.T("（だれか）", "(someone)");
            return ResidentLabel(r.speakerId);
        }

        // ---- 関係図 ----

        internal DiagramNode GetNode(string residentId, int indexForDefault, int total)
        {
            foreach (var n in Know.diagramNodes) if (n.residentId == residentId) return n;
            float ang = (total <= 0 ? 0 : indexForDefault / (float)total) * Mathf.PI * 2f;
            var node = new DiagramNode { residentId = residentId, x = 0.5f + Mathf.Cos(ang) * 0.36f, y = 0.5f + Mathf.Sin(ang) * 0.36f };
            Know.diagramNodes.Add(node);
            return node;
        }

        internal PhotoInfo FindPhoto(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var p in Know.photos) if (p.id == id) return p;
            return null;
        }
    }
}
