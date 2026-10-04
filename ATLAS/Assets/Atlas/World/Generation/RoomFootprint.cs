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

        public static RoomFootprint Rectangle(double width, double depth, double curvatureRadiusL)
        {
            double w = width / 2, d = depth / 2;
            return new RoomFootprint(curvatureRadiusL, new[] { new double2(-w, -d), new double2(w, -d), new double2(w, d), new double2(-w, d) });
        }
    }
}
