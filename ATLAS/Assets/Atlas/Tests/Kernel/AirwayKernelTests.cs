using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;

namespace Atlas.Kernel.Tests
{
    // P2-B03 kernel additions: HolonomyRig.Reaim, WalkerHeading, TileGraphQueries (G6 §4.0).
    public class AirwayKernelTests
    {
        static double Wrap(double a) { a = Math.IEEERemainder(a, 2 * Math.PI); return a; }

        static HolonomyRig RigAt(Iso2 u, double kappa, double l)
        {
            var rig = new HolonomyRig((TileId t, int e, out Iso2 g) => { g = Iso2.Identity; return t; });
            rig.Reset(new WalkerPose { Tile = TileId.Zero, Local = u }, kappa, l);
            return rig;
        }

        // K1: Reaim keeps the position and rotates the heading by alpha (MP §10.2.3 step 4; MP §0.5 rule 3 exception).
        [Test]
        public void Rig_Reaim_KeepsPosition_RotatesHeading()
        {
            var r = new Random(20261003);
            for (int kappa = -1; kappa <= 0; kappa++)
                for (int i = 0; i < 1000; i++)
                {
                    double2 t = new double2(r.NextDouble() * 1.8 - 0.9, r.NextDouble() * 1.8 - 0.9);
                    if (math.length(t) > 0.9) t = t / math.length(t) * 0.9;
                    double th = (r.NextDouble() * 2 - 1) * Math.PI, al = (r.NextDouble() * 2 - 1) * Math.PI;
                    var u = Iso2.Boost(t, kappa) * Iso2.Rot(th);
                    var rig = RigAt(u, kappa, 1.7);
                    long n0 = rig.Reaims; var p0 = rig.Pose.Local;
                    rig.Reaim(al);
                    var p1 = rig.Pose.Local;
                    Assert.Less(math.length(p1.M.c2 - p0.M.c2), 1e-12, "position");
                    Assert.AreEqual(n0 + 1, rig.Reaims);
                    Assert.AreEqual(p0.Kappa, p1.Kappa);
                    Assert.AreEqual(rig.Kappa, p1.Kappa);
                    if (kappa < 0) Assert.Less(Math.Abs(Wrap(WalkerHeading.Of(p1) - WalkerHeading.Of(p0) - al)), 1e-12, "heading");
                }
        }

        // K2: WalkerHeading inverts U = Boost(t)*Rot(theta) (MP A.2).
        [Test]
        public void WalkerHeading_InvertsDecomposition()
        {
            var r = new Random(7);
            for (int i = 0; i < 1000; i++)
            {
                double2 t = new double2(r.NextDouble() * 1.8 - 0.9, r.NextDouble() * 1.8 - 0.9);
                double th = (r.NextDouble() * 2 - 1) * Math.PI;
                Assert.Less(Math.Abs(Wrap(WalkerHeading.Of(Iso2.Boost(t, -1) * Iso2.Rot(th)) - th)), 1e-12);
            }
        }

        // K3: EdgeTo / TryCommonNeighbour on the depth-5 ball (MP §10.2.3; kernel.md §5.3).
        [Test]
        public void TileGraphQueries_EdgeTo_CommonNeighbour()
        {
            var g = new TileGraph(5);
            var seen = new HashSet<TileId> { TileId.Zero };
            var frontier = new List<TileId> { TileId.Zero };
            var all = new List<TileId> { TileId.Zero };
            for (int d = 0; d < 5; d++)
            {
                var next = new List<TileId>();
                foreach (var t in frontier)
                    for (int k = 0; k < 4; k++) { var n = g.Neighbour(t, k); if (seen.Add(n)) { next.Add(n); all.Add(n); } }
                frontier = next;
            }
            Assert.Greater(all.Count, 100);
            foreach (var t in all)
                for (int k = 0; k < 4; k++)
                {
                    var m = g.Neighbour(t, k);
                    Assert.AreEqual(k, g.EdgeTo(t, m));
                    int back = g.EdgeTo(m, t); Assert.GreaterOrEqual(back, 0);
                    var straight = g.Neighbour(m, (back + 2) & 3);
                    Assert.AreEqual(-1, g.EdgeTo(t, straight));
                    Assert.IsTrue(g.TryCommonNeighbour(t, straight, out var mid)); Assert.AreEqual(m, mid);
                    var turn = g.Neighbour(m, (back + 1) & 3);
                    Assert.IsTrue(g.TryCommonNeighbour(t, turn, out mid), "turn"); Assert.AreEqual(m, mid);
                    Assert.IsFalse(g.TryCommonNeighbour(t, t, out _));
                }
        }
    }
}
