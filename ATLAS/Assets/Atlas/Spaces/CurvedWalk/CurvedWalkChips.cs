using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.CurvedWalk
{
    // P1-B09: where the gaze twins of the dial sit (Master Plan §0.5 rule 5, §10.1.3). Three chips in a frame that keeps
    // the head's floor position and follows its heading lazily: it stays put while the walker looks from chip to chip and
    // turns to face the walker again once the head has turned further than FollowAngle. Pure.
    // In a hands session the chips come within reach instead (documents/ATLAS_Hands_Design.md §6.1): each has its slot
    // about the near anchor, level with each other as they are a metre away, and its radius is multiplied by the scale.
    public struct CurvedWalkChips
    {
        /// <summary>Chip centres in the frame: x right, y up from the eyes, z ahead. Metres.</summary>
        public static readonly float3 DialLeft = new float3(-0.55f, 0.05f, 1.0f);
        public static readonly float3 DialRight = new float3(0.55f, 0.05f, 1.0f);
        /// <summary>Mode 1 to Mode 2: up and to the right, above the dial's right detent. Not below the horizon: the
        /// SpaceLab signs and the menu chip stand there in heading frames of their own, which turn against this one, so
        /// a chip at their elevation ends on one ray with a sign.</summary>
        public static readonly float3 Mode = new float3(0.55f, 0.30f, 1.0f);
        public const float Radius = 0.06f;
        /// <summary>The chips' places within reach of the hands, about the near anchor (ReachSlots).</summary>
        public const ReachSlot DialLeftSlot = ReachSlot.DialLeft, DialRightSlot = ReachSlot.DialRight, ModeSlot = ReachSlot.Mode;
        /// <summary>The frame turns to the head's heading once they differ by more than this. Radians. Wider than the
        /// 29 degrees at which the dial chips sit, so looking at a chip never moves it.</summary>
        public const float FollowAngle = 0.8727f;   // 50 degrees

        float yaw;
        bool placed;

        /// <summary>The frame's heading about +Y, radians (0 looks along +z, positive turns towards +x).</summary>
        public float Yaw { get { return yaw; } }

        /// <summary>Once per frame with the head's rotation. True when the frame turned.</summary>
        public bool Follow(quaternion headRotation)
        {
            float3 f = math.mul(headRotation, new float3(0f, 0f, 1f));
            if (math.lengthsq(f.xz) < 1e-6f) return false;   // looking straight up or down: no heading
            float heading = math.atan2(f.x, f.z);
            float d = heading - yaw;
            d -= 2f * math.PI * math.round(d / (2f * math.PI));
            if (placed && math.abs(d) <= FollowAngle) return false;
            yaw = heading; placed = true;
            return true;
        }

        /// <summary>A chip's centre in the room frame for the head position given.</summary>
        public float3 Centre(float3 head, float3 chip)
        {
            return head + math.mul(quaternion.RotateY(yaw), chip);
        }

        /// <summary>A chip's radius within reach: the usual one times the scale, and never under 3 cm across.</summary>
        public static float NearRadius(float scale)
        {
            return ReachSlots.TargetHalf(Radius, scale);
        }
    }
}
