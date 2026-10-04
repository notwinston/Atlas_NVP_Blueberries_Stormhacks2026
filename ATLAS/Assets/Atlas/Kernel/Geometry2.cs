using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    public static class Geometry2
    {
        public static readonly double3 BasePoint = new double3(0, 0, 1);

        // Master Plan A.2.
        public static double3 Exp(double2 t, double kappa)
        {
            if (kappa == 0) return new double3(t.x, t.y, 1);
            double r = math.length(t);
            if (r == 0) return BasePoint;
            double2 dir = t / r;
            return kappa < 0
                ? new double3(Math.Sinh(r) * dir, Math.Cosh(r))
                : new double3(Math.Sin(r) * dir, Math.Cos(r));
        }

        public static double2 Log(double3 p, double kappa)
        {
            if (kappa == 0) return p.xy / p.z;
            double n = math.length(p.xy);
            if (n == 0) return kappa > 0 && p.z < 0 ? new double2(Math.PI, 0) : double2.zero;
            // spherical r = atan2(n, w): equals A.2's acos(w) on the unit sphere, without its cancellation (deviation D4)
            double r = kappa < 0 ? Math.Asinh(n) : Math.Atan2(n, p.z);
            return p.xy * (r / n);
        }

        public static double Distance(double3 p, double3 q, double kappa)
        {
            if (kappa == 0) return math.length(p.xy / p.z - q.xy / q.z);
            if (kappa < 0)
            {
                // <p-q,p-q>_eta = 4 sinh^2(d/2) (equals A.2's cosh d = -<p,q>, stable near 0)
                double3 d = p - q;
                double ip = d.x * d.x + d.y * d.y - d.z * d.z;
                return 2 * Math.Asinh(0.5 * Math.Sqrt(Math.Max(0, ip)));
            }
            return Math.Atan2(math.length(math.cross(p, q)), math.dot(p, q)); // cos d = p.q (A.2)
        }
    }
}
