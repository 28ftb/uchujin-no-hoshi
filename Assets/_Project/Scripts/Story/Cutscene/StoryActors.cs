using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Story
{
    /// <summary>
    /// 演出中の住民・主人公の動かし方。
    /// 住民は IResidentDirectory.RequestGoTo で向かわせ、制限時間内に着かなければ位置を直接合わせる（演出の確実さのため）。
    /// 住民の Transform を直接動かすのは演出時の補助に限る（Residents 側が位置を持っている場合は戻される可能性あり：README 参照）。
    /// </summary>
    public static class StoryActors
    {
        public static IResidentDirectory Directory => Game.Instance != null ? Game.Instance.Service<IResidentDirectory>() : null;

        public static Vector3 Ground(Vector3 p)
        {
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            return layout != null ? layout.OnGround(p) : p;
        }

        /// <summary>
        /// RequestGoTo の期限（その日の分）。日をまたぐ場合は翌日の時刻として渡す（RequestGoTo は「今以前の時刻」を翌日扱いにする）。
        /// 23:59 で丸めると、23:59 ちょうどに呼んだ時に丸1日その場に留まってしまうため丸めない。
        /// </summary>
        public static int UntilMinute(int addMinutes)
        {
            var clock = Game.Instance != null ? Game.Instance.Time : null;
            int now = clock != null ? clock.MinuteOfDay : 8 * 60;
            int add = Mathf.Clamp(addMinutes, 1, TimeSystem.MinutesPerDay - 1);
            return (now + add) % TimeSystem.MinutesPerDay;
        }

        public static IResidentInfo Get(string id)
        {
            var dir = Directory;
            if (dir == null || string.IsNullOrEmpty(id)) return null;
            try { return dir.Get(id); }
            catch (System.Exception) { return null; }
        }

        public static void GoTo(string residentId, Vector3 target, int minutes = 120, string tag = "story")
        {
            var dir = Directory;
            if (dir == null || string.IsNullOrEmpty(residentId)) return;
            try { dir.RequestGoTo(residentId, Ground(target), UntilMinute(minutes), tag); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        /// <summary>位置を直接合わせる（演出の補助）。</summary>
        public static void Warp(string residentId, Vector3 position)
        {
            var r = Get(residentId);
            if (r == null) return;
            var pos = Ground(position);
            // 住民モジュールの Warp（AIの内部位置・経路・ゲストの補間先まで同期する契約）を優先する
            var dir = Directory;
            if (dir != null)
            {
                try
                {
                    float yaw = r.Transform != null ? r.Transform.eulerAngles.y : 0f;
                    dir.Warp(residentId, pos, yaw);
                    return;
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            if (r.Transform == null) return;
            var t = r.Transform;
            var cc = t.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            t.position = pos;
            if (cc != null) cc.enabled = true;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null) rb.position = pos;
            Physics.SyncTransforms();
        }

        public static void Face(string residentId, Vector3 lookAt)
        {
            var r = Get(residentId);
            if (r == null || r.Transform == null) return;
            var d = lookAt - r.Transform.position;
            d.y = 0;
            if (d.sqrMagnitude > 0.001f) r.Transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        public static float Distance(string residentId, Vector3 p)
        {
            var r = Get(residentId);
            if (r == null) return float.MaxValue;
            var d = r.Position - p;
            d.y = 0;
            return d.magnitude;
        }

        /// <summary>住民を向かわせて、着くまで待つ（間に合わなければ位置を合わせる）。</summary>
        public static IEnumerator MoveResident(CutsceneDirector director, string residentId, Vector3 target, float arrive = 1.6f, float timeout = -1f, bool warpOnTimeout = true)
        {
            if (string.IsNullOrEmpty(residentId) || Get(residentId) == null) yield break;
            if (timeout < 0f) timeout = StoryTuning.ActorArriveTimeout;
            GoTo(residentId, target);
            float t = 0f;
            while (t < timeout)
            {
                if (director != null && director.SkipRequested) yield break;
                if (Distance(residentId, target) <= arrive) yield break;
                t += Time.deltaTime;
                yield return null;
            }
            if (warpOnTimeout) Warp(residentId, target);
        }

        /// <summary>複数の住民を同時に向かわせて、全員が着く（か時間切れ）まで待つ。</summary>
        public static IEnumerator MoveResidents(CutsceneDirector director, List<KeyValuePair<string, Vector3>> moves, float arrive = 1.8f, float timeout = -1f)
        {
            if (moves == null || moves.Count == 0) yield break;
            if (timeout < 0f) timeout = StoryTuning.ActorArriveTimeout + 3f;
            foreach (var m in moves) GoTo(m.Key, m.Value);
            float t = 0f;
            while (t < timeout)
            {
                if (director != null && director.SkipRequested) yield break;
                bool all = true;
                foreach (var m in moves)
                {
                    if (Distance(m.Key, m.Value) > arrive)
                    {
                        all = false;
                        break;
                    }
                }
                if (all) yield break;
                t += Time.deltaTime;
                yield return null;
            }
            foreach (var m in moves)
                if (Distance(m.Key, m.Value) > arrive) Warp(m.Key, m.Value);
        }

        /// <summary>話させる（吹き出し・記録は Language/Residents 側）。演出の字幕にも出す。</summary>
        public static UtteranceRecord Say(CutsceneDirector director, string residentId, Utterance u, SpeechChannel channel = SpeechChannel.Direct, float seconds = 4.5f)
        {
            var dir = Directory;
            if (dir == null || u == null || string.IsNullOrEmpty(residentId)) return null;
            UtteranceRecord rec = null;
            try { rec = dir.Speak(residentId, u, channel); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (rec != null && director != null)
            {
                var r = Get(residentId);
                director.ShowSpeech(rec, r != null ? r.MainColor : Color.white, seconds);
            }
            return rec;
        }

        // ------------------------------------------------------------
        // 主人公
        // ------------------------------------------------------------

        /// <summary>主人公を自動で歩かせる（演出・プロローグの「ついていく」）。</summary>
        public static IEnumerator WalkPlayer(CutsceneDirector director, Vector3 target, float speed = 3.6f, float arrive = 1.2f, float timeout = 20f)
        {
            var game = Game.Instance;
            var p = game != null ? game.LocalPlayer : null;
            if (p == null || p.Avatar == null) yield break;
            var cc = p.Avatar.GetComponent<CharacterController>();
            float t = 0f;
            while (t < timeout)
            {
                if (director != null && director.SkipRequested) yield break;
                if (p.Avatar == null) yield break;
                var pos = p.Avatar.position;
                var d = target - pos;
                d.y = 0;
                if (d.magnitude <= arrive) yield break;
                var dir = d.normalized;
                p.Avatar.rotation = Quaternion.RotateTowards(p.Avatar.rotation, Quaternion.LookRotation(dir, Vector3.up), 540f * Time.deltaTime);
                var step = dir * speed * Time.deltaTime;
                if (cc != null && cc.enabled) cc.Move(step + Vector3.down * 2f * Time.deltaTime);
                else p.Avatar.position = Ground(pos + step);
                t += Time.deltaTime;
                yield return null;
            }
            // 引っかかった場合は位置を合わせる
            if (p.Avatar != null)
            {
                var d = target - p.Avatar.position;
                d.y = 0;
                if (d.magnitude > arrive * 3f) p.Position = Ground(target) + Vector3.up * 0.1f;
            }
        }

        public static void FacePlayer(Vector3 lookAt)
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            if (p == null || p.Avatar == null) return;
            var d = lookAt - p.Avatar.position;
            d.y = 0;
            if (d.sqrMagnitude > 0.001f) p.Avatar.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        public static void SetPlayerVisible(bool visible)
        {
            var p = Game.Instance != null ? Game.Instance.LocalPlayer : null;
            if (p == null || p.Avatar == null) return;
            var v = p.Avatar.Find("Visual");
            if (v != null) v.gameObject.SetActive(visible);
        }

        public static void DoPlayerGesture(PlayerGesture g)
        {
            var ctrl = Game.Instance != null && Game.Instance.PlayerSys != null ? Game.Instance.PlayerSys.LocalController : null;
            if (ctrl != null) ctrl.PlayGesture(g);
        }
    }
}
