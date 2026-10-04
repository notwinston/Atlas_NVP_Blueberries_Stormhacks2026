using Atlas.Link;

namespace Atlas.World
{
    // P1-B09 [D4]: the two registry parameters of the curvature dial, shared by S2 and S4 (Spaces may not reference each other, MP §6.2).
    // curved.q is the dial snap (MP §3.2, "the default" is 5); curved.strong lifts the comfort floors (MP §4.4).
    public static class CurvedDialParams
    {
        public const string ParamQ = "curved.q", ParamStrong = "curved.strong";
        public const string Group = "curved";

        // Redeclaring a name returns the same entry, so S2 and S4 may both call these from every Configure.
        public static ParamEntry RegisterQ(ParamRegistry registry)
            => registry.DeclareInt(ParamQ, Group, 3, 6, 5, null, false);

        public static ParamEntry RegisterStrong(ParamRegistry registry)
            => registry.DeclareBool(ParamStrong, Group, false, null, true);

        public static bool Strong(ParamEntry strong) => strong.Bool;
    }
}
