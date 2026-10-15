using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    // =====================================================================
    // 星の状態を変える操作（ホスト権威：マルチ仕様 §9.2「同じ野生生物を二人が同時に捕獲した場合の確定処理」）
    // =====================================================================

    /// <summary>野生の個体を網で捕まえる。同じ個体の二重捕獲はホストが先着1名に確定する。</summary>
    [Serializable]
    public class CatchCreatureCommand : WorldCommand
    {
        public string wildId;
        public string speciesId;
        /// <summary>発行者の端末で見えていた「隙」0〜1（休憩中・食事中・真似に夢中など）。</summary>
        public float window;
        public int netLevel;
        public int catchLevel;
        public bool netTechnique;
    }

    /// <summary>持ち歩いている個体を星に置く（農場・自宅・器具・作業場など＝預ける）。</summary>
    [Serializable]
    public class PlaceCreatureCommand : WorldCommand
    {
        public string creatureJson;
        public int placement;
        public string targetId;
    }

    /// <summary>星に置いた個体の居場所を変える。</summary>
    [Serializable]
    public class MovePlacedCreatureCommand : WorldCommand
    {
        public string uid;
        public int placement;
        public string targetId;
    }

    /// <summary>星に置いた個体を引き取る（同行・ケージへ）。</summary>
    [Serializable]
    public class RetrieveCreatureCommand : WorldCommand
    {
        public string uid;
        public int placement;
    }

    /// <summary>星に置いた個体のお世話（なでる・餌・名前）。</summary>
    [Serializable]
    public class CareCreatureCommand : WorldCommand
    {
        public string uid;
        public string action; // "pet" "feed" "rename"
        public string itemId;
        public string text;
    }

    /// <summary>星に置いた個体を逃がす／ケースに入れて持ち物にする。</summary>
    [Serializable]
    public class ReleasePlacedCreatureCommand : WorldCommand
    {
        public string uid;
        public bool toCase;
    }

    public sealed partial class CreatureSystem
    {
        /// <summary>捕獲の基本成功率（希少度ごと）【仮数値】。</summary>
        private static readonly float[] CatchBaseByRarity = { 0.55f, 0.45f, 0.36f, 0.27f, 0.18f, 0.12f };

        private void RegisterCommands()
        {
            WorldAuthority.Register<CatchCreatureCommand>(HandleCatch);
            WorldAuthority.Register<PlaceCreatureCommand>(HandlePlace);
            WorldAuthority.Register<MovePlacedCreatureCommand>(HandleMovePlaced);
            WorldAuthority.Register<RetrieveCreatureCommand>(HandleRetrieve);
            WorldAuthority.Register<CareCreatureCommand>(HandleCare);
            WorldAuthority.Register<ReleasePlacedCreatureCommand>(HandleReleasePlaced);
        }

        // ---------------- 捕獲 ----------------

        public static float ComputeCatchChance(CreatureSpecies sp, float window, int catchLevel, int netLevel, bool technique, int failed)
        {
            if (sp == null) return 0f;
            if (window <= 0f) return 0.03f;
            float p = CatchBaseByRarity[Mathf.Clamp(sp.Rarity, 0, 5)] + Mathf.Clamp01(window) * 0.45f
                      + (catchLevel - 1) * 0.025f + (Mathf.Max(1, netLevel) - 1) * 0.05f + (technique ? 0.05f : 0f)
                      - Mathf.Min(3, failed) * 0.03f;
            return Mathf.Clamp(p, 0.03f, 0.97f);
        }

        private CommandResult HandleCatch(CatchCreatureCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星では生き物を捕まえられません", "You can't catch creatures on this planet");
            if (string.IsNullOrEmpty(cmd.wildId) || !wilds.TryGetValue(cmd.wildId, out var w))
                return CommandResult.Fail("逃げられてしまった…", "It got away...");
            var sp = CreatureDatabase.Get(w.speciesId);
            if (sp == null) return CommandResult.Fail("逃げられてしまった…", "It got away...");
            float chance = ComputeCatchChance(sp, cmd.window, cmd.catchLevel, cmd.netLevel, cmd.netTechnique, w.failedAttempts);
            var rng = SeededRandom.For(Game.Planet.seed ^ StableHash.Fnv1a64(w.wildId), "creatures.catch." + w.failedAttempts);
            if (!rng.Chance(chance))
            {
                w.failedAttempts++;
                MarkDirty();
                return CommandResult.Fail("網をすり抜けて逃げられた！", "It slipped through the net!");
            }
            wilds.Remove(w.wildId);
            RemoveWildView(w.wildId);
            MarkDirty();

            var c = CreateIndividual(sp, ctx.IssuerId, w.individualSeed ?? w.wildId, "caught", null);
            c.caughtAreaName = DescribePlace(w.Position, w.areaId);
            if (ctx.IsLocalIssuer)
            {
                AdoptLocal(c, null);
                OnLocalCaught(c, sp);
            }
            else
            {
                ctx.GiveItem(MakeCaseStack(c, true, null), "creature_catch");
            }
            EventBus.Publish(new CreatureCaughtEvent { PlayerId = ctx.IssuerId, SpeciesId = sp.Id, CreatureUid = c.uid, Position = w.Position });
            return CommandResult.Ok(sp.NameJa + "を捕まえた！", "Caught " + sp.NameEn + "!");
        }

        private void OnLocalCaught(OwnedCreature c, CreatureSpecies sp)
        {
            RecordCaught(sp.Id, c.size);
            TotalCaught++;
            AddCatchExp(10 + sp.Rarity * 8);
            string extra = c.colorVariant == 3 ? Loc.T("（珍しい色！）", " (rare color!)") : "";
            UIManager.Toast(Loc.Format("{0}を捕まえた！{1}", "Caught {0}!{1}", GetSpeciesName(sp.Id), extra), 4f);
            if (c.placement == CreaturePlacement.Storage)
                UIManager.Toast(Loc.T("連れて歩ける数がいっぱいなので、ケージで休ませています", "Your party is full, so it's resting in a cage"));
        }

        // ---------------- 配置（同行・預ける） ----------------

        /// <summary>
        /// 配置の変更（ICreatureService.SetPlacement の本体）。
        /// 持ち歩く個体 ⇔ 星に置く個体 の移動は WorldCommand（ホスト確定）。同行⇔ケージはローカル。
        /// </summary>
        public bool RequestPlacement(string uid, CreaturePlacement placement, string targetId, Action<bool> done)
        {
            if (placement == CreaturePlacement.Deposited || LocalPlayer == null) return false;
            string local = LocalPlayer.PlayerId;

            if (carried.TryGetValue(uid, out var c) && !placed.ContainsKey(uid))
            {
                if (c.placement == CreaturePlacement.Deposited)
                {
                    UIManager.Toast(Loc.T("ほかの星に預けている子は、その星に行かないと連れて来られません", "That creature is on another planet; go there to bring it back"));
                    return false;
                }
                if (PlacementText.IsCarried(placement))
                {
                    if (placement == CreaturePlacement.Companion && c.placement != CreaturePlacement.Companion && CompanionCount() >= MaxCompanions)
                    {
                        UIManager.Toast(Loc.Format("連れて歩けるのは{0}匹までです", "You can take up to {0} along", MaxCompanions));
                        return false;
                    }
                    c.placement = placement;
                    c.targetId = null;
                    RefreshOwnedAvatars();
                    done?.Invoke(true);
                    return true;
                }
                if (placed.Count >= MaxPlacedPerPlanet)
                {
                    UIManager.Toast(Loc.T("この星にはもう生き物を置けません", "No more room for creatures on this planet"));
                    return false;
                }
                var copy = c.Clone();
                copy.placement = placement;
                copy.targetId = targetId;
                copy.ownerId = local;
                WorldAuthority.Submit(new PlaceCreatureCommand { creatureJson = JsonUtility.ToJson(copy), placement = (int)placement, targetId = targetId }, r =>
                {
                    bool ok = r != null && r.Success;
                    if (ok && carried.TryGetValue(uid, out var cc))
                    {
                        // 控え（預けている記録）として残す。この星では星の記録が優先される
                        cc.placement = CreaturePlacement.Deposited;
                        cc.targetId = placement.ToString();
                        cc.residentWorldId = Game.Planet != null ? Game.Planet.worldId : null;
                    }
                    RefreshOwnedAvatars();
                    done?.Invoke(ok);
                });
                return true;
            }

            if (placed.TryGetValue(uid, out var p))
            {
                if (p.ownerId != local && LocalPlayer.Permission < PermissionLevel.Owner) return false;
                if (PlacementText.IsCarried(placement))
                {
                    if (p.ownerId != local) return false;
                    if (placement == CreaturePlacement.Companion && CompanionCount() >= MaxCompanions) placement = CreaturePlacement.Storage;
                    WorldAuthority.Submit(new RetrieveCreatureCommand { uid = uid, placement = (int)placement }, r => done?.Invoke(r != null && r.Success));
                }
                else
                {
                    WorldAuthority.Submit(new MovePlacedCreatureCommand { uid = uid, placement = (int)placement, targetId = targetId }, r => done?.Invoke(r != null && r.Success));
                }
                return true;
            }
            return false;
        }

        private CommandResult HandlePlace(PlaceCreatureCommand cmd, CommandContext ctx)
        {
            if (!ctx.HasPermission(PermissionLevel.Helper)) return CommandResult.Fail("この星には生き物を預けられません", "You can't leave creatures on this planet");
            OwnedCreature c = null;
            try { c = JsonUtility.FromJson<OwnedCreature>(cmd.creatureJson); }
            catch (Exception e) { Debug.LogException(e); }
            if (c == null || string.IsNullOrEmpty(c.uid) || CreatureDatabase.Get(c.speciesId) == null)
                return CommandResult.Fail("生き物の情報が読めませんでした", "Couldn't read the creature");
            if (placed.TryGetValue(c.uid, out var existing) && existing.ownerId != ctx.IssuerId)
                return CommandResult.Fail("その生き物はほかの人の子です", "That creature belongs to someone else");
            c.ownerId = ctx.IssuerId; // なりすまし防止
            c.placement = (CreaturePlacement)cmd.placement;
            c.targetId = cmd.targetId;
            c.residentWorldId = Game.Planet != null ? Game.Planet.worldId : null;
            placed[c.uid] = c;
            if (ctx.IsLocalIssuer && carried.TryGetValue(c.uid, out var stub))
            {
                stub.placement = CreaturePlacement.Deposited;
                stub.targetId = c.placement.ToString();
                stub.residentWorldId = c.residentWorldId;
            }
            MarkDirty();
            RefreshOwnedAvatars();
            return CommandResult.Ok();
        }

        private CommandResult HandleMovePlaced(MovePlacedCreatureCommand cmd, CommandContext ctx)
        {
            if (!placed.TryGetValue(cmd.uid ?? "", out var c)) return CommandResult.Fail("その生き物は見当たりません", "That creature isn't here");
            if (c.ownerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("ほかの人の生き物は動かせません", "You can't move someone else's creature");
            var p = (CreaturePlacement)cmd.placement;
            if (PlacementText.IsCarried(p)) return CommandResult.Fail("引き取りは別の操作です", "Use retrieve instead");
            c.placement = p;
            c.targetId = cmd.targetId;
            MarkDirty();
            RefreshAvatar(c.uid, false);
            return CommandResult.Ok();
        }

        private CommandResult HandleRetrieve(RetrieveCreatureCommand cmd, CommandContext ctx)
        {
            if (!placed.TryGetValue(cmd.uid ?? "", out var c)) return CommandResult.Fail("その生き物は見当たりません", "That creature isn't here");
            if (c.ownerId != ctx.IssuerId) return CommandResult.Fail("ほかの人の生き物は連れて行けません", "You can't take someone else's creature");
            placed.Remove(c.uid);
            MarkDirty();
            var p = (CreaturePlacement)cmd.placement;
            if (ctx.IsLocalIssuer) AdoptLocal(c, p.ToString());
            else ctx.GiveItem(MakeCaseStack(c, true, p.ToString()), "creature_retrieve");
            RefreshOwnedAvatars();
            return CommandResult.Ok();
        }

        // ---------------- お世話 ----------------

        public void Pet(OwnedCreature c)
        {
            if (c == null) return;
            if (IsPlaced(c.uid))
            {
                WorldAuthority.Submit(new CareCreatureCommand { uid = c.uid, action = "pet" });
            }
            else ApplyPet(c);
            var av = GetAvatar(c.uid);
            if (av != null) av.Body.ShowEmote("♥", 2f);
            if (CreatureDatabase.Get(c.speciesId) != null) RecordBehavior(c.speciesId, "pet_happy", false);
        }

        private void ApplyPet(OwnedCreature c)
        {
            int day = Clock != null ? Clock.Day : 0;
            if (c.lastPetDay == day) return;
            c.lastPetDay = day;
            c.affection = Mathf.Clamp(c.affection + 3, 0, 100);
            AddCreatureExp(c, 2, false);
        }

        /// <summary>餌をあげる（持ち物のスロットから1つ）。毎日の義務ではなく、あげると成長・なつきに良い影響（生き物仕様 §6.2）。</summary>
        public void Feed(OwnedCreature c, int slot)
        {
            if (c == null || LocalPlayer == null) return;
            var stack = LocalPlayer.Inventory.GetSlot(slot);
            if (stack == null || stack.IsEmpty) return;
            var def = stack.Definition;
            if (!CreatureDatabase.IsEdibleForCreature(def))
            {
                UIManager.Toast(Loc.Format("{0}は首をかしげた", "{0} tilted its head", DisplayName(c)));
                return;
            }
            if (c.fedCountToday >= 3)
            {
                UIManager.Toast(Loc.Format("{0}はおなかいっぱいのようだ", "{0} seems full", DisplayName(c)));
                return;
            }
            var taken = LocalPlayer.Inventory.TakeFromSlot(slot, 1, "creature_feed");
            if (taken == null) return;
            bool fav = CreatureDatabase.FoodMatches(def, c.favoriteFood);
            if (IsPlaced(c.uid)) WorldAuthority.Submit(new CareCreatureCommand { uid = c.uid, action = "feed", itemId = def.Id });
            else ApplyFeed(c, def);
            var av = GetAvatar(c.uid);
            if (av != null)
            {
                av.Body.PlayAct(1.2f);
                av.Body.ShowEmote(fav ? "♥♥" : "♪", 2.2f);
            }
            if (fav)
            {
                c.favoriteKnown = true;
                RecordBehavior(c.speciesId, "eat_favorite", false);
                UIManager.Toast(Loc.Format("{0}は{1}をとてもおいしそうに食べた！", "{0} loved the {1}!", DisplayName(c), def.Name));
            }
            else UIManager.Toast(Loc.Format("{0}は{1}を食べた", "{0} ate the {1}", DisplayName(c), def.Name));
        }

        private void ApplyFeed(OwnedCreature c, ItemDefinition def)
        {
            if (c.fedCountToday >= 3) return;
            c.fedCountToday++;
            c.lastFedDay = Clock != null ? Clock.Day : 0;
            bool fav = CreatureDatabase.FoodMatches(def, c.favoriteFood);
            if (fav) c.favoriteKnown = true;
            c.affection = Mathf.Clamp(c.affection + (fav ? 8 : 3), 0, 100);
            AddCreatureExp(c, fav ? 6 : 2, false);
        }

        public void Rename(OwnedCreature c, string name)
        {
            if (c == null) return;
            name = (name ?? "").Trim();
            if (name.Length > 16) name = name.Substring(0, 16);
            if (IsPlaced(c.uid)) WorldAuthority.Submit(new CareCreatureCommand { uid = c.uid, action = "rename", text = name });
            else c.nickname = name;
        }

        private CommandResult HandleCare(CareCreatureCommand cmd, CommandContext ctx)
        {
            if (!placed.TryGetValue(cmd.uid ?? "", out var c)) return CommandResult.Fail("その生き物は見当たりません", "That creature isn't here");
            switch (cmd.action)
            {
                case "pet":
                    ApplyPet(c);
                    break;
                case "feed":
                    ApplyFeed(c, ItemRegistry.GetOrPlaceholder(cmd.itemId));
                    break;
                case "rename":
                    if (c.ownerId != ctx.IssuerId) return CommandResult.Fail("名前は飼い主が付けます", "Only the owner can name it");
                    c.nickname = cmd.text;
                    break;
            }
            MarkDirty();
            return CommandResult.Ok();
        }

        /// <summary>逃がす（野へ帰る）。</summary>
        public void Release(OwnedCreature c)
        {
            if (c == null) return;
            var av = GetAvatar(c.uid);
            if (av != null) CreatureFx.Puff(av.transform.position + Vector3.up * 0.4f, new Color(1f, 0.9f, 0.6f), 8);
            if (IsPlaced(c.uid))
            {
                WorldAuthority.Submit(new ReleasePlacedCreatureCommand { uid = c.uid, toCase = false });
                return;
            }
            carried.Remove(c.uid);
            RefreshOwnedAvatars();
            UIManager.Toast(Loc.Format("{0}は振り返りながら野へ帰っていった", "{0} looked back once and went home to the wild", DisplayName(c)));
        }

        /// <summary>ケースに入れて持ち物にする（商店に並べる・贈る・マルチで譲る）。</summary>
        public void PutInCase(OwnedCreature c)
        {
            if (c == null || LocalPlayer == null) return;
            if (IsPlaced(c.uid))
            {
                WorldAuthority.Submit(new ReleasePlacedCreatureCommand { uid = c.uid, toCase = true });
                return;
            }
            if (c.placement == CreaturePlacement.Deposited) return;
            carried.Remove(c.uid);
            LocalPlayer.GiveOrDrop(MakeCaseStack(c, false, null), "creature_case");
            RefreshOwnedAvatars();
            UIManager.Toast(Loc.Format("{0}をケースに入れました", "Put {0} in a case", DisplayName(c)));
        }

        private CommandResult HandleReleasePlaced(ReleasePlacedCreatureCommand cmd, CommandContext ctx)
        {
            if (!placed.TryGetValue(cmd.uid ?? "", out var c)) return CommandResult.Fail("その生き物は見当たりません", "That creature isn't here");
            if (c.ownerId != ctx.IssuerId) return CommandResult.Fail("ほかの人の生き物です", "That's someone else's creature");
            placed.Remove(c.uid);
            if (cmd.toCase) ctx.GiveItem(MakeCaseStack(c, false, null), "creature_case");
            if (ctx.IsLocalIssuer) carried.Remove(c.uid);
            MarkDirty();
            RefreshOwnedAvatars();
            return CommandResult.Ok();
        }

        // ---------------- ケース（アイテム）----------------

        public ItemStack MakeCaseStack(OwnedCreature c, bool autoAdopt, string placementAfter)
        {
            var data = new CaseData { creature = c, autoAdopt = autoAdopt, placementAfter = placementAfter };
            return new ItemStack(CreatureDatabase.CaseItemId(c.speciesId), 1) { data = JsonUtility.ToJson(data), size = c.size };
        }

        public static CaseData ParseCase(ItemStack stack)
        {
            if (stack == null || string.IsNullOrEmpty(stack.data)) return null;
            try { return JsonUtility.FromJson<CaseData>(stack.data); }
            catch { return null; }
        }

        /// <summary>手に持ったケースから生き物を出す。</summary>
        public bool ReleaseFromCase(int slot)
        {
            if (LocalPlayer == null) return false;
            var stack = LocalPlayer.Inventory.GetSlot(slot);
            if (stack == null || stack.IsEmpty || !CreatureDatabase.IsCaseItem(stack.itemId)) return false;
            var sp = CreatureDatabase.Get(CreatureDatabase.SpeciesFromCase(stack.itemId));
            if (sp == null) return false;
            var taken = LocalPlayer.Inventory.TakeFromSlot(slot, 1, "creature_unbox");
            if (taken == null) return false;
            var data = ParseCase(taken);
            var c = data != null && data.creature != null && !string.IsNullOrEmpty(data.creature.uid)
                ? data.creature
                : CreateIndividual(sp, LocalPlayer.PlayerId, "case:" + Ids.New(), "case", null);
            bool wasOthers = c.ownerId != LocalPlayer.PlayerId;
            AdoptLocal(c, null);
            var e = GetEntry(sp.Id, true);
            if (wasOthers || e.obtainedCount == 0) e.obtainedCount++;
            if (e.firstSeenDay < 0) e.firstSeenDay = Clock != null ? Clock.Day : 0;
            var av = GetAvatar(c.uid);
            if (av != null) CreatureFx.Puff(av.transform.position + Vector3.up * 0.4f, sp.Main, 8);
            UIManager.Toast(Loc.Format("{0}がケースから出てきた！", "{0} came out of the case!", DisplayName(c)));
            return true;
        }

        /// <summary>ホストから届いた「自動で迎える」ケース（ゲストの捕獲・引き取り・贈り物）を自分の生き物にする。</summary>
        private void AdoptIncomingCases()
        {
            var p = LocalPlayer;
            if (p == null || p.Inventory == null) return;
            var slots = p.Inventory.slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null || s.IsEmpty || !CreatureDatabase.IsCaseItem(s.itemId) || string.IsNullOrEmpty(s.data)) continue;
                var data = ParseCase(s);
                if (data == null || !data.autoAdopt || data.creature == null) continue;
                p.Inventory.TakeFromSlot(i, 1, "creature_adopt");
                var c = data.creature;
                AdoptLocal(c, data.placementAfter);
                var sp = CreatureDatabase.Get(c.speciesId);
                if (sp == null) continue;
                if (c.origin == "caught" && c.caughtDay == (Clock != null ? Clock.Day : -1) && string.IsNullOrEmpty(data.placementAfter))
                {
                    OnLocalCaught(c, sp);
                    if (!IsAuthority) EventBus.Publish(new CreatureCaughtEvent { PlayerId = p.PlayerId, SpeciesId = sp.Id, CreatureUid = c.uid, Position = p.Position });
                }
                else
                {
                    var e = GetEntry(sp.Id, true);
                    if (e.obtainedCount == 0) e.obtainedCount = 1;
                    UIManager.Toast(Loc.Format("{0}が戻ってきた", "{0} is back with you", DisplayName(c)));
                }
            }
        }
    }
}
