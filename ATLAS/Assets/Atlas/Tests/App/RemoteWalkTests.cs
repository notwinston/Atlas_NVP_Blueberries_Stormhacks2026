using System;
using System.Collections.Generic;
using Atlas.Input;
using Atlas.Link;
using Atlas.Scan;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The Station's Map page drives the walker head (OperatorCmd walk): the router keeps the input, the input
    /// step hands it to the walker, and an input the Console stops repeating runs out by itself.
    /// </summary>
    public class RemoteWalkTests
    {
        const float Dt = 1f / 60f;

        /// <summary>No devices: the walker's own input is always zero.</summary>
        sealed class NoInput : IAtlasInput
        {
            public void Tick(Vector3 headPosition, Quaternion headRotation, float deltaTime) { }
            public InputContext Context { get { return default(InputContext); } }
            public void PushContext(InputContext context) { }
            public void PopContext(InputContext context) { }
            public bool Pressed(AtlasAction action) { return false; }
            public bool Held(AtlasAction action) { return false; }
            public float Axis(AtlasAxis axis) { return 0f; }
            public InputSource LastSource(AtlasAction action) { return default(InputSource); }
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
            public bool GamepadPresent { get { return false; } }
            public void SetRumble(float lowFrequency, float highFrequency) { }
            public WalkerInput Walker { get { return default(WalkerInput); } }
        }

        /// <summary>A head that stands still: any source that is not the walker.</summary>
        sealed class StillSource : IPoseSource
        {
            public string Name { get { return "still"; } }
            public PoseFrameKind Frame { get { return PoseFrameKind.Room; } }
            public bool DrivesRig { get { return false; } }
            public bool TryRead(double time, float deltaTime, out HeadPose pose)
            {
                pose = new HeadPose(time, new float3(0f, 1.6f, 0f), quaternion.identity, TrackingState.Normal);
                return true;
            }
        }

        sealed class Rig
        {
            public readonly RemoteWalkInput Remote = new RemoteWalkInput();
            public readonly FakePipeline Pipeline = new FakePipeline();
            public readonly KeyboardWalkerPoseSource Walker = new KeyboardWalkerPoseSource();
            public readonly InputSenseStep Step;
            double _time;

            public Rig()
            {
                Pipeline.Source = Walker;
                Step = new InputSenseStep(new NoInput(), Pipeline, Remote);
            }

            /// <summary>Frames as the loop runs them: the source is read, then the input step feeds the next sample.</summary>
            public HeadPose Run(int frames)
            {
                HeadPose pose = default(HeadPose);
                for (int i = 0; i < frames; i++)
                {
                    _time += Dt;
                    var frame = new FrameContext { Time = _time, DeltaTime = Dt };
                    if (Pipeline.Source != null) Pipeline.Source.TryRead(_time, Dt, out pose);
                    Step.Sense(ref frame);
                }
                return pose;
            }
        }

        [Test]
        public void A_held_input_walks_the_walker_forward_at_its_speed()
        {
            var rig = new Rig();
            rig.Remote.Set(0f, 1f, 0f, 1f);
            HeadPose pose = rig.Run(31);     // 30 integrated steps: the first read comes before any input
            Assert.That(pose.Position.z, Is.EqualTo(0.5f).Within(0.02f), "half a second at 1 m/s, facing +Z");
            Assert.That(pose.Position.x, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void An_input_that_is_not_repeated_runs_out_and_the_walker_stops()
        {
            var rig = new Rig();
            rig.Remote.Set(0f, 1f, 0f, 1f);
            float stopped = rig.Run(60).Position.z;
            Assert.That(rig.Remote.Active, Is.False);
            Assert.That(stopped, Is.EqualTo(OperatorCommand.WalkHoldSeconds).Within(0.04f), "it walks for the hold, then stops");
            Assert.That(rig.Run(60).Position.z, Is.EqualTo(stopped), "and stays stopped");
        }

        [Test]
        public void A_still_input_stops_the_walker_at_once()
        {
            var rig = new Rig();
            rig.Remote.Set(0f, 1f, 0f, 1f);
            rig.Run(11);
            rig.Remote.Set(0f, 0f, 0f, 1f);
            Assert.That(rig.Remote.Active, Is.False);
            float z = rig.Run(1).Position.z;
            Assert.That(rig.Run(30).Position.z, Is.EqualTo(z));
        }

        [Test]
        public void A_turn_input_turns_the_walker_and_strafing_follows_its_heading()
        {
            var rig = new Rig();
            rig.Remote.Set(0f, 0f, 90f, 1f);
            rig.Run(31);
            Assert.That(math.degrees(rig.Walker.Yaw), Is.EqualTo(45f).Within(1f), "half a second at 90 degrees a second");

            rig.Remote.Set(1f, 0f, 0f, 1f);     // right, at a heading of 45 degrees: towards +X and -Z
            HeadPose before = rig.Run(1);
            HeadPose after = rig.Run(30);
            float2 moved = after.Floor - before.Floor;
            Assert.That(moved.x, Is.GreaterThan(0.3f));
            Assert.That(moved.y, Is.LessThan(-0.3f));
        }

        [Test]
        public void The_input_runs_down_while_another_source_is_chosen()
        {
            var rig = new Rig();
            rig.Pipeline.Source = new StillSource();
            rig.Remote.Set(0f, 1f, 0f, 1f);
            rig.Run(60);
            Assert.That(rig.Remote.Active, Is.False);

            rig.Pipeline.Source = rig.Walker;
            Assert.That(rig.Run(30).Position.z, Is.EqualTo(0f), "an old input does not move a walker chosen later");
        }

        [Test]
        public void The_router_hands_the_walk_command_to_the_remote_input()
        {
            var remote = new RemoteWalkInput();
            var router = new CommandRouter(new FakeHost(), new FakeLoop(), new ParamRegistry(), null, () => { }, remote);
            router.Walk(-0.5f, 2f, 30f, 0.7f);
            Assert.That(remote.Active, Is.True);
            Assert.That(remote.Move, Is.EqualTo(new float2(-0.5f, 1f)), "clamped as the link clamps it");
            Assert.That(remote.Turn, Is.EqualTo(30f));

            // With nowhere to put it the input is ignored.
            Assert.DoesNotThrow(() => new CommandRouter(new FakeHost(), new FakeLoop(), new ParamRegistry()).Walk(0f, 1f, 0f, 1f));
        }
    }
}
