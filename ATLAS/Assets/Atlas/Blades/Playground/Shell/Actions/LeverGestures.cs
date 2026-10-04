namespace Atlas.Blades.Playground
{
    /// <summary>What the headset lever did: one touch of the screen, read as a gesture.</summary>
    public enum LeverGesture : byte
    {
        None = 0,
        Click = 1,
        DoubleClick = 2,
        TripleClick = 3,
        /// <summary>Held past <see cref="LeverGestures.LongPressSeconds"/>; fires while still down.</summary>
        LongPress = 4,
        /// <summary>Held past <see cref="LeverGestures.HoldDelaySeconds"/> where a hold is bound: a sweep begins.</summary>
        HoldBegin = 5,
        HoldEnd = 6
    }

    /// <summary>
    /// Turns the lever's down and up into clicks, double and triple clicks, long presses and holds (Master Plan
    /// section 10.1.3, twins table). The timings are provisional: the plan marks the lever's behaviour as open
    /// item 10 and they are to be tuned on the phone in the headset. Engine-free; allocates nothing.
    /// <para>
    /// A click is reported on release, and at once when no longer sequence is bound; with a double or triple
    /// click bound, it waits <see cref="MultiClickSeconds"/> for the next touch. A long press is reported while the
    /// lever is still down, and the release after it is silent.
    /// </para>
    /// </summary>
    public sealed class LeverGestures
    {
        /// <summary>Longest gap between two touches of one double or triple click, seconds (provisional).</summary>
        public float MultiClickSeconds = 0.30f;
        /// <summary>A touch held this long is a long press, seconds (provisional).</summary>
        public const float LongPressSeconds = 0.60f;
        /// <summary>Where a hold is bound, a touch held this long starts the sweep, seconds (provisional).</summary>
        public const float HoldDelaySeconds = 0.25f;

        /// <summary>The longest click sequence the context binds: 1, 2 or 3. Set by the action map each frame.</summary>
        public int MaxClicks = 1;
        /// <summary>A long press means something now.</summary>
        public bool LongPressBound;
        /// <summary>A hold means something now (it then replaces the long press).</summary>
        public bool HoldBound;

        bool _down;
        float _downSeconds;
        bool _longFired;
        /// <summary>The touch in progress was cut short by <see cref="Reset"/>: it makes no gesture, ever.</summary>
        bool _spent;
        bool _holdFired;
        int _clicks;
        float _gap;

        /// <summary>True from HoldBegin until HoldEnd.</summary>
        public bool Holding { get { return _holdFired && _down && !_spent; } }

        /// <summary>Forgets a touch in progress (the context changed: a touch that began in a menu must not
        /// finish as a click in the space).</summary>
        public void Reset()
        {
            // A finger still down stays down and spent: seen as a new touch on the next frame it would make a
            // second gesture (a long press that opens the menu would pick from it 0.6 s later).
            _spent = _down;
            _downSeconds = 0f;
            _longFired = false;
            _holdFired = false;
            _clicks = 0;
            _gap = 0f;
        }

        /// <summary>One frame. down is the lever now; at most one gesture is returned.</summary>
        public LeverGesture Update(bool down, float deltaTime)
        {
            float dt = deltaTime > 0f ? deltaTime : 0f;
            int max = MaxClicks < 1 ? 1 : MaxClicks > 3 ? 3 : MaxClicks;

            if (down)
            {
                if (_spent) return LeverGesture.None;
                if (!_down)
                {
                    _down = true;
                    _downSeconds = 0f;
                    _longFired = false;
                    _holdFired = false;
                    _gap = 0f;
                    return LeverGesture.None;
                }
                _downSeconds += dt;
                if (HoldBound)
                {
                    if (!_holdFired && _downSeconds >= HoldDelaySeconds)
                    {
                        _holdFired = true;
                        _clicks = 0;
                        return LeverGesture.HoldBegin;
                    }
                }
                else if (LongPressBound && !_longFired && _downSeconds >= LongPressSeconds)
                {
                    _longFired = true;
                    _clicks = 0;
                    return LeverGesture.LongPress;
                }
                return LeverGesture.None;
            }

            if (_spent)
            {
                // The release of a touch that Reset cut short is silent.
                _spent = false;
                _down = false;
                return LeverGesture.None;
            }
            if (_down)
            {
                _down = false;
                if (_holdFired)
                {
                    _holdFired = false;
                    return LeverGesture.HoldEnd;
                }
                if (_longFired)
                {
                    _longFired = false;
                    return LeverGesture.None;
                }
                _clicks++;
                _gap = 0f;
                if (_clicks >= max) return Emit();
                return LeverGesture.None;
            }

            if (_clicks > 0)
            {
                _gap += dt;
                if (_gap >= MultiClickSeconds) return Emit();
            }
            return LeverGesture.None;
        }

        LeverGesture Emit()
        {
            int n = _clicks;
            _clicks = 0;
            _gap = 0f;
            return n >= 3 ? LeverGesture.TripleClick : n == 2 ? LeverGesture.DoubleClick : LeverGesture.Click;
        }
    }
}
