using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// One hand as the lift leaves it (documents/ATLAS_Hands_Design.md section 4.3): 21 joints in 3D with how
    /// they were obtained. The lift fills it in camera space at capture time; the sense step moves the joints,
    /// the palm centre and the palm normal into world space before the filter sees them.
    /// </summary>
    public struct ObservedHand
    {
        public JointSet Joints;
        /// <summary>0 to 1 (section 4.4, item 6).</summary>
        public float Confidence;
        /// <summary>What the detector called it: 0 left, 1 right, -1 unknown.</summary>
        public int SideHint;
        /// <summary>The palm's distance came from a LiDAR plane fit, not from the hand's size.</summary>
        public bool PalmFromDepth;
        /// <summary>Metres from the camera to the palm's centre, along the camera's forward axis.</summary>
        public float PalmDistance;
        public float3 PalmCentre;
        /// <summary>Unit length, out of the palm.</summary>
        public float3 PalmNormal;
        /// <summary>How far the palm's centre is inside the picture's nearest border, as a share of the picture's height. Negative outside.</summary>
        public float EdgeMargin;
        /// <summary>Thumb-tip to index-tip gap, measured in the picture at the palm's distance, over the palm's width. Small is pinched.</summary>
        public float PinchRatio;
        /// <summary>The palm's centre in the picture, pixels of the captured image.</summary>
        public float2 PalmPixel;
        /// <summary>A size measurement this detection allows (palm square to the camera, LiDAR valid): the hand's scale relative to the template, or 0 for none.</summary>
        public float MeasuredScale;
    }

    /// <summary>
    /// One hand as the filter hands it on each frame (section 4.4): joints in world space at display time,
    /// filtered and predicted, with what later stages gate on.
    /// </summary>
    public struct TrackedHand
    {
        /// <summary>A pose exists: fresh, or held through the grace period.</summary>
        public bool Tracked;
        /// <summary>A detection of this hand arrived this frame.</summary>
        public bool Fresh;
        public JointSet World;
        public float Confidence;
        /// <summary>Seconds from the capture of the detection behind the pose to now.</summary>
        public float AgeSeconds;
        /// <summary>Seconds since this hand was last detected; 0 while detections keep coming.</summary>
        public float LostSeconds;
        /// <summary>Seconds since this hand started being tracked.</summary>
        public float TrackedSeconds;
        /// <summary>Detections received in a row at or above the enter confidence.</summary>
        public int GoodDetections;
        public float3 PalmCentre;
        public float3 PalmNormal;
        /// <summary>World, metres per second, filtered.</summary>
        public float3 PalmVelocity;
        /// <summary>From the last detection (see ObservedHand).</summary>
        public float EdgeMargin;
        public float PinchRatio;
        public float PalmDistance;
        public bool PalmFromDepth;
    }

    /// <summary>The frames a detection passes through on its way into the room (section 3.4).</summary>
    public struct HandsFrames
    {
        /// <summary>Camera at capture to the AR session's space: the detection's own camera pose.</summary>
        public float4x4 SessionFromCamera;
        /// <summary>The AR session's space to world (the room frame once a room is active), as the pose pipeline placed the XR Origin this frame.</summary>
        public float4x4 WorldFromSession;

        public float4x4 WorldFromCamera { get { return math.mul(WorldFromSession, SessionFromCamera); } }
    }
}
