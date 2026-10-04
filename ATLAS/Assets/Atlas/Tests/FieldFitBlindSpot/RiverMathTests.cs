using System;
using System.Numerics;
using Atlas.Blades.FieldFit.BlindSpotFinder;
using Atlas.Blades.FieldFit.ScotomaRiver;
using NUnit.Framework;

namespace Atlas.Blades.FieldFit.BlindSpotTests
{
    public class RiverMathTests
    {
        [Test]
        public void River_InverseRoundTrip()
        {
            // checks: §10.4.3 w = z + a^2/z inverse, |z| >= a
            foreach (double a in new[] { 0.0524078, 3.0 })
            {
                var rng = new SplitMix64(7);
                for (int i = 0; i < 1000; i++)
                {
                    var w = new Complex((rng.NextDouble() * 2 - 1) * 6 * a, (rng.NextDouble() * 2 - 1) * 3 * a);
                    var z = RiverMath.Inverse(w, a);
                    Assert.LessOrEqual((RiverMath.Forward(z, a) - w).Magnitude, 1e-12 * a);
                    Assert.GreaterOrEqual(z.Magnitude, a * (1 - 1e-12));
                }
            }
        }

        [Test]
        public void River_NoGlyphInsideA()
        {
            // checks: B07 acceptance 1: no glyph centre inside radius a
            foreach (double a in new[] { 1.5, 3.0, 4.5 })
                for (int j = -18; j <= 18; j++) for (int k = -6; k <= 6; k++)
                        Assert.GreaterOrEqual(RiverMath.Inverse(new Complex(0.43 * j, 1.0 * k), a).Magnitude, a * (1 - 1e-12));
        }

        [Test]
        public void River_ScaleDirectlyAbove()
        {
            // checks: B07 acceptance 2: scale directly above = 0.5 +- 0.02
            foreach (double a in new[] { 0.0524078, 1.5, 3.0 })
            {
                var z = RiverMath.Inverse(Complex.Zero, a);
                Assert.AreEqual(0, (z - new Complex(0, a)).Magnitude, 1e-12 * a);
                double s = RiverMath.LetterScale(z, a);
                Assert.AreEqual(0.5, s, 0.02); Assert.AreEqual(0.5, s, 1e-12);
            }
        }

        [Test]
        public void River_ScaleProfile()
        {
            // checks: letter scale 1/|1 - a^2/z^2| clamped to <= 2 (§10.4.3)
            double a = 1.7;
            Assert.AreEqual(0.8, RiverMath.LetterScale(new Complex(0, 2 * a), a), 1e-12);
            foreach (double y in new[] { 1.1, 1.5, 3, 10 })
                Assert.AreEqual(1 / (1 + 1 / (y * y)), RiverMath.LetterScale(new Complex(0, y * a), a), 1e-12);
            Assert.AreEqual(1.0, RiverMath.LetterScale(new Complex(1000 * a, 0), a), 1.1e-6);   // 1/|1 - 1e-6| = 1 + 1.000001e-6
            Assert.AreEqual(2.0, RiverMath.LetterScale(new Complex(a * (1 + 1e-9), 0), a), 0);
            double max = 0;
            for (int j = -18; j <= 18; j++) for (int k = -6; k <= 6; k++)
                    max = Math.Max(max, RiverMath.LetterScale(RiverMath.Inverse(new Complex(0.43 * j, k), a), a));
            Assert.LessOrEqual(max, 2.0);
        }

        [Test]
        public void River_Streamlines()
        {
            // checks: lines follow Im(w) = const (§10.4.3)
            foreach (double a in new[] { 1.5, 3.0 })
                for (int j = -18; j <= 18; j++) for (int k = -6; k <= 6; k++)
                        Assert.AreEqual(k, RiverMath.Forward(RiverMath.Inverse(new Complex(0.43 * j, k), a), a).Imaginary, 1e-12 * a);
        }

        [Test]
        public void River_SlitRule()
        {
            // checks: text on the dividing line goes over the top (D9)
            double a = 2.0;
            for (double x = -3.9; x < 3.95; x += 0.3)
            {
                var z = RiverMath.Inverse(new Complex(x, 0), a);
                Assert.Greater(z.Imaginary, 0); Assert.AreEqual(a, z.Magnitude, 1e-12 * a);
            }
            Assert.AreEqual(a, RiverMath.Inverse(new Complex(2 * a, 0), a).Real, 1e-12 * a);
            Assert.AreEqual(-a, RiverMath.Inverse(new Complex(-2 * a, 0), a).Real, 1e-12 * a);
        }
    }
}
