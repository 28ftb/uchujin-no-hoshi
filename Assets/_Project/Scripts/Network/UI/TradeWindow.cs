using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// 直接交換の画面（マルチ §12.2）。表示はホストが確定した状態（TradeStateMsg）をそのまま映す。
    /// 自分の提示を変えるとホストへ送られ、双方の「これでOK」は外れる。
    /// </summary>
    public sealed class TradeWindow : UIWindow
    {
        private readonly NetworkSystem net;
        private Vector2 scrollMine;
        private Vector2 scrollTheirs;
        internal bool SuppressCancelOnClose;

        public TradeWindow(NetworkSystem net)
        {
            this.net = net;
        }

        private TradeStateMsg S => net.CurrentTrade;
        private bool IsA => S != null && S.aId == Game.Instance.LocalPlayerId;

        public override string Title
        {
            get
            {
                var s = S;
                if (s == null) return Loc.T("交換", "Trade");
                return Loc.Format("{0}さんと交換", "Trade with {0}", IsA ? s.bName : s.aName);
            }
        }

        public override Vector2 Size => new Vector2(1100, 760);
        private bool Busy => S != null && S.phase == TradePhase.Depositing;
        public override bool CloseOnEscape => !Busy;
        public override bool ShowCloseButton => !Busy;

        public override void OnClose()
        {
            var s = S;
            if (!SuppressCancelOnClose && s != null && (s.phase == TradePhase.Inviting || s.phase == TradePhase.Negotiating))
                net.TradeCancel(s.tradeId);
            net.OnTradeWindowClosed(this);
        }

        public override void DrawContents(Rect content)
        {
            var s = S;
            if (s == null)
            {
                GUILayout.Label(Loc.T("交換は終わりました。", "The trade has ended."), UIStyles.LabelDim);
                if (GUILayout.Button(Loc.T("閉じる", "Close"), UIStyles.Button, GUILayout.Height(46))) Close();
                return;
            }
            var lp = Game.Instance.LocalPlayer;
            bool isA = IsA;
            var myItems = isA ? s.aItems : s.bItems;
            int myMoney = isA ? s.aMoney : s.bMoney;
            var theirItems = isA ? s.bItems : s.aItems;
            int theirMoney = isA ? s.bMoney : s.aMoney;
            bool myReady = isA ? s.aReady : s.bReady;
            bool theirReady = isA ? s.bReady : s.aReady;
            string partner = isA ? s.bName : s.aName;
            bool editable = s.phase == TradePhase.Negotiating;

            string msg = Loc.T(s.messageJa, s.messageEn);
            if (!string.IsNullOrEmpty(msg)) GUILayout.Label(msg, UIStyles.LabelBold);
            if (s.phase == TradePhase.Inviting)
            {
                GUILayout.Label(Loc.Format("{0}さんの返事を待っています…", "Waiting for {0}...", partner), UIStyles.Label);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(48))) Close();
                return;
            }

            GUILayout.BeginHorizontal();
            // ---- 自分の出す物 ----
            GUILayout.BeginVertical(UIStyles.Box, GUILayout.Width(520));
            GUILayout.Label(Loc.T("あなたが出す物", "You give"), UIStyles.LabelBold);
            scrollMine = GUILayout.BeginScrollView(scrollMine, GUILayout.Height(360));
            for (int i = 0; i < myItems.Count; i++)
            {
                var it = myItems[i];
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(r, it);
                GUILayout.Label($"{it.DisplayName} ×{it.count}", UIStyles.Label);
                GUILayout.FlexibleSpace();
                GUI.enabled = editable;
                if (GUILayout.Button("-", UIStyles.ButtonSmall, GUILayout.Width(40))) ChangeCount(myItems, myMoney, i, -1);
                if (GUILayout.Button("+", UIStyles.ButtonSmall, GUILayout.Width(40))) ChangeCount(myItems, myMoney, i, +1);
                if (GUILayout.Button("×", UIStyles.ButtonSmall, GUILayout.Width(40))) ChangeCount(myItems, myMoney, i, -it.count);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            if (myItems.Count == 0) GUILayout.Label(Loc.T("（品物なし）", "(no items)"), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUI.enabled = editable;
            if (GUILayout.Button(Loc.T("持ち物から加える", "Add from inventory"), UIStyles.Button, GUILayout.Height(44)))
            {
                UIManager.PickItem(lp, Loc.T("交換に出す物", "Item to offer"), st => st != null && !st.IsEmpty, (idx, stack) =>
                {
                    // 選んでいる間に提示が変わっているかもしれないので、選び終えた時点の状態から作る
                    var cur = S;
                    if (cur == null || cur.phase != TradePhase.Negotiating) return;
                    bool a = cur.aId == Game.Instance.LocalPlayerId;
                    var list = Copy(a ? cur.aItems : cur.bItems);
                    list.Add(stack.CloneWithCount(1));
                    net.TradeSetOffer(cur.tradeId, list, a ? cur.aMoney : cur.bMoney);
                });
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("お金：{0}", "Money: {0}", UIStyles.Money(myMoney)), UIStyles.Label, GUILayout.Width(220));
            foreach (var d in new[] { -100, -10, 10, 100 })
            {
                if (GUILayout.Button((d > 0 ? "+" : "") + d, UIStyles.ButtonSmall, GUILayout.Width(64)))
                    net.TradeSetOffer(s.tradeId, Copy(myItems), Mathf.Clamp(myMoney + d, 0, lp.Wallet.money));
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Label(myReady ? Loc.T("あなた：これでOK", "You: OK") : Loc.T("あなた：まだ", "You: not yet"), myReady ? UIStyles.LabelBold : UIStyles.LabelDim);
            GUILayout.EndVertical();

            // ---- 相手の出す物 ----
            GUILayout.BeginVertical(UIStyles.Box, GUILayout.Width(520));
            GUILayout.Label(Loc.Format("{0}さんが出す物", "{0} gives", partner), UIStyles.LabelBold);
            scrollTheirs = GUILayout.BeginScrollView(scrollTheirs, GUILayout.Height(360));
            foreach (var it in theirItems)
            {
                GUILayout.BeginHorizontal();
                var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(r, it);
                GUILayout.Label($"{it.DisplayName} ×{it.count}", UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            if (theirItems.Count == 0) GUILayout.Label(Loc.T("（品物なし）", "(no items)"), UIStyles.LabelDim);
            GUILayout.EndScrollView();
            GUILayout.Space(48);
            GUILayout.Label(Loc.Format("お金：{0}", "Money: {0}", UIStyles.Money(theirMoney)), UIStyles.Label);
            GUILayout.Label(theirReady ? Loc.Format("{0}さん：これでOK", "{0}: OK", partner) : Loc.Format("{0}さん：まだ", "{0}: not yet", partner),
                theirReady ? UIStyles.LabelBold : UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            switch (s.phase)
            {
                case TradePhase.Negotiating:
                {
                    bool empty = myItems.Count == 0 && myMoney == 0 && theirItems.Count == 0 && theirMoney == 0;
                    GUI.enabled = !empty;
                    if (GUILayout.Button(myReady ? Loc.T("OKを取り消す", "Undo OK") : Loc.T("これでOK", "OK"), UIStyles.Button, GUILayout.Height(52)))
                        net.TradeSetReady(s.tradeId, s.version, !myReady);
                    GUI.enabled = true;
                    if (GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(52))) Close();
                    break;
                }
                case TradePhase.Depositing:
                    GUILayout.Label(Loc.T("受け渡し中…（このまま待ってください）", "Exchanging... please wait"), UIStyles.LabelBold);
                    break;
                default:
                    if (GUILayout.Button(Loc.T("閉じる", "Close"), UIStyles.Button, GUILayout.Height(52))) Close();
                    break;
            }
            GUILayout.EndHorizontal();
        }

        private void ChangeCount(List<ItemStack> items, int money, int index, int delta)
        {
            var list = Copy(items);
            if (index < 0 || index >= list.Count) return;
            list[index].count += delta;
            if (list[index].count <= 0) list.RemoveAt(index);
            net.TradeSetOffer(S != null ? S.tradeId : null, list, money);
        }

        private static List<ItemStack> Copy(List<ItemStack> src)
        {
            var r = new List<ItemStack>();
            if (src != null) foreach (var s in src) if (s != null) r.Add(s.Clone());
            return r;
        }
    }
}
