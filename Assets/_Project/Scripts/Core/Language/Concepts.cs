using System.Collections.Generic;

namespace Uchujin.Core
{
    /// <summary>
    /// 宇宙語で表現できる基本概念のID（宇宙語仕様 §4.1 の生活語彙）。
    /// どの星にもこれらの概念があり、語の発音・綴り・語順・文法は星のシードで変わる。
    /// アイテム・住民・施設などの固有概念は "ITEM:" "NAME:" "PLACE:" "SPECIES:" の接頭辞で動的に扱う。
    /// Language モジュールは未登録の概念IDでも決定的に語を生成できること。
    /// 多義語は区別する（例：PRICE_EXPENSIVE と TALL は別概念）。
    /// </summary>
    public static class Concepts
    {
        private static readonly Dictionary<string, ConceptKind> kinds = new Dictionary<string, ConceptKind>();

        public const string ItemPrefix = "ITEM:";
        public const string NamePrefix = "NAME:";
        public const string PlacePrefix = "PLACE:";
        public const string SpeciesPrefix = "SPECIES:";

        public static string Item(string itemId) => ItemPrefix + itemId;
        public static string Name(string entityId) => NamePrefix + entityId;
        public static string Place(string facilityId) => PlacePrefix + facilityId;
        public static string Species(string speciesId) => SpeciesPrefix + speciesId;

        // ---- 代名詞・人 ----
        public const string I = "I";
        public const string YOU = "YOU";
        public const string HE_SHE = "HE_SHE";
        public const string WE = "WE";
        public const string THEY = "THEY";
        public const string THIS = "THIS";
        public const string THAT = "THAT";
        public const string EVERYONE = "EVERYONE";
        public const string SOMEONE = "SOMEONE";
        public const string FRIEND = "FRIEND";
        public const string FAMILY = "FAMILY";
        public const string CHILD = "CHILD";
        public const string PERSON = "PERSON";
        public const string NEIGHBOR = "NEIGHBOR";
        public const string GUEST = "GUEST";            // 客・訪問者
        public const string STRANGE_ONE = "STRANGE_ONE"; // 変な生き物（来たばかりの主人公の呼び方）
        public const string CUSTOMER = "CUSTOMER";

        // ---- 動詞 ----
        public const string GO = "GO";
        public const string COME = "COME";
        public const string GIVE = "GIVE";
        public const string RECEIVE = "RECEIVE";
        public const string BUY = "BUY";
        public const string SELL = "SELL";
        public const string EAT = "EAT";
        public const string DRINK = "DRINK";
        public const string CATCH_FISH = "CATCH_FISH";
        public const string CATCH = "CATCH";
        public const string GROW = "GROW";
        public const string PLANT = "PLANT";
        public const string HARVEST = "HARVEST";
        public const string WATER_VERB = "WATER_VERB";
        public const string DIG = "DIG";
        public const string MAKE = "MAKE";
        public const string COOK = "COOK";
        public const string REPAIR = "REPAIR";
        public const string BREAK = "BREAK";
        public const string SEE = "SEE";
        public const string LIKE = "LIKE";
        public const string DISLIKE = "DISLIKE";
        public const string WANT = "WANT";        // 欲しい（patient を欲する）
        public const string NEED = "NEED";
        public const string HAVE = "HAVE";
        public const string EXIST = "EXIST";      // ある・いる
        public const string HELP = "HELP";
        public const string WORK = "WORK";
        public const string REST = "REST";
        public const string SLEEP = "SLEEP";
        public const string PLAY = "PLAY";
        public const string MEET = "MEET";
        public const string TALK = "TALK";
        public const string KNOW = "KNOW";
        public const string THINK = "THINK";
        public const string FIND = "FIND";
        public const string LOSE = "LOSE";
        public const string LIVE = "LIVE";
        public const string MOVE_HOUSE = "MOVE_HOUSE";
        public const string LEAVE = "LEAVE";
        public const string RETURN = "RETURN";
        public const string WAIT = "WAIT";
        public const string LOVE = "LOVE";
        public const string RAIN_VERB = "RAIN_VERB";
        public const string BLOW = "BLOW";
        public const string SHINE = "SHINE";
        public const string FLY = "FLY";
        public const string WALK = "WALK";
        public const string RUN = "RUN";
        public const string SING = "SING";
        public const string CARRY = "CARRY";
        public const string BRING = "BRING";
        public const string USE = "USE";
        public const string PUT = "PUT";
        public const string OPEN = "OPEN";
        public const string CLOSE = "CLOSE";
        public const string START = "START";
        public const string FINISH = "FINISH";
        public const string TEACH = "TEACH";
        public const string LEARN = "LEARN";
        public const string CRY = "CRY";
        public const string LAUGH = "LAUGH";
        public const string CELEBRATE = "CELEBRATE";
        public const string VISIT = "VISIT";
        public const string CHANGE = "CHANGE";
        public const string REMEMBER = "REMEMBER";
        public const string FORGET = "FORGET";
        public const string DREAM = "DREAM";
        public const string LOOK_FOR = "LOOK_FOR";
        public const string FLOAT = "FLOAT";
        public const string FALL = "FALL";
        public const string WRITE = "WRITE";
        public const string READ = "READ";
        public const string DECORATE = "DECORATE";
        public const string FIGHT = "FIGHT";     // 喧嘩する
        public const string MAKE_UP = "MAKE_UP"; // 仲直りする
        public const string COMPETE = "COMPETE"; // 競う
        public const string DRAW = "DRAW";       // 絵を描く

