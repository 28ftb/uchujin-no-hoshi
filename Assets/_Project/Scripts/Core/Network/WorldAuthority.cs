using System;
using System.Collections.Generic;
using UnityEngine;

namespace Uchujin.Core
{
    public enum NetRole
    {
        Offline,
        Host,
        Guest
    }

    /// <summary>
    /// プレイヤーが星の状態（星のセーブに属するもの）を変更する操作の基底クラス。
    /// 例：作物を収穫する、鉱石を掘る、家具を置く、棚に商品を並べる、生物を捕まえる。
    /// JsonUtility で直列化されるため public フィールドのみで定義すること。
    /// </summary>
    [Serializable]
    public abstract class WorldCommand
    {
        public string commandId;
        public string issuerId;
    }

    public sealed class CommandResult
    {
        public bool Success;
        public string MessageJa;
        public string MessageEn;
        /// <summary>結果の追加データ（採掘で得た物など。モジュールが自由に使う）。</summary>
        public string Data;

        public string Message => Loc.T(MessageJa, MessageEn);

        public static CommandResult Ok(string ja = null, string en = null) => new CommandResult { Success = true, MessageJa = ja, MessageEn = en };
        public static CommandResult Fail(string ja, string en) => new CommandResult { Success = false, MessageJa = ja, MessageEn = en };
    }

    /// <summary>
    /// コマンド実行時の文脈。発行者へのアイテム付与・課金は必ずここを通す。
    /// （発行者がリモートのゲストなら、ネットワーク経由で相手の持ち物へ反映される）
    /// </summary>
    public sealed class CommandContext
    {
        public string IssuerId { get; }
        public PlayerState Issuer { get; }
        public bool IsLocalIssuer => Issuer != null && Issuer.IsLocal;

        public CommandContext(string issuerId, PlayerState issuer)
        {
            IssuerId = issuerId;
            Issuer = issuer;
        }

        public PermissionLevel Permission => Issuer != null ? Issuer.Permission : PermissionLevel.Sightseeing;

        public bool HasPermission(PermissionLevel required) => Permission >= required;

        /// <summary>発行者にアイテムを渡す（入らなければ足元に落とす）。</summary>
        public void GiveItem(ItemStack stack, string source)
        {
            if (stack == null || stack.IsEmpty) return;
            if (IsLocalIssuer) Issuer.GiveOrDrop(stack, source);
            else WorldAuthority.Bridge?.GrantItemToRemote(IssuerId, stack, source);
        }

        /// <summary>発行者が十分な持ち物を持っているか（リモートはゲスト側の事前確認を信頼する）。</summary>
        public bool HasItem(string itemId, int count)
        {
            if (IsLocalIssuer) return Issuer.Inventory.Has(itemId, count);
            return WorldAuthority.Bridge == null || WorldAuthority.Bridge.RemoteHasItem(IssuerId, itemId, count);
        }

        /// <summary>発行者の持ち物から取り除く。</summary>
        public bool TakeItem(string itemId, int count, string reason)
        {
            if (IsLocalIssuer) return Issuer.Inventory.Remove(itemId, count, 0, reason) == count;
            return WorldAuthority.Bridge != null && WorldAuthority.Bridge.TakeItemFromRemote(IssuerId, itemId, count, reason);
        }

        public bool TryCharge(int amount, string reason)
        {
            if (amount <= 0) return true;
            if (IsLocalIssuer) return Issuer.Wallet.TrySpend(amount, reason);
            return WorldAuthority.Bridge != null && WorldAuthority.Bridge.TryChargeRemote(IssuerId, amount, reason);
        }

        public void PayMoney(int amount, string reason)
        {
            if (amount <= 0) return;
            if (IsLocalIssuer) Issuer.Wallet.Add(amount, reason);
            else WorldAuthority.Bridge?.GrantMoneyToRemote(IssuerId, amount, reason);
        }

        /// <summary>発行者へ通知（トースト）。</summary>
        public void Notify(string ja, string en)
        {
            if (IsLocalIssuer) UI.UIManager.Toast(Loc.T(ja, en));
            else WorldAuthority.Bridge?.NotifyRemote(IssuerId, ja, en);
        }
    }

    /// <summary>ネットワークモジュールが実装する橋渡し。オフラインでは null。</summary>
    public interface INetworkBridge
    {
        void SendCommandToHost(string typeName, string json, string commandId);
        void MarkDirty(string stateKey);
        void GrantItemToRemote(string playerId, ItemStack stack, string source);
        bool TakeItemFromRemote(string playerId, string itemId, int count, string reason);
        bool RemoteHasItem(string playerId, string itemId, int count);
        bool TryChargeRemote(string playerId, int amount, string reason);
        void GrantMoneyToRemote(string playerId, int amount, string reason);
        void NotifyRemote(string playerId, string ja, string en);
    }

