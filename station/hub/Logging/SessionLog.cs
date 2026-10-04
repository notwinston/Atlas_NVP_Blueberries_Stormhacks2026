using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Atlas.Link;

namespace Atlas.Station
{
    /// <summary>
    /// The files of one Hub session under <c>logs/&lt;session&gt;/</c> (Master Plan section 7.3.3, lane D
    /// contract 3.4): <c>state.bin</c> (every <c>State</c> packet with its receive time, also the replay source),
    /// <c>events.jsonl</c>, <c>perf.csv</c> (one <c>Telemetry</c> row per second), <c>phone.log</c>,
    /// <c>hub.log</c> (connects, leaves, every command and parameter sent), and the last <c>manifest.json</c>
    /// and <c>outline.bin</c>, which ReplayPhone sends again with the recording.
    /// <para>
    /// A folder that already holds a session is continued, never overwritten: the phone names the session in its
    /// <c>Hello</c>, so a Hub that was restarted in mid-session appends to the files of the Hub before it.
    /// </para>
    /// Motion logs are treated as health data (section 13.5): the folder belongs on an encrypted volume.
    /// </summary>
    public sealed class SessionLog : IDisposable
    {
        public const string StateFile = "state.bin";
        public const string EventsFile = "events.jsonl";
        public const string PerfFile = "perf.csv";
        public const string PhoneLogFile = "phone.log";
        public const string HubLogFile = "hub.log";
        public const string ManifestFile = "manifest.json";
        public const string OutlineFile = "outline.bin";

        /// <summary>Every file a session folder may hold; nothing else is served for download.</summary>
        public static readonly string[] Files = { PerfFile, EventsFile, StateFile, PhoneLogFile, HubLogFile, ManifestFile, OutlineFile };

        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        readonly object _lock = new object();
        readonly byte[] _record = new byte[StateLog.RecordSize];
        readonly StringBuilder _row = new StringBuilder(TelemetryCsv.MaxRowLength);
        readonly HashSet<uint> _perfSeqs = new HashSet<uint>();
        uint _perfLastSeq;
        bool _touched;
        FileStream _state;
        StreamWriter _events, _perf, _phone, _hub;

        public string SessionId { get; private set; }

        /// <summary>The session's folder, absolute.</summary>
        public string Folder { get; private set; }

        /// <summary><c>State</c> records in <c>state.bin</c>, those of an earlier Hub included.</summary>
        public long StateRecords { get; private set; }

        /// <summary>Lines this Hub wrote to <c>events.jsonl</c>.</summary>
        public long EventLines { get; private set; }

        /// <summary>Rows in <c>perf.csv</c>, those of an earlier Hub and those merged from an upload included.</summary>
        public long TelemetryRows { get; private set; }

        /// <summary>True when this Hub made the folder; false when it continues a session that was already on disk.</summary>
        public bool CreatedFolder { get; private set; }

        /// <summary>
        /// While true, a <c>Telemetry</c> row whose <c>seq</c> is already in <c>perf.csv</c> is skipped and the
        /// file is kept in <c>seq</c> order (lane D contract 3.2). The Hub switches it on for a session the phone
        /// named: there <c>seq</c> counts from 0 once. A phone that names no session may start again from 0 after
        /// a restart, so its rows are written as they come.
        /// </summary>
        public bool SkipKnownRows { get; set; }

        /// <summary>
        /// True while the folder holds nothing but this Hub's own <c>hub.log</c> lines: no phone has written to it
        /// and it did not exist before. Such a folder can be left behind without losing anything.
        /// </summary>
        public bool IsPristine { get { lock (_lock) return CreatedFolder && !_touched; } }

        public SessionLog(string root, string sessionId)
        {
            SessionId = sessionId;
            Folder = Path.GetFullPath(Path.Combine(root, sessionId));
            CreatedFolder = !Directory.Exists(Folder);
            Directory.CreateDirectory(Folder);

            // Readers (the download routes, ReplayPhone on a live session) may open the files while they grow.
            _state = OpenState();
            _events = Text(EventsFile);
            _phone = Text(PhoneLogFile);
            _hub = Text(HubLogFile);
            OpenPerf();
            Flush();
        }

