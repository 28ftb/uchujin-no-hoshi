using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>タブレットの設定（推測訳の併記 §6.5、文字表示、その場で保存キー、ピン留め表示など）。</summary>
    internal sealed class SettingsApp : TabletAppBase
    {
        public const string Id = "settings";

        internal SettingsApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("設定", "Settings");
        public override int Order => 900;
        public override string IconGlyph => "⚙";
        public override Color IconColor => new Color(0.45f, 0.48f, 0.55f);

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            scroll = GUILayout.BeginScrollView(scroll);

            TabletGui.SectionHeader(Loc.T("宇宙語の表示", "Alien text"));
            bool gloss = GUILayout.Toggle(TabletSettings.GlossOverlay,
                Loc.T(" 自分が辞書に書いた推測訳を宇宙語の下に小さく添える", " Show my own dictionary guesses under alien words"), UIStyles.Toggle);
            if (gloss != TabletSettings.GlossOverlay) TabletSettings.GlossOverlay = gloss;
            GUILayout.Label(Loc.T("表示されるのは自分の推測だけです。間違っていれば間違ったまま表示されます。書いていない語は何も出ません。原文はいつでも見られます。",
                "Only your own guesses are shown — wrong ones stay wrong. Unwritten words show nothing. The original is always visible."), TabletGui.SmallDim);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("タブレット内の宇宙語の文字の大きさ：", "Alien text size in the tablet:"), UIStyles.LabelSmall, GUILayout.Width(380));
            int s = TabletSettings.AlienTextScale;
            if (TabletGui.ChipToggle(Loc.T("小", "Small"), s == 0)) TabletSettings.AlienTextScale = 0;
            if (TabletGui.ChipToggle(Loc.T("標準", "Normal"), s == 1)) TabletSettings.AlienTextScale = 1;
            if (TabletGui.ChipToggle(Loc.T("大", "Large"), s == 2)) TabletSettings.AlienTextScale = 2;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            TabletGui.SectionHeader(Loc.T("記録", "Recording"));
            bool qs = GUILayout.Toggle(TabletSettings.QuickSaveKeyEnabled,
                Loc.Format(" 話しかけられた直後に [{0}] でその声を音声カードに保存する", " Press [{0}] right after hearing someone to save it as a voice card", TabletTuning.QuickSaveKey), UIStyles.Toggle);
            if (qs != TabletSettings.QuickSaveKeyEnabled) TabletSettings.QuickSaveKeyEnabled = qs;
            bool pin = GUILayout.Toggle(TabletSettings.ShowPinnedOverlay,
                Loc.T(" ピン留めした単語・カードを画面の端に表示する", " Show pinned words and cards at the screen edge"), UIStyles.Toggle);
            if (pin != TabletSettings.ShowPinnedOverlay) TabletSettings.ShowPinnedOverlay = pin;
            bool mimic = GUILayout.Toggle(TabletSettings.ManepiMimic,
                Loc.T(" マネピが自分の身振りを真似する", " Manepi copies my gestures"), UIStyles.Toggle);
            if (mimic != TabletSettings.ManepiMimic) TabletSettings.ManepiMimic = mimic;

            var time = Game.Instance?.Time;
            if (time != null)
            {
                TabletGui.SectionHeader(Loc.T("時間", "Time"));
                bool pause = GUILayout.Toggle(time.PauseInSoloWhenTabletOpen,
                    Loc.T(" ひとりで遊んでいる時は、タブレットを開いている間 時間を止める", " Pause time while the tablet is open (solo only)"), UIStyles.Toggle);
                if (pause != time.PauseInSoloWhenTabletOpen)
                {
                    time.PauseInSoloWhenTabletOpen = pause;
                    // 開いたままの切り替えにも即座に反映（UIManager と同じトークン名）
                    var w = Sys.Window;
                    if (w != null && w.IsOpen)
                    {
                        string token = "ui:" + w.GetHashCode();
                        if (pause) time.AddPersonalPause(token);
                        else time.RemovePersonalPause(token);
                    }
                }
                GUILayout.Label(Loc.T("マルチプレイ中は、だれかがタブレットを開いても時間は止まりません。", "In multiplayer, opening the tablet never stops time for others."), TabletGui.SmallDim);
            }

            TabletGui.SectionHeader(Loc.T("このタブレットについて", "About this tablet"));
            GUILayout.Label(Loc.T("故郷の宇宙船から持ち出した端末です。この星の言葉は知らないので、翻訳はできません。聞いた声・見た様子・写真を記録して、意味は自分で推理しましょう。辞書や住民辞典に書いたことは、まちがっていても勝手に直されません。",
                "A device you brought from your ship. It doesn't know this planet's language, so it can't translate. Record voices, what you saw, and photos — and work out the meanings yourself. Nothing you write is ever corrected for you."), UIStyles.LabelWrap);
            GUILayout.EndScrollView();
        }
    }
}
