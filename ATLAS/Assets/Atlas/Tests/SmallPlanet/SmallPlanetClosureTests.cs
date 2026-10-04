using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using static Atlas.Spaces.SmallPlanet.Tests.SmallPlanetTestUtil;

namespace Atlas.Spaces.SmallPlanet.Tests
{
    // The walker of S4 is the app's kernel at q = 3 (HolonomyWalkerKernel over SphereWalker): these run it as the space sets it up.
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
                        double2 start = centre ? double2.zero : new double2(-s / 2, -s / 2);
                        var k = NewKernel(s, start);
                        double R = k.MetresPerUnit;
                        Assert.AreEqual(2 * s / Math.PI, R, 1e-12);
                        Assert.AreEqual(s, Math.PI / 2 * R, 1e-12);
                        Assert.AreEqual(Curvature.Spherical, k.Kappa);
                        int f0 = CubeTiling.FaceOf(k.Pose.Tile);
                        var u0 = k.Pose.Local;
                        double3 p0 = WorldPoint(f0, u0);
                        var legs = new[] { new double2(s, 0), new double2(0, left ? s : -s), new double2(-s, 0) };
                        var faces = new List<int> { f0 };
                        int crossings = 0;
                        var heads = new List<double2>(Feed(start, legs));
                        int[] legEndIdx = new int[3]; int acc = 0;
                        for (int i = 0; i < 3; i++) { acc += (int)Math.Ceiling(math.length(legs[i]) / 0.01); legEndIdx[i] = acc; }
                        for (int i = 0; i < heads.Count; i++)
                        {
                            crossings += k.Step(heads[i], true, i / 60.0).Crossings;
                            int face = CubeTiling.FaceOf(k.Pose.Tile);
                            if (faces[faces.Count - 1] != face) faces.Add(face);
                            if (i == legEndIdx[0] || i == legEndIdx[1])
                            {
                                double d = R * Geometry2.Distance(WorldPoint(k.Pose), p0, 1);
                                Assert.Greater(d, 0.5 * s, $"s={s} after leg {(i == legEndIdx[0] ? 1 : 2)} must not close");
                            }
                        }
                        double3 p1 = WorldPoint(k.Pose);
                        double closure = R * math.length(p1 - p0);
                        worst = Math.Max(worst, closure);
                        Assert.Less(closure, 1e-3, $"s={s} centre={centre} left={left}");
                        var m = math.mul(math.transpose((CubeTiling.Frame(f0) * u0).M), (CubeTiling.Frame(CubeTiling.FaceOf(k.Pose.Tile)) * k.Pose.Local).M);
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

        // T3. §3.2 table, A.4; §4.4 comfort floor on the rooms of the fixtures
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

