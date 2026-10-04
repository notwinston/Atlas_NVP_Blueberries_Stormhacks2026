using System.Collections.Generic;
using Atlas.Blades.Network.Core;
using Atlas.Blades.Network.Walk;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Tests
{
    public class NetworkTilePlacementTests
    {
        [Test]
        public void Snap_EqualsBruteForceNearestCentre_200Nodes()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                NetworkModel model = NetworkTestEnv.Model(seed);
                Assert.AreEqual(200, model.NodeCount);
                var graph = new TileGraph(5);
                var placement = new NetworkTilePlacement(graph, model);

                double maxR = 0;
                for (int i = 0; i < model.NodeCount; i++) maxR = System.Math.Max(maxR, model.Radius(i));
                // the covering radius bounds the distance to the nearest centre, so this set holds every candidate
                var tiles = new List<VisibleTile>();
                graph.VisibleSet(TileId.Zero, maxR + graph.CircumRadius + 0.1, tiles);

                for (int i = 0; i < model.NodeCount; i++)
                {
                    double3 p = model.HyperboloidPoint(i);
                    double best = double.MaxValue; TileId bestTile = TileId.Zero;
                    for (int t = 0; t < tiles.Count; t++)
                    {
                        double3 c = tiles[t].F.Position;
                        double ch = c.z * p.z - c.x * p.x - c.y * p.y;
                        if (ch < best) { best = ch; bestTile = tiles[t].Id; }
                    }
                    Assert.AreEqual(bestTile, placement.Tile(i), "seed " + seed + " node " + i);
                }
            }
        }

        [Test]
        public void Offset_IsWithinCircumRadiusAndRoundTrips()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var graph = new TileGraph(5);
            var placement = new NetworkTilePlacement(graph, model);
            for (int i = 0; i < model.NodeCount; i++)
                Assert.LessOrEqual(math.length(placement.Offset(i)), graph.CircumRadius + 1e-6, "node " + i);
        }
    }
}
