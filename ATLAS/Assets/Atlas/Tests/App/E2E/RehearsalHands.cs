using System;
using Atlas.Hands;
using Atlas.Scan;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.E2E.Tests
{
    /// <summary>
    /// The phone's hand detector with no phone: a test says where the walker's hands are in the camera's own
    /// space each frame, and this hands them to the app as a perfect detector and a perfect LiDAR would
    /// (<see cref="HandsSynth"/>), late, as the phone does. What the camera sees in a frame is kept with that
    /// frame's camera pose and handed over <see cref="AgeFrames"/> frames later, at the rate the app asked for;
    /// so a result carries the pose of the frame it was captured in, and a hand held still before a walking
    /// camera arrives where it was, not where the camera is now (documents/ATLAS_Hands_Design.md section 3.4).
    /// The picture has the field of view of the rehearsal's depth image. Allocates nothing per frame.
    /// </summary>
    sealed class ScriptedHandsSource : IHandsSource
    {
        public const int ImageWidth = 1280, ImageHeight = 960;
        /// <summary>Pixels: the focal length that gives the picture the depth image's field of view.</summary>
        public static readonly float FocalPixels = 0.5f * ImageWidth / math.tan(0.5f * SyntheticDepthSource.HorizontalFov);
        /// <summary>Frames from the capture of a picture to the poll that hands its result over: 33 ms at 60 frames a second.</summary>
        public const int DefaultAgeFrames = 2;
        const int History = 16;
        // Seconds. The clock is a count of frames and a rate's interval is a whole number of them.
        const double ClockSlack = 1e-6;

        struct Seen
        {
            public double Time;
            public float4x4 CameraToSession;
            public bool HasLeft, HasRight;
            public JointSet Left, Right;
        }

        readonly IPosePipeline _pipeline;
        readonly Seen[] _ring = new Seen[History];
        int _next, _count;
        double _lastResult = double.NegativeInfinity;
        int _sequence;
        bool _hasLeft, _hasRight;
        JointSet _left, _right;

        /// <param name="pipeline">Where the camera is in each frame: the raw head of the pose pipeline's sense step, which runs first.</param>
        public ScriptedHandsSource(IPosePipeline pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException("pipeline");
            _pipeline = pipeline;
        }

        /// <summary>False: a phone with nothing to detect with.</summary>
        public bool IsAvailable = true;
        /// <summary>See <see cref="DefaultAgeFrames"/>. At most 15.</summary>
        public int AgeFrames = DefaultAgeFrames;
        /// <summary>The detector's confidence in every joint it reports.</summary>
        public float Confidence = 0.95f;
        /// <summary>The rate the app last asked for; 0 while stopped or idle.</summary>
        public int Rate { get; private set; }
        /// <summary>Times the app stopped the detector, and results handed over, since the start.</summary>
        public int Stops { get; private set; }
        public int Results { get; private set; }

        /// <summary>The walker holds this hand before the camera from the next frame on: 21 joints in the camera's space.</summary>
        public void Show(HandSide side, in JointSet cameraJoints)
        {
            if (side == HandSide.Left)
            {
                _hasLeft = true;
                _left = cameraJoints;
            }
            else
            {
                _hasRight = true;
                _right = cameraJoints;
            }
        }

        /// <summary>The hand is out of the camera's view from the next frame on.</summary>
        public void Hide(HandSide side)
        {
            if (side == HandSide.Left) _hasLeft = false;
            else _hasRight = false;
        }

        public bool Available { get { return IsAvailable; } }
        public string Name { get { return "script"; } }
        public int Fault { get { return 0; } }

        public void SetRate(int hz)
        {
            Rate = hz > 0 ? hz : 0;
        }

        public void Stop()
        {
            Rate = 0;
            Stops++;
            // A stopped detector holds no frame: what it saw before is not handed over when it starts again.
            _count = 0;
            _lastResult = double.NegativeInfinity;
        }

        public bool TryRead(double time, ref HandsDetection detection)
        {
            // What the camera sees in this frame, with the pose it sees it from.
            ref readonly SensorFrame sensors = ref _pipeline.Sensors;
            ref Seen now = ref _ring[_next];
            now.Time = time;
            now.CameraToSession = float4x4.TRS(sensors.Head.Position, sensors.Head.Rotation, new float3(1f));
            now.HasLeft = _hasLeft;
            now.HasRight = _hasRight;
            now.Left = _left;
            now.Right = _right;
            _next = (_next + 1) % History;
            if (_count < History) _count++;

            int age = math.clamp(AgeFrames, 0, History - 1);
            if (Rate <= 0 || _count <= age) return false;
            if (time - _lastResult < 1.0 / Rate - ClockSlack) return false;
            _lastResult = time;

            ref Seen seen = ref _ring[(_next - 1 - age + 2 * History) % History];
            HandsSynth.Begin(ref detection, ++_sequence, ImageWidth, ImageHeight, FocalPixels, FocalPixels,
                0.5f * ImageWidth, 0.5f * ImageHeight, SyntheticDepthSource.ImageWidth, SyntheticDepthSource.ImageHeight,
                seen.Time, time, in seen.CameraToSession);
            if (seen.HasLeft) HandsSynth.AddHand(ref detection, in seen.Left, (int)HandSide.Left, Confidence);
            if (seen.HasRight) HandsSynth.AddHand(ref detection, in seen.Right, (int)HandSide.Right, Confidence);
            Results++;
            return true;
        }
    }

    /// <summary>
    /// A pose source as the phone's AR camera delivers it: the samples of another source, from a source that
    /// says it drives the rig. The hands' gate asks exactly that of the pose pipeline's source (decision R8): with
    /// a replayed head the camera no longer matches the view, and hands stay off.
    /// </summary>
    sealed class CameraPoseSource : IPoseSource
    {
        readonly IPoseSource _inner;

        public CameraPoseSource(IPoseSource inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
        }

        public string Name { get { return "arcamera"; } }
        public PoseFrameKind Frame { get { return _inner.Frame; } }
        public bool DrivesRig { get { return true; } }
        public bool TryRead(double time, float deltaTime, out HeadPose pose) { return _inner.TryRead(time, deltaTime, out pose); }
    }

    /// <summary>
    /// Step 8b of the hands, as Atlas.Hands.HandsAfterFrameStep is in the app, less the sounds, which need the
    /// engine: the edge events, behind the same net (decision R9: the frame loop has none under its after-frame
    /// hooks, so an exception switches the hands off for the session and the frame carries on).
    /// </summary>
    sealed class RehearsalHandsAfterFrame : IAfterFrameStep
    {
        readonly HandsRuntime _runtime;

        public RehearsalHandsAfterFrame(HandsRuntime runtime) { _runtime = runtime; }

        public void AfterFrame(in FrameContext frame)
        {
            if (_runtime.Faulted) return;
            try
            {
                _runtime.AfterFrame(in frame);
            }
            catch (Exception e)
            {
                _runtime.ReportFault("after", e);
            }
        }
    }
}
