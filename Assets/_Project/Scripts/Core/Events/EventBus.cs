using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// システム間の疎結合な通知に使う型付きイベントバス。
    /// 例：釣りで魚を釣った → FishCaughtEvent → クエスト・住民の記憶・タブレットの記録が反応する。
    /// 購読側は OnPlayStarted などで Subscribe し、Teardown で Unsubscribe すること。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            handlers.TryGetValue(t, out var existing);
            handlers[t] = Delegate.Combine(existing, handler);
        }

        public static void Unsubscribe<T>(Action<T> handler)
        {
            var t = typeof(T);
            if (!handlers.TryGetValue(t, out var existing)) return;
            var result = Delegate.Remove(existing, handler);
            if (result == null) handlers.Remove(t);
            else handlers[t] = result;
        }

        public static void Publish<T>(T evt)
        {
            if (!handlers.TryGetValue(typeof(T), out var d)) return;
            var list = d.GetInvocationList();
            for (int i = 0; i < list.Length; i++)
            {
                try
                {
                    ((Action<T>)list[i]).Invoke(evt);
                }
                catch (Exception e)
                {
                    // 1つの購読者の例外で他システムの処理が止まらないようにする
                    Debug.LogException(e);
                }
            }
        }

        public static void ClearAll() => handlers.Clear();
    }
}
