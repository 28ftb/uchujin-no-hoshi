using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 所有している生物1匹（固有ID付き：マルチ仕様 §10.4）。
    /// 持ち歩く個体（同行・ケージ）はプレイヤーの PlayerProfile、星に置いた個体（農場・自宅・器具など）はその星の Planet に保存する。
    /// </summary>
    [Serializable]
    public class OwnedCreature
    {
        public string uid;
        public string speciesId;
        public string nickname;
        public string ownerId;
        public CreaturePlacement placement = CreaturePlacement.Companion;
        public string targetId;

        // ---- 成長・なつき（生き物仕様 §6.3 / 農業仕様 §5）----
        public int level = 1;
        public int exp;
        /// <summary>なつき度 0〜100。</summary>
        public int affection = 10;

        // ---- 個体差 ----
        /// <summary>体の大きさの個体差（倍率）。</summary>
        public float size = 1f;
        /// <summary>色の個体差 0=ふつう 1=淡い 2=濃い 3=珍しい色違い。</summary>
        public int colorVariant;
        public float hueShift;
        public int personality;
        /// <summary>得意な能力（-1=なし）。その能力だけ1段階高い。</summary>
        public int talent = -1;
        /// <summary>好きな食べ物の系統（種族の好みと違うこともある）。</summary>
        public string favoriteFood;
        public bool favoriteKnown;

        // ---- 来歴（譲った時も引き継ぐ：マルチ仕様 §10.3）----
        public int caughtDay;
        public string caughtWorldId;
        public string caughtAreaName;
        /// <summary>"caught" "gift" "quest" "bred" "case" など。</summary>
        public string origin;
        public string originDetail;

        /// <summary>星に住んでいる場合、その星の WorldId（預けた先の記録にも使う）。</summary>
        public string residentWorldId;

        public int lastPetDay = -1;
        public int lastFedDay = -1;
        public int fedCountToday;
        public int lastDailyDay = -1;

        public OwnedCreature Clone() => JsonUtility.FromJson<OwnedCreature>(JsonUtility.ToJson(this));
    }

    /// <summary>野生の個体（ホストが出現を決め、星の状態として同期する）。動きは各端末の見た目だけ。</summary>
    [Serializable]
    public class WildRecord
    {
        public string wildId;
        public string speciesId;
        public string areaId = AreaIds.Overworld;
        public float x, y, z;
        public long spawnMinute;
        public long expireMinute;
        /// <summary>餌・採掘・誘致などの出現理由（"normal" "mine" "dust" "farm" "home"）。</summary>
        public string reason;
        /// <summary>決定的な個体差の種。</summary>
        public string individualSeed;
        public int failedAttempts;

        public Vector3 Position => new Vector3(x, y, z);
    }

    /// <summary>生き物図鑑の1種（PlayerPlanetKnowledge）。客観的な記録＋プレイヤーの自由メモ（生き物仕様 §10.1）。</summary>
    [Serializable]
    public class BestiaryEntry
    {
        public string speciesId;
        public int seenCount;
        public int caughtCount;
        public int obtainedCount;
        public int firstSeenDay = -1;
        public int firstCaughtDay = -1;
        /// <summary>見かけた時刻（0〜23時のビット）。</summary>
        public int hourMask;
        public List<string> areas = new List<string>();
        public List<string> weathers = new List<string>();
        /// <summary>観察した行動（BehaviorFact のキー）と最初に見た日。</summary>
        public List<StringIntPair> behaviors = new List<StringIntPair>();
        /// <summary>行動から分かった「得意そうなこと」（CreatureAbility の int）。</summary>
        public List<int> revealedAbilities = new List<int>();
        /// <summary>名人に見せる等で知った捕まえ方の手掛かり。</summary>
        public bool clueKnown;
        public float biggestSize;
        /// <summary>プレイヤーが付けた呼び名（空なら既定の仮名）。宇宙語の呼び名は書かない。</summary>
        public string playerName;
        /// <summary>プレイヤーの自由メモ（ゲームが正解に書き換えない）。</summary>
        public string memo;

        public bool Seen => seenCount > 0 || caughtCount > 0 || obtainedCount > 0;

        public bool HasBehavior(string key)
        {
            foreach (var b in behaviors) if (b.key == key) return true;
            return false;
        }

        public bool IsRevealed(CreatureAbility a) => revealedAbilities.Contains((int)a);
    }

    /// <summary>ケースに入った生物（ItemStack.data の中身）。</summary>
    [Serializable]
    public class CaseData
    {
        public OwnedCreature creature;
        /// <summary>受け取ったら自動で自分の生物として迎える（ホストからの捕獲・譲渡の受け渡し用）。</summary>
        public bool autoAdopt;
        public string placementAfter;
    }

    [Serializable]
    public class CreaturePlanetSave
    {
        public int version = 1;
        public List<OwnedCreature> placed = new List<OwnedCreature>();
        public List<WildRecord> wilds = new List<WildRecord>();
    }

    [Serializable]
    public class CreatureProfileSave
    {
        public int version = 1;
        public List<OwnedCreature> carried = new List<OwnedCreature>();
        public int catchLevel = 1;
        public int catchExp;
        public List<string> techniques = new List<string>();
        public int totalCaught;
        public List<StringIntPair> dailyShown = new List<StringIntPair>();
    }

    [Serializable]
    public class CreatureKnowledgeSave
    {
        public int version = 1;
        public List<BestiaryEntry> entries = new List<BestiaryEntry>();
    }
}
