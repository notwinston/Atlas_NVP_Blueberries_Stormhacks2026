using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The curved tile graphs through ITileGraph alone: the rules of lane-b-kernel.md section 2.7 that
    /// FlatTileGraphTests holds for the flat grid, on {4,5}, {4,6} and {4,3}.</summary>
    [TestFixture]
    public class TileGraphContractTests
    {
        private static ITileGraph Make(int q)
        {
            if (q == 3) return new CubeTileGraph();
            return new TileGraph(q);
        }

        private static TileId Zero(int q)
        {
            return q == 3 ? CubeTiling.FaceId(0) : TileId.Origin;
        }

        /// <summary>Every tile within maxDistance edge steps, through Neighbour alone.</summary>
        private static List<TileId> Reach(ITileGraph graph, TileId from, int maxDistance)
        {
            var seen = new HashSet<TileId> { from };
            var all = new List<TileId> { from };
            int ringStart = 0;
            for (int n = 1; n <= maxDistance; n++)
            {
                int ringEnd = all.Count;
                for (int i = ringStart; i < ringEnd; i++)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        TileId next = graph.Neighbour(all[i], k);
                        if (seen.Add(next)) all.Add(next);
                    }
                }
                ringStart = ringEnd;
            }
            return all;
        }

        /// <summary>Steps taken to walk round a corner post, turning left, until the first tile returns.</summary>
        private static int StepsRoundCorner(ITileGraph graph, TileId first, int limit)
        {
            TileId tile = first;
            int edge = 0;
            for (int step = 1; step <= limit; step++)
            {
                int back = graph.BackEdge(tile, edge);
                tile = graph.Neighbour(tile, edge);
                if (tile == first) return step;
                edge = (back + 3) % 4;
            }
            return -1;
        }

        // Centre spacing 2r and circumradius r_c of lane-b-kernel.md section 1.2.
        [TestCase(3, 1.5707963267948966, 0.955316618124509)]
        [TestCase(5, 1.0612750619050357, 0.8424820814620078)]
        [TestCase(6, 1.3169578969248166, 1.1462158347805893)]
        public void TileGraph_Constants(int q, double spacing, double circumRadius)
        {
            ITileGraph graph = Make(q);
            Assert.That(graph.Q, Is.EqualTo(q));
            Assert.That(graph.Kappa, Is.EqualTo(Curvature.OfQ(q)));
            Assert.That(graph.CentreSpacing, Is.EqualTo(spacing).Within(1e-12));
            Assert.That(graph.CircumRadius, Is.EqualTo(circumRadius).Within(1e-12));
        }

        [TestCase(3, 2)]
        [TestCase(5, 4)]
        [TestCase(6, 4)]
        public void TileGraph_NeighbourRoundTrip(int q, int depth)
        {
            ITileGraph graph = Make(q);
            foreach (TileId t in Reach(graph, Zero(q), depth))
            {
                var neighbours = new HashSet<TileId>();
                for (int k = 0; k < 4; k++)
                {
                    TileId n = graph.Neighbour(t, k);
                    int back = graph.BackEdge(t, k);

                    Assert.That(n, Is.Not.EqualTo(t));
                    Assert.That(neighbours.Add(n), Is.True, "the four neighbours are distinct");
                    Assert.That(back, Is.InRange(0, 3));
                    Assert.That(graph.Neighbour(n, back), Is.EqualTo(t), "tile " + t + " edge " + k);

                    Iso2 step = graph.EdgeStep(t, k);
                    Assert.That(step.Kappa, Is.EqualTo(graph.Kappa));
                    Assert.That(KernelTestUtil.MaxAbsDifference(graph.EdgeStep(n, back), step.Inverse()),
                        Is.LessThan(1e-12));
                    Assert.That(Geometry2.Distance(step.Position, Geometry2.BasePoint, graph.Kappa),
                        Is.EqualTo(graph.CentreSpacing).Within(1e-12));
                    // The neighbour lies across edge k: at angle k * 90 degrees from the tile centre.
                    double2 towards = Geometry2.Log(step.Position, graph.Kappa);
                    double2 expected = graph.CentreSpacing * new double2(Math.Cos(k * Math.PI / 2.0), Math.Sin(k * Math.PI / 2.0));
                    Assert.That(math.length(towards - expected), Is.LessThan(1e-12));
                }
            }
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(6)]
        public void TileGraph_VertexClosure(int q)
        {
            ITileGraph graph = Make(q);
            foreach (TileId first in Reach(graph, Zero(q), 2))
            {
                // Exactly Q steps: the helper returns at the first return, so fewer are excluded.
                Assert.That(StepsRoundCorner(graph, first, 16), Is.EqualTo(q));
            }
        }

        [Test]
        public void TileGraph_BackEdgeGolden()
        {
            // kernel.md section 5.5: each neighbour of tile zero in {4,5} leads back through the edge it was entered by.
            var graph = new TileGraph(5);
            for (int k = 0; k < 4; k++)
            {
                Assert.That(graph.BackEdge(TileId.Origin, k), Is.EqualTo(k));
                Assert.That(graph.Neighbour(TileId.Origin, k), Is.EqualTo(TileId.Origin.Step(k)));
                Assert.That(graph.EdgeStep(TileId.Origin, k), Is.EqualTo(graph.StepMatrix(TileId.Origin, k)));
            }
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(6)]
        public void TileGraph_VisibleSetEntries(int q)
        {
            ITileGraph graph = Make(q);
            var list = new List<VisibleTile>(1024);
            graph.VisibleSet(Zero(q), 3.0, list);

            Assert.That(list[0].Tile, Is.EqualTo(Zero(q)));
            Assert.That(KernelTestUtil.MaxAbsDifference(list[0].Chain, Iso2.IdentityFor(graph.Kappa)), Is.EqualTo(0.0));
            Assert.That(list[0].GraphDistance, Is.EqualTo(0));

            var seen = new HashSet<TileId>();
            int previous = 0;
            foreach (VisibleTile v in list)
            {
                Assert.That(v.Tile, Is.EqualTo(v.Id));
                Assert.That(v.Chain, Is.EqualTo(v.F));
                Assert.That(seen.Add(v.Tile), Is.True);
                Assert.That(v.Chain.Kappa, Is.EqualTo(graph.Kappa));
                Assert.That(v.Chain.Defect(), Is.LessThan(1e-9));
                Assert.That(v.GraphDistance, Is.GreaterThanOrEqualTo(previous));
                previous = v.GraphDistance;
            }
            // The entries at graph distance 1 are the four neighbours, each with its edge step as the chain.
            for (int k = 0; k < 4; k++)
            {
                TileId n = graph.Neighbour(Zero(q), k);
                VisibleTile entry = list.Find(v => v.Tile == n);
                Assert.That(entry.GraphDistance, Is.EqualTo(1));
                Assert.That(KernelTestUtil.MaxAbsDifference(entry.Chain, graph.EdgeStep(Zero(q), k)), Is.LessThan(1e-12));
            }
        }

        [TestCase(3)]
        [TestCase(5)]
        [TestCase(6)]
        public void TileGraph_BadInput(int q)
        {
            ITileGraph graph = Make(q);
            TileId zero = Zero(q);
            var list = new List<VisibleTile>();
            foreach (int edge in new[] { 4, -1 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.Neighbour(zero, edge));
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.BackEdge(zero, edge));
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.EdgeStep(zero, edge));
            }
            // An id that is no tile of the tiling: "no tile" in every tiling, and tile zero's flat neighbour in the curved ones.
            var foreign = new List<TileId> { q == 3 ? new TileId(6, 0, 0, 0, 0, 0) : default(TileId) };
            if (q != 3) foreign.Add(FlatTileGraph.Cell(1, 0));
            foreach (TileId bad in foreign)
            {
                Assert.Throws<ArgumentException>(() => graph.Neighbour(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.BackEdge(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.EdgeStep(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.VisibleSet(bad, 1.0, list));
            }
        }
    }
}
