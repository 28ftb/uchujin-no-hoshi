using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// プロローグ「空から来た変な生き物」（メインストーリー：プロローグ、ロケット §2）。
    ///   空から落ちてくる光 → 衝撃と煙 → 主人公が脱出 → タブレットの診断（主人公の装置なので主人公の言語）
    ///   → まんまるな宇宙人（案内役）が近づく → 主人公が近づくと驚いて逃げる
    ///   → 仲間（四本腕の修理屋・料理好き）を連れて戻る：修理屋はロケットを調べ、料理好きは食べ物を差し出す
    ///   → 皆が宇宙語で話す（翻訳しない）→ 案内役が手を引いて村へ（主人公はついて歩く／止まっていると自動で歩く）→ 自由行動。
    /// 2つ目以降の星（arrival）は短い版：到着 → 診断 → 案内役の挨拶 → 村へ。
    /// スキップ可能。住民モジュールが無い・住民がいない場合は住民の場面を飛ばして落ちずに終える。
    /// </summary>
    public sealed class PrologueSequence
    {
        private readonly StorySystem story;
        private readonly bool arrival;
        private CutsceneDirector director;
        private Transform fxRoot;
        /// <summary>光が落ちてくるまでの間、不時着地に既にある宇宙船（Rocket の表示）を隠す。</summary>
        private readonly RendererHider shipHider = new RendererHider();
        public bool Running { get; private set; }

        // 診断画面
        private float diagProgress;
        private int diagLines;
        private bool diagDone;

        private static readonly LocText[] DiagParts =
        {
            new LocText("補助電源", "Auxiliary power"),
            new LocText("船体外装", "Hull plating"),
            new LocText("冷却循環装置", "Coolant loop"),
            new LocText("燃料精製装置", "Fuel refinery"),
            new LocText("主エンジン", "Main engine"),
            new LocText("次元観測装置", "Dimensional observer"),
            new LocText("位相安定装置", "Phase stabilizer"),
        };

        private static readonly LocText[] DiagStates =
        {
            new LocText("停止", "Offline"),
            new LocText("損傷", "Damaged"),
            new LocText("漏れ", "Leaking"),
            new LocText("停止", "Offline"),
            new LocText("停止", "Offline"),
            new LocText("応答なし", "No response"),
            new LocText("不安定", "Unstable"),
        };

        public PrologueSequence(StorySystem story, bool arrival)
        {
            this.story = story;
            this.arrival = arrival;
        }

        public void Start()
        {
            director = CutsceneDirector.Create("PrologueDirector");
            director.SkipEnabled = true;
            var fx = new GameObject("PrologueFx");
            if (Game.Instance.SceneRoot != null) fx.transform.SetParent(Game.Instance.SceneRoot, false);
            fxRoot = fx.transform;
            Running = true;
            director.StartCoroutine(Run());
        }

        /// <summary>星を離れる時など、完了させずに止める。</summary>
        public void Abort()
        {
            Running = false;
            shipHider.Restore();
            if (director != null) Object.Destroy(director.gameObject);
            director = null;
        }

        private bool Skipped => director == null || director.SkipRequested;

        private IEnumerator Run()
        {
            yield return director.StartCoroutine(Main());
            yield return director.StartCoroutine(Finish());
        }

        private IEnumerator Main()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            if (p == null) yield break;

            Vector3 crash = StoryPlaces.Rocket();
            Vector3 village = StoryPlaces.Village();
            Vector3 toVillage = StoryPlaces.Dir(crash, village, Vector3.forward);
            Vector3 side = StoryPlaces.Side(toVillage);
            Vector3 playerSpot = StoryPlaces.Ground(crash + toVillage * 6f + side * 2f);

            // ---- 演出開始 ----
            game.SetState(GameState.Cutscene);
            UIManager.HudVisible = false;
            p.LockInput("story");
            director.Letterbox = true;
            ScreenFader.SetBlack();
            p.Position = playerSpot + Vector3.up * 0.1f;
            StoryActors.SetPlayerVisible(false);
            HideShipUntilImpact(crash);

            // ---- 1. 空から落ちてくる光 ----
            Vector3 skyFrom = crash - toVillage * 140f + side * 50f + Vector3.up * (arrival ? 120f : 170f);
            Vector3 meteorPos = skyFrom;
            Vector3 camPos = crash + toVillage * 34f + side * 12f + Vector3.up * 7f;
            director.Shot(() => camPos, () => Vector3.Lerp(meteorPos, crash, 0.25f), 2.5f, true);
            yield return null;
            ScreenFader.FadeIn(1.5f);
            yield return director.Wait(1.2f);
            if (Skipped) yield break;
            yield return director.StartCoroutine(CutsceneFx.Meteor(fxRoot, skyFrom, crash + Vector3.up * 1.5f, arrival ? 3.2f : 3.8f, director,
                pos => meteorPos = pos, arrival ? 0.8f : 1f));
            if (Skipped) yield break;

            // ---- 2. 衝撃 ----
            shipHider.Restore();
            director.Flash(Color.white, arrival ? 0.7f : 1f);
            director.Shake(arrival ? 0.5f : 0.9f, 1.3f);
            CutsceneFx.Impact(fxRoot, crash, arrival ? 60f : 120f);
            director.Shot(crash + toVillage * 20f + side * 8f + Vector3.up * 5f, crash + Vector3.up * 2f, 2f);
            yield return director.Wait(2.2f);
            if (Skipped) yield break;

            // ---- 3. 主人公が脱出 ----
            p.Position = StoryPlaces.Ground(crash + toVillage * 2.6f) + Vector3.up * 0.1f;
            StoryActors.FacePlayer(playerSpot + toVillage * 5f);
            StoryActors.SetPlayerVisible(true);
            director.Shot(() => p.Position + toVillage * 6f + side * 3.5f + Vector3.up * 2.4f, () => p.Position + Vector3.up * 1f, 3f);
            yield return director.Wait(0.4f);
            StoryActors.DoPlayerGesture(PlayerGesture.Surprised);
            yield return director.StartCoroutine(StoryActors.WalkPlayer(director, playerSpot, 3.2f, 0.6f, 4f));
            if (Skipped) yield break;
            StoryActors.FacePlayer(crash);
            StoryActors.DoPlayerGesture(PlayerGesture.Think);
            yield return director.Wait(1.2f);
            if (Skipped) yield break;

            // ---- 4. タブレットの診断（主人公の装置：主人公の言語で表示してよい ロケット §2.1）----
            director.Shot(p.Position - toVillage * 2.5f + side * 1.8f + Vector3.up * 2.2f, p.Position + Vector3.up * 1.1f, 2.5f);
            diagProgress = 0f;
            diagLines = 0;
            diagDone = false;
            director.ExtraDraw = DrawDiagnosis;
            float t = 0f;
            while (t < 2.2f && !Skipped)
            {
                t += Time.deltaTime;
                diagProgress = Mathf.Clamp01(t / 2.2f);
                yield return null;
            }
            for (int i = 0; i < DiagParts.Length && !Skipped; i++)
            {
                diagLines = i + 1;
                yield return director.Wait(0.35f);
            }
            diagDone = true;
            yield return director.Wait(3.6f);
            director.ExtraDraw = null;
            if (Skipped) yield break;

            // ---- 住民の場面（住民モジュールが無ければ飛ばす）----
            var dir = StoryActors.Directory;
            var guide = story.GuideInfo();
            if (dir == null || guide == null)
            {
                Debug.Log("[Story] 住民が見つからないため、プロローグの住民の場面を飛ばします");
                yield break;
            }
            string guideId = guide.Id;
            string phrase = story.GuideCatchphraseConcept;

            // ---- 5. 背後から声：まんまるな宇宙人がこちらを見ている ----
            Vector3 guideSpot = StoryPlaces.Ground(playerSpot + toVillage * 7f + side * 1f);
            StoryActors.Warp(guideId, playerSpot + toVillage * 20f + side * 3f);
            director.Shot(() => p.Position - toVillage * 3f + side * 2.5f + Vector3.up * 2f, () => Vector3.Lerp(p.Position, guideSpot, 0.6f) + Vector3.up, 2.5f);
            yield return director.StartCoroutine(StoryActors.MoveResident(director, guideId, guideSpot, 1.5f, 6f));
            if (Skipped) yield break;
            StoryActors.Face(guideId, p.Position);
            StoryActors.Say(director, guideId, StoryUtterances.GuideFirstSighting(guideId), SpeechChannel.Direct);
            yield return director.Wait(0.6f);
            StoryActors.FacePlayer(guideSpot);
            StoryActors.DoPlayerGesture(PlayerGesture.Surprised);
            yield return director.Wait(2.6f);
            if (Skipped) yield break;

            if (!arrival)
            {
                // ---- 6. 近づいてみる（操作できる）----
                yield return director.StartCoroutine(InteractiveApproach(guideId));
                if (Skipped) yield break;

                // 驚いて逃げる
                StoryActors.Say(director, guideId, StoryUtterances.GuideStartled(guideId), SpeechChannel.Direct, 2.5f);
                StoryActors.DoPlayerGesture(PlayerGesture.Surprised);
                yield return director.Wait(0.7f);
                Vector3 runAway = StoryPlaces.Ground(guideSpot + toVillage * 22f - side * 4f);
                director.Shot(() => p.Position - toVillage * 4f + Vector3.up * 3f, () => StoryActors.Get(guideId) != null ? StoryActors.Get(guideId).Position + Vector3.up : runAway, 2.5f);
                yield return director.StartCoroutine(StoryActors.MoveResident(director, guideId, runAway, 2f, 4.5f));
                if (Skipped) yield break;
                StoryActors.FacePlayer(runAway);
                StoryActors.DoPlayerGesture(PlayerGesture.Think);
                director.Shot(p.Position + toVillage * 4f + side * 2f + Vector3.up * 1.8f, p.Position + Vector3.up, 2f);
                yield return director.Wait(2.4f);
                if (Skipped) yield break;

                // ---- 7. 仲間を連れて戻ってくる ----
                var mech = FindHelper(dir, ResidentJob.Mechanic, guideId, null);
                var cook = FindHelper(dir, ResidentJob.Cook, guideId, mech != null ? mech.Id : null);
                var moves = new List<KeyValuePair<string, Vector3>>();
                Vector3 guideBack = StoryPlaces.Ground(p.Position + toVillage * 3f + side * 1.4f);
                moves.Add(new KeyValuePair<string, Vector3>(guideId, guideBack));
                Vector3 mechSpot = StoryPlaces.Ground(crash + side * 3.2f + toVillage * 1.5f);
                Vector3 cookSpot = StoryPlaces.Ground(p.Position + toVillage * 2.4f - side * 1.6f);
                if (mech != null)
                {
                    StoryActors.Warp(mech.Id, runAway + side * 2f);
                    moves.Add(new KeyValuePair<string, Vector3>(mech.Id, mechSpot));
                }
                if (cook != null)
                {
                    StoryActors.Warp(cook.Id, runAway - side * 2f);
                    moves.Add(new KeyValuePair<string, Vector3>(cook.Id, cookSpot));
                }
                director.Shot(p.Position - toVillage * 3.5f + side * 3f + Vector3.up * 2.6f, guideBack + toVillage * 4f, 2f);
                yield return director.Wait(0.5f);
                StoryActors.Say(director, guideId, StoryUtterances.GuideCallsFriends(guideId), SpeechChannel.Overheard, 3f);
                yield return director.StartCoroutine(StoryActors.MoveResidents(director, moves, 1.8f, 9f));
                if (Skipped) yield break;
                StoryActors.Face(guideId, p.Position);
                StoryActors.FacePlayer(guideBack);

                // ---- 8. 修理屋がロケットを調べる ----
                if (mech != null)
                {
                    StoryActors.Face(mech.Id, crash);
                    director.Shot(mechSpot + side * 4f - toVillage * 2f + Vector3.up * 2.4f, Vector3.Lerp(mechSpot, crash, 0.5f) + Vector3.up * 1.2f, 2.2f);
                    yield return director.Wait(0.6f);
                    StoryActors.Say(director, mech.Id, StoryUtterances.MechanicInspects(mech.Id), SpeechChannel.Overheard, 4f);
                    yield return director.Wait(3.8f);
                    if (Skipped) yield break;
                }

                // ---- 料理好きが食べ物を差し出す（料理アイテムを1つ渡す）----
                if (cook != null)
                {
                    StoryActors.Face(cook.Id, p.Position);
                    StoryActors.FacePlayer(cookSpot);
                    director.Shot(p.Position + toVillage * 1.2f - side * 3.5f + Vector3.up * 1.9f, Vector3.Lerp(p.Position, cookSpot, 0.5f) + Vector3.up * 1.1f, 2.5f);
                    StoryActors.Say(director, cook.Id, StoryUtterances.CookWonders(cook.Id), SpeechChannel.Overheard, 2.6f);
                    yield return director.Wait(2.4f);
                    if (Skipped) yield break;
                    string food = story.PrologueFoodItemId();
                    StoryActors.Say(director, cook.Id, StoryUtterances.CookOffers(cook.Id, food), SpeechChannel.Direct, 4f);
                    yield return director.Wait(1.2f);
                    GiveFood(food);
                    StoryActors.DoPlayerGesture(PlayerGesture.Bow);
                    try
                    {
                        dir.AddMemory(cook.Id, new ResidentMemoryEntry { kind = "gift", playerId = p.PlayerId, itemId = food, day = game.Time.Day, importance = 0.6f, detail = "story.prologue" });
                    }
                    catch (System.Exception e) { Debug.LogException(e); }
                    yield return director.Wait(2.4f);
                    if (Skipped) yield break;
                }
            }

            // ---- 9. 案内役のあいさつ（口癖：意味はゲームが説明しない）----
            StoryActors.Face(guideId, p.Position);
            var g = StoryActors.Get(guideId);
            Vector3 gp = g != null ? g.Position : guideSpot;
            StoryActors.FacePlayer(gp);
            director.Shot(Vector3.Lerp(p.Position, gp, 0.5f) + side * 3.2f + Vector3.up * 1.6f, Vector3.Lerp(p.Position, gp, 0.5f) + Vector3.up * 1.1f, 2.5f);
            yield return director.Wait(0.4f);
            StoryActors.Say(director, guideId, StoryUtterances.GuideGreets(guideId, phrase), SpeechChannel.Direct, 4f);
            try
            {
                dir.AddMemory(guideId, new ResidentMemoryEntry { kind = "met", playerId = p.PlayerId, day = game.Time.Day, importance = 0.9f, detail = "story.prologue.guide" });
            }
            catch (System.Exception e) { Debug.LogException(e); }
            yield return director.Wait(3.6f);
            if (Skipped) yield break;
            StoryActors.DoPlayerGesture(PlayerGesture.Wave);
            yield return director.Wait(1f);

            // ---- 10. 案内役が手を引いて村へ ----
            StoryActors.Say(director, guideId, StoryUtterances.GuideInvitesToVillage(guideId), SpeechChannel.Direct, 3.5f);
            yield return director.Wait(1.2f);
            StoryActors.DoPlayerGesture(PlayerGesture.Nod);
            yield return director.Wait(1.4f);
            if (Skipped) yield break;
            yield return director.StartCoroutine(FollowGuideToVillage(guideId, village));
            if (Skipped) yield break;

            // ---- 11. 村に着いた ----
            game.SetState(GameState.Cutscene);
            p.LockInput("story");
            director.Letterbox = true;
            StoryActors.Face(guideId, p.Position);
            StoryActors.Say(director, guideId, StoryUtterances.GuideArrivedVillage(guideId), SpeechChannel.Direct, 3.5f);
            director.Shot(() => village + toVillage * 14f + side * 10f + Vector3.up * 9f, () => village + Vector3.up * 1.5f, 1.2f);
            yield return director.Wait(4.5f);
        }

        /// <summary>主人公が近づくまで待つ（操作可能）。時間切れなら自動で歩く。</summary>
        private IEnumerator InteractiveApproach(string guideId)
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            game.SetState(GameState.Playing);
            director.Letterbox = false;
            director.ReleaseCamera();
            p.UnlockInput("story");
            var g = StoryActors.Get(guideId);
            director.Marker(g != null ? g.Transform : null);
            director.Hint(Loc.T("［移動キー］ 近づいてみよう", "[Move] Walk closer"));
            float t = 0f;
            while (t < StoryTuning.PrologueApproachTimeout && !Skipped)
            {
                if (StoryActors.Distance(guideId, p.Position) <= StoryTuning.PrologueApproachDistance) break;
                t += Time.deltaTime;
                yield return null;
            }
            director.Hint(null);
            director.Marker(null);
            p.LockInput("story");
            game.SetState(GameState.Cutscene);
            director.Letterbox = true;
            if (Skipped) yield break;
            if (StoryActors.Distance(guideId, p.Position) > StoryTuning.PrologueApproachDistance)
            {
                var gi = StoryActors.Get(guideId);
                if (gi != null)
                {
                    director.Shot(() => p.Position + Vector3.up * 3f - p.Forward * 5f, () => p.Position + p.Forward * 3f + Vector3.up, 2.5f);
                    yield return director.StartCoroutine(StoryActors.WalkPlayer(director, gi.Position, 3f, StoryTuning.PrologueApproachDistance - 0.5f, 8f));
                }
            }
            var gg = StoryActors.Get(guideId);
            director.Shot(p.Position - p.Forward * 4f + Vector3.up * 2.2f, (gg != null ? gg.Position : p.Position + p.Forward * 3f) + Vector3.up, 3f);
        }

        /// <summary>案内役についていく（操作可能。止まっていると自動で歩き出す）。</summary>
        private IEnumerator FollowGuideToVillage(string guideId, Vector3 village)
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            StoryActors.GoTo(guideId, village, 120, "story.prologue.lead");
            game.SetState(GameState.Playing);
            director.Letterbox = false;
            director.ReleaseCamera();
            p.UnlockInput("story");
            var g = StoryActors.Get(guideId);
            director.Marker(g != null ? g.Transform : null);
            director.Hint(Loc.T("［移動キー］ ついていこう（止まっていると自動で歩きます）", "[Move] Follow (you'll walk automatically if idle)"));
            var cc = p.Avatar != null ? p.Avatar.GetComponent<CharacterController>() : null;
            float t = 0f;
            float idle = 0f;
            float lastGuideDist = float.MaxValue;
            float guideStuck = 0f;
            while (t < StoryTuning.PrologueFollowTimeout && !Skipped)
            {
                float dt = Time.deltaTime;
                t += dt;
                var pd = p.Position - village;
                pd.y = 0;
                if (pd.magnitude <= StoryTuning.PrologueArriveDistance) break;

                var gi = StoryActors.Get(guideId);
                // 案内役が動けなくなっていたら少しずつ村へ寄せる
                float gd = StoryActors.Distance(guideId, village);
                if (gd < lastGuideDist - 0.2f) { lastGuideDist = gd; guideStuck = 0f; }
                else guideStuck += dt;
                if (gi != null && guideStuck > 5f && gd > 3f)
                {
                    StoryActors.Warp(guideId, gi.Position + StoryPlaces.Dir(gi.Position, village, Vector3.forward) * Mathf.Min(6f, gd));
                    StoryActors.GoTo(guideId, village, 120, "story.prologue.lead");
                    guideStuck = 0f;
                }
                // 案内役が離れすぎたら近くへ
                if (gi != null && StoryActors.Distance(guideId, p.Position) > 30f)
                    StoryActors.Warp(guideId, p.Position + StoryPlaces.Dir(p.Position, village, Vector3.forward) * 6f);

                // 止まっていたら手を引かれて自動で歩く
                if (UIManager.AnyModalOpen || GameInput.Move.sqrMagnitude > 0.01f) idle = 0f;
                else idle += dt;
                if (idle > 4f && gi != null && cc != null && cc.enabled && !UIManager.AnyModalOpen)
                {
                    var target = StoryActors.Distance(guideId, p.Position) > 2.5f ? gi.Position : village;
                    var d = target - p.Position;
                    d.y = 0;
                    if (d.sqrMagnitude > 1f)
                    {
                        var dirv = d.normalized;
                        p.Avatar.rotation = Quaternion.RotateTowards(p.Avatar.rotation, Quaternion.LookRotation(dirv, Vector3.up), 540f * dt);
                        cc.Move(dirv * 3.6f * dt);
                    }
                }
                yield return null;
            }
            director.Hint(null);
            director.Marker(null);
            p.LockInput("story");
            if (Skipped) yield break;
            var pd2 = p.Position - village;
            pd2.y = 0;
            if (pd2.magnitude > StoryTuning.PrologueArriveDistance)
            {
                // 時間切れ：暗転して村へ
                yield return director.StartCoroutine(director.Fade(true, 0.8f));
                p.Position = StoryPlaces.Ground(village + Vector3.back * 3f) + Vector3.up * 0.2f;
                StoryActors.Warp(guideId, village + Vector3.back * 1.5f + Vector3.right * 1.5f);
                yield return null;
                if (CameraRig.Instance != null) CameraRig.Instance.SnapToTarget();
                yield return director.StartCoroutine(director.Fade(false, 0.8f));
            }
        }

        private void HideShipUntilImpact(Vector3 crash)
        {
            var exclude = new List<Transform>();
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            if (p != null && p.Avatar != null) exclude.Add(p.Avatar);
            if (fxRoot != null) exclude.Add(fxRoot);
            if (director != null) exclude.Add(director.transform);
            var dir = StoryActors.Directory;
            if (dir != null)
            {
                try { foreach (var r in dir.ActiveResidents) if (r != null && r.Transform != null) exclude.Add(r.Transform); }
                catch (System.Exception) { }
            }
            shipHider.HideNear(crash, 5.5f, 28f, exclude);
        }

        private static IResidentInfo FindHelper(IResidentDirectory dir, ResidentJob job, string excludeA, string excludeB)
        {
            IResidentInfo r = null;
            try { r = dir.FindByJob(job); }
            catch (System.Exception) { }
            if (r == null || !r.IsActive || r.Id == excludeA || r.Id == excludeB) return null;
            return r;
        }

        private void GiveFood(string food)
        {
            if (story.PrologueFoodGiven) return;
            story.MarkPrologueFoodGiven();
            var p = Game.Instance.LocalPlayer;
            p.GiveOrDrop(new ItemStack(food, 1), "story.prologue");
            UIManager.Toast(Loc.Format("{0}をもらった", "Received: {0}", ItemRegistry.NameOf(food)));
        }

        /// <summary>演出の後片付け（スキップされた場合も通る）。</summary>
        private IEnumerator Finish()
        {
            shipHider.Restore();
            var game = Game.Instance;
            var p = game != null ? game.LocalPlayer : null;
            bool skipped = director != null && director.SkipRequested;
            if (director != null)
            {
                director.ExtraDraw = null;
                director.Hint(null);
                director.Marker(null);
                director.ClearSpeech();
                director.SkipEnabled = false;
            }
            if (skipped && p != null)
            {
                // スキップ：暗転して村の入り口へ（住民がいなければ不時着地のそば）
                ScreenFader.SetBlack();
                yield return null;
                StoryActors.SetPlayerVisible(true);
                var guide = story.GuideInfo();
                Vector3 village = StoryPlaces.Village();
                if (guide != null && village != Vector3.zero)
                {
                    p.Position = StoryPlaces.Ground(village + Vector3.back * 3f) + Vector3.up * 0.2f;
                    StoryActors.Warp(guide.Id, village + Vector3.back * 1.5f + Vector3.right * 1.5f);
                    StoryActors.Face(guide.Id, p.Position);
                }
                // 不時着の跡（煙）は残す
                var crash = StoryPlaces.Rocket();
                if (fxRoot != null && fxRoot.GetComponentInChildren<SmokeEmitter>() == null) CutsceneFx.Impact(fxRoot, crash, 120f);
            }
            StoryActors.SetPlayerVisible(true);
            if (director != null)
            {
                director.Letterbox = false;
                director.ReleaseCamera();
            }
            if (game != null) game.SetState(GameState.Playing);
            UIManager.HudVisible = true;
            if (p != null) p.UnlockInput("story");
            story.CompletePrologue(skipped, true);
            if (skipped) ScreenFader.FadeIn(1f);
            Running = false;
            UIManager.Toast(Loc.T("Tab：タブレット（宇宙船の診断・宇宙語の辞書）", "Tab: Tablet (ship diagnosis, alien dictionary)"), 6f);
            yield return new WaitForSeconds(0.5f);
            if (director != null) Object.Destroy(director.gameObject);
            director = null;
        }

        // ------------------------------------------------------------
        // 診断画面（主人公のタブレット）
        // ------------------------------------------------------------

        private void DrawDiagnosis()
        {
            float vw = UIStyles.VirtualWidth;
            float w = 620f, h = 560f;
            var r = new Rect(vw - w - 80f, 140f, w, h);
            UIStyles.FillRect(new Rect(r.x - 10, r.y - 10, r.width + 20, r.height + 20), new Color(0.2f, 0.22f, 0.3f, 0.95f));
            UIStyles.FillRect(r, new Color(0.04f, 0.08f, 0.12f, 0.96f));
            var title = new GUIStyle(UIStyles.LabelBold);
            title.normal.textColor = new Color(0.55f, 1f, 0.75f);
            GUI.Label(new Rect(r.x + 20, r.y + 14, w - 40, 32), arrival ? Loc.T("宇宙船の診断（新しい星に到着）", "Ship diagnosis (arrived at a new planet)") : Loc.T("宇宙船の診断", "Ship diagnosis"), title);
            if (diagProgress < 1f)
            {
                GUI.Label(new Rect(r.x + 20, r.y + 60, w - 40, 30), Loc.T("診断中…", "Scanning..."), UIStyles.Label);
                UIStyles.ProgressBar(new Rect(r.x + 20, r.y + 100, w - 40, 22), diagProgress, new Color(0.4f, 1f, 0.7f));
                return;
            }
            var rocket = Game.Instance != null ? Game.Instance.Service<IRocketService>() : null;
            int broken = DiagParts.Length;
            if (rocket != null)
            {
                try { broken = Mathf.Max(1, rocket.TotalDeviceCount - rocket.RepairedDeviceCount); } catch (System.Exception) { }
            }
            float y = r.y + 60;
            for (int i = 0; i < diagLines && i < DiagParts.Length; i++)
            {
                GUI.Label(new Rect(r.x + 30, y, 320, 30), DiagParts[i].ToString(), UIStyles.Label);
                var st = new GUIStyle(UIStyles.LabelBold);
                st.normal.textColor = UIStyles.Bad;
                GUI.Label(new Rect(r.x + 380, y, 200, 30), DiagStates[i].ToString(), st);
                y += 34;
            }
            if (diagLines >= DiagParts.Length)
            {
                var ok = new GUIStyle(UIStyles.LabelBold);
                ok.normal.textColor = UIStyles.Good;
                GUI.Label(new Rect(r.x + 30, y, 320, 30), Loc.T("航路データ（故郷）", "Route data (home)"), UIStyles.Label);
                GUI.Label(new Rect(r.x + 380, y, 200, 30), Loc.T("無事", "Intact"), ok);
                y += 44;
            }
            if (diagDone)
            {
                var msg = new GUIStyle(UIStyles.LabelWrap);
                msg.normal.textColor = new Color(0.9f, 1f, 0.92f);
                GUI.Label(new Rect(r.x + 20, y, w - 40, 90),
                    Loc.Format("故障箇所が {0} か所見つかりました。航路データは無事です。\n必要な部品がそろえば、修理できそうです。",
                        "{0} faults found. The route data is intact.\nWith the right parts, the ship can be repaired.", broken), msg);
            }
        }
    }
}
