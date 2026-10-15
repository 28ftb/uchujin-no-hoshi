using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Shop
{
    /// <summary>商店の画面を開く入口。</summary>
    internal static class ShopWindows
    {
        private static ShopManageWindow manage;

        public static bool IsManager(PlayerState p)
        {
            var shop = ShopSystem.Instance;
            if (shop == null || p == null) return false;
            return p.PlayerId == shop.HostId || p.Permission >= PermissionLevel.CoLiving;
        }

        public static void OpenManage(PlayerState p, ShopManageWindow.Tab tab)
        {
            var shop = ShopSystem.Instance;
            if (shop == null || p == null) return;
            if (manage == null) manage = new ShopManageWindow(shop, p);
            manage.SetPlayer(p);
            manage.tab = IsManager(p) || tab == ShopManageWindow.Tab.Records ? tab : ShopManageWindow.Tab.Shelves;
            UIManager.Open(manage);
        }

        public static void OpenMessages(PlayerState p)
        {
            var shop = ShopSystem.Instance;
            if (shop == null) return;
            UIManager.Open(new ShopMessagesWindow(shop));
        }

        public static void OpenRequestBoard(PlayerState p)
        {
            var shop = ShopSystem.Instance;
            if (shop == null) return;
            UIManager.Open(new ShopRequestBoardWindow(shop));
        }

        public static void OpenUpgrades(PlayerState p, string carpenterId)
        {
            var shop = ShopSystem.Instance;
            if (shop == null) return;
            UIManager.Open(new ShopUpgradeWindow(shop, p, carpenterId));
        }

        /// <summary>棚の前で [E]：その枠への操作。</summary>
        public static void OpenSlotMenu(PlayerState p, int index)
        {
            var shop = ShopSystem.Instance;
            var slot = shop != null ? shop.GetSlot(index) : null;
            if (slot == null || p == null) return;
            bool manager = IsManager(p);
            string owner = shop.IsHostOwner(slot.ownerId) ? shop.HostId : slot.ownerId;
            bool mine = owner == p.PlayerId || (manager && owner == shop.HostId);
            var opts = new List<ChoiceOption>();
            string title = ShopRules.DisplayLabel(slot.display) + " " + (index + 1);
            string msg;
            if (slot.IsEmpty)
            {
                msg = Loc.T("空いています。", "This slot is empty.");
                bool canConsign = manager || p.Permission >= PermissionLevel.Helper;
                opts.Add(new ChoiceOption(manager ? Loc.T("持ち物から並べる", "Put out from inventory") : Loc.T("持ち物を委託して並べる", "Consign from inventory"),
                    () => PickFromInventory(p, index), canConsign));
                if (manager) opts.Add(new ChoiceOption(Loc.T("倉庫から並べる", "Put out from storage"), () => PickFromStorage(p, index)));
            }
            else
            {
                var def = ItemRegistry.GetOrPlaceholder(slot.goods.itemId);
                msg = Loc.Format("{0} ×{1}　{2}（目安 {3}）\n{4}", "{0} x{1}  {2} (ref {3})\n{4}",
                    slot.goods.DisplayName, slot.goods.count, Wallet.Format(slot.price), Wallet.Format(ShopRules.SuggestedPrice(def, slot.goods.quality)), shop.OwnerLabel(slot.ownerId));
                opts.Add(new ChoiceOption(Loc.T("値段を変える", "Change price"), () => UIManager.Open(new ShopPriceDialog(shop, index)), mine));
                opts.Add(new ChoiceOption(Loc.T("持ち物から補充する", "Add more from inventory"), () => PickFromInventory(p, index),
                    mine && p.Inventory.CountWhere(s => s.itemId == slot.goods.itemId && s.quality == slot.goods.quality) > 0));
                if (manager && owner == shop.HostId)
                    opts.Add(new ChoiceOption(Loc.T("倉庫から補充する", "Add more from storage"), () => PickFromStorage(p, index)));
                opts.Add(new ChoiceOption(owner == p.PlayerId || owner == shop.HostId ? Loc.T("下げる（持ち物へ）", "Take down (to inventory)") : Loc.T("下げる（持ち主へ返品）", "Take down (return to owner)"),
                    () => WorldAuthority.Submit(new ShopUnstockCommand { slotIndex = index, count = 0, toStorage = false }), mine || manager));
                if (manager && owner == shop.HostId)
                    opts.Add(new ChoiceOption(Loc.T("下げる（倉庫へ）", "Take down (to storage)"),
                        () => WorldAuthority.Submit(new ShopUnstockCommand { slotIndex = index, count = 0, toStorage = true })));
            }
            opts.Add(new ChoiceOption(Loc.T("店の管理を開く", "Open shop management"), () => OpenManage(p, ShopManageWindow.Tab.Shelves)));
            opts.Add(new ChoiceOption(Loc.T("閉じる", "Close"), null));
            UIManager.Choose(title, msg, opts);
        }

        public static void PickFromInventory(PlayerState p, int slotIndex)
        {
            var shop = ShopSystem.Instance;
            var slot = shop?.GetSlot(slotIndex);
            if (slot == null) return;
            UIManager.PickItem(p, Loc.T("並べる物を選ぶ", "Choose goods to put out"), s =>
            {
                var def = s.Definition;
                if (!ShopRules.CanDisplay(slot.display, def)) return false;
                if (!slot.IsEmpty && (s.itemId != slot.goods.itemId || s.quality != slot.goods.quality)) return false;
                return true;
            }, (invIndex, stack) => UIManager.Open(new ShopStockDialog(shop, slotIndex, false, invIndex, stack)));
        }

        public static void PickFromStorage(PlayerState p, int slotIndex)
        {
            var shop = ShopSystem.Instance;
            var slot = shop?.GetSlot(slotIndex);
            if (slot == null) return;
            var opts = new List<ChoiceOption>();
            var storage = shop.Data.storage;
            for (int i = 0; i < storage.slots.Count; i++)
            {
                var s = storage.slots[i];
                if (s == null || s.IsEmpty) continue;
                if (!ShopRules.CanDisplay(slot.display, s.Definition)) continue;
                if (!slot.IsEmpty && (s.itemId != slot.goods.itemId || s.quality != slot.goods.quality)) continue;
                int si = i;
                var snapshot = s.Clone();
                opts.Add(new ChoiceOption(string.Format("{0} ×{1}", s.DisplayName, s.count), () => UIManager.Open(new ShopStockDialog(shop, slotIndex, true, si, snapshot))));
            }
            if (opts.Count == 0)
            {
                UIManager.Message(Loc.T("倉庫", "Storage"), Loc.T("この棚に並べられる物が倉庫にありません。", "Nothing in storage fits this slot."));
                return;
            }
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(Loc.T("倉庫から選ぶ", "Choose from storage"), null, opts);
        }

        /// <summary>値段の入力（基準価格の目安つき）。戻り値は新しい値段。</summary>
        public static int PriceEditor(int price, int reference, ref string text)
        {
            GUILayout.Label(Loc.Format("目安：{0}（基準価格×品質。住民によって感じ方は違います）", "Reference: {0} (base price x quality; residents feel differently)", Wallet.Format(reference)), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-100", UIStyles.ButtonSmall)) price -= 100;
            if (GUILayout.Button("-10", UIStyles.ButtonSmall)) price -= 10;
            if (GUILayout.Button("-1", UIStyles.ButtonSmall)) price -= 1;
            GUILayout.Label(Wallet.Format(Mathf.Max(1, price)), UIStyles.LabelBold, GUILayout.Width(150));
            if (GUILayout.Button("+1", UIStyles.ButtonSmall)) price += 1;
            if (GUILayout.Button("+10", UIStyles.ButtonSmall)) price += 10;
            if (GUILayout.Button("+100", UIStyles.ButtonSmall)) price += 100;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("×0.8", UIStyles.ButtonSmall)) price = Mathf.RoundToInt(price * 0.8f);
            if (GUILayout.Button("×0.9", UIStyles.ButtonSmall)) price = Mathf.RoundToInt(price * 0.9f);
            if (GUILayout.Button("×1.1", UIStyles.ButtonSmall)) price = Mathf.RoundToInt(price * 1.1f);
            if (GUILayout.Button("×1.25", UIStyles.ButtonSmall)) price = Mathf.RoundToInt(price * 1.25f);
            if (GUILayout.Button(Loc.T("目安にする", "Use reference"), UIStyles.ButtonSmall)) price = reference;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("直接入力：", "Type: "), UIStyles.Label, GUILayout.Width(120));
            string newText = GUILayout.TextField(text ?? "", UIStyles.TextField, GUILayout.Width(160));
            if (newText != text)
            {
                text = newText;
                if (int.TryParse(newText, out var typed) && typed > 0) price = typed;
            }
            GUILayout.EndHorizontal();
            float ratio = price / (float)Mathf.Max(1, reference);
            GUILayout.Label(Loc.Format("目安の {0:0}%", "{0:0}% of reference", ratio * 100f), ratio > 1.3f ? UIStyles.LabelDim : UIStyles.LabelSmall);
            return Mathf.Clamp(price, 1, ShopConfig.MaxPrice);
        }
    }

    // =====================================================================
    // 店の管理（帳簿）：棚・倉庫・営業時間・改装・記録
    // =====================================================================
    internal sealed class ShopManageWindow : UIWindow
    {
        public enum Tab { Shelves, Storage, Hours, Upgrades, Records }

        private readonly ShopSystem shop;
        private PlayerState player;
        public Tab tab = Tab.Shelves;
        private readonly ShopReportView report;
        private int swapFrom = -1;
        private int editOpen = -1, editClose = -1;

        public ShopManageWindow(ShopSystem shop, PlayerState player)
        {
            this.shop = shop;
            this.player = player;
            report = new ShopReportView(shop);
        }

        public void SetPlayer(PlayerState p) => player = p;

        public override string Title => Loc.T("お店の帳簿", "Shop ledger");
        public override Vector2 Size => new Vector2(1180, 820);
        public override bool PausesTimeInSolo => true;

        public override void OnOpen()
        {
            editOpen = shop.Data.openHour;
            editClose = shop.Data.closeHour;
            swapFrom = -1;
        }

        public override void DrawContents(Rect content)
        {
            bool manager = ShopWindows.IsManager(player);
            GUILayout.BeginHorizontal();
            TabButton(Tab.Shelves, Loc.T("棚", "Shelves"));
            if (manager)
            {
                TabButton(Tab.Storage, Loc.T("倉庫", "Storage"));
                TabButton(Tab.Hours, Loc.T("営業時間", "Hours"));
                TabButton(Tab.Upgrades, Loc.T("改装", "Upgrades"));
            }
            TabButton(Tab.Records, Loc.T("売上・来店の記録", "Sales & visits"));
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.Format("所持金 {0}", "Money {0}", Wallet.Format(player.Wallet.money)), UIStyles.LabelBold);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            switch (tab)
            {
                case Tab.Shelves: DrawShelves(manager); break;
                case Tab.Storage: DrawStorage(); break;
                case Tab.Hours: DrawHours(); break;
                case Tab.Upgrades: ShopUpgradeWindow.DrawUpgrades(shop, player, null, ref scroll); break;
                case Tab.Records: report.Draw(); break;
            }
        }

        private void TabButton(Tab t, string label)
        {
            if (GUILayout.Button(label, tab == t ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42)))
            {
                tab = t;
                scroll = Vector2.zero;
            }
        }

        private void DrawShelves(bool manager)
        {
            GUILayout.Label(Loc.T("番号が小さい棚ほど入口に近く、住民の目に留まりやすい。", "Lower numbers are nearer the entrance and catch more eyes."), UIStyles.LabelDim);
            if (swapFrom >= 0) GUILayout.Label(Loc.Format("入れ替え先の棚を選んでください（{0}番から）", "Choose the slot to swap with (from {0})", swapFrom + 1), UIStyles.Label);
            scroll = GUILayout.BeginScrollView(scroll);
            bool autoAvailable = shop.Level(ShopUpgradeIds.AutoRestock) > 0;
            for (int i = 0; i < shop.Data.slots.Count; i++)
            {
                var s = shop.Data.slots[i];
                int idx = i;
                string owner = shop.IsHostOwner(s.ownerId) ? shop.HostId : s.ownerId;
                bool mine = owner == player.PlayerId || (manager && owner == shop.HostId);
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(string.Format("{0}. {1}", i + 1, ShopRules.DisplayLabel(s.display)), UIStyles.LabelSmall, GUILayout.Width(150));
                var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(r, s.IsEmpty ? null : s.goods);
                GUILayout.BeginVertical(GUILayout.Width(380));
                if (s.IsEmpty) GUILayout.Label(Loc.T("（空き）", "(empty)"), UIStyles.LabelDim);
                else
                {
                    var def = ItemRegistry.GetOrPlaceholder(s.goods.itemId);
                    GUILayout.Label(string.Format("{0} ×{1}　{2}", s.goods.DisplayName, s.goods.count, Wallet.Format(s.price)), UIStyles.Label);
                    GUILayout.Label(Loc.Format("目安 {0}・{1}・通算{2}個売れた", "ref {0} · {1} · {2} sold",
                        Wallet.Format(ShopRules.SuggestedPrice(def, s.goods.quality)), shop.OwnerLabel(s.ownerId), s.soldTotal), UIStyles.LabelDim);
                }
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (swapFrom >= 0)
                {
                    if (GUILayout.Button(swapFrom == i ? Loc.T("やめる", "Cancel") : Loc.T("ここと入れ替え", "Swap here"), UIStyles.ButtonSmall, GUILayout.Width(150)))
                    {
                        if (swapFrom != i) WorldAuthority.Submit(new ShopSwapSlotsCommand { a = swapFrom, b = idx });
                        swapFrom = -1;
                    }
                }
                else if (s.IsEmpty)
                {
                    bool canPut = manager || player.Permission >= PermissionLevel.Helper;
                    GUI.enabled = canPut;
                    if (GUILayout.Button(Loc.T("持ち物から並べる", "From inventory"), UIStyles.ButtonSmall, GUILayout.Width(170))) ShopWindows.PickFromInventory(player, idx);
                    GUI.enabled = true;
                    if (manager && GUILayout.Button(Loc.T("倉庫から", "From storage"), UIStyles.ButtonSmall, GUILayout.Width(120))) ShopWindows.PickFromStorage(player, idx);
                }
                else
                {
                    GUI.enabled = mine;
                    if (GUILayout.Button(Loc.T("値段", "Price"), UIStyles.ButtonSmall, GUILayout.Width(80))) UIManager.Open(new ShopPriceDialog(shop, idx));
                    if (GUILayout.Button(Loc.T("補充", "Add"), UIStyles.ButtonSmall, GUILayout.Width(80)))
                    {
                        if (manager && owner == shop.HostId)
                        {
                            UIManager.Choose(Loc.T("補充", "Add more"), null, new List<ChoiceOption>
                            {
                                new ChoiceOption(Loc.T("持ち物から", "From inventory"), () => ShopWindows.PickFromInventory(player, idx)),
                                new ChoiceOption(Loc.T("倉庫から", "From storage"), () => ShopWindows.PickFromStorage(player, idx)),
                                new ChoiceOption(Loc.T("やめる", "Cancel"), null)
                            });
                        }
                        else ShopWindows.PickFromInventory(player, idx);
                    }
                    GUI.enabled = mine || manager;
                    if (GUILayout.Button(Loc.T("下げる", "Take down"), UIStyles.ButtonSmall, GUILayout.Width(90)))
                        WorldAuthority.Submit(new ShopUnstockCommand { slotIndex = idx, count = 0, toStorage = false });
                    GUI.enabled = manager && owner == shop.HostId;
                    if (GUILayout.Button(Loc.T("倉庫へ", "To storage"), UIStyles.ButtonSmall, GUILayout.Width(100)))
                        WorldAuthority.Submit(new ShopUnstockCommand { slotIndex = idx, count = 0, toStorage = true });
                    GUI.enabled = true;
                    if (autoAvailable && manager && owner == shop.HostId)
                    {
                        bool on = GUILayout.Toggle(s.autoRestock, Loc.T("自動補充", "Auto"), UIStyles.Toggle, GUILayout.Width(120));
                        if (on != s.autoRestock) WorldAuthority.Submit(new ShopSetAutoRestockCommand { slotIndex = idx, on = on });
                    }
                }
                if (manager && swapFrom < 0 && GUILayout.Button(Loc.T("入替", "Swap"), UIStyles.ButtonSmall, GUILayout.Width(70))) swapFrom = idx;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void DrawStorage()
        {
            var storage = shop.Data.storage;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("倉庫箱　空き {0}/{1}", "Storage box  free {0}/{1}", storage.FreeSlots(), storage.capacity), UIStyles.LabelBold);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("持ち物から入れる", "Store from inventory"), UIStyles.Button, GUILayout.Width(240)))
            {
                UIManager.PickItem(player, Loc.T("倉庫に入れる物", "Choose what to store"), s => ShopRules.IsSellable(s.Definition),
                    (invIndex, stack) => UIManager.Open(new ShopDepositDialog(invIndex, stack)));
            }
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            bool any = false;
            for (int i = 0; i < storage.slots.Count; i++)
            {
                var s = storage.slots[i];
                if (s == null || s.IsEmpty) continue;
                any = true;
                int si = i;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(r, s);
                GUILayout.Label(string.Format("{0} ×{1}", s.DisplayName, s.count), UIStyles.Label, GUILayout.Width(380));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Loc.T("棚へ", "To shelf"), UIStyles.ButtonSmall, GUILayout.Width(110)))
                {
                    var snapshot = s.Clone();
                    var opts = new List<ChoiceOption>();
                    for (int k = 0; k < shop.Data.slots.Count; k++)
                    {
                        var slot = shop.Data.slots[k];
                        if (!ShopRules.CanDisplay(slot.display, snapshot.Definition)) continue;
                        if (!slot.IsEmpty && (slot.goods.itemId != snapshot.itemId || slot.goods.quality != snapshot.quality || !shop.IsHostOwner(slot.ownerId))) continue;
                        int ki = k;
                        opts.Add(new ChoiceOption(string.Format("{0}. {1} {2}", k + 1, ShopRules.DisplayLabel(slot.display), slot.IsEmpty ? Loc.T("（空き）", "(empty)") : "×" + slot.goods.count),
                            () => UIManager.Open(new ShopStockDialog(shop, ki, true, si, snapshot))));
                    }
                    if (opts.Count == 0) UIManager.Message(Loc.T("棚", "Shelves"), Loc.T("並べられる棚がありません。", "No slot can take it."));
                    else
                    {
                        opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
                        UIManager.Choose(Loc.T("どの棚に並べる？", "Which slot?"), null, opts);
                    }
                }
                if (GUILayout.Button(Loc.T("持ち物へ", "Take"), UIStyles.ButtonSmall, GUILayout.Width(110)))
                    WorldAuthority.Submit(new ShopStorageWithdrawCommand { storageSlot = si, count = 0 });
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("倉庫は空です。", "Storage is empty."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        private void DrawHours()
        {
            int earliest = shop.EarliestOpenHour, latest = shop.LatestCloseHour;
            GUILayout.Label(Loc.Format("いまの営業時間：{0}:00〜{1}:00（{2}）", "Current hours: {0}:00-{1}:00 ({2})",
                shop.Data.openHour, shop.Data.closeHour, shop.IsOpen ? Loc.T("営業中", "open") : Loc.T("準備中", "closed")), UIStyles.LabelBold);
            GUILayout.Label(Loc.Format("設定できる範囲：{0}:00〜{1}:00（店の灯りの改装で広がります）", "Allowed: {0}:00-{1}:00 (shop lights extend this)", earliest, latest), UIStyles.LabelDim);
            GUILayout.Label(Loc.T("店番はいりません。営業時間中は、あなたが出かけていても住民が買い物をします。", "No need to tend the shop: residents shop during opening hours even while you're away."), UIStyles.LabelDim);
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("開店", "Open"), UIStyles.Label, GUILayout.Width(100));
            if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(44))) editOpen = Mathf.Max(earliest, editOpen - 1);
            GUILayout.Label(editOpen + ":00", UIStyles.LabelBold, GUILayout.Width(90));
            if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(44))) editOpen = Mathf.Min(editClose - ShopConfig.MinOpenSpanHours, editOpen + 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("閉店", "Close"), UIStyles.Label, GUILayout.Width(100));
            if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(44))) editClose = Mathf.Max(editOpen + ShopConfig.MinOpenSpanHours, editClose - 1);
            GUILayout.Label(editClose + ":00", UIStyles.LabelBold, GUILayout.Width(90));
            if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(44))) editClose = Mathf.Min(latest, editClose + 1);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            if (GUILayout.Button(Loc.T("この時間にする", "Set these hours"), UIStyles.Button, GUILayout.Width(260), GUILayout.Height(46)))
                WorldAuthority.Submit(new ShopSetHoursCommand { openHour = editOpen, closeHour = editClose });
        }
    }

    /// <summary>並べる個数と値段を決める。</summary>
    internal sealed class ShopStockDialog : UIWindow
    {
        private readonly ShopSystem shop;
        private readonly int slotIndex;
        private readonly bool fromStorage;
        private readonly int sourceSlot;
        private readonly ItemStack stack;
        private int count;
        private int price;
        private string priceText = "";

        public ShopStockDialog(ShopSystem shop, int slotIndex, bool fromStorage, int sourceSlot, ItemStack stack)
        {
            this.shop = shop;
            this.slotIndex = slotIndex;
            this.fromStorage = fromStorage;
            this.sourceSlot = sourceSlot;
            this.stack = stack.Clone();
            var slot = shop.GetSlot(slotIndex);
            int cap = slot != null ? ShopConfig.MaxPerSlot(slot.display) - slot.Count : 0;
            count = Mathf.Max(1, Mathf.Min(stack.count, cap));
            price = slot != null && !slot.IsEmpty ? slot.price : ShopRules.SuggestedPrice(stack.Definition, stack.quality);
        }

        public override string Title => Loc.T("棚に並べる", "Put out goods");
        public override Vector2 Size => new Vector2(780, 520);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            var slot = shop.GetSlot(slotIndex);
            if (slot == null)
            {
                Close();
                return;
            }
            int cap = Mathf.Max(0, ShopConfig.MaxPerSlot(slot.display) - slot.Count);
            int max = Mathf.Max(1, Mathf.Min(stack.count, cap));
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
            UIStyles.ItemIcon(r, stack);
            GUILayout.Label(string.Format("{0}　→　{1}. {2}", stack.DisplayName, slotIndex + 1, ShopRules.DisplayLabel(slot.display)), UIStyles.LabelBold);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("個数", "Count"), UIStyles.Label, GUILayout.Width(100));
            if (GUILayout.Button("-10", UIStyles.ButtonSmall)) count -= 10;
            if (GUILayout.Button("-", UIStyles.ButtonSmall)) count -= 1;
            count = Mathf.Clamp(count, 1, max);
            GUILayout.Label(count + " / " + max, UIStyles.LabelBold, GUILayout.Width(110));
            if (GUILayout.Button("+", UIStyles.ButtonSmall)) count += 1;
            if (GUILayout.Button("+10", UIStyles.ButtonSmall)) count += 10;
            if (GUILayout.Button(Loc.T("全部", "All"), UIStyles.ButtonSmall)) count = max;
            count = Mathf.Clamp(count, 1, max);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            price = ShopWindows.PriceEditor(price, ShopRules.SuggestedPrice(stack.Definition, stack.quality), ref priceText);
            var local = Game.Instance.LocalPlayer;
            if (local != null && !shop.IsLocalHost && !fromStorage)
                GUILayout.Label(Loc.T("委託販売：売上は店に預けられ、あとで受け取れます。", "Consignment: the money is held at the shop for you to collect."), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUI.enabled = cap > 0;
            if (GUILayout.Button(Loc.T("並べる", "Put out"), UIStyles.Button, GUILayout.Height(48)))
            {
                WorldAuthority.Submit(new ShopStockCommand
                {
                    slotIndex = slotIndex,
                    fromStorage = fromStorage,
                    sourceSlot = sourceSlot,
                    itemId = stack.itemId,
                    quality = stack.quality,
                    count = count,
                    price = price
                });
                Close();
            }
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(48))) Close();
            GUILayout.EndHorizontal();
        }
    }

    internal sealed class ShopPriceDialog : UIWindow
    {
        private readonly ShopSystem shop;
        private readonly int slotIndex;
        private int price;
        private string text = "";

        public ShopPriceDialog(ShopSystem shop, int slotIndex)
        {
            this.shop = shop;
            this.slotIndex = slotIndex;
            var s = shop.GetSlot(slotIndex);
            price = s != null ? s.price : 1;
        }

        public override string Title => Loc.T("値段を変える", "Change price");
        public override Vector2 Size => new Vector2(760, 440);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            var s = shop.GetSlot(slotIndex);
            if (s == null || s.IsEmpty)
            {
                Close();
                return;
            }
            GUILayout.BeginHorizontal();
            var r = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
            UIStyles.ItemIcon(r, s.goods);
            GUILayout.Label(Loc.Format("{0} ×{1}　いま {2}", "{0} x{1}  now {2}", s.goods.DisplayName, s.goods.count, Wallet.Format(s.price)), UIStyles.LabelBold);
            GUILayout.EndHorizontal();
            price = ShopWindows.PriceEditor(price, ShopRules.SuggestedPrice(s.goods.Definition, s.goods.quality), ref text);
            GUILayout.Label(Loc.T("値段を変えると、住民の反応やマネピが覚える声も変わるかもしれません。", "Changing the price may change how residents react and what the Manepi hears."), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("この値段にする", "Set price"), UIStyles.Button, GUILayout.Height(48)))
            {
                WorldAuthority.Submit(new ShopSetPriceCommand { slotIndex = slotIndex, price = price });
                Close();
            }
            if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(48))) Close();
            GUILayout.EndHorizontal();
        }
    }

    internal sealed class ShopDepositDialog : UIWindow
    {
        private readonly int invIndex;
        private readonly ItemStack stack;
        private int count;

        public ShopDepositDialog(int invIndex, ItemStack stack)
        {
            this.invIndex = invIndex;
            this.stack = stack.Clone();
            count = stack.count;
        }

        public override string Title => Loc.T("倉庫に入れる", "Store");
        public override Vector2 Size => new Vector2(640, 300);

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(stack.DisplayName, UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(44))) count--;
            count = Mathf.Clamp(count, 1, stack.count);
            GUILayout.Label(count + " / " + stack.count, UIStyles.LabelBold, GUILayout.Width(120));
            if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(44))) count++;
            if (GUILayout.Button(Loc.T("全部", "All"), UIStyles.ButtonSmall, GUILayout.Width(80))) count = stack.count;
            count = Mathf.Clamp(count, 1, stack.count);
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("入れる", "Store"), UIStyles.Button, GUILayout.Height(46)))
            {
                WorldAuthority.Submit(new ShopStorageDepositCommand { inventorySlot = invIndex, itemId = stack.itemId, quality = stack.quality, count = count });
                Close();
            }
        }
    }

    /// <summary>要望ボード：形・色の絵、顔、数、マネピの再現。文字の訳は出さない。</summary>
    internal sealed class ShopRequestBoardWindow : UIWindow
    {
        private readonly ShopSystem shop;
        private readonly ShopReportView view;

        public ShopRequestBoardWindow(ShopSystem shop)
        {
            this.shop = shop;
            view = new ShopReportView(shop) { tab = ShopReportView.Tab.Requests };
        }

        public override string Title => Loc.T("要望ボード", "Request board");
        public override Vector2 Size => new Vector2(980, 720);

        public override void DrawContents(Rect content)
        {
            view.Draw(false);
        }
    }

    /// <summary>マネピの仕組みが無い時の代わり：商店の伝言を聞く。</summary>
    internal sealed class ShopMessagesWindow : UIWindow
    {
        private readonly ShopSystem shop;
        private readonly ShopReportView view;

        public ShopMessagesWindow(ShopSystem shop)
        {
            this.shop = shop;
            view = new ShopReportView(shop);
        }

        public override string Title => Loc.T("マネピの伝言", "Manepi messages");
        public override Vector2 Size => new Vector2(1080, 760);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            int n = 0;
            for (int i = shop.Data.visits.Count - 1; i >= 0 && n < 40; i--)
            {
                var v = shop.Data.visits[i];
                if (string.IsNullOrEmpty(v.recordId)) continue;
                n++;
                GUILayout.Label(Loc.Format("{0}日目", "Day {0}", v.day), UIStyles.LabelDim);
                view.DrawVisitRow(v);
            }
            if (n == 0) GUILayout.Label(Loc.T("マネピはまだ何も覚えていません。", "The Manepi hasn't learned anything yet."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            shop.MarkMessagesReviewed();
        }
    }

    /// <summary>家具職人への改装の依頼（住民に話しかけた時のメニューから開く）。</summary>
    internal sealed class ShopUpgradeWindow : UIWindow
    {
        private readonly ShopSystem shop;
        private readonly PlayerState player;
        private readonly string carpenterId;

        public ShopUpgradeWindow(ShopSystem shop, PlayerState player, string carpenterId)
        {
            this.shop = shop;
            this.player = player;
            this.carpenterId = carpenterId;
        }

        public override string Title => Loc.T("お店の改装を頼む", "Order shop upgrades");
        public override Vector2 Size => new Vector2(1000, 760);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.Format("所持金 {0}", "Money {0}", Wallet.Format(player.Wallet.money)), UIStyles.LabelBold);
            DrawUpgrades(shop, player, carpenterId, ref scroll);
        }

        /// <summary>
        /// 改装の一覧。carpenterId が null の時（店の帳簿から）は、家具職人がいれば「話しかけて頼む」案内だけを出す。
        /// 家具職人が村にいない時は、帳簿から直接注文できる（必ず遊べるようにする保険）。
        /// </summary>
        public static void DrawUpgrades(ShopSystem shop, PlayerState player, string carpenterId, ref Vector2 scroll)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            var carpenter = dir?.FindByJob(ResidentJob.Carpenter);
            bool canOrder = carpenterId != null || carpenter == null;
            if (carpenterId == null && carpenter != null)
            {
                GUILayout.BeginHorizontal();
                var fr = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                ShopIcons.Face(fr, carpenter.MainColor, carpenter.Lineage);
                GUILayout.Label(Loc.T("改装は家具職人に話しかけて頼みます。", "Talk to the carpenter to order upgrades."), UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            if (shop.Data.pendingUpgrades.Count > 0)
            {
                foreach (var p in shop.Data.pendingUpgrades)
                {
                    var d = ShopConfig.GetUpgrade(p.upgradeId);
                    if (d != null) GUILayout.Label(Loc.Format("工事中：{0}（{1}日目の朝に完成）", "In progress: {0} (ready on day {1})", d.Name, p.readyDay), UIStyles.LabelSmall);
                }
            }
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var def in ShopConfig.Upgrades)
            {
                int level = shop.Level(def.Id);
                int pending = 0;
                foreach (var p in shop.Data.pendingUpgrades) if (p.upgradeId == def.Id) pending++;
                int cost = def.CostForNextLevel(level + pending);
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.BeginVertical(GUILayout.Width(560));
                GUILayout.Label(string.Format("{0}　{1}/{2}", def.Name, level, def.MaxLevel), UIStyles.LabelBold);
                GUILayout.Label(def.Description, UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (cost < 0) GUILayout.Label(pending > 0 ? Loc.T("工事中", "In progress") : Loc.T("最大", "Max"), UIStyles.LabelDim, GUILayout.Width(150));
                else
                {
                    GUILayout.Label(Wallet.Format(cost), UIStyles.Label, GUILayout.Width(120));
                    GUI.enabled = canOrder && player.Wallet.CanAfford(cost) && ShopWindows.IsManager(player);
                    if (GUILayout.Button(Loc.T("依頼する", "Order"), UIStyles.ButtonSmall, GUILayout.Width(120)))
                        WorldAuthority.Submit(new ShopOrderUpgradeCommand { upgradeId = def.Id, carpenterId = carpenterId });
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }
}
