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
            foreach (var w in room.walls)
            {
                double2 a = new double2(w.a.x, w.a.z), b = new double2(w.b.x, w.b.z);
                double2 ab = b - a; double t = math.clamp(math.dot(p - a, ab) / math.dot(ab, ab), 0, 1);
                best = Math.Min(best, math.length(p - (a + t * ab)));
            }
            if (room.objects != null)
                foreach (var o in room.objects)
                {
                    double c = Math.Cos(o.yaw), s = Math.Sin(o.yaw);
                    double wx = p.x - o.position.x, wz = p.y - o.position.z;
                    double lx = wx * c - wz * s, lz = wx * s + wz * c;   // rotate by -yaw (Unity yaw: +z turns towards +x)
                    double dx = Math.Max(Math.Abs(lx) - o.size.x / 2, 0), dz = Math.Max(Math.Abs(lz) - o.size.z / 2, 0);
                    best = Math.Min(best, Math.Sqrt(dx * dx + dz * dz));
                }
            return best;
        }
    }
}
