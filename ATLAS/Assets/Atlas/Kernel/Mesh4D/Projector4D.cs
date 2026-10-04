using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>
    /// The shadow verb (Master Plan section 5.1): flattens 4D points to 3D. The shadow is drawn with the
    /// mesh's fixed <see cref="Mesh4D.Edges"/> (wire) or its cell-boundary triangles (solid), so only the
    /// positions change from frame to frame.
    /// </summary>
    public static unsafe class Projector4D
    {
        /// <summary>Distance of the 4D eye along +w, model units (twice the circumradius).</summary>
        public const float DefaultFocal = 2f;

        /// <summary>
        /// Perspective shadow: p.xyz * focal / (focal - p.w). Points with w = 0 keep their size and points
        /// nearer the eye grow: the cube within a cube. The depth is held above 5% of the focal distance so a
        /// vertex that reaches the eye stays finite.
        /// </summary>
        public static float3 Perspective(float4 p, float focal = DefaultFocal)
        {
            return p.xyz * (focal / math.max(focal - p.w, 0.05f * focal));
        }

        /// <summary>Orthographic shadow: p.xyz.</summary>
        public static float3 Orthographic(float4 p) => p.xyz;

        /// <summary>
        /// Shadow of a whole mesh: every vertex is turned by <paramref name="modelToSlice"/>, moved by
        /// <paramref name="translation"/> and projected. Writes one position per vertex and, when
        /// <paramref name="outW"/> is not null, the w it had before projection (to tint the shadow by depth).
        /// No allocation.
        /// </summary>
        public static void ProjectVertices(float4* vertices, int vertexCount,
                                           in float4x4 modelToSlice, in float4 translation,
                                           bool perspective, float focal,
                                           float3* outPositions, float* outW)
        {
            for (int i = 0; i < vertexCount; i++)
            {
                float4 p = math.mul(modelToSlice, vertices[i]) + translation;
                outPositions[i] = perspective ? Perspective(p, focal) : p.xyz;
                if (outW != null) outW[i] = p.w;
            }
        }
    }
}
