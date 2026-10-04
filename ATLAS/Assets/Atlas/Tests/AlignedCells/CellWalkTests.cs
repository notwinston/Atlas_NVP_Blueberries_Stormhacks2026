using System;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class CellWalkTests
    {
        static CellGrid G(double w, double d) => CellGrid.FromFreeRectangle(new FreeRectangle(w, d));

        static TileId Chain(TileGraph graph, params int[] edges)
        {
            var t = TileId.Zero;
            foreach (int e in edges) t = graph.Neighbour(t, e);
            return t;
        }

        // Acceptance P1-B10 (Editor half); Master Plan §4.7: four quarter-turns end in a fifth tile, a fifth returns to the first
        [Test]
        public void Mode2_CornerQuarterTurns()
        {
            var graph = new TileGraph(5);
            var starts = new[] { TileId.Zero, Chain(graph, 1), Chain(graph, 1, 2), Chain(graph, 1, 2, 0), Chain(graph, 1, 2, 0, 3), Chain(graph, 1, 2, 0, 3, 1) };
            foreach (var grid in new[] { G(2.4, 2.4), G(1.2, 1.4) })
            {
                var corners = new System.Collections.Generic.List<int2>();
                for (int X = 1; X < grid.CountX; X++) for (int Z = 1; Z < grid.CountZ; Z++) corners.Add(new int2(X, Z));
                foreach (var c in corners)
                {
                    var loop = new[] { new int2(c.x - 1, c.y - 1), new int2(c.x, c.y - 1), new int2(c.x, c.y), new int2(c.x - 1, c.y) };
                    for (int sense = 0; sense < 2; sense++)
                    foreach (var st in starts)
                    for (int turn = 0; turn < 4; turn++)
                    {
                        var walker = new CellWalker(grid, graph);
                        var f0 = new CellFrame(st, turn);
                        walker.Reset(grid.CellCentre(loop[0]), f0);
                        var t = new TileId[11];
                        t[0] = st;
                        for (int m = 1; m <= 10; m++)
                        {
                            var cell = sense == 0 ? loop[m & 3] : loop[(4 - (m & 3)) & 3];
                            Assert.AreEqual(1, walker.Walk(grid.CellCentre(cell)));
                            t[m] = walker.Frame.Tile;
                        }
                        for (int i = 0; i <= 4; i++) for (int j = i + 1; j <= 4; j++) Assert.AreNotEqual(t[i], t[j], $"t{i} vs t{j}");
                        Assert.AreEqual(t[0], t[5]);
                        Assert.AreEqual(t[0], t[10]);
                    }
                }
            }
        }

        // Master Plan §4.7 neighbour stepping; §4.8 one kernel serves both
        [Test]
        public void Mode2_StepBackAndStraight()
        {
            var graph = new TileGraph(5);
            foreach (var tile in new[] { TileId.Zero, Chain(graph, 1), Chain(graph, 1, 2), Chain(graph, 1, 2, 0, 3) })
            for (int turn = 0; turn < 4; turn++)
            for (int d = 0; d < 4; d++)
            {
                var f = new CellFrame(tile, turn);
                var f1 = f.Cross(graph, d);
                Assert.AreEqual(f, f1.Cross(graph, (d + 2) & 3));
                Assert.AreEqual(graph.Neighbour(f.Tile, f.TileEdge(d)), f1.Tile);
                int back = -1;
                for (int e = 0; e < 4; e++) if (graph.Neighbour(f1.Tile, e) == f.Tile) back = e;
                Assert.GreaterOrEqual(back, 0);
                Assert.AreEqual((back + 2) & 3, f1.TileEdge(d));
            }
        }

        // DV4, DV5
        [Test]
        public void Mode2_WalkOrderingAndClamp()
        {
            var graph = new TileGraph(5);
            var grid = G(2.4, 2.4);
            var f = new CellFrame(Chain(graph, 1, 2), 3);
            var w = new CellWalker(grid, graph);
            double g = grid.Side;
            w.Reset(grid.Origin + g * new double2(0.5, 0.95), f);
            Assert.AreEqual(2, w.Walk(grid.Origin + g * new double2(1.5, 1.25)));
            Assert.AreEqual(f.Cross(graph, 1).Cross(graph, 0), w.Frame);
            Assert.AreNotEqual(f.Cross(graph, 0).Cross(graph, 1), w.Frame);
            w.Reset(grid.Origin + g * new double2(0.5, 0.5), f);
            Assert.AreEqual(0, w.Walk(grid.Origin + g * new double2(0.7, 0.4)));

            var inside = new CellWalker(grid, graph);
            inside.Reset(grid.Origin + g * new double2(0.5, 0.5), f);
            inside.Walk(grid.Origin + g * new double2(1.5, 0.5));
            inside.Walk(grid.Origin + g * new double2(2.5, 0.5));
            var outside = new CellWalker(grid, graph);
            outside.Reset(grid.Origin + g * new double2(0.5, 0.5), f);
            outside.Walk(grid.Origin + g * new double2(1.5, -1.0));
            outside.Walk(grid.Origin + g * new double2(2.5, -1.0));
            Assert.AreEqual(inside.Frame, outside.Frame);
            Assert.AreEqual(inside.Cell, outside.Cell);
        }

        // P1-B10 output "switch from Mode 1 at the same tile id"; Master Plan §4.8
        [Test]
        public void Mode2_SwitchFromCurvedWalk()
        {
            var graph = new TileGraph(5);
            var grid = G(2.4, 2.4);
            var tile = Chain(graph, 1, 2);
            double[] th = { 0, 0.3, Math.PI / 2 - 0.2, Math.PI, -Math.PI / 2 + 0.1, 2.5 };
            int[] expect = { 0, 0, 3, 2, 1, 2 };
            for (int i = 0; i < th.Length; i++)
            {
                var pose = new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.2, -0.1), -1) * Iso2.Rot(th[i]) };
                int exp = (int)Math.Floor(-th[i] / (Math.PI / 2) + 0.5) & 3;
                Assert.AreEqual(expect[i], exp);
                Assert.AreEqual(expect[i], CellWalker.TurnFromCurvedWalk(pose.Local), $"theta {th[i]}");
                var head = new double2(0.3, -0.2);
                var space = new AlignedCellsSpace(graph);
                space.Configure(new FreeRectangle(2.4, 2.4));
                space.Enter(pose, head);
                Assert.AreEqual(tile, space.Walker.Frame.Tile);
                Assert.AreEqual(grid.CellAt(head), space.Walker.Cell);
                bool found = false;
                for (int k = 0; k < space.Instances.Count; k++)
                    if (space.Instances[k].Cell.Equals(space.Walker.Cell)) { found = true; Assert.AreEqual(tile, space.Instances[k].Frame.Tile); }
                Assert.IsTrue(found);
            }
        }
    }
}