        // state.bin is fixed-size records after a header. A Hub that was killed in mid-write leaves part of a
        // record; appending after it would shift every later record, so the file is cut back to whole records.
        FileStream OpenState()
        {
            string path = Path.Combine(Folder, StateFile);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                bool valid;
                string error;
                using (var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    valid = StateLog.TryReadHeader(probe, out error);
                if (valid)
                {
                    var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read, 1 << 16);
                    long records = (stream.Length - StateLog.HeaderSize) / StateLog.RecordSize;
                    stream.SetLength(StateLog.HeaderSize + records * StateLog.RecordSize);
                    stream.Seek(0, SeekOrigin.End);
                    StateRecords = records;
                    return stream;
                }
                // Not a recording this build can continue: keep it under another name rather than write over it.
                File.Move(path, Path.Combine(Folder, "state." + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".bin"));
            }
            var created = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
            StateLog.WriteHeader(created);
            return created;
        }

        StreamWriter Text(string name)
        {
            string path = Path.Combine(Folder, name);
            bool torn = EndsWithoutLineEnd(path);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 1 << 14);
            var writer = new StreamWriter(stream, s_utf8) { NewLine = "\n" };
            if (torn) writer.Write('\n');   // the Hub before this one stopped in mid-line
            return writer;
        }

        static bool EndsWithoutLineEnd(string path)
        {
            if (!File.Exists(path)) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length == 0) return false;
                stream.Seek(-1, SeekOrigin.End);
                return stream.ReadByte() != '\n';
            }
        }

        // perf.csv: the rows already there are kept and their seq numbers remembered; a file that is not well
        // formed (no header, a torn last line) is written again first.
        void OpenPerf()
        {
            string path = Path.Combine(Folder, PerfFile);
            bool intact;
            List<string> rows = PerfCsv.Read(path, out intact);
            if (!intact) PerfCsv.MergeFile(path, null);
            _perfSeqs.Clear();
            _perfLastSeq = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                uint seq;
                PerfCsv.TryReadSeq(rows[i], out seq);
                _perfSeqs.Add(seq);
                if (seq > _perfLastSeq) _perfLastSeq = seq;
            }
            TelemetryRows = rows.Count;
            _perf = Text(PerfFile);
        }

        /// <summary>Appends one packet. Runs on the frame path of the relay: a copy into a buffer, no allocation.</summary>
        public void WriteState(long hubUs, byte[] raw, int offset)
        {
            lock (_lock)
            {
                if (_state == null) return;
                StateLog.PackRecord(_record, hubUs, raw, offset);
                _state.Write(_record, 0, _record.Length);
                StateRecords++;
                _touched = true;
            }
        }

        /// <summary>Appends one line to <c>events.jsonl</c> and puts it on disk at once: events are rare and precious.</summary>
        public void WriteEvent(string jsonLine)
        {
            lock (_lock)
            {
                if (_events == null) return;
                _events.WriteLine(jsonLine);
                _events.Flush();
                EventLines++;
                _touched = true;
            }
        }

        /// <summary>
        /// Writes one row to <c>perf.csv</c>. Returns false, having written nothing, for a row whose <c>seq</c>
        /// the file already holds while <see cref="SkipKnownRows"/> is on. A row that arrives after a later one
        /// is put in its place, so the file stays in <c>seq</c> order.
        /// </summary>
        public bool WriteTelemetry(in TelemetryMessage telemetry)
        {
            lock (_lock)
            {
                if (_perf == null) return false;
                _row.Clear();
                TelemetryCsv.AppendRow(_row, telemetry);
                if (SkipKnownRows)
                {
                    if (!_perfSeqs.Add(telemetry.Seq)) return false;
                    if (TelemetryRows > 0 && telemetry.Seq < _perfLastSeq)
                    {
                        // The trailing line end is not part of a row as PerfCsv holds it.
                        MergePerfLocked(new List<string> { _row.ToString(0, _row.Length - 1) });
                        _touched = true;
                        return true;
                    }
                }
                if (telemetry.Seq > _perfLastSeq) _perfLastSeq = telemetry.Seq;
                _perf.Write(_row);
                _perf.Flush();
                TelemetryRows++;
                _touched = true;
                return true;
            }
        }

        /// <summary>
        /// Merges rows (as <see cref="PerfCsv.Rows"/> returns them) into <c>perf.csv</c> by <c>seq</c>: the rows
        /// the file lacks are added in their place, the others are left alone. Returns how many were added.
        /// </summary>
        public int MergePerf(List<string> rows)
        {
            lock (_lock)
            {
                // A log that is closed is merged as the file it has become.
                if (_perf == null) return PerfCsv.MergeFile(Path.Combine(Folder, PerfFile), rows);
                int added = MergePerfLocked(rows);
                if (added > 0) _touched = true;
                return added;
            }
        }

        int MergePerfLocked(List<string> rows)
        {
            _perf.Dispose();
            _perf = null;
            try
            {
                return PerfCsv.MergeFile(Path.Combine(Folder, PerfFile), rows);
            }
            finally
            {
                OpenPerf();
            }
        }

        /// <summary>One line of <c>phone.log</c>: <c>&lt;t_us&gt; &lt;level&gt; &lt;msg&gt;</c>, in the phone's clock.</summary>
        public void WritePhoneLog(long phoneUs, string level, string message)
        {
            lock (_lock)
            {
                if (_phone == null) return;
                _phone.Write(phoneUs);
                _phone.Write(' ');
                _phone.Write(string.IsNullOrEmpty(level) ? "info" : level);
                _phone.Write(' ');
                _phone.WriteLine(OneLine(message));
                _phone.Flush();
                _touched = true;
            }
        }

        /// <summary>One line of <c>hub.log</c>: <c>&lt;hub_us&gt; &lt;kind&gt; &lt;JSON&gt;</c>.</summary>
        public void WriteHubLog(long hubUs, string kind, string json)
        {
            lock (_lock)
            {
                if (_hub == null) return;
                _hub.Write(hubUs);
                _hub.Write(' ');
                _hub.Write(kind);
                _hub.Write(' ');
                _hub.WriteLine(OneLine(json));
                _hub.Flush();
            }
        }

        /// <summary>Everything written to <c>hub.log</c> so far, whole lines.</summary>
        public string ReadHubLog()
        {
            lock (_lock)
            {
                if (_hub != null) _hub.Flush();
                string path = Path.Combine(Folder, HubLogFile);
                if (!File.Exists(path)) return string.Empty;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, s_utf8))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>
        /// Appends lines taken from another session's <c>hub.log</c> (see <see cref="ReadHubLog"/>), as they are:
        /// what the Hub noted before the phone named the session belongs to that session's story.
        /// </summary>
        public void AppendHubLog(string lines)
        {
            if (string.IsNullOrEmpty(lines)) return;
            lock (_lock)
            {
                if (_hub == null) return;
                _hub.Write(lines);
                if (lines[lines.Length - 1] != '\n') _hub.Write('\n');
                _hub.Flush();
            }
        }

        /// <summary>Replaces <c>manifest.json</c> with the phone's latest <c>ParamManifest</c> body.</summary>
        public void WriteManifest(string json)
        {
            lock (_lock)
            {
                if (_state == null) return;
                File.WriteAllText(Path.Combine(Folder, ManifestFile), json + "\n", s_utf8);
                _touched = true;
            }
        }

        /// <summary>Replaces <c>outline.bin</c> with the phone's latest <c>RoomOutline</c> message.</summary>
        public void WriteOutline(byte[] message, int length)
        {
            lock (_lock)
            {
                if (_state == null) return;
                using (var stream = new FileStream(Path.Combine(Folder, OutlineFile), FileMode.Create, FileAccess.Write, FileShare.Read))
                    stream.Write(message, 0, length);
                _touched = true;
            }
        }

        static string OneLine(string text) { return (text ?? string.Empty).Replace('\n', ' ').Replace('\r', ' '); }

        /// <summary>Puts buffered <c>State</c> records on disk. Called once a second and before a download.</summary>
        public void Flush()
        {
            lock (_lock)
            {
                if (_state == null) return;
                _state.Flush();
                _events.Flush();
                if (_perf != null) _perf.Flush();
                _phone.Flush();
                _hub.Flush();
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_state == null) return;
                _state.Dispose();
                _events.Dispose();
                if (_perf != null) _perf.Dispose();
                _phone.Dispose();
                _hub.Dispose();
                _state = null;
                _events = null;
                _perf = null;
                _phone = null;
                _hub = null;
            }
        }
    }
}
