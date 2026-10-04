// Lane A engine-free core, P2-A20 (TubeLoader). Plain C#, System only, no Unity references.
using System;
using System.Collections.Generic;
using Atlas.Cases;

namespace Atlas.Lung.Core
{
    /// <summary>
    /// Tetrahedra in R^4 as flat arrays, laid out and named like the Phase 1 <c>Atlas.Kernel.FourD.Mesh4D</c>:
    /// <c>new Mesh4D(vertices, Tetrahedra, TetCell, null, CellCount, WPeriod, VertexData)</c>, where
    /// <c>vertices</c> is <see cref="Vertices"/> read four floats at a time (a float4 is four packed floats).
    /// Immutable once built. The tetrahedra of one cell are contiguous: cell c is the range
    /// [<see cref="SlabStart"/>[c], SlabStart[c + 1]).
    /// </summary>
    public sealed class TubeMesh
    {
        /// <summary>4 floats per vertex: x, y, z, w. For a case tube: display metres, w = tau.</summary>
        public float[] Vertices;
        /// <summary>4 vertex indices per tetrahedron.</summary>
        public int[] Tetrahedra;
        /// <summary>One cell id per tetrahedron. For a tube: the slab (phase layer) k.</summary>
        public int[] TetCell;
        /// <summary>One scalar per vertex. For a tube: the breathing phase tau / Lambda in [0, 1].</summary>
        public float[] VertexData;
        public int CellCount;
        /// <summary>Greater than 0 when w wraps (Lambda for a tube); 0 otherwise.</summary>
        public float WPeriod;
        /// <summary>CellCount + 1 tetrahedron indices.</summary>
        public int[] SlabStart;
        /// <summary>Display magnification already applied to x, y, z (15 for a case tube, 1 for generated meshes).</summary>
        public float M = 1f;
        /// <summary>Display units per millimetre: M / 1000 for a case tube.</summary>
        public float UnitsPerMm = 1f;
        /// <summary>True when every tetrahedron is oriented so that its 4D normal points out of the body.</summary>
        public bool Oriented;

        public int VertexCount { get { return Vertices.Length / 4; } }
        public int TetCount { get { return TetCell.Length; } }
        /// <summary>Vertices of a slice buffer with one fixed slot of two triangles per tetrahedron.</summary>
        public int MaxSliceVertices { get { return ReferenceSlicer4D.MaxVerticesPerTet * TetCount; } }

        /// <summary>
        /// The w of breathing phase <paramref name="phase"/> (in phase bins, any real number), wrapped into
        /// (0, WPeriod]. Phase 0 gives WPeriod: the ring that repeats ring 0, so the cut lies in the last slab.
        /// At a whole phase k the value is bit-identical to the tau the contract stores for ring k.
        /// </summary>
        public float PhaseW(double phase)
        {
            double n = CellCount;
            double p = phase - Math.Floor(phase / n) * n;
            if (p <= 0.0) p = n;
            return (float)((double)WPeriod * p / n);
        }

        /// <summary>w wrapped into (0, WPeriod].</summary>
        public float WrapW(double w)
        {
            double period = WPeriod;
            double r = w - Math.Floor(w / period) * period;
            if (r <= 0.0) r = period;
            return (float)r;
        }
    }