        // ---- 形容詞（評価・状態） ----
        public const string BIG = "BIG";
        public const string SMALL = "SMALL";
        public const string PRICE_EXPENSIVE = "PRICE_EXPENSIVE";
        public const string PRICE_CHEAP = "PRICE_CHEAP";
        public const string TALL = "TALL";
        public const string DELICIOUS = "DELICIOUS";
        public const string BAD_TASTE = "BAD_TASTE";
        public const string GOOD = "GOOD";
        public const string BAD = "BAD";
        public const string HAPPY = "HAPPY";
        public const string SAD = "SAD";
        public const string SCARY = "SCARY";
        public const string BEAUTIFUL = "BEAUTIFUL";
        public const string CUTE = "CUTE";
        public const string HARD = "HARD";
        public const string SOFT = "SOFT";
        public const string RARE = "RARE";
        public const string BROKEN = "BROKEN";
        public const string NEW = "NEW";
        public const string OLD = "OLD";
        public const string HOT = "HOT";
        public const string COLD = "COLD";
        public const string FAST = "FAST";
        public const string SLOW = "SLOW";
        public const string STRONG = "STRONG";
        public const string WEAK = "WEAK";
        public const string HEAVY = "HEAVY";
        public const string BRIGHT = "BRIGHT";
        public const string DARK = "DARK";
        public const string QUIET = "QUIET";
        public const string LOUD = "LOUD";
        public const string TIRED = "TIRED";
        public const string HUNGRY = "HUNGRY";
        public const string FULL = "FULL";
        public const string BUSY = "BUSY";
        public const string FREE_TIME = "FREE_TIME";
        public const string WET = "WET";
        public const string DRY = "DRY";
        public const string NEAR = "NEAR";
        public const string FAR = "FAR";
        public const string DIFFICULT = "DIFFICULT";
        public const string EASY = "EASY";
        public const string FUN = "FUN";
        public const string BORING = "BORING";
        public const string KIND = "KIND";
        public const string STRANGE = "STRANGE";
        public const string IMPORTANT = "IMPORTANT";
        public const string LONELY = "LONELY";
        public const string GLOWING = "GLOWING";
        public const string MANY = "MANY";
        public const string FEW = "FEW";
        public const string SAME = "SAME";
        public const string DIFFERENT = "DIFFERENT";
        public const string ANGRY = "ANGRY";
        public const string SURPRISED = "SURPRISED";
        public const string FRESH = "FRESH";

        // ---- 色 ----
        public const string RED = "RED";
        public const string BLUE = "BLUE";
        public const string GREEN = "GREEN";
        public const string YELLOW = "YELLOW";
        public const string WHITE = "WHITE";
        public const string BLACK = "BLACK";
        public const string PURPLE = "PURPLE";
        public const string ORANGE = "ORANGE";
        public const string PINK = "PINK";

