using System;
using Atlas.Input;

namespace Atlas.Blades.Playground
{
    /// <summary>What the Playground's shell asks for, one value for each row of the twins table of Master Plan
    /// section 10.1.3. The shell, the exhibits and the Slicer toybox read these; none of them reads a gamepad
    /// button, a dwell or the lever directly.</summary>
    public enum PgAction : byte
    {
        None = 0,
        /// <summary>Confirm / next beat.</summary>
        Confirm = 1,
        /// <summary>Dial q one detent: Value is -1 or +1.</summary>
        DialStep = 2,
        /// <summary>Dial q to a detent: Value is q (3 to 6). Gaze dwell on a detent, and the lever's cycle.</summary>
        DialTo = 3,
        /// <summary>Mode 1 and Mode 2 swap.</summary>
        Mode = 4,
        /// <summary>Open the exhibit menu.</summary>
        ExhibitOpen = 5,
        /// <summary>Step the menu's selection: Value is -1 or +1.</summary>
        ExhibitStep = 6,
        /// <summary>Pick: Value is the sign dwelt on, or -1 for the selected one.</summary>
        ExhibitPick = 7,
        /// <summary>Gluing T2, Klein, T3: Value is +1 or -1 (bumpers), 0 for the sign and the lever (next).</summary>
        Gluing = 8,
        /// <summary>The lever's double click in the Slicer: rotate by <see cref="ActionMap.RotateStepDegrees"/>.</summary>
        RotateStep = 9,
        /// <summary>Shadow (X, or looking down past -35 degrees).</summary>
        Shadow = 10,
        /// <summary>Return Home.</summary>
        ReturnHome = 11,
        /// <summary>The Slicer's w sweep and its rotation are held axes, read from the input service
        /// (<see cref="AtlasAxis.Sweep"/>, <see cref="AtlasAxis.Twist"/>), not events. These two values name their rows.</summary>
        SliceW = 12,
        Rotate = 13
    }

    /// <summary>One firing of <see cref="ActionMap"/>.</summary>
    public readonly struct PgActionEvent
    {
        public readonly PgAction Action;
        public readonly InputSource Source;
        /// <summary>See <see cref="PgAction"/>.</summary>
        public readonly int Value;

        public PgActionEvent(PgAction action, InputSource source, int value)
        {
            Action = action;
            Source = source;
            Value = value;
        }
    }

    /// <summary>What a single click of the lever means while a space runs.</summary>
    public enum LeverClickRole : byte
    {
        /// <summary>Confirm / next beat.</summary>
        Confirm = 0,
        /// <summary>Cycle the dial 3 to 4 to 5 to 6.</summary>
        Dial = 1,
        /// <summary>Next gluing.</summary>
        Gluing = 2
    }

    /// <summary>What a double click of the lever means while a space runs.</summary>
    public enum LeverDoubleRole : byte
    {
        None = 0,
        /// <summary>Mode 1 and Mode 2 swap.</summary>
        Mode = 1,
        /// <summary>Step the rotation by 15 degrees.</summary>
        RotateStep = 2
    }

    /// <summary>One row of the twins table: what each of the three inputs is, in the plan's words.</summary>
    public readonly struct PgTwin
    {
        public readonly PgAction Action;
        public readonly string Name;
        public readonly string Gamepad;
        public readonly string GazeDwell;
        /// <summary>Null for the shadow, which has a gaze twin only.</summary>
        public readonly string Lever;
        /// <summary>The input service actions the gamepad route is made of, and a second for stick and A.</summary>
        public readonly AtlasAction Primary;
        public readonly AtlasAction Secondary;
        /// <summary>The held axis of the row, when it has one.</summary>
        public readonly bool HasAxis;
        public readonly AtlasAxis Axis;

        public PgTwin(PgAction action, string name, string gamepad, string gaze, string lever,
                      AtlasAction primary, AtlasAction secondary, bool hasAxis = false, AtlasAxis axis = AtlasAxis.Sweep)
        {
            Action = action;
            Name = name;
            Gamepad = gamepad;
            GazeDwell = gaze;
            Lever = lever;
            Primary = primary;
            Secondary = secondary;
            HasAxis = hasAxis;
            Axis = axis;
        }
    }

