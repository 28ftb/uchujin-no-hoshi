using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Mining
{
    // =====================================================================
    // 鉱山モジュールの型定義（セーブ用DTO・WorldCommand）。
    // 数値の調整は MiningTuning（MineCatalog.cs）へ。
    // =====================================================================

    /// <summary>その日の採掘ノードの種類。</summary>
    public enum MineNodeKind
    {
        Rock,    // 岩（石＋まれに宝石・化石・遺物）
        Ore,     // 鉱石
        Gem,     // 宝石の原石（大きさ・透明度の個体差）
        Forage   // 洞窟キノコなど（手で拾える）
    }

    // ---------------- Planet：鉱石の配置（日付と採掘済みの差分だけを保存。配置自体はシード＋日付から決定的に再生成）----------------

    [Serializable]
    public class MineFloorState
    {
        public int floor;
        /// <summary>採掘済みノードの番号。</summary>
        public List<int> mined = new List<int>();
        /// <summary>途中まで叩かれたノード（番号と累計ダメージ）。</summary>
        public List<int> damagedIdx = new List<int>();
        public List<float> damagedVal = new List<float>();
    }

    [Serializable]
    public class MiningPlanetSave
    {
        /// <summary>現在の鉱石配置の日付（日付更新で即座に再配置：時間天候仕様 §4.3【決定】）。</summary>
        public int oreDay;
        public List<MineFloorState> floors = new List<MineFloorState>();
        /// <summary>各プレイヤーの最深到達階（ホストのクエスト等が参照）。</summary>
        public List<StringIntPair> deepestByPlayer = new List<StringIntPair>();
        /// <summary>鍛冶屋の買い取り数（日ごと）。</summary>
        public int buybackDay;
        public int buybackCount;
    }

    // ---------------- PlayerProfile：採掘レベル（持ち歩く技能）----------------

    [Serializable]
    public class MiningProfileData
    {
        public string playerId;
        public int xp;
        public int level = 1;
        public int totalMined;
    }

    [Serializable]
    public class MiningProfileSave
    {
        public List<MiningProfileData> players = new List<MiningProfileData>();
    }

    // ---------------- PlayerPlanetKnowledge：鉱山の記録（minelog）----------------

    [Serializable]
    public class MineLogEntry
    {
        public string itemId;
        public int count;
        public int firstDay;
        public int firstFloor;
        public float bestSize;
        public int bestQuality;
        /// <summary>見つけた階（重複なし）。</summary>
        public List<int> floors = new List<int>();
    }

    /// <summary>その日、その階で見かけた鉱石（観察の事実）。</summary>
    [Serializable]
    public class MineFloorSighting
    {
        public int floor;
        public int day;
        public List<string> itemIds = new List<string>();
    }

    [Serializable]
    public class MineLogSave
    {
        public List<int> reachedFloors = new List<int>();
        public int deepest;
        public List<MineLogEntry> entries = new List<MineLogEntry>();
        public List<MineFloorSighting> sightings = new List<MineFloorSighting>();
        public bool sawDeepestHall;
    }

    // ---------------- WorldCommand ----------------

    /// <summary>
    /// ノードを叩く（ホスト権威：同じ鉱石の二重取得を防ぐ。マルチ仕様 §8.3）。
    /// day が現在の配置日と違えば失敗（日付更新の再配置と同時に起きた入力は、再配置後の世界で判定する）。
    /// </summary>
    [Serializable]
    public class MineHitCommand : WorldCommand
    {
        public int floor;
        public int nodeIndex;
        public int day;
        public int pickaxeTier;
        public int miningLevel;
    }

    /// <summary>ツルハシの強化（鍛冶屋。前段階の鉱石＋お金：鉱山仕様 §4【既存決定】）。</summary>
    [Serializable]
    public class UpgradePickaxeCommand : WorldCommand
    {
        public int targetTier;
        public string residentId;
    }

    /// <summary>鍛冶屋の買い取り（低価格・日数制限。商店の価値を損なわない範囲 §11.2）。</summary>
    [Serializable]
    public class SellToBlacksmithCommand : WorldCommand
    {
        public string residentId;
        public string itemId;
        public int count;
    }

    /// <summary>到達階の報告（星側の記録。ホストのクエストが GetDeepestFloorReached で参照する）。</summary>
    [Serializable]
    public class ReachFloorCommand : WorldCommand
    {
        public int floor;
    }
}
