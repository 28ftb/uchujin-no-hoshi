using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace Uchujin.Network
{
    /// <summary>
    /// 開発検証用の簡易 UDP トランスポート（EOS なしで LAN／同一PCの2起動をテストする）。
    /// ・System.Net.Sockets の UDP。受信は別スレッド → ConcurrentQueue → Poll()（メインスレッド）で処理。
    /// ・到達保証：連番＋ACK＋一定間隔の再送、受信側で順序を並べ直す（簡易版。輻輳制御は送信窓の上限のみ）。
    /// ・EOS と同じく、1パケット約1200バイトに分割して送る（FragmentAssembler）。
    /// ・暗号化・認証は無い。インターネット越しの公開には使わないこと（README 参照）。
    /// </summary>
    public sealed class LocalUdpTransport : INetTransport
    {
        // ---- 調整値【仮数値】 ----
        private const uint Magic = 0x314A4355; // "UCJ1"
        private const int MaxDatagram = 1200;
        private const int ReliableHeader = 1 + 4;
        private const float ResendInterval = 0.25f;
        private const int MaxResendTries = 40;       // 約10秒届かなければ切断
        private const int MaxInFlight = 128;         // 送信窓
        private const int MaxOutOfOrder = 1024;
        private const float PingInterval = 1f;
        private const float PeerTimeout = 10f;
        private const float ConnectRetryInterval = 0.5f;
        private const float ConnectTimeout = 10f;
        private const int MaxDatagramsPerPoll = 4000;

        private enum PacketKind : byte
        {
            Connect = 1,
            Accept = 2,
            Reject = 3,
            Disconnect = 4,
            Ping = 5,
            Reliable = 6,
            Ack = 7,
            Unreliable = 8
        }

        private struct Datagram
        {
            public IPEndPoint From;
            public byte[] Data;
        }

        private sealed class Pending
        {
            public byte[] Packet;
            public float LastSent;
            public int Tries;
        }

        private sealed class Peer
        {
            public string Id;
            public IPEndPoint EndPoint;
            public bool Connected;
            public uint NextSendSeq = 1;
            public uint NextExpectedSeq = 1;
            public uint NextMessageId = 1;
            public readonly Dictionary<uint, Pending> InFlight = new Dictionary<uint, Pending>();
            public readonly Queue<byte[]> SendQueue = new Queue<byte[]>();
            public readonly Dictionary<uint, byte[]> OutOfOrder = new Dictionary<uint, byte[]>();
            public readonly List<uint> AcksToSend = new List<uint>();
            public readonly FragmentAssembler Assembler = new FragmentAssembler();
            public float LastReceive;
            public float LastSend;
        }

        public string Kind => "udp";
        public bool IsHost { get; private set; }
        public TransportState State { get; private set; } = TransportState.Idle;
        public string StatusText { get; private set; } = "";
        public string HostPeerId { get; private set; }
        public int BoundPort { get; private set; }

        public event Action<string> PeerConnected;
        public event Action<string, string> PeerDisconnected;
        public event Action<string, byte[]> Received;

        private Socket socket;
        private Thread receiveThread;
        private volatile bool running;
        private readonly ConcurrentQueue<Datagram> incoming = new ConcurrentQueue<Datagram>();
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        private int maxPeers = NetConfig.MaxPlayers - 1;
        private float now;
        private IPEndPoint connectTarget;
        private float connectStarted;
        private float lastConnectSent = -10f;

        // ------------------------------------------------------------
        // 開始
        // ------------------------------------------------------------

        public void StartHost(TransportHostOptions options)
        {
            if (options == null) options = new TransportHostOptions();
            IsHost = true;
            maxPeers = Math.Max(1, options.MaxPeers);
            if (!OpenSocket(options.Port))
            {
                State = TransportState.Failed;
                return;
            }
            State = TransportState.Hosting;
            StatusText = "UDP :" + BoundPort;
        }

        public void Connect(string target)
        {
            IsHost = false;
            if (!TryParseEndPoint(target, out var ep, out var error))
            {
                State = TransportState.Failed;
                StatusText = error;
                return;
            }
            if (!OpenSocket(0))
            {
                State = TransportState.Failed;
                return;
            }
            connectTarget = ep;
            HostPeerId = KeyOf(ep);
            var p = GetOrCreatePeer(ep);
            p.LastReceive = now;
            connectStarted = now;
            lastConnectSent = -10f;
            State = TransportState.Connecting;
            StatusText = "→ " + HostPeerId;
        }

        private bool OpenSocket(int port)
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.ReceiveTimeout = 500;
                socket.ReceiveBufferSize = 1 << 20;
                socket.SendBufferSize = 1 << 20;
                TryDisableConnReset(socket);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                BoundPort = ((IPEndPoint)socket.LocalEndPoint).Port;
                running = true;
                receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "UchujinUdpReceive" };
                receiveThread.Start();
                return true;
            }
            catch (Exception e)
            {
                StatusText = "UDP の開始に失敗: " + e.Message;
                Debug.LogWarning("[LocalUdpTransport] " + e);
                CloseSocket();
                return false;
            }
        }

        /// <summary>Windows で相手が居ない時に ICMP 到達不能で受信が例外になる挙動（WSAECONNRESET）を止める。他OSでは何もしない。</summary>
        private static void TryDisableConnReset(Socket s)
        {
            try
            {
                const int SIO_UDP_CONNRESET = -1744830452;
                s.IOControl((IOControlCode)SIO_UDP_CONNRESET, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch
            {
                // Windows 以外では未対応。問題ない。
            }
        }

        public static bool TryParseEndPoint(string text, out IPEndPoint ep, out string error)
        {
            ep = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "アドレスが空です";
                return false;
            }
            text = text.Trim();
            int port = NetConfig.DefaultUdpPort;
            string host = text;
            int colon = text.LastIndexOf(':');
            if (colon > 0 && text.IndexOf(':') == colon)
            {
                host = text.Substring(0, colon);
                if (!int.TryParse(text.Substring(colon + 1), out port) || port <= 0 || port > 65535)
                {
                    error = "ポート番号が正しくありません";
                    return false;
                }
            }
            try
            {
                if (!IPAddress.TryParse(host, out var addr))
                {
                    addr = null;
                    foreach (var a in Dns.GetHostAddresses(host))
                    {
                        if (a.AddressFamily == AddressFamily.InterNetwork)
                        {
                            addr = a;
                            break;
                        }
                    }
                    if (addr == null)
                    {
                        error = "アドレスが見つかりません: " + host;
                        return false;
                    }
                }
                ep = new IPEndPoint(addr, port);
                return true;
            }
            catch (Exception e)
            {
                error = "アドレスを解決できません: " + e.Message;
                return false;
            }
        }

        /// <summary>このPCの IPv4 アドレス（ホストが相手に伝える用）。</summary>
        public static List<string> LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (a.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(a)) continue;
                    list.Add(a.ToString());
                }
            }
            catch
            {
                // 取得できない環境もある
            }
            list.Add("127.0.0.1");
            return list;
        }

        // ------------------------------------------------------------
        // 受信スレッド
        // ------------------------------------------------------------

        private void ReceiveLoop()
        {
            var buffer = new byte[65536];
            while (running)
            {
                try
                {
                    EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    int n = socket.ReceiveFrom(buffer, ref remote);
                    if (n <= 0) continue;
                    var copy = new byte[n];
                    Buffer.BlockCopy(buffer, 0, copy, 0, n);
                    incoming.Enqueue(new Datagram { From = (IPEndPoint)remote, Data = copy });
                }
                catch (SocketException)
                {
                    // タイムアウト（ReceiveTimeout）・接続リセット等。running を確認して続ける。
                    if (!running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception e)
                {
                    if (!running) break;
                    Debug.LogWarning("[LocalUdpTransport] 受信エラー: " + e.Message);
                }
            }
        }

        // ------------------------------------------------------------
        // メインスレッドの処理
        // ------------------------------------------------------------

        public void Poll(float realDeltaTime)
        {
            now += Mathf.Max(0f, realDeltaTime);
            if (socket == null) return;

            int processed = 0;
            while (processed < MaxDatagramsPerPoll && incoming.TryDequeue(out var d))
            {
                processed++;
                try { HandleDatagram(d.From, d.Data); }
                catch (Exception e) { Debug.LogException(e); }
            }

            if (State == TransportState.Connecting && connectTarget != null)
            {
                if (now - connectStarted > ConnectTimeout)
                {
                    State = TransportState.Failed;
                    StatusText = "接続できませんでした（タイムアウト）";
                    var hp = HostPeerId;
                    peers.Remove(hp);
                    PeerDisconnected?.Invoke(hp, "timeout");
                    return;
                }
                if (now - lastConnectSent > ConnectRetryInterval)
                {
                    lastConnectSent = now;
                    var pkt = new byte[1 + 4 + 2];
                    pkt[0] = (byte)PacketKind.Connect;
                    FragmentAssembler.WriteU32(pkt, 1, Magic);
                    FragmentAssembler.WriteU16(pkt, 5, (ushort)NetConfig.ProtocolVersion);
                    RawSend(connectTarget, pkt);
                }
            }

            List<Peer> dead = null;
            foreach (var p in peers.Values)
            {
                if (!p.Connected) continue;
                // 再送
                foreach (var kv in p.InFlight)
                {
                    var pend = kv.Value;
                    if (now - pend.LastSent < ResendInterval) continue;
                    pend.Tries++;
                    if (pend.Tries > MaxResendTries)
                    {
                        if (dead == null) dead = new List<Peer>();
                        dead.Add(p);
                        break;
                    }
                    pend.LastSent = now;
                    RawSend(p.EndPoint, pend.Packet);
                    p.LastSend = now;
                }
                Flush(p);
                SendAcks(p);
                if (now - p.LastSend > PingInterval)
                {
                    RawSend(p.EndPoint, new[] { (byte)PacketKind.Ping });
                    p.LastSend = now;
                }
                if (now - p.LastReceive > PeerTimeout)
                {
                    if (dead == null) dead = new List<Peer>();
                    if (!dead.Contains(p)) dead.Add(p);
                }
                p.Assembler.Cleanup(now);
            }
            if (dead != null)
            {
                foreach (var p in dead) DropPeer(p, "timeout", true);
            }
        }

        private void HandleDatagram(IPEndPoint from, byte[] data)
        {
            if (data == null || data.Length < 1) return;
            var kind = (PacketKind)data[0];
            string key = KeyOf(from);
            peers.TryGetValue(key, out var peer);

            switch (kind)
            {
                case PacketKind.Connect:
                {
                    if (!IsHost || data.Length < 7) return;
                    uint magic = FragmentAssembler.ReadU32(data, 1);
                    int ver = FragmentAssembler.ReadU16(data, 5);
                    if (magic != Magic)
                        return;
                    if (ver != NetConfig.ProtocolVersion)
                    {
                        SendReject(from, "version");
                        return;
                    }
                    if (peer != null && peer.Connected)
                    {
                        if (peer.NextExpectedSeq <= 1)
                        {
                            RawSend(from, new[] { (byte)PacketKind.Accept }); // Accept が届かなかった時の再送
                            return;
                        }
                        // 既にデータをやり取りした相手からの Connect ＝ 相手が再起動して繋ぎ直した。古い接続を捨てる。
                        DropPeer(peer, "reconnect", false);
                        peer = null;
                    }
                    int connected = 0;
                    foreach (var p in peers.Values) if (p.Connected) connected++;
                    if (connected >= maxPeers)
                    {
                        SendReject(from, "full");
                        return;
                    }
                    peer = GetOrCreatePeer(from);
                    peer.Connected = true;
                    peer.LastReceive = now;
                    RawSend(from, new[] { (byte)PacketKind.Accept });
                    peer.LastSend = now;
                    PeerConnected?.Invoke(peer.Id);
                    return;
                }
                case PacketKind.Accept:
                {
                    if (IsHost || peer == null || peer.Connected) return;
                    peer.Connected = true;
                    peer.LastReceive = now;
                    State = TransportState.Connected;
                    StatusText = "UDP → " + peer.Id;
                    PeerConnected?.Invoke(peer.Id);
                    return;
                }
                case PacketKind.Reject:
                {
                    if (IsHost || peer == null) return;
                    string reason = data.Length > 1 ? System.Text.Encoding.UTF8.GetString(data, 1, data.Length - 1) : "rejected";
                    State = TransportState.Failed;
                    StatusText = "接続を断られました: " + reason;
                    peers.Remove(peer.Id);
                    PeerDisconnected?.Invoke(peer.Id, "rejected:" + reason);
                    return;
                }
                case PacketKind.Disconnect:
                {
                    if (peer == null) return;
                    DropPeer(peer, "remote_closed", false);
                    return;
                }
            }

            if (peer == null || !peer.Connected) return;
            peer.LastReceive = now;

            switch (kind)
            {
                case PacketKind.Ping:
                    return;
                case PacketKind.Ack:
                {
                    if (data.Length < 3) return;
                    int n = FragmentAssembler.ReadU16(data, 1);
                    for (int i = 0; i < n; i++)
                    {
                        int o = 3 + i * 4;
                        if (o + 4 > data.Length) break;
                        peer.InFlight.Remove(FragmentAssembler.ReadU32(data, o));
                    }
                    Flush(peer);
                    return;
                }
                case PacketKind.Reliable:
                {
                    if (data.Length < ReliableHeader) return;
                    uint seq = FragmentAssembler.ReadU32(data, 1);
                    peer.AcksToSend.Add(seq);
                    if (seq < peer.NextExpectedSeq) return; // 重複（ACK だけ返す）
                    if (seq == peer.NextExpectedSeq)
                    {
                        DeliverReliable(peer, data, ReliableHeader, data.Length - ReliableHeader);
                        peer.NextExpectedSeq++;
                        while (peer.OutOfOrder.TryGetValue(peer.NextExpectedSeq, out var buffered))
                        {
                            peer.OutOfOrder.Remove(peer.NextExpectedSeq);
                            DeliverReliable(peer, buffered, ReliableHeader, buffered.Length - ReliableHeader);
                            peer.NextExpectedSeq++;
                        }
                    }
                    else if (peer.OutOfOrder.Count < MaxOutOfOrder && !peer.OutOfOrder.ContainsKey(seq))
                    {
                        peer.OutOfOrder[seq] = data;
                    }
                    return;
                }
                case PacketKind.Unreliable:
                {
                    if (data.Length < 2) return;
                    var payload = new byte[data.Length - 1];
                    Buffer.BlockCopy(data, 1, payload, 0, payload.Length);
                    Received?.Invoke(peer.Id, payload);
                    return;
                }
            }
        }

        private void DeliverReliable(Peer peer, byte[] packet, int offset, int length)
        {
            var msg = peer.Assembler.Accept(packet, offset, length, now);
            if (msg != null) Received?.Invoke(peer.Id, msg);
        }

        public void Send(string peerId, byte[] data, NetDelivery delivery)
        {
            if (socket == null || data == null || peerId == null) return;
            if (!peers.TryGetValue(peerId, out var p) || !p.Connected) return;
            if (delivery == NetDelivery.Unreliable)
            {
                if (data.Length + 1 > MaxDatagram)
                {
                    Debug.LogWarning($"[LocalUdpTransport] unreliable データが大きすぎます（{data.Length}バイト）。破棄しました。");
                    return;
                }
                var pkt = new byte[data.Length + 1];
                pkt[0] = (byte)PacketKind.Unreliable;
                Buffer.BlockCopy(data, 0, pkt, 1, data.Length);
                RawSend(p.EndPoint, pkt);
                p.LastSend = now;
                return;
            }
            foreach (var frag in FragmentAssembler.Split(data, MaxDatagram - ReliableHeader, p.NextMessageId++))
                p.SendQueue.Enqueue(frag);
            Flush(p);
        }

        /// <summary>送信待ちの断片を送信窓の範囲で送り出す。</summary>
        private void Flush(Peer p)
        {
            while (p.SendQueue.Count > 0 && p.InFlight.Count < MaxInFlight)
            {
                var frag = p.SendQueue.Dequeue();
                uint seq = p.NextSendSeq++;
                var pkt = new byte[ReliableHeader + frag.Length];
                pkt[0] = (byte)PacketKind.Reliable;
                FragmentAssembler.WriteU32(pkt, 1, seq);
                Buffer.BlockCopy(frag, 0, pkt, ReliableHeader, frag.Length);
                p.InFlight[seq] = new Pending { Packet = pkt, LastSent = now, Tries = 0 };
                RawSend(p.EndPoint, pkt);
                p.LastSend = now;
            }
        }

        private void SendAcks(Peer p)
        {
            if (p.AcksToSend.Count == 0) return;
            int index = 0;
            while (index < p.AcksToSend.Count)
            {
                int n = Math.Min(200, p.AcksToSend.Count - index);
                var pkt = new byte[3 + n * 4];
                pkt[0] = (byte)PacketKind.Ack;
                FragmentAssembler.WriteU16(pkt, 1, (ushort)n);
                for (int i = 0; i < n; i++) FragmentAssembler.WriteU32(pkt, 3 + i * 4, p.AcksToSend[index + i]);
                RawSend(p.EndPoint, pkt);
                index += n;
            }
            p.AcksToSend.Clear();
            p.LastSend = now;
        }

        private void SendReject(IPEndPoint to, string reason)
        {
            var r = System.Text.Encoding.UTF8.GetBytes(reason ?? "");
            var pkt = new byte[1 + r.Length];
            pkt[0] = (byte)PacketKind.Reject;
            Buffer.BlockCopy(r, 0, pkt, 1, r.Length);
            RawSend(to, pkt);
        }

        private void RawSend(IPEndPoint to, byte[] packet)
        {
            try
            {
                socket?.SendTo(packet, to);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[LocalUdpTransport] 送信エラー: " + e.Message);
            }
        }

        public void DisconnectPeer(string peerId)
        {
            if (peerId != null && peers.TryGetValue(peerId, out var p)) DropPeer(p, "local_closed", true);
        }

        private void DropPeer(Peer p, string reason, bool notifyRemote)
        {
            if (notifyRemote && p.EndPoint != null)
            {
                RawSend(p.EndPoint, new[] { (byte)PacketKind.Disconnect });
                RawSend(p.EndPoint, new[] { (byte)PacketKind.Disconnect });
            }
            bool wasConnected = p.Connected;
            p.Connected = false;
            peers.Remove(p.Id);
            if (!IsHost)
            {
                State = TransportState.Closed;
                StatusText = "切断: " + reason;
            }
            if (wasConnected) PeerDisconnected?.Invoke(p.Id, reason);
        }

        public void Shutdown()
        {
            foreach (var p in new List<Peer>(peers.Values))
            {
                if (p.Connected && p.EndPoint != null)
                {
                    // 送信待ちの reliable を可能な限り吐き出してから切断を伝える（HostClosing / Leave を届けるため）
                    while (p.SendQueue.Count > 0)
                    {
                        var frag = p.SendQueue.Dequeue();
                        var pkt = new byte[ReliableHeader + frag.Length];
                        pkt[0] = (byte)PacketKind.Reliable;
                        FragmentAssembler.WriteU32(pkt, 1, p.NextSendSeq++);
                        Buffer.BlockCopy(frag, 0, pkt, ReliableHeader, frag.Length);
                        RawSend(p.EndPoint, pkt);
                    }
                    RawSend(p.EndPoint, new[] { (byte)PacketKind.Disconnect });
                    RawSend(p.EndPoint, new[] { (byte)PacketKind.Disconnect });
                }
            }
            peers.Clear();
            CloseSocket();
            State = TransportState.Closed;
        }

        private void CloseSocket()
        {
            running = false;
            try { socket?.Close(); } catch { }
            socket = null;
            try
            {
                if (receiveThread != null && receiveThread.IsAlive && Thread.CurrentThread != receiveThread) receiveThread.Join(1000);
            }
            catch { }
            receiveThread = null;
            while (incoming.TryDequeue(out _)) { }
        }

        public void Dispose() => Shutdown();

        private Peer GetOrCreatePeer(IPEndPoint ep)
        {
            string key = KeyOf(ep);
            if (!peers.TryGetValue(key, out var p))
            {
                p = new Peer { Id = key, EndPoint = ep, LastReceive = now, LastSend = now };
                peers[key] = p;
            }
            return p;
        }

        private static string KeyOf(IPEndPoint ep) => ep.Address + ":" + ep.Port;
    }
}
