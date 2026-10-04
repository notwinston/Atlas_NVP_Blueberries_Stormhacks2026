using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.World
{
    // Lane-B plain input for the Home set (Master Plan §3.4, §4.6): the room outline in metres in tile zero's chart.
    public sealed class RoomFootprint
    {
        readonly double2[] outline;

        public RoomFootprint(double curvatureRadiusL, IReadOnlyList<double2> outlineMetres)
        {
            if (!(curvatureRadiusL > 0)) throw new ArgumentException("L must be positive", nameof(curvatureRadiusL));
            if (outlineMetres == null || outlineMetres.Count < 3) throw new ArgumentException("at least 3 outline points", nameof(outlineMetres));
            L = curvatureRadiusL;
            outline = new double2[outlineMetres.Count];
            for (int i = 0; i < outline.Length; i++) outline[i] = outlineMetres[i];
        }

        public double L { get; }
        public IReadOnlyList<double2> Outline => outline;

        // P1-B11 [B11]: the polygon is room.Outline, wall to wall (metres, room frame = tile zero's chart, MP §4.6);
        // without an outline the `A` points of room.Walls in order, then the walkable outline; failing that the free
        // rectangle +-Wf/2, +-Df/2. A float2 floor point is (x, z).
        public static RoomFootprint FromRoomModel(RoomModel room, double L)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var pts = new List<double2>();
            if (room.Outline != null && room.Outline.Length >= 3)
                foreach (var p in room.Outline) pts.Add(new double2(p.x, p.y));
            else if (room.Walls != null && room.Walls.Length >= 3)
                foreach (var w in room.Walls) pts.Add(new double2(w.A.x, w.A.y));
            else if (room.Walkable != null && room.Walkable.Outer != null && room.Walkable.Outer.Length >= 3)
                foreach (var p in room.Walkable.Outer) pts.Add(new double2(p.x, p.y));
            else return Rectangle(room.FreeRect.Wf, room.FreeRect.Df, L);
            return new RoomFootprint(L, pts);
        }

        public static RoomFootprint Rectangle(double width, double depth, double curvatureRadiusL)
        {
            double w = width / 2, d = depth / 2;
            return new RoomFootprint(curvatureRadiusL, new[] { new double2(-w, -d), new double2(w, -d), new double2(w, d), new double2(-w, d) });
        }
    }
}
