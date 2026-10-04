using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace Atlas.Kernel.FourD
{
    /// <summary>Thin Burst wrapper round <see cref="Slicer4D.Slice"/> (README F2). It holds no logic of its own.</summary>
    /// <remarks>
    /// Every container must be created before scheduling. A zero-length <see cref="TetCell"/> means no cell
    /// ids, a zero-length <see cref="VertexData"/> or <see cref="OutVertexData"/> means nothing to interpolate.
    /// The output arrays hold 6 vertices per tetrahedron (<see cref="Mesh4D.MaxSliceVertices"/>).
    /// </remarks>
    [BurstCompile]
    public unsafe struct SliceJob : IJob
    {
        [ReadOnly] public NativeArray<float4> Vertices;
        [ReadOnly] public NativeArray<int> Tetrahedra;
        [ReadOnly] public NativeArray<int> TetCell;         // length 0 = no cell ids
        [ReadOnly] public NativeArray<float> VertexData;    // length 0 = none
        public float4x4 ModelToSlice;
        public float4 Translation;
        public float4 Normal;
        public float Offset;
        public NativeArray<float3> OutPositions;            // length = 6 * tetrahedra
        public NativeArray<float3> OutNormals;              // length = 6 * tetrahedra
        public NativeArray<int> OutCells;                   // length = 6 * tetrahedra
        public NativeArray<float> OutVertexData;            // length = 6 * tetrahedra, or 0
        public NativeArray<int> OutCounts;                  // length 3: vertices, triangles, tetrahedra cut

        public void Execute()
        {
            bool withData = VertexData.Length > 0 && OutVertexData.Length > 0;
            int capacity = math.min(OutPositions.Length, math.min(OutNormals.Length, OutCells.Length));
            if (withData) capacity = math.min(capacity, OutVertexData.Length);

            Slicer4D.Slice(
                (float4*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Vertices),
                (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Tetrahedra), Tetrahedra.Length / 4,
                TetCell.Length > 0 ? (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(TetCell) : null,
                withData ? (float*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(VertexData) : null,
                in ModelToSlice, in Translation, in Normal, Offset,
                (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(OutPositions),
                (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(OutNormals),
                (int*)NativeArrayUnsafeUtility.GetUnsafePtr(OutCells),
                withData ? (float*)NativeArrayUnsafeUtility.GetUnsafePtr(OutVertexData) : null,
                capacity,
                out int vertexCount, out int triangleCount, out int tetsCut);

            int* counts = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(OutCounts);
            counts[0] = vertexCount;
            counts[1] = triangleCount;
            counts[2] = tetsCut;
        }
    }
}
