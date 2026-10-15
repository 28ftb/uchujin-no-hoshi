using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// マネピの伝言一覧（タブレット仕様 §4, §9.1, §10.1）。
    /// 預けた住民の顔・日時 → 再生（マネピ位置で吹き出し＋表情・仕草の再現）→「音声カードとして保存」。
    /// 意味は翻訳しない。まだ聞いていない伝言は宇宙語の文字も伏せておき、聞いてから表示する。
    /// </summary>
    internal sealed class ManepiBoardWindow : UIWindow
    {
        private readonly TabletSystem sys;
        private readonly string boardId;
        private readonly PlayerState player;
        private Coroutine playAll;

        public ManepiBoardWindow(TabletSystem sys, string boardId, PlayerState player)
        {
            this.sys = sys;
            this.boardId = boardId;
            this.player = player;
        }

        public override string Title => sys.BoardLabel(boardId);
        public override bool PausesTimeInSolo => Game.Instance?.Time == null || Game.Instance.Time.PauseInSoloWhenTabletOpen;
        public override Vector2 Size => new Vector2(1080, Mathf.Min(UIStyles.VirtualHeight - 80f, 780f));
        /// <summary>再生中のマネピの演技が見えるよう、画面の下側に寄せる。</summary>
        public override Vector2? Position => new Vector2((UIStyles.VirtualWidth - 1080f) * 0.5f, Mathf.Max(20f, UIStyles.VirtualHeight - Size.y - 20f));

        public override void OnClose()
        {
            if (playAll != null && Game.Instance != null) Game.Instance.StopCoroutine(playAll);
            playAll = null;
        }

        public override void DrawContents(Rect content)
        {
            TabletGui.Ensure();
            var msgs = sys.GetMessages(boardId);
            int unheard = sys.UnheardCount(boardId);
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("預かっている伝言：{0}件（まだ聞いていない伝言 {1}件）", "Messages: {0} ({1} not heard yet)", msgs.Count, unheard), UIStyles.Label);
            GUILayout.FlexibleSpace();
            GUI.enabled = msgs.Count > 0 && playAll == null;
            if (GUILayout.Button(Loc.T("▶ 新しい伝言を続けて聞く", "▶ Play new messages"), UIStyles.ButtonSmall, GUILayout.Width(300), GUILayout.Height(40)))
            {
                if (Game.Instance != null) playAll = Game.Instance.StartCoroutine(PlayAllRoutine());
            }
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("タブレットで見る", "Open tablet"), UIStyles.ButtonSmall, GUILayout.Width(200), GUILayout.Height(40)))
            {
                Close();
                sys.Open(VoiceCardsApp.Id);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("マネピは、住民が預けていった声と仕草だけを覚えています。意味は教えてくれません。", "Manepi only remembers voices and gestures that residents left with it. It won't tell you what they mean."), TabletGui.SmallDim);

            scroll = GUILayout.BeginScrollView(scroll);
            if (msgs.Count == 0)
                GUILayout.Label(Loc.T("（いまは伝言を預かっていないようです。マネピは首をかしげている）", "(No messages right now. The Manepi tilts its head.)"), UIStyles.LabelWrap);
            float w = content.width - 60;
            var list = new List<UtteranceRecord>(msgs);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var r = list[i];
                if (r == null) continue;
                bool heard = sys.IsHeard(r.recordId);
                var card = sys.FindCardByRecord(r.recordId);
                GUILayout.BeginHorizontal(TabletGui.CardBox);
                TabletGui.SpeakerFace(sys, r, 72);
                GUILayout.BeginVertical();
                GUILayout.BeginHorizontal();
                if (!heard) GUILayout.Label(Loc.T("【新】", "[NEW]"), new GUIStyle(UIStyles.LabelBold) { normal = { textColor = UIStyles.AccentWarm } }, GUILayout.Width(70));
                GUILayout.Label(sys.SpeakerLabel(r), UIStyles.LabelBold);
                GUILayout.Label(TabletText.DayTime(r.day, r.minuteOfDay), UIStyles.LabelDim);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (heard)
                {
                    TabletGui.RecordWords(r, w - 520, TabletSettings.GlossOverlay);
                    GUILayout.Label(Loc.Format("様子：{0}", "Looked: {0}", TabletText.NonverbalSummary(r)), TabletGui.FactLabel);
                }
                else GUILayout.Label(Loc.T("（まだ聞いていません）", "(not heard yet)"), UIStyles.LabelDim);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(Loc.T("▶ 再生", "▶ Play"), UIStyles.ButtonSmall, GUILayout.Width(110), GUILayout.Height(44))) sys.PlayMessage(boardId, r);
                if (card == null)
                {
                    GUI.enabled = heard;
                    if (GUILayout.Button(Loc.T("音声カードとして保存", "Save as voice card"), UIStyles.ButtonSmall, GUILayout.Width(230), GUILayout.Height(44)))
                    {
                        sys.BoardSourceLabel(boardId, out var ja, out var en);
                        var copy = TabletText.CloneRecord(r);
                        var id = sys.SaveVoiceCard(copy, ja, en);
                        var saved = sys.FindCard(id);
                        if (saved != null) saved.boardId = boardId;
                    }
                    GUI.enabled = true;
                }
                else
                {
                    if (GUILayout.Button(Loc.T("保存済み（開く）", "Saved (open)"), UIStyles.ButtonSmall, GUILayout.Width(200), GUILayout.Height(44)))
                    {
                        Close();
                        sys.Open(VoiceCardsApp.Id);
                        sys.CardsApp.ShowCard(card);
                    }
                }
                if (CanErase() && GUILayout.Button(Loc.T("消す", "Erase"), UIStyles.ButtonSmall, GUILayout.Width(80), GUILayout.Height(44)))
                {
                    var rec = r;
                    UIManager.Confirm(Loc.T("伝言を消す", "Erase message"), Loc.T("この伝言をマネピに忘れてもらいますか？（保存した音声カードは残ります）", "Have the Manepi forget this message? (Saved voice cards stay.)"),
                        () => WorldAuthority.Submit(new ManepiDeleteMessageCommand { boardId = boardId, recordId = rec.recordId }));
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        /// <summary>自宅のマネピは持ち主が、共有の伝言板は共同生活以上の権限で消せる。</summary>
        private bool CanErase()
        {
            if (player == null) return false;
            if (boardId.StartsWith("home:")) return boardId.Substring(5) == player.PlayerId || player.Permission >= PermissionLevel.Owner;
            return player.Permission >= PermissionLevel.CoLiving;
        }

        private IEnumerator PlayAllRoutine()
        {
            var list = new List<UtteranceRecord>(sys.GetMessages(boardId));
            bool any = false;
            foreach (var r in list)
            {
                if (r == null || sys.IsHeard(r.recordId)) continue;
                any = true;
                sys.PlayMessage(boardId, r);
                yield return new WaitForSecondsRealtime(3.8f + (r.gestures != null ? r.gestures.Count * 1.0f : 0f));
                if (!IsOpen) break;
            }
            if (!any && list.Count > 0)
            {
                // 新しい伝言が無ければ最新の1件を聞き直す
                sys.PlayMessage(boardId, list[list.Count - 1]);
            }
            playAll = null;
        }
    }
}
