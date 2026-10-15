using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Fishing
{
    /// <summary>
    /// 釣りミニゲームの中身（釣り仕様 §3：ライン操作・魚との駆け引き型）。
    /// ・巻く（Space／左クリック長押し）：魚を寄せるが張力が上がる。
    /// ・緩める（離す／S）：張力が下がるが、引かれた分だけ糸が出る。
    /// ・竿を倒す（A/D）：魚が走る向きと反対へ倒すと張力が上がりにくく、魚が疲れる。
    /// ・竿を立てる（W）：潜る魚を誘導する。
    /// ・跳ねている間に巻くと張力が跳ね上がる。
    /// 魚の行動タイプごとに動き（行動の並び）が違う。数式・数値は【仮】。
    /// </summary>
    public sealed class FishFight
    {
        public enum ActionKind { Drift, Rest, Burst, Dive, Jump, Turn, Puff, Wave, Phase, Harden }
        public enum Outcome { None, Landed, LineBroke, Escaped }

        public FishSpecies Species { get; }
        public float SizeFactor { get; }
        public float Distance { get; private set; }
        public float StartDistance { get; }
        public float Tension { get; private set; }
        public float Stamina { get; private set; } = 1f;
        public float RodX { get; private set; }
        public bool RodUp { get; private set; }
        public bool Reeling { get; private set; }
        public ActionKind Action { get; private set; } = ActionKind.Drift;
        public ActionKind NextAction { get; private set; } = ActionKind.Drift;
        /// <summary>魚が引いている向き（-1=左, 1=右）。Dive/Jump では縦。</summary>
        public float FishDir { get; private set; } = 1f;
        public float ActionTimeLeft => actionLeft;
        public float Elapsed { get; private set; }
        public float MaxTension { get; private set; }
        public Outcome Result { get; private set; } = Outcome.None;
        public bool Puffed { get; private set; }
        /// <summary>直前のフレームで糸さばきが働いた（表示用）。</summary>
        public float LineReliefFlash { get; private set; }
        public float CounterQuality { get; private set; }

        private readonly SeededRandom rng;
        private readonly FishingProfileData profile;
        private readonly int rodTier;
        private readonly float basePull;
        private readonly float staminaFactor;
        private readonly float lineStrength;
        private readonly float reelSpeed;
        private float actionLeft;
        private float overTime;
        private float slackTime;
        private float reliefCooldown;
        private float wavePhase;

        public FishFight(FishSpecies species, float sizeFactor01, int rodTier, FishingProfileData profile, SeededRandom rng)
        {
            Species = species;
            SizeFactor = Mathf.Clamp01(sizeFactor01);
            this.rng = rng;
            this.profile = profile;
            this.rodTier = Mathf.Clamp(rodTier, 1, 5);
            int level = profile != null ? profile.level : 1;
            basePull = (0.22f + 0.06f * species.Difficulty) * (1f + 0.2f * SizeFactor) * (1f - FishingTuning.LevelPullReduction * (level - 1));
            staminaFactor = 0.55f + 0.12f * species.Difficulty;
            if (species.Behavior == FishBehavior.Endurance) staminaFactor *= 1.7f;
            if (species.Behavior == FishBehavior.LegendLord) staminaFactor *= 2.0f;
            staminaFactor /= 1f + FishingTuning.LevelDrainBonus * (level - 1);
            lineStrength = FishingTuning.RodLineStrength[this.rodTier - 1];
            reelSpeed = FishingTuning.RodReelSpeed[this.rodTier - 1];
            StartDistance = 10f + 1.2f * species.Difficulty + 5f * SizeFactor;
            Distance = StartDistance;
            Tension = 0.3f;
            FishDir = rng.Chance(0.5f) ? -1f : 1f;
            NextAction = ChooseAction();
            BeginNextAction();
        }

        private bool Knows(FishingTechnique t) => profile != null && profile.Knows(t);

        // ---------------- 魚の行動 ----------------

        private ActionKind ChooseAction()
        {
            float[] w;
            // 並び：Drift, Rest, Burst, Dive, Jump, Turn, Puff, Wave, Phase, Harden
            switch (Species.Behavior)
            {
                case FishBehavior.Calm: w = new float[] { 4, 3, 0.6f, 0, 0, 0, 0, 0, 0, 0 }; break;
                case FishBehavior.Rush: w = new float[] { 2, 1.5f, 3, 0, 0, 0, 0, 0, 0, 0 }; break;
                case FishBehavior.Diver: w = new float[] { 2, 1.5f, 0.5f, 3, 0, 0, 0, 0, 0, 0 }; break;
                case FishBehavior.Jumper: w = new float[] { 2, 1.5f, 0.5f, 0, 3, 0, 0, 0, 0, 0 }; break;
                case FishBehavior.Turner: w = new float[] { 2, 1.2f, 0.5f, 0, 0, 3, 0, 0, 0, 0 }; break;
                case FishBehavior.Endurance: w = new float[] { 3, 0.6f, 1.2f, 0.5f, 0, 0, 0, 0, 0, 0 }; break;
                case FishBehavior.Erratic: w = new float[] { 1.5f, 1, 1.5f, 1.5f, 1.5f, 1.5f, 0, 0, 0, 0 }; break;
                case FishBehavior.Puffer:
                    w = Puffed ? new float[] { 1.5f, 0.8f, 2.5f, 0, 0, 1, 0, 0, 0, 0 } : new float[] { 3, 2, 0.5f, 0, 0, 0, 0, 0, 0, 0 };
                    break;
                case FishBehavior.LegendLord: w = new float[] { 2, 0.6f, 2.5f, 2, 0, 0.8f, 0, 0, 0, 0 }; break;
                case FishBehavior.LegendTide: w = new float[] { 0.5f, 1, 0, 0, 0, 0, 0, 4, 0, 0 }; break;
                case FishBehavior.LegendPhantom: w = new float[] { 1, 1, 0.5f, 0, 0, 2, 0, 0, 3, 0 }; break;
                case FishBehavior.LegendCrystal: w = new float[] { 2, 1.5f, 0.5f, 1, 0, 0, 0, 0, 0, 2.5f }; break;
                default: w = new float[] { 0.8f, 1, 1, 0, 4, 0, 0, 0, 0, 0 }; break; // LegendSkylark
            }
            int idx = rng.WeightedIndex(w);
            return idx < 0 ? ActionKind.Drift : (ActionKind)idx;
        }

        private void BeginNextAction()
        {
            Action = NextAction;
            switch (Action)
            {
                case ActionKind.Rest: actionLeft = rng.Range(1.0f, 1.9f); break;
                case ActionKind.Burst: actionLeft = rng.Range(0.8f, 1.5f); FishDir = rng.Chance(0.5f) ? -1f : 1f; break;
                case ActionKind.Dive: actionLeft = rng.Range(1.1f, 2.0f); break;
                case ActionKind.Jump: actionLeft = rng.Range(0.6f, 0.9f); break;
                case ActionKind.Turn: actionLeft = rng.Range(0.6f, 1.0f); FishDir = -FishDir; break;
                case ActionKind.Wave: actionLeft = rng.Range(2.5f, 4f); wavePhase = 0f; break;
                case ActionKind.Phase: actionLeft = rng.Range(1.2f, 2f); break;
                case ActionKind.Harden: actionLeft = rng.Range(0.7f, 1.1f); break;
                default: actionLeft = rng.Range(1.0f, 2.2f); if (rng.Chance(0.4f)) FishDir = -FishDir; break;
            }
            NextAction = ChooseAction();
        }

        private float ActionPull()
        {
            switch (Action)
            {
                case ActionKind.Rest: return 0.15f;
                case ActionKind.Burst: return 1.5f;
                case ActionKind.Dive: return 1.15f;
                case ActionKind.Jump: return 0.9f;
                case ActionKind.Turn: return 1.25f;
                case ActionKind.Wave: return 0.35f + 1.3f * Mathf.Max(0f, Mathf.Sin(wavePhase));
                case ActionKind.Phase: return 1.05f;
                case ActionKind.Harden: return 2.1f;
                default: return 0.6f;
            }
        }

        /// <summary>竿の操作がどれだけ魚の動きに合っているか 0〜1。</summary>
        private float Counter(bool reeling)
        {
            switch (Action)
            {
                case ActionKind.Dive: return RodUp ? 1f : 0f;
                case ActionKind.Jump: return reeling ? 0f : 1f;
                case ActionKind.Harden: return reeling ? 0f : 0.8f;
                case ActionKind.Rest: return 0.6f;
                case ActionKind.Wave: return reeling ? (Mathf.Sin(wavePhase) < 0.2f ? 1f : 0f) : 0.6f;
                case ActionKind.Phase:
                    // 向きが揺らいで見えない。中央に構えると多少効く
                    return 0.5f - Mathf.Abs(RodX) * 0.4f;
                default:
                    return Mathf.Clamp01(-RodX * FishDir);
            }
        }

        // ---------------- 毎フレーム ----------------

        public void Update(float dt, bool reelInput, bool slackInput, float tiltInput, bool raiseInput)
        {
            if (Result != Outcome.None || dt <= 0f) return;
            Elapsed += dt;
            LineReliefFlash = Mathf.Max(0f, LineReliefFlash - dt);
            reliefCooldown = Mathf.Max(0f, reliefCooldown - dt);

            // 竿の操作
            if (Mathf.Abs(tiltInput) > 0.01f) RodX = Mathf.Clamp(RodX + tiltInput * 2.6f * dt, -1f, 1f);
            else RodX = Mathf.MoveTowards(RodX, 0f, 0.8f * dt);
            RodUp = raiseInput;
            Reeling = reelInput && !slackInput;

            // 行動の進行
            actionLeft -= dt;
            if (Action == ActionKind.Wave) wavePhase += dt * 2.4f;
            if (Action == ActionKind.Phase && rng.Chance(dt * 2.5f)) FishDir = -FishDir;
            if (actionLeft <= 0f) BeginNextAction();
            if (Species.Behavior == FishBehavior.Puffer && !Puffed && Stamina < 0.55f)
            {
                Puffed = true;
                Action = ActionKind.Burst;
                actionLeft = 1.2f;
            }

            float counter = Counter(Reeling);
            float guide = Knows(FishingTechnique.Guide) ? FishingTuning.GuideMultiplier : 1f;
            counter = Mathf.Clamp01(counter * guide);
            CounterQuality = counter;

            float pull = basePull * ActionPull() * (0.4f + 0.6f * Stamina);
            if (Puffed) pull *= 1.6f;
            float progress = 1f - Distance / StartDistance;
            bool secretZone = Knows(FishingTechnique.MasterSecret) && progress >= 1f - FishingTuning.MasterSecretZone;
            if (secretZone) pull *= 0.5f;
            float effPull = pull * (1f - 0.6f * counter);

            if (Reeling)
            {
                float t = (0.22f + effPull * 1.15f) / lineStrength;
                if (Action == ActionKind.Jump) t += 1.3f / lineStrength;
                if (Action == ActionKind.Harden) t += 1.6f / lineStrength;
                Tension += t * dt;
                float speed = reelSpeed * Mathf.Clamp01(1f - 0.65f * effPull);
                bool resting = Action == ActionKind.Rest || Stamina < 0.3f;
                if (resting && Knows(FishingTechnique.PowerReel)) speed *= FishingTuning.PowerReelMultiplier;
                Distance -= speed * dt;
            }
            else
            {
                float slack = FishingTuning.SlackRate * (slackInput ? 1.7f : 1f);
                if (Knows(FishingTechnique.LineHandling)) slack *= FishingTuning.LineHandlingSlackMultiplier;
                Tension -= slack * dt;
                Tension += effPull * 0.18f * dt; // 引かれている分の張り
                Distance += effPull * (slackInput ? 6.5f : 4.5f) * dt;
            }

            // 魚の体力
            float drain = (0.035f + 0.11f * counter + (Reeling ? 0.04f : 0f)) / staminaFactor;
            if (Action == ActionKind.Rest) drain *= 0.3f;
            Stamina = Mathf.Clamp01(Stamina - drain * dt + (Action == ActionKind.Rest ? 0.02f * dt : 0f));

            // 糸さばき：切れる直前に一度だけ緩める
            if (Tension > 0.95f && reliefCooldown <= 0f && Knows(FishingTechnique.LineHandling))
            {
                Tension -= FishingTuning.LineHandlingRelief;
                reliefCooldown = FishingTuning.LineHandlingCooldown;
                LineReliefFlash = 0.8f;
            }

            Tension = Mathf.Clamp(Tension, 0f, 1.25f);
            MaxTension = Mathf.Max(MaxTension, Tension);

            // 判定
            if (Distance <= 0f)
            {
                Distance = 0f;
                Result = Outcome.Landed;
                return;
            }
            if (Tension >= 1f) overTime += dt;
            else overTime = Mathf.Max(0f, overTime - dt * 2f);
            if (overTime >= FishingTuning.BreakHoldSeconds)
            {
                Result = Outcome.LineBroke;
                return;
            }
            bool slackDanger = Tension < FishingTuning.SlackThreshold && Action != ActionKind.Rest && !secretZone;
            if (slackDanger) slackTime += dt;
            else slackTime = Mathf.Max(0f, slackTime - dt);
            if (slackTime >= FishingTuning.SlackEscapeSeconds || Distance > StartDistance + FishingTuning.FightMaxDistanceExtra)
            {
                Result = Outcome.Escaped;
            }
        }

        public float SlackDanger01 => Mathf.Clamp01(slackTime / FishingTuning.SlackEscapeSeconds);
        public float BreakDanger01 => Mathf.Clamp01(overTime / FishingTuning.BreakHoldSeconds);
        public bool IsTired => Stamina < 0.3f;
        public float MaxDistance => StartDistance + FishingTuning.FightMaxDistanceExtra;

        /// <summary>品質（§8）：無理なく釣ったか・早さ・大きさから 0〜3。</summary>
        public int ComputeQuality(float sizePercentile)
        {
            int q = 0;
            if (MaxTension < 0.9f) q++;
            float par = 8f + Species.Difficulty * 2.2f;
            if (Elapsed <= par) q++;
            if (sizePercentile >= 0.75f) q++;
            return Mathf.Clamp(q, 0, 3);
        }

        public static string ActionHint(ActionKind a)
        {
            switch (a)
            {
                case ActionKind.Rest: return Loc.T("休む", "rests");
                case ActionKind.Burst: return Loc.T("≫ 走る", "≫ bolt");
                case ActionKind.Dive: return Loc.T("↓ 潜る", "↓ dive");
                case ActionKind.Jump: return Loc.T("↑ 跳ねる", "↑ jump");
                case ActionKind.Turn: return Loc.T("⇄ 反転", "⇄ turn");
                case ActionKind.Wave: return Loc.T("〜 波", "~ wave");
                case ActionKind.Phase: return Loc.T("？ 揺らぐ", "? waver");
                case ActionKind.Harden: return Loc.T("◆ 固まる", "◆ stiffen");
                case ActionKind.Puff: return Loc.T("● 膨らむ", "● puff");
                default: return Loc.T("泳ぐ", "swims");
            }
        }
    }
}
