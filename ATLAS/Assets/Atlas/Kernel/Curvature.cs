using System;

namespace Atlas.Kernel
{
    /// <summary>The three legal values of kappa (Master Plan Appendix A.1).</summary>
    public static class Curvature
    {
        public const double Hyperbolic = -1.0;
        public const double Flat       =  0.0;
        public const double Spherical  = +1.0;

        /// <summary>True when kappa is one of the three geometries: exactly -1, 0 or +1.</summary>
        public static bool IsSupported(double kappa)
        {
            return kappa == Flat || kappa == Hyperbolic || kappa == Spherical;
        }

        /// <summary>ArgumentOutOfRangeException unless kappa is exactly -1, 0 or +1.</summary>
        public static void Require(double kappa)
        {
            if (IsSupported(kappa)) return;
            throw new ArgumentOutOfRangeException(nameof(kappa), kappa,
                "Atlas.Kernel: kappa is a sign and must be exactly -1, 0 or +1.");
        }

        /// <summary>Curvature of the {4,q} tiling: 3 gives +1, 4 gives 0, 5 and 6 give -1.
        /// Any other q: ArgumentOutOfRangeException.</summary>
        public static double OfQ(int q)
        {
            switch (q)
            {
                case 3: return Spherical;
                case 4: return Flat;
                case 5:
                case 6: return Hyperbolic;
                default:
                    throw new ArgumentOutOfRangeException(nameof(q), q,
                        "Atlas.Kernel: tiles per corner must be 3, 4, 5 or 6.");
            }
        }
    }
}
