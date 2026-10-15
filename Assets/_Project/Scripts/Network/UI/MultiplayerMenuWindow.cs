using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// マルチプレイのメニュー（INetworkService.OpenMultiplayerMenu）。
    ///  ・ソロでプレイ中：星を開く（LAN UDP／EOS）／友達の星へ行く
    ///  ・タイトル：どのセーブの主人公で行くか選ぶ → 友達の星へ行く
    ///  ・ホスト：参加者と権限（観光・お手伝い・共同生活：マルチ §6.2）／辞書を渡す／交換する／星を閉じる
    ///  ・ゲスト：参加者／辞書を渡す／交換する／退出（自分の星へ帰る・タイトルへ）
    /// EOS が無効なビルドでは UDP のみ使え、「EOS は未設定」と表示する。
    /// </summary>
    public sealed class MultiplayerMenuWindow : UIWindow
    {
        private enum Page { Main, Dictionary, Trade }

        private readonly NetworkSystem net;
        private Page page = Page.Main;
        private string joinKind = "udp";
        private string address;
        private string portText;
        private string inviteCode = "";
        private List<LobbyListing> lobbies;
        private string lobbyStatus;
        private bool refreshing;
        private List<SlotMeta> slots;
        private string selectedSlot;
        private Vector2 scroll2;

        // 辞書を渡す
        private string dictTarget;
        private bool dictAll = true;
        private List<KeyValuePair<string, string>> dictCandidates;
        private readonly HashSet<string> dictSelected = new HashSet<string>();
        private string dictManual = "";

        public MultiplayerMenuWindow(NetworkSystem net)
        {
            this.net = net;
        }

        public override string Title => Loc.T("マルチプレイ", "Multiplayer");
        public override Vector2 Size => new Vector2(960, 800);

        public override void OnOpen()
        {
            address = PlayerPrefs.GetString("uchujin.net.lastAddress", "127.0.0.1:" + NetConfig.DefaultUdpPort);
            portText = PlayerPrefs.GetInt("uchujin.net.hostPort", NetConfig.DefaultUdpPort).ToString();
            page = Page.Main;
            var game = Game.Instance;
            if (game != null && game.State == GameState.Title) LoadSlots();
        }

        private void LoadSlots()
        {
            slots = new List<SlotMeta>();
            var game = Game.Instance;
            foreach (var s in game.Saves.ListSlots())
            {
                PlayerSaveFile pf = null;
                try { pf = game.Saves.ReadPlayer(s.slotId, game.LocalPlayerId); } catch { }
                if (pf != null && pf.profile != null && pf.profile.Count > 0) slots.Add(s);
            }
        }

        public override void DrawContents(Rect content)
        {
            var game = Game.Instance;
            if (game == null) return;
            DrawStatusLine();
            GUILayout.Space(6);

            if (page == Page.Dictionary)
            {
                DrawDictionaryPage();
                return;
            }
            if (page == Page.Trade)
            {
                DrawTradePage();
                return;
            }

            scroll = GUILayout.BeginScrollView(scroll);
            if (net.Session == SessionState.GuestConnecting || net.Session == SessionState.GuestAwaitingSnapshot || net.Session == SessionState.GuestLoading)
                DrawConnecting();
            else if (net.IsHostSession)
                DrawHost();
            else if (net.Session == SessionState.GuestPlaying)
                DrawGuest();
            else if (game.State == GameState.Title)
                DrawTitleJoin();
            else if (game.State == GameState.Playing)
                DrawOffline();
            else
                GUILayout.Label(Loc.T("今はマルチプレイの操作ができません。", "Multiplayer is unavailable right now."), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }

        private void DrawStatusLine()
        {
            string role;
            switch (net.Session)
            {
                case SessionState.Hosting: role = Loc.T("あなたの星を開いています", "Your planet is open"); break;
                case SessionState.HostStarting: role = Loc.T("星を開く準備中…", "Opening your planet..."); break;
                case SessionState.GuestPlaying: role = Loc.Format("{0}さんの星を訪問中", "Visiting {0}'s planet", net.HostName ?? "?"); break;
                case SessionState.None: role = Loc.T("ひとりで遊んでいます", "Playing solo"); break;
                default: role = Loc.T("接続中…", "Connecting..."); break;
            }
            GUILayout.Label(role, UIStyles.LabelBold);
            var t = net.Transport;
            if (t != null && !string.IsNullOrEmpty(t.StatusText))
                GUILayout.Label((t.Kind == "eos" ? "EOS: " : "LAN: ") + t.StatusText, UIStyles.LabelDim);
        }

        // ------------------------------------------------------------
        // ソロ：星を開く／友達の星へ行く
        // ------------------------------------------------------------

        private void DrawOffline()
        {
            GUILayout.Label(Loc.T("自分の星を開く", "Open your planet"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.Format("友達がこの星に遊びに来られるようにします（最大{0}人）。星の時間・住民・お店はこの星のまま続きます。",
                "Let friends visit this planet (up to {0} players). Time, residents and shops continue as usual.", NetConfig.MaxPlayers), UIStyles.LabelWrap);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("LAN のポート", "LAN port"), UIStyles.Label, GUILayout.Width(160));
            portText = GUILayout.TextField(portText ?? "", UIStyles.TextField, GUILayout.Width(140), GUILayout.Height(40));
            if (GUILayout.Button(Loc.T("LAN（UDP）で開く", "Open via LAN (UDP)"), UIStyles.Button, GUILayout.Height(46)))
            {
                int port = NetConfig.DefaultUdpPort;
                int.TryParse(portText, out port);
                if (port <= 0 || port > 65535) port = NetConfig.DefaultUdpPort;
                PlayerPrefs.SetInt("uchujin.net.hostPort", port);
                net.StartHosting("udp", port);
            }
            GUILayout.EndHorizontal();
            if (TransportFactory.EosAvailable)
            {
                if (GUILayout.Button(Loc.T("EOS（インターネット）で開く", "Open via EOS (online)"), UIStyles.Button, GUILayout.Height(46)))
                    net.StartHosting("eos", 0);
            }
            else
            {
                GUILayout.Label(Loc.T("EOS は未設定です（UCHUJIN_EOS が定義されていないビルド）。LAN のみ使えます。",
                    "EOS is not configured (built without UCHUJIN_EOS). Only LAN is available."), UIStyles.LabelDim);
            }

            GUILayout.Space(18);
            GUILayout.Label(Loc.T("友達の星へ行く", "Visit a friend's planet"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("この星をセーブしてから出かけます。持ち物・お金・道具はそのまま持って行き、帰る時に自分のセーブへ保存します。",
                "Your planet is saved before you leave. You bring your items, money and tools; they are saved back to your own save when you leave."), UIStyles.LabelWrap);
            DrawJoinForm(null);
        }

        private void DrawTitleJoin()
        {
            GUILayout.Label(Loc.T("友達の星へ行く", "Visit a friend's planet"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("どのセーブの主人公で行きますか？（持ち物はそのセーブに戻ってきます）", "Which save's character will travel? (Belongings return to that save.)"), UIStyles.LabelWrap);
            if (slots == null) LoadSlots();
            if (slots.Count == 0)
            {
                GUILayout.Label(Loc.T("出かけられる主人公がいません。先に自分の星を始めてください。", "No character to travel with. Start your own planet first."), UIStyles.LabelDim);
                return;
            }
            foreach (var s in slots)
            {
                bool sel = selectedSlot == s.slotId;
                string label = (string.IsNullOrEmpty(s.playerName) ? "?" : s.playerName) + "　" + s.Describe();
                if (GUILayout.Button(label, sel ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(46)))
                {
                    selectedSlot = s.slotId;
                    if (!net.LoadProfileFromSlot(selectedSlot))
                    {
                        UIManager.Toast(Loc.T("この主人公を読み込めませんでした", "Could not load that character"));
                        selectedSlot = null;
                    }
                }
            }
            if (selectedSlot == null) return;
            GUILayout.Space(10);
            DrawJoinForm(selectedSlot);
        }

        private void DrawJoinForm(string slotFromTitle)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("LAN（IPアドレス）", "LAN (IP address)"), joinKind == "udp" ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42))) joinKind = "udp";
            if (GUILayout.Button(Loc.T("EOS（ロビー・招待コード）", "EOS (lobbies / invite code)"), joinKind == "eos" ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42))) joinKind = "eos";
            GUILayout.EndHorizontal();

            if (joinKind == "udp")
            {
                GUILayout.Label(Loc.T("相手のアドレス（例：192.168.0.10:47777、同じPCなら 127.0.0.1）", "Host address (e.g. 192.168.0.10:47777, same PC: 127.0.0.1)"), UIStyles.LabelDim);
                GUILayout.BeginHorizontal();
                address = GUILayout.TextField(address ?? "", UIStyles.TextField, GUILayout.Height(42));
                if (GUILayout.Button(Loc.T("行く", "Go"), UIStyles.Button, GUILayout.Width(140), GUILayout.Height(46)))
                {
                    GUIUtility.keyboardControl = 0;
                    net.JoinUdp(address, slotFromTitle);
                }
                GUILayout.EndHorizontal();
                return;
            }

            if (!TransportFactory.EosAvailable)
            {
                GUILayout.Label(Loc.T("EOS は未設定です（UCHUJIN_EOS が定義されていないビルド）。", "EOS is not configured (built without UCHUJIN_EOS)."), UIStyles.LabelDim);
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("招待コード", "Invite code"), UIStyles.Label, GUILayout.Width(140));
            inviteCode = GUILayout.TextField(inviteCode ?? "", NetConfig.InviteCodeLength, UIStyles.TextField, GUILayout.Width(200), GUILayout.Height(42));
            if (GUILayout.Button(Loc.T("コードで行く", "Join by code"), UIStyles.Button, GUILayout.Height(46)))
            {
                GUIUtility.keyboardControl = 0;
                net.JoinEosCode(inviteCode, slotFromTitle);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            GUI.enabled = !refreshing;
            if (GUILayout.Button(refreshing ? Loc.T("探しています…", "Searching...") : Loc.T("開いている星を探す", "Find open planets"), UIStyles.Button, GUILayout.Height(44)))
            {
                refreshing = true;
                lobbyStatus = null;
                net.RefreshEosLobbies(list =>
                {
                    refreshing = false;
                    lobbies = list;
                    if (list == null || list.Count == 0) lobbyStatus = Loc.T("見つかりませんでした", "No planets found");
                }, err =>
                {
                    refreshing = false;
                    lobbyStatus = err;
                });
            }
            GUI.enabled = true;
            if (!string.IsNullOrEmpty(lobbyStatus)) GUILayout.Label(lobbyStatus, UIStyles.LabelDim);
            if (lobbies != null)
            {
                foreach (var l in lobbies)
                {
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    GUILayout.Label(Loc.Format("{0}さんの星「{1}」　{2}/{3}人", "{0}'s planet \"{1}\"  {2}/{3}",
                        string.IsNullOrEmpty(l.HostName) ? "?" : l.HostName, string.IsNullOrEmpty(l.PlanetName) ? "?" : l.PlanetName, l.Members, l.MaxMembers), UIStyles.Label);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(Loc.T("行く", "Go"), UIStyles.Button, GUILayout.Width(120), GUILayout.Height(42))) net.JoinEosLobby(l.LobbyId, slotFromTitle);
                    GUILayout.EndHorizontal();
                }
            }
        }

        private void DrawConnecting()
        {
            GUILayout.Label(Loc.T("友達の星へ向かっています…", "Travelling to your friend's planet..."), UIStyles.LabelTitle);
            string phase;
            switch (net.Session)
            {
                case SessionState.GuestConnecting: phase = Loc.T("接続中", "Connecting"); break;
                case SessionState.GuestAwaitingSnapshot: phase = Loc.T("星の様子を受け取っています", "Receiving the planet"); break;
                default: phase = Loc.T("星を読み込んでいます", "Loading the planet"); break;
            }
            GUILayout.Label(phase, UIStyles.Label);
            if (net.Session != SessionState.GuestLoading && GUILayout.Button(Loc.T("やめる", "Cancel"), UIStyles.Button, GUILayout.Height(46))) net.CancelJoin();
        }

        // ------------------------------------------------------------
        // ホスト
        // ------------------------------------------------------------

        private void DrawHost()
        {
            var t = net.Transport;
            if (t is ILobbyTransport lt && !string.IsNullOrEmpty(lt.InviteCode))
            {
                GUILayout.Label(Loc.Format("招待コード：{0}", "Invite code: {0}", lt.InviteCode), UIStyles.LabelTitle);
            }
            else if (t != null && t.Kind == "udp")
            {
                int port = t is LocalUdpTransport u ? u.BoundPort : NetConfig.DefaultUdpPort;
                GUILayout.Label(Loc.T("友達に伝えるアドレス：", "Address to give your friends:"), UIStyles.Label);
                foreach (var a in LocalUdpTransport.LocalAddresses()) GUILayout.Label($"  {a}:{port}", UIStyles.LabelBold);
                GUILayout.Label(Loc.T("※ LAN 用の簡易通信です（暗号化なし）。インターネット越しには EOS を使ってください。",
                    "LAN-only test transport (unencrypted). Use EOS over the internet."), UIStyles.LabelDim);
            }

            GUILayout.Space(8);
            GUILayout.Label(Loc.T("参加者と権限", "Players and permissions"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("観光：見学・交流・釣り・買い物・探索　／　お手伝い：畑仕事・採掘・料理・素材集め　／　共同生活：家具や農場づくりなど生活の編集も",
                "Sightseeing: explore, chat, fish, shop / Helper: farm, mine, cook, gather / Co-living: also edit furniture and farms"), UIStyles.LabelDim);
            int guests = 0;
            foreach (var p in net.SessionPlayers.Values)
            {
                if (p.IsLocal) continue;
                guests++;
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(p.DisplayName + (p.Ready ? "" : Loc.T("（読み込み中）", " (loading)")), UIStyles.Label, GUILayout.Width(260));
                foreach (var level in new[] { PermissionLevel.Sightseeing, PermissionLevel.Helper, PermissionLevel.CoLiving })
                {
                    if (GUILayout.Button(NetworkSystem.PermissionLabel(level), p.Permission == level ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Width(130), GUILayout.Height(38)))
                        net.SetPermission(p.PlayerId, level);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Loc.T("帰ってもらう", "Remove"), UIStyles.ButtonSmall, GUILayout.Width(150), GUILayout.Height(38)))
                {
                    string id = p.PlayerId;
                    string name = p.DisplayName;
                    UIManager.Confirm(Loc.T("確認", "Confirm"), Loc.Format("{0}さんに帰ってもらいますか？", "Remove {0}?", name), () => net.KickPlayer(id));
                }
                GUILayout.EndHorizontal();
            }
            if (guests == 0) GUILayout.Label(Loc.T("まだ誰も来ていません。", "Nobody has arrived yet."), UIStyles.LabelDim);

            GUILayout.Space(12);
            DrawSharedActions(guests > 0);
            GUILayout.Space(12);
            if (GUILayout.Button(Loc.T("星を閉じる（ひとり遊びに戻る）", "Close the planet (back to solo)"), UIStyles.Button, GUILayout.Height(48)))
            {
                UIManager.Confirm(Loc.T("確認", "Confirm"), Loc.T("マルチプレイを終えますか？ 友達は自分の星（またはタイトル）へ帰ります。", "End the session? Your friends will be sent home."),
                    () => net.HostShutdown(true, true));
            }
        }

        // ------------------------------------------------------------
        // ゲスト
        // ------------------------------------------------------------

        private void DrawGuest()
        {
            var lp = Game.Instance.LocalPlayer;
            GUILayout.Label(Loc.Format("あなたの権限：{0}", "Your permission: {0}", NetworkSystem.PermissionLabel(lp.Permission)), UIStyles.Label);
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("いっしょにいる人", "Players here"), UIStyles.LabelTitle);
            foreach (var p in net.SessionPlayers.Values)
            {
                string mark = p.IsHost ? "★" : "・";
                GUILayout.Label($"{mark} {p.DisplayName}　{(p.IsHost ? Loc.T("星の持ち主", "Owner") : NetworkSystem.PermissionLabel(p.Permission))}{(p.IsLocal ? Loc.T("（あなた）", " (you)") : "")}", UIStyles.Label);
            }
            GUILayout.Space(12);
            DrawSharedActions(net.SessionPlayers.Count > 1);
            GUILayout.Space(12);
            GUILayout.Label(Loc.T("帰る（持ち物は自分のセーブに保存されます）", "Leave (your belongings are saved to your own save)"), UIStyles.LabelDim);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("自分の星へ帰る", "Go home"), UIStyles.Button, GUILayout.Height(48)))
            {
                Close();
                net.LeaveAsGuest(true);
            }
            if (GUILayout.Button(Loc.T("タイトルへ戻る", "Back to title"), UIStyles.Button, GUILayout.Height(48)))
            {
                Close();
                net.LeaveAsGuest(false);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawSharedActions(bool enabled)
        {
            GUILayout.BeginHorizontal();
            GUI.enabled = enabled;
            if (GUILayout.Button(Loc.T("辞書を渡す", "Give dictionary"), UIStyles.Button, GUILayout.Height(48)))
            {
                page = Page.Dictionary;
                dictTarget = null;
                dictAll = true;
                dictSelected.Clear();
                dictCandidates = net.CandidateDictionaryWords();
            }
            if (GUILayout.Button(Loc.T("交換する", "Trade"), UIStyles.Button, GUILayout.Height(48))) page = Page.Trade;
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------
        // 辞書を渡す（マルチ §13：自動配布はしない）
        // ------------------------------------------------------------

        private void DrawDictionaryPage()
        {
            GUILayout.Label(Loc.T("辞書を渡す", "Give your dictionary"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("自分で書いた推測訳・メモを、コピーして相手に渡します（自分の辞書は減りません）。正しいかどうかはゲームは判定しません。",
                "Gives a copy of your own guesses and notes (yours are kept). The game never judges whether they are correct."), UIStyles.LabelWrap);
            GUILayout.Label(Loc.T("だれに？", "To whom?"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            foreach (var p in net.SessionPlayers.Values)
            {
                if (p.IsLocal) continue;
                if (GUILayout.Button(p.DisplayName, dictTarget == p.PlayerId ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42))) dictTarget = p.PlayerId;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("どこまで？", "How much?"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("全部", "Everything"), dictAll ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42))) dictAll = true;
            if (GUILayout.Button(Loc.T("選んだ語だけ", "Selected words"), !dictAll ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(42))) dictAll = false;
            GUILayout.EndHorizontal();

            if (!dictAll)
            {
                scroll2 = GUILayout.BeginScrollView(scroll2, GUILayout.Height(300));
                if (dictCandidates == null || dictCandidates.Count == 0)
                    GUILayout.Label(Loc.T("最近聞いた言葉の中に、辞書に書いた語がありません。下に直接入力できます。", "No recently heard words with your notes. You can type words below."), UIStyles.LabelDim);
                else
                {
                    foreach (var kv in dictCandidates)
                    {
                        bool on = dictSelected.Contains(kv.Key);
                        bool now = GUILayout.Toggle(on, $"  {kv.Key}　（{kv.Value}）", UIStyles.Toggle);
                        if (now != on)
                        {
                            if (now) dictSelected.Add(kv.Key);
                            else dictSelected.Remove(kv.Key);
                        }
                    }
                }
                GUILayout.EndScrollView();
                GUILayout.Label(Loc.T("ほかの語（聞こえた音をカンマ区切りで）", "Other words (as heard, comma separated)"), UIStyles.LabelDim);
                dictManual = GUILayout.TextField(dictManual ?? "", UIStyles.TextField, GUILayout.Height(40));
            }

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("戻る", "Back"), UIStyles.Button, GUILayout.Height(48))) page = Page.Main;
            GUI.enabled = dictTarget != null;
            if (GUILayout.Button(Loc.T("渡す", "Give"), UIStyles.Button, GUILayout.Height(48)))
            {
                List<string> words = null;
                if (!dictAll)
                {
                    words = new List<string>(dictSelected);
                    foreach (var w in (dictManual ?? "").Split(',', '、', ' ', '　'))
                    {
                        var t = w.Trim();
                        if (t.Length > 0 && !words.Contains(t)) words.Add(t);
                    }
                }
                if (!dictAll && words.Count == 0) UIManager.Toast(Loc.T("渡す語を選んでください", "Choose words to give"));
                else
                {
                    GUIUtility.keyboardControl = 0;
                    net.OfferDictionary(dictTarget, words);
                    page = Page.Main;
                }
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------
        // 交換の相手を選ぶ
        // ------------------------------------------------------------

        private void DrawTradePage()
        {
            GUILayout.Label(Loc.T("交換する", "Trade"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("品物やお金を出し合い、ふたりとも「これでOK」で成立します。片方だけが出せばプレゼントになります。",
                "Both put up items or money; it completes when both press OK. If only one side offers, it is a gift."), UIStyles.LabelWrap);
            foreach (var p in net.SessionPlayers.Values)
            {
                if (p.IsLocal) continue;
                if (GUILayout.Button(Loc.Format("{0}さんと交換", "Trade with {0}", p.DisplayName), UIStyles.Button, GUILayout.Height(48)))
                {
                    Close();
                    net.RequestTrade(p.PlayerId);
                }
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("戻る", "Back"), UIStyles.Button, GUILayout.Height(48))) page = Page.Main;
        }
    }
}
