using System;
using Atlas.Scan;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// Frame-loop steps 1 and 3 with no engine. Atlas.Scan.PosePipeline does the same work but also moves the
    /// XR Origin and the tracked camera, which needs Unity; this class keeps its order line for line and uses
    /// the same engine-free parts (the pose source, <see cref="RoomFrame"/>, the depth source and
    /// <see cref="PoseFilter"/>), so a rehearsal with no Unity feeds Bump Guard, the kernel and the spaces
    /// what the phone would feed them.
    /// </summary>
    sealed class RehearsalPosePipeline : IPosePipeline, ISenseStep, IPoseFilterStep
    {
        readonly PoseFilter _filter = new PoseFilter();
        IPoseSource _source;
        IDepthSource _depth = new NullDepthSource();
        IPosedDepthSource _posedDepth;
        RoomModel _room;
        SensorFrame _sensors;
        HeadPose _lastSample = HeadPose.Identity;
        TrackingState _lastTracking = TrackingState.NotTracking;
        bool _haveTracking;

        public RehearsalPosePipeline()
        {
            _sensors.Head = HeadPose.Identity;
        }

        public IPoseSource Source
        {
            get { return _source; }
            set
            {
                if (ReferenceEquals(_source, value)) return;
                _source = value;
                // A different source is a different place: the first displacement after the change is not a step.
                _filter.Reset();
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

        public ref readonly SensorFrame Sensors { get { return ref _sensors; } }
        public HeadPose Raw { get { return _sensors.Head; } }
        public HeadPose Geometry { get { return _filter.Geometry; } }
        public float2 Step { get { return _filter.Step; } }
        public float2 PathFloor { get { return _filter.PathFloor; } }
        public bool JumpRejected { get { return _filter.JumpRejected; } }
        public float JumpLength { get { return _filter.JumpLength; } }
        public int JumpCount { get { return _filter.JumpCount; } }
        public bool TrackingLost { get { return _filter.TrackingLost; } }
        public int GeometryResets { get; private set; }

        public void ResetGeometry()
        {
            GeometryResets++;
            _filter.Reset();
        }

        public float GyroPredictMs { get; set; }
        public quaternion PredictionRotation { get { return quaternion.identity; } }

        public event Action<TrackingState> TrackingChanged;
        public event Action TrackingLostRaised;
        public event Action TrackingRegained;
        public event Action<float> JumpRejectedRaised;

        // ---- step 1 ---------------------------------------------------------------------------------------

        public void Sense(ref FrameContext frame)
        {
            double time = frame.Time;
            float deltaTime = frame.DeltaTime;
            IPoseSource source = _source;

            HeadPose sample;
            if (source != null && source.TryRead(time, deltaTime, out sample))
                _lastSample = sample;
            else
                // No sample this frame: the head stays where it was, and is not tracked.
                sample = new HeadPose(time, _lastSample.Position, _lastSample.Rotation, TrackingState.NotTracking);

            float3 sessionPosition = float3.zero;
            quaternion sessionRotation = quaternion.identity;
            RoomModel room = _room;
            bool sessionFrame = source == null || source.Frame == PoseFrameKind.Session;
            if (room != null && sessionFrame)
            {
                RoomFrame.SessionToRoom(room.Anchor.Position, quaternion.RotateY(room.Anchor.Yaw),
                    out sessionPosition, out sessionRotation);
            }

            float3 headPosition;
            quaternion headRotation;
            RoomFrame.Compose(sessionPosition, sessionRotation, sample.Position, sample.Rotation,
                out headPosition, out headRotation);
            var raw = new HeadPose(time, headPosition, headRotation, sample.Tracking);

            _sensors.Time = time;
            _sensors.DeltaTime = deltaTime;
            _sensors.Head = raw;
            _sensors.RoomActive = room != null;
            _sensors.AnchorTracked = true;      // a replay or a walker has no anchor to lose

            if (_posedDepth != null) _posedDepth.SetCameraPose(headPosition, headRotation);
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
        }

        // ---- step 3 ---------------------------------------------------------------------------------------

        public void Filter(ref FrameContext frame)
        {
            HeadPose raw = _sensors.Head;
            _filter.Update(in raw, frame.DeltaTime);

            frame.HeadPosition = raw.Position;
            frame.HeadRotation = raw.Rotation;
            frame.FloorDelta = _filter.Step;
            frame.PoseJumpRejected = _filter.JumpRejected;

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
    }

    /// <summary>A head that stands still, tracked, in the room frame: the walker between two walks.</summary>
    sealed class StandingPoseSource : IPoseSource
    {
        public float3 Position = new float3(0f, 1.6f, 0f);
        public quaternion Rotation = quaternion.identity;
        public TrackingState Tracking = TrackingState.Normal;
        /// <summary>
        /// The head is the phone's AR camera, which drives the rig; false (the default) is a head from somewhere
        /// else, as a replay's is. The hands' gate asks which it is (decision R8).
        /// </summary>
        public bool Camera;

        public string Name { get { return "standing"; } }
        public PoseFrameKind Frame { get { return PoseFrameKind.Room; } }
        public bool DrivesRig { get { return Camera; } }

        public bool TryRead(double time, float deltaTime, out HeadPose pose)
        {
            pose = new HeadPose(time, Position, Rotation, Tracking);
            return true;
        }
    }
}
