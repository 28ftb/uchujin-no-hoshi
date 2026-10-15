using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.World;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 住民同士の関係・交流、主人公の噂と記憶、聞こえる会話（住民生活仕様 §5・§6・§7・§15・§16）
    // =====================================================================
    public sealed partial class ResidentSystem
    {
        private string conversationResidentId;
        private bool suppressGestureEvent;
        private readonly HashSet<string> observedKeys = new HashSet<string>();
        private int socialAccumulator;

        // ---------------- イベント購読 ----------------

        private void SubscribeEvents()
        {
            if (subscribed) return;
            subscribed = true;
            EventBus.Subscribe<FishCaughtEvent>(OnFishCaught);
            EventBus.Subscribe<OreMinedEvent>(OnOreMined);
            EventBus.Subscribe<CropHarvestedEvent>(OnCropHarvested);
            EventBus.Subscribe<CreatureCaughtEvent>(OnCreatureCaught);
            EventBus.Subscribe<ShopItemSoldEvent>(OnShopItemSold);
            EventBus.Subscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
            EventBus.Subscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Subscribe<QuestStateChangedEvent>(OnQuestStateChanged);
            EventBus.Subscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Subscribe<ItemCraftedEvent>(OnItemCrafted);
        }

        private void UnsubscribeEvents()
        {
            if (!subscribed) return;
            subscribed = false;
            EventBus.Unsubscribe<FishCaughtEvent>(OnFishCaught);
            EventBus.Unsubscribe<OreMinedEvent>(OnOreMined);
            EventBus.Unsubscribe<CropHarvestedEvent>(OnCropHarvested);
            EventBus.Unsubscribe<CreatureCaughtEvent>(OnCreatureCaught);
            EventBus.Unsubscribe<ShopItemSoldEvent>(OnShopItemSold);
            EventBus.Unsubscribe<RocketDeviceRepairedEvent>(OnRocketRepaired);
            EventBus.Unsubscribe<PlayerGestureEvent>(OnPlayerGesture);
            EventBus.Unsubscribe<QuestStateChangedEvent>(OnQuestStateChanged);
            EventBus.Unsubscribe<WeatherChangedEvent>(OnWeatherChanged);
            EventBus.Unsubscribe<ItemCraftedEvent>(OnItemCrafted);
        }

        /// <summary>近くで見ていた住民（起きていて外にいる）。</summary>
        private List<Resident> Witnesses(Vector3 pos, float radius)
        {
            var list = new List<Resident>();
            foreach (var r in ordered)
            {
                if (!r.State.active || r.State.hidden || r.State.activity == ResidentActivity.Sleeping) continue;
                if (ResidentUtil.FlatDistance(r.Position, pos) <= radius) list.Add(r);
            }
            return list;
        }

        private void Remember(Resident r, string kind, string playerId, string itemId, float importance, bool oncePerDay = true)
        {
            if (oncePerDay && r.HasMemory(kind, playerId, Today)) return;
            r.AddMemory(new ResidentMemoryEntry { kind = kind, playerId = playerId, itemId = itemId, day = Today, importance = importance });
        }

        private void OnFishCaught(FishCaughtEvent e)
        {
            if (!IsAuthority || string.IsNullOrEmpty(e.PlayerId)) return;
            var rep = Reputation(e.PlayerId);
            rep.fishCount++;
            if (rep.lastFishDay != Today) { rep.fishDays++; rep.lastFishDay = Today; }
            var def = ItemRegistry.Get(e.ItemId);
            bool rare = def != null && def.Rarity >= 3;
            if (rare) rep.rareFishCount++;
            Resident exclaimer = null;
            foreach (var r in Witnesses(e.Position, ResidentTuning.WitnessRadius))
            {
                Remember(r, "saw_fishing", e.PlayerId, e.ItemId, 0.4f);
                if (rare) Remember(r, "saw_rare_fish", e.PlayerId, e.ItemId, 0.8f);
                // 一緒に釣った（§6：共通の思い出）
                if (r.State.activity == ResidentActivity.Fishing && ResidentUtil.FlatDistance(r.Position, e.Position) < 18f)
                    SharedActivity(r, e.PlayerId, "fished_together", e.ItemId);
                if (exclaimer == null && (rare || r.P.curiosity > 0.7f)) exclaimer = r;
            }
            if (exclaimer != null && (rare || fxRng.Chance(0.3f))) Speak(exclaimer.Id, ResidentLines.WitnessReaction(exclaimer, "fish", e.ItemId), SpeechChannel.Direct);
            MarkDirty();
        }

        private void OnOreMined(OreMinedEvent e)
        {
            if (!IsAuthority || string.IsNullOrEmpty(e.PlayerId)) return;
            var rep = Reputation(e.PlayerId);
            rep.oreCount++;
            rep.deepestFloor = Mathf.Max(rep.deepestFloor, e.Floor);
            if (rep.lastMineDay != Today)
            {
                rep.mineStreak = rep.lastMineDay == Today - 1 ? rep.mineStreak + 1 : 1;
                rep.mineDays++;
                rep.lastMineDay = Today;
                // 鉱山の入口近くにいた住民・鉱山で働く住民が、主人公が鉱山へ通っているのを見ている（§16）
                var layout = Layout;
                foreach (var r in ordered)
                {
                    if (!r.State.active) continue;
                    bool miner = r.State.activity == ResidentActivity.Mining;
                    bool near = layout != null && !r.State.hidden && ResidentUtil.FlatDistance(r.Position, layout.MineEntrance) < 35f;
                    if (miner || near) Remember(r, "saw_mining", e.PlayerId, e.ItemId, 0.4f);
                }
                var smith = FindByJobInternal(ResidentJob.Blacksmith);
                if (smith != null && rep.mineStreak >= 3) Remember(smith, "heard_mining", e.PlayerId, e.ItemId, 0.5f);
            }
            MarkDirty();
        }

        private void OnCropHarvested(CropHarvestedEvent e)
        {
            if (!IsAuthority || string.IsNullOrEmpty(e.PlayerId)) return;
            var rep = Reputation(e.PlayerId);
            rep.harvestCount += Mathf.Max(1, e.Count);
            var def = ItemRegistry.Get(e.ItemId);
            bool big = e.Quality >= 2 || def != null && def.HasTag(ItemTags.Big);
            if (big) rep.bigCropCount++;
            var player = Game.Players.Get(e.PlayerId);
            if (player == null || rep.lastHarvestDay == Today && !big) { rep.lastHarvestDay = Today; return; }
            rep.lastHarvestDay = Today;
            foreach (var r in Witnesses(player.Position, ResidentTuning.WitnessRadius))
            {
                Remember(r, "saw_harvest", e.PlayerId, e.ItemId, 0.3f);
                if (big) Remember(r, "saw_big_crop", e.PlayerId, e.ItemId, 0.7f);
                if (r.State.activity == ResidentActivity.Farming || r.State.activity == ResidentActivity.Gardening)
                    SharedActivity(r, e.PlayerId, "farmed_together", e.ItemId);
            }
            MarkDirty();
        }

        private void OnCreatureCaught(CreatureCaughtEvent e)
        {
            if (!IsAuthority || string.IsNullOrEmpty(e.PlayerId)) return;
            var rep = Reputation(e.PlayerId);
            rep.creatureCount++;
            foreach (var r in Witnesses(e.Position, ResidentTuning.WitnessRadius))
            {
                float imp = r.State.job == ResidentJob.CreatureExpert ? 0.8f : 0.5f;
                Remember(r, "saw_creature", e.PlayerId, e.SpeciesId, imp);
                if (r.State.activity == ResidentActivity.CatchingCreatures)
                    SharedActivity(r, e.PlayerId, "caught_together", e.SpeciesId);
            }
            // 生き物名人は噂でも知る（生き物仕様 §11.1）
            var expert = FindByJobInternal(ResidentJob.CreatureExpert);
            if (expert != null && expert.State.job == ResidentJob.CreatureExpert) Remember(expert, "heard_creature", e.PlayerId, e.SpeciesId, 0.6f);
            MarkDirty();
        }

        private void SharedActivity(Resident r, string playerId, string kind, string itemId)
        {
            var b = r.Bond(playerId);
            if (b.lastSharedActivityDay == Today) return;
            b.lastSharedActivityDay = Today;
            Remember(r, kind, playerId, itemId, 0.6f);
            ChangeFriendship(r.Id, playerId, ResidentTuning.FriendshipSharedActivity, kind);
        }

        private void OnShopItemSold(ShopItemSoldEvent e)
        {
            if (!IsAuthority) return;
            var r = GetResident(e.ResidentId);
            if (!string.IsNullOrEmpty(e.ShopOwnerId))
            {
                var rep = Reputation(e.ShopOwnerId);
                rep.shopSales++;
                if (rep.shopSalesRecentDay < Today - 3) rep.shopSalesRecent = 0;
                rep.shopSalesRecent++;
                rep.shopSalesRecentDay = Today;
            }
            if (r == null) return;
            r.AddMemory(new ResidentMemoryEntry { kind = "bought_from_shop", playerId = e.ShopOwnerId, itemId = e.ItemId, day = Today, importance = 0.5f, detail = e.Price.ToString() });
            if (r.State.lastBoughtDay != Today || r.State.lastBoughtItemId != e.ItemId)
            {
                r.State.lastBoughtItemId = e.ItemId;
                r.State.lastBoughtDay = Today;
                r.State.lastBoughtFromPlayer = true;
            }
            // 好きな商品を売ってくれる店の常連になる（§6）
            if (!string.IsNullOrEmpty(e.ShopOwnerId) && r.EvaluateItem(ItemRegistry.Get(e.ItemId)) > 0.4f)
            {
                var b = r.Bond(e.ShopOwnerId);
                if (b.lastGestureBonusDay != Today)
                {
                    b.lastGestureBonusDay = Today;
                    ChangeFriendship(r.Id, e.ShopOwnerId, 1, "shop_regular");
                }
            }
            MarkDirty();
        }

        private void OnRocketRepaired(RocketDeviceRepairedEvent e)
        {
            if (!IsAuthority) return;
            if (!string.IsNullOrEmpty(e.PlayerId)) Reputation(e.PlayerId).repairs++;
            var rocket = Game.Service<IRocketService>();
            Vector3 pos = rocket != null ? rocket.RocketPosition : (Layout != null ? Layout.CrashSite : Vector3.zero);
            foreach (var r in ordered)
            {
                if (!r.State.active) continue;
                bool saw = r.State.job == ResidentJob.Mechanic || !r.State.hidden && ResidentUtil.FlatDistance(r.Position, pos) < 40f;
                Remember(r, saw ? "saw_repair" : "heard_repair", e.PlayerId, e.DeviceId, saw ? 0.8f : 0.4f, false);
                if (r.Profile.dream == GoalKind.TravelStars || r.Profile.dream == GoalKind.BuildSpaceship)
                    r.State.longGoal.progress += 3f; // 宇宙船が直るのを見て、夢がふくらむ
                if (saw && r.Avatar != null && r.State.job == ResidentJob.Mechanic)
                {
                    r.Avatar.ShowExpression(Expression.VeryHappy, 4f);
                    r.Avatar.PlayGesture(new GestureCue(Gesture.Jump));
                    r.Avatar.PlayGesture(new GestureCue(Gesture.Clap));
                }
            }
            MarkDirty();
        }

        private void OnQuestStateChanged(QuestStateChangedEvent e)
        {
            if (!IsAuthority || e.Status != QuestStatus.Completed) return;
            var r = GetResident(e.ResidentId);
            if (r == null) return;
            Remember(r, "quest_done", e.PlayerId, null, 0.8f, false);
            var rel = r.Bond(e.PlayerId ?? "");
            rel.met = true;
            MarkDirty();
        }

        private void OnItemCrafted(ItemCraftedEvent e)
        {
            if (!IsAuthority || !e.FirstTime || string.IsNullOrEmpty(e.PlayerId)) return;
            var player = Game.Players.Get(e.PlayerId);
            if (player == null) return;
            foreach (var r in Witnesses(player.Position, 10f))
            {
                Remember(r, "saw_crafting", e.PlayerId, e.ItemId, 0.4f);
                if (r.State.activity == ResidentActivity.Cooking) SharedActivity(r, e.PlayerId, "cooked_together", e.ItemId);
            }
        }

        private void OnWeatherChanged(WeatherChangedEvent e)
        {
            if (!IsAuthority) return;
            if (e.Current.IsRaining && !e.Previous.IsRaining)
            {
                foreach (var r in ordered)
                {
                    if (!r.State.active || r.State.hidden || r.State.holdUntil > Now || r.State.hasGoTo) continue;
                    if (r.P.rainLike < -0.3f && IsOutdoorActivity(r.State.activity)) StartSheltering(r);
                    else if (r.P.rainLike > 0.5f && r.Avatar != null) r.Avatar.ShowExpression(Expression.Happy, 3f);
                }
                MarkDirty();
            }
        }

        private void OnPlayerGesture(PlayerGestureEvent e)
        {
            if (suppressGestureEvent || string.IsNullOrEmpty(e.TargetResidentId)) return;
            if (Game.LocalPlayer == null || e.PlayerId != Game.LocalPlayer.PlayerId) return;
            if (conversationResidentId == e.TargetResidentId) return;
            var r = GetResident(e.TargetResidentId);
            if (r == null || r.Avatar == null || !r.Avatar.IsVisible) return;
            // 会話ウィンドウの外での身振り（Game.PerformLocalGesture が既にクエストへ渡している）
            WorldAuthority.Submit(new ResidentGestureCommand { residentId = r.Id, gesture = (int)e.Gesture, skipQuest = true }, res =>
            {
                if (res == null || !res.Success) return;
                string tag = res.MessageEn ?? "";
                if (tag == "quest") return;
                var u = tag == "stay" ? StayLine(r) : ResidentLines.GestureResponse(r, e.Gesture, e.PlayerId, this, null, out _);
                if (u != null)
                {
                    r.Avatar?.LookAt(Game.LocalPlayer.Position);
                    Speak(r.Id, u, SpeechChannel.Direct);
                }
            });
        }

        // ---------------- 住民同士の交流（ホスト） ----------------

        private void SocialTick(int minutes)
        {
            socialAccumulator += minutes;
            if (socialAccumulator < 5) return;
            socialAccumulator = 0;
            var cands = new List<Resident>();
            foreach (var r in ordered)
            {
                var st = r.State;
                if (!(st.active || r.IsVisitingFormer) || st.hidden || !st.arrived || st.hasGoTo || st.cutsceneHold || st.holdUntil > Now) continue;
                if (!string.IsNullOrEmpty(st.chatPartnerId) || !string.IsNullOrEmpty(st.followPlayerId)) continue;
                switch (st.activity)
                {
                    case ResidentActivity.AtPlaza:
                    case ResidentActivity.Walking:
                    case ResidentActivity.Visiting:
                    case ResidentActivity.ShelteringFromRain:
                    case ResidentActivity.Resting:
                    case ResidentActivity.Playing:
                    case ResidentActivity.Celebrating:
                    case ResidentActivity.Exploring:
                    case ResidentActivity.WatchingSky:
                    case ResidentActivity.Eating:
                    case ResidentActivity.AtHome:
                        cands.Add(r);
                        break;
                }
            }
            var rng = SeededRandom.For(seed, "social." + Now);
            for (int i = 0; i < cands.Count; i++)
            {
                var a = cands[i];
                if (!string.IsNullOrEmpty(a.State.chatPartnerId)) continue;
                for (int j = i + 1; j < cands.Count; j++)
                {
                    var b = cands[j];
                    if (!string.IsNullOrEmpty(b.State.chatPartnerId)) continue;
                    if (ResidentUtil.FlatDistance(a.Position, b.Position) > 8f) continue;
                    var rel = a.FindRelation(b.Id);
                    if (rel != null && rel.quarrelUntilDay >= Today)
                    {
                        // 喧嘩中：時間が経っていれば仲直り、そうでなければ距離を置く（§5）
                        if (Today - (rel.quarrelUntilDay - 3) >= 1 && rng.Chance(0.3f + (a.P.generosity + b.P.generosity) * 0.15f)) MakeUp(a, b);
                        else
                        {
                            var leaver = a.P.sociability < b.P.sociability ? a : b;
                            if (leaver.State.activity == ResidentActivity.AtPlaza || leaver.State.activity == ResidentActivity.Walking)
                            {
                                leaver.State.activity = ResidentActivity.Walking;
                                SetDestination(leaver, ResolveDestination(leaver, new ScheduleBlock { destKind = "roam" }));
                            }
                            continue;
                        }
                    }
                    float aff = rel != null ? rel.affinity : 0f;
                    float chance = 0.18f + (a.P.sociability + b.P.sociability) * 0.15f + aff / 250f - (a.P.shyness + b.P.shyness) * 0.08f;
                    if (a.State.activity == ResidentActivity.ShelteringFromRain && b.State.activity == ResidentActivity.ShelteringFromRain) chance += 0.3f; // 同じ軒下（時間・天候 §10）
                    if (!rng.Chance(Mathf.Clamp01(chance))) continue;
                    StartChat(a, b, rng.Range(10, 26), rng);
                    break;
                }
            }
        }

        internal void StartChat(Resident a, Resident b, int minutes, SeededRandom rng)
        {
            a.State.chatPartnerId = b.Id;
            b.State.chatPartnerId = a.Id;
            a.State.activity = ResidentActivity.Chatting;
            b.State.activity = ResidentActivity.Chatting;
            chatEnds[a.Id] = Now + minutes;
            chatEnds[b.Id] = Now + minutes;
            a.hasTarget = false; a.path.Clear();
            b.hasTarget = false; b.path.Clear();
            a.chatTurn = 0; b.chatTurn = 0;
            a.nextChatLineTime = UnityEngine.Time.time + 1f;
            ApplyChatEffects(a, b, rng);
            SyncAvatar(a);
            SyncAvatar(b);
            MarkDirty();
        }

        internal void EndChat(Resident r)
        {
            var other = GetResident(r.State.chatPartnerId);
            r.State.chatPartnerId = null;
            chatEnds.Remove(r.Id);
            r.State.blockIndex = -1;
            if (other != null && other.State.chatPartnerId == r.Id)
            {
                other.State.chatPartnerId = null;
                chatEnds.Remove(other.Id);
                other.State.blockIndex = -1;
            }
        }

        private static float Compat(Resident a, Resident b)
        {
            var p = a.P;
            var q = b.P;
            float diff = (Mathf.Abs(p.sociability - q.sociability) + Mathf.Abs(p.easygoing - q.easygoing) + Mathf.Abs(p.curiosity - q.curiosity) + Mathf.Abs(p.tidiness - q.tidiness)) / 4f;
            float c = 1f - diff * 2.4f;
            if (a.Profile.hobby == b.Profile.hobby || a.Profile.hobby == b.Profile.hobby2 || a.Profile.hobby2 == b.Profile.hobby) c += 0.35f;
            return Mathf.Clamp(c, -1f, 1f);
        }

        /// <summary>立ち話の結果（関係の変化・噂の伝わり・約束・喧嘩・贈り物）。</summary>
        private void ApplyChatEffects(Resident a, Resident b, SeededRandom rng)
        {
            int day = Today;
            var ab = a.Relation(b.Id);
            var ba = b.Relation(a.Id);
            if (ab.compatibility == 0f) ab.compatibility = Compat(a, b);
            if (ba.compatibility == 0f) ba.compatibility = Compat(b, a);
            ab.affinity = Mathf.Clamp(ab.affinity + 1f + ab.compatibility * 3f, -100f, 100f);
            ba.affinity = Mathf.Clamp(ba.affinity + 1f + ba.compatibility * 3f, -100f, 100f);
            ab.timesTogether++; ba.timesTogether++;
            ab.lastMetDay = day; ba.lastMetDay = day;
            if (ab.kind == RelationKind.Acquaintance && ab.affinity > 35f) ab.kind = RelationKind.Friend;
            if (ba.kind == RelationKind.Acquaintance && ba.affinity > 35f) ba.kind = RelationKind.Friend;
            a.State.social = Mathf.Max(0f, a.State.social - 0.3f);
            b.State.social = Mathf.Max(0f, b.State.social - 0.3f);

            // 噂：見たことを話す → 相手は「聞いた」記憶を持つ（§7）
            SpreadGossip(a, b);
            SpreadGossip(b, a);
            // 予報を知っている住民から聞く
            if (a.State.forecastKnownDay == day) b.State.forecastKnownDay = day;
            else if (b.State.forecastKnownDay == day) a.State.forecastKnownDay = day;

            // 喧嘩（相性が悪い・ライバル同士）
            bool rivals = ab.kind == RelationKind.Rival || a.P.competitive > 0.7f && b.P.competitive > 0.7f;
            float quarrel = (ab.compatibility < -0.2f ? 0.06f : 0.01f) + (rivals ? 0.05f : 0f) + a.P.mischief * 0.02f;
            if (ab.kind != RelationKind.Family && rng.Chance(quarrel))
            {
                int until = day + rng.Range(2, 5);
                ab.quarrelUntilDay = until; ba.quarrelUntilDay = until;
                ab.affinity -= 15f; ba.affinity -= 15f;
                a.AddMemory(new ResidentMemoryEntry { kind = "quarrel", otherResidentId = b.Id, day = day, importance = 0.7f });
                b.AddMemory(new ResidentMemoryEntry { kind = "quarrel", otherResidentId = a.Id, day = day, importance = 0.7f });
                chatEnds[a.Id] = Now + 4;
                chatEnds[b.Id] = Now + 4;
                return;
            }

            // 約束（趣味が同じで仲が良い → 明日一緒に）
            if (ab.compatibility > 0.2f && ab.affinity > 30f && a.State.promises.Count == 0 && b.State.promises.Count == 0 && rng.Chance(0.18f))
            {
                var hobby = a.Profile.hobby == b.Profile.hobby || a.Profile.hobby == b.Profile.hobby2 ? a.Profile.hobby : b.Profile.hobby;
                CreatePromise(a, b, day + 1, JobTable.HobbyActivity(hobby), rng);
            }

            // 贈り物を渡す（買っておいた贈り物、気前のいい住民の食べ物・花）
            DeliverGifts(a, b);
            DeliverGifts(b, a);
            if (a.P.generosity > 0.7f && ab.affinity > 40f && rng.Chance(0.12f)) ShareSomething(a, b);
        }

        private void CreatePromise(Resident a, Resident b, int day, ResidentActivity act, SeededRandom rng)
        {
            bool rest = IsRestDayOn(day);
            int start = rest ? 10 * 60 : Mathf.Max(15 * 60, Mathf.Max(a.Profile.workEndMinute, b.Profile.workEndMinute) + 15);
            if (start > 19 * 60) start = 10 * 60;
            string place = null;
            if (act == ResidentActivity.Fishing) place = PickFishingSpot(a, rng);
            else if (act == ResidentActivity.WatchingSky) place = Layout?.GetFacility(FacilityType.Lookout)?.id;
            else place = Layout?.GetFacility(FacilityType.Plaza)?.id;
            if (act == ResidentActivity.AtHome || act == ResidentActivity.Resting || act == ResidentActivity.Crafting || act == ResidentActivity.Cooking) act = ResidentActivity.Playing;
            a.State.promises.Add(new Promise { partnerId = b.Id, day = day, activity = act, placeId = place, start = start, end = start + 90 });
            b.State.promises.Add(new Promise { partnerId = a.Id, day = day, activity = act, placeId = place, start = start, end = start + 90 });
            // 翌日の予定がもう作られていれば反映
            if (a.State.planDay == day) ApplyPromises(a, a.State, day);
            if (b.State.planDay == day) ApplyPromises(b, b.State, day);
        }

        private void SpreadGossip(Resident from, Resident to)
        {
            int day = Today;
            for (int i = from.State.memories.Count - 1; i >= 0; i--)
            {
                var m = from.State.memories[i];
                if (string.IsNullOrEmpty(m.playerId) || day - m.day > 5) continue;
                if (!m.kind.StartsWith("saw_")) continue;
                string kind = "heard_" + m.kind.Substring(4);
                if (kind == "heard_rare_fish" || kind == "heard_big_crop") kind = kind == "heard_rare_fish" ? "heard_fishing" : "heard_harvest";
                if (to.HasMemory(kind, m.playerId, day) || to.HasMemory(m.kind, m.playerId, m.day)) continue;
                to.AddMemory(new ResidentMemoryEntry { kind = kind, playerId = m.playerId, itemId = m.itemId, day = day, importance = 0.3f, otherResidentId = from.Id });
                return;
            }
        }

        private void DeliverGifts(Resident giver, Resident receiver)
        {
            string tag = GiftTag + receiver.Id;
            for (int i = giver.State.items.Count - 1; i >= 0; i--)
            {
                var s = giver.State.items[i];
                if (s == null || s.IsEmpty || s.data != tag) continue;
                giver.State.items.RemoveAt(i);
                var gift = new ItemStack(s.itemId, s.count, s.quality);
                receiver.AddItem(gift);
                HandleReceivedItem(receiver, gift, "resident_gift");
                float v = receiver.EvaluateItem(gift.Definition);
                receiver.Relation(giver.Id).affinity += 6f + v * 6f;
                giver.Relation(receiver.Id).affinity += 3f;
                receiver.AddMemory(new ResidentMemoryEntry { kind = "received_gift", otherResidentId = giver.Id, itemId = gift.itemId, day = Today, importance = 0.7f });
                giver.AddMemory(new ResidentMemoryEntry { kind = "gave_gift", otherResidentId = receiver.Id, itemId = gift.itemId, day = Today, importance = 0.5f });
                receiver.State.mood = Mathf.Clamp(receiver.State.mood + 0.2f + v * 0.2f, -1f, 1f);
                if (giver.Avatar != null) giver.Avatar.PlayGesture(new GestureCue(Gesture.OfferItem, gift.itemId));
                if (receiver.Avatar != null) receiver.Avatar.ShowExpression(v > 0.2f ? Expression.VeryHappy : Expression.Happy, 3f);
                // 主人公が近くで見ていれば、渡す言葉が聞こえる（青い花が村の中を移動する例：§4）
                if (LocalPlayerNear(giver.Position, ResidentTuning.HearChatRadius))
                {
                    var u = new Utterance(giver.Id, "chat.gift").Related(gift.itemId).Related(receiver.Id);
                    u.Say(ResidentLines.F(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(ResidentLines.ItemArg(gift.itemId)).Recipient(Concepts.YOU));
                    if (receiver.IsBirthday(Today)) u.Say(ResidentLines.F(SpeechAct.Exclamation, Concepts.CONGRATS));
                    u.Face(Expression.Happy).With(Gesture.OfferItem, gift.itemId);
                    Speak(giver.Id, u, SpeechChannel.Overheard);
                }
            }
        }

        private void ShareSomething(Resident giver, Resident receiver)
        {
            ItemStack item = giver.FindItem(d => Resident.IsEdible(d) || d.HasTag(ItemTags.Flower));
            if (item == null || !string.IsNullOrEmpty(item.data)) return;
            string id = item.itemId;
            giver.RemoveItem(id, 1);
            var gift = new ItemStack(id, 1);
            receiver.AddItem(gift);
            HandleReceivedItem(receiver, gift, "resident_share");
            receiver.Relation(giver.Id).affinity += 4f;
            receiver.AddMemory(new ResidentMemoryEntry { kind = "received_gift", otherResidentId = giver.Id, itemId = id, day = Today, importance = 0.5f });
            if (giver.Avatar != null) giver.Avatar.PlayGesture(new GestureCue(Gesture.OfferItem, id));
        }

        private void MakeUp(Resident a, Resident b)
        {
            var ab = a.Relation(b.Id);
            var ba = b.Relation(a.Id);
            ab.quarrelUntilDay = -1; ba.quarrelUntilDay = -1;
            ab.affinity += 8f; ba.affinity += 8f;
            ab.trust += 3f; ba.trust += 3f;
            a.AddMemory(new ResidentMemoryEntry { kind = "made_up", otherResidentId = b.Id, day = Today, importance = 0.6f });
            b.AddMemory(new ResidentMemoryEntry { kind = "made_up", otherResidentId = a.Id, day = Today, importance = 0.6f });
            StartChat(a, b, 12, SeededRandom.For(seed, "makeup." + Now));
        }

        private void ArriveVisit(Resident r, ScheduleBlock b)
        {
            if (b.destKind == "player_home")
            {
                // 主人公の家に遊びに来る（§6 親交による反応の変化）
                Remember(r, "visited_player_home", b.destId, null, 0.5f);
                if (LocalPlayerNear(r.Position, 15f) && Game.LocalPlayer.PlayerId == b.destId)
                {
                    var u = new Utterance(r.Id, "visit.player_home");
                    u.Say(ResidentLines.F(SpeechAct.Greeting, Concepts.HELLO)).Say(ResidentLines.F(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)));
                    u.Face(Expression.Happy).With(Gesture.Wave);
                    Speak(r.Id, u, SpeechChannel.Direct);
                }
                return;
            }
            var host = GetResident(b.destId);
            if (host == null || !host.State.active) return;
            bool home = ResidentUtil.FlatDistance(host.Position, HomeYard(host)) < 9f && string.IsNullOrEmpty(host.State.chatPartnerId);
            if (!home)
            {
                r.AddMemory(new ResidentMemoryEntry { kind = "visited_absent", otherResidentId = host.Id, day = Today, importance = 0.2f });
                return;
            }
            if (host.State.hidden) { host.State.hidden = false; SyncAvatar(host); }
            int left = Mathf.Max(15, b.end - MinuteOfDay);
            StartChat(r, host, Mathf.Min(left, 60), SeededRandom.For(seed, "visit." + r.Id + "." + Now));
            r.AddMemory(new ResidentMemoryEntry { kind = "visited", otherResidentId = host.Id, day = Today, importance = 0.4f });
            if (host.IsBirthday(Today))
            {
                host.State.mood = Mathf.Min(1f, host.State.mood + 0.15f);
                host.AddMemory(new ResidentMemoryEntry { kind = "birthday_guest", otherResidentId = r.Id, day = Today, importance = 0.6f });
            }
            // 家に飾ってある物を見て話題にする（§16：花を見た友達も欲しくなる）
            if (host.State.homeItems.Count > 0)
            {
                string item = host.State.homeItems[host.State.homeItems.Count - 1];
                var def = ItemRegistry.Get(item);
                if (def != null && r.EvaluateItem(def) > 0.3f && r.State.midGoal.kind != GoalKind.DecorateHome && r.State.midGoal.kind != GoalKind.BuyFurniture)
                {
                    r.State.midGoal = new GoalState { kind = def.Category == ItemCategory.Furniture ? GoalKind.BuyFurniture : GoalKind.DecorateHome, tag = Concepts.ColorFromTags(def) != null ? FirstColorTag(def) : null, target = 1f, startedDay = Today };
                    r.AddMemory(new ResidentMemoryEntry { kind = "saw_friend_item", otherResidentId = host.Id, itemId = item, day = Today, importance = 0.5f });
                }
            }
        }

        private static string FirstColorTag(ItemDefinition d)
        {
            foreach (var t in d.Tags) if (t.StartsWith("color_")) return t;
            return null;
        }

        private void HourlySocial(int day, int hour)
        {
            // 人恋しさは少しずつ増える
            foreach (var r in ordered)
            {
                if (!r.State.active) continue;
                r.State.social = Mathf.Clamp01(r.State.social + 0.03f * r.P.sociability);
            }
        }

        // ---------------- 聞こえる会話（ローカル。ゲストも同期された状態から作る） ----------------

        private bool LocalPlayerNear(Vector3 pos, float radius)
        {
            var p = Game.LocalPlayer;
            if (p == null || p.CurrentAreaId != AreaIds.Overworld) return false;
            return ResidentUtil.FlatDistance(p.Position, pos) <= radius;
        }

        private void TickLocalSpeech()
        {
            var player = Game.LocalPlayer;
            if (player == null || player.CurrentAreaId != AreaIds.Overworld || Game.State != GameState.Playing) return;
            var ppos = player.Position;
            float now = UnityEngine.Time.time;
            foreach (var r in ordered)
            {
                if (r.Avatar == null || !r.Avatar.IsVisible) continue;
                if (r.Id == conversationResidentId || r.State.cutsceneHold) continue;
                float dist = ResidentUtil.FlatDistance(r.Avatar.transform.position, ppos);
                var st = r.State;

                // ---- 住民同士の会話 ----
                if (st.activity == ResidentActivity.Chatting && !string.IsNullOrEmpty(st.chatPartnerId))
                {
                    var o = GetResident(st.chatPartnerId);
                    if (o == null || o.State.chatPartnerId != r.Id || string.CompareOrdinal(r.Id, o.Id) > 0) continue;
                    if (dist > ResidentTuning.HearChatRadius || o.Id == conversationResidentId) continue;
                    if (now < r.nextChatLineTime) continue;
                    var speaker = (r.chatTurn / 2) % 2 == 0 ? r : o;
                    var listener = speaker == r ? o : r;
                    if (r.chatTurn % 2 == 0)
                    {
                        var u = ResidentLines.ChatOpening(speaker, listener, this, fxRng, out var topic);
                        r.chatTopicKey = topic;
                        Speak(speaker.Id, u, SpeechChannel.Overheard);
                        speaker.Avatar?.LookAt(listener.Position + Vector3.up);
                        r.nextChatLineTime = now + 2.8f;
                    }
                    else
                    {
                        var u = ResidentLines.ChatReply(listener, speaker, r.chatTopicKey, this, fxRng);
                        Speak(listener.Id, u, SpeechChannel.Overheard);
                        listener.Avatar?.LookAt(speaker.Position + Vector3.up);
                        r.nextChatLineTime = now + fxRng.Range(ResidentTuning.ChatLineIntervalMin, ResidentTuning.ChatLineIntervalMax);
                    }
                    r.chatTurn++;
                    continue;
                }

                // ---- 遠くから手を振る（親しい住民） ----
                int fr = r.GetFriendship(player.PlayerId);
                if (fr >= ResidentTuning.WaveFromAfarFriendship && dist > 6f && dist < 16f && now - r.lastWaveTime > ResidentTuning.WaveCooldown
                    && st.activity != ResidentActivity.Sleeping && string.IsNullOrEmpty(st.chatPartnerId))
                {
                    r.lastWaveTime = now;
                    r.Avatar.LookAt(ppos + Vector3.up);
                    Speak(r.Id, ResidentLines.WaveFromAfar(r, player.PlayerId), SpeechChannel.Direct);
                    continue;
                }

                // ---- 独り言 ----
                if (dist < ResidentTuning.SelfTalkRadius && now >= r.nextSelfTalkTime)
                {
                    r.nextSelfTalkTime = now + ResidentTuning.SelfTalkInterval * fxRng.Range(0.7f, 1.4f);
                    if (fxRng.Chance(0.45f + r.P.talkativeness * 0.3f))
                    {
                        var u = ResidentLines.SelfTalk(r, this, fxRng);
                        if (u != null && u.sentences.Count > 0) Speak(r.Id, u, SpeechChannel.Overheard);
                    }
                }
            }
        }

        // ---------------- 日々の変化 ----------------

        private void DailyMaintenance(int day)
        {
            foreach (var r in ordered)
            {
                var st = r.State;
                if (!st.active) continue;
                // 古い記憶を忘れる（重要な記憶・主人公との思い出は残りやすい）
                st.memories.RemoveAll(m => day - m.day > ResidentTuning.MemoryForgetDays && m.importance < 0.7f);
                foreach (var rel in st.relations)
                {
                    // 喧嘩の期間が過ぎた：時間が解決することもある
                    if (rel.quarrelUntilDay == day - 1)
                    {
                        rel.quarrelUntilDay = -1;
                        if (StableHash.Unit(seed, "makeup." + r.Id + rel.otherId + day) < 0.5f)
                        {
                            rel.affinity += 5f;
                            st.memories.Add(new ResidentMemoryEntry { kind = "made_up", otherResidentId = rel.otherId, day = day, importance = 0.5f });
                        }
                    }
                    // 親しさはゆっくり相性に近づく
                    float target = rel.compatibility * 25f + (rel.kind == RelationKind.Family ? 40f : 0f);
                    rel.affinity = Mathf.MoveTowards(rel.affinity, Mathf.Max(target, rel.affinity - 1f), 0.4f);
                    rel.affinity = Mathf.Clamp(rel.affinity, -100f, 100f);
                    if (rel.affinity > 60f && rel.kind == RelationKind.Friend) rel.kind = RelationKind.BestFriend;
                }
                foreach (var b in st.bonds) if (b.lastGiftDay != day) b.giftsToday = 0;
                st.mood = Mathf.MoveTowards(st.mood, 0.1f, 0.1f);
                st.hunger = Mathf.Clamp01(st.hunger);

                // 目標の更新（§3：達成したら次の目標が生まれる）
                var g = st.midGoal;
                if (g.kind == GoalKind.SaveMoney) g.progress = st.money;
                if (g.kind == GoalKind.LearnSkill && st.lastWorkDay == day - 1) g.progress += 1f;
                if (g.kind == GoalKind.BigFish && st.fishingDays > 0 && st.lastCaughtDay == day - 1) g.progress += 0.3f;
                if (g.IsDone)
                {
                    st.memories.Add(new ResidentMemoryEntry { kind = "achieved", detail = g.kind.ToString(), day = day, importance = 0.85f });
                    st.mood = Mathf.Min(1f, st.mood + 0.4f);
                    if (g.kind == GoalKind.Masterpiece) st.longGoal.progress += 8f;
                    if (g.kind == GoalKind.NewRecipe && st.job == ResidentJob.Cook) st.longGoal.progress += 4f;
                    var next = NewMidGoal(r, SeededRandom.For(seed, "goal." + r.Id + "." + day), g.kind);
                    next.completedCount = g.completedCount + 1;
                    st.midGoal = next;
                }
                DreamProgress(r, day);
            }
            foreach (var rep in meta.reputation)
            {
                if (rep.lastMineDay < day - 1) rep.mineStreak = 0;
                if (rep.shopSalesRecentDay < day - 3) rep.shopSalesRecent = 0;
            }
        }

        private void DreamProgress(Resident r, int day)
        {
            var lg = r.State.longGoal;
            switch (lg.kind)
            {
                case GoalKind.TravelStars: lg.progress += 0.6f + r.P.curiosity * 0.6f; break;
                case GoalKind.BuildSpaceship: lg.progress += 0.6f; break;
                case GoalKind.OwnShop: lg.progress += r.State.money > 1500 ? 1.4f : 0.4f; break;
                case GoalKind.BestCraftsman: lg.progress += r.State.lastWorkDay == day - 1 ? 0.9f : 0.2f; break;
                case GoalKind.FamilyReunion: lg.progress += 0.7f; break;
            }
        }

        private static float DreamTarget(GoalKind dream)
        {
            switch (dream)
            {
                case GoalKind.TravelStars: return 30f;
                case GoalKind.BuildSpaceship: return 50f;
                case GoalKind.OwnShop: return 40f;
                case GoalKind.BestCraftsman: return 40f;
                case GoalKind.FamilyReunion: return 28f;
                default: return 99999f;
            }
        }

        // ---------------- 初期化 ----------------

        private static int CoworkerGroup(ResidentJob j)
        {
            switch (j)
            {
                case ResidentJob.Blacksmith: case ResidentJob.Miner: return 1;
                case ResidentJob.Carpenter: case ResidentJob.Builder: return 2;
                case ResidentJob.SeedSeller: case ResidentJob.FarmingMaster: case ResidentJob.Florist: return 3;
                case ResidentJob.FishingMaster: case ResidentJob.Cook: return 4;
                case ResidentJob.Merchant: case ResidentJob.Courier: return 5;
                case ResidentJob.Mechanic: case ResidentJob.Scholar: return 6;
                case ResidentJob.Musician: case ResidentJob.Artist: case ResidentJob.Teacher: return 7;
                default: return 0;
            }
        }

        /// <summary>初期の人間関係（家族・仕事仲間・ライバル・友達・苦手）。村仕様：初期の人間関係もシードで変わる。</summary>
        private void InitRelations(List<Resident> list, SeededRandom rng)
        {
            for (int i = 0; i < list.Count; i++)
            {
                for (int j = i + 1; j < list.Count; j++)
                {
                    var a = list[i];
                    var b = list[j];
                    float compat = Compat(a, b);
                    float aff = compat * 30f + rng.Range(-10f, 10f);
                    var kind = RelationKind.Acquaintance;
                    if (a.Profile.familyId == b.Id || b.Profile.familyId == a.Id) { kind = RelationKind.Family; aff = rng.Range(60f, 80f); }
                    else if (CoworkerGroup(a.State.job) != 0 && CoworkerGroup(a.State.job) == CoworkerGroup(b.State.job)) { kind = RelationKind.Coworker; aff += 10f; }
                    else if ((a.Profile.hobby == b.Profile.hobby) && a.P.competitive > 0.6f && b.P.competitive > 0.6f) { kind = RelationKind.Rival; aff = rng.Range(10f, 25f); }
                    else if (compat < -0.45f) { kind = RelationKind.Dislike; aff = rng.Range(-25f, -10f); }
                    // プロローグで一緒に来た仲間（案内役・修理屋・料理人）
                    if ((a.IsGuide || b.IsGuide) && (a.State.job == ResidentJob.Mechanic || b.State.job == ResidentJob.Mechanic || a.State.job == ResidentJob.Cook || b.State.job == ResidentJob.Cook))
                    {
                        kind = RelationKind.Friend;
                        aff = Mathf.Max(aff, 50f);
                    }
                    a.State.relations.Add(new RelationEntry { otherId = b.Id, kind = kind, affinity = aff + rng.Range(-4f, 4f), compatibility = compat, trust = Mathf.Max(0f, aff * 0.6f) });
                    b.State.relations.Add(new RelationEntry { otherId = a.Id, kind = kind, affinity = aff + rng.Range(-4f, 4f), compatibility = compat, trust = Mathf.Max(0f, aff * 0.6f) });
                }
            }
            foreach (var r in list)
            {
                var sorted = new List<RelationEntry>(r.State.relations);
                sorted.Sort((x, y) => y.affinity.CompareTo(x.affinity));
                for (int k = 0; k < sorted.Count && k < 2; k++)
                {
                    var rel = sorted[k];
                    if (rel.kind != RelationKind.Acquaintance) continue;
                    if (rel.affinity > 55f) rel.kind = RelationKind.BestFriend;
                    else if (rel.affinity > 28f) rel.kind = RelationKind.Friend;
                }
            }
            // 恋は控えめに：大人同士で特に仲の良い1組だけ（恋愛・結婚は【未決定】のため、贈り物が増える程度）
            if (rng.Chance(0.4f))
            {
                foreach (var a in list)
                {
                    if (a.Profile.age != ResidentAge.Adult) continue;
                    foreach (var rel in a.State.relations)
                    {
                        var b = GetResident(rel.otherId);
                        if (b == null || b.Profile.age != ResidentAge.Adult || rel.kind == RelationKind.Family || rel.affinity < 45f) continue;
                        rel.kind = RelationKind.Sweetheart;
                        var back = b.Relation(a.Id);
                        back.kind = RelationKind.Sweetheart;
                        back.affinity = Mathf.Max(back.affinity, 50f);
                        return;
                    }
                }
            }
        }

        private void InitGoals(Resident r, SeededRandom rng)
        {
            r.State.midGoal = NewMidGoal(r, rng, GoalKind.None);
            var dream = r.Profile.dream;
            float target = DreamTarget(dream);
            r.State.longGoal = new GoalState { kind = dream, target = target, progress = target < 1000f ? rng.Range(0f, target * 0.35f) : 0f, startedDay = Today };
        }

        private GoalState NewMidGoal(Resident r, SeededRandom rng, GoalKind previous)
        {
            var opts = new List<KeyValuePair<GoalKind, float>>();
            void O(GoalKind k, float w) { if (k != previous && w > 0) opts.Add(new KeyValuePair<GoalKind, float>(k, w)); }
            var job = r.State.job;
            O(GoalKind.NewRecipe, job == ResidentJob.Cook ? 6f : r.Profile.hobby == ResidentHobby.Cooking ? 3f : 0.3f);
            O(GoalKind.Masterpiece, job == ResidentJob.Blacksmith || job == ResidentJob.Carpenter ? 5f : 0f);
            O(GoalKind.BigFish, job == ResidentJob.FishingMaster ? 5f : r.Profile.hobby == ResidentHobby.Fishing ? 3f : 0f);
            O(GoalKind.DecorateHome, r.Profile.hobby == ResidentHobby.Decorating || r.Profile.hobby == ResidentHobby.Gardening ? 3f : r.P.tidiness);
            O(GoalKind.RaiseCreature, job == ResidentJob.CreatureExpert || r.Profile.hobby == ResidentHobby.CreatureWatching ? 3f : 0.2f);
            O(GoalKind.BuyFurniture, 1.2f);
            O(GoalKind.SaveMoney, 0.6f + r.P.thrift);
            O(GoalKind.LearnSkill, r.Profile.age == ResidentAge.Child || r.State.apprenticeFor != ResidentJob.None ? 3f : 0.2f);
            float total = 0f;
            foreach (var o in opts) total += o.Value;
            float pick = rng.NextFloat() * total;
            var kind = GoalKind.BuyFurniture;
            foreach (var o in opts)
            {
                pick -= o.Value;
                if (pick <= 0f) { kind = o.Key; break; }
            }
            var g = new GoalState { kind = kind, startedDay = Today };
            switch (kind)
            {
                case GoalKind.NewRecipe: g.target = rng.Range(2, 5); g.tag = ItemTags.Vegetable; break;
                case GoalKind.Masterpiece: g.target = rng.Range(4, 8); g.tag = ItemTags.Ore; break;
                case GoalKind.BigFish: g.target = rng.Range(2, 4); break;
                case GoalKind.DecorateHome: g.target = rng.Range(2, 4); g.tag = r.Profile.favoriteColorTag; break;
                case GoalKind.RaiseCreature: g.target = rng.Range(3, 6); break;
                case GoalKind.BuyFurniture: g.target = 1; g.tag = r.Profile.favoriteColorTag; break;
                case GoalKind.SaveMoney: g.target = r.State.money + rng.Range(500, 1200); g.progress = r.State.money; break;
                case GoalKind.LearnSkill: g.target = rng.Range(5, 10); break;
            }
            return g;
        }

        // ---------------- 伝言（マネピ）：天気予報・新住民の挨拶 ----------------

        /// <summary>天気読みの予報を伝言広場のマネピに預ける（時間・天候仕様 §14）。</summary>
        internal void PostForecast(Resident r)
        {
            if (r == null || meta.lastWeatherForecastDay == Today) return;
            var weather = Game.Service<IWeatherService>();
            if (weather == null) return;
            meta.lastWeatherForecastDay = Today;
            r.State.forecastKnownDay = Today;
            var u = ResidentLines.Forecast(r, weather, Today + 1);
            LeaveMessage("plaza", r, u, SpeechChannel.Broadcast);
            if (r.Avatar != null && r.Avatar.IsVisible && LocalPlayerNear(r.Position, ResidentTuning.HearChatRadius))
                Speak(r.Id, u, SpeechChannel.Broadcast);
            // 広場にいた住民も予報を聞いた
            foreach (var o in Witnesses(r.Position, 15f)) o.State.forecastKnownDay = Today;
            MarkDirty();
        }

        private void PostNewcomerHello(Resident r)
        {
            if (r.HasMemoryKindOnDay("posted_hello", Today)) return;
            r.AddMemory(new ResidentMemoryEntry { kind = "posted_hello", day = Today, importance = 0.3f });
            var u = ResidentLines.NewcomerHello(r);
            LeaveMessage("plaza", r, u, SpeechChannel.Broadcast);
            if (LocalPlayerNear(r.Position, ResidentTuning.HearChatRadius)) Speak(r.Id, u, SpeechChannel.Broadcast);
        }

        /// <summary>マネピに伝言を預ける（記録を作るだけ。再生はマネピ・タブレットが行う）。</summary>
        internal void LeaveMessage(string boardId, Resident r, Utterance u, SpeechChannel channel)
        {
            var speech = Game.Service<ISpeechService>();
            var manepi = Game.Service<IManepiService>();
            if (speech == null || manepi == null) return;
            u.speakerId = r.Id;
            try
            {
                var rec = speech.CreateRecord(u, channel, "resident", r.Position);
                if (rec != null) manepi.LeaveMessage(boardId, rec);
            }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        // ---------------- タブレット（ローカルの観察記録） ----------------

        /// <summary>住民辞典に観察できた事実を記録（内部の好みは書かない：タブレット仕様 §7.3）。1日1回・種類ごと。</summary>
        internal void RecordObservation(Resident r, string key, string ja, string en, string recordId = null)
        {
            var tablet = Game.Service<ITabletService>();
            if (tablet == null || string.IsNullOrEmpty(ja)) return;
            string k = r.Id + "|" + Today + "|" + key;
            if (!observedKeys.Add(k)) return;
            try { tablet.RecordResidentObservation(r.Id, ja, en, recordId); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>いま見えている様子（場所＋行動）。観察できる事実だけ。</summary>
        internal void ObservationOf(Resident r, out string key, out string ja, out string en)
        {
            var layout = Layout;
            string placeJa = "", placeEn = "";
            if (layout != null)
            {
                FacilityInfo near = null;
                float bd = 12f;
                foreach (var f in layout.Facilities)
                {
                    if (f.type == FacilityType.Streetlight || f.type == FacilityType.Bench || f.type == FacilityType.Well) continue;
                    float d = ResidentUtil.FlatDistance(f.position, r.Position);
                    if (d < bd) { bd = d; near = f; }
                }
                if (near != null) FacilityLabel(near, r, out placeJa, out placeEn);
                else
                {
                    var area = layout.GetAreaAt(r.Position);
                    if (area != null) { placeJa = area.nameJa + "で"; placeEn = " at " + area.nameEn; }
                }
            }
            string actJa, actEn;
            switch (r.State.activity)
            {
                case ResidentActivity.Working: actJa = "仕事をしていた"; actEn = "was working"; break;
                case ResidentActivity.Crafting: actJa = "何かを作っていた"; actEn = "was making something"; break;
                case ResidentActivity.Fishing: actJa = "釣りをしていた"; actEn = "was fishing"; break;
                case ResidentActivity.Gardening: actJa = "花の世話をしていた"; actEn = "was tending flowers"; break;
                case ResidentActivity.Farming: actJa = "畑仕事をしていた"; actEn = "was farming"; break;
                case ResidentActivity.Cooking: actJa = "料理をしていた"; actEn = "was cooking"; break;
                case ResidentActivity.Eating: actJa = "何か食べていた"; actEn = "was eating"; break;
                case ResidentActivity.WatchingSky: actJa = "空を見上げていた"; actEn = "was looking at the sky"; break;
                case ResidentActivity.Packing: actJa = r.IsMovingOut ? "荷物を箱に詰めていた（家の前に箱が並んでいた）" : "箱を運び込んでいた"; actEn = r.IsMovingOut ? "was packing boxes (boxes outside the house)" : "was carrying boxes in"; break;
                case ResidentActivity.Chatting: actJa = "誰かと立ち話をしていた"; actEn = "was chatting with someone"; break;
                case ResidentActivity.ShelteringFromRain: actJa = "雨宿りをしていた"; actEn = "was sheltering from the rain"; break;
                case ResidentActivity.Playing: actJa = "遊んでいた"; actEn = "was playing"; break;
                case ResidentActivity.CatchingCreatures: actJa = "生き物を探していた"; actEn = "was looking for creatures"; break;
                case ResidentActivity.Celebrating: actJa = "楽しそうにしていた"; actEn = "was celebrating"; break;
                case ResidentActivity.Shopping: actJa = "買い物に来ていた"; actEn = "was shopping"; break;
                case ResidentActivity.Resting: actJa = "のんびりしていた"; actEn = "was relaxing"; break;
                case ResidentActivity.Exploring: actJa = "あちこち見て回っていた"; actEn = "was looking around"; break;
                case ResidentActivity.Visiting: actJa = "誰かを訪ねていた"; actEn = "was visiting someone"; break;
                default: actJa = "歩いていた"; actEn = "was walking"; break;
            }
            key = r.State.activity + "@" + placeJa;
            ja = (string.IsNullOrEmpty(placeJa) ? "" : placeJa) + actJa;
            en = "Seen" + placeEn + ": " + actEn;
        }

        private void FacilityLabel(FacilityInfo f, Resident r, out string ja, out string en)
        {
            switch (f.type)
            {
                case FacilityType.Plaza: ja = "広場で"; en = " at the plaza"; return;
                case FacilityType.PlayerShop: ja = "主人公の商店の前で"; en = " at your shop"; return;
                case FacilityType.MessagePlaza: ja = "伝言広場で"; en = " at the message plaza"; return;
                case FacilityType.Blacksmith: ja = "鍛冶屋で"; en = " at the smithy"; return;
                case FacilityType.RepairWorkshop: ja = "修理屋で"; en = " at the repair shop"; return;
                case FacilityType.SeedShop: ja = "種屋で"; en = " at the seed shop"; return;
                case FacilityType.GeneralStore: ja = "雑貨屋で"; en = " at the general store"; return;
                case FacilityType.CarpenterWorkshop: ja = "家具工房で"; en = " at the carpenter's"; return;
                case FacilityType.FishingHut: ja = "釣り小屋の近くで"; en = " near the fishing hut"; return;
                case FacilityType.CommunityKitchen: ja = "共用調理場で"; en = " at the community kitchen"; return;
                case FacilityType.Lookout: ja = "高台で"; en = " at the lookout"; return;
                case FacilityType.MineEntrance: ja = "鉱山の入口で"; en = " at the mine entrance"; return;
                case FacilityType.RocketSite: ja = "宇宙船の近くで"; en = " near the spaceship"; return;
                case FacilityType.ResidentHouse:
                    if (f.ownerResidentId == r.Id) { ja = "自分の家の前で"; en = " in front of its house"; }
                    else { ja = "誰かの家の前で"; en = " in front of someone's house"; }
                    return;
                case FacilityType.PlayerHouse: ja = "主人公の家の前で"; en = " in front of your house"; return;
                default: ja = ""; en = ""; return;
            }
        }
    }
}
