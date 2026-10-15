using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Quests
{
    // 村の行事（§13、四季なし）：誕生日／週1回の市場の日／星の光帯の観察会／お別れ会／引っ越し祝い。
    // 行事は星で共通（マルチ仕様 §15.1）。告知はマネピの伝言広場と掲示板で、宇宙語のまま。
    public sealed partial class QuestSystem
    {
        // ------------------------------------------------------------
        // 暦（時間仕様 §15：誕生日の割り当ては【未決定】→ 住民IDから決定的に周期上の日を割り当てる）
        // ------------------------------------------------------------

        internal int BirthdayIndex(string residentId)
        {
            return StableHash.Int(seed, "birthday:" + residentId, 0, QuestTuning.BirthdayCycleDays);
        }

        /// <summary>その住民の次の誕生日までの日数（0=今日）。</summary>
        internal int DaysUntilBirthday(string residentId, int day)
        {
            int cycle = QuestTuning.BirthdayCycleDays;
            int today = (day - 1) % cycle;
            int b = BirthdayIndex(residentId);
            return ((b - today) % cycle + cycle) % cycle;
        }

        internal int MarketDayOfWeek()
        {
            int week = Clock != null ? Mathf.Max(1, Clock.WeekLength) : 7;
            return StableHash.Int(seed, "quests.market.dow", 0, week);
        }

        private bool IsMarketDay(int day)
        {
            int week = Clock != null ? Mathf.Max(1, Clock.WeekLength) : 7;
            return (day - 1) % week == MarketDayOfWeek();
        }

        private VillageEvent FindEventOf(VillageEventKind kind, string host, int day)
        {
            foreach (var e in S.events)
                if (e.kind == kind && e.day == day && (host == null || e.hostResidentId == host)) return e;
            return null;
        }

        internal VillageEvent CreateEvent(VillageEventKind kind, string host, int day, int start, int end, Vector3 pos)
        {
            var e = FindEventOf(kind, host, day);
            if (e != null) return e;
            e = new VillageEvent
            {
                id = NewId("ev"),
                kind = kind,
                hostResidentId = host,
                day = day,
                startMinute = start,
                endMinute = end,
                position = Ground(pos)
            };
            S.events.Add(e);
            return e;
        }

        private void ScheduleDailyEvents(int day)
        {
            var residents = ActiveResidents();
            foreach (var r in residents)
            {
                int d = DaysUntilBirthday(r.Id, day);
                if (d == 0) CreateEvent(VillageEventKind.Birthday, r.Id, day, QuestTuning.PartyStartMinute, QuestTuning.PartyEndMinute, HomeOf(r));
                else if (d == 1)
                {
                    var ev = CreateEvent(VillageEventKind.Birthday, r.Id, day + 1, QuestTuning.PartyStartMinute, QuestTuning.PartyEndMinute, HomeOf(r));
                    Announce(ev, day);
                }
            }
            if (IsMarketDay(day)) CreateMarket(day);
            if (IsMarketDay(day + 1))
            {
                var m = CreateMarket(day + 1);
                Announce(m, day);
            }
            foreach (var e in S.events.ToArray()) if (e.day == day && !e.announced) Announce(e, day);
        }

        /// <summary>告知：マネピの伝言広場に宇宙語の伝言を預け、掲示板にお知らせの紙を貼る（タブレット仕様 §9.1）。</summary>
        internal void Announce(VillageEvent ev, int today)
        {
            if (ev == null || ev.announced) return;
            ev.announced = true;
            string speaker = ev.hostResidentId;
            if (ev.kind == VillageEventKind.MarketDay || ev.kind == VillageEventKind.StarBandViewing || ev.kind == VillageEventKind.FarewellParty || ev.kind == VillageEventKind.WelcomeParty || ev.kind == VillageEventKind.Birthday)
            {
                // 伝言係がいれば伝言係が預ける（誕生日・お別れは本人ではなく友達が知らせる）
                var courier = Dir?.FindByJob(ResidentJob.Courier);
                if (courier != null && courier.IsActive && courier.Id != ev.hostResidentId) speaker = courier.Id;
                else if (ev.kind == VillageEventKind.Birthday || ev.kind == VillageEventKind.FarewellParty)
                {
                    foreach (var r in ActiveResidents()) if (r.Id != ev.hostResidentId) { speaker = r.Id; break; }
                }
            }
            if (string.IsNullOrEmpty(speaker))
            {
                var list = ActiveResidents();
                if (list.Count == 0) return;
                speaker = list[0].Id;
            }
            var u = QuestSpeech.EventAnnounce(ev, speaker, today);
            var speech = Speech;
            var manepi = Game.Service<IManepiService>();
            if (speech != null && manepi != null)
            {
                var rec = speech.CreateRecord(u, SpeechChannel.Broadcast, "resident", PlazaPos());
                if (rec != null) manepi.LeaveMessage("plaza", rec);
            }
            S.board.Add(new BoardPaper
            {
                id = NewId("bn"),
                residentId = speaker,
                isNotice = true,
                noticeEventId = ev.id,
                postedDay = today,
                expireDay = ev.day,
                template = new QuestInstance { residentId = speaker }
            });
        }

        // ------------------------------------------------------------
        // 市場の日（週1回）
        // ------------------------------------------------------------

        private VillageEvent CreateMarket(int day)
        {
            var existing = FindEventOf(VillageEventKind.MarketDay, null, day);
            if (existing != null) return existing;
            var plaza = PlazaPos();
            var ev = CreateEvent(VillageEventKind.MarketDay, null, day, QuestTuning.MarketStartMinute, QuestTuning.MarketEndMinute, plaza);
            var rng = Rng("quests.market." + day);
            var vendors = new List<IResidentInfo>();
            var order = new[] { ResidentJob.Merchant, ResidentJob.SeedSeller, ResidentJob.FarmingMaster, ResidentJob.Cook, ResidentJob.FishingMaster, ResidentJob.Carpenter, ResidentJob.Artist, ResidentJob.CreatureExpert, ResidentJob.Miner };
            var all = ActiveResidents();
            foreach (var job in order)
            {
                if (vendors.Count >= QuestTuning.MarketMaxStalls) break;
                foreach (var r in all) if (r.Job == job && !vendors.Contains(r)) { vendors.Add(r); break; }
            }
            int i = 0;
            foreach (var v in vendors)
            {
                var pool = MarketPool(v.Job);
                if (pool.Count == 0) continue;
                rng.Shuffle(pool);
                var stall = new MarketStall { residentId = v.Id };
                float ang = (i / (float)Mathf.Max(1, vendors.Count)) * Mathf.PI * 2f;
                stall.position = Ground(plaza + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 7f);
                for (int k = 0; k < pool.Count && stall.itemIds.Count < QuestTuning.MarketItemsPerStall; k++)
                {
                    var def = ItemRegistry.Get(pool[k]);
                    if (def == null || def.BasePrice <= 0) continue;
                    stall.itemIds.Add(def.Id);
                    stall.prices.Add(Mathf.Max(1, Mathf.RoundToInt(def.BasePrice * QuestTuning.MarketPriceMultiplier)));
                    stall.stock.Add(QuestTuning.MarketStockPerItem);
                }
                if (stall.itemIds.Count == 0) continue;
                ev.stalls.Add(stall);
                ev.gatherers.Add(v.Id);
                i++;
            }
            if (ev.stalls.Count == 0) ev.cancelled = true;
            return ev;
        }

        private static List<string> MarketPool(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.SeedSeller: return QuestCatalog.ByCategory(ItemCategory.Seed);
                case ResidentJob.FarmingMaster: return QuestCatalog.Crops();
                case ResidentJob.Cook: return QuestCatalog.Dishes();
                case ResidentJob.FishingMaster: { var l = QuestCatalog.ByCategory(ItemCategory.Bait); if (l.Count == 0) l = QuestCatalog.Fish(); return l; }
                case ResidentJob.Carpenter: { var l = QuestCatalog.ByCategory(ItemCategory.Furniture); l.AddRange(QuestCatalog.ByCategory(ItemCategory.Decor)); return l; }
                case ResidentJob.Artist: return QuestCatalog.ByCategory(ItemCategory.Decor);
                case ResidentJob.CreatureExpert: return QuestCatalog.ByCategory(ItemCategory.CreatureFood);
                case ResidentJob.Miner: return QuestCatalog.ByCategory(ItemCategory.Gem);
                default:
                {
                    var l = QuestCatalog.ByCategory(ItemCategory.Processed);
                    l.AddRange(QuestCatalog.ByCategory(ItemCategory.Fertilizer));
                    if (l.Count == 0) l = QuestCatalog.Crops();
                    return l;
                }
            }
        }

        internal void OpenMarketStall(VillageEvent ev, int stallIndex)
        {
            var p = LocalPlayer;
            if (ev == null || p == null || stallIndex < 0 || stallIndex >= ev.stalls.Count) return;
            var stall = ev.stalls[stallIndex];
            var entries = new List<VendorEntry>();
            for (int i = 0; i < stall.itemIds.Count; i++)
            {
                string itemId = stall.itemIds[i];
                int price = stall.prices[i];
                var e = new VendorEntry { ItemId = itemId, Price = price, Stock = stall.stock[i] };
                e.CustomPurchase = (pl, amount) =>
                {
                    WorldAuthority.Submit(new QuestMarketBuyCommand { eventId = ev.id, stallIndex = stallIndex, itemId = itemId, amount = amount, paid = price * amount });
                };
                entries.Add(e);
            }
            if (!string.IsNullOrEmpty(stall.residentId) && stall.itemIds.Count > 0)
                Say(stall.residentId, QuestSpeech.MarketGreeting(stall.residentId, stall.itemIds[0]));
            UIManager.Open(new VendorWindow(Loc.T("市場の屋台", "Market stall"), p, entries));
        }

        private CommandResult HandleMarketBuy(QuestMarketBuyCommand c, CommandContext ctx)
        {
            var ev = FindEvent(c.eventId);
            if (ev == null || c.stallIndex < 0 || c.stallIndex >= ev.stalls.Count || c.amount <= 0)
            {
                ctx.PayMoney(c.paid, "market_refund");
                return CommandResult.Fail("屋台はもう片付けられていました", "The stall has packed up");
            }
            var stall = ev.stalls[c.stallIndex];
            int idx = stall.itemIds.IndexOf(c.itemId);
            if (idx < 0 || stall.stock[idx] < c.amount)
            {
                ctx.PayMoney(c.paid, "market_refund");
                return CommandResult.Fail("売り切れでした（代金は返ってきた）", "Sold out (you were refunded)");
            }
            stall.stock[idx] -= c.amount;
            ctx.GiveItem(new ItemStack(c.itemId, c.amount), "market");
            Dir?.GiveMoney(stall.residentId, c.paid, "market_sale");
            EventBus.Publish(new ResidentPurchaseEvent { ResidentId = stall.residentId, ItemId = c.itemId, Price = c.paid, Where = "market_sold_to_player" });
            Memory(stall.residentId, "bought_from_me", ctx.IssuerId, c.itemId, null, 0.3f);
            MarkDirty();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 天気・引っ越しとの連携
        // ------------------------------------------------------------

        private void OnWeatherChanged(WeatherChangedEvent e)
        {
            if (!IsAuthority) return;
            if (e.Current.IsRaining && !e.Previous.IsRaining) SpawnLaundry();
            if (!e.Current.IsRaining && e.Previous.IsRaining) RemoveUnclaimedLaundry();
            if (e.Current.Has(AlienPhenomenon.StarBand) && !e.Previous.Has(AlienPhenomenon.StarBand)) ScheduleStarBand();
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnWeather(this, st, e);
            }
            Changed();
        }

        /// <summary>星の光帯が出た夜、住民が高台へ集まる（時間仕様 §12）。</summary>
        private void ScheduleStarBand()
        {
            int now = Minute;
            int start, end;
            if (now >= QuestTuning.StarBandEarliestMinute) { start = now; end = Mathf.Max(QuestTuning.StarBandEndMinute, Mathf.Min(now + 90, TimeSystem.MinutesPerDay - 1)); }
            else if (now < 4 * 60) { start = now; end = 4 * 60 + 30; }
            else { start = QuestTuning.StarBandEarliestMinute; end = QuestTuning.StarBandEndMinute; }
            var ev = CreateEvent(VillageEventKind.StarBandViewing, null, Today, start, end, HillPos());
            if (!ev.announced) Announce(ev, Today);
        }

        private void OnResidentMovedIn(ResidentMovedInEvent e)
        {
            if (!IsAuthority) return;
            var r = R(e.ResidentId);
            if (r == null) return;
            // 引っ越し当日は荷物運び（メインストーリー 第2章）
            var rng = Rng("quests.movein." + e.ResidentId);
            SpawnCarry(r, rng, "move_in");
            SpawnCarry(r, rng.Fork("2"), "move_in");
            var ev = CreateEvent(VillageEventKind.WelcomeParty, r.Id, Today + 1, QuestTuning.WelcomeStartMinute, QuestTuning.WelcomeEndMinute, PlazaPos());
            Announce(ev, Today);
            Changed();
        }

        private void OnResidentMoveOutDecided(ResidentMoveOutDecidedEvent e)
        {
            if (!IsAuthority) return;
            var r = R(e.ResidentId);
            if (r == null) return;
            int day = Mathf.Max(Today, e.PlannedDay - 1);
            var ev = CreateEvent(VillageEventKind.FarewellParty, r.Id, day, QuestTuning.PartyStartMinute, QuestTuning.PartyEndMinute, HomeOf(r));
            Announce(ev, Today);
            // 引っ越しの荷造り（メインストーリー 第4章：外に箱が並ぶ）
            SpawnCarry(r, Rng("quests.moveout." + e.ResidentId + "." + Today), "move_out", true);
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnMoveOutDecided(this, st, e.ResidentId);
            }
            Changed();
        }

        private void OnResidentMovedOut(ResidentMovedOutEvent e)
        {
            if (!IsAuthority) return;
            // 引っ越しても記録・思い出は残る（§14）。進行中の依頼は静かに終わる（罰はない）
            foreach (var q in S.quests)
            {
                if (!q.IsOpen) continue;
                if (q.residentId == e.ResidentId || (q.MainGoal != null && q.MainGoal.targetResidentId == e.ResidentId && q.kind != QuestKind.Story))
                {
                    SetStatus(q, QuestStatus.Expired);
                    q.outcome = 3;
                }
            }
            foreach (var ev in S.events) if (ev.hostResidentId == e.ResidentId && !ev.finished && ev.kind != VillageEventKind.FarewellParty) ev.cancelled = true;
            S.helps.RemoveAll(h => h.residentId == e.ResidentId && string.IsNullOrEmpty(h.claimedBy));
            S.board.RemoveAll(p => p.residentId == e.ResidentId && !p.isNotice);
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                if (st.residentId == e.ResidentId || st.partnerResidentId == e.ResidentId)
                    StoryDef(st.storyId)?.OnResidentMovedOut(this, st, e.ResidentId);
            }
            Changed();
        }

        // ------------------------------------------------------------
        // 進行（ホスト）
        // ------------------------------------------------------------

        private void HourlyEvents(int day, int hour)
        {
            // 前日に告知しそびれた物を告知
            foreach (var e in S.events.ToArray()) if (!e.announced && e.day >= day && e.day <= day + 1) Announce(e, day);
        }

        private void UpdateEvents()
        {
            int today = Today;
            int now = Minute;
            foreach (var ev in S.events.ToArray())
            {
                if (ev.cancelled || ev.finished) continue;
                if (ev.day < today) { FinishEvent(ev); continue; }
                if (ev.day != today) continue;
                if (!ev.started && now >= ev.startMinute && now < ev.endMinute) StartEvent(ev);
                else if (ev.started && now >= ev.endMinute) FinishEvent(ev);
            }
        }

        private void StartEvent(VillageEvent ev)
        {
            if (ev.kind == VillageEventKind.StarBandViewing)
            {
                var w = Game.Service<IWeatherService>();
                if (w != null && !w.Current.Has(AlienPhenomenon.StarBand)) { ev.cancelled = true; Changed(); return; }
            }
            ev.started = true;
            var all = ActiveResidents();
            var gather = new List<string>(ev.gatherers);
            switch (ev.kind)
            {
                case VillageEventKind.Birthday:
                case VillageEventKind.FarewellParty:
                {
                    // 家の近い住民（ご近所の友達）が集まる
                    if (!string.IsNullOrEmpty(ev.hostResidentId) && !gather.Contains(ev.hostResidentId)) gather.Insert(0, ev.hostResidentId);
                    var host = R(ev.hostResidentId);
                    var hostHome = HomeOf(host);
                    var neighbors = new List<IResidentInfo>(all);
                    neighbors.RemoveAll(x => x.Id == ev.hostResidentId);
                    neighbors.Sort((a, b) => Flat(HomeOf(a) - hostHome).CompareTo(Flat(HomeOf(b) - hostHome)));
                    foreach (var n in neighbors) { if (gather.Count >= QuestTuning.MaxGatherers) break; if (!gather.Contains(n.Id)) gather.Add(n.Id); }
                    break;
                }
                case VillageEventKind.StarBandViewing:
                    foreach (var r in all) if (r.Activity != ResidentActivity.Sleeping && !gather.Contains(r.Id)) gather.Add(r.Id);
                    break;
                case VillageEventKind.WelcomeParty:
                case VillageEventKind.CookParty:
                {
                    if (!string.IsNullOrEmpty(ev.hostResidentId) && !gather.Contains(ev.hostResidentId)) gather.Insert(0, ev.hostResidentId);
                    var rng = Rng("quests.gather." + ev.id);
                    var others = new List<IResidentInfo>(all);
                    rng.Shuffle(others);
                    foreach (var r in others) { if (gather.Count >= QuestTuning.MaxGatherers) break; if (!gather.Contains(r.Id)) gather.Add(r.Id); }
                    break;
                }
                case VillageEventKind.NightSky:
                    if (!string.IsNullOrEmpty(ev.hostResidentId) && !gather.Contains(ev.hostResidentId)) gather.Insert(0, ev.hostResidentId);
                    break;
            }
            ev.gatherers = gather;
            for (int i = 0; i < gather.Count; i++)
            {
                Vector3 pos;
                if (ev.kind == VillageEventKind.MarketDay)
                {
                    var stall = ev.stalls.Find(s => s.residentId == gather[i]);
                    pos = stall != null ? stall.position + new Vector3(0f, 0f, -1.2f) : ev.position;
                }
                else
                {
                    float ang = i * 2.399f; // 黄金角で輪になって集まる
                    float rad = 1.8f + 0.35f * i;
                    pos = ev.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
                }
                GoTo(gather[i], pos, ev.endMinute, "quest.event." + ev.kind);
            }
            // 誕生日：主人公と選んだ贈り物を、依頼主が渡す（§10）
            if (ev.kind == VillageEventKind.Birthday)
            {
                foreach (var q in S.quests)
                {
                    var g = q.MainGoal;
                    if (g == null || g.type != GoalType.GiftFor || g.targetResidentId != ev.hostResidentId) continue;
                    if (q.status != QuestStatus.Completed || string.IsNullOrEmpty(q.givenItemId) || q.sourceId == ev.id) continue;
                    q.sourceId = ev.id;
                    var def = ItemRegistry.Get(q.givenItemId);
                    var host = R(ev.hostResidentId);
                    float like = host != null && def != null ? host.EvaluateItem(def) : 0f;
                    Dir?.GiveItem(ev.hostResidentId, new ItemStack(q.givenItemId, 1), "birthday_gift");
                    Say(q.residentId, QuestSpeech.GiftGiven(q.residentId, q.givenItemId), SpeechChannel.Overheard);
                    Say(ev.hostResidentId, QuestSpeech.BirthdayGiftReaction(ev.hostResidentId, q.givenItemId, like), SpeechChannel.Overheard);
                    Memory(ev.hostResidentId, "birthday_gift", q.playerId, q.givenItemId, q.residentId, like >= 0.35f ? 0.8f : 0.5f, "chosen_with_player");
                    AddPending(q.playerId, q.residentId, like >= 0.35f ? "giftfor_liked" : "giftfor_other", q.givenItemId, ev.hostResidentId);
                }
            }
            Changed();
        }

        private void FinishEvent(VillageEvent ev)
        {
            ev.finished = true;
            if (ev.kind == VillageEventKind.Birthday)
            {
                // 手伝わなかった贈り物探しは、依頼主が自分で用意した（§10 手伝わない）
                foreach (var q in S.quests)
                {
                    var g = q.MainGoal;
                    if (g == null || g.type != GoalType.GiftFor || g.targetResidentId != ev.hostResidentId || !q.IsOpen) continue;
                    SetStatus(q, QuestStatus.Expired);
                    q.outcome = 3;
                    Memory(ev.hostResidentId, "birthday_gift", null, null, q.residentId, 0.5f, "prepared_by_friend");
                }
            }
            foreach (var st in S.stories)
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnEventFinished(this, st, ev);
            }
            Changed();
        }

        // ------------------------------------------------------------
        // 居合わせる（ローカル判定 → ホストへ）
        // ------------------------------------------------------------

        private void LocalEventCheck()
        {
            var p = LocalPlayer;
            if (p == null || p.Avatar == null) return;
            int today = Today;
            foreach (var ev in S.events)
            {
                if (ev.day != today || !ev.started || ev.finished || ev.cancelled) continue;
                if (ev.kind == VillageEventKind.MarketDay) continue;
                if (ev.attendees.Contains(p.PlayerId) || attendSent.Contains(ev.id)) continue;
                if (Flat(p.Position - ev.position) > QuestTuning.EventAttendRadius) continue;
                attendSent.Add(ev.id);
                string speaker = !string.IsNullOrEmpty(ev.hostResidentId) && IsActiveResident(ev.hostResidentId) ? ev.hostResidentId : (ev.gatherers.Count > 0 ? ev.gatherers[0] : null);
                if (speaker != null)
                {
                    var entry = BookEnsureEvent(ev);
                    BookAttach(entry, Say(speaker, QuestSpeech.EventWelcome(ev, speaker)), BookScene.Event);
                }
                WorldAuthority.Submit(new QuestAttendCommand { eventId = ev.id });
            }
        }

        private CommandResult HandleAttend(QuestAttendCommand c, CommandContext ctx)
        {
            var ev = FindEvent(c.eventId);
            if (ev == null || ev.cancelled || ev.attendees.Contains(ctx.IssuerId)) return CommandResult.Ok();
            ev.attendees.Add(ctx.IssuerId);
            if (!string.IsNullOrEmpty(ev.hostResidentId))
            {
                Friendship(ev.hostResidentId, ctx.IssuerId, QuestTuning.FriendshipOnEventAttend, "event:" + ev.kind);
                Memory(ev.hostResidentId, "event_" + ev.kind.ToString().ToLowerInvariant(), ctx.IssuerId, null, null, 0.7f);
            }
            if (ev.kind == VillageEventKind.StarBandViewing || ev.kind == VillageEventKind.NightSky)
            {
                foreach (var g in ev.gatherers)
                {
                    Friendship(g, ctx.IssuerId, 1, "star_band_together");
                    Memory(g, "watched_sky_together", ctx.IssuerId, null, null, 0.6f);
                }
            }
            if (ev.kind == VillageEventKind.FarewellParty && FriendshipOf(ev.hostResidentId, ctx.IssuerId) >= QuestTuning.FarewellGiftFriendship)
            {
                // 引っ越す前日、仲の良い住民が贈り物をくれる（メインストーリー 第4章）
                string gift = FavoriteThingOf(ev.hostResidentId);
                if (gift != null)
                {
                    ctx.GiveItem(new ItemStack(gift, 1), "farewell_gift");
                    AddPending(ctx.IssuerId, ev.hostResidentId, "gift", gift, null);
                }
            }
            if (ev.kind == VillageEventKind.CookParty) ctx.GiveItem(new ItemStack(QuestItemIds.HomemadeDish, 1), "cook_party");
            foreach (var st in S.stories)
            {
                if (st.finished || st.playerId != ctx.IssuerId) continue;
                StoryDef(st.storyId)?.OnAttended(this, st, ev, ctx);
            }
            Changed();
            return CommandResult.Ok();
        }

        /// <summary>住民のお気に入り（好みの内部値が最も高い、売り物になる物）。贈り物用。</summary>
        internal string FavoriteThingOf(string residentId)
        {
            var r = R(residentId);
            if (r == null) return null;
            string best = null;
            float bestLike = 0.1f;
            int n = 0;
            foreach (var def in ItemRegistry.All)
            {
                if (def.HasTag(ItemTags.NotSellable) || def.BasePrice <= 0 || def.Category == ItemCategory.Tool || def.Category == ItemCategory.RocketPart) continue;
                if (++n > 400) break;
                float like = r.EvaluateItem(def);
                if (like > bestLike) { bestLike = like; best = def.Id; }
            }
            return best ?? SharedItemIds.GiftBox;
        }
    }
}
