using Atlas.Link;

namespace Atlas.World
{
    // P1-B09 [D4]: the two registry parameters of the curvature dial, shared by S2 and S4 (Spaces may not reference each other, MP §6.2).
    // curved.q is the dial snap (MP §3.2, "the default" is 5); curved.strong lifts the comfort floors (MP §4.4).
    public static class CurvedDialParams
    {
        public const string ParamQ = "curved.q", ParamStrong = "curved.strong";
        public const string Group = "curved";

        public static ushort RegisterQ(ParamRegistry registry)
            => registry.Register(ParamQ, Group, 3, 6, 5, false);

        public static ushort RegisterStrong(ParamRegistry registry)
            => registry.Register(ParamStrong, Group, 0, 1, 0, true);

        public static bool Strong(ParamRegistry registry, ushort strongId) => registry.Get(strongId) >= 0.5f;
    }
}
