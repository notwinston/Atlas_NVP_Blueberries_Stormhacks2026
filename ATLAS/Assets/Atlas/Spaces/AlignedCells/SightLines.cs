using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells
{
    // Master Plan §4.7; R1 §A.3 and §F.7: exact and combinatorial. One instance per monotone cell path
    // from the walker's cell to a cell within two steps, clipped by per-fragment gates round the interior posts.
    public static class SightLines
    {
        public const int ViewRange = 2;       // Chebyshev cells; R1 §D.4 "within two steps"; the path table below depends on it
        public const int MaxInstances = 57;   // §4.7, §8.5
        public const int MaxGates = 4;        // §8.3

        static readonly string[][] PathTable = BuildPathTable();

        static string[][] BuildPathTable()
        {
            var t = new string[(ViewRange + 1) * (ViewRange + 1)][];
            for (int a = 0; a <= ViewRange; a++)
                for (int b = 0; b <= ViewRange; b++)
                {
                    var list = new System.Collections.Generic.List<string>();
                    Gen("", a, b, list);
                    // no straight segment runs two whole cells along one axis from cell (0,0) to cell (2,2)
                    if (a == 2 && b == 2) { list.Remove("RRUU"); list.Remove("UURR"); }
                    t[a * (ViewRange + 1) + b] = list.ToArray();
                }
            return t;
        }

        static void Gen(string s, int r, int u, System.Collections.Generic.List<string> into)
        {
            if (r == 0 && u == 0) { into.Add(s); return; }
            if (r > 0) Gen(s + "R", r - 1, u, into);
            if (u > 0) Gen(s + "U", r, u - 1, into);
        }

        public static void Build(CellGrid grid, TileGraph graph, int2 walkerCell, CellFrame walkerFrame, CellInstanceList into)
        {
            into.Clear();
            Span<int> rX = stackalloc int[ViewRange + 1];
            for (int dz = -ViewRange; dz <= ViewRange; dz++)
                for (int dx = -ViewRange; dx <= ViewRange; dx++)
                {
                    int2 c = walkerCell + new int2(dx, dz);
                    if (!grid.Contains(c)) continue;
                    int a = Math.Abs(dx), b = Math.Abs(dz);
                    int sx = dx >= 0 ? 1 : -1, sz = dz >= 0 ? 1 : -1;
                    int dirR = sx > 0 ? 0 : 2, dirU = sz > 0 ? 1 : 3;
                    string[] paths = PathTable[a * (ViewRange + 1) + b];
                    for (int p = 0; p < paths.Length; p++)
                    {
                        string path = paths[p];
                        var inst = new CellInstance { Cell = c };
                        var f = walkerFrame;
                        for (int i = 0; i < path.Length; i++) f = f.Cross(graph, path[i] == 'R' ? dirR : dirU);
                        inst.Frame = f;
                        if (a >= 1 && b >= 1)
                        {
                            int ups = 0, xi = 0;
                            for (int i = 0; i < path.Length; i++) { if (path[i] == 'U') ups++; else rX[++xi] = ups; }
                            int n = 0;
                            for (int X = 1; X <= a; X++)
                                for (int Z = 1; Z <= b; Z++)
                                {
                                    int canonSide = rX[X] < Z ? -1 : 1;     // vertex above the sight line keeps side -1
                                    int side = canonSide * sx * sz;         // a single-axis reflection flips cross products
                                    double2 post = grid.Corner(walkerCell.x + (sx > 0 ? X : 1 - X), walkerCell.y + (sz > 0 ? Z : 1 - Z));
                                    SetGate(ref inst, n++, new double4(post.x, post.y, side, 0));
                                }
                            inst.GateCount = n;
                        }
                        into.Add(inst);
                    }
                }
        }

        static void SetGate(ref CellInstance inst, int i, double4 g)
        {
            switch (i) { case 0: inst.Gate0 = g; break; case 1: inst.Gate1 = g; break; case 2: inst.Gate2 = g; break; default: inst.Gate3 = g; break; }
        }
    }
}
