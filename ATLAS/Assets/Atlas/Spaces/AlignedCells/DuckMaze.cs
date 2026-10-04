using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // The duck maze (S3 duck maze design, 2026-10-04): a tree of {4,5} tiles grown by simulating the walk on the cell grid, rooted at
    // tile zero, turn 0, in the start cell. A step in room direction d joins the tree only when the next cell is inside the grid and the
    // next tile is new, so every open edge is a step the real floor allows, and each tile has one cell and one frame. Walls: a maze
    // tile is walled on every edge that is not a tree edge; a tile outside the maze is walled on every edge facing a maze tile.
    public sealed class DuckMaze
    {
        public const int TargetTiles = 20, MinDuckDepth = 8, MaxDuckDepth = 12, IdealDuckDepth = 10, Attempts = 64;
        public const int MaxSeed = (1 << 20) - 1;

        struct Node { public CellFrame Frame; public int2 Cell; public int Depth, Parent, FromDir, Open; }

        static readonly int2[] Step = { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) };

        readonly TileGraph graph;
        readonly List<Node> nodes;
        readonly Dictionary<TileId, int> index;
        readonly int duck;

        public int Seed { get; }
        public int2 StartCell { get; }
        public int TileCount => nodes.Count;
        public TileId DuckTile => nodes[duck].Frame.Tile;
        public int DuckDepth => nodes[duck].Depth;
        public int Code => Seed * 16 + StartCell.y * 4 + StartCell.x;

        DuckMaze(TileGraph graph, int seed, int2 start, List<Node> nodes, Dictionary<TileId, int> index, int duck)
        {
            this.graph = graph; Seed = seed; StartCell = start; this.nodes = nodes; this.index = index; this.duck = duck;
        }

        public static DuckMaze Generate(CellGrid grid, TileGraph graph, int2 startCell, int seed)
        {
            if (grid == null) throw new ArgumentNullException(nameof(grid));
            if (graph == null || graph.Q != 5) throw new ArgumentException("the duck maze uses {4,5} only", nameof(graph));
            if (!grid.Contains(startCell)) throw new ArgumentOutOfRangeException(nameof(startCell));
            if (seed < 1 || seed > MaxSeed) throw new ArgumentOutOfRangeException(nameof(seed));
            DuckMaze best = null; int bestMiss = int.MaxValue;
            for (int a = 0; a < Attempts; a++)
            {
                var rng = new SplitMix64(((ulong)(uint)seed << 32) | (uint)a);
                DuckMaze m = Grow(grid, graph, startCell, seed, ref rng);
                if (m.DuckDepth >= MinDuckDepth && m.DuckDepth <= MaxDuckDepth) return m;
                int miss = Math.Abs(m.DuckDepth - IdealDuckDepth);
                if (miss < bestMiss) { best = m; bestMiss = miss; }
            }
            return best;
        }

        static DuckMaze Grow(CellGrid grid, TileGraph graph, int2 start, int seed, ref SplitMix64 rng)
        {
            var nodes = new List<Node>(TargetTiles);
            var index = new Dictionary<TileId, int>(TargetTiles);
            nodes.Add(new Node { Frame = new CellFrame(TileId.Zero, 0), Cell = start, Depth = 0, Parent = -1, FromDir = -1 });
            index[TileId.Zero] = 0;
            var stack = new List<int> { 0 };
            Span<int> dirs = stackalloc int[4];
            Span<CellFrame> next = stackalloc CellFrame[4];
            while (stack.Count > 0 && nodes.Count < TargetTiles)
            {
                int top = stack[stack.Count - 1];
                Node n = nodes[top];
                int c = 0;
                for (int d = 0; d < 4; d++)
                {
                    if (!grid.Contains(n.Cell + Step[d])) continue;
                    CellFrame f = n.Frame.Cross(graph, d);
                    if (index.ContainsKey(f.Tile)) continue;
                    dirs[c] = d; next[c] = f; c++;
                }
                if (c == 0) { stack.RemoveAt(stack.Count - 1); continue; }
                int pick = rng.Below(c), dir = dirs[pick];
                CellFrame nf = next[pick];
                n.Open |= 1 << n.Frame.TileEdge(dir);
                nodes[top] = n;
                index[nf.Tile] = nodes.Count;
                nodes.Add(new Node { Frame = nf, Cell = n.Cell + Step[dir], Depth = n.Depth + 1, Parent = top, FromDir = dir, Open = 1 << nf.TileEdge((dir + 2) & 3) });
                stack.Add(nodes.Count - 1);
            }
            // The duck: the deepest tile whose depth is in the window. The depth-first growth runs deeper than the window (the deepest
            // tile of a 20-tile tree is usually 15-19 steps out), so the window is applied here; with no tile in it, the deepest tile.
            int duck = 0;
            for (int i = 1; i < nodes.Count; i++) if (nodes[i].Depth > nodes[duck].Depth) duck = i;
            int inWindow = -1;
            for (int i = 1; i < nodes.Count; i++)
            {
                int dep = nodes[i].Depth;
                if (dep >= MinDuckDepth && dep <= MaxDuckDepth && (inWindow < 0 || dep > nodes[inWindow].Depth)) inWindow = i;
            }
            if (inWindow >= 0) duck = inWindow;
            return new DuckMaze(graph, seed, start, nodes, index, duck);
        }

        public TileId Tile(int i) => nodes[i].Frame.Tile;
        public bool Contains(TileId tile) => index.ContainsKey(tile);

        public bool TryPlace(TileId tile, out int2 cell, out CellFrame frame)
        {
            if (index.TryGetValue(tile, out int i)) { cell = nodes[i].Cell; frame = nodes[i].Frame; return true; }
            cell = default; frame = default; return false;
        }

        public int WallMask(TileId tile)
        {
            if (index.TryGetValue(tile, out int i)) return ~nodes[i].Open & 0xF;
            int mask = 0;
            for (int k = 0; k < 4; k++) if (index.ContainsKey(graph.Neighbour(tile, k))) mask |= 1 << k;
            return mask;
        }

        public bool IsWall(TileId tile, int edge) => ((WallMask(tile) >> (edge & 3)) & 1) != 0;

        public int[] SolutionDirections()
        {
            var path = new int[nodes[duck].Depth];
            for (int i = duck, k = path.Length - 1; nodes[i].Parent >= 0; i = nodes[i].Parent, k--) path[k] = nodes[i].FromDir;
            return path;
        }

        public static bool TryDecode(int code, out int seed, out int2 startCell)
        {
            seed = code >> 4; startCell = new int2(code & 3, (code >> 2) & 3);
            return code > 0 && seed >= 1 && seed <= MaxSeed;
        }

        // SplitMix64 (Steele, Lea, Flood 2014): the same numbers on the phone (IL2CPP) and the Station (.NET).
        struct SplitMix64
        {
            ulong s;
            public SplitMix64(ulong seed) { s = seed; }
            public ulong Next()
            {
                ulong z = s += 0x9E3779B97F4A7C15UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
            public int Below(int n) => (int)(Next() % (ulong)n);
        }
    }
}
