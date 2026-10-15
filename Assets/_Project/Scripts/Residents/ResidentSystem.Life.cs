using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.World;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 1日の生活とAI（住民生活仕様 §2〜§4・§19、時間・天候仕様 §10）。ホスト（IsAuthority）だけが実行する。
    //   状態を参照 → 予定を決める → 行動を選ぶ → 実行（購入・消費・設置・交流）→ 結果を反映 → 記憶 → 発言
    // =====================================================================
    public sealed partial class ResidentSystem
    {
        internal const string GiftTag = "gift_for:";
        private readonly Dictionary<string, ShoppingIntent> pendingIntents = new Dictionary<string, ShoppingIntent>();

        // ---------------- 予定づくり ----------------

        private void PlanDay(Resident r, int day)
        {
            var st = r.State;
            var p = r.Profile;
            var rng = SeededRandom.For(seed, "res.plan." + day + "." + r.Id);
            st.plan.Clear();
            st.planDay = day;
            st.blockIndex = -1;
            st.promises.RemoveAll(x => x.day < day);

            // 引っ越した住民が遊びに来る日（転出後の再訪：住民生活仕様 §11）
            if (!st.active)
            {
                if (st.visitDay == day)
                {
                    var plaza = FacilityPos(FacilityType.Plaza, r.Id, 4f);
                    AddBlock(st, 0, 10 * 60, ResidentActivity.Sleeping, "edge", null, VillageEdge());
                    AddBlock(st, 10 * 60, 16 * 60, ResidentActivity.AtPlaza, "pos", null, plaza);
                    AddBlock(st, 16 * 60, 24 * 60, ResidentActivity.Walking, "edge", null, VillageEdge());
                }
                return;
            }

            if (PlanDepartureDay(r, st, day)) return;

            int wake = Mathf.Clamp(p.wakeMinute + rng.Range(-20, 21), 4 * 60, 11 * 60);
            int sleep = Mathf.Clamp(p.sleepMinute + rng.Range(-20, 21), 20 * 60, 24 * 60);
            bool rest = IsRestDayOn(day);
            var weather = Game.Service<IWeatherService>();
            bool movingSoon = r.IsMovingOut && st.moveOutDay - day <= ResidentTuning.MoveOutNoticeDays - 1 && st.moveOutDay > day;
            bool movedInToday = st.movedInDay == day && day > 0;

            AddBlock(st, 0, wake, ResidentActivity.Sleeping, "home", null, Vector3.zero);
            int t = wake;
            AddBlock(st, t, t + 35, ResidentActivity.Eating, "home", null, Vector3.zero, "breakfast");
            t += 35;

            // ---- 新しく引っ越してきた日：荷ほどき → 村を歩いて挨拶（第2章） ----
            if (movedInToday)
            {
                AddBlock(st, t, Mathf.Max(t + 30, 14 * 60), ResidentActivity.Packing, "home", null, Vector3.zero, "unpack");
                t = Mathf.Max(t + 30, 14 * 60);
                AddBlock(st, t, t + 90, ResidentActivity.Walking, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero, "greet_village");
                t += 90;
                AddBlock(st, t, t + 60, ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero);
                t += 60;
                FillFree(r, st, t, sleep, day, rng, weather);
                AddBlock(st, sleep, 24 * 60, ResidentActivity.Sleeping, "home", null, Vector3.zero);
                return;
            }

            // ---- 引っ越しの準備（荷造り・友達が集まる：第4章） ----
            if (movingSoon)
            {
                AddBlock(st, t, 12 * 60, ResidentActivity.Packing, "home", null, Vector3.zero, "pack");
                AddBlock(st, 12 * 60, 13 * 60, ResidentActivity.Eating, "home", null, Vector3.zero, "lunch");
                bool lastDay = st.moveOutDay - day == 1;
                if (lastDay)
                {
                    // 前日は挨拶回り：仲の良い住民の家・広場へ
                    string friend = r.BestFriendId(residents);
                    if (friend != null) AddBlock(st, 13 * 60, 15 * 60, ResidentActivity.Visiting, "resident", friend, Vector3.zero, "farewell");
                    else AddBlock(st, 13 * 60, 15 * 60, ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero, "farewell");
                    AddBlock(st, 15 * 60, 17 * 60, ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero, "farewell");
                    AddBlock(st, 17 * 60, sleep, ResidentActivity.AtHome, "home", null, Vector3.zero, "farewell_party");
                }
                else
                {
                    AddBlock(st, 13 * 60, 17 * 60, ResidentActivity.Packing, "home", null, Vector3.zero, "pack");
                    FillFree(r, st, 17 * 60, sleep, day, rng, weather);
                }
                AddBlock(st, sleep, 24 * 60, ResidentActivity.Sleeping, "home", null, Vector3.zero);
                return;
            }

            // ---- 天気読み：朝は高台で空を読み、広場のマネピに予報を預ける（時間・天候仕様 §14） ----
            if (st.job == ResidentJob.WeatherReader && !rest)
            {
                AddBlock(st, t, t + 50, ResidentActivity.WatchingSky, "facility_type", FacilityType.Lookout.ToString(), Vector3.zero, "read_sky");
                t += 50;
                AddBlock(st, t, t + 40, ResidentActivity.Working, "facility_type", FacilityType.MessagePlaza.ToString(), Vector3.zero, "forecast");
                t += 40;
            }

            // ---- 仕事 ----
            var workplace = JobTable.Workplace(st.job);
            bool hasWork = !rest && st.job != ResidentJob.None && st.job != ResidentJob.Retired && st.job != ResidentJob.Traveler;
            int ws = Mathf.Max(t, p.workStartMinute);
            int we = Mathf.Max(ws + 120, p.workEndMinute);
            const int lunchStart = 12 * 60, lunchEnd = 13 * 60;
            if (hasWork)
            {
                var act = JobTable.WorkActivity(st.job);
                string kind = st.job == ResidentJob.FishingMaster ? "work_fish" : workplace.HasValue ? "work" : "work_roam";
                if (ws >= lunchStart)
                {
                    // 午後から働く（音楽家など）：昼までは自由
                    ws = Mathf.Max(ws, lunchEnd);
                    we = Mathf.Max(we, ws + 120);
                    FillFree(r, st, t, lunchStart, day, rng, weather);
                    PlanLunch(r, st, lunchStart, lunchEnd, rng);
                    FillFree(r, st, lunchEnd, ws, day, rng, weather);
                    AddBlock(st, ws, we, act, kind, null, Vector3.zero, "work");
                    t = we;
                }
                else
                {
                    if (ws > t) FillFree(r, st, t, ws, day, rng, weather);
                    AddBlock(st, ws, lunchStart, act, kind, null, Vector3.zero, "work");
                    PlanLunch(r, st, lunchStart, lunchEnd, rng);
                    if (we > lunchEnd) AddBlock(st, lunchEnd, we, act, kind, null, Vector3.zero, "work");
                    t = Mathf.Max(we, lunchEnd);
                }
            }
            else
            {
                FillFree(r, st, t, lunchStart, day, rng, weather);
                PlanLunch(r, st, lunchStart, lunchEnd, rng);
                t = lunchEnd;
            }

            // ---- 夕方〜夜 ----
            int dinner = Mathf.Max(18 * 60 + rng.Range(0, 60), t);
            if (dinner + 45 <= sleep)
            {
                if (t < dinner) FillFree(r, st, t, dinner, day, rng, weather);
                AddBlock(st, dinner, dinner + 45, ResidentActivity.Eating, "home", null, Vector3.zero, "dinner");
                t = dinner + 45;
            }

            // 誕生日：夜は家でお祝い（友達が集まる）
            if (r.IsBirthday(day) && t < sleep)
            {
                AddBlock(st, t, Mathf.Min(sleep, t + 120), ResidentActivity.Celebrating, "home", null, Vector3.zero, "birthday");
                t = Mathf.Min(sleep, t + 120);
            }
            // 星の光帯の夜は高台へ（時間・天候仕様 §12）
            if (weather != null && t < sleep)
            {
                var night = weather.GetPlanned(day, 21);
                if (night.Has(AlienPhenomenon.StarBand) && (r.P.curiosity > 0.45f || p.hobby == ResidentHobby.Stargazing || st.job == ResidentJob.WeatherReader))
                {
                    int end = Mathf.Min(Mathf.Max(sleep, 22 * 60 + 30), 24 * 60);
                    AddBlock(st, Mathf.Max(t, 20 * 60), end, ResidentActivity.WatchingSky, "facility_type", FacilityType.Lookout.ToString(), Vector3.zero, "starband");
                    if (t < 20 * 60) FillFree(r, st, t, 20 * 60, day, rng, weather);
                    t = end;
                    sleep = Mathf.Max(sleep, end);
                }
            }
            if (t < sleep) FillEvening(r, st, t, sleep, day, rng);
            if (sleep < 24 * 60) AddBlock(st, sleep, 24 * 60, ResidentActivity.Sleeping, "home", null, Vector3.zero);
            ApplySeeOff(r, st, day);
            ApplyPromises(r, st, day);
            st.plan.Sort((a, b) => a.start.CompareTo(b.start));
        }

        /// <summary>約束（「明日一緒に〜しよう」）を、2人で同じ時刻・同じ場所の予定として差し込む。会話の意味と行動を一致させる。</summary>
        private void ApplyPromises(Resident r, ResidentState st, int day)
        {
            foreach (var pr in st.promises)
            {
                if (pr.day != day || pr.end <= pr.start) continue;
                InsertBlock(st, new ScheduleBlock { start = pr.start, end = pr.end, activity = pr.activity, destKind = "promise", destId = pr.placeId, note = "promise:" + pr.partnerId });
            }
        }

        /// <summary>予定に区間を差し込み、重なる区間を切り詰める（睡眠・引っ越し準備は上書きしない）。</summary>
        private static void InsertBlock(ResidentState st, ScheduleBlock nb)
        {
            foreach (var b in st.plan)
                if ((b.activity == ResidentActivity.Sleeping || b.activity == ResidentActivity.Packing) && b.start < nb.end && nb.start < b.end) return;
            var result = new List<ScheduleBlock>();
            foreach (var b in st.plan)
            {
                if (b.end <= nb.start || b.start >= nb.end) { result.Add(b); continue; }
                if (b.start < nb.start)
                {
                    var head = Clone(b);
                    head.end = nb.start;
                    result.Add(head);
                }
                if (b.end > nb.end)
                {
                    var tail = Clone(b);
                    tail.start = nb.end;
                    result.Add(tail);
                }
            }
            result.Add(nb);
            result.Sort((a, b) => a.start.CompareTo(b.start));
            st.plan.Clear();
            st.plan.AddRange(result);
        }

        private static ScheduleBlock Clone(ScheduleBlock b)
        {
            return new ScheduleBlock { start = b.start, end = b.end, activity = b.activity, destKind = b.destKind, destId = b.destId, dx = b.dx, dy = b.dy, dz = b.dz, note = b.note };
        }

        private void PlanLunch(Resident r, ResidentState st, int start, int end, SeededRandom rng)
        {
            // 昼：家で食べる／共用調理場で外食／商店へ買いに行く（空腹とお金と食べ物の有無で決める）
            bool hasFood = r.FindItem(Resident.IsEdible) != null;
            if (!hasFood && st.money > 60 && rng.Chance(0.55f))
            {
                AddBlock(st, start, start + 40, ResidentActivity.Shopping, ShopDestKind(rng), null, Vector3.zero, "lunch_shopping");
                AddBlock(st, start + 40, end, ResidentActivity.Eating, "here", null, Vector3.zero, "lunch");
            }
            else if (rng.Chance(0.35f + r.P.sociability * 0.2f))
                AddBlock(st, start, end, ResidentActivity.Eating, "facility_type", FacilityType.CommunityKitchen.ToString(), Vector3.zero, "lunch_out");
            else
                AddBlock(st, start, end, ResidentActivity.Eating, JobTable.Workplace(st.job).HasValue ? "here" : "home", null, Vector3.zero, "lunch");
        }

        private string ShopDestKind(SeededRandom rng)
        {
            var shop = Game.Service<IShopService>();
            // 予定は 0:00（日付更新時）に立てるので、その時点の IsOpen（閉店中）では判断しない。
            // 営業しているか・商品があるかは、買い物の予定を始める時（StartBlock）に確かめる。
            if (shop != null && rng.Chance(ResidentTuning.PlayerShopPreference)) return "shop";
            return "village_store";
        }

        /// <summary>自由時間を趣味・買い物・交流・休息で埋める（住民生活仕様 §2「予定を変える要素」）。</summary>
        private void FillFree(Resident r, ResidentState st, int from, int to, int day, SeededRandom rng, IWeatherService weather)
        {
            int t = from;
            int guard = 0;
            while (to - t >= 25 && guard++ < 12)
            {
                int len = Mathf.Min(to - t, rng.Range(50, 140));
                if (to - (t + len) < 25) len = to - t;
                ChooseFree(r, st, t, t + len, day, rng, weather);
                t += len;
            }
            if (t < to) AddBlock(st, t, to, ResidentActivity.Walking, "roam", null, Vector3.zero);
        }

        private void ChooseFree(Resident r, ResidentState st, int start, int end, int day, SeededRandom rng, IWeatherService weather)
        {
            var p = r.Profile;
            int hour = start / 60;
            bool rainy = weather != null && weather.GetPlanned(day, Mathf.Clamp(hour, 0, 23)).IsRaining;
            bool avoidRain = rainy && r.P.rainLike < -0.2f;

            var options = new List<KeyValuePair<string, float>>();
            void Opt(string k, float w) { if (w > 0) options.Add(new KeyValuePair<string, float>(k, w)); }
            float social = r.P.sociability;
            Opt("hobby", avoidRain && IsOutdoorHobby(p.hobby) ? 0.3f : 3f);
            Opt("hobby2", avoidRain && IsOutdoorHobby(p.hobby2) ? 0.1f : 1.2f);
            bool canShop = st.money > 80 && st.lastShopVisitDay != day;
            float shopW = canShop ? 1.2f + (st.hunger > 0.5f ? 1.5f : 0f) + (NeedsPurchase(r) ? 2f : 0f) - r.P.thrift * 0.8f : 0f;
            Opt("shop", shopW);
            Opt("plaza", avoidRain ? 0.3f : 0.8f + social * 1.8f);
            string friend = PickFriendToVisit(r, day, rng);
            Opt("visit", friend != null ? 0.6f + social * 1.5f : 0f);
            Opt("rest", 0.4f + r.P.easygoing * 1.2f + (avoidRain ? 1.5f : 0f));
            if (rainy && r.P.rainLike > 0.4f) Opt("rain_walk", 2.5f);
            if (r.Profile.dream == GoalKind.TravelStars || st.job == ResidentJob.Mechanic || p.hobby == ResidentHobby.Stargazing) Opt("rocket", 0.8f);
            string playerToVisit = PickPlayerToVisit(r);
            if (playerToVisit != null && !avoidRain) Opt("player_home", 0.35f);
            if (st.midGoal.kind == GoalKind.RaiseCreature) Opt("creatures", 1.2f);

            float total = 0f;
            foreach (var o in options) total += o.Value;
            float pick = rng.NextFloat() * total;
            string choice = "rest";
            foreach (var o in options)
            {
                pick -= o.Value;
                if (pick <= 0) { choice = o.Key; break; }
            }
            switch (choice)
            {
                case "hobby": AddHobbyBlock(r, st, p.hobby, start, end, rng, avoidRain); break;
                case "hobby2": AddHobbyBlock(r, st, p.hobby2, start, end, rng, avoidRain); break;
                case "shop":
                    AddBlock(st, start, Mathf.Min(end, start + 50), ResidentActivity.Shopping, ShopDestKind(rng), null, Vector3.zero, "shopping");
                    if (end - start > 75) AddBlock(st, start + 50, end, ResidentActivity.Walking, "roam", null, Vector3.zero);
                    else if (end > start + 50) AddBlock(st, start + 50, end, ResidentActivity.AtHome, "home", null, Vector3.zero);
                    break;
                case "plaza": AddBlock(st, start, end, ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero); break;
                case "visit": AddBlock(st, start, end, ResidentActivity.Visiting, "resident", friend, Vector3.zero, "visit"); break;
                case "rain_walk": AddBlock(st, start, end, ResidentActivity.Walking, "roam", null, Vector3.zero, "rain_walk"); break;
                case "rocket": AddBlock(st, start, end, ResidentActivity.Exploring, "rocket", null, Vector3.zero, "rocket"); break;
                case "player_home": AddBlock(st, start, Mathf.Min(end, start + 60), ResidentActivity.Visiting, "player_home", playerToVisit, Vector3.zero, "visit_player"); if (end > start + 60) AddBlock(st, start + 60, end, ResidentActivity.Walking, "roam", null, Vector3.zero); break;
                case "creatures": AddBlock(st, start, end, ResidentActivity.CatchingCreatures, "area", AreaType.Meadow.ToString(), Vector3.zero); break;
                default:
                    AddBlock(st, start, end, r.P.tidiness > 0.6f ? ResidentActivity.AtHome : ResidentActivity.Resting, "home", null, Vector3.zero);
                    break;
            }
        }

        private void FillEvening(Resident r, ResidentState st, int from, int to, int day, SeededRandom rng)
        {
            float social = r.P.sociability;
            if (to - from < 30)
            {
                AddBlock(st, from, to, ResidentActivity.AtHome, "home", null, Vector3.zero);
                return;
            }
            // 友達の誕生日・引っ越し前日のお別れ会に集まる
            foreach (var other in ordered)
            {
                if (other == r || !other.State.active) continue;
                float aff = r.Affinity(other.Id);
                bool party = other.IsBirthday(day) && aff > 35f;
                bool farewell = other.IsMovingOut && other.State.moveOutDay - day == 1 && aff > 25f;
                if (party || farewell)
                {
                    int end = Mathf.Min(to, from + 100);
                    AddBlock(st, from, end, ResidentActivity.Visiting, "resident", other.Id, Vector3.zero, party ? "birthday_guest" : "farewell_guest");
                    if (end < to) AddBlock(st, end, to, ResidentActivity.AtHome, "home", null, Vector3.zero);
                    return;
                }
            }
            if (st.job == ResidentJob.Musician || r.P.nightOwl > 0.65f && rng.Chance(0.6f))
            {
                AddBlock(st, from, to, st.job == ResidentJob.Musician ? ResidentActivity.Playing : ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero, "night_plaza");
                return;
            }
            if (r.Profile.hobby == ResidentHobby.Stargazing && rng.Chance(0.6f))
            {
                AddBlock(st, from, to, ResidentActivity.WatchingSky, "facility_type", FacilityType.Lookout.ToString(), Vector3.zero);
                return;
            }
            if (rng.Chance(social * 0.5f))
            {
                int mid = from + (to - from) / 2;
                AddBlock(st, from, mid, ResidentActivity.AtPlaza, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero);
                AddBlock(st, mid, to, ResidentActivity.AtHome, "home", null, Vector3.zero);
                return;
            }
            AddBlock(st, from, to, r.Profile.hobby == ResidentHobby.Cooking ? ResidentActivity.Cooking : ResidentActivity.AtHome, "home", null, Vector3.zero);
        }

        private void AddHobbyBlock(Resident r, ResidentState st, ResidentHobby h, int start, int end, SeededRandom rng, bool avoidRain)
        {
            if (avoidRain && IsOutdoorHobby(h))
            {
                // 雨の日は家で道具の手入れ（住民生活仕様 §2 の釣り好きの例）
                AddBlock(st, start, end, h == ResidentHobby.Fishing ? ResidentActivity.Crafting : ResidentActivity.AtHome, "home", null, Vector3.zero, "rainy_home");
                return;
            }
            switch (h)
            {
                case ResidentHobby.Fishing:
                    AddBlock(st, start, end, ResidentActivity.Fishing, "spot", PickFishingSpot(r, rng), Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Gardening:
                case ResidentHobby.Decorating:
                case ResidentHobby.Crafting:
                case ResidentHobby.Reading:
                    AddBlock(st, start, end, JobTable.HobbyActivity(h), "home", null, Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Cooking:
                    AddBlock(st, start, end, ResidentActivity.Cooking, rng.Chance(0.5f) ? "home" : "facility_type", FacilityType.CommunityKitchen.ToString(), Vector3.zero, "hobby");
                    break;
                case ResidentHobby.CreatureWatching:
                    AddBlock(st, start, end, ResidentActivity.CatchingCreatures, "area", (rng.Chance(0.5f) ? AreaType.Forest : AreaType.Meadow).ToString(), Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Collecting:
                    AddBlock(st, start, end, ResidentActivity.Exploring, "area", (rng.Chance(0.5f) ? AreaType.Riverside : AreaType.Coast).ToString(), Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Mining:
                    AddBlock(st, start, end, ResidentActivity.Mining, "mine", null, Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Music:
                    AddBlock(st, start, end, ResidentActivity.Playing, "facility_type", FacilityType.Plaza.ToString(), Vector3.zero, "hobby");
                    break;
                case ResidentHobby.Stargazing:
                    AddBlock(st, start, end, ResidentActivity.WatchingSky, "area", AreaType.Hill.ToString(), Vector3.zero, "hobby");
                    break;
                default:
                    AddBlock(st, start, end, ResidentActivity.Walking, "roam", null, Vector3.zero, "hobby");
                    break;
            }
        }

        private static bool IsOutdoorHobby(ResidentHobby h)
        {
            switch (h)
            {
                case ResidentHobby.Fishing:
                case ResidentHobby.Gardening:
                case ResidentHobby.CreatureWatching:
                case ResidentHobby.Collecting:
                case ResidentHobby.Walking:
                case ResidentHobby.Stargazing:
                    return true;
            }
            return false;
        }

        private static void AddBlock(ResidentState st, int start, int end, ResidentActivity act, string destKind, string destId, Vector3 pos, string note = null)
        {
            if (end <= start) return;
            st.plan.Add(new ScheduleBlock { start = start, end = end, activity = act, destKind = destKind, destId = destId, dx = pos.x, dy = pos.y, dz = pos.z, note = note });
        }

        private string PickFriendToVisit(Resident r, int day, SeededRandom rng)
        {
            var cands = new List<Resident>();
            foreach (var rel in r.State.relations)
            {
                if (rel.affinity < 25f || rel.quarrelUntilDay >= day) continue;
                var o = GetResident(rel.otherId);
                if (o == null || !o.State.active) continue;
                cands.Add(o);
            }
            if (cands.Count == 0) return null;
            return rng.WeightedPick(cands, o => 1f + r.Affinity(o.Id) / 25f).Id;
        }

        private string PickPlayerToVisit(Resident r)
        {
            if (Game.Service<IHousingService>() == null) return null;
            foreach (var b in r.State.bonds) if (b.friendship >= 60) return b.playerId;
            return null;
        }

        private bool NeedsPurchase(Resident r)
        {
            var g = r.State.midGoal;
            if (g.kind == GoalKind.BuyFurniture && r.State.money > 250) return true;
            if (g.kind == GoalKind.NewRecipe || g.kind == GoalKind.DecorateHome) return true;
            if (r.FindItem(Resident.IsEdible) == null && r.State.hunger > 0.4f) return true;
            return false;
        }

        private string PickFishingSpot(Resident r, SeededRandom rng)
        {
            var layout = Layout;
            if (layout == null || layout.FishingSpots.Count == 0) return null;
            // 好きな釣り場は住民ごとにだいたい決まっている（時々変える）
            int idx = rng.Chance(0.7f) ? (int)(StableHash.Derive(seed, "fav_spot." + r.Id) % (ulong)layout.FishingSpots.Count) : rng.Range(0, layout.FishingSpots.Count);
            return layout.FishingSpots[idx].id;
        }

        // ---------------- 行き先の解決 ----------------

        private Vector3 FacilityPos(FacilityType type, string jitterKey, float jitter)
        {
            var layout = Layout;
            if (layout == null) return Vector3.zero;
            var f = layout.GetFacility(type);
            if (f == null)
            {
                if (type == FacilityType.Lookout)
                {
                    var hill = new List<AreaRegion>(layout.GetAreas(AreaType.Hill));
                    if (hill.Count > 0) return layout.OnGround(hill[0].center + Jitter(jitterKey, jitter));
                }
                if (type == FacilityType.MineEntrance) return layout.OnGround(layout.MineEntrance + Jitter(jitterKey, jitter));
                var plaza = layout.GetFacility(FacilityType.Plaza);
                return layout.OnGround((plaza != null ? plaza.position : layout.PlayerSpawn) + Jitter(jitterKey, jitter));
            }
            var basePos = f.entrance != Vector3.zero ? f.entrance : f.position;
            return layout.OnGround(basePos + Jitter(jitterKey + type, jitter));
        }

        internal Vector3 VillageEdge()
        {
            var layout = Layout;
            if (layout == null) return new Vector3(60, 0, 60);
            var plaza = layout.GetFacility(FacilityType.Plaza);
            var center = plaza != null ? plaza.position : layout.PlayerSpawn;
            Vector3 best = center;
            float bestD = -1f;
            foreach (var n in layout.Roads.nodes)
            {
                float d = ResidentUtil.FlatDistance(n, center);
                if (d > bestD && d < 220f) { bestD = d; best = n; }
            }
            return layout.OnGround(best);
        }

        private Vector3 ResolveDestination(Resident r, ScheduleBlock b)
        {
            var layout = Layout;
            string key = r.Id;
            switch (b.destKind)
            {
                case "home":
                    return HomeYard(r);
                case "here":
                    return r.Avatar != null ? r.Avatar.transform.position : r.StatePosition;
                case "pos":
                case "edge":
                    return b.DestPos;
                case "work":
                {
                    var wp = JobTable.Workplace(r.State.job);
                    if (wp.HasValue) return FacilityPos(wp.Value, key, 1.6f);
                    return FacilityPos(FacilityType.Plaza, key, 6f);
                }
                case "work_fish":
                {
                    // 釣り名人：小屋に一番近い釣り場で働く
                    if (layout != null && layout.FishingSpots.Count > 0)
                    {
                        var hut = layout.GetFacility(FacilityType.FishingHut);
                        var near = hut != null ? hut.position : layout.PlayerSpawn;
                        FishingSpot best = null;
                        float bd = float.MaxValue;
                        foreach (var s in layout.FishingSpots)
                        {
                            float d = ResidentUtil.FlatDistance(s.standPosition, near);
                            if (d < bd) { bd = d; best = s; }
                        }
                        if (best != null) return layout.OnGround(best.standPosition);
                    }
                    return FacilityPos(FacilityType.FishingHut, key, 2f);
                }
                case "work_roam":
                case "roam":
                {
                    if (layout != null && layout.Roads.nodes.Count > 0)
                    {
                        var rng = SeededRandom.For(seed, "roam." + key + "." + Now / 30);
                        var n = layout.Roads.nodes[rng.Range(0, layout.Roads.nodes.Count)];
                        return layout.OnGround(n + Jitter(key + Now, 1.5f));
                    }
                    return FacilityPos(FacilityType.Plaza, key, 8f);
                }
                case "facility_type":
                {
                    if (System.Enum.TryParse(b.destId, out FacilityType ft))
                        return FacilityPos(ft, key, ft == FacilityType.Plaza ? 5.5f : 2.2f);
                    return FacilityPos(FacilityType.Plaza, key, 5f);
                }
                case "facility":
                {
                    var f = layout != null ? layout.GetFacilityById(b.destId) : null;
                    if (f != null) return layout.OnGround((f.entrance != Vector3.zero ? f.entrance : f.position) + Jitter(key, 1.5f));
                    return FacilityPos(FacilityType.Plaza, key, 5f);
                }
                case "spot":
                case "promise":
                {
                    if (layout != null && !string.IsNullOrEmpty(b.destId))
                    {
                        foreach (var s in layout.FishingSpots)
                            if (s.id == b.destId) return layout.OnGround(s.standPosition + Jitter(key, 1.2f));
                        var f = layout.GetFacilityById(b.destId);
                        if (f != null) return layout.OnGround(f.position + Jitter(key, 2f));
                    }
                    if (b.activity == ResidentActivity.Fishing && layout != null && layout.FishingSpots.Count > 0)
                        return layout.OnGround(layout.FishingSpots[0].standPosition + Jitter(key, 1.2f));
                    return FacilityPos(FacilityType.Plaza, key, 5f);
                }
                case "area":
                {
                    if (layout != null && System.Enum.TryParse(b.destId, out AreaType at))
                    {
                        var rng = SeededRandom.For(seed, "area." + key + "." + Today + "." + b.start);
                        return layout.RandomPointInArea(at, rng);
                    }
                    return FacilityPos(FacilityType.Plaza, key, 8f);
                }
                case "mine":
                    return layout != null ? layout.OnGround(layout.MineEntrance + Jitter(key, 1.2f)) : Vector3.zero;
                case "rocket":
                {
                    var rocket = Game.Service<IRocketService>();
                    var p = rocket != null ? rocket.RocketPosition : (layout != null ? layout.CrashSite : Vector3.zero);
                    return layout != null ? layout.OnGround(p + Jitter(key, 6f)) : p;
                }
                case "resident":
                {
                    var o = GetResident(b.destId);
                    if (o != null && o.State.active) return HomeYard(o) + Jitter(key, 1.4f);
                    return FacilityPos(FacilityType.Plaza, key, 5f);
                }
                case "player_home":
                {
                    var housing = Game.Service<IHousingService>();
                    if (housing != null) return (layout != null ? layout.OnGround(housing.PlayerHomeEntrance + Jitter(key, 1.8f)) : housing.PlayerHomeEntrance);
                    return FacilityPos(FacilityType.Plaza, key, 5f);
                }
                case "shop":
                {
                    var shop = Game.Service<IShopService>();
                    if (shop != null) return layout != null ? layout.OnGround(shop.EntrancePosition + Jitter(key, 1.4f)) : shop.EntrancePosition;
                    return FacilityPos(FacilityType.GeneralStore, key, 1.5f);
                }
                case "village_store":
                    return FacilityPos(FacilityType.GeneralStore, key, 1.5f);
                default:
                    return FacilityPos(FacilityType.Plaza, key, 5f);
            }
        }

        // ---------------- 行動の更新（1分ごと） ----------------

        private void UpdateBehavior(Resident r, bool snap)
        {
            var st = r.State;
            int day = Today;
            int minute = MinuteOfDay;

            // 演出中：RequestGoTo だけ効く
            if (st.cutsceneHold && !st.hasGoTo)
            {
                if (r.hasTarget) { r.hasTarget = false; r.path.Clear(); }
                return;
            }

            // 会話中は止まる
            if (st.holdUntil > Now)
            {
                if (r.hasTarget) { r.hasTarget = false; r.path.Clear(); }
                return;
            }

            // 一時的な行き先（エンディングの見送り・イベント・プロローグ）
            if (st.hasGoTo)
            {
                if (Now < st.goUntil)
                {
                    var goal = new Vector3(st.goX, st.goY, st.goZ);
                    if (!r.hasTarget && ResidentUtil.FlatDistance(r.Position, goal) > 1.2f) SetDestination(r, goal);
                    st.hidden = false;
                    st.activity = st.goReason != null && (st.goReason.Contains("ending") || st.goReason.Contains("farewell") || st.goReason.Contains("celebr") || st.goReason.Contains("event"))
                        ? ResidentActivity.Celebrating : (r.hasTarget ? ResidentActivity.Walking : ResidentActivity.Visiting);
                    SyncAvatar(r);
                    return;
                }
                st.hasGoTo = false;
                st.blockIndex = -1;
                if (st.cutsceneHold) return;
            }

            // 手招きされて主人公について行く
            if (!string.IsNullOrEmpty(st.followPlayerId))
            {
                var pl = Game.Players.Get(st.followPlayerId);
                if (pl != null && Now < st.followUntil && pl.CurrentAreaId == AreaIds.Overworld)
                {
                    var target = pl.Position - pl.Forward * 1.8f + Jitter(r.Id, 0.8f);
                    if (ResidentUtil.FlatDistance(r.Position, target) > 2.5f) SetDestination(r, target);
                    st.activity = ResidentActivity.Walking;
                    st.hidden = false;
                    SyncAvatar(r);
                    return;
                }
                st.followPlayerId = null;
                st.blockIndex = -1;
            }

            // 立ち話中
            if (!string.IsNullOrEmpty(st.chatPartnerId))
            {
                var other = GetResident(st.chatPartnerId);
                if (other != null && Now < ChatEnd(r) && other.State.chatPartnerId == r.Id) return;
                EndChat(r);
            }

            // 雨宿り（雨嫌いの住民は軒下へ。雨が止んだら予定に戻る：時間・天候仕様 §10）
            if (st.activity == ResidentActivity.ShelteringFromRain)
            {
                var w = Game.Service<IWeatherService>();
                if (w != null && w.Current.IsRaining) return;
                st.blockIndex = -1;
            }

            if (st.planDay != day) PlanDay(r, day);
            int idx = -1;
            for (int i = 0; i < st.plan.Count; i++)
                if (minute >= st.plan[i].start && minute < st.plan[i].end) { idx = i; break; }
            if (idx < 0)
            {
                if (st.plan.Count == 0) return;
                idx = st.plan.Count - 1;
            }
            if (idx != st.blockIndex) StartBlock(r, idx, snap);
            else if (st.arrived) BlockTick(r, st.plan[idx]);
        }

        private long ChatEnd(Resident r) => chatEnds.TryGetValue(r.Id, out var t) ? t : 0;
        private readonly Dictionary<string, long> chatEnds = new Dictionary<string, long>();

        private void StartBlock(Resident r, int idx, bool snap)
        {
            var st = r.State;
            var b = st.plan[idx];
            st.blockIndex = idx;
            st.arrived = false;
            // 同じ予定をやり直す（会話・RequestGoTo・雨宿りの後など）ときは、済ませた到着時の行動を繰り返さない
            st.blockActionDone = st.actionDoneKey != null && st.actionDoneKey == BlockActionKey(b);
            if (b.activity == ResidentActivity.Shopping && b.destKind == "shop" && !st.blockActionDone)
            {
                // 主人公の商店が閉まっている・何も並んでいない時は、村の雑貨屋へ行く（営業時間は看板で分かる）
                var shop = Game.Service<IShopService>();
                if (shop == null || !shop.IsOpen || shop.OfferCount == 0) b.destKind = "village_store";
            }
            st.activity = b.activity;
            st.activityTarget = b.destId;
            st.carrying = b.activity == ResidentActivity.Packing;
            st.heldItemId = null;
            st.hidden = false;
            var dest = ResolveDestination(r, b);
            if (b.activity == ResidentActivity.Sleeping && b.destKind == "edge")
            {
                // 遊びに来る前・帰った後の元住民は村の外（表示しない）
                st.hidden = true;
                r.StatePosition = dest;
                st.arrived = true;
                SyncAvatar(r);
                return;
            }
            SetDestination(r, dest);
            if (snap) SnapToTarget(r);
            SyncAvatar(r);
            // 移動中は歩いている（到着してから本来の行動）
            if (r.hasTarget && b.activity != ResidentActivity.Packing) st.activity = ResidentActivity.Walking;
            if (!r.hasTarget) OnArrive(r);
        }

        internal void SetDestination(Resident r, Vector3 dest)
        {
            var layout = Layout;
            var from = r.Avatar != null ? r.Avatar.transform.position : r.StatePosition;
            r.path.Clear();
            r.pathIndex = 0;
            if (ResidentUtil.FlatDistance(from, dest) < ResidentTuning.ArriveDistance)
            {
                r.hasTarget = false;
                return;
            }
            if (layout != null && layout.Roads.nodes.Count > 1 && ResidentUtil.FlatDistance(from, dest) > 12f)
            {
                var path = layout.Roads.FindPath(from, dest);
                // 最寄りノードが後戻りになる場合は省く
                if (path.Count > 1 && ResidentUtil.FlatDistance(from, path[1]) < ResidentUtil.FlatDistance(path[0], path[1])) path.RemoveAt(0);
                r.path.AddRange(path);
            }
            else r.path.Add(dest);
            r.currentTarget = dest;
            r.hasTarget = true;
        }

        private void SnapToTarget(Resident r)
        {
            if (!r.hasTarget) return;
            var dest = r.currentTarget;
            var layout = Layout;
            if (layout != null) dest = layout.OnGround(dest);
            r.StatePosition = dest;
            if (r.Avatar != null) r.Avatar.transform.position = dest;
            r.path.Clear();
            r.hasTarget = false;
            OnArrive(r);
        }

        /// <summary>実時間で道を歩く（ホスト）。</summary>
        private void MoveResidents(float dt)
        {
            var layout = Layout;
            float timeScale = Clock != null ? Mathf.Clamp(Clock.SpeedMultiplier, 0.5f, 3f) : 1f;
            foreach (var r in ordered)
            {
                if (r.Avatar == null) continue;
                var tr = r.Avatar.transform;
                // 他モジュール（Story の演出など）が Transform を直接動かした：その位置を正として経路を作り直す
                if (r.hasWrittenPos && (tr.position - r.lastWrittenPos).sqrMagnitude > 0.25f) OnExternallyMoved(r, tr.position);
                if (r.hasTarget && r.path.Count > 0)
                {
                    var target = r.path[Mathf.Min(r.pathIndex, r.path.Count - 1)];
                    var pos = tr.position;
                    var to = target - pos;
                    to.y = 0;
                    float dist = to.magnitude;
                    float speed = ResidentTuning.WalkSpeed * r.SpeedFactor * timeScale;
                    if (r.State.followPlayerId != null) speed *= 1.6f;
                    if (dist < ResidentTuning.ArriveDistance)
                    {
                        r.pathIndex++;
                        if (r.pathIndex >= r.path.Count)
                        {
                            r.hasTarget = false;
                            r.path.Clear();
                            OnArrive(r);
                        }
                    }
                    else
                    {
                        var step = to / dist * Mathf.Min(dist, speed * dt);
                        var np = pos + step;
                        if (layout != null) np.y = layout.HeightAt(np.x, np.z);
                        tr.position = np;
                        var face = Quaternion.LookRotation(new Vector3(to.x, 0, to.z));
                        tr.rotation = Quaternion.RotateTowards(tr.rotation, face, 540f * dt);
                    }
                }
                else if (layout != null)
                {
                    var pos = tr.position;
                    float gy = layout.HeightAt(pos.x, pos.z);
                    if (Mathf.Abs(pos.y - gy) > 0.05f) tr.position = new Vector3(pos.x, gy, pos.z);
                }
                // 話し相手・会話中の主人公の方を向く
                if (!r.hasTarget)
                {
                    Vector3? look = null;
                    if (!string.IsNullOrEmpty(r.State.chatPartnerId))
                    {
                        var o = GetResident(r.State.chatPartnerId);
                        if (o != null) look = o.Position;
                    }
                    else if (r.State.holdUntil > Now && !string.IsNullOrEmpty(r.State.holdForPlayerId))
                    {
                        var pl = Game.Players.Get(r.State.holdForPlayerId);
                        if (pl != null) look = pl.Position;
                    }
                    else if (r.State.activity == ResidentActivity.Fishing && layout != null)
                    {
                        var spot = NearestFishingSpot(tr.position);
                        if (spot != null) look = spot.waterPosition;
                    }
                    if (look.HasValue)
                    {
                        var d = look.Value - tr.position;
                        d.y = 0;
                        if (d.sqrMagnitude > 0.01f) tr.rotation = Quaternion.RotateTowards(tr.rotation, Quaternion.LookRotation(d), 360f * dt);
                    }
                }
                r.StatePosition = tr.position;
                r.State.rotY = tr.eulerAngles.y;
                r.lastWrittenPos = tr.position;
                r.hasWrittenPos = true;
            }
        }

        /// <summary>外部から位置が変えられた時の同期（Warp と同じ扱い）。</summary>
        private void OnExternallyMoved(Resident r, Vector3 pos)
        {
            r.StatePosition = pos;
            r.lastWrittenPos = pos;
            r.path.Clear();
            r.pathIndex = 0;
            if (r.State.hasGoTo)
            {
                var goal = new Vector3(r.State.goX, r.State.goY, r.State.goZ);
                if (ResidentUtil.FlatDistance(pos, goal) > ResidentTuning.ArriveDistance * 2f) SetDestination(r, goal);
                else r.hasTarget = false;
            }
            else if (r.hasTarget)
            {
                var dest = r.currentTarget;
                r.hasTarget = false;
                if (!r.State.cutsceneHold) SetDestination(r, dest);
            }
        }

        private FishingSpot NearestFishingSpot(Vector3 p)
        {
            var layout = Layout;
            if (layout == null) return null;
            FishingSpot best = null;
            float bd = 6f;
            foreach (var s in layout.FishingSpots)
            {
                float d = ResidentUtil.FlatDistance(s.standPosition, p);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // ---------------- 到着時・滞在中の行動 ----------------

        private string BlockActionKey(ScheduleBlock b) => Today + ":" + b.start + ":" + (int)b.activity;

        private void OnArrive(Resident r)
        {
            var st = r.State;
            // RequestGoTo（行事・演出・商店の立ち寄り）・手招き・雨宿りの行き先に着いただけ。
            // ここで予定の到着時の行動（買い物＝ResidentVisit など）をすると、店から離れた場所で来店処理が走ったり、
            // 雨宿りの状態が予定の行動で上書きされたりするので、予定の行動は行わない（終われば予定をやり直す）。
            if (st.hasGoTo || !string.IsNullOrEmpty(st.followPlayerId))
            {
                SyncAvatar(r);
                return;
            }
            if (st.activity == ResidentActivity.ShelteringFromRain)
            {
                st.arrived = true; // 同じ軒下での立ち話のきっかけ（SocialTick は arrived を見る）
                SyncAvatar(r);
                return;
            }
            st.arrived = true;
            if (st.blockIndex < 0 || st.blockIndex >= st.plan.Count) return;
            var b = st.plan[st.blockIndex];
            st.activity = b.activity;
            bool atHomeNight = (b.activity == ResidentActivity.AtHome || b.activity == ResidentActivity.Resting) && (MinuteOfDay >= 20 * 60 + 30 || MinuteOfDay < 5 * 60);
            st.hidden = b.activity == ResidentActivity.Sleeping || b.activity == ResidentActivity.Mining || atHomeNight;
            if (b.destKind == "edge") st.hidden = true; // 村の外へ出た
            if (b.activity == ResidentActivity.Sleeping) st.face = Expression.Sleepy;
            else st.face = MoodFace(r);
            SyncAvatar(r);
            DoArrivalAction(r, b);
            MarkDirty();
        }

        private Expression MoodFace(Resident r)
        {
            var st = r.State;
            if (r.IsMovingOut) return Expression.Thinking;
            if (st.hunger > 0.8f) return Expression.Worried;
            if (st.mood > 0.45f) return Expression.Happy;
            if (st.mood < -0.4f) return Expression.Sad;
            return Expression.Neutral;
        }

        private void DoArrivalAction(Resident r, ScheduleBlock b)
        {
            if (r.State.blockActionDone) return;
            DoArrivalActionInner(r, b);
            if (r.State.blockActionDone) r.State.actionDoneKey = BlockActionKey(b);
        }

        private void DoArrivalActionInner(Resident r, ScheduleBlock b)
        {
            var st = r.State;
            int day = Today;
            switch (b.activity)
            {
                case ResidentActivity.Shopping:
                    st.blockActionDone = true;
                    DoShopping(r, b);
                    break;
                case ResidentActivity.Eating:
                    st.blockActionDone = true;
                    EatSomething(r, b.destKind == "facility_type");
                    break;
                case ResidentActivity.Working:
                case ResidentActivity.Crafting:
                case ResidentActivity.Fishing:
                case ResidentActivity.Gardening:
                case ResidentActivity.Farming:
                case ResidentActivity.Cooking:
                case ResidentActivity.WatchingSky:
                case ResidentActivity.CatchingCreatures:
                case ResidentActivity.Mining:
                case ResidentActivity.Playing:
                case ResidentActivity.Exploring:
                case ResidentActivity.Resting:
                    if (b.note == "work" || b.note == "forecast" || b.note == "read_sky")
                    {
                        if (st.lastWorkDay != day)
                        {
                            st.lastWorkDay = day;
                            st.workDaysTotal++;
                            OnWorkStarted(r);
                        }
                    }
                    if (b.note == "forecast")
                    {
                        st.blockActionDone = true;
                        PostForecast(r);
                    }
                    if (b.note == "read_sky") st.forecastKnownDay = day;
                    if (b.activity == ResidentActivity.Cooking && b.note != "work")
                    {
                        st.blockActionDone = true;
                        CookAtHome(r);
                    }
                    if (b.activity == ResidentActivity.Fishing && st.fishingDays >= 0 && (st.lastCaughtDay != day || b.note != "work"))
                    {
                        if (b.note == "hobby" || b.note == "work" || b.note != null && b.note.StartsWith("promise:"))
                            st.fishingDays++;
                    }
                    if (b.note != null && b.note.StartsWith("promise:"))
                    {
                        // 約束を守った：一緒に過ごした記憶
                        string partner = b.note.Substring("promise:".Length);
                        var o = GetResident(partner);
                        if (o != null && ResidentUtil.FlatDistance(o.Position, r.Position) < 12f)
                        {
                            var rel = r.Relation(partner);
                            rel.affinity = Mathf.Min(100f, rel.affinity + 4f);
                            rel.trust = Mathf.Min(100f, rel.trust + 5f);
                            rel.timesTogether++;
                            if (!r.HasMemoryWithResident("promise_kept", partner, day))
                                r.AddMemory(new ResidentMemoryEntry { kind = "promise_kept", otherResidentId = partner, day = day, importance = 0.6f });
                        }
                    }
                    break;
                case ResidentActivity.Visiting:
                    st.blockActionDone = true;
                    ArriveVisit(r, b);
                    break;
                case ResidentActivity.AtPlaza:
                    if (b.note == "greet_village" || r.State.movedInDay == day && day > 0)
                    {
                        st.blockActionDone = true;
                        PostNewcomerHello(r);
                    }
                    break;
                case ResidentActivity.Walking:
                    if (b.note == "greet_village")
                    {
                        st.blockActionDone = true;
                        PostNewcomerHello(r);
                    }
                    break;
                case ResidentActivity.Celebrating:
                    if (b.note == "birthday" && st.birthdayCelebratedDay != day)
                    {
                        st.birthdayCelebratedDay = day;
                        st.mood = Mathf.Min(1f, st.mood + 0.4f);
                        r.AddMemory(new ResidentMemoryEntry { kind = "birthday", day = day, importance = 0.7f });
                    }
                    break;
            }
        }

        /// <summary>滞在中の周期的な行動（釣果・仕事の進み具合・交流）。</summary>
        private void BlockTick(Resident r, ScheduleBlock b)
        {
            var st = r.State;
            int minute = MinuteOfDay;
            int day = Today;
            if (minute % 15 != (StableHash.Int(seed, r.Id, 0, 15))) return;
            var rng = SeededRandom.For(seed, "tick." + r.Id + "." + Now);
            if ((b.destKind == "roam" || b.destKind == "work_roam") && rng.Chance(0.7f))
            {
                // 散歩・配達：次の道へ（毎日同じ場所に立ち止まらない）
                SetDestination(r, ResolveDestination(r, b));
                return;
            }
            switch (b.activity)
            {
                case ResidentActivity.Fishing:
                    if (rng.Chance(r.State.job == ResidentJob.FishingMaster ? 0.3f : 0.18f)) CatchFish(r, rng);
                    break;
                case ResidentActivity.Mining:
                    if (rng.Chance(0.2f)) MineOre(r, rng);
                    break;
                case ResidentActivity.CatchingCreatures:
                    if (rng.Chance(0.15f) && st.midGoal.kind == GoalKind.RaiseCreature) st.midGoal.progress += 0.5f;
                    break;
                case ResidentActivity.Crafting:
                case ResidentActivity.Working:
                    if (st.job == ResidentJob.Blacksmith && st.midGoal.kind == GoalKind.Masterpiece)
                    {
                        // 鉱石があれば最高傑作の作業が進む（主人公が届けなくても、自分で買って続ける：§3）
                        var ore = r.FindItem(d => d.Category == ItemCategory.Ore || d.HasTag(ItemTags.Ore));
                        if (ore != null && rng.Chance(0.3f))
                        {
                            r.RemoveItem(ore.itemId, 1);
                            st.midGoal.progress += 1f;
                        }
                    }
                    break;
                case ResidentActivity.WatchingSky:
                    var w = Game.Service<IWeatherService>();
                    if (w != null && w.Current.Has(AlienPhenomenon.StarBand))
                    {
                        st.mood = Mathf.Min(1f, st.mood + 0.05f);
                        if (!r.HasMemoryKindOnDay("saw_starband", day)) r.AddMemory(new ResidentMemoryEntry { kind = "saw_starband", day = day, importance = 0.5f });
                    }
                    break;
            }
            // 屋外にいる雨嫌いの住民は雨宿り
            var weather = Game.Service<IWeatherService>();
            if (weather != null && weather.Current.IsRaining && r.P.rainLike < -0.3f && IsOutdoorActivity(st.activity) && !st.hidden)
                StartSheltering(r);
        }

        private static bool IsOutdoorActivity(ResidentActivity a)
        {
            switch (a)
            {
                case ResidentActivity.Fishing:
                case ResidentActivity.Gardening:
                case ResidentActivity.Walking:
                case ResidentActivity.AtPlaza:
                case ResidentActivity.CatchingCreatures:
                case ResidentActivity.Exploring:
                case ResidentActivity.WatchingSky:
                case ResidentActivity.Playing:
                    return true;
            }
            return false;
        }

        internal void StartSheltering(Resident r)
        {
            var layout = Layout;
            if (layout == null) return;
            FacilityInfo best = null;
            float bd = 60f;
            foreach (var f in layout.Facilities)
            {
                if (f.type == FacilityType.Streetlight || f.type == FacilityType.Bench || f.type == FacilityType.Well) continue;
                float d = ResidentUtil.FlatDistance(f.position, r.Position);
                if (d < bd) { bd = d; best = f; }
            }
            if (best == null) return;
            r.State.activity = ResidentActivity.ShelteringFromRain;
            r.State.chatPartnerId = null;
            // 同じ軒下に集まる（偶然の会話のきっかけ）ため、ずれは小さめ
            SetDestination(r, layout.OnGround((best.entrance != Vector3.zero ? best.entrance : best.position) + Jitter(r.Id, 1.3f)));
            r.State.face = Expression.Worried;
            SyncAvatar(r);
        }

        // ---------------- 仕事・経済 ----------------

        private void OnWorkStarted(Resident r)
        {
            var rng = SeededRandom.For(seed, "work." + r.Id + "." + Today);
            switch (r.State.job)
            {
                case ResidentJob.Cook:
                {
                    // 料理人は毎日料理を作る（あとで友達や主人公に振る舞う：メインストーリー第3章）
                    var dish = PickCheapItem(ItemCategory.Food, rng) ?? PickCheapEdible(rng);
                    if (dish != null) r.AddItem(new ItemStack(dish.Id, 2));
                    break;
                }
                case ResidentJob.FarmingMaster:
                {
                    var crop = PickCheapItem(ItemCategory.Crop, rng);
                    if (crop != null) r.AddItem(new ItemStack(crop.Id, 2));
                    break;
                }
                case ResidentJob.Florist:
                {
                    var flower = PickTagged(ItemTags.Flower, rng);
                    if (flower != null && rng.Chance(0.5f)) r.AddItem(new ItemStack(flower.Id, 1));
                    break;
                }
            }
        }

        private void DailyIncome(int day)
        {
            foreach (var r in ordered)
            {
                if (!r.State.active) continue;
                int income = r.State.lastWorkDay == day ? JobTable.DailyWage(r.State.job) : ResidentTuning.BaseDailyIncome;
                r.State.money += income;
                if (r.State.midGoal.kind == GoalKind.SaveMoney) r.State.midGoal.progress = r.State.money;
                // お金が余りすぎたら夢のための貯金に回す（村の経済が膨らみ続けないように：バランス §7.6）
                if (r.State.money > 4000)
                {
                    r.State.longGoal.progress += 1f;
                    r.State.money = 3200;
                }
            }
        }

        private ShoppingIntent BuildIntent(Resident r, string note)
        {
            var st = r.State;
            int day = Today;
            float frac = Mathf.Lerp(ResidentTuning.BudgetFractionMax, ResidentTuning.BudgetFractionMin, r.P.thrift);
            var intent = new ShoppingIntent { budget = Mathf.Max(30, Mathf.RoundToInt(st.money * frac)), reasonTag = note };
            // 友達の誕生日・お別れ：贈り物を買う
            foreach (var o in ordered)
            {
                if (o == r || !o.State.active) continue;
                float aff = r.Affinity(o.Id);
                if (aff > 35f && (o.IsBirthday(day) || o.IsBirthday(day + 1)))
                {
                    intent.need = ShoppingNeed.Gift; intent.forResidentId = o.Id; intent.reasonTag = "party";
                    intent.preferredTag = TopLike(o);
                    intent.budget = Mathf.Min(intent.budget, 300);
                    return intent;
                }
                if (aff > 30f && o.IsMovingOut && o.State.moveOutDay - day <= 2)
                {
                    intent.need = ShoppingNeed.Gift; intent.forResidentId = o.Id; intent.reasonTag = "farewell";
                    intent.preferredTag = TopLike(o);
                    intent.budget = Mathf.Min(intent.budget, 300);
                    return intent;
                }
            }
            if (note == "lunch_shopping" || st.hunger > ResidentTuning.HungryThreshold && r.FindItem(Resident.IsEdible) == null)
            {
                intent.need = ShoppingNeed.Food; intent.reasonTag = "hungry"; intent.preferredTag = TopFoodLike(r);
                return intent;
            }
            switch (st.midGoal.kind)
            {
                case GoalKind.NewRecipe: intent.need = ShoppingNeed.Ingredient; intent.reasonTag = "cooking"; intent.preferredTag = st.midGoal.tag; return intent;
                case GoalKind.BuyFurniture:
                    if (st.money > 250) { intent.need = ShoppingNeed.Furniture; intent.reasonTag = "redecorate"; intent.preferredTag = st.midGoal.tag ?? r.Profile.favoriteColorTag; return intent; }
                    break;
                case GoalKind.DecorateHome: intent.need = ShoppingNeed.Decor; intent.reasonTag = "decorate"; intent.preferredTag = r.Profile.favoriteColorTag; return intent;
                case GoalKind.Masterpiece: intent.need = ShoppingNeed.Material; intent.reasonTag = "masterpiece"; intent.preferredTag = ItemTags.Ore; return intent;
                case GoalKind.RaiseCreature: intent.need = ShoppingNeed.Curiosity; intent.reasonTag = "creature"; intent.preferredTag = ItemTags.CreatureHabitat; return intent;
            }
            if (r.Profile.hobby == ResidentHobby.Collecting || r.P.curiosity > 0.8f) { intent.need = ShoppingNeed.Curiosity; intent.reasonTag = "curious"; return intent; }
            if (r.Profile.hobby == ResidentHobby.Fishing && r.P.gluttony > 0.5f) { intent.need = ShoppingNeed.Fish; intent.reasonTag = "dinner"; return intent; }
            intent.need = st.hunger > 0.4f ? ShoppingNeed.Food : ShoppingNeed.Any;
            intent.preferredTag = TopLike(r);
            return intent;
        }

        private static string TopLike(Resident r)
        {
            string best = r.Profile.favoriteColorTag;
            float bw = 0.3f;
            foreach (var tw in r.Profile.likes) if (tw.weight > bw) { bw = tw.weight; best = tw.tag; }
            return best;
        }

        private static string TopFoodLike(Resident r)
        {
            string[] food = { ItemTags.Sweet, ItemTags.Spicy, ItemTags.Sour, ItemTags.Savory, ItemTags.Fruit, ItemTags.Vegetable, ItemTags.Fish };
            string best = null;
            float bw = 0f;
            foreach (var tw in r.Profile.likes)
                if (System.Array.IndexOf(food, tw.tag) >= 0 && tw.weight > bw) { bw = tw.weight; best = tw.tag; }
            return best;
        }

        private void DoShopping(Resident r, ScheduleBlock b)
        {
            var st = r.State;
            st.lastShopVisitDay = Today;
            var intent = BuildIntent(r, b.note);
            bool bought = false;
            if (b.destKind == "shop")
            {
                var shop = Game.Service<IShopService>();
                if (shop != null && shop.IsOpen)
                {
                    // 主人公の商店：来店して見る・買う・反応するのは Shop モジュールが行う（住民はこれを呼ぶだけ）
                    pendingIntents[r.Id] = intent;
                    ShopVisitResult res = null;
                    try { res = shop.ResidentVisit(r, intent); }
                    catch (System.Exception e) { Debug.LogException(e); }
                    pendingIntents.Remove(r.Id);
                    if (res != null && res.bought && !string.IsNullOrEmpty(res.itemId))
                    {
                        bought = true;
                        st.lastBoughtItemId = res.itemId;
                        st.lastBoughtDay = Today;
                        st.lastBoughtFromPlayer = true;
                        st.lastBoughtSatisfaction = res.satisfaction;
                        st.mood = Mathf.Clamp(st.mood + res.satisfaction * 0.15f, -1f, 1f);
                        if (st.midGoal.kind == GoalKind.BuyFurniture && IsFurniture(res.itemId)) st.midGoal.progress += 1f;
                    }
                    else if (res != null && res.thoughtTooExpensive)
                    {
                        st.mood = Mathf.Clamp(st.mood - 0.05f, -1f, 1f);
                    }
                }
            }
            if (!bought && (b.destKind != "shop" || intent.need == ShoppingNeed.Food || intent.need == ShoppingNeed.Gift))
            {
                // 村の雑貨屋（住民の店）で買う：代金は雑貨屋の住民の収入になる
                VillageStorePurchase(r, intent);
            }
            MarkDirty();
        }

        private static bool IsFurniture(string itemId)
        {
            var d = ItemRegistry.Get(itemId);
            return d != null && (d.Category == ItemCategory.Furniture || d.HasTag(ItemTags.Furniture));
        }

        /// <summary>村の雑貨屋の品揃え：ありふれた食べ物・種・装飾・家具（主人公の商店の競合になりすぎないよう希少品は置かない）。</summary>
        private void VillageStorePurchase(Resident r, ShoppingIntent intent)
        {
            var rng = SeededRandom.For(seed, "store." + r.Id + "." + Now);
            var cands = new List<ItemDefinition>();
            foreach (var d in ItemRegistry.All)
            {
                if (d.Rarity > 1 || d.BasePrice <= 0 || d.HasTag(ItemTags.NotSellable)) continue;
                if (d.Category == ItemCategory.RocketPart || d.Category == ItemCategory.KeyItem || d.Category == ItemCategory.Tool || d.Category == ItemCategory.Letter) continue;
                int price = Mathf.CeilToInt(d.BasePrice * 1.15f);
                if (price > intent.budget || price > r.State.money) continue;
                bool ok;
                switch (intent.need)
                {
                    case ShoppingNeed.Food: ok = Resident.IsEdible(d) && (d.Category == ItemCategory.Food || d.Category == ItemCategory.Processed || d.Category == ItemCategory.Crop); break;
                    case ShoppingNeed.Ingredient: ok = d.HasTag(ItemTags.Ingredient) || d.Category == ItemCategory.Crop || d.Category == ItemCategory.Fish; break;
                    case ShoppingNeed.Furniture: ok = d.Category == ItemCategory.Furniture; break;
                    case ShoppingNeed.Decor: ok = d.Category == ItemCategory.Decor || d.HasTag(ItemTags.Flower); break;
                    case ShoppingNeed.Gift: ok = d.Category == ItemCategory.Food || d.Category == ItemCategory.Decor || d.HasTag(ItemTags.Flower) || d.Category == ItemCategory.Processed; break;
                    case ShoppingNeed.Material: ok = d.Category == ItemCategory.Ore || d.Category == ItemCategory.Material; break;
                    default: ok = d.Category == ItemCategory.Food || d.Category == ItemCategory.Processed || d.Category == ItemCategory.Seed || d.Category == ItemCategory.Decor; break;
                }
                if (ok) cands.Add(d);
            }
            if (cands.Count == 0)
            {
                // 品物が無くても、食事なら共用調理場で軽く食べて済ませる
                if (intent.need == ShoppingNeed.Food && r.State.money >= 20)
                {
                    r.State.money -= 20;
                    var cook = FindByJobInternal(ResidentJob.Cook);
                    if (cook != null && cook != r) cook.State.money += 20;
                    r.State.hunger = Mathf.Max(0f, r.State.hunger - 0.6f);
                }
                return;
            }
            Resident target = intent.need == ShoppingNeed.Gift ? GetResident(intent.forResidentId) : r;
            var pick = rng.WeightedPick(cands, d =>
            {
                float v = (target ?? r).EvaluateItem(d) + 1.1f;
                if (!string.IsNullOrEmpty(intent.preferredTag) && d.HasTag(intent.preferredTag)) v += 0.8f;
                return v * v;
            });
            if (pick == null) return;
            int cost = Mathf.CeilToInt(pick.BasePrice * 1.15f);
            r.State.money -= cost;
            var merchant = FindByJobInternal(ResidentJob.Merchant);
            if (merchant != null && merchant != r) merchant.State.money += cost;
            var stack = new ItemStack(pick.Id, 1);
            if (intent.need == ShoppingNeed.Gift && !string.IsNullOrEmpty(intent.forResidentId)) stack.data = GiftTag + intent.forResidentId;
            r.AddItem(stack);
            r.State.lastBoughtItemId = pick.Id;
            r.State.lastBoughtDay = Today;
            r.State.lastBoughtFromPlayer = false;
            r.State.lastBoughtSatisfaction = r.EvaluateItem(pick);
            EventBus.Publish(new ResidentPurchaseEvent { ResidentId = r.Id, ItemId = pick.Id, Price = cost, Where = "village_store" });
            HandleReceivedItem(r, stack, "village_store");
        }

        /// <summary>
        /// 受け取った物を生活に使う（住民生活仕様 §4「買った物が生活に残る」、家具仕様 §15）。
        /// 家具・飾り → 家に置く／花・宝石 → 飾る／食べ物 → 空腹なら食べる、料理好きは料理に使う／贈り物用 → 相手に渡すまで持つ。
        /// </summary>
        private void HandleReceivedItem(Resident r, ItemStack stack, string reason)
        {
            var def = stack.Definition;
            if (def == null) return;
            if (!string.IsNullOrEmpty(stack.data) && stack.data.StartsWith(GiftTag)) return; // 贈り物は渡すまで持っておく
            var housing = Game != null ? Game.Service<IHousingService>() : null;
            if (Resident.IsDisplayable(def) && !Resident.IsEdible(def))
            {
                r.RemoveItem(def.Id, 1);
                ResidentUtil.AddUnique(r.State.homeItems, def.Id);
                if (r.State.homeItems.Count > 30) r.State.homeItems.RemoveAt(0);
                try { housing?.AddFurnitureToResidentHome(r.Id, def.Id); }
                catch (System.Exception e) { Debug.LogException(e); }
                if (r.State.midGoal.kind == GoalKind.DecorateHome) r.State.midGoal.progress += 1f;
                r.State.mood = Mathf.Clamp(r.State.mood + 0.1f, -1f, 1f);
                return;
            }
            if (Resident.IsEdible(def))
            {
                if (r.State.midGoal.kind == GoalKind.NewRecipe && (def.HasTag(ItemTags.Ingredient) || def.Category == ItemCategory.Crop || def.Category == ItemCategory.Fish))
                {
                    r.State.midGoal.progress += 1f;
                    return;
                }
                if (r.State.hunger > ResidentTuning.HungryThreshold && reason != "gift_from_player") EatSomething(r, false);
            }
            else if (def.Category == ItemCategory.Ore && r.State.midGoal.kind == GoalKind.Masterpiece)
            {
                // 鉱石は作業のときに使う
            }
        }

        private void EatSomething(Resident r, bool eatingOut)
        {
            var st = r.State;
            ItemStack best = null;
            float bv = -9f;
            foreach (var s in st.items)
            {
                if (s == null || s.IsEmpty) continue;
                if (!string.IsNullOrEmpty(s.data) && s.data.StartsWith(GiftTag)) continue;
                var d = s.Definition;
                if (!Resident.IsEdible(d)) continue;
                if (st.midGoal.kind == GoalKind.NewRecipe && (d.HasTag(ItemTags.Ingredient) || d.Category == ItemCategory.Crop) && st.midGoal.progress < st.midGoal.target) continue;
                float v = r.EvaluateItem(d);
                if (v > bv) { bv = v; best = s; }
            }
            if (best != null)
            {
                string id = best.itemId;
                r.RemoveItem(id, 1);
                st.lastAteItemId = id;
                st.lastAteDay = Today;
                st.lastAteLiking = bv;
                st.hunger = 0f;
                st.mood = Mathf.Clamp(st.mood + bv * 0.2f, -1f, 1f);
                st.heldItemId = id;
                if (r.Avatar != null) r.Avatar.PlayGesture(new GestureCue(Gesture.Eat, id));
                r.AddMemory(new ResidentMemoryEntry { kind = "ate", itemId = id, day = Today, importance = Mathf.Abs(bv) > 0.5f ? 0.5f : 0.2f });
                return;
            }
            if (eatingOut && st.money >= 25)
            {
                st.money -= 25;
                var cook = FindByJobInternal(ResidentJob.Cook);
                if (cook != null && cook != r) cook.State.money += 25;
                st.hunger = 0f;
                return;
            }
            st.hunger = Mathf.Max(0f, st.hunger - 0.6f); // 家の食料（ゲーム上の品物ではない）
        }

        private void CookAtHome(Resident r)
        {
            var st = r.State;
            var ing = r.FindItem(d => d.HasTag(ItemTags.Ingredient) || d.Category == ItemCategory.Crop || d.Category == ItemCategory.Fish);
            if (ing == null) return;
            string id = ing.itemId;
            r.RemoveItem(id, 1);
            r.AddMemory(new ResidentMemoryEntry { kind = "cooked", itemId = id, day = Today, importance = 0.4f });
            st.mood = Mathf.Clamp(st.mood + 0.1f, -1f, 1f);
            st.hunger = Mathf.Max(0f, st.hunger - 0.5f);
            st.lastAteItemId = id;
            st.lastAteDay = Today;
            st.lastAteLiking = r.EvaluateItem(ItemRegistry.Get(id)) + 0.2f;
            if (st.midGoal.kind == GoalKind.NewRecipe && st.midGoal.progress >= st.midGoal.target - 0.01f)
                st.midGoal.progress = st.midGoal.target + 0.01f; // 新しい料理の完成
        }

        private void CatchFish(Resident r, SeededRandom rng)
        {
            var fishing = Game.Service<IFishingService>();
            var ids = new List<string>();
            if (fishing != null) foreach (var id in fishing.CatchableFishIds) if (ItemRegistry.Exists(id)) ids.Add(id);
            if (ids.Count == 0) foreach (var d in ItemRegistry.ByCategory(ItemCategory.Fish)) if (d.Rarity <= 2) ids.Add(d.Id);
            if (ids.Count == 0) return;
            string fish = rng.WeightedPick(ids, id => 1f / (1f + ItemRegistry.Get(id).Rarity * 2f));
            r.AddItem(new ItemStack(fish, 1));
            r.State.lastCaughtItemId = fish;
            r.State.lastCaughtDay = Today;
            r.State.mood = Mathf.Min(1f, r.State.mood + 0.1f);
            if (r.State.midGoal.kind == GoalKind.BigFish && ItemRegistry.Get(fish).Rarity >= 2) r.State.midGoal.progress += 1f;
            else if (r.State.midGoal.kind == GoalKind.BigFish) r.State.midGoal.progress += 0.25f;
            r.AddMemory(new ResidentMemoryEntry { kind = "caught_fish", itemId = fish, day = Today, importance = ItemRegistry.Get(fish).Rarity >= 3 ? 0.8f : 0.3f });
            if (r.Avatar != null)
            {
                r.Avatar.ShowExpression(Expression.Excited, 2.5f);
                r.Avatar.PlayGesture(new GestureCue(Gesture.Jump));
            }
        }

        private void MineOre(Resident r, SeededRandom rng)
        {
            var mining = Game.Service<IMiningService>();
            var ids = new List<string>();
            if (mining != null)
            {
                for (int f = 1; f <= Mathf.Min(3, Mathf.Max(1, mining.FloorCount)); f++)
                    foreach (var id in mining.OreIdsOnFloor(f)) if (ItemRegistry.Exists(id)) ids.Add(id);
            }
            if (ids.Count == 0) foreach (var d in ItemRegistry.ByCategory(ItemCategory.Ore)) if (d.Rarity <= 1) ids.Add(d.Id);
            if (ids.Count == 0) return;
            r.AddItem(new ItemStack(ids[rng.Range(0, ids.Count)], 1));
        }

        private ItemDefinition PickCheapItem(ItemCategory cat, SeededRandom rng)
        {
            var list = new List<ItemDefinition>();
            foreach (var d in ItemRegistry.ByCategory(cat)) if (d.Rarity <= 1 && !d.HasTag(ItemTags.NotSellable)) list.Add(d);
            if (list.Count == 0) return null;
            list.Sort((a, b) => a.BasePrice.CompareTo(b.BasePrice));
            int n = Mathf.Min(list.Count, 4);
            return list[rng.Range(0, n)];
        }

        private ItemDefinition PickCheapEdible(SeededRandom rng)
        {
            var list = new List<ItemDefinition>();
            foreach (var d in ItemRegistry.All) if (Resident.IsEdible(d) && d.Rarity <= 1 && d.Category != ItemCategory.Fish && !d.HasTag(ItemTags.NotSellable)) list.Add(d);
            if (list.Count == 0) return null;
            return list[rng.Range(0, list.Count)];
        }

        private ItemDefinition PickTagged(string tag, SeededRandom rng)
        {
            var list = new List<ItemDefinition>();
            foreach (var d in ItemRegistry.WithTag(tag)) if (d.Rarity <= 1 && !d.HasTag(ItemTags.NotSellable)) list.Add(d);
            if (list.Count == 0) return null;
            return list[rng.Range(0, list.Count)];
        }
    }
}