    /// <summary>
    /// The action twins of the Geometry Playground (Master Plan section 10.1.3, task P2-C07). Hand tracking is not
    /// in the Core build, so every action has a gamepad input and a gaze-dwell input, and every action but the
    /// shadow a headset-lever input. The service of lane C (P1-C10) already turns gamepad, keys and dwell into
    /// <see cref="AtlasAction"/> firings; this class reads those, adds the lever's gestures (which that service
    /// does not know: it fires <see cref="AtlasAction.Confirm"/> on every touch), and answers in
    /// <see cref="PgAction"/>. Engine-free. Main thread only.
    /// <para>
    /// The lever of the input service is dropped here on purpose: its every touch is a Confirm, and the lever of
    /// the Playground means a different action in each context (a click confirms, cycles the dial or steps the
    /// gluing). The gestures come from <see cref="Lever"/> instead, fed with the raw touch.
    /// </para>
    /// </summary>
    public sealed class ActionMap
    {
        /// <summary>Gaze dwell in the Playground, seconds (Engineering Spec section 4.5).</summary>
        public const float DwellSeconds = 0.7f;
        /// <summary>Degrees of one lever-driven rotation step.</summary>
        public const int RotateStepDegrees = 15;
        /// <summary>First and last dial position.</summary>
        public const int MinQ = 3, MaxQ = 6;

        static readonly PgTwin[] s_table =
        {
            new PgTwin(PgAction.Confirm, "Confirm / next beat", "A", "prompt chip", "click",
                       AtlasAction.Confirm, AtlasAction.None),
            new PgTwin(PgAction.DialStep, "Dial q", "D-pad left/right", "floor dial, four detents", "click cycles 3 to 4 to 5 to 6",
                       AtlasAction.DialLeft, AtlasAction.DialRight),
            new PgTwin(PgAction.Mode, "Mode 1 / Mode 2", "Y", "mode chip", "double click",
                       AtlasAction.Primary, AtlasAction.None),
            new PgTwin(PgAction.ExhibitOpen, "Exhibit menu, pick", "Start, stick + A", "exhibit sign",
                       "long press opens, click steps, long press picks", AtlasAction.SpaceMenu, AtlasAction.Confirm),
            new PgTwin(PgAction.Gluing, "Gluing T2 / Klein / T3", "bumpers", "gluing sign", "click",
                       AtlasAction.VariantPrev, AtlasAction.VariantNext),
            new PgTwin(PgAction.SliceW, "Slice w", "bumpers", "ends of the w-ruler", "hold to sweep",
                       AtlasAction.VariantPrev, AtlasAction.VariantNext, true, AtlasAxis.Sweep),
            new PgTwin(PgAction.Rotate, "Rotate in a w-plane", "triggers (or marker cube pitch)", "floor dial, five detents", "double click steps 15 deg",
                       AtlasAction.None, AtlasAction.None, true, AtlasAxis.Twist),
            new PgTwin(PgAction.Shadow, "Shadow", "X", "look down past -35 deg", null,
                       AtlasAction.Secondary, AtlasAction.None),
            new PgTwin(PgAction.ReturnHome, "Return Home", "B held", "home beacon", "triple click",
                       AtlasAction.ReturnHome, AtlasAction.None),
        };

        /// <summary>The twins table of section 10.1.3, in the plan's order.</summary>
        public static PgTwin[] Table { get { return s_table; } }

        public ActionMap()
        {
            for (int i = 0; i < _live.Length; i++) _live[i] = true;
        }

        readonly bool[] _live = new bool[14];
        int _q = 5;
        bool _menuOpen;
        float _sweepSign = -1f;

