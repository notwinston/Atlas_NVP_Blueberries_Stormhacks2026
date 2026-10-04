using System;
using Atlas.Kernel;
using Atlas.Safety;
using Atlas.Spaces.AlignedCells;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    // P2-B04. The Mode 2 path of the airway wing (G6 brief 3.4 "Mode 2 frame"), kept as its own component because
    // AirwayWing (P2-B03) is not in this branch's base; AirwayWing delegates its Mode 2 KernelStep/valve to it (DV-B04-1).
    public sealed class CellValveWalk
    {
        public const double ChamberRadius = 0.5;   // m, MP §10.2.3 step 1 (chamber disc)

        public readonly struct Turn
        {
            public readonly int Dir, TurnBefore, TurnAfter; public readonly double Alpha;   // Alpha = (after - before)·pi/2
            public Turn(int dir, int before, int after) { Dir = dir; TurnBefore = before; TurnAfter = after; Alpha = (after - before) * Math.PI / 2; }
        }

        readonly CellGrid grid;

        public CellValve Valve { get; }
        public CellWalker Cells { get; }
        public CellGrid Grid => grid;
        public int2 TargetCell { get; private set; }

        public CellValveWalk(CellGrid grid, TileGraph graph, IClearanceQuery clearance)
        {
            this.grid = grid;
            Valve = new CellValve(grid, clearance);
            Cells = new CellWalker(grid, graph);
        }

        // Enter / switch-in: the current tile sits in the head's cell with turn 0 (MP §4.8: the tile id is kept).
        public void Enter(double2 head, TileId tile)
        {
            Cells.Reset(head, new CellFrame(tile, 0));
            TargetCell = Cells.Cell;
        }

        // MP §6.3 step 4, Mode 2: Cells.Walk(head.xz).
        public int Walk(double2 head) => Cells.Walk(head);

        // MP §10.2.3 step 1 for Mode 2: in the registered target cell and within 0.5 m of its centre.
        public bool InChamber(double2 head) =>
            Cells.Cell.Equals(TargetCell) && math.length(head - grid.CellCentre(TargetCell)) <= ChamberRadius;

        // Valve (MP §10.2.3 steps 4-5, Mode 2). The caller guarantees the shell is Opaque (rotation only while opaque,
        // MP §0.5 rule 3). False = no free neighbour cell: the valve stays closed.
        public bool TryOpen(double2 head, int exitEdge, out Turn turn)
        {
            turn = default;
            if (!Valve.TryPick(Cells.Cell, out int dir)) return false;
            int before = Cells.Frame.Turn, after = (dir - exitEdge) & 3;   // tile edge exitEdge faces room direction dir
            Cells.Reset(head, new CellFrame(Cells.Frame.Tile, after));
            TargetCell = Cells.Cell + CellValve.Step(dir);
            turn = new Turn(dir, before, after);
            return true;
        }
    }
}
