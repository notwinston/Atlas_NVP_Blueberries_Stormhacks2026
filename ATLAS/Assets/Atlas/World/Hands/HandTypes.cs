using Unity.Mathematics;

namespace Atlas.World
{
    /// <summary>
    /// Which of the walker's tracked hands (documents/ATLAS_Hands_Design.md section 3.6). "Hand" alone means
    /// "the phone is held in the hand" elsewhere in this codebase (DisplayMode.Hand, InputContext.InHand);
    /// the tracked-hands feature is always the plural, Hands.
    /// </summary>
    public enum HandSide : byte { Left = 0, Right = 1 }

    /// <summary>The 21 joints: the wrist, then each finger from its base to its tip. The values index every joint array.</summary>
    public enum HandJoint : byte
    {
        Wrist = 0,
        ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4,
        IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8,
        MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12,
        RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16,
        LittleMcp = 17, LittlePip = 18, LittleDip = 19, LittleTip = 20
    }

    /// <summary>The shape of the joint set: five chains of four joints hanging off the wrist.</summary>
    public static class HandJoints
    {
        public const int Count = 21;
        public const int FingerCount = 5;
        /// <summary>Joints in one finger's chain, base to tip.</summary>
        public const int PerFinger = 4;

        /// <summary>The base joint of a finger: 0 thumb (ThumbCmc), 1 index (IndexMcp), 2 middle, 3 ring, 4 little.</summary>
        public static int Base(int finger) { return 1 + PerFinger * finger; }

        /// <summary>The tip joint of a finger.</summary>
        public static int Tip(int finger) { return PerFinger + PerFinger * finger; }

        /// <summary>The finger a joint belongs to, or -1 for the wrist.</summary>
        public static int FingerOf(int joint) { return joint <= 0 ? -1 : (joint - 1) / PerFinger; }

        /// <summary>The joint a joint hangs from: the wrist for a finger's base, -1 for the wrist itself.</summary>
        public static int Parent(int joint)
        {
            if (joint <= 0) return -1;
            return (joint - 1) % PerFinger == 0 ? 0 : joint - 1;
        }
    }

    /// <summary>One tracked hand in one frame. Valid from step 1 of a frame until step 1 of the next.</summary>
    public struct HandPose
    {
        /// <summary>A pose exists: fresh, or held through the grace period of a brief loss.</summary>
        public bool Tracked;
        /// <summary>0 to 1. Touch and the depth mask gate on it.</summary>
        public float Confidence;
        /// <summary>0 to 1: what is drawn (enter, exit, edge of the camera's view, Bump Guard warning).</summary>
        public float Opacity;
        /// <summary>Seconds between the capture of the camera frame behind this pose and now.</summary>
        public float AgeSeconds;
        /// <summary>This walker's hand size relative to the template; 1 until measured.</summary>
        public float Scale;
        /// <summary>World, metres.</summary>
        public float3 PalmCentre;
        /// <summary>World, unit length, out of the palm.</summary>
        public float3 PalmNormal;
        /// <summary>0 open to 1 closed.</summary>
        public float Pinch;
        /// <summary>After hysteresis.</summary>
        public bool Pinching;
    }

    /// <summary>Both hands pinching at once. Recognised and reported; nothing is bound to it yet.</summary>
    public struct TwoHandPinch
    {
        public bool Active;
        /// <summary>Metres between the two pinch points.</summary>
        public float Distance;
        /// <summary>Radians: the line from the left pinch point to the right one, about the view axis. Continuous while Active (a full turn reads 2 pi).</summary>
        public float Angle;
        /// <summary>World, metres.</summary>
        public float3 Midpoint;
    }
}
