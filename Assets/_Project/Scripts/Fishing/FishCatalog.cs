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
        /// <summary>原本の整理ID（F001〜F050）。</summary>
        public string No;
        public string NameJa, NameEn;
        public string DescJa, DescEn;
        /// <summary>原本のサイズ区分（XS/S/M/L/XL）。</summary>
        public string SizeClass;
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
        /// <summary>好む釣り場の特徴タグ（"deep" "rocky" "pool" "quiet" など）。</summary>
        public string[] LikeSpotTags = new string[0];
        /// <summary>指定があれば、どれか1つのタグを持つ釣り場にしかいない（水域のタグ "lake" "river" "sea" も使える）。</summary>
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
    /// 宇宙魚50種（一般魚20・個性派魚15・希少魚10・伝説魚5）。
    /// 正：『宇宙人の星』宇宙魚50種類 デザイン・生態・釣り方仕様案 v0.2（モデル制作中の50種。Fish/design/fish_50.json）。
    /// 名前・外見・生息候補・行動タイプ・修理の候補は原本どおり。価格・希少度・難しさ・大きさ(cm)は【仮数値】。
    /// 生息候補は、地形生成（World）が釣り場に付けるタグに置き換えている（マップ設計 v0.2 §2.3・§2.4・§2.11・§8.3）。
    ///   川："river" "rapids"(急流) "fast"(早瀬) "riffle"(瀬) "pool"(淵) "slack"(よどみ) "weed"(水草帯) "rocky" "spring"(冷たい湧水) "waterfall" "estuary"
    ///   池："pond" "spring"(湧水口) "mud"(泥底) "turbid"(濁り) "quiet"(静かな岸) "shallow"
    ///   湖："lake" "shallow"(浅場) "mid"(中層) "deep" "deepest"(最深部) "mud" "sandmud" "turbid" "quiet" "foggy" "wide"
    ///   海："sea" "beach"(砂底) "rocky" "bottom"(海底近く) "offshore"(沖) "pier" "sandmud" "deep"
    ///   地下湖："underground" "shore_shallow"(浅い岸) "ore_shore"(鉱石の多い岸) "deep" "deepest" "crystal"
    ///   滝の裏："behind_waterfall" "dark"
    /// 四季は無いので季節魚は作らず、時間帯・天候・宇宙固有現象・場所で出会い方を変える（宇宙固有現象は「さらに出やすく」だけで必須にしない）。
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
            if (id == null) return null;
            if (byId.TryGetValue(id, out var s)) return s;
            if (LegacyIds.TryGetValue(id, out var to) && byId.TryGetValue(to, out s)) return s;
            return null;
        }

        /// <summary>F番号（"F022"）から引く。</summary>
        public static FishSpecies ByNo(string no)
        {
            foreach (var s in All) if (s.No == no) return s;
            return null;
        }

        private static FishSpecies Add(FishSpecies s)
        {
            all.Add(s);
            byId[s.Id] = s;
            return s;
        }

        private static string IdOf(int no, string slug) => "fish.f" + no.ToString("000") + "_" + slug;

        /// <summary>1種を定義する。サイズ区分から大きさ(cm)の範囲を決める（原本 SPEC の仮の値の中で）。</summary>
        private static FishSpecies F(int no, string slug, string ja, string en, string size, string descJa, string descEn,
            Color color, string colorTag, int rarity, FishBehavior behavior, int difficulty, int price, FishWater waters)
        {
            float mn, mx;
            switch (size)
            {
                case "XS": mn = 3f; mx = 7f; break;
                case "S": mn = 9f; mx = 18f; break;
                case "M": mn = 22f; mx = 45f; break;
                case "L": mn = 55f; mx = 110f; break;
                default: mn = 160f; mx = 320f; break;
            }
            return Add(new FishSpecies
            {
                Id = IdOf(no, slug), No = "F" + no.ToString("000"), NameJa = ja, NameEn = en, SizeClass = size,
                DescJa = descJa, DescEn = descEn, Color = color, ColorTag = colorTag,
                BasePrice = price, Rarity = rarity, Behavior = behavior, Difficulty = difficulty,
                SizeMin = mn, SizeMax = mx, Waters = waters
            });
        }

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        private static void Build()
        {
            all.Clear();
            byId.Clear();
            const FishWater R = FishWater.River, P = FishWater.Pond, L = FishWater.Lake, S = FishWater.Sea, U = FishWater.Underground;
            FishSpecies f;

            // ================= 一般魚 F001〜F020 =================
            f = F(1, "marukoro", "マルコロ", "Marukoro", "S", "桃色のまんまるな体に小さな三角ヒレと団扇のような尾。のんびり泳ぐ。あまり抵抗しない。",
                "A round pink fish with tiny triangle fins and a fan tail. Swims lazily and barely resists.",
                C(1f, 0.62f, 0.72f), ItemTags.ColorPink, 0, FishBehavior.Calm, 1, 20, R | P);
            f.ExtraTags = new[] { ItemTags.Cute };
            f = F(2, "sasahire", "ササヒレ", "Sasahire", "S", "笹の葉形の細長い黄緑の体。水草の間に暮らし、左右にゆっくり揺れる。",
                "A slim yellow-green fish shaped like a bamboo leaf. Lives among water weeds and sways slowly.",
                C(0.72f, 0.88f, 0.35f), ItemTags.ColorGreen, 0, FishBehavior.Calm, 1, 22, R);
            f.RequireSpotTags = new[] { "weed" };
            f = F(3, "chorosuji", "チョロスジ", "Chorosuji", "S", "水色の流線形に白い二本線。流れのある瀬を素早く泳ぎ、短いダッシュを繰り返す。",
                "A sleek light-blue fish with two white stripes. Darts through riffles in short bursts.",
                C(0.5f, 0.82f, 1f), ItemTags.ColorBlue, 0, FishBehavior.Rush, 2, 26, R);
            f.RequireSpotTags = new[] { "riffle", "fast" };
            f = F(4, "higetama", "ヒゲタマ", "Higetama", "M", "茶色い丸い顔に6本の長いひげ。泥底をひげで探る。夕方から夜に活発で、底に張り付くように抵抗する。",
                "A brown round-faced fish with six long whiskers that probes muddy bottoms. Active from evening into night; clings to the bottom.",
                C(0.58f, 0.42f, 0.28f), ItemTags.ColorOrange, 1, FishBehavior.Diver, 3, 48, P | L);
            f.RequireSpotTags = new[] { "mud" };
            f.Times = FishTime.Evening | FishTime.Night | FishTime.LateNight;
            f.BaitId = BaitWorm;
            f = F(5, "funebire", "フネビレ", "Funebire", "M", "小舟のような平たいお腹と左右に張り出したヒレ。水面近くを泳ぎ、時々小さく跳ねる。",
                "A boat-bellied fish with fins sticking out to the sides. Swims near the surface and hops now and then.",
                C(0.95f, 0.78f, 0.45f), ItemTags.ColorYellow, 0, FishBehavior.Jumper, 2, 30, R);
            f.LikeSpotTags = new[] { "slack", "pool" };
            f = F(6, "kometsubu", "コメツブウオ", "Rice-grain Fish", "XS", "白い米粒のような極小魚。尾に黒い点。群れで行動し、小さなアタリが何度も来る。",
                "A tiny white fish like a grain of rice, with a black dot on its tail. Moves in schools; nibbles again and again.",
                C(0.97f, 0.97f, 0.92f), ItemTags.ColorWhite, 0, FishBehavior.Calm, 1, 15, P);
            f.Schooling = true; f.LikeSpotTags = new[] { "shallow" }; f.ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };
            f.BaitId = BaitDough;
            f = F(7, "peraobi", "ペラオビ", "Peraobi", "M", "黄橙色の楕円の体。背から尾まで続く帯状の背びれを波打たせ、前にも後ろにも泳ぐ。突然後ろへ下がって糸をたるませる。",
                "An oval yellow-orange fish with a long ribbon dorsal fin that lets it swim forward or backward. Suddenly backs up to slacken the line.",
                C(1f, 0.72f, 0.3f), ItemTags.ColorOrange, 1, FishBehavior.Turner, 3, 52, L | R);
            f.RequireSpotTags = new[] { "lake", "slack" };
            f = F(8, "shimomadara", "シモマダラ", "Shimomadara", "M", "灰青色の体に霜のような白い斑点。体表の粘膜がひんやり冷気を帯びる。冷たい湧き水の出る淵に集まり、冷たい流れへ戻ろうと粘り続ける。",
                "A grey-blue fish speckled like frost; its slime coat is cold to the touch. Gathers in pools fed by cold springs and keeps pulling back toward the cold water.",
                C(0.55f, 0.65f, 0.78f), ItemTags.ColorBlue, 1, FishBehavior.Endurance, 4, 85, R | P);
            f.RequireSpotTags = new[] { "spring" };
            f.LikeSpotTags = new[] { "cold", "pool" };
            f.BaitId = BaitDeepSinker;
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Cooling, ItemTags.RocketMaterial };
            f.RocketNoteJa = "体を包む冷たい膜は温度差に強い。冷却膜（標準）の素材になりそうだ。";
            f.RocketNoteEn = "Its cold slime membrane resists temperature changes - could make a standard cooling membrane.";
            f = F(9, "nagakuchi", "ナガクチ", "Nagakuchi", "S", "ラッパのような細長い口のクリーム色の小魚。岩の隙間の餌を吸い込み、隙間へ戻ろうと急に引く。地下湖の浅い岸にもいる。",
                "A cream fish with a trumpet mouth that sucks food from rock crevices, then yanks back toward them. Also lives on shallow underground shores.",
                C(0.95f, 0.9f, 0.74f), ItemTags.ColorWhite, 0, FishBehavior.Diver, 2, 28, R | U);
            f.RequireSpotTags = new[] { "rocky", "shore_shallow" };
            f.MinFloor = 2; f.MaxFloor = 4;
            f = F(10, "yurashippo", "ユラシッポ", "Yurashippo", "S", "小さな水滴形の体に大きなフリル状の尾びれ。湖の浅場を優雅に泳ぐ。水槽映えする。",
                "A droplet-shaped fish with a huge frilly tail. Glides elegantly in the lake shallows.",
                C(0.78f, 0.86f, 1f), ItemTags.ColorBlue, 1, FishBehavior.Calm, 1, 45, L);
            f.RequireSpotTags = new[] { "shallow" }; f.ExtraTags = new[] { ItemTags.Cute };
            f = F(11, "yamagiza", "ヤマギザ", "Yamagiza", "M", "灰青色の頑丈な体に山脈のようなギザギザの背びれ。岩場や滝つぼにいて、2回激しく首を振った後に隙ができる。",
                "A sturdy grey-blue fish with a mountain-range dorsal fin. Lives on rocky stretches and plunge pools; thrashes twice, then leaves an opening.",
                C(0.48f, 0.56f, 0.66f), ItemTags.ColorBlue, 1, FishBehavior.Rush, 4, 60, R);
            f.RequireSpotTags = new[] { "rocky", "waterfall" };
            f = F(12, "kobuhire", "コブヒレ", "Kobuhire", "M", "薄黄色の体。背びれに3つの丸いコブ。湖の中層をゆっくり、しかし力強く泳ぎ、なかなか疲れない。",
                "A pale yellow fish with three round lumps on its dorsal fin. Cruises the lake's middle depths, slow but strong and tireless.",
                C(0.98f, 0.92f, 0.6f), ItemTags.ColorYellow, 1, FishBehavior.Endurance, 4, 58, L);
            f.RequireSpotTags = new[] { "mid", "deep" };
            f = F(13, "gyorome", "ギョロメ", "Gyorome", "S", "大きく飛び出た丸い目の小魚。朝の浅瀬で目をきょろきょろ動かして見張っている。強く引いた直後に突然止まる。",
                "A small fish with huge bulging eyes that keeps watch in morning shallows. Pulls hard, then stops dead.",
                C(1f, 0.66f, 0.36f), ItemTags.ColorOrange, 1, FishBehavior.Rush, 2, 40, R | P);
            f.RequireSpotTags = new[] { "shallow", "riffle" };
            f.Times = FishTime.EarlyMorning | FishTime.Morning;
            f = F(14, "tsunosaki", "ツノサキ", "Tsunosaki", "M", "青紫色の細身。尖った口先の上に小さな角。夕方に群れで回遊し、前へ勢いよく突進する。",
                "A slender blue-violet fish with a small horn above its pointed snout. Roams in schools at dusk and charges forward.",
                C(0.5f, 0.45f, 0.9f), ItemTags.ColorPurple, 1, FishBehavior.Rush, 3, 55, S);
            f.RequireSpotTags = new[] { "beach", "pier", "rocky" };
            f.Times = FishTime.Evening; f.Schooling = true;
            f = F(15, "nijisuji", "ニジスジ", "Nijisuji", "M", "白銀の体を虹色の一本線が横切る。雨上がりは線の色が鮮やかになる。小さく方向転換を繰り返す。",
                "A silver fish crossed by one rainbow stripe that brightens after rain. Keeps making small turns.",
                C(0.9f, 0.92f, 0.96f), ItemTags.ColorWhite, 0, FishBehavior.Turner, 2, 34, R | L);
            f = F(16, "shiohira", "シオヒラ", "Shiohira", "M", "銀色のひし形の薄く平たい体。海底近くを横滑りし、左右に滑るように切り返す。海の入門魚。",
                "A thin silver diamond-shaped fish that slides sideways near the sea floor, cutting left and right.",
                C(0.82f, 0.85f, 0.9f), ItemTags.ColorWhite, 0, FishBehavior.Turner, 2, 30, S);
            f.LikeSpotTags = new[] { "bottom" };
            f = F(17, "sangohoho", "サンゴホホ", "Sangohoho", "S", "両頬に珊瑚の花びらのようなヒレを持つ桃橙色の海魚。岩陰を住処にし、岩陰へ逃げ込もうとする。",
                "A peach-orange sea fish with coral-petal fins on its cheeks. Lives in rocky nooks and tries to dash back into them.",
                C(1f, 0.6f, 0.48f), ItemTags.ColorOrange, 1, FishBehavior.Diver, 3, 50, S);
            f.RequireSpotTags = new[] { "rocky" }; f.ExtraTags = new[] { ItemTags.Cute };
            f = F(18, "hiraboushi", "ヒラボウシ", "Hiraboushi", "M", "砂色の平たい体。頭の上に平たい帽子のような皮弁があり、砂に潜ると帽子だけが覗く。",
                "A flat sand-colored fish with a hat-like flap on its head. When buried in sand only the hat shows.",
                C(0.86f, 0.76f, 0.56f), ItemTags.ColorYellow, 1, FishBehavior.Diver, 3, 48, S);
            f.RequireSpotTags = new[] { "beach" };
            f = F(19, "shimakuchi", "シマクチ", "Shimakuchi", "M", "くちばし状の口に黒白の縞模様。桟橋のまわりで群れを作り、取り込み直前に粘る。",
                "A black-and-white striped fish with a beak-like mouth. Schools around piers and digs in just before landing.",
                C(0.2f, 0.2f, 0.24f), ItemTags.ColorBlack, 0, FishBehavior.Endurance, 3, 38, S);
            f.RequireSpotTags = new[] { "pier" }; f.Schooling = true;
            f = F(20, "hoshimame", "ホシマメ", "Hoshimame", "S", "小豆のような丸い青い体。脇腹の小さな星模様3つがかすかに光る。夜の池や地下湖で、弱い波状の引き。",
                "A round blue fish like a bean, with three faintly glowing star marks. Found at night in ponds and underground lakes.",
                C(0.35f, 0.5f, 0.95f), ItemTags.ColorBlue, 1, FishBehavior.Calm, 1, 42, P | U);
            f.Times = FishTime.Night | FishTime.LateNight; f.Glowing = true; f.BaitId = BaitGlowLure;
            f.MinFloor = 2; f.MaxFloor = 6; f.ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };

            // ================= 個性派魚 F021〜F035 =================
            f = F(21, "hikariuo", "ヒカリウオ", "Glowfish", "S", "濃紺の細身。体側に並ぶ光の粒が点滅し、群れで泳ぐ。点灯中に暴れ、消灯中に巻ける。",
                "A slender navy fish with blinking lights along its sides; swims in schools. Thrashes while lit, reelable while dark.",
                C(0.16f, 0.22f, 0.55f), ItemTags.ColorBlue, 1, FishBehavior.Rush, 3, 70, R | P);
            f.Times = FishTime.Evening | FishTime.Night | FishTime.LateNight; f.Schooling = true; f.Glowing = true; f.BaitId = BaitGlowLure;
            f = F(22, "kurukuru", "クルクルウオ", "Twirlfish", "M", "丸い水色の体を輪のような背びれが囲む。小さな円を描いて泳ぎ、一定の周期で逆回転する。逆回転の直前に輪びれが光る。",
                "A round light-blue fish ringed by a hoop fin. Swims in small circles and reverses on a rhythm; the fin flashes just before.",
                C(0.55f, 0.85f, 0.95f), ItemTags.ColorBlue, 1, FishBehavior.Turner, 3, 75, L);
            f.BaitId = BaitDough;
            f = F(23, "haneuo", "ハネウオ", "Hopfish", "M", "ツバメのような長い胸びれの流線形。滝の近くの急流から勢いよく飛び出し、胸びれで短く滑空する。",
                "A streamlined fish with swallow-like pectoral fins. Leaps out of the rapids below waterfalls and glides briefly.",
                C(0.3f, 0.7f, 0.72f), ItemTags.ColorGreen, 2, FishBehavior.Jumper, 4, 120, R);
            f.RequireSpotTags = new[] { "rapids", "waterfall" };
            f = F(24, "pukufugu", "プクフグ", "Puffpuff", "S", "紫色の小さなフグ。驚くと柔らかなトゲを立てて風船のように膨らみ、途中で抵抗が急に強くなる。",
                "A small purple puffer. When startled it puffs up with soft spines and suddenly resists much harder.",
                C(0.66f, 0.45f, 0.86f), ItemTags.ColorPurple, 1, FishBehavior.Puffer, 4, 90, P | S);
            f.ExtraTags = new[] { ItemTags.Cute };
            f = F(25, "ribonuo", "リボンウオ", "Ribbonfin", "M", "細長い体の左右から桃色と紫色のリボンのヒレが伸びる。開けた中層を泳ぎ続け、素早いUターンで引っ張る。",
                "A slim fish trailing pink and purple ribbon fins. Swims through open mid-water and makes sharp U-turns.",
                C(1f, 0.55f, 0.8f), ItemTags.ColorPink, 2, FishBehavior.Turner, 5, 140, L | S);
            f.RequireSpotTags = new[] { "lake", "offshore" }; f.LikeSpotTags = new[] { "mid" }; f.ExtraTags = new[] { ItemTags.Cute };
            f = F(26, "moyahaki", "モヤハキ", "Moyahaki", "M", "頭の大きい黒紫色の魚。背中の袋から黒い煙幕を吐く。濁った水にいて、煙幕で姿が見えなくなる。",
                "A big-headed black-purple fish that puffs an ink-like smoke screen from a back pouch. Lives in murky water.",
                C(0.3f, 0.18f, 0.36f), ItemTags.ColorPurple, 2, FishBehavior.Erratic, 5, 130, P | L);
            f.RequireSpotTags = new[] { "turbid" };
            f = F(27, "sakasauo", "サカサウオ", "Sakasauo", "M", "お腹を上にして泳ぐ不思議な魚。背中側に白い模様。上下反転するたびに引く向きが入れ替わる。逆雨の日は水面近くに上がってくる。",
                "A strange fish that swims belly-up, with white marks on its back. Each flip reverses its pull. Rises near the surface during reverse rain.",
                C(0.3f, 0.45f, 0.55f), ItemTags.ColorBlue, 2, FishBehavior.Turner, 4, 120, R | L);
            f.RequireSpotTags = new[] { "slack", "lake" }; f.LikeWeather = new[] { WeatherNeed.ReverseRain };
            f = F(28, "haguruma", "ハグルマウオ", "Gearfish", "M", "歯車のようなギザギザの尾びれと角ばった青緑の体。一定のリズムで3回強く引き、その後に止まる。",
                "An angular blue-green fish with a gear-toothed tail. Pulls hard three times in rhythm, then stops.",
                C(0.25f, 0.62f, 0.6f), ItemTags.ColorGreen, 2, FishBehavior.Rush, 5, 115, R | U);
            f.RequireSpotTags = new[] { "fast", "riffle", "underground" }; f.MinFloor = 2; f.MaxFloor = 8;
            f = F(29, "sokotobi", "ソコトビ", "Sokotobi", "M", "足のように見える大きな胸びれと短く太い胴。海底に潜み、水底から跳ね上がっては急に潜る。",
                "A stubby fish with leg-like pectoral fins that lurks on the sea floor, springs upward and dives back down.",
                C(0.72f, 0.45f, 0.36f), ItemTags.ColorRed, 2, FishBehavior.Jumper, 5, 125, S);
            f.RequireSpotTags = new[] { "bottom", "deep" };
            f = F(30, "hamoriuo", "ハモリウオ", "Hamoriuo", "M", "エラのまわりに2つのベル状の袋。仲間と高さの違う音を重ねて鳴く。静かな時間帯に鳴き、鳴くとベル袋が震えて水面に二重の波紋が出る。",
                "A fish with two bell-shaped sacs by its gills that harmonize with others. Sings in quiet hours; the sacs tremble and double ripples spread on the water.",
                C(0.78f, 0.7f, 0.95f), ItemTags.ColorPurple, 2, FishBehavior.Erratic, 5, 170, L | P);
            f.Times = FishTime.LateNight | FishTime.EarlyMorning | FishTime.Night;
            f.LikeSpotTags = new[] { "quiet" };
            f.BaitId = BaitChimeLure;
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Sensor, ItemTags.RocketMaterial, ItemTags.Precision, ItemTags.Music };
            f.RocketNoteJa = "ベル袋はわずかな振動にも共鳴する。観測装置の振動センサーに使えそうだ。";
            f.RocketNoteEn = "Its bell sacs resonate with the slightest vibration - could serve as a vibration sensor.";
            f = F(31, "horohoro", "ホロホロウオ", "Horohoro", "M", "半透明の体を薄いベール状のヒレが包む。ゆっくり漂うが、ヒレを広げて水流に乗ると張力がじわじわ高まり続ける。",
                "A translucent fish wrapped in veil-like fins. Drifts slowly, but when it spreads its fins the tension keeps rising.",
                C(0.92f, 0.94f, 1f), ItemTags.ColorWhite, 2, FishBehavior.Endurance, 4, 110, P);
            f = F(32, "jishaku", "ジシャクウオ", "Magnetfish", "M", "金属の粒を散りばめた青銅色の体と馬蹄形の尾。地下湖の鉱石の多い岸にいて、水底の鉱石へ引き寄せられる。",
                "A bronze fish studded with metal grains and a horseshoe tail. Lives by ore-rich underground shores and is drawn to ore on the bottom.",
                C(0.7f, 0.52f, 0.3f), ItemTags.ColorOrange, 2, FishBehavior.Diver, 5, 150, U);
            f.RequireSpotTags = new[] { "ore_shore" }; f.MinFloor = 4; f.MaxFloor = 8; f.ExtraTags = new[] { ItemTags.Conductive };
            f = F(33, "kubireuo", "クビレウオ", "Kubireuo", "M", "胴の真ん中が極端に細いひょうたん形。ゴムのようにしなり、体を縮めてから反動で急加速する。",
                "A gourd-shaped fish pinched in the middle. Bends like rubber, curls up, then shoots off.",
                C(0.86f, 0.55f, 0.32f), ItemTags.ColorOrange, 1, FishBehavior.Rush, 4, 80, R | S);
            f.RequireSpotTags = new[] { "river", "rocky" };
            f = F(34, "nejiobi", "ネジオビ", "Nejiobi", "M", "太い円筒形の胴にネジ山のような螺旋の縞。体をねじって砂や泥に潜り込み、潜られると糸が底で擦れて切れやすくなる。",
                "A thick cylindrical fish with screw-thread stripes. Twists itself into sand or mud; the line frays on the bottom if it gets in.",
                C(0.9f, 0.8f, 0.35f), ItemTags.ColorYellow, 2, FishBehavior.Diver, 5, 125, L | S);
            f.RequireSpotTags = new[] { "sandmud" };
            f = F(35, "kasabire", "カサビレ", "Kasabire", "M", "淡い青色の体。背中に折りたたみ傘のような大きなヒレ。ヒレを急に広げてブレーキをかけ、長く粘る。",
                "A pale blue fish with an umbrella-like dorsal fin. Snaps it open to brake and then holds out for a long time.",
                C(0.66f, 0.8f, 0.95f), ItemTags.ColorBlue, 1, FishBehavior.Endurance, 4, 85, L | R);
            f.RequireSpotTags = new[] { "lake", "pool" };

            // ================= 希少魚 F036〜F045 =================
            f = F(36, "crystal", "クリスタルフィッシュ", "Crystalfish", "M", "透明な水晶でできたような魚。角ばった面で光を屈折させ、見える位置と本当の位置がずれる。鉱山の深い階の地下湖にいる。",
                "A fish that seems carved from clear crystal. Its facets bend light so it is never quite where it appears. Lives in deep underground lakes.",
                C(0.8f, 0.92f, 1f), ItemTags.ColorWhite, 3, FishBehavior.Erratic, 6, 320, U);
            f.MinFloor = 8; f.MaxFloor = 10; f.Glowing = true; f.ExtraTags = new[] { ItemTags.Shiny, ItemTags.Crystal };
            f = F(37, "ameshizuku", "アメシズク", "Ameshizuku", "S", "透明な青色の体に水滴のような粒が並ぶ。雨上がりに現れ、水面を水切り石のように連続で弾む。光雨の後はさらに出やすい。",
                "A clear blue fish dotted like raindrops. Appears after rain and skips across the surface like a thrown stone. Even more common after light-rain.",
                C(0.55f, 0.78f, 1f), ItemTags.ColorBlue, 3, FishBehavior.Jumper, 5, 260, R | P);
            f.RequireWeather = WeatherNeed.AfterRain; f.LikeWeather = new[] { WeatherNeed.LightRain };
            f.ExtraTags = new[] { ItemTags.Small, ItemTags.Cute };
            f = F(38, "kazeuke", "カゼウケ", "Kazeuke", "L", "白と薄緑の体に帆のような二枚の背びれ。強風の日に海に現れ、風や潮に帆を向けて引く方向を大きく変える。星風の日はさらに出やすい。",
                "A white-and-mint fish with two sail-like dorsal fins. Appears at sea on windy days and swings its pull with the wind. Even more common on star-wind days.",
                C(0.82f, 0.95f, 0.86f), ItemTags.ColorGreen, 3, FishBehavior.Turner, 6, 340, S);
            f.RequireWeather = WeatherNeed.StrongWind; f.LikeWeather = new[] { WeatherNeed.StarWind };
            f.LikeSpotTags = new[] { "offshore", "rocky", "pier" }; f.ExtraTags = new[] { ItemTags.Big };
            f = F(39, "shirubefude", "シルベフデ", "Shirubefude", "M", "筆先のような細長い尾で、夜の水中に光る軌跡を描く。軌跡が次の進路のヒントになる。滝の裏の暗い淵や夜の湖にいる。",
                "A fish with a brush-tip tail that draws glowing trails in night water - the trail hints at its next turn. Lives in the dark pool behind the waterfall and in the lake at night.",
                C(0.4f, 0.85f, 0.95f), ItemTags.ColorBlue, 3, FishBehavior.Turner, 6, 360, R | L);
            f.RequireSpotTags = new[] { "behind_waterfall", "lake" }; f.LikeSpotTags = new[] { "behind_waterfall", "dark" };
            f.Times = FishTime.Night | FishTime.LateNight; f.Glowing = true; f.BaitId = BaitGlowLure;
            f = F(40, "kirigakure", "キリガクレ", "Kirigakure", "M", "真珠色の鱗が周りの水色に溶け込む。霧の日の湖にだけ現れ、引きもほとんど感じない。",
                "Pearl scales that melt into the water's color. Appears only on the lake in fog, and its pull is barely felt.",
                C(0.94f, 0.94f, 0.9f), ItemTags.ColorWhite, 3, FishBehavior.Erratic, 6, 330, L);
            f.RequireWeather = WeatherNeed.Fog; f.LikeSpotTags = new[] { "foggy" };
            f = F(41, "iwanaki", "イワナキ", "Iwanaki", "L", "岩のような厚い鱗と波打つ石板状の背びれ。餌に近づくと低い音を出し、水面と砂粒が細かく震える。食いつくと重く長く引く。地下湖にいる。",
                "A rock-scaled fish with a wavy slate dorsal fin. Rumbles low as it nears the bait, making the water and sand shiver. Pulls heavy and long. Lives in underground lakes.",
                C(0.5f, 0.48f, 0.46f), ItemTags.ColorBlack, 3, FishBehavior.Endurance, 7, 380, U);
            f.MinFloor = 4; f.MaxFloor = 10; f.BaitId = BaitChimeLure;
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Sensor, ItemTags.RocketMaterial, ItemTags.Precision, ItemTags.Big };
            f.RocketNoteJa = "低い音を感じ取る器官がある。観測装置の振動センサーに使えそうだ。";
            f.RocketNoteEn = "It has an organ that senses low sounds - could serve as a vibration sensor.";
            f = F(42, "yorutsubasa", "ヨルツバサ", "Yorutsubasa", "L", "黒い体から巨大な翼のような胸びれが広がる。夜の沖や桟橋の先で、大きく弧を描いて滑空し、糸がたるむと一気に深く潜る。",
                "A black fish with huge wing-like fins. At night off the pier it glides in wide arcs, then dives deep the moment the line slackens.",
                C(0.12f, 0.12f, 0.2f), ItemTags.ColorBlack, 3, FishBehavior.Diver, 7, 400, S);
            f.RequireSpotTags = new[] { "offshore", "pier" }; f.Times = FishTime.Night | FishTime.LateNight;
            f.BaitId = BaitGlowLure; f.ExtraTags = new[] { ItemTags.Big };
            f = F(43, "otonashi", "オトナシ", "Otonashi", "M", "透き通った大きな耳のようなエラ。人の来ない静かな岸にいる敏感な魚。急に巻くと警戒してエラを白く広げる。ゆっくり巻くのが有効。",
                "A sensitive fish with large clear ear-like gills, found only on quiet shores. Reel fast and it flares its gills white in alarm - go slow.",
                C(0.86f, 0.92f, 0.96f), ItemTags.ColorWhite, 3, FishBehavior.Calm, 5, 300, P | L);
            f.RequireSpotTags = new[] { "quiet" }; f.BaitId = BaitChimeLure;
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Sensor, ItemTags.RocketMaterial, ItemTags.Precision };
            f.RocketNoteJa = "耳のようなエラは、わずかな揺れも聞き分ける。観測装置の振動センサーに使えそうだ。";
            f.RocketNoteEn = "Its ear-like gills pick up the faintest wobble - could serve as a vibration sensor.";
            f = F(44, "hiyashiuo", "ヒヤシウオ", "Hiyashiuo", "M", "水色の体を冷たい半透明の膜がマントのように覆う。涼しい深場を好み、浅場に近づくほど活発になる。",
                "A light-blue fish cloaked in a cold translucent membrane. Prefers cool depths and gets livelier the closer it comes to warm shallows.",
                C(0.6f, 0.85f, 1f), ItemTags.ColorBlue, 3, FishBehavior.Erratic, 6, 350, L | U);
            f.RequireSpotTags = new[] { "deep", "deepest" }; f.MinFloor = 6; f.MaxFloor = 10; f.BaitId = BaitDeepSinker;
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Cooling, ItemTags.RocketMaterial, ItemTags.Rare };
            f.RocketNoteJa = "マントのような冷たい膜は、シモマダラの膜より上質だ。冷却膜（高品質）になりそうだ。";
            f.RocketNoteEn = "Its cold cloak membrane is finer than the Shimomadara's - could make a high-quality cooling membrane.";
            f = F(45, "sazanamime", "サザナミメ", "Sazanamime", "M", "体の左右に細かな感覚器が一直線に並ぶ。川の早瀬や海で水の振動を感じ取り、向きを変える直前に竿先へ細かな振動が伝わる。",
                "A fish with a fine line of sense organs along each side. Feels the water's tremors in fast river runs and at sea; the rod tip quivers just before it turns.",
                C(0.66f, 0.82f, 0.74f), ItemTags.ColorGreen, 3, FishBehavior.Turner, 6, 300, R | S);
            f.RequireSpotTags = new[] { "fast", "sea" };
            f.IsRocketMaterial = true;
            f.ExtraTags = new[] { ItemTags.Sensor, ItemTags.RocketMaterial, ItemTags.Precision };
            f.RocketNoteJa = "並んだ感覚器が水の揺らぎを読み取っている。観測装置の振動センサーに使えそうだ。";
            f.RocketNoteEn = "Its row of sense organs reads every ripple - could serve as a vibration sensor.";

            // ================= 伝説魚 F046〜F050（固有の行動。釣られると星全体でしばらく休む）=================
            // 固有の行動は今の釣りミニゲームにある5つの伝説の動きのうち、原本の説明に一番近いものを使う【提案】。
            f = F(46, "kagaminushi", "カガミヌシ", "Kagaminushi", "XL", "鏡のように周りを映す銀色の巨大な円盤型の魚。風のない日、湖の最深部の静かな水面に現れる。水面の虚像が本体と逆へ動いて惑わせる。",
                "A giant silver disc of a fish that mirrors its surroundings. Appears over the lake's deepest water on windless days; its reflection moves opposite to its body.",
                C(0.86f, 0.88f, 0.92f), ItemTags.ColorWhite, 5, FishBehavior.LegendPhantom, 9, 1800, L);
            f.RequireSpotTags = new[] { "deepest" }; f.RequireWeather = WeatherNeed.Calm; f.Glowing = true;
            f = F(47, "raimeiuo", "ライメイウオ", "Raimeiuo", "XL", "紺色の大きな体に雷のように折れ曲がった黄色い背びれと三又の尾。強風と強い雨の荒天に現れ、非常に速いジグザグで暴れる。背びれが光った直後が危ない。",
                "A huge navy fish with a lightning-bolt yellow dorsal fin and a trident tail. Appears in storms of strong wind and heavy rain and zigzags wildly; trouble follows when the fin flashes.",
                C(0.15f, 0.2f, 0.5f), ItemTags.ColorBlue, 5, FishBehavior.LegendLord, 9, 1700, S | L);
            f.RequireWeather = WeatherNeed.Storm; f.ExtraTags = new[] { ItemTags.Conductive };
            f = F(48, "uzushiro", "ウズシロ", "Uzushiro", "XL", "長い体に白い螺旋模様の巨大魚。体を丸めて渦を作り、仕掛けを渦の中心へ引き込む。途中で渦の向きが逆転する。湖や海の深場にいる。",
                "A long giant fish with white spiral markings. Curls up to make a whirlpool that drags the rig to its center, then reverses it. Lives in deep lake and sea water.",
                C(0.92f, 0.94f, 0.98f), ItemTags.ColorWhite, 5, FishBehavior.LegendTide, 9, 1900, L | S);
            f.RequireSpotTags = new[] { "deepest", "deep" };
            f = F(49, "ganteiou", "ガンテイオウ", "Ganteiou", "XL", "岩盤のような分厚い装甲鱗と巨大な尾の古代魚。鉱山の最深部の地下湖にいて、ほとんど動かない時間と非常に強い潜行を繰り返す。",
                "An ancient fish with bedrock armor scales and a huge tail, in the deepest underground lake. Alternates long stillness with immensely strong dives.",
                C(0.42f, 0.36f, 0.32f), ItemTags.ColorBlack, 5, FishBehavior.LegendCrystal, 10, 2200, U);
            f.RequireSpotTags = new[] { "deepest" }; f.MinFloor = 10; f.MaxFloor = 10;
            f = F(50, "sorawatari", "ソラワタリ", "Sorawatari", "XL", "夜空のような青い体に四枚の大きな三日月形のヒレ。晴れた夜、湖や海の広い水面に現れ、水面の滑走・深い潜行・大きな方向転換を段階的に繰り出す。星の光帯が出る夜はさらに出やすい。",
                "A night-sky blue fish with four crescent fins. On clear nights it appears over wide lake or sea water, gliding, diving deep and turning hard in stages. Even more likely under the star band.",
                C(0.2f, 0.28f, 0.7f), ItemTags.ColorBlue, 5, FishBehavior.LegendSkylark, 9, 2000, L | S);
            f.RequireSpotTags = new[] { "wide", "offshore" }; f.RequireWeather = WeatherNeed.ClearNight;
            f.LikeWeather = new[] { WeatherNeed.StarBand }; f.Times = FishTime.Night | FishTime.LateNight; f.Glowing = true;
        }

        // ---------------- 旧案（41種）の古いセーブの読み替え ----------------

        /// <summary>旧案の魚ID → 魚50種の一番近い魚（名前が同じもの・修理用の役割・水域で対応させた）。</summary>
        public static readonly Dictionary<string, string> LegacyIds = new Dictionary<string, string>
        {
            { "fish.pipi_minnow", IdOf(1, "marukoro") }, { "fish.kurukuru", IdOf(22, "kurukuru") }, { "fish.hane", IdOf(23, "haneuo") },
            { "fish.hikari", IdOf(21, "hikariuo") }, { "fish.ribbon", IdOf(25, "ribonuo") }, { "fish.mossback", IdOf(31, "horohoro") },
            { "fish.pebble_loach", IdOf(9, "nagakuchi") }, { "fish.sun_carp", IdOf(12, "kobuhire") }, { "fish.mist_trout", IdOf(11, "yamagiza") },
            { "fish.bubble_perch", IdOf(5, "funebire") }, { "fish.mud_pout", IdOf(4, "higetama") }, { "fish.snore_catfish", IdOf(35, "kasabire") },
            { "fish.cloud_puff", IdOf(10, "yurashippo") }, { "fish.bloom_koi", IdOf(7, "peraobi") }, { "fish.lightrain_tetra", IdOf(37, "ameshizuku") },
            { "fish.twin_tail", IdOf(33, "kubireuo") }, { "fish.dawn_salmon", IdOf(15, "nijisuji") }, { "fish.tiny_star_guppy", IdOf(6, "kometsubu") },
            { "fish.thunder_eel", IdOf(28, "haguruma") }, { "fish.haze_glass", IdOf(40, "kirigakure") }, { "fish.star_sardine", IdOf(14, "tsunosaki") },
            { "fish.wave_bream", IdOf(17, "sangohoho") }, { "fish.moon_flounder", IdOf(18, "hiraboushi") }, { "fish.windsail", IdOf(38, "kazeuke") },
            { "fish.jelly_float", IdOf(24, "pukufugu") }, { "fish.puku_fugu", IdOf(24, "pukufugu") }, { "fish.spiral_shell_fish", IdOf(29, "sokotobi") },
            { "fish.starwind_flyer", IdOf(42, "yorutsubasa") }, { "fish.orbit_ray", IdOf(42, "yorutsubasa") }, { "fish.echo_goby", IdOf(9, "nagakuchi") },
            { "fish.cave_blind", IdOf(20, "hoshimame") }, { "fish.crystal", IdOf(36, "crystal") }, { "fish.lantern_angler", IdOf(41, "iwanaki") },
            { "fish.gem_scale", IdOf(36, "crystal") }, { SharedItemIds.LegacyFishFrostFin, SharedItemIds.FishShimomadara },
            { SharedItemIds.LegacyFishRippleEye, SharedItemIds.FishSazanamime },
            { "fish.legend_river_lord", IdOf(47, "raimeiuo") }, { "fish.legend_star_whale", IdOf(50, "sorawatari") },
            { "fish.legend_yuragi", IdOf(46, "kagaminushi") }, { "fish.legend_crystal_dragon", IdOf(49, "ganteiou") },
            { "fish.legend_lightrain_nymph", IdOf(48, "uzushiro") }
        };

        /// <summary>修理用の冷却膜の候補魚か（タグ Cooling）。</summary>
        public static bool IsCoolingFish(FishSpecies s) => s != null && s.IsRocketMaterial && System.Array.IndexOf(s.ExtraTags, ItemTags.Cooling) >= 0;
        /// <summary>修理用の振動センサーの候補魚か（タグ Sensor）。</summary>
        public static bool IsSensorFish(FishSpecies s) => s != null && s.IsRocketMaterial && System.Array.IndexOf(s.ExtraTags, ItemTags.Sensor) >= 0;

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
                if (s.SizeMax <= 8f) def.WithTags(ItemTags.Small);
                ItemRegistry.Register(def);
            }
            foreach (var kv in LegacyIds) ItemRegistry.RegisterAlias(kv.Key, kv.Value);

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
