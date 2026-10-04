using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>The Editor walker's integration (contract README F9).</summary>
    public class KeyboardWalkerPoseTests
    {
        const float Dt = 1f / 60f;

        static HeadPose Run(KeyboardWalkerPoseSource walker, float2 move, float speed, int frames)
        {
            HeadPose pose = default(HeadPose);
            for (int k = 0; k < frames; k++)
            {
                walker.SetInput(move, float2.zero, speed);
                walker.TryRead(k * Dt, Dt, out pose);
            }
            return pose;
        }

        [Test]
        public void Identity_IsThatOfAWalkerInTheRoomFrame()
        {
            var walker = new KeyboardWalkerPoseSource();
            Assert.That(walker.Name, Is.EqualTo("walker"));
            Assert.That(walker.Frame, Is.EqualTo(PoseFrameKind.Room));
            Assert.That(walker.DrivesRig, Is.False);
            HeadPose pose;
            Assert.That(walker.TryRead(3.0, Dt, out pose), Is.True);
            Assert.That(pose.Position, Is.EqualTo(new float3(0f, 1.6f, 0f)));
            Assert.That(pose.Tracking, Is.EqualTo(TrackingState.Normal));
            Assert.That(pose.Time, Is.EqualTo(3.0));
            Assert.That(pose.Heading.y, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void Forward_ForOneSecondAtOneMetrePerSecond_IsOneMetreAlongZ()
        {
            var walker = new KeyboardWalkerPoseSource();
            HeadPose pose = Run(walker, new float2(0f, 1f), 1.0f, 60);
            Assert.That(pose.Position.z, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pose.Position.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(pose.Position.y, Is.EqualTo(KeyboardWalkerPoseSource.EyeHeight));
        }

        [Test]
        public void EachStep_IsSmallEnoughForThePoseFilter()
        {
            var walker = new KeyboardWalkerPoseSource();
            var filter = new PoseFilter();
            HeadPose pose;
            for (int k = 0; k < 120; k++)
            {
                walker.SetInput(new float2(0f, 1f), new float2(1f, 0f), 2.0f);
                walker.TryRead(k * Dt, Dt, out pose);
                filter.Update(in pose, Dt);
                Assert.That(filter.JumpRejected, Is.False);
            }
            Assert.That(math.distance(filter.PathFloor, walker.Floor), Is.LessThan(1e-3f));
        }

        [Test]
        public void Move_IsInTheHeadingFrame()
        {
            var walker = new KeyboardWalkerPoseSource();
            HeadPose pose;
            walker.SetInput(float2.zero, new float2(90f, 0f), 1f);       // turn right: +Z towards +X
            walker.TryRead(0.0, Dt, out pose);
            Assert.That(pose.Yaw, Is.EqualTo(math.PI / 2f).Within(1e-5f));
            Assert.That(pose.Heading.x, Is.EqualTo(1f).Within(1e-5f));

            pose = Run(walker, new float2(0f, 1f), 1f, 60);              // forward is now +X
            Assert.That(pose.Position.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pose.Position.z, Is.EqualTo(0f).Within(1e-4f));

            pose = Run(walker, new float2(1f, 0f), 1f, 60);              // right of +X is -Z
            Assert.That(pose.Position.x, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pose.Position.z, Is.EqualTo(-1f).Within(1e-4f));
        }

        [Test]
        public void ADiagonal_IsNotFaster()
        {
            var walker = new KeyboardWalkerPoseSource();
            HeadPose pose = Run(walker, new float2(1f, 1f), 1f, 60);
            Assert.That(math.length(pose.Floor), Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Look_IsAppliedOncePerInput_AndPitchIsClamped()
        {
            var walker = new KeyboardWalkerPoseSource();
            HeadPose pose;
            walker.SetInput(float2.zero, new float2(10f, 20f), 1f);
            walker.TryRead(0.0, Dt, out pose);
            walker.TryRead(Dt, Dt, out pose);                             // no new input: no further turn
            Assert.That(math.degrees(walker.Yaw), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(math.degrees(walker.Pitch), Is.EqualTo(20f).Within(1e-4f));
            Assert.That(pose.Forward.y, Is.GreaterThan(0f), "a positive pitch looks up");

            walker.SetInput(float2.zero, new float2(0f, 500f), 1f);
            walker.TryRead(2 * Dt, Dt, out pose);
            Assert.That(math.degrees(walker.Pitch), Is.EqualTo(85f).Within(1e-4f));
            walker.SetInput(float2.zero, new float2(0f, -500f), 1f);
            walker.TryRead(3 * Dt, Dt, out pose);
            Assert.That(math.degrees(walker.Pitch), Is.EqualTo(-85f).Within(1e-4f));
            // The heading survives a steep look.
            Assert.That(pose.Yaw, Is.EqualTo(math.radians(10f)).Within(1e-3f));
        }

        [Test]
        public void Teleport_IsAJumpThePoseFilterDrops_AndTrackingCanBeInjected()
        {
            var walker = new KeyboardWalkerPoseSource();
            var filter = new PoseFilter();
            HeadPose pose;
            walker.TryRead(0.0, Dt, out pose);
            filter.Update(in pose, 0f);
            walker.Teleport(new float2(0.5f, 0f));
            walker.TryRead(Dt, Dt, out pose);
            filter.Update(in pose, Dt);
            Assert.That(pose.Position.x, Is.EqualTo(0.5f));
            Assert.That(filter.JumpRejected, Is.True);
            Assert.That(filter.PathFloor, Is.EqualTo(float2.zero));

            walker.Tracking = TrackingState.Limited;
            walker.TryRead(2 * Dt, Dt, out pose);
            Assert.That(pose.Tracking, Is.EqualTo(TrackingState.Limited));
        }

        [Test]
        public void ExternalInput_IsNoted_SoTheBuiltInReaderStaysOut()
        {
            var walker = new KeyboardWalkerPoseSource();
            Assert.That(walker.HasExternalInput, Is.False);
            walker.Feed(new float2(0f, 1f), float2.zero, 1f);
            Assert.That(walker.HasExternalInput, Is.False);
            walker.SetInput(float2.zero, float2.zero, 1f);
            Assert.That(walker.HasExternalInput, Is.True);
        }

        [Test]
        public void Place_SetsThePointAndTheHeading()
        {
            var walker = new KeyboardWalkerPoseSource();
            walker.Place(new float2(-1f, -1f), math.PI);
            HeadPose pose;
            walker.TryRead(0.0, 0f, out pose);
            Assert.That(pose.Floor, Is.EqualTo(new float2(-1f, -1f)));
            Assert.That(pose.Heading.y, Is.EqualTo(-1f).Within(1e-5f));
        }
    }
}
