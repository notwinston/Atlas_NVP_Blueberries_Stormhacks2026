using System;
using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// A growable vertex and index list in the AtlasVertex layout, filled by engine-free mesh generators and
    /// handed to the renderer in one piece. Triangles are wound clockwise seen from the front (Unity's rule).
    /// Adding within the reserved capacity allocates nothing.
    /// </summary>
    public sealed class MeshData
    {
        /// <summary>No edge longer than this on anything that can stand in a curved space (Master Plan section 8.4). Metres.</summary>
        public const float DefaultMaxEdge = 0.25f;

        /// <summary>Valid up to VertexCount.</summary>
        public AtlasVertex[] Vertices;
        public int VertexCount;
        /// <summary>Valid up to IndexCount.</summary>
        public int[] Indices;
        public int IndexCount;
        /// <summary>Kept up to date by the Add methods. Both zero while the mesh is empty.</summary>
        public float3 BoundsMin, BoundsMax;

        public MeshData(int vertexCapacity = 1024, int indexCapacity = 4096)
        {
            Vertices = new AtlasVertex[math.max(vertexCapacity, 4)];
            Indices = new int[math.max(indexCapacity, 6)];
        }

        public int TriangleCount { get { return IndexCount / 3; } }

        public void Clear()
        {
            VertexCount = 0;
            IndexCount = 0;
            BoundsMin = float3.zero;
            BoundsMax = float3.zero;
        }

        /// <summary>Makes room for this many more vertices and indices, so that the adds that follow do not allocate.</summary>
        public void Reserve(int moreVertices, int moreIndices)
        {
            if (VertexCount + moreVertices > Vertices.Length)
                Array.Resize(ref Vertices, math.max(Vertices.Length * 2, VertexCount + moreVertices));
            if (IndexCount + moreIndices > Indices.Length)
                Array.Resize(ref Indices, math.max(Indices.Length * 2, IndexCount + moreIndices));
        }

        /// <summary>Appends a vertex and returns its index.</summary>
        public int AddVertex(in AtlasVertex vertex)
        {
            if (VertexCount == Vertices.Length) Reserve(1, 0);
            if (VertexCount == 0)
            {
                BoundsMin = vertex.Position;
                BoundsMax = vertex.Position;
            }
            else
            {
                BoundsMin = math.min(BoundsMin, vertex.Position);
                BoundsMax = math.max(BoundsMax, vertex.Position);
            }
            Vertices[VertexCount] = vertex;
            return VertexCount++;
        }

        /// <summary>Appends one triangle; a, b, c clockwise seen from the front.</summary>
        public void AddTriangle(int a, int b, int c)
        {
            if (IndexCount + 3 > Indices.Length) Reserve(0, 3);
            Indices[IndexCount++] = a;
            Indices[IndexCount++] = b;
            Indices[IndexCount++] = c;
        }

        /// <summary>
        /// Adds the parallelogram origin, origin + u, origin + u + v, origin + v as a grid whose cells are at most
        /// maxEdge long on both sides. Front face towards cross(v, u) (Unity's clockwise winding).
        /// uv0 is one palette coordinate for the whole quad. Line coordinates are line0 + s * lineU + t * lineV
        /// for s, t in 0..1 along u and v. shade is baked from AtlasShading.FaceShade of the face normal.
        /// </summary>
        public void AddQuadGrid(float3 origin, float3 u, float3 v, float2 uv0,
                                float2 line0, float2 lineU, float2 lineV,
                                float ambientOcclusion = 1f, byte lineWeight = 255, float maxEdge = DefaultMaxEdge)
        {
            float3 cross = math.cross(v, u);
            float area = math.length(cross);
            if (!(area > 1e-12f)) return;                       // a degenerate quad draws nothing
            float3 normal = cross / area;
            int nu = Segments(math.length(u), maxEdge);
            int nv = Segments(math.length(v), maxEdge);
            Reserve((nu + 1) * (nv + 1), nu * nv * 6);

            uint color = AtlasVertex.PackColor(AtlasShading.ToByte(AtlasShading.FaceShade(normal), ambientOcclusion), lineWeight);
            int first = VertexCount;
            AtlasVertex vertex;
            vertex.Normal = normal;
            vertex.Color = color;
            vertex.Uv0 = uv0;
            for (int j = 0; j <= nv; j++)
            {
                float t = (float)j / nv;
                for (int i = 0; i <= nu; i++)
                {
                    float s = (float)i / nu;
                    vertex.Position = origin + u * s + v * t;
                    vertex.Uv1 = line0 + lineU * s + lineV * t;
                    AddVertex(in vertex);
                }
            }
            int stride = nu + 1;
            for (int j = 0; j < nv; j++)
            {
                for (int i = 0; i < nu; i++)
                {
                    int p00 = first + j * stride + i;
                    int p10 = p00 + 1;
                    int p01 = p00 + stride;
                    int p11 = p01 + 1;
                    AddTriangle(p00, p01, p11);
                    AddTriangle(p00, p11, p10);
                }
            }
        }

        /// <summary>A box standing on its base centre, turned by yaw about +Y (Unity's sense), five or six faces.
        /// size is (w along local X, h up, d along local Z). Line coordinates are zero.</summary>
        public void AddBox(float3 baseCentre, float3 size, float yaw, float2 uv0,
                           bool withBottom = false, byte lineWeight = 0, float maxEdge = DefaultMaxEdge)
        {
            float c = math.cos(yaw), s = math.sin(yaw);
            // Unity's yaw carries local +X to (cos, 0, -sin) and local +Z to (sin, 0, cos).
            float3 x = new float3(c, 0f, -s) * size.x;
            float3 z = new float3(s, 0f, c) * size.z;
            float3 y = new float3(0f, size.y, 0f);
            float3 corner = baseCentre - 0.5f * x - 0.5f * z;   // low corner of the base
            float2 none = float2.zero;

            AddQuadGrid(corner + y, x, z, uv0, none, none, none, 1f, lineWeight, maxEdge);          // top, +Y
            AddQuadGrid(corner + x, z, y, uv0, none, none, none, 1f, lineWeight, maxEdge);          // +X
            AddQuadGrid(corner, y, z, uv0, none, none, none, 1f, lineWeight, maxEdge);              // -X
            AddQuadGrid(corner + z, y, x, uv0, none, none, none, 1f, lineWeight, maxEdge);          // +Z
            AddQuadGrid(corner, x, y, uv0, none, none, none, 1f, lineWeight, maxEdge);              // -Z
            if (withBottom) AddQuadGrid(corner, z, x, uv0, none, none, none, 1f, lineWeight, maxEdge); // -Y
        }

        /// <summary>Length of the longest triangle edge. Metres.</summary>
        public float LongestEdge()
        {
            float longest = 0f;
            for (int k = 0; k + 2 < IndexCount; k += 3)
            {
                float3 a = Vertices[Indices[k]].Position;
                float3 b = Vertices[Indices[k + 1]].Position;
                float3 c = Vertices[Indices[k + 2]].Position;
                longest = math.max(longest, math.max(math.distancesq(a, b), math.max(math.distancesq(b, c), math.distancesq(c, a))));
            }
            return math.sqrt(longest);
        }

        // Cells along a side so that none is longer than maxEdge. The small slack keeps a 1.0 m side at four
        // 0.25 m cells instead of five when the length carries rounding noise.
        static int Segments(float length, float maxEdge)
        {
            if (!(maxEdge > 0f)) return 1;
            return math.max(1, (int)math.ceil(length / maxEdge - 1e-4f));
        }
    }
}
