using System.Collections.Generic;

namespace Atlas.Kernel
{
    /// <summary>A regular tiling by four-sided tiles, addressed by exact tile id (Master Plan section 6.5).</summary>
    public interface ITileGraph
    {
        /// <summary>Tiles per corner: 3, 4, 5 or 6.</summary>
        int Q { get; }
        /// <summary>Curvature of the tiling: Curvature.OfQ(Q).</summary>
        double Kappa { get; }
        /// <summary>Distance between the centres of neighbouring tiles, 2r. Kernel units.</summary>
        double CentreSpacing { get; }
        /// <summary>Distance from a tile's centre to its corners, r_c. Kernel units.</summary>
        double CircumRadius { get; }

        /// <summary>The tile across edge 0..3 of tile.</summary>
        TileId Neighbour(TileId tile, int edge);
        /// <summary>The edge of Neighbour(tile, edge) that leads back to tile.</summary>
        int BackEdge(TileId tile, int edge);
        /// <summary>G[edge]: maps the frame of Neighbour(tile, edge) into the frame of tile.</summary>
        Iso2 EdgeStep(TileId tile, int edge);

        /// <summary>
        /// Clears into, then adds every tile whose centre lies within rho (kernel units, inclusive)
        /// of the centre of the given tile: id, step chain F[t], graph distance.
        /// </summary>
        void VisibleSet(TileId centre, double rho, List<VisibleTile> into);
    }
}
