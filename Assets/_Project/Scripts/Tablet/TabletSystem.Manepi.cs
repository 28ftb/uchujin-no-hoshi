using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;

namespace Uchujin.Tablet
{
    /// <summary>
    /// マネピ（No.033）の伝言板（タブレット仕様 §4, §9、家具仕様 §13、時間・天候仕様 §14）。
    /// ・住民が「預けた」伝言だけを覚える。全会話を自動録音する存在ではない（§4.3）。
    /// ・伝言の中身は星の状態（Planet スコープ・ホストが正本）。誰がどれを聞いたかは各プレイヤーの知識。
    /// ・再生では声（吹き出し）・表情・仕草を正確に再現する。意味を翻訳して教えることはしない（§4.2）。
    /// </summary>
    public sealed partial class TabletSystem
    {
        private static readonly List<UtteranceRecord> EmptyMessages = new List<UtteranceRecord>();

        internal readonly Dictionary<string, List<ManepiBehaviour>> manepis = new Dictionary<string, List<ManepiBehaviour>>();
        internal readonly Dictionary<string, LocText> boardLabels = new Dictionary<string, LocText>();

        public const string PlazaBoardId = "plaza";
        public const string ShopBoardId = "shop";
        public static string HomeBoardId(string playerId) => "home:" + playerId;

        // ------------------------------------------------------------
        // IManepiService
        // ------------------------------------------------------------

        public void LeaveMessage(string boardId, UtteranceRecord record)
        {
            if (string.IsNullOrEmpty(boardId) || record == null) return;
            var board = GetOrCreateBoard(boardId);
            if (!string.IsNullOrEmpty(record.recordId))
            {
                foreach (var m in board.messages) if (m != null && m.recordId == record.recordId) return;
            }
            // 預かった声は原本のまま（否定の語尾なども失わない §4.2）
            var copy = TabletText.CloneRecord(record);
            if (string.IsNullOrEmpty(copy.recordId)) copy.recordId = "msg:" + Ids.New();
            board.messages.Add(copy);
            while (board.messages.Count > TabletTuning.ManepiMaxMessagesPerBoard) board.messages.RemoveAt(0);
            MarkDirty();
        }

        public IReadOnlyList<UtteranceRecord> GetMessages(string boardId)
        {
            var b = FindBoard(boardId);
            return b != null ? (IReadOnlyList<UtteranceRecord>)b.messages : EmptyMessages;
        }

        public void ClearMessagesOlderThan(string boardId, int day)
        {
            var b = FindBoard(boardId);
            if (b == null) return;
            int n = b.messages.RemoveAll(m => m == null || m.day < day);
            if (n > 0) MarkDirty();
        }

        public GameObject SpawnManepi(string boardId, Transform parent, Vector3 localPosition, string labelJa, string labelEn)
        {
            if (string.IsNullOrEmpty(boardId)) return null;
            if (!string.IsNullOrEmpty(labelJa) || !string.IsNullOrEmpty(labelEn)) boardLabels[boardId] = new LocText(labelJa, labelEn);
            var root = new GameObject("Manepi_" + boardId);
            var p = parent != null ? parent : Uchujin.Core.Game.Instance?.SceneRoot;
            if (p != null) root.transform.SetParent(p, false);
            root.transform.localPosition = localPosition;
            var mb = root.AddComponent<ManepiBehaviour>();
            mb.Init(this, boardId);
            if (!manepis.TryGetValue(boardId, out var list))
            {
                list = new List<ManepiBehaviour>();
                manepis[boardId] = list;
            }
            list.Add(mb);
            WorldLabels.Add(mb.LabelAnchor, () => mb.LabelText(), Vector3.zero, new Color(1f, 0.92f, 0.75f), 18f);
            return root;
        }

        public void OpenBoard(string boardId, PlayerState player)
        {
            if (string.IsNullOrEmpty(boardId)) return;
            var existing = UIManager.Find<ManepiBoardWindow>();
            if (existing != null) UIManager.Close(existing);
            UIManager.Open(new ManepiBoardWindow(this, boardId, player ?? Uchujin.Core.Game.Instance?.LocalPlayer));
        }

        // ------------------------------------------------------------
        // 内部
        // ------------------------------------------------------------

        internal ManepiBoardData FindBoard(string boardId)
        {
            if (string.IsNullOrEmpty(boardId)) return null;
            foreach (var b in ManepiData.boards) if (b.boardId == boardId) return b;
            return null;
        }

        private ManepiBoardData GetOrCreateBoard(string boardId)
        {
            var b = FindBoard(boardId);
            if (b != null) return b;
            b = new ManepiBoardData { boardId = boardId };
            ManepiData.boards.Add(b);
            return b;
        }

        internal static int RetentionDays(string boardId)
        {
            if (boardId == PlazaBoardId) return TabletTuning.ManepiRetentionDaysPlaza;
            if (boardId == ShopBoardId) return TabletTuning.ManepiRetentionDaysShop;
            if (boardId != null && boardId.StartsWith("home:")) return TabletTuning.ManepiRetentionDaysHome;
            return TabletTuning.ManepiRetentionDaysDefault;
        }

        internal string BoardLabel(string boardId)
        {
            if (boardId != null && boardLabels.TryGetValue(boardId, out var t) && !t.IsEmpty) return t.ToString();
            if (boardId == PlazaBoardId) return Loc.T("伝言広場のマネピ", "Message Plaza Manepi");
            if (boardId == ShopBoardId) return Loc.T("商店のマネピ", "Shop Manepi");
            if (boardId != null && boardId.StartsWith("home:")) return Loc.T("自宅のマネピ", "Home Manepi");
            return Loc.T("マネピ", "Manepi");
        }

