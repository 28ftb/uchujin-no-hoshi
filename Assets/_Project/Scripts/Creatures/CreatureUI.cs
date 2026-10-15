using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>飼っている生き物に話しかけた時のお世話メニュー（なでる・餌・名前・様子を見る）。</summary>
    public static class CreatureCareMenu
    {
        public static void Open(CreatureSystem sys, OwnedCreature c)
        {
            if (sys == null || c == null) return;
            var player = Game.Instance.LocalPlayer;
            var opts = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("なでる", "Pet"), () => sys.Pet(c)),
                new ChoiceOption(Loc.T("餌をあげる", "Feed"), () => FeedPicker(sys, c, player)),
                new ChoiceOption(Loc.T("名前を付ける", "Name"), () => NamePrompt(sys, c)),
                new ChoiceOption(Loc.T("様子を見る・居場所を変える", "Details / move"), () => UIManager.Open(new CreatureManagerWindow(sys, c.uid))),
                new ChoiceOption(Loc.T("やめる", "Cancel"), null)
            };
            UIManager.Choose(sys.DisplayName(c), null, opts);
        }

        public static void FeedPicker(CreatureSystem sys, OwnedCreature c, PlayerState player)
        {
            if (player == null) return;
            UIManager.PickItem(player, Loc.Format("{0}に何をあげる？", "Feed {0} what?", sys.DisplayName(c)),
                s => CreatureDatabase.IsEdibleForCreature(s.Definition), (slot, stack) => sys.Feed(c, slot));
        }

        public static void NamePrompt(CreatureSystem sys, OwnedCreature c)
        {
            UIManager.InputText(Loc.T("名前を付ける", "Name"), Loc.T("この子の名前（空欄なら種類の呼び名）", "Its name (blank = species name)"), c.nickname ?? "", n => sys.Rename(c, n));
        }
    }

    /// <summary>飼っている生き物の一覧・詳細・配置変更（タブレットの図鑑アプリとウィンドウの両方で使う）。</summary>
    public sealed class OwnedCreaturesPanel
    {
        private readonly CreatureSystem sys;
        public string SelectedUid;
        private Vector2 listScroll;
        private Vector2 detailScroll;

        public OwnedCreaturesPanel(CreatureSystem sys) { this.sys = sys; }

        public void Draw(float height)
        {
            var list = sys.LocalOwnedList();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(380));
            GUILayout.Label(Loc.Format("飼っている生き物：{0}匹（連れて歩く {1}/{2}）", "Your creatures: {0} (with you {1}/{2})", list.Count, sys.CompanionCount(), CreatureSystem.MaxCompanions), UIStyles.LabelBold);
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(Mathf.Max(200, height - 50)));
            if (list.Count == 0) GUILayout.Label(Loc.T("まだ生き物はいません。網で捕まえたり、住民から迎えたりしよう。", "No creatures yet. Catch some with a net, or get one from a resident."), UIStyles.LabelWrap);
            foreach (var c in list)
            {
                var sp = CreatureDatabase.Get(c.speciesId);
                string label = sys.DisplayName(c) + "  " + (sp != null ? "No." + sp.NumberLabel : "") + "\n" + PlacementLabel(c);
                if (GUILayout.Button(label, c.uid == SelectedUid ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(52))) SelectedUid = c.uid;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            var sel = sys.Find(SelectedUid);
            if (sel == null && list.Count > 0) { sel = list[0]; SelectedUid = sel.uid; }
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(Mathf.Max(200, height - 10)));
            if (sel != null) DrawDetail(sel);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private string PlacementLabel(OwnedCreature c)
        {
            if (c.placement == CreaturePlacement.Deposited)
            {
                bool here = Game.Instance.Planet != null && c.residentWorldId == Game.Instance.Planet.worldId;
                if (System.Enum.TryParse(c.targetId, out CreaturePlacement inner))
                    return Loc.Format("{0}（{1}）", "{0} ({1})", here ? Loc.T("この星", "this planet") : Loc.T("ほかの星", "another planet"), PlacementText.Label(inner));
                return PlacementText.Label(c.placement);
            }
            return PlacementText.Label(c.placement);
        }

        private void DrawDetail(OwnedCreature c)
        {
            var sp = CreatureDatabase.Get(c.speciesId);
            if (sp == null) return;
            var entry = sys.GetEntry(sp.Id, false);
            GUILayout.BeginHorizontal();
            var sw = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
            UIStyles.FillRect(sw, CreatureVisualBuilder.Vary(sp.Main, c.colorVariant, c.hueShift));
            UIStyles.FillRect(new Rect(sw.x + 40, sw.y + 40, 20, 20), CreatureVisualBuilder.Vary(sp.Accent, c.colorVariant, c.hueShift * 0.5f));
            GUILayout.BeginVertical();
            GUILayout.Label(sys.DisplayName(c), UIStyles.LabelTitle);
            GUILayout.Label(Loc.Format("No.{0} {1}　Lv{2}", "No.{0} {1}  Lv{2}", sp.NumberLabel, sys.GetSpeciesName(sp.Id), c.level), UIStyles.Label);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            int next = c.level >= CreatureSystem.MaxCreatureLevel ? 0 : CreatureSystem.ExpForLevel[c.level + 1];
            var r = GUILayoutUtility.GetRect(300, 18);
            UIStyles.ProgressBar(r, next > 0 ? c.exp / (float)next : 1f, UIStyles.Accent);
            GUILayout.Label(Loc.T("成長", "Growth"), UIStyles.LabelDim);
            r = GUILayoutUtility.GetRect(300, 18);
            UIStyles.ProgressBar(r, c.affection / 100f, new Color(1f, 0.6f, 0.75f));
            GUILayout.Label(Loc.Format("なつき度 {0}", "Affection {0}", c.affection), UIStyles.LabelDim);

            GUILayout.Label(Loc.Format("性格：{0}　大きさ：×{1:0.00}{2}", "Personality: {0}  Size: x{1:0.00}{2}",
                PersonalityText.Label((CreaturePersonality)c.personality), c.size,
                c.colorVariant == 3 ? Loc.T("　珍しい色", "  Rare color") : c.colorVariant == 1 ? Loc.T("　淡い色", "  Pale") : c.colorVariant == 2 ? Loc.T("　濃い色", "  Deep color") : ""), UIStyles.Label);
            GUILayout.Label(Loc.Format("好きな食べ物：{0}", "Favorite food: {0}", c.favoriteKnown ? CreatureDatabase.FoodLabel(c.favoriteFood) : "？"), UIStyles.Label);

            // 得意なこと：観察して分かったものだけ表示する（図鑑の事実に基づく）
            var abil = sys.ComputeAbilities(c);
            if (abil.Count == 0) GUILayout.Label(Loc.T("得意なこと：のんびり暮らすこと", "Specialty: living a relaxed life"), UIStyles.Label);
            else
            {
                var parts = new List<string>();
                foreach (var a in abil)
                {
                    bool known = entry != null && entry.IsRevealed(a.ability);
                    parts.Add(known ? AbilityText.Label(a.ability) + " Lv" + a.level : "？？？");
                }
                GUILayout.Label(Loc.Format("得意なこと：{0}", "Specialties: {0}", string.Join(Loc.T("、", ", "), parts.ToArray())), UIStyles.LabelWrap);
                if (parts.Contains("？？？"))
                    GUILayout.Label(Loc.T("（働く様子を観察したり、生き物名人に見せたりすると分かる）", "(Watch it work or show it to the creature expert to find out)"), UIStyles.LabelDim);
            }
            GUILayout.Label(OriginText(c), UIStyles.LabelDim);
            GUILayout.Label(Loc.Format("居場所：{0}", "Location: {0}", PlacementLabel(c)), UIStyles.LabelBold);

            bool deposited = c.placement == CreaturePlacement.Deposited;
            if (deposited)
            {
                GUILayout.Label(Loc.T("ほかの星に預けている子です。その星に行くと連れて帰れます。", "This one lives on another planet. Visit it to bring it back."), UIStyles.LabelWrap);
                return;
            }
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            PlaceButton(c, CreaturePlacement.Companion);
            PlaceButton(c, CreaturePlacement.Storage);
            PlaceButton(c, CreaturePlacement.Farm);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            PlaceButton(c, CreaturePlacement.Home);
            PlaceButton(c, CreaturePlacement.RocketWorkshop);
            PlaceButton(c, CreaturePlacement.Shop);
            if (sp.Id == SharedItemIds.SpeciesManepi) PlaceButton(c, CreaturePlacement.MessagePlaza);
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("※ 器具や住処の家具に入れるのは、その器具・家具を調べて行います。", "* To put it in a device or habitat furniture, interact with that item."), UIStyles.LabelDim);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("なでる", "Pet"), UIStyles.ButtonSmall)) sys.Pet(c);
            if (GUILayout.Button(Loc.T("餌をあげる", "Feed"), UIStyles.ButtonSmall)) CreatureCareMenu.FeedPicker(sys, c, Game.Instance.LocalPlayer);
            if (GUILayout.Button(Loc.T("名前を付ける", "Name"), UIStyles.ButtonSmall)) CreatureCareMenu.NamePrompt(sys, c);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("ケースに入れる（持ち物へ）", "Put in a case"), UIStyles.ButtonSmall))
            {
                var cc = c;
                UIManager.Confirm(Loc.T("ケースに入れる", "Put in case"), Loc.Format("{0}をケースに入れて持ち物にしますか？\n（商店に並べたり、贈ったり、友達に譲ったりできます）", "Put {0} in a case?\n(You can sell, gift or hand it to a friend)", sys.DisplayName(cc)), () => sys.PutInCase(cc));
            }
            if (GUILayout.Button(Loc.T("逃がす", "Release"), UIStyles.ButtonSmall))
            {
                var cc = c;
                UIManager.Confirm(Loc.T("逃がす", "Release"), Loc.Format("{0}を野に帰しますか？（戻ってきません）", "Release {0} to the wild? (It won't come back)", sys.DisplayName(cc)), () => { sys.Release(cc); SelectedUid = null; });
            }
            GUILayout.EndHorizontal();
        }

        private void PlaceButton(OwnedCreature c, CreaturePlacement p)
        {
            bool current = c.placement == p;
            if (GUILayout.Button(PlacementText.Label(p), current ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(40)) && !current)
            {
                if (sys.RequestPlacement(c.uid, p, null, null))
                    UIManager.Toast(Loc.Format("{0}：{1}", "{0}: {1}", sys.DisplayName(c), PlacementText.Label(p)), 2f);
            }
        }

        private string OriginText(OwnedCreature c)
        {
            switch (c.origin)
            {
                case "caught":
                    return string.IsNullOrEmpty(c.caughtAreaName)
                        ? Loc.Format("{0}日目に捕まえた", "Caught on day {0}", c.caughtDay)
                        : Loc.Format("{0}日目に{1}で捕まえた", "Caught on day {0} at {1}", c.caughtDay, c.caughtAreaName);
                case "gift": return Loc.Format("{0}日目に迎えた", "Joined on day {0}", c.caughtDay);
                case "bred": return Loc.Format("{0}日目に生まれた", "Born on day {0}", c.caughtDay);
                default: return Loc.Format("{0}日目から一緒", "With you since day {0}", c.caughtDay);
            }
        }
    }

    /// <summary>飼っている生き物の管理画面（一覧・配置変更・名前を付ける・逃がす）。</summary>
    public sealed class CreatureManagerWindow : UIWindow
    {
        private readonly OwnedCreaturesPanel panel;

        public CreatureManagerWindow(CreatureSystem sys, string selectUid = null)
        {
            panel = new OwnedCreaturesPanel(sys) { SelectedUid = selectUid };
        }

        public override string Title => Loc.T("飼っている生き物", "Your creatures");
        public override Vector2 Size => new Vector2(1180, 780);
        public override bool PausesTimeInSolo => true;

        public override void DrawContents(Rect content)
        {
            panel.Draw(content.height - 10);
        }
    }

    /// <summary>
    /// 生き物図鑑アプリ（タブレット "bestiary"）。PlayerPlanetKnowledge。
    /// 見た・捕まえた・観察した習性（事実）・プレイヤーのメモ。宇宙語の呼び名は書かない。ゲームがメモを正解に書き換えることはない。
    /// </summary>
    public sealed class BestiaryApp : ITabletApp
    {
        private readonly CreatureSystem sys;
        private readonly OwnedCreaturesPanel ownedPanel;
        private int tab;
        private string selected;
        private Vector2 listScroll;
        private Vector2 detailScroll;

        public BestiaryApp(CreatureSystem sys)
        {
            this.sys = sys;
            ownedPanel = new OwnedCreaturesPanel(sys);
        }

        public string AppId => "bestiary";
        public string Title => Loc.T("生き物図鑑", "Bestiary");
        public int Order => 40;
        public string IconGlyph => Loc.T("生", "Bz");
        public Color IconColor => new Color(0.55f, 0.85f, 0.55f);
        public bool IsAvailable => true;

        public void OnOpen() { }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            string[] tabs = { Loc.T("図鑑", "Species"), Loc.T("飼っている生き物", "Your creatures"), Loc.T("記録", "Records") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], tab == i ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(40))) tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            float h = area.height - 60;
            switch (tab)
            {
                case 0: DrawSpecies(h); break;
                case 1: ownedPanel.Draw(h); break;
                default: DrawRecords(); break;
            }
            GUILayout.EndArea();
        }

        private void DrawSpecies(float h)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(300));
            GUILayout.Label(Loc.Format("見つけた {0}/{1}　捕まえた {2}", "Found {0}/{1}  Caught {2}", sys.DiscoveredSpeciesCount(), CreatureDatabase.All.Count, sys.CaughtSpeciesCount()), UIStyles.LabelBold);
            listScroll = GUILayout.BeginScrollView(listScroll, GUILayout.Height(h - 40));
            foreach (var sp in CreatureDatabase.All)
            {
                var e = sys.GetEntry(sp.Id, false);
                bool seen = e != null && e.Seen;
                bool caught = e != null && (e.caughtCount > 0 || e.obtainedCount > 0);
                string mark = caught ? "●" : seen ? "○" : "　";
                string name = seen ? sys.GetSpeciesName(sp.Id) : "？？？";
                if (GUILayout.Button($"{mark} No.{sp.NumberLabel}  {name}", selected == sp.Id ? UIStyles.ButtonSelected : UIStyles.ButtonSmall, GUILayout.Height(34)))
                {
                    selected = sp.Id;
                    GUIUtility.keyboardControl = 0;
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical();
            detailScroll = GUILayout.BeginScrollView(detailScroll, GUILayout.Height(h));
            var sel = CreatureDatabase.Get(selected);
            if (sel == null) GUILayout.Label(Loc.T("左から生き物を選んでください。", "Choose a creature on the left."), UIStyles.LabelDim);
            else DrawSpeciesPage(sel);
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawSpeciesPage(CreatureSpecies sp)
        {
            var e = sys.GetEntry(sp.Id, false);
            if (e == null || !e.Seen)
            {
                GUILayout.Label(Loc.Format("No.{0}　？？？", "No.{0}  ???", sp.NumberLabel), UIStyles.LabelTitle);
                GUILayout.Label(Loc.T("まだ見つけていない。", "Not found yet."), UIStyles.LabelDim);
                return;
            }
            bool caught = e.caughtCount > 0 || e.obtainedCount > 0;
            GUILayout.BeginHorizontal();
            var sw = GUILayoutUtility.GetRect(72, 72, GUILayout.Width(72), GUILayout.Height(72));
            UIStyles.FillRect(sw, sp.Main);
            UIStyles.FillRect(new Rect(sw.x + 46, sw.y + 46, 22, 22), sp.Accent);
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.Format("No.{0}　{1}", "No.{0}  {1}", sp.NumberLabel, sys.GetSpeciesName(sp.Id)), UIStyles.LabelTitle);
            if (GUILayout.Button(Loc.T("呼び名を変える", "Rename"), UIStyles.ButtonSmall, GUILayout.Width(200)))
            {
                var entry = e;
                UIManager.InputText(Loc.T("呼び名", "Name"), Loc.T("自分でつけた呼び名（空欄で元に戻す）", "Your own name for it (blank to reset)"), entry.playerName ?? "", n => entry.playerName = string.IsNullOrEmpty(n) ? null : n.Trim());
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Label(Loc.Format("見た回数 {0}　捕まえた {1}　迎えた {2}", "Seen {0}  Caught {1}  Obtained {2}", e.seenCount, e.caughtCount, e.obtainedCount), UIStyles.Label);
            if (e.firstSeenDay >= 0) GUILayout.Label(Loc.Format("初めて見た日：{0}日目", "First seen: day {0}", e.firstSeenDay), UIStyles.LabelDim);
            if (e.firstCaughtDay >= 0) GUILayout.Label(Loc.Format("初めて捕まえた日：{0}日目", "First caught: day {0}", e.firstCaughtDay), UIStyles.LabelDim);
            if (e.biggestSize > 0) GUILayout.Label(Loc.Format("いちばん大きかった子：×{0:0.00}", "Biggest: x{0:0.00}", e.biggestSize), UIStyles.LabelDim);
            if (e.areas.Count > 0) GUILayout.Label(Loc.Format("見た場所：{0}", "Seen at: {0}", string.Join(Loc.T("、", ", "), e.areas.ToArray())), UIStyles.LabelWrap);
            string hours = HoursText(e.hourMask);
            if (!string.IsNullOrEmpty(hours)) GUILayout.Label(Loc.Format("見た時間帯：{0}", "Seen during: {0}", hours), UIStyles.LabelWrap);
            if (e.weathers.Count > 0) GUILayout.Label(Loc.Format("その時の天気：{0}", "Weather then: {0}", string.Join(Loc.T("／", " / "), e.weathers.ToArray())), UIStyles.LabelWrap);

            if (caught)
            {
                GUILayout.Space(4);
                GUILayout.Label(Loc.T("近くで見た特徴", "Up close"), UIStyles.LabelBold);
                GUILayout.Label(Loc.T(sp.HabitJa, sp.HabitEn), UIStyles.LabelWrap);
            }
            if (e.clueKnown)
            {
                GUILayout.Label(Loc.T("名人の身振りから分かった捕まえ方", "How to catch (from the expert's gestures)"), UIStyles.LabelBold);
                GUILayout.Label(Loc.T(sp.ClueJa, sp.ClueEn), UIStyles.LabelWrap);
            }

            GUILayout.Space(4);
            GUILayout.Label(Loc.T("観察した行動", "Observed behavior"), UIStyles.LabelBold);
            if (e.behaviors.Count == 0) GUILayout.Label(Loc.T("（まだない。じっと観察してみよう）", "(None yet. Try watching closely.)"), UIStyles.LabelDim);
            foreach (var b in e.behaviors)
            {
                var fact = CreatureDatabase.GetFact(b.key);
                string text = fact != null ? fact.Text : Loc.T("見慣れない様子", "Something unfamiliar");
                GUILayout.Label(Loc.Format("・{0}（{1}日目）", "- {0} (day {1})", text, b.value), UIStyles.LabelWrap);
            }
            if (e.revealedAbilities.Count > 0)
            {
                var parts = new List<string>();
                foreach (var a in e.revealedAbilities) parts.Add(AbilityText.Label((CreatureAbility)a));
                GUILayout.Label(Loc.Format("得意そうなこと：{0}", "Seems good at: {0}", string.Join(Loc.T("、", ", "), parts.ToArray())), UIStyles.LabelWrap);
            }

            GUILayout.Space(6);
            GUILayout.Label(Loc.T("メモ（自由に書けます）", "Notes (write freely)"), UIStyles.LabelBold);
            e.memo = GUILayout.TextArea(e.memo ?? "", UIStyles.TextArea, GUILayout.Height(140));
        }

        private static string HoursText(int mask)
        {
            if (mask == 0) return null;
            var parts = new List<string>();
            DayPart last = (DayPart)(-1);
            for (int h = 0; h < 24; h++)
            {
                if ((mask & (1 << h)) == 0) continue;
                var p = DayPartUtil.FromHour(h);
                if (p == last) continue;
                last = p;
                string l = DayPartUtil.Label(p);
                if (!parts.Contains(l)) parts.Add(l);
            }
            return string.Join(Loc.T("、", ", "), parts.ToArray());
        }

        private void DrawRecords()
        {
            var p = Game.Instance.LocalPlayer;
            GUILayout.Label(Loc.Format("生き物取り Lv{0}", "Creature catching Lv{0}", sys.CatchLevel), UIStyles.LabelTitle);
            int toNext = sys.CatchExpToNext();
            if (toNext > 0) GUILayout.Label(Loc.Format("次のレベルまで {0}", "{0} to next level", toNext), UIStyles.LabelDim);
            GUILayout.Label(Loc.Format("網：{0}", "Net: {0}", ToolTypeUtil.TierLabel(p != null ? p.Tools.GetLevel(ToolType.CatchNet) : 1)), UIStyles.Label);
            GUILayout.Label(Loc.Format("捕まえた数（通算）：{0}", "Total caught: {0}", sys.TotalCaught), UIStyles.Label);
            GUILayout.Label(Loc.Format("見つけた種類：{0}/{1}　捕まえた種類：{2}", "Species found: {0}/{1}  caught: {2}", sys.DiscoveredSpeciesCount(), CreatureDatabase.All.Count, sys.CaughtSpeciesCount()), UIStyles.Label);
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("身につけたコツ", "Techniques"), UIStyles.LabelBold);
            bool any = false;
            if (sys.HasTechnique(CreatureSystem.TechSneak)) { any = true; GUILayout.Label(Loc.T("・忍び足（気づかれにくい）", "- Soft steps (less noticed)"), UIStyles.Label); }
            if (sys.HasTechnique(CreatureSystem.TechNet)) { any = true; GUILayout.Label(Loc.T("・網さばき（捕まえやすい）", "- Net handling (easier catches)"), UIStyles.Label); }
            if (sys.HasTechnique(CreatureSystem.TechBait)) { any = true; GUILayout.Label(Loc.T("・餌の使い方（餌が長持ち）", "- Bait craft (longer-lasting bait)"), UIStyles.Label); }
            if (sys.HasTechnique(CreatureSystem.TechObserve)) { any = true; GUILayout.Label(Loc.T("・観察のコツ（隠れた生き物に気づく）", "- Keen eye (spot hidden creatures)"), UIStyles.Label); }
            if (!any) GUILayout.Label(Loc.T("（生き物名人の実演を見ると身につく）", "(Learn by watching the creature expert)"), UIStyles.LabelDim);
            GUILayout.Space(8);
            if (GUILayout.Button(Loc.T("飼っている生き物の管理画面を開く", "Open creature manager"), UIStyles.Button, GUILayout.Width(420)))
                UIManager.Open(new CreatureManagerWindow(sys));
        }
    }
}
