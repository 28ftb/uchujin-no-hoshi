using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// タブレットのアプリ「着替え」（AppId "appearance"）。
    /// 基本決定 §3【未決定】「作成後の見た目の変更方法や範囲」の仮実装：いつでもキャラクターメイク画面で着替えられる。
    /// 自宅の鏡などは Housing 担当の範囲外なので、タブレットから行う。お気に入りの服装を保存して着替えられる。
    /// 変更後は PlayerSystem.RebuildLocalVisual() で見た目を作り直す（マルチの他プレイヤーへの反映は Network 担当）。
    /// </summary>
    public sealed class AppearanceApp : ITabletApp
    {
        private Vector2 scroll;

        public string AppId => StoryTuning.AppAppearance;
        public string Title => Loc.T("着替え", "Wardrobe");
        public int Order => 80;
        public string IconGlyph => "服";
        public Color IconColor => new Color(1f, 0.6f, 0.8f);
        public bool IsAvailable => true;

        public void OnOpen() { }

        public void Draw(Rect area)
        {
            var game = Game.Instance;
            var p = game != null ? game.LocalPlayer : null;
            GUILayout.BeginArea(area);
            if (p == null)
            {
                GUILayout.EndArea();
                return;
            }
            GUILayout.Label(Loc.T("いつでも見た目を変えられます。", "Change your look any time."), UIStyles.LabelWrap);
            GUILayout.Space(8);
            GUI.enabled = game.State == GameState.Playing;
            if (GUILayout.Button(Loc.T("着替える（見た目を編集）", "Change outfit (edit look)"), UIStyles.ButtonSelected, GUILayout.Height(54)))
                OpenEditor();
            GUI.enabled = true;

            GUILayout.Space(14);
            GUILayout.Label(Loc.T("お気に入りの服装", "Saved looks"), UIStyles.LabelBold);
            var story = StorySystem.Instance;
            if (story != null)
            {
                if (GUILayout.Button(Loc.T("今の服装を保存する", "Save current look"), UIStyles.Button, GUILayout.Height(44), GUILayout.Width(320)))
                {
                    int n = story.Profile.presets.Count + 1;
                    story.SavePreset(Loc.Format("服装 {0}", "Look {0}", n), p.Appearance);
                    UIManager.Toast(Loc.T("服装を保存しました", "Look saved"));
                }
                scroll = GUILayout.BeginScrollView(scroll);
                var presets = story.Profile.presets;
                int remove = -1;
                for (int i = 0; i < presets.Count; i++)
                {
                    var pr = presets[i];
                    if (pr == null || pr.appearance == null) continue;
                    GUILayout.BeginHorizontal(UIStyles.Box);
                    var sw = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                    UIStyles.FillRect(new Rect(sw.x, sw.y, 20, 40), pr.appearance.topColor);
                    UIStyles.FillRect(new Rect(sw.x + 20, sw.y, 20, 40), pr.appearance.bottomColor);
                    GUILayout.Label(pr.label + "　" + AppearanceCatalog.Label(AppearanceCatalog.Tops, pr.appearance.outfitTop) + "／" +
                                    AppearanceCatalog.Label(AppearanceCatalog.Accessories, pr.appearance.accessory), UIStyles.Label);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(Loc.T("着る", "Wear"), UIStyles.ButtonSmall, GUILayout.Width(90), GUILayout.Height(36)))
                    {
                        var a = pr.appearance.Clone();
                        a.displayName = p.Appearance != null ? p.Appearance.displayName : a.displayName;
                        Apply(a);
                    }
                    if (GUILayout.Button(Loc.T("消す", "Delete"), UIStyles.ButtonSmall, GUILayout.Width(90), GUILayout.Height(36))) remove = i;
                    GUILayout.EndHorizontal();
                }
                if (remove >= 0) presets.RemoveAt(remove);
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        }

        private static void OpenEditor()
        {
            var game = Game.Instance;
            var tablet = game.Service<ITabletService>();
            if (tablet != null && tablet.IsOpen) tablet.Close();
            UIManager.Open(new CharacterMakeWindow(game.LocalPlayer.Appearance, CharacterMakeWindow.Mode.ChangeClothes, Apply, null));
        }

        private static void Apply(CharacterAppearance a)
        {
            var game = Game.Instance;
            if (game == null || game.LocalPlayer == null || a == null) return;
            game.LocalPlayer.Appearance = AppearanceCatalog.Sanitize(a.Clone());
            game.PlayerSys?.RebuildLocalVisual();
            UIManager.Toast(Loc.T("着替えました", "Changed your look"));
        }
    }
}
