using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Mining
{
    /// <summary>その日の採掘ノード1つ（シード＋日付＋階から決定的に生成）。</summary>
    public sealed class MineNodeData
    {
        public int Floor;
        public int Index;
        public MineNodeKind Kind;
        public string ItemId;   // 岩は null（壊すと石＋まれに発見物）
        public int Hardness;
        public float Hp;
        public Vector3 Position;
        public ulong Seed;
        public float Scale;
    }

    /// <summary>採掘で手に入った物（ホスト→発行者へ結果として返す）。</summary>
    public struct MineDrop
    {
        public string ItemId;
        public int Count;
        public int Quality;
        public float Size;
    }

    /// <summary>
    /// 日替わりの鉱石配置（鉱山仕様 §3：位置・種類・数量が日によって変わる。最低保証は設けない）。
    /// 「今日は○○が多い」「宝石が多い日」も確率で起きる（§3.1）。
    /// </summary>
    public static class MineNodeGenerator
    {
        public static List<MineNodeData> Generate(ulong worldSeed, int day, MineFloorLayout layout, out string favoredOre, out bool gemDayOut)
        {
            int floor = layout.FloorNumber;
            var rng = SeededRandom.For(worldSeed, "mine.ores.d" + day + ".f" + floor);
            var result = new List<MineNodeData>();
            var slots = new List<Vector2Int>(layout.NodeSlots);
            rng.Shuffle(slots);
            int count = Mathf.Min(slots.Count, MiningTuning.NodesBase + floor * MiningTuning.NodesPerFloor + rng.Range(0, MiningTuning.NodesRandom + 1));

            var ores = new List<MineMaterial>(MineCatalog.OnFloor(floor, MineNodeKind.Ore));
            var gems = new List<MineMaterial>(MineCatalog.OnFloor(floor, MineNodeKind.Gem));
            var forage = new List<MineMaterial>(MineCatalog.OnFloor(floor, MineNodeKind.Forage));
            var favored = ores.Count > 0 ? rng.Pick(ores) : null;
            bool gemDay = rng.Chance(MiningTuning.GemDayChance);
            favoredOre = favored != null ? favored.Id : null;
            gemDayOut = gemDay;

            for (int i = 0; i < count; i++)
            {
                var cell = slots[i];
                var node = new MineNodeData
                {
                    Floor = floor,
                    Index = i,
                    Seed = StableHash.Derive(worldSeed, "mine.node.d" + day + ".f" + floor, i)
                };
                float r = rng.NextFloat();
                float gemShare = MiningTuning.GemShareBase * (gemDay ? MiningTuning.GemDayMultiplier : 1f);
                if (r < MiningTuning.RockShare)
                {
                    node.Kind = MineNodeKind.Rock;
                    node.Hardness = MiningTuning.RockHardness(floor);
                }
                else if (r < MiningTuning.RockShare + MiningTuning.ForageShare && forage.Count > 0)
                {
                    node.Kind = MineNodeKind.Forage;
                    node.ItemId = rng.Pick(forage).Id;
                    node.Hardness = 0;
                }
                else if (gems.Count > 0 && rng.Chance(gemShare))
                {
                    var g = rng.WeightedPick(gems, m => m.Weight);
                    node.Kind = MineNodeKind.Gem;
                    node.ItemId = g.Id;
                    node.Hardness = g.Hardness;
                }
                else if (ores.Count > 0)
                {
                    var o = rng.WeightedPick(ores, m => m.Weight * (m == favored ? MiningTuning.FavoredOreMultiplier : 1f));
                    node.Kind = MineNodeKind.Ore;
                    node.ItemId = o.Id;
                    node.Hardness = o.Hardness;
                }
                else
                {
                    node.Kind = MineNodeKind.Rock;
                    node.Hardness = MiningTuning.RockHardness(floor);
                }
                node.Hp = MiningTuning.NodeHp(node.Hardness, node.Kind);
                var jitter = rng.InsideUnitCircle() * MiningTuning.Cell * 0.22f;
                node.Position = layout.CellCenter(cell) + new Vector3(jitter.x, 0f, jitter.y);
                node.Scale = rng.Range(0.85f, 1.15f);
                result.Add(node);
            }
            return result;
        }

        /// <summary>壊した時に出る物（ノードごとに決定的。誰が壊しても同じ）。</summary>
        public static List<MineDrop> RollDrops(MineNodeData node, int miningLevel)
        {
            var drops = new List<MineDrop>();
            var rng = new SeededRandom(node.Seed ^ 0xA5A5A5A55A5A5A5AUL);
            float lv = Mathf.Max(0, miningLevel - 1);
            switch (node.Kind)
            {
                case MineNodeKind.Ore:
                {
                    int count = 1 + (rng.Chance(MiningTuning.LevelBonusDropChance * lv + 0.05f) ? 1 : 0);
                    int q = rng.Chance(0.12f + 0.02f * lv) ? 1 : 0;
                    drops.Add(new MineDrop { ItemId = node.ItemId, Count = count, Quality = q });
                    break;
                }
                case MineNodeKind.Gem:
                    drops.Add(RollGem(MineCatalog.Get(node.ItemId), rng, lv));
                    break;
                case MineNodeKind.Forage:
                    drops.Add(new MineDrop { ItemId = node.ItemId, Count = rng.Chance(0.3f) ? 2 : 1 });
                    break;
                default:
                {
                    drops.Add(new MineDrop { ItemId = MineCatalog.Stone, Count = rng.Chance(0.4f) ? 2 : 1 });
                    if (rng.Chance(0.12f + 0.01f * lv))
                    {
                        var finds = new List<MineMaterial>(MineCatalog.RockFindsOnFloor(node.Floor));
                        if (finds.Count > 0)
                        {
                            var f = rng.WeightedPick(finds, m => m.Weight);
                            drops.Add(new MineDrop { ItemId = f.Id, Count = 1, Quality = rng.Chance(0.2f) ? 1 : 0 });
                        }
                    }
                    else if (rng.Chance(0.05f + 0.005f * lv))
                    {
                        var gems = new List<MineMaterial>(MineCatalog.OnFloor(node.Floor, MineNodeKind.Gem));
                        if (gems.Count > 0) drops.Add(RollGem(rng.WeightedPick(gems, m => m.Weight), rng, lv));
                    }
                    break;
                }
            }
            return drops;
        }

        /// <summary>宝石の個体差（鉱山仕様 §6.1：大きさ・透明度）。大きさは ItemStack.size、透明度は品質に。</summary>
        private static MineDrop RollGem(MineMaterial g, SeededRandom rng, float lv)
        {
            if (g == null) return new MineDrop { ItemId = MineCatalog.Stone, Count = 1 };
            float u = Mathf.Pow(rng.NextFloat(), 1.8f - 0.04f * lv);
            float size = Mathf.Round(Mathf.Lerp(g.SizeMin, g.SizeMax, u) * 10f) / 10f;
            float clarity = rng.NextFloat() + MiningTuning.LevelGemQualityBonus * lv;
            int q = clarity > 0.97f ? 3 : clarity > 0.82f ? 2 : clarity > 0.5f ? 1 : 0;
            return new MineDrop { ItemId = g.Id, Count = 1, Quality = q, Size = size };
        }

        // ---- 結果の受け渡し（CommandResult.MessageEn に入れる。表示はされない）----

        public const string PayloadPrefix = "mined:";

        public static string Encode(List<MineDrop> drops)
        {
            var sb = new StringBuilder(PayloadPrefix);
            for (int i = 0; i < drops.Count; i++)
            {
                if (i > 0) sb.Append(';');
                var d = drops[i];
                sb.Append(d.ItemId).Append('|').Append(d.Count).Append('|').Append(d.Quality).Append('|')
                    .Append(d.Size.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        public static List<MineDrop> Decode(string payload)
        {
            var list = new List<MineDrop>();
            if (string.IsNullOrEmpty(payload) || !payload.StartsWith(PayloadPrefix)) return list;
            foreach (var part in payload.Substring(PayloadPrefix.Length).Split(';'))
            {
                var f = part.Split('|');
                if (f.Length < 4) continue;
                var d = new MineDrop { ItemId = f[0] };
                int.TryParse(f[1], out d.Count);
                int.TryParse(f[2], out d.Quality);
                float.TryParse(f[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d.Size);
                list.Add(d);
            }
            return list;
        }
    }

    // =====================================================================
    // 表示：ノード（IToolTarget：ツルハシで叩く）
    // =====================================================================

    public class MineNodeView : MonoBehaviour, IToolTarget
    {
        public int floor;
        public int index;
        public int day;
        public MineNodeKind kind;
        public bool vanishing;

        private MiningSystem system;
        private Transform visual;
        private float appear = 1f;
        private float vanish = -1f;
        private float shake;
        private bool broken;
        private GameObject glow;

        public static MineNodeView Create(MiningSystem system, Transform parent, MineNodeData data, int day, bool animateAppear)
        {
            var go = new GameObject("MineNode_" + data.Floor + "_" + data.Index);
            go.transform.SetParent(parent, false);
            go.transform.position = data.Position;
            var view = go.AddComponent<MineNodeView>();
            view.system = system;
            view.floor = data.Floor;
            view.index = data.Index;
            view.day = day;
            view.kind = data.Kind;
            // 当たり判定はトリガー（目の前に出現してもプレイヤーが挟まらない：時間天候仕様 §4.3 の衝突処理）
            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.9f, 0);
            col.size = new Vector3(1.3f, 1.8f, 1.3f);
            view.visual = PrimitiveBuilder.Empty("Visual", go.transform, Vector3.zero).transform;
            BuildVisual(view.visual, data);
            view.visual.localScale = Vector3.one * data.Scale;
            if (data.Kind == MineNodeKind.Forage)
            {
                var pick = go.AddComponent<MineForagePickup>();
                pick.view = view;
            }
            if (animateAppear)
            {
                view.appear = 0f;
                view.visual.localScale = Vector3.zero;
                view.glow = PrimitiveBuilder.EmissivePart(go.transform, PrimitiveType.Sphere, new Vector3(0, 0.6f, 0), Vector3.one * 1.6f, new Color(0.7f, 0.95f, 1f, 0.35f), "AppearGlow");
            }
            return view;
        }

        private static void BuildVisual(Transform t, MineNodeData d)
        {
            var def = d.ItemId != null ? ItemRegistry.GetOrPlaceholder(d.ItemId) : null;
            Color rock = Color.Lerp(new Color(0.55f, 0.5f, 0.45f), new Color(0.35f, 0.35f, 0.45f), (d.Floor - 1) / 9f);
            switch (d.Kind)
            {
                case MineNodeKind.Rock:
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.45f, 0), new Vector3(1.1f, 0.85f, 1f), rock, "Rock");
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0.3f, 0.25f, 0.2f), new Vector3(0.6f, 0.5f, 0.6f), rock * 0.9f, "Rock2");
                    break;
                case MineNodeKind.Ore:
                {
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.45f, 0), new Vector3(1.05f, 0.85f, 0.95f), rock * 0.95f, "Base");
                    Color c = def != null ? def.Color : Color.yellow;
                    bool glowy = def != null && (def.HasTag(ItemTags.Glowing) || def.HasTag(ItemTags.Crystal));
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * 90f + 20f;
                        var p = Quaternion.Euler(0, a, 0) * new Vector3(0.38f, 0.55f + 0.08f * (i % 2), 0f);
                        var chunk = glowy
                            ? PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, p, Vector3.one * 0.26f, c, "Ore" + i)
                            : PrimitiveBuilder.Part(t, PrimitiveType.Cube, p, Vector3.one * 0.26f, c, "Ore" + i);
                        chunk.transform.localEulerAngles = new Vector3(30 + i * 10, a, 15);
                    }
                    break;
                }
                case MineNodeKind.Gem:
                {
                    PrimitiveBuilder.Part(t, PrimitiveType.Sphere, new Vector3(0, 0.4f, 0), new Vector3(0.95f, 0.75f, 0.9f), rock * 0.85f, "Base");
                    Color c = def != null ? def.Color : Color.cyan;
                    var g = PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, new Vector3(0, 0.95f, 0), new Vector3(0.28f, 0.45f, 0.28f), c, "Gem");
                    g.transform.localEulerAngles = new Vector3(0, 45, 20);
                    var g2 = PrimitiveBuilder.EmissivePart(t, PrimitiveType.Cube, new Vector3(0.2f, 0.75f, 0.1f), new Vector3(0.18f, 0.3f, 0.18f), c, "Gem2");
                    g2.transform.localEulerAngles = new Vector3(0, 10, -25);
                    break;
                }
                default:
                {
                    Color c = def != null ? def.Color : Color.green;
                    for (int i = 0; i < 3; i++)
                    {
                        var p = new Vector3(-0.25f + i * 0.25f, 0f, (i % 2) * 0.2f);
                        PrimitiveBuilder.Part(t, PrimitiveType.Cylinder, p + new Vector3(0, 0.15f, 0), new Vector3(0.06f, 0.15f, 0.06f), new Color(0.9f, 0.9f, 0.85f), "Stem" + i);
                        PrimitiveBuilder.EmissivePart(t, PrimitiveType.Sphere, p + new Vector3(0, 0.32f, 0), new Vector3(0.28f, 0.14f, 0.28f), c, "Cap" + i);
                    }
                    break;
                }
            }
        }

        public bool OnToolUsed(ToolUseContext ctx)
        {
            if (system == null || ctx == null) return false;
            if (kind == MineNodeKind.Forage)
            {
                system.HitNode(this, ctx.Player);
                return true;
            }
            if (ctx.Tool != ToolType.Pickaxe) return false;
            system.HitNode(this, ctx.Player);
            return true;
        }

        public void PlayHit()
        {
            shake = 0.2f;
            if (visual != null) MineFx.Burst(transform.position + Vector3.up * 0.7f, 3, new Color(0.7f, 0.65f, 0.6f), 0.08f);
        }

        public void PlayClank()
        {
            shake = 0.08f;
            MineFx.Burst(transform.position + Vector3.up * 0.9f, 4, new Color(1f, 0.95f, 0.6f), 0.05f, true);
        }

        /// <summary>壊れた（採掘成功）。</summary>
        public void Break(Color color)
        {
            if (broken) return;
            broken = true;
            vanishing = true;
            MineFx.Burst(transform.position + Vector3.up * 0.6f, 10, color, 0.14f);
            Destroy(gameObject, 0.05f);
        }

        /// <summary>日付更新で消える（光って消失）。</summary>
        public void Vanish()
        {
            if (vanishing) return;
            vanishing = true;
            vanish = 0f;
            if (glow == null) glow = PrimitiveBuilder.EmissivePart(transform, PrimitiveType.Sphere, new Vector3(0, 0.6f, 0), Vector3.one * 0.4f, new Color(0.8f, 0.95f, 1f, 0.4f), "VanishGlow");
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            if (visual == null) return;
            if (appear < 1f)
            {
                appear = Mathf.Min(1f, appear + dt / 0.7f);
                float s = Mathf.SmoothStep(0f, 1f, appear) * (1f + 0.15f * Mathf.Sin(appear * Mathf.PI));
                visual.localScale = Vector3.one * s;
                if (glow != null)
                {
                    glow.transform.localScale = Vector3.one * (1.6f * (1f - appear) + 0.1f);
                    if (appear >= 1f) { Destroy(glow); glow = null; }
                }
            }
            if (vanish >= 0f)
            {
                vanish += dt / 0.6f;
                visual.localScale = Vector3.one * Mathf.Max(0f, 1f - vanish);
                if (glow != null) glow.transform.localScale = Vector3.one * (0.4f + vanish * 1.8f);
                if (vanish >= 1f)
                {
                    MineFx.Burst(transform.position + Vector3.up * 0.5f, 5, new Color(0.8f, 0.95f, 1f), 0.06f, true);
                    Destroy(gameObject);
                }
            }
            if (shake > 0f)
            {
                shake -= dt;
                visual.localPosition = new Vector3(Mathf.Sin(UnityEngine.Time.time * 80f) * 0.05f, 0f, 0f);
                if (shake <= 0f) visual.localPosition = Vector3.zero;
            }
        }
    }

    /// <summary>洞窟キノコなどの採集物（[E] で拾う）。</summary>
    public class MineForagePickup : MonoBehaviour, IInteractable
    {
        public MineNodeView view;

        public string GetInteractionPrompt(PlayerState player) => Loc.T("摘む", "Pick");
        public bool CanInteract(PlayerState player) => view != null && !view.vanishing;

        public void Interact(PlayerState player)
        {
            if (view == null) return;
            view.OnToolUsed(new ToolUseContext { Player = player, Tool = ToolType.Hand });
        }
    }

    /// <summary>小さな破片・きらめき（見た目だけ。UnityEngine.Random 可）。</summary>
    public class MineFx : MonoBehaviour
    {
        private Vector3 velocity;
        private float life;
        private bool floaty;

        public static void Burst(Vector3 at, int count, Color color, float size, bool sparkle = false)
        {
            var parent = Game.Instance != null ? Game.Instance.SceneRoot : null;
            for (int i = 0; i < count; i++)
            {
                var go = sparkle
                    ? PrimitiveBuilder.EmissivePart(parent, PrimitiveType.Cube, Vector3.zero, Vector3.one * size, color, "Spark")
                    : PrimitiveBuilder.Part(parent, PrimitiveType.Cube, Vector3.zero, Vector3.one * size, color, "Chip");
                go.transform.position = at;
                var fx = go.AddComponent<MineFx>();
                var r = Random.insideUnitSphere;
                fx.velocity = sparkle ? r * 1.2f + Vector3.up * 0.8f : new Vector3(r.x * 2.5f, Random.Range(2f, 4f), r.z * 2.5f);
                fx.life = sparkle ? 0.8f : 0.6f;
                fx.floaty = sparkle;
            }
        }

        private void Update()
        {
            float dt = UnityEngine.Time.deltaTime;
            life -= dt;
            if (!floaty) velocity += Vector3.down * 12f * dt;
            transform.position += velocity * dt;
            transform.Rotate(200f * dt, 150f * dt, 0f);
            if (floaty) transform.localScale *= 1f - dt * 1.2f;
            if (life <= 0f) Destroy(gameObject);
        }
    }
}
