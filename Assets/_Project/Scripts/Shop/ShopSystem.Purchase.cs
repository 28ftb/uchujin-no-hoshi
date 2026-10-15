using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Shop
{
    // =====================================================================
    // 来店・購買判断・取引（商店仕様 §3、バランス仕様 §7.2〜§7.6）
    // =====================================================================
    public sealed partial class ShopSystem
    {
        /// <summary>検討中の商品1つ分。</summary>
        private sealed class Candidate
        {
            public int slotIndex;
            public ShelfSlotData slot;
            public ItemDefinition def;
            public float preference;
            public float needMatch;
            public float desire;
            public float fairValue;
            public float ratio;
            public float tolerance;
            public float freshness;
            public bool matchesRequest;
            public VisitLineData line;
        }

        /// <summary>
        /// 住民の来店（住民モジュールが来店時に呼ぶ）。棚を見て回り、買うかを決め、取引・反応・マネピの記録まで行う。
        /// シミュレーションなのでホスト（権威側）でのみ実行する。
        /// </summary>
        public ShopVisitResult ResidentVisit(IResidentInfo resident, ShoppingIntent intent)
        {
            if (resident == null || !IsAuthority || Clock == null || !resident.IsActive) return new ShopVisitResult();
            Data.lastExternalVisitDay = Today;
            if (!IsOpen)
            {
                ReactToClosedShop(resident);
                return new ShopVisitResult { satisfaction = -0.1f };
            }
            return RunVisit(resident, intent ?? DefaultIntent(resident), false);
        }

        /// <summary>目的の指定が無い来店（立ち寄り）の目的。</summary>
        internal ShoppingIntent DefaultIntent(IResidentInfo r)
        {
            var intent = new ShoppingIntent();
            var req = Data.OpenRequestOf(r.Id);
            if (req != null)
            {
                intent.need = req.need;
                intent.preferredTag = req.preferredTag;
                return intent;
            }
            int h = Clock != null ? Clock.Hour : 12;
            if (r.Job == ResidentJob.Cook && h < 12)
            {
                intent.need = ShoppingNeed.Ingredient;
                intent.reasonTag = "cooking";
            }
            else if (h >= 16 && h < 20) intent.need = ShoppingNeed.Food;
            return intent;
        }

        private ShopVisitResult RunVisit(IResidentInfo resident, ShoppingIntent intent, bool dropIn)
        {
            var result = new ShopVisitResult();
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return result;

            int day = Today;
            int minute = Clock.MinuteOfDay;
            int hour = Clock.Hour;
            ulong seed = Seed;
            var rng = SeededRandom.For(seed, "shop.visit:" + resident.Id + ":" + Clock.TotalMinutes + ":" + (Data.visitSerial++));
            var traits = ShopTraits.For(seed, resident, day);
            var mem = Data.Memory(resident.Id);
            if (mem.spendDay != day)
            {
                mem.spendDay = day;
                mem.spentThatDay = 0;
            }
            mem.lastVisitDay = day;
            mem.lastVisitTotalMinutes = Clock.TotalMinutes;
            mem.visits++;

            // ---- 予算：指定予算・所持金・1日の上限のうち最も小さいもの（住民に無限購買させない） ----
            int money = Mathf.Max(0, resident.Money);
            int dailyCap = Mathf.Max(ShopConfig.MinDailySpendCap, Mathf.RoundToInt(money * ShopConfig.DailySpendFraction));
            int capLeft = Mathf.Max(0, dailyCap - mem.spentThatDay);
            int budget = intent.budget > 0
                ? intent.budget
                : ShopConfig.DefaultBudgetBase + Mathf.RoundToInt(money * Mathf.Lerp(ShopConfig.DefaultBudgetFractionFree, ShopConfig.DefaultBudgetFractionThrifty, traits.thrift));
            budget = Mathf.Max(0, Mathf.Min(budget, Mathf.Min(money, capLeft)));

            var weatherSvc = Game.Service<IWeatherService>();
            WeatherState? weather = null;
            if (weatherSvc != null) weather = weatherSvc.Current;
            var openRequest = Data.OpenRequestOf(resident.Id);
            IResidentInfo giftTarget = !string.IsNullOrEmpty(intent.forResidentId) ? dir.Get(intent.forResidentId) : null;
            int sign = Data.Level(ShopUpgradeIds.Sign);
            int decor = Data.Level(ShopUpgradeIds.Decor);
            bool hasNeed = intent.need != ShoppingNeed.Any || !string.IsNullOrEmpty(intent.preferredTag);

            var visit = new VisitRecordData
            {
                id = Ids.New(),
                day = day,
                minute = minute,
                residentId = resident.Id,
                faceColor = resident.MainColor,
                lineage = resident.Lineage,
                dropIn = dropIn
            };

            // ---- 棚を見て回る ----
            var cands = new List<Candidate>();
            int total = Data.slots.Count;
            bool anyNeedMatchOnShelf = false;
            for (int i = 0; i < total; i++)
            {
                var slot = Data.slots[i];
                if (slot.IsEmpty) continue;
                var def = ItemRegistry.Get(slot.goods.itemId);
                if (def == null) continue;

                float needMatch = ShopRules.NeedMatch(intent.need, def);
                bool tagMatch = !string.IsNullOrEmpty(intent.preferredTag) && def.HasTag(intent.preferredTag);
                bool reqMatch = openRequest != null && RequestMatches(openRequest, def);
                if (intent.need != ShoppingNeed.Any && needMatch >= 0.6f && (string.IsNullOrEmpty(intent.preferredTag) || tagMatch)) anyNeedMatchOnShelf = true;

                // 目に留まるか（入口付近・看板・内装・特売・水槽・家具ステージ）
                float rank = total <= 1 ? 0f : i / (float)(total - 1);
                float notice = 0.55f + 0.35f * (1f - rank) + 0.05f * sign + 0.03f * decor;
                bool discounted = slot.price < def.BasePrice;
                if (slot.display == ShopDisplayType.Specials && discounted) notice += 0.25f;
                if (slot.display == ShopDisplayType.Aquarium) notice += 0.15f;
                if (slot.display == ShopDisplayType.FurnitureStage) notice += 0.1f;
                if (needMatch >= 0.8f || tagMatch || reqMatch) notice += 0.3f;
                if (!rng.Chance(Mathf.Clamp(notice, 0.1f, 0.97f))) continue;

                float pref = resident.EvaluateItem(def);
                if (giftTarget != null) pref = 0.4f * pref + 0.6f * giftTarget.EvaluateItem(def);
                float fresh = ShopRules.Freshness(slot, def, day);

                float desire = needMatch + 0.6f * pref;
                if (hasNeed && intent.need != ShoppingNeed.Any && needMatch < 0.3f) desire -= 0.35f;
                if (tagMatch) desire += 0.4f;
                if (reqMatch) desire += 0.5f;
                desire += ShopRules.JobAffinity(resident.Job, def);
                desire += ShopRules.WeatherTimeModifier(def, hour, weather);
                desire += traits.collector * def.Rarity * 0.12f;
                if (def.Consumable || def.Category == ItemCategory.Food) desire += traits.glutton * 0.2f;
                if (day - slot.listedDay <= ShopConfig.NewArrivalDays) desire += traits.trendy * 0.25f;
                if (slot.display == ShopDisplayType.Aquarium && def.Category == ItemCategory.Fish) desire += 0.15f;
                if (slot.display == ShopDisplayType.FurnitureStage) desire += 0.15f;
                if (slot.display == ShopDisplayType.Specials && discounted) desire += 0.2f;
                desire += 0.03f * decor;
                desire += 0.15f * traits.whim * traits.whimsical;
                // 耐久品は既に持っていれば欲しくない／消耗品は今日もう買っていれば控えめ（バランス §7.3）
                if (!def.Consumable && mem.durablesBought.Contains(def.Id) && string.IsNullOrEmpty(intent.forResidentId)) desire -= ShopConfig.OwnedDurablePenalty;
                if (mem.LastBoughtDay(def.Id) == day) desire -= ShopConfig.SatiationPenalty;
                if (fresh < 1f) desire -= (1f - fresh) * 0.5f;

                // その住民が感じる価値と「高い」の境界
                float fair = Mathf.Max(1, def.BasePrice) * ShopRules.QualityMultiplier(slot.goods.quality, traits.picky) * fresh * (1f + 0.35f * pref);
                if (ShopRules.IsRare(def)) fair *= 1f + 0.25f * traits.collector * Mathf.Max(1, def.Rarity) * 0.5f;
                float ratio = slot.price / Mathf.Max(0.5f, fair);

                var line = new VisitLineData
                {
                    slotIndex = i,
                    itemId = def.Id,
                    quality = slot.goods.quality,
                    unitPrice = slot.price,
                    qty = 0,
                    outcome = VisitLineOutcome.Looked,
                    ownerId = slot.ownerId
                };
                visit.lines.Add(line);
                if (!result.lookedAtItemIds.Contains(def.Id)) result.lookedAtItemIds.Add(def.Id);

                cands.Add(new Candidate
                {
                    slotIndex = i,
                    slot = slot,
                    def = def,
                    preference = pref,
                    needMatch = needMatch,
                    desire = desire,
                    fairValue = fair,
                    ratio = ratio,
                    tolerance = traits.Tolerance(pref),
                    freshness = fresh,
                    matchesRequest = reqMatch,
                    line = line
                });
            }

            cands.Sort((a, b) => b.desire.CompareTo(a.desire));

            // ---- 手に取って検討する ----
            Candidate chosen = null, expensive = null, unaffordable = null, notFresh = null;
            int considered = 0;
            float nightMult = (hour >= 20 || hour < 6) ? ShopConfig.NightBuyMultiplier : 1f;
            foreach (var c in cands)
            {
                if (considered >= ShopConfig.MaxConsiderPerVisit) break;
                if (c.desire < ShopConfig.InterestThreshold) break;
                considered++;
                if (c.freshness < ShopConfig.NotFreshThreshold && rng.Chance(0.6f))
                {
                    c.line.outcome = VisitLineOutcome.PutBackAfterSniff;
                    if (notFresh == null) notFresh = c;
                    continue;
                }
                bool feelsExpensive = c.ratio > c.tolerance;
                if (c.slot.price > budget)
                {
                    if (feelsExpensive)
                    {
                        c.line.outcome = VisitLineOutcome.PutBackAfterPrice;
                        if (expensive == null) expensive = c;
                    }
                    else
                    {
                        c.line.outcome = VisitLineOutcome.PutBackAfterWallet;
                        if (unaffordable == null) unaffordable = c;
                    }
                    continue;
                }
                float accept = 1f / (1f + Mathf.Exp(ShopConfig.AcceptCurveSteepness * (c.ratio - c.tolerance)));
                float pBuy = accept * Mathf.Clamp(0.3f + 0.6f * c.desire, 0.1f, 0.98f) * nightMult;
                if (rng.Chance(pBuy))
                {
                    chosen = c;
                    break;
                }
                if (feelsExpensive)
                {
                    c.line.outcome = VisitLineOutcome.PutBackAfterPrice;
                    if (expensive == null) expensive = c;
                }
                else c.line.outcome = VisitLineOutcome.PutBackOther;
            }

            // ---- 取引 ----
            int spent = 0;
            bool fulfilled = false;
            if (chosen != null)
            {
                int qty = DecideQuantity(chosen, intent, traits, budget, rng);
                int bought = TryExecuteSale(dir, resident, chosen, qty, visit);
                if (bought <= 0 && qty > 1) bought = TryExecuteSale(dir, resident, chosen, 1, visit);
                if (bought > 0)
                {
                    spent += chosen.slot.price * bought;
                    chosen.line.qty = bought;
                    chosen.line.outcome = VisitLineOutcome.Bought;
                    if (openRequest != null && RequestMatches(openRequest, chosen.def))
                    {
                        fulfilled = true;
                        openRequest.fulfilled = true;
                        openRequest.fulfilledDay = day;
                        visit.fulfilledRequest = true;
                        // 要望を叶えてくれた店主への好意（商店 §7：要望を叶えた際の反応）
                        string owner = IsHostOwner(chosen.slot.ownerId) ? HostId : chosen.slot.ownerId;
                        dir.ChangeFriendship(resident.Id, owner, 2, "shop_request_fulfilled");
                    }
                    // まとめ買い：隣の関連商品（商店 §8 レイアウト効果）
                    int left = budget - spent;
                    foreach (var c in cands)
                    {
                        if (c == chosen || c.line.outcome != VisitLineOutcome.Looked) continue;
                        if (Mathf.Abs(c.slotIndex - chosen.slotIndex) > 1 || !ShopRules.Related(c.def, chosen.def)) continue;
                        if (c.desire < ShopConfig.InterestThreshold * 0.8f || c.ratio > c.tolerance || c.slot.price > left) continue;
                        if (!rng.Chance(ShopConfig.BundleChance)) continue;
                        int b2 = TryExecuteSale(dir, resident, c, 1, visit);
                        if (b2 > 0)
                        {
                            c.line.qty = b2;
                            c.line.outcome = VisitLineOutcome.Bought;
                            spent += c.slot.price * b2;
                        }
                        break;
                    }
                }
                else
                {
                    chosen.line.outcome = VisitLineOutcome.PutBackAfterWallet;
                    if (unaffordable == null) unaffordable = chosen;
                    chosen = null;
                }
            }
            mem.spentThatDay += spent;

            // ---- 欲しい物が無かった → 要望（マネピへ） ----
            ShopRequestData newRequest = null;
            bool wantedNotFound = false;
            if (chosen == null && expensive == null && unaffordable == null && notFresh == null)
            {
                string againItem = FindSoldOutFavorite(mem, intent);
                if ((hasNeed && !anyNeedMatchOnShelf) || againItem != null || (openRequest != null && !OpenRequestMatchesStock(resident.Id)))
                {
                    wantedNotFound = true;
                    newRequest = openRequest ?? CreateRequest(resident, intent, againItem, rng);
                }
            }

            // ---- 結果のまとめ ----
            VisitOutcome outcome;
            ShopReactionKind kind;
            Candidate focus;
            float satisfaction;
            if (chosen != null)
            {
                outcome = VisitOutcome.Bought;
                focus = chosen;
                float valueJoy = Mathf.Clamp(chosen.tolerance - chosen.ratio, -0.5f, 0.8f);
                satisfaction = Mathf.Clamp(0.25f + 0.4f * chosen.preference + 0.4f * valueJoy + (fulfilled ? 0.3f : 0f) + 0.03f * decor, -1f, 1f);
                bool repeat = (chosen.def.Consumable || IsEdible(chosen.def)) && mem.LastBoughtDay(chosen.def.Id) >= 0 && mem.LastBoughtDay(chosen.def.Id) < day;
                if (fulfilled) kind = ShopReactionKind.FulfilledRequest;
                else if (chosen.ratio <= ShopConfig.CheapRatio) kind = ShopReactionKind.BoughtCheap;
                else if (ShopRules.IsRare(chosen.def) && (traits.collector > 0.35f || intent.need == ShoppingNeed.Curiosity)) kind = ShopReactionKind.BoughtRare;
                else if (repeat && chosen.preference > 0.3f && IsEdible(chosen.def)) kind = ShopReactionKind.BoughtDelicious;
                else kind = ShopReactionKind.Bought;
            }
            else if (expensive != null)
            {
                outcome = VisitOutcome.DeclinedPrice;
                kind = ShopReactionKind.TooExpensive;
                focus = expensive;
                satisfaction = -0.3f;
            }
            else if (unaffordable != null)
            {
                outcome = VisitOutcome.DeclinedWallet;
                kind = ShopReactionKind.CantAfford;
                focus = unaffordable;
                satisfaction = -0.2f;
            }
            else if (notFresh != null)
            {
                outcome = VisitOutcome.DeclinedFreshness;
                kind = ShopReactionKind.NotFresh;
                focus = notFresh;
                satisfaction = -0.25f;
            }
            else if (wantedNotFound)
            {
                outcome = VisitOutcome.WantedNotFound;
                kind = ShopReactionKind.WantedNotFound;
                focus = null;
                satisfaction = -0.4f;
            }
            else
            {
                outcome = VisitOutcome.NothingInteresting;
                kind = ShopReactionKind.NothingGood;
                focus = null;
                satisfaction = -0.1f;
            }

            // 消耗品・耐久品の記憶（購入した物すべて）
            foreach (var l in visit.lines)
            {
                if (l.outcome != VisitLineOutcome.Bought) continue;
                var d = ItemRegistry.Get(l.itemId);
                if (d == null) continue;
                if (d.Consumable || IsEdible(d)) mem.SetLastBought(d.Id, day);
                else if (string.IsNullOrEmpty(intent.forResidentId) && !mem.durablesBought.Contains(d.Id)) mem.durablesBought.Add(d.Id);
            }
            if (chosen != null && chosen.preference > 0.3f && !mem.likedItems.Contains(chosen.def.Id))
            {
                mem.likedItems.Add(chosen.def.Id);
                if (mem.likedItems.Count > 8) mem.likedItems.RemoveAt(0);
            }

            visit.outcome = outcome;
            visit.reaction = kind;
            visit.satisfaction = satisfaction;

            // ---- 反応：意味データの発話 → 近くにいれば吹き出し → 必ずマネピが覚える ----
            var rc = new ShopReactionContext
            {
                resident = resident,
                kind = kind,
                focusDef = focus != null ? focus.def : null,
                focusQuality = focus != null ? focus.line.quality : 0,
                qty = focus != null ? Mathf.Max(1, focus.line.qty) : 1,
                ratio = focus != null ? focus.ratio : 1f,
                tolerance = focus != null ? focus.tolerance : 1f,
                preference = focus != null ? focus.preference : 0f,
                intent = intent,
                request = newRequest,
                thrift = traits.thrift
            };
            var utterance = ShopReactions.Build(rc, rng);
            string recordId = SpeakAndRemember(resident, utterance, focus != null ? focus.slotIndex : -1);
            visit.recordId = recordId;
            if (newRequest != null)
            {
                visit.requestId = newRequest.id;
                if (string.IsNullOrEmpty(newRequest.recordId)) newRequest.recordId = recordId;
            }

            // ---- 記録 ----
            Data.visits.Add(visit);
            while (Data.visits.Count > ShopConfig.MaxVisitLog) Data.visits.RemoveAt(0);
            var sum = Data.Summary(day, true);
            sum.visitors++;
            if (chosen != null) sum.buyers++;
            if (!string.IsNullOrEmpty(recordId)) sum.manepiMessages++;
            if (newRequest != null && newRequest.day == day && newRequest.minute == minute) sum.newRequests++;
            foreach (var l in visit.lines)
            {
                var st = sum.Item(l.itemId);
                st.looked++;
                if (l.outcome == VisitLineOutcome.PutBackAfterPrice || l.outcome == VisitLineOutcome.PutBackAfterWallet ||
                    l.outcome == VisitLineOutcome.PutBackAfterSniff || l.outcome == VisitLineOutcome.PutBackOther)
                {
                    st.putBack++;
                    sum.putBacks++;
                }
            }

            // ---- 演出：棚の前で品定めする姿が外から見える ----
            if (ShopConfig.BrowseWalkEnabled && focus != null)
            {
                int until = Mathf.Min(TimeSystem.MinutesPerDay - 1, minute + ShopConfig.BrowseMinutes);
                dir.RequestGoTo(resident.Id, SlotWorldPosition(focus.slotIndex), until, "shop_browse");
            }

            AfterStateChanged();

            result.bought = chosen != null;
            result.itemId = chosen != null ? chosen.def.Id : null;
            if (chosen != null)
            {
                result.quality = chosen.line.quality;
                result.price = chosen.line.unitPrice * chosen.line.qty;
            }
            result.satisfaction = satisfaction;
            result.thoughtTooExpensive = expensive != null;
            result.wantedButNotFound = wantedNotFound;
            result.reactionRecordId = recordId;
            return result;
        }

        private static bool IsEdible(ItemDefinition def)
        {
            if (def == null) return false;
            return def.Category == ItemCategory.Food || def.Category == ItemCategory.Processed || def.Category == ItemCategory.Fish ||
                   def.Category == ItemCategory.Crop || def.HasTag(ItemTags.Edible) || def.HasTag(ItemTags.Food);
        }

        private static int DecideQuantity(Candidate c, ShoppingIntent intent, ShopTraits traits, int budget, SeededRandom rng)
        {
            int qty = 1;
            bool consumable = c.def.Consumable || IsEdible(c.def);
            if (consumable)
            {
                if (intent.need == ShoppingNeed.Food || intent.need == ShoppingNeed.Ingredient) qty += rng.Range(0, 2);
                if (intent.reasonTag == "cooking" || intent.reasonTag == "party") qty += rng.Range(1, 3);
                if (c.ratio <= ShopConfig.CheapRatio) qty += 1;
                if (traits.glutton > 0.65f) qty += 1;
            }
            qty = Mathf.Min(qty, ShopConfig.MaxQtyPerPurchase);
            qty = Mathf.Min(qty, c.slot.goods.count);
            if (c.slot.price > 0) qty = Mathf.Min(qty, budget / c.slot.price);
            return Mathf.Max(1, qty);
        }

        /// <summary>
        /// 取引：住民の支払い → 住民が受け取る → 棚から減らす → 持ち主へ売上 → イベント → 売上記録。
        /// 実際に売れた個数を返す。
        /// </summary>
        private int TryExecuteSale(IResidentDirectory dir, IResidentInfo resident, Candidate c, int qty, VisitRecordData visit)
        {
            var slot = c.slot;
            if (slot.IsEmpty || qty <= 0) return 0;
            qty = Mathf.Min(qty, slot.goods.count);
            int unit = slot.price;
            int total = unit * qty;
            string itemId = slot.goods.itemId;
            if (!dir.TrySpend(resident.Id, total, "shop:" + itemId)) return 0;

            var given = slot.goods.CloneWithCount(qty);
            given.ownerId = null;
            dir.GiveItem(resident.Id, given, "shop_purchase");

            slot.goods.count -= qty;
            slot.soldTotal += qty;
            string owner = IsHostOwner(slot.ownerId) ? HostId : slot.ownerId;
            int quality = given.quality;
            int day = Today;
            int minute = Clock.MinuteOfDay;
            var sum = Data.Summary(day, true);
            if (slot.goods.count <= 0)
            {
                slot.lastItemId = itemId;
                slot.lastQuality = quality;
                slot.lastData = given.data;
                slot.goods = new ItemStack();
                if (!sum.soldOut.Contains(itemId)) sum.soldOut.Add(itemId);
            }

            // 売上：ローカルの持ち主（＝ホスト）なら財布へ、委託品は未精算へ（マルチ §12.3）
            var local = LocalPlayer;
            if (local != null && owner == local.PlayerId) local.Wallet.Add(total, "shop_sale:" + itemId);
            else
            {
                var st = Data.Settlement(owner, true);
                st.money += total;
                st.soldCount += qty;
            }

            sum.revenue += total;
            sum.itemsSold += qty;
            sum.AddRevenue(owner, total);
            var stat = sum.Item(itemId);
            stat.sold += qty;
            stat.revenue += total;

            // 共有イベント（クエスト・住民の生活・タブレットが反応する）。数えやすいよう1個ずつ発行する。
            for (int i = 0; i < qty; i++)
            {
                EventBus.Publish(new ShopItemSoldEvent { ShopOwnerId = owner, ResidentId = resident.Id, ItemId = itemId, Quality = quality, Price = unit, Day = day, Minute = minute });
                EventBus.Publish(new ResidentPurchaseEvent { ResidentId = resident.Id, ItemId = itemId, Price = unit, Where = PurchaseWhere });
            }
            dir.AddMemory(resident.Id, new ResidentMemoryEntry { kind = "bought_from_shop", playerId = owner, itemId = itemId, day = day, importance = 0.3f, detail = "price:" + unit });

            // 住民辞典へ「客観的な事実」として購入を記録（解釈は書かない：タブレット §7.3）
            if (local != null && owner == local.PlayerId)
            {
                var tablet = Game.Service<ITabletService>();
                tablet?.RecordResidentObservation(resident.Id,
                    string.Format("{0}日目 {1:00}:{2:00} 商店で {3} を{4}個買った（{5}）", day, minute / 60, minute % 60, ItemRegistry.NameOf(itemId) + ItemStack.QualityStars(quality), qty, Wallet.Format(total)),
                    string.Format("Day {0} {1:00}:{2:00} bought {4} x {3} at the shop ({5})", day, minute / 60, minute % 60, ItemRegistry.GetOrPlaceholder(itemId).NameEn + ItemStack.QualityStars(quality), qty, Wallet.Format(total)),
                    null);
                if (IsLocalPlayerNear(30f))
                {
                    UIManager.Toast(Loc.Format("{0}が売れました（+{1}）", "{0} sold (+{1})", ItemRegistry.NameOf(itemId), Wallet.Format(total)), 2.5f);
                    Visuals?.PlaySaleFx(c.slotIndex);
                }
            }
            return qty;
        }

        /// <summary>前に買って気に入った物が、今は棚に無いか（「また欲しい」）。</summary>
        private string FindSoldOutFavorite(ResidentShopMemoryData mem, ShoppingIntent intent)
        {
            for (int i = mem.likedItems.Count - 1; i >= 0; i--)
            {
                string id = mem.likedItems[i];
                var def = ItemRegistry.Get(id);
                if (def == null) continue;
                if (intent.need != ShoppingNeed.Any && ShopRules.NeedMatch(intent.need, def) < 0.6f) continue;
                if (!(def.Consumable || IsEdible(def))) continue;
                bool onShelf = false;
                foreach (var s in Data.slots) if (!s.IsEmpty && s.goods.itemId == id) { onShelf = true; break; }
                if (!onShelf) return id;
            }
            return null;
        }

        internal static bool RequestMatches(ShopRequestData req, ItemDefinition def)
        {
            if (req == null || def == null || req.fulfilled) return false;
            if (!string.IsNullOrEmpty(req.hintItemId) && req.again) return def.Id == req.hintItemId;
            if (req.need != ShoppingNeed.Any && ShopRules.NeedMatch(req.need, def) < 0.6f) return false;
            if (!string.IsNullOrEmpty(req.preferredTag) && !def.HasTag(req.preferredTag)) return false;
            if (req.need == ShoppingNeed.Any && string.IsNullOrEmpty(req.preferredTag)) return def.Id == req.hintItemId;
            return true;
        }

        /// <summary>要望を作る：何を思い浮かべているか（絵として見せる物）を決める。</summary>
        private ShopRequestData CreateRequest(IResidentInfo resident, ShoppingIntent intent, string againItem, SeededRandom rng)
        {
            int open = 0;
            foreach (var r in Data.requests) if (!r.fulfilled) open++;
            if (open >= ShopConfig.MaxOpenRequests)
            {
                // 古い要望から押し出す
                for (int i = 0; i < Data.requests.Count; i++)
                {
                    if (Data.requests[i].fulfilled) continue;
                    Data.requests.RemoveAt(i);
                    break;
                }
            }
            var req = new ShopRequestData
            {
                id = Ids.New(),
                residentId = resident.Id,
                faceColor = resident.MainColor,
                lineage = resident.Lineage,
                day = Today,
                minute = Clock.MinuteOfDay,
                need = intent.need,
                preferredTag = intent.preferredTag,
                quantity = intent.reasonTag == "cooking" || intent.reasonTag == "party" ? rng.Range(2, 4) : 1
            };
            ItemDefinition hint = null;
            if (againItem != null)
            {
                hint = ItemRegistry.Get(againItem);
                req.again = true;
            }
            else
            {
                // その住民が思い浮かべる品：目的・特徴に合う登録アイテムの中で一番好きな物
                float best = -999f;
                foreach (var d in ItemRegistry.All)
                {
                    if (!ShopRules.IsSellable(d)) continue;
                    if (intent.need != ShoppingNeed.Any && ShopRules.NeedMatch(intent.need, d) < 0.6f) continue;
                    if (!string.IsNullOrEmpty(intent.preferredTag) && !d.HasTag(intent.preferredTag)) continue;
                    if (intent.need == ShoppingNeed.Any && string.IsNullOrEmpty(intent.preferredTag) && resident.EvaluateItem(d) < 0.4f) continue;
                    float s = resident.EvaluateItem(d) + rng.Range(0f, 0.1f);
                    if (s > best)
                    {
                        best = s;
                        hint = d;
                    }
                }
            }
            req.hintItemId = hint != null ? hint.Id : null;
            req.colorConcept = !string.IsNullOrEmpty(intent.preferredTag) ? ShopRules.TagAdjective(intent.preferredTag) : null;
            if (req.colorConcept == null && hint != null && req.again) req.colorConcept = Concepts.ColorFromTags(hint);
            if (req.again && hint != null) req.wantedConcept = hint.EffectiveConceptId;
            else
            {
                string noun = !string.IsNullOrEmpty(intent.preferredTag) ? ShopRules.TagNoun(intent.preferredTag) : null;
                req.wantedConcept = noun ?? (intent.need == ShoppingNeed.Any && hint != null ? hint.EffectiveConceptId : ShopRules.NeedConcept(intent.need));
            }
            req.pictureShape = hint != null ? hint.Shape : ShopRules.NeedShape(intent.need);
            Color baseColor = hint != null ? hint.Color : new Color(0.85f, 0.85f, 0.85f);
            req.pictureColor = ShopRules.ColorOfConcept(req.colorConcept, baseColor);
            Data.requests.Add(req);
            return req;
        }

        /// <summary>
        /// 反応を話させ（プレイヤーが近くにいれば吹き出し）、必ずマネピに覚えさせる（タブレット §4.3：客がマネピへ伝えた感想）。
        /// 戻り値はマネピの記録ID。
        /// </summary>
        private string SpeakAndRemember(IResidentInfo resident, Utterance u, int slotIndex)
        {
            if (u == null) return null;
            if (IsLocalPlayerNear(ShopConfig.HearingRadius))
                Game.Service<IResidentDirectory>()?.Speak(resident.Id, u, SpeechChannel.Overheard);
            var speech = Game.Service<ISpeechService>();
            if (speech == null) return null;
            Vector3 at = ManepiTransform != null ? ManepiTransform.position : (Frame != null ? Frame.Center : resident.Position);
            var record = speech.CreateRecord(u, SpeechChannel.ManepiPlayback, "resident", at);
            if (record == null) return null;
            Game.Service<IManepiService>()?.LeaveMessage(ManepiBoardId, record);
            Visuals?.OnManepiHeard();
            return record.recordId;
        }

        /// <summary>閉店中に来た住民：がっかりした様子（吹き出しのみ。マネピは閉店中は預からない）。</summary>
        private void ReactToClosedShop(IResidentInfo resident)
        {
            if (!IsLocalPlayerNear(ShopConfig.HearingRadius)) return;
            var rc = new ShopReactionContext { resident = resident, kind = ShopReactionKind.Closed, intent = new ShoppingIntent() };
            var u = ShopReactions.Build(rc, SeededRandom.For(Seed, "shop.closed:" + resident.Id + ":" + Clock.TotalMinutes));
            Game.Service<IResidentDirectory>()?.Speak(resident.Id, u, SpeechChannel.Overheard);
        }
    }
}
