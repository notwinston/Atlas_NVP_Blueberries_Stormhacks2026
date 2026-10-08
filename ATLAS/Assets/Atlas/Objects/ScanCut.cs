using System;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>
    /// The LiDAR cut-out (standalone-phone plan D8, T7): an object's own triangles cut out of the scanned room mesh
    /// and made into a mesh the look can draw. Used for Unknown and Other objects, and for objects whose best prop
    /// fits poorly. Engine-free and deterministic: the same scan and object give the same bytes. Each thread keeps
    /// its own scratch mesh, so once it has grown nothing is allocated beyond the target mesh's own growth.
    ///
    /// Winding. The scanned mesh is taken in Unity's winding (front faces clockwise seen from the front, the side
    /// the sensor saw), as Unity's ARMeshManager gives it; each face's normal is cross(b - a, c - a).
    /// </summary>
    public static class ScanCut
    {
        /// <summary>Metres: the box is grown by this much on every side (the bottom included) before triangles are taken.</summary>
        public const float Margin = 0.03f;
        /// <summary>An object with fewer triangles in its box than this has no cut-out.</summary>
        public const int MinTriangles = 200;

        /// <summary>Metres: the first cell of the vertex clustering, doubled until the mesh fits.</summary>
        public const float StartCell = 0.03f;
        /// <summary>Parts with less than this share of the area are dropped (0.5 %).</summary>
        public const float MinPartShare = 0.005f;
        /// <summary>Metres: no edge is longer after the cut (Master Plan section 8.4).</summary>
        public const float MaxEdge = MeshData.DefaultMaxEdge;

        /// <summary>The palette cell the cut-out is drawn in until it is coloured: the tint cell of the object's
        /// category, 32 + ObjectCategories.TintIndex (the placeholder box's colour, as the first-shape scan cut uses).
        /// A caller recolours it with PropColour.Apply(mesh, new[] { DefaultCell(category) }, new[] { cell }).</summary>
        public static byte DefaultCell(ObjectCategory category)
        {
            return (byte)(CategoryCellBase + ObjectCategories.TintIndex(category));
        }

        const int CategoryCellBase = 32;
        // Without face classes, a face is floor when it is level (|normal.y| >= this) and lies within Margin of y = 0.
        const float LevelNormalY = 0.9f;

        [ThreadStatic] static MeshData t_source;

        /// <summary>
        /// Triangles of mesh inside obj's box (+Margin), not Floor-class, moved to box-local, simplified to
        /// ≤ maxTriangles, small parts dropped, edges split ≤ 0.25 m, shade baked. false if fewer than MinTriangles
        /// were inside.
        ///
        /// In detail: a triangle is taken when its three corners lie inside the object's box turned by its yaw and
        /// grown by <see cref="Margin"/>, and its class is not Floor (when the scan has no classes, a level triangle
        /// within <see cref="Margin"/> of the room's floor counts as floor). Its corners move to box-local (origin at
        /// the base centre, front +Z) with y clipped to at least 0. Each corner gets the face's normal, the palette
        /// cell <see cref="DefaultCell"/>, no lines. The result is clustered (<see cref="MeshOps.ClusterSimplify"/>)
        /// at a cell of <see cref="StartCell"/>, then parts under <see cref="MinPartShare"/> of the area are dropped
        /// and long edges split; whenever the result has more than <paramref name="maxTriangles"/> triangles, it is
        /// made again from the taken triangles at twice the cell. Last, the shade is baked for the object's yaw.
        /// Also false, with <paramref name="into"/> empty, when the object or its box is unusable, or nothing fits
        /// within <paramref name="maxTriangles"/>.
        /// </summary>
        public static bool Build(ScanMesh mesh, RoomObject obj, int maxTriangles, MeshData into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (mesh == null || obj == null || maxTriangles < 1) return false;
            float3 size = obj.Size;
            if (!(size.x > 0f) || !(size.y > 0f) || !(size.z > 0f)) return false;
            if (!math.all(math.isfinite(size)) || !math.all(math.isfinite(obj.Base)) || !math.isfinite(obj.Yaw)) return false;

            MeshData source = t_source ?? (t_source = new MeshData(4096, 12288));
            source.Clear();
            int taken = Take(mesh, obj, source);
            if (taken < MinTriangles)
            {
                source.Clear();
                return false;
            }

            // Past this cell the grid cannot merge the box's corners any further: at most one cell either side of
            // each axis's zero, so doubling again changes nothing.
            float limit = 2f * math.cmax(size + 2f * Margin);
            for (float cell = StartCell; ; cell *= 2f)
            {
                Copy(source, into);
                MeshOps.ClusterSimplify(into, cell);
                if (into.TriangleCount == 0) break;
                if (into.TriangleCount <= maxTriangles)
                {
                    MeshOps.DropSmallParts(into, MinPartShare);
                    if (into.TriangleCount == 0) break;
                    if (MeshOps.SplitLongEdges(into, MaxEdge, maxTriangles) && into.TriangleCount <= maxTriangles)
                    {
                        MeshOps.BakeShade(into, obj.Yaw);
                        source.Clear();
                        return true;
                    }
                }
                if (cell > limit) break;
            }
            into.Clear();
            source.Clear();
            return false;
        }

        // Adds the taken triangles to into as separate corners (box-local, y >= 0, face normal); returns how many.
        static int Take(ScanMesh mesh, RoomObject obj, MeshData into)
        {
            float3[] positions = mesh.Vertices;
            int[] indices = mesh.Indices;
            byte[] classes = mesh.Classes;
            float3 half = new float3(0.5f * obj.Size.x + Margin, obj.Size.y + Margin, 0.5f * obj.Size.z + Margin);
            float3 origin = obj.Base;
            float cos = math.cos(obj.Yaw), sin = math.sin(obj.Yaw);
            // Unity's yaw carries local +X to (cos, 0, -sin) and local +Z to (sin, 0, cos); box-local is the inverse.
            var axisX = new float3(cos, 0f, -sin);
            var axisZ = new float3(sin, 0f, cos);
            float floorY = -origin.y;           // the room's floor, box-local

            AtlasVertex vertex;
            vertex.Uv0 = PropColour.CellUv(DefaultCell(obj.Category));
            vertex.Uv1 = float2.zero;
            vertex.Color = AtlasVertex.PackColor(255, 255);
            int taken = 0;
            int triangles = mesh.TriangleCount;
            for (int t = 0; t < triangles; t++)
            {
                if (classes != null && classes[t] == ScanMesh.ClassFloor) continue;
                float3 a = ToLocal(positions[indices[3 * t]], origin, axisX, axisZ);
                if (!Inside(a, half)) continue;
                float3 b = ToLocal(positions[indices[3 * t + 1]], origin, axisX, axisZ);
                if (!Inside(b, half)) continue;
                float3 c = ToLocal(positions[indices[3 * t + 2]], origin, axisX, axisZ);
                if (!Inside(c, half)) continue;

                float3 cross = math.cross(b - a, c - a);
                float length = math.length(cross);
                if (!(length > 1e-12f) || float.IsInfinity(length)) continue;     // degenerate: nothing to draw
                float3 normal = cross / length;
                if (classes == null && math.abs(normal.y) >= LevelNormalY
                    && math.cmax(new float3(a.y, b.y, c.y)) - floorY <= Margin) continue;   // floor, by its shape
                taken++;

                vertex.Normal = normal;
                into.Reserve(3, 3);
                vertex.Position = new float3(a.x, math.max(a.y, 0f), a.z);
                int ia = into.AddVertex(in vertex);
                vertex.Position = new float3(b.x, math.max(b.y, 0f), b.z);
                int ib = into.AddVertex(in vertex);
                vertex.Position = new float3(c.x, math.max(c.y, 0f), c.z);
                int ic = into.AddVertex(in vertex);
                into.AddTriangle(ia, ib, ic);
            }
            return taken;
        }

        static float3 ToLocal(float3 p, float3 origin, float3 axisX, float3 axisZ)
        {
            float3 d = p - origin;
            return new float3(math.dot(d, axisX), d.y, math.dot(d, axisZ));
        }

        // NaN fails every test, so a corner that is not a number is outside.
        static bool Inside(float3 p, float3 half)
        {
            return math.abs(p.x) <= half.x && math.abs(p.z) <= half.z && p.y >= -Margin && p.y <= half.y;
        }

        static void Copy(MeshData from, MeshData to)
        {
            to.Clear();
            to.Reserve(from.VertexCount, from.IndexCount);
            Array.Copy(from.Vertices, to.Vertices, from.VertexCount);
            Array.Copy(from.Indices, to.Indices, from.IndexCount);
            to.VertexCount = from.VertexCount;
            to.IndexCount = from.IndexCount;
            to.BoundsMin = from.BoundsMin;
            to.BoundsMax = from.BoundsMax;
        }
    }
}