        // ---- 副詞・程度 ----
        public const string VERY = "VERY";
        public const string A_LITTLE = "A_LITTLE";
        public const string AGAIN = "AGAIN";
        public const string TOGETHER = "TOGETHER";
        public const string ALSO = "ALSO";
        public const string ONLY = "ONLY";
        public const string STILL = "STILL";
        public const string ALREADY = "ALREADY";
        public const string MAYBE = "MAYBE";

        // ---- 時 ----
        public const string TODAY = "TODAY";
        public const string TOMORROW = "TOMORROW";
        public const string YESTERDAY = "YESTERDAY";
        public const string NOW = "NOW";
        public const string MORNING = "MORNING";
        public const string NOON = "NOON";
        public const string EVENING = "EVENING";
        public const string NIGHT = "NIGHT";
        public const string ALWAYS = "ALWAYS";
        public const string SOMETIMES = "SOMETIMES";
        public const string LATER = "LATER";
        public const string SOON = "SOON";
        public const string EVERY_DAY = "EVERY_DAY";
        public const string LONG_AGO = "LONG_AGO";
        public const string SOMEDAY = "SOMEDAY";
        public const string REST_DAY = "REST_DAY";

        // ---- 場所 ----
        public const string HOUSE = "HOUSE";
        public const string SHOP = "SHOP";
        public const string PLAZA = "PLAZA";
        public const string RIVER = "RIVER";
        public const string SEA = "SEA";
        public const string POND = "POND";
        public const string FOREST = "FOREST";
        public const string MOUNTAIN = "MOUNTAIN";
        public const string MINE = "MINE";
        public const string FARM = "FARM";
        public const string MEADOW = "MEADOW";
        public const string SKY = "SKY";
        public const string VILLAGE = "VILLAGE";
        public const string ROAD = "ROAD";
        public const string BRIDGE = "BRIDGE";
        public const string HOME_PLANET = "HOME_PLANET"; // 故郷
        public const string HILL = "HILL";
        public const string HERE = "HERE";
        public const string THERE = "THERE";
        public const string INSIDE = "INSIDE";
        public const string OUTSIDE = "OUTSIDE";
        public const string UP = "UP";
        public const string DOWN = "DOWN";

        // ---- 物・自然 ----
        public const string WATER = "WATER";
        public const string FIRE = "FIRE";
        public const string FISH = "FISH";
        public const string CROP = "CROP";
        public const string FRUIT = "FRUIT";
        public const string VEGETABLE = "VEGETABLE";
        public const string FLOWER = "FLOWER";
        public const string TREE = "TREE";
        public const string SEED = "SEED";
        public const string ORE = "ORE";
        public const string STONE = "STONE";
        public const string GEM = "GEM";
        public const string METAL = "METAL";
        public const string CREATURE = "CREATURE";
        public const string FOOD = "FOOD";
        public const string DISH = "DISH";
        public const string DRINK_N = "DRINK_N";
        public const string FURNITURE = "FURNITURE";
        public const string CHAIR = "CHAIR";
        public const string TABLE = "TABLE";
        public const string BED = "BED";
        public const string LAMP = "LAMP";
        public const string BOX = "BOX";
        public const string TOOL = "TOOL";
        public const string PICKAXE = "PICKAXE";
        public const string FISHING_ROD = "FISHING_ROD";
        public const string NET = "NET";
        public const string MONEY = "MONEY";
        public const string GIFT = "GIFT";
        public const string LETTER = "LETTER";
        public const string MESSAGE = "MESSAGE";
        public const string PART = "PART";
        public const string FUEL = "FUEL";
        public const string ROCKET = "ROCKET";
        public const string STAR = "STAR";
        public const string PLANET = "PLANET";
        public const string SUN = "SUN";
        public const string MOON = "MOON";
        public const string MUSIC = "MUSIC";
        public const string PICTURE = "PICTURE";
        public const string CLOTHES = "CLOTHES";
        public const string DREAM_N = "DREAM_N";
        public const string JOB = "JOB";
        public const string PARTY = "PARTY";
        public const string BIRTHDAY = "BIRTHDAY";
        public const string NAME = "NAME";
        public const string WORD = "WORD";
        public const string THING = "THING";

