using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Pose
{
    /// <summary>The small engine-free cores around the pose pipeline: the room frame, the rotation prediction,
    /// the video-format rule, the depth copy and the ray-cast depth of the Editor.</summary>
    public class PoseCoreTests
    {
        [Test]
        public void RoomFrame_PutsTheAnchorAtTheWorldOrigin_Unturned()
        {
            var anchorPosition = new float3(2f, -1.2f, 3f);
            quaternion anchorRotation = quaternion.RotateY(0.7f);
            float3 sessionPosition;
            quaternion sessionRotation;
            RoomFrame.SessionToRoom(anchorPosition, anchorRotation, out sessionPosition, out sessionRotation);

            float3 p;
            quaternion q;
            RoomFrame.Compose(sessionPosition, sessionRotation, anchorPosition, anchorRotation, out p, out q);
            Assert.That(math.length(p), Is.LessThan(1e-5f));
            Assert.That(math.abs(q.value.w), Is.EqualTo(1f).Within(1e-5f));

            // A head one metre in front of the anchor (along the anchor's +Z) is at (0, h, 1) in the room.
            float3 head = anchorPosition + math.mul(anchorRotation, new float3(0f, 1.6f, 1f));
            RoomFrame.Compose(sessionPosition, sessionRotation, head, anchorRotation, out p, out q);
            Assert.That(math.distance(p, new float3(0f, 1.6f, 1f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void RoomFrame_UsesOnlyTheYaw_SoGravityStaysUp()
        {
            quaternion tilted = math.mul(quaternion.RotateY(-1.1f), quaternion.RotateX(0.2f));
            Assert.That(RoomFrame.Yaw(tilted), Is.EqualTo(-1.1f).Within(1e-5f));
            float3 sessionPosition;
            quaternion sessionRotation;
            RoomFrame.SessionToRoom(float3.zero, tilted, out sessionPosition, out sessionRotation);
            float3 up = math.mul(sessionRotation, new float3(0f, 1f, 0f));
            Assert.That(up.y, Is.EqualTo(1f).Within(1e-6f));
            // Straight up or down has no yaw: the identity.
            Assert.That(RoomFrame.YawOnly(quaternion.RotateX(math.PI / 2f)).value.w, Is.EqualTo(1f).Within(1e-6f));
        }

        [Test]
        public void RoomFrame_ParentFor_IsTheInverseOfCompose()
        {
            var world = new float3(0.3f, 1.6f, -0.8f);
            quaternion worldRotation = math.mul(quaternion.RotateY(2f), quaternion.RotateX(-0.3f));
            var local = new float3(0f, -0.02f, 0.05f);
            quaternion localRotation = quaternion.RotateZ(0.1f);
            float3 parent, p;
            quaternion parentRotation, q;
            RoomFrame.ParentFor(world, worldRotation, local, localRotation, out parent, out parentRotation);
            RoomFrame.Compose(parent, parentRotation, local, localRotation, out p, out q);
            Assert.That(math.distance(p, world), Is.LessThan(1e-5f));
            Assert.That(math.abs(math.dot(q.value, worldRotation.value)), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void GyroPrediction_IsOffByDefault_AndLeadsATurnWhenOn()
        {
            const float dt = 1f / 60f;
            var gyro = new GyroPredictor();
            float rate = math.radians(90f);                      // a steady turn to the right
            for (int k = 0; k < 5; k++) gyro.Update(quaternion.RotateY(k * rate * dt), dt, 0f, true);
            Assert.That(gyro.Prediction.value, Is.EqualTo(quaternion.identity.value));

            for (int k = 5; k < 10; k++) gyro.Update(quaternion.RotateY(k * rate * dt), dt, 50f, true);
            float3 forward = math.mul(gyro.Prediction, new float3(0f, 0f, 1f));
            // 90 degrees a second for 50 ms is 4.5 degrees about the head's own up axis.
            Assert.That(math.degrees(math.atan2(forward.x, forward.z)), Is.EqualTo(4.5f).Within(0.05f));
            Assert.That(forward.y, Is.EqualTo(0f).Within(1e-5f));

            gyro.Update(quaternion.RotateY(10 * rate * dt), dt, 50f, false);
            Assert.That(gyro.Prediction.value, Is.EqualTo(quaternion.identity.value), "not while tracking is doubtful");
        }

        [Test]
        public void GyroPrediction_IsBounded()
        {
            var gyro = new GyroPredictor();
            gyro.Update(quaternion.identity, 1f / 60f, 60f, true);
            gyro.Update(quaternion.RotateY(1.0f), 1f / 60f, 600f, true);      // a wild turn and a lead beyond the limit
            float3 forward = math.mul(gyro.Prediction, new float3(0f, 0f, 1f));
            Assert.That(math.atan2(forward.x, forward.z), Is.EqualTo(GyroPredictor.MaxAngle).Within(1e-4f));
        }

        [Test]
        public void VideoFormat_IsTheSmallestSixtyHertzEntry()
        {
            int[] widths = { 1920, 1920, 1280, 3840, 1440, 1280 };
            int[] heights = { 1440, 1080, 720, 2160, 1080, 720 };
            int[] rates = { 60, 60, 60, 30, 60, 30 };
            Assert.That(VideoFormat.ChooseLowest60Hz(widths, heights, rates, 6), Is.EqualTo(2));
            Assert.That(VideoFormat.Describe(1280, 720, 60), Is.EqualTo("1280x720@60"));
            // XR Simulation lists one format with no frame rate: nothing is chosen.
            Assert.That(VideoFormat.ChooseLowest60Hz(new[] { 640 }, new[] { 480 }, new[] { 0 }, 1), Is.EqualTo(-1));
            Assert.That(VideoFormat.ChooseLowest60Hz(widths, heights, rates, 0), Is.EqualTo(-1));
        }

        [Test]
        public unsafe void DepthCopy_HonoursTheRowStride()
        {
            const int width = 4, height = 3, rowStride = 24;      // two floats of padding after each row
            var source = new byte[rowStride * height];
            var destination = new float[width * height];
            fixed (byte* s = source)
            fixed (float* d = destination)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < 6; x++)
                        *(float*)(s + y * rowStride + x * 4) = x < width ? y * 10 + x + 0.5f : -1f;
                DepthKernel.CopyRows(s, rowStride, 4, width, height, d);
                Assert.That(DepthKernel.CountReturns(d, width * height), Is.EqualTo(4));     // only the first row lies between 0.3 and 5 m
            }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    Assert.That(destination[y * width + x], Is.EqualTo(y * 10 + x + 0.5f));
        }

        [Test]
        public void SyntheticDepth_SeesTheWallOfTheSampleRoom_AtFifteenHertz()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var depth = new SyntheticDepthSource { Room = room };
            DepthFrame frame;
            Assert.That(depth.TryRead(0.0, out frame), Is.False, "no camera pose yet");

            depth.SetCameraPose(new float3(0f, 1.2f, 0f), quaternion.identity);
            Assert.That(depth.TryRead(0.0, out frame), Is.True);
            Assert.That(frame.IsValid, Is.True);
            Assert.That(frame.Width, Is.EqualTo(SyntheticDepthSource.ImageWidth));
            float centre = frame.Depth[(frame.Height / 2) * frame.Width + frame.Width / 2];
            Assert.That(centre, Is.EqualTo(1.5f).Within(1e-3f));              // the wall at z = 1.5
            float3 hit = frame.ToWorld(frame.Width / 2, frame.Height / 2, centre);
            Assert.That(hit.z, Is.EqualTo(1.5f).Within(1e-3f));
            float3 left = frame.ToWorld(3, frame.Height / 2, frame.Depth[(frame.Height / 2) * frame.Width + 3]);
            Assert.That(left.z, Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(left.x, Is.LessThan(-0.8f));

            // Not again within a fifteenth of a second.
            Assert.That(depth.TryRead(1.0 / 60.0, out frame), Is.False);
            Assert.That(depth.TryRead(4.0 / 60.0, out frame), Is.True);
            Assert.That(frame.Sequence, Is.EqualTo(2));

            // A stool put down after the scan, 0.8 m ahead.
            Assert.That(depth.AddBox(new float3(0f, 0f, 1.0f), new float3(0.4f, 1.4f, 0.4f)), Is.True);
            Assert.That(depth.TryRead(1.0, out frame), Is.True);
            centre = frame.Depth[(frame.Height / 2) * frame.Width + frame.Width / 2];
            Assert.That(centre, Is.EqualTo(0.8f).Within(1e-3f));
            depth.ClearBoxes();
            Assert.That(depth.ExtraBoxCount, Is.EqualTo(0));
        }
    }
}
