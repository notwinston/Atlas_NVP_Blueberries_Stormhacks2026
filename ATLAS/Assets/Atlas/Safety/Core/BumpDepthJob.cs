using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace Atlas.Safety
{
    /// <summary>
    /// Runs <see cref="BumpDepthKernel.Process"/> under Burst (Master Plan section 4.10, "processed in a Burst
    /// job"). The job holds the containers and hands raw pointers to the kernel; it has no logic of its own
    /// (contracts F2). The static field and the dynamic layer are memory pinned by <see cref="BumpGuardCore"/>,
    /// so the job must be completed before the core is stepped, given a new room or disposed.
    /// </summary>
    [BurstCompile]
    public unsafe struct BumpDepthJob : IJob
    {
        /// <summary>Input.Width x Input.Height depths in metres, row 0 at the top of the image.</summary>
        [ReadOnly] public NativeArray<float> Depth;
        public DepthKernelInput Input;
        public BumpGridSpec Grid;
        [NativeDisableUnsafePtrRestriction] public float* StaticField;
        [NativeDisableUnsafePtrRestriction] public float* DynamicTopHeight;
        [NativeDisableUnsafePtrRestriction] public double* DynamicLastHit;
        [NativeDisableUnsafePtrRestriction] public int* DynamicEmptyState;
        [NativeDisableUnsafePtrRestriction] public int* DynamicBlockCount;
        [NativeDisableUnsafePtrRestriction] public int* DynamicActiveCount;
        public int DynamicBlocksX;
        public int DynamicBlocksZ;
        /// <summary>One element: the result, to pass to <see cref="BumpGuardCore.SubmitDepthResult"/>.</summary>
        public NativeArray<DepthFrameResult> Result;

        /// <summary>Builds the job for one depth frame against the core's room.</summary>
        public static BumpDepthJob Create(BumpGuardCore core, NativeArray<float> depth, in DepthKernelInput input,
            NativeArray<DepthFrameResult> result)
        {
            DynamicGrid dynamic = core.Dynamic;
            return new BumpDepthJob
            {
                Depth = depth,
                Input = input,
                Grid = core.Grid,
                StaticField = core.StaticField,
                DynamicTopHeight = dynamic.TopHeight,
                DynamicLastHit = dynamic.LastHit,
                DynamicEmptyState = dynamic.EmptyState,
                DynamicBlockCount = dynamic.BlockCount,
                DynamicActiveCount = dynamic.ActiveCount,
                DynamicBlocksX = dynamic.BlocksX,
                DynamicBlocksZ = dynamic.BlocksZ,
                Result = result
            };
        }

        public void Execute()
        {
            DynamicGrid dynamic = new DynamicGrid
            {
                TopHeight = DynamicTopHeight,
                LastHit = DynamicLastHit,
                EmptyState = DynamicEmptyState,
                BlockCount = DynamicBlockCount,
                ActiveCount = DynamicActiveCount,
                Spec = Grid,
                BlocksX = DynamicBlocksX,
                BlocksZ = DynamicBlocksZ
            };
            BumpDepthKernel.Process((float*)Depth.GetUnsafeReadOnlyPtr(), Input, StaticField, Grid, dynamic,
                out DepthFrameResult result);
            Result[0] = result;
        }
    }
}
