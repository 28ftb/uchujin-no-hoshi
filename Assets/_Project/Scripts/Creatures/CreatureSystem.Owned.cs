using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 飼っている生き物の見た目（同行・農場・自宅の庭・住処家具・商店・作業場）と、生物同士の交流（生き物仕様 §7）。
    /// 器具（Device）に入った個体の見た目は Cooking 側、商店・伝言広場のマネピは Tablet/Shop 側が出すのでここでは出さない。
    ///
    /// 他モジュールとの見た目の約束（クラスを直接参照しないための SendMessage）：
    ///   SceneRoot/OwnedCreatures/Creature_&lt;uid&gt; に生物がいる。
    ///   "UchujinCreatureAnchor"(Vector3) … 住み場所の位置を伝える（Farming が農場の住処の位置を渡す）
    ///   "UchujinCreatureWork"(Vector3)   … その場所へ行って働く仕草をする（Farming が作業した区画を渡す）
    /// </summary>
    public sealed partial class CreatureSystem
    {
        // 名人の技（生き物仕様 §5.4）
        public const string TechSneak = "sneak";     // 忍び足：気づかれにくい
        public const string TechNet = "net";         // 網さばき：捕まえやすい
        public const string TechBait = "bait";       // 餌の使い方：餌が長持ち・遠くまで匂う
        public const string TechObserve = "observe"; // 観察のコツ：擬態に気づきやすい

        private readonly Dictionary<string, OwnedCreatureAvatar> avatars = new Dictionary<string, OwnedCreatureAvatar>();
        private float pairTimer;
        private readonly Dictionary<string, float> pairCooldown = new Dictionary<string, float>();

        public OwnedCreatureAvatar GetAvatar(string uid)
        {
            if (uid != null && avatars.TryGetValue(uid, out var a)) return a;
            return null;
        }

        public IEnumerable<OwnedCreatureAvatar> Avatars => avatars.Values;

        public static string AvatarObjectName(string uid) => "Creature_" + uid;

        private bool ShouldRender(OwnedCreature c, bool isPlaced)
        {
            if (c == null) return false;
            if (!isPlaced)
            {
                if (LocalPlayer == null || c.ownerId != LocalPlayer.PlayerId) return false;
                return c.placement == CreaturePlacement.Companion;
            }
            switch (c.placement)
            {
                case CreaturePlacement.Farm:
                case CreaturePlacement.Home:
                case CreaturePlacement.Habitat:
                case CreaturePlacement.RocketWorkshop:
                    return true;
                case CreaturePlacement.Shop:
                case CreaturePlacement.MessagePlaza:
                    return c.speciesId != SharedItemIds.SpeciesManepi;
                default:
                    return false;
            }
        }

        public void RefreshOwnedAvatars()
        {
            if (!sceneBuilt) return;
            EnsureRoots();
            var want = new Dictionary<string, OwnedCreature>();
            foreach (var c in placed.Values) if (ShouldRender(c, true)) want[c.uid] = c;
            foreach (var c in carried.Values) if (!placed.ContainsKey(c.uid) && ShouldRender(c, false)) want[c.uid] = c;

            var remove = new List<string>();
            foreach (var kv in avatars) if (kv.Value == null || !want.ContainsKey(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (avatars.TryGetValue(id, out var a) && a != null) Object.Destroy(a.gameObject);
                avatars.Remove(id);
            }

            int index = 0;
            foreach (var c in want.Values)
            {
                var sp = CreatureDatabase.Get(c.speciesId);
                if (sp == null) continue;
                if (!avatars.TryGetValue(c.uid, out var av) || av == null || av.BuiltLevelStage != LevelStage(sp, c.level))
                {
                    if (av != null) Object.Destroy(av.gameObject);
                    av = OwnedCreatureAvatar.Create(this, c, sp, ownedRoot, AnchorFor(c, index), LevelStage(sp, c.level));
                    avatars[c.uid] = av;
                }
                av.Apply(c, AnchorFor(c, index), c.placement == CreaturePlacement.Companion && !placed.ContainsKey(c.uid), index);
                index++;
            }
        }

        /// <summary>見た目が大きく変わる成長段階（ニジマユは Lv3 で繭から羽へ）。</summary>
        private static int LevelStage(CreatureSpecies sp, int level) => sp.Shape == BodyShape.Cocoon && level >= 3 ? 1 : 0;

        private void RefreshAvatar(string uid, bool rebuild)
        {
            var av = GetAvatar(uid);
            var c = Find(uid);
            if (av == null || c == null) return;
            var sp = CreatureDatabase.Get(c.speciesId);
            if (sp == null) return;
            if (rebuild && av.BuiltLevelStage != LevelStage(sp, c.level))
            {
                RefreshOwnedAvatars();
                return;
            }
            av.Apply(c, AnchorFor(c, 0), av.IsCompanion, av.FollowIndex);
        }

        public Vector3 AnchorFor(OwnedCreature c, int index)
        {
            var layout = Game != null ? Game.Layout : null;
            ulong h = StableHash.Fnv1a64(c.uid ?? "x");
            float ang = (h % 360) * Mathf.Deg2Rad;
            float rad = 1.2f + (h >> 12) % 100 / 100f * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(ang) * rad, 0, Mathf.Sin(ang) * rad);
            Vector3 basePos = layout != null ? layout.PlayerSpawn : Vector3.zero;
            switch (c.placement)
            {
                case CreaturePlacement.Companion:
                    return LocalPlayer != null ? LocalPlayer.Position : basePos;
                case CreaturePlacement.Farm:
                    if (layout != null && layout.FarmRect.width > 0)
                    {
                        var r = layout.FarmRect;
                        float fx = r.xMin + 1f + (h % 1000) / 1000f * Mathf.Max(1f, r.width - 2f);
                        float fz = r.yMin + 1f + ((h >> 20) % 1000) / 1000f * Mathf.Max(1f, r.height - 2f);
                        return new Vector3(fx, 0, fz);
                    }
                    return basePos + offset;
                case CreaturePlacement.Home:
                    return HomeAnchor(layout, basePos) + offset;
                case CreaturePlacement.Habitat:
                    {
                        var housing = Game.Service<IHousingService>();
                        if (housing != null && !string.IsNullOrEmpty(c.targetId))
                        {
                            foreach (var f in housing.GetPlayerHomeFurniture(c.ownerId))
                                if (f != null && f.InstanceId == c.targetId) return f.Position + offset * 0.3f;
                        }
                        return HomeAnchor(layout, basePos) + offset;
                    }
                case CreaturePlacement.Shop:
                    {
                        var f = layout != null ? layout.GetFacility(FacilityType.PlayerShop) : null;
                        return (f != null ? f.entrance : basePos) + offset;
                    }
                case CreaturePlacement.MessagePlaza:
                    {
                        var f = layout != null ? layout.GetFacility(FacilityType.MessagePlaza) : null;
                        return (f != null ? f.position : basePos) + offset;
                    }
                case CreaturePlacement.RocketWorkshop:
                    {
                        var rocket = Game.Service<IRocketService>();
                        var p = rocket != null ? rocket.RocketPosition : layout != null ? layout.CrashSite : basePos;
                        return p + offset * 2f + new Vector3(3f, 0, 0);
                    }
            }
            return basePos + offset;
        }

        private Vector3 HomeAnchor(WorldLayout layout, Vector3 fallback)
        {
            var housing = Game.Service<IHousingService>();
            if (housing != null) return housing.PlayerHomeEntrance + new Vector3(0, 0, 2.5f);
            var f = layout != null ? layout.GetFacility(FacilityType.PlayerHouse) : null;
            return f != null ? f.entrance + new Vector3(0, 0, 2.5f) : fallback;
        }

        // ---------------- 生物同士の交流（生き物仕様 §7、家具仕様 §9.2） ----------------

        private void UpdatePairInteractions(float dt)
        {
            pairTimer -= dt;
            if (pairTimer > 0f) return;
            pairTimer = 4f;
            if (avatars.Count < 2) return;
            var local = LocalPlayer;
            float now = UnityEngine.Time.time;
            foreach (var pair in CreatureDatabase.Pairs)
            {
                foreach (var a in avatars.Values)
                {
                    if (a == null || a.SpeciesId != pair.A || a.InPairUntil > now) continue;
                    foreach (var b in avatars.Values)
                    {
                        if (b == null || b == a || b.SpeciesId != pair.B || b.InPairUntil > now) continue;
                        if ((a.transform.position - b.transform.position).sqrMagnitude > 4f * 4f) continue;
                        string key = pair.Key + ":" + a.Uid + ":" + b.Uid;
                        if (pairCooldown.TryGetValue(key, out var until) && until > now) continue;
                        pairCooldown[key] = now + 90f;
                        a.PlayPair(pair.Style, b, true);
                        b.PlayPair(pair.Style, a, false);
                        if (local != null && (a.transform.position - local.Position).sqrMagnitude < 12f * 12f)
                        {
                            RecordBehavior(pair.A, pair.Key, true);
                            RecordBehavior(pair.B, pair.Key, false);
                        }
                        break;
                    }
                }
            }
        }

        // ---------------- 繁殖（生き物仕様 §6.3【提案】） ----------------

        private void TryBreeding(int day)
        {
            if (placed.Count >= MaxPlacedPerPlanet) return;
            var rng = SeededRandom.For(Game.Planet.seed, "creatures.breed." + day);
            var list = new List<OwnedCreature>(placed.Values);
            var babies = new List<OwnedCreature>();
            for (int i = 0; i < list.Count; i++)
            {
                var a = list[i];
                if (a.affection < 60 || a.level < 2) continue;
                for (int j = i + 1; j < list.Count; j++)
                {
                    var b = list[j];
                    if (b.speciesId != a.speciesId || b.ownerId != a.ownerId || b.placement != a.placement || b.targetId != a.targetId) continue;
                    if (b.affection < 60 || b.level < 2) continue;
                    if (!rng.Chance(BreedChancePerDay)) continue;
                    var sp = CreatureDatabase.Get(a.speciesId);
                    if (sp == null || sp.Rarity >= 5) continue;
                    var baby = CreateIndividual(sp, a.ownerId, "bred:" + day + ":" + a.uid + ":" + b.uid, "bred", null);
                    // 親と違う色・模様が出ることがある
                    if (rng.Chance(0.25f)) baby.colorVariant = rng.Chance(0.2f) ? 3 : rng.Range(1, 3);
                    baby.affection = 40;
                    baby.size = Mathf.Min(baby.size, 0.9f);
                    baby.placement = a.placement;
                    baby.targetId = a.targetId;
                    baby.residentWorldId = a.residentWorldId;
                    babies.Add(baby);
                    break;
                }
                if (placed.Count + babies.Count >= MaxPlacedPerPlanet) break;
            }
            foreach (var baby in babies)
            {
                placed[baby.uid] = baby;
                if (LocalPlayer != null && baby.ownerId == LocalPlayer.PlayerId)
                {
                    Core.UI.UIManager.Toast(Loc.Format("{0}に赤ちゃんが生まれた！", "A baby {0} was born!", GetSpeciesName(baby.speciesId)), 5f);
                    var e = GetEntry(baby.speciesId, true);
                    e.obtainedCount++;
                }
            }
        }
    }
}
