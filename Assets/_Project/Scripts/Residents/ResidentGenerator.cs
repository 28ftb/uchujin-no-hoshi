using System;
using System.Collections.Generic;
using Uchujin.Core;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// 住民の決定的生成（ワールドシードから）。
    /// ・初期住民は「同じシードなら同じ顔ぶれ」になる（村仕様：初期住民の生成系列を独立させる）。
    /// ・新住民は、生成番号と「その時点で空いている必須職業」から作る（村の状態に依存するのでプロフィールを保存する）。
    /// </summary>
    public static class ResidentGenerator
    {
        /// <summary>初期住民の必須職業＋ストーリーの人物像（メインストーリー第3章の例）。</summary>
        private static readonly ResidentJob[] CoreInitialJobs =
        {
            ResidentJob.Courier,        // 案内役（まんまるな宇宙人）
            ResidentJob.Mechanic,       // 四本腕の修理屋
            ResidentJob.Blacksmith,
            ResidentJob.SeedSeller,
            ResidentJob.FishingMaster,
            ResidentJob.Carpenter,
            ResidentJob.Merchant,
            ResidentJob.WeatherReader,
            ResidentJob.Cook,           // いつも食べ物をくれる
            ResidentJob.Traveler,       // 旅に憧れる小さな宇宙人
            ResidentJob.Florist         // 花を育てる年配の宇宙人
        };

        private static readonly ResidentJob[] ExtraJobPool =
        {
            ResidentJob.FarmingMaster, ResidentJob.CreatureExpert, ResidentJob.Musician, ResidentJob.Child,
            ResidentJob.Miner, ResidentJob.Artist, ResidentJob.Scholar, ResidentJob.Retired, ResidentJob.Doctor,
            ResidentJob.Teacher, ResidentJob.Builder
        };

        private static readonly string[] LikeTagPool =
        {
            ItemTags.Sweet, ItemTags.Spicy, ItemTags.Sour, ItemTags.Savory, ItemTags.Glowing, ItemTags.Cute, ItemTags.Shiny,
            ItemTags.Fragrant, ItemTags.Flower, ItemTags.Fruit, ItemTags.Vegetable, ItemTags.Fish, ItemTags.Gem, ItemTags.Music,
            ItemTags.Toy, ItemTags.Light, ItemTags.Plant, ItemTags.Big, ItemTags.Small, ItemTags.Rare, ItemTags.Crystal,
            ItemTags.Seating, ItemTags.Aquarium, ItemTags.Drink, ItemTags.Grain, ItemTags.Outdoor
        };

        private static readonly string[] ColorTags =
        {
            ItemTags.ColorRed, ItemTags.ColorBlue, ItemTags.ColorGreen, ItemTags.ColorYellow, ItemTags.ColorWhite,
            ItemTags.ColorBlack, ItemTags.ColorPurple, ItemTags.ColorOrange, ItemTags.ColorPink
        };

        public static string MakeId(ulong seed, string genKey) => "r" + Ids.Deterministic(seed, "resident." + genKey);

        /// <summary>初期住民をまとめて生成する。案内役（Round の初期住民のうち Id 順で最初の1人）が Courier になるよう系統を割り振る。</summary>
        public static List<ResidentProfile> GenerateInitial(ulong seed, int count)
        {
            var rng = SeededRandom.For(seed, "residents.initial");
            count = Mathf.Max(1, count);

            // ---- 職業 ----
            var jobs = new List<ResidentJob>();
            for (int i = 0; i < CoreInitialJobs.Length && jobs.Count < count; i++) jobs.Add(CoreInitialJobs[i]);
            var extra = new List<ResidentJob>(ExtraJobPool);
            rng.Shuffle(extra);
            for (int i = 0; jobs.Count < count; i++) jobs.Add(extra[i % extra.Count]);

            // ---- ID ----
            var list = new List<ResidentProfile>();
            for (int i = 0; i < count; i++)
            {
                string key = "initial:" + i;
                list.Add(new ResidentProfile { id = MakeId(seed, key), genKey = key, originalJob = jobs[i] });
            }

            // ---- デザイン（100人の中から職業に合う住民を選ぶ。系統はデザインで決まる） ----
            // 案内役は「まんまる」で道案内の住民、修理屋は「多腕」の住民（メインストーリー第1・3章）。
            // 案内役以外の Round は、案内役より Id が後ろの住民にだけ割り当てる（案内役の判定：Round の初期住民のうち Id 順で最初の1人）。
            var guide = list[0];
            var drng = SeededRandom.For(seed, "residents.designs");
            var used = new HashSet<string>();
            var lineageCount = new Dictionary<AlienLineage, int>();
            foreach (AlienLineage l in Enum.GetValues(typeof(AlienLineage))) lineageCount[l] = 0;
            foreach (var p in list)
            {
                ResidentDesign d;
                if (p == guide) d = PickDesign(drng, p.originalJob, used, lineageCount, x => x.Lineage == AlienLineage.Round);
                else if (p.originalJob == ResidentJob.Mechanic) d = PickDesign(drng, p.originalJob, used, lineageCount, x => x.Lineage == AlienLineage.MultiArm);
                else
                {
                    var self = p;
                    d = PickDesign(drng, p.originalJob, used, lineageCount,
                        x => x.Lineage != AlienLineage.Round || string.CompareOrdinal(self.id, guide.id) > 0);
                }
                AssignDesign(p, d, used, lineageCount);
                if (d == null) p.lineage = p == guide ? AlienLineage.Round : p.originalJob == ResidentJob.Mechanic ? AlienLineage.MultiArm : AlienLineage.Expressive;
            }

            // ---- 個性 ----
            foreach (var p in list)
            {
                var r = SeededRandom.For(seed, "residents.profile." + p.genKey);
                FillProfile(p, r, p == guide);
            }

            // ---- 家族（子どもと大人、年配と若者）【提案】 ----
            ResidentProfile child = list.Find(x => x.age == ResidentAge.Child && x.originalJob == ResidentJob.Child)
                                    ?? list.Find(x => x.originalJob == ResidentJob.Traveler);
            if (child != null)
            {
                var candidates = list.FindAll(x => x != child && x != guide && x.age == ResidentAge.Adult && string.IsNullOrEmpty(x.familyId));
                if (candidates.Count > 0)
                {
                    var parent = candidates[rng.Range(0, candidates.Count)];
                    child.familyId = parent.id;
                    parent.familyId = child.id;
                }
            }
            if (rng.Chance(0.5f))
            {
                var elder = list.Find(x => x.age == ResidentAge.Elder && string.IsNullOrEmpty(x.familyId));
                var young = list.FindAll(x => x.age != ResidentAge.Elder && x != guide && string.IsNullOrEmpty(x.familyId));
                if (elder != null && young.Count > 0)
                {
                    var y = young[rng.Range(0, young.Count)];
                    elder.familyId = y.id;
                    y.familyId = elder.id;
                }
            }
            return list;
        }

        /// <summary>新住民を1人生成する（引っ越し：住民生活仕様 §11）。</summary>
        public static ResidentProfile GenerateNewcomer(ulong seed, int counter, ResidentJob requiredJob, IList<Resident> current)
        {
            string key = "newcomer:" + counter;
            var rng = SeededRandom.For(seed, "residents." + key);
            var p = new ResidentProfile { id = MakeId(seed, key), genKey = key };

            // 職業：空いている必須職業があればそれ、無ければ村にいない職業を優先
            if (requiredJob != ResidentJob.None) p.originalJob = requiredJob;
            else
            {
                var present = new HashSet<ResidentJob>();
                foreach (var r in current) if (r.State.active) present.Add(r.State.job);
                var pool = new List<ResidentJob>();
                foreach (var j in ExtraJobPool) if (!present.Contains(j)) pool.Add(j);
                if (pool.Count == 0) pool.AddRange(ExtraJobPool);
                pool.Add(ResidentJob.Cook);
                pool.Add(ResidentJob.FishingMaster);
                pool.Add(ResidentJob.Traveler);
                p.originalJob = pool[rng.Range(0, pool.Count)];
            }

            // デザイン：この星にまだ来ていない住民から、職業に合う住民を選ぶ。少ない系統ほど来やすい（顔ぶれの変化を大きく）。
            // 引っ越して行った住民も選ばない（手紙や再訪で同じ顔が2人になるのを避ける）。
            var counts = new Dictionary<AlienLineage, int>();
            foreach (AlienLineage l in Enum.GetValues(typeof(AlienLineage))) counts[l] = 0;
            var used = new HashSet<string>();
            foreach (var r in current)
            {
                if (!string.IsNullOrEmpty(r.Profile.designId)) used.Add(r.Profile.designId);
                if (r.State.active) counts[r.Profile.lineage]++;
            }
            var d = PickDesign(SeededRandom.For(seed, "residents.design." + key), p.originalJob, used, counts, null);
            AssignDesign(p, d, used, counts);
            if (d == null)
            {
                // 100人全員がこの星に来た後：従来の生成（プリミティブの見た目）
                var lineages = new List<AlienLineage>(counts.Keys);
                p.lineage = rng.WeightedPick(lineages, l => 1f / (1f + counts[l] * 1.5f));
                if (p.originalJob == ResidentJob.Mechanic && rng.Chance(0.6f)) p.lineage = AlienLineage.MultiArm;
            }

            FillProfile(p, rng, false);
            return p;
        }

        /// <summary>
        /// 職業に合うデザインを選ぶ。第一の職業が合う住民 → 代わりの職業が合う住民 → だれでも、の順に探し、
        /// 同じ段の中では村に少ない系統ほど選ばれやすくする。見つからなければ null。
        /// </summary>
        private static ResidentDesign PickDesign(SeededRandom r, ResidentJob job, HashSet<string> used,
            Dictionary<AlienLineage, int> lineageCount, Func<ResidentDesign, bool> allow)
        {
            for (int tier = 0; tier < 3; tier++)
            {
                var candidates = new List<ResidentDesign>();
                foreach (var d in ResidentDesigns.All)
                {
                    if (used.Contains(d.Id) || (allow != null && !allow(d))) continue;
                    bool ok = tier == 0 ? d.Job == job : tier == 1 ? d.CanDo(job) : true;
                    if (ok) candidates.Add(d);
                }
                if (candidates.Count > 0)
                    return r.WeightedPick(candidates, d => 1f / (1f + lineageCount[d.Lineage] * 1.5f));
            }
            return null;
        }

        private static void AssignDesign(ResidentProfile p, ResidentDesign d, HashSet<string> used, Dictionary<AlienLineage, int> lineageCount)
        {
            if (d == null) return;
            p.designId = d.Id;
            p.lineage = d.Lineage;
            used.Add(d.Id);
            lineageCount[d.Lineage]++;
        }

        // ------------------------------------------------------------

        private static void FillProfile(ResidentProfile p, SeededRandom r, bool isGuide)
        {
            var job = p.originalJob;

            // ---- 年齢 ----
            if (job == ResidentJob.Traveler || job == ResidentJob.Child) p.age = ResidentAge.Child;
            else if (job == ResidentJob.Florist || job == ResidentJob.Retired) p.age = ResidentAge.Elder;
            else p.age = r.Chance(0.15f) ? ResidentAge.Elder : ResidentAge.Adult;

            // ---- 性格 ----
            var q = p.personality;
            q.sociability = r.NextFloat();
            q.curiosity = r.NextFloat();
            q.easygoing = r.NextFloat();
            q.tidiness = r.NextFloat();
            q.competitive = r.NextFloat();
            q.shyness = r.NextFloat() * 0.9f;
            q.thrift = r.NextFloat();
            q.gluttony = r.NextFloat();
            q.generosity = r.NextFloat();
            q.talkativeness = r.NextFloat();
            q.mischief = r.NextFloat() * 0.8f;
            q.nightOwl = r.Chance(0.2f) ? r.Range(0.7f, 1f) : r.Range(0f, 0.5f);
            q.rainLike = r.Range(-1f, 1f);

            switch (job)
            {
                case ResidentJob.Cook: q.generosity = r.Range(0.85f, 1f); q.gluttony = r.Range(0.6f, 1f); break;
                case ResidentJob.Traveler: q.curiosity = r.Range(0.85f, 1f); q.sociability = r.Range(0.5f, 1f); break;
                case ResidentJob.Florist: q.easygoing = r.Range(0.7f, 1f); q.tidiness = r.Range(0.5f, 1f); break;
                case ResidentJob.Mechanic: q.curiosity = r.Range(0.8f, 1f); q.talkativeness = r.Range(0.6f, 1f); break;
                case ResidentJob.Merchant: q.sociability = r.Range(0.6f, 1f); q.thrift = r.Range(0.5f, 1f); break;
                case ResidentJob.Child: q.mischief = r.Range(0.5f, 1f); q.shyness *= 0.6f; break;
                case ResidentJob.WeatherReader: q.nightOwl = r.Range(0f, 0.3f); break; // 早起きして空を読む
                case ResidentJob.Musician: q.nightOwl = Mathf.Max(q.nightOwl, r.Range(0.5f, 1f)); break;
            }
            if (isGuide)
            {
                // 主人公の世話を焼く、人なつっこいまんまるな宇宙人（メインストーリー第1章）
                q.sociability = r.Range(0.85f, 1f);
                q.generosity = r.Range(0.75f, 1f);
                q.shyness = r.Range(0f, 0.2f);
                q.curiosity = r.Range(0.6f, 1f);
            }

            // ---- 趣味 ----
            p.hobby = PickHobby(job, r);
            var hobbies = (ResidentHobby[])Enum.GetValues(typeof(ResidentHobby));
            do p.hobby2 = hobbies[r.Range(0, hobbies.Length)]; while (p.hobby2 == p.hobby);

            // ---- 好み（タグ・色・カテゴリ） ----
            var pool = new List<string>(LikeTagPool);
            r.Shuffle(pool);
            int likeCount = r.Range(3, 6);
            for (int i = 0; i < likeCount && i < pool.Count; i++) p.likes.Add(new TagWeight(pool[i], r.Range(0.2f, 0.6f)));
            int dislikeCount = r.Range(1, 3);
            for (int i = 0; i < dislikeCount && likeCount + i < pool.Count; i++) p.likes.Add(new TagWeight(pool[likeCount + i], -r.Range(0.2f, 0.5f)));
            p.favoriteColorTag = ColorTags[r.Range(0, ColorTags.Length)];
            p.dislikedTag = pool[pool.Count - 1];
            p.categoryLikes.Add(new TagWeight(ItemCategory.Food.ToString(), r.Range(0f, 0.3f)));
            p.categoryLikes.Add(new TagWeight(ItemCategory.Furniture.ToString(), r.Range(-0.1f, 0.3f)));
            p.categoryLikes.Add(new TagWeight(ItemCategory.Gem.ToString(), r.Range(-0.1f, 0.35f)));
            p.categoryLikes.Add(new TagWeight(ItemCategory.Fish.ToString(), r.Range(-0.2f, 0.3f)));
            p.categoryLikes.Add(new TagWeight(ItemCategory.Crop.ToString(), r.Range(-0.1f, 0.3f)));
            p.categoryLikes.Add(new TagWeight(ItemCategory.Ore.ToString(), r.Range(-0.3f, 0.15f)));
            if (job == ResidentJob.Florist) p.likes.Add(new TagWeight(ItemTags.Flower, 0.5f));
            if (job == ResidentJob.Cook) p.likes.Add(new TagWeight(ItemTags.Ingredient, 0.3f));
            if (job == ResidentJob.Traveler) p.likes.Add(new TagWeight(ItemTags.RocketMaterial, 0.4f));

            // ---- 夢（長期目標） ----
            switch (job)
            {
                case ResidentJob.Traveler: p.dream = GoalKind.TravelStars; break;
                case ResidentJob.Mechanic: p.dream = GoalKind.BuildSpaceship; break;
                case ResidentJob.Blacksmith: p.dream = GoalKind.BestCraftsman; break;
                case ResidentJob.Cook: p.dream = GoalKind.OwnShop; break;
                case ResidentJob.Florist: p.dream = GoalKind.QuietLife; break;
                default:
                    var dreams = new[] { GoalKind.OwnShop, GoalKind.BestCraftsman, GoalKind.FamilyReunion, GoalKind.QuietLife, GoalKind.QuietLife, GoalKind.TravelStars };
                    p.dream = dreams[r.Range(0, dreams.Length)];
                    break;
            }
            if (isGuide) p.dream = GoalKind.QuietLife;

            // ---- 生活時間 ----
            bool night = q.nightOwl > 0.65f;
            p.wakeMinute = night ? r.Range(9 * 60, 10 * 60 + 30) : r.Range(5 * 60 + 30, 7 * 60 + 30);
            if (job == ResidentJob.WeatherReader) p.wakeMinute = r.Range(5 * 60, 5 * 60 + 40);
            p.sleepMinute = night ? r.Range(23 * 60 + 30, 24 * 60) : r.Range(21 * 60, 22 * 60 + 40);
            if (p.age == ResidentAge.Child) p.sleepMinute = Mathf.Min(p.sleepMinute, 21 * 60);
            p.workStartMinute = Mathf.Max(p.wakeMinute + 60, r.Range(8 * 60 + 30, 10 * 60));
            p.workEndMinute = r.Range(16 * 60, 18 * 60);
            if (job == ResidentJob.FishingMaster) { p.workStartMinute = Mathf.Max(p.wakeMinute + 30, 6 * 60); p.workEndMinute = 14 * 60; }
            if (job == ResidentJob.Musician) { p.workStartMinute = 14 * 60; p.workEndMinute = 21 * 60; }
            p.birthdayIndex = r.Range(0, ResidentTuning.BirthdayCycleDays);

            // ---- 外見 ----
            p.appearance = MakeAppearance(p.lineage, p.age, job, r);
            var design = ResidentDesigns.Get(p.designId);
            if (design != null)
            {
                // 見た目はモデルで決まる。色は吹き出し・顔アイコン・引っ越しの箱などに使う。
                var a = p.appearance;
                a.bodyColor = design.MainColor;
                a.accentColor = design.AccentColor;
                // size 1 ≒ 以前のプリミティブの住民（約1.2 m）。当たり判定・仕事道具の大きさに使う
                a.size = Mathf.Clamp(design.Height * Uchujin.Core.Visual.ResidentModelBuilder.GameScale / 1.2f, 0.5f, 1.4f);
                a.floatHeight = 0f; // 浮遊する住民はモデル自体が浮いた位置で作られている
            }
        }

        private static ResidentHobby PickHobby(ResidentJob job, SeededRandom r)
        {
            switch (job)
            {
                case ResidentJob.Florist: return ResidentHobby.Gardening;
                case ResidentJob.Cook: return ResidentHobby.Cooking;
                case ResidentJob.FishingMaster: return ResidentHobby.Fishing;
                case ResidentJob.Traveler: return r.Chance(0.5f) ? ResidentHobby.Stargazing : ResidentHobby.Walking;
                case ResidentJob.CreatureExpert: return ResidentHobby.CreatureWatching;
                case ResidentJob.Musician: return ResidentHobby.Music;
                case ResidentJob.Miner: return ResidentHobby.Collecting;
                case ResidentJob.Carpenter: return ResidentHobby.Decorating;
            }
            var hobbies = (ResidentHobby[])Enum.GetValues(typeof(ResidentHobby));
            return hobbies[r.Range(0, hobbies.Length)];
        }

        /// <summary>系統ごとの見た目（キャラデザ §2 の各テーマ）＋個体差（色・大きさ・付属品）。</summary>
        public static ResidentAppearance MakeAppearance(AlienLineage lineage, ResidentAge age, ResidentJob job, SeededRandom r)
        {
            var a = new ResidentAppearance { lineage = lineage };
            float h = r.NextFloat();
            a.bodyColor = Color.HSVToRGB(h, r.Range(0.25f, 0.6f), r.Range(0.85f, 1f));
            a.accentColor = Color.HSVToRGB((h + r.Range(0.3f, 0.7f)) % 1f, r.Range(0.35f, 0.7f), r.Range(0.8f, 1f));
            a.patternColor = Color.HSVToRGB((h + 0.5f) % 1f, r.Range(0.1f, 0.35f), 1f);
            a.accessoryColor = Color.HSVToRGB(r.NextFloat(), r.Range(0.4f, 0.8f), r.Range(0.6f, 0.95f));
            a.eyeColor = Color.HSVToRGB(r.NextFloat(), r.Range(0.2f, 0.6f), r.Range(0.1f, 0.3f));
            a.size = r.Range(0.85f, 1.1f);
            a.headScale = r.Range(0.9f, 1.1f);
            a.squash = 1f;
            a.eyeScale = r.Range(0.9f, 1.15f);
            a.earType = r.Chance(0.3f) ? r.Range(1, 5) : 0;
            a.antennaType = r.Chance(0.3f) ? r.Range(1, 3) : 0;
            a.pattern = r.Chance(0.35f) ? r.Range(1, 3) : 0;
            a.accessory = r.Chance(0.25f) ? r.Range(1, 7) : 0;
            a.secondary = lineage;
            ApplyLineage(a, lineage, r);
            if (lineage == AlienLineage.Mixed)
            {
                // ミックス：2つのテーマを掛け合わせる（丸型＋多腕、ふわふわ＋浮遊、四足＋天体…）
                var choices = new[] { AlienLineage.Round, AlienLineage.Mochi, AlienLineage.Plush, AlienLineage.ChibiAnimal, AlienLineage.Floating, AlienLineage.MultiArm, AlienLineage.StarChild, AlienLineage.Expressive };
                var first = choices[r.Range(0, choices.Length)];
                AlienLineage second;
                do second = choices[r.Range(0, choices.Length)]; while (second == first);
                a.secondary = second;
                ApplyLineage(a, first, r);
                ApplyLineage(a, second, r);
                a.lineage = AlienLineage.Mixed;
                a.secondary = first;
                // 保存用：主たる形を secondary に、2つ目の特徴は上で反映済み
            }
            if (age == ResidentAge.Child) a.size *= 0.72f;
            if (age == ResidentAge.Elder)
            {
                a.size *= 0.95f;
                if (a.accessory == 0 && r.Chance(0.6f)) a.accessory = 5; // 眼鏡
                a.bodyColor = Color.Lerp(a.bodyColor, Color.white, 0.2f);
            }
            if (job == ResidentJob.Cook && a.accessory == 0) a.accessory = 2;      // エプロン
            if (job == ResidentJob.Merchant && a.accessory == 0) a.accessory = 3;  // かばん
            if (job == ResidentJob.Traveler && a.accessory == 0) a.accessory = 4;  // マフラー
            return a;
        }

        private static void ApplyLineage(ResidentAppearance a, AlienLineage l, SeededRandom r)
        {
            switch (l)
            {
                case AlienLineage.Round:
                    a.squash = r.Range(0.95f, 1.1f);
                    a.headScale = r.Range(1.0f, 1.15f);
                    if (r.Chance(0.5f)) a.antennaType = 1;
                    break;
                case AlienLineage.Mochi:
                    a.translucent = true;
                    a.squash = r.Range(0.65f, 0.85f);
                    a.bodyColor.a = 0.85f;
                    a.earType = 0;
                    a.pattern = r.Chance(0.6f) ? 1 : 0;
                    break;
                case AlienLineage.Plush:
                    a.earType = r.Chance(0.5f) ? 2 : 4;
                    a.tailType = 3;
                    a.bodyColor = Color.Lerp(a.bodyColor, Color.white, 0.25f);
                    break;
                case AlienLineage.ChibiAnimal:
                    a.fourLegged = r.Chance(0.55f);
                    a.earType = r.Chance(0.5f) ? 2 : 3;
                    a.tailType = r.Chance(0.5f) ? 2 : 1;
                    break;
                case AlienLineage.Floating:
                    a.floatHeight = r.Range(0.35f, 0.75f);
                    a.fourLegged = false;
                    if (r.Chance(0.4f)) a.translucent = true;
                    break;
                case AlienLineage.MultiArm:
                    a.armCount = 4;
                    a.size = Mathf.Max(a.size, r.Range(1.0f, 1.15f));
                    break;
                case AlienLineage.StarChild:
                    a.glows = true;
                    a.antennaType = r.Chance(0.35f) ? 3 : 0;
                    a.pattern = 3;
                    break;
                case AlienLineage.Lifestyle:
                    if (a.accessory == 0) a.accessory = r.Range(1, 7);
                    break;
                case AlienLineage.Expressive:
                    a.eyeScale = r.Range(1.45f, 1.8f);
                    a.headScale = r.Range(1.15f, 1.3f);
                    if (r.Chance(0.6f)) a.antennaType = 1;
                    break;
            }
        }
    }
}
