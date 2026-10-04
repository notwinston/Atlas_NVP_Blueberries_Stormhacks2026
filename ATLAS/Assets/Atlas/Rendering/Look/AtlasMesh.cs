using Atlas.Kernel;
using Unity.Collections;
using UnityEngine.Rendering;
using UnityEngine;

namespace Atlas.Rendering
{
    /// <summary>
    /// Uploads an <see cref="AtlasMeshBuilder"/> to a <see cref="Mesh"/> through the vertex-buffer API: one stream
    /// in the <see cref="AtlasVertex"/> layout, one sub-mesh of triangles, bounds taken from the data.
    /// </summary>
    public static class AtlasMesh
    {
        /// <summary>Position, Normal, Color (UNorm8 x 4), TexCoord0, TexCoord1; all in stream 0.</summary>
        public static readonly VertexAttributeDescriptor[] Layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2, 0),
        };

        /// <summary>The most vertices a mesh with 16-bit indices can hold.</summary>
        public const int MaxVertices16 = 65535;

        const MeshUpdateFlags k_Flags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;

        // Narrowed copy of the indices for 16-bit meshes; grows to the largest mesh uploaded and is then reused.
        static ushort[] s_Index16 = new ushort[4096];

        /// <summary>A new Mesh with the data, bounds from the data, UInt16 or UInt32 indices as needed.</summary>
        public static Mesh Create(AtlasMeshBuilder data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            Apply(data, mesh);
            return mesh;
        }

        /// <summary>Replaces the contents of an existing mesh. Allocates nothing when the counts fit.</summary>
        public static void Apply(AtlasMeshBuilder data, Mesh mesh)
        {
            int vertexCount = data.VertexCount, indexCount = data.IndexCount;
            mesh.SetVertexBufferParams(vertexCount, Layout);
            mesh.SetVertexBufferData(data.Vertices, 0, 0, vertexCount, 0, k_Flags);

            if (vertexCount <= MaxVertices16)
            {
                if (s_Index16.Length < indexCount) s_Index16 = new ushort[Mathf.NextPowerOfTwo(indexCount)];
                int[] source = data.Indices;
                ushort[] narrow = s_Index16;
                for (int i = 0; i < indexCount; i++) narrow[i] = (ushort)source[i];
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt16);
                mesh.SetIndexBufferData(narrow, 0, 0, indexCount, k_Flags);
            }
            else
            {
                mesh.SetIndexBufferParams(indexCount, IndexFormat.UInt32);
                mesh.SetIndexBufferData(data.Indices, 0, 0, indexCount, k_Flags);
            }

            var bounds = new Bounds(data.BoundsCentre, data.BoundsSize);
            SetSubMesh(mesh, vertexCount, indexCount, bounds);
        }

        /// <summary>For meshes rewritten every frame: fixed capacity, UInt32 indices, MarkDynamic.</summary>
        public static Mesh CreateDynamic(string name, int vertexCapacity, int indexCapacity)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertexCapacity, Layout);
            mesh.SetIndexBufferParams(indexCapacity, IndexFormat.UInt32);
            SetSubMesh(mesh, 0, 0, new Bounds(Vector3.zero, Vector3.zero));
            return mesh;
        }

        /// <summary>
        /// Uploads the first counts of the arrays into a mesh from <see cref="CreateDynamic"/> and sets the bounds.
        /// The counts must not exceed the mesh's capacity. No allocation.
        /// </summary>
        public static void Upload(Mesh mesh,
                                  NativeArray<AtlasVertex> vertices, int vertexCount,
                                  NativeArray<int> indices, int indexCount,
                                  Bounds bounds)
        {
            mesh.SetVertexBufferData(vertices, 0, 0, vertexCount, 0, k_Flags);
            mesh.SetIndexBufferData(indices, 0, 0, indexCount, k_Flags);
            SetSubMesh(mesh, vertexCount, indexCount, bounds);
        }

        static void SetSubMesh(Mesh mesh, int vertexCount, int indexCount, Bounds bounds)
        {
            mesh.subMeshCount = 1;
            var subMesh = new SubMeshDescriptor(0, indexCount, MeshTopology.Triangles)
            {
                bounds = bounds,
                firstVertex = 0,
                vertexCount = vertexCount,
            };
            mesh.SetSubMesh(0, subMesh, k_Flags);
            mesh.bounds = bounds;
        }
    }
}
