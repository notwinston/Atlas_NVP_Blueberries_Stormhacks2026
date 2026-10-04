using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests
{
    /// <summary>
    /// The synthetic depth image showing something the walker carries in front of the camera: a raised hand
    /// (documents/ATLAS_Hands_Design.md section 7.1). The rehearsal puts the walker's hands into the image Bump
    /// Guard reads, so these pin what such a thing looks like there: its near face, on the pixels its rays meet,
    /// in the pixel convention of <see cref="DepthFrame.Unproject"/>.
    /// </summary>
    public class SyntheticDepthHandTests
    {
        static readonly float3 Eye = new float3(0f, 1.6f, 0f);
        /// <summary>A hand's size, held up 0.45 m ahead, a little right of and below the optical axis.</summary>
        static readonly float3 HandCentre = new float3(0.10f, -0.05f, 0.45f), HandSize = new float3(0.09f, 0.19f, 0.03f);

        static DepthFrame Read(SyntheticDepthSource depth, double time, float3 position, quaternion rotation)
        {
            depth.SetCameraPose(position, rotation);
            DepthFrame frame;
            Assert.That(depth.TryRead(time, out frame), Is.True);
            return frame;
        }

        static float[] Copy(in DepthFrame frame)
        {
            var copy = new float[frame.Width * frame.Height];
            System.Array.Copy(frame.Depth, copy, copy.Length);
            return copy;
        }

        [Test]
        public void SyntheticDepth_ACameraBox_ReadsAsItsNearFace_OnThePixelsItsRaysMeet()
        {
            var depth = new SyntheticDepthSource();     // no room: the box is all there is to see
            Assert.That(depth.AddCameraBox(HandCentre, HandSize), Is.True);
            Assert.That(depth.CameraBoxCount, Is.EqualTo(1));
            DepthFrame frame = Read(depth, 0.0, Eye, quaternion.identity);

            float3 low = HandCentre - 0.5f * HandSize, high = HandCentre + 0.5f * HandSize;
            float front = low.z;
            int onTheFace = 0, returns = 0;
            for (int v = 0; v < frame.Height; v++)
            {
                for (int u = 0; u < frame.Width; u++)
                {
                    float d = frame.Depth[v * frame.Width + u];
                    // Where this pixel's ray is when it reaches the plane of the near face.
                    float3 atFront = frame.Unproject(u, v, front);
                    bool meetsFace = atFront.x > low.x + 1e-4f && atFront.x < high.x - 1e-4f
                                  && atFront.y > low.y + 1e-4f && atFront.y < high.y - 1e-4f;
                    string at = "pixel (" + u + ", " + v + ")";
                    if (meetsFace)
                    {
                        Assert.That(d, Is.EqualTo(front).Within(1e-5f), at + " looks at the near face");
                        onTheFace++;
                    }
                    if (d == 0f) continue;
                    returns++;
                    // Every return is a point of the box's surface, never of its inside or of thin air.
                    float3 hit = frame.Unproject(u, v, d);
                    Assert.That(math.all(hit >= low - 1e-4f) && math.all(hit <= high + 1e-4f), Is.True, at + " returns " + hit);
                    Assert.That(d, Is.GreaterThanOrEqualTo(front - 1e-5f), at);
                }
            }
            // 0.09 m by 0.19 m at 0.435 m through a lens of 46.8 pixels: about 9.7 by 20.4 pixels.
            Assert.That(onTheFace, Is.InRange(160, 220), "pixels on the near face");
            // The rest are the few rays that slip past the near face's edge and meet a side.
            Assert.That(returns - onTheFace, Is.InRange(0, 80));
            // The image is the depth frame's own: the camera is where it was put, the pixels are its convention's.
            Assert.That(frame.Position, Is.EqualTo(Eye));
            float3 centreRay = frame.ToWorld(42, 29, front);
            Assert.That(centreRay.z, Is.EqualTo(front).Within(1e-5f));
        }

        [Test]
        public void SyntheticDepth_ACameraBox_MovesAndTurnsWithTheCamera()
        {
            var depth = new SyntheticDepthSource();
            depth.AddCameraBox(HandCentre, HandSize);
            float[] first = Copy(Read(depth, 0.0, Eye, quaternion.identity));
            // Somewhere else, looking somewhere else: the thing is carried along, so the picture of it is the same.
            DepthFrame moved = Read(depth, 1.0, new float3(0.7f, 1.4f, -0.3f),
                math.mul(quaternion.RotateY(1.1f), quaternion.RotateX(-0.2f)));
            for (int i = 0; i < first.Length; i++) Assert.That(moved.Depth[i], Is.EqualTo(first[i]), "pixel " + i);

            // In a room the walls go by behind it and the hand stays where it is in the picture.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            depth.Room = room;
            float[] facingZ = Copy(Read(depth, 2.0, Eye, quaternion.identity));
            float[] turned = Copy(Read(depth, 3.0, Eye, quaternion.RotateY(0.6f)));
            int hand = 0, wallChanged = 0;
            for (int i = 0; i < first.Length; i++)
            {
                if (first[i] != 0f && first[i] < 0.6f)
                {
                    hand++;
                    Assert.That(facingZ[i], Is.EqualTo(first[i]), "the hand hides the wall, pixel " + i);
                    Assert.That(turned[i], Is.EqualTo(first[i]), "pixel " + i);
                }
                else
                {
                    Assert.That(facingZ[i], Is.GreaterThan(1.4f), "the wall behind, pixel " + i);
                    if (turned[i] != facingZ[i]) wallChanged++;
                }
            }
            Assert.That(hand, Is.GreaterThan(160));
            Assert.That(wallChanged, Is.GreaterThan(1000), "turning the head changes what the walls return");
        }

        [Test]
        public void SyntheticDepth_ACameraBox_NearerThanLidarsRange_GivesNoReturn_AndHidesWhatIsBehindIt()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var depth = new SyntheticDepthSource { Room = room };
            // A palm 0.25 m from the lens: nearer than LiDAR's 0.3 m.
            depth.AddCameraBox(new float3(0f, 0f, 0.265f), new float3(0.09f, 0.09f, 0.03f));
            DepthFrame frame = Read(depth, 0.0, Eye, quaternion.identity);
            int centre = (frame.Height / 2) * frame.Width + frame.Width / 2;
            Assert.That(frame.Depth[centre], Is.EqualTo(0f), "no return from the palm, and none from the wall it hides");
            Assert.That(frame.Depth[(frame.Height / 2) * frame.Width + 3], Is.GreaterThan(1.4f), "beside it the wall is seen");
        }

        [Test]
        public void SyntheticDepth_CameraBoxes_AreClearedAndCounted_AndWithNoneTheImageIsWhatItWas()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var plain = new SyntheticDepthSource { Room = room };
            plain.AddBox(new float3(0.3f, 0f, 1.0f), new float3(0.4f, 1.4f, 0.4f));
            float[] before = Copy(Read(plain, 0.0, Eye, quaternion.RotateY(0.2f)));

            var depth = new SyntheticDepthSource { Room = room };
            depth.AddBox(new float3(0.3f, 0f, 1.0f), new float3(0.4f, 1.4f, 0.4f));
            for (int i = 0; i < SyntheticDepthSource.MaxCameraBoxes; i++)
                Assert.That(depth.AddCameraBox(HandCentre + new float3(0f, 0f, 0.01f * i), HandSize), Is.True);
            Assert.That(depth.AddCameraBox(HandCentre, HandSize), Is.False, "the list is full");
            Assert.That(depth.CameraBoxCount, Is.EqualTo(SyntheticDepthSource.MaxCameraBoxes));
            Assert.That(depth.ExtraBoxCount, Is.EqualTo(1), "the room's extra boxes are another list");

            depth.ClearCameraBoxes();
            Assert.That(depth.CameraBoxCount, Is.EqualTo(0));
            Assert.That(depth.ExtraBoxCount, Is.EqualTo(1));
            DepthFrame frame = Read(depth, 0.0, Eye, quaternion.RotateY(0.2f));
            for (int i = 0; i < before.Length; i++) Assert.That(frame.Depth[i], Is.EqualTo(before[i]), "pixel " + i);
        }

        [Test]
        public void SyntheticDepth_ARoomBox_MayHaveItsBaseAboveTheFloor()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            var depth = new SyntheticDepthSource { Room = room };
            // Something hanging a metre ahead between 1.3 m and 1.5 m above the floor: nothing stands under it.
            depth.AddBox(new float3(0f, 1.3f, 1.0f), new float3(0.2f, 0.2f, 0.1f));
            DepthFrame frame = Read(depth, 0.0, Eye, quaternion.identity);

            int u = frame.Width / 2;
            // Looking 0.2 m down at 0.95 m: the box's front. Looking 0.4 m down at 0.95 m: under it, the wall.
            int atBox = (int)math.round(frame.Cy + 0.2f / 0.95f * frame.Fy - 0.5f);
            int under = (int)math.round(frame.Cy + 0.4f / 0.95f * frame.Fy - 0.5f);
            Assert.That(under, Is.LessThan(frame.Height));
            Assert.That(frame.Depth[atBox * frame.Width + u], Is.EqualTo(0.95f).Within(1e-4f));
            Assert.That(frame.Depth[under * frame.Width + u], Is.EqualTo(1.5f).Within(1e-3f));
            Assert.That(frame.Depth[(frame.Height / 2) * frame.Width + u], Is.EqualTo(1.5f).Within(1e-3f), "over it, the wall");
        }
    }
}