        /// <summary>The lever's gesture reader. Fed by <see cref="Tick"/>.</summary>
        public readonly LeverGestures Lever = new LeverGestures();

        /// <summary>What a single click means in a running space. Beats and exhibits set it.</summary>
        public LeverClickRole ClickRole = LeverClickRole.Confirm;
        /// <summary>What a double click means in a running space.</summary>
        public LeverDoubleRole DoubleRole = LeverDoubleRole.None;
        /// <summary>A held lever sweeps w (the Slicer). While true it replaces the long press.</summary>
        public bool HoldSweeps;

        /// <summary>The dial position the lever cycles from. Kept by the caller when the dial moves.</summary>
        public int Q
        {
            get { return _q; }
            set { _q = value < MinQ ? MinQ : value > MaxQ ? MaxQ : value; }
        }

        /// <summary>The exhibit menu is open: stick, A and the lever navigate it.</summary>
        public bool MenuOpen
        {
            get { return _menuOpen; }
            set
            {
                if (_menuOpen == value) return;
                _menuOpen = value;
                Lever.Reset();
            }
        }

        /// <summary>-1, 0 or +1: the w sweep the lever drives while it is held. The Slicer adds it to the sweep axis.</summary>
        public float LeverSweep { get; private set; }

        /// <summary>Raised from <see cref="OnActionFired"/> and <see cref="Tick"/> with every Playground action.</summary>
        public event Action<PgActionEvent> Fired;

        /// <summary>Turns an action off or on. A dropped action fires nothing, from any twin.</summary>
        public void SetLive(PgAction action, bool live)
        {
            int i = (int)action;
            if (i > 0 && i < _live.Length) _live[i] = live;
        }

        public bool IsLive(PgAction action)
        {
            int i = (int)action;
            return i > 0 && i < _live.Length && _live[i];
        }

        /// <summary>Everything on again, roles back to their defaults (a new beat).</summary>
        public void Reset()
        {
            for (int i = 0; i < _live.Length; i++) _live[i] = true;
            ClickRole = LeverClickRole.Confirm;
            DoubleRole = LeverDoubleRole.None;
            HoldSweeps = false;
            LeverSweep = 0f;
            Lever.Reset();
        }

        /// <summary>
        /// One action of the input service (<see cref="IAtlasInput.ActionFired"/>): gamepad, keys and gaze dwell.
        /// The lever's firings are dropped, see the class remarks.
        /// </summary>
        public void OnActionFired(AtlasAction action, InputSource source, int tag)
        {
            if (source == InputSource.Lever || source == InputSource.None) return;
            if (_menuOpen)
            {
                switch (action)
                {
                    case AtlasAction.NavUp: Emit(PgAction.ExhibitStep, source, -1); break;
                    case AtlasAction.NavDown: Emit(PgAction.ExhibitStep, source, 1); break;
                    case AtlasAction.Confirm:
                        // A dwell on a sign picks that sign (its index is the tag); A picks the selected one.
                        Emit(PgAction.ExhibitPick, source, source == InputSource.Gaze ? tag : -1);
                        break;
                    case AtlasAction.SpaceMenu: Emit(PgAction.ExhibitOpen, source, 0); break;
                }
                return;
            }
            switch (action)
            {
                case AtlasAction.Confirm: Emit(PgAction.Confirm, source, 0); break;
                case AtlasAction.DialLeft:
                case AtlasAction.DialRight:
                    // A dwell on a detent names it with the tag (the detent's q); a d-pad press steps.
                    if (source == InputSource.Gaze && tag >= MinQ && tag <= MaxQ) Emit(PgAction.DialTo, source, tag);
                    else Emit(PgAction.DialStep, source, action == AtlasAction.DialLeft ? -1 : 1);
                    break;
                case AtlasAction.Primary: Emit(PgAction.Mode, source, 0); break;
                case AtlasAction.SpaceMenu: Emit(PgAction.ExhibitOpen, source, 0); break;
                case AtlasAction.VariantPrev: Emit(PgAction.Gluing, source, source == InputSource.Gaze ? 0 : -1); break;
                case AtlasAction.VariantNext: Emit(PgAction.Gluing, source, source == InputSource.Gaze ? 0 : 1); break;
                case AtlasAction.Secondary: Emit(PgAction.Shadow, source, 0); break;
                case AtlasAction.ReturnHome: Emit(PgAction.ReturnHome, source, 0); break;
            }
        }

