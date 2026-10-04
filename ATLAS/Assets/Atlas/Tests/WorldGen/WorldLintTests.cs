using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using Atlas.World.Lint;
using NUnit.Framework;

namespace Atlas.WorldGen.Tests
{
    public class WorldLintTests
    {
        public static IEnumerable<TestCaseData> Cases()
        {
            foreach (uint seed in WorldGeneratorTests.Seeds)
            {
                yield return new TestCaseData(seed, "R45").SetName($"WorldLint_All_{seed}_R45");
                yield return new TestCaseData(seed, "R33").SetName($"WorldLint_All_{seed}_R33");
            }
        }

#if !ATLAS_HEADLESS
        // §9.5.6 "WorldLint passes for three seeds"; §11.1 WorldLint_All. WorldLint.Run reads back generated Mesh objects.
        [Test, TestCaseSource(nameof(Cases)), Timeout(600000), Category("Unity")]
        public void WorldLint_All(uint seed, string room)
        {
            var report = WorldLint.Run(seed, room == "R45" ? WorldGeneratorTests.R45 : WorldGeneratorTests.R33);
            TestContext.WriteLine(report.ToString());
            Assert.AreEqual(597, report.TilesLinted, "§11.1 597 tiles");
            Assert.AreEqual(0, report.Unreachable, "§3.4/§11.1 every tile reachable");
            Assert.LessOrEqual(report.MaxClosedEdges, 3, "no tile with 4 closed edges");
            Assert.LessOrEqual(report.WorstTowerDistance, 2.5, "§3.4 tower within 2.5");
            Assert.AreEqual(2.4529, report.WorstTowerDistance, 1e-3);
            Assert.AreEqual(0, report.TilesMissingWedges, "§3.4 four wedges");
            Assert.GreaterOrEqual(report.StartSpanPairs, 1, "§3.5 spanning object near the start");
            Assert.LessOrEqual(report.MaxDrawnTiles, 71, "§8.5 S2 row");
            Assert.LessOrEqual(report.MaxTileDraws, 150);
            Assert.LessOrEqual(report.MaxDraws, 210);
            Assert.LessOrEqual(report.MaxTriangles, 250000);
            Assert.AreEqual(0, report.CoincidentFaces, "§3.4 no coincident faces");
            Assert.AreEqual(0, report.StructuralConflicts);
            Assert.AreEqual(0, report.RegionMismatches, "§9.5.6 same region on two runs");
            Assert.AreEqual(0, report.ContentMismatches, "§9.5.6 same content on two runs");
            Assert.AreEqual(0, report.CorridorsMissingKerb, "§3.4 Corridor: kerbs on two opposite edges (only Home / feature exemptions)");
            Assert.AreEqual(4, report.RegionsPresent, "§10.1.4 four regions");
            Assert.AreEqual(report.WorldHash1, report.WorldHash2, "same seed same world hash");
            Assert.IsTrue(report.Passed, report.ToString());
        }
#endif

        static (WorldGenerator g, TileId n, int back) Pair()
        {
            var g = new WorldGenerator(1, WorldGeneratorTests.R45);
            var n = g.Graph.Neighbour(TileId.Zero, 0);
            return (g, n, g.BackEdge(TileId.Zero, 0));
        }

#if !ATLAS_HEADLESS
        // §3.4 "no coincident faces"
        [Test, Category("Unity")]
        public void WorldLint_DetectsCoincidentKerbs()
        {
            var (g, n, back) = Pair();
            var fc = new FaceCoincidence(g.Graph, WorldGeneratorTests.R45.L, new TileMeshCache(WorldGeneratorTests.R45.L));
            int both = fc.Count(TileId.Zero, 0, 1, n, back, 1 << back);
            int one = fc.Count(TileId.Zero, 0, 1, n, back, 0);
            int none = fc.Count(TileId.Zero, 0, 0, n, back, 0);
            TestContext.WriteLine($"both={both} one={one} none={none}");
            Assert.Greater(both, 0);
            Assert.AreEqual(0, one);
            Assert.AreEqual(0, none);
        }
#endif

        // §3.4 "a tower within rho = 2.5" (the check must be able to fail)
        [Test]
        public void WorldLint_DetectsMissingTower()
        {
            var g = new WorldGenerator(1, WorldGeneratorTests.R45);
            double worst = WorldLint.WorstTowerDistance(g.Graph, g.Ball(3), _ => false, 2.5);
            Assert.Greater(worst, 2.5);
            double ok = WorldLint.WorstTowerDistance(g.Graph, g.Ball(3), t => g.Content(t).Archetype == Archetype.Tower, 2.5);
            Assert.LessOrEqual(ok, 2.5);
        }

        // §3.4 / §11.1 "every tile reachable" (the check must be able to fail)
        [Test]
        public void WorldLint_DetectsUnreachable()
        {
            var g = new WorldGenerator(1, WorldGeneratorTests.R45);
            var n0 = g.Graph.Neighbour(TileId.Zero, 0);
            int Closed(TileId t)
            {
                if (t == n0) return 0xF;
                int m = 0;
                for (int e = 0; e < 4; e++) if (g.Graph.Neighbour(t, e) == n0) m |= 1 << e;
                return m;
            }
            Assert.GreaterOrEqual(WorldLint.CountUnreachable(g, g.Ball(6), 8, Closed), 1);
            Assert.AreEqual(0, WorldLint.CountUnreachable(g, g.Ball(6), 8, t => g.Content(t).ClosedMask));
        }

#if !ATLAS_HEADLESS
        // §9.9 rule 7: a budget number binds
        [Test, Timeout(600000), Category("Unity")]
        public void WorldLint_BudgetIsBinding()
        {
            var s = new WorldLintSettings { TriCap = 100000 };
            var report = WorldLint.Run(1, WorldGeneratorTests.R45, s);
            Assert.IsFalse(report.Passed);
            Assert.IsTrue(report.Failures.Exists(f => f.StartsWith("budget:") && f.Contains("triangles")), report.ToString());
        }
#endif
    }
}
