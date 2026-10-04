using System;
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>
    /// Rotations of R^4 (R2 section D.5). A rotation in four dimensions turns a plane, not about an axis; the
    /// planes that contain w are the ones that change the shape of a slice (Master Plan section 5.1).
    /// Built in double and cast to float4x4 last, so a long run of small turns does not drift.
    /// </summary>
    public static class Rotation4D
    {
        /// <summary>Rotation by angle in the plane of axes i and j (0 = x, 1 = y, 2 = z, 3 = w), radians:
        /// p_i' = p_i cos - p_j sin, p_j' = p_i sin + p_j cos.</summary>
        public static double4x4 Plane(int i, int j, double angle)
        {
            if ((uint)i > 3u || (uint)j > 3u || i == j) throw new ArgumentException("A plane needs two different axes in 0..3.");

            math.sincos(angle, out double s, out double c);
            double4x4 m = double4x4.identity;
            double4 ci = default, cj = default;
            ci[i] = c; ci[j] = s;
            cj[i] = -s; cj[j] = c;
            m[i] = ci;
            m[j] = cj;
            return m;
        }

        /// <summary>The six plane rotations applied in the fixed order XY, XZ, XW, YZ, YW, ZW. Radians.</summary>
        public static double4x4 FromPlaneAngles(double xy, double xz, double xw, double yz, double yw, double zw)
        {
            double4x4 m = Plane(0, 1, xy);
            m = math.mul(Plane(0, 2, xz), m);
            m = math.mul(Plane(0, 3, xw), m);
            m = math.mul(Plane(1, 2, yz), m);
            m = math.mul(Plane(1, 3, yw), m);
            m = math.mul(Plane(2, 3, zw), m);
            return m;
        }

        /// <summary>The rotation that takes (1, 1, 1, 1) / 2 onto +w: the corner-first view of the tesseract.</summary>
        public static double4x4 CornerFirst()
        {
            // Rows of a Hadamard matrix, ordered for determinant +1. Every entry is exactly +-0.5.
            return math.transpose(new double4x4(
                new double4(0.5, -0.5, 0.5, -0.5),
                new double4(0.5, -0.5, -0.5, 0.5),
                new double4(0.5, 0.5, -0.5, -0.5),
                new double4(0.5, 0.5, 0.5, 0.5)));
        }

        /// <summary>Gram-Schmidt on the rows.</summary>
        public static double4x4 Orthonormalised(in double4x4 m)
        {
            double4x4 rows = math.transpose(m);
            double4 r0 = math.normalize(rows.c0);
            double4 r1 = math.normalize(rows.c1 - math.dot(rows.c1, r0) * r0);
            double4 r2 = rows.c2 - math.dot(rows.c2, r0) * r0;
            r2 = math.normalize(r2 - math.dot(r2, r1) * r1);
            double4 r3 = rows.c3 - math.dot(rows.c3, r0) * r0;
            r3 -= math.dot(r3, r1) * r1;
            r3 = math.normalize(r3 - math.dot(r3, r2) * r2);
            return math.transpose(new double4x4(r0, r1, r2, r3));
        }
    }
}