        /// <summary>One frame of the lever: leverDown is the raw touch (or the Editor's mouse button).</summary>
        public void Tick(bool leverDown, float deltaTime)
        {
            ConfigureLever();
            LeverGesture g = Lever.Update(leverDown, deltaTime);
            LeverSweep = Lever.Holding && IsLive(PgAction.SliceW) ? _sweepSign : 0f;
            if (g == LeverGesture.None) return;

            if (g == LeverGesture.HoldBegin)
            {
                // Each hold sweeps the other way, so that a walker can reach both ends with one finger.
                _sweepSign = -_sweepSign;
                LeverSweep = IsLive(PgAction.SliceW) ? _sweepSign : 0f;
                return;
            }
            if (g == LeverGesture.HoldEnd) return;

            if (_menuOpen)
            {
                if (g == LeverGesture.Click) Emit(PgAction.ExhibitStep, InputSource.Lever, 1);
                else if (g == LeverGesture.LongPress) Emit(PgAction.ExhibitPick, InputSource.Lever, -1);
                return;
            }
            switch (g)
            {
                case LeverGesture.Click:
                    switch (ClickRole)
                    {
                        case LeverClickRole.Dial: Emit(PgAction.DialTo, InputSource.Lever, NextQ(_q)); break;
                        case LeverClickRole.Gluing: Emit(PgAction.Gluing, InputSource.Lever, 0); break;
                        default: Emit(PgAction.Confirm, InputSource.Lever, 0); break;
                    }
                    break;
                case LeverGesture.DoubleClick:
                    if (DoubleRole == LeverDoubleRole.Mode) Emit(PgAction.Mode, InputSource.Lever, 0);
                    else if (DoubleRole == LeverDoubleRole.RotateStep) Emit(PgAction.RotateStep, InputSource.Lever, RotateStepDegrees);
                    break;
                case LeverGesture.TripleClick: Emit(PgAction.ReturnHome, InputSource.Lever, 0); break;
                case LeverGesture.LongPress: Emit(PgAction.ExhibitOpen, InputSource.Lever, 0); break;
            }
        }

        /// <summary>The lever cycles 3, 4, 5, 6 and back to 3.</summary>
        public static int NextQ(int q)
        {
            return q >= MaxQ ? MinQ : q < MinQ ? MinQ : q + 1;
        }

        // The context decides which gestures are bound, which decides how long a single click may be held back.
        void ConfigureLever()
        {
            if (_menuOpen)
            {
                Lever.MaxClicks = 1;
                Lever.LongPressBound = true;
                Lever.HoldBound = false;
                return;
            }
            int max = 1;
            if (DoubleRole != LeverDoubleRole.None) max = 2;
            // Return Home is live in every beat by default, so a single click waits MultiClickSeconds (0.30 s) to
            // tell it from a triple click; a beat that does not offer Return Home sets it not live to drop that
            // wait. Provisional, to be tuned on the phone (open item 10).
            if (IsLive(PgAction.ReturnHome)) max = 3;
            Lever.MaxClicks = max;
            Lever.HoldBound = HoldSweeps;
            Lever.LongPressBound = !HoldSweeps && IsLive(PgAction.ExhibitOpen);
        }

        void Emit(PgAction action, InputSource source, int value)
        {
            if (!IsLive(action)) return;
            Action<PgActionEvent> handler = Fired;
            if (handler != null) handler(new PgActionEvent(action, source, value));
        }
    }
}
