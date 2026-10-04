using Unity.Mathematics;

namespace Atlas.Safety
{
    /// <summary>Who asks for a cue: Bump Guard itself, or the operator's STOP command.</summary>
    public enum CueSource : byte { Guard = 0, Operator = 1 }

    /// <summary>Which indicator is drawn. One at a time.</summary>
    public enum CueIcon : byte { None = 0, ChevronWarn = 1, ChevronStrong = 2, StandStill = 3, Rescan = 4 }

    /// <summary>Which sound a pulse plays.</summary>
    public enum CueSound : byte { None = 0, Tick = 1, Tone = 2 }

    /// <summary>
    /// Decides what the warning cue plays and when (Master Plan section 4.10, outputs table; task P1-C14).
    /// No engine type and no clock: time comes in through <see cref="Advance"/>, so the pulse train can be
    /// tested headless. It merges the two sources (Bump Guard and the operator), keeps the pulse rhythm
    /// (2 per second for WARN and for the rescan icon, 6 per second for STRONG and tracking lost) and places
    /// the indicator and the sound. A cue starts in the call that raises it: the first pulse is pending as soon
    /// as <see cref="Fire"/> returns. Allocates nothing.
    /// </summary>
    public sealed class BumpCueScheduler
    {
        /// <summary>The indicators sit this far ahead of the head, metres.</summary>
        public const float IndicatorDistance = 1.0f;
        /// <summary>Radius of the circle the chevron sits on, metres at <see cref="IndicatorDistance"/> (about 17 degrees).</summary>
        public const float ChevronRadius = 0.30f;
        /// <summary>The chevron is this much larger at STRONG.</summary>
        public const float StrongScale = 1.6f;
        /// <summary>Depth of the 6 Hz size pulse of the STRONG chevron, as a fraction of its size.</summary>
        public const float StrongPulseDepth = 0.2f;
        /// <summary>The rescan icon sits this far below the view centre, metres at <see cref="IndicatorDistance"/>.</summary>
        public const float RescanDrop = 0.18f;
        /// <summary>Strength of a WARN pulse, 0..1 (a medium impact).</summary>
        public const float WarnStrength = 0.6f;
        /// <summary>Strength of a STRONG or tracking-lost pulse, 0..1 (a heavy impact).</summary>
        public const float StrongStrength = 1.0f;

        // A pulse is due when the period has passed, less this: frame times do not land exactly on the period.
        const double PulseSlack = 1e-4;

        BumpLevel _guardLevel;
        float2 _guardDirection;
        BumpLevel _operatorLevel;
        float2 _operatorDirection;
        double _operatorUntil;
        bool _operatorStampPending;
        bool _rescanAdvised;

        double _time;
        double _lastPulse;
        bool _restartRhythm;
        bool _pulsePending;
        double _strongSince;

        /// <summary>What the walker gets now: the higher of the two sources.</summary>
        public BumpLevel Shown { get; private set; }
        /// <summary>Unit vector towards the hazard in head space (x right, y ahead); zero when there is no side.</summary>
        public float2 Direction { get; private set; }
        /// <summary>The rescan icon is up: a rescan is advised and no level is shown.</summary>
        public bool RescanShown { get; private set; }
        /// <summary>Rises with every rise of <see cref="Shown"/>.</summary>
        public int FireCount { get; private set; }
        /// <summary>The pulse rhythm that is running.</summary>
        public HapticPattern Pattern { get; private set; }
        /// <summary>Pulses emitted since the last <see cref="Reset"/>.</summary>
        public int PulseCount { get; private set; }

        /// <summary>The indicator to draw.</summary>
        public CueIcon Icon { get; private set; }
        /// <summary>Where the indicator sits, in the head-locked parent's space, metres.</summary>
        public float3 IndicatorPosition { get; private set; }
        /// <summary>Rotation of the indicator about the view axis, radians; turning +Y towards +X is negative.</summary>
        public float IndicatorRoll { get; private set; }
        /// <summary>Uniform scale of the indicator.</summary>
        public float IndicatorScale { get; private set; } = 1f;
        /// <summary>Where the sound comes from, in the head-locked parent's space: 1 m from the head towards the hazard.</summary>
        public float3 SoundPosition { get; private set; }
        /// <summary>The sound has a side. False for tracking lost, the rescan icon and a cue with no direction.</summary>
        public bool SoundSpatial { get; private set; }

        /// <summary>Back to silence: no level from either source, no pulse pending, counters at zero.</summary>
        public void Reset()
        {
            _guardLevel = BumpLevel.Clear;
            _guardDirection = float2.zero;
            _operatorLevel = BumpLevel.Clear;
            _operatorDirection = float2.zero;
            _operatorUntil = 0.0;
            _operatorStampPending = false;
            _rescanAdvised = false;
            _time = 0.0;
            _lastPulse = 0.0;
            _restartRhythm = false;
            _pulsePending = false;
            _strongSince = 0.0;
            Shown = BumpLevel.Clear;
            Direction = float2.zero;
            RescanShown = false;
            FireCount = 0;
            Pattern = HapticPattern.Off;
            PulseCount = 0;
            UpdateIndicator();
        }

        /// <summary>
        /// Sets the level one source asks for. Guard: called every frame with Bump Guard's level, Clear included.
        /// Operator: held for <see cref="BumpConstants.OperatorHoldSeconds"/>, then dropped; Clear withdraws it.
        /// </summary>
        public void Fire(BumpLevel level, float2 direction, CueSource source)
        {
            float2 unit = math.normalizesafe(direction);
            if (source == CueSource.Operator)
            {
                _operatorLevel = level;
                _operatorDirection = unit;
                // The hold runs from the next Advance: this class reads no clock of its own.
                _operatorStampPending = level != BumpLevel.Clear;
            }
            else
            {
                _guardLevel = level;
                _guardDirection = unit;
            }
            Resolve();
        }

