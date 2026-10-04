using Atlas.Kernel;
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// Everything a system may know about the current frame (Master Plan section 6.3). One value per frame,
    /// owned by the frame loop and passed with in or ref. Nobody keeps a copy across frames.
    /// </summary>
    public struct FrameContext
    {
        /// <summary>0 on the loop's first frame, then +1 per frame. Never reset.</summary>
        public int FrameIndex;
        /// <summary>Seconds on the app clock; the pose log's clock in replay. Monotonic.</summary>
        public double Time;
        /// <summary>Seconds since the previous frame, clamped to 0.004 to 0.1.</summary>
        public float DeltaTime;

        /// <summary>The tracked head (the midpoint between the eyes), metres, world. What Bump Guard and the cameras use.</summary>
        public float3 RawHeadPosition;
        public quaternion RawHeadRotation;
        public TrackingState Tracking;

        /// <summary>The geometry head pose: what a space reads (step 3).</summary>
        public float3 HeadPosition;
        public quaternion HeadRotation;
        /// <summary>HeadPosition.xz minus the previous frame's, metres (x, z). What step 4 hands to the kernel.</summary>
        public float2 FloorDelta;
        /// <summary>The pose filter dropped a jump this frame. State.flags bit 2.</summary>
        public bool PoseJumpRejected;

        /// <summary>IWalkerKernel.Pose after this frame's Step.</summary>
        public WalkerPose Walker;
        /// <summary>What the kernel step did this frame.</summary>
        public KernelStep Kernel;

        /// <summary>Raw head pose with the eye offset; both equal the head position when there is no rig.</summary>
        public float3 LeftEyePosition;
        public float3 RightEyePosition;

        /// <summary>The quality tier in force this frame.</summary>
        public QualityTier Tier;
    }
}
