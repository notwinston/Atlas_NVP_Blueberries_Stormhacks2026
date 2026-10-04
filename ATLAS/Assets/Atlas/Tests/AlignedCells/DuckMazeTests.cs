using System.Collections.Generic;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class DuckMazeTests
    {
        static CellGrid Grid4() => CellGrid.FromAligned(new Atlas.World.AlignedGrid { N = 4, M = 4, G = 0.5 }, true);
        static CellGrid Grid3() => CellGrid.FromAligned(new Atlas.World.AlignedGrid { N = 3, M = 3, G = 0.5 }, true);
        static readonly int2[] Step = { new int2(1, 0), new int2(0, 1), new int2(-1, 0), new int2(0, -1) };

        [Test]
        public void DuckMaze_IsATreeInsideTheGrid()
        {
            var grid = Grid4(); var graph = new TileGraph(5);
            for (int seed = 1; seed <= 1000; seed++)
            {
                var maze = DuckMaze.Generate(grid, graph, new int2(seed % 4, (seed / 4) % 4), seed);
                Assert.That(maze.TileCount, Is.InRange(2, DuckMaze.TargetTiles));
                Assert.That(maze.Tile(0), Is.EqualTo(TileId.Zero));
                int openEdges = 0;
                for (int i = 0; i < maze.TileCount; i++)
                {
                    TileId t = maze.Tile(i);
                    Assert.That(maze.TryPlace(t, out int2 cell, out CellFrame f), Is.True);
                    Assert.That(grid.Contains(cell), Is.True, "seed " + seed);
                    Assert.That(f.Tile, Is.EqualTo(t));
                    openEdges += 4 - math.countbits(maze.WallMask(t));
                }
                Assert.That(openEdges, Is.EqualTo(2 * (maze.TileCount - 1)), "a tree has n - 1 edges, each open from both sides");
            }
        }

        [Test]
        public void DuckMaze_DuckDepthIsInTheWindowForMostSeeds()
        {
            var grid = Grid4(); var graph = new TileGraph(5);
            int inWindow = 0;
            for (int seed = 1; seed <= 1000; seed++)
            {
                var maze = DuckMaze.Generate(grid, graph, new int2(seed % 4, (seed / 4) % 4), seed);
                Assert.That(maze.DuckDepth, Is.GreaterThanOrEqualTo(6), "seed " + seed);
                Assert.That(maze.SolutionDirections().Length, Is.EqualTo(maze.DuckDepth));
                if (maze.DuckDepth >= DuckMaze.MinDuckDepth && maze.DuckDepth <= DuckMaze.MaxDuckDepth) inWindow++;
            }
            Assert.That(inWindow, Is.GreaterThanOrEqualTo(950));
        }

        // The 3 x 3 grid of a 1.2 to 1.6 m floor: the duck depth window holds as on 4 x 4.
        [Test]
        public void DuckMaze_ThreeByThree()
        {
            var grid = Grid3(); var graph = new TileGraph(5);
            var hist = new SortedDictionary<int, int>();
            for (int seed = 1; seed <= 1000; seed++)
            {
                var maze = DuckMaze.Generate(grid, graph, new int2(seed % 3, (seed / 3) % 3), seed);
                hist.TryGetValue(maze.DuckDepth, out int h); hist[maze.DuckDepth] = h + 1;
            }
            // measured: 8:134 9:181 10:215 11:240 12:230, every seed in the window
            int inWindow = 0;
            foreach (var kv in hist) if (kv.Key >= DuckMaze.MinDuckDepth && kv.Key <= DuckMaze.MaxDuckDepth) inWindow += kv.Value;
            Assert.That(inWindow, Is.GreaterThanOrEqualTo(950));
        }

        [Test]
        public void DuckMaze_SameSeedSameMaze()
        {
            var grid = Grid4();
            var a = DuckMaze.Generate(grid, new TileGraph(5), new int2(1, 2), 4242);
            var b = DuckMaze.Generate(grid, new TileGraph(5), new int2(1, 2), 4242);
            Assert.That(b.TileCount, Is.EqualTo(a.TileCount));
            Assert.That(b.DuckTile, Is.EqualTo(a.DuckTile));
            for (int i = 0; i < a.TileCount; i++) { Assert.That(b.Tile(i), Is.EqualTo(a.Tile(i))); Assert.That(b.WallMask(b.Tile(i)), Is.EqualTo(a.WallMask(a.Tile(i)))); }
        }

        [Test]
        public void DuckMaze_WalkingTheSolutionReachesTheDuck()
        {
            var grid = Grid4(); var graph = new TileGraph(5);
            for (int seed = 1; seed <= 200; seed++)
            {
                var maze = DuckMaze.Generate(grid, graph, new int2(2, 1), seed);
                var walker = new CellWalker(grid, graph);
                walker.Reset(grid.CellCentre(maze.StartCell), new CellFrame(TileId.Zero, 0));
                foreach (int d in maze.SolutionDirections())
                {
                    Assert.That(maze.IsWall(walker.Frame.Tile, walker.Frame.TileEdge(d)), Is.False, "the route never crosses a wall");
                    Assert.That(walker.Walk(grid.CellCentre(walker.Cell + Step[d])), Is.EqualTo(1));
                    Assert.That(maze.TryPlace(walker.Frame.Tile, out int2 cell, out CellFrame f), Is.True);
                    Assert.That(cell, Is.EqualTo(walker.Cell)); Assert.That(f, Is.EqualTo(walker.Frame));
                }
                Assert.That(walker.Frame.Tile, Is.EqualTo(maze.DuckTile));
            }
        }

        [Test]
        public void DuckMaze_WallsAreTheSameFromBothSides()
        {
            var grid = Grid4(); var graph = new TileGraph(5);
            var maze = DuckMaze.Generate(grid, graph, new int2(0, 0), 77);
            for (int i = 0; i < maze.TileCount; i++)
            {
                TileId t = maze.Tile(i);
                for (int k = 0; k < 4; k++)
                {
                    TileId n = graph.Neighbour(t, k);
                    int back = graph.BackEdge(t, k);
                    Assert.That(maze.IsWall(n, back), Is.EqualTo(maze.IsWall(t, k)), "tile " + i + " edge " + k);
                }
            }
        }

        [Test]
        public void DuckMaze_DuckIsADeadEnd()
        {
            var grid = Grid4(); var graph = new TileGraph(5);
            for (int seed = 1; seed <= 200; seed++)
            {
                var maze = DuckMaze.Generate(grid, graph, new int2(seed % 4, (seed / 4) % 4), seed);
                Assert.That(math.countbits(maze.WallMask(maze.DuckTile)), Is.EqualTo(3), "seed " + seed);
            }
        }

        [Test]
        public void DuckMaze_CodeRoundTrips()
        {
            var maze = DuckMaze.Generate(Grid4(), new TileGraph(5), new int2(3, 2), DuckMaze.MaxSeed);
            Assert.That(maze.Code, Is.LessThan(1 << 24), "exact in a float");
            Assert.That((int)(float)maze.Code, Is.EqualTo(maze.Code));
            Assert.That(DuckMaze.TryDecode(maze.Code, out int seed, out int2 cell), Is.True);
            Assert.That(seed, Is.EqualTo(DuckMaze.MaxSeed)); Assert.That(cell, Is.EqualTo(new int2(3, 2)));
            Assert.That(DuckMaze.TryDecode(0, out _, out _), Is.False);
        }
    }
}
