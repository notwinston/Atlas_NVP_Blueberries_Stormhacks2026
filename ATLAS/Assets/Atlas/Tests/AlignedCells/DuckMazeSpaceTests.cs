using System.Text;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // S3 duck maze design (2026-10-04) §3 and §5, wired: AlignedCellsSpace hands the core's maze to the view (redrawn whenever
    // the maze changes, the ducks animated every frame) and the run to the head-locked read-out.
    public class DuckMazeSpaceTests
    {
        static readonly int2[] Step = { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) };

        // cells.maze on (the default), every run on `seed`.
        static ParamRegistry MazeRegistry(int seed)
        {
            var r = new ParamRegistry();
            AlignedCellsCore.DeclareParams(r);
            r.Find(AlignedCellsCore.ParamMazeSeed).Set(seed);
            return r;
        }

        // Engine-free: the read-out the space shows each frame, through a whole run, its find and the next run; hidden without a maze.
        [Test]
        public void DuckMaze_ReadoutFollowsTheRun()
        {
            RoomModel room = Fixtures.Rectangle(2.4f, 2.4f);
            ParamRegistry registry = MazeRegistry(1234);
            var core = new AlignedCellsCore();
            core.Configure(room, registry);
            int2 cell = new int2(1, 2);
            core.Enter(WalkerPose.Start, core.Grid.CellCentre(cell));
            var hud = new MazeHud(null);
            double time = 0;
            void Frame(int2 c)
            {
                time += 0.5;
                core.Tick(core.Grid.CellCentre(c), false, time);
                AlignedCellsSpace.ShowRun(hud, core);
            }

            Frame(cell);
            DuckMaze first = core.Maze;
            Assert.AreSame(first, AlignedCellsSpace.ShownMaze(core));
            Assert.IsTrue(hud.Visible);
            Assert.AreEqual("Find the duck  0:00  walls 0", hud.Text.ToString());

            foreach (int d in first.SolutionDirections())
            {
                Assert.IsFalse(core.MazeFound);
                StringAssert.StartsWith("Find the duck  ", hud.Text.ToString(), "the live time while searching");
                cell += Step[d];
                Frame(cell);
            }
            Assert.IsTrue(core.MazeFound);
            Assert.AreEqual(0.5 * first.DuckDepth, core.MazeElapsed, 1e-9);
            var want = new StringBuilder();
            MazeHud.Format(want, 0.5 * first.DuckDepth, first.DuckDepth, 0, true);
            Assert.AreEqual(want.ToString(), hud.Text.ToString());
            StringAssert.StartsWith("Found the duck!  " + first.DuckDepth + " steps, 0 walls crossed, ", hud.Text.ToString());

            Frame(cell); Frame(cell);
            Assert.AreEqual(want.ToString(), hud.Text.ToString(), "the time is frozen at the find");

            // The next run, five seconds after the find: a new maze to draw and a new read-out.
            time = core.MazeFoundTime + AlignedCellsCore.RestartSeconds - 0.5;
            Frame(cell);
            Assert.AreNotSame(first, core.Maze);
            Assert.AreSame(core.Maze, AlignedCellsSpace.ShownMaze(core));
            Assert.IsFalse(core.MazeFound);
            Assert.AreEqual("Find the duck  0:00  walls 0", hud.Text.ToString());

            // cells.maze off: classic S3, nothing to draw, no read-out.
            registry.Find(AlignedCellsCore.ParamMaze).Set(false);
            core.Configure(room, registry);
            core.Enter(WalkerPose.Start, core.Grid.CellCentre(int2.zero));
            Frame(int2.zero);
            Assert.IsNull(AlignedCellsSpace.ShownMaze(core));
            Assert.IsFalse(hud.Visible);
        }

