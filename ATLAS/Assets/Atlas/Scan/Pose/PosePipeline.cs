using System;
using Atlas.World;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ArTrackingState = UnityEngine.XR.ARSubsystems.TrackingState;
using TrackingState = Atlas.World.TrackingState;

namespace Atlas.Scan
{
    /// <summary>
    /// Frame-loop steps 1 and 3 (Master Plan section 6.3; task P1-C11). Step 1 reads the head once, places
    /// the XR Origin so that the room anchor is the world origin (contract README F4), reads the depth image
    /// and fills the raw stream that Bump Guard and the eyes use. Step 3 runs the engine-free PoseFilter and
    /// fills the geometry stream that the kernel and the spaces use. Nothing else reads the head (F5).
    /// </summary>
    public sealed class PosePipeline : IPosePipeline, ISenseStep, IPoseFilterStep
    {
        readonly Transform _xrOrigin;
        readonly Transform _trackedCamera;
        readonly Transform _head;
        readonly Transform _sessionSpace;       // the tracked camera's parent: its local frame is the AR session frame
        readonly ARSession _session;
        readonly PoseFilter _filter = new PoseFilter();
        readonly GyroPredictor _gyro = new GyroPredictor();

        IPoseSource _source;
        IDepthSource _depth = new NullDepthSource();
        IPosedDepthSource _posedDepth;
        KeyboardWalkerPoseSource _walker;
        RoomModel _room;
        Transform _anchor;
        ARAnchor _arAnchor;

        SensorFrame _sensors;
        HeadPose _lastSample = HeadPose.Identity;     // in the source's own frame
        TrackingState _lastTracking = TrackingState.NotTracking;
        bool _haveTracking;
        float _gyroPredictMs;
        float3 _originPosition;
        quaternion _originRotation = quaternion.identity;
        bool _originWritten;
        int _driverState = -1;                         // -1 unknown, 0 disabled, 1 enabled

        /// <summary>Takes XrOrigin, TrackedCamera and Head from the boot context. All may be null (the Station, tests of the flow with no rig).</summary>
        public PosePipeline(AtlasBootContext boot, ARSession session)
        {
            if (boot == null) throw new ArgumentNullException(nameof(boot));
            _xrOrigin = boot.XrOrigin;
            _trackedCamera = boot.TrackedCamera;
            _head = boot.Head != null ? boot.Head : boot.TrackedCamera;
            _sessionSpace = _trackedCamera != null && _trackedCamera.parent != null ? _trackedCamera.parent : _xrOrigin;
            _session = session;
            _sensors.Head = HeadPose.Identity;
        }

        // ---- wiring -------------------------------------------------------------------------------------

        public IPoseSource Source
        {
            get { return _source; }
            set
            {
                if (ReferenceEquals(_source, value)) return;
                _source = value;
                _walker = value as KeyboardWalkerPoseSource;
                _driverState = -1;
                // A different source is a different place: the first displacement after the change is not a step.
                _filter.Reset();
                _gyro.Reset();
            }
        }

        public IDepthSource Depth
        {
            get { return _depth; }
            set
            {
                _depth = value ?? new NullDepthSource();
                _posedDepth = _depth as IPosedDepthSource;
            }
        }

        public RoomModel Room
        {
            get { return _room; }
            set
            {
                _room = value;
                ResetGeometry();
            }
        }

        /// <summary>
        /// The ARAnchor of the active room, or null (fixture, replay). With no anchor, Room.Anchor's position
        /// and yaw are used as a fixed pose in the session frame. Set it before the room (lane C section 14.2).
        /// </summary>
        public Transform Anchor
        {
            get { return _anchor; }
            set
            {
                _anchor = value;
                _arAnchor = value != null ? value.GetComponent<ARAnchor>() : null;
            }
        }

        /// <summary>The room anchor is tracked this frame. True when there is no anchor to lose.</summary>
        public bool AnchorTracked { get; private set; } = true;

        /// <summary>The AR session set-up, so that its video format is retried until the camera has started. May be null.</summary>
        public ArSessionRig Rig { get; set; }
        /// <summary>Receives the raw stream at the end of step 1 while it is recording. May be null.</summary>
        public PoseLogRecorder Recorder { get; set; }

