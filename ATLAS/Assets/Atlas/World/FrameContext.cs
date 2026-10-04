// PLACEHOLDER (lane A, P1-X05): contract surface only; replaced by lane A's implementation
using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>What the frame loop hands to the active space each frame. Master Plan §6.3 (fields: deviation PH-2).</summary>
    public readonly struct FrameContext
    {
        public readonly long Frame;             // Master Plan §6.3: frame counter, +1 per frame
        public readonly double Time;            // Master Plan §6.3: s, monotonic
        public readonly float DeltaTime;        // Master Plan §6.3: s
        public readonly float3 HeadPosition;    // Master Plan §6.3 step 3, §4.2: m, geometry pose after the §6.3 step-3 filter; Unity world axes = RoomModel anchor frame
        public readonly quaternion HeadRotation;// Master Plan §6.3, App. B: Unity convention
        public readonly bool TrackingNormal;    // Master Plan §6.3, App. B.1 flags: ARKit tracking state is Normal
        public readonly bool PoseJumpRejected;  // Master Plan §6.3 step 3: rejected a jump > 0.2 m this frame (State.flags bit 2)

        public FrameContext(long frame, double time, float deltaTime, float3 headPosition, quaternion headRotation,
                            bool trackingNormal, bool poseJumpRejected)
        {
            Frame = frame; Time = time; DeltaTime = deltaTime; HeadPosition = headPosition; HeadRotation = headRotation;
            TrackingNormal = trackingNormal; PoseJumpRejected = poseJumpRejected;
        }
    }
}
