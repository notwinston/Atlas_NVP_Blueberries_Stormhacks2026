namespace Atlas.Kernel
{
    /// <summary>Where the walker is: a tile and a frame inside it. Never a global coordinate (Master Plan §0.5 rule 7).</summary>
    public struct WalkerPose
    {
        /// <summary>The tile the walker stands on.</summary>
        public TileId Tile;
        /// <summary>U: maps the walker frame into the frame of Tile. Kernel units.</summary>
        public Iso2 Local;

        /// <summary>At the centre of tile zero, unturned, flat-tagged: (TileId.Zero, Iso2.Identity).</summary>
        public static WalkerPose Start
        {
            get { return new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity }; }
        }

        /// <summary>False for default(WalkerPose): the tile is not "none" and Local.Defect() is below 1e-6.</summary>
        public bool IsValid
        {
            get { return !Tile.IsNone && Local.Defect() < 1e-6; }
        }
    }
}
