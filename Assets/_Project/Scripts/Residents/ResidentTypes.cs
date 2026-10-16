using System.Collections.Generic;
using Uchujin.Core;
using Uchujin.World;
using UnityEngine;

namespace Uchujin.Residents
{
    // =====================================================================
    // 住民モジュールの列挙型・調整用の仮数値・職業テーブル。
    // 数値はすべて【仮数値】。住民生活仕様 §21・バランス仕様 §7/§12 で【未決定】の項目を、
    // 遊びとして成立する値で仮置きしている。調整はこのファイルだけで行えるようにまとめる。
    // =====================================================================

    /// <summary>趣味（住民生活仕様 §1 の「趣味」）。仕事と独立に持つ。</summary>
    public enum ResidentHobby
    {
        Fishing,
        Gardening,
        Cooking,
        CreatureWatching,
        Collecting,
        Mining,
        Music,
        Walking,
        Stargazing,
        Crafting,
        Decorating,
        Reading
    }

    /// <summary>年齢層（見た目・歩く速さ・話題に反映。ストーリーの「小さな」「年配の」人物像）。</summary>
    public enum ResidentAge
    {
        Child,
        Adult,
        Elder
    }

    /// <summary>住民同士の関係の種類（住民生活仕様 §5）。恋は控えめに「Sweetheart」だけ。</summary>
    public enum RelationKind
    {
        Acquaintance,
        Friend,
        BestFriend,
        Family,
        Coworker,
        Rival,
        Dislike,
        Sweetheart
    }

    /// <summary>引っ越しの理由（住民生活仕様 §11 / メインストーリー第4章）。</summary>
    public enum MoveReason
    {
        None,
        Dream,       // 夢を叶えるため（修行・挑戦・旅）
        Family,      // 家族のもとへ
        OtherPlace,  // 別の場所で暮らしてみたい（好奇心・気分転換）
        Work,        // 別の村で仕事が見つかった
        Friend       // 親しい住民の近くへ
    }

    /// <summary>中期・長期目標の種類（住民生活仕様 §3）。</summary>
    public enum GoalKind
    {
        None,
        BuyFurniture,     // 家具を買う（お金を貯める）
        NewRecipe,        // 新しい料理（材料を集める）
        RaiseCreature,    // 生き物を迎える・観察する
        BigFish,          // 大物を釣る
        Masterpiece,      // 最高傑作を作る（鍛冶屋・職人）
        DecorateHome,     // 家を飾る（花・小物）
        SaveMoney,        // 貯金
        LearnSkill,       // 弟子入り・技を覚える
        // ---- 長期（夢） ----
        TravelStars,      // ほかの星へ行く（旅に憧れる住民）
        OwnShop,          // 自分の店を持つ
        BuildSpaceship,   // 宇宙船を作る（修理屋の夢）
        BestCraftsman,    // 一流の職人になる
        FamilyReunion,    // 家族と暮らす
        QuietLife         // この村で穏やかに暮らす（転出しにくい）
    }

