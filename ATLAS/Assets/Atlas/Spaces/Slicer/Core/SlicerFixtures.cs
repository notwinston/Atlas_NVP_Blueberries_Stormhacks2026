using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The still parts of the Slicer: the w lane on the floor, the w axis bar beside the body (the flat twin
    /// of the cut, Master Plan section 0.5 rule 6). The gaze handles are drawn as buttons (SlicerButtons). Built
    /// once per room; never warped, so the 0.25 m edge rule does not apply.
    /// </summary>
    public static class SlicerFixtures
    {
        /// <summary>Height of the lane above the floor: clear of the room's floor mesh, with no z-fighting.</summary>
        public const float LaneLift = 0.012f;
        public const float BarThickness = 0.025f;

        // Nothing here is ever warped, so no face is split: a box is twelve triangles.
        const float NoSplit = 1000f;

        /// <summary>The lane and the bar, in room metres.</summary>
        public static void Build(in SlicerLayout layout, AtlasMeshBuilder into)
        {
            if (layout.HasLane) AddLane(layout, into);

            // The bar: a thin post from the lowest to the highest cut, with a tick at w = -1, 0 and +1.
            float3 foot = layout.BarPoint(-SlicerObjects.CutRange);
            float3 head = layout.BarPoint(SlicerObjects.CutRange);
            float2 neutral = AtlasPalette.Uv(AtlasPalette.Text);
            into.AddBox(foot, new float3(BarThickness, head.y - foot.y, BarThickness), 0f, neutral, true, 0, NoSplit);
            for (int tick = -1; tick <= 1; tick++)
            {
                float3 at = layout.BarPoint(tick);
                into.AddBox(at - new float3(0f, 0.004f, 0f), new float3(0.035f, 0.008f, tick == 0 ? 0.14f : 0.09f),
                            0f, neutral, true, 0, NoSplit);
            }
        }

        /// <summary>The marker that slides along the bar and along the lane: a flat box about the origin.</summary>
        public static void BuildMarker(AtlasMeshBuilder into)
        {
            const float height = 0.02f;
            into.AddBox(new float3(0f, -0.5f * height, 0f), new float3(0.07f, height, 0.14f), 0f,
                        AtlasPalette.Uv(AtlasPalette.SliceEdge), true, 0, NoSplit);
        }

        // The lane is one strip whose colour runs along the w-depth strip, like the shadow and the bar's ends:
        // the same colour means the same w everywhere in the space.
        static void AddLane(in SlicerLayout layout, AtlasMeshBuilder into)
        {
            float x0 = layout.LaneX - 0.5f * SlicerLayout.LaneWidth, x1 = layout.LaneX + 0.5f * SlicerLayout.LaneWidth;
            float z0 = -0.5f * layout.LaneLength, z1 = 0.5f * layout.LaneLength;
            float3 up = new float3(0f, 1f, 0f);
            float2 near = SlicerMeshKernel.DepthUv(layout.CutAt(z0)), far = SlicerMeshKernel.DepthUv(layout.CutAt(z1));
            int a = into.AddVertex(new float3(x0, LaneLift, z0), up, near, float2.zero, 1f, 0);
            int b = into.AddVertex(new float3(x0, LaneLift, z1), up, far, float2.zero, 1f, 0);
            int c = into.AddVertex(new float3(x1, LaneLift, z1), up, far, float2.zero, 1f, 0);
            int d = into.AddVertex(new float3(x1, LaneLift, z0), up, near, float2.zero, 1f, 0);
            into.AddTriangle(a, b, c);
            into.AddTriangle(a, c, d);
        }
    }
}
