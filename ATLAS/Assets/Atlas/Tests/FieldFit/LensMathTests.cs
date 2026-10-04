using System;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B01 tests, Master Plan §10.4.3; Build Pipeline P3-B01. Seeded System.Random(20261003).
    public class LensMathTests
    {
        const double Deg = Math.PI / 180.0;
        static double ToDeg(double r) => r / Deg;

        static LensParams Pow() => LensParams.Default;                                           // Morph, T = 1
        static LensParams WithT(double t) { var d = LensParams.Default; return new LensParams(LensProfile.Morph, d.E, d.Rho0, d.E1, t); }
        static LensParams Tanh() { var d = LensParams.Default; return new LensParams(LensProfile.Tanh, d.E, d.Rho0, d.E1, d.T); }
        static LensParams None() { var d = LensParams.Default; return new LensParams(LensProfile.None, d.E, d.Rho0, d.E1, d.T); }

        [Test, Description("P3-B01 acceptance: K* = 2.918 +/- 0.001 (Master Plan §10.4.3)")]
        public void KStar_Default_2_918()
        {
            double k = LensMath.KStar(Pow());
            Debug.Log($"P3-B01 K* = {k:F7}");
            Assert.That(Math.Abs(k - 2.918), Is.LessThanOrEqualTo(0.001));
        }

        [Test, Description("P3-B01 acceptance: rings at 5.5, 7.0, 8.0 deg +/- 0.05 (Master Plan §10.4.3)")]
        public void Rings_10_20_30_Land_5_5_7_0_8_0()
        {
            var p = Pow();
            Assert.That(Math.Abs(ToDeg(LensMath.Forward(10 * Deg, p)) - 5.5), Is.LessThanOrEqualTo(0.05));
            Assert.That(Math.Abs(ToDeg(LensMath.Forward(20 * Deg, p)) - 7.0), Is.LessThanOrEqualTo(0.05));
            Assert.That(Math.Abs(ToDeg(LensMath.Forward(30 * Deg, p)) - 8.0), Is.LessThanOrEqualTo(0.05));
        }

        [Test, Description("P3-B01 acceptance: 54 deg maps to 10.00 deg (Master Plan §10.4.3)")]
        public void Edge_54_MapsTo_10_00()
        {
            foreach (var p in new[] { Pow(), WithT(0) })
                Assert.That(Math.Abs(ToDeg(LensMath.Forward(54 * Deg, p)) - 10.0), Is.LessThanOrEqualTo(0.005));
        }

        [Test, Description("P3-B01 acceptance: tanh K at 30 deg between 30 and 40 (Master Plan §10.4.3; Spec §11.5)")]
        public void Tanh_K30_Between_30_And_40()
        {
            double k = LensMath.Dilatation(30 * Deg, Tanh());
            Debug.Log($"P3-B01 tanh K(30) = {k:F3}");
            Assert.That(k, Is.GreaterThan(30).And.LessThan(40));
        }

        [Test, Description("P3-B01 output inverses: round trip < 1e-9 rad, 1000 values (Master Plan §10.4.3)")]
        public void Inverses_RoundTrip()
        {
            var rng = new System.Random(20261003);
            var cases = new[] { (None(), 54 * Deg), (WithT(0), 54 * Deg), (WithT(0.5), 54 * Deg), (WithT(1), 54 * Deg), (Tanh(), 50 * Deg) };
            foreach (var (p, max) in cases)
                for (int i = 0; i < 1000; i++)
                {
                    double e = rng.NextDouble() * max;
                    double back = LensMath.Inverse(LensMath.Forward(e, p), p);
                    Assert.That(Math.Abs(back - e), Is.LessThan(1e-9), $"{p.Profile} T={p.T} e={e}");
                }
            // eOut beyond rho0 has no preimage (Master Plan §10.4.3)
            Assert.IsTrue(double.IsPositiveInfinity(LensMath.Inverse(11 * Deg, Pow())));
            Assert.IsTrue(double.IsPositiveInfinity(LensMath.Inverse(11 * Deg, Tanh())));
            Assert.IsTrue(double.IsPositiveInfinity(LensMath.TanhInverse(10 * Deg, Tanh())));
        }

        [Test, Description("P3-B01 output K(e): values consumed by P3-D03 (Master Plan §10.4.3, §7.1)")]
        public void Dilatation_Values()
        {
            var d = LensParams.Default;
            for (int i = 0; i <= 99; i++)
            {
                double e = d.E * i / 99.0;
                Assert.That(LensMath.Dilatation(e, None()), Is.EqualTo(1.0).Within(1e-12));
                Assert.That(LensMath.Dilatation(e, WithT(0)), Is.EqualTo(1.0).Within(1e-12));
                double k = LensMath.Dilatation(e, WithT(1));
                if (e <= d.E1) Assert.That(k, Is.EqualTo(1.0).Within(1e-12));
                else Assert.That(Math.Abs(k - LensMath.KStar(WithT(1))), Is.LessThan(1e-9));
            }
            Assert.That(LensMath.Dilatation(10 * Deg, Tanh()), Is.EqualTo(1.817).Within(0.001));
        }

        [Test, Description("P3-B01 acceptance: uniform lens attains the Schwarz bound (Master Plan §10.4.2)")]
        public void Uniform_AttainsSchwarzBound()
        {
            var p = Pow();
            Assert.That(Math.Abs(LensMath.Sigma0(p) / LensMath.S(p) - LensMath.SchwarzBound(p)), Is.LessThan(1e-12));
            Assert.That(Math.Abs(LensMath.SchwarzBound(p) - 0.1717062), Is.LessThan(1e-6));
        }

        [Test, Description("P3-B01 acceptance: slider endpoints and monotonicity (Master Plan §10.4.3 Slider)")]
        public void Morph_Endpoints_Monotone()
        {
            foreach (double t in new[] { 0, 0.25, 0.5, 0.75, 1 })
            {
                var p = WithT(t);
                double S = LensMath.S(p);
                Assert.That(Math.Abs(LensMath.SigmaMorph(S, p) - LensMath.Sigma0(p)), Is.LessThan(1e-12));
                Assert.That(LensMath.SigmaMorph(0, p), Is.EqualTo(0.0));
                double prev = 0;
                for (int i = 1; i <= 10000; i++)
                {
                    double v = LensMath.SigmaMorph(S * i / 10000.0, p);
                    Assert.That(v, Is.GreaterThan(prev), $"t={t} i={i}");
                    prev = v;
                }
            }
            // t = 0.5 reference values (Master Plan §10.4.3 Slider)
            var h = WithT(0.5);
            Assert.That(ToDeg(LensMath.Forward(10 * Deg, h)), Is.EqualTo(3.6003).Within(0.001));
            Assert.That(ToDeg(LensMath.Forward(20 * Deg, h)), Is.EqualTo(5.2158).Within(0.001));
            Assert.That(ToDeg(LensMath.Forward(30 * Deg, h)), Is.EqualTo(6.6506).Within(0.001));
        }

        [Test, Description("P3-B01 acceptance: parameter validity (Master Plan §10.4.3)")]
        public void Params_Validity()
        {
            var d = LensParams.Default;
            Assert.IsTrue(d.IsValid);
            Assert.IsFalse(new LensParams(LensProfile.Morph, d.E, d.Rho0, d.Rho0, 1).IsValid);          // e1 = rho0
            Assert.IsFalse(new LensParams(LensProfile.Morph, d.Rho0, d.Rho0, d.E1, 1).IsValid);         // rho0 = E
            Assert.IsFalse(new LensParams(LensProfile.Morph, 90 * Deg, d.Rho0, d.E1, 1).IsValid);       // E = 90 deg
            Assert.IsFalse(new LensParams(LensProfile.Morph, d.E, d.Rho0, d.E1, -0.01).IsValid);
            Assert.IsFalse(new LensParams(LensProfile.Morph, d.E, d.Rho0, d.E1, 1.01).IsValid);
            Assert.IsFalse(new LensParams((LensProfile)3, d.E, d.Rho0, d.E1, 1).IsValid);
            Assert.IsTrue(d.Equals(LensParams.Default));
        }

        [Test, Description("P3-B01 output constants (Master Plan §10.4.3)")]
        public void Constants_Default()
        {
            var p = LensParams.Default;
            Assert.That(LensMath.S(p), Is.EqualTo(1.0190509).Within(1e-7));
            Assert.That(LensMath.Sigma0(p), Is.EqualTo(0.1749773).Within(1e-7));
            Assert.That(LensMath.S1(p), Is.EqualTo(0.0698415).Within(1e-7));
            Assert.That(LensMath.KStar(p), Is.EqualTo(2.9184646).Within(1e-6));
            Assert.That(ToDeg(LensMath.Forward(30 * Deg, Tanh())), Is.EqualTo(9.9505).Within(0.001));
        }
    }
}
