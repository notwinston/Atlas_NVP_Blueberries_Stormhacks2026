using Atlas.Rendering;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Shell
{
    /// <summary>
    /// Beat 4, Centre (Master Plan section 10.1.2): a floor disc marks the free-rectangle centre c0, and the beat
    /// ends when the head is within 0.3 m of it. In the room frame c0 is the origin. Engine-free.
    /// </summary>
    public sealed class CentreMark : IBeatStep
    {
        /// <summary>Metres: the head must come this close to c0 (derived, plan beat 4). The disc has this radius.</summary>
        public const float EnterRadius = 0.3f;
        /// <summary>Metres above the floor, as the Home marks (clear of the floor in the depth buffer).</summary>
        public const float Height = 0.005f;
        /// <summary>Segments around the disc.</summary>
        public const int Segments = 32;

        /// <summary>The centre of the free rectangle on the floor, room frame: the origin.</summary>
        public static readonly float2 Centre = float2.zero;

        bool m_Within;

        public PlaygroundBeat Beat { get { return PlaygroundBeat.Centre; } }
        public bool Complete { get { return m_Within; } }

        /// <summary>Metres from the head to c0 at the last update; NaN before the first.</summary>
        public float Distance { get; private set; } = float.NaN;

        public void Enter()
        {
            m_Within = false;
            Distance = float.NaN;
        }

        /// <summary>Per frame, with the head's floor point (x, z) of the room frame.</summary>
        public void Update(float2 headFloor)
        {
            Distance = math.distance(headFloor, Centre);
            // Written so that a NaN position never completes the beat.
            if (Distance <= EnterRadius) m_Within = true;
        }

        /// <summary>The disc: a fan of <see cref="Segments"/> triangles at c0.</summary>
        public static void Build(AtlasMeshBuilder into)
        {
            float2 uv = AtlasPalette.Uv(AtlasPalette.HomeBeacon);
            float3 up = new float3(0f, 1f, 0f);
            float3 lift = new float3(Centre.x, Height, Centre.y);
            into.Reserve(Segments + 1, 3 * Segments);
            int centre = into.AddVertex(lift, up, uv, float2.zero, 1f, 0);
            int first = into.VertexCount;
            for (int k = 0; k < Segments; k++)
            {
                float angle = 2f * math.PI * k / Segments;
                into.AddVertex(lift + new float3(math.cos(angle), 0f, math.sin(angle)) * EnterRadius, up, uv, float2.zero, 1f, 0);
            }
            // The angle runs from +X towards +Z, so a face seen from above lists its corners against it.
            for (int k = 0; k < Segments; k++)
                into.AddTriangle(centre, first + (k + 1) % Segments, first + k);
        }
    }
}
