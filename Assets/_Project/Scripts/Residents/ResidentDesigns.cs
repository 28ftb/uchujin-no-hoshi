using System.Collections.Generic;
using Uchujin.Core;
using UnityEngine;

namespace Uchujin.Residents
{
    /// <summary>
    /// デザイン済みの住民100人（10系統×10人）。星ごとにシードでこの中から選ばれて村に住む。
    /// 外見は Resources/Residents/&lt;Id&gt;.fbx のモデル（アクション Idle / Signature / Sleep）。
    /// 名前は星の宇宙語で生成するので、ここには持たない（コメントの仮称は開発用で、画面には出さない）。
    /// このファイルは Residents フォルダの design/residents_100.json と各モデルの検査値から生成した。
    /// </summary>
    public sealed class ResidentDesign
    {
        /// <summary>4桁のデザインID（"0101" など）。モデルのファイル名と同じ。</summary>
        public readonly string Id;
        public readonly AlienLineage Lineage;
        /// <summary>デザイン資料の役割に一番近い職業。</summary>
        public readonly ResidentJob Job;
        /// <summary>代わりに就いてもよい職業（村で足りない職業を埋めるとき）。</summary>
        public readonly ResidentJob[] AltJobs;
        /// <summary>モデルの全高（m）。</summary>
        public readonly float Height;
        /// <summary>体ごと宙に浮く住民（モデル自体が浮いた位置で作られている）。</summary>
        public readonly bool Floats;
        /// <summary>寝姿のアクション（Sleep）がある。</summary>
        public readonly bool HasSleep;
        public readonly Color MainColor;
        public readonly Color AccentColor;

        public ResidentDesign(string id, AlienLineage lineage, ResidentJob job, ResidentJob[] altJobs, float height, bool floats, bool hasSleep, Color main, Color accent)
        {
            Id = id; Lineage = lineage; Job = job; AltJobs = altJobs; Height = height; Floats = floats; HasSleep = hasSleep; MainColor = main; AccentColor = accent;
        }

        /// <summary>その職業に就けるか（第一の職業か、代わりの職業）。</summary>
        public bool CanDo(ResidentJob job)
        {
            if (Job == job) return true;
            for (int i = 0; i < AltJobs.Length; i++) if (AltJobs[i] == job) return true;
            return false;
        }

        /// <summary>Resources.Load 用のパス。</summary>
        public string ResourcePath => ResidentDesigns.ResourceFolder + "/" + Id;
    }

    public static class ResidentDesigns
    {
        public const string ResourceFolder = "Residents";
        private static readonly ResidentJob[] NoJobs = new ResidentJob[0];

