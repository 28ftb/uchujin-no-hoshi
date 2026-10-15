using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Network
{
    /// <summary>
    /// 辞書の任意の手渡し（マルチ §13、宇宙語 §12、タブレット §12）。
    ///  ・自動配布は絶対にしない。渡すのは持ち主の明示的な操作だけ。
    ///  ・範囲は「全部」か「選んだ語だけ」。受け取る側は「別冊として保存／自分の辞書に統合／受け取らない」を選ぶ。
    ///  ・正誤は判定しない（推測やうそが混ざっていてもそのまま渡る）。中身の形式は Tablet モジュールが決める。
    /// </summary>
    public sealed partial class NetworkSystem
    {
        /// <summary>辞書を渡す。surfaces が null なら全部。</summary>
        public void OfferDictionary(string toPlayerId, IList<string> surfaces)
        {
            if (!IsOnline || string.IsNullOrEmpty(toPlayerId) || toPlayerId == LocalId) return;
            var tablet = Game.Service<ITabletService>();
            if (tablet == null)
            {
                UIManager.Toast(Loc.T("タブレットが無いので辞書を渡せません", "No tablet - cannot share a dictionary"));
                return;
            }
            string json = tablet.ExportDictionary(surfaces);
            if (string.IsNullOrEmpty(json))
            {
                UIManager.Toast(Loc.T("渡せる記述がありません", "Nothing to share"));
                return;
            }
            var msg = new DictionaryOfferMsg
            {
                offerId = Ids.New(),
                fromPlayerId = LocalId,
                fromName = Game.LocalPlayer.DisplayName,
                toPlayerId = toPlayerId,
                all = surfaces == null,
                wordCount = surfaces != null ? surfaces.Count : 0,
                json = json
            };
            if (IsHostSession) HostRouteDictionaryOffer(null, msg);
            else SendToHost(NetMsg.DictionaryOffer, msg);
            UIManager.Toast(Loc.Format("{0}さんに辞書を差し出しました", "You offered your dictionary to {0}", GetPlayerName(toPlayerId)));
        }

        /// <summary>ホスト：辞書の受け渡しを中継（送り主の名前はホストが確認したものに書き換える）。</summary>
        private void HostRouteDictionaryOffer(SessionPlayer sender, DictionaryOfferMsg msg)
        {
            if (msg == null) return;
            if (sender != null)
            {
                msg.fromPlayerId = sender.PlayerId;
                msg.fromName = sender.DisplayName;
            }
            if (msg.toPlayerId == LocalId)
            {
                OnDictionaryOfferReceived(msg);
                return;
            }
            if (players.TryGetValue(msg.toPlayerId, out var target) && !target.IsLocal && target.Ready)
            {
                SendTo(target, NetMsg.DictionaryOffer, msg);
                return;
            }
            if (sender == null) UIManager.Toast(Loc.T("相手が見つかりません", "That player is not here"));
            else NotifyRemote(sender.PlayerId, "相手が見つかりません", "That player is not here");
        }

        private void HostRouteDictionaryReply(SessionPlayer sender, DictionaryReplyMsg msg)
        {
            if (msg == null) return;
            if (sender != null)
            {
                msg.responderId = sender.PlayerId;
                msg.responderName = sender.DisplayName;
            }
            if (msg.toPlayerId == LocalId)
            {
                OnDictionaryReplyReceived(msg);
                return;
            }
            if (players.TryGetValue(msg.toPlayerId, out var target) && !target.IsLocal) SendTo(target, NetMsg.DictionaryReply, msg);
        }

        private void OnDictionaryOfferReceived(DictionaryOfferMsg msg)
        {
            if (msg == null || string.IsNullOrEmpty(msg.json)) return;
            string range = msg.all ? Loc.T("全部", "the whole dictionary") : Loc.Format("{0}語ぶん", "{0} words", msg.wordCount);
            string text = Loc.Format(
                "{0}さんが、自分で書いた宇宙語の辞書（{1}）を渡そうとしています。\n中身が正しいかは分かりません（推測やまちがい、うそが混ざっていることもあります）。\n受け取りますか？",
                "{0} wants to give you their own alien-language dictionary ({1}).\nThere is no telling whether it is correct (it may contain guesses, mistakes or even lies).\nAccept it?",
                msg.fromName, range);
            UIManager.Choose(Loc.T("辞書の手渡し", "Dictionary offer"), text, new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("別冊として保存する（自分の記述と混ぜない）", "Keep as a separate volume"), () => AcceptDictionary(msg, true)),
                new ChoiceOption(Loc.T("自分の辞書に統合する", "Merge into my dictionary"), () => AcceptDictionary(msg, false)),
                new ChoiceOption(Loc.T("受け取らない", "Decline"), () => SendDictionaryReply(msg, DictionaryReplyChoice.Declined))
            });
        }

        private void AcceptDictionary(DictionaryOfferMsg msg, bool separate)
        {
            var tablet = Game.Service<ITabletService>();
            if (tablet == null)
            {
                UIManager.Toast(Loc.T("タブレットが無いので受け取れません", "No tablet - cannot receive"));
                SendDictionaryReply(msg, DictionaryReplyChoice.NoTablet);
                return;
            }
            tablet.ImportDictionary(msg.json, msg.fromName, separate);
            UIManager.Toast(separate
                ? Loc.Format("{0}さんの辞書を別冊として保存しました", "Saved {0}'s dictionary as a separate volume", msg.fromName)
                : Loc.Format("{0}さんの辞書を自分の辞書に統合しました", "Merged {0}'s dictionary into yours", msg.fromName));
            SendDictionaryReply(msg, separate ? DictionaryReplyChoice.SeparateVolume : DictionaryReplyChoice.Merged);
        }

        private void SendDictionaryReply(DictionaryOfferMsg offer, DictionaryReplyChoice choice)
        {
            var reply = new DictionaryReplyMsg
            {
                offerId = offer.offerId,
                responderId = LocalId,
                responderName = Game.LocalPlayer.DisplayName,
                toPlayerId = offer.fromPlayerId,
                choice = (int)choice
            };
            if (IsHostSession) HostRouteDictionaryReply(null, reply);
            else if (IsOnline) SendToHost(NetMsg.DictionaryReply, reply);
        }

        private void OnDictionaryReplyReceived(DictionaryReplyMsg msg)
        {
            if (msg == null) return;
            string who = msg.responderName ?? GetPlayerName(msg.responderId);
            switch ((DictionaryReplyChoice)msg.choice)
            {
                case DictionaryReplyChoice.SeparateVolume:
                    UIManager.Toast(Loc.Format("{0}さんが辞書を受け取りました（別冊）", "{0} accepted your dictionary (separate volume)", who));
                    break;
                case DictionaryReplyChoice.Merged:
                    UIManager.Toast(Loc.Format("{0}さんが辞書を受け取りました（統合）", "{0} merged your dictionary", who));
                    break;
                case DictionaryReplyChoice.NoTablet:
                    UIManager.Toast(Loc.Format("{0}さんは受け取れませんでした", "{0} could not receive it", who));
                    break;
                default:
                    UIManager.Toast(Loc.Format("{0}さんは辞書を受け取りませんでした", "{0} declined your dictionary", who));
                    break;
            }
        }

        /// <summary>
        /// 「選んだ語だけ」の候補：自分の辞書に書いた語（ITabletService.GetDictionarySurfaces）。
        /// 表示は表面形（聞こえた音）と自分の推測訳だけ（推測訳が無くメモだけの語は「（メモ）」）。内部の意味は表示しない。
        /// 辞書の一覧が取れない場合は、最近聞いた発話の表面形のうち推測訳を書いた語を候補にする。
        /// </summary>
        public List<KeyValuePair<string, string>> CandidateDictionaryWords()
        {
            var result = new List<KeyValuePair<string, string>>();
            var tablet = Game.Service<ITabletService>();
            if (tablet == null) return result;
            var seen = new HashSet<string>();
            IReadOnlyList<string> surfaces = null;
            try { surfaces = tablet.GetDictionarySurfaces(); }
            catch (System.Exception e) { UnityEngine.Debug.LogException(e); }
            if (surfaces != null)
            {
                foreach (var surface in surfaces)
                {
                    if (string.IsNullOrEmpty(surface) || !seen.Add(surface)) continue;
                    string gloss = tablet.GetPlayerGloss(surface);
                    result.Add(new KeyValuePair<string, string>(surface, string.IsNullOrEmpty(gloss) ? Loc.T("メモ", "notes") : gloss));
                }
            }
            var speech = Game.Service<ISpeechService>();
            if (result.Count == 0 && speech != null && speech.RecentRecords != null)
            {
                foreach (var rec in speech.RecentRecords)
                {
                    if (rec?.tokens == null) continue;
                    foreach (var tok in rec.tokens)
                    {
                        if (tok == null || string.IsNullOrEmpty(tok.surface) || !seen.Add(tok.surface)) continue;
                        string gloss = tablet.GetPlayerGloss(tok.surface);
                        if (!string.IsNullOrEmpty(gloss)) result.Add(new KeyValuePair<string, string>(tok.surface, gloss));
                    }
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return result;
        }
    }
}
