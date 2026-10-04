using System.Collections.Generic;
using Atlas.Scan.Grid;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Objects
{
    /// <summary>
    /// Scanned furniture for the profile and facing tests: triangles written in a box's local frame (origin
    /// at the centre of the base, +Y up) and placed in the room by a base point and a Unity yaw.
    /// </summary>
    sealed class ScannedShapes
    {
        readonly List<float3> _vertices = new List<float3>();
        readonly List<int> _indices = new List<int>();
        readonly List<SurfaceClass> _classes = new List<SurfaceClass>();

        public float3[] Vertices { get { return _vertices.ToArray(); } }
        public int[] Indices { get { return _indices.ToArray(); } }
        public SurfaceClass[] Classes { get { return _classes.ToArray(); } }

        /// <summary>A closed box, twelve triangles. The top is split along the diagonal from (lo.x, lo.z) to (hi.x, hi.z).</summary>
        public ScannedShapes AddBox(float3 lo, float3 hi, SurfaceClass cls = SurfaceClass.None)
        {
            Level(lo.x, lo.z, hi.x, hi.z, hi.y, cls);
            Level(lo.x, lo.z, hi.x, hi.z, lo.y, cls);
            Quad(new float3(lo.x, lo.y, lo.z), new float3(hi.x, lo.y, lo.z), new float3(hi.x, hi.y, lo.z), new float3(lo.x, hi.y, lo.z), cls);
            Quad(new float3(lo.x, lo.y, hi.z), new float3(hi.x, lo.y, hi.z), new float3(hi.x, hi.y, hi.z), new float3(lo.x, hi.y, hi.z), cls);
            Quad(new float3(lo.x, lo.y, lo.z), new float3(lo.x, lo.y, hi.z), new float3(lo.x, hi.y, hi.z), new float3(lo.x, hi.y, lo.z), cls);
            Quad(new float3(hi.x, lo.y, lo.z), new float3(hi.x, lo.y, hi.z), new float3(hi.x, hi.y, hi.z), new float3(hi.x, hi.y, lo.z), cls);
            return this;
        }

        /// <summary>A level rectangle at height y.</summary>
        public ScannedShapes Level(float x0, float z0, float x1, float z1, float y, SurfaceClass cls = SurfaceClass.None)
        {
            return Quad(new float3(x0, y, z0), new float3(x1, y, z0), new float3(x1, y, z1), new float3(x0, y, z1), cls);
        }

        /// <summary>Two triangles a-b-c and a-c-d.</summary>
        public ScannedShapes Quad(float3 a, float3 b, float3 c, float3 d, SurfaceClass cls = SurfaceClass.None)
        {
            int first = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            _indices.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            _classes.Add(cls);
            _classes.Add(cls);
            return this;
        }

        /// <summary>One triangle.</summary>
        public ScannedShapes Triangle(float3 a, float3 b, float3 c, SurfaceClass cls = SurfaceClass.None)
        {
            int first = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _indices.AddRange(new[] { first, first + 1, first + 2 });
            _classes.Add(cls);
            return this;
        }

        /// <summary>Everything so far carried from the local frame of a box into the room: turned by the box's yaw, then set on its base.</summary>
        public ScannedShapes Place(float3 basePoint, float yaw)
        {
            for (int k = 0; k < _vertices.Count; k++) _vertices[k] = ToRoom(_vertices[k], basePoint, yaw);
            return this;
        }

        /// <summary>A local point in the room: yaw carries local +Z to (sin, cos) and local +X to (cos, -sin).</summary>
        public static float3 ToRoom(float3 local, float3 basePoint, float yaw)
        {
            float c = math.cos(yaw), s = math.sin(yaw);
            return basePoint + new float3(local.x * c + local.z * s, local.y, -local.x * s + local.z * c);
        }

        /// <summary>
        /// A chair in its own frame with the back on local -Z: a seat over the whole footprint up to seatHeight
        /// and a back of backDepth along the -Z edge up to the full height.
        /// </summary>
        public static ScannedShapes Chair(float w, float h, float d, float seatHeight, float backDepth)
        {
            return new ScannedShapes()
                .AddBox(new float3(-0.5f * w, 0f, -0.5f * d), new float3(0.5f * w, seatHeight, 0.5f * d))
                .AddBox(new float3(-0.5f * w, 0f, -0.5f * d), new float3(0.5f * w, h, -0.5f * d + backDepth));
        }

        /// <summary>Test vector A of the mesh brief (S3): the chair of a 0.5 x 1.0 x 0.5 box, seat at 0.5, back 0.05 deep.</summary>
        public static ScannedShapes VectorA()
        {
            return Chair(0.5f, 1f, 0.5f, 0.5f, 0.05f);
        }

        public static readonly float3 VectorASize = new float3(0.5f, 1f, 0.5f);

        /// <summary>The profile vector A must give: rows 0 and 1 at 255, every other row at 128.</summary>
        public static byte[] VectorAProfile()
        {
            var expected = new byte[ObjectProfile.Length];
            for (int j = 0; j < ObjectProfile.Side; j++)
                for (int i = 0; i < ObjectProfile.Side; i++)
                    expected[j * ObjectProfile.Side + i] = j <= 1 ? (byte)255 : (byte)128;
            return expected;
        }
    }
}
