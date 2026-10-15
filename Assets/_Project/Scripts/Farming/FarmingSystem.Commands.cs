using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Farming
{
    // =====================================================================
    // 農作業のコマンド（ホスト権威：マルチ仕様 §7「複数人で同時に耕す・まく・水やり・収穫」）
    // =====================================================================

    [Serializable] public class FarmTillCommand : WorldCommand { public List<int> xs = new List<int>(); public List<int> zs = new List<int>(); }
    [Serializable] public class FarmWaterCommand : WorldCommand { public List<int> xs = new List<int>(); public List<int> zs = new List<int>(); }
    [Serializable] public class FarmPlantCommand : WorldCommand { public List<int> xs = new List<int>(); public List<int> zs = new List<int>(); public string seedId; }
    [Serializable] public class FarmHarvestCommand : WorldCommand { public List<int> xs = new List<int>(); public List<int> zs = new List<int>(); public int harvesterLevel = 1; }
    [Serializable] public class FarmFertilizeCommand : WorldCommand { public int x; public int z; public string itemId; }
    [Serializable] public class FarmPlaceHabitatCommand : WorldCommand { public int x; public int z; public string itemId; }
    [Serializable] public class FarmRemoveHabitatCommand : WorldCommand { public string habitatId; }
    [Serializable] public class FarmExpandCommand : WorldCommand { public int toLevel; }
    [Serializable] public class FarmStorageTakeCommand : WorldCommand { public string storage; public int slot; public int count; }
    [Serializable] public class FarmSeedBoxPutCommand : WorldCommand { public string itemId; public int count; }
    [Serializable] public class FarmSetShareCommand : WorldCommand { public bool share; }

    public sealed partial class FarmingSystem
    {
        private void RegisterCommands()
        {
            WorldAuthority.Register<FarmTillCommand>(HandleTill);
            WorldAuthority.Register<FarmWaterCommand>(HandleWater);
            WorldAuthority.Register<FarmPlantCommand>(HandlePlant);
            WorldAuthority.Register<FarmHarvestCommand>(HandleHarvest);
            WorldAuthority.Register<FarmFertilizeCommand>(HandleFertilize);
            WorldAuthority.Register<FarmPlaceHabitatCommand>(HandlePlaceHabitat);
            WorldAuthority.Register<FarmRemoveHabitatCommand>(HandleRemoveHabitat);
            WorldAuthority.Register<FarmExpandCommand>(HandleExpand);
            WorldAuthority.Register<FarmStorageTakeCommand>(HandleStorageTake);
            WorldAuthority.Register<FarmSeedBoxPutCommand>(HandleSeedBoxPut);
            WorldAuthority.Register<FarmSetShareCommand>(HandleSetShare);
        }

        private static CommandResult NoPermission() => CommandResult.Fail("この農場では作業できません（権限）", "You can't work this farm (permission)");
        private static CommandResult Quiet() => CommandResult.Fail(null, null);

        private void Changed()
        {
            MarkDirty();
            RefreshPlotViews();
        }

        private CommandResult HandleTill(FarmTillCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            int n = 0;
            for (int i = 0; i < cmd.xs.Count && i < cmd.zs.Count; i++)
            {
                int x = cmd.xs[i], z = cmd.zs[i];
                if (!InUnlocked(x, z) || HabitatAt(x, z) != null) continue;
                var p = GetOrCreatePlot(x, z);
                if (p.HasCrop || p.tilled) continue;
                p.tilled = true;
                p.wasFarmed = false;
                n++;
            }
            if (n == 0) return Quiet();
            if (ctx.IsLocalIssuer) AddExp(n);
            Changed();
            return CommandResult.Ok();
        }

        private CommandResult HandleWater(FarmWaterCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            int n = 0;
            for (int i = 0; i < cmd.xs.Count && i < cmd.zs.Count; i++)
            {
                var p = GetPlot(cmd.xs[i], cmd.zs[i]);
                if (p == null || (!p.tilled && !p.HasCrop) || p.watered) continue;
                WaterPlot(p);
                n++;
            }
            if (n == 0) return Quiet();
            if (ctx.IsLocalIssuer) AddExp(1);
            Changed();
            return CommandResult.Ok();
        }

        private CommandResult HandlePlant(FarmPlantCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            var def = FarmCatalog.BySeed(cmd.seedId);
            if (def == null) return CommandResult.Fail("これは植えられない", "That can't be planted");
            int n = 0;
            for (int i = 0; i < cmd.xs.Count && i < cmd.zs.Count; i++)
            {
                int x = cmd.xs[i], z = cmd.zs[i];
                if (!InUnlocked(x, z) || HabitatAt(x, z) != null) continue;
                var p = GetPlot(x, z);
                if (p == null || !p.tilled || p.HasCrop) continue;
                if (!ctx.HasItem(def.SeedId, 1) || !ctx.TakeItem(def.SeedId, 1, "farm_plant")) break;
                Plant(p, def, ctx.IssuerId);
                EventBus.Publish(new CropPlantedEvent { PlayerId = ctx.IssuerId, CropItemId = def.CropId, Position = CellCenter(x, z) });
                n++;
            }
            if (n == 0) return CommandResult.Fail("耕した空いている畑に植えよう", "Plant in an empty tilled plot");
            if (ctx.IsLocalIssuer) AddExp(2 * n);
            Changed();
            return CommandResult.Ok();
        }

        private void Plant(FarmPlot p, CropDef def, string ownerId)
        {
            p.cropKey = def.Key;
            p.plantedDay = Clock != null ? Clock.Day : 0;
            p.growth = 0f;
            p.ripe = false;
            p.harvests = 0;
            p.ownerId = ownerId;
            p.pollinated = false;
            p.glowNights = 0;
            p.daysDry = 0;
            p.daysWatered = 0;
            p.lightRain = false;
            p.waterSoilComboDay = -1;
        }

        private CommandResult HandleHarvest(FarmHarvestCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            int got = 0, stored = 0;
            HarvestOutcome special = null;
            string lastName = null;
            int lastQuality = 0;
            for (int i = 0; i < cmd.xs.Count && i < cmd.zs.Count; i++)
            {
                var p = GetPlot(cmd.xs[i], cmd.zs[i]);
                if (p == null || !p.ripe) continue;
                string owner = p.ownerId;
                var o = HarvestPlot(p, ctx.IssuerId, false, Mathf.Clamp(cmd.harvesterLevel, 1, MaxFarmLevel));
                if (o == null) continue;
                bool mine = string.IsNullOrEmpty(owner) || owner == ctx.IssuerId || ShareHarvest;
                foreach (var s in o.Stacks)
                {
                    if (mine) { ctx.GiveItem(s, "farm_harvest"); got += s.count; }
                    else
                    {
                        // 他人の作物は収穫者へ自動で移さず、農場の倉庫へ（マルチ仕様 §7.2）
                        int left = warehouse.Add(s, "farm_harvest_other");
                        stored += s.count - left;
                        if (left > 0) { ctx.GiveItem(s.CloneWithCount(left), "farm_harvest"); got += left; }
                    }
                    lastName = ItemRegistry.NameOf(s.itemId);
                    lastQuality = s.quality;
                }
                if (o.Giant || o.Glow || o.RareColor) special = o;
                CleanupPlot(p);
            }
            if (got == 0 && stored == 0) return Quiet();
            if (ctx.IsLocalIssuer)
            {
                AddExp(3 * Mathf.Max(1, got / 2) + (special != null ? 10 : 0));
                AddHarvestCount(got + stored);
            }
            Changed();
            if (special != null)
            {
                if (special.Giant) ctx.Notify("とても大きく育っている！", "It grew enormous!");
                else if (special.Glow) ctx.Notify("実がぼんやり光っている！", "The fruit is glowing!");
                else ctx.Notify("見たことのない色の花が咲いた！", "A flower of a color you've never seen!");
            }
            if (got > 0) ctx.Notify("収穫：" + lastName + ItemStack.QualityStars(lastQuality) + " ほか ×" + got, "Harvested " + lastName + ItemStack.QualityStars(lastQuality) + " etc. x" + got);
            if (stored > 0) ctx.Notify("ほかの人が植えた作物は農場の倉庫に入れました（×" + stored + "）", "Crops planted by others went to the farm storage (x" + stored + ")");
            return CommandResult.Ok();
        }

        private CommandResult HandleFertilize(FarmFertilizeCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            var f = FarmCatalog.Fertilizer(cmd.itemId);
            var p = GetPlot(cmd.x, cmd.z);
            if (f == null || p == null || (!p.tilled && !p.HasCrop)) return CommandResult.Fail("耕した畑にまこう", "Use it on a tilled plot");
            if (p.fertilizer == f.ItemId) return CommandResult.Fail("もう肥料が入っている", "Already fertilized");
            if (!ctx.TakeItem(f.ItemId, 1, "farm_fertilize")) return Quiet();
            p.fertilizer = f.ItemId;
            if (p.watered && f.RetainDays > 0) p.retainDays = Mathf.Max(p.retainDays, f.RetainDays);
            Changed();
            return CommandResult.Ok();
        }

        private CommandResult HandlePlaceHabitat(FarmPlaceHabitatCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            var def = FarmCatalog.Habitat(cmd.itemId);
            if (def == null) return CommandResult.Fail("これは住み場所にならない", "That isn't a creature home");
            if (cmd.x < 0 || cmd.z < 0 || cmd.x >= Cols || cmd.z >= Rows) return CommandResult.Fail("農場の中に置こう", "Place it inside the farm");
            if (HabitatAt(cmd.x, cmd.z) != null) return CommandResult.Fail("そこにはもう置いてある", "Something is already there");
            var p = GetPlot(cmd.x, cmd.z);
            if (p != null && (p.tilled || p.HasCrop)) return CommandResult.Fail("畑ではない地面に置こう", "Place it on ground that isn't a plot");
            if (!ctx.TakeItem(def.ItemId, 1, "farm_habitat")) return Quiet();
            habitatCounter++;
            habitats.Add(new FarmHabitat { id = "fh" + habitatCounter + "_" + Ids.New().Substring(0, 6), itemId = def.ItemId, x = cmd.x, z = cmd.z, ownerId = ctx.IssuerId });
            MarkDirty();
            RebuildFarmScene();
            ctx.Notify(def.NameJa + "を置いた。生き物を住まわせよう", "Placed the " + def.NameEn + ". Let a creature move in!");
            return CommandResult.Ok();
        }

        private CommandResult HandleRemoveHabitat(FarmRemoveHabitatCommand cmd, CommandContext ctx)
        {
            var h = GetHabitat(cmd.habitatId);
            if (h == null) return Quiet();
            if (h.ownerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner) return NoPermission();
            habitats.Remove(h);
            ctx.GiveItem(new ItemStack(h.itemId, 1), "farm_habitat_remove");
            MarkDirty();
            RebuildFarmScene();
            return CommandResult.Ok();
        }

        private CommandResult HandleExpand(FarmExpandCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.CoLiving)) return NoPermission();
            int next = Expansion + 1;
            if (next >= ExpansionSizes.Length) return CommandResult.Fail("これ以上は広げられない", "The farm can't grow any further");
            if (cmd.toLevel != next) return Quiet();
            if (!ctx.TryCharge(ExpansionPrices[next], "farm_expand")) return CommandResult.Fail("お金が足りない", "Not enough money");
            Expansion = next;
            MarkDirty();
            RebuildFarmScene();
            ctx.Notify("農場が広くなった！", "Your farm got bigger!");
            return CommandResult.Ok();
        }

        private CommandResult HandleStorageTake(FarmStorageTakeCommand cmd, CommandContext ctx)
        {
            var inv = GetStorage(cmd.storage);
            if (inv == null) return Quiet();
            var need = cmd.storage == "seedbox" ? PermissionLevel.Helper : PermissionLevel.CoLiving;
            if (!ctx.HasPermission(need)) return NoPermission();
            var taken = inv.TakeFromSlot(cmd.slot, Mathf.Max(1, cmd.count), "farm_storage_take");
            if (taken == null) return Quiet();
            ctx.GiveItem(taken, "farm_storage");
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleSeedBoxPut(FarmSeedBoxPutCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            if (FarmCatalog.BySeed(cmd.itemId) == null) return CommandResult.Fail("種箱には種だけ入れよう", "Only seeds go in the seed box");
            int n = Mathf.Max(1, cmd.count);
            var stack = new ItemStack(cmd.itemId, n);
            if (!seedBox.CanAdd(stack)) return CommandResult.Fail("種箱がいっぱい", "The seed box is full");
            if (!ctx.HasItem(cmd.itemId, n) || !ctx.TakeItem(cmd.itemId, n, "farm_seedbox")) return Quiet();
            seedBox.Add(stack, "farm_seedbox");
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleSetShare(FarmSetShareCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Owner)) return NoPermission();
            ShareHarvest = cmd.share;
            MarkDirty();
            return CommandResult.Ok();
        }
    }
}
