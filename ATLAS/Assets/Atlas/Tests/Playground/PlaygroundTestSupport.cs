using System.Collections.Generic;
using Atlas.World;

namespace Atlas.Blades.Playground.Tests
{
    /// <summary>One logged event as the fakes keep it: the name and the data as JSON text.</summary>
    sealed class LoggedEvent
    {
        public string Ev;
        public string Json;
    }

    sealed class RecordingEvents : IEventLog
    {
        public readonly List<LoggedEvent> Events = new List<LoggedEvent>();

        public void Log(string ev) { Events.Add(new LoggedEvent { Ev = ev, Json = "{}" }); }
        public void Log(string ev, string key, double value) { Events.Add(new LoggedEvent { Ev = ev, Json = "{\"" + key + "\":" + F(value) + "}" }); }
        public void Log(string ev, string key, string value) { Events.Add(new LoggedEvent { Ev = ev, Json = "{\"" + key + "\":\"" + value + "\"}" }); }

        public void Log(string ev, string key1, double value1, string key2, double value2)
        {
            Events.Add(new LoggedEvent { Ev = ev, Json = "{\"" + key1 + "\":" + F(value1) + ",\"" + key2 + "\":" + F(value2) + "}" });
        }

        public void LogJson(string ev, string dataJson) { Events.Add(new LoggedEvent { Ev = ev, Json = dataJson }); }

        public int Count(string ev)
        {
            int n = 0;
            foreach (LoggedEvent e in Events) if (e.Ev == ev) n++;
            return n;
        }

        public LoggedEvent Last(string ev)
        {
            for (int i = Events.Count - 1; i >= 0; i--) if (Events[i].Ev == ev) return Events[i];
            return null;
        }

        static string F(double v) { return v.ToString(System.Globalization.CultureInfo.InvariantCulture); }
    }

    sealed class RecordingSink : IPromptSink
    {
        public readonly List<Prompt> Shown = new List<Prompt>();
        public bool Visible;

        public void Show(Prompt prompt) { Shown.Add(prompt); Visible = true; }
        public void Hide() { Visible = false; }
    }
}
