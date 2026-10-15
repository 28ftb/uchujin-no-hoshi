using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.UI
{
    /// <summary>
    /// コアの簡易タイトル画面（Story モジュールのタイトル画面が無い場合の代替）。
    /// </summary>
    public class FallbackTitleWindow : UIWindow
    {
        private string seed = "";
        private List<SlotMeta> slots;

        public override string Title => Loc.T("宇宙人の星", "Planet of Aliens");
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => false;
        public override Vector2 Size => new Vector2(900, 760);

        public override void OnOpen()
        {
            slots = Game.Instance.Saves.ListSlots();
        }

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.T("（簡易タイトル画面）", "(fallback title screen)"), UIStyles.LabelDim);
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("星のシード（空欄ならランダム）", "Planet seed (blank = random)"), UIStyles.Label);
            seed = GUILayout.TextField(seed, UIStyles.TextField, GUILayout.Height(42));
            if (GUILayout.Button(Loc.T("新しい星で始める", "Start on a new planet"), UIStyles.Button, GUILayout.Height(54)))
            {
                Close();
                Game.Instance.StartNewPlanet(new NewGameSettings { SeedText = seed });
            }
            GUILayout.Space(16);
            GUILayout.Label(Loc.T("続きから", "Continue"), UIStyles.LabelBold);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(380));
            if (slots == null || slots.Count == 0) GUILayout.Label(Loc.T("セーブデータはありません。", "No saves."), UIStyles.LabelDim);
            else
            {
                foreach (var s in slots)
                {
                    if (GUILayout.Button(s.Describe(), UIStyles.Button, GUILayout.Height(48)))
                    {
                        Close();
                        Game.Instance.LoadSlot(s.slotId);
                    }
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(Loc.Current == UILanguage.Japanese ? "English" : "日本語", UIStyles.ButtonSmall, GUILayout.Width(160)))
                Loc.SetLanguage(Loc.Current == UILanguage.Japanese ? UILanguage.English : UILanguage.Japanese);
        }
    }

    /// <summary>Esc のメニュー：再開・セーブ・設定・マルチプレイ・タイトルへ。</summary>
    public class PauseMenuWindow : UIWindow
    {
        private bool showSettings;

        public override string Title => Loc.T("メニュー", "Menu");
        public override bool PausesTimeInSolo => true;
        public override Vector2 Size => new Vector2(640, showSettings ? 760 : 560);

        public override void DrawContents(Rect content)
        {
            var game = Game.Instance;
            if (showSettings)
            {
                DrawSettings(game);
                return;
            }
            if (GUILayout.Button(Loc.T("ゲームに戻る", "Resume"), UIStyles.Button, GUILayout.Height(50))) Close();
            if (GUILayout.Button(Loc.T("セーブする", "Save"), UIStyles.Button, GUILayout.Height(50))) game.SaveGame();
            if (GUILayout.Button(Loc.T("設定", "Settings"), UIStyles.Button, GUILayout.Height(50))) showSettings = true;
            var net = game.Service<INetworkService>();
            if (net != null && GUILayout.Button(Loc.T("マルチプレイ", "Multiplayer"), UIStyles.Button, GUILayout.Height(50)))
            {
                Close();
                net.OpenMultiplayerMenu();
            }
            var tablet = game.Service<ITabletService>();
            if (tablet != null && GUILayout.Button(Loc.T("タブレットを開く", "Open tablet"), UIStyles.Button, GUILayout.Height(50)))
            {
                Close();
                tablet.Open();
            }
            GUILayout.Space(12);
            if (GUILayout.Button(Loc.T("セーブしてタイトルへ", "Save and return to title"), UIStyles.Button, GUILayout.Height(50)))
            {
                Close();
                game.ReturnToTitle(true);
            }
            if (GUILayout.Button(Loc.T("ゲームを終了", "Quit game"), UIStyles.Button, GUILayout.Height(50)))
            {
                game.SaveGame(false);
                Application.Quit();
            }
        }

        private void DrawSettings(Game game)
        {
            GUILayout.Label(Loc.T("表示言語（UIのみ。宇宙語は変わりません）", "UI language (alien language is unaffected)"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("日本語", Loc.Current == UILanguage.Japanese ? UIStyles.ButtonSelected : UIStyles.Button)) SetLang(UILanguage.Japanese);
            if (GUILayout.Button("English", Loc.Current == UILanguage.English ? UIStyles.ButtonSelected : UIStyles.Button)) SetLang(UILanguage.English);
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            GUILayout.Label(Loc.Format("時間の速さ：×{0:0.00}（1日＝約{1:0}分）", "Time speed: x{0:0.00} (1 day = ~{1:0} min)",
                game.Time.SpeedMultiplier, game.Time.RealSecondsPerGameDay / 60f / game.Time.SpeedMultiplier), UIStyles.Label);
            game.Time.SpeedMultiplier = GUILayout.HorizontalSlider(game.Time.SpeedMultiplier, 0.5f, 3f);
            GUILayout.Space(10);
            UIManager.Hud.ShowClock = GUILayout.Toggle(UIManager.Hud.ShowClock, Loc.T(" 時計を常に表示", " Always show clock"), UIStyles.Toggle);
            UIManager.Hud.ShowControlsHelp = GUILayout.Toggle(UIManager.Hud.ShowControlsHelp, Loc.T(" 操作説明を表示", " Show controls help"), UIStyles.Toggle);
            game.Time.PauseInSoloWhenTabletOpen = GUILayout.Toggle(game.Time.PauseInSoloWhenTabletOpen,
                Loc.T(" ソロではタブレット中に時間を止める", " Pause time while tablet is open (solo)"), UIStyles.Toggle);
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("音量", "Volume"), UIStyles.Label);
            AudioListener.volume = GUILayout.HorizontalSlider(AudioListener.volume, 0f, 1f);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("戻る", "Back"), UIStyles.Button, GUILayout.Height(48))) showSettings = false;
        }

        private static void SetLang(UILanguage lang)
        {
            Loc.SetLanguage(lang);
            PlayerPrefs.SetInt("uchujin.uiLanguage", (int)lang);
        }
    }
}
