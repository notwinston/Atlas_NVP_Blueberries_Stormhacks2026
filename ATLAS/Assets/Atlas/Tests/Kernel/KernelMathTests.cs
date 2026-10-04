using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;

namespace Atlas.Kernel.Tests
{
    public class KernelMathTests
    {
        const int Seed = 20261003;
        static readonly double3x3 Eta = new double3x3(1, 0, 0, 0, 1, 0, 0, 0, -1);
        static readonly double Phi = (1 + Math.Sqrt(5)) / 2;

        static double Fro(double3x3 a) =>
            math.sqrt(math.dot(a.c0, a.c0) + math.dot(a.c1, a.c1) + math.dot(a.c2, a.c2));
        static double3x3 G(double k) => k < 0 ? Eta : double3x3.identity;
        static double Angle(double2 v, double2 w) =>
            Math.Atan2(Math.Abs(v.x * w.y - v.y * w.x), math.dot(v, w));
        static double BlockErr(double3x3 m)
        {
            var B = new double2x2(m.c0.xy, m.c1.xy);
            var d = math.mul(math.transpose(B), B) - double2x2.identity;
            return math.sqrt(math.dot(d.c0, d.c0) + math.dot(d.c1, d.c1));
        }
        static double Max3(double3 v) => math.max(math.abs(v.x), math.max(math.abs(v.y), math.abs(v.z)));
        static double U(Random r, double a, double b) => a + (b - a) * r.NextDouble();
        static double2 Dir(Random r, double mag) { double th = U(r, -Math.PI, Math.PI); return mag * new double2(Math.Cos(th), Math.Sin(th)); }
        static double Dev(Iso2 x, double k) // ||M^T G M - G||
        {
            var g = G(k);
            return Fro(math.mul(math.transpose(x.M), math.mul(g, x.M)) - g);
        }

        // Master Plan §9.5.6 P1-B01 "exp∘log = identity to 1e-12"; §11.1 Kernel_ExpLog.
        [TestCase(-1.0)] [TestCase(0.0)] [TestCase(1.0)]
        public void Kernel_ExpLog(double k)
        {
            var rnd = new Random(Seed);
            double rmax = k > 0 ? 3.0 : 4.0;
            var radii = new List<double> { 0, 1e-9, rmax };
            for (int i = 0; i < 1000; i++) radii.Add(rmax * rnd.NextDouble());
            double e1 = 0, e2 = 0, e3 = 0, e4 = 0;
            var o = Geometry2.BasePoint;
            foreach (var r in radii)
            {
                var t = Dir(rnd, r);
                var p = Geometry2.Exp(t, k);
                e1 = math.max(e1, Max3(Geometry2.Exp(Geometry2.Log(p, k), k) - p));
                e2 = math.max(e2, math.cmax(math.abs(Geometry2.Log(p, k) - t)));
                e3 = math.max(e3, Math.Abs(Geometry2.Distance(o, p, k) - math.length(t)));
                foreach (var L in new[] { 1.4, 1.9, 3.0 })
                {
                    var pf = (double3)(float3)p;
                    e4 = math.max(e4, math.cmax(math.abs(Geometry2.Log(pf, k) * L - t * L)));
                }
            }
            TestContext.WriteLine($"k={k}: exp(log p)-p {e1:E2}; log(exp t)-t {e2:E2}; dist {e3:E2}; float-cast {e4:E2} m");
            Assert.Less(e1, 1e-12); Assert.Less(e2, 1e-12); Assert.Less(e3, 1e-12);
            Assert.Less(e4, 1e-3);
        }

        static Iso2 RandomIso(Random rnd, double k, double dmax) =>
            Iso2.Rot(U(rnd, -Math.PI, Math.PI)) * Iso2.Boost(Dir(rnd, U(rnd, 0, dmax)), k) * Iso2.Rot(U(rnd, -Math.PI, Math.PI));

