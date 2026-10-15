using System;
using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.UI;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // マルチプレイの同期と、家の中の代理の姿・プレイヤー個人への記録
    //  ・軽い状態（副キー "residents.live"）：位置・向き・行動・表示・表情・持ち物の見た目・立ち話の相手と、
    //    AI が見せた表情・身振り（発話を伴わないもの）。ホストが約0.5秒ごとに送る。セーブ・参加時のスナップショットには入らない。
    //  ・全体状態（SaveKey "residents"）：記憶・関係・予定・お金・持ち物・引っ越し。変化があった時だけ、最短10秒間隔でまとめて送る。
    //    引っ越し・新住民・日付の更新など見た目がすぐ変わるものは MarkDirtyNow で即座に送る。参加時はこれだけで完全に復元できる。
    //  ・家の中の代理（Housing が作る姿）：設定中は本体を隠し、位置・吹き出し・身振りの対象・会話の距離を代理に合わせる。
    //  ・手紙・観察記録：知っている／その場にいたプレイヤーへ。接続していないプレイヤーには次に参加した時に届ける。
    // =====================================================================
    public sealed partial class ResidentSystem
    {
        public const string LiveSyncKey = "residents.live";
        private static readonly string[] liveSyncKeys = { LiveSyncKey };

        private float liveSyncTimer;
        private bool fullDirty;
        private float lastFullSyncTime = -999f;
        private readonly List<ResidentCueEntry> recentCues = new List<ResidentCueEntry>();
        private int cueSerial;
        private int cueLastSeen;
        private bool cuePrimed;

        private const int MaxPendingRecords = 200;
        /// <summary>代理を設定した場所から本体がこれ以上離れたら（出かけた）代理を解除する（m）。</summary>
        private const float IndoorLeaveDistance = 10f;

        private void ResetSyncState()
        {
            liveSyncTimer = 0f;
            fullDirty = false;
            lastFullSyncTime = -999f;
            recentCues.Clear();
            cueSerial = 0;
            cueLastSeen = 0;
            cuePrimed = false;
        }

        // ---------------- 全体状態の送信の間引き ----------------

        /// <summary>
        /// 全体状態（記憶・関係・予定など）が変わった印を付けるだけ。実際の送信は TickSync が最短間隔でまとめて行う。
        /// （基底の MarkDirty を隠す。位置・行動は residents.live で送るので、ここでは即座に送らない）
        /// </summary>
        private new void MarkDirty()
        {
            fullDirty = true;
        }

        /// <summary>全体状態をすぐ送る（引っ越し・新住民・日付の更新など、見た目がすぐ変わるもの）。</summary>
        private void MarkDirtyNow()
        {
            fullDirty = false;
            lastFullSyncTime = UnityEngine.Time.unscaledTime;
            base.MarkDirty();
        }

        private void TickSync(float deltaTime)
        {
            if (!WorldAuthority.IsMultiplayer)
            {
                // ソロ：送る相手がいない（ホストを始めた時は参加時のスナップショットで全体が届く）
                fullDirty = false;
                liveSyncTimer = 0f;
                return;
            }
            liveSyncTimer += deltaTime;
            if (liveSyncTimer >= ResidentTuning.LiveSyncInterval)
            {
                liveSyncTimer = 0f;
                MarkDirtySub(LiveSyncKey);
            }
            if (fullDirty && UnityEngine.Time.unscaledTime - lastFullSyncTime >= ResidentTuning.FullSyncMinInterval)
                MarkDirtyNow();
        }

        // ---------------- 軽い状態（residents.live） ----------------

        public override IEnumerable<string> SyncSubKeys => liveSyncKeys;

        public override string CaptureSyncState(string subKey)
        {
            if (subKey != LiveSyncKey || !IsAuthority) return null;
            var data = new ResidentsLiveData();
            foreach (var r in ordered)
            {
                if (!r.State.active && !r.IsVisitingFormer) continue;
                var st = r.State;
                var pos = r.BodyPosition;
                float yaw = r.Avatar != null ? r.Avatar.transform.eulerAngles.y : st.rotY;
                var e = new ResidentLiveEntry
                {
                    id = r.Id,
                    x = Round2(pos.x),
                    y = Round2(pos.y),
                    z = Round2(pos.z),
                    r = Mathf.Round(yaw),
                    a = (int)st.activity,
                    f = (int)st.face,
                    c = st.chatPartnerId ?? "",
                    h = st.heldItemId ?? "",
                    ar = r.HasIndoorProxy ? r.indoorAreaId ?? "" : ""
                };
                int fl = 0;
                if (st.hidden) fl |= ResidentLiveFlags.Hidden;
                if (st.carrying) fl |= ResidentLiveFlags.Carrying;
                if (st.cutsceneHold) fl |= ResidentLiveFlags.CutsceneHold;
                if (st.hasGoTo) fl |= ResidentLiveFlags.GoTo;
                if (r.hasTarget && r.path.Count > 0)
                {
                    var wp = r.path[Mathf.Clamp(r.pathIndex, 0, r.path.Count - 1)];
                    e.tx = Round2(wp.x);
                    e.tz = Round2(wp.z);
                    fl |= ResidentLiveFlags.Moving;
                }
                e.fl = fl;
                data.e.Add(e);
            }
            data.q.AddRange(recentCues);
            return JsonUtility.ToJson(data);
        }

        private static float Round2(float v) => Mathf.Round(v * 100f) / 100f;

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        public override void RestoreSyncState(string subKey, string json)
        {
            if (subKey != LiveSyncKey || IsAuthority || string.IsNullOrEmpty(json) || sceneRoot == null) return;
            ResidentsLiveData data;
            try { data = JsonUtility.FromJson<ResidentsLiveData>(json); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }
            if (data == null || data.e == null) return;
            var layout = Layout;
            foreach (var e in data.e)
            {
                if (e == null) continue;
                var r = GetResident(e.id);
                if (r == null) continue; // 新しい住民は全体状態で届く
                var st = r.State;
                var pos = new Vector3(e.x, e.y, e.z);
                r.StatePosition = pos;
                st.rotY = e.r;
                st.activity = (ResidentActivity)e.a;
                st.face = (Expression)e.f;
                st.hidden = (e.fl & ResidentLiveFlags.Hidden) != 0;
                st.carrying = (e.fl & ResidentLiveFlags.Carrying) != 0;
                st.cutsceneHold = (e.fl & ResidentLiveFlags.CutsceneHold) != 0;
                st.hasGoTo = (e.fl & ResidentLiveFlags.GoTo) != 0;
                st.chatPartnerId = NullIfEmpty(e.c);
                st.heldItemId = NullIfEmpty(e.h);
                r.netIndoorArea = NullIfEmpty(e.ar);
                if (r.Avatar != null)
                {
                    // 歩いている時は次の経由点の方へ、次の更新までに進む分だけ先読みする（補間の遅れを減らす）
                    var target = pos;
                    if ((e.fl & ResidentLiveFlags.Moving) != 0)
                    {
                        var to = new Vector3(e.tx - e.x, 0f, e.tz - e.z);
                        float dist = to.magnitude;
                        if (dist > 0.01f)
                        {
                            float step = Mathf.Min(dist, ResidentTuning.WalkSpeed * r.SpeedFactor * ResidentTuning.LiveSyncInterval);
                            target += to / dist * step;
                            if (layout != null) target.y = layout.HeightAt(target.x, target.z);
                        }
                    }
                    r.Avatar.HasNetTarget = true;
                    r.Avatar.NetTarget = target;
                    r.Avatar.NetYaw = e.r;
                }
                SyncAvatar(r);
            }
            PlayRemoteCues(data.q);
        }

        // ---------------- 表情・身振り（AI が見せたもの）をゲストでも再生する ----------------

        /// <summary>住民の表情を見せる（ホストではゲストにも届ける）。</summary>
        internal void CueFace(Resident r, Expression e, float seconds)
        {
            if (r == null) return;
            if (r.Avatar != null) r.Avatar.ShowExpression(e, seconds);
            AddCue(r, 0, (int)e, null, 0, seconds);
        }

        /// <summary>住民の身振りを見せる（ホストではゲストにも届ける）。</summary>
        internal void CueGesture(Resident r, GestureCue g)
        {
            if (r == null || g == null || g.gesture == Gesture.None) return;
            if (r.Avatar != null) r.Avatar.PlayGesture(g);
            AddCue(r, 1, (int)g.gesture, g.target, g.number, 0f);
        }

        /// <summary>中継される発話の表情・身振り（ゲストでは吹き出しだけが中継されるので、体の動きはこちらで届ける）。</summary>
        private void AddSpeechCues(Resident r, Utterance u)
        {
            if (r == null || u == null) return;
            AddCue(r, 0, (int)u.expression, null, 0, 3.5f);
            foreach (var g in u.gestures)
                if (g != null && g.gesture != Gesture.None) AddCue(r, 1, (int)g.gesture, g.target, g.number, 0f);
        }

        private void AddCue(Resident r, int kind, int value, string target, int number, float seconds)
        {
            if (!IsAuthority || !WorldAuthority.IsMultiplayer) return;
            cueSerial++;
            recentCues.Add(new ResidentCueEntry { s = cueSerial, id = r.Id, k = kind, v = value, t = target ?? "", n = number, d = seconds });
            while (recentCues.Count > ResidentTuning.LiveCueBuffer) recentCues.RemoveAt(0);
        }

        private void PlayRemoteCues(List<ResidentCueEntry> cues)
        {
            int max = cueLastSeen;
            if (cues != null) foreach (var c in cues) if (c != null && c.s > max) max = c.s;
            if (!cuePrimed)
            {
                // 参加直後：過去の身振りは再生しない
                cuePrimed = true;
                cueLastSeen = max;
                return;
            }
            if (cues == null || cues.Count == 0) return;
            var sorted = new List<ResidentCueEntry>(cues);
            sorted.Sort((a, b) => (a != null ? a.s : 0).CompareTo(b != null ? b.s : 0));
            foreach (var c in sorted)
            {
                if (c == null || c.s <= cueLastSeen) continue;
                var r = GetResident(c.id);
                if (r == null || r.Avatar == null) continue;
                if (c.k == 0) r.Avatar.ShowExpression((Expression)c.v, c.d > 0f ? c.d : 3f);
                else r.Avatar.PlayGesture(new GestureCue((Gesture)c.v, NullIfEmpty(c.t), c.n));
            }
            cueLastSeen = max;
        }

        // ---------------- 家の中の代理の姿 ----------------

        public void SetIndoorProxy(string residentId, Transform proxy, string areaId, int untilMinuteOfDay)
        {
            var r = GetResident(residentId);
            if (r == null) return;
            if (proxy == null)
            {
                ClearIndoorProxyOf(r);
                return;
            }
            long dayStart = Now - MinuteOfDay;
            long until = dayStart + Mathf.Clamp(untilMinuteOfDay, 0, TimeSystem.MinutesPerDay);
            if (until <= Now) until += TimeSystem.MinutesPerDay;
            var st = r.State;
            r.indoorProxy = proxy;
            r.indoorAreaId = string.IsNullOrEmpty(areaId) ? "indoor" : areaId;
            r.indoorUntil = until;
            r.indoorByGoTo = st.hasGoTo;
            r.indoorAnchor = IndoorAnchorOf(r);
            // 家の中にいる間は外での立ち話をしない
            if (IsAuthority && !string.IsNullOrEmpty(st.chatPartnerId)) EndChat(r);
            SyncAvatar(r);
        }

        public void ClearIndoorProxy(string residentId)
        {
            var r = GetResident(residentId);
            if (r != null) ClearIndoorProxyOf(r);
        }

        private void ClearIndoorProxyOf(Resident r)
        {
            if ((object)r.indoorProxy == null && r.indoorAreaId == null) return;
            r.indoorProxy = null;
            r.indoorAreaId = null;
            r.indoorUntil = 0;
            r.indoorByGoTo = false;
            SyncAvatar(r);
        }

        /// <summary>
        /// 代理を設定した時の本体の居場所：一時的な行き先（訪問）があればその行き先、家の近くなら家の前、それ以外は今の位置。
        /// 本体がまだ向かっている途中でも、近づく分には解除しない。
        /// </summary>
        private Vector3 IndoorAnchorOf(Resident r)
        {
            var st = r.State;
            var body = r.BodyPosition;
            if (st.hasGoTo)
            {
                // ゲストの行き先は全体状態（間引き）でしか届かないので、明らかに古い行き先は使わない
                var goal = new Vector3(st.goX, st.goY, st.goZ);
                if (IsAuthority || ResidentUtil.FlatDistance(goal, body) < 40f) return goal;
            }
            var yard = HomeYard(r);
            return ResidentUtil.FlatDistance(body, yard) <= IndoorLeaveDistance ? yard : body;
        }

        /// <summary>RequestGoTo：家の中の代理から遠くへ向かわせる時は代理を解除する（同じ家の前へ向かわせ直すだけなら保つ）。</summary>
        private void OnGoToRequested(Resident r, Vector3 goal)
        {
            if ((object)r.indoorProxy == null && r.indoorAreaId == null) return;
            if (ResidentUtil.FlatDistance(goal, r.indoorAnchor) > IndoorLeaveDistance) ClearIndoorProxyOf(r);
            else r.indoorByGoTo = true;
        }

        /// <summary>期限・予定の終わり・代理の破棄で自動的に元へ戻す（各端末）。</summary>
        private void TickIndoorProxies()
        {
            for (int i = 0; i < ordered.Count; i++)
            {
                var r = ordered[i];
                if ((object)r.indoorProxy == null && r.indoorAreaId == null) continue;
                var st = r.State;
                // 予定の終わりは「本体が居場所から離れた」で判定する（ホストは AI の位置、ゲストは同期された位置。
                // 家の中で次の予定も在宅なら本体は動かないので保たれる）
                bool clear = r.indoorProxy == null                      // 代理が破棄された
                             || Now >= r.indoorUntil                    // 期限
                             || !st.active && !r.IsVisitingFormer
                             || r.indoorByGoTo && !st.hasGoTo           // 一時的な行き先（訪問）が終わった
                             || ResidentUtil.FlatDistance(r.BodyPosition, r.indoorAnchor) > IndoorLeaveDistance; // 出かけた
                if (clear) ClearIndoorProxyOf(r);
            }
        }

        public void OpenConversation(string residentId, PlayerState player)
        {
            var r = GetResident(residentId);
            if (r == null || !(r.State.active || r.IsVisitingFormer)) return;
            BeginConversation(r, player ?? LocalPlayer);
        }

        // ---------------- プレイヤー個人への記録（手紙・観察） ----------------

        private bool IsLocalPlayerId(string playerId)
        {
            var local = Game != null ? Game.LocalPlayer : null;
            return local != null && local.PlayerId == playerId;
        }

        /// <summary>リモートのプレイヤーが今この星に接続しているか（ホストから届けられるか）。</summary>
        private bool IsRemoteOnline(string playerId)
        {
            if (WorldAuthority.Role != NetRole.Host || WorldAuthority.Bridge == null || Game == null) return false;
            var p = Game.Players.Get(playerId);
            return p != null && !p.IsLocal;
        }

        /// <summary>
        /// 住民辞典の観察記録をそのプレイヤーへ届ける。ローカルなら直接、接続中のリモートなら RecordResidentObservationFor、
        /// 接続していなければ次に参加した時に届ける（ホストだけ）。1日1回・種類ごと・プレイヤーごと。
        /// </summary>
        internal void RecordObservationFor(string playerId, Resident r, string key, string ja, string en, string recordId = null)
        {
            if (string.IsNullOrEmpty(playerId) || r == null || string.IsNullOrEmpty(ja)) return;
            if (IsLocalPlayerId(playerId))
            {
                RecordObservation(r, key, ja, en, recordId);
                return;
            }
            if (!IsAuthority) return;
            if (!observedKeys.Add(playerId + "|" + r.Id + "|" + Today + "|" + key)) return;
            if (IsRemoteOnline(playerId))
            {
                var tablet = Game.Service<ITabletService>();
                if (tablet == null) return;
                try { tablet.RecordResidentObservationFor(playerId, r.Id, ja, en, recordId); }
                catch (Exception ex) { Debug.LogException(ex); }
                return;
            }
            AddPending(new PendingPlayerRecord { playerId = playerId, kind = "observation", residentId = r.Id, day = Today, minuteOfDay = MinuteOfDay, factJa = ja, factEn = en });
        }

        private void AddPending(PendingPlayerRecord p)
        {
            if (meta.pendingRecords == null) meta.pendingRecords = new List<PendingPlayerRecord>();
            meta.pendingRecords.Add(p);
            while (meta.pendingRecords.Count > MaxPendingRecords) meta.pendingRecords.RemoveAt(0);
            MarkDirty();
        }

        /// <summary>引っ越した住民からの手紙を1人のプレイヤーへ（手紙の品物＋資料ボックス）。接続していなければ参加時に届ける。</summary>
        private void DeliverLetter(Resident r, string playerId, int day, int minuteOfDay)
        {
            bool local = IsLocalPlayerId(playerId);
            if (!local && !IsRemoteOnline(playerId))
            {
                AddPending(new PendingPlayerRecord { playerId = playerId, kind = "letter", residentId = r.Id, day = day, minuteOfDay = minuteOfDay });
                return;
            }
            var speech = Game.Service<ISpeechService>();
            var u = ResidentLines.Letter(r, playerId, this);
            u.addresseeId = playerId;
            UtteranceRecord rec = null;
            try { rec = speech?.CreateRecord(u, SpeechChannel.Letter, "resident", Vector3.zero); }
            catch (Exception ex) { Debug.LogException(ex); }
            var stack = new ItemStack(SharedItemIds.Letter, 1) { data = rec != null ? rec.recordId : null };
            var mat = new TabletMaterial
            {
                id = "letter." + r.Id + "." + day,
                kind = TabletMaterialKind.Letter,
                titleJa = "手紙",
                titleEn = "Letter",
                factJa = string.Format("{0}日目に届いた手紙。引っ越していった住民から。", day),
                factEn = string.Format("A letter that arrived on day {0}, from a resident who moved away.", day),
                day = day,
                minuteOfDay = minuteOfDay,
                linkedRecordId = rec != null ? rec.recordId : null,
                linkedResidentId = r.Id
            };
            mat.tags.Add("letter");
            var tablet = Game.Service<ITabletService>();
            if (local)
            {
                Game.LocalPlayer.GiveOrDrop(stack, "resident_letter");
                if (tablet != null)
                {
                    try { tablet.AddMaterial(mat); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
                UIManager.Toast(Loc.T("手紙が届きました", "A letter has arrived"));
                return;
            }
            WorldAuthority.Bridge?.GrantItemToRemote(playerId, stack, "resident_letter");
            if (tablet != null)
            {
                try { tablet.AddMaterialFor(playerId, mat, rec); }
                catch (Exception ex) { Debug.LogException(ex); }
            }
            WorldAuthority.Bridge?.NotifyRemote(playerId, "手紙が届きました", "A letter has arrived");
        }

        /// <summary>ホスト：プレイヤーが参加した。不在の間の手紙・観察記録を届ける。</summary>
        private void OnPlayerJoined(PlayerJoinedEvent e)
        {
            if (!IsAuthority || string.IsNullOrEmpty(e.PlayerId) || meta.pendingRecords == null || meta.pendingRecords.Count == 0) return;
            var mine = meta.pendingRecords.FindAll(p => p != null && p.playerId == e.PlayerId);
            if (mine.Count == 0) return;
            meta.pendingRecords.RemoveAll(p => p == null || p.playerId == e.PlayerId);
            var tablet = Game.Service<ITabletService>();
            foreach (var p in mine)
            {
                var r = GetResident(p.residentId);
                if (r == null) continue;
                if (p.kind == "letter") DeliverLetter(r, e.PlayerId, p.day, p.minuteOfDay);
                else if (IsRemoteOnline(e.PlayerId) && tablet != null)
                {
                    try { tablet.RecordResidentObservationFor(e.PlayerId, r.Id, p.factJa, p.factEn); }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
                else AddPending(p);
            }
            MarkDirty();
        }

        // ---------------- 発話の届く範囲 ----------------

        /// <summary>この場所の近く（外）にいずれかのプレイヤーがいるか（ホストだけが生成する発話の判定）。</summary>
        private bool AnyPlayerNear(Vector3 pos, float radius)
        {
            if (Game == null) return false;
            foreach (var p in Game.Players.All)
            {
                if (p == null || p.CurrentAreaId != AreaIds.Overworld) continue;
                if (ResidentUtil.FlatDistance(p.Position, pos) <= radius) return true;
            }
            return false;
        }
    }
}
