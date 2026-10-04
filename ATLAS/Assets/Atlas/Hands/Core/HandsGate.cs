using Atlas.World;

namespace Atlas.Hands
{
    /// <summary>Why the gate is not active: the first row of the table in section 3.5 that does not hold. None while it is active.</summary>
    public enum HandsOffReason : byte
    {
        None = 0,
        /// <summary>The display is not the headset: in the hand, the walker's hands hold the phone (H1).</summary>
        Mode = 1,
        /// <summary>A gamepad is connected (H3).</summary>
        Pad = 2,
        /// <summary>hands.enabled is off: the kill switch.</summary>
        Param = 3,
        /// <summary>The hands source is not available: no LiDAR, the Station, the Editor without hands.editor.</summary>
        Source = 4,
        /// <summary>The head does not come from the AR camera, so the camera no longer matches the view.</summary>
        Pose = 5,
        /// <summary>The quality tier is Serious or Critical (H12).</summary>
        Heat = 6
    }

    /// <summary>What sets the detector's rate: the tier, the self-governor, the idle stop, or the bench's override.</summary>
    public enum HandsRateWhy : byte
    {
        Tier = 0,
        Governor = 1,
        Idle = 2,
        Override = 3
    }

    /// <summary>Everything the gate reads in a frame, gathered by the sense step.</summary>
    public struct GateInputs
    {
        /// <summary>The display mode is Headset (IStereoRig.Mode).</summary>
        public bool Headset;
        /// <summary>A gamepad is connected right now (IAtlasInput.GamepadPresent): the gate holds it before it counts.</summary>
        public bool GamepadPresent;
        /// <summary>hands.enabled.</summary>
        public bool Enabled;
        /// <summary>IHandsSource.Available; in the Editor only while hands.editor is on.</summary>
        public bool SourceAvailable;
        /// <summary>The head comes from the AR camera (IPosePipeline.Source.DrivesRig); true for the Editor's own stand-in.</summary>
        public bool HeadFromCamera;
        /// <summary>FrameContext.Tier.</summary>
        public QualityTier Tier;
        /// <summary>The hands.rate parameter: 0 is auto, else 30 or 15.</summary>
        public int RateOverride;
        /// <summary>How long the newest detector result's Vision request took, seconds (HandsDetection.RequestMs / 1000).</summary>
        public float RequestSeconds;
        /// <summary>The hands' own time on the main thread, milliseconds per frame.</summary>
        public float MainThreadMs;
        /// <summary>A hand is tracked this frame, on either side.</summary>
        public bool AnyHandSeen;
    }

    /// <summary>The gate's answer for one frame.</summary>
    public struct GateState
    {
        /// <summary>The detector is running (IHands.Active): every row of section 3.5 holds.</summary>
        public bool Active;
        /// <summary>Detections per second to ask of the source: 30, 15 or 5; 0 while not active.</summary>
        public int RateHz;
        /// <summary>None while active, else the first row that failed.</summary>
        public HandsOffReason Reason;
        /// <summary>What decided <see cref="RateHz"/>. Tier while not active.</summary>
        public HandsRateWhy RateWhy;
        /// <summary>Active is not what it was in the previous update: the frame hands.on or hands.off is logged.</summary>
        public bool ActiveChanged;
        /// <summary>
        /// RateHz is not what it was in the previous update. That includes the update in which the gate turns
        /// on (from 0) or off (to 0), so a caller that sets the source's rate whenever this is true needs
        /// nothing else; hands.rate is for the updates where this is true, Active is true and
        /// <see cref="ActiveChanged"/> is false.
        /// </summary>
        public bool RateChanged;

        /// <summary>
        /// Panels belong within reach (IReachLayout.Near): every row of the table holds except the tier's. When
        /// hands stop for heat the panels stay where they are and work by gaze, so this is true for a Heat
        /// reason as well as while active. The tier and Bump Guard's level never move a panel.
        /// </summary>
        public bool Near
        {
            get { return Active || Reason == HandsOffReason.Heat; }
        }
    }

    /// <summary>
    /// When hands run, and how fast (documents/ATLAS_Hands_Design.md sections 3.5 and 7.3, decision R8). The
    /// detector is active only while the display is the headset, no gamepad has been connected for a while,
    /// hands are enabled, a source is available, the head comes from the AR camera and the tier is Nominal,
    /// High or Fair. Evaluated every frame by the sense step. Engine-free and allocation-free.
    ///
    /// The rate is the override when there is one; otherwise <see cref="HandsConstants.RateFull"/> at Nominal and
    /// High and <see cref="HandsConstants.RateReduced"/> at Fair. Two guards sit under it. The self-governor drops
    /// a full rate to the reduced one when the detector's requests stay longer than <see cref="HandsConstants.GovernorRequestSeconds"/>
    /// or the hands' main-thread time stays over <see cref="HandsConstants.GovernorMainThreadMs"/>, each for
    /// <see cref="HandsConstants.GovernorSeconds"/> (the two causes keep their own clocks), and stays down until
    /// the gate next goes inactive. The idle stop drops to <see cref="HandsConstants.RateIdle"/> after
    /// <see cref="HandsConstants.IdleAfterSeconds"/> with no hand seen, and the first hand brings the rate back.
    /// </summary>
    public sealed class HandsGate
    {
        GateState _current;
        /// <summary>The gamepad has been looked at once: there is a value to hold changes against.</summary>
        bool _padSeen;
        /// <summary>The gamepad as the gate counts it: a change counts after it has held.</summary>
        bool _pad;
        /// <summary>The raw gamepad differs from <see cref="_pad"/> and is waiting out its hold.</summary>
        bool _padPending;
        /// <summary>App time at which the raw gamepad began to differ.</summary>
        double _padSince;
        bool _governed;
        /// <summary>Seconds each of the two governor causes has held without a break.</summary>
        double _slowResults, _slowMainThread;
        /// <summary>Seconds, while active, with no hand seen.</summary>
        double _noHand;

