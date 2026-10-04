using System;
using System.Globalization;
using System.IO;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The hand log (documents/ATLAS_Hands_Design.md section 4.5): a text header line, "ATLAS-HANDLOG 1" and a
    /// line feed, then one binary record per detection, little-endian, <see cref="RecordLength"/> bytes each:
    ///
    ///   time           double    seconds on the app clock of the poll, counted from the first record of the file
    ///   head position  3 floats  the head pose of the frame that polled the result (the room frame when a room is
    ///                            active, the AR session's otherwise)
    ///   head rotation  4 floats  x y z w
    ///   detection      HandsDetection.NativeSize bytes: the struct exactly as the plugin lays it out
    ///
    /// A record holds joint positions and depths and no image (rule 10). The headless tests of the lift, the
    /// filter, touch and the mask play logs through <see cref="ReplayHandsSource"/>; the phone writes one through
    /// <see cref="HandsLogRecorder"/> when the developer parameter hands.record is on. Like a pose log it is
    /// motion data of a person: record one only with their agreement, and commit one only with it.
    /// </summary>
    public static unsafe class HandsLog
    {
        /// <summary>The header line without its line feed. The number is the version of the record layout.</summary>
        public const string Magic = "ATLAS-HANDLOG 1";
        /// <summary>The version in the header: it changes whenever the layout of a record does, HandsDetection included.</summary>
        public const int FormatVersion = 1;
        /// <summary>Bytes of the header: <see cref="Magic"/> and a line feed.</summary>
        public const int HeaderLength = 16;
        /// <summary>Bytes of a record before the detection: time, head position, head rotation.</summary>
        public const int HeadLength = 8 + 3 * 4 + 4 * 4;
        /// <summary>Bytes of a record.</summary>
        public const int RecordLength = HeadLength + HandsDetection.NativeSize;

        /// <summary>Where the version and the hand count sit in a detection (the reader checks both of every record).</summary>
        internal static readonly int VersionOffset = OffsetOfVersion();
        internal static readonly int HandCountOffset = OffsetOfHandCount();

        static int OffsetOfVersion()
        {
            HandsDetection d = default(HandsDetection);
            return (int)((byte*)&d.Version - (byte*)&d);
        }

        static int OffsetOfHandCount()
        {
            HandsDetection d = default(HandsDetection);
            return (int)((byte*)&d.HandCount - (byte*)&d);
        }

        /// <summary>The header line as bytes, line feed included.</summary>
        public static byte[] HeaderBytes()
        {
            var header = new byte[HeaderLength];
            for (int i = 0; i < Magic.Length; i++) header[i] = (byte)Magic[i];
            header[HeaderLength - 1] = (byte)'\n';
            return header;
        }

        /// <summary>
        /// Writes one record into buffer at offset: <see cref="RecordLength"/> bytes. Allocates nothing; the
        /// recorder calls it on the frame. ArgumentOutOfRangeException when the record does not fit.
        /// </summary>
        public static void Encode(byte[] buffer, int offset, double time, float3 headPosition, quaternion headRotation,
                                  in HandsDetection detection)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length - RecordLength) throw new ArgumentOutOfRangeException(nameof(offset));
            fixed (byte* p = &buffer[offset])
            fixed (HandsDetection* d = &detection)
            {
                // Byte copies: a record starts at any multiple of its length, not at the alignment of a double.
                Buffer.MemoryCopy(&time, p, 8, 8);
                Buffer.MemoryCopy(&headPosition, p + 8, 12, 12);
                Buffer.MemoryCopy(&headRotation, p + 20, 16, 16);
                Buffer.MemoryCopy(d, p + HeadLength, HandsDetection.NativeSize, HandsDetection.NativeSize);
            }
        }

        /// <summary>Reads the record at offset of buffer. The reverse of <see cref="Encode"/>; allocates nothing.</summary>
        public static void Decode(byte[] buffer, int offset, out double time, out float3 headPosition,
                                  out quaternion headRotation, ref HandsDetection detection)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length - RecordLength) throw new ArgumentOutOfRangeException(nameof(offset));
            double t;
            float3 position;
            quaternion rotation;
            fixed (byte* p = &buffer[offset])
            fixed (HandsDetection* d = &detection)
            {
                Buffer.MemoryCopy(p, &t, 8, 8);
                Buffer.MemoryCopy(p + 8, &position, 12, 12);
                Buffer.MemoryCopy(p + 20, &rotation, 16, 16);
                Buffer.MemoryCopy(p + HeadLength, d, HandsDetection.NativeSize, HandsDetection.NativeSize);
            }
            time = t;
            headPosition = position;
            headRotation = rotation;
        }

        /// <summary>The time of the record at offset of buffer, without reading the rest.</summary>
        internal static double TimeAt(byte[] buffer, int offset)
        {
            return BitConverter.ToDouble(buffer, offset);
        }

        /// <summary>ArgumentException unless the time can be written to a log after one at last.</summary>
        internal static void CheckTime(double time, double last)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
                throw new ArgumentException("A hand log time must be a number, not " + time.ToString(CultureInfo.InvariantCulture), nameof(time));
            if (time < last)
                throw new ArgumentException("A hand log's time never goes back: " + time.ToString("R", CultureInfo.InvariantCulture) +
                                            " after " + last.ToString("R", CultureInfo.InvariantCulture), nameof(time));
        }
    }

    /// <summary>
    /// Writes a hand log to a stream (<see cref="HandsLog"/>). The header goes out at once, so a log with no
    /// record is a valid file. Not thread-safe; one writer for one stream. Disposing it flushes the stream and,
    /// unless it was told to leave it open, closes it.
    /// </summary>
    public sealed class HandsLogWriter : IDisposable
    {
        readonly Stream _stream;
        readonly bool _leaveOpen;
        readonly byte[] _record = new byte[HandsLog.RecordLength];
        double _last = double.NegativeInfinity;
        int _count;
        bool _disposed;

        /// <summary>Writes the header to the stream at once. Disposing the writer closes the stream.</summary>
        public HandsLogWriter(Stream stream) : this(stream, false)
        {
        }

        /// <summary>leaveOpen keeps the stream open when the writer is disposed.</summary>
        public HandsLogWriter(Stream stream, bool leaveOpen)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanWrite) throw new ArgumentException("The stream cannot be written to", nameof(stream));
            _stream = stream;
            _leaveOpen = leaveOpen;
            byte[] header = HandsLog.HeaderBytes();
            stream.Write(header, 0, header.Length);
        }

        /// <summary>Records written so far.</summary>
        public int Count { get { return _count; } }

        /// <summary>
        /// Appends one record: the app-clock time of the poll, the head pose of the frame that polled, and the
        /// detection as it came. ArgumentException for a time that is not a number or is earlier than the last
        /// one written: the reader would refuse the file.
        /// </summary>
        public void Write(double time, float3 headPosition, quaternion headRotation, in HandsDetection detection)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandsLogWriter));
            HandsLog.CheckTime(time, _last);
            HandsLog.Encode(_record, 0, time, headPosition, headRotation, in detection);
            _stream.Write(_record, 0, _record.Length);
            _last = time;
            _count++;
        }

        /// <summary>Appends a record that is already encoded (the recorder's thread does: the frame encoded it).</summary>
        internal void WriteEncoded(byte[] buffer, int offset)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandsLogWriter));
            double time = HandsLog.TimeAt(buffer, offset);
            HandsLog.CheckTime(time, _last);
            _stream.Write(buffer, offset, HandsLog.RecordLength);
            _last = time;
            _count++;
        }

        /// <summary>Pushes what has been written on to the stream's own storage.</summary>
        public void Flush()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(HandsLogWriter));
            _stream.Flush();
        }

        /// <summary>Flushes the stream and, unless the writer was told to leave it open, closes it.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _stream.Flush(); }
            finally
            {
                if (!_leaveOpen) _stream.Dispose();
            }
        }
    }

    /// <summary>
    /// A hand log in memory (<see cref="HandsLog"/>). The constructor checks the header and every record's time
    /// (a finite number that does not go back), detection version and hand count, so a record that was read can be
    /// trusted. It does not copy the bytes: do not change the array afterwards. Reading allocates nothing.
    /// A file that ends inside a record, as when a phone died while writing, is read as far as its last whole record.
    /// </summary>
    public sealed class HandsLogReader
    {
        readonly byte[] _bytes;
        readonly int _count;
        readonly int _trailing;

        /// <summary>FormatException when the bytes are not a hand log of this version, or a record is damaged; the message names it.</summary>
        public HandsLogReader(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            _bytes = bytes;
            CheckHeader(bytes);

            int body = bytes.Length - HandsLog.HeaderLength;
            _count = body / HandsLog.RecordLength;
            _trailing = body - _count * HandsLog.RecordLength;

            double last = double.NegativeInfinity;
            for (int i = 0; i < _count; i++)
            {
                int at = HandsLog.HeaderLength + i * HandsLog.RecordLength;
                double time = HandsLog.TimeAt(bytes, at);
                if (double.IsNaN(time) || double.IsInfinity(time))
                    throw new FormatException("Hand log record " + i + ": the time is not a number");
                if (time < last)
                    throw new FormatException("Hand log record " + i + ": time goes back, " + time.ToString("R", CultureInfo.InvariantCulture) +
                                              " after " + last.ToString("R", CultureInfo.InvariantCulture));
                last = time;
                int version = BitConverter.ToInt32(bytes, at + HandsLog.HeadLength + HandsLog.VersionOffset);
                if (version != HandsDetection.CurrentVersion)
                    throw new FormatException("Hand log record " + i + ": detection version " + version + " is not supported");
                int hands = BitConverter.ToInt32(bytes, at + HandsLog.HeadLength + HandsLog.HandCountOffset);
                if (hands < 0 || hands > HandsDetection.MaxHands)
                    throw new FormatException("Hand log record " + i + ": " + hands + " hands");
            }
        }

        /// <summary>Reads a hand-log file. Throws what File.ReadAllBytes and the constructor throw.</summary>
        public static HandsLogReader FromFile(string path)
        {
            return new HandsLogReader(File.ReadAllBytes(path));
        }

        /// <summary>Whole records in the log.</summary>
        public int Count { get { return _count; } }

        /// <summary>Bytes after the last whole record: 0 for a file that was closed properly.</summary>
        public int TrailingBytes { get { return _trailing; } }

        /// <summary>Seconds from the first record to the last, 0 for a log of fewer than two.</summary>
        public double Duration { get { return _count > 1 ? Time(_count - 1) - Time(0) : 0.0; } }

        /// <summary>The time of a record: the app clock of its poll, as the writer was given it (the recorder counts from the first record).</summary>
        public double Time(int index)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            return HandsLog.TimeAt(_bytes, HandsLog.HeaderLength + index * HandsLog.RecordLength);
        }

        /// <summary>
        /// Copies a record into detection and its head pose into the two out arguments. Allocates nothing.
        /// ArgumentOutOfRangeException for an index outside 0 to Count - 1.
        /// </summary>
        public void Read(int index, ref HandsDetection detection, out float3 headPosition, out quaternion headRotation)
        {
            if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException(nameof(index));
            HandsLog.Decode(_bytes, HandsLog.HeaderLength + index * HandsLog.RecordLength, out double _, out headPosition,
                            out headRotation, ref detection);
        }

        static void CheckHeader(byte[] bytes)
        {
            int end = -1;
            int limit = Math.Min(bytes.Length, 64);
            for (int i = 0; i < limit; i++)
                if (bytes[i] == (byte)'\n') { end = i; break; }
            if (end < 0) throw new FormatException("Hand log: the file does not start with \"" + HandsLog.Magic + "\"");

            string line = System.Text.Encoding.ASCII.GetString(bytes, 0, end);
            string[] parts = line.Split(' ');
            if (parts.Length != 2 || parts[0] != "ATLAS-HANDLOG")
                throw new FormatException("Hand log: the file does not start with \"" + HandsLog.Magic + "\"");
            int version;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
                throw new FormatException("Hand log: the file does not start with \"" + HandsLog.Magic + "\"");
            if (version != HandsLog.FormatVersion)
                throw new FormatException("Hand log: version " + version + " is not supported");
            if (end + 1 != HandsLog.HeaderLength)
                throw new FormatException("Hand log: the header is not \"" + HandsLog.Magic + "\"");
        }
    }
}
