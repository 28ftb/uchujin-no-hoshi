using System;
using System.Collections.Generic;

namespace Uchujin.Network
{
    public enum NetDelivery
    {
        /// <summary>到達保証・順序保証（コマンド、状態、取引など）。大きなデータは分割して送る。</summary>
        Reliable,
        /// <summary>到達保証なし（位置など。NetConfig.MaxUnreliableBytes 以下）。</summary>
        Unreliable
    }

    public enum TransportState
    {
        Idle,
        Starting,     // ログイン・ロビー作成などの準備中
        Hosting,      // ホストとして待ち受け中
        Connecting,   // ホストへ接続中
        Connected,    // ホストへ接続済み（ゲスト）
        Failed,
        Closed
    }

    public class TransportHostOptions
    {
        public int MaxPeers = NetConfig.MaxPlayers - 1;
        public int Port = NetConfig.DefaultUdpPort;
        public string HostName;
        public string PlanetName;
        public string WorldId;
    }

    /// <summary>
    /// 通信路の抽象化。EOS P2P（EosP2PTransport）と、開発検証用の LAN UDP（LocalUdpTransport）を差し替えられる。
    /// すべてのイベントは Poll() の中（メインスレッド）で発行される。
    /// ピアIDはトランスポート内の識別子（UDP は "ip:port"、EOS は ProductUserId の文字列）で、プレイヤーIDとは別物。
    /// </summary>
    public interface INetTransport : IDisposable
    {
        /// <summary>"udp" / "eos"。</summary>
        string Kind { get; }
        bool IsHost { get; }
        TransportState State { get; }
        /// <summary>画面表示用の状態説明（接続先・招待コード・エラー理由など）。</summary>
        string StatusText { get; }
        /// <summary>ゲスト側：ホストのピアID。</summary>
        string HostPeerId { get; }

        /// <summary>ピアとの接続が確立した（ホスト：ゲストが来た／ゲスト：ホストに繋がった）。</summary>
        event Action<string> PeerConnected;
        /// <summary>ピアとの接続が切れた（理由文字列）。</summary>
        event Action<string, string> PeerDisconnected;
        /// <summary>メッセージを受信した（分割されたものは再構成済み）。</summary>
        event Action<string, byte[]> Received;

        void StartHost(TransportHostOptions options);
        /// <summary>接続先へ繋ぐ（UDP："ip:port"、EOS：ロビーID または 招待コード）。</summary>
        void Connect(string target);
        void Send(string peerId, byte[] data, NetDelivery delivery);
        void DisconnectPeer(string peerId);
        void Shutdown();
        /// <summary>毎フレーム（メインスレッド）。受信処理・再送・タイムアウト。</summary>
        void Poll(float realDeltaTime);
    }

    /// <summary>EOS のロビー一覧（友達の星の一覧）。</summary>
    public class LobbyListing
    {
        public string LobbyId;
        public string HostName;
        public string PlanetName;
        public string InviteCode;
        public int Members;
        public int MaxMembers;
    }

    /// <summary>ロビー検索・招待コードに対応するトランスポート（EOS）。</summary>
    public interface ILobbyTransport
    {
        /// <summary>ホスト中の招待コード（無ければ null）。</summary>
        string InviteCode { get; }
        void RefreshLobbies(Action<List<LobbyListing>> onDone, Action<string> onError);
        void JoinLobby(string lobbyId);
        void JoinByInviteCode(string code);
    }

    /// <summary>トランスポートの生成口。EOS は UCHUJIN_EOS 定義時のみ。</summary>
    public static class TransportFactory
    {
#if UCHUJIN_EOS
        public static readonly bool EosAvailable = true;
#else
        public static readonly bool EosAvailable = false;
#endif

        public static INetTransport Create(string kind)
        {
            if (kind == "eos")
            {
#if UCHUJIN_EOS
                return new EosP2PTransport();
#else
                return null;
#endif
            }
            return new LocalUdpTransport();
        }
    }
}
