using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>
    /// Raw view of the dynamic layer's storage: one cell per 5 cm of floor, on the same grid as the static
    /// distance field. The memory belongs to <see cref="BumpGuardCore"/>; this struct only points at it, so the
    /// depth job and the tests reach the same data (contracts F2).
    /// </summary>
    public unsafe struct DynamicGrid
    {
        /// <summary>Highest hit seen in each cell, metres above the floor. 0 = empty.</summary>
        public float* TopHeight;
        /// <summary>Time of the last hit in each cell, seconds.</summary>
        public double* LastHit;
        /// <summary>Per cell: how many depth images have seen it empty since its last hit (low byte) and the sequence number of the last such image (upper bits).</summary>
        public int* EmptyState;
        /// <summary>Number of occupied cells in each block of 8 x 8 cells.</summary>
        public int* BlockCount;
        /// <summary>One int: number of occupied cells in the whole layer.</summary>
        public int* ActiveCount;
        /// <summary>Layout, identical to the static grid.</summary>
        public BumpGridSpec Spec;
        /// <summary>Blocks along +X.</summary>
        public int BlocksX;
        /// <summary>Blocks along +Z.</summary>
        public int BlocksZ;

        /// <summary>True when the layer has storage.</summary>
        public bool IsCreated => TopHeight != null;
    }

    /// <summary>
    /// The dynamic layer of Bump Guard (Master Plan section 4.10): obstacles that live depth has seen and the
    /// static map does not know. A cell keeps its highest hit and the time of its last hit. It is cleared only
    /// when later rays are seen to pass through it in three depth images, or after 30 s without a hit; going out
    /// of view clears nothing.
    /// </summary>
    public static unsafe class DynamicLayer
    {
        const int Shift = BumpConstants.BlockShift;

        /// <summary>Blocks needed along one axis for <paramref name="cells"/> cells.</summary>
        public static int BlocksFor(int cells) => (cells + (1 << Shift) - 1) >> Shift;

        /// <summary>
        /// Marks the cell under floor point <paramref name="p"/> with a hit at <paramref name="height"/> metres
        /// above the floor. Keeps the highest hit and renews the cell's time. False when the point is off the grid.
        /// </summary>
        public static bool Stamp(in DynamicGrid g, float2 p, float height, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float old = g.TopHeight[idx];
            if (old <= 0f)
            {
                g.BlockCount[(j >> Shift) * g.BlocksX + (i >> Shift)]++;
                (*g.ActiveCount)++;
                g.TopHeight[idx] = height;
            }
            else if (height > old)
            {
                g.TopHeight[idx] = height;
            }
            g.LastHit[idx] = time;
            g.EmptyState[idx] = 0;
            return true;
        }

        /// <summary>
        /// Called for a point on a depth ray that goes on to land farther away. When the ray passes below the
        /// stored height of the cell under <paramref name="p"/>, the cell has been seen empty in the depth image
        /// numbered <paramref name="sequence"/>; a cell hit in that same image (at <paramref name="time"/>) is
        /// left alone. The cell is cleared once <see cref="BumpConstants.ClearConfirmFrames"/> images have seen
        /// it empty with no hit between. True when a cell was cleared.
        /// </summary>
        public static bool ClearIfSeenEmpty(in DynamicGrid g, float2 p, float rayHeight, int sequence, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float top = g.TopHeight[idx];
            if (top <= 0f || !(rayHeight < top - BumpConstants.ClearHeightTolerance)) return false;
            if (g.LastHit[idx] == time) return false;

            int mark = (sequence & 0x7FFFFF) + 1;
            int state = g.EmptyState[idx];
            if (state >> 8 == mark) return false; // already counted in this image
            int seen = (state & 0xFF) + 1;
            if (seen >= BumpConstants.ClearConfirmFrames)
            {
                Remove(g, i, j);
                return true;
            }
            g.EmptyState[idx] = (mark << 8) | seen;
            return false;
        }

        /// <summary>
        /// Distance from floor point <paramref name="p"/> to the nearest live dynamic cell, metres: the distance
        /// to the cell's centre less half a cell, never negative. Looks no farther than <see cref="BumpConstants.DynamicQueryRange"/> and
        /// returns positive infinity when nothing is that near. Cells past their 30 s are ignored.
        /// Cost when nothing is near: nine block counters.
        /// </summary>
        public static float Distance(in DynamicGrid g, float2 p, double time, out float2 nearest)
        {
            nearest = p;
            if (*g.ActiveCount == 0) return float.PositiveInfinity;

            float inv = 1f / g.Spec.Cell;
            float u = (p.x - g.Spec.OriginX) * inv;
            float v = (p.y - g.Spec.OriginZ) * inv;
            // One more than the range in cells: distances are taken to cell centres.
            int reach = (int)math.ceil(BumpConstants.DynamicQueryRange * inv) + 1;
            if (!(u >= -reach && v >= -reach && u <= g.Spec.Width + reach && v <= g.Spec.Height + reach))
                return float.PositiveInfinity;

            int ci = (int)math.floor(u);
            int cj = (int)math.floor(v);
            int i0 = math.max(ci - reach, 0), i1 = math.min(ci + reach, g.Spec.Width - 1);
            int j0 = math.max(cj - reach, 0), j1 = math.min(cj + reach, g.Spec.Height - 1);
            if (i0 > i1 || j0 > j1) return float.PositiveInfinity;

            // In cell units, to the centre: the range plus the half cell that is taken off at the end.
            float range = BumpConstants.DynamicQueryRange * inv + 0.5f;
            float best = range * range;
            bool found = false;
            int bi = 0, bj = 0;
            double oldest = time - BumpConstants.DynExpirySeconds;

            for (int bz = j0 >> Shift; bz <= j1 >> Shift; bz++)
            {
                for (int bx = i0 >> Shift; bx <= i1 >> Shift; bx++)
                {
                    if (g.BlockCount[bz * g.BlocksX + bx] == 0) continue;
                    int ja = math.max(j0, bz << Shift), jb = math.min(j1, ((bz + 1) << Shift) - 1);
                    int ia = math.max(i0, bx << Shift), ib = math.min(i1, ((bx + 1) << Shift) - 1);
                    for (int j = ja; j <= jb; j++)
                    {
                        float dz = j + 0.5f - v;
                        float dz2 = dz * dz;
                        if (dz2 > best) continue;
                        int row = j * g.Spec.Width;
                        for (int i = ia; i <= ib; i++)
                        {
                            if (g.TopHeight[row + i] <= 0f || g.LastHit[row + i] < oldest) continue;
                            float dx = i + 0.5f - u;
                            float d2 = dx * dx + dz2;
                            if (d2 <= best)
                            {
                                best = d2;
                                bi = i;
                                bj = j;
                                found = true;
                            }
                        }
                    }
                }
            }

            if (!found) return float.PositiveInfinity;
            nearest = new float2(g.Spec.OriginX + (bi + 0.5f) * g.Spec.Cell, g.Spec.OriginZ + (bj + 0.5f) * g.Spec.Cell);
            return math.max(math.sqrt(best) - 0.5f, 0f) * g.Spec.Cell;
        }

        /// <summary>
        /// Housekeeping: drops cells past their 30 s in the next <paramref name="blocks"/> occupancy blocks,
        /// continuing from <paramref name="cursor"/>. Empty blocks cost one look-up.
        /// </summary>
        public static void SweepExpired(in DynamicGrid g, double time, ref int cursor, int blocks)
        {
            if (*g.ActiveCount == 0) return;
            int total = g.BlocksX * g.BlocksZ;
            double oldest = time - BumpConstants.DynExpirySeconds;
            for (int n = 0; n < blocks; n++)
            {
                if (cursor >= total) cursor = 0;
                int b = cursor++;
                if (g.BlockCount[b] == 0) continue;
                int bx = b % g.BlocksX, bz = b / g.BlocksX;
                int ib = math.min(((bx + 1) << Shift), g.Spec.Width);
                int jb = math.min(((bz + 1) << Shift), g.Spec.Height);
                for (int j = bz << Shift; j < jb; j++)
                    for (int i = bx << Shift; i < ib; i++)
                    {
                        int idx = j * g.Spec.Width + i;
                        if (g.TopHeight[idx] > 0f && g.LastHit[idx] < oldest) Remove(g, i, j);
                    }
            }
        }

        /// <summary>Empties the layer.</summary>
        public static void ClearAll(in DynamicGrid g)
        {
            int cells = g.Spec.Width * g.Spec.Height;
            for (int k = 0; k < cells; k++)
            {
                g.TopHeight[k] = 0f;
                g.EmptyState[k] = 0;
            }
            int blocks = g.BlocksX * g.BlocksZ;
            for (int k = 0; k < blocks; k++) g.BlockCount[k] = 0;
            *g.ActiveCount = 0;
        }

        /// <summary>Stored height of the cell under <paramref name="p"/>, or 0 when empty or off the grid.</summary>
        public static float TopHeightAt(in DynamicGrid g, float2 p)
        {
            return CellOf(g.Spec, p, out int i, out int j) ? g.TopHeight[j * g.Spec.Width + i] : 0f;
        }

        static void Remove(in DynamicGrid g, int i, int j)
        {
            g.TopHeight[j * g.Spec.Width + i] = 0f;
            g.EmptyState[j * g.Spec.Width + i] = 0;
            g.BlockCount[(j >> Shift) * g.BlocksX + (i >> Shift)]--;
            (*g.ActiveCount)--;
        }

        static bool CellOf(in BumpGridSpec s, float2 p, out int i, out int j)
        {
            float inv = 1f / s.Cell;
            float u = (p.x - s.OriginX) * inv;
            float v = (p.y - s.OriginZ) * inv;
            i = (int)math.floor(u);
            j = (int)math.floor(v);
            return u >= 0f && v >= 0f && i < s.Width && j < s.Height;
        }
    }
}