        // §11.1 Kernel_EtaOrthonormal; row "η-Gram–Schmidt".
        [TestCase(-1.0)] [TestCase(1.0)] [TestCase(0.0)]
        public void Kernel_EtaOrthonormal(double k)
        {
            var rnd = new Random(Seed);
            double a = 0;
            var gens = new List<Iso2> { Iso2.Rot(Math.PI / 2) };
            if (k < 0) gens.Add(Iso2.Boost(new double2(Math.Acosh(Phi), 0), -1)); // cosh 2r = phi
            for (int i = 0; i < 1000; i++)
            {
                gens.Add(Iso2.Boost(Dir(rnd, U(rnd, 0, 2)), k));
                gens.Add(Iso2.Rot(U(rnd, -Math.PI, Math.PI)));
            }
            if (k != 0)
                foreach (var g in gens) a = math.max(a, Dev(g, k));
            else
                foreach (var g in gens) a = math.max(a, BlockErr(g.M));
            TestContext.WriteLine($"k={k}: generators max {a:E2}");
            Assert.Less(a, 1e-12);

            double b = 0, drift = 0; double tmaxDelta = 0;
            for (int i = 0; i < 1000; i++)
            {
                var m = Iso2.Identity;
                for (int j = 0; j < 3; j++)
                    m = m * (Iso2.Rot(U(rnd, -Math.PI, Math.PI)) * Iso2.Boost(Dir(rnd, U(rnd, 0, 2.0 / 3)), k));
                var M = m.M;
                var noise = new double3x3(
                    U(rnd, -1e-6, 1e-6), U(rnd, -1e-6, 1e-6), U(rnd, -1e-6, 1e-6),
                    U(rnd, -1e-6, 1e-6), U(rnd, -1e-6, 1e-6), U(rnd, -1e-6, 1e-6),
                    k == 0 ? 0 : U(rnd, -1e-6, 1e-6), k == 0 ? 0 : U(rnd, -1e-6, 1e-6), k == 0 ? 0 : U(rnd, -1e-6, 1e-6));
                var noisy = M + noise;
                var x = new Iso2(noisy, k).Orthonormalised();
                drift = math.max(drift, Fro(x.M - M));
                if (k != 0) b = math.max(b, Dev(x, k));
                else
                {
                    var X = x.M;
                    b = math.max(b, BlockErr(X));
                    Assert.AreEqual(0.0, X.c0.z); Assert.AreEqual(0.0, X.c1.z); Assert.AreEqual(1.0, X.c2.z);
                    Assert.AreEqual(noisy.c2.x, X.c2.x); Assert.AreEqual(noisy.c2.y, X.c2.y);
                }
            }
            TestContext.WriteLine($"k={k}: renormalised max {b:E2}; distance to clean {drift:E2}");
            Assert.Less(b, 1e-12);
            Assert.Less(drift, 1e-4);
        }

        // §11.1 Kernel_Inverse; row "inverse as ηMᵀη".
        [TestCase(-1.0)] [TestCase(0.0)] [TestCase(1.0)]
        public void Kernel_Inverse(double k)
        {
            var rnd = new Random(Seed);
            double worst = 0;
            for (int i = 0; i < 1000; i++)
            {
                var m = RandomIso(rnd, k, 2);
                var inv = m.Inverse();
                worst = math.max(worst, math.max(Fro(math.mul(m.M, inv.M) - double3x3.identity), Fro(math.mul(inv.M, m.M) - double3x3.identity)));
                if (k < 0) Assert.AreEqual(math.mul(Eta, math.mul(math.transpose(m.M), Eta)), inv.M);
                else if (k > 0) Assert.AreEqual(math.transpose(m.M), inv.M);
                else Assert.Less(Fro(inv.M - math.inverse(m.M)), 1e-12);
            }
            TestContext.WriteLine($"k={k}: M*Inv-I max {worst:E2}");
            Assert.Less(worst, 1e-12);
        }

        // Row "holonomy of a geodesic triangle equals its angle defect to 1e-9"; §11.1 Holonomy_Triangle (A.2).
        [TestCase(-1.0)] [TestCase(1.0)] [TestCase(0.0)]
        public void Holonomy_Triangle(double k)
        {
            var rnd = new Random(Seed);
            double worst = 0; int n = 0;
            double hi = k > 0 ? 1.0 : 2.0;
            var o = Geometry2.BasePoint;
            while (n < 100)
            {
                var b = Dir(rnd, U(rnd, 0.2, hi)); var c = Dir(rnd, U(rnd, 0.2, hi));
                double cross = b.x * c.y - b.y * c.x;
                if (Math.Abs(cross) / (math.length(b) * math.length(c)) < 0.1) continue;
                n++;
                double orient = Math.Sign(cross);
                var B = Geometry2.Exp(b, k); var C = Geometry2.Exp(c, k);
                var B1 = Iso2.Boost(b, k);
                var v2 = Geometry2.Log(Apply(B1.Inverse(), C), k);
                var P = B1 * Iso2.Boost(v2, k);
                var v3 = Geometry2.Log(Apply(P.Inverse(), o), k);
                var Uh = P * Iso2.Boost(v3, k);
                double alpha = Math.Atan2(Uh.M.c0.y, Uh.M.c0.x);
                double sum = Angle(b, c) + Angle(Geometry2.Log(Apply(B1.Inverse(), o), k), v2) + Angle(Geometry2.Log(Apply(P.Inverse(), B), k), v3);
                double err = Math.Abs(alpha - orient * (sum - Math.PI));
                worst = math.max(worst, err);
                worst = math.max(worst, Max3(Uh.M.c2 - o));
                if (k != 0) Assert.AreEqual(Math.Sign(alpha), k * orient);
                else { Assert.Less(Math.Abs(alpha), 1e-9); Assert.Less(Math.Abs(sum - Math.PI), 1e-9); }
            }
            TestContext.WriteLine($"k={k}: holonomy vs angle defect max {worst:E2}");
            Assert.Less(worst, 1e-9);
        }

