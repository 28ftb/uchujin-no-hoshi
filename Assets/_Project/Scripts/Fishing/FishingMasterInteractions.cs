using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Fishing
{
    /// <summary>
    /// 釣り名人（ResidentJob.FishingMaster）との交流（釣り仕様 §4.3, §5）と釣り名人の小屋の機能。
    /// ・技は名人の実演（身振り・竿の動き）を見て真似すると覚えられる。宇宙語の理解は不要（§5.1）。
    /// ・名人が引っ越しても覚えた技は失われない（技能は PlayerProfile）。
    /// ・名人に会えない場合も、小屋の「見本」で（少し高い釣りレベルで）練習できる（§5.3「永久に習得不能にしない」）。
    /// ・名人は今日の釣り場の様子を宇宙語＋身振りで話す（理解できれば手掛かりになる §10）。
    /// </summary>
    public sealed class FishingMasterInteractions : IResidentInteractionProvider
    {
        private readonly FishingSystem sys;

        public FishingMasterInteractions(FishingSystem sys) { this.sys = sys; }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            if (resident == null || player == null || resident.Job != ResidentJob.FishingMaster) yield break;
            string rid = resident.Id;
            yield return new ResidentInteractionOption("釣りの技を見せてもらう", "Ask to see a fishing technique", () => StartLesson(player, rid, false), 20);
            yield return new ResidentInteractionOption("竿の強化を頼む", "Ask to upgrade your rod", () => UIManager.Open(new RodUpgradeWindow(sys, player, rid)), 21);
            yield return new ResidentInteractionOption("釣り場の話を聞く", "Listen to fishing talk", () => TalkAboutFishing(player, rid), 22);
            yield return new ResidentInteractionOption("餌・仕掛けを買う", "Buy bait and rigs", () => OpenBaitShop(player), 23);
        }

        // ---------------- 小屋 ----------------

        public void OpenHutMenu(PlayerState player, string residentId)
        {
            var master = FindMaster();
            string rid = master != null ? master.Id : null;
            var options = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("竿の強化を頼む（呼び鈴を鳴らす）", "Upgrade rod (ring the bell)"), () => UIManager.Open(new RodUpgradeWindow(sys, player, rid))),
                new ChoiceOption(Loc.T("餌・仕掛けを買う", "Buy bait and rigs"), () => OpenBaitShop(player)),
                new ChoiceOption(Loc.T("名人が残した見本で技を練習する", "Practice with the master's models"), () => StartLesson(player, null, true)),
                new ChoiceOption(Loc.T("村の釣果ボードを見る", "View the village catch board"), () => UIManager.Open(new VillageRecordWindow(sys))),
                new ChoiceOption(Loc.T("やめる", "Leave"), null)
            };
            UIManager.Choose(Loc.T("釣り名人の小屋", "Fishing master's hut"),
                master != null ? null : Loc.T("名人の姿は見えない。棚には竿と見本の模型が並んでいる。", "The master isn't here. Rods and practice models line the shelf."), options);
        }

        private IResidentInfo FindMaster()
        {
            var dir = Game.Instance != null ? Game.Instance.Service<IResidentDirectory>() : null;
            if (dir == null) return null;
            return dir.FindByJob(ResidentJob.FishingMaster);
        }

        public void OpenBaitShop(PlayerState player)
        {
            var prof = sys.GetProfile(player.PlayerId);
            var entries = new List<VendorEntry>
            {
                new VendorEntry { ItemId = FishCatalog.BaitWorm, Price = 6 },
                new VendorEntry { ItemId = FishCatalog.BaitDough, Price = 8 },
                new VendorEntry { ItemId = FishCatalog.BaitGlowLure, Price = 80, Locked = prof.level < 2, LockedReasonJa = "釣りLv2から", LockedReasonEn = "From fishing Lv2" },
                new VendorEntry { ItemId = FishCatalog.BaitDeepSinker, Price = 45, Locked = prof.level < 2, LockedReasonJa = "釣りLv2から", LockedReasonEn = "From fishing Lv2" },
                new VendorEntry { ItemId = FishCatalog.BaitChimeLure, Price = 140, Locked = prof.level < 3, LockedReasonJa = "釣りLv3から", LockedReasonEn = "From fishing Lv3" },
            };
            UIManager.Open(new VendorWindow(Loc.T("餌・仕掛け", "Bait & rigs"), player, entries));
        }

        // ---------------- 技の実演 ----------------

        public void StartLesson(PlayerState player, string residentId, bool fromHutModel)
        {
            var prof = sys.GetProfile(player.PlayerId);
            int penalty = fromHutModel ? TechniqueInfo.HutLevelPenalty : 0;
            FishingTechnique? next = null;
            FishingTechnique? lockedNext = null;
            foreach (var t in TechniqueInfo.All)
            {
                if (prof.Knows(t)) continue;
                if (prof.level >= TechniqueInfo.RequiredLevel(t) + penalty)
                {
                    next = t;
                    break;
                }
                if (lockedNext == null) lockedNext = t;
            }
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (next == null)
            {
                if (lockedNext == null)
                {
                    if (residentId != null && dir != null)
                        dir.Speak(residentId, new Utterance(residentId, "fishing.lesson.all_done")
                            .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.KNOW).Agent(Concepts.YOU).Patient(Concepts.ALL))
                            .Face(Expression.VeryHappy).With(Gesture.Clap));
                    UIManager.Toast(Loc.T("もう教わる技は無いようだ", "There is nothing more to learn"));
                    return;
                }
                // まだ早い：首を振って水辺を指す（もっと釣っておいで）
                if (residentId != null && dir != null)
                    dir.Speak(residentId, new Utterance(residentId, "fishing.lesson.not_yet")
                        .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.CATCH_FISH).Agent(Concepts.YOU).Adverb(Concepts.MORE))
                        .Face(Expression.Thinking).With(Gesture.ShakeHead).With(Gesture.PointAt, "water"));
                UIManager.Toast(fromHutModel
                    ? Loc.Format("見本の動きはまだ難しそうだ（釣りLv{0}くらいで分かりそう）", "The model's moves look too hard for now (maybe around fishing Lv{0})",
                        TechniqueInfo.RequiredLevel(lockedNext.Value) + penalty)
                    : Loc.T("名人は首を振って、水辺を指さした。もう少し釣ってからにしよう。", "The master shook their head and pointed at the water. Fish a bit more first."), 4f);
                return;
            }
            if (residentId != null && dir != null)
            {
                dir.Speak(residentId, new Utterance(residentId, "fishing.lesson.start")
                    .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.SEE).Agent(Concepts.YOU))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.TEACH).Agent(Concepts.I).Recipient(Concepts.YOU).Patient(Concepts.CATCH_FISH))
                    .Face(Expression.Excited).With(Gesture.Beckon).With(Gesture.Hold, "fishing_rod"));
            }
            UIManager.Open(new LessonWindow(sys, player, residentId, next.Value, fromHutModel));
        }

        // ---------------- 釣り場の話（宇宙語＋身振り。正しい内容のみ） ----------------

        public void TalkAboutFishing(PlayerState player, string residentId)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (dir == null) return;
            var u = BuildHintUtterance(player, residentId);
            if (u != null) dir.Speak(residentId, u);
            Game.Instance.Service<ITabletService>()?.RecordResidentObservation(residentId,
                "釣り場の方を指さしながら、何かを話してくれた。", "Pointed toward the fishing spots while talking about something.");
        }

        private Utterance BuildHintUtterance(PlayerState player, string residentId)
        {
            int day = sys.CurrentDay;
            // 修理用の魚をまだ釣っていなければ、その手掛かりを日替わりで話す（低確率待ちにしないための住民の情報 §10）
            bool hasFrost = sys.FindLog(SharedItemIds.FishFrostFin) != null;
            bool hasRipple = sys.FindLog(SharedItemIds.FishRippleEye) != null;
            bool talkRocket = (!hasFrost || !hasRipple) && (day % 2 == 0);
            if (talkRocket)
            {
                bool frost = !hasFrost && (hasRipple || day % 4 == 0);
                if (frost)
                {
                    var spot = FindSpotWithTags(new[] { "waterfall", "deep", "cold", "rocky" }, FishWater.River | FishWater.Pond);
                    var u = new Utterance(residentId, "fishing.hint.frostfin").Related(SharedItemIds.FishFrostFin)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.Item(SharedItemIds.FishFrostFin), Concepts.BLUE))
                            .At(spot != null ? spot.WaterConcept() : Concepts.RIVER).When(Concepts.MORNING))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.Item(SharedItemIds.FishFrostFin)).Patient(MeaningArg.Of(Concepts.WATER, Concepts.COLD)))
                        .Face(Expression.Thinking).With(Gesture.Shiver);
                    if (spot != null) u.With(Gesture.PointAt, spot.Id);
                    return u;
                }
                if (!hasRipple)
                {
                    var spot = FindSpotWithTags(new[] { "deep" }, FishWater.Sea | FishWater.Pond);
                    var u = new Utterance(residentId, "fishing.hint.rippleeye").Related(SharedItemIds.FishRippleEye)
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.Item(SharedItemIds.FishRippleEye))
                            .At(spot != null ? spot.WaterConcept() : Concepts.SEA).When(Concepts.NIGHT))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BLOW).Agent(Concepts.WIND).Not())
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.Item(SharedItemIds.FishRippleEye)).Patient(Concepts.MUSIC))
                        .Face(Expression.Thinking).With(Gesture.LookUp);
                    if (spot != null) u.With(Gesture.PointAt, spot.Id);
                    return u;
                }
            }
            // 今日の傾向：活発な魚のうち一番珍しいものと、その場所
            SpotInfo bestSpot = null;
            FishSpecies bestFish = null;
            foreach (var s in sys.Spawn.Spots)
            {
                foreach (var id in sys.Spawn.TrendFor(day, s.Id))
                {
                    var f = FishCatalog.Get(id);
                    if (f == null) continue;
                    if (bestFish == null || f.Rarity > bestFish.Rarity)
                    {
                        bestFish = f;
                        bestSpot = s;
                    }
                }
            }
            if (bestFish == null)
            {
                return new Utterance(residentId, "fishing.hint.none")
                    .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.CATCH_FISH).Agent(Concepts.WE).Adverb(Concepts.TOGETHER))
                    .Face(Expression.Happy).With(Gesture.Wave);
            }
            var arg = MeaningArg.Of(Concepts.Item(bestFish.Id));
            var col = Concepts.ColorFromTags(ItemRegistry.Get(bestFish.Id));
            if (col != null) arg.Adj(col);
            var utt = new Utterance(residentId, "fishing.hint.trend").Related(bestFish.Id)
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(arg).At(bestSpot.WaterConcept()).When(Concepts.TODAY).Adverb(Concepts.MANY))
                .Face(Expression.Excited).With(Gesture.PointAt, bestSpot.Id);
            if (sys.Spawn.BigFishSpotId != null)
            {
                var big = sys.Spawn.GetSpot(sys.Spawn.BigFishSpotId);
                if (big != null)
                    utt.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.FISH, Concepts.BIG)).At(big.WaterConcept()).Adverb(Concepts.ALSO));
            }
            return utt;
        }

        private SpotInfo FindSpotWithTags(string[] tags, FishWater waters)
        {
            SpotInfo fallback = null;
            foreach (var s in sys.Spawn.Spots)
            {
                if ((s.Water & waters) == 0) continue;
                if (fallback == null) fallback = s;
                foreach (var t in tags) if (s.Tags.Contains(t)) return s;
            }
            return fallback;
        }
    }

    // =====================================================================
    // 竿の強化画面
    // =====================================================================

    public sealed class RodUpgradeWindow : UIWindow
    {
        private readonly FishingSystem sys;
        private readonly PlayerState player;
        private readonly string residentId;
        private bool busy;

        public RodUpgradeWindow(FishingSystem sys, PlayerState player, string residentId)
        {
            this.sys = sys;
            this.player = player;
            this.residentId = residentId;
        }

        public override string Title => Loc.T("釣り竿の強化", "Rod upgrade");
        public override Vector2 Size => new Vector2(820, 600);
        public override bool PausesTimeInSolo => true;

        public override void OnOpen()
        {
            // 名人は必要な素材を指さして数を示す（言葉が分からなくても分かる）
            var dir = Game.Instance.Service<IResidentDirectory>();
            int cur = Mathf.Max(1, player.Tools.GetLevel(ToolType.FishingRod));
            var req = RodUpgrades.ForTier(cur + 1);
            if (dir == null || string.IsNullOrEmpty(residentId) || req == null) return;
            var u = new Utterance(residentId, "fishing.rod_upgrade")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISHING_ROD, Concepts.STRONG)).Future())
                .Face(Expression.Happy).With(Gesture.Hold, "fishing_rod");
            foreach (var m in req.Materials)
            {
                if (!RodUpgrades.IsNeedActive(m)) continue;
                if (!string.IsNullOrEmpty(m.ItemId))
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.Item(m.ItemId)).Qty(m.Count)));
                    u.With(Gesture.PointAt, m.ItemId).With(Gesture.ShowNumber, null, m.Count);
                }
                else
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.FISH).Qty(m.Count)));
                    u.With(Gesture.ShowNumber, null, m.Count);
                }
            }
            u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.MONEY).Qty(req.Money)));
            dir.Speak(residentId, u);
        }

        public override void DrawContents(Rect content)
        {
            int cur = Mathf.Max(1, player.Tools.GetLevel(ToolType.FishingRod));
            GUILayout.Label(Loc.Format("今の竿：{0}（段階{1}）", "Current rod: {0} (tier {1})", ToolTypeUtil.TierLabel(cur), cur), UIStyles.LabelBold);
            var req = RodUpgrades.ForTier(cur + 1);
            if (req == null)
            {
                GUILayout.Label(Loc.T("これ以上強くはできないようだ。最高の竿だ！", "It can't get any better. The finest rod!"), UIStyles.LabelWrap);
                return;
            }
            GUILayout.Label(Loc.Format("強化後：{0}（段階{1}）", "After: {0} (tier {1})", ToolTypeUtil.TierLabel(req.TargetTier), req.TargetTier), UIStyles.Label);
            GUILayout.Label(Loc.Format("糸の強さ ×{0:0.00} → ×{1:0.00}　巻く速さ {2:0.0} → {3:0.0}", "Line strength x{0:0.00} → x{1:0.00}  Reel speed {2:0.0} → {3:0.0}",
                FishingTuning.RodLineStrength[cur - 1], FishingTuning.RodLineStrength[req.TargetTier - 1],
                FishingTuning.RodReelSpeed[cur - 1], FishingTuning.RodReelSpeed[req.TargetTier - 1]), UIStyles.LabelDim);
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("必要なもの", "Required"), UIStyles.LabelBold);
            bool ok = player.Wallet.CanAfford(req.Money);
            GUILayout.Label((ok ? "○ " : "× ") + UIStyles.Money(req.Money) + Loc.Format("（所持 {0}）", " (have {0})", UIStyles.Money(player.Wallet.money)), UIStyles.Label);
            foreach (var m in req.Materials)
            {
                if (!RodUpgrades.IsNeedActive(m)) continue;
                int have = player.Inventory.CountWhere(m.Matches);
                bool enough = have >= m.Count;
                ok &= enough;
                GUILayout.Label((enough ? "○ " : "× ") + m.Label() + Loc.Format("（所持 {0}）", " (have {0})", have), UIStyles.Label);
            }
            var plan = RodUpgrades.PlanPayment(req, player.Inventory);
            ok &= plan != null;
            if (plan != null && plan.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in plan) parts.Add(ItemRegistry.NameOf(kv.Key) + "×" + kv.Value);
                GUILayout.Label(Loc.T("渡すもの：", "Will hand over: ") + string.Join("、", parts), UIStyles.LabelDim);
            }
            GUILayout.FlexibleSpace();
            GUI.enabled = ok && !busy;
            if (GUILayout.Button(Loc.T("強化を頼む", "Request upgrade"), UIStyles.Button, GUILayout.Height(52)))
            {
                busy = true;
                sys.RequestRodUpgrade(player, residentId, success =>
                {
                    busy = false;
                    if (success)
                    {
                        var dir = Game.Instance.Service<IResidentDirectory>();
                        if (dir != null && !string.IsNullOrEmpty(residentId))
                            dir.Speak(residentId, new Utterance(residentId, "fishing.rod_upgrade.done")
                                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Recipient(Concepts.YOU).Patient(MeaningArg.Of(Concepts.FISHING_ROD, Concepts.NEW)))
                                .Face(Expression.VeryHappy).With(Gesture.OfferItem, "fishing_rod"));
                        Close();
                    }
                });
            }
            GUI.enabled = true;
        }
    }

    // =====================================================================
    // 技の実演・まね（§5.1：名人の実演を見て同じ動きを試す）
    // =====================================================================

    public sealed class LessonWindow : UIWindow
    {
        private enum Step { Demo, Try, Success, Fail }

        private readonly FishingSystem sys;
        private readonly PlayerState player;
        private readonly string residentId;
        private readonly FishingTechnique technique;
        private readonly bool fromHut;
        private readonly TechniqueInfo.Move[] moves;
        private Step step = Step.Demo;
        private int demoIndex;
        private float demoTimer;
        private int inputIndex;
        private int mistakes;
        private float flash;
        private TechniqueInfo.Move? lastInput;
        private readonly List<string> observed = new List<string>();

        public LessonWindow(FishingSystem sys, PlayerState player, string residentId, FishingTechnique technique, bool fromHut)
        {
            this.sys = sys;
            this.player = player;
            this.residentId = residentId;
            this.technique = technique;
            this.fromHut = fromHut;
            moves = TechniqueInfo.Demonstration(technique);
        }

        public override string Title => fromHut ? Loc.T("見本の模型で練習", "Practice with the model") : Loc.T("名人の実演", "The master's demonstration");
        public override Vector2 Size => new Vector2(900, 640);
        public override bool PausesTimeInSolo => true;

        public override void Update()
        {
            float dt = UnityEngine.Time.unscaledDeltaTime;
            flash = Mathf.Max(0f, flash - dt);
            switch (step)
            {
                case Step.Demo:
                    demoTimer += dt;
                    if (demoTimer >= 1.0f)
                    {
                        demoTimer = 0f;
                        if (demoIndex < moves.Length) observed.Add(TechniqueInfo.MoveObservation(moves[demoIndex]));
                        demoIndex++;
                        if (demoIndex > moves.Length)
                        {
                            step = Step.Try;
                            inputIndex = 0;
                        }
                    }
                    break;
                case Step.Try:
                    var m = ReadMove();
                    if (m == null) break;
                    lastInput = m;
                    flash = 0.25f;
                    if (m.Value == moves[inputIndex])
                    {
                        inputIndex++;
                        if (inputIndex >= moves.Length) Succeed();
                    }
                    else
                    {
                        mistakes++;
                        inputIndex = 0;
                        if (mistakes >= 3) Fail();
                    }
                    break;
            }
        }

        private static TechniqueInfo.Move? ReadMove()
        {
            if (GameInput.KeyDown(KeyCode.A) || GameInput.KeyDown(KeyCode.LeftArrow)) return TechniqueInfo.Move.Left;
            if (GameInput.KeyDown(KeyCode.D) || GameInput.KeyDown(KeyCode.RightArrow)) return TechniqueInfo.Move.Right;
            if (GameInput.KeyDown(KeyCode.W) || GameInput.KeyDown(KeyCode.UpArrow)) return TechniqueInfo.Move.RaiseRod;
            if (GameInput.KeyDown(KeyCode.Space)) return TechniqueInfo.Move.Reel;
            if (GameInput.KeyDown(KeyCode.S) || GameInput.KeyDown(KeyCode.DownArrow)) return TechniqueInfo.Move.Slack;
            return null;
        }

        private void Succeed()
        {
            step = Step.Success;
            sys.LearnTechnique(player, technique);
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (!string.IsNullOrEmpty(residentId))
            {
                dir?.Speak(residentId, new Utterance(residentId, "fishing.lesson.success")
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(Concepts.YOU).Adverb(Concepts.VERY))
                    .Face(Expression.VeryHappy).With(Gesture.Clap));
                WorldAuthority.Submit(new LessonLearnedCommand { residentId = residentId, technique = technique.ToString() });
                Game.Instance.Service<ITabletService>()?.RecordResidentObservation(residentId,
                    "釣りの竿さばきを実演して見せてくれた。真似をしたら、手を叩いて喜んでいた。",
                    "Demonstrated a fishing move. When I copied it, they clapped happily.");
            }
        }

        private void Fail()
        {
            step = Step.Fail;
            var prof = sys.GetProfile(player.PlayerId);
            if (!prof.lessonsTried.Contains(technique.ToString())) prof.lessonsTried.Add(technique.ToString());
            if (!string.IsNullOrEmpty(residentId))
            {
                Game.Instance.Service<IResidentDirectory>()?.Speak(residentId, new Utterance(residentId, "fishing.lesson.again")
                    .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.SEE).Agent(Concepts.YOU).Adverb(Concepts.AGAIN))
                    .Face(Expression.Thinking).With(Gesture.Think));
            }
        }

        public override void DrawContents(Rect content)
        {
            // 実演する人の簡易図（頭・体・竿）
            var stage = GUILayoutUtility.GetRect(content.width, 250);
            UIStyles.FillRect(stage, new Color(0.15f, 0.3f, 0.45f, 0.6f));
            UIStyles.FillRect(new Rect(stage.x, stage.y + 190, stage.width, 60), new Color(0.2f, 0.45f, 0.7f, 0.8f));
            TechniqueInfo.Move? showing = null;
            if (step == Step.Demo && demoIndex < moves.Length && demoTimer > 0.15f) showing = moves[demoIndex];
            if (step == Step.Try && flash > 0f) showing = lastInput;
            float cx = stage.x + 220, cy = stage.y + 120;
            UIStyles.FillRect(new Rect(cx - 30, cy - 10, 60, 80), fromHut ? new Color(0.6f, 0.5f, 0.4f) : new Color(0.6f, 0.8f, 0.6f));
            UIStyles.FillRect(new Rect(cx - 26, cy - 60, 52, 48), fromHut ? new Color(0.7f, 0.6f, 0.5f) : new Color(0.75f, 0.95f, 0.75f));
            float angle = -40f;
            float rodLen = 200f;
            if (showing.HasValue)
            {
                switch (showing.Value)
                {
                    case TechniqueInfo.Move.Left: angle = -70f; break;
                    case TechniqueInfo.Move.Right: angle = -10f; break;
                    case TechniqueInfo.Move.RaiseRod: angle = -85f; rodLen = 220f; break;
                    case TechniqueInfo.Move.Reel: angle = -40f; break;
                    case TechniqueInfo.Move.Slack: angle = -25f; rodLen = 180f; break;
                }
            }
            var pivot = new Vector2(cx + 20, cy + 10);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, pivot);
            UIStyles.FillRect(new Rect(pivot.x, pivot.y - 3, rodLen, 6), new Color(0.85f, 0.65f, 0.4f));
            GUI.matrix = m;
            if (showing == TechniqueInfo.Move.Reel)
                GUI.Label(new Rect(cx + 30, cy + 30, 100, 40), "↻", UIStyles.LabelTitle);
            if (showing == TechniqueInfo.Move.Slack)
                GUI.Label(new Rect(cx + 30, cy + 30, 100, 40), "〜", UIStyles.LabelTitle);

            GUILayout.Space(8);
            switch (step)
            {
                case Step.Demo:
                    GUILayout.Label(fromHut ? Loc.T("見本の模型の動きを、よく見る…", "Watch the model's movements carefully...")
                        : Loc.T("名人が竿を構えて、何か言いながら動きを見せてくれる…", "The master readies the rod and shows the moves while saying something..."), UIStyles.LabelWrap);
                    break;
                case Step.Try:
                    GUILayout.Label(Loc.T("同じ動きを真似してみよう：[A] 左　[D] 右　[W] 立てる　[Space] 巻く　[S] 緩める",
                        "Copy the moves: [A] left  [D] right  [W] raise  [Space] reel  [S] slack"), UIStyles.LabelBold);
                    GUILayout.Label(Loc.Format("{0} / {1} 手　（失敗 {2}/3）", "{0} / {1} moves  (misses {2}/3)", inputIndex, moves.Length, mistakes), UIStyles.Label);
                    break;
                case Step.Success:
                    GUILayout.Label(Loc.Format("技「{0}」を覚えた！", "Learned \"{0}\"!", TechniqueInfo.Name(technique)), UIStyles.LabelTitle);
                    GUILayout.Label(TechniqueInfo.Effect(technique), UIStyles.LabelWrap);
                    break;
                case Step.Fail:
                    GUILayout.Label(Loc.T("うまく真似できなかった。もう一度見せてもらおう。", "Couldn't copy it. Ask to see it again."), UIStyles.LabelWrap);
                    break;
            }
            GUILayout.Label(Loc.T("見た動き：", "Observed: ") + string.Join(Loc.T("、", ", "), observed), UIStyles.LabelDim);
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (step == Step.Fail && GUILayout.Button(Loc.T("もう一度見る", "Watch again"), UIStyles.Button, GUILayout.Height(48)))
            {
                step = Step.Demo;
                demoIndex = 0;
                demoTimer = 0f;
                mistakes = 0;
                observed.Clear();
            }
            if (GUILayout.Button(Loc.T("閉じる", "Close"), UIStyles.Button, GUILayout.Height(48))) Close();
            GUILayout.EndHorizontal();
        }
    }

    // =====================================================================
    // 村の釣果ボード（星の記録。マルチで大物を見せ合う §13）
    // =====================================================================

    public sealed class VillageRecordWindow : UIWindow
    {
        private readonly FishingSystem sys;

        public VillageRecordWindow(FishingSystem sys) { this.sys = sys; }

        public override string Title => Loc.T("村の釣果ボード", "Village catch board");
        public override Vector2 Size => new Vector2(820, 640);

        public override void DrawContents(Rect content)
        {
            var recs = new List<VillageFishRecord>(sys.PlanetData.records);
            recs.Sort((a, b) => b.size.CompareTo(a.size));
            GUILayout.Label(Loc.Format("この星で釣られた魚：{0}匹", "Fish caught on this planet: {0}", sys.PlanetData.totalCatchesOnPlanet), UIStyles.LabelDim);
            scroll = GUILayout.BeginScrollView(scroll);
            if (recs.Count == 0) GUILayout.Label(Loc.T("まだ記録はない。", "No records yet."), UIStyles.LabelDim);
            foreach (var r in recs)
            {
                GUILayout.BeginHorizontal(UIStyles.Box);
                var icon = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(icon, new ItemStack(r.itemId, 1, r.quality));
                GUILayout.Label(ItemRegistry.NameOf(r.itemId), UIStyles.Label, GUILayout.Width(300));
                GUILayout.Label(string.Format("{0:0.0}cm", r.size), UIStyles.LabelBold, GUILayout.Width(110));
                GUILayout.Label(r.playerName + Loc.Format("（{0}日目）", " (day {0})", r.day), UIStyles.LabelDim);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }
}
