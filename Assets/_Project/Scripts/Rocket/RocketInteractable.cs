using System;
using UnityEngine;
using Uchujin.Core;

namespace Uchujin.Rocket
{
    /// <summary>
    /// 表示文が状態で変わる [E] の対象（宇宙船・作業台・加工台・部品置き場など）。
    /// コライダーは子のプリミティブに付いていればよい（PlayerInteractor は GetComponentInParent で探す）。
    /// </summary>
    public class RocketInteractable : MonoBehaviour, IInteractable
    {
        public Func<PlayerState, string> Prompt;
        public Func<PlayerState, bool> Can;
        public Action<PlayerState> Act;

        public string GetInteractionPrompt(PlayerState player)
        {
            try { return Prompt != null ? Prompt(player) : null; }
            catch (Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        public bool CanInteract(PlayerState player) => Can == null || Can(player);

        public void Interact(PlayerState player)
        {
            try { Act?.Invoke(player); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static RocketInteractable Attach(GameObject go, Func<PlayerState, string> prompt, Action<PlayerState> act, Func<PlayerState, bool> can = null)
        {
            if (go == null) return null;
            var ri = go.GetComponent<RocketInteractable>();
            if (ri == null) ri = go.AddComponent<RocketInteractable>();
            ri.Prompt = prompt;
            ri.Act = act;
            ri.Can = can;
            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();
            return ri;
        }
    }
}
