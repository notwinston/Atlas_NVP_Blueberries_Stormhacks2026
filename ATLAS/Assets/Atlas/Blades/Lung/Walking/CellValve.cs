using Atlas.Safety;
using Atlas.Spaces.AlignedCells;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B04. Master Plan §10.2.3 Mode 2: "the cell's tile frame is rotated by a multiple of 90 degrees so the chosen tree
    // edge faces the free neighbouring cell with the longest clear run. Needs >= 1x2 free cells" (derived).
    // Room directions 0..3 are +x, +z, -x, -z (P1-B10 CellFrame). Cells are (column x, row z).
    public sealed class CellValve
    {
        // m, MP §10.2.3 step 3 (the same 0.30 m as ValveSolver.ClearMin, G6 brief 3.3; kept here so this file stands alone).
        public const double ClearMin = 0.30;

        readonly CellGrid grid;
        readonly IClearanceQuery clearance;

        public CellValve(CellGrid grid, IClearanceQuery c) { this.grid = grid; clearance = c; }

        public static int2 Step(int dir) => dir == 0 ? new int2(1, 0) : dir == 1 ? new int2(0, 1) : dir == 2 ? new int2(-1, 0) : new int2(0, -1);

        // DV-G6-6: a cell is free when it is in the grid and the clearance at its centre is >= ClearMin.
        public bool Free(int2 cell) =>
            grid.Contains(cell) && clearance.Clearance((float2)grid.CellCentre(cell)) >= ClearMin;

        // Largest n with cells cell+1u .. cell+nu all free (the start cell itself is not tested).
        public int ClearRun(int2 cell, int dir)
        {
            int2 u = Step(dir & 3); int n = 0;
            while (Free(cell + u * (n + 1))) n++;
            return n;
        }

        // Longest clear run wins; ties go to the lowest direction; no run of at least 1 -> false (the valve stays closed).
        public bool TryPick(int2 cell, out int dir)
        {
            int best = 0; dir = -1;
            for (int d = 0; d < 4; d++)
            {
                int r = ClearRun(cell, d);
                if (r > best) { best = r; dir = d; }
            }
            return best >= 1;
        }
    }
}
