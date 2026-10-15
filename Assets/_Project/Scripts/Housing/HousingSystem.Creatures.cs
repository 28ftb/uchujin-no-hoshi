using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;
using Uchujin.Core.Visual;

namespace Uchujin.Housing
{
    /// <summary>
    /// 家の中・庭にいる宇宙生物の姿（家具仕様 §9, §10、生物仕様 §9）。
    /// 生物そのもの（能力・なつき度・捕獲）は Creatures モジュールが持つ。ここでは住処の家具と家の中での「かわいい行動」を見せる。
    /// 見た目だけの揺らぎなので UnityEngine.Random を使う（同期しない）。
    /// </summary>
    public class HomeCreature : MonoBehaviour, IInteractable
    {
        public HousingSystem System;
        public string Uid;
        public string SpeciesId;
        public string DisplayName;
        public List<CreatureAbility> Abilities = new List<CreatureAbility>();
        public string HabitatInstanceId;
        public bool Wild;
        public string Feature;
        /// <summary>"wander" "rest" "sleep" "hide" "orbit" "swim" "flutter" "perch" "roll" "tap" "watch" "gather" "stack"</summary>
        public string Mode = "wander";
        public Vector3 Target;
        public Transform Focus;
        public float NextThink;

        private Transform body;
        private Renderer bodyRenderer;
        private Color baseColor;
        private float seed;
        private float mood;

        public bool Has(CreatureAbility a) => Abilities.Contains(a);
        public string Number => FurnitureCatalog.SpeciesNumber(SpeciesId);

        public static HomeCreature Create(Transform parent, Vector3 pos, string uid, string speciesId, string name, IReadOnlyList<CreatureAbility> abilities, HousingSystem sys)
        {
            var go = new GameObject("HomeCreature_" + (uid ?? speciesId));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var hc = go.AddComponent<HomeCreature>();
            hc.System = sys;
            hc.Uid = uid;
            hc.SpeciesId = speciesId;
            hc.DisplayName = name;
            if (abilities != null) hc.Abilities.AddRange(abilities);
            hc.BuildVisual();
            var col = go.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 0.25f, 0);
            col.radius = 0.3f;
            col.isTrigger = true;
            hc.seed = UnityEngine.Random.Range(0f, 100f);
            hc.Target = pos;
            hc.NextThink = UnityEngine.Time.time + UnityEngine.Random.Range(0.5f, 2f);
            return hc;
        }

