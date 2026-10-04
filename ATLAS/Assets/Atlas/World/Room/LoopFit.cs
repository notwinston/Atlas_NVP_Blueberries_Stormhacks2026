using System;

namespace Atlas.World
{
    /// <summary>Lap square and exact loop-fit radii in double precision, before they are stored as floats.</summary>
    public struct LoopFitResult
    {
        /// <summary>Side of the lap square, metres.</summary>
        public double S;
        /// <summary>Curvature radius L for five rooms per corner (q = 5), metres.</summary>
        public double L5;
        /// <summary>Curvature radius L for six rooms per corner (q = 6), metres.</summary>
        public double L6;
        /// <summary>Sphere radius R for three rooms per corner (q = 3), metres.</summary>
        public double R3;
    }

    /// <summary>
    /// The loop-fit rule (Master Plan section 4.4, Appendix A.4): from the free rectangle Wf x Df to the lap
    /// square s and the radii at which a square lap closes after 3, 5 or 6 corners. Engine-free. All arithmetic
    /// is double; the float entry points round once, at the end.
    /// </summary>
    public static class LoopFit
    {
        /// <summary>Metres from the free rectangle to the nearest wall or furniture.</summary>
        public const float WallClearance = 0.30f;
        /// <summary>s = min(Wf, Df) - LapInset: the lap square lies 0.20 m inside the free rectangle per side.</summary>
        public const float LapInset = 0.40f;
        /// <summary>Smallest lap square, metres.</summary>
        public const float LapMin = 1.0f;
        /// <summary>Largest lap square, metres: a lap stays a short walk.</summary>
        public const float LapMax = 3.2f;
        /// <summary>Standing mode when the shorter side of the free rectangle is below this, metres.</summary>
        public const float StandingBelow = 1.5f;
        /// <summary>s / L at q = 5: the centre spacing of the {4,5} tiling, 2 asinh(sqrt(cos(2 pi / 5))).</summary>
        public const double SpacingQ5 = 1.0612750619050357;
        /// <summary>s / L at q = 6: the centre spacing of the {4,6} tiling, 2 asinh(sqrt(cos(2 pi / 6))).</summary>
        public const double SpacingQ6 = 1.3169578969248166;
        /// <summary>s / R at q = 3: a quarter of the circumference, pi / 2.</summary>
        public const double SpacingQ3 = 1.5707963267948966;
        /// <summary>Provisional comfort floor for L until gate G2 has measured it, metres.</summary>
        public const float FloorL = 1.4f;
        /// <summary>Provisional comfort floor for R until gate G2 has measured it, metres.</summary>
        public const float FloorR = 1.2f;

        // The rule's numbers in double precision, so that 2.4 - 0.4 is 2.0 and not 1.99999999.
        const double Inset = 0.4, Min = 1.0, Max = 3.2, Standing = 1.5, Clearance = 0.3;

        /// <summary>Centre spacing of the {4,q} tiling in units of L (q of 5 or more).</summary>
        public static double CentreSpacing(int q)
        {
            double root = Math.Sqrt(Math.Cos(2.0 * Math.PI / q));
            return 2.0 * Math.Log(root + Math.Sqrt(root * root + 1.0));
        }

        /// <summary>Lap square and exact radii from the free rectangle, in double precision.</summary>
        public static LoopFitResult Fit(double wf, double df)
        {
            double s = Math.Min(Math.Max(Math.Min(wf, df) - Inset, Min), Max);
            return new LoopFitResult { S = s, L5 = s / SpacingQ5, L6 = s / SpacingQ6, R3 = s / SpacingQ3 };
        }

        /// <summary>s from the free rectangle: clamp(min(wf, df) - 0.4, 1.0, 3.2), metres.</summary>
        public static float LapSide(float wf, float df)
        {
            return (float)Fit(wf, df).S;
        }

        /// <summary>s from an empty rectangular room measured wall to wall: clamp(min(w, d) - 1.0, 1.0, 3.2).</summary>
        public static float LapSideFromWalls(float w, float d)
        {
            return (float)Fit(w - 2.0 * Clearance, d - 2.0 * Clearance).S;
        }

        /// <summary>Exact loop-fit radius for q = 3, 5 or 6. Any other q: ArgumentOutOfRangeException.</summary>
        public static float Radius(int q, float lapSide)
        {
            switch (q)
            {
                case 3: return (float)(lapSide / SpacingQ3);
                case 5: return (float)(lapSide / SpacingQ5);
                case 6: return (float)(lapSide / SpacingQ6);
                default: throw new ArgumentOutOfRangeException(nameof(q), "The loop-fit has radii for q = 3, 5 and 6 only.");
            }
        }

        /// <summary>
        /// Radius the dial uses for q = 3, 5 or 6: the loop-fit value raised to the comfort floor, or the exact
        /// loop-fit value when the walker opted into the strong setting.
        /// </summary>
        public static float DialRadius(int q, float lapSide, bool strong = false)
        {
            float exact = Radius(q, lapSide);
            return strong ? exact : Math.Max(exact, q == 3 ? FloorR : FloorL);
        }

        /// <summary>True when the free floor is smaller than 1.5 x 1.5 m: the walker looks and leans.</summary>
        public static bool IsStanding(double wf, double df)
        {
            return Math.Min(wf, df) < Standing - 1e-9;
        }

        /// <summary>True when the free floor is smaller than 1.5 x 1.5 m: the walker looks and leans.</summary>
        public static bool IsStanding(float wf, float df)
        {
            // A float just below 1.5 (1.45f) is well below; 1.5f is exact. The margin covers rounding only.
            return Math.Min(wf, df) < StandingBelow - 1e-6f;
        }
    }
}
