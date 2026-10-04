using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Atlas.Input
{
    /// <summary>
    /// What the devices say this frame, as the adapter read it from the action map. Masks hold one bit per
    /// action (<see cref="InputTwins.Bit"/>): set while a control bound to the action is down.
    /// </summary>
    public struct RawInput
    {
        public uint PadDown;
        public uint KeyDown;
        /// <summary>The chord modifier (select) is down.</summary>
        public bool SelectDown;
        public float2 LeftStick;
        public float2 RightStick;
        /// <summary>Right trigger minus left trigger, plus the Editor keys: -1..1.</summary>
        public float Twist;
        /// <summary>A touch is on the screen (the headset lever), or the mouse button in the Editor.</summary>
        public bool PointerDown;
        /// <summary>Screen pixels.</summary>
        public float2 PointerPosition;
        public bool GamepadPresent;
    }

    /// <summary>
    /// The input service without the engine (README F2): turns the raw device state and the head pose of a
    /// frame into the frame's snapshot, i.e. the actions pressed and held, the axes, the gaze focus. Gamepad,
    /// keyboard, gaze dwell and the lever all end in the same <see cref="Fire"/>, which is what makes them twins
    /// (Master Plan section 0.5 rule 5). Events are raised only from <see cref="Tick"/>, after the snapshot is
    /// frozen. Allocates nothing per frame.
    /// </summary>
    public sealed class AtlasInputCore
    {
        private const int MaxContextDepth = 8;
        private const int MaxFiringsPerFrame = 32;
        private const int ActionSlots = InputTwins.ActionCount + 1;

        private struct Firing
        {
            public AtlasAction Action;
            public InputSource Source;
            public int Tag;
            public GazeTargetId Id;
            public Action<GazeTargetId, int> Callback;
        }

        private readonly GazeRegistry _targets = new GazeRegistry();
        private readonly GazeDwell _dwell;

        private readonly InputContext[] _contexts = new InputContext[MaxContextDepth];
        private int _contextDepth;

        private readonly Firing[] _firings = new Firing[MaxFiringsPerFrame];
        private int _firingCount;

        private readonly InputSource[] _lastSource = new InputSource[ActionSlots];
        private readonly int[] _lastTag = new int[ActionSlots];
        private readonly float[] _axes = new float[InputTwins.AxisCount];
        private readonly float[] _navTime = new float[4];
        private readonly float[] _navNext = new float[4];
        private readonly bool[] _navWasDown = new bool[4];

        private uint _pressed;
        private uint _held;
        private uint _prevPad;
        private uint _prevKey;
        private bool _prevPointer;
        private bool _eastWasDown;
        private bool _homeFired;
        private float _eastSeconds;
        private bool _tap;
        private float2 _tapPosition;
        private bool _gamepadPresent;

        // Who asked for what, this frame and the one before: the twin check reports only what is in use.
        private uint _polledNow;
        private uint _polledBefore;
        private uint _polledAxesNow;
        private uint _polledAxesBefore;

        public AtlasInputCore()
        {
            _dwell = new GazeDwell(_targets);
        }

        /// <summary>Raised inside Tick, after the frame's snapshot is frozen: action, source, tag.</summary>
        public event Action<AtlasAction, InputSource, int> ActionFired;

        /// <summary>The gaze targets. Spaces and menus add and remove theirs here.</summary>
        public GazeRegistry Targets => _targets;

        /// <summary>
        /// True for a screen point (pixels) where a press belongs to something other than the headset lever, for
        /// example the Editor's development overlay under a mouse click. A press that begins there fires no
        /// gaze target and no Confirm. Null: no point is blocked. Called once per press, with no allocation.
        /// </summary>
        public Func<float2, bool> PointerBlocked { get; set; }

        /// <summary>The dwell state: focus, progress, hold.</summary>
        public GazeDwell Dwell => _dwell;

        public InputContext Context => _contextDepth > 0 ? _contexts[_contextDepth - 1] : InputContext.InHand;

        public float DwellSeconds
        {
            get => _dwell.DwellSeconds;
            set => _dwell.DwellSeconds = value;
        }

        public bool GamepadPresent => _gamepadPresent;
        public GazeTargetId GazeFocused => _dwell.Focused;
        public float GazeProgress => _dwell.Progress;
        public float3 GazePoint => _dwell.Point;

        /// <summary>Contexts nest; the bottom of the stack is InHand.</summary>
        public void PushContext(InputContext context)
        {
            if (_contextDepth == MaxContextDepth)
            {
                // Deeper than any real flow goes: keep the newest contexts rather than refuse the caller.
                for (int i = 1; i < MaxContextDepth; i++) _contexts[i - 1] = _contexts[i];
                _contextDepth--;
            }
            _contexts[_contextDepth++] = context;
            OnContextChanged();
        }

        /// <summary>Removes the topmost entry of that context. Does nothing when it is not on the stack.</summary>
        public void PopContext(InputContext context)
        {
            for (int i = _contextDepth - 1; i >= 0; i--)
            {
                if (_contexts[i] != context) continue;
                for (int j = i + 1; j < _contextDepth; j++) _contexts[j - 1] = _contexts[j];
                _contextDepth--;
                OnContextChanged();
                return;
            }
        }

        /// <summary>True during the frame in which the action fired, from any twin.</summary>
        public bool Pressed(AtlasAction action)
        {
            uint bit = BitOf(action);
            _polledNow |= bit;
            return (_pressed & bit) != 0;
        }

        /// <summary>True while a control of the action is down, or a Hold target of it is being held.</summary>
        public bool Held(AtlasAction action)
        {
            uint bit = BitOf(action);
            _polledNow |= bit;
            return (_held & bit) != 0;
        }

        public float Axis(AtlasAxis axis)
        {
            int i = (int)axis;
            if (i < 0 || i >= InputTwins.AxisCount) return 0f;
            _polledAxesNow |= 1u << i;
            return _axes[i];
        }

        /// <summary>Source of the action's last firing; None when it never fired.</summary>
        public InputSource LastSource(AtlasAction action)
        {
            int i = (int)action;
            return i > 0 && i < ActionSlots ? _lastSource[i] : InputSource.None;
        }

        public int LastTag(AtlasAction action)
        {
            int i = (int)action;
            return i > 0 && i < ActionSlots ? _lastTag[i] : 0;
        }

        /// <summary>InHand: a touch that began this frame, in screen pixels.</summary>
        public bool TryGetTap(out float2 screenPosition)
        {
            screenPosition = _tapPosition;
            return _tap;
        }

        /// <summary>
        /// Rule 5 at run time: the actions polled with Pressed or Held in the current context, this frame or
        /// the last, that no enabled gaze target can fire. Empty when every action in use has its twin.
        /// </summary>
        public void CopyActionsWithoutGazeTwin(List<AtlasAction> into)
        {
            if (into == null) return;
            into.Clear();
            InputContext context = Context;
            uint polled = (_polledNow | _polledBefore) & InputTwins.ContextMask(context);
            for (int i = 1; i <= InputTwins.ActionCount && polled != 0; i++)
            {
                if ((polled & (1u << i)) == 0) continue;
                var action = (AtlasAction)i;
                AtlasAction alias = InputTwins.Of(action).GazeAlias;
                if (_targets.HasEnabled(action, context)) continue;
                if (alias != AtlasAction.None && _targets.HasEnabled(alias, context)) continue;
                into.Add(action);
            }
        }

        /// <summary>The same for the axes: polled with Axis and driven by no enabled Hold target.</summary>
        public void CopyAxesWithoutGazeTwin(List<AtlasAxis> into)
        {
            if (into == null) return;
            into.Clear();
            InputContext context = Context;
            if (context == InputContext.InHand) return;
            uint polled = _polledAxesNow | _polledAxesBefore;
            for (int i = 0; i < InputTwins.AxisCount; i++)
            {
                if ((polled & (1u << i)) == 0) continue;
                if (!_targets.HasEnabled((AtlasAxis)i, context)) into.Add((AtlasAxis)i);
            }
        }

        /// <summary>
        /// One frame: freezes the snapshot from the raw device state and the gaze dwell, then raises the events.
        /// </summary>
        public void Tick(in RawInput raw, float3 headPosition, quaternion headRotation, float deltaTime)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            InputContext context = Context;
            uint live = InputTwins.ContextMask(context);

            _polledBefore = _polledNow;
            _polledNow = 0;
            _polledAxesBefore = _polledAxesNow;
            _polledAxesNow = 0;
            _pressed = 0;
            _held = 0;
            _tap = false;
            _firingCount = 0;
            _gamepadPresent = raw.GamepadPresent;
            for (int i = 0; i < _axes.Length; i++) _axes[i] = 0f;

            bool pointerBegan = raw.PointerDown && !_prevPointer;
            // The lever is a press that begins off whatever else owns the screen. A press held on from the
            // overlay onto the view is not a new press, so it never becomes the lever.
            bool lever = pointerBegan && !IsBlocked(raw.PointerPosition);

            if (context == InputContext.InHand)
            {
                // In the hand the screen is a touch UI: a touch is a tap with a position and fires no action.
                if (pointerBegan)
                {
                    _tap = true;
                    _tapPosition = raw.PointerPosition;
                }
                // A button held while the phone goes into the headset must not fire on its release.
                _eastWasDown = (raw.PadDown & BitOf(AtlasAction.Back)) != 0;
                _homeFired = true;
                ResetNav();
            }
            else
            {
                Buttons(in raw, live, context, dt);
                Axes(in raw, context);

                if (_dwell.Tick(headPosition, headRotation, dt, context, lever, out GazeFiring g))
                    Fire(g.Action, g.Source, g.Tag, g.Id, g.Callback);

                if (_dwell.TryGetHold(out AtlasAction holdAction, out AtlasAxis holdAxis, out float holdValue))
                {
                    if (holdAction != AtlasAction.None) _held |= BitOf(holdAction);
                    else if (math.abs(holdValue) > math.abs(_axes[(int)holdAxis])) _axes[(int)holdAxis] = math.clamp(holdValue, -1f, 1f);
                }
            }

            _prevPad = raw.PadDown;
            _prevKey = raw.KeyDown;
            _prevPointer = raw.PointerDown;

            Dispatch();
        }

        private bool IsBlocked(float2 point)
        {
            Func<float2, bool> blocked = PointerBlocked;
            return blocked != null && blocked(point);
        }

        private void Buttons(in RawInput raw, uint live, InputContext context, float dt)
        {
            uint padEdge = raw.PadDown & ~_prevPad;
            uint keyEdge = raw.KeyDown & ~_prevKey;

            // Gamepad controls that are one action each.
            const uint simple = (1u << (int)AtlasAction.Confirm) | (1u << (int)AtlasAction.NextSpace)
                              | (1u << (int)AtlasAction.PrevSpace) | (1u << (int)AtlasAction.DialLeft)
                              | (1u << (int)AtlasAction.DialRight) | (1u << (int)AtlasAction.Primary)
                              | (1u << (int)AtlasAction.Secondary) | (1u << (int)AtlasAction.VariantPrev)
                              | (1u << (int)AtlasAction.VariantNext);
            FireMask(padEdge & simple & live, InputSource.Gamepad);

            // buttonEast: a short press is Back, a long hold is ReturnHome.
            bool east = (raw.PadDown & BitOf(AtlasAction.Back)) != 0;
            if (east)
            {
                if (!_eastWasDown)
                {
                    _eastSeconds = 0f;
                    _homeFired = false;
                }
                else
                {
                    _eastSeconds += dt;
                }
                if (!_homeFired && _eastSeconds >= InputTwins.ReturnHomeHoldSeconds && (live & BitOf(AtlasAction.ReturnHome)) != 0)
                {
                    _homeFired = true;
                    Fire(AtlasAction.ReturnHome, InputSource.Gamepad, 0, default, null);
                }
            }
            else if (_eastWasDown && !_homeFired && _eastSeconds < InputTwins.BackTapSeconds && (live & BitOf(AtlasAction.Back)) != 0)
            {
                Fire(AtlasAction.Back, InputSource.Gamepad, 0, default, null);
            }
            _eastWasDown = east;

            // start: the exhibit menu, or with select held the on-device menu (Master Plan section 7.3.2).
            if ((padEdge & BitOf(AtlasAction.SpaceMenu)) != 0)
            {
                AtlasAction menu = raw.SelectDown ? AtlasAction.ParamMenu : AtlasAction.SpaceMenu;
                if ((live & BitOf(menu)) != 0) Fire(menu, InputSource.Gamepad, 0, default, null);
            }

            // Keyboard (Editor): every key is its action, on the press.
            const uint nav = (1u << (int)AtlasAction.NavUp) | (1u << (int)AtlasAction.NavDown)
                           | (1u << (int)AtlasAction.NavLeft) | (1u << (int)AtlasAction.NavRight);
            FireMask(keyEdge & live & ~nav, InputSource.Keyboard);

            // Menu navigation repeats while held: d-pad, left stick past the press point, arrow keys.
            if (context == InputContext.Menu)
            {
                for (int i = 0; i < 4; i++)
                {
                    uint bit = 1u << ((int)AtlasAction.NavUp + i);
                    bool pad = (raw.PadDown & bit) != 0;
                    bool key = (raw.KeyDown & bit) != 0;
                    bool down = pad || key;
                    if (down)
                    {
                        bool fire = false;
                        if (!_navWasDown[i])
                        {
                            _navTime[i] = 0f;
                            _navNext[i] = InputTwins.NavRepeatDelaySeconds;
                            fire = true;
                        }
                        else
                        {
                            _navTime[i] += dt;
                            if (_navTime[i] >= _navNext[i])
                            {
                                _navNext[i] += InputTwins.NavRepeatIntervalSeconds;
                                fire = true;
                            }
                        }
                        if (fire)
                            Fire((AtlasAction)((int)AtlasAction.NavUp + i), pad ? InputSource.Gamepad : InputSource.Keyboard, 0, default, null);
                    }
                    _navWasDown[i] = down;
                }
            }
            else
            {
                ResetNav();
            }

            _held = (raw.PadDown | raw.KeyDown) & live;
            // buttonEast is down for both of its actions; ReturnHome counts as held only once it is past Back.
            if (east && _eastSeconds < InputTwins.BackTapSeconds && (raw.KeyDown & BitOf(AtlasAction.ReturnHome)) == 0)
                _held &= ~BitOf(AtlasAction.ReturnHome);
        }

        private void Axes(in RawInput raw, InputContext context)
        {
            _axes[(int)AtlasAxis.Twist] = math.clamp(raw.Twist, -1f, 1f);
            _axes[(int)AtlasAxis.RightX] = math.clamp(raw.RightStick.x, -1f, 1f);
            _axes[(int)AtlasAxis.RightY] = math.clamp(raw.RightStick.y, -1f, 1f);
            if (context != InputContext.Space) return;

            // In a menu the left stick and the d-pad navigate, and the bumpers have no meaning: these stay silent.
            uint down = raw.PadDown | raw.KeyDown;
            float sweep = 0f;
            if ((down & BitOf(AtlasAction.VariantNext)) != 0) sweep += 1f;
            if ((down & BitOf(AtlasAction.VariantPrev)) != 0) sweep -= 1f;
            _axes[(int)AtlasAxis.Sweep] = sweep;
            _axes[(int)AtlasAxis.LeftX] = math.clamp(raw.LeftStick.x, -1f, 1f);
            _axes[(int)AtlasAxis.LeftY] = math.clamp(raw.LeftStick.y, -1f, 1f);
        }

        private void FireMask(uint mask, InputSource source)
        {
            for (int i = 1; i <= InputTwins.ActionCount && mask != 0; i++)
            {
                uint bit = 1u << i;
                if ((mask & bit) == 0) continue;
                mask &= ~bit;
                Fire((AtlasAction)i, source, 0, default, null);
            }
        }

        // Every twin ends here: the snapshot bit, the source and tag, and one queued event.
        private void Fire(AtlasAction action, InputSource source, int tag, GazeTargetId id, Action<GazeTargetId, int> callback)
        {
            int i = (int)action;
            if (i > 0 && i < ActionSlots)
            {
                _pressed |= 1u << i;
                _lastSource[i] = source;
                _lastTag[i] = tag;
            }
            else if (callback == null)
            {
                return;
            }
            if (_firingCount == MaxFiringsPerFrame) return;
            ref Firing f = ref _firings[_firingCount++];
            f.Action = action;
            f.Source = source;
            f.Tag = tag;
            f.Id = id;
            f.Callback = callback;
        }

        // Raised after the snapshot is complete, so a handler sees the whole frame and may register or remove
        // gaze targets, or change the context, without disturbing it.
        private void Dispatch()
        {
            int count = _firingCount;
            _firingCount = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    Firing f = _firings[i];
                    f.Callback?.Invoke(f.Id, f.Tag);
                    if (f.Action != AtlasAction.None) ActionFired?.Invoke(f.Action, f.Source, f.Tag);
                }
            }
            finally
            {
                for (int i = 0; i < count; i++) _firings[i].Callback = null;
            }
        }

        private void OnContextChanged()
        {
            // A dwell or a hold that began in one context must not complete in another.
            _dwell.Reset();
            _polledNow = 0;
            _polledBefore = 0;
            _polledAxesNow = 0;
            _polledAxesBefore = 0;
            // A buttonEast press that straddles the change is spent: it fires neither Back nor ReturnHome.
            if (_eastWasDown) _homeFired = true;
            ResetNav();
        }

        private void ResetNav()
        {
            for (int i = 0; i < 4; i++) _navWasDown[i] = false;
        }

        private static uint BitOf(AtlasAction action)
        {
            int i = (int)action;
            return i > 0 && i < ActionSlots ? 1u << i : 0u;
        }
    }
}
