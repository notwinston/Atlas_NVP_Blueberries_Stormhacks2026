using System;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    // Convention (Master Plan A.1): a point is double3 (x, z, w) stored in .x .y .z; .y is the floor-plane z, not height,
    // and .z is the hyperboloid/sphere w. A tangent step is double2 (x, z) in units of L (or R). Base point o = (0,0,1).
    // Matrices act on column vectors. Kappa is -1, 0 or +1; 0 is neutral in products so Rot and Identity compose with every geometry.
    // default(Iso2) is the zero matrix, not the identity.
    public readonly struct Iso2 : IEquatable<Iso2>
    {
        public readonly double3x3 M;
        public readonly double Kappa;

        public Iso2(double3x3 m, double kappa)
        {
            M = m;
            Kappa = kappa < 0 ? -1 : kappa > 0 ? 1 : 0;
        }

        /// <summary>Raw entries, row-major: Mrc is row r, column c (index 0 = x, 1 = z, 2 = w). kappa must be exactly -1, 0 or +1,
        /// else ArgumentOutOfRangeException; the entries are not checked.</summary>
        public Iso2(double m00, double m01, double m02,
                    double m10, double m11, double m12,
                    double m20, double m21, double m22, double kappa)
        {
            Curvature.Require(kappa);
            M = new double3x3(m00, m01, m02, m10, m11, m12, m20, m21, m22);
            Kappa = kappa;
        }

        // Row-major entries of M, the order of State.xform (kernel.md §4.5).
        public double M00 => M.c0.x;
        public double M01 => M.c1.x;
        public double M02 => M.c2.x;
        public double M10 => M.c0.y;
        public double M11 => M.c1.y;
        public double M12 => M.c2.y;
        public double M20 => M.c0.z;
        public double M21 => M.c1.z;
        public double M22 => M.c2.z;

        public static readonly Iso2 Identity = new Iso2(double3x3.identity, 0);

        /// <summary>The identity tagged with the given curvature (exactly -1, 0 or +1).</summary>
        public static Iso2 IdentityFor(double kappa)
        {
            Curvature.Require(kappa);
            return new Iso2(double3x3.identity, kappa);
        }

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

        /// <summary>The same rotation, tagged with the given curvature (exactly -1, 0 or +1).</summary>
        public static Iso2 Rot(double angle, double kappa)
        {
            Curvature.Require(kappa);
            return new Iso2(Rot(angle).M, kappa);
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
                // A matrix with no rotation part has no nearest group element (lane-b-kernel.md §2.2).
                double n = math.length(c0.xy);
                if (!(n >= 1e-12) || double.IsInfinity(n))
                    throw new InvalidOperationException("Atlas.Kernel: the matrix has no usable rotation part and cannot be orthonormalised.");
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

        /// <summary>M · p for a model point p = (x, z, w).</summary>
        public double3 Apply(double3 p) => math.mul(M, p);

        /// <summary>The image of the base point (0, 0, 1): the third column (M02, M12, M22).</summary>
        public double3 Position => M.c2;

        /// <summary>Largest absolute entry of the group residual; 0 for an exact group element.</summary>
        public double Defect()
        {
            // Math.Max keeps a NaN, so a broken matrix never reports a small defect.
            double3 c0 = M.c0, c1 = M.c1, c2 = M.c2;
            double worst;
            if (Kappa == 0)
            {
                worst = Math.Abs(c0.x * c0.x + c0.y * c0.y - 1.0);
                worst = Math.Max(worst, Math.Abs(c0.x * c1.x + c0.y * c1.y));
                worst = Math.Max(worst, Math.Abs(c1.x * c1.x + c1.y * c1.y - 1.0));
                worst = Math.Max(worst, Math.Abs(c0.z));
                worst = Math.Max(worst, Math.Abs(c1.z));
                worst = Math.Max(worst, Math.Abs(c2.z - 1.0));
                return worst;
            }
            // M^T J M - J with J = diag(1, 1, kappa): eta in H^2, the identity on S^2.
            double k = Kappa;
            worst = Math.Abs(Ip(c0, c0, k) - 1.0);
            worst = Math.Max(worst, Math.Abs(Ip(c1, c1, k) - 1.0));
            worst = Math.Max(worst, Math.Abs(Ip(c2, c2, k) - k));
            worst = Math.Max(worst, Math.Abs(Ip(c0, c1, k)));
            worst = Math.Max(worst, Math.Abs(Ip(c0, c2, k)));
            worst = Math.Max(worst, Math.Abs(Ip(c1, c2, k)));
            return worst;
        }

        public double3x3 ToDouble3x3() => M;

        /// <summary>The double-to-float cast for upload. Use it last (kernel.md §3 rule 1).</summary>
        public float3x3 ToFloat3x3() => new float3x3((float3)M.c0, (float3)M.c1, (float3)M.c2);

        /// <summary>Upload form for the per-material _Anchor: Mrc at row r, column c; row 3 and column 3 from the identity.</summary>
        public float4x4 ToFloat4x4() => new float4x4(
            new float4((float3)M.c0, 0f), new float4((float3)M.c1, 0f), new float4((float3)M.c2, 0f), new float4(0f, 0f, 0f, 1f));

        /// <summary>Writes M00, M01, M02, M10, ... M22 into the first nine elements (State.xform, kernel.md §4.5).</summary>
        public void CopyTo(Span<double> rowMajor9)
        {
            if (rowMajor9.Length < 9)
            {
                throw new ArgumentException("Atlas.Kernel: nine elements are needed.", nameof(rowMajor9));
            }
            rowMajor9[0] = M00; rowMajor9[1] = M01; rowMajor9[2] = M02;
            rowMajor9[3] = M10; rowMajor9[4] = M11; rowMajor9[5] = M12;
            rowMajor9[6] = M20; rowMajor9[7] = M21; rowMajor9[8] = M22;
        }

        /// <summary>Reads the nine entries in wire order (Appendix B.1, State.xform). kappa is not on the wire:
        /// the receiver passes the curvature of the space, exactly -1, 0 or +1.</summary>
        public static Iso2 FromRowMajor(ReadOnlySpan<double> rowMajor9, double kappa)
        {
            Curvature.Require(kappa);
            if (rowMajor9.Length < 9)
            {
                throw new ArgumentException("Atlas.Kernel: nine elements are needed.", nameof(rowMajor9));
            }
            return new Iso2(rowMajor9[0], rowMajor9[1], rowMajor9[2],
                            rowMajor9[3], rowMajor9[4], rowMajor9[5],
                            rowMajor9[6], rowMajor9[7], rowMajor9[8], kappa);
        }

        /// <summary>All ten doubles equal.</summary>
        public bool Equals(Iso2 other) =>
            M.c0.x == other.M.c0.x && M.c0.y == other.M.c0.y && M.c0.z == other.M.c0.z &&
            M.c1.x == other.M.c1.x && M.c1.y == other.M.c1.y && M.c1.z == other.M.c1.z &&
            M.c2.x == other.M.c2.x && M.c2.y == other.M.c2.y && M.c2.z == other.M.c2.z &&
            Kappa == other.Kappa;

        public override bool Equals(object obj) => obj is Iso2 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = M00.GetHashCode();
                h = h * 31 + M01.GetHashCode();
                h = h * 31 + M02.GetHashCode();
                h = h * 31 + M10.GetHashCode();
                h = h * 31 + M11.GetHashCode();
                h = h * 31 + M12.GetHashCode();
                h = h * 31 + M20.GetHashCode();
                h = h * 31 + M21.GetHashCode();
                h = h * 31 + M22.GetHashCode();
                h = h * 31 + Kappa.GetHashCode();
                return h;
            }
        }

        /// <summary>For logs only: rows, round-trip format, invariant culture.</summary>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture,
                "[[{0:R}, {1:R}, {2:R}], [{3:R}, {4:R}, {5:R}], [{6:R}, {7:R}, {8:R}]] kappa={9:R}",
                M00, M01, M02, M10, M11, M12, M20, M21, M22, Kappa);
        }
    }
}
