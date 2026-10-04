using Atlas.Input;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    // P1-C10: the gaze twin of S4's dial (Master Plan §0.5 rule 5; lane C contract §13.1). q = 3 is the dial's lowest
    // snap, so the planet has one detent, "up" (DialRight), which leads back to Curved Walk. It sits where S2's right
    // detent sits, in a frame that keeps the head's floor position and follows its heading lazily, and it is live exactly
    // when the dial can act: while the walker stands still (§10.1.4). No scene object, so it runs headless; spaces may
    // not reference one another, which is why this repeats the few lines of S2's twins.
    sealed class SmallPlanetDial
    {
        /// <summary>The detent's centre in the frame: x right, y up from the eyes, z ahead. Metres. As S2's right detent.</summary>
        public static readonly float3 Chip = new float3(0.55f, 0.05f, 1.0f);
        public const float Radius = 0.06f;
        /// <summary>The frame turns to the head's heading once they differ by more than this. Radians. Wider than the
        /// 29 degrees at which the detent sits, so looking at it never moves it.</summary>
        public const float FollowAngle = 0.8727f;   // 50 degrees
        public const string Label = "planet.dial+";

        readonly IAtlasInput input;
        GazeTargetId id;
        float yaw;
        bool placed;

        /// <summary>input may be null: nothing is registered, and the detent is still placed and judged.</summary>
        public SmallPlanetDial(IAtlasInput input)
        {
            this.input = input;
            if (input == null) return;
            GazeTarget target = GazeTarget.World(AtlasAction.DialRight, Vector3.zero, Radius);
            target.Label = Label;
            id = input.RegisterGazeTarget(in target);
            input.SetGazeTargetEnabled(id, false);   // live from the first SetLive
        }

        public GazeTargetId Target { get { return id; } }
        public float3 Centre { get; private set; }
        public bool Live { get; private set; }

        /// <summary>Step 6: the detent follows the head's place and, lazily, its heading.</summary>
        public void Place(float3 head, quaternion headRotation)
        {
            float3 f = math.mul(headRotation, new float3(0f, 0f, 1f));
            if (math.lengthsq(f.xz) >= 1e-6f)   // looking straight up or down: no heading
            {
                float heading = math.atan2(f.x, f.z);
                float d = heading - yaw;
                d -= 2f * math.PI * math.round(d / (2f * math.PI));
                if (!placed || math.abs(d) > FollowAngle) { yaw = heading; placed = true; }
            }
            Centre = head + math.mul(quaternion.RotateY(yaw), Chip);
            if (input != null && id.IsValid) input.MoveGazeTarget(id, (Vector3)Centre);
        }

        /// <summary>Whether the dial can act now. True when the twin went live or dead.</summary>
        public bool SetLive(bool value)
        {
            if (Live == value) return false;
            Live = value;
            if (input != null && id.IsValid) input.SetGazeTargetEnabled(id, value);
            return true;
        }

        public void Dispose()
        {
            if (input != null && id.IsValid) input.UnregisterGazeTarget(id);
            id = default;
            Live = false;
        }
    }
}
