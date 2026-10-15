using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>能力の出どころ（器具に住む生物・同行する生物・他の人の生物）。</summary>
    public sealed class AbilitySource
    {
        public CreatureAbility Ability;
        public string CreatureUid;
        public string SpeciesId;
        public string DisplayName;
        public int Level = 1;
        /// <summary>方式A：対応する器具に住んで能力を供給している。</summary>
        public bool InDevice;
        public string DeviceInstanceId;
        public string DeviceItemId;
        public string OwnerId;
        /// <summary>共同調理：他のプレイヤーの生物を借りている【提案】。</summary>
        public bool Borrowed;
        /// <summary>修理作業場で手伝っている。</summary>
        public bool AtWorkshop;
    }

    public enum StepMode
    {
        BuiltIn,   // 設備・手作業
        Creature,  // 生物の能力
        Weather,   // 天候の力（光雨・逆雨）
        Timed,     // 時間をかける（乾燥・発酵・燻製）
        Slow,      // 生物がいないので設備にまかせて時間をかける
        Missing    // できない
    }

    public sealed class StepPlan
    {
        public CraftStep Step;
        public StepMode Mode;
        public AbilitySource Source;
        /// <summary>手作業の工程を上手にしてくれる生物（研磨粉・箱詰めなど）。</summary>
        public AbilitySource Helper;
        public float Hours;
    }

    /// <summary>あるステーションで、あるレシピを作る計画。</summary>
    public sealed class CraftPlan
    {
        public RecipeDef Recipe;
        public readonly List<StepPlan> Steps = new List<StepPlan>();
        public readonly List<CraftStep> Missing = new List<CraftStep>();
        public bool StationAllowed = true;
        public bool WeatherOk = true;
        public float TimedHours;
        public bool UsesSlow;
        public string TimedKey;
        public int TimedCapacity;
        public string TimedNameJa, TimedNameEn;

        public bool IsTimed => TimedHours > 0f;
        public bool Feasible => StationAllowed && WeatherOk && Missing.Count == 0;

        public IEnumerable<AbilitySource> UsedSources
        {
            get
            {
                foreach (var s in Steps)
                {
                    if (s.Source != null) yield return s.Source;
                    if (s.Helper != null) yield return s.Helper;
                }
            }
        }
    }

    /// <summary>時間の工程を置く場所（乾燥棚・発酵樽・共用調理場・精製器）。</summary>
    public sealed class TimedTarget
    {
        public string Key;
        public int Capacity;
        public string NameJa, NameEn;
        public bool Outdoor;
    }

    /// <summary>
    /// 開いたステーションの状況（どの能力が使えるか・どこに時間の加工を置けるか）。
    /// どこから開かれても動くよう、stationInstanceId は家具インスタンスID・施設ID・生物UIDなどを受け付ける。
    /// </summary>
    public sealed class StationContext
    {
        public CraftStationType Type;
        public string InstanceId;
        public StationProfile Profile;
        public DeviceDef Device;
        public PlacedFurnitureView Furniture;
        public PlayerState Player;
        public string OwnerId;
        public Vector3 Position;
        public bool Outdoor;
        public WeatherState Weather = WeatherState.Fine;
        public int BatchMax;
        public readonly List<PlacedFurnitureView> LinkedDevices = new List<PlacedFurnitureView>();
        public readonly List<AbilitySource> Sources = new List<AbilitySource>();
        public readonly Dictionary<CraftStep, TimedTarget> TimedTargets = new Dictionary<CraftStep, TimedTarget>();
        /// <summary>設備まかせ（Slow）の加工を置く場所。</summary>
        public TimedTarget OwnTimed;
        public bool HasCarry;
        public bool HasMimic;
        public bool HasSense;

        public string Title
        {
            get
            {
                if (Device != null) return Device.Name;
                return Profile != null ? Profile.Name : "";
            }
        }

        public bool HasBuiltIn(CraftStep s) => Profile != null && Profile.Has(Profile.BuiltIn, s);
        public bool IsSlowCapable(CraftStep s) => Profile != null && Profile.Has(Profile.SlowCapable, s);

        public AbilitySource BestSource(CreatureAbility[] abilities)
        {
            AbilitySource best = null;
            foreach (var src in Sources)
            {
                bool match = false;
                foreach (var a in abilities) if (src.Ability == a) match = true;
                if (!match) continue;
                if (best == null) { best = src; continue; }
                int sb = (src.InDevice ? 100 : 0) + src.Level * 10 + (src.Borrowed ? 0 : 1);
                int bb = (best.InDevice ? 100 : 0) + best.Level * 10 + (best.Borrowed ? 0 : 1);
                if (sb > bb) best = src;
            }
            return best;
        }

        public bool HasAbility(CreatureAbility a)
        {
            foreach (var s in Sources) if (s.Ability == a) return true;
            return false;
        }
    }
}
