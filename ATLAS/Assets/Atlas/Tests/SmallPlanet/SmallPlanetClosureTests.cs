using System;
using System.Collections.Generic;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    public class SmallPlanetClosureTests
    {
        // T1. Master Plan §9.5.6 P1-B08 "three steps of (pi/2)R with 90 deg turns close to 1 mm"; §11.1 Closure_345; A.4; A.2 holonomy on S^2.
        [Test]
        public void SmallPlanet_TriangleClosure()
        {
            double worst = 0;
            foreach (double s in LapSides)
                foreach (bool centre in new[] { true, false })
                    foreach (bool left in new[] { true, false })
                    {
                        var sp = NewSpace(s);
                        double R = sp.R;
                        Assert.AreEqual(2 * s / Math.PI, R, 1e-12);
                        Assert.AreEqual(s, Math.PI / 2 * R, 1e-12);
                        double2 start = centre ? double2.zero : new double2(-s / 2, -s / 2);
                        sp.Enter(SmallPlanetSpace.StartPose(start, R));
                        int f0 = sp.Walker.Face;
                        var u0 = sp.Walker.U;
                        double3 p0 = WorldPoint(f0, u0);
                        var legs = new[] { new double2(s, 0), new double2(0, left ? s : -s), new double2(-s, 0) };
                        var faces = new List<int> { f0 };
                        int crossings = 0;
                        var heads = new List<PlanetHead>(Feed(start, legs));
                        int[] legEndIdx = new int[3]; int acc = 0;
                        for (int i = 0; i < 3; i++) { acc += (int)Math.Ceiling(math.length(legs[i]) / 0.01); legEndIdx[i] = acc; }
                        for (int i = 0; i < heads.Count; i++)
                        {
                            sp.Tick(heads[i]);
                            crossings += sp.Walker.LastCrossings;
                            if (faces[faces.Count - 1] != sp.Walker.Face) faces.Add(sp.Walker.Face);
                            if (i == legEndIdx[0] || i == legEndIdx[1])
                            {
                                double d = R * Geometry2.Distance(WorldPoint(sp.Walker.Face, sp.Walker.U), p0, 1);
                                Assert.Greater(d, 0.5 * s, $"s={s} after leg {(i == legEndIdx[0] ? 1 : 2)} must not close");
                            }
                        }
                        double3 p1 = WorldPoint(sp.Walker.Face, sp.Walker.U);
                        double closure = R * math.length(p1 - p0);
                        worst = Math.Max(worst, closure);
                        Assert.Less(closure, 1e-3, $"s={s} centre={centre} left={left}");
                        var m = math.mul(math.transpose((CubeTiling.Frame(f0) * u0).M), (CubeTiling.Frame(sp.Walker.Face) * sp.Walker.U).M);
                        var expect = Iso2.Rot((left ? 1 : -1) * Math.PI / 2).M;
                        Assert.Less(MaxAbsDiff(m, expect), 1e-9, $"holonomy s={s} centre={centre} left={left}");
                        if (centre)
                        {
                            Assert.AreEqual(3, crossings, $"crossings s={s} left={left}");
                            CollectionAssert.AreEqual(left ? new[] { 0, 1, 2, 0 } : new[] { 0, 1, 4, 0 }, faces);
                        }
                    }
            TestContext.WriteLine($"T1 worst closure {worst:E3} m over {LapSides.Length * 4} cases");
        }

        // T3. §3.2 table, A.4
        [Test]
        public void SmallPlanet_RadiusFromLapSide()
        {
            double[] s = { 1.5, 2.0, 3.0, 3.2 }, r = { 0.95, 1.27, 1.91, 2.04 };
            for (int i = 0; i < s.Length; i++)
            {
                double R = SmallPlanetConstants.RadiusFromLapSide(s[i]);
                TestContext.WriteLine($"s={s[i]} R={R:F4}");
                Assert.AreEqual(r[i], R, 0.005);
                Assert.AreEqual(2 * s[i] / Math.PI, R, 1e-12);
            }
            foreach (double bad in new[] { 0, -1, double.NaN, double.PositiveInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() => SmallPlanetConstants.RadiusFromLapSide(bad));
        }

        static PlanetHead H(double x, double z, bool ok = true) => new PlanetHead(new double3(x, 1.6, z), ok);

        // T8. §4.6 and §4.9
        [Test]
        public void SmallPlanet_WalkerRules()
        {
            var sp = NewSpace(2.0);
            Assert.Throws<InvalidOperationException>(() => sp.Tick(H(0, 0)));
            sp.Enter(SmallPlanetSpace.StartPose(double2.zero, sp.R));
            Assert.Throws<InvalidOperationException>(() => sp.Configure(new SmallPlanetRoom(2)));
            sp.Tick(H(0, 0));
            var u = sp.Walker.U;
            sp.Tick(H(0.25, 0));
            Assert.AreEqual(0, math.length(sp.Walker.LastApplied));
            Assert.IsTrue(ExactEq(u, sp.Walker.U), "0.25 m jump leaves U");
            Assert.IsTrue(sp.Registered);
            sp.Tick(H(0.25, 0, false));
            Assert.IsTrue(ExactEq(u, sp.Walker.U), "tracking not normal leaves U");
            sp.Tick(H(double.NaN, 0));
            Assert.IsTrue(ExactEq(u, sp.Walker.U), "NaN counts as a jump");
            sp.Tick(H(0.25, 0));
            Assert.IsTrue(sp.Registered, "stand-still keeps Registered");
            sp.Tick(H(0.25 + 0.19, 0));
            Assert.AreEqual(0.19, sp.Walker.LastApplied.x, 1e-12);
            Assert.IsFalse(ExactEq(u, sp.Walker.U));
            Assert.IsFalse(sp.Registered);
            sp.Enter(SmallPlanetSpace.StartPose(double2.zero, sp.R));
            Assert.IsTrue(sp.Registered);

            var rng = new System.Random(7);
            double x = 0, z = 0;
            sp.Tick(H(0, 0));
            for (int i = 0; i < 10000; i++)
            {
                x += (rng.NextDouble() * 2 - 1) * 0.0141; z += (rng.NextDouble() * 2 - 1) * 0.0141;
                sp.Tick(H(x, z));
            }
            var m = sp.Walker.U.M;
            double err = math.cmax(math.abs(math.mul(math.transpose(m), m).c0 - new double3(1, 0, 0)));
            var utu = math.mul(math.transpose(m), m);
            err = math.max(err, math.max(math.cmax(math.abs(utu.c1 - new double3(0, 1, 0))), math.cmax(math.abs(utu.c2 - new double3(0, 0, 1)))));
            TestContext.WriteLine($"T8 orthonormality after 10000 steps {err:E3}");
            Assert.Less(err, 1e-12);
        }

        // T9. §9.5.6 "fog opaque at 0.8 pi, fragments beyond 2.6 rad discarded"; §10.2
        [Test]
        public void SmallPlanet_FogAndDiscard()
        {
            Assert.AreEqual(2.6, SmallPlanetConstants.DiscardRad);
            Assert.AreEqual(0.8 * Math.PI, SmallPlanetConstants.FogOpaqueRad, 1e-15);
            Assert.AreEqual(0.5 * Math.PI, SmallPlanetConstants.FogStartRad, 1e-15);
            Assert.Less(SmallPlanetConstants.FogOpaqueRad, SmallPlanetConstants.DiscardRad);
            float st = (float)(0.5 * Math.PI), op = (float)(0.8 * Math.PI);
            Assert.AreEqual(0f, SmallPlanetShading.Fog(st, st, op));
            Assert.AreEqual(0f, SmallPlanetShading.Fog(0.1f, st, op));
            Assert.AreEqual(1f, SmallPlanetShading.Fog(op, st, op), 1e-6);
            Assert.AreEqual(1f, SmallPlanetShading.Fog(3f, st, op));
            Assert.AreEqual(0.5f, SmallPlanetShading.Fog((float)(0.65 * Math.PI), st, op), 1e-5);
            Assert.IsFalse(SmallPlanetShading.Discard(2.6f));
            Assert.IsTrue(SmallPlanetShading.Discard(2.6001f));
            var sp = new SmallPlanetSpace();
            Assert.AreEqual(0.8 * Math.PI, sp.FogOpaqueRad, 1e-15);
            sp.FogOpaqueRad = 3.0;
            Assert.AreEqual(0.8 * Math.PI, sp.FogOpaqueRad, 1e-15);
            sp.FogOpaqueRad = 1.0;
            Assert.AreEqual(0.5 * Math.PI, sp.FogOpaqueRad, 1e-15);
            Assert.AreEqual(0.2 * Math.PI, sp.FogStartRad, 1e-12);
        }
    }
}
