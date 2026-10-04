using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Scan.Grid
{
    /// <summary>
    /// Operations on the FloorScan grid. A grid is a row-major array, index = j * width + i, with row 0 at the
    /// lowest z. Everything here is plain arithmetic on managed arrays and runs once per scan.
    /// </summary>
    public static class FloorGridMath
    {
        const double Big = 1e9;

        /// <summary>Grow a mask by one cell with the 3 x 3 square (the cell and its eight neighbours).</summary>
        public static bool[] Dilate8(bool[] mask, int width, int height)
        {
            var grown = new bool[mask.Length];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    if (!mask[j * width + i]) continue;
                    int jLo = Math.Max(0, j - 1), jHi = Math.Min(height - 1, j + 1);
                    int iLo = Math.Max(0, i - 1), iHi = Math.Min(width - 1, i + 1);
                    for (int b = jLo; b <= jHi; b++)
                        for (int a = iLo; a <= iHi; a++)
                            grown[b * width + a] = true;
                }
            }
            return grown;
        }

        /// <summary>
        /// Close unseen specks: a 4-connected set of at most <paramref name="maxCells"/> unknown cells whose
        /// outer 4-neighbours are all floor becomes floor. Larger unseen areas stay not-free.
        /// </summary>
        /// <returns>The number of cells closed.</returns>
        public static int ClosePinholes(bool[] floor, bool[] unknown, int width, int height, int maxCells)
        {
            var seen = new bool[floor.Length];
            var stack = new int[floor.Length];
            var comp = new List<int>(maxCells + 1);
            int closed = 0;
            for (int start = 0; start < floor.Length; start++)
            {
                if (!unknown[start] || seen[start]) continue;
                comp.Clear();
                int top = 0;
                stack[top++] = start;
                seen[start] = true;
                bool enclosed = true;
                int size = 0;
                while (top > 0)
                {
                    int c = stack[--top];
                    size++;
                    if (size <= maxCells) comp.Add(c);
                    int i = c % width, j = c / width;
                    for (int k = 0; k < 4; k++)
                    {
                        int a = i + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int b = j + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if (a < 0 || b < 0 || a >= width || b >= height) { enclosed = false; continue; }
                        int n = b * width + a;
                        if (unknown[n])
                        {
                            if (!seen[n]) { seen[n] = true; stack[top++] = n; }
                        }
                        else if (!floor[n]) enclosed = false;
                    }
                }
                if (enclosed && size <= maxCells)
                {
                    for (int k = 0; k < comp.Count; k++)
                    {
                        floor[comp[k]] = true;
                        unknown[comp[k]] = false;
                    }
                    closed += size;
                }
            }
            return closed;
        }

        /// <summary>
        /// Exact Euclidean distance, in metres, from every cell centre to the edge of the free set: positive in
        /// free cells (distance to the nearest not-free cell's square), negative in the others (distance to the
        /// nearest free cell's square). Cells beyond the grid count as not-free. Because the first not-free cell
        /// lies one cell inside the wall, a positive value reads one cell less than the true distance to it.
        /// </summary>
        public static float[] SignedDistance(bool[] free, int width, int height, double cell)
        {
            var toBlocked = new double[free.Length];
            var toFree = new double[free.Length];
            DistanceTo(free, false, width, height, toBlocked);
            DistanceTo(free, true, width, height, toFree);
            var sdf = new float[free.Length];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    int c = j * width + i;
                    if (free[c])
                    {
                        double d = toBlocked[c];
                        d = Math.Min(d, Math.Min(Math.Min(i + 0.5, width - i - 0.5), Math.Min(j + 0.5, height - j - 0.5)));
                        sdf[c] = (float)(d * cell);
                    }
                    else
                    {
                        double d = toFree[c];
                        sdf[c] = (float)(d < Big ? -(d * cell) : -(Math.Max(width, height) * cell));
                    }
                }
            }
            return sdf;
        }

        // Distance in cells from each centre to the nearest square whose mask value equals `target`.
        // Two passes: the gap along each row, then the exact minimum down each column.
        static void DistanceTo(bool[] mask, bool target, int width, int height, double[] result)
        {
            var rowGap = new double[mask.Length];
            for (int j = 0; j < height; j++)
            {
                int last = -1;
                for (int i = 0; i < width; i++)
                {
                    int c = j * width + i;
                    if (mask[c] == target) { last = i; rowGap[c] = 0.0; }
                    else rowGap[c] = last >= 0 ? (i - last) - 0.5 : Big;
                }
                last = -1;
                for (int i = width - 1; i >= 0; i--)
                {
                    int c = j * width + i;
                    if (mask[c] == target) last = i;
                    else if (last >= 0) rowGap[c] = Math.Min(rowGap[c], (last - i) - 0.5);
                }
            }
            for (int i = 0; i < width; i++)
            {
                for (int j = 0; j < height; j++)
                {
                    double best = rowGap[j * width + i];
                    double best2 = best >= Big ? double.MaxValue : best * best;
                    // Walk outwards from row j; stop once the vertical gap alone exceeds the best so far.
                    for (int step = 1; step < height; step++)
                    {
                        double vgap = step - 0.5;
                        double v2 = vgap * vgap;
                        if (v2 >= best2) break;
                        int up = j + step, down = j - step;
                        if (up >= height && down < 0) break;
                        if (up < height)
                        {
                            double hgap = rowGap[up * width + i];
                            if (hgap < Big) { double d2 = hgap * hgap + v2; if (d2 < best2) best2 = d2; }
                        }
                        if (down >= 0)
                        {
                            double hgap = rowGap[down * width + i];
                            if (hgap < Big) { double d2 = hgap * hgap + v2; if (d2 < best2) best2 = d2; }
                        }
                    }
                    result[j * width + i] = best2 == double.MaxValue ? Big : Math.Sqrt(best2);
                }
            }
        }

        /// <summary>
        /// The 4-connected part of {sdf &gt; threshold} that contains the seed cell. When the seed is not in the
        /// set, the part with the cell centre nearest the seed point is taken. Returns null when the set is empty.
        /// </summary>
        /// <param name="seedX">Seed, metres from the grid's low corner.</param>
        /// <param name="seedZ">Seed, metres from the grid's low corner.</param>
        public static bool[] WalkComponent(float[] sdf, int width, int height, double cell, double threshold,
            double seedX, double seedZ)
        {
            int count = sdf.Length;
            float limit = (float)threshold;
            int si = (int)Math.Floor(seedX / cell), sj = (int)Math.Floor(seedZ / cell);
            int seed = -1;
            if (si >= 0 && sj >= 0 && si < width && sj < height && sdf[sj * width + si] > limit)
                seed = sj * width + si;
            if (seed < 0)
            {
                double bestD = double.MaxValue;
                for (int c = 0; c < count; c++)
                {
                    if (!(sdf[c] > limit)) continue;
                    double dx = (c % width + 0.5) * cell - seedX, dz = (c / width + 0.5) * cell - seedZ;
                    double d = dx * dx + dz * dz;
                    if (d < bestD - 1e-12) { bestD = d; seed = c; }
                }
                if (seed < 0) return null;
            }
            var walk = new bool[count];
            var stack = new int[count];
            int top = 0;
            stack[top++] = seed;
            walk[seed] = true;
            while (top > 0)
            {
                int c = stack[--top];
                int i = c % width, j = c / width;
                if (i + 1 < width) Visit(c + 1, sdf, limit, walk, stack, ref top);
                if (i > 0) Visit(c - 1, sdf, limit, walk, stack, ref top);
                if (j + 1 < height) Visit(c + width, sdf, limit, walk, stack, ref top);
                if (j > 0) Visit(c - width, sdf, limit, walk, stack, ref top);
            }
            return walk;
        }

        static void Visit(int n, float[] sdf, float limit, bool[] walk, int[] stack, ref int top)
        {
            if (walk[n] || !(sdf[n] > limit)) return;
            walk[n] = true;
            stack[top++] = n;
        }

        /// <summary>Side, in cells, of the largest square of set cells.</summary>
        public static int LargestSquare(bool[] mask, int width, int height)
        {
            var prev = new int[width + 1];
            var cur = new int[width + 1];
            int best = 0;
            for (int j = 0; j < height; j++)
            {
                cur[0] = 0;
                for (int i = 0; i < width; i++)
                {
                    int v = 0;
                    if (mask[j * width + i]) v = 1 + Math.Min(prev[i], Math.Min(prev[i + 1], cur[i]));
                    cur[i + 1] = v;
                    if (v > best) best = v;
                }
                var swap = prev; prev = cur; cur = swap;
            }
            return best;
        }

        /// <summary>
        /// The free rectangle inside a mask, as cells (i0, j0) to (i1, j1), half-open. Rule: the largest short
        /// side, counted up to <paramref name="sideCapCells"/> (a longer short side gains nothing for the lap);
        /// then the largest area; then the centre nearest the walker; then the lowest row, then the lowest column.
        /// </summary>
        /// <returns>False when the mask is empty.</returns>
        public static bool FreeRectangle(bool[] mask, int width, int height, double cell, int sideCapCells,
            double walkerX, double walkerZ, out int i0, out int j0, out int i1, out int j1)
        {
            i0 = j0 = i1 = j1 = 0;
            int side = LargestSquare(mask, width, height);
            if (side == 0) return false;
            int need = Math.Min(side, sideCapCells);
            bool found = false;
            long bestArea = 0;
            double bestDist = 0.0;
            var heights = new int[width];
            var stackCol = new int[width + 1];
            var stackH = new int[width + 1];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                    heights[i] = mask[j * width + i] ? heights[i] + 1 : 0;
                // Histogram method: every rectangle that cannot be widened at its bottom row.
                int top = 0;
                for (int i = 0; i <= width; i++)
                {
                    int curH = i < width ? heights[i] : 0;
                    int start = i;
                    while (top > 0 && stackH[top - 1] >= curH)
                    {
                        top--;
                        int col = stackCol[top], h = stackH[top];
                        start = col;
                        if (h <= 0) continue;
                        int w = i - col;
                        if (Math.Min(w, h) < need) continue;
                        int rj0 = j + 1 - h;
                        long area = (long)w * h;
                        double cx = 0.5 * (col + i) * cell - walkerX, cz = 0.5 * (rj0 + j + 1) * cell - walkerZ;
                        double dist = Math.Round(cx * cx + cz * cz, 9);
                        bool better = !found
                            || area > bestArea
                            || (area == bestArea && (dist < bestDist
                                || (dist == bestDist && (rj0 < j0 || (rj0 == j0 && col < i0)))));
                        if (!better) continue;
                        found = true;
                        bestArea = area;
                        bestDist = dist;
                        i0 = col; j0 = rj0; i1 = i; j1 = j + 1;
                    }
                    stackCol[top] = start;
                    stackH[top] = curH;
                    top++;
                }
            }
            return found;
        }

        /// <summary>
        /// Boundary of a set of cells as rings of lattice points, in cells from the grid's low corner. Outer rings
        /// run counter-clockwise (seen with +X right and +Z up), holes clockwise. Each ring starts at its lowest
        /// point (lowest z, then lowest x) and has no collinear points, so it follows the cells exactly: the
        /// polygon never claims floor the grid did not.
        /// </summary>
        public static void TraceRegion(bool[] mask, int width, int height, List<int2[]> outers, List<int2[]> holes)
        {
            int pw = width + 1;
            // Directed boundary edges leaving each lattice point, the region on their left.
            // Bit 0: +x, bit 1: +z, bit 2: -x, bit 3: -z.
            var edges = new byte[pw * (height + 1)];
            for (int j = 0; j < height; j++)
            {
                for (int i = 0; i < width; i++)
                {
                    if (!mask[j * width + i]) continue;
                    if (j == 0 || !mask[(j - 1) * width + i]) edges[j * pw + i] |= 1;
                    if (i == width - 1 || !mask[j * width + i + 1]) edges[j * pw + i + 1] |= 2;
                    if (j == height - 1 || !mask[(j + 1) * width + i]) edges[(j + 1) * pw + i + 1] |= 4;
                    if (i == 0 || !mask[j * width + i - 1]) edges[(j + 1) * pw + i] |= 8;
                }
            }
            var ring = new List<int2>();
            for (int start = 0; start < edges.Length; start++)
            {
                while (edges[start] != 0)
                {
                    ring.Clear();
                    int cur = start, prevDir = -1;
                    do
                    {
                        ring.Add(new int2(cur % pw, cur / pw));
                        int options = edges[cur];
                        int dir;
                        // Where two cells touch only by their corners, turn left: the ring then stays round
                        // one 4-connected piece.
                        if (prevDir >= 0 && (options & (1 << ((prevDir + 1) & 3))) != 0) dir = (prevDir + 1) & 3;
                        else { dir = 0; while ((options & (1 << dir)) == 0) dir++; }
                        edges[cur] = (byte)(options & ~(1 << dir));
                        cur += dir == 0 ? 1 : dir == 1 ? pw : dir == 2 ? -1 : -pw;
                        prevDir = dir;
                    }
                    while (cur != start);
                    int2[] clean = DropCollinear(ring);
                    long area2 = 0;
                    for (int k = 0; k < clean.Length; k++)
                    {
                        int2 p = clean[k], q = clean[(k + 1) % clean.Length];
                        area2 += (long)p.x * q.y - (long)q.x * p.y;
                    }
                    if (area2 > 0) outers.Add(clean); else if (area2 < 0) holes.Add(clean);
                }
            }
        }

        static int2[] DropCollinear(List<int2> ring)
        {
            int n = ring.Count;
            var kept = new List<int2>(n);
            for (int k = 0; k < n; k++)
            {
                int2 a = ring[(k + n - 1) % n], b = ring[k], c = ring[(k + 1) % n];
                if ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x) != 0) kept.Add(b);
            }
            // Start at the lowest point: lowest z, then lowest x.
            int first = 0;
            for (int k = 1; k < kept.Count; k++)
                if (kept[k].y < kept[first].y || (kept[k].y == kept[first].y && kept[k].x < kept[first].x)) first = k;
            var result = new int2[kept.Count];
            for (int k = 0; k < result.Length; k++) result[k] = kept[(first + k) % kept.Count];
            return result;
        }

        /// <summary>Even-odd point-in-polygon test for a floor point.</summary>
        public static bool PointInPolygon(double x, double z, double[] px, double[] pz)
        {
            bool inside = false;
            int n = px.Length;
            for (int k = 0, prev = n - 1; k < n; prev = k++)
            {
                double x0 = px[prev], z0 = pz[prev], x1 = px[k], z1 = pz[k];
                if ((z0 > z) != (z1 > z) && x < x0 + (z - z0) * (x1 - x0) / (z1 - z0)) inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// True when the triangle's footprint reaches into the open square of half-size <paramref name="half"/>
        /// centred on (cx, cz) by more than <paramref name="tol"/>. Touching does not count, so a wall standing
        /// exactly on a cell edge marks neither neighbour. A vertical triangle (a segment from above) counts when
        /// it passes through the square.
        /// </summary>
        public static bool TriangleOverlapsCell(double x0, double z0, double x1, double z1, double x2, double z2,
            double cx, double cz, double half, double tol)
        {
            double lo = Math.Min(x0, Math.Min(x1, x2)), hi = Math.Max(x0, Math.Max(x1, x2));
            if (!(hi > cx - half + tol && lo < cx + half - tol)) return false;
            lo = Math.Min(z0, Math.Min(z1, z2)); hi = Math.Max(z0, Math.Max(z1, z2));
            if (!(hi > cz - half + tol && lo < cz + half - tol)) return false;
            return EdgeAxisOverlaps(x0, z0, x1, z1, x2, z2, cx, cz, half, tol)
                && EdgeAxisOverlaps(x1, z1, x2, z2, x0, z0, cx, cz, half, tol)
                && EdgeAxisOverlaps(x2, z2, x0, z0, x1, z1, cx, cz, half, tol);
        }

        // Separating-axis test along the normal of edge a-b; c is the third vertex.
        static bool EdgeAxisOverlaps(double ax, double az, double bx, double bz, double px, double pz,
            double cx, double cz, double half, double tol)
        {
            double ex = bx - ax, ez = bz - az;
            double len = Math.Sqrt(ex * ex + ez * ez);
            if (len < 1e-9) return true;
            double nx = -ez / len, nz = ex / len;
            double a = nx * ax + nz * az, p = nx * px + nz * pz;
            double lo = Math.Min(a, p), hi = Math.Max(a, p);
            double c = nx * cx + nz * cz, r = half * (Math.Abs(nx) + Math.Abs(nz));
            return hi > c - r + tol && lo < c + r - tol;
        }
    }
}
