using System;

namespace Atlas.World
{
    /// <summary>Result of the Glued cell rule (S1): nx x nz cells of sizeX x sizeZ metres filling the free rectangle.</summary>
    public struct GluedGrid
    {
        /// <summary>Cells along Wf (room +X).</summary>
        public int Nx;
        /// <summary>Cells along Df (room +Z).</summary>
        public int Nz;
        /// <summary>Cell size along X, metres.</summary>
        public double SizeX;
        /// <summary>Cell size along Z, metres.</summary>
        public double SizeZ;
    }

    /// <summary>Result of the Aligned Cells rule (S3): n square cells of side g along the short side, m along the long side.</summary>
    public struct AlignedGrid
    {
        /// <summary>Cells along the shorter side of the free rectangle.</summary>
        public int N;
        /// <summary>Cells along the longer side.</summary>
        public int M;
        /// <summary>Cell side, metres.</summary>
        public double G;
    }

    /// <summary>
    /// The two cell-grid rules that cut the free rectangle into cells: the Glued cell of S1 (Master Plan
    /// section 4.3) and the Aligned Cells grid of S3 (section 4.7). Engine-free.
    /// </summary>
    public static class CellGridRules
    {
        /// <summary>Target size of a Glued cell, metres.</summary>
        public const double GluedTarget = 1.5;
        /// <summary>Target size of an Aligned cell, metres.</summary>
        public const double AlignedTarget = 1.5;
        /// <summary>Fewest Aligned cells along the short side of a full grid.</summary>
        public const int AlignedMinN = 3;
        /// <summary>Below this cell side the Aligned grid falls back to the 2 x 2 corner demo, metres.</summary>
        public const double AlignedMinG = 0.75;

        /// <summary>The rounding used by every room rule: floor(x + 0.5).</summary>
        public static int RoundHalfUp(double x)
        {
            return (int)Math.Floor(x + 0.5);
        }

        /// <summary>Glued cell: per side n = max(1, round(side / 1.5)), cell = side / n.</summary>
        public static GluedGrid Glued(double wf, double df)
        {
            int nx = Math.Max(1, RoundHalfUp(wf / GluedTarget));
            int nz = Math.Max(1, RoundHalfUp(df / GluedTarget));
            return new GluedGrid { Nx = nx, Nz = nz, SizeX = wf / nx, SizeZ = df / nz };
        }

        /// <summary>
        /// Aligned Cells grid: n = max(3, ceil(short / 1.5)), g = short / n, m = floor(long / g + 1e-6); when g
        /// comes out below 0.75 m the grid is the 2 x 2 corner demo (five tiles round one post).
        /// </summary>
        public static AlignedGrid Aligned(double wf, double df)
        {
            double shortSide = Math.Min(wf, df);
            double longSide = Math.Max(wf, df);
            int n = Math.Max(AlignedMinN, (int)Math.Ceiling(shortSide / AlignedTarget - 1e-9));
            double g = shortSide / n;
            int m = (int)Math.Floor(longSide / g + 1e-6);
            if (g < AlignedMinG - 1e-9)
            {
                n = 2;
                m = 2;
                g = shortSide / 2.0;
            }
            return new AlignedGrid { N = n, M = m, G = g };
        }
    }
}
