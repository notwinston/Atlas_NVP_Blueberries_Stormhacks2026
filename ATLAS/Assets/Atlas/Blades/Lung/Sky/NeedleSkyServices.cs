using Atlas.Input;
using Atlas.World;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>What the needle sky takes from the app: the live input, the event log, and where the case package lies.</summary>
    public static class NeedleSkyServices
    {
        public static string CaseRoot(string caseId) => CaseManifest.Root(caseId);   // D19; the case package is lane D's (P2-D01)

        public static ISkyEvents EventLogSink() => new Sink();

        /// <summary>The published input, or null (the beat then reads no action and no stick).</summary>
        public static IAtlasInput Input() => AtlasServices.TryGet<IAtlasInput>(out var input) ? input : null;

        sealed class Sink : ISkyEvents
        {
            public void Emit(string ev, string dataJson) => LungEvents.Emit(ev, dataJson);   // MP §10.2.6, scene "lung"
        }
    }
}
