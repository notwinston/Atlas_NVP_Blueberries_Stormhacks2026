using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Photo
{
    /// <summary>The key-frame ring and its image kernels (contract lane C section 11.1).</summary>
    public unsafe class KeyFrameRingTests
    {
        static HeadPose Pose(double time, float3 position, float yawDeg = 0f, TrackingState tracking = TrackingState.Normal)
        {
            return new HeadPose(time, position, quaternion.RotateY(math.radians(yawDeg)), tracking);
        }

        [Test]
        public void Ring_IsBounded_AndDropsTheOldest()
        {
            var ring = new KeyFrameRing(4);
            Assert.That(ring.Capacity, Is.EqualTo(4));
            var first = new KeyFrame[4];
            for (int i = 0; i < 6; i++)
            {
                KeyFrame frame = ring.Add(Pose(i, new float3(i, 0f, 0f)));
                frame.EnsureImage(KeyFrameFormat.YCbCr420, 32, 16);
                if (i < 4) first[i] = frame;
                Assert.That(ring.Count, Is.LessThanOrEqualTo(4));
            }
            Assert.That(ring.Count, Is.EqualTo(4));
            Assert.That(ring.Evicted, Is.EqualTo(2));
            // Frames 2..5 are left, oldest first.
            for (int i = 0; i < 4; i++) Assert.That(ring[i].Time, Is.EqualTo(2.0 + i));
            // The dropped slots were reused, buffers and all.
            Assert.That(ring[2], Is.SameAs(first[0]));
            Assert.That(ring[3], Is.SameAs(first[1]));
            Assert.That(() => ring[4], Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void Ring_DefaultCapacityIs96()
        {
            var ring = new KeyFrameRing();
            for (int i = 0; i < 200; i++) ring.Add(Pose(i, new float3(i, 0f, 0f)));
            Assert.That(ring.Count, Is.EqualTo(96));
            Assert.That(ring[0].Time, Is.EqualTo(104.0));
            Assert.That(ring[95].Time, Is.EqualTo(199.0));
        }

        [Test]
        public void Reuse_KeepsBuffersOfTheSameSize()
        {
            var frame = new KeyFrame();
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 64, 32);
            byte[] luma = frame.Plane0, chroma = frame.Plane1;
            Assert.That(luma.Length, Is.EqualTo(64 * 32));
            Assert.That(chroma.Length, Is.EqualTo(32 * 16 * 2));
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 64, 32);
            Assert.That(frame.Plane0, Is.SameAs(luma));
            Assert.That(frame.Plane1, Is.SameAs(chroma));
            frame.EnsureImage(KeyFrameFormat.Rgba32, 8, 8);
            Assert.That(frame.Plane0.Length, Is.EqualTo(8 * 8 * 4));
            Assert.That(frame.Plane1, Is.Null);
        }

        [Test]
        public void ClearAndTakeAll_EmptyTheRing()
        {
            var ring = new KeyFrameRing(8);
            for (int i = 0; i < 3; i++) ring.Add(Pose(i, new float3(i, 0f, 0f)));
            KeyFrame[] taken = ring.TakeAll();
            Assert.That(taken.Length, Is.EqualTo(3));
            Assert.That(taken[0].Time, Is.EqualTo(0.0));
            Assert.That(ring.Count, Is.EqualTo(0));
            // A fresh sweep does not hand out a frame somebody else now owns.
            Assert.That(ring.Add(Pose(9, float3.zero)), Is.Not.SameAs(taken[0]));
            ring.Clear();
            Assert.That(ring.Count, Is.EqualTo(0));
            Assert.That(ring.Wants(Pose(10, float3.zero)), Is.True);
        }

        [Test]
        public void Gate_TheFirstTrackedFrameIsAKeyFrame()
        {
            var ring = new KeyFrameRing(8);
            Assert.That(ring.Wants(Pose(0.0, float3.zero, 0f, TrackingState.NotTracking)), Is.False);
            Assert.That(ring.Wants(Pose(0.016, float3.zero)), Is.True);
            // Still wanted until it is taken (the camera image may not be ready on the first try).
            Assert.That(ring.Wants(Pose(0.033, float3.zero)), Is.True);
        }

        [Test]
        public void Gate_OpensAfterAQuarterMetreOrFifteenDegrees()
        {
            var ring = new KeyFrameRing(8);
            HeadPose start = Pose(0.0, float3.zero);
            Assert.That(ring.Wants(in start), Is.True);
            ring.Add(in start);
            Assert.That(ring.Wants(Pose(1.0, new float3(0.1f, 0f, 0f))), Is.False);
            Assert.That(ring.Wants(Pose(2.0, new float3(0.2f, 0f, 0.1f))), Is.False);
            HeadPose moved = Pose(3.0, new float3(0.26f, 0f, 0f));
            Assert.That(ring.Wants(in moved), Is.True);
            ring.Add(in moved);
            Assert.That(ring.Wants(Pose(4.0, new float3(0.26f, 0f, 0f), 10f)), Is.False);
            Assert.That(ring.Wants(Pose(5.0, new float3(0.26f, 0f, 0f), 16f)), Is.True);
        }

        [Test]
        public void Gate_StaysShutWhileTurningFast()
        {
            var ring = new KeyFrameRing(8);
            HeadPose start = Pose(0.0, float3.zero);
            ring.Wants(in start);
            ring.Add(in start);
            // 20 degrees in a tenth of a second is 200 degrees a second: the image is smeared.
            Assert.That(ring.Wants(Pose(0.1, float3.zero, 20f)), Is.False);
            Assert.That(ring.Wants(Pose(0.2, float3.zero, 40f)), Is.False);
            // The turn slows to 10 degrees a second: now the frame is taken.
            Assert.That(ring.Wants(Pose(0.3, float3.zero, 41f)), Is.True);
        }

        [Test]
        public void AngleDeg_BetweenRotations()
        {
            Assert.That(KeyFrameRing.AngleDeg(quaternion.identity, quaternion.RotateY(math.radians(15f))), Is.EqualTo(15f).Within(1e-2f));
            quaternion q = quaternion.RotateX(1f);
            Assert.That(KeyFrameRing.AngleDeg(q, new quaternion(-q.value)), Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void StepFor_KeepsTheStoredWidthWithin1280()
        {
            Assert.That(KeyFrameRing.StepFor(640), Is.EqualTo(1));
            Assert.That(KeyFrameRing.StepFor(1280), Is.EqualTo(1));
            Assert.That(KeyFrameRing.StepFor(1920), Is.EqualTo(2));
            Assert.That(KeyFrameRing.StepFor(3840), Is.EqualTo(3));
        }

        [Test]
        public void CopyPlane_PacksRowsAndSkipsPadding()
        {
            // 4 x 2 pixels in rows of 6 bytes.
            byte[] source = { 1, 2, 3, 4, 99, 99, 5, 6, 7, 8, 99, 99 };
            var packed = new byte[8];
            fixed (byte* s = source)
            fixed (byte* d = packed)
                PhotoKernel.CopyPlane(s, 6, 1, 1, 1, 4, 2, d);
            Assert.That(packed, Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));
        }

        [Test]
        public void CopyPlane_KeepsEverySecondPixelWhenDownsizing()
        {
            // 4 x 4 luma, step 2 leaves 2 x 2.
            byte[] luma = { 10, 11, 12, 13, 20, 21, 22, 23, 30, 31, 32, 33, 40, 41, 42, 43 };
            var small = new byte[4];
            fixed (byte* s = luma)
            fixed (byte* d = small)
                PhotoKernel.CopyPlane(s, 4, 1, 1, 2, 2, 2, d);
            Assert.That(small, Is.EqualTo(new byte[] { 10, 12, 30, 32 }));

            // Chroma pairs: 4 pairs a row, two rows; step 2 leaves 2 pairs of the first row.
            byte[] chroma = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9, 9, 9, 9, 9, 9 };
            var pairs = new byte[4];
            fixed (byte* s = chroma)
            fixed (byte* d = pairs)
                PhotoKernel.CopyPlane(s, 8, 2, 2, 2, 2, 1, d);
            Assert.That(pairs, Is.EqualTo(new byte[] { 1, 2, 5, 6 }));
        }

        [Test]
        public void Depth_IsStoredInMillimetres_NoReturnAsZero()
        {
            float[] metres = { 1.2345f, 0.1f, 6f, float.NaN, 0f, 5f };
            var mm = new ushort[6];
            fixed (float* s = metres)
            fixed (ushort* d = mm)
                PhotoKernel.DepthToMillimetres(s, 6, d);
            Assert.That(mm, Is.EqualTo(new ushort[] { 1235, 0, 0, 0, 0, 5000 }));
        }

        [Test]
        public void DepthAt_MapsImagePixelsOntoTheDepthImage()
        {
            var frame = new KeyFrame();
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 1280, 720);
            Assert.That(frame.DepthAt(new float2(640f, 360f)), Is.EqualTo(0f));     // no depth yet
            frame.EnsureDepth(256, 192);
            frame.Depth[96 * 256 + 128] = 1600;
            Assert.That(frame.DepthAt(new float2(642f, 361f)), Is.EqualTo(1.6f).Within(1e-4f));
            Assert.That(frame.DepthAt(new float2(-5f, 361f)), Is.EqualTo(0f));
            Assert.That(frame.DepthAt(new float2(642f, 5000f)), Is.EqualTo(0f));
        }

        static KeyFrame Patterned(int period)
        {
            var frame = new KeyFrame();
            frame.EnsureImage(KeyFrameFormat.YCbCr420, 640, 360);
            for (int y = 0; y < 360; y++)
                for (int x = 0; x < 640; x++)
                    frame.Plane0[y * 640 + x] = period == 0 ? (byte)128 : (((x / period) + (y / period)) % 2 == 0 ? (byte)40 : (byte)210);
            return frame;
        }

        [Test]
        public void Sharpness_FlatIsZero_FineDetailBeatsCoarse()
        {
            KeyFrame flat = Patterned(0), fine = Patterned(4), coarse = Patterned(64);
            flat.MeasureSharpness();
            fine.MeasureSharpness();
            coarse.MeasureSharpness();
            Assert.That(flat.SharpnessReady, Is.True);
            Assert.That(flat.Sharpness, Is.EqualTo(0f));
            Assert.That(coarse.Sharpness, Is.GreaterThan(0f));
            Assert.That(fine.Sharpness, Is.GreaterThan(4f * coarse.Sharpness));
        }

        [Test]
        public void CropYCbCr_ConvertsFullRangeColour()
        {
            // 4 x 4: left half grey, right half red (Y 76, Cb 85, Cr 255).
            var luma = new byte[16];
            var chroma = new byte[8];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    luma[y * 4 + x] = x < 2 ? (byte)100 : (byte)76;
            for (int cy = 0; cy < 2; cy++)
            {
                chroma[cy * 4 + 0] = 128; chroma[cy * 4 + 1] = 128;
                chroma[cy * 4 + 2] = 85; chroma[cy * 4 + 3] = 255;
            }
            var rgba = new byte[4 * 4 * 4];
            var whole = new CropRect { X = 0, Y = 0, Width = 4, Height = 4 };
            fixed (byte* l = luma)
            fixed (byte* c = chroma)
            fixed (byte* o = rgba)
                PhotoKernel.CropYCbCr(l, c, 4, 4, in whole, 4, 4, o, false);
            Assert.That(rgba[0], Is.EqualTo(100));
            Assert.That(rgba[1], Is.EqualTo(100));
            Assert.That(rgba[2], Is.EqualTo(100));
            Assert.That(rgba[3], Is.EqualTo(255));
            int red = 3 * 4;
            Assert.That(rgba[red], Is.GreaterThanOrEqualTo(250));
            Assert.That(rgba[red + 1], Is.LessThanOrEqualTo(5));
            Assert.That(rgba[red + 2], Is.LessThanOrEqualTo(5));
        }

        [Test]
        public void Crop_TakesTheRectangle_ScalesDown_AndCanFlipRows()
        {
            // 8 x 8 luma whose value is 10 * row + column, grey chroma.
            var luma = new byte[64];
            var chroma = new byte[32];
            for (int i = 0; i < 64; i++) luma[i] = (byte)(10 * (i / 8) + i % 8);
            for (int i = 0; i < 32; i++) chroma[i] = 128;
            var crop = new CropRect { X = 2, Y = 4, Width = 4, Height = 4 };
            var rgba = new byte[2 * 2 * 4];
            fixed (byte* l = luma)
            fixed (byte* c = chroma)
            fixed (byte* o = rgba)
            {
                PhotoKernel.CropYCbCr(l, c, 8, 8, in crop, 2, 2, o, false);
                // Output pixel centres fall on source (3, 5), (5, 5), (3, 7), (5, 7).
                Assert.That(rgba[0], Is.EqualTo(53));
                Assert.That(rgba[4], Is.EqualTo(55));
                Assert.That(rgba[8], Is.EqualTo(73));
                Assert.That(rgba[12], Is.EqualTo(75));

                PhotoKernel.CropYCbCr(l, c, 8, 8, in crop, 2, 2, o, true);
                Assert.That(rgba[0], Is.EqualTo(73));
                Assert.That(rgba[8], Is.EqualTo(53));
            }
        }

        [Test]
        public void CropPacked_ReordersChannels()
        {
            // One BGRA pixel repeated: blue 10, green 20, red 30.
            var pixels = new byte[2 * 2 * 4];
            for (int i = 0; i < 4; i++)
            {
                pixels[i * 4] = 10; pixels[i * 4 + 1] = 20; pixels[i * 4 + 2] = 30; pixels[i * 4 + 3] = 0;
            }
            int r, g, b;
            KeyFrame.ChannelOffsets(KeyFrameFormat.Bgra32, out r, out g, out b);
            var rgba = new byte[4];
            var one = new CropRect { X = 1, Y = 1, Width = 1, Height = 1 };
            fixed (byte* p = pixels)
            fixed (byte* o = rgba)
                PhotoKernel.CropPacked(p, 2, 2, 4, r, g, b, in one, 1, 1, o, false);
            Assert.That(rgba, Is.EqualTo(new byte[] { 30, 20, 10, 255 }));
        }
    }
}
