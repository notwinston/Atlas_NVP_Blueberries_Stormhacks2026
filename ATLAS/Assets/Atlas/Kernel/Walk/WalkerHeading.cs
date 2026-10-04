using System;

namespace Atlas.Kernel
{
    // P2-B03. MP A.2: U = Boost(t)*Rot(theta), t = Log(U*o, kappa), R = Boost(t, kappa)^-1 * U, theta = atan2(R.c0.y, R.c0.x).
    // Same decomposition as HolonomyRig.Rescale (P1-B02) and CellWalker.TurnFromCurvedWalk (P1-B10).
    public static class WalkerHeading
    {
        // kappa is taken from local.Kappa; result in (-pi, pi].
        public static double Of(in Iso2 local)
        {
            double k = local.Kappa;
            var t = Geometry2.Log(local.M.c2, k);
            var r = Iso2.Boost(t, k).Inverse() * new Iso2(local.M, k);
            return Math.Atan2(r.M.c0.y, r.M.c0.x);
        }
    }
}