#if !ATLAS_HEADLESS
        // How many cell instances show the duck's tile (the view draws at most MaxDuckViews of them).
        static int DuckViews(AlignedCellsCore core)
        {
            int k = 0;
            for (int i = 0; i < core.Instances.Count; i++) if (core.Instances[i].Frame.Tile.Equals(core.Maze.DuckTile)) k++;
            return math.min(k, AlignedCellsView.MaxDuckViews);
        }

        static void AssertMazeDrawn(AlignedCellsCore core, AlignedCellsView view)
        {
            Assert.AreEqual(DuckViews(core), view.DuckViewCount);
            for (int i = 0; i < core.Instances.Count; i++)
            {
                CellFrame f = core.Instances[i].Frame;
                Assert.AreSame(view.CellMesh(AlignedCellsView.InstanceKerbMask(core.Maze.WallMask(f.Tile), f.Turn)), view.InstanceMesh(i));
            }
        }

        [Test, Category("Unity")]
        public void Space_DrawsTheDuckOfEachRun()
        {
            using (var stage = new UnityStage(Fixtures.Rectangle(2.4f, 2.4f), registry: MazeRegistry(1234)))
            {
                stage.Enter(WalkerPose.Start, new int2(1, 2));                   // Enter, one Tick and one Upload
                AlignedCellsCore core = stage.Space.Core;
                AlignedCellsView view = stage.Space.View;
                Assert.IsTrue(core.MazeMode);
                Assert.AreEqual(4, stage.Grid.CountX);
                Assert.IsNotNull(core.Maze);
                AssertMazeDrawn(core, view);

                DuckMaze first = core.Maze;
                int2 c = core.Walker.Cell;
                foreach (int d in first.SolutionDirections())
                {
                    Assert.IsFalse(core.MazeFound);
                    c += Step[d];
                    stage.GoCell(c.x, c.y, () => Assert.AreEqual(DuckViews(core), view.DuckViewCount));
                }
                Assert.IsTrue(core.MazeFound, "walking the solution finds the duck");
                Assert.That(view.DuckViewCount, Is.GreaterThanOrEqualTo(1), "the walker stands on the duck's tile");

                // Animated every frame: after the pop (0.3 s) the duck stays big.
                for (int i = 0; i < 30; i++) stage.Step();
                Assert.AreEqual((float)AlignedCellsView.DuckPopScale, view.DuckTransform(0).localScale.x, 1e-4);

                // Five seconds after the find a new run starts; its maze is drawn at once, without a step.
                for (int i = 0; i < 400 && ReferenceEquals(first, core.Maze); i++) stage.Step();
                Assert.AreNotSame(first, core.Maze);
                Assert.IsFalse(core.MazeFound);
                AssertMazeDrawn(core, view);
            }
        }

        [Test, Category("Unity")]
        public void Space_MazeOffDrawsNoDucks()
        {
            using (var stage = new UnityStage(Fixtures.Rectangle(2.4f, 2.4f)))     // classic: cells.maze off
            {
                stage.Enter(WalkerPose.Start, new int2(0, 0));
                AlignedCellsCore core = stage.Space.Core;
                AlignedCellsView view = stage.Space.View;
                Assert.IsFalse(core.MazeMode);
                Assert.IsNull(core.Maze);
                void Check()
                {
                    Assert.AreEqual(0, view.DuckViewCount);
                    for (int i = 0; i < core.Instances.Count; i++) Assert.AreSame(view.CellMesh(0), view.InstanceMesh(i), "no walls");
                }
                Check();
                stage.GoCell(1, 0, Check);
                stage.GoCell(1, 1, Check);
                stage.GoCell(0, 1, Check);
                Assert.IsFalse(stage.Space.Hud.Visible);
            }
        }

        [Test, Category("Unity")]
        public void Space_MazeToggleRedrawsAndKeepsTheReadout()
        {
            ParamRegistry registry = MazeRegistry(1234);
            using (var stage = new UnityStage(Fixtures.Rectangle(2.4f, 2.4f), registry: registry))
            {
                stage.Enter(WalkerPose.Start, new int2(1, 1));
                MazeHud hud = stage.Space.Hud;
                Assert.IsNotNull(hud);
                Assert.IsTrue(hud.Visible);

                registry.Find(AlignedCellsCore.ParamMaze).Set(false);
                stage.Step();                                                    // reconfigured: classic grid, no maze
                Assert.IsFalse(stage.Space.Core.MazeMode);
                Assert.AreEqual(0, stage.Space.View.DuckViewCount);
                Assert.AreSame(hud, stage.Space.Hud, "one read-out across reconfigures");
                Assert.IsFalse(hud.Visible);

                registry.Find(AlignedCellsCore.ParamMaze).Set(true);
                stage.Step();
                Assert.IsTrue(stage.Space.Core.MazeMode);
                AssertMazeDrawn(stage.Space.Core, stage.Space.View);
                Assert.AreSame(hud, stage.Space.Hud);
                Assert.IsTrue(hud.Visible);

                stage.Space.Exit();
                Assert.IsFalse(hud.Visible, "hidden on Exit");
            }
        }

        // Entered from Curved Walk with cells.maze on: classic S3 for this entry, drawn on the classic grid (the views follow the
        // core's grid), no duck and no read-out; a cells.maze toggle re-enters fresh, never at the Mode-1 tile.
        [Test, Category("Unity")]
        public void Space_EnteredFromCurvedWalkDrawsClassic()
        {
            ParamRegistry registry = MazeRegistry(1234);
            RoomModel room = Fixtures.Rectangle(2.4f, 2.4f);
            using (var stage = new UnityStage(room, registry: registry))
            {
                var tile = TileId.Zero.Step(1).Step(2);
                stage.Enter(new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) }, new int2(1, 1));
                AlignedCellsCore core = stage.Space.Core;
                Assert.IsFalse(core.MazeMode);
                Assert.IsTrue(core.EnteredFromCurvedWalk);
                Assert.AreEqual(tile, core.Walker.Frame.Tile);
                Assert.AreEqual(CellGrid.FromRoom(room).CountX, stage.Grid.CountX);
                Assert.AreEqual(core.Instances.Count, stage.Space.View.ActiveInstanceCount);
                Assert.AreEqual(0, stage.Space.View.DuckViewCount);
                Assert.IsFalse(stage.Space.Hud.Visible);
                stage.Step(); stage.Step();
                Assert.IsFalse(core.NeedsReconfigure);
                Assert.AreEqual(tile, core.Walker.Frame.Tile, "no reconfigure loop");

                registry.Find(AlignedCellsCore.ParamMaze).Set(false);
                stage.Step();
                Assert.IsFalse(core.MazeMode);
                Assert.AreEqual(new CellFrame(TileId.Zero, 0), core.Walker.Frame, "re-entered fresh: no stale Mode-1 tile");

                registry.Find(AlignedCellsCore.ParamMaze).Set(true);
                stage.Step();
                Assert.IsTrue(core.MazeMode, "turned on mid-S3: a maze starts");
                Assert.AreEqual(4, stage.Grid.CountX);
                AssertMazeDrawn(core, stage.Space.View);
                Assert.IsTrue(stage.Space.Hud.Visible);
            }
        }
#endif
    }
}
