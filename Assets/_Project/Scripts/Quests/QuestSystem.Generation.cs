using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Quests
{
    // 日次の処理と、住民の暮らしから依頼を生む部分（§6 日常のお願い、§6.2 掲示板、§8 暮らしから生まれる依頼）
    public sealed partial class QuestSystem
    {
        private enum Domain { Fish, Ore, Crop, Flower, Dish, Creature, Delivery, Any }

        /// <summary>日付更新の処理（ホストのみ）。</summary>
        internal void DailyProcess(int day)
        {
            if (!IsAuthority) return;
            S.lastDayProcessed = day;
            ExpireQuests(day);
            CleanupOld(day);
            DailyHelpReset(day);
            RefreshBoard(day);
            foreach (var p in Game.Players.All) GenerateFavorsFor(p.PlayerId, day);
            ScheduleDailyEvents(day);
            foreach (var p in Game.Players.All) StartStoriesFor(p.PlayerId, day);
            foreach (var st in S.stories.ToArray())
            {
                if (st.finished) continue;
                StoryDef(st.storyId)?.OnDayStarted(this, st, day);
            }
            Changed();
        }

        // ------------------------------------------------------------
        // 期限・住民が自分で解決（§5.3）
        // ------------------------------------------------------------

        private void ExpireQuests(int day)
        {
            foreach (var q in S.quests)
            {
                if (!q.IsOpen) continue;
                if (q.kind == QuestKind.Story) continue; // 個別ストーリーは物語側で扱う
                var g = q.MainGoal;
                bool residentGone = !IsActiveResident(q.residentId);
                if (residentGone)
                {
                    SetStatus(q, QuestStatus.Expired);
                    q.outcome = 3;
                    continue;
                }
                if (q.status == QuestStatus.Hinted)
                {
                    if (day - q.createdDay >= QuestTuning.HintedExpireDays)
                    {
                        SetStatus(q, QuestStatus.Expired);
                        q.outcome = 3;
                        // 引き受けていないので報告はしない。住民は自分で何とかした（暮らしは止まらない：住民 §3）。
                        Memory(q.residentId, "solved_self", q.playerId, g?.itemId, null, 0.2f, q.reasonConcept);
                    }
                    continue;
                }
                bool passed = q.deadlineDay >= 0 ? day > q.deadlineDay : day - q.acceptedDay >= QuestTuning.ActiveNoDeadlineExpireDays;
                if (g != null && (g.type == GoalType.Escort || g.type == GoalType.Attend)) passed = day > q.acceptedDay;
                if (!passed) continue;
                SetStatus(q, QuestStatus.Expired);
                q.outcome = 3;
                Memory(q.residentId, "solved_self", q.playerId, g?.itemId, null, 0.3f, q.reasonConcept);
                // 次に会った時「もう自分で見つけた（買った）よ」と伝える。罰はない。
                AddPending(q.playerId, q.residentId, "self_solved", g != null ? (g.itemId ?? g.sampleItemId) : null, null);
            }
        }

        private void CleanupOld(int day)
        {
            S.quests.RemoveAll(q => !q.IsOpen && day - q.finishedDay > QuestTuning.PlanetKeepFinishedDays);
            S.pendings.RemoveAll(p => day - p.day > 6);
            S.events.RemoveAll(e => e.day < day - 2);
        }

        internal void AddPending(string playerId, string residentId, string kind, string itemId, string other)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(residentId)) return;
            foreach (var p in S.pendings)
                if (p.playerId == playerId && p.residentId == residentId && p.kind == kind && p.itemId == itemId) return;
            S.pendings.Add(new PendingTalk { id = NewId("pt"), playerId = playerId, residentId = residentId, kind = kind, itemId = itemId, otherResidentId = other, day = Today });
        }

        // ------------------------------------------------------------
        // 日常のお願い・暮らしから生まれる依頼（§6, §8）
        // ------------------------------------------------------------

        private int GeneratedDayOf(string playerId)
        {
            foreach (var p in S.generatedDay) if (p.key == playerId) return p.value;
            return 0;
        }

        private void SetGeneratedDay(string playerId, int day)
        {
            foreach (var p in S.generatedDay)
            {
                if (p.key == playerId) { p.value = day; return; }
            }
            S.generatedDay.Add(new StringIntPair(playerId, day));
        }

        internal bool HasOpenQuestWith(string playerId, string residentId)
        {
            foreach (var q in S.quests)
                if (q.IsOpen && q.playerId == playerId && q.residentId == residentId) return true;
            return false;
        }

        internal void GenerateFavorsFor(string playerId, int day)
        {
            if (!IsAuthority || string.IsNullOrEmpty(playerId)) return;
            if (GeneratedDayOf(playerId) >= day) return;
            SetGeneratedDay(playerId, day);
            var residents = ActiveResidents();
            if (residents.Count == 0) return;

            int open = 0;
            foreach (var q in S.quests)
                if (q.playerId == playerId && q.IsOpen && (q.kind == QuestKind.DailyFavor || q.kind == QuestKind.LifeBorn || q.kind == QuestKind.Board)) open++;
            var rng = Rng("quests.daily." + day + "." + playerId);
            int n = Mathf.Min(rng.Range(QuestTuning.DailyFavorsMin, QuestTuning.DailyFavorsMax + 1), QuestTuning.MaxOpenFavorsPerPlayer - open);
            if (n <= 0) return;

            rng.Shuffle(residents);
            foreach (var r in residents)
            {
                if (n <= 0) break;
                if (r.Activity == ResidentActivity.Sleeping) continue;
                if (HasOpenQuestWith(playerId, r.Id)) continue;
                var q = BuildFavor(r, playerId, day, rng.Fork(r.Id), QuestKind.DailyFavor);
                if (q == null) continue;
                S.quests.Add(q);
                EventBus.Publish(new QuestStateChangedEvent { QuestId = q.id, ResidentId = q.residentId, Status = q.status, PlayerId = playerId });
                n--;
            }
        }

        /// <summary>
        /// 住民1人の今の事情（職業・行動・天気・出来事・好み）から依頼を1つ作る。
        /// 依頼の対象はこの星で実際に入手できる物だけ（§4.2、言語 §5.2）。作れなければ null。
        /// </summary>
        internal QuestInstance BuildFavor(IResidentInfo r, string playerId, int day, SeededRandom rng, QuestKind kind)
        {
            if (r == null) return null;

            // 誕生日の贈り物探し（§10, 時間仕様 §15）：近いうちに誕生日の住民がいれば、友達として相談する
            if (kind != QuestKind.Board && rng.Chance(0.5f))
            {
                var gift = TryBuildBirthdayGiftFavor(r, playerId, day, rng);
                if (gift != null) return gift;
            }

            var weights = DomainWeights(r);
            Domain domain = PickDomain(weights, rng);
            QuestInstance q = null;
            for (int attempt = 0; attempt < 3 && q == null; attempt++)
            {
                q = BuildForDomain(r, playerId, day, rng, kind, domain);
                if (q == null) domain = attempt == 0 ? Domain.Any : Domain.Delivery;
            }
            return q;
        }

        private Dictionary<Domain, float> DomainWeights(IResidentInfo r)
        {
            var w = new Dictionary<Domain, float>();
            void Add(Domain d, float v) { w.TryGetValue(d, out var old); w[d] = old + v; }
            switch (r.Job)
            {
                case ResidentJob.Blacksmith:
                case ResidentJob.Miner: Add(Domain.Ore, 1f); break;
                case ResidentJob.Builder: Add(Domain.Ore, 0.7f); Add(Domain.Any, 0.3f); break;
                case ResidentJob.Mechanic: Add(Domain.Ore, 0.8f); Add(Domain.Any, 0.2f); break;
                case ResidentJob.Cook: Add(Domain.Crop, 0.45f); Add(Domain.Fish, 0.45f); Add(Domain.Any, 0.1f); break;
                case ResidentJob.FishingMaster: Add(Domain.Fish, 1f); break;
                case ResidentJob.FarmingMaster: Add(Domain.Crop, 0.8f); Add(Domain.Flower, 0.2f); break;
                case ResidentJob.SeedSeller: Add(Domain.Crop, 0.6f); Add(Domain.Flower, 0.4f); break;
                case ResidentJob.Florist: Add(Domain.Flower, 0.8f); Add(Domain.Crop, 0.2f); break;
                case ResidentJob.CreatureExpert: Add(Domain.Creature, 0.7f); Add(Domain.Crop, 0.3f); break;
                case ResidentJob.Carpenter: Add(Domain.Ore, 0.6f); Add(Domain.Any, 0.4f); break;
                case ResidentJob.Merchant: Add(Domain.Any, 1f); break;
                case ResidentJob.Courier: Add(Domain.Delivery, 0.7f); Add(Domain.Any, 0.3f); break;
                default: Add(Domain.Any, 0.7f); Add(Domain.Delivery, QuestTuning.DeliveryFavorChance); Add(Domain.Dish, 0.15f); break;
            }
            if (r.Job != ResidentJob.CreatureExpert) Add(Domain.Creature, QuestTuning.CreatureFavorChance);
            // 今していること（住民 §3 短期目標）
            switch (r.Activity)
            {
                case ResidentActivity.Fishing: Add(Domain.Fish, 1f); break;
                case ResidentActivity.Farming:
                case ResidentActivity.Gardening: Add(Domain.Crop, 0.8f); Add(Domain.Flower, 0.3f); break;
                case ResidentActivity.Mining: Add(Domain.Ore, 1f); break;
                case ResidentActivity.Cooking: Add(Domain.Crop, 0.5f); Add(Domain.Fish, 0.5f); break;
                case ResidentActivity.CatchingCreatures: Add(Domain.Creature, 1f); break;
                case ResidentActivity.Eating: Add(Domain.Dish, 0.5f); break;
            }
            // 天気（雨の日は魚がよく釣れる：釣り仕様）
            var weather = Game.Service<IWeatherService>();
            if (weather != null && weather.Current.IsRaining) Add(Domain.Fish, 0.4f);
            return w;
        }

        private static Domain PickDomain(Dictionary<Domain, float> w, SeededRandom rng)
        {
            var keys = new List<Domain>(w.Keys);
            keys.Sort();
            var weights = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++) weights[i] = w[keys[i]];
            int idx = rng.WeightedIndex(weights);
            return idx < 0 ? Domain.Any : keys[idx];
        }

        private QuestInstance NewQuest(IResidentInfo r, string playerId, int day, QuestKind kind)
        {
            return new QuestInstance
            {
                id = NewId("q"),
                kind = kind,
                playerId = playerId,
                residentId = r.Id,
                status = QuestStatus.Hinted,
                createdDay = day,
                face = Expression.Worried
            };
        }

        private QuestInstance BuildForDomain(IResidentInfo r, string playerId, int day, SeededRandom rng, QuestKind kind, Domain domain)
        {
            var q = NewQuest(r, playerId, day, kind);
            var g = new QuestGoal { type = GoalType.DeliverItem, count = 1 };
            q.goals.Add(g);
            List<string> pool = null;
            switch (domain)
            {
                case Domain.Fish: pool = QuestCatalog.Fish(); break;
                case Domain.Ore: pool = QuestCatalog.Ores(playerId); break;
                case Domain.Crop: pool = QuestCatalog.Crops(); break;
                case Domain.Flower: pool = QuestCatalog.Flowers(); if (pool.Count == 0) pool = QuestCatalog.Crops(); break;
                case Domain.Dish: pool = QuestCatalog.Dishes(); break;
                case Domain.Any: pool = QuestCatalog.AllObtainable(playerId); break;
                case Domain.Creature:
                    return BuildCreatureFavor(q, r, playerId, day, rng);
                case Domain.Delivery:
                    return BuildDeliveryFavor(q, r, day, rng);
            }
            if (pool == null || pool.Count == 0) return null;
            string itemId = QuestCatalog.PickByPreference(r, pool, rng, day, domain == Domain.Any ? 0.15f : -0.3f);
            var def = ItemRegistry.Get(itemId);
            if (def == null) return null;

            // 数量（§6.1）
            switch (domain)
            {
                case Domain.Ore: g.count = rng.Range(2, 6); break;
                case Domain.Fish: g.count = rng.Range(1, 4); break;
                case Domain.Crop: g.count = rng.Range(1, 5); break;
                case Domain.Flower: g.count = rng.Range(1, 4); break;
                case Domain.Dish: g.count = 1; break;
                default: g.count = rng.Range(1, 3); break;
            }
            if (def.MaxStack <= 1) g.count = 1;

            // 「青い魚」のように種類＋色で頼む（言語 §9.2, §9.6）
            string colorTag = QuestCatalog.ColorTag(def);
            if (colorTag != null && (def.Category == ItemCategory.Fish || def.Category == ItemCategory.Crop) && rng.Chance(QuestTuning.CategoryColorGoalChance))
            {
                g.useCategory = true;
                g.category = def.Category;
                g.tag = colorTag;
                g.sampleItemId = itemId;
            }
            else
            {
                g.itemId = itemId;
            }

            // 大きさ・品質（釣り仕様 §12.2：一定以上のサイズ）
            if (def.Category == ItemCategory.Fish && r.Job == ResidentJob.FishingMaster && rng.Chance(0.4f))
            {
                float typical = TypicalFishSize(itemId);
                if (typical > 0f && !g.useCategory) { g.minSize = Mathf.Round(typical * 1.15f); g.count = 1; }
            }
            if (day >= 10 && rng.Chance(0.15f)) g.minQuality = 1;

            // 理由（その住民の本当の事情：言語 §5.2）
            SetReason(q, r, domain, def);
            FinishQuest(q, r, day, rng, kind);
            return q;
        }

        private void SetReason(QuestInstance q, IResidentInfo r, Domain domain, ItemDefinition def)
        {
            switch (domain)
            {
                case Domain.Ore:
                    if (r.Job == ResidentJob.Blacksmith) { q.reasonConcept = Concepts.MAKE; q.reasonPatient = Concepts.TOOL; q.lifeChangeKind = "forge_work"; }
                    else if (r.Job == ResidentJob.Carpenter) { q.reasonConcept = Concepts.MAKE; q.reasonPatient = Concepts.FURNITURE; q.lifeChangeKind = "furniture"; }
                    else if (r.Job == ResidentJob.Mechanic) { q.reasonConcept = Concepts.REPAIR; q.lifeChangeKind = "gadget"; }
                    else { q.reasonConcept = Concepts.MAKE; }
                    break;
                case Domain.Fish:
                    if (r.Job == ResidentJob.Cook || r.Activity == ResidentActivity.Cooking) { q.reasonConcept = Concepts.COOK; q.lifeChangeKind = "dish"; }
                    else if (r.Job == ResidentJob.FishingMaster) { q.reasonConcept = Concepts.SEE; q.reasonPatient = Concepts.FISH; q.lifeChangeKind = "fish_trophy"; }
                    else q.reasonConcept = Concepts.EAT;
                    break;
                case Domain.Crop:
                    if (r.Job == ResidentJob.Cook || r.Activity == ResidentActivity.Cooking) { q.reasonConcept = Concepts.COOK; q.lifeChangeKind = "dish"; }
                    else if (r.Job == ResidentJob.FarmingMaster || r.Job == ResidentJob.SeedSeller) { q.reasonConcept = Concepts.GROW; q.reasonPatient = Concepts.SEED; q.lifeChangeKind = "crop_row"; }
                    else q.reasonConcept = def.Consumable ? Concepts.EAT : Concepts.LIKE;
                    break;
                case Domain.Flower:
                    q.reasonConcept = Concepts.DECORATE; q.reasonPatient = Concepts.HOUSE; q.lifeChangeKind = "flowerpot";
                    break;
                case Domain.Dish:
                    q.reasonConcept = Concepts.EAT; q.face = Expression.Sad;
                    break;
                default:
                    if (r.Job == ResidentJob.Merchant) q.reasonConcept = Concepts.SELL;
                    else q.reasonConcept = def.Consumable ? Concepts.EAT : Concepts.LIKE;
                    if (def.Category == ItemCategory.Furniture) q.lifeChangeKind = "furniture";
                    break;
            }
        }

        /// <summary>期限・手掛かり・報酬を決める（共通）。</summary>
        private void FinishQuest(QuestInstance q, IResidentInfo r, int day, SeededRandom rng, QuestKind kind)
        {
            var g = q.MainGoal;
            if (rng.Chance(QuestTuning.DeadlineChance))
            {
                q.deadlineDay = day + rng.Range(0, 2);
                q.face = Expression.Worried;
            }
            else if (q.face == Expression.Worried)
            {
                q.face = rng.Chance(0.5f) ? Expression.Thinking : Expression.Neutral;
            }
            // 手掛かり（§4.2）：住民が実際に見せるかどうかは住民ごと・場面ごと
            float sampleChance = QuestTuning.ShowSampleBaseChance + QuestTuning.SampleBias(r.Lineage);
            if (day <= QuestTuning.EarlyDaysForMoreClues) sampleChance += QuestTuning.EarlyClueBonus;
            q.showSample = g != null && g.type == GoalType.DeliverItem && rng.Chance(sampleChance);
            q.showNumber = g != null && g.count > 1 && rng.Chance(QuestTuning.ShowNumberChance);

            // 報酬（§12）
            int value = 0;
            foreach (var goal in q.goals)
            {
                string id = goal.itemId ?? goal.sampleItemId;
                var d = ItemRegistry.Get(id);
                value += (d != null ? d.BasePrice : 30) * Mathf.Max(1, goal.count);
            }
            float mult = QuestTuning.RewardPriceMultiplier * (kind == QuestKind.Board ? QuestTuning.BoardRewardMultiplier : 1f);
            q.rewardMoney = Mathf.Clamp(Mathf.RoundToInt(value * mult) + QuestTuning.RewardBase, QuestTuning.RewardMin, QuestTuning.RewardMax);
            if (rng.Chance(QuestTuning.RewardItemChance))
            {
                var item = QuestCatalog.RewardItemFor(r.Job, rng);
                if (item != null)
                {
                    q.rewardItemId = item;
                    q.rewardItemCount = Mathf.Max(1, Mathf.Min(5, 300 / Mathf.Max(10, ItemRegistry.GetOrPlaceholder(item).BasePrice)));
                    q.rewardMoney = Mathf.Max(QuestTuning.RewardMin, q.rewardMoney / 2);
                }
            }
        }

        private QuestInstance BuildCreatureFavor(QuestInstance q, IResidentInfo r, string playerId, int day, SeededRandom rng)
        {
            var species = QuestCatalog.Species();
            if (species.Count == 0) return null;
            var cs = Game.Service<ICreatureService>();
            // 見たことのある種を優先（総当たりにしない：§1.2）
            var known = new List<string>();
            if (cs != null) foreach (var s in species) if (cs.IsSpeciesDiscovered(playerId, s)) known.Add(s);
            string sp = known.Count > 0 && rng.Chance(0.7f) ? rng.Pick(known) : rng.Pick(species);
            var g = q.MainGoal;
            g.type = GoalType.CatchCreature;
            g.speciesId = sp;
            g.count = 1;
            q.reasonConcept = Concepts.SEE;
            q.reasonPatient = Concepts.CREATURE;
            q.lifeChangeKind = r.Job == ResidentJob.CreatureExpert ? "pet" : null;
            FinishQuest(q, r, day, rng, q.kind);
            q.rewardMoney = Mathf.Clamp(q.rewardMoney + 150, QuestTuning.RewardMin, QuestTuning.RewardMax);
            // 生き物名人は、お礼に自分の育てた生き物を譲ってくれることがある（§12、生き物仕様 §11.1）
            if (r.Job == ResidentJob.CreatureExpert && species.Count > 1 && rng.Chance(QuestTuning.CreatureRewardChance))
            {
                var others = new List<string>(species);
                others.Remove(sp);
                q.rewardSpeciesId = rng.Pick(others);
                q.rewardMoney = Mathf.Max(QuestTuning.RewardMin, q.rewardMoney / 2);
            }
            return q;
        }

        private QuestInstance BuildDeliveryFavor(QuestInstance q, IResidentInfo r, int day, SeededRandom rng)
        {
            var others = ActiveResidents();
            others.RemoveAll(x => x.Id == r.Id);
            if (others.Count == 0) return null;
            var target = rng.Pick(others);
            var g = q.MainGoal;
            g.type = GoalType.DeliverToResident;
            g.itemId = QuestItemIds.Parcel;
            g.targetResidentId = target.Id;
            g.count = 1;
            q.pointTarget = target.Id;
            q.showSample = true;
            q.face = Expression.Happy;
            q.reasonConcept = null;
            if (rng.Chance(0.4f)) q.deadlineDay = day;
            q.rewardMoney = rng.Range(40, 110);
            return q;
        }

        private QuestInstance TryBuildBirthdayGiftFavor(IResidentInfo r, string playerId, int day, SeededRandom rng)
        {
            foreach (var other in ActiveResidents())
            {
                if (other.Id == r.Id) continue;
                int d = DaysUntilBirthday(other.Id, day);
                if (d < 1 || d > 2) continue;
                // 同じ相手への相談は1件だけ
                bool exists = false;
                foreach (var x in S.quests)
                    if (x.playerId == playerId && x.MainGoal != null && x.MainGoal.type == GoalType.GiftFor && x.MainGoal.targetResidentId == other.Id && day - x.createdDay < 5) exists = true;
                if (exists) continue;
                var q = NewQuest(r, playerId, day, QuestKind.LifeBorn);
                q.goals.Add(new QuestGoal { type = GoalType.GiftFor, targetResidentId = other.Id, count = 1 });
                q.reasonConcept = Concepts.BIRTHDAY;
                q.reasonPatient = Concepts.Name(other.Id);
                q.deadlineDay = day + d - 1;
                q.pointTarget = other.Id;
                q.face = Expression.Thinking;
                q.rewardMoney = rng.Range(60, 140);
                return q;
            }
            return null;
        }

        // ------------------------------------------------------------
        // 日替わり掲示板（§6.2）
        // ------------------------------------------------------------

        internal Vector3 BoardPosition()
        {
            var l = Layout;
            if (l != null)
            {
                var f = l.GetFacility(FacilityType.BulletinBoard);
                if (f != null) return Ground(f.position);
                var m = l.GetFacility(FacilityType.MessagePlaza);
                if (m != null) return Ground(m.position + new Vector3(3f, 0f, 0f));
            }
            return PlazaPos() + new Vector3(4f, 0f, 3f);
        }

        private void RefreshBoard(int day)
        {
            S.board.RemoveAll(p => p.expireDay < day || !string.IsNullOrEmpty(p.takenBy));
            var rng = Rng("quests.board." + day);
            var residents = ActiveResidents();
            if (residents.Count == 0) return;
            rng.Shuffle(residents);
            int add = Mathf.Min(QuestTuning.BoardNewPapersPerDay, QuestTuning.BoardMaxPapers - CountBoardRequests());
            foreach (var r in residents)
            {
                if (add <= 0) break;
                bool already = false;
                foreach (var p in S.board) if (!p.isNotice && p.residentId == r.Id) already = true;
                if (already) continue;
                // 対象の選び方（到達階など）はホストを基準にする【仮】。取った人の依頼になる
                var t = BuildFavor(r, LocalId ?? "", day, rng.Fork("p" + r.Id), QuestKind.Board);
                if (t == null) continue;
                t.playerId = null;
                var g = t.MainGoal;
                if (g == null || g.type == GoalType.GiftFor || g.type == GoalType.Escort) continue;
                t.showSample = false; // 紙には身振りは無い。描いた絵があれば絵として載る
                t.deadlineDay = t.deadlineDay >= 0 ? t.deadlineDay + 1 : -1;
                var paper = new BoardPaper
                {
                    id = NewId("bp"),
                    residentId = r.Id,
                    template = t,
                    postedDay = day,
                    expireDay = day + QuestTuning.BoardPaperLifetimeDays,
                    drewPicture = g.type == GoalType.DeliverItem && rng.Chance(QuestTuning.BoardPictureChance),
                };
                if (paper.drewPicture) paper.pictureItemId = g.itemId ?? g.sampleItemId;
                S.board.Add(paper);
                add--;
            }
        }

        internal static string BoardQuestId(string paperId) => "qb_" + paperId;

        private int CountBoardRequests()
        {
            int n = 0;
            foreach (var p in S.board) if (!p.isNotice && string.IsNullOrEmpty(p.takenBy)) n++;
            return n;
        }

        /// <summary>紙に書かれた内容（意味データ）。宇宙人の文字で表示し、翻訳しない（看板と同じ扱い）。</summary>
        internal Utterance PaperUtterance(BoardPaper p)
        {
            if (p.isNotice)
            {
                var ev = FindEvent(p.noticeEventId);
                if (ev != null) return QuestSpeech.EventAnnounce(ev, p.residentId, p.postedDay);
                return new Utterance(p.residentId, QuestSpeech.TagBoard).Say(MeaningFrame.Make(SpeechAct.Statement, Concepts.PARTY));
            }
            var t = p.template;
            if (t == null) return new Utterance(p.residentId, QuestSpeech.TagBoard);
            var u = QuestSpeech.Request(t, p.postedDay, 0);
            u.contextTag = QuestSpeech.TagBoard;
            u.gestures.Clear();
            if (t.rewardMoney > 0)
                u.Say(MeaningFrame.Make(SpeechAct.Offer, Concepts.GIVE).Agent(Concepts.I).Patient(MeaningArg.Of(Concepts.MONEY).Qty(t.rewardMoney)).Recipient(Concepts.YOU));
            return u;
        }

        private CommandResult HandleTakePaper(QuestTakePaperCommand c, CommandContext ctx)
        {
            BoardPaper paper = null;
            foreach (var p in S.board) if (p.id == c.paperId) paper = p;
            if (paper == null || paper.isNotice) return CommandResult.Fail("その紙はもうありません", "That notice is gone");
            if (!string.IsNullOrEmpty(paper.takenBy)) return CommandResult.Fail("もう誰かが取っていきました", "Someone already took it");
            if (!IsActiveResident(paper.residentId)) return CommandResult.Fail("依頼主はもう村にいないようです", "The requester seems to have left");
            int open = 0;
            foreach (var q0 in S.quests) if (q0.playerId == ctx.IssuerId && q0.IsOpen && q0.kind == QuestKind.Board) open++;
            if (open >= QuestTuning.MaxOpenFavorsPerPlayer) return CommandResult.Fail("今は手がいっぱいです", "Your hands are full right now");

            paper.takenBy = ctx.IssuerId;
            var t = paper.template;
            var q = new QuestInstance();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(t), q);
            q.id = BoardQuestId(paper.id); // 取った人のローカルでも同じIDで依頼帳を作れるように
            q.kind = QuestKind.Board;
            q.playerId = ctx.IssuerId;
            q.sourceId = paper.id;
            q.createdDay = Today;
            q.acceptedDay = Today;
            q.showSample = paper.drewPicture;
            q.status = QuestStatus.Hinted;
            S.quests.Add(q);
            SetStatus(q, QuestStatus.Active);
            if (q.MainGoal != null && q.MainGoal.type == GoalType.DeliverToResident)
                ctx.GiveItem(new ItemStack(q.MainGoal.itemId, 1) { data = QuestItemIds.DataParcelPrefix + q.id }, "quest_parcel");
            S.board.Remove(paper);
            Changed();
            return CommandResult.Ok();
        }

        // ------------------------------------------------------------
        // 個別ストーリーの開始（§9）
        // ------------------------------------------------------------

        internal void StartStoriesFor(string playerId, int day)
        {
            if (!IsAuthority || string.IsNullOrEmpty(playerId)) return;
            if (day < QuestTuning.StoryEarliestDay) return;
            int running = 0;
            foreach (var s in S.stories) if (s.playerId == playerId && !s.finished) running++;
            foreach (var def in Stories)
            {
                if (running >= QuestTuning.MaxConcurrentStories) break;
                if (FindStory(def.Id, playerId) != null) continue;
                if (day < def.EarliestDay) continue;
                var st = def.TryCreate(this, playerId, day);
                if (st == null) continue;
                st.storyId = def.Id;
                st.playerId = playerId;
                st.stageDay = day;
                st.stageMinute = TotalMinutes;
                S.stories.Add(st);
                running++;
            }
        }

        /// <summary>個別ストーリーの段階で使う依頼を作る（生成は物語側から呼ぶ）。</summary>
        internal QuestInstance CreateStoryQuest(StoryState st, string residentId, QuestGoal goal, string reason, string reasonPatient, bool showSample, string pointTarget, Expression face, int reward)
        {
            var q = new QuestInstance
            {
                id = NewId("q"),
                kind = QuestKind.Story,
                playerId = st.playerId,
                residentId = residentId,
                status = QuestStatus.Hinted,
                createdDay = Today,
                reasonConcept = reason,
                reasonPatient = reasonPatient,
                showSample = showSample,
                showNumber = goal.count > 1,
                pointTarget = pointTarget,
                face = face,
                storyId = st.storyId,
                storyStage = st.stage,
                rewardMoney = reward
            };
            q.goals.Add(goal);
            S.quests.Add(q);
            st.questId = q.id;
            EventBus.Publish(new QuestStateChangedEvent { QuestId = q.id, ResidentId = residentId, Status = q.status, PlayerId = st.playerId });
            return q;
        }

        // ------------------------------------------------------------
        // 宇宙船修理の手掛かり（§15、ロケット仕様 §8.3, §14.3）
        // ------------------------------------------------------------

        /// <summary>修理屋が、まだ直っていない装置の素材を見本として見せる（1日1回まで）。</summary>
        internal Utterance TryRepairHint(IResidentInfo r, string playerId)
        {
            if (r == null || r.Job != ResidentJob.Mechanic) return null;
            var rocket = Game.Service<IRocketService>();
            if (rocket == null || rocket.IsLaunchReady || rocket.HasLaunched) return null;
            foreach (var p in S.repairHintDay) if (p.key == playerId && p.value >= Today) return null;
            var rng = Rng("quests.repairhint." + Today + "." + playerId);
            if (!rng.Chance(QuestTuning.RepairHintChance)) return null;
            var candidates = new List<string>();
            foreach (var row in QuestTuning.RepairHintTable)
            {
                if (rocket.IsDeviceRepaired(row[0])) continue;
                if (ItemRegistry.Exists(row[1])) candidates.Add(row[1]);
            }
            if (candidates.Count == 0) return null;
            bool found = false;
            foreach (var p in S.repairHintDay) if (p.key == playerId) { p.value = Today; found = true; }
            if (!found) S.repairHintDay.Add(new StringIntPair(playerId, Today));
            return QuestSpeech.RepairHint(r.Id, rng.Pick(candidates));
        }
    }
}
