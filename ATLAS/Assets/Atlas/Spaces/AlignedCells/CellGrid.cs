using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7 grid. The rule itself is CellGridRules.Aligned (lane C contract §4.2), which also fills RoomModel.Aligned.
    // double2 .x = anchor x, .y = anchor z; int2 cell .x = column (x), .y = row (z).
    public sealed class CellGrid
    {
        public const double MinSide = 0.4;        // below this the flat cell mesh cannot be built (TileMeshSpec)
        public const int MazeCount = 4;           // S3 duck maze: a fixed 4 x 4 grid of small cells

        public int CountX { get; }
        public int CountZ { get; }
        public double Side { get; }
        public bool CornerDemo { get; }
        public double2 Origin { get; }

        CellGrid(int cx, int cz, double g, bool demo)
        {
            CountX = cx; CountZ = cz; Side = g; CornerDemo = demo;
            Origin = new double2(-cx * g / 2, -cz * g / 2);
        }

        public static CellGrid FromFreeRectangle(in FreeRectangle r)
            => FromAligned(CellGridRules.Aligned(r.Width, r.Depth), r.Width <= r.Depth);

        // The grid the RoomModel carries (cells.aligned); a model that was never fitted gets the rule applied to its free rectangle.
        public static CellGrid FromRoom(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            AlignedGrid a = room.Aligned;
            if (a.N <= 0 || a.M <= 0 || !(a.G > 0)) a = CellGridRules.Aligned(Math.Round(room.FreeRect.Wf, 6), Math.Round(room.FreeRect.Df, 6));
            return FromAligned(a, room.FreeRect.Wf <= room.FreeRect.Df);
        }

        // n cells along the short side, m along the long one; n = 2 only in the corner demo (§4.7 "five tiles round one post").
        public static CellGrid FromAligned(in AlignedGrid a, bool xShort)
        {
            if (a.N < 2 || a.M < 2 || !(a.G >= MinSide) || double.IsInfinity(a.G))
                throw new ArgumentOutOfRangeException(nameof(a), "free rectangle too small: cell side below 0.4 m");
            return new CellGrid(xShort ? a.N : a.M, xShort ? a.M : a.N, a.G, a.N < CellGridRules.AlignedMinN);
        }

        // The duck maze grid: 4 x 4 cells of side min(Wf, Df) / 4 centred on the anchor; null when that side is below MinSide.
        public static CellGrid Maze(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            double side = Math.Round(Math.Min(room.FreeRect.Wf, room.FreeRect.Df), 6) / MazeCount;
            return side >= MinSide ? new CellGrid(MazeCount, MazeCount, side, false) : null;
        }

        public bool Contains(int2 cell) => cell.x >= 0 && cell.x < CountX && cell.y >= 0 && cell.y < CountZ;

        public int2 CellAt(double2 p)
        {
            int x = (int)Math.Floor((p.x - Origin.x) / Side);
            int z = (int)Math.Floor((p.y - Origin.y) / Side);
            return new int2(Math.Clamp(x, 0, CountX - 1), Math.Clamp(z, 0, CountZ - 1));
        }

        public double2 CellCentre(int2 cell) => Origin + Side * ((double2)cell + 0.5);
        public double2 Corner(int x, int z) => Origin + Side * new double2(x, z);
        public int InteriorCornerCount => (CountX - 1) * (CountZ - 1);

        // Master Plan A.7: g / sqrt(2 pi / 5) = 0.892 g
        public double EquivalentRadius => Side / Math.Sqrt(2 * Math.PI / 5);
    }
}
