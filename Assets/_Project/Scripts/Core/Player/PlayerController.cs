using UnityEngine;
using Uchujin.Core.UI;

namespace Uchujin.Core
{
    /// <summary>
    /// ローカルプレイヤーの3D移動（CharacterController）。スタミナは無いので走っても疲れない（基本決定 v0.2）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public float walkSpeed = 4.5f;
        public float runSpeed = 8f;
        public float jumpSpeed = 5.5f;
        public float gravity = 18f;
        public float turnSpeed = 720f;

        public PlayerState Player { get; set; }
        public bool IsMoving { get; private set; }
        public float CurrentSpeed { get; private set; }

        private CharacterController cc;
        private float verticalVelocity;

        private void Awake()
        {
            cc = GetComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0, 0.85f, 0);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.45f;
        }

        private void Update()
        {
            if (Player == null || Game.Instance == null) return;
            bool blocked = Game.Instance.State != GameState.Playing || UIManager.BlocksGameplay || Player.InputLocked;
            Vector2 input = blocked ? Vector2.zero : GameInput.Move;
            var cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized : Vector3.forward;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 move = fwd * input.y + right * input.x;
            move = LimitWading(move);
            float speed = GameInput.Run ? runSpeed : walkSpeed;
            IsMoving = move.sqrMagnitude > 0.01f;
            CurrentSpeed = IsMoving ? speed : 0f;

            if (IsMoving)
            {
                var targetRot = Quaternion.LookRotation(move, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeed * UnityEngine.Time.deltaTime);
            }

            if (cc.isGrounded)
            {
                verticalVelocity = -1f;
                if (!blocked && GameInput.Jump) verticalVelocity = jumpSpeed;
            }
            else
            {
                verticalVelocity -= gravity * UnityEngine.Time.deltaTime;
            }

            var velocity = move * speed + Vector3.up * verticalVelocity;
            if (cc.enabled) cc.Move(velocity * UnityEngine.Time.deltaTime);

            // 地形の外・奈落への落下救済
            if (transform.position.y < -500f && Game.Instance.Layout != null && Player.CurrentAreaId == AreaIds.Overworld)
            {
                Player.Position = Game.Instance.Layout.OnGround(Game.Instance.Layout.PlayerSpawn) + Vector3.up;
            }
        }

        /// <summary>
        /// 深さ0.6mを超える水へは入らない（泳ぎは無い。マップ設計 v0.2 §2.7）。橋の上・浅瀬は通れる。
        /// 今いる所より深くなる向きだけ止め、壁沿いのように横へは滑らせる。すでに深い所にいても浅い方へは出られる。
        /// 水深は WorldLayout.WaterDepthSampler（生成バージョン2の星だけ設定される）。
        /// </summary>
        private Vector3 LimitWading(Vector3 move)
        {
            if (move.sqrMagnitude < 1e-4f || Player == null || Player.CurrentAreaId != AreaIds.Overworld) return move;
            var layout = Game.Instance != null ? Game.Instance.Layout : null;
            if (layout == null) return move;
            var pos = transform.position;
            float here = layout.WaterDepthAt(pos);
            if (!TooDeep(layout, pos, move, here)) return move;
            var mx = new Vector3(move.x, 0f, 0f);
            if (!TooDeep(layout, pos, mx, here)) return mx;
            var mz = new Vector3(0f, 0f, move.z);
            if (!TooDeep(layout, pos, mz, here)) return mz;
            return Vector3.zero;
        }

        private static bool TooDeep(Uchujin.World.WorldLayout layout, Vector3 pos, Vector3 dir, float here)
        {
            if (dir.sqrMagnitude < 1e-4f) return false;
            float d = layout.WaterDepthAt(pos + dir.normalized * 0.6f);
            return d > Uchujin.World.WorldLayout.WadeableDepth && d > here + 0.02f;
        }

        /// <summary>簡単な身振りアニメーション（見た目担当モジュールが差し替え可能）。</summary>
        public void PlayGesture(PlayerGesture gesture)
        {
            StartCoroutine(GestureRoutine(gesture));
        }

        private System.Collections.IEnumerator GestureRoutine(PlayerGesture g)
        {
            var body = transform.Find("Visual");
            if (body == null) yield break;
            float t = 0f;
            var baseRot = body.localRotation;
            var basePos = body.localPosition;
            while (t < 0.8f)
            {
                t += UnityEngine.Time.deltaTime;
                float k = Mathf.Sin(t / 0.8f * Mathf.PI * 2f);
                switch (g)
                {
                    case PlayerGesture.Nod:
                    case PlayerGesture.Bow:
                        body.localRotation = baseRot * Quaternion.Euler(k * (g == PlayerGesture.Bow ? 25f : 12f), 0, 0);
                        break;
                    case PlayerGesture.ShakeHead:
                        body.localRotation = baseRot * Quaternion.Euler(0, k * 20f, 0);
                        break;
                    case PlayerGesture.Happy:
                    case PlayerGesture.Clap:
                    case PlayerGesture.Surprised:
                        body.localPosition = basePos + Vector3.up * Mathf.Abs(k) * 0.3f;
                        break;
                    case PlayerGesture.Sad:
                        body.localRotation = baseRot * Quaternion.Euler(15f, 0, 0);
                        break;
                    default:
                        body.localRotation = baseRot * Quaternion.Euler(0, 0, k * 10f);
                        break;
                }
                yield return null;
            }
            body.localRotation = baseRot;
            body.localPosition = basePos;
        }
    }

    /// <summary>カメラ。具体的な方式は未決定（基本決定 v0.2）のため、斜め見下ろし／三人称／真上を切り替えられる。</summary>
    public class CameraRig : MonoBehaviour
    {
        public enum Mode { Oblique, ThirdPerson, TopDown }

        public Transform target;
        public Mode mode = Mode.Oblique;
        public float yaw = 0f;
        public float pitch = 50f;
        public float distance = 14f;
        public float followSharpness = 10f;

        private Camera cam;

        public static CameraRig Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
            ApplyModeDefaults();
        }

        public void ApplyModeDefaults()
        {
            switch (mode)
            {
                case Mode.Oblique: pitch = 50f; distance = 14f; break;
                case Mode.ThirdPerson: pitch = 18f; distance = 6.5f; break;
                case Mode.TopDown: pitch = 82f; distance = 26f; break;
            }
        }

        private void LateUpdate()
        {
            if (target == null) return;
            bool blocked = UIManager.BlocksGameplay;
            if (!blocked)
            {
                if (GameInput.CameraMode)
                {
                    mode = (Mode)(((int)mode + 1) % 3);
                    ApplyModeDefaults();
                    UIManager.Toast(Loc.T(mode == Mode.Oblique ? "カメラ：斜め見下ろし" : mode == Mode.ThirdPerson ? "カメラ：三人称" : "カメラ：真上",
                        mode == Mode.Oblique ? "Camera: oblique" : mode == Mode.ThirdPerson ? "Camera: third person" : "Camera: top-down"), 1.5f);
                }
                if (GameInput.MouseButtonHeld(1))
                {
                    var d = GameInput.MouseDelta;
                    yaw += d.x * 0.3f;
                    if (mode == Mode.ThirdPerson) pitch = Mathf.Clamp(pitch - d.y * 0.2f, -10f, 70f);
                }
                if (GameInput.KeyHeld(KeyCode.Z)) yaw -= 90f * UnityEngine.Time.deltaTime;
                if (GameInput.KeyHeld(KeyCode.C)) yaw += 90f * UnityEngine.Time.deltaTime;
                float scroll = GameInput.Scroll;
                if (Mathf.Abs(scroll) > 0.01f) distance = Mathf.Clamp(distance - scroll * 1.2f, 3f, 40f);
            }

            var focus = target.position + Vector3.up * 1.2f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            var desired = focus - rot * Vector3.forward * distance;
            // 地形・壁へのめり込みを軽減
            if (Physics.Raycast(focus, (desired - focus).normalized, out var hit, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform != target && !hit.transform.IsChildOf(target))
                    desired = hit.point + hit.normal * 0.3f;
            }
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-followSharpness * UnityEngine.Time.deltaTime));
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            var focus = target.position + Vector3.up * 1.2f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = focus - rot * Vector3.forward * distance;
            transform.rotation = Quaternion.LookRotation(focus - transform.position, Vector3.up);
        }
    }
}
