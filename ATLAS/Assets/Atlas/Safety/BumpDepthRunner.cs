using System;
using Unity.Collections;
using Unity.Jobs;

namespace Atlas.Safety
{
    /// <summary>
    /// Runs the depth kernel through its Burst job for <see cref="BumpGuard"/> (Master Plan section 4.10: "LiDAR
    /// depth image, processed in a Burst job"). The job is run and finished inside the call, on the calling
    /// thread: Bump Guard is the first thing in the frame and must have the result before it steps, so there is
    /// nothing to gain from a worker thread, and nothing ever waits. The image is copied into a buffer the job
    /// may read (0.2 MB, a few microseconds). Buffers are made on the first image and kept.
    /// </summary>
    public sealed class BumpDepthRunner : IDisposable
    {
        NativeArray<float> _depth;
        NativeArray<DepthFrameResult> _result;

        /// <summary>Processes one depth image and hands the result to the core.</summary>
        /// <param name="depth">At least input.Width x input.Height metres, row 0 at the top.</param>
        public void Run(BumpGuardCore core, float[] depth, in DepthKernelInput input)
        {
            int count = input.Width * input.Height;
            if (!_depth.IsCreated || _depth.Length != count)
            {
                if (_depth.IsCreated) _depth.Dispose();
                _depth = new NativeArray<float>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
            if (!_result.IsCreated)
                _result = new NativeArray<DepthFrameResult>(1, Allocator.Persistent);

            NativeArray<float>.Copy(depth, _depth, count);
            BumpDepthJob.Create(core, _depth, input, _result).Run();
            core.SubmitDepthResult(_result[0]);
        }

        public void Dispose()
        {
            if (_depth.IsCreated) _depth.Dispose();
            if (_result.IsCreated) _result.Dispose();
        }
    }
}
