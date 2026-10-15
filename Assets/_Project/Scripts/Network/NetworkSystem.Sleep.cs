using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// 睡眠（時間天候 §16、マルチ §18.2）：
    ///  ・誰かが眠ろうとすると全員に投票ウィンドウ → 全員が同意したらホストが SleepUntilHour（翌朝へ）。
    ///  ・同意が揃わなければ、眠ろうとした人だけ「個人の休息」（暗転するだけで時間は進めない）。
    ///  ・個人の操作で他人の時間を飛ばさない。ホスト優先スキップ等は未決定のため実装しない。
    /// </summary>
    public sealed partial class NetworkSystem
    {
        private sealed class SleepVoteState
        {
            public string VoteId;
            public string RequesterId;
            public int WakeHour;
            public float Deadline;
            public readonly HashSet<string> Participants = new HashSet<string>();
            public readonly HashSet<string> Agreed = new HashSet<string>();
        }

        private SleepVoteState hostVote;
        private SleepVoteWindow voteWindow;
        /// <summary>自分が眠ろうとして返事を待っている投票（HUD 表示用）。</summary>
        internal string WaitingSleepVoteId { get; private set; }
        internal float WaitingSleepDeadline { get; private set; }

        /// <summary>INetworkService：睡眠の要求（SleepHelper.RequestSleep から呼ばれる）。</summary>
        public void RequestSleep(PlayerState player, int targetHour)
        {
            if (player == null || Game == null) return;
            if (!IsOnline)
            {
                Game.StartCoroutine(Game.SleepRoutine(player, targetHour));
                return;
            }
            if (IsHostSession)
            {
                HostBeginSleepVote(LocalId, targetHour);
            }
            else
            {
                SendToHost(NetMsg.SleepRequest, new SleepRequestMsg { wakeHour = targetHour });
                UIManager.Toast(Loc.T("みんなに「そろそろ眠ろう」と伝えました…", "You suggested going to sleep..."));
            }
        }

        private void HostBeginSleepVote(string requesterId, int wakeHour)
        {
            if (hostVote != null)
            {
                if (requesterId == LocalId) UIManager.Toast(Loc.T("いま、おやすみの相談中です", "A sleep vote is already in progress"));
                else NotifyRemote(requesterId, "いま、おやすみの相談中です", "A sleep vote is already in progress");
                return;
            }
            var v = new SleepVoteState
            {
                VoteId = Ids.New(),
                RequesterId = requesterId,
                WakeHour = Mathf.Clamp(wakeHour, 0, 23),
                Deadline = clock + NetConfig.SleepVoteSeconds
            };
            foreach (var p in players.Values) if (p.IsLocal || p.Ready) v.Participants.Add(p.PlayerId);
            v.Participants.Add(requesterId);
            if (v.Participants.Count <= 1)
            {
                // 実質ひとり：そのまま眠る
                if (requesterId == LocalId) Game.StartCoroutine(Game.SleepRoutine(Game.LocalPlayer, v.WakeHour));
                return;
            }
            v.Agreed.Add(requesterId);
            hostVote = v;
            var msg = new SleepVoteStartMsg
            {
                voteId = v.VoteId,
                requesterId = requesterId,
                requesterName = GetPlayerName(requesterId),
                wakeHour = v.WakeHour,
                seconds = NetConfig.SleepVoteSeconds
            };
            BroadcastToGuests(NetMsg.SleepVoteStart, msg, null, true);
            ShowSleepVoteLocally(msg);
            CheckSleepVote();
        }

        private void HostReceiveSleepVote(string playerId, string voteId, bool agree)
        {
            var v = hostVote;
            if (v == null || v.VoteId != voteId || !v.Participants.Contains(playerId)) return;
            if (!agree)
            {
                FinishSleepVote(false);
                return;
            }
            v.Agreed.Add(playerId);
            CheckSleepVote();
        }

        private void CheckSleepVote()
        {
            var v = hostVote;
            if (v == null) return;
            foreach (var p in v.Participants) if (!v.Agreed.Contains(p)) return;
            FinishSleepVote(true);
        }

        private void SleepParticipantLeft(string playerId)
        {
            var v = hostVote;
            if (v == null) return;
            if (v.RequesterId == playerId)
            {
                FinishSleepVote(false);
                return;
            }
            v.Participants.Remove(playerId);
            v.Agreed.Remove(playerId);
            CheckSleepVote();
        }

        private void FinishSleepVote(bool success)
        {
            var v = hostVote;
            if (v == null) return;
            hostVote = null;
            var msg = new SleepResultMsg { voteId = v.VoteId, success = success, wakeHour = v.WakeHour, requesterId = v.RequesterId };
            BroadcastToGuests(NetMsg.SleepResult, msg, null, true);
            OnSleepResult(msg);
        }

        private void CancelSleepVote()
        {
            hostVote = null;
            ResetSleepVoteState();
        }

        /// <summary>セッション終了時：投票ウィンドウと「返事待ち」の表示を片付ける（次のセッションに残さない）。</summary>
        private void ResetSleepVoteState()
        {
            CloseSleepVoteUI();
            WaitingSleepVoteId = null;
        }

        private void SleepUpdate()
        {
            if (hostVote != null && clock > hostVote.Deadline) FinishSleepVote(false);
        }

        // ---- 両者共通 ----

        private void GuestOnSleepVoteStart(SleepVoteStartMsg m)
        {
            if (m == null || Session != SessionState.GuestPlaying) return;
            ShowSleepVoteLocally(m);
        }

        private void ShowSleepVoteLocally(SleepVoteStartMsg m)
        {
            if (m.requesterId == LocalId)
            {
                WaitingSleepVoteId = m.voteId;
                WaitingSleepDeadline = clock + m.seconds;
                UIManager.Toast(Loc.T("みんなの返事を待っています…", "Waiting for everyone's answer..."));
                return;
            }
            CloseSleepVoteUI();
            voteWindow = new SleepVoteWindow(m.voteId, m.requesterName, m.wakeHour, clock + m.seconds, this, agree => CastSleepVote(m.voteId, agree));
            UIManager.Open(voteWindow);
        }

        private void CastSleepVote(string voteId, bool agree)
        {
            if (IsHostSession) HostReceiveSleepVote(LocalId, voteId, agree);
            else SendToHost(NetMsg.SleepVote, new SleepVoteMsg { voteId = voteId, agree = agree });
        }

        private void OnSleepResult(SleepResultMsg m)
        {
            if (m == null) return;
            CloseSleepVoteUI();
            if (WaitingSleepVoteId == m.voteId) WaitingSleepVoteId = null;
            var lp = Game.LocalPlayer;
            if (m.success)
            {
                if (IsHostSession)
                {
                    // 星の時間を進めるのはホストだけ。進んだ時刻は "core.time" の差分でゲストへ届く。
                    Game.StartCoroutine(Game.SleepRoutine(lp, m.wakeHour));
                    dirtyKeys.Add("core.time");
                }
                else if (runner != null)
                {
                    runner.StartCoroutine(FadeRoutine(lp, "net.sleep", 0.8f, 2.4f, 1f));
                }
                UIManager.Toast(Loc.T("みんなで眠りました", "Everyone went to sleep"));
            }
            else
            {
                if (m.requesterId == LocalId)
                {
                    // 個人の休息：時間は進めない（時間天候 §16 の案）
                    if (runner != null) runner.StartCoroutine(FadeRoutine(lp, "net.rest", 0.5f, NetConfig.PersonalRestSeconds, 0.8f));
                    UIManager.Toast(Loc.T("みんなの同意がそろわなかったので、少し休みました（時間は進みません）",
                        "Not everyone agreed, so you just rested a bit (time does not skip)"));
                }
                else
                {
                    UIManager.Toast(Loc.T("おやすみの提案は見送られました", "The sleep suggestion was declined"));
                }
            }
        }

        private static IEnumerator FadeRoutine(PlayerState lp, string token, float fadeOut, float hold, float fadeIn)
        {
            lp?.LockInput(token);
            ScreenFader.FadeOut(fadeOut);
            yield return new WaitForSecondsRealtime(fadeOut + hold);
            ScreenFader.FadeIn(fadeIn);
            lp?.UnlockInput(token);
        }

        private void CloseSleepVoteUI()
        {
            if (voteWindow != null)
            {
                UIManager.Close(voteWindow);
                voteWindow = null;
            }
        }

        internal bool IsSleepVoteActive => hostVote != null || voteWindow != null || WaitingSleepVoteId != null;
    }
}
