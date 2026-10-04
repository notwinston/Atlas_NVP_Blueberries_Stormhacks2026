namespace Atlas.Kernel
{
    // Never a global coordinate (Master Plan §0.5 rule 7).
    public struct WalkerPose
    {
        public TileId Tile;
        public Iso2 Local;
    }
}