        // ---- 天気 ----
        public const string RAIN = "RAIN";
        public const string SUNNY = "SUNNY";
        public const string CLOUD = "CLOUD";
        public const string WIND = "WIND";
        public const string FOG = "FOG";
        public const string WEATHER = "WEATHER";
        public const string LIGHT_RAIN = "LIGHT_RAIN";     // 光雨
        public const string STAR_WIND = "STAR_WIND";       // 星風
        public const string REVERSE_RAIN = "REVERSE_RAIN"; // 逆雨
        public const string STAR_BAND = "STAR_BAND";       // 星の光帯

        // ---- 数量 ----
        public const string ALL = "ALL";
        public const string SOME = "SOME";
        public const string NOTHING = "NOTHING";
        public const string MORE = "MORE";

        // ---- 間投詞 ----
        public const string HELLO = "HELLO";
        public const string GOODBYE = "GOODBYE";
        public const string THANKS = "THANKS";
        public const string SORRY = "SORRY";
        public const string YES = "YES";
        public const string NO = "NO";
        public const string WOW = "WOW";
        public const string WELCOME = "WELCOME";
        public const string CONGRATS = "CONGRATS";
        public const string GOOD_NIGHT = "GOOD_NIGHT";
        public const string OK = "OK";
        public const string HMM = "HMM";

        // ---- 疑問詞 ----
        public const string WHAT = "WHAT";
        public const string WHO = "WHO";
        public const string WHERE = "WHERE";
        public const string WHEN = "WHEN";
        public const string WHY = "WHY";
        public const string HOW_MANY = "HOW_MANY";
        public const string WHICH = "WHICH";

        static Concepts()
        {
            Reg(ConceptKind.Pronoun, I, YOU, HE_SHE, WE, THEY, THIS, THAT, EVERYONE, SOMEONE);
            Reg(ConceptKind.Noun, FRIEND, FAMILY, CHILD, PERSON, NEIGHBOR, GUEST, STRANGE_ONE, CUSTOMER);
            Reg(ConceptKind.Verb, GO, COME, GIVE, RECEIVE, BUY, SELL, EAT, DRINK, CATCH_FISH, CATCH, GROW, PLANT, HARVEST,
                WATER_VERB, DIG, MAKE, COOK, REPAIR, BREAK, SEE, LIKE, DISLIKE, WANT, NEED, HAVE, EXIST, HELP, WORK, REST,
                SLEEP, PLAY, MEET, TALK, KNOW, THINK, FIND, LOSE, LIVE, MOVE_HOUSE, LEAVE, RETURN, WAIT, LOVE, RAIN_VERB,
                BLOW, SHINE, FLY, WALK, RUN, SING, CARRY, BRING, USE, PUT, OPEN, CLOSE, START, FINISH, TEACH, LEARN, CRY,
                LAUGH, CELEBRATE, VISIT, CHANGE, REMEMBER, FORGET, DREAM, LOOK_FOR, FLOAT, FALL, WRITE, READ, DECORATE,
                FIGHT, MAKE_UP, COMPETE, DRAW);
            Reg(ConceptKind.Adjective, BIG, SMALL, PRICE_EXPENSIVE, PRICE_CHEAP, TALL, DELICIOUS, BAD_TASTE, GOOD, BAD, HAPPY,
                SAD, SCARY, BEAUTIFUL, CUTE, HARD, SOFT, RARE, BROKEN, NEW, OLD, HOT, COLD, FAST, SLOW, STRONG, WEAK, HEAVY,
                BRIGHT, DARK, QUIET, LOUD, TIRED, HUNGRY, FULL, BUSY, FREE_TIME, WET, DRY, NEAR, FAR, DIFFICULT, EASY, FUN,
                BORING, KIND, STRANGE, IMPORTANT, LONELY, GLOWING, MANY, FEW, SAME, DIFFERENT, ANGRY, SURPRISED, FRESH);
            Reg(ConceptKind.Color, RED, BLUE, GREEN, YELLOW, WHITE, BLACK, PURPLE, ORANGE, PINK);
            Reg(ConceptKind.Adverb, VERY, A_LITTLE, AGAIN, TOGETHER, ALSO, ONLY, STILL, ALREADY, MAYBE);
            Reg(ConceptKind.TimeWord, TODAY, TOMORROW, YESTERDAY, NOW, MORNING, NOON, EVENING, NIGHT, ALWAYS, SOMETIMES,
                LATER, SOON, EVERY_DAY, LONG_AGO, SOMEDAY, REST_DAY);
            Reg(ConceptKind.Place, HOUSE, SHOP, PLAZA, RIVER, SEA, POND, FOREST, MOUNTAIN, MINE, FARM, MEADOW, SKY, VILLAGE,
                ROAD, BRIDGE, HOME_PLANET, HILL, HERE, THERE, INSIDE, OUTSIDE, UP, DOWN);
            Reg(ConceptKind.Noun, WATER, FIRE, FISH, CROP, FRUIT, VEGETABLE, FLOWER, TREE, SEED, ORE, STONE, GEM, METAL,
                CREATURE, FOOD, DISH, DRINK_N, FURNITURE, CHAIR, TABLE, BED, LAMP, BOX, TOOL, PICKAXE, FISHING_ROD, NET,
                MONEY, GIFT, LETTER, MESSAGE, PART, FUEL, ROCKET, STAR, PLANET, SUN, MOON, MUSIC, PICTURE, CLOTHES, DREAM_N,
                JOB, PARTY, BIRTHDAY, NAME, WORD, THING, RAIN, SUNNY, CLOUD, WIND, FOG, WEATHER, LIGHT_RAIN, STAR_WIND,
                REVERSE_RAIN, STAR_BAND, ALL, SOME, NOTHING, MORE);
            Reg(ConceptKind.Interjection, HELLO, GOODBYE, THANKS, SORRY, YES, NO, WOW, WELCOME, CONGRATS, GOOD_NIGHT, OK, HMM);
            Reg(ConceptKind.QuestionWord, WHAT, WHO, WHERE, WHEN, WHY, HOW_MANY, WHICH);
        }

