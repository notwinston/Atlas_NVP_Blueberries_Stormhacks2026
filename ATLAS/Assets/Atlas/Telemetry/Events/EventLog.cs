using System;
using System.Collections.Generic;
using System.Text;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Telemetry
{
    /// <summary>
    /// The session's event log (Master Plan section 7.3.3, Appendix B.2 <c>Event</c>). Every event becomes one
    /// JSON line <c>{"t_us":…,"scene":"S1","ev":"…","data":{…}}</c> in the phone's file whether or not the link
    /// is up, and one <c>Event</c> message when it is. While the link is down the newest 256 events wait and are
    /// sent after the next handshake; the file never loses one.
    /// <para>Main thread only. Logging is for edges and milestones, never for something that happens every frame.</para>
    /// </summary>
    public sealed class EventLog : IEventLog
    {
        public const int MaxQueued = 256;
        public const string NoScene = "none";

        readonly ILinkClock _clock;
        readonly LogFileWriter _files;
        readonly StringBuilder _line = new StringBuilder(256);
        readonly Queue<EventMessage> _waiting = new Queue<EventMessage>(MaxQueued);
        string _scene = NoScene;

        /// <param name="clock">Stamps <c>t_us</c>; the same clock the link uses.</param>
        /// <param name="files">The writer of the phone's files, or null to keep events off the disk (tests, the Spectator).</param>
        public EventLog(ILinkClock clock, LogFileWriter files)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            _clock = clock;
            _files = files;
        }

        /// <summary>The link the events are sent on; null while there is none.</summary>
        public LinkClient Link { get; set; }

        /// <summary><c>S0</c> to <c>S6</c> while that space is entered, <c>none</c> otherwise.</summary>
        public string Scene
        {
            get { return _scene; }
            set { _scene = string.IsNullOrEmpty(value) ? NoScene : value; }
        }

        /// <summary>Events logged since start.</summary>
        public int Count { get; private set; }

        /// <summary>Events waiting for the link.</summary>
        public int Queued { get { return _waiting.Count; } }

        /// <summary>Raised for every event, after it was written.</summary>
        public event Action<EventMessage> Logged;

        public void Log(string ev) { Log(ev, JsonValue.Object()); }

        public void Log(string ev, string key, double value) { Log(ev, JsonValue.Object().Set(key, Number(value))); }

        public void Log(string ev, string key, string value) { Log(ev, JsonValue.Object().Set(key, value ?? string.Empty)); }

        public void Log(string ev, string key1, double value1, string key2, double value2)
        {
            Log(ev, JsonValue.Object().Set(key1, Number(value1)).Set(key2, Number(value2)));
        }

        public void LogJson(string ev, string dataJson)
        {
            Log(ev, DataOf(dataJson));
        }

        /// <summary>
        /// As <see cref="LogJson(string, string)"/>, for an event that belongs to a scene other than the entered
        /// space: a use case names itself (<c>lung</c>, contract section 2.6). Null or empty keeps the current scene.
        /// </summary>
        public void LogJson(string scene, string ev, string dataJson)
        {
            Log(new EventMessage(_clock.NowUs, string.IsNullOrEmpty(scene) ? _scene : scene, ev ?? string.Empty, DataOf(dataJson)));
        }

        /// <summary>Logs an event whose data is already a JSON object.</summary>
        public void Log(string ev, JsonValue data)
        {
            Log(new EventMessage(_clock.NowUs, _scene, ev ?? string.Empty, data != null && data.IsObject ? data : JsonValue.Object()));
        }

        /// <summary>Logs a finished event to the file and to the link.</summary>
        public void Log(EventMessage message)
        {
            if (message == null) return;
            WriteLine(message);
            LinkClient link = Link;
            // Order matters to the reader of the Hub's log: nothing overtakes an event that is still waiting.
            if (link != null && link.IsReady && _waiting.Count == 0 && link.Send(message)) return;
            if (_waiting.Count == MaxQueued) _waiting.Dequeue();
            _waiting.Enqueue(message);
        }

        /// <summary>Writes an event to the phone's file only, for what the link already reports itself.</summary>
        public void LogLocal(EventMessage message)
        {
            if (message != null) WriteLine(message);
        }

        /// <summary>Sends waiting events while the link is ready, at most <paramref name="max"/> per call. Returns how many went.</summary>
        public int SendQueued(int max = 16)
        {
            LinkClient link = Link;
            if (link == null || !link.IsReady) return 0;
            int sent = 0;
            while (sent < max && _waiting.Count > 0)
            {
                if (!link.Send(_waiting.Peek())) break;
                _waiting.Dequeue();
                sent++;
            }
            return sent;
        }

        /// <summary>
        /// A warning or an error: one line <c>&lt;t_us&gt; &lt;level&gt; &lt;msg&gt;</c> in the phone log, and a <c>Log</c>
        /// message when <paramref name="toLink"/> is true and the link is up.
        /// </summary>
        public void LogLine(string level, string message, bool toLink = true)
        {
            long now = _clock.NowUs;
            if (_files != null)
            {
                _line.Length = 0;
                TelemetryCsv.AppendInt(_line, now);
                _line.Append(' ').Append(level).Append(' ');
                int start = _line.Length;
                _line.Append(message);
                // One record per line.
                _line.Replace('\n', ' ', start, _line.Length - start).Replace('\r', ' ', start, _line.Length - start);
                _line.Append('\n');
                _files.Append(LogFileWriter.Phone, _line);
            }
            LinkClient link = Link;
            if (toLink && link != null && link.IsReady) link.SendLog(level, message);
        }

        void WriteLine(EventMessage message)
        {
            Count++;
            if (_files != null)
            {
                _line.Length = 0;
                message.ToJson().WriteTo(_line, false);
                _line.Append('\n');
                _files.Append(LogFileWriter.Events, _line);
            }
            Action<EventMessage> logged = Logged;
            if (logged != null) logged(message);
        }

        static JsonValue DataOf(string dataJson)
        {
            JsonValue data;
            if (string.IsNullOrEmpty(dataJson)) data = JsonValue.Object();
            else if (!AtlasJson.TryParse(dataJson, out data) || !data.IsObject) data = JsonValue.Object().Set("text", dataJson);
            return data;
        }

        // Counts and levels arrive as doubles through IEventLog; a whole number is written as one.
        static JsonValue Number(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return JsonValue.From(0L);
            if (value == Math.Floor(value) && Math.Abs(value) < 9.0e15) return JsonValue.From((long)value);
            return JsonValue.From(value);
        }
    }

    /// <summary>The scene-tagged <c>LogJson</c> for code that holds the service as <see cref="IEventLog"/>.</summary>
    public static class EventLogExtensions
    {
        /// <summary>Logs under <paramref name="scene"/> when the log is the session's <see cref="EventLog"/>; any other
        /// <see cref="IEventLog"/> has no scene to set and takes the event as <c>LogJson(ev, dataJson)</c>.</summary>
        public static void LogJson(this IEventLog log, string scene, string ev, string dataJson)
        {
            if (log == null) throw new ArgumentNullException("log");
            EventLog real = log as EventLog;
            if (real != null) real.LogJson(scene, ev, dataJson);
            else log.LogJson(ev, dataJson);
        }
    }
}
