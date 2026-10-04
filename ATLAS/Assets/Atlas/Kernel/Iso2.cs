using System;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Convention (Master Plan A.1): a point is double3 (x, z, w) stored in .x .y .z; .y is the floor-plane z, not height,
    // and .z is the hyperboloid/sphere w. A tangent step is double2 (x, z) in units of L (or R). Base point o = (0,0,1).
    // Matrices act on column vectors. Kappa is -1, 0 or +1; 0 is neutral in products so Rot and Identity compose with every geometry.
    public readonly struct Iso2
    {
        public readonly double3x3 M;
        public readonly double Kappa;

        public Iso2(double3x3 m, double kappa)
        {
            M = m;
            Kappa = kappa < 0 ? -1 : kappa > 0 ? 1 : 0;
        }

        public static readonly Iso2 Identity = new Iso2(double3x3.identity, 0);

        // Master Plan A.2: unified form exp([[0,0,dx],[0,0,dz],[-k dx,-k dz,0]]).
        public static Iso2 Boost(double2 step, double kappa)
        {
            double k = kappa < 0 ? -1 : kappa > 0 ? 1 : 0;
            double d = math.length(step);
            if (d == 0) return new Iso2(double3x3.identity, k);
            double2 u = step / d;
            double c, s, cm1;
            if (k < 0) { c = Math.Cosh(d); s = Math.Sinh(d); double sh = Math.Sinh(d / 2); cm1 = 2 * sh * sh; } // cm1 = cosh d - 1, A.2
            else if (k > 0) { c = Math.Cos(d); s = Math.Sin(d); double sh = Math.Sin(d / 2); cm1 = -2 * sh * sh; } // cm1 = cos d - 1
            else { c = 1; s = d; cm1 = 0; }
            var m = new double3x3(
                1 + cm1 * u.x * u.x, cm1 * u.x * u.y, s * u.x,
                cm1 * u.x * u.y, 1 + cm1 * u.y * u.y, s * u.y,
                -k * s * u.x, -k * s * u.y, c);
            return new Iso2(m, k);
        }

        // Master Plan A.5: Rot(+90 deg) = [[0,-1,0],[1,0,0],[0,0,1]].
        public static Iso2 Rot(double angle)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            return new Iso2(new double3x3(c, -s, 0, s, c, 0, 0, 0, 1), 0);
        }

        public Iso2 Inverse()
        {
            var m = M;
            if (Kappa < 0) // eta * M^T * eta (A.2, §6.5)
                return new Iso2(new double3x3(
                    m.c0.x, m.c0.y, -m.c0.z,
                    m.c1.x, m.c1.y, -m.c1.z,
                    -m.c2.x, -m.c2.y, m.c2.z), Kappa);
            if (Kappa > 0) return new Iso2(math.transpose(m), Kappa); // orthogonal on S^2 (A.2)
            // flat: [[A^T, -A^T t], [0, 0, 1]]
            var at = math.transpose(new double2x2(m.c0.xy, m.c1.xy));
            double2 t = m.c2.xy;
            double2 nt = -math.mul(at, t);
            return new Iso2(new double3x3(
                at.c0.x, at.c1.x, nt.x,
                at.c0.y, at.c1.y, nt.y,
                0, 0, 1), 0);
        }

        // Master Plan A.2 "Renormalise" (eta-Gram-Schmidt, position column first as in R3 fixmatrix).
        public Iso2 Orthonormalised()
        {
            double3 c0 = M.c0, c1 = M.c1, c2 = M.c2;
            if (Kappa == 0)
            {
                double2 a = math.normalize(c0.xy);
                double2 b = c1.xy - math.dot(c1.xy, a) * a;
                b = math.normalize(b);
                return new Iso2(new double3x3(a.x, b.x, c2.x, a.y, b.y, c2.y, 0, 0, 1), 0);
            }
            double k = Kappa;
            c2 /= Math.Sqrt(k * Ip(c2, c2, k));
            c0 -= k * Ip(c0, c2, k) * c2;
            c0 /= Math.Sqrt(Ip(c0, c0, k));
            c1 -= k * Ip(c1, c2, k) * c2 + Ip(c1, c0, k) * c0;
            c1 /= Math.Sqrt(Ip(c1, c1, k));
            return new Iso2(new double3x3(c0, c1, c2), Kappa);
        }

        static double Ip(double3 a, double3 b, double k) => a.x * b.x + a.y * b.y + k * a.z * b.z;

        public static Iso2 operator *(Iso2 a, Iso2 b)
        {
            if (a.Kappa != 0 && b.Kappa != 0 && a.Kappa != b.Kappa)
                throw new InvalidOperationException("Iso2 product across different curvatures");
            return new Iso2(math.mul(a.M, b.M), a.Kappa != 0 ? a.Kappa : b.Kappa);
        }
    }
}
