using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// Triangle budgets of Master Plan sections 8.4 and 8.5, and the counts the mesh builder produces, so a
    /// generator can check a shape against its budget before it builds it.
    /// </summary>
    public static class AtlasMeshBudget
    {
        /// <summary>Per frame, both eyes.</summary>
        public const int FrameTriangles = 500000;
        public const int HomeShell = 3000;
        public const int GluedCell = 1500;
        public const int PortalRoom = 2000;
        public const int SelfAvatar = 600;
        public const int BuiltInFurniture = 1500;
        /// <summary>All generated furniture of a room together.</summary>
        public const int FurnitureTotal = 60000;
        public const int FurnitureMin = 3000, FurnitureMax = 5000;
        /// <summary>At most this many objects receive generated meshes.</summary>
        public const int FurnitureObjects = 20;

        /// <summary>Triangle target per object when n objects receive meshes: clamp(60,000 / n, 3,000, 5,000).</summary>
        public static int FurnitureTarget(int objects)
        {
            return math.clamp(FurnitureTotal / math.max(objects, 1), FurnitureMin, FurnitureMax);
        }

        /// <summary>Equal segments a length is cut into so that none is longer than maxEdge. At least 1.</summary>
        public static int Segments(float length, float maxEdge)
        {
            if (!(maxEdge > 0f) || !(length > 0f)) return 1;
            // The small allowance keeps a length that is an exact multiple from gaining a segment to rounding.
            return math.max(1, (int)math.ceil(length / maxEdge - 1e-4f));
        }

        /// <summary>The same, rounded up to a power of two: what repeated halving of the length arrives at.</summary>
        public static int DyadicSegments(float length, float maxEdge)
        {
            int n = 1;
            if (!(maxEdge > 0f) || !(length > 0f)) return n;
            while (length / n > maxEdge * (1f + 1e-4f) && n < (1 << 20)) n *= 2;
            return n;
        }

        /// <summary>Triangles of AddQuadGrid for a quad with these side lengths.</summary>
        public static int QuadGridTriangles(float lengthU, float lengthV, float maxEdge = AtlasMeshBuilder.MaxEdge)
        {
            return 2 * Segments(lengthU, maxEdge) * Segments(lengthV, maxEdge);
        }

        /// <summary>Triangles of AddBox for a box of this size.</summary>
        public static int BoxTriangles(float3 size, bool withBottom = false, float maxEdge = AtlasMeshBuilder.MaxEdge)
        {
            int nx = Segments(size.x, maxEdge), ny = Segments(size.y, maxEdge), nz = Segments(size.z, maxEdge);
            int caps = withBottom ? 2 : 1;
            return 2 * (caps * nx * nz + 2 * nx * ny + 2 * nz * ny);
        }

        /// <summary>True when the triangle count is within the budget.</summary>
        public static bool Fits(int triangles, int budget)
        {
            return triangles <= budget;
        }
    }
}
