namespace Atlas.Kernel
{
    // Crossing hook: returns the neighbour of `tile` across `edge` (0..3, kernel.md §5.3); g maps the neighbour's frame
    // into tile's frame (Master Plan §4.6 G[i]). P1-B04's TileGraph supplies it.
    public delegate TileId TileStep(TileId tile, int edge, out Iso2 g);
}
