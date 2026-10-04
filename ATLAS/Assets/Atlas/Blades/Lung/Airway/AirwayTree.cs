using System;
using System.Collections.Generic;
using Atlas.Kernel;

namespace Atlas.Blades.Lung
{
    // The airway as a graph on the {4,5} tile graph (MP §10.2.3 Rule S, R3 §D3). Node data from tree.json; tile frames are exact (Kernel RuleS).
    public sealed class AirwayTree
    {
        public const int WalkableGenerations = 16; // MP §10.2.3

        readonly Dictionary<string, AirwayNode> nodes;
        readonly Dictionary<string, TileFrame> forkFrames = new Dictionary<string, TileFrame>(StringComparer.Ordinal);
        AirwayWindow window;

        public AirwayTree(AirwayTreeFile file, in TileFrame trachea)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (!file.Validated) file.Validate();
            Trachea = trachea;
            nodes = new Dictionary<string, AirwayNode>(file.nodes.Length, StringComparer.Ordinal);
            int max = 0;
            foreach (var n in file.nodes) { nodes.Add(n.address, new AirwayNode(n)); if (n.gen > max) max = n.gen; }
            MaxGeneration = max;
            TargetAddress = file.target.address;
            CompassForkAddress = file.compass_fork.address;
            forkFrames[""] = trachea;
            window = new AirwayWindow(this, "");
        }

        // D-G4-2: the trachea frame is TileFrame.Zero; the carina fork is at tile zero.
        public static AirwayTree Load(string caseRoot) => new AirwayTree(AirwayTreeFile.Load(caseRoot), TileFrame.Zero);

        public TileFrame Trachea { get; }
        public int NodeCount => nodes.Count;
        public int MaxGeneration { get; }
        public string TargetAddress { get; }
        public string CompassForkAddress { get; }
        public AirwayWindow Window => window;

        public bool TryGetNode(string address, out AirwayNode n) => nodes.TryGetValue(address, out n);

        public AirwayNode Node(string address)
        {
            if (!nodes.TryGetValue(address, out var n)) throw new KeyNotFoundException("no airway node '" + address + "'");
            return n;
        }

        // Both children present (a leaf of the generated tree has none).
        public bool HasChildren(string address) => nodes.ContainsKey(address + "L") && nodes.ContainsKey(address + "R");

        // Fork tile frame, RuleS.FrameOf(address, Trachea), cached. Data independent up to 23 letters (D-G4-4).
        public TileFrame FrameOf(string address)
        {
            if (address == null) throw new ArgumentNullException(nameof(address));
            if (forkFrames.TryGetValue(address, out var f)) return f;
            if (address.Length > RuleS.MaxAddressLength) throw new ArgumentOutOfRangeException(nameof(address), "more than 23 generations");
            if (address[address.Length - 1] != 'L' && address[address.Length - 1] != 'R') throw new ArgumentException("invalid address", nameof(address));
            var p = FrameOf(address.Substring(0, address.Length - 1));
            f = RuleS.BendToFork(RuleS.ChildBend(p, address[address.Length - 1]));
            forkFrames[address] = f;
            return f;
        }

        // Forward frame of the bend tile in front of a non-root address.
        public TileFrame BendFrameOf(string address)
        {
            if (string.IsNullOrEmpty(address)) throw new ArgumentException("the root fork has no bend tile", nameof(address));
            return RuleS.ChildBend(FrameOf(address.Substring(0, address.Length - 1)), address[address.Length - 1]);
        }

        public TileFrame FrameOf(AirwayTile tile) => tile.Kind == AirwayTileKind.Fork ? FrameOf(tile.Address) : BendFrameOf(tile.Address);

        // Bit k = canonical edge k leads to a tree tile. Tree frame (R3 §D3): fork opens 0 (not root) and 3, 1 (with children); bend opens 0 and 2.
        public int OpenEdgesCanonical(AirwayTile tile)
        {
            var f = FrameOf(tile);
            int mask = 0;
            if (tile.Kind == AirwayTileKind.Fork)
            {
                if (tile.Address.Length > 0) mask |= 1 << f.CanonicalEdge(RuleS.BackEdge);
                if (HasChildren(tile.Address))
                    mask |= (1 << f.CanonicalEdge(RuleS.LeftEdge)) | (1 << f.CanonicalEdge(RuleS.RightEdge));
            }
            else mask = (1 << f.CanonicalEdge(RuleS.BackEdge)) | (1 << f.CanonicalEdge(RuleS.StraightEdge));
            return mask;
        }

        // Recentres the window when the tile's address differs from Window.Current.
        public void SetCurrent(AirwayTile tile)
        {
            if (tile.Address == null || !nodes.ContainsKey(tile.Address)) throw new ArgumentException("tile address is not a node of the tree", nameof(tile));
            if (tile.Address == window.Current) return;
            window = new AirwayWindow(this, tile.Address);
        }
    }
}
