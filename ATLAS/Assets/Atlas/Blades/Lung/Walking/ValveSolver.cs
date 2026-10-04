using System;
using Atlas.Kernel;
using Atlas.Safety;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking
{
    public readonly struct ValveDecision
    {
        public readonly bool Open; public readonly int Corner, TargetCorner; public readonly double2 Direction;
        public readonly double ClearNext, ClearPrev; public readonly bool Tie;
        public ValveDecision(bool open, int corner, int target, double2 dir, double cn, double cp, bool tie)
        { Open = open; Corner = corner; TargetCorner = target; Direction = dir; ClearNext = cn; ClearPrev = cp; Tie = tie; }
    }

    // MP §10.2.3 steps 3-4 (valve chamber, Mode 1): pick the lap-square side with the larger smallest Bump Guard clearance.
    public sealed class ValveSolver
    {
        public const double ChamberRadius = 0.5;   // m, MP §10.2.3 step 1 and parameter table (chamber disc)
        public const double GlyphRadius = 0.15;    // m, MP §10.2.3 table (standing glyph); rotation gate (DV-G6-1)
        public const double ClearMin = 0.30;       // m, MP §10.2.3 step 3
        public const double SampleStep = 0.05;     // m, MP §4.2 SDF cell (DV-G6-3)
        public const double TieEpsilon = 0.05;     // m, one SDF cell (DV-G6-4)

        readonly LapCorners _lap;
        readonly IClearanceQuery _clear;
        bool _nextOnTie = true;   // one toggle per solver instance; the first tie takes Next

        public ValveSolver(LapCorners lap, IClearanceQuery clearance)
        {
            _lap = lap ?? throw new ArgumentNullException(nameof(lap));
            _clear = clearance ?? throw new ArgumentNullException(nameof(clearance));
        }

        public ValveDecision Solve(int corner)
        {
            corner &= 3;
            int next = (corner + 1) & 3, prev = (corner + 3) & 3;
            double2 ka = _lap.Corner(corner);
            double cn = SideClearance(_clear, ka, _lap.Corner(next));
            double cp = SideClearance(_clear, ka, _lap.Corner(prev));
            bool okN = cn >= ClearMin, okP = cp >= ClearMin;
            if (!okN && !okP) return new ValveDecision(false, corner, -1, double2.zero, cn, cp, false);   // step 3: stays closed
            int target; bool tie = false;
            if (okN != okP) target = okN ? next : prev;       // DV-B03-1: only a side clear to 0.30 m is eligible
            else if (Math.Abs(cn - cp) <= TieEpsilon)
            {
                tie = true; target = _nextOnTie ? next : prev; _nextOnTie = !_nextOnTie;   // ties alternate (DV-G6-4)
            }
            else target = cn > cp ? next : prev;
            return new ValveDecision(true, corner, target, math.normalize(_lap.Corner(target) - ka), cn, cp, tie);
        }

        // Minimum of c.Clearance along a..b sampled every 0.05 m, both ends included (DV-G6-3).
        public static double SideClearance(IClearanceQuery c, double2 a, double2 b)
        {
            double len = math.length(b - a);
            int n = Math.Max(1, (int)Math.Ceiling(len / SampleStep - 1e-9));
            double min = double.PositiveInfinity;
            for (int i = 0; i <= n; i++)
            {
                double2 p = a + (double)i / n * (b - a);
                double v = c.Clearance((float2)p);
                if (v < min) min = v;
            }
            return min;
        }

        // MP §10.2.3 step 4 / A.2; kernel.md §2.4 (Rot(theta > 0) turns +x towards +z). The bearing of tile edge k seen from the
        // walker is k*pi/2 - theta; after U*Rot(alpha) it is k*pi/2 - theta - alpha = beta (DV-G6-5).
        public static double ReaimAngle(in Iso2 local, int exitEdge, double2 direction)
        {
            double theta = WalkerHeading.Of(local);
            double beta = Math.Atan2(direction.y, direction.x);
            return Wrap(exitEdge * Math.PI / 2 - theta - beta);
        }

        internal static double Wrap(double a)
        {
            a = Math.IEEERemainder(a, 2 * Math.PI);          // (-pi, pi]
            if (a <= -Math.PI) a += 2 * Math.PI;
            return a;
        }
    }
}
