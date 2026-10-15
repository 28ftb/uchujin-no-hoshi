using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 飼っている生き物の見た目と生活（生き物仕様 §6.2：仕事以外の時間にも遊ぶ・眠る・食べる・交流する）。
    /// 同行中は主人公の後ろをついて歩く。農場・庭・作業場では住み場所の周りで暮らす。
    /// SendMessage の受け口："UchujinCreatureAnchor"(Vector3)・"UchujinCreatureWork"(Vector3)（CreatureSystem.Owned 参照）。
    /// </summary>
    public class OwnedCreatureAvatar : MonoBehaviour, IInteractable
    {
        private enum S { Idle, Walk, Sleep, Act, Work, WorkAct, Pair, Follow }

        public string Uid;
        public string SpeciesId;
        public CreatureSpecies Species;
        public CreatureBody Body;
        public bool IsCompanion;
        public int FollowIndex;
        public int BuiltLevelStage;
        public float InPairUntil;

        private CreatureSystem sys;
        private S state = S.Idle;
        private float timer;
        private Vector3 anchor;
        private bool anchorOverride;
        private CreaturePlacement lastPlacement = (CreaturePlacement)(-1);
        private Vector3 target;
        private float speed = 1.2f;
        private Vector3 workTarget;
        private float actTimer;
        private PairStyle pairStyle;
        private OwnedCreatureAvatar partner;
        private bool pairLeader;
        private bool indoor;

        public static OwnedCreatureAvatar Create(CreatureSystem sys, OwnedCreature c, CreatureSpecies sp, Transform parent, Vector3 anchor, int stage)
        {
            float scale = sp.Scale * c.size * (0.75f + 0.08f * c.level);
            var go = CreatureVisualBuilder.Build(sp, parent, anchor, c.colorVariant, c.hueShift, scale, c.level, CreatureSystem.AvatarObjectName(c.uid));
            var body = go.AddComponent<CreatureBody>();
            body.Init(sp);
            var av = go.AddComponent<OwnedCreatureAvatar>();
            av.sys = sys;
            av.Uid = c.uid;
            av.SpeciesId = c.speciesId;
            av.Species = sp;
            av.Body = body;
            av.BuiltLevelStage = stage;
            av.anchor = anchor;
            av.actTimer = 8f + (StableHash.Fnv1a64(c.uid) % 100) / 10f;
            return av;
        }

        public void Apply(OwnedCreature c, Vector3 newAnchor, bool companion, int index)
        {
            IsCompanion = companion;
            FollowIndex = index;
            if (c.placement != lastPlacement)
            {
                anchorOverride = false;
                lastPlacement = c.placement;
                indoor = c.placement == CreaturePlacement.Habitat;
                if (!companion)
                {
                    var p = newAnchor;
                    p.y = GroundY(p);
                    transform.position = p;
                }
            }
            if (!anchorOverride) anchor = newAnchor;
            float scale = Species.Scale * c.size * (0.75f + 0.08f * c.level);
            Body.SetBaseScale(scale);
        }

        // ---------------- SendMessage の受け口（他モジュールからの演出指示） ----------------

        /// <summary>住み場所の位置（農場の住処など）。</summary>
        public void UchujinCreatureAnchor(Vector3 p)
        {
            anchor = p;
            anchorOverride = true;
        }

        /// <summary>そこへ行って働く仕草をする（農場の水やり・収穫など）。</summary>
        public void UchujinCreatureWork(Vector3 p)
        {
            if (IsCompanion) return;
            workTarget = p;
            speed = 2.6f;
            state = S.Work;
            timer = 8f;
            Body.CurrentPose = Species.Behavior == WildBehavior.Flyer ? CreatureBody.Pose.Fly : CreatureBody.Pose.Walk;
        }

        public void OnPlayerGesture(PlayerGesture g)
        {
            if (!Species.HasAbility(CreatureAbility.Mimic)) return;
            Body.PlayGesture(g, 1.8f);
            Body.ShowEmote("♪", 1.4f);
            sys.RecordBehavior(Species.Id, "copy_gesture", false);
        }

        public void PlayPair(PairStyle style, OwnedCreatureAvatar other, bool leader)
        {
            pairStyle = style;
            partner = other;
            pairLeader = leader;
            InPairUntil = Time.time + 7f;
            state = S.Pair;
            timer = 7f;
            switch (style)
            {
                case PairStyle.SleepTogether:
                    Body.CurrentPose = CreatureBody.Pose.Sleep;
                    Body.ShowEmote("z", 3f);
                    break;
                case PairStyle.AlternateGlow:
                case PairStyle.FlyTogether:
                    Body.CurrentPose = CreatureBody.Pose.Fly;
                    break;
                case PairStyle.CopyRhythm:
                    Body.ShowEmote("♪", 3f);
                    break;
                default:
                    Body.CurrentPose = CreatureBody.Pose.Walk;
                    Body.ShowEmote("♪", 2f);
                    break;
            }
        }

        // ---------------- 毎フレーム ----------------

        private void Update()
        {
            var game = Game.Instance;
            if (game == null || sys == null || game.State != GameState.Playing) return;
            float dt = Time.deltaTime;
            timer -= dt;
            if (IsCompanion && state != S.Pair)
            {
                UpdateFollow(game.LocalPlayer, dt);
                return;
            }
            switch (state)
            {
                case S.Idle:
                    if (ShouldSleep())
                    {
                        state = S.Sleep;
                        Body.CurrentPose = CreatureBody.Pose.Sleep;
                        timer = 30f;
                        break;
                    }
                    if (timer <= 0f)
                    {
                        var c = Random.insideUnitCircle * 2.6f;
                        target = anchor + new Vector3(c.x, 0, c.y);
                        speed = Species.Behavior == WildBehavior.Hop ? 2f : 1.1f;
                        state = S.Walk;
                        timer = 6f;
                        Body.CurrentPose = Species.Behavior == WildBehavior.Hop ? CreatureBody.Pose.Hop
                            : Species.Behavior == WildBehavior.Flyer || Species.Behavior == WildBehavior.Swarm ? CreatureBody.Pose.Fly
                            : Species.Behavior == WildBehavior.Float || Species.Behavior == WildBehavior.Drift ? CreatureBody.Pose.Float : CreatureBody.Pose.Walk;
                    }
                    break;
                case S.Walk:
                    if (MoveTo(target, speed, dt) || timer <= 0f) ToIdle();
                    break;
                case S.Sleep:
                    if (!ShouldSleep() || timer <= -600f) ToIdle();
                    break;
                case S.Act:
                    if (timer <= 0f) ToIdle();
                    break;
                case S.Work:
                    if (MoveTo(workTarget, speed, dt) || timer <= 0f)
                    {
                        state = S.WorkAct;
                        timer = 1.8f;
                        Body.PlayAct(1.8f);
                        WorkFx();
                    }
                    break;
                case S.WorkAct:
                    if (timer <= 0f)
                    {
                        target = anchor;
                        speed = 1.6f;
                        state = S.Walk;
                        timer = 8f;
                    }
                    break;
                case S.Pair:
                    UpdatePair(dt);
                    if (timer <= 0f) ToIdle();
                    break;
            }

            if (state == S.Idle || state == S.Walk)
            {
                actTimer -= dt;
                if (actTimer <= 0f)
                {
                    actTimer = 14f + Random.Range(0f, 10f);
                    Body.PlayAct(1.6f);
                    var local = game.LocalPlayer;
                    if (local != null && (local.Position - transform.position).sqrMagnitude < 10f * 10f && !string.IsNullOrEmpty(Species.SignatureBehavior))
                        sys.RecordBehavior(Species.Id, Species.SignatureBehavior, false);
                }
            }
        }

        private void ToIdle()
        {
            state = S.Idle;
            timer = 2f + Random.Range(0f, 4f);
            Body.CurrentPose = Species.Behavior == WildBehavior.Float || Species.Behavior == WildBehavior.Drift ? CreatureBody.Pose.Float : CreatureBody.Pose.Idle;
        }

        private bool ShouldSleep()
        {
            var clock = Game.Instance.Time;
            if (clock == null) return false;
            int h = clock.Hour;
            bool nocturnal = (Species.Active & (ActiveTime.Night | ActiveTime.LateNight)) != 0 && (Species.Active & ActiveTime.Daytime) == 0;
            if (nocturnal) return h >= 9 && h < 16;
            var c = sys.Find(Uid);
            bool sleepy = c != null && c.personality == (int)CreaturePersonality.Sleepy;
            return h >= (sleepy ? 21 : 22) || h < 5 || (sleepy && h >= 13 && h < 14);
        }

        private void UpdateFollow(PlayerState player, float dt)
        {
            if (player == null) return;
            var fwd = player.Forward;
            fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            var side = Vector3.Cross(Vector3.up, fwd);
            var desired = player.Position - fwd * (1.5f + FollowIndex * 0.9f) + side * ((FollowIndex % 2 == 0 ? 1 : -1) * 0.7f);
            float dist = Vector3.Distance(new Vector3(transform.position.x, 0, transform.position.z), new Vector3(desired.x, 0, desired.z));
            if (dist > 18f || Mathf.Abs(transform.position.y - player.Position.y) > 4f)
            {
                transform.position = desired; // エリア移動・遠くへ離れた時はそばへ
                return;
            }
            if (dist > 0.6f)
            {
                speed = Mathf.Clamp(dist * 1.6f, 1.5f, 9f);
                MoveTo(desired, speed, dt, player.Position.y);
                if (Body.CurrentPose == CreatureBody.Pose.Idle || Body.CurrentPose == CreatureBody.Pose.Sleep)
                    Body.CurrentPose = Species.Behavior == WildBehavior.Hop ? CreatureBody.Pose.Hop
                        : Species.Behavior == WildBehavior.Flyer || Species.Behavior == WildBehavior.Swarm ? CreatureBody.Pose.Fly : CreatureBody.Pose.Walk;
            }
            else
            {
                Body.CurrentPose = Species.Behavior == WildBehavior.Float || Species.Behavior == WildBehavior.Drift ? CreatureBody.Pose.Float : CreatureBody.Pose.Idle;
                var look = player.Position - transform.position;
                look.y = 0;
                if (look.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(look), 240f * dt);
                actTimer -= dt;
                if (actTimer <= 0f)
                {
                    actTimer = 18f + Random.Range(0f, 12f);
                    Body.PlayAct(1.4f);
                    var c = sys.Find(Uid);
                    if (c != null && c.affection >= 50) Body.ShowEmote("♥", 1.5f); // なついているとハートを見せる
                }
            }
        }

        private void UpdatePair(float dt)
        {
            if (partner == null) return;
            var p = partner.transform.position;
            switch (pairStyle)
            {
                case PairStyle.Chase:
                    if (!pairLeader) MoveTo(p, 2.5f, dt);
                    else MoveTo(transform.position + (transform.position - p).normalized * 2f, 2f, dt);
                    break;
                case PairStyle.FlyTogether:
                    {
                        float a = Time.time * 1.6f + (pairLeader ? 0f : Mathf.PI);
                        var center = (transform.position + p) * 0.5f;
                        MoveTo(center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.2f, 2f, dt);
                        break;
                    }
                case PairStyle.AlternateGlow:
                    Body.SetGlow(Mathf.Repeat(Time.time + (pairLeader ? 0f : 1f), 2f) < 1f);
                    break;
                case PairStyle.SleepTogether:
                    if ((p - transform.position).sqrMagnitude > 0.8f) MoveTo(p, 1f, dt);
                    break;
                case PairStyle.Splash:
                    if (Mathf.Repeat(Time.time, 1.2f) < dt) CreatureFx.Puff(transform.position + Vector3.up * 0.4f, new Color(0.6f, 0.85f, 1f), 3);
                    break;
                case PairStyle.CopyRhythm:
                    if (Mathf.Repeat(Time.time + (pairLeader ? 0f : 0.3f), 0.8f) < dt) Body.PlayAct(0.3f);
                    break;
                case PairStyle.Play:
                    if (Mathf.Repeat(Time.time, 2f) < dt) Body.PlayAct(0.6f);
                    break;
            }
        }

        private void WorkFx()
        {
            var p = transform.position + Vector3.up * 0.5f;
            var a = Species.Abilities.Length > 0 ? Species.Abilities[0] : CreatureAbility.Carry;
            switch (a)
            {
                case CreatureAbility.Water:
                case CreatureAbility.Splash: CreatureFx.Puff(p, new Color(0.5f, 0.8f, 1f), 8); break;
                case CreatureAbility.Till:
                case CreatureAbility.SoilImprove: CreatureFx.Puff(p - Vector3.up * 0.4f, new Color(0.45f, 0.33f, 0.22f), 7); break;
                case CreatureAbility.Sow:
                case CreatureAbility.SeedCollect: CreatureFx.Puff(p, new Color(0.9f, 0.8f, 0.4f), 5, 0.7f, 0.05f); break;
                case CreatureAbility.Harvest:
                case CreatureAbility.Carry: CreatureFx.Puff(p, new Color(1f, 0.6f, 0.3f), 4, 0.6f); break;
                case CreatureAbility.Pollinate: CreatureFx.Puff(p, new Color(1f, 0.85f, 0.4f), 6, 0.5f, 0.04f, true); break;
                case CreatureAbility.Glow: CreatureFx.Puff(p, Species.Accent, 6, 0.4f, 0.05f, true); break;
                case CreatureAbility.Heat: CreatureFx.Puff(p, new Color(1f, 0.55f, 0.3f), 5, 0.5f); break;
                case CreatureAbility.Cool: CreatureFx.Puff(p, new Color(0.75f, 0.95f, 1f), 5, 0.5f); break;
                default: Body.ShowEmote("♪", 1.5f); break;
            }
        }

        private bool MoveTo(Vector3 dest, float spd, float dt, float? yOverride = null)
        {
            var pos = transform.position;
            var flat = new Vector3(dest.x - pos.x, 0, dest.z - pos.z);
            float dist = flat.magnitude;
            if (dist < 0.15f) return true;
            var np = pos + flat / dist * Mathf.Min(dist, spd * dt);
            np.y = yOverride ?? GroundY(np);
            transform.position = np;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat, Vector3.up), 540f * dt);
            return dist < 0.2f;
        }

        private float GroundY(Vector3 p)
        {
            if (indoor) return anchor.y;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            if (layout == null) return p.y;
            return layout.HeightAt(p.x, p.z);
        }

        // ---------------- IInteractable ----------------

        public string GetInteractionPrompt(PlayerState player)
        {
            var c = sys.Find(Uid);
            if (c == null) return null;
            if (player != null && c.ownerId != player.PlayerId) return Loc.Format("{0}をなでる", "Pet {0}", sys.DisplayName(c));
            return Loc.Format("{0}と遊ぶ", "Play with {0}", sys.DisplayName(c));
        }

        public bool CanInteract(PlayerState player) => sys != null && sys.Find(Uid) != null;

        public void Interact(PlayerState player)
        {
            var c = sys.Find(Uid);
            if (c == null) return;
            if (player != null && c.ownerId != player.PlayerId)
            {
                sys.Pet(c);
                return;
            }
            CreatureCareMenu.Open(sys, c);
        }
    }
}
