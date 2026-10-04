using System;
using System.Globalization;

namespace Atlas.Kernel
{
    /// <summary>The three legal values of kappa (Master Plan Appendix A.1).</summary>
    public static class Curvature
    {
        public const double Hyperbolic = -1.0;
        public const double Flat       =  0.0;
        public const double Spherical  = +1.0;

        /// <summary>True when this build implements the geometry. Flat only until lane B lands.</summary>
        public static bool IsSupported(double kappa)
        {
            return kappa == Flat;
        }

        /// <summary>ArgumentOutOfRangeException unless kappa is exactly -1, 0 or +1;
        /// then NotSupportedException unless IsSupported(kappa).</summary>
        public static void Require(double kappa)
        {
            // The flat case first: it is the only one on the frame path.
            if (kappa == Flat) return;
            if (kappa != Hyperbolic && kappa != Spherical)
            {
                throw new ArgumentOutOfRangeException(nameof(kappa), kappa,
                    "Atlas.Kernel: kappa is a sign and must be exactly -1, 0 or +1.");
            }
            throw new NotSupportedException(NotAvailable(
                "curvature kappa = " + kappa.ToString("R", CultureInfo.InvariantCulture), "P1-B01"));
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

        /// <summary>The fixed wording for everything the flat build does not carry, so logs read the same everywhere.</summary>
        internal static string NotAvailable(string what, string task)
        {
            return "Atlas.Kernel: " + what + " is not available in this build. Only the flat kernel (kappa = 0) " +
                   "is implemented; hyperbolic and spherical geometry arrive with lane B (" + task + ").";
        }
    }
}
