using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>The engine-free pose filter (Master Plan task P1-C11; test PosePipeline_Jump of lane C section 14.3).</summary>
    public class PoseFilterTests
    {
        const float Dt = 1f / 60f;

        static HeadPose At(double t, float x, float z, TrackingState tracking = TrackingState.Normal)
        {
            return new HeadPose(t, new float3(x, 1.6f, z), quaternion.identity, tracking);
        }

        [Test]
        public void FirstSample_GivesNoStep_AndStartsThePath()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0.7f, -0.4f), 0f);
            Assert.That(filter.Step, Is.EqualTo(float2.zero));
            Assert.That(filter.PathFloor, Is.EqualTo(new float2(0.7f, -0.4f)));
            Assert.That(filter.JumpRejected, Is.False);
            Assert.That(filter.Geometry.Position, Is.EqualTo(new float3(0.7f, 1.6f, -0.4f)));
        }

        [Test]
        public void SmallSteps_PassUnchanged()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0f, 0f), 0f);
            float x = 0f;
            for (int k = 1; k <= 120; k++)
            {
                float next = k * 0.0125f;                       // 0.75 m/s at 60 Hz
                filter.Update(At(k * Dt, next, 0f), Dt);
                Assert.That(filter.Step.x, Is.EqualTo(next - x));
                Assert.That(filter.Step.y, Is.EqualTo(0f));
                Assert.That(filter.JumpRejected, Is.False);
                x = next;
            }
            Assert.That(filter.PathFloor.x, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(filter.JumpCount, Is.EqualTo(0));
        }

        [Test]
        public void AStepOfTwentyCentimetres_Passes_AndALongerOneIsDropped()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0f, 0f), 0f);
            filter.Update(At(Dt, 0.19f, 0f), Dt);
            Assert.That(filter.JumpRejected, Is.False);
            Assert.That(filter.Step.x, Is.EqualTo(0.19f));

            filter.Update(At(2 * Dt, 0.19f, 0.25f), Dt);
            Assert.That(filter.JumpRejected, Is.True);
            Assert.That(filter.JumpLength, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(filter.Step, Is.EqualTo(float2.zero));
            Assert.That(filter.PathFloor, Is.EqualTo(new float2(0.19f, 0f)));
            Assert.That(filter.JumpCount, Is.EqualTo(1));

            // The walk goes on from the new place: the next small step counts again.
            filter.Update(At(3 * Dt, 0.20f, 0.25f), Dt);
            Assert.That(filter.JumpRejected, Is.False);
            Assert.That(filter.JumpLength, Is.EqualTo(0f));
            Assert.That(filter.Step.x, Is.EqualTo(0.01f).Within(1e-6f));
        }

        [Test]
        public void FloorDelta_SumsToThePathLength_OnTheLap()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            PoseLog log = SampleWalks.Lap(room.LapSide);
            var replay = new PoseLogReplaySource(log, ReplayMode.EveryFrame);
            var filter = new PoseFilter();
            double length = 0.0;
            float2 sum = float2.zero;
            HeadPose raw;
            int frame = 0;
            while (replay.TryRead(frame * (1.0 / 60.0), Dt, out raw))
            {
                filter.Update(in raw, frame == 0 ? 0f : Dt);
                length += math.length(filter.Step);
                sum += filter.Step;
                frame++;
            }
            Assert.That(frame, Is.EqualTo(log.Samples.Length));
            Assert.That(length, Is.EqualTo(4.0 * room.LapSide).Within(1e-3));
            // A closed lap: the displacements cancel and the path ends where it began.
            Assert.That(math.length(sum), Is.LessThan(1e-3f));
            Assert.That(math.distance(filter.PathFloor, log.Samples[0].Floor), Is.LessThan(1e-3f));
            Assert.That(filter.JumpCount, Is.EqualTo(0));
        }

        [Test]
        public void InjectedJump_IsAbsentFromGeometry_AndPresentInRaw()
        {
            PoseLog log = SampleWalks.JumpAndLoss();
            var replay = new PoseLogReplaySource(log, ReplayMode.EveryFrame);
            var filter = new PoseFilter();
            float2 expectedPath = log.Samples[0].Floor;
            HeadPose raw, previous = default(HeadPose);
            float longestStep = 0f;
            for (int k = 0; k < log.Samples.Length; k++)
            {
                Assert.That(replay.TryRead(k / 60.0, Dt, out raw), Is.True);
                filter.Update(in raw, k == 0 ? 0f : Dt);
                float rawMove = k == 0 ? 0f : math.distance(raw.Floor, previous.Floor);

                if (k == SampleWalks.JumpFrame)
                {
                    // Raw (safety) stream: the jump is there at once.
                    Assert.That(rawMove, Is.GreaterThan(0.49f));
                    Assert.That(raw.Position.z, Is.EqualTo(SampleWalks.JumpMetres).Within(1e-6f));
                    // Geometry stream: no displacement, and the event data is there.
                    Assert.That(filter.JumpRejected, Is.True);
                    Assert.That(filter.Step, Is.EqualTo(float2.zero));
                    Assert.That(filter.JumpLength, Is.EqualTo(rawMove).Within(1e-6f));
                }
                else
                {
                    Assert.That(filter.JumpRejected, Is.False, "frame " + k);
                }
                // The geometry pose itself follows the raw pose, so a registered space stays registered.
                Assert.That(filter.Geometry.Position, Is.EqualTo(raw.Position));

                bool lost = k >= SampleWalks.LossFirstFrame && k < SampleWalks.LossFirstFrame + SampleWalks.LossFrames;
                if (k > 0 && k != SampleWalks.JumpFrame && !lost) expectedPath += raw.Floor - previous.Floor;
                if (lost) Assert.That(filter.Step, Is.EqualTo(float2.zero), "no geometry step while tracking is limited");
                longestStep = math.max(longestStep, math.length(filter.Step));
                previous = raw;
            }
            Assert.That(filter.JumpCount, Is.EqualTo(1));
            Assert.That(longestStep, Is.LessThan(0.02f));
            Assert.That(math.distance(filter.PathFloor, expectedPath), Is.LessThan(1e-4f));
            // The path never took the half metre sideways.
            Assert.That(filter.PathFloor.y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(previous.Position.z, Is.EqualTo(SampleWalks.JumpMetres).Within(1e-6f));
        }

        [Test]
        public void TrackingLost_IsRaisedWithinHalfASecond_Once_AndClearedOnRecovery()
        {
            var filter = new PoseFilter();
            int k = 0;
            for (; k < 30; k++) filter.Update(At(k * Dt, 0f, 0f), Dt);
            Assert.That(filter.TrackingLost, Is.False);

            double leftNormalAt = (k - 1) * (double)Dt;       // the time of the last Normal sample
            int raised = 0;
            double raisedAt = -1.0;
            for (int n = 0; n < 90; n++, k++)
            {
                filter.Update(At(k * Dt, 0f, 0f, TrackingState.Limited), Dt);
                if (filter.LostThisFrame)
                {
                    raised++;
                    raisedAt = k * (double)Dt;
                }
                Assert.That(filter.Step, Is.EqualTo(float2.zero));
            }
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(filter.TrackingLost, Is.True);
            double after = raisedAt - leftNormalAt;
            Assert.That(after, Is.GreaterThanOrEqualTo(0.5 - 1e-3));
            Assert.That(after, Is.LessThanOrEqualTo(0.5 + Dt + 1e-3));

            filter.Update(At(k * Dt, 0f, 0f), Dt);
            Assert.That(filter.TrackingLost, Is.False);
            Assert.That(filter.RegainedThisFrame, Is.True);
            Assert.That(filter.NotNormalSeconds, Is.EqualTo(0.0));
            k++;
            filter.Update(At(k * Dt, 0f, 0f), Dt);
            Assert.That(filter.RegainedThisFrame, Is.False);
        }

        [Test]
        public void AShortDropout_RaisesNothing()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0f, 0f), 0f);
            for (int k = 1; k <= 20; k++)
            {
                filter.Update(At(k * Dt, 0f, 0f, TrackingState.NotTracking), Dt);
                Assert.That(filter.LostThisFrame, Is.False);
            }
            filter.Update(At(21 * Dt, 0f, 0f), Dt);
            Assert.That(filter.TrackingLost, Is.False);
            Assert.That(filter.RegainedThisFrame, Is.False);
        }

        [Test]
        public void TheLossInTheSampleWalk_IsRaisedAndCleared()
        {
            PoseLog log = SampleWalks.JumpAndLoss();
            var filter = new PoseFilter();
            int lostAt = -1, regainedAt = -1;
            for (int k = 0; k < log.Samples.Length; k++)
            {
                filter.Update(in log.Samples[k], k == 0 ? 0f : Dt);
                if (filter.LostThisFrame) lostAt = k;
                if (filter.RegainedThisFrame) regainedAt = k;
            }
            // Thirty frames of 1/60 s after the last Normal frame.
            Assert.That(lostAt, Is.EqualTo(SampleWalks.LossFirstFrame + 29));
            Assert.That(regainedAt, Is.EqualTo(SampleWalks.LossFirstFrame + SampleWalks.LossFrames));
        }

        [Test]
        public void Reset_MakesTheNextDisplacementZero_AndRestartsThePath()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0f, 0f), 0f);
            filter.Update(At(Dt, 0.01f, 0f), Dt);
            filter.Reset();
            filter.Update(At(2 * Dt, 3f, -2f), Dt);       // the room frame has just changed under the head
            Assert.That(filter.Step, Is.EqualTo(float2.zero));
            Assert.That(filter.JumpRejected, Is.False);
            Assert.That(filter.PathFloor, Is.EqualTo(new float2(3f, -2f)));
            Assert.That(filter.JumpCount, Is.EqualTo(0));
        }

        [Test]
        public void ASampleThatIsNotANumber_GivesNoStep_AndTheWalkRecovers()
        {
            var filter = new PoseFilter();
            filter.Update(At(0.0, 0f, 0f), 0f);
            filter.Update(At(Dt, float.NaN, 0f), Dt);
            Assert.That(filter.Step, Is.EqualTo(float2.zero));
            Assert.That(math.all(math.isfinite(filter.PathFloor)), Is.True);
            Assert.That(filter.JumpRejected, Is.False);
            // Measured from the last good sample.
            filter.Update(At(2 * Dt, 0.01f, 0f), Dt);
            Assert.That(filter.Step.x, Is.EqualTo(0.01f).Within(1e-6f));
            filter.Update(At(3 * Dt, 0.02f, 0f), Dt);
            Assert.That(filter.Step.x, Is.EqualTo(0.01f).Within(1e-6f));
            Assert.That(filter.PathFloor.x, Is.EqualTo(0.02f).Within(1e-6f));
        }
    }
}
