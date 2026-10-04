using System;
using Atlas.Kernel.FourD;
using Atlas.Rendering;
using Unity.Collections;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The native memory of one body: its tetrahedra, its boundary triangles, and the fixed-size output of the
    /// slice and of the shadow. Everything is allocated once when the body is chosen and disposed when it is
    /// replaced, so a frame allocates nothing (Master Plan section 0.5 rule 8).
    /// </summary>
    sealed class SlicerBuffers : IDisposable
    {
        public readonly SlicerObject Body;
        public readonly int TetCount;
        /// <summary>6 vertices per tetrahedron: a cut tetrahedron gives at most two triangles.</summary>
        public readonly int SliceCapacity;
        public readonly int ShadowTriangleCount;
        public readonly int ShadowCapacity;

        public NativeArray<float4> Vertices;
        public NativeArray<int> Tetrahedra;
        public NativeArray<int> TetCell;
        public NativeArray<int> ShadowTriangles;
        public NativeArray<float2> CellUv;

        public NativeArray<float3> ScratchPositions;
        public NativeArray<float3> ScratchNormals;
        public NativeArray<int> ScratchCells;
        public NativeArray<AtlasVertex> SliceVertices;
        public NativeArray<AtlasVertex> ShadowVertices;
        /// <summary>0, 1, 2, ...: both meshes are triangle soups, so one index run serves them both.</summary>
        public NativeArray<int> Indices;
        public NativeArray<float3> SliceBounds;
        public NativeArray<float3> ShadowBounds;
        public NativeArray<int> SliceCounts;

        public SlicerBuffers(SlicerObject body)
        {
            Body = SlicerObjects.Clamp(body);
            Mesh4D mesh = SlicerObjects.Build(Body);
            int[] shadow = mesh.BuildCellBoundaryTriangles();
            var uv = new float2[SlicerObjects.CellUvCount];
            SlicerObjects.FillCellUv(Body, uv);

            TetCount = mesh.TetCount;
            SliceCapacity = mesh.MaxSliceVertices;
            ShadowTriangleCount = shadow.Length / 3;
            ShadowCapacity = math.max(shadow.Length, 3);

            const Allocator persistent = Allocator.Persistent;
            Vertices = new NativeArray<float4>(mesh.Vertices, persistent);
            Tetrahedra = new NativeArray<int>(mesh.Tetrahedra, persistent);
            TetCell = new NativeArray<int>(mesh.TetCell, persistent);
            CellUv = new NativeArray<float2>(uv, persistent);
            ShadowTriangles = new NativeArray<int>(ShadowCapacity, persistent);
            NativeArray<int>.Copy(shadow, ShadowTriangles, shadow.Length);

            ScratchPositions = new NativeArray<float3>(SliceCapacity, persistent, NativeArrayOptions.UninitializedMemory);
            ScratchNormals = new NativeArray<float3>(SliceCapacity, persistent, NativeArrayOptions.UninitializedMemory);
            ScratchCells = new NativeArray<int>(SliceCapacity, persistent, NativeArrayOptions.UninitializedMemory);
            SliceVertices = new NativeArray<AtlasVertex>(SliceCapacity, persistent);
            ShadowVertices = new NativeArray<AtlasVertex>(ShadowCapacity, persistent);
            SliceBounds = new NativeArray<float3>(2, persistent);
            ShadowBounds = new NativeArray<float3>(2, persistent);
            SliceCounts = new NativeArray<int>(3, persistent);

            int indexCount = math.max(SliceCapacity, ShadowCapacity);
            Indices = new NativeArray<int>(indexCount, persistent, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < indexCount; i++) Indices[i] = i;
        }

        public void Dispose()
        {
            Free(ref Vertices);
            Free(ref Tetrahedra);
            Free(ref TetCell);
            Free(ref ShadowTriangles);
            Free(ref CellUv);
            Free(ref ScratchPositions);
            Free(ref ScratchNormals);
            Free(ref ScratchCells);
            Free(ref SliceVertices);
            Free(ref ShadowVertices);
            Free(ref Indices);
            Free(ref SliceBounds);
            Free(ref ShadowBounds);
            Free(ref SliceCounts);
        }

        static void Free<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated) array.Dispose();
            array = default;
        }
    }
}
