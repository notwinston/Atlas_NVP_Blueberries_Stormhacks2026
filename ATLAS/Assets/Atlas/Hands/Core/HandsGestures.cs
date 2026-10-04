using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>One hand's pinch in one frame (documents/ATLAS_Hands_Design.md section 6.3).</summary>
    public struct PinchState
    {
        /// <summary>
        /// 0 open to 1 closed: the thumb-to-index gap over the palm's width, mapped linearly from
        /// <see cref="HandsConstants.PinchOpen"/> (0) to <see cref="HandsConstants.PinchClose"/> (1).
        /// </summary>
        public float Amount;
        /// <summary>
        /// The fingers are closed, after hysteresis. It follows the hand's own measure and nothing else, so it
        /// is also true for a pinch that <see cref="Down"/> held back (a hand that came into view already
        /// closed, say): Down and Up are the edges that may fire an action, this is where the fingers are.
        /// </summary>
        public bool Pinching;
        /// <summary>
        /// True for the one frame in which a pinch closed and none of the cases of section 6.3 held it back
        /// (the index fingertip hovering a target, the hand's first moments, a fast hand, a pinch just after
        /// this hand's last, low confidence, the other hand already pinching).
        /// </summary>
        public bool Down;
        /// <summary>
        /// True for the one frame in which a pinch whose <see cref="Down"/> fired opens, or its hand is no
        /// longer tracked. A pinch that was held back has no Up either.
        /// </summary>
        public bool Up;
        /// <summary>World, metres: the midpoint of the thumb tip and the index tip. Keeps its last value while the hand is not tracked.</summary>
        public float3 Point;
    }

    /// <summary>
    /// Pinch and two-hand recognition (documents/ATLAS_Hands_Design.md sections 6.3 and 6.4). Engine-free: it is
    /// given the tracked hands and the time, reads no clock and allocates nothing (README F2).
    ///
    /// A pinch closes when the hand's pinch ratio is under <see cref="HandsConstants.PinchClose"/> in
    /// <see cref="HandsConstants.PinchDetections"/> detections in a row (frames that carry a detection of this
    /// hand: held frames repeat the last one and do not count) and opens when it is over
    /// <see cref="HandsConstants.PinchOpen"/>. The closing is also a Down that may fire something, unless it is
    /// held back; a pinch held back fires nothing, not even later when the reason has gone: the hand has to
    /// open and close again.
    ///
    /// Two hands pinching at once are a two-hand pinch. Nothing is bound to it; it is recognised and reported.
    /// </summary>
    public sealed class HandsGestures
    {
        const int Sides = 2;
        /// <summary>
        /// The line between the two pinch points has an angle only when its length in the plane of the view is
        /// at least this, metres. Under it, both points are on one spot or one is behind the other along the
        /// view axis, and the angle keeps its value.
        /// </summary>
        internal const float MinLineMetres = 0.002f;

        readonly PinchState[] _pinch = new PinchState[Sides];
        /// <summary>Detections in a row under the close threshold, never counted past what closes a pinch.</summary>
        readonly int[] _closeRun = new int[Sides];
        /// <summary>App time of each hand's last Down that fired.</summary>
        readonly double[] _lastDown = new double[Sides];
        /// <summary>The pinch that is closed now fired its Down, so its Up will fire.</summary>
        readonly bool[] _fired = new bool[Sides];

        TwoHandPinch _two;
        bool _twoStarted, _twoEnded;
        float _twoDistanceChange, _twoAngleChange;
        /// <summary>The angle of the line as the last frame read it, in (-pi, pi], and the same unwrapped.</summary>
        float _rawAngle, _angle;
        /// <summary>The line's length and angle in the first and the last frame of the two-hand pinch.</summary>
        float _startDistance, _startAngle, _lastDistance, _lastAngle;

        /// <summary>Both hands open, nothing pending.</summary>
        public HandsGestures()
        {
            Reset();
        }

        /// <summary>Forgets everything: both pinches open, no two-hand pinch, no last Down.</summary>
        public void Reset()
        {
            for (int i = 0; i < Sides; i++)
            {
                _pinch[i] = default(PinchState);
                _closeRun[i] = 0;
                _lastDown[i] = double.NegativeInfinity;
                _fired[i] = false;
            }
            _two = default(TwoHandPinch);
            _twoStarted = false;
            _twoEnded = false;
            _twoDistanceChange = 0f;
            _twoAngleChange = 0f;
            _rawAngle = 0f;
            _angle = 0f;
            _startDistance = 0f;
            _startAngle = 0f;
            _lastDistance = 0f;
            _lastAngle = 0f;
        }

        /// <summary>
        /// One frame. now is the app clock. leftHovering and rightHovering say that the hand's index fingertip is
        /// hovering a target (input's touch state of the last tick): a hand that is touching does not pinch.
        /// headRotation turns the two-hand pinch's line into an angle about the head's forward axis.
        /// </summary>
        public void Update(double now, in TrackedHand left, in TrackedHand right, quaternion headRotation,
                           bool leftHovering, bool rightHovering)
        {
            bool leftClosed, leftOpened, leftHeld, rightClosed, rightOpened, rightHeld;
            Follow(0, now, in left, leftHovering, out leftClosed, out leftOpened, out leftHeld);
            Follow(1, now, in right, rightHovering, out rightClosed, out rightOpened, out rightHeld);

            // The first hand's pinch fires; a second hand closing on top of it, or on the very same detection,
            // makes a two-hand pinch and fires nothing (section 6.4). Both postures are known by now.
            bool bothPinching = _pinch[0].Pinching && _pinch[1].Pinching;
            Settle(0, now, leftClosed, leftOpened, leftHeld || bothPinching);
            Settle(1, now, rightClosed, rightOpened, rightHeld || bothPinching);

            FollowTwoHands(headRotation);
        }

        /// <summary>One hand's pinch in the current frame. Valid from Update until the next.</summary>
        public ref readonly PinchState Pinch(HandSide side)
        {
            return ref _pinch[side == HandSide.Right ? 1 : 0];
        }

        /// <summary>Both hands pinching: how far apart, at what angle, where. Everything is 0 while Active is false.</summary>
        public ref readonly TwoHandPinch TwoHand
        {
            get { return ref _two; }
        }

        /// <summary>True for the one frame in which the two-hand pinch starts.</summary>
        public bool TwoHandStarted
        {
            get { return _twoStarted; }
        }

        /// <summary>True for the one frame in which the two-hand pinch ends: a hand opened or was lost.</summary>
        public bool TwoHandEnded
        {
            get { return _twoEnded; }
        }

        /// <summary>Metres: the distance in the last frame of the two-hand pinch minus the distance in its first. Valid while <see cref="TwoHandEnded"/>, else 0.</summary>
        public float TwoHandDistanceChange
        {
            get { return _twoDistanceChange; }
        }

        /// <summary>Radians: the unwrapped angle in the last frame of the two-hand pinch minus the angle in its first. Valid while <see cref="TwoHandEnded"/>, else 0.</summary>
        public float TwoHandAngleChange
        {
            get { return _twoAngleChange; }
        }

        /// <summary>
        /// Where one hand's fingers are this frame: the run of detections, the posture, the amount and the
        /// point. Says whether the posture closed or opened in this frame, and whether a closing is held back
        /// for a reason of its own (the other hand's pinch is the caller's to add).
        /// </summary>
        void Follow(int side, double now, in TrackedHand hand, bool hovering, out bool closed, out bool opened, out bool held)
        {
            ref PinchState pinch = ref _pinch[side];
            bool was = pinch.Pinching;
            pinch.Down = false;
            pinch.Up = false;
            closed = false;
            opened = false;
            held = false;

            if (!hand.Tracked)
            {
                // A hand with no pose is not pinching: whatever it held is let go, and its run starts again.
                pinch.Amount = 0f;
                pinch.Pinching = false;
                _closeRun[side] = 0;
                opened = was;
                return;
            }

            JointSet joints = hand.World;
            float3 point = 0.5f * (joints[HandJoint.ThumbTip] + joints[HandJoint.IndexTip]);
            if (math.all(math.isfinite(point))) pinch.Point = point;

            // A ratio that is not a number is an open hand: the safe reading.
            float ratio = hand.PinchRatio;
            if (float.IsNaN(ratio) || float.IsInfinity(ratio)) ratio = float.PositiveInfinity;
            pinch.Amount = math.saturate((HandsConstants.PinchOpen - ratio) / (HandsConstants.PinchOpen - HandsConstants.PinchClose));

            // Held frames repeat the last detection: only a frame that carries one moves the run.
            if (hand.Fresh)
                _closeRun[side] = ratio < HandsConstants.PinchClose ? math.min(_closeRun[side] + 1, HandsConstants.PinchDetections) : 0;

            if (!was)
            {
                if (_closeRun[side] >= HandsConstants.PinchDetections)
                {
                    pinch.Pinching = true;
                    closed = true;
                    held = IsHeldBack(side, now, in hand, hovering);
                }
            }
            else if (ratio > HandsConstants.PinchOpen)
            {
                pinch.Pinching = false;
                opened = true;
                _closeRun[side] = 0;
            }
        }

        /// <summary>
        /// The cases of section 6.3 in which a closing does not fire. Each is written so that a number that is
        /// not a number holds the pinch back.
        /// </summary>
        bool IsHeldBack(int side, double now, in TrackedHand hand, bool hovering)
        {
            const float speedLimitSquared = HandsConstants.PinchMaxHandSpeed * HandsConstants.PinchMaxHandSpeed;
            return hovering
                || !(hand.TrackedSeconds >= HandsConstants.PinchQuietAfterEnterSeconds)
                || !(math.lengthsq(hand.PalmVelocity) <= speedLimitSquared)
                || now - _lastDown[side] < HandsConstants.PinchRefractorySeconds
                || !(hand.Confidence >= HandsConstants.InteractConfidence);
        }

        /// <summary>Turns a closing or an opening into the frame's Down or Up, and remembers what fired.</summary>
        void Settle(int side, double now, bool closed, bool opened, bool held)
        {
            ref PinchState pinch = ref _pinch[side];
            if (closed && !held)
            {
                pinch.Down = true;
                _fired[side] = true;
                _lastDown[side] = now;
            }
            if (opened)
            {
                pinch.Up = _fired[side];
                _fired[side] = false;
            }
        }

        /// <summary>
        /// Active while both hands are pinching. The angle is that of the line from the left pinch point to the
        /// right one in the plane of the view (the head's right and up axes, seen along its forward axis), with
        /// the head's right axis at 0 and a turn about the forward axis counting as it does for
        /// quaternion.AxisAngle. Each frame's change is taken the short way round and added to the running
        /// value, so a full turn reads 2 pi and nothing steps.
        /// </summary>
        void FollowTwoHands(quaternion headRotation)
        {
            bool active = _pinch[0].Pinching && _pinch[1].Pinching;
            bool wasActive = _two.Active;
            _twoStarted = active && !wasActive;
            _twoEnded = !active && wasActive;
            _twoDistanceChange = 0f;
            _twoAngleChange = 0f;

            if (!active)
            {
                if (wasActive)
                {
                    _twoDistanceChange = _lastDistance - _startDistance;
                    _twoAngleChange = _lastAngle - _startAngle;
                }
                _two = default(TwoHandPinch);
                return;
            }

            float3 left = _pinch[0].Point, right = _pinch[1].Point;
            float3 line = right - left;
            quaternion head = math.normalizesafe(headRotation);
            float x = math.dot(line, math.mul(head, new float3(1f, 0f, 0f)));
            float y = math.dot(line, math.mul(head, new float3(0f, 1f, 0f)));
            float distance = math.length(line);
            bool hasDirection = x * x + y * y >= MinLineMetres * MinLineMetres;

            if (!wasActive)
            {
                _rawAngle = hasDirection ? math.atan2(y, x) : 0f;
                _angle = _rawAngle;
                _startDistance = distance;
                _startAngle = _angle;
            }
            else if (hasDirection)
            {
                float raw = math.atan2(y, x);
                float delta = raw - _rawAngle;
                if (delta > math.PI) delta -= 2f * math.PI;
                else if (delta < -math.PI) delta += 2f * math.PI;
                _angle += delta;
                _rawAngle = raw;
            }

            _lastDistance = distance;
            _lastAngle = _angle;
            _two.Active = true;
            _two.Distance = distance;
            _two.Angle = _angle;
            _two.Midpoint = 0.5f * (left + right);
        }
    }
}
