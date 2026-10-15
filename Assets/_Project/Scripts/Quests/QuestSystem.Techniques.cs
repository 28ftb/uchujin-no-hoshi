using System.Collections.Generic;
using Uchujin.Core;

namespace Uchujin.Quests
{
    /// <summary>
    /// クエストで教わる釣りの技 → 釣りモジュールの技（IFishingService.TechniqueIds）の対応表。
    /// クエスト側の技は物語の中の「見て覚えたこつ」で、釣り側の7種と1対1ではないため、近い順に候補を並べる。
    /// 候補が釣りモジュールに無ければ次の候補、どれも無ければ釣り側の最初の技（最も基本的なもの）にする。
    /// </summary>
    internal static class QuestFishingTechniques
    {
        /// <summary>釣り名人の物語（§9.3）の段階3「技を教わる」：名人の竿さばきを見て覚えるこつ。</summary>
        public const string MasterRodKnack = "fishing_master.rod_knack";

        /// <summary>
        /// 対応表（クエスト側の技, 釣り側の技の候補…）。
        /// 名人の竿さばき（竿を倒す・立てる動き）→「魚の誘導 Guide」（竿の角度の効き目が上がる）が最も近い。
        /// 次点は糸の扱いの「糸さばき LineHandling」、その次が名人ならではの「名人の極意 MasterSecret」。
        /// </summary>
        private static readonly string[][] Table =
        {
            new[] { MasterRodKnack, "Guide", "LineHandling", "MasterSecret" },
        };

        /// <summary>クエスト側の技に対応する釣り側の技 ID（釣りモジュールが無い・技が無ければ null）。</summary>
        public static string Resolve(IFishingService fishing, string questTechnique)
        {
            if (fishing == null) return null;
            var available = new List<string>();
            var ids = fishing.TechniqueIds;
            if (ids != null) foreach (var id in ids) if (!string.IsNullOrEmpty(id)) available.Add(id);
            if (available.Count == 0) return null;
            foreach (var row in Table)
            {
                if (row.Length == 0 || row[0] != questTechnique) continue;
                for (int i = 1; i < row.Length; i++)
                    foreach (var id in available)
                        if (string.Equals(id, row[i], System.StringComparison.OrdinalIgnoreCase)) return id;
            }
            return available[0];
        }
    }

    // =====================================================================
    // 釣り名人に教わった技を、釣りの性能（IFishingService の技）へ反映する
    // =====================================================================
    public sealed partial class QuestSystem
    {
        /// <summary>釣り名人に技を教わった印（Game.Flags。値 1）。末尾にプレイヤーID。</summary>
        internal const string FishingTechniqueFlagPrefix = "quest.fishing_master.technique.";

        /// <summary>このセッションで、自分のフラグに対応する技を釣りモジュールへ反映したか。</summary>
        private bool localTechniqueSynced;
        private float techniqueCheckTimer;

        /// <summary>
        /// 教わった技を釣りモジュールで覚えさせる。リモートのゲストなら釣りモジュールが本人の端末へ届ける。
        /// 新しく覚えた（または送った）ら true。
        /// </summary>
        internal bool GrantFishingTechnique(string playerId)
        {
            if (string.IsNullOrEmpty(playerId) || Game == null) return false;
            var fishing = Game.Service<IFishingService>();
            string id = QuestFishingTechniques.Resolve(fishing, QuestFishingTechniques.MasterRodKnack);
            if (id == null) return false;
            try { return fishing.GrantTechnique(playerId, id); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogException(e);
                return false;
            }
        }

        /// <summary>
        /// 自分のフラグがあれば、その技を釣りモジュールへ反映する（既存セーブのフラグ・ホストから同期されたフラグも対象）。
        /// 釣り側の付与は覚えていれば何もしないので、何度呼んでもよい。ゲストは同期されたフラグを見て自分の端末で覚える。
        /// </summary>
        private void CheckLocalFishingTechnique(float deltaTime)
        {
            if (localTechniqueSynced || Game == null || Game.Flags == null) return;
            techniqueCheckTimer -= deltaTime;
            if (techniqueCheckTimer > 0f) return;
            techniqueCheckTimer = 2f;
            string me = LocalId;
            if (string.IsNullOrEmpty(me) || !Game.Flags.Has(FishingTechniqueFlagPrefix + me)) return;
            if (Game.Service<IFishingService>() == null) return;
            GrantFishingTechnique(me);
            localTechniqueSynced = true;
        }

        private void ResetTechniqueSync()
        {
            localTechniqueSynced = false;
            techniqueCheckTimer = 0f;
        }
    }
}
