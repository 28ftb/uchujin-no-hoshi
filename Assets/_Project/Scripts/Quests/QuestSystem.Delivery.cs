using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Quests
{
    // 住民との受け答え（§5）：話しかける → お願い（意味データ＋身振り）→ 身振りで返事 → 物を渡す → 反応。
    public sealed partial class QuestSystem
    {
        private enum GiveKind
        {
            None,
            LostReturned,
            LostWrongOwner,
            ParcelDelivered,
            ParcelWrong,
            QuestProgress,
            QuestComplete,
            QuestNearMiss,
            QuestDifferent,
            GiftForChosen,
            BirthdayGift
        }

        private class GiveEval
        {
            public GiveKind kind;
            public QuestInstance quest;
            public QuestGoal goal;
            public int take;
            public string helpId;
            public string ownerId;
            public VillageEvent birthday;
            public float like;
        }

        private readonly HashSet<string> talkedKeys = new HashSet<string>();

        // ------------------------------------------------------------
        // IQuestService
        // ------------------------------------------------------------

        public bool HasActiveRequest(string residentId, string playerId)
        {
            foreach (var q in S.quests)
                if (q.IsOpen && q.residentId == residentId && q.playerId == playerId) return true;
            return false;
        }

        public Utterance GetRequestTalk(string residentId, string playerId)
        {
            if (string.IsNullOrEmpty(residentId) || string.IsNullOrEmpty(playerId)) return null;
            try { return RequestTalkInner(residentId, playerId); }
            catch (Exception e) { Debug.LogException(e); return null; }
        }

        private Utterance RequestTalkInner(string residentId, string playerId)
        {
            bool local = playerId == LocalId;
            var r = R(residentId);
            if (r == null || !r.IsActive) return null;

            // 1. 伝えたいこと（お礼・自分で解決した報告など）
            PendingTalk pending = null;
            foreach (var p in S.pendings) if (p.playerId == playerId && p.residentId == residentId) { pending = p; break; }
            if (pending != null)
            {
                var u = PendingUtterance(pending);
                WorldAuthority.Submit(new QuestAckCommand { pendingId = pending.id, issuerId = playerId });
                if (u != null)
                {
                    if (local) Await(residentId, u.contextTag, PendingBookEntryId(pending), BookScene.Talk);
                    return u;
                }
            }

            // 2. 個別ストーリーの大事な場面
            foreach (var st in S.stories.ToArray())
            {
                if (st.finished || st.playerId != playerId) continue;
                if (st.residentId != residentId && st.partnerResidentId != residentId) continue;
                var def = StoryDef(st.storyId);
                var u = def?.GetTalk(this, st, residentId);
                if (u == null) continue;
                if (local) Await(residentId, u.contextTag, StoryBookId(st.storyId), BookScene.Talk, st.residentId);
                return u;
            }

            // 3. 引き受けたお願い（もう一度の説明・報告）
            QuestInstance active = null, hinted = null;
            foreach (var q in S.quests)
            {
                if (q.playerId != playerId || q.residentId != residentId) continue;
                if (q.status == QuestStatus.Active && active == null) active = q;
                else if (q.status == QuestStatus.Hinted && hinted == null) hinted = q;
            }
            if (active != null)
            {
                var g = active.MainGoal;
                if (g != null && g.type == GoalType.CatchCreature && g.IsDone)
                {
                    // 捕まえた生き物を見せに来た → 達成
                    WorldAuthority.Submit(new QuestReportCommand { questId = active.id, issuerId = playerId });
                    var thanks = QuestSpeech.Thanks(residentId, null, true, true).With(Gesture.Clap);
                    if (local) Await(residentId, thanks.contextTag, BookForQuest(active, true)?.id, BookScene.Talk);
                    return thanks;
                }
                var rem = QuestSpeech.Reminder(active, Today);
                if (local)
                {
                    pendingOffers[residentId] = new PendingOffer { questId = active.id, time = UnityEngine.Time.realtimeSinceStartup, clue = 0 };
                    Await(residentId, rem.contextTag, BookForQuest(active, true)?.id, BookScene.Talk);
                }
                return rem;
            }

            // 4. 困っている様子（気配）→ お願いを伝える
            if (hinted != null)
            {
                var u = QuestSpeech.Request(hinted, Today, 0);
                if (local)
                {
                    pendingOffers[residentId] = new PendingOffer { questId = hinted.id, time = UnityEngine.Time.realtimeSinceStartup, clue = 0 };
                    var entry = BookForQuest(hinted, true);
                    if (entry != null) entry.heardOnly = true;
                    Await(residentId, u.contextTag, entry?.id, BookScene.Talk);
                }
                return u;
            }

            // 5. 今日の行事
            foreach (var ev in S.events)
            {
                if (ev.day != Today || ev.finished || ev.cancelled || ev.hostResidentId != residentId) continue;
                string key = "ev:" + ev.id + ":" + residentId + ":" + Today;
                if (talkedKeys.Contains(key)) continue;
                talkedKeys.Add(key);
                var u = ev.started ? QuestSpeech.EventWelcome(ev, residentId) : QuestSpeech.EventAnnounce(ev, residentId, Today);
                if (local) Await(residentId, u.contextTag, "event:" + ev.id, BookScene.Talk, residentId, QuestKind.VillageEvent);
                return u;
            }

            // 6. 自発的なお手伝いの状況（落とし物を探している・迷子・重い荷物）
            foreach (var h in S.helps)
            {
                if (h.done || h.residentId != residentId) continue;
                string key = "help:" + h.id + ":" + Today;
                if (talkedKeys.Contains(key)) continue;
                Utterance u = null;
                switch (h.kind)
                {
                    case HelpKind.LostItem: u = QuestSpeech.LostSearching(residentId, h.itemId); break;
                    case HelpKind.LostCreature: u = QuestSpeech.LostCreatureSearching(residentId, h.speciesId); break;
                    case HelpKind.CarryLoad:
                        if (!string.IsNullOrEmpty(h.claimedBy)) continue;
                        u = QuestSpeech.CarryAsk(residentId, r.HomeFacilityId);
                        break;
                }
                if (u == null) continue;
                talkedKeys.Add(key);
                if (local) Await(residentId, u.contextTag, "help:" + h.id, BookScene.Talk, residentId, QuestKind.Spontaneous);
                return u;
            }

            // 7. ほかの住民の困りごとを耳にする（§3 入口：ほかの住民から）
            string gossipKey = "gossip:" + residentId + ":" + Today;
            if (!talkedKeys.Contains(gossipKey))
            {
                talkedKeys.Add(gossipKey);
                var rng = Rng("quests.gossip." + Today + "." + residentId + "." + playerId);
                if (rng.Chance(0.35f))
                {
                    foreach (var q in S.quests)
                    {
                        if (q.playerId != playerId || q.status != QuestStatus.Hinted || q.residentId == residentId) continue;
                        var g = q.MainGoal;
                        if (g == null || g.type != GoalType.DeliverItem) continue;
                        var u = new Utterance(residentId, QuestSpeech.TagRequest)
                            .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.NEED).Agent(Concepts.Name(q.residentId)).Patient(QuestSpeech.GoalArg(g, false)))
                            .Face(Expression.Worried).With(Gesture.PointAt, q.residentId).Related(g.itemId);
                        if (local) Await(residentId, u.contextTag, BookForQuest(q, true)?.id, BookScene.Overheard);
                        return u;
                    }
                }
            }

            // 8. 修理屋の手掛かり（§15）
            var hint = TryRepairHint(r, playerId);
            if (hint != null)
            {
                if (local) Await(residentId, hint.contextTag, "repair:" + residentId, BookScene.Talk, residentId, QuestKind.LifeBorn);
                return hint;
            }
            return null;
        }

        private Utterance PendingUtterance(PendingTalk p)
        {
            switch (p.kind)
            {
                case "self_solved": return QuestSpeech.SelfSolved(p.residentId, p.itemId, false);
                case "laundry_thanks": return QuestSpeech.LaundryThanks(p.residentId);
                case "help_thanks": return QuestSpeech.CarryThanks(p.residentId);
                case "giftfor_liked":
                case "giftfor_other":
                {
                    bool liked = p.kind == "giftfor_liked";
                    var u = new Utterance(p.residentId, QuestSpeech.TagThanks);
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.I).Patient(QuestSpeech.ItemArg(p.itemId)).Recipient(Concepts.Name(p.otherResidentId)).Past());
                    if (liked)
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.Name(p.otherResidentId)).Adverb(Concepts.VERY).Past());
                    else
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SURPRISED).Agent(Concepts.Name(p.otherResidentId)).Past());
                    u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                    return u.Face(liked ? Expression.VeryHappy : Expression.Happy).With(Gesture.PointAt, p.otherResidentId).Related(p.itemId);
                }
                case "gift":
                    return QuestSpeech.GiftGiven(p.residentId, p.itemId);
                default:
                    var st = p.kind.StartsWith("story:") ? FindStory(p.kind.Substring(6), p.playerId) : null;
                    if (st != null) return StoryDef(st.storyId)?.PendingTalk(this, st, p);
                    return null;
            }
        }

        private CommandResult HandleAck(QuestAckCommand c, CommandContext ctx)
        {
            int removed = S.pendings.RemoveAll(p => p.id == c.pendingId && p.playerId == ctx.IssuerId);
            if (removed > 0) MarkDirty();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 身振りで返事（§5.1）
        // ------------------------------------------------------------

        public bool TryHandleGesture(string residentId, string playerId, PlayerGesture gesture)
        {
            if (string.IsNullOrEmpty(residentId) || playerId != LocalId) return false;
            try { return GestureInner(residentId, playerId, gesture); }
            catch (Exception e) { Debug.LogException(e); return false; }
        }

        private bool GestureInner(string residentId, string playerId, PlayerGesture gesture)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;

            // 個別ストーリーの場面（釣り勝負の誘い・味見の感想など）
            foreach (var st in S.stories.ToArray())
            {
                if (st.finished || st.playerId != playerId) continue;
                if (st.residentId != residentId && st.partnerResidentId != residentId) continue;
                if (StoryDef(st.storyId)?.OnGesture(this, st, residentId, gesture) == true) return true;
            }

            QuestInstance q = null;
            PendingOffer po = null;
            if (pendingOffers.TryGetValue(residentId, out po) && now - po.time < QuestTuning.OfferResponseSeconds)
            {
                q = FindQuest(po.questId);
                if (q != null && (q.playerId != playerId || !q.IsOpen)) q = null;
            }
            if (q == null)
            {
                foreach (var x in S.quests)
                {
                    if (x.playerId != playerId || x.residentId != residentId || !x.IsOpen) continue;
                    if (q == null || x.status == QuestStatus.Active) q = x;
                }
                po = null;
            }

            if (q != null)
            {
                if (q.status == QuestStatus.Hinted)
                {
                    if (po == null)
                    {
                        // まだお願いを聞いていない：首をかしげると、困っている事を話してくれる
                        if (gesture == PlayerGesture.Think || gesture == PlayerGesture.Surprised)
                        {
                            var u = QuestSpeech.Request(q, Today, 0);
                            pendingOffers[residentId] = new PendingOffer { questId = q.id, time = now };
                            var entry = BookForQuest(q, true);
                            if (entry != null) entry.heardOnly = true;
                            BookAttach(entry, Say(residentId, u), BookScene.AfterThink);
                            return true;
                        }
                        return false;
                    }
                    switch (gesture)
                    {
                        case PlayerGesture.Nod:
                        case PlayerGesture.Happy:
                        case PlayerGesture.Clap:
                        case PlayerGesture.Bow:
                            AcceptLocal(q);
                            return true;
                        case PlayerGesture.ShakeHead:
                            pendingOffers.Remove(residentId);
                            BookAttach(BookForQuest(q, true), Say(residentId, QuestSpeech.Declined(residentId)), BookScene.AfterShake);
                            WorldAuthority.Submit(new QuestRespondCommand { questId = q.id, response = (int)QuestResponse.Decline });
                            return true;
                        case PlayerGesture.Think:
                        case PlayerGesture.Surprised:
                            ReExplain(q, po, BookScene.AfterThink);
                            return true;
                    }
                    return false;
                }

                // 引き受けたお願い
                switch (gesture)
                {
                    case PlayerGesture.Think:
                    case PlayerGesture.Surprised:
                        if (po == null) { po = new PendingOffer { questId = q.id, time = now }; pendingOffers[residentId] = po; }
                        ReExplain(q, po, BookScene.AfterThink);
                        return true;
                    case PlayerGesture.ShakeHead:
                        if (abandonAsks.TryGetValue(residentId, out var t0) && now - t0 < QuestTuning.AbandonConfirmSeconds)
                        {
                            abandonAsks.Remove(residentId);
                            BookAttach(BookForQuest(q, true), Say(residentId, QuestSpeech.Declined(residentId)), BookScene.AfterShake);
                            WorldAuthority.Submit(new QuestRespondCommand { questId = q.id, response = (int)QuestResponse.Abandon });
                        }
                        else
                        {
                            abandonAsks[residentId] = now;
                            BookAttach(BookForQuest(q, true), Say(residentId, QuestSpeech.AbandonAsk(residentId)), BookScene.AfterShake);
                        }
                        return true;
                    case PlayerGesture.Nod:
                        if (abandonAsks.Remove(residentId))
                        {
                            Say(residentId, QuestSpeech.Accepted(residentId));
                            return true;
                        }
                        return false;
                    case PlayerGesture.ShowItem:
                        OpenGivePicker(residentId);
                        return true;
                }
            }

            // 行事の場で喜ぶ・拍手する
            if (gesture == PlayerGesture.Clap || gesture == PlayerGesture.Happy || gesture == PlayerGesture.Wave)
            {
                foreach (var ev in S.events)
                {
                    if (ev.day != Today || !ev.started || ev.finished || ev.hostResidentId != residentId) continue;
                    string key = "evg:" + ev.id + ":" + gesture;
                    if (talkedKeys.Contains(key)) return false;
                    talkedKeys.Add(key);
                    var u = new Utterance(residentId, QuestSpeech.TagEvent)
                        .Say(MeaningFrame.Make(SpeechAct.Exclamation, ev.kind == VillageEventKind.FarewellParty ? Concepts.GOODBYE : Concepts.WOW))
                        .Face(ev.kind == VillageEventKind.FarewellParty ? Expression.Crying : Expression.VeryHappy)
                        .With(ev.kind == VillageEventKind.FarewellParty ? Gesture.Wave : Gesture.Dance);
                    Await(residentId, u.contextTag, "event:" + ev.id, BookScene.AfterGesture, residentId, QuestKind.VillageEvent);
                    Say(residentId, u);
                    return true;
                }
            }
            if (gesture == PlayerGesture.ShowItem && HasDeliverableFor(residentId, playerId))
            {
                OpenGivePicker(residentId);
                return true;
            }
            return false;
        }

        private void AcceptLocal(QuestInstance q)
        {
            pendingOffers.Remove(q.residentId);
            var entry = BookForQuest(q, true);
            if (entry != null) entry.heardOnly = false;
            BookAttach(entry, Say(q.residentId, QuestSpeech.Accepted(q.residentId)), BookScene.AfterNod);
            AddQuestMaterial(q, entry);
            WorldAuthority.Submit(new QuestRespondCommand { questId = q.id, response = (int)QuestResponse.Accept });
        }

        private void ReExplain(QuestInstance q, PendingOffer po, string scene)
        {
            po.clue = Mathf.Min(QuestTuning.MaxReExplain, po.clue + 1);
            po.time = UnityEngine.Time.realtimeSinceStartup;
            var u = QuestSpeech.Request(q, Today, po.clue);
            u.contextTag = QuestSpeech.TagReminder;
            BookAttach(BookForQuest(q, true), Say(q.residentId, u), scene);
        }

        private CommandResult HandleRespond(QuestRespondCommand c, CommandContext ctx)
        {
            var q = FindQuest(c.questId);
            if (q == null || q.playerId != ctx.IssuerId) return CommandResult.Fail(null, null);
            var resp = (QuestResponse)c.response;
            var st = string.IsNullOrEmpty(q.storyId) ? null : FindStory(q.storyId, q.playerId);
            var def = st != null ? StoryDef(st.storyId) : null;
            switch (resp)
            {
                case QuestResponse.Accept:
                    if (q.status != QuestStatus.Hinted) return CommandResult.Ok();
                    q.acceptedDay = Today;
                    SetStatus(q, QuestStatus.Active);
                    var g = q.MainGoal;
                    if (g != null && g.type == GoalType.DeliverToResident)
                        ctx.GiveItem(new ItemStack(g.itemId, 1) { data = QuestItemIds.DataParcelPrefix + q.id }, "quest_parcel");
                    Memory(q.residentId, "quest_accepted", q.playerId, g?.itemId, null, 0.3f);
                    def?.OnQuestAccepted(this, st, q, ctx);
                    break;
                case QuestResponse.Decline:
                    if (q.status != QuestStatus.Hinted) return CommandResult.Ok();
                    SetStatus(q, QuestStatus.Declined);
                    def?.OnQuestDeclined(this, st, q);
                    break;
                case QuestResponse.Abandon:
                    if (q.status != QuestStatus.Active) return CommandResult.Ok();
                    SetStatus(q, QuestStatus.Declined);
                    if (q.MainGoal != null && q.MainGoal.type == GoalType.DeliverToResident)
                        ctx.TakeItem(q.MainGoal.itemId, 1, "quest_abandon");
                    def?.OnQuestDeclined(this, st, q);
                    break;
            }
            Changed();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 物を渡す（§5.2 目標と方法を分ける、§4.3 誤解から起きるやり取り）
        // ------------------------------------------------------------

        public bool TryHandleItemGiven(string residentId, string playerId, ItemStack stack)
        {
            if (handlingGive || stack == null || stack.IsEmpty || string.IsNullOrEmpty(residentId) || string.IsNullOrEmpty(playerId)) return false;
            GiveEval ev;
            try { ev = EvaluateGive(residentId, playerId, stack); }
            catch (Exception e) { Debug.LogException(e); return false; }
            if (ev.kind == GiveKind.None) return false;
            handlingGive = true;
            try
            {
                SpeakGiveReaction(ev, residentId, playerId, stack);
                WorldAuthority.Submit(new QuestGiveCommand
                {
                    residentId = residentId,
                    itemId = stack.itemId,
                    count = stack.count,
                    quality = stack.quality,
                    size = stack.size,
                    data = stack.data,
                    issuerId = playerId
                });
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                handlingGive = false;
            }
            return true;
        }

        private static bool ParseLostData(string data, out string helpId, out string ownerId)
        {
            helpId = null;
            ownerId = null;
            if (string.IsNullOrEmpty(data) || !data.StartsWith(QuestItemIds.DataLostPrefix)) return false;
            var parts = data.Substring(QuestItemIds.DataLostPrefix.Length).Split('|');
            helpId = parts.Length > 0 ? parts[0] : null;
            ownerId = parts.Length > 1 ? parts[1] : null;
            return true;
        }

        private GiveEval EvaluateGive(string residentId, string playerId, ItemStack stack)
        {
            var ev = new GiveEval { kind = GiveKind.None };
            string data = stack.data ?? "";

            // 落とし物
            if (ParseLostData(data, out var helpId, out var ownerId))
            {
                ev.helpId = helpId;
                ev.ownerId = ownerId;
                ev.kind = ownerId == residentId ? GiveKind.LostReturned : GiveKind.LostWrongOwner;
                return ev;
            }
            // 預かった物（配達・青い花）
            if (data.StartsWith(QuestItemIds.DataParcelPrefix))
            {
                var pq = FindQuest(data.Substring(QuestItemIds.DataParcelPrefix.Length));
                if (pq == null || pq.MainGoal == null) return ev;
                ev.quest = pq;
                ev.goal = pq.MainGoal;
                ev.ownerId = pq.MainGoal.targetResidentId;
                ev.kind = ev.ownerId == residentId ? GiveKind.ParcelDelivered : GiveKind.ParcelWrong;
                return ev;
            }
            var def = stack.Definition;
            if (def.Category == ItemCategory.KeyItem && def.SourceActivity == "quest") return ev;

            // 依頼（引き受けたものを優先。気配だけのものも、ぴったりの物なら受け取って達成）
            var open = new List<QuestInstance>();
            foreach (var q in S.quests)
                if (q.playerId == playerId && q.residentId == residentId && q.IsOpen) open.Add(q);
            open.Sort((a, b) => (a.status == QuestStatus.Active ? 0 : 1).CompareTo(b.status == QuestStatus.Active ? 0 : 1));

            foreach (var q in open)
            {
                foreach (var g in q.goals)
                {
                    if (g.type == GoalType.DeliverItem && !g.IsDone && QuestCatalog.Matches(g, stack))
                    {
                        ev.quest = q;
                        ev.goal = g;
                        ev.take = Mathf.Min(stack.count, g.Remaining);
                        int remainingAfter = 0;
                        foreach (var x in q.goals) remainingAfter += x == g ? x.Remaining - ev.take : x.Remaining;
                        ev.kind = remainingAfter <= 0 ? GiveKind.QuestComplete : GiveKind.QuestProgress;
                        var r = R(residentId);
                        ev.like = r != null ? r.EvaluateItem(def) : 0f;
                        return ev;
                    }
                    if (g.type == GoalType.GiftFor && !g.IsDone)
                    {
                        ev.quest = q;
                        ev.goal = g;
                        ev.take = 1;
                        var target = R(g.targetResidentId);
                        ev.like = target != null ? target.EvaluateItem(def) : 0f;
                        ev.kind = GiveKind.GiftForChosen;
                        return ev;
                    }
                }
            }
            // 誕生日の贈り物（§13）
            foreach (var e in S.events)
            {
                if (e.kind != VillageEventKind.Birthday || e.day != Today || e.cancelled || e.hostResidentId != residentId) continue;
                if (e.attendees.Contains("gift:" + playerId)) continue;
                ev.birthday = e;
                var r = R(residentId);
                ev.like = r != null ? r.EvaluateItem(def) : 0f;
                ev.kind = GiveKind.BirthdayGift;
                return ev;
            }
            foreach (var q in open)
            {
                if (q.status != QuestStatus.Active) continue;
                foreach (var g in q.goals)
                {
                    if (g.type != GoalType.DeliverItem || g.IsDone) continue;
                    ev.quest = q;
                    ev.goal = g;
                    ev.kind = QuestCatalog.NearMiss(g, stack) ? GiveKind.QuestNearMiss : GiveKind.QuestDifferent;
                    return ev;
                }
            }
            return ev;
        }

        private bool KnowsOwner(string residentId, string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId)) return false;
            // 住民同士の関係（未公開の内部情報）の代わりに、決定的な「知り合いかどうか」【仮】
            return StableHash.Int(seed, "knows:" + residentId + ":" + ownerId, 0, 3) != 0;
        }

        /// <summary>渡した場面の反応（その場の画面で話す）。</summary>
        private void SpeakGiveReaction(GiveEval ev, string residentId, string playerId, ItemStack stack)
        {
            bool local = playerId == LocalId;
            Utterance u = null;
            string bookId = null;
            switch (ev.kind)
            {
                case GiveKind.LostReturned:
                    u = QuestSpeech.ReturnedThanks(residentId, stack.itemId);
                    bookId = "help:" + ev.helpId;
                    if (local) BookEnsureHelp(bookId, residentId);
                    break;
                case GiveKind.LostWrongOwner:
                    u = QuestSpeech.WrongRecipient(residentId, stack.itemId, ev.ownerId, KnowsOwner(residentId, ev.ownerId));
                    bookId = "help:" + ev.helpId;
                    if (local) BookEnsureHelp(bookId, ev.ownerId);
                    break;
                case GiveKind.ParcelDelivered:
                {
                    var st = string.IsNullOrEmpty(ev.quest.storyId) ? null : FindStory(ev.quest.storyId, playerId);
                    u = st != null ? StoryDef(st.storyId)?.DeliveredSpeech(this, st, ev.quest, residentId) : null;
                    if (u == null)
                    {
                        u = new Utterance(residentId, QuestSpeech.TagThanks)
                            .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                            .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.Name(ev.quest.residentId)).Patient(QuestSpeech.ItemArg(stack.itemId)).Recipient(Concepts.I).Past())
                            .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU))
                            .Face(Expression.Happy).With(Gesture.PointAt, ev.quest.residentId);
                    }
                    bookId = BookIdForQuest(ev.quest);
                    break;
                }
                case GiveKind.ParcelWrong:
                {
                    if (residentId == ev.quest.residentId)
                    {
                        // 依頼主に返そうとした → 困った顔で届け先を指さす（§4.3 の例そのもの）
                        u = new Utterance(residentId, QuestSpeech.TagWrongRecipient)
                            .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.WANT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THIS)).Not())
                            .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.THIS)).Recipient(Concepts.Name(ev.ownerId)))
                            .Face(Expression.Confused).With(Gesture.ShakeHead).With(Gesture.PointAt, ev.ownerId).With(Gesture.OfferItem, stack.itemId);
                    }
                    else u = QuestSpeech.WrongRecipient(residentId, stack.itemId, ev.ownerId, KnowsOwner(residentId, ev.ownerId));
                    bookId = BookIdForQuest(ev.quest);
                    break;
                }
                case GiveKind.QuestProgress:
                    u = QuestSpeech.Partial(ev.quest, stack.itemId);
                    bookId = BookIdForQuest(ev.quest);
                    break;
                case GiveKind.QuestComplete:
                {
                    var st = string.IsNullOrEmpty(ev.quest.storyId) ? null : FindStory(ev.quest.storyId, playerId);
                    u = st != null ? StoryDef(st.storyId)?.DeliveredSpeech(this, st, ev.quest, residentId) : null;
                    if (u == null) u = QuestSpeech.Thanks(residentId, stack.itemId, ev.like >= 0.35f, ev.like >= 0.35f || ev.quest.kind == QuestKind.Story);
                    bookId = BookIdForQuest(ev.quest);
                    break;
                }
                case GiveKind.QuestNearMiss:
                    u = QuestSpeech.NearMiss(ev.quest, stack);
                    bookId = BookIdForQuest(ev.quest);
                    break;
                case GiveKind.QuestDifferent:
                    u = QuestSpeech.Different(ev.quest, stack.itemId);
                    bookId = BookIdForQuest(ev.quest);
                    break;
                case GiveKind.GiftForChosen:
                {
                    var target = ev.goal.targetResidentId;
                    u = new Utterance(residentId, QuestSpeech.TagThanks);
                    if (ev.like >= 0.35f)
                    {
                        u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW));
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.Name(target)).Patient(QuestSpeech.ItemArg(stack.itemId)).Adverb(Concepts.VERY));
                        u.Face(Expression.VeryHappy).With(Gesture.Jump);
                    }
                    else
                    {
                        u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM));
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.Name(target)).Patient(QuestSpeech.ItemArg(stack.itemId)).Adverb(Concepts.MAYBE));
                        u.Face(Expression.Thinking).With(Gesture.Think);
                    }
                    u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU));
                    u.With(Gesture.PointAt, target).Related(stack.itemId);
                    bookId = BookIdForQuest(ev.quest);
                    break;
                }
                case GiveKind.BirthdayGift:
                    u = QuestSpeech.BirthdayGiftReaction(residentId, stack.itemId, ev.like);
                    bookId = "event:" + ev.birthday.id;
                    if (local) BookEnsureEvent(ev.birthday);
                    break;
            }
            if (u == null) return;
            var rec = Say(residentId, u);
            if (!local) return;
            var entry = bookId != null ? BookGet(bookId) : null;
            if (entry == null && ev.quest != null) entry = BookForQuest(ev.quest, true);
            if (entry != null)
            {
                entry.givenItemIds.Add(stack.itemId);
                BookAttach(entry, rec, BookScene.AfterGive);
            }
            // 住民辞典へ「観察できた事実」だけを記録（内部の好みや正解は書かない）
            if (ev.kind == GiveKind.QuestComplete || ev.kind == GiveKind.BirthdayGift || ev.kind == GiveKind.QuestDifferent)
            {
                var face = u.expression;
                Tablet?.RecordResidentObservation(residentId,
                    Loc.Format("{0}日目：{1}を渡した時、{2}", "Day {0}: when I gave {1}, {2}", Today, stack.Definition.NameJa, FaceFactJa(face)),
                    string.Format("Day {0}: when I gave {1}, {2}", Today, stack.Definition.NameEn, FaceFactEn(face)),
                    rec != null ? rec.recordId : null);
            }
        }

        internal static string FaceFactJa(Expression e)
        {
            switch (e)
            {
                case Expression.VeryHappy: case Expression.Love: return "飛び上がるような顔をした";
                case Expression.Happy: return "にこにこした";
                case Expression.Confused: return "首をかしげた";
                case Expression.Thinking: return "考え込むような顔をした";
                case Expression.Surprised: return "目を丸くした";
                case Expression.Sad: case Expression.Crying: return "しょんぼりした";
                default: return "表情はあまり変わらなかった";
            }
        }

        internal static string FaceFactEn(Expression e)
        {
            switch (e)
            {
                case Expression.VeryHappy: case Expression.Love: return "they looked like they might jump";
                case Expression.Happy: return "they smiled";
                case Expression.Confused: return "they tilted their head";
                case Expression.Thinking: return "they looked thoughtful";
                case Expression.Surprised: return "their eyes went round";
                case Expression.Sad: case Expression.Crying: return "they drooped";
                default: return "their face barely changed";
            }
        }

        private CommandResult HandleGive(QuestGiveCommand c, CommandContext ctx)
        {
            var stack = new ItemStack(c.itemId, Mathf.Max(1, c.count), c.quality) { size = c.size, data = c.data };
            var ev = EvaluateGive(c.residentId, ctx.IssuerId, stack);
            var dir = Dir;
            switch (ev.kind)
            {
                case GiveKind.None:
                    // ホスト側では関係が無かった（状態の行き違い）→ 物は住民への贈り物として届ける
                    dir?.GiveItem(c.residentId, stack, "gift");
                    break;
                case GiveKind.LostReturned:
                {
                    var h = FindHelp(ev.helpId);
                    if (h != null) h.done = true;
                    RewardHelp(c.residentId, ctx, "returned_lost", c.itemId);
                    break;
                }
                case GiveKind.LostWrongOwner:
                case GiveKind.ParcelWrong:
                case GiveKind.QuestNearMiss:
                    // 受け取らずに返してくれる
                    ctx.GiveItem(stack, "returned");
                    break;
                case GiveKind.ParcelDelivered:
                    Memory(c.residentId, "received_delivery", ctx.IssuerId, c.itemId, ev.quest.residentId, 0.5f);
                    Memory(ev.quest.residentId, "delivered_for_me", ctx.IssuerId, c.itemId, c.residentId, 0.5f);
                    Friendship(c.residentId, ctx.IssuerId, 1, "delivery");
                    ev.goal.progress = ev.goal.count;
                    CompleteQuest(ev.quest, ctx, 1, c.itemId);
                    break;
                case GiveKind.QuestProgress:
                case GiveKind.QuestComplete:
                {
                    ev.goal.progress += ev.take;
                    var taken = stack.CloneWithCount(ev.take);
                    if (stack.count > ev.take) ctx.GiveItem(stack.CloneWithCount(stack.count - ev.take), "returned");
                    // 渡した物は住民の暮らしで使われる（食べる・飾る・使う：住民 §4）
                    dir?.GiveItem(c.residentId, taken, "quest");
                    if (ev.kind == GiveKind.QuestComplete || ev.quest.AllGoalsDone)
                        CompleteQuest(ev.quest, ctx, ev.like >= 0.35f ? 2 : 1, c.itemId);
                    else Changed();
                    break;
                }
                case GiveKind.QuestDifferent:
                    // 違う物でも罰しない：困った顔をしつつ贈り物として受け取る（§4.3）
                    dir?.GiveItem(c.residentId, stack, "gift_misunderstanding");
                    Friendship(c.residentId, ctx.IssuerId, QuestTuning.FriendshipOnMisunderstanding, "gift_misunderstanding");
                    Memory(c.residentId, "gift", ctx.IssuerId, c.itemId, null, 0.3f, "misunderstanding");
                    ev.quest.givenItemId = c.itemId;
                    Changed();
                    break;
                case GiveKind.GiftForChosen:
                    ev.goal.progress = 1;
                    ev.quest.givenItemId = c.itemId;
                    CompleteQuest(ev.quest, ctx, ev.like >= 0.35f ? 2 : 4, c.itemId);
                    break;
                case GiveKind.BirthdayGift:
                {
                    ev.birthday.attendees.Add("gift:" + ctx.IssuerId);
                    dir?.GiveItem(c.residentId, stack, "birthday_gift");
                    bool liked = ev.like >= 0.35f;
                    Friendship(c.residentId, ctx.IssuerId, liked ? QuestTuning.FriendshipBirthdayGiftLiked : QuestTuning.FriendshipBirthdayGiftOther, "birthday_gift");
                    Memory(c.residentId, "birthday_gift", ctx.IssuerId, c.itemId, null, liked ? 0.9f : 0.6f);
                    EventBus.Publish(new GiftGivenEvent { PlayerId = ctx.IssuerId, ResidentId = c.residentId, ItemId = c.itemId, Quality = c.quality, Reaction = liked ? 2 : (ev.like <= -0.3f ? -1 : 1) });
                    Changed();
                    break;
                }
            }
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 達成・報酬（§12）
        // ------------------------------------------------------------

        internal void CompleteQuest(QuestInstance q, CommandContext ctx, int outcome, string itemId)
        {
            if (q == null || q.status == QuestStatus.Completed) return;
            q.outcome = outcome;
            if (!string.IsNullOrEmpty(itemId)) q.givenItemId = itemId;
            SetStatus(q, QuestStatus.Completed);
            GiveReward(q, ctx, outcome == 2);
            Memory(q.residentId, "quest_done", q.playerId, itemId, q.MainGoal?.targetResidentId, outcome == 2 ? 0.8f : 0.6f, q.kind.ToString());
            ApplyLifeChange(q);
            if (q.MainGoal != null && q.MainGoal.type == GoalType.GiftFor)
            {
                // 誕生日当日、依頼主が選んだ物を相手へ渡す（§10：結果は相手の好み次第）
                foreach (var e in S.events)
                {
                    if (e.kind == VillageEventKind.Birthday && e.hostResidentId == q.MainGoal.targetResidentId && e.day >= Today)
                        if (!e.gatherers.Contains(q.residentId)) e.gatherers.Add(q.residentId);
                }
            }
            if (!string.IsNullOrEmpty(q.storyId))
            {
                var st = FindStory(q.storyId, q.playerId);
                if (st != null) StoryDef(st.storyId)?.OnQuestCompleted(this, st, q, ctx);
            }
            Changed();
        }

        private void GiveReward(QuestInstance q, CommandContext ctx, bool liked)
        {
            var dir = Dir;
            var r = R(q.residentId);
            int money = q.rewardMoney;
            if (liked) money = Mathf.RoundToInt(money * (1f + QuestTuning.RewardQualityBonus));
            if (money > 0)
            {
                // 住民も所持金から払う（住民 §4 所持金）。足りなければ払える分＋村のお礼【仮】
                if (dir != null && r != null)
                {
                    int canPay = Mathf.Min(money, Mathf.Max(0, r.Money));
                    if (canPay > 0) dir.TrySpend(q.residentId, canPay, "quest_reward");
                    money = Mathf.Max(QuestTuning.RewardMin, canPay > 0 ? money : money / 2);
                }
                ctx.PayMoney(money, "quest:" + q.id);
            }
            if (!string.IsNullOrEmpty(q.rewardItemId) && ItemRegistry.Exists(q.rewardItemId))
                ctx.GiveItem(new ItemStack(q.rewardItemId, Mathf.Max(1, q.rewardItemCount)), "quest_reward");
            if (!string.IsNullOrEmpty(q.rewardSpeciesId))
                Game.Service<ICreatureService>()?.GrantCreature(q.playerId, q.rewardSpeciesId, "quest:" + q.residentId);
            Friendship(q.residentId, q.playerId, liked ? QuestTuning.FriendshipOnCompleteLiked : QuestTuning.FriendshipOnComplete, "quest_done");
            if (money > 0) ctx.Notify(string.Format("お礼を受け取りました（{0}）", Wallet.Format(money)), string.Format("Received thanks ({0})", Wallet.Format(money)));
        }

        internal void RewardHelp(string residentId, CommandContext ctx, string memoryKind, string itemId)
        {
            Friendship(residentId, ctx.IssuerId, QuestTuning.FriendshipOnHelp, memoryKind);
            Memory(residentId, "helped", ctx.IssuerId, itemId, null, 0.6f, memoryKind);
            var rng = Rng("quests.helpreward." + residentId + "." + Today + "." + memoryKind);
            int money = rng.Range(QuestTuning.HelpRewardMoneyMin, QuestTuning.HelpRewardMoneyMax + 1);
            var r = R(residentId);
            if (r != null && r.Money >= money) Dir?.TrySpend(residentId, money, "help_thanks");
            ctx.PayMoney(money, "help:" + memoryKind);
            ctx.Notify(string.Format("お礼をもらった（{0}）", Wallet.Format(money)), string.Format("Got a thank-you ({0})", Wallet.Format(money)));
            // 一緒に過ごした出来事として後から辞典で振り返れる（§3：受注なしでも記録される）
            EventBus.Publish(new QuestStateChangedEvent { QuestId = "help:" + memoryKind, ResidentId = residentId, Status = QuestStatus.Completed, PlayerId = ctx.IssuerId });
        }

        private CommandResult HandleReport(QuestReportCommand c, CommandContext ctx)
        {
            var q = FindQuest(c.questId);
            if (q == null || q.playerId != ctx.IssuerId || q.status != QuestStatus.Active) return CommandResult.Ok();
            if (!q.AllGoalsDone) return CommandResult.Ok();
            CompleteQuest(q, ctx, 2, null);
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 生活の変化（§8.2：工房に作品、家に花、ペット…）
        // ------------------------------------------------------------

        private void ApplyLifeChange(QuestInstance q)
        {
            var r = R(q.residentId);
            if (r == null) return;
            string kind = q.lifeChangeKind;
            string itemId = q.givenItemId ?? q.MainGoal?.itemId ?? q.MainGoal?.sampleItemId;
            var def = ItemRegistry.Get(itemId);
            if (def != null && def.Category == ItemCategory.Furniture)
                Game.Service<IHousingService>()?.AddFurnitureToResidentHome(q.residentId, itemId);
            if (string.IsNullOrEmpty(kind)) return;
            bool atWork = kind == "forge_work" || kind == "gadget" || kind == "fish_trophy";
            Vector3 basePos = atWork ? WorkplaceOf(r) : HomeOf(r);
            Color color = def != null ? def.Color : Color.white;
            AddLifeMark(kind, q.residentId, itemId, basePos, color);
        }

        internal void AddLifeMark(string kind, string residentId, string itemId, Vector3 basePos, Color color)
        {
            int count = 0;
            LifeMark oldest = null;
            foreach (var m in S.marks)
            {
                if (m.residentId != residentId) continue;
                count++;
                if (oldest == null || m.day < oldest.day) oldest = m;
            }
            if (count >= QuestTuning.MaxLifeMarksPerResident && oldest != null) S.marks.Remove(oldest);
            float ang = StableHash.Unit(seed, "mark:" + residentId + ":" + S.marks.Count + ":" + Today) * Mathf.PI * 2f;
            var offset = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 2.2f;
            S.marks.Add(new LifeMark
            {
                id = NewId("lm"),
                kind = kind,
                residentId = residentId,
                itemId = itemId,
                position = Ground(basePos + offset),
                color = color,
                day = Today
            });
        }

        // ------------------------------------------------------------
        // 話しかけメニューへの追加（IResidentInteractionProvider）
        // ------------------------------------------------------------

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            var list = new List<ResidentInteractionOption>();
            if (resident == null || player == null || player.PlayerId != LocalId) return list;
            string rid = resident.Id;
            // 主人公は話さない。UIは「身振り」の操作名だけを出す（意味を先回りして明かさない：§5.1）
            if (pendingOffers.TryGetValue(rid, out var po) && UnityEngine.Time.realtimeSinceStartup - po.time < QuestTuning.OfferResponseSeconds)
            {
                list.Add(new ResidentInteractionOption("うなずく", "Nod", () => DoGesture(rid, PlayerGesture.Nod), 10));
                list.Add(new ResidentInteractionOption("首を振る", "Shake head", () => DoGesture(rid, PlayerGesture.ShakeHead), 11));
                list.Add(new ResidentInteractionOption("首をかしげる（考える）", "Tilt head (think)", () => DoGesture(rid, PlayerGesture.Think), 12));
            }
            if (HasDeliverableFor(rid, player.PlayerId))
                list.Add(new ResidentInteractionOption("持ち物を見せて渡す", "Show and hand over an item", () => OpenGivePicker(rid), 20));
            return list;
        }

        private bool HasDeliverableFor(string residentId, string playerId)
        {
            foreach (var q in S.quests)
            {
                if (q.playerId != playerId || !q.IsOpen) continue;
                if (q.residentId == residentId && q.MainGoal != null && (q.MainGoal.type == GoalType.DeliverItem || q.MainGoal.type == GoalType.GiftFor)) return true;
            }
            var p = LocalPlayer;
            if (p != null)
            {
                foreach (var s in p.Inventory.NonEmpty())
                {
                    if (string.IsNullOrEmpty(s.data)) continue;
                    if (s.data.StartsWith(QuestItemIds.DataLostPrefix) || s.data.StartsWith(QuestItemIds.DataParcelPrefix)) return true;
                }
            }
            foreach (var e in S.events)
                if (e.kind == VillageEventKind.Birthday && e.day == Today && e.hostResidentId == residentId) return true;
            return false;
        }

        internal void DoGesture(string residentId, PlayerGesture g)
        {
            var p = LocalPlayer;
            if (p == null) return;
            Game.PlayerSys?.LocalController?.PlayGesture(g);
            TryHandleGesture(residentId, p.PlayerId, g);
            EventBus.Publish(new PlayerGestureEvent { PlayerId = p.PlayerId, Gesture = g, TargetResidentId = residentId });
        }

        /// <summary>持ち物から選んで手渡す（何を渡すかはプレイヤーが決める。正解の候補は示さない）。</summary>
        internal void OpenGivePicker(string residentId)
        {
            var p = LocalPlayer;
            if (p == null) return;
            UIManager.PickItem(p, Loc.T("渡す物を選ぶ", "Choose what to hand over"), s => s.Definition.Category != ItemCategory.Tool, (slot, stack) =>
            {
                int count = 1;
                var probe = stack.CloneWithCount(stack.count);
                var ev = EvaluateGive(residentId, p.PlayerId, probe);
                if ((ev.kind == GiveKind.QuestComplete || ev.kind == GiveKind.QuestProgress) && ev.goal != null)
                    count = Mathf.Min(stack.count, ev.goal.Remaining);
                var taken = p.Inventory.TakeFromSlot(slot, count, "give:" + residentId);
                if (taken == null) return;
                if (TryHandleItemGiven(residentId, p.PlayerId, taken)) return;
                // 依頼に関係ない物：受け取らずに返してくれる（ふつうの贈り物は住民の会話メニューから）
                p.GiveOrDrop(taken, "returned");
                Say(residentId, new Utterance(residentId, QuestSpeech.TagDifferent)
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.HMM))
                    .Face(Expression.Confused).With(Gesture.Shrug).With(Gesture.OfferItem, taken.itemId));
            });
        }
    }
}
