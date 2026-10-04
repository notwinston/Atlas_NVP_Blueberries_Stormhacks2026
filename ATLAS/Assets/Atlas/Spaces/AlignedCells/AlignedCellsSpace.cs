using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Seam for ISpaceModule (lane A, Master Plan §6.5): Configure/Enter/Tick/Exit forward here; WriteState is deferred.
    public sealed class AlignedCellsSpace
    {
        readonly TileGraph graph;
        public CellGrid Grid { get; private set; }
        public CellWalker Walker { get; private set; }
        public CellInstanceList Instances { get; } = new CellInstanceList();

        public AlignedCellsSpace(TileGraph graph) { this.graph = graph; }

        public void Configure(in FreeRectangle freeRect)
        {
            Grid = CellGrid.FromFreeRectangle(freeRect);
            Walker = new CellWalker(Grid, graph);
        }

        // §4.8: switch from Mode 1 at the same tile id.
        public void Enter(in WalkerPose start, double2 headXZ)
        {
            Walker.EnterFromCurvedWalk(start, headXZ);
            Rebuild();
        }

        public bool Tick(double2 headXZ)
        {
            if (Walker.Walk(headXZ) == 0) return false;
            Rebuild();
            return true;
        }

        public void Exit() => Instances.Clear();

        void Rebuild() => SightLines.Build(Grid, graph, Walker.Cell, Walker.Frame, Instances);
    }
}
