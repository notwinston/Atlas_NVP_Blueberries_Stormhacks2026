using System;
using Atlas.Kernel;
using Unity.Collections;
using Unity.Mathematics;

namespace Atlas.Blades.FieldFit
{
    /// <summary>
    /// 1,024-sample table of the morph lens sigma_t(s), s = S*i/1023, for 0 < t < 1 (inverse by search and interpolation).
    /// Master Plan §10.4.3 Slider; Brief §5, §7.2 step 4 (DV16).
    /// </summary>
    internal struct LensRadialTable : IDisposable
    {
        public const int Samples = 1024;            // Master Plan §10.4.3
        NativeArray<double> sigma;
        double s;                                    // S of the filled parameters
        public bool IsCreated => sigma.IsCreated;
        public NativeArray<double> Sigma => sigma;

        /// <summary>sigma_i = SigmaMorph(S*i/1023, p), i = 0..1023.</summary>
        public void Fill(in LensParams p)
        {
            if (!sigma.IsCreated) sigma = new NativeArray<double>(Samples, Allocator.Persistent);
            s = LensMath.S(p);
            for (int i = 0; i < Samples; i++) sigma[i] = LensMath.SigmaMorph(s * i / (Samples - 1), p);
        }

        /// <summary>Stereographic source radius s for output radius sigma: binary search, linear interpolation, clamped to [0, S].</summary>
        public double InverseS(double sg) { return InverseS(sigma, s, sg); }

        /// <summary>Burst-callable form: the table's sigma array and its S.</summary>
        public static double InverseS(in NativeArray<double> sigma, double sMax, double sg)
        {
            if (!(sg > sigma[0])) return 0.0;
            if (sg >= sigma[Samples - 1]) return sMax;
            int lo = 0, hi = Samples - 1;
            while (hi - lo > 1) { int mid = (lo + hi) >> 1; if (sigma[mid] <= sg) lo = mid; else hi = mid; }
            lo = math.clamp(lo, 0, Samples - 2);          // Brief §14: clamp the index to [0, 1022]
            double f = (sg - sigma[lo]) / (sigma[lo + 1] - sigma[lo]);
            return sMax * (lo + f) / (Samples - 1);
        }

        public void Dispose() { if (sigma.IsCreated) sigma.Dispose(); }
    }
}
