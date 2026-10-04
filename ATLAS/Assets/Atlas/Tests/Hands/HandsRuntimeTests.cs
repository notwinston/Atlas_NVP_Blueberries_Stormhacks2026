using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The hands' frame work end to end without the engine (documents/ATLAS_Hands_Design.md section 3.3): a
    /// scripted source, the real lift, filter, gate, visibility, gestures and mask, and fakes for what the app
    /// supplies.
    /// </summary>
    public class HandsRuntimeTests
    {
        const float FrameSeconds = 1f / 60f;

        // ---- fakes -------------------------------------------------------------------------------------------

        sealed class FakeInput : IAtlasInput, IHandsInput
        {
            public bool Pad;
            public HandsInput Last;
            public int SetCalls;
            HandsTouchState _touch;

            public void SetHands(in HandsInput hands) { Last = hands; SetCalls++; }
            public ref readonly HandsTouchState HandsTouch { get { return ref _touch; } }
            public string LabelOf(GazeTargetId id) { return null; }
            public int TagOf(GazeTargetId id) { return 0; }

            public bool Pressed(AtlasAction action) { return false; }
            public float Axis(AtlasAxis axis) { return 0f; }
            public bool Held(AtlasAction action) { return false; }
            public InputContext Context { get { return InputContext.Space; } }
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime) { }
            public void PushContext(InputContext context) { }
            public void PopContext(InputContext context) { }
            public InputSource LastSource(AtlasAction action) { return InputSource.None; }
            public int LastTag(AtlasAction action) { return 0; }
            public event Action<AtlasAction, InputSource, int> ActionFired { add { } remove { } }
            public float DwellSeconds { get; set; }
            public GazeTargetId RegisterGazeTarget(in GazeTarget target) { return default(GazeTargetId); }
            public void MoveGazeTarget(GazeTargetId id, Vector3 centre) { }
            public void SetGazeTargetEnabled(GazeTargetId id, bool enabled) { }
            public void UnregisterGazeTarget(GazeTargetId id) { }
            public GazeState Gaze { get { return default(GazeState); } }
            public void CopyActionsWithoutGazeTwin(List<AtlasAction> into) { }
            public bool TryGetTap(out Vector2 screenPosition) { screenPosition = default(Vector2); return false; }
            public bool GamepadPresent { get { return Pad; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        // A detector that sees one right hand held still in front of the camera, back of the hand to the camera.
        sealed class ScriptSource : IHandsSource
        {
            public bool IsAvailable = true;
            public bool Throw;
            public int Rate;
            public int Stops;
            public float3 Wrist = new float3(0.04f, -0.10f, 0.45f);
            double _last = double.NegativeInfinity;
            int _sequence;

            public bool Available { get { return IsAvailable; } }
            public string Name { get { return "script"; } }
            public int Fault { get { return 0; } }
            public void SetRate(int hz) { Rate = hz; }
            public void Stop() { Rate = 0; Stops++; }

            public JointSet CameraJoints()
            {
                // Palm frame to camera: fingers up, the palm's normal away from the camera, so the thumb is at the left.
                var rotation = new quaternion(new float3x3(new float3(-1f, 0f, 0f), new float3(0f, 0f, 1f), new float3(0f, 1f, 0f)));
                return HandModel.RestPose(HandSide.Right).Transformed(rotation, Wrist);
            }

            public bool TryRead(double time, ref HandsDetection detection)
            {
                if (Throw) throw new InvalidOperationException("scripted fault");
                if (Rate <= 0 || time - _last < 1.0 / Rate - 1e-6) return false;
                _last = time;
                HandsSynth.Begin(ref detection, ++_sequence, 1280, 720, 880f, 880f, 640f, 360f, 256, 144,
                    time - 0.08, time, float4x4.identity);
                HandsSynth.AddHand(ref detection, CameraJoints(), 1);
                return true;
            }
        }

        sealed class FakeEvents : IEventLog
        {
            public readonly List<string> Names = new List<string>();
            public readonly List<string> Data = new List<string>();
            public void Log(string ev) { Names.Add(ev); Data.Add(""); }
            public void Log(string ev, string key, double value) { Names.Add(ev); Data.Add(key + "=" + value); }
            public void Log(string ev, string key, string value) { Names.Add(ev); Data.Add(key + "=" + value); }
            public void Log(string ev, string key1, double value1, string key2, double value2) { Names.Add(ev); Data.Add(key1 + "," + key2); }
            public void LogJson(string ev, string dataJson) { Names.Add(ev); Data.Add(dataJson); }
            public int Count(string ev) { int n = 0; for (int i = 0; i < Names.Count; i++) if (Names[i] == ev) n++; return n; }
        }

        sealed class FakePipeline : IPosePipeline
        {
            public SensorFrame Frame;
            public IPoseSource Source { get; set; }
            public IDepthSource Depth { get; set; }
            public RoomModel Room { get; set; }
            public ref readonly SensorFrame Sensors { get { return ref Frame; } }
            public HeadPose Raw { get { return Frame.Head; } }
            public HeadPose Geometry { get { return Frame.Head; } }
            public float2 Step { get { return float2.zero; } }
            public float2 PathFloor { get { return float2.zero; } }
            public bool JumpRejected { get { return false; } }
            public int JumpCount { get { return 0; } }
            public bool TrackingLost { get { return false; } }
            public void ResetGeometry() { }
            public float GyroPredictMs { get; set; }
            public quaternion PredictionRotation { get { return quaternion.identity; } }
            public event Action<TrackingState> TrackingChanged { add { } remove { } }
            public event Action TrackingLostRaised { add { } remove { } }
            public event Action TrackingRegained { add { } remove { } }
            public event Action<float> JumpRejectedRaised { add { } remove { } }
        }

        sealed class Rig
        {
            public readonly ScriptSource Source = new ScriptSource();
            public readonly FakeInput Input = new FakeInput();
            public readonly FakeEvents Events = new FakeEvents();
            public readonly FakePipeline Pipeline = new FakePipeline();
            public readonly HandsRuntime Runtime;
            public bool Headset = true, Warning;
            public double Time;
            public int FrameIndex;
            readonly float[] _depth = new float[64 * 36];

            public Rig()
            {
                Runtime = new HandsRuntime(new HandsDeps
                {
                    Source = Source,
                    Input = Input,
                    Pipeline = Pipeline,
                    Headset = () => Headset,
                    Warning = () => Warning,
                    HeadFromCamera = () => true,
                    Events = () => Events,
                });
                Runtime.Enabled = true;
            }

            public void Step(bool withDepth = false)
            {
                Time += FrameSeconds;
                var frame = new FrameContext
                {
                    FrameIndex = FrameIndex++,
                    Time = Time,
                    DeltaTime = FrameSeconds,
                    RawHeadRotation = quaternion.identity,
                    HeadRotation = quaternion.identity,
                    Tracking = TrackingState.Normal,
                    Tier = QualityTier.Nominal,
                };
                Pipeline.Frame.HasDepth = withDepth;
                if (withDepth)
                {
                    Pipeline.Frame.Depth = new DepthFrame
                    {
                        Depth = _depth, Width = 64, Height = 36, Fx = 44f, Fy = 44f, Cx = 32f, Cy = 18f,
                        Rotation = quaternion.identity, Time = Time, Sequence = FrameIndex,
                    };
                }
                Runtime.Sense(ref frame);
                Runtime.AfterFrame(in frame);
            }

            public void Run(float seconds, bool withDepth = false)
            {
                int frames = (int)math.round(seconds / FrameSeconds);
                for (int i = 0; i < frames; i++) Step(withDepth);
            }
        }

        // ---- tests -------------------------------------------------------------------------------------------

        [Test]
        public void Runtime_IsOffUntilEnabled_AndOnInAHandsSession()
        {
            var rig = new Rig();
            rig.Runtime.Enabled = false;
            rig.Run(1.5f);
            Assert.That(rig.Runtime.Active, Is.False);
            Assert.That(rig.Source.Rate, Is.EqualTo(0));

            rig.Runtime.Enabled = true;
            rig.Run(1.5f);
            Assert.That(rig.Runtime.Active, Is.True);
            Assert.That(rig.Source.Rate, Is.EqualTo(HandsConstants.RateFull));
            Assert.That(rig.Events.Count("hands.on"), Is.EqualTo(1));
        }

        [Test]
        public void Runtime_AHandIsTracked_Drawn_AndHandedToInput()
        {
            var rig = new Rig();
            rig.Run(2f);

            ref readonly HandPose right = ref rig.Runtime.Pose(HandSide.Right);
            Assert.That(right.Tracked, Is.True);
            Assert.That(right.Opacity, Is.GreaterThan(0.9f));
            Assert.That(rig.Runtime.Pose(HandSide.Left).Tracked, Is.False);

            // The camera is the world here, so the joints come back where the script put them.
            float3 expected = rig.Source.CameraJoints()[HandJoint.IndexTip];
            float3 seen = rig.Runtime.Joint(HandSide.Right, HandJoint.IndexTip);
            Assert.That(math.distance(seen, expected), Is.LessThan(0.03f), "index fingertip, metres");

            Assert.That(rig.Input.Last.Active, Is.True);
            Assert.That(rig.Input.Last.Right.Valid, Is.True);
            Assert.That(rig.Input.Last.Left.Valid, Is.False);
            Assert.That(math.distance(rig.Input.Last.Right.World, expected), Is.LessThan(0.035f));
            Assert.That(rig.Events.Count("hands.enter"), Is.EqualTo(1));
        }

        [Test]
        public void Runtime_AGamepadSwitchesHandsOff()
        {
            var rig = new Rig();
            rig.Run(2f);
            rig.Input.Pad = true;
            // The gamepad must hold a second before the gate follows it, and the panels go back a second after that.
            rig.Run(HandsConstants.GamepadHoldSeconds + 2.5f);

            Assert.That(rig.Runtime.Active, Is.False);
            Assert.That(rig.Source.Rate, Is.EqualTo(0));
            Assert.That(rig.Runtime.Pose(HandSide.Right).Opacity, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(rig.Input.Last.Active, Is.False);
            Assert.That(rig.Events.Count("hands.off"), Is.EqualTo(1));
            Assert.That(rig.Events.Count("hands.stats"), Is.EqualTo(1));
            Assert.That(((IReachLayout)rig.Runtime).Near, Is.False);
        }

        [Test]
        public void Runtime_ABumpGuardWarningHidesTheHands_AndSuspendsTouch()
        {
            var rig = new Rig();
            rig.Run(2f);
            rig.Warning = true;
            rig.Run(0.3f, withDepth: true);

            Assert.That(rig.Runtime.Pose(HandSide.Right).Opacity, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(rig.Input.Last.Active, Is.False);
            // Hidden hands are still in front of the LiDAR: the mask is still offered.
            DepthMask mask;
            Assert.That(rig.Runtime.TryGetMask(rig.Pipeline.Frame.Depth.Sequence, out mask), Is.True);
            Assert.That(mask.Count, Is.GreaterThanOrEqualTo(1));

            rig.Warning = false;
            rig.Run(0.6f);
            Assert.That(rig.Runtime.Pose(HandSide.Right).Opacity, Is.GreaterThan(0.9f));
        }

        [Test]
        public void Runtime_TheMaskBelongsToTheDepthImageOfItsFrame()
        {
            var rig = new Rig();
            rig.Run(2f, withDepth: true);
            int sequence = rig.Pipeline.Frame.Depth.Sequence;

            DepthMask mask;
            Assert.That(rig.Runtime.TryGetMask(sequence, out mask), Is.True);
            Assert.That(mask.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(mask.DepthSequence, Is.EqualTo(sequence));
            Assert.That(mask.Session, Is.EqualTo(1));

            // Another image: the session is still reported, the regions are not.
            Assert.That(rig.Runtime.TryGetMask(sequence + 1, out mask), Is.True);
            Assert.That(mask.Count, Is.EqualTo(0));
            Assert.That(mask.Session, Is.EqualTo(1));

            rig.Runtime.Enabled = false;
            rig.Run(0.5f, withDepth: true);
            Assert.That(rig.Runtime.TryGetMask(rig.Pipeline.Frame.Depth.Sequence, out mask), Is.False);
            Assert.That(mask.Session, Is.EqualTo(0));
        }

        [Test]
        public void Runtime_AFaultSwitchesHandsOff_AndLeavesNothingBehind()
        {
            var rig = new Rig();
            rig.Run(2f, withDepth: true);
            rig.Source.Throw = true;
            Assert.DoesNotThrow(() => rig.Run(0.5f, withDepth: true));

            Assert.That(rig.Runtime.Faulted, Is.True);
            Assert.That(rig.Runtime.Active, Is.False);
            Assert.That(rig.Runtime.Pose(HandSide.Right).Tracked, Is.False);
            Assert.That(rig.Input.Last.Active, Is.False);
            DepthMask mask;
            Assert.That(rig.Runtime.TryGetMask(rig.Pipeline.Frame.Depth.Sequence, out mask), Is.False);
            Assert.That(rig.Events.Count("hands.fault"), Is.EqualTo(1));
        }

        [Test]
        public void Runtime_LeavingTheHeadsetStopsTheDetector()
        {
            var rig = new Rig();
            rig.Run(2f);
            rig.Headset = false;
            rig.Run(0.5f);
            Assert.That(rig.Runtime.Active, Is.False);
            Assert.That(rig.Source.Rate, Is.EqualTo(0));
            Assert.That(rig.Source.Stops, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void Runtime_LogsEdgesOnly()
        {
            var rig = new Rig();
            rig.Run(4f);
            Assert.That(rig.Events.Count("hands.on"), Is.EqualTo(1));
            Assert.That(rig.Events.Count("hands.enter"), Is.EqualTo(1));
            Assert.That(rig.Events.Names.Count, Is.LessThan(6), "nothing is logged per frame");
        }

        [Test]
        public void Runtime_SteadyStateAllocatesNothing()
        {
            var rig = new Rig();
            rig.Run(2f, withDepth: true);
            rig.Step(withDepth: true);      // the first direct call from this method is compiled here, not measured
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) rig.Step(withDepth: true);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0));
        }
    }
}
