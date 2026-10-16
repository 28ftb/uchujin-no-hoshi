using UnityEngine;
using Uchujin.Core;
using Uchujin.Core.Visual;

namespace Uchujin.Tablet
{
    /// <summary>
    /// タブレットを開いている間、主人公が右手（Visual/ToolHolder）に持つ3Dのタブレット（Resources/Art/Tablet、幅 0.40 m）。
    /// 主人公が不時着した宇宙船から持ち出した故郷の端末（タブレット仕様 §2.1）なので、最初はロケットとおそろいの修理前の姿。
    /// ロケットの修理がすべて終わった（IRocketService.IsLaunchReady）後は、村の技術で直した修理後の姿にする（ユーザーの指定）。
    /// 見た目だけで、タブレットの機能・データには関わらない。モデルが無ければ何も出さない。
    /// </summary>
    public sealed class TabletHeldView : MonoBehaviour
    {
        internal TabletSystem system;

        /// <summary>【仮数値】右手からの位置と向き。体の前・胸の高さで、画面（モデルの +Z）を顔のほうへ上向きに傾ける。</summary>
        private static readonly Vector3 LocalPosition = new Vector3(-0.2f, 0.15f, 0.35f);
        private static readonly Vector3 LocalEuler = new Vector3(-40f, 180f, 0f);

        private GameObject held;
        private Transform heldOn;
        private bool heldRepaired;

        private void LateUpdate()
        {
            var game = Game.Instance;
            var avatar = game != null && game.PlayerSys != null ? game.PlayerSys.LocalAvatar : null;
            bool open = system != null && system.IsOpen && avatar != null;
            if (!open)
            {
                if (held != null && held.activeSelf) held.SetActive(false);
                return;
            }
            var hand = FindDeep(avatar.transform, "ToolHolder");
            if (hand == null) return;
            var rocket = game.Service<IRocketService>();
            bool repaired = rocket != null && (rocket.IsLaunchReady || rocket.HasLaunched);
            if (held == null || heldOn != hand || heldRepaired != repaired)
            {
                if (held != null) Destroy(held);
                held = Attach(hand, repaired);
                heldOn = hand;
                heldRepaired = repaired;
            }
            if (!held.activeSelf) held.SetActive(true);
        }

        /// <summary>右手（ToolHolder）にタブレットを持たせる。確認用の撮影（Editor/VillageArtGallery）でも使う。</summary>
        public static GameObject Attach(Transform hand, bool repaired)
        {
            var go = new GameObject("HeldTablet");
            go.transform.SetParent(hand, false);
            go.transform.localPosition = LocalPosition;
            go.transform.localRotation = Quaternion.Euler(LocalEuler);
            ArtModels.Build(go.transform, repaired ? "Tablet/tablet_after" : "Tablet/tablet_before");
            return go;
        }

        private void OnDestroy()
        {
            if (held != null) Destroy(held);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform c in root)
            {
                var f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
