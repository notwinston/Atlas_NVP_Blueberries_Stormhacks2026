using Atlas.Input;
using Atlas.Spaces.Glued;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>
    /// The gluing switch of the Glued exhibit (Master Plan task P2-C14): T2, Klein and T3 chosen by the
    /// bumpers, by dwelling on the gluing sign, or by a click of the headset lever (the action twins of
    /// section 10.1.3). It turns an action into the glued.mode value to set. Engine-free.
    /// </summary>
    public static class GluedGluingSwitch
    {
        /// <summary>Label of the gluing sign's gaze target.</summary>
        public const string SignLabel = "gluing";

        /// <summary>
        /// The mode a fired action asks for, or -1 for none. VariantPrev and VariantNext are the bumpers and the two
        /// ends of the sign; they step round the three modes. A lever click arrives as Confirm and steps on
        /// only when `leverSteps` is set, which is the case once the wave task is over: until then Confirm is the
        /// prompt's.
        /// </summary>
        public static int Next(AtlasAction action, InputSource source, int current, bool leverSteps)
        {
            int count = GluedRules.LayoutNames.Length;
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
