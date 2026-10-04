using Atlas.Input;
using Atlas.Kernel;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    // P1-C10: the gaze twins of S2's controls (Master Plan §0.5 rule 5; lane C contract §13.1): the two detents of the
    // curvature dial (DialLeft, DialRight) and the mode chip (Primary). A twin is live exactly when its action can act:
    // the dial and the mode switch are locked while the walker moves (§10.1.4), and the dial stops at the ends of the
    // snaps. The registration, the frame the chips stand in and these rules are here, with no scene object, so they run
    // headless; CurvedWalkHud draws them.
    // In a hands session the chips come within reach (documents/ATLAS_Hands_Design.md §6.1, decision H14): they are placed
    // as always, then ReachSlots.TryGetNear is asked, and on true each stands at its slot in the near anchor's frame with
    // its radius times the scale. The open menu panel stands on those slots, so within reach the chips are neither live
    // nor drawn while a menu holds the input. With no layout, or one that says far, nothing here differs by one number.
    sealed class CurvedWalkTwins
    {
        public const int Count = 3, DialLeft = 0, DialRight = 1, Mode = 2;

        static readonly float3[] s_chips = { CurvedWalkChips.DialLeft, CurvedWalkChips.DialRight, CurvedWalkChips.Mode };
        static readonly ReachSlot[] s_slots = { CurvedWalkChips.DialLeftSlot, CurvedWalkChips.DialRightSlot, CurvedWalkChips.ModeSlot };
        static readonly AtlasAction[] s_actions = { AtlasAction.DialLeft, AtlasAction.DialRight, AtlasAction.Primary };
        static readonly string[] s_labels = { "curved.dial-", "curved.dial+", "curved.mode2" };

        readonly IAtlasInput input;
        readonly GazeTargetId[] ids = new GazeTargetId[Count];
        readonly float3[] centres = new float3[Count];
        readonly bool[] live = new bool[Count];
        CurvedWalkChips frame;
        bool updated;
        // Within reach of the hands, and the scale in force there (1 when far).
        bool near;
        float scale = 1f;

        /// <summary>input may be null: nothing is registered, and the chips are still placed and judged.</summary>
        public CurvedWalkTwins(IAtlasInput input)
        {
            this.input = input;
            if (input == null) return;
            for (int i = 0; i < Count; i++) Register(i);   // live from the first Update
        }

        // The room frame's origin until step 6 places it. A new target starts enabled, so it takes its own flag at once.
        void Register(int i)
        {
            float radius = near ? CurvedWalkChips.NearRadius(scale) : CurvedWalkChips.Radius;
            GazeTarget target = GazeTarget.World(s_actions[i], Vector3.zero, radius);
            target.Label = s_labels[i];
            ids[i] = input.RegisterGazeTarget(in target);
            input.SetGazeTargetEnabled(ids[i], live[i]);
        }

        public GazeTargetId Target(int i) { return ids[i]; }
        public float3 Centre(int i) { return centres[i]; }
        public bool IsLive(int i) { return live[i]; }
        /// <summary>A chip's place within reach of the hands.</summary>
        public static ReachSlot SlotOf(int i) { return s_slots[i]; }
        /// <summary>True while the chips stand within reach of the hands.</summary>
        public bool Near { get { return near; } }
        /// <summary>What multiplies a chip's size where it is drawn: the layout's scale within reach, 1 a metre away.</summary>
        public float Scale { get { return scale; } }

        /// <summary>
        /// Step 6: the chips follow the head's place and, lazily, its heading; in a hands session they then stand at
        /// their slots about the near anchor instead.
        /// </summary>
        public void Place(float3 head, quaternion headRotation)
        {
            frame.Follow(headRotation);
            float3 anchor;
            quaternion facing;
            float size;
            SetReach(ReachSlots.TryGetNear(out anchor, out facing, out size), size);
            for (int i = 0; i < Count; i++)
            {
                centres[i] = near ? ReachSlots.World(anchor, facing, scale, s_slots[i]) : frame.Centre(head, s_chips[i]);
                if (input != null && ids[i].IsValid) input.MoveGazeTarget(ids[i], (Vector3)centres[i]);
            }
        }

        // The targets are spheres, whose radius cannot be changed in place: they are made again when the chips come
        // within reach, go back, or the scale moves.
        void SetReach(bool reach, float size)
        {
            if (!reach) size = 1f;
            if (reach == near && !ReachSlots.ScaleMoved(scale, size)) return;
            near = reach;
            scale = size;
            if (input == null) return;
            for (int i = 0; i < Count; i++)
            {
                if (ids[i].IsValid) input.UnregisterGazeTarget(ids[i]);
                Register(i);
            }
        }

        /// <summary>
        /// Which twins can act now: q is the dial snap, still is the kernel's stillness of this frame. True when a
        /// twin went live or dead since the last call (the first call counts), so the caller redraws the chips.
        /// </summary>
        public bool Update(int q, bool still)
        {
            bool changed = !updated;
            updated = true;
            changed |= Set(DialLeft, still && q > CurvatureDial.MinQ);
            changed |= Set(DialRight, still && q < CurvatureDial.MaxQ);
            changed |= Set(Mode, still);
            return changed;
        }

        // Within reach the open menu panel stands where the chips stand: while a menu holds the input they are not live,
        // and CurvedWalkHud, which draws a chip only while it is live, does not draw them.
        bool Hidden(int i)
        {
            return near && input != null && input.Context == InputContext.Menu && ReachSlots.HidesWhileMenuOpen(s_slots[i]);
        }

        bool Set(int i, bool value)
        {
            value &= !Hidden(i);
            if (live[i] == value) return false;
            live[i] = value;
            if (input != null && ids[i].IsValid) input.SetGazeTargetEnabled(ids[i], value);
            return true;
        }

        public void Dispose()
        {
            for (int i = 0; i < Count; i++)
            {
                if (input != null && ids[i].IsValid) input.UnregisterGazeTarget(ids[i]);
                ids[i] = default;
                live[i] = false;
            }
        }
    }
}
