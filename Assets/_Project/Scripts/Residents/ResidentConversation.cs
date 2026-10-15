using System;
using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.UI;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 主人公との会話（[E]で話す）。住民は宇宙語（意味データ）で話し、主人公は話さない。
    // 選択肢：身振りで返す／物を渡す／物を見せる／他モジュールの項目（RegisterInteractionProvider）／立ち去る。
    // 星の状態を変える部分（親しさ・贈り物・引き留め）は WorldCommand でホストが確定する。
    // =====================================================================

    [Serializable]
    public class ResidentTalkCommand : WorldCommand
    {
        public string residentId;
    }

    [Serializable]
    public class ResidentEndTalkCommand : WorldCommand
    {
        public string residentId;
    }

    [Serializable]
    public class ResidentGiftCommand : WorldCommand
    {
        public string residentId;
        public string itemId;
        public int quality;
        /// <summary>選んだ持ち物の枠（ローカルの発行者なら、その枠の物を正確に取り出す）。</summary>
        public int slot = -1;
        /// <summary>選んだ物の大きさ・固有データ（釣果の大きさ、預かり物・落とし物の印など。依頼の判定に必要）。</summary>
        public float size;
        public string data;
    }

    [Serializable]
    public class ResidentGestureCommand : WorldCommand
    {
        public string residentId;
        public int gesture;
        /// <summary>Game.PerformLocalGesture が既にクエストへ渡した場合は true。</summary>
        public bool skipQuest;
        /// <summary>ShowItem のときに見せた物。</summary>
        public string itemId;
    }

    public sealed partial class ResidentSystem
    {
        // ---------------- 会話の開始・終了（ローカル） ----------------

        internal bool CanTalk(Resident r, PlayerState player)
        {
            if (r == null || player == null || !player.IsLocal) return false;
            // 家の中の代理を表示中は本体が隠れていても話せる（OpenConversation）
            if (r.State.hidden && !r.HasIndoorProxy || r.State.activity == ResidentActivity.Sleeping) return false;
            return UIManager.Find<ResidentConversationWindow>() == null;
        }

        internal void BeginConversation(Resident r, PlayerState player)
        {
            if (!CanTalk(r, player)) return;
            UIManager.Open(new ResidentConversationWindow(this, r, player));
        }

        internal void OnConversationOpened(ResidentConversationWindow w)
        {
            var r = w.Resident;
            var player = w.Player;
            conversationResidentId = r.Id;
            string pid = player.PlayerId;
            bool firstMeet = !r.Knows(pid);
            if (r.Avatar != null)
            {
                r.Avatar.LookAt(player.Position + Vector3.up);
                var d = player.Position - r.Avatar.transform.position;
                d.y = 0;
                if (d.sqrMagnitude > 0.01f && IsAuthority && !r.HasIndoorProxy) r.Avatar.transform.rotation = Quaternion.LookRotation(d);
            }
            var tablet = Game.Service<ITabletService>();
            if (firstMeet && tablet != null)
            {
                try { tablet.RecordResidentMet(r.Id); }
                catch (Exception e) { Debug.LogException(e); }
            }
            ObservationOf(r, out var key, out var ja, out var en);
            RecordObservation(r, key, ja, en);

            // 依頼は他モジュールのメニュー項目を集めるより先に取得する（1回目から依頼への身振りの選択肢が出るように）
            Utterance questTalk = null;
            var quest = Game.Service<IQuestService>();
            if (quest != null)
            {
                try { questTalk = quest.GetRequestTalk(r.Id, pid); }
                catch (Exception e) { Debug.LogException(e); }
            }
            w.RefreshOptions();

            string catchphrase = null;
            var story = Game.Service<IStoryService>();
            bool isGuide = story != null && !string.IsNullOrEmpty(story.GuideResidentId) ? story.GuideResidentId == r.Id : r.IsGuide;
            if (isGuide && story != null) catchphrase = story.GuideCatchphraseConcept;

            int hour = Clock != null ? Clock.Hour : 12;
            WorldAuthority.Submit(new ResidentTalkCommand { residentId = r.Id }, res =>
            {
                if (!w.IsOpen) return;
                w.Enqueue(ResidentLines.Greeting(r, pid, firstMeet, catchphrase, hour));
                string tag = res != null && res.Success ? res.MessageEn ?? "" : "";
                foreach (var t in tag.Split('|'))
                {
                    if (t.StartsWith("farewell:")) w.Enqueue(ResidentLines.Farewell(r, pid, t.Substring(9)));
                    else if (t.StartsWith("offer:")) w.Enqueue(ResidentLines.OfferFood(r, t.Substring(6), pid));
                    else if (t.StartsWith("gift:"))
                    {
                        string item = t.Substring(5);
                        var u = new Utterance(r.Id, "resident.gift_to_player").Related(item);
                        u.Say(ResidentLines.F(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ResidentLines.ItemArg(item)).Recipient(Concepts.YOU));
                        u.Face(Expression.Shy).With(Gesture.OfferItem, item);
                        w.Enqueue(u);
                    }
                }
                if (!firstMeet || r.P.talkativeness > 0.5f)
                {
                    var topic = ResidentLines.TalkTopic(r, pid, this, SeededRandom.For(seed, "talk." + r.Id + "." + Now + "." + pid));
                    if (topic != null) w.Enqueue(topic);
                }
                if (questTalk != null)
                {
                    questTalk.speakerId = r.Id;
                    w.Enqueue(questTalk);
                }
            });
        }

        internal void OnConversationClosed(ResidentConversationWindow w)
        {
            if (conversationResidentId == w.Resident.Id) conversationResidentId = null;
            var r = w.Resident;
            if (r.Avatar != null && r.State.active && w.SaidAnything)
                r.Avatar.PlayGesture(new GestureCue(Gesture.Wave));
            WorldAuthority.Submit(new ResidentEndTalkCommand { residentId = r.Id });
        }

        // ---------------- 主人公の行動（ローカル → コマンド） ----------------

        internal void PlayerGives(ResidentConversationWindow w, int slot, ItemStack stack)
        {
            var r = w.Resident;
            string itemId = stack.itemId;
            int quality = stack.quality;
            string pid = w.Player.PlayerId;
            bool birthday = r.IsBirthday(Today);
            WorldAuthority.Submit(new ResidentGiftCommand { residentId = r.Id, itemId = itemId, quality = quality, slot = slot, size = stack.size, data = stack.data }, res =>
            {
                if (res == null || !res.Success) return;
                string tag = res.MessageEn ?? "";
                if (tag == "quest") { w.Note(Loc.T("（依頼の品を渡した）", "(You handed over the requested item)")); return; }
                int reaction = 0;
                bool repeated = false;
                var parts = tag.Split(':');
                if (parts.Length >= 3 && parts[0] == "gift")
                {
                    int.TryParse(parts[1], out reaction);
                    repeated = parts[2] == "1";
                }
                var u = ResidentLines.GiftResponse(r, itemId, reaction, birthday, repeated);
                w.Enqueue(u);
                string name = ItemRegistry.NameOf(itemId);
                RecordObservation(r, "gift:" + itemId, string.Format("{0}を渡したら、{1}", name, LineageExpressionJa(u.expression)),
                    string.Format("Gave {0}; it looked {1}", name, LineageExpressionEn(u.expression)));
            });
        }

        internal void PlayerShows(ResidentConversationWindow w, ItemStack stack)
        {
            var r = w.Resident;
            string itemId = stack.itemId;
            Game.PlayerSys?.LocalController?.PlayGesture(PlayerGesture.ShowItem);
            PublishOwnGesture(w.Player.PlayerId, PlayerGesture.ShowItem, r.Id);
            if (LocalQuestGesture(r, w.Player.PlayerId, PlayerGesture.ShowItem)) return;
            WorldAuthority.Submit(new ResidentGestureCommand { residentId = r.Id, gesture = (int)PlayerGesture.ShowItem, itemId = itemId, skipQuest = true }, res =>
            {
                if (res == null || !res.Success || res.MessageEn == "quest") return;
                var u = ResidentLines.ShowItemResponse(r, itemId);
                w.Enqueue(u);
                string name = ItemRegistry.NameOf(itemId);
                RecordObservation(r, "show:" + itemId, string.Format("{0}を見せたら、{1}", name, LineageExpressionJa(u.expression)),
                    string.Format("Showed {0}; it looked {1}", name, LineageExpressionEn(u.expression)));
            });
        }

        internal void PlayerGestures(ResidentConversationWindow w, PlayerGesture g)
        {
            var r = w.Resident;
            string pid = w.Player.PlayerId;
            Game.PlayerSys?.LocalController?.PlayGesture(g);
            PublishOwnGesture(pid, g, r.Id);
            var last = w.LastUtterance;
            if (LocalQuestGesture(r, pid, g)) return;
            WorldAuthority.Submit(new ResidentGestureCommand { residentId = r.Id, gesture = (int)g, skipQuest = true }, res =>
            {
                if (res == null || !res.Success) return;
                string tag = res.MessageEn ?? "";
                if (tag == "quest") return;
                if (tag == "stay") { w.Enqueue(StayLine(r)); return; }
                var u = ResidentLines.GestureResponse(r, g, pid, this, last, out bool repeat);
                if (repeat && last != null) w.Enqueue(last);
                else if (u != null) w.Enqueue(u);
                if (tag == "follow") w.CloseAfterQueue();
            });
        }

        /// <summary>
        /// 依頼への返事としての身振りは、身振りをした本人の端末でクエストに渡す（Game.PerformLocalGesture と同じ扱い）。
        /// IQuestService.TryHandleGesture はローカルのプレイヤーの身振りしか処理しない（引き受け・断るは中で WorldCommand を送る）ため、
        /// ホストのコマンド処理で呼ぶとゲストの身振りが依頼に届かない。処理されたら住民側の反応はしない。
        /// </summary>
        private bool LocalQuestGesture(Resident r, string playerId, PlayerGesture g)
        {
            var quest = Game.Service<IQuestService>();
            if (quest == null) return false;
            try { return quest.TryHandleGesture(r.Id, playerId, g); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        private void PublishOwnGesture(string playerId, PlayerGesture g, string residentId)
        {
            suppressGestureEvent = true;
            try { EventBus.Publish(new PlayerGestureEvent { PlayerId = playerId, Gesture = g, TargetResidentId = residentId }); }
            finally { suppressGestureEvent = false; }
        }

        internal Utterance StayLine(Resident r)
        {
            var u = new Utterance(r.Id, "resident.stay");
            u.Say(ResidentLines.F(SpeechAct.Statement, Concepts.OK));
            u.Say(ResidentLines.F(SpeechAct.Statement, Concepts.LIVE).Agent(Concepts.I).At(Concepts.HERE).Adverb(Concepts.STILL).Future());
            u.Face(Expression.Crying).With(Gesture.Hug).With(Gesture.Nod);
            return u;
        }

        private static string LineageExpressionJa(Expression e)
        {
            var cur = Loc.Current;
            Loc.Current = UILanguage.Japanese;
            string s = LineageText.ExpressionText(e);
            Loc.Current = cur;
            return s;
        }

        private static string LineageExpressionEn(Expression e)
        {
            var cur = Loc.Current;
            Loc.Current = UILanguage.English;
            string s = LineageText.ExpressionText(e);
            Loc.Current = cur;
            return s;
        }

        internal List<ResidentInteractionOption> CollectOptions(Resident r, PlayerState player)
        {
            var list = new List<ResidentInteractionOption>();
            foreach (var p in providers.ToArray())
            {
                if (p == null) continue;
                try
                {
                    var opts = p.GetOptions(r, player);
                    if (opts != null) foreach (var o in opts) if (o != null) list.Add(o);
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            list.Sort((a, b) => a.Order.CompareTo(b.Order));
            return list;
        }

        // ---------------- コマンド処理（ホスト） ----------------

        private CommandResult HandleTalkCommand(ResidentTalkCommand cmd, CommandContext ctx)
        {
            var r = GetResident(cmd.residentId);
            if (r == null || !(r.State.active || r.IsVisitingFormer)) return CommandResult.Fail("相手がいません", "Nobody is there");
            string pid = ctx.IssuerId;
            int day = Today;
            var b = r.Bond(pid);
            bool first = !b.met;
            if (first)
            {
                b.met = true;
                b.firstMetDay = day;
                ChangeFriendship(r.Id, pid, ResidentTuning.FriendshipFirstMeet, "first_meet");
                r.AddMemory(new ResidentMemoryEntry { kind = "met_player", playerId = pid, day = day, importance = 0.8f });
                Reputation(pid);
            }
            else if (b.lastTalkDay != day)
            {
                ChangeFriendship(r.Id, pid, ResidentTuning.FriendshipTalkDaily, "talk");
            }
            b.lastTalkDay = day;
            b.totalTalks++;
            r.State.holdUntil = Now + 30;
            r.State.holdForPlayerId = pid;
            if (!string.IsNullOrEmpty(r.State.chatPartnerId)) EndChat(r);
            r.hasTarget = false;
            r.path.Clear();

            var tags = new List<string>();
            var rng = SeededRandom.For(seed, "talkcmd." + r.Id + "." + day + "." + b.totalTalks);
            // お別れの贈り物（引っ越し前日：第4章）
            if (r.IsMovingOut && r.State.moveOutDay - day <= 1 && b.friendship >= ResidentTuning.OfferGiftFriendship && b.farewellGiftDay < 0)
            {
                string item = PickGiftForPlayer(r, true);
                if (item != null)
                {
                    ctx.GiveItem(new ItemStack(item, 1), "resident_farewell");
                    b.farewellGiftDay = day;
                    tags.Add("farewell:" + item);
                    r.AddMemory(new ResidentMemoryEntry { kind = "gave_player_gift", playerId = pid, itemId = item, day = day, importance = 0.8f });
                }
            }
            // 食べ物をくれる（料理人・案内役・気前のいい住民：メインストーリー第1章「何かと食べ物をくれる」）
            else if (b.lastOfferDay != day)
            {
                bool giver = r.State.job == ResidentJob.Cook || r.IsGuide || r.P.generosity > 0.8f && b.friendship >= 10;
                float chance = r.State.job == ResidentJob.Cook ? 0.6f : r.IsGuide ? 0.4f : 0.2f;
                var food = r.FindItem(d => Resident.IsEdible(d));
                if (giver && food != null && string.IsNullOrEmpty(food.data) && rng.Chance(chance))
                {
                    string id = food.itemId;
                    r.RemoveItem(id, 1);
                    ctx.GiveItem(new ItemStack(id, 1), "resident_offer");
                    b.lastOfferDay = day;
                    tags.Add("offer:" + id);
                }
                else if (b.friendship >= ResidentTuning.OfferGiftFriendship && rng.Chance(0.1f + r.P.generosity * 0.1f))
                {
                    string item = PickGiftForPlayer(r, false);
                    if (item != null)
                    {
                        r.RemoveItem(item, 1);
                        ctx.GiveItem(new ItemStack(item, 1), "resident_gift");
                        b.lastOfferDay = day;
                        tags.Add("gift:" + item);
                        r.AddMemory(new ResidentMemoryEntry { kind = "gave_player_gift", playerId = pid, itemId = item, day = day, importance = 0.6f });
                    }
                }
            }
            EventBus.Publish(new ResidentTalkedEvent { PlayerId = pid, ResidentId = r.Id });
            MarkDirty();
            return CommandResult.Ok(null, string.Join("|", tags.ToArray()));
        }

        /// <summary>主人公への贈り物（持ち物から。お別れなら一番気に入っている物、無ければ包み）。</summary>
        private string PickGiftForPlayer(Resident r, bool farewell)
        {
            ItemStack best = null;
            float bv = farewell ? -9f : 0.1f;
            foreach (var s in r.State.items)
            {
                if (s == null || s.IsEmpty || !string.IsNullOrEmpty(s.data)) continue;
                float v = r.EvaluateItem(s.Definition);
                if (v > bv) { bv = v; best = s; }
            }
            if (best != null)
            {
                if (farewell) r.RemoveItem(best.itemId, 1);
                return best.itemId;
            }
            return farewell && ItemRegistry.Exists(SharedItemIds.GiftBox) ? SharedItemIds.GiftBox : null;
        }

        private CommandResult HandleEndTalkCommand(ResidentEndTalkCommand cmd, CommandContext ctx)
        {
            var r = GetResident(cmd.residentId);
            if (r != null && r.State.holdForPlayerId == ctx.IssuerId)
            {
                r.State.holdUntil = 0;
                r.State.holdForPlayerId = null;
                r.State.blockIndex = -1;
                MarkDirty();
            }
            return CommandResult.Ok();
        }

        /// <summary>
        /// 物を渡す（統括との取り決め）：①主人公の持ち物から先に取り除く → ②IQuestService.TryHandleItemGiven。
        /// true ならその物の行き先はクエスト側が決める（住民は何もしない）。false なら贈り物として住民が受け取る。
        /// </summary>
        private CommandResult HandleGiftCommand(ResidentGiftCommand cmd, CommandContext ctx)
        {
            var r = GetResident(cmd.residentId);
            if (r == null || string.IsNullOrEmpty(cmd.itemId)) return CommandResult.Fail("相手がいません", "Nobody is there");
            if (!ctx.HasItem(cmd.itemId, 1)) return CommandResult.Fail("その物を持っていません", "You don't have that");
            var stack = TakeGiftItem(cmd, ctx, "gift:" + r.Id);
            if (stack == null) return CommandResult.Fail("渡せませんでした", "Could not give it");
            string pid = ctx.IssuerId;
            r.State.holdUntil = Now + 30;
            r.State.holdForPlayerId = pid;

            var quest = Game.Service<IQuestService>();
            bool handled = false;
            if (quest != null)
            {
                try { handled = quest.TryHandleItemGiven(r.Id, pid, stack); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (handled)
            {
                MarkDirty();
                return CommandResult.Ok(null, "quest");
            }

            int day = Today;
            var def = stack.Definition;
            int reaction = ResidentLines.GiftReaction(r, def, stack.quality, day);
            var b = r.Bond(pid);
            b.met = true;
            if (b.lastGiftDay != day) b.giftsToday = 0;
            bool repeated = b.giftsToday > 0;
            b.giftsToday++;
            b.totalGifts++;
            b.lastGiftDay = day;
            int delta;
            switch (reaction)
            {
                case 2: delta = ResidentTuning.FriendshipGiftLoved; break;
                case 1: delta = ResidentTuning.FriendshipGiftLiked; break;
                case 0: delta = ResidentTuning.FriendshipGiftNeutral; break;
                default: delta = ResidentTuning.FriendshipGiftDisliked; break;
            }
            if (repeated && delta > 0) delta = Mathf.Max(1, Mathf.RoundToInt(delta * ResidentTuning.RepeatGiftFactor));
            if (r.IsBirthday(day) && delta > 0) delta = Mathf.RoundToInt(delta * 1.5f);
            ChangeFriendship(r.Id, pid, delta, "gift");
            r.AddItem(stack);
            HandleReceivedItem(r, stack, "gift_from_player");
            if (Resident.IsEdible(def) && reaction >= 1 && r.State.hunger > 0.3f) EatSomething(r, false);
            r.AddMemory(new ResidentMemoryEntry { kind = "gift", playerId = pid, itemId = cmd.itemId, day = day, importance = reaction >= 2 ? 0.9f : reaction >= 0 ? 0.6f : 0.5f, detail = reaction.ToString() });
            r.State.mood = Mathf.Clamp(r.State.mood + reaction * 0.1f, -1f, 1f);
            Reputation(pid).giftsGiven++;
            // 近くで見ていた住民も覚えている（後で噂になる）
            foreach (var o in Witnesses(r.Position, 12f))
                if (o != r) Remember(o, "saw_gift", pid, cmd.itemId, 0.3f);
            EventBus.Publish(new GiftGivenEvent { PlayerId = pid, ResidentId = r.Id, ItemId = cmd.itemId, Quality = stack.quality, Reaction = reaction });
            MarkDirty();
            return CommandResult.Ok(null, "gift:" + reaction + ":" + (repeated ? "1" : "0"));
        }

        /// <summary>
        /// 渡す物を主人公の持ち物から取り出す。ローカルの発行者は、選んだ枠（無ければ同じ品質・同じデータの枠）から正確に取り出す。
        /// （CommandContext.TakeItem は品質の低い物から取るため、★3 を選んでも ★0 が減り、住民には ★3 が渡る＝品質が増える。
        /// また大きさ・固有データが失われ、依頼の大きさ判定・預かり物の判定ができなくなる。）
        /// リモートのゲストは CommandContext の制約上 itemId で取り出し、品質・大きさ・データはゲスト側の申告を使う。
        /// </summary>
        private static ItemStack TakeGiftItem(ResidentGiftCommand cmd, CommandContext ctx, string reason)
        {
            int quality = Mathf.Clamp(cmd.quality, 0, 3);
            string data = cmd.data ?? "";
            if (ctx.IsLocalIssuer)
            {
                var inv = ctx.Issuer.Inventory;
                int slot = -1;
                var picked = inv.GetSlot(cmd.slot);
                if (picked != null && !picked.IsEmpty && picked.itemId == cmd.itemId && picked.quality == quality && (picked.data ?? "") == data) slot = cmd.slot;
                else
                {
                    for (int i = 0; i < inv.slots.Count; i++)
                    {
                        var s = inv.slots[i];
                        if (s == null || s.IsEmpty || s.itemId != cmd.itemId || s.quality != quality || (s.data ?? "") != data) continue;
                        slot = i;
                        break;
                    }
                }
                if (slot < 0) return null;
                var taken = inv.TakeFromSlot(slot, 1, reason);
                if (taken == null || taken.IsEmpty) return null;
                taken.ownerId = null;
                return taken;
            }
            var like = new ItemStack(cmd.itemId, 1, quality) { data = string.IsNullOrEmpty(cmd.data) ? null : cmd.data };
            var got = ctx.TakeItemExact(like, 1, reason);
            if (got == null) return null;
            got.size = cmd.size;
            got.ownerId = null;
            return got;
        }

        private CommandResult HandleGestureCommand(ResidentGestureCommand cmd, CommandContext ctx)
        {
            var r = GetResident(cmd.residentId);
            if (r == null) return CommandResult.Fail("相手がいません", "Nobody is there");
            var g = (PlayerGesture)cmd.gesture;
            string pid = ctx.IssuerId;
            if (!cmd.skipQuest)
            {
                var quest = Game.Service<IQuestService>();
                bool handled = false;
                if (quest != null)
                {
                    try { handled = quest.TryHandleGesture(r.Id, pid, g); }
                    catch (Exception e) { Debug.LogException(e); }
                }
                if (handled) { MarkDirty(); return CommandResult.Ok(null, "quest"); }
            }
            int day = Today;
            var b = r.Bond(pid);
            string tag = "";
            switch (g)
            {
                case PlayerGesture.Wave:
                case PlayerGesture.Bow:
                case PlayerGesture.Clap:
                case PlayerGesture.Happy:
                    if (b.lastGestureBonusDay != day)
                    {
                        b.lastGestureBonusDay = day;
                        ChangeFriendship(r.Id, pid, ResidentTuning.FriendshipGestureDaily, "gesture");
                    }
                    break;
                case PlayerGesture.ShakeHead:
                    if (r.IsMovingOut)
                    {
                        // 引き留め：親しければ考え直すこともある。ただし夢のための引っ越しは本人の意思が強い（§11）
                        b.persuadedDay = day;
                        bool strong = r.State.moveReason == MoveReason.Dream;
                        if (b.friendship >= ResidentTuning.PersuadeFriendship && (!strong || b.friendship >= 85) && r.State.moveOutDay - day >= 1)
                        {
                            CancelMoveOut(r, "persuaded");
                            ChangeFriendship(r.Id, pid, 3, "persuaded");
                            tag = "stay";
                        }
                        else r.AddMemory(new ResidentMemoryEntry { kind = "asked_to_stay", playerId = pid, day = day, importance = 0.8f });
                    }
                    break;
                case PlayerGesture.Nod:
                    if (r.IsMovingOut && b.encouragedDay != day)
                    {
                        b.encouragedDay = day;
                        r.AddMemory(new ResidentMemoryEntry { kind = "encouraged", playerId = pid, day = day, importance = 0.7f });
                    }
                    break;
                case PlayerGesture.Beckon:
                {
                    var act = r.State.activity;
                    bool busy = act == ResidentActivity.Working || act == ResidentActivity.Crafting || act == ResidentActivity.Packing;
                    if (!busy && b.friendship >= 15 && r.State.active)
                    {
                        r.State.followPlayerId = pid;
                        r.State.followUntil = Now + 60;
                        r.State.holdUntil = 0;
                        r.State.chatPartnerId = null;
                        tag = "follow";
                    }
                    break;
                }
                case PlayerGesture.ShowItem:
                    if (!string.IsNullOrEmpty(cmd.itemId))
                        r.AddMemory(new ResidentMemoryEntry { kind = "shown_item", playerId = pid, itemId = cmd.itemId, day = day, importance = 0.3f });
                    break;
            }
            if (tag != "follow" && r.State.holdForPlayerId == pid && r.State.holdUntil > 0) r.State.holdUntil = Now + 30;
            MarkDirty();
            return CommandResult.Ok(null, tag);
        }
    }

    /// <summary>会話ウィンドウ（画面下部。ソロでは時間停止）。</summary>
    public sealed class ResidentConversationWindow : UIWindow
    {
        internal readonly ResidentSystem Sys;
        internal readonly Resident Resident;
        internal readonly PlayerState Player;
        internal Utterance LastUtterance;
        internal bool SaidAnything;

        private readonly Queue<Utterance> queue = new Queue<Utterance>();
        private readonly List<Line> lines = new List<Line>();
        private float nextTime;
        private bool gestureMode;
        private bool closeAfterQueue;
        private List<ResidentInteractionOption> extra = new List<ResidentInteractionOption>();
        private GUIStyle tokenStyle;
        private Vector2 lineScroll;

        private class Line
        {
            public UtteranceRecord rec;
            public Utterance u;
            public string note;
        }

        private static readonly PlayerGesture[] GestureChoices =
        {
            PlayerGesture.Nod, PlayerGesture.ShakeHead, PlayerGesture.Wave, PlayerGesture.Bow, PlayerGesture.Clap,
            PlayerGesture.Think, PlayerGesture.Happy, PlayerGesture.Sad, PlayerGesture.Surprised, PlayerGesture.Point, PlayerGesture.Beckon
        };

        public ResidentConversationWindow(ResidentSystem sys, Resident r, PlayerState player)
        {
            Sys = sys;
            Resident = r;
            Player = player;
        }

        public override string Title => LineageText.Describe(Resident.Lineage);
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(Mathf.Min(1200f, UIStyles.VirtualWidth - 40f), 480f);
        public override Vector2? Position => new Vector2((UIStyles.VirtualWidth - Size.x) * 0.5f, UIStyles.VirtualHeight - Size.y - 24f);

        public override void OnOpen() => Sys.OnConversationOpened(this);
        public override void OnClose() => Sys.OnConversationClosed(this);

        internal void Enqueue(Utterance u)
        {
            if (u == null || u.sentences.Count == 0 && u.gestures.Count == 0) return;
            queue.Enqueue(u);
        }

        internal void Note(string text) => lines.Add(new Line { note = text });

        internal void CloseAfterQueue() => closeAfterQueue = true;

        internal void RefreshOptions() => extra = Sys.CollectOptions(Resident, Player);

        public override void Update()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (queue.Count > 0 && now >= nextTime)
            {
                var u = queue.Dequeue();
                // 主人公へ直接話す言葉（中継先では「自分に向けた言葉か」をこれで判定する）
                if (string.IsNullOrEmpty(u.addresseeId)) u.addresseeId = Player.PlayerId;
                var rec = Sys.Speak(Resident.Id, u, SpeechChannel.Direct);
                lines.Add(new Line { rec = rec, u = u });
                if (lines.Count > 12) lines.RemoveAt(0);
                LastUtterance = u;
                SaidAnything = true;
                nextTime = now + 1.4f + 0.6f * u.sentences.Count;
                lineScroll.y = 99999f;
            }
            if (closeAfterQueue && queue.Count == 0 && now >= nextTime) Close();
            // 相手がいなくなった・離れすぎた（家の中の代理を表示中は代理の位置で測る：Resident.Position）
            if (!Resident.State.active && !Resident.IsVisitingFormer || Resident.Avatar == null || ResidentUtil.FlatDistance(Resident.Position, Player.Position) > 10f)
                Close();
        }

        public override void DrawContents(Rect content)
        {
            if (tokenStyle == null)
            {
                tokenStyle = new GUIStyle(UIStyles.ButtonSmall) { fontSize = 22, wordWrap = false, alignment = TextAnchor.MiddleCenter };
                tokenStyle.normal.textColor = UIStyles.AlienText;
                tokenStyle.hover.textColor = Color.white;
            }
            var tablet = Game.Instance != null ? Game.Instance.Service<ITabletService>() : null;
            lineScroll = GUILayout.BeginScrollView(lineScroll, GUILayout.Height(content.height - 150f));
            foreach (var line in lines) DrawLine(line, content.width - 40f, tablet);
            if (queue.Count > 0) GUILayout.Label("……", UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.FlexibleSpace();
            if (gestureMode) DrawGestures(content);
            else DrawMain(content);
        }

        private void DrawLine(Line line, float width, ITabletService tablet)
        {
            if (!string.IsNullOrEmpty(line.note))
            {
                GUILayout.Label(line.note, UIStyles.LabelDim);
                return;
            }
            GUILayout.BeginVertical(UIStyles.Box);
            if (line.rec != null && line.rec.tokens != null && line.rec.tokens.Count > 0)
            {
                float x = 0f;
                GUILayout.BeginHorizontal();
                foreach (var tok in line.rec.tokens)
                {
                    if (tok == null || string.IsNullOrEmpty(tok.surface)) continue;
                    string label = tok.surface;
                    if (tablet != null && tablet.ShowGlossOverlay)
                    {
                        string gloss = null;
                        try { gloss = tablet.GetPlayerGloss(tok.surface); } catch { }
                        if (!string.IsNullOrEmpty(gloss)) label += "\n<size=15>" + gloss + "</size>";
                    }
                    var gc = new GUIContent(label);
                    var size = tokenStyle.CalcSize(gc);
                    if (x + size.x > width && x > 0f)
                    {
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                        GUILayout.BeginHorizontal();
                        x = 0f;
                    }
                    if (tok.attachedToPrevious) GUILayout.Space(-6f);
                    // 単語を選ぶと辞書の該当ページを開く（タブレットが処理）。意味は表示しない
                    if (GUILayout.Button(gc, tokenStyle))
                        EventBus.Publish(new DictionaryLookupRequestEvent { Surface = tok.surface, RecordId = line.rec.recordId });
                    x += size.x + 8f;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else if (line.rec != null && !string.IsNullOrEmpty(line.rec.text))
            {
                GUILayout.Label(line.rec.text, UIStyles.LabelAlien);
            }
            else
            {
                GUILayout.Label(Loc.T("（何か話しているが、聞き取れない）", "(It says something you can't make out)"), UIStyles.LabelDim);
            }
            // 言葉以外の手がかり（表情・身振り）。意味の訳は出さない
            var parts = new List<string> { LineageText.ExpressionText(line.u.expression) };
            foreach (var g in line.u.gestures)
            {
                string gt = LineageText.GestureText(g.gesture, g.number);
                if (!string.IsNullOrEmpty(gt))
                {
                    if (g.gesture == Gesture.OfferItem && !string.IsNullOrEmpty(g.target) && ItemRegistry.Exists(g.target))
                        gt = Loc.Format("{0}を差し出した", "held out {0}", ItemRegistry.NameOf(g.target));
                    parts.Add(gt);
                }
            }
            GUILayout.Label("（" + string.Join("・", parts.ToArray()) + "）", UIStyles.LabelDim);
            GUILayout.EndVertical();
        }

        private void DrawMain(Rect content)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("身振りで返す", "Gesture"), UIStyles.Button, GUILayout.Height(46))) gestureMode = true;
            if (GUILayout.Button(Loc.T("物を渡す", "Give item"), UIStyles.Button, GUILayout.Height(46)))
            {
                UIManager.PickItem(Player, Loc.T("渡す物を選ぶ", "Choose something to give"),
                    s => s.Definition.Category != ItemCategory.KeyItem,
                    (idx, stack) => Sys.PlayerGives(this, idx, stack));
            }
            if (GUILayout.Button(Loc.T("物を見せる", "Show item"), UIStyles.Button, GUILayout.Height(46)))
            {
                UIManager.PickItem(Player, Loc.T("見せる物を選ぶ", "Choose something to show"), null, (idx, stack) => Sys.PlayerShows(this, stack));
            }
            if (GUILayout.Button(Loc.T("立ち去る", "Leave"), UIStyles.Button, GUILayout.Height(46))) Close();
            GUILayout.EndHorizontal();
            if (extra.Count > 0)
            {
                GUILayout.BeginHorizontal();
                foreach (var o in extra)
                {
                    if (GUILayout.Button(o.Label, UIStyles.ButtonSmall, GUILayout.Height(40)))
                    {
                        try { o.Execute?.Invoke(); }
                        catch (Exception e) { Debug.LogException(e); }
                        RefreshOptions();
                        GUIUtility.ExitGUI();
                    }
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }

        private void DrawGestures(Rect content)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < GestureChoices.Length; i++)
            {
                var g = GestureChoices[i];
                if (GUILayout.Button(PlayerGestureUtil.Label(g), UIStyles.ButtonSmall, GUILayout.Height(42)))
                {
                    gestureMode = false;
                    Sys.PlayerGestures(this, g);
                }
                if (i == 5)
                {
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                }
            }
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.ButtonSmall, GUILayout.Height(42))) gestureMode = false;
            GUILayout.EndHorizontal();
        }
    }
}