    /// <summary>【仮数値】住民の調整値まとめ。</summary>
    public static class ResidentTuning
    {
        // ---- 人数・引っ越し（住民生活仕様 §18、バランス §12.2） ----
        /// <summary>初期住民数（仮。住宅区画が足りなければ区画数-1 まで減らす）。</summary>
        public const int InitialResidentCount = 10;
        /// <summary>初期住民の後、引っ越して来て増える目標人数（初期＋この数まで増える）。</summary>
        public const int PopulationGrowth = 5; // 初期10人＋5人＝最大15人まで増える
        /// <summary>同時居住の上限。</summary>
        public const int MaxResidents = 15;
        /// <summary>最初の新住民が引っ越してくる日（第2章「村の新しい住民」を序盤に1回起こす）。</summary>
        public const int FirstNewcomerDay = 4;
        /// <summary>空きができてから新住民が来るまでの日数の範囲。</summary>
        public const int NewcomerDelayMin = 2;
        public const int NewcomerDelayMax = 5;
        /// <summary>この日より前は誰も引っ越しを決めない（顔を覚える前に去らない）。</summary>
        public const int EarliestMoveOutDecisionDay = 12;
        /// <summary>この日までに一度も転出が無ければ、条件の合う住民が引っ越しを考え始める（第4章）。</summary>
        public const int Chapter4NudgeDay = 22;
        /// <summary>入居してから転出を考えられるようになるまでの最短日数。</summary>
        public const int MinStayDays = 14;
        /// <summary>転出と転出の最短間隔（日）。</summary>
        public const int MoveOutInterval = 8;
        /// <summary>引っ越しを決めてから出ていくまでの日数（兆候：伝言・荷造り・友達が集まる）。</summary>
        public const int MoveOutNoticeDays = 3;
        /// <summary>1日あたりの転出検討確率の基準（長期目標の進み具合で上がる）。</summary>
        public const float MoveOutBaseChance = 0.02f;
        /// <summary>主人公と親しいと転出しにくくなる係数（親しさ100で確率×(1-この値)）。</summary>
        public const float MoveOutFriendshipDamp = 0.6f;
        /// <summary>引き留め（首を振る）が効く親しさ。</summary>
        public const int PersuadeFriendship = 55;
        /// <summary>転出後に手紙が届くまでの日数。</summary>
        public const int LetterDelayMin = 3;
        public const int LetterDelayMax = 7;
        /// <summary>手紙をくれる親しさ。</summary>
        public const int LetterFriendship = 30;
        /// <summary>引っ越した住民が遊びに戻ってくる確率（1日あたり、転出10日以上後）。</summary>
        public const float FormerVisitChance = 0.015f;

        // ---- お金（バランス §7.6。住民の財布と経済循環） ----
        public const int StartMoneyMin = 300;
        public const int StartMoneyMax = 900;
        /// <summary>仕事の無い日・無職の最低収入（年金・お小遣い・内職）。</summary>
        public const int BaseDailyIncome = 25;
        /// <summary>買い物1回に使う予算の割合（節約家ほど小さい）。</summary>
        public const float BudgetFractionMin = 0.12f;
        public const float BudgetFractionMax = 0.45f;
        /// <summary>主人公の商店へ行く確率（村の店との比較。バランス §7.4 の 60% を仮置き）。</summary>
        public const float PlayerShopPreference = 0.6f;

        // ---- 欲求 ----
        /// <summary>空腹が1時間あたり増える量。</summary>
        public const float HungerPerHour = 0.06f;
        public const float HungryThreshold = 0.55f;

        // ---- 主人公との親交（住民生活仕様 §6） ----
        public const int FriendshipTalkDaily = 2;
        public const int FriendshipFirstMeet = 3;
        public const int FriendshipGiftLoved = 10;
        public const int FriendshipGiftLiked = 6;
        public const int FriendshipGiftNeutral = 2;
        public const int FriendshipGiftDisliked = -3;
        public const int FriendshipGestureDaily = 1;
        public const int FriendshipSharedActivity = 2;
        /// <summary>同じ日に2個目以降の贈り物の効果倍率（連打だけが最適にならない：バランス §12.1）。</summary>
        public const float RepeatGiftFactor = 0.35f;
        /// <summary>遠くから手を振ってくれる親しさ。</summary>
        public const int WaveFromAfarFriendship = 30;
        /// <summary>食べ物・プレゼントをくれる親しさ。</summary>
        public const int OfferGiftFriendship = 45;
        /// <summary>夢を打ち明けてくれる親しさ。</summary>
        public const int ConfideFriendship = 60;
        /// <summary>主人公を「変な生き物」ではなく呼び名で呼ぶようになる親しさ。</summary>
        public const int NameCallFriendship = 25;

        // ---- 記憶 ----
        public const int MaxMemories = 36;
        public const int MemoryForgetDays = 40;
        public const float WitnessRadius = 22f;