    /// <summary>P2-A20: <c>tube4d.bin</c> (read by <see cref="Tube4D"/>) to the arrays of a Mesh4D. Load time only: allocates.</summary>
    public static class TubeLoader
    {
        /// <summary>
        /// Copies the tube into Mesh4D-shaped arrays, groups the tetrahedra by slab (the contract already stores
        /// them so), and orients every tetrahedron outward, which the contract leaves open and the Phase 1
        /// slicer needs for its winding.
        /// </summary>
        public static TubeMesh Load(Tube4D tube, bool orientOutward = true)
        {
            if (tube == null) throw new ArgumentNullException("tube");
            tube.Validate();
            int layers = tube.Layers;
            int tets = tube.TetCount;
            var mesh = new TubeMesh();
            mesh.M = tube.M;
            mesh.UnitsPerMm = tube.M / 1000f;
            mesh.WPeriod = tube.Lambda;
            mesh.CellCount = layers;
            mesh.Vertices = (float[])tube.P.Clone();
            mesh.VertexData = new float[tube.VertexCount];
            for (int i = 0; i < mesh.VertexData.Length; i++)
                mesh.VertexData[i] = (float)((double)tube.P[4 * i + 3] / (double)tube.Lambda);

            // Counting sort by slab. Stable, and the identity when the file is stored slab by slab.
            mesh.SlabStart = new int[layers + 1];
            for (int t = 0; t < tets; t++) mesh.SlabStart[tube.Slab[t] + 1]++;
            for (int k = 0; k < layers; k++) mesh.SlabStart[k + 1] += mesh.SlabStart[k];
            var next = new int[layers];
            Array.Copy(mesh.SlabStart, next, layers);
            mesh.Tetrahedra = new int[4 * tets];
            mesh.TetCell = new int[tets];
            for (int t = 0; t < tets; t++)
            {
                int k = tube.Slab[t];
                int to = next[k]++;
                Array.Copy(tube.T, 4 * t, mesh.Tetrahedra, 4 * to, 4);
                mesh.TetCell[to] = k;
            }
            if (orientOutward)
            {
                Orientation.OrientOutward(mesh.Vertices, mesh.Tetrahedra);
                mesh.Oriented = true;
            }
            return mesh;
        }

        public static TubeMesh Load(byte[] tube4dFile, bool orientOutward = true)
        {
            return Load(Tube4D.Read(tube4dFile), orientOutward);
        }

        /// <summary>Slab starts of a mesh whose cells are already contiguous and ascending.</summary>
        public static int[] SlabStarts(int[] tetCell, int cellCount)
        {
            var start = new int[cellCount + 1];
            int prev = 0;
            for (int t = 0; t < tetCell.Length; t++)
            {
                if (tetCell[t] < prev || tetCell[t] >= cellCount) throw new ArgumentException("Cells must be contiguous and ascending.");
                prev = tetCell[t];
                start[tetCell[t] + 1]++;
            }
            for (int k = 0; k < cellCount; k++) start[k + 1] += start[k];
            return start;
        }
    }

    /// <summary>Tetrahedron orientation. Load time only: allocates.</summary>
    public static class Orientation
    {
        /// <summary>
        /// The vector orthogonal to three edge vectors a, b, c (4 doubles each at the given offsets): the same
        /// formula and sign as Phase 1 <c>Mesh4D.TetNormal</c>. Its length is six times the tetrahedron's volume.
        /// </summary>
        public static void TetNormal(double ax, double ay, double az, double aw, double bx, double by, double bz, double bw,
                                     double cx, double cy, double cz, double cw,
                                     out double nx, out double ny, out double nz, out double nw)
        {
            nx = Det(ay, az, aw, by, bz, bw, cy, cz, cw);
            ny = -Det(ax, az, aw, bx, bz, bw, cx, cz, cw);
            nz = Det(ax, ay, aw, bx, by, bw, cx, cy, cw);
            nw = -Det(ax, ay, az, bx, by, bz, cx, cy, cz);
        }

        // dot(a, cross(b, c))
        static double Det(double a0, double a1, double a2, double b0, double b1, double b2, double c0, double c1, double c2)
        {
            return a0 * (b1 * c2 - b2 * c1) + a1 * (b2 * c0 - b0 * c2) + a2 * (b0 * c1 - b1 * c0);
        }

        /// <summary>Normal of tetrahedron t, from its vertex order (not normalised).</summary>
        public static void TetNormal(float[] v, int[] tets, int t, out double nx, out double ny, out double nz, out double nw)
        {
            int i0 = 4 * tets[4 * t], i1 = 4 * tets[4 * t + 1], i2 = 4 * tets[4 * t + 2], i3 = 4 * tets[4 * t + 3];
            TetNormal((double)v[i1] - v[i0], (double)v[i1 + 1] - v[i0 + 1], (double)v[i1 + 2] - v[i0 + 2], (double)v[i1 + 3] - v[i0 + 3],
                      (double)v[i2] - v[i0], (double)v[i2 + 1] - v[i0 + 1], (double)v[i2 + 2] - v[i0 + 2], (double)v[i2 + 3] - v[i0 + 3],
                      (double)v[i3] - v[i0], (double)v[i3 + 1] - v[i0 + 1], (double)v[i3 + 2] - v[i0 + 2], (double)v[i3 + 3] - v[i0 + 3],
                      out nx, out ny, out nz, out nw);
        }

