using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 野生の宇宙生物1匹の行動（生き物仕様 §3 の習性・§5.2 の捕獲の型）。
    /// 跳ねて休む、驚くと転がる、擬態する、浮かぶ・漂う、走ると逃げる、巣穴に潜る、眠る（岩を叩くと起きる）、
    /// マネピは主人公の身振りを真似る、群れで光をそろえる……など。
    /// 出現・消滅はホストが決め、動きは各端末の見た目だけ（同期しない）。捕獲の可否はホストが確定する。
    /// </summary>
    public class WildCreature : MonoBehaviour, IInteractable
    {
        private enum S { Idle, Move, Rest, Flee, Hidden, Sleep, Eat, Mimic, Roll, Fly, Act, ToBait }

        // ---------------- 【仮数値】警戒・行動 ----------------
        private const float SeenRadius = 15f;
        private const float ObserveRadius = 12f;
        private const float WanderRadius = 4f;

        public WildRecord Record;
        public CreatureSpecies Species;
        public CreatureBody Body;

        private CreatureSystem sys;
        private S state;
        private float timer;
        private Vector3 home;
        private Vector3 target;
        private float speed;
        private bool discovered;
        private bool needsDiscovery;
        private float waryUntil;
        private float actTimer;
        private float seenTimer;
        private bool seenRecorded;
        private float stillTimer;
        private bool inMine;
        private bool onWater;
        private Vector3 windDir = Vector3.right;
        private BaitSpot bait;
        private float flashCycle;
        private Color envColor;

        public bool Discovered => discovered;

        public static WildCreature Create(CreatureSystem sys, WildRecord rec, CreatureSpecies sp, Transform parent)
        {
            CreatureSystem.IndividualLook(rec.individualSeed ?? rec.wildId, out var size, out var hue, out var variant);
            var go = CreatureVisualBuilder.Build(sp, parent, rec.Position, variant, hue, sp.Scale * size, 1, "Wild_" + rec.wildId);
            var body = go.AddComponent<CreatureBody>();
            body.Init(sp);
            var w = go.AddComponent<WildCreature>();
            w.Init(sys, rec, sp, body);
            return w;
        }

        private void Init(CreatureSystem system, WildRecord rec, CreatureSpecies sp, CreatureBody body)
        {
            sys = system;
            Record = rec;
            Species = sp;
            Body = body;
            home = rec.Position;
            inMine = AreaIds.IsMine(rec.areaId);
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            onWater = !inMine && layout != null && layout.IsWater(home);
            needsDiscovery = sp.Catch == CatchStyle.Discover;
            transform.rotation = Quaternion.Euler(0, (StableHash.Fnv1a64(rec.wildId) % 360), 0);
            actTimer = 6f + (StableHash.Fnv1a64(rec.wildId) % 80) / 10f;
            var day = Game.Instance != null && Game.Instance.Time != null ? Game.Instance.Time.Day : 1;
            float wa = (StableHash.Fnv1a64("wind" + day) % 360) * Mathf.Deg2Rad;
            windDir = new Vector3(Mathf.Cos(wa), 0, Mathf.Sin(wa));
            envColor = EnvironmentColor();

            switch (sp.Behavior)
            {
                case WildBehavior.Burrow: SetState(S.Hidden, 6f + Random.Range(0f, 5f)); break;
                case WildBehavior.Sleeper: SetState(S.Sleep, 9999f); break;
                case WildBehavior.Flyer: SetState(S.Fly, 2f); PickTarget(6f); break;
                default: SetState(S.Idle, 1f + Random.Range(0f, 2f)); break;
            }
            if (sp.No == 43) Body.SetEyesVisible(false); // イワネムは眠っていると岩そっくり
            if (needsDiscovery && sp.Behavior != WildBehavior.Burrow && sp.No != 43)
            {
                Body.SetCamouflage(envColor);
                Body.SetEyesVisible(false);
            }
        }

        private Color EnvironmentColor()
        {
            if (inMine) return new Color(0.42f, 0.4f, 0.38f);
            string f = Species.Features != null && Species.Features.Length > 0 ? Species.Features[0] : null;
            switch (f)
            {
                case "tree":
                case "fallen_log": return new Color(0.45f, 0.34f, 0.22f);
                case "grass":
                case "flower": return new Color(0.36f, 0.62f, 0.3f);
                case "rock": return new Color(0.56f, 0.55f, 0.53f);
                case "mud": return new Color(0.4f, 0.32f, 0.25f);
                case "window": return new Color(0.72f, 0.82f, 0.92f);
                case "box": return new Color(0.75f, 0.6f, 0.4f);
                case "roof": return new Color(0.62f, 0.36f, 0.3f);
            }
            if (System.Array.IndexOf(Species.Areas, World.AreaType.Coast) >= 0) return new Color(0.9f, 0.82f, 0.6f);
            return new Color(0.4f, 0.6f, 0.32f);
        }

        private void SetState(S s, float t)
        {
            state = s;
            timer = t;
            switch (s)
            {
                case S.Idle:
                case S.Rest:
                case S.Eat:
                case S.Mimic:
                    Body.CurrentPose = IsFloaty() ? CreatureBody.Pose.Float : CreatureBody.Pose.Idle;
                    break;
                case S.Move:
                case S.ToBait:
                    Body.CurrentPose = Species.Behavior == WildBehavior.Hop ? CreatureBody.Pose.Hop
                        : IsFlying() ? CreatureBody.Pose.Fly : IsFloaty() ? CreatureBody.Pose.Float : CreatureBody.Pose.Walk;
                    break;
                case S.Flee: Body.CurrentPose = IsFlying() ? CreatureBody.Pose.Fly : CreatureBody.Pose.Walk; break;
                case S.Fly: Body.CurrentPose = CreatureBody.Pose.Fly; break;
                case S.Roll: Body.CurrentPose = CreatureBody.Pose.Roll; break;
                case S.Hidden: Body.CurrentPose = CreatureBody.Pose.Hidden; break;
                case S.Sleep: Body.CurrentPose = CreatureBody.Pose.Sleep; break;
                case S.Act: break;
            }
        }

        private bool IsFlying() => Species.Behavior == WildBehavior.Flyer || Species.Behavior == WildBehavior.Swarm;
        private bool IsFloaty() => Species.Behavior == WildBehavior.Float || Species.Behavior == WildBehavior.Drift;

        private void PickTarget(float radius)
        {
            var c = Random.insideUnitCircle * radius;
            target = home + new Vector3(c.x, 0, c.y);
        }

        // =====================================================================
        // 捕獲の可否
        // =====================================================================

        /// <summary>捕まえやすさ（隙）0〜1。捕まえられない状態なら -1。</summary>
        public float CatchWindow()
        {
            if (needsDiscovery && !discovered) return -1f;
            bool wary = Time.time < waryUntil;
            if (Species.No == 47) return Mathf.Repeat(flashCycle, 3f) < 0.9f ? 1f : 0.2f;      // ムレピカ：光りそろった瞬間
            if (Species.No == 7) return CreatureBody.IsDark() && Mathf.Repeat(Time.time, 2.4f) < 1.6f ? 1f : 0.15f; // ヒカリムシ：光っている間
            switch (state)
            {
                case S.Hidden: return -1f;
                case S.Sleep: return Species.No == 43 ? -1f : 1f;
                case S.Flee:
                case S.Roll: return 0.05f;
                case S.Eat:
                case S.Mimic: return 1f;
                case S.Rest: return wary ? 0.35f : (Species.Behavior == WildBehavior.Burrow && Species.Catch == CatchStyle.Bait ? 0.5f : 1f);
                case S.Fly: return 0.12f;
                case S.Move:
                case S.ToBait:
                    if (Species.Catch == CatchStyle.Timing) return 0.15f;
                    return Species.Catch == CatchStyle.Normal ? 0.6f : 0.45f;
                default:
                    float w;
                    switch (Species.Catch)
                    {
                        case CatchStyle.Timing: w = 0.3f; break;
                        case CatchStyle.Bait: w = 0.4f; break;
                        case CatchStyle.Sneak: w = 0.85f; break;
                        default: w = 0.8f; break;
                    }
                    if (Species.Catch == CatchStyle.Lure && (transform.position - home).sqrMagnitude > 25f) w *= 0.6f;
                    return wary ? w * 0.5f : w;
            }
        }

        /// <summary>網を振った時、捕獲以前に反応する場合（擬態・地中・眠る岩）。true なら捕獲判定に進まない。</summary>
        public bool HandleNetBeforeCatch()
        {
            // 眠るイワネムは網で叩くと起きる（擬態の判定より先）
            if (state == S.Sleep && Species.No == 43)
            {
                Wake();
                UIManager.Toast(Loc.T("岩を叩いた。…何かが動いた？", "You tapped the rock... did something move?"));
                return true;
            }
            if (needsDiscovery && !discovered)
            {
                // 擬態していた生き物に網が当たって驚いて姿を現す
                Discover(false);
                Flee(transform.position - transform.forward, false);
                UIManager.Toast(Loc.T("葉っぱ…じゃない！何かが飛び出した！", "That wasn't a leaf! Something jumped out!"));
                return true;
            }
            if (state == S.Hidden)
            {
                UIManager.Toast(Loc.T("土の中に潜ってしまった…", "It ducked back into the ground..."));
                timer += 3f;
                return true;
            }
            return false;
        }

        public void OnNetMiss()
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            Flee(p != null ? p.Position : transform.position - transform.forward, false);
        }

        // =====================================================================
        // 反応
        // =====================================================================

        public void Discover(bool byPlayer)
        {
            if (discovered) return;
            discovered = true;
            Body.SetEyesVisible(true);
            if (Species.HasAbility(CreatureAbility.ColorShift))
            {
                // 色を変える生き物は、見つかると元の色に戻る
                Body.SetCamouflage(null);
            }
            else Body.SetCamouflage(null);
            Body.ShowEmote("!", 1.6f);
            if (byPlayer)
            {
                UIManager.Toast(Loc.T("見つけた！生き物が隠れていた", "Found it! A creature was hiding"));
                if (Species.Behavior == WildBehavior.Camouflage || Species.Behavior == WildBehavior.Stick)
                    sys.RecordBehavior(Species.Id, Species.SignatureBehavior);
            }
            if (state == S.Hidden && Species.Behavior == WildBehavior.Burrow) Surface(8f);
        }

        public void Wake()
        {
            if (state != S.Sleep) return;
            Body.SetEyesVisible(true);
            Body.ShowEmote("!?", 1.8f);
            if (Species.No == 43)
            {
                discovered = true;
                SetState(S.Rest, 14f); // 起きている間が狙い目
                if (Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "wake_up");
            }
            else SetState(S.Rest, 4f);
        }

        public void OnPlayerGesture(PlayerGesture g)
        {
            if (Species.Behavior != WildBehavior.Copycat || state == S.Flee) return;
            Body.PlayGesture(g, 1.8f);
            Body.ShowEmote("♪", 1.5f);
            SetState(S.Mimic, 5f);
            sys.RecordBehavior(Species.Id, "copy_gesture");
        }

        public void AttractToBait(BaitSpot b)
        {
            if (b == null || state == S.Flee) return;
            if (needsDiscovery && !discovered) Discover(false);
            bait = b;
            if (state == S.Hidden || state == S.Sleep) Body.SetEyesVisible(true);
            target = b.Position;
            speed = IsFlying() ? 2.4f : 1.7f;
            SetState(S.ToBait, 15f);
            Body.ShowEmote("?", 1.2f);
        }

        private void Surface(float seconds)
        {
            SetState(S.Rest, seconds);
            if (Near(ObserveRadius)) sys.RecordBehavior(Species.Id, Species.No == 2 ? "peek_from_hole" : Species.SignatureBehavior);
        }

        private void Flee(Vector3 from, bool dueToRunning)
        {
            if (state == S.Flee || state == S.Roll) return;
            bait = null;
            var away = transform.position - from;
            away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away.Normalize();
            Body.ShowEmote("!", 1.2f);
            Body.SetEyesVisible(true);
            if (dueToRunning && (Species.Catch == CatchStyle.Sneak || Species.Catch == CatchStyle.Bait) && Near(15f))
                sys.RecordBehavior(Species.Id, "flee_when_running", false);
            switch (Species.Behavior)
            {
                case WildBehavior.Burrow:
                    SetState(S.Hidden, 8f);
                    return;
                case WildBehavior.Roll:
                    target = transform.position + away * 7f;
                    speed = 6f;
                    SetState(S.Roll, 1.8f);
                    return;
                case WildBehavior.Stick:
                case WildBehavior.Sleeper:
                    target = transform.position + away * 2.5f;
                    speed = 3f;
                    SetState(S.Flee, 1.2f);
                    return;
                case WildBehavior.Float:
                case WildBehavior.Drift:
                    target = transform.position + away * 3.5f;
                    speed = 2.2f;
                    SetState(S.Flee, 2f);
                    return;
                default:
                    target = transform.position + away * 8f;
                    speed = IsFlying() ? 6f : 5.2f;
                    SetState(S.Flee, 2.2f);
                    return;
            }
        }

        private bool Near(float r)
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            return p != null && (p.Position - transform.position).sqrMagnitude < r * r;
        }

        // =====================================================================
        // 毎フレーム
        // =====================================================================

        private void Update()
        {
            var game = Game.Instance;
            if (game == null || sys == null || game.State != GameState.Playing) return;
            float dt = Time.deltaTime;
            var player = game.LocalPlayer;
            var controller = game.PlayerSys != null ? game.PlayerSys.LocalController : null;
            bool playerMoving = controller != null && controller.IsMoving;
            bool running = playerMoving && GameInput.Run;
            float d = player != null ? Vector3.Distance(player.Position, transform.position) : 999f;
            flashCycle += dt;

            CheckAlert(player, d, running, playerMoving);
            CheckDiscovery(d, playerMoving, dt);
            CheckSeen(player, d, dt);

            timer -= dt;
            switch (state)
            {
                case S.Idle: UpdateIdle(); break;
                case S.Move:
                    if (MoveTo(target, speed, dt) || timer <= 0f) AfterMove();
                    break;
                case S.Fly:
                    if (MoveTo(target, speed, dt) || timer <= 0f)
                    {
                        // 止まる（花・枝・灯り）＝タイミングの狙い目
                        SetState(S.Rest, 3f + Random.Range(0f, 1.5f));
                        if (Species.No == 6 && Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "land_on_flower", false);
                    }
                    break;
                case S.Rest:
                    if (timer <= 0f) ResumeNormal();
                    break;
                case S.Flee:
                    if (MoveTo(target, speed, dt) || timer <= 0f)
                    {
                        home = transform.position;
                        waryUntil = Time.time + 6f;
                        SetState(S.Rest, 3f);
                    }
                    break;
                case S.Roll:
                    MoveTo(target, speed, dt);
                    if (timer <= 0f)
                    {
                        home = transform.position;
                        waryUntil = Time.time + 1.5f;
                        SetState(S.Rest, 3.5f); // 転がり終わって止まった時が狙い目
                        if (Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "roll_when_startled");
                        if (Species.No == 13 && Random.value < 0.3f && Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "crush_nuts");
                    }
                    break;
                case S.Hidden:
                    if (timer <= 0f)
                    {
                        // 地中から顔を出す／泡から顔を出す
                        if (Species.Behavior == WildBehavior.Burrow && needsDiscovery && !discovered) { timer = 5f; break; }
                        Surface(Species.No == 21 ? 2.5f : 3f);
                    }
                    break;
                case S.Sleep:
                    break;
                case S.Eat:
                    if (timer <= 0f)
                    {
                        if (bait != null) sys.ConsumeBait(bait);
                        bait = null;
                        SetState(S.Rest, 2.5f);
                    }
                    break;
                case S.ToBait:
                    if (bait == null || bait.Go == null) { ResumeNormal(); break; }
                    if (MoveTo(bait.Position, speed, dt) || timer <= 0f)
                    {
                        SetState(S.Eat, 8f);
                        Body.PlayAct(1.5f);
                        Body.ShowEmote("♪", 2f);
                        if (Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "come_to_bait");
                    }
                    break;
                case S.Mimic:
                    if (timer <= 0f) ResumeNormal();
                    break;
                case S.Act:
                    if (timer <= 0f) ResumeNormal();
                    break;
            }

            if (state != S.Flee && state != S.Roll && state != S.Act && state != S.Hidden && state != S.Sleep)
            {
                actTimer -= dt;
                if (actTimer <= 0f)
                {
                    actTimer = 10f + Random.Range(0f, 8f);
                    if (!needsDiscovery || discovered || Species.Behavior == WildBehavior.Camouflage) DoSignature(player, d, running);
                }
            }
            else if (state == S.Sleep)
            {
                actTimer -= dt;
                if (actTimer <= 0f)
                {
                    actTimer = 8f;
                    if (d < 8f && !running && (Species.No != 43 || discovered)) sys.RecordBehavior(Species.Id, Species.SignatureBehavior);
                    if (Species.No == 43 && d < 6f && discovered && Random.value < 0.3f) sys.RecordBehavior(Species.Id, "heavy_press");
                }
            }
        }

        private void CheckAlert(PlayerState player, float d, bool running, bool moving)
        {
            if (player == null || state == S.Flee || state == S.Roll || state == S.Mimic) return;
            if (needsDiscovery && !discovered) return;
            float mult = sys.HasTechnique(CreatureSystem.TechSneak) ? 0.7f : 1f;
            mult *= 1f - 0.03f * (sys.CatchLevel - 1);
            float runR, walkR = 0f;
            switch (Species.Catch)
            {
                case CatchStyle.Sneak: runR = 11f; break;
                case CatchStyle.Normal: runR = 4.5f; break;
                case CatchStyle.Timing: runR = 6f; walkR = state == S.Move || state == S.Fly ? 1.2f : 0f; break;
                case CatchStyle.Bait: runR = 6.5f; walkR = state == S.Eat ? 0f : 3f; break;
                case CatchStyle.Lure: runR = 5f; break;
                default: runR = 4f; break;
            }
            if (state == S.Eat) runR *= 0.5f;
            if (state == S.Hidden)
            {
                if (running && d < 4f) timer = Mathf.Max(timer, 5f);
                return;
            }
            if (state == S.Sleep)
            {
                if (Species.No == 43) return; // イワネムは岩を叩かないと起きない
                if (running && d < 8f * mult) { Wake(); Flee(player.Position, true); }
                return;
            }
            if ((running && d < runR * mult) || (moving && !running && walkR > 0f && d < walkR * mult))
                Flee(player.Position, running);
        }

        private void CheckDiscovery(float d, bool moving, float dt)
        {
            if (!needsDiscovery || discovered) return;
            bool tech = sys.HasTechnique(CreatureSystem.TechObserve);
            if (tech && d < 2.2f) { Discover(true); return; }
            if (d < 3.5f && !moving)
            {
                stillTimer += dt;
                if (stillTimer >= (tech ? 1.2f : 2.5f)) Discover(true);
            }
            else stillTimer = 0f;
            // 音を鳴らす生き物は、隠れていても♪で居場所が分かる
            if (Species.MakesSound && Mathf.Repeat(Time.time + home.x, 5f) < dt) Body.ShowEmote("♪", 1.5f);
            // 擬態している生き物は、ときどき小さく動く（よく見ると分かる）
            if (Species.Behavior == WildBehavior.Camouflage && Mathf.Repeat(Time.time + home.z, 7f) < dt) Body.PlayAct(0.25f);
        }

        private void CheckSeen(PlayerState player, float d, float dt)
        {
            if (seenRecorded || player == null) return;
            seenTimer -= dt;
            if (seenTimer > 0f) return;
            seenTimer = 0.5f;
            if (d > SeenRadius) return;
            if (needsDiscovery && !discovered) return;
            if (state == S.Hidden) return;
            if (Species.GlowsOnlyAtNight && !CreatureBody.IsDark()) return;
            seenRecorded = true;
            sys.RecordSeen(Species.Id, transform.position, Record.areaId);
        }

        private void UpdateIdle()
        {
            if (timer > 0f) return;
            switch (Species.Behavior)
            {
                case WildBehavior.Camouflage:
                case WildBehavior.Stick:
                    if (Species.No == 28)
                    {
                        // シオリボ：流れが弱まったときに少し動く
                        PickTarget(1.5f);
                        speed = 0.8f;
                        SetState(S.Move, 2.5f);
                        return;
                    }
                    timer = 3f;
                    return;
                case WildBehavior.Hop:
                    PickTarget(WanderRadius + 1f);
                    speed = 2.4f;
                    SetState(S.Move, 2.4f);
                    return;
                case WildBehavior.Flyer:
                    PickTarget(6f);
                    speed = 2.2f;
                    SetState(S.Fly, 3f + Random.Range(0f, 2f));
                    return;
                case WildBehavior.Float:
                    if (Species.No == 21)
                    {
                        // アワリン：泡の中に隠れる
                        SetState(S.Hidden, 4f);
                        Body.ShowEmote("o", 2f);
                        return;
                    }
                    PickTarget(2.5f);
                    speed = 0.6f;
                    SetState(S.Move, 6f);
                    return;
                case WildBehavior.Drift:
                    {
                        var ws = Game.Instance.Service<IWeatherService>();
                        float k = ws != null ? (ws.Current.wind == WindLevel.Strong ? 1.8f : ws.Current.wind == WindLevel.Breeze ? 1.2f : 0.7f) : 1f;
                        target = transform.position + windDir * 5f * k;
                        if ((target - home).sqrMagnitude > 15f * 15f) target = home;
                        speed = 1f * k;
                        SetState(S.Move, 5f);
                        return;
                    }
                case WildBehavior.Swarm:
                    PickTarget(1.8f);
                    speed = 1.2f;
                    SetState(S.Move, 2.5f);
                    return;
                case WildBehavior.Burrow:
                    SetState(S.Hidden, 8f + Random.Range(0f, 6f));
                    return;
                case WildBehavior.Sleeper:
                    SetState(S.Sleep, 9999f);
                    if (Species.No == 43) Body.SetEyesVisible(false);
                    return;
                default:
                    PickTarget(WanderRadius);
                    speed = Species.No == 27 ? 0.4f : 1.1f;
                    SetState(S.Move, 5f);
                    return;
            }
        }

        private void AfterMove()
        {
            switch (Species.Behavior)
            {
                case WildBehavior.Hop:
                    SetState(S.Rest, 3.2f); // 何度か跳ねたあとに休む
                    if (Near(ObserveRadius)) sys.RecordBehavior(Species.Id, "hop_then_rest");
                    break;
                case WildBehavior.Drift:
                    SetState(S.Rest, 3f); // ふわりと着地
                    break;
                case WildBehavior.Float:
                    SetState(S.Rest, 2.5f);
                    break;
                default:
                    SetState(S.Idle, 2f + Random.Range(0f, 3f));
                    break;
            }
        }

        private void ResumeNormal()
        {
            switch (Species.Behavior)
            {
                case WildBehavior.Burrow:
                    SetState(S.Hidden, 8f + Random.Range(0f, 6f));
                    break;
                case WildBehavior.Sleeper:
                    SetState(Species.No == 43 ? S.Sleep : S.Idle, Species.No == 43 ? 9999f : 20f);
                    if (Species.No == 43) Body.SetEyesVisible(false);
                    break;
                default:
                    SetState(S.Idle, 1.5f + Random.Range(0f, 2f));
                    break;
            }
        }

        /// <summary>特徴的な行動（観察すると図鑑に記録される）。</summary>
        private void DoSignature(PlayerState player, float d, bool running)
        {
            Body.PlayAct(1.8f);
            PlaySignatureFx();
            if (d < ObserveRadius && !running && !string.IsNullOrEmpty(Species.SignatureBehavior))
                sys.RecordBehavior(Species.Id, Species.SignatureBehavior);
            if (Species.ExtraBehaviors.Length > 0 && d < ObserveRadius && !running && Random.value < 0.3f)
            {
                var extra = Species.ExtraBehaviors[Random.Range(0, Species.ExtraBehaviors.Length)];
                if (extra != "come_to_bait" && extra != "flee_when_running" && extra != "wake_up" && extra != "peek_from_hole")
                {
                    if (extra == "play_in_rain")
                    {
                        var ws = Game.Instance.Service<IWeatherService>();
                        if (ws == null || !ws.Current.IsRaining) return;
                    }
                    sys.RecordBehavior(Species.Id, extra);
                }
            }
        }

        private void PlaySignatureFx()
        {
            var p = transform.position + Vector3.up * 0.5f;
            switch (Species.SignatureBehavior)
            {
                case "spray_water":
                case "umbrella_leaf": CreatureFx.Puff(p, new Color(0.5f, 0.8f, 1f), 7); break;
                case "skip_on_water": CreatureFx.Puff(p - Vector3.up * 0.4f, new Color(0.7f, 0.9f, 1f), 6); break;
                case "dig_soil":
                case "eat_soil":
                case "carry_moss": CreatureFx.Puff(p - Vector3.up * 0.4f, new Color(0.45f, 0.33f, 0.22f), 6); break;
                case "peck_seeds":
                case "gather_seeds":
                case "collect_pebbles": CreatureFx.Puff(p, new Color(0.9f, 0.8f, 0.4f), 4, 0.8f, 0.05f); break;
                case "visit_flowers": CreatureFx.Puff(p, new Color(1f, 0.85f, 0.4f), 6, 0.6f, 0.04f, true); break;
                case "glow_at_night":
                case "gather_at_light":
                case "gem_shell_shine":
                case "sync_flash":
                case "dew_sparkle":
                case "dream_float":
                case "ring_spin": CreatureFx.Puff(p, Species.Accent, 5, 0.5f, 0.05f, true); break;
                case "warm_body":
                case "sleep_in_sun": CreatureFx.Puff(p, new Color(1f, 0.6f, 0.35f), 4, 0.5f); break;
                case "dew_chill":
                case "shadow_chill": CreatureFx.Puff(p, new Color(0.75f, 0.95f, 1f), 5, 0.4f, 0.05f); break;
                case "sound_shell":
                case "tap_rhythm":
                case "echo_call": Body.ShowEmote("♪♪", 2f); break;
                case "make_bubbles":
                case "mud_bubbles": CreatureFx.Puff(p, new Color(1f, 1f, 1f, 0.7f), 8, 0.6f, 0.1f); break;
                case "spore_puff": CreatureFx.Puff(p + Vector3.up * 0.1f, new Color(0.95f, 0.9f, 0.8f), 8, 0.5f, 0.06f); break;
                case "dust_puff": CreatureFx.Puff(p, new Color(0.8f, 0.78f, 0.7f), 8, 0.6f); break;
                case "roll_sand": CreatureFx.Puff(p - Vector3.up * 0.4f, new Color(0.95f, 0.85f, 0.6f), 5); break;
                case "change_color":
                case "match_color":
                    Body.SetCamouflage(Color.HSVToRGB(Random.value, 0.5f, 0.9f));
                    Invoke(nameof(RestoreColor), 1.8f);
                    break;
                case "copy_gesture": Body.ShowEmote("?", 1.5f); break;
                case "stack_stones": CreatureFx.Puff(p, new Color(0.7f, 0.72f, 0.78f), 3, 0.3f, 0.08f); break;
                default: Body.ShowEmote("～", 1.5f); break;
            }
        }

        private void RestoreColor()
        {
            if (Body == null) return;
            if (needsDiscovery && !discovered) Body.SetCamouflage(envColor);
            else Body.SetCamouflage(null);
        }

        private bool MoveTo(Vector3 dest, float spd, float dt)
        {
            var pos = transform.position;
            var flat = new Vector3(dest.x - pos.x, 0, dest.z - pos.z);
            float dist = flat.magnitude;
            if (dist < 0.15f) return true;
            var step = flat / dist * Mathf.Min(dist, spd * dt);
            var np = pos + step;
            np.y = GroundY(np);
            transform.position = np;
            var face = Quaternion.LookRotation(flat, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, face, 540f * dt);
            return dist < 0.2f;
        }

        private float GroundY(Vector3 p)
        {
            if (inMine) return home.y;
            var layout = Game.Instance.Layout;
            if (layout == null) return p.y;
            float h = layout.HeightAt(p.x, p.z);
            if (onWater && layout.IsWater(p)) return Mathf.Max(h, home.y);
            return h;
        }

        // =====================================================================
        // IInteractable（[E]：よく見る・観察する）
        // =====================================================================

        public string GetInteractionPrompt(PlayerState player)
        {
            if (state == S.Hidden && Species.Behavior == WildBehavior.Burrow) return Loc.T("土の盛り上がりを調べる", "Inspect the mound");
            if (needsDiscovery && !discovered) return Loc.T("よく見る", "Look closely");
            if (state == S.Sleep && Species.No == 43) return Loc.T("岩を調べる", "Inspect the rock");
            return Loc.Format("観察する：{0}", "Observe: {0}", KnownName());
        }

        private string KnownName()
        {
            var e = sys.GetEntry(Species.Id, false);
            return e != null && e.Seen ? sys.GetSpeciesName(Species.Id) : Loc.T("見たことのない生き物", "an unknown creature");
        }

        public bool CanInteract(PlayerState player) => true;

        public void Interact(PlayerState player)
        {
            if (state == S.Hidden && Species.Behavior == WildBehavior.Burrow)
            {
                UIManager.Toast(Loc.T("土がもぞもぞ動いている…", "The soil is wriggling..."));
                if (needsDiscovery) stillTimer += 1.5f;
                return;
            }
            if (needsDiscovery && !discovered)
            {
                Discover(true);
                return;
            }
            if (state == S.Sleep && Species.No == 43)
            {
                UIManager.Toast(Loc.T("岩のような生き物が眠っている", "A rock-like creature is sleeping"));
                sys.RecordBehavior(Species.Id, "rock_sleep");
                return;
            }
            // じっと観察すると、その生き物らしい行動を見せてくれる
            if (!seenRecorded)
            {
                seenRecorded = true;
                sys.RecordSeen(Species.Id, transform.position, Record.areaId);
            }
            Body.PlayAct(1.8f);
            PlaySignatureFx();
            if (state != S.Sleep && state != S.Eat) SetState(S.Act, 1.8f);
            bool isNew = sys.RecordBehavior(Species.Id, Species.SignatureBehavior);
            if (!isNew) UIManager.Toast(Loc.Format("{0}をじっと観察した", "You watched {0} closely", KnownName()), 2f);
        }
    }
}
