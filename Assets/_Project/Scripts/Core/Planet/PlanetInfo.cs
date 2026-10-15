using System;
using System.Collections.Generic;

namespace Uchujin.Core
{
    /// <summary>
    /// 1つの星の識別情報。WorldId（星そのもの）とシード（生成規則）を分ける（マルチ仕様 §3.1）。
    /// 生成アルゴリズムを将来変更しても既存の星の言葉・地形が変わらないよう、生成バージョンを保存する。
    /// </summary>
    [Serializable]
    public class PlanetInfo
    {
        public const int CurrentWorldGenVersion = 1;
        public const int CurrentLanguageGenVersion = 1;

        public string worldId;
        public ulong seed;
        public string seedText;
        public int worldGenVersion = CurrentWorldGenVersion;
        public int languageGenVersion = CurrentLanguageGenVersion;
        /// <summary>主人公が付けた呼び名（UI用）。住民が呼ぶ星の名前は宇宙語で別に生成される。</summary>
        public string playerGivenName;
        public string hostPlayerId;
        public long createdUtcTicks;
        /// <summary>何番目の星か（クリア後「次の星へ進む」で増える）。</summary>
        public int planetIndex = 1;
        /// <summary>前の星の WorldId（次の星へ進んだ場合）。</summary>
        public string previousWorldId;

        public static PlanetInfo CreateNew(string seedText, string hostPlayerId, int planetIndex = 1, string previousWorldId = null)
        {
            var info = new PlanetInfo
            {
                worldId = Ids.New(),
                seedText = string.IsNullOrWhiteSpace(seedText) ? RandomSeedText() : seedText.Trim(),
                hostPlayerId = hostPlayerId,
                createdUtcTicks = DateTime.UtcNow.Ticks,
                planetIndex = planetIndex,
                previousWorldId = previousWorldId
            };
            info.seed = SeedFromText(info.seedText);
            return info;
        }

        public static ulong SeedFromText(string text)
        {
            if (ulong.TryParse(text, out var numeric) && numeric != 0) return numeric;
            return StableHash.Mix(StableHash.Fnv1a64(text ?? string.Empty));
        }

        public static string RandomSeedText()
        {
            var r = new System.Random(Guid.NewGuid().GetHashCode());
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var c = new char[8];
            for (int i = 0; i < c.Length; i++) c[i] = chars[r.Next(chars.Length)];
            return new string(c);
        }
    }

    /// <summary>星の生成・初期化時に各システムへ渡す情報。</summary>
    public sealed class PlanetContext
    {
        public PlanetInfo Planet { get; }
        public bool IsNewPlanet { get; }
        public ulong Seed => Planet.seed;
        /// <summary>ワールドシステムが PreparePlanet で設定する。以降のシステムが参照する。</summary>
        public World.WorldLayout Layout { get; set; }

        /// <summary>新規開始時の設定（ロード・ゲスト参加では null）。</summary>
        public NewGameSettings Settings { get; set; }

        /// <summary>
        /// 持ち物・お金・道具・技能を初期化した新規ゲームか（「次の星へ進む」で引き継いだ場合は false、ロード時も false）。
        /// PlayerProfile スコープのデータ（技能・同行生物など）を初期化するかの判断に使う。
        /// </summary>
        public bool IsFreshProfile => IsNewPlanet && (Settings == null || !Settings.KeepProfile);

        /// <summary>システム間で生成時に受け渡す任意データ。</summary>
        public readonly Dictionary<string, object> Shared = new Dictionary<string, object>();

        public PlanetContext(PlanetInfo planet, bool isNew)
        {
            Planet = planet;
            IsNewPlanet = isNew;
        }

        /// <summary>用途キーごとに独立した決定的乱数。キーは "residents" "mine.floor3" のように用途で分ける。</summary>
        public SeededRandom Rng(string key) => SeededRandom.For(Planet.seed, key);
    }
}
