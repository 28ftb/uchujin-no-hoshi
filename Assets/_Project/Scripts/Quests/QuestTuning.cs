using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Quests
{
    /// <summary>
    /// クエスト・住民イベント・村の行事の調整用の値。
    /// 仕様上の数値はすべて【仮数値】（クエスト仕様 §21「具体的な報酬額・友好度・分岐条件」【未決定】）。
    /// Unity 上で遊びながら調整する前提で、ここに集める。
    /// </summary>
    public static class QuestTuning
    {
        // ---------------- 日常のお願い（§6）----------------
        /// <summary>1人のプレイヤーに同時に出ている「気配のある／引き受けた」お願いの上限（§21 同時受注数【未決定】）。</summary>
        public const int MaxOpenFavorsPerPlayer = 4;
        /// <summary>1日に新しく生まれるお願いの数（最小〜最大）。</summary>
        public const int DailyFavorsMin = 1;
        public const int DailyFavorsMax = 3;
        /// <summary>まだ引き受けていないお願いが、住民が自分で解決して消えるまでの日数（§5.3 住民が別の方法で解決）。</summary>
        public const int HintedExpireDays = 2;
        /// <summary>期限の言葉（明日まで等）を付けたお願いの割合。</summary>
        public const float DeadlineChance = 0.35f;
        /// <summary>期限なしで引き受けたお願いが、住民が自分で解決してしまうまでの日数。</summary>
        public const int ActiveNoDeadlineExpireDays = 6;
        /// <summary>配達（別の住民へ荷物を届ける）のお願いになる割合。</summary>
        public const float DeliveryFavorChance = 0.15f;
        /// <summary>「青い魚」のように種類＋色で頼む割合（色タグがある場合）。</summary>
        public const float CategoryColorGoalChance = 0.3f;
        /// <summary>生き物の捕獲・観察のお願いになる割合（生き物名人以外）。</summary>
        public const float CreatureFavorChance = 0.08f;

        // ---------------- 手掛かり（§4.2）----------------
        /// <summary>住民が見本（実物・絵）を見せる基本確率。系統で増減する。</summary>
        public const float ShowSampleBaseChance = 0.55f;
        /// <summary>序盤（この日数まで）は見本を見せやすい（宇宙語が分からない時期の救済）。</summary>
        public const int EarlyDaysForMoreClues = 7;
        public const float EarlyClueBonus = 0.25f;
        /// <summary>数が2以上の時に指で数を示す確率。</summary>
        public const float ShowNumberChance = 0.85f;
        /// <summary>「考える」身振りに応えてもう一度説明してくれる回数の上限（その場で）。</summary>
        public const int MaxReExplain = 3;
        /// <summary>話しかけられてお願いを伝えた後、うなずき等を返事として受け付ける時間（実秒）。</summary>
        public const float OfferResponseSeconds = 40f;
        /// <summary>引き受けた依頼を首振り2回でやめる時の受付時間（実秒）。</summary>
        public const float AbandonConfirmSeconds = 15f;

        // ---------------- 報酬（§12）----------------
        public const float RewardPriceMultiplier = 1.6f;
        public const int RewardBase = 40;
        public const int RewardMin = 30;
        public const int RewardMax = 2500;
        public const float BoardRewardMultiplier = 1.25f;
        /// <summary>報酬に物（住民の仕事に関わる物）が付く確率。</summary>
        public const float RewardItemChance = 0.3f;
        /// <summary>生き物名人の依頼で、お礼に生き物を譲ってくれる確率。</summary>
        public const float CreatureRewardChance = 0.3f;
        public const int FriendshipOnComplete = 4;
        public const int FriendshipOnCompleteLiked = 6;
        public const int FriendshipOnHelp = 3;
        public const int FriendshipOnMisunderstanding = 1;
        public const int FriendshipOnEventAttend = 2;
        public const int FriendshipBirthdayGiftLiked = 6;
        public const int FriendshipBirthdayGiftOther = 2;
        /// <summary>品質が求められた以上だった時の報酬倍率。</summary>
        public const float RewardQualityBonus = 0.15f;

        // ---------------- 日替わり掲示板（§6.2）----------------
        public const int BoardMaxPapers = 5;
        public const int BoardNewPapersPerDay = 2;
        public const int BoardPaperLifetimeDays = 3;
        /// <summary>依頼主が絵を描いて添える確率（描いた場合のみ絵を掲示する）。</summary>
        public const float BoardPictureChance = 0.6f;

        // ---------------- 自発的なお手伝い（§7）----------------
        public const int MaxHelpEventsActive = 3;
        public const int HelpSpawnStartHour = 8;
        public const int HelpSpawnEndHour = 18;
        public const float HelpSpawnChancePerHour = 0.35f;
        public const float CarryArriveRadius = 2.8f;
        public const float GuideArriveRadius = 3.5f;
        public const float LostItemOwnerGiveUpDays = 3;
        public const int HelpRewardMoneyMin = 20;
        public const int HelpRewardMoneyMax = 80;
        /// <summary>雨が降り始めた時に洗濯物の手伝いが出る住民数の上限。</summary>
        public const int LaundryEventsPerRain = 2;

        // ---------------- 生活変化（§8.2）----------------
        public const int MaxLifeMarksPerResident = 3;

        // ---------------- 村の行事（§13）----------------
        /// <summary>誕生日の周期（日）。四季なしの暦：星の「ひと巡り」【仮】（時間仕様 §15【未決定】）。</summary>
        public const int BirthdayCycleDays = 28;
        public const int PartyStartMinute = 17 * 60;
        public const int PartyEndMinute = 20 * 60;
        public const int MarketStartMinute = 9 * 60;
        public const int MarketEndMinute = 15 * 60;
        public const int MarketMaxStalls = 4;
        public const int MarketItemsPerStall = 3;
        public const float MarketPriceMultiplier = 1.1f;
        public const int MarketStockPerItem = 5;
        public const int StarBandEarliestMinute = 19 * 60;
        public const int StarBandEndMinute = 23 * 60 + 40;
        public const int WelcomeStartMinute = 16 * 60;
        public const int WelcomeEndMinute = 18 * 60;
        public const float EventAttendRadius = 14f;
        public const int MaxGatherers = 5;
        /// <summary>お別れ会で贈り物をもらえる親しさ。</summary>
        public const int FarewellGiftFriendship = 20;

        // ---------------- 個別ストーリー（§9）----------------
        public const int StoryEarliestDay = 2;
        /// <summary>同時に進む（未完の）個別ストーリーの上限（プレイヤーごと）。</summary>
        public const int MaxConcurrentStories = 3;
        public const int StoryNoParticipationDays = 4;
        public const int StoryFriendshipForEpilogue = 30;
        public const int FishingContestMinutes = 90;
        public const float FishingTogetherRadius = 35f;
        public const float EscortRadius = 12f;

        // ---------------- 依頼帳（§11）----------------
        public const int BookMaxEntries = 300;
        /// <summary>星の状態に残す完了済みクエストの保持日数（依頼帳には残る）。</summary>
        public const int PlanetKeepFinishedDays = 20;

        // ---------------- 宇宙船修理の手掛かり（§15, ロケット仕様 §8.3）----------------
        /// <summary>修理屋が修理素材の手掛かりを見せる（1日1回まで）確率。</summary>
        public const float RepairHintChance = 0.5f;

        /// <summary>修理装置ID → 手掛かりとして見せる素材（ロケット仕様の R01〜R06【仮】。Rocket 側のIDと合わなければ単に毎回手掛かりが出るだけ）。</summary>
        public static readonly string[][] RepairHintTable =
        {
            new[] { "R01", SharedItemIds.OreConductive },
            new[] { "R02", SharedItemIds.OreSturdyMetal },
            new[] { "R03", SharedItemIds.FishShimomadara },
            new[] { "R04", SharedItemIds.CropStarHoneyFruit },
            new[] { "R05", SharedItemIds.OreHeatproofMetal },
            new[] { "R06", SharedItemIds.FishSazanamime },
        };

        /// <summary>系統ごとの「絵や見本を見せる」傾向（MultiArm・Lifestyle・Expressive は見せたがり）【仮】。</summary>
        public static float SampleBias(AlienLineage lineage)
        {
            switch (lineage)
            {
                case AlienLineage.Expressive: return 0.25f;
                case AlienLineage.Lifestyle: return 0.2f;
                case AlienLineage.MultiArm: return 0.15f;
                case AlienLineage.Floating: return -0.1f;
                case AlienLineage.StarChild: return -0.05f;
                default: return 0f;
            }
        }

        public static readonly Color PaperColor = new Color(0.98f, 0.95f, 0.85f);
        public static readonly Color BoardWood = new Color(0.55f, 0.38f, 0.24f);
    }
}