        // ---- 動き・知覚 ----
        public const float WalkSpeed = 2.1f;
        public const float ChildSpeedFactor = 1.15f;
        public const float ElderSpeedFactor = 0.75f;
        public const float ArriveDistance = 0.6f;
        public const float ChatDistance = 3.5f;
        public const float HearChatRadius = 18f;
        public const float SelfTalkRadius = 9f;
        /// <summary>ホストが位置・行動など軽い状態（副キー "residents.live"）を送る間隔（実時間秒）。</summary>
        public const float LiveSyncInterval = 0.5f;
        /// <summary>全体状態（記憶・関係・予定を含む SaveKey "residents"）を送る最短間隔（実時間秒）。変化があった時だけ送る。</summary>
        public const float FullSyncMinInterval = 10f;
        /// <summary>ゲストへ再生させる表情・身振りを覚えておく数。</summary>
        public const int LiveCueBuffer = 16;
        /// <summary>この分以上まとめて時間が進んだら（睡眠など）住民を目的地へ瞬間移動させる。</summary>
        public const int SnapMinutes = 20;

        // ---- 会話の頻度（実時間秒） ----
        public const float ChatLineIntervalMin = 7f;
        public const float ChatLineIntervalMax = 13f;
        public const float SelfTalkInterval = 28f;
        public const float WaveCooldown = 90f;

        // ---- 誕生日（時間・天候仕様 §15【提案】。暦が未決定のため仮の周期） ----
        public const int BirthdayCycleDays = 28;
    }

    /// <summary>職業ごとの職場・収入・必須サービスかどうか。</summary>
    public static class JobTable
    {
        /// <summary>引っ越しで消えてはならない必須サービス職（バランス §12.2、鉱山 §4.3）。</summary>
        public static readonly ResidentJob[] EssentialJobs =
        {
            ResidentJob.Blacksmith,
            ResidentJob.Mechanic,
            ResidentJob.SeedSeller,
            ResidentJob.FishingMaster,
            ResidentJob.Carpenter,
            ResidentJob.Merchant,
            ResidentJob.WeatherReader
        };

        public static bool IsEssential(ResidentJob job)
        {
            for (int i = 0; i < EssentialJobs.Length; i++) if (EssentialJobs[i] == job) return true;
            return false;
        }

        /// <summary>職場の施設（無ければ null。職場が無い職業は村の中を回る）。</summary>
        public static FacilityType? Workplace(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return FacilityType.Blacksmith;
                case ResidentJob.Mechanic: return FacilityType.RepairWorkshop;
                case ResidentJob.SeedSeller: return FacilityType.SeedShop;
                case ResidentJob.FishingMaster: return FacilityType.FishingHut;
                case ResidentJob.Carpenter: return FacilityType.CarpenterWorkshop;
                case ResidentJob.Builder: return FacilityType.CarpenterWorkshop;
                case ResidentJob.Merchant: return FacilityType.GeneralStore;
                case ResidentJob.Cook: return FacilityType.CommunityKitchen;
                case ResidentJob.WeatherReader: return FacilityType.Lookout;
                case ResidentJob.Courier: return FacilityType.MessagePlaza;
                case ResidentJob.Miner: return FacilityType.MineEntrance;
                case ResidentJob.Musician: return FacilityType.Plaza;
                case ResidentJob.Teacher: return FacilityType.Plaza;
                case ResidentJob.Florist: return FacilityType.Plaza;
                default: return null;
            }
        }

