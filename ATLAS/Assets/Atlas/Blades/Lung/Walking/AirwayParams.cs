using Atlas.Telemetry;

namespace Atlas.Blades.Lung.Walking
{
    // MP §12.4 kill switch and G6 §3.7. Bound with AtlasParamScanner.Bind.
    public sealed class AirwayParams
    {
        // DV-G6-8: 1 = Mode 1 (Curved Walk), 2 = Mode 2 (Aligned Cells); range [1, 2], default 1.
        [AtlasParam("curved.mode", "curved", 1f, 2f, 1f)] public int CurvedMode = 1;
    }
}