        /// <summary>
        /// Reorders the indices of tetrahedra (swapping the last two) so that all are oriented coherently and
        /// their normals point out of the body. Works for any hypersurface in which every face belongs to one
        /// or two tetrahedra: a closed polytope boundary, or a tube open at its two w ends.
        /// Coherence comes from walking across shared faces (neighbours must induce opposite orders on the
        /// face they share); the side comes from the flux of (x, y, z) through each connected part, which is
        /// three times the enclosed volume and so positive for outward normals, wherever the origin is.
        /// Returns the number of tetrahedra flipped.
        /// </summary>
        public static int OrientOutward(float[] vertices, int[] tets)
        {
            int count = tets.Length / 4;
            var adjacent = new int[4 * count];      // (tet * 4 + corner) of the neighbour across each face, or -1
            var sign = new sbyte[4 * count];        // induced orientation of each face against its sorted order
            var seen = new Dictionary<long, int>(2 * count);
            for (int i = 0; i < adjacent.Length; i++) adjacent[i] = -1;
            for (int t = 0; t < count; t++)
            {
                for (int corner = 0; corner < 4; corner++)
                {
                    int a = tets[4 * t + (corner == 0 ? 1 : 0)];
                    int b = tets[4 * t + (corner <= 1 ? 2 : 1)];
                    int c = tets[4 * t + (corner <= 2 ? 3 : 2)];
                    int s = (corner & 1) == 0 ? 1 : -1;
                    if (a > b) { int x = a; a = b; b = x; s = -s; }
                    if (b > c) { int x = b; b = c; c = x; s = -s; }
                    if (a > b) { int x = a; a = b; b = x; s = -s; }
                    if (a == b || b == c) throw new ArgumentException("Degenerate tetrahedron " + t + ".");
                    sign[4 * t + corner] = (sbyte)s;
                    long key = ((long)a << 42) | ((long)b << 21) | (long)c;
                    int other;
                    if (seen.TryGetValue(key, out other))
                    {
                        if (other < 0) throw new ArgumentException("A face is shared by more than two tetrahedra.");
                        adjacent[4 * t + corner] = other;
                        adjacent[other] = 4 * t + corner;
                        seen[key] = -1;
                    }
                    else seen[key] = 4 * t + corner;
                }
            }
            if (vertices.Length / 4 >= (1 << 21)) throw new ArgumentException("Too many vertices.");

            var flip = new sbyte[count];            // 0 = not reached, 1 = keep, -1 = flip
            var queue = new int[count];
            var part = new List<int>();
            int flipped = 0;
            for (int seed = 0; seed < count; seed++)
            {
                if (flip[seed] != 0) continue;
                int head = 0, tail = 0;
                queue[tail++] = seed;
                flip[seed] = 1;
                part.Clear();
                double flux = 0.0;
                while (head < tail)
                {
                    int t = queue[head++];
                    part.Add(t);
                    double nx, ny, nz, nw;
                    TetNormal(vertices, tets, t, out nx, out ny, out nz, out nw);
                    double cx = 0.0, cy = 0.0, cz = 0.0;
                    for (int k = 0; k < 4; k++)
                    {
                        int o = 4 * tets[4 * t + k];
                        cx += vertices[o]; cy += vertices[o + 1]; cz += vertices[o + 2];
                    }
                    flux += flip[t] * 0.25 * (nx * cx + ny * cy + nz * cz);
                    for (int corner = 0; corner < 4; corner++)
                    {
                        int n = adjacent[4 * t + corner];
                        if (n < 0) continue;
                        int u = n >> 2;
                        // Coherent when the two induced orders are opposite.
                        sbyte want = (sbyte)(sign[4 * t + corner] * flip[t] * sign[n] < 0 ? 1 : -1);
                        if (flip[u] == 0) { flip[u] = want; queue[tail++] = u; }
                        else if (flip[u] != want) throw new ArgumentException("The mesh is not orientable.");
                    }
                }
                bool reverse = flux < 0.0;
                for (int i = 0; i < part.Count; i++)
                {
                    int t = part[i];
                    if ((flip[t] < 0) != reverse)
                    {
                        int x = tets[4 * t + 2]; tets[4 * t + 2] = tets[4 * t + 3]; tets[4 * t + 3] = x;
                        flipped++;
                    }
                }
            }
            return flipped;
        }
    }
}
