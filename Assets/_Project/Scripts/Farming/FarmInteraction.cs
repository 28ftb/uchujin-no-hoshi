using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Farming
{
    /// <summary>
    /// 農具・持ち物の使用（ToolUseRouter）：クワで耕す／じょうろで水やり／カマで収穫／手で種まき・肥料・住み場所の設置・収穫。
    /// 農具の強化で範囲が広がる（農業仕様 §10.1）：Lv1 1マス → Lv2 前方3マス → Lv3 3×3 → Lv4 3×5 → Lv5 5×5【仮数値】
    /// </summary>
    public sealed class FarmToolHandler : IToolHandler
    {
        private readonly FarmingSystem sys;

        public FarmToolHandler(FarmingSystem sys) { this.sys = sys; }

        public int Priority => 50;

        public bool TryUseTool(ToolUseContext ctx)
        {
            var p = ctx.Player;
            if (p == null || !p.IsLocal || ctx.AreaId != AreaIds.Overworld) return false;
            if (!sys.CellAt(ctx.GroundPoint, out int x, out int z)) return false;
            switch (ctx.Tool)
            {
                case ToolType.Hoe:
                    {
                        var cells = Pattern(x, z, ctx.Forward, ctx.ToolLevel, true);
                        if (cells.Count == 0) { LockedHint(); return true; }
                        var cmd = new FarmTillCommand();
                        Fill(cmd.xs, cmd.zs, cells);
                        WorldAuthority.Submit(cmd, r => GuestExp(r, cells.Count));
                        Fx(cells, new Color(0.5f, 0.36f, 0.24f));
                        return true;
                    }
                case ToolType.WateringCan:
                    {
                        var cells = Pattern(x, z, ctx.Forward, ctx.ToolLevel, true);
                        if (cells.Count == 0) { LockedHint(); return true; }
                        var cmd = new FarmWaterCommand();
                        Fill(cmd.xs, cmd.zs, cells);
                        WorldAuthority.Submit(cmd, r => GuestExp(r, 1));
                        Fx(cells, new Color(0.5f, 0.8f, 1f));
                        return true;
                    }
                case ToolType.Sickle:
                    {
                        var cells = Pattern(x, z, ctx.Forward, ctx.ToolLevel, true);
                        if (cells.Count == 0) return false;
                        var cmd = new FarmHarvestCommand { harvesterLevel = sys.Level };
                        Fill(cmd.xs, cmd.zs, cells);
                        WorldAuthority.Submit(cmd, r => GuestExp(r, 3));
                        return true;
                    }
                case ToolType.Hand:
                    return UseHand(ctx, x, z);
            }
            return false;
        }

        private bool UseHand(ToolUseContext ctx, int x, int z)
        {
            var item = ctx.SelectedItem;
            if (item == null || item.IsEmpty)
            {
                var plot = sys.GetPlot(x, z);
                if (plot == null || !plot.ripe) return false;
                sys.HarvestByHand(x, z);
                return true;
            }
            var seedDef = FarmCatalog.BySeed(item.itemId);
            if (seedDef != null)
            {
                if (!sys.InUnlocked(x, z)) { LockedHint(); return true; }
                var cells = new List<Vector2Int> { new Vector2Int(x, z) };
                if (sys.HasTechnique(FarmingSystem.TechSowLine))
                {
                    // 連続種まき：前方3マスにまとめてまく
                    var dir = Snap(ctx.Forward);
                    cells.Add(new Vector2Int(x + dir.x, z + dir.y));
                    cells.Add(new Vector2Int(x + dir.x * 2, z + dir.y * 2));
                }
                var cmd = new FarmPlantCommand { seedId = seedDef.SeedId };
                foreach (var c in cells) { cmd.xs.Add(c.x); cmd.zs.Add(c.y); }
                WorldAuthority.Submit(cmd, r => GuestExp(r, 2));
                return true;
            }
            if (FarmCatalog.Fertilizer(item.itemId) != null)
            {
                WorldAuthority.Submit(new FarmFertilizeCommand { x = x, z = z, itemId = item.itemId });
                return true;
            }
            if (FarmCatalog.Habitat(item.itemId) != null)
            {
                WorldAuthority.Submit(new FarmPlaceHabitatCommand { x = x, z = z, itemId = item.itemId });
                return true;
            }
            return false;
        }

        private static void GuestExp(CommandResult r, int amount)
        {
            // ホスト・ソロでは各コマンドの処理内で経験値を加える。ゲストは結果を受けて自分の記録に加える
            if (r != null && r.Success && WorldAuthority.IsGuest && FarmingSystem.Instance != null) FarmingSystem.Instance.AddExp(amount);
        }

        private static void LockedHint()
        {
            UIManager.Toast(Loc.T("ここはまだ畑にできない（農地を広げると使える）", "You can't farm here yet (expand your farm)"), 2.5f);
        }

        private static Vector2Int Snap(Vector3 f)
        {
            if (Mathf.Abs(f.x) > Mathf.Abs(f.z)) return new Vector2Int(f.x >= 0 ? 1 : -1, 0);
            return new Vector2Int(0, f.z >= 0 ? 1 : -1);
        }

        private List<Vector2Int> Pattern(int x, int z, Vector3 forward, int level, bool unlockedOnly)
        {
            var dir = Snap(forward);
            var side = new Vector2Int(-dir.y, dir.x);
            int len, half;
            switch (Mathf.Clamp(level, 1, 5))
            {
                case 1: len = 1; half = 0; break;
                case 2: len = 3; half = 0; break;
                case 3: len = 3; half = 1; break;
                case 4: len = 5; half = 1; break;
                default: len = 5; half = 2; break;
            }
            var list = new List<Vector2Int>();
            for (int f = 0; f < len; f++)
                for (int s = -half; s <= half; s++)
                {
                    int cx = x + dir.x * f + side.x * s, cz = z + dir.y * f + side.y * s;
                    if (unlockedOnly && !sys.InUnlocked(cx, cz)) continue;
                    list.Add(new Vector2Int(cx, cz));
                }
            return list;
        }

        private static void Fill(List<int> xs, List<int> zs, List<Vector2Int> cells)
        {
            foreach (var c in cells) { xs.Add(c.x); zs.Add(c.y); }
        }

        private void Fx(List<Vector2Int> cells, Color color)
        {
            int i = 0;
            foreach (var c in cells)
            {
                if (i++ > 6) break;
                var pos = sys.CellCenter(c.x, c.y) + Vector3.up * 0.2f;
                for (int k = 0; k < 2; k++)
                {
                    var go = Core.Visual.PrimitiveBuilder.Create(PrimitiveType.Sphere, Game.Instance.SceneRoot, pos, Vector3.one * 0.07f, color, "FarmFx", false);
                    go.transform.position = pos + new Vector3(Random.Range(-0.3f, 0.3f), 0, Random.Range(-0.3f, 0.3f));
                    Object.Destroy(go, 0.5f);
                }
            }
        }
    }

    public sealed partial class FarmingSystem
    {
        /// <summary>手で収穫（広範囲の収穫の技があれば 3×3）。</summary>
        public void HarvestByHand(int x, int z)
        {
            var cmd = new FarmHarvestCommand { harvesterLevel = Level };
            int r = HasTechnique(TechWideHarvest) ? 1 : 0;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    cmd.xs.Add(x + dx);
                    cmd.zs.Add(z + dz);
                }
            WorldAuthority.Submit(cmd, res =>
            {
                if (res != null && res.Success && WorldAuthority.IsGuest) AddExp(3);
            });
        }

        /// <summary>この星の種屋で今買えるか（星蜜果は序盤〜少し進んでから：README 参照）。</summary>
        public bool IsSeedUnlocked(CropDef def, out string reasonJa, out string reasonEn)
        {
            reasonJa = reasonEn = null;
            int day = Clock != null ? Clock.Day : 1;
            if (def.Key == "star_honey_fruit")
            {
                var rocket = Game.Service<IRocketService>();
                if (Level >= def.UnlockLevel || day >= def.UnlockDay || (rocket != null && rocket.RepairedDeviceCount > 0)) return true;
                reasonJa = "種屋がまだ奥にしまっている（少し農業に慣れたら／" + def.UnlockDay + "日目ごろ）";
                reasonEn = "Still stored away (after some farming / around day " + def.UnlockDay + ")";
                return false;
            }
            if (Level >= def.UnlockLevel || day >= def.UnlockDay) return true;
            reasonJa = "まだ並んでいない（農業Lv" + def.UnlockLevel + "／" + def.UnlockDay + "日目ごろ）";
            reasonEn = "Not on the shelf yet (Farming Lv" + def.UnlockLevel + " / around day " + def.UnlockDay + ")";
            return false;
        }

        /// <summary>種屋（ResidentJob.SeedSeller の会話メニュー、FacilityType.SeedShop の棚）。</summary>
        public void OpenSeedShop(PlayerState player)
        {
            if (player == null) return;
            var entries = new List<VendorEntry>();
            foreach (var c in FarmCatalog.Crops)
            {
                bool ok = IsSeedUnlocked(c, out var rj, out var re);
                entries.Add(new VendorEntry { ItemId = c.SeedId, Price = c.SeedPrice, Locked = !ok, LockedReasonJa = rj, LockedReasonEn = re });
            }
            foreach (var f in FarmCatalog.Fertilizers)
                entries.Add(new VendorEntry { ItemId = f.ItemId, Price = f.Price, Locked = Level < f.UnlockLevel, LockedReasonJa = "農業Lv" + f.UnlockLevel + "から", LockedReasonEn = "From Farming Lv" + f.UnlockLevel });
            foreach (var h in FarmCatalog.Habitats)
                entries.Add(new VendorEntry { ItemId = h.ItemId, Price = h.Price, Locked = Level < h.UnlockLevel, LockedReasonJa = "農業Lv" + h.UnlockLevel + "から", LockedReasonEn = "From Farming Lv" + h.UnlockLevel });
            // 序盤から生き物を誘えるよう、基本の餌も少し置いている（Creatures が登録したID。無ければ出さない）
            if (ItemRegistry.Exists("cfood.basic")) entries.Add(new VendorEntry { ItemId = "cfood.basic", Price = ItemRegistry.Get("cfood.basic").BasePrice + 5 });
            UIManager.Open(new VendorWindow(Loc.T("種屋", "Seed shop"), player, entries));
        }
    }

    /// <summary>畑のマス（作物があるマス）。[E]で収穫・様子を見る（「生育を見極める」技で残り日数が分かる）。</summary>
    public class FarmPlotInteractable : MonoBehaviour, IInteractable
    {
        public FarmingSystem System;
        public int X, Z;

        public string GetInteractionPrompt(PlayerState player)
        {
            var p = System != null ? System.GetPlot(X, Z) : null;
            if (p == null || !p.HasCrop) return null;
            var def = FarmCatalog.Get(p.cropKey);
            if (def == null) return null;
            if (p.ripe) return Loc.Format("収穫する：{0}", "Harvest: {0}", def.Name);
            if (System.HasTechnique(FarmingSystem.TechInsight))
            {
                int left = Mathf.Max(1, Mathf.CeilToInt(def.GrowDays - p.growth));
                return Loc.Format("{0}（あと約{1}日{2}）", "{0} (~{1} days{2})", def.Name, left, p.watered ? "" : Loc.T("・水がほしそう", ", thirsty"));
            }
            return Loc.Format("{0}を見る", "Look at {0}", def.Name);
        }

        public bool CanInteract(PlayerState player) => System != null && System.GetPlot(X, Z) != null;

        public void Interact(PlayerState player)
        {
            var p = System.GetPlot(X, Z);
            if (p == null || !p.HasCrop) return;
            if (p.ripe)
            {
                System.HarvestByHand(X, Z);
                return;
            }
            var def = FarmCatalog.Get(p.cropKey);
            int stage = def != null ? def.StageOf(p.growth, false) : 0;
            string s = stage == 0 ? Loc.T("まだ種のまま", "Still a seed") : stage == 1 ? Loc.T("芽が出ている", "It has sprouted") : Loc.T("ぐんぐん育っている", "Growing well");
            if (!p.watered) s += Loc.T("。土が乾いている", ". The soil is dry");
            if (p.pollinated) s += Loc.T("。花粉がついている", ". It's been pollinated");
            UIManager.Toast(s, 2.5f);
        }
    }

    /// <summary>農場の住み場所。[E]で住んでいる生き物の確認・住まわせる・片付ける。</summary>
    public class FarmHabitatInteractable : MonoBehaviour, IInteractable
    {
        public FarmingSystem System;
        public string HabitatId;

        public string GetInteractionPrompt(PlayerState player)
        {
            var h = System.GetHabitat(HabitatId);
            if (h == null) return null;
            var def = FarmCatalog.Habitat(h.itemId);
            int n = 0;
            foreach (var v in System.CreaturesOnFarm()) if (v.PlacementTargetId == HabitatId) n++;
            return Loc.Format("住み場所：{0}（{1}匹）", "Home: {0} ({1})", def != null ? Loc.T(def.NameJa, def.NameEn) : "?", n);
        }

        public bool CanInteract(PlayerState player) => System.GetHabitat(HabitatId) != null;

        public void Interact(PlayerState player)
        {
            var h = System.GetHabitat(HabitatId);
            if (h == null) return;
            var cs = Game.Instance.Service<ICreatureService>();
            var def = FarmCatalog.Habitat(h.itemId);
            var names = new List<string>();
            foreach (var v in System.CreaturesOnFarm()) if (v.PlacementTargetId == HabitatId) names.Add(v.Nickname);
            string msg = names.Count > 0 ? Loc.Format("住んでいる子：{0}", "Living here: {0}", string.Join(Loc.T("、", ", "), names.ToArray())) : Loc.T("まだ誰も住んでいない", "Nobody lives here yet");
            if (def != null && def.Favored.Length > 0) msg += "\n" + Loc.T("（好きな生き物が住むと、よく働く）", "(Creatures that like it work better)");
            var opts = new List<ChoiceOption>();
            if (cs != null)
                opts.Add(new ChoiceOption(Loc.T("生き物を住まわせる", "Let a creature live here"), () => ChooseCreature(cs, player)));
            opts.Add(new ChoiceOption(Loc.T("片付ける（持ち物に戻す）", "Pick it up"), () =>
                UIManager.Confirm(Loc.T("片付ける", "Pick up"), Loc.T("住み場所を片付けますか？住んでいた子は農場のほかの場所へ移ります。", "Pick this up? Its residents will move elsewhere on the farm."),
                    () => WorldAuthority.Submit(new FarmRemoveHabitatCommand { habitatId = HabitatId }))));
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(def != null ? Loc.T(def.NameJa, def.NameEn) : "", msg, opts);
        }

        private void ChooseCreature(ICreatureService cs, PlayerState player)
        {
            var opts = new List<ChoiceOption>();
            foreach (var v in cs.GetOwned(player.PlayerId))
            {
                if (v.Placement == CreaturePlacement.Deposited || (v.Placement == CreaturePlacement.Farm && v.PlacementTargetId == HabitatId)) continue;
                var vv = v;
                opts.Add(new ChoiceOption(vv.Nickname + " (" + cs.GetSpeciesName(vv.SpeciesId) + ")", () =>
                {
                    if (cs.SetPlacement(vv.Uid, CreaturePlacement.Farm, HabitatId))
                        UIManager.Toast(Loc.Format("{0}が住み場所に引っ越した", "{0} moved in", vv.Nickname));
                }));
            }
            if (opts.Count == 0)
            {
                UIManager.Message(Loc.T("住み場所", "Home"), Loc.T("住まわせられる生き物がいない。網で捕まえてみよう。", "No creatures to move in. Try catching some with a net."));
                return;
            }
            opts.Add(new ChoiceOption(Loc.T("やめる", "Cancel"), null));
            UIManager.Choose(Loc.T("どの子を住まわせる？", "Which one?"), null, opts);
        }
    }

    /// <summary>種屋の棚（FacilityType.SeedShop の前）。店の人が不在でも種を買える。</summary>
    public class SeedShopCounter : MonoBehaviour, IInteractable
    {
        public FarmingSystem System;
        public string GetInteractionPrompt(PlayerState player) => Loc.T("種屋の棚を見る", "Browse the seed shelf");
        public bool CanInteract(PlayerState player) => System != null;
        public void Interact(PlayerState player) => System.OpenSeedShop(player);
    }
}
