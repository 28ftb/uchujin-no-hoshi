using System;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;
using Uchujin.World;

namespace Uchujin.Creatures
{
    // =====================================================================
    // 宇宙生物の種族データの型（生き物仕様 §2〜§5）。
    // 名前・数値はすべて【仮】。CreatureDatabase に50種のテーブルがある。
    // =====================================================================

    /// <summary>仕様 §2 の5分類（企画整理用。ゲーム内で厳密に固定はしない）。</summary>
    public enum CreatureGroup
    {
        Farm,         // 001〜010 農業お手伝い生物
        ForestMeadow, // 011〜020 森・草原
        Waterside,    // 021〜030 水辺・海岸
        Village,      // 031〜040 村・住宅地
        Special       // 041〜050 地下・特殊条件
    }

    /// <summary>捕獲のタイプ（生き物仕様 §5.2）。</summary>
    public enum CatchStyle
    {
        Normal,   // 通常捕獲：網で捕まえる（走って近づくと逃げる）
        Sneak,    // 忍び寄り：走ると遠くからでも気づいて逃げる。歩いて近づく
        Timing,   // タイミング：着地・休憩・何かに夢中な瞬間を狙う
        Bait,     // 餌で誘う：好物を置くと寄ってきて食べている間は捕まえやすい
        Discover, // 発見型：擬態・隠れ場所を見抜く（[E]でよく見ると見つかる）
        Lure      // 環境誘致：好む場所（灯り・花・箱など）に現れたところを捕まえる
    }

    /// <summary>野生での動き方（生き物仕様 §3 の習性から）。</summary>
    public enum WildBehavior
    {
        Wander,     // うろうろ歩く
        Float,      // ぷかぷか浮かぶ（水面・空中）
        Hop,        // 何度か跳ねてから休む
        Roll,       // 驚くと丸まって転がる
        Camouflage, // 擬態してじっとしている（時々小さく動く）
        Flyer,      // 飛び回り、時々止まる
        Burrow,     // 地中に潜り、時々顔を出す
        Sleeper,    // 眠っている
        Copycat,    // ほかの者の動きを真似する（マネピ）
        Stick,      // 何かに張り付いている
        Drift,      // 風に乗って漂う
        Swarm       // 群れで光りながら動く
    }

    /// <summary>活動する時間帯（四季なし。時間帯・天候・場所で出現が変わる：生き物仕様 §4）。</summary>
    [Flags]
    public enum ActiveTime
    {
        None = 0,
        LateNight = 1 << 0,    // 0-5
        EarlyMorning = 1 << 1, // 5-8
        Morning = 1 << 2,      // 8-12
        Afternoon = 1 << 3,    // 12-17
        Evening = 1 << 4,      // 17-20
        Night = 1 << 5,        // 20-24
        Day = EarlyMorning | Morning | Afternoon | Evening,
        Daytime = Morning | Afternoon,
        Dark = LateNight | Night | Evening,
        Always = LateNight | EarlyMorning | Morning | Afternoon | Evening | Night
    }

    /// <summary>好む天候（出現率の補正）。</summary>
    [Flags]
    public enum WeatherLike
    {
        None = 0,
        Rain = 1 << 0,      // 雨で増える
        Dry = 1 << 1,       // 雨の日は隠れる
        Sunny = 1 << 2,     // 晴れで増える
        Wind = 1 << 3,      // 風で増える
        Fog = 1 << 4,       // 霧で増える
        AfterRain = 1 << 5  // 雨上がりに増える
    }

    /// <summary>見た目の形（プリミティブの組み合わせ方）。</summary>
    public enum BodyShape
    {
        Round, Mole, Bird, LongArm, Bug, Fluffy, Nut, Leaf, Hopper, Roller, LongEar, Seedfluff,
        Mushroom, Shell, Lizard, Bubble, Fishy, Umbrella, Box, Mimic, Drummer, Stacker, Rock, Echo,
        Shadow, Cocoon, Star
    }

