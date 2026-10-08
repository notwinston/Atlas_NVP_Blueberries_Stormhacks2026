using System;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// The scanned LiDAR mesh of one room, in the room frame (metres, +Y up). Kept in memory only, until the
    /// room's objects are built: it is never saved (standalone-phone plan D8, D10). Immutable after
    /// construction, so worker threads may read it; the arrays are taken as given, not copied, and nobody
    /// writes them afterwards. No UnityEngine type.
    /// </summary>
    public sealed class ScanMesh
    {
        /// <summary>Room frame, metres.</summary>
        public readonly float3[] Vertices;
        /// <summary>Three per triangle, each an index into Vertices.</summary>
        public readonly int[] Indices;
        /// <summary>null, or one per triangle: 0 None, 1 Wall, 2 Floor, 3 Ceiling, 4 Table, 5 Seat, 6 Window,
        /// 7 Door (the values of Atlas.Scan.Grid.SurfaceClass, which are ARKit's face classes).</summary>
        public readonly byte[] Classes;

        /// <summary>Class values, the same as Atlas.Scan.Grid.SurfaceClass.</summary>
        public const byte ClassNone = 0, ClassWall = 1, ClassFloor = 2, ClassCeiling = 3, ClassTable = 4,
            ClassSeat = 5, ClassWindow = 6, ClassDoor = 7;

        /// <summary>
        /// ArgumentNullException when vertices or indices is null. ArgumentException when the index count is not a
        /// multiple of three, an index is outside Vertices, or classes is not null and does not hold exactly one
        /// value per triangle.
        /// </summary>
        public ScanMesh(float3[] vertices, int[] indices, byte[] classes)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (indices == null) throw new ArgumentNullException(nameof(indices));
            if (indices.Length % 3 != 0)
                throw new ArgumentException("The index count must be a multiple of three.", nameof(indices));
            int n = vertices.Length;
            for (int k = 0; k < indices.Length; k++)
            {
                if ((uint)indices[k] >= (uint)n)
                    throw new ArgumentException("Index " + k + " points outside the vertices.", nameof(indices));
            }
            if (classes != null && classes.Length != indices.Length / 3)
                throw new ArgumentException("There must be exactly one class per triangle.", nameof(classes));
            Vertices = vertices;
            Indices = indices;
            Classes = classes;
        }

        public int TriangleCount { get { return Indices.Length / 3; } }
    }
}
