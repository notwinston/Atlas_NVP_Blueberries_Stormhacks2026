using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Room
{
    /// <summary>
    /// Stand-in furniture for the budget fixture (Master Plan section 8.5): the ellipsoid inscribed in an
    /// object's box, always exactly 5,000 triangles, so the full furniture load can be measured without the
    /// Forge. Engine-free and deterministic: the same box gives the same vertices in the same order.
    /// </summary>
    public static class StandInMeshBuilder
    {
        public const int Segments = 50, Bands = 51;
        public const int Triangles = 2 * Segments * (Bands - 1);      // 5,000
        public const int Vertices = 2 + Segments * (Bands - 1);       // 2,502
        /// <summary>Largest box side for which no edge is over 0.25 m, metres.</summary>
        public const float MaxBoxSide = 3.5f;

        /// <summary>
        /// Appends the stand-in for a box of the given size (w, h, d) in the box's local frame: origin at the
        /// centre of the base, +Y up. Tinted by category; occlusion darkens it towards the floor.
        /// </summary>
        public static void Build(float3 size, ObjectCategory category, AtlasMeshBuilder data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            float a = 0.5f * math.max(size.x, 1e-3f), b = 0.5f * math.max(size.y, 1e-3f), c = 0.5f * math.max(size.z, 1e-3f);
            float2 uv0 = AtlasPalette.Uv(AtlasPalette.Category(ObjectCategories.TintIndex(category)));
            data.Reserve(Vertices, Triangles * 3);

            int top = data.AddVertex(new float3(0f, 2f * b, 0f), new float3(0f, 1f, 0f), uv0, float2.zero, 1f, 0);
            int firstRing = data.VertexCount;
            for (int k = 1; k < Bands; k++)
            {
                math.sincos(math.PI * k / Bands, out float sinTheta, out float cosTheta);
                for (int j = 0; j < Segments; j++)
                {
                    math.sincos(2f * math.PI * j / Segments, out float sinLambda, out float cosLambda);
                    var p = new float3(a * sinTheta * cosLambda, b + b * cosTheta, c * sinTheta * sinLambda);
                    float3 normal = math.normalize(new float3(p.x / (a * a), (p.y - b) / (b * b), p.z / (c * c)));
                    data.AddVertex(p, normal, uv0, float2.zero, 0.6f + 0.4f * p.y / (2f * b), 0);
                }
            }
            int bottom = data.AddVertex(float3.zero, new float3(0f, -1f, 0f), uv0, float2.zero, 0.6f, 0);

            for (int j = 0; j < Segments; j++)
                data.AddTriangle(top, Ring(firstRing, 1, j + 1), Ring(firstRing, 1, j));
            for (int k = 1; k < Bands - 1; k++)
            {
                for (int j = 0; j < Segments; j++)
                {
                    int p00 = Ring(firstRing, k, j), p01 = Ring(firstRing, k, j + 1);
                    int p10 = Ring(firstRing, k + 1, j), p11 = Ring(firstRing, k + 1, j + 1);
                    data.AddTriangle(p00, p01, p11);
                    data.AddTriangle(p00, p11, p10);
                }
            }
            for (int j = 0; j < Segments; j++)
                data.AddTriangle(bottom, Ring(firstRing, Bands - 1, j), Ring(firstRing, Bands - 1, j + 1));
        }

        static int Ring(int firstRing, int k, int j)
        {
            return firstRing + (k - 1) * Segments + j % Segments;
        }
    }
}
