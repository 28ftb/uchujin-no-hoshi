using UnityEngine;

namespace Uchujin.Core.UI
{
    /// <summary>
    /// 画面常時表示（時計・天気・所持金・道具・ホットバー・操作プロンプト）。
    /// 時計を常時表示するかは仕様上未決定のため、設定で非表示にできる。
    /// </summary>
    public class HudRenderer
    {
        public bool ShowClock = true;
        public bool ShowControlsHelp = true;

        private static readonly ToolType[] toolOrder =
        {
            ToolType.Hand, ToolType.Hoe, ToolType.WateringCan, ToolType.Sickle, ToolType.FishingRod, ToolType.Pickaxe, ToolType.CatchNet
        };

        public static ToolType[] ToolOrder => toolOrder;

        public void Draw(string prompt)
        {
            var game = Game.Instance;
            var player = game?.LocalPlayer;
            if (player == null) return;
            float vw = UIStyles.VirtualWidth;
            float vh = UIStyles.VirtualHeight;

            // 左上：日時・天気・場所
            if (ShowClock && game.Time != null)
            {
                var r = new Rect(20, 20, 420, 112);
                GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
                GUI.Label(new Rect(36, 26, 400, 32), $"{game.Time.FormatDate()}  {game.Time.FormatClock()}  {DayPartUtil.Label(game.Time.CurrentDayPart)}", UIStyles.LabelBold);
                var weather = game.Service<IWeatherService>();
                string ws = weather != null ? weather.GetAt(player.Position).Summary() : "";
                GUI.Label(new Rect(36, 58, 400, 28), ws, UIStyles.LabelSmall);
                var area = game.Areas.Get(player.CurrentAreaId);
                string areaName = area != null ? area.Name : "";
                if (string.IsNullOrEmpty(areaName))
                {
                    var layout = game.Layout;
                    var reg = layout?.GetAreaAt(player.Position);
                    if (reg != null) areaName = Loc.T(reg.nameJa, reg.nameEn);
                }
                GUI.Label(new Rect(36, 86, 400, 28), areaName, UIStyles.LabelDim);
                if (game.Time.IsPaused) GUI.Label(new Rect(300, 86, 120, 28), Loc.T("（時間停止中）", "(paused)"), UIStyles.LabelDim);
            }

            // 右上：所持金
            var mr = new Rect(vw - 300, 20, 280, 56);
            GUI.Box(mr, GUIContent.none, UIStyles.PanelBox);
            GUI.Label(new Rect(mr.x + 16, mr.y + 12, 260, 32), UIStyles.Money(player.Wallet.money), UIStyles.LabelBold);

            // 下：道具バー＋ホットバー
            float cell = 64;
            float toolsWidth = toolOrder.Length * cell;
            float hotbarWidth = 10 * cell;
            float totalWidth = toolsWidth + 24 + hotbarWidth;
            float x0 = (vw - totalWidth) * 0.5f;
            float y0 = vh - cell - 20;
            for (int i = 0; i < toolOrder.Length; i++)
            {
                var t = toolOrder[i];
                var r = new Rect(x0 + i * cell, y0, cell - 4, cell - 4);
                bool has = player.Tools.Has(t);
                bool sel = player.Tools.equipped == t && (t != ToolType.Hand || player.SelectedSlot < 0);
                GUI.Box(r, GUIContent.none, sel ? UIStyles.SlotSelected : UIStyles.Slot);
                var st = new GUIStyle(UIStyles.LabelSmall) { alignment = TextAnchor.MiddleCenter, fontSize = 14, wordWrap = true };
                st.normal.textColor = has ? Color.white : new Color(1, 1, 1, 0.3f);
                GUI.Label(r, ToolTypeUtil.Label(t) + (t != ToolType.Hand && has ? "\n" + ToolTypeUtil.TierLabel(player.Tools.GetLevel(t)) : ""), st);
            }
            float hx = x0 + toolsWidth + 24;
            for (int i = 0; i < 10; i++)
            {
                var r = new Rect(hx + i * cell, y0, cell - 4, cell - 4);
                UIStyles.ItemIcon(r, player.Inventory.GetSlot(i), player.SelectedSlot == i);
                var num = new GUIStyle(UIStyles.LabelSmall) { fontSize = 12 };
                num.normal.textColor = UIStyles.TextDim;
                GUI.Label(new Rect(r.x + 3, r.y + r.height - 18, 20, 18), ((i + 1) % 10).ToString(), num);
            }
            var sel2 = player.SelectedItem;
            if (sel2 != null)
                GUI.Label(new Rect(hx, y0 - 30, hotbarWidth, 28), sel2.DisplayName, UIStyles.LabelSmall);

            // 中央下：操作プロンプト
            if (!string.IsNullOrEmpty(prompt))
            {
                var size = UIStyles.LabelBold.CalcSize(new GUIContent(prompt));
                var pr = new Rect((vw - size.x) * 0.5f - 16, y0 - 80, size.x + 32, 44);
                GUI.Box(pr, GUIContent.none, UIStyles.PanelBox);
                GUI.Label(new Rect(pr.x + 16, pr.y + 7, size.x + 4, 32), prompt, UIStyles.LabelBold);
            }

            if (ShowControlsHelp)
            {
                GUI.Label(new Rect(20, vh - 46, 1400, 28),
                    Loc.T("移動:WASD  走る:Shift  調べる/話す:E  道具を使う:左クリック/F  道具切替:R  持ち物:1〜0/I  タブレット:Tab  身振り:G  聞いた言葉を保存:K  カメラ:右ドラッグ/ホイール/V  メニュー:Esc  ヘルプ非表示:H",
                          "Move:WASD Run:Shift Interact:E Use:LMB/F Tool:R Items:1-0/I Tablet:Tab Gesture:G Save words:K Camera:RMB/Wheel/V Menu:Esc Hide help:H"),
                    UIStyles.LabelDim);
            }
        }
    }
}
