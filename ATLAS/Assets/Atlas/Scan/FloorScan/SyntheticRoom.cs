using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Scan.Grid
{
    /// <summary>
    /// Builds a room as a classified triangle soup, the way a finished scan would deliver it, for tests and for
    /// trying FloorScan in the Editor without a phone. Floor points are (x, z); the floor is at y = 0 unless the
    /// room is moved with <see cref="Move"/>.
    /// </summary>
    public sealed class SyntheticRoom
    {
        readonly List<float3> _vertices = new List<float3>();
        readonly List<int> _indices = new List<int>();
        readonly List<SurfaceClass> _classes = new List<SurfaceClass>();
        readonly List<float2> _path = new List<float2>();
        readonly List<WallPlane> _walls = new List<WallPlane>();
        readonly List<ObstacleBox> _external = new List<ObstacleBox>();
        float2[] _outline;
        float2 _walker;
        float _floorHeight;

        /// <summary>An empty rectangular room from (0, 0) to (w, d): floor, walls and the outline, walker in the middle.</summary>
        public static SyntheticRoom Rectangle(float w, float d, float wallHeight = 2.4f)
        {
            return Polygon(new[] { new float2(0f, 0f), new float2(w, 0f), new float2(w, d), new float2(0f, d) },
                new float2(0.5f * w, 0.5f * d), wallHeight);
        }

        /// <summary>A room with the given counter-clockwise outline: floor, walls and the outline.</summary>
        public static SyntheticRoom Polygon(float2[] outline, float2 walker, float wallHeight = 2.4f)
        {
            var room = new SyntheticRoom();
            room.AddFloor(outline);
            room.AddWalls(outline, wallHeight);
            room.SetOutline(outline);
            room.SetWalker(walker);
            return room;
        }

        /// <summary>Number of triangles so far.</summary>
        public int TriangleCount => _classes.Count;

        /// <summary>The outline the walker confirmed. Null removes it.</summary>
        public SyntheticRoom SetOutline(float2[] outline)
        {
            _outline = outline != null ? (float2[])outline.Clone() : null;
            return this;
        }

        /// <summary>Where the walker stands when the sweep ends.</summary>
        public SyntheticRoom SetWalker(float2 walker)
        {
            _walker = walker;
            return this;
        }

        /// <summary>Append a point to the walker's path.</summary>
        public SyntheticRoom AddPathPoint(float2 point)
        {
            _path.Add(point);
            return this;
        }

        /// <summary>Floor triangles over a simple counter-clockwise polygon.</summary>
        public SyntheticRoom AddFloor(float2[] polygon)
        {
            // Ear clipping: enough for the simple outlines of a room.
            var ring = new List<int>(polygon.Length);
            for (int k = 0; k < polygon.Length; k++) ring.Add(k);
            int first = _vertices.Count;
            for (int k = 0; k < polygon.Length; k++) _vertices.Add(new float3(polygon[k].x, 0f, polygon[k].y));
            int guard = polygon.Length * polygon.Length;
            while (ring.Count > 3 && guard-- > 0)
            {
                for (int k = 0; k < ring.Count; k++)
                {
                    int ia = ring[(k + ring.Count - 1) % ring.Count], ib = ring[k], ic = ring[(k + 1) % ring.Count];
                    float2 a = polygon[ia], b = polygon[ib], c = polygon[ic];
                    if (Cross(b - a, c - b) <= 0f) continue;
                    bool ear = true;
                    for (int m = 0; m < ring.Count && ear; m++)
                    {
                        int ip = ring[m];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        float2 p = polygon[ip];
                        if (Cross(b - a, p - a) >= 0f && Cross(c - b, p - b) >= 0f && Cross(a - c, p - c) >= 0f) ear = false;
                    }
                    if (!ear) continue;
                    AddTriangle(first + ia, first + ib, first + ic, SurfaceClass.Floor);
                    ring.RemoveAt(k);
                    break;
                }
            }
            if (ring.Count == 3) AddTriangle(first + ring[0], first + ring[1], first + ring[2], SurfaceClass.Floor);
            return this;
        }

        /// <summary>Floor triangles over an axis-aligned rectangle (for floors seen only in part).</summary>
        public SyntheticRoom AddFloorRect(float x0, float z0, float x1, float z1)
        {
            return AddQuad(new float3(x0, 0f, z0), new float3(x1, 0f, z0), new float3(x1, 0f, z1), new float3(x0, 0f, z1),
                SurfaceClass.Floor);
        }

        /// <summary>One wall per outline edge, from the floor up, plus the wall planes for the axis.</summary>
        public SyntheticRoom AddWalls(float2[] outline, float height)
        {
            for (int k = 0; k < outline.Length; k++)
            {
                float2 a = outline[k], b = outline[(k + 1) % outline.Length];
                AddQuad(new float3(a.x, 0f, a.y), new float3(b.x, 0f, b.y), new float3(b.x, height, b.y),
                    new float3(a.x, height, a.y), SurfaceClass.Wall);
                float2 along = b - a;
                _walls.Add(new WallPlane { Normal = new float2(-along.y, along.x), Length = math.length(along) });
            }
            return this;
        }

        /// <summary>A box-shaped piece of furniture as scanned: its top and four sides.</summary>
        /// <param name="centre">Centre of the footprint.</param>
        /// <param name="size">Width along local X, height, depth along local Z.</param>
        /// <param name="yaw">Rotation about +Y, radians, Unity sense.</param>
        public SyntheticRoom AddBox(float2 centre, float3 size, float yaw = 0f, SurfaceClass topClass = SurfaceClass.Table,
            float bottom = 0f)
        {
            float c = math.cos(yaw), s = math.sin(yaw);
            float2 ux = new float2(c, -s) * (0.5f * size.x), uz = new float2(s, c) * (0.5f * size.z);
            float2[] corner = { centre - ux - uz, centre + ux - uz, centre + ux + uz, centre - ux + uz };
            float top = bottom + size.y;
            AddQuad(new float3(corner[0].x, top, corner[0].y), new float3(corner[1].x, top, corner[1].y),
                new float3(corner[2].x, top, corner[2].y), new float3(corner[3].x, top, corner[3].y), topClass);
            for (int k = 0; k < 4; k++)
            {
                float2 a = corner[k], b = corner[(k + 1) % 4];
                AddQuad(new float3(a.x, bottom, a.y), new float3(b.x, bottom, b.y), new float3(b.x, top, b.y),
                    new float3(a.x, top, a.y), SurfaceClass.None);
            }
            return this;
        }

        /// <summary>A box from another source (RoomPlan) rather than from the mesh.</summary>
        public SyntheticRoom AddExternalBox(ObstacleBox box)
        {
            _external.Add(box);
            return this;
        }

        /// <summary>Two triangles a-b-c and a-c-d of one class.</summary>
        public SyntheticRoom AddQuad(float3 a, float3 b, float3 c, float3 d, SurfaceClass cls)
        {
            int first = _vertices.Count;
            _vertices.Add(a);
            _vertices.Add(b);
            _vertices.Add(c);
            _vertices.Add(d);
            AddTriangle(first, first + 1, first + 2, cls);
            AddTriangle(first, first + 2, first + 3, cls);
            return this;
        }

        /// <summary>
        /// Place the whole room elsewhere in the session frame: turn it about +Y by <paramref name="angle"/>
        /// (radians, from +X towards +Z), then shift it. Everything added so far moves together.
        /// </summary>
        public SyntheticRoom Move(float angle, float3 offset)
        {
            float c = math.cos(angle), s = math.sin(angle);
            for (int k = 0; k < _vertices.Count; k++)
            {
                float3 v = _vertices[k];
                _vertices[k] = new float3(c * v.x - s * v.z + offset.x, v.y + offset.y, s * v.x + c * v.z + offset.z);
            }
            float2 shift = new float2(offset.x, offset.z);
            for (int k = 0; k < _path.Count; k++) _path[k] = Turn(_path[k], c, s) + shift;
            if (_outline != null)
                for (int k = 0; k < _outline.Length; k++) _outline[k] = Turn(_outline[k], c, s) + shift;
            _walker = Turn(_walker, c, s) + shift;
            for (int k = 0; k < _walls.Count; k++)
            {
                WallPlane w = _walls[k];
                w.Normal = Turn(w.Normal, c, s);
                _walls[k] = w;
            }
            for (int k = 0; k < _external.Count; k++)
            {
                ObstacleBox b = _external[k];
                float2 xz = Turn(new float2(b.Base.x, b.Base.z), c, s) + shift;
                b.Base = new float3(xz.x, b.Base.y + offset.y, xz.y);
                b.Yaw -= angle;      // Unity yaw runs from +Z towards +X, the other way round
                _external[k] = b;
            }
            _floorHeight += offset.y;
            return this;
        }

        /// <summary>The scan as FloorScan reads it.</summary>
        /// <param name="withWallPlanes">False leaves the wall planes out, so the axis comes from the outline or the mesh.</param>
        /// <param name="withFloorHeight">False leaves y0 out, so it is derived from the mesh.</param>
        public FloorGridInput Build(bool withWallPlanes = true, bool withFloorHeight = true)
        {
            return new FloorGridInput
            {
                Vertices = _vertices.ToArray(),
                Indices = _indices.ToArray(),
                TriangleClasses = _classes.ToArray(),
                FloorHeight = withFloorHeight ? _floorHeight : float.NaN,
                WalkerPath = _path.Count > 0 ? _path.ToArray() : null,
                Walker = _walker,
                Outline = _outline != null ? (float2[])_outline.Clone() : null,
                Walls = withWallPlanes && _walls.Count > 0 ? _walls.ToArray() : null,
                ExternalBoxes = _external.Count > 0 ? _external.ToArray() : null,
            };
        }

        void AddTriangle(int a, int b, int c, SurfaceClass cls)
        {
            _indices.Add(a);
            _indices.Add(b);
            _indices.Add(c);
            _classes.Add(cls);
        }

        static float Cross(float2 a, float2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        static float2 Turn(float2 p, float c, float s)
        {
            return new float2(c * p.x - s * p.y, s * p.x + c * p.y);
        }
    }
}
