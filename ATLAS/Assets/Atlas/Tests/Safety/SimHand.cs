using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// A synthetic open hand for the Bump Guard tests: a flat plate the size of a hand, palm to the camera and
    /// fingers up, held at a fixed place in front of the camera, with or without a forearm that runs down and
    /// back towards the body. It travels with the head, as <see cref="SimPlate"/> does. It also says what the
    /// depth mask for it is, built the way the hands design describes (section 7.1), so that the tests feed
    /// Bump Guard the kind of mask it will get.
    /// </summary>
    public struct SimHand
    {
        /// <summary>The plate: across the hand and from wrist to fingertips, metres.</summary>
        public const float Width = 0.09f, Length = 0.19f;
        /// <summary>The forearm: its thickness, how far the elbow is below and behind the wrist, metres.</summary>
        public const float ArmThickness = 0.06f, ElbowDrop = 0.18f, ElbowBack = 0.17f;
        const int ArmPieces = 8;

        // The numbers of the design's mask (its Appendix B), as the builder has them.
        public const float PalmWidth = 0.08f;
        public const float Grow = 0.35f;
        public const float ArmPalmWidths = 1.2f;
        public const float BehindPalm = 0.20f;
        public const float MaxDepth = 0.80f;

        /// <summary>Centre of the plate in camera space (+x right, +y up, +z ahead), metres; z is the palm's depth.</summary>
        public float3 Palm;
        public bool Forearm;

        public SimHand(float x, float y, float depth, bool forearm = false)
        {
            Palm = new float3(x, y, depth);
            Forearm = forearm;
        }

        /// <summary>The wrist: the middle of the plate's lower edge.</summary>
        public float3 Wrist { get { return Palm - new float3(0f, 0.5f * Length, 0f); } }

        /// <summary>Adds the hand, and its forearm if it has one, to what the camera carries.</summary>
        public void AddTo(List<SimPlate> body)
        {
            body.Add(new SimPlate(Palm, Width, Length));
            if (!Forearm) return;
            float3 wrist = Wrist;
            for (int k = 0; k < ArmPieces; k++)
            {
                float s = (k + 0.5f) / ArmPieces;
                body.Add(new SimPlate(wrist + new float3(0f, -ElbowDrop * s, -ElbowBack * s), ArmThickness, ElbowDrop / ArmPieces));
            }
        }

        /// <summary>
        /// This hand's region of the depth mask: the box of the hand in the image grown by 35 % of its longer
        /// side on every side, a strip from the wrist straight down to the image's lower border as wide as 1.2
        /// palm widths plus the same growth, and a gate 0.20 m behind the palm, never beyond 0.80 m.
        /// </summary>
        public DepthMaskRegion Region(in DepthIntrinsics k, int imageHeight)
        {
            float scale = k.Fx / Palm.z, scaleY = k.Fy / Palm.z;
            float u = k.Cx + Palm.x * scale, v = k.Cy - Palm.y * scaleY;
            float halfU = 0.5f * Width * scale, halfV = 0.5f * Length * scaleY;
            float grow = Grow * 2f * math.max(halfU, halfV);
            return new DepthMaskRegion
            {
                MinU = u - halfU - grow,
                MaxU = u + halfU + grow,
                MinV = v - halfV - grow,
                MaxV = v + halfV + grow,
                ArmU0 = u,
                ArmV0 = v + halfV,
                ArmU1 = u,
                ArmV1 = imageHeight,
                ArmHalfWidth = 0.5f * ArmPalmWidths * PalmWidth * scale + grow,
                MaxDepth = math.min(Palm.z + BehindPalm, MaxDepth)
            };
        }

        /// <summary>The mask of one hand for the depth image with this sequence number.</summary>
        public static DepthMask Mask(int depthSequence, in DepthIntrinsics k, int imageHeight, in SimHand hand)
        {
            return new DepthMask { Count = 1, Region0 = hand.Region(k, imageHeight), DepthSequence = depthSequence };
        }

        /// <summary>The mask of two hands for the depth image with this sequence number.</summary>
        public static DepthMask Mask(int depthSequence, in DepthIntrinsics k, int imageHeight, in SimHand first, in SimHand second)
        {
            return new DepthMask
            {
                Count = 2,
                Region0 = first.Region(k, imageHeight),
                Region1 = second.Region(k, imageHeight),
                DepthSequence = depthSequence
            };
        }
    }
}
