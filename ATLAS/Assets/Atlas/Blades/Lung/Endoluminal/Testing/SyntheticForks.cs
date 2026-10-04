using System;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Endoluminal.Testing
{
    // Deterministic forks from the plan generator's constants (mirrors tools/placeholders/make_fixtures_d.py without randomness):
    // radius ratio 0.794, branching angle 37 deg, plane rotated 90 deg per generation, length = 3 diameters, R0 = 9 mm.
    public static class SyntheticForks
    {
        public const double R0 = 9.0, Ratio = 0.794, AngleDeg = 37.0;

        struct Node { public string Address; public double Radius, Len; public double3 Pos, Dir, U; }

        public static ForkView[] Route(int forks) => Route(forks, new double3(1, 0, 0));

        // Route: fork i's chosen child is L for even i, R for odd i (fork addresses "", "L", "LR", "LRL", ...).
        public static ForkView[] Route(int forks, double3 rootPlaneAxis)
        {
            if (forks < 1) throw new ArgumentOutOfRangeException(nameof(forks));
            var result = new ForkView[forks];
            var node = new Node { Address = "", Radius = R0, Len = 2 * R0 * 3.0, Pos = double3.zero, Dir = new double3(0, 0, -1), U = rootPlaneAxis };
            double ang = AngleDeg * Math.PI / 180.0;
            for (int i = 0; i < forks; i++)
            {
                double3 end = node.Pos + node.Dir * node.Len;
                var kids = new Node[2];
                for (int k = 0; k < 2; k++)
                {
                    double sgn = k == 0 ? 1.0 : -1.0;
                    double3 c = math.normalize(node.Dir * Math.Cos(ang) + node.U * (sgn * Math.Sin(ang)));
                    double3 p = math.normalize(node.U - math.dot(node.U, c) * c);
                    double3 uc = math.normalize(math.cross(c, p));
                    double rad = node.Radius * Ratio;
                    kids[k] = new Node { Address = node.Address + (k == 0 ? "L" : "R"), Radius = rad, Len = 2 * rad * 3.0, Pos = end, Dir = c, U = uc };
                }
                result[i] = new ForkView(Seg(node), Seg(kids[0]), Seg(kids[1]));
                node = kids[i % 2];
            }
            return result;
        }

        static AirwaySegmentMm Seg(Node n) => new AirwaySegmentMm(n.Address, n.Pos, n.Dir, n.Len, n.Radius);
    }
}
