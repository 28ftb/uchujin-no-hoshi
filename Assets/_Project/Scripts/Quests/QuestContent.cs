using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Quests
{
    /// <summary>クエスト固有のアイテムID（登録担当：Quests）。</summary>
    public static class QuestItemIds
    {
        /// <summary>青い花束（個別ストーリー「青い花」：花の世話をする住民が友達へ渡したい花）。</summary>
        public const string BlueBouquet = "quest.blue_bouquet";
        /// <summary>配達の包み（別の住民へ届ける）。</summary>
        public const string Parcel = "quest.parcel";
        /// <summary>変わった手料理（料理人を目指す住民の練習作）。</summary>
        public const string OddDish = "quest.odd_dish";
        /// <summary>手作りの料理（料理人を目指す住民の自信作）。</summary>
        public const string HomemadeDish = "quest.homemade_dish";
        /// <summary>子どもの描いた宇宙船の絵（旅に憧れる住民）。</summary>
        public const string ChildDrawing = "quest.child_drawing";
        /// <summary>引っ越す住民の荷物（運ぶ演出用・持ち物には入らない）。</summary>
        public const string MovingBox = "quest.moving_box";

        // 落とし物（持ち主は ItemStack.data で表す）
        public const string LostScarf = "quest.lost_scarf";
        public const string LostKey = "quest.lost_key";
        public const string LostToy = "quest.lost_toy";
        public const string LostGlasses = "quest.lost_glasses";
        public const string LostBell = "quest.lost_bell";

        public static readonly string[] LostItems = { LostScarf, LostKey, LostToy, LostGlasses, LostBell };

        /// <summary>持ち物の data に入れる接頭辞（持ち主・由来）。プレイヤーには表示しない。</summary>
        public const string DataLostPrefix = "lost:";
        public const string DataStoryPrefix = "story:";
        public const string DataParcelPrefix = "parcel:";

        public static bool IsLostItem(string id)
        {
            foreach (var l in LostItems) if (l == id) return true;
            return false;
        }
    }

    /// <summary>
    /// クエスト用アイテムの登録と、「この星で実際に入手可能な物」から依頼の対象を選ぶ処理（§4.2, §6.1, 言語 §5.2）。
    /// 他モジュールの物は契約インターフェース（釣り・鉱山・農業・生き物）と ItemRegistry から集める。
    /// </summary>
    public static class QuestCatalog
    {
        public static void RegisterItems()
        {
            Reg(QuestItemIds.BlueBouquet, "青い花束", "Blue bouquet", "誰かが大事そうに用意した青い花の束。", "A carefully prepared bunch of blue flowers.",
                ItemCategory.KeyItem, new Color(0.35f, 0.55f, 1f), PrimitiveType.Sphere, Concepts.FLOWER, ItemTags.Flower, ItemTags.ColorBlue);
            Reg(QuestItemIds.Parcel, "預かった包み", "Entrusted parcel", "住民から預かった包み。", "A parcel a resident entrusted to you.",
                ItemCategory.KeyItem, new Color(0.85f, 0.7f, 0.45f), PrimitiveType.Cube, Concepts.BOX);
            Reg(QuestItemIds.OddDish, "ふしぎな手料理", "Peculiar home cooking", "住民が練習で作った料理。不思議な色をしている。", "A practice dish made by a resident. It has an odd color.",
                ItemCategory.Food, new Color(0.6f, 0.75f, 0.35f), PrimitiveType.Cylinder, Concepts.DISH, ItemTags.Food, ItemTags.Edible);
            Reg(QuestItemIds.HomemadeDish, "手作りの料理", "Home-cooked dish", "住民が心を込めて作った料理。", "A dish a resident made with care.",
                ItemCategory.Food, new Color(1f, 0.7f, 0.4f), PrimitiveType.Cylinder, Concepts.DISH, ItemTags.Food, ItemTags.Edible, ItemTags.Savory);
            Reg(QuestItemIds.ChildDrawing, "宇宙船の絵", "Drawing of a spaceship", "小さな住民が描いた、空を飛ぶ宇宙船の絵。", "A little resident's drawing of a spaceship flying in the sky.",
                ItemCategory.Decor, new Color(0.95f, 0.95f, 1f), PrimitiveType.Quad, Concepts.PICTURE);
            Reg(QuestItemIds.MovingBox, "引っ越しの荷物", "Moving box", "住民の荷物。", "A resident's luggage.",
                ItemCategory.KeyItem, new Color(0.8f, 0.62f, 0.4f), PrimitiveType.Cube, Concepts.BOX);

            Reg(QuestItemIds.LostScarf, "誰かのマフラー", "Someone's scarf", "道に落ちていた。持ち主が探しているかもしれない。", "Found on the road. Someone may be looking for it.",
                ItemCategory.KeyItem, new Color(0.9f, 0.35f, 0.4f), PrimitiveType.Capsule, Concepts.CLOTHES, ItemTags.ColorRed);
            Reg(QuestItemIds.LostKey, "誰かの鍵", "Someone's key", "道に落ちていた。持ち主が探しているかもしれない。", "Found on the road. Someone may be looking for it.",
                ItemCategory.KeyItem, new Color(0.95f, 0.8f, 0.3f), PrimitiveType.Cylinder, Concepts.THING, ItemTags.ColorYellow, ItemTags.Metal);
            Reg(QuestItemIds.LostToy, "誰かのおもちゃ", "Someone's toy", "道に落ちていた。持ち主が探しているかもしれない。", "Found on the road. Someone may be looking for it.",
                ItemCategory.KeyItem, new Color(0.5f, 0.85f, 0.6f), PrimitiveType.Sphere, Concepts.THING, ItemTags.Toy, ItemTags.ColorGreen);
            Reg(QuestItemIds.LostGlasses, "誰かの眼鏡", "Someone's glasses", "道に落ちていた。持ち主が探しているかもしれない。", "Found on the road. Someone may be looking for it.",
                ItemCategory.KeyItem, new Color(0.6f, 0.6f, 0.75f), PrimitiveType.Cube, Concepts.THING);
            Reg(QuestItemIds.LostBell, "誰かの鈴", "Someone's bell", "道に落ちていた。振ると澄んだ音がする。", "Found on the road. It rings clearly when shaken.",
                ItemCategory.KeyItem, new Color(0.75f, 0.9f, 1f), PrimitiveType.Sphere, Concepts.THING, ItemTags.Music, ItemTags.Shiny);
        }

        private static void Reg(string id, string ja, string en, string dja, string den, ItemCategory cat, Color color, PrimitiveType shape, string generic, params string[] tags)
        {
            var def = new ItemDefinition
            {
                Id = id,
                NameJa = ja,
                NameEn = en,
                DescriptionJa = dja,
                DescriptionEn = den,
                Category = cat,
                BasePrice = cat == ItemCategory.Food ? 60 : 0,
                MaxStack = cat == ItemCategory.Food ? 10 : 1,
                Color = color,
                Shape = shape,
                GenericConceptId = generic,
                SourceActivity = "quest",
                Consumable = cat == ItemCategory.Food
            };
            def.WithTags(tags);
            if (cat != ItemCategory.Food) def.WithTags(ItemTags.NotSellable);
            ItemRegistry.Register(def);
        }

        // ------------------------------------------------------------
        // この星で入手可能な物（§4.2：住民の事情・この星の生態と矛盾しない対象）
        // ------------------------------------------------------------

        public static List<string> Fish()
        {
            var list = new List<string>();
            var s = Game.Instance?.Service<IFishingService>();
            if (s != null) foreach (var id in s.CatchableFishIds) if (ItemRegistry.Exists(id)) list.Add(id);
            return list;
        }

        /// <summary>そのプレイヤーが届く範囲の鉱石（到達階＋1階まで。未到達なら1〜2階）。</summary>
        public static List<string> Ores(string playerId, int extraFloors = 1)
        {
            var list = new List<string>();
            var s = Game.Instance?.Service<IMiningService>();
            if (s == null) return list;
            int deepest = Mathf.Max(1, s.GetDeepestFloorReached(playerId));
            int max = Mathf.Clamp(deepest + extraFloors, 2, Mathf.Max(1, s.FloorCount));
            for (int f = 1; f <= max; f++)
            {
                foreach (var id in s.OreIdsOnFloor(f))
                    if (ItemRegistry.Exists(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        /// <summary>育てられる作物（種のIDが返る場合は対応する作物IDへ読み替える）。</summary>
        public static List<string> Crops()
        {
            var list = new List<string>();
            var s = Game.Instance?.Service<IFarmService>();
            if (s == null) return list;
            foreach (var raw in s.PlantableCropIds)
            {
                string id = raw;
                var def = ItemRegistry.Get(id);
                if (def != null && def.Category == ItemCategory.Seed)
                {
                    string alt = id.StartsWith("seed.") ? "crop." + id.Substring(5) : null;
                    if (alt != null && ItemRegistry.Exists(alt)) id = alt;
                    else continue;
                }
                if (ItemRegistry.Exists(id) && !list.Contains(id)) list.Add(id);
            }
            return list;
        }

        public static List<string> Flowers()
        {
            var list = new List<string>();
            foreach (var id in Crops())
            {
                var def = ItemRegistry.Get(id);
                if (def != null && def.HasTag(ItemTags.Flower)) list.Add(id);
            }
            return list;
        }

        public static List<string> Dishes()
        {
            var list = new List<string>();
            foreach (var d in ItemRegistry.ByCategory(ItemCategory.Food))
                if (d.SourceActivity != "quest") list.Add(d.Id);
            foreach (var d in ItemRegistry.ByCategory(ItemCategory.Processed)) list.Add(d.Id);
            return list;
        }

        public static List<string> Species()
        {
            var list = new List<string>();
            var s = Game.Instance?.Service<ICreatureService>();
            if (s != null) foreach (var id in s.AllSpeciesIds) list.Add(id);
            return list;
        }

        public static List<string> ByCategory(ItemCategory cat, bool sellableOnly = true)
        {
            var list = new List<string>();
            foreach (var d in ItemRegistry.ByCategory(cat))
            {
                if (sellableOnly && d.HasTag(ItemTags.NotSellable)) continue;
                list.Add(d.Id);
            }
            return list;
        }

        /// <summary>依頼に使える「普通に手に入る物」全部（好みで選ぶ住民用）。</summary>
        public static List<string> AllObtainable(string playerId)
        {
            var list = new List<string>();
            list.AddRange(Fish());
            list.AddRange(Crops());
            list.AddRange(Ores(playerId));
            foreach (var d in Dishes()) if (!list.Contains(d)) list.Add(d);
            return list;
        }

        /// <summary>序盤は希少度の低い物を選びやすくする重み。</summary>
        public static float RarityWeight(ItemDefinition def, int day)
        {
            if (def == null) return 0f;
            int allowed = 1 + day / 6; // 6日ごとに希少度の上限が1上がる【仮】
            if (def.Rarity > allowed) return 0.05f;
            return 1f / (1f + def.Rarity * 0.5f);
        }

        /// <summary>住民の好み（内部の真実）を重みに。依頼の対象は住民の事情と矛盾させない（言語 §5.2）。</summary>
        public static string PickByPreference(IResidentInfo r, List<string> pool, SeededRandom rng, int day, float minLike = -0.2f)
        {
            if (pool == null || pool.Count == 0) return null;
            var w = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++)
            {
                var def = ItemRegistry.Get(pool[i]);
                if (def == null) continue;
                float like = r != null ? r.EvaluateItem(def) : 0f;
                if (like < minLike) continue;
                w[i] = RarityWeight(def, day) * (1.2f + like);
            }
            int idx = rng.WeightedIndex(w);
            return idx < 0 ? rng.Pick(pool) : pool[idx];
        }

        /// <summary>アイテムの色タグ（無ければ null）。</summary>
        public static string ColorTag(ItemDefinition def)
        {
            if (def == null) return null;
            foreach (var t in def.Tags) if (t.StartsWith("color_")) return t;
            return null;
        }

        public static string ColorConceptFromTag(string tag)
        {
            switch (tag)
            {
                case ItemTags.ColorRed: return Concepts.RED;
                case ItemTags.ColorBlue: return Concepts.BLUE;
                case ItemTags.ColorGreen: return Concepts.GREEN;
                case ItemTags.ColorYellow: return Concepts.YELLOW;
                case ItemTags.ColorWhite: return Concepts.WHITE;
                case ItemTags.ColorBlack: return Concepts.BLACK;
                case ItemTags.ColorPurple: return Concepts.PURPLE;
                case ItemTags.ColorOrange: return Concepts.ORANGE;
                case ItemTags.ColorPink: return Concepts.PINK;
                case ItemTags.Glowing: return Concepts.GLOWING;
                case ItemTags.Big: return Concepts.BIG;
                case ItemTags.Sweet: return Concepts.DELICIOUS;
                default: return null;
            }
        }

        /// <summary>カテゴリの総称概念。</summary>
        public static string CategoryConcept(ItemCategory c)
        {
            switch (c)
            {
                case ItemCategory.Fish: return Concepts.FISH;
                case ItemCategory.Crop: return Concepts.CROP;
                case ItemCategory.Ore: return Concepts.ORE;
                case ItemCategory.Gem: return Concepts.GEM;
                case ItemCategory.Food: return Concepts.DISH;
                case ItemCategory.Processed: return Concepts.FOOD;
                case ItemCategory.Furniture: return Concepts.FURNITURE;
                case ItemCategory.Seed: return Concepts.SEED;
                case ItemCategory.Material: return Concepts.METAL;
                default: return Concepts.THING;
            }
        }

        /// <summary>依頼の目標にその物が合うか（物・数・品質・大きさの判定）。</summary>
        public static bool Matches(QuestGoal g, ItemStack s)
        {
            if (g == null || s == null || s.IsEmpty) return false;
            var def = s.Definition;
            if (!string.IsNullOrEmpty(g.itemId))
            {
                if (s.itemId != g.itemId) return false;
            }
            else if (g.useCategory)
            {
                if (def.Category != g.category) return false;
            }
            if (!string.IsNullOrEmpty(g.tag) && !def.HasTag(g.tag)) return false;
            if (s.quality < g.minQuality) return false;
            if (g.minSize > 0f && s.size > 0f && s.size < g.minSize) return false;
            return true;
        }

        /// <summary>物は合っているが条件（品質・大きさ・色）が違う「惜しい」状態か。</summary>
        public static bool NearMiss(QuestGoal g, ItemStack s)
        {
            if (g == null || s == null || s.IsEmpty) return false;
            var def = s.Definition;
            bool sameThing = !string.IsNullOrEmpty(g.itemId) ? s.itemId == g.itemId : (g.useCategory && def.Category == g.category);
            return sameThing && !Matches(g, s);
        }

        /// <summary>住民の仕事に関わるお礼の品（あれば）。</summary>
        public static string RewardItemFor(ResidentJob job, SeededRandom rng)
        {
            List<string> pool = null;
            switch (job)
            {
                case ResidentJob.SeedSeller:
                case ResidentJob.FarmingMaster:
                case ResidentJob.Florist:
                    pool = ByCategory(ItemCategory.Seed); break;
                case ResidentJob.FishingMaster:
                    pool = ByCategory(ItemCategory.Bait); break;
                case ResidentJob.CreatureExpert:
                    pool = ByCategory(ItemCategory.CreatureFood); break;
                case ResidentJob.Cook:
                    pool = Dishes(); break;
                case ResidentJob.Carpenter:
                    pool = ByCategory(ItemCategory.Furniture);
                    if (pool.Count == 0) pool = ByCategory(ItemCategory.Decor);
                    break;
                case ResidentJob.Blacksmith:
                case ResidentJob.Miner:
                    pool = ByCategory(ItemCategory.Material); break;
            }
            if (pool == null || pool.Count == 0) pool = ByCategory(ItemCategory.Fertilizer);
            if (pool == null || pool.Count == 0) return null;
            return rng.Pick(pool);
        }
    }
}
