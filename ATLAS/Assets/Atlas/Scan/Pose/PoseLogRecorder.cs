using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Atlas.World;

namespace Atlas.Scan
{
    /// <summary>
    /// Writes the raw head stream to a pose log (contract page lane C, section 7.6). The frame path only puts
    /// a sample into a ring; a thread of the recorder's own formats the lines and writes the file, so the
    /// frame never waits for storage. A pose log is motion data of a person: nothing is recorded unless it
    /// is switched on.
    /// </summary>
    public sealed class PoseLogRecorder : IDisposable
    {
        /// <summary>Samples the ring holds: more than a minute at 60 Hz.</summary>
        public const int RingSize = 4096;
        const int RingMask = RingSize - 1;

        readonly HeadPose[] _ring = new HeadPose[RingSize];
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        // One producer (the frame) and one consumer (the writer thread): each index has a single writer.
        volatile int _head;
        volatile int _tail;
        volatile bool _recording;
        volatile bool _stop;
        Thread _thread;
        TextWriter _writer;
        double _firstTime;
        double _lastWritten;
        bool _haveFirst;
        int _sinceWake;
        int _dropped;
        volatile string _error;

        /// <summary>Folder that Enabled writes into. Set by the installer (persistentDataPath/logs on the phone).</summary>
        public string Directory { get; set; } = "";
        /// <summary>The header's room line for a log started through Enabled.</summary>
        public string RoomId { get; set; } = "";
        /// <summary>The header's frame line for a log started through Enabled.</summary>
        public PoseFrameKind Frame { get; set; } = PoseFrameKind.Room;
        /// <summary>The header's device line; may be empty.</summary>
        public string Device { get; set; } = "";

        public bool IsRecording { get { return _recording; } }
        /// <summary>The file being written, or the last one written.</summary>
        public string Path { get; private set; } = "";
        /// <summary>Samples lost because the writer fell a whole ring behind.</summary>
        public int Dropped { get { return _dropped; } }
        /// <summary>The message of a write failure, or null. A failure ends the recording.</summary>
        public string Error { get { return _error; } }

        /// <summary>
        /// The pose.record switch. Turning it on starts "walk_&lt;UTC time&gt;.poselog" in Directory; turning it
        /// off closes the file.
        /// </summary>
        public bool Enabled
        {
            get { return _recording; }
            set
            {
                if (value == _recording) return;
                if (!value) { Stop(); return; }
                DateTime now = DateTime.UtcNow;
                string name = "walk_" + now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".poselog";
                string folder = string.IsNullOrEmpty(Directory) ? "." : Directory;
                Start(System.IO.Path.Combine(folder, name), RoomId, Frame,
                    now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), Device);
            }
        }

        /// <summary>Starts a log at a path. False when the file cannot be created (Error says why).</summary>
        public bool Start(string path, string roomId, PoseFrameKind frame, string createdUtc, string device = null)
        {
            Stop();
            try
            {
                string folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) System.IO.Directory.CreateDirectory(folder);
                _writer = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 16);
                PoseLog.WriteHeader(_writer, roomId, frame, createdUtc, device);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is NotSupportedException)
            {
                _error = e.Message;
                CloseWriter();
                return false;
            }
            Path = path;
            _error = null;
            _dropped = 0;
            _head = 0;
            _tail = 0;
            _haveFirst = false;
            _lastWritten = 0.0;
            _sinceWake = 0;
            _stop = false;
            _recording = true;
            _thread = new Thread(WriterLoop) { IsBackground = true, Name = "Atlas pose log" };
            _thread.Start();
            return true;
        }

        /// <summary>
        /// One raw sample, at the end of frame-loop step 1. Allocates nothing and never blocks. A sample that
        /// finds the ring full is dropped and counted.
        /// </summary>
        public void Record(in HeadPose raw)
        {
            if (!_recording) return;
            int head = _head;
            if (head - _tail >= RingSize)
            {
                _dropped++;
                return;
            }
            _ring[head & RingMask] = raw;
            _head = head + 1;
            // Waking the writer about once a second keeps the frame path to one array write.
            if (++_sinceWake >= 64)
            {
                _sinceWake = 0;
                _wake.Set();
            }
        }

        /// <summary>Writes what is in the ring, closes the file and ends the writer thread.</summary>
        public void Stop()
        {
            Thread thread = _thread;
            if (thread == null) return;
            _recording = false;
            _stop = true;
            _wake.Set();
            thread.Join();
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        void WriterLoop()
        {
            try
            {
                while (true)
                {
                    bool stopping = _stop;      // read before draining, so nothing recorded before Stop is lost
                    Drain();
                    if (stopping) break;
                    _wake.WaitOne(250);
                }
                _writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException)
            {
                _error = e.Message;
                _recording = false;
            }
            finally
            {
                CloseWriter();
            }
        }

        void Drain()
        {
            int head = _head;
            int tail = _tail;
            while (tail != head)
            {
                HeadPose sample = _ring[tail & RingMask];
                if (!_haveFirst)
                {
                    _haveFirst = true;
                    _firstTime = sample.Time;
                }
                double t = sample.Time - _firstTime;
                // The reader refuses a time that runs backwards, so a clock that was set back is held.
                if (t < _lastWritten) t = _lastWritten;
                _lastWritten = t;
                _writer.Write(PoseLog.FormatLine(t, in sample));
                _writer.Write('\n');
                tail++;
                _tail = tail;
            }
        }

        void CloseWriter()
        {
            TextWriter writer = _writer;
            _writer = null;
            if (writer == null) return;
            try { writer.Dispose(); }
            catch (IOException e) { _error = e.Message; }
        }
    }
}
