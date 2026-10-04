using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// Writes the detections the phone's source gives to a hand log (<see cref="HandsLog"/>), for the developer
    /// parameter hands.record and for the bench (documents/ATLAS_Hands_Design.md section 9.3). The frame only
    /// copies a record into a fixed ring; a thread of the recorder's own writes the file, so the frame never
    /// waits for storage, as with the pose-log recorder. A hand log is motion data of a person: nothing is
    /// recorded unless it is switched on, and the file stays on the phone.
    ///
    /// Main-thread calls (the frame): Start, Offer, Stop, Enabled. One thread calls them, and only one.
    /// </summary>
    public sealed class HandsLogRecorder : IDisposable
    {
        /// <summary>Records the ring holds: a little over 8 s at 30 Hz, so a stall of storage that long loses nothing. 700 kB, allocated at the first start.</summary>
        public const int RingSize = 256;
        const int RingMask = RingSize - 1;
        /// <summary>The writer is woken after this many records; it also looks every quarter of a second.</summary>
        const int WakeEvery = 8;

        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        // One producer (the frame) and one consumer (the writer thread): each index has a single writer.
        volatile int _head;
        volatile int _tail;
        volatile bool _recording;
        volatile bool _stop;
        volatile int _written;
        volatile int _dropped;
        volatile string _error;
        byte[] _ring;                   // RingSize records, encoded by the frame
        Thread _thread;
        HandsLogWriter _writer;
        int _sinceWake;
        // The frame's side of the clock: times are counted from the first record and never go back.
        bool _haveFirst;
        double _firstTime;
        double _lastTime;

        /// <summary>Folder that Enabled writes into. Set by the installer (persistentDataPath/logs on the phone).</summary>
        public string Directory { get; set; } = "";

        /// <summary>True from a successful Start until Stop, or until a write fails.</summary>
        public bool IsRecording { get { return _recording; } }
        /// <summary>The file being written, or the last one written; "" for a stream.</summary>
        public string Path { get; private set; } = "";
        /// <summary>Records lost because the writer fell a whole ring behind.</summary>
        public int Dropped { get { return _dropped; } }
        /// <summary>Records the writer has written to the stream so far in this log.</summary>
        public int Written { get { return _written; } }
        /// <summary>The message of a write failure, or null. A failure ends the recording.</summary>
        public string Error { get { return _error; } }

        /// <summary>
        /// The hands.record switch. Turning it on starts "hands_&lt;UTC time&gt;.handlog" in Directory; turning it
        /// off closes the file.
        /// </summary>
        public bool Enabled
        {
            get { return _recording; }
            set
            {
                if (value == _recording) return;
                if (!value) { Stop(); return; }
                string name = "hands_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".handlog";
                string folder = string.IsNullOrEmpty(Directory) ? "." : Directory;
                Start(System.IO.Path.Combine(folder, name));
            }
        }

        /// <summary>Starts a log in a file. False when the file cannot be created (Error says why).</summary>
        public bool Start(string path)
        {
            Stop();
            Stream stream;
            try
            {
                string folder = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder)) System.IO.Directory.CreateDirectory(folder);
                stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is NotSupportedException)
            {
                _error = e.Message;
                return false;
            }
            if (!Start(stream)) return false;
            Path = path;
            return true;
        }

        /// <summary>Starts a log in a stream, which the recorder closes when it stops. False when the header cannot be written.</summary>
        public bool Start(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            Stop();
            try
            {
                _writer = new HandsLogWriter(stream);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException ||
                                      e is NotSupportedException || e is ObjectDisposedException)
            {
                _error = e.Message;
                try { stream.Dispose(); } catch (IOException) { }
                return false;
            }
            if (_ring == null) _ring = new byte[RingSize * HandsLog.RecordLength];
            Path = "";
            _error = null;
            _dropped = 0;
            _written = 0;
            _head = 0;
            _tail = 0;
            _haveFirst = false;
            _lastTime = 0.0;
            _sinceWake = 0;
            _stop = false;
            _recording = true;
            _thread = new Thread(WriterLoop) { IsBackground = true, Name = "Atlas hands log" };
            _thread.Start();
            return true;
        }

        /// <summary>
        /// One result of the detector with the head pose of the frame that polled it, at the end of the sense
        /// step. Allocates nothing and never blocks. False when it was not kept: nothing is being recorded, or the
        /// ring was full (the record is then dropped and counted).
        /// </summary>
        public bool Offer(double time, float3 headPosition, quaternion headRotation, in HandsDetection detection)
        {
            if (!_recording) return false;
            int head = _head;
            if (head - _tail >= RingSize)
            {
                _dropped++;
                return false;
            }
            if (!_haveFirst)
            {
                // The first record is time 0: a time that is not a number cannot be that.
                if (double.IsNaN(time) || double.IsInfinity(time)) return false;
                _haveFirst = true;
                _firstTime = time;
            }
            double t = time - _firstTime;
            // The reader refuses a time that runs backwards (or is not a number), so a clock that was set back is held.
            if (double.IsNaN(t) || double.IsInfinity(t) || t < _lastTime) t = _lastTime;
            _lastTime = t;
            HandsLog.Encode(_ring, (head & RingMask) * HandsLog.RecordLength, t, headPosition, headRotation, in detection);
            _head = head + 1;
            // Waking the writer now and then keeps the frame path to a copy and a counter.
            if (++_sinceWake >= WakeEvery)
            {
                _sinceWake = 0;
                _wake.Set();
            }
            return true;
        }

        /// <summary>As the other Offer, with the head as a pose.</summary>
        public bool Offer(double time, in HeadPose head, in HandsDetection detection)
        {
            return Offer(time, head.Position, head.Rotation, in detection);
        }

        /// <summary>Writes what is in the ring, closes the stream and ends the writer thread.</summary>
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

        /// <summary>Stops, writing what is in the ring.</summary>
        public void Dispose()
        {
            Stop();
            _wake.Dispose();
        }

        void WriterLoop()
        {
            try
            {
                while (true)
                {
                    bool stopping = _stop;      // read before draining, so nothing offered before Stop is lost
                    // What reached the file's buffer is pushed on at every pass, so a phone that is lost mid-recording loses a quarter of a second, not the buffer.
                    if (Drain()) _writer.Flush();
                    if (stopping) break;
                    _wake.WaitOne(250);
                }
                _writer.Flush();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ObjectDisposedException ||
                                      e is NotSupportedException || e is ArgumentException)
            {
                _error = e.Message;
                _recording = false;
            }
            finally
            {
                CloseWriter();
            }
        }

        /// <summary>Writes every record in the ring. True when there was one.</summary>
        bool Drain()
        {
            int head = _head;
            int tail = _tail;
            bool any = tail != head;
            while (tail != head)
            {
                _writer.WriteEncoded(_ring, (tail & RingMask) * HandsLog.RecordLength);
                tail++;
                // The slot is the frame's again only once it is written: a writer that is stuck keeps its record.
                _tail = tail;
                _written = tail;
            }
            return any;
        }

        void CloseWriter()
        {
            HandsLogWriter writer = _writer;
            _writer = null;
            if (writer == null) return;
            try { writer.Dispose(); }
            catch (IOException e) { _error = e.Message; }
        }
    }
}
