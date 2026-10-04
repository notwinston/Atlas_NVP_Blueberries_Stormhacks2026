#if !ATLAS_HEADLESS
using System;
using System.IO;
using System.Text;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B04 acceptance: "tunnel edge at 10 deg +- 0.2 deg measured on a ring test scene" (Master Plan §10.4.8, §10.4.6; Brief §9, DV14).
    // The ring test scene is a uniform white source rendered through the FieldFitBlit rule at 2796 x 1290. The CPU mirror
    // (BlitReference) renders it everywhere; the shader itself is exercised by FieldFitBlitTests (GPU, DV-B03-g).
    public class RingTestSceneTests
    {
        const double Deg = Math.PI / 180.0;
        const int W = 1398, H = 1290;
        static readonly Vector4 Unbounded = new Vector4(-1e4f, 1e4f, -1e4f, 1e4f);

        static string EvidenceDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B04"));
            Directory.CreateDirectory(d); return d;
        }

        static LensParams Params(LensProfile pr) { var d = LensParams.Default; return new LensParams(pr, d.E, d.Rho0, d.E1, 1.0); }

        sealed class Scene : IDisposable
        {
            public LensLut Lut = new LensLut(); public BlitReference Ref; public TestGeometry G = TestGeometry.Dtest;
            public Scene(LensParams p, FieldLossMask mask)
            {
                Lut.SetEyeGeometry(0, G); Lut.SetEyeGeometry(1, G);
                Assert.IsTrue(Lut.Rebuild(p, mask));
                var fr = p.Profile == LensProfile.None ? G.Frustum : Unbounded;
                Ref = new BlitReference(Lut, (float)Math.Tan(p.E), fr, fr);
            }
            public bool White(int eye, int ix, int iy)
            {
                if (ix < 0 || iy < 0 || ix >= W || iy >= H) return false;
                return Ref.Eval(eye, new Vector2((ix + 0.5f) / W, (iy + 0.5f) / H), out _);   // white source: visible = white
            }
            public double EOut(double px, double py)
            {
                var v = G.ViewTangent(new Vector2((float)(px / W), (float)(py / H)));
                return Math.Atan(Math.Sqrt((double)v.x * v.x + (double)v.y * v.y));
            }
            public void Dispose() { Lut.Dispose(); }
        }

        // 360 rays per eye from the screen point of e_out = 0, 0.25 px steps; edge = midpoint of the last white and first black pixel centres.
        static void Measure(Scene sc, out double max, out double mean, out int n)
        {
            max = 0; double sum = 0; n = 0;
            for (int eye = 0; eye < 2; eye++)
                for (int a = 0; a < 360; a++)
                {
                    double cx = W * 0.5, cy = H * 0.5;                     // D_test: q = (0.5, 0.5) is e_out = 0
                    double ca = Math.Cos(a * Deg), sa = Math.Sin(a * Deg);
                    int lastX = (int)Math.Floor(cx), lastY = (int)Math.Floor(cy);
                    Assert.IsTrue(sc.White(eye, lastX, lastY), "centre must be white");
                    bool found = false; double ex = 0, ey = 0;
                    for (double t = 0.25; t < 900; t += 0.25)
                    {
                        int ix = (int)Math.Floor(cx + t * ca), iy = (int)Math.Floor(cy + t * sa);
                        if (ix == lastX && iy == lastY) continue;
                        if (!sc.White(eye, ix, iy)) { ex = 0.5 * ((lastX + 0.5) + (ix + 0.5)); ey = 0.5 * ((lastY + 0.5) + (iy + 0.5)); found = true; break; }
                        lastX = ix; lastY = iy;
                    }
                    Assert.IsTrue(found, $"no edge on ray {a} eye {eye}");
                    double err = Math.Abs(sc.EOut(ex, ey) / Deg - 10.0);
                    if (err > max) max = err;
                    sum += err; n++;
                }
            mean = sum / n;
        }

        static void SaveRing(Scene sc, string path)
        {
            var tex = new Texture2D(2 * W, H, TextureFormat.RGBA32, false);
            var px = new Color32[2 * W * H];
            for (int eye = 0; eye < 2; eye++)
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        bool w = sc.White(eye, x, y);
                        var c = w ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 255);
                        double e = sc.EOut(x + 0.5, y + 0.5) / Deg, er = sc.EOut(x + 1.5, y + 0.5) / Deg, eu = sc.EOut(x + 0.5, y + 1.5) / Deg;
                        foreach (var r in new[] { 9.8, 10.2 })
                            if ((e - r) * (er - r) <= 0 || (e - r) * (eu - r) <= 0) c = new Color32(255, 0, 0, 255);   // reference circles
                        px[y * 2 * W + eye * W + x] = c;
                    }
            tex.SetPixels32(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void RunConfig(string name, LensParams p, FieldLossMask mask, StringBuilder log, bool savePng)
        {
            using (var sc = new Scene(p, mask))
            {
                Measure(sc, out double max, out double mean, out int n);
                Debug.Log($"P3-B04 ring test {name}: max {max:F4} deg, mean {mean:F4} deg over {n} rays");
                log.AppendLine($"{name}: rays={n} max_err_deg={max:F4} mean_err_deg={mean:F4} (limit 0.2)");
                Assert.LessOrEqual(max, 0.2, name + " max edge error (deg)");
                if (savePng) SaveRing(sc, Path.Combine(EvidenceDir(), "ring_test.png"));
            }
        }

        static readonly StringBuilder Log = new StringBuilder();

        [Test, Description("P3-B04 acceptance: tunnel edge at 10 deg +- 0.2 deg, (a) profile None with the generated 10 deg tunnel mask (Master Plan §10.4.8)")]
        public void TunnelEdge_10Deg_None_WithMask()
        {
            var tex = MaskFactory.Disc(512, 30, 10);
            using (var mask = FieldLossMask.FromTexture(tex, 30f)) RunConfig("a_None_mask", Params(LensProfile.None), mask, Log, true);
            UnityEngine.Object.DestroyImmediate(tex);
            Flush();
        }

        [Test, Description("P3-B04 acceptance: tunnel edge at 10 deg +- 0.2 deg, (b) Morph t = 1, rho0 = 10 deg, with the tunnel mask (Master Plan §10.4.8)")]
        public void TunnelEdge_10Deg_Morph_WithMask()
        {
            var tex = MaskFactory.Disc(512, 30, 10);
            using (var mask = FieldLossMask.FromTexture(tex, 30f)) RunConfig("b_Morph_mask", Params(LensProfile.Morph), mask, Log, false);
            UnityEngine.Object.DestroyImmediate(tex);
            Flush();
        }

        [Test, Description("P3-B04 acceptance: tunnel edge at 10 deg +- 0.2 deg, (c) Morph t = 1, rho0 = 10 deg, no mask (Master Plan §10.4.8)")]
        public void TunnelEdge_10Deg_Morph_NoMask()
        {
            RunConfig("c_Morph_nomask", Params(LensProfile.Morph), FieldLossMask.Absent, Log, false);
            Flush();
        }

        [Test, Description("P3-B04 acceptance: tunnel edge at 10 deg +- 0.2 deg, (d) None with lane D's tunnel10.png preset (R11; placeholder: 4 px/deg, so M = 64 deg) when present")]
        public void TunnelEdge_10Deg_None_LaneDPreset()
        {
            var path = Path.Combine(Application.dataPath, "StreamingAssets", "fieldloss", "tunnel10.png");
            if (!File.Exists(path)) Assert.Ignore("lane D preset tunnel10.png not present");
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 8 || bytes[1] != (byte)'P') Assert.Ignore("tunnel10.png is a Git LFS pointer, not the image");
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            Assert.IsTrue(ImageConversion.LoadImage(tex, bytes));
            Assert.AreEqual(512, tex.width);
            float halfWidth = 0.5f * tex.width / 4f;          // 4 px/deg (placeholder PH-38, README) -> 64 deg
            using (var mask = FieldLossMask.FromTexture(tex, halfWidth)) RunConfig("d_None_laneD_preset", Params(LensProfile.None), mask, Log, false);
            UnityEngine.Object.DestroyImmediate(tex);
            Flush();
        }

        static void Flush() { File.WriteAllText(Path.Combine(EvidenceDir(), "ring_test.txt"), "P3-B04 TunnelEdge_10Deg_RingTestScene (CPU mirror of FieldFitBlit, D_test, 360 rays x 2 eyes, 0.25 px steps)\n" + Log); }
    }
}
#endif
