using System;
using Atlas.Blades.Network.Core;
using Atlas.Blades.Network.Walk;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Tests
{
    public class NetworkBeaconTests
    {
        static double Wrap(double a) { while (a > Math.PI) a -= 2 * Math.PI; while (a < -Math.PI) a += 2 * Math.PI; return a; }

        [Test]
        public void Bearing_Within1DegreeOfLogMap()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var graph = new TileGraph(5);
            var placement = new NetworkTilePlacement(graph, model);
            var beacon = new NetworkBeacon(graph, model, placement);
            double worst = 0; int checkedPairs = 0;
            for (int a = 0; a < model.NodeCount; a++)
            {
                if (model.NeighbourCount(a) < 2) continue;
                WalkerPose pose = beacon.PoseAt(a);
                // Independent log map: the frame whose origin is P_a, built from the point alone (no tile chain).
                double3 pa = model.HyperboloidPoint(a);
                Iso2 plain = Iso2.Boost(Geometry2.Log(pa, -1), -1);
                double refBearing0 = 0, bearing0 = 0;
                for (int k = 0; k < model.NeighbourCount(a); k++)
                {
                    int b = model.Neighbour(a, k);
                    double bearing, dist;
                    Assert.IsTrue(beacon.TryNeighbourBearing(in pose, a, k, out bearing, out dist));
                    Assert.AreEqual(model.Distance(a, b), dist, 1e-6 * Math.Max(1, dist), "distance " + a + "->" + b);
                    double2 v = NetworkBeacon.LogVector(plain, model.HyperboloidPoint(b));
                    double refBearing = NetworkBeacon.BearingOf(v);
                    if (k == 0) { refBearing0 = refBearing; bearing0 = bearing; continue; }
                    // the two frames differ by a fixed turn (holonomy of the tile chain): compare angles between neighbours
                    double err = Math.Abs(Wrap((bearing - bearing0) - (refBearing - refBearing0)));
                    worst = Math.Max(worst, err); checkedPairs++;
                }
            }
            Assert.Greater(checkedPairs, 100);
            Assert.LessOrEqual(worst * 180 / Math.PI, 1.0, "worst bearing error in degrees");
        }

        [Test]
        public void Bearing_ReachesNeighbourAlongTheGeodesic()
        {
            NetworkModel model = NetworkTestEnv.Model(7);
            var graph = new TileGraph(5);
            var beacon = new NetworkBeacon(graph, model, new NetworkTilePlacement(graph, model));
            for (int a = 0; a < model.NodeCount; a += 7)
            {
                WalkerPose pose = beacon.PoseAt(a);
                Iso2 g;
                Assert.IsTrue(beacon.TryWalkerFrame(in pose, out g));
                for (int k = 0; k < model.NeighbourCount(a); k++)
                {
                    int b = model.Neighbour(a, k);
                    double2 v = NetworkBeacon.LogVector(g, model.HyperboloidPoint(b));
                    double3 back = g.Apply(Geometry2.Exp(v, -1));
                    double3 pb = model.HyperboloidPoint(b);
                    Assert.AreEqual(0.0, math.length(back - pb) / math.length(pb), 1e-6, a + "->" + b);
                }
            }
        }
    }
}
