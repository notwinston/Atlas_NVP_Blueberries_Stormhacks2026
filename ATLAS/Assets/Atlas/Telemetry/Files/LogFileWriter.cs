using System;
using System.IO;
using System.Text;
using System.Threading;

namespace Atlas.Telemetry
{
    /// <summary>
    /// Owns the phone's three log files of a session (contracts F8): the perf CSV, the event log and the phone
    /// log, all under <c>persistentDataPath/logs</c>. The main thread hands over finished lines, which are
    /// copied into a preallocated buffer; one background thread appends and flushes once per second, so the
    /// frame never waits for the file system and a steady run allocates nothing.
    /// </summary>
    public sealed class LogFileWriter : IDisposable
    {
        public const int Perf = 0;
        public const int Events = 1;
        public const int Phone = 2;
        public const int FileCount = 3;

        const int InitialChars = 8192;
        const int MaxChars = 1 << 20;
        const int FlushPeriodMs = 1000;

        sealed class Channel
        {
            public char[] Pending = new char[InitialChars];
            public int PendingLength;
            public char[] Writing = new char[InitialChars];
            public string Path;
            public StreamWriter Writer;
        }

        readonly Channel[] _channels = new Channel[FileCount];
        readonly object _gate = new object();     // guards the pending buffers and the paths
        readonly object _io = new object();       // one writer at a time: the thread, or a Flush on the caller's thread
        readonly AutoResetEvent _wake = new AutoResetEvent(false);
        readonly string _directory;
        Thread _thread;
        volatile bool _stop;
        int _errors;
        int _dropped;

