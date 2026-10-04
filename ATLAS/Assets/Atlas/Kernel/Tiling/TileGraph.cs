using System.Collections.Generic;

namespace Atlas.Kernel
{
    public interface ITileGraph
    {
        int Q { get; } // tiles per corner (Master Plan §6.5)
        void VisibleSet(TileId centre, double rho, List<VisibleTile> into);
    }

    // Chain = F[t]: chain of steps from the centre tile to tile t, mapping t's frame into the centre tile's frame (§4.6).
    public struct VisibleTile
    {
        public TileId Id;
        public Iso2 Chain;
        public int GraphDistance;
    }
}
