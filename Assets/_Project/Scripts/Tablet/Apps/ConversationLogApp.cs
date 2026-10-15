using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 会話ログ（タブレット仕様 §8.4）。主人公が実際に聞いた最近の発話（ISpeechService.RecentRecords）を並べ、
    /// 後から「音声カードとして保存」できる。会話中にメモを強制しない。主人公のいない場所の会話は入らない。
    /// </summary>
    internal sealed class ConversationLogApp : TabletAppBase
    {
        public const string Id = "log";

        internal ConversationLogApp(TabletSystem sys) : base(sys) { }

        public override string AppId => Id;
        public override string Title => Loc.T("会話ログ", "Conversation Log");
        public override int Order => 25;
        public override string IconGlyph => Loc.T("話", "Log");
        public override Color IconColor => new Color(0.45f, 0.55f, 0.85f);

        public override void Draw(Rect area)
        {
            TabletGui.Ensure();
            var speech = Game.Instance?.Service<ISpeechService>();
            if (speech == null)
            {
                GUILayout.Label(Loc.T("（言葉の記録が使えません）", "(Speech records are unavailable)"), UIStyles.LabelWrap);
                return;
            }
            var recs = speech.RecentRecords;
            GUILayout.Label(Loc.T("最近聞いた言葉です。気になるものは音声カードとして保存しておきましょう（古いものから消えていきます）。単語を押すと辞書が開きます。",
                "Words you heard recently. Save the interesting ones as voice cards (older ones fade away). Tap a word to open the dictionary."), TabletGui.SmallDim);
            scroll = GUILayout.BeginScrollView(scroll);
            if (recs == null || recs.Count == 0) GUILayout.Label(Loc.T("（まだ何も聞いていません）", "(Nothing heard yet)"), UIStyles.LabelDim);
            float w = area.width - 50;
            if (recs != null)
            {
                for (int i = recs.Count - 1; i >= 0; i--)
                {
                    var r = recs[i];
                    if (r == null) continue;
                    var card = Sys.FindCardByRecord(r.recordId);
                    GUILayout.BeginHorizontal(TabletGui.CardBox);
                    TabletGui.SpeakerFace(Sys, r, 56);
                    GUILayout.BeginVertical();
                    TabletGui.RecordWords(r, w - 420, TabletSettings.GlossOverlay);
                    GUILayout.Label(Sys.SpeakerLabel(r) + " / " + TabletText.DayTime(r.day, r.minuteOfDay) + " / " + TabletText.PlaceOfRecord(r) + " / " + TabletText.ChannelLabel(r.channel), TabletGui.FactLabel);
                    GUILayout.Label(Loc.Format("様子：{0}", "Looked: {0}", TabletText.NonverbalSummary(r)), TabletGui.FactLabel);
                    GUILayout.EndVertical();
                    GUILayout.FlexibleSpace();
                    if (Button("▶", 50)) Sys.ReplayNearPlayer(r);
                    if (card == null)
                    {
                        if (Button(Loc.T("音声カードとして保存", "Save as voice card"), 240)) Sys.SaveVoiceCard(r, "会話ログ", "Conversation log");
                    }
                    else if (Button(Loc.T("保存済み（開く）", "Saved (open)"), 200))
                    {
                        Sys.CardsApp.ShowCard(card);
                        Sys.Window?.Navigate(VoiceCardsApp.Id);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }
    }
}
