using System;
using System.Collections.Generic;

namespace Uchujin.Network
{
    /// <summary>
    /// 大きなメッセージを1パケットに収まる大きさへ分割し、受信側で再構成する。
    /// EOS P2P は1パケット約1170バイトが上限（EOS_P2P_MAX_PACKET_SIZE）なので、スナップショットや辞書の受け渡しに必須。
    /// 分割ヘッダ（8バイト）：[メッセージ番号 u32][断片番号 u16][断片数 u16]（リトルエンディアン）。
    /// </summary>
    public sealed class FragmentAssembler
    {
        public const int HeaderSize = 8;
        /// <summary>未完成の分割メッセージを捨てるまでの秒数。</summary>
        public const float PartialTimeout = 60f;

        private sealed class Partial
        {
            public byte[][] parts;
            public int received;
            public int totalBytes;
            public float lastTime;
        }

        private readonly Dictionary<uint, Partial> partials = new Dictionary<uint, Partial>();

        /// <summary>data を分割する。各断片の長さは maxPacket 以下。</summary>
        public static List<byte[]> Split(byte[] data, int maxPacket, uint messageId)
        {
            int chunk = maxPacket - HeaderSize;
            if (chunk <= 0) throw new ArgumentException("maxPacket too small");
            int count = Math.Max(1, (data.Length + chunk - 1) / chunk);
            if (count > ushort.MaxValue) throw new ArgumentException("message too large to fragment: " + data.Length);
            var list = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int offset = i * chunk;
                int len = Math.Min(chunk, data.Length - offset);
                if (len < 0) len = 0;
                var packet = new byte[HeaderSize + len];
                WriteU32(packet, 0, messageId);
                WriteU16(packet, 4, (ushort)i);
                WriteU16(packet, 6, (ushort)count);
                if (len > 0) Buffer.BlockCopy(data, offset, packet, HeaderSize, len);
                list.Add(packet);
            }
            return list;
        }

        /// <summary>断片を受け取り、揃ったら元のメッセージを返す（未完成なら null）。</summary>
        public byte[] Accept(byte[] packet, int offset, int length, float now)
        {
            if (length < HeaderSize) return null;
            uint id = ReadU32(packet, offset);
            int index = ReadU16(packet, offset + 4);
            int count = ReadU16(packet, offset + 6);
            int payloadLen = length - HeaderSize;
            if (count <= 0 || index >= count) return null;

            if (count == 1)
            {
                var single = new byte[payloadLen];
                Buffer.BlockCopy(packet, offset + HeaderSize, single, 0, payloadLen);
                return single;
            }

            if (!partials.TryGetValue(id, out var p) || p.parts.Length != count)
            {
                p = new Partial { parts = new byte[count][] };
                partials[id] = p;
            }
            p.lastTime = now;
            if (p.parts[index] != null) return null; // 重複
            var part = new byte[payloadLen];
            Buffer.BlockCopy(packet, offset + HeaderSize, part, 0, payloadLen);
            p.parts[index] = part;
            p.received++;
            p.totalBytes += payloadLen;
            if (p.received < count) return null;

            partials.Remove(id);
            var result = new byte[p.totalBytes];
            int w = 0;
            for (int i = 0; i < count; i++)
            {
                Buffer.BlockCopy(p.parts[i], 0, result, w, p.parts[i].Length);
                w += p.parts[i].Length;
            }
            return result;
        }

        public void Cleanup(float now)
        {
            if (partials.Count == 0) return;
            List<uint> remove = null;
            foreach (var kv in partials)
            {
                if (now - kv.Value.lastTime > PartialTimeout)
                {
                    if (remove == null) remove = new List<uint>();
                    remove.Add(kv.Key);
                }
            }
            if (remove != null) foreach (var id in remove) partials.Remove(id);
        }

        public void Clear() => partials.Clear();

        // ---- バイト列ヘルパー（リトルエンディアン固定。BitConverter のエンディアン依存を避ける） ----

        public static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
        }

        public static uint ReadU32(byte[] b, int o) => (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));

        public static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
        }

        public static ushort ReadU16(byte[] b, int o) => (ushort)(b[o] | (b[o + 1] << 8));
    }
}
