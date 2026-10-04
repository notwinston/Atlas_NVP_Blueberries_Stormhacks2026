using System;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class SightLineTests
    {
        static CellGrid G(double w, double d) => CellGrid.FromFreeRectangle(new FreeRectangle(w, d));

        // Acceptance P1-B10 (13 / 15 / 17); Master Plan §11.1 Mode2_SightLines; §4.7
        [Test]
        public void Mode2_SightLines_CentreEdgeCorner()
        {
            var graph = new TileGraph(5);
            var f0 = new CellFrame(TileId.Zero, 0);
            foreach (var grid in new[] { G(2.4, 2.4), G(3.4, 4.4) })
            {
                Assert.AreEqual(3, grid.CountX); Assert.AreEqual(3, grid.CountZ);
                var list = new CellInstanceList();
                for (int cz = 0; cz < 3; cz++)
                    for (int cx = 0; cx < 3; cx++)
                    {
                        var u = new int2(cx, cz);
                        SightLines.Build(grid, graph, u, f0, list);
                        int ex = (cx == 1 && cz == 1) ? 13 : (cx == 1 || cz == 1) ? 15 : 17;
                        Assert.AreEqual(ex, list.Count, $"cell ({cx},{cz})");
                        var perCell = new int[3, 3];
                        var tiles = new System.Collections.Generic.HashSet<TileId>[3, 3];
                        for (int i = 0; i < list.Count; i++)
                        {
                            var inst = list[i];
                            Assert.LessOrEqual(inst.GateCount, 4);
                            perCell[inst.Cell.x, inst.Cell.y]++;
                            (tiles[inst.Cell.x, inst.Cell.y] ??= new System.Collections.Generic.HashSet<TileId>()).Add(inst.Frame.Tile);
                            if (inst.Cell.Equals(u)) { Assert.AreEqual(0, inst.GateCount); Assert.AreEqual(f0, inst.Frame); }
                            if (inst.Cell.x == cx || inst.Cell.y == cz) Assert.AreEqual(0, inst.GateCount, "row/column placed directly");
                        }
                        for (int x = 0; x < 3; x++)
                            for (int z = 0; z < 3; z++)
                            {
                                Assert.LessOrEqual(perCell[x, z], 4);
                                if (tiles[x, z] != null) Assert.LessOrEqual(tiles[x, z].Count, 4);
                                if (x == cx || z == cz) Assert.AreEqual(1, perCell[x, z]);
                            }
                        Assert.AreEqual(1, perCell[cx, cz]);
                    }
            }
        }

        // Master Plan §4.7 "clipped per pixel against the lines from the eye through the posts"; R1 §F.7
        [Test]
        public void Mode2_GatesPartition()
        {
            var graph = new TileGraph(5);
            var f0 = new CellFrame(new TileGraphProbe(graph).Some(), 1);
            int w43 = 0;
            foreach (var (grid, expectWorst) in new[] { (G(2.4, 2.4), 13 + 4), (G(5.4, 7.4), 42), (G(8.1, 10.8), 57) })
            {
                if (expectWorst == 57) { Assert.AreEqual(6, grid.CountX); Assert.AreEqual(8, grid.CountZ); }
                var list = new CellInstanceList();
                int worst = 0, skipped = 0, checkedN = 0;
                double g = grid.Side;
                for (int cz = 0; cz < grid.CountZ; cz++)
                    for (int cx = 0; cx < grid.CountX; cx++)
                    {
                        var u = new int2(cx, cz);
                        SightLines.Build(grid, graph, u, f0, list);
                        worst = Math.Max(worst, list.Count);
                        double2 cmin = grid.Origin + g * (double2)u;
                        for (int ei = 0; ei < 5; ei++)
                        for (int ej = 0; ej < 5; ej++)
                        {
                            double2 eye = cmin + g * (0.1 + 0.8 * (new double2(ei, ej) + 0.3183098861837907) / 5);
                            for (int dz = -2; dz <= 2; dz++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                var t = u + new int2(dx, dz);
                                if (!grid.Contains(t)) continue;
                                double2 tmin = grid.Origin + g * (double2)t;
                                for (int fi = 0; fi < 7; fi++)
                                for (int fj = 0; fj < 7; fj++)
                                {
                                    double2 frag = tmin + g * (new double2(fi, fj) + new double2(0.6180339887498949, 0.7071067811865476)) / 7; // z phase differs: equal x/z offsets put eye, post and fragment exactly on one diagonal
                                    int kept = -1, keptCount = 0; bool skip = false;
                                    for (int i = 0; i < list.Count; i++)
                                    {
                                        if (!list[i].Cell.Equals(t)) continue;
                                        for (int k = 0; k < list[i].GateCount; k++)
                                        {
                                            double4 gt = list[i].Gate(k);
                                            double2 gg = gt.xy - eye, d = frag - eye;
                                            if (Math.Abs(gg.x * d.y - gg.y * d.x) < 1e-9) skip = true;
                                        }
                                        if (GateMath.Keeps(list[i], eye, frag)) { kept = i; keptCount++; }
                                    }
                                    if (skip) { skipped++; continue; }
                                    Assert.AreEqual(1, keptCount, $"walker ({cx},{cz}) target ({t.x},{t.y})");
                                    Assert.AreEqual(Trace(grid, graph, f0, eye, frag).Tile, list[kept].Frame.Tile);
                                    checkedN++;
                                }
                            }
                        }
                    }
                Assert.AreEqual(0, skipped);
                if (expectWorst >= 42) Assert.AreEqual(expectWorst, worst);
                TestContext.WriteLine($"S2 grid {grid.CountX}x{grid.CountZ}: worst instances {worst}, samples {checkedN}");
                w43++;
            }
        }

        // independent trace of the straight eye -> fragment cell sequence from crossing parameters
        static CellFrame Trace(CellGrid grid, TileGraph graph, CellFrame f, double2 eye, double2 frag)
        {
            int2 a = grid.CellAt(eye), b = grid.CellAt(frag);
            var ev = new System.Collections.Generic.List<(double t, int dir)>();
            for (int X = Math.Min(a.x, b.x) + 1; X <= Math.Max(a.x, b.x); X++)
                ev.Add(((grid.Origin.x + grid.Side * X - eye.x) / (frag.x - eye.x), b.x > a.x ? 0 : 2));
            for (int Z = Math.Min(a.y, b.y) + 1; Z <= Math.Max(a.y, b.y); Z++)
                ev.Add(((grid.Origin.y + grid.Side * Z - eye.y) / (frag.y - eye.y), b.y > a.y ? 1 : 3));
            ev.Sort((p, q) => p.t.CompareTo(q.t));
            foreach (var e in ev) f = f.Cross(graph, e.dir);
            return f;
        }
    }

    // some non-zero tile id reached by edges 1 then 2
    readonly struct TileGraphProbe
    {
        readonly TileGraph g;
        public TileGraphProbe(TileGraph g) { this.g = g; }
        public TileId Some() => g.Neighbour(g.Neighbour(TileId.Zero, 1), 2);
    }
}
