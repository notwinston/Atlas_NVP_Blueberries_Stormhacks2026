using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>Exponential and logarithm maps at the base point, and distance (Appendix A.2). Kernel units.</summary>
    public static class KernelMaps
    {
        /// <summary>The base point o = (0, 0, 1) in every geometry.</summary>
        public static readonly double3 BasePoint = new double3(0.0, 0.0, 1.0);

        /// <summary>Tangent vector at o (direction and distance) to the point reached.</summary>
        public static double3 Exp(double2 t, double kappa)
        {
            Curvature.Require(kappa);
            return new double3(t.x, t.y, 1.0);
        }

        /// <summary>Point to the tangent vector at o that reaches it. Inverse of Exp.</summary>
        public static double2 Log(double3 p, double kappa)
        {
            Curvature.Require(kappa);
            return new double2(p.x, p.y);
        }

        /// <summary>Geodesic distance between two model points.</summary>
        public static double Distance(double3 p, double3 q, double kappa)
        {
            Curvature.Require(kappa);
            double dx = p.x - q.x;
            double dz = p.y - q.y;
            return Math.Sqrt(dx * dx + dz * dz);
        }
    }
}
