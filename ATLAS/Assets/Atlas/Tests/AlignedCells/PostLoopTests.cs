using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // Master Plan §10.1.3, S3 exhibit card: events cells.cross and cells.postLoop; "after four cell crossings round one corner
    // post the tile id differs from the start; after five it matches". The events make the P1-B10 phone walk readable on the
    // Console and in events.jsonl.
    public class PostLoopTests
    {
        // sample_3x3: a 3 x 3 grid, interior posts at corners (1,1), (2,1), (1,2), (2,2). The four cells round post (1,1)
        // counter-clockwise (x right, z up).
        static readonly int2[] RoundPost11 = { new int2(0, 0), new int2(1, 0), new int2(1, 1), new int2(0, 1) };

        static Rig RigWithLog(out RecordingLog log)
        {
            var rig = new Rig(Fixtures.Room(RoomFixtures.Sample3x3), RoundPost11[0]);
            log = new RecordingLog();
            rig.Core.Events = log;
            return rig;
        }

        // Criterion §10.1.3: four quarter-turns -> another tile in the same cell; five -> the starting tile, one cell further on
        [Test]
        public void Mode2_PostLoop_FourTurnsNewTile_FiveReturns()
        {
            for (int sense = 0; sense < 2; sense++)
            {
                var rig = RigWithLog(out RecordingLog log);
                TileId start = rig.Core.Walker.Frame.Tile;
                for (int m = 1; m <= 5; m++)
                {
                    int2 c = sense == 0 ? RoundPost11[m & 3] : RoundPost11[(4 - (m & 3)) & 3];
                    rig.GoCell(c.x, c.y);
                    if (m == 4)
                    {
                        Assert.AreEqual(RoundPost11[0], rig.Core.Walker.Cell, "back in the starting cell");
                        Assert.AreNotEqual(start, rig.Core.Walker.Frame.Tile, "four quarter-turns: a fifth tile");
                    }
                }
                Assert.AreEqual(start, rig.Core.Walker.Frame.Tile, "five quarter-turns: the first tile again");
                Assert.AreEqual(5, log.Count(AlignedCellsCore.EventCross));
                Assert.AreEqual(5, rig.Core.Crossings);

                List<string> loops = log.DataOf(AlignedCellsCore.EventPostLoop);
                Assert.AreEqual(2, loops.Count, "postLoop after the fourth and the fifth quarter-turn");
                string sign = sense == 0 ? "" : "-";
                StringAssert.Contains("\"post_x\":1,\"post_z\":1", loops[0]);
                StringAssert.Contains("\"quarter_turns\":" + sign + "4,\"same_tile\":0", loops[0]);
                StringAssert.Contains("\"quarter_turns\":" + sign + "5,\"same_tile\":1", loops[1]);
                Assert.AreEqual(sense == 0 ? 5 : -5, rig.Core.Loops.TurnsRound(new int2(1, 1)));
            }
        }

        // Stepping back and forth over one edge winds round no post
        [Test]
        public void Mode2_PostLoop_BackAndForthIsNoLoop()
        {
            var rig = RigWithLog(out RecordingLog log);
            for (int i = 0; i < 6; i++) { rig.GoCell(1, 0); rig.GoCell(0, 0); }
            Assert.AreEqual(12, log.Count(AlignedCellsCore.EventCross));
            Assert.AreEqual(0, log.Count(AlignedCellsCore.EventPostLoop));
            Assert.AreEqual(0, rig.Core.Loops.TurnsRound(new int2(1, 1)));
            Assert.AreEqual(0, rig.Core.Loops.TurnsRound(new int2(1, 0)));
        }

        // A straight walk across the room turns round no post either (each edge's two posts turn opposite ways and are left behind)
        [Test]
        public void Mode2_PostLoop_StraightWalkIsNoLoop()
        {
            var rig = RigWithLog(out RecordingLog log);
            for (int i = 0; i < 4; i++) { rig.GoCell(2, 0); rig.GoCell(2, 2); rig.GoCell(0, 2); rig.GoCell(0, 0); }
            Assert.AreEqual(0, log.Count(AlignedCellsCore.EventPostLoop), "round the room's outside cells: no single post circled four times");
            Assert.AreEqual(4 * 8, log.Count(AlignedCellsCore.EventCross));
        }

        // cells.cross carries the cell entered (row * columns + column), the direction, the running count, the tile and its turn
        [Test]
        public void Mode2_Cross_EventFields()
        {
            var rig = RigWithLog(out RecordingLog log);
            rig.GoCell(1, 0);
            CellFrame f = rig.Core.Walker.Frame;
            Assert.AreEqual(1, log.Count(AlignedCellsCore.EventCross));
            string data = log.DataOf(AlignedCellsCore.EventCross)[0];
            Assert.AreEqual("{\"cell\":1,\"dir\":0,\"crossings\":1,\"tile\":\"" + f.Tile + "\",\"turn\":" + f.Turn + "}", data);
            Assert.IsTrue(TileId.TryParse(f.Tile.ToString(), out TileId parsed));
            Assert.AreEqual(f.Tile, parsed, "the tile field round-trips");
        }

        // A head jump the kernel refused re-seats the walker without a tile step, and forgets every count
        [Test]
        public void Mode2_PostLoop_RejectedStepForgetsTheCount()
        {
            var rig = RigWithLog(out RecordingLog log);
            rig.GoCell(1, 0); rig.GoCell(1, 1); rig.GoCell(0, 1);                  // three quarter-turns round (1,1)
            Assert.AreEqual(3, rig.Core.Loops.TurnsRound(new int2(1, 1)));
            double2 back = rig.Grid.CellCentre(new int2(0, 0));
            rig.X = back.x; rig.Z = back.y;
            rig.Step(rejected: true);                                                 // a jump into (0,0): no crossing
            Assert.AreEqual(new int2(0, 0), rig.Core.Walker.Cell);
            Assert.AreEqual(0, rig.Core.Loops.TurnsRound(new int2(1, 1)));
            rig.GoCell(1, 0);
            Assert.AreEqual(0, log.Count(AlignedCellsCore.EventPostLoop));
            Assert.AreEqual(4, log.Count(AlignedCellsCore.EventCross), "the refused jump is not a crossing");
        }

        // The Station's copy draws the phone's frame and logs nothing: the events are the phone's
        [Test]
        public void Mode2_PostLoop_StationCopyLogsNothing()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.Sample3x3);
            var phone = new Rig(room, RoundPost11[0]);
            var station = new AlignedCellsCore();
            station.Configure(room, Fixtures.ClassicRegistry());
            var log = new RecordingLog();
            station.Events = log;
            station.Enter(WalkerPose.Start, double2.zero);
            for (int m = 1; m <= 5; m++)
            {
                int2 c = RoundPost11[m & 3];
                phone.GoCell(c.x, c.y);
                var st = StateMessage.Identity;
                st.SpaceId = (byte)SpaceId.AlignedCells;
                phone.Core.WriteState(ref st);
                station.ReadState(in st);
                station.Tick(new double2(double.NaN, double.NaN), false);
                Assert.AreEqual(phone.Core.Walker.Frame, station.Walker.Frame);
            }
            Assert.AreEqual(0, log.Events.Count);
        }

        // The walker's record of the last Walk: a diagonal through a post is two crossings, in order, each with its frame
        [Test]
        public void Mode2_Walker_RecordsEachCrossing()
        {
            var graph = new TileGraph(5);
            CellGrid grid = CellGrid.FromFreeRectangle(new FreeRectangle(2.4, 2.4));
            var walker = new CellWalker(grid, graph);
            walker.Reset(grid.CellCentre(new int2(0, 0)), new CellFrame(TileId.Zero, 0));
            Assert.AreEqual(0, walker.LastCrossingCount);
            double2 target = grid.CellCentre(new int2(1, 1)) + new double2(0.01, 0);  // just right of the diagonal: x first
            Assert.AreEqual(2, walker.Walk(target));
            Assert.AreEqual(2, walker.LastCrossingCount);
            walker.LastCrossing(0, out int2 from0, out int dir0, out CellFrame f0);
            walker.LastCrossing(1, out int2 from1, out int dir1, out CellFrame f1);
            Assert.AreEqual(new int2(0, 0), from0); Assert.AreEqual(0, dir0);
            Assert.AreEqual(new int2(1, 0), from1); Assert.AreEqual(1, dir1);
            Assert.AreEqual(new CellFrame(TileId.Zero, 0).Cross(graph, 0), f0);
            Assert.AreEqual(walker.Frame, f1);
            Assert.AreEqual(0, walker.Walk(target), "standing still");
            Assert.AreEqual(0, walker.LastCrossingCount);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => walker.LastCrossing(0, out _, out _, out _));
        }
    }
}