        /// <summary>Whether a rescan is advised. The icon shows only while no level is shown.</summary>
        public void Rescan(bool advised)
        {
            _rescanAdvised = advised;
            Resolve();
        }

        /// <summary>Once per frame, after Fire: ends an operator hold, keeps the pulse rhythm, moves the indicator.</summary>
        public void Advance(double time, float deltaTime)
        {
            if (_operatorStampPending)
            {
                _operatorUntil = time + BumpConstants.OperatorHoldSeconds;
                _operatorStampPending = false;
            }
            else if (_operatorLevel != BumpLevel.Clear && time >= _operatorUntil)
            {
                _operatorLevel = BumpLevel.Clear;
            }

            _time = time;
            Resolve();

            if (Pattern == HapticPattern.Off)
            {
                _restartRhythm = false;
            }
            else if (_restartRhythm)
            {
                // The cue started since the last frame and its first pulse has gone out: the rhythm runs from here.
                _lastPulse = time;
                _restartRhythm = false;
            }
            else
            {
                double period = 1.0 / (Pattern == HapticPattern.Strong ? BumpConstants.StrongPulseHz : BumpConstants.WarnPulseHz);
                if (time - _lastPulse >= period - PulseSlack)
                {
                    _pulsePending = true;
                    _lastPulse += period;
                    // After a long frame: one pulse, not a burst to catch up.
                    if (time - _lastPulse >= period - PulseSlack) _lastPulse = time;
                }
            }
            UpdateIndicator();
        }

        /// <summary>
        /// True once for every pulse that is due: the caller plays the haptic pulse and the sound now.
        /// </summary>
        public bool TakePulse(out float strength, out CueSound sound)
        {
            strength = 0f;
            sound = CueSound.None;
            if (!_pulsePending) return false;
            _pulsePending = false;
            if (Pattern == HapticPattern.Off) return false;
            bool strong = Pattern == HapticPattern.Strong;
            strength = strong ? StrongStrength : WarnStrength;
            sound = strong ? CueSound.Tone : CueSound.Tick;
            PulseCount++;
            return true;
        }

        // Merges the two sources and starts a cue at once when the result rises.
        void Resolve()
        {
            bool guardLeads = _guardLevel >= _operatorLevel;       // on a tie the Guard's direction is used
            BumpLevel shown = guardLeads ? _guardLevel : _operatorLevel;
            float2 direction = shown == BumpLevel.Clear ? float2.zero : (guardLeads ? _guardDirection : _operatorDirection);
            bool rescan = _rescanAdvised && shown == BumpLevel.Clear;

            HapticPattern pattern;
            if (shown == BumpLevel.Strong || shown == BumpLevel.TrackingLost) pattern = HapticPattern.Strong;
            else if (shown == BumpLevel.Warn || rescan) pattern = HapticPattern.Warn;
            else pattern = HapticPattern.Off;

            bool rose = shown > Shown;
            bool started = pattern != HapticPattern.Off && Pattern == HapticPattern.Off;
            if (rose) FireCount++;
            if (rose || started)
            {
                _pulsePending = true;
                _restartRhythm = true;
            }
            if (pattern == HapticPattern.Off) _pulsePending = false;
            if (shown == BumpLevel.Strong && Shown != BumpLevel.Strong) _strongSince = _time;

            Shown = shown;
            Direction = direction;
            RescanShown = rescan;
            Pattern = pattern;
            UpdateIndicator();
        }

        void UpdateIndicator()
        {
            float2 d = Direction;
            bool hasSide = math.lengthsq(d) > 0f;
            SoundSpatial = false;
            SoundPosition = new float3(0f, 0f, IndicatorDistance);
            IndicatorRoll = 0f;
            IndicatorScale = 1f;

            switch (Shown)
            {
                case BumpLevel.TrackingLost:
                    Icon = CueIcon.StandStill;
                    IndicatorPosition = new float3(0f, 0f, IndicatorDistance);
                    return;
                case BumpLevel.Warn:
                case BumpLevel.Strong:
                    // Bearing 0 (ahead) is the top of the circle, +90 degrees the right, 180 the bottom. The
                    // chevron points outwards. With no direction it sits at the top.
                    float bearing = hasSide ? math.atan2(d.x, d.y) : 0f;
                    math.sincos(bearing, out float s, out float c);
                    IndicatorPosition = new float3(s * ChevronRadius, c * ChevronRadius, IndicatorDistance);
                    IndicatorRoll = -bearing;
                    if (Shown == BumpLevel.Strong)
                    {
                        Icon = CueIcon.ChevronStrong;
                        double phase = (_time - _strongSince) * BumpConstants.StrongPulseHz;
                        phase -= math.floor(phase);
                        IndicatorScale = StrongScale * (1f + StrongPulseDepth * math.sin((float)(phase * 2.0 * math.PI_DBL)));
                    }
                    else
                    {
                        Icon = CueIcon.ChevronWarn;
                    }
                    if (hasSide)
                    {
                        SoundSpatial = true;
                        SoundPosition = new float3(d.x, 0f, d.y);
                    }
                    return;
                default:
                    if (RescanShown)
                    {
                        Icon = CueIcon.Rescan;
                        IndicatorPosition = new float3(0f, -RescanDrop, IndicatorDistance);
                    }
                    else
                    {
                        Icon = CueIcon.None;
                        IndicatorPosition = new float3(0f, 0f, IndicatorDistance);
                    }
                    return;
            }
        }
    }
}
