// Great-circle check (P2-B11): three directions on the unit sphere, joined by the three minor great-circle arcs, and the
// interior angle sum of the spherical triangle they bound. Plain C#, System only, no allocation.
//
// Angles come from the tangent vectors at each vertex. The area comes from a different formula (Van Oosterom and
// Strakhov), so "angle sum = 180 degrees + 180 degrees * area / pi" is a real check and not an identity:
//   E = 2 atan2(|a . (b x c)|, 1 + a.b + b.c + c.a)        (E in steradians, a, b, c unit vectors)
using System;

namespace Atlas.Sky
{
    public readonly struct SphericalTriangle
    {
        /// <summary>False when two vertices coincide or are antipodal (the arc is then undefined) or a vector is zero or not finite.</summary>
        public readonly bool Valid;
        public readonly double AngleADeg, AngleBDeg, AngleCDeg;
        public readonly double AngleSumDeg;
        public readonly double AreaSr;        // steradians, independent formula
        public SphericalTriangle(bool valid, double a, double b, double c, double areaSr)
        {
            Valid = valid; AngleADeg = a; AngleBDeg = b; AngleCDeg = c; AngleSumDeg = a + b + c; AreaSr = areaSr;
        }
        /// <summary>180 degrees + 180 degrees * area / pi.</summary>
        public double ExpectedSumDeg { get { return 180.0 + 180.0 * AreaSr / Math.PI; } }
        /// <summary>|angle sum - expected sum|, in degrees.</summary>
        public double ConsistencyErrorDeg { get { return Math.Abs(AngleSumDeg - ExpectedSumDeg); } }
        public GcCheck ToCheck() { return new GcCheck(AngleSumDeg); }
    }

    public static class GreatCircle
    {
        const double Tiny = 1e-9;

        /// <summary>Length in radians of the minor arc between two directions (any non-zero length).</summary>
        public static double ArcLengthRad(Dir3 a, Dir3 b)
        {
            Unit(a, out double ax, out double ay, out double az);
            Unit(b, out double bx, out double by, out double bz);
            double cx = ay * bz - az * by, cy = az * bx - ax * bz, cz = ax * by - ay * bx;
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), ax * bx + ay * by + az * bz);
        }

        public static SphericalTriangle Triangle(Dir3 a, Dir3 b, Dir3 c)
        {
            Unit(a, out double ax, out double ay, out double az);
            Unit(b, out double bx, out double by, out double bz);
            Unit(c, out double cx, out double cy, out double cz);
            if (!(IsFinite(ax + ay + az + bx + by + bz + cx + cy + cz))) return Invalid();
            double ab = ax * bx + ay * by + az * bz, bc = bx * cx + by * cy + bz * cz, ca = cx * ax + cy * ay + cz * az;
            // coincident (dot = 1) or antipodal (dot = -1) vertices have no unique arc
            if (Math.Abs(ab) > 1.0 - Tiny || Math.Abs(bc) > 1.0 - Tiny || Math.Abs(ca) > 1.0 - Tiny) return Invalid();

            double angA = VertexAngle(ax, ay, az, bx, by, bz, cx, cy, cz);
            double angB = VertexAngle(bx, by, bz, cx, cy, cz, ax, ay, az);
            double angC = VertexAngle(cx, cy, cz, ax, ay, az, bx, by, bz);

            double dx = by * cz - bz * cy, dy = bz * cx - bx * cz, dz = bx * cy - by * cx;
            double det = Math.Abs(ax * dx + ay * dy + az * dz);
            double area = 2.0 * Math.Atan2(det, 1.0 + ab + bc + ca);
            return new SphericalTriangle(true, angA * SkyFrame.Deg, angB * SkyFrame.Deg, angC * SkyFrame.Deg, area);
        }

        /// <summary>The gc_check payload for three marked directions, or false when the triangle is not valid.</summary>
        public static bool TryCheck(Dir3 a, Dir3 b, Dir3 c, out GcCheck check)
        {
            SphericalTriangle t = Triangle(a, b, c);
            check = t.ToCheck();
            return t.Valid;
        }

        // Angle at vertex p between the arcs p->q and p->r: the angle between the projections of q and r on the tangent plane at p.
        static double VertexAngle(double px, double py, double pz, double qx, double qy, double qz, double rx, double ry, double rz)
        {
            double dq = px * qx + py * qy + pz * qz, dr = px * rx + py * ry + pz * rz;
            double t1x = qx - dq * px, t1y = qy - dq * py, t1z = qz - dq * pz;
            double t2x = rx - dr * px, t2y = ry - dr * py, t2z = rz - dr * pz;
            double cx = t1y * t2z - t1z * t2y, cy = t1z * t2x - t1x * t2z, cz = t1x * t2y - t1y * t2x;
            return Math.Atan2(Math.Sqrt(cx * cx + cy * cy + cz * cz), t1x * t2x + t1y * t2y + t1z * t2z);
        }

        static void Unit(Dir3 v, out double x, out double y, out double z)
        {
            double n = Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
            if (!(n > 0.0) || double.IsInfinity(n)) { x = double.NaN; y = double.NaN; z = double.NaN; return; }
            x = v.X / n; y = v.Y / n; z = v.Z / n;
        }

        static bool IsFinite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }

        static SphericalTriangle Invalid() { return new SphericalTriangle(false, double.NaN, double.NaN, double.NaN, double.NaN); }
    }
}