        private void BuildVisual()
        {
            ulong h = StableHash.Fnv1a64(SpeciesId ?? "creature");
            baseColor = Color.HSVToRGB((h % 1000) / 1000f, 0.45f, 0.95f);
            body = PrimitiveBuilder.Empty("Body", transform, Vector3.zero).transform;
            string n = Number;
            var shape = PrimitiveType.Sphere;
            Vector3 scale = new Vector3(0.38f, 0.32f, 0.38f);
            if (n == "031") { shape = PrimitiveType.Cube; scale = new Vector3(0.32f, 0.28f, 0.32f); }      // ハコネ：四角い
            else if (n == "037") scale = new Vector3(0.45f, 0.38f, 0.45f);                                   // モフピ：ふわふわ
            else if (n == "013") scale = Vector3.one * 0.33f;                                                // クルマリ：まんまる
            else if (Has(CreatureAbility.Float)) scale = new Vector3(0.3f, 0.3f, 0.3f);
            GameObject b = Has(CreatureAbility.Glow)
                ? PrimitiveBuilder.EmissivePart(body, shape, new Vector3(0, scale.y * 0.5f + 0.02f, 0), scale, baseColor, "Main")
                : PrimitiveBuilder.Part(body, shape, new Vector3(0, scale.y * 0.5f + 0.02f, 0), scale, baseColor, "Main");
            bodyRenderer = b.GetComponent<Renderer>();
            foreach (var sx in new[] { -1f, 1f })
            {
                PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(sx * 0.07f, scale.y * 0.62f, scale.z * 0.45f), Vector3.one * 0.06f, Color.white, "Eye");
                PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(sx * 0.07f, scale.y * 0.62f, scale.z * 0.5f), Vector3.one * 0.035f, new Color(0.1f, 0.1f, 0.15f), "Pupil");
            }
            if (n == "037" || n == "035") // 耳
                foreach (var sx in new[] { -1f, 1f })
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(sx * 0.12f, scale.y + 0.05f, 0), new Vector3(0.1f, 0.12f, 0.06f), Color.Lerp(baseColor, Color.white, 0.4f), "Ear");
            if (n == "033") // マネピ：くちばしと尾羽
            {
                PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, scale.y * 0.45f, scale.z * 0.55f), new Vector3(0.06f, 0.04f, 0.1f), new Color(1f, 0.75f, 0.3f), "Beak");
                PrimitiveBuilder.Part(body, PrimitiveType.Cube, new Vector3(0, scale.y * 0.6f, -scale.z * 0.55f), new Vector3(0.12f, 0.03f, 0.14f), Color.Lerp(baseColor, Color.black, 0.2f), "Tail", new Vector3(-30, 0, 0));
            }
            if (Has(CreatureAbility.Glow) || Has(CreatureAbility.Pollinate) || Has(CreatureAbility.Float)) // 羽
                foreach (var sx in new[] { -1f, 1f })
                    PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(sx * 0.2f, scale.y * 0.8f, -0.03f), new Vector3(0.16f, 0.03f, 0.1f), new Color(1f, 1f, 1f, 0.6f), "Wing");
            if (Has(CreatureAbility.Bubble) || Has(CreatureAbility.Water))
                PrimitiveBuilder.Part(body, PrimitiveType.Sphere, new Vector3(0.12f, scale.y + 0.08f, 0), Vector3.one * 0.07f, new Color(0.8f, 0.95f, 1f, 0.5f), "Bubble");
        }

        public void SetColor(Color c)
        {
            if (bodyRenderer == null) return;
            bodyRenderer.sharedMaterial = Has(CreatureAbility.Glow) ? MaterialCache.GetEmissive(c) : MaterialCache.Get(c);
        }

        public void ResetColor() => SetColor(baseColor);

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            float t = UnityEngine.Time.time + seed;
            if (System != null && UnityEngine.Time.time >= NextThink) System.ThinkCreature(this);

            Vector3 goal = Target;
            if (Mode == "orbit" && Focus != null)
            {
                float a = t * 1.6f;
                goal = Focus.position + new Vector3(Mathf.Cos(a) * 0.6f, 0.2f + Mathf.Sin(t * 2f) * 0.15f, Mathf.Sin(a) * 0.6f);
            }
            else if (Mode == "flutter" && Focus != null)
            {
                goal = Focus.position + new Vector3(Mathf.Sin(t * 1.3f) * 0.4f, 0.5f + Mathf.Sin(t * 3f) * 0.1f, Mathf.Cos(t) * 0.4f);
            }
            var d = goal - transform.position;
            float speed = (Mode == "orbit" || Mode == "flutter") ? 3f : Mode == "gather" ? 2.2f : 1.2f;
            if (d.magnitude > 0.05f)
            {
                transform.position += Vector3.ClampMagnitude(d, speed * dt);
                var flat = new Vector3(d.x, 0, d.z);
                if (flat.sqrMagnitude > 0.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), dt * 6f);
            }
            else if (Mode == "watch" && Focus != null)
            {
                var look = Focus.position - transform.position;
                look.y = 0;
                if (look.sqrMagnitude > 0.001f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look), dt * 4f);
            }

            if (body == null) return;
            Vector3 pos = Vector3.zero, scl = Vector3.one, eul = Vector3.zero;
            bool moving = d.magnitude > 0.05f;
            if (Has(CreatureAbility.Float) || Mode == "orbit" || Mode == "flutter") pos.y += 0.15f + Mathf.Sin(t * 2.5f) * 0.05f;
            switch (Mode)
            {
                case "sleep":
                case "rest":
                    if (!moving) { scl = new Vector3(1.1f, 0.75f + Mathf.Sin(t * 1.2f) * 0.04f, 1.1f); }
                    break;
                case "hide":
                    if (!moving) { scl = new Vector3(0.9f, 0.5f, 0.9f); pos.y -= 0.05f; }
                    break;
                case "roll":
                    eul.x = t * 360f;
                    break;
                case "tap":
                    pos.y += Mathf.Abs(Mathf.Sin(t * 8f)) * 0.06f;
                    break;
                case "swim":
                    pos.y += Mathf.Sin(t * 2f) * 0.04f;
                    eul.z = Mathf.Sin(t * 3f) * 10f;
                    break;
                default:
                    if (moving) pos.y += Mathf.Abs(Mathf.Sin(t * 10f)) * 0.05f;
                    break;
            }
            if (mood > 0f)
            {
                mood -= dt;
                pos.y += Mathf.Abs(Mathf.Sin(mood * 12f)) * 0.15f;
            }
            body.localPosition = pos;
            body.localScale = scl;
            body.localEulerAngles = eul;
        }

        public void Hop() => mood = 1.2f;

        public string GetInteractionPrompt(PlayerState player)
        {
            if (Wild) return Loc.Format("{0}をそっと観察する", "Quietly watch the {0}", DisplayName);
            return Loc.Format("{0}をなでる", "Pet {0}", DisplayName);
        }

        public bool CanInteract(PlayerState player) => System != null;
        public void Interact(PlayerState player) => System?.PetCreature(this, player);
    }

    public sealed partial class HousingSystem
    {
        private readonly Dictionary<string, HomeCreature> homeCreatures = new Dictionary<string, HomeCreature>();
        private Transform creaturesRoot;
        private bool refreshCreaturesSoon;
        private float creatureRefreshTimer;

        /// <summary>
        /// Creatures モジュールが描いている生物の姿（SceneRoot/OwnedCreatures/Creature_&lt;uid&gt;）を、
        /// 公開されている SendMessage（"UchujinCreatureAnchor" / "UchujinCreatureWork"）で家具へ誘導する。
        /// 姿が見つからない時だけ、このモジュールの簡易な姿（HomeCreature）を出す。
        /// </summary>
        private class CreatureDriver
        {
            public string Uid;
            public string SpeciesId;
            public List<CreatureAbility> Abilities = new List<CreatureAbility>();
            public string HabitatInstanceId;
            public Transform Avatar;
            public float NextThink;
        }

        private readonly Dictionary<string, CreatureDriver> drivers = new Dictionary<string, CreatureDriver>();

        private void TickCreatures(float dt)
        {
            if (!sceneBuilt) return;
            creatureRefreshTimer -= dt;
            if (refreshCreaturesSoon && creatureRefreshTimer <= 0f)
            {
                refreshCreaturesSoon = false;
                creatureRefreshTimer = 1f;
                RefreshHomeCreatures();
            }
            foreach (var d in drivers.Values)
            {
                if (d.Avatar == null) { refreshCreaturesSoon = true; continue; }
                if (UnityEngine.Time.time < d.NextThink) continue;
                d.NextThink = UnityEngine.Time.time + UnityEngine.Random.Range(8f, 16f);
                ThinkDriver(d);
            }
        }

        private Transform FindCreatureAvatar(string uid)
        {
            if (Game?.SceneRoot == null || string.IsNullOrEmpty(uid)) return null;
            return Game.SceneRoot.Find("OwnedCreatures/Creature_" + uid);
        }

        private void ThinkDriver(CreatureDriver d)
        {
            bool night = Clock != null && Clock.IsNight;
            FurnitureObject fo = null;
            if (!string.IsNullOrEmpty(d.HabitatInstanceId) && UnityEngine.Random.value < 0.55f) fo = GetFurnitureObject(d.HabitatInstanceId);
            if (fo == null) fo = PickCreatureFurniture(d.Avatar.position, d.SpeciesId, d.Abilities, night);
            if (fo == null) return;
            var spec = FurnitureCatalog.Resolve(fo.Def);
            var p = fo.transform.position + Vector3.up * Mathf.Min(Mathf.Max(0f, spec.height), 1.2f) * (spec.surface == FurnitureSurface.Ceiling ? 0f : 0.6f);
            bool work = spec.use == FurnitureUse.Music || spec.use == FurnitureUse.Toy || spec.style == FurnitureStyle.Rocks;
            d.Avatar.gameObject.SendMessage(work ? "UchujinCreatureWork" : "UchujinCreatureAnchor", p, SendMessageOptions.DontRequireReceiver);
        }

        private void EnsureCreatureRoot()
        {
            if (creaturesRoot == null && housingRoot != null) creaturesRoot = PrimitiveBuilder.Empty("HomeCreatures", housingRoot, Vector3.zero).transform;
        }

        private void ClearCreatureVisuals()
        {
            foreach (var c in homeCreatures.Values) if (c != null) UnityEngine.Object.Destroy(c.gameObject);
            homeCreatures.Clear();
            drivers.Clear();
            creaturesRoot = null;
        }

        /// <summary>家で暮らす・住処に住む生物（Creatures の状態から）。姿は Creatures 側の物を誘導し、無ければ簡易な姿を出す。</summary>
        private void RefreshHomeCreatures()
        {
            var creatures = Game?.Service<ICreatureService>();
            EnsureCreatureRoot();
            if (creatures == null || creaturesRoot == null) return;
            var owners = new HashSet<string> { HouseOwnerId };
            foreach (var p in Game.Players.All) owners.Add(p.PlayerId);
            var alive = new HashSet<string>();
            foreach (var owner in owners)
            {
                IReadOnlyList<CreatureView> owned = null;
                try { owned = creatures.GetOwned(owner); } catch (Exception e) { Debug.LogException(e); }
                if (owned == null) continue;
                foreach (var v in owned)
                {
                    PlacedFurniture habitat = null;
                    if (v.Placement == CreaturePlacement.Habitat)
                    {
                        habitat = FindFurniture(v.PlacementTargetId);
                        if (habitat == null || habitat.location == HousingConst.LocStored) continue;
                        if (IsAuthority && habitat.creatureUid != v.Uid) { habitat.creatureUid = v.Uid; MarkDirty(); }
                    }
                    else if (v.Placement != CreaturePlacement.Home) continue;
                    alive.Add(v.Uid);
                    IReadOnlyList<CreatureAbility> abilities = null;
                    try { abilities = creatures.GetSpeciesAbilities(v.SpeciesId); } catch (Exception e) { Debug.LogException(e); }

                    var avatar = FindCreatureAvatar(v.Uid);
                    if (avatar != null)
                    {
                        // Creatures の姿がある：二重に出さず、家具へ誘導する
                        if (homeCreatures.TryGetValue(v.Uid, out var fb) && fb != null) UnityEngine.Object.Destroy(fb.gameObject);
                        homeCreatures.Remove(v.Uid);
                        if (!drivers.TryGetValue(v.Uid, out var d))
                        {
                            d = new CreatureDriver { Uid = v.Uid, SpeciesId = v.SpeciesId, NextThink = UnityEngine.Time.time + UnityEngine.Random.Range(1f, 4f) };
                            if (abilities != null) d.Abilities.AddRange(abilities);
                            drivers[v.Uid] = d;
                        }
                        d.Avatar = avatar;
                        d.HabitatInstanceId = habitat?.instanceId;
                        continue;
                    }
                    drivers.Remove(v.Uid);
                    if (homeCreatures.TryGetValue(v.Uid, out var existing) && existing != null)
                    {
                        existing.HabitatInstanceId = habitat?.instanceId;
                        continue;
                    }
                    Vector3 pos = HouseInteriorSpawn + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(1f, 2.5f));
                    var fo = habitat != null ? GetFurnitureObject(habitat.instanceId) : null;
                    if (fo != null) pos = fo.transform.position + new Vector3(0.3f, 0f, 0.3f);
                    string name = !string.IsNullOrEmpty(v.Nickname) ? v.Nickname : SafeSpeciesName(creatures, v.SpeciesId);
                    var hc = HomeCreature.Create(creaturesRoot, pos, v.Uid, v.SpeciesId, name, abilities, this);
                    hc.HabitatInstanceId = habitat?.instanceId;
                    homeCreatures[v.Uid] = hc;
                    WorldLabels.Add(hc.transform, () => hc.DisplayName, new Vector3(0, 0.75f, 0), new Color(1f, 0.95f, 0.8f), 10f);
                }
            }
            var remove = new List<string>();
            foreach (var kv in homeCreatures) if (!alive.Contains(kv.Key)) remove.Add(kv.Key);
            foreach (var id in remove)
            {
                if (homeCreatures[id] != null) UnityEngine.Object.Destroy(homeCreatures[id].gameObject);
                homeCreatures.Remove(id);
            }
            var removeDrivers = new List<string>();
            foreach (var kv in drivers) if (!alive.Contains(kv.Key)) removeDrivers.Add(kv.Key);
            foreach (var id in removeDrivers) drivers.Remove(id);
        }

        private static string SafeSpeciesName(ICreatureService creatures, string speciesId)
        {
            try { return creatures.GetSpeciesName(speciesId) ?? speciesId; }
            catch (Exception e) { Debug.LogException(e); return speciesId; }
        }

        /// <summary>生き物の次の行動を決める（住処・能力・時間帯・家具）。</summary>
        public void ThinkCreature(HomeCreature c)
        {
            c.NextThink = UnityEngine.Time.time + UnityEngine.Random.Range(6f, 14f);
            if (c.Mode == "gather") c.Mode = "wander";
            c.ResetColor();
            if (c.Wild)
            {
                // 庭に来た野生の生き物は、来た場所のまわりで過ごす
                if (c.Focus != null && c.Mode != "orbit" && c.Mode != "flutter")
                    c.Target = c.Focus.position + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0.05f, UnityEngine.Random.Range(-0.8f, 0.8f));
                return;
            }
            bool night = Clock != null && Clock.IsNight;
            bool inGarden = !HouseGeometry.WorldToRoomCell(state, c.transform.position, out var room, out _, out _);

            // 住処へ帰る（半分くらいの確率）
            if (!string.IsNullOrEmpty(c.HabitatInstanceId) && UnityEngine.Random.value < 0.55f)
            {
                var fo = GetFurnitureObject(c.HabitatInstanceId);
                if (fo != null)
                {
                    GoUse(c, fo, true);
                    return;
                }
            }

            // ほかの生き物が箱に隠れていたら、それを眺める（§9.2）
            foreach (var other in homeCreatures.Values)
            {
                if (other == null || other == c || other.Mode != "hide") continue;
                if ((other.transform.position - c.transform.position).sqrMagnitude < 9f && UnityEngine.Random.value < 0.5f)
                {
                    c.Mode = "watch";
                    c.Focus = other.transform;
                    c.Target = other.transform.position + (c.transform.position - other.transform.position).normalized * 0.9f;
                    return;
                }
            }

            // 能力・時間帯に合う家具
            var best = PickCreatureFurniture(c.transform.position, c.SpeciesId, c.Abilities, night);
            if (best != null)
            {
                GoUse(c, best, false);
                return;
            }
            // ぶらぶら歩く
            c.Mode = "wander";
            c.Focus = null;
            if (!inGarden && room != null)
            {
                var o = HouseGeometry.RoomOrigin(state, room);
                c.Target = o + new Vector3(UnityEngine.Random.Range(0.5f, room.widthCells * HousingConst.Cell - 0.5f), 0.02f, UnityEngine.Random.Range(0.5f, room.depthCells * HousingConst.Cell - 0.5f));
            }
            else c.Target = c.transform.position + new Vector3(UnityEngine.Random.Range(-1.5f, 1.5f), 0f, UnityEngine.Random.Range(-1.5f, 1.5f));
        }

        /// <summary>生き物の能力・時間帯・種族に合う家具を選ぶ（同じ部屋／庭の中から）。</summary>
        private FurnitureObject PickCreatureFurniture(Vector3 pos, string speciesId, IList<CreatureAbility> abilities, bool night)
        {
            bool inGarden = !HouseGeometry.WorldToRoomCell(state, pos, out var room, out _, out _);
            string number = FurnitureCatalog.SpeciesNumber(speciesId);
            Func<CreatureAbility, bool> has = a => abilities != null && abilities.Contains(a);
            IReadOnlyList<CreatureAbility> abilityList = abilities as IReadOnlyList<CreatureAbility>;
            FurnitureObject best = null;
            float bestScore = 0.25f;
            foreach (var kv in furnitureObjects)
            {
                var fo = kv.Value;
                if (fo == null) continue;
                var f = FindFurniture(fo.InstanceId);
                if (f == null) continue;
                if (inGarden) { if (f.location != HousingConst.LocGarden) continue; }
                else if (f.location != HousingConst.LocHouse || room == null || f.room != room.index) continue;
                var spec = FurnitureCatalog.Resolve(fo.Def);
                float s = UnityEngine.Random.Range(0f, 0.3f);
                if (FurnitureCatalog.HabitatSuits(spec, speciesId, abilityList)) s += 0.6f;
                if (has(CreatureAbility.Glow) && spec.use == FurnitureUse.Light && f.lightOn) s += night ? 1.0f : 0.2f;
                if ((number == "035" || number == "037" || has(CreatureAbility.Heat)) && (spec.use == FurnitureUse.Sleep || spec.style == FurnitureStyle.Cushion || spec.style == FurnitureStyle.Nest)) s += night ? 0.9f : 0.5f;
                if (has(CreatureAbility.Pack) && (spec.style == FurnitureStyle.Box || spec.use == FurnitureUse.Storage)) s += 0.8f;
                if ((has(CreatureAbility.Water) || has(CreatureAbility.Bubble) || has(CreatureAbility.Splash)) && (spec.use == FurnitureUse.Aquarium || spec.style == FurnitureStyle.Pond)) s += 0.8f;
                if ((has(CreatureAbility.Pollinate) || has(CreatureAbility.Float)) && spec.use == FurnitureUse.Plant) s += night ? 0.2f : 0.8f;
                if (has(CreatureAbility.Stack) && (spec.style == FurnitureStyle.Rocks || spec.style == FurnitureStyle.Blocks || spec.style == FurnitureStyle.Figurine)) s += 0.7f;
                if ((has(CreatureAbility.Sound) || has(CreatureAbility.Mimic) || has(CreatureAbility.Echo)) && (spec.use == FurnitureUse.Music || spec.use == FurnitureUse.Perch)) s += 0.7f;
                if (has(CreatureAbility.ColorShift)) s += 0.35f;
                if (has(CreatureAbility.Grind) && spec.use == FurnitureUse.Toy) s += 0.7f;
                if (number == "039" && spec.height >= 1.8f) s += 0.8f;          // ヤネコロ：高い所
                if (number == "040" && spec.use == FurnitureUse.Window) s += 0.8f; // マルマド：窓辺
                if (s > bestScore) { bestScore = s; best = fo; }
            }
            return best;
        }

        private void GoUse(HomeCreature c, FurnitureObject fo, bool isHome)
        {
            var spec = FurnitureCatalog.Resolve(fo.Def);
            var p = fo.transform.position;
            c.Focus = fo.transform;
            float top = spec.surface == FurnitureSurface.Ceiling ? 0f : Mathf.Max(0.05f, spec.height);
            switch (spec.style)
            {
                case FurnitureStyle.Box:
                case FurnitureStyle.Chest:
                    c.Mode = "hide"; c.Target = p + Vector3.up * 0.05f; break;
                case FurnitureStyle.Lamp:
                case FurnitureStyle.FloorLamp:
                case FurnitureStyle.Streetlight:
                case FurnitureStyle.Chandelier:
                    c.Mode = "orbit"; c.Focus = fo.transform; break;
                case FurnitureStyle.Aquarium:
                case FurnitureStyle.WaterOrb:
                case FurnitureStyle.Pond:
                    c.Mode = "swim"; c.Target = p + Vector3.up * Mathf.Min(top * 0.6f, 0.9f); break;
                case FurnitureStyle.Plant:
                case FurnitureStyle.BigPlant:
                case FurnitureStyle.FlowerBed:
                case FurnitureStyle.Vase:
                case FurnitureStyle.Tree:
                    c.Mode = "flutter"; c.Focus = fo.transform; break;
                case FurnitureStyle.Shelf:
                case FurnitureStyle.Perch:
                case FurnitureStyle.Birdhouse:
                    c.Mode = "perch"; c.Target = p + Vector3.up * top; break;
                case FurnitureStyle.Playground:
                case FurnitureStyle.Ball:
                    c.Mode = "roll"; c.Target = p + new Vector3(UnityEngine.Random.Range(-0.5f, 0.5f), 0.05f, UnityEngine.Random.Range(-0.5f, 0.5f)); break;
                case FurnitureStyle.Drum:
                case FurnitureStyle.Xylophone:
                case FurnitureStyle.Piano:
                    c.Mode = "tap"; c.Target = p + Vector3.up * top; break;
                case FurnitureStyle.Rocks:
                case FurnitureStyle.Blocks:
                    c.Mode = "stack"; c.Target = p + Vector3.up * top; break;
                case FurnitureStyle.Window:
                case FurnitureStyle.RoundWindow:
                    c.Mode = "watch"; c.Target = new Vector3(p.x, 0.02f + HousingConst.HouseOrigin.y, p.z) + fo.transform.forward * 0.5f; break;
                case FurnitureStyle.Bed:
                case FurnitureStyle.Cushion:
                case FurnitureStyle.Nest:
                case FurnitureStyle.Sofa:
                {
                    // 同じ寝床に集まると、一緒に丸くなる（§9.2）
                    int n = 0;
                    foreach (var other in homeCreatures.Values) if (other != null && other != c && other.Focus == fo.transform && other.Mode == "sleep") n++;
                    c.Mode = "sleep";
                    c.Target = p + Vector3.up * Mathf.Min(top, 0.6f) + new Vector3((n % 3 - 1) * 0.25f, 0, (n / 3) * 0.25f);
                    break;
                }
                default:
                    c.Mode = "rest"; c.Target = p + Vector3.up * Mathf.Min(top, 1f) + new Vector3(0.2f, 0, 0.2f); break;
            }
            // カラポン：家具の色に合わせて体色が変わる
            if (c.Has(CreatureAbility.ColorShift)) c.SetColor(fo.Def.Color);
            // 別の階・庭など遠い場所は、ひょいと移る（壁や空間を横切らない）
            var tp = (c.Mode == "orbit" || c.Mode == "flutter") && c.Focus != null ? c.Focus.position : c.Target;
            if ((tp - c.transform.position).sqrMagnitude > 144f) c.transform.position = tp;
        }

        /// <summary>おもちゃ・音楽で生き物が集まる。集まった数を返す。</summary>
        public int AttractCreaturesTo(Vector3 pos, float radius, params CreatureAbility[] abilities)
        {
            int n = 0;
            foreach (var d in drivers.Values)
            {
                if (d.Avatar == null || (d.Avatar.position - pos).sqrMagnitude > radius * radius * 4f) continue;
                d.Avatar.gameObject.SendMessage("UchujinCreatureWork", pos, SendMessageOptions.DontRequireReceiver);
                d.NextThink = UnityEngine.Time.time + 10f;
                n++;
            }
            foreach (var c in homeCreatures.Values)
            {
                if (c == null) continue;
                if ((c.transform.position - pos).sqrMagnitude > radius * radius * 4f) continue;
                if (abilities != null && abilities.Length > 0)
                {
                    bool ok = false;
                    foreach (var a in abilities) if (c.Has(a)) ok = true;
                    if (!ok && UnityEngine.Random.value > 0.3f) continue;
                }
                c.Mode = "gather";
                c.Focus = null;
                c.Target = pos + new Vector3(UnityEngine.Random.Range(-0.8f, 0.8f), 0.02f, UnityEngine.Random.Range(-0.8f, 0.8f));
                c.NextThink = UnityEngine.Time.time + 8f;
                c.Hop();
                n++;
            }
            return n;
        }

        public void PetCreature(HomeCreature c, PlayerState player)
        {
            if (c == null || player == null) return;
            c.Hop();
            string behavior = (c.Wild ? "garden_" : "home_") + c.Mode;
            if (!string.IsNullOrEmpty(c.SpeciesId))
                EventBus.Publish(new CreatureObservedEvent { PlayerId = player.PlayerId, SpeciesId = c.SpeciesId, Behavior = behavior });
            if (c.Wild)
                UIManager.Toast(Loc.Format("{0}が{1}。", "The {0} is {1}.", c.DisplayName, ModeLabel(c.Mode)));
            else
                UIManager.Toast(Loc.Format("{0}はうれしそうに跳ねた。", "{0} hops happily.", c.DisplayName));
        }

        private static string ModeLabel(string mode)
        {
            switch (mode)
            {
                case "swim": return Loc.T("水浴びをしている", "bathing");
                case "flutter": return Loc.T("花のまわりを飛んでいる", "fluttering around the flowers");
                case "orbit": return Loc.T("灯りのまわりを飛んでいる", "circling the light");
                case "hide": return Loc.T("箱に入って隠れている", "hiding in the box");
                case "stack": return Loc.T("小石を積んでいる", "stacking pebbles");
                case "perch": return Loc.T("高い所で休んでいる", "resting up high");
                case "roll": return Loc.T("ころころ転がっている", "rolling around");
                case "tap": return Loc.T("リズムを刻んでいる", "tapping a rhythm");
                case "sleep": return Loc.T("気持ちよさそうに眠っている", "sleeping soundly");
                default: return Loc.T("あたりを歩き回っている", "wandering about");
            }
        }

        private static bool Contains(IReadOnlyList<CreatureAbility> list, CreatureAbility a)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == a) return true;
            return false;
        }
    }
}