        /// <summary>音声カードの出典に使う名前。</summary>
        internal void BoardSourceLabel(string boardId, out string ja, out string en)
        {
            if (boardId != null && boardLabels.TryGetValue(boardId, out var t) && !t.IsEmpty)
            {
                ja = "マネピ（" + (t.ja ?? t.en) + "）";
                en = "Manepi (" + (t.en ?? t.ja) + ")";
                return;
            }
            if (boardId == PlazaBoardId) { ja = "マネピ（伝言広場）"; en = "Manepi (Message Plaza)"; return; }
            if (boardId == ShopBoardId) { ja = "マネピ（商店）"; en = "Manepi (Shop)"; return; }
            if (boardId != null && boardId.StartsWith("home:")) { ja = "マネピ（自宅）"; en = "Manepi (Home)"; return; }
            ja = "マネピ";
            en = "Manepi";
        }

        internal bool IsHeard(string recordId) => !string.IsNullOrEmpty(recordId) && Know.heardMessageIds.Contains(recordId);

        internal void MarkHeard(string recordId)
        {
            if (string.IsNullOrEmpty(recordId) || Know.heardMessageIds.Contains(recordId)) return;
            Know.heardMessageIds.Add(recordId);
            // 古い既読IDを掃除（伝言は日数で消えるので無制限に増やさない）
            if (Know.heardMessageIds.Count > 400) Know.heardMessageIds.RemoveRange(0, Know.heardMessageIds.Count - 400);
        }

        internal int UnheardCount(string boardId)
        {
            var b = FindBoard(boardId);
            if (b == null) return 0;
            int n = 0;
            foreach (var m in b.messages) if (m != null && !IsHeard(m.recordId)) n++;
            return n;
        }

        internal ManepiBehaviour ManepiFor(string boardId)
        {
            if (boardId == null || !manepis.TryGetValue(boardId, out var list)) return null;
            list.RemoveAll(m => m == null);
            return list.Count > 0 ? list[0] : null;
        }

        internal void UnregisterManepi(ManepiBehaviour mb)
        {
            if (mb == null) return;
            if (mb.BoardId != null && manepis.TryGetValue(mb.BoardId, out var list)) list.Remove(mb);
        }

        /// <summary>伝言を再生：マネピの位置で吹き出しを出し、表情・仕草を再現する（§4.2）。</summary>
        internal void PlayMessage(string boardId, UtteranceRecord rec)
        {
            if (rec == null) return;
            var mb = ManepiFor(boardId);
            var speech = Uchujin.Core.Game.Instance?.Service<ISpeechService>();
            Transform at = mb != null ? mb.VoiceAnchor : Uchujin.Core.Game.Instance?.LocalPlayer?.Avatar;
            if (speech != null) speech.Replay(rec, at);
            else UIManager.Toast(Loc.T("（マネピが口をぱくぱくさせている）", "(The Manepi moves its mouth)"));
            if (mb != null) mb.Reenact(rec, SpeakerColor(rec.speakerId), SpeakerLineage(rec.speakerId));
            MarkHeard(rec.recordId);
        }

        /// <summary>伝言広場（村の MessagePlaza 施設）にマネピを置く。</summary>
        private void BuildPlazaManepi()
        {
            var layout = Uchujin.Core.Game.Instance?.Layout;
            if (layout == null) return;
            var fac = layout.GetFacility(FacilityType.MessagePlaza);
            if (fac == null) return;
            var rot = Quaternion.Euler(0f, fac.rotationY, 0f);
            var pos = fac.position + rot * new Vector3(1.4f, 0f, 0.6f);
            pos.y = layout.HeightAt(pos);
            var go = SpawnManepi(PlazaBoardId, null, pos, "伝言広場", "Message Plaza");
            if (go != null) go.transform.rotation = rot;
        }

        private void OnPlayerGesture(PlayerGestureEvent e)
        {
            if (!TabletSettings.ManepiMimic) return;
            var g = Uchujin.Core.Game.Instance;
            var p = g != null ? g.Players.Get(e.PlayerId) : null;
            if (p == null) return;
            var pos = p.Position;
            foreach (var kv in manepis)
            {
                foreach (var mb in kv.Value)
                {
                    if (mb == null) continue;
                    if ((mb.transform.position - pos).sqrMagnitude > TabletTuning.ManepiMimicRange * TabletTuning.ManepiMimicRange) continue;
                    mb.Mimic(e.Gesture);
                }
            }
        }

        /// <summary>伝言を消す：自宅のマネピは持ち主、その他は共同生活以上の権限で。</summary>
        private CommandResult HandleDeleteMessage(ManepiDeleteMessageCommand cmd, CommandContext ctx)
        {
            var b = FindBoard(cmd.boardId);
            if (b == null) return CommandResult.Fail("伝言が見つかりません", "Message not found");
            bool allowed;
            if (cmd.boardId.StartsWith("home:")) allowed = cmd.boardId.Substring(5) == ctx.IssuerId || ctx.HasPermission(PermissionLevel.Owner);
            else allowed = ctx.HasPermission(PermissionLevel.CoLiving);
            if (!allowed) return CommandResult.Fail("このマネピの伝言は消せません", "You can't erase this Manepi's messages");
            int n = b.messages.RemoveAll(m => m != null && m.recordId == cmd.recordId);
            if (n > 0) MarkDirty();
            return CommandResult.Ok();
        }
    }
}
