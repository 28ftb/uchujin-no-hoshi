using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core.UI;

namespace Uchujin.Core
{
    /// <summary>
    /// ローカルプレイヤーの「調べる・話す（E）」「道具を使う（左クリック/F）」「道具切替（R）」「持ち物選択（1〜0）」。
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        public PlayerState Player { get; set; }
        public float interactRadius = 1.4f;
        public float interactReach = 1.3f;
        public float toolReach = 3f;
        public float toolCooldown = 0.25f;

        private IInteractable current;
        private float lastToolTime;
        private readonly Collider[] buffer = new Collider[32];

        public IInteractable CurrentTarget => current;

        private void Update()
        {
            if (Player == null || Game.Instance == null || Game.Instance.State != GameState.Playing) return;
            if (UIManager.BlocksGameplay || Player.InputLocked)
            {
                current = null;
                return;
            }

            current = FindInteractable();
            if (current != null)
            {
                string prompt = current.GetInteractionPrompt(Player);
                if (!string.IsNullOrEmpty(prompt)) UIManager.SetPrompt("[E] " + prompt);
                if (GameInput.Interact && current.CanInteract(Player))
                {
                    current.Interact(Player);
                    return;
                }
            }

            int hot = GameInput.HotbarKey;
            if (hot >= 0)
            {
                Player.SelectedSlot = Player.SelectedSlot == hot ? -1 : hot;
                if (Player.SelectedSlot >= 0) Player.Tools.equipped = ToolType.Hand;
            }

            if (GameInput.CycleTool) CycleTool(GameInput.Run ? -1 : 1);

            if (GameInput.UseTool && UnityEngine.Time.time - lastToolTime > toolCooldown && !IsPointerOverHud())
            {
                lastToolTime = UnityEngine.Time.time;
                UseTool();
            }
        }

        private bool IsPointerOverHud()
        {
            // 下部のツールバー付近のクリックは道具使用にしない
            if (!GameInput.MouseButtonDown(0)) return false;
            var mp = GameInput.MousePosition;
            return mp.y < 110 * UIStyles.Scale || UIManager.PointerOverOverlay(mp);
        }

        public void CycleTool(int dir)
        {
            var order = HudRenderer.ToolOrder;
            int idx = System.Array.IndexOf(order, Player.Tools.equipped);
            for (int i = 1; i <= order.Length; i++)
            {
                int n = ((idx + dir * i) % order.Length + order.Length) % order.Length;
                if (Player.Tools.Has(order[n]))
                {
                    Player.Tools.equipped = order[n];
                    Player.SelectedSlot = -1;
                    UIManager.Toast(ToolTypeUtil.Label(order[n]), 1.2f);
                    return;
                }
            }
        }

        private IInteractable FindInteractable()
        {
            Vector3 center = transform.position + Vector3.up * 0.9f + transform.forward * interactReach;
            int n = Physics.OverlapSphereNonAlloc(center, interactRadius, buffer, ~0, QueryTriggerInteraction.Collide);
            IInteractable best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var col = buffer[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                var it = col.GetComponentInParent<IInteractable>();
                if (it == null) continue;
                if (!(it is MonoBehaviour mb) || !mb.isActiveAndEnabled) continue;
                var p = col.ClosestPoint(center);
                float score = (p - center).sqrMagnitude;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = it;
                }
            }
            return best;
        }

        public void UseTool()
        {
            var ctx = BuildContext();
            if (ctx.HitCollider != null)
            {
                var target = ctx.HitCollider.GetComponentInParent<IToolTarget>();
                if (target != null && target.OnToolUsed(ctx))
                {
                    AnimateSwing();
                    return;
                }
            }
            if (ToolUseRouter.Use(ctx)) AnimateSwing();
        }

        public ToolUseContext BuildContext()
        {
            var origin = transform.position + Vector3.up * 1.0f;
            var fwd = transform.forward;
            var ctx = new ToolUseContext
            {
                Player = Player,
                Tool = Player.SelectedItem != null ? ToolType.Hand : Player.Tools.equipped,
                ToolLevel = Player.Tools.GetLevel(Player.SelectedItem != null ? ToolType.Hand : Player.Tools.equipped),
                SelectedItem = Player.SelectedItem,
                SelectedSlot = Player.SelectedSlot,
                Origin = origin,
                Forward = fwd,
                AreaId = Player.CurrentAreaId
            };
            var groundProbe = transform.position + fwd * 1.3f + Vector3.up * 3f;
            if (Physics.Raycast(groundProbe, Vector3.down, out var gh, 8f, ~0, QueryTriggerInteraction.Ignore))
                ctx.GroundPoint = gh.point;
            else
                ctx.GroundPoint = transform.position + fwd * 1.3f;

            var hits = Physics.RaycastAll(origin, fwd, toolReach, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    ctx.HitCollider = h.collider;
                    ctx.HitPoint = h.point;
                }
            }
            return ctx;
        }

        private void AnimateSwing()
        {
            var tool = transform.Find("Visual/ToolHolder");
            if (tool != null) StartCoroutine(Swing(tool));
        }

        private System.Collections.IEnumerator Swing(Transform t)
        {
            var baseRot = t.localRotation;
            float time = 0f;
            while (time < 0.25f)
            {
                time += UnityEngine.Time.deltaTime;
                t.localRotation = baseRot * Quaternion.Euler(Mathf.Sin(time / 0.25f * Mathf.PI) * 70f, 0, 0);
                yield return null;
            }
            t.localRotation = baseRot;
        }
    }
}
