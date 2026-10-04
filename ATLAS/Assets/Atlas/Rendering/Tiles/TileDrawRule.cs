// P1-B06: the CPU draw rule of Master Plan 8.5 (constants and plane test) and the widened view cone.
// The rule is the only culling wherever the hyperbolic warp is on (kernel.md 4.7); one test per frame serves both eyes.
using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    public static class TileDrawRule
    {
        public const double WidenSideDeg = 12.0;            // MP 8.5 / A.3 "widened by 12 deg to each side"
        public const double WidenUpDownDeg = 4.0;           // MP 8.5 "and 4 deg up and down"
        public const double DefaultHalfSideDeg = 50.0;      // MP 8.5 "about 50 deg to each side"
        public const double DefaultHalfUpDownDeg = 48.0;    // MP 8.5 "48 deg up and down"
        public const double FloorY = 0.0;                   // kernel.md 9.1: world y = 0 on the floor [P1-B06 D9]
        public const double ShellHeight = TileChart.PostHeight; // 2.6 m, tallest part of a B05 shell [P1-B06 D1]

        /// <summary>A.3, p = 4: acosh(cot(pi/4) cot(pi/q)); q = 5 gives 0.8424821, q = 6 gives 1.1462158.</summary>
        public static double CircumRadius(int q) => Math.Acosh(1.0 / Math.Tan(Math.PI / q));

        /// <summary>
        /// MP 8.5: "wholly outside a plane with inward normal n when n.c + r.|n_floor| &lt; 0", generalised to the vertical shell
        /// (the disc extruded up to height) by adding max(0, height * n.y) [P1-B06 D1]; with height 0 it is exactly the plan's formula.
        /// cRel is the disc centre relative to the eye, metres.
        /// </summary>
        public static bool ShellOutside(in double3 n, in double3 cRel, double r, double height)
            => math.dot(n, cRel) + r * math.length(n.xz) + Math.Max(0.0, height * n.y) < 0.0;
    }

    /// <summary>The four side planes of the view frustum widened by 12 deg sideways and 4 deg up/down (MP 8.5, A.3), world space.</summary>
    public readonly struct TileViewCone
    {
        public readonly double3 Eye, NLeft, NRight, NTop, NBottom;   // inward unit normals of the WIDENED side planes

        public TileViewCone(double3 eye, double3 nLeft, double3 nRight, double3 nTop, double3 nBottom)
        { Eye = eye; NLeft = nLeft; NRight = nRight; NTop = nTop; NBottom = nBottom; }

        // Brief 3.1; the same plane construction as P1-B07's DrawRuleSim. Rotation done in double so the normals are unit to 1e-15.
        public static TileViewCone FromHead(float3 headPosition, quaternion headRotation, double halfSideDeg, double halfUpDownDeg)
        {
            double4 q = math.normalize((double4)headRotation.value);
            double3 fwd = Rotate(q, new double3(0, 0, 1)), rgt = Rotate(q, new double3(1, 0, 0)), up = Rotate(q, new double3(0, 1, 0));
            double a = (halfSideDeg + TileDrawRule.WidenSideDeg) * Math.PI / 180.0;
            double b = (halfUpDownDeg + TileDrawRule.WidenUpDownDeg) * Math.PI / 180.0;
            double sa = Math.Sin(a), ca = Math.Cos(a), sb = Math.Sin(b), cb = Math.Cos(b);
            return new TileViewCone((double3)headPosition,
                sa * fwd + ca * rgt, sa * fwd - ca * rgt, sb * fwd - cb * up, sb * fwd + cb * up);
        }

        static double3 Rotate(double4 q, double3 v)
        {
            double3 u = q.xyz; double3 t = 2.0 * math.cross(u, v);
            return v + q.w * t + math.cross(u, t);
        }

        /// <summary>False iff the shell volume is wholly outside one of the four planes (MP 8.5 (b), D1).</summary>
        public bool ShellMayShow(in double3 floorCentreWorld, double radius, double height)
        {
            double3 c = floorCentreWorld - Eye;
            return !(TileDrawRule.ShellOutside(NLeft, c, radius, height) || TileDrawRule.ShellOutside(NRight, c, radius, height)
                  || TileDrawRule.ShellOutside(NTop, c, radius, height) || TileDrawRule.ShellOutside(NBottom, c, radius, height));
        }
    }
}
