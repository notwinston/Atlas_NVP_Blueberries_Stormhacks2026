using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>Pose-log replay as a pose source (Master Plan task P1-C01).</summary>
    public class ReplaySourceTests
    {
        const float Dt = 1f / 60f;

        static PoseLog Line()
        {
            // Four samples a tenth of a second apart, 0.1 m apart along +X.
            var samples = new HeadPose[4];
            for (int i = 0; i < 4; i++)
                samples[i] = new HeadPose(i * 0.1, new float3(i * 0.1f, 1.6f, 0f), quaternion.RotateY(i * 0.2f), TrackingState.Normal);
            return new PoseLog { Frame = PoseFrameKind.Room, Samples = samples };
        }

        [Test]
        public void Identity_IsThatOfAReplay()
        {
            var source = new PoseLogReplaySource(Line());
            Assert.That(source.Name, Is.EqualTo("poselog"));
            Assert.That(source.Frame, Is.EqualTo(PoseFrameKind.Room));
            Assert.That(source.DrivesRig, Is.False);
            var session = new PoseLogReplaySource(new PoseLog { Frame = PoseFrameKind.Session, Samples = Line().Samples });
            Assert.That(session.Frame, Is.EqualTo(PoseFrameKind.Session));
        }

        [Test]
        public void EveryFrame_DeliversEachSampleInOrder_ThenEnds()
        {
            PoseLog log = Line();
            var source = new PoseLogReplaySource(log, ReplayMode.EveryFrame);
            HeadPose pose;
            for (int i = 0; i < 4; i++)
            {
                Assert.That(source.TryRead(50.0 + i, Dt, out pose), Is.True);
                Assert.That(pose.Position, Is.EqualTo(log.Samples[i].Position));
                Assert.That(pose.Time, Is.EqualTo(50.0 + i), "the sample carries the frame's time");
                Assert.That(source.SampleIndex, Is.EqualTo(i));
                Assert.That(source.SampleTime, Is.EqualTo(log.Samples[i].Time).Within(1e-9));
            }
            Assert.That(source.Ended, Is.False);
            Assert.That(source.TryRead(60.0, Dt, out pose), Is.False);
            Assert.That(source.Ended, Is.True);

            source.Restart();
            Assert.That(source.Ended, Is.False);
            Assert.That(source.TryRead(61.0, Dt, out pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(log.Samples[0].Position));
        }

        [Test]
        public void EveryFrame_Looping_StartsAgain()
        {
            var source = new PoseLogReplaySource(Line(), ReplayMode.EveryFrame, true);
            HeadPose pose;
            for (int i = 0; i < 9; i++) Assert.That(source.TryRead(i, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(0));
            Assert.That(source.LoopCount, Is.EqualTo(2));
            Assert.That(source.Ended, Is.False);
        }

        [Test]
        public void RealTime_HoldsTheLastSampleAtOrBeforeTheClock()
        {
            PoseLog log = Line();
            var source = new PoseLogReplaySource(log);
            HeadPose pose;
            Assert.That(source.TryRead(10.0, Dt, out pose), Is.True);        // the replay starts at the first call
            Assert.That(source.SampleIndex, Is.EqualTo(0));
            Assert.That(source.TryRead(10.05, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(0));
            Assert.That(pose.Position.x, Is.EqualTo(0f));
            Assert.That(source.TryRead(10.1, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(1));
            Assert.That(source.TryRead(10.29, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(2));
            Assert.That(pose.Position.x, Is.EqualTo(0.2f));
            Assert.That(source.TryRead(10.3, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(3));
            Assert.That(source.TryRead(10.31, Dt, out pose), Is.False);
            Assert.That(source.Ended, Is.True);
        }

        [Test]
        public void RealTime_Interpolation_BlendsPositionAndRotationBetweenSamples()
        {
            var source = new PoseLogReplaySource(Line()) { Interpolate = true };
            HeadPose pose;
            source.TryRead(0.0, Dt, out pose);
            Assert.That(source.TryRead(0.05, Dt, out pose), Is.True);
            Assert.That(pose.Position.x, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(pose.Yaw, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(source.SampleTime, Is.EqualTo(0.05).Within(1e-9));
            Assert.That(source.TryRead(0.275, Dt, out pose), Is.True);
            Assert.That(pose.Position.x, Is.EqualTo(0.275f).Within(1e-6f));
            Assert.That(pose.Yaw, Is.EqualTo(0.55f).Within(1e-5f));
            Assert.That(source.TryRead(0.3, Dt, out pose), Is.True);
            Assert.That(pose.Position.x, Is.EqualTo(0.3f).Within(1e-6f));
        }

        [Test]
        public void RealTime_Interpolation_NeverBlendsAcrossAJumpOrATrackingChange()
        {
            var samples = new[]
            {
                new HeadPose(0.0, new float3(0f, 1.6f, 0f), quaternion.identity, TrackingState.Normal),
                new HeadPose(0.1, new float3(0f, 1.6f, 0.5f), quaternion.identity, TrackingState.Normal),
                new HeadPose(0.2, new float3(0f, 1.6f, 0.6f), quaternion.identity, TrackingState.Limited),
            };
            var source = new PoseLogReplaySource(new PoseLog { Frame = PoseFrameKind.Room, Samples = samples }) { Interpolate = true };
            HeadPose pose;
            source.TryRead(0.0, Dt, out pose);
            source.TryRead(0.05, Dt, out pose);
            Assert.That(pose.Position.z, Is.EqualTo(0f), "half a jump would pass the 0.2 m rule");
            source.TryRead(0.15, Dt, out pose);
            Assert.That(pose.Position.z, Is.EqualTo(0.5f));
            Assert.That(pose.Tracking, Is.EqualTo(TrackingState.Normal));
        }

        [Test]
        public void RealTime_AtSixtyHertz_LandsOnEverySampleOfASixtyHertzLog()
        {
            PoseLog log = PoseLog.Read(new System.IO.StringReader(SampleWalks.ToText(SampleWalks.JumpAndLoss())));
            var source = new PoseLogReplaySource(log);
            HeadPose pose;
            double clock = 123.456;
            for (int k = 0; k < log.Samples.Length; k++)
            {
                Assert.That(source.TryRead(clock + k / 60.0, Dt, out pose), Is.True);
                Assert.That(source.SampleIndex, Is.EqualTo(k));
                Assert.That(pose.Tracking, Is.EqualTo(log.Samples[k].Tracking));
            }
            Assert.That(source.TryRead(clock + log.Samples.Length / 60.0, Dt, out pose), Is.False);
        }

        [Test]
        public void RealTime_Looping_WrapsTheClock()
        {
            var source = new PoseLogReplaySource(Line(), ReplayMode.RealTime, true);
            HeadPose pose;
            source.TryRead(0.0, Dt, out pose);
            Assert.That(source.TryRead(0.35, Dt, out pose), Is.True);         // 0.05 s into the second lap
            Assert.That(source.SampleIndex, Is.EqualTo(0));
            Assert.That(source.LoopCount, Is.EqualTo(1));
            Assert.That(source.TryRead(0.52, Dt, out pose), Is.True);
            Assert.That(source.SampleIndex, Is.EqualTo(2));
            Assert.That(source.Ended, Is.False);
        }

        [Test]
        public void AnEmptyLog_HasNoSample()
        {
            var source = new PoseLogReplaySource(new PoseLog { Frame = PoseFrameKind.Room });
            HeadPose pose;
            Assert.That(source.TryRead(0.0, Dt, out pose), Is.False);
            Assert.That(source.Ended, Is.True);
        }

        [Test]
        public void FromFile_ReadsASampleWalk()
        {
            string path = System.IO.Path.Combine(SampleWalks.RecordingsDirectory(), SampleWalks.WallName + ".poselog");
            PoseLogReplaySource source = PoseLogReplaySource.FromFile(path, ReplayMode.EveryFrame);
            Assert.That(source.Log.RoomId, Is.EqualTo(RoomFixtures.Sample3x3));
            Assert.That(source.Frame, Is.EqualTo(PoseFrameKind.Room));
            HeadPose pose;
            Assert.That(source.TryRead(0.0, Dt, out pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(new float3(0f, 1.6f, -1f)));
        }
    }
}
