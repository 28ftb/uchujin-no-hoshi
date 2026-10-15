using System;
using System.Collections.Generic;

namespace Uchujin.Core
{
    // JsonUtility は Dictionary を直列化できないため、セーブ用DTOでは以下のリスト型を使う。

    [Serializable]
    public class StringIntPair
    {
        public string key;
        public int value;

        public StringIntPair() { }
        public StringIntPair(string key, int value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public class StringFloatPair
    {
        public string key;
        public float value;

        public StringFloatPair() { }
        public StringFloatPair(string key, float value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public class StringStringPair
    {
        public string key;
        public string value;

        public StringStringPair() { }
        public StringStringPair(string key, string value) { this.key = key; this.value = value; }
    }

    [Serializable]
    public class StringList
    {
        public List<string> items = new List<string>();
    }

    public static class PairListUtil
    {
        public static List<StringIntPair> ToPairs(Dictionary<string, int> dict)
        {
            var list = new List<StringIntPair>();
            if (dict == null) return list;
            foreach (var kv in dict) list.Add(new StringIntPair(kv.Key, kv.Value));
            return list;
        }

        public static Dictionary<string, int> ToDict(List<StringIntPair> list)
        {
            var dict = new Dictionary<string, int>();
            if (list == null) return dict;
            foreach (var p in list) if (p != null && p.key != null) dict[p.key] = p.value;
            return dict;
        }

        public static List<StringFloatPair> ToPairs(Dictionary<string, float> dict)
        {
            var list = new List<StringFloatPair>();
            if (dict == null) return list;
            foreach (var kv in dict) list.Add(new StringFloatPair(kv.Key, kv.Value));
            return list;
        }

        public static Dictionary<string, float> ToDict(List<StringFloatPair> list)
        {
            var dict = new Dictionary<string, float>();
            if (list == null) return dict;
            foreach (var p in list) if (p != null && p.key != null) dict[p.key] = p.value;
            return dict;
        }

        public static List<StringStringPair> ToPairs(Dictionary<string, string> dict)
        {
            var list = new List<StringStringPair>();
            if (dict == null) return list;
            foreach (var kv in dict) list.Add(new StringStringPair(kv.Key, kv.Value));
            return list;
        }

        public static Dictionary<string, string> ToDict(List<StringStringPair> list)
        {
            var dict = new Dictionary<string, string>();
            if (list == null) return dict;
            foreach (var p in list) if (p != null && p.key != null) dict[p.key] = p.value;
            return dict;
        }
    }

    public static class Ids
    {
        /// <summary>新しい一意ID（セーブ・ネットワーク越しでも衝突しない）。</summary>
        public static string New() => Guid.NewGuid().ToString("N");

        /// <summary>決定的なID（シードから生成する住民などに使う）。</summary>
        public static string Deterministic(ulong seed, string key) => StableHash.Derive(seed, key).ToString("x16");
    }
}
