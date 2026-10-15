using System;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Shop
{
    // =====================================================================
    // 棚・倉庫・営業時間・改装・委託販売の操作（WorldCommand：ホスト権威。マルチ §12・§20）
    // =====================================================================

    /// <summary>棚に並べる（持ち物または倉庫から）。ゲストが自分の物を並べると委託販売になる。</summary>
    [Serializable]
    public class ShopStockCommand : WorldCommand
    {
        public int slotIndex;
        public bool fromStorage;
        /// <summary>持ち物（ローカル発行者のみ有効）または倉庫のスロット番号。</summary>
        public int sourceSlot = -1;
        public string itemId;
        public int quality;
        public int count;
        /// <summary>0 以下なら現在の値段（空き枠なら目安の値段）。</summary>
        public int price;
    }

    /// <summary>棚から下げる。</summary>
    [Serializable]
    public class ShopUnstockCommand : WorldCommand
    {
        public int slotIndex;
        public int count;
        public bool toStorage;
    }

    [Serializable]
    public class ShopSetPriceCommand : WorldCommand
    {
        public int slotIndex;
        public int price;
    }

    [Serializable]
    public class ShopSetAutoRestockCommand : WorldCommand
    {
        public int slotIndex;
        public bool on;
    }

    /// <summary>2つの枠の中身を入れ替える（入口付近に置く商品を変えるなど）。</summary>
    [Serializable]
    public class ShopSwapSlotsCommand : WorldCommand
    {
        public int a;
        public int b;
    }

    [Serializable]
    public class ShopStorageDepositCommand : WorldCommand
    {
        public int inventorySlot = -1;
        public string itemId;
        public int quality;
        public int count;
    }

    [Serializable]
    public class ShopStorageWithdrawCommand : WorldCommand
    {
        public int storageSlot;
        public int count;
    }

    [Serializable]
    public class ShopSetHoursCommand : WorldCommand
    {
        public int openHour;
        public int closeHour;
    }

    /// <summary>家具職人に改装を依頼する（翌朝に完成）。</summary>
    [Serializable]
    public class ShopOrderUpgradeCommand : WorldCommand
    {
        public string upgradeId;
        public string carpenterId;
    }

    /// <summary>委託販売の売上・返品を受け取る。</summary>
    [Serializable]
    public class ShopCollectSettlementCommand : WorldCommand
    {
    }

    public sealed partial class ShopSystem
    {
        private void RegisterCommands()
        {
            WorldAuthority.Register<ShopStockCommand>(HandleStock);
            WorldAuthority.Register<ShopUnstockCommand>(HandleUnstock);
            WorldAuthority.Register<ShopSetPriceCommand>(HandleSetPrice);
            WorldAuthority.Register<ShopSetAutoRestockCommand>(HandleAutoRestock);
            WorldAuthority.Register<ShopSwapSlotsCommand>(HandleSwap);
            WorldAuthority.Register<ShopStorageDepositCommand>(HandleDeposit);
            WorldAuthority.Register<ShopStorageWithdrawCommand>(HandleWithdraw);
            WorldAuthority.Register<ShopSetHoursCommand>(HandleSetHours);
            WorldAuthority.Register<ShopOrderUpgradeCommand>(HandleOrderUpgrade);
            WorldAuthority.Register<ShopCollectSettlementCommand>(HandleCollect);
        }

        /// <summary>店主側の操作（星の持ち主、または共同生活の権限）。</summary>
        private bool IsShopManager(CommandContext ctx) => ctx.IssuerId == HostId || ctx.HasPermission(PermissionLevel.CoLiving);

        private static CommandResult NoPermission() => CommandResult.Fail("この操作をする権限がありません", "You don't have permission to do that");

        private CommandResult HandleStock(ShopStockCommand cmd, CommandContext ctx)
        {
            var slot = GetSlot(cmd.slotIndex);
            if (slot == null) return CommandResult.Fail("その棚はありません", "No such shelf");
            var def = ItemRegistry.Get(cmd.itemId);
            if (def == null || !ShopRules.IsSellable(def)) return CommandResult.Fail("それは売り物にできません", "That can't be sold");
            if (!ShopRules.CanDisplay(slot.display, def))
                return CommandResult.Fail(ShopRules.CannotDisplayReasonJa(slot.display), ShopRules.CannotDisplayReasonEn(slot.display));
            bool manager = IsShopManager(ctx);
            if (cmd.fromStorage && !manager) return NoPermission();
            if (!cmd.fromStorage && !manager && !ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            string owner = cmd.fromStorage || ctx.IssuerId == HostId ? HostId : ctx.IssuerId;

            // 取り出す元を確認（品質・データまで一致させる）
            ItemStack source = null;
            if (cmd.fromStorage)
            {
                source = Data.storage.GetSlot(cmd.sourceSlot);
                if (source == null || source.IsEmpty || source.itemId != cmd.itemId || source.quality != cmd.quality)
                    return CommandResult.Fail("倉庫にその商品がありません", "That item is not in storage");
            }
            else if (ctx.IsLocalIssuer)
            {
                source = ctx.Issuer.Inventory.GetSlot(cmd.sourceSlot);
                if (source == null || source.IsEmpty || source.itemId != cmd.itemId || source.quality != cmd.quality)
                    return CommandResult.Fail("持ち物にその商品がありません", "You don't have that item");
            }

            if (!slot.IsEmpty)
            {
                bool sameOwner = slot.ownerId == owner || (IsHostOwner(slot.ownerId) && IsHostOwner(owner));
                bool same = slot.goods.itemId == cmd.itemId && slot.goods.quality == cmd.quality && sameOwner &&
                            (source == null || (slot.goods.data ?? "") == (source.data ?? ""));
                if (!same) return CommandResult.Fail("その棚には別の商品が並んでいます", "Another item is already on that shelf");
            }
            int cap = ShopConfig.MaxPerSlot(slot.display) - slot.Count;
            int count = Mathf.Min(cmd.count, cap);
            if (source != null) count = Mathf.Min(count, source.count);
            if (count <= 0) return CommandResult.Fail("棚がいっぱいです", "The shelf is full");

            ItemStack taken;
            if (cmd.fromStorage) taken = Data.storage.TakeFromSlot(cmd.sourceSlot, count, "shop_stock");
            else if (ctx.IsLocalIssuer) taken = ctx.Issuer.Inventory.TakeFromSlot(cmd.sourceSlot, count, "shop_stock");
            else
            {
                // リモートのゲスト：品質はゲスト側の事前確認を信頼する（コアの TakeItem は品質を指定できないため）
                if (!ctx.HasItem(cmd.itemId, count)) return CommandResult.Fail("持ち物にその商品がありません", "You don't have that item");
                if (!ctx.TakeItem(cmd.itemId, count, "shop_consign")) return CommandResult.Fail("持ち物から取り出せませんでした", "Couldn't take the item");
                taken = new ItemStack(cmd.itemId, count, cmd.quality);
            }
            if (taken == null || taken.IsEmpty) return CommandResult.Fail("取り出せませんでした", "Couldn't take the item");

            bool wasEmpty = slot.IsEmpty;
            AddToSlot(slot, taken);
            slot.ownerId = owner;
            if (cmd.price > 0) slot.price = Mathf.Clamp(cmd.price, 1, ShopConfig.MaxPrice);
            else if (wasEmpty || slot.price <= 0) slot.price = ShopRules.SuggestedPrice(def, cmd.quality);
            if (wasEmpty) slot.autoRestock = false;
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleUnstock(ShopUnstockCommand cmd, CommandContext ctx)
        {
            var slot = GetSlot(cmd.slotIndex);
            if (slot == null || slot.IsEmpty) return CommandResult.Fail("その棚には何もありません", "That shelf is empty");
            bool manager = IsShopManager(ctx);
            string owner = IsHostOwner(slot.ownerId) ? HostId : slot.ownerId;
            bool isOwner = ctx.IssuerId == owner;
            int count = Mathf.Clamp(cmd.count <= 0 ? slot.goods.count : cmd.count, 1, slot.goods.count);
            var stack = slot.goods.CloneWithCount(count);
            stack.ownerId = null;

            if (IsHostOwner(owner))
            {
                if (!manager && !isOwner) return NoPermission();
                if (cmd.toStorage)
                {
                    int left = Data.storage.Add(stack, "shop_unstock");
                    count -= left;
                    if (count <= 0) return CommandResult.Fail("倉庫がいっぱいです", "Storage is full");
                }
                else ctx.GiveItem(stack, "shop_unstock");
            }
            else if (isOwner)
            {
                ctx.GiveItem(stack, "shop_unstock");
            }
            else if (manager)
            {
                // 店主が委託品を下げた → 持ち主への返品として預かる（次に来た時に受け取れる）
                Data.Settlement(owner, true).returnedItems.Add(stack);
                ctx.Notify("委託品を下げ、持ち主への返品として預かりました", "Consigned goods were set aside for their owner");
            }
            else return NoPermission();

            slot.goods.count -= count;
            if (slot.goods.count <= 0)
            {
                slot.goods = new ItemStack();
                slot.autoRestock = false;
                slot.lastItemId = null;
            }
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleSetPrice(ShopSetPriceCommand cmd, CommandContext ctx)
        {
            var slot = GetSlot(cmd.slotIndex);
            if (slot == null || slot.IsEmpty) return CommandResult.Fail("その棚には何もありません", "That shelf is empty");
            string owner = IsHostOwner(slot.ownerId) ? HostId : slot.ownerId;
            bool ok = ctx.IssuerId == owner || (IsHostOwner(owner) && IsShopManager(ctx));
            if (!ok) return NoPermission();
            slot.price = Mathf.Clamp(cmd.price, 1, ShopConfig.MaxPrice);
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleAutoRestock(ShopSetAutoRestockCommand cmd, CommandContext ctx)
        {
            var slot = GetSlot(cmd.slotIndex);
            if (slot == null) return CommandResult.Fail("その棚はありません", "No such shelf");
            if (!IsShopManager(ctx)) return NoPermission();
            if (Data.Level(ShopUpgradeIds.AutoRestock) <= 0) return CommandResult.Fail("補充の仕掛けがありません", "No restock rig yet");
            if (cmd.on && (slot.IsEmpty || !IsHostOwner(slot.ownerId))) return CommandResult.Fail("店の商品が並んでいる棚だけ設定できます", "Only shelves with shop goods");
            slot.autoRestock = cmd.on;
            if (cmd.on) AutoRestockAll();
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleSwap(ShopSwapSlotsCommand cmd, CommandContext ctx)
        {
            var a = GetSlot(cmd.a);
            var b = GetSlot(cmd.b);
            if (a == null || b == null || cmd.a == cmd.b) return CommandResult.Fail("その棚はありません", "No such shelf");
            if (!IsShopManager(ctx)) return NoPermission();
            if (!a.IsEmpty && !ShopRules.CanDisplay(b.display, ItemRegistry.Get(a.goods.itemId)))
                return CommandResult.Fail(ShopRules.CannotDisplayReasonJa(b.display), ShopRules.CannotDisplayReasonEn(b.display));
            if (!b.IsEmpty && !ShopRules.CanDisplay(a.display, ItemRegistry.Get(b.goods.itemId)))
                return CommandResult.Fail(ShopRules.CannotDisplayReasonJa(a.display), ShopRules.CannotDisplayReasonEn(a.display));
            var tmp = new ShelfSlotData();
            CopyContents(a, tmp);
            CopyContents(b, a);
            CopyContents(tmp, b);
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private static void CopyContents(ShelfSlotData from, ShelfSlotData to)
        {
            to.goods = from.goods;
            to.price = from.price;
            to.ownerId = from.ownerId;
            to.listedDay = from.listedDay;
            to.autoRestock = from.autoRestock;
            to.soldTotal = from.soldTotal;
            to.lastItemId = from.lastItemId;
            to.lastQuality = from.lastQuality;
            to.lastData = from.lastData;
        }

        private CommandResult HandleDeposit(ShopStorageDepositCommand cmd, CommandContext ctx)
        {
            if (!IsShopManager(ctx)) return NoPermission();
            var def = ItemRegistry.Get(cmd.itemId);
            if (def == null || cmd.count <= 0) return CommandResult.Fail("入れられません", "Can't store that");
            ItemStack stack;
            if (ctx.IsLocalIssuer)
            {
                var src = ctx.Issuer.Inventory.GetSlot(cmd.inventorySlot);
                if (src == null || src.IsEmpty || src.itemId != cmd.itemId || src.quality != cmd.quality)
                    return CommandResult.Fail("持ち物にその物がありません", "You don't have that item");
                int n = Mathf.Min(cmd.count, src.count);
                var probe = src.CloneWithCount(n);
                probe.ownerId = null;
                if (Data.storage.SpaceFor(probe) < n) return CommandResult.Fail("倉庫がいっぱいです", "Storage is full");
                stack = ctx.Issuer.Inventory.TakeFromSlot(cmd.inventorySlot, n, "shop_storage");
            }
            else
            {
                var probe = new ItemStack(cmd.itemId, cmd.count, cmd.quality);
                if (Data.storage.SpaceFor(probe) < cmd.count) return CommandResult.Fail("倉庫がいっぱいです", "Storage is full");
                if (!ctx.TakeItem(cmd.itemId, cmd.count, "shop_storage")) return CommandResult.Fail("持ち物から取り出せませんでした", "Couldn't take the item");
                stack = probe;
            }
            if (stack == null) return CommandResult.Fail("取り出せませんでした", "Couldn't take the item");
            stack.ownerId = null;
            int left = Data.storage.Add(stack, "shop_storage");
            if (left > 0) ctx.GiveItem(stack.CloneWithCount(left), "shop_storage_overflow");
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleWithdraw(ShopStorageWithdrawCommand cmd, CommandContext ctx)
        {
            if (!IsShopManager(ctx)) return NoPermission();
            var s = Data.storage.GetSlot(cmd.storageSlot);
            if (s == null || s.IsEmpty) return CommandResult.Fail("そこには何もありません", "Nothing there");
            var taken = Data.storage.TakeFromSlot(cmd.storageSlot, cmd.count <= 0 ? s.count : cmd.count, "shop_storage_out");
            if (taken == null) return CommandResult.Fail("取り出せませんでした", "Couldn't take the item");
            ctx.GiveItem(taken, "shop_storage_out");
            AfterStateChanged();
            return CommandResult.Ok();
        }

        internal int EarliestOpenHour => ShopConfig.EarliestOpenByLights[Mathf.Clamp(Data.Level(ShopUpgradeIds.Lights), 0, ShopConfig.EarliestOpenByLights.Length - 1)];
        internal int LatestCloseHour => ShopConfig.LatestCloseByLights[Mathf.Clamp(Data.Level(ShopUpgradeIds.Lights), 0, ShopConfig.LatestCloseByLights.Length - 1)];

        private CommandResult HandleSetHours(ShopSetHoursCommand cmd, CommandContext ctx)
        {
            if (!IsShopManager(ctx)) return NoPermission();
            int open = Mathf.Clamp(cmd.openHour, EarliestOpenHour, LatestCloseHour - ShopConfig.MinOpenSpanHours);
            int close = Mathf.Clamp(cmd.closeHour, open + ShopConfig.MinOpenSpanHours, LatestCloseHour);
            Data.openHour = open;
            Data.closeHour = close;
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleOrderUpgrade(ShopOrderUpgradeCommand cmd, CommandContext ctx)
        {
            if (!IsShopManager(ctx)) return NoPermission();
            var def = ShopConfig.GetUpgrade(cmd.upgradeId);
            if (def == null) return CommandResult.Fail("その改装はありません", "Unknown upgrade");
            int level = Data.Level(def.Id);
            int pendingSame = 0;
            foreach (var p in Data.pendingUpgrades) if (p.upgradeId == def.Id) pendingSame++;
            if (level + pendingSame >= def.MaxLevel) return CommandResult.Fail("これ以上は改装できません", "Already at the maximum");
            int cost = def.CostForNextLevel(level + pendingSame);
            if (cost < 0) return CommandResult.Fail("これ以上は改装できません", "Already at the maximum");
            if (!ctx.TryCharge(cost, "shop_upgrade:" + def.Id)) return CommandResult.Fail("お金が足りません", "Not enough money");

            var dir = Game.Service<IResidentDirectory>();
            string carpenterId = cmd.carpenterId;
            if (string.IsNullOrEmpty(carpenterId)) carpenterId = dir?.FindByJob(ResidentJob.Carpenter)?.Id;
            if (dir != null && !string.IsNullOrEmpty(carpenterId))
            {
                // 改装費の一部は家具職人の収入になる（住民の財布と経済循環：バランス §7.6）
                dir.GiveMoney(carpenterId, Mathf.RoundToInt(cost * ShopConfig.CarpenterShare), "shop_upgrade_work");
                dir.AddMemory(carpenterId, new ResidentMemoryEntry { kind = "helped", playerId = ctx.IssuerId, day = Today, importance = 0.4f, detail = "shop_upgrade:" + def.Id });
                var u = new Utterance(carpenterId, "shop.upgrade_order")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(Concepts.FURNITURE).When(Concepts.TOMORROW).Future())
                    .Face(Expression.Happy)
                    .With(Gesture.Nod)
                    .With(Gesture.PointAt, "shop");
                dir.Speak(carpenterId, u, SpeechChannel.Direct);
            }
            Data.pendingUpgrades.Add(new PendingUpgradeData
            {
                upgradeId = def.Id,
                readyDay = Today + 1,
                carpenterId = carpenterId,
                orderedBy = ctx.IssuerId,
                cost = cost
            });
            ctx.Notify(string.Format("「{0}」を依頼しました。明日の朝に完成します", def.NameJa), string.Format("Ordered \"{0}\". It will be ready tomorrow morning", def.NameEn));
            AfterStateChanged();
            return CommandResult.Ok();
        }

        private CommandResult HandleCollect(ShopCollectSettlementCommand cmd, CommandContext ctx)
        {
            var st = Data.Settlement(ctx.IssuerId, false);
            if (st == null || st.IsEmpty) return CommandResult.Fail("受け取る物はありません", "Nothing to collect");
            int money = st.money;
            if (money > 0) ctx.PayMoney(money, "shop_consignment");
            foreach (var it in st.returnedItems) if (it != null && !it.IsEmpty) ctx.GiveItem(it, "shop_consignment_return");
            Data.settlements.Remove(st);
            ctx.Notify(string.Format("委託販売の売上 {0} を受け取りました", Wallet.Format(money)), string.Format("Collected {0} from consignment sales", Wallet.Format(money)));
            AfterStateChanged();
            return CommandResult.Ok();
        }
    }
}
