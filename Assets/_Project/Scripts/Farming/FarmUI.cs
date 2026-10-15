using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Farming
{
    /// <summary>
    /// タブレット「農場」アプリ（AppId "farm"）：区画の状態・生物の作業記録・作物の記録・農場の設定。
    /// </summary>
    public sealed class FarmTabletApp : ITabletApp
    {
        private readonly FarmingSystem sys;
        private int tab;
        private Vector2 scroll;

        public FarmTabletApp(FarmingSystem sys) { this.sys = sys; }

        public string AppId => "farm";
        public string Title => Loc.T("農場", "Farm");
        public int Order => 30;
        public string IconGlyph => Loc.T("農", "Fm");
        public Color IconColor => new Color(0.6f, 0.85f, 0.4f);
        public bool IsAvailable => true;
        public void OnOpen() { }

        public void Draw(Rect area)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            string[] tabs = { Loc.T("区画", "Plots"), Loc.T("生き物", "Creatures"), Loc.T("作物の記録", "Crop log"), Loc.T("農場", "Farm") };
            for (int i = 0; i < tabs.Length; i++)
                if (GUILayout.Button(tabs[i], tab == i ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(40))) tab = i;
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(area.height - 56));
            switch (tab)
            {
                case 0: DrawPlots(area.width - 30); break;
                case 1: DrawCreatures(); break;
                case 2: DrawRecords(); break;
                default: DrawFarm(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ---------------- 区画 ----------------

        private void DrawPlots(float width)
        {
            var b = sys.UnlockedBounds();
            float cell = Mathf.Clamp(Mathf.Min((width - 20) / Mathf.Max(1, b.width), 420f / Mathf.Max(1, b.height)), 8f, 30f);
            int planted = 0, ripe = 0, dry = 0, tilled = 0;
            foreach (var p in sys.Plots)
            {
                if (p.HasCrop) { planted++; if (p.ripe) ripe++; if (!p.watered) dry++; }
                else if (p.tilled) tilled++;
            }
            GUILayout.Label(Loc.Format("作物 {0}マス（実り {1}・乾いている {2}）　空いた畑 {3}マス　広さ {4}×{5}", "Crops {0} (ripe {1}, dry {2})  Empty plots {3}  Size {4}x{5}", planted, ripe, dry, tilled, b.width, b.height), UIStyles.Label);
            var r = GUILayoutUtility.GetRect(b.width * cell, b.height * cell);
            UIStyles.FillRect(r, new Color(0.25f, 0.4f, 0.22f));
            for (int x = b.xMin; x < b.xMax; x++)
                for (int z = b.yMin; z < b.yMax; z++)
                {
                    // 北（+z）が上
                    var cr = new Rect(r.x + (x - b.xMin) * cell + 1, r.y + (b.yMax - 1 - z) * cell + 1, cell - 2, cell - 2);
                    var p = sys.GetPlot(x, z);
                    var h = sys.HabitatAt(x, z);
                    Color c;
                    if (h != null) { var hd = FarmCatalog.Habitat(h.itemId); c = hd != null ? hd.Color : Color.cyan; }
                    else if (p == null) c = new Color(0.3f, 0.48f, 0.26f);
                    else if (p.HasCrop)
                    {
                        var def = FarmCatalog.Get(p.cropKey);
                        c = p.ripe && def != null ? def.Color : Color.Lerp(new Color(0.35f, 0.5f, 0.25f), new Color(0.3f, 0.8f, 0.3f), def != null ? p.growth / Mathf.Max(1, def.GrowDays) : 0.5f);
                        if (!p.watered) c = Color.Lerp(c, new Color(0.7f, 0.6f, 0.4f), 0.35f);
                    }
                    else if (p.tilled) c = p.watered ? new Color(0.32f, 0.22f, 0.14f) : new Color(0.58f, 0.43f, 0.28f);
                    else c = new Color(0.5f, 0.44f, 0.32f);
                    UIStyles.FillRect(cr, c);
                    if (p != null && p.HasCrop && p.ripe && cell >= 12) GUI.Label(cr, "★", UIStyles.LabelSmall);
                }
            GUILayout.Space(6);
            GUILayout.Label(Loc.T("色：濃い茶＝水やり済み　薄茶＝乾いた畑　緑＝育ち中（明るいほど実りが近い）　作物の色＋★＝収穫できる　その他の色＝住み場所",
                "Dark brown = watered, light brown = dry, green = growing, crop color + ★ = ready, other colors = creature homes"), UIStyles.LabelDim);
        }

        // ---------------- 生き物 ----------------

        private static string ActionLabel(string a)
        {
            switch (a)
            {
                case "water": return Loc.T("水やり", "watered");
                case "splash": return Loc.T("水しぶき", "splashed");
                case "till": return Loc.T("耕した", "tilled");
                case "sow": return Loc.T("種まき", "sowed");
                case "harvest": return Loc.T("収穫", "harvested");
                case "carry": return Loc.T("倉庫へ運んだ", "carried to storage");
                case "glow": return Loc.T("夜の畑を照らした", "lit the field");
                case "soil": return Loc.T("土を豊かにした", "enriched soil");
                case "pollinate": return Loc.T("受粉", "pollinated");
                case "heat": return Loc.T("温めた", "warmed");
                case "cool": return Loc.T("冷やした", "cooled");
                case "seedcollect": return Loc.T("種を集めた", "collected seeds");
                default: return a;
            }
        }

        private static string ComboLabel(string key)
        {
            switch (key)
            {
                case "combo.water_soil": return Loc.T("水やり＋土づくり：品質の高い作物が育ちやすい", "Watering + soil care: higher quality crops");
                case "combo.till_sow": return Loc.T("耕す→種まき：耕したすぐ後に種がまかれる", "Till → sow: seeds go in right after tilling");
                case "combo.harvest_carry": return Loc.T("収穫→運搬：収穫物がそのまま倉庫へ", "Harvest → carry: crops go straight to storage");
                case "combo.pollinate_glow": return Loc.T("夜の受粉＋光：珍しい花色が出ることがある", "Night pollination + light: rare flower colors");
                default: return key;
            }
        }

        private void DrawCreatures()
        {
            var cs = Game.Instance.Service<ICreatureService>();
            var local = Game.Instance.LocalPlayer;
            var farm = sys.CreaturesOnFarm();
            GUILayout.Label(Loc.Format("農場で暮らす生き物：{0}匹", "Creatures living on the farm: {0}", farm.Count), UIStyles.LabelBold);
            if (farm.Count == 0) GUILayout.Label(Loc.T("生き物を農場に住まわせると、水やり・耕す・種まき・収穫・運搬などを手伝ってくれる。住み場所を置くとその周りで働く。", "Creatures on the farm help with watering, tilling, sowing, harvesting and carrying. Place homes and they'll work around them."), UIStyles.LabelWrap);
            foreach (var v in farm)
            {
                var h = sys.GetHabitat(v.PlacementTargetId);
                var hd = h != null ? FarmCatalog.Habitat(h.itemId) : null;
                GUILayout.BeginHorizontal(UIStyles.Box);
                GUILayout.Label(Loc.Format("{0}（{1}）Lv{2}　住み場所：{3}", "{0} ({1}) Lv{2}  Home: {3}", v.Nickname, cs != null ? cs.GetSpeciesName(v.SpeciesId) : v.SpeciesId, v.Level, hd != null ? Loc.T(hd.NameJa, hd.NameEn) : Loc.T("畑のどこか", "around the field")), UIStyles.Label);
                if (local != null && v.OwnerId == local.PlayerId && cs != null && sys.Habitats.Count > 0)
                {
                    if (GUILayout.Button(Loc.T("住み場所を変える", "Change home"), UIStyles.ButtonSmall, GUILayout.Width(200)))
                    {
                        var opts = new List<ChoiceOption>();
                        foreach (var hh in sys.Habitats)
                        {
                            var d = FarmCatalog.Habitat(hh.itemId);
                            var target = hh;
                            var uid = v.Uid;
                            opts.Add(new ChoiceOption((d != null ? Loc.T(d.NameJa, d.NameEn) : hh.itemId) + " (" + hh.x + "," + hh.z + ")", () => cs.SetPlacement(uid, CreaturePlacement.Farm, target.id)));
                        }
                        opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
                        UIManager.Choose(Loc.T("どこに住む？", "Where should it live?"), null, opts);
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (cs != null && local != null)
            {
                var others = new List<CreatureView>();
                foreach (var v in cs.GetOwned(local.PlayerId))
                    if (v.Placement != CreaturePlacement.Farm && v.Placement != CreaturePlacement.Deposited) others.Add(v);
                if (others.Count > 0)
                {
                    GUILayout.Space(6);
                    GUILayout.Label(Loc.T("農場に住まわせる", "Move to the farm"), UIStyles.LabelBold);
                    GUILayout.BeginHorizontal();
                    int i = 0;
                    foreach (var v in others)
                    {
                        var uid = v.Uid;
                        if (GUILayout.Button(v.Nickname, UIStyles.ButtonSmall)) cs.SetPlacement(uid, CreaturePlacement.Farm, null);
                        if (++i % 4 == 0) { GUILayout.EndHorizontal(); GUILayout.BeginHorizontal(); }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("見つけた協力作業", "Teamwork discovered"), UIStyles.LabelBold);
            if (sys.Combos.Count == 0) GUILayout.Label(Loc.T("（まだない。いろいろな生き物を近くに住まわせてみよう）", "(None yet. Try housing different creatures near each other.)"), UIStyles.LabelDim);
            foreach (var k in sys.Combos) GUILayout.Label("・" + ComboLabel(k), UIStyles.LabelWrap);
            GUILayout.Space(8);
            GUILayout.Label(Loc.T("作業の記録", "Work log"), UIStyles.LabelBold);
            var log = sys.WorkLog;
            int shown = 0;
            for (int i = log.Count - 1; i >= 0 && shown < 25; i--, shown++)
            {
                var e = log[i];
                GUILayout.Label(Loc.Format("{0}日目 {1}時　{2}：{3} ×{4}", "Day {0} {1}:00  {2}: {3} x{4}", e.day, e.hour, e.creatureName, ActionLabel(e.action), e.count), UIStyles.LabelSmall);
            }
            if (log.Count == 0) GUILayout.Label(Loc.T("（まだない）", "(none yet)"), UIStyles.LabelDim);
        }

        // ---------------- 作物の記録 ----------------

        private void DrawRecords()
        {
            GUILayout.Label(Loc.Format("この星で収穫した数（自分）：{0}", "Harvested on this planet (you): {0}", sys.TotalHarvested), UIStyles.LabelBold);
            bool any = false;
            foreach (var c in FarmCatalog.Crops)
            {
                CropRecord rec = null;
                foreach (var r in sys.Records) if (r.cropKey == c.Key) rec = r;
                if (rec == null) continue;
                any = true;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var sw = GUILayoutUtility.GetRect(28, 28, GUILayout.Width(28), GUILayout.Height(28));
                UIStyles.FillRect(sw, c.Color);
                string extra = "";
                if (rec.giants > 0) extra += Loc.Format("　巨大 {0}", "  giant {0}", rec.giants);
                if (rec.glowing > 0) extra += Loc.Format("　光る {0}", "  glowing {0}", rec.glowing);
                if (rec.rareColors > 0) extra += Loc.Format("　珍しい色 {0}", "  rare color {0}", rec.rareColors);
                if (rec.byCreatures > 0) extra += Loc.Format("　生き物が収穫 {0}", "  by creatures {0}", rec.byCreatures);
                GUILayout.Label(Loc.Format("{0}：{1}個　最高 {2}{3}", "{0}: {1}  best {2}{3}", c.Name, rec.harvested, rec.bestQuality > 0 ? ItemStack.QualityStars(rec.bestQuality) : Loc.T("ふつう", "basic"), extra), UIStyles.Label);
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("（まだ収穫していない）", "(nothing harvested yet)"), UIStyles.LabelDim);
        }

        // ---------------- 農場 ----------------

        private void DrawFarm()
        {
            var p = Game.Instance.LocalPlayer;
            GUILayout.Label(Loc.Format("農業 Lv{0}", "Farming Lv{0}", sys.Level), UIStyles.LabelTitle);
            int toNext = sys.ExpToNext();
            if (toNext > 0) GUILayout.Label(Loc.Format("次のレベルまで {0}", "{0} to next level", toNext), UIStyles.LabelDim);
            if (p != null)
                GUILayout.Label(Loc.Format("クワ：{0}　じょうろ：{1}　カマ：{2}", "Hoe: {0}  Can: {1}  Sickle: {2}",
                    ToolTypeUtil.TierLabel(p.Tools.GetLevel(ToolType.Hoe)), ToolTypeUtil.TierLabel(p.Tools.GetLevel(ToolType.WateringCan)), ToolTypeUtil.TierLabel(p.Tools.GetLevel(ToolType.Sickle))), UIStyles.Label);
            GUILayout.Label(Loc.T("（農具は鍛冶屋で強化すると、まとめて作業できる範囲が広がる）", "(Upgrade tools at the blacksmith to work wider areas)"), UIStyles.LabelDim);

            GUILayout.Space(6);
            var b = sys.UnlockedBounds();
            GUILayout.Label(Loc.Format("農地の広さ：{0}×{1}マス", "Farm size: {0}x{1}", b.width, b.height), UIStyles.LabelBold);
            int next = sys.Expansion + 1;
            if (next < FarmingSystem.ExpansionSizes.Length && (b.width < sys.Cols || b.height < sys.Rows))
            {
                int price = FarmingSystem.ExpansionPrices[next];
                GUI.enabled = p != null && p.Wallet.CanAfford(price);
                if (GUILayout.Button(Loc.Format("農地を広げる（{0}）", "Expand the farm ({0})", UIStyles.Money(price)), UIStyles.Button, GUILayout.Width(420)))
                    sys.RequestExpand();
                GUI.enabled = true;
            }
            else GUILayout.Label(Loc.T("農場の区画いっぱいまで広がっている", "The farm fills its whole plot"), UIStyles.LabelDim);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("収穫かご", "Basket"), UIStyles.ButtonSmall)) UIManager.Open(new FarmStorageWindow(sys, p, 0));
            if (GUILayout.Button(Loc.T("倉庫", "Storage"), UIStyles.ButtonSmall)) UIManager.Open(new FarmStorageWindow(sys, p, 1));
            if (GUILayout.Button(Loc.T("種箱", "Seed box"), UIStyles.ButtonSmall)) UIManager.Open(new FarmStorageWindow(sys, p, 2));
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.Format("かご {0}/{1}　倉庫 {2}/{3}　種箱 {4}/{5}（使用中の枠）", "Basket {0}/{1}  Storage {2}/{3}  Seeds {4}/{5} (slots used)",
                sys.Basket.capacity - sys.Basket.FreeSlots(), sys.Basket.capacity, sys.Warehouse.capacity - sys.Warehouse.FreeSlots(), sys.Warehouse.capacity,
                sys.SeedBox.capacity - sys.SeedBox.FreeSlots(), sys.SeedBox.capacity), UIStyles.LabelDim);

            if (WorldAuthority.IsMultiplayer)
            {
                GUILayout.Space(6);
                bool host = p != null && p.Permission >= PermissionLevel.Owner;
                GUI.enabled = host;
                bool share = GUILayout.Toggle(sys.ShareHarvest, Loc.T(" ほかの人が植えた作物も、収穫した人が持ち帰れる（共有）", " Anyone may keep crops they harvest (shared)"), UIStyles.Toggle);
                GUI.enabled = true;
                if (host && share != sys.ShareHarvest) WorldAuthority.Submit(new FarmSetShareCommand { share = share });
                if (!sys.ShareHarvest) GUILayout.Label(Loc.T("共有しない時、ほかの人の作物を収穫すると農場の倉庫に入る", "If not shared, others' crops go to the farm storage when harvested"), UIStyles.LabelDim);
            }

            GUILayout.Space(6);
            GUILayout.Label(Loc.T("身につけた技", "Techniques"), UIStyles.LabelBold);
            bool any = false;
            foreach (var t in FarmResidentProvider.Techniques)
            {
                if (!sys.HasTechnique(t.Id)) continue;
                any = true;
                GUILayout.Label("・" + Loc.T(t.NameJa, t.NameEn) + Loc.T("：", ": ") + Loc.T(t.EffectJa, t.EffectEn), UIStyles.LabelWrap);
            }
            if (!any) GUILayout.Label(Loc.T("（農業名人の実演を見ると身につく）", "(Learn by watching the farming master)"), UIStyles.LabelDim);
        }
    }

    public sealed partial class FarmingSystem
    {
        public void RequestExpand()
        {
            int next = Expansion + 1;
            if (next >= ExpansionSizes.Length) return;
            UIManager.Confirm(Loc.T("農地を広げる", "Expand farm"), Loc.Format("{0}で農地を広げますか？", "Expand the farm for {0}?", UIStyles.Money(ExpansionPrices[next])),
                () => WorldAuthority.Submit(new FarmExpandCommand { toLevel = next }));
        }
    }

    /// <summary>タブレットが無い時の農場アプリ表示。</summary>
    public sealed class FarmAppWindow : UIWindow
    {
        private readonly FarmTabletApp app;
        public FarmAppWindow(FarmingSystem sys) { app = new FarmTabletApp(sys); }
        public override string Title => Loc.T("農場", "Farm");
        public override Vector2 Size => new Vector2(1100, 780);
        public override bool PausesTimeInSolo => true;
        public override void DrawContents(Rect content) => app.Draw(content);
    }

    /// <summary>収穫かご・倉庫・種箱。中身の受け取りは FarmStorageTakeCommand（ホスト確定）。</summary>
    public sealed class FarmStorageWindow : UIWindow
    {
        private readonly FarmingSystem sys;
        private readonly PlayerState player;
        private int tab;

        public FarmStorageWindow(FarmingSystem sys, PlayerState player, int tab)
        {
            this.sys = sys;
            this.player = player;
            this.tab = tab;
        }

        public override string Title => Loc.T("農場の収納", "Farm storage");
        public override Vector2 Size => new Vector2(980, 720);

        private static readonly string[] Ids = { "basket", "warehouse", "seedbox" };

        public override void DrawContents(Rect content)
        {
            GUILayout.BeginHorizontal();
            string[] names = { Loc.T("収穫かご", "Harvest basket"), Loc.T("倉庫", "Storage"), Loc.T("種箱", "Seed box") };
            for (int i = 0; i < 3; i++)
                if (GUILayout.Button(names[i], tab == i ? UIStyles.ButtonSelected : UIStyles.Button, GUILayout.Height(40))) tab = i;
            GUILayout.EndHorizontal();
            string[] notes =
            {
                Loc.T("収穫が得意な生き物が収穫した物が入る。運ぶのが得意な生き物がいると倉庫へ運んでくれる。", "Harvests by creatures go here. Carrier creatures move them to storage."),
                Loc.T("運ぶ生き物が運び込んだ物や、ほかの人が植えた作物の収穫物が入る。（商店へ勝手に出品はされない）", "Carried harvests and crops planted by others go here. (Nothing is put up for sale automatically.)"),
                Loc.T("種まきが得意な生き物はここから種を取って、空いた畑にまく。", "Sowing creatures take seeds from here for empty plots.")
            };
            GUILayout.Label(notes[tab], UIStyles.LabelWrap);
            var inv = sys.GetStorage(Ids[tab]);
            if (inv == null || player == null) return;
            if (tab == 2 && GUILayout.Button(Loc.T("持ち物の種を入れる", "Put seeds in"), UIStyles.Button, GUILayout.Width(320)))
            {
                UIManager.PickItem(player, Loc.T("種箱に入れる種", "Seeds for the box"), s => FarmCatalog.BySeed(s.itemId) != null,
                    (slot, stack) => WorldAuthority.Submit(new FarmSeedBoxPutCommand { itemId = stack.itemId, count = stack.count }));
            }
            if (GUILayout.Button(Loc.T("全部受け取る", "Take all"), UIStyles.ButtonSmall, GUILayout.Width(240)))
            {
                for (int i = 0; i < inv.slots.Count; i++)
                    if (inv.slots[i] != null && !inv.slots[i].IsEmpty)
                        WorldAuthority.Submit(new FarmStorageTakeCommand { storage = Ids[tab], slot = i, count = inv.slots[i].count });
            }
            scroll = GUILayout.BeginScrollView(scroll);
            bool any = false;
            for (int i = 0; i < inv.slots.Count; i++)
            {
                var s = inv.slots[i];
                if (s == null || s.IsEmpty) continue;
                any = true;
                GUILayout.BeginHorizontal(UIStyles.Box);
                var r = GUILayoutUtility.GetRect(48, 48, GUILayout.Width(48), GUILayout.Height(48));
                UIStyles.ItemIcon(r, s);
                GUILayout.Label($"{s.DisplayName} ×{s.count}", UIStyles.Label);
                GUILayout.FlexibleSpace();
                int slot = i;
                if (GUILayout.Button(Loc.T("受け取る", "Take"), UIStyles.ButtonSmall, GUILayout.Width(140)))
                    WorldAuthority.Submit(new FarmStorageTakeCommand { storage = Ids[tab], slot = slot, count = s.count });
                GUILayout.EndHorizontal();
            }
            if (!any) GUILayout.Label(Loc.T("（空っぽ）", "(empty)"), UIStyles.LabelDim);
            GUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// 住民の会話メニュー：
    ///  ・種屋（SeedSeller）：種を買う（言葉が分からなくても絵の付いた種袋で選べる：農業仕様 §11.1）
    ///  ・農業名人（FarmingMaster）：技の実演（身振りで学ぶ：§10.3）、農地を広げる相談
    ///  ・鍛冶屋（Blacksmith）：農具（クワ・じょうろ・カマ）の強化（鉱石とお金：§10.1）
    /// </summary>
    public sealed class FarmResidentProvider : IResidentInteractionProvider
    {
        private readonly FarmingSystem sys;
        public FarmResidentProvider(FarmingSystem sys) { this.sys = sys; }

        public sealed class TechniqueDef
        {
            public string Id;
            public int Level;
            public int Friendship;
            public string NameJa, NameEn, EffectJa, EffectEn;
        }

        // 【仮数値】習得条件
        public static readonly TechniqueDef[] Techniques =
        {
            new TechniqueDef { Id = FarmingSystem.TechInsight, Level = 2, Friendship = 0, NameJa = "生育を見極める", NameEn = "Crop insight", EffectJa = "作物を調べると、実るまでの日数と水の具合が分かる", EffectEn = "Inspecting a crop shows days left and thirst" },
            new TechniqueDef { Id = FarmingSystem.TechSowLine, Level = 2, Friendship = 10, NameJa = "連続種まき", NameEn = "Row sowing", EffectJa = "種を前方3マスにまとめてまける", EffectEn = "Sow 3 plots in a row at once" },
            new TechniqueDef { Id = FarmingSystem.TechWideHarvest, Level = 3, Friendship = 15, NameJa = "広範囲の収穫", NameEn = "Wide harvest", EffectJa = "手で収穫すると周りの3×3マスもまとめて収穫", EffectEn = "Hand-harvesting gathers a 3x3 area" },
            new TechniqueDef { Id = FarmingSystem.TechFertilizer, Level = 4, Friendship = 20, NameJa = "肥料の使い方", NameEn = "Fertilizer know-how", EffectJa = "肥料の効き目が1.5倍", EffectEn = "Fertilizer is 1.5x as effective" },
            new TechniqueDef { Id = FarmingSystem.TechSpecial, Level = 6, Friendship = 40, NameJa = "特別な栽培技術", NameEn = "Master growing", EffectJa = "巨大な作物・珍しい花色が出やすい", EffectEn = "Giant crops and rare colors appear more often" },
        };

        private struct ToolCost { public int Price; public string ItemId; public int Count; }

        /// <summary>農具の強化費用（index=今のレベル）【仮数値】。鉱石は鉱山の共有ID。</summary>
        private static readonly ToolCost[] ToolCosts =
        {
            new ToolCost(), new ToolCost { Price = 1000, ItemId = SharedItemIds.OreConductive, Count = 5 },
            new ToolCost { Price = 3000, ItemId = SharedItemIds.OreSturdyMetal, Count = 5 },
            new ToolCost { Price = 8000, ItemId = SharedItemIds.OreHeatproofMetal, Count = 5 },
            new ToolCost { Price = 20000, ItemId = SharedItemIds.OreEnergyCrystal, Count = 3 },
        };

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            if (resident == null || player == null) yield break;
            switch (resident.Job)
            {
                case ResidentJob.SeedSeller:
                    yield return new ResidentInteractionOption("種を買う", "Buy seeds", () => SeedSellerTalk(resident, player), 10);
                    break;
                case ResidentJob.FarmingMaster:
                    yield return new ResidentInteractionOption("農業の技を見せてもらう", "Watch a farming technique", () => Demonstrate(resident, player), 20);
                    if (sys.Expansion + 1 < FarmingSystem.ExpansionSizes.Length)
                        yield return new ResidentInteractionOption("農地を広げたい", "Expand the farm", () => sys.RequestExpand(), 30);
                    break;
                case ResidentJob.Blacksmith:
                    yield return new ResidentInteractionOption("農具を強化してもらう", "Upgrade farm tools", () => ChooseTool(resident, player), 40);
                    break;
            }
        }

        private void SeedSellerTalk(IResidentInfo r, PlayerState player)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            dir?.Speak(r.Id, new Utterance(r.Id, "farm.seedshop")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.WELCOME))
                .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.SELL).Patient(MeaningArg.Of(Concepts.SEED).Plural()))
                .Face(Expression.Happy).With(Gesture.OfferItem, "seed.fuwafuwa_tomato"));
            sys.OpenSeedShop(player);
        }

        private void Demonstrate(IResidentInfo r, PlayerState player)
        {
            var dir = Game.Instance.Service<IResidentDirectory>();
            if (dir == null) return;
            TechniqueDef next = null;
            foreach (var t in Techniques) if (!sys.HasTechnique(t.Id)) { next = t; break; }
            if (next == null)
            {
                dir.Speak(r.Id, new Utterance(r.Id, "farm.master_done").Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(Concepts.YOU)).Adverb(Concepts.VERY)).Face(Expression.VeryHappy).With(Gesture.Clap));
                return;
            }
            if (sys.Level < next.Level || r.GetFriendship(player.PlayerId) < next.Friendship)
            {
                dir.Speak(r.Id, new Utterance(r.Id, "farm.master_notyet")
                    .Say(MeaningFrame.Make(SpeechAct.Request, Concepts.GROW).Patient(MeaningArg.Of(Concepts.CROP).Plural()).Adverb(Concepts.MORE))
                    .Face(Expression.Thinking).With(Gesture.ShakeHead).With(Gesture.PointAt, "farm"));
                UIManager.Toast(Loc.T("名人は首を振って、畑のほうを指さした", "The master shook their head and pointed toward the fields"), 4f);
                return;
            }
            var u = new Utterance(r.Id, "farm.master_teach." + next.Id);
            switch (next.Id)
            {
                case FarmingSystem.TechInsight:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.SEE).Patient(MeaningArg.Of(Concepts.CROP)).Adverb(Concepts.VERY)).Face(Expression.Thinking).With(Gesture.Think).With(Gesture.PointAt, "farm");
                    break;
                case FarmingSystem.TechSowLine:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.PLANT).Patient(MeaningArg.Of(Concepts.SEED).Plural()).Adverb(Concepts.TOGETHER)).Face(Expression.Happy).With(Gesture.Wave).With(Gesture.ShowNumber, null, 3);
                    break;
                case FarmingSystem.TechWideHarvest:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.HARVEST).Patient(MeaningArg.Of(Concepts.ALL))).Face(Expression.Excited).With(Gesture.Spin);
                    break;
                case FarmingSystem.TechFertilizer:
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.PUT).Patient(MeaningArg.Of(Concepts.Item("fert.basic")))).Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GROW).Agent(MeaningArg.Of(Concepts.CROP)).Adverb(Concepts.VERY)).Face(Expression.Happy).With(Gesture.OfferItem, "fert.basic");
                    break;
                default:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GROW).Agent(MeaningArg.Of(Concepts.CROP, Concepts.BIG)).Future()).Face(Expression.Excited).With(Gesture.Hug);
                    break;
            }
            dir.Speak(r.Id, u, SpeechChannel.Direct);
            sys.LearnTechnique(next.Id);
            dir.ChangeFriendship(r.Id, player.PlayerId, 2, "taught_farming");
            UIManager.Toast(Loc.Format("名人の身振りを真似して「{0}」のコツをつかんだ！（{1}）", "Copying the master, you got the knack of \"{0}\"! ({1})", Loc.T(next.NameJa, next.NameEn), Loc.T(next.EffectJa, next.EffectEn)), 6f);
        }

        private void ChooseTool(IResidentInfo r, PlayerState player)
        {
            var opts = new List<ChoiceOption>();
            foreach (var t in new[] { ToolType.Hoe, ToolType.WateringCan, ToolType.Sickle })
            {
                int lv = Mathf.Max(1, player.Tools.GetLevel(t));
                var tool = t;
                if (lv >= 5) opts.Add(new ChoiceOption(ToolTypeUtil.Label(t) + Loc.T("（最高）", " (max)"), null, false));
                else opts.Add(new ChoiceOption(ToolTypeUtil.Label(t) + "：" + ToolTypeUtil.TierLabel(lv) + " → " + ToolTypeUtil.TierLabel(lv + 1), () => OfferUpgrade(r, player, tool)));
            }
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(Loc.T("どの農具を強化する？", "Which tool?"), Loc.T("強化すると一度に作業できる範囲が広がる", "Upgrades widen the area you work at once"), opts);
        }

        private void OfferUpgrade(IResidentInfo r, PlayerState player, ToolType tool)
        {
            int lv = Mathf.Max(1, player.Tools.GetLevel(tool));
            if (lv >= 5) return;
            var cost = ToolCosts[lv];
            string need = UIStyles.Money(cost.Price) + Loc.Format("＋{0}×{1}", " + {0} x{1}", ItemRegistry.NameOf(cost.ItemId), cost.Count);
            UIManager.Confirm(Loc.T("農具の強化", "Upgrade tool"), Loc.Format("{0}を「{1}」にしてもらう？\n必要：{2}", "Upgrade the {0} to \"{1}\"?\nCost: {2}", ToolTypeUtil.Label(tool), ToolTypeUtil.TierLabel(lv + 1), need), () =>
            {
                if (!player.Wallet.CanAfford(cost.Price) || !player.Inventory.Has(cost.ItemId, cost.Count))
                {
                    UIManager.Toast(Loc.T("足りない物がある…", "You don't have enough..."));
                    return;
                }
                if (!player.Wallet.TrySpend(cost.Price, "farm_tool_upgrade")) return;
                player.Inventory.Remove(cost.ItemId, cost.Count, 0, "farm_tool_upgrade");
                player.Tools.SetLevel(tool, lv + 1);
                EventBus.Publish(new ToolUpgradedEvent { PlayerId = player.PlayerId, Tool = tool, NewLevel = lv + 1 });
                Game.Instance.Service<IResidentDirectory>()?.Speak(r.Id, new Utterance(r.Id, "farm.tool_upgraded")
                    .Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Patient(MeaningArg.Of(Concepts.TOOL, Concepts.NEW)).Recipient(Concepts.YOU))
                    .Face(Expression.Happy).With(Gesture.OfferItem));
                UIManager.Toast(Loc.Format("{0}が「{1}」になった！", "Your {0} is now \"{1}\"!", ToolTypeUtil.Label(tool), ToolTypeUtil.TierLabel(lv + 1)));
            });
        }
    }
}
