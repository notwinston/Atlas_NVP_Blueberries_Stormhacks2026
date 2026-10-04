using System;
using System.Numerics;

namespace Atlas.Blades.FieldFit.ScotomaRiver
{
    /// <summary>Master Plan §10.4.3: lines follow Im(w) = const with w = z + a^2/z; letter scale 1/|1 - a^2/z^2| clamped to MaxScale (Brief §5, D9).</summary>
    public static class RiverMath
    {
        public const double MaxScale = 2.0;   // MP §10.4.3

        public static Complex Forward(Complex z, double a) => z + a * a / z;

        /// <summary>The root of z + a^2/z = w outside the disc |z| = a; on the slit (|z1| = |z2| = a) the root with the larger Im (over the top).</summary>
        public static Complex Inverse(Complex w, double a)
        {
            Complex q = Complex.Sqrt(w * w - 4.0 * a * a);
            Complex z1 = (w + q) / 2.0, z2 = (w - q) / 2.0;
            double m1 = z1.Magnitude, m2 = z2.Magnitude;
            if (Math.Abs(m1 - m2) <= 1e-12 * a) return z1.Imaginary >= z2.Imaginary ? z1 : z2;
            return m1 > m2 ? z1 : z2;
        }

        public static double LetterScale(Complex z, double a)
        {
            double d = (Complex.One - a * a / (z * z)).Magnitude;
            return d <= 1.0 / MaxScale ? MaxScale : 1.0 / d;
        }
    }
}
