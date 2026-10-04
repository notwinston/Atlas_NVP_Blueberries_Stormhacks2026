using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // §4.6 update with kappa = +1 and R in place of L, on the six faces of CubeTiling (P1-B08). State is (Face, U); never a
    // global coordinate (§0.5 rule 7). The q = 3 walker behind HolonomyWalkerKernel: the mathematics of S4's walker, unchanged.
    public sealed class SphereWalker
    {
        static readonly double[] ex = { 1, 0, -1, 0 }, ey = { 0, 1, 0, -1 };
        static readonly double SinHysteresis = Math.Sin(KernelConstants.CrossHysteresis);
        static readonly double InvSqrt2 = 1.0 / Math.Sqrt(2.0);

        bool hasPrev;
        double2 prev;

        public double R { get; private set; } = double.NaN;
        public int Face { get; private set; }
        public Iso2 U { get; private set; } = new Iso2(double3x3.identity, 1);
        public WalkerPose Pose => new WalkerPose { Tile = CubeTiling.FaceId(Face), Local = U };
        public int LastCrossings { get; private set; }
        public int LastEdge { get; private set; } = -1;
        public double2 LastApplied { get; private set; }
        public bool LastRejected { get; private set; }

        public void Reset(in WalkerPose start, double radius)
        {
            int f = CubeTiling.FaceOf(start.Tile);
            if (f < 0) throw new ArgumentException("start tile is not a face id", nameof(start));
            R = radius;
            Face = f;
            U = new Iso2(start.Local.M, 1);
            hasPrev = false;
            LastApplied = double2.zero;
            LastRejected = false;
            LastCrossings = Cross();
        }

        /// <summary>The head's floor point at the Reset: the next Step integrates from it.</summary>
        public void SetPrevious(double2 headXZ) { prev = headXZ; hasPrev = true; }

        public void Step(double2 headXZ, bool trackingNormal)
        {
            double2 cur = headXZ;
            bool finite = math.all(math.isfinite(cur));
            if (!hasPrev)
            {
                if (finite) { prev = cur; hasPrev = true; }
                LastApplied = double2.zero; LastCrossings = 0; LastEdge = -1; LastRejected = !finite || !trackingNormal;
                return;
            }
            double2 d = finite ? cur - prev : double2.zero;
            LastRejected = !finite || !(math.length(d) <= KernelConstants.MaxStepMetres) || !trackingNormal;
            if (!(math.length(d) <= KernelConstants.MaxStepMetres) || !trackingNormal) d = double2.zero; // §4.6
            if (finite) prev = cur;
            LastApplied = d;
            // §4.6 U <- U * Boost(d/R); A.2 ordinary Gram-Schmidt
            U = new Iso2((U * Iso2.Boost(d / R, 1)).M, 1).Orthonormalised();
            LastCrossings = Cross();
        }

        // U <- U*Rot(alpha) about the walker, as HolonomyRig.Reaim: the position U*o and the previous head are kept.
        public void Reaim(double alpha)
        {
            U = new Iso2(math.mul(U.M, Iso2.Rot(alpha).M), 1).Orthonormalised();
        }

        // A.3 in-radius pi/4: sigma_k is sin of the signed distance past edge k
        int Cross()
        {
            int n = 0;
            LastEdge = -1;
            while (n < KernelConstants.MaxCrossingsPerStep)
            {
                double3 p = U.M.c2;
                int best = -1;
                double bs = SinHysteresis;
                for (int k = 0; k < 4; k++)
                {
                    double sg = (p.x * ex[k] + p.y * ey[k] - p.z) * InvSqrt2;
                    if (sg > bs) { bs = sg; best = k; }
                }
                if (best < 0) break;
                U = CubeTiling.Step(Face, best).Inverse() * U; // exact integer change of frame
                Face = CubeTiling.Neighbour(Face, best);
                LastEdge = best;
                n++;
            }
            return n;
        }
    }
}
