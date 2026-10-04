using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// The hand the lift and the mesh agree on: an average adult right hand, open, in its own palm frame
    /// (documents/ATLAS_Hands_Design.md sections 4.3 and 5.1, Appendix B). A walker's hand is this one times
    /// HandPose.Scale. The left hand is the mirror: x negated.
    ///
    /// Palm frame, right hand: the origin is the wrist; +Z runs from the wrist towards the middle knuckle; +Y is
    /// the palm's normal, out of the palm; +X = cross(Y, Z) is the thumb's side. Metres.
    /// </summary>
    public static class HandModel
    {
        /// <summary>Wrist to middle fingertip of the template, metres.</summary>
        public static float HandLength { get { return s_handLength; } }
        /// <summary>Index knuckle to little-finger knuckle of the template, metres (joint centres).</summary>
        public static float PalmWidth { get { return s_palmWidth; } }
        /// <summary>Wrist to middle knuckle of the template, metres.</summary>
        public static float PalmLength { get { return s_palmLength; } }

        // Where each finger's base sits in the palm frame, the direction its straight finger points, and the
        // lengths of its three bones from base to tip. Thumb first. Adult averages: starting values.
        static readonly float3[] s_base =
        {
            new float3(0.024f, 0.008f, 0.022f),     // thumb CMC
            new float3(0.032f, 0f, 0.090f),         // index MCP
            new float3(0.010f, 0f, 0.094f),         // middle MCP
            new float3(-0.011f, 0f, 0.088f),        // ring MCP
            new float3(-0.031f, 0f, 0.078f),        // little MCP
        };
        static readonly float3[] s_direction =
        {
            math.normalize(new float3(0.70f, 0.20f, 0.68f)),
            math.normalize(new float3(0.10f, 0f, 1f)),
            math.normalize(new float3(0.02f, 0f, 1f)),
            math.normalize(new float3(-0.06f, 0f, 1f)),
            math.normalize(new float3(-0.14f, 0f, 1f)),
        };
        static readonly float3[] s_bones =
        {
            new float3(0.044f, 0.033f, 0.028f),
            new float3(0.043f, 0.025f, 0.022f),
            new float3(0.047f, 0.029f, 0.024f),
            new float3(0.043f, 0.027f, 0.023f),
            new float3(0.034f, 0.019f, 0.021f),
        };
        // Half thickness of the hand at each joint of a finger (base, second, third, tip), metres.
        static readonly float4[] s_radius =
        {
            new float4(0.0125f, 0.0105f, 0.0095f, 0.0085f),
            new float4(0.0100f, 0.0090f, 0.0080f, 0.0070f),
            new float4(0.0100f, 0.0090f, 0.0080f, 0.0070f),
            new float4(0.0095f, 0.0085f, 0.0075f, 0.0065f),
            new float4(0.0085f, 0.0075f, 0.0068f, 0.0060f),
        };

        static readonly float3[] s_rest = BuildRest();
        static readonly float[] s_boneLength = BuildBoneLengths();
        static readonly float s_handLength = math.distance(s_rest[(int)HandJoint.Wrist], s_rest[(int)HandJoint.MiddleTip]);
        static readonly float s_palmWidth = math.distance(s_rest[(int)HandJoint.IndexMcp], s_rest[(int)HandJoint.LittleMcp]);
        static readonly float s_palmLength = math.distance(s_rest[(int)HandJoint.Wrist], s_rest[(int)HandJoint.MiddleMcp]);

        /// <summary>Half the palm's thickness at the knuckles and at the wrist, metres.</summary>
        public const float PalmHalfThicknessKnuckles = 0.011f, PalmHalfThicknessWrist = 0.014f;

        /// <summary>Where a joint of the open template hand is, in the palm frame of the given side.</summary>
        public static float3 Rest(HandSide side, int joint)
        {
            float3 p = s_rest[math.clamp(joint, 0, HandJoints.Count - 1)];
            return side == HandSide.Left ? new float3(-p.x, p.y, p.z) : p;
        }

        /// <summary>Distance from a joint to the joint it hangs from, metres, at scale 1. 0 for the wrist.</summary>
        public static float BoneLength(int joint)
        {
            return s_boneLength[math.clamp(joint, 0, HandJoints.Count - 1)];
        }

        /// <summary>Half the hand's thickness at a joint, metres, at scale 1. The wrist uses the palm's.</summary>
        public static float Radius(int joint)
        {
            if (joint <= 0) return PalmHalfThicknessWrist;
            int finger = HandJoints.FingerOf(joint);
            return s_radius[finger][(joint - 1) % HandJoints.PerFinger];
        }

        /// <summary>The open template hand in the palm frame of the given side, at the given scale.</summary>
        public static JointSet RestPose(HandSide side, float scale = 1f)
        {
            JointSet set = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++) set[i] = Rest(side, i) * scale;
            return set;
        }

        /// <summary>
        /// The template hand with each finger curled, in the palm frame of the given side. curl holds one value
        /// per finger (thumb first), 0 straight to 1 fully bent: every joint of the finger bends towards the palm
        /// by curl times its limit (knuckle 80 degrees, the two finger joints 100 and 70; the thumb 35, 45, 60).
        /// </summary>
        public static JointSet Pose(HandSide side, in float4 fingerCurl, float thumbCurl, float scale = 1f)
        {
            JointSet set = default(JointSet);
            set[0] = float3.zero;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                float curl = math.saturate(finger == 0 ? thumbCurl : fingerCurl[finger - 1]);
                float3 limits = finger == 0
                    ? math.radians(new float3(35f, 45f, 60f))
                    : math.radians(new float3(80f, 100f, 70f));
                float3 direction = s_direction[finger];
                // Bending turns the finger from its straight direction towards the palm's normal, about the axis
                // that is perpendicular to both.
                float3 axis = math.normalizesafe(math.cross(direction, new float3(0f, 1f, 0f)), new float3(1f, 0f, 0f));
                float3 position = s_base[finger];
                int joint = HandJoints.Base(finger);
                set[joint] = Mirror(side, position) * scale;
                float angle = 0f;
                for (int bone = 0; bone < 3; bone++)
                {
                    angle += curl * limits[bone];
                    float3 d = math.mul(quaternion.AxisAngle(axis, angle), direction);
                    position += d * s_bones[finger][bone];
                    set[joint + 1 + bone] = Mirror(side, position) * scale;
                }
            }
            return set;
        }

        static float3 Mirror(HandSide side, float3 p)
        {
            return side == HandSide.Left ? new float3(-p.x, p.y, p.z) : p;
        }

        static float3[] BuildRest()
        {
            var rest = new float3[HandJoints.Count];
            rest[0] = float3.zero;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int joint = HandJoints.Base(finger);
                float3 position = s_base[finger];
                rest[joint] = position;
                for (int bone = 0; bone < 3; bone++)
                {
                    position += s_direction[finger] * s_bones[finger][bone];
                    rest[joint + 1 + bone] = position;
                }
            }
            return rest;
        }

        static float[] BuildBoneLengths()
        {
            var lengths = new float[HandJoints.Count];
            for (int joint = 1; joint < HandJoints.Count; joint++)
                lengths[joint] = math.distance(s_rest[joint], s_rest[HandJoints.Parent(joint)]);
            return lengths;
        }
    }
}
