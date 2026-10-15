using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Uchujin.Core
{
    [Serializable]
    public class SaveEntry
    {
        public string key;
        public string json;
    }

    [Serializable]
    public class PlanetSaveFile
    {
        public int formatVersion = 1;
        public PlanetInfo planet;
        public FlagStore flags = new FlagStore();
        public List<SaveEntry> entries = new List<SaveEntry>();
    }

    [Serializable]
    public class PlayerSaveFile
    {
        public int formatVersion = 1;
        public string playerId;
        public string worldId;
        /// <summary>PlayerProfile スコープ（持ち歩く資産）。</summary>
        public List<SaveEntry> profile = new List<SaveEntry>();
        /// <summary>PlayerPlanetKnowledge スコープ（この星についての個人の知識）。</summary>
        public List<SaveEntry> knowledge = new List<SaveEntry>();
    }

    [Serializable]
    public class SlotMeta
    {
        public string slotId;
        public string worldId;
        public string seedText;
        public string planetName;
        public string playerName;
        public int day;
        public int money;
        public int planetIndex;
        public long lastSavedTicks;
        public int repairedDevices;
        public bool launched;

        public string Describe()
        {
            var dt = new DateTime(lastSavedTicks, DateTimeKind.Utc).ToLocalTime();
            return Loc.Format("{0}　{1}日目　{2}　（星{3}・シード {4}）　{5:yyyy/MM/dd HH:mm}",
                "{0}  Day {1}  {2}  (planet {3}, seed {4})  {5:yyyy/MM/dd HH:mm}",
                string.IsNullOrEmpty(planetName) ? "?" : planetName, day, Wallet.Format(money), planetIndex, seedText, dt);
        }
    }

    /// <summary>
    /// セーブデータの読み書き。
    ///   Saves/&lt;slot&gt;/meta.json, planet.json, player_&lt;id&gt;.json
    ///   Saves/_visits/&lt;worldId&gt;/knowledge_&lt;id&gt;.json（他人の星を訪問した時の自分の辞書など）
    /// 星の宇宙語辞書は星ごとに分けて保存し、混同しない（基本決定 v0.2 §5）。
    /// </summary>
    public sealed class SaveSystem
    {
        public string Root => Path.Combine(Application.persistentDataPath, "Saves");

        private string SlotDir(string slotId) => Path.Combine(Root, slotId);

        public List<SlotMeta> ListSlots()
        {
            var list = new List<SlotMeta>();
            if (!Directory.Exists(Root)) return list;
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith("_")) continue;
                var metaPath = Path.Combine(dir, "meta.json");
                if (!File.Exists(metaPath)) continue;
                try
                {
                    var meta = JsonUtility.FromJson<SlotMeta>(File.ReadAllText(metaPath));
                    if (meta != null) list.Add(meta);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"セーブ情報の読み込みに失敗: {metaPath}\n{e}");
                }
            }
            list.Sort((a, b) => b.lastSavedTicks.CompareTo(a.lastSavedTicks));
            return list;
        }

        public void WriteSlot(SlotMeta meta, PlanetSaveFile planet, PlayerSaveFile player)
        {
            var dir = SlotDir(meta.slotId);
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "planet.json"), JsonUtility.ToJson(planet));
            if (player != null) WriteAtomic(Path.Combine(dir, $"player_{player.playerId}.json"), JsonUtility.ToJson(player));
            WriteAtomic(Path.Combine(dir, "meta.json"), JsonUtility.ToJson(meta, true));
        }

        public SlotMeta ReadMeta(string slotId)
        {
            var path = Path.Combine(SlotDir(slotId), "meta.json");
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<SlotMeta>(File.ReadAllText(path));
        }

        public void WriteMeta(SlotMeta meta)
        {
            if (meta == null || string.IsNullOrEmpty(meta.slotId)) return;
            var dir = SlotDir(meta.slotId);
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, "meta.json"), JsonUtility.ToJson(meta, true));
        }

        public bool SlotExists(string slotId) => File.Exists(Path.Combine(SlotDir(slotId), "planet.json"));

        public PlanetSaveFile ReadPlanet(string slotId)
        {
            var path = Path.Combine(SlotDir(slotId), "planet.json");
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<PlanetSaveFile>(File.ReadAllText(path));
        }

        public PlayerSaveFile ReadPlayer(string slotId, string playerId)
        {
            var path = Path.Combine(SlotDir(slotId), $"player_{playerId}.json");
            if (!File.Exists(path)) return null;
            return JsonUtility.FromJson<PlayerSaveFile>(File.ReadAllText(path));
        }

        /// <summary>プレイヤーファイルだけを書き換える（他人の星から戻った時に持ち物を保存する等）。</summary>
        public void WritePlayer(string slotId, PlayerSaveFile player)
        {
            if (player == null) return;
            var dir = SlotDir(slotId);
            Directory.CreateDirectory(dir);
            WriteAtomic(Path.Combine(dir, $"player_{player.playerId}.json"), JsonUtility.ToJson(player));
        }

        public void DeleteSlot(string slotId)
        {
            var dir = SlotDir(slotId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        public void WriteVisitKnowledge(string worldId, string playerId, List<SaveEntry> knowledge)
        {
            var dir = Path.Combine(Root, "_visits", worldId);
            Directory.CreateDirectory(dir);
            var file = new PlayerSaveFile { playerId = playerId, worldId = worldId, knowledge = knowledge };
            WriteAtomic(Path.Combine(dir, $"knowledge_{playerId}.json"), JsonUtility.ToJson(file));
        }

        public List<SaveEntry> ReadVisitKnowledge(string worldId, string playerId)
        {
            var path = Path.Combine(Root, "_visits", worldId, $"knowledge_{playerId}.json");
            if (!File.Exists(path)) return new List<SaveEntry>();
            var f = JsonUtility.FromJson<PlayerSaveFile>(File.ReadAllText(path));
            return f?.knowledge ?? new List<SaveEntry>();
        }

        private static void WriteAtomic(string path, string content)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public static string FindEntry(List<SaveEntry> entries, string key)
        {
            if (entries == null) return null;
            foreach (var e in entries) if (e.key == key) return e.json;
            return null;
        }
    }
}
