using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    // Double reference for AtlasWarp.hlsl, built from the kernel (P1-B03).
    public static class AtlasWarpReference
    {
        // kappa must be -1 or +1; mode "none" is the ordinary transform and has no reference.
        public static double3 Warp(double3 positionOS, in Iso2 anchor, double kappa, double hypL, double3 headPosWS)
        {
            if (kappa != -1 && kappa != 1) throw new ArgumentException("kappa must be -1 or +1", nameof(kappa));
            double3 p = Geometry2.Exp(new double2(positionOS.x, positionOS.z) / hypL, kappa);       // MP A.2
            double2 t = Geometry2.Log(math.mul(anchor.M, p), kappa) * hypL;                         // MP §4.6
            return new double3(headPosWS.x + t.x, positionOS.y, headPosWS.z + t.y);
        }
    }
}
