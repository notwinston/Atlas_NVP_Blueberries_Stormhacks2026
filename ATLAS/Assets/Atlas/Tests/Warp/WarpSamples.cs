using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Unity.Mathematics;
using Random = System.Random;

namespace Atlas.Warp.Tests
{
    // Deterministic samples shared by the CPU and GPU tests (P1-B03 acceptance, Master Plan §9.5.6).
    public static class WarpSamples
    {
        public const int Seed = 20261003;

        public static readonly float3[] Heads =
        {
            new float3(0f, 1.6f, 0f), new float3(1.7f, 1.6f, -2.3f), new float3(-4.2f, 1.5f, 3.9f),
        };

        public static void TileConstants(double kappa, out double rcTile, out double inR, out double chainMax)
        {
            if (kappa < 0) { rcTile = 0.8424821; inR = 0.5306375; chainMax = 4.6849642; } // MP A.3 {4,5}; 3.8424821 + r_c
            else { rcTile = 0.9553166; inR = Math.PI / 4; chainMax = Math.PI; }           // MP A.3 {4,3}
        }

        static double2 Dir(double a) => new double2(Math.Cos(a), Math.Sin(a));

        // Returns (U, F, A = inverse(U)·F) triples; frame 0 is the identity.
        public static (Iso2 U, Iso2 F, Iso2 A)[] Frames(double kappa, int count, Random rng)
        {
            TileConstants(kappa, out double rcTile, out _, out double chainMax);
            var res = new (Iso2, Iso2, Iso2)[count];
            for (int i = 0; i < count; i++)
            {
                if (i == 0) { res[i] = (Iso2.Identity, Iso2.Identity, WarpAnchor.Compute(Iso2.Identity, Iso2.Identity)); continue; }
                double a = U(rng, -Math.PI, Math.PI), th = U(rng, -Math.PI, Math.PI);
                double al = U(rng, -Math.PI, Math.PI), be = U(rng, -Math.PI, Math.PI);
                double u1 = rng.NextDouble(), u2 = rng.NextDouble();
                var u = (Iso2.Boost(rcTile * Math.Sqrt(u1) * Dir(a), kappa) * Iso2.Rot(th)).Orthonormalised();
                var f = Iso2.Rot(al) * Iso2.Boost(new double2(chainMax * u2, 0), kappa) * Iso2.Rot(be);
                res[i] = (u, f, WarpAnchor.Compute(u, f));
            }
            return res;
        }

        static double U(Random rng, double lo, double hi) => lo + (hi - lo) * rng.NextDouble();

        public static float3[] Points(double kappa, double hypL, Random rng)
        {
            TileConstants(kappa, out double rcTile, out double inR, out _);
            var p = new float3[64];
            int n = 0;
            p[n++] = new float3(0f, 1f, 0f);
            float e = (float)(inR * hypL), c = (float)(rcTile * hypL), d = c * 0.70710678f;
            p[n++] = new float3(e, 1f, 0); p[n++] = new float3(-e, 1f, 0); p[n++] = new float3(0, 1f, e); p[n++] = new float3(0, 1f, -e);
            p[n++] = new float3(d, 1f, d); p[n++] = new float3(-d, 1f, d); p[n++] = new float3(d, 1f, -d); p[n++] = new float3(-d, 1f, -d);
            double rmax = Math.Max(rcTile * hypL, 3.2);
            while (n < 64)
            {
                double r = rmax * Math.Sqrt(rng.NextDouble()), b = U(rng, -Math.PI, Math.PI);
                p[n++] = new float3((float)(r * Math.Cos(b)), (float)U(rng, 0, 2.5), (float)(r * Math.Sin(b)));
            }
            return p;
        }
    }
}
