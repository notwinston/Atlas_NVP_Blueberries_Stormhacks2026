using System;
using Unity.Mathematics;

namespace Atlas.World.Lint
{
    // Master Plan §8.5 draw rule: (a) tiles within rho + r_c enter the list; (b) a tile is drawn iff its circumscribed
    // disc is not outside any of the four view-frustum side planes (floor-plane projection of a horizontal radius r_c L).
    static class DrawRuleSim
    {
        public const double CircumRadius = 0.8425; // §8.5 literal

        // c[i] = floor centre in the eye frame (X = x, Y = up, Z = z), metres. Returns the drawn count and sums tris[i].
        public static int Drawn(double3[] c, long[] tris, int count, double thetaDeg, double psiDeg, double L, WorldLintSettings s, out long sumTris)
        {
            double th = math.radians(thetaDeg), ps = math.radians(psiDeg);
            double ct = Math.Cos(th), st = Math.Sin(th), cp = Math.Cos(ps), sp = Math.Sin(ps);
            var f = new double3(cp * ct, sp, cp * st);
            var rgt = new double3(-st, 0, ct);
            var up = new double3(-sp * ct, cp, -sp * st);
            double a = math.radians(s.HalfFovHDeg + s.MarginHDeg), b = math.radians(s.HalfFovVDeg + s.MarginVDeg);
            double sa = Math.Sin(a), ca = Math.Cos(a), sb = Math.Sin(b), cb = Math.Cos(b);
            var n0 = sa * f + ca * rgt; var n1 = sa * f - ca * rgt;
            var n2 = sb * f + cb * up; var n3 = sb * f - cb * up;
            double e0 = CircumRadius * L * math.length(n0.xz), e1 = CircumRadius * L * math.length(n1.xz);
            double e2 = CircumRadius * L * math.length(n2.xz), e3 = CircumRadius * L * math.length(n3.xz);
            int n = 0; sumTris = 0;
            for (int i = 0; i < count; i++)
            {
                var p = c[i];
                if (math.dot(n0, p) + e0 < 0 || math.dot(n1, p) + e1 < 0 || math.dot(n2, p) + e2 < 0 || math.dot(n3, p) + e3 < 0) continue;
                n++; sumTris += tris[i];
            }
            return n;
        }
    }
}