            RoomModel square = Room(RoomFixtures.Sample3x3), ell = Room(RoomFixtures.SampleL3x4);
            Assert.AreEqual(SmallPlanetConstants.RadiusFromLapSide(square.LapSide), SmallPlanetCore.RadiusFor(square, false), 1e-6);   // RoomModel stores float32
            Assert.AreEqual(1.27324, SmallPlanetCore.RadiusFor(square, false), 1e-6);
            Assert.AreEqual(ell.Curvature.FloorR, SmallPlanetCore.RadiusFor(ell, false), 1e-6, "the comfort floor lifts a small room's planet");
            Assert.AreEqual(SmallPlanetConstants.RadiusFromLapSide(ell.LapSide), SmallPlanetCore.RadiusFor(ell, true), 1e-6, "strong: the loop-fit radius");
            Assert.AreEqual(2.0, SmallPlanetCore.RadiusFor(square, false, 2.0), 0.0, "planet.R is a lower bound");
            Assert.AreEqual(1.27324, SmallPlanetCore.RadiusFor(square, false, 0.5), 1e-6);
        }

        // T8. §4.6 and §4.9: the kernel's rules at q = 3
        [Test]
        public void SmallPlanet_WalkerRules()
        {
            var k = NewKernel(2.0, double2.zero);
            double R = k.MetresPerUnit;
            Assert.IsTrue(ExactEq(TileZeroPlacement.Registered(double2.zero, 1, R), k.Pose.Local), "registered at the start (§4.9)");
            Assert.AreEqual(0, CubeTiling.FaceOf(k.Pose.Tile));
            k.Step(double2.zero, true, 0.0);
            var u = k.Pose.Local;
            KernelStep step = k.Step(new double2(0.25, 0), true, 0.1);
            Assert.AreEqual(0, math.length(step.AppliedMetres));
            Assert.IsTrue(step.Rejected);
            Assert.IsTrue(ExactEq(u, k.Pose.Local), "0.25 m jump leaves U");
            k.Step(new double2(0.25, 0), false, 0.2);
            Assert.IsTrue(ExactEq(u, k.Pose.Local), "tracking not normal leaves U");
            k.Step(new double2(double.NaN, 0), true, 0.3);
            Assert.IsTrue(ExactEq(u, k.Pose.Local), "NaN counts as a jump");
            k.Step(new double2(0.25, 0), true, 0.4);
            Assert.IsTrue(ExactEq(u, k.Pose.Local), "stand-still keeps the registered pose");
            Assert.IsTrue(k.Still);
            step = k.Step(new double2(0.25 + 0.19, 0), true, 0.5);
            Assert.AreEqual(0.19, step.AppliedMetres.x, 1e-12);
            Assert.IsFalse(ExactEq(u, k.Pose.Local));
            Assert.IsFalse(k.Still, "1.9 m/s is not standing");
            k.ResetToFloor(double2.zero);
            Assert.IsTrue(ExactEq(u, k.Pose.Local), "placed again: registered again");

            var rng = new System.Random(7);
            double x = 0, z = 0;
            k.Step(double2.zero, true, 1.0);
            for (int i = 0; i < 10000; i++)
            {
                x += (rng.NextDouble() * 2 - 1) * 0.0141; z += (rng.NextDouble() * 2 - 1) * 0.0141;
                k.Step(new double2(x, z), true, 1.0 + (i + 1) / 60.0);
            }
            var m = k.Pose.Local.M;
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
            Assert.AreEqual(0.8 * Math.PI, SmallPlanetCore.ClampFog(3.0), 1e-15);
            Assert.AreEqual(0.5 * Math.PI, SmallPlanetCore.ClampFog(1.0), 1e-15);

            // planet.fogRho through the registry: the opaque end moves within [0.5 pi, 0.8 pi], the start follows 0.3 pi behind
            var reg = new ParamRegistry();
            var core = new SmallPlanetCore();
            core.Configure(Room(RoomFixtures.Sample3x3), reg);
            Assert.AreEqual(0.8 * Math.PI, core.FogOpaqueRad, 1e-15);
            Assert.AreEqual(0.5 * Math.PI, core.FogStartRad, 1e-12);
            var k = new HolonomyWalkerKernel();
            core.Enter(k, double2.zero);
            ParamEntry fog = reg.Find(SmallPlanetCore.ParamFogRho);
            Assert.AreEqual(SpaceIds.ParamGroup(SpaceId.SmallPlanet), fog.Group);
            fog.Set(3.0f);
            core.Tick(k.Pose, false, false, false);
            Assert.AreEqual(0.8 * Math.PI, core.FogOpaqueRad, 1e-15);
            fog.Set(1.0f);
            core.Tick(k.Pose, false, false, false);
            Assert.AreEqual(0.5 * Math.PI, core.FogOpaqueRad, 1e-6);   // a float32 parameter
            Assert.AreEqual(0.2 * Math.PI, core.FogStartRad, 1e-6);
            Assert.Less(core.FogOpaqueRad * core.R, SmallPlanetConstants.DiscardRad * k.MetresPerUnit, "opaque before the discard, in metres");
        }
    }
}
