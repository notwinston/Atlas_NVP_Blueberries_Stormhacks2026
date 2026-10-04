using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Sky
{
    /// <summary>
    /// Geodesic triangle on the unit sphere S2 (MP App. A.1/A.2, kappa = +1; beat g). Stable atan2 forms, no acos (brief §11.13).
    /// Inputs are unit vectors.
    /// </summary>
    public static class SphericalTriangle
    {
        /// <summary>Angle at a between the tangent vectors b - (a.b)a and c - (a.c)a: atan2(|a.(b x c)|, b.c - (a.b)(a.c)).</summary>
        public static double VertexAngleRad(double3 a, double3 b, double3 c) =>
            Math.Atan2(Math.Abs(math.dot(a, math.cross(b, c))), math.dot(b, c) - math.dot(a, b) * math.dot(a, c));

        public static double AngleSumDeg(double3 a, double3 b, double3 c) =>
            (VertexAngleRad(a, b, c) + VertexAngleRad(b, c, a) + VertexAngleRad(c, a, b)) * 180.0 / Math.PI;

        /// <summary>Van Oosterom-Strackee (D14): E = 2 atan2(|a.(b x c)|, 1 + a.b + b.c + c.a), steradians. Independent of the angles.</summary>
        public static double AreaSr(double3 a, double3 b, double3 c) =>
            2.0 * Math.Atan2(Math.Abs(math.dot(a, math.cross(b, c))), 1.0 + math.dot(a, b) + math.dot(b, c) + math.dot(c, a));

        /// <summary>180 + area.180/pi (MP §10.2.2 beat g; App. A.2 Gauss-Bonnet with R = 1).</summary>
        public static double GirardSumDeg(double areaSr) => 180.0 + areaSr * 180.0 / Math.PI;
    }
}
