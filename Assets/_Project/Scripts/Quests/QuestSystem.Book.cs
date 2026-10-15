using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Quests
{
    /// <summary>依頼帳に残す「場面」の区分。主人公の行動・状況だけを表し、宇宙語の意味は表さない（§11、言語 §8.4）。</summary>
    public static class BookScene
    {
        public const string Talk = "talk";
        public const string AfterNod = "nod";
        public const string AfterShake = "shake";
        public const string AfterThink = "think";
        public const string AfterGive = "give";
        public const string AfterGesture = "gesture";
        public const string AfterHelp = "help";
        public const string Board = "board";
        public const string Overheard = "overheard";
        public const string Event = "event";

        public static string Label(string scene)
        {
            switch (scene)
            {
                case Talk: return Loc.T("話しかけた時", "When I spoke to them");
                case AfterNod: return Loc.T("うなずいた後", "After I nodded");
                case AfterShake: return Loc.T("首を振った後", "After I shook my head");
                case AfterThink: return Loc.T("首をかしげた後", "After I tilted my head");
                case AfterGive: return Loc.T("物を渡した後", "After I handed something over");
                case AfterGesture: return Loc.T("身振りをした後", "After my gesture");
                case AfterHelp: return Loc.T("手伝った時", "While helping");
                case Board: return Loc.T("掲示板の紙", "Notice on the board");
                case Overheard: return Loc.T("近くで聞こえた", "Overheard nearby");
                case Event: return Loc.T("集まりで", "At the gathering");
                default: return "";
            }
        }
    }

    // 依頼帳（§11）：宇宙語のまま記録し、自動翻訳しない。手掛かりは住民が実際に出したものだけ（§4.2）。
    public sealed partial class QuestSystem
    {
        internal RequestBookEntry BookGet(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in K.entries) if (e.id == id) return e;
            return null;
        }

        internal static string StoryBookId(string storyId) => "story:" + storyId;

        internal string BookIdForQuest(QuestInstance q)
        {
            if (q == null) return null;
            if (!string.IsNullOrEmpty(q.storyId)) return StoryBookId(q.storyId);
            return q.id;
        }

        private string PendingBookEntryId(PendingTalk p)
        {
            if (p.kind == "self_solved" || p.kind.StartsWith("giftfor"))
            {
                QuestInstance best = null;
                foreach (var q in S.quests)
                    if (q.playerId == p.playerId && q.residentId == p.residentId && !q.IsOpen && (best == null || q.finishedDay >= best.finishedDay)) best = q;
                return BookIdForQuest(best);
            }
            if (p.kind.StartsWith("story:")) return StoryBookId(p.kind.Substring(6));
            if (p.kind == "gift") return "event:farewell:" + p.residentId;
            return null;
        }

        internal RequestBookEntry BookNew(string id, QuestKind kind, string residentId, string questId)
        {
            var existing = BookGet(id);
            if (existing != null) return existing;
            var r = R(residentId);
            var p = LocalPlayer;
            Vector3 pos = p != null ? p.Position : (r != null ? r.Position : Vector3.zero);
            var e = new RequestBookEntry
            {
                id = id,
                questId = questId,
                kind = kind,
                residentId = residentId,
                faceColor = r != null ? r.MainColor : Color.gray,
                lineage = r != null ? r.Lineage : AlienLineage.Round,
                day = Today,
                minute = Minute,
            };
            PlaceLabel(pos, out e.placeJa, out e.placeEn);
            K.entries.Add(e);
            TrimBook();
            return e;
        }

        private void TrimBook()
        {
            while (K.entries.Count > QuestTuning.BookMaxEntries)
            {
                int idx = K.entries.FindIndex(x => !x.starred);
                if (idx < 0) idx = 0;
                K.entries.RemoveAt(idx);
            }
        }

        internal RequestBookEntry BookForQuest(QuestInstance q, bool create)
        {
            if (q == null || q.playerId != LocalId) return null;
            string id = BookIdForQuest(q);
            var e = BookGet(id);
            if (e != null || !create) return e;
            string resident = q.residentId;
            if (!string.IsNullOrEmpty(q.storyId))
            {
                var st = FindStory(q.storyId, q.playerId);
                if (st != null) resident = st.residentId;
            }
            return BookNew(id, q.kind, resident, string.IsNullOrEmpty(q.storyId) ? q.id : null);
        }

        internal RequestBookEntry BookEnsureHelp(string id, string residentId)
        {
            return BookNew(id, QuestKind.Spontaneous, residentId, null);
        }

        internal RequestBookEntry BookEnsureEvent(VillageEvent ev)
        {
            if (ev == null) return null;
            string host = ev.hostResidentId;
            if (string.IsNullOrEmpty(host) && ev.gatherers.Count > 0) host = ev.gatherers[0];
            return BookNew("event:" + ev.id, QuestKind.VillageEvent, host, null);
        }

        /// <summary>実際に聞こえた発話を依頼帳へ写す（原文・表情・身振り。意味は写さない）。</summary>
        internal void BookAttach(RequestBookEntry entry, UtteranceRecord rec, string scene)
        {
            if (entry == null || rec == null) return;
            foreach (var x in entry.records) if (!string.IsNullOrEmpty(rec.recordId) && x.recordId == rec.recordId) return;
            var bu = new BookUtterance
            {
                recordId = rec.recordId,
                text = rec.text,
                expression = rec.expression,
                channel = rec.channel,
                speakerId = rec.speakerId,
                day = rec.day > 0 ? rec.day : Today,
                minute = rec.minuteOfDay,
                scene = scene,
                record = CopyRecord(rec)
            };
            if (rec.tokens != null) foreach (var t in rec.tokens) bu.surfaces.Add(t.surface);
            if (rec.gestures != null)
            {
                foreach (var g in rec.gestures)
                {
                    bu.gestures.Add(new GestureCue(g.gesture, g.target, g.number));
                    switch (g.gesture)
                    {
                        case Gesture.OfferItem:
                            if (!string.IsNullOrEmpty(g.target) && ItemRegistry.Exists(g.target) && !entry.shownItemIds.Contains(g.target)) entry.shownItemIds.Add(g.target);
                            break;
                        case Gesture.ShowNumber:
                            if (g.number > 0 && !entry.shownNumbers.Contains(g.number)) entry.shownNumbers.Add(g.number);
                            break;
                        case Gesture.PointAt:
                            if (!string.IsNullOrEmpty(g.target) && !entry.pointedTargets.Contains(g.target)) entry.pointedTargets.Add(g.target);
                            break;
                    }
                }
            }
            entry.records.Add(bu);
        }

        private static UtteranceRecord CopyRecord(UtteranceRecord r)
        {
            var c = new UtteranceRecord
            {
                recordId = r.recordId,
                speakerId = r.speakerId,
                speakerKind = r.speakerKind,
                text = r.text,
                expression = r.expression,
                channel = r.channel,
                contextTag = r.contextTag,
                day = r.day,
                minuteOfDay = r.minuteOfDay,
                areaId = r.areaId,
                posX = r.posX,
                posY = r.posY,
                posZ = r.posZ,
                weatherSummary = r.weatherSummary,
                meaning = null // 内部の意味は依頼帳に持ち込まない
            };
            if (r.tokens != null) foreach (var t in r.tokens) c.tokens.Add(new SurfaceToken { surface = t.surface, internalId = t.internalId, grammatical = t.grammatical, attachedToPrevious = t.attachedToPrevious });
            if (r.gestures != null) foreach (var g in r.gestures) c.gestures.Add(new GestureCue(g.gesture, g.target, g.number));
            if (r.relatedIds != null) c.relatedIds.AddRange(r.relatedIds);
            return c;
        }

        /// <summary>住民モジュールが GetRequestTalk の発話を話した時に、その記録を依頼帳へ結び付ける。</summary>
        internal void Await(string residentId, string contextTag, string entryId, string scene, string createResident = null, QuestKind createKind = QuestKind.DailyFavor)
        {
            if (string.IsNullOrEmpty(entryId)) return;
            if (BookGet(entryId) == null)
            {
                if (createResident == null) return;
                BookNew(entryId, createKind, createResident, null);
            }
            awaiting.Add(new AwaitRecord { residentId = residentId, contextTag = contextTag, entryId = entryId, scene = scene, time = UnityEngine.Time.realtimeSinceStartup });
        }

        private void OnUtteranceSpoken(UtteranceSpokenEvent e)
        {
            var rec = e.Record;
            if (rec == null || awaiting.Count == 0) return;
            for (int i = 0; i < awaiting.Count; i++)
            {
                var a = awaiting[i];
                if (a.residentId != rec.speakerId) continue;
                if (!string.IsNullOrEmpty(a.contextTag) && a.contextTag != rec.contextTag) continue;
                awaiting.RemoveAt(i);
                BookAttach(BookGet(a.entryId), rec, a.scene);
                return;
            }
        }

        private void CleanupAwaiting()
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            awaiting.RemoveAll(a => now - a.time > 15f);
        }

        // ------------------------------------------------------------
        // 表示用の補助（UI言語：場所・指さした先は主人公の呼び名）
        // ------------------------------------------------------------

        internal void PlaceLabel(Vector3 pos, out string ja, out string en)
        {
            var area = Layout?.GetAreaAt(pos);
            if (area != null && !string.IsNullOrEmpty(area.nameJa))
            {
                ja = area.nameJa;
                en = string.IsNullOrEmpty(area.nameEn) ? area.nameJa : area.nameEn;
                return;
            }
            ja = "村";
            en = "Village";
        }

        internal string PointedLabel(string target)
        {
            if (string.IsNullOrEmpty(target)) return "";
            if (target == "rocket") return Loc.T("宇宙船", "the spaceship");
            if (target == "hill") return Loc.T("高台のほう", "toward the hill");
            if (target == "cart") return Loc.T("村の外れのほう", "toward the edge of the village");
            if (R(target) != null) return Loc.T("ほかの住民", "another resident");
            var f = Layout?.GetFacilityById(target);
            if (f != null)
            {
                switch (f.type)
                {
                    case FacilityType.ResidentHouse: return Loc.T("住民の家", "a resident's house");
                    case FacilityType.PlayerHouse: return Loc.T("自分の家", "my house");
                    case FacilityType.Plaza: return Loc.T("広場", "the plaza");
                    case FacilityType.PlayerShop: return Loc.T("自分の店", "my shop");
                    default: return Loc.T("建物", "a building");
                }
            }
            return Loc.T("どこか", "somewhere");
        }

        /// <summary>タブレットの資料ボックスへ「依頼」として追加（観察できた事実だけ：タブレット仕様 §9.1）。</summary>
        internal void AddQuestMaterial(QuestInstance q, RequestBookEntry entry)
        {
            var tablet = Tablet;
            if (tablet == null || q == null || entry == null) return;
            var ja = new StringBuilder();
            var en = new StringBuilder();
            ja.AppendFormat("{0}日目 {1}で。", entry.day, entry.placeJa);
            en.AppendFormat("Day {0}, {1}. ", entry.day, entry.placeEn);
            if (entry.shownItemIds.Count > 0)
            {
                ja.Append("見せられた物：");
                en.Append("Shown: ");
                for (int i = 0; i < entry.shownItemIds.Count; i++)
                {
                    var d = ItemRegistry.GetOrPlaceholder(entry.shownItemIds[i]);
                    if (i > 0) { ja.Append("、"); en.Append(", "); }
                    ja.Append(d.NameJa);
                    en.Append(d.NameEn);
                }
                ja.Append("。");
                en.Append(". ");
            }
            if (entry.drawnItemIds.Count > 0) { ja.Append("絵が添えてあった。"); en.Append("A drawing was attached. "); }
            if (entry.shownNumbers.Count > 0)
            {
                ja.Append("指で示された数：" + string.Join("、", entry.shownNumbers) + "。");
                en.Append("Number shown with fingers: " + string.Join(", ", entry.shownNumbers) + ". ");
            }
            if (entry.pointedTargets.Count > 0)
            {
                var cur = Loc.Current;
                Loc.Current = UILanguage.Japanese;
                var pj = new List<string>();
                foreach (var t in entry.pointedTargets) pj.Add(PointedLabel(t));
                Loc.Current = UILanguage.English;
                var pe = new List<string>();
                foreach (var t in entry.pointedTargets) pe.Add(PointedLabel(t));
                Loc.Current = cur;
                ja.Append("指さした先：" + string.Join("、", pj) + "。");
                en.Append("Pointed at: " + string.Join(", ", pe) + ". ");
            }
            var mat = new TabletMaterial
            {
                id = "quest:" + q.id,
                kind = TabletMaterialKind.QuestRequest,
                titleJa = string.Format("依頼（{0}日目）", entry.day),
                titleEn = string.Format("Request (day {0})", entry.day),
                factJa = ja.ToString(),
                factEn = en.ToString(),
                day = entry.day,
                minuteOfDay = entry.minute,
                linkedRecordId = entry.records.Count > 0 ? entry.records[entry.records.Count - 1].recordId : null,
                linkedResidentId = entry.residentId
            };
            mat.relatedIds.AddRange(entry.shownItemIds);
            mat.tags.Add("quest");
            mat.tags.Add(q.kind.ToString());
            tablet.AddMaterial(mat);
        }

        /// <summary>依頼帳に表示する進行状態（進行の情報であって、翻訳の正しさではない：§11.1）。</summary>
        internal QuestStatus BookStatus(RequestBookEntry e, out int outcome)
        {
            outcome = 0;
            if (e == null) return QuestStatus.Hinted;
            if (e.id.StartsWith("story:"))
            {
                var st = FindStory(e.id.Substring(6), LocalId);
                if (st != null)
                {
                    outcome = st.outcome;
                    e.lastStatus = st.finished ? QuestStatus.Completed : QuestStatus.Active;
                }
                return e.lastStatus;
            }
            if (e.id.StartsWith("help:"))
            {
                var h = FindHelp(e.id.Substring(5));
                if (h == null || h.done) e.lastStatus = QuestStatus.Completed;
                else e.lastStatus = QuestStatus.Active;
                return e.lastStatus;
            }
            if (e.id.StartsWith("event:"))
            {
                var ev = FindEvent(e.id.Substring(6));
                if (ev != null) e.lastStatus = ev.finished || ev.cancelled ? QuestStatus.Completed : QuestStatus.Active;
                else e.lastStatus = QuestStatus.Completed;
                return e.lastStatus;
            }
            var q = FindQuest(e.questId ?? e.id);
            if (q != null)
            {
                e.lastStatus = q.status;
                outcome = q.outcome;
                if (q.status == QuestStatus.Hinted && !e.heardOnly) e.heardOnly = true;
            }
            return e.lastStatus;
        }
    }
}
