using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>The flat {4,4} square grid: four tiles per corner, all tile frames parallel.</summary>
    /// <remarks>
    /// Cell (i, j) has the id (i, 0, j, 0, 1, 0) and its centre at (a·i, a·j) in the frame of tile zero,
    /// with a the centre spacing. It is the q = 4 position of the dial (Master Plan section 3.2).
    /// </remarks>
    public sealed class FlatTileGraph : ITileGraph
    {
        private const double InvSqrt2 = 0.70710678118654752440;

        private readonly double _spacing;

        /// <param name="centreSpacing">Tile side in kernel units: tile side in metres divided by L.
        /// Must be finite and positive, else ArgumentOutOfRangeException.</param>
        public FlatTileGraph(double centreSpacing)
        {
            if (!(centreSpacing > 0.0) || double.IsInfinity(centreSpacing))
            {
                throw new ArgumentOutOfRangeException(nameof(centreSpacing), centreSpacing,
                    "Atlas.Kernel: the centre spacing must be finite and positive.");
            }
            _spacing = centreSpacing;
        }

        public int Q { get { return 4; } }
        public double Kappa { get { return Curvature.Flat; } }
        public double CentreSpacing { get { return _spacing; } }
        public double CircumRadius { get { return _spacing * InvSqrt2; } }

        public TileId Neighbour(TileId tile, int edge)
        {
            long i, j;
            RequireCell(tile, out i, out j);
            switch (edge)
            {
                case 0: return Cell(i + 1, j);
                case 1: return Cell(i, j + 1);
                case 2: return Cell(i - 1, j);
                case 3: return Cell(i, j - 1);
                default: throw BadEdge(edge);
            }
        }

        public int BackEdge(TileId tile, int edge)
        {
            long i, j;
            RequireCell(tile, out i, out j);
            if ((uint)edge > 3u) throw BadEdge(edge);
            return (edge + 2) & 3;
        }

        public Iso2 EdgeStep(TileId tile, int edge)
        {
            long i, j;
            RequireCell(tile, out i, out j);
            switch (edge)
            {
                case 0: return Iso2.Boost(new double2(_spacing, 0.0), Curvature.Flat);
                case 1: return Iso2.Boost(new double2(0.0, _spacing), Curvature.Flat);
                case 2: return Iso2.Boost(new double2(-_spacing, 0.0), Curvature.Flat);
                case 3: return Iso2.Boost(new double2(0.0, -_spacing), Curvature.Flat);
                default: throw BadEdge(edge);
            }
        }

        public void VisibleSet(TileId centre, double rho, List<VisibleTile> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            long ci, cj;
            RequireCell(centre, out ci, out cj);
            if (!(rho >= 0.0) || double.IsInfinity(rho))
            {
                throw new ArgumentOutOfRangeException(nameof(rho), rho,
                    "Atlas.Kernel: rho must be finite and not negative.");
            }

            into.Clear();
            double limit = rho + KernelConstants.RadiusEpsilon;
            Add(into, ci, cj, 0, 0, 0, limit);

            // Ring n holds the cells at graph distance n. Its nearest cells sit on the diagonal, at
            // n·a/√2, so the first ring whose diagonal is out of range ends the search.
            for (long n = 1; n * _spacing * InvSqrt2 <= limit; n++)
            {
                int ring = (int)n;
                for (long k = 0; k < n; k++) Add(into, ci, cj, n - k, k, ring, limit);   // from +x round towards +z
                for (long k = 0; k < n; k++) Add(into, ci, cj, -k, n - k, ring, limit);  // from +z round towards -x
                for (long k = 0; k < n; k++) Add(into, ci, cj, k - n, -k, ring, limit);  // from -x round towards -z
                for (long k = 0; k < n; k++) Add(into, ci, cj, k, k - n, ring, limit);   // from -z round towards +x
            }
        }

        private void Add(List<VisibleTile> into, long ci, long cj, long di, long dj, int ring, double limit)
        {
            double x = di;
            double z = dj;
            if (_spacing * Math.Sqrt(x * x + z * z) > limit) return;

            VisibleTile entry;
            entry.Tile = Cell(ci + di, cj + dj);
            // Built from the offset each time: nothing accumulates between calls or along a chain.
            entry.Chain = Iso2.Boost(new double2(_spacing * x, _spacing * z), Curvature.Flat);
            entry.GraphDistance = ring;
            into.Add(entry);
        }

        /// <summary>The id of grid cell (i, j): i counts tiles along +x, j along +z, from tile zero.</summary>
        public static TileId Cell(long i, long j)
        {
            return new TileId(i, 0, j, 0, 1, 0);
        }

        /// <summary>False when the id is not a flat-grid id.</summary>
        public static bool TryGetCell(TileId tile, out long i, out long j)
        {
            i = tile.Xu;
            j = tile.Yu;
            if (tile.Xv == 0 && tile.Yv == 0 && tile.Tu == 1 && tile.Tv == 0) return true;
            i = 0;
            j = 0;
            return false;
        }

        private static void RequireCell(TileId tile, out long i, out long j)
        {
            if (!TryGetCell(tile, out i, out j))
            {
                throw new ArgumentException("Atlas.Kernel: the tile id is not a cell of the flat grid.", nameof(tile));
            }
        }

        private static ArgumentOutOfRangeException BadEdge(int edge)
        {
            return new ArgumentOutOfRangeException(nameof(edge), edge, "Atlas.Kernel: a tile has edges 0 to 3.");
        }
    }
}
