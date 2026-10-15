using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.UI
{
    public class ChoiceOption
    {
        public string Label;
        public Action OnSelect;
        public bool Enabled = true;

        public ChoiceOption(string label, Action onSelect, bool enabled = true)
        {
            Label = label;
            OnSelect = onSelect;
            Enabled = enabled;
        }
    }

    /// <summary>選択肢ダイアログ（確認・メニュー）。</summary>
    public class ChoiceDialog : UIWindow
    {
        private readonly string title;
        private readonly string message;
        private readonly List<ChoiceOption> options;

        public ChoiceDialog(string title, string message, List<ChoiceOption> options)
        {
            this.title = title;
            this.message = message;
            this.options = options ?? new List<ChoiceOption>();
        }

        public override string Title => title;
        public override Vector2 Size => new Vector2(640, Mathf.Min(900, 200 + (string.IsNullOrEmpty(message) ? 0 : 80) + options.Count * 52));

        public override void DrawContents(Rect content)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, UIStyles.LabelWrap);
            GUILayout.Space(8);
            foreach (var o in options)
            {
                GUI.enabled = o.Enabled;
                if (GUILayout.Button(o.Label, UIStyles.Button, GUILayout.Height(44)))
                {
                    Close();
                    o.OnSelect?.Invoke();
                }
                GUI.enabled = true;
            }
            GUILayout.EndScrollView();
        }
    }

    /// <summary>文字入力ダイアログ（辞書のメモ・名前入力など）。</summary>
    public class TextInputDialog : UIWindow
    {
        private readonly string title;
        private readonly string message;
        private readonly Action<string> onDone;
        private readonly bool multiline;
        private string text;

        public TextInputDialog(string title, string message, string initial, Action<string> onDone, bool multiline)
        {
            this.title = title;
            this.message = message;
            this.onDone = onDone;
            this.multiline = multiline;
            text = initial ?? "";
        }

        public override string Title => title;
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(720, multiline ? 520 : 300);

        public override void DrawContents(Rect content)
        {
            if (!string.IsNullOrEmpty(message)) GUILayout.Label(message, UIStyles.LabelWrap);
            GUI.SetNextControlName("TextInputDialogField");
            if (multiline) text = GUILayout.TextArea(text, UIStyles.TextArea, GUILayout.Height(260));
            else text = GUILayout.TextField(text, UIStyles.TextField, GUILayout.Height(42));
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("決定", "OK"), UIStyles.Button, GUILayout.Height(44)))
            {
                Close();
                onDone?.Invoke(text);
            }
            if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(44))) Close();
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>持ち物からアイテムを1つ選ぶ（贈り物・棚に並べる・納品・調理材料など）。</summary>
    public class ItemPickerWindow : UIWindow
    {
        private readonly PlayerState player;
        private readonly string title;
        private readonly Func<ItemStack, bool> filter;
        private readonly Action<int, ItemStack> onPicked;

        public ItemPickerWindow(PlayerState player, string title, Func<ItemStack, bool> filter, Action<int, ItemStack> onPicked)
        {
            this.player = player;
            this.title = title;
            this.filter = filter;
            this.onPicked = onPicked;
        }

        public override string Title => title;
        public override Vector2 Size => new Vector2(820, 640);

        public override void DrawContents(Rect content)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            bool any = false;
            var slots = player.Inventory.slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty) continue;
                if (filter != null && !filter(s)) continue;
                any = true;
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
                UIStyles.ItemIcon(r, s);
                GUILayout.BeginVertical();
                GUILayout.Label($"{s.DisplayName} ×{s.count}", UIStyles.Label);
                GUILayout.Label(s.Definition.Description, UIStyles.LabelDim);
                GUILayout.EndVertical();
                if (GUILayout.Button(Loc.T("これにする", "Choose"), UIStyles.Button, GUILayout.Width(160), GUILayout.Height(48)))
                {
                    int index = i;
                    var stack = s;
                    Close();
                    onPicked?.Invoke(index, stack);
                }
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("選べる物を持っていません。", "You have nothing suitable."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }
    }

    /// <summary>持ち物画面。</summary>
    public class InventoryWindow : UIWindow
    {
        private readonly PlayerState player;
        private int selected = -1;

        public InventoryWindow(PlayerState player) { this.player = player; }

        public override string Title => Loc.T("持ち物", "Inventory");
        public override Vector2 Size => new Vector2(960, 720);

        public override void DrawContents(Rect content)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("所持金：{0}", "Money: {0}", UIStyles.Money(player.Wallet.money)), UIStyles.LabelBold);
            GUILayout.FlexibleSpace();
            GUILayout.Label(Loc.Format("空き {0}/{1}", "Free {0}/{1}", player.Inventory.FreeSlots(), player.Inventory.capacity), UIStyles.LabelDim);
            GUILayout.EndHorizontal();

            const int cols = 10;
            const float cell = 80;
            var slots = player.Inventory.slots;
            int rows = Mathf.CeilToInt(slots.Count / (float)cols);
            var gridRect = GUILayoutUtility.GetRect(cols * cell, rows * cell);
            for (int i = 0; i < slots.Count; i++)
            {
                var r = new Rect(gridRect.x + (i % cols) * cell, gridRect.y + (i / cols) * cell, cell - 6, cell - 6);
                UIStyles.ItemIcon(r, slots[i], i == selected || i == player.SelectedSlot);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    if (selected >= 0 && selected != i)
                    {
                        player.Inventory.Swap(selected, i);
                        selected = -1;
                    }
                    else selected = i == selected ? -1 : i;
                }
            }

            GUILayout.Space(12);
            if (selected >= 0)
            {
                var s = player.Inventory.GetSlot(selected);
                if (s != null && !s.IsEmpty)
                {
                    var def = s.Definition;
                    GUILayout.Label($"{s.DisplayName} ×{s.count}", UIStyles.LabelBold);
                    GUILayout.Label(def.Description, UIStyles.LabelWrap);
                    GUILayout.Label(Loc.Format("目安の値段：{0}", "Reference price: {0}", UIStyles.Money(def.BasePrice)), UIStyles.LabelDim);
                    if (s.size > 0) GUILayout.Label(Loc.Format("大きさ：{0:0.0}", "Size: {0:0.0}", s.size), UIStyles.LabelDim);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(Loc.T("手に持つ", "Hold"), UIStyles.Button, GUILayout.Width(180)))
                    {
                        player.SelectedSlot = selected;
                        player.Tools.equipped = ToolType.Hand;
                    }
                    if (GUILayout.Button(Loc.T("足元に置く", "Drop"), UIStyles.Button, GUILayout.Width(180)))
                    {
                        var taken = player.Inventory.TakeFromSlot(selected, s.count, "drop");
                        if (taken != null) DroppedItem.Spawn(taken, player.Position + player.Forward);
                        selected = -1;
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Label(Loc.T("※ 別のマスをクリックすると入れ替えます。", "Click another slot to swap."), UIStyles.LabelDim);
                }
            }
            else
            {
                GUILayout.Label(Loc.T("マスを選ぶと詳細を表示します。", "Select a slot to see details."), UIStyles.LabelDim);
            }
        }
    }

    /// <summary>住民の店などで物を買う汎用画面（種屋・修理屋の部品・家具職人など）。</summary>
    public class VendorEntry
    {
        public string ItemId;
        public int Price;
        /// <summary>-1 は無制限。</summary>
        public int Stock = -1;
        public int Quality;
        public bool Locked;
        public string LockedReasonJa;
        public string LockedReasonEn;
        /// <summary>購入時の処理を差し替える（null なら持ち物に追加）。</summary>
        public Action<PlayerState, int> CustomPurchase;
    }

    public class VendorWindow : UIWindow
    {
        private readonly string title;
        private readonly PlayerState player;
        private readonly List<VendorEntry> entries;
        private readonly Action<VendorEntry, int> onPurchased;
        private readonly Dictionary<VendorEntry, int> amounts = new Dictionary<VendorEntry, int>();

        public VendorWindow(string title, PlayerState player, List<VendorEntry> entries, Action<VendorEntry, int> onPurchased = null)
        {
            this.title = title;
            this.player = player;
            this.entries = entries;
            this.onPurchased = onPurchased;
        }

        public override string Title => title;
        public override Vector2 Size => new Vector2(900, 700);

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.Format("所持金：{0}", "Money: {0}", UIStyles.Money(player.Wallet.money)), UIStyles.LabelBold);
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var e in entries)
            {
                var def = ItemRegistry.GetOrPlaceholder(e.ItemId);
                if (!amounts.ContainsKey(e)) amounts[e] = 1;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var r = GUILayoutUtility.GetRect(56, 56, GUILayout.Width(56), GUILayout.Height(56));
                UIStyles.ItemIcon(r, new ItemStack(e.ItemId, 1, e.Quality));
                GUILayout.BeginVertical();
                GUILayout.Label(def.Name + (e.Stock >= 0 ? Loc.Format("（残り{0}）", " (left {0})", e.Stock) : ""), UIStyles.Label);
                GUILayout.Label(e.Locked ? Loc.T(e.LockedReasonJa, e.LockedReasonEn) : def.Description, UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.Label(UIStyles.Money(e.Price), UIStyles.LabelBold, GUILayout.Width(120));
                int amount = amounts[e];
                if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(36))) amount = Mathf.Max(1, amount - 1);
                GUILayout.Label(amount.ToString(), UIStyles.LabelCenter, GUILayout.Width(40));
                if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(36))) amount = Mathf.Min(e.Stock >= 0 ? Mathf.Max(1, e.Stock) : 99, amount + 1);
                amounts[e] = amount;
                int total = e.Price * amount;
                GUI.enabled = !e.Locked && player.Wallet.CanAfford(total) && (e.Stock < 0 || e.Stock >= amount);
                if (GUILayout.Button(Loc.T("買う", "Buy"), UIStyles.Button, GUILayout.Width(100), GUILayout.Height(48)))
                {
                    if (player.Wallet.TrySpend(total, "vendor:" + e.ItemId))
                    {
                        if (e.CustomPurchase != null) e.CustomPurchase(player, amount);
                        else player.GiveOrDrop(new ItemStack(e.ItemId, amount, e.Quality), "vendor");
                        if (e.Stock >= 0) e.Stock -= amount;
                        onPurchased?.Invoke(e, amount);
                        UIManager.Toast(Loc.Format("{0}を{1}個買いました", "Bought {1} x {0}", def.Name, amount));
                    }
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }

    /// <summary>身振りメニュー（主人公は話さず、身振りで意思を示す）。</summary>
    public class GestureMenu : UIWindow
    {
        private readonly PlayerState player;
        private readonly Action<PlayerGesture> onGesture;

        public GestureMenu(PlayerState player, Action<PlayerGesture> onGesture)
        {
            this.player = player;
            this.onGesture = onGesture;
        }

        public override string Title => Loc.T("身振り", "Gestures");
        public override Vector2 Size => new Vector2(620, 460);

        public override void DrawContents(Rect content)
        {
            var values = (PlayerGesture[])Enum.GetValues(typeof(PlayerGesture));
            int cols = 3;
            for (int i = 0; i < values.Length; i += cols)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(values.Length, i + cols); j++)
                {
                    var g = values[j];
                    if (GUILayout.Button(PlayerGestureUtil.Label(g), UIStyles.Button, GUILayout.Height(56)))
                    {
                        Close();
                        onGesture?.Invoke(g);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }
    }
}