        // ---- the two streams ----------------------------------------------------------------------------

        public ref readonly SensorFrame Sensors { get { return ref _sensors; } }
        public HeadPose Raw { get { return _sensors.Head; } }
        public HeadPose Geometry { get { return _filter.Geometry; } }
        public float2 Step { get { return _filter.Step; } }
        public float2 PathFloor { get { return _filter.PathFloor; } }
        public bool JumpRejected { get { return _filter.JumpRejected; } }
        public int JumpCount { get { return _filter.JumpCount; } }
        public bool TrackingLost { get { return _filter.TrackingLost; } }

        public void ResetGeometry()
        {
            _filter.Reset();
        }

        public float GyroPredictMs
        {
            get { return _gyroPredictMs; }
            set { _gyroPredictMs = math.clamp(value, 0f, GyroPredictor.MaxLeadMs); }
        }

        public quaternion PredictionRotation { get { return _gyro.Prediction; } }

        public event Action<TrackingState> TrackingChanged;
        public event Action TrackingLostRaised;
        public event Action TrackingRegained;
        public event Action<float> JumpRejectedRaised;

        // ---- step 1 -------------------------------------------------------------------------------------

        public void Sense(ref FrameContext frame)
        {
            ArSessionRig rig = Rig;
            if (rig != null && !rig.VideoFormatApplied) rig.ApplyBase();

            double time = frame.Time;
            float deltaTime = frame.DeltaTime;
            IPoseSource source = _source;
            bool drivesRig = source != null && source.DrivesRig;
            SetDriver(drivesRig, rig);

            if (_walker != null && !_walker.HasExternalInput) KeyboardWalkerInput.Feed(_walker);

            HeadPose sample;
            if (source != null && source.TryRead(time, deltaTime, out sample))
                _lastSample = sample;
            else
                // No sample this frame: the head stays where it was, and is not tracked.
                sample = new HeadPose(time, _lastSample.Position, _lastSample.Rotation, TrackingState.NotTracking);

            // Where the AR session frame sits in the world. The identity until a room is active, and for a
            // source that already speaks the room frame.
            float3 sessionPosition = float3.zero;
            quaternion sessionRotation = quaternion.identity;
            bool anchorTracked = true;
            RoomModel room = _room;
            bool sessionFrame = source == null || source.Frame == PoseFrameKind.Session;
            if (room != null && sessionFrame)
            {
                if (_anchor != null && _sessionSpace != null)
                {
                    // The anchor's pose in the session frame. Its world pose was placed with the session
                    // frame's pose of the last render, which is still the pose of _sessionSpace now.
                    _anchor.GetPositionAndRotation(out Vector3 anchorWorld, out Quaternion anchorWorldRotation);
                    float3 anchorPosition = _sessionSpace.InverseTransformPoint(anchorWorld);
                    quaternion anchorRotation = Quaternion.Inverse(_sessionSpace.rotation) * anchorWorldRotation;
                    RoomFrame.SessionToRoom(anchorPosition, anchorRotation, out sessionPosition, out sessionRotation);
                    anchorTracked = _arAnchor == null || _arAnchor.trackingState == ArTrackingState.Tracking;
                }
                else
                {
                    RoomFrame.SessionToRoom(room.Anchor.Position, quaternion.RotateY(room.Anchor.Yaw),
                        out sessionPosition, out sessionRotation);
                }
            }
            PlaceSessionFrame(sessionPosition, sessionRotation);

            float3 headPosition;
            quaternion headRotation;
            RoomFrame.Compose(sessionPosition, sessionRotation, sample.Position, sample.Rotation,
                out headPosition, out headRotation);
            var raw = new HeadPose(time, headPosition, headRotation, sample.Tracking);

            if (!drivesRig) PlaceTrackedCamera(headPosition, headRotation);

            AnchorTracked = anchorTracked;
            _sensors.Time = time;
            _sensors.DeltaTime = deltaTime;
            _sensors.Head = raw;
            _sensors.RoomActive = room != null;
            _sensors.AnchorTracked = anchorTracked;

            // Depth after the origin has moved, so the image's camera pose is in this frame's world.
            if (_posedDepth != null) FeedCameraPose(headPosition, headRotation);
            DepthFrame depth;
            if (_depth.TryRead(time, out depth))
            {
                _sensors.HasDepth = true;
                _sensors.Depth = depth;
            }
            else
            {
                _sensors.HasDepth = false;
            }

            frame.RawHeadPosition = headPosition;
            frame.RawHeadRotation = headRotation;
            frame.Tracking = raw.Tracking;

            if (!_haveTracking || raw.Tracking != _lastTracking)
            {
                _haveTracking = true;
                _lastTracking = raw.Tracking;
                Action<TrackingState> changed = TrackingChanged;
                if (changed != null) changed(raw.Tracking);
            }

            PoseLogRecorder recorder = Recorder;
            if (recorder != null && recorder.IsRecording) recorder.Record(in raw);
        }

