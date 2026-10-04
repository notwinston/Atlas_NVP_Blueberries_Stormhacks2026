using System.Collections.Generic;

namespace Atlas.Blades.FieldFit.Tests
{
    // Master Plan App. B.2; test double for ILensEventSink.
    internal sealed class FakeEvents : ILensEventSink
    {
        public readonly List<KeyValuePair<string, string>> Log = new List<KeyValuePair<string, string>>();
        public void Emit(string ev, string json) { Log.Add(new KeyValuePair<string, string>(ev, json)); }
        public int Count(string ev) { int n = 0; foreach (var e in Log) if (e.Key == ev) n++; return n; }
    }
}
