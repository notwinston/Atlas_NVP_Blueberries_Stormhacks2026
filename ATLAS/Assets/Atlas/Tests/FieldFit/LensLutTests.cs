using System;
using System.Diagnostics;
using System.IO;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B02 tests, Master Plan §10.4.3; Brief §9. Seeded System.Random(20261003). CPU only (EditMode, also runs -nographics).
    public class LensLutTests
    {
        const double Deg = Math.PI / 180.0;
        static LensParams Make(LensProfile pr, double t)
        { var d = LensParams.Default; return new LensParams(pr, d.E, d.Rho0, d.E1, t); }
        static readonly LensParams MorphT0 = Make(LensProfile.Morph, 0), MorphT1 = Make(LensProfile.Morph, 1),
                                   TanhP = Make(LensProfile.Tanh, 1), NoneP = Make(LensProfile.None, 1);

        static LensLut NewLut(TestGeometry g)
        {
            var lut = new LensLut();
            lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
            return lut;
        }

        static string EvidenceDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B02"));
            Directory.CreateDirectory(d); return d;
        }

        static Vector2[] Read(Texture2D t)
        {
            var px = t.GetPixels();           // RGHalf -> float colours
            var r = new Vector2[px.Length];
            for (int i = 0; i < px.Length; i++) r[i] = new Vector2(px[i].r, px[i].g);
            return r;
        }

        [Test, Description("P3-B02 acceptance: rebuild < 16 ms for both eyes (Master Plan §10.4.3)")]
        public void Rebuild_Under16ms()
        {
            using (var lut = NewLut(TestGeometry.Dtest))
            {
                Assert.IsTrue(lut.Rebuild(MorphT1, FieldLossMask.Absent));   // warm-up (excluded)
                var ms = new double[10];
                for (int i = 0; i < 10; i++)
                {
                    // vary a parameter so each rebuild is real work
                    var p = new LensParams(LensProfile.Morph, 54 * Deg, (10 + 0.01 * i) * Deg, 4 * Deg, 1);
                    Assert.IsTrue(lut.Rebuild(p, FieldLossMask.Absent));
                    ms[i] = lut.LastRebuildMs;
                }
                Array.Sort(ms);
                double med = 0.5 * (ms[4] + ms[5]), max = ms[9];
                Debug.Log($"P3-B02 rebuild median {med:F3} ms, max {max:F3} ms");
                File.WriteAllText(Path.Combine(EvidenceDir(), "rebuild_timing.txt"),
                    $"P3-B02 Rebuild_Under16ms (Editor, Burst, both eyes, D_test, Morph t=1)\nmedian_ms={med:F3}\nmax_ms={max:F3}\nall_ms={string.Join(",", Array.ConvertAll(ms, m => m.ToString("F3")))}\n");
                Assert.Less(max, 16.0, "max rebuild ms");
            }
        }

        static double Angle(Vector3 a, Vector3 b)
        {
            double dot = Vector3.Dot(a.normalized, b.normalized);
            Vector3 c = Vector3.Cross(a.normalized, b.normalized);
            return Math.Atan2(c.magnitude, dot);
        }

        [Test, Description("P3-B02 acceptance: round trip through the forward lens within 0.1 deg at 1,000 samples (Master Plan §10.4.3)")]
        public void RoundTrip_1000_Within_0_1Deg()
        {
            double worst = 0;
            foreach (var p in new[] { MorphT0, MorphT1, TanhP })
                foreach (var g in new[] { TestGeometry.Identity, TestGeometry.Dtest })
                    using (var lut = NewLut(g))
                    {
                        Assert.IsTrue(lut.Rebuild(p, FieldLossMask.Absent));
                        double tanE = Math.Tan(p.E);
                        foreach (var tex in new[] { lut.Left, lut.Right })
                        {
                            var data = Read(tex);
                            var rng = new System.Random(20261003);
                            int n = 0, guard = 0;
                            while (n < 1000 && guard++ < 1000000)
                            {
                                int i = rng.Next(512), j = rng.Next(512);
                                var d = data[j * 512 + i];
                                if (d.x < -0.75f) continue;            // sentinel
                                n++;
                                var q = new Vector2((i + 0.5f) / 512f, (j + 0.5f) / 512f);
                                var v = g.ViewTangent(q);
                                double tx = (2.0 * d.x - 1.0) * tanE, ty = (2.0 * d.y - 1.0) * tanE;
                                double eSrc = Math.Atan(Math.Sqrt(tx * tx + ty * ty));
                                double eShown = LensMath.Forward(eSrc, p);
                                double az = Math.Atan2(ty, tx);
                                var shown = new Vector3((float)(Math.Tan(eShown) * Math.Cos(az)), (float)(Math.Tan(eShown) * Math.Sin(az)), 1f);
                                var viewDir = new Vector3(v.x, v.y, 1f);
                                double a = Angle(shown, viewDir) / Deg;
                                if (a > worst) worst = a;
                                Assert.Less(a, 0.1, $"{p.Profile} t={p.T} texel {i},{j}");
                            }
                            Assert.AreEqual(1000, n);
                        }
                    }
            Debug.Log($"P3-B02 round-trip max error {worst:F4} deg");
            File.WriteAllText(Path.Combine(EvidenceDir(), "round_trip.txt"), $"P3-B02 RoundTrip_1000_Within_0_1Deg max_deg={worst:F4}\n");
        }

        [Test, Description("P3-B02 acceptance: two RG16F 512x512 textures, stable instances (Master Plan §10.4.3)")]
        public void Textures_RG16F_512()
        {
            using (var lut = NewLut(TestGeometry.Dtest))
            {
                Assert.IsTrue(lut.Rebuild(MorphT1, FieldLossMask.Absent));
                var l = lut.Left; var r = lut.Right;
                foreach (var t in new[] { l, r })
                {
                    Assert.AreEqual(TextureFormat.RGHalf, t.format);
                    Assert.AreEqual(512, t.width); Assert.AreEqual(512, t.height);
                    Assert.AreEqual(1, t.mipmapCount);
                    Assert.AreEqual(TextureWrapMode.Clamp, t.wrapMode);
                    Assert.AreEqual(FilterMode.Point, t.filterMode);
                    Assert.IsTrue(t.isReadable);
                }
                Assert.AreNotSame(l, r);
                lut.SetEyeGeometry(1, TestGeometry.Identity);       // make the eyes differ
                Assert.IsTrue(lut.Rebuild(MorphT1, FieldLossMask.Absent));
                CollectionAssert.AreNotEqual(l.GetRawTextureData<byte>().ToArray(), r.GetRawTextureData<byte>().ToArray());
                for (int i = 0; i < 100; i++)
                    Assert.IsTrue(lut.Rebuild(new LensParams(LensProfile.Morph, 54 * Deg, (10 + 0.001 * i) * Deg, 4 * Deg, 1), FieldLossMask.Absent));
                Assert.AreSame(l, lut.Left); Assert.AreSame(r, lut.Right);
                Assert.AreEqual(102, lut.RebuildCount);
            }
        }

        [Test, Description("P3-B02 acceptance: uv = -1 beyond rho0 for Morph and Tanh (Master Plan §10.4.3)")]
        public void Clip_Sentinel()
        {
            var g = TestGeometry.Dtest;
            foreach (var p in new[] { MorphT0, MorphT1, TanhP })
                using (var lut = NewLut(g))
                {
                    Assert.IsTrue(lut.Rebuild(p, FieldLossMask.Absent));
                    var data = Read(lut.Left);
                    int lost = 0, seen = 0;
                    for (int j = 0; j < 512; j++)
                        for (int i = 0; i < 512; i++)
                        {
                            var v = g.ViewTangent(new Vector2((i + 0.5f) / 512f, (j + 0.5f) / 512f));
                            double eOut = Math.Atan(Math.Sqrt((double)v.x * v.x + (double)v.y * v.y));
                            var d = data[j * 512 + i];
                            if (eOut > p.Rho0 + 1e-6)
                            {
                                lost++;
                                Assert.AreEqual(-1f, d.x, $"{p.Profile} R at {i},{j}");
                                Assert.That(d.y, Is.InRange(-4f, 0f), $"{p.Profile} G at {i},{j}");
                            }
                            else if (eOut < p.Rho0 - 1e-6)
                            {
                                if (p.Profile == LensProfile.Tanh && eOut > 9.9995 * Deg) continue;   // source out of range, §10.4.3
                                seen++;
                                Assert.That(d.x, Is.InRange(0f, 1f), $"{p.Profile} R at {i},{j}");
                                Assert.That(d.y, Is.InRange(0f, 1f), $"{p.Profile} G at {i},{j}");
                            }
                        }
                    Assert.Greater(lost, 0); Assert.Greater(seen, 0);
                }
        }

        [Test, Description("P3-B02 acceptance: profile None is the identity, no sentinel (prerequisite for P3-B03; Master Plan §10.4.3)")]
        public void None_Identity()
        {
            foreach (var g in new[] { TestGeometry.Identity, TestGeometry.Dtest })
                using (var lut = NewLut(g))
                {
                    Assert.IsTrue(lut.Rebuild(NoneP, FieldLossMask.Absent));
                    double tanE = Math.Tan(NoneP.E);
                    var data = Read(lut.Right);
                    double worst = 0; string wi = "";
                    for (int j = 0; j < 512; j++)
                        for (int i = 0; i < 512; i++)
                        {
                            var d = data[j * 512 + i];
                            Assert.GreaterOrEqual(d.x, -0.75f, $"sentinel at {i},{j}");
                            if (d.x < 0 || d.x > 1 || d.y < 0 || d.y > 1) continue;
                            var v = g.ViewTangent(new Vector2((i + 0.5f) / 512f, (j + 0.5f) / 512f));
                            double tx = (2.0 * d.x - 1.0) * tanE, ty = (2.0 * d.y - 1.0) * tanE;
                            double a = Angle(new Vector3((float)tx, (float)ty, 1), new Vector3(v.x, v.y, 1)) / Deg;
                            if (a > worst) { worst = a; wi = $"{i},{j} uv={d.x},{d.y} v={v}"; }
                        }
                    Debug.Log($"P3-B02 None {wi} identity max error {worst:F4} deg");
                    // Brief asks 0.05 deg, but RG16F cannot: half-ulp near uv 0.5 is 2^-12, i.e. 0.0385 deg per axis, 0.054 deg on the diagonal
                    // (the same bound the brief quotes for the round trip). Bound set to 0.06 deg; recorded as deviation DV-B02-a.
                    Assert.Less(worst, 0.06);
                }
        }

        [Test, Description("P3-B02 acceptance: invalid parameters keep the previous lookup (Master Plan §10.4.3)")]
        public void Invalid_KeepsPrevious()
        {
            using (var lut = NewLut(TestGeometry.Dtest))
            {
                Assert.IsTrue(lut.Rebuild(MorphT1, FieldLossMask.Absent));
                int count = lut.RebuildCount;
                var before = lut.Left.GetRawTextureData<byte>().ToArray();
                var bad = new LensParams(LensProfile.Morph, 54 * Deg, 4 * Deg, 4 * Deg, 1);   // e1 == rho0
                Assert.IsFalse(lut.Rebuild(bad, FieldLossMask.Absent));
                Assert.AreEqual(count, lut.RebuildCount);
                CollectionAssert.AreEqual(before, lut.Left.GetRawTextureData<byte>().ToArray());
            }
            using (var lut = new LensLut())      // no geometry
                Assert.IsFalse(lut.Rebuild(MorphT1, FieldLossMask.Absent));
        }
    }
}
