using UnityEngine;

namespace Uchujin.World
{
    /// <summary>
    /// ワールド生成・表示の調整用【仮数値】をまとめたもの。
    /// 村仕様 §19「マップの規模」「住宅数」「建物の密度」は未決定のため、ここで一括調整できるようにしている。
    /// </summary>
    public static class WorldTuning
    {
        // ---- 生成バージョン（村仕様 §1.3：生成済みの星の地形を勝手に変えない） ----
        /// <summary>このコードが生成する地形の版。PlanetInfo.worldGenVersion と比較する。</summary>
        /// <remarks>1：最初の版（湖なし）。2：マップ設計 v0.2 の設計決定（湖・静かな高台・8m以上の滝と滝の裏の洞・必ず作る桟橋・川の区間・種ごとの検証）。
        /// 3：マップ設計 v0.3 の試作（星の骨格、村の端のゲート、別マップの T1 花の野道・R2 湖の森）。村の地形は版2の作り方のまま。
        /// 版1・版2で作った星はその版のまま作り直す（地形を勝手に変えない）。</remarks>
        public const int GeneratorVersion = 3;

        // ---- 生成バージョン3（マップ設計 v0.3。地域マップ＋道中マップの試作）【仮数値】 ----
        /// <summary>地域・道中マップを置く座標（1枚目）と、マップどうしの間隔（屋外・鉱山・家・滝の裏の洞から離す）。</summary>
        public static readonly Vector3 FieldMapOriginBase = new Vector3(-4000f, 0f, 3200f);
        public const float FieldMapSpacing = 800f;
        /// <summary>1枚のマップの作り直しの上限。</summary>
        public const int FieldMapMaxAttempts = 6;
        /// <summary>道中・地域マップの本道と脇道の幅の半分（m）。</summary>
        public const float FieldRoadHalfWidth = 1.9f;
        public const float FieldTrailHalfWidth = 0.9f;
        /// <summary>ゲートを遊べる範囲の端から何m内側に置くか、となりのマップから来た時にゲートから何m先に立つか。</summary>
        public const float FieldGateInset = 4f;
        public const float FieldGateArrival = 7f;
        /// <summary>ゲートに入ったと判定する範囲（横の半分・奥行きの半分。m）。</summary>
        public const float FieldGateHalfWidth = 4.5f;
        public const float FieldGateHalfDepth = 3f;
        /// <summary>ゲートの地面の高さと骨格の高さの差の許容（m。§10.5 は 0.2m）。</summary>
        public const float FieldGateHeightTolerance = 0.25f;
        /// <summary>歩いて行けるかの検証：隣のグリッド点（2m）との高さの差の上限（約40°）。</summary>
        public const float FieldWalkMaxStep = 1.7f;
        /// <summary>走る速さ（m/秒。PlayerController.runSpeed と同じ）。</summary>
        public const float RunSpeed = 8f;
        /// <summary>道中の本道を走り抜ける時間の上限（秒。§3.3 は 30〜35秒、§10.5 は 40秒以内）。</summary>
        public const float FieldRouteMaxSeconds = 38f;
        /// <summary>地域マップのゲートから主な場所（湖の岬など）までの上限（秒）。</summary>
        public const float FieldRegionMaxSeconds = 26f;
        /// <summary>村のゲートを置く位置（村の中心から、端の手前までの距離。見えない壁の内側）。</summary>
        public const float VillageGateInset = 11f;
        /// <summary>広場から村のゲートまでの道のりの上限（m）。</summary>
        public const float MaxPathPlazaToGate = 300f;

        // ---- 生成バージョン2（マップ設計 v0.2 §2）【仮数値】 ----
        /// <summary>必ず作る滝の最低の落差（m）。</summary>
        public const float WaterfallMinDrop = 8f;
        /// <summary>滝つぼを通常の川底より深く掘る量（m）。川の深さ 1.6m と合わせて 3m 以上。</summary>
        public const float PlungePoolExtraDepth = 2.2f;
        /// <summary>湖の差し渡しの最低（m）と最深部の最低（m）。</summary>
        public const float LakeMinDiameter = 90f;
        public const float LakeMinMaxDepth = 8f;
        /// <summary>静かな高台から本道・家・見晴らし台までの最低距離（m、高台の中心から）。</summary>
        public const float QuietRoadClearance = 75f;
        public const float QuietHouseClearance = 80f;
        /// <summary>静かな岸（湖・池）から本道・家・見晴らし台までの最低距離（m）。</summary>
        public const float QuietShoreClearance = 40f;
        /// <summary>鍛冶屋・修理屋が広場から離れて建つ時の、道のりの上限（m。走って25秒）。</summary>
        public const float SatelliteMaxPath = 200f;
        /// <summary>桟橋の先端の位置（海岸線から沖へ何m）。</summary>
        public const float PierReachV2 = 22f;
        /// <summary>滝の裏の洞（別エリア）を作る座標（屋外・鉱山から離れた場所）。</summary>
        public static readonly Vector3 WaterfallCaveOrigin = new Vector3(-6000f, 0f, 6000f);
        /// <summary>広場から地域の入口（鉱山など）までの道のりの上限（m。走って45秒：マップ設計 §5.5）。</summary>
        public const float MaxPathRegionV2 = 360f;
        /// <summary>住宅区画を置ける広場からの距離（湖・静かな高台を避ける分、少し広げる）。</summary>
        public const float HousePlotMaxDistFromPlazaV2 = 195f;
        /// <summary>道沿いの住宅区画の間隔（生成バージョン2。湖・静かな高台を避ける分、少し詰める）。</summary>
        public const float HousePlotSpacingV2 = 12.6f;

