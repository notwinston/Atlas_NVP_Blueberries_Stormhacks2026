using System;
using Atlas.Link;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The toybox's two switches (tasks P2-A01, P2-A02), in group "slicer" beside the contract's eight (lane A contract,
    /// section 14.3, which lists those eight; these two are additions to announce): <c>slicer.lookDown</c>, the look-down
    /// shadow, and <c>slicer.tour</c>, the guided steps on the panel. Both are on by default and both are kill switches.
    /// </summary>
    public sealed class SlicerToyboxParams
    {
        public const string LookDown = "slicer.lookDown", Tour = "slicer.tour";

        readonly ParamEntry _lookDown, _tour;

        SlicerToyboxParams(ParamRegistry registry)
        {
            _lookDown = registry.DeclareBool(LookDown, SlicerParams.Group, true);
            _tour = registry.DeclareBool(Tour, SlicerParams.Group, true);
        }

        /// <summary>Declares the two switches, or finds them again with their current values.</summary>
        public static SlicerToyboxParams Declare(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return new SlicerToyboxParams(registry);
        }

        public bool LookDownEnabled { get { return _lookDown.Bool; } }
        public bool TourShown { get { return _tour.Bool; } }
    }
}
