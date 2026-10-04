using System;
using NUnit.Framework;
using Unity.Mathematics;
using Atlas.Kernel;

namespace Atlas.Blades.Lung.Sky.Tests
{
    public class GreatCircleTests
    {
        static readonly double3 X = new double3(1, 0, 0), Y = new double3(0, 1, 0), Z = new double3(0, 0, 1);

        static double Gauss(System.Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

        // Gaussian-normalised triples, skipping |det| < 1e-3 and any side > 170 deg, until n are accepted.
        static System.Collections.Generic.List<double3[]> Triangles(int n, int seed)
        {
            var rng = new System.Random(seed); var list = new System.Collections.Generic.List<double3[]>();
            while (list.Count < n)
            {
                double3 a = math.normalize(new double3(Gauss(rng), Gauss(rng), Gauss(rng)));
                double3 b = math.normalize(new double3(Gauss(rng), Gauss(rng), Gauss(rng)));
                double3 c = math.normalize(new double3(Gauss(rng), Gauss(rng), Gauss(rng)));
                if (Math.Abs(math.dot(a, math.cross(b, c))) < 1e-3) continue;
                double lim = 170 * Math.PI / 180;
                if (SkyDirection.AngleRad(a, b) > lim || SkyDirection.AngleRad(b, c) > lim || SkyDirection.AngleRad(c, a) > lim) continue;
                list.Add(new[] { a, b, c });
            }
            return list;
        }

        [Test] // P2-B11 acceptance "Octant triangle gives 270 deg +- 0.5"
        public void B11_OctantTriangle_270()
        {
            double s = SphericalTriangle.AngleSumDeg(X, Y, Z);
            Assert.That(s, Is.InRange(269.5, 270.5)); Assert.AreEqual(270.0, s, 1e-9);
            Assert.AreEqual(Math.PI / 2, SphericalTriangle.AreaSr(X, Y, Z), 1e-12);
            Assert.AreEqual(Math.PI / 2, SphericalTriangle.VertexAngleRad(X, Y, Z), 1e-12);
            Assert.AreEqual(Math.PI / 2, SphericalTriangle.VertexAngleRad(Y, Z, X), 1e-12);
            Assert.AreEqual(Math.PI / 2, SphericalTriangle.VertexAngleRad(Z, X, Y), 1e-12);
        }

        [Test] // P2-B11 acceptance via the beat; MP §10.2.6 gc_check
        public void B11_OctantViaTwins_GcCheckEvent()
        {
            var ev = new FakeSkyEvents(); var p = new FakePresenter(); var reg = new Atlas.Link.ParamRegistry();
            var b = new NeedleSkyBeat(SkyMapsFactory.Fixture(), reg, ev, null, p);
            b.Enter();
            var iaz = reg.Find("lung.skyPickAz"); var iel = reg.Find("lung.skyPickEl"); var ipk = reg.Find("lung.skyPick");
            int n = 0;
            void Pick(float az, float el)
            {
                reg.Set(iaz, az); reg.Set(iel, el); reg.Set(ipk, (float)++n);
                b.Tick(SkyTestEnv.Frame(n, n * 0.01, 0.01f, float3.zero, quaternion.identity));
            }
            Pick(90, 0); Pick(0, 90);
            Assert.AreEqual(0, ev.Count("gc_check"));
            Pick(0, 0);
            Assert.AreEqual(1, ev.Count("gc_check"));
            int k = ev.Names.IndexOf("gc_check");
            Assert.AreEqual(3, ev.Count("sky_pick")); Assert.AreEqual(3, k, "gc_check follows the third sky_pick");
            StringAssert.Contains("angle_sum_deg", ev.Data[k]);
            Assert.AreEqual("{\"angle_sum_deg\":", ev.Data[k].Substring(0, 17));
            var d = GcCheckData.FromJson(Atlas.Link.AtlasJson.Parse(ev.Data[k]));
            Assert.That(d.angle_sum_deg, Is.InRange(269.5f, 270.5f));
            Assert.AreEqual(270.0f, b.Readout.AngleSumShownDeg);
            Assert.IsTrue(p.Last.ArcsOn);
        }

        [Test] // MP §10.2.2 beat g "Angle sum > 180 and equal to 180 + 180 area/pi within 0.5"
        public void B11_AngleSum_EqualsGirard_1000Random()
        {
            double max = 0;
            foreach (var t in Triangles(1000, 7))
            {
                double s = SphericalTriangle.AngleSumDeg(t[0], t[1], t[2]);
                double g = SphericalTriangle.GirardSumDeg(SphericalTriangle.AreaSr(t[0], t[1], t[2]));
                Assert.Greater(s, 180.0);
                max = Math.Max(max, Math.Abs(s - g));
            }
            TestContext.WriteLine("B11 max |sum - Girard| = " + max);
            Assert.Less(max, 0.5);
        }

        [Test] // Reuse of the kernel (MP App. A.2, kappa = +1)
        public void B11_VertexAngle_MatchesKernelLogMap()
        {
            var ts = Triangles(100, 7);
            foreach (var t in ts)
            {
                var m = Iso2.Boost(Geometry2.Log(t[0], +1), +1).Inverse();
                Assert.Less(math.length(math.mul(m.M, t[0]) - new double3(0, 0, 1)), 1e-12);
                double2 u = Geometry2.Log(math.mul(m.M, t[1]), +1), v = Geometry2.Log(math.mul(m.M, t[2]), +1);
                double ang = Math.Atan2(Math.Abs(u.x * v.y - u.y * v.x), math.dot(u, v));
                Assert.AreEqual(ang, SphericalTriangle.VertexAngleRad(t[0], t[1], t[2]), 1e-9);
            }
        }

        [Test] // P2-B11 Output "Three arcs"
        public void B11_ThreeArcs()
        {
            var c = GreatCircleCheck.From(X, Y, Z);
            Assert.AreEqual(3, c.Arcs.Length);
            var p = new[] { X, Y, Z };
            for (int k = 0; k < 3; k++)
            {
                Assert.AreEqual(0.0, math.length(c.Arcs[k].From - p[k]), 0); Assert.AreEqual(0.0, math.length(c.Arcs[k].To - p[(k + 1) % 3]), 0);
                Assert.AreEqual(Math.PI / 2, c.Arcs[k].LengthRad, 1e-12);
            }
            foreach (var t in Triangles(50, 3))
            {
                var cc = GreatCircleCheck.From(t[0], t[1], t[2]);
                for (int k = 0; k < 3; k++)
                    Assert.AreEqual(SkyDirection.AngleRad(t[k], t[(k + 1) % 3]), cc.Arcs[k].LengthRad, 1e-12);
            }
        }

        [Test] // Shader arc rule, CPU mirror
        public void B11_ArcDistance()
        {
            var a = new GreatCircleArc(X, Y);
            for (int k = 0; k <= 20; k++)
            {
                double th = k / 20.0 * Math.PI / 2;
                Assert.Less(a.DistanceRad(new double3(Math.Cos(th), Math.Sin(th), 0)), 1e-12);
            }
            double off = Math.PI / 180;
            var d = new double3(Math.Cos(0.7) * Math.Cos(off), Math.Sin(0.7) * Math.Cos(off), Math.Sin(off));
            Assert.AreEqual(off, a.DistanceRad(d), 1e-9);
            var beyond = new double3(Math.Cos(2.0), Math.Sin(2.0), 0);
            Assert.AreEqual(SkyDirection.AngleRad(beyond, Y), a.DistanceRad(beyond), 1e-12);
            var before = new double3(Math.Cos(-0.5), Math.Sin(-0.5), 0);
            Assert.AreEqual(SkyDirection.AngleRad(before, X), a.DistanceRad(before), 1e-12);
        }

        [Test] // Numerics
        public void B11_SmallTriangle_Stable()
        {
            double e = 0.1 * Math.PI / 180;
            var a = new double3(0, 0, 1);
            var b = SkyDirection.FromAzEl(0.1, 0);
            var c = SkyDirection.FromAzEl(0.05, 0.0866);
            double s = SphericalTriangle.AngleSumDeg(a, b, c);
            Assert.Greater(s, 180.0); Assert.Less(s, 180.001);
            Assert.Less(Math.Abs(s - SphericalTriangle.GirardSumDeg(SphericalTriangle.AreaSr(a, b, c))), 1e-6);
        }
    }
}
