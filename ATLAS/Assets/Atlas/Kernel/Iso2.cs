using System;
using System.Globalization;
using Unity.Mathematics;

namespace Atlas.Kernel
{
    /// <summary>
    /// An orientation-preserving isometry of the kernel plane: a 3 x 3 double matrix acting on
    /// model points (x, z, w), tagged with the curvature it belongs to (Master Plan section 6.5, Appendix A.2).
    /// </summary>
    /// <remarks>
    /// Points are column vectors and <c>a * b</c> applies b first. Only kappa = 0 exists in this build:
    /// a flat isometry is a rigid motion of the plane w = 1, so its last row is exactly (0, 0, 1).
    /// default(Iso2) is the zero matrix, not the identity.
    /// </remarks>
    public readonly struct Iso2 : IEquatable<Iso2>
    {
        // Row-major. Mrc = row r, column c. Index 0 = x, 1 = z, 2 = w.
        public readonly double M00, M01, M02;
        public readonly double M10, M11, M12;
        public readonly double M20, M21, M22;

        /// <summary>-1 hyperbolic, 0 flat, +1 spherical. Fixed at construction.</summary>
        public readonly double Kappa;

        /// <summary>Raw entries. Checks kappa with Curvature.Require; does not check the entries.</summary>
        public Iso2(double m00, double m01, double m02,
                    double m10, double m11, double m12,
                    double m20, double m21, double m22, double kappa)
        {
            Curvature.Require(kappa);
            M00 = m00; M01 = m01; M02 = m02;
            M10 = m10; M11 = m11; M12 = m12;
            M20 = m20; M21 = m21; M22 = m22;
            Kappa = kappa;
        }

        /// <summary>The flat identity (Kappa = 0).</summary>
        public static Iso2 Identity
        {
            get { return new Iso2(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, Curvature.Flat); }
        }

        /// <summary>The identity tagged with the given curvature.</summary>
        public static Iso2 IdentityFor(double kappa)
        {
            return new Iso2(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, kappa);
        }

        /// <summary>Move by a tangent vector at the base point. step is in kernel units
        /// (already divided by L): step.x along +x, step.y along +z.</summary>
        public static Iso2 Boost(double2 step, double kappa)
        {
            // Require throws for every curved value, so past the constructor this is the flat translation.
            return new Iso2(1.0, 0.0, step.x,
                            0.0, 1.0, step.y,
                            0.0, 0.0, 1.0, kappa);
        }

        /// <summary>Rotation about the base point by angle radians, anticlockwise seen from above
        /// (+x towards +z: a left turn). Flat-tagged (Kappa = 0).</summary>
        public static Iso2 Rot(double angle)
        {
            return Rot(angle, Curvature.Flat);
        }

        /// <summary>The same rotation, tagged with the given curvature.</summary>
        public static Iso2 Rot(double angle, double kappa)
        {
            double c = Math.Cos(angle);
            double s = Math.Sin(angle);
            return new Iso2(c, -s, 0.0,
                            s, c, 0.0,
                            0.0, 0.0, 1.0, kappa);
        }

        /// <summary>The inverse isometry. Same Kappa.</summary>
        public Iso2 Inverse()
        {
            // Flat: the rotation block is transposed and the translation carried back through it.
            return new Iso2(M00, M10, -(M00 * M02 + M10 * M12),
                            M01, M11, -(M01 * M02 + M11 * M12),
                            0.0, 0.0, 1.0, Kappa);
        }

        /// <summary>The nearest group element, by Gram-Schmidt. Same Kappa.</summary>
        public Iso2 Orthonormalised()
        {
            // Flat: rebuild the rotation from its first column. The translation is exact already and is
            // passed through bit for bit, which is what keeps a long walk 1 : 1.
            double n = Math.Sqrt(M00 * M00 + M10 * M10);
            if (!(n >= 1e-12) || double.IsInfinity(n))
            {
                throw new InvalidOperationException(
                    "Atlas.Kernel: the matrix has no usable rotation part and cannot be orthonormalised.");
            }
            double c = M00 / n;
            double s = M10 / n;
            // 0.0 - s rather than -s, so an unturned frame keeps +0.0 there.
            return new Iso2(c, 0.0 - s, M02,
                            s, c, M12,
                            0.0, 0.0, 1.0, Kappa);
        }

        /// <summary>Matrix product a·b: b is applied first. InvalidOperationException when the tags differ.</summary>
        public static Iso2 operator *(Iso2 a, Iso2 b)
        {
            if (a.Kappa != b.Kappa)
            {
                throw new InvalidOperationException(
                    "Atlas.Kernel: isometries of different curvature cannot be multiplied.");
            }
            return new Iso2(
                a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
                a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
                a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
                a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
                a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
                a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
                a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
                a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
                a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22,
                a.Kappa);
        }

        /// <summary>M · p for a model point p = (x, z, w).</summary>
        public double3 Apply(double3 p)
        {
            return new double3(
                M00 * p.x + M01 * p.y + M02 * p.z,
                M10 * p.x + M11 * p.y + M12 * p.z,
                M20 * p.x + M21 * p.y + M22 * p.z);
        }

        /// <summary>The image of the base point (0, 0, 1): the third column (M02, M12, M22).</summary>
        public double3 Position
        {
            get { return new double3(M02, M12, M22); }
        }

        /// <summary>Largest absolute entry of the group residual; 0 for an exact group element.</summary>
        public double Defect()
        {
            // Math.Max keeps a NaN, so a broken matrix never reports a small defect.
            double worst = Math.Abs(M00 * M00 + M10 * M10 - 1.0);
            worst = Math.Max(worst, Math.Abs(M00 * M01 + M10 * M11));
            worst = Math.Max(worst, Math.Abs(M01 * M01 + M11 * M11 - 1.0));
            worst = Math.Max(worst, Math.Abs(M20));
            worst = Math.Max(worst, Math.Abs(M21));
            worst = Math.Max(worst, Math.Abs(M22 - 1.0));
            return worst;
        }

        public double3x3 ToDouble3x3()
        {
            return new double3x3(M00, M01, M02,
                                 M10, M11, M12,
                                 M20, M21, M22);
        }

        /// <summary>The only double-to-float cast in the kernel. Use it last.</summary>
        public float3x3 ToFloat3x3()
        {
            return new float3x3((float)M00, (float)M01, (float)M02,
                                (float)M10, (float)M11, (float)M12,
                                (float)M20, (float)M21, (float)M22);
        }

        /// <summary>Upload form for the per-material _Anchor: Mrc at row r, column c; row 3 and column 3 from the identity.</summary>
        public float4x4 ToFloat4x4()
        {
            return new float4x4((float)M00, (float)M01, (float)M02, 0f,
                                (float)M10, (float)M11, (float)M12, 0f,
                                (float)M20, (float)M21, (float)M22, 0f,
                                0f, 0f, 0f, 1f);
        }

        /// <summary>Writes M00, M01, M02, M10, ... M22 into the first nine elements.</summary>
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

        /// <summary>Reads the nine entries in wire order (Appendix B.1, State.xform).</summary>
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
        public bool Equals(Iso2 other)
        {
            return M00 == other.M00 && M01 == other.M01 && M02 == other.M02
                && M10 == other.M10 && M11 == other.M11 && M12 == other.M12
                && M20 == other.M20 && M21 == other.M21 && M22 == other.M22
                && Kappa == other.Kappa;
        }

        public override bool Equals(object obj)
        {
            return obj is Iso2 other && Equals(other);
        }

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

        public override string ToString()
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            return string.Format(inv,
                "[[{0:R}, {1:R}, {2:R}], [{3:R}, {4:R}, {5:R}], [{6:R}, {7:R}, {8:R}]] kappa={9:R}",
                M00, M01, M02, M10, M11, M12, M20, M21, M22, Kappa);
        }
    }
}
