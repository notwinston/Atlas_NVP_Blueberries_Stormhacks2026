using Atlas.Input;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.SmallPlanet
{
    // P1-C10: the gaze twin of S4's dial (Master Plan §0.5 rule 5; lane C contract §13.1). q = 3 is the dial's lowest
    // snap, so the planet has one detent, "up" (DialRight), which leads back to Curved Walk. It sits where S2's right
    // detent sits, in a frame that keeps the head's floor position and follows its heading lazily, and it is live exactly
    // when the dial can act: while the walker stands still (§10.1.4). No scene object, so it runs headless; spaces may
    // not reference one another, which is why this repeats the few lines of S2's twins.
    // In a hands session the detent comes within reach (documents/ATLAS_Hands_Design.md §6.1, decision H14): it is placed
    // as always, then ReachSlots.TryGetNear is asked, and on true it stands at its slot in the near anchor's frame, again
    // where S2's right detent stands, with its radius times the scale. The open menu panel stands on that slot, so
    // within reach the detent is neither live nor drawn while a menu holds the input. With no layout, or one that says
    // far, nothing here differs by one number.
    sealed class SmallPlanetDial
    {
        /// <summary>The detent's centre in the frame: x right, y up from the eyes, z ahead. Metres. As S2's right detent.</summary>
        public static readonly float3 Chip = new float3(0.55f, 0.05f, 1.0f);
        public const float Radius = 0.06f;
        /// <summary>The detent's place within reach of the hands, about the near anchor (ReachSlots).</summary>
        public const ReachSlot Slot = ReachSlot.DialRight;
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
            Scale = 1f;
            if (input == null) return;
            Register();   // live from the first SetLive
        }

        // The room frame's origin until step 6 places it. A new target starts enabled, so it takes its own flag at once.
        void Register()
        {
            GazeTarget target = GazeTarget.World(AtlasAction.DialRight, Vector3.zero, Near ? NearRadius(Scale) : Radius);
            target.Label = Label;
            id = input.RegisterGazeTarget(in target);
            input.SetGazeTargetEnabled(id, Live);
        }

        public GazeTargetId Target { get { return id; } }
        public float3 Centre { get; private set; }
        public bool Live { get; private set; }
        /// <summary>True while the detent stands within reach of the hands.</summary>
        public bool Near { get; private set; }
        /// <summary>What multiplies the plate's size where it is drawn: the layout's scale within reach, 1 a metre away.</summary>
        public float Scale { get; private set; }

        /// <summary>The detent's radius within reach: the usual one times the scale, and never under 3 cm across.</summary>
        public static float NearRadius(float scale)
        {
            return ReachSlots.TargetHalf(Radius, scale);
        }

        /// <summary>
        /// Step 6: the detent follows the head's place and, lazily, its heading; in a hands session it then stands at
        /// its slot about the near anchor instead.
        /// </summary>
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

            float3 anchor;
            quaternion facing;
            float size;
            SetReach(ReachSlots.TryGetNear(out anchor, out facing, out size), size);
            if (Near) Centre = ReachSlots.World(anchor, facing, Scale, Slot);
            if (input != null && id.IsValid) input.MoveGazeTarget(id, (Vector3)Centre);
        }

        // The target is a sphere, whose radius cannot be changed in place: it is made again when the detent comes
        // within reach, goes back, or the scale moves.
        void SetReach(bool reach, float size)
        {
            if (!reach) size = 1f;
            if (reach == Near && !ReachSlots.ScaleMoved(Scale, size)) return;
            Near = reach;
            Scale = size;
            if (input == null) return;
            if (id.IsValid) input.UnregisterGazeTarget(id);
            Register();
        }

        /// <summary>
        /// Whether the dial can act now. True when the twin went live or dead. Within reach the open menu panel stands
        /// where the detent stands: while a menu holds the input it is not live, and the space, which draws the plate
        /// only while the twin is live, does not draw it.
        /// </summary>
        public bool SetLive(bool value)
        {
            if (Near && input != null && input.Context == InputContext.Menu && ReachSlots.HidesWhileMenuOpen(Slot)) value = false;
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
