using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    /// <summary>
    /// プレイヤー1人分の状態。ローカルプレイヤーとリモート（マルチのゲスト／ホスト）の両方に使う。
    /// 持ち物・お金・道具は個人の携帯資産（マルチ仕様 §5）。
    /// </summary>
    public sealed class PlayerState
    {
        public string PlayerId { get; }
        public bool IsLocal { get; }
        public CharacterAppearance Appearance { get; set; } = new CharacterAppearance();
        public Inventory Inventory { get; private set; }
        public Wallet Wallet { get; private set; }
        public ToolBelt Tools { get; private set; }
        /// <summary>ホットバーで選択中の持ち物スロット（-1=なし）。</summary>
        public int SelectedSlot { get; set; } = -1;
        /// <summary>マルチでの権限（ホストが設定）。</summary>
        public PermissionLevel Permission { get; set; } = PermissionLevel.Owner;
        /// <summary>シーン上のアバター（無い場合もある）。</summary>
        public Transform Avatar { get; set; }
        /// <summary>現在いるエリア（"overworld" "mine:3" "house:player" など）。</summary>
        public string CurrentAreaId { get; set; } = AreaIds.Overworld;
        /// <summary>入力を受け付けない状態（釣りミニゲーム中、カットシーン中など）。</summary>
        public bool InputLocked => inputLocks.Count > 0;

        private readonly HashSet<string> inputLocks = new HashSet<string>();

        public string DisplayName => string.IsNullOrEmpty(Appearance?.displayName) ? Loc.T("旅人", "Traveler") : Appearance.displayName;

        public Vector3 Position
        {
            get => Avatar != null ? Avatar.position : lastKnownPosition;
            set
            {
                lastKnownPosition = value;
                if (Avatar != null)
                {
                    var cc = Avatar.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    Avatar.position = value;
                    if (cc != null) cc.enabled = true;
                }
            }
        }

        public Vector3 Forward => Avatar != null ? Avatar.forward : Vector3.forward;

        private Vector3 lastKnownPosition;

        public PlayerState(string playerId, bool isLocal)
        {
            PlayerId = playerId;
            IsLocal = isLocal;
            Inventory = new Inventory(24, playerId);
            Wallet = new Wallet { ownerId = playerId };
            Tools = ToolBelt.CreateStarter();
        }

        public void LockInput(string token) => inputLocks.Add(token);
        public void UnlockInput(string token) => inputLocks.Remove(token);
        public void ClearInputLocks() => inputLocks.Clear();

        public ItemStack SelectedItem
        {
            get
            {
                if (SelectedSlot < 0) return null;
                var s = Inventory.GetSlot(SelectedSlot);
                return s == null || s.IsEmpty ? null : s;
            }
        }

        /// <summary>持ち物に追加。入りきらない分は足元へ落とす（落とし物表示はコアの DroppedItem）。</summary>
        public void GiveOrDrop(ItemStack stack, string source)
        {
            int left = Inventory.Add(stack, source);
            if (left > 0)
            {
                DroppedItem.Spawn(stack.CloneWithCount(left), Position + Forward * 0.8f);
                UI.UIManager.Toast(Loc.T("持ち物がいっぱいなので足元に置きました", "Inventory full - dropped at your feet"));
            }
        }

        [Serializable]
        public class ProfileData
        {
            public string playerId;
            public CharacterAppearance appearance;
            public Inventory inventory;
            public int money;
            public ToolBelt tools;
            public int selectedSlot;
        }

        public ProfileData CaptureProfile()
        {
            return new ProfileData
            {
                playerId = PlayerId,
                appearance = Appearance,
                inventory = Inventory,
                money = Wallet.money,
                tools = Tools,
                selectedSlot = SelectedSlot
            };
        }

        public void RestoreProfile(ProfileData d)
        {
            if (d == null) return;
            if (d.appearance != null) Appearance = d.appearance;
            if (d.inventory != null)
            {
                d.inventory.ownerId = PlayerId;
                d.inventory.EnsureSlots();
                var changed = Inventory.Changed;
                Inventory = d.inventory;
                Inventory.Changed = changed;
            }
            Wallet.money = d.money;
            if (d.tools != null) Tools = d.tools;
            SelectedSlot = d.selectedSlot;
        }
    }

    /// <summary>マルチプレイの操作権限3段階（マルチ仕様 §6.2）＋星の持ち主。</summary>
    public enum PermissionLevel
    {
        Sightseeing = 0, // 観光
        Helper = 1,      // お手伝い
        CoLiving = 2,    // 共同生活
        Owner = 3        // 星の持ち主（ホスト）
    }

    public static class AreaIds
    {
        public const string Overworld = "overworld";
        public const string PlayerHouse = "house:player";
        public const string Shop = "shop:interior";
        public static string MineFloor(int floor) => "mine:" + floor;
        public static string ResidentHouse(string residentId) => "house:" + residentId;
        public static bool IsMine(string areaId) => areaId != null && areaId.StartsWith("mine:");
        public static int MineFloorOf(string areaId)
        {
            if (!IsMine(areaId)) return 0;
            int.TryParse(areaId.Substring(5), out var f);
            return f;
        }
    }

    /// <summary>参加中の全プレイヤー（ソロでは1人）。</summary>
    public sealed class PlayerRegistry
    {
        private readonly Dictionary<string, PlayerState> players = new Dictionary<string, PlayerState>();
        public PlayerState Local { get; private set; }
        public IEnumerable<PlayerState> All => players.Values;
        public int Count => players.Count;

        public PlayerState CreateLocal(string playerId)
        {
            Local = new PlayerState(playerId, true);
            players[playerId] = Local;
            return Local;
        }

        public PlayerState AddRemote(string playerId)
        {
            if (players.TryGetValue(playerId, out var existing)) return existing;
            var p = new PlayerState(playerId, false) { Permission = PermissionLevel.Helper };
            players[playerId] = p;
            return p;
        }

        public void Remove(string playerId)
        {
            if (Local != null && Local.PlayerId == playerId) return;
            players.Remove(playerId);
        }

        public PlayerState Get(string playerId)
        {
            if (playerId != null && players.TryGetValue(playerId, out var p)) return p;
            return null;
        }

        public void ClearRemotes()
        {
            var keep = Local;
            players.Clear();
            if (keep != null) players[keep.PlayerId] = keep;
        }
    }
}
