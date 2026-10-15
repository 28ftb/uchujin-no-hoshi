using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// エンディング「最後の引っ越し」（メインストーリー最終章、ロケット §19）。
    ///   住民たちがロケットの周りに集まる（親しさで反応が変わる：泣く・プレゼント・遠くから見ているだけ）
    ///   → 修理屋の最後の点検 → 料理好きの旅のお弁当 → 仲の良い住民のプレゼント → 引っ越した住民からの手紙
    ///   → 住民たちが次々に宇宙語で言葉をかける（翻訳しない。推測訳の併記が on ならプレイヤー自身の推測だけを添える）
    ///   → 最後に案内役が口癖をもう一度 → ハッチが閉まり離陸 → 小さくなる村
    ///   → 主人公がいない村のいつもの生活（商店の客・広場・家の前で立ち止まる住民・数日後の新しい住民）
    ///   → 故郷の星が見える宇宙船の窓 → プレイヤーが書いた辞書 → 着陸 → クレジット → クリア後の選択。
    /// プレイヤーの暮らしを反映する（常連客・手紙・熱中した活動）。言語理解の割合で「正解エンディング」は解放しない。
    /// 交流が少なくても同じように帰還できる。
    /// </summary>
    public sealed class EndingSequence
    {
        private enum Tier { Far, Near, Close }

        private class Attendee
        {
            public IResidentInfo info;
            public int friendship;
            public int purchases;
            public int gifts;
            public Tier tier;
            public bool regular;
            public Vector3 spot;
        }

        private readonly StorySystem story;
        private CutsceneDirector director;
        private Transform fxRoot;
        private RocketProxy proxy;
        private readonly RendererHider hider = new RendererHider();
        private CabinSet cabin;
        private DictionaryPage dictPage;
        private EndingRecord record;
        private readonly List<UtteranceRecord> heard = new List<UtteranceRecord>();
        private bool grantItems;
        /// <summary>受け取り済みの物（"bento" "gift:&lt;id&gt;" "letter:&lt;id&gt;"）。スキップ時の重複防止。</summary>
        private readonly HashSet<string> granted = new HashSet<string>();
        public bool Running { get; private set; }

        private Vector3 rocketPos, village, toVillage, side, front;

        public EndingSequence(StorySystem story)
        {
            this.story = story;
        }

        public void Start()
        {
            director = CutsceneDirector.Create("EndingDirector");
            director.SkipEnabled = true;
            var fx = new GameObject("EndingFx");
            if (Game.Instance.SceneRoot != null) fx.transform.SetParent(Game.Instance.SceneRoot, false);
            fxRoot = fx.transform;
            Running = true;
            // 贈り物・手紙は初めての帰還の時だけ受け取る（ロケット前へ戻って何度も帰還を選んだ時に増えないように）
            grantItems = story.EndingCount == 0;
            director.StartCoroutine(Run());
        }

        public void Abort()
        {
            Running = false;
            Cleanup();
            if (director != null) UnityEngine.Object.Destroy(director.gameObject);
            director = null;
        }

        private bool Skipped => director == null || director.SkipRequested;

        private IEnumerator Run()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            record = new EndingRecord { day = game.Time != null ? game.Time.Day : 1, topActivity = story.TopActivity() };

            game.SetState(GameState.Ending);
            UIManager.HudVisible = false;
            p.LockInput("story");
            game.Time?.AddGlobalPause("story.ending");
            EventBus.Publish(new EndingStartedEvent());
            director.Letterbox = true;

            yield return director.StartCoroutine(director.Fade(true, 1f));
            yield return director.StartCoroutine(Farewell());
            bool skippedFarewell = director.SkipRequested;
            if (skippedFarewell)
            {
                // スキップしても受け取る物は受け取る（暮らしの結果）
                GrantSkippedItems();
                director.ResetSkip();
                ScreenFader.SetBlack();
            }
            yield return director.StartCoroutine(Liftoff());
            if (director.SkipRequested) { director.ResetSkip(); ScreenFader.SetBlack(); }
            // ロケットは飛び立った：代役は片付け、元のロケットは村の場面の間は隠しておく
            if (proxy != null) UnityEngine.Object.Destroy(proxy.gameObject);
            proxy = null;
            HideOriginalRocket();
            StoryActors.SetPlayerVisible(false);
            yield return director.StartCoroutine(Epilogue());
            if (director.SkipRequested) { director.ResetSkip(); ScreenFader.SetBlack(); }
            yield return director.StartCoroutine(WindowAndDictionary(director, record, heard, story.GuideCatchphraseConcept, p.Appearance, fxRoot, c => cabin = c, d => dictPage = d));
            director.ResetSkip();
            record.dictionaryWordCount = dictPage != null ? dictPage.TotalGlossed : 0;

            story.OnEndingPresented(record);

            // クレジット
            director.ExtraDraw = null;
            director.SkipEnabled = false;
            bool creditsDone = false;
            UIManager.Open(new CreditsWindow(true, record, () => creditsDone = true));
            ScreenFader.FadeIn(0.5f);
            while (!creditsDone) yield return null;

            // クリア後の選択（基本決定 §5）
            game.SetState(GameState.PostEndingChoice);
            ScreenFader.SetBlack();
            UIManager.Open(new PostEndingWindow(story));
            ScreenFader.FadeIn(0.8f);
        }

        // ------------------------------------------------------------
        // 見送り
        // ------------------------------------------------------------

        private IEnumerator Farewell()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            var dir = StoryActors.Directory;

            rocketPos = StoryPlaces.Rocket();
            village = StoryPlaces.Village();
            toVillage = StoryPlaces.Dir(rocketPos, village, Vector3.forward);
            side = StoryPlaces.Side(toVillage);
            front = StoryPlaces.Ground(rocketPos + toVillage * 3.4f);

            if (p.CurrentAreaId != AreaIds.Overworld) game.Areas.Transition(p, AreaIds.Overworld, front + Vector3.up * 0.1f);
            else p.Position = front + Vector3.up * 0.1f;
            StoryActors.FacePlayer(front + toVillage * 5f);
            StoryActors.SetPlayerVisible(true);

            if (dir == null)
            {
                director.Shot(front + toVillage * 10f + Vector3.up * 4f, rocketPos + Vector3.up * 3f, 2f, true);
                yield return director.StartCoroutine(director.Fade(false, 1.2f));
                yield return director.Wait(2.5f);
                yield break;
            }

            // ---- 参加者を決める（親しさ・常連・贈り物の回数）----
            string guideId = story.GuideResidentId;
            var guide = story.GuideInfo();
            IResidentInfo mech = null, cook = null;
            try { mech = dir.FindByJob(ResidentJob.Mechanic); } catch (Exception) { }
            try { cook = dir.FindByJob(ResidentJob.Cook); } catch (Exception) { }
            if (mech != null && (!mech.IsActive || mech.Id == guideId)) mech = null;
            if (cook != null && (!cook.IsActive || cook.Id == guideId || (mech != null && cook.Id == mech.Id))) cook = null;

            var all = new List<Attendee>();
            try
            {
                foreach (var r in dir.ActiveResidents)
                {
                    if (r == null || !r.IsActive) continue;
                    var a = new Attendee { info = r };
                    a.friendship = r.GetFriendship(p.PlayerId);
                    a.purchases = story.ShopPurchasesBy(r.Id);
                    a.gifts = story.GiftsGivenTo(r.Id);
                    a.regular = a.purchases >= StoryTuning.EndingRegularPurchases;
                    bool key = r.Id == guideId || (mech != null && r.Id == mech.Id) || (cook != null && r.Id == cook.Id);
                    if (a.friendship >= StoryTuning.EndingCloseFriendship) a.tier = Tier.Close;
                    else if (key || a.regular || a.friendship >= StoryTuning.EndingNearFriendship) a.tier = Tier.Near;
                    else a.tier = Tier.Far;
                    all.Add(a);
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            all.Sort((x, y) =>
            {
                int c = ((int)y.tier).CompareTo((int)x.tier);
                if (c != 0) return c;
                c = (y.friendship + y.purchases * 3 + y.gifts * 4).CompareTo(x.friendship + x.purchases * 3 + x.gifts * 4);
                return c != 0 ? c : string.CompareOrdinal(x.info.Id, y.info.Id);
            });

            // ---- 立ち位置 ----
            var near = new List<Attendee>();
            var far = new List<Attendee>();
            foreach (var a in all)
            {
                if (a.info.Id == guideId) { a.spot = StoryPlaces.Ground(front + toVillage * 4.2f); continue; }
                if (mech != null && a.info.Id == mech.Id) { a.spot = StoryPlaces.Ground(rocketPos + side * 3.6f + toVillage * 0.8f); continue; }
                if (cook != null && a.info.Id == cook.Id) { a.spot = StoryPlaces.Ground(front + toVillage * 3.2f - side * 2.6f); continue; }
                if (a.tier == Tier.Far) far.Add(a); else near.Add(a);
            }
            PlaceArc(near, StoryTuning.EndingRingNear, StoryTuning.EndingRingMid, 75f, 7);
            PlaceArc(far, StoryTuning.EndingRingFar, StoryTuning.EndingRingFar + 5f, 110f, 10);

            var moves = new List<KeyValuePair<string, Vector3>>();
            foreach (var a in all)
            {
                // 少し離れた場所から歩いて集まってくる
                StoryActors.Warp(a.info.Id, a.spot + StoryPlaces.Dir(front, a.spot, toVillage) * 9f);
                moves.Add(new KeyValuePair<string, Vector3>(a.info.Id, a.spot));
                if (a.tier != Tier.Far) record.farewellResidentIds.Add(a.info.Id);
                else record.watcherResidentIds.Add(a.info.Id);
                if (a.tier == Tier.Close) record.closeFriendCount++;
                if (a.regular) record.regularCount++;
            }
            record.farewellCount = record.farewellResidentIds.Count;

            // ---- 1. 集まってくる ----
            director.Shot(rocketPos - toVillage * 6f + side * 5f + Vector3.up * 9f, front + toVillage * 6f, 1.2f, true);
            yield return director.StartCoroutine(director.Fade(false, 1.5f));
            yield return director.StartCoroutine(StoryActors.MoveResidents(director, moves, 1.8f, 9f));
            foreach (var a in all) StoryActors.Face(a.info.Id, front);
            yield return director.Wait(1.5f);
            if (Skipped) yield break;

            // ---- 2. 修理屋は最後まで点検 ----
            if (mech != null)
            {
                var ms = StoryActors.Get(mech.Id);
                StoryActors.Face(mech.Id, rocketPos + Vector3.up * 2f);
                director.Shot(rocketPos + side * 7f + toVillage * 4f + Vector3.up * 2.5f, Vector3.Lerp(rocketPos, ms != null ? ms.Position : rocketPos, 0.6f) + Vector3.up * 1.5f, 2f);
                yield return director.Wait(1f);
                heard.Add(StoryActors.Say(director, mech.Id, StoryUtterances.MechanicFinalCheck(mech.Id), SpeechChannel.Direct, 4.5f));
                yield return director.Wait(4.2f);
                if (Skipped) yield break;
            }

            // ---- 3. 料理好きが旅のための料理を持ってくる ----
            if (cook != null)
            {
                var cookTo = StoryPlaces.Ground(front + toVillage * 1.5f - side * 0.9f);
                director.Shot(front + toVillage * 1.5f - side * 5f + Vector3.up * 2f, Vector3.Lerp(front, cookTo, 0.5f) + Vector3.up * 1.1f, 2f);
                yield return director.StartCoroutine(StoryActors.MoveResident(director, cook.Id, cookTo, 1f, 4f));
                StoryActors.Face(cook.Id, front);
                StoryActors.FacePlayer(cookTo);
                heard.Add(StoryActors.Say(director, cook.Id, StoryUtterances.CookBento(cook.Id, StoryTuning.ItemTravelBento), SpeechChannel.Direct, 4.5f));
                yield return director.Wait(1.4f);
                if (grantItems && granted.Add("bento")) GiveItem(new ItemStack(StoryTuning.ItemTravelBento, 1), "story.ending.cook");
                StoryActors.DoPlayerGesture(PlayerGesture.Bow);
                yield return director.Wait(3f);
                StoryActors.GoTo(cook.Id, StoryPlaces.Ground(front + toVillage * 3.2f - side * 2.6f));
                if (Skipped) yield break;
            }

            // ---- 4. 仲の良い住民のプレゼント（泣いている住民もいる）----
            int givers = 0;
            foreach (var a in near)
            {
                if (a.tier != Tier.Close || givers >= StoryTuning.EndingMaxGiftGivers) continue;
                givers++;
                bool crying = a.friendship >= StoryTuning.EndingCryingFriendship;
                var to = StoryPlaces.Ground(front + toVillage * 1.5f + side * (givers % 2 == 0 ? -0.9f : 0.9f));
                director.Shot(front + toVillage * 1.2f + side * 4.5f + Vector3.up * 1.9f, Vector3.Lerp(front, to, 0.5f) + Vector3.up * 1.1f, 2f);
                yield return director.StartCoroutine(StoryActors.MoveResident(director, a.info.Id, to, 1f, 4f));
                StoryActors.Face(a.info.Id, front);
                StoryActors.FacePlayer(to);
                heard.Add(StoryActors.Say(director, a.info.Id, StoryUtterances.CloseFriendGift(a.info.Id, crying), SpeechChannel.Direct, 5f));
                yield return director.Wait(1.5f);
                GrantGift(a.info.Id);
                StoryActors.DoPlayerGesture(crying ? PlayerGesture.Sad : PlayerGesture.Bow);
                yield return director.Wait(3.4f);
                StoryActors.GoTo(a.info.Id, a.spot);
                if (Skipped) yield break;
            }

            // ---- 5. 引っ越していった住民からの手紙 ----
            yield return director.StartCoroutine(Letters(dir, guideId));
            if (Skipped) yield break;

            // ---- 6. 住民たちが次々に言葉をかける（翻訳しない）----
            var speakers = new List<Attendee>();
            string top = record.topActivity;
            ResidentJob? masterJob = JobForActivity(top);
            foreach (var a in near)
            {
                if (speakers.Count >= StoryTuning.EndingMaxFarewellSpeakers) break;
                speakers.Add(a);
            }
            foreach (var a in speakers)
            {
                var r = a.info;
                Utterance u;
                if (masterJob.HasValue && r.Job == masterJob.Value && !string.IsNullOrEmpty(top)) u = StoryUtterances.ActivityMemory(r.Id, top);
                else if (a.regular) u = StoryUtterances.RegularCustomer(r.Id);
                else if (r.Job == ResidentJob.Child) u = StoryUtterances.ChildExcited(r.Id);
                else if (r.Job == ResidentJob.Traveler) u = StoryUtterances.TravelerDream(r.Id);
                else u = StoryUtterances.PlainFarewell(r.Id);
                var sp = StoryActors.Get(r.Id);
                Vector3 rp = sp != null ? sp.Position : a.spot;
                director.Shot(front - StoryPlaces.Dir(front, rp, toVillage) * 2.2f + Vector3.up * 2.1f + side * 0.6f, rp + Vector3.up * 1.1f, 2.2f);
                yield return director.Wait(0.5f);
                StoryActors.Face(r.Id, front);
                heard.Add(StoryActors.Say(director, r.Id, u, SpeechChannel.Direct, 4f));
                yield return director.Wait(3.3f);
                if (Skipped) yield break;
                if (speakers.IndexOf(a) % 3 == 1) StoryActors.DoPlayerGesture(PlayerGesture.Wave);
            }

            // ---- 7. あまり交流しなかった住民は、遠くから見ているだけ ----
            if (far.Count > 0)
            {
                var fc = far[far.Count / 2].spot;
                director.Shot(front + Vector3.up * 3f - toVillage * 1.5f, fc + Vector3.up, 0.9f);
                yield return director.Wait(3.2f);
                if (Skipped) yield break;
            }

            // ---- 8. 最後に、案内役が口癖をもう一度 ----
            if (guide != null)
            {
                var gTo = StoryPlaces.Ground(front + toVillage * 1.4f);
                director.Shot(front + toVillage * 0.7f + side * 3.4f + Vector3.up * 1.5f, Vector3.Lerp(front, gTo, 0.5f) + Vector3.up * 1f, 1.5f);
                yield return director.StartCoroutine(StoryActors.MoveResident(director, guideId, gTo, 0.8f, 5f));
                StoryActors.Face(guideId, front);
                StoryActors.FacePlayer(gTo);
                yield return director.Wait(1.6f);
                var rec = StoryActors.Say(director, guideId, StoryUtterances.Catchphrase(guideId, story.GuideCatchphraseConcept, Expression.Happy), SpeechChannel.Direct, 5.5f);
                heard.Add(rec);
                if (rec != null) record.guideLastRecordId = rec.recordId;
                try { StoryActors.Directory.AddMemory(guideId, new ResidentMemoryEntry { kind = "farewell", playerId = p.PlayerId, day = record.day, importance = 1f }); }
                catch (Exception) { }
                yield return director.Wait(4.5f);
                StoryActors.DoPlayerGesture(PlayerGesture.Wave);
                yield return director.Wait(1.8f);
            }
            foreach (var h in heard) if (h != null && !string.IsNullOrEmpty(h.recordId)) record.farewellRecordIds.Add(h.recordId);
        }

        private void PlaceArc(List<Attendee> list, float r0, float r1, float spreadDeg, int perRow)
        {
            for (int i = 0; i < list.Count; i++)
            {
                int row = i / perRow;
                int idx = i % perRow;
                int countInRow = Mathf.Min(perRow, list.Count - row * perRow);
                float k = countInRow <= 1 ? 0.5f : idx / (float)(countInRow - 1);
                float ang = Mathf.Lerp(-spreadDeg, spreadDeg, k) + (row % 2) * 6f;
                float radius = Mathf.Lerp(r0, r1, Mathf.Clamp01(row * 0.5f));
                var dir = Quaternion.Euler(0, ang, 0) * toVillage;
                list[i].spot = StoryPlaces.Ground(front + dir * radius);
            }
        }

        private static ResidentJob? JobForActivity(string key)
        {
            switch (key)
            {
                case "fish": return ResidentJob.FishingMaster;
                case "crop": return ResidentJob.FarmingMaster;
                case "ore": return ResidentJob.Blacksmith;
                case "creature": return ResidentJob.CreatureExpert;
                case "craft": return ResidentJob.Cook;
                case "sold": return ResidentJob.Merchant;
                default: return null;
            }
        }

        /// <summary>引っ越していった住民から手紙が届く（FormerResidents、SharedItemIds.Letter）。</summary>
        private IEnumerator Letters(IResidentDirectory dir, string guideId)
        {
            var formers = LetterSenders(dir);
            if (formers.Count == 0) yield break;

            // 届けてくれる住民（配達係がいれば配達係、いなければ案内役）
            IResidentInfo courier = null;
            try { courier = dir.FindByJob(ResidentJob.Courier); } catch (Exception) { }
            if (courier == null || !courier.IsActive) courier = story.GuideInfo();
            if (courier != null)
            {
                var to = StoryPlaces.Ground(front + toVillage * 1.6f + side * 1.2f);
                director.Shot(front + toVillage * 1.3f + side * 4.5f + Vector3.up * 2f, Vector3.Lerp(front, to, 0.5f) + Vector3.up * 1.1f, 2f);
                yield return director.StartCoroutine(StoryActors.MoveResident(director, courier.Id, to, 1f, 4f));
                StoryActors.Face(courier.Id, front);
                heard.Add(StoryActors.Say(director, courier.Id, StoryUtterances.CourierLetters(courier.Id, formers.Count), SpeechChannel.Direct, 3.5f));
                yield return director.Wait(1.6f);
            }

            foreach (var kv in formers)
            {
                var former = kv.Key;
                var rec = GrantLetter(former);
                if (rec != null) director.ShowSpeech(rec, former.MainColor, 3.4f);
                yield return director.Wait(2.6f);
                if (Skipped) yield break;
            }
            if (courier != null) StoryActors.GoTo(courier.Id, StoryPlaces.Ground(front + toVillage * 6f));
        }

        private void GiveItem(ItemStack stack, string source)
        {
            var p = Game.Instance.LocalPlayer;
            p.GiveOrDrop(stack, source);
            UIManager.Toast(Loc.Format("{0}を受け取った", "Received: {0}", stack.DisplayName), 3f);
        }

        private List<KeyValuePair<IResidentInfo, int>> LetterSenders(IResidentDirectory dir)
        {
            var p = Game.Instance.LocalPlayer;
            var formers = new List<KeyValuePair<IResidentInfo, int>>();
            if (dir == null || p == null) return formers;
            try
            {
                foreach (var r in dir.FormerResidents)
                {
                    if (r == null) continue;
                    int f = r.GetFriendship(p.PlayerId);
                    if (f >= StoryTuning.EndingLetterFriendship || r.Knows(p.PlayerId)) formers.Add(new KeyValuePair<IResidentInfo, int>(r, f));
                }
            }
            catch (Exception e) { Debug.LogException(e); }
            formers.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key.Id, b.Key.Id));
            if (formers.Count > StoryTuning.EndingMaxLetters) formers.RemoveRange(StoryTuning.EndingMaxLetters, formers.Count - StoryTuning.EndingMaxLetters);
            return formers;
        }

        /// <summary>引っ越した住民の手紙を1通受け取る（記録・資料・住民辞典）。</summary>
        private UtteranceRecord GrantLetter(IResidentInfo former)
        {
            var game = Game.Instance;
            if (former == null || !granted.Add("letter:" + former.Id)) return null;
            var speech = game.Service<ISpeechService>();
            var tablet = game.Service<ITabletService>();
            UtteranceRecord rec = null;
            if (speech != null)
            {
                try { rec = speech.CreateRecord(StoryUtterances.LetterFromFormer(former.Id), SpeechChannel.Letter, "letter", rocketPos); }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (grantItems)
            {
                GiveItem(new ItemStack(SharedItemIds.Letter, 1) { data = rec != null ? rec.recordId : "from:" + former.Id }, "story.ending.letter");
                if (tablet != null)
                {
                    try
                    {
                        var mat = new TabletMaterial
                        {
                            id = "story.letter." + former.Id + "." + (game.Planet != null ? game.Planet.worldId : ""),
                            kind = TabletMaterialKind.Letter,
                            titleJa = "帰還の日に届いた手紙",
                            titleEn = "A letter on the day you left",
                            factJa = "帰還の日、村を引っ越していった住民から手紙が届いた。",
                            factEn = "On the day you left, a letter arrived from a resident who had moved away.",
                            day = record.day,
                            minuteOfDay = game.Time != null ? game.Time.MinuteOfDay : 0,
                            linkedRecordId = rec != null ? rec.recordId : null,
                            linkedResidentId = former.Id
                        };
                        mat.tags.Add("ending");
                        tablet.AddMaterial(mat);
                        tablet.RecordResidentObservation(former.Id, "帰還の日に手紙をくれた。", "Sent you a letter on the day you left.", rec != null ? rec.recordId : null);
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            record.letterCount++;
            record.letterResidentIds.Add(former.Id);
            if (rec != null) heard.Add(rec);
            return rec;
        }

        /// <summary>仲の良い住民のプレゼントを受け取る。</summary>
        private void GrantGift(string residentId)
        {
            if (string.IsNullOrEmpty(residentId) || !granted.Add("gift:" + residentId)) return;
            var p = Game.Instance.LocalPlayer;
            if (grantItems)
            {
                GiveItem(new ItemStack(SharedItemIds.GiftBox, 1) { data = "from:" + residentId }, "story.ending.gift");
                try { StoryActors.Directory?.AddMemory(residentId, new ResidentMemoryEntry { kind = "farewell_gift", playerId = p.PlayerId, itemId = SharedItemIds.GiftBox, day = record.day, importance = 1f }); }
                catch (Exception) { }
            }
            record.giftCount++;
        }

        /// <summary>見送りをスキップした時も、暮らしの結果として受け取る物は受け取る（まだ受け取っていない分だけ）。</summary>
        private void GrantSkippedItems()
        {
            var p = Game.Instance.LocalPlayer;
            var dir = StoryActors.Directory;
            if (dir == null || p == null) return;
            IResidentInfo cook = null;
            try { cook = dir.FindByJob(ResidentJob.Cook); } catch (Exception) { }
            if (cook != null && cook.IsActive && grantItems && granted.Add("bento")) GiveItem(new ItemStack(StoryTuning.ItemTravelBento, 1), "story.ending.cook");
            var guideId = story.GuideResidentId;
            var close = new List<IResidentInfo>();
            try
            {
                foreach (var r in dir.ActiveResidents)
                    if (r != null && r.IsActive && r.Id != guideId && r.GetFriendship(p.PlayerId) >= StoryTuning.EndingCloseFriendship) close.Add(r);
            }
            catch (Exception) { }
            close.Sort((a, b) => b.GetFriendship(p.PlayerId).CompareTo(a.GetFriendship(p.PlayerId)));
            for (int i = 0; i < close.Count && i < StoryTuning.EndingMaxGiftGivers; i++) GrantGift(close[i].Id);
            foreach (var kv in LetterSenders(dir)) GrantLetter(kv.Key);
        }

        // ------------------------------------------------------------
        // 離陸
        // ------------------------------------------------------------

        private IEnumerator Liftoff()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            if (rocketPos == Vector3.zero && front == Vector3.zero)
            {
                rocketPos = StoryPlaces.Rocket();
                village = StoryPlaces.Village();
                toVillage = StoryPlaces.Dir(rocketPos, village, Vector3.forward);
                side = StoryPlaces.Side(toVillage);
                front = StoryPlaces.Ground(rocketPos + toVillage * 3.4f);
            }
            // 乗り込む
            director.Shot(front + toVillage * 7f + side * 3f + Vector3.up * 2.5f, rocketPos + Vector3.up * 2f, 2f, ScreenFader.Alpha > 0.5f);
            if (ScreenFader.Alpha > 0.5f) ScreenFader.FadeIn(0.6f);
            yield return director.StartCoroutine(StoryActors.WalkPlayer(director, rocketPos + toVillage * 1.5f, 2.6f, 0.4f, 3f));

            // ロケット本体を代役に差し替える（一瞬の暗転の間に）
            yield return director.StartCoroutine(director.Fade(true, 0.35f));
            StoryActors.SetPlayerVisible(false);
            var dir = StoryActors.Directory;
            HideOriginalRocket();
            float yaw = Quaternion.LookRotation(toVillage, Vector3.up).eulerAngles.y;
            proxy = RocketProxy.Build(fxRoot, rocketPos, yaw);
            director.Shot(rocketPos + toVillage * 9f + side * 2f + Vector3.up * 3f, rocketPos + Vector3.up * 3f, 3f, true);
            yield return director.StartCoroutine(director.Fade(false, 0.5f));

            // ハッチが閉まる
            yield return director.StartCoroutine(proxy.CloseHatch(1.4f));
            yield return director.Wait(0.8f);

            // エンジン始動
            proxy.Ignite(0.5f);
            director.Shake(0.15f, 2f);
            yield return director.Wait(1.6f);
            proxy.Ignite(1.2f);
            director.Shake(0.35f, 1.5f);
            CutsceneFx.DustRing(fxRoot, rocketPos, 20);

            // 住民たちが手を振る
            if (dir != null)
            {
                int n = 0;
                foreach (var id in record.farewellResidentIds)
                {
                    if (n++ >= 4) break;
                    var u = StoryUtterances.LiftoffWave(id);
                    try { dir.Speak(id, u, SpeechChannel.Direct); } catch (Exception) { }
                }
            }

            // 上昇：地上から見上げる → ロケットから村を見下ろす（小さくなる村）
            var start = proxy.transform.position;
            float t = 0f;
            float height = 0f;
            director.Shot(() => front + toVillage * 10f + side * 4f + Vector3.up * 1.5f, () => proxy != null ? proxy.transform.position + Vector3.up * 3f : rocketPos, 3f);
            while (t < 4f)
            {
                t += Time.deltaTime;
                height += Mathf.Lerp(1f, 14f, t / 4f) * Time.deltaTime;
                if (proxy != null) proxy.transform.position = start + Vector3.up * height;
                yield return null;
                if (Skipped) yield break;
            }
            director.Shot(() => proxy != null ? proxy.transform.position + Vector3.up * 2f - toVillage * 3f : rocketPos, () => village, 2f, true);
            t = 0f;
            while (t < 6f)
            {
                t += Time.deltaTime;
                height += Mathf.Lerp(14f, 60f, t / 6f) * Time.deltaTime;
                if (proxy != null) proxy.transform.position = start + Vector3.up * height;
                if (t > 4.6f && ScreenFader.Alpha < 0.05f) ScreenFader.FadeOut(1.4f);
                yield return null;
                if (Skipped) yield break;
            }
            yield return director.Wait(0.4f);
            ScreenFader.SetBlack();
            if (proxy != null) UnityEngine.Object.Destroy(proxy.gameObject);
            proxy = null;
        }

        private bool rocketHidden;

        /// <summary>Rocket 担当のロケット本体の見た目を、離陸〜村の場面の間だけ隠す。</summary>
        private void HideOriginalRocket()
        {
            if (rocketHidden) return;
            rocketHidden = true;
            var p = Game.Instance.LocalPlayer;
            var exclude = new List<Transform>();
            if (p != null && p.Avatar != null) exclude.Add(p.Avatar);
            if (fxRoot != null) exclude.Add(fxRoot);
            var dir = StoryActors.Directory;
            if (dir != null)
            {
                try { foreach (var r in dir.ActiveResidents) if (r != null && r.Transform != null) exclude.Add(r.Transform); }
                catch (Exception) { }
            }
            if (rocketPos == Vector3.zero) rocketPos = StoryPlaces.Rocket();
            hider.HideNear(rocketPos, 5.5f, 28f, exclude);
        }

        // ------------------------------------------------------------
        // 主人公がいない村のいつもの生活
        // ------------------------------------------------------------

        private IEnumerator Epilogue()
        {
            var dir = StoryActors.Directory;
            ScreenFader.SetBlack();
            director.ClearSpeech();
            if (dir == null) yield break;
            var actives = new List<IResidentInfo>();
            try { foreach (var r in dir.ActiveResidents) if (r != null && r.IsActive) actives.Add(r); }
            catch (Exception) { }
            if (actives.Count == 0) yield break;
            string guideId = story.GuideResidentId;
            IResidentInfo Pick(Func<IResidentInfo, bool> pred, params string[] exclude)
            {
                foreach (var r in actives)
                {
                    if (Array.IndexOf(exclude, r.Id) >= 0) continue;
                    if (pred == null || pred(r)) return r;
                }
                return null;
            }

            // a. 商店には客がやってくる
            var shop = StoryPlaces.ShopEntrance();
            var shopDir = StoryPlaces.Dir(village, shop, Vector3.forward);
            string regularId = null;
            foreach (var id in record.farewellResidentIds) if (story.ShopPurchasesBy(id) >= StoryTuning.EndingRegularPurchases && id != guideId) { regularId = id; break; }
            var customer = regularId != null ? StoryActors.Get(regularId) : Pick(r => r.Id != guideId);
            if (customer != null)
            {
                StoryActors.Warp(customer.Id, shop - shopDir * 9f);
                StoryActors.GoTo(customer.Id, shop, 60, "story.epilogue.shop");
            }
            director.Shot(shop - shopDir * 6f + StoryPlaces.Side(shopDir) * 4f + Vector3.up * 3f, shop + Vector3.up * 1.2f, 1.5f, true);
            yield return director.StartCoroutine(director.Fade(false, 1.2f));
            yield return director.Wait(4f);
            if (Skipped) yield break;

            // b. 広場：子どもたちが遊び、住民たちは主人公のことを話している
            var kids = new List<IResidentInfo>();
            foreach (var r in actives) if (r.Job == ResidentJob.Child && kids.Count < 3) kids.Add(r);
            int ki = 0;
            foreach (var k in kids)
            {
                var spot = village + Quaternion.Euler(0, ki * 120f, 0) * Vector3.forward * 3f;
                StoryActors.Warp(k.Id, spot + Vector3.right * 2f);
                StoryActors.GoTo(k.Id, spot, 60, "story.epilogue.play");
                ki++;
            }
            var talkerA = Pick(r => r.Job != ResidentJob.Child, guideId, customer != null ? customer.Id : "");
            var talkerB = talkerA != null ? Pick(r => r.Job != ResidentJob.Child, guideId, talkerA.Id, customer != null ? customer.Id : "") : null;
            if (talkerA != null) StoryActors.Warp(talkerA.Id, village + side * 1.2f + toVillage * 2f);
            if (talkerB != null) StoryActors.Warp(talkerB.Id, village - side * 1.2f + toVillage * 2f);
            if (talkerA != null && talkerB != null)
            {
                StoryActors.Face(talkerA.Id, talkerB.Position);
                StoryActors.Face(talkerB.Id, talkerA.Position);
            }
            yield return director.StartCoroutine(director.Fade(true, 0.6f));
            director.Shot(village - toVillage * 7f + side * 5f + Vector3.up * 3.5f, village + toVillage * 2f + Vector3.up, 1.5f, true);
            yield return director.StartCoroutine(director.Fade(false, 0.8f));
            if (talkerA != null)
            {
                StoryActors.Say(director, talkerA.Id, StoryUtterances.EpilogueTalkA(talkerA.Id), SpeechChannel.Overheard, 3.5f);
                yield return director.Wait(3.4f);
            }
            if (talkerB != null)
            {
                StoryActors.Say(director, talkerB.Id, StoryUtterances.EpilogueTalkB(talkerB.Id), SpeechChannel.Overheard, 4f);
                yield return director.Wait(4f);
            }
            if (Skipped) yield break;

            // c. 主人公の家の前で、住民がふと立ち止まる
            var home = StoryPlaces.PlayerHome();
            var homeDir = StoryPlaces.Dir(village, home, Vector3.forward);
            string friendId = null;
            foreach (var id in record.farewellResidentIds) if (id != guideId) { friendId = id; break; }
            var passer = friendId != null ? StoryActors.Get(friendId) : Pick(null, guideId);
            yield return director.StartCoroutine(director.Fade(true, 0.6f));
            Vector3 passStop = StoryPlaces.Ground(home - homeDir * 2.5f);
            Vector3 passSide = StoryPlaces.Side(homeDir);
            if (passer != null) StoryActors.Warp(passer.Id, passStop - passSide * 8f);
            director.Shot(home - homeDir * 9f + passSide * 4f + Vector3.up * 2.5f, home + Vector3.up * 1.2f, 1.5f, true);
            yield return director.StartCoroutine(director.Fade(false, 0.8f));
            if (passer != null)
            {
                yield return director.StartCoroutine(StoryActors.MoveResident(director, passer.Id, passStop, 1f, 5f));
                StoryActors.Face(passer.Id, home + Vector3.up);
                yield return director.Wait(1.2f);
                StoryActors.Say(director, passer.Id, StoryUtterances.EpilogueHomePause(passer.Id), SpeechChannel.Overheard, 3f);
                yield return director.Wait(3f);
                StoryActors.GoTo(passer.Id, passStop + passSide * 10f, 60, "story.epilogue.walk_on");
                yield return director.Wait(2f);
            }
            else yield return director.Wait(3f);
            if (Skipped) yield break;

            // d. 数日後：新しい住民が引っ越してくる（代役）。案内役がいつもの言葉で迎える
            yield return director.StartCoroutine(director.Fade(true, 0.8f));
            director.ClearSpeech();
            director.Caption(Loc.T("数日後", "A few days later"), 3.5f);
            yield return director.Wait(1.6f);
            var plot = StoryPlaces.EmptyPlotOrEdge(out _);
            var plotDir = StoryPlaces.Dir(village, plot, Vector3.forward);
            var plotSide = StoryPlaces.Side(plotDir);
            var newcomer = NewcomerStandIn.Build(fxRoot, StoryPlaces.Ground(plot + plotDir * 14f + plotSide * 2f), new Color(0.75f, 0.85f, 1f));
            NewcomerStandIn.BoxPile(fxRoot, StoryPlaces.Ground(plot + plotSide * 2.2f));
            var walker = newcomer.GetComponent<HopWalker>();
            if (walker != null) walker.target = StoryPlaces.Ground(plot + plotDir * 1.5f);
            var guide = story.GuideInfo();
            if (guide != null)
            {
                StoryActors.Warp(guide.Id, plot - plotDir * 7f);
                StoryActors.GoTo(guide.Id, StoryPlaces.Ground(plot - plotDir * 0.8f), 60, "story.epilogue.welcome");
            }
            director.Shot(plot - plotSide * 9f + plotDir * 2f + Vector3.up * 3f, plot + plotDir * 1f + Vector3.up * 1f, 1.2f, true);
            yield return director.StartCoroutine(director.Fade(false, 1f));
            yield return director.Wait(4.5f);
            if (guide != null)
            {
                StoryActors.Face(guide.Id, newcomer.transform.position);
                StoryActors.Say(director, guide.Id, StoryUtterances.GuideWelcomesNewcomer(guide.Id, story.GuideCatchphraseConcept), SpeechChannel.Overheard, 5f);
            }
            yield return director.Wait(5f);
            yield return director.StartCoroutine(director.Fade(true, 1.2f));
            director.ClearSpeech();
            if (newcomer != null) UnityEngine.Object.Destroy(newcomer);
        }

        // ------------------------------------------------------------
        // 宇宙船の窓・辞書・着陸（思い出の再生でも使う）
        // ------------------------------------------------------------

        internal static IEnumerator WindowAndDictionary(CutsceneDirector director, EndingRecord rec, List<UtteranceRecord> heard, string catchphrase,
            CharacterAppearance appearance, Transform parent, Action<CabinSet> onCabin, Action<DictionaryPage> onPage)
        {
            ScreenFader.SetBlack();
            director.ClearSpeech();
            // 宇宙の場面：空の色と霧を一時的に切る（着陸の前に戻す）
            var cam = director.Cam;
            var prevFlags = cam != null ? cam.clearFlags : CameraClearFlags.Skybox;
            var prevBg = cam != null ? cam.backgroundColor : Color.black;
            bool prevFog = RenderSettings.fog;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
            }
            RenderSettings.fog = false;
            var cabin = CabinSet.Build(parent, appearance);
            onCabin?.Invoke(cabin);
            director.Shot(() => cabin.cameraPoint.position, () => cabin.lookPoint.position, 1.5f, true);
            yield return null;
            yield return director.StartCoroutine(director.Fade(false, 1.6f));
            var approach = director.StartCoroutine(cabin.Approach(14f));
            yield return director.Wait(3.5f);

            // プレイヤーが書いた辞書が映し出される
            var page = DictionaryPage.Collect(heard, catchphrase);
            onPage?.Invoke(page);
            director.ExtraDraw = page.Draw;
            float t = 0f;
            while (t < 1.2f)
            {
                t += Time.deltaTime;
                page.Alpha = Mathf.Clamp01(t / 1.2f);
                yield return null;
            }
            yield return director.Wait(9f);
            t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime;
                page.Alpha = 1f - Mathf.Clamp01(t / 1f);
                yield return null;
            }
            director.ExtraDraw = null;
            director.ResetSkip();

            // 着陸：故郷の地面へ降りていく
            yield return director.Wait(1.5f);
            director.Flash(Color.white, 1f);
            ScreenFader.SetBlack();
            if (cam != null)
            {
                cam.clearFlags = prevFlags;
                cam.backgroundColor = prevBg;
            }
            RenderSettings.fog = prevFog;
            var land = BuildHomeLanding(parent, appearance);
            var landProxy = land.GetComponentInChildren<RocketProxy>();
            var groundPos = land.transform.position;
            if (landProxy != null)
            {
                landProxy.Ignite(0.6f);
                landProxy.transform.position = groundPos + Vector3.up * 40f;
            }
            director.Shot(groundPos + new Vector3(16f, 4f, -18f), groundPos + Vector3.up * 6f, 2f, true);
            yield return director.StartCoroutine(director.Fade(false, 1f));
            t = 0f;
            while (t < 5f && landProxy != null)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0, 1, t / 5f);
                landProxy.transform.position = groundPos + Vector3.up * Mathf.Lerp(40f, 0f, k);
                yield return null;
            }
            if (landProxy != null)
            {
                landProxy.Ignite(0f);
                CutsceneFx.DustRing(parent, groundPos, 22);
            }
            yield return director.Wait(2.5f);
            yield return director.StartCoroutine(director.Fade(true, 1.4f));
            if (land != null) UnityEngine.Object.Destroy(land);
        }

        /// <summary>故郷の着陸地（簡単なセット）。</summary>
        private static GameObject BuildHomeLanding(Transform parent, CharacterAppearance appearance)
        {
            var root = Core.Visual.PrimitiveBuilder.Empty("HomeLanding", parent, Vector3.zero);
            root.transform.position = new Vector3(12000f, 3000f, 13000f);
            var t = root.transform;
            Core.Visual.PrimitiveBuilder.Part(t, PrimitiveType.Cube, new Vector3(0, -0.5f, 0), new Vector3(200f, 1f, 200f), new Color(0.55f, 0.78f, 0.45f), "HomeGround");
            Core.Visual.PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(10f, 0.02f, 10f), new Color(0.75f, 0.75f, 0.8f), "Pad");
            for (int i = 0; i < 14; i++)
            {
                var pos = new Vector3(UnityEngine.Random.Range(-60f, 60f), 0, UnityEngine.Random.Range(-10f, 70f));
                if (pos.magnitude < 12f) continue;
                Core.Visual.PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, pos + Vector3.up * 1.2f, new Vector3(0.5f, 1.2f, 0.5f), new Color(0.5f, 0.35f, 0.22f), "Trunk");
                Core.Visual.PrimitiveBuilder.Part(t, PrimitiveType.Sphere, pos + Vector3.up * 3.4f, Vector3.one * UnityEngine.Random.Range(2.5f, 3.6f), new Color(0.3f, 0.62f, 0.32f), "Leaves");
            }
            var lightGo = Core.Visual.PrimitiveBuilder.Empty("HomeSun", t, new Vector3(0, 30f, -10f));
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 120f;
            l.intensity = 1.2f;
            l.color = new Color(1f, 0.92f, 0.8f);
            RocketProxy.Build(t, root.transform.position, 0f);
            return root;
        }

        // ------------------------------------------------------------
        // クリア後：A「ロケット前へ戻る」
        // ------------------------------------------------------------

        /// <summary>出発直前の状態でロケットの前に主人公を戻し、プレイを続ける。</summary>
        public void ReturnToRocketFront()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            Cleanup();
            UIManager.CloseAll();
            var rp = StoryPlaces.Rocket();
            var v = StoryPlaces.Village();
            var dirv = StoryPlaces.Dir(rp, v, Vector3.forward);
            if (p != null)
            {
                p.CurrentAreaId = AreaIds.Overworld;
                p.Position = StoryPlaces.Ground(rp + dirv * 3.4f) + Vector3.up * 0.1f;
                StoryActors.FacePlayer(rp + dirv * 8f);
                StoryActors.SetPlayerVisible(true);
                p.UnlockInput("story");
            }
            if (director != null)
            {
                director.ExtraDraw = null;
                director.Letterbox = false;
                director.ReleaseCamera();
                UnityEngine.Object.Destroy(director.gameObject);
            }
            director = null;
            game.SetState(GameState.Playing);
            UIManager.HudVisible = true;
            ScreenFader.FadeIn(1.2f);
            Running = false;
        }

        private void Cleanup()
        {
            hider.Restore();
            rocketHidden = false;
            if (proxy != null) UnityEngine.Object.Destroy(proxy.gameObject);
            proxy = null;
            if (cabin != null) UnityEngine.Object.Destroy(cabin.gameObject);
            cabin = null;
            if (fxRoot != null) UnityEngine.Object.Destroy(fxRoot.gameObject);
            fxRoot = null;
            var game = Game.Instance;
            if (game != null && game.Time != null) game.Time.RemoveGlobalPause("story.ending");
            StoryActors.SetPlayerVisible(true);
        }
    }
}
