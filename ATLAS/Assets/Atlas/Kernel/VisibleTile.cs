namespace Atlas.Kernel
{
    /// <summary>One entry of a visible-set query (Master Plan sections 4.6 and 6.5).</summary>
    public struct VisibleTile
    {
        /// <summary>The tile.</summary>
        public TileId Tile;
        /// <summary>F[t]: maps this tile's frame into the frame of the query's centre tile. Identity for the centre tile.</summary>
        public Iso2 Chain;
        /// <summary>Number of edge steps from the centre tile. 0 for the centre tile.</summary>
        public int GraphDistance;
    }
}
