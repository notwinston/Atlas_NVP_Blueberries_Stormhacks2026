namespace Atlas.Blades.Lung.Walking
{
    // IForkTiles over P2-B01's AirwayTree. The only file in the walking code that names P2-B01 members (G6 §2):
    // AirwayTree.FrameOf(address).Tile, .TargetAddress, .CompassForkAddress, .SetCurrent(AirwayTile.Fork(address)).
    public sealed class AirwayTreeBinding : IForkTiles
    {
        readonly AirwayTree _tree;
        public AirwayTreeBinding(AirwayTree tree) { _tree = tree ?? throw new System.ArgumentNullException(nameof(tree)); }
        public Atlas.Kernel.TileId Root => _tree.FrameOf("").Tile;
        public Atlas.Kernel.TileId ForkTile(string address) => _tree.FrameOf(address).Tile;
        public string TargetAddress => _tree.TargetAddress;
        public string CompassForkAddress => _tree.CompassForkAddress;
        public void Focus(string address) => _tree.SetCurrent(AirwayTile.Fork(address));
    }
}
