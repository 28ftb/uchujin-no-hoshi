using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 初めての料理で「自分で触れる」短い操作（§6.1）。
    /// すべてを同じゲージ停止にせず、能力ごとに手触りを変える：
    ///   温める・しぼる＝長押しで目盛りを保つ／水・成形・冷やす・包む＝ちょうどで止める／
    ///   回す・泡立て・磨く＝連打で範囲へ（泡はあふれる）／光らせる・積む＝拍に合わせる。
    /// 感知・音の生き物がいると範囲が広がる（状態を知らせてくれる）。
    /// 閉じると中止（材料は使わない）。
    /// </summary>
    public sealed class CookingMinigameWindow : UIWindow
    {
        private enum Phase { Intro, Play, Result, Done }

        private readonly StationContext ctx;
        private readonly RecipeDef recipe;
        private readonly Action<float> onDone;
        private readonly List<StepPlan> steps = new List<StepPlan>();
        private readonly List<float> scores = new List<float>();
        private int index;
        private Phase phase = Phase.Intro;
        private float timer;
        private float value;
        private float inBand;
        private float target = 0.5f;
        private int beats;
        private float beatScore;
        private bool stopped;
        private bool overflowed;
        private string reaction = "";
        private bool mouseHeld;
        private bool mouseDown;
        private Rect actionRect;
        private bool finished;

        // 【仮数値】ミニゲームの長さ・範囲
        private const float IntroSeconds = 1.1f;
        private const float ResultSeconds = 0.9f;
        private const float HoldDuration = 4f;
        private const float MashDuration = 4f;
        private const float StopTimeout = 5f;
        private const int RhythmBeats = 3;
        private const float BeatPeriod = 1.2f;

        public CookingMinigameWindow(StationContext ctx, CraftPlan plan, RecipeDef recipe, Action<float> onDone)
        {
            this.ctx = ctx;
            this.recipe = recipe;
            this.onDone = onDone;
            foreach (var s in plan.Steps)
            {
                if (s.Mode == StepMode.Timed || s.Mode == StepMode.Slow || s.Mode == StepMode.Missing) continue;
                if (CookingTables.Step(s.Step).Minigame == MinigameKind.None) continue;
                steps.Add(s);
            }
        }

        public override string Title => recipe != null && CookingSystem.Instance != null && CookingSystem.Instance.IsKnown(ctx.Player.PlayerId, recipe)
            ? Loc.Format("{0}を作る", "Making {0}", ItemRegistry.NameOf(recipe.OutputItemId))
            : Loc.T("何ができるかな…", "What will it become...");
        public override Vector2 Size => new Vector2(820, 600);
        public override bool PausesTimeInSolo => true;

        private float BandHalf => 0.13f + (ctx.HasSense ? 0.05f : 0f);
        private MinigameKind Kind => index < steps.Count ? CookingTables.Step(steps[index].Step).Minigame : MinigameKind.None;

        public override void OnOpen()
        {
            if (steps.Count == 0) Finish();
            else BeginStep();
        }

        public override void OnClose()
        {
            if (!finished) UIManager.Toast(Loc.T("作るのをやめた（材料はそのまま）", "Stopped (ingredients kept)"));
        }

        private void BeginStep()
        {
            phase = Phase.Intro;
            timer = 0f;
            value = Kind == MinigameKind.Mash ? 0.1f : 0.2f;
            inBand = 0f;
            beats = 0;
            beatScore = 0f;
            stopped = false;
            overflowed = false;
            reaction = "";
            // 見た目だけの揺らぎ（UnityEngine.Random 可）
            target = UnityEngine.Random.Range(0.35f, 0.7f);
        }

        public override void Update()
        {
            if (phase == Phase.Done) return;
            float dt = Mathf.Min(0.1f, UnityEngine.Time.unscaledDeltaTime);
            bool held = GameInput.KeyHeld(KeyCode.Space) || mouseHeld;
            bool down = GameInput.KeyDown(KeyCode.Space) || mouseDown;
            mouseDown = false;
            timer += dt;
            switch (phase)
            {
                case Phase.Intro:
                    if (timer >= IntroSeconds) { phase = Phase.Play; timer = 0f; }
                    break;
                case Phase.Play:
                    UpdatePlay(dt, held, down);
                    break;
                case Phase.Result:
                    if (timer >= ResultSeconds)
                    {
                        index++;
                        if (index >= steps.Count) Finish();
                        else BeginStep();
                    }
                    break;
            }
        }

        private void UpdatePlay(float dt, bool held, bool down)
        {
            float half = BandHalf;
            switch (Kind)
            {
                case MinigameKind.HoldBand:
                {
                    float c = 0.68f;
                    value += (held ? 0.55f : -0.42f) * dt;
                    value = Mathf.Clamp01(value);
                    bool ok = Mathf.Abs(value - c) <= half;
                    if (ok) inBand += dt;
                    reaction = ok ? Loc.T("いい感じ♪", "Just right!") : (value > c ? Loc.T("強すぎ！", "Too much!") : Loc.T("弱いかも…", "Too weak..."));
                    if (timer >= HoldDuration) EndStep(Mathf.Clamp01(inBand / (HoldDuration * 0.8f)));
                    break;
                }
                case MinigameKind.StopAtLine:
                {
                    if (!stopped) value = Mathf.PingPong(timer * 0.85f, 1f);
                    if (!stopped && down)
                    {
                        stopped = true;
                        float d = Mathf.Abs(value - target);
                        float s = Mathf.Clamp01(1f - Mathf.Max(0f, d - half * 0.35f) / 0.4f);
                        reaction = s > 0.8f ? Loc.T("ぴったり！", "Perfect!") : s > 0.5f ? Loc.T("まあまあ", "Not bad") : Loc.T("ずれちゃった", "A bit off");
                        EndStep(s);
                    }
                    else if (!stopped && timer >= StopTimeout)
                    {
                        reaction = Loc.T("のんびりしすぎた", "Too slow");
                        EndStep(0.3f);
                    }
                    break;
                }
                case MinigameKind.Mash:
                {
                    float c = 0.72f;
                    if (down) value += 0.085f;
                    value -= 0.2f * dt;
                    if (steps[index].Step == CraftStep.Bubble && value > 0.97f)
                    {
                        // 泡はあふれる
                        value = 0.55f;
                        overflowed = true;
                        reaction = Loc.T("あふれた！", "Overflow!");
                        inBand = Mathf.Max(0f, inBand - 0.5f);
                    }
                    value = Mathf.Clamp01(value);
                    bool ok = Mathf.Abs(value - c) <= half;
                    if (ok) { inBand += dt; reaction = Loc.T("その調子！", "Keep going!"); }
                    else if (!overflowed || value < 0.6f) reaction = value < c ? Loc.T("もっと！", "More!") : Loc.T("やりすぎ注意", "Easy now");
                    if (timer >= MashDuration) EndStep(Mathf.Clamp01(inBand / (MashDuration * 0.7f)));
                    break;
                }
                case MinigameKind.Rhythm:
                {
                    float phase01 = (timer % BeatPeriod) / BeatPeriod;
                    value = Mathf.Sin(phase01 * Mathf.PI); // 0→1→0 の脈動
                    int cycle = Mathf.FloorToInt(timer / BeatPeriod);
                    if (down && beats < RhythmBeats)
                    {
                        float acc = Mathf.Clamp01(1f - Mathf.Abs(phase01 - 0.5f) / (0.25f + half * 0.5f));
                        beatScore += acc;
                        beats++;
                        reaction = acc > 0.75f ? Loc.T("ぴかっ！", "Flash!") : acc > 0.4f ? Loc.T("ちょっとずれた", "Slightly off") : Loc.T("あれれ", "Oops");
                    }
                    if (beats >= RhythmBeats) EndStep(beatScore / RhythmBeats);
                    else if (cycle >= RhythmBeats + 2) EndStep(beatScore / RhythmBeats);
                    break;
                }
                default:
                    EndStep(0.5f);
                    break;
            }
        }

        private void EndStep(float score)
        {
            scores.Add(Mathf.Clamp01(score));
            phase = Phase.Result;
            timer = 0f;
        }

        private void Finish()
        {
            if (finished) return;
            finished = true;
            phase = Phase.Done;
            float avg = 0.5f;
            if (scores.Count > 0)
            {
                avg = 0f;
                foreach (var s in scores) avg += s;
                avg /= scores.Count;
            }
            Close();
            try { onDone?.Invoke(avg); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public override void DrawContents(Rect content)
        {
            var e = Event.current;
            if (index >= steps.Count)
            {
                GUILayout.Label(Loc.T("仕上げ中…", "Finishing..."), UIStyles.LabelCenter);
                return;
            }
            var sp = steps[index];
            var info = CookingTables.Step(sp.Step);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.Format("手順 {0}/{1}", "Step {0}/{1}", index + 1, steps.Count), UIStyles.LabelDim, GUILayout.Width(140));
            for (int i = 0; i < steps.Count; i++) CookingUI.StepIcon(steps[i].Step, 28f, i != index);
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            // 手伝う生き物（表情・動作の代わりに、跳ねる札と擬音）
            string who = sp.Source != null ? sp.Source.DisplayName : (sp.Mode == StepMode.Weather ? Loc.T("空の光と水", "The sky") : Loc.T("あなたの手", "Your hands"));
            GUILayout.BeginHorizontal();
            float bounce = Mathf.Abs(Mathf.Sin(UnityEngine.Time.unscaledTime * (phase == Phase.Play ? 6f : 2f))) * 8f;
            GUILayout.Space(10);
            GUILayout.BeginVertical(GUILayout.Width(90));
            GUILayout.Space(10 - bounce);
            if (sp.Source != null) CookingUI.AbilityIcon(sp.Source.Ability, 72f);
            else CookingUI.StepIcon(sp.Step, 72f);
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            GUILayout.Label(who + "：" + info.Name, CookingUI.Header);
            GUILayout.Label(info.Action, UIStyles.LabelWrap);
            GUILayout.Label(HowTo(), UIStyles.LabelDim);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(12);

            // ゲージ
            actionRect = GUILayoutUtility.GetRect(content.width - 40, 120, GUILayout.Height(120));
            DrawGauge(actionRect);
            GUILayout.Space(8);
            string status = phase == Phase.Intro ? Loc.T("よーい…", "Ready...") : phase == Phase.Result ? Loc.Format("{0}（{1}）", "{0} ({1})", reaction, Grade(scores.Count > 0 ? scores[scores.Count - 1] : 0f)) : reaction;
            GUILayout.Label(status, UIStyles.LabelCenter);

            // マウス入力（ゲージ部分を押す）
            if (e.type == EventType.MouseDown && actionRect.Contains(e.mousePosition))
            {
                mouseHeld = true;
                mouseDown = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                mouseHeld = false;
            }
        }

        private string HowTo()
        {
            switch (Kind)
            {
                case MinigameKind.HoldBand: return Loc.T("スペース（またはゲージ）を長押しして、目印の範囲に保つ", "Hold Space (or the gauge) to stay in the band");
                case MinigameKind.StopAtLine: return Loc.T("目印のところでスペース（またはゲージをクリック）", "Press Space (or click) at the mark");
                case MinigameKind.Mash: return Loc.T("スペース（またはゲージ）を連打して範囲に入れる", "Tap Space (or the gauge) to stay in the band");
                case MinigameKind.Rhythm: return Loc.T("光が一番強くなった瞬間にスペース（3回）", "Press Space at each brightest pulse (3x)");
                default: return "";
            }
        }

        private static string Grade(float s)
        {
            if (s > 0.85f) return Loc.T("すばらしい", "Excellent");
            if (s > 0.6f) return Loc.T("じょうず", "Good");
            if (s > 0.35f) return Loc.T("まずまず", "Okay");
            return Loc.T("ざんねん", "Rough");
        }

        private void DrawGauge(Rect r)
        {
            UIStyles.FillRect(r, new Color(0f, 0f, 0f, 0.45f));
            var info = CookingTables.Step(steps[index].Step);
            float half = BandHalf;
            var inner = new Rect(r.x + 10, r.y + 40, r.width - 20, 40);
            UIStyles.FillRect(inner, new Color(0.15f, 0.17f, 0.25f, 1f));
            switch (Kind)
            {
                case MinigameKind.HoldBand:
                case MinigameKind.Mash:
                {
                    float c = Kind == MinigameKind.HoldBand ? 0.68f : 0.72f;
                    var band = new Rect(inner.x + inner.width * (c - half), inner.y, inner.width * half * 2f, inner.height);
                    UIStyles.FillRect(band, new Color(0.4f, 0.9f, 0.5f, 0.45f));
                    UIStyles.FillRect(new Rect(inner.x, inner.y + 8, inner.width * value, inner.height - 16), info.Color);
                    if (Kind == MinigameKind.Mash && steps[index].Step == CraftStep.Bubble)
                        UIStyles.FillRect(new Rect(inner.x + inner.width * 0.97f, inner.y, 3, inner.height), UIStyles.Bad);
                    float limit = Kind == MinigameKind.HoldBand ? HoldDuration : MashDuration;
                    if (phase == Phase.Play) UIStyles.ProgressBar(new Rect(r.x + 10, r.y + 92, r.width - 20, 14), 1f - timer / limit, UIStyles.AccentWarm);
                    break;
                }
                case MinigameKind.StopAtLine:
                {
                    var band = new Rect(inner.x + inner.width * (target - half * 0.6f), inner.y, inner.width * half * 1.2f, inner.height);
                    UIStyles.FillRect(band, new Color(0.4f, 0.9f, 0.5f, 0.5f));
                    UIStyles.FillRect(new Rect(inner.x + inner.width * target - 1, inner.y - 6, 3, inner.height + 12), Color.white);
                    UIStyles.FillRect(new Rect(inner.x + inner.width * value - 6, inner.y - 10, 12, inner.height + 20), info.Color);
                    break;
                }
                case MinigameKind.Rhythm:
                {
                    var glow = info.Color;
                    glow.a = 0.25f + 0.75f * value;
                    float size = 30f + 50f * value;
                    UIStyles.FillRect(new Rect(r.center.x - size * 0.5f, r.center.y - size * 0.5f, size, size), glow);
                    for (int i = 0; i < RhythmBeats; i++)
                        UIStyles.FillRect(new Rect(r.x + 20 + i * 30, r.y + 10, 20, 20), i < beats ? UIStyles.Good : new Color(1f, 1f, 1f, 0.2f));
                    break;
                }
            }
        }
    }
}
