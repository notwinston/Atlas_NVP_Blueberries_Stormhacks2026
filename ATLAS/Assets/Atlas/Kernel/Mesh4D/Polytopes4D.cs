using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>
    /// Builders for the 4D bodies of the Slicer (Master Plan section 5.5): the regular polytopes of the toybox,
    /// a 4D ball, and the worldtube of a moving surface in R^3 x S^1. All run at load time and allocate.
    /// Every result is the boundary of its body, with tetrahedra oriented outward.
    /// </summary>
    public static class Polytopes4D
    {
        private static readonly int[] KuhnOrders = { 0, 1, 2, 0, 2, 1, 1, 0, 2, 1, 2, 0, 2, 0, 1, 2, 1, 0 };

        /// <summary>
        /// The tesseract of edge 1 centred on the origin, vertices (+-0.5, +-0.5, +-0.5, +-0.5), circumradius 1:
        /// 16 vertices, 32 edges, 8 cubic cells each split into 6 Kuhn tetrahedra, 48 in all.
        /// Cell id = 2 * axis + side, for the cell on which coordinate <c>axis</c> is -0.5 (side 0) or +0.5 (side 1).
        /// </summary>
        public static Mesh4D Tesseract()
        {
            return CubeBoundary(1, false);
        }

        /// <summary>
        /// The regular 5-cell with circumradius 1: 5 vertices, 10 edges, 5 tetrahedral cells. Cell k is the one
        /// opposite vertex k.
        /// </summary>
        public static Mesh4D Cell5()
        {
            float s = 1f / math.sqrt(3.2f);
            float h = 1f / math.sqrt(5f);
            var vertices = new[]
            {
                s * new float4(1f, 1f, 1f, -h),
                s * new float4(1f, -1f, -1f, -h),
                s * new float4(-1f, 1f, -1f, -h),
                s * new float4(-1f, -1f, 1f, -h),
                s * new float4(0f, 0f, 0f, 4f * h),
            };

            var tetrahedra = new int[20];
            var cells = new int[5];
            for (int skip = 0; skip < 5; skip++)
            {
                int o = skip * 4;
                for (int i = 0; i < 5; i++)
                {
                    if (i != skip) tetrahedra[o++] = i;
                }
                cells[skip] = skip;
            }

            OrientOutward(vertices, tetrahedra, float4.zero);
            return new Mesh4D(vertices, tetrahedra, cells, EdgesOfLength(vertices, math.distance(vertices[0], vertices[1])), 5);
        }

        /// <summary>
        /// The regular 16-cell with circumradius 1 (vertices on the coordinate axes): 8 vertices, 24 edges,
        /// 16 tetrahedral cells. Bit k of the cell id is set when the cell's vertex on axis k is the negative one.
        /// </summary>
        public static Mesh4D Cell16()
        {
            var vertices = new float4[8];
            for (int axis = 0; axis < 4; axis++)
            {
                float4 p = default;
                p[axis] = 1f;
                vertices[axis * 2] = p;
                vertices[axis * 2 + 1] = -p;
            }

            var tetrahedra = new int[64];
            var cells = new int[16];
            for (int signs = 0; signs < 16; signs++)
            {
                for (int axis = 0; axis < 4; axis++)
                    tetrahedra[signs * 4 + axis] = axis * 2 + ((signs >> axis) & 1);
                cells[signs] = signs;
            }

            OrientOutward(vertices, tetrahedra, float4.zero);
            return new Mesh4D(vertices, tetrahedra, cells, EdgesOfLength(vertices, math.SQRT2), 16);
        }

        /// <summary>
        /// The regular 24-cell with circumradius 1, vertices the permutations of (+-1, +-1, 0, 0) / sqrt(2):
        /// 24 vertices, 96 edges, 24 octahedral cells each split into 4 tetrahedra about one diagonal, 96 in
        /// all. Cells 0-7 face the coordinate axes, cells 8-23 the sixteen diagonals.
        /// </summary>
        public static Mesh4D Cell24()
        {
            var vertices = new float4[24];
            int count = 0;
            float r = 1f / math.SQRT2;
            for (int i = 0; i < 4; i++)
            {
                for (int j = i + 1; j < 4; j++)
                {
                    for (int signs = 0; signs < 4; signs++)
                    {
                        float4 p = default;
                        p[i] = (signs & 1) == 0 ? r : -r;
                        p[j] = (signs & 2) == 0 ? r : -r;
                        vertices[count++] = p;
                    }
                }
            }

            var directions = new float4[24];
            for (int axis = 0; axis < 4; axis++)
            {
                float4 p = default;
                p[axis] = 1f;
                directions[axis * 2] = p;
                directions[axis * 2 + 1] = -p;
            }
            for (int signs = 0; signs < 16; signs++)
            {
                directions[8 + signs] = 0.5f * new float4(
                    (signs & 1) == 0 ? 1f : -1f, (signs & 2) == 0 ? 1f : -1f,
                    (signs & 4) == 0 ? 1f : -1f, (signs & 8) == 0 ? 1f : -1f);
            }

            var tetrahedra = new int[96 * 4];
            var cells = new int[96];
            var corner = new int[6];
            for (int cell = 0; cell < 24; cell++)
            {
                // The six vertices of this octahedron are the ones furthest along the cell's direction.
                int found = 0;
                float4 centre = float4.zero;
                for (int i = 0; i < 24; i++)
                {
                    if (math.dot(directions[cell], vertices[i]) > r - 1e-3f)
                    {
                        if (found == 6) throw new InvalidOperationException("24-cell: a cell has more than six vertices.");
                        corner[found++] = i;
                        centre += vertices[i];
                    }
                }
                if (found != 6) throw new InvalidOperationException("24-cell: a cell does not have six vertices.");
                centre /= 6f;

                // Order them as an axis (0, 1) and the square around it (2, 3, 4, 5).
                MoveOppositeTo(vertices, corner, centre, 0, 1);
                MoveOppositeTo(vertices, corner, centre, 2, 4);

                for (int k = 0; k < 4; k++)
                {
                    int t = cell * 4 + k;
                    tetrahedra[t * 4] = corner[0];
                    tetrahedra[t * 4 + 1] = corner[1];
                    tetrahedra[t * 4 + 2] = corner[2 + k];
                    tetrahedra[t * 4 + 3] = corner[2 + (k + 1) % 4];
                    cells[t] = cell;
                }
            }

            OrientOutward(vertices, tetrahedra, float4.zero);
            return new Mesh4D(vertices, tetrahedra, cells, EdgesOfLength(vertices, 1f), 24);
        }

        /// <summary>Boundary of the unit 4D ball: the tesseract's cells cut n times per side and pushed onto the 3-sphere.</summary>
        /// <remarks>
        /// 48 * n^3 tetrahedra, 8 cells, no edges. Slice vertices lie on chords of the sphere, so a slice at
        /// offset c is smaller than the true one by roughly 0.9 / (n^2 * (1 - c^2)) of its radius: n = 24
        /// keeps every vertex within 1% up to c = 0.9, and 6 to 8 is plenty for display.
        /// </remarks>
        public static Mesh4D Ball(int n)
        {
            if (n < 1) throw new ArgumentOutOfRangeException(nameof(n));
            return CubeBoundary(n, true);
        }

        /// <summary>
        /// A closed triangle surface carried through a periodic motion (Appendix A.8): ring k is the surface moved by
        /// phaseOffsets[k] at w = k * wPeriod / phaseOffsets.Length; one more ring repeats ring 0 at w = wPeriod.
        /// Each triangle between two rings is a prism split into three tetrahedra with its indices sorted.
        /// Cell id = the phase interval. Vertex data = the phase, w / wPeriod.
        /// </summary>
        /// <remarks>
        /// Tetrahedra are oriented away from the mean of the surface's vertices at the same instant, which is
        /// outward for any surface that is star-shaped about that point.
        /// </remarks>
        public static Mesh4D Tube(float3[] surfaceVertices, int[] surfaceTriangles, float3[] phaseOffsets, float wPeriod)
        {
            if (surfaceVertices == null) throw new ArgumentNullException(nameof(surfaceVertices));
            if (surfaceTriangles == null) throw new ArgumentNullException(nameof(surfaceTriangles));
            if (phaseOffsets == null) throw new ArgumentNullException(nameof(phaseOffsets));
            if (phaseOffsets.Length < 1) throw new ArgumentException("A tube needs at least one phase.", nameof(phaseOffsets));
            if (surfaceTriangles.Length % 3 != 0) throw new ArgumentException("Triangles are index triples.", nameof(surfaceTriangles));
            if (!(wPeriod > 0f)) throw new ArgumentOutOfRangeException(nameof(wPeriod));
            foreach (int index in surfaceTriangles)
            {
                if ((uint)index >= (uint)surfaceVertices.Length) throw new ArgumentException("Triangle index out of range.", nameof(surfaceTriangles));
            }

            int phases = phaseOffsets.Length;
            int ring = surfaceVertices.Length;
            int triangleCount = surfaceTriangles.Length / 3;

            var vertices = new float4[(phases + 1) * ring];
            var data = new float[vertices.Length];
            for (int k = 0; k <= phases; k++)
            {
                float3 offset = phaseOffsets[k % phases];
                float phase = (float)k / phases;
                for (int i = 0; i < ring; i++)
                {
                    vertices[k * ring + i] = new float4(surfaceVertices[i] + offset, phase * wPeriod);
                    data[k * ring + i] = phase;
                }
            }

            var tetrahedra = new int[phases * triangleCount * 12];
            var cells = new int[phases * triangleCount * 3];
            int o = 0;
            for (int k = 0; k < phases; k++)
            {
                int lo = k * ring, hi = lo + ring;
                for (int t = 0; t < triangleCount; t++)
                {
                    // Sorting the indices makes neighbouring prisms cut their shared quadrilateral the same way.
                    var (a, b, c) = Mesh4D.SortedFace(surfaceTriangles[t * 3], surfaceTriangles[t * 3 + 1], surfaceTriangles[t * 3 + 2]);
                    cells[o / 4] = k;
                    tetrahedra[o++] = lo + a; tetrahedra[o++] = lo + b; tetrahedra[o++] = lo + c; tetrahedra[o++] = hi + c;
                    cells[o / 4] = k;
                    tetrahedra[o++] = lo + a; tetrahedra[o++] = lo + b; tetrahedra[o++] = hi + b; tetrahedra[o++] = hi + c;
                    cells[o / 4] = k;
                    tetrahedra[o++] = lo + a; tetrahedra[o++] = hi + a; tetrahedra[o++] = hi + b; tetrahedra[o++] = hi + c;
                }
            }

            float3 middle = float3.zero;
            foreach (float3 p in surfaceVertices) middle += p;
            middle /= ring;

            for (int t = 0; t < cells.Length; t++)
            {
                int k = cells[t];
                float4 v0 = vertices[tetrahedra[t * 4]], v1 = vertices[tetrahedra[t * 4 + 1]];
                float4 v2 = vertices[tetrahedra[t * 4 + 2]], v3 = vertices[tetrahedra[t * 4 + 3]];
                float4 centroid = 0.25f * (v0 + v1 + v2 + v3);
                float along = centroid.w / wPeriod * phases - k;
                float3 centre = middle + math.lerp(phaseOffsets[k], phaseOffsets[(k + 1) % phases], along);
                if (math.dot(Mesh4D.TetNormal(v1 - v0, v2 - v0, v3 - v0).xyz, centroid.xyz - centre) < 0f)
                    Flip(tetrahedra, t);
            }

            return new Mesh4D(vertices, tetrahedra, cells, null, phases, wPeriod, data);
        }

        /// <summary>
        /// Tube of a sphere of 600 triangles (20 segments, 16 bands), centred on the origin before the offsets,
        /// with its poles on the z axis (the scanner's table axis). Ten phases give 18,000 tetrahedra.
        /// </summary>
        public static Mesh4D MovingSphere(float diameter, float3[] phaseOffsets, float wPeriod)
        {
            const int Segments = 20, Bands = 16;
            float radius = 0.5f * diameter;

            // North pole, the rings between the bands, south pole.
            int ringCount = Bands - 1;
            var surface = new float3[ringCount * Segments + 2];
            surface[0] = new float3(0f, 0f, radius);
            for (int ring = 0; ring < ringCount; ring++)
            {
                math.sincos(math.PI * (ring + 1) / Bands, out float sinLat, out float cosLat);
                for (int k = 0; k < Segments; k++)
                {
                    math.sincos(2f * math.PI * k / Segments, out float sinLon, out float cosLon);
                    surface[1 + ring * Segments + k] = radius * new float3(sinLat * cosLon, sinLat * sinLon, cosLat);
                }
            }
            int south = surface.Length - 1;
            surface[south] = new float3(0f, 0f, -radius);

            var triangles = new List<int>(600 * 3);
            int last = 1 + (ringCount - 1) * Segments;
            for (int k = 0; k < Segments; k++)
            {
                int next = (k + 1) % Segments;
                triangles.Add(0); triangles.Add(1 + k); triangles.Add(1 + next);
                triangles.Add(south); triangles.Add(last + next); triangles.Add(last + k);
                for (int ring = 0; ring + 1 < ringCount; ring++)
                {
                    int a = 1 + ring * Segments + k, b = 1 + ring * Segments + next;
                    int c = a + Segments, d = b + Segments;
                    triangles.Add(a); triangles.Add(c); triangles.Add(d);
                    triangles.Add(a); triangles.Add(d); triangles.Add(b);
                }
            }

            return Tube(surface, triangles.ToArray(), phaseOffsets, wPeriod);
        }

        /// <summary>Offsets along +z for a constant speed: z = speed * k * period / phases.</summary>
        public static float3[] OffsetsConstantSpeed(int phases, float speed, float periodSeconds)
        {
            if (phases < 1) throw new ArgumentOutOfRangeException(nameof(phases));
            var offsets = new float3[phases];
            for (int k = 0; k < phases; k++)
                offsets[k] = new float3(0f, 0f, speed * k * periodSeconds / phases);
            return offsets;
        }

        /// <summary>Offsets of the illustrative breathing curve: z = -travel * cos^4(pi * k / phases) (Appendix A.8).</summary>
        public static float3[] OffsetsBreathing(int phases, float travel)
        {
            if (phases < 1) throw new ArgumentOutOfRangeException(nameof(phases));
            var offsets = new float3[phases];
            for (int k = 0; k < phases; k++)
            {
                float c = math.cos(math.PI * k / phases);
                offsets[k] = new float3(0f, 0f, -travel * c * c * c * c);
            }
            return offsets;
        }

        // The boundary of the 4D cube as a lattice: 8 cubic cells of n^3 small cubes, 6 Kuhn tetrahedra each.
        // Kuhn's split always cuts a square from its lowest to its highest corner, so neighbouring cubes and
        // neighbouring cells agree on every shared face.
        private static Mesh4D CubeBoundary(int n, bool sphere)
        {
            int side = n + 1;
            var lookup = new Dictionary<int, int>();
            var vertices = new List<float4>();
            var tetrahedra = new int[48 * n * n * n * 4];
            var cells = new int[48 * n * n * n];
            int o = 0;

            int Vertex(int4 p)
            {
                int key = ((p.x * side + p.y) * side + p.z) * side + p.w;
                if (lookup.TryGetValue(key, out int index)) return index;

                float4 position = (float4)p / n - 0.5f;
                if (sphere)
                {
                    // Equal steps in angle rather than in distance keep the tetrahedra at the middle of a cell
                    // from being much larger than those at its corners.
                    position = math.normalize(math.tan(position * (math.PI / 2f)));
                }
                index = vertices.Count;
                vertices.Add(position);
                lookup.Add(key, index);
                return index;
            }

            var free = new int[3];
            for (int axis = 0; axis < 4; axis++)
            {
                for (int f = 0, k = 0; k < 4; k++)
                {
                    if (k != axis) free[f++] = k;
                }

                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                for (int i0 = 0; i0 < n; i0++)
                for (int i1 = 0; i1 < n; i1++)
                for (int i2 = 0; i2 < n; i2++)
                for (int order = 0; order < 6; order++)
                {
                    int4 p = default;
                    p[axis] = sideIndex * n;
                    p[free[0]] = i0;
                    p[free[1]] = i1;
                    p[free[2]] = i2;

                    cells[o / 4] = axis * 2 + sideIndex;
                    tetrahedra[o++] = Vertex(p);
                    for (int step = 0; step < 3; step++)
                    {
                        p[free[KuhnOrders[order * 3 + step]]] += 1;
                        tetrahedra[o++] = Vertex(p);
                    }
                }
            }

            float4[] array = vertices.ToArray();
            OrientOutward(array, tetrahedra, float4.zero);
            return new Mesh4D(array, tetrahedra, cells, sphere ? null : EdgesOfLength(array, 1f / n), 8);
        }

        // Reorders every tetrahedron so that its normal points away from an interior point. Valid for the
        // boundary of a body that is star-shaped about that point (every convex polytope).
        private static void OrientOutward(float4[] vertices, int[] tetrahedra, float4 interior)
        {
            for (int t = 0; t < tetrahedra.Length / 4; t++)
            {
                float4 v0 = vertices[tetrahedra[t * 4]], v1 = vertices[tetrahedra[t * 4 + 1]];
                float4 v2 = vertices[tetrahedra[t * 4 + 2]], v3 = vertices[tetrahedra[t * 4 + 3]];
                float4 centroid = 0.25f * (v0 + v1 + v2 + v3);
                if (math.dot(Mesh4D.TetNormal(v1 - v0, v2 - v0, v3 - v0), centroid - interior) < 0f)
                    Flip(tetrahedra, t);
            }
        }

        private static void Flip(int[] tetrahedra, int tet)
        {
            int swap = tetrahedra[tet * 4 + 2];
            tetrahedra[tet * 4 + 2] = tetrahedra[tet * 4 + 3];
            tetrahedra[tet * 4 + 3] = swap;
        }

        // Swaps into slot `to` the corner that lies opposite corner `from` through the cell's centre.
        private static void MoveOppositeTo(float4[] vertices, int[] corner, float4 centre, int from, int to)
        {
            int best = to;
            float lowest = float.MaxValue;
            for (int i = from + 1; i < corner.Length; i++)
            {
                float d = math.dot(vertices[corner[from]] - centre, vertices[corner[i]] - centre);
                if (d < lowest) { lowest = d; best = i; }
            }
            int swap = corner[to];
            corner[to] = corner[best];
            corner[best] = swap;
        }

        // For a regular polytope the edges are exactly the vertex pairs at the shortest distance.
        private static int[] EdgesOfLength(float4[] vertices, float length)
        {
            var edges = new List<int>();
            for (int i = 0; i < vertices.Length; i++)
            {
                for (int j = i + 1; j < vertices.Length; j++)
                {
                    if (math.abs(math.distance(vertices[i], vertices[j]) - length) < 1e-3f * length)
                    {
                        edges.Add(i);
                        edges.Add(j);
                    }
                }
            }
            return edges.ToArray();
        }
    }
}
