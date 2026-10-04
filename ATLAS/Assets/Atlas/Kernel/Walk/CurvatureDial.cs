using System;

namespace Atlas.Kernel
{
    // Master Plan §3.2 / §4.3 / A.4: which geometry a dial snap selects.
    public enum DialGeometry : byte { Spherical, Flat, Hyperbolic }

    // One resolved dial snap. LengthScaleM is R (q = 3), 1 (q = 4, flat, metres) or L (q = 5, 6), after the comfort floor unless strong (MP §4.4).
    public readonly struct DialTarget
    {
        public readonly int Q;
        public readonly DialGeometry Geometry;
        public readonly double Kappa;          // +1, 0, -1
        public readonly double LengthScaleM;
        public readonly bool FloorApplied;

        public DialTarget(int q, DialGeometry g, double kappa, double lengthScaleM, bool floorApplied)
        {
            Q = q; Geometry = g; Kappa = kappa; LengthScaleM = lengthScaleM; FloorApplied = floorApplied;
        }
    }

    // P1-B09: the curvature dial of Master Plan §3.2 and §10.1.3 (snaps q = 3, 4, 5, 6). Pure logic: the ramp itself is the
    // HolonomyRig's, and only runs while the walker stands (head speed <= 0.1 m/s, §4.6, §10.1.4).
    public sealed class CurvatureDial
    {
        public const int MinQ = 3, MaxQ = 6;
        public const double ProvisionalFloorL = 1.4, ProvisionalFloorR = 1.2;   // MP §4.4, provisional until G2 [B09 D5]

        public CurvatureDial(int q = 5)
        {
            CheckQ(q);
            Q = q;
        }

        public int Q { get; private set; }

        static void CheckQ(int q)
        {
            if (q < MinQ || q > MaxQ) throw new ArgumentOutOfRangeException(nameof(q), "q must be 3..6");
        }

        /// <summary>Returns true if the snap changed.</summary>
        public bool Set(int q)
        {
            CheckQ(q);
            if (q == Q) return false;
            Q = q;
            return true;
        }

        /// <summary>D-pad right (MP §10.1.3), clamps at 6.</summary>
        public bool Next() => Q < MaxQ && Set(Q + 1);

        /// <summary>D-pad left (MP §10.1.3), clamps at 3.</summary>
        public bool Prev() => Q > MinQ && Set(Q - 1);

        /// <summary>Lever twin (MP §10.1.3): 3 to 4 to 5 to 6 to 3.</summary>
        public bool Cycle() => Set(Q == MaxQ ? MinQ : Q + 1);

        /// <summary>
        /// MP §3.2, §4.3, A.4: q = 3 sphere of radius R3, q = 4 flat, q = 5 and 6 hyperbolic with L5 / L6.
        /// MP §4.4 comfort floor: length = strong ? fit : max(fit, floor).
        /// </summary>
        public static DialTarget Target(int q, double L5, double L6, double R3, bool strong)
        {
            CheckQ(q);
            switch (q)
            {
                case 4: return new DialTarget(4, DialGeometry.Flat, 0, 1.0, false);
                case 3: return Floor(3, DialGeometry.Spherical, +1, R3, ProvisionalFloorR, strong);
                case 5: return Floor(5, DialGeometry.Hyperbolic, -1, L5, ProvisionalFloorL, strong);
                default: return Floor(6, DialGeometry.Hyperbolic, -1, L6, ProvisionalFloorL, strong);
            }
        }

        static DialTarget Floor(int q, DialGeometry g, double kappa, double fit, double floor, bool strong)
        {
            bool applied = !strong && fit < floor;
            return new DialTarget(q, g, kappa, applied ? floor : fit, applied);
        }
    }
}