        public static readonly ResidentDesign[] All =
        {
            // 01-01 ポルン（おっちょこちょいな案内好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0101", AlienLineage.Round, ResidentJob.Courier, NoJobs, 0.806f, false, false, new Color(0.949f, 0.831f, 0.482f), new Color(0.215f, 0.463f, 0.785f)),
            // 01-02 ミモラ（新しい味を探す料理好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0102", AlienLineage.Round, ResidentJob.Cook, NoJobs, 0.757f, false, false, new Color(0.914f, 0.592f, 0.334f), new Color(1.000f, 0.941f, 0.851f)),
            // 01-03 ナネッタ（花を愛する村の古株）— 開発用の仮称。画面には出さない
            new ResidentDesign("0103", AlienLineage.Round, ResidentJob.Florist, new[] { ResidentJob.Retired }, 0.950f, false, false, new Color(0.568f, 0.663f, 0.525f), new Color(0.898f, 0.463f, 0.384f)),
            // 01-04 ソラット（宇宙に憧れる夢見がち）— 開発用の仮称。画面には出さない
            new ResidentDesign("0104", AlienLineage.Round, ResidentJob.Traveler, new[] { ResidentJob.Child }, 1.031f, false, false, new Color(0.608f, 0.608f, 0.823f), new Color(0.914f, 0.518f, 0.258f)),
            // 01-05 カラミ（きっちり者の種屋さん）— 開発用の仮称。画面には出さない
            new ResidentDesign("0105", AlienLineage.Round, ResidentJob.SeedSeller, NoJobs, 0.860f, false, false, new Color(0.953f, 0.894f, 0.812f), new Color(0.381f, 0.553f, 0.404f)),
            // 01-06 ガッチョ（無口で器用な鍛冶屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0106", AlienLineage.Round, ResidentJob.Blacksmith, NoJobs, 1.055f, false, false, new Color(0.330f, 0.330f, 0.357f), new Color(1.000f, 0.941f, 0.851f)),
            // 01-07 ウメリ（釣りが大好きな負けず嫌い）— 開発用の仮称。画面には出さない
            new ResidentDesign("0107", AlienLineage.Round, ResidentJob.FishingMaster, NoJobs, 0.812f, false, false, new Color(0.297f, 0.624f, 0.760f), new Color(0.297f, 0.396f, 0.568f)),
            // 01-08 ルクノ（家具づくりが好きな職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0108", AlienLineage.Round, ResidentJob.Carpenter, NoJobs, 0.950f, false, false, new Color(0.721f, 0.471f, 0.278f), new Color(1.000f, 0.941f, 0.851f)),
            // 01-09 モヤト（天気を調べる不思議好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0109", AlienLineage.Round, ResidentJob.WeatherReader, new[] { ResidentJob.Scholar }, 0.890f, false, false, new Color(0.721f, 0.682f, 0.816f), new Color(0.749f, 0.777f, 0.808f)),
            // 01-10 ペピン（せっかちな配達員）— 開発用の仮称。画面には出さない
            new ResidentDesign("0110", AlienLineage.Round, ResidentJob.Courier, NoJobs, 0.742f, false, false, new Color(0.914f, 0.529f, 0.568f), new Color(0.914f, 0.518f, 0.258f)),
            // 02-01 ヌルダク（狭い管をくぐる配管・井戸直し）— 開発用の仮称。画面には出さない
            new ResidentDesign("0201", AlienLineage.Mochi, ResidentJob.Builder, new[] { ResidentJob.Mechanic }, 0.657f, false, true, new Color(0.396f, 0.498f, 0.604f), new Color(0.777f, 0.651f, 0.420f)),
            // 02-02 ユノボ（噂が集まる湯屋の番台）— 開発用の仮称。画面には出さない
            new ResidentDesign("0202", AlienLineage.Mochi, ResidentJob.Merchant, new[] { ResidentJob.Cook }, 0.729f, false, true, new Color(0.851f, 0.510f, 0.474f), new Color(0.961f, 0.918f, 0.851f)),
            // 02-03 ヒヤリコ（ひんやり湿布の手当て屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0203", AlienLineage.Mochi, ResidentJob.Doctor, NoJobs, 0.560f, false, true, new Color(0.498f, 0.682f, 0.533f), new Color(0.949f, 0.910f, 0.839f)),
            // 02-04 ナデコ（毛のない毛づくろい屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0204", AlienLineage.Mochi, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.620f, false, true, new Color(0.788f, 0.585f, 0.568f), new Color(0.262f, 0.224f, 0.262f)),
            // 02-05 ノビヨン（誰でもできる体操を目指す先生）— 開発用の仮称。画面には出さない
            new ResidentDesign("0205", AlienLineage.Mochi, ResidentJob.Teacher, NoJobs, 0.840f, false, true, new Color(0.769f, 0.847f, 0.309f), new Color(0.203f, 0.248f, 0.439f)),
            // 02-06 ビドロ（息で器を作るガラス吹き職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0206", AlienLineage.Mochi, ResidentJob.Artist, new[] { ResidentJob.Blacksmith }, 0.903f, false, true, new Color(0.400f, 0.780f, 0.772f), new Color(0.851f, 0.596f, 0.224f)),
            // 02-07 ウツシ（形を写し取る型取り・拓本職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0207", AlienLineage.Mochi, ResidentJob.Scholar, new[] { ResidentJob.Artist }, 0.755f, false, true, new Color(0.870f, 0.851f, 0.823f), new Color(0.961f, 0.918f, 0.851f)),
            // 02-08 ノリノ（くっつきたがりの糊屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0208", AlienLineage.Mochi, ResidentJob.Merchant, new[] { ResidentJob.Carpenter }, 0.650f, false, true, new Color(0.780f, 0.553f, 0.196f), new Color(0.851f, 0.596f, 0.224f)),
            // 02-09 ザブラ（雨の日が苦手な防火・散水番）— 開発用の仮称。画面には出さない
            new ResidentDesign("0209", AlienLineage.Mochi, ResidentJob.Builder, new[] { ResidentJob.FarmingMaster }, 0.875f, false, true, new Color(0.851f, 0.369f, 0.306f), new Color(0.777f, 0.651f, 0.420f)),
            // 02-10 ズシモ（心まで量れると噂の計量係）— 開発用の仮称。画面には出さない
            new ResidentDesign("0210", AlienLineage.Mochi, ResidentJob.Merchant, NoJobs, 0.620f, false, true, new Color(0.529f, 0.580f, 0.274f), new Color(0.777f, 0.651f, 0.420f)),
            // 03-01 フワネル（眠ることが大好きな寝具職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0301", AlienLineage.Plush, ResidentJob.Carpenter, NoJobs, 0.768f, false, true, new Color(0.941f, 0.894f, 0.847f), new Color(1.000f, 0.961f, 0.910f)),
            // 03-02 トビム（じっとしていられない虫取り名人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0302", AlienLineage.Plush, ResidentJob.CreatureExpert, new[] { ResidentJob.Child }, 0.815f, false, true, new Color(0.859f, 0.529f, 0.313f), new Color(0.941f, 0.651f, 0.424f)),
            // 03-03 シュロッタ（宝石を磨く無口な職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0303", AlienLineage.Plush, ResidentJob.Miner, new[] { ResidentJob.Blacksmith }, 0.629f, false, true, new Color(0.466f, 0.466f, 0.482f), new Color(0.667f, 0.655f, 0.647f)),
            // 03-04 ミミュラ（おしゃれが大好きな服飾家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0304", AlienLineage.Plush, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.716f, false, true, new Color(0.785f, 0.518f, 0.596f), new Color(0.655f, 0.549f, 0.686f)),
            // 03-05 ケモリン（音楽が大好きな広場の演奏家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0305", AlienLineage.Plush, ResidentJob.Musician, NoJobs, 0.779f, false, true, new Color(0.718f, 0.831f, 0.760f), new Color(0.568f, 0.725f, 0.647f)),
            // 03-06 コメロ（食べ歩きが趣味の小さな商人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0306", AlienLineage.Plush, ResidentJob.Merchant, new[] { ResidentJob.Cook }, 0.605f, false, true, new Color(0.714f, 0.502f, 0.309f), new Color(0.831f, 0.655f, 0.487f)),
            // 03-07 ネボル（夜の村を好む物静かな住民）— 開発用の仮称。画面には出さない
            new ResidentDesign("0307", AlienLineage.Plush, ResidentJob.Miner, NoJobs, 0.705f, false, true, new Color(0.147f, 0.176f, 0.321f), new Color(0.777f, 0.792f, 0.843f)),
            // 03-08 モワッチ（片付け上手なきれい好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0308", AlienLineage.Plush, ResidentJob.Retired, new[] { ResidentJob.Teacher }, 0.834f, false, true, new Color(0.510f, 0.592f, 0.479f), new Color(0.659f, 0.725f, 0.608f)),
            // 03-09 クルポン（宇宙生物を愛する飼育好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0309", AlienLineage.Plush, ResidentJob.CreatureExpert, NoJobs, 0.755f, false, true, new Color(0.608f, 0.533f, 0.760f), new Color(0.769f, 0.714f, 0.870f)),
            // 03-10 フィルモ（引っ越しを考えている旅好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0310", AlienLineage.Plush, ResidentJob.Traveler, NoJobs, 0.895f, false, true, new Color(0.611f, 0.788f, 0.847f), new Color(0.772f, 0.882f, 0.910f)),
            // 04-01 ミミラト（音を集める楽器職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0401", AlienLineage.ChibiAnimal, ResidentJob.Musician, new[] { ResidentJob.Carpenter }, 0.679f, false, true, new Color(0.906f, 0.800f, 0.447f), new Color(0.945f, 0.851f, 0.529f)),
            // 04-02 カワピル（川を愛する水辺の世話役）— 開発用の仮称。画面には出さない
            new ResidentDesign("0402", AlienLineage.ChibiAnimal, ResidentJob.FishingMaster, NoJobs, 0.568f, false, true, new Color(0.278f, 0.682f, 0.694f), new Color(0.455f, 0.780f, 0.753f)),
            // 04-03 ナギルネ（夜の村を彩る灯り職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0403", AlienLineage.ChibiAnimal, ResidentJob.Builder, new[] { ResidentJob.Mechanic }, 0.711f, false, true, new Color(0.556f, 0.529f, 0.611f), new Color(0.718f, 0.682f, 0.757f)),
            // 04-04 シカノン（森の植物を守る庭師）— 開発用の仮称。画面には出さない
            new ResidentDesign("0404", AlienLineage.ChibiAnimal, ResidentJob.Florist, new[] { ResidentJob.FarmingMaster }, 0.821f, false, true, new Color(0.533f, 0.635f, 0.471f), new Color(0.663f, 0.741f, 0.580f)),
            // 04-05 トビッカ（絵を描くことが大好きな看板絵師）— 開発用の仮称。画面には出さない
            new ResidentDesign("0405", AlienLineage.ChibiAnimal, ResidentJob.Artist, NoJobs, 0.636f, false, true, new Color(0.898f, 0.611f, 0.396f), new Color(0.941f, 0.741f, 0.529f)),
            // 04-06 コトロム（不思議な焼き物を作る陶芸家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0406", AlienLineage.ChibiAnimal, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.726f, false, true, new Color(0.721f, 0.474f, 0.272f), new Color(0.816f, 0.580f, 0.346f)),
            // 04-07 ハリクル（地形を調べる小さな測量家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0407", AlienLineage.ChibiAnimal, ResidentJob.Scholar, new[] { ResidentJob.Builder }, 0.449f, false, true, new Color(0.772f, 0.675f, 0.529f), new Color(0.859f, 0.796f, 0.682f)),
            // 04-08 マグノル（古いものを大切にする古道具商）— 開発用の仮称。画面には出さない
            new ResidentDesign("0408", AlienLineage.ChibiAnimal, ResidentJob.Merchant, NoJobs, 0.844f, false, true, new Color(0.506f, 0.349f, 0.262f), new Color(0.647f, 0.474f, 0.346f)),
            // 04-09 ピラノン（村の行事を盛り上げる飾り職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0409", AlienLineage.ChibiAnimal, ResidentJob.Artist, NoJobs, 0.581f, false, true, new Color(0.914f, 0.549f, 0.450f), new Color(0.839f, 0.416f, 0.349f)),
            // 04-10 ビビナ（発酵食品を研究する食いしん坊）— 開発用の仮称。画面には出さない
            new ResidentDesign("0410", AlienLineage.ChibiAnimal, ResidentJob.Cook, NoJobs, 0.662f, false, true, new Color(0.721f, 0.529f, 0.330f), new Color(0.831f, 0.667f, 0.439f)),
            // 05-01 ポッカ（忘れ物を届ける世話好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0501", AlienLineage.Floating, ResidentJob.Courier, NoJobs, 0.647f, true, true, new Color(0.910f, 0.843f, 0.482f), new Color(0.957f, 0.918f, 0.867f)),
            // 05-02 カゼルン（風の飾りを作る職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0502", AlienLineage.Floating, ResidentJob.WeatherReader, new[] { ResidentJob.Artist }, 0.506f, true, true, new Color(0.733f, 0.867f, 0.823f), new Color(0.737f, 0.906f, 0.875f)),
            // 05-03 ベルピノ（小さな音を作る鈴職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0503", AlienLineage.Floating, ResidentJob.Musician, NoJobs, 0.495f, true, true, new Color(0.663f, 0.835f, 0.906f), new Color(0.827f, 0.725f, 0.428f)),
            // 05-04 チャプル（お茶を淹れるのが大好きな住民）— 開発用の仮称。画面には出さない
            new ResidentDesign("0504", AlienLineage.Floating, ResidentJob.Cook, NoJobs, 0.774f, true, true, new Color(0.953f, 0.635f, 0.495f), new Color(1.000f, 0.894f, 0.800f)),
            // 05-05 ワッカリ（高い場所が大好きな建物の見回り役）— 開発用の仮称。画面には出さない
            new ResidentDesign("0505", AlienLineage.Floating, ResidentJob.Builder, NoJobs, 0.703f, true, true, new Color(0.616f, 0.804f, 0.718f), new Color(0.384f, 0.710f, 0.777f)),
            // 05-06 ホシト（夜空を描く星の絵描き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0506", AlienLineage.Floating, ResidentJob.Artist, new[] { ResidentJob.Traveler }, 0.541f, true, true, new Color(0.156f, 0.235f, 0.450f), new Color(0.737f, 0.906f, 0.875f)),
            // 05-07 カイユラ（海岸の小物を作る貝細工職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0507", AlienLineage.Floating, ResidentJob.Artist, new[] { ResidentJob.FishingMaster }, 0.618f, true, true, new Color(0.957f, 0.918f, 0.867f), new Color(0.906f, 0.894f, 0.839f)),
            // 05-08 フミト（昔の本を集める古書店主）— 開発用の仮称。画面には出さない
            new ResidentDesign("0508", AlienLineage.Floating, ResidentJob.Scholar, new[] { ResidentJob.Merchant }, 0.715f, true, true, new Color(0.910f, 0.867f, 0.772f), new Color(0.957f, 0.918f, 0.867f)),
            // 05-09 ジキロ（石の違いを見分ける鉱石選別職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0509", AlienLineage.Floating, ResidentJob.Miner, new[] { ResidentJob.Blacksmith }, 0.569f, true, true, new Color(0.522f, 0.502f, 0.659f), new Color(0.588f, 0.843f, 0.847f)),
            // 05-10 ヒラメラ（身振りで物語を伝える人形劇師）— 開発用の仮称。画面には出さない
            new ResidentDesign("0510", AlienLineage.Floating, ResidentJob.Musician, new[] { ResidentJob.Child, ResidentJob.Teacher }, 0.690f, true, true, new Color(0.785f, 0.565f, 0.714f), new Color(0.941f, 0.863f, 0.812f)),
            // 06-01 ガチャルン（宇宙船に夢中な修理屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0601", AlienLineage.MultiArm, ResidentJob.Mechanic, NoJobs, 0.780f, false, true, new Color(0.272f, 0.682f, 0.714f), new Color(0.835f, 0.682f, 0.364f)),
            // 06-02 イトリィ（どんな体型の服でも作れる仕立て屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0602", AlienLineage.MultiArm, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.820f, false, true, new Color(0.804f, 0.686f, 0.851f), new Color(0.933f, 0.898f, 0.859f)),
            // 06-03 パピョン（身振りで物語を伝える語り部）— 開発用の仮称。画面には出さない
            new ResidentDesign("0603", AlienLineage.MultiArm, ResidentJob.Teacher, NoJobs, 0.760f, false, true, new Color(0.910f, 0.628f, 0.435f), new Color(0.949f, 0.867f, 0.760f)),
            // 06-04 キマメ（珍しい苗を育てる農園の研究家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0604", AlienLineage.MultiArm, ResidentJob.FarmingMaster, new[] { ResidentJob.SeedSeller }, 0.721f, false, true, new Color(0.568f, 0.655f, 0.474f), new Color(0.851f, 0.741f, 0.663f)),
            // 06-05 トントラ（六本腕のにぎやかな打楽器奏者）— 開発用の仮称。画面には出さない
            new ResidentDesign("0605", AlienLineage.MultiArm, ResidentJob.Musician, NoJobs, 0.960f, false, true, new Color(0.851f, 0.428f, 0.404f), new Color(0.851f, 0.679f, 0.346f)),
            // 06-06 ルーペラ（小さな世界を覗く観測器職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0606", AlienLineage.MultiArm, ResidentJob.Scholar, new[] { ResidentJob.Mechanic }, 0.840f, false, true, new Color(0.514f, 0.733f, 0.831f), new Color(0.725f, 0.780f, 0.808f)),
            // 06-07 ムギロ（五本腕の移動屋台商人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0607", AlienLineage.MultiArm, ResidentJob.Merchant, new[] { ResidentJob.Cook }, 0.949f, false, true, new Color(0.721f, 0.541f, 0.334f), new Color(0.577f, 0.780f, 0.639f)),
            // 06-08 ナベッチ（保存食を作る四本腕の料理職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0608", AlienLineage.MultiArm, ResidentJob.Cook, NoJobs, 0.810f, false, true, new Color(0.878f, 0.604f, 0.384f), new Color(0.400f, 0.289f, 0.227f)),
            // 06-09 ハチミナ（八本腕のモザイク職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0609", AlienLineage.MultiArm, ResidentJob.Builder, new[] { ResidentJob.Artist }, 0.807f, false, true, new Color(0.749f, 0.679f, 0.887f), new Color(1.000f, 0.941f, 0.855f)),
            // 06-10 マロモ（小さな生き物を大切にする飼育家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0610", AlienLineage.MultiArm, ResidentJob.CreatureExpert, NoJobs, 0.760f, false, true, new Color(0.933f, 0.906f, 0.851f), new Color(0.659f, 0.772f, 0.631f)),
            // 07-01 キラポル（村の夜道を照らす灯り職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0701", AlienLineage.StarChild, ResidentJob.Builder, new[] { ResidentJob.Mechanic }, 0.670f, false, true, new Color(0.910f, 0.785f, 0.309f), new Color(0.953f, 0.914f, 0.785f)),
            // 07-02 ミカノア（夜に焼き菓子を作るお菓子職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0702", AlienLineage.StarChild, ResidentJob.Cook, NoJobs, 0.720f, false, true, new Color(0.957f, 0.922f, 0.851f), new Color(0.725f, 0.663f, 0.820f)),
            // 07-03 リンディ（惑星の輪を持つアクセサリー職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0703", AlienLineage.StarChild, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.753f, false, true, new Color(0.549f, 0.804f, 0.772f), new Color(0.835f, 0.714f, 0.369f)),
            // 07-04 スイロ（彗星の尾を持つ風景写真家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0704", AlienLineage.StarChild, ResidentJob.Artist, new[] { ResidentJob.Traveler }, 0.690f, false, true, new Color(0.898f, 0.549f, 0.450f), new Color(0.502f, 0.737f, 0.831f)),
            // 07-05 カゲノ（日食のような姿をした古書修復家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0705", AlienLineage.StarChild, ResidentJob.Scholar, NoJobs, 0.830f, false, true, new Color(0.851f, 0.721f, 0.381f), new Color(0.147f, 0.176f, 0.321f)),
            // 07-06 ゴロッタ（隕石のような体をした石工）— 開発用の仮称。画面には出さない
            new ResidentDesign("0706", AlienLineage.StarChild, ResidentJob.Builder, new[] { ResidentJob.Miner }, 0.940f, false, true, new Color(0.659f, 0.364f, 0.283f), new Color(0.255f, 0.725f, 0.675f)),
            // 07-07 ツヅリ（星座模様の体を持つ刺繍職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0707", AlienLineage.StarChild, ResidentJob.Artist, NoJobs, 0.860f, false, true, new Color(0.143f, 0.190f, 0.321f), new Color(0.757f, 0.827f, 0.918f)),
            // 07-08 モヤシル（星雲のような毛並みを持つ染色職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0708", AlienLineage.StarChild, ResidentJob.Artist, new[] { ResidentJob.Florist }, 0.790f, false, true, new Color(0.721f, 0.655f, 0.843f), new Color(0.898f, 0.620f, 0.745f)),
            // 07-09 ヒナリ（太陽のような頭を持つ朝食職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0709", AlienLineage.StarChild, ResidentJob.Cook, NoJobs, 0.710f, false, true, new Color(0.914f, 0.568f, 0.262f), new Color(0.949f, 0.867f, 0.682f)),
            // 07-10 オーロミ（オーロラのような膜を持つ風景案内人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0710", AlienLineage.StarChild, ResidentJob.Traveler, new[] { ResidentJob.WeatherReader }, 0.890f, false, true, new Color(0.396f, 0.741f, 0.710f), new Color(0.721f, 0.647f, 0.839f)),
            // 08-01 カサノン（雨の日が大好きな雨具職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0801", AlienLineage.Lifestyle, ResidentJob.WeatherReader, new[] { ResidentJob.Carpenter }, 0.750f, false, true, new Color(0.663f, 0.851f, 0.745f), new Color(0.518f, 0.667f, 0.447f)),
            // 08-02 ポケム（何でも整理したがる道具管理係）— 開発用の仮称。画面には出さない
            new ResidentDesign("0802", AlienLineage.Lifestyle, ResidentJob.Merchant, new[] { ResidentJob.Blacksmith }, 0.700f, false, true, new Color(0.894f, 0.635f, 0.463f), new Color(0.407f, 0.293f, 0.243f)),
            // 08-03 キイリ（村の空き家を世話する管理人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0803", AlienLineage.Lifestyle, ResidentJob.Builder, new[] { ResidentJob.Carpenter }, 0.790f, false, true, new Color(0.152f, 0.243f, 0.407f), new Color(0.816f, 0.679f, 0.364f)),
            // 08-04 メジャラ（いろいろな体型に合わせる家具調整師）— 開発用の仮称。画面には出さない
            new ResidentDesign("0804", AlienLineage.Lifestyle, ResidentJob.Carpenter, NoJobs, 0.820f, false, true, new Color(0.714f, 0.635f, 0.827f), new Color(0.949f, 0.926f, 0.941f)),
            // 08-05 フロミィ（贈り物を包むのが大好きな住民）— 開発用の仮称。画面には出さない
            new ResidentDesign("0805", AlienLineage.Lifestyle, ResidentJob.Merchant, NoJobs, 0.710f, false, true, new Color(0.910f, 0.635f, 0.682f), new Color(0.910f, 0.847f, 0.772f)),
            // 08-06 メクリ（何でも観察して描く記録好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0806", AlienLineage.Lifestyle, ResidentJob.Scholar, NoJobs, 0.760f, false, true, new Color(0.572f, 0.667f, 0.553f), new Color(0.910f, 0.847f, 0.772f)),
            // 08-07 ツギル（古い衣服を直す繕い屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0807", AlienLineage.Lifestyle, ResidentJob.Artist, new[] { ResidentJob.Merchant }, 0.820f, false, true, new Color(0.608f, 0.463f, 0.334f), new Color(0.890f, 0.831f, 0.725f)),
            // 08-08 サオルカ（釣り道具を大切にする道具好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0808", AlienLineage.Lifestyle, ResidentJob.FishingMaster, NoJobs, 0.739f, false, true, new Color(0.289f, 0.651f, 0.635f), new Color(0.839f, 0.729f, 0.471f)),
            // 08-09 ユゲッタ（外で食べるのが大好きなピクニック好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0809", AlienLineage.Lifestyle, ResidentJob.Cook, new[] { ResidentJob.Retired }, 0.780f, false, true, new Color(0.902f, 0.608f, 0.337f), new Color(0.572f, 0.407f, 0.262f)),
            // 08-10 トランカ（大きな旅行鞄を持つ新しい住民）— 開発用の仮称。画面には出さない
            new ResidentDesign("0810", AlienLineage.Lifestyle, ResidentJob.Traveler, NoJobs, 0.861f, false, true, new Color(0.663f, 0.585f, 0.777f), new Color(0.611f, 0.733f, 0.847f)),
            // 09-01 ニッパル（笑顔が止まらない歓迎好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0901", AlienLineage.Expressive, ResidentJob.Courier, NoJobs, 0.720f, false, true, new Color(0.914f, 0.847f, 0.580f), new Color(0.914f, 0.628f, 0.647f)),
            // 09-02 ビクッチ（怖がりなのに頼れる安全具職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0902", AlienLineage.Expressive, ResidentJob.Miner, new[] { ResidentJob.Blacksmith }, 0.750f, false, true, new Color(0.471f, 0.733f, 0.867f), new Color(0.957f, 0.914f, 0.847f)),
            // 09-03 ウルメア（嬉しくても泣いてしまう思い出好き）— 開発用の仮称。画面には出さない
            new ResidentDesign("0903", AlienLineage.Expressive, ResidentJob.Retired, new[] { ResidentJob.Scholar }, 0.680f, false, true, new Color(0.777f, 0.721f, 0.855f), new Color(0.843f, 0.941f, 0.929f)),
            // 09-04 プンスケ（怒っているように見える頑張り屋）— 開発用の仮称。画面には出さない
            new ResidentDesign("0904", AlienLineage.Expressive, ResidentJob.Carpenter, new[] { ResidentJob.Builder }, 0.726f, false, true, new Color(0.553f, 0.416f, 0.321f), new Color(0.890f, 0.604f, 0.369f)),
            // 09-05 クシャネ（顔の形が変わる製本職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0905", AlienLineage.Expressive, ResidentJob.Scholar, NoJobs, 0.685f, false, true, new Color(0.651f, 0.765f, 0.628f), new Color(0.933f, 0.906f, 0.827f)),
            // 09-06 パクルン（ひと口ごとの反応が大きい試食家）— 開発用の仮称。画面には出さない
            new ResidentDesign("0906", AlienLineage.Expressive, ResidentJob.Cook, NoJobs, 0.747f, false, true, new Color(0.890f, 0.631f, 0.392f), new Color(0.957f, 0.898f, 0.780f)),
            // 09-07 テレリィ（すぐ顔が赤くなる花束職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0907", AlienLineage.Expressive, ResidentJob.Florist, NoJobs, 0.663f, false, true, new Color(0.914f, 0.714f, 0.772f), new Color(0.506f, 0.655f, 0.474f)),
            // 09-08 パタメラ（全身で感情を表す踊り手）— 開発用の仮称。画面には出さない
            new ResidentDesign("0908", AlienLineage.Expressive, ResidentJob.Musician, new[] { ResidentJob.Teacher }, 0.781f, false, true, new Color(0.890f, 0.769f, 0.364f), new Color(0.317f, 0.682f, 0.714f)),
            // 09-09 マユノン（いつも不機嫌そうな検品職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0909", AlienLineage.Expressive, ResidentJob.Blacksmith, new[] { ResidentJob.Mechanic }, 0.820f, false, true, new Color(0.667f, 0.659f, 0.635f), new Color(0.577f, 0.420f, 0.274f)),
            // 09-10 ポカロ（驚くたびに顔が伸びる模型職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("0910", AlienLineage.Expressive, ResidentJob.Mechanic, new[] { ResidentJob.Carpenter, ResidentJob.Child }, 0.700f, false, true, new Color(0.663f, 0.600f, 0.808f), new Color(0.953f, 0.910f, 0.831f)),
            // 10-01 フクプニ（ふわふわなのにぷるぷるな洗濯職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1001", AlienLineage.Mixed, ResidentJob.Merchant, new[] { ResidentJob.Retired }, 0.840f, false, true, new Color(0.914f, 0.663f, 0.745f), new Color(0.957f, 0.914f, 0.835f)),
            // 10-02 ワッカム（六本腕で時計を作る惑星型の職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1002", AlienLineage.Mixed, ResidentJob.Mechanic, NoJobs, 0.921f, false, true, new Color(0.209f, 0.714f, 0.733f), new Color(0.843f, 0.686f, 0.346f)),
            // 10-03 ハネリィ（空中を漂う小さな橋の見守り役）— 開発用の仮称。画面には出さない
            new ResidentDesign("1003", AlienLineage.Mixed, ResidentJob.Builder, new[] { ResidentJob.Courier }, 0.700f, true, true, new Color(0.651f, 0.847f, 0.772f), new Color(0.663f, 0.902f, 0.847f)),
            // 10-04 ハナユゲ（香りで表情が変わる不思議な調香師）— 開発用の仮称。画面には出さない
            new ResidentDesign("1004", AlienLineage.Mixed, ResidentJob.Florist, new[] { ResidentJob.Artist }, 0.690f, false, true, new Color(0.710f, 0.631f, 0.839f), new Color(0.902f, 0.616f, 0.721f)),
            // 10-05 ツノテト（四本腕の小さな建築職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1005", AlienLineage.Mixed, ResidentJob.Builder, NoJobs, 0.810f, false, true, new Color(0.725f, 0.541f, 0.346f), new Color(0.851f, 0.749f, 0.568f)),
            // 10-06 ネムツキ（月の耳を持つ夜の採音家）— 開発用の仮称。画面には出さない
            new ResidentDesign("1006", AlienLineage.Mixed, ResidentJob.Musician, new[] { ResidentJob.Scholar }, 0.780f, true, true, new Color(0.123f, 0.168f, 0.372f), new Color(0.957f, 0.914f, 0.835f)),
            // 10-07 ピタルテ（六本の吸着腕を持つ舞台職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1007", AlienLineage.Mixed, ResidentJob.Carpenter, new[] { ResidentJob.Builder }, 0.750f, false, true, new Color(0.326f, 0.725f, 0.655f), new Color(0.945f, 0.887f, 0.765f)),
            // 10-08 キラトビ（星の尻尾を持つ屋根飾りの絵描き）— 開発用の仮称。画面には出さない
            new ResidentDesign("1008", AlienLineage.Mixed, ResidentJob.Artist, NoJobs, 0.830f, false, true, new Color(0.898f, 0.495f, 0.471f), new Color(0.721f, 0.898f, 0.898f)),
            // 10-09 ポケワタ（お腹が大きなポケットになったおもちゃ職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1009", AlienLineage.Mixed, ResidentJob.Carpenter, new[] { ResidentJob.Child }, 0.744f, false, true, new Color(0.679f, 0.459f, 0.306f), new Color(0.957f, 0.914f, 0.835f)),
            // 10-10 カガリカ（光る鏡板を持つ四本腕の鏡職人）— 開発用の仮称。画面には出さない
            new ResidentDesign("1010", AlienLineage.Mixed, ResidentJob.Blacksmith, new[] { ResidentJob.Artist }, 0.860f, false, true, new Color(0.835f, 0.890f, 0.910f), new Color(0.718f, 0.659f, 0.816f)),
        };

        private static Dictionary<string, ResidentDesign> byId;

        public static ResidentDesign Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, ResidentDesign>();
                foreach (var d in All) byId[d.Id] = d;
            }
            return byId.TryGetValue(id, out var x) ? x : null;
        }
    }
}
