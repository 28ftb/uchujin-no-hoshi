using System;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// 睡眠の投票（時間天候 §16：全員の同意で翌朝へ）。非モーダルなので、答えるまで普通に動ける。
    /// </summary>
    public sealed class SleepVoteWindow : UIWindow
    {
        private readonly string voteId;
        private readonly string requesterName;
        private readonly int wakeHour;
        private readonly float deadline;
        private readonly NetworkSystem net;
        private readonly Action<bool> onVote;
        private bool answered;

        public SleepVoteWindow(string voteId, string requesterName, int wakeHour, float deadline, NetworkSystem net, Action<bool> onVote)
        {
            this.voteId = voteId;
            this.requesterName = requesterName;
            this.wakeHour = wakeHour;
            this.deadline = deadline;
            this.net = net;
            this.onVote = onVote;
        }

        public string VoteId => voteId;
        public override string Title => Loc.T("おやすみの相談", "Time to sleep?");
        public override bool IsModal => false;
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => false;
        public override Vector2 Size => new Vector2(640, 250);
        public override Vector2? Position => new Vector2((UIStyles.VirtualWidth - 640f) * 0.5f, 24f);

        public override void DrawContents(Rect content)
        {
            int left = Mathf.Max(0, Mathf.CeilToInt(deadline - net.NetClock));
            GUILayout.Label(Loc.Format("{0}さんが「{1}時まで眠ろう」と言っています。（あと{2}秒）",
                "{0} suggests sleeping until {1}:00. ({2}s left)", requesterName, wakeHour, left), UIStyles.LabelWrap);
            GUILayout.Label(Loc.T("全員が同意すると、みんなで翌朝まで時間が進みます。", "If everyone agrees, time skips to the morning for all."), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            if (answered)
            {
                GUILayout.Label(Loc.T("返事をしました。みんなの返事を待っています…", "Answered. Waiting for the others..."), UIStyles.Label);
                return;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("いっしょに眠る", "Sleep together"), UIStyles.Button, GUILayout.Height(48)))
            {
                answered = true;
                onVote?.Invoke(true);
            }
            if (GUILayout.Button(Loc.T("まだ起きている", "Stay up"), UIStyles.Button, GUILayout.Height(48)))
            {
                answered = true;
                onVote?.Invoke(false);
            }
            GUILayout.EndHorizontal();
        }
    }
}
