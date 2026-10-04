// PLACEHOLDER (lane A, P1-X05): contract surface only; replaced by lane A's implementation
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>What the frame loop hands to the active space each frame. Master Plan §6.3 (fields: deviation PH-2).</summary>
    public readonly struct FrameContext
    {
        public readonly long Frame;             // frame counter, +1 per frame
        public readonly double Time;            // s, monotonic
        public readonly float DeltaTime;        // s
        public readonly float3 HeadPosition;    // m, geometry pose after the §6.3 step-3 filter; Unity world axes = RoomModel anchor frame
        public readonly quaternion HeadRotation;// Unity convention (App. B)
        public readonly bool TrackingNormal;    // ARKit tracking state is Normal
        public readonly bool PoseJumpRejected;  // step 3 rejected a jump > 0.2 m this frame (State.flags bit 2)

        public FrameContext(long frame, double time, float deltaTime, float3 headPosition, quaternion headRotation,
                            bool trackingNormal, bool poseJumpRejected)
        {
            Frame = frame; Time = time; DeltaTime = deltaTime; HeadPosition = headPosition; HeadRotation = headRotation;
            TrackingNormal = trackingNormal; PoseJumpRejected = poseJumpRejected;
        }
    }
}
