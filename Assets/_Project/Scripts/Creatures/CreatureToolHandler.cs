using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.UI;

namespace Uchujin.Creatures
{
    /// <summary>
    /// 道具の使用（ToolUseRouter）：
    ///  ・網（ToolType.CatchNet）で捕まえる（捕獲は CatchCreatureCommand でホストが確定）
    ///  ・手に持ったケースから生き物を出す
    ///  ・手に持った餌（いきものフード等）を置いて誘う／好物の作物で誘う
    ///  ・ツルハシを使った音で近くのイワネムが目を覚ます（ツルハシ自体の処理は Mining に任せるので false を返す）
    /// </summary>
    public sealed class CreatureToolHandler : IToolHandler
    {
        // 【仮数値】網の届く距離（Lvごとに伸びる）
        public const float NetBaseReach = 2.3f;
        public const float NetReachPerLevel = 0.35f;

        private readonly CreatureSystem sys;

        public CreatureToolHandler(CreatureSystem sys) { this.sys = sys; }

        public int Priority => 500;

        public bool TryUseTool(ToolUseContext ctx)
        {
            var p = ctx.Player;
            if (p == null || !p.IsLocal) return false;
            switch (ctx.Tool)
            {
                case ToolType.CatchNet:
                    return SwingNet(ctx);
                case ToolType.Pickaxe:
                    WakeRockSleepers(ctx.Origin, 6f);
                    return false;
                case ToolType.Hand:
                    {
                        var item = ctx.SelectedItem;
                        if (item == null || item.IsEmpty) return false;
                        if (CreatureDatabase.IsCaseItem(item.itemId)) return sys.ReleaseFromCase(ctx.SelectedSlot);
                        var def = item.Definition;
                        if (def.Category == ItemCategory.CreatureFood) return sys.PlaceBait(ctx.SelectedSlot, ctx.GroundPoint);
                        // 作物は、好物にしている野生の生き物が近くにいる時だけ餌として置く（農場の作業と取り合わない）
                        if ((def.Category == ItemCategory.Crop || def.HasTag(ItemTags.Fruit)) && !InFarm(ctx.GroundPoint) && AnyWildLikes(def, ctx.Origin))
                            return sys.PlaceBait(ctx.SelectedSlot, ctx.GroundPoint);
                        return false;
                    }
            }
            return false;
        }

        private bool InFarm(Vector3 p)
        {
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            return layout != null && layout.FarmRect.width > 0 && layout.FarmRect.Contains(new Vector2(p.x, p.z));
        }

        private bool AnyWildLikes(ItemDefinition def, Vector3 origin)
        {
            foreach (var v in sys.WildViews)
            {
                if (v == null) continue;
                if ((v.transform.position - origin).sqrMagnitude > CreatureSystem.BaitRadius * CreatureSystem.BaitRadius) continue;
                if (sys.LikesBait(v.Species, def)) return true;
            }
            return false;
        }

        private void WakeRockSleepers(Vector3 origin, float radius)
        {
            foreach (var v in sys.WildViews)
                if (v != null && v.Species.No == 43 && (v.transform.position - origin).sqrMagnitude < radius * radius) v.Wake();
        }

        private bool SwingNet(ToolUseContext ctx)
        {
            var p = ctx.Player;
            int netLevel = Mathf.Max(1, ctx.ToolLevel);
            float reach = NetBaseReach + NetReachPerLevel * (netLevel - 1) + (sys.HasTechnique(CreatureSystem.TechNet) ? 0.3f : 0f);
            var origin = p.Position;
            var target = sys.FindWildInFront(origin, ctx.Forward, reach);
            // 網で近くの岩を叩いてもイワネムは起きる（狙った個体は下の HandleNetBeforeCatch で起きる＝この一振りでは捕まらない）
            var tapAt = origin + ctx.Forward * 1.5f;
            foreach (var v in sys.WildViews)
                if (v != null && v != target && v.Species.No == 43 && (v.transform.position - tapAt).sqrMagnitude < 2.2f * 2.2f) v.Wake();
            CreatureFx.Puff(origin + ctx.Forward * 1.4f + Vector3.up * 0.9f, new Color(1f, 1f, 1f, 0.6f), 3, 0.5f, 0.05f);
            if (target == null) return true; // 空振り（網は振る）
            if (target.HandleNetBeforeCatch()) return true;
            float window = target.CatchWindow();
            if (window < 0f)
            {
                UIManager.Toast(Loc.T("網が届かなかった…", "The net missed..."));
                return true;
            }
            // 網さばき：タイミングの幅が広がる
            if (sys.HasTechnique(CreatureSystem.TechNet)) window = Mathf.Min(1f, window + 0.15f);
            string wildId = target.Record.wildId;
            var cmd = new CatchCreatureCommand
            {
                wildId = wildId,
                speciesId = target.Species.Id,
                window = window,
                netLevel = netLevel,
                catchLevel = sys.CatchLevel,
                netTechnique = sys.HasTechnique(CreatureSystem.TechNet)
            };
            var view = target;
            WorldAuthority.Submit(cmd, r =>
            {
                if (r != null && r.Success)
                {
                    sys.RemoveWildView(wildId);
                }
                else if (view != null)
                {
                    view.OnNetMiss();
                }
            });
            return true;
        }
    }
}
