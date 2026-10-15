using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Quests
{
    // 自発的なお手伝い（§7）：受注ボタンなしで、状況を見て手伝う。
    // 重い荷物を運ぶ住民／道の落とし物／迷子の生き物／雨の日の洗濯物。
    public sealed partial class QuestSystem
    {
        /// <summary>ローカルプレイヤーが今運んでいる荷物・連れている生き物。</summary>
        internal string LocalCarryHelpId;
        internal string LocalGuideHelpId;

        private void DailyHelpReset(int day)
        {
            S.helps.RemoveAll(h =>
            {
                if (h.done) return true;
                if (h.kind == HelpKind.LostItem) return day - h.day > QuestTuning.LostItemOwnerGiveUpDays;
                // 運び残した荷物・迷子・洗濯物は、その日のうちに住民が自分で片付けた
                return h.day < day;
            });
        }

        private void HourlyHelp(int day, int hour)
        {
            if (hour < QuestTuning.HelpSpawnStartHour || hour >= QuestTuning.HelpSpawnEndHour) return;
            int active = 0;
            foreach (var h in S.helps) if (!h.done && h.day == day && h.kind != HelpKind.LostItem) active++;
            if (active >= QuestTuning.MaxHelpEventsActive) return;
            var rng = Rng("quests.help." + day + "." + hour);
            if (!rng.Chance(QuestTuning.HelpSpawnChancePerHour)) return;

            var residents = ActiveResidents();
            residents.RemoveAll(r => r.Activity == ResidentActivity.Sleeping);
            if (residents.Count == 0) return;
            var r0 = rng.Pick(residents);
            bool hasSpecies = QuestCatalog.Species().Count > 0;
            var weights = new float[] { 0.4f, 0.35f, hasSpecies ? 0.25f : 0f };
            switch (rng.WeightedIndex(weights))
            {
                case 0: SpawnCarry(r0, rng, "errand"); break;
                case 1: SpawnLostItem(r0, rng); break;
                case 2:
                {
                    var expert = Dir?.FindByJob(ResidentJob.CreatureExpert);
                    SpawnLostCreature(expert != null && expert.IsActive && rng.Chance(0.5f) ? expert : r0, rng);
                    break;
                }
            }
        }

        private Vector3 RoadPointAwayFrom(Vector3 from, float minDist, float maxDist, SeededRandom rng)
        {
            var l = Layout;
            var candidates = new List<Vector3>();
            if (l != null && l.Roads != null)
            {
                foreach (var n in l.Roads.nodes)
                {
                    float d = Vector3.Distance(new Vector3(n.x, 0, n.z), new Vector3(from.x, 0, from.z));
                    if (d >= minDist && d <= maxDist) candidates.Add(n);
                }
            }
            if (candidates.Count > 0) return Ground(rng.Pick(candidates));
            var c = rng.InsideUnitCircle().normalized * Mathf.Lerp(minDist, maxDist, 0.5f);
            return Ground(from + new Vector3(c.x, 0f, c.y));
        }

        internal HelpEvent SpawnCarry(IResidentInfo r, SeededRandom rng, string reasonTag, bool fromHome = false)
        {
            if (r == null) return null;
            Vector3 home = HomeOf(r);
            Vector3 start, target;
            if (fromHome)
            {
                // 引っ越しの荷物：家から村の外れの荷車へ
                start = home + new Vector3(rng.Range(-1.5f, 1.5f), 0f, rng.Range(-1.5f, 1.5f));
                target = RoadPointAwayFrom(home, 18f, 45f, rng);
            }
            else
            {
                start = RoadPointAwayFrom(home, 14f, 40f, rng);
                target = home;
            }
            var h = new HelpEvent
            {
                id = NewId("hp"),
                kind = HelpKind.CarryLoad,
                residentId = r.Id,
                itemId = QuestItemIds.MovingBox,
                position = Ground(start),
                target = Ground(target),
                day = Today,
                reasonTag = reasonTag,
                color = Color.HSVToRGB(rng.Range(0.05f, 0.12f), 0.45f, 0.85f)
            };
            S.helps.Add(h);
            // 住民は荷物のそばで困っている（重そうに持ち上げようとする）
            GoTo(r.Id, start + new Vector3(0.8f, 0f, 0.6f), Mathf.Min(Minute + 60, TimeSystem.MinutesPerDay - 1), "quest.help.carry");
            return h;
        }

        private void SpawnLostItem(IResidentInfo owner, SeededRandom rng)
        {
            var pos = RoadPointAwayFrom(HomeOf(owner), 8f, 60f, rng);
            var itemId = rng.Pick(QuestItemIds.LostItems);
            foreach (var h0 in S.helps) if (!h0.done && h0.kind == HelpKind.LostItem && h0.residentId == owner.Id) return;
            S.helps.Add(new HelpEvent
            {
                id = NewId("hp"),
                kind = HelpKind.LostItem,
                residentId = owner.Id,
                itemId = itemId,
                position = pos + new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)),
                day = Today,
                color = ItemRegistry.GetOrPlaceholder(itemId).Color
            });
        }

        private void SpawnLostCreature(IResidentInfo owner, SeededRandom rng)
        {
            var species = QuestCatalog.Species();
            if (species.Count == 0 || owner == null) return;
            Vector3 pos;
            var l = Layout;
            var habitats = new List<HabitatPoint>();
            if (l != null)
            {
                foreach (var hp in l.HabitatPoints)
                {
                    float d = Vector3.Distance(hp.position, HomeOf(owner));
                    if (d > 15f && d < 70f && (hp.area == AreaType.Meadow || hp.area == AreaType.Forest || hp.area == AreaType.Riverside)) habitats.Add(hp);
                }
            }
            pos = habitats.Count > 0 ? Ground(rng.Pick(habitats).position) : RoadPointAwayFrom(HomeOf(owner), 15f, 50f, rng);
            var sp = rng.Pick(species);
            S.helps.Add(new HelpEvent
            {
                id = NewId("hp"),
                kind = HelpKind.LostCreature,
                residentId = owner.Id,
                speciesId = sp,
                position = pos,
                target = HomeOf(owner),
                day = Today,
                color = Color.HSVToRGB(StableHash.Unit(seed, "creature.color:" + sp), 0.5f, 0.95f)
            });
        }

        /// <summary>雨が降り始めたら、留守の家の洗濯物が濡れそうになる（§7）。</summary>
        private void SpawnLaundry()
        {
            int hour = Minute / 60;
            if (hour < 7 || hour >= 19) return;
            var rng = Rng("quests.laundry." + Today + "." + Minute);
            var residents = ActiveResidents();
            rng.Shuffle(residents);
            int n = 0;
            foreach (var r in residents)
            {
                if (n >= QuestTuning.LaundryEventsPerRain) break;
                if (r.Activity == ResidentActivity.AtHome || r.Activity == ResidentActivity.Sleeping) continue;
                bool exists = false;
                foreach (var h in S.helps) if (h.kind == HelpKind.Laundry && h.residentId == r.Id && !h.done) exists = true;
                if (exists) continue;
                var home = HomeOf(r);
                S.helps.Add(new HelpEvent
                {
                    id = NewId("hp"),
                    kind = HelpKind.Laundry,
                    residentId = r.Id,
                    position = Ground(home + new Vector3(2.5f, 0f, 1.5f)),
                    target = home,
                    day = Today,
                    color = r.MainColor
                });
                n++;
            }
        }

        private void RemoveUnclaimedLaundry()
        {
            S.helps.RemoveAll(h => h.kind == HelpKind.Laundry && !h.done && string.IsNullOrEmpty(h.claimedBy));
        }

        // ------------------------------------------------------------
        // コマンド
        // ------------------------------------------------------------

        private CommandResult HandleHelp(QuestHelpCommand c, CommandContext ctx)
        {
            var h = FindHelp(c.helpId);
            if (h == null || h.done) return CommandResult.Fail("もう片付いたようです", "It seems already taken care of");
            var action = (HelpAction)c.action;
            switch (action)
            {
                case HelpAction.Claim:
                    if (!string.IsNullOrEmpty(h.claimedBy) && h.claimedBy != ctx.IssuerId)
                        return CommandResult.Fail("ほかの人が手伝っています", "Someone else is helping");
                    h.claimedBy = ctx.IssuerId;
                    switch (h.kind)
                    {
                        case HelpKind.CarryLoad:
                            // 住民が先に立って目的地へ歩く（言葉なしで行き先が分かる）
                            GoTo(h.residentId, h.target + new Vector3(1.2f, 0f, 0.8f), Mathf.Min(Minute + 120, TimeSystem.MinutesPerDay - 1), "quest.help.carry");
                            break;
                        case HelpKind.LostItem:
                            ctx.GiveItem(new ItemStack(h.itemId, 1) { data = QuestItemIds.DataLostPrefix + h.id + "|" + h.residentId }, "found_on_road");
                            break;
                        case HelpKind.Laundry:
                            h.done = true;
                            RewardHelp(h.residentId, ctx, "laundry", null);
                            AddPending(ctx.IssuerId, h.residentId, "laundry_thanks", null, null);
                            break;
                    }
                    break;
                case HelpAction.Complete:
                    if (h.claimedBy != ctx.IssuerId) return CommandResult.Fail(null, null);
                    h.done = true;
                    if (h.kind == HelpKind.CarryLoad)
                    {
                        RewardHelp(h.residentId, ctx, h.reasonTag == "move_out" ? "packing" : "carry", null);
                        var r = R(h.residentId);
                        if (r != null && Vector3.Distance(r.Position, h.target) > 18f)
                            AddPending(ctx.IssuerId, h.residentId, "help_thanks", null, null);
                        // 届けた荷物で家の模様替えが進む（§19.2）
                        if (h.reasonTag != "move_out" && r != null) AddLifeMark("unpacked", h.residentId, null, HomeOf(r), h.color);
                    }
                    else if (h.kind == HelpKind.LostCreature)
                    {
                        RewardHelp(h.residentId, ctx, "lost_creature", h.speciesId);
                        if (Dir?.Get(h.residentId)?.Job == ResidentJob.CreatureExpert)
                            Memory(h.residentId, "helped", ctx.IssuerId, null, null, 0.8f, "creature:" + h.speciesId);
                    }
                    break;
                case HelpAction.Release:
                    if (h.claimedBy == ctx.IssuerId) h.claimedBy = null;
                    break;
            }
            Changed();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // ローカルの操作（荷物を持つ・生き物を連れる・洗濯物を取り込む）
        // ------------------------------------------------------------

        internal void InteractHelp(HelpEvent h, PlayerState p)
        {
            if (h == null || p == null || h.done) return;
            switch (h.kind)
            {
                case HelpKind.CarryLoad:
                    if (LocalCarryHelpId == h.id)
                    {
                        // その場に下ろす（やめる）
                        LocalCarryHelpId = null;
                        WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Release });
                        return;
                    }
                    if (!string.IsNullOrEmpty(LocalCarryHelpId)) return;
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Claim }, res =>
                    {
                        if (res == null || !res.Success) return;
                        LocalCarryHelpId = h.id;
                        var u = new Utterance(h.residentId, QuestSpeech.TagHelp)
                            .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                            .Say(MeaningFrame.Make(SpeechAct.Invitation, Concepts.GO).Agent(Concepts.WE).Adverb(Concepts.TOGETHER))
                            .Face(Expression.Happy).With(Gesture.Beckon);
                        var r = R(h.residentId);
                        if (r != null) u.With(Gesture.PointAt, h.reasonTag == "move_out" ? "cart" : r.HomeFacilityId);
                        BookAttach(BookEnsureHelp("help:" + h.id, h.residentId), Say(h.residentId, u), BookScene.AfterHelp);
                    });
                    break;
                case HelpKind.LostItem:
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Claim });
                    break;
                case HelpKind.LostCreature:
                    if (LocalGuideHelpId == h.id) return;
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Claim }, res =>
                    {
                        if (res != null && res.Success) LocalGuideHelpId = h.id;
                    });
                    break;
                case HelpKind.Laundry:
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Claim });
                    BookEnsureHelp("help:" + h.id, h.residentId);
                    break;
            }
        }

        private void LocalHelpCheck()
        {
            var p = LocalPlayer;
            if (p == null || p.Avatar == null) return;
            if (!string.IsNullOrEmpty(LocalCarryHelpId))
            {
                var h = FindHelp(LocalCarryHelpId);
                if (h == null || h.done || (h.claimedBy != null && h.claimedBy != p.PlayerId && !string.IsNullOrEmpty(h.claimedBy)))
                {
                    LocalCarryHelpId = null;
                }
                else if (Flat(p.Position - h.target) < QuestTuning.CarryArriveRadius)
                {
                    LocalCarryHelpId = null;
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Complete });
                    var r = R(h.residentId);
                    if (r != null && Flat(r.Position - p.Position) < 25f)
                        BookAttach(BookEnsureHelp("help:" + h.id, h.residentId), Say(h.residentId, QuestSpeech.CarryThanks(h.residentId)), BookScene.AfterHelp);
                }
            }
            if (!string.IsNullOrEmpty(LocalGuideHelpId))
            {
                var h = FindHelp(LocalGuideHelpId);
                var r = h != null ? R(h.residentId) : null;
                if (h == null || h.done || r == null)
                {
                    LocalGuideHelpId = null;
                }
                else if (Flat(r.Position - p.Position) < QuestTuning.GuideArriveRadius + 1.5f)
                {
                    LocalGuideHelpId = null;
                    WorldAuthority.Submit(new QuestHelpCommand { helpId = h.id, action = (int)HelpAction.Complete });
                    var u = new Utterance(h.residentId, QuestSpeech.TagHelpThanks)
                        .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                        .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.RETURN).Agent(MeaningArg.Of(Concepts.Species(h.speciesId)).OwnedBy(Concepts.I)).Past())
                        .Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS).Recipient(Concepts.YOU).Adverb(Concepts.VERY))
                        .Face(Expression.VeryHappy).With(Gesture.Hug);
                    BookAttach(BookEnsureHelp("help:" + h.id, h.residentId), Say(h.residentId, u), BookScene.AfterHelp);
                }
            }
        }

        internal static float Flat(Vector3 v)
        {
            v.y = 0f;
            return v.magnitude;
        }
    }
}
