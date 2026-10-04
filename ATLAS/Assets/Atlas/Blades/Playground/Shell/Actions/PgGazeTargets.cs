using System;
using Atlas.Input;
using UnityEngine;

namespace Atlas.Blades.Playground
{
    /// <summary>The places in the Playground where a gaze dwell is an input: the gaze-dwell column of the twins table
    /// (Master Plan section 10.1.3). Whoever owns the thing in the world registers its slot.</summary>
    public enum PgSlot : byte
    {
        None = 0,
        PromptChip = 1,
        DialDetent3 = 2, DialDetent4 = 3, DialDetent5 = 4, DialDetent6 = 5,
        ModeChip = 6,
        ExhibitSign = 7,
        GluingSign = 8,
        WRulerLow = 9, WRulerHigh = 10,
        RotateDetent0 = 11, RotateDetent1 = 12, RotateDetent2 = 13, RotateDetent3 = 14, RotateDetent4 = 15,
        LookDown = 16,
        HomeBeacon = 17
    }

    /// <summary>What a slot's gaze target fires: the input service's action or axis, its tag and its mode.</summary>
    public readonly struct PgSlotSpec
    {
        public readonly PgSlot Slot;
        public readonly PgAction Twin;
        public readonly AtlasAction Action;
        public readonly AtlasAxis Axis;
        public readonly float AxisValue;
        public readonly int Tag;
        public readonly GazeMode Mode;
        public readonly string Label;

        public PgSlotSpec(PgSlot slot, PgAction twin, AtlasAction action, int tag, string label)
            : this(slot, twin, action, AtlasAxis.Sweep, 0f, tag, GazeMode.Once, label) { }

        public PgSlotSpec(PgSlot slot, PgAction twin, AtlasAxis axis, float axisValue, string label)
            : this(slot, twin, AtlasAction.None, axis, axisValue, 0, GazeMode.Hold, label) { }

        PgSlotSpec(PgSlot slot, PgAction twin, AtlasAction action, AtlasAxis axis, float axisValue, int tag, GazeMode mode, string label)
        {
            Slot = slot;
            Twin = twin;
            Action = action;
            Axis = axis;
            AxisValue = axisValue;
            Tag = tag;
            Mode = mode;
            Label = label;
        }

        public bool IsAxis { get { return Mode == GazeMode.Hold && Action == AtlasAction.None; } }
    }

    /// <summary>The table of slots. Dial detents: the left two carry DialLeft and the right two DialRight with the
    /// detent's q as the tag, so both d-pad actions keep a gaze twin while a dwell still names the position.</summary>
    public static class PgSlots
    {
        public const int Count = 18;

        static readonly PgSlotSpec[] s_slots =
        {
            default,
            new PgSlotSpec(PgSlot.PromptChip, PgAction.Confirm, AtlasAction.Confirm, 0, "prompt chip"),
            new PgSlotSpec(PgSlot.DialDetent3, PgAction.DialTo, AtlasAction.DialLeft, 3, "dial q=3"),
            new PgSlotSpec(PgSlot.DialDetent4, PgAction.DialTo, AtlasAction.DialLeft, 4, "dial q=4"),
            new PgSlotSpec(PgSlot.DialDetent5, PgAction.DialTo, AtlasAction.DialRight, 5, "dial q=5"),
            new PgSlotSpec(PgSlot.DialDetent6, PgAction.DialTo, AtlasAction.DialRight, 6, "dial q=6"),
            new PgSlotSpec(PgSlot.ModeChip, PgAction.Mode, AtlasAction.Primary, 0, "mode chip"),
            new PgSlotSpec(PgSlot.ExhibitSign, PgAction.ExhibitOpen, AtlasAction.SpaceMenu, 0, "exhibit sign"),
            new PgSlotSpec(PgSlot.GluingSign, PgAction.Gluing, AtlasAction.VariantNext, 0, "gluing sign"),
            new PgSlotSpec(PgSlot.WRulerLow, PgAction.SliceW, AtlasAxis.Sweep, -1f, "w-ruler low end"),
            new PgSlotSpec(PgSlot.WRulerHigh, PgAction.SliceW, AtlasAxis.Sweep, 1f, "w-ruler high end"),
            new PgSlotSpec(PgSlot.RotateDetent0, PgAction.Rotate, AtlasAxis.Twist, -1f, "rotate detent 1"),
            new PgSlotSpec(PgSlot.RotateDetent1, PgAction.Rotate, AtlasAxis.Twist, -0.5f, "rotate detent 2"),
            new PgSlotSpec(PgSlot.RotateDetent2, PgAction.Rotate, AtlasAxis.Twist, 0f, "rotate detent 3"),
            new PgSlotSpec(PgSlot.RotateDetent3, PgAction.Rotate, AtlasAxis.Twist, 0.5f, "rotate detent 4"),
            new PgSlotSpec(PgSlot.RotateDetent4, PgAction.Rotate, AtlasAxis.Twist, 1f, "rotate detent 5"),
            new PgSlotSpec(PgSlot.LookDown, PgAction.Shadow, AtlasAction.Secondary, 0, "look down"),
            new PgSlotSpec(PgSlot.HomeBeacon, PgAction.ReturnHome, AtlasAction.ReturnHome, 0, "home beacon"),
        };

