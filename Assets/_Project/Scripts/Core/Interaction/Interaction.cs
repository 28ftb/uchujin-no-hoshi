using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// 調べる・話す・使う対象（[E]キー）。コライダーを持つ GameObject の MonoBehaviour に実装する。
    /// </summary>
    public interface IInteractable
    {
        /// <summary>画面に出す操作名（例：「話す」「棚を見る」「ベッドで眠る」）。null なら表示しない。</summary>
        string GetInteractionPrompt(PlayerState player);
        bool CanInteract(PlayerState player);
        void Interact(PlayerState player);
    }

    /// <summary>Action で手早く作れる汎用インタラクト。</summary>
    public class SimpleInteractable : MonoBehaviour, IInteractable
    {
        public string promptJa = "調べる";
        public string promptEn = "Inspect";
        public Action<PlayerState> onInteract;
        public Func<PlayerState, bool> canInteract;

        public string GetInteractionPrompt(PlayerState player) => Loc.T(promptJa, promptEn);
        public bool CanInteract(PlayerState player) => canInteract == null || canInteract(player);
        public void Interact(PlayerState player) => onInteract?.Invoke(player);

        public static SimpleInteractable Attach(GameObject go, string ja, string en, Action<PlayerState> action)
        {
            var si = go.GetComponent<SimpleInteractable>();
            if (si == null) si = go.AddComponent<SimpleInteractable>();
            si.promptJa = ja;
            si.promptEn = en;
            si.onInteract = action;
            if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            return si;
        }
    }

    /// <summary>道具（または選択中の持ち物）を使った時の情報。</summary>
    public sealed class ToolUseContext
    {
        public PlayerState Player;
        public ToolType Tool;
        public int ToolLevel;
        /// <summary>選択中の持ち物（種を植える、家具を置く、餌を置く等）。</summary>
        public ItemStack SelectedItem;
        public int SelectedSlot;
        public Vector3 Origin;
        public Vector3 Forward;
        /// <summary>足元前方の地面の点（農作業・設置用）。</summary>
        public Vector3 GroundPoint;
        /// <summary>前方レイキャストで当たったコライダー（無ければ null）。</summary>
        public Collider HitCollider;
        public Vector3 HitPoint;
        public string AreaId;
    }

    /// <summary>
    /// 道具の使用を処理するハンドラ。農業（クワ・じょうろ・種）、鉱山（ツルハシ）、釣り（竿）、生き物（網）、
    /// 家具（設置）などのモジュールが ToolUseRouter に登録する。最初に true を返したものが処理する。
    /// </summary>
    public interface IToolHandler
    {
        int Priority { get; }
        bool TryUseTool(ToolUseContext ctx);
    }

    public static class ToolUseRouter
    {
        private static readonly List<IToolHandler> handlers = new List<IToolHandler>();

        public static void Register(IToolHandler handler)
        {
            if (handler == null || handlers.Contains(handler)) return;
            handlers.Add(handler);
            handlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        }

        public static void Unregister(IToolHandler handler) => handlers.Remove(handler);

        public static bool Use(ToolUseContext ctx)
        {
            for (int i = 0; i < handlers.Count; i++)
            {
                try
                {
                    if (handlers[i].TryUseTool(ctx)) return true;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            return false;
        }

        public static void Clear() => handlers.Clear();
    }

    /// <summary>道具を使う対象側（岩・作物など）に付けるとレイキャスト命中時に直接呼ばれる。</summary>
    public interface IToolTarget
    {
        bool OnToolUsed(ToolUseContext ctx);
    }

    /// <summary>地面に置かれたアイテム（拾える）。</summary>
    public class DroppedItem : MonoBehaviour, IInteractable
    {
        public ItemStack stack;

        public static DroppedItem Spawn(ItemStack stack, Vector3 position)
        {
            var def = stack.Definition;
            var go = Visual.PrimitiveBuilder.Create(def.Shape, null, position + Vector3.up * 0.2f, Vector3.one * 0.35f, def.Color, "Dropped_" + stack.itemId);
            var di = go.AddComponent<DroppedItem>();
            di.stack = stack;
            if (Game.Instance != null) go.transform.SetParent(Game.Instance.SceneRoot, true);
            return di;
        }

        public string GetInteractionPrompt(PlayerState player) => Loc.Format("拾う：{0} ×{1}", "Pick up: {0} x{1}", stack.DisplayName, stack.count);
        public bool CanInteract(PlayerState player) => stack != null && !stack.IsEmpty;

        public void Interact(PlayerState player)
        {
            int left = player.Inventory.Add(stack, "pickup");
            if (left <= 0) Destroy(gameObject);
            else
            {
                stack.count = left;
                UI.UIManager.Toast(Loc.T("持ち物がいっぱいです", "Inventory is full"));
            }
        }

        private void Update()
        {
            transform.Rotate(0f, 60f * UnityEngine.Time.deltaTime, 0f);
        }
    }

    /// <summary>星ごとのフラグ（進行・イベント既読など）。星のセーブに含まれる。</summary>
    [Serializable]
    public class FlagStore
    {
        public List<StringIntPair> values = new List<StringIntPair>();

        public int Get(string key, int fallback = 0)
        {
            foreach (var p in values) if (p.key == key) return p.value;
            return fallback;
        }

        public bool Has(string key) => Get(key) != 0;

        public void Set(string key, int value = 1)
        {
            foreach (var p in values)
            {
                if (p.key == key)
                {
                    p.value = value;
                    return;
                }
            }
            values.Add(new StringIntPair(key, value));
        }

        public int Increment(string key, int by = 1)
        {
            int v = Get(key) + by;
            Set(key, v);
            return v;
        }

        public void Clear() => values.Clear();
    }

    /// <summary>エンディング後の選択（基本決定 v0.2 §5）。</summary>
    public enum PostEndingChoice
    {
        None,
        ReturnBeforeLaunch, // ロケット前へ戻り、その星で生活を続ける
        NextPlanet          // 次の星へ進む
    }

    /// <summary>睡眠の窓口（ソロは即時、マルチはネットワークの合意処理へ）。</summary>
    public static class SleepHelper
    {
        public const int DefaultWakeHour = 6;

        public static void RequestSleep(PlayerState player, int wakeHour = DefaultWakeHour)
        {
            if (Game.Instance == null || player == null) return;
            if (WorldAuthority.IsMultiplayer)
            {
                var net = Game.Instance.Service<INetworkService>();
                if (net != null)
                {
                    net.RequestSleep(player, wakeHour);
                    return;
                }
            }
            Game.Instance.StartCoroutine(Game.Instance.SleepRoutine(player, wakeHour));
        }
    }
}
