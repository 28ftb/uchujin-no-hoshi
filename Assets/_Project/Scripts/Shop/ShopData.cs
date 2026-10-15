using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Shop
{
    // =====================================================================
    // 商店の保存データ（SaveScope.Planet：ホストの星のセーブ。マルチ仕様 §5・§12）
    // JsonUtility で直列化するため public フィールドのみ・Dictionary 不使用。
    // =====================================================================

    /// <summary>棚の1枠。</summary>
    [Serializable]
    public class ShelfSlotData
    {
        public ShopDisplayType display;
        /// <summary>並べている商品（count=0 なら空き）。ownerId は使わず、持ち主は ownerId フィールドで持つ。</summary>
        public ItemStack goods = new ItemStack();
        /// <summary>主人公（または委託者）が付けた1個あたりの値段。</summary>
        public int price;
        /// <summary>商品の持ち主（ホスト＝星の持ち主、それ以外は委託販売：マルチ仕様 §12.3）。</summary>
        public string ownerId;
        /// <summary>並べた日（鮮度・新商品の判定）。</summary>
        public int listedDay;
        /// <summary>倉庫から同じ商品を同じ値段で自動補充する（補充の仕掛けが必要）。</summary>
        public bool autoRestock;
        public int soldTotal;
        /// <summary>売り切れた時に覚えておく商品（自動補充で同じ物を並べ直すため）。</summary>
        public string lastItemId;
        public int lastQuality;
        public string lastData;

        public bool IsEmpty => goods == null || goods.IsEmpty;
        public int Count => IsEmpty ? 0 : goods.count;
    }

    /// <summary>来店1回の中で、ある商品に対して観察できた行動。</summary>
    public enum VisitLineOutcome
    {
        Looked,              // 見ていった
        Bought,              // 買った
        PutBackAfterPrice,   // 値札を見て棚に戻した（内部的には「高い」と感じた。表示は行動の事実のみ）
        PutBackAfterWallet,  // 財布をのぞいて戻した（内部的には予算不足）
        PutBackAfterSniff,   // においをかいで戻した（内部的には鮮度）
        PutBackOther         // 手に取ったが戻した
    }

    [Serializable]
    public class VisitLineData
    {
        public int slotIndex;
        public string itemId;
        public int quality;
        public int unitPrice;
        public int qty;
        public VisitLineOutcome outcome;
        public string ownerId;
    }

    /// <summary>来店の結果のまとめ（内部用。画面には行動の事実だけを出す）。</summary>
    public enum VisitOutcome
    {
        Bought,
        DeclinedPrice,
        DeclinedWallet,
        DeclinedFreshness,
        WantedNotFound,
        NothingInteresting,
        Closed
    }

    /// <summary>反応の種類（発話の意味データを作るため。内部用で表示しない）。</summary>
    public enum ShopReactionKind
    {
        Bought,
        BoughtCheap,
        BoughtRare,
        BoughtDelicious,
        FulfilledRequest,
        TooExpensive,
        CantAfford,
        NotFresh,
        WantedNotFound,
        NothingGood,
        Closed
    }

    /// <summary>来店履歴（商店仕様 §9）。</summary>
    [Serializable]
    public class VisitRecordData
    {
        public string id;
        public int day;
        public int minute;
        public string residentId;
        /// <summary>来店時の見た目（引っ越し後も顔アイコンを出せるように保持）。</summary>
        public Color faceColor = Color.white;
        public AlienLineage lineage;
        public List<VisitLineData> lines = new List<VisitLineData>();
        public VisitOutcome outcome;
        public ShopReactionKind reaction;
        /// <summary>マネピが覚えた反応の発話記録ID（ISpeechService の記録）。</summary>
        public string recordId;
        public float satisfaction;
        public bool fulfilledRequest;
        /// <summary>この来店で残された要望ID。</summary>
        public string requestId;
        /// <summary>看板などで立ち寄った来店か。</summary>
        public bool dropIn;

        public int TotalSpent
        {
            get
            {
                int n = 0;
                foreach (var l in lines) if (l.outcome == VisitLineOutcome.Bought) n += l.unitPrice * l.qty;
                return n;
            }
        }
    }

    [Serializable]
    public class ItemDayStat
    {
        public string itemId;
        public int sold;
        public int revenue;
        public int putBack;
        public int looked;
    }

    /// <summary>日別の売上集計（商店仕様 §9）。</summary>
    [Serializable]
    public class DailySummaryData
    {
        public int day;
        public int revenue;
        public int itemsSold;
        public int visitors;
        public int buyers;
        public int putBacks;
        public int newRequests;
        public int manepiMessages;
        public List<ItemDayStat> items = new List<ItemDayStat>();
        /// <summary>持ち主ごとの売上（委託販売の区別）。</summary>
        public List<StringIntPair> revenueByOwner = new List<StringIntPair>();
        public List<string> soldOut = new List<string>();

        public ItemDayStat Item(string itemId)
        {
            foreach (var s in items) if (s.itemId == itemId) return s;
            var n = new ItemDayStat { itemId = itemId };
            items.Add(n);
            return n;
        }

        public int RevenueOf(string ownerId)
        {
            foreach (var p in revenueByOwner) if (p.key == ownerId) return p.value;
            return 0;
        }

        public void AddRevenue(string ownerId, int amount)
        {
            foreach (var p in revenueByOwner)
            {
                if (p.key == ownerId)
                {
                    p.value += amount;
                    return;
                }
            }
            revenueByOwner.Add(new StringIntPair(ownerId, amount));
        }
    }

    /// <summary>委託販売の未精算（持ち主が店で受け取る）。</summary>
    [Serializable]
    public class SettlementData
    {
        public string ownerId;
        public int money;
        public int soldCount;
        /// <summary>ホストが棚から下げた委託品（持ち主へ返す物）。</summary>
        public List<ItemStack> returnedItems = new List<ItemStack>();

        public bool IsEmpty => money <= 0 && (returnedItems == null || returnedItems.Count == 0);
    }

    /// <summary>
    /// 要望（商店仕様 §7「要望ボード」の最新版）。欲しい物が無かった住民がマネピに伝えた内容。
    /// ボードには形・色の絵と仕草だけを出し、文字の訳は出さない。
    /// </summary>
    [Serializable]
    public class ShopRequestData
    {
        public string id;
        public string residentId;
        public Color faceColor = Color.white;
        public AlienLineage lineage;
        public int day;
        public int minute;
        public ShoppingNeed need;
        public string preferredTag;
        /// <summary>欲しい物の概念（"ITEM:xxx" か総称）。内部用・表示禁止。</summary>
        public string wantedConcept;
        public string colorConcept;
        /// <summary>絵として見せる物（その住民が思い浮かべた具体的な品。無ければ総称の形）。</summary>
        public string hintItemId;
        public PrimitiveType pictureShape = PrimitiveType.Cube;
        public Color pictureColor = Color.white;
        public int quantity = 1;
        /// <summary>前に買った物が無くなっていて「また欲しい」。</summary>
        public bool again;
        public string recordId;
        public bool fulfilled;
        public int fulfilledDay;
    }

    /// <summary>住民ごとの商店での記憶（無限購買を防ぎ、耐久品の重複購入を避ける：バランス §7.3）。</summary>
    [Serializable]
    public class ResidentShopMemoryData
    {
        public string residentId;
        public List<string> durablesBought = new List<string>();
        /// <summary>消耗品を最後に買った日（itemId → day）。</summary>
        public List<StringIntPair> consumablesLastDay = new List<StringIntPair>();
        /// <summary>前に買って気に入った物（「また欲しい」の要望に使う）。</summary>
        public List<string> likedItems = new List<string>();
        public int spendDay;
        public int spentThatDay;
        public int lastVisitDay;
        public long lastVisitTotalMinutes = -100000;
        public int visits;

        public int LastBoughtDay(string itemId)
        {
            foreach (var p in consumablesLastDay) if (p.key == itemId) return p.value;
            return -1;
        }

        public void SetLastBought(string itemId, int day)
        {
            foreach (var p in consumablesLastDay)
            {
                if (p.key == itemId)
                {
                    p.value = day;
                    return;
                }
            }
            consumablesLastDay.Add(new StringIntPair(itemId, day));
            if (consumablesLastDay.Count > 40) consumablesLastDay.RemoveAt(0);
        }
    }

    [Serializable]
    public class PendingUpgradeData
    {
        public string upgradeId;
        public int readyDay;
        public string carpenterId;
        public string orderedBy;
        public int cost;
    }

    [Serializable]
    public class ShopSaveData
    {
        public int version = 1;
        public List<ShelfSlotData> slots = new List<ShelfSlotData>();
        public Inventory storage = new Inventory(ShopConfig.InitialStorageCapacity, "shop.storage");
        public int openHour = ShopConfig.DefaultOpenHour;
        public int closeHour = ShopConfig.DefaultCloseHour;
        public List<StringIntPair> upgradeLevels = new List<StringIntPair>();
        public List<PendingUpgradeData> pendingUpgrades = new List<PendingUpgradeData>();
        public List<VisitRecordData> visits = new List<VisitRecordData>();
        public List<DailySummaryData> days = new List<DailySummaryData>();
        public List<SettlementData> settlements = new List<SettlementData>();
        public List<ShopRequestData> requests = new List<ShopRequestData>();
        public List<ResidentShopMemoryData> residentMemory = new List<ResidentShopMemoryData>();
        /// <summary>住民モジュールから最後に来店の呼び出しがあった日（保険の立ち寄りの判定）。</summary>
        public int lastExternalVisitDay;
        public int visitSerial;

        public static ShopSaveData CreateDefault()
        {
            var d = new ShopSaveData();
            for (int i = 0; i < ShopConfig.InitialSlotCount; i++) d.slots.Add(new ShelfSlotData { display = ShopDisplayType.Normal });
            d.storage.EnsureSlots();
            return d;
        }

        /// <summary>読み込み後の欠損補完。</summary>
        public void Normalize()
        {
            if (slots == null) slots = new List<ShelfSlotData>();
            foreach (var s in slots)
            {
                if (s.goods == null) s.goods = new ItemStack();
                if (s.goods.count <= 0) s.goods = new ItemStack();
            }
            if (storage == null) storage = new Inventory(ShopConfig.InitialStorageCapacity, "shop.storage");
            storage.ownerId = "shop.storage";
            storage.EnsureSlots();
            if (upgradeLevels == null) upgradeLevels = new List<StringIntPair>();
            if (pendingUpgrades == null) pendingUpgrades = new List<PendingUpgradeData>();
            if (visits == null) visits = new List<VisitRecordData>();
            foreach (var v in visits) if (v.lines == null) v.lines = new List<VisitLineData>();
            if (days == null) days = new List<DailySummaryData>();
            foreach (var d in days)
            {
                if (d.items == null) d.items = new List<ItemDayStat>();
                if (d.revenueByOwner == null) d.revenueByOwner = new List<StringIntPair>();
                if (d.soldOut == null) d.soldOut = new List<string>();
            }
            if (settlements == null) settlements = new List<SettlementData>();
            foreach (var s in settlements) if (s.returnedItems == null) s.returnedItems = new List<ItemStack>();
            if (requests == null) requests = new List<ShopRequestData>();
            if (residentMemory == null) residentMemory = new List<ResidentShopMemoryData>();
            foreach (var m in residentMemory)
            {
                if (m.durablesBought == null) m.durablesBought = new List<string>();
                if (m.consumablesLastDay == null) m.consumablesLastDay = new List<StringIntPair>();
                if (m.likedItems == null) m.likedItems = new List<string>();
            }
            if (closeHour <= openHour) { openHour = ShopConfig.DefaultOpenHour; closeHour = ShopConfig.DefaultCloseHour; }
        }

        public int Level(string upgradeId)
        {
            foreach (var p in upgradeLevels) if (p.key == upgradeId) return p.value;
            return 0;
        }

        public void SetLevel(string upgradeId, int level)
        {
            foreach (var p in upgradeLevels)
            {
                if (p.key == upgradeId)
                {
                    p.value = level;
                    return;
                }
            }
            upgradeLevels.Add(new StringIntPair(upgradeId, level));
        }

        public DailySummaryData Summary(int day, bool create)
        {
            foreach (var d in days) if (d.day == day) return d;
            if (!create) return null;
            var n = new DailySummaryData { day = day };
            days.Add(n);
            return n;
        }

        public SettlementData Settlement(string ownerId, bool create)
        {
            foreach (var s in settlements) if (s.ownerId == ownerId) return s;
            if (!create) return null;
            var n = new SettlementData { ownerId = ownerId };
            settlements.Add(n);
            return n;
        }

        public ResidentShopMemoryData Memory(string residentId)
        {
            foreach (var m in residentMemory) if (m.residentId == residentId) return m;
            var n = new ResidentShopMemoryData { residentId = residentId };
            residentMemory.Add(n);
            return n;
        }

        public ShopRequestData OpenRequestOf(string residentId)
        {
            foreach (var r in requests) if (!r.fulfilled && r.residentId == residentId) return r;
            return null;
        }

        public VisitRecordData FindVisit(string id)
        {
            foreach (var v in visits) if (v.id == id) return v;
            return null;
        }
    }

    /// <summary>星ごとのプレイヤー個人の知識（どこまで伝言を確認したか）。</summary>
    [Serializable]
    public class ShopKnowledgeData
    {
        public long lastReviewedTotalMinutes;
    }
}
