using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.World;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 引っ越し（住民生活仕様 §11、キャラデザ §4、メインストーリー第2章・第4章、バランス §12.2）
    //  ・転出：理由（夢・家族・別の場所…）→ 決める → 伝言でお知らせ・荷造り（家の外に箱）→ 前日に友達が集まり贈り物
    //          → 当日の朝、友達に見送られて村を出る → 元住民（FormerResidents）。手紙・伝言・再訪がある。
    //  ・必須サービス職は消えない：転出を決めた時点で弟子（後継者）を決め、出ていく日に仕事を引き継ぐ。
    //  ・転入：空き区画ができると新住民が来る（荷物の箱の演出）。最初の新住民は数日目（第2章）。
    // =====================================================================
    public sealed partial class ResidentSystem
    {
        private const int DepartureMinute = 9 * 60 + 15;
        private const int DepartureFinalizeHour = 11;

        private void ProcessMoving(int day)
        {
            // 取りこぼした転出（長い時間送りなど）
            foreach (var r in new List<Resident>(ordered))
                if (r.State.active && r.State.moveOutDay >= 0 && r.State.moveOutDay < day) MoveOut(r, day);

            // 元住民：手紙・再訪
            foreach (var r in ordered)
            {
                if (r.State.active) continue;
                if (r.State.nextLetterDay >= 0 && r.State.nextLetterDay <= day) SendLetters(r, day);
                if (r.State.visitDay >= 0 && r.State.visitDay < day) r.State.visitDay = -1;
                if (r.State.visitDay < 0 && r.State.movedOutDay >= 0 && day - r.State.movedOutDay >= 10
                    && StableHash.Unit(seed, "visit." + r.Id + "." + day) < ResidentTuning.FormerVisitChance)
                {
                    r.State.visitDay = day; // 村に遊びに戻ってくる（§11 引っ越し後）
                    r.StatePosition = VillageEdge();
                }
            }

            // 新住民
            if (meta.nextMoveInDay >= 0 && meta.nextMoveInDay <= day) MoveIn(day);

            // 転出の決定
            ConsiderMoveOuts(day);
        }

        // ---------------- 転出 ----------------

        private bool AnyoneMovingOut()
        {
            foreach (var r in ordered) if (r.IsMovingOut) return true;
            return false;
        }

        private bool IsEligibleToMove(Resident r, int day)
        {
            var st = r.State;
            if (!st.active || r.IsGuide || r.IsMovingOut) return false; // 案内役は引っ越さない（最終章で見送る）
            if (day - st.movedInDay < ResidentTuning.MinStayDays) return false;
            if (st.apprenticeFor != ResidentJob.None) return false;
            foreach (var b in st.bonds) if (b.persuadedDay >= 0 && day - b.persuadedDay < 10) return false;
            // 家族（子ども）は一人では出ていかない
            if (r.Profile.age == ResidentAge.Child && !string.IsNullOrEmpty(r.Profile.familyId))
            {
                var fam = GetResident(r.Profile.familyId);
                if (fam != null && fam.State.active && r.Profile.dream != GoalKind.TravelStars) return false;
            }
            return true;
        }

        private void ConsiderMoveOuts(int day)
        {
            if (day < ResidentTuning.EarliestMoveOutDecisionDay) return;
            if (AnyoneMovingOut()) return;
            if (day - meta.lastMoveOutDay < ResidentTuning.MoveOutInterval) return;
            int active = 0;
            foreach (var r in ordered) if (r.State.active) active++;
            if (active <= 6) return;

            Resident chosen = null;
            MoveReason reason = MoveReason.None;
            float bestScore = 0f;
            foreach (var r in ordered)
            {
                if (!IsEligibleToMove(r, day)) continue;
                var lg = r.State.longGoal;
                float ratio = lg.target > 1000f ? 0f : lg.Ratio;
                float chance = ResidentTuning.MoveOutBaseChance * (1f + ratio * 3f);
                if (lg.kind == GoalKind.QuietLife) chance *= 0.2f;
                if (ratio >= 1f) chance *= 4f;
                int maxFr = 0;
                foreach (var b in r.State.bonds) maxFr = Mathf.Max(maxFr, b.friendship);
                chance *= 1f - ResidentTuning.MoveOutFriendshipDamp * maxFr / 100f;
                float roll = StableHash.Unit(seed, "moveout." + r.Id + "." + day);
                if (roll < chance && (chosen == null || chance - roll > bestScore))
                {
                    chosen = r;
                    bestScore = chance - roll;
                    reason = PickReason(r, day);
                }
            }
            // 第4章「誰かの旅立ち」：一度も転出が無いまま日が経ったら、夢に一番近い住民が考え始める
            if (chosen == null && meta.moveOutsTotal == 0 && day >= ResidentTuning.Chapter4NudgeDay)
            {
                float best = -1f;
                foreach (var r in ordered)
                {
                    if (!IsEligibleToMove(r, day) || r.State.longGoal.kind == GoalKind.QuietLife) continue;
                    float score = r.State.longGoal.Ratio - MaxFriendship(r) / 200f;
                    if (score > best) { best = score; chosen = r; }
                }
                if (chosen != null) reason = PickReason(chosen, day);
            }
            if (chosen != null) DecideMoveOut(chosen, reason, day);
        }

        private static int MaxFriendship(Resident r)
        {
            int m = 0;
            foreach (var b in r.State.bonds) m = Mathf.Max(m, b.friendship);
            return m;
        }

        private MoveReason PickReason(Resident r, int day)
        {
            switch (r.State.longGoal.kind)
            {
                case GoalKind.TravelStars:
                case GoalKind.OwnShop:
                case GoalKind.BestCraftsman:
                case GoalKind.BuildSpaceship:
                    if (r.State.longGoal.Ratio >= 0.6f) return MoveReason.Dream;
                    break;
                case GoalKind.FamilyReunion:
                    return MoveReason.Family;
            }
            // 親友が引っ越していった
            foreach (var rel in r.State.relations)
            {
                var o = GetResident(rel.otherId);
                if (o != null && !o.State.active && o.State.movedOutDay >= 0 && day - o.State.movedOutDay < 30 && rel.affinity > 55f) return MoveReason.Friend;
            }
            if (r.P.curiosity > 0.65f) return MoveReason.OtherPlace;
            return MoveReason.Work;
        }

        /// <summary>引っ越しを決める（兆候：伝言でお知らせ、荷造り、友達が集まる）。必須職なら弟子を決める。</summary>
        internal bool DecideMoveOut(Resident r, MoveReason reason, int day)
        {
            if (JobTable.IsEssential(r.State.job))
            {
                var apprentice = PickSuccessor(r.State.job, r);
                if (apprentice == null) return false; // 後継者がいなければ、今は村を離れない（必須サービスを失わない）
                apprentice.State.apprenticeFor = r.State.job;
                apprentice.State.masterId = r.Id;
                apprentice.State.midGoal = new GoalState { kind = GoalKind.LearnSkill, target = ResidentTuning.MoveOutNoticeDays, startedDay = day };
                apprentice.AddMemory(new ResidentMemoryEntry { kind = "apprentice", otherResidentId = r.Id, day = day, importance = 0.8f, detail = r.State.job.ToString() });
                apprentice.Relation(r.Id).trust += 10f;
            }
            r.State.moveOutDecidedDay = day;
            r.State.moveOutDay = day + ResidentTuning.MoveOutNoticeDays;
            r.State.moveReason = reason;
            r.State.moveNoticePosted = false;
            r.AddMemory(new ResidentMemoryEntry { kind = "decided_move", day = day, importance = 0.9f, detail = reason.ToString() });
            foreach (var rel in r.State.relations)
            {
                var o = GetResident(rel.otherId);
                if (o == null || !o.State.active) continue;
                if (o.Affinity(r.Id) > 10f) o.AddMemory(new ResidentMemoryEntry { kind = "heard_moving", otherResidentId = r.Id, day = day, importance = 0.7f });
            }
            PostMoveNotice(r, day);
            EventBus.Publish(new ResidentMoveOutDecidedEvent { ResidentId = r.Id, PlannedDay = r.State.moveOutDay });
            if (r.State.planDay == day) PlanDay(r, day);
            RefreshBoxes();
            MarkDirty();
            return true;
        }

        private Resident PickSuccessor(ResidentJob job, Resident leaving)
        {
            Resident best = null;
            float bestScore = float.MinValue;
            foreach (var o in ordered)
            {
                if (o == leaving || !o.State.active || o.IsMovingOut) continue;
                if (JobTable.IsEssential(o.State.job) || o.State.apprenticeFor != ResidentJob.None) continue;
                if (o.Profile.age == ResidentAge.Child && o.State.job != ResidentJob.Child) continue;
                float score = o.Affinity(leaving.Id) * 0.05f;
                if (CoworkerGroup(o.State.job) != 0 && CoworkerGroup(o.State.job) == CoworkerGroup(job)) score += 5f;
                if (o.Profile.originalJob == job) score += 8f;
                if (o.Profile.age == ResidentAge.Child) score -= 4f;
                if (o.State.job == ResidentJob.Retired || o.State.job == ResidentJob.Builder || o.State.job == ResidentJob.None) score += 2f;
                if (o.IsGuide) score -= 1f;
                if (score > bestScore) { bestScore = score; best = o; }
            }
            return best;
        }

        private void PostMoveNotice(Resident r, int day)
        {
            if (r.State.moveNoticePosted) return;
            r.State.moveNoticePosted = true;
            var u = ResidentLines.MoveNotice(r, r.State.moveOutDay - day, this);
            LeaveMessage("plaza", r, u, SpeechChannel.Broadcast);
        }

        /// <summary>引っ越しの取りやめ（主人公に引き留められた等。住民本人の意思もある：§11）。</summary>
        internal void CancelMoveOut(Resident r, string reason)
        {
            if (!r.IsMovingOut) return;
            r.State.moveOutDay = -1;
            r.State.moveOutDecidedDay = -1;
            r.State.moveReason = MoveReason.None;
            r.State.longGoal.progress *= 0.6f;
            r.AddMemory(new ResidentMemoryEntry { kind = "stayed", day = Today, importance = 0.9f, detail = reason });
            foreach (var o in ordered)
            {
                if (o.State.masterId == r.Id && o.State.apprenticeFor != ResidentJob.None)
                {
                    o.State.apprenticeFor = ResidentJob.None;
                    o.State.masterId = null;
                }
            }
            if (r.State.planDay == Today) PlanDay(r, Today);
            RefreshBoxes();
            MarkDirty();
        }

        /// <summary>出発の日の予定：荷物を運び出し、友達に見送られて村の外へ。</summary>
        private bool PlanDepartureDay(Resident r, ResidentState st, int day)
        {
            if (st.moveOutDay != day) return false;
            int wake = Mathf.Clamp(r.Profile.wakeMinute - 30, 5 * 60, 8 * 60);
            AddBlock(st, 0, wake, ResidentActivity.Sleeping, "home", null, Vector3.zero);
            AddBlock(st, wake, DepartureMinute, ResidentActivity.Packing, "home", null, Vector3.zero, "last_pack");
            AddBlock(st, DepartureMinute, 24 * 60, ResidentActivity.Walking, "edge", null, VillageEdge(), "departing");
            return true;
        }

        /// <summary>引っ越していく友達を見送りに行く。</summary>
        private void ApplySeeOff(Resident r, ResidentState st, int day)
        {
            foreach (var o in ordered)
            {
                if (o == r || !o.State.active || o.State.moveOutDay != day) continue;
                if (r.Affinity(o.Id) < 25f) continue;
                InsertBlock(st, new ScheduleBlock { start = 8 * 60 + 20, end = DepartureMinute + 10, activity = ResidentActivity.Visiting, destKind = "resident", destId = o.Id, note = "see_off" });
                return;
            }
        }

        private void FinalizeDepartures(int day, int hour)
        {
            if (hour != DepartureFinalizeHour) return;
            foreach (var r in new List<Resident>(ordered))
                if (r.State.active && r.State.moveOutDay == day) MoveOut(r, day);
        }

        private void MoveOut(Resident r, int day)
        {
            var st = r.State;
            ResidentJob job = st.job;
            st.active = false;
            st.movedOutDay = day;
            st.moveOutDay = -1;
            st.chatPartnerId = null;
            st.hidden = true;
            st.hasGoTo = false;
            st.followPlayerId = null;
            st.nextLetterDay = day + StableHash.Int(seed, "letter." + r.Id, ResidentTuning.LetterDelayMin, ResidentTuning.LetterDelayMax + 1);
            // 持ち物は一緒に持っていく。家の家具は Housing 側に痕跡として残る（家具仕様 §15.2）
            ReleaseHome(r);

            // 後継者が仕事を引き継ぐ
            foreach (var o in ordered)
            {
                if (o.State.masterId != r.Id || o.State.apprenticeFor == ResidentJob.None) continue;
                o.State.job = o.State.apprenticeFor;
                o.State.apprenticeFor = ResidentJob.None;
                o.AddMemory(new ResidentMemoryEntry { kind = "succeeded", otherResidentId = r.Id, day = day, importance = 0.9f, detail = job.ToString() });
                RebuildJobProp(o);
            }
            // 友達は寂しがる・思い出す（§15）
            foreach (var o in ordered)
            {
                if (!o.State.active) continue;
                float aff = o.Affinity(r.Id);
                if (aff > 20f)
                {
                    o.AddMemory(new ResidentMemoryEntry { kind = "friend_moved", otherResidentId = r.Id, day = day, importance = aff > 50f ? 0.9f : 0.6f });
                    o.State.mood = Mathf.Clamp(o.State.mood - aff / 200f, -1f, 1f);
                }
            }
            meta.lastMoveOutDay = day;
            meta.moveOutsTotal++;
            int target = Mathf.Min(InitialCount(Layout) + ResidentTuning.PopulationGrowth, ResidentTuning.MaxResidents);
            int delay = StableHash.Int(seed, "newcomer.delay." + day, ResidentTuning.NewcomerDelayMin, ResidentTuning.NewcomerDelayMax + 1);
            if (meta.nextMoveInDay < 0 || meta.nextMoveInDay > day + delay) meta.nextMoveInDay = day + delay;
            EnsureEssentialJobs();
            AssignWorkplaceOwners();
            SyncAvatar(r);
            RefreshBoxes();
            OnLocalSawMoveOut(r);
            EventBus.Publish(new ResidentMovedOutEvent { ResidentId = r.Id, Day = day });
            MarkDirty();
        }

        private void OnLocalSawMoveOut(Resident r)
        {
            var local = Game.LocalPlayer;
            if (local == null || !r.Knows(local.PlayerId)) return;
            RecordObservation(r, "moved_out", string.Format("{0}日目に村から引っ越していった", Today),
                string.Format("Moved away from the village on day {0}", Today));
        }

        // ---------------- 手紙（引っ越した住民から） ----------------

        private void SendLetters(Resident r, int day)
        {
            var st = r.State;
            st.lettersSent++;
            st.nextLetterDay = st.lettersSent < 2 ? day + StableHash.Int(seed, "letter2." + r.Id, 12, 21) : -1;
            var speech = Game.Service<ISpeechService>();
            bool any = false;
            foreach (var b in st.bonds)
            {
                if (b.friendship < ResidentTuning.LetterFriendship || !b.met) continue;
                var u = ResidentLines.Letter(r, b.playerId, this);
                UtteranceRecord rec = null;
                try { rec = speech?.CreateRecord(u, SpeechChannel.Letter, "resident", Vector3.zero); }
                catch (System.Exception e) { Debug.LogException(e); }
                var stack = new ItemStack(SharedItemIds.Letter, 1) { data = rec != null ? rec.recordId : null };
                var player = Game.Players.Get(b.playerId);
                if (player != null && player.IsLocal)
                {
                    player.GiveOrDrop(stack, "resident_letter");
                    var tablet = Game.Service<ITabletService>();
                    if (tablet != null)
                    {
                        var mat = new TabletMaterial
                        {
                            id = "letter." + r.Id + "." + day,
                            kind = TabletMaterialKind.Letter,
                            titleJa = "手紙",
                            titleEn = "Letter",
                            factJa = string.Format("{0}日目に届いた手紙。引っ越していった住民から。", day),
                            factEn = string.Format("A letter that arrived on day {0}, from a resident who moved away.", day),
                            day = day,
                            minuteOfDay = MinuteOfDay,
                            linkedRecordId = rec != null ? rec.recordId : null,
                            linkedResidentId = r.Id
                        };
                        mat.tags.Add("letter");
                        try { tablet.AddMaterial(mat); }
                        catch (System.Exception e) { Debug.LogException(e); }
                    }
                    UIManager.Toast(Loc.T("手紙が届きました", "A letter has arrived"));
                }
                else
                {
                    WorldAuthority.Bridge?.GrantItemToRemote(b.playerId, stack, "resident_letter");
                }
                any = true;
            }
            // 村の友達へは伝言広場のマネピ経由で近況が届く（§11・タブレット §9.2）
            bool hasFriends = false;
            foreach (var rel in st.relations) if (rel.affinity > 40f) { hasFriends = true; break; }
            if (any || hasFriends)
            {
                var voice = ResidentLines.Letter(r, null, this);
                voice.contextTag = "resident.news_from_former";
                LeaveMessage("plaza", r, voice, SpeechChannel.Broadcast);
                foreach (var rel in st.relations)
                {
                    var o = GetResident(rel.otherId);
                    if (o != null && o.State.active && rel.affinity > 40f)
                        o.AddMemory(new ResidentMemoryEntry { kind = "letter_from", otherResidentId = r.Id, day = day, importance = 0.6f });
                }
            }
            MarkDirty();
        }

        // ---------------- 転入 ----------------

        private void MoveIn(int day)
        {
            var layout = Layout;
            HousePlot free = null;
            if (layout != null)
            {
                var plots = new List<HousePlot>(layout.HousePlots);
                var prng = SeededRandom.For(seed, "residents.houses.newcomer." + meta.newcomerCounter);
                prng.Shuffle(plots);
                foreach (var p in plots) if (!p.occupied) { free = p; break; }
            }
            int active = 0;
            foreach (var r in ordered) if (r.State.active) active++;
            if ((layout != null && layout.HousePlots.Count > 0 && free == null) || active >= ResidentTuning.MaxResidents)
            {
                meta.nextMoveInDay = day + 3; // 空き家ができるまで待つ
                return;
            }
            ResidentJob required = ResidentJob.None;
            foreach (var j in JobTable.EssentialJobs)
            {
                bool covered = false;
                foreach (var r in ordered) if (r.State.active && (r.State.job == j || r.State.apprenticeFor == j)) { covered = true; break; }
                if (!covered) { required = j; break; }
            }
            meta.newcomerCounter++;
            var profile = ResidentGenerator.GenerateNewcomer(seed, meta.newcomerCounter, required, ordered);
            if (residents.ContainsKey(profile.id)) { meta.nextMoveInDay = day + 2; return; }
            var rng = SeededRandom.For(seed, "residents.newcomer.state." + meta.newcomerCounter);
            var st = new ResidentState
            {
                id = profile.id,
                active = true,
                movedInDay = day,
                job = profile.originalJob,
                money = rng.Range(ResidentTuning.StartMoneyMin, ResidentTuning.StartMoneyMax),
                mood = 0.3f,
                hunger = 0.2f,
                social = 0.7f
            };
            var nr = new Resident(profile, st, false);
            AddResident(nr);
            if (free != null) AssignHome(nr, free);
            nr.StatePosition = VillageEdge();
            // 新しい住民はまだ誰とも親しくない（日が経つにつれて関係ができる）
            foreach (var o in ordered)
            {
                if (o == nr || !o.State.active) continue;
                float c = Compat(nr, o);
                nr.State.relations.Add(new RelationEntry { otherId = o.Id, kind = RelationKind.Acquaintance, affinity = c * 8f, compatibility = c });
                o.State.relations.Add(new RelationEntry { otherId = nr.Id, kind = RelationKind.Acquaintance, affinity = c * 8f, compatibility = c });
                o.AddMemory(new ResidentMemoryEntry { kind = "new_neighbor", otherResidentId = nr.Id, day = day, importance = 0.5f });
            }
            InitGoals(nr, SeededRandom.For(seed, "residents.goals." + nr.Id));
            meta.moveInsTotal++;
            int target = Mathf.Min(InitialCount(layout) + ResidentTuning.PopulationGrowth, ResidentTuning.MaxResidents);
            int freePlots = 0;
            if (layout != null) foreach (var p in layout.HousePlots) if (!p.occupied) freePlots++;
            meta.nextMoveInDay = active + 1 < target && freePlots > 0 ? day + StableHash.Int(seed, "newcomer.next." + day, 5, 10) : -1;
            UpdateGuideFlag();
            EnsureEssentialJobs();
            AssignWorkplaceOwners();
            PlanDay(nr, day);
            SyncAvatar(nr);
            RefreshBoxes();
            EventBus.Publish(new ResidentMovedInEvent { ResidentId = nr.Id, Day = day });
            MarkDirty();
        }

        /// <summary>必須サービス職が必ず誰かに担われている状態を保つ（不変条件の修復）。</summary>
        private void EnsureEssentialJobs()
        {
            foreach (var job in JobTable.EssentialJobs)
            {
                bool has = false;
                foreach (var r in ordered) if (r.State.active && r.State.job == job) { has = true; break; }
                if (has) continue;
                Resident pick = null;
                foreach (var r in ordered) if (r.State.active && r.State.apprenticeFor == job) { pick = r; break; }
                if (pick == null)
                {
                    var dummy = new Resident(new ResidentProfile { id = "__none__" }, new ResidentState(), true);
                    pick = PickSuccessor(job, dummy);
                }
                if (pick == null) continue;
                pick.State.job = job;
                pick.State.apprenticeFor = ResidentJob.None;
                pick.State.masterId = null;
                pick.AddMemory(new ResidentMemoryEntry { kind = "new_job", day = Today, importance = 0.8f, detail = job.ToString() });
                RebuildJobProp(pick);
            }
        }
    }
}