        public static ref readonly PgSlotSpec Of(PgSlot slot)
        {
            int i = (int)slot;
            return ref s_slots[i > 0 && i < Count ? i : 0];
        }

        /// <summary>The slot of a dial position (3 to 6), or None.</summary>
        public static PgSlot DialDetent(int q)
        {
            return q >= ActionMap.MinQ && q <= ActionMap.MaxQ ? (PgSlot)((int)PgSlot.DialDetent3 + (q - ActionMap.MinQ)) : PgSlot.None;
        }
    }

    /// <summary>
    /// The registry of the Playground's gaze targets (rule 5 of Master Plan section 0.5: whoever handles an action
    /// registers its gaze target while the action is available). The owner gives the geometry of a slot, the
    /// action, tag and mode come from <see cref="PgSlots"/>. Main thread only.
    /// </summary>
    public sealed class PgGazeTargets : IDisposable
    {
        readonly IAtlasInput _input;
        readonly GazeTargetId[] _ids = new GazeTargetId[PgSlots.Count];

        public PgGazeTargets(IAtlasInput input)
        {
            _input = input ?? throw new ArgumentNullException(nameof(input));
        }

        public bool IsRegistered(PgSlot slot)
        {
            int i = (int)slot;
            return i > 0 && i < PgSlots.Count && _ids[i].IsValid;
        }

        /// <summary>
        /// Registers the slot, or replaces its geometry. geometry is a target built with the shape and the frame
        /// (<see cref="GazeTarget.World"/>, <see cref="GazeTarget.Head"/>, the rect builders, or
        /// <see cref="GazeTarget.LookDown"/>); the slot overwrites what it fires. False when the service had no
        /// room left.
        /// </summary>
        public bool Bind(PgSlot slot, GazeTarget geometry)
        {
            int i = (int)slot;
            if (i <= 0 || i >= PgSlots.Count) return false;
            ref readonly PgSlotSpec spec = ref PgSlots.Of(slot);
            geometry.Action = spec.Action;
            geometry.Axis = spec.Axis;
            geometry.AxisValue = spec.AxisValue;
            geometry.Tag = spec.Tag;
            geometry.Mode = spec.Mode;
            geometry.Label = spec.Label;
            if (geometry.Context == InputContext.InHand) geometry.Context = InputContext.Space;
            if (_ids[i].IsValid) _input.UnregisterGazeTarget(_ids[i]);
            _ids[i] = _input.RegisterGazeTarget(in geometry);
            return _ids[i].IsValid;
        }

        /// <summary>The look-down target of the shadow (pitch below -35 degrees, R2).</summary>
        public bool BindLookDown(float pitchBelowDeg = -35f)
        {
            return Bind(PgSlot.LookDown, GazeTarget.LookDown(AtlasAction.Secondary, pitchBelowDeg));
        }

        public void Move(PgSlot slot, Vector3 centre)
        {
            if (IsRegistered(slot)) _input.MoveGazeTarget(_ids[(int)slot], centre);
        }

        public void SetEnabled(PgSlot slot, bool enabled)
        {
            if (IsRegistered(slot)) _input.SetGazeTargetEnabled(_ids[(int)slot], enabled);
        }

        public void Unbind(PgSlot slot)
        {
            int i = (int)slot;
            if (i <= 0 || i >= PgSlots.Count || !_ids[i].IsValid) return;
            _input.UnregisterGazeTarget(_ids[i]);
            _ids[i] = default;
        }

        /// <summary>The slot whose target the id is, or None.</summary>
        public PgSlot SlotOf(GazeTargetId id)
        {
            if (!id.IsValid) return PgSlot.None;
            for (int i = 1; i < PgSlots.Count; i++)
                if (_ids[i] == id) return (PgSlot)i;
            return PgSlot.None;
        }

        public void Dispose()
        {
            for (int i = 1; i < PgSlots.Count; i++) Unbind((PgSlot)i);
        }
    }

    /// <summary>
    /// The dwell reticle of the Playground (Master Plan task P2-C07). The head-locked dot and ring are drawn by the
    /// input service (P1-C10, <see cref="GazeReticle"/>) and fill over the dwell time; this sets that time to 700 ms
    /// (Engineering Spec section 4.5) and says what is under the gaze, so a chip or a sign can light up while it
    /// fills. Engine-free apart from the service's types.
    /// </summary>
    public sealed class DwellReticle
    {
        readonly PgGazeTargets _targets;

        public DwellReticle(PgGazeTargets targets)
        {
            _targets = targets;
        }

        /// <summary>The slot under the gaze, or None.</summary>
        public PgSlot Slot { get; private set; }
        /// <summary>0 to 1 of the dwell.</summary>
        public float Progress { get; private set; }
        public bool OnTarget { get { return Slot != PgSlot.None; } }

        /// <summary>Sets the dwell time of the input service to 0.7 s.</summary>
        public static void Apply(IAtlasInput input)
        {
            if (input != null) input.DwellSeconds = ActionMap.DwellSeconds;
        }

        /// <summary>Once per frame, after the input service's Tick.</summary>
        public void Update(in GazeState gaze)
        {
            Slot = _targets != null ? _targets.SlotOf(gaze.Focused) : PgSlot.None;
            Progress = Slot != PgSlot.None ? gaze.Progress : 0f;
        }
    }
}
