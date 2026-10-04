using Atlas.Link;
using Atlas.Telemetry;
using Atlas.World;

namespace Atlas.Blades.Lung
{
    // MP §10.2.6: every Lung event belongs to scene "lung", whichever space is entered (lane D contract 2.6). It goes to the session's
    // real event log; with none published the event is skipped, as the contract has it.
    public static class LungEvents
    {
        public const string Scene = "lung";

        public static void Emit(string ev, JsonValue data) => Emit(ev, data?.ToJson(false));

        // dataJson is one JSON object as text.
        public static void Emit(string ev, string dataJson)
        {
            if (AtlasServices.TryGet<IEventLog>(out var log)) log.LogJson(Scene, ev, dataJson);
        }
    }
}
