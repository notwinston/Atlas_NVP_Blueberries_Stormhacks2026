using System;
using Atlas.Blades.Lung.AirwayKit;
using Atlas.Kernel;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class KitClassifyTests
    {
        // Criterion: MP 10.2.3 Rule S; R3 D3 (edge 3 left, edge 1 right).
        [Test]
        public void Kit_Classify_RuleS()
        {
            foreach (var kind in KitTestUtil.Kinds)
                for (int k = 0; k < 4; k++)
                {
                    int q;
                    var got = KitGeometry.Classify(KitTestUtil.RotL(KitGeometry.KitMask(kind), k), k, out q);
                    Assert.AreEqual(kind, got); Assert.AreEqual(k, q);
                }
            Assert.AreEqual(0b1011, KitGeometry.KitMask(KitKind.Fork));
            Assert.AreEqual(0b0101, KitGeometry.KitMask(KitKind.Straight));
            Assert.AreEqual(0b0011, KitGeometry.KitMask(KitKind.BendRight));
            Assert.AreEqual(0b1001, KitGeometry.KitMask(KitKind.BendLeft));
            Assert.AreEqual(0b0001, KitGeometry.KitMask(KitKind.End));
            // entry edge outside the mask
            Assert.Throws<ArgumentException>(() => KitGeometry.Classify(0b1011, 2, out _));
            // invalid masks (entry bit set where possible)
            foreach (int bad in new[] { 0b0111, 0b1101, 0b1111, 0 })
                Assert.Throws<ArgumentException>(() => KitGeometry.Classify(bad, 0, out _), "mask " + bad);
            // KitToTile(k) carries kit edge j direction to canonical edge (j + k) mod 4 direction
            for (int k = 0; k < 4; k++)
                for (int j = 0; j < 4; j++)
                {
                    double a = j * math.PI_DBL / 2, b = ((j + k) % 4) * math.PI_DBL / 2;
                    double3 d = math.mul(KitGeometry.KitToTile(k).M, new double3(Math.Cos(a), Math.Sin(a), 0));
                    Assert.AreEqual(Math.Cos(b), d.x, 1e-12); Assert.AreEqual(Math.Sin(b), d.y, 1e-12);
                }
        }

        // Criterion: MP 10.2.3 corridor geometry (Fermi coordinates about an arm).
        [Test]
        public void Kit_FermiOracle()
        {
            var rnd = new System.Random(7);
            foreach (var (kappa, scale) in KitTestUtil.Geoms())
            {
                double lam = kappa < 0 ? scale : 1.0, H = KitGeometry.InRadiusMetres(kappa, scale);
                for (int n = 0; n < 200; n++)
                {
                    int e = rnd.Next(4);
                    double t = rnd.NextDouble() * H, u = (rnd.NextDouble() * 2 - 1) * 0.6;
                    var f = Iso2.Rot(e * Math.PI / 2) * Iso2.Boost(new double2(t / lam, 0), kappa) * Iso2.Boost(new double2(0, u / lam), kappa);
                    double2 oracle = lam * Geometry2.Log(math.mul(f.M, Geometry2.BasePoint), kappa);
                    double2 got = KitGeometry.FermiToChart(e, t, u, kappa, scale);
                    Assert.AreEqual(0, math.length(got - oracle), 1e-12, $"kappa {kappa} e {e}");
                    KitGeometry.ChartToFermi(e, got, kappa, scale, out double t2, out double u2);
                    Assert.AreEqual(t, t2, 1e-9); Assert.AreEqual(u, u2, 1e-9);
                }
            }
        }

        // Criterion: D-10, D-2 (no sphere, flat g >= 1.0 m).
        [Test]
        public void Kit_RejectsSphere()
        {
            var b = KitTestUtil.NewBuilder();
            {
                Assert.Catch<ArgumentException>(() => b.BuildTile(KitKind.Fork, Atlas.Rendering.TileMeshSpec.Spherical(2.0, Atlas.Rendering.TileLod.Near, 0, 0)));
                Assert.Catch<ArgumentException>(() => b.BuildTile(KitKind.Fork, Atlas.Rendering.TileMeshSpec.FlatCell(0.9, 0, 0)));
                Assert.Catch<ArgumentException>(() => b.BuildTile(KitKind.Fork, Atlas.Rendering.TileMeshSpec.Hyperbolic(5, 1.885, Atlas.Rendering.TileLod.Near, 1, 0)));
                Assert.Catch<ArgumentException>(() => b.BuildTile(KitKind.Fork, Atlas.Rendering.TileMeshSpec.Hyperbolic(5, 1.885, Atlas.Rendering.TileLod.Near, 0, 1)));
                Assert.Catch<ArgumentException>(() => b.BuildTile(KitKind.Fork, Atlas.Rendering.TileMeshSpec.FlatCell(1.5, 0, 0xF)));
                Assert.Catch<ArgumentException>(() => KitGeometry.InRadiusMetres(1, 2.0));
                Assert.Catch<ArgumentException>(() => KitGeometry.InRadiusMetres(0, 0.9));
            }
        }
    }
}
