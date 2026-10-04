using System;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>The kappa = 0 half of the kernel maths tests of Master Plan section 11.1.</summary>
    [TestFixture]
    public class Iso2FlatTests
    {
        private const double Flat = Curvature.Flat;

        [Test]
        public void Iso2_Conventions()
        {
            double3 o = KernelMaps.BasePoint;
            Iso2 boost = Iso2.Boost(new double2(1.0, 0.0), Flat);
            Iso2 quarter = Iso2.Rot(Math.PI / 2.0);

            Assert.That(boost.Apply(o), Is.EqualTo(new double3(1.0, 0.0, 1.0)));

            // +x turns towards +z: anticlockwise seen from above, a left turn.
            double3 turned = quarter.Apply(new double3(1.0, 0.0, 1.0));
            Assert.That(KernelTestUtil.MaxAbsDifference(turned, new double3(0.0, 1.0, 1.0)), Is.LessThan(1e-15));

            // The right factor acts first: turn, then move.
            double3 both = (boost * quarter).Apply(new double3(1.0, 0.0, 1.0));
            Assert.That(KernelTestUtil.MaxAbsDifference(both, new double3(1.0, 1.0, 1.0)), Is.LessThan(1e-15));

            Iso2 m = Iso2.Boost(new double2(0.25, -0.5), Flat) * Iso2.Rot(0.3);
            Assert.That(m.Position, Is.EqualTo(new double3(m.M02, m.M12, m.M22)));
            Assert.That(m.Position, Is.EqualTo(m.Apply(o)));
            Assert.That(m.ToDouble3x3().c2, Is.EqualTo(m.Position));
            Assert.That(m.ToDouble3x3().c0, Is.EqualTo(new double3(m.M00, m.M10, m.M20)));

            Assert.That(Iso2.Identity.Kappa, Is.EqualTo(0.0));
            Assert.That(Iso2.Identity, Is.EqualTo(Iso2.IdentityFor(Flat)));
            Assert.That(Iso2.Identity.Defect(), Is.EqualTo(0.0));
            Assert.That(Iso2.Rot(0.3).Kappa, Is.EqualTo(0.0));
            Assert.That(default(Iso2), Is.Not.EqualTo(Iso2.Identity));
            Assert.That(Iso2.Rot(0.3).GetHashCode(), Is.EqualTo(Iso2.Rot(0.3, Flat).GetHashCode()));
            Assert.That(Iso2.Identity.Equals((object)Iso2.Identity), Is.True);
            Assert.That(Iso2.Identity.ToString(), Is.EqualTo("[[1, 0, 0], [0, 1, 0], [0, 0, 1]] kappa=0"));
        }

        // Lane B adds the curved rows: (1.0612750619050357, 2 pi / 5 ... , -1, 5) and (pi / 2, ..., +1, 3).
        [TestCase(0.5, Flat, 4)]
        [TestCase(1.0613, Flat, 4)]
        [TestCase(2.0, Flat, 4)]
        [TestCase(3.2, Flat, 4)]
        public void Closure_345_Flat(double side, double kappa, int laps)
        {
            Iso2 g = KernelTestUtil.LapGenerator(side, Math.PI / 2.0, kappa);
            Iso2 identity = Iso2.IdentityFor(kappa);

            Assert.That(KernelTestUtil.MaxAbsDifference(KernelTestUtil.Power(g, laps), identity), Is.LessThan(1e-12));
            for (int n = 1; n < laps; n++)
            {
                Assert.That(KernelTestUtil.MaxAbsDifference(KernelTestUtil.Power(g, n), identity), Is.GreaterThan(0.1),
                    "the walk must not close after " + n + " sides");
            }
        }

        [Test]
        public void Kernel_Inverse_Flat()
        {
            var random = new System.Random(KernelTestUtil.Seed);
            for (int n = 0; n < 1000; n++)
            {
                var t = new double2(KernelTestUtil.Range(random, -10.0, 10.0), KernelTestUtil.Range(random, -10.0, 10.0));
                Iso2 m = Iso2.Boost(t, Flat) * Iso2.Rot(KernelTestUtil.Range(random, -Math.PI, Math.PI));
                Iso2 inverse = m.Inverse();

                Assert.That(inverse.Kappa, Is.EqualTo(m.Kappa));
                Assert.That(KernelTestUtil.MaxAbsDifference(m * inverse, Iso2.Identity), Is.LessThan(1e-12));
                Assert.That(KernelTestUtil.MaxAbsDifference(inverse * m, Iso2.Identity), Is.LessThan(1e-12));
            }
        }

        [Test]
        public void Kernel_EtaOrthonormal_Flat()
        {
            var random = new System.Random(KernelTestUtil.Seed);
            for (int n = 0; n < 1000; n++)
            {
                Iso2 boost = Iso2.Boost(KernelTestUtil.RandomStep(random, 10.0), Flat);
                Iso2 rot = Iso2.Rot(KernelTestUtil.Range(random, -Math.PI, Math.PI));
                Assert.That(boost.Defect(), Is.LessThan(1e-12));
                Assert.That(rot.Defect(), Is.LessThan(1e-12));
                Assert.That((boost * rot).Defect(), Is.LessThan(1e-12));
                Assert.That((rot * boost * rot).Defect(), Is.LessThan(1e-12));
            }

            // The drift walk: the frame stays on the group over a long session.
            Iso2 m = Iso2.Identity;
            for (int n = 0; n < 100000; n++)
            {
                Iso2 raw = m * Iso2.Boost(KernelTestUtil.RandomStep(random, 0.2), Flat)
                             * Iso2.Rot(KernelTestUtil.Range(random, -0.1, 0.1));
                m = raw.Orthonormalised();

                bool translationKept =
                    BitConverter.DoubleToInt64Bits(m.M02) == BitConverter.DoubleToInt64Bits(raw.M02) &&
                    BitConverter.DoubleToInt64Bits(m.M12) == BitConverter.DoubleToInt64Bits(raw.M12);
                bool lastRowExact = m.M20 == 0.0 && m.M21 == 0.0 && m.M22 == 1.0
                                 && raw.M20 == 0.0 && raw.M21 == 0.0 && raw.M22 == 1.0;
                if (!translationKept || !lastRowExact || !(m.Defect() < 1e-12))
                {
                    Assert.Fail("step " + n + ": translationKept=" + translationKept + " lastRowExact=" + lastRowExact
                                + " defect=" + m.Defect());
                }
            }
            Assert.That(m.Defect(), Is.LessThan(1e-12));

            // A matrix with no rotation part has no nearest group element.
            var broken = new Iso2(0.0, 0.0, 1.0, 0.0, 0.0, 2.0, 0.0, 0.0, 1.0, Flat);
            Assert.Throws<InvalidOperationException>(() => broken.Orthonormalised());
        }

        [Test]
        public void Kernel_ExpLog_Flat()
        {
            var random = new System.Random(KernelTestUtil.Seed);
            for (int n = 0; n < 1000; n++)
            {
                double2 t = KernelTestUtil.RandomStep(random, 4.0);
                double3 p = KernelMaps.Exp(t, Flat);

                Assert.That(math.cmax(math.abs(KernelMaps.Log(p, Flat) - t)), Is.LessThan(1e-12));
                Assert.That(KernelMaps.Distance(p, KernelMaps.BasePoint, Flat), Is.EqualTo(math.length(t)).Within(1e-12));
                // Exp agrees with the boost: both move the base point along the same tangent vector.
                Assert.That(KernelTestUtil.MaxAbsDifference(Iso2.Boost(t, Flat).Position, p), Is.LessThan(1e-12));
            }
            Assert.That(KernelMaps.BasePoint, Is.EqualTo(new double3(0.0, 0.0, 1.0)));
        }

        [Test]
        public void Iso2_Upload()
        {
            Iso2 m = Iso2.Boost(new double2(0.25, -0.5), Flat) * Iso2.Rot(0.3);
            float4x4 upload = m.ToFloat4x4();

            Assert.That(upload.c2.x, Is.EqualTo((float)m.M02));
            Assert.That(upload.c2.y, Is.EqualTo((float)m.M12));
            Assert.That(upload.c0.y, Is.EqualTo((float)m.M10));
            Assert.That(upload.c1.x, Is.EqualTo((float)m.M01));
            Assert.That(upload.c3, Is.EqualTo(new float4(0f, 0f, 0f, 1f)));
            Assert.That(upload.c0.w, Is.EqualTo(0f));
            Assert.That(upload.c1.w, Is.EqualTo(0f));
            Assert.That(upload.c2.w, Is.EqualTo(0f));

            float3x3 small = m.ToFloat3x3();
            Assert.That(small.c0, Is.EqualTo(upload.c0.xyz));
            Assert.That(small.c1, Is.EqualTo(upload.c1.xyz));
            Assert.That(small.c2, Is.EqualTo(upload.c2.xyz));

            // The shader reads mul((float3x3)_Anchor, p): the same product as Apply.
            float3 p = math.mul(small, new float3(1f, 2f, 1f));
            double3 expected = m.Apply(new double3(1.0, 2.0, 1.0));
            Assert.That(math.cmax(math.abs((double3)p - expected)), Is.LessThan(1e-6));
        }

        [Test]
        public void Iso2_RowMajorRoundTrip()
        {
            Iso2 m = Iso2.Boost(new double2(0.25, -0.5), Flat) * Iso2.Rot(0.3);
            var wire = new double[9];
            m.CopyTo(wire);

            Assert.That(wire[2], Is.EqualTo(m.M02));
            Assert.That(wire[3], Is.EqualTo(m.M10));
            Assert.That(wire[5], Is.EqualTo(m.M12));
            Assert.That(KernelTestUtil.BitIdentical(Iso2.FromRowMajor(wire, Flat), m), Is.True);
            Assert.That(Iso2.FromRowMajor(wire, Flat), Is.EqualTo(m));

            Assert.Throws<ArgumentException>(() => m.CopyTo(new double[8]));
            Assert.Throws<ArgumentException>(() => Iso2.FromRowMajor(new double[8], Flat));
        }
    }
}
