using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>Kappa is a sign: the three geometries are accepted, anything else is refused, and the flat
    /// kernel never quietly treats a curved tiling as flat.</summary>
    [TestFixture]
    public class KernelCurvedGuardTests
    {
        [Test]
        public void Kernel_CurvatureGuards()
        {
            Assert.That(Curvature.IsSupported(Curvature.Flat), Is.True);
            Assert.That(Curvature.IsSupported(Curvature.Hyperbolic), Is.True);
            Assert.That(Curvature.IsSupported(Curvature.Spherical), Is.True);
            Assert.That(Curvature.IsSupported(0.5), Is.False);
            Assert.That(Curvature.IsSupported(double.NaN), Is.False);
            foreach (double kappa in new[] { Curvature.Flat, Curvature.Hyperbolic, Curvature.Spherical })
            {
                Assert.DoesNotThrow(() => Curvature.Require(kappa));
            }

            Assert.That(Curvature.OfQ(3), Is.EqualTo(Curvature.Spherical));
            Assert.That(Curvature.OfQ(4), Is.EqualTo(Curvature.Flat));
            Assert.That(Curvature.OfQ(5), Is.EqualTo(Curvature.Hyperbolic));
            Assert.That(Curvature.OfQ(6), Is.EqualTo(Curvature.Hyperbolic));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.OfQ(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.OfQ(7));

            // Kappa is a sign, never a strength.
            var rowMajor = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            foreach (double bad in new[] { 0.5, -0.5, 2.0, -1e-9, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.Require(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => new Iso2(1, 0, 0, 0, 1, 0, 0, 0, 1, bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.IdentityFor(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.Rot(0.1, bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.FromRowMajor(rowMajor, bad));
            }

            // The curved geometries are real: the tag is kept and the matrices lie on their group.
            foreach (double kappa in new[] { Curvature.Hyperbolic, Curvature.Spherical })
            {
                Assert.That(new Iso2(1, 0, 0, 0, 1, 0, 0, 0, 1, kappa).Kappa, Is.EqualTo(kappa));
                Assert.That(Iso2.IdentityFor(kappa).Kappa, Is.EqualTo(kappa));
                Assert.That(Iso2.IdentityFor(kappa).Defect(), Is.EqualTo(0.0));
                Assert.That(Iso2.Rot(0.1, kappa).Kappa, Is.EqualTo(kappa));
                Assert.That(Iso2.FromRowMajor(rowMajor, kappa), Is.EqualTo(Iso2.IdentityFor(kappa)));

                Iso2 boost = Iso2.Boost(new double2(0.1, 0.0), kappa);
                Assert.That(boost.Kappa, Is.EqualTo(kappa));
                Assert.That(boost.Defect(), Is.LessThan(1e-12));
                Assert.That((boost * Iso2.Rot(0.3, kappa)).Defect(), Is.LessThan(1e-12));
                Assert.That(KernelTestUtil.MaxAbsDifference(boost * boost.Inverse(), Iso2.IdentityFor(kappa)), Is.LessThan(1e-12));

                double3 p = Geometry2.Exp(new double2(0.1, 0.0), kappa);
                Assert.That(KernelTestUtil.MaxAbsDifference(boost.Position, p), Is.LessThan(1e-12));
                Assert.That(Geometry2.Log(p, kappa).x, Is.EqualTo(0.1).Within(1e-12));
                Assert.That(Geometry2.Distance(p, Geometry2.BasePoint, kappa), Is.EqualTo(0.1).Within(1e-12));

                // A flat translation is not an isometry of a curved plane, and a walker pose says so.
                var mistagged = new WalkerPose { Tile = TileId.Origin, Local = new Iso2(1, 0, 0.5, 0, 1, 0, 0, 0, 1, kappa) };
                Assert.That(mistagged.IsValid, Is.False);
                Assert.That(new WalkerPose { Tile = TileId.Origin, Local = boost }.IsValid, Is.True);
            }

            // Geometries never mix: a product across -1 and +1 throws; the flat tag is neutral (kernel.md section 4.2).
            Assert.Throws<InvalidOperationException>(() =>
            {
                Iso2 unused = Iso2.IdentityFor(Curvature.Hyperbolic) * Iso2.IdentityFor(Curvature.Spherical);
            });
            Assert.That((Iso2.Rot(0.3) * Iso2.Boost(new double2(0.1, 0.0), Curvature.Hyperbolic)).Kappa,
                Is.EqualTo(Curvature.Hyperbolic));

            // TileId.Step is the {4,5} step: the four neighbours of tile zero (kernel.md section 5.5).
            Assert.That(TileId.Origin, Is.EqualTo(TileId.Zero));
            Assert.That(TileId.Origin.Step(0), Is.EqualTo(new TileId(1, 0, 0, 0, 0, 1)));
            Assert.That(TileId.Origin.Step(1), Is.EqualTo(new TileId(0, 0, 1, 0, 0, 1)));
            Assert.That(TileId.Origin.Step(2), Is.EqualTo(new TileId(-1, 0, 0, 0, 0, 1)));
            Assert.That(TileId.Origin.Step(3), Is.EqualTo(new TileId(0, 0, -1, 0, 0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => TileId.Origin.Step(4));

            // The flat kernel carries q = 4 only and says so by name.
            var kernel = new FlatWalkerKernel();
            foreach (int q in new[] { 3, 5, 6 })
            {
                KernelConfig config = KernelConfig.Flat(2.0);
                config.Q = q;
                var e = Assert.Throws<NotSupportedException>(() => kernel.Configure(config));
                Assert.That(e.Message, Does.StartWith("Atlas.Kernel: q = " + q + " is not a flat tiling."));
            }
            // A refused Configure changes nothing.
            Assert.That(kernel.Graph.Q, Is.EqualTo(4));
            Assert.That(kernel.Pose.IsValid, Is.True);
        }
    }
}
