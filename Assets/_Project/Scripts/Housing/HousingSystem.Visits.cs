using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Housing
{
    [Serializable] public class InviteResidentCommand : WorldCommand { public string residentId; public int day; public int hour; }
    [Serializable] public class CancelInvitationCommand : WorldCommand { public string invitationId; }

    /// <summary>住民の会話メニューに「家に招待する」「増築を頼む」などを足す（§11, §3.2）。</summary>
    public sealed class HousingResidentProvider : IResidentInteractionProvider
    {
        private readonly HousingSystem sys;
        private int lastFrame = -1;
        private string lastResident;
        public HousingResidentProvider(HousingSystem sys) { this.sys = sys; }

        public IEnumerable<ResidentInteractionOption> GetOptions(IResidentInfo resident, PlayerState player)
        {
            // 星を離れた後も住民側の登録が残る可能性があるので、プレイ中の時だけ返す
            if (sys == null || sys.Game == null || sys.Game.State != GameState.Playing || resident == null) return new List<ResidentInteractionOption>();
            // 二重登録の保険：同じフレームに同じ住民で2回呼ばれたら2回目は空
            if (UnityEngine.Time.frameCount == lastFrame && lastResident == resident.Id) return new List<ResidentInteractionOption>();
            lastFrame = UnityEngine.Time.frameCount;
            lastResident = resident.Id;
            return sys.GetResidentOptions(resident, player);
        }
    }

    // 住民の招待と訪問（家具仕様 §11、§12：家具と宇宙語の解読、§15.2）。
    public sealed partial class HousingSystem
    {
        private readonly Dictionary<string, ResidentProxy> visitorProxies = new Dictionary<string, ResidentProxy>();
        private bool visitorProxiesDirty;
        private Transform visitorsRoot;
        private readonly HashSet<string> greeted = new HashSet<string>();
        private readonly Dictionary<string, float> reactTimeout = new Dictionary<string, float>();
        private float musicReactCooldown;

        // ------------------------------------------------------------
        // 会話メニュー
        // ------------------------------------------------------------

        public List<ResidentInteractionOption> GetResidentOptions(IResidentInfo r, PlayerState p)
        {
            var list = new List<ResidentInteractionOption>();
            if (r == null || p == null || !r.IsActive) return list;

            if (r.Job == ResidentJob.Carpenter)
            {
                list.Add(new ResidentInteractionOption("家具を買う", "Buy furniture", () => OpenCarpenterShop(r, p), 40));
                list.Add(new ResidentInteractionOption("増築・改装を頼む", "Ask for building work", () => UIManager.Open(new ConstructionWindow(this, r, p)), 41));
                list.Add(new ResidentInteractionOption("家具を作ってもらう", "Commission furniture", () => UIManager.Open(new CommissionWindow(this, r, p)), 42));
                var crafting = Game.Service<ICraftingService>();
                if (crafting != null)
                    list.Add(new ResidentInteractionOption("作業台で家具を作る", "Craft at the bench", () => crafting.OpenStation(CraftStationType.CarpenterBench, r.Id, p), 43));
            }

            var pending = FindPendingInvitation(r.Id);
            if (pending != null && pending.playerId == p.PlayerId)
            {
                list.Add(new ResidentInteractionOption("家に招く約束を取り消す", "Cancel the invitation", () =>
                    WorldAuthority.Submit(new CancelInvitationCommand { invitationId = pending.id }), 61));
            }
            else if (pending == null && r.Knows(p.PlayerId) && r.GetFriendship(p.PlayerId) >= HousingConst.InviteMinFriendship && r.Activity != ResidentActivity.Packing)
            {
                list.Add(new ResidentInteractionOption("家に招待する", "Invite to your home", () => OpenInviteChoice(r, p), 60));
            }
            return list;
        }

        public Invitation FindPendingInvitation(string residentId)
        {
            foreach (var i in state.invitations)
                if (i.residentId == residentId && (i.status == InvitationStatus.Planned || i.status == InvitationStatus.Arriving || i.status == InvitationStatus.Inside))
                    return i;
            return null;
        }

        private void OpenInviteChoice(IResidentInfo r, PlayerState p)
        {
            if (Clock == null) return;
            int day = Clock.Day, hour = Clock.Hour;
            var options = new List<ChoiceOption>();
            Action<int, int> invite = (d, h) => WorldAuthority.Submit(new InviteResidentCommand { residentId = r.Id, day = d, hour = h });
            if (hour < 11) options.Add(new ChoiceOption(Loc.T("今日のお昼（13時）", "Today at noon (13:00)"), () => invite(day, 13)));
            if (hour < 16) options.Add(new ChoiceOption(Loc.T("今日の夕方（18時）", "This evening (18:00)"), () => invite(day, 18)));
            options.Add(new ChoiceOption(Loc.T("明日のお昼（13時）", "Tomorrow at noon (13:00)"), () => invite(day + 1, 13)));
            options.Add(new ChoiceOption(Loc.T("明日の夕方（18時）", "Tomorrow evening (18:00)"), () => invite(day + 1, 18)));
            options.Add(new ChoiceOption(Loc.T("やめる", "Never mind"), null));
            int seats = SeatCount();
            UIManager.Choose(Loc.T("家に招待する", "Invite home"),
                Loc.Format("いつ来てもらう？（身振りと時計で伝える）\n家の座れる場所：{0}", "When should they come? (You show it with gestures and the clock.)\nSeats at home: {0}", seats),
                options);
        }

        public int SeatCount()
        {
            int n = 0;
            foreach (var f in state.furniture)
            {
                if (f.location != HousingConst.LocHouse) continue;
                var s = HouseGeometry.Resolve(f);
                if (s.use == FurnitureUse.Sit) n += Mathf.Max(1, s.seats);
            }
            return n;
        }

        // ------------------------------------------------------------
        // 招待の受け付け（ホスト）
        // ------------------------------------------------------------

        private CommandResult HandleInvite(InviteResidentCommand cmd, CommandContext ctx)
        {
            var dir = Game.Service<IResidentDirectory>();
            var r = dir?.Get(cmd.residentId);
            if (r == null || !r.IsActive) return CommandResult.Fail("その住民はいないようです", "That resident isn't around");
            if (Clock == null) return CommandResult.Fail("時間がありません", "No time");
            if (cmd.day < Today || (cmd.day == Today && cmd.hour <= Clock.Hour)) return CommandResult.Fail("その時間はもう過ぎています", "That time has passed");
            if (FindPendingInvitation(r.Id) != null) return CommandResult.Fail("もう約束しています", "Already invited");
            int same = 0;
            foreach (var i in state.invitations)
                if (i.day == cmd.day && i.hour == cmd.hour && (i.status == InvitationStatus.Planned || i.status == InvitationStatus.Arriving)) same++;
            if (same >= HousingConst.MaxVisitorsAtOnce) return CommandResult.Fail("その時間はもう大勢を招いています", "Too many guests at that time");

            int friendship = r.GetFriendship(ctx.IssuerId);
            var rng = SeededRandom.For(planetSeed, "housing.invite." + r.Id + "." + cmd.day + "." + cmd.hour + "." + ctx.IssuerId);
            bool accept = r.Activity != ResidentActivity.Packing && rng.Chance(Mathf.Clamp(0.55f + friendship / 150f, 0.3f, 0.97f));
            var inv = new Invitation
            {
                id = Ids.New(),
                residentId = r.Id,
                playerId = ctx.IssuerId,
                day = cmd.day,
                hour = cmd.hour,
                status = accept ? InvitationStatus.Planned : InvitationStatus.Declined
            };
            state.invitations.Add(inv);
            MarkDirty();

            // 返事は宇宙語＋身振り（うなずく／首を振る、時刻を指で示す）
            Utterance u;
            if (accept)
            {
                u = new Utterance(r.Id, "housing.invite_accept")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)).When(cmd.day == Today ? Concepts.TODAY : Concepts.TOMORROW).Future())
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I))
                    .Face(Expression.Happy)
                    .With(Gesture.Nod)
                    .With(Gesture.ShowNumber, null, cmd.hour);
            }
            else
            {
                u = new Utterance(r.Id, "housing.invite_decline")
                    .Say(MeaningFrame.Make(SpeechAct.Apology, Concepts.SORRY))
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)).When(cmd.day == Today ? Concepts.TODAY : Concepts.TOMORROW).Future().Not())
                    .Face(Expression.Worried)
                    .With(Gesture.ShakeHead);
            }
            try { dir.Speak(r.Id, u); } catch (Exception e) { Debug.LogException(e); }
            return CommandResult.Ok();
        }

        private CommandResult HandleCancelInvite(CancelInvitationCommand cmd, CommandContext ctx)
        {
            foreach (var i in state.invitations)
            {
                if (i.id != cmd.invitationId) continue;
                if (i.playerId != ctx.IssuerId && ctx.Permission < PermissionLevel.Owner) return CommandResult.Fail("約束した人だけが取り消せます", "Only the inviter can cancel");
                if (i.status != InvitationStatus.Planned && i.status != InvitationStatus.Arriving) return CommandResult.Fail("もう取り消せません", "Too late to cancel");
                i.status = InvitationStatus.Cancelled;
                MarkDirty();
                var dir = Game.Service<IResidentDirectory>();
                var u = new Utterance(i.residentId, "housing.invite_cancelled")
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, Concepts.OK))
                    .Face(Expression.Neutral)
                    .With(Gesture.Nod);
                try { dir?.Speak(i.residentId, u); } catch (Exception e) { Debug.LogException(e); }
                return CommandResult.Ok();
            }
            return CommandResult.Fail("約束が見つかりません", "Invitation not found");
        }

        // ------------------------------------------------------------
        // 訪問の進行（ホストのみ。5分ごと）
        // ------------------------------------------------------------

        private void UpdateVisits()
        {
            if (Clock == null || Game == null) return;
            var dir = Game.Service<IResidentDirectory>();
            long now = Clock.TotalMinutes;
            bool changed = false;
            foreach (var inv in state.invitations.ToArray())
            {
                long start = TimeSystem.ToTotalMinutes(inv.day, inv.hour, 0);
                var r = dir?.Get(inv.residentId);
                switch (inv.status)
                {
                    case InvitationStatus.Planned:
                        if (r == null || !r.IsActive) { inv.status = InvitationStatus.Cancelled; changed = true; break; }
                        if (now >= start - 45)
                        {
                            inv.status = InvitationStatus.Arriving;
                            int until = Mathf.Min(TimeSystem.MinutesPerDay - 1, inv.hour * 60 + HousingConst.VisitGraceMinutes + HousingConst.VisitDurationMinutes);
                            try { dir.RequestGoTo(inv.residentId, PlayerHomeEntrance, until, "housing.visit"); } catch (Exception e) { Debug.LogException(e); }
                            changed = true;
                        }
                        break;
                    case InvitationStatus.Arriving:
                        if (now < start) break;
                        if (r == null || !r.IsActive) { inv.status = InvitationStatus.Cancelled; changed = true; break; }
                        if (!sleeping && HostPresent(inv)) { StartVisit(inv, r); changed = true; }
                        else if (now >= start + HousingConst.VisitGraceMinutes || sleeping) { MissVisit(inv, r); changed = true; }
                        break;
                    case InvitationStatus.Inside:
                        if (r == null || !r.IsActive || now >= inv.endTotalMinutes || !AnyPlayerInHouse() || sleeping) { EndVisit(inv, r); changed = true; break; }
                        long startedAt = inv.endTotalMinutes - HousingConst.VisitDurationMinutes;
                        int due = (int)((now - startedAt) / HousingConst.VisitStepMinutes);
                        if (due > inv.steps) { VisitStep(inv, r); changed = true; }
                        break;
                }
            }
            if (changed)
            {
                MarkDirty();
                visitorProxiesDirty = true;
            }
        }

        private bool HostPresent(Invitation inv)
        {
            if (inv.spontaneous) return AnyPlayerInHouse();
            return PlayerInHouse(inv.playerId) || AnyPlayerInHouse();
        }

        private void StartVisit(Invitation inv, IResidentInfo r)
        {
            inv.status = InvitationStatus.Inside;
            inv.endTotalMinutes = Clock.TotalMinutes + HousingConst.VisitDurationMinutes;
            inv.steps = 0;
            inv.enjoyment = 0f;
            inv.usedInstances.Clear();
            inv.currentInstanceId = "";
            int until = Mathf.Min(TimeSystem.MinutesPerDay - 1, Clock.MinuteOfDay + HousingConst.VisitDurationMinutes);
            try { Game.Service<IResidentDirectory>()?.RequestGoTo(inv.residentId, PlayerHomeEntrance, until, "housing.visit_inside"); } catch (Exception e) { Debug.LogException(e); }
            if (Game.LocalPlayerId == inv.playerId || inv.spontaneous)
                UIManager.Toast(Loc.Format("{0}が遊びに来た！", "{0} came to visit!", ResidentName(inv.residentId)));
        }

        private void MissVisit(Invitation inv, IResidentInfo r)
        {
            inv.status = InvitationStatus.Missed;
            // 留守だった：家庭用マネピに伝言を預ける（§13.2 留守中の訪問）
            var u = new Utterance(inv.residentId, "housing.visit_missed")
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)).Past())
                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.YOU).Not().Past())
                .Face(Expression.Sad)
                .With(Gesture.Shrug);
            LeaveHomeMessage(u);
            try
            {
                Game.Service<IResidentDirectory>()?.AddMemory(inv.residentId, new ResidentMemoryEntry { kind = "visit_missed", playerId = inv.playerId, day = Today, importance = 0.3f });
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void LeaveHomeMessage(Utterance u)
        {
            var speech = Game.Service<ISpeechService>();
            var manepi = Game.Service<IManepiService>();
            if (speech == null || manepi == null) return;
            try
            {
                var rec = speech.CreateRecord(u, SpeechChannel.Direct, "resident", HouseInteriorSpawn);
                if (rec != null) manepi.LeaveMessage(ManepiBoardId, rec);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------
        // 家具を選ぶ（好み・職業・体型・贈り物・生き物）
        // ------------------------------------------------------------

        private PlacedFurniture ChooseFurnitureFor(IResidentInfo r, Invitation inv)
        {
            PlacedFurniture best = null;
            float bestScore = float.MinValue;
            var rng = SeededRandom.For(planetSeed, "housing.visitstep." + inv.id + "." + inv.steps);
            var occupied = new Dictionary<string, int>();
            foreach (var other in state.invitations)
                if (other != inv && other.status == InvitationStatus.Inside && !string.IsNullOrEmpty(other.currentInstanceId))
                {
                    occupied.TryGetValue(other.currentInstanceId, out int n);
                    occupied[other.currentInstanceId] = n + 1;
                }
            foreach (var f in state.furniture)
            {
                if (f.location != HousingConst.LocHouse) continue;
                var def = ItemRegistry.GetOrPlaceholder(f.itemId);
                var spec = FurnitureCatalog.Resolve(def);
                if (spec.surface == FurnitureSurface.Rug && spec.use != FurnitureUse.SoundFloor) continue;
                if (spec.use == FurnitureUse.Storage || spec.use == FurnitureUse.Sleep) continue;
                occupied.TryGetValue(f.instanceId, out int occ);
                if (spec.use == FurnitureUse.Sit && occ >= Mathf.Max(1, spec.seats)) continue;
                float score = 0.2f + SafeEvaluate(r, def) + Affinity(r, f, def, spec) + rng.Range(-0.15f, 0.15f);
                if (inv.usedInstances.Contains(f.instanceId)) score -= 0.9f;
                if (!inv.ateMeal && spec.use == FurnitureUse.Table && HasFood(f)) score += 0.8f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = f;
                }
            }
            return best;
        }

        private static float SafeEvaluate(IResidentInfo r, ItemDefinition def)
        {
            try { return Mathf.Clamp(r.EvaluateItem(def), -1f, 1f); }
            catch (Exception e) { Debug.LogException(e); return 0f; }
        }

        private static bool HasFood(PlacedFurniture f)
        {
            foreach (var s in f.displayed)
            {
                var d = s?.Definition;
                if (d != null && (d.Category == ItemCategory.Food || d.Category == ItemCategory.Processed || d.HasTag(ItemTags.Edible))) return true;
            }
            return false;
        }

        /// <summary>職業・体型（10系統 §8）・贈り物・生き物による興味。</summary>
        private float Affinity(IResidentInfo r, PlacedFurniture f, ItemDefinition def, FurnitureSpec spec)
        {
            float a = 0f;
            switch (r.Job)
            {
                case ResidentJob.FishingMaster: if (spec.use == FurnitureUse.Aquarium || spec.displayKind == DisplayKind.Fish) a += 0.6f; break;
                case ResidentJob.Musician: if (spec.use == FurnitureUse.Music || spec.use == FurnitureUse.SoundFloor) a += 0.7f; break;
                case ResidentJob.Artist: if (spec.style == FurnitureStyle.Painting || spec.use == FurnitureUse.Music) a += 0.5f; break;
                case ResidentJob.CreatureExpert: if (spec.IsHabitat || !string.IsNullOrEmpty(f.creatureUid)) a += 0.6f; break;
                case ResidentJob.Cook: if (spec.use == FurnitureUse.Table || def.HasTag(ItemTags.Kitchen)) a += 0.6f; break;
                case ResidentJob.Florist:
                case ResidentJob.FarmingMaster: if (spec.use == FurnitureUse.Plant) a += 0.6f; break;
                case ResidentJob.Miner:
                case ResidentJob.Blacksmith: if (spec.displayKind == DisplayKind.Mineral || def.HasTag(ItemTags.Crystal)) a += 0.5f; break;
                case ResidentJob.Scholar:
                case ResidentJob.Teacher: if (spec.use == FurnitureUse.Book) a += 0.6f; break;
                case ResidentJob.Child: if (spec.use == FurnitureUse.Toy || spec.use == FurnitureUse.SoundFloor) a += 0.6f; break;
                case ResidentJob.Carpenter: if (spec.series == FurnitureSeries.Natural || spec.series == FurnitureSeries.Country || spec.series == FurnitureSeries.Antique) a += 0.35f; break;
                case ResidentJob.Retired: if (spec.use == FurnitureUse.Sit) a += 0.4f; break;
                case ResidentJob.WeatherReader: if (spec.use == FurnitureUse.Window) a += 0.6f; break;
                case ResidentJob.Traveler: if (spec.use == FurnitureUse.Window || spec.style == FurnitureStyle.Painting) a += 0.4f; break;
                case ResidentJob.Merchant: if (def.BasePrice >= 5000) a += 0.4f; break;
            }
            switch (r.Lineage)
            {
                case AlienLineage.Round: if (spec.style == FurnitureStyle.Cushion || spec.soft) a += 0.4f; break;
                case AlienLineage.Mochi: if (spec.wobble || spec.soft) a += 0.5f; break;
                case AlienLineage.Plush: if (spec.style == FurnitureStyle.Nest || spec.style == FurnitureStyle.Cushion) a += 0.4f; break;
                case AlienLineage.ChibiAnimal: if (spec.use == FurnitureUse.Sit && spec.seatHeight < 0.3f) a += 0.5f; break;
                case AlienLineage.Floating: if (spec.floating || spec.surface == FurnitureSurface.Ceiling || spec.style == FurnitureStyle.Swing) a += 0.5f; break;
                case AlienLineage.MultiArm: if (spec.use == FurnitureUse.Music) a += 0.5f; break;
                case AlienLineage.StarChild: if (spec.series == FurnitureSeries.Starry || spec.glowing) a += 0.5f; break;
                case AlienLineage.Lifestyle: if (spec.style == FurnitureStyle.ToolRack || spec.use == FurnitureUse.Display) a += 0.4f; break;
                case AlienLineage.Expressive: if (spec.use == FurnitureUse.Mirror) a += 0.6f; break;
                case AlienLineage.Mixed: if (spec.shapeShift) a += 0.5f; break;
            }
            if (spec.lineage >= 0 && spec.lineage == (int)r.Lineage) a += 0.6f;
            if (f.giftFromResidentId == r.Id) a += 0.7f;
            if (!string.IsNullOrEmpty(f.creatureUid)) a += 0.2f;
            if (f.displayed.Count > 0) a += 0.15f;
            return a;
        }

        // ------------------------------------------------------------
        // 1回の行動
        // ------------------------------------------------------------

        private void VisitStep(Invitation inv, IResidentInfo r)
        {
            inv.steps++;
            var f = ChooseFurnitureFor(r, inv);
            inv.currentInstanceId = f != null ? f.instanceId : "";
            if (f != null) reactTimeout[inv.id] = UnityEngine.Time.time + 12f;
            else React(inv, r, null);
            // 複数人のとき、住民同士の会話（§11.3）
            MaybeVisitorsChat(inv);
        }

        /// <summary>訪問者の姿が家具に着いた（ホスト）。</summary>
        private void OnProxyArrived(ResidentProxy proxy)
        {
            if (!IsAuthority || proxy == null) return;
            var inv = FindInvitation(proxy.InvitationId);
            if (inv == null || inv.status != InvitationStatus.Inside) return;
            if (proxy.TargetInstanceId != inv.currentInstanceId || string.IsNullOrEmpty(inv.currentInstanceId)) return;
            if (!reactTimeout.ContainsKey(inv.id)) return;
            reactTimeout.Remove(inv.id);
            var r = Game.Service<IResidentDirectory>()?.Get(inv.residentId);
            if (r == null) return;
            React(inv, r, FindFurniture(inv.currentInstanceId));
        }

        private Invitation FindInvitation(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var i in state.invitations) if (i.id == id) return i;
            return null;
        }

        private void React(Invitation inv, IResidentInfo r, PlacedFurniture f)
        {
            if (r == null) return;
            var dir = Game.Service<IResidentDirectory>();
            string name = ResidentName(r.Id);
            Utterance u;
            string memKind = "visit_look";
            string memJa = null, memEn = null, obsJa = null, obsEn = null;
            float like = 0f;

            if (f == null)
            {
                // 座る場所も見る物もない：部屋を見回す
                u = new Utterance(r.Id, "housing.visit_room")
                    .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SMALL).Agent(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)))
                    .Face(Expression.Thinking).With(Gesture.Think);
                SpeakAsVisitor(inv, r, u);
                inv.enjoyment = Mathf.Clamp(inv.enjoyment - 0.05f, -1f, 1f);
                return;
            }

            var def = ItemRegistry.GetOrPlaceholder(f.itemId);
            var spec = FurnitureCatalog.Resolve(def);
            like = SafeEvaluate(r, def);
            var itemArg = MeaningArg.Of(def.EffectiveConceptId);
            var color = Concepts.ColorFromTags(def);
            if (color != null) itemArg.Adj(color);
            u = new Utterance(r.Id, "housing.visit_furniture").Related(def.Id).With(Gesture.PointAt, f.instanceId);

            // 贈ってくれた住民が見に来た（§15.2・§22.1）
            if (f.giftFromResidentId == r.Id)
            {
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.THIS)).Recipient(Concepts.YOU).Past());
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.HAPPY).Agent(Concepts.I).Adverb(Concepts.VERY));
                like = Mathf.Max(like, 0.5f);
                memKind = "visit_gift";
                memJa = string.Format("{0}日目、贈ってくれた{1}が見に来て、うれしそうだった。", Today, name);
                memEn = string.Format("Day {0}: {1}, who gave it, came to see it and looked happy.", Today, name);
            }

            bool ateNow = false;
            switch (spec.use)
            {
                case FurnitureUse.Sit:
                    memKind = memKind == "visit_gift" ? memKind : "visit_sat";
                    if (spec.soft) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SOFT).Agent(MeaningArg.Of(Concepts.THIS)));
                    else if (spec.seatHeight > 0.9f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FLOAT).Agent(Concepts.I));
                    memJa = memJa ?? string.Format("{0}日目、{1}が座った。", Today, name);
                    memEn = memEn ?? string.Format("Day {0}: {1} sat here.", Today, name);
                    obsJa = string.Format("{0}日目：家に来て{1}に座った。", Today, def.NameJa);
                    obsEn = string.Format("Day {0}: came over and sat on the {1}.", Today, def.NameEn);
                    break;
                case FurnitureUse.Music:
                    if (r.Job == ResidentJob.Musician || r.Lineage == AlienLineage.MultiArm || like > 0.3f)
                    {
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PLAY).Agent(Concepts.I).Patient(Concepts.MUSIC));
                        var fo = GetFurnitureObject(f.instanceId);
                        if (fo != null) ProceduralMusic.PlayTune(fo.transform.position, spec.style, StableHash.Derive(planetSeed, r.Id) ^ (ulong)Today);
                        memKind = "visit_music";
                        memJa = string.Format("{0}日目、{1}が演奏した。", Today, name);
                        memEn = string.Format("Day {0}: {1} played music here.", Today, name);
                        obsJa = string.Format("{0}日目：家の{1}で演奏した。", Today, def.NameJa);
                        obsEn = string.Format("Day {0}: played the {1} at my home.", Today, def.NameEn);
                        ListenersReact(inv, f, def);
                    }
                    break;
                case FurnitureUse.Aquarium:
                {
                    int fish = 0;
                    foreach (var s in f.displayed) if (s != null && !s.IsEmpty && s.Definition.Category == ItemCategory.Fish) fish++;
                    if (fish > 0)
                    {
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.FISH).Qty(fish)));
                        float fishLike = SafeEvaluate(r, f.displayed[0].Definition);
                        if (fishLike > 0.3f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(MeaningArg.Of(Concepts.FISH)));
                        like = Mathf.Max(like, fishLike);
                    }
                    else u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EXIST).Agent(Concepts.FISH).Not());
                    break;
                }
                case FurnitureUse.Display:
                    if (f.displayed.Count > 0)
                    {
                        var shown = f.displayed[0].Definition;
                        float shownLike = SafeEvaluate(r, shown);
                        u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.I).Patient(MeaningArg.Of(shown.EffectiveConceptId)));
                        if (shownLike > 0.3f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(MeaningArg.Of(shown.EffectiveConceptId)));
                        like = Mathf.Max(like, shownLike);
                        u.Related(shown.Id);
                    }
                    break;
                case FurnitureUse.Table:
                    ateNow = TryEatAtTable(inv, r, f, u, ref memJa, ref memEn, ref obsJa, ref obsEn, ref like);
                    if (ateNow) memKind = "visit_meal";
                    break;
                case FurnitureUse.Light:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, f.lightOn ? Concepts.BRIGHT : Concepts.DARK).Agent(MeaningArg.Of(Concepts.LAMP)));
                    if (f.lightOn && spec.glowing && like > 0.3f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BEAUTIFUL).Agent(MeaningArg.Of(Concepts.THIS)));
                    break;
                case FurnitureUse.Plant:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, like > 0.2f ? Concepts.BEAUTIFUL : Concepts.GROW).Agent(MeaningArg.Of(Concepts.FLOWER)));
                    break;
                case FurnitureUse.Book:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.READ).Agent(Concepts.I));
                    memJa = string.Format("{0}日目、{1}が本を読んでいた。", Today, name);
                    memEn = string.Format("Day {0}: {1} read a book here.", Today, name);
                    break;
                case FurnitureUse.Window:
                {
                    var w = Game.Service<IWeatherService>();
                    string concept = w != null ? w.Current.MainConcept() : Concepts.SUNNY;
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.SEE).Agent(Concepts.I).Patient(concept));
                    break;
                }
                case FurnitureUse.Mirror:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LAUGH).Agent(Concepts.I));
                    like = Mathf.Max(like, r.Lineage == AlienLineage.Expressive ? 0.6f : like);
                    break;
                case FurnitureUse.Toy:
                case FurnitureUse.SoundFloor:
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PLAY).Agent(Concepts.I));
                    if (like > 0f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FUN).Agent(MeaningArg.Of(Concepts.THIS)));
                    if (spec.use == FurnitureUse.SoundFloor)
                    {
                        var fo = GetFurnitureObject(f.instanceId);
                        if (fo != null) ProceduralMusic.PlayTune(fo.transform.position, spec.style, StableHash.Derive(planetSeed, r.Id + "floor"));
                    }
                    break;
            }

            // 住処の生き物（§9：生物好きは一緒に遊ぶ）
            if (!string.IsNullOrEmpty(f.creatureUid))
            {
                var creatures = Game.Service<ICreatureService>();
                var cv = creatures?.Get(f.creatureUid);
                if (cv != null)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.CUTE).Agent(MeaningArg.Of(Concepts.Species(cv.SpeciesId))));
                    AttractCreaturesTo(GetFurnitureObject(f.instanceId)?.transform.position ?? HouseInteriorSpawn, 2.5f);
                    if (r.Job == ResidentJob.CreatureExpert) like = Mathf.Max(like, 0.5f);
                    string cname = !string.IsNullOrEmpty(cv.Nickname) ? cv.Nickname : creatures.GetSpeciesName(cv.SpeciesId);
                    memKind = "visit_creature";
                    memJa = string.Format("{0}日目、{1}が{2}と遊んだ。", Today, name, cname);
                    memEn = string.Format("Day {0}: {1} played with {2}.", Today, name, cname);
                }
            }

            // 高い・大きい（家具を使う住民の行動は宇宙語の手掛かり §12）
            if (!ateNow && def.BasePrice >= 8000) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PRICE_EXPENSIVE).Agent(MeaningArg.Of(Concepts.THIS)).Adverb(Concepts.VERY));
            else if (!ateNow && (spec.seats >= 3 || spec.w * spec.d >= 16)) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.BIG).Agent(MeaningArg.Of(Concepts.THIS)));

            // 好き／好きではない（その住民の本当の好みと矛盾させない）
            if (!ateNow)
            {
                float trueLike = SafeEvaluate(r, def);
                if (trueLike > 0.35f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(itemArg));
                else if (trueLike < -0.35f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.LIKE).Agent(Concepts.I).Patient(itemArg).Not());
            }
            if (u.sentences.Count == 0) u.Say(MeaningFrame.Make(SpeechAct.Exclamation, like > 0f ? Concepts.WOW : Concepts.HMM));

            u.Face(like > 0.6f ? Expression.VeryHappy : like > 0.25f ? Expression.Happy : like > -0.2f ? Expression.Neutral : Expression.Worried);
            if (like > 0.6f) u.With(Gesture.Clap);
            else if (like < -0.35f) u.With(Gesture.Shrug);

            var record = SpeakAsVisitor(inv, r, u);
            inv.enjoyment = Mathf.Clamp(inv.enjoyment + like * 0.3f + 0.05f, -1f, 1f);
            inv.usedInstances.Add(f.instanceId);

            // 家具の思い出（§14）
            if (memJa == null)
            {
                memJa = string.Format("{0}日目、{1}が眺めていた。", Today, name);
                memEn = string.Format("Day {0}: {1} looked at it for a while.", Today, name);
            }
            AddMemory(f, memKind, memJa, memEn, r.Id, inv.playerId, record?.recordId);

            // 住民辞典には「観察できた事実」だけを残す（内部の好みは書かない）
            if (obsJa != null && inv.steps <= 3)
            {
                var tablet = Game.Service<ITabletService>();
                try { tablet?.RecordResidentObservation(r.Id, obsJa, obsEn, record?.recordId); } catch (Exception e) { Debug.LogException(e); }
            }
            MarkDirty();
        }

        private bool TryEatAtTable(Invitation inv, IResidentInfo r, PlacedFurniture f, Utterance u, ref string memJa, ref string memEn, ref string obsJa, ref string obsEn, ref float like)
        {
            if (inv.ateMeal) return false;
            for (int i = 0; i < f.displayed.Count; i++)
            {
                var s = f.displayed[i];
                var food = s?.Definition;
                if (food == null || !(food.Category == ItemCategory.Food || food.Category == ItemCategory.Processed || food.HasTag(ItemTags.Edible))) continue;
                float foodLike = SafeEvaluate(r, food);
                u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.EAT).Agent(Concepts.I).Patient(MeaningArg.Of(food.EffectiveConceptId)));
                u.Say(MeaningFrame.Make(SpeechAct.Statement, foodLike >= 0f ? Concepts.DELICIOUS : Concepts.BAD_TASTE).Agent(MeaningArg.Of(Concepts.THIS)).Adverb(foodLike > 0.6f ? Concepts.VERY : Concepts.A_LITTLE));
                u.With(Gesture.Eat).Related(food.Id);
                f.displayed.RemoveAt(i);
                inv.ateMeal = true;
                like = foodLike;
                string name = ResidentName(r.Id);
                memJa = string.Format("{0}日目、{1}とここで食事した（{2}）。", Today, name, food.NameJa);
                memEn = string.Format("Day {0}: had a meal here with {1} ({2}).", Today, name, food.NameEn);
                obsJa = string.Format("{0}日目：家で{1}を食べた。", Today, food.NameJa);
                obsEn = string.Format("Day {0}: ate {1} at my home.", Today, food.NameEn);
                try
                {
                    Game.Service<IResidentDirectory>()?.AddMemory(r.Id, new ResidentMemoryEntry { kind = "ate_at_player_home", playerId = inv.playerId, itemId = food.Id, day = Today, importance = 0.5f + foodLike * 0.2f });
                }
                catch (Exception e) { Debug.LogException(e); }
                visualsDirty = true;
                return true;
            }
            return false;
        }

        private UtteranceRecord SpeakAsVisitor(Invitation inv, IResidentInfo r, Utterance u)
        {
            if (visitorProxies.TryGetValue(inv.id, out var proxy) && proxy != null)
            {
                foreach (var g in u.gestures) proxy.PlayGesture(g.gesture);
                var speech = Game.Service<ISpeechService>();
                if (speech != null)
                {
                    try { return speech.Speak(u, proxy.transform, SpeechChannel.Direct, "resident"); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            try { return Game.Service<IResidentDirectory>()?.Speak(r.Id, u); }
            catch (Exception e) { Debug.LogException(e); return null; }
        }

        /// <summary>演奏を聴いた他の訪問者の反応（演奏会 §11.3）。</summary>
        private void ListenersReact(Invitation player, PlacedFurniture instrument, ItemDefinition def)
        {
            var dir = Game.Service<IResidentDirectory>();
            foreach (var other in state.invitations)
            {
                if (other == player || other.status != InvitationStatus.Inside) continue;
                var r = dir?.Get(other.residentId);
                if (r == null) continue;
                float l = SafeEvaluate(r, def);
                var u = new Utterance(r.Id, "housing.visit_listen")
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, l > 0f ? Concepts.WOW : Concepts.HMM))
                    .Face(l > 0.3f ? Expression.Excited : Expression.Neutral)
                    .With(l > 0f ? Gesture.Clap : Gesture.Nod);
                if (l > 0.3f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.GOOD).Agent(MeaningArg.Of(Concepts.MUSIC)));
                SpeakAsVisitor(other, r, u);
                other.enjoyment = Mathf.Clamp(other.enjoyment + l * 0.15f, -1f, 1f);
            }
        }

        private void MaybeVisitorsChat(Invitation inv)
        {
            var inside = new List<Invitation>();
            foreach (var i in state.invitations) if (i.status == InvitationStatus.Inside) inside.Add(i);
            if (inside.Count < 2) return;
            var rng = SeededRandom.For(planetSeed, "housing.chat." + inv.id + "." + inv.steps);
            if (!rng.Chance(0.35f)) return;
            var a = inv;
            Invitation b = null;
            foreach (var i in inside) if (i != a) { b = i; break; }
            if (b == null) return;
            var dir = Game.Service<IResidentDirectory>();
            var ra = dir?.Get(a.residentId);
            var rb = dir?.Get(b.residentId);
            if (ra == null || rb == null) return;
            var u = new Utterance(ra.Id, "housing.visit_chat").Related(rb.Id);
            if (a.enjoyment > 0.1f) u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FUN).Agent(Concepts.WE).Adverb(Concepts.TOGETHER));
            else u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.TALK).Agent(Concepts.I).Recipient(Concepts.Name(rb.Id)));
            u.Face(a.enjoyment > 0.1f ? Expression.Happy : Expression.Neutral).With(Gesture.PointAt, rb.Id);
            SpeakAsVisitorOverheard(a, ra, u);
            var reply = new Utterance(rb.Id, "housing.visit_chat_reply")
                .Say(b.enjoyment > 0f ? MeaningFrame.Make(SpeechAct.Statement, Concepts.LAUGH).Agent(Concepts.I) : MeaningFrame.Make(SpeechAct.Exclamation, Concepts.OK))
                .Face(b.enjoyment > 0f ? Expression.Happy : Expression.Neutral)
                .With(Gesture.Nod);
            SpeakAsVisitorOverheard(b, rb, reply);
            try
            {
                dir.AddMemory(ra.Id, new ResidentMemoryEntry { kind = "chatted_at_player_home", otherResidentId = rb.Id, playerId = a.playerId, day = Today, importance = 0.4f });
                dir.AddMemory(rb.Id, new ResidentMemoryEntry { kind = "chatted_at_player_home", otherResidentId = ra.Id, playerId = b.playerId, day = Today, importance = 0.4f });
            }
            catch (Exception e) { Debug.LogException(e); }
            a.enjoyment = Mathf.Clamp(a.enjoyment + 0.08f, -1f, 1f);
            b.enjoyment = Mathf.Clamp(b.enjoyment + 0.08f, -1f, 1f);
        }

        private void SpeakAsVisitorOverheard(Invitation inv, IResidentInfo r, Utterance u)
        {
            if (visitorProxies.TryGetValue(inv.id, out var proxy) && proxy != null)
            {
                foreach (var g in u.gestures) proxy.PlayGesture(g.gesture);
                var speech = Game.Service<ISpeechService>();
                if (speech != null)
                {
                    try { speech.Speak(u, proxy.transform, SpeechChannel.Overheard, "resident"); return; }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            try { Game.Service<IResidentDirectory>()?.Speak(r.Id, u, SpeechChannel.Overheard); } catch (Exception e) { Debug.LogException(e); }
        }

        private void EndVisit(Invitation inv, IResidentInfo r)
        {
            inv.status = InvitationStatus.Done;
            inv.currentInstanceId = "";
            reactTimeout.Remove(inv.id);
            var dir = Game.Service<IResidentDirectory>();
            if (r != null && dir != null)
            {
                var u = new Utterance(r.Id, "housing.visit_end").Face(inv.enjoyment > 0.15f ? Expression.Happy : Expression.Neutral).With(Gesture.Wave);
                if (inv.enjoyment > 0.15f)
                {
                    u.Say(MeaningFrame.Make(SpeechAct.Thanks, Concepts.THANKS));
                    u.Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.FUN).Agent(Concepts.THIS).When(Concepts.TODAY).Past());
                }
                u.Say(MeaningFrame.Make(SpeechAct.Farewell, Concepts.GOODBYE));
                SpeakAsVisitor(inv, r, u);

                int delta = inv.enjoyment > 0.15f ? Mathf.Clamp(Mathf.RoundToInt(2 + inv.enjoyment * 4f), 1, 6) : inv.enjoyment > -0.2f ? 1 : 0;
                if (inv.ateMeal) delta += 1;
                string pid = string.IsNullOrEmpty(inv.playerId) ? HouseOwnerId : inv.playerId;
                try
                {
                    if (delta > 0) dir.ChangeFriendship(r.Id, pid, delta, "housing.visit");
                    dir.AddMemory(r.Id, new ResidentMemoryEntry { kind = "visited_home", playerId = pid, day = Today, importance = Mathf.Clamp01(0.4f + inv.enjoyment * 0.3f) });
                    foreach (var other in state.invitations)
                        if (other != inv && other.status == InvitationStatus.Inside)
                            dir.AddMemory(r.Id, new ResidentMemoryEntry { kind = "visited_home_together", otherResidentId = other.residentId, playerId = pid, day = Today, importance = 0.4f });
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (visitorProxies.TryGetValue(inv.id, out var proxy) && proxy != null) SendProxyHome(proxy);
            visitorProxies.Remove(inv.id);
        }

        private void ExpireInvitations(int day)
        {
            for (int i = state.invitations.Count - 1; i >= 0; i--)
            {
                var inv = state.invitations[i];
                bool finished = inv.status == InvitationStatus.Done || inv.status == InvitationStatus.Missed || inv.status == InvitationStatus.Cancelled || inv.status == InvitationStatus.Declined;
                if (finished && inv.day < day - 3) { state.invitations.RemoveAt(i); continue; }
                if ((inv.status == InvitationStatus.Planned || inv.status == InvitationStatus.Arriving) && inv.day < day - 1)
                    inv.status = InvitationStatus.Missed;
            }
            MarkDirty();
        }

        /// <summary>仲の良い住民が、ふらっと遊びに来る（§11.1 普通の訪問）。</summary>
        private void PlanSpontaneousVisits(int day)
        {
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return;
            var owner = HouseOwnerId;
            int planned = 0;
            foreach (var r in dir.ActiveResidents)
            {
                if (planned >= 1) break;
                if (r.GetFriendship(owner) < HousingConst.SpontaneousVisitMinFriendship) continue;
                if (FindPendingInvitation(r.Id) != null) continue;
                var rng = SeededRandom.For(planetSeed, "housing.spontaneous." + r.Id + "." + day);
                if (!rng.Chance(HousingConst.SpontaneousVisitChance)) continue;
                state.invitations.Add(new Invitation
                {
                    id = Ids.New(),
                    residentId = r.Id,
                    playerId = owner,
                    day = day,
                    hour = rng.Range(14, 17),
                    status = InvitationStatus.Planned,
                    spontaneous = true
                });
                planned++;
            }
            if (planned > 0) MarkDirty();
        }

        // ------------------------------------------------------------
        // 訪問者の姿（各クライアントで状態から作る）
        // ------------------------------------------------------------

        private void TickVisitors(float dt)
        {
            if (!sceneBuilt) return;
            if (visitorProxiesDirty)
            {
                visitorProxiesDirty = false;
                SyncVisitorProxies();
            }
            // 家具に着けなかった時の保険
            if (IsAuthority && reactTimeout.Count > 0)
            {
                foreach (var kv in new List<KeyValuePair<string, float>>(reactTimeout))
                {
                    if (UnityEngine.Time.time < kv.Value) continue;
                    reactTimeout.Remove(kv.Key);
                    var inv = FindInvitation(kv.Key);
                    if (inv == null || inv.status != InvitationStatus.Inside) continue;
                    var r = Game.Service<IResidentDirectory>()?.Get(inv.residentId);
                    if (r != null) React(inv, r, FindFurniture(inv.currentInstanceId));
                }
            }
            if (musicReactCooldown > 0f) musicReactCooldown -= dt;
        }

        private void SyncVisitorProxies()
        {
            if (visitorsRoot == null && housingRoot != null) visitorsRoot = PrimitiveBuilder.Empty("Visitors", housingRoot, Vector3.zero).transform;
            if (visitorsRoot == null) return;
            var dir = Game.Service<IResidentDirectory>();
            var alive = new HashSet<string>();
            foreach (var inv in state.invitations)
            {
                if (inv.status != InvitationStatus.Inside) continue;
                alive.Add(inv.id);
                if (!visitorProxies.TryGetValue(inv.id, out var proxy) || proxy == null)
                {
                    var info = dir?.Get(inv.residentId);
                    proxy = ResidentProxy.Create(visitorsRoot, HouseInteriorSpawn, info, inv.residentId, this);
                    proxy.InvitationId = inv.id;
                    visitorProxies[inv.id] = proxy;
                    WorldLabels.Add(proxy.transform, () => ResidentName(inv.residentId), new Vector3(0, 1.5f, 0), UIStyles.AlienText, 14f, true);
                    if (IsAuthority && !greeted.Contains(inv.id))
                    {
                        greeted.Add(inv.id);
                        var r = info;
                        if (r != null)
                        {
                            var u = new Utterance(r.Id, "housing.visit_greet")
                                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                                .Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.VISIT).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.HOUSE).OwnedBy(Concepts.YOU)))
                                .Face(Expression.Happy)
                                .With(Gesture.Wave);
                            SpeakAsVisitor(inv, r, u);
                            if (inv.playerId == Game.LocalPlayerId)
                            {
                                try { Game.Service<ITabletService>()?.RecordResidentObservation(r.Id, string.Format("{0}日目：家に遊びに来た。", Today), string.Format("Day {0}: came to visit my home.", Today)); }
                                catch (Exception e) { Debug.LogException(e); }
                            }
                        }
                    }
                }
                UpdateProxyTarget(proxy, inv);
            }
            var remove = new List<string>();
            foreach (var kv in visitorProxies) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                var p = visitorProxies[id];
                if (p != null) SendProxyHome(p);
                visitorProxies.Remove(id);
            }
        }

        private void UpdateProxyTarget(ResidentProxy proxy, Invitation inv)
        {
            string target = inv.currentInstanceId ?? "";
            if (proxy.TargetInstanceId == target && (proxy.Arrived || proxy.Action == "walk")) return;
            if (string.IsNullOrEmpty(target))
            {
                proxy.TargetInstanceId = "";
                return;
            }
            var f = FindFurniture(target);
            var fo = GetFurnitureObject(target);
            if (f == null || fo == null) return;
            var spec = HouseGeometry.Resolve(f);
            Vector3 dest;
            float yaw = fo.transform.eulerAngles.y;
            string action;
            float lift = 0f;
            float floorY = HousingConst.HouseOrigin.y;
            if (spec.use == FurnitureUse.Sit && fo.Visual?.SeatPoint != null)
            {
                // 同じ家具に複数人：座る位置を横にずらす
                int index = 0;
                foreach (var other in state.invitations)
                {
                    if (other == inv) break;
                    if (other.status == InvitationStatus.Inside && other.currentInstanceId == target) index++;
                }
                var right = fo.transform.right;
                float seatW = spec.w * HousingConst.Cell / Mathf.Max(1, spec.seats);
                var seat = fo.Visual.SeatPoint.position + right * (index - (Mathf.Max(1, spec.seats) - 1) * 0.5f) * seatW;
                dest = new Vector3(seat.x, floorY, seat.z);
                lift = Mathf.Max(0f, seat.y - floorY - 0.05f);
                action = "sit";
                if (fo.Visual.Fx != null)
                {
                    fo.Visual.Fx.occupiedUntil = UnityEngine.Time.time + HousingConst.VisitStepMinutes * 1.5f;
                    var info = Game.Service<IResidentDirectory>()?.Get(inv.residentId);
                    if (info != null) fo.Visual.Fx.occupantLineage = (int)info.Lineage;
                }
            }
            else
            {
                dest = HouseGeometry.UsePoint(fo.transform.position, floorY, yaw, spec);
                var look = (fo.transform.position - dest).WithY(0f);
                if (look.sqrMagnitude > 0.0001f) yaw = Quaternion.LookRotation(look.normalized).eulerAngles.y;
                action = spec.use == FurnitureUse.Music ? "play" : spec.use == FurnitureUse.Table ? "eat" : spec.use == FurnitureUse.Book ? "look" : "look";
            }
            var path = IndoorPath(proxy.transform.position, dest, out bool teleport);
            if (teleport) proxy.Teleport(dest);
            proxy.OnArrive = OnProxyArrived;
            proxy.GoTo(teleport ? null : path, yaw, action, target, lift);
        }

        /// <summary>家の中の経路（部屋の出入口を通る。別の階は階段で移動したことにする）。</summary>
        public List<Vector3> IndoorPath(Vector3 from, Vector3 to, out bool teleport)
        {
            teleport = false;
            var list = new List<Vector3>();
            if (!HouseGeometry.WorldToRoomCell(state, from, out var ra, out _, out _) || !HouseGeometry.WorldToRoomCell(state, to, out var rb, out _, out _))
            {
                list.Add(to);
                return list;
            }
            if (ra.index == rb.index)
            {
                list.Add(to);
                return list;
            }
            if (HouseGeometry.RowOf(ra.kind) != HouseGeometry.RowOf(rb.kind))
            {
                teleport = true;
                list.Add(to);
                return list;
            }
            // 同じ列：出入口を順に通る
            var row = new List<RoomData>();
            foreach (var r in state.rooms) if (HouseGeometry.RowOf(r.kind) == HouseGeometry.RowOf(ra.kind)) row.Add(r);
            int ia = row.IndexOf(ra), ib = row.IndexOf(rb);
            int dirStep = ib > ia ? 1 : -1;
            float doorZ = (HouseGeometry.DoorZ0 + HouseGeometry.DoorZ1) * 0.5f;
            for (int i = ia; i != ib; i += dirStep)
            {
                var cur = row[i];
                var o = HouseGeometry.RoomOrigin(state, cur);
                float x = dirStep > 0 ? o.x + cur.widthCells * HousingConst.Cell : o.x;
                list.Add(new Vector3(x - dirStep * 0.4f, from.y, o.z + doorZ));
                list.Add(new Vector3(x + dirStep * (HousingConst.WallThickness + 0.4f), from.y, o.z + doorZ));
            }
            list.Add(to);
            return list;
        }

        private void SendProxyHome(ResidentProxy proxy)
        {
            if (proxy == null) return;
            proxy.Leaving = true;
            var exit = HouseInteriorSpawn + new Vector3(0, 0, -0.9f);
            var path = IndoorPath(proxy.transform.position, exit, out bool teleport);
            proxy.OnArrive = p => { if (p != null) UnityEngine.Object.Destroy(p.gameObject); };
            if (teleport) proxy.Teleport(exit);
            proxy.GoTo(teleport ? null : path, 180f, "idle", null);
        }

        private void ClearVisitorProxies()
        {
            foreach (var p in visitorProxies.Values) if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            visitorProxies.Clear();
            greeted.Clear();
            reactTimeout.Clear();
            visitorsRoot = null;
        }

        /// <summary>訪問者の姿に [E]：手を振ってあいさつ。相手もあいさつを返す。</summary>
        public void GreetProxy(ResidentProxy proxy, PlayerState player)
        {
            if (proxy == null || player == null) return;
            Game.PlayerSys?.LocalController?.PlayGesture(PlayerGesture.Wave);
            proxy.PlayGesture(Gesture.Wave);
            var u = new Utterance(proxy.ResidentId, "housing.greet_back")
                .Say(MeaningFrame.Make(SpeechAct.Greeting, Concepts.HELLO))
                .Face(Expression.Happy)
                .With(Gesture.Wave);
            var speech = Game.Service<ISpeechService>();
            try
            {
                if (speech != null) speech.Speak(u, proxy.transform, SpeechChannel.Direct, "resident");
                else Game.Service<IResidentDirectory>()?.Speak(proxy.ResidentId, u);
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void OnPlayerGesture(PlayerGestureEvent e)
        {
            var p = LocalPlayer;
            if (p == null || e.PlayerId != p.PlayerId) return;
            ResidentProxy nearest = null;
            float best = 16f;
            foreach (var proxy in AllProxies())
            {
                if (proxy == null || proxy.Leaving) continue;
                float d = (proxy.transform.position - p.Position).sqrMagnitude;
                if (d < best) { best = d; nearest = proxy; }
            }
            if (nearest == null) return;
            switch (e.Gesture)
            {
                case PlayerGesture.Wave:
                case PlayerGesture.Bow:
                    GreetProxy(nearest, p);
                    break;
                case PlayerGesture.Clap:
                case PlayerGesture.Happy:
                    nearest.PlayGesture(Gesture.Jump);
                    break;
                case PlayerGesture.Sad:
                    nearest.PlayGesture(Gesture.Hug);
                    break;
                default:
                    nearest.PlayGesture(Gesture.Think);
                    break;
            }
        }

        private IEnumerable<ResidentProxy> AllProxies()
        {
            foreach (var p in visitorProxies.Values) yield return p;
            if (residentHomeProxy != null) yield return residentHomeProxy;
        }

        /// <summary>主人公が楽器を鳴らした：家にいる訪問者が反応する（ホスト）。</summary>
        private void OnPlayerMusic(PlacedFurniture instrument, PlayerState player)
        {
            if (musicReactCooldown > 0f) return;
            musicReactCooldown = 20f;
            var def = ItemRegistry.GetOrPlaceholder(instrument.itemId);
            var dir = Game.Service<IResidentDirectory>();
            int listeners = 0;
            foreach (var inv in state.invitations)
            {
                if (inv.status != InvitationStatus.Inside) continue;
                var r = dir?.Get(inv.residentId);
                if (r == null) continue;
                float l = SafeEvaluate(r, def);
                var u = new Utterance(r.Id, "housing.player_music")
                    .Say(MeaningFrame.Make(SpeechAct.Exclamation, l > 0f ? Concepts.WOW : Concepts.HMM))
                    .Face(l > 0.3f ? Expression.Excited : Expression.Neutral)
                    .With(l > 0f ? Gesture.Clap : Gesture.Nod);
                SpeakAsVisitor(inv, r, u);
                inv.enjoyment = Mathf.Clamp(inv.enjoyment + l * 0.2f + 0.05f, -1f, 1f);
                listeners++;
            }
            if (listeners > 0)
            {
                AddMemory(instrument, "music", string.Format("{0}日目、お客さんの前で演奏した。", Today), string.Format("Day {0}: played for guests.", Today), null, player?.PlayerId);
                MarkDirty();
            }
        }

        // ------------------------------------------------------------
        // 庭に住民が立ち寄る（§10.1：花好きがベンチで過ごす）
        // ------------------------------------------------------------

        private void MaybeResidentGardenStroll(int day, int hour)
        {
            if (hour < 10 || hour > 17) return;
            PlacedFurniture bench = null;
            bool flowers = false, water = false;
            foreach (var f in state.furniture)
            {
                if (f.location != HousingConst.LocGarden) continue;
                var spec = HouseGeometry.Resolve(f);
                if (spec.use == FurnitureUse.Sit && bench == null) bench = f;
                if (spec.use == FurnitureUse.Plant) flowers = true;
                if (spec.allowWater || spec.style == FurnitureStyle.Pond) water = true;
            }
            if (bench == null || !(flowers || water)) return;
            var rng = SeededRandom.For(planetSeed, "housing.gardenstroll." + day + "." + hour);
            if (!rng.Chance(0.2f)) return;
            var dir = Game.Service<IResidentDirectory>();
            if (dir == null) return;
            var flowerDef = ItemRegistry.Get("furniture.flower_bed");
            IResidentInfo pick = null;
            float best = 0.2f;
            foreach (var r in dir.ActiveResidents)
            {
                if (r.Activity == ResidentActivity.Sleeping || r.Activity == ResidentActivity.Working) continue;
                float s = (r.Job == ResidentJob.Florist ? 0.6f : 0f) + (flowerDef != null ? SafeEvaluate(r, flowerDef) : 0f) + rng.Range(0f, 0.3f);
                if (s > best) { best = s; pick = r; }
            }
            if (pick == null) return;
            var spec2 = HouseGeometry.Resolve(bench);
            var pos = HouseGeometry.FurniturePosition(state, Layout, bench, spec2, out float yaw);
            var front = pos + Quaternion.Euler(0, yaw, 0) * Vector3.forward * 0.8f;
            try { dir.RequestGoTo(pick.Id, front, Mathf.Min(TimeSystem.MinutesPerDay - 1, hour * 60 + 60), "housing.garden"); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    internal static class VectorExt
    {
        public static Vector3 WithY(this Vector3 v, float y) => new Vector3(v.x, y, v.z);
    }
}
