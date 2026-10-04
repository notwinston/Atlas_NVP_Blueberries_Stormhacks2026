using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>
    /// Tetrahedra in R^4 (Master Plan section 5.5, decision D17): the boundary of a 4D polytope, or the
    /// worldtube of a moving surface in R^3 x S^1. One slicer serves both. Immutable once built.
    /// </summary>
    /// <remarks>
    /// A space module copies the arrays into native containers for <see cref="SliceJob"/>; tests pin them and
    /// call <see cref="Slicer4D"/> directly. The generators store every tetrahedron oriented, so that
    /// <see cref="TetNormal(int)"/> points out of the body: that is what lets a slice be wound outward.
    /// </remarks>
    public sealed class Mesh4D
    {
        /// <summary>Vertex positions (x, y, z, w), model units.</summary>
        public readonly float4[] Vertices;

        /// <summary>Four vertex indices per tetrahedron.</summary>
        public readonly int[] Tetrahedra;

        /// <summary>One cell id (0 .. CellCount - 1) per tetrahedron. Colours the slice.</summary>
        public readonly int[] TetCell;

        /// <summary>Two vertex indices per edge of the body, for the shadow. May be empty.</summary>
        public readonly int[] Edges;

        public readonly int CellCount;

        /// <summary>Greater than 0 when w wraps (R^3 x S^1); 0 otherwise.</summary>
        public readonly float WPeriod;

        /// <summary>Optional scalar per vertex, interpolated onto the slice (the breathing phase of a tube). May be null.</summary>
        public readonly float[] VertexData;

        public int VertexCount => Vertices.Length;
        public int TetCount => TetCell.Length;
        public int EdgeCount => Edges.Length / 2;

        /// <summary>Upper bound on the triangles of one slice: a cut tetrahedron gives a triangle or a quadrilateral.</summary>
        public int MaxSliceTriangles => Slicer4D.MaxTrianglesPerTet * TetCount;

        /// <summary>Upper bound on the vertices of one slice (three per triangle, never shared).</summary>
        public int MaxSliceVertices => Slicer4D.MaxVerticesPerTet * TetCount;

        /// <summary>ArgumentException when an index is out of range, a length does not fit, or a cell id is not below cellCount.</summary>
        public Mesh4D(float4[] vertices, int[] tetrahedra, int[] tetCell, int[] edges, int cellCount,
                      float wPeriod = 0f, float[] vertexData = null)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (tetrahedra == null) throw new ArgumentNullException(nameof(tetrahedra));
            if (tetCell == null) throw new ArgumentNullException(nameof(tetCell));
            edges = edges ?? Array.Empty<int>();
            if (tetrahedra.Length != tetCell.Length * 4)
                throw new ArgumentException("Expected four indices and one cell id per tetrahedron.");
            if ((edges.Length & 1) != 0)
                throw new ArgumentException("Edges are index pairs.");
            if (vertexData != null && vertexData.Length != vertices.Length)
                throw new ArgumentException("Vertex data must have one value per vertex.");
            foreach (int index in tetrahedra)
            {
                if ((uint)index >= (uint)vertices.Length) throw new ArgumentException("Tetrahedron index out of range.");
            }
            foreach (int index in edges)
            {
                if ((uint)index >= (uint)vertices.Length) throw new ArgumentException("Edge index out of range.");
            }
            foreach (int cell in tetCell)
            {
                if ((uint)cell >= (uint)cellCount) throw new ArgumentException("Cell id not below cellCount.");
            }

            Vertices = vertices;
            Tetrahedra = tetrahedra;
            TetCell = tetCell;
            Edges = edges;
            CellCount = cellCount;
            WPeriod = wPeriod;
            VertexData = vertexData;
        }

        /// <summary>
        /// The vector orthogonal to three edge vectors of a tetrahedron (the 4D analogue of the cross product).
        /// Its length is six times the tetrahedron's volume.
        /// </summary>
        public static float4 TetNormal(float4 a, float4 b, float4 c)
        {
            return new float4(
                 math.dot(a.yzw, math.cross(b.yzw, c.yzw)),
                -math.dot(a.xzw, math.cross(b.xzw, c.xzw)),
                 math.dot(a.xyw, math.cross(b.xyw, c.xyw)),
                -math.dot(a.xyz, math.cross(b.xyz, c.xyz)));
        }

        /// <summary>Oriented normal of a tetrahedron (not normalised).</summary>
        public float4 TetNormal(int tet)
        {
            int o = tet * 4;
            float4 v0 = Vertices[Tetrahedra[o]];
            return TetNormal(Vertices[Tetrahedra[o + 1]] - v0, Vertices[Tetrahedra[o + 2]] - v0,
                             Vertices[Tetrahedra[o + 3]] - v0);
        }

        public float4 TetCentroid(int tet)
        {
            int o = tet * 4;
            return 0.25f * (Vertices[Tetrahedra[o]] + Vertices[Tetrahedra[o + 1]]
                          + Vertices[Tetrahedra[o + 2]] + Vertices[Tetrahedra[o + 3]]);
        }

        /// <summary>
        /// Triangles (index triples) of the faces that separate two cells or lie on an open end: the 2-faces of
        /// a polytope, the phase surfaces of a tube. For the solid shadow. Allocates; call at load time.
        /// </summary>
        public int[] BuildCellBoundaryTriangles()
        {
            var faces = new Dictionary<(int, int, int), (int cell, int uses, bool mixed)>(TetCount * 2);
            for (int t = 0; t < TetCount; t++)
            {
                int o = t * 4;
                for (int skip = 0; skip < 4; skip++)
                {
                    var key = SortedFace(Tetrahedra[o + (skip + 1) % 4], Tetrahedra[o + (skip + 2) % 4],
                                         Tetrahedra[o + (skip + 3) % 4]);
                    if (faces.TryGetValue(key, out var seen))
                        faces[key] = (seen.cell, seen.uses + 1, seen.mixed || seen.cell != TetCell[t]);
                    else
                        faces[key] = (TetCell[t], 1, false);
                }
            }

            var triangles = new List<int>();
            foreach (var pair in faces)
            {
                if (pair.Value.mixed || pair.Value.uses == 1)
                {
                    triangles.Add(pair.Key.Item1);
                    triangles.Add(pair.Key.Item2);
                    triangles.Add(pair.Key.Item3);
                }
            }
            return triangles.ToArray();
        }

        internal static (int, int, int) SortedFace(int a, int b, int c)
        {
            if (a > b) { int s = a; a = b; b = s; }
            if (b > c) { int s = b; b = c; c = s; }
            if (a > b) { int s = a; a = b; b = s; }
            return (a, b, c);
        }
    }
}
