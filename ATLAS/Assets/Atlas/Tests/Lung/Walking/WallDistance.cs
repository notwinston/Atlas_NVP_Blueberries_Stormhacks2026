using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Lung.Walking.Tests
{
    // Minimum distance from head.xz to every wall segment and every object box footprint (DV-G6-21), in double.
    static class WallDistance
    {
        public static double Of(RoomModel room, double2 p)
        {
            double best = double.PositiveInfinity;
            foreach (var w in room.Walls)
            {
                double2 a = new double2(w.A.x, w.A.y), b = new double2(w.B.x, w.B.y);
                double2 ab = b - a; double t = math.clamp(math.dot(p - a, ab) / math.dot(ab, ab), 0, 1);
                best = Math.Min(best, math.length(p - (a + t * ab)));
            }
            foreach (var o in room.Objects)
            {
                double c = Math.Cos(o.Yaw), s = Math.Sin(o.Yaw);
                double wx = p.x - o.Base.x, wz = p.y - o.Base.z;
                double lx = wx * c - wz * s, lz = wx * s + wz * c;   // rotate by -yaw (Unity yaw: +z turns towards +x)
                double dx = Math.Max(Math.Abs(lx) - o.Size.x / 2, 0), dz = Math.Max(Math.Abs(lz) - o.Size.z / 2, 0);
                best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }
    }
}