        /// <summary>A gate that has not yet looked at anything: not active.</summary>
        public HandsGate()
        {
            Reset();
        }

        /// <summary>Back to the start: not active, no gamepad remembered, the governor and the idle clock cleared.</summary>
        public void Reset()
        {
            _current = default(GateState);
            _padSeen = false;
            _pad = false;
            _padPending = false;
            _padSince = 0.0;
            _governed = false;
            _slowResults = 0.0;
            _slowMainThread = 0.0;
            _noHand = 0.0;
        }

        /// <summary>
        /// One frame. now is the app clock, which times the gamepad's hold; deltaTime sums the governor's and
        /// the idle clock's runs (a negative or unreadable one is no time). The answer is also
        /// <see cref="Current"/> until the next call.
        /// </summary>
        public GateState Update(double now, float deltaTime, in GateInputs inputs)
        {
            double dt = deltaTime > 0f ? deltaTime : 0.0;

            FollowGamepad(now, inputs.GamepadPresent);

            HandsOffReason reason = FirstFailedRow(in inputs);
            bool active = reason == HandsOffReason.None;
            int rate = 0;
            HandsRateWhy why = HandsRateWhy.Tier;

            if (active)
            {
                rate = ChooseRate(dt, in inputs, out why);
            }
            else
            {
                // Nothing runs, so nothing is timed: the governor and the idle clock start with the gate.
                _governed = false;
                _slowResults = 0.0;
                _slowMainThread = 0.0;
                _noHand = 0.0;
            }

            GateState next;
            next.Active = active;
            next.RateHz = rate;
            next.Reason = reason;
            next.RateWhy = why;
            next.ActiveChanged = active != _current.Active;
            next.RateChanged = rate != _current.RateHz;
            _current = next;
            return next;
        }

        /// <summary>What the last <see cref="Update"/> answered. Inactive before the first.</summary>
        public GateState Current
        {
            get { return _current; }
        }

        /// <summary>
        /// A change of the gamepad counts after it has held for <see cref="HandsConstants.GamepadHoldSeconds"/>,
        /// whichever way it went; the first look has nothing to hold against and counts at once.
        /// </summary>
        void FollowGamepad(double now, bool raw)
        {
            if (!_padSeen)
            {
                _pad = raw;
                _padPending = false;
                _padSeen = true;
                return;
            }
            if (raw == _pad)
            {
                _padPending = false;
                return;
            }
            if (!_padPending)
            {
                _padPending = true;
                _padSince = now;
            }
            if (now - _padSince >= HandsConstants.GamepadHoldSeconds)
            {
                _pad = raw;
                _padPending = false;
            }
        }

        /// <summary>The rows of the table in section 3.5, in its order.</summary>
        HandsOffReason FirstFailedRow(in GateInputs inputs)
        {
            if (!inputs.Headset) return HandsOffReason.Mode;
            if (_pad) return HandsOffReason.Pad;
            if (!inputs.Enabled) return HandsOffReason.Param;
            if (!inputs.SourceAvailable) return HandsOffReason.Source;
            if (!inputs.HeadFromCamera) return HandsOffReason.Pose;
            QualityTier tier = inputs.Tier;
            if (tier != QualityTier.Nominal && tier != QualityTier.High && tier != QualityTier.Fair) return HandsOffReason.Heat;
            return HandsOffReason.None;
        }

        int ChooseRate(double dt, in GateInputs inputs, out HandsRateWhy why)
        {
            // The idle clock: how long the detector has run without a hand in view.
            _noHand = inputs.AnyHandSeen ? 0.0 : _noHand + dt;
            bool idle = _noHand >= HandsConstants.IdleAfterSeconds;

            int tierRate = inputs.Tier == QualityTier.Fair ? HandsConstants.RateReduced : HandsConstants.RateFull;
            int overrideRate = OverrideRate(inputs.RateOverride);

            // The self-governor watches the full rate, the one it can drop, and only when nothing else sets it.
            if (overrideRate == 0 && tierRate == HandsConstants.RateFull && !idle)
            {
                _slowResults = inputs.RequestSeconds > HandsConstants.GovernorRequestSeconds ? _slowResults + dt : 0.0;
                _slowMainThread = inputs.MainThreadMs > HandsConstants.GovernorMainThreadMs ? _slowMainThread + dt : 0.0;
                if (_slowResults >= HandsConstants.GovernorSeconds || _slowMainThread >= HandsConstants.GovernorSeconds)
                    _governed = true;
            }
            else
            {
                _slowResults = 0.0;
                _slowMainThread = 0.0;
            }

            if (overrideRate != 0)
            {
                why = HandsRateWhy.Override;
                return overrideRate;
            }
            if (idle)
            {
                why = HandsRateWhy.Idle;
                return HandsConstants.RateIdle;
            }
            if (_governed && tierRate > HandsConstants.RateReduced)
            {
                why = HandsRateWhy.Governor;
                return HandsConstants.RateReduced;
            }
            why = HandsRateWhy.Tier;
            return tierRate;
        }

        /// <summary>0 is auto. A positive value is 30 or 15, or the nearer of the two; zero or less is auto.</summary>
        static int OverrideRate(int requested)
        {
            if (requested <= 0) return 0;
            return requested > (HandsConstants.RateFull + HandsConstants.RateReduced) / 2
                ? HandsConstants.RateFull
                : HandsConstants.RateReduced;
        }
    }
}
