using Atlas.Blades.Lung;
using Atlas.Input;
using Atlas.Telemetry;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>The ONLY file naming placeholder-specific facts (docs/laneB/placeholders.md).</summary>
    public static class NeedleSkyPlaceholderSeams
    {
        public static string CaseRoot(string caseId) => CaseManifest.Root(caseId);   // D19

        public static ISkyEvents EventLogSink() => new Sink();

        /// <summary>False today: the placeholder IActionSource has no public live accessor (P3-B07 precedent).</summary>
        public static bool TryResolveInput(out IActionSource input, out string missing)
        {
            input = null;
            missing = "live IActionSource (lane C P1-C10)";
            return false;
        }

        sealed class Sink : ISkyEvents
        {
            public void Emit(string ev, string dataJson) => EventLog.Emit("lung", ev, dataJson);   // MP §10.2.6, scene "lung"
        }
    }
}
