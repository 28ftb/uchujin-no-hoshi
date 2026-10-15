using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>エリア（屋外・鉱山の各階・家の中など）の登録情報。屋内は屋外から離れた座標に生成する。</summary>
    public class AreaDescriptor
    {
        public string Id;
        public string NameJa;
        public string NameEn;
        public bool IsIndoor;
        public bool IsUnderground;
        /// <summary>入った時の既定の出現位置。</summary>
        public Vector3 DefaultSpawn;
        public float DefaultYaw;
        /// <summary>エリア固有の環境光（屋内・地下）。null なら天候システムの屋外光。</summary>
        public Color? AmbientOverride;
        /// <summary>天候（雨など）の演出を表示するか。</summary>
        public bool ShowWeather = true;
        /// <summary>入った時・出た時の処理。</summary>
        public Action<PlayerState> OnEnter;
        public Action<PlayerState> OnExit;

        public string Name => Loc.T(NameJa, NameEn);
    }

    public struct AreaChangedEvent
    {
        public string PlayerId;
        public string FromAreaId;
        public string ToAreaId;
    }

    /// <summary>エリア移動（フェード付きテレポート）。</summary>
    public sealed class AreaManager
    {
        private readonly Dictionary<string, AreaDescriptor> areas = new Dictionary<string, AreaDescriptor>();

        public void Register(AreaDescriptor area)
        {
            if (area == null || string.IsNullOrEmpty(area.Id)) return;
            areas[area.Id] = area;
        }

        public void Unregister(string id) => areas.Remove(id);

        public AreaDescriptor Get(string id)
        {
            if (id != null && areas.TryGetValue(id, out var a)) return a;
            return null;
        }

        public void Clear() => areas.Clear();

        /// <summary>プレイヤーを別エリアへ移動する。position が null ならエリアの既定位置。</summary>
        public void Transition(PlayerState player, string areaId, Vector3? position = null, float? yaw = null)
        {
            if (player == null) return;
            var to = Get(areaId);
            var from = Get(player.CurrentAreaId);
            from?.OnExit?.Invoke(player);
            var pos = position ?? (to != null ? to.DefaultSpawn : player.Position);
            string prev = player.CurrentAreaId;
            player.CurrentAreaId = areaId;
            player.Position = pos;
            if (player.Avatar != null && (yaw.HasValue || to != null))
                player.Avatar.rotation = Quaternion.Euler(0f, yaw ?? to.DefaultYaw, 0f);
            // 屋内・鉱山は屋外から数千m離れているので、カメラが遠くから飛んでこないよう即座に合わせる
            if (player.IsLocal && CameraRig.Instance != null && CameraRig.Instance.target == player.Avatar) CameraRig.Instance.SnapToTarget();
            to?.OnEnter?.Invoke(player);
            if (player.IsLocal) UI.ScreenFader.FadeIn(0.35f);
            EventBus.Publish(new AreaChangedEvent { PlayerId = player.PlayerId, FromAreaId = prev, ToAreaId = areaId });
        }
    }

    /// <summary>エリアの出入口（触れる or [E]で移動）。</summary>
    public class AreaPortal : MonoBehaviour, IInteractable
    {
        public string targetAreaId;
        public Vector3 targetPosition;
        public float targetYaw;
        public bool useTargetPosition = true;
        public string promptJa = "入る";
        public string promptEn = "Enter";

        public static AreaPortal Create(Transform parent, Vector3 position, Vector3 size, string targetArea, Vector3 targetPos, float targetYaw, string ja, string en, Color color)
        {
            var go = Visual.PrimitiveBuilder.Create(PrimitiveType.Cube, parent, position, size, color, "Portal_" + targetArea);
            var p = go.AddComponent<AreaPortal>();
            p.targetAreaId = targetArea;
            p.targetPosition = targetPos;
            p.targetYaw = targetYaw;
            p.promptJa = ja;
            p.promptEn = en;
            return p;
        }

        public string GetInteractionPrompt(PlayerState player) => Loc.T(promptJa, promptEn);
        public bool CanInteract(PlayerState player) => true;

        public void Interact(PlayerState player)
        {
            if (Game.Instance == null) return;
            if (useTargetPosition) Game.Instance.Areas.Transition(player, targetAreaId, targetPosition, targetYaw);
            else Game.Instance.Areas.Transition(player, targetAreaId);
        }
    }
}
