using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace Uchujin.Network
{
    /// <summary>
    /// メッセージの符号化：[種別 1バイト][フラグ 1バイト][本体]。
    /// 本体は JsonUtility の JSON（UTF-8）。NetConfig.CompressThresholdBytes を超えたら GZip 圧縮（フラグ bit0）。
    /// 位置（Transform）だけは頻度が高いので簡易バイナリ。
    /// </summary>
    public static class NetCodec
    {
        private const byte FlagGzip = 1;

        public static byte[] Encode(NetMsg type, object payload)
        {
            string json = payload != null ? JsonUtility.ToJson(payload) : "{}";
            return EncodeRaw(type, Encoding.UTF8.GetBytes(json));
        }

        public static byte[] EncodeRaw(NetMsg type, byte[] body)
        {
            byte flags = 0;
            if (body.Length > NetConfig.CompressThresholdBytes)
            {
                try
                {
                    var z = Compress(body);
                    if (z.Length < body.Length)
                    {
                        body = z;
                        flags |= FlagGzip;
                    }
                }
                catch (Exception e)
                {
                    // GZip が使えない環境（Unity上で要確認：プラットフォームによっては未対応）では非圧縮で送る
                    Debug.LogWarning("[NetCodec] 圧縮に失敗（非圧縮で送信）: " + e.Message);
                }
            }
            var data = new byte[body.Length + 2];
            data[0] = (byte)type;
            data[1] = flags;
            Buffer.BlockCopy(body, 0, data, 2, body.Length);
            return data;
        }

        public static bool TryDecodeHeader(byte[] data, out NetMsg type, out byte[] body)
        {
            type = 0;
            body = null;
            if (data == null || data.Length < 2) return false;
            type = (NetMsg)data[0];
            byte flags = data[1];
            body = new byte[data.Length - 2];
            Buffer.BlockCopy(data, 2, body, 0, body.Length);
            if ((flags & FlagGzip) != 0)
            {
                try { body = Decompress(body); }
                catch (Exception e)
                {
                    Debug.LogWarning("[NetCodec] 展開に失敗: " + e.Message);
                    return false;
                }
            }
            return true;
        }

        public static T Parse<T>(byte[] body) where T : class
        {
            try
            {
                return JsonUtility.FromJson<T>(Encoding.UTF8.GetString(body));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetCodec] {typeof(T).Name} の解析に失敗: {e.Message}");
                return null;
            }
        }

        private static byte[] Compress(byte[] data)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionMode.Compress, true)) gz.Write(data, 0, data.Length);
                return ms.ToArray();
            }
        }

        private static byte[] Decompress(byte[] data)
        {
            using (var input = new MemoryStream(data))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                gz.CopyTo(output);
                return output.ToArray();
            }
        }

        // ---- 位置（unreliable、約10Hz） ----

        public struct TransformSample
        {
            public string PlayerId;
            public Vector3 Position;
            public float RotY;
            public string AreaId;
            public bool Moving;
            public uint Sequence;
        }

        public static byte[] EncodeTransform(TransformSample s)
        {
            using (var ms = new MemoryStream(96))
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write((byte)NetMsg.Transform);
                w.Write((byte)0);
                w.Write(s.PlayerId ?? "");
                w.Write(s.Sequence);
                w.Write(s.Position.x);
                w.Write(s.Position.y);
                w.Write(s.Position.z);
                w.Write(s.RotY);
                w.Write(s.AreaId ?? "");
                w.Write(s.Moving);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static bool TryDecodeTransform(byte[] data, out TransformSample s)
        {
            s = default;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    r.ReadByte();
                    r.ReadByte();
                    s.PlayerId = r.ReadString();
                    s.Sequence = r.ReadUInt32();
                    s.Position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    s.RotY = r.ReadSingle();
                    s.AreaId = r.ReadString();
                    s.Moving = r.ReadBoolean();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
