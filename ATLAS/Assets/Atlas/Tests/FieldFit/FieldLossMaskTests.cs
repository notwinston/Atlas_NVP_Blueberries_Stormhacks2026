#if !ATLAS_HEADLESS
using System;
using NUnit.Framework;
using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B04 tests, Master Plan §10.4.6; Brief §7.4, §9. CPU only.
    public class FieldLossMaskTests
    {
        const double Deg = Math.PI / 180.0;

        [Test, Description("P3-B04 acceptance: mask sampling classifies a generated 10 deg tunnel: 9.8 deg seen, 10.2 deg lost on 360 azimuths (Master Plan §10.4.6)")]
        public void Mask_Disc10_Classification()
        {
            var tex = MaskFactory.Disc(512, 30, 10);
            using (var m = FieldLossMask.FromTexture(tex, 30f))
            {
                Assert.IsTrue(m.Data.Present);
                for (int a = 0; a < 360; a++)
                {
                    double c = Math.Cos(a * Deg), s = Math.Sin(a * Deg);
                    Assert.LessOrEqual(m.Data.Margin(9.8 * Deg, c, s), 0, "9.8 deg seen at alpha " + a);
                    Assert.Greater(m.Data.Margin(10.2 * Deg, c, s), 0, "10.2 deg lost at alpha " + a);
                }
            }
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [Test, Description("P3-B04 acceptance: everything beyond the mask half-width M = 30 deg is lost; 29.5 deg is seen with an all-white mask (Master Plan §10.4.6, DV6)")]
        public void Mask_BeyondHalfWidth_Lost()
        {
            var tex = MaskFactory.Solid(512, true);
            using (var m = FieldLossMask.FromTexture(tex, 30f))
                for (int a = 0; a < 360; a++)
                {
                    double c = Math.Cos(a * Deg), s = Math.Sin(a * Deg);
                    Assert.Greater(m.Data.Margin(30.5 * Deg, c, s), 0, "30.5 deg lost at alpha " + a);
                    Assert.LessOrEqual(m.Data.Margin(29.5 * Deg, c, s), 0, "29.5 deg seen at alpha " + a);
                }
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [Test, Description("P3-B04 acceptance: the signed distance puts the boundary half-way between pixels of different class (Brief §7.4)")]
        public void Mask_SignedDistance_HalfPixelBoundary()
        {
            var tex = MaskFactory.Solid(512, false, 256);           // columns 0..255 seen
            using (var m = FieldLossMask.FromTexture(tex, 30f))
            {
                double rad = 2 * 30 * Deg / 512;
                Assert.AreEqual(-0.5 * rad, m.Data.Dist[100 * 512 + 255], 1e-7);
                Assert.AreEqual(0.5 * rad, m.Data.Dist[100 * 512 + 256], 1e-7);
                Assert.AreEqual(-10.5 * rad, m.Data.Dist[100 * 512 + 245], 1e-6);
                Assert.AreEqual(5.5 * rad, m.Data.Dist[100 * 512 + 261], 1e-6);
            }
            UnityEngine.Object.DestroyImmediate(tex);
        }

        [Test, Description("P3-B04 acceptance: the mask's native memory is released and Absent stays a no-op (Brief §14)")]
        public void Mask_Dispose_ReleasesNative()
        {
            var tex = MaskFactory.Disc(512, 30, 10);
            var m = FieldLossMask.FromTexture(tex, 30f);
            m.Dispose(); m.Dispose();
            Assert.IsFalse(m.Data.Present);
            FieldLossMask.Absent.Dispose();
            Assert.IsFalse(FieldLossMask.Absent.Data.Present);
            UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
#endif
