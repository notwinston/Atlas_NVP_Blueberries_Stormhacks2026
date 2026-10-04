namespace Atlas.Kernel
{
    /// <summary>One entry of a visible-set query (Master Plan sections 4.6 and 6.5).</summary>
    // kernel.md §4.1. F = F[t]: tile t's frame -> the centre tile's frame (§4.6).
    public readonly struct VisibleTile
    {
        public VisibleTile(TileId id, Iso2 f, int graphDistance) { Id = id; F = f; GraphDistance = graphDistance; }
        public readonly TileId Id;
        public readonly Iso2 F;
        public readonly int GraphDistance; // edge steps from the centre tile (BFS depth)

        /// <summary>The tile: Id, under the name of the frame-loop contract.</summary>
        public TileId Tile => Id;
        /// <summary>F[t]: F, under the name of the frame-loop contract. Identity for the centre tile.</summary>
        public Iso2 Chain => F;
    }
}
