// Lane A engine-free core, P2-A20: reference CPU slicer (Master Plan Appendix A.8) and the slab index.
// The Phase 1 Slicer4D (Burst) is the production slicer; this one is the plain C# reference the tests
// run without the engine. Plain C#, System only. Nothing here allocates.
using System;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// A cutting hyperplane dot(N, x) = Offset in R^4 (N a unit vector) and the three rows E0, E1, E2 that
    /// turn a 4D point of the cut into its 3D picture, q = (E0.x, E1.x, E2.x). A plain value: pass it by <c>in</c>.
    /// In Phase 1 terms E0..E2 are the first three rows of <c>modelToSlice</c> and N is its fourth row
    /// (the slice normal +w taken back to model space), with translation 0.
    /// </summary>
    public struct SliceFrame
    {
        public float Nx, Ny, Nz, Nw, Offset;
        public float E0x, E0y, E0z, E0w, E1x, E1y, E1z, E1w, E2x, E2y, E2z, E2w;

        /// <summary>The untilted cut w = offset (theta = 0): the picture is the point's own x, y, z.</summary>
        public static SliceFrame AtW(float w)
        {
            var f = new SliceFrame();
            f.Nw = 1f; f.Offset = w;
            f.E0x = 1f; f.E1y = 1f; f.E2z = 1f;
            return f;
        }

        /// <summary>
        /// The cut n.x = d with the picture "x, y, z with w dropped": the scanner picture of section 5.4, in
        /// which the table axis keeps its true scale. n need not be a unit vector; n and d are scaled together.
        /// </summary>
        public static SliceFrame DropW(double nx, double ny, double nz, double nw, double d)
        {
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz + nw * nw);
            if (!(len > 0.0)) throw new ArgumentException("The normal must not be zero.");
            var f = new SliceFrame();
            f.Nx = (float)(nx / len); f.Ny = (float)(ny / len); f.Nz = (float)(nz / len); f.Nw = (float)(nw / len);
            f.Offset = (float)(d / len);
            f.E0x = 1f; f.E1y = 1f; f.E2z = 1f;
            return f;
        }

        /// <summary>
        /// The cut n.x = d with an orthonormal basis of the hyperplane itself (Gram-Schmidt on the standard
        /// basis, as in R2_slicer_proto.py): an isometric picture, so areas are true.
        /// </summary>
        public static SliceFrame InPlane(double nx, double ny, double nz, double nw, double d)
        {
            double len = Math.Sqrt(nx * nx + ny * ny + nz * nz + nw * nw);
            if (!(len > 0.0)) throw new ArgumentException("The normal must not be zero.");
            double n0 = nx / len, n1 = ny / len, n2 = nz / len, n3 = nw / len;
            // Three accepted basis vectors, 4 doubles each, in locals (no array: this may run per frame).
            double b00 = 0, b01 = 0, b02 = 0, b03 = 0, b10 = 0, b11 = 0, b12 = 0, b13 = 0, b20 = 0, b21 = 0, b22 = 0, b23 = 0;
            int have = 0;
            for (int axis = 0; axis < 4 && have < 3; axis++)
            {
                double v0 = axis == 0 ? 1 : 0, v1 = axis == 1 ? 1 : 0, v2 = axis == 2 ? 1 : 0, v3 = axis == 3 ? 1 : 0;
                double along = axis == 0 ? n0 : axis == 1 ? n1 : axis == 2 ? n2 : n3;
                v0 -= along * n0; v1 -= along * n1; v2 -= along * n2; v3 -= along * n3;
                if (have > 0)
                {
                    double p = v0 * b00 + v1 * b01 + v2 * b02 + v3 * b03;
                    v0 -= p * b00; v1 -= p * b01; v2 -= p * b02; v3 -= p * b03;
                }
                if (have > 1)
                {
                    double p = v0 * b10 + v1 * b11 + v2 * b12 + v3 * b13;
                    v0 -= p * b10; v1 -= p * b11; v2 -= p * b12; v3 -= p * b13;
                }
                double norm = Math.Sqrt(v0 * v0 + v1 * v1 + v2 * v2 + v3 * v3);
                if (norm <= 1e-6) continue;
                v0 /= norm; v1 /= norm; v2 /= norm; v3 /= norm;
                if (have == 0) { b00 = v0; b01 = v1; b02 = v2; b03 = v3; }
                else if (have == 1) { b10 = v0; b11 = v1; b12 = v2; b13 = v3; }
                else { b20 = v0; b21 = v1; b22 = v2; b23 = v3; }
                have++;
            }
            var f = new SliceFrame();
            f.Nx = (float)n0; f.Ny = (float)n1; f.Nz = (float)n2; f.Nw = (float)n3; f.Offset = (float)(d / len);
            f.E0x = (float)b00; f.E0y = (float)b01; f.E0z = (float)b02; f.E0w = (float)b03;
            f.E1x = (float)b10; f.E1y = (float)b11; f.E1z = (float)b12; f.E1w = (float)b13;
            f.E2x = (float)b20; f.E2y = (float)b21; f.E2z = (float)b22; f.E2w = (float)b23;
            return f;
        }

        /// <summary>True when every field is equal: the hyperplane and the picture are unchanged.</summary>
        public bool Same(in SliceFrame o)
        {
            return Nx == o.Nx && Ny == o.Ny && Nz == o.Nz && Nw == o.Nw && Offset == o.Offset
                && E0x == o.E0x && E0y == o.E0y && E0z == o.E0z && E0w == o.E0w
                && E1x == o.E1x && E1y == o.E1y && E1z == o.E1z && E1w == o.E1w
                && E2x == o.E2x && E2y == o.E2y && E2z == o.E2z && E2w == o.E2w;
        }
    }

    /// <summary>
    /// Cut of tetrahedra by a hyperplane (Appendix A.8): with f_i = n.x_i - w0 at each vertex, every edge whose
    /// ends have opposite signs gives the point x_i + f_i / (f_i - f_j) (x_j - x_i); three points make a
    /// triangle, four a quadrilateral (two triangles). Same rules as Phase 1 <c>Slicer4D</c>: a vertex exactly
    /// on the cut counts as positive; every edge is interpolated from its negative end to its positive end,
    /// so tetrahedra that share an edge compute bit-identical points; the output is a triangle soup, three
    /// vertices per triangle, wound outward when the tetrahedra are oriented outward.
    /// </summary>
    public static class ReferenceSlicer4D
    {
        public const int MaxTrianglesPerTet = 2;
        public const int MaxVerticesPerTet = 6;

        /// <summary>
        /// Slices tetrahedra [tetStart, tetEnd).
        /// Packed (fixedSlots false): triangles are written one after another from vertex <paramref name="firstVertex"/>;
        /// slicing stops before the first tetrahedron that would not fit.
        /// Fixed slots (fixedSlots true): tetrahedron t owns the six vertices from firstVertex + 6 (t - tetStart);
        /// its triangles come first and the rest of the slot is zero (degenerate triangles), so the vertex count
        /// of the receiving mesh never changes (section 10.2.5).
        /// Returns the number of vertices that belong to real triangles (3 per triangle).
        /// </summary>
        /// <param name="outPositions">3 floats per vertex.</param>
        /// <param name="outCells">One cell id per vertex, or null.</param>
        /// <param name="outData">One interpolated scalar per vertex, or null (also unused when vertexData is null).</param>
        public static int Slice(float[] vertices, int[] tetrahedra, int tetStart, int tetEnd, int[] tetCell, float[] vertexData,
                                in SliceFrame frame, float[] outPositions, int[] outCells, float[] outData,
                                int firstVertex, bool fixedSlots, out int tetsCut)
        {
            bool withData = vertexData != null && outData != null;
            int capacity = outPositions.Length / 3;
            Span<int> above = stackalloc int[4];
            Span<int> below = stackalloc int[4];
            Span<int> vi = stackalloc int[4];
            Span<float> f = stackalloc float[4];
            Span<float> q = stackalloc float[12];
            Span<float> qd = stackalloc float[4];
            float nx = frame.Nx, ny = frame.Ny, nz = frame.Nz, nw = frame.Nw, d0 = frame.Offset;
            int written = 0, cut = 0, packedAt = firstVertex;

            for (int t = tetStart; t < tetEnd; t++)
            {
                int nAbove = 0, nBelow = 0;
                for (int k = 0; k < 4; k++)
                {
                    int o = 4 * tetrahedra[4 * t + k];
                    vi[k] = o;
                    float fk = nx * vertices[o] + ny * vertices[o + 1] + nz * vertices[o + 2] + nw * vertices[o + 3] - d0;
                    f[k] = fk;
                    if (fk >= 0f) above[nAbove++] = k; else below[nBelow++] = k;
                }
                int slot = fixedSlots ? firstVertex + MaxVerticesPerTet * (t - tetStart) : packedAt;
                if (nAbove == 0 || nBelow == 0)
                {
                    if (fixedSlots) ClearSlot(outPositions, outCells, withData ? outData : null, slot, MaxVerticesPerTet);
                    continue;
                }
                int corners = nAbove == 2 ? 4 : 3;
                int need = (corners - 2) * 3;
                if (!fixedSlots && slot + need > capacity) break;

                // Corners of the cut in cyclic order.
                if (nBelow == 1)
                {
                    for (int k = 0; k < 3; k++) EdgePoint(vertices, vertexData, withData, vi, f, below[0], above[k], frame, q, qd, k);
                }
                else if (nAbove == 1)
                {
                    for (int k = 0; k < 3; k++) EdgePoint(vertices, vertexData, withData, vi, f, below[k], above[0], frame, q, qd, k);
                }
                else
                {
                    EdgePoint(vertices, vertexData, withData, vi, f, below[0], above[0], frame, q, qd, 0);
                    EdgePoint(vertices, vertexData, withData, vi, f, below[1], above[0], frame, q, qd, 1);
                    EdgePoint(vertices, vertexData, withData, vi, f, below[1], above[1], frame, q, qd, 2);
                    EdgePoint(vertices, vertexData, withData, vi, f, below[0], above[1], frame, q, qd, 3);
                }

                // Which way is out: the tetrahedron's 4D normal with its part along the cut's normal removed,
                // pictured like the points.
                int i0 = vi[0], i1 = vi[1], i2 = vi[2], i3 = vi[3];
                double tx, ty, tz, tw;
                Orientation.TetNormal(
                    vertices[i1] - vertices[i0], vertices[i1 + 1] - vertices[i0 + 1], vertices[i1 + 2] - vertices[i0 + 2], vertices[i1 + 3] - vertices[i0 + 3],
                    vertices[i2] - vertices[i0], vertices[i2 + 1] - vertices[i0 + 1], vertices[i2 + 2] - vertices[i0 + 2], vertices[i2 + 3] - vertices[i0 + 3],
                    vertices[i3] - vertices[i0], vertices[i3 + 1] - vertices[i0 + 1], vertices[i3 + 2] - vertices[i0 + 2], vertices[i3 + 3] - vertices[i0 + 3],
                    out tx, out ty, out tz, out tw);
                double along = tx * nx + ty * ny + tz * nz + tw * nw;
                tx -= along * nx; ty -= along * ny; tz -= along * nz; tw -= along * nw;
                double ox = frame.E0x * tx + frame.E0y * ty + frame.E0z * tz + frame.E0w * tw;
                double oy = frame.E1x * tx + frame.E1y * ty + frame.E1z * tz + frame.E1w * tw;
                double oz = frame.E2x * tx + frame.E2y * ty + frame.E2z * tz + frame.E2w * tw;

                // For a quadrilateral the cross product of its diagonals is the normal of its plane.
                double ux, uy, uz, vx, vy, vz;
                if (corners == 4)
                {
                    ux = (double)q[6] - q[0]; uy = (double)q[7] - q[1]; uz = (double)q[8] - q[2];
                    vx = (double)q[9] - q[3]; vy = (double)q[10] - q[4]; vz = (double)q[11] - q[5];
                }
                else
                {
                    ux = (double)q[3] - q[0]; uy = (double)q[4] - q[1]; uz = (double)q[5] - q[2];
                    vx = (double)q[6] - q[0]; vy = (double)q[7] - q[1]; vz = (double)q[8] - q[2];
                }
                bool flip = (uy * vz - uz * vy) * ox + (uz * vx - ux * vz) * oy + (ux * vy - uy * vx) * oz < 0.0;
                int cell = tetCell != null ? tetCell[t] : 0;

                int w = slot;
                for (int tri = 0; tri < corners - 2; tri++)
                {
                    int ib = flip ? tri + 2 : tri + 1;
                    int ic = flip ? tri + 1 : tri + 2;
                    Put(outPositions, outCells, outData, withData, w, q, qd, 0, cell);
                    Put(outPositions, outCells, outData, withData, w + 1, q, qd, ib, cell);
                    Put(outPositions, outCells, outData, withData, w + 2, q, qd, ic, cell);
                    w += 3;
                }
                if (fixedSlots)
                {
                    if (need < MaxVerticesPerTet) ClearSlot(outPositions, outCells, withData ? outData : null, slot + need, MaxVerticesPerTet - need);
                }
                else packedAt = w;
                written += need;
                cut++;
            }
            tetsCut = cut;
            return written;
        }

        // Where the edge from corner a (negative side) to corner b (positive side) meets the cut.
        static void EdgePoint(float[] v, float[] data, bool withData, Span<int> vi, Span<float> f, int a, int b,
                              in SliceFrame frame, Span<float> q, Span<float> qd, int corner)
        {
            int ia = vi[a], ib = vi[b];
            float s = f[a] / (f[a] - f[b]);
            float px, py, pz, pw;
            if (s == 1f)
            {
                // The positive end lies on the cut (f_b = 0). a + 1 * (b - a) is not always b in floating
                // point, so take b itself: a cut exactly at a ring then returns the ring's own vertices and
                // the tetrahedra that only touch the cut give exactly degenerate triangles.
                px = v[ib]; py = v[ib + 1]; pz = v[ib + 2]; pw = v[ib + 3];
            }
            else
            {
                px = v[ia] + s * (v[ib] - v[ia]);
                py = v[ia + 1] + s * (v[ib + 1] - v[ia + 1]);
                pz = v[ia + 2] + s * (v[ib + 2] - v[ia + 2]);
                pw = v[ia + 3] + s * (v[ib + 3] - v[ia + 3]);
            }
            q[3 * corner] = frame.E0x * px + frame.E0y * py + frame.E0z * pz + frame.E0w * pw;
            q[3 * corner + 1] = frame.E1x * px + frame.E1y * py + frame.E1z * pz + frame.E1w * pw;
            q[3 * corner + 2] = frame.E2x * px + frame.E2y * py + frame.E2z * pz + frame.E2w * pw;
            if (withData)
            {
                float da = data[ia >> 2], db = data[ib >> 2];
                qd[corner] = da + s * (db - da);
            }
        }

        static void Put(float[] pos, int[] cells, float[] data, bool withData, int at, Span<float> q, Span<float> qd, int corner, int cell)
        {
            pos[3 * at] = q[3 * corner]; pos[3 * at + 1] = q[3 * corner + 1]; pos[3 * at + 2] = q[3 * corner + 2];
            if (cells != null) cells[at] = cell;
            if (withData) data[at] = qd[corner];
        }

        static void ClearSlot(float[] pos, int[] cells, float[] data, int at, int count)
        {
            Array.Clear(pos, 3 * at, 3 * count);
            if (cells != null) Array.Clear(cells, at, count);
            if (data != null) Array.Clear(data, at, count);
        }

        /// <summary>Sum of the triangle areas (three vertices per triangle, 3 floats per vertex).</summary>
        public static double Area(float[] positions, int firstVertex, int vertexCount)
        {
            double area = 0.0;
            for (int i = firstVertex; i + 2 < firstVertex + vertexCount; i += 3)
            {
                double ax = positions[3 * i], ay = positions[3 * i + 1], az = positions[3 * i + 2];
                double ux = positions[3 * i + 3] - ax, uy = positions[3 * i + 4] - ay, uz = positions[3 * i + 5] - az;
                double vx = positions[3 * i + 6] - ax, vy = positions[3 * i + 7] - ay, vz = positions[3 * i + 8] - az;
                double cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
                area += 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
            }
            return area;
        }

        /// <summary>Volume enclosed by a closed, outward-wound triangle soup (negative when wound inward).</summary>
        public static double SignedVolume(float[] positions, int firstVertex, int vertexCount)
        {
            double six = 0.0;
            for (int i = firstVertex; i + 2 < firstVertex + vertexCount; i += 3)
            {
                double ax = positions[3 * i], ay = positions[3 * i + 1], az = positions[3 * i + 2];
                double bx = positions[3 * i + 3], by = positions[3 * i + 4], bz = positions[3 * i + 5];
                double cx = positions[3 * i + 6], cy = positions[3 * i + 7], cz = positions[3 * i + 8];
                six += ax * (by * cz - bz * cy) + ay * (bz * cx - bx * cz) + az * (bx * cy - by * cx);
            }
            return six / 6.0;
        }

        /// <summary>
        /// Smallest and largest coordinate along one picture axis (0, 1, 2) over the vertices of real triangles.
        /// Zero-area triangles (the empty parts of fixed slots) are skipped. False when there is none.
        /// </summary>
        public static bool Extent(float[] positions, int firstVertex, int vertexCount, int axis, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            bool any = false;
            for (int i = firstVertex; i + 2 < firstVertex + vertexCount; i += 3)
            {
                int o = 3 * i;
                bool degenerate = positions[o] == positions[o + 3] && positions[o + 1] == positions[o + 4] && positions[o + 2] == positions[o + 5]
                               && positions[o] == positions[o + 6] && positions[o + 1] == positions[o + 7] && positions[o + 2] == positions[o + 8];
                if (degenerate) continue;
                for (int k = 0; k < 3; k++)
                {
                    float s = positions[o + 3 * k + axis];
                    if (s < min) min = s;
                    if (s > max) max = s;
                }
                any = true;
            }
            if (!any) { min = 0f; max = 0f; }
            return any;
        }
    }

    /// <summary>
    /// Per-cell tetrahedron ranges and bounding boxes of a <see cref="TubeMesh"/>, so that a cut visits only the
    /// cells it can meet: for a pure phase slice (tau = const) that is one slab in ten (section 10.2.5).
    /// Built once at load time; the queries do not allocate.
    /// </summary>
    public sealed class SlabIndex
    {
        public readonly int CellCount;
        /// <summary>CellCount + 1 tetrahedron indices (the mesh's own array).</summary>
        public readonly int[] Start;
        readonly float[] lo, hi;    // 4 floats per cell

        public SlabIndex(TubeMesh mesh)
        {
            CellCount = mesh.CellCount;
            Start = mesh.SlabStart;
            lo = new float[4 * CellCount];
            hi = new float[4 * CellCount];
            for (int c = 0; c < CellCount; c++)
            {
                for (int a = 0; a < 4; a++) { lo[4 * c + a] = float.MaxValue; hi[4 * c + a] = float.MinValue; }
                for (int i = 4 * Start[c]; i < 4 * Start[c + 1]; i++)
                {
                    int o = 4 * mesh.Tetrahedra[i];
                    for (int a = 0; a < 4; a++)
                    {
                        float x = mesh.Vertices[o + a];
                        if (x < lo[4 * c + a]) lo[4 * c + a] = x;
                        if (x > hi[4 * c + a]) hi[4 * c + a] = x;
                    }
                }
            }
        }

        public int TetStart(int cell) { return Start[cell]; }
        public int TetEnd(int cell) { return Start[cell + 1]; }
        public float WMin(int cell) { return lo[4 * cell + 3]; }
        public float WMax(int cell) { return hi[4 * cell + 3]; }

        /// <summary>
        /// False only when no tetrahedron of the cell can be cut. A tetrahedron is cut when some vertex has
        /// f &lt; 0 and some f &gt;= 0, so the cell is met when min(n.x) &lt; offset &lt;= max(n.x) over its
        /// box. For a normal along one axis the test is exact (a cut that lies exactly on the ring between two
        /// slabs meets only the lower one); otherwise it is widened by a rounding margin.
        /// </summary>
        public bool Touches(int cell, in SliceFrame frame)
        {
            int o = 4 * cell;
            float min = 0f, max = 0f, scale = 0f;
            int axes = 0;
            Add(frame.Nx, lo[o], hi[o], ref min, ref max, ref scale, ref axes);
            Add(frame.Ny, lo[o + 1], hi[o + 1], ref min, ref max, ref scale, ref axes);
            Add(frame.Nz, lo[o + 2], hi[o + 2], ref min, ref max, ref scale, ref axes);
            Add(frame.Nw, lo[o + 3], hi[o + 3], ref min, ref max, ref scale, ref axes);
            float margin = axes > 1 ? 1e-5f * (scale + Math.Abs(frame.Offset)) : 0f;
            return min - margin < frame.Offset && frame.Offset <= max + margin;
        }

        static void Add(float n, float lo, float hi, ref float min, ref float max, ref float scale, ref int axes)
        {
            if (n == 0f) return;
            float a = n * lo, b = n * hi;
            if (a < b) { min += a; max += b; } else { min += b; max += a; }
            scale += Math.Max(Math.Abs(a), Math.Abs(b));
            axes++;
        }
    }

    /// <summary>
    /// The live slice of one tube with fixed slots (the engine-free part of P2-A21's TubeSlicer): the buffers
    /// are allocated once, an unchanged hyperplane costs nothing, and only the slabs the cut meets are visited.
    /// The Unity adapter copies <see cref="Positions"/> (or just the dirty range) into the mesh's vertex buffer.
    /// </summary>
    public sealed class TubeSliceState
    {
        public readonly TubeMesh Mesh;
        public readonly SlabIndex Index;
        /// <summary>18 floats per tetrahedron: the fixed slot of tetrahedron t starts at vertex 6 t.</summary>
        public readonly float[] Positions;
        public readonly float[] Data;
        public readonly int[] Cells;
        readonly bool[] written;
        SliceFrame last;
        bool hasLast;

        /// <summary>Counters of the last <see cref="Update"/>.</summary>
        public int SlabsTouched, TetsVisited, TetsCut, TriangleCount;
        /// <summary>Vertex range [DirtyFirst, DirtyEnd) changed by the last Update; empty when nothing changed.</summary>
        public int DirtyFirst, DirtyEnd;

        public TubeSliceState(TubeMesh mesh)
        {
            Mesh = mesh;
            Index = new SlabIndex(mesh);
            Positions = new float[3 * mesh.MaxSliceVertices];
            Data = mesh.VertexData != null ? new float[mesh.MaxSliceVertices] : null;
            Cells = new int[mesh.MaxSliceVertices];
            written = new bool[mesh.CellCount];
        }

        /// <summary>Re-slices when the frame differs from the last one. Returns false, having done no work, when it does not.</summary>
        public bool Update(in SliceFrame frame)
        {
            DirtyFirst = 0; DirtyEnd = 0;
            if (hasLast && frame.Same(last))
            {
                SlabsTouched = 0; TetsVisited = 0;
                return false;
            }
            last = frame; hasLast = true;
            SlabsTouched = 0; TetsVisited = 0; TetsCut = 0; TriangleCount = 0;
            int first = int.MaxValue, end = 0;
            for (int c = 0; c < Index.CellCount; c++)
            {
                int t0 = Index.Start[c], t1 = Index.Start[c + 1];
                if (Index.Touches(c, frame))
                {
                    int cut;
                    int n = ReferenceSlicer4D.Slice(Mesh.Vertices, Mesh.Tetrahedra, t0, t1, Mesh.TetCell, Mesh.VertexData, frame,
                                                    Positions, Cells, Data, 6 * t0, true, out cut);
                    written[c] = true;
                    SlabsTouched++; TetsVisited += t1 - t0; TetsCut += cut; TriangleCount += n / 3;
                }
                else if (written[c])
                {
                    Array.Clear(Positions, 18 * t0, 18 * (t1 - t0));
                    Array.Clear(Cells, 6 * t0, 6 * (t1 - t0));
                    if (Data != null) Array.Clear(Data, 6 * t0, 6 * (t1 - t0));
                    written[c] = false;
                }
                else continue;
                if (6 * t0 < first) first = 6 * t0;
                if (6 * t1 > end) end = 6 * t1;
            }
            if (end > 0) { DirtyFirst = first; DirtyEnd = end; }
            return true;
        }
    }
}
