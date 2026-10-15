using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// クレジット（タイトルの「クレジット」とエンディングの最後）。自動で流れ、クリック／Esc で飛ばせる。
    /// エンディングでは「この星で出会った住民」を宇宙語の名前（表面形）で流す。プレイヤーの暮らしの記録も添える。
    /// </summary>
    public class CreditsWindow : UIWindow
    {
        private readonly List<string> lines = new List<string>();
        private readonly List<bool> alienLine = new List<bool>();
        private readonly Action onFinished;
        private float offset;
        private float contentHeight = 2000f;
        private bool finished;

        public override string Title => "";
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => true;
        public override Vector2 Size => new Vector2(UIStyles.VirtualWidth, UIStyles.VirtualHeight);
        public override Vector2? Position => Vector2.zero;

        public CreditsWindow(bool ending, EndingRecord record, Action onFinished)
        {
            this.onFinished = onFinished;
            Add(Loc.T("宇宙人の星", "Planet of Aliens"));
            Add("");
            if (ending)
            {
                Add(Loc.T("― 最後の引っ越し ―", "- The Last Move -"));
                Add("");
                AppendPlanetMemories(record);
            }
            Add(Loc.T("企画・仕様", "Game design"));
            Add(Loc.T("（仮）企画チーム", "(TBD) Design team"));
            Add("");
            Add(Loc.T("プログラム", "Programming"));
            Add(Loc.T("（仮）開発チーム", "(TBD) Development team"));
            Add("");
            Add(Loc.T("宇宙語", "Alien languages"));
            Add(Loc.T("星ごとに生まれる言葉たち", "Words born on every planet"));
            Add("");
            Add(Loc.T("そして", "And"));
            string name = Game.Instance != null && Game.Instance.LocalPlayer != null ? Game.Instance.LocalPlayer.DisplayName : "";
            Add(Loc.Format("辞書を書いた、{0}", "{0}, who wrote the dictionary", name));
            Add("");
            Add("");
            Add(Loc.T("おしまい", "The End"));
        }

        private void AppendPlanetMemories(EndingRecord record)
        {
            var game = Game.Instance;
            var dir = game != null ? game.Service<IResidentDirectory>() : null;
            var speech = game != null ? game.Service<ISpeechService>() : null;
            if (dir == null || speech == null) return;
            Add(Loc.T("この星で暮らしている人たち", "The people living on this planet"));
            int n = 0;
            try
            {
                foreach (var r in dir.ActiveResidents)
                {
                    if (r == null) continue;
                    string nm = null;
                    try { nm = speech.ResidentName(r.Id); } catch (Exception) { }
                    if (string.IsNullOrEmpty(nm)) continue;
                    AddAlien(nm);
                    if (++n >= 40) break;
                }
            }
            catch (Exception) { }
            Add("");
            int former = 0;
            try { foreach (var r in dir.FormerResidents) if (r != null) former++; } catch (Exception) { }
            if (former > 0)
            {
                Add(Loc.T("引っ越していった人たち", "Those who moved away"));
                n = 0;
                try
                {
                    foreach (var r in dir.FormerResidents)
                    {
                        if (r == null) continue;
                        string nm = null;
                        try { nm = speech.ResidentName(r.Id); } catch (Exception) { }
                        if (string.IsNullOrEmpty(nm)) continue;
                        AddAlien(nm);
                        if (++n >= 20) break;
                    }
                }
                catch (Exception) { }
                Add("");
            }
            if (record != null)
            {
                int days = record.day;
                Add(Loc.Format("この星で過ごした日々：{0}日", "Days on this planet: {0}", days));
                if (record.farewellCount > 0) Add(Loc.Format("見送りに来てくれた人：{0}人", "Came to see you off: {0}", record.farewellCount));
                if (record.regularCount > 0) Add(Loc.Format("お店の常連さん：{0}人", "Regular customers: {0}", record.regularCount));
                if (record.letterCount > 0) Add(Loc.Format("届いた手紙：{0}通", "Letters received: {0}", record.letterCount));
                Add(Loc.Format("辞書に書いた言葉：{0}語", "Words in your dictionary: {0}", record.dictionaryWordCount));
                Add("");
            }
        }

        private void Add(string s)
        {
            lines.Add(s);
            alienLine.Add(false);
        }

        private void AddAlien(string s)
        {
            lines.Add(s);
            alienLine.Add(true);
        }

        public override void OnOpen()
        {
            offset = 0f;
            finished = false;
        }

        public override void OnClose()
        {
            if (finished) return;
            finished = true;
            onFinished?.Invoke();
        }

        public override void Update()
        {
            offset += UnityEngine.Time.unscaledDeltaTime * 60f;
            if (offset > contentHeight + UIStyles.VirtualHeight * 0.5f) Close();
        }

        public override void DrawContents(Rect content)
        {
            UIStyles.FillRect(new Rect(-16, -12, content.width + 32, content.height + 24), new Color(0.02f, 0.02f, 0.06f, 1f));
            float y = content.height - offset;
            var normal = new GUIStyle(UIStyles.Label) { alignment = TextAnchor.MiddleCenter, fontSize = 26 };
            var title = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 40 };
            var alien = new GUIStyle(UIStyles.LabelAlien) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            float start = y;
            for (int i = 0; i < lines.Count; i++)
            {
                var st = i == 0 ? title : alienLine[i] ? alien : normal;
                float h = i == 0 ? 70 : 44;
                if (y > -60 && y < content.height + 60) GUI.Label(new Rect(0, y, content.width, h), lines[i], st);
                y += h;
            }
            contentHeight = y - start;
            GUI.Label(new Rect(content.width - 420, content.height - 40, 400, 30), Loc.T("クリック／Esc：とばす", "Click / Esc: skip"), UIStyles.LabelDim);
            if (Event.current.type == EventType.MouseDown)
            {
                Event.current.Use();
                Close();
            }
        }
    }
}
