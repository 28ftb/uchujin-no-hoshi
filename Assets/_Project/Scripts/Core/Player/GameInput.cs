using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Uchujin.Core
{
    /// <summary>
    /// 入力の抽象化。旧Input Manager（Old/Both）を優先し、新Input System専用プロジェクトでも動くようにする。
    /// Unity 6 の新規プロジェクトは「Input System Package (New)」既定の場合があるため両対応している。
    /// 新Input System側のコードは Unity 上でのコンパイル確認が必要。
    /// キー割り当ての変更UIは未実装（今後の課題）。
    /// </summary>
    public static class GameInput
    {
        public static bool KeyHeld(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(key);
#elif ENABLE_INPUT_SYSTEM
            var k = Map(key);
            if (k == Key.None) return MouseHeld(key);
            return Keyboard.current != null && Keyboard.current[k].isPressed;
#else
            return false;
#endif
        }

        public static bool KeyDown(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(key);
#elif ENABLE_INPUT_SYSTEM
            var k = Map(key);
            if (k == Key.None) return MouseDown(key);
            return Keyboard.current != null && Keyboard.current[k].wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static bool KeyUp(KeyCode key)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyUp(key);
#elif ENABLE_INPUT_SYSTEM
            var k = Map(key);
            if (k == Key.None) return false;
            return Keyboard.current != null && Keyboard.current[k].wasReleasedThisFrame;
#else
            return false;
#endif
        }

        public static bool MouseButtonHeld(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.isPressed : button == 1 ? m.rightButton.isPressed : m.middleButton.isPressed;
#else
            return false;
#endif
        }

        public static bool MouseButtonDown(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonDown(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasPressedThisFrame : button == 1 ? m.rightButton.wasPressedThisFrame : m.middleButton.wasPressedThisFrame;
#else
            return false;
#endif
        }

        public static bool MouseButtonUp(int button)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButtonUp(button);
#elif ENABLE_INPUT_SYSTEM
            var m = Mouse.current;
            if (m == null) return false;
            return button == 0 ? m.leftButton.wasReleasedThisFrame : button == 1 ? m.rightButton.wasReleasedThisFrame : m.middleButton.wasReleasedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>マウス移動量（ピクセル）。</summary>
        public static Vector2 MouseDelta
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 10f;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.delta.ReadValue() * 0.5f : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>ホイール（上=正）。1ノッチ≒1。</summary>
        public static float Scroll
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mouseScrollDelta.y;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#else
                return 0f;
#endif
            }
        }

        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_LEGACY_INPUT_MANAGER
                return Input.mousePosition;
#elif ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Vector2.zero;
#endif
            }
        }

        /// <summary>移動入力（WASD・矢印）。</summary>
        public static Vector2 Move
        {
            get
            {
                float x = 0f, y = 0f;
                if (KeyHeld(KeyCode.A) || KeyHeld(KeyCode.LeftArrow)) x -= 1f;
                if (KeyHeld(KeyCode.D) || KeyHeld(KeyCode.RightArrow)) x += 1f;
                if (KeyHeld(KeyCode.S) || KeyHeld(KeyCode.DownArrow)) y -= 1f;
                if (KeyHeld(KeyCode.W) || KeyHeld(KeyCode.UpArrow)) y += 1f;
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        public static bool Run => KeyHeld(KeyCode.LeftShift) || KeyHeld(KeyCode.RightShift);
        public static bool Interact => KeyDown(KeyCode.E);
        public static bool UseTool => KeyDown(KeyCode.F) || MouseButtonDown(0);
        public static bool CycleTool => KeyDown(KeyCode.R);
        public static bool Jump => KeyDown(KeyCode.Space);
        public static bool Tablet => KeyDown(KeyCode.Tab);
        public static bool Inventory => KeyDown(KeyCode.I);
        public static bool GestureMenu => KeyDown(KeyCode.G);
        public static bool CameraMode => KeyDown(KeyCode.V);
        public static bool Pause => KeyDown(KeyCode.Escape);
        public static bool QuickSave => KeyDown(KeyCode.F5);
        public static bool ToggleHelp => KeyDown(KeyCode.H);
        public static bool Map => KeyDown(KeyCode.M);

        /// <summary>数字キーで選んだホットバー番号（0〜9）。無ければ -1。</summary>
        public static int HotbarKey
        {
            get
            {
                for (int i = 0; i < 10; i++)
                {
                    var k = i == 9 ? KeyCode.Alpha0 : KeyCode.Alpha1 + i;
                    if (KeyDown(k)) return i;
                }
                return -1;
            }
        }

#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
        private static bool MouseHeld(KeyCode key)
        {
            if (key == KeyCode.Mouse0) return MouseButtonHeld(0);
            if (key == KeyCode.Mouse1) return MouseButtonHeld(1);
            if (key == KeyCode.Mouse2) return MouseButtonHeld(2);
            return false;
        }

        private static bool MouseDown(KeyCode key)
        {
            if (key == KeyCode.Mouse0) return MouseButtonDown(0);
            if (key == KeyCode.Mouse1) return MouseButtonDown(1);
            if (key == KeyCode.Mouse2) return MouseButtonDown(2);
            return false;
        }

        private static Key Map(KeyCode key)
        {
            if (key >= KeyCode.A && key <= KeyCode.Z) return Key.A + (key - KeyCode.A);
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return key == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (key - KeyCode.Alpha1);
            if (key >= KeyCode.F1 && key <= KeyCode.F12) return Key.F1 + (key - KeyCode.F1);
            switch (key)
            {
                case KeyCode.Space: return Key.Space;
                case KeyCode.Return: return Key.Enter;
                case KeyCode.KeypadEnter: return Key.NumpadEnter;
                case KeyCode.Escape: return Key.Escape;
                case KeyCode.Tab: return Key.Tab;
                case KeyCode.Backspace: return Key.Backspace;
                case KeyCode.LeftShift: return Key.LeftShift;
                case KeyCode.RightShift: return Key.RightShift;
                case KeyCode.LeftControl: return Key.LeftCtrl;
                case KeyCode.RightControl: return Key.RightCtrl;
                case KeyCode.LeftAlt: return Key.LeftAlt;
                case KeyCode.UpArrow: return Key.UpArrow;
                case KeyCode.DownArrow: return Key.DownArrow;
                case KeyCode.LeftArrow: return Key.LeftArrow;
                case KeyCode.RightArrow: return Key.RightArrow;
                case KeyCode.Comma: return Key.Comma;
                case KeyCode.Period: return Key.Period;
                case KeyCode.Minus: return Key.Minus;
                case KeyCode.Equals: return Key.Equals;
                default: return Key.None;
            }
        }
#endif
    }
}
