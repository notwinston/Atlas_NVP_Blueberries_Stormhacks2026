using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>Until lane B lands every curved request throws; nothing quietly treats curved space as flat.</summary>
    [TestFixture]
    public class KernelCurvedGuardTests
    {
        [Test]
        public void Kernel_CurvedNotSupported()
        {
            Assert.That(Curvature.IsSupported(Curvature.Flat), Is.True);
            Assert.That(Curvature.IsSupported(Curvature.Hyperbolic), Is.False);
            Assert.That(Curvature.IsSupported(Curvature.Spherical), Is.False);
            Assert.That(Curvature.IsSupported(0.5), Is.False);
            Assert.DoesNotThrow(() => Curvature.Require(Curvature.Flat));

            Assert.That(Curvature.OfQ(3), Is.EqualTo(Curvature.Spherical));
            Assert.That(Curvature.OfQ(4), Is.EqualTo(Curvature.Flat));
            Assert.That(Curvature.OfQ(5), Is.EqualTo(Curvature.Hyperbolic));
            Assert.That(Curvature.OfQ(6), Is.EqualTo(Curvature.Hyperbolic));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.OfQ(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.OfQ(7));

            // Kappa is a sign, never a strength.
            foreach (double bad in new[] { 0.5, -0.5, 2.0, -1e-9, double.NaN, double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Curvature.Require(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.Boost(new double2(0.0, 0.0), bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.IdentityFor(bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => Iso2.Rot(0.1, bad));
                Assert.Throws<ArgumentOutOfRangeException>(() => KernelMaps.Exp(new double2(0.0, 0.0), bad));
            }

            var rowMajor = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
            foreach (double kappa in new[] { Curvature.Hyperbolic, Curvature.Spherical })
            {
                var e = Assert.Throws<NotSupportedException>(() => Curvature.Require(kappa));
                Assert.That(e.Message, Is.EqualTo(
                    "Atlas.Kernel: curvature kappa = " + (kappa < 0 ? "-1" : "1") + " is not available in this build. " +
                    "Only the flat kernel (kappa = 0) is implemented; hyperbolic and spherical geometry arrive with " +
                    "lane B (P1-B01)."));

                Assert.Throws<NotSupportedException>(() => new Iso2(1, 0, 0, 0, 1, 0, 0, 0, 1, kappa));
                Assert.Throws<NotSupportedException>(() => Iso2.IdentityFor(kappa));
                Assert.Throws<NotSupportedException>(() => Iso2.Boost(new double2(0.1, 0.0), kappa));
                Assert.Throws<NotSupportedException>(() => Iso2.Rot(0.1, kappa));
                Assert.Throws<NotSupportedException>(() => Iso2.FromRowMajor(rowMajor, kappa));
                Assert.Throws<NotSupportedException>(() => KernelMaps.Exp(new double2(0.1, 0.0), kappa));
                Assert.Throws<NotSupportedException>(() => KernelMaps.Log(KernelMaps.BasePoint, kappa));
                Assert.Throws<NotSupportedException>(
                    () => KernelMaps.Distance(KernelMaps.BasePoint, KernelMaps.BasePoint, kappa));
            }

            for (int edge = 0; edge < 4; edge++)
            {
                int k = edge;
                var e = Assert.Throws<NotSupportedException>(() => TileId.Origin.Step(k));
                Assert.That(e.Message, Does.StartWith("Atlas.Kernel: the {4,5} tile step is not available in this build."));
                Assert.That(e.Message, Does.EndWith("(P1-B04)."));
            }

            var kernel = new FlatWalkerKernel();
            foreach (int q in new[] { 3, 5, 6 })
            {
                KernelConfig config = KernelConfig.Flat(2.0);
                config.Q = q;
                var e = Assert.Throws<NotSupportedException>(() => kernel.Configure(config));
                Assert.That(e.Message, Does.StartWith("Atlas.Kernel: q = " + q + " is not available in this build."));
            }
            // A refused Configure changes nothing.
            Assert.That(kernel.Graph.Q, Is.EqualTo(4));
            Assert.That(kernel.Pose.IsValid, Is.True);
        }
    }
}
