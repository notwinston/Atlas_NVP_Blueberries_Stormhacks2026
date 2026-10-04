using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Cuts tetrahedra in R^4 with a hyperplane (Master Plan sections 5.5 and A.8). Static, pointer-based, allocation-free.</summary>
    /// <remarks>
    /// Engine-free (foundation F2): the Burst job and the headless tests run the same code on raw pointers.
    /// The output is a triangle soup, three vertices per triangle with a flat normal, so the index buffer of
    /// the receiving mesh is the fixed sequence 0, 1, 2, ...
    /// Capacity: a tetrahedron gives 0, 1 or 2 triangles, so 6 * tetCount output vertices always suffice.
    /// </remarks>
    public static unsafe class Slicer4D
    {
        public const int MaxTrianglesPerTet = 2;
        public const int MaxVerticesPerTet = 6;

        /// <summary>
        /// Slices the mesh with the hyperplane dot(normal, p) = offset in the slice frame, where
        /// p = mul(modelToSlice, vertex) + translation. The 3D position of a cut point is p.xyz: with
        /// normal = +w that is an isometric picture of the cross-section, and a tilted normal with w dropped
        /// is the scanner picture of section 5.4.
        /// Every triangle (a, b, c) is wound so that cross(b - a, c - a) points out of the body, along its
        /// stored normal (Unity's front face), provided the tetrahedra are oriented outward as the generators
        /// leave them and modelToSlice is a rotation.
        /// </summary>
        /// <param name="vertices">Model-space vertices.</param>
        /// <param name="tetrahedra">4 * tetCount vertex indices.</param>
        /// <param name="tetCount">Number of tetrahedra.</param>
        /// <param name="tetCell">tetCount cell ids, or null (every cell id is then 0).</param>
        /// <param name="vertexData">One scalar per vertex to interpolate, or null.</param>
        /// <param name="modelToSlice">Rotation of the object in R^4.</param>
        /// <param name="translation">Added after the rotation.</param>
        /// <param name="normal">Unit normal of the cut in the slice frame.</param>
        /// <param name="offset">The cut is dot(normal, p) = offset.</param>
        /// <param name="outPositions">Receives up to vertexCapacity positions, 3 per triangle.</param>
        /// <param name="outNormals">Receives the unit flat normal of its triangle for every vertex.</param>
        /// <param name="outCells">Receives the cell id of the tetrahedron each vertex came from.</param>
        /// <param name="outVertexData">Receives the interpolated scalar, or null. Unused when vertexData is null.</param>
        /// <param name="vertexCapacity">Length of every output buffer. Slicing stops before the first tetrahedron that would not fit.</param>
        /// <param name="outVertexCount">Vertices written (3 * outTriangleCount).</param>
        /// <param name="outTriangleCount">Triangles written.</param>
        /// <param name="outTetsCut">Tetrahedra whose cut was written.</param>
        public static void Slice(
            float4* vertices,
            int* tetrahedra, int tetCount,
            int* tetCell,
            float* vertexData,
            in float4x4 modelToSlice, in float4 translation,
            in float4 normal, float offset,
            float3* outPositions, float3* outNormals, int* outCells, float* outVertexData,
            int vertexCapacity,
            out int outVertexCount, out int outTriangleCount, out int outTetsCut)
        {
            // Work in model space: dot(normal, M v + t) - offset = dot(M^T normal, v) - (offset - dot(normal, t)).
            // Only the points of the cut are transformed, not every vertex, and no scratch buffer is needed.
            float4x4 m = modelToSlice;
            float4 n = math.mul(math.transpose(m), normal);
            float d0 = offset - math.dot(normal, translation);
            bool withData = vertexData != null && outVertexData != null;

            float4* v = stackalloc float4[4];
            float* f = stackalloc float[4];
            float* d = stackalloc float[4];
            int* above = stackalloc int[4];
            int* below = stackalloc int[4];
            float3* q = stackalloc float3[4];
            float* qd = stackalloc float[4];
            d[0] = d[1] = d[2] = d[3] = 0f;

            int written = 0;
            int cut = 0;

            for (int t = 0; t < tetCount; t++)
            {
                int* idx = tetrahedra + t * 4;
                v[0] = vertices[idx[0]];
                v[1] = vertices[idx[1]];
                v[2] = vertices[idx[2]];
                v[3] = vertices[idx[3]];
                f[0] = math.dot(n, v[0]) - d0;
                f[1] = math.dot(n, v[1]) - d0;
                f[2] = math.dot(n, v[2]) - d0;
                f[3] = math.dot(n, v[3]) - d0;

                // A vertex exactly on the cut counts as positive. One rule for every tetrahedron keeps the
                // surface closed.
                int nAbove = 0, nBelow = 0;
                for (int k = 0; k < 4; k++)
                {
                    if (f[k] >= 0f) above[nAbove++] = k; else below[nBelow++] = k;
                }
                if (nAbove == 0 || nBelow == 0) continue;

                int corners = nAbove == 2 ? 4 : 3;
                if (written + (corners - 2) * 3 > vertexCapacity) break;

                if (withData)
                {
                    d[0] = vertexData[idx[0]];
                    d[1] = vertexData[idx[1]];
                    d[2] = vertexData[idx[2]];
                    d[3] = vertexData[idx[3]];
                }

                // Corners of the cut in cyclic order. Every edge is interpolated from its negative end to its
                // positive end, so two tetrahedra that share an edge compute bit-identical points: no cracks.
                if (nBelow == 1)
                {
                    for (int k = 0; k < 3; k++) EdgePoint(v, f, d, below[0], above[k], m, translation, q + k, qd + k);
                }
                else if (nAbove == 1)
                {
                    for (int k = 0; k < 3; k++) EdgePoint(v, f, d, below[k], above[0], m, translation, q + k, qd + k);
                }
                else
                {
                    EdgePoint(v, f, d, below[0], above[0], m, translation, q + 0, qd + 0);
                    EdgePoint(v, f, d, below[1], above[0], m, translation, q + 1, qd + 1);
                    EdgePoint(v, f, d, below[1], above[1], m, translation, q + 2, qd + 2);
                    EdgePoint(v, f, d, below[0], above[1], m, translation, q + 3, qd + 3);
                }

                // Which way is out: the tetrahedron's 4D normal, turned into the slice frame, with its part
                // along the cut's normal removed. Dropping w keeps sides, so its xyz says which side of the
                // face is outside.
                float4 normal4 = math.mul(m, Mesh4D.TetNormal(v[1] - v[0], v[2] - v[0], v[3] - v[0]));
                float3 outward = (normal4 - math.dot(normal4, normal) * normal).xyz;

                // One flat normal for the whole face. For a quadrilateral the cross product of its diagonals
                // is the normal of its plane.
                float3 face = corners == 4 ? math.cross(q[2] - q[0], q[3] - q[1]) : math.cross(q[1] - q[0], q[2] - q[0]);
                bool flip = math.dot(face, outward) < 0f;
                if (flip) face = -face;
                float3 unit = math.normalizesafe(face, math.normalizesafe(outward, new float3(0f, 1f, 0f)));
                int cell = tetCell != null ? tetCell[t] : 0;

                for (int tri = 0; tri < corners - 2; tri++)
                {
                    int ib = flip ? tri + 2 : tri + 1;
                    int ic = flip ? tri + 1 : tri + 2;
                    outPositions[written] = q[0];
                    outPositions[written + 1] = q[ib];
                    outPositions[written + 2] = q[ic];
                    outNormals[written] = unit;
                    outNormals[written + 1] = unit;
                    outNormals[written + 2] = unit;
                    outCells[written] = cell;
                    outCells[written + 1] = cell;
                    outCells[written + 2] = cell;
                    if (withData)
                    {
                        outVertexData[written] = qd[0];
                        outVertexData[written + 1] = qd[ib];
                        outVertexData[written + 2] = qd[ic];
                    }
                    written += 3;
                }
                cut++;
            }

            outVertexCount = written;
            outTriangleCount = written / 3;
            outTetsCut = cut;
        }

        // Where the edge from vertex a (negative side) to vertex b (positive side) meets the cut:
        // p_a + d_a / (d_a - d_b) * (p_b - p_a) (Master Plan A.8), then into the slice frame with w dropped.
        private static void EdgePoint(float4* v, float* f, float* d, int a, int b,
                                      in float4x4 m, in float4 translation, float3* point, float* data)
        {
            float s = f[a] / (f[a] - f[b]);
            float4 p = v[a] + s * (v[b] - v[a]);
            *point = (math.mul(m, p) + translation).xyz;
            *data = d[a] + s * (d[b] - d[a]);
        }

        /// <summary>Sum of the triangle areas of a slice (three positions per triangle).</summary>
        public static float Area(float3* positions, int triangleCount)
        {
            double area = 0.0;
            for (int t = 0; t < triangleCount; t++)
            {
                double3 a = positions[t * 3];
                double3 b = positions[t * 3 + 1];
                double3 c = positions[t * 3 + 2];
                area += 0.5 * math.length(math.cross(b - a, c - a));
            }
            return (float)area;
        }

        /// <summary>
        /// Extent of a slice along <paramref name="axis"/> (normalised here): the largest minus the smallest
        /// coordinate of its vertices. 0 for an empty slice.
        /// </summary>
        public static float Extent(float3* positions, int vertexCount, float3 axis)
        {
            if (vertexCount <= 0) return 0f;
            float3 unit = math.normalize(axis);
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vertexCount; i++)
            {
                float s = math.dot(positions[i], unit);
                min = math.min(min, s);
                max = math.max(max, s);
            }
            return max - min;
        }
    }
}
