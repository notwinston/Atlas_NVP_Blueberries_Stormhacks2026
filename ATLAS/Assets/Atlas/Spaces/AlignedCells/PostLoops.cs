using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7 and §10.1.3 (S3 exhibit card): circling one corner post. On the real floor four cells meet at a post; in the
    // world five tiles do, so after four quarter-turns round one post the walker is back in the same cell but on another tile, and
    // after five the tile it started on comes back (one cell further round). This keeps, for the four corners of the walker's cell,
    // the signed quarter-turns walked round each since the walker first stood next to it, and the tile it showed then.
    // Engine-free and allocation-free.
    public sealed class PostLoops
    {
        /// <summary>Quarter-turns round one post after which the cells.postLoop event is due (Master Plan §10.1.3).</summary>
        public const int LoopTurns = 4;

        readonly CellGrid grid;
        // Indexed by the corner of the walker's cell: 0 = (x, z), 1 = (x+1, z), 2 = (x+1, z+1), 3 = (x, z+1).
        readonly int2[] posts = new int2[4];
        readonly int[] turns = new int[4];
        readonly TileId[] startTiles = new TileId[4];
        readonly int2[] nextPosts = new int2[4];
        readonly int[] nextTurns = new int[4];
        readonly TileId[] nextStarts = new TileId[4];

        /// <summary>The post that the last crossing turned round furthest, the turns and whether its starting tile came back.</summary>
        public int2 LastPost { get; private set; }
        public int LastTurns { get; private set; }
        public bool LastSameTile { get; private set; }

        public PostLoops(CellGrid grid) { this.grid = grid; }

        /// <summary>Forgets every count: the walker stands in cell, on frame's tile, and has turned round no post.</summary>
        public void Reset(int2 cell, CellFrame frame)
        {
            Corners(cell, posts);
            for (int k = 0; k < 4; k++) { turns[k] = 0; startTiles[k] = frame.Tile; }
            LastTurns = 0; LastSameTile = false; LastPost = posts[0];
        }

        /// <summary>
        /// One crossing from cell from in room direction direction (0 +x, 1 +z, 2 -x, 3 -z), arriving on frame entered. The two
        /// posts at the ends of the crossed edge each gain a signed quarter-turn (+1 counter-clockwise seen from above with x right
        /// and z up); the two posts left behind are forgotten and the two new corners start at zero on the entered tile. True when an
        /// interior post has now been circled LoopTurns quarter-turns or more: LastPost, LastTurns and LastSameTile describe it.
        /// </summary>
        public bool Cross(int2 from, int direction, CellFrame entered)
        {
            int2 to = from + Step(direction);
            Corners(to, nextPosts);
            bool loop = false; int best = 0;
            for (int k = 0; k < 4; k++)
            {
                int2 p = nextPosts[k];
                int i = IndexOf(p);
                if (i < 0) { nextTurns[k] = 0; nextStarts[k] = entered.Tile; continue; }
                // A corner shared by both cells is an end of the crossed edge: the walker turned a quarter round it.
                int t = turns[i] + QuarterTurn(p, from, to);
                nextTurns[k] = t; nextStarts[k] = startTiles[i];
                if (math.abs(t) >= LoopTurns && Interior(p) && math.abs(t) > best)
                {
                    best = math.abs(t); loop = true;
                    LastPost = p; LastTurns = t; LastSameTile = entered.Tile == startTiles[i];
                }
            }
            for (int k = 0; k < 4; k++) { posts[k] = nextPosts[k]; turns[k] = nextTurns[k]; startTiles[k] = nextStarts[k]; }
            return loop;
        }

        /// <summary>The signed quarter-turns walked round post p, or 0 when p is not a corner of the walker's cell.</summary>
        public int TurnsRound(int2 p)
        {
            int i = IndexOf(p);
            return i < 0 ? 0 : turns[i];
        }

        bool Interior(int2 p) => p.x >= 1 && p.x < grid.CountX && p.y >= 1 && p.y < grid.CountZ;

        int IndexOf(int2 p)
        {
            for (int k = 0; k < 4; k++) if (posts[k].Equals(p)) return k;
            return -1;
        }

        static void Corners(int2 cell, int2[] into)
        {
            into[0] = cell; into[1] = cell + new int2(1, 0); into[2] = cell + new int2(1, 1); into[3] = cell + new int2(0, 1);
        }

        static int2 Step(int direction)
            => direction == 0 ? new int2(1, 0) : direction == 1 ? new int2(0, 1) : direction == 2 ? new int2(-1, 0) : new int2(0, -1);

        // Sign of the turn round post p from the centre of cell a to the centre of cell b (cell units, x right, z up).
        static int QuarterTurn(int2 p, int2 a, int2 b)
        {
            double2 u = (double2)a + 0.5 - p, v = (double2)b + 0.5 - p;
            return u.x * v.y - u.y * v.x > 0 ? 1 : -1;
        }
    }
}
