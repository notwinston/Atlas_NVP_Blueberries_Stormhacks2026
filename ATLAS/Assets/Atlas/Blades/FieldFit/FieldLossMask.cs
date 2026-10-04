using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atlas.Blades.FieldFit
{
    /// <summary>
    /// Signed distance of the mask in radians (positive in the lost region), 512^2, plus its geometry. Master Plan §10.4.6;
    /// Brief §7.4 (DV6). Azimuthal-equidistant view-direction image of half-width M: pixel (i, j) is the direction
    /// (x, y) = ((i+0.5)/W*2 - 1, (j+0.5)/H*2 - 1) * M, +x right, +y up (texture row 0 = bottom).
    /// </summary>
    internal readonly struct FieldLossMaskData
    {
        public readonly NativeArray<float> Dist;     // rad, signed: > 0 lost
        public readonly int Width, Height;
        public readonly double HalfWidthRad;         // M
        public bool Present => Dist.IsCreated;

        public FieldLossMaskData(NativeArray<float> dist, int w, int h, double halfWidthRad)
        { Dist = dist; Width = w; Height = h; HalfWidthRad = halfWidthRad; }

        /// <summary>Margin phi (rad) of the mask for shown angle eOut along (nx, ny): lost iff > 0. Brief §7.2 step 2:
        /// max(phi_mask, phi_out), phi_out = eOut - M beyond the mask's square or disc.</summary>
        public double Margin(double eOut, double nx, double ny)
        {
            return MarginStatic(Dist, Width, Height, HalfWidthRad, eOut, nx, ny);
        }

        // Burst-callable: no managed types.
        public static double MarginStatic(in NativeArray<float> dist, int w, int h, double m, double eOut, double nx, double ny)
        {
            double phiOut = eOut - m;
            double x = eOut * nx, y = eOut * ny;
            double u = 0.5 + 0.5 * x / m, v = 0.5 + 0.5 * y / m;
            if (u < 0 || u > 1 || v < 0 || v > 1) return phiOut;
            double px = u * w - 0.5, py = v * h - 0.5;
            int i0 = (int)math.floor(px), j0 = (int)math.floor(py);
            double fx = px - i0, fy = py - j0;
            int i1 = math.min(i0 + 1, w - 1), j1 = math.min(j0 + 1, h - 1);
            i0 = math.max(i0, 0); j0 = math.max(j0, 0);
            double d = (1 - fx) * (1 - fy) * dist[j0 * w + i0] + fx * (1 - fy) * dist[j0 * w + i1]
                     + (1 - fx) * fy * dist[j1 * w + i0] + fx * fy * dist[j1 * w + i1];
            return math.max(d, phiOut);
        }
    }

    /// <summary>The tunnel (or other) field-loss mask. Master Plan §10.4.6; Brief §3, §7.4 (DV6).</summary>
    public sealed class FieldLossMask : IDisposable
    {
        public const float DefaultHalfWidthDeg = 30f;   // DV6, Brief §7.4
        static readonly FieldLossMask absent = new FieldLossMask(default);
        public static FieldLossMask Absent => absent;

        FieldLossMaskData data;
        FieldLossMask(FieldLossMaskData d) { data = d; }
        internal FieldLossMaskData Data => data;
        internal int Version { get; private set; }

        /// <summary>
        /// R channel, threshold 0.5 (>= 0.5 is seen), signed Euclidean distance transform (Felzenszwalb, two passes, pixel units;
        /// the boundary sits half-way between pixels of different class), converted to radians x (2M/W). Brief §7.4.
        /// A non-readable texture is read back with AsyncGPUReadback (needs a graphics device).
        /// </summary>
        public static FieldLossMask FromTexture(Texture2D tex, float halfWidthDeg)
        {
            if (tex == null) throw new ArgumentNullException(nameof(tex));
            int w = tex.width, h = tex.height;
            var seen = new bool[w * h];
            if (tex.isReadable)
            {
                var px = tex.GetPixels32();
                for (int k = 0; k < seen.Length; k++) seen[k] = px[k].r >= 128;   // 0.5 on the 8-bit R channel
            }
            else
            {
                var req = AsyncGPUReadback.Request(tex, 0, TextureFormat.RGBA32);
                req.WaitForCompletion();
                if (req.hasError) throw new InvalidOperationException("FieldLossMask: GPU readback failed for " + tex.name);
                var px = req.GetData<Color32>();
                for (int k = 0; k < seen.Length; k++) seen[k] = px[k].r >= 128;
            }
            double m = halfWidthDeg * Math.PI / 180.0;
            var dist = new NativeArray<float>(w * h, Allocator.Persistent);
            var toSeen = Edt(seen, w, h, true);     // distance to the nearest seen pixel (for lost pixels)
            var toLost = Edt(seen, w, h, false);    // distance to the nearest lost pixel (for seen pixels)
            double rad = 2.0 * m / w;               // radians per pixel
            for (int k = 0; k < seen.Length; k++)
            {
                double pxd = seen[k] ? -(Math.Sqrt(toLost[k]) - 0.5) : (Math.Sqrt(toSeen[k]) - 0.5);
                dist[k] = (float)(pxd * rad);
            }
            return new FieldLossMask(new FieldLossMaskData(dist, w, h, m)) { Version = 1 };
        }

        // Squared Euclidean distance to the nearest pixel of class `target` (seen == target); Felzenszwalb, rows then columns.
        static double[] Edt(bool[] seen, int w, int h, bool target)
        {
            const double Inf = 1e20;
            var f = new double[w * h];
            for (int k = 0; k < f.Length; k++) f[k] = seen[k] == target ? 0.0 : Inf;
            var line = new double[Math.Max(w, h)]; var outl = new double[Math.Max(w, h)];
            var vv = new int[Math.Max(w, h)]; var z = new double[Math.Max(w, h) + 1];
            for (int y = 0; y < h; y++)       // pass 1: along x
            {
                for (int x = 0; x < w; x++) line[x] = f[y * w + x];
                Dt1(line, outl, vv, z, w);
                for (int x = 0; x < w; x++) f[y * w + x] = outl[x];
            }
            for (int x = 0; x < w; x++)       // pass 2: along y
            {
                for (int y = 0; y < h; y++) line[y] = f[y * w + x];
                Dt1(line, outl, vv, z, h);
                for (int y = 0; y < h; y++) f[y * w + x] = outl[y];
            }
            return f;
        }

        static void Dt1(double[] f, double[] d, int[] v, double[] z, int n)
        {
            int k = 0; v[0] = 0; z[0] = -1e30; z[1] = 1e30;
            for (int q = 1; q < n; q++)
            {
                double s;
                while (true)
                {
                    s = ((f[q] + (double)q * q) - (f[v[k]] + (double)v[k] * v[k])) / (2.0 * q - 2.0 * v[k]);
                    if (s <= z[k] && k > 0) k--; else break;
                }
                k++; v[k] = q; z[k] = s; z[k + 1] = 1e30;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q) k++;
                d[q] = (double)(q - v[k]) * (q - v[k]) + f[v[k]];
            }
        }

        public void Dispose()
        {
            if (data.Present) data.Dist.Dispose();
            data = default;
        }
    }
}
