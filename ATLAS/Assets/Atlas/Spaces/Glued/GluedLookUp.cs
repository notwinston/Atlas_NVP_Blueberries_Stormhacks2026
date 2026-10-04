using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>
    /// The look-up of the 3-torus task (Master Plan task P2-C14): in T3 the walker's copy stands one vertical
    /// period (2.6 m) above, straight overhead, and looking up past 60 degrees shows it. Engine-free.
    /// </summary>
    public static class GluedLookUp
    {
        /// <summary>The pitch above the horizon past which the copy overhead is in view, degrees (Master Plan P2-C14).</summary>
        public const float PitchDeg = 60f;

        /// <summary>
        /// Half the angle of the view that shows the copy: the copy overhead sits at 90 degrees, so a head pitched
        /// past <see cref="PitchDeg"/> has it inside this cone.
        /// </summary>
        public const float ViewHalfAngleDeg = 90f - PitchDeg;

        /// <summary>Degrees above the horizon the head looks: +90 straight up, -90 straight down.</summary>
        public static float PitchOf(quaternion head)
        {
            float3 forward = math.mul(head, new float3(0f, 0f, 1f));
            return math.degrees(math.asin(math.clamp(forward.y, -1f, 1f)));
        }

        /// <summary>
        /// True when the copy of the walker in the cell above is inside the view cone: the angle between
        /// the gaze and the direction to the copy's head is at most <see cref="ViewHalfAngleDeg"/>. Only the
        /// 3-torus has such copies; the torus and the Klein bottle give false.
        /// </summary>
        public static bool AvatarVisible(in GluedLattice lattice, GluedCell walker, float3 head, quaternion rotation)
        {
            if (lattice.Layout != GluedLayout.Torus3) return false;
            float3 forward = math.normalize(math.mul(rotation, new float3(0f, 0f, 1f)));
            float cosLimit = math.cos(math.radians(ViewHalfAngleDeg));
            // Only the copy a period above: the task is to look up. The copy below is seen by looking down.
            var cell = new GluedCell(walker.Col, walker.Row, walker.Layer + 1);
            float3 copy = lattice.ImageOf(walker, cell, head, out bool _);
            float3 toCopy = math.normalize(copy - head);
            // A margin of a thousandth keeps the 60 degree row exact against rounding.
            return math.dot(forward, toCopy) > cosLimit + 1e-3f;
        }
    }
}
