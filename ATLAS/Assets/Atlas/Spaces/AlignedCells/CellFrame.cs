using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Which tile a cell shows and how it is turned: tile edge k faces room direction (k + Turn) & 3.
    // Room directions: 0 = +x, 1 = +z, 2 = -x, 3 = -z (kernel.md §2.4, §5.3).
    public readonly struct CellFrame : IEquatable<CellFrame>
    {
        public readonly TileId Tile;
        public readonly int Turn;

        public CellFrame(TileId tile, int turn) { Tile = tile; Turn = turn & 3; }

        public int TileEdge(int direction) => (direction - Turn) & 3;

        // Master Plan §4.7: crossing a cell edge steps the tile graph; A.5 S_k = R^k·T·H, canonical frames kernel.md §5.3
        public CellFrame Cross(TileGraph graph, int direction)
        {
            int k = TileEdge(direction);
            TileId next = graph.Neighbour(Tile, k);
            Iso2 g = graph.StepMatrix(Tile, k);                 // neighbour frame -> tile frame
            double3 c = g.Inverse().M.c2;                       // old tile centre in the neighbour's frame (A.2 inverse)
            int b = 0; double best = c.x;
            if (c.y > best) { best = c.y; b = 1; }
            if (-c.x > best) { best = -c.x; b = 2; }
            if (-c.y > best) { b = 3; }
            return new CellFrame(next, (direction + 2 - b) & 3); // the back edge faces the room direction we came from
        }

        public bool Equals(CellFrame o) => Turn == o.Turn && Tile.Equals(o.Tile);
        public override bool Equals(object o) => o is CellFrame f && Equals(f);
        public override int GetHashCode() => Tile.GetHashCode() * 5 + Turn;
        public static bool operator ==(CellFrame a, CellFrame b) => a.Equals(b);
        public static bool operator !=(CellFrame a, CellFrame b) => !a.Equals(b);
    }
}
