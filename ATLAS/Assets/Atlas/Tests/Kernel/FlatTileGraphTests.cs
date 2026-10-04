using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The flat {4,4} grid as a tile graph. The graph-walking tests go through ITileGraph only,
    /// so the {4,5} graph can be run through the same bodies with its own expected numbers.</summary>
    [TestFixture]
    public class FlatTileGraphTests
    {
        private static readonly int[] FlatGrowth = { 1, 4, 8, 12, 16, 20, 24, 28, 32 };

        /// <summary>Breadth-first rings from a tile, through Neighbour alone.</summary>
        private static List<List<TileId>> Rings(ITileGraph graph, TileId from, int maxDistance)
        {
            var seen = new HashSet<TileId> { from };
            var rings = new List<List<TileId>> { new List<TileId> { from } };
            for (int n = 1; n <= maxDistance; n++)
            {
                var ring = new List<TileId>();
                foreach (TileId t in rings[n - 1])
                {
                    for (int k = 0; k < 4; k++)
                    {
                        TileId next = graph.Neighbour(t, k);
                        if (seen.Add(next)) ring.Add(next);
                    }
                }
                rings.Add(ring);
            }
            return rings;
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

        [Test]
        public void FlatTileGraph_NeighbourRoundTrip()
        {
            ITileGraph graph = new FlatTileGraph(1.0613);
            foreach (List<TileId> ring in Rings(graph, TileId.Origin, 6))
            {
                foreach (TileId t in ring)
                {
                    var neighbours = new HashSet<TileId>();
                    for (int k = 0; k < 4; k++)
                    {
                        TileId n = graph.Neighbour(t, k);
                        int back = graph.BackEdge(t, k);

                        Assert.That(n, Is.Not.EqualTo(t));
                        Assert.That(neighbours.Add(n), Is.True, "the four neighbours are distinct");
                        Assert.That(back, Is.EqualTo((k + 2) % 4));
                        Assert.That(graph.Neighbour(n, back), Is.EqualTo(t));

                        Iso2 step = graph.EdgeStep(t, k);
                        Assert.That(KernelTestUtil.MaxAbsDifference(graph.EdgeStep(n, back), step.Inverse()),
                            Is.LessThan(1e-12));
                        Assert.That(KernelMaps.Distance(step.Position, KernelMaps.BasePoint, graph.Kappa),
                            Is.EqualTo(graph.CentreSpacing).Within(1e-12));
                    }
                }
            }
        }

        [Test]
        public void FlatTileGraph_Growth()
        {
            ITileGraph graph = new FlatTileGraph(1.0);
            List<List<TileId>> rings = Rings(graph, TileId.Origin, FlatGrowth.Length - 1);
            var all = new HashSet<TileId>();
            for (int n = 0; n < FlatGrowth.Length; n++)
            {
                Assert.That(rings[n].Count, Is.EqualTo(FlatGrowth[n]), "tiles at graph distance " + n);
                foreach (TileId t in rings[n]) Assert.That(all.Add(t), Is.True, "no duplicate");
            }
        }

        [Test]
        public void FlatTileGraph_VertexClosure()
        {
            ITileGraph graph = new FlatTileGraph(2.0);
            Assert.That(graph.Q, Is.EqualTo(4));
            Assert.That(graph.Kappa, Is.EqualTo(Curvature.OfQ(graph.Q)));
            foreach (TileId first in new[] { TileId.Origin, FlatTileGraph.Cell(-7, 3), FlatTileGraph.Cell(1000000, -5) })
            {
                // Exactly Q steps: the helper returns at the first return, so 1, 2 and 3 are excluded.
                Assert.That(StepsRoundCorner(graph, first, 16), Is.EqualTo(graph.Q));
            }
        }

        [Test]
        public void FlatTileGraph_VisibleSet()
        {
            double[] ratios = { 0.0, 1.0, Math.Sqrt(2.0), 2.0, Math.Sqrt(5.0), 2.5, 2.0 * Math.Sqrt(2.0), 3.0, 4.0, 5.0 };
            int[] counts = { 1, 5, 9, 13, 21, 21, 25, 29, 49, 81 };
            var list = new List<VisibleTile>(1024);
            var again = new List<VisibleTile>(1024);

            foreach (double a in new[] { 1.0, 2.5 })
            {
                var graph = new FlatTileGraph(a);
                Assert.That(graph.CentreSpacing, Is.EqualTo(a));
                Assert.That(graph.CircumRadius, Is.EqualTo(a / Math.Sqrt(2.0)).Within(1e-15));

                foreach (TileId centre in new[] { TileId.Origin, FlatTileGraph.Cell(-7, 3) })
                {
                    Assert.That(FlatTileGraph.TryGetCell(centre, out long ci, out long cj), Is.True);
                    for (int r = 0; r < ratios.Length; r++)
                    {
                        double rho = a * ratios[r];
                        graph.VisibleSet(centre, rho, list);
                        Assert.That(list.Count, Is.EqualTo(counts[r]), "a = " + a + ", rho / a = " + ratios[r]);

                        Assert.That(list[0].Tile, Is.EqualTo(centre));
                        Assert.That(list[0].Chain, Is.EqualTo(Iso2.Identity));
                        Assert.That(list[0].GraphDistance, Is.EqualTo(0));

                        var seen = new HashSet<TileId>();
                        int previous = 0;
                        foreach (VisibleTile v in list)
                        {
                            Assert.That(FlatTileGraph.TryGetCell(v.Tile, out long i, out long j), Is.True);
                            long di = i - ci, dj = j - cj;
                            Assert.That(seen.Add(v.Tile), Is.True);
                            Assert.That(v.GraphDistance, Is.GreaterThanOrEqualTo(previous));
                            Assert.That(v.GraphDistance, Is.EqualTo(Math.Abs(di) + Math.Abs(dj)));
                            Assert.That(v.Chain.Position, Is.EqualTo(new double3(a * di, a * dj, 1.0)));
                            Assert.That(v.Chain.Kappa, Is.EqualTo(graph.Kappa));
                            Assert.That(KernelMaps.Distance(v.Chain.Position, KernelMaps.BasePoint, graph.Kappa),
                                Is.LessThanOrEqualTo(rho + KernelConstants.RadiusEpsilon));
                            previous = v.GraphDistance;
                        }

                        // The same list on a second call, into a list that held something else.
                        graph.VisibleSet(FlatTileGraph.Cell(99, 99), a * 5.0, again);
                        graph.VisibleSet(centre, rho, again);
                        Assert.That(again.Count, Is.EqualTo(list.Count));
                        for (int n = 0; n < list.Count; n++)
                        {
                            Assert.That(again[n].Tile, Is.EqualTo(list[n].Tile));
                            Assert.That(again[n].Chain, Is.EqualTo(list[n].Chain));
                            Assert.That(again[n].GraphDistance, Is.EqualTo(list[n].GraphDistance));
                        }
                    }
                }
            }

            // The figure section 4.6 quotes for a flat grid of the {4,5} spacing.
            new FlatTileGraph(1.0613).VisibleSet(TileId.Origin, 3.0, list);
            Assert.That(list.Count, Is.EqualTo(21));

            // The order within a ring is fixed: from +x round towards +z.
            new FlatTileGraph(1.0).VisibleSet(TileId.Origin, 1.0, list);
            Assert.That(list[1].Tile, Is.EqualTo(FlatTileGraph.Cell(1, 0)));
            Assert.That(list[2].Tile, Is.EqualTo(FlatTileGraph.Cell(0, 1)));
            Assert.That(list[3].Tile, Is.EqualTo(FlatTileGraph.Cell(-1, 0)));
            Assert.That(list[4].Tile, Is.EqualTo(FlatTileGraph.Cell(0, -1)));

            // Nothing is allocated once the list has the capacity.
            var graph5 = new FlatTileGraph(1.0);
            graph5.VisibleSet(TileId.Origin, 5.0, list);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int n = 0; n < 100; n++) graph5.VisibleSet(FlatTileGraph.Cell(n, -n), 5.0, list);
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
        }

        [Test]
        public void FlatTileGraph_Ids()
        {
            Assert.That(FlatTileGraph.Cell(0, 0), Is.EqualTo(TileId.Origin));
            TileId cell = FlatTileGraph.Cell(5, -9);
            var words = new long[6];
            cell.CopyTo(words);
            Assert.That(words, Is.EqualTo(new long[] { 5, 0, -9, 0, 1, 0 }));
            Assert.That(FlatTileGraph.TryGetCell(cell, out long i, out long j), Is.True);
            Assert.That(i, Is.EqualTo(5));
            Assert.That(j, Is.EqualTo(-9));

            var graph = new FlatTileGraph(1.0);
            Assert.That(graph.Neighbour(cell, 0), Is.EqualTo(FlatTileGraph.Cell(6, -9)));
            Assert.That(graph.Neighbour(cell, 1), Is.EqualTo(FlatTileGraph.Cell(5, -8)));
            Assert.That(graph.Neighbour(cell, 2), Is.EqualTo(FlatTileGraph.Cell(4, -9)));
            Assert.That(graph.Neighbour(cell, 3), Is.EqualTo(FlatTileGraph.Cell(5, -10)));
            Assert.That(graph.EdgeStep(cell, 1), Is.EqualTo(Iso2.Boost(new double2(0.0, 1.0), Curvature.Flat)));

            var list = new List<VisibleTile>();
            var foreign = new TileId(1, 0, 0, 0, 0, 1);   // the {4,5} tile across edge 0 of tile zero
            foreach (TileId bad in new[] { foreign, default(TileId) })
            {
                Assert.That(FlatTileGraph.TryGetCell(bad, out long bi, out long bj), Is.False);
                Assert.Throws<ArgumentException>(() => graph.Neighbour(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.BackEdge(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.EdgeStep(bad, 0));
                Assert.Throws<ArgumentException>(() => graph.VisibleSet(bad, 1.0, list));
            }
            foreach (int edge in new[] { 4, -1 })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.Neighbour(cell, edge));
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.BackEdge(cell, edge));
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.EdgeStep(cell, edge));
            }
            foreach (double rho in new[] { -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => graph.VisibleSet(cell, rho, list));
            }
            foreach (double spacing in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new FlatTileGraph(spacing));
            }
        }
    }
}
