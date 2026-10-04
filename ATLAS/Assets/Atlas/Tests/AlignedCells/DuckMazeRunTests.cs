using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // S3 duck maze design §2 and §5: AlignedCellsCore runs maze games on a 4 x 4 grid: a run from tile zero, the walls walked
    // through counted, the duck found, a new maze five seconds later, and the maze sent to the Station in State.P7.
    public class DuckMazeRunTests
    {
        static readonly int2[] Step = { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) };

        // The phone's core on a free 2.4 m square (0.6 m maze cells), entered in `start`; time goes up 0.5 s per frame.
        sealed class Run
        {
            public readonly RoomModel Room;
            public readonly ParamRegistry Registry = new ParamRegistry();
            public readonly AlignedCellsCore Core = new AlignedCellsCore();
            public readonly RecordingLog Log = new RecordingLog();
            public double Time;

            public Run(RoomModel room = null, bool? maze = null, int seed = 0, int2? start = null, WalkerPose? pose = null)
            {
                Room = room ?? Fixtures.Rectangle(2.4f, 2.4f);
                AlignedCellsCore.DeclareParams(Registry);
                if (maze.HasValue) Registry.Find(AlignedCellsCore.ParamMaze).Set(maze.Value);
                Registry.Find(AlignedCellsCore.ParamMazeSeed).Set(seed);
                Core.Configure(Room, Registry);
                Core.Events = Log;
                Core.Enter(pose ?? WalkerPose.Start, Core.Grid.CellCentre(start ?? new int2(1, 1)));
            }

            public bool At(double2 head, double time) { Time = time; return Core.Tick(head, false, time); }
            public bool GoCell(int2 c) => At(Core.Grid.CellCentre(c), Time + 0.5);
            public bool Stay() => GoCell(Core.Walker.Cell);

            // From the start, along the maze's solution, one cell per frame.
            public void WalkTheSolution()
            {
                int2 c = Core.Walker.Cell;
                foreach (int d in Core.Maze.SolutionDirections())
                {
                    Assert.IsFalse(Core.MazeFound, "not before the duck's tile");
                    c += Step[d];
                    GoCell(c);
                }
            }
        }

        static void AssertSameGrid(CellGrid expected, CellGrid actual)
        {
            Assert.AreEqual(expected.CountX, actual.CountX);
            Assert.AreEqual(expected.CountZ, actual.CountZ);
            Assert.AreEqual(expected.Side, actual.Side, 1e-9);
        }

        [Test]
        public void Maze_GridIsFourByFour()
        {
            var run = new Run();
            Assert.IsTrue(run.Core.MazeMode, "cells.maze is on by default");
            Assert.AreEqual(4, run.Core.Grid.CountX);
            Assert.AreEqual(4, run.Core.Grid.CountZ);
            Assert.AreEqual(0.6, run.Core.Grid.Side, 1e-6);
            Assert.IsNotNull(run.Core.Maze);
            Assert.IsTrue(run.Registry.Find(AlignedCellsCore.ParamMaze).Bool);
            Assert.AreEqual(SpaceIds.ParamGroup(SpaceId.AlignedCells), run.Registry.Find(AlignedCellsCore.ParamMaze).Group);
            Assert.AreEqual((float)DuckMaze.MaxSeed, run.Registry.Find(AlignedCellsCore.ParamMazeSeed).Max);
        }

        [Test]
        public void Maze_TooSmallFloorFallsBack()
        {
            RoomModel room = Fixtures.Rectangle(1.5f, 1.5f);
            Assert.IsNull(CellGrid.Maze(room), "1.5 m / 4 = 0.375 m, below MinSide");
            var run = new Run(room, start: new int2(0, 0));
            Assert.IsFalse(run.Core.MazeMode);
            Assert.IsNull(run.Core.Maze);
            AssertSameGrid(CellGrid.FromRoom(room), run.Core.Grid);
            run.Stay();
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeUnavailable));
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeStart));
            var st = StateMessage.Identity;
            run.Core.WriteState(ref st);
            Assert.AreEqual(0f, st.P7);
        }

        [Test]
        public void Maze_OffIsClassic()
        {
            var run = new Run(maze: false, start: new int2(0, 0));
            Assert.IsFalse(run.Core.MazeMode);
            Assert.IsNull(run.Core.Maze);
            AssertSameGrid(CellGrid.FromRoom(run.Room), run.Core.Grid);
            run.Stay();
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeStart));
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeUnavailable));
            Assert.IsFalse(run.Core.NeedsReconfigure);
        }

        // Any start but Curved Walk's (a flat pose, whatever its tile) begins a maze run at tile zero.
        [Test]
        public void Maze_EnterStartsAtTileZero()
        {
            var tile = TileId.Zero.Step(1).Step(2);
            var run = new Run(seed: 99, start: new int2(2, 1), pose: new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), 0) });
            Assert.IsTrue(run.Core.MazeMode);
            Assert.IsFalse(run.Core.EnteredFromCurvedWalk, "a maze run does not keep the start's tile");
            Assert.AreEqual(new CellFrame(TileId.Zero, 0), run.Core.Walker.Frame);
            Assert.AreEqual(new int2(2, 1), run.Core.Walker.Cell);
            DuckMaze m = run.Core.Maze;
            Assert.AreEqual(run.Core.Walker.Cell, m.StartCell);
            run.Stay();
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeStart));
            string data = run.Log.Last(AlignedCellsCore.EventMazeStart);
            StringAssert.Contains("\"seed\":99", data);
            StringAssert.Contains("\"tiles\":" + m.TileCount, data);
            StringAssert.Contains("\"duckDepth\":" + m.DuckDepth, data);
            StringAssert.Contains("\"startCell\":" + (1 * 4 + 2), data);
            Assert.IsFalse(run.Core.MazeFound);
            Assert.AreEqual(0, run.Core.MazeSteps);
            Assert.AreEqual(0, run.Core.MazeWallsCrossed);
        }

        // S3 entered from S2 Curved Walk (a hyperbolic pose with a tile) keeps the walker's tile (Master Plan §4.8) and runs classic
        // S3 for that entry, with cells.maze on: the classic grid, no maze, P7 = 0, no maze events, nothing to draw.
        [Test]
        public void Maze_EnterFromCurvedWalkRunsClassic()
        {
            var tile = TileId.Zero.Step(1).Step(2);
            var pose = new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) };
            var run = new Run(seed: 99, start: new int2(2, 1), pose: pose);
            Assert.IsTrue(run.Registry.Find(AlignedCellsCore.ParamMaze).Bool, "cells.maze is on");
            Assert.IsFalse(run.Core.MazeMode);
            Assert.IsNull(run.Core.Maze);
            Assert.IsTrue(run.Core.EnteredFromCurvedWalk);
            Assert.AreEqual(tile, run.Core.Walker.Frame.Tile, "Mode 1's tile is kept");
            AssertSameGrid(CellGrid.FromRoom(run.Room), run.Core.Grid);
            Assert.That(run.Core.Instances.Count, Is.GreaterThan(0));
            bool shown = false;
            for (int k = 0; k < run.Core.Instances.Count; k++)
                if (run.Core.Instances[k].Cell.Equals(run.Core.Walker.Cell)) { shown = true; Assert.AreEqual(tile, run.Core.Instances[k].Frame.Tile); }
            Assert.IsTrue(shown);

            for (int i = 0; i < 3; i++) run.Stay();
            run.GoCell(new int2(1, 0));
            Assert.IsFalse(run.Core.NeedsReconfigure, "cells.maze did not move: no reconfigure loop");
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeStart));
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeUnavailable));
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeWall));
            Assert.That(run.Log.Count(AlignedCellsCore.EventCross), Is.GreaterThan(0), "classic S3's own events");
            Assert.IsNull(AlignedCellsSpace.ShownMaze(run.Core));
            var st = StateMessage.Identity;
            run.Core.WriteState(ref st);
            Assert.AreEqual(0f, st.P7);
            Assert.AreEqual(run.Core.Walker.Frame.Tile, new TileId(st.Tile0, st.Tile1, st.Tile2, st.Tile3, st.Tile4, st.Tile5));
            Assert.AreEqual((float)run.Core.Grid.CountX, st.P4);

            // Entered again from anywhere else (the reconfigure's fresh start): the maze grid and a run at tile zero.
            run.Core.Enter(WalkerPose.Start, CellGrid.Maze(run.Room).CellCentre(new int2(2, 1)));
            Assert.IsTrue(run.Core.MazeMode);
            Assert.IsFalse(run.Core.EnteredFromCurvedWalk);
            Assert.AreEqual(4, run.Core.Grid.CountX);
            Assert.AreEqual(new CellFrame(TileId.Zero, 0), run.Core.Walker.Frame);
            Assert.AreEqual(new int2(2, 1), run.Core.Walker.Cell);
            Assert.IsNotNull(run.Core.Maze);
            run.Stay();
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeStart));
        }

        // A cells.maze toggle while entered from Curved Walk: the space configures again and re-enters with a fresh start
        // (WalkerPose.Start), so turning the maze off never revives the stale Mode-1 tile, and turning it on starts a maze.
        [Test]
        public void Maze_ToggleAfterCurvedWalkStartsFresh()
        {
            var tile = TileId.Zero.Step(1).Step(2);
            var run = new Run(seed: 99, start: new int2(2, 1), pose: new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) });
            Assert.IsFalse(run.Core.MazeMode);
            run.Registry.Find(AlignedCellsCore.ParamMaze).Set(false);
            run.Stay();
            Assert.IsTrue(run.Core.NeedsReconfigure);
            run.Core.Configure(run.Room, run.Registry);
            run.Core.Enter(AlignedCellsSpace.ReconfigureStart, run.Core.Grid.CellCentre(int2.zero));
            Assert.IsFalse(run.Core.MazeMode);
            Assert.IsFalse(run.Core.EnteredFromCurvedWalk);
            Assert.AreEqual(new CellFrame(TileId.Zero, 0), run.Core.Walker.Frame, "no stale Mode-1 tile");

            run.Registry.Find(AlignedCellsCore.ParamMaze).Set(true);
            run.Stay();
            Assert.IsTrue(run.Core.NeedsReconfigure);
            run.Core.Configure(run.Room, run.Registry);
            run.Core.Enter(AlignedCellsSpace.ReconfigureStart, run.Core.Grid.CellCentre(int2.zero));
            Assert.IsTrue(run.Core.MazeMode, "turned on mid-S3: a maze starts");
            Assert.IsNotNull(run.Core.Maze);
        }

        [Test]
        public void Maze_FixedSeed()
        {
            var run = new Run(seed: 1234);
            Assert.AreEqual(1234, run.Core.Maze.Seed);
            var fresh = new Run();
            Assert.That(fresh.Core.Maze.Seed, Is.InRange(1, DuckMaze.MaxSeed), "seed 0: a fresh seed for the run");
        }

        [Test]
        public void Maze_WalkingTheSolutionFindsTheDuck()
        {
            var run = new Run(seed: 1234, start: new int2(1, 2));
            DuckMaze m = run.Core.Maze;
            run.Stay();                                                          // the run starts at the first frame: t = 0.5
            run.WalkTheSolution();
            Assert.IsTrue(run.Core.MazeFound);
            Assert.AreEqual(m.DuckTile, run.Core.Walker.Frame.Tile);
            Assert.AreEqual(m.DuckDepth, run.Core.MazeSteps);
            Assert.AreEqual(0, run.Core.MazeWallsCrossed);
            Assert.AreEqual(0, run.Log.Count(AlignedCellsCore.EventMazeWall));
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeFound));
            string data = run.Log.Last(AlignedCellsCore.EventMazeFound);
            StringAssert.Contains("\"steps\":" + m.DuckDepth, data);
            StringAssert.Contains("\"walls\":0", data);
            StringAssert.Contains("\"seconds\":", data);
            Assert.AreEqual(run.Time, run.Core.MazeFoundTime);
            Assert.AreEqual(0.5 * m.DuckDepth, run.Core.MazeElapsed, 1e-9);

            double elapsed = run.Core.MazeElapsed;
            run.Stay(); run.Stay();
            Assert.AreEqual(elapsed, run.Core.MazeElapsed, "frozen at the find");
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeFound), "found once");
            Assert.AreSame(m, run.Core.Maze);
        }

        [Test]
        public void Maze_WalkingThroughAWallIsCounted()
        {
            var start = new int2(1, 1);
            Run run = null; int dir = -1;
            for (int seed = 1; seed <= 200 && dir < 0; seed++)
            {
                run = new Run(seed: seed, start: start);
                CellFrame f = run.Core.Walker.Frame;
                for (int d = 0; d < 4 && dir < 0; d++)
                    if (run.Core.Grid.Contains(start + Step[d]) && run.Core.Maze.IsWall(f.Tile, f.TileEdge(d))) dir = d;
            }
            Assert.GreaterOrEqual(dir, 0, "some seed walls an edge of the start");
            CellFrame left = run.Core.Walker.Frame;
            run.Stay();
            run.GoCell(start + Step[dir]);
            Assert.AreEqual(1, run.Core.MazeSteps);
            Assert.AreEqual(1, run.Core.MazeWallsCrossed);
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeWall));
            string data = run.Log.Last(AlignedCellsCore.EventMazeWall);
            StringAssert.Contains("\"tile\":\"" + left.Tile + "\"", data);
            StringAssert.Contains("\"edge\":" + left.TileEdge(dir), data);
            // Through a wall the walker reaches a tile off the maze, or a maze tile the tree does not join here: the far side
            // carries the same wall.
            CellFrame now = run.Core.Walker.Frame;
            Assert.IsTrue(run.Core.Maze.IsWall(now.Tile, now.TileEdge((dir + 2) & 3)), "the wall seen from the other side");

            run.GoCell(start);                                                   // back through the same wall
            Assert.AreEqual(2, run.Core.MazeSteps);
            Assert.AreEqual(2, run.Core.MazeWallsCrossed);
            Assert.AreEqual(2, run.Log.Count(AlignedCellsCore.EventMazeWall));
        }

        [Test]
        public void Maze_RestartsFiveSecondsAfterTheFind()
        {
            var run = new Run(seed: 1234, start: new int2(1, 2));
            DuckMaze first = run.Core.Maze;
            run.Stay();
            run.WalkTheSolution();
            Assert.IsTrue(run.Core.MazeFound);
            double found = run.Core.MazeFoundTime;
            double2 head = run.Core.Grid.CellCentre(run.Core.Walker.Cell);
            CellFrame duckFrame = run.Core.Walker.Frame;

            run.At(head, found + 4.9);
            Assert.AreSame(first, run.Core.Maze, "4.9 s after the find: the same maze");
            Assert.IsTrue(run.Core.MazeFound);
            Assert.AreEqual(duckFrame, run.Core.Walker.Frame);

            Assert.IsTrue(run.At(head, found + AlignedCellsCore.RestartSeconds), "a new run changes what is drawn");
            Assert.AreEqual(2, run.Log.Count(AlignedCellsCore.EventMazeStart));
            Assert.AreNotSame(first, run.Core.Maze);
            Assert.IsFalse(run.Core.MazeFound);
            Assert.AreEqual(new CellFrame(TileId.Zero, 0), run.Core.Walker.Frame, "relabelled to tile zero, turn 0");
            Assert.AreEqual(run.Core.Walker.Cell, run.Core.Maze.StartCell);
            Assert.AreEqual(0, run.Core.MazeSteps);
            Assert.AreEqual(0, run.Core.MazeWallsCrossed);
            Assert.AreEqual(0.0, run.Core.MazeElapsed, 1e-9);

            run.Stay();
            Assert.AreEqual(0.5, run.Core.MazeElapsed, 1e-9, "the new run's clock started at the restart");
            Assert.AreEqual(2, run.Log.Count(AlignedCellsCore.EventMazeStart));
        }

        [Test]
        public void Maze_StateRoundTripRebuildsTheMaze()
        {
            var phone = new Run(seed: 777, start: new int2(2, 1));
            var station = new AlignedCellsCore();
            station.Configure(phone.Room, new ParamRegistry());
            var stationLog = new RecordingLog();
            station.Events = stationLog;
            station.Enter(WalkerPose.Start, double2.zero);
            var wire = new byte[StateMessage.Size];

            void Sync(double time)
            {
                var st = StateMessage.Identity;
                st.SpaceId = (byte)SpaceId.AlignedCells;
                phone.Core.WriteState(ref st);
                Assert.AreEqual((float)phone.Core.Maze.Code, st.P7);
                st.WriteTo(wire, 0);
                var received = new StateMessage();
                Assert.IsTrue(received.ReadFrom(wire, 0, wire.Length));
                station.ReadState(in received);
                station.Tick(new double2(double.NaN, double.NaN), false, time);
                DuckMaze a = phone.Core.Maze, b = station.Maze;
                Assert.IsNotNull(b);
                Assert.AreEqual(a.Code, b.Code);
                Assert.AreEqual(a.DuckTile, b.DuckTile);
                Assert.AreEqual(a.TileCount, b.TileCount);
                for (int i = 0; i < a.TileCount; i++)
                {
                    TileId t = a.Tile(i);
                    Assert.AreEqual(a.WallMask(t), b.WallMask(t));
                    for (int k = 0; k < 4; k++)
                    {
                        TileId n = phone.Core.Graph.Neighbour(t, k);
                        Assert.AreEqual(a.WallMask(n), b.WallMask(n));
                    }
                }
                Assert.AreEqual(phone.Core.Walker.Frame, station.Walker.Frame);
                Assert.AreEqual(phone.Core.MazeFound, station.MazeFound);
            }

            phone.Stay();
            Sync(phone.Time);
            Assert.IsTrue(station.Remote);
            DuckMaze copy = station.Maze;
            Sync(phone.Time + 0.1);
            Assert.AreSame(copy, station.Maze, "the same code: not generated again");

            int2 c = phone.Core.Walker.Cell;
            foreach (int d in phone.Core.Maze.SolutionDirections())
            {
                c += Step[d];
                phone.GoCell(c);
                Sync(phone.Time);
            }
            Assert.IsTrue(station.MazeFound, "the Station sees the find");
            phone.At(phone.Core.Grid.CellCentre(c), phone.Core.MazeFoundTime + AlignedCellsCore.RestartSeconds);
            Sync(phone.Time);
            Assert.IsFalse(station.MazeFound);
            Assert.AreEqual(0, stationLog.Events.Count, "the Station's copy logs nothing");

            var off = new Run(maze: false);
            off.Stay();
            var blank = StateMessage.Identity;
            blank.P7 = 5f;
            off.Core.WriteState(ref blank);
            Assert.AreEqual(0f, blank.P7, "no maze: P7 = 0");
        }

        [Test]
        public void Maze_ToggleWhileEnteredAsksForReconfigure()
        {
            var run = new Run();
            run.Stay();
            Assert.IsFalse(run.Core.NeedsReconfigure);
            run.Registry.Find(AlignedCellsCore.ParamMaze).Set(false);
            run.Stay();
            Assert.IsTrue(run.Core.NeedsReconfigure);
            run.Core.Configure(run.Room, run.Registry);
            Assert.IsFalse(run.Core.NeedsReconfigure);
            Assert.IsFalse(run.Core.MazeMode);
            AssertSameGrid(CellGrid.FromRoom(run.Room), run.Core.Grid);
        }

        // A head move past a post corner crosses two cell edges in one frame. When the first enters the duck's tile and the
        // second leaves it, the duck was still found, with the steps counted up to that crossing.
        [Test]
        public void Maze_DuckTilePassedMidFrameIsFound()
        {
            Run run = null; int last = -1, side = -1;
            for (int seed = 1; seed <= 200 && side < 0; seed++)
            {
                run = new Run(seed: seed, start: new int2(1, 1));
                int[] path = run.Core.Maze.SolutionDirections();
                last = path[path.Length - 1];
                int2 duckCell = run.Core.Maze.StartCell;
                foreach (int d in path) duckCell += Step[d];
                for (int k = 1; k <= 3 && side < 0; k += 2)
                    if (run.Core.Grid.Contains(duckCell + Step[(last + k) & 3])) side = (last + k) & 3;
            }
            Assert.GreaterOrEqual(side, 0, "some maze has a cell beside its duck's cell, across its last step");

            int[] dirs = run.Core.Maze.SolutionDirections();
            int2 c = run.Core.Walker.Cell;
            run.Stay();
            for (int i = 0; i < dirs.Length - 1; i++) { c += Step[dirs[i]]; run.GoCell(c); }
            Assert.IsFalse(run.Core.MazeFound, "one step before the duck");

            // From the centre of the cell before the duck's, diagonally to the cell beside the duck's: a little further along
            // the last step's axis, so that edge is crossed first (into the duck's tile), then the side edge (out of it).
            double2 target = run.Core.Grid.CellCentre(c + Step[last] + Step[side]) + 0.01 * (double2)Step[last];
            run.At(target, run.Time + 0.5);
            Assert.AreEqual(2, run.Core.Walker.LastCrossingCount, "two crossings in one frame");
            Assert.AreNotEqual(run.Core.Maze.DuckTile, run.Core.Walker.Frame.Tile, "the frame ends off the duck's tile");
            Assert.IsTrue(run.Core.MazeFound, "the duck's tile was entered on the way");
            Assert.AreEqual(run.Core.Maze.DuckDepth, run.Core.MazeSteps, "steps up to the find");
            Assert.AreEqual(run.Time, run.Core.MazeFoundTime);
            Assert.AreEqual(1, run.Log.Count(AlignedCellsCore.EventMazeFound));
            StringAssert.Contains("\"steps\":" + run.Core.Maze.DuckDepth + ",", run.Log.Last(AlignedCellsCore.EventMazeFound));
        }

        static StateMessage Send(Run phone)
        {
            var st = StateMessage.Identity;
            st.SpaceId = (byte)SpaceId.AlignedCells;
            phone.Core.WriteState(ref st);
            return st;
        }

        static AlignedCellsCore Station(RoomModel room, ParamRegistry registry)
        {
            var station = new AlignedCellsCore();
            station.Configure(room, registry);
            station.Enter(WalkerPose.Start, double2.zero);
            return station;
        }

        static bool Receive(AlignedCellsCore station, in StateMessage st, double time)
        {
            station.ReadState(in st);
            return station.Tick(new double2(double.NaN, double.NaN), false, time);
        }

        // A State whose grid (P4 columns, P5 rows, P6 side) is neither this room's maze grid nor its classic grid is ignored, so its
        // Sheet is never read with the wrong column count.
        [Test]
        public void Maze_StationIgnoresAStateOfAnotherGrid()
        {
            var phone = new Run(seed: 777, start: new int2(2, 1));
            phone.Stay();
            AlignedCellsCore station = Station(phone.Room, new ParamRegistry());
            CellGrid classic = CellGrid.FromRoom(phone.Room);

            StateMessage st = Send(phone);
            st.P4 = 7; st.P5 = 9;
            Receive(station, in st, phone.Time);
            Assert.IsFalse(station.Remote, "7 x 9 is no grid of this room");

            st = Send(phone);
            st.P6 = 0.75f;
            Receive(station, in st, phone.Time);
            Assert.IsFalse(station.Remote, "4 x 4 of another side");

            st = Send(phone);
            st.P4 = classic.CountX; st.P5 = classic.CountZ; st.P6 = (float)classic.Side + 0.2f;
            Receive(station, in st, phone.Time);
            Assert.IsFalse(station.Remote, "the classic counts with another side");
            Assert.IsTrue(station.MazeMode);
            Assert.AreEqual(4, station.Grid.CountX);

            st = Send(phone);
            Receive(station, in st, phone.Time);
            Assert.IsTrue(station.Remote);
            Assert.AreEqual(phone.Core.Walker.Cell, station.Walker.Cell);
        }

        // A Station whose own grid lags (its cells.maze not yet changed, or the phone entered S3 from Curved Walk and runs classic
        // S3 for that entry) takes the phone's grid when it is this room's other one, and reads the Sheet with that grid.
        [Test]
        public void Maze_StationFollowsThePhonesGrid()
        {
            var phone = new Run(seed: 777, start: new int2(2, 1));
            phone.Stay();
            AlignedCellsCore station = Station(phone.Room, Fixtures.ClassicRegistry());
            Assert.IsFalse(station.MazeMode);
            Assert.AreNotEqual(4, station.Grid.CountX, "the classic grid of this room is not 4 x 4");

            Assert.IsTrue(Receive(station, Send(phone), phone.Time), "the grid changed: redraw");
            Assert.IsTrue(station.Remote);
            Assert.IsTrue(station.MazeMode);
            AssertSameGrid(phone.Core.Grid, station.Grid);
            Assert.AreEqual(phone.Core.Maze.Code, station.Maze.Code);
            Assert.AreEqual(phone.Core.Walker.Cell, station.Walker.Cell);
            Assert.AreEqual(phone.Core.Walker.Frame, station.Walker.Frame);
            Assert.IsFalse(station.NeedsReconfigure, "its own cells.maze did not move");

            // And back: a phone running classic S3 (P7 = 0, the room's own grid) on a Station in maze mode.
            var classic = new Run(phone.Room, maze: false, start: new int2(1, 0));
            classic.Stay();
            AlignedCellsCore mazeStation = Station(phone.Room, new ParamRegistry());
            Assert.IsTrue(mazeStation.MazeMode);
            Receive(mazeStation, Send(classic), classic.Time);
            Assert.IsTrue(mazeStation.Remote);
            Assert.IsFalse(mazeStation.MazeMode);
            Assert.IsNull(mazeStation.Maze);
            AssertSameGrid(CellGrid.FromRoom(phone.Room), mazeStation.Grid);
            Assert.AreEqual(classic.Core.Walker.Cell, mazeStation.Walker.Cell);
            Assert.IsNull(AlignedCellsSpace.ShownMaze(mazeStation));
            Assert.IsFalse(mazeStation.NeedsReconfigure);
        }

        // The Station's find is latched: the walker stepping off the duck's tile during the celebration does not take the pop and
        // the spin back. It goes with the run: a new code, P7 = 0, or the walker relabelled to tile zero (a new run with the same
        // code: a fixed seed restarted in the same cell).
        [Test]
        public void Maze_StationKeepsTheFindUntilTheNextRun()
        {
            var phone = new Run(seed: 0, start: new int2(2, 1));                // a fresh seed each run: the next run has a new code
            phone.Stay();
            AlignedCellsCore station = Station(phone.Room, new ParamRegistry());
            Receive(station, Send(phone), phone.Time);
            int2 c = phone.Core.Walker.Cell;
            foreach (int d in phone.Core.Maze.SolutionDirections())
            {
                c += Step[d];
                phone.GoCell(c);
                Receive(station, Send(phone), phone.Time);
            }
            Assert.IsTrue(station.MazeFound);
            double foundTime = station.MazeFoundTime;
            TileId duck = phone.Core.Maze.DuckTile;

            int2 off = c;
            for (int d = 0; d < 4; d++) if (phone.Core.Grid.Contains(c + Step[d])) { off = c + Step[d]; break; }
            phone.GoCell(off);
            Assert.AreNotEqual(duck, phone.Core.Walker.Frame.Tile, "stepped off the duck's tile");
            Assert.IsTrue(phone.Core.MazeFound, "the phone's find stands until the next run");
            Receive(station, Send(phone), phone.Time);
            Assert.AreNotEqual(duck, station.Walker.Frame.Tile);
            Assert.IsTrue(station.MazeFound, "the Station's find stands too");
            Assert.AreEqual(foundTime, station.MazeFoundTime);

            // Same code, the walker relabelled to tile zero: a new run.
            StateMessage st = Send(phone);
            TileId z = TileId.Zero;
            st.SetTile(z.Xu, z.Xv, z.Yu, z.Yv, z.Tu, z.Tv);
            Receive(station, in st, phone.Time + 0.5);
            Assert.IsFalse(station.MazeFound, "tile zero: the next run");

            // Off the duck's tile again with nothing latched; then on it, and the next run proper: a new code.
            Receive(station, Send(phone), phone.Time + 1.0);
            Assert.IsFalse(station.MazeFound, "off the duck's tile, no latch to keep");
            phone.GoCell(c);
            Receive(station, Send(phone), phone.Time);
            Assert.IsTrue(station.MazeFound);
            phone.At(phone.Core.Grid.CellCentre(c), phone.Core.MazeFoundTime + AlignedCellsCore.RestartSeconds);
            Assert.AreNotEqual(station.Maze.Code, phone.Core.Maze.Code);
            Receive(station, Send(phone), phone.Time);
            Assert.IsFalse(station.MazeFound, "a new code: the find goes");
        }

        // The Station's maze is the phone's: a State with P7 = 0 (the phone has no maze) takes it away, and the find with it.
        [Test]
        public void Maze_StationDropsTheMazeWhenP7IsZero()
        {
            var phone = new Run(seed: 777, start: new int2(2, 1));
            var station = new AlignedCellsCore();
            station.Configure(phone.Room, new ParamRegistry());
            station.Enter(WalkerPose.Start, double2.zero);
            StateMessage Send()
            {
                var st = StateMessage.Identity;
                st.SpaceId = (byte)SpaceId.AlignedCells;
                phone.Core.WriteState(ref st);
                return st;
            }

            phone.Stay();
            phone.WalkTheSolution();
            Assert.IsTrue(phone.Core.MazeFound);
            StateMessage withMaze = Send();
            station.ReadState(in withMaze);
            station.Tick(new double2(double.NaN, double.NaN), false, phone.Time);
            Assert.IsNotNull(station.Maze);
            Assert.IsTrue(station.MazeFound);

            StateMessage noMaze = Send();
            noMaze.P7 = 0f;
            station.ReadState(in noMaze);
            Assert.IsTrue(station.Tick(new double2(double.NaN, double.NaN), false, phone.Time + 0.5), "the maze went: redraw");
            Assert.IsNull(station.Maze);
            Assert.IsFalse(station.MazeFound);
        }
    }
}
