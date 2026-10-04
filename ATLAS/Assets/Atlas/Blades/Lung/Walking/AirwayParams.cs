using Atlas.Link;

namespace Atlas.Blades.Lung.Walking
{
    // MP §12.4 kill switch and G6 §3.7. curved.mode is lane A's declaration (AppParams.DeclareUnownedKillSwitches: an Enum, index 0 = Mode 1
    // Curved Walk, 1 = Mode 2 Aligned Cells); the airway wing reads it and declares it only when nothing has (a bare registry).
    public static class AirwayParams
    {
        public const string CurvedMode = "curved.mode";
        static readonly string[] s_modeNames = { "Mode 1 Curved Walk", "Mode 2 Aligned Cells" };   // as AppParams

        public static ParamEntry DeclareCurvedMode(ParamRegistry registry) =>
            registry.Find(CurvedMode) ?? registry.DeclareEnum(CurvedMode, "curved", s_modeNames, 0, null, true);

        // The wire index is 0-based, AirwayMode (G6 DV-G6-8) is 1 and 2.
        public static AirwayMode ModeOf(ParamEntry entry) => (AirwayMode)(entry.Int + 1);

        public static int IndexOf(AirwayMode mode) => (int)mode - 1;
    }
}