        /// <param name="directory">The logs folder; created on first use.</param>
        /// <param name="background">False keeps everything on the caller's thread: lines are written by <see cref="Flush"/> only.</param>
        public LogFileWriter(string directory, bool background = true)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("A log directory is needed", "directory");
            _directory = directory;
            for (int i = 0; i < FileCount; i++) _channels[i] = new Channel();
            if (background)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Atlas.LogWriter" };
                _thread.Start();
            }
        }

        public string Directory { get { return _directory; } }

        /// <summary>The session whose files are open; null before <see cref="Open"/>.</summary>
        public string SessionId { get; private set; }

        /// <summary>Writes that failed (disk full, folder gone). The lines of a failed write are lost.</summary>
        public int Errors { get { return _errors; } }

        /// <summary>Lines dropped because more than a megabyte was waiting.</summary>
        public int Dropped { get { return _dropped; } }

        public static string FileName(int file, string sessionId)
        {
            switch (file)
            {
                case Perf: return "perf_" + sessionId + ".csv";
                case Events: return "events_" + sessionId + ".jsonl";
                default: return "phone_" + sessionId + ".log";
            }
        }

        /// <summary>The full path of one of the session's files; null before <see cref="Open"/>.</summary>
        public string PathOf(int file)
        {
            lock (_gate) return _channels[file].Path;
        }

        /// <summary>Starts the files of a session. Lines still waiting go to the previous session's files first.</summary>
        public void Open(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) throw new ArgumentException("A session id is needed", "sessionId");
            lock (_io)
            {
                if (SessionId != null) WriteAll();
                CloseWriters();
                lock (_gate)
                {
                    SessionId = sessionId;
                    for (int i = 0; i < FileCount; i++) _channels[i].Path = Path.Combine(_directory, FileName(i, sessionId));
                }
            }
        }

        /// <summary>Queues one finished line (with its line end). Main thread; copies the characters and returns.</summary>
        public void Append(int file, StringBuilder line)
        {
            if (line == null || line.Length == 0) return;
            lock (_gate)
            {
                Channel c = _channels[file];
                int n = line.Length;
                if (!Reserve(c, n)) return;
                line.CopyTo(0, c.Pending, c.PendingLength, n);
                c.PendingLength += n;
            }
        }

        /// <summary>As <see cref="Append(int, StringBuilder)"/>, for text that already exists as a string.</summary>
        public void Append(int file, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_gate)
            {
                Channel c = _channels[file];
                int n = line.Length;
                if (!Reserve(c, n)) return;
                line.CopyTo(0, c.Pending, c.PendingLength, n);
                c.PendingLength += n;
            }
        }

        bool Reserve(Channel c, int n)
        {
            int needed = c.PendingLength + n;
            if (needed <= c.Pending.Length) return true;
            if (needed > MaxChars)
            {
                _dropped++;
                return false;
            }
            int size = c.Pending.Length;
            while (size < needed) size *= 2;
            Array.Resize(ref c.Pending, size);
            return true;
        }

        /// <summary>Writes everything that is waiting, now, on the caller's thread. Used at pause, at quit and by tests.</summary>
        public void Flush()
        {
            lock (_io) WriteAll();
        }

        void Run()
        {
            while (!_stop)
            {
                _wake.WaitOne(FlushPeriodMs);
                if (_stop) break;
                lock (_io) WriteAll();
            }
        }

        // Caller holds _io.
        void WriteAll()
        {
            for (int i = 0; i < FileCount; i++)
            {
                Channel c = _channels[i];
                int n;
                string path;
                lock (_gate)
                {
                    n = c.PendingLength;
                    path = c.Path;
                    if (n == 0 || path == null) continue;
                    char[] swap = c.Writing;
                    c.Writing = c.Pending;
                    c.Pending = swap.Length >= c.Writing.Length ? swap : new char[c.Writing.Length];
                    c.PendingLength = 0;
                }
                try
                {
                    if (c.Writer == null)
                    {
                        System.IO.Directory.CreateDirectory(_directory);
                        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                        c.Writer = new StreamWriter(stream, new UTF8Encoding(false));
                        // A new perf file starts with the column names, written by the link library as the Hub
                        // writes them, so the two files are the same from their first byte.
                        if (i == Perf && stream.Length == 0)
                        {
                            var header = new StringBuilder(Atlas.Link.TelemetryCsv.MaxRowLength);
                            Atlas.Link.TelemetryCsv.AppendHeader(header);
                            c.Writer.Write(header.ToString());
                        }
                    }
                    c.Writer.Write(c.Writing, 0, n);
                    c.Writer.Flush();
                }
                catch (Exception)
                {
                    // A log must never take the app down. The lines are lost and the count says so.
                    Interlocked.Increment(ref _errors);
                    try { if (c.Writer != null) c.Writer.Dispose(); } catch (Exception) { }
                    c.Writer = null;
                }
            }
        }

        void CloseWriters()
        {
            for (int i = 0; i < FileCount; i++)
            {
                Channel c = _channels[i];
                if (c.Writer == null) continue;
                try { c.Writer.Dispose(); } catch (Exception) { Interlocked.Increment(ref _errors); }
                c.Writer = null;
            }
        }

        /// <summary>Removes the three files of a session (Master Plan section 13.5, delete by id). Returns how many were deleted.</summary>
        public int DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return 0;
            int deleted = 0;
            lock (_io)
            {
                if (string.Equals(sessionId, SessionId, StringComparison.Ordinal)) CloseWriters();
                for (int i = 0; i < FileCount; i++)
                {
                    string path = Path.Combine(_directory, FileName(i, sessionId));
                    try
                    {
                        if (!File.Exists(path)) continue;
                        File.Delete(path);
                        deleted++;
                    }
                    catch (Exception) { Interlocked.Increment(ref _errors); }
                }
            }
            return deleted;
        }

        /// <summary>Writes what is waiting, closes the files and stops the thread.</summary>
        public void Dispose()
        {
            _stop = true;
            _wake.Set();
            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread != Thread.CurrentThread) thread.Join(2000);
            lock (_io)
            {
                WriteAll();
                CloseWriters();
            }
        }
    }
}
