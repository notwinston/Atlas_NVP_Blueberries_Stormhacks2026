using System;
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.App.Tests
{
    /// <summary>
    /// The frame loop's order (Master Plan section 6.3, contract README F5) with stand-ins for every hook and
    /// the real flat kernel.
    /// </summary>
    public class FrameLoopTests
    {
        const float Dt = 1f / 60f;

        sealed class Rig
        {
            public readonly List<string> Log = new List<string>();
            public readonly FlatWalkerKernel Kernel = new FlatWalkerKernel();
            public readonly FrameLoopCore Loop;
            public readonly FakeSense Pose;
            public readonly FakeGuard Guard;
            public readonly FakeSink Sink;
            public double Time;

            public Rig(bool withFilter)
            {
                Kernel.Configure(KernelConfig.Flat(2.0));
                Loop = new FrameLoopCore(Kernel);
                Pose = new FakeSense(Log, "pose");
                Guard = new FakeGuard(Log);
                Sink = new FakeSink(Log);
                Loop.AddSense(Pose);
                Loop.AddSense(new FakeSense(Log, "input") { WritesPose = false });
                Loop.SetGuard(Guard);
                if (withFilter) Loop.SetPoseFilter(new FakeFilter(Log));
                Loop.Space = new FakeSpaceFrame(Log);
                Loop.AddUpload(new FakeUpload(Log, "look"));
                Loop.AddUpload(new FakeUpload(Log, "rig"));
                Loop.SetStateSink(Sink);
                Loop.AddAfterFrame(new FakeAfter(Log, "probe"));
                Loop.AddAfterFrame(new FakeAfter(Log, "link"));
                Loop.AddAfterFrame(new FakeAfter(Log, "spacelab"));
            }

            public void Frame()
            {
                Time += Dt;
                Loop.RunUpdate(Time, Dt);
                Loop.RunState();
                Loop.RunAfterFrame();
            }
        }

        [Test]
        public void Frame_Order_BumpGuardBeforeEverything()
        {
            var rig = new Rig(true);
            rig.Frame();
            Assert.That(rig.Log, Is.EqualTo(new[]
            {
                "pose", "input",                    // 1
                "guard",                            // 2
                "filter",                           // 3
                "space.tick",                       // 5 (4 is the kernel)
                "space.upload", "look", "rig",      // 6
                "space.state", "sink",              // 8a
                "probe", "link", "spacelab"         // 8b
            }));
            // Bump Guard has run before the pose filter wrote the geometry pose of this frame.
            Assert.That(rig.Guard.SeenGeometry, Is.EqualTo(float3.zero));
            Assert.That(rig.Guard.SeenRaw, Is.EqualTo(new float3(0f, 1.6f, 0f)));
        }

        [Test]
        public void Kernel_SteppedOncePerFrame_AlsoWithNoSpaceAndTrackingLost()
        {
            var rig = new Rig(true);
            rig.Loop.Space = null;
            long before = rig.Kernel.StepCount;
            for (int i = 0; i < 10; i++)
            {
                rig.Pose.Tracking = i < 5 ? TrackingState.Normal : TrackingState.NotTracking;
                rig.Frame();
            }
            Assert.That(rig.Kernel.StepCount - before, Is.EqualTo(10));
            Assert.That(rig.Loop.Current.Kernel.Rejected, Is.True, "a frame with tracking lost integrates nothing");
        }

        [Test]
        public void Kernel_ReceivesTheHeadFloorPoint_NotTheDisplacement()
        {
            var rig = new Rig(true);
            rig.Frame();
            for (int i = 1; i <= 80; i++)
            {
                rig.Pose.Position = new float3(0.01f * i, 1.6f, 0f);
                rig.Frame();
            }
            // 80 steps of 1 cm: the walker is 0.8 m from where it started, inside tile zero.
            Assert.That(rig.Loop.Current.Walker.Local.M02, Is.EqualTo(0.8).Within(1e-4));
            Assert.That(rig.Loop.Current.Walker.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(rig.Loop.Current.FloorDelta.x, Is.EqualTo(0.01f).Within(1e-5f));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Safety_Independence_PoseJump_ReachesBumpGuard_GeometryPoseUnchanged(bool withFilter)
        {
            var rig = new Rig(withFilter);
            for (int i = 0; i <= 20; i++)
            {
                rig.Pose.Position = new float3(0.01f * i, 1.6f, 0f);
                rig.Frame();
            }
            WalkerPose before = rig.Kernel.Pose;

            // The tracker relocalises: the raw head jumps 0.25 m in one frame.
            var jumped = new float3(0.2f + 0.25f, 1.6f, 0f);
            rig.Pose.Position = jumped;
            rig.Frame();

            Assert.That(rig.Guard.SeenRaw, Is.EqualTo(jumped), "Bump Guard works from the raw pose, jump included");
            Assert.That(rig.Loop.Current.RawHeadPosition, Is.EqualTo(jumped));
            Assert.That(rig.Loop.Current.Kernel.Rejected, Is.True);
            Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(before.Tile));
            Assert.That(rig.Kernel.Pose.Local.Equals(before.Local), Is.True, "the geometry pose is bit for bit as it was");
            Assert.That(rig.Loop.Current.Walker.Local.Equals(before.Local), Is.True);
            Assert.That(rig.Sink.Last.PoseJumpRejected, Is.True, "State.flags bit 2");

            // Walking goes on from the new place: the jump is dropped once, not carried.
            rig.Pose.Position = jumped + new float3(0.01f, 0f, 0f);
            rig.Frame();
            Assert.That(rig.Loop.Current.Kernel.Rejected, Is.False);
            Assert.That(rig.Kernel.Pose.Local.M02 - before.Local.M02, Is.EqualTo(0.01).Within(1e-5));
            Assert.That(rig.Sink.Last.PoseJumpRejected, Is.False);
        }

        [Test]
        public void RoomReset_IsLatched_UntilTheHeadIsInTheRoomFrame()
        {
            var rig = new Rig(true);
            var pipeline = new FakePipeline { Log = rig.Log };
            rig.Loop.Pipeline = pipeline;
            rig.Pose.Position = new float3(5f, 1.6f, 7f);          // AR session frame
            rig.Frame();

            rig.Loop.LatchRoomReset();
            rig.Frame();
            Assert.That(rig.Loop.RoomResetPending, Is.True, "the head is still in the session frame");

            // The pipeline puts the head in the room frame: 3 m away in world terms, but no step at all.
            pipeline.RoomActive = true;
            rig.Pose.Position = new float3(0.5f, 1.6f, -0.25f);
            rig.Log.Clear();
            rig.Frame();
            Assert.That(rig.Loop.RoomResetPending, Is.False);
            Assert.That(pipeline.Resets, Is.EqualTo(1));
            Assert.That(rig.Log.IndexOf("pipeline.reset"), Is.GreaterThan(rig.Log.IndexOf("filter")));
            Assert.That(rig.Log.IndexOf("pipeline.reset"), Is.LessThan(rig.Log.IndexOf("space.tick")));
            Assert.That(rig.Loop.Current.Kernel.Rejected, Is.False, "the change of frame is not seen as a jump");
            Assert.That(rig.Kernel.Pose.Tile, Is.EqualTo(TileId.Origin));
            Assert.That(rig.Kernel.Pose.Local.M02, Is.EqualTo(0.5).Within(1e-6));
            Assert.That(rig.Kernel.Pose.Local.M12, Is.EqualTo(-0.25).Within(1e-6));
            Assert.That(rig.Loop.Current.FloorDelta, Is.EqualTo(float2.zero));
        }

        [Test]
        public void State_CommonFields()
        {
            var rig = new Rig(true);
            rig.Pose.Position = new float3(1f, 1.5f, -2f);
            rig.Pose.Tracking = TrackingState.Limited;
            rig.Guard.Level = 2;
            var host = new FakeHost { WorldVersion = 7 };
            rig.Loop.Host = host;
            rig.Frame();

            StateMessage s = rig.Sink.Last;
            Assert.That(s.Tracking, Is.EqualTo(1), "limited on the wire");
            Assert.That(s.SpaceId, Is.EqualTo(SpaceIds.NoSpace));
            Assert.That(s.SceneId, Is.EqualTo(SpaceIds.NoScene));
            Assert.That(s.WorldVer, Is.EqualTo(7));
            Assert.That(new float3(s.HeadPosX, s.HeadPosY, s.HeadPosZ), Is.EqualTo(new float3(1f, 1.5f, -2f)));
            Assert.That(s.HeadRotW, Is.EqualTo(1f));
            Assert.That(s.BumpLevel, Is.EqualTo(2));
            Assert.That(s.CueActive, Is.True);
            Assert.That(s.P0, Is.EqualTo((float)rig.Kernel.MetresPerUnit));
            Assert.That(s.P2, Is.EqualTo((float)rig.Kernel.CullRho));
            Assert.That(s.P1, Is.EqualTo((float)rig.Kernel.CullRho - 1f));
            Assert.That(s.GetXform(0), Is.EqualTo(1.0));

            rig.Pose.Tracking = TrackingState.Normal;
            host.HasSpace = true;
            host.Current = SpaceId.Portal;
            rig.Frame();
            Assert.That(rig.Sink.Last.Tracking, Is.EqualTo(0), "normal is 0 on the wire, not the enum's 2");
            Assert.That(rig.Sink.Last.SpaceId, Is.EqualTo(5));
            Assert.That(rig.Sink.Last.SceneId, Is.EqualTo(5));
        }

        [Test]
        public void NoHooks_NeutralDefaults()
        {
            var kernel = new FlatWalkerKernel();
            var loop = new FrameLoopCore(kernel);
            loop.RunUpdate(1.0, Dt);
            loop.RunState();
            loop.RunAfterFrame();
            Assert.That(loop.Current.Tracking, Is.EqualTo(TrackingState.Normal));
            Assert.That(loop.Current.HeadRotation.value, Is.EqualTo(quaternion.identity.value));
            Assert.That(loop.Current.Kernel.Rejected, Is.False);
            Assert.That(loop.Current.FrameIndex, Is.EqualTo(0));
            Assert.That(kernel.StepCount, Is.EqualTo(1));
        }

        [Test]
        public void FrameTime_IsClamped()
        {
            var rig = new Rig(true);
            rig.Loop.RunUpdate(1.0, 5f);
            Assert.That(rig.Loop.Current.DeltaTime, Is.EqualTo(FrameLoopCore.MaxDeltaTime));
            rig.Loop.RunUpdate(2.0, 0f);
            Assert.That(rig.Loop.Current.DeltaTime, Is.EqualTo(FrameLoopCore.MinDeltaTime));
        }

        [Test]
        public void Tail_RunsOncePerFrame_AndIsCaughtUpWhenNothingRendered()
        {
            var rig = new Rig(true);
            var after = new FakeAfter(null, "count");
            rig.Loop.AddAfterFrame(after);

            rig.Loop.RunUpdate(1.0, Dt);
            rig.Loop.RunState();
            rig.Loop.RunState();
            rig.Loop.RunAfterFrame();
            rig.Loop.RunAfterFrame();       // a second rendered view in the Editor
            Assert.That(rig.Sink.Calls, Is.EqualTo(1));
            Assert.That(after.Calls, Is.EqualTo(1));

            // A frame that was never rendered: its tail runs at the start of the next one, 8a before 8b.
            rig.Loop.RunUpdate(2.0, Dt);
            rig.Log.Clear();
            rig.Loop.RunUpdate(3.0, Dt);
            Assert.That(rig.Sink.Calls, Is.EqualTo(2));
            Assert.That(after.Calls, Is.EqualTo(2));
            Assert.That(rig.Log.IndexOf("sink"), Is.LessThan(rig.Log.IndexOf("probe")));
            Assert.That(rig.Log.IndexOf("spacelab"), Is.LessThan(rig.Log.IndexOf("pose")));
        }

        [Test]
        public void SecondGuard_Replaces_AndWarns_LateHook_IsReported()
        {
            var rig = new Rig(true);
            var warnings = new List<string>();
            rig.Loop.Warning = warnings.Add;
            var second = new FakeGuard(null);
            rig.Loop.SetGuard(second);
            Assert.That(warnings.Count, Is.EqualTo(1));
            rig.Frame();
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(rig.Guard.Calls, Is.EqualTo(0));

            rig.Loop.Seal();
            rig.Loop.AddUpload(new FakeUpload(null, "late"));
            Assert.That(warnings.Count, Is.EqualTo(2));
        }

        sealed class ThrowingSense : ISenseStep
        {
            public bool Throw = true;
            public int Calls;
            public void Sense(ref FrameContext frame)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("input device gone");
            }
        }

        sealed class ThrowingEyes : IEyePositions
        {
            public void GetEyes(out float3 left, out float3 right)
            {
                throw new InvalidOperationException("rig destroyed");
            }
        }

        sealed class ThrowingAfter : IAfterFrameStep
        {
            public void AfterFrame(in FrameContext frame) { throw new InvalidOperationException("after"); }
        }

        // Master Plan section 6.3, step 2; lane A contract, section 7.2, rule 2: Bump Guard runs every frame,
        // whatever a step-1 hook does.
        [Test]
        public void SenseHookThatThrows_DoesNotSkipBumpGuard_AndIsReportedOnce()
        {
            var rig = new Rig(true);
            var warnings = new List<string>();
            rig.Loop.Warning = warnings.Add;
            var faulty = new ThrowingSense();
            rig.Loop.AddSense(faulty);
            rig.Pose.Position = new float3(0.5f, 1.6f, 0.25f);

            for (int i = 0; i < 3; i++) rig.Frame();

            Assert.That(rig.Guard.Calls, Is.EqualTo(3));
            Assert.That(rig.Guard.SeenRaw.x, Is.EqualTo(0.5f));
            Assert.That(faulty.Calls, Is.EqualTo(3), "a faulted hook is still called");
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("ThrowingSense"));
            Assert.That(rig.Loop.FaultedSenseCount, Is.EqualTo(1));
            // The rest of the frame ran too.
            Assert.That(rig.Sink.Calls, Is.EqualTo(3));

            faulty.Throw = false;
            rig.Frame();
            Assert.That(rig.Guard.Calls, Is.EqualTo(4));
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void EyePositionsThatThrow_FallBackToTheHead_AndBumpGuardRuns()
        {
            var rig = new Rig(false);
            var warnings = new List<string>();
            rig.Loop.Warning = warnings.Add;
            rig.Loop.Eyes = new ThrowingEyes();
            rig.Pose.Position = new float3(1f, 1.5f, 2f);

            rig.Frame();
            rig.Frame();

            Assert.That(rig.Guard.Calls, Is.EqualTo(2));
            Assert.That(rig.Loop.Current.LeftEyePosition, Is.EqualTo(new float3(1f, 1.5f, 2f)));
            Assert.That(rig.Loop.Current.RightEyePosition, Is.EqualTo(new float3(1f, 1.5f, 2f)));
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void LateTailThatThrows_DoesNotSkipBumpGuard()
        {
            var rig = new Rig(true);
            var warnings = new List<string>();
            rig.Loop.Warning = warnings.Add;
            rig.Loop.AddAfterFrame(new ThrowingAfter());

            // Two frames that were never rendered: the second Update runs the first one's tail itself.
            rig.Loop.RunUpdate(1.0, Dt);
            Assert.DoesNotThrow(() => rig.Loop.RunUpdate(1.0 + Dt, Dt));
            Assert.That(rig.Guard.Calls, Is.EqualTo(2));
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void WarningSinkThatThrows_DoesNotSkipBumpGuard()
        {
            var rig = new Rig(true);
            rig.Loop.Warning = message => throw new InvalidOperationException("log");
            rig.Loop.AddSense(new ThrowingSense());
            Assert.DoesNotThrow(() => rig.Frame());
            Assert.That(rig.Guard.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ResetWalker_PlacesTheWalker_AndIntegratesNothingNext()
        {
            var rig = new Rig(true);
            rig.Pose.Position = new float3(0.6f, 1.6f, 0.3f);
            rig.Frame();
            rig.Loop.ResetWalkerToFloor();
            Assert.That(rig.Kernel.Pose.Local.M02, Is.EqualTo(0.6).Within(1e-6));
            Assert.That(rig.Kernel.Pose.Local.M12, Is.EqualTo(0.3).Within(1e-6));
            rig.Frame();
            Assert.That(rig.Kernel.Pose.Local.M02, Is.EqualTo(0.6).Within(1e-6), "the same head point is no step");

            rig.Loop.ResetWalker(WalkerPose.Start);
            Assert.That(rig.Loop.Current.Walker.Local.Equals(Iso2.Identity), Is.True);
        }

        [Test]
        public void FramePath_AllocatesNothing()
        {
            var rig = new Rig(true);
            rig.Log.Capacity = 64;
            for (int i = 0; i < 50; i++) { rig.Log.Clear(); rig.Frame(); }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                rig.Log.Clear();
                rig.Pose.Position = new float3(0.001f * i, 1.6f, 0f);
                rig.Frame();
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "steps 1 to 8 of the frame loop allocate no managed memory");
        }
    }
}
