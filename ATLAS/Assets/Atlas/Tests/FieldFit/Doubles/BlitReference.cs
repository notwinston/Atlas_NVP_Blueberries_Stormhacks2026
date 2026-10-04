using UnityEngine;

namespace Atlas.Blades.FieldFit.Tests
{
    // CPU mirror of Shaders/FieldFitBlit.shader per-pixel logic (Brief §7.3), reading the lookup textures as the shader
    // Load()s them. Lets the blit rule be tested where no graphics device exists; the GPU tests compare against it.
    internal sealed class BlitReference
    {
        readonly Color[][] lut = new Color[2][];
        public float TanE; public Vector4[] Frustum = new Vector4[2];

        public BlitReference(LensLut l, float tanE, Vector4 fl, Vector4 fr)
        { lut[0] = l.Left.GetPixels(); lut[1] = l.Right.GetPixels(); TanE = tanE; Frustum[0] = fl; Frustum[1] = fr; }

        Vector2 Load(int eye, int x, int y)
        {
            x = Mathf.Clamp(x, 0, 511); y = Mathf.Clamp(y, 0, 511);
            var c = lut[eye][y * 512 + x]; return new Vector2(c.r, c.g);
        }

        /// <summary>Steps 1-5 of §7.3: returns false when the pixel is black. uv is the source uv otherwise.</summary>
        public bool Eval(int eye, Vector2 q, out Vector2 uv)
        {
            uv = default;
            float px = q.x * 512f - 0.5f, py = q.y * 512f - 0.5f;
            int bx = Mathf.FloorToInt(px), by = Mathf.FloorToInt(py);
            float fx = px - bx, fy = py - by;
            var v = new[] { Load(eye, bx, by), Load(eye, bx + 1, by), Load(eye, bx, by + 1), Load(eye, bx + 1, by + 1) };
            var w = new[] { (1 - fx) * (1 - fy), fx * (1 - fy), (1 - fx) * fy, fx * fy };
            var lost = new bool[4]; int nLost = 0;
            for (int k = 0; k < 4; k++) { lost[k] = v[k].x < -0.75f; if (lost[k]) nLost++; }
            if (nLost == 4) return false;
            if (nLost == 0) { uv = w[0] * v[0] + w[1] * v[1] + w[2] * v[2] + w[3] * v[3]; }
            else
            {
                var vk = new float[4];
                for (int a = 0; a < 4; a++)
                {
                    if (lost[a]) { vk[a] = v[a].y; continue; }
                    float m = 1e9f;
                    for (int b = 0; b < 4; b++)
                    {
                        if (!lost[b]) continue;
                        int dx = (a & 1) - (b & 1), dy = (a >> 1) - (b >> 1);
                        m = Mathf.Min(m, (dx != 0 && dy != 0 ? 1.41421356f : 1f) + v[b].y);
                    }
                    vk[a] = m;
                }
                float vv = w[0] * vk[0] + w[1] * vk[1] + w[2] * vk[2] + w[3] * vk[3];
                float sw = 0; Vector2 su = Vector2.zero;
                for (int c = 0; c < 4; c++) { float ww = lost[c] ? 0 : w[c]; sw += ww; su += ww * v[c]; }
                if (vv < 0f || sw <= 1e-6f) return false;
                uv = su / sw;
            }
            var t = (2f * uv - Vector2.one) * TanE; var f = Frustum[eye];
            if (t.x < f.x || t.x > f.y || t.y < f.z || t.y > f.w) return false;
            return uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
        }
    }
}
