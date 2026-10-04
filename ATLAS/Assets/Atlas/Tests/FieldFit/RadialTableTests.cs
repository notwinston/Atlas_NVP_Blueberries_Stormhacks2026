using System;
using System.IO;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B04 tests, Master Plan §10.4.3 Slider; Brief §9. Seeded System.Random(20261003).
    public class RadialTableTests
    {
        const double Deg = Math.PI / 180.0;
        static LensParams Morph(double t) { var d = LensParams.Default; return new LensParams(LensProfile.Morph, d.E, d.Rho0, d.E1, t); }

        [Test, Description("P3-B04 acceptance: slider table sigma_i strictly increasing over 1,024 samples, sigma_1023 = sigma0 (Master Plan §10.4.3 Slider)")]
        public void RadialTable_Monotone_1024()
        {
            var tb = new LensRadialTable();
            try
            {
                for (int k = 0; k <= 10; k++)
                {
                    var p = Morph(k / 10.0);
                    tb.Fill(p);
                    Assert.AreEqual(1024, tb.Sigma.Length);
                    for (int i = 1; i < 1024; i++) Assert.Greater(tb.Sigma[i], tb.Sigma[i - 1], $"t={p.T} i={i}");
                    Assert.Less(Math.Abs(tb.Sigma[1023] - LensMath.Sigma0(p)), 1e-12, "sigma_1023 = sigma0 at t=" + p.T);
                }
            }
            finally { tb.Dispose(); }
        }

        [Test, Description("P3-B04 acceptance (derived): table inverse within 0.02 deg of the exact inverse for t = 0.25, 0.5, 0.75 (Master Plan §10.4.3; DV16)")]
        public void RadialTable_Inverse_Accuracy()
        {
            var tb = new LensRadialTable(); double worst = 0;
            try
            {
                foreach (var t in new[] { 0.25, 0.5, 0.75 })
                {
                    var p = Morph(t); tb.Fill(p);
                    var rng = new System.Random(20261003);
                    double s0 = LensMath.Sigma0(p);
                    for (int i = 0; i < 1000; i++)
                    {
                        double sg = rng.NextDouble() * s0;
                        double eTable = LensMath.InvStereo(tb.InverseS(sg));
                        double eExact = LensMath.Inverse(LensMath.InvStereo(sg), p);
                        double d = Math.Abs(eTable - eExact) / Deg;
                        if (d > worst) worst = d;
                        Assert.Less(d, 0.02, $"t={t} sigma={sg}");
                    }
                }
            }
            finally { tb.Dispose(); }
            Debug.Log($"P3-B04 radial table worst inverse error {worst:F5} deg");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B04")); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "radial_table.txt"), $"P3-B04 RadialTable_Inverse_Accuracy worst_deg={worst:F5} (limit 0.02)\n");
        }

        static double Angle(Vector3 a, Vector3 b) => Math.Atan2(Vector3.Cross(a, b).magnitude, Vector3.Dot(a, b));

        [Test, Description("P3-B04 acceptance: lookup round trip through the forward lens within 0.1 deg at 1,000 samples for t = 0.25, 0.5, 0.75 (Master Plan §10.4.3)")]
        public void RoundTrip_Morph_Partial()
        {
            double worst = 0;
            foreach (var t in new[] { 0.25, 0.5, 0.75 })
                foreach (var g in new[] { TestGeometry.Identity, TestGeometry.Dtest })
                    using (var lut = new LensLut())
                    {
                        lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
                        var p = Morph(t);
                        Assert.IsTrue(lut.Rebuild(p, FieldLossMask.Absent));
                        double tanE = Math.Tan(p.E);
                        foreach (var tex in new[] { lut.Left, lut.Right })
                        {
                            var px = tex.GetPixels(); var rng = new System.Random(20261003); int n = 0, guard = 0;
                            while (n < 1000 && guard++ < 1000000)
                            {
                                int i = rng.Next(512), j = rng.Next(512); var d = px[j * 512 + i];
                                if (d.r < -0.75f) continue;
                                n++;
                                var v = g.ViewTangent(new Vector2((i + 0.5f) / 512f, (j + 0.5f) / 512f));
                                double tx = (2.0 * d.r - 1.0) * tanE, ty = (2.0 * d.g - 1.0) * tanE;
                                double eShown = LensMath.Forward(Math.Atan(Math.Sqrt(tx * tx + ty * ty)), p), az = Math.Atan2(ty, tx);
                                var shown = new Vector3((float)(Math.Tan(eShown) * Math.Cos(az)), (float)(Math.Tan(eShown) * Math.Sin(az)), 1f);
                                double a = Angle(shown, new Vector3(v.x, v.y, 1f)) / Deg;
                                if (a > worst) worst = a;
                                Assert.Less(a, 0.1, $"t={t} texel {i},{j}");
                            }
                            Assert.AreEqual(1000, n);
                        }
                    }
            Debug.Log($"P3-B04 partial-morph round-trip max {worst:F4} deg");
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B04")); Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "round_trip_partial.txt"), $"P3-B04 RoundTrip_Morph_Partial max_deg={worst:F4} (limit 0.1)\n");
        }
    }
}
