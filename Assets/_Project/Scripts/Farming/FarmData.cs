using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Farming
{
    /// <summary>畑1マス（約1m）。耕した・作物があるマスだけ保存する。</summary>
    [Serializable]
    public class FarmPlot
    {
        public int x;
        public int z;
        public bool tilled;
        /// <summary>収穫後に荒れた土（ホリモグなどが耕し直す対象）。</summary>
        public bool wasFarmed;
        public bool watered;
        public int retainDays;
        public string cropKey;
        public int plantedDay;
        public float growth;
        public bool ripe;
        public int harvests;
        /// <summary>植えた人（生物が植えたなら生物の飼い主）。収穫物の持ち主の判定に使う（マルチ仕様 §7.2）。</summary>
        public string ownerId;
        public string fertilizer;
        /// <summary>土の豊かさ 0〜3（ツチポンなど）。</summary>
        public float soil;
        public bool pollinated;
        public int glowNights;
        public int litDay = -1;
        public int heatDay = -1;
        public int coolDay = -1;
        public int waterSoilComboDay = -1;
        public int daysWatered;
        public int daysDry;
        /// <summary>この時間に生物が耕した（耕す→種まきの連携の判定）。</summary>
        public long tilledByCreatureMinute = -1;
        public bool wateredByCreature;
        /// <summary>光雨（宇宙固有現象）を浴びた。品質が上がりやすい。</summary>
        public bool lightRain;

        public bool HasCrop => !string.IsNullOrEmpty(cropKey);
        public int Key => FarmingSystem.CellKey(x, z);
    }

    /// <summary>農場に置いた生き物の住み場所。</summary>
    [Serializable]
    public class FarmHabitat
    {
        public string id;
        public string itemId;
        public int x;
        public int z;
        public string ownerId;
    }

    /// <summary>生物の作業記録（タブレット「農場」アプリ）。</summary>
    [Serializable]
    public class FarmWorkLog
    {
        public int day;
        public int hour;
        public string creatureUid;
        public string speciesId;
        public string creatureName;
        public string action;
        public int count;
    }

    /// <summary>作物の記録。</summary>
    [Serializable]
    public class CropRecord
    {
        public string cropKey;
        public int harvested;
        public int bestQuality;
        public int giants;
        public int glowing;
        public int rareColors;
        public int byCreatures;
        public int firstDay = -1;
    }

    [Serializable]
    public class FarmPlanetSave
    {
        public int version = 1;
        public int expansion;
        public List<FarmPlot> plots = new List<FarmPlot>();
        public List<FarmHabitat> habitats = new List<FarmHabitat>();
        public Inventory basket;
        public Inventory warehouse;
        public Inventory seedBox;
        public bool shareHarvest;
        public List<FarmWorkLog> log = new List<FarmWorkLog>();
        public List<CropRecord> records = new List<CropRecord>();
        public List<string> combos = new List<string>();
        public int habitatCounter;
        public int lastVisitDay = -1;
    }

    [Serializable]
    public class FarmProfileSave
    {
        public int version = 1;
        public int level = 1;
        public int exp;
        public List<string> techniques = new List<string>();
        public int totalHarvested;
    }
}
