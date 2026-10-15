using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Farming
{
    /// <summary>
    /// 農業用宇宙生物による自動化（農業仕様 §3・§6・§7、バランス仕様 §8.2：明確な時間短縮と生産力向上。弱くしない）。
    /// ICreatureService.GetOwned("*") から Placement==Farm の個体を集め、住み場所（PlacementTargetId=住み場所ID）の周りで働く。
    ///  毎時：水やり(6-9時)・耕す・種まき(種箱から)・収穫(収穫かご／運ぶ子がいれば倉庫へ)・運搬(かご→倉庫)・夜の光(19-2時)
    ///  毎日：土壌改良・受粉・温める・冷やす
    /// 作業範囲は (2r+1)×(2r+1) マス：r = 能力Lv（1〜4）＋好きな住み場所なら+1【仮数値】
    /// 協力作業：水やり＋土づくり＝品質+1／耕す→すぐ種まき／収穫→そのまま倉庫／夜の受粉＋光＝珍しい花色
    /// 生物は収穫物を勝手に商店へ出さない（農業仕様 §3.3）。
    /// </summary>
    public sealed partial class FarmingSystem
    {
        private const float ObserveDistance = 25f;

        private List<CreatureView> FarmCreatures()
        {
            var list = new List<CreatureView>();
            var cs = Game.Service<ICreatureService>();
            if (cs == null) return list;
            foreach (var v in cs.GetOwned(AnyOwner))
                if (v != null && v.Placement == CreaturePlacement.Farm) list.Add(v);
            return list;
        }

        public List<CreatureView> CreaturesOnFarm() => FarmCreatures();

        /// <summary>生物の住み場所（未指定・無効なら好みに合う住み場所へ自動で引っ越す）。</summary>
        private FarmHabitat ResolveHabitat(CreatureView v, bool allowAssign)
        {
            var h = GetHabitat(v.PlacementTargetId);
            if (h != null || habitats.Count == 0 || !allowAssign || !IsAuthority) return h;
            FarmHabitat best = null;
            int bestScore = int.MinValue;
            foreach (var cand in habitats)
            {
                var def = FarmCatalog.Habitat(cand.itemId);
                if (def == null) continue;
                int score = 0;
                foreach (var a in v.Abilities) if (def.Favors(a.ability)) score += 10;
                if (def.AnyCreature) score += 3;
                int residents = 0;
                foreach (var o in FarmCreatures()) if (o.PlacementTargetId == cand.id) residents++;
                score -= residents * 4;
                if (score > bestScore) { bestScore = score; best = cand; }
            }
            if (best != null && bestScore > -8)
            {
                Game.Service<ICreatureService>()?.SetPlacement(v.Uid, CreaturePlacement.Farm, best.id);
                return best;
            }
            return null;
        }

        private Vector2Int AnchorCell(CreatureView v, FarmHabitat h)
        {
            if (h != null) return new Vector2Int(h.x, h.z);
            var b = UnlockedBounds();
            ulong hash = StableHash.Fnv1a64(v.Uid ?? "x");
            return new Vector2Int(b.xMin + (int)(hash % (ulong)Mathf.Max(1, b.width)), b.yMin + (int)((hash >> 16) % (ulong)Mathf.Max(1, b.height)));
        }

        private int Range(CreatureView v, CreatureAbility a, FarmHabitat h)
        {
            int lv = v.AbilityLevel(a);
            if (lv <= 0) return 0;
            var def = h != null ? FarmCatalog.Habitat(h.itemId) : null;
            if (def != null && def.Favors(a)) lv++;
            return Mathf.Clamp(lv, 1, 5);
        }

        private IEnumerable<Vector2Int> CellsAround(Vector2Int c, int r)
        {
            for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = c.x + dx, z = c.y + dz;
                    if (InUnlocked(x, z)) yield return new Vector2Int(x, z);
                }
        }

        private bool Covers(CreatureView v, CreatureAbility a, FarmHabitat h, int x, int z)
        {
            int r = Range(v, a, h);
            if (r <= 0) return false;
            var c = AnchorCell(v, h);
            return Mathf.Abs(x - c.x) <= r && Mathf.Abs(z - c.y) <= r;
        }

        // ---------------- 毎時の作業 ----------------

        private bool RunCreatureWork(int day, int hour)
        {
            var list = FarmCreatures();
            if (list.Count == 0) return false;
            bool changed = false;
            var ws = Game.Service<IWeatherService>();
            bool raining = ws != null && ws.Current.IsRaining;
            long nowMin = Clock != null ? Clock.TotalMinutes : 0;
            var homes = new Dictionary<string, FarmHabitat>();
            foreach (var v in list) homes[v.Uid] = ResolveHabitat(v, true);
            bool anyCarrier = false;
            foreach (var v in list) if (v.AbilityLevel(CreatureAbility.Carry) > 0) anyCarrier = true;

            // 水やり（朝）。雨の日は雨が水をくれるので、水やり役は水遊びをしている
            if (hour >= 6 && hour <= 9 && !raining)
            {
                foreach (var v in list)
                {
                    var a = v.AbilityLevel(CreatureAbility.Water) > 0 ? CreatureAbility.Water : v.AbilityLevel(CreatureAbility.Splash) > 0 ? CreatureAbility.Splash : (CreatureAbility)(-1);
                    if ((int)a < 0) continue;
                    var h = homes[v.Uid];
                    int quota = Quota(v, a) * 3;
                    int n = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(AnchorCell(v, h), Range(v, a, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || (!p.tilled && !p.HasCrop) || p.watered) continue;
                        WaterPlot(p);
                        p.wateredByCreature = true;
                        if (n == 0) first = c;
                        if (++n >= quota) break;
                    }
                    if (n > 0) { Worked(v, a == CreatureAbility.Water ? "water" : "splash", n, first, day, hour); changed = true; }
                }
            }

            if (hour >= 6 && hour <= 18)
            {
                // 耕す：収穫後に荒れた畑を耕し直す
                foreach (var v in list)
                {
                    if (v.AbilityLevel(CreatureAbility.Till) <= 0) continue;
                    var h = homes[v.Uid];
                    int quota = Quota(v, CreatureAbility.Till), n = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(AnchorCell(v, h), Range(v, CreatureAbility.Till, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || !p.wasFarmed || p.tilled || p.HasCrop || HabitatAt(c.x, c.y) != null) continue;
                        p.tilled = true;
                        p.wasFarmed = false;
                        p.tilledByCreatureMinute = nowMin;
                        if (n == 0) first = c;
                        if (++n >= quota) break;
                    }
                    if (n > 0) { Worked(v, "till", n, first, day, hour); changed = true; }
                }
                // 種まき：種箱の種を空いた畑へ
                foreach (var v in list)
                {
                    if (v.AbilityLevel(CreatureAbility.Sow) <= 0) continue;
                    var h = homes[v.Uid];
                    int quota = Quota(v, CreatureAbility.Sow), n = 0;
                    bool chain = false;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(AnchorCell(v, h), Range(v, CreatureAbility.Sow, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || !p.tilled || p.HasCrop || HabitatAt(c.x, c.y) != null) continue;
                        var def = TakeSeedFromBox();
                        if (def == null) break;
                        Plant(p, def, v.OwnerId);
                        if (p.tilledByCreatureMinute == nowMin) chain = true;
                        EventBus.Publish(new CropPlantedEvent { PlayerId = v.OwnerId, CropItemId = def.CropId, Position = CellCenter(c.x, c.y) });
                        if (n == 0) first = c;
                        if (++n >= quota) break;
                    }
                    if (n > 0)
                    {
                        Worked(v, "sow", n, first, day, hour);
                        changed = true;
                        if (chain) DiscoverCombo("combo.till_sow", list, CreatureAbility.Till, CreatureAbility.Sow, first);
                    }
                }
            }

            if (hour >= 7 && hour <= 18)
            {
                // 収穫
                foreach (var v in list)
                {
                    if (v.AbilityLevel(CreatureAbility.Harvest) <= 0) continue;
                    var h = homes[v.Uid];
                    int quota = Quota(v, CreatureAbility.Harvest), n = 0, items = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(AnchorCell(v, h), Range(v, CreatureAbility.Harvest, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || !p.ripe) continue;
                        var target = anyCarrier ? warehouse : basket;
                        if (target.FreeSlots() <= 0 && (anyCarrier ? basket.FreeSlots() <= 0 : true)) break;
                        var o = HarvestPlot(p, v.OwnerId, true, 1);
                        if (o == null) continue;
                        foreach (var s in o.Stacks)
                        {
                            int left = target.Add(s, "farm_creature_harvest");
                            if (left > 0) left = basket.Add(s.CloneWithCount(left), "farm_creature_harvest");
                            items += s.count - left;
                        }
                        CollectSeeds(list, homes, o.Def, c.x, c.y, day, hour);
                        CleanupPlot(p);
                        if (n == 0) first = c;
                        if (++n >= quota) break;
                    }
                    if (n > 0)
                    {
                        Worked(v, "harvest", items, first, day, hour);
                        changed = true;
                        if (anyCarrier) DiscoverCombo("combo.harvest_carry", list, CreatureAbility.Harvest, CreatureAbility.Carry, first);
                        if (LocalPlayer != null && v.OwnerId == LocalPlayer.PlayerId) { AddExp(n); AddHarvestCount(items); }
                    }
                }
                // 運ぶ：収穫かご → 倉庫
                foreach (var v in list)
                {
                    if (v.AbilityLevel(CreatureAbility.Carry) <= 0) continue;
                    int quota = Quota(v, CreatureAbility.Carry), moved = 0;
                    for (int i = 0; i < basket.slots.Count && moved < quota; i++)
                    {
                        var s = basket.slots[i];
                        if (s == null || s.IsEmpty || !warehouse.CanAdd(s)) continue;
                        var taken = basket.TakeFromSlot(i, s.count, "farm_carry");
                        if (taken == null) continue;
                        warehouse.Add(taken, "farm_carry");
                        moved++;
                    }
                    if (moved > 0) { Worked(v, "carry", moved, new Vector2Int(-1, 0), day, hour); changed = true; }
                }
            }

            // 夜の光（ヒカリムシ・ランプルなど）
            if (hour >= 19 || hour <= 2)
            {
                int nightDay = hour <= 2 ? day - 1 : day;
                foreach (var v in list)
                {
                    if (v.AbilityLevel(CreatureAbility.Glow) <= 0) continue;
                    var h = homes[v.Uid];
                    int n = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(AnchorCell(v, h), Range(v, CreatureAbility.Glow, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || !p.HasCrop) continue;
                        if (p.litDay != nightDay)
                        {
                            p.litDay = nightDay;
                            p.glowNights++;
                            if (n == 0) first = c;
                            n++;
                        }
                    }
                    if (n > 0) { Worked(v, "glow", n, first, day, hour); changed = true; }
                }
            }
            return changed;
        }

        private int Quota(CreatureView v, CreatureAbility a) => 3 + 3 * Mathf.Max(1, v.AbilityLevel(a)) + v.Level;

        private CropDef TakeSeedFromBox()
        {
            for (int i = 0; i < seedBox.slots.Count; i++)
            {
                var s = seedBox.slots[i];
                if (s == null || s.IsEmpty) continue;
                var def = FarmCatalog.BySeed(s.itemId);
                if (def == null) continue;
                seedBox.TakeFromSlot(i, 1, "farm_creature_sow");
                return def;
            }
            return null;
        }

        private void CollectSeeds(List<CreatureView> list, Dictionary<string, FarmHabitat> homes, CropDef def, int x, int z, int day, int hour)
        {
            if (def == null) return;
            foreach (var v in list)
            {
                int lv = v.AbilityLevel(CreatureAbility.SeedCollect);
                if (lv <= 0 || !Covers(v, CreatureAbility.SeedCollect, homes[v.Uid], x, z)) continue;
                var rng = SeededRandom.For(Game.Planet.seed, "farm.seedcollect." + x + "." + z + "." + day + "." + hour + "." + v.Uid);
                if (!rng.Chance(0.4f + 0.15f * lv)) continue;
                seedBox.Add(new ItemStack(def.SeedId, 1), "farm_seedcollect");
                Worked(v, "seedcollect", 1, new Vector2Int(x, z), day, hour);
            }
        }

        // ---------------- 毎日の作業 ----------------

        private void RunDailyCreatureWork(int day)
        {
            var list = FarmCreatures();
            if (list.Count == 0) return;
            var homes = new Dictionary<string, FarmHabitat>();
            foreach (var v in list) homes[v.Uid] = ResolveHabitat(v, true);
            int hour = 7;
            foreach (var v in list)
            {
                var h = homes[v.Uid];
                var anchor = AnchorCell(v, h);
                // 土を豊かにする
                int lv = v.AbilityLevel(CreatureAbility.SoilImprove);
                if (lv > 0)
                {
                    int n = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(anchor, Range(v, CreatureAbility.SoilImprove, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || (!p.tilled && !p.HasCrop)) continue;
                        if (p.soil >= 3f) continue;
                        p.soil = Mathf.Min(3f, p.soil + 0.5f * lv);
                        if (n == 0) first = c;
                        n++;
                        // 水やり＋土づくり（同じ畑）
                        foreach (var w in list)
                        {
                            if (w == v) continue;
                            if (Covers(w, CreatureAbility.Water, homes[w.Uid], c.x, c.y) || Covers(w, CreatureAbility.Splash, homes[w.Uid], c.x, c.y))
                            {
                                if (p.HasCrop) p.waterSoilComboDay = day;
                                DiscoverCombo("combo.water_soil", list, CreatureAbility.Water, CreatureAbility.SoilImprove, c);
                                break;
                            }
                        }
                    }
                    if (n > 0) Worked(v, "soil", n, first, day, hour);
                }
                // 受粉
                if (v.AbilityLevel(CreatureAbility.Pollinate) > 0)
                {
                    int n = 0;
                    Vector2Int first = default;
                    foreach (var c in CellsAround(anchor, Range(v, CreatureAbility.Pollinate, h)))
                    {
                        var p = GetPlot(c.x, c.y);
                        if (p == null || !p.HasCrop || p.ripe || p.pollinated) continue;
                        var def = FarmCatalog.Get(p.cropKey);
                        if (def == null || (def.Kind == CropKind.Vegetable && (def.Likes & CropLikes.Pollen) == 0) || def.Kind == CropKind.Grain) continue;
                        p.pollinated = true;
                        if (def.RareColorVariantId != null && p.glowNights > 0) DiscoverCombo("combo.pollinate_glow", list, CreatureAbility.Pollinate, CreatureAbility.Glow, c);
                        if (n == 0) first = c;
                        n++;
                    }
                    if (n > 0) Worked(v, "pollinate", n, first, day, hour);
                }
                // 温める・冷やす
                if (v.AbilityLevel(CreatureAbility.Heat) > 0) Mark(v, h, CreatureAbility.Heat, day, hour, (p) => p.heatDay = day, "heat");
                if (v.AbilityLevel(CreatureAbility.Cool) > 0) Mark(v, h, CreatureAbility.Cool, day, hour, (p) => p.coolDay = day, "cool");
            }
        }

        private void Mark(CreatureView v, FarmHabitat h, CreatureAbility a, int day, int hour, System.Action<FarmPlot> act, string action)
        {
            int n = 0;
            Vector2Int first = default;
            foreach (var c in CellsAround(AnchorCell(v, h), Range(v, a, h)))
            {
                var p = GetPlot(c.x, c.y);
                if (p == null || !p.HasCrop) continue;
                act(p);
                if (n == 0) first = c;
                n++;
            }
            if (n > 0) Worked(v, action, n, first, day, hour);
        }

        // ---------------- 演出・観察 ----------------

        /// <summary>作業の記録・見た目（生物がその区画へ行って働く）・観察イベント（近くで見ていれば図鑑に記録）。</summary>
        private void Worked(CreatureView v, string action, int count, Vector2Int cell, int day, int hour)
        {
            Log(v, action, count, day, hour);
            Vector3 pos = cell.x >= 0 ? CellCenter(cell.x, cell.y) : WarehousePosition();
            SendToAvatar(v.Uid, "UchujinCreatureWork", pos);
            var local = LocalPlayer;
            if (local != null && local.CurrentAreaId == AreaIds.Overworld && (local.Position - pos).sqrMagnitude < ObserveDistance * ObserveDistance)
                EventBus.Publish(new CreatureObservedEvent { PlayerId = local.PlayerId, SpeciesId = v.SpeciesId, Behavior = "work." + action });
        }

        private void DiscoverCombo(string key, List<CreatureView> list, CreatureAbility a, CreatureAbility b, Vector2Int cell)
        {
            if (combos.Contains(key)) return;
            combos.Add(key);
            var local = LocalPlayer;
            var pos = CellCenter(cell.x, cell.y);
            bool near = local != null && (local.Position - pos).sqrMagnitude < ObserveDistance * ObserveDistance;
            if (local != null) UIManager.Toast(Loc.T("農場で生き物たちの息の合った働きぶりが見られた！（農場アプリに記録）", "Your farm creatures worked together beautifully! (Noted in the Farm app)"), 5f);
            if (!near) return;
            foreach (var v in list)
                if (v.AbilityLevel(a) > 0 || v.AbilityLevel(b) > 0)
                    EventBus.Publish(new CreatureObservedEvent { PlayerId = local.PlayerId, SpeciesId = v.SpeciesId, Behavior = key });
        }

        private void SendToAvatar(string uid, string message, Vector3 pos)
        {
            var root = Game != null ? Game.SceneRoot : null;
            if (root == null) return;
            var t = root.Find("OwnedCreatures/Creature_" + uid);
            if (t != null) t.gameObject.SendMessage(message, pos, SendMessageOptions.DontRequireReceiver);
        }

        /// <summary>農場の生物に住み場所の位置を伝える（見た目：その周りで暮らす）。</summary>
        private void SendCreatureAnchors()
        {
            foreach (var v in FarmCreatures())
            {
                var h = GetHabitat(v.PlacementTargetId);
                var c = AnchorCell(v, h);
                SendToAvatar(v.Uid, "UchujinCreatureAnchor", CellCenter(c.x, c.y) + (h != null ? new Vector3(0.6f, 0, 0.6f) : Vector3.zero));
            }
        }

        // ---------------- 農場に遊びに来る住民（農業仕様 §14.2） ----------------

        private class PendingVisit
        {
            public string ResidentId;
            public string Topic;
            public long UntilMinute;
        }
        private PendingVisit pendingVisit;

        private void UpdateFarmVisitors(int day, int hour)
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null || Clock == null) return;
            var center = CellCenter(UnlockedBounds().xMin + UnlockedBounds().width / 2, UnlockedBounds().yMin - 1);
            if (pendingVisit != null)
            {
                var r = dir.Get(pendingVisit.ResidentId);
                if (r == null || !r.IsActive || Clock.TotalMinutes > pendingVisit.UntilMinute) { pendingVisit = null; return; }
                if ((r.Position - center).sqrMagnitude < 10f * 10f)
                {
                    VisitorSpeak(dir, r, pendingVisit.Topic);
                    pendingVisit = null;
                }
                return;
            }
            if ((hour != 10 && hour != 15) || lastVisitDay == day) return;
            if (Game.State != GameState.Playing) return; // 演出中（プロローグ等）は住民を呼ばない
            string topic = PickVisitTopic();
            if (topic == null) return;
            var rng = SeededRandom.For(Game.Planet.seed, "farm.visit." + day + "." + hour);
            var candidates = new List<IResidentInfo>();
            foreach (var r in dir.ActiveResidents)
                if (r.Activity != ResidentActivity.Sleeping && r.Activity != ResidentActivity.Working) candidates.Add(r);
            var master = dir.FindByJob(ResidentJob.FarmingMaster);
            IResidentInfo pick = master != null && master.IsActive && rng.Chance(0.4f) ? master : rng.Pick(candidates);
            if (pick == null) return;
            lastVisitDay = day;
            pendingVisit = new PendingVisit { ResidentId = pick.Id, Topic = topic, UntilMinute = Clock.TotalMinutes + 150 };
            dir.RequestGoTo(pick.Id, center, Mathf.Min(23 * 60, Clock.MinuteOfDay + 150), "farm.visit");
        }

        private string PickVisitTopic()
        {
            int creatures = FarmCreatures().Count;
            int ripe = 0, flowers = 0, planted = 0;
            foreach (var p in plots.Values)
            {
                if (!p.HasCrop) continue;
                planted++;
                if (p.ripe) ripe++;
                var d = FarmCatalog.Get(p.cropKey);
                if (d != null && d.Kind == CropKind.Flower && p.ripe) flowers++;
            }
            if (creatures >= 2) return "creatures";
            if (flowers >= 3) return "flowers";
            if (ripe >= 6) return "harvest";
            if (planted >= 10) return "field";
            return null;
        }

        private void VisitorSpeak(IResidentDirectory dir, IResidentInfo r, string topic)
        {
            var u = new Utterance(r.Id, "farm.visit." + topic);
            string factJa, factEn;
            if (r.Job == ResidentJob.FarmingMaster)
            {
                int dry = 0;
                foreach (var p in plots.Values) if (p.HasCrop && !p.watered) dry++;
                if (dry >= 5 && Clock.Hour >= 12)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Request, Concepts.WATER_VERB).Patient(MeaningArg.Of(Concepts.CROP).Plural())).Face(Expression.Worried).With(Gesture.LookUp).With(Gesture.PointAt, "farm");
                    factJa = "農場に来て、乾いた畑を指さしていた"; factEn = "Came to the farm and pointed at the dry plots";
                }
                else
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(Concepts.FARM)).Adverb(Concepts.VERY)).Face(Expression.Happy).With(Gesture.Nod).With(Gesture.Clap);
                    factJa = "農場に来て、満足そうにうなずいていた"; factEn = "Came to the farm and nodded approvingly";
                }
            }
            else switch (topic)
            {
                case "creatures":
                    u.Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.WOW))
                     .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CUTE).Agent(MeaningArg.Of(Concepts.CREATURE).Plural()).Adverb(Concepts.VERY))
                     .Face(Expression.Excited).With(Gesture.Clap);
                    factJa = "農場に遊びに来て、働く生き物たちを見て目を輝かせていた"; factEn = "Visited the farm, eyes shining at the working creatures";
                    break;
                case "flowers":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(MeaningArg.Of(Concepts.FLOWER).Plural())).Face(Expression.Love).With(Gesture.Hug);
                    factJa = "農場の花を見て、うっとりしていた"; factEn = "Gazed dreamily at the farm's flowers";
                    break;
                case "harvest":
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.MANY).Agent(MeaningArg.Of(Concepts.CROP).Plural()))
                     .Say(MeaningFrame.Make(SpeechAct.Desire, Concepts.EAT).Agent(MeaningArg.Of(Concepts.I)).Patient(MeaningArg.Of(Concepts.CROP)))
                     .Face(Expression.Happy).With(Gesture.Jump);
                    factJa = "実った作物を見て、跳びはねて喜んでいた"; factEn = "Hopped with joy seeing the ripe crops";
                    break;
                default:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GROW).Agent(MeaningArg.Of(Concepts.CROP).Plural()).Adverb(Concepts.ALREADY)).Face(Expression.Happy).With(Gesture.Nod);
                    factJa = "農場の畑をのぞき込んで、にこにこしていた"; factEn = "Peeked at the fields, smiling";
                    break;
            }
            dir.Speak(r.Id, u, SpeechChannel.Overheard);
            var local = LocalPlayer;
            if (local != null && (local.Position - r.Position).sqrMagnitude < 30f * 30f)
                Game.Service<ITabletService>()?.RecordResidentObservation(r.Id, factJa, factEn);
            dir.AddMemory(r.Id, new ResidentMemoryEntry { kind = "visited_farm", playerId = Game.Planet != null ? Game.Planet.hostPlayerId : null, day = Clock.Day, importance = 0.3f, detail = topic });
        }
    }
}
