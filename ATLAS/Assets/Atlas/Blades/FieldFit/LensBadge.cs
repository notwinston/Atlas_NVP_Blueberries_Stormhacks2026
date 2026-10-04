using System;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.FieldFit
{
    /// <summary>
    /// The "LENS ON" badge, composited inside the final blit so the lens can never hide it. Master Plan §10.4.5; Brief §8, DV11.
    /// TextMeshPro builds the text mesh once; DrawMesh into a 256 x 64 sRGB target (the only draw call in this assembly).
    /// </summary>
    internal sealed class LensBadge : IDisposable
    {
        public const float CentreBelowDeg = 15f, WidthDeg = 8f, HeightDeg = 2f;   // DV11
        public const int TexW = 256, TexH = 64;

        RenderTexture rt; Texture tex; bool ready;
        public bool Ready => ready;
        public Texture Texture => tex;
        public Vector4 RectLeft { get; private set; }
        public Vector4 RectRight { get; private set; }

        public static LensBadge Create(TMP_FontAsset font, IEyeGeometry left, IEyeGeometry right, LensLut lut)
        {
            var b = new LensBadge();
            if (font == null) return b;                      // Ready = false: the lens may not run without its badge
            b.Render(font);
            b.ComputeRects(lut);
            b.ready = b.tex != null;
            return b;
        }

        /// <summary>Test hook: a given texture in place of the rendered text (e.g. all white).</summary>
        internal static LensBadge CreateForTest(Texture texture, LensLut lut)
        {
            var b = new LensBadge { tex = texture };
            b.ComputeRects(lut);
            b.ready = true;
            return b;
        }

        void Render(TMP_FontAsset font)
        {
            var go = new GameObject("FieldFitBadgeText") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var t = go.AddComponent<TextMeshPro>();
                t.font = font; t.text = "LENS ON"; t.color = Color.white; t.alignment = TextAlignmentOptions.Center;
                t.enableAutoSizing = true; t.fontSizeMin = 1f; t.fontSizeMax = 100f; t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(25.6f, 6.4f);       // world units: 1/10 of the target in texels
                t.ForceMeshUpdate();
                rt = new RenderTexture(TexW, TexH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, name = "FieldFit_Badge" };
                rt.Create();
                var cmd = new CommandBuffer { name = "FieldFit badge (once)" };
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(false, true, new Color(0, 0, 0, 0));
                var proj = Matrix4x4.Ortho(-12.8f, 12.8f, -3.2f, 3.2f, -10f, 10f);
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(proj, true));
                cmd.DrawMesh(t.mesh, go.transform.localToWorldMatrix, t.fontSharedMaterial);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Release();
                tex = rt;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        // Rect of the per-eye q whose view tangent is (0, -tan 15 deg): bounding box of the images of the +-tan 4 deg wide, +-tan 1 deg tall box.
        void ComputeRects(LensLut lut)
        {
            RectLeft = RectFor(lut, 0); RectRight = RectFor(lut, 1);
        }

        static Vector4 RectFor(LensLut lut, int eye)
        {
            double cy = -math.tan(math.radians((double)CentreBelowDeg)), hw = math.tan(math.radians(WidthDeg * 0.5)), hh = math.tan(math.radians(HeightDeg * 0.5));
            double2 lo = new double2(1e9), hi = new double2(-1e9);
            double[] xs = { -1, 0, 1, -1, 1, -1, 0, 1 }, ys = { -1, -1, -1, 0, 0, 1, 1, 1 };
            for (int k = 0; k < 8; k++)
            {
                var q = lut.InvertTangent(eye, new double2(xs[k] * hw, cy + ys[k] * hh));
                lo = math.min(lo, q); hi = math.max(hi, q);
            }
            return new Vector4((float)lo.x, (float)lo.y, (float)(hi.x - lo.x), (float)(hi.y - lo.y));
        }

        public void Dispose()
        {
            if (rt != null) { rt.Release(); UnityEngine.Object.DestroyImmediate(rt); rt = null; }
            tex = null; ready = false;
        }
    }
}
