using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Housing
{
    // =====================================================================
    // 家の状態を変える操作（WorldCommand：ホスト権威。マルチの共同生活は CoLiving 以上 §20）。
    // JsonUtility で送るので public フィールドのみ。
    // =====================================================================

    [Serializable] public class PlaceFurnitureCommand : WorldCommand
    {
        public string itemId; public int quality; public string data;
        public string location; public int room; public int gx; public int gz; public int rot;
        public string originKind; public string originResidentId; public int originDay;
    }
    [Serializable] public class MoveFurnitureCommand : WorldCommand { public string instanceId; public string location; public int room; public int gx; public int gz; public int rot; }
    [Serializable] public class StoreFurnitureCommand : WorldCommand { public string instanceId; }
    [Serializable] public class SetRoomSurfaceCommand : WorldCommand { public int room; public bool wallpaper; public string itemId; public int quality; }
    [Serializable] public class ToggleFurnitureLightCommand : WorldCommand { public string instanceId; }
    [Serializable] public class StorageTransferCommand : WorldCommand { public string instanceId; public bool put; public string itemId; public int quality; public int count; public string data; public float size; }
    [Serializable] public class DisplayItemCommand : WorldCommand { public string instanceId; public bool put; public string itemId; public int quality; public string data; public float size; public int index; }
    [Serializable] public class OrderConstructionCommand : WorldCommand { public string kind; public int room; public string itemId; public List<StringIntPair> materials = new List<StringIntPair>(); }
    [Serializable] public class CancelConstructionCommand : WorldCommand { public string orderId; }
    [Serializable] public class AssignCreatureCommand : WorldCommand { public string creatureUid; public string instanceId; public string mode; }
    [Serializable] public class FurnitureNoteCommand : WorldCommand
    {
        public const string ModeAddNote = "add_note";
        public const string ModeRemove = "remove";
        public const string ModePin = "pin";
        public const string ModeSatPlayer = "sat_player";
        public string instanceId; public string mode; public string text; public int index; public bool pinned;
    }
    [Serializable] public class SetStoragePrivateCommand : WorldCommand { public string instanceId; public bool value; }
    [Serializable] public class SetRoomOwnerCommand : WorldCommand { public int room; public string ownerId; }
    [Serializable] public class SetGuestEntryCommand : WorldCommand { public bool value; }
    [Serializable] public class TakeDeliveryCommand : WorldCommand { public int index; }

    /// <summary>増築・家具の制作依頼に使う素材の種類。</summary>
    public enum MaterialGroup { Wood, Stone, Crystal, Flower }

    public class ConstructionOption
    {
        public string kind;
        public string ja, en, descJa, descEn;
        public int price;
        public int days;
        public List<KeyValuePair<MaterialGroup, int>> materials = new List<KeyValuePair<MaterialGroup, int>>();
        public bool perRoom;
        public string Label => Loc.T(ja, en);
        public string Desc => Loc.T(descJa, descEn);
    }

    /// <summary>増築・改装の一覧（§3 増築項目。費用・工期は【仮数値】）。</summary>
    public static class ConstructionCatalog
    {
        public static readonly List<ConstructionOption> Options = new List<ConstructionOption>
        {
            Opt(ConstructionKinds.AddRoom, "部屋を増やす", "Add a room", "横に新しい部屋を作る。用途は置く家具しだい。", "A new room beside the others. Its use is up to your furniture.", 8000, 3, MaterialGroup.Wood, 10),
            Opt(ConstructionKinds.AddGlass, "ガラス張りの部屋", "Glass room", "景色や植物を眺める明るい部屋。", "A bright room to enjoy the view and plants.", 14000, 4, MaterialGroup.Wood, 8, MaterialGroup.Crystal, 4),
            Opt(ConstructionKinds.AddWaterside, "水辺の部屋", "Waterside room", "奥に水場がある部屋。水槽や水辺の生き物に。", "A room with a pool at the back for tanks and water creatures.", 16000, 4, MaterialGroup.Wood, 8, MaterialGroup.Stone, 10),
            Opt(ConstructionKinds.AddTerrace, "テラス", "Terrace", "屋根のない屋外の部屋。庭の家具も置ける。", "An open-air room; garden furniture works here too.", 6000, 2, MaterialGroup.Wood, 12),
            Opt(ConstructionKinds.AddLoft, "2階・ロフト", "Loft", "階段で上がる上の階の部屋。", "An upstairs room reached by stairs.", 18000, 5, MaterialGroup.Wood, 20),
            Opt(ConstructionKinds.AddBasement, "地下室", "Basement", "倉庫・趣味室・展示室に向いた地下の部屋。", "An underground room for storage, hobbies or displays.", 15000, 5, MaterialGroup.Stone, 20),
            Opt(ConstructionKinds.ExpandWidth, "部屋を横に広げる", "Widen a room", "選んだ部屋の幅を2m広げる。", "Widen the chosen room by 2m.", 4000, 2, MaterialGroup.Wood, 6, perRoom: true),
            Opt(ConstructionKinds.ExpandDepth, "部屋を奥に広げる", "Deepen a room", "選んだ部屋の奥行きを2m広げる。", "Deepen the chosen room by 2m.", 4000, 2, MaterialGroup.Wood, 6, perRoom: true),
            Opt(ConstructionKinds.GardenExpand, "庭を広げる", "Expand the garden", "庭の範囲を4m広げる。", "Expand the garden by 4m.", 3000, 1, MaterialGroup.Wood, 6),
        };

        private static ConstructionOption Opt(string kind, string ja, string en, string dja, string den, int price, int days, MaterialGroup g1, int c1, MaterialGroup g2 = MaterialGroup.Wood, int c2 = 0, bool perRoom = false)
        {
            var o = new ConstructionOption { kind = kind, ja = ja, en = en, descJa = dja, descEn = den, price = price, days = days, perRoom = perRoom };
            if (c1 > 0) o.materials.Add(new KeyValuePair<MaterialGroup, int>(g1, c1));
            if (c2 > 0) o.materials.Add(new KeyValuePair<MaterialGroup, int>(g2, c2));
            return o;
        }

        public static ConstructionOption Get(string kind)
        {
            foreach (var o in Options) if (o.kind == kind) return o;
            return null;
        }

        /// <summary>家具の制作依頼（§16.1：素材とお金を渡して作ってもらう）。</summary>
        public static ConstructionOption Commission(ItemDefinition def)
        {
            var spec = FurnitureCatalog.Resolve(def);
            int price = Mathf.Max(50, Mathf.RoundToInt(def.BasePrice * HousingConst.CommissionPriceRate));
            var o = new ConstructionOption
            {
                kind = ConstructionKinds.Commission,
                ja = def.NameJa + "を作ってもらう",
                en = "Commission: " + def.NameEn,
                descJa = def.DescriptionJa,
                descEn = def.DescriptionEn,
                price = price,
                days = HousingConst.CommissionDays + (def.BasePrice >= 8000 ? 1 : 0)
            };
            o.materials.Add(new KeyValuePair<MaterialGroup, int>(MaterialGroup.Wood, Mathf.Clamp(def.BasePrice / 400, 2, 12)));
            if (spec.series == FurnitureSeries.Crystal || def.HasTag(ItemTags.Crystal)) o.materials.Add(new KeyValuePair<MaterialGroup, int>(MaterialGroup.Crystal, 2));
            if (spec.style == FurnitureStyle.Rocks || spec.style == FurnitureStyle.Pond || spec.style == FurnitureStyle.Path) o.materials.Add(new KeyValuePair<MaterialGroup, int>(MaterialGroup.Stone, 4));
            if (spec.use == FurnitureUse.Plant) o.materials.Add(new KeyValuePair<MaterialGroup, int>(MaterialGroup.Flower, 2));
            return o;
        }

        public static string GroupLabel(MaterialGroup g)
        {
            switch (g)
            {
                case MaterialGroup.Stone: return Loc.T("石・鉱石", "Stone / ore");
                case MaterialGroup.Crystal: return Loc.T("結晶・宝石・板ガラス", "Crystal / gem / glass");
                case MaterialGroup.Flower: return Loc.T("花", "Flowers");
                default: return Loc.T("素材（木材など）", "Materials (lumber etc.)");
            }
        }

        public static bool Matches(MaterialGroup g, ItemDefinition def)
        {
            if (def == null) return false;
            // ロケット修理の部品・大切な物は素材として使わない
            if (def.Category == ItemCategory.RocketPart || def.Category == ItemCategory.KeyItem) return false;
            if (def.HasTag(ItemTags.RocketMaterial) || def.HasTag(ItemTags.Precision) || def.HasTag(ItemTags.NotSellable)) return false;
            switch (g)
            {
                case MaterialGroup.Stone: return def.HasTag("stone") || def.Category == ItemCategory.Ore;
                case MaterialGroup.Crystal: return def.HasTag(ItemTags.Crystal) || def.HasTag(ItemTags.Gem) || def.Category == ItemCategory.Gem || def.HasTag("glass");
                case MaterialGroup.Flower: return def.HasTag(ItemTags.Flower) && !FurnitureCatalog.IsPlaceable(def);
                default: return def.Category == ItemCategory.Material || def.HasTag("wood");
            }
        }

        /// <summary>持ち物から、安い物から順に素材を選ぶ（足りなければ false）。</summary>
        public static bool PickMaterials(PlayerState p, ConstructionOption o, List<StringIntPair> result)
        {
            result.Clear();
            var used = new Dictionary<string, int>();
            foreach (var req in o.materials)
            {
                int need = req.Value;
                var candidates = new List<ItemStack>();
                foreach (var s in p.Inventory.NonEmpty()) if (Matches(req.Key, s.Definition) && s.quality == 0 && string.IsNullOrEmpty(s.data)) candidates.Add(s);
                candidates.Sort((a, b) => a.Definition.BasePrice.CompareTo(b.Definition.BasePrice));
                foreach (var s in candidates)
                {
                    if (need <= 0) break;
                    used.TryGetValue(s.itemId, out int already);
                    int available = p.Inventory.CountOf(s.itemId) - already;
                    int take = Mathf.Min(need, Mathf.Min(available, s.count));
                    if (take <= 0) continue;
                    used[s.itemId] = already + take;
                    need -= take;
                }
                if (need > 0) return false;
            }
            foreach (var kv in used) result.Add(new StringIntPair(kv.Key, kv.Value));
            return true;
        }

        public static int CountAvailable(PlayerState p, MaterialGroup g)
        {
            int n = 0;
            foreach (var s in p.Inventory.NonEmpty()) if (Matches(g, s.Definition) && s.quality == 0 && string.IsNullOrEmpty(s.data)) n += s.count;
            return n;
        }
    }

    public sealed partial class HousingSystem
    {
        private void RegisterCommands()
        {
            WorldAuthority.Register<PlaceFurnitureCommand>(HandlePlace);
            WorldAuthority.Register<MoveFurnitureCommand>(HandleMove);
            WorldAuthority.Register<StoreFurnitureCommand>(HandleStore);
            WorldAuthority.Register<SetRoomSurfaceCommand>(HandleSurface);
            WorldAuthority.Register<ToggleFurnitureLightCommand>(HandleLight);
            WorldAuthority.Register<StorageTransferCommand>(HandleStorage);
            WorldAuthority.Register<DisplayItemCommand>(HandleDisplay);
            WorldAuthority.Register<OrderConstructionCommand>(HandleOrder);
            WorldAuthority.Register<CancelConstructionCommand>(HandleCancelOrder);
            WorldAuthority.Register<InviteResidentCommand>(HandleInvite);
            WorldAuthority.Register<CancelInvitationCommand>(HandleCancelInvite);
            WorldAuthority.Register<AssignCreatureCommand>(HandleAssignCreature);
            WorldAuthority.Register<FurnitureNoteCommand>(HandleNote);
            WorldAuthority.Register<SetStoragePrivateCommand>(HandlePrivate);
            WorldAuthority.Register<SetRoomOwnerCommand>(HandleRoomOwner);
            WorldAuthority.Register<SetGuestEntryCommand>(HandleGuestEntry);
            WorldAuthority.Register<TakeDeliveryCommand>(HandleTakeDelivery);
        }

        private static bool CanEditCtx(CommandContext ctx) => !WorldAuthority.IsMultiplayer || ctx.HasPermission(PermissionLevel.CoLiving);

        private static CommandResult NoPermission() =>
            CommandResult.Fail("家主から模様替えの許可（共同生活）をもらう必要があります", "You need co-living permission from the owner");

        private bool RoomAllowed(CommandContext ctx, string location, int room)
        {
            if (location != HousingConst.LocHouse) return true;
            var r = HouseGeometry.GetRoom(state, room);
            if (r == null || string.IsNullOrEmpty(r.ownerId)) return true;
            return r.ownerId == ctx.IssuerId || ctx.Permission >= PermissionLevel.Owner;
        }

        /// <summary>発行者の持ち物から、データ（思い出の紐付け）まで一致する物を取る。</summary>
        private static bool TakeExact(CommandContext ctx, string itemId, int quality, string data, int count, string reason)
        {
            if (count <= 0) return true;
            if (ctx.IsLocalIssuer)
            {
                var inv = ctx.Issuer.Inventory;
                int have = 0;
                foreach (var s in inv.slots)
                    if (s != null && !s.IsEmpty && s.itemId == itemId && s.quality == quality && (s.data ?? "") == (data ?? "")) have += s.count;
                if (have < count) return false;
                int left = count;
                for (int i = 0; i < inv.slots.Count && left > 0; i++)
                {
                    var s = inv.slots[i];
                    if (s == null || s.IsEmpty || s.itemId != itemId || s.quality != quality || (s.data ?? "") != (data ?? "")) continue;
                    var taken = inv.TakeFromSlot(i, left, reason);
                    if (taken != null) left -= taken.count;
                }
                return left <= 0;
            }
            return ctx.TakeItem(itemId, count, reason);
        }

        private void AfterChange()
        {
            MarkDirty();
            visualsDirty = true;
        }

        // ------------------------------------------------------------
        // 置く・動かす・しまう（§5.1）
        // ------------------------------------------------------------

        private CommandResult HandlePlace(PlaceFurnitureCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            var def = ItemRegistry.Get(cmd.itemId);
            if (def == null || !FurnitureCatalog.IsPlaceable(def)) return CommandResult.Fail("それは置けません", "That cannot be placed");
            if (!RoomAllowed(ctx, cmd.location, cmd.room)) return CommandResult.Fail("ほかの人の個室です", "That is someone's private room");
            var spec = FurnitureCatalog.Resolve(def);
            if (!HouseGeometry.CanPlace(state, Layout, cmd.location, cmd.room, spec, cmd.gx, cmd.gz, cmd.rot, null, out var ja, out var en))
                return CommandResult.Fail(ja, en);
            if (!TakeExact(ctx, cmd.itemId, cmd.quality, cmd.data, 1, "housing.place"))
                return CommandResult.Fail("持ち物にありません", "You don't have it");

            PlacedFurniture f = null;
            if (!string.IsNullOrEmpty(cmd.data) && cmd.data.StartsWith("furn:"))
            {
                var old = FindFurniture(cmd.data.Substring(5));
                if (old != null && old.location == HousingConst.LocStored && old.itemId == cmd.itemId) f = old;
            }
            bool isNew = f == null;
            if (isNew)
            {
                f = new PlacedFurniture
                {
                    instanceId = Ids.New(),
                    itemId = cmd.itemId,
                    quality = cmd.quality,
                    ownerId = ctx.IssuerId,
                    acquiredDay = cmd.originDay > 0 ? cmd.originDay : Today
                };
                if (!string.IsNullOrEmpty(cmd.originKind))
                {
                    DescribeOrigin(cmd.originKind, cmd.originResidentId, f.acquiredDay, out var oja, out var oen);
                    AddMemory(f, "origin", oja, oen, cmd.originResidentId, ctx.IssuerId);
                    if (cmd.originKind == "gift") f.giftFromResidentId = cmd.originResidentId;
                }
                state.furniture.Add(f);
            }
            f.location = cmd.location;
            f.room = cmd.room;
            f.gx = cmd.gx;
            f.gz = cmd.gz;
            f.rot = cmd.rot & 3;
            f.placedDay = Today;
            AddPlacedMemory(f, ctx.IssuerId);
            if (spec.storageSlots > 0) GetOrCreateStorage(f, spec);

            // 贈り主の住民に「飾ってくれた」ことを覚えてもらう（訪問時の反応 §15.2）
            if (isNew && !string.IsNullOrEmpty(f.giftFromResidentId))
                Game.Service<IResidentDirectory>()?.AddMemory(f.giftFromResidentId, new ResidentMemoryEntry { kind = "gift_placed_at_home", playerId = ctx.IssuerId, itemId = f.itemId, day = Today, importance = 0.6f });

            EventBus.Publish(new FurniturePlacedEvent { PlayerId = ctx.IssuerId, ItemId = f.itemId, InstanceId = f.instanceId, Location = f.location });
            AfterChange();
            return CommandResult.Ok();
        }

        private void AddPlacedMemory(PlacedFurniture f, string playerId)
        {
            string whereJa, whereEn;
            if (f.location == HousingConst.LocGarden) { whereJa = "庭"; whereEn = "the garden"; }
            else
            {
                var r = HouseGeometry.GetRoom(state, f.room);
                whereJa = RoomNameJa(r);
                whereEn = RoomNameEn(r);
            }
            string extraJa = "", extraEn = "";
            var w = Game?.Service<IWeatherService>();
            if (w != null)
            {
                var c = w.Current;
                if (c.Has(AlienPhenomenon.LightRain)) { extraJa = "光雨の降る日だった。"; extraEn = " It was a day of light-rain."; }
                else if (c.Has(AlienPhenomenon.StarBand)) { extraJa = "星の光帯が見えた日だった。"; extraEn = " The star band was in the sky."; }
                else if (c.Has(AlienPhenomenon.StarWind)) { extraJa = "星風の吹く日だった。"; extraEn = " The star wind was blowing."; }
                else if (c.IsRaining) { extraJa = "雨の日だった。"; extraEn = " It was raining."; }
            }
            if (extraJa == "" && Clock != null && Clock.IsRestDay) { extraJa = "休みの日だった。"; extraEn = " It was a rest day."; }
            string who = WorldAuthority.IsMultiplayer && playerId != HouseOwnerId ? PlayerName(playerId) : null;
            AddMemory(f, "placed",
                string.Format("{0}日目、{1}{2}に置いた。{3}", Today, who != null ? who + "が" : "", whereJa, extraJa),
                string.Format("Day {0}: {1} placed in {2}.{3}", Today, who ?? "", whereEn, extraEn), null, playerId);
        }

        private CommandResult HandleMove(MoveFurnitureCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            var f = FindFurniture(cmd.instanceId);
            if (f == null || f.location == HousingConst.LocStored) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            if (!RoomAllowed(ctx, f.location, f.room) || !RoomAllowed(ctx, cmd.location, cmd.room)) return CommandResult.Fail("ほかの人の個室です", "That is someone's private room");
            var spec = HouseGeometry.Resolve(f);
            if (HouseGeometry.HasItemsOnTop(state, f)) return CommandResult.Fail("上に置いた物を先に動かしてください", "Move the things on top first");
            if (!HouseGeometry.CanPlace(state, Layout, cmd.location, cmd.room, spec, cmd.gx, cmd.gz, cmd.rot, f.instanceId, out var ja, out var en))
                return CommandResult.Fail(ja, en);
            bool roomChanged = f.location != cmd.location || f.room != cmd.room;
            f.location = cmd.location;
            f.room = cmd.room;
            f.gx = cmd.gx;
            f.gz = cmd.gz;
            f.rot = cmd.rot & 3;
            if (roomChanged) AddPlacedMemory(f, ctx.IssuerId);
            AfterChange();
            return CommandResult.Ok();
        }

        private CommandResult HandleStore(StoreFurnitureCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            var f = FindFurniture(cmd.instanceId);
            if (f == null || f.location == HousingConst.LocStored) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            // 持ち帰りは所有者だけ（§20：家具を持ち帰るには明示的な譲渡が必要）
            if (!string.IsNullOrEmpty(f.ownerId) && f.ownerId != ctx.IssuerId)
                return CommandResult.Fail("持ち主しか片付けられません（動かすことはできます）", "Only its owner can put it away (you may move it)");
            var storage = FindStorage(f.instanceId);
            if (storage != null && storage.inventory.FreeSlots() < storage.inventory.slots.Count)
                return CommandResult.Fail("中身を出してから片付けてください", "Empty it first");
            if (f.displayed.Count > 0) return CommandResult.Fail("飾った物を外してから片付けてください", "Take down the displayed items first");
            if (HouseGeometry.HasItemsOnTop(state, f)) return CommandResult.Fail("上に置いた物を先に片付けてください", "Clear the things on top first");
            if (!string.IsNullOrEmpty(f.creatureUid))
            {
                // 住んでいた生き物は家で自由に暮らす（ほかの場所へ移った後なら、その配置を上書きしない）
                var cs = Game.Service<ICreatureService>();
                if (IsCreatureLivingIn(cs, f.creatureUid, f.instanceId))
                    cs.SetPlacement(f.creatureUid, CreaturePlacement.Home, HousingConst.LocHouse);
                f.creatureUid = null;
            }
            f.location = HousingConst.LocStored;
            ctx.GiveItem(new ItemStack(f.itemId, 1, f.quality) { data = "furn:" + f.instanceId }, "housing.store");
            AfterChange();
            return CommandResult.Ok();
        }

        private CommandResult HandleSurface(SetRoomSurfaceCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            if (!RoomAllowed(ctx, HousingConst.LocHouse, cmd.room)) return CommandResult.Fail("ほかの人の個室です", "That is someone's private room");
            var room = HouseGeometry.GetRoom(state, cmd.room);
            if (room == null) return CommandResult.Fail("部屋がありません", "No room");
            var def = ItemRegistry.Get(cmd.itemId);
            if (!FurnitureCatalog.IsSurfaceDecor(def, cmd.wallpaper)) return CommandResult.Fail(cmd.wallpaper ? "壁紙ではありません" : "床材ではありません", cmd.wallpaper ? "Not wallpaper" : "Not flooring");
            if (room.kind == RoomKind.Glass && cmd.wallpaper) return CommandResult.Fail("ガラスの壁には貼れません", "Can't paper glass walls");
            if (!TakeExact(ctx, cmd.itemId, cmd.quality, null, 1, "housing.decor")) return CommandResult.Fail("持ち物にありません", "You don't have it");
            string prev = cmd.wallpaper ? room.wallpaperId : room.floorId;
            if (cmd.wallpaper) room.wallpaperId = cmd.itemId;
            else room.floorId = cmd.itemId;
            if (!string.IsNullOrEmpty(prev) && ItemRegistry.Exists(prev)) ctx.GiveItem(new ItemStack(prev, 1), "housing.decor");
            AfterChange();
            return CommandResult.Ok();
        }

        private CommandResult HandleLight(ToggleFurnitureLightCommand cmd, CommandContext ctx)
        {
            if (WorldAuthority.IsMultiplayer && !ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
            var f = FindFurniture(cmd.instanceId);
            if (f == null) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            f.lightOn = !f.lightOn;
            MarkDirty();
            var fo = GetFurnitureObject(f.instanceId);
            if (fo?.Visual?.Fx != null) fo.Visual.Fx.SetLight(f.lightOn);
            furnitureSigs.Remove(f.instanceId);
            visualsDirty = true;
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 収納・展示（§2.2, §6, マルチ仕様 §16.4）
        // ------------------------------------------------------------

        private CommandResult HandleStorage(StorageTransferCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            var f = FindFurniture(cmd.instanceId);
            if (f == null) return CommandResult.Fail("収納が見つかりません", "Storage not found");
            if (f.privateStorage && f.ownerId != ctx.IssuerId) return CommandResult.Fail("持ち主の個人用の収納です", "This is private storage");
            var spec = HouseGeometry.Resolve(f);
            if (spec.storageSlots <= 0) return CommandResult.Fail("収納ではありません", "Not a storage");
            var storage = GetOrCreateStorage(f, spec);
            int count = Mathf.Max(1, cmd.count);
            if (cmd.put)
            {
                var stack = new ItemStack(cmd.itemId, count, cmd.quality) { data = cmd.data, size = cmd.size };
                if (ItemRegistry.Get(cmd.itemId) == null) return CommandResult.Fail("不明な物です", "Unknown item");
                if (!storage.inventory.CanAdd(stack)) return CommandResult.Fail("収納がいっぱいです", "Storage is full");
                if (!TakeExact(ctx, cmd.itemId, cmd.quality, cmd.data, count, "housing.storage")) return CommandResult.Fail("持ち物にありません", "You don't have it");
                storage.inventory.Add(stack, "housing.storage");
            }
            else
            {
                int left = count;
                var inv = storage.inventory;
                float size = 0f;
                for (int i = 0; i < inv.slots.Count && left > 0; i++)
                {
                    var s = inv.slots[i];
                    if (s == null || s.IsEmpty || s.itemId != cmd.itemId || s.quality != cmd.quality || (s.data ?? "") != (cmd.data ?? "")) continue;
                    var taken = inv.TakeFromSlot(i, left, "housing.storage");
                    if (taken == null) continue;
                    size = Mathf.Max(size, taken.size);
                    left -= taken.count;
                }
                int got = count - left;
                if (got <= 0) return CommandResult.Fail("収納にありません", "Not in storage");
                ctx.GiveItem(new ItemStack(cmd.itemId, got, cmd.quality) { data = cmd.data, size = size }, "housing.storage");
            }
            MarkDirty();
            return CommandResult.Ok();
        }

        public static bool AcceptsDisplay(FurnitureSpec spec, ItemDefinition def)
        {
            if (spec == null || def == null || spec.displaySlots <= 0) return false;
            switch (spec.displayKind)
            {
                case DisplayKind.Fish: return def.Category == ItemCategory.Fish || def.HasTag(ItemTags.Fish);
                case DisplayKind.Mineral: return def.Category == ItemCategory.Ore || def.Category == ItemCategory.Gem || def.HasTag(ItemTags.Ore) || def.HasTag(ItemTags.Gem) || def.HasTag(ItemTags.Crystal);
                case DisplayKind.Food: return def.Category == ItemCategory.Food || def.Category == ItemCategory.Processed || def.HasTag(ItemTags.Edible) || def.HasTag(ItemTags.Food);
                case DisplayKind.Plant: return def.HasTag(ItemTags.Flower) || def.HasTag(ItemTags.Plant) || def.Category == ItemCategory.Crop;
                case DisplayKind.Any: return def.Category != ItemCategory.KeyItem && def.Category != ItemCategory.Letter && !(def.ModuleData is FurnitureSpec fs && fs.w * fs.d > 4);
                default: return false;
            }
        }

        private CommandResult HandleDisplay(DisplayItemCommand cmd, CommandContext ctx)
        {
            if (!CanEditCtx(ctx)) return NoPermission();
            var f = FindFurniture(cmd.instanceId);
            if (f == null) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            var spec = HouseGeometry.Resolve(f);
            if (cmd.put)
            {
                var def = ItemRegistry.Get(cmd.itemId);
                if (!AcceptsDisplay(spec, def)) return CommandResult.Fail("ここには飾れない物です", "That can't be displayed here");
                if (f.displayed.Count >= spec.displaySlots) return CommandResult.Fail("もう飾る場所がありません", "No more room");
                if (!TakeExact(ctx, cmd.itemId, cmd.quality, cmd.data, 1, "housing.display")) return CommandResult.Fail("持ち物にありません", "You don't have it");
                f.displayed.Add(new ItemStack(cmd.itemId, 1, cmd.quality) { data = cmd.data, size = cmd.size, ownerId = ctx.IssuerId });
                if (spec.use == FurnitureUse.Aquarium && def.Category == ItemCategory.Fish)
                    AddMemory(f, "display", string.Format("{0}日目、{1}を入れた。", Today, def.NameJa), string.Format("Day {0}: added a {1}.", Today, def.NameEn), null, ctx.IssuerId);
            }
            else
            {
                if (cmd.index < 0 || cmd.index >= f.displayed.Count) return CommandResult.Fail("飾った物が見つかりません", "Not found");
                var s = f.displayed[cmd.index];
                if (!string.IsNullOrEmpty(s.ownerId) && s.ownerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner)
                    return CommandResult.Fail("ほかの人が飾った物です", "Someone else displayed that");
                f.displayed.RemoveAt(cmd.index);
                var give = s.Clone();
                give.ownerId = null;
                ctx.GiveItem(give, "housing.display");
            }
            AfterChange();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 増築・家具の制作依頼（§3.2, §16.1）
        // ------------------------------------------------------------

        public int PendingBuildOrders()
        {
            int n = 0;
            foreach (var o in state.orders) if (!o.delivered && o.kind != ConstructionKinds.Commission) n++;
            return n;
        }

        private int PendingOfKind(string kind, int room = -1)
        {
            int n = 0;
            foreach (var o in state.orders) if (!o.delivered && o.kind == kind && (room < 0 || o.room == room)) n++;
            return n;
        }

        /// <summary>その増築が今頼めるか（上限・工事中）。</summary>
        public bool IsConstructionAvailable(string kind, int room, out string reasonJa, out string reasonEn)
        {
            reasonJa = reasonEn = null;
            int ground = HouseGeometry.CountRow(state, 0) + PendingOfKind(ConstructionKinds.AddRoom) + PendingOfKind(ConstructionKinds.AddGlass) + PendingOfKind(ConstructionKinds.AddWaterside) + PendingOfKind(ConstructionKinds.AddTerrace);
            if (kind != ConstructionKinds.Commission && PendingBuildOrders() >= 2) { reasonJa = "工事中の注文が多すぎます"; reasonEn = "Too many jobs in progress"; return false; }
            switch (kind)
            {
                case ConstructionKinds.AddRoom:
                case ConstructionKinds.AddGlass:
                case ConstructionKinds.AddWaterside:
                case ConstructionKinds.AddTerrace:
                    if (ground >= HousingConst.MaxGroundRooms) { reasonJa = "これ以上横には広げられません"; reasonEn = "No more space beside the house"; return false; }
                    return true;
                case ConstructionKinds.AddLoft:
                    if (HouseGeometry.CountRow(state, 1) + PendingOfKind(kind) >= HousingConst.MaxLoftRooms) { reasonJa = "上の階はもう作れません"; reasonEn = "No more upstairs rooms"; return false; }
                    return true;
                case ConstructionKinds.AddBasement:
                    if (HouseGeometry.CountRow(state, 2) + PendingOfKind(kind) >= HousingConst.MaxBasementRooms) { reasonJa = "地下はもう掘れません"; reasonEn = "No more basement rooms"; return false; }
                    return true;
                case ConstructionKinds.ExpandWidth:
                case ConstructionKinds.ExpandDepth:
                {
                    var r = HouseGeometry.GetRoom(state, room);
                    if (r == null) { reasonJa = "部屋を選んでください"; reasonEn = "Choose a room"; return false; }
                    if (PendingOfKind(ConstructionKinds.ExpandWidth, room) + PendingOfKind(ConstructionKinds.ExpandDepth, room) > 0) { reasonJa = "その部屋は工事中です"; reasonEn = "That room is under construction"; return false; }
                    if (kind == ConstructionKinds.ExpandWidth && r.widthCells + HousingConst.ExpandStep > HousingConst.MaxRoomWidth) { reasonJa = "これ以上広げられません"; reasonEn = "Can't widen further"; return false; }
                    if (kind == ConstructionKinds.ExpandDepth && r.depthCells + HousingConst.ExpandStep > HousingConst.MaxRoomDepth) { reasonJa = "これ以上広げられません"; reasonEn = "Can't deepen further"; return false; }
                    return true;
                }
                case ConstructionKinds.GardenExpand:
                    if (PendingOfKind(kind) > 0) { reasonJa = "庭の工事中です"; reasonEn = "The garden is under construction"; return false; }
                    if (state.gardenWidthCells >= HousingConst.MaxGardenWidth && state.gardenDepthCells >= HousingConst.MaxGardenDepth) { reasonJa = "庭はもう広げられません"; reasonEn = "The garden can't grow further"; return false; }
                    return true;
                case ConstructionKinds.Commission:
                    return true;
            }
            reasonJa = "不明な注文です";
            reasonEn = "Unknown order";
            return false;
        }

        private CommandResult HandleOrder(OrderConstructionCommand cmd, CommandContext ctx)
        {
            if (cmd.kind != ConstructionKinds.Commission && !CanEditCtx(ctx)) return NoPermission();
            ConstructionOption opt;
            if (cmd.kind == ConstructionKinds.Commission)
            {
                var def = ItemRegistry.Get(cmd.itemId);
                if (def == null || !FurnitureCatalog.IsPlaceable(def)) return CommandResult.Fail("作れない物です", "Can't make that");
                opt = ConstructionCatalog.Commission(def);
            }
            else opt = ConstructionCatalog.Get(cmd.kind);
            if (opt == null) return CommandResult.Fail("不明な注文です", "Unknown order");
            if (!IsConstructionAvailable(cmd.kind, cmd.room, out var ja, out var en)) return CommandResult.Fail(ja, en);

            // 素材の検証（種類と数）
            var mats = cmd.materials ?? new List<StringIntPair>();
            foreach (var req in opt.materials)
            {
                int have = 0;
                foreach (var m in mats) if (ConstructionCatalog.Matches(req.Key, ItemRegistry.Get(m.key))) have += m.value;
                if (have < req.Value) return CommandResult.Fail("素材が足りません", "Not enough materials");
            }
            foreach (var m in mats) if (m.value <= 0 || !ctx.HasItem(m.key, m.value)) return CommandResult.Fail("素材が足りません", "Not enough materials");
            if (!ctx.TryCharge(opt.price, "housing.construction:" + opt.kind)) return CommandResult.Fail("お金が足りません", "Not enough money");
            var taken = new List<StringIntPair>();
            foreach (var m in mats)
            {
                if (!ctx.TakeItem(m.key, m.value, "housing.construction"))
                {
                    // 取り消し：お金と素材を返す
                    ctx.PayMoney(opt.price, "housing.refund");
                    foreach (var t in taken) ctx.GiveItem(new ItemStack(t.key, t.value), "housing.refund");
                    return CommandResult.Fail("素材が足りません", "Not enough materials");
                }
                taken.Add(new StringIntPair(m.key, m.value));
            }
            var carpenter = Game.Service<IResidentDirectory>()?.FindByJob(ResidentJob.Carpenter);
            var order = new ConstructionOrder
            {
                orderId = Ids.New(),
                kind = opt.kind,
                room = cmd.room,
                itemId = cmd.itemId,
                orderedDay = Today,
                readyDay = Today + opt.days,
                ownerId = ctx.IssuerId,
                carpenterId = carpenter?.Id,
                paid = opt.price,
                materials = taken
            };
            state.orders.Add(order);
            MarkDirty();
            CarpenterAcknowledge(carpenter, opt.days);
            return CommandResult.Ok();
        }

        private void CarpenterAcknowledge(IResidentInfo carpenter, int days)
        {
            if (carpenter == null) return;
            var dir = Game.Service<IResidentDirectory>();
            // 「わかった。（指で日数）日で作る」：日数は身振りで示す（宇宙語が分からなくても伝わる）
            var u = new Utterance(carpenter.Id, "housing.construction_accept")
                .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.OK))
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MAKE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)).Future())
                .Face(Expression.Happy)
                .With(Gesture.Nod)
                .With(Gesture.ShowNumber, null, days);
            try { dir?.Speak(carpenter.Id, u); } catch (Exception e) { Debug.LogException(e); }
        }

        private CommandResult HandleCancelOrder(CancelConstructionCommand cmd, CommandContext ctx)
        {
            ConstructionOrder order = null;
            foreach (var o in state.orders) if (o.orderId == cmd.orderId) order = o;
            if (order == null || order.delivered) return CommandResult.Fail("注文が見つかりません", "Order not found");
            if (order.ownerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("注文した人しか取り消せません", "Only the person who ordered can cancel");
            state.orders.Remove(order);
            ctx.PayMoney(order.paid, "housing.refund");
            foreach (var m in order.materials) ctx.GiveItem(new ItemStack(m.key, m.value), "housing.refund");
            MarkDirty();
            return CommandResult.Ok("注文を取り消しました", "Order cancelled");
        }

        /// <summary>日付更新で完成した工事を反映する。</summary>
        private void CompleteConstruction(int day)
        {
            bool changed = false;
            var dir = Game.Service<IResidentDirectory>();
            for (int i = state.orders.Count - 1; i >= 0; i--)
            {
                var o = state.orders[i];
                if (o.delivered || o.readyDay > day) continue;
                ApplyOrder(o);
                o.delivered = true;
                state.orders.RemoveAt(i);
                changed = true;
                LeaveCarpenterMessage(o);
            }
            if (changed) AfterChange();
        }

        private void ApplyOrder(ConstructionOrder o)
        {
            switch (o.kind)
            {
                case ConstructionKinds.AddRoom: AddRoom(RoomKind.Normal, o.ownerId); break;
                case ConstructionKinds.AddGlass: AddRoom(RoomKind.Glass, o.ownerId); break;
                case ConstructionKinds.AddWaterside: AddRoom(RoomKind.Waterside, o.ownerId); break;
                case ConstructionKinds.AddTerrace: AddRoom(RoomKind.Terrace, o.ownerId); break;
                case ConstructionKinds.AddLoft: AddRoom(RoomKind.Loft, o.ownerId); break;
                case ConstructionKinds.AddBasement: AddRoom(RoomKind.Basement, o.ownerId); break;
                case ConstructionKinds.ExpandWidth:
                {
                    var r = HouseGeometry.GetRoom(state, o.room);
                    if (r != null) r.widthCells = Mathf.Min(HousingConst.MaxRoomWidth, r.widthCells + HousingConst.ExpandStep);
                    break;
                }
                case ConstructionKinds.ExpandDepth:
                {
                    var r = HouseGeometry.GetRoom(state, o.room);
                    if (r != null) r.depthCells = Mathf.Min(HousingConst.MaxRoomDepth, r.depthCells + HousingConst.ExpandStep);
                    break;
                }
                case ConstructionKinds.GardenExpand:
                    state.gardenWidthCells = Mathf.Min(HousingConst.MaxGardenWidth, state.gardenWidthCells + HousingConst.GardenExpandStep);
                    state.gardenDepthCells = Mathf.Min(HousingConst.MaxGardenDepth, state.gardenDepthCells + HousingConst.GardenExpandStep / 2);
                    break;
                case ConstructionKinds.Commission:
                    state.deliveries.Add(new ItemStack(o.itemId, 1) { ownerId = o.ownerId, data = "commission:" + (o.carpenterId ?? "") });
                    break;
            }
            // 部屋が増えた・広がった時、家具が範囲外にならないように確認（広げるだけなので通常は問題ない）
        }

        private void AddRoom(RoomKind kind, string ownerId)
        {
            int idx = 0;
            foreach (var r in state.rooms) idx = Mathf.Max(idx, r.index + 1);
            state.rooms.Add(new RoomData
            {
                index = idx,
                kind = kind,
                widthCells = kind == RoomKind.Terrace ? HousingConst.NewRoomWidth - 2 : HousingConst.NewRoomWidth,
                depthCells = HousingConst.NewRoomDepth,
                wallpaperId = "",
                floorId = "",
                ownerId = ""
            });
        }

        private void LeaveCarpenterMessage(ConstructionOrder o)
        {
            bool isItem = o.kind == ConstructionKinds.Commission;
            if (Game.LocalPlayerId == o.ownerId)
                UIManager.Toast(isItem ? Loc.Format("注文した{0}が届きました（収納箱で受け取れます）", "Your {0} has been delivered (collect it at the chest)", ItemRegistry.NameOf(o.itemId))
                                       : Loc.T("増築・改装が完成しました！", "The construction is finished!"));
            if (string.IsNullOrEmpty(o.carpenterId)) return;
            var speech = Game.Service<ISpeechService>();
            var manepi = Game.Service<IManepiService>();
            if (speech == null || manepi == null) return;
            var u = new Utterance(o.carpenterId, "housing.construction_done")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FINISH).Agent(Concepts.I).Patient(isItem ? MeaningArg.Of(Concepts.Item(o.itemId)) : MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)).Past())
                .Face(Expression.Happy)
                .With(Gesture.Clap)
                .Related(o.itemId);
            try
            {
                var rec = speech.CreateRecord(u, SpeechChannel.Direct, "resident", HouseInteriorSpawn);
                if (rec != null) manepi.LeaveMessage(ManepiBoardId, rec);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private CommandResult HandleTakeDelivery(TakeDeliveryCommand cmd, CommandContext ctx)
        {
            if (cmd.index < 0 || cmd.index >= state.deliveries.Count) return CommandResult.Fail("届いた物が見つかりません", "Delivery not found");
            var s = state.deliveries[cmd.index];
            if (!string.IsNullOrEmpty(s.ownerId) && s.ownerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner)
                return CommandResult.Fail("ほかの人の注文です", "That's someone else's order");
            state.deliveries.RemoveAt(cmd.index);
            ctx.GiveItem(new ItemStack(s.itemId, s.count, s.quality), "housing.delivery");
            MarkDirty();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 生き物の住処（§9）
        // ------------------------------------------------------------

        private CommandResult HandleAssignCreature(AssignCreatureCommand cmd, CommandContext ctx)
        {
            var creatures = Game.Service<ICreatureService>();
            if (creatures == null) return CommandResult.Fail("生き物の仕組みがありません", "No creature system");
            var view = creatures.Get(cmd.creatureUid);
            if (view == null) return CommandResult.Fail("生き物が見つかりません", "Creature not found");
            if (!string.IsNullOrEmpty(view.OwnerId) && view.OwnerId != ctx.IssuerId) return CommandResult.Fail("飼い主しか住まわせられません", "Only its keeper can do that");
            foreach (var other in state.furniture) if (other.creatureUid == cmd.creatureUid) other.creatureUid = null;
            string name = !string.IsNullOrEmpty(view.Nickname) ? view.Nickname : creatures.GetSpeciesName(view.SpeciesId);
            switch (cmd.mode)
            {
                case "habitat":
                {
                    var f = FindFurniture(cmd.instanceId);
                    if (f == null) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
                    if (!string.IsNullOrEmpty(f.creatureUid) && f.creatureUid != cmd.creatureUid && IsCreatureLivingIn(creatures, f.creatureUid, f.instanceId))
                        creatures.SetPlacement(f.creatureUid, CreaturePlacement.Home, HousingConst.LocHouse);
                    if (!creatures.SetPlacement(cmd.creatureUid, CreaturePlacement.Habitat, f.instanceId)) return CommandResult.Fail("今は移せないようです", "Can't move it right now");
                    f.creatureUid = cmd.creatureUid;
                    AddMemory(f, "creature", string.Format("{0}日目、{1}が住み始めた。", Today, name), string.Format("Day {0}: {1} moved in.", Today, name));
                    break;
                }
                case "home":
                    if (!creatures.SetPlacement(cmd.creatureUid, CreaturePlacement.Home, HousingConst.LocHouse)) return CommandResult.Fail("今は移せないようです", "Can't move it right now");
                    break;
                case "companion":
                    if (!creatures.SetPlacement(cmd.creatureUid, CreaturePlacement.Companion, ctx.IssuerId)) return CommandResult.Fail("今は連れて行けないようです", "Can't take it along now");
                    break;
                default:
                    if (!creatures.SetPlacement(cmd.creatureUid, CreaturePlacement.Storage, "")) return CommandResult.Fail("今は移せないようです", "Can't move it right now");
                    break;
            }
            AfterChange();
            refreshCreaturesSoon = true;
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 思い出のメモ・印（§14.3）／個人収納・個室・訪問設定（§20）
        // ------------------------------------------------------------

        private CommandResult HandleNote(FurnitureNoteCommand cmd, CommandContext ctx)
        {
            var f = FindFurniture(cmd.instanceId);
            if (f == null) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            switch (cmd.mode)
            {
                case FurnitureNoteCommand.ModeAddNote:
                {
                    if (WorldAuthority.IsMultiplayer && !ctx.HasPermission(PermissionLevel.Helper)) return NoPermission();
                    string text = (cmd.text ?? "").Trim();
                    if (text.Length == 0) return CommandResult.Fail("メモが空です", "The note is empty");
                    if (text.Length > 200) text = text.Substring(0, 200);
                    var m = AddMemory(f, "note", text, text, null, ctx.IssuerId);
                    m.pinned = true;
                    break;
                }
                case FurnitureNoteCommand.ModeRemove:
                {
                    if (cmd.index < 0 || cmd.index >= f.memories.Count) return CommandResult.Fail("見つかりません", "Not found");
                    var m = f.memories[cmd.index];
                    if (m.kind != "note") return CommandResult.Fail("自分で書いたメモだけ消せます", "Only notes can be removed");
                    if (m.playerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("書いた人だけが消せます", "Only the writer can remove it");
                    f.memories.RemoveAt(cmd.index);
                    break;
                }
                case FurnitureNoteCommand.ModePin:
                {
                    if (!CanEditCtx(ctx)) return NoPermission();
                    if (cmd.index < 0 || cmd.index >= f.memories.Count) return CommandResult.Fail("見つかりません", "Not found");
                    f.memories[cmd.index].pinned = cmd.pinned;
                    break;
                }
                case FurnitureNoteCommand.ModeSatPlayer:
                {
                    foreach (var m in f.memories) if (m.kind == "sat_player" && m.playerId == ctx.IssuerId && m.day == Today) return CommandResult.Ok();
                    string name = PlayerName(ctx.IssuerId);
                    AddMemory(f, "sat_player", string.Format("{0}日目、友達の{1}が座った。", Today, name), string.Format("Day {0}: your friend {1} sat here.", Today, name), null, ctx.IssuerId);
                    break;
                }
                default:
                    return CommandResult.Fail("不明な操作です", "Unknown");
            }
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandlePrivate(SetStoragePrivateCommand cmd, CommandContext ctx)
        {
            var f = FindFurniture(cmd.instanceId);
            if (f == null) return CommandResult.Fail("家具が見つかりません", "Furniture not found");
            if (f.ownerId != ctx.IssuerId) return CommandResult.Fail("持ち主だけが変えられます", "Only the owner can change this");
            f.privateStorage = cmd.value;
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleRoomOwner(SetRoomOwnerCommand cmd, CommandContext ctx)
        {
            if (WorldAuthority.IsMultiplayer && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("家主だけが決められます", "Only the owner can decide");
            var r = HouseGeometry.GetRoom(state, cmd.room);
            if (r == null) return CommandResult.Fail("部屋がありません", "No room");
            if (r.index == 0) return CommandResult.Fail("最初の部屋はみんなの部屋です", "The first room is shared");
            r.ownerId = cmd.ownerId ?? "";
            MarkDirty();
            return CommandResult.Ok();
        }

        private CommandResult HandleGuestEntry(SetGuestEntryCommand cmd, CommandContext ctx)
        {
            if (WorldAuthority.IsMultiplayer && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("家主だけが決められます", "Only the owner can decide");
            state.guestsMayEnter = cmd.value;
            MarkDirty();
            return CommandResult.Ok();
        }
    }
}
