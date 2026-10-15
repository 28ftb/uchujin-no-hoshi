using UnityEngine;

namespace Uchujin.Core.UI
{
    /// <summary>
    /// IMGUI のウィンドウ基底。UIManager.Open(window) で開く。
    /// DrawContents は仮想座標系（高さ1080）で、content 矩形の中に GUILayout で描いてよい。
    /// </summary>
    public abstract class UIWindow
    {
        public bool IsOpen { get; internal set; }
        public virtual string Title => "";
        /// <summary>モーダルなら他のゲーム操作（移動・道具）を止める。</summary>
        public virtual bool IsModal => true;
        /// <summary>ソロプレイ中に開いている間、時間を止める（タブレット・会話など）。マルチでは止まらない。</summary>
        public virtual bool PausesTimeInSolo => false;
        public virtual bool ShowCloseButton => true;
        /// <summary>Esc で閉じられるか。</summary>
        public virtual bool CloseOnEscape => true;
        /// <summary>仮想解像度上の大きさ。</summary>
        public virtual Vector2 Size => new Vector2(900, 640);
        /// <summary>null なら中央。</summary>
        public virtual Vector2? Position => null;

        protected Vector2 scroll;

        public virtual void OnOpen() { }
        public virtual void OnClose() { }
        public virtual void Update() { }
        public abstract void DrawContents(Rect content);

        public void Close() => UIManager.Close(this);

        public Rect GetRect()
        {
            var size = Size;
            var pos = Position ?? new Vector2((UIStyles.VirtualWidth - size.x) * 0.5f, (UIStyles.VirtualHeight - size.y) * 0.5f);
            return new Rect(pos.x, pos.y, size.x, size.y);
        }
    }

    /// <summary>毎フレーム HUD 層に描画するもの（吹き出し、釣りゲージ、ミニゲーム等）。</summary>
    public interface IUIOverlay
    {
        /// <summary>小さいほど先に（下に）描画。HUD=0、吹き出し=100、ミニゲーム=200 目安。</summary>
        int OverlayOrder { get; }
        void DrawOverlay();
    }
}
