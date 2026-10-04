using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// Layout of a floor grid in the room frame (contracts F4): cell (i, j) covers
    /// x in [OriginX + i * Cell, OriginX + (i + 1) * Cell) and likewise in z. Values are row-major with row 0 at
    /// the lowest z: index = j * Width + i. This is the layout of the RoomModel distance field (section 4.3).
    /// </summary>
    public struct BumpGridSpec
    {
        /// <summary>Cells along +X.</summary>
        public int Width;
        /// <summary>Cells along +Z.</summary>
        public int Height;
        /// <summary>Cell size, metres. 0.05 for the RoomModel.</summary>
        public float Cell;
        /// <summary>x of the low corner of cell (0, 0), metres.</summary>
        public float OriginX;
        /// <summary>z of the low corner of cell (0, 0), metres.</summary>
        public float OriginZ;

        public BumpGridSpec(int width, int height, float cell, float originX, float originZ)
        {
            Width = width;
            Height = height;
            Cell = cell;
            OriginX = originX;
            OriginZ = originZ;
        }

        /// <summary>True when the grid can be sampled.</summary>
        public bool IsValid => Width > 0 && Height > 0 && Cell > 0f;
    }

    /// <summary>
    /// Static clearance: bilinear look-up of the RoomModel distance field at a floor point in the room frame
    /// (Master Plan section 4.10, "static SDF at p"). The field holds metres at cell centres, positive inside the
    /// free floor.
    /// </summary>
    public static unsafe class StaticClearance
    {
        /// <summary>
        /// Clearance at <paramref name="p"/> (x, z), metres. A point outside the grid, or a point that is not a
        /// number, counts as blocked and returns <see cref="BumpConstants.BlockedClearance"/>.
        /// </summary>
        public static float Sample(float* sdf, in BumpGridSpec grid, float2 p)
        {
            float inv = 1f / grid.Cell;
            float u = (p.x - grid.OriginX) * inv;
            float v = (p.y - grid.OriginZ) * inv;
            // Written so that NaN fails the test.
            if (!(u >= 0f && v >= 0f && u <= grid.Width && v <= grid.Height))
                return BumpConstants.BlockedClearance;
            return Bilinear(sdf, grid, u, v);
        }

        /// <summary>
        /// Signed distance at <paramref name="p"/> that keeps going outside the grid: the value at the nearest
        /// point of the grid, less the distance to it. Used by the rescan check, which must measure how far
        /// beyond the mapped wall a depth hit lies.
        /// </summary>
        public static float SampleExtended(float* sdf, in BumpGridSpec grid, float2 p)
        {
            float inv = 1f / grid.Cell;
            float u = (p.x - grid.OriginX) * inv;
            float v = (p.y - grid.OriginZ) * inv;
            float uc = math.clamp(u, 0f, grid.Width);
            float vc = math.clamp(v, 0f, grid.Height);
            float outside = math.sqrt((u - uc) * (u - uc) + (v - vc) * (v - vc)) * grid.Cell;
            return Bilinear(sdf, grid, uc, vc) - outside;
        }

        /// <summary>
        /// Direction of increasing clearance at <paramref name="p"/> (towards safety), by central differences
        /// one cell apart. Zero where the field is flat. Not normalised.
        /// </summary>
        public static float2 Gradient(float* sdf, in BumpGridSpec grid, float2 p)
        {
            float h = grid.Cell;
            float dx = SampleExtended(sdf, grid, new float2(p.x + h, p.y)) - SampleExtended(sdf, grid, new float2(p.x - h, p.y));
            float dz = SampleExtended(sdf, grid, new float2(p.x, p.y + h)) - SampleExtended(sdf, grid, new float2(p.x, p.y - h));
            return new float2(dx, dz) / (2f * h);
        }

        // u, v in cell units from the grid's low corner, already inside [0, Width] x [0, Height].
        static float Bilinear(float* sdf, in BumpGridSpec grid, float u, float v)
        {
            // Values sit at cell centres; the half-cell rim outside the outer centres repeats the edge value.
            float fu = math.clamp(u - 0.5f, 0f, grid.Width - 1);
            float fv = math.clamp(v - 0.5f, 0f, grid.Height - 1);
            int i0 = (int)fu;
            int j0 = (int)fv;
            int i1 = math.min(i0 + 1, grid.Width - 1);
            int j1 = math.min(j0 + 1, grid.Height - 1);
            float tu = fu - i0;
            float tv = fv - j0;
            float* r0 = sdf + j0 * grid.Width;
            float* r1 = sdf + j1 * grid.Width;
            float a = r0[i0] + (r0[i1] - r0[i0]) * tu;
            float b = r1[i0] + (r1[i1] - r1[i0]) * tu;
            return a + (b - a) * tv;
        }
    }
}
