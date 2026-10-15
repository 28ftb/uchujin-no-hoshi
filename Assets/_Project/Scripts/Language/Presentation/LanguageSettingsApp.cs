using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Language
{
    /// <summary>
    /// タブレットのアプリ「声と文字」：宇宙語の声（音節ごとの音）・看板の読み方の併記・吹き出しの大きさ・記号の表示を切り替える。
    /// 宇宙語の意味・正解は一切表示しない（設定だけ）。
    /// </summary>
    public sealed class LanguageSettingsApp : ITabletApp
    {
        public string AppId => "language.settings";
        public string Title => Loc.T("声と文字", "Voices & Script");
        public int Order => 900;
        public string IconGlyph => "♪";
        public Color IconColor => new Color(0.55f, 0.8f, 0.7f, 1f);
        public bool IsAvailable => true;

        public void OnOpen() { }

        public void Draw(Rect area)
        {
            GUILayout.Label(Loc.T("宇宙語の聞こえ方・見え方の設定です。", "How the alien language sounds and looks."), UIStyles.LabelDim);
            GUILayout.Space(8);

            LanguageSettings.VoiceEnabled = GUILayout.Toggle(LanguageSettings.VoiceEnabled,
                Loc.T(" 話し声（音節ごとの音）を鳴らす", " Play voices (a sound per syllable)"), UIStyles.Toggle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("声の大きさ", "Voice volume"), UIStyles.Label, GUILayout.Width(200));
            float v = GUILayout.HorizontalSlider(LanguageSettings.VoiceVolume, 0f, 1f, GUILayout.Width(260));
            if (!Mathf.Approximately(v, LanguageSettings.VoiceVolume)) LanguageSettings.VoiceVolume = v;
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            LanguageSettings.ShowSignReading = GUILayout.Toggle(LanguageSettings.ShowSignReading,
                Loc.T(" 看板・手紙の宇宙文字に読み方を添える", " Show readings under alien script on signs/letters"), UIStyles.Toggle);
            LanguageSettings.ShowSymbols = GUILayout.Toggle(LanguageSettings.ShowSymbols,
                Loc.T(" 吹き出しに表情・身振りの記号を出す", " Show expression/gesture symbols in bubbles"), UIStyles.Toggle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("吹き出しの大きさ", "Bubble size"), UIStyles.Label, GUILayout.Width(200));
            float s = GUILayout.HorizontalSlider(LanguageSettings.BubbleScale, 0.7f, 1.6f, GUILayout.Width(260));
            if (!Mathf.Approximately(s, LanguageSettings.BubbleScale)) LanguageSettings.BubbleScale = s;
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label(Loc.T("吹き出しの単語をクリックすると、辞書のその単語のページが開きます。",
                "Click a word in a speech bubble to open it in your dictionary."), UIStyles.LabelWrap);
            GUILayout.Label(Loc.T("辞書に書いた推測訳を吹き出しに併記するかどうかは、辞書の設定で切り替えます。",
                "Whether your guesses appear under bubbles is set in the dictionary."), UIStyles.LabelWrap);
        }
    }
}