        static double3 Apply(Iso2 m, double3 p) => math.mul(m.M, p);

        // Row "five steps of 1.0613 with 90° turns ... three of π/2 on the sphere, four flat"; §11.1 Closure_345 (A.3, A.4).
        [TestCase(-1.0, 5)] [TestCase(1.0, 3)] [TestCase(0.0, 4)] [TestCase(-2.0, 4)]
        public void Closure_345(double kk, int n)
        {
            double k = kk == -2 ? 0 : kk;
            double s = kk < -1.5 ? 0.37 : k < 0 ? 2 * Math.Asinh(Math.Sqrt(Math.Cos(2 * Math.PI / 5))) : k > 0 ? Math.PI / 2 : 1.0;
            var g = Iso2.Boost(new double2(s, 0), k) * Iso2.Rot(Math.PI / 2);
            var acc = Iso2.Identity;
            double minOpen = double.MaxValue;
            for (int i = 1; i <= n; i++)
            {
                acc = acc * g;
                if (i < n) minOpen = math.min(minOpen, Fro(acc.M - double3x3.identity));
            }
            double closed = Fro(acc.M - double3x3.identity);
            TestContext.WriteLine($"k={k} s={s:F10}: |g^{n}-I| {closed:E2}; min over k<n {minOpen:E2}");
            Assert.Less(closed, 1e-12);
            Assert.Greater(minOpen, 0.5);
            if (k < 0)
            {
                Assert.Less(Math.Abs(s - 1.0613), 5e-5);
                Assert.Less(Math.Abs(Math.Cosh(s) - Phi), 1e-12);
            }
        }

        // Row "‖UᵀηU − η‖ < 1e-12 after 100,000 random boosts"; §11.1 Lorentz_Drift (A.3, A.5, §4.6).
        [Test]
        public void Lorentz_Drift()
        {
            const double L = 1.885;
            double r = Math.Acosh(Math.Cos(Math.PI / 5) / Math.Sin(Math.PI / 4));
            double h = Math.Tanh(r);
            var S = new Iso2[4];
            for (int e = 0; e < 4; e++)
                S[e] = Iso2.Rot(e * Math.PI / 2) * Iso2.Boost(new double2(2 * r, 0), -1) * Iso2.Rot(Math.PI);
            double Run(bool renorm, out double final, out double maxEntry, out int crossings)
            {
                var rnd = new Random(Seed);
                var u = Iso2.Identity; double mx = 0; maxEntry = 0; crossings = 0; final = 0;
                for (int i = 0; i < 100000; i++)
                {
                    var d = Dir(rnd, 0.2 * rnd.NextDouble() / L);
                    u = u * Iso2.Boost(d, -1);
                    if (renorm) u = u.Orthonormalised();
                    for (int j = 0; j < 4; j++)
                    {
                        var kx = u.M.c2.x / u.M.c2.z; var kz = u.M.c2.y / u.M.c2.z;
                        if (math.max(Math.Abs(kx), Math.Abs(kz)) <= h) break;
                        int edge = Math.Abs(kx) >= Math.Abs(kz) ? (kx > 0 ? 0 : 2) : (kz > 0 ? 1 : 3);
                        u = S[edge].Inverse() * u; crossings++;
                    }
                    double dev = Dev(u, -1);
                    if (double.IsNaN(dev) || double.IsInfinity(dev)) { final = dev; return dev; }
                    mx = math.max(mx, dev);
                    maxEntry = math.max(maxEntry, math.max(math.cmax(math.abs(u.M.c0)), math.max(math.cmax(math.abs(u.M.c1)), math.cmax(math.abs(u.M.c2)))));
                }
                final = Dev(u, -1);
                return mx;
            }
            double mxv = Run(true, out var fin, out var ent, out var cr);
            TestContext.WriteLine($"renormalised: max {mxv:E2}, final {fin:E2}, max entry {ent:F3}, crossings {cr}");
            Assert.Less(mxv, 1e-12); Assert.Less(fin, 1e-12); Assert.Less(ent, 2);
            double raw = Run(false, out var rf, out _, out var rc);
            TestContext.WriteLine($"without Orthonormalised (log only): max {raw:E2}, final {rf:E2}");
        }

        // G0: kernel contract stubs compile and are usable (Build Graph p. 10; §6.5).
        [Test]
        public void KernelContracts_Stubs()
        {
            Assert.IsTrue(TileId.Zero == new TileId(0, 0, 0, 0, 1, 0));
            var a = new TileId(1, 2, 3, 4, 5, 6); var b = new TileId(1, 2, 3, 4, 5, 6);
            Assert.IsTrue(a.Equals(b)); Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsTrue(default(TileId) != TileId.Zero);
            var w = new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity };
            Assert.AreEqual(TileId.Zero, w.Tile); Assert.AreEqual(double3x3.identity, w.Local.M);
        }
    }
}
