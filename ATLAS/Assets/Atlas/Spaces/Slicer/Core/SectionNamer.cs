using System;
using System.Collections.Generic;
using Atlas.Kernel.FourD;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>What a cross-section of a 4D polytope is, by the faces it has (Master Plan section 10.1.3, task P2-A02).</summary>
    public enum SectionShape : byte
    {
        /// <summary>The cut misses the body.</summary>
        None = 0,
        /// <summary>The cut touches the body at one vertex.</summary>
        Point,
        /// <summary>The cut touches the body along an edge or a face: no volume.</summary>
        Flat,
        Tetrahedron,
        TriangularPrism,
        SquarePyramid,
        /// <summary>Six squares of one size.</summary>
        Cube,
        /// <summary>Six quadrilaterals that are not all one square: a cuboid or a parallelepiped.</summary>
        Box,
        Octahedron,
        PentagonalPrism,
        HexagonalPrism,
        TruncatedTetrahedron,
        Cuboctahedron,
        TruncatedOctahedron,
        RhombicDodecahedron,
        /// <summary>A section of a smooth body (the 4D ball, one moment of the breathing tube).</summary>
        Sphere,
        /// <summary>Any other polyhedron: shown with its face count.</summary>
        Polyhedron,
    }

    /// <summary>A named cross-section: the shape, and the counts behind the name.</summary>
    public struct SectionInfo : IEquatable<SectionInfo>
    {
        public SectionShape Shape;
        /// <summary>Faces, vertices and edges of the section; V - E + F = 2 for every section with a volume.</summary>
        public int Faces, Vertices, Edges;
        /// <summary>Faces with 3, 4, 5 and 6 sides, and with more.</summary>
        public int Triangles, Quads, Pentagons, Hexagons, Others;

        public string Name { get { return SectionNamer.Name(Shape); } }

        public bool Equals(SectionInfo o)
        {
            return Shape == o.Shape && Faces == o.Faces && Vertices == o.Vertices && Edges == o.Edges && Triangles == o.Triangles &&
                   Quads == o.Quads && Pentagons == o.Pentagons && Hexagons == o.Hexagons && Others == o.Others;
        }

        public override bool Equals(object obj) { return obj is SectionInfo o && Equals(o); }
        public override int GetHashCode() { return ((int)Shape * 397) ^ (Faces * 31) ^ Vertices; }
    }

    /// <summary>
    /// Names the cross-section of a convex 4D polytope (Master Plan section 10.1.3: "section named octahedron"; task P2-A02)
    /// from the polytope's own structure, not from the triangles on screen. A hyperplane cuts a convex polytope in a convex
    /// polyhedron whose vertices are the polytope's edges it crosses and whose faces are the cells it crosses; a face has as many
    /// sides as its cell has edges crossed. Counting those is exact, so V - E + F = 2 holds by construction. Section vertices
    /// closer than <see cref="WeldDistance"/> are one vertex (a cut that grazes a polytope vertex is named by the shape the
    /// walker sees), merged with a union-find so that welding is transitive. Engine-free. The tables are built once per body;
    /// <see cref="Analyse"/> allocates nothing.
    /// </summary>
    public sealed class SectionNamer
    {
        /// <summary>Section vertices closer than this are one vertex: model units, on bodies of circumradius 1. One per cent of
        /// the body, a few millimetres as drawn.</summary>
        public const float WeldDistance = 1e-2f;
        /// <summary>Edges within this fraction of each other are equal (a cube rather than a box).</summary>
        public const float RegularTolerance = 0.02f;

        readonly float4[] m_Vertices;
        readonly int[] m_Edges;                 // two polytope vertex indices per edge
        readonly int[] m_CellEdgeStart;         // cell c owns m_CellEdges[m_CellEdgeStart[c] .. m_CellEdgeStart[c + 1])
        readonly int[] m_CellEdges;
        // Per frame, sized once.
        readonly float[] m_Side;                // signed distance of each polytope vertex from the cut
        readonly int[] m_Hit;                   // per edge: section vertex index, or -1 when the edge is not crossed
        readonly float3[] m_Point;              // per section vertex: position in the slice frame
        readonly int[] m_Parent;                // union-find over section vertices
        readonly int[] m_Stamp;                 // per root: the last face that counted it (a distinct count without clearing)
        readonly int[] m_Used;                  // per root: 1 when a face uses it
        readonly float[] m_Distances;

        /// <summary>The cells of the body.</summary>
        public int CellCount { get; }

        /// <summary>Builds the cell-to-edge tables of a polytope. Allocates. A mesh with no edges (the 4D ball) cannot be named.</summary>
        public SectionNamer(Mesh4D mesh)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (mesh.Edges == null || mesh.Edges.Length == 0) throw new ArgumentException("the body has no edges to name a section by", nameof(mesh));
            m_Vertices = mesh.Vertices;
            m_Edges = mesh.Edges;
            CellCount = mesh.CellCount;

            // The vertices of each cell are those of its tetrahedra; its edges are the polytope edges with both ends among them
            // (in a convex polytope such an edge lies in the cell).
            var cellVertices = new HashSet<int>[CellCount];
            for (int c = 0; c < CellCount; c++) cellVertices[c] = new HashSet<int>();
            for (int t = 0; t < mesh.TetCount; t++)
                for (int k = 0; k < 4; k++) cellVertices[mesh.TetCell[t]].Add(mesh.Tetrahedra[t * 4 + k]);
            var lists = new List<int>();
            m_CellEdgeStart = new int[CellCount + 1];
            for (int c = 0; c < CellCount; c++)
            {
                m_CellEdgeStart[c] = lists.Count;
                for (int e = 0; e < mesh.EdgeCount; e++)
                    if (cellVertices[c].Contains(m_Edges[2 * e]) && cellVertices[c].Contains(m_Edges[2 * e + 1])) lists.Add(e);
            }
            m_CellEdgeStart[CellCount] = lists.Count;
            m_CellEdges = lists.ToArray();

            int edges = mesh.EdgeCount;
            m_Side = new float[mesh.VertexCount];
            m_Hit = new int[edges];
            m_Point = new float3[edges];
            m_Parent = new int[edges];
            m_Stamp = new int[edges];
            m_Used = new int[edges];
            m_Distances = new float[edges * (edges - 1) / 2 + 1];
        }

        /// <summary>
        /// The section of the body under the rotation <paramref name="modelToSlice"/> and translation, cut by
        /// dot(normal, p) = offset. Positions are compared in the slice frame (p.xyz), as the slicer draws them.
        /// </summary>
        public SectionInfo Analyse(in float4x4 modelToSlice, in float4 translation, in float4 normal, float offset)
        {
            var info = new SectionInfo();
            float4x4 m = modelToSlice;
            for (int i = 0; i < m_Vertices.Length; i++)
                m_Side[i] = math.dot(normal, math.mul(m, m_Vertices[i]) + translation) - offset;

            // Section vertices: one per crossed edge. A vertex exactly on the cut counts as above it (a symbolic nudge), so every
            // crossing is well defined; welding then merges the coincident points such a vertex makes.
            int count = 0;
            for (int e = 0; e < m_Hit.Length; e++)
            {
                int a = m_Edges[2 * e], b = m_Edges[2 * e + 1];
                bool aAbove = m_Side[a] >= 0f, bAbove = m_Side[b] >= 0f;
                if (aAbove == bAbove) { m_Hit[e] = -1; continue; }
                float s = m_Side[a] / (m_Side[a] - m_Side[b]);
                float4 p = math.lerp(math.mul(m, m_Vertices[a]) + translation, math.mul(m, m_Vertices[b]) + translation, s);
                m_Point[count] = p.xyz;
                m_Parent[count] = count;
                m_Stamp[count] = -1;
                m_Used[count] = 0;
                m_Hit[e] = count++;
            }
            if (count == 0) { info.Shape = SectionShape.None; return info; }

            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    if (math.distancesq(m_Point[i], m_Point[j]) < WeldDistance * WeldDistance) Union(i, j);

            // Faces: the cells crossed, with their distinct welded corners as sides.
            int sidesTotal = 0;
            for (int c = 0; c < CellCount; c++)
            {
                int sides = 0;
                for (int k = m_CellEdgeStart[c]; k < m_CellEdgeStart[c + 1]; k++)
                {
                    int h = m_Hit[m_CellEdges[k]];
                    if (h < 0) continue;
                    int r = Find(h);
                    if (m_Stamp[r] == c) continue;
                    m_Stamp[r] = c;
                    sides++;
                }
                if (sides < 3) continue;
                for (int k = m_CellEdgeStart[c]; k < m_CellEdgeStart[c + 1]; k++)
                {
                    int h = m_Hit[m_CellEdges[k]];
                    if (h >= 0) m_Used[Find(h)] = 1;
                }
                info.Faces++;
                sidesTotal += sides;
                if (sides == 3) info.Triangles++;
                else if (sides == 4) info.Quads++;
                else if (sides == 5) info.Pentagons++;
                else if (sides == 6) info.Hexagons++;
                else info.Others++;
            }

            int distinct = 0, used = 0;
            for (int i = 0; i < count; i++)
            {
                if (Find(i) != i) continue;
                distinct++;
                if (m_Used[i] != 0) used++;
            }
            if (info.Faces < 4)
            {
                // No volume: the cut touches a vertex, an edge or a face of the body.
                return new SectionInfo { Vertices = distinct, Shape = distinct <= 1 ? SectionShape.Point : SectionShape.Flat };
            }
            info.Vertices = used;
            info.Edges = sidesTotal / 2;
            info.Shape = Classify(in info, info.Quads == 6 && info.Faces == 6 && CubeEdges(count));
            return info;
        }

        /// <summary>The words shown for a shape.</summary>
        public static string Name(SectionShape shape)
        {
            switch (shape)
            {
                case SectionShape.None: return "nothing";
                case SectionShape.Point: return "a point";
                case SectionShape.Flat: return "flat (no volume)";
                case SectionShape.Tetrahedron: return "tetrahedron";
                case SectionShape.TriangularPrism: return "triangular prism";
                case SectionShape.SquarePyramid: return "square pyramid";
                case SectionShape.Cube: return "cube";
                case SectionShape.Box: return "box";
                case SectionShape.Octahedron: return "octahedron";
                case SectionShape.PentagonalPrism: return "pentagonal prism";
                case SectionShape.HexagonalPrism: return "hexagonal prism";
                case SectionShape.TruncatedTetrahedron: return "truncated tetrahedron";
                case SectionShape.Cuboctahedron: return "cuboctahedron";
                case SectionShape.TruncatedOctahedron: return "truncated octahedron";
                case SectionShape.RhombicDodecahedron: return "rhombic dodecahedron";
                case SectionShape.Sphere: return "sphere";
                default: return "polyhedron";
            }
        }

        /// <summary>The shape a face histogram describes. <paramref name="equalEdges"/>: the six quadrilaterals are one square.</summary>
        public static SectionShape Classify(in SectionInfo f, bool equalEdges)
        {
            int t = f.Triangles, q = f.Quads, p = f.Pentagons, h = f.Hexagons, o = f.Others;
            if (o == 0)
            {
                if (t == 4 && q == 0 && p == 0 && h == 0) return SectionShape.Tetrahedron;
                if (t == 2 && q == 3 && p == 0 && h == 0) return SectionShape.TriangularPrism;
                if (t == 4 && q == 1 && p == 0 && h == 0) return SectionShape.SquarePyramid;
                if (t == 0 && q == 6 && p == 0 && h == 0) return equalEdges ? SectionShape.Cube : SectionShape.Box;
                if (t == 8 && q == 0 && p == 0 && h == 0) return SectionShape.Octahedron;
                if (t == 0 && q == 5 && p == 2 && h == 0) return SectionShape.PentagonalPrism;
                if (t == 0 && q == 6 && p == 0 && h == 2) return SectionShape.HexagonalPrism;
                if (t == 4 && q == 0 && p == 0 && h == 4) return SectionShape.TruncatedTetrahedron;
                if (t == 8 && q == 6 && p == 0 && h == 0) return SectionShape.Cuboctahedron;
                if (t == 0 && q == 6 && p == 0 && h == 8) return SectionShape.TruncatedOctahedron;
                if (t == 0 && q == 12 && p == 0 && h == 0 && f.Vertices == 14) return SectionShape.RhombicDodecahedron;
            }
            return SectionShape.Polyhedron;
        }

        // A hexahedron with eight corners is a cube when its twelve shortest corner-to-corner distances (its edges) are equal.
        bool CubeEdges(int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (Find(i) != i || m_Used[i] == 0) continue;
                for (int j = i + 1; j < count; j++)
                {
                    if (Find(j) != j || m_Used[j] == 0) continue;
                    m_Distances[n++] = math.distance(m_Point[i], m_Point[j]);
                }
            }
            if (n != 28) return false;                      // eight corners, 8 * 7 / 2 pairs
            Array.Sort(m_Distances, 0, n);
            return m_Distances[11] <= m_Distances[0] * (1f + RegularTolerance);
        }

        int Find(int i)
        {
            while (m_Parent[i] != i)
            {
                m_Parent[i] = m_Parent[m_Parent[i]];
                i = m_Parent[i];
            }
            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a == b) return;
            if (a < b) m_Parent[b] = a; else m_Parent[a] = b;
        }
    }
}
