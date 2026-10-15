using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>星ごとの物語の状態（SaveScope.Planet。ホストが正本）。</summary>
    [Serializable]
    public class StoryPlanetState
    {
        public int version = 1;
        public int chapter;
        public bool prologueDone;
        public bool prologueFoodGiven;
        public string guideResidentId;
        public string catchphraseConcept;
        /// <summary>節目（キー → 到達した日）。"arrived" "first_move_in" "first_move_out" "first_repair" "launch_ready" "chapterN" など。</summary>
        public List<StringIntPair> milestones = new List<StringIntPair>();
        /// <summary>主人公の商店で各住民が買った回数（常連客の判定）。</summary>
        public List<StringIntPair> shopPurchases = new List<StringIntPair>();
        /// <summary>主人公の活動の回数（エンディングで暮らしを反映する）。</summary>
        public List<StringIntPair> activity = new List<StringIntPair>();
        /// <summary>贈り物をした住民と回数。</summary>
        public List<StringIntPair> giftsGiven = new List<StringIntPair>();
        public int lastGuideVisitDay;
        public bool guideVisitPending;
        public int endingCount;
        public bool departedToNextPlanet;
        public EndingRecord lastEnding;

        public int GetMilestone(string key) => Find(milestones, key);
        public bool HasMilestone(string key) => GetMilestone(key) > 0;

        public void SetMilestoneOnce(string key, int day)
        {
            if (HasMilestone(key)) return;
            Set(milestones, key, Math.Max(1, day));
        }

        public static int Find(List<StringIntPair> list, string key)
        {
            if (list == null) return 0;
            foreach (var p in list) if (p != null && p.key == key) return p.value;
            return 0;
        }

        public static void Set(List<StringIntPair> list, string key, int value)
        {
            foreach (var p in list)
            {
                if (p != null && p.key == key)
                {
                    p.value = value;
                    return;
                }
            }
            list.Add(new StringIntPair(key, value));
        }

        public static int Add(List<StringIntPair> list, string key, int delta)
        {
            int v = Find(list, key) + delta;
            Set(list, key, v);
            return v;
        }
    }

    /// <summary>最後に見たエンディングの記録（タブレットの「思い出」で見返す）。</summary>
    [Serializable]
    public class EndingRecord
    {
        public int day;
        public int farewellCount;
        public int closeFriendCount;
        public int regularCount;
        public int giftCount;
        public int letterCount;
        public int dictionaryWordCount;
        public string topActivity;
        /// <summary>見送りに来た住民（近くまで来た人）。</summary>
        public List<string> farewellResidentIds = new List<string>();
        /// <summary>遠くから見ていた住民。</summary>
        public List<string> watcherResidentIds = new List<string>();
        /// <summary>手紙をくれた（引っ越した）住民。</summary>
        public List<string> letterResidentIds = new List<string>();
        /// <summary>見送りで聞いた言葉の記録ID（原本は Language 側。表示は表面形のみ）。</summary>
        public List<string> farewellRecordIds = new List<string>();
        public string guideLastRecordId;
    }

    /// <summary>旅の記録（星をまたいで持ち歩く。SaveScope.PlayerProfile）。</summary>
    [Serializable]
    public class JourneyEntry
    {
        public string worldId;
        public string seedText;
        public string planetName;
        public int planetIndex;
        public int daysStayed;
        public bool departed;
        public int residentsMet;
    }

    [Serializable]
    public class AppearancePreset
    {
        public string label;
        public CharacterAppearance appearance;
    }

    [Serializable]
    public class StoryProfileState
    {
        public int version = 1;
        public List<JourneyEntry> journey = new List<JourneyEntry>();
        public List<AppearancePreset> presets = new List<AppearancePreset>();
    }

    /// <summary>次の星へ進む時の引き継ぎ設定（基本決定 §5 の未決定事項を選択式・仮で扱う）。</summary>
    public class CarryOverPlan
    {
        public bool keepAppearance = StoryTuning.CarryDefaultKeepAppearance;
        public bool keepItems = StoryTuning.CarryDefaultKeepItems;
        public bool keepMoney = StoryTuning.CarryDefaultKeepMoney;
        public bool keepTools = StoryTuning.CarryDefaultKeepTools;
        public bool shortPrologue = true;
        public string seedText = "";
        public string nickname = "";
        public CharacterAppearance newAppearance;
    }
}
