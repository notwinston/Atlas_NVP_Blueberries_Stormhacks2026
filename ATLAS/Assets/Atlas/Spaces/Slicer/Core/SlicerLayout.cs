using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// Where the Slicer puts things in the room (lane A contract, section 14.3). Everything follows from the
    /// free rectangle of the RoomModel; the room frame has its origin at the rectangle's centre (README F4).
    /// </summary>
    public readonly struct SlicerLayout
    {
        /// <summary>Height of the body's centre above the floor, metres.</summary>
        public const float CentreHeight = 1.25f;
        public const float MinRadius = 0.25f, MaxRadius = 0.5f;
        /// <summary>The w lane stops this far short of the free rectangle's ends in total, and is never longer than MaxLane.</summary>
        public const float LaneMargin = 0.4f, MaxLane = 3.0f;
        /// <summary>A lane shorter than this teaches nothing: the cut is then moved with the stick alone.</summary>
        public const float MinLane = 0.8f;
        public const float LaneWidth = 0.08f;
        /// <summary>Radius of a gaze handle, and the edge of the box that marks it. Metres.</summary>
        public const float HandleRadius = 0.09f, HandleBox = 0.07f;

        /// <summary>Centre of the body, room frame, metres.</summary>
        public readonly float3 Centre;
        /// <summary>Metres per model unit: the body's circumradius in the room.</summary>
        public readonly float Radius;
        /// <summary>False in standing mode and in a room too short for a lane.</summary>
        public readonly bool HasLane;
        /// <summary>x of the lane on the floor. The lane runs along z, centred on z = 0.</summary>
        public readonly float LaneX;
        public readonly float LaneLength;

        SlicerLayout(float3 centre, float radius, bool hasLane, float laneX, float laneLength)
        {
            Centre = centre;
            Radius = radius;
            HasLane = hasLane;
            LaneX = laneX;
            LaneLength = laneLength;
        }

        /// <summary>The layout for a free rectangle of wf by df metres.</summary>
        public static SlicerLayout FromRoom(float wf, float df, bool standing)
        {
            wf = math.max(wf, 0f);
            df = math.max(df, 0f);
            float radius = math.clamp(0.2f * wf, MinRadius, MaxRadius);
            float lane = math.min(df - LaneMargin, MaxLane);
            bool hasLane = !standing && lane >= MinLane;
            return new SlicerLayout(new float3(0.25f * wf, CentreHeight, 0f), radius, hasLane,
                                    -0.25f * wf, hasLane ? lane : 0f);
        }

        /// <summary>Model units of cut per metre walked along the lane: the whole range over the lane's length.</summary>
        public float CutPerMetre
        {
            get { return HasLane ? 2f * SlicerObjects.CutRange / LaneLength : 0f; }
        }

        /// <summary>The cut offset for a walker at floor coordinate z: -CutRange at the lane's near end
        /// (z = -length / 2), +CutRange at its far end, held at the ends beyond them.</summary>
        public float CutAt(float z)
        {
            if (!HasLane) return 0f;
            float t = math.saturate(z / LaneLength + 0.5f);
            return math.lerp(-SlicerObjects.CutRange, SlicerObjects.CutRange, t);
        }

        /// <summary>z on the lane for a cut offset: the inverse of <see cref="CutAt"/>.</summary>
        public float LaneZ(float w)
        {
            return 0.5f * LaneLength * math.clamp(w / SlicerObjects.CutRange, -1f, 1f);
        }

        // The bar and the handles stand clear of the body and of its shadow, which can reach 4/3 of the radius.
        float SideOffset { get { return 1.4f * Radius + 0.15f; } }

        /// <summary>
        /// The w axis bar, the flat twin of the cut (Master Plan section 0.5 rule 6): a vertical ruler beside
        /// the body. This is the point on it for cut offset w.
        /// </summary>
        public float3 BarPoint(float w)
        {
            float t = math.clamp(w / SlicerObjects.CutRange, -1f, 1f);
            return Centre + new float3(0f, Radius * t, -SideOffset);
        }

        /// <summary>Gaze handle at an end of the bar that pushes the cut: sign +1 above, -1 below.</summary>
        public float3 CutHandle(int sign)
        {
            float s = sign >= 0 ? 1f : -1f;
            return BarPoint(s * SlicerObjects.CutRange) + new float3(0f, s * 0.16f, 0f);
        }

        /// <summary>Gaze handle that turns the body in plane 0 (XW), 1 (YW) or 2 (ZW): the minus handle is
        /// the one nearer the body, the plus handle the one beyond it. One row per plane, top to bottom.</summary>
        public float3 TurnHandle(int plane, int sign)
        {
            float row = (1 - math.clamp(plane, 0, 2)) * 0.25f;
            float along = SideOffset + (sign >= 0 ? 0.24f : 0f);
            return Centre + new float3(0f, row, along);
        }

        /// <summary>Gaze chip that steps to the next body: beside the bar, just above the body's centre.</summary>
        public float3 NextObjectChip
        {
            get { return Centre + new float3(0f, 0.14f, -(SideOffset + 0.3f)); }
        }

        /// <summary>Gaze chip that shows and hides the shadow: under the chip for the next body.</summary>
        public float3 ViewChip
        {
            get { return Centre + new float3(0f, -0.14f, -(SideOffset + 0.3f)); }
        }
    }
}
