using System;
using Atlas.Scan.Grid;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>Synthetic scans for the route tests: what the sensors would hand to the scan flow.</summary>
    static class ScanTestRooms
    {
        public static readonly float2[] LOutline =
        {
            new float2(0f, 0f), new float2(4f, 0f), new float2(4f, 1.5f), new float2(2.5f, 1.5f), new float2(2.5f, 3f), new float2(0f, 3f),
        };

        public static float2[] Rectangle(float w, float d)
        {
            return new[] { new float2(0f, 0f), new float2(w, 0f), new float2(w, d), new float2(0f, d) };
        }

        /// <summary>A FloorScan input as the snapshot the sensors would give: mesh, classes, wall planes, floor height, boxes.</summary>
        public static ScanSnapshot Snapshot(FloorGridInput input, bool keepOutline = false)
        {
            return new ScanSnapshot
            {
                Vertices = input.Vertices, Indices = input.Indices, Classes = input.TriangleClasses,
                Walls = input.Walls, FloorHeight = input.FloorHeight, Boxes = input.ExternalBoxes,
                Path = input.WalkerPath, Walker = input.Walker, Outline = keepOutline ? input.Outline : null,
            };
        }

        /// <summary>The same turn and shift SyntheticRoom.Move applies, for a floor point.</summary>
        public static float2 Moved(float2 p, float angle, float2 shift)
        {
            float c = math.cos(angle), s = math.sin(angle);
            return new float2(c * p.x - s * p.y, s * p.x + c * p.y) + shift;
        }

        public static float2[] Moved(float2[] ring, float angle, float2 shift)
        {
            var moved = new float2[ring.Length];
            for (int k = 0; k < ring.Length; k++) moved[k] = Moved(ring[k], angle, shift);
            return moved;
        }

        /// <summary>Distance from a point to the nearest corner of a ring.</summary>
        public static float NearestCorner(float2 p, float2[] ring)
        {
            float best = float.MaxValue;
            for (int k = 0; k < ring.Length; k++) best = math.min(best, math.distance(p, ring[k]));
            return best;
        }

        /// <summary>A repeatable offset of at most amplitude per axis.</summary>
        public static float2 Jitter(System.Random random, float amplitude)
        {
            return new float2((float)(random.NextDouble() * 2.0 - 1.0), (float)(random.NextDouble() * 2.0 - 1.0)) * amplitude;
        }
    }
}