    /// <summary>1種族の定義。</summary>
    public sealed class CreatureSpecies
    {
        public int No;
        public string Id;
        public string NameJa;
        public string NameEn;
        public CreatureGroup Group;
        /// <summary>外見・野生での習性（観察できる事実。宇宙語の訳ではない）。</summary>
        public string HabitJa, HabitEn;
        /// <summary>発見・捕獲の手掛かり（名人の実演や観察で分かること）。</summary>
        public string ClueJa, ClueEn;
        /// <summary>捕獲後の魅力。</summary>
        public string CharmJa, CharmEn;
        public AreaType[] Areas = new AreaType[0];
        /// <summary>HabitatPoint.feature の好み（"tree" "flower" "rock" "mud" "grass" "fallen_log" "roof" "window" "streetlight" "box" など）。</summary>
        public string[] Features = new string[0];
        public ActiveTime Active = ActiveTime.Always;
        public WeatherLike Weather;
        /// <summary>この宇宙固有現象の時に増える。</summary>
        public AlienPhenomenon Phenomena;
        /// <summary>鉱山に出る（AreaChangedEvent で "mine:N" に入った時）。</summary>
        public bool Underground;
        public int MinFloor = 1, MaxFloor = 10;
        /// <summary>地上にも出る（地下生物の一部）。</summary>
        public bool AlsoOverworld = true;
        public CatchStyle Catch;
        public WildBehavior Behavior;
        /// <summary>能力（農業・料理・加工・修理で共通。活動ごとに分断しない）。空なら観察・かわいさ重視の種。</summary>
        public CreatureAbility[] Abilities = new CreatureAbility[0];
        /// <summary>希少度 0〜5。</summary>
        public int Rarity;
        public int BasePrice;
        public BodyShape Shape;
        public Color Main = Color.white;
        public Color Accent = Color.gray;
        public bool Glows;
        /// <summary>夜だけ光る（昼は光らない）。</summary>
        public bool GlowsOnlyAtNight;
        public bool MakesSound;
        public float Scale = 1f;
        /// <summary>好きな食べ物の系統（"fruit" "flower" "seed" "mineral" "glow" "moss" "water" "veg" "sweet"）。</summary>
        public string FavoriteFood = "fruit";
        /// <summary>野生で見せる特徴的な行動（観察で図鑑に記録される）。</summary>
        public string SignatureBehavior;
        /// <summary>そのほかに観察できる行動。</summary>
        public string[] ExtraBehaviors = new string[0];
        /// <summary>ロケット修理や器具の「提案」割り当てか（README に明記）。</summary>
        public bool HasProposedAbility;

        public bool IsWorker => Abilities != null && Abilities.Length > 0;
        public string Name => Loc.T(NameJa, NameEn);
        public string NumberLabel => No.ToString("000");

        public bool HasAbility(CreatureAbility a)
        {
            if (Abilities == null) return false;
            for (int i = 0; i < Abilities.Length; i++) if (Abilities[i] == a) return true;
            return false;
        }

        public static ActiveTime ToMask(DayPart p)
        {
            switch (p)
            {
                case DayPart.LateNight: return ActiveTime.LateNight;
                case DayPart.EarlyMorning: return ActiveTime.EarlyMorning;
                case DayPart.Morning: return ActiveTime.Morning;
                case DayPart.Afternoon: return ActiveTime.Afternoon;
                case DayPart.Evening: return ActiveTime.Evening;
                default: return ActiveTime.Night;
            }
        }
    }

    /// <summary>観察できる行動の事実（図鑑に自動記録してよい客観情報：生き物仕様 §10.1）。</summary>
    public sealed class BehaviorFact
    {
        public string Key;
        public string Ja;
        public string En;
        /// <summary>この行動を見ると「得意そうなこと」として分かる能力。</summary>
        public CreatureAbility[] Reveals = new CreatureAbility[0];
        public string Text => Loc.T(Ja, En);
    }

    /// <summary>生物同士の交流（生き物仕様 §7、家具仕様 §9.2）。</summary>
    public sealed class PairInteraction
    {
        public string A;
        public string B;
        public string Key;
        public string Ja;
        public string En;
        /// <summary>見た目の演出の種類。</summary>
        public PairStyle Style;
    }

    public enum PairStyle { Chase, SleepTogether, FlyTogether, AlternateGlow, Play, Splash, CopyRhythm }