        /// <summary>仕事中の行動。</summary>
        public static ResidentActivity WorkActivity(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.FishingMaster: return ResidentActivity.Fishing;
                case ResidentJob.FarmingMaster: return ResidentActivity.Farming;
                case ResidentJob.Florist: return ResidentActivity.Gardening;
                case ResidentJob.Cook: return ResidentActivity.Cooking;
                case ResidentJob.WeatherReader: return ResidentActivity.WatchingSky;
                case ResidentJob.CreatureExpert: return ResidentActivity.CatchingCreatures;
                case ResidentJob.Miner: return ResidentActivity.Mining;
                case ResidentJob.Blacksmith:
                case ResidentJob.Carpenter:
                case ResidentJob.Builder:
                case ResidentJob.Artist:
                    return ResidentActivity.Crafting;
                case ResidentJob.Musician: return ResidentActivity.Playing;
                case ResidentJob.Child: return ResidentActivity.Playing;
                case ResidentJob.Retired: return ResidentActivity.Resting;
                case ResidentJob.Traveler: return ResidentActivity.Exploring;
                case ResidentJob.Scholar: return ResidentActivity.Exploring;
                default: return ResidentActivity.Working;
            }
        }

        /// <summary>仕事のある日の収入【仮数値】。</summary>
        public static int DailyWage(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return 150;
                case ResidentJob.Mechanic: return 160;
                case ResidentJob.SeedSeller: return 110;
                case ResidentJob.FishingMaster: return 120;
                case ResidentJob.FarmingMaster: return 110;
                case ResidentJob.CreatureExpert: return 100;
                case ResidentJob.Carpenter: return 140;
                case ResidentJob.Cook: return 120;
                case ResidentJob.Merchant: return 130;
                case ResidentJob.WeatherReader: return 90;
                case ResidentJob.Florist: return 80;
                case ResidentJob.Courier: return 90;
                case ResidentJob.Miner: return 130;
                case ResidentJob.Builder: return 120;
                case ResidentJob.Artist: return 90;
                case ResidentJob.Musician: return 80;
                case ResidentJob.Scholar: return 100;
                case ResidentJob.Doctor: return 150;
                case ResidentJob.Teacher: return 110;
                case ResidentJob.Child: return 10;
                case ResidentJob.Retired: return 60;
                case ResidentJob.Traveler: return 50;
                default: return 60;
            }
        }

        /// <summary>職業に関係する好みのタグ（EvaluateItem の根拠の一部）。</summary>
        public static string[] JobTags(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return new[] { ItemTags.Ore, ItemTags.Metal, ItemTags.Heatproof };
                case ResidentJob.Mechanic: return new[] { ItemTags.Precision, ItemTags.RocketMaterial, ItemTags.Conductive, ItemTags.Sensor };
                case ResidentJob.SeedSeller: return new[] { ItemTags.Seed, ItemTags.Vegetable, ItemTags.Plant };
                case ResidentJob.FishingMaster: return new[] { ItemTags.Fish, ItemTags.Aquarium };
                case ResidentJob.FarmingMaster: return new[] { ItemTags.Vegetable, ItemTags.Fruit, ItemTags.Grain };
                case ResidentJob.CreatureExpert: return new[] { ItemTags.CreatureHabitat, ItemTags.Glowing };
                case ResidentJob.Carpenter:
                case ResidentJob.Builder: return new[] { ItemTags.Furniture, ItemTags.Seating, ItemTags.Table };
                case ResidentJob.Cook: return new[] { ItemTags.Ingredient, ItemTags.Food, ItemTags.Savory };
                case ResidentJob.Merchant: return new[] { ItemTags.Rare, ItemTags.Shiny };
                case ResidentJob.WeatherReader: return new[] { ItemTags.Crystal, ItemTags.Sensor };
                case ResidentJob.Florist: return new[] { ItemTags.Flower, ItemTags.Fragrant, ItemTags.Plant };
                case ResidentJob.Miner: return new[] { ItemTags.Ore, ItemTags.Gem, ItemTags.Crystal };
                case ResidentJob.Musician: return new[] { ItemTags.Music };
                case ResidentJob.Artist: return new[] { ItemTags.Cute, ItemTags.Shiny };
                case ResidentJob.Child: return new[] { ItemTags.Toy, ItemTags.Sweet, ItemTags.Cute };
                case ResidentJob.Traveler: return new[] { ItemTags.Rare, ItemTags.Glowing, ItemTags.RocketMaterial };
                case ResidentJob.Scholar: return new[] { ItemTags.Rare, ItemTags.Crystal };
                case ResidentJob.Doctor: return new[] { ItemTags.Fragrant, ItemTags.Plant };
                default: return new string[0];
            }
        }

        /// <summary>職業を表す概念（会話で「私は〜の仕事をする」）。</summary>
        public static string JobConcept(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return Concepts.PICKAXE;
                case ResidentJob.Mechanic: return Concepts.ROCKET;
                case ResidentJob.SeedSeller: return Concepts.SEED;
                case ResidentJob.FishingMaster: return Concepts.FISH;
                case ResidentJob.FarmingMaster: return Concepts.CROP;
                case ResidentJob.CreatureExpert: return Concepts.CREATURE;
                case ResidentJob.Carpenter:
                case ResidentJob.Builder: return Concepts.FURNITURE;
                case ResidentJob.Cook: return Concepts.DISH;
                case ResidentJob.Merchant: return Concepts.SHOP;
                case ResidentJob.WeatherReader: return Concepts.WEATHER;
                case ResidentJob.Florist: return Concepts.FLOWER;
                case ResidentJob.Courier: return Concepts.MESSAGE;
                case ResidentJob.Miner: return Concepts.ORE;
                case ResidentJob.Musician: return Concepts.MUSIC;
                case ResidentJob.Artist: return Concepts.PICTURE;
                default: return Concepts.JOB;
            }
        }

        /// <summary>仕事の動詞（会話用）。</summary>
        public static string JobVerb(ResidentJob job)
        {
            switch (job)
            {
                case ResidentJob.Blacksmith: return Concepts.MAKE;
                case ResidentJob.Mechanic: return Concepts.REPAIR;
                case ResidentJob.SeedSeller: return Concepts.SELL;
                case ResidentJob.Merchant: return Concepts.SELL;
                case ResidentJob.FishingMaster: return Concepts.CATCH_FISH;
                case ResidentJob.FarmingMaster: return Concepts.GROW;
                case ResidentJob.CreatureExpert: return Concepts.CATCH;
                case ResidentJob.Carpenter:
                case ResidentJob.Builder:
                case ResidentJob.Artist: return Concepts.MAKE;
                case ResidentJob.Cook: return Concepts.COOK;
                case ResidentJob.WeatherReader: return Concepts.SEE;
                case ResidentJob.Florist: return Concepts.GROW;
                case ResidentJob.Courier: return Concepts.CARRY;
                case ResidentJob.Miner: return Concepts.DIG;
                case ResidentJob.Musician: return Concepts.SING;
                case ResidentJob.Teacher: return Concepts.TEACH;
                case ResidentJob.Scholar: return Concepts.LEARN;
                case ResidentJob.Child: return Concepts.PLAY;
                default: return Concepts.WORK;
            }
        }

        public static string HobbyVerb(ResidentHobby h)
        {
            switch (h)
            {
                case ResidentHobby.Fishing: return Concepts.CATCH_FISH;
                case ResidentHobby.Gardening: return Concepts.GROW;
                case ResidentHobby.Cooking: return Concepts.COOK;
                case ResidentHobby.CreatureWatching: return Concepts.CATCH;
                case ResidentHobby.Collecting: return Concepts.FIND;
                case ResidentHobby.Mining: return Concepts.DIG;
                case ResidentHobby.Music: return Concepts.SING;
                case ResidentHobby.Walking: return Concepts.WALK;
                case ResidentHobby.Stargazing: return Concepts.SEE;
                case ResidentHobby.Crafting: return Concepts.MAKE;
                case ResidentHobby.Decorating: return Concepts.DECORATE;
                case ResidentHobby.Reading: return Concepts.READ;
                default: return Concepts.PLAY;
            }
        }

        public static string HobbyObject(ResidentHobby h)
        {
            switch (h)
            {
                case ResidentHobby.Fishing: return Concepts.FISH;
                case ResidentHobby.Gardening: return Concepts.FLOWER;
                case ResidentHobby.Cooking: return Concepts.DISH;
                case ResidentHobby.CreatureWatching: return Concepts.CREATURE;
                case ResidentHobby.Collecting: return Concepts.GEM;
                case ResidentHobby.Mining: return Concepts.ORE;
                case ResidentHobby.Music: return Concepts.MUSIC;
                case ResidentHobby.Stargazing: return Concepts.STAR;
                case ResidentHobby.Crafting: return Concepts.THING;
                case ResidentHobby.Decorating: return Concepts.HOUSE;
                case ResidentHobby.Reading: return Concepts.LETTER;
                default: return null;
            }
        }

        public static ResidentActivity HobbyActivity(ResidentHobby h)
        {
            switch (h)
            {
                case ResidentHobby.Fishing: return ResidentActivity.Fishing;
                case ResidentHobby.Gardening: return ResidentActivity.Gardening;
                case ResidentHobby.Cooking: return ResidentActivity.Cooking;
                case ResidentHobby.CreatureWatching: return ResidentActivity.CatchingCreatures;
                case ResidentHobby.Collecting: return ResidentActivity.Exploring;
                case ResidentHobby.Mining: return ResidentActivity.Mining;
                case ResidentHobby.Music: return ResidentActivity.Playing;
                case ResidentHobby.Walking: return ResidentActivity.Walking;
                case ResidentHobby.Stargazing: return ResidentActivity.WatchingSky;
                case ResidentHobby.Crafting: return ResidentActivity.Crafting;
                case ResidentHobby.Decorating: return ResidentActivity.AtHome;
                case ResidentHobby.Reading: return ResidentActivity.Resting;
                default: return ResidentActivity.Walking;
            }
        }

        public static string HobbyTag(ResidentHobby h)
        {
            switch (h)
            {
                case ResidentHobby.Fishing: return ItemTags.Fish;
                case ResidentHobby.Gardening: return ItemTags.Flower;
                case ResidentHobby.Cooking: return ItemTags.Ingredient;
                case ResidentHobby.CreatureWatching: return ItemTags.CreatureHabitat;
                case ResidentHobby.Collecting: return ItemTags.Rare;
                case ResidentHobby.Mining: return ItemTags.Gem;
                case ResidentHobby.Music: return ItemTags.Music;
                case ResidentHobby.Stargazing: return ItemTags.Glowing;
                case ResidentHobby.Crafting: return ItemTags.Metal;
                case ResidentHobby.Decorating: return ItemTags.Furniture;
                case ResidentHobby.Reading: return ItemTags.Light;
                default: return ItemTags.Outdoor;
            }
        }

        /// <summary>職業の日本語名（README・デバッグ表示用。プレイヤー向けUIには出さない）。</summary>
        public static string DebugName(ResidentJob job) => job.ToString();
    }

    /// <summary>系統の見た目の説明（会話ウィンドウの見出し。観察できる外見だけを書き、内部情報は書かない）。</summary>
    public static class LineageText
    {
        public static string Describe(AlienLineage l)
        {
            switch (l)
            {
                case AlienLineage.Round: return Loc.T("まんまるな宇宙人", "Round alien");
                case AlienLineage.Mochi: return Loc.T("もちもちの宇宙人", "Squishy alien");
                case AlienLineage.Plush: return Loc.T("ふわふわの宇宙人", "Fluffy alien");
                case AlienLineage.ChibiAnimal: return Loc.T("耳としっぽの宇宙人", "Eared alien");
                case AlienLineage.Floating: return Loc.T("ぷかぷか浮かぶ宇宙人", "Floating alien");
                case AlienLineage.MultiArm: return Loc.T("四本腕の宇宙人", "Four-armed alien");
                case AlienLineage.StarChild: return Loc.T("星の冠の宇宙人", "Star-crowned alien");
                case AlienLineage.Lifestyle: return Loc.T("小物を身につけた宇宙人", "Well-dressed alien");
                case AlienLineage.Expressive: return Loc.T("大きな目の宇宙人", "Big-eyed alien");
                default: return Loc.T("ふしぎな形の宇宙人", "Curious-shaped alien");
            }
        }

        public static string ExpressionText(Expression e)
        {
            switch (e)
            {
                case Expression.Happy: return Loc.T("にこにこしている", "smiling");
                case Expression.VeryHappy: return Loc.T("とても嬉しそう", "overjoyed");
                case Expression.Sad: return Loc.T("しょんぼりしている", "looking sad");
                case Expression.Crying: return Loc.T("泣いている", "crying");
                case Expression.Angry: return Loc.T("ぷんぷんしている", "looking angry");
                case Expression.Surprised: return Loc.T("目を丸くしている", "wide-eyed");
                case Expression.Confused: return Loc.T("首をかしげている", "puzzled");
                case Expression.Shy: return Loc.T("照れている", "bashful");
                case Expression.Sleepy: return Loc.T("眠そう", "sleepy");
                case Expression.Love: return Loc.T("うっとりしている", "dreamy");
                case Expression.Worried: return Loc.T("困った顔", "worried");
                case Expression.Excited: return Loc.T("わくわくしている", "excited");
                case Expression.Disgusted: return Loc.T("いやそうな顔", "grimacing");
                case Expression.Thinking: return Loc.T("考えこんでいる", "thinking");
                default: return Loc.T("ふつうの顔", "calm");
            }
        }

        public static string GestureText(Gesture g, int number)
        {
            switch (g)
            {
                case Gesture.Wave: return Loc.T("手を振った", "waved");
                case Gesture.Nod: return Loc.T("うなずいた", "nodded");
                case Gesture.ShakeHead: return Loc.T("首を振った", "shook its head");
                case Gesture.PointAt: return Loc.T("何かを指さした", "pointed at something");
                case Gesture.ShowNumber: return Loc.Format("{0}を示した", "showed {0}", number);
                case Gesture.Jump: return Loc.T("ぴょんと跳ねた", "hopped");
                case Gesture.Bow: return Loc.T("お辞儀した", "bowed");
                case Gesture.Shrug: return Loc.T("肩をすくめた", "shrugged");
                case Gesture.Hug: return Loc.T("ぎゅっとした", "hugged");
                case Gesture.OfferItem: return Loc.T("何かを差し出した", "held something out");
                case Gesture.Eat: return Loc.T("食べるしぐさ", "mimed eating");
                case Gesture.Cry: return Loc.T("涙をこぼした", "shed tears");
                case Gesture.Clap: return Loc.T("拍手した", "clapped");
                case Gesture.Spin: return Loc.T("くるっと回った", "spun around");
                case Gesture.Think: return Loc.T("考えるしぐさ", "pondered");
                case Gesture.Beckon: return Loc.T("手招きした", "beckoned");
                case Gesture.Shoo: return Loc.T("あっちを示した", "waved you off");
                case Gesture.Hold: return Loc.T("何かを抱えた", "held something");
                case Gesture.LookUp: return Loc.T("空を見上げた", "looked up at the sky");
                case Gesture.Shiver: return Loc.T("ぶるっと震えた", "shivered");
                case Gesture.Stretch: return Loc.T("伸びをした", "stretched");
                case Gesture.Dance: return Loc.T("踊った", "danced");
                default: return null;
            }
        }
    }

    internal static class ResidentUtil
    {
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);

        public static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static void AddUnique(List<string> list, string s)
        {
            if (!string.IsNullOrEmpty(s) && !list.Contains(s)) list.Add(s);
        }
    }
}
