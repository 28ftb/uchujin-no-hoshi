using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 宇宙生物50種（生き物仕様 §3 の No.001〜050）の種族テーブル。名前・習性・発見場所・捕獲の手掛かりは仕様から。
    /// 能力の割り当て：
    ///   ・農業10種は農業仕様 §3.2 / 生き物仕様 §8.1 のとおり。
    ///   ・Grind/Sound/Bubble/Splash/Pack/Mimic/ColorShift/Stack/MineralDust/Echo/Stick/Shade/Float は料理仕様 §3.1 と Core の CreatureAbility の注記どおり。
    ///   ・圧搾 Press・発酵 Ferment・冷却 Cool・安定化 Stabilize・感知 Sense は仕様上【未決定】のため、
    ///     既存50種の特徴から妥当な種に割り当てた【提案】（HasProposedAbility=true。README に一覧）。
    ///   ・全種を労働要員にしない：ハッパリ・ピョコロ・モリマル・ナミパタ・シオリボ・モフピ・ヤネコロ・マルマド・ニジマユは能力なし（観察・かわいさ重視）。
    ///   ・ホシノコは象徴的な希少種。修理の必須能力（Stabilize 等）は持たせない。
    /// 数値（希少度・価格・色・大きさ）はすべて【仮数値】。
    /// </summary>
    public static class CreatureDatabase
    {
        private static readonly List<CreatureSpecies> all = new List<CreatureSpecies>();
        private static readonly Dictionary<string, CreatureSpecies> byId = new Dictionary<string, CreatureSpecies>();
        private static readonly Dictionary<string, BehaviorFact> facts = new Dictionary<string, BehaviorFact>();
        private static readonly List<PairInteraction> pairs = new List<PairInteraction>();

        public static IReadOnlyList<CreatureSpecies> All { get { EnsureBuilt(); return all; } }
        public static IReadOnlyList<PairInteraction> Pairs { get { EnsureBuilt(); return pairs; } }

        public static CreatureSpecies Get(string id)
        {
            EnsureBuilt();
            if (id != null && byId.TryGetValue(id, out var s)) return s;
            return null;
        }

        public static BehaviorFact GetFact(string key)
        {
            EnsureBuilt();
            if (key != null && facts.TryGetValue(key, out var f)) return f;
            return null;
        }

        public static string MakeId(int no, string key) => $"creature.{no:000}_{key}";

        private static bool built;

        private static void EnsureBuilt()
        {
            if (built) return;
            built = true;
            BuildSpecies();
            BuildFacts();
            BuildPairs();
        }

        private static Color C(float r, float g, float b, float a = 1f) => new Color(r, g, b, a);

        private static CreatureSpecies Add(int no, string key, string ja, string en, CreatureGroup g)
        {
            var s = new CreatureSpecies { No = no, Id = MakeId(no, key), NameJa = ja, NameEn = en, Group = g };
            all.Add(s);
            byId[s.Id] = s;
            return s;
        }

        private static void BuildSpecies()
        {
            CreatureSpecies s;
            // ================= 001〜010 農業お手伝い生物（農業仕様 §3.2） =================
            s = Add(1, "mizupuku", "ミズプク", "Mizupuku", CreatureGroup.Farm);
            s.HabitJa = "水をため込む丸い生物。池や川辺でぷかぷか浮かんでいる。"; s.HabitEn = "A round creature that stores water. Floats on ponds and rivers.";
            s.ClueJa = "水辺で動きを見て網で捕まえる。浮かんで止まった瞬間が狙い目。"; s.ClueEn = "Watch it by the water and net it when it stops drifting.";
            s.CharmJa = "畑に水をまく。雨の日は水遊びをする。"; s.CharmEn = "Waters crops. Plays in the rain.";
            s.Areas = new[] { AreaType.Pond, AreaType.Riverside, AreaType.Coast }; s.Weather = WeatherLike.Rain;
            s.Phenomena = AlienPhenomenon.LightRain | AlienPhenomenon.ReverseRain;
            s.Catch = CatchStyle.Normal; s.Behavior = WildBehavior.Float; s.Abilities = new[] { CreatureAbility.Water };
            s.Rarity = 0; s.BasePrice = 300; s.Shape = BodyShape.Round; s.Main = C(0.5f, 0.8f, 1f); s.Accent = C(0.3f, 0.55f, 0.95f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "spray_water"; s.ExtraBehaviors = new[] { "float_on_water", "play_in_rain" };

            s = Add(2, "horimogu", "ホリモグ", "Horimogu", CreatureGroup.Farm);
            s.HabitJa = "大きな前足のモグラ型。草原の地中に暮らし、時々土から顔を出す。"; s.HabitEn = "Mole-like with big front paws. Lives underground in meadows.";
            s.ClueJa = "土が盛り上がって動く所を見る。好物（野菜）を置くと出てくる。"; s.ClueEn = "Watch for moving soil. Put down vegetables to lure it out.";
            s.CharmJa = "空いた畑を前足で耕す。"; s.CharmEn = "Tills empty plots with its paws.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Farm, AreaType.Forest }; s.Features = new[] { "grass", "mud" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.AfterRain;
            s.Catch = CatchStyle.Bait; s.Behavior = WildBehavior.Burrow; s.Abilities = new[] { CreatureAbility.Till };
            s.Rarity = 1; s.BasePrice = 350; s.Shape = BodyShape.Mole; s.Main = C(0.55f, 0.4f, 0.3f); s.Accent = C(1f, 0.7f, 0.72f);
            s.FavoriteFood = "veg"; s.SignatureBehavior = "dig_soil"; s.ExtraBehaviors = new[] { "peek_from_hole", "come_to_bait" };

            s = Add(3, "tanepata", "タネパタ", "Tanepata", CreatureGroup.Farm);
            s.HabitJa = "小さな鳥型。木に止まって種をついばむ。"; s.HabitEn = "A small bird-like creature that pecks at seeds on trees.";
            s.ClueJa = "走ると遠くから気づいて飛び立つ。歩いてそっと近づくか、種で誘う。"; s.ClueEn = "It flies off if you run. Walk up quietly, or lure it with seeds.";
            s.CharmJa = "空いた畑に種をまく。"; s.CharmEn = "Sows seeds into empty plots.";
            s.Areas = new[] { AreaType.Forest, AreaType.Meadow, AreaType.Farm }; s.Features = new[] { "tree" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Wind; s.Phenomena = AlienPhenomenon.StarWind;
            s.Catch = CatchStyle.Sneak; s.Behavior = WildBehavior.Flyer; s.Abilities = new[] { CreatureAbility.Sow };
            s.Rarity = 1; s.BasePrice = 320; s.Shape = BodyShape.Bird; s.Main = C(0.72f, 0.9f, 0.4f); s.Accent = C(1f, 0.6f, 0.25f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "peck_seeds"; s.ExtraBehaviors = new[] { "flee_when_running", "ride_wind" };

            s = Add(4, "mogimogi", "モギモギ", "Mogimogi", CreatureGroup.Farm);
            s.HabitJa = "腕が長い小型生物。果樹の近くで実をもいで食べる。"; s.HabitEn = "A small long-armed creature that picks fruit near trees.";
            s.ClueJa = "好物の果実を置くと寄ってくる。食べている間は捕まえやすい。"; s.ClueEn = "Put down fruit; it is easy to catch while eating.";
            s.CharmJa = "実った作物を収穫して箱に入れる。"; s.CharmEn = "Harvests ripe crops into a basket.";
            s.Areas = new[] { AreaType.Forest, AreaType.Farm, AreaType.Meadow }; s.Features = new[] { "tree" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Bait; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Harvest };
            s.Rarity = 1; s.BasePrice = 380; s.Shape = BodyShape.LongArm; s.Main = C(0.95f, 0.62f, 0.42f); s.Accent = C(0.6f, 0.36f, 0.22f);
            s.FavoriteFood = "fruit"; s.SignatureBehavior = "pick_fruit"; s.ExtraBehaviors = new[] { "come_to_bait" };

            s = Add(5, "tsuchipon", "ツチポン", "Tsuchipon", CreatureGroup.Farm);
            s.HabitJa = "土に親しむ丸い生物。湿った土や落ち葉の下に隠れている。"; s.HabitEn = "A round creature fond of soil, hiding under damp leaves.";
            s.ClueJa = "落ち葉や土の盛り上がりをよく見る（[E]で観察）と見つかる。"; s.ClueEn = "Look closely ([E]) at leaves and soil mounds.";
            s.CharmJa = "土を食べて栄養に変え、畑の土を豊かにする。"; s.CharmEn = "Eats soil and enriches it.";
            s.Areas = new[] { AreaType.Forest, AreaType.Meadow, AreaType.Farm }; s.Features = new[] { "fallen_log", "mud", "grass" };
            s.Weather = WeatherLike.Rain | WeatherLike.AfterRain;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.SoilImprove };
            s.Rarity = 1; s.BasePrice = 350; s.Shape = BodyShape.Round; s.Main = C(0.5f, 0.38f, 0.26f); s.Accent = C(0.42f, 0.65f, 0.3f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "eat_soil"; s.ExtraBehaviors = new[] { "hide_under_leaves" };

            s = Add(6, "hanapika", "ハナピカ", "Hanapika", CreatureGroup.Farm);
            s.HabitJa = "花から花へ飛ぶ発光生物。晴れた日の花畑で活発。"; s.HabitEn = "A glowing creature that flits between flowers on sunny days.";
            s.ClueJa = "花に止まったタイミングを狙う。花を植えた場所にもやってくる。"; s.ClueEn = "Net it the moment it lands on a flower. It visits planted flowers.";
            s.CharmJa = "受粉を助けて実りを増やす。"; s.CharmEn = "Helps pollination for bigger harvests.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Farm, AreaType.Hill }; s.Features = new[] { "flower" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Sunny; s.Phenomena = AlienPhenomenon.BloomTide;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Flyer; s.Abilities = new[] { CreatureAbility.Pollinate, CreatureAbility.Glow };
            s.Rarity = 2; s.BasePrice = 450; s.Shape = BodyShape.Bug; s.Main = C(1f, 0.62f, 0.86f); s.Accent = C(1f, 1f, 0.62f); s.Glows = true;
            s.FavoriteFood = "flower"; s.SignatureBehavior = "visit_flowers"; s.ExtraBehaviors = new[] { "land_on_flower" };

            s = Add(7, "hikarimushi", "ヒカリムシ", "Hikarimushi", CreatureGroup.Farm);
            s.HabitJa = "夜になると光る小さな生物。夜の草むらで点々と光る。"; s.HabitEn = "A tiny creature that glows in the grass at night.";
            s.ClueJa = "昼はほとんど見えない。夜の草むらで光を手掛かりに探し、光っている瞬間を狙う。"; s.ClueEn = "Search by its light at night; net it while it is lit.";
            s.CharmJa = "光を好む作物の育成を助ける。"; s.CharmEn = "Helps light-loving crops grow.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest, AreaType.Farm, AreaType.Riverside }; s.Features = new[] { "grass", "flower" };
            s.Active = ActiveTime.Dark; s.Phenomena = AlienPhenomenon.LightRain;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Flyer; s.Abilities = new[] { CreatureAbility.Glow };
            s.Rarity = 1; s.BasePrice = 360; s.Shape = BodyShape.Bug; s.Main = C(0.72f, 1f, 0.5f); s.Accent = C(0.95f, 1f, 0.7f); s.Glows = true; s.GlowsOnlyAtNight = true; s.Scale = 0.7f;
            s.FavoriteFood = "glow"; s.SignatureBehavior = "glow_at_night"; s.ExtraBehaviors = new[] { "blink_pattern" };

            s = Add(8, "hakobachi", "ハコバチ", "Hakobachi", CreatureGroup.Farm);
            s.HabitJa = "小さな荷物を抱えて飛ぶ生物。巣箱や荷物のある場所に多い。"; s.HabitEn = "Flies around hugging little bundles. Common near boxes.";
            s.ClueJa = "荷物を下ろす瞬間は動きが止まる。甘い物が好き。"; s.ClueEn = "It stops when it sets its bundle down. Likes sweets.";
            s.CharmJa = "収穫物を倉庫へ運ぶ。"; s.CharmEn = "Carries harvests to storage.";
            s.Areas = new[] { AreaType.VillageCenter, AreaType.Residential, AreaType.Farm, AreaType.Meadow }; s.Features = new[] { "box", "flower" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Flyer; s.Abilities = new[] { CreatureAbility.Carry };
            s.Rarity = 1; s.BasePrice = 400; s.Shape = BodyShape.Bug; s.Main = C(1f, 0.86f, 0.3f); s.Accent = C(0.32f, 0.26f, 0.2f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "carry_bundle";

            s = Add(9, "pokaris", "ポカリス", "Pokaris", CreatureGroup.Farm);
            s.HabitJa = "体が温かいふわふわの生物。日だまりに集まるが、涼しい場所にも現れる。"; s.HabitEn = "A warm fluffy creature. Gathers in sunny spots, also seen in cool places.";
            s.ClueJa = "暖かい場所（屋根・岩・街灯の下）で待つと現れる。近くにいるとほんのり暖かい。"; s.ClueEn = "Appears at warm spots (roofs, rocks, lamps). Feels warm nearby.";
            s.CharmJa = "周りを温める。かまどの火にもなる。"; s.CharmEn = "Keeps things warm; can heat an oven.";
            s.Areas = new[] { AreaType.Residential, AreaType.Hill, AreaType.Meadow, AreaType.Mountain }; s.Features = new[] { "rock", "roof", "streetlight" };
            s.Weather = WeatherLike.Sunny;
            s.Catch = CatchStyle.Lure; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Heat };
            s.Rarity = 1; s.BasePrice = 420; s.Shape = BodyShape.Fluffy; s.Main = C(1f, 0.66f, 0.42f); s.Accent = C(1f, 0.4f, 0.3f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "warm_body"; s.ExtraBehaviors = new[] { "curl_in_sun" };

            s = Add(10, "tanekoro", "タネコロ", "Tanekoro", CreatureGroup.Farm);
            s.HabitJa = "丸い木の実のような小動物。草むらや果樹の近くで種を集める。"; s.HabitEn = "A nut-like critter that gathers seeds near grass and fruit trees.";
            s.ClueJa = "種集めに夢中になっている間が狙い目。"; s.ClueEn = "Net it while it's busy gathering seeds.";
            s.CharmJa = "種を集めて増やす。"; s.CharmEn = "Collects and multiplies seeds.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest, AreaType.Farm }; s.Features = new[] { "grass", "tree" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.SeedCollect };
            s.Rarity = 1; s.BasePrice = 330; s.Shape = BodyShape.Nut; s.Main = C(0.66f, 0.46f, 0.26f); s.Accent = C(0.45f, 0.7f, 0.32f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "gather_seeds";

            // ================= 011〜020 森・草原の生物 =================
            s = Add(11, "happari", "ハッパリ", "Happari", CreatureGroup.ForestMeadow);
            s.HabitJa = "葉っぱそっくりの小さな生物。"; s.HabitEn = "A tiny creature that looks exactly like a leaf.";
            s.ClueJa = "ほかの葉と違って動くものを見つける。[E]でよく見ると見つかる。"; s.ClueEn = "Spot the 'leaf' that moves differently. Look closely with [E].";
            s.CharmJa = "植物の陰に隠れる。"; s.CharmEn = "Hides in the shade of plants.";
            s.Areas = new[] { AreaType.Forest, AreaType.Meadow }; s.Features = new[] { "tree", "grass" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Wind;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage;
            s.Rarity = 2; s.BasePrice = 280; s.Shape = BodyShape.Leaf; s.Main = C(0.42f, 0.76f, 0.3f); s.Accent = C(0.3f, 0.55f, 0.2f); s.Scale = 0.8f;
            s.FavoriteFood = "veg"; s.SignatureBehavior = "mimic_leaf"; s.ExtraBehaviors = new[] { "hide_in_plants" };

            s = Add(12, "pyokoro", "ピョコロ", "Pyokoro", CreatureGroup.ForestMeadow);
            s.HabitJa = "バネのような足で跳ねる。何度か跳ねると休憩する。"; s.HabitEn = "Bounces on springy legs and rests after a few hops.";
            s.ClueJa = "追いかけると逃げる。跳ねた後に止まる瞬間を狙う。"; s.ClueEn = "Chasing scares it. Net it when it stops after hopping.";
            s.CharmJa = "ぴょんぴょん跳ね回る。"; s.CharmEn = "Bounces around happily.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest }; s.Features = new[] { "grass" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Hop;
            s.Rarity = 0; s.BasePrice = 200; s.Shape = BodyShape.Hopper; s.Main = C(0.6f, 0.86f, 1f); s.Accent = C(1f, 1f, 1f);
            s.FavoriteFood = "veg"; s.SignatureBehavior = "hop_then_rest"; s.ExtraBehaviors = new[] { "flee_when_running" };

            s = Add(13, "kurumari", "クルマリ", "Kurumari", CreatureGroup.ForestMeadow);
            s.HabitJa = "驚くと丸まって転がる。転がった跡の木の実が粉々になっていることがある。"; s.HabitEn = "Curls up and rolls when startled; nuts in its path get crushed.";
            s.ClueJa = "転がる方向を予測して待つ。転がり終わって止まった時が狙い目。"; s.ClueEn = "Predict where it rolls; net it when it stops.";
            s.CharmJa = "室内でもころころ転がる。"; s.CharmEn = "Rolls around even indoors.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest, AreaType.Mountain }; s.Features = new[] { "rock", "grass" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Roll; s.Abilities = new[] { CreatureAbility.Grind };
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Roller; s.Main = C(0.76f, 0.7f, 0.6f); s.Accent = C(0.5f, 0.45f, 0.4f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "roll_when_startled"; s.ExtraBehaviors = new[] { "crush_nuts" };

            s = Add(14, "edamimi", "エダミミ", "Edamimi", CreatureGroup.ForestMeadow);
            s.HabitJa = "枝のように長い耳を持つ。木の枝に擬態し、遠くの物音に耳を向ける。"; s.HabitEn = "Has branch-like ears; mimics twigs and turns toward far sounds.";
            s.ClueJa = "木の枝の中に混ざった耳を見つける。"; s.ClueEn = "Find the ears hidden among the branches.";
            s.CharmJa = "耳を揺らして反応する。"; s.CharmEn = "Wiggles its ears in response.";
            s.Areas = new[] { AreaType.Forest }; s.Features = new[] { "tree", "fallen_log" };
            s.Active = ActiveTime.Day | ActiveTime.Evening;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.Sense }; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 380; s.Shape = BodyShape.LongEar; s.Main = C(0.56f, 0.43f, 0.3f); s.Accent = C(0.86f, 0.76f, 0.6f);
            s.FavoriteFood = "fruit"; s.SignatureBehavior = "ear_twitch"; s.ExtraBehaviors = new[] { "mimic_branch" };

            s = Add(15, "fuwatane", "フワタネ", "Fuwatane", CreatureGroup.ForestMeadow);
            s.HabitJa = "綿毛のように風に運ばれて漂う。風の強い日に多い。"; s.HabitEn = "Drifts on the wind like fluff; common on windy days.";
            s.ClueJa = "風の流れを見て、流れていく先で待つ。"; s.ClueEn = "Read the wind and wait where it drifts.";
            s.CharmJa = "ふわふわ浮いて移動する。"; s.CharmEn = "Floats around softly.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Hill }; s.Features = new[] { "flower", "grass" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Wind; s.Phenomena = AlienPhenomenon.StarWind;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Drift; s.Abilities = new[] { CreatureAbility.Float };
            s.Rarity = 1; s.BasePrice = 260; s.Shape = BodyShape.Seedfluff; s.Main = C(1f, 1f, 0.95f); s.Accent = C(0.9f, 0.9f, 0.7f);
            s.FavoriteFood = "flower"; s.SignatureBehavior = "ride_wind";

            s = Add(16, "morimaru", "モリマル", "Morimaru", CreatureGroup.ForestMeadow);
            s.HabitJa = "苔をまとった丸い生物。苔むした倒木でくつろぐ。"; s.HabitEn = "A round creature wearing moss; lounges on mossy logs.";
            s.ClueJa = "苔むした倒木などを探す。苔に紛れているのでよく見る。"; s.ClueEn = "Search mossy logs and look closely.";
            s.CharmJa = "苔のある場所でくつろぐ。"; s.CharmEn = "Relaxes wherever there's moss.";
            s.Areas = new[] { AreaType.Forest }; s.Features = new[] { "fallen_log", "tree" };
            s.Weather = WeatherLike.Rain | WeatherLike.Fog;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage;
            s.Rarity = 1; s.BasePrice = 240; s.Shape = BodyShape.Round; s.Main = C(0.36f, 0.6f, 0.3f); s.Accent = C(0.52f, 0.8f, 0.4f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "rest_on_moss";

            s = Add(17, "tobikasa", "トビカサ", "Tobikasa", CreatureGroup.ForestMeadow);
            s.HabitJa = "キノコの傘のような頭を持つ。小刻みに跳ねては止まる。湿った日は傘から胞子を出す。"; s.HabitEn = "Mushroom-capped; hops in small bursts. Puffs spores on damp days.";
            s.ClueJa = "跳ねた後は動きが止まる。"; s.ClueEn = "It freezes after each hop.";
            s.CharmJa = "小刻みに跳ねる。"; s.CharmEn = "Hops in tiny bursts.";
            s.Areas = new[] { AreaType.Forest }; s.Features = new[] { "fallen_log", "mud" };
            s.Active = ActiveTime.Day | ActiveTime.Evening; s.Weather = WeatherLike.Rain | WeatherLike.Fog;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Hop; s.Abilities = new[] { CreatureAbility.Ferment }; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 340; s.Shape = BodyShape.Mushroom; s.Main = C(0.92f, 0.36f, 0.36f); s.Accent = C(1f, 0.97f, 0.9f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "spore_puff"; s.ExtraBehaviors = new[] { "hop_then_rest" };

            s = Add(18, "ototsumu", "オトツム", "Ototsumu", CreatureGroup.ForestMeadow);
            s.HabitJa = "背中の殻で音を鳴らす。"; s.HabitEn = "Makes sounds with the shell on its back.";
            s.ClueJa = "殻の音（♪）を頼りに探す。"; s.ClueEn = "Follow the shell sounds (♪).";
            s.CharmJa = "音を鳴らして遊ぶ。"; s.CharmEn = "Plays with sounds.";
            s.Areas = new[] { AreaType.Forest, AreaType.Meadow }; s.Features = new[] { "rock", "fallen_log" };
            s.Active = ActiveTime.Day | ActiveTime.Evening;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Sound }; s.MakesSound = true;
            s.Rarity = 1; s.BasePrice = 310; s.Shape = BodyShape.Shell; s.Main = C(0.96f, 0.86f, 0.6f); s.Accent = C(0.6f, 0.42f, 0.72f);
            s.FavoriteFood = "veg"; s.SignatureBehavior = "sound_shell";

            s = Add(19, "iropopo", "イロポポ", "Iropopo", CreatureGroup.ForestMeadow);
            s.HabitJa = "周りに合わせて体の色が変わる。"; s.HabitEn = "Changes color to match its surroundings.";
            s.ClueJa = "色ではなく動きに注目する。"; s.ClueEn = "Watch for movement, not color.";
            s.CharmJa = "周囲に応じて色が変化する。"; s.CharmEn = "Shifts color with its surroundings.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest, AreaType.Residential }; s.Features = new[] { "flower", "grass" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.ColorShift };
            s.Rarity = 2; s.BasePrice = 400; s.Shape = BodyShape.Lizard; s.Main = C(0.6f, 0.8f, 0.6f); s.Accent = C(0.92f, 0.52f, 0.82f);
            s.FavoriteFood = "flower"; s.SignatureBehavior = "change_color";

            s = Add(20, "pocketon", "ポケットン", "Pocketon", CreatureGroup.ForestMeadow);
            s.HabitJa = "小石や木の実を集めてどこかへ運ぶ。"; s.HabitEn = "Collects pebbles and nuts and carries them away.";
            s.ClueJa = "好きな物（種や木の実）を置くと近づく。"; s.ClueEn = "Put down seeds or nuts and it comes closer.";
            s.CharmJa = "拾った小物を集める。"; s.CharmEn = "Collects little trinkets.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Riverside, AreaType.Residential }; s.Features = new[] { "rock", "grass" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Bait; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Carry };
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Nut; s.Main = C(0.86f, 0.76f, 0.56f); s.Accent = C(0.56f, 0.4f, 0.3f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "collect_pebbles"; s.ExtraBehaviors = new[] { "come_to_bait" };

            // ================= 021〜030 水辺・海岸の生物 =================
            s = Add(21, "awarin", "アワリン", "Awarin", CreatureGroup.Waterside);
            s.HabitJa = "水面に泡を作って遊ぶ。"; s.HabitEn = "Blows bubbles on the water surface.";
            s.ClueJa = "泡から顔を出す瞬間を狙う。"; s.ClueEn = "Net it the moment it pops out of the bubbles.";
            s.CharmJa = "水槽や池で泡を作る。"; s.CharmEn = "Makes bubbles in ponds and tanks.";
            s.Areas = new[] { AreaType.Pond, AreaType.Riverside }; s.Active = ActiveTime.Day; s.Weather = WeatherLike.Rain; s.Phenomena = AlienPhenomenon.ReverseRain;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Float; s.Abilities = new[] { CreatureAbility.Bubble };
            s.Rarity = 1; s.BasePrice = 330; s.Shape = BodyShape.Bubble; s.Main = C(0.72f, 0.92f, 1f); s.Accent = C(1f, 1f, 1f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "make_bubbles";

            s = Add(22, "ishipeta", "イシペタ", "Ishipeta", CreatureGroup.Waterside);
            s.HabitJa = "石に張り付いている。"; s.HabitEn = "Clings to stones.";
            s.ClueJa = "石の隙間の小さな目を見つける。"; s.ClueEn = "Spot the tiny eyes between stones.";
            s.CharmJa = "石にぺたりと張り付く。"; s.CharmEn = "Sticks flat to stones.";
            s.Areas = new[] { AreaType.Riverside, AreaType.Coast, AreaType.Pond }; s.Features = new[] { "rock" };
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Stick; s.Abilities = new[] { CreatureAbility.Stick };
            s.Rarity = 1; s.BasePrice = 280; s.Shape = BodyShape.Lizard; s.Main = C(0.6f, 0.6f, 0.63f); s.Accent = C(0.92f, 0.92f, 0.5f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "stick_to_stone";

            s = Add(23, "kawahane", "カワハネ", "Kawahane", CreatureGroup.Waterside);
            s.HabitJa = "水面を跳ねて移動し、水しぶきを飛ばす。"; s.HabitEn = "Skips across water, splashing.";
            s.ClueJa = "岸に上がる瞬間を狙う。"; s.ClueEn = "Net it when it climbs onto the bank.";
            s.CharmJa = "水辺を飛び跳ねる。"; s.CharmEn = "Leaps around the waterside.";
            s.Areas = new[] { AreaType.Riverside, AreaType.Pond }; s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Hop; s.Abilities = new[] { CreatureAbility.Splash };
            s.Rarity = 1; s.BasePrice = 340; s.Shape = BodyShape.Fishy; s.Main = C(0.42f, 0.72f, 0.92f); s.Accent = C(1f, 1f, 1f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "skip_on_water";

            s = Add(24, "sunakoro", "スナコロ", "Sunakoro", CreatureGroup.Waterside);
            s.HabitJa = "砂玉を転がして歩く。丸めた砂玉はぎゅっと固い。"; s.HabitEn = "Rolls sand balls, packed very tight.";
            s.ClueJa = "砂に残った跡をたどる。"; s.ClueEn = "Follow the tracks in the sand.";
            s.CharmJa = "砂を丸めて遊ぶ。"; s.CharmEn = "Plays by rolling sand.";
            // 仕様 §3.3「砂に残った跡をたどる」に合わせて砂（砂浜・川の砂州）。旧実装は rock（マップ設計 v0.2 §2.8 P9）
            s.Areas = new[] { AreaType.Coast, AreaType.Riverside }; s.Features = new[] { "sand" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Dry;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Roll; s.Abilities = new[] { CreatureAbility.Press }; s.HasProposedAbility = true;
            s.Rarity = 1; s.BasePrice = 320; s.Shape = BodyShape.Roller; s.Main = C(0.96f, 0.86f, 0.6f); s.Accent = C(0.8f, 0.66f, 0.42f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "roll_sand";

            s = Add(25, "namipata", "ナミパタ", "Namipata", CreatureGroup.Waterside);
            s.HabitJa = "波に乗って移動する。"; s.HabitEn = "Rides the waves.";
            s.ClueJa = "波が引くタイミングで姿を現す。"; s.ClueEn = "Shows itself as the wave pulls back.";
            s.CharmJa = "水の動きに合わせて揺れる。"; s.CharmEn = "Sways with the water.";
            s.Areas = new[] { AreaType.Coast }; s.Active = ActiveTime.Day; s.Weather = WeatherLike.Wind;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Float;
            s.Rarity = 2; s.BasePrice = 300; s.Shape = BodyShape.Fishy; s.Main = C(0.32f, 0.6f, 0.86f); s.Accent = C(0.9f, 0.96f, 1f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "ride_waves";

            s = Add(26, "mizugasa", "ミズガサ", "Mizugasa", CreatureGroup.Waterside);
            s.HabitJa = "葉を傘のように使う。雨の日によく出てくる。"; s.HabitEn = "Uses a leaf as an umbrella; out on rainy days.";
            s.ClueJa = "雨の日や水をまいた場所で見つける。"; s.ClueEn = "Look on rainy days or where water was sprinkled.";
            s.CharmJa = "水滴を傘で受ける。"; s.CharmEn = "Catches raindrops on its umbrella.";
            s.Areas = new[] { AreaType.Riverside, AreaType.Pond, AreaType.Forest, AreaType.Farm }; s.Weather = WeatherLike.Rain;
            s.Catch = CatchStyle.Normal; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Water };
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Umbrella; s.Main = C(0.5f, 0.8f, 0.5f); s.Accent = C(0.5f, 0.72f, 1f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "umbrella_leaf";

            s = Add(27, "kokekoro", "コケコロ", "Kokekoro", CreatureGroup.Waterside);
            s.HabitJa = "水辺の苔を背負い、ゆっくり歩く。"; s.HabitEn = "Walks slowly with waterside moss on its back.";
            s.ClueJa = "苔の多い水際を探す。"; s.ClueEn = "Search mossy shorelines.";
            s.CharmJa = "苔を背負ってゆっくり歩く。"; s.CharmEn = "Plods along carrying moss.";
            s.Areas = new[] { AreaType.Riverside, AreaType.Pond }; s.Features = new[] { "rock", "mud" }; s.Weather = WeatherLike.Rain | WeatherLike.Fog;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.SoilImprove };
            s.Rarity = 1; s.BasePrice = 260; s.Shape = BodyShape.Shell; s.Main = C(0.62f, 0.56f, 0.46f); s.Accent = C(0.36f, 0.66f, 0.3f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "carry_moss";

            s = Add(28, "shioribo", "シオリボ", "Shioribo", CreatureGroup.Waterside);
            s.HabitJa = "リボン状のひれで岩に張り付く。"; s.HabitEn = "Clings to rocks with ribbon-like fins.";
            s.ClueJa = "水の流れが弱まったときに動く。動いた時が狙い目。"; s.ClueEn = "It moves when the current weakens; net it then.";
            s.CharmJa = "水中でひれをひらひらさせる。"; s.CharmEn = "Flutters its fins in the water.";
            s.Areas = new[] { AreaType.Coast, AreaType.Riverside }; s.Features = new[] { "rock" };
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Stick;
            s.Rarity = 2; s.BasePrice = 360; s.Shape = BodyShape.Fishy; s.Main = C(1f, 0.62f, 0.72f); s.Accent = C(1f, 0.92f, 0.96f);
            s.FavoriteFood = "water"; s.SignatureBehavior = "flutter_fins";

            s = Add(29, "numapuni", "ヌマプニ", "Numapuni", CreatureGroup.Waterside);
            s.HabitJa = "泥に隠れるぷにぷにした生物。泥の中でぷくぷく泡を立てる。"; s.HabitEn = "A squishy creature hiding in mud, making bubbles.";
            s.ClueJa = "泥から現れるまで、近くでじっと待って観察する。"; s.ClueEn = "Wait quietly nearby until it emerges from the mud.";
            s.CharmJa = "水や泥の中で形を変える。"; s.CharmEn = "Changes shape in water and mud.";
            s.Areas = new[] { AreaType.Pond, AreaType.Riverside }; s.Features = new[] { "mud" }; s.Weather = WeatherLike.Rain;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Burrow; s.Abilities = new[] { CreatureAbility.Ferment }; s.HasProposedAbility = true;
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Round; s.Main = C(0.56f, 0.46f, 0.52f); s.Accent = C(0.82f, 0.72f, 0.86f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "mud_bubbles";

            s = Add(30, "tsuyutsuyu", "ツユツユ", "Tsuyutsuyu", CreatureGroup.Waterside);
            s.HabitJa = "朝露のような小さな生物。朝の葉の水滴に紛れている。触れた葉はひんやり冷たい。"; s.HabitEn = "A dewdrop-like creature hiding among morning droplets. Leaves it touches turn cold.";
            s.ClueJa = "早朝や雨上がりの葉の上をよく見る。"; s.ClueEn = "Look closely at leaves early morning or after rain.";
            s.CharmJa = "葉やガラスの上で光る。"; s.CharmEn = "Sparkles on leaves and glass.";
            s.Areas = new[] { AreaType.Meadow, AreaType.Forest, AreaType.Farm, AreaType.Riverside }; s.Features = new[] { "grass", "flower" };
            s.Active = ActiveTime.EarlyMorning | ActiveTime.Morning; s.Weather = WeatherLike.AfterRain | WeatherLike.Fog;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.Cool, CreatureAbility.Glow }; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 380; s.Shape = BodyShape.Round; s.Main = C(0.76f, 0.95f, 1f, 0.8f); s.Accent = C(1f, 1f, 1f); s.Glows = true; s.Scale = 0.6f;
            s.FavoriteFood = "water"; s.SignatureBehavior = "dew_chill"; s.ExtraBehaviors = new[] { "dew_sparkle" };

            // ================= 031〜040 村・住宅地の生物 =================
            s = Add(31, "hakone", "ハコネ", "Hakone", CreatureGroup.Village);
            s.HabitJa = "空き箱に入りたがり、物をしまい込む。"; s.HabitEn = "Loves empty boxes and tucks things away inside.";
            s.ClueJa = "家や商店の箱の周りを探す。"; s.ClueEn = "Search around boxes near houses and shops.";
            s.CharmJa = "用意した箱に入る。"; s.CharmEn = "Climbs into boxes you provide.";
            s.Areas = new[] { AreaType.VillageCenter, AreaType.Residential, AreaType.Farm }; s.Features = new[] { "box" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Lure; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Pack };
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Box; s.Main = C(0.96f, 0.82f, 0.62f); s.Accent = C(0.76f, 0.56f, 0.36f);
            s.FavoriteFood = "fruit"; s.SignatureBehavior = "enter_box";

            s = Add(32, "ranpuru", "ランプル", "Lampuru", CreatureGroup.Village);
            s.HabitJa = "夜の灯りに集まる発光生物。"; s.HabitEn = "A glowing creature drawn to lights at night.";
            s.ClueJa = "夜の街灯や窓辺で見つける。"; s.ClueEn = "Find it by streetlights and windows at night.";
            s.CharmJa = "室内の灯りの周りを飛ぶ。"; s.CharmEn = "Flies around indoor lamps.";
            s.Areas = new[] { AreaType.VillageCenter, AreaType.Residential }; s.Features = new[] { "streetlight", "window" };
            s.Active = ActiveTime.Dark;
            s.Catch = CatchStyle.Lure; s.Behavior = WildBehavior.Flyer; s.Abilities = new[] { CreatureAbility.Glow };
            s.Rarity = 1; s.BasePrice = 360; s.Shape = BodyShape.Bug; s.Main = C(1f, 0.92f, 0.5f); s.Accent = C(1f, 0.72f, 0.3f); s.Glows = true;
            s.FavoriteFood = "glow"; s.SignatureBehavior = "gather_at_light";

            s = Add(33, "manepi", "マネピ", "Manepi", CreatureGroup.Village);
            s.HabitJa = "ほかの生物や人物の動き・声・表情を真似する。"; s.HabitEn = "Copies the movements, voices and faces of others.";
            s.ClueJa = "誰かを見ているところを探す。身振り（G）を見せると真似をする。真似に夢中な間が狙い目。"; s.ClueEn = "Show it a gesture (G); net it while it's busy copying.";
            s.CharmJa = "主人公の動作を真似する。伝言を覚えて再現する。"; s.CharmEn = "Mimics you. Remembers and replays messages.";
            s.Areas = new[] { AreaType.VillageCenter, AreaType.Residential, AreaType.Meadow }; s.Features = new[] { "window", "roof", "box" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Copycat; s.Abilities = new[] { CreatureAbility.Mimic, CreatureAbility.Sound };
            s.Rarity = 2; s.BasePrice = 500; s.Shape = BodyShape.Mimic; s.Main = C(0.92f, 0.86f, 1f); s.Accent = C(1f, 0.62f, 0.72f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "copy_gesture";

            s = Add(34, "tonton", "トントン", "Tonton", CreatureGroup.Village);
            s.HabitJa = "壁や看板を一定のリズムで叩く。"; s.HabitEn = "Taps walls and signs in a steady rhythm.";
            s.ClueJa = "音（♪）がする方向を探す。"; s.ClueEn = "Search toward the tapping (♪).";
            s.CharmJa = "家具などを叩いて音を出す。"; s.CharmEn = "Drums on furniture.";
            s.Areas = new[] { AreaType.VillageCenter, AreaType.Residential }; s.Features = new[] { "roof", "window", "streetlight" };
            s.Active = ActiveTime.Day | ActiveTime.Evening;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Sound }; s.MakesSound = true;
            s.Rarity = 1; s.BasePrice = 300; s.Shape = BodyShape.Drummer; s.Main = C(0.72f, 0.56f, 0.4f); s.Accent = C(0.96f, 0.86f, 0.5f);
            s.FavoriteFood = "seed"; s.SignatureBehavior = "tap_rhythm";

            s = Add(35, "nemuriko", "ネムリコ", "Nemuriko", CreatureGroup.Village);
            s.HabitJa = "日なたで寝るのが大好き。寝ている体はぽかぽか温かい。"; s.HabitEn = "Loves napping in the sun; its sleeping body is toasty.";
            s.ClueJa = "日当たりのよい場所で見つける。走ると起きて逃げるので、歩いてそっと。"; s.ClueEn = "Find it in sunny spots. Running wakes it; walk softly.";
            s.CharmJa = "寝場所を変えながら眠る。"; s.CharmEn = "Naps, moving from spot to spot.";
            s.Areas = new[] { AreaType.Residential, AreaType.VillageCenter, AreaType.Meadow, AreaType.Hill }; s.Features = new[] { "roof", "grass" };
            s.Active = ActiveTime.Daytime; s.Weather = WeatherLike.Sunny | WeatherLike.Dry;
            s.Catch = CatchStyle.Sneak; s.Behavior = WildBehavior.Sleeper; s.Abilities = new[] { CreatureAbility.Heat }; s.HasProposedAbility = true;
            s.Rarity = 1; s.BasePrice = 280; s.Shape = BodyShape.Round; s.Main = C(1f, 0.86f, 0.76f); s.Accent = C(0.96f, 0.62f, 0.62f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "sleep_in_sun";

            s = Add(36, "karapon", "カラポン", "Karapon", CreatureGroup.Village);
            s.HabitJa = "そばの物の色に合わせて体の色が変わる。"; s.HabitEn = "Changes its color to match nearby objects.";
            s.ClueJa = "物陰や家具の色をよく見る。"; s.ClueEn = "Look closely at shadows and furniture colors.";
            s.CharmJa = "家具に合わせて色を変える。"; s.CharmEn = "Matches furniture colors.";
            s.Areas = new[] { AreaType.Residential, AreaType.VillageCenter }; s.Features = new[] { "box", "window" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.ColorShift };
            s.Rarity = 2; s.BasePrice = 380; s.Shape = BodyShape.Lizard; s.Main = C(0.82f, 0.72f, 0.92f); s.Accent = C(0.6f, 0.92f, 0.82f);
            s.FavoriteFood = "fruit"; s.SignatureBehavior = "match_color";

            s = Add(37, "mofupi", "モフピ", "Mofupi", CreatureGroup.Village);
            s.HabitJa = "ふわふわの毛を持つ人なつこい生物。"; s.HabitEn = "A friendly creature with fluffy fur.";
            s.ClueJa = "村の庭や草地で見つける。網でふつうに捕まえられる。"; s.ClueEn = "Found in gardens and grass; easy to net.";
            s.CharmJa = "主人公にすり寄る。"; s.CharmEn = "Snuggles up to you.";
            s.Areas = new[] { AreaType.Residential, AreaType.VillageCenter, AreaType.Meadow }; s.Features = new[] { "grass" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Normal; s.Behavior = WildBehavior.Wander;
            s.Rarity = 0; s.BasePrice = 250; s.Shape = BodyShape.Fluffy; s.Main = C(1f, 0.96f, 0.92f); s.Accent = C(0.92f, 0.76f, 0.82f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "rub_against";

            s = Add(38, "tsumitsumi", "ツミツミ", "Tsumitsumi", CreatureGroup.Village);
            s.HabitJa = "小石を器用に積み上げる。"; s.HabitEn = "Stacks pebbles skillfully.";
            s.ClueJa = "石を積んだ跡を探す。"; s.ClueEn = "Look for stacked stones.";
            s.CharmJa = "小物を積んで遊ぶ。"; s.CharmEn = "Plays by stacking things.";
            s.Areas = new[] { AreaType.Residential, AreaType.Riverside, AreaType.Mountain }; s.Features = new[] { "rock" };
            s.Active = ActiveTime.Day;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Stack };
            s.Rarity = 1; s.BasePrice = 290; s.Shape = BodyShape.Stacker; s.Main = C(0.72f, 0.76f, 0.82f); s.Accent = C(0.92f, 0.62f, 0.42f);
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "stack_stones";

            s = Add(39, "yanekoro", "ヤネコロ", "Yanekoro", CreatureGroup.Village);
            s.HabitJa = "建物の屋根を跳び回る。"; s.HabitEn = "Bounds across rooftops.";
            s.ClueJa = "屋根の上や軒先を観察し、降りてきた時を狙う。"; s.ClueEn = "Watch rooftops and net it when it comes down.";
            s.CharmJa = "家の高い場所を好む。"; s.CharmEn = "Likes high places in the house.";
            s.Areas = new[] { AreaType.Residential, AreaType.VillageCenter }; s.Features = new[] { "roof" };
            s.Active = ActiveTime.Day | ActiveTime.Evening;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Hop;
            s.Rarity = 2; s.BasePrice = 330; s.Shape = BodyShape.Hopper; s.Main = C(0.96f, 0.72f, 0.42f); s.Accent = C(0.42f, 0.32f, 0.32f);
            s.FavoriteFood = "fruit"; s.SignatureBehavior = "roof_hop";

            s = Add(40, "marumado", "マルマド", "Marumado", CreatureGroup.Village);
            s.HabitJa = "窓辺に現れる丸い生物。窓に模様を描く。"; s.HabitEn = "A round creature that appears at windows and draws patterns.";
            s.ClueJa = "窓に付いた姿を見つける。"; s.ClueEn = "Spot it stuck to a window.";
            s.CharmJa = "窓から外を眺めたり模様を描く。"; s.CharmEn = "Gazes out of windows and draws patterns.";
            s.Areas = new[] { AreaType.Residential, AreaType.VillageCenter }; s.Features = new[] { "window" };
            s.Weather = WeatherLike.Rain | WeatherLike.Fog;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Stick;
            s.Rarity = 2; s.BasePrice = 340; s.Shape = BodyShape.Round; s.Main = C(0.82f, 0.9f, 1f); s.Accent = C(0.52f, 0.62f, 0.92f);
            s.FavoriteFood = "sweet"; s.SignatureBehavior = "window_watch";

            // ================= 041〜050 地下・特殊条件の生物 =================
            s = Add(41, "kabepita", "カベピタ", "Kabepita", CreatureGroup.Special);
            s.HabitJa = "洞窟の壁に張り付いて歩く。"; s.HabitEn = "Walks stuck to cave walls.";
            s.ClueJa = "壁の小さな目に気づく。"; s.ClueEn = "Notice the tiny eyes on the wall.";
            s.CharmJa = "壁面を歩く。"; s.CharmEn = "Walks on walls.";
            s.Areas = new[] { AreaType.Mountain, AreaType.MineEntrance }; s.Features = new[] { "rock" };
            s.Underground = true; s.MinFloor = 1; s.MaxFloor = 6;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Stick; s.Abilities = new[] { CreatureAbility.Stick };
            s.Rarity = 1; s.BasePrice = 320; s.Shape = BodyShape.Lizard; s.Main = C(0.52f, 0.52f, 0.62f); s.Accent = C(1f, 0.9f, 0.42f);
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "wall_walk";

            s = Add(42, "kodamaru", "コダマル", "Kodamaru", CreatureGroup.Special);
            s.HabitJa = "鳴き声を洞窟の中で反響させ、返ってきた音に耳を澄ませる。"; s.HabitEn = "Echoes its calls through caves and listens to the reply.";
            s.ClueJa = "声の方向（♪）を追う。"; s.ClueEn = "Follow the direction of its voice (♪).";
            s.CharmJa = "音で呼び合う。"; s.CharmEn = "Calls back and forth with sound.";
            s.Areas = new[] { AreaType.MineEntrance, AreaType.Mountain }; s.Active = ActiveTime.Always;
            s.Underground = true; s.MinFloor = 1; s.MaxFloor = 10;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Echo, CreatureAbility.Sense }; s.MakesSound = true; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 450; s.Shape = BodyShape.Echo; s.Main = C(0.62f, 0.66f, 0.86f); s.Accent = C(0.92f, 0.92f, 1f);
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "echo_call";

            s = Add(43, "iwanemu", "イワネム", "Iwanemu", CreatureGroup.Special);
            s.HabitJa = "岩に似た眠る生物。眠っている間は少しも揺れず、重い体の下の石はぺしゃんこ。"; s.HabitEn = "A rock-like sleeper. It never budges while asleep; stones under it get flattened.";
            s.ClueJa = "近くの岩を（ツルハシや網で）叩くと目覚める。起きた時が狙い目。"; s.ClueEn = "Tap a nearby rock (pickaxe or net) to wake it, then net it.";
            s.CharmJa = "眠ると岩そっくりになる。"; s.CharmEn = "Looks just like a rock when sleeping.";
            s.Areas = new[] { AreaType.Mountain }; s.Features = new[] { "rock" };
            s.Underground = true; s.MinFloor = 3; s.MaxFloor = 10;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Sleeper; s.Abilities = new[] { CreatureAbility.Stabilize, CreatureAbility.Press }; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 480; s.Shape = BodyShape.Rock; s.Main = C(0.56f, 0.53f, 0.5f); s.Accent = C(0.76f, 0.72f, 0.66f);
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "rock_sleep"; s.ExtraBehaviors = new[] { "heavy_press", "wake_up" };

            s = Add(44, "kurikoro", "クリコロ", "Kurikoro", CreatureGroup.Special);
            s.HabitJa = "宝石のような殻を背負う。鉱石の近くにいる。"; s.HabitEn = "Carries a jewel-like shell; found near ore.";
            s.ClueJa = "鉱石の近くで見つける。殻がきらきら光る。"; s.ClueEn = "Look near ore; its shell sparkles.";
            s.CharmJa = "殻をきらきら光らせる。"; s.CharmEn = "Makes its shell sparkle.";
            s.Areas = new[] { AreaType.Mountain }; s.Features = new[] { "rock" }; s.Phenomena = AlienPhenomenon.CrystalHaze;
            s.Underground = true; s.MinFloor = 4; s.MaxFloor = 10;
            s.Catch = CatchStyle.Normal; s.Behavior = WildBehavior.Wander; s.Abilities = new[] { CreatureAbility.Glow };
            s.Rarity = 3; s.BasePrice = 700; s.Shape = BodyShape.Shell; s.Main = C(0.6f, 0.5f, 0.46f); s.Accent = C(0.5f, 0.9f, 1f); s.Glows = true;
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "gem_shell_shine";

            s = Add(45, "sumikage", "スミカゲ", "Sumikage", CreatureGroup.Special);
            s.HabitJa = "暗い場所を好む影のような生物。そばはひんやりしている。"; s.HabitEn = "A shadow-like creature of dark places; the air near it is cool.";
            s.ClueJa = "光と影の境目を探す。"; s.ClueEn = "Search where light meets shadow.";
            s.CharmJa = "薄暗い場所で遊ぶ。"; s.CharmEn = "Plays in dim places.";
            s.Areas = new[] { AreaType.Forest }; s.Active = ActiveTime.Dark;
            s.Underground = true; s.MinFloor = 2; s.MaxFloor = 10;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage; s.Abilities = new[] { CreatureAbility.Shade, CreatureAbility.Cool }; s.HasProposedAbility = true;
            s.Rarity = 2; s.BasePrice = 420; s.Shape = BodyShape.Shadow; s.Main = C(0.16f, 0.13f, 0.26f, 0.8f); s.Accent = C(0.62f, 0.52f, 1f);
            s.FavoriteFood = "moss"; s.SignatureBehavior = "shadow_chill";

            s = Add(46, "hokorifu", "ホコリフ", "Hokorifu", CreatureGroup.Special);
            s.HabitJa = "鉱石粉をまとったふわふわ生物。採掘の後に舞い上がって現れる。"; s.HabitEn = "A fluffball coated in ore dust; appears after mining.";
            s.ClueJa = "鉱山で掘った後、舞い上がる粉を見る。舞い降りた時が狙い目。"; s.ClueEn = "Watch the dust after mining; net it when it settles.";
            s.CharmJa = "体の粉をふわりと散らす。"; s.CharmEn = "Scatters dust from its body.";
            s.Underground = true; s.AlsoOverworld = false; s.MinFloor = 1; s.MaxFloor = 10;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Drift; s.Abilities = new[] { CreatureAbility.MineralDust };
            s.Rarity = 1; s.BasePrice = 340; s.Shape = BodyShape.Fluffy; s.Main = C(0.82f, 0.8f, 0.74f); s.Accent = C(0.92f, 0.82f, 0.52f);
            s.FavoriteFood = "mineral"; s.SignatureBehavior = "dust_puff";

            s = Add(47, "murepika", "ムレピカ", "Murepika", CreatureGroup.Special);
            s.HabitJa = "集団で光を点滅させて動く。仲間とぴったり同じ間隔で光る。"; s.HabitEn = "Moves in groups, flashing in perfect sync.";
            s.ClueJa = "光のパターンを観察する。光りそろった瞬間が狙い目。"; s.ClueEn = "Watch the light pattern; net them when they flash together.";
            s.CharmJa = "仲間と光を合わせる。"; s.CharmEn = "Syncs its glow with friends.";
            s.Areas = new[] { AreaType.Hill, AreaType.Pond }; s.Active = ActiveTime.Dark; s.Phenomena = AlienPhenomenon.LightRain | AlienPhenomenon.StarBand;
            s.Underground = true; s.MinFloor = 6; s.MaxFloor = 10;
            s.Catch = CatchStyle.Timing; s.Behavior = WildBehavior.Swarm; s.Abilities = new[] { CreatureAbility.Glow, CreatureAbility.Stabilize }; s.HasProposedAbility = true;
            s.Rarity = 3; s.BasePrice = 650; s.Shape = BodyShape.Bug; s.Main = C(0.52f, 1f, 0.82f); s.Accent = C(0.82f, 1f, 1f); s.Glows = true; s.Scale = 0.6f;
            s.FavoriteFood = "glow"; s.SignatureBehavior = "sync_flash";

            s = Add(48, "nijimayu", "ニジマユ", "Nijimayu", CreatureGroup.Special);
            s.HabitJa = "高台の木で繭になって休み、繭から虹色の羽を広げる。"; s.HabitEn = "Rests in a cocoon on hilltop trees, then spreads rainbow wings.";
            s.ClueJa = "高台の木などで繭を探す。"; s.ClueEn = "Search hilltop trees for cocoons.";
            s.CharmJa = "成長すると繭から羽を広げた姿に変わる。"; s.CharmEn = "Grows from cocoon into a winged form.";
            s.Areas = new[] { AreaType.Hill, AreaType.Forest }; s.Features = new[] { "tree" };
            s.Active = ActiveTime.Day; s.Weather = WeatherLike.Sunny | WeatherLike.AfterRain;
            s.Catch = CatchStyle.Discover; s.Behavior = WildBehavior.Camouflage;
            s.Rarity = 3; s.BasePrice = 600; s.Shape = BodyShape.Cocoon; s.Main = C(0.96f, 0.92f, 0.86f); s.Accent = C(0.72f, 0.6f, 1f);
            s.FavoriteFood = "flower"; s.SignatureBehavior = "cocoon_rest"; s.ExtraBehaviors = new[] { "rainbow_wings" };

            s = Add(49, "yumeboshi", "ユメボシ", "Yumeboshi", CreatureGroup.Special);
            s.HabitJa = "淡く光る浮遊生物。夜の静かな高台に現れる。"; s.HabitEn = "A faintly glowing floater on quiet hilltops at night.";
            s.ClueJa = "走ると消えるように逃げる。静かにそっと近づく。"; s.ClueEn = "Running makes it vanish; approach quietly.";
            s.CharmJa = "ふわふわ漂って光る。"; s.CharmEn = "Drifts softly, glowing.";
            s.Areas = new[] { AreaType.Hill, AreaType.Meadow }; s.Active = ActiveTime.Night | ActiveTime.LateNight; s.Phenomena = AlienPhenomenon.StarBand | AlienPhenomenon.StarWind;
            s.Catch = CatchStyle.Sneak; s.Behavior = WildBehavior.Float; s.Abilities = new[] { CreatureAbility.Float, CreatureAbility.Glow };
            s.Rarity = 3; s.BasePrice = 750; s.Shape = BodyShape.Seedfluff; s.Main = C(0.76f, 0.82f, 1f); s.Accent = C(1f, 0.96f, 0.72f); s.Glows = true;
            s.FavoriteFood = "glow"; s.SignatureBehavior = "dream_float";

            s = Add(50, "hoshinoko", "ホシノコ", "Hoshinoko", CreatureGroup.Special);
            s.HabitJa = "小さな惑星の輪を持つ希少生物。生き物たちが静かに集まる場所に現れる。"; s.HabitEn = "A rare creature with a tiny planetary ring. Appears where creatures gather peacefully.";
            s.ClueJa = "たくさんの生き物が穏やかに過ごす夜や、星の光帯の下で出会えることがある。"; s.ClueEn = "May appear on nights when many creatures gather peacefully, or under the star band.";
            s.CharmJa = "農場や自宅の象徴的な存在になる。"; s.CharmEn = "Becomes the symbol of your farm or home.";
            s.Areas = new[] { AreaType.Hill, AreaType.Farm, AreaType.Meadow }; s.Active = ActiveTime.Night | ActiveTime.LateNight; s.Phenomena = AlienPhenomenon.StarBand;
            s.Catch = CatchStyle.Sneak; s.Behavior = WildBehavior.Float; s.Abilities = new[] { CreatureAbility.Glow };
            s.Rarity = 5; s.BasePrice = 3000; s.Shape = BodyShape.Star; s.Main = C(0.96f, 0.88f, 1f); s.Accent = C(1f, 0.86f, 0.52f); s.Glows = true;
            s.FavoriteFood = "glow"; s.SignatureBehavior = "ring_spin";
        }

        private static void F(string key, string ja, string en, params CreatureAbility[] reveals)
        {
            facts[key] = new BehaviorFact { Key = key, Ja = ja, En = en, Reveals = reveals ?? new CreatureAbility[0] };
        }

        private static void BuildFacts()
        {
            // 野生の行動（観察で図鑑に記録する事実。正解の訳ではない）
            F("spray_water", "体を膨らませて水を吹きかけていた", "Puffed up and sprayed water", CreatureAbility.Water);
            F("float_on_water", "水面にぷかぷか浮かんでいた", "Floated on the water");
            F("play_in_rain", "雨の中で楽しそうに水遊びしていた", "Played happily in the rain");
            F("dig_soil", "大きな前足で土を掘り返していた", "Dug up soil with big paws", CreatureAbility.Till);
            F("peek_from_hole", "土の穴から顔を出してはすぐ引っ込めた", "Peeked out of a hole and ducked back");
            F("come_to_bait", "置いた食べ物に寄ってきて食べていた", "Came to eat food that was put down");
            F("peck_seeds", "種をついばみ、別の場所にぽとりと落としていた", "Pecked seeds and dropped them elsewhere", CreatureAbility.Sow);
            F("flee_when_running", "走って近づくと遠くからでも逃げた", "Fled from far away when approached at a run");
            F("ride_wind", "風に乗ってすいっと移動していた", "Glided along on the wind");
            F("pick_fruit", "長い腕で実をもいで集めていた", "Picked fruit with its long arms", CreatureAbility.Harvest);
            F("eat_soil", "土をもぐもぐ食べ、そこの土がふかふかになっていた", "Munched soil, leaving it soft and rich", CreatureAbility.SoilImprove);
            F("hide_under_leaves", "落ち葉の下に隠れていた", "Hid under fallen leaves");
            F("visit_flowers", "花から花へ飛び回り、花粉をつけていた", "Flitted between flowers, covered in pollen", CreatureAbility.Pollinate);
            F("land_on_flower", "花に止まるとしばらく動かなかった", "Stayed still for a while after landing on a flower");
            F("glow_at_night", "夜になると体が光り、周りの草がよく伸びていた", "Glowed at night; nearby grass grew well", CreatureAbility.Glow);
            F("blink_pattern", "光ったり消えたりしながら移動していた", "Moved while blinking on and off");
            F("carry_bundle", "自分より大きな荷物を抱えて飛んでいた", "Flew carrying a bundle bigger than itself", CreatureAbility.Carry);
            F("warm_body", "近くにいるとほんのり暖かかった", "Felt warm to be near", CreatureAbility.Heat);
            F("curl_in_sun", "日だまりで丸くなっていた", "Curled up in a sunny spot");
            F("gather_seeds", "種を集めて、ほっぺにためていた", "Gathered seeds into its cheeks", CreatureAbility.SeedCollect);
            F("mimic_leaf", "葉っぱそっくりで、風が止んでも少し動いていた", "Looked like a leaf, but moved even when the wind stopped");
            F("hide_in_plants", "植物の陰にさっと隠れた", "Ducked into the shade of plants");
            F("hop_then_rest", "何度か跳ねたあと、止まって休んでいた", "Rested after hopping a few times");
            F("roll_when_startled", "驚くと丸まって転がり、止まるとしばらく動かなかった", "Rolled up when startled and stayed still after stopping");
            F("crush_nuts", "転がった跡の木の実が粉々になっていた", "Nuts in its rolling path were crushed to powder", CreatureAbility.Grind);
            F("ear_twitch", "遠くの足音に長い耳を向けていた", "Turned its long ears toward distant footsteps", CreatureAbility.Sense);
            F("mimic_branch", "枝に混ざって耳を伸ばしていた", "Stretched its ears among the branches");
            F("rest_on_moss", "苔の上で気持ちよさそうにくつろいでいた", "Lounged comfortably on moss");
            F("spore_puff", "傘からふわりと胞子を出し、近くの実がぷくぷく泡立っていた", "Puffed spores; nearby fruit began to bubble", CreatureAbility.Ferment);
            F("sound_shell", "背中の殻を鳴らしていた", "Made sounds with its shell", CreatureAbility.Sound);
            F("change_color", "周りの色に合わせて体の色を変えた", "Changed color to match its surroundings", CreatureAbility.ColorShift);
            F("collect_pebbles", "小石や木の実を拾って、どこかへ運んでいた", "Picked up pebbles and nuts and carried them off", CreatureAbility.Carry);
            F("make_bubbles", "水面にたくさんの泡を作っていた", "Made lots of bubbles on the water", CreatureAbility.Bubble);
            F("stick_to_stone", "石にぺたりと張り付いて離れなかった", "Stuck flat to a stone and wouldn't let go", CreatureAbility.Stick);
            F("skip_on_water", "水面を跳ねて水しぶきを飛ばしていた", "Skipped across water, splashing", CreatureAbility.Splash);
            F("roll_sand", "砂をぎゅっと押し固めて丸い玉にしていた", "Pressed sand into a tight ball", CreatureAbility.Press);
            F("ride_waves", "波に乗ってゆらゆら移動していた", "Swayed along riding the waves");
            F("umbrella_leaf", "葉の傘に水をためて、ざばっとこぼしていた", "Collected water on its leaf and poured it out", CreatureAbility.Water);
            F("carry_moss", "背中の苔を落とした所の土がしっとり豊かになっていた", "Soil where its moss fell became moist and rich", CreatureAbility.SoilImprove);
            F("flutter_fins", "リボンのようなひれをひらひらさせていた", "Fluttered its ribbon fins");
            F("mud_bubbles", "泥の中でぷくぷく泡を立て、泥が甘酸っぱい匂いになった", "Bubbled in the mud; it began to smell sweet and sour", CreatureAbility.Ferment);
            F("dew_chill", "触れた葉がひんやり冷たくなっていた", "Leaves it touched turned cold", CreatureAbility.Cool);
            F("dew_sparkle", "朝日を受けてきらきら光っていた", "Sparkled in the morning light", CreatureAbility.Glow);
            F("enter_box", "空き箱を見つけると中に入り、物をしまい込んでいた", "Climbed into an empty box and tucked things in", CreatureAbility.Pack);
            F("gather_at_light", "灯りの周りに集まって光っていた", "Gathered around a light, glowing", CreatureAbility.Glow);
            F("copy_gesture", "こちらの身振りをそっくり真似した", "Copied your gesture exactly", CreatureAbility.Mimic);
            F("tap_rhythm", "一定のリズムで壁を叩いていた", "Tapped on a wall in a steady rhythm", CreatureAbility.Sound);
            F("sleep_in_sun", "日なたで眠り、寝ている体がぽかぽか温かかった", "Slept in the sun; its body was toasty", CreatureAbility.Heat);
            F("match_color", "そばの物と同じ色に体を変えた", "Turned the same color as a nearby object", CreatureAbility.ColorShift);
            F("rub_against", "すり寄ってきた", "Snuggled up");
            F("stack_stones", "小石を器用に積み上げていた", "Stacked pebbles skillfully", CreatureAbility.Stack);
            F("roof_hop", "屋根から屋根へ跳び移っていた", "Leapt from roof to roof");
            F("window_watch", "窓からじっと外を眺め、ガラスに模様を描いていた", "Gazed out a window, drawing patterns on the glass");
            F("wall_walk", "壁にぴったり張り付いて歩いていた", "Walked stuck to the wall", CreatureAbility.Stick);
            F("echo_call", "鳴き声を響かせ、返ってきた音に耳を澄ませていた", "Called out and listened to the echo", CreatureAbility.Echo, CreatureAbility.Sense);
            F("rock_sleep", "眠っている間、少しも揺れなかった", "Did not budge at all while sleeping", CreatureAbility.Stabilize);
            F("heavy_press", "重い体の下で、石がぺしゃんこになっていた", "A stone under its heavy body was flattened", CreatureAbility.Press);
            F("wake_up", "近くの岩を叩くと目を覚ました", "Woke up when a nearby rock was tapped");
            F("gem_shell_shine", "宝石のような殻をきらきら光らせていた", "Made its jewel shell sparkle", CreatureAbility.Glow);
            F("shadow_chill", "影の中に入ると、周りがひんやりした", "The air turned cool when it slipped into shadow", CreatureAbility.Shade, CreatureAbility.Cool);
            F("dust_puff", "掘った後の粉をまとって、ふわりと舞っていた", "Floated up wrapped in mining dust", CreatureAbility.MineralDust);
            F("sync_flash", "仲間とぴったり同じ間隔で光り、周りの揺れが静まったように見えた", "Flashed in perfect sync; the trembling around seemed to calm", CreatureAbility.Glow, CreatureAbility.Stabilize);
            F("cocoon_rest", "高い木の上で繭になって休んでいた", "Rested as a cocoon high in a tree");
            F("rainbow_wings", "繭から虹色の羽を広げた", "Spread rainbow wings from its cocoon");
            F("dream_float", "淡く光りながらふわふわ漂っていた", "Drifted softly, glowing faintly", CreatureAbility.Float, CreatureAbility.Glow);
            F("ring_spin", "体の周りの輪がゆっくり回っていた", "The ring around its body slowly turned");
            F("eat_favorite", "好物をとてもおいしそうに食べた", "Ate its favorite food with delight");
            F("pet_happy", "なでると嬉しそうに体を揺らした", "Wiggled happily when petted");

            // 農場での働き（Farming が CreatureObservedEvent で発行する）
            F("work.water", "畑に水をまいていた", "Watered the field", CreatureAbility.Water);
            F("work.splash", "畑に水しぶきを飛ばしていた", "Splashed water on the field", CreatureAbility.Splash);
            F("work.till", "空いた畑を耕していた", "Tilled empty plots", CreatureAbility.Till);
            F("work.sow", "種箱の種を空いた畑にまいていた", "Sowed seeds from the seed box", CreatureAbility.Sow);
            F("work.harvest", "実った作物を収穫していた", "Harvested ripe crops", CreatureAbility.Harvest);
            F("work.soil", "畑の土を豊かにしていた", "Enriched the soil", CreatureAbility.SoilImprove);
            F("work.pollinate", "畑の花に花粉を運んでいた", "Carried pollen between crop flowers", CreatureAbility.Pollinate);
            F("work.glow", "夜の畑を照らしていた", "Lit up the field at night", CreatureAbility.Glow);
            F("work.carry", "収穫物を倉庫へ運んでいた", "Carried harvests to storage", CreatureAbility.Carry);
            F("work.heat", "畑のそばで周りを温めていた", "Kept the field warm", CreatureAbility.Heat);
            F("work.cool", "畑のそばで周りを冷やしていた", "Kept the field cool", CreatureAbility.Cool);
            F("work.seedcollect", "収穫の後に種を集めていた", "Collected seeds after harvest", CreatureAbility.SeedCollect);
            F("combo.water_soil", "水やりと土づくりが重なった畑の作物がよく育っていた", "Crops grew well where watering and soil care overlapped");
            F("combo.till_sow", "耕したすぐ後に種がまかれていった", "Seeds were sown right after tilling");
            F("combo.harvest_carry", "収穫した作物がそのまま倉庫へ運ばれていった", "Harvests went straight to storage");
            F("combo.pollinate_glow", "夜の花畑で光と受粉が重なり、珍しい色の花が咲いた", "Light and pollination at night produced a rare-colored flower");

            // 生き物名人に見せた時の実演（言葉が分からなくても身振りで分かる：生き物仕様 §5.4・§11.1）
            foreach (CreatureAbility a in System.Enum.GetValues(typeof(CreatureAbility)))
            {
                AbilityText.Texts(a, out var ja, out var en);
                F("expert." + a, "生き物名人に見せると「" + ja + "」ような身振りをしてみせた", "When shown to the creature expert, they mimed: \"" + en + "\"", a);
            }
        }

        private static void P(int a, string ak, int b, string bk, string key, PairStyle style, string ja, string en)
        {
            var p = new PairInteraction { A = MakeId(a, ak), B = MakeId(b, bk), Key = "pair." + key, Style = style, Ja = ja, En = en };
            pairs.Add(p);
            F(p.Key, ja, en);
        }

        private static void BuildPairs()
        {
            // 生き物仕様 §7（＋家具仕様 §9.2 の例を追加）
            P(13, "kurumari", 12, "pyokoro", "kurumari_pyokoro", PairStyle.Chase, "転がるクルマリをピョコロが追いかけていた", "Pyokoro chased a rolling Kurumari");
            P(37, "mofupi", 35, "nemuriko", "mofupi_nemuriko", PairStyle.SleepTogether, "モフピとネムリコが丸くなって一緒に眠っていた", "Mofupi and Nemuriko slept curled up together");
            P(6, "hanapika", 15, "fuwatane", "hanapika_fuwatane", PairStyle.FlyTogether, "ハナピカとフワタネが一緒に飛び回っていた", "Hanapika and Fuwatane flew around together");
            P(32, "ranpuru", 7, "hikarimushi", "ranpuru_hikarimushi", PairStyle.AlternateGlow, "ランプルとヒカリムシが交互に光りながら遊んでいた", "Lampuru and Hikarimushi played, glowing in turns");
            P(31, "hakone", 38, "tsumitsumi", "hakone_tsumitsumi", PairStyle.Play, "ツミツミの集めた小石をハコネが箱にしまっていた", "Hakone tucked Tsumitsumi's pebbles into its box");
            P(1, "mizupuku", 21, "awarin", "mizupuku_awarin", PairStyle.Splash, "ミズプクとアワリンが水場で一緒に水遊びしていた", "Mizupuku and Awarin splashed together");
            P(33, "manepi", 34, "tonton", "manepi_tonton", PairStyle.CopyRhythm, "トントンのリズムをマネピが真似していた", "Manepi copied Tonton's rhythm");
            P(9, "pokaris", 35, "nemuriko", "pokaris_nemuriko", PairStyle.SleepTogether, "温かいポカリスにネムリコが寄り添って眠っていた", "Nemuriko slept snuggled against warm Pokaris");
            P(10, "tanekoro", 3, "tanepata", "tanekoro_tanepata", PairStyle.Play, "タネコロが集めた種をタネパタがついばんでいた", "Tanepata pecked at seeds Tanekoro gathered");
        }

        // ---------------- 餌・ケース（アイテム） ----------------

        public const string FoodBasic = "cfood.basic";
        public const string FoodFruit = "cfood.fruit_pellet";
        public const string FoodNectar = "cfood.nectar_drop";
        public const string FoodMineral = "cfood.mineral_cookie";
        public const string FoodGlow = "cfood.glow_powder";
        public const string FoodMoss = "cfood.moss_cake";
        public const string FoodWater = "cfood.water_jelly";
        public const string FoodSeed = "cfood.seed_mix";

        public const string TagCreatureFood = "creature_food";
        public const string TagCreatureCase = "creature_case";

        /// <summary>生物をケースに入れたアイテムID（売る・贈る・譲る・マルチの受け渡しに使う）。</summary>
        public static string CaseItemId(string speciesId) => "case." + speciesId;
        public static bool IsCaseItem(string itemId) => itemId != null && itemId.StartsWith("case.creature.");
        public static string SpeciesFromCase(string itemId) => IsCaseItem(itemId) ? itemId.Substring(5) : null;

        /// <summary>食べ物が好物の系統に合うか。</summary>
        public static bool FoodMatches(ItemDefinition def, string fav)
        {
            if (def == null || string.IsNullOrEmpty(fav)) return false;
            if (def.HasTag("cf." + fav)) return true;
            switch (fav)
            {
                case "fruit": return def.HasTag(ItemTags.Fruit);
                case "flower": return def.HasTag(ItemTags.Flower);
                case "seed": return def.HasTag(ItemTags.Grain) || def.Category == ItemCategory.Seed;
                case "glow": return def.HasTag(ItemTags.Glowing);
                case "veg": return def.HasTag(ItemTags.Vegetable);
                case "sweet": return def.HasTag(ItemTags.Sweet);
                case "mineral": return def.Category == ItemCategory.Ore || def.HasTag(ItemTags.Ore);
                case "water": return def.HasTag(ItemTags.Drink);
                case "moss": return def.HasTag("moss");
            }
            return false;
        }

        public static bool IsEdibleForCreature(ItemDefinition def)
        {
            if (def == null) return false;
            return def.Category == ItemCategory.CreatureFood || def.Category == ItemCategory.Crop || def.HasTag(ItemTags.Fruit) || def.HasTag(ItemTags.Vegetable);
        }

        public static string FoodLabel(string fav)
        {
            switch (fav)
            {
                case "fruit": return Loc.T("果物", "fruit");
                case "flower": return Loc.T("花", "flowers");
                case "seed": return Loc.T("種・穀物", "seeds & grain");
                case "glow": return Loc.T("光る物", "glowing things");
                case "veg": return Loc.T("野菜", "vegetables");
                case "sweet": return Loc.T("甘い物", "sweets");
                case "mineral": return Loc.T("鉱物", "minerals");
                case "water": return Loc.T("水気の多い物", "watery things");
                case "moss": return Loc.T("苔", "moss");
                default: return fav;
            }
        }

        /// <summary>餌アイテムの登録（RegisterContent から）。価格は【仮数値】。</summary>
        public static void RegisterItems()
        {
            Food(FoodBasic, "いきものフード", "Creature chow", "どの生き物も食べる基本の餌。置くと餌で誘う生き物が寄ってくる。", "Basic food most creatures eat. Place it to lure bait-loving creatures.", 30, new Color(0.85f, 0.72f, 0.5f));
            Food(FoodFruit, "木の実ペレット", "Fruit pellets", "甘い木の実を固めた餌。", "Pellets of sweet nuts and fruit.", 50, new Color(0.95f, 0.55f, 0.4f), "cf.fruit", "cf.sweet", ItemTags.Sweet);
            Food(FoodNectar, "花蜜ドロップ", "Nectar drops", "花の蜜を固めた餌。", "Drops of flower nectar.", 60, new Color(1f, 0.75f, 0.9f), "cf.flower", "cf.sweet", ItemTags.Sweet);
            Food(FoodMineral, "鉱石クッキー", "Mineral cookie", "鉱物の粉を焼き固めた、地下の生き物向けの餌。", "A baked cookie of mineral dust for underground creatures.", 70, new Color(0.7f, 0.7f, 0.75f), "cf.mineral");
            Food(FoodGlow, "ひかり粉", "Glow powder", "ほんのり光る粉。光る生き物が好む。", "A faintly glowing powder that glowing creatures love.", 90, new Color(0.75f, 1f, 0.7f), "cf.glow", ItemTags.Glowing);
            Food(FoodMoss, "苔もち", "Moss cake", "しっとりした苔のおもち。", "A moist cake of moss.", 50, new Color(0.45f, 0.7f, 0.4f), "cf.moss", "moss");
            Food(FoodWater, "水ゼリー", "Water jelly", "ぷるぷるの水のゼリー。", "A wobbly jelly of water.", 50, new Color(0.6f, 0.85f, 1f), "cf.water");
            Food(FoodSeed, "種ミックス", "Seed mix", "いろいろな種を混ぜた餌。", "A mix of assorted seeds.", 40, new Color(0.8f, 0.75f, 0.45f), "cf.seed");

            // 生物ケース（1種1アイテム）。ItemStack.data に個体データ（JSON）を入れる。
            foreach (var sp in All)
            {
                var def = new ItemDefinition
                {
                    Id = CaseItemId(sp.Id),
                    NameJa = sp.NameJa + "（ケース入り）",
                    NameEn = sp.NameEn + " (in case)",
                    DescriptionJa = "生き物が入ったケース。手に持って使うと外に出せる。商店に並べたり、贈ったりもできる。",
                    DescriptionEn = "A case with a creature inside. Use it while held to let it out. Can be sold or gifted.",
                    Category = ItemCategory.Misc,
                    BasePrice = sp.BasePrice,
                    MaxStack = 1,
                    Rarity = Mathf.Clamp(sp.Rarity, 0, 5),
                    Color = sp.Main,
                    Shape = PrimitiveType.Sphere,
                    ConceptId = Concepts.Species(sp.Id),
                    GenericConceptId = Concepts.CREATURE,
                    SourceActivity = "creatures",
                    ModuleData = sp
                };
                def.WithTags(TagCreatureCase, "creature", ItemTags.Cute);
                if (sp.Rarity >= 3) def.WithTags(ItemTags.Rare);
                if (sp.Glows) def.WithTags(ItemTags.Glowing);
                ItemRegistry.Register(def);
            }
        }

        private static void Food(string id, string ja, string en, string dja, string den, int price, Color color, params string[] tags)
        {
            var def = new ItemDefinition
            {
                Id = id,
                NameJa = ja,
                NameEn = en,
                DescriptionJa = dja,
                DescriptionEn = den,
                Category = ItemCategory.CreatureFood,
                BasePrice = price,
                MaxStack = 99,
                Color = color,
                Shape = PrimitiveType.Sphere,
                GenericConceptId = Concepts.FOOD,
                SourceActivity = "creatures"
            };
            def.WithTags(TagCreatureFood);
            def.WithTags(tags);
            ItemRegistry.Register(def);
        }
    }
}