    /// <summary>能力の表示名（UI。図鑑では観察で分かった能力だけ表示する）。</summary>
    public static class AbilityText
    {
        public static void Texts(CreatureAbility a, out string ja, out string en)
        {
            switch (a)
            {
                case CreatureAbility.Water: ja = "水を出す"; en = "Water"; return;
                case CreatureAbility.Till: ja = "耕す"; en = "Till"; return;
                case CreatureAbility.Sow: ja = "種をまく"; en = "Sow"; return;
                case CreatureAbility.Harvest: ja = "収穫する"; en = "Harvest"; return;
                case CreatureAbility.SoilImprove: ja = "土を豊かにする"; en = "Enrich soil"; return;
                case CreatureAbility.Pollinate: ja = "受粉を助ける"; en = "Pollinate"; return;
                case CreatureAbility.Glow: ja = "光る"; en = "Glow"; return;
                case CreatureAbility.Carry: ja = "運ぶ"; en = "Carry"; return;
                case CreatureAbility.Heat: ja = "温める"; en = "Heat"; return;
                case CreatureAbility.SeedCollect: ja = "種を集める"; en = "Collect seeds"; return;
                case CreatureAbility.Grind: ja = "転がって砕く"; en = "Grind"; return;
                case CreatureAbility.Sound: ja = "音を鳴らす"; en = "Sound"; return;
                case CreatureAbility.Bubble: ja = "泡を作る"; en = "Bubbles"; return;
                case CreatureAbility.Splash: ja = "水を飛ばす"; en = "Splash"; return;
                case CreatureAbility.Pack: ja = "箱にしまう"; en = "Pack"; return;
                case CreatureAbility.Mimic: ja = "まねる"; en = "Mimic"; return;
                case CreatureAbility.ColorShift: ja = "色を変える"; en = "Color shift"; return;
                case CreatureAbility.Stack: ja = "積み重ねる"; en = "Stack"; return;
                case CreatureAbility.MineralDust: ja = "鉱石粉をまとう"; en = "Mineral dust"; return;
                case CreatureAbility.Echo: ja = "反響させる"; en = "Echo"; return;
                case CreatureAbility.Stick: ja = "張り付く"; en = "Stick"; return;
                case CreatureAbility.Shade: ja = "影を作る"; en = "Shade"; return;
                case CreatureAbility.Float: ja = "浮かぶ"; en = "Float"; return;
                case CreatureAbility.Press: ja = "ぎゅっと押す"; en = "Press"; return;
                case CreatureAbility.Ferment: ja = "ぷくぷく発酵"; en = "Ferment"; return;
                case CreatureAbility.Cool: ja = "ひんやり冷やす"; en = "Cool"; return;
                case CreatureAbility.Stabilize: ja = "揺れを鎮める"; en = "Stabilize"; return;
                case CreatureAbility.Sense: ja = "気配を感じる"; en = "Sense"; return;
                default: ja = a.ToString(); en = a.ToString(); return;
            }
        }

        public static string Label(CreatureAbility a)
        {
            Texts(a, out var ja, out var en);
            return Loc.T(ja, en);
        }
    }

    public static class PlacementText
    {
        public static string Label(CreaturePlacement p)
        {
            switch (p)
            {
                case CreaturePlacement.Companion: return Loc.T("連れて歩く", "Companion");
                case CreaturePlacement.Farm: return Loc.T("農場", "Farm");
                case CreaturePlacement.Home: return Loc.T("自宅の庭", "Home garden");
                case CreaturePlacement.Habitat: return Loc.T("住処の家具", "Habitat furniture");
                case CreaturePlacement.Device: return Loc.T("器具", "Device");
                case CreaturePlacement.Shop: return Loc.T("商店", "Shop");
                case CreaturePlacement.MessagePlaza: return Loc.T("伝言広場", "Message plaza");
                case CreaturePlacement.RocketWorkshop: return Loc.T("修理作業場", "Rocket workshop");
                case CreaturePlacement.Storage: return Loc.T("ケージで休む", "Resting in cage");
                case CreaturePlacement.Deposited: return Loc.T("ほかの星に預けている", "Left on another planet");
                default: return p.ToString();
            }
        }

        /// <summary>プレイヤーが持ち歩く（PlayerProfile に入る）配置か。</summary>
        public static bool IsCarried(CreaturePlacement p) => p == CreaturePlacement.Companion || p == CreaturePlacement.Storage || p == CreaturePlacement.Deposited;
    }

    /// <summary>性格（個体差：生き物仕様 §6.3）。</summary>
    public enum CreaturePersonality { Calm, Playful, Shy, Sleepy, Curious, Brave, Gluttonous, Cuddly }

    public static class PersonalityText
    {
        public static string Label(CreaturePersonality p)
        {
            switch (p)
            {
                case CreaturePersonality.Calm: return Loc.T("おっとり", "Calm");
                case CreaturePersonality.Playful: return Loc.T("遊び好き", "Playful");
                case CreaturePersonality.Shy: return Loc.T("こわがり", "Shy");
                case CreaturePersonality.Sleepy: return Loc.T("ねぼすけ", "Sleepy");
                case CreaturePersonality.Curious: return Loc.T("好奇心旺盛", "Curious");
                case CreaturePersonality.Brave: return Loc.T("どっしり", "Bold");
                case CreaturePersonality.Gluttonous: return Loc.T("くいしんぼう", "Glutton");
                default: return Loc.T("甘えんぼう", "Cuddly");
            }
        }
    }
}
