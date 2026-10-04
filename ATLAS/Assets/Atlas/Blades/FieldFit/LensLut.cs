using System;
using System.Diagnostics;
using Atlas.Kernel;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Atlas.Blades.FieldFit
{
    /// <summary>One eye's ordinary-path geometry: R1 (screen uv to eye-texture uv) and R5 (frustum tangents). Brief §4.</summary>
    public interface IEyeGeometry
    {
        /// <summary>(l, r, b, t) tangents at unit distance.</summary>
        Vector4 Frustum { get; }
        /// <summary>Per-eye screen uv ([0,1]^2, origin bottom-left) to eye-texture uv; may leave [0,1]^2.</summary>
        Vector2 ScreenToEyeUv(Vector2 eyeScreenUv);
    }

    /// <summary>
    /// Per-eye lookup textures: per-texel source uv (RG16F, 512^2); (-1, -d) where the view is lost.
    /// Master Plan §10.4.3 "Lookup"; Brief §7.1 (stage A) and §7.2 (stage B).
    /// </summary>
    public sealed class LensLut : IDisposable
    {
        public const int Size = 512;                // Master Plan §10.4.3
        public const float Sentinel = -1f;          // Master Plan §10.4.3 "store uv = -1"
        public const float MaxEdgeDistance = 4f;    // texels, DV7

        static readonly ProfilerMarker Marker = new ProfilerMarker("Atlas.FieldFit.LutRebuild");   // Brief §8

        readonly Texture2D[] tex = new Texture2D[2];
        readonly IEyeGeometry[] geo = new IEyeGeometry[2];
        NativeArray<double2>[] tangents = new NativeArray<double2>[2];   // stage A, persistent
        readonly float[] gMax = new float[2];                            // rad per texel
        bool disposed;

        public Texture2D Left => tex[0];
        public Texture2D Right => tex[1];
        public int RebuildCount { get; private set; }
        public double LastRebuildMs { get; private set; }

        public LensLut()
        {
            for (int e = 0; e < 2; e++)
            {
                // RGHalf, linear, one mip, Clamp, Point; stays readable (Brief §14 "Lookup texture").
                tex[e] = new Texture2D(Size, Size, TextureFormat.RGHalf, false, true)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave,
                  name = e == 0 ? "FieldFit_LutL" : "FieldFit_LutR" };
            }
        }

        /// <summary>Stage A (Brief §7.1): view tangents per texel for one eye. Managed; once at enable.</summary>
        public void SetEyeGeometry(int eye, IEyeGeometry g)
        {
            if (eye < 0 || eye > 1) throw new ArgumentOutOfRangeException(nameof(eye));
            if (g == null) throw new ArgumentNullException(nameof(g));
            if (!tangents[eye].IsCreated) tangents[eye] = new NativeArray<double2>(Size * Size, Allocator.Persistent);
            var f = g.Frustum;
            var t = tangents[eye];
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    var eu = g.ScreenToEyeUv(new Vector2((i + 0.5f) / Size, (j + 0.5f) / Size));
                    double x = f.x + (double)eu.x * (f.y - f.x), y = f.z + (double)eu.y * (f.w - f.z);
                    t[j * Size + i] = (math.isfinite(x) && math.isfinite(y)) ? new double2(x, y) : new double2(double.NaN);   // NaN -> sentinel
                }
            // gMax: largest angle between directions of adjacent texels (rad per texel).
            double gm = 0;
            for (int j = 0; j < Size; j++)
                for (int i = 0; i < Size; i++)
                {
                    var a = t[j * Size + i];
                    if (i + 1 < Size) gm = math.max(gm, Ang(a, t[j * Size + i + 1]));
                    if (j + 1 < Size) gm = math.max(gm, Ang(a, t[(j + 1) * Size + i]));
                }
            gMax[eye] = (float)gm;
            geo[eye] = g;
        }

        static double Ang(double2 a, double2 b)
        {
            if (!math.all(math.isfinite(a)) || !math.all(math.isfinite(b))) return 0;
            double3 u = math.normalize(new double3(a, 1)), v = math.normalize(new double3(b, 1));
            return math.atan2(math.length(math.cross(u, v)), math.dot(u, v));
        }

        /// <summary>Stage B (Brief §7.2). False, textures untouched, if the parameters are invalid or a geometry is missing.</summary>
        public bool Rebuild(in LensParams p, FieldLossMask mask)
        {
            if (disposed || !p.IsValid || !tangents[0].IsCreated || !tangents[1].IsCreated) return false;
            using (Marker.Auto())
            {
                var sw = Stopwatch.StartNew();
                var pix0 = tex[0].GetPixelData<half2>(0);
                var pix1 = tex[1].GetPixelData<half2>(0);
                var h0 = MakeJob(p, tangents[0], pix0, gMax[0]).Schedule(Size * Size, 512);
                var h1 = MakeJob(p, tangents[1], pix1, gMax[1]).Schedule(Size * Size, 512);
                JobHandle.CombineDependencies(h0, h1).Complete();
                tex[0].Apply(false, false);     // stays readable
                tex[1].Apply(false, false);
                sw.Stop();
                LastRebuildMs = sw.Elapsed.TotalMilliseconds;
            }
            RebuildCount++;
            return true;
        }

        static LensLutJob MakeJob(in LensParams p, NativeArray<double2> tan, NativeArray<half2> output, float gmax)
        {
            return new LensLutJob
            {
                Tangents = tan, Output = output, Params = p,
                TanE = math.tan(p.E), Rho0 = p.Rho0, Sigma0 = LensMath.Sigma0(p), S1 = LensMath.S1(p),
                S = LensMath.S(p), KStar = LensMath.KStar(p), GMax = gmax,
            };
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int e = 0; e < 2; e++)
            {
                if (tangents[e].IsCreated) tangents[e].Dispose();
                if (tex[e] != null) UnityEngine.Object.DestroyImmediate(tex[e]);
            }
        }
    }

    // Master Plan §10.4.3 "Inverse per output pixel"; Brief §7.2. Double precision, strict float mode.
    [BurstCompile(CompileSynchronously = true, FloatPrecision = FloatPrecision.Standard, FloatMode = FloatMode.Strict)]
    internal struct LensLutJob : IJobParallelFor
    {
        const int N = LensLut.Size;
        [ReadOnly] public NativeArray<double2> Tangents;
        [WriteOnly] public NativeArray<half2> Output;
        public LensParams Params;
        public double TanE, Rho0, Sigma0, S1, S, KStar;
        public float GMax;

        static half2 Lost(double d) => new half2((half)LensLut.Sentinel, (half)(float)(-math.clamp(d, 0.0, (double)LensLut.MaxEdgeDistance)));

        // Margin phi (rad), lost <=> phi > 0: phi_clip = e_out - rho0 (mask terms: P3-B04). Master Plan §10.4.3; Brief §7.2.

        public void Execute(int index)
        {
            double2 v = Tangents[index];
            if (!math.all(math.isfinite(v))) { Output[index] = Lost(0); return; }
            double rho2 = math.dot(v, v), rho = math.sqrt(rho2);
            double2 n = rho > 0 ? v / rho : new double2(1, 0);
            var prof = Params.Profile;

            if (prof == LensProfile.None)
            {
                // DV5: no rho0 clip. Continued uv clamped so it can never reach the sentinel threshold.
                double2 uvN = 0.5 + 0.5 * v / TanE;
                Output[index] = new half2((half)(float)math.clamp(uvN.x, -0.5, 1.5), (half)(float)math.clamp(uvN.y, -0.5, 1.5));
                return;
            }

            double sOut = 2.0 * rho / (1.0 + math.sqrt(1.0 + rho2));   // 2 tan(e_out/2)
            bool lost = sOut > Sigma0;
            double eOut = 0;
            if (lost || prof == LensProfile.Tanh) eOut = math.atan(rho);
            if (!lost && eOut - Rho0 > 0) lost = true;
            if (lost)
            {
                double phi = eOut - Rho0;
                double d = LensLut.MaxEdgeDistance;
                if (phi < 4.0 * GMax)
                {
                    int i = index % N, j = index / N;
                    int i0 = math.max(i - 1, 0), i1 = math.min(i + 1, N - 1), j0 = math.max(j - 1, 0), j1 = math.min(j + 1, N - 1);
                    double gx = (PhiAt(j * N + i1) - PhiAt(j * N + i0)) / (i1 - i0);
                    double gy = (PhiAt(j1 * N + i) - PhiAt(j0 * N + i)) / (j1 - j0);
                    double g = math.sqrt(gx * gx + gy * gy);
                    d = g > 1e-12 ? phi / g : LensLut.MaxEdgeDistance;
                }
                Output[index] = Lost(d);
                return;
            }

            double tanSrc;
            if (prof == LensProfile.Morph)
            {
                double s;
                double sg = math.min(sOut, Sigma0);
                if (Params.T >= 1.0) s = sg <= S1 ? sg : S1 * math.pow(sg / S1, KStar);
                else if (Params.T <= 0.0) s = sg * S / Sigma0;
                else s = LensMath.Stereo(LensMath.Inverse(eOut, Params));   // B04 swaps in LensRadialTable for 0 < t < 1 (DV16)
                tanSrc = s / (1.0 - 0.25 * s * s);
            }
            else   // Tanh
            {
                double eSrc = LensMath.TanhInverse(eOut, Params);
                if (!(eSrc < 0.5 * math.PI_DBL)) { Output[index] = Lost(0); return; }
                tanSrc = math.tan(eSrc);
            }
            double2 uv = 0.5 + 0.5 * (tanSrc / TanE) * n;
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) { Output[index] = Lost(0); return; }
            Output[index] = new half2((half)(float)uv.x, (half)(float)uv.y);
        }

        double PhiAt(int k)
        {
            double2 v = Tangents[k];
            if (!math.all(math.isfinite(v))) return 0;
            return math.atan(math.length(v)) - Rho0;
        }
    }
}
