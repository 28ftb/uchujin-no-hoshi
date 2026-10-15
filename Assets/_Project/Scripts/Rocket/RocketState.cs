using System;
using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Rocket
{
    // =====================================================================
    // セーブ・同期データ（JsonUtility 用。public フィールドのみ、Dictionary 不使用）。
    // Planet スコープ：修理状態・燃料・注文・履歴（ホストが正本 §17.3）。
    // PlayerPlanetKnowledge スコープ：図を見たか・発見した素材（個人の知識）。
    // =====================================================================

    [Serializable]
    public class DeviceState
    {
        public string id;
        /// <summary>診断・図で調べた（§3.3 調査中）。</summary>
        public bool inspected;
        /// <summary>納品済みの数（itemId → 個数）。</summary>
        public List<StringIntPair> delivered = new List<StringIntPair>();
        /// <summary>支払い済みの作業費。</summary>
        public int paid;
        /// <summary>必須能力の生物（作業場に配置中）。</summary>
        public string creatureUid;
        /// <summary>任意の手伝い生物。</summary>
        public string helperUid;
        public bool repaired;
        public bool verified;
        public int repairedDay;
        public string repairedBy;
        /// <summary>誰がどれだけ貢献したか（playerId → 点数。マルチ §17）。</summary>
        public List<StringIntPair> contributions = new List<StringIntPair>();

        public int Delivered(string itemId)
        {
            foreach (var p in delivered) if (p.key == itemId) return p.value;
            return 0;
        }

        public void AddDelivered(string itemId, int n)
        {
            foreach (var p in delivered)
            {
                if (p.key == itemId)
                {
                    p.value += n;
                    return;
                }
            }
            delivered.Add(new StringIntPair(itemId, n));
        }

        public void AddContribution(string playerId, int points)
        {
            if (string.IsNullOrEmpty(playerId) || points <= 0) return;
            foreach (var p in contributions)
            {
                if (p.key == playerId)
                {
                    p.value += points;
                    return;
                }
            }
            contributions.Add(new StringIntPair(playerId, points));
        }

        public bool AnyProgress => paid > 0 || delivered.Exists(p => p.value > 0) || !string.IsNullOrEmpty(creatureUid);
    }

    /// <summary>修理屋への加工依頼（§9.3）。</summary>
    [Serializable]
    public class WorkOrder
    {
        public string id;
        public string playerId;
        public string recipeId;
        public int count;
        public int orderedDay;
        public int readyDay;
    }

    /// <summary>修理履歴（§13.1：完成した部品がどこに付いたか・誰の貢献か）。</summary>
    [Serializable]
    public class RepairHistoryEntry
    {
        public int day;
        public int minute;
        public string playerId;
        /// <summary>"deliver" "pay" "creature" "install" "test" "final" "fuel" "launch" "return"</summary>
        public string kind;
        public string deviceId;
        public string itemId;
        public int count;
    }

    [Serializable]
    public class RocketPlanetSave
    {
        public int version = 1;
        public List<DeviceState> devices = new List<DeviceState>();
        public int fuelInTank;
        public bool finalTestDone;
        public bool launchReady;
        public int launchReadyDay;
        public bool hasLaunched;
        public int launchCount;
        /// <summary>試運転の通し番号（ゲストが演出を再生するため）。</summary>
        public int testSerial;
        public string lastTestDevice;
        public int finalSerial;
        public List<WorkOrder> orders = new List<WorkOrder>();
        public List<RepairHistoryEntry> history = new List<RepairHistoryEntry>();
        /// <summary>貢献者の表示名（オフラインでも履歴に名前を出す）。</summary>
        public List<StringStringPair> playerNames = new List<StringStringPair>();

        public DeviceState Device(string id)
        {
            foreach (var d in devices) if (d.id == id) return d;
            return null;
        }

        public void EnsureDevices()
        {
            if (devices == null) devices = new List<DeviceState>();
            if (orders == null) orders = new List<WorkOrder>();
            if (history == null) history = new List<RepairHistoryEntry>();
            if (playerNames == null) playerNames = new List<StringStringPair>();
            foreach (var def in RocketTables.Devices)
            {
                if (Device(def.Id) == null) devices.Add(new DeviceState { id = def.Id });
            }
            foreach (var d in devices)
            {
                if (d.delivered == null) d.delivered = new List<StringIntPair>();
                if (d.contributions == null) d.contributions = new List<StringIntPair>();
            }
        }

        public string NameOf(string playerId)
        {
            foreach (var p in playerNames) if (p.key == playerId) return p.value;
            return null;
        }

        public void RememberName(string playerId, string name)
        {
            if (string.IsNullOrEmpty(playerId) || string.IsNullOrEmpty(name)) return;
            foreach (var p in playerNames)
            {
                if (p.key == playerId)
                {
                    p.value = name;
                    return;
                }
            }
            playerNames.Add(new StringStringPair(playerId, name));
        }
    }

    [Serializable]
    public class RocketKnowledgeSave
    {
        /// <summary>修理屋に図を見せてもらった（第1章の導入）。</summary>
        public bool diagramSeen;
        /// <summary>実際に手に入れた・見た素材（診断に名前が出るようになる §2.1）。</summary>
        public List<string> discovered = new List<string>();
        public string lastSelectedDevice;
    }

    // =====================================================================
    // コマンド（星の状態を変える操作はすべてホスト権威 §17.3）
    // =====================================================================

    [Serializable]
    public class RocketInspectCommand : WorldCommand
    {
        public string deviceId;
    }

    /// <summary>素材を納品する。deviceId が "tank" なら燃料タンクへ給油。</summary>
    [Serializable]
    public class RocketDeliverCommand : WorldCommand
    {
        public string deviceId;
        public string itemId;
        public int count;
    }

    [Serializable]
    public class RocketPayCommand : WorldCommand
    {
        public string deviceId;
        public int amount;
    }

    /// <summary>作業場へ生物を配置する（uid が空なら連れて帰る）。</summary>
    [Serializable]
    public class RocketAssignCreatureCommand : WorldCommand
    {
        public string deviceId;
        public string creatureUid;
        public bool asHelper;
    }

    [Serializable]
    public class RocketInstallCommand : WorldCommand
    {
        public string deviceId;
    }

    [Serializable]
    public class RocketTestCommand : WorldCommand
    {
        public string deviceId;
    }

    [Serializable]
    public class RocketFinalTestCommand : WorldCommand
    {
    }

    [Serializable]
    public class RocketBeginReturnCommand : WorldCommand
    {
    }

    [Serializable]
    public class RocketOrderCommand : WorldCommand
    {
        public string recipeId;
        public int count;
    }

    [Serializable]
    public class RocketCollectOrdersCommand : WorldCommand
    {
    }

    /// <summary>修理屋から部品を買った代金を修理屋の財布へ（住民経済の循環）。</summary>
    [Serializable]
    public class RocketMechanicIncomeCommand : WorldCommand
    {
        public int amount;
        public string itemId;
        public int count;
    }
}