        // ---- 全体の大きさ（村仕様 §2.1：一つの村とその周辺の密度の高い生活圏） ----
        public const float WorldSize = 400f;
        public const float Half = WorldSize * 0.5f;
        /// <summary>高さグリッドの間隔（m）。</summary>
        public const float GridStep = 2f;
        /// <summary>地形チャンクの1辺のセル数（GridStep 単位）。</summary>
        public const int ChunkCells = 25;
        /// <summary>周囲の見た目だけの地形（コライダー無し）の幅。</summary>
        public const float OuterRingWidth = 260f;
        public const float OuterRingStep = 10f;
        /// <summary>見えない壁の位置（中心からの距離）。</summary>
        public const float WallInset = 3f;

        // ---- 地形の大まかな形 ----
        public const float SeaLevel = 0f;
        public const float CoastDistMin = 118f;
        public const float CoastDistMax = 150f;
        public const float MountainDistMin = 128f;
        public const float MountainDistMax = 152f;
        public const float MountainRadiusMin = 68f;
        public const float MountainRadiusMax = 88f;
        public const float MountainPeakMin = 34f;
        public const float MountainPeakMax = 50f;
        public const float HillRadiusMin = 26f;
        public const float HillRadiusMax = 34f;
        public const float HillHeightMin = 9f;
        public const float HillHeightMax = 14f;

        // ---- 水辺 ----
        public const int PondCountMin = 2;
        public const int PondCountMax = 4;
        public const float PondRadiusMin = 6f;
        public const float PondRadiusMax = 12f;
        public const float RiverRadiusStart = 2.4f;
        public const float RiverRadiusEnd = 4.8f;
        public const float RiverDepth = 1.6f;
        /// <summary>2本目の川（池へ注ぐ小川）が生まれる確率。</summary>
        public const float SecondRiverChance = 0.55f;

        // ---- 村 ----
        public const float PlazaRadius = 14f;
        public const float FacilityRingRadius = 27f;
        /// <summary>鍛冶屋・修理屋が広場から離れた街道沿いに建つ確率（ロケット仕様 §15.2：修理屋が鉱山に近い星もある）。</summary>
        public const float SatelliteFacilityChance = 0.4f;
        public const int HousePlotTarget = 24;
        public const int HousePlotMinimum = 20;
        /// <summary>生成バージョン2の住宅区画の最低数と目標（同時居住の上限15人。引っ越しの入れ替わり用に少し空き家を残す）。</summary>
        public const int HousePlotMinimumV2 = 15;
        public const int HousePlotTargetV2 = 17;
        public const float HousePlotSpacing = 14f;
        public const float HousePlotRoadOffset = 11f;
        public const float HousePlotMaxDistFromPlaza = 175f;

        // ---- 主人公の拠点 ----
        public const float FarmWidth = 24f;   // X
        public const float FarmDepth = 18f;   // Z
        /// <summary>農場の拡張用に確保する周囲の余地（m）。</summary>
        public const float FarmExpansionMargin = 12f;
        public const float HomeDistMin = 55f;
        public const float HomeDistMax = 115f;
        public const float CrashDistMin = 100f;
        public const float CrashDistMax = 165f;
        public const float CrashCraterRadius = 11f;

        // ---- 道路 ----
        public const float RoadHalfWidth = 1.8f;
        public const float RoadBlend = 2.2f;
        public const float PathCell = 4f;
        public const float BridgeWidth = 3.4f;

        // ---- 検証（村仕様 §16.1） ----
        public const float MaxPathHomeToPlaza = 190f;
        public const float MaxPathPlazaToMine = 290f;
        public const float MaxPathPlazaToCrash = 260f;
        public const float MaxPathPlazaToFishingHut = 180f;
        public const int MinFishingSpots = 20;
        public const int MinHabitatPoints = 200;
        public const int MaxGenerationAttempts = 6;

        // ---- 自然 ----
        public const int TreeAttempts = 2600;
        public const int RockAttempts = 520;
        public const int FlowerAttempts = 1300;
        public const int GrassAttempts = 1700;

        // ---- 生活による変化（村仕様 §11） ----
        /// <summary>踏み跡グリッドの1セル（m）。</summary>
        public const float WearCell = 4f;
        /// <summary>プレイヤーが1回の観測で与える踏み跡。</summary>
        public const int WearPerPlayerSample = 2;
        public const int WearPerResidentSample = 1;
        public const float WearSampleInterval = 1.0f;
        /// <summary>日付更新ごとに草が戻る量。</summary>
        public const int WearDailyRecovery = 3;
        /// <summary>この値以上で道らしく見え始める。</summary>
        public const int WearVisibleThreshold = 24;
        public const int WearFullThreshold = 200;

        /// <summary>公共施設の整備に毎日進む量（住民自身が進める分）。</summary>
        public const int ProjectDailyProgress = 3;

        // ---- 地図（村仕様 §13） ----
        public const int MapTextureSize = 512;
        public const int ExploreGrid = 100;
        public const float ExploreRevealRadius = 26f;
        public const float SignReadDistance = 6f;

        // ---- 夜の灯り ----
        public const int LampOnHour = 18;
        public const int LampOffHour = 6;

        public static Vector2 WorldMin => new Vector2(-Half, -Half);
    }
}
