using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Tablet
{
    /// <summary>
    /// 個人への記録（マルチ）：ホストで起きた事実を、その本人（ゲスト）のタブレットへ届ける。
    ///  ・AddMaterialFor / RecordResidentObservationFor：宛先がローカルなら従来どおり、リモートなら一時メッセージ
    ///    <see cref="RecordChannel"/> で送り、受信側で自分の知識（PlayerPlanetKnowledge）へ入れる。
    ///  ・資料・観察に結び付いた発話の原本も一緒に送り、受信側で <see cref="KnowledgeData.receivedRecords"/> に保持する
    ///    （ゲストの会話ログには無いため。音声カード化・辞書の証拠に使える）。
    ///  ・ホストでしか発行されない共有イベント（依頼の達成・贈り物・商店の購入・はじめて話した）を、ゲスト本人へ転送する。
    /// 送るのは観察できた事実だけ（内部の好み・正解の訳は送らない）。
    /// </summary>
    public sealed partial class TabletSystem
    {
        public const string RecordChannel = "tablet.record";

        /// <summary>同じ出来事が続けて届いた時に同じメッセージを何度も送らないための直前のキー。</summary>
        private string lastForwardKey;
        /// <summary>このセッションで「はじめて話した」を転送した (playerId|residentId)。</summary>
        private readonly HashSet<string> forwardedMet = new HashSet<string>();

        private bool IsLocalTarget(string playerId) => string.IsNullOrEmpty(playerId) || playerId == LocalId;

        /// <summary>リモートの本人へ送れるか（ホスト側のマルチのみ。ゲストからホストへ記録を押し込むことはしない）。</summary>
        private static bool CanSendToRemote => WorldAuthority.IsMultiplayer && WorldAuthority.IsAuthority;

        // ------------------------------------------------------------
        // ITabletService：特定のプレイヤーへの記録
        // ------------------------------------------------------------

        public void AddMaterialFor(string playerId, TabletMaterial material, UtteranceRecord linkedRecord = null)
        {
            if (material == null) return;
            var copy = JsonUtility.FromJson<TabletMaterial>(JsonUtility.ToJson(material));
            if (string.IsNullOrEmpty(copy.id)) copy.id = Ids.New();
            if (copy.day <= 0 && Clock != null)
            {
                copy.day = Today;
                copy.minuteOfDay = NowMinute;
            }
            if (linkedRecord != null && string.IsNullOrEmpty(copy.linkedRecordId)) copy.linkedRecordId = linkedRecord.recordId;
            if (IsLocalTarget(playerId))
            {
                AddMaterial(copy);
                // 会話ログから消えても資料から原本を引けるように写しを持つ
                if (linkedRecord != null) KeepRecord(linkedRecord);
                return;
            }
            if (!CanSendToRemote) return;
            var msg = new TabletRecordMessage { hasMaterial = true, material = copy };
            var rec = linkedRecord ?? ResolveRecord(copy.linkedRecordId);
            if (rec != null)
            {
                msg.hasRecord = true;
                msg.record = rec;
            }
            Send(playerId, msg);
        }

        public void RecordResidentObservationFor(string playerId, string residentId, string factJa, string factEn, string linkedRecordId = null)
        {
            if (string.IsNullOrEmpty(residentId) || (string.IsNullOrEmpty(factJa) && string.IsNullOrEmpty(factEn))) return;
            ObserveFor(playerId, residentId, "observed", factJa, factEn, linkedRecordId, -1, -1);
        }

        /// <summary>住民辞典へ観察した事実を記録する（宛先がリモートならその本人へ送る）。kind は観察の種類（"purchase" "gift" "quest" など）。</summary>
        internal void ObserveFor(string playerId, string residentId, string kind, string factJa, string factEn, string linkedRecordId, int day, int minute)
        {
            if (string.IsNullOrEmpty(residentId)) return;
            if (IsLocalTarget(playerId))
            {
                AddObservation(GetOrCreatePage(residentId), kind, factJa, factEn, linkedRecordId, day, minute);
                return;
            }
            if (!CanSendToRemote) return;
            var msg = new TabletRecordMessage
            {
                hasObservation = true,
                observationKind = kind,
                residentId = residentId,
                factJa = factJa,
                factEn = factEn,
                linkedRecordId = linkedRecordId,
                day = day > 0 ? day : Today,
                minute = minute >= 0 ? minute : NowMinute
            };
            var rec = ResolveRecord(linkedRecordId);
            if (rec != null)
            {
                msg.hasRecord = true;
                msg.record = rec;
            }
            // 連続して同じ事実（まとめ買いで1個ずつ届く商店の購入など）は1回だけ送る
            string key = playerId + "|" + residentId + "|" + msg.day + "|" + msg.minute + "|" + factJa;
            if (key == lastForwardKey) return;
            lastForwardKey = key;
            Send(playerId, msg);
        }

        /// <summary>はじめて話した住民をその本人へ知らせる（受信側で住民辞典のページを作る）。</summary>
        private void ForwardMet(string playerId, string residentId)
        {
            if (!CanSendToRemote || string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(residentId)) return;
            if (!forwardedMet.Add(playerId + "|" + residentId)) return;
            Send(playerId, new TabletRecordMessage { metResidentId = residentId });
        }

        private static void Send(string playerId, TabletRecordMessage msg)
        {
            try { WorldAuthority.SendTransient(playerId, RecordChannel, JsonUtility.ToJson(msg)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------
        // 受信
        // ------------------------------------------------------------

        private void OnRecordMessage(string fromPlayerId, string json)
        {
            // 星の正本はホスト。ホスト（権威側）はゲストからの記録の押し込みを受け付けない（自分宛ての直接呼び出しは除く）
            if (WorldAuthority.IsAuthority && fromPlayerId != LocalId) return;
            if (string.IsNullOrEmpty(json) || Game == null || Game.Planet == null) return;
            TabletRecordMessage m;
            try { m = JsonUtility.FromJson<TabletRecordMessage>(json); }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            if (m == null) return;
            Know.EnsureLists();

            if (!string.IsNullOrEmpty(m.metResidentId)) RecordResidentMet(m.metResidentId);

            if (m.hasObservation && !string.IsNullOrEmpty(m.residentId))
            {
                AddObservation(GetOrCreatePage(m.residentId), string.IsNullOrEmpty(m.observationKind) ? "observed" : m.observationKind,
                    m.factJa, m.factEn, string.IsNullOrEmpty(m.linkedRecordId) ? null : m.linkedRecordId, m.day, m.minute);
            }

            if (m.hasMaterial && m.material != null && !string.IsNullOrEmpty(m.material.id))
            {
                bool isNew = FindMaterial(m.material.id) == null;
                AddMaterial(m.material);
                if (isNew)
                {
                    string title = Loc.T(m.material.titleJa, m.material.titleEn);
                    UIManager.Toast(string.IsNullOrEmpty(title)
                        ? Loc.T("資料ボックスに記録が届きました", "A record arrived in the materials box")
                        : Loc.Format("資料ボックスに記録が届きました（{0}）", "A record arrived in the materials box ({0})", title), 3f);
                }
            }

            // 原本は資料・観察を入れた後で保持する（参照されているものは整理で消さないため）
            if (m.hasRecord && m.record != null) KeepRecord(m.record);
        }

        // ------------------------------------------------------------
        // 発話の原本の自前保持
        // ------------------------------------------------------------

        /// <summary>発話の原本の写しを保持する（同じ記録IDは1つ。音声カードとして保存済みならカードの原本が優先される）。</summary>
        internal void KeepRecord(UtteranceRecord rec)
        {
            if (rec == null || string.IsNullOrEmpty(rec.recordId)) return;
            if (Know.receivedRecords == null) Know.receivedRecords = new List<UtteranceRecord>();
            foreach (var r in Know.receivedRecords) if (r != null && r.recordId == rec.recordId) return;
            Know.receivedRecords.Add(TabletText.CloneRecord(rec));
            TrimReceivedRecords();
        }

        internal UtteranceRecord FindReceivedRecord(string recordId)
        {
            if (string.IsNullOrEmpty(recordId) || Know.receivedRecords == null) return null;
            foreach (var r in Know.receivedRecords) if (r != null && r.recordId == recordId) return r;
            return null;
        }

        /// <summary>上限を超えたら、資料・観察から参照されていない古いものから捨てる（それでも多ければ古い順）。</summary>
        private void TrimReceivedRecords()
        {
            var list = Know.receivedRecords;
            int over = list.Count - TabletTuning.MaxReceivedRecords;
            if (over <= 0) return;
            var used = new HashSet<string>();
            foreach (var m in Know.materials) if (m != null && !string.IsNullOrEmpty(m.linkedRecordId)) used.Add(m.linkedRecordId);
            foreach (var p in Know.residents)
                foreach (var o in p.observations)
                    if (o != null && !string.IsNullOrEmpty(o.linkedRecordId)) used.Add(o.linkedRecordId);
            for (int i = 0; i < list.Count && over > 0;)
            {
                var r = list[i];
                if (r == null || !used.Contains(r.recordId))
                {
                    list.RemoveAt(i);
                    over--;
                }
                else i++;
            }
            while (list.Count > TabletTuning.MaxReceivedRecords) list.RemoveAt(0);
        }
    }
}