        private static void Reg(ConceptKind kind, params string[] ids)
        {
            foreach (var id in ids) kinds[id] = kind;
        }

        /// <summary>モジュールが独自概念を追加する。</summary>
        public static void Register(string id, ConceptKind kind) => kinds[id] = kind;

        public static ConceptKind KindOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return ConceptKind.Noun;
            if (kinds.TryGetValue(id, out var k)) return k;
            if (id.StartsWith(NamePrefix)) return ConceptKind.Name;
            if (id.StartsWith(PlacePrefix)) return ConceptKind.Place;
            return ConceptKind.Noun;
        }

        public static IEnumerable<string> AllRegistered => kinds.Keys;

        /// <summary>アイテムタグから色概念を推定（住民の発話で「青い魚」などに使う）。</summary>
        public static string ColorFromTags(ItemDefinition def)
        {
            if (def == null) return null;
            if (def.HasTag(ItemTags.ColorRed)) return RED;
            if (def.HasTag(ItemTags.ColorBlue)) return BLUE;
            if (def.HasTag(ItemTags.ColorGreen)) return GREEN;
            if (def.HasTag(ItemTags.ColorYellow)) return YELLOW;
            if (def.HasTag(ItemTags.ColorWhite)) return WHITE;
            if (def.HasTag(ItemTags.ColorBlack)) return BLACK;
            if (def.HasTag(ItemTags.ColorPurple)) return PURPLE;
            if (def.HasTag(ItemTags.ColorOrange)) return ORANGE;
            if (def.HasTag(ItemTags.ColorPink)) return PINK;
            return null;
        }

        /// <summary>アイテムの総称概念（住民が「魚」「作物」と総称する場合）。</summary>
        public static string GenericOf(ItemDefinition def)
        {
            if (def == null) return THING;
            if (!string.IsNullOrEmpty(def.GenericConceptId)) return def.GenericConceptId;
            switch (def.Category)
            {
                case ItemCategory.Fish: return FISH;
                case ItemCategory.Crop: return CROP;
                case ItemCategory.Seed: return SEED;
                case ItemCategory.Ore: return ORE;
                case ItemCategory.Gem: return GEM;
                case ItemCategory.Material: return METAL;
                case ItemCategory.Food: return DISH;
                case ItemCategory.Processed: return FOOD;
                case ItemCategory.Furniture: return FURNITURE;
                case ItemCategory.Tool: return TOOL;
                case ItemCategory.RocketPart: return PART;
                case ItemCategory.Letter: return LETTER;
                case ItemCategory.Gift: return GIFT;
                default: return THING;
            }
        }
    }
}
