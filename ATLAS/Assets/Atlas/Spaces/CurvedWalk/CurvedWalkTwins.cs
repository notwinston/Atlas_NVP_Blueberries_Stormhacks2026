using Atlas.Input;
using Atlas.Kernel;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.CurvedWalk
{
    // P1-C10: the gaze twins of S2's controls (Master Plan §0.5 rule 5; lane C contract §13.1): the two detents of the
    // curvature dial (DialLeft, DialRight) and the mode chip (Primary). A twin is live exactly when its action can act:
    // the dial and the mode switch are locked while the walker moves (§10.1.4), and the dial stops at the ends of the
    // snaps. The registration, the frame the chips stand in and these rules are here, with no scene object, so they run
    // headless; CurvedWalkHud draws them.
    sealed class CurvedWalkTwins
    {
        public const int Count = 3, DialLeft = 0, DialRight = 1, Mode = 2;

        static readonly float3[] s_chips = { CurvedWalkChips.DialLeft, CurvedWalkChips.DialRight, CurvedWalkChips.Mode };
        static readonly AtlasAction[] s_actions = { AtlasAction.DialLeft, AtlasAction.DialRight, AtlasAction.Primary };
        static readonly string[] s_labels = { "curved.dial-", "curved.dial+", "curved.mode2" };

        readonly IAtlasInput input;
        readonly GazeTargetId[] ids = new GazeTargetId[Count];
        readonly float3[] centres = new float3[Count];
        readonly bool[] live = new bool[Count];
        CurvedWalkChips frame;
        bool updated;

        /// <summary>input may be null: nothing is registered, and the chips are still placed and judged.</summary>
        public CurvedWalkTwins(IAtlasInput input)
        {
            this.input = input;
            if (input == null) return;
            for (int i = 0; i < Count; i++)
            {
                GazeTarget target = GazeTarget.World(s_actions[i], Vector3.zero, CurvedWalkChips.Radius);
                target.Label = s_labels[i];
                ids[i] = input.RegisterGazeTarget(in target);
                input.SetGazeTargetEnabled(ids[i], false);   // live from the first Update
            }
        }

        public GazeTargetId Target(int i) { return ids[i]; }
        public float3 Centre(int i) { return centres[i]; }
        public bool IsLive(int i) { return live[i]; }

        /// <summary>Step 6: the chips follow the head's place and, lazily, its heading.</summary>
        public void Place(float3 head, quaternion headRotation)
        {
            frame.Follow(headRotation);
            for (int i = 0; i < Count; i++)
            {
                centres[i] = frame.Centre(head, s_chips[i]);
                if (input != null && ids[i].IsValid) input.MoveGazeTarget(ids[i], (Vector3)centres[i]);
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

        bool Set(int i, bool value)
        {
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
