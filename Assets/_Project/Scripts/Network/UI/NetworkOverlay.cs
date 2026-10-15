using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>HUD：マルチプレイ中の参加者一覧（時計の下）と、睡眠の返事待ちの表示。</summary>
    public sealed class NetworkOverlay : IUIOverlay
    {
        private readonly NetworkSystem net;

        public NetworkOverlay(NetworkSystem net)
        {
            this.net = net;
        }

        public int OverlayOrder => 5;

        public void DrawOverlay()
        {
            var game = Game.Instance;
            if (game == null || game.State != GameState.Playing || !UIManager.HudVisible) return;
            if (!net.IsOnline && net.Session != SessionState.HostStarting) return;

            int n = net.SessionPlayers.Count;
            float h = 40 + Mathf.Max(1, n) * 26 + (net.WaitingSleepVoteId != null ? 28 : 0);
            var r = new Rect(20, 142, 320, h);
            GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
            string head = net.IsHostSession ? Loc.T("マルチプレイ（ホスト）", "Multiplayer (host)") : Loc.T("マルチプレイ（訪問中）", "Multiplayer (visiting)");
            if (net.Transport != null) head += net.Transport.Kind == "eos" ? " EOS" : " LAN";
            GUI.Label(new Rect(r.x + 14, r.y + 6, r.width - 20, 28), head, UIStyles.LabelSmall);
            float y = r.y + 34;
            foreach (var p in net.SessionPlayers.Values)
            {
                string mark = p.IsHost ? "★" : "・";
                string perm = p.IsHost ? "" : "  " + NetworkSystem.PermissionLabel(p.Permission);
                GUI.Label(new Rect(r.x + 14, y, r.width - 20, 26), mark + p.DisplayName + perm + (p.IsLocal ? Loc.T("（あなた）", " (you)") : ""), UIStyles.LabelDim);
                y += 26;
            }
            if (net.WaitingSleepVoteId != null)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(net.WaitingSleepDeadline - net.NetClock));
                GUI.Label(new Rect(r.x + 14, y, r.width - 20, 26), Loc.Format("おやすみの返事待ち…（{0}秒）", "Waiting for sleep votes... ({0}s)", left), UIStyles.LabelSmall);
            }
        }
    }

    /// <summary>
    /// タイトル画面の右下に「友達の星へ行く」ボタンを出す（Story のタイトルに入口が無い場合の暫定。NetConfig.ShowTitleJoinButton）。
    /// Story のタイトル画面からは INetworkService.OpenMultiplayerMenu() を呼べば同じ画面が開く。
    /// </summary>
    public sealed class TitleJoinOverlay : IUIOverlay
    {
        private readonly NetworkSystem net;

        public TitleJoinOverlay(NetworkSystem net)
        {
            this.net = net;
        }

        public int OverlayOrder => 300;

        public void DrawOverlay()
        {
            var game = Game.Instance;
            if (game == null || game.State != GameState.Title || game.IsBusy) return;
            if (UIManager.Find<MultiplayerMenuWindow>() != null) return;
            var r = new Rect(UIStyles.VirtualWidth - 400, UIStyles.VirtualHeight - 80, 380, 56);
            if (GUI.Button(r, Loc.T("友達の星へ行く（マルチプレイ）", "Visit a friend (multiplayer)"), UIStyles.Button)) net.OpenMultiplayerMenu();
        }
    }
}
