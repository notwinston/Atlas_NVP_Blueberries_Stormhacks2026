using Atlas.Rendering;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// Cuts the body and packs the cross-section into look vertices. A thin Burst wrapper round
    /// <see cref="SlicerMeshKernel.SliceToVertices"/> (README F2); the marker measures the slicer's cost
    /// wherever the job runs (Master Plan section 11.2, "Atlas.Slicer4D").
    /// </summary>
    [BurstCompile]
    unsafe struct SlicerSliceJob : IJob
    {
        [ReadOnly] public NativeArray<float4> Vertices;
        [ReadOnly] public NativeArray<int> Tetrahedra;
        [ReadOnly] public NativeArray<int> TetCell;
        [ReadOnly] public NativeArray<float2> CellUv;
        public float4x4 ModelToSlice;
        public float4 Translation;
        public float4 Normal;
        public float Offset;
        public float3 Centre;
        public float Scale;
        public float3 Light;
        public NativeArray<float3> ScratchPositions;
        public NativeArray<float3> ScratchNormals;
        public NativeArray<int> ScratchCells;
        public NativeArray<AtlasVertex> OutVertices;
        /// <summary>Length 2: minimum and maximum of the written positions.</summary>
        public NativeArray<float3> OutBounds;
        /// <summary>Length 3: vertices, triangles, tetrahedra cut.</summary>
        public NativeArray<int> OutCounts;
        public ProfilerMarker Marker;

        public void Execute()
        {
            Marker.Begin();
            int capacity = math.min(math.min(ScratchPositions.Length, ScratchNormals.Length),
                                    math.min(ScratchCells.Length, OutVertices.Length));
            SlicerMeshKernel.SliceToVertices(
                (float4*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Vertices),
                (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Tetrahedra), TetCell.Length,
                (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(TetCell),
                in ModelToSlice, in Translation, in Normal, Offset,
                (float2*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(CellUv), CellUv.Length,
                Centre, Scale, Light,
                (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(ScratchPositions),
                (float3*)NativeArrayUnsafeUtility.GetUnsafePtr(ScratchNormals),
                (int*)NativeArrayUnsafeUtility.GetUnsafePtr(ScratchCells),
                (AtlasVertex*)NativeArrayUnsafeUtility.GetUnsafePtr(OutVertices), capacity,
                out int vertexCount, out int triangleCount, out int tetsCut,
                out float3 boundsMin, out float3 boundsMax);
            OutCounts[0] = vertexCount;
            OutCounts[1] = triangleCount;
            OutCounts[2] = tetsCut;
            OutBounds[0] = boundsMin;
            OutBounds[1] = boundsMax;
            Marker.End();
        }
    }

    /// <summary>Projects the body's boundary triangles into the shadow mesh. A thin Burst wrapper round
    /// <see cref="SlicerMeshKernel.ShadowToVertices"/>.</summary>
    [BurstCompile]
    unsafe struct SlicerShadowJob : IJob
    {
        [ReadOnly] public NativeArray<float4> Vertices;
        [ReadOnly] public NativeArray<int> Triangles;
        public int TriangleCount;
        public float4x4 ModelToSlice;
        public float4 Translation;
        public bool Perspective;
        public float Focal;
        public float3 Centre;
        public float Scale;
        public float3 Light;
        public NativeArray<AtlasVertex> OutVertices;
        /// <summary>Length 2: minimum and maximum of the written positions.</summary>
        public NativeArray<float3> OutBounds;

        public void Execute()
        {
            int count = math.min(TriangleCount, math.min(Triangles.Length, OutVertices.Length) / 3);
            SlicerMeshKernel.ShadowToVertices(
                (float4*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Vertices),
                (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(Triangles), count,
                in ModelToSlice, in Translation, Perspective, Focal, Centre, Scale, Light,
                (AtlasVertex*)NativeArrayUnsafeUtility.GetUnsafePtr(OutVertices),
                out float3 boundsMin, out float3 boundsMax);
            OutBounds[0] = boundsMin;
            OutBounds[1] = boundsMax;
        }
    }
}
