using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// タイトル画面（Story が Game.TitleScreenFactory に設定）。
    ///   新しい星で始める（シード入力 or ランダム、星の呼び名は任意）→ キャラクターメイク → Game.StartNewPlanet
    ///   続きから（削除確認付き）／友達の星へ行く（Network があれば）／設定・言語切替／クレジット
    /// 背景は TitleBackdrop（3Dの宇宙）、ロゴは重ね描き（IUIOverlay）。
    /// </summary>
    public class TitleWindow : UIWindow, IUIOverlay
    {
        private enum Page { Main, NewPlanet, Continue, Settings }

        private Page page;
        private List<SlotMeta> slots;
        private string seed = "";
        private string nickname = "";
        private bool skipPrologue;
        private static CharacterAppearance lastMade;

        public TitleWindow() { }

        private TitleWindow(Page start)
        {
            page = start;
        }

        public override string Title => "";
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => false;
        public override Vector2 Size => page == Page.Continue ? new Vector2(1100, 760) : new Vector2(640, page == Page.Settings ? 700 : 640);
        public override Vector2? Position => new Vector2(90f, Mathf.Max(260f, UIStyles.VirtualHeight - Size.y - 90f));

        public int OverlayOrder => -10;

        public override void OnOpen()
        {
            TitleBackdrop.Ensure();
            UIManager.RegisterOverlay(this);
            RefreshSlots();
            if (Game.Instance != null && Game.Instance.State == GameState.CharacterCreation) Game.Instance.SetState(GameState.Title);
        }

        public override void OnClose()
        {
            UIManager.UnregisterOverlay(this);
        }

        private void RefreshSlots()
        {
            try { slots = Game.Instance != null ? Game.Instance.Saves.ListSlots() : new List<SlotMeta>(); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                slots = new List<SlotMeta>();
            }
        }

        // ---- ロゴ（重ね描き）----
        public void DrawOverlay()
        {
            if (!IsOpen) return;
            float vw = UIStyles.VirtualWidth;
            var big = new GUIStyle(UIStyles.LabelTitle) { fontSize = 92, alignment = TextAnchor.MiddleCenter };
            var shadow = new GUIStyle(big);
            shadow.normal.textColor = new Color(0.1f, 0.15f, 0.35f, 0.8f);
            big.normal.textColor = new Color(0.85f, 0.95f, 1f);
            float bob = Mathf.Sin(UnityEngine.Time.unscaledTime * 1.2f) * 6f;
            var r = new Rect(vw * 0.35f, 70 + bob, vw * 0.6f, 120);
            GUI.Label(new Rect(r.x + 5, r.y + 6, r.width, r.height), Loc.T("宇宙人の星", "Planet of Aliens"), shadow);
            GUI.Label(r, Loc.T("宇宙人の星", "Planet of Aliens"), big);
            var sub = new GUIStyle(UIStyles.Label) { fontSize = 26, alignment = TextAnchor.MiddleCenter };
            sub.normal.textColor = new Color(1f, 0.85f, 0.55f);
            GUI.Label(new Rect(r.x, r.yMax, r.width, 40), Loc.T("― 帰るために始めた暮らし ―", "- A life begun to find the way home -"), sub);
            GUI.Label(new Rect(vw - 360, UIStyles.VirtualHeight - 40, 340, 30), Loc.T("プロトタイプ版", "Prototype build"), UIStyles.LabelDim);
        }

        public override void DrawContents(Rect content)
        {
            switch (page)
            {
                case Page.NewPlanet: DrawNewPlanet(); break;
                case Page.Continue: DrawContinue(content); break;
                case Page.Settings: DrawSettings(); break;
                default: DrawMain(); break;
            }
        }

        private void DrawMain()
        {
            GUILayout.Space(10);
            if (GUILayout.Button(Loc.T("新しい星で始める", "Start on a new planet"), UIStyles.ButtonSelected, GUILayout.Height(66))) page = Page.NewPlanet;
            GUI.enabled = slots != null && slots.Count > 0;
            if (GUILayout.Button(Loc.T("続きから", "Continue"), UIStyles.Button, GUILayout.Height(60)))
            {
                RefreshSlots();
                page = Page.Continue;
            }
            GUI.enabled = true;
            var net = Game.Instance != null ? Game.Instance.Service<INetworkService>() : null;
            if (net != null && GUILayout.Button(Loc.T("友達の星へ行く", "Visit a friend's planet"), UIStyles.Button, GUILayout.Height(60)))
            {
                try { net.OpenMultiplayerMenu(); }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (GUILayout.Button(Loc.T("設定", "Settings"), UIStyles.Button, GUILayout.Height(56))) page = Page.Settings;
            if (GUILayout.Button(Loc.T("クレジット", "Credits"), UIStyles.Button, GUILayout.Height(56)))
            {
                Close();
                UIManager.Open(new CreditsWindow(false, null, () => UIManager.Open(new TitleWindow())));
            }
            GUILayout.Space(10);
            if (GUILayout.Button(Loc.T("ゲームを終了", "Quit"), UIStyles.Button, GUILayout.Height(50))) Application.Quit();
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("日本語", Loc.Current == UILanguage.Japanese ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) SetLang(UILanguage.Japanese);
            if (GUILayout.Button("English", Loc.Current == UILanguage.English ? UIStyles.ButtonSelected : UIStyles.ButtonSmall)) SetLang(UILanguage.English);
            GUILayout.EndHorizontal();
        }

        private void DrawNewPlanet()
        {
            GUILayout.Label(Loc.T("新しい星で始める", "Start on a new planet"), UIStyles.LabelTitle);
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("星のシード（同じシードなら同じ地形・言葉・住民の星になります。空欄ならランダム）",
                "Planet seed (same seed = same terrain, language and residents. Blank = random)"), UIStyles.LabelWrap);
            GUILayout.BeginHorizontal();
            seed = GUILayout.TextField(seed ?? "", 32, UIStyles.TextField, GUILayout.Height(44), GUILayout.Width(380));
            if (GUILayout.Button(Loc.T("ランダム", "Random"), UIStyles.Button, GUILayout.Height(44))) seed = PlanetInfo.RandomSeedText();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("星の呼び名（任意。あなたが付ける名前です）", "Your name for the planet (optional)"), UIStyles.LabelWrap);
            nickname = GUILayout.TextField(nickname ?? "", 24, UIStyles.TextField, GUILayout.Height(44), GUILayout.Width(480));
            GUILayout.Space(10);
            skipPrologue = GUILayout.Toggle(skipPrologue, Loc.T(" 不時着の場面（プロローグ）を飛ばす", " Skip the crash-landing prologue"), UIStyles.Toggle);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.Button, GUILayout.Height(54), GUILayout.Width(160))) page = Page.Main;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("次へ（キャラクターメイク）", "Next (create character)"), UIStyles.ButtonSelected, GUILayout.Height(54)))
            {
                string s = seed;
                string n = nickname;
                bool skip = skipPrologue;
                Close();
                var initial = lastMade ?? AppearanceCatalog.Default();
                UIManager.Open(new CharacterMakeWindow(initial, CharacterMakeWindow.Mode.NewGame,
                    a =>
                    {
                        lastMade = a.Clone();
                        Game.Instance.StartNewPlanet(new NewGameSettings
                        {
                            SeedText = s,
                            Appearance = a,
                            PlanetNickname = string.IsNullOrWhiteSpace(n) ? null : n.Trim(),
                            SkipPrologue = skip,
                            KeepProfile = false,
                            PlanetIndex = 1,
                            PreviousWorldId = null
                        });
                    },
                    () =>
                    {
                        var back = new TitleWindow(Page.NewPlanet) { seed = s, nickname = n, skipPrologue = skip };
                        if (Game.Instance != null) Game.Instance.SetState(GameState.Title);
                        UIManager.Open(back);
                    }));
            }
            GUILayout.EndHorizontal();
        }

        private void DrawContinue(Rect content)
        {
            GUILayout.Label(Loc.T("続きから", "Continue"), UIStyles.LabelTitle);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(content.height - 130));
            if (slots == null || slots.Count == 0) GUILayout.Label(Loc.T("セーブデータはありません。", "No saves."), UIStyles.LabelDim);
            else
            {
                foreach (var s in slots)
                {
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    GUILayout.BeginVertical();
                    GUILayout.Label(s.Describe(), UIStyles.Label);
                    string extra = Loc.Format("{0}　修理 {1} か所", "{0}  Repairs: {1}", string.IsNullOrEmpty(s.playerName) ? "" : s.playerName, s.repairedDevices);
                    if (s.launched) extra += Loc.T("　（帰還済み）", "  (departed)");
                    GUILayout.Label(extra, UIStyles.LabelDim);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(Loc.T("遊ぶ", "Play"), UIStyles.ButtonSelected, GUILayout.Width(120), GUILayout.Height(52)))
                    {
                        string id = s.slotId;
                        Close();
                        Game.Instance.LoadSlot(id);
                    }
                    if (GUILayout.Button(Loc.T("削除", "Delete"), UIStyles.Button, GUILayout.Width(110), GUILayout.Height(52)))
                    {
                        var target = s;
                        UIManager.Confirm(Loc.T("セーブの削除", "Delete save"),
                            Loc.Format("「{0}」を削除します。元に戻せません。よろしいですか？", "Delete \"{0}\"? This cannot be undone.", target.Describe()),
                            () =>
                            {
                                try { Game.Instance.Saves.DeleteSlot(target.slotId); }
                                catch (System.Exception e) { Debug.LogException(e); }
                                RefreshSlots();
                                UIManager.Toast(Loc.T("削除しました", "Deleted"));
                            });
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(180))) page = Page.Main;
        }

        private void DrawSettings()
        {
            GUILayout.Label(Loc.T("設定", "Settings"), UIStyles.LabelTitle);
            GUILayout.Label(Loc.T("表示言語（UIのみ。宇宙語は変わりません）", "UI language (the alien language does not change)"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("日本語", Loc.Current == UILanguage.Japanese ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(46))) SetLang(UILanguage.Japanese);
            if (GUILayout.Button("English", Loc.Current == UILanguage.English ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(46))) SetLang(UILanguage.English);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("音量", "Volume"), UIStyles.LabelBold);
            AudioListener.volume = GUILayout.HorizontalSlider(AudioListener.volume, 0f, 1f);
            GUILayout.Space(10);
            var time = Game.Instance != null ? Game.Instance.Time : null;
            if (time != null)
            {
                GUILayout.Label(Loc.Format("時間の速さ：×{0:0.00}", "Time speed: x{0:0.00}", time.SpeedMultiplier), UIStyles.LabelBold);
                time.SpeedMultiplier = GUILayout.HorizontalSlider(time.SpeedMultiplier, 0.5f, 3f);
            }
            GUILayout.Space(10);
            UIManager.Hud.ShowControlsHelp = GUILayout.Toggle(UIManager.Hud.ShowControlsHelp, Loc.T(" 操作説明を表示", " Show controls help"), UIStyles.Toggle);
            UIManager.Hud.ShowClock = GUILayout.Toggle(UIManager.Hud.ShowClock, Loc.T(" 時計を常に表示", " Always show clock"), UIStyles.Toggle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(180))) page = Page.Main;
        }

        private static void SetLang(UILanguage lang)
        {
            Loc.SetLanguage(lang);
            PlayerPrefs.SetInt("uchujin.uiLanguage", (int)lang);
            PlayerPrefs.Save();
        }
    }
}