    /// <summary>
    /// ホスト権威のコマンド実行窓口（マルチ仕様 §20：同時操作はホストが確定する）。
    ///
    /// 使い方（各モジュール）：
    ///   1. [Serializable] class HarvestCropCommand : WorldCommand { public int plotIndex; }
    ///   2. RegisterContent() で WorldAuthority.Register&lt;HarvestCropCommand&gt;(HandleHarvest);
    ///   3. 入力時に WorldAuthority.Submit(new HarvestCropCommand { plotIndex = i }, r => ...);
    ///   ソロ／ホストでは即時に実行され、ゲストではホストへ送られて結果が返る。
    ///   ハンドラ内では状態を変更したら MarkDirty し、報酬は ctx.GiveItem で渡す。
    /// </summary>
    public static class WorldAuthority
    {
        public static NetRole Role { get; set; } = NetRole.Offline;
        public static string LocalPlayerId { get; set; }
        public static INetworkBridge Bridge { get; set; }

        public static bool IsAuthority => Role != NetRole.Guest;
        public static bool IsMultiplayer => Role != NetRole.Offline;
        public static bool IsGuest => Role == NetRole.Guest;

        private static readonly Dictionary<string, Type> typesByName = new Dictionary<string, Type>();
        private static readonly Dictionary<Type, Func<WorldCommand, CommandContext, CommandResult>> handlers =
            new Dictionary<Type, Func<WorldCommand, CommandContext, CommandResult>>();
        private static readonly Dictionary<string, Action<CommandResult>> pending = new Dictionary<string, Action<CommandResult>>();

        /// <summary>プレイヤーIDから PlayerState を引く関数（Game が設定）。</summary>
        public static Func<string, PlayerState> PlayerLookup;

        public static void Register<T>(Func<T, CommandContext, CommandResult> handler) where T : WorldCommand
        {
            var t = typeof(T);
            typesByName[t.FullName] = t;
            handlers[t] = (cmd, ctx) => handler((T)cmd, ctx);
        }

        public static void Submit(WorldCommand cmd, Action<CommandResult> onComplete = null)
        {
            if (cmd == null) return;
            if (string.IsNullOrEmpty(cmd.commandId)) cmd.commandId = Ids.New();
            if (string.IsNullOrEmpty(cmd.issuerId)) cmd.issuerId = LocalPlayerId;

            if (IsAuthority || Bridge == null)
            {
                var result = Execute(cmd, cmd.issuerId);
                onComplete?.Invoke(result);
                if (result != null && !result.Success && !string.IsNullOrEmpty(result.MessageJa) && cmd.issuerId == LocalPlayerId)
                    UI.UIManager.Toast(result.Message);
                return;
            }

            if (onComplete != null) pending[cmd.commandId] = onComplete;
            Bridge.SendCommandToHost(cmd.GetType().FullName, JsonUtility.ToJson(cmd), cmd.commandId);
        }

        private static CommandResult Execute(WorldCommand cmd, string issuerId)
        {
            if (!handlers.TryGetValue(cmd.GetType(), out var handler))
            {
                Debug.LogWarning($"WorldAuthority: {cmd.GetType().Name} のハンドラが未登録です");
                return CommandResult.Fail("この操作はまだ実装されていません", "This action is not implemented yet");
            }
            var player = PlayerLookup?.Invoke(issuerId);
            var ctx = new CommandContext(issuerId, player);
            try
            {
                return handler(cmd, ctx) ?? CommandResult.Ok();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return CommandResult.Fail("操作中にエラーが発生しました", "An error occurred");
            }
        }

        /// <summary>ホスト側：ゲストから届いたコマンドを実行する（ネットワークモジュールが呼ぶ）。</summary>
        public static CommandResult ExecuteRemote(string typeName, string json, string issuerId)
        {
            if (!typesByName.TryGetValue(typeName, out var t))
                return CommandResult.Fail("不明な操作です", "Unknown command");
            var cmd = (WorldCommand)JsonUtility.FromJson(json, t);
            cmd.issuerId = issuerId; // なりすまし防止：送信元のIDで上書き
            return Execute(cmd, issuerId);
        }

        /// <summary>ゲスト側：ホストから結果が届いた（ネットワークモジュールが呼ぶ）。</summary>
        public static void CompletePending(string commandId, CommandResult result)
        {
            if (commandId != null && pending.TryGetValue(commandId, out var cb))
            {
                pending.Remove(commandId);
                cb?.Invoke(result);
            }
            if (result != null && !result.Success && !string.IsNullOrEmpty(result.MessageJa))
                UI.UIManager.Toast(result.Message);
        }

        public static void MarkDirty(string stateKey)
        {
            if (Role == NetRole.Host) Bridge?.MarkDirty(stateKey);
        }

        public static void ResetSession()
        {
            Role = NetRole.Offline;
            Bridge = null;
            pending.Clear();
        }
    }
}
