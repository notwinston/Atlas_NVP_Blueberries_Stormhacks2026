namespace Atlas.Kernel
{
    // P2-B03: queries on the public TileGraph API only (additions, not contract surface).
    public static class TileGraphQueries
    {
        // k in 0..3 with g.Neighbour(from, k) == to, else -1.
        public static int EdgeTo(this TileGraph g, TileId from, TileId to)
        {
            for (int k = 0; k < 4; k++) if (g.Neighbour(from, k) == to) return k;
            return -1;
        }

        // True iff exactly one tile m has EdgeTo(m, a) >= 0 and EdgeTo(m, b) >= 0, searched over the 4 neighbours of a.
        // Unique for Rule S: the {4,5} adjacency graph has no 4-cycles (its dual {5,4} has 5-gon faces).
        public static bool TryCommonNeighbour(this TileGraph g, TileId a, TileId b, out TileId middle)
        {
            middle = default;
            int count = 0;
            for (int k = 0; k < 4; k++)
            {
                var m = g.Neighbour(a, k);
                if (g.EdgeTo(m, a) >= 0 && g.EdgeTo(m, b) >= 0) { count++; middle = m; }
            }
            if (count != 1) { middle = default; return false; }
            return true;
        }
    }
}
