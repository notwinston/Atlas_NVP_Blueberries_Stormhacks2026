// PLACEHOLDER (lane D, P1-D04): contract surface only; replaced by lane D's implementation
using System;
using System.Diagnostics;
using Atlas.Link;

namespace Atlas.Telemetry
{
    // Master Plan §6.2 ("event log" in Telemetry), App. B.2 Event.
    public static class EventLog
    {
        public static event Action<EventMessage> Emitted;

        // Fills TUs from a monotonic microsecond clock; raises Emitted synchronously.
        public static void Emit(string scene, string ev, string dataJson)
        {
            var msg = new EventMessage
            {
                TUs = (long)(Stopwatch.GetTimestamp() * 1000000.0 / Stopwatch.Frequency),
                Scene = scene,
                Ev = ev,
                Data = dataJson,
            };
            Emitted?.Invoke(msg);
        }
    }
}
