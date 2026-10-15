using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Fishing
{
    // =====================================================================
    // 釣りモジュールの型定義（列挙・セーブ用DTO・WorldCommand）。
    // 数値はすべて【仮数値】。調整は FishingTuning（FishingProgress.cs）と FishCatalog にまとめている。
    // =====================================================================

    /// <summary>魚の行動タイプ（釣り仕様 §3.2）。伝説の魚は固有の行動を持つ（§9）。</summary>
    public enum FishBehavior
    {
        Calm,          // おとなしい
        Rush,          // 暴走型
        Diver,         // 潜行型
        Jumper,        // 跳躍型
        Turner,        // 反転型
        Endurance,     // 持久型
        Erratic,       // 変則型
        Puffer,        // 膨張（プクフグ：途中で急に抵抗が強くなる §7）
        LegendLord,    // 川のヌシ：持久＋暴走＋潜行
        LegendTide,    // 星クジラ：潮のような波で引く（波の谷で巻く）
        LegendPhantom, // ゆらぎのヌシ：向きが読めなくなる
        LegendCrystal, // 結晶竜魚：体が固まった瞬間は強引に巻けない
        LegendSkylark  // 光雨の天女魚：連続ジャンプ
    }

    /// <summary>水域（釣り仕様 §6）。地下湖は鉱山モジュールが Layout.FishingSpots に追加する。</summary>
    [Flags]
    public enum FishWater
    {
        None = 0,
        River = 1,
        Pond = 2,
        Sea = 4,
        Underground = 8,
        Surface = River | Pond | Sea,
        Any = River | Pond | Sea | Underground
    }

    /// <summary>活発な時間帯（時間・天候仕様 §8）。</summary>
    [Flags]
    public enum FishTime
    {
        None = 0,
        LateNight = 1,
        EarlyMorning = 2,
        Morning = 4,
        Afternoon = 8,
        Evening = 16,
        Night = 32,
        Daytime = EarlyMorning | Morning | Afternoon | Evening,
        Dark = Night | LateNight,
        All = 63
    }

    /// <summary>天候条件。複数要素の天候（時間・天候仕様 §5）と宇宙固有現象（§12）を判定する。</summary>
    public enum WeatherNeed
    {
        Any,
        Rain,        // 雨・小雨・大雨
        HeavyRain,
        NoRain,
        Clear,       // 快晴・晴れ
        Cloudy,
        Fog,
        StrongWind,
        Calm,        // 無風かつ雨なし（揺らぎを嫌う魚）
        LightRain,   // 光雨
        StarWind,    // 星風
        ReverseRain, // 逆雨
        StarBand,    // 星の光帯
        CrystalHaze, // 結晶霞
        BloomTide    // 花潮
    }

    /// <summary>名人から教わる技（釣り仕様 §4.3）。名称・効果は仮案。</summary>
    public enum FishingTechnique
    {
        LineHandling,  // 糸さばき：張力の逃がしが速くなり、切れる直前に一度だけ自動で緩める
        PowerReel,     // ため巻き：魚が休んだ・疲れた瞬間に速く巻ける
        Guide,         // 魚の誘導：竿の角度の効き目が上がる
        PrecisionCast, // 精密キャスト：狙いの幅が広がり、狙い通りなら珍しい魚が寄りやすい
        Lure,          // 誘い釣り：待ち時間に誘う（Space）と食いつきが早くなる
        ReadFish,      // 魚の見極め：次の動きと魚の疲れが見える
        MasterSecret   // 名人の極意：釣り上げ直前の抵抗を抑え、外れにくくなる
    }

    // ---------------- セーブ（PlayerProfile：持ち歩く技能） ----------------

    [Serializable]
    public class FishingProfileData
    {
        public string playerId;
        public int xp;
        public int level = 1;
        public int totalCaught;
        public List<string> techniques = new List<string>();
        /// <summary>見せてもらったが失敗した技（再挑戦用。記録のみ）。</summary>
        public List<string> lessonsTried = new List<string>();

        public bool Knows(FishingTechnique t) => techniques.Contains(t.ToString());
    }

    [Serializable]
    public class FishingProfileSave
    {
        public List<FishingProfileData> players = new List<FishingProfileData>();
    }

    // ---------------- セーブ（PlayerPlanetKnowledge：釣果記録） ----------------

    /// <summary>釣果帳の1種分（釣り仕様 §8）。観察した事実のみ。住民の呼び名は記録しない。</summary>
    [Serializable]
    public class FishLogEntry
    {
        public string itemId;
        public int count;
        public float maxSize;
        public int maxSizeDay;
        public string maxSizeSpotId;
        public int bestQuality;
        public int firstDay;
        public int lastDay;
        public int lastMinute;
        public string lastSpotId;
        public WeatherState lastWeather;
        /// <summary>釣れた時間帯の記録（DayPart の bit）。</summary>
        public int dayPartMask;
        /// <summary>釣れた場所（spotId の一覧、重複なし）。</summary>
        public List<string> spots = new List<string>();
        /// <summary>釣れた時の天候の特徴（"rain" "fog" "light_rain" など、重複なし）。</summary>
        public List<string> weatherTags = new List<string>();
    }

    /// <summary>見かけた魚影（観察の事実。何の魚かは書かない）。</summary>
    [Serializable]
    public class FishSighting
    {
        public string spotId;
        public int day;
        public int minute;
        /// <summary>"shadow"（大きな魚影）"frost"（白い霜のきらめき）"ripple"（輪の波紋）"glow"（光る魚影）。</summary>
        public string kind;
        public Color color;
        public WeatherState weather;
    }

    [Serializable]
    public class FishLogSave
    {
        public List<FishLogEntry> entries = new List<FishLogEntry>();
        public List<FishSighting> sightings = new List<FishSighting>();
    }

    // ---------------- セーブ（Planet：村の釣果ボード・伝説の魚の休息） ----------------

    /// <summary>星に残る釣果記録（マルチで大物・珍魚の釣果を見せ合う §13）。</summary>
    [Serializable]
    public class VillageFishRecord
    {
        public string itemId;
        public float size;
        public int quality;
        public string playerId;
        public string playerName;
        public int day;
    }

    [Serializable]
    public class FishingPlanetSave
    {
        public List<VillageFishRecord> records = new List<VillageFishRecord>();
        /// <summary>伝説の魚：釣られた後、この日まで姿を見せない（再捕獲可・休息日数は仮）。</summary>
        public List<StringIntPair> legendRestUntil = new List<StringIntPair>();
        public int totalCatchesOnPlanet;
    }

    // ---------------- WorldCommand ----------------

    /// <summary>釣果を星の記録（村の釣果ボード・伝説の魚の休息）へ報告する。魚そのものは釣った人の持ち物（ローカル）に入る。</summary>
    [Serializable]
    public class ReportCatchCommand : WorldCommand
    {
        public string itemId;
        public float size;
        public int quality;
        public string spotId;
        public string playerName;
        public float posX, posY, posZ;
    }

    /// <summary>竿の強化（素材＋お金）。代金は名人（いれば）へ渡る。成功時、発行者側で竿の段階を上げる。</summary>
    [Serializable]
    public class UpgradeRodCommand : WorldCommand
    {
        public int targetTier;
        public string residentId;
        public int money;
        public List<string> payItemIds = new List<string>();
        public List<int> payCounts = new List<int>();
    }

    /// <summary>技を教わったことを名人の記憶・親しさへ反映する（名人は星の住民＝星の状態）。</summary>
    [Serializable]
    public class LessonLearnedCommand : WorldCommand
    {
        public string residentId;
        public string technique;
    }
}
