using System;
using System.Collections.Generic;
using Atlas.Kernel.FourD;
using Unity.Mathematics;

namespace Atlas.Kernel.Tests
{
    /// <summary>
    /// Runs the slicing kernel on pinned managed arrays, exactly as the Burst job runs it on native ones, and
    /// offers the topology checks the slicer tests share.
    /// </summary>
    internal sealed unsafe class SliceHarness
    {
        public static readonly float4 PlusW = new float4(0f, 0f, 0f, 1f);

        public readonly Mesh4D Mesh;
        public readonly float3[] Positions;
        public readonly float3[] Normals;
        public readonly int[] Cells;
        public readonly float[] Data;
        public int VertexCount, TriangleCount, TetsCut;

        public SliceHarness(Mesh4D mesh)
        {
            Mesh = mesh;
            Positions = new float3[mesh.MaxSliceVertices];
            Normals = new float3[mesh.MaxSliceVertices];
            Cells = new int[mesh.MaxSliceVertices];
            Data = new float[mesh.MaxSliceVertices];
        }

        /// <summary>The cut w = offset of the mesh turned by <paramref name="modelToSlice"/>.</summary>
        public void Slice(double4x4 modelToSlice, float offset) => Slice((float4x4)modelToSlice, float4.zero, PlusW, offset);

        public void Slice(float4x4 modelToSlice, float4 translation, float4 normal, float offset)
        {
            fixed (float4* vertices = Mesh.Vertices)
            fixed (int* tetrahedra = Mesh.Tetrahedra)
            fixed (int* cells = Mesh.TetCell)
            fixed (float* vertexData = Mesh.VertexData)
            fixed (float3* positions = Positions)
            fixed (float3* normals = Normals)
            fixed (int* outCells = Cells)
            fixed (float* outData = Data)
            {
                Slicer4D.Slice(vertices, tetrahedra, Mesh.TetCount, cells, vertexData,
                               in modelToSlice, in translation, in normal, offset,
                               positions, normals, outCells, outData, Positions.Length,
                               out VertexCount, out TriangleCount, out TetsCut);
            }
        }

        public float Area()
        {
            fixed (float3* positions = Positions) return Slicer4D.Area(positions, TriangleCount);
        }

        public float Extent(float3 axis)
        {
            fixed (float3* positions = Positions) return Slicer4D.Extent(positions, VertexCount, axis);
        }

        /// <summary>Mean of the slice's vertices. A slice of a convex body is convex, so this lies inside it.</summary>
        public float3 Centroid()
        {
            double3 sum = double3.zero;
            for (int i = 0; i < VertexCount; i++) sum += Positions[i];
            return (float3)(sum / math.max(1, VertexCount));
        }

        /// <summary>
        /// Welds coincident vertices and counts how many triangles use each edge, ignoring triangles that
        /// collapsed to a point or a line. Returns the number of edges not used by exactly two triangles.
        /// </summary>
        public int OpenEdgeCount(out int solidTriangles)
        {
            const float Cell = 1e-4f;
            var ids = new Dictionary<(int, int, int), int>();
            var edges = new Dictionary<(int, int), int>();
            solidTriangles = 0;

            int Id(float3 p)
            {
                var key = ((int)math.round(p.x / Cell), (int)math.round(p.y / Cell), (int)math.round(p.z / Cell));
                if (!ids.TryGetValue(key, out int id))
                {
                    id = ids.Count;
                    ids.Add(key, id);
                }
                return id;
            }

            void Edge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                edges.TryGetValue(key, out int uses);
                edges[key] = uses + 1;
            }

            for (int t = 0; t < TriangleCount; t++)
            {
                int a = Id(Positions[t * 3]), b = Id(Positions[t * 3 + 1]), c = Id(Positions[t * 3 + 2]);
                if (a == b || b == c || a == c) continue;
                solidTriangles++;
                Edge(a, b);
                Edge(b, c);
                Edge(c, a);
            }

            int open = 0;
            foreach (var pair in edges)
            {
                if (pair.Value != 2) open++;
            }
            return open;
        }

        /// <summary>Number of triangles whose winding or stored normal does not point away from <paramref name="inside"/>.</summary>
        public int InwardTriangleCount(float3 inside, float minArea = 1e-7f)
        {
            int inward = 0;
            for (int t = 0; t < TriangleCount; t++)
            {
                float3 a = Positions[t * 3], b = Positions[t * 3 + 1], c = Positions[t * 3 + 2];
                float3 face = math.cross(b - a, c - a);
                if (math.length(face) < minArea) continue;
                float3 away = (a + b + c) / 3f - inside;
                if (math.dot(face, away) <= 0f || math.dot(Normals[t * 3], away) <= 0f) inward++;
            }
            return inward;
        }

        /// <summary>Counts the triangular faces of the tetrahedra by how many tetrahedra use them.</summary>
        public static Dictionary<(int, int, int), int> FaceUses(Mesh4D mesh, Func<int, int> identify = null)
        {
            var faces = new Dictionary<(int, int, int), int>();
            for (int t = 0; t < mesh.TetCount; t++)
            {
                for (int skip = 0; skip < 4; skip++)
                {
                    int a = mesh.Tetrahedra[t * 4 + (skip + 1) % 4];
                    int b = mesh.Tetrahedra[t * 4 + (skip + 2) % 4];
                    int c = mesh.Tetrahedra[t * 4 + (skip + 3) % 4];
                    if (identify != null) { a = identify(a); b = identify(b); c = identify(c); }
                    var key = Sort(a, b, c);
                    faces.TryGetValue(key, out int uses);
                    faces[key] = uses + 1;
                }
            }
            return faces;
        }

        private static (int, int, int) Sort(int a, int b, int c)
        {
            if (a > b) (a, b) = (b, a);
            if (b > c) (b, c) = (c, b);
            if (a > b) (a, b) = (b, a);
            return (a, b, c);
        }
    }
}
