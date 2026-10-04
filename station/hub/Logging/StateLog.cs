using System;
using System.Collections.Generic;
using System.IO;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// The layout of <c>logs/&lt;session&gt;/state.bin</c> (Master Plan section 7.3.3, lane D contract 2.12): a
    /// 16-byte header, then one fixed-size record per <c>State</c> packet, the Hub's receive time in microseconds
    /// followed by the packet's 202 bytes exactly as the phone sent them. The Hub writes it; ReplayPhone compiles
    /// this same file to read it.
    /// </summary>
    public static class StateLog
    {
        public const int HeaderSize = 16;
        public const ushort Version = 1;

        /// <summary>Hub receive time (i64, microseconds, little-endian) plus the raw packet.</summary>
        public const int RecordSize = 8 + StateMessage.Size;

        static readonly byte[] s_magic = { (byte)'A', (byte)'T', (byte)'L', (byte)'S', (byte)'T', (byte)'A', (byte)'T', (byte)'E' };

        /// <summary>One recorded packet.</summary>
        public struct Entry
        {
            public long HubUs;
            public StateMessage State;
        }

        public static void WriteHeader(Stream stream)
        {
            var header = new byte[HeaderSize];
            Buffer.BlockCopy(s_magic, 0, header, 0, s_magic.Length);
            int o = s_magic.Length;
            Wire.PutU16(header, ref o, Version);
            Wire.PutU16(header, ref o, (ushort)RecordSize);
            Wire.PutU32(header, ref o, 0u);
            stream.Write(header, 0, HeaderSize);
        }

        /// <summary>Fills a caller-owned record buffer. Allocates nothing.</summary>
        public static void PackRecord(byte[] record, long hubUs, byte[] raw, int offset)
        {
            int o = 0;
            Wire.PutI64(record, ref o, hubUs);
            Buffer.BlockCopy(raw, offset, record, o, StateMessage.Size);
        }

        /// <summary>Checks the header and leaves the stream at the first record.</summary>
        public static bool TryReadHeader(Stream stream, out string error)
        {
            var header = new byte[HeaderSize];
            if (ReadFully(stream, header, HeaderSize) != HeaderSize)
            {
                error = "the file is shorter than a state.bin header";
                return false;
            }
            for (int i = 0; i < s_magic.Length; i++)
            {
                if (header[i] == s_magic[i]) continue;
                error = "not a state.bin file (wrong magic)";
                return false;
            }
            int o = s_magic.Length;
            ushort version = Wire.GetU16(header, ref o);
            ushort recordSize = Wire.GetU16(header, ref o);
            if (version != Version || recordSize != RecordSize)
            {
                error = "state.bin version " + version + " with " + recordSize + "-byte records; this build reads version " +
                        Version + " with " + RecordSize + "-byte records";
                return false;
            }
            error = null;
            return true;
        }

        /// <summary>Reads the next record. False at the end of the file; a torn last record counts as the end.</summary>
        public static bool TryReadRecord(Stream stream, byte[] record, out Entry entry)
        {
            entry = default(Entry);
            if (ReadFully(stream, record, RecordSize) != RecordSize) return false;
            int o = 0;
            entry.HubUs = Wire.GetI64(record, ref o);
            return entry.State.ReadFrom(record, o, StateMessage.Size);
        }

        /// <summary>Reads a whole recording. Throws <see cref="InvalidDataException"/> when the header is wrong.</summary>
        public static List<Entry> ReadAll(string path)
        {
            var entries = new List<Entry>();
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                string error;
                if (!TryReadHeader(stream, out error)) throw new InvalidDataException(path + ": " + error);
                var record = new byte[RecordSize];
                Entry entry;
                while (TryReadRecord(stream, record, out entry)) entries.Add(entry);
            }
            return entries;
        }

        static int ReadFully(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = stream.Read(buffer, total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }
    }
}
