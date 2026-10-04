using System;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
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
                var space = new AlignedCellsCore();
                space.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
                space.Enter(pose, head);
                Assert.IsTrue(space.EnteredFromCurvedWalk);
                Assert.AreEqual(tile, space.Walker.Frame.Tile);
                Assert.AreEqual(expect[i], space.Walker.Frame.Turn);
                Assert.AreEqual(grid.CellAt(head), space.Walker.Cell);
                bool found = false;
                for (int k = 0; k < space.Instances.Count; k++)
                    if (space.Instances[k].Cell.Equals(space.Walker.Cell)) { found = true; Assert.AreEqual(tile, space.Instances[k].Frame.Tile); }
                Assert.IsTrue(found);
            }
        }

        // Master Plan §4.9 (S3 is registered), lane A contract §4: entered from anywhere but Mode 1, the start is the flat kernel's
        // pose and the walker stands in tile zero, unturned, in the cell under the head.
        [Test]
        public void Mode2_EnterRegisteredStartsAtTileZero()
        {
            var flat = new FlatWalkerKernel();
            flat.Configure(KernelConfig.Flat(2.0));
            flat.ResetToFloor(new double2(0.9, -0.9));
            var core = new AlignedCellsCore();
            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
            Assert.Throws<InvalidOperationException>(() => new AlignedCellsCore().Enter(flat.Pose, double2.zero));
            core.Enter(flat.Pose, new double2(0.9, -0.9));
            Assert.IsFalse(core.EnteredFromCurvedWalk);
            Assert.AreEqual(new CellFrame(TileId.Zero, 0), core.Walker.Frame);
            Assert.AreEqual(new int2(2, 0), core.Walker.Cell);
            Assert.AreEqual(0, core.Crossings);

            // A hyperbolic pose with no tile is not a Mode 1 pose.
            core.Enter(new WalkerPose { Tile = default, Local = Iso2.IdentityFor(-1) }, double2.zero);
            Assert.AreEqual(TileId.Zero, core.Walker.Frame.Tile);
        }

        // Master Plan §4.7 on a recorded walk (recordings/walk_lap_sample_3x3: one lap of the 2 m square from (-1, -1), +Z first):
        // eight cell edges are crossed, the walker is back in its cell, and the tile is the one the eight crossings give in order.
        // The lap goes once round all four posts, so it does not come back to tile zero.
        [Test]
        public void Mode2_RecordedLap()
        {
            PoseLog log = Fixtures.Walk("walk_lap_sample_3x3");
            Assert.Greater(log.Samples.Length, 900);
            var core = new AlignedCellsCore();
            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
            float3 p0 = log.Samples[0].Position;
            core.Enter(WalkerPose.Start, new double2(p0.x, p0.z));
            Assert.AreEqual(new int2(0, 0), core.Walker.Cell);
            int rebuilt = 0;
            foreach (HeadPose s in log.Samples)
                if (core.Tick(new double2(s.Position.x, s.Position.z), s.Tracking != TrackingState.Normal)) rebuilt++;
            Assert.AreEqual(8, core.Crossings);
            Assert.AreEqual(8, rebuilt);
            Assert.AreEqual(new int2(0, 0), core.Walker.Cell);
            var f = new CellFrame(TileId.Zero, 0);
            foreach (int d in new[] { 1, 1, 0, 0, 3, 3, 2, 2 }) f = f.Cross(core.Graph, d);
            Assert.AreEqual(f, core.Walker.Frame);
            Assert.AreNotEqual(TileId.Zero, core.Walker.Frame.Tile);

            // The same walk at a sixth of the rate crosses the same edges in the same order.
            var coarse = new AlignedCellsCore();
            coarse.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
            coarse.Enter(WalkerPose.Start, new double2(p0.x, p0.z));
            for (int i = 0; i < log.Samples.Length; i += 6) coarse.Tick(new double2(log.Samples[i].Position.x, log.Samples[i].Position.z), false);
            float3 last = log.Samples[log.Samples.Length - 1].Position;
            coarse.Tick(new double2(last.x, last.z), false);
            Assert.AreEqual(core.Walker.Frame, coarse.Walker.Frame);
        }

        // Lane B contract §4 (KernelStep.Rejected): a head displacement the kernel refused steps no tile. The walker keeps its
        // tile and stands in the cell under the head; a head position that is not finite changes nothing.
        [Test]
        public void Mode2_RejectedStepKeepsTheTile()
        {
            var core = new AlignedCellsCore();
            core.Configure(Fixtures.Room(RoomFixtures.Sample3x3), new ParamRegistry());
            CellGrid grid = core.Grid;
            core.Enter(WalkerPose.Start, grid.CellCentre(new int2(0, 0)));
            core.Tick(grid.CellCentre(new int2(1, 0)), false);
            CellFrame before = core.Walker.Frame;
            Assert.AreEqual(1, core.Crossings);

            Assert.IsTrue(core.Tick(grid.CellCentre(new int2(2, 2)), true), "the instances follow the cell");
            Assert.AreEqual(before, core.Walker.Frame);
            Assert.AreEqual(new int2(2, 2), core.Walker.Cell);
            Assert.AreEqual(1, core.Crossings);
            Assert.IsFalse(core.Tick(new double2(double.NaN, 0), false));
            Assert.AreEqual(new int2(2, 2), core.Walker.Cell);

            // The next accepted step starts from where the head was seen last.
            core.Tick(grid.CellCentre(new int2(2, 1)), false);
            Assert.AreEqual(before.Cross(core.Graph, 3), core.Walker.Frame);
            Assert.AreEqual(2, core.Crossings);
        }

        // Master Plan §4.8 "one kernel serves both": the quarter-turn of a rotation, exact at the four turns State carries.
        [Test]
        public void Mode2_TurnFromRotation()
        {
            for (int turn = 0; turn < 4; turn++)
            {
                double theta = -turn * Math.PI / 2;
                Assert.AreEqual(turn, CellWalker.TurnFromRotation(Math.Cos(theta), Math.Sin(theta)));
                Assert.AreEqual(turn, CellWalker.TurnFromCurvedWalk(Iso2.Rot(theta, -1)));
            }
        }
    }
}
