using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Atlas.Input;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Hands
{
    /// <summary>
    /// What the runtime needs from the app, as plain values and delegates: Atlas.Hands may reference neither
    /// the app, the scan nor Bump Guard (documents/ATLAS_Hands_Design.md section 3.7, E3). Every delegate may be
    /// null; each has a neutral meaning then.
    /// </summary>
    public sealed class HandsDeps
    {
        /// <summary>Where detections come from. Never null: the null source when there is none.</summary>
        public IHandsSource Source;
        /// <summary>The input service: its gamepad flag, and its hands door when it implements IHandsInput.</summary>
        public IAtlasInput Input;
        /// <summary>The pose pipeline: the depth image for the mask. May be null (no mask).</summary>
        public IPosePipeline Pipeline;
        /// <summary>The display mode is Headset. Null: true.</summary>
        public Func<bool> Headset;
        /// <summary>Bump Guard's level is not Clear. Null: false.</summary>
        public Func<bool> Warning;
        /// <summary>The head comes from the AR camera (or the Editor's stand-in is driving). Null: true.</summary>
        public Func<bool> HeadFromCamera;
        /// <summary>The AR session's space to world, as placed this frame. Null: identity.</summary>
        public Func<float4x4> WorldFromSession;
        /// <summary>World position of the rear camera this frame. Null: the raw head position.</summary>
        public Func<float3> CameraPosition;
        /// <summary>The event log, looked up lazily because it is built after the hands. May return null.</summary>
        public Func<IEventLog> Events;
        /// <summary>The space host, looked up lazily. May return null.</summary>
        public Func<ISpaceHost> Host;
        /// <summary>Where a hand log is written when recording is switched on. Null: no recording.</summary>
        public Func<string> RecordPath;
        /// <summary>Times Bump Guard's step caught the mask source throwing (it then ran unmasked). Null: never.</summary>
        public Func<int> MaskFaults;
    }

    /// <summary>What a fingertip is doing to the world's surfaces this frame (section 6.5).</summary>
    public struct WorldTouch
    {
        /// <summary>A surface is within the glow distance.</summary>
        public bool Near;
        /// <summary>The fingertip is on the surface.</summary>
        public bool Contact;
        /// <summary>True for the frame the contact began.</summary>
        public bool ContactBegan;
        /// <summary>World: the closest surface point and its normal, towards the open side.</summary>
        public float3 Point, Normal;
        /// <summary>Metres from the fingertip to the surface.</summary>
        public float Distance;
    }

    /// <summary>
    /// The tracked hands' frame work without the engine (section 3.3): step 1 polls the source, lifts, filters,
    /// runs the gate, visibility, pinch, the depth mask, the near anchor and world touch, freezes this frame's
    /// hands and hands the fingertips to input; step 8b logs edges. It is also what the rest of the app sees:
    /// IHands, IDepthMaskSource and IReachLayout. Allocates nothing per frame; strings are built only at edges.
    /// </summary>
    public sealed class HandsRuntime : IHands, IDepthMaskSource, IReachLayout
    {
        const int AgeRing = 256;
        const float NearOnSeconds = 0.5f, NearOffSeconds = 1f;

        readonly HandsDeps _deps;
        readonly IHandsInput _handsInput;
        readonly HandsLift _lift = new HandsLift();
        readonly HandsFilter _filter = new HandsFilter();
        readonly HandsGestures _gestures = new HandsGestures();
        readonly HandsVisibility _visibility = new HandsVisibility();
        readonly HandsGate _gate = new HandsGate();
        readonly HandsMaskBuilder _maskBuilder = new HandsMaskBuilder();
        readonly ReachAnchor _reach = new ReachAnchor();
        readonly RoomSurfaces _surfaces = new RoomSurfaces();
        readonly HandsLogRecorder _recorder = new HandsLogRecorder();
        readonly StringBuilder _json = new StringBuilder(256);
        readonly float[] _ages = new float[AgeRing];

        HandsDetection _detection;
        HandPose _left, _right;
        WorldTouch _touchLeft, _touchRight;
        DepthMask _mask;
        GateState _gateState;
        RoomModel _room;
        float3 _previousHead;
        bool _havePreviousHead;
        bool _hideThisFrame, _hiddenNow;
        bool _nearNow;
        float _nearTimer;
        bool _faulted;
        int _lastFaultCode;
        int _maskFaults;
        double _lastRestart = double.NegativeInfinity;
        bool _recording;

        // Edges and statistics (section 8.1).
        bool _wasLeft, _wasRight;
        // The edge margin of each hand's last tracked frame, for the cause of its exit (section 8.1), and whether its enter was held back.
        float _edgeLeft, _edgeRight;
        bool _heldLeft, _heldRight;
        // The entered space's own surfaces, when it publishes them by implementing IHandsSurface (section 6.5).
        IHandsSurface _spaceSurface;
        double _enterLeft, _enterRight, _lastEventLeft = double.NegativeInfinity, _lastEventRight = double.NegativeInfinity;
        int _suppressedEvents;
        double _activeSince, _lastBench = double.NegativeInfinity;
        int _detections, _presses, _pinches, _maskFrames, _ageCount;
        float _visibleLeftSeconds, _visibleRightSeconds, _activeSeconds;
        float _lastAge, _lastRequestMs;
        float _mainThreadMs;
        long _senseTicks;

        public HandsRuntime(HandsDeps deps)
        {
            if (deps == null) throw new ArgumentNullException("deps");
            if (deps.Source == null) deps.Source = new NullHandsSource();
            _deps = deps;
            _handsInput = deps.Input as IHandsInput;
            Reach = HandsConstants.ReachMetres;
            Sound = true;
            Draw = true;
        }

        // ---- settings, written by the parameter callbacks ----------------------------------------------------

        /// <summary>hands.enabled: the kill switch.</summary>
        public bool Enabled { get; set; }
        /// <summary>hands.rate: 0 follows the tier; 30 or 15 fixes the detector's rate (for the bench).</summary>
        public int RateOverride { get; set; }
        /// <summary>hands.sound.</summary>
        public bool Sound { get; set; }
        /// <summary>hands.draw: off hides the meshes while everything else runs (the blind-reach check of the camera-to-eye offset).</summary>
        public bool Draw { get; set; }
        /// <summary>hands.bench: one line of measurements a second in the event log.</summary>
        public bool Bench { get; set; }
        /// <summary>hands.reach: metres from the camera to the near anchor.</summary>
        public float Reach
        {
            get { return _reach.ReachMetres; }
            set { _reach.ReachMetres = value; }
        }

        /// <summary>hands.record: writes detections (joints only, never an image) to the path the app gives.</summary>
        public bool Record
        {
            get { return _recording; }
            set
            {
                if (value == _recording) return;
                if (value)
                {
                    string path = _deps.RecordPath != null ? _deps.RecordPath() : null;
                    if (string.IsNullOrEmpty(path)) return;
                    _recorder.Start(path);
                    _recording = true;
                }
                else
                {
                    _recorder.Stop();
                    _recording = false;
                }
            }
        }

        // ---- what the view and the tests read ----------------------------------------------------------------

        public IHandsSource Source { get { return _deps.Source; } }
        public GateState Gate { get { return _gateState; } }
        public ref readonly TrackedHand Tracked(HandSide side) { return ref _filter.Hand(side); }
        public ref readonly PinchState Pinch(HandSide side) { return ref _gestures.Pinch(side); }
        public ref readonly WorldTouch Touch(HandSide side)
        {
            if (side == HandSide.Left) return ref _touchLeft;
            return ref _touchRight;
        }
        /// <summary>Nothing of the hands is drawn this frame (a space asked, or drawing is switched off).</summary>
        public bool Hidden { get { return _hiddenNow || !Draw; } }
        /// <summary>The hands' own main-thread time, milliseconds, smoothed.</summary>
        public float MainThreadMs { get { return _mainThreadMs; } }
        /// <summary>The sense step caught an exception: hands are off until the app restarts.</summary>
        public bool Faulted { get { return _faulted; } }
        public float Scale { get { return _lift.Scale; } }
        public RoomSurfaces Surfaces { get { return _surfaces; } }
        /// <summary>What input's hands door reported after its last tick; default when input has no such door.</summary>
        public HandsTouchState InputTouch { get { return _handsInput != null ? _handsInput.HandsTouch : default(HandsTouchState); } }

        // ---- IHands ------------------------------------------------------------------------------------------

        public bool Active { get { return _gateState.Active; } }

        public ref readonly HandPose Pose(HandSide side)
        {
            if (side == HandSide.Left) return ref _left;
            return ref _right;
        }

        public float3 Joint(HandSide side, HandJoint joint)
        {
            return _filter.Hand(side).World[joint];
        }

        public ref readonly TwoHandPinch TwoHand { get { return ref _gestures.TwoHand; } }

        public void HideThisFrame() { _hideThisFrame = true; _hiddenNow = true; }

        // ---- IDepthMaskSource --------------------------------------------------------------------------------

        public bool TryGetMask(int depthSequence, out DepthMask mask)
        {
            mask = default(DepthMask);
            if (_faulted || !_gateState.Active) return false;
            // In a hands session Bump Guard is told so even when no hand is in view (decision H17); the regions
            // are handed over only for the depth image they were built for.
            if (!_mask.IsEmpty && _mask.DepthSequence == depthSequence) mask = _mask;
            mask.DepthSequence = depthSequence;
            mask.Session = 1;
            return true;
        }

        // ---- IReachLayout ------------------------------------------------------------------------------------

        public bool Near { get { return _reach.Near; } }
        public float3 Position { get { return _reach.Position; } }
        public quaternion Rotation { get { return _reach.Rotation; } }
        float IReachLayout.Scale { get { return _reach.Scale; } }

        // ---- step 1 ------------------------------------------------------------------------------------------

        /// <summary>
        /// Step 1 of the frame, after the pose pipeline and before input. Any exception switches the hands off
        /// for the session and leaves no hand, no touch and no mask behind (decision R9).
        /// </summary>
        public void Sense(ref FrameContext frame)
        {
            long start = Stopwatch.GetTimestamp();
            if (_faulted)
            {
                ClearOutputs();
                return;
            }
            try
            {
                SenseCore(ref frame);
            }
            catch (Exception e)
            {
                Fault("sense", e);
            }
            _senseTicks = Stopwatch.GetTimestamp() - start;
        }

        void SenseCore(ref FrameContext frame)
        {
            double now = frame.Time;
            float dt = frame.DeltaTime;
            IHandsSource source = _deps.Source;
            // A space's veto holds for one frame of touch; drawing was already suppressed in the frame it was asked.
            bool hidden = _hideThisFrame;
            _hideThisFrame = false;
            _hiddenNow = false;

            // The gate (section 3.5).
            GateInputs gate;
            gate.Headset = _deps.Headset == null || _deps.Headset();
            gate.GamepadPresent = _deps.Input != null && _deps.Input.GamepadPresent;
            gate.Enabled = Enabled;
            gate.SourceAvailable = source.Available;
            gate.HeadFromCamera = _deps.HeadFromCamera == null || _deps.HeadFromCamera();
            gate.Tier = frame.Tier;
            gate.RateOverride = RateOverride;
            gate.RequestSeconds = _lastRequestMs * 0.001f;
            gate.MainThreadMs = _mainThreadMs;
            gate.AnyHandSeen = _left.Tracked || _right.Tracked;
            _gateState = _gate.Update(now, dt, in gate);
            if (_gateState.ActiveChanged)
            {
                if (_gateState.Active)
                {
                    // A new session: capture times start again, so everything that remembers a hand starts again.
                    _activeSince = now;
                    _lift.Reset();
                    _filter.Reset();
                    _gestures.Reset();
                    _maskBuilder.Reset();
                    source.SetRate(_gateState.RateHz);
                    LogOn();
                }
                else
                {
                    // The hands keep their last pose while the gate's fade plays; the filter lets them go by itself.
                    source.Stop();
                    LogOff(now);
                }
            }
            else if (_gateState.Active && _gateState.RateChanged)
            {
                source.SetRate(_gateState.RateHz);
                LogRate();
            }

            // The newest detection, lifted into the room (sections 3.4, 4.3).
            if (_gateState.Active)
            {
                _activeSeconds += dt;
                RestartAfterFault(now, source);
                if (source.TryRead(now, ref _detection))
                {
                    float age = _detection.AgeSeconds;
                    _lastAge = age;
                    _lastRequestMs = _detection.RequestMs;
                    _ages[_ageCount % AgeRing] = age;
                    _ageCount++;
                    _detections++;
                    if (_recording) _recorder.Offer(now, frame.RawHeadPosition, frame.RawHeadRotation, in _detection);
                    if (_detection.ImageWidth > 0) _filter.ViewCentreX = _detection.ImageWidth * 0.5f;
                    // A result with no hand in it is observed too: that is how the filter learns of a loss.
                    if (age <= HandsConstants.MaxResultAgeSeconds) Observe(now - age);
                }
            }

            _filter.Advance(now);
            ref readonly TrackedHand left = ref _filter.Hand(HandSide.Left);
            ref readonly TrackedHand right = ref _filter.Hand(HandSide.Right);

            // Pinch and two hands; a hand that hovers a target does not pinch (section 6.3).
            HandsTouchState inputTouch = InputTouch;
            _gestures.Update(now, in left, in right, frame.RawHeadRotation,
                inputTouch.Left.Hover > 0f, inputTouch.Right.Hover > 0f);

            // Opacity (section 5.4).
            bool warning = _deps.Warning != null && _deps.Warning();
            VisibilityInputs visibility;
            visibility.GateActive = _gateState.Active;
            visibility.Warning = warning;
            visibility.Hidden = hidden;
            float opacityLeft = _visibility.Update(HandSide.Left, dt, in left, in visibility);
            float opacityRight = _visibility.Update(HandSide.Right, dt, in right, in visibility);
            if (opacityLeft > 0.5f) _visibleLeftSeconds += dt;
            if (opacityRight > 0.5f) _visibleRightSeconds += dt;

            FillPose(ref _left, in left, opacityLeft, in _gestures.Pinch(HandSide.Left));
            FillPose(ref _right, in right, opacityRight, in _gestures.Pinch(HandSide.Right));

            // Fingertips and the pinch edge go to input before its tick (section 6.2). Nothing while Bump Guard
            // warns, while a space hides the hands, or while the gate is inactive.
            bool interact = _gateState.Active && !warning && !hidden;
            HandsInput input = default(HandsInput);
            input.Active = interact;
            float3 tipLeft = PressPoint(in left), tipRight = PressPoint(in right);
            bool leftValid = interact && left.Tracked && left.Confidence >= HandsConstants.InteractConfidence;
            bool rightValid = interact && right.Tracked && right.Confidence >= HandsConstants.InteractConfidence;
            input.Left.Valid = leftValid;
            input.Left.World = tipLeft;
            input.Left.VelocityWorld = left.PalmVelocity;
            input.Left.Confidence = left.Confidence;
            input.Right.Valid = rightValid;
            input.Right.World = tipRight;
            input.Right.VelocityWorld = right.PalmVelocity;
            input.Right.Confidence = right.Confidence;
            input.PinchDownLeft = interact && _gestures.Pinch(HandSide.Left).Down;
            input.PinchDownRight = interact && _gestures.Pinch(HandSide.Right).Down;
            if (_handsInput != null) _handsInput.SetHands(in input);

            // The mask for the depth image Bump Guard reads next (section 7.1). Hidden hands are still masked.
            IPosePipeline pipeline = _deps.Pipeline;
            DepthFrame depth = default(DepthFrame);
            if (pipeline != null)
            {
                ref readonly SensorFrame sensors = ref pipeline.Sensors;
                if (sensors.HasDepth) depth = sensors.Depth;
            }
            // Built every frame, with or without a new depth image: the builder remembers each hand through a
            // brief loss, and a frame with no image simply gets the empty mask.
            _mask = _maskBuilder.Build(now, in depth, in left, in right);
            if (!_gateState.Active) _mask = default(DepthMask);
            if (!_mask.IsEmpty) _maskFrames++;

            // The near anchor (section 6.1).
            float3 head = frame.RawHeadPosition;
            float headSpeed = _havePreviousHead && dt > 0f ? math.length((head - _previousHead).xz) / dt : 0f;
            _previousHead = head;
            _havePreviousHead = true;
            float3 camera = _deps.CameraPosition != null ? _deps.CameraPosition() : head;
            if (UpdateSurfaces() || frame.PoseJumpRejected) _reach.Reset();
            _reach.Update(now, dt, DebouncedNear(dt), camera, frame.RawHeadRotation, headSpeed,
                leftValid, tipLeft, rightValid, tipRight,
                inputTouch.Left.Pressed || inputTouch.Right.Pressed, _surfaces);

            // World touch (section 6.5).
            UpdateWorldTouch(ref _touchLeft, leftValid && opacityLeft > 0.5f, tipLeft);
            UpdateWorldTouch(ref _touchRight, rightValid && opacityRight > 0.5f, tipRight);
        }

        void Observe(double captureTime)
        {
            float4x4 worldFromSession = _deps.WorldFromSession != null ? _deps.WorldFromSession() : float4x4.identity;
            float4x4 worldFromCamera = math.mul(worldFromSession, _detection.CameraToSession);
            ObservedHand a = default(ObservedHand), b = default(ObservedHand);
            int count = 0;
            int hands = math.clamp(_detection.HandCount, 0, HandsDetection.MaxHands);
            for (int i = 0; i < hands; i++)
            {
                ObservedHand hand;
                if (!_lift.TryLift(in _detection, i, out hand)) continue;
                hand.Joints = hand.Joints.Transformed(in worldFromCamera);
                hand.PalmCentre = math.transform(worldFromCamera, hand.PalmCentre);
                hand.PalmNormal = math.normalizesafe(math.rotate(worldFromCamera, hand.PalmNormal), new float3(0f, 0f, -1f));
                if (!hand.Joints.IsFinite()) continue;
                if (count == 0) a = hand;
                else b = hand;
                count++;
            }
            _filter.Observe(captureTime, count, in a, in b);
        }

        void FillPose(ref HandPose pose, in TrackedHand hand, float opacity, in PinchState pinch)
        {
            pose.Tracked = hand.Tracked;
            pose.Confidence = hand.Confidence;
            pose.Opacity = hand.Tracked ? opacity : 0f;
            pose.AgeSeconds = hand.AgeSeconds;
            pose.Scale = _lift.Scale;
            pose.PalmCentre = hand.PalmCentre;
            pose.PalmNormal = hand.PalmNormal;
            pose.Pinch = pinch.Amount;
            pose.Pinching = pinch.Pinching;
        }

        // The pad of the index finger: a little back from the tip joint, along the last bone. It is where the
        // filter has the finger, not where the drawn hand is predicted to be: a jab that stops three centimetres
        // short of a face would otherwise be carried through it and fire (measured by the touch tests).
        static float3 PressPoint(in TrackedHand hand)
        {
            float3 tip = hand.World[HandJoint.IndexTip];
            float3 back = math.normalizesafe(hand.World[HandJoint.IndexDip] - tip, float3.zero);
            return tip + back * HandsConstants.FingerPadMetres - hand.Predicted;
        }

        // Panels come near once the session has held for half a second and go back a second after it ends, so a
        // gate that flickers does not throw the panels to and fro.
        bool DebouncedNear(float dt)
        {
            bool wanted = _gateState.Near;
            if (wanted == _nearNow)
            {
                _nearTimer = 0f;
                return _nearNow;
            }
            _nearTimer += dt;
            if (_nearTimer >= (wanted ? NearOnSeconds : NearOffSeconds))
            {
                _nearNow = wanted;
                _nearTimer = 0f;
            }
            return _nearNow;
        }

        // True when the room changed.
        bool UpdateSurfaces()
        {
            ISpaceHost host = _deps.Host != null ? _deps.Host() : null;
            RoomModel room = host != null ? host.Room : null;
            bool changed = !ReferenceEquals(room, _room);
            if (changed)
            {
                _room = room;
                _surfaces.SetRoom(room);
            }
            // Only where the picture is the real room: a registered space that is up (section 6.5).
            ISpaceModule module = host != null && host.HasSpace ? host.Module : null;
            _surfaces.Enabled = room != null && module != null && module.Registered &&
                                (host.State == SpaceHostState.Running || host.State == SpaceHostState.FadingIn);
            bool up = module != null && (host.State == SpaceHostState.Running || host.State == SpaceHostState.FadingIn);
            _spaceSurface = up ? module as IHandsSurface : null;
            return changed;
        }

        void UpdateWorldTouch(ref WorldTouch touch, bool valid, float3 tip)
        {
            bool was = touch.Contact;
            float3 point, normal;
            bool hit = false;
            point = float3.zero;
            normal = float3.zero;
            if (valid)
            {
                hit = _surfaces.TryClosest(tip, HandsConstants.GlowMetres, out point, out normal);
                float3 sp, sn;
                if (_spaceSurface != null && _spaceSurface.TryClosest(tip, HandsConstants.GlowMetres, out sp, out sn) &&
                    (!hit || math.distancesq(tip, sp) < math.distancesq(tip, point)))
                {
                    hit = true;
                    point = sp;
                    normal = sn;
                }
            }
            if (hit)
            {
                touch.Near = true;
                touch.Point = point;
                touch.Normal = normal;
                touch.Distance = math.distance(tip, point);
                touch.Contact = touch.Distance <= HandsConstants.ContactMetres;
            }
            else
            {
                touch.Near = false;
                touch.Contact = false;
                touch.Distance = float.PositiveInfinity;
            }
            touch.ContactBegan = touch.Contact && !was;
        }

        void RestartAfterFault(double now, IHandsSource source)
        {
            int code = source.Fault;
            if (code == 0) return;
            if (code != _lastFaultCode)
            {
                _lastFaultCode = code;
                IEventLog events = Events();
                if (events != null) events.Log("hands.fault", "code", code);
            }
            if (now - _lastRestart < HandsConstants.RestartAfterFaultSeconds) return;
            _lastRestart = now;
            source.Stop();
            source.SetRate(_gateState.RateHz);
        }

        void Fault(string where, Exception e)
        {
            _faulted = true;
            ClearOutputs();
            try
            {
                _deps.Source.Stop();
                IEventLog events = Events();
                if (events != null) events.Log("hands.fault", "code", where + ": " + e.GetType().Name + ": " + e.Message);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Called by the upload and after-frame hooks when they catch an exception of their own (decision R9).</summary>
        public void ReportFault(string where, Exception e) { Fault(where, e); }

        void ClearOutputs()
        {
            _left = default(HandPose);
            _right = default(HandPose);
            _touchLeft = default(WorldTouch);
            _touchRight = default(WorldTouch);
            _mask = default(DepthMask);
            _gateState = default(GateState);
            if (_handsInput != null)
            {
                HandsInput none = default(HandsInput);
                _handsInput.SetHands(in none);
            }
            _reach.Update(0.0, 0f, false, float3.zero, quaternion.identity, 0f, false, float3.zero, false, float3.zero, false, null);
        }

        /// <summary>Folds the cost of this frame's sense and upload into the smoothed figure the governor reads.</summary>
        public void AddUploadTicks(long ticks)
        {
            float ms = (float)((_senseTicks + ticks) * 1000.0 / Stopwatch.Frequency);
            _mainThreadMs = _mainThreadMs <= 0f ? ms : math.lerp(_mainThreadMs, ms, 0.1f);
        }

        // ---- step 8b: edges to the event log (section 8.1) -----------------------------------------------------

        /// <summary>Step 8b. Logs edges; never per frame.</summary>
        public void AfterFrame(in FrameContext frame)
        {
            if (_faulted) return;
            double now = frame.Time;
            IEventLog events = Events();

            // Bump Guard's step swallows a mask that throws and runs unmasked; it is said once here.
            if (_deps.MaskFaults != null)
            {
                int maskFaults = _deps.MaskFaults();
                if (maskFaults > _maskFaults)
                {
                    if (_maskFaults == 0 && events != null) events.Log("hands.fault", "code", "mask");
                    _maskFaults = maskFaults;
                }
            }

            EnterExit(events, now, HandSide.Left, _left.Tracked, ref _wasLeft, ref _enterLeft, ref _lastEventLeft, ref _edgeLeft, ref _heldLeft);
            EnterExit(events, now, HandSide.Right, _right.Tracked, ref _wasRight, ref _enterRight, ref _lastEventRight, ref _edgeRight, ref _heldRight);

            HandsTouchState touch = InputTouch;
            if (touch.Left.Fired) LogTouch(events, "L", touch.Left.Target);
            if (touch.Right.Fired) LogTouch(events, "R", touch.Right.Target);
            if (touch.PinchFired)
            {
                _pinches++;
                if (events != null)
                {
                    string hand = _gestures.Pinch(HandSide.Left).Down ? "L" : "R";
                    string label = _handsInput != null ? _handsInput.LabelOf(touch.PinchTarget) : null;
                    _json.Length = 0;
                    _json.Append("{\"hand\":\"").Append(hand).Append("\",\"label\":\"").Append(Clean(label)).Append("\"}");
                    events.LogJson("hands.pinch", _json.ToString());
                }
            }
            if (events != null && _gestures.TwoHandStarted) events.Log("hands.two", "phase", "start");
            if (events != null && _gestures.TwoHandEnded)
            {
                _json.Length = 0;
                _json.Append("{\"phase\":\"end\",\"dist\":").Append(Number(_gestures.TwoHandDistanceChange))
                     .Append(",\"angle\":").Append(Number(math.degrees(_gestures.TwoHandAngleChange))).Append('}');
                events.LogJson("hands.two", _json.ToString());
            }

            if (Bench && _gateState.Active && events != null && now - _lastBench >= 1.0)
            {
                _lastBench = now;
                events.LogJson("hands.bench", StatsJson(now));
            }
        }

        void EnterExit(IEventLog events, double now, HandSide side, bool tracked, ref bool was, ref double entered,
                       ref double lastEvent, ref float edge, ref bool held)
        {
            if (tracked) edge = _filter.Hand(side).EdgeMargin;
            if (tracked == was) return;
            was = tracked;
            if (tracked) entered = now;
            // At most one enter and one exit a second for each hand; the rest is counted (section 8.1).
            if (tracked && now - lastEvent < HandsConstants.EnterExitEventSeconds)
            {
                _suppressedEvents++;
                held = true;
                return;
            }
            // The exit of an enter that was held back is held back too, so no exit is logged without its enter.
            if (!tracked && held)
            {
                held = false;
                _suppressedEvents++;
                return;
            }
            if (tracked) held = false;
            if (tracked) lastEvent = now;
            if (events == null) return;
            string hand = side == HandSide.Left ? "L" : "R";
            if (tracked)
            {
                events.Log("hands.enter", "hand", hand);
                return;
            }
            _json.Length = 0;
            _json.Append("{\"hand\":\"").Append(hand).Append("\",\"cause\":\"")
                 .Append(edge < HandsConstants.EdgeFadeBand ? "edge" : "lost")
                 .Append("\",\"visibleMs\":").Append((long)math.round((now - entered) * 1000.0)).Append('}');
            events.LogJson("hands.exit", _json.ToString());
        }

        void LogTouch(IEventLog events, string hand, GazeTargetId target)
        {
            _presses++;
            if (events == null) return;
            string label = _handsInput != null ? _handsInput.LabelOf(target) : null;
            int tag = _handsInput != null ? _handsInput.TagOf(target) : 0;
            _json.Length = 0;
            _json.Append("{\"hand\":\"").Append(hand).Append("\",\"label\":\"").Append(Clean(label))
                 .Append("\",\"tag\":").Append(tag).Append('}');
            events.LogJson("hands.touch", _json.ToString());
        }

        void LogOn()
        {
            IEventLog events = Events();
            if (events != null) events.Log("hands.on", "hz", _gateState.RateHz);
        }

        void LogRate()
        {
            IEventLog events = Events();
            if (events == null) return;
            _json.Length = 0;
            _json.Append("{\"hz\":").Append(_gateState.RateHz).Append(",\"why\":\"").Append(RateWhyName(_gateState.RateWhy)).Append("\"}");
            events.LogJson("hands.rate", _json.ToString());
        }

        void LogOff(double now)
        {
            IEventLog events = Events();
            if (events == null) return;
            events.Log("hands.off", "reason", ReasonName(_gateState.Reason));
            events.LogJson("hands.stats", StatsJson(now));
            ResetStats();
        }

        /// <summary>The statistics of the session so far; also written when the app stops.</summary>
        public void LogStats(double now)
        {
            IEventLog events = Events();
            if (events != null && _detections > 0) events.LogJson("hands.stats", StatsJson(now));
        }

        string StatsJson(double now)
        {
            float seconds = math.max(_activeSeconds, 1e-3f);
            int n = math.min(_ageCount, AgeRing);
            float p50 = 0f, p95 = 0f;
            if (n > 0)
            {
                Array.Sort(_ages, 0, n);
                p50 = _ages[n / 2];
                p95 = _ages[math.min(n - 1, (int)(n * 0.95f))];
            }
            _json.Length = 0;
            _json.Append("{\"hz\":").Append(Number(_detections / seconds))
                 .Append(",\"ageMsP50\":").Append(Number(p50 * 1000f))
                 .Append(",\"ageMsP95\":").Append(Number(p95 * 1000f))
                 .Append(",\"requestMs\":").Append(Number(_lastRequestMs))
                 .Append(",\"mainMs\":").Append(Number(_mainThreadMs))
                 .Append(",\"visibleL\":").Append(Number(_visibleLeftSeconds / seconds))
                 .Append(",\"visibleR\":").Append(Number(_visibleRightSeconds / seconds))
                 .Append(",\"presses\":").Append(_presses)
                 .Append(",\"pinches\":").Append(_pinches)
                 .Append(",\"maskFrames\":").Append(_maskFrames)
                 .Append(",\"scale\":").Append(Number(_lift.Scale))
                 .Append(",\"image\":\"").Append(_detection.ImageWidth).Append('x').Append(_detection.ImageHeight)
                 .Append("\",\"depth\":\"").Append(_detection.DepthWidth).Append('x').Append(_detection.DepthHeight)
                 .Append("\",\"fx\":").Append(Number(_detection.Fx))
                 .Append(",\"fy\":").Append(Number(_detection.Fy))
                 .Append(",\"quietEvents\":").Append(_suppressedEvents)
                 .Append(",\"source\":\"").Append(_deps.Source.Name).Append("\"}");
            return _json.ToString();
        }

        void ResetStats()
        {
            _detections = _presses = _pinches = _maskFrames = _ageCount = _suppressedEvents = 0;
            _visibleLeftSeconds = _visibleRightSeconds = _activeSeconds = 0f;
        }

        /// <summary>Stops the detector and the recorder. The app calls it before the AR objects go.</summary>
        public void Shutdown(double now)
        {
            try
            {
                if (_gateState.Active) LogStats(now);
                _deps.Source.Stop();
                if (_recording) _recorder.Stop();
            }
            catch (Exception)
            {
            }
            _recording = false;
            ClearOutputs();
        }

        IEventLog Events() { return _deps.Events != null ? _deps.Events() : null; }

        static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static string Clean(string label)
        {
            if (string.IsNullOrEmpty(label)) return "none";
            return label.Replace('"', '\'').Replace('\\', '/');
        }

        static string ReasonName(HandsOffReason reason)
        {
            switch (reason)
            {
                case HandsOffReason.Mode: return "mode";
                case HandsOffReason.Pad: return "pad";
                case HandsOffReason.Param: return "param";
                case HandsOffReason.Source: return "source";
                case HandsOffReason.Pose: return "pose";
                case HandsOffReason.Heat: return "heat";
                default: return "none";
            }
        }

        static string RateWhyName(HandsRateWhy why)
        {
            switch (why)
            {
                case HandsRateWhy.Governor: return "governor";
                case HandsRateWhy.Idle: return "idle";
                case HandsRateWhy.Override: return "override";
                default: return "tier";
            }
        }
    }
}