        // ---- step 3 -------------------------------------------------------------------------------------

        public void Filter(ref FrameContext frame)
        {
            HeadPose raw = _sensors.Head;
            _filter.Update(in raw, frame.DeltaTime);

            frame.HeadPosition = raw.Position;
            frame.HeadRotation = raw.Rotation;
            frame.FloorDelta = _filter.Step;
            frame.PoseJumpRejected = _filter.JumpRejected;

            // The eyes may lead the head; the kernel and Bump Guard have already used the unpredicted pose.
            _gyro.Update(raw.Rotation, frame.DeltaTime, _gyroPredictMs,
                raw.Tracking == TrackingState.Normal && !_filter.JumpRejected);

            if (_filter.JumpRejected)
            {
                Action<float> jump = JumpRejectedRaised;
                if (jump != null) jump(_filter.JumpLength);
            }
            if (_filter.LostThisFrame)
            {
                Action lost = TrackingLostRaised;
                if (lost != null) lost();
            }
            if (_filter.RegainedThisFrame)
            {
                Action regained = TrackingRegained;
                if (regained != null) regained();
            }
        }

        // ---- the rig ------------------------------------------------------------------------------------

        void SetDriver(bool enabled, ArSessionRig rig)
        {
            int wanted = enabled ? 1 : 0;
            if (_driverState == wanted) return;
            _driverState = wanted;
            // A replayed or walked head is written by this class: the driver must not write over it.
            if (rig != null && rig.Driver != null) rig.Driver.enabled = enabled;
        }

        /// <summary>Moves the XR Origin so that the session frame (the tracked camera's parent) has this world pose.</summary>
        void PlaceSessionFrame(float3 position, quaternion rotation)
        {
            if (_xrOrigin == null) return;
            float3 originPosition = position;
            quaternion originRotation = rotation;
            if (_sessionSpace != null && _sessionSpace != _xrOrigin)
            {
                float3 offset = _sessionSpace.localPosition;
                quaternion offsetRotation = _sessionSpace.localRotation;
                RoomFrame.ParentFor(position, rotation, offset, offsetRotation, out originPosition, out originRotation);
            }
            // Written only when it changes: moving the origin re-places every trackable below it.
            if (_originWritten && math.all(originPosition == _originPosition) &&
                math.all(originRotation.value == _originRotation.value))
                return;
            _originWritten = true;
            _originPosition = originPosition;
            _originRotation = originRotation;
            _xrOrigin.SetPositionAndRotation(originPosition, originRotation);
        }

        /// <summary>Writes the tracked camera so that the head below it lands on the sample.</summary>
        void PlaceTrackedCamera(float3 headPosition, quaternion headRotation)
        {
            if (_trackedCamera == null) return;
            float3 cameraPosition = headPosition;
            quaternion cameraRotation = headRotation;
            if (_head != null && _head != _trackedCamera)
            {
                float3 headLocal = _head.localPosition;
                quaternion headLocalRotation = _head.localRotation;
                RoomFrame.ParentFor(headPosition, headRotation, headLocal, headLocalRotation,
                    out cameraPosition, out cameraRotation);
            }
            _trackedCamera.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        void FeedCameraPose(float3 headPosition, quaternion headRotation)
        {
            if (_trackedCamera != null)
            {
                _trackedCamera.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                _posedDepth.SetCameraPose(position, rotation);
            }
            else
            {
                _posedDepth.SetCameraPose(headPosition, headRotation);
            }
        }
    }
}
