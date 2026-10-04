using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>
    /// The press point of one index finger this frame (documents/ATLAS_Hands_Design.md section 6.2): the pad of
    /// the finger, a little back from the tip joint. Atlas.Input references no other Atlas assembly, so the hands
    /// module hands its fingertips over in these plain values.
    /// </summary>
    public struct HandsFingertip
    {
        /// <summary>The hand is tracked and shown this frame. False: the fingertip touches nothing and does not pinch.</summary>
        public bool Valid;
        /// <summary>Room frame, metres.</summary>
        public float3 World;
        /// <summary>Room frame, metres per second. Carried with the fingertip; touch itself works from the places it has seen.</summary>
        public float3 VelocityWorld;
        /// <summary>The hand's confidence, 0 to 1. Below 0.6 the hand is not trusted: it touches nothing and does not pinch.</summary>
        public float Confidence;
    }

    /// <summary>What the tracked hands say this frame: given to <see cref="IHandsInput.SetHands"/> before the input tick.</summary>
    public struct HandsInput
    {
        /// <summary>Hands may act this frame. False (the default) while there is no hands session, and while Bump Guard warns: hands do nothing.</summary>
        public bool Active;
        public HandsFingertip Left, Right;
        /// <summary>True in the one frame in which that hand's pinch closed, after the hands module's own checks.</summary>
        public bool PinchDownLeft, PinchDownRight;
    }

    /// <summary>What one fingertip did in the last input tick: for the ring under the fingertip, the click and the event log.</summary>
    public struct HandsTouchHand
    {
        /// <summary>The target the fingertip is over; default when it is over none.</summary>
        public GazeTargetId Target;
        /// <summary>0 to 1: 0 with the fingertip 8 cm in front of the target's face or farther, 1 on the face and through it.</summary>
        public float Hover;
        /// <summary>The fingertip is through the press plane of the target it pressed, and has not let go.</summary>
        public bool Pressed;
        /// <summary>True for the frame in which the fingertip fired the target: on the press, and on each repeat of a Repeat target.</summary>
        public bool Fired;
        /// <summary>Room frame, metres: the point of the target's face under the fingertip, kept inside the target's outline.</summary>
        public float3 FacePoint;
        /// <summary>Room frame, unit length: the normal of that face, towards the walker.</summary>
        public float3 FaceNormal;
    }

    /// <summary>What touch and pinch did in the last input tick.</summary>
    public struct HandsTouchState
    {
        public HandsTouchHand Left, Right;
        /// <summary>A pinch fired the target under the gaze in this tick.</summary>
        public bool PinchFired;
        /// <summary>The target that pinch fired; default when none did.</summary>
        public GazeTargetId PinchTarget;
        /// <summary>A trusted fingertip is within 0.20 m of a target that can be touched: the dwell on that target waits.</summary>
        public bool AnyNear;
    }
}
