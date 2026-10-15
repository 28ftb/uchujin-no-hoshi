using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// タブレットのアプリ「思い出」（AppId "memories"）。
    /// ・この星での節目（不時着・初めての引っ越し・初めての修理・帰還の日など）を、観察できた事実として振り返る（章名は出さない）。
    /// ・最後に見たエンディングの記録（見送りに来た人数・手紙・贈り物）。
    /// ・エンディングの最後（宇宙船の窓・辞書・クレジット）をもう一度見る（基本決定 §5：エンディング再鑑賞の仮実装）。
    /// ・星をまたいだ旅の記録。
    /// </summary>
    public sealed class MemoriesApp : ITabletApp
    {
        private Vector2 scroll;

        public string AppId => StoryTuning.AppMemories;
        public string Title => Loc.T("思い出", "Memories");
        public int Order => 90;
        public string IconGlyph => "★";
        public Color IconColor => new Color(1f, 0.8f, 0.45f);
        public bool IsAvailable => StorySystem.Instance != null && StorySystem.Instance.IsPrologueDone;

        public void OnOpen() { }

        private static readonly Dictionary<string, LocText> MilestoneText = new Dictionary<string, LocText>
        {
            { "arrived", new LocText("この星に不時着した", "Crash-landed on this planet") },
            { "first_move_in", new LocText("はじめて新しい住民が引っ越してきた", "A new resident moved in for the first time") },
            { "first_move_out", new LocText("はじめて住民が村を引っ越していった", "A resident moved away for the first time") },
            { "first_repair", new LocText("宇宙船の修理がはじめて進んだ", "The ship's repair moved forward for the first time") },
            { "launch_ready", new LocText("宇宙船の修理が完了した", "The ship's repair was complete") },
            { "first_ending", new LocText("帰還の日。みんなが見送りに来た", "The day of departure. Everyone came to see you off") },
        };

        public void Draw(Rect area)
        {
            var story = StorySystem.Instance;
            GUILayout.BeginArea(area);
            scroll = GUILayout.BeginScrollView(scroll);
            if (story == null)
            {
                GUILayout.Label(Loc.T("記録がありません。", "No records."), UIStyles.LabelDim);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label(Loc.T("この星での思い出", "Memories of this planet"), UIStyles.LabelBold);
            var list = new List<KeyValuePair<string, int>>();
            foreach (var kv in story.Milestones()) if (MilestoneText.ContainsKey(kv.Key)) list.Add(kv);
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            if (list.Count == 0) GUILayout.Label(Loc.T("まだ何もありません。", "Nothing yet."), UIStyles.LabelDim);
            foreach (var kv in list)
            {
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}日目", "Day {0}", kv.Value), UIStyles.LabelBold, GUILayout.Width(120));
                GUILayout.Label(MilestoneText[kv.Key].ToString(), UIStyles.LabelWrap);
                GUILayout.EndHorizontal();
            }

            var rec = story.State.lastEnding;
            if (rec != null)
            {
                GUILayout.Space(12);
                GUILayout.Label(Loc.T("帰還の日の記録", "The day you left"), UIStyles.LabelBold);
                GUILayout.Label(Loc.Format("{0}日目　見送りに来てくれた人：{1}人　遠くから見ていた人：{2}人", "Day {0}  Came to see you off: {1}  Watched from afar: {2}",
                    rec.day, rec.farewellCount, rec.watcherResidentIds != null ? rec.watcherResidentIds.Count : 0), UIStyles.Label);
                GUILayout.Label(Loc.Format("プレゼント：{0}　手紙：{1}通　お店の常連さん：{2}人　辞書に書いた言葉：{3}語",
                    "Gifts: {0}  Letters: {1}  Regular customers: {2}  Words in your dictionary: {3}",
                    rec.giftCount, rec.letterCount, rec.regularCount, rec.dictionaryWordCount), UIStyles.Label);
                GUILayout.Label(Loc.Format("帰還を選んだ回数：{0}", "Times you chose to leave: {0}", story.EndingCount), UIStyles.LabelDim);
                GUILayout.Space(6);
                GUI.enabled = Game.Instance != null && Game.Instance.State == GameState.Playing && !story.IsCutsceneRunning;
                if (GUILayout.Button(Loc.T("あの日の窓と辞書を、もう一度見る", "See the window and the dictionary again"), UIStyles.Button, GUILayout.Height(48)))
                    story.PlayMemoriesReel();
                GUI.enabled = true;
            }

            var journey = story.Profile.journey;
            if (journey != null && journey.Count > 0)
            {
                GUILayout.Space(12);
                GUILayout.Label(Loc.T("旅の記録", "Journey"), UIStyles.LabelBold);
                foreach (var j in journey)
                {
                    if (j == null) continue;
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    GUILayout.Label(Loc.Format("星{0}", "Planet {0}", j.planetIndex), UIStyles.LabelBold, GUILayout.Width(100));
                    GUILayout.Label(string.IsNullOrEmpty(j.planetName) ? "?" : j.planetName, UIStyles.LabelAlien, GUILayout.Width(260));
                    GUILayout.Label(Loc.Format("{0}日　出会った住民 {1}人{2}", "{0} days, met {1} residents{2}", j.daysStayed, j.residentsMet,
                        j.departed ? Loc.T("　（旅立った）", " (departed)") : ""), UIStyles.LabelWrap);
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }

    /// <summary>エンディングの最後（宇宙船の窓・辞書・着陸・クレジット）の再生。暮らしは変化しない（贈り物も増えない）。</summary>
    public sealed class MemoriesReel
    {
        private readonly StorySystem story;
        private CutsceneDirector director;
        private Transform root;
        public bool Running { get; private set; }

        public MemoriesReel(StorySystem story)
        {
            this.story = story;
        }

        public void Start()
        {
            director = CutsceneDirector.Create("MemoriesReel");
            director.SkipEnabled = true;
            var go = new GameObject("MemoriesReelSet");
            if (Game.Instance.SceneRoot != null) go.transform.SetParent(Game.Instance.SceneRoot, false);
            root = go.transform;
            Running = true;
            director.StartCoroutine(Run());
        }

        public void Abort()
        {
            Running = false;
            EndingSequence.RestoreSpaceView();
            if (Game.Instance != null && Game.Instance.Time != null) Game.Instance.Time.RemoveGlobalPause("story.reel");
            if (root != null) Object.Destroy(root.gameObject);
            if (director != null) Object.Destroy(director.gameObject);
            director = null;
        }

        private IEnumerator Run()
        {
            var game = Game.Instance;
            var p = game.LocalPlayer;
            var back = p.Position;
            game.SetState(GameState.Cutscene);
            UIManager.HudVisible = false;
            p.LockInput("story");
            game.Time?.AddGlobalPause("story.reel");
            director.Letterbox = true;
            yield return director.StartCoroutine(director.Fade(true, 0.8f));

            var heard = new List<UtteranceRecord>();
            var speech = game.Service<ISpeechService>();
            var rec = story.State.lastEnding;
            if (speech != null && rec != null && rec.farewellRecordIds != null)
            {
                foreach (var id in rec.farewellRecordIds)
                {
                    UtteranceRecord r = null;
                    try { r = speech.GetRecord(id); } catch (System.Exception) { }
                    if (r != null) heard.Add(r);
                }
            }
            yield return director.StartCoroutine(EndingSequence.WindowAndDictionary(director, rec, heard, story.GuideCatchphraseConcept, p.Appearance, root, null, null));
            director.SkipEnabled = false;
            bool done = false;
            UIManager.Open(new CreditsWindow(true, rec, () => done = true));
            ScreenFader.FadeIn(0.5f);
            while (!done) yield return null;

            ScreenFader.SetBlack();
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
            director.Letterbox = false;
            director.ReleaseCamera();
            p.Position = back;
            game.Time?.RemoveGlobalPause("story.reel");
            game.SetState(GameState.Playing);
            UIManager.HudVisible = true;
            p.UnlockInput("story");
            ScreenFader.FadeIn(1f);
            Running = false;
            yield return null;
            if (director != null) Object.Destroy(director.gameObject);
            director = null;
        }
    }
}
