using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Fishing
{
    /// <summary>
    /// 宇宙魚1種の定義（釣り仕様 §7・§9）。ItemDefinition.ModuleData に入れて登録する。
    /// 日本語名・英語名は「主人公が付けた呼び名」（UI用）。住民が話す魚の名前は宇宙語（"ITEM:"+id から生成）。
    /// </summary>
    public sealed class FishSpecies
    {
        public string Id;
        public string NameJa, NameEn;
        public string DescJa, DescEn;
        public Color Color;
        public string ColorTag;
        public string[] ExtraTags = new string[0];
        public int BasePrice;
        /// <summary>0（ありふれた）〜5（伝説）。</summary>
        public int Rarity;
        public FishBehavior Behavior;
        /// <summary>1〜10。引きの強さ・粘り強さの目安。</summary>
        public int Difficulty;
        public float SizeMin, SizeMax;
        public FishWater Waters;
        public FishTime Times = FishTime.All;
        /// <summary>この天候でなければ現れない（地下湖では常に満たす扱い）。</summary>
        public WeatherNeed RequireWeather = WeatherNeed.Any;
        /// <summary>この天候なら現れやすい。</summary>
        public WeatherNeed[] LikeWeather = new WeatherNeed[0];
        /// <summary>好む釣り場の特徴タグ（"deep" "rocky" "shallow" "waterfall" "estuary" "crystal" など）。</summary>
        public string[] LikeSpotTags = new string[0];
        /// <summary>指定があればそのタグを持つ釣り場にしかいない。</summary>
        public string[] RequireSpotTags = new string[0];
        /// <summary>地下湖の出現階（0なら制限なし）。</summary>
        public int MinFloor, MaxFloor;
        /// <summary>好む餌・仕掛けのアイテムID。</summary>
        public string BaitId;
        public bool Schooling;
        public bool Glowing;
        public bool IsLegend => Rarity >= 5;
        public bool IsRocketMaterial;
        /// <summary>修理用素材としての特徴の説明（観察で分かる範囲）。</summary>
        public string RocketNoteJa, RocketNoteEn;
    }

    /// <summary>
    /// 宇宙魚の一覧（41種：通常・珍しい魚・修理用素材の魚2種・伝説5種）。
    /// 名前・数・数値はすべて【仮】（釣り仕様 §7「正式名称・総数は未決定」）。
    /// 四季は無いので季節魚は作らず、代わりに天候・宇宙固有現象・時間帯・場所で出会い方を変える。
    /// </summary>
    public static class FishCatalog
    {
        // ---- 餌・仕掛け（釣り仕様 §4.1）----
        public const string BaitWorm = "bait.star_worm";
        public const string BaitDough = "bait.dough";
        public const string BaitGlowLure = "bait.glow_lure";
        public const string BaitDeepSinker = "bait.deep_sinker";
        public const string BaitChimeLure = "bait.chime_lure";

        public const string TagBait = "bait";
        public const string TagLegend = "legend_fish";

        private static readonly List<FishSpecies> all = new List<FishSpecies>();
        private static readonly Dictionary<string, FishSpecies> byId = new Dictionary<string, FishSpecies>();

        public static IReadOnlyList<FishSpecies> All
        {
            get
            {
                if (all.Count == 0) Build();
                return all;
            }
        }

        public static FishSpecies Get(string id)
        {
            if (all.Count == 0) Build();
            if (id != null && byId.TryGetValue(id, out var s)) return s;
            return null;
        }

        private static FishSpecies Add(FishSpecies s)
        {
            all.Add(s);
            byId[s.Id] = s;
            return s;
        }

        private static FishSpecies F(string id, string ja, string en, string descJa, string descEn, Color color, string colorTag,
            int price, int rarity, FishBehavior behavior, int difficulty, float sizeMin, float sizeMax, FishWater waters, FishTime times)
        {
            return Add(new FishSpecies
            {
                Id = id, NameJa = ja, NameEn = en, DescJa = descJa, DescEn = descEn, Color = color, ColorTag = colorTag,
                BasePrice = price, Rarity = rarity, Behavior = behavior, Difficulty = difficulty,
                SizeMin = sizeMin, SizeMax = sizeMax, Waters = waters, Times = times
            });
        }

        private static void Build()
        {
            all.Clear();
            byId.Clear();
            const FishWater R = FishWater.River, P = FishWater.Pond, S = FishWater.Sea, U = FishWater.Underground;

            // ===== 川・池の身近な魚 =====
            F("fish.pipi_minnow", "ピピメダカ", "Pipi Minnow", "小さな青い魚。ぴぴっと跳ねるように泳ぐ。", "A tiny blue fish that swims in little hops.",
                new Color(0.4f, 0.7f, 1f), ItemTags.ColorBlue, 20, 0, FishBehavior.Calm, 1, 3f, 8f, R | P, FishTime.All).ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };
            var kuru = F("fish.kurukuru", "クルクルウオ", "Twirlfish", "円を描いて泳ぐ橙色の魚。目を回さないらしい。", "An orange fish that swims in circles without getting dizzy.",
                new Color(1f, 0.6f, 0.2f), ItemTags.ColorOrange, 45, 1, FishBehavior.Turner, 3, 10f, 22f, R, FishTime.Morning | FishTime.Afternoon);
            kuru.BaitId = BaitDough;
            var hane = F("fish.hane", "ハネウオ", "Hopfish", "水面からよく跳ぶ緑の魚。", "A green fish that loves to leap out of the water.",
                new Color(0.4f, 0.85f, 0.4f), ItemTags.ColorGreen, 50, 1, FishBehavior.Jumper, 3, 12f, 28f, R | P, FishTime.Afternoon | FishTime.Evening);
            hane.LikeWeather = new[] { WeatherNeed.Clear };
            var hikari = F("fish.hikari", "ヒカリウオ", "Glowfish", "夜になると体が光り、群れで泳ぐ。", "Glows at night and swims in schools.",
                new Color(1f, 0.95f, 0.45f), ItemTags.ColorYellow, 60, 1, FishBehavior.Calm, 2, 8f, 16f, R | P, FishTime.Night | FishTime.LateNight | FishTime.Evening);
            hikari.Schooling = true; hikari.Glowing = true; hikari.BaitId = BaitGlowLure;
            var ribbon = F("fish.ribbon", "リボンウオ", "Ribbonfin", "長いひれがリボンのように揺れる美しい魚。素早く向きを変える。", "A beautiful fish with ribbon-like fins. Turns sharply.",
                new Color(1f, 0.55f, 0.8f), ItemTags.ColorPink, 120, 2, FishBehavior.Turner, 5, 18f, 35f, P, FishTime.Morning | FishTime.EarlyMorning);
            ribbon.ExtraTags = new[] { ItemTags.Cute };
            ribbon.LikeWeather = new[] { WeatherNeed.Clear };
            var moss = F("fish.mossback", "コケセオイ", "Mossback", "背中に苔を生やしたのんびり屋。雨の日に浮いてくる。", "A slowpoke with moss on its back. Surfaces on rainy days.",
                new Color(0.35f, 0.6f, 0.3f), ItemTags.ColorGreen, 25, 0, FishBehavior.Calm, 2, 12f, 30f, P, FishTime.All);
            moss.LikeWeather = new[] { WeatherNeed.Rain };
            moss.BaitId = BaitDough;
            var loach = F("fish.pebble_loach", "コイシドジョウ", "Pebble Loach", "小石そっくりの黒い魚。岩の多い川底に潜る。", "A black fish that looks like a pebble. Hides on rocky riverbeds.",
                new Color(0.2f, 0.2f, 0.25f), ItemTags.ColorBlack, 30, 0, FishBehavior.Diver, 2, 8f, 18f, R, FishTime.All);
            loach.LikeSpotTags = new[] { "rocky" };
            loach.BaitId = BaitWorm;
            var carp = F("fish.sun_carp", "ひだまりゴイ", "Sunbask Carp", "晴れた午後に水面でひなたぼっこをする。なかなか疲れない。", "Basks at the surface on sunny afternoons. Hard to tire out.",
                new Color(1f, 0.7f, 0.3f), ItemTags.ColorOrange, 70, 1, FishBehavior.Endurance, 4, 25f, 60f, P, FishTime.Afternoon);
            carp.LikeWeather = new[] { WeatherNeed.Clear };
            carp.ExtraTags = new[] { ItemTags.Big };
            var trout = F("fish.mist_trout", "きりマス", "Mist Trout", "霧の朝、滝つぼの近くに現れる白いマス。突然走り出す。", "A white trout that appears near waterfalls on foggy mornings. Bolts suddenly.",
                new Color(0.9f, 0.95f, 1f), ItemTags.ColorWhite, 140, 2, FishBehavior.Rush, 5, 25f, 50f, R, FishTime.EarlyMorning | FishTime.Morning);
            trout.LikeWeather = new[] { WeatherNeed.Fog };
            trout.LikeSpotTags = new[] { "waterfall", "rocky" };
            var bubble = F("fish.bubble_perch", "アワアワ", "Bubble Perch", "口から小さな泡を出し続ける青い魚。雨の日に元気。", "A blue fish that keeps blowing bubbles. Lively on rainy days.",
                new Color(0.45f, 0.65f, 0.95f), ItemTags.ColorBlue, 30, 0, FishBehavior.Calm, 2, 10f, 20f, P | R, FishTime.Daytime);
            bubble.LikeWeather = new[] { WeatherNeed.Rain };
            var mud = F("fish.mud_pout", "ドロンコ", "Mud Pout", "浅い泥底でうとうとしている丸い魚。", "A round fish dozing in shallow mud.",
                new Color(0.75f, 0.5f, 0.3f), ItemTags.ColorOrange, 20, 0, FishBehavior.Calm, 1, 10f, 22f, P, FishTime.All);
            mud.LikeSpotTags = new[] { "shallow" };
            mud.LikeWeather = new[] { WeatherNeed.Rain };
            mud.BaitId = BaitWorm;
            var catfish = F("fish.snore_catfish", "イビキナマズ", "Snore Catfish", "夜の深みで寝息のような音を立てる。粘り強い。", "Makes snoring sounds in deep water at night. Very stubborn.",
                new Color(0.25f, 0.22f, 0.3f), ItemTags.ColorBlack, 80, 1, FishBehavior.Endurance, 5, 30f, 70f, P | R, FishTime.Night | FishTime.LateNight);
            catfish.LikeSpotTags = new[] { "deep" };
            catfish.ExtraTags = new[] { ItemTags.Big };
            catfish.BaitId = BaitWorm;
            var cloud = F("fish.cloud_puff", "クモワタウオ", "Cloudpuff", "綿雲のようにふわふわした白い魚。曇りの日にぷかりと跳ねる。", "A fluffy white fish that bobs up on cloudy days.",
                new Color(0.95f, 0.95f, 1f), ItemTags.ColorWhite, 55, 1, FishBehavior.Jumper, 3, 12f, 26f, P, FishTime.Daytime);
            cloud.LikeWeather = new[] { WeatherNeed.Cloudy };
            cloud.ExtraTags = new[] { ItemTags.Cute };
            var bloom = F("fish.bloom_koi", "ハナビラゴイ", "Petal Koi", "花びら模様のコイ。花潮の日には池じゅうで舞う。", "A koi with petal patterns. Dances all over the pond during bloom tides.",
                new Color(1f, 0.7f, 0.85f), ItemTags.ColorPink, 150, 2, FishBehavior.Turner, 4, 25f, 55f, P, FishTime.Daytime);
            bloom.LikeWeather = new[] { WeatherNeed.BloomTide };
            bloom.ExtraTags = new[] { ItemTags.Fragrant };
            var tetra = F("fish.lightrain_tetra", "ヒカリアメテトラ", "Lightrain Tetra", "光雨の日だけ現れる、光る雨粒のような小魚。群れで泳ぐ。", "Tiny fish like glowing raindrops, seen only during light-rain.",
                new Color(0.55f, 0.85f, 1f), ItemTags.ColorBlue, 110, 2, FishBehavior.Calm, 3, 4f, 9f, R | P, FishTime.All);
            tetra.RequireWeather = WeatherNeed.LightRain; tetra.Schooling = true; tetra.Glowing = true;
            tetra.ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };
            var twin = F("fish.twin_tail", "フタオウオ", "Twintail", "尾びれが二つある紫の魚。動きが読めない。", "A purple fish with two tails. Its moves are unpredictable.",
                new Color(0.65f, 0.4f, 0.9f), ItemTags.ColorPurple, 130, 2, FishBehavior.Erratic, 5, 18f, 36f, R | S, FishTime.Evening);
            twin.LikeSpotTags = new[] { "estuary" };
            var dawn = F("fish.dawn_salmon", "アケボノサケ", "Dawn Salmon", "夜明けに河口を駆け上がる桃色のサケ。走って、粘る。", "A pink salmon that rushes up estuaries at dawn.",
                new Color(1f, 0.6f, 0.6f), ItemTags.ColorPink, 260, 3, FishBehavior.Rush, 7, 45f, 90f, R | S, FishTime.EarlyMorning);
            dawn.LikeSpotTags = new[] { "estuary" };
            dawn.ExtraTags = new[] { ItemTags.Big };
            var tiny = F("fish.tiny_star_guppy", "ちびぼし", "Starlet Guppy", "星のような点々が光る小さな魚。夜の池で群れる。", "A tiny fish with starry glowing dots. Gathers in ponds at night.",
                new Color(0.9f, 0.9f, 1f), ItemTags.ColorWhite, 40, 1, FishBehavior.Calm, 2, 3f, 7f, P, FishTime.Night | FishTime.LateNight);
            tiny.Schooling = true; tiny.Glowing = true; tiny.ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };
            var thunder = F("fish.thunder_eel", "ピリピリウナギ", "Zap Eel", "体がぴりぴりする黄色いウナギ。雨の夜、深みで暴れる。", "A tingly yellow eel. Thrashes in deep water on rainy nights.",
                new Color(1f, 0.9f, 0.2f), ItemTags.ColorYellow, 180, 2, FishBehavior.Rush, 6, 40f, 85f, R | P, FishTime.Night | FishTime.LateNight | FishTime.Evening);
            thunder.LikeWeather = new[] { WeatherNeed.Rain, WeatherNeed.HeavyRain };
            thunder.LikeSpotTags = new[] { "deep" };
            thunder.ExtraTags = new[] { ItemTags.Conductive };
            thunder.BaitId = BaitWorm;
            var haze = F("fish.haze_glass", "カスミガラスウオ", "Hazeglass", "体が透き通ったガラスのような魚。結晶霞の日に姿を見せる。", "A glass-clear fish that shows itself on crystal-haze days.",
                new Color(0.85f, 0.95f, 1f, 1f), ItemTags.ColorWhite, 160, 2, FishBehavior.Calm, 4, 12f, 26f, R | P | S, FishTime.All);
            haze.RequireWeather = WeatherNeed.CrystalHaze; haze.Glowing = true; haze.ExtraTags = new[] { ItemTags.Shiny, ItemTags.Crystal };

            // ===== 海の魚 =====
            var sardine = F("fish.star_sardine", "ホシイワシ", "Star Sardine", "夜の海で星のように群れる小魚。", "Small fish that school like stars in the night sea.",
                new Color(0.85f, 0.9f, 1f), ItemTags.ColorWhite, 25, 0, FishBehavior.Calm, 1, 8f, 16f, S, FishTime.All);
            sardine.Schooling = true; sardine.LikeWeather = new[] { WeatherNeed.StarBand };
            var bream = F("fish.wave_bream", "ナミダイ", "Wave Bream", "波の模様をもつ赤い魚。朝の海で粘り強く引く。", "A red fish with wave patterns. Pulls hard in the morning sea.",
                new Color(0.95f, 0.35f, 0.35f), ItemTags.ColorRed, 75, 1, FishBehavior.Endurance, 4, 20f, 45f, S, FishTime.Morning | FishTime.EarlyMorning);
            var flounder = F("fish.moon_flounder", "ツキカレイ", "Moon Flounder", "月のように白く平たい魚。夜、深い海底に潜る。", "A flat fish white as the moon. Dives deep at night.",
                new Color(0.95f, 0.95f, 0.85f), ItemTags.ColorWhite, 85, 1, FishBehavior.Diver, 4, 20f, 45f, S, FishTime.Night | FishTime.LateNight);
            flounder.LikeSpotTags = new[] { "deep" };
            var sail = F("fish.windsail", "ホカケウオ", "Windsail", "帆のような背びれで風を受けて走る青い魚。強風の日に現れる。", "A blue fish that rides the wind with a sail fin. Appears on windy days.",
                new Color(0.3f, 0.55f, 1f), ItemTags.ColorBlue, 170, 2, FishBehavior.Rush, 6, 40f, 80f, S, FishTime.Daytime);
            sail.LikeWeather = new[] { WeatherNeed.StrongWind };
            var jelly = F("fish.jelly_float", "プカクラゲウオ", "Jellyfloat", "クラゲのように漂う紫の魚。逆雨の日は空へ昇ろうとする。", "A purple fish that drifts like a jellyfish. Tries to rise during reverse rain.",
                new Color(0.75f, 0.5f, 1f), ItemTags.ColorPurple, 65, 1, FishBehavior.Calm, 3, 10f, 25f, S | P, FishTime.All);
            jelly.Glowing = true; jelly.LikeWeather = new[] { WeatherNeed.ReverseRain };
            var puku = F("fish.puku_fugu", "プクフグ", "Puffpuff", "驚くと体がまんまるに膨らむ。途中で急に手ごわくなる。", "Puffs up when startled - suddenly gets tougher mid-fight.",
                new Color(1f, 0.75f, 0.8f), ItemTags.ColorPink, 90, 1, FishBehavior.Puffer, 4, 10f, 24f, S, FishTime.Daytime);
            puku.ExtraTags = new[] { ItemTags.Cute };
            var shell = F("fish.spiral_shell_fish", "カイガラウオ", "Shellback", "巻き貝を背負った魚。岩場の底へ潜る。", "Carries a spiral shell and dives to rocky bottoms.",
                new Color(1f, 0.65f, 0.4f), ItemTags.ColorOrange, 70, 1, FishBehavior.Diver, 4, 14f, 30f, S, FishTime.Afternoon | FishTime.Morning);
            shell.LikeSpotTags = new[] { "rocky" };
            var flyer = F("fish.starwind_flyer", "ホシカゼトビウオ", "Starwind Flyer", "星風に乗って海面を滑空する光る魚。", "A glowing fish that glides over the sea on the star wind.",
                new Color(1f, 0.95f, 0.5f), ItemTags.ColorYellow, 150, 2, FishBehavior.Jumper, 5, 20f, 40f, S, FishTime.All);
            flyer.RequireWeather = WeatherNeed.StarWind; flyer.Glowing = true;
            var ray = F("fish.orbit_ray", "ワクセイエイ", "Orbit Ray", "体のまわりに惑星の輪のような模様がある紫のエイ。", "A purple ray with planet-ring patterns around its body.",
                new Color(0.5f, 0.35f, 0.85f), ItemTags.ColorPurple, 300, 3, FishBehavior.Erratic, 7, 50f, 110f, S, FishTime.Night | FishTime.LateNight);
            ray.LikeSpotTags = new[] { "deep" };
            ray.ExtraTags = new[] { ItemTags.Big };
            ray.BaitId = BaitGlowLure;

            // ===== 鉱山の地下湖（鉱山モジュールが Layout.FishingSpots に "underground" タグ付きで追加）=====
            var echo = F("fish.echo_goby", "コダマハゼ", "Echo Goby", "洞窟の水たまりにいる小さなハゼ。声が反響する場所が好き。", "A tiny goby of cave pools. Likes echoing places.",
                new Color(0.7f, 0.65f, 0.6f), ItemTags.ColorWhite, 30, 0, FishBehavior.Calm, 2, 5f, 12f, U, FishTime.All);
            echo.MinFloor = 1; echo.MaxFloor = 10;
            var blind = F("fish.cave_blind", "メナシウオ", "Cave Blindfish", "目のない真っ白な魚。水の揺れで周りを知る。", "A pure white fish without eyes. Senses its surroundings by water motion.",
                new Color(0.97f, 0.97f, 0.97f), ItemTags.ColorWhite, 60, 1, FishBehavior.Calm, 3, 12f, 26f, U, FishTime.All);
            blind.MinFloor = 2; blind.MaxFloor = 10;
            var crystal = F("fish.crystal", "クリスタルフィッシュ", "Crystalfish", "半透明で鉱石のように光る魚。地下湖に棲む。", "Translucent and glittering like ore. Lives in underground lakes.",
                new Color(0.75f, 0.9f, 1f), ItemTags.ColorBlue, 150, 2, FishBehavior.Diver, 5, 15f, 32f, U, FishTime.All);
            crystal.Glowing = true; crystal.MinFloor = 3; crystal.MaxFloor = 10; crystal.ExtraTags = new[] { ItemTags.Shiny, ItemTags.Crystal };
            var angler = F("fish.lantern_angler", "チョウチンさん", "Lantern Angler", "頭の提灯で暗い湖を照らす。深く潜って粘る。", "Lights the dark lake with its head lantern. Dives deep.",
                new Color(1f, 0.85f, 0.3f), ItemTags.ColorYellow, 260, 3, FishBehavior.Diver, 7, 30f, 60f, U, FishTime.All);
            angler.Glowing = true; angler.MinFloor = 6; angler.MaxFloor = 10; angler.BaitId = BaitGlowLure;
            var gemscale = F("fish.gem_scale", "ホウセキウロコ", "Gemscale", "鱗の一枚一枚が宝石のように輝く。結晶の湖でひらりと身をかわす。", "Every scale shines like a gem. Dodges nimbly in crystal lakes.",
                new Color(0.8f, 0.45f, 1f), ItemTags.ColorPurple, 320, 3, FishBehavior.Turner, 7, 20f, 40f, U, FishTime.All);
            gemscale.Glowing = true; gemscale.MinFloor = 8; gemscale.MaxFloor = 10; gemscale.ExtraTags = new[] { ItemTags.Shiny, ItemTags.Gem };

            // ===== ロケット修理用（ロケット仕様 §6。低確率待ちだけにしない：時刻・天候・場所・餌・観察で見つけられる）=====
            var frost = F(SharedItemIds.FishFrostFin, "ひやひれ（フロストフィン）", "Frostfin",
                "氷のように冷たい膜のひれを持つ魚。温度差に強い。冷たく深い水の底に潜る。早朝や霧・雨の日、滝つぼ・深み、浅い地下湖で見かける。",
                "A fish with ice-cold membrane fins that resist temperature swings. Dives in cold deep water. Seen early mornings, in fog or rain, near waterfalls, deep water and shallow underground lakes.",
                new Color(0.7f, 0.9f, 1f), ItemTags.ColorBlue, 220, 2, FishBehavior.Diver, 5, 20f, 42f, R | P | U,
                FishTime.EarlyMorning | FishTime.Morning | FishTime.Night | FishTime.LateNight);
            frost.LikeSpotTags = new[] { "waterfall", "deep", "cold", "rocky" };
            frost.LikeWeather = new[] { WeatherNeed.Fog, WeatherNeed.Rain };
            frost.MinFloor = 3; frost.MaxFloor = 6;
            frost.BaitId = BaitDeepSinker;
            frost.IsRocketMaterial = true;
            frost.ExtraTags = new[] { ItemTags.Cooling, ItemTags.RocketMaterial, ItemTags.Rare };
            frost.RocketNoteJa = "ひれの膜は熱にも冷たさにも強い。冷却の素材になりそうだ。";
            frost.RocketNoteEn = "Its fin membrane withstands heat and cold - could serve as a cooling material.";

            var ripple = F(SharedItemIds.FishRippleEye, "ゆらめ（リップルアイ）", "Rippleeye",
                "水の揺れ・振動・空間の揺らぎを感じ取る大きな目の魚。風のない静かな夜の海や池の深みに現れ、逆雨や星の光帯の夜には浮いてくる。鈴の音のする仕掛けに寄る。深い地下湖にもいる。",
                "A big-eyed fish that senses ripples, vibrations and spatial fluctuations. Appears on calm, windless nights in the sea or deep ponds, and rises on reverse-rain or star-band nights. Drawn to chiming lures. Also lives in deep underground lakes.",
                new Color(0.55f, 0.95f, 0.85f), ItemTags.ColorGreen, 380, 3, FishBehavior.Turner, 6, 25f, 50f, S | P | U, FishTime.Night | FishTime.LateNight);
            ripple.RequireWeather = WeatherNeed.Calm;
            ripple.LikeWeather = new[] { WeatherNeed.ReverseRain, WeatherNeed.StarBand };
            ripple.LikeSpotTags = new[] { "deep" };
            ripple.MinFloor = 7; ripple.MaxFloor = 9;
            ripple.BaitId = BaitChimeLure;
            ripple.Glowing = true;
            ripple.IsRocketMaterial = true;
            ripple.ExtraTags = new[] { ItemTags.Sensor, ItemTags.RocketMaterial, ItemTags.Rare, ItemTags.Precision };
            ripple.RocketNoteJa = "大きな目の奥の器官が、わずかな揺れにも反応して光る。観測装置に使えそうだ。";
            ripple.RocketNoteEn = "An organ behind its big eyes glows at the faintest vibration - could be used in an observation device.";

            // ===== 伝説の魚（§9：固有の行動。休息後は再び現れる）=====
            var lord = F("fish.legend_river_lord", "カワヌシ", "River Lord", "川の深みに棲む巨大な主。雨の夕方にだけ姿を見せる。走り、潜り、決して諦めない。",
                "The giant lord of the river depths. Shows itself only on rainy evenings. Runs, dives and never gives up.",
                new Color(0.3f, 0.45f, 0.35f), ItemTags.ColorGreen, 1500, 5, FishBehavior.LegendLord, 9, 120f, 180f, R, FishTime.Evening);
            lord.RequireWeather = WeatherNeed.Rain; lord.RequireSpotTags = new[] { "deep" };
            var whale = F("fish.legend_star_whale", "ホシクジラウオ", "Star Whale Fish", "星の光帯が出た夜、沖に現れる星模様の巨大魚。潮のように寄せては返す。",
                "A star-patterned giant that appears offshore on star-band nights. Pulls like the tide.",
                new Color(0.25f, 0.3f, 0.7f), ItemTags.ColorBlue, 2000, 5, FishBehavior.LegendTide, 9, 150f, 240f, S, FishTime.Night | FishTime.LateNight);
            whale.RequireWeather = WeatherNeed.StarBand; whale.Glowing = true;
            var yuragi = F("fish.legend_yuragi", "ユラギノヌシ", "Wavering Lord", "逆雨の日、池の深みで景色ごと揺らぐ不思議な魚。どこを向いているのか分からなくなる。",
                "On reverse-rain days, this strange fish makes the scenery waver in deep ponds. You can't tell which way it faces.",
                new Color(0.6f, 0.95f, 0.9f), ItemTags.ColorGreen, 1800, 5, FishBehavior.LegendPhantom, 9, 80f, 130f, P, FishTime.All);
            yuragi.RequireWeather = WeatherNeed.ReverseRain; yuragi.Glowing = true;
            var dragon = F("fish.legend_crystal_dragon", "ケッショウリュウギョ", "Crystal Dragonfish", "鉱山の最深部の湖に棲む、結晶の鱗をもつ竜のような魚。体を固めた瞬間は岩のように重い。",
                "A dragon-like fish with crystal scales in the deepest mine lake. When it stiffens, it is heavy as stone.",
                new Color(0.8f, 0.6f, 1f), ItemTags.ColorPurple, 2200, 5, FishBehavior.LegendCrystal, 10, 100f, 160f, U, FishTime.All);
            dragon.MinFloor = 10; dragon.MaxFloor = 10; dragon.Glowing = true;
            var nymph = F("fish.legend_lightrain_nymph", "アマヒカリ", "Lightrain Nymph", "光雨の朝、光の雨粒と一緒に跳ね続ける羽衣のような魚。",
                "On light-rain mornings this robe-finned fish keeps leaping with the shining raindrops.",
                new Color(1f, 0.95f, 0.7f), ItemTags.ColorYellow, 1600, 5, FishBehavior.LegendSkylark, 9, 60f, 100f, R | P, FishTime.EarlyMorning | FishTime.Morning);
            nymph.RequireWeather = WeatherNeed.LightRain; nymph.Glowing = true;
        }

        // ---------------- アイテム登録 ----------------

        public static void RegisterItems()
        {
            foreach (var s in All)
            {
                var def = new ItemDefinition
                {
                    Id = s.Id,
                    NameJa = s.NameJa,
                    NameEn = s.NameEn,
                    DescriptionJa = s.DescJa + (string.IsNullOrEmpty(s.RocketNoteJa) ? "" : "\n" + s.RocketNoteJa),
                    DescriptionEn = s.DescEn + (string.IsNullOrEmpty(s.RocketNoteEn) ? "" : "\n" + s.RocketNoteEn),
                    Category = ItemCategory.Fish,
                    BasePrice = s.BasePrice,
                    MaxStack = s.IsLegend ? 10 : 99,
                    Rarity = s.Rarity,
                    Consumable = true,
                    Color = s.Color,
                    Shape = PrimitiveType.Capsule,
                    GenericConceptId = Concepts.FISH,
                    SourceActivity = "fishing",
                    ModuleData = s
                };
                def.WithTags(ItemTags.Fish, ItemTags.Ingredient, ItemTags.Edible, s.ColorTag);
                def.WithTags(s.ExtraTags);
                if (s.Glowing) def.WithTags(ItemTags.Glowing);
                if (s.Rarity >= 3) def.WithTags(ItemTags.Rare);
                if (s.IsLegend) def.WithTags(TagLegend, ItemTags.Gift);
                if (s.SizeMax >= 60f) def.WithTags(ItemTags.Big);
                ItemRegistry.Register(def);
            }

            RegisterBait(BaitWorm, "星ミミズ", "Star worm", "小さく光るミミズ。たいていの魚が食いつき、アタリが早くなる。",
                "A tiny glowing worm. Most fish bite faster.", 6, new Color(0.9f, 0.55f, 0.6f), 0);
            RegisterBait(BaitDough, "練り餌", "Dough bait", "穀物を練った餌。草食の魚が好む。", "Kneaded grain bait. Plant-eating fish like it.",
                8, new Color(0.95f, 0.85f, 0.6f), 0);
            RegisterBait(BaitGlowLure, "光るルアー", "Glow lure", "暗い水の中で光るルアー。夜や深い所の魚が寄る。めったに失くさない。",
                "A lure that glows in dark water. Attracts night and deep-water fish. Rarely lost.", 80, new Color(1f, 0.95f, 0.4f), 1);
            RegisterBait(BaitDeepSinker, "深場の仕掛け", "Deep sinker rig", "重りで冷たい深みまで沈める仕掛け。底の魚を狙える。",
                "A weighted rig that sinks into cold depths. Targets bottom dwellers.", 45, new Color(0.5f, 0.6f, 0.75f), 1);
            RegisterBait(BaitChimeLure, "鈴鳴りルアー", "Chime lure", "水の中でかすかに鈴の音を響かせるルアー。揺れや振動を感じる魚が寄ってくる。",
                "A lure that faintly chimes underwater. Fish that sense vibrations come closer.", 140, new Color(0.6f, 1f, 0.85f), 2);
        }

        private static void RegisterBait(string id, string ja, string en, string dja, string den, int price, Color color, int rarity)
        {
            var def = new ItemDefinition
            {
                Id = id, NameJa = ja, NameEn = en, DescriptionJa = dja + "\n（手に持って水辺で使う）", DescriptionEn = den + "\n(Hold it and use it at the water)",
                Category = ItemCategory.Bait, BasePrice = price, MaxStack = 99, Rarity = rarity, Color = color,
                Shape = PrimitiveType.Sphere, GenericConceptId = Concepts.THING, SourceActivity = "fishing"
            };
            def.WithTags(TagBait, ItemTags.Tool);
            ItemRegistry.Register(def);
        }

        /// <summary>ルアー類は使っても失くなりにくい（消費確率）。【仮数値】</summary>
        public static float BaitConsumeChance(string baitId)
        {
            switch (baitId)
            {
                case BaitGlowLure: return 0.12f;
                case BaitChimeLure: return 0.12f;
                case BaitDeepSinker: return 0.3f;
                default: return 1f;
            }
        }

        public static bool IsBait(string itemId) => itemId != null && itemId.StartsWith("bait.");
    }
}
