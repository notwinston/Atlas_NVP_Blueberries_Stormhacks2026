using System;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7 grid rule. double2 .x = anchor x, .y = anchor z; int2 cell .x = column (x), .y = row (z).
    public sealed class CellGrid
    {
        public const double TargetCell = 1.5;     // §4.7 n = max(3, ceil(Wf_short/1.5))
        public const double MinCell = 0.75;       // §4.7 corner demo below this
        public const double Epsilon = 1e-6;       // §4.7 m = floor(Wf_long/g + 1e-6)

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
        {
            double s = Math.Min(r.Width, r.Depth), l = Math.Max(r.Width, r.Depth);
            int n = Math.Max(3, (int)Math.Ceiling(s / TargetCell));
            double g = s / n;
            int m = (int)Math.Floor(l / g + Epsilon);
            bool demo = false;
            if (g < MinCell) { n = 2; m = 2; g = s / 2; demo = true; } // §4.7 "a 2 x 2 grid, five tiles round one post"
            if (g < 0.4) throw new ArgumentOutOfRangeException(nameof(r), "free rectangle too small: cell side below 0.4 m");
            bool xShort = r.Width <= r.Depth;
            return new CellGrid(xShort ? n : m, xShort ? m : n, g, demo);
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
