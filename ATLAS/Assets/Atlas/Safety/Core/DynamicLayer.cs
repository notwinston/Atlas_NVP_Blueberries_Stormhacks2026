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
        /// <summary>
        /// Time each cell was first stamped, seconds: written when a cell goes from empty to stamped, or starts
        /// again, and kept while later hits renew <see cref="LastHit"/>. One clock for the two rules of the
        /// walker's hands: the young-cell rule of the depth mask counts its second from it, the pending rule
        /// its <see cref="BumpConstants.PendingSeconds"/> (see <see cref="DynamicLayer"/>).
        /// </summary>
        public double* FirstHit;
        /// <summary>
        /// Per cell: 1 while the cell is pending (decision R13): stored, and not counted by
        /// <see cref="DynamicLayer.Distance"/> until it has been seen again. 0 for a cell that counts and for an
        /// empty one. No cell becomes pending outside a hands session.
        /// </summary>
        public byte* Pending;
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
    /// of view clears nothing. Two more rules exist for the walker's own hands; the first never runs without a
    /// depth mask, the second never outside a hands session. The young-cell rule (<see cref="ForgetIfYoung"/>)
    /// forgets a cell under a masked sample, and reaches only cells that are under a second old. The pending rule
    /// (<see cref="Stamp(in DynamicGrid, float2, float, double, bool)"/>, decision R13) holds back a cell that
    /// a near return made in a hands session: it is stored, and <see cref="Distance"/>, which is all that
    /// clear(p), the level, the two paths and the clearance query read, does not count it until a hit that
    /// comes <see cref="BumpConstants.PendingSeconds"/> or more after its first.
    /// <para>How the two rules share a cell's first stamp. There is one clock, <see cref="DynamicGrid.FirstHit"/>:
    /// the first hit of the cell's present life. A pending cell lives only while it is being hit: with no hit
    /// for <see cref="BumpConstants.PendingRestartSeconds"/> it has run out, as a counting cell has after its
    /// 30 s; the sweep drops it, and a hit that finds it first starts a new cell in its place, first stamp,
    /// height and all. A cell that counts keeps its first stamp for as long as it lives, and never becomes
    /// pending again. So neither rule weakens the other. The mask reaches no cell that counts for longer than
    /// it did: its second runs from the first hit of the life in which the cell came to count, the pending time
    /// is part of that second, and an obstacle that has been in view for a second keeps every cell. And
    /// nothing the mask forgets counts any sooner: forgetting only removes a cell, and what is stamped in its
    /// place in a session is a new cell that waits its own pending time.</para>
    /// </summary>
    public static unsafe class DynamicLayer
    {
        const int Shift = BumpConstants.BlockShift;

        /// <summary>Blocks needed along one axis for <paramref name="cells"/> cells.</summary>
        public static int BlocksFor(int cells) => (cells + (1 << Shift) - 1) >> Shift;

        /// <summary>
        /// Marks the cell under floor point <paramref name="p"/> with a hit at <paramref name="height"/> metres
        /// above the floor, outside a hands session: the cell counts from this hit. Keeps the highest hit and
        /// renews the cell's time. False when the point is off the grid.
        /// </summary>
        public static bool Stamp(in DynamicGrid g, float2 p, float height, double time)
        {
            return Stamp(g, p, height, time, false);
        }

        /// <summary>
        /// Marks the cell under floor point <paramref name="p"/> with a hit at <paramref name="height"/> metres
        /// above the floor. Keeps the highest hit and renews the cell's time. False when the point is off the grid.
        /// The time of a cell's first stamp is written here and nowhere else: when the cell was empty, or had
        /// run out (a counting cell past its 30 s, a pending one with no hit for
        /// <see cref="BumpConstants.PendingRestartSeconds"/>; neither swept yet).
        /// <para>The pending rule (decision R13). <paramref name="near"/> says that the hit is a near return of
        /// a hands session: nearer than <see cref="BumpConstants.PendingNearMetres"/> in a depth image that came
        /// with a session. A cell that such a hit makes is pending. A pending cell counts from the first hit
        /// that is not near (a far return, or any return outside a session), and from the first near hit that
        /// comes <see cref="BumpConstants.PendingSeconds"/> or more after its first stamp. A cell that counts is
        /// not changed by what hits it. With <paramref name="near"/> false throughout, no cell is ever pending
        /// and every other array holds what it held before the rule existed.</para>
        /// </summary>
        public static bool Stamp(in DynamicGrid g, float2 p, float height, double time, bool near)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float old = g.TopHeight[idx];
            // A pending cell that no image has hit for a while has run out: this hit starts a new cell there.
            bool runOut = old > 0f && g.Pending[idx] != 0 && RunOut(time - g.LastHit[idx]);
            if (old <= 0f || runOut)
            {
                if (!runOut)
                {
                    g.BlockCount[(j >> Shift) * g.BlocksX + (i >> Shift)]++;
                    (*g.ActiveCount)++;
                }
                g.TopHeight[idx] = height;
                g.FirstHit[idx] = time;
                g.Pending[idx] = near ? (byte)1 : (byte)0;
            }
            else
            {
                if (height > old) g.TopHeight[idx] = height;
                if (g.LastHit[idx] < time - BumpConstants.DynExpirySeconds)
                {
                    g.FirstHit[idx] = time;
                    g.Pending[idx] = near ? (byte)1 : (byte)0;
                }
                else if (g.Pending[idx] != 0 && (!near || time - g.FirstHit[idx] >= BumpConstants.PendingSeconds))
                {
                    g.Pending[idx] = 0;
                }
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
        /// The young-cell rule of the depth mask (documents/ATLAS_Hands_Design.md section 7.1, rule 2, as its
        /// review decided it). Called for a depth sample the mask took out, with the floor point and the height
        /// it lands at, after every other sample of the image has been stamped. The cell under
        /// <paramref name="p"/> is forgotten only if all three hold: it was first stamped less than
        /// <see cref="BumpConstants.MaskYoungSeconds"/> ago; no sample of this image (the one at
        /// <paramref name="time"/>) hit it, so nothing the mask left alone stands there; and its stored height is
        /// within <see cref="BumpConstants.MaskHeightTolerance"/> of <paramref name="height"/>. That is a cell
        /// the hand itself stamped in the moment before it was recognised. An obstacle that has been in view for
        /// a second keeps every cell, however long a hand then covers it. A pending cell is judged as any other.
        /// True when a cell was forgotten.
        /// </summary>
        public static bool ForgetIfYoung(in DynamicGrid g, float2 p, float height, double time)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            float top = g.TopHeight[idx];
            if (top <= 0f) return false;
            if (g.LastHit[idx] == time) return false;
            // Written so that a time that is not a number, or a first stamp later than now, keeps the cell.
            double age = time - g.FirstHit[idx];
            if (!(age >= 0.0 && age < BumpConstants.MaskYoungSeconds)) return false;
            if (!(math.abs(top - height) <= BumpConstants.MaskHeightTolerance)) return false;
            Remove(g, i, j);
            return true;
        }

        /// <summary>
        /// Distance from floor point <paramref name="p"/> to the nearest live dynamic cell, metres: the distance
        /// to the cell's centre less half a cell, never negative. Looks no farther than <see cref="BumpConstants.DynamicQueryRange"/> and
        /// returns positive infinity when nothing is that near. Cells past their 30 s are ignored, and so are
        /// pending cells: this is the one place where the layer is read for clear(p), so a pending cell counts
        /// for nothing.
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
                            if (g.TopHeight[row + i] <= 0f || g.LastHit[row + i] < oldest || g.Pending[row + i] != 0) continue;
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
        /// Housekeeping: drops the cells that have run out in the next <paramref name="blocks"/> occupancy
        /// blocks, continuing from <paramref name="cursor"/>: cells past their 30 s, and pending cells with no
        /// hit for <see cref="BumpConstants.PendingRestartSeconds"/>, which a hit would start again anyway.
        /// Empty blocks cost one look-up.
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
                        if (!(g.TopHeight[idx] > 0f)) continue;
                        if (g.LastHit[idx] < oldest || (g.Pending[idx] != 0 && RunOut(time - g.LastHit[idx])))
                            Remove(g, i, j);
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
                g.Pending[k] = 0;
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

        /// <summary>Time the cell under <paramref name="p"/> was first stamped, seconds; not a number when it is empty or off the grid.</summary>
        public static double FirstHitAt(in DynamicGrid g, float2 p)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return double.NaN;
            int idx = j * g.Spec.Width + i;
            return g.TopHeight[idx] > 0f ? g.FirstHit[idx] : double.NaN;
        }

        /// <summary>True when the cell under <paramref name="p"/> is stored and pending: it does not count yet. False for a cell that counts, an empty one and a point off the grid.</summary>
        public static bool PendingAt(in DynamicGrid g, float2 p)
        {
            if (!CellOf(g.Spec, p, out int i, out int j)) return false;
            int idx = j * g.Spec.Width + i;
            return g.TopHeight[idx] > 0f && g.Pending[idx] != 0;
        }

        // A pending cell has run out when its last hit lies PendingRestartSeconds or more back. Written so that a
        // last hit later than now (a clock that went back) and a time that is not a number count as run out too:
        // such a cell starts again at its next hit and is never left pending for good.
        static bool RunOut(double sinceLastHit)
        {
            return !(sinceLastHit >= 0.0 && sinceLastHit < BumpConstants.PendingRestartSeconds);
        }

        static void Remove(in DynamicGrid g, int i, int j)
        {
            g.TopHeight[j * g.Spec.Width + i] = 0f;
            g.EmptyState[j * g.Spec.Width + i] = 0;
            g.Pending[j * g.Spec.Width + i] = 0;
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
