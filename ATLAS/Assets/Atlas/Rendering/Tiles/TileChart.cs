using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    public static class TileChart
    {
        public const int BoundarySegments = 16;          // Master Plan §3.4
        public const float KerbHeight = 0.9f;            // Master Plan §3.4, §3.5
        public const double KerbHalfThickness = 0.05;    // [P1-B05]
        public const double PostRadius = 0.15;           // [R6 §2.6]
        public const float PostHeight = 2.6f;            // [R6 §2.6]
        public const int PostArcSegments = 4;            // [P1-B05]
        public const double AoMin = 0.6, AoRange = 0.3;  // [P1-B05]

        public static int TriangleCap(TileLod lod) => lod switch { TileLod.Near => 1200, TileLod.Mid => 500, _ => 250 }; // Master Plan §8.4

        // Master Plan A.3: cosh r = cos(pi/q)/sin(pi/4), half-width tanh r; sphere tan r; flat g/2.
        public static double HalfWidth(int kappa, int q, double scale)
        {
            if (kappa == 0) return scale / 2;
            double c = Math.Cos(Math.PI / q) / Math.Sin(Math.PI / 4);
            return kappa < 0 ? Math.Tanh(Math.Acosh(c)) : Math.Tan(Math.Acos(c));
        }

        public static double CornerAngle(int kappa, int q) => kappa == 0 ? Math.PI / 2 : 2 * Math.PI / q; // A.3

        // Master Plan A.6
        public static double2 KleinToChart(double2 k, int kappa, double scale)
        {
            if (kappa == 0) return k;
            double n = math.length(k);
            if (n == 0) return double2.zero;
            double r = kappa < 0 ? scale * Math.Atanh(n) : scale * Math.Atan(n);
            return k * (r / n);
        }

        public static double2 ChartToKlein(double2 x, int kappa, double scale)
        {
            if (kappa == 0) return x;
            double n = math.length(x);
            if (n == 0) return double2.zero;
            double t = kappa < 0 ? Math.Tanh(n / scale) : Math.Tan(n / scale);
            return x * (t / n);
        }

        // Master Plan A.6: model point (k, 1)/sqrt(1 + kappa |k|^2)
        public static double3 KleinToModel(double2 k, int kappa)
        {
            if (kappa == 0) return new double3(k, 1);
            return new double3(k, 1) / Math.Sqrt(1 + kappa * math.lengthsq(k));
        }

        public static double2 ModelToKlein(double3 p) => p.xy / p.z;
    }
}
