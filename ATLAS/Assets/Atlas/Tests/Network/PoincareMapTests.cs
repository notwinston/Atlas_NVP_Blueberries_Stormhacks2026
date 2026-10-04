using System;
using Atlas.Blades.Network.Core;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Network.Tests
{
    public class PoincareMapTests
    {
        [Test]
        public void Project_EqualsPythonPoincare_1e9()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                NetworkModel m = NetworkTestEnv.Model(seed);
                var pts = NetworkTestEnv.Fixture("golden_poincare_" + seed + ".json")["points"];
                Assert.AreEqual(m.NodeCount, pts.Count);
                for (int i = 0; i < pts.Count; i++)
                {
                    double2 p = PoincareMap.Project(m.Radius(i), m.Theta(i));
                    Assert.AreEqual(pts[i][0].AsDouble(), p.x, 1e-9);
                    Assert.AreEqual(pts[i][1].AsDouble(), p.y, 1e-9);
                }
            }
        }

        // Contract section 9, C3: re-centre on every node of both fixtures; every pair whose two images stay within R of the new
        // centre (|w| <= tanh(R/2)) keeps its distance to 1e-9, both sides measured with PoincareMap.Distance. The all-pairs
        // maximum is logged (far points are only resolved to about 1e-7 in doubles).
        [Test]
        public void Mobius_PreservesPairwiseDistances_1e9()
        {
            foreach (int seed in NetworkModel.BundledSeeds)
            {
                NetworkModel m = NetworkTestEnv.Model(seed);
                int n = m.NodeCount;
                double limit = Math.Tanh(m.R / 2);
                var orig = new double2[n]; var origOm = new double[n];
                var pts = new double2[n]; var om = new double[n];
                PoincareMap.ProjectAll(m, double2.zero, orig, origOm);
                double maxNear = 0, maxAll = 0;
                for (int c = 0; c < n; c++)
                {
                    double2 centre = orig[c];
                    PoincareMap.ProjectAll(m, centre, pts, om);
                    Assert.AreEqual(0, math.length(pts[c]), 1e-12);
                    for (int i = 0; i < n; i++)
                        for (int j = i + 1; j < n; j++)
                        {
                            double before = PoincareMap.Distance(orig[i], origOm[i], orig[j], origOm[j]);
                            double after = PoincareMap.Distance(pts[i], om[i], pts[j], om[j]);
                            double diff = Math.Abs(before - after);
                            maxAll = Math.Max(maxAll, diff);
                            if (math.length(pts[i]) <= limit && math.length(pts[j]) <= limit)
                            {
                                maxNear = Math.Max(maxNear, diff);
                                Assert.AreEqual(before, after, 1e-9, "seed " + seed + " centre " + c + " pair " + i + "," + j);
                            }
                        }
                }
                TestContext.Out.WriteLine("seed " + seed + ": max change within R of the centre " + maxNear + ", all pairs " + maxAll);
            }
        }

        [Test]
        public void Mobius_InverseRoundTrips()
        {
            double2 a = new double2(0.3, -0.4), z = new double2(-0.5, 0.2);
            double2 back = PoincareMap.MobiusInverse(PoincareMap.Mobius(z, a), a);
            Assert.AreEqual(z.x, back.x, 1e-12); Assert.AreEqual(z.y, back.y, 1e-12);
        }

        [Test]
        public void EaseCentre_EndsAtTargetAndStaysOnGeodesic()
        {
            double2 from = new double2(0.2, 0.1), to = new double2(-0.6, 0.5);
            double2 c0 = PoincareMap.EaseCentre(from, to, 0), c1 = PoincareMap.EaseCentre(from, to, 1);
            Assert.AreEqual(from.x, c0.x, 1e-12); Assert.AreEqual(from.y, c0.y, 1e-12);
            Assert.AreEqual(to.x, c1.x, 1e-12); Assert.AreEqual(to.y, c1.y, 1e-12);
            double total = PoincareMap.Distance(from, to), prev = 0;
            for (int i = 1; i <= 10; i++)
            {
                double2 c = PoincareMap.EaseCentre(from, to, i / 10.0);
                double a = PoincareMap.Distance(from, c), b = PoincareMap.Distance(c, to);
                Assert.AreEqual(total, a + b, 1e-9);
                Assert.GreaterOrEqual(a, prev - 1e-12);
                prev = a;
            }
            Assert.AreEqual(from, PoincareMap.EaseCentre(from, from, 0.5));
        }

        [Test]
        public void Geodesic_EndsAtNodes_AndAllocatesNothing()
        {
            NetworkModel m = NetworkTestEnv.Model(7);
            var o = new double2[17];
            double2 c = new double2(0.1, -0.2);
            PoincareMap.Geodesic(m, 0, 6, c, 17, o, 0);
            double2 p0 = PoincareMap.Mobius(PoincareMap.Project(m.Radius(0), m.Theta(0)), c);
            double2 p1 = PoincareMap.Mobius(PoincareMap.Project(m.Radius(6), m.Theta(6)), c);
            Assert.AreEqual(p0.x, o[0].x, 1e-9); Assert.AreEqual(p0.y, o[0].y, 1e-9);
            Assert.AreEqual(p1.x, o[16].x, 1e-9); Assert.AreEqual(p1.y, o[16].y, 1e-9);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++) PoincareMap.Geodesic(m, 0, 6, c, 17, o, 0);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }
    }
}
