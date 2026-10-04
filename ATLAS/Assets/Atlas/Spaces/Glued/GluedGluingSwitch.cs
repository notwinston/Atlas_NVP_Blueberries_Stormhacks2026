using Atlas.Input;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// The gluing switch of S1 (Master Plan task P2-C14): T2, Klein and T3 chosen by the bumpers, by dwelling on one
    /// of the three buttons of the gluing sign, or by a click of the headset lever (the action twins of section
    /// 10.1.3). It turns an action into the glued.mode value to set. Engine-free.
    /// </summary>
    public static class GluedGluingSwitch
    {
        /// <summary>Label of the gluing sign's gaze targets.</summary>
        public const string SignLabel = "gluing";

        /// <summary>The gaze tag of the sign's button for a gluing: 1 + the glued.mode value. Tag 0 is a bumper.</summary>
        public static int SignTag(GluedLayout layout) { return 1 + (int)layout; }

        /// <summary>
        /// The mode a fired action asks for, or -1 for none. VariantPrev and VariantNext are the bumpers and the two
        /// ends of the sign; they step round the three modes, except that a button of the sign (a tag from
        /// <see cref="SignTag"/>) picks its own gluing. A lever click arrives as Confirm and steps on only when
        /// `leverSteps` is set, which is the case once the wave task is over: until then Confirm is the prompt's.
        /// </summary>
        public static int Next(AtlasAction action, InputSource source, int current, bool leverSteps, int tag = 0)
        {
            int count = GluedRules.LayoutNames.Length;
            if ((action == AtlasAction.VariantNext || action == AtlasAction.VariantPrev) && tag >= 1 && tag <= count)
                return tag - 1;
            switch (action)
            {
                case AtlasAction.VariantNext: return Wrap(current + 1, count);
                case AtlasAction.VariantPrev: return Wrap(current - 1, count);
                case AtlasAction.Confirm:
                    return source == InputSource.Lever && leverSteps ? Wrap(current + 1, count) : -1;
                default: return -1;
            }
        }

        static int Wrap(int value, int count)
        {
            return ((value % count) + count) % count;
        }
    }
}
