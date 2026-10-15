using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Mining
{
    /// <summary>
    /// 鍛冶屋（ResidentJob.Blacksmith）と採掘師（ResidentJob.Miner）の会話メニュー（鉱山仕様 §4.3, §8）。
    /// ・ツルハシの強化（前段階の鉱石＋お金）、炉での金属加工（料理・加工モジュールの Forge）、鉱石を見せる、買い取り（低価格）。
    /// ・宇宙語が分からなくても、鉱石を指さす・数を指で示す身振りで必要な物が分かる（§8.1）。
    /// ・鍛冶屋が引っ越しても、鍛冶屋の施設の金床（呼び鈴）から強化を頼める（§4.3：機能を恒久的に失わない）。
    /// </summary>
    public sealed class BlacksmithInteractions : IResidentInteractionProvider
    {
        private readonly MiningSystem sys;

        public BlacksmithInteractions(MiningSystem sys) { this.sys = sys; }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            if (resident == null || player == null) yield break;
            string rid = resident.Id;
            if (resident.Job == ResidentJob.Blacksmith)
            {
                yield return new ResidentInteractionOption("ツルハシを強化してもらう", "Ask to upgrade your pickaxe", () => UIManager.Open(new PickaxeUpgradeWindow(sys, player, rid)), 20);
                yield return new ResidentInteractionOption("炉で金属を加工する", "Use the forge", () => OpenForge(player), 21);
                yield return new ResidentInteractionOption("鉱石を見せる", "Show an ore", () => ShowOre(player, rid), 22);
                yield return new ResidentInteractionOption("鉱石を買い取ってもらう", "Sell ore to the smith", () => OpenBuyback(player, rid), 23);
            }
            if (resident.Job == ResidentJob.Miner || resident.Job == ResidentJob.Blacksmith)
                yield return new ResidentInteractionOption("鉱山の話を聞く", "Listen to mine talk", () => TalkAboutMine(rid), 24);
        }

        // ---------------- 金床（呼び鈴）----------------

        public void OpenAnvilMenu(PlayerState player, string facilityId)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            var smith = dir?.FindByJob(ResidentJob.Blacksmith);
            string rid = smith != null ? smith.Id : null;
            var options = new List<ChoiceOption>
            {
                new ChoiceOption(Loc.T("ツルハシの強化を頼む（呼び鈴を鳴らす）", "Ask for a pickaxe upgrade (ring the bell)"), () => UIManager.Open(new PickaxeUpgradeWindow(sys, player, rid))),
                new ChoiceOption(Loc.T("炉で金属を加工する", "Use the forge"), () => OpenForge(player, facilityId)),
            };
            if (rid != null) options.Add(new ChoiceOption(Loc.T("鉱石を買い取ってもらう", "Sell ore"), () => OpenBuyback(player, rid)));
            options.Add(new ChoiceOption(Loc.T("やめる", "Leave"), null));
            UIManager.Choose(Loc.T("鍛冶屋の金床", "Blacksmith's anvil"),
                smith != null ? null : Loc.T("金床の横に小さな呼び鈴がある。鳴らすと奥で誰かが動く気配がした。", "There's a small bell by the anvil. When rung, someone stirs in the back."), options);
        }

        public void OpenForge(PlayerState player, string facilityId = null)
        {
            var craft = Game.Instance.Service<ICraftingService>();
            if (craft == null)
            {
                UIManager.Toast(Loc.T("炉はまだ火が入っていないようだ", "The forge isn't lit yet"));
                return;
            }
            if (string.IsNullOrEmpty(facilityId))
            {
                var f = Game.Instance.Layout?.GetFacility(Uchujin.World.FacilityType.Blacksmith);
                facilityId = f != null ? f.id : "mine.forge";
            }
            craft.OpenStation(CraftStationType.Forge, facilityId, player);
        }

        // ---------------- 鉱石を見せる（「硬い」「きれい」「珍しい」を推理できる §8.1, 宇宙語仕様 §9.3）----------------

        public void ShowOre(PlayerState player, string residentId)
        {
            UIManager.PickItem(player, Loc.T("どれを見せる？", "Show which?"),
                s => s.Definition.SourceActivity == "mining",
                (slot, stack) => ReactToOre(player, residentId, stack.itemId));
        }

        private void ReactToOre(PlayerState player, string residentId, string itemId)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (dir == null) return;
            var def = ItemRegistry.Get(itemId);
            var mat = MineCatalog.Get(itemId);
            if (def == null) return;
            var item = MeaningArg.Of(Concepts.Item(itemId));
            var u = new Utterance(residentId, "mining.show_ore").Related(itemId).With(Gesture.PointAt, itemId);
            int hard = mat != null ? mat.Hardness : 1;
            if (mat != null && mat.Kind != MineNodeKind.Forage)
            {
                var hardFrame = MeaningFrame.Make(SpeechAct.Statement, Concepts.HARD).Agent(item);
                if (hard >= 3) hardFrame.Adverb(Concepts.VERY);
                else if (hard <= 1) hardFrame.Adverb(Concepts.A_LITTLE);
                u.Say(hardFrame);
            }
            if (def.Category == ItemCategory.Gem || def.HasTag(ItemTags.Shiny)) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(item));
            if (def.HasTag(MineCatalog.TagFossil) || def.HasTag(MineCatalog.TagRelic)) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.OLD).Agent(item).Adverb(Concepts.VERY));
            if (def.Rarity >= 3) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.RARE).Agent(item));
            // ツルハシ強化に使う鉱石なら「（これで）強いツルハシを作る」
            if (IsUpgradeOre(itemId))
            {
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PICKAXE, Concepts.STRONG)).Future());
                u.With(Gesture.Hold, "pickaxe");
            }
            u.Face(def.Rarity >= 3 ? Expression.Surprised : Expression.Happy);
            if (hard >= 3) u.With(Gesture.Shrug);
            dir.Speak(residentId, u);
            Game.Instance.Service<ITabletService>()?.RecordResidentObservation(residentId,
                string.Format("{0}を見せると、こつこつ叩いてから何か言った。", def.NameJa),
                string.Format("When shown the {0}, tapped it and said something.", def.NameEn));
        }

        private static bool IsUpgradeOre(string itemId)
        {
            foreach (var up in PickaxeUpgrade.Table)
                foreach (var kv in up.Ores)
                    if (kv.Key == itemId) return true;
            return false;
        }

        // ---------------- 買い取り（§11.2：商店の価値を損なわない低価格・1日上限）----------------

        public void OpenBuyback(PlayerState player, string residentId)
        {
            UIManager.PickItem(player, Loc.Format("買い取り（目安の{0}%）", "Buyback ({0}% of reference price)", Mathf.RoundToInt(MiningTuning.BuybackRate * 100)),
                s => s.Definition.SourceActivity == "mining" && s.Definition.BasePrice > 0,
                (slot, stack) =>
                {
                    int price = Mathf.Max(1, Mathf.RoundToInt(stack.Definition.BasePrice * MiningTuning.BuybackRate));
                    var opts = new List<ChoiceOption>();
                    foreach (var n in new[] { 1, 5, stack.count })
                    {
                        int count = Mathf.Min(n, stack.count);
                        if (count <= 0) continue;
                        string id = stack.itemId;
                        opts.Add(new ChoiceOption(Loc.Format("{0}個 → {1}", "{0} → {1}", count, UIStyles.Money(price * count)),
                            () => WorldAuthority.Submit(new SellToBlacksmithCommand { residentId = residentId, itemId = id, count = count }, r =>
                            {
                                if (r != null && r.Success) UIManager.Toast(Loc.Format("{0}を{1}個買い取ってもらった", "Sold {1} x {0}", ItemRegistry.NameOf(id), count));
                            })));
                    }
                    opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
                    UIManager.Choose(stack.Definition.Name, Loc.T("商店に並べた方が高く売れるかもしれない。", "It might sell for more in your shop."), opts);
                });
        }

        // ---------------- 鉱山の話（今日の鉱脈：正しい内容だけ）----------------

        public void TalkAboutMine(string residentId)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (dir == null) return;
            int floor = sys.TalkTopicFloor();
            var info = sys.GetDayInfo(floor);
            var u = new Utterance(residentId, "mining.today").With(Gesture.PointAt, "down").With(Gesture.ShowNumber, null, floor);
            if (!string.IsNullOrEmpty(info.FavoredOre))
            {
                u.Related(info.FavoredOre);
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.Item(info.FavoredOre)).Plural())
                    .At(Concepts.MINE).When(Concepts.TODAY).Adverb(Concepts.MANY));
            }
            if (info.GemDay)
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(MeaningArg.Of(Concepts.GEM, Concepts.BEAUTIFUL).Plural()).At(Concepts.MINE).Adverb(Concepts.MANY));
            if (floor >= 6) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HARD).Agent(Concepts.STONE).At(Concepts.DOWN).Adverb(Concepts.VERY));
            u.Face(info.GemDay ? Expression.Excited : Expression.Happy);
            dir.Speak(residentId, u);
        }
    }

    // =====================================================================
    // ツルハシの強化画面
    // =====================================================================

    public sealed class PickaxeUpgradeWindow : UIWindow
    {
        private readonly MiningSystem sys;
        private readonly PlayerState player;
        private readonly string residentId;
        private float hammer = -1f;
        private int hammerTarget;

        public PickaxeUpgradeWindow(MiningSystem sys, PlayerState player, string residentId)
        {
            this.sys = sys;
            this.player = player;
            this.residentId = residentId;
        }

        public override string Title => Loc.T("ツルハシの強化", "Pickaxe upgrade");
        public override Vector2 Size => new Vector2(840, 640);
        public override bool PausesTimeInSolo => true;

        public override void OnOpen()
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            int cur = Mathf.Max(1, player.Tools.GetLevel(ToolType.Pickaxe));
            var up = PickaxeUpgrade.ForTier(cur + 1);
            if (dir == null || string.IsNullOrEmpty(residentId) || up == null) return;
            // 必要な鉱石を指さし、数を指で示す（宇宙語が分からなくても伝わる）
            var u = new Utterance(residentId, "mining.pickaxe_upgrade")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.PICKAXE, Concepts.STRONG)).Future())
                .Face(Expression.Happy).With(Gesture.Hold, "pickaxe");
            foreach (var kv in up.Ores)
            {
                u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.BRING).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.Item(kv.Key)).Qty(kv.Value)));
                u.With(Gesture.PointAt, kv.Key).With(Gesture.ShowNumber, null, kv.Value);
            }
            u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GIVE).Agent(Concepts.YOU).Patient(MeaningArg.Of(Concepts.MONEY).Qty(up.Money)));
            dir.Speak(residentId, u);
        }

        public override void Update()
        {
            if (hammer < 0f) return;
            hammer += UnityEngine.Time.unscaledDeltaTime;
            if (hammer >= 1.6f)
            {
                hammer = -1f;
                var dir = Game.Instance.Service<IResidentDirectory>();
                if (dir != null && !string.IsNullOrEmpty(residentId))
                    dir.Speak(residentId, new Utterance(residentId, "mining.pickaxe_upgrade.done")
                        .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Recipient(Concepts.YOU).Patient(MeaningArg.Of(Concepts.PICKAXE, Concepts.NEW)))
                        .Face(Expression.VeryHappy).With(Gesture.OfferItem, "pickaxe"));
                UIManager.Toast(Loc.Format("ツルハシが「{0}」になった！ 硬い鉱石も掘れるようになった。", "Your pickaxe is now \"{0}\"! You can mine harder ore.", ToolTypeUtil.TierLabel(hammerTarget)), 5f);
                Close();
            }
        }

        public override void DrawContents(Rect content)
        {
            int cur = Mathf.Max(1, player.Tools.GetLevel(ToolType.Pickaxe));
            if (hammer >= 0f)
            {
                GUILayout.Space(80);
                var big = new GUIStyle(UIStyles.LabelTitle) { alignment = TextAnchor.MiddleCenter, fontSize = 44 };
                GUILayout.Label(Mathf.Repeat(hammer, 0.4f) < 0.2f ? Loc.T("カン！", "Clang!") : Loc.T("カン、", "Clang,"), big);
                GUILayout.Space(20);
                UIStyles.ProgressBar(GUILayoutUtility.GetRect(content.width, 26), hammer / 1.6f, UIStyles.AccentWarm);
                return;
            }
            GUILayout.Label(Loc.Format("今のツルハシ：{0}（段階{1}）", "Current pickaxe: {0} (tier {1})", ToolTypeUtil.TierLabel(cur), cur), UIStyles.LabelBold);
            var up = PickaxeUpgrade.ForTier(cur + 1);
            if (up == null)
            {
                GUILayout.Label(Loc.T("これ以上は強くできない。最高のツルハシだ！", "It can't get any stronger. The finest pickaxe!"), UIStyles.LabelWrap);
                return;
            }
            GUILayout.Label(Loc.Format("強化後：{0}（段階{1}）", "After: {0} (tier {1})", ToolTypeUtil.TierLabel(up.TargetTier), up.TargetTier), UIStyles.Label);
            var newly = new List<string>();
            foreach (var m in MineCatalog.All) if (m.Hardness == up.TargetTier && !m.FromRockOnly) newly.Add(ItemRegistry.NameOf(m.Id));
            if (newly.Count > 0) GUILayout.Label(Loc.T("新しく掘れる：", "Newly minable: ") + string.Join(Loc.T("、", ", "), newly), UIStyles.LabelWrap);
            GUILayout.Label(Loc.T("これまでの岩や鉱石も、少ない回数で掘れるようになる。", "Earlier rocks and ores break in fewer hits."), UIStyles.LabelDim);
            GUILayout.Space(10);
            GUILayout.Label(Loc.T("必要なもの", "Required"), UIStyles.LabelBold);
            bool ok = player.Wallet.CanAfford(up.Money);
            GUILayout.Label((ok ? "○ " : "× ") + UIStyles.Money(up.Money) + Loc.Format("（所持 {0}）", " (have {0})", UIStyles.Money(player.Wallet.money)), UIStyles.Label);
            foreach (var kv in up.Ores)
            {
                int have = player.Inventory.CountOf(kv.Key);
                bool enough = have >= kv.Value;
                ok &= enough;
                GUILayout.BeginHorizontal();
                var icon = GUILayoutUtility.GetRect(40, 40, GUILayout.Width(40), GUILayout.Height(40));
                UIStyles.ItemIcon(icon, new ItemStack(kv.Key, 1));
                GUILayout.Label((enough ? "○ " : "× ") + Loc.Format("{0} ×{1}（所持 {2}）", "{0} x{1} (have {2})", ItemRegistry.NameOf(kv.Key), kv.Value, have), UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            GUILayout.FlexibleSpace();
            GUI.enabled = ok;
            if (GUILayout.Button(Loc.T("強化を頼む", "Request upgrade"), UIStyles.Button, GUILayout.Height(52)))
            {
                int target = up.TargetTier;
                sys.RequestPickaxeUpgrade(player, residentId, target, success =>
                {
                    if (!success) return;
                    hammer = 0f;
                    hammerTarget = target;
                });
            }
            GUI.enabled = true;
        }
    }
}
