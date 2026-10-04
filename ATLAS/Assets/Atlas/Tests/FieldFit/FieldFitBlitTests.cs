using System;
using System.IO;
using Atlas.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Atlas.Blades.FieldFit.Tests
{
    // P3-B03 acceptance: "with lens off, output differs from the ordinary blit by < 1 pixel" (Master Plan §10.4.3; Brief §9).
    // "Lens off" = profile None, no mask, no badge (DV4). The CPU variant runs the blit rule through BlitReference (a mirror of
    // the shader) and always runs. The GPU variant renders the real shader; it needs a graphics device and is ignored on the
    // -nographics runner (DV-B03-g).
    public class FieldFitBlitTests
    {
        const float TanE = 1.3763819f;
        static LensParams NoneParams => new LensParams(LensProfile.None, 54 * Math.PI / 180, 10 * Math.PI / 180, 4 * Math.PI / 180, 1);

        static string EvidenceDir()
        {
            var d = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P3-B03"));
            Directory.CreateDirectory(d); return d;
        }

        // Ordinary-path tangent at screen uv q.
        static Vector2 Tangent(TestGeometry g, Vector2 q) => g.ViewTangent(q);

        [Test, Description("P3-B03 acceptance: with lens off, output differs from the ordinary blit by < 1 pixel (Master Plan §10.4.3), CPU mirror of the shader")]
        public void LensOff_DiffersFromOrdinaryBlit_Under1Px()
        {
            double worst = 0; long interior = 0, exterior = 0;
            foreach (var g in new[] { TestGeometry.Identity, TestGeometry.Dtest })
            using (var lut = new LensLut())
            {
                lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
                Assert.IsTrue(lut.Rebuild(NoneParams, FieldLossMask.Absent));
                var f = g.Frustum;
                var rf = new BlitReference(lut, TanE, f, f);
                int W = TestGeometry.EyeScreen.x, H = TestGeometry.EyeScreen.y;
                for (int eye = 0; eye < 2; eye++)
                    for (int y = 0; y < H; y += (eye == 0 ? 1 : 3))
                        for (int x = 0; x < W; x += (eye == 0 ? 1 : 3))
                        {
                            var q = new Vector2((x + 0.5f) / W, (y + 0.5f) / H);
                            Vector2 eu = g.ScreenToEyeUv(q);
                            Vector2 qx0 = new Vector2((x - 0.5f) / W, q.y), qx1 = new Vector2((x + 1.5f) / W, q.y);
                            Vector2 qy0 = new Vector2(q.x, (y - 0.5f) / H), qy1 = new Vector2(q.x, (y + 1.5f) / H);
                            bool In(Vector2 u) => u.x >= 0 && u.x <= 1 && u.y >= 0 && u.y <= 1;
                            var nb = new[] { eu, g.ScreenToEyeUv(qx0), g.ScreenToEyeUv(qx1), g.ScreenToEyeUv(qy0), g.ScreenToEyeUv(qy1) };
                            int nIn = 0; foreach (var n in nb) if (In(n)) nIn++;
                            bool ok = rf.Eval(eye, q, out var uv);
                            if (nIn == 5)
                            {
                                interior++;
                                Assert.IsTrue(ok, $"interior pixel black at {x},{y}");
                                var tf = (2 * uv - Vector2.one) * TanE; var to = Tangent(g, q);
                                var dtx = (Tangent(g, qx1) - Tangent(g, qx0)) * 0.5f; var dty = (Tangent(g, qy1) - Tangent(g, qy0)) * 0.5f;
                                // J columns: dt/dx_px, dt/dy_px
                                double det = (double)dtx.x * dty.y - (double)dty.x * dtx.y;
                                var d = tf - to;
                                double ex = ((double)dty.y * d.x - (double)dty.x * d.y) / det, ey = (-(double)dtx.y * d.x + (double)dtx.x * d.y) / det;
                                double disp = Math.Sqrt(ex * ex + ey * ey);
                                if (disp > worst) worst = disp;
                            }
                            else if (nIn == 0) { exterior++; Assert.IsFalse(ok, $"exterior pixel not black at {x},{y}"); }
                        }
            }
            Debug.Log($"P3-B03 LensOff max displacement {worst:F3} px (interior {interior}, exterior {exterior})");
            File.WriteAllText(Path.Combine(EvidenceDir(), "lens_off_displacement.txt"),
                $"P3-B03 LensOff_DiffersFromOrdinaryBlit_Under1Px (CPU mirror of FieldFitBlit; identity and D_test; None, no mask, no badge)\nmax_displacement_px={worst:F4}\ninterior_pixels={interior}\nexterior_pixels={exterior}\nlimit_px=1\n");
            Assert.Less(worst, 1.0, "max displacement in output pixels");
            Assert.Greater(interior, 1000000);
        }

        static void RequireGpu()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("GPU test needs a graphics device; the shared runner passes -nographics (DV-B03-g). Run without -nographics.");
            Assert.IsTrue(SystemInfo.IsFormatSupported(UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat,
                UnityEngine.Experimental.Rendering.GraphicsFormatUsage.Linear), "RGBAFloat must be filterable");
        }

        [Test, Category("GPU"), Description("P3-B03 acceptance: with lens off, the real shader's output matches the CPU mirror and the ordinary blit within 1 px; orientation probe (Master Plan §10.4.3)")]
        public void LensOff_RealShader_MatchesReference_GPU()
        {
            RequireGpu();
            var g = TestGeometry.Dtest;
            using (var lut = new LensLut())
            {
                lut.SetEyeGeometry(0, g); lut.SetEyeGeometry(1, g);
                Assert.IsTrue(lut.Rebuild(NoneParams, FieldLossMask.Absent));
                const int N = 1536;
                var srcs = new Texture2D[2];
                for (int e = 0; e < 2; e++)
                {
                    var px = new Color[N * N];
                    for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) px[j * N + i] = new Color((i + 0.5f) / N, (j + 0.5f) / N, 1, 1);
                    srcs[e] = new Texture2D(N, N, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                    srcs[e].SetPixels(px); srcs[e].Apply(false, false);
                }
                var shader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(FieldFitFeatureTests.ShaderPath);
                var mat = new Material(shader);
                mat.SetTexture(FieldFitShaderIDs.SourceL, srcs[0]); mat.SetTexture(FieldFitShaderIDs.SourceR, srcs[1]);
                mat.SetTexture(FieldFitShaderIDs.LutL, lut.Left); mat.SetTexture(FieldFitShaderIDs.LutR, lut.Right);
                mat.SetFloat(FieldFitShaderIDs.TanE, TanE);
                mat.SetVector(FieldFitShaderIDs.FrustumL, g.Frustum); mat.SetVector(FieldFitShaderIDs.FrustumR, g.Frustum);
                mat.SetVector(FieldFitShaderIDs.BadgeRectL, Vector4.zero); mat.SetVector(FieldFitShaderIDs.BadgeRectR, Vector4.zero);
                int W = 2796, H = 1290;
                var rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                var cmd = new CommandBuffer();
                cmd.SetRenderTarget(rt);
                cmd.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                var req = UnityEngine.Rendering.AsyncGPUReadback.Request(rt); req.WaitForCompletion();
                var data = req.GetData<Vector4>();
                var rf = new BlitReference(lut, TanE, g.Frustum, g.Frustum);
                double worst = 0; int checkedPx = 0;
                for (int y = 5; y < H; y += 11)
                    for (int x = 5; x < W; x += 13)
                    {
                        int eye = x >= W / 2 ? 1 : 0; int lx = x - eye * (W / 2);
                        var q = new Vector2((lx + 0.5f) / (W / 2), (y + 0.5f) / H);
                        bool ok = rf.Eval(eye, q, out var uv);
                        var o = data[y * W + x];
                        if (!ok) continue;
                        checkedPx++;
                        // output = (uv.x, uv.y, 1) up to bilinear source sampling: |dt| in tangent units -> pixels via 1398 px per ~2.4 tangent
                        double d = Math.Max(Math.Abs(o.x - uv.x), Math.Abs(o.y - uv.y));
                        worst = Math.Max(worst, d);
                    }
                Debug.Log($"P3-B03 GPU shader vs CPU mirror: max uv difference {worst:E3} over {checkedPx} pixels");
                Assert.Less(worst, 2e-3, "GPU uv vs CPU mirror");
                // orientation probe: the top screen row reads g > 0.5 (Brief §9)
                Assert.Greater(data[(H - 1) * W + W / 4].y, 0.5f, "top row green > 0.5");
                UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(mat);
                cmd.Release();
            }
        }
    }
}
