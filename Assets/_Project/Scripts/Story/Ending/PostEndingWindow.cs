using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Story
{
    /// <summary>
    /// クリア後の選択（基本決定 §5【確定】）。
    ///   A「ロケット前へ戻り、この星で生活を続ける」→ EndingFinishedEvent{ReturnBeforeLaunch}（Rocket が出発直前の状態に戻す）
    ///   B「次の星へ進む」→ 引き継ぎ設定 → EndingFinishedEvent{NextPlanet} → 現在の星をセーブ → 新しい星へ
    /// どちらを選んでも強制終了しない。
    /// </summary>
    public class PostEndingWindow : UIWindow
    {
        private readonly StorySystem story;

        public PostEndingWindow(StorySystem story)
        {
            this.story = story;
        }

        public override string Title => Loc.T("これから", "What's next");
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => false;
        public override Vector2 Size => new Vector2(980, 640);

        public override void DrawContents(Rect content)
        {
            GUILayout.Label(Loc.T("故郷へ帰る物語を見届けました。これからどうしますか？", "You have seen the journey home. What would you like to do now?"), UIStyles.LabelWrap);
            GUILayout.Space(16);

            GUILayout.BeginVertical(UIStyles.Box);
            if (GUILayout.Button(Loc.T("ロケットの前に戻って、この星で暮らしを続ける", "Return to the rocket and keep living on this planet"), UIStyles.ButtonSelected, GUILayout.Height(64)))
            {
                Close();
                story.ChooseReturnBeforeLaunch();
            }
            GUILayout.Label(Loc.T(
                "出発の直前に戻ります。農場・家・お店・住民との関係・辞書や図鑑はそのまま。好きな時にまた帰還を選べます。\nエンディングの思い出は、タブレットの「思い出」で見返せます。",
                "You return to just before departure. Your farm, home, shop, friendships and dictionary stay as they are. You can choose to leave again any time.\nYou can look back on the ending in the tablet's \"Memories\" app."), UIStyles.LabelDim);
            GUILayout.EndVertical();

            GUILayout.Space(14);

            GUILayout.BeginVertical(UIStyles.Box);
            if (GUILayout.Button(Loc.T("次の星へ進む", "Travel on to the next planet"), UIStyles.Button, GUILayout.Height(64)))
            {
                Close();
                UIManager.Open(new CarryOverWindow(story));
            }
            GUILayout.Label(Loc.T(
                "新しい星では、言葉も住民も地形も気候もちがいます。辞書は星ごとに別の冊子になります。\nこの星のセーブは残り、タイトルの「続きから」でいつでも戻ってこられます（仮）。",
                "On a new planet, the language, residents, terrain and climate are all different. Each planet gets its own dictionary.\nThis planet's save remains; you can come back any time from \"Continue\" on the title screen (provisional)."), UIStyles.LabelDim);
            GUILayout.EndVertical();
        }
    }

    /// <summary>
    /// 次の星への引き継ぎ設定（基本決定 §5：見た目・持ち物・お金・道具の引き継ぎは【未決定】→ 選択式の仮実装）。
    /// 手紙・贈り物などの思い出の品は、持ち物を置いていく場合でも持っていく。
    /// </summary>
    public class CarryOverWindow : UIWindow
    {
        private readonly StorySystem story;
        private readonly CarryOverPlan plan = new CarryOverPlan();

        public CarryOverWindow(StorySystem story)
        {
            this.story = story;
        }

        public override string Title => Loc.T("次の星へ（持っていくもの）", "To the next planet (what to bring)");
        public override bool ShowCloseButton => false;
        public override bool CloseOnEscape => false;
        public override Vector2 Size => new Vector2(1000, 820);

        public override void DrawContents(Rect content)
        {
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(content.height - 80));
            GUILayout.Label(Loc.T("※ 引き継ぎの範囲は仮のルールです。あとで調整されることがあります。", "* Carry-over rules are provisional."), UIStyles.LabelDim);
            GUILayout.Space(8);

            GUILayout.Label(Loc.T("見た目", "Appearance"), UIStyles.LabelBold);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("今の姿のまま", "Keep as is"), plan.keepAppearance ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(44)))
            {
                plan.keepAppearance = true;
                plan.newAppearance = null;
            }
            if (GUILayout.Button(plan.newAppearance != null ? Loc.T("作り直した（もう一度編集）", "Remade (edit again)") : Loc.T("作り直す", "Remake"),
                    !plan.keepAppearance ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(44)))
            {
                var baseLook = plan.newAppearance ?? Game.Instance.LocalPlayer.Appearance;
                var self = this;
                UIManager.Close(this);
                UIManager.Open(new CharacterMakeWindow(baseLook, CharacterMakeWindow.Mode.NextPlanet,
                    a =>
                    {
                        plan.newAppearance = a;
                        plan.keepAppearance = false;
                        UIManager.Open(self);
                    },
                    () => UIManager.Open(self)));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            plan.keepItems = GUILayout.Toggle(plan.keepItems, Loc.T(" 持ち物を持っていく（置いていく場合も、手紙・贈り物などの思い出の品は持っていきます）",
                " Bring your items (keepsakes such as letters and gifts always come with you)"), UIStyles.Toggle);
            plan.keepMoney = GUILayout.Toggle(plan.keepMoney, Loc.Format(" お金を持っていく（持っていかない場合は {0} から）", " Bring your money (otherwise start with {0})",
                UIStyles.Money(PlayerSystem.StarterMoney)), UIStyles.Toggle);
            plan.keepTools = GUILayout.Toggle(plan.keepTools, Loc.T(" 道具の強化を持っていく", " Keep your tool upgrades"), UIStyles.Toggle);
            plan.shortPrologue = GUILayout.Toggle(plan.shortPrologue, Loc.T(" 新しい星に着く場面を見る", " Watch the arrival on the new planet"), UIStyles.Toggle);
            GUILayout.Space(12);

            GUILayout.Label(Loc.T("星のシード（空欄ならランダム）", "Planet seed (blank = random)"), UIStyles.LabelBold);
            plan.seedText = GUILayout.TextField(plan.seedText ?? "", 32, UIStyles.TextField, GUILayout.Height(42), GUILayout.Width(480));
            GUILayout.Label(Loc.T("星の呼び名（任意。あなたが付ける名前で、住民は別の名前で呼んでいます）", "Your name for the planet (optional; residents call it something else)"), UIStyles.LabelBold);
            plan.nickname = GUILayout.TextField(plan.nickname ?? "", 24, UIStyles.TextField, GUILayout.Height(42), GUILayout.Width(480));
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("新しい星では、宇宙語も住民も地形も気候も変わります。辞書は星ごとに分かれ、この星の辞書と混ざりません。",
                "On the new planet, the alien language, residents, terrain and climate change. Dictionaries are kept per planet and never mixed."), UIStyles.LabelWrap);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("もどる", "Back"), UIStyles.Button, GUILayout.Height(52), GUILayout.Width(200)))
            {
                Close();
                UIManager.Open(new PostEndingWindow(story));
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("出発する", "Depart"), UIStyles.ButtonSelected, GUILayout.Height(52), GUILayout.Width(260)))
            {
                Close();
                story.ChooseNextPlanet(plan);
            }
            GUILayout.EndHorizontal();
        }
    }
}
