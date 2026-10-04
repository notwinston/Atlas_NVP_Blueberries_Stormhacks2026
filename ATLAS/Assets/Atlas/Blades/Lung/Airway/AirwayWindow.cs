using System;
using System.Collections.Generic;
using Atlas.Kernel;

namespace Atlas.Blades.Lung
{
    // Lazy window of the tree around the current node (MP §10.2.3 "current node, 2 ancestors, descendants to depth 3: <= 60 nodes,
    // <= 120 tiles"). D-G4-3: window nodes = union over x in {current, parent, grandparent} of the data nodes below x to relative depth 3.
    public sealed class AirwayWindow
    {
        public const int MaxNodes = 60, MaxTiles = 120;     // MP §10.2.3
        public const int AncestorLevels = 2, DescendantDepth = 3;

        readonly List<string> nodes = new List<string>(32);
        readonly List<AirwayTile> tiles = new List<AirwayTile>(64);
        readonly Dictionary<TileId, AirwayTile> byId = new Dictionary<TileId, AirwayTile>(64);

        public string Current { get; }
        public int NodeCount => nodes.Count;
        public int TileCount => tiles.Count;
        public IReadOnlyList<string> Nodes => nodes;
        public IReadOnlyList<AirwayTile> Tiles => tiles;

        internal AirwayWindow(AirwayTree tree, string current)
        {
            Current = current;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string x = current;
            for (int level = 0; level <= AncestorLevels; level++)
            {
                Collect(tree, x, 0, seen);
                if (x.Length == 0) break;
                x = x.Substring(0, x.Length - 1);
            }
            foreach (var a in nodes)
            {
                Add(AirwayTile.Fork(a), tree.FrameOf(a).Tile);
                if (a.Length > 0) Add(AirwayTile.Bend(a), tree.BendFrameOf(a).Tile);
            }
            if (nodes.Count > MaxNodes || tiles.Count > MaxTiles) throw new InvalidOperationException("window exceeds the plan's 60 nodes / 120 tiles");
        }

        void Collect(AirwayTree tree, string a, int depth, HashSet<string> seen)
        {
            if (!tree.TryGetNode(a, out _)) return;
            if (seen.Add(a)) nodes.Add(a);
            if (depth == DescendantDepth) return;
            Collect(tree, a + "L", depth + 1, seen);
            Collect(tree, a + "R", depth + 1, seen);
        }

        void Add(AirwayTile t, TileId id) { tiles.Add(t); byId.Add(id, t); }

        // false = solid tissue (MP §10.2.3 "tiles not in the tree are solid tissue").
        public bool TryLocate(TileId id, out AirwayTile tile) => byId.TryGetValue(id, out tile);
    }
}
