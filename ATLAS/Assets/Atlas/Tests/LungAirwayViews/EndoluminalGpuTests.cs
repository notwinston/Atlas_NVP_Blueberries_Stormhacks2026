using System.IO;
using Atlas.Blades.Lung.Endoluminal;
using Atlas.Blades.Lung.Endoluminal.Testing;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.Lung.AirwayViews.Tests
{
    public class EndoluminalGpuTests
    {
        static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // Criterion: MP 10.2.3 "true 3D view ... clock positions" (needs a graphics device; skipped under -nographics).
        [Test]
        public void Endo_Render_GPU()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("Null graphics device: run without -nographics (docs/laneB/TESTING.md)");
            var palette = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var px = new Color32[128 * 128]; for (int i = 0; i < px.Length; i++) px[i] = new Color32(230, 140, 128, 255);
            palette.SetPixels32(px); palette.Apply();
            Shader.SetGlobalTexture("_PaletteTex", palette);
            var w = EndoTestUtil.NewWindow(out var go);
            RenderTexture flat = null; Texture2D img = null;
            try
            {
                CheckFork(w, SyntheticForks.Route(1)[0], "gpu_render.png", ref flat, ref img);
                // plane axis y: labels 6 and 12 o'clock; catches a vertically flipped image (the x-axis fork is symmetric)
                EndoTestUtil.Dispose(w, go);
                w = EndoTestUtil.NewWindow(out go);   // same address "" again: a fresh window renders it
                CheckFork(w, SyntheticForks.Route(1, new double3(0, 1, 0))[0], "gpu_render_axisY.png", ref flat, ref img);
            }
            finally
            {
                Shader.SetGlobalTexture("_PaletteTex", null);
                Object.DestroyImmediate(palette);
                if (flat != null) { flat.Release(); Object.DestroyImmediate(flat); }
                if (img != null) Object.DestroyImmediate(img);
                EndoTestUtil.Dispose(w, go);
            }
        }

        static void CheckFork(EndoluminalWindow w, ForkView f, string png, ref RenderTexture flat, ref Texture2D img)
        {
            {
                Assert.IsTrue(w.Show(f));
                if (flat == null) { flat = new RenderTexture(512, 512, 0, RenderTextureFormat.ARGB32) { antiAliasing = 1 }; flat.Create(); }
                if (img == null) img = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                Graphics.Blit(w.Texture, flat);
                var prev = RenderTexture.active; RenderTexture.active = flat;
                img.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); img.Apply();
                RenderTexture.active = prev;
                var pix = img.GetPixels();
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "laneB", "evidence", "P2-B07"));
                Directory.CreateDirectory(dir);
                // EncodeToPNG of a Metal read-back comes out upside down; write the rows reversed so the file shows 12 o'clock on top
                var rows = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                var flipped = new Color[pix.Length];
                for (int y = 0; y < 512; y++) System.Array.Copy(pix, y * 512, flipped, (511 - y) * 512, 512);
                rows.SetPixels(flipped); rows.Apply();
                File.WriteAllBytes(Path.Combine(dir, png), rows.EncodeToPNG());
                Object.DestroyImmediate(rows);

                float bg = Luma(EndoluminalWindow.Background.gamma);   // the RT is sRGB in a linear project
                int differ = 0; foreach (var c in pix) if (Mathf.Abs(Luma(c) - bg) > 0.02f) differ++;
                Debug.Log($"[P2-B07] GPU: {100.0 * differ / pix.Length:F1}% of pixels differ from the background");
                Assert.GreaterOrEqual(differ, pix.Length / 2, "at least 50% of pixels differ from Background");

                var pose = EndoluminalCamera.Pose(f);
                double s = EndoluminalCamera.SceneScale(f);
                Matrix4x4 vp = EndoluminalCamera.ProjectionMatrix() * EndoluminalCamera.ViewMatrix(pose, f.Parent.EndMm, s);
                float refLuma = Luma(img.GetPixel(256, (int)(0.9f * 511)));
                float darkest = 1f; foreach (var c in pix) { float l = Luma(c); if (l > bg + 0.05f) darkest = Mathf.Min(darkest, l); }
                Debug.Log($"[P2-B07] GPU: centre luma {Luma(img.GetPixel(256, 256)):F3}, darkest tissue luma {darkest:F3}, reference (0.5, 0.9) luma {refLuma:F3}");
                Assert.LessOrEqual(darkest, 0.85f * refLuma, "depth cue: distant tissue must be darker than the near wall (D-27)");
                for (int k = 0; k < 2; k++)
                {
                    var c = f.Child(k);
                    Vector3 p = EndoluminalCamera.ToScene(c.EndMm, f.Parent.EndMm, s);
                    Vector4 clip = vp * new Vector4(p.x, p.y, p.z, 1);
                    int x = Mathf.Clamp((int)((clip.x / clip.w * 0.5f + 0.5f) * 511), 0, 511), y = Mathf.Clamp((int)((clip.y / clip.w * 0.5f + 0.5f) * 511), 0, 511);
                    float far = Luma(img.GetPixel(x, y));
                    Debug.Log($"[P2-B07] GPU: child {k} far-axis luma {far:F3}, reference (0.5, 0.9) luma {refLuma:F3}, ratio {far / refLuma:F3} (brief: <= 0.5)");

                    double ang = EndoluminalCamera.ClockAngleDeg(pose, c.Dir) * System.Math.PI / 180;
                    int lx = (int)(256 + 0.35 * 512 * System.Math.Sin(ang)), ly = (int)(256 + 0.35 * 512 * System.Math.Cos(ang));
                    float mx = 0;
                    for (int j = -20; j < 20; j++) for (int i = -20; i < 20; i++) mx = Mathf.Max(mx, Luma(img.GetPixel(Mathf.Clamp(lx + i, 0, 511), Mathf.Clamp(ly + j, 0, 511))));
                    Debug.Log($"[P2-B07] GPU: label {k} ({w.LabelText(k)}) max luma {mx:F3} at ({lx}, {ly})");
                    Assert.GreaterOrEqual(mx, 0.8f, "label text must be bright");
                }
            }
        }
    }
}
