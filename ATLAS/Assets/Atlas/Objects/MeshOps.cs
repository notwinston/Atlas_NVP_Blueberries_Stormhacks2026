using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Objects
{
    /// <summary>
    /// In-place operations on <see cref="MeshData"/> for the on-phone object meshes (standalone-phone plan section
    /// 6.4): the edge split that lets a mesh bend in curved space, vertex clustering and small-part removal for the
    /// LiDAR cut-out, and the shade bake. Engine-free and deterministic: the same mesh in gives the same bytes out.
    /// Each thread keeps its own scratch buffers, so once they have grown nothing is allocated beyond the mesh's
    /// own growth.
    /// </summary>
    public static class MeshOps
    {
        /// <summary>The floor darkens what stands on it: this much at the floor, none from <see cref="OcclusionHeight"/> up (FallbackMeshBuilder's rule).</summary>
        public const float FloorOcclusion = 0.7f;
        /// <summary>Metres.</summary>
        public const float OcclusionHeight = 0.3f;

        [ThreadStatic] static Dictionary<long, int> t_midpoints;
        [ThreadStatic] static Dictionary<long, int> t_cells;
        [ThreadStatic] static int[] t_map;
        [ThreadStatic] static int[] t_first;
        [ThreadStatic] static int[] t_count;
        [ThreadStatic] static double3[] t_positionSum;
        [ThreadStatic] static float3[] t_normalSum;
        [ThreadStatic] static double[] t_area;

        // ---- SplitLongEdges ----------------------------------------------------------------------------------

        /// <summary>
        /// Splits triangles until no triangle edge is longer than <paramref name="maxEdge"/> (Master Plan section
        /// 8.4), by longest-edge midpoint bisection (AtlasMeshBuilder.Subdivide's rule). An edge is cut exactly when
        /// it is longer than the limit, always at its midpoint, so two triangles that share an edge cut it at the
        /// same points and no crack opens: triangles that share the edge's vertex indices share the new vertex too,
        /// and triangles with their own copies of the end points get a midpoint at the same position to the bit.
        /// A midpoint takes the mean normal and colour of its ends and the palette coordinate of the end with the
        /// lower index, so it never lands between two palette cells.
        /// </summary>
        public static void SplitLongEdges(MeshData mesh, float maxEdge)
        {
            SplitLongEdges(mesh, maxEdge, int.MaxValue);
        }

        /// <summary>
        /// The same, stopping as soon as the mesh holds more than <paramref name="maxTriangles"/> triangles. False
        /// then, with the mesh half split; true when every edge is within the limit.
        /// </summary>
        internal static bool SplitLongEdges(MeshData mesh, float maxEdge, int maxTriangles)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!(maxEdge > 0f)) throw new ArgumentOutOfRangeException(nameof(maxEdge));
            Dictionary<long, int> midpoints = t_midpoints ?? (t_midpoints = new Dictionary<long, int>(256));
            midpoints.Clear();
            float limitSq = maxEdge * maxEdge;

            // Triangles appended by a split are reached later by the same loop.
            for (int t = 0; t + 2 < mesh.IndexCount; t += 3)
            {
                while (true)
                {
                    int[] indices = mesh.Indices;
                    AtlasVertex[] vertices = mesh.Vertices;
                    int i0 = indices[t], i1 = indices[t + 1], i2 = indices[t + 2];
                    float3 p0 = vertices[i0].Position, p1 = vertices[i1].Position, p2 = vertices[i2].Position;
                    float d01 = math.distancesq(p0, p1), d12 = math.distancesq(p1, p2), d20 = math.distancesq(p2, p0);
                    float longest = math.max(d01, math.max(d12, d20));
                    if (!(longest > limitSq)) break;        // NaN positions are left alone
                    if (mesh.TriangleCount >= maxTriangles)
                    {
                        midpoints.Clear();
                        return false;
                    }

                    if (d01 >= d12 && d01 >= d20)
                    {
                        int m = Midpoint(mesh, midpoints, i0, i1);
                        mesh.Indices[t + 1] = m;            // (i0, m, i2)
                        mesh.AddTriangle(m, i1, i2);
                    }
                    else if (d12 >= d20)
                    {
                        int m = Midpoint(mesh, midpoints, i1, i2);
                        mesh.Indices[t + 2] = m;            // (i0, i1, m)
                        mesh.AddTriangle(i0, m, i2);
                    }
                    else
                    {
                        int m = Midpoint(mesh, midpoints, i2, i0);
                        mesh.Indices[t + 2] = m;            // (i0, i1, m)
                        mesh.AddTriangle(m, i1, i2);
                    }
                }
            }
            midpoints.Clear();
            return true;
        }

        static int Midpoint(MeshData mesh, Dictionary<long, int> midpoints, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (midpoints.TryGetValue(key, out int index)) return index;

            AtlasVertex va = mesh.Vertices[a], vb = mesh.Vertices[b];
            AtlasVertex mid;
            // (a + b) / 2 is the same whichever end comes first, so both sides of an edge agree to the bit.
            mid.Position = (va.Position + vb.Position) * 0.5f;
            mid.Normal = math.normalizesafe(va.Normal + vb.Normal, va.Normal);
            mid.Uv0 = a < b ? va.Uv0 : vb.Uv0;
            mid.Uv1 = (va.Uv1 + vb.Uv1) * 0.5f;
            mid.Color = AverageColor(va.Color, vb.Color);
            index = mesh.AddVertex(in mid);
            midpoints.Add(key, index);
            return index;
        }

        static uint AverageColor(uint a, uint b)
        {
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                uint ca = (a >> shift) & 255u, cb = (b >> shift) & 255u;
                result |= ((ca + cb + 1u) >> 1) << shift;
            }
            return result;
        }

        // ---- ClusterSimplify ---------------------------------------------------------------------------------

        /// <summary>
        /// Vertex clustering on a uniform grid of <paramref name="cell"/> metres: every vertex moves to the mean
        /// position of the vertices in its grid cell (normal: the normalised mean; colour, palette and line
        /// coordinates: those of the cell's first vertex), triangles that lose a corner are dropped, and vertices
        /// no triangle uses any more are removed. Clusters keep the order of their first vertex, so the result
        /// depends only on the input. A cell that is not positive leaves the mesh as it is.
        /// </summary>
        public static void ClusterSimplify(MeshData mesh, float cell)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!(cell > 0f) || mesh.VertexCount == 0) return;
            int n = mesh.VertexCount;
            EnsureScratch(n);
            Dictionary<long, int> cells = t_cells ?? (t_cells = new Dictionary<long, int>(1024));
            cells.Clear();
            int[] map = t_map, first = t_first, count = t_count;
            double3[] positionSum = t_positionSum;
            float3[] normalSum = t_normalSum;
            AtlasVertex[] vertices = mesh.Vertices;
            double inverse = 1.0 / cell;

            int clusters = 0;
            for (int v = 0; v < n; v++)
            {
                float3 p = vertices[v].Position;
                long key = CellKey(p, inverse);
                if (!cells.TryGetValue(key, out int c))
                {
                    c = clusters++;
                    cells.Add(key, c);
                    first[c] = v;
                    count[c] = 0;
                    positionSum[c] = double3.zero;
                    normalSum[c] = float3.zero;
                }
                map[v] = c;
                count[c]++;
                positionSum[c] += (double3)p;
                normalSum[c] += vertices[v].Normal;
            }
            cells.Clear();

            // Cluster c's first vertex has an index of at least c, so writing cluster c over vertex c never
            // overwrites the first vertex of a later cluster.
            for (int c = 0; c < clusters; c++)
            {
                AtlasVertex vertex = vertices[first[c]];
                vertex.Position = (float3)(positionSum[c] / count[c]);
                vertex.Normal = math.normalizesafe(normalSum[c], vertex.Normal);
                vertices[c] = vertex;
            }

            int[] indices = mesh.Indices;
            int kept = 0;
            for (int t = 0; t + 2 < mesh.IndexCount; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                a = map[a]; b = map[b]; c = map[c];
                if (a == b || b == c || c == a) continue;   // collapsed
                indices[kept++] = a;
                indices[kept++] = b;
                indices[kept++] = c;
            }
            mesh.IndexCount = kept;
            mesh.VertexCount = clusters;
            Compact(mesh);
        }

        static long CellKey(float3 p, double inverse)
        {
            long x = (long)Math.Floor(p.x * inverse), y = (long)Math.Floor(p.y * inverse), z = (long)Math.Floor(p.z * inverse);
            return ((x & 0x1FFFFF) << 42) | ((y & 0x1FFFFF) << 21) | (z & 0x1FFFFF);
        }

        // ---- DropSmallParts ----------------------------------------------------------------------------------

        /// <summary>
        /// Removes every connected part (triangles joined through shared vertex indices) whose area is less than
        /// <paramref name="minAreaShare"/> of the whole mesh's area (0.005 = 0.5 %), then the vertices no triangle
        /// uses any more. Order is kept.
        /// </summary>
        public static void DropSmallParts(MeshData mesh, float minAreaShare)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            int n = mesh.VertexCount;
            if (n == 0 || mesh.IndexCount < 3) return;
            EnsureScratch(n);
            int[] parent = t_map;
            double[] area = t_area;
            for (int v = 0; v < n; v++) { parent[v] = v; area[v] = 0.0; }

            int[] indices = mesh.Indices;
            AtlasVertex[] vertices = mesh.Vertices;
            int triangles = mesh.IndexCount - mesh.IndexCount % 3;
            for (int t = 0; t < triangles; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                Union(parent, a, b);
                Union(parent, a, c);
            }
            double total = 0.0;
            for (int t = 0; t < triangles; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                double s = TriangleArea(vertices[a].Position, vertices[b].Position, vertices[c].Position);
                area[Find(parent, a)] += s;
                total += s;
            }
            double minimum = total * minAreaShare;
            int kept = 0;
            for (int t = 0; t < triangles; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if ((uint)a >= (uint)n || (uint)b >= (uint)n || (uint)c >= (uint)n) continue;
                if (area[Find(parent, a)] < minimum) continue;
                indices[kept++] = a;
                indices[kept++] = b;
                indices[kept++] = c;
            }
            mesh.IndexCount = kept;
            Compact(mesh);
        }

        static int Find(int[] parent, int v)
        {
            int root = v;
            while (parent[root] != root) root = parent[root];
            while (parent[v] != root) { int next = parent[v]; parent[v] = root; v = next; }
            return root;
        }

        static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a), rb = Find(parent, b);
            if (ra == rb) return;
            // The smaller index becomes the root, so the result does not depend on the order of the unions.
            if (ra < rb) parent[rb] = ra; else parent[ra] = rb;
        }

        static double TriangleArea(float3 a, float3 b, float3 c)
        {
            double3 u = (double3)b - (double3)a, v = (double3)c - (double3)a;
            return 0.5 * math.length(math.cross(u, v));
        }

        // ---- BakeShade ---------------------------------------------------------------------------------------

        /// <summary>
        /// Bakes the fixed diagram light into COLOR.r of every vertex, as FallbackMeshBuilder does: the vertex
        /// normal (box-local) is turned by the object's <paramref name="yaw"/> into the room, shaded with
        /// <see cref="AtlasShading.FaceShade"/>, and darkened near the floor from <see cref="FloorOcclusion"/> at
        /// y = 0 to none at <see cref="OcclusionHeight"/>. COLOR.g (line weight) is kept; COLOR.b and COLOR.a become
        /// 255 (not batched).
        /// </summary>
        public static void BakeShade(MeshData mesh, float yaw)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            float cos = math.cos(yaw), sin = math.sin(yaw);
            AtlasVertex[] vertices = mesh.Vertices;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                float3 normal = vertices[v].Normal;
                // Unity's yaw carries local +X to (cos, 0, -sin) and local +Z to (sin, 0, cos).
                var inRoom = new float3(normal.x * cos + normal.z * sin, normal.y, -normal.x * sin + normal.z * cos);
                float occlusion = math.lerp(FloorOcclusion, 1f, math.saturate(vertices[v].Position.y / OcclusionHeight));
                byte lineWeight = (byte)((vertices[v].Color >> 8) & 255u);
                vertices[v].Color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(inRoom), occlusion), lineWeight);
            }
        }

        // ---- Shared ------------------------------------------------------------------------------------------

        /// <summary>Removes the vertices no triangle uses (order kept) and sets the bounds to the vertices left.</summary>
        internal static void Compact(MeshData mesh)
        {
            int n = mesh.VertexCount;
            EnsureScratch(n);
            int[] map = t_map;
            for (int v = 0; v < n; v++) map[v] = -1;
            int[] indices = mesh.Indices;
            for (int k = 0; k < mesh.IndexCount; k++) map[indices[k]] = 0;
            int next = 0;
            AtlasVertex[] vertices = mesh.Vertices;
            for (int v = 0; v < n; v++)
            {
                if (map[v] < 0) continue;
                map[v] = next;
                if (next != v) vertices[next] = vertices[v];   // next <= v: nothing still needed is overwritten
                next++;
            }
            for (int k = 0; k < mesh.IndexCount; k++) indices[k] = map[indices[k]];
            mesh.VertexCount = next;
            RecomputeBounds(mesh);
        }

        /// <summary>Sets BoundsMin and BoundsMax to the vertices' (both zero for no vertex), as MeshData.AddVertex keeps them.</summary>
        internal static void RecomputeBounds(MeshData mesh)
        {
            if (mesh.VertexCount == 0)
            {
                mesh.BoundsMin = float3.zero;
                mesh.BoundsMax = float3.zero;
                return;
            }
            float3 lo = mesh.Vertices[0].Position, hi = lo;
            for (int v = 1; v < mesh.VertexCount; v++)
            {
                lo = math.min(lo, mesh.Vertices[v].Position);
                hi = math.max(hi, mesh.Vertices[v].Position);
            }
            mesh.BoundsMin = lo;
            mesh.BoundsMax = hi;
        }

        static void EnsureScratch(int n)
        {
            if (t_map != null && t_map.Length >= n) return;
            int size = Math.Max(n, t_map == null ? 1024 : t_map.Length * 2);
            t_map = new int[size];
            t_first = new int[size];
            t_count = new int[size];
            t_positionSum = new double3[size];
            t_normalSum = new float3[size];
            t_area = new double[size];
        }
    }
}
