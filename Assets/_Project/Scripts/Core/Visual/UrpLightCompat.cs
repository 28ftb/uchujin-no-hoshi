#if UCHUJIN_URP
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core.Visual
{
    /// <summary>
    /// 点光源の明るさを URP 用に補正する。
    /// 各モジュールの点光源（家の灯り・鉱山・街灯・キャラメイクの舞台・演出など）は Built-in の距離減衰で明るさを決めているが、
    /// URP は逆二乗の物理的な減衰になるため、そのままでは大幅に暗くなる。
    /// 範囲（range）の約35%の距離で Built-in と同じ明るさになるよう倍率 k = 0.03×range² を掛ける。
    /// 他のコードが intensity を設定し直した時（演出の明滅など）は、その値を新しい基準として補正し直す。
    /// </summary>
    public static class UrpLightCompat
    {
        private const float Factor = 0.03f;          // 【仮数値】
        private const float ScanInterval = 0.5f;     // 新しい点光源を探す間隔（秒）

        private struct Entry { public float scaled; }

        private static readonly Dictionary<Light, Entry> entries = new Dictionary<Light, Entry>();
        private static readonly List<Light> lights = new List<Light>();
        private static readonly List<Light> dead = new List<Light>();
        private static float nextScan;

        public static float Multiplier(float range) => Mathf.Max(1f, Factor * range * range);

        /// <summary>毎フレーム（描画前）に呼ぶ。</summary>
        public static void Update()
        {
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + ScanInterval;
                lights.Clear();
#if UNITY_6000_6_OR_NEWER
                foreach (var l in Object.FindObjectsByType<Light>())
#else
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
#endif
                    if (l.type == LightType.Point || l.type == LightType.Spot) lights.Add(l);
            }

            foreach (var l in lights)
            {
                if (l == null) continue;
                float cur = l.intensity;
                if (entries.TryGetValue(l, out var e) && Mathf.Approximately(cur, e.scaled)) continue;
                // 初めて見た光源か、他のコードが基準の明るさを設定し直した
                float scaled = cur * Multiplier(l.range);
                l.intensity = scaled;
                entries[l] = new Entry { scaled = scaled };
            }

            if (entries.Count > lights.Count + 64)
            {
                dead.Clear();
                foreach (var kv in entries) if (kv.Key == null) dead.Add(kv.Key);
                foreach (var d in dead) entries.Remove(d);
            }
        }
    }
}
#endif
