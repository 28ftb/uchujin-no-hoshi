using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    // =====================================================================
    // セーブ・同期用のデータ（JsonUtility：public フィールドのみ、Dictionary 不可）
    //   Planet               … 星の器具の状態（発酵樽・乾燥棚・精製器の中身）。ホストが正本。
    //   PlayerPlanetKnowledge… 料理・加工記録（作った物・試した組み合わせ・観察した実演）。
    //   PlayerProfile        … 持ち歩くもの（生物の調理習熟・食の記録・食べ物の効果・持ち越したレシピ）。
    // =====================================================================

    /// <summary>時間経過の加工1枠（§8.1）。</summary>
    [Serializable]
    public class TimedSlotData
    {
        public string slotId;
        public string stationKey;
        public string stationNameJa;
        public string stationNameEn;
        public int stationType;
        public string recipeId;
        public string outputItemId;
        public int count;
        public int quality;
        public string ownerId;
        public string ownerName;
        public int startDay;
        public int startMinute;
        public float hoursRequired;
        public float progressHours;
        public bool ready;
        public bool hasDry;
        public bool hasFerment;
        public bool hasSmoke;
        public bool outdoor;
        public bool weatherBonusApplied;
        public bool notified;
        public List<string> ingredients = new List<string>();
        public List<int> steps = new List<int>();
        public List<string> creatureSpecies = new List<string>();

        public float Progress01 => hoursRequired <= 0f ? 1f : UnityEngine.Mathf.Clamp01(progressHours / hoursRequired);
    }

    [Serializable]
    public class CookingPlanetData
    {
        public List<TimedSlotData> slots = new List<TimedSlotData>();
    }

    /// <summary>作った物の記録（§12.2〜12.3）。自分で行った手順・確認できた結果だけを記録する。</summary>
    [Serializable]
    public class RecipeRecord
    {
        public string recipeId;
        /// <summary>自分で付けた料理名（自由入力。宇宙語名とは混同しない）。</summary>
        public string customName;
        /// <summary>住民の好みの予想など、自由メモ。</summary>
        public string note;
        public int timesMade;
        public int timesCommissioned;
        public int firstDay;
        public int lastDay;
        public int bestQuality;
        public int lastStation;
        public string lastOutputItemId;
        public List<string> lastIngredients = new List<string>();
        public List<int> lastSteps = new List<int>();
        public List<string> creatureSpecies = new List<string>();
        public string weatherNote;
        public string voiceCardRecordId;
    }

    /// <summary>試した組み合わせ（成功・失敗・謎料理すべて）。</summary>
    [Serializable]
    public class TryRecord
    {
        public int day;
        public int minute;
        public List<string> ingredients = new List<string>();
        public List<int> steps = new List<int>();
        public int station;
        public string resultItemId;
        public string recipeId;
        public int quality;
        public bool mystery;
        public string weatherNote;
    }

    /// <summary>住民の料理の実演を見た記録（§11・§12.1：身振りと材料。言葉の意味は記録しない）。</summary>
    [Serializable]
    public class DemoRecord
    {
        public string recipeId;
        public string residentId;
        public int day;
        public string utteranceRecordId;
        public List<string> ingredients = new List<string>();
        public List<int> steps = new List<int>();
    }

    [Serializable]
    public class CookingKnowledgeData
    {
        public List<RecipeRecord> recipes = new List<RecipeRecord>();
        public List<TryRecord> tries = new List<TryRecord>();
        public List<DemoRecord> demos = new List<DemoRecord>();
        /// <summary>住民ごとの最後に実演を見た日。</summary>
        public List<StringIntPair> lastDemoDay = new List<StringIntPair>();
    }

    /// <summary>生物の調理習熟（§6.3【提案】）。個体UID×能力ごとの経験回数。</summary>
    [Serializable]
    public class MasteryEntry
    {
        public string uid;
        public int ability;
        public int uses;
    }

    /// <summary>食の記録（コレクション）【提案】。</summary>
    [Serializable]
    public class FoodLogEntry
    {
        public string itemId;
        public int times;
        public int firstDay;
        public string planetName;
        public int bestQuality;
    }

    [Serializable]
    public class CookingProfileData
    {
        /// <summary>前の星から持ち越した「作れる」レシピ（§14【提案】）。</summary>
        public List<string> carriedRecipes = new List<string>();
        public List<MasteryEntry> mastery = new List<MasteryEntry>();
        public List<FoodLogEntry> foodLog = new List<FoodLogEntry>();
        public string effectId;
        public int effectMinutes;
        public string effectSourceItemId;
    }
}
