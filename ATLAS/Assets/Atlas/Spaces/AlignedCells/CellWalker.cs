using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    public sealed class CellWalker
    {
        readonly CellGrid grid;
        readonly TileGraph graph;
        // The crossings of the last Walk, in order (the cell left, the room direction, the frame entered): read by the
        // cells.cross and cells.postLoop events (Master Plan §10.1.3). Sized once: a Walk crosses at most CountX + CountZ edges.
        readonly int2[] crossFrom;
        readonly int[] crossDir;
        readonly CellFrame[] crossFrame;

        public int2 Cell { get; private set; }
        public CellFrame Frame { get; private set; }
        public double2 LastHead { get; private set; }
        /// <summary>How many cell edges the last <see cref="Walk"/> crossed; 0 after any other call.</summary>
        public int LastCrossingCount { get; private set; }

        public CellWalker(CellGrid grid, TileGraph graph)
        {
            if (graph.Q != 5) throw new ArgumentException("Aligned Cells uses {4,5} only", nameof(graph));
            this.grid = grid; this.graph = graph;
            int max = grid.CountX + grid.CountZ;
            crossFrom = new int2[max]; crossDir = new int[max]; crossFrame = new CellFrame[max];
        }

        /// <summary>Crossing i of the last Walk: the cell left, the room direction (0 +x, 1 +z, 2 -x, 3 -z) and the frame entered.</summary>
        public void LastCrossing(int i, out int2 from, out int direction, out CellFrame entered)
        {
            if ((uint)i >= (uint)LastCrossingCount) throw new ArgumentOutOfRangeException(nameof(i));
            from = crossFrom[i]; direction = crossDir[i]; entered = crossFrame[i];
        }

        public void Reset(double2 headXZ, CellFrame frame)
        {
            Cell = grid.CellAt(headXZ); Frame = frame; LastHead = headXZ; LastCrossingCount = 0;
        }

        // Master Plan §4.8: switching from Mode 1 keeps the tile id.
        public void EnterFromCurvedWalk(in WalkerPose pose, double2 headXZ)
            => Reset(headXZ, new CellFrame(pose.Tile, TurnFromCurvedWalk(pose.Local)));

        // The Station's copy: cell and frame exactly as the phone sent them (State.sheet, tileId, xform).
        public void Place(int2 cell, CellFrame frame)
        {
            Cell = math.clamp(cell, int2.zero, new int2(grid.CountX - 1, grid.CountZ - 1));
            Frame = frame; LastHead = grid.CellCentre(Cell); LastCrossingCount = 0;
        }

        // A head displacement the kernel refused (jump, tracking not Normal: KernelStep.Rejected) steps no tile: the walker
        // keeps its tile and stands in the cell that is under the head now. True when the cell changed.
        public bool Reseat(double2 headXZ)
        {
            int2 c = grid.CellAt(headXZ);
            bool moved = !c.Equals(Cell);
            Cell = c; LastHead = headXZ; LastCrossingCount = 0;
            return moved;
        }

        // Master Plan §4.7: each cell edge crossed along the head's segment steps the tile graph.
        public int Walk(double2 headXZ)
        {
            int2 target = grid.CellAt(headXZ);
            LastCrossingCount = 0;
            if (target.Equals(Cell)) { LastHead = headXZ; return 0; }
            Span<double> tt = stackalloc double[grid.CountX + grid.CountZ];
            Span<int> dd = stackalloc int[grid.CountX + grid.CountZ];
            int n = 0;
            double2 d = headXZ - LastHead;
            double g = grid.Side;
            int lo, hi;
            if (target.x > Cell.x) { lo = Cell.x + 1; hi = target.x; }
            else { lo = target.x + 1; hi = Cell.x; }
            if (target.x != Cell.x)
                for (int X = lo; X <= hi; X++) { tt[n] = Param(grid.Origin.x + g * X, LastHead.x, d.x); dd[n++] = target.x > Cell.x ? 0 : 2; }
            if (target.y > Cell.y) { lo = Cell.y + 1; hi = target.y; }
            else { lo = target.y + 1; hi = Cell.y; }
            if (target.y != Cell.y)
                for (int Z = lo; Z <= hi; Z++) { tt[n] = Param(grid.Origin.y + g * Z, LastHead.y, d.y); dd[n++] = target.y > Cell.y ? 1 : 3; }
            // insertion sort by t; stable, so equal t keeps x before z
            for (int i = 1; i < n; i++)
            {
                double t = tt[i]; int dir = dd[i]; int j = i - 1;
                while (j >= 0 && tt[j] > t) { tt[j + 1] = tt[j]; dd[j + 1] = dd[j]; j--; }
                tt[j + 1] = t; dd[j + 1] = dir;
            }
            var f = Frame; var c = Cell;
            for (int i = 0; i < n; i++)
            {
                crossFrom[i] = c; crossDir[i] = dd[i];
                f = f.Cross(graph, dd[i]);
                c += dd[i] == 0 ? new int2(1, 0) : dd[i] == 1 ? new int2(0, 1) : dd[i] == 2 ? new int2(-1, 0) : new int2(0, -1);
                crossFrame[i] = f;
            }
            Frame = f; Cell = c; LastHead = headXZ; LastCrossingCount = n;
            return n;
        }

        static double Param(double line, double from, double delta) => delta == 0 ? 0 : (line - from) / delta;

        // Master Plan §4.8 / A.2: U = Boost(t)·Rot(theta); the room turn is the nearest quarter-turn.
        public static int TurnFromCurvedWalk(in Iso2 local)
        {
            double2 t = Geometry2.Log(local.M.c2, -1);
            Iso2 r = Iso2.Boost(t, -1).Inverse() * local;
            return TurnFromRotation(r.M.c0.x, r.M.c0.y);
        }

        // The quarter-turn nearest to a rotation by theta, given as (cos theta, sin theta).
        public static int TurnFromRotation(double cos, double sin)
            => (int)Math.Floor(-Math.Atan2(sin, cos) / (Math.PI / 2) + 0.5) & 3;
    }
}
