using System;

namespace Atlas.Link
{
    /// <summary>Message ids of link schema v1 (Master Plan Appendix B). Byte 0 of every packet.</summary>
    public static class MessageId
    {
        public const byte Hello = 0x01;
        public const byte Welcome = 0x02;
        public const byte TPing = 0x03;
        public const byte TPong = 0x04;
        public const byte State = 0x10;
        /// <summary>
        /// Reserved for Phase 3 (P3-D07): a peer's State relayed by the Hub. Phase 1 has no layout for it and a
        /// receiver ignores the id.
        /// </summary>
        public const byte PeerState = 0x11;
        public const byte ParamManifest = 0x20;
        public const byte ParamSet = 0x21;
        public const byte SceneCmd = 0x22;
        public const byte OperatorCmd = 0x23;
        public const byte Telemetry = 0x30;
        public const byte Event = 0x40;
        public const byte RoomOutline = 0x41;
        public const byte Log = 0x42;
        public const byte JobStatus = 0x50;
        public const byte AssetReady = 0x51;
        public const byte AssetAck = 0x52;
        public const byte Bye = 0x7F;

        /// <summary>True for an id this version has a codec for. <see cref="PeerState"/> is not one of them.</summary>
        public static bool IsKnown(byte id)
        {
            switch (id)
            {
                case Hello: case Welcome: case TPing: case TPong: case State:
                case ParamManifest: case ParamSet: case SceneCmd: case OperatorCmd:
                case Telemetry: case Event: case RoomOutline: case Log:
                case JobStatus: case AssetReady: case AssetAck: case Bye:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>True for the messages sent as id byte + UTF-8 JSON.</summary>
        public static bool IsJson(byte id)
        {
            switch (id)
            {
                case Hello: case Welcome: case ParamManifest: case SceneCmd: case OperatorCmd:
                case Event: case Log: case JobStatus: case AssetReady: case AssetAck: case Bye:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The schema's name for an id, or null for an unknown id.</summary>
        public static string Name(byte id)
        {
            switch (id)
            {
                case Hello: return "Hello";
                case Welcome: return "Welcome";
                case TPing: return "TPing";
                case TPong: return "TPong";
                case State: return "State";
                case PeerState: return "PeerState";
                case ParamManifest: return "ParamManifest";
                case ParamSet: return "ParamSet";
                case SceneCmd: return "SceneCmd";
                case OperatorCmd: return "OperatorCmd";
                case Telemetry: return "Telemetry";
                case Event: return "Event";
                case RoomOutline: return "RoomOutline";
                case Log: return "Log";
                case JobStatus: return "JobStatus";
                case AssetReady: return "AssetReady";
                case AssetAck: return "AssetAck";
                case Bye: return "Bye";
                default: return null;
            }
        }
    }

    /// <summary>Constants of the link that both ends must agree on (Master Plan sections 7.1 and 7.2).</summary>
    public static class LinkProtocol
    {
        public const int ProtoMajor = 1;
        public const int ProtoMinor = 0;
        public const string ConnectionKey = "atlas-v1";
        public const int DefaultPort = 9050;

        public const byte ChannelControl = 0;   // ReliableOrdered
        public const byte ChannelState = 1;     // Sequenced
        public const byte ChannelsCount = 2;

        /// <summary>No link message may reach the 1,024-byte initial MTU unfragmented; this is the scratch size.</summary>
        public const int MaxBinaryMessageBytes = 2048;
    }

    /// <summary>
    /// Little-endian packing into a byte array, written out by hand so that it behaves the same on Mono, IL2CPP
    /// and .NET 8 whatever the CPU's byte order, with no unsafe code and no allocation.
    /// </summary>
    public static class Wire
    {
        public static void PutU16(byte[] b, ref int o, ushort v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            o += 2;
        }

        public static void PutI16(byte[] b, ref int o, short v) { PutU16(b, ref o, (ushort)v); }

        public static void PutU32(byte[] b, ref int o, uint v)
        {
            b[o] = (byte)v;
            b[o + 1] = (byte)(v >> 8);
            b[o + 2] = (byte)(v >> 16);
            b[o + 3] = (byte)(v >> 24);
            o += 4;
        }

        public static void PutI64(byte[] b, ref int o, long v)
        {
            ulong u = (ulong)v;
            b[o] = (byte)u;
            b[o + 1] = (byte)(u >> 8);
            b[o + 2] = (byte)(u >> 16);
            b[o + 3] = (byte)(u >> 24);
            b[o + 4] = (byte)(u >> 32);
            b[o + 5] = (byte)(u >> 40);
            b[o + 6] = (byte)(u >> 48);
            b[o + 7] = (byte)(u >> 56);
            o += 8;
        }

        public static void PutF32(byte[] b, ref int o, float v)
        {
            PutU32(b, ref o, (uint)BitConverter.SingleToInt32Bits(v));
        }

        public static void PutF64(byte[] b, ref int o, double v)
        {
            PutI64(b, ref o, BitConverter.DoubleToInt64Bits(v));
        }

        public static ushort GetU16(byte[] b, ref int o)
        {
            ushort v = (ushort)(b[o] | (b[o + 1] << 8));
            o += 2;
            return v;
        }

        public static short GetI16(byte[] b, ref int o) { return (short)GetU16(b, ref o); }

        public static uint GetU32(byte[] b, ref int o)
        {
            uint v = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) | ((uint)b[o + 3] << 24);
            o += 4;
            return v;
        }

        public static long GetI64(byte[] b, ref int o)
        {
            uint lo = (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) | ((uint)b[o + 3] << 24);
            uint hi = (uint)(b[o + 4] | (b[o + 5] << 8) | (b[o + 6] << 16)) | ((uint)b[o + 7] << 24);
            o += 8;
            return (long)(((ulong)hi << 32) | lo);
        }

        public static float GetF32(byte[] b, ref int o)
        {
            return BitConverter.Int32BitsToSingle((int)GetU32(b, ref o));
        }

        public static double GetF64(byte[] b, ref int o)
        {
            return BitConverter.Int64BitsToDouble(GetI64(b, ref o));
        }

        /// <summary>True when <paramref name="count"/> bytes from <paramref name="offset"/> lie inside the buffer.</summary>
        public static bool InRange(byte[] buffer, int offset, int count)
        {
            return buffer != null && offset >= 0 && count >= 0 && offset <= buffer.Length - count;
        }
    }
}
