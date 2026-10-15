using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.World
{
    // 地図アプリの個人の記録（SaveScope.PlayerPlanetKnowledge：星ごと・プレイヤーごと。他の星・他人の記録と混ざらない：村仕様 §13.3、§16.4）

    [Serializable]
    public class MapPin
    {
        public string id;
        public float x;
        public float z;
        /// <summary>プレイヤーが付けた名前（自由な地名：村仕様 §13.1）。</summary>
        public string name;
        /// <summary>自由記述のメモ。</summary>
        public string note;
        public int color;
        public int day;
    }

    /// <summary>看板で見た宇宙語の地名（見たものだけ。意味・用途は記録しない）。</summary>
    [Serializable]
    public class SeenPlaceName
    {
        public string signId;
        /// <summary>内部の結びつけ用（プレイヤーには表示しない）。</summary>
        public string facilityId;
        public string surface;
        public float x;
        public float z;
        public int day;
        public int timesSeen;
        public bool signpost;
        /// <summary>プレイヤーが自分で書き添えた推測（任意）。</summary>
        public string guess;
    }

    [Serializable]
    public class MapKnowledge
    {
        /// <summary>探索済みのセル（ビット列）。</summary>
        public List<int> explored = new List<int>();
        public List<MapPin> pins = new List<MapPin>();
        public List<SeenPlaceName> seen = new List<SeenPlaceName>();
        /// <summary>実際に見つけた建物（観測済みの事実：村仕様 §13.2）。</summary>
        public List<string> discovered = new List<string>();
        public int pinCounter;

        [NonSerialized] private HashSet<string> discoveredSet;

        public int Grid => WorldTuning.ExploreGrid;

        public void EnsureBits()
        {
            int words = (Grid * Grid + 31) / 32;
            while (explored.Count < words) explored.Add(0);
        }

        public bool IsExplored(int idx)
        {
            if (idx < 0) return false;
            int w = idx >> 5;
            return w < explored.Count && (explored[w] & (1 << (idx & 31))) != 0;
        }

        public bool SetExplored(int idx)
        {
            EnsureBits();
            int w = idx >> 5;
            int bit = 1 << (idx & 31);
            if ((explored[w] & bit) != 0) return false;
            explored[w] |= bit;
            return true;
        }

        public int CellOf(float x, float z)
        {
            float cell = WorldTuning.WorldSize / Grid;
            int cx = Mathf.FloorToInt((x + WorldTuning.Half) / cell);
            int cz = Mathf.FloorToInt((z + WorldTuning.Half) / cell);
            if (cx < 0 || cz < 0 || cx >= Grid || cz >= Grid) return -1;
            return cz * Grid + cx;
        }

        public bool IsExploredAt(float x, float z) => IsExplored(CellOf(x, z));

        public float ExploredRatio()
        {
            int n = 0;
            for (int i = 0; i < Grid * Grid; i++) if (IsExplored(i)) n++;
            return n / (float)(Grid * Grid);
        }

        public bool IsDiscovered(string id)
        {
            if (discoveredSet == null) discoveredSet = new HashSet<string>(discovered);
            return discoveredSet.Contains(id);
        }

        public bool Discover(string id)
        {
            if (IsDiscovered(id)) return false;
            discovered.Add(id);
            discoveredSet.Add(id);
            return true;
        }

        public SeenPlaceName FindSeen(string signId)
        {
            foreach (var s in seen) if (s.signId == signId) return s;
            return null;
        }

        public void Clear()
        {
            explored.Clear();
            pins.Clear();
            seen.Clear();
            discovered.Clear();
            discoveredSet = null;
            pinCounter = 0;
            EnsureBits();
        }

        public void AfterLoad()
        {
            if (explored == null) explored = new List<int>();
            if (pins == null) pins = new List<MapPin>();
            if (seen == null) seen = new List<SeenPlaceName>();
            if (discovered == null) discovered = new List<string>();
            discoveredSet = null;
            EnsureBits();
        }
    }
}
