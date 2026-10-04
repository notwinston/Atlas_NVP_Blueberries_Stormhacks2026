using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// A named place about the near anchor (documents/ATLAS_Hands_Design.md section 6.1). In a hands session
    /// five separately owned groups come within reach: the on-device menu panel, the chip that opens it, the
    /// four SpaceLab signs, the dial and mode chips of Curved Walk and the dial chip of Small Planet. One
    /// anchor and one scale do not say where each of them goes, so every target of every group has its place
    /// here and none lands on another.
    /// </summary>
    public enum ReachSlot : byte
    {
        /// <summary>The open on-device menu panel. Its centre is the anchor's origin.</summary>
        MenuPanel = 0,
        /// <summary>The chip that opens the on-device menu.</summary>
        MenuChip = 1,
        /// <summary>The home beacon of the SpaceLab signs.</summary>
        SignHome = 2,
        /// <summary>The previous-space sign.</summary>
        SignPrevious = 3,
        /// <summary>The exhibit sign, which opens the menu on its Spaces page.</summary>
        SignMenu = 4,
        /// <summary>The next-space sign.</summary>
        SignNext = 5,
        /// <summary>The dial's lower detent (Curved Walk).</summary>
        DialLeft = 6,
        /// <summary>The dial's upper detent: Curved Walk's, and the one detent of Small Planet.</summary>
        DialRight = 7,
        /// <summary>The mode chip of Curved Walk.</summary>
        Mode = 8,
        /// <summary>The Playground's prompt chip: one wide line above the grid.</summary>
        PromptChip = 9,
        /// <summary>The Playground's caption chip: one wide line above the prompt chip.</summary>
        CaptionChip = 10
    }

    /// <summary>
    /// Where each group stands about the near anchor, and what a view needs to stand there (section 6.1). A
    /// view places itself as it always has, then asks <see cref="TryGetNear"/>; on true it stands at its slot
    /// in the anchor's frame, with its own layout multiplied by the scale.
    /// <para>
    /// The table is a grid of three columns and three rows about the anchor, in the anchor's plane:
    /// <code>
    ///              x = -Pitch      x = 0           x = +Pitch
    /// y = +Pitch   menu chip       (free)          mode chip
    /// y = 0        dial, down      home beacon     dial, up
    /// y = -Pitch   previous sign   exhibit sign    next sign
    /// </code>
    /// The open menu panel is centred on the anchor and covers the grid, so everything else hides while it
    /// is open. At the usual scale of 0.75 the grid's pitch is 14 cm: the eight small targets (spheres 9 to
    /// 10.5 cm across) stand at least 3.7 cm apart and lie within 19.5 cm of the anchor to each side. The
    /// panel alone is wider: 0.70 m times 0.75 is 26 cm to each side.
    /// </para>
    /// Engine-free; allocates nothing.
    /// </summary>
    public static class ReachSlots
    {
        /// <summary>Number of slots: the values of <see cref="ReachSlot"/> run from 0 to Count - 1.</summary>
        public const int Count = 11;
        /// <summary>Distance between neighbouring columns, and between neighbouring rows, metres before scaling.</summary>
        public const float Pitch = 0.19f;
        /// <summary>No target within reach is smaller than this across after scaling, metres: a fingertip must find it.</summary>
        public const float MinTargetMetres = 0.03f;

        // A view makes its targets again when the scale or the rotation they were made with has moved by more
        // than these, and only moves them otherwise: an anchor that trembles in its last bits must not take
        // the dwell and the touch away every frame. 0.16 degrees is under a millimetre at the panel's edge.
        const float ScaleTolerance = 1e-4f;
        const float RotationTolerance = 1e-6f;

        // x right, y up along the panel, z towards the walker; metres before scaling.
        static readonly float3[] s_centres =
        {
            new float3(0f, 0f, 0f),             // MenuPanel
            new float3(-Pitch, Pitch, 0f),      // MenuChip
            new float3(0f, 0f, 0f),             // SignHome
            new float3(-Pitch, -Pitch, 0f),     // SignPrevious
            new float3(0f, -Pitch, 0f),         // SignMenu
            new float3(Pitch, -Pitch, 0f),      // SignNext
            new float3(-Pitch, 0f, 0f),         // DialLeft
            new float3(Pitch, 0f, 0f),          // DialRight
            new float3(Pitch, Pitch, 0f),       // Mode
            new float3(0f, 2f * Pitch, 0f),     // PromptChip
            new float3(0f, 3f * Pitch, 0f),     // CaptionChip
        };

        /// <summary>
        /// Centre of a slot about the anchor: x to the walker's right, y up along the panel, z towards the
        /// walker. Metres, before scaling.
        /// </summary>
        public static float3 Centre(ReachSlot slot)
        {
            return s_centres[(int)slot];
        }

        /// <summary>
        /// True for a slot whose group is not drawn and not live while the menu panel is open: every slot but
        /// the panel's own, because the open panel stands on all of them.
        /// </summary>
        public static bool HidesWhileMenuOpen(ReachSlot slot)
        {
            return slot != ReachSlot.MenuPanel;
        }

        /// <summary>
        /// A place given as the table gives its centres, in the anchor's own axes after scaling: +X right, +Y up
        /// along the panel, +Z away from the walker (IReachLayout), so the table's z, which runs towards the
        /// walker, changes sign.
        /// </summary>
        public static float3 Local(float3 place, float scale)
        {
            return scale * new float3(place.x, place.y, -place.z);
        }

        /// <summary>Centre of a slot in the anchor's own axes, after scaling.</summary>
        public static float3 Local(ReachSlot slot, float scale)
        {
            return Local(s_centres[(int)slot], scale);
        }

        /// <summary>Centre of a slot in the room frame, for the anchor's pose and scale given.</summary>
        public static float3 World(float3 position, quaternion rotation, float scale, ReachSlot slot)
        {
            return position + math.mul(rotation, Local(slot, scale));
        }

        /// <summary>
        /// Half the size of a gaze target within reach: its usual half size (a radius, half a width, half a
        /// height) times the scale, and never under half of <see cref="MinTargetMetres"/>.
        /// </summary>
        public static float TargetHalf(float half, float scale)
        {
            return math.max(half * scale, 0.5f * MinTargetMetres);
        }

        /// <summary>
        /// What a view asks once in its step 6, after it has placed itself as it always has. True when a
        /// layout is published, says Near and gives a pose that can be used: the anchor's pose and scale of
        /// this frame come back, the rotation at unit length. False (no service, Near false, or numbers that
        /// are not a pose) leaves the view exactly where it is.
        /// </summary>
        public static bool TryGetNear(out float3 position, out quaternion rotation, out float scale)
        {
            position = float3.zero;
            rotation = quaternion.identity;
            scale = 1f;
            IReachLayout layout;
            if (!AtlasServices.TryGet(out layout) || !layout.Near) return false;

            float3 p = layout.Position;
            quaternion r = layout.Rotation;
            float s = layout.Scale;
            float lengthSq = math.lengthsq(r.value);
            // Written so that a NaN fails every test.
            if (!(s > 0f) || float.IsInfinity(s) || !math.all(math.isfinite(p))) return false;
            if (!(lengthSq > 1e-6f) || float.IsInfinity(lengthSq)) return false;

            position = p;
            rotation = math.abs(lengthSq - 1f) <= 1e-5f ? r : new quaternion(r.value * math.rsqrt(lengthSq));
            scale = s;
            return true;
        }

        /// <summary>
        /// True when targets made for one scale no longer fit another: their radius or half size cannot be
        /// changed in place, so their owner makes them again.
        /// </summary>
        public static bool ScaleMoved(float applied, float now)
        {
            return !(math.abs(now - applied) <= ScaleTolerance * math.max(math.abs(applied), math.abs(now)));
        }

        /// <summary>
        /// True when rectangle targets made with one rotation no longer fit another (more than 0.16 degrees
        /// apart): a rectangle cannot be turned in place, so its owner makes it again.
        /// </summary>
        public static bool RotationMoved(quaternion applied, quaternion now)
        {
            return !(math.abs(math.dot(applied.value, now.value)) >= 1f - RotationTolerance);
        }
    }
}
