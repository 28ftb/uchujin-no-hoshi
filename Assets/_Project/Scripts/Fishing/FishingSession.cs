using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Fishing
{
    /// <summary>
    /// ローカルプレイヤーの1回の釣り（釣り仕様 §2）：
    /// 狙う（キャストのタイミング）→ 浮きが着水 → 待つ（誘い）→ アタリ（合わせ）→ やり取り（ミニゲーム §3）→ 釣果（§8）。
    /// 入力は GameInput を直接読む（釣り中は PlayerState.LockInput で移動・他の操作を止める）。
    /// </summary>
    public sealed class FishingSession
    {
        public enum Phase { Aiming, Flying, Waiting, Bite, Fighting, Result, Done }

        public const string LockToken = "fishing";

        public Phase State { get; private set; } = Phase.Aiming;
        public PlayerState Player { get; }
        public SpotInfo Spot { get; private set; }
        public string BaitId { get; }
        public FishFight Fight { get; private set; }
        public FishSpecies Hooked { get; private set; }
        public float CastMeter { get; private set; }
        public float SweetCenter { get; private set; } = 0.65f;
        public float SweetHalfWidth { get; private set; }
        public bool PrecisionHit { get; private set; }
        public float ReactionLeft { get; private set; }
        public float ReactionWindow { get; private set; }
        public int Twitches { get; private set; }
        public CatchResult LastResult { get; private set; }
        public float ResultTimer { get; private set; }
        public string StatusMessage { get; private set; }

        private readonly FishingSystem system;
        private readonly SeededRandom rng;
        private readonly int startFrame;
        private readonly Vector3 startPosition;
        private readonly string startArea;
        private readonly int rodTier;
        private bool meterUp = true;
        private float waitLeft;
        private float nibbleTimer;
        private float flyT;
        private Vector3 bobberTarget;
        private Vector3 flyFrom;
        private GameObject bobber, line, rod, heldFish;
        private float bobberDip;
        private float sizePercentile;
        private bool bigDay;

        public sealed class CatchResult
        {
            public FishSpecies Species;
            public float Size;
            public int Quality;
            public bool FirstTime;
            public bool NewRecord;
            public bool LevelUp;
            public int NewLevel;
            public int Xp;
        }

        public FishingSession(FishingSystem system, PlayerState player, string baitId, SeededRandom rng)
        {
            this.system = system;
            Player = player;
            BaitId = baitId;
            this.rng = rng;
            startFrame = UnityEngine.Time.frameCount;
            startPosition = player.Position;
            startArea = player.CurrentAreaId;
            rodTier = Mathf.Max(1, player.Tools.GetLevel(ToolType.FishingRod));
            var prof = system.GetProfile(player.PlayerId);
            SweetHalfWidth = prof.Knows(FishingTechnique.PrecisionCast) ? FishingTuning.SweetZoneTechnique : FishingTuning.SweetZoneBase;
            SweetCenter = system.EstimateSweetSpot(player);
            player.LockInput(LockToken);
            if (player.Avatar != null)
            {
                var holder = player.Avatar.Find("Visual/ToolHolder");
                rod = FishVisuals.BuildRod(holder != null ? holder : player.Avatar, rodTier);
                if (holder == null) rod.transform.localPosition = new Vector3(0.35f, 0.9f, 0.15f);
            }
        }

        public bool IsActive => State != Phase.Done;

        private Vector3 RodTip
        {
            get
            {
                if (Player.Avatar == null) return Player.Position + Vector3.up * 1.6f;
                return Player.Avatar.position + Vector3.up * 1.7f + Player.Avatar.forward * 1.1f;
            }
        }

        private static bool ConfirmPressed => GameInput.UseTool || GameInput.KeyDown(KeyCode.Space);
        private static bool CancelPressed => GameInput.KeyDown(KeyCode.Q) || GameInput.KeyDown(KeyCode.Backspace);

        // ---------------- 毎フレーム ----------------

        public void Update(float dt)
        {
            if (State == Phase.Done) return;
            // ポーズ・タブレット等のモーダル中は止める
            if (UIManager.AnyModalOpen) return;
            // 移動・エリア移動したら中止（エリアが変わった・テレポートした）
            if (Player.CurrentAreaId != startArea || (Player.Position - startPosition).sqrMagnitude > 9f)
            {
                End(Loc.T("釣りをやめました", "Stopped fishing"));
                return;
            }
            bool fresh = UnityEngine.Time.frameCount <= startFrame + 1;

            switch (State)
            {
                case Phase.Aiming: UpdateAiming(dt, fresh); break;
                case Phase.Flying: UpdateFlying(dt); break;
                case Phase.Waiting: UpdateWaiting(dt); break;
                case Phase.Bite: UpdateBite(dt); break;
                case Phase.Fighting: UpdateFighting(dt); break;
                case Phase.Result: UpdateResult(dt); break;
            }
            UpdateVisuals(dt);
        }

        private void UpdateAiming(float dt, bool fresh)
        {
            CastMeter += (meterUp ? 1f : -1f) * FishingTuning.CastMeterSpeed * dt;
            if (CastMeter >= 1f) { CastMeter = 1f; meterUp = false; }
            if (CastMeter <= 0f) { CastMeter = 0f; meterUp = true; }
            if (CancelPressed)
            {
                End(null);
                return;
            }
            if (fresh || !ConfirmPressed) return;

            float dist = Mathf.Lerp(FishingTuning.CastMinDistance, FishingTuning.CastMaxDistance, CastMeter);
            var fwd = Player.Forward;
            fwd.y = 0;
            fwd.Normalize();
            var target = Player.Position + fwd * dist;
            var spot = system.Spawn.ResolveWaterAt(target, system.Layout);
            if (spot == null)
            {
                End(Loc.T("水面に届かなかった…（水の方を向いて、強さを調整しよう）", "Missed the water... (face the water and adjust the power)"));
                return;
            }
            Spot = spot;
            PrecisionHit = Mathf.Abs(CastMeter - SweetCenter) <= SweetHalfWidth;
            bobberTarget = new Vector3(target.x, spot.SurfaceY + 0.02f, target.z);
            flyFrom = RodTip;
            flyT = 0f;
            State = Phase.Flying;
            if (Player.Avatar != null)
            {
                bobber = FishVisuals.BuildBobber(Game.Instance.SceneRoot);
                bobber.transform.position = flyFrom;
                line = FishVisuals.BuildLine(Game.Instance.SceneRoot);
            }
        }

        private void UpdateFlying(float dt)
        {
            flyT += dt / 0.55f;
            if (bobber != null)
            {
                var p = Vector3.Lerp(flyFrom, bobberTarget, flyT);
                p.y += Mathf.Sin(Mathf.Clamp01(flyT) * Mathf.PI) * 2.2f;
                bobber.transform.position = p;
            }
            if (flyT < 1f) return;
            FishingTransientFx.Splash(bobberTarget, 5, new Color(0.7f, 0.85f, 1f, 0.8f));
            float wait = rng.Range(FishingTuning.WaitMin, FishingTuning.WaitMax);
            if (!string.IsNullOrEmpty(BaitId)) wait *= FishingTuning.BaitWaitMultiplier;
            if (PrecisionHit) wait *= 0.6f;
            waitLeft = wait;
            nibbleTimer = rng.Range(0.8f, 2f);
            State = Phase.Waiting;
            StatusMessage = PrecisionHit ? Loc.T("狙いどおりの場所に落ちた！", "Right on target!") : null;
        }

        private void UpdateWaiting(float dt)
        {
            if (CancelPressed)
            {
                End(null);
                return;
            }
            if (GameInput.KeyDown(KeyCode.Space) && Twitches < FishingTuning.MaxTwitches)
            {
                Twitches++;
                bool tech = system.GetProfile(Player.PlayerId).Knows(FishingTechnique.Lure);
                waitLeft *= 1f - (tech ? FishingTuning.LureTwitchCutTechnique : FishingTuning.LureTwitchCut);
                bobberDip = 0.12f;
            }
            waitLeft -= dt;
            nibbleTimer -= dt;
            if (nibbleTimer <= 0f && waitLeft > 1f)
            {
                // 小さなつつき（フェイント）
                bobberDip = 0.06f;
                nibbleTimer = rng.Range(0.9f, 2.4f);
            }
            if (waitLeft > 0f) return;

            Hooked = system.PickFish(Player, Spot, BaitId, PrecisionHit, rng);
            if (Hooked == null)
            {
                End(Loc.T("ここには魚の気配がない…（時間や天気を変えてみよう）", "No sign of fish here... (try another time or weather)"));
                return;
            }
            int level = system.GetProfile(Player.PlayerId).level;
            ReactionWindow = FishingTuning.ReactionWindowBase + FishingTuning.LevelReactionBonus * (level - 1) + FishingTuning.RodReactionBonus * (rodTier - 1);
            ReactionLeft = ReactionWindow;
            State = Phase.Bite;
            bobberDip = 0.35f;
            if (bobber != null) FishingTransientFx.Splash(bobber.transform.position, 3, new Color(0.7f, 0.85f, 1f, 0.8f));
        }

        private void UpdateBite(float dt)
        {
            ReactionLeft -= dt;
            if (ConfirmPressed)
            {
                StartFight();
                return;
            }
            if (ReactionLeft <= 0f)
            {
                ConsumeBait(0.5f);
                End(Loc.T("合わせが遅れて、逃げられた…", "Too slow - it got away..."));
            }
        }

        private void StartFight()
        {
            ConsumeBait(1f);
            var prof = system.GetProfile(Player.PlayerId);
            bigDay = system.Spawn.IsBigFishDay(system.CurrentDay, Spot.Id);
            float bias = 1.7f - 0.05f * (prof.level - 1) - FishingTuning.RodSizeBonus * (rodTier - 1) - (bigDay ? FishingTuning.BigDaySizeBonus : 0f);
            sizePercentile = Mathf.Pow(rng.NextFloat(), Mathf.Max(0.5f, bias));
            Fight = new FishFight(Hooked, sizePercentile, rodTier, prof, rng.Fork("fight"));
            State = Phase.Fighting;
        }

        private void UpdateFighting(float dt)
        {
            if (CancelPressed)
            {
                End(Loc.T("糸を切って逃がした", "Cut the line and let it go"));
                return;
            }
            bool reel = GameInput.KeyHeld(KeyCode.Space) || GameInput.MouseButtonHeld(0) || GameInput.KeyHeld(KeyCode.F);
            bool slack = GameInput.KeyHeld(KeyCode.S) || GameInput.KeyHeld(KeyCode.DownArrow);
            float tilt = 0f;
            if (GameInput.KeyHeld(KeyCode.A) || GameInput.KeyHeld(KeyCode.LeftArrow)) tilt -= 1f;
            if (GameInput.KeyHeld(KeyCode.D) || GameInput.KeyHeld(KeyCode.RightArrow)) tilt += 1f;
            bool raise = GameInput.KeyHeld(KeyCode.W) || GameInput.KeyHeld(KeyCode.UpArrow);
            var prevAction = Fight.Action;
            Fight.Update(dt, reel, slack, tilt, raise);
            if (Fight.Action == FishFight.ActionKind.Jump && prevAction != FishFight.ActionKind.Jump && bobber != null)
                FishingTransientFx.Splash(bobber.transform.position, 6, new Color(0.75f, 0.9f, 1f, 0.8f));

            switch (Fight.Result)
            {
                case FishFight.Outcome.Landed:
                    Land();
                    break;
                case FishFight.Outcome.LineBroke:
                    End(Loc.T("糸が切れてしまった！（張りすぎに注意。緩めるのも大事）", "The line snapped! (Watch the tension - easing off matters too)"));
                    break;
                case FishFight.Outcome.Escaped:
                    End(Loc.T("針が外れて逃げられた…（緩めすぎ・離されすぎに注意）", "It slipped the hook... (don't give too much slack)"));
                    break;
            }
        }

        private void Land()
        {
            float size = Mathf.Lerp(Hooked.SizeMin, Hooked.SizeMax, sizePercentile);
            if (bigDay) size *= 1.08f;
            size = Mathf.Round(size * 10f) / 10f;
            int quality = Fight.ComputeQuality(sizePercentile);
            LastResult = system.OnFishLanded(Player, Hooked, size, quality, Spot);
            ResultTimer = 0f;
            State = Phase.Result;
            if (bobber != null) Object.Destroy(bobber);
            if (line != null) Object.Destroy(line);
            bobber = null;
            line = null;
            if (Player.Avatar != null)
            {
                float len = Mathf.Clamp(size / 100f, 0.25f, 1.6f);
                heldFish = FishVisuals.BuildFish(Hooked, Game.Instance.SceneRoot, Vector3.zero, len);
                heldFish.transform.position = Player.Avatar.position + Vector3.up * (2.2f + len * 0.3f);
                heldFish.transform.rotation = Quaternion.Euler(0, Player.Avatar.eulerAngles.y + 90f, 0);
                FishingTransientFx.Splash(Player.Avatar.position + Player.Avatar.forward * 1.2f + Vector3.up * 0.3f, 8, new Color(0.7f, 0.85f, 1f, 0.8f));
            }
        }

        private void UpdateResult(float dt)
        {
            ResultTimer += dt;
            if (heldFish != null) heldFish.transform.Rotate(0, 40f * dt, 0, Space.World);
            if (ResultTimer > 0.6f && (ConfirmPressed || CancelPressed || GameInput.Interact)) End(null);
            else if (ResultTimer > 6f) End(null);
        }

        private void ConsumeBait(float chanceScale)
        {
            if (string.IsNullOrEmpty(BaitId)) return;
            float chance = FishCatalog.BaitConsumeChance(BaitId) * chanceScale;
            if (rng.Chance(chance)) Player.Inventory.Remove(BaitId, 1, 0, "fishing_bait");
        }

        // ---------------- 表示 ----------------

        private void UpdateVisuals(float dt)
        {
            bobberDip = Mathf.MoveTowards(bobberDip, 0f, dt * 0.5f);
            if (bobber != null && State != Phase.Flying)
            {
                Vector3 p = bobberTarget;
                if (State == Phase.Fighting && Fight != null)
                {
                    // 魚が近づくほど浮きが手前へ。左右の引きで横へずれる
                    float k = Mathf.Clamp01(Fight.Distance / Mathf.Max(1f, Fight.StartDistance));
                    var near = Player.Position + Player.Forward * 1.5f;
                    near.y = bobberTarget.y;
                    p = Vector3.Lerp(near, bobberTarget, k);
                    var side = Vector3.Cross(Vector3.up, Player.Forward).normalized;
                    p += side * Fight.FishDir * 0.6f * Mathf.Sin(UnityEngine.Time.time * 3f);
                    if (Fight.Action == FishFight.ActionKind.Dive || Fight.Action == FishFight.ActionKind.Harden) p.y -= 0.15f;
                    if (Fight.Action == FishFight.ActionKind.Jump) p.y += 0.4f * Mathf.Abs(Mathf.Sin(UnityEngine.Time.time * 8f));
                }
                p.y += Mathf.Sin(UnityEngine.Time.time * 2.5f) * 0.03f - bobberDip;
                bobber.transform.position = p;
            }
            if (line != null && bobber != null) FishVisuals.StretchLine(line, RodTip, bobber.transform.position);
        }

        public void End(string message)
        {
            if (State == Phase.Done) return;
            State = Phase.Done;
            if (!string.IsNullOrEmpty(message)) UIManager.Toast(message);
            if (bobber != null) Object.Destroy(bobber);
            if (line != null) Object.Destroy(line);
            if (rod != null) Object.Destroy(rod);
            if (heldFish != null)
            {
                var fx = heldFish.AddComponent<FishingTransientFx>();
                fx.life = 0.4f;
                fx.velocity = Vector3.up * 2f;
            }
            Player.UnlockInput(LockToken);
        }

        // ---------------- IMGUI 表示（釣り仕様 §3：テンション・リール） ----------------

        public void Draw()
        {
            if (State == Phase.Done) return;
            float vw = UIStyles.VirtualWidth, vh = UIStyles.VirtualHeight;
            switch (State)
            {
                case Phase.Aiming:
                {
                    var r = new Rect((vw - 680) * 0.5f, vh - 300, 680, 130);
                    GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
                    GUI.Label(new Rect(r.x + 20, r.y + 10, 640, 30), Loc.T("投げる強さ：タイミングよく [左クリック / F / Space]　[Q] やめる",
                        "Cast power: press [LMB / F / Space] at the right moment   [Q] cancel"), UIStyles.LabelBold);
                    var bar = new Rect(r.x + 20, r.y + 54, 640, 34);
                    UIStyles.FillRect(bar, new Color(0, 0, 0, 0.5f));
                    var zone = new Rect(bar.x + bar.width * (SweetCenter - SweetHalfWidth), bar.y, bar.width * SweetHalfWidth * 2f, bar.height);
                    UIStyles.FillRect(zone, new Color(0.4f, 0.9f, 0.5f, 0.55f));
                    UIStyles.FillRect(new Rect(bar.x + bar.width * CastMeter - 4, bar.y - 6, 8, bar.height + 12), UIStyles.AccentWarm);
                    GUI.Label(new Rect(r.x + 20, r.y + 94, 640, 26), Loc.T("緑の範囲：魚影のあたり（狙いどおりだと食いつきが早い）", "Green: around the fish shadows (on target = faster bites)"), UIStyles.LabelDim);
                    break;
                }
                case Phase.Flying:
                case Phase.Waiting:
                {
                    var r = new Rect((vw - 640) * 0.5f, vh - 250, 640, 82);
                    GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
                    string bait = string.IsNullOrEmpty(BaitId) ? Loc.T("餌なし", "no bait") : ItemRegistry.NameOf(BaitId);
                    GUI.Label(new Rect(r.x + 18, r.y + 8, 610, 30), Loc.Format("アタリを待っています…（{0}）", "Waiting for a bite... ({0})", bait), UIStyles.LabelBold);
                    GUI.Label(new Rect(r.x + 18, r.y + 42, 610, 28), Loc.Format("[Space] 誘う（{0}/{1}）　[Q] 糸を上げる", "[Space] twitch ({0}/{1})   [Q] reel up",
                        Twitches, FishingTuning.MaxTwitches) + (Spot != null ? "　" + Spot.Label() : ""), UIStyles.LabelDim);
                    if (!string.IsNullOrEmpty(StatusMessage))
                        GUI.Label(new Rect(r.x + 18, r.y - 30, 610, 28), StatusMessage, UIStyles.LabelBold);
                    break;
                }
                case Phase.Bite:
                {
                    var r = new Rect((vw - 520) * 0.5f, vh * 0.32f, 520, 150);
                    GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
                    var big = new GUIStyle(UIStyles.LabelTitle) { fontSize = 64, alignment = TextAnchor.MiddleCenter };
                    big.normal.textColor = UIStyles.AccentWarm;
                    GUI.Label(new Rect(r.x, r.y + 4, r.width, 80), "！", big);
                    GUI.Label(new Rect(r.x, r.y + 80, r.width, 30), Loc.T("今だ！ [左クリック / F / Space]", "Now! [LMB / F / Space]"), UIStyles.LabelCenter);
                    UIStyles.ProgressBar(new Rect(r.x + 40, r.y + 118, r.width - 80, 16), ReactionLeft / Mathf.Max(0.01f, ReactionWindow), UIStyles.AccentWarm);
                    break;
                }
                case Phase.Fighting:
                    DrawFight(vw, vh);
                    break;
                case Phase.Result:
                    DrawResult(vw, vh);
                    break;
            }
        }

        private void DrawFight(float vw, float vh)
        {
            var f = Fight;
            if (f == null) return;
            var prof = system.GetProfile(Player.PlayerId);
            bool read = prof.Knows(FishingTechnique.ReadFish);
            var r = new Rect((vw - 800) * 0.5f, vh - 410, 800, 300);
            GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
            string title = Hooked != null && Hooked.IsLegend ? Loc.T("とてつもない手応え……！", "An incredible pull...!") :
                f.SizeFactor > 0.8f ? Loc.T("大物の手応え！", "Feels like a big one!") : Loc.T("何かがかかった！", "Something's on the line!");
            GUI.Label(new Rect(r.x + 20, r.y + 8, 500, 32), title, UIStyles.LabelBold);
            if (f.LineReliefFlash > 0f)
                GUI.Label(new Rect(r.x + 520, r.y + 8, 260, 32), "<color=#9ff>" + Loc.T("糸さばき！", "Line eased!") + "</color>", UIStyles.LabelBold);

            // 距離（右が沖、左が手元）
            var dist = new Rect(r.x + 20, r.y + 50, 760, 40);
            UIStyles.FillRect(dist, new Color(0.1f, 0.25f, 0.45f, 0.7f));
            UIStyles.FillRect(new Rect(dist.x, dist.y, 8, dist.height), new Color(0.6f, 0.45f, 0.3f));
            float fx = dist.x + dist.width * Mathf.Clamp01(f.Distance / f.MaxDistance);
            float wobble = f.Action == FishFight.ActionKind.Jump ? -10f * Mathf.Abs(Mathf.Sin(UnityEngine.Time.time * 10f)) :
                f.Action == FishFight.ActionKind.Dive ? 8f : 0f;
            Color fishCol = Hooked != null && Hooked.Glowing ? Hooked.Color : new Color(0.08f, 0.1f, 0.15f);
            UIStyles.FillRect(new Rect(fx - 18, dist.y + 10 + wobble, 36, 20), fishCol);
            UIStyles.FillRect(new Rect(dist.x + dist.width * (f.StartDistance / f.MaxDistance), dist.y, 2, dist.height), new Color(1, 1, 1, 0.25f));
            GUI.Label(new Rect(dist.x, dist.y + 40, 300, 24), Loc.Format("手元まで {0:0.0}m", "{0:0.0}m to go", f.Distance), UIStyles.LabelDim);

            // 張力
            var ten = new Rect(r.x + 20, r.y + 120, 760, 30);
            UIStyles.FillRect(ten, new Color(0, 0, 0, 0.5f));
            UIStyles.FillRect(new Rect(ten.x, ten.y, ten.width * 0.6f, ten.height), new Color(0.3f, 0.6f, 0.35f, 0.35f));
            UIStyles.FillRect(new Rect(ten.x + ten.width * 0.6f, ten.y, ten.width * 0.3f, ten.height), new Color(0.75f, 0.7f, 0.25f, 0.35f));
            UIStyles.FillRect(new Rect(ten.x + ten.width * 0.9f, ten.y, ten.width * 0.1f, ten.height), new Color(0.85f, 0.25f, 0.25f, 0.45f));
            float t = Mathf.Clamp01(f.Tension / 1.1f);
            Color tc = f.Tension > 0.9f ? UIStyles.Bad : f.Tension > 0.6f ? UIStyles.AccentWarm : UIStyles.Good;
            if (f.BreakDanger01 > 0f && Mathf.Repeat(UnityEngine.Time.unscaledTime * 8f, 1f) > 0.5f) tc = Color.white;
            UIStyles.FillRect(new Rect(ten.x + 2, ten.y + 4, (ten.width - 4) * t, ten.height - 8), tc);
            string tensionLabel = f.BreakDanger01 > 0f ? Loc.T("糸が切れそう！ 緩めて！", "About to snap! Ease off!") :
                f.SlackDanger01 > 0.4f ? Loc.T("緩みすぎ！ 巻いて！", "Too slack! Reel!") : Loc.T("糸の張り", "Line tension");
            GUI.Label(new Rect(ten.x, ten.y + 30, 400, 24), tensionLabel, f.BreakDanger01 > 0f || f.SlackDanger01 > 0.4f ? UIStyles.LabelBold : UIStyles.LabelDim);

            // 竿と魚の向き
            var rodBox = new Rect(r.x + 20, r.y + 180, 260, 70);
            UIStyles.FillRect(rodBox, new Color(0, 0, 0, 0.35f));
            float rx = rodBox.x + rodBox.width * 0.5f + f.RodX * (rodBox.width * 0.42f);
            UIStyles.FillRect(new Rect(rx - 4, rodBox.y + (f.RodUp ? 4 : 22), 8, f.RodUp ? 62 : 44), new Color(0.85f, 0.65f, 0.4f));
            string dir;
            switch (f.Action)
            {
                case FishFight.ActionKind.Dive: dir = Loc.T("魚：↓ 下へ", "Fish: ↓ down"); break;
                case FishFight.ActionKind.Jump: dir = Loc.T("魚：↑ 跳ねた！", "Fish: ↑ jumped!"); break;
                case FishFight.ActionKind.Phase: dir = Loc.T("魚：？？", "Fish: ??"); break;
                case FishFight.ActionKind.Rest: dir = Loc.T("魚：おとなしい", "Fish: calm"); break;
                case FishFight.ActionKind.Harden: dir = Loc.T("魚：ずしり…", "Fish: heavy..."); break;
                default: dir = f.FishDir < 0 ? Loc.T("魚：← 左へ", "Fish: ← left") : Loc.T("魚：→ 右へ", "Fish: → right"); break;
            }
            GUI.Label(new Rect(r.x + 300, r.y + 180, 240, 30), dir, UIStyles.LabelBold);
            UIStyles.ProgressBar(new Rect(r.x + 300, r.y + 214, 200, 14), f.CounterQuality, UIStyles.Accent);
            GUI.Label(new Rect(r.x + 505, r.y + 208, 100, 24), Loc.T("竿の効き", "rod"), UIStyles.LabelDim);

            // 魚の見極め
            if (read)
            {
                UIStyles.ProgressBar(new Rect(r.x + 600, r.y + 186, 180, 16), f.Stamina, new Color(1f, 0.6f, 0.4f));
                GUI.Label(new Rect(r.x + 600, r.y + 202, 180, 24), Loc.T("魚の元気", "Fish stamina"), UIStyles.LabelDim);
                if (f.ActionTimeLeft < 0.8f)
                    GUI.Label(new Rect(r.x + 600, r.y + 224, 190, 28), Loc.T("次：", "Next: ") + FishFight.ActionHint(f.NextAction), UIStyles.LabelBold);
            }
            else if (f.IsTired)
            {
                GUI.Label(new Rect(r.x + 600, r.y + 190, 190, 28), Loc.T("魚が疲れてきた！", "The fish is tiring!"), UIStyles.LabelBold);
            }

            GUI.Label(new Rect(r.x + 20, r.y + 262, 770, 28),
                Loc.T("[Space/左クリック 長押し] 巻く　[S] 糸を出す　[A/D] 竿を倒す（魚と反対へ）　[W] 竿を立てる（潜る魚）　[Q] あきらめる",
                    "[Hold Space/LMB] reel  [S] give line  [A/D] tilt rod (against the fish)  [W] raise rod (divers)  [Q] give up"), UIStyles.LabelDim);
        }

        private void DrawResult(float vw, float vh)
        {
            var res = LastResult;
            if (res == null || res.Species == null) return;
            var r = new Rect((vw - 620) * 0.5f, vh * 0.2f, 620, 250);
            GUI.Box(r, GUIContent.none, UIStyles.PanelBox);
            var title = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter };
            string name = Loc.T(res.Species.NameJa, res.Species.NameEn);
            GUI.Label(new Rect(r.x, r.y + 10, r.width, 44), Loc.Format("{0} を釣った！", "Caught: {0}!", name), title);
            GUI.Label(new Rect(r.x, r.y + 60, r.width, 32), Loc.Format("大きさ {0:0.0}cm　{1}", "Size {0:0.0}cm  {1}", res.Size, ItemStack.QualityStars(res.Quality)), UIStyles.LabelCenter);
            float y = r.y + 100;
            if (res.FirstTime) { GUI.Label(new Rect(r.x, y, r.width, 30), "<color=#ffd27a>" + Loc.T("初めて釣った魚！ 釣果帳に記録した", "First catch! Logged in the fish log") + "</color>", UIStyles.LabelCenter); y += 30; }
            else if (res.NewRecord) { GUI.Label(new Rect(r.x, y, r.width, 30), "<color=#ffd27a>" + Loc.T("自己記録更新！", "New personal record!") + "</color>", UIStyles.LabelCenter); y += 30; }
            if (res.LevelUp) { GUI.Label(new Rect(r.x, y, r.width, 30), "<color=#9fe8a8>" + Loc.Format("釣りの腕が上がった！（Lv{0}）", "Fishing level up! (Lv{0})", res.NewLevel) + "</color>", UIStyles.LabelCenter); y += 30; }
            if (res.Species.IsRocketMaterial)
            {
                GUI.Label(new Rect(r.x + 20, y, r.width - 40, 50), Loc.T(res.Species.RocketNoteJa, res.Species.RocketNoteEn), UIStyles.LabelWrap);
            }
            GUI.Label(new Rect(r.x, r.yMax - 34, r.width, 28), Loc.T("[左クリック / F / E] 閉じる", "[LMB / F / E] close"), UIStyles.LabelDim);
        }
    }
}
