using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Cooking
{
    /// <summary>
    /// 工程・能力・ステーション・食べ物の効果の表（すべて【仮】。調整しやすいよう1か所にまとめる）。
    /// 圧搾・発酵・冷却・乾燥・燻煙を担当する生物は仕様上【未決定】のため、能力（CreatureAbility）で指定し、
    /// どの種族がその能力を持つかは Creatures モジュールの割当に任せる。
    /// </summary>
    public static class CookingTables
    {
        private static readonly Dictionary<CraftStep, StepInfo> steps = new Dictionary<CraftStep, StepInfo>();
        private static readonly Dictionary<CreatureAbility, AbilityInfo> abilities = new Dictionary<CreatureAbility, AbilityInfo>();
        private static readonly Dictionary<CraftStationType, StationProfile> stations = new Dictionary<CraftStationType, StationProfile>();
        private static readonly Dictionary<string, FoodEffectDef> effects = new Dictionary<string, FoodEffectDef>();

        public static IEnumerable<StepInfo> AllSteps => steps.Values;
        public static IEnumerable<FoodEffectDef> AllEffects => effects.Values;

        // 食べ物の効果ID（§7.2【提案】）
        public const string EffectSwift = "swift";            // 足取りが軽い（歩く・走るが少し速い）
        public const string EffectGlow = "glow";              // 体がほんのり光る（夜道が楽しい）
        public const string EffectWarm = "warm";              // ぽかぽか（湯気が立つ・見た目）
        public const string EffectFishingBite = "fishing_bite"; // 釣りのアタリが分かりやすい（Fishing の対応待ち）
        public const string EffectKeenEye = "keen_eye";       // 生き物・鉱石に気づきやすい（Creatures/Mining の対応待ち）
        public const string EffectCalm = "calm";              // 生き物が寄ってきやすい（Creatures の対応待ち）

        static CookingTables()
        {
            // ---- 工程 ----
            AddStep(CraftStep.Heat, "温める", "Heat", "温", new Color(1f, 0.55f, 0.3f), MinigameKind.HoldBand, "ぽかぽか…火加減を保とう", "Toasty... keep the heat steady", CreatureAbility.Heat);
            AddStep(CraftStep.Water, "水を入れる", "Add water", "水", new Color(0.4f, 0.75f, 1f), MinigameKind.StopAtLine, "ぷくぷく…ちょうどの水かさで止めよう", "Bloop... stop at the right level", CreatureAbility.Water, CreatureAbility.Splash);
            AddStep(CraftStep.Grind, "回して砕く", "Grind", "回", new Color(0.85f, 0.75f, 0.5f), MinigameKind.Mash, "ころころ…回してあげよう", "Roll roll... keep it spinning", CreatureAbility.Grind);
            AddStep(CraftStep.Bubble, "泡立てる", "Whip", "泡", new Color(0.8f, 0.95f, 1f), MinigameKind.Mash, "あわあわ…あふれないように", "Fizz... don't let it overflow", CreatureAbility.Bubble);
            AddStep(CraftStep.Press, "しぼる", "Press", "搾", new Color(0.95f, 0.5f, 0.7f), MinigameKind.HoldBand, "ぎゅーっ…しぼり加減を保とう", "Squeeze... hold the pressure", CreatureAbility.Press);
            AddStep(CraftStep.Ferment, "発酵させる", "Ferment", "醸", new Color(0.75f, 0.6f, 0.95f), MinigameKind.None, "ぷくぷく…あとは待つだけ", "Bubbling... now we wait", CreatureAbility.Ferment);
            AddStep(CraftStep.Cool, "冷やす", "Chill", "冷", new Color(0.6f, 0.9f, 1f), MinigameKind.StopAtLine, "ひんやり…青くなったら止めよう", "Brr... stop when it turns blue", CreatureAbility.Cool);
            AddStep(CraftStep.Dry, "干す", "Dry", "干", new Color(1f, 0.9f, 0.55f), MinigameKind.None, "ふわふわ…風と光にまかせよう", "Fluttering... let the air do it", CreatureAbility.Float);
            AddStep(CraftStep.Smoke, "燻す", "Smoke", "燻", new Color(0.6f, 0.6f, 0.65f), MinigameKind.None, "もくもく…香りがしみこむ", "Puff... the aroma soaks in", CreatureAbility.Shade, CreatureAbility.Heat);
            AddStep(CraftStep.Glow, "光らせる", "Glow", "光", new Color(1f, 1f, 0.55f), MinigameKind.Rhythm, "ぴかっ…光の拍に合わせよう", "Flash... match the pulse", CreatureAbility.Glow);
            AddStep(CraftStep.Stack, "積む・盛る", "Stack", "積", new Color(0.7f, 0.85f, 0.6f), MinigameKind.Rhythm, "つみつみ…まっすぐ積もう", "Stack... keep it straight", CreatureAbility.Stack);
            AddStep(CraftStep.Shape, "形を作る", "Shape", "形", new Color(0.9f, 0.8f, 0.7f), MinigameKind.StopAtLine, "こねこね…まんなかで止めよう", "Knead... stop in the middle", new CreatureAbility[0]);
            AddStep(CraftStep.Polish, "磨く", "Polish", "磨", new Color(0.8f, 0.85f, 0.95f), MinigameKind.Mash, "きゅっきゅっ…丁寧に磨こう", "Squeak... polish carefully", new CreatureAbility[0]);
            AddStep(CraftStep.Pack, "包む", "Wrap", "包", new Color(1f, 0.75f, 0.6f), MinigameKind.StopAtLine, "ぱたん…ちょうどよく包もう", "Fold... wrap it neatly", new CreatureAbility[0]);
            // 時間の工程【仮数値】：設備まかせ / 担当の生物がいる時
            steps[CraftStep.Ferment].Timed = true; steps[CraftStep.Ferment].BaseHours = 30f; steps[CraftStep.Ferment].FastHours = 12f;
            steps[CraftStep.Dry].Timed = true; steps[CraftStep.Dry].BaseHours = 12f; steps[CraftStep.Dry].FastHours = 6f;
            steps[CraftStep.Smoke].Timed = true; steps[CraftStep.Smoke].BaseHours = 10f; steps[CraftStep.Smoke].FastHours = 5f;

            // ---- 能力（アイコン＝1文字＋色、放置中のくらし §4.3）----
            AddAbility(CreatureAbility.Heat, "温", new Color(1f, 0.55f, 0.3f), "温める", "Warming", "かまどで丸まってぬくぬく眠っている", "is curled up asleep, warm and cozy");
            AddAbility(CreatureAbility.Water, "水", new Color(0.4f, 0.75f, 1f), "水を出す", "Water", "水場でぷかぷか遊んでいる", "is bobbing around in the water");
            AddAbility(CreatureAbility.Splash, "跳", new Color(0.45f, 0.8f, 0.95f), "水を飛ばす", "Splash", "ぴちゃぴちゃ跳ねている", "is splashing about");
            AddAbility(CreatureAbility.Grind, "回", new Color(0.85f, 0.75f, 0.5f), "回る・砕く", "Spin & grind", "台の上でころころ転がっている", "is rolling around the table");
            AddAbility(CreatureAbility.Bubble, "泡", new Color(0.8f, 0.95f, 1f), "泡を作る", "Bubbles", "ボウルの中で泡をふいている", "is blowing bubbles in the bowl");
            AddAbility(CreatureAbility.Press, "搾", new Color(0.95f, 0.5f, 0.7f), "しぼる", "Press", "体をぎゅっと縮めて伸びをしている", "is squishing and stretching");
            AddAbility(CreatureAbility.Ferment, "醸", new Color(0.75f, 0.6f, 0.95f), "発酵させる", "Ferment", "樽のそばでうとうとしている", "is dozing by the barrel");
            AddAbility(CreatureAbility.Cool, "冷", new Color(0.6f, 0.9f, 1f), "冷やす", "Chill", "箱の中でひんやり涼んでいる", "is chilling inside the box");
            AddAbility(CreatureAbility.Float, "浮", new Color(1f, 0.95f, 0.75f), "ふわふわ浮く", "Float", "棚のまわりをふわふわ漂っている", "is drifting around the rack");
            AddAbility(CreatureAbility.Shade, "影", new Color(0.45f, 0.45f, 0.55f), "影をまとう", "Shade", "箱の暗がりでじっとしている", "is resting in the dark corner");
            AddAbility(CreatureAbility.Glow, "光", new Color(1f, 1f, 0.55f), "光る", "Glow", "ゆっくり明滅している", "is slowly blinking with light");
            AddAbility(CreatureAbility.Stack, "積", new Color(0.7f, 0.85f, 0.6f), "積み重ねる", "Stack", "小石を積んで遊んでいる", "is stacking pebbles");
            AddAbility(CreatureAbility.Pack, "箱", new Color(1f, 0.75f, 0.6f), "箱に詰める", "Pack", "箱にすっぽり入っている", "is tucked inside a box");
            AddAbility(CreatureAbility.MineralDust, "粉", new Color(0.75f, 0.7f, 0.65f), "鉱石粉", "Mineral dust", "粉をふわりと散らしている", "is puffing out mineral dust");
            AddAbility(CreatureAbility.Carry, "運", new Color(1f, 0.85f, 0.4f), "運ぶ", "Carry", "棚の近くで休んでいる", "is resting near the shelf");
            AddAbility(CreatureAbility.Mimic, "真", new Color(0.95f, 0.7f, 1f), "まねる", "Mimic", "止まり木で誰かの仕草をまねている", "is mimicking someone on its perch");
            AddAbility(CreatureAbility.Sense, "感", new Color(0.7f, 1f, 0.8f), "感じ取る", "Sense", "耳をすませている", "is listening carefully");
            AddAbility(CreatureAbility.Sound, "音", new Color(0.95f, 0.85f, 0.6f), "音を鳴らす", "Sound", "殻をこつこつ鳴らしている", "is clicking its shell");
            AddAbility(CreatureAbility.ColorShift, "色", new Color(1f, 0.6f, 0.9f), "色を変える", "Color shift", "まわりの色にとけこんでいる", "is blending into the colors");
            AddAbility(CreatureAbility.Stabilize, "安", new Color(0.7f, 0.8f, 1f), "安定させる", "Stabilize", "静かにじっとしている", "is sitting very still");

            // ---- ステーション ----
            AddStation(new StationProfile
            {
                Type = CraftStationType.Hand, NameJa = "その場で", NameEn = "By hand",
                BuiltIn = new[] { CraftStep.Shape, CraftStep.Pack }, BatchMax = 3
            });
            AddStation(new StationProfile
            {
                // §2.1：生物がいなくても基本の料理ができる（住民が用意したかまどと水場）
                Type = CraftStationType.CommunityKitchen, NameJa = "村の共用調理場", NameEn = "Community kitchen",
                BuiltIn = new[] { CraftStep.Heat, CraftStep.Water, CraftStep.Shape, CraftStep.Pack },
                TimedSteps = new[] { CraftStep.Dry, CraftStep.Ferment }, TimedSlots = 2, BatchMax = 3,
                Outdoor = true, QualityOffset = -0.2f
            });
            AddStation(new StationProfile
            {
                Type = CraftStationType.Kitchen, NameJa = "自宅のキッチン", NameEn = "Home kitchen",
                BuiltIn = new[] { CraftStep.Shape, CraftStep.Pack }, BatchMax = 5
            });
            AddStation(new StationProfile
            {
                Type = CraftStationType.Workbench, NameJa = "加工作業台", NameEn = "Workbench",
                BuiltIn = new[] { CraftStep.Shape, CraftStep.Polish, CraftStep.Pack }, BatchMax = 5
            });
            AddStation(new StationProfile
            {
                // 鍛冶屋の炉と焼き入れ水槽を借りる（自分で叩く）か、鍛冶屋に頼む（ロケット仕様 §4.3・§9.3）
                Type = CraftStationType.Forge, NameJa = "鍛冶屋の炉", NameEn = "Blacksmith's forge",
                BuiltIn = new[] { CraftStep.Heat, CraftStep.Shape, CraftStep.Cool }, BatchMax = 3,
                CommissionJob = ResidentJob.Blacksmith, UseFee = 30
            });
            AddStation(new StationProfile
            {
                // 燃料精製設備。生物がいなくても時間をかければ精製できる（必須の生物収集を課さない）
                Type = CraftStationType.Refinery, NameJa = "燃料精製器", NameEn = "Fuel refinery",
                BuiltIn = new[] { CraftStep.Shape }, SlowCapable = new[] { CraftStep.Press, CraftStep.Heat, CraftStep.Grind },
                TimedSteps = new[] { CraftStep.Ferment }, TimedSlots = 3, BatchMax = 3,
                CommissionJob = ResidentJob.Mechanic, AcceptsWorkshopCreatures = true
            });
            AddStation(new StationProfile
            {
                // 修理作業場：修理台・冷却試験台（ロケット仕様 §11.2）
                Type = CraftStationType.RocketWorkshop, NameJa = "修理作業場", NameEn = "Repair workshop",
                BuiltIn = new[] { CraftStep.Shape, CraftStep.Polish, CraftStep.Cool },
                SlowCapable = new[] { CraftStep.Water, CraftStep.Heat },
                TimedSteps = new CraftStep[0], TimedSlots = 3, BatchMax = 3, Outdoor = true,
                CommissionJob = ResidentJob.Mechanic, AcceptsWorkshopCreatures = true
            });
            AddStation(new StationProfile
            {
                Type = CraftStationType.CreatureDevice, NameJa = "生き物といっしょに", NameEn = "With a creature",
                BuiltIn = new[] { CraftStep.Shape }, BatchMax = 2
            });
            AddStation(new StationProfile
            {
                Type = CraftStationType.CarpenterBench, NameJa = "家具職人の作業台", NameEn = "Carpenter's bench",
                BuiltIn = new[] { CraftStep.Shape, CraftStep.Polish, CraftStep.Press }, BatchMax = 3,
                CommissionJob = ResidentJob.Carpenter
            });

            // ---- 食べ物の効果（§7.2【提案】）----
            AddEffect(EffectSwift, "足取りかるい", "Light-footed", "しばらく歩くのが少し速くなる。", "You walk a little faster for a while.", "足", new Color(0.6f, 1f, 0.7f), true);
            AddEffect(EffectGlow, "ほんのり発光", "Softly glowing", "しばらく体がほんのり光る。夜道が楽しい。", "You glow softly for a while.", "光", new Color(1f, 1f, 0.6f), true);
            AddEffect(EffectWarm, "ぽかぽか", "Toasty", "体がぽかぽかして湯気が立つ。", "You feel toasty; steam rises.", "温", new Color(1f, 0.6f, 0.4f), true);
            AddEffect(EffectFishingBite, "うきうき釣り心", "Angler's hunch", "しばらく釣りのアタリが分かりやすい（釣り側の対応が必要）。", "Bites are easier to notice (needs fishing support).", "釣", new Color(0.5f, 0.8f, 1f), false);
            AddEffect(EffectKeenEye, "きらり目線", "Keen eye", "しばらく鉱石や生き物に気づきやすい（鉱山・生き物側の対応が必要）。", "You notice ores and creatures more easily (needs support).", "目", new Color(0.9f, 0.85f, 0.5f), false);
            AddEffect(EffectCalm, "ほっこり", "Cozy aura", "生き物が寄ってきやすい（生き物側の対応が必要）。", "Creatures come closer (needs creature support).", "和", new Color(1f, 0.8f, 0.9f), false);
        }

        private static void AddStep(CraftStep s, string ja, string en, string glyph, Color c, MinigameKind mg, string actJa, string actEn, params CreatureAbility[] abilities)
        {
            steps[s] = new StepInfo { Step = s, NameJa = ja, NameEn = en, Glyph = glyph, Color = c, Minigame = mg, ActionJa = actJa, ActionEn = actEn, Abilities = abilities ?? new CreatureAbility[0] };
        }

        private static void AddAbility(CreatureAbility a, string glyph, Color c, string ja, string en, string idleJa, string idleEn)
        {
            abilities[a] = new AbilityInfo { Ability = a, Glyph = glyph, Color = c, NameJa = ja, NameEn = en, IdleJa = idleJa, IdleEn = idleEn };
        }

        private static void AddStation(StationProfile p) => stations[p.Type] = p;

        private static void AddEffect(string id, string ja, string en, string dja, string den, string glyph, Color c, bool implemented)
        {
            effects[id] = new FoodEffectDef { Id = id, NameJa = ja, NameEn = en, DescJa = dja, DescEn = den, Glyph = glyph, Color = c, ImplementedHere = implemented };
        }

        public static StepInfo Step(CraftStep s) => steps[s];

        public static AbilityInfo Ability(CreatureAbility a)
        {
            if (abilities.TryGetValue(a, out var info)) return info;
            return new AbilityInfo { Ability = a, Glyph = "？", Color = Color.gray, NameJa = a.ToString(), NameEn = a.ToString(), IdleJa = "のんびりしている", IdleEn = "is relaxing" };
        }

        public static StationProfile Station(CraftStationType t)
        {
            if (stations.TryGetValue(t, out var p)) return p;
            return stations[CraftStationType.Hand];
        }

        public static FoodEffectDef Effect(string id)
        {
            if (id != null && effects.TryGetValue(id, out var e)) return e;
            return null;
        }
    }
}
