using System;
using System.Globalization;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_Mask_Limits (hands design sections 7.1 and 9.1, as the review decided them): what Bump Guard
    /// holds a depth mask to, whatever built it. No sample deeper than its gate is masked and none beyond
    /// 0.80 m; a mask built for another depth image is ignored; a mask that would take more than its share of
    /// the image is ignored; at most two regions are read; the static map is never touched. The scenes are a
    /// walker in a scanned 7 x 7 m room with a level 256 x 192 depth camera, as in BumpGuard_Replay. Hands are
    /// <see cref="SimHand"/> plates that only the synthetic depth sees, with masks built the way the design
    /// describes. Where a mask must change nothing, a second Bump Guard is given the same frames with no mask
    /// and the two are compared bit for bit.
    /// </summary>
    [TestFixture]
    public unsafe class BumpGuardMaskLimitsTests
    {
        const float RoomHalf = 3.5f;
        const int W = BumpScenario.DepthW, H = BumpScenario.DepthH;
        const int Stride = BumpConstants.DepthStride;

        static readonly SimPose Standing = new SimPose { Position = new float2(0f, -2.5f), Yaw = 0f };

        static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }

        // One region that covers the whole picture: only its gate, and Bump Guard's own limits, decide.
        static DepthMask WholePicture(float gate)
        {
            return new DepthMask
            {
                Count = 1,
                Region0 = new DepthMaskRegion { MinU = 0f, MinV = 0f, MaxU = W, MaxV = H, MaxDepth = gate }
            };
        }

        // Of the samples the kernel reads in the scenario's last image: those with a return, and those of them
        // the mask would take out.
        static void CountSamples(BumpScenario sim, in DepthMask mask, out int valid, out int covered)
        {
            DepthKernelMask kernel = BumpGuard.KernelMask(mask, mask.DepthSequence);
            valid = 0;
            covered = 0;
            int first = BumpDepthKernel.FirstSample(Stride);
            for (int v = first; v < H; v += Stride)
                for (int u = first; u < W; u += Stride)
                {
                    float d = sim.Depth[v * W + u];
                    if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;
                    valid++;
                    if (kernel.Covers(u + 0.5f, v + 0.5f, d)) covered++;
                }
        }

        // ---- BumpGuard_Mask_Limits: the gate ----

        [TestCase(0.45f)]
        [TestCase(0.65f)]
        [TestCase(0.80f)]
        public void BumpGuard_Mask_Limits_NoSampleDeeperThanItsGateIsMasked(float gate)
        {
            // A tray carried in front of the camera, one centimetre inside the gate and one beyond it, under a
            // mask that covers the whole picture.
            foreach (float depth in new[] { gate - 0.01f, gate + 0.01f })
            {
                bool inside = depth <= gate;
                using (var masked = new BumpScenario(RoomHalf, RoomHalf))
                using (var bare = new BumpScenario(RoomHalf, RoomHalf))
                {
                    var tray = new SimPlate(new float3(0f, -0.05f, depth), 0.24f, 0.16f);
                    masked.Body.Add(tray);
                    bare.Body.Add(tray);
                    masked.Mask = WholePicture(gate);
                    for (int k = 0; k < 30; k++)
                    {
                        masked.StepFrame(Standing);
                        bare.StepFrame(Standing);
                        DepthFrameResult result = masked.Core.LastDepth;
                        Assert.That(result.MaskRefused, Is.False, "the tray is well under the share");
                        if (inside)
                        {
                            Assert.That(result.Masked, Is.GreaterThan(100), "the tray is inside the gate");
                            Assert.That(result.Stamped, Is.EqualTo(0));
                            Assert.That(masked.Core.DynamicCellCount, Is.EqualTo(0));
                        }
                        else
                        {
                            Assert.That(result.Masked, Is.EqualTo(0), "nothing in the picture is inside the gate");
                            BumpCompare.Same(masked.Core, bare.Core, "tray " + F(depth) + " m, gate " + F(gate) + " m", k);
                        }
                    }
                    Assert.That(bare.Core.DynamicCellCount, Is.GreaterThan(0), "without a mask the tray is an obstacle");
                }
            }
        }

        [TestCase(5.0f)]
        [TestCase(float.PositiveInfinity)]
        public void BumpGuard_Mask_Limits_NothingBeyond80cmIsMasked(float askedGate)
        {
            // The mask asks for far more than Bump Guard allows. A tray at 0.79 m is taken out; at 0.81 m it is
            // not, and neither is anything else in the room.
            foreach (float depth in new[] { 0.79f, 0.81f })
            {
                using (var masked = new BumpScenario(RoomHalf, RoomHalf))
                using (var bare = new BumpScenario(RoomHalf, RoomHalf))
                {
                    var tray = new SimPlate(new float3(0f, -0.05f, depth), 0.24f, 0.16f);
                    var chair = SimBox.OnFloor(0.6f, -0.5f, 0.45f, 0.45f, 0.90f);
                    masked.Body.Add(tray);
                    bare.Body.Add(tray);
                    masked.Boxes.Add(chair);
                    bare.Boxes.Add(chair);
                    masked.Mask = WholePicture(askedGate);
                    for (int k = 0; k < 30; k++)
                    {
                        masked.StepFrame(Standing);
                        bare.StepFrame(Standing);
                        if (depth > BumpConstants.MaskMaxDepth)
                        {
                            Assert.That(masked.Core.LastDepth.Masked, Is.EqualTo(0));
                            BumpCompare.Same(masked.Core, bare.Core, "tray at " + F(depth) + " m", k);
                        }
                    }
                    if (depth <= BumpConstants.MaskMaxDepth)
                    {
                        CountSamples(masked, masked.Mask, out int valid, out int covered);
                        Assert.That(masked.Core.LastDepth.Masked, Is.EqualTo(covered).And.GreaterThan(50), "only the tray");
                        // The chair, 2 m away under the same region, is stamped as it is with no mask.
                        Assert.That(masked.Core.DynamicCellCount, Is.GreaterThan(8));
                        Assert.That(masked.Core.DynamicCellCount, Is.LessThan(bare.Core.DynamicCellCount), "the tray's cells are the only ones missing");
                    }
                }
            }
        }

        // ---- BumpGuard_Mask_Limits: what the kernel takes out, against the limits worked out here ----

        // Distance from (u, v) to the segment of a region's strip, in double precision and by projection onto
        // the segment's unit direction: not the kernel's arithmetic.
        static double DistanceToStrip(in DepthKernelMaskRegion r, float u, float v)
        {
            double ax = r.ArmU0, ay = r.ArmV0, bx = r.ArmU1, by = r.ArmV1;
            double length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
            if (length < 1e-3) return Math.Sqrt((u - ax) * (u - ax) + (v - ay) * (v - ay));
            double dx = (bx - ax) / length, dy = (by - ay) / length;
            double along = (u - ax) * dx + (v - ay) * dy;
            if (along < 0.0) along = 0.0;
            else if (along > length) along = length;
            double px = ax + along * dx, py = ay + along * dy;
            return Math.Sqrt((u - px) * (u - px) + (v - py) * (v - py));
        }

        // +1 the limits allow this sample to be masked, -1 they do not, 0 it lies within a thousandth of a pixel
        // of a strip's edge, where single and double precision may disagree.
        static int MayBeMasked(in DepthKernelMask mask, float u, float v, float d)
        {
            int verdict = -1;
            int regions = math.clamp(mask.Count, 0, BumpConstants.MaskMaxRegions);
            for (int k = 0; k < regions; k++)
            {
                DepthKernelMaskRegion r = k == 0 ? mask.Region0 : mask.Region1;
                if (float.IsNaN(r.MaxDepth) || d > r.MaxDepth || d > BumpConstants.MaskMaxDepth) continue;
                if (u >= r.MinU && u <= r.MaxU && v >= r.MinV && v <= r.MaxV) return 1;
                if (!(r.ArmHalfWidth > 0f)) continue;
                double edge = DistanceToStrip(r, u, v) - r.ArmHalfWidth;
                if (edge < -1e-3) return 1;
                if (edge <= 1e-3) verdict = 0;
            }
            return verdict;
        }

        static DepthKernelMaskRegion RandomRegion(ref Unity.Mathematics.Random random)
        {
            float u = random.NextFloat(-20f, W + 20f), v = random.NextFloat(-20f, H + 20f);
            float halfU = random.NextFloat(2f, 110f), halfV = random.NextFloat(2f, 90f);
            var r = new DepthKernelMaskRegion
            {
                MinU = u - halfU,
                MaxU = u + halfU,
                MinV = v - halfV,
                MaxV = v + halfV,
                ArmU0 = u + random.NextFloat(-10f, 10f),
                ArmV0 = v + halfV,
                ArmU1 = u + random.NextFloat(-80f, 80f),
                ArmV1 = random.NextBool() ? H : random.NextFloat(0f, H),
                ArmHalfWidth = random.NextFloat(-5f, 45f),
                MaxDepth = random.NextFloat(0.25f, 1.3f)
            };
            switch (random.NextInt(0, 12))
            {
                case 0: r.MaxDepth = float.NaN; break;
                case 1: r.MaxDepth = float.PositiveInfinity; break;
                case 2: r.MaxDepth = -1f; break;
                case 3: r.MinU = float.NaN; break;                          // no box; the strip is still read
                case 4: r.ArmHalfWidth = float.NaN; break;                  // no strip
                case 5: r.MinU = float.NegativeInfinity; r.MaxU = float.PositiveInfinity; break;
                case 6: r.MinU = r.MaxU + 1f; break;                        // a box turned inside out
            }
            return r;
        }

        [Test]
        public void BumpGuard_Mask_Limits_TheKernelTakesOutWhatTheLimitsAllowAndNoMore()
        {
            var random = new Unity.Mathematics.Random(20261004u);
            var depth = new float[W * H];
            float f = (H * 0.5f) / math.tan(math.radians(24f));
            int first = BumpDepthKernel.FirstSample(Stride);
            int honoured = 0, refused = 0, untouched = 0, unsure = 0;

            for (int trial = 0; trial < 600; trial++)
            {
                // A picture of near and far returns and of pixels with none. One picture in three gets a region
                // over most of it and many near returns, so that the share is put to the test.
                bool big = trial % 3 == 0;
                float nearShare = big ? random.NextFloat(0.3f, 0.95f) : random.NextFloat(0f, 0.9f);
                for (int v = first; v < H; v += Stride)
                    for (int u = first; u < W; u += Stride)
                    {
                        float roll = random.NextFloat();
                        float d;
                        if (roll < 0.03f) d = 0f;
                        else if (roll < 0.05f) d = float.NaN;
                        else if (roll < 0.07f) d = 0.2f;
                        else if (roll < 0.09f) d = 7f;
                        else d = random.NextFloat() < nearShare ? random.NextFloat(0.3f, 1.0f) : random.NextFloat(1.0f, 5.0f);
                        depth[v * W + u] = d;
                    }

                var mask = new DepthKernelMask { Region0 = RandomRegion(ref random), Region1 = RandomRegion(ref random) };
                int roll2 = random.NextInt(0, 10);
                mask.Count = roll2 == 0 ? 0 : roll2 == 1 ? -3 : roll2 == 2 ? 3 : roll2 == 3 ? 200 : roll2 < 7 ? 1 : 2;
                if (big)
                {
                    mask.Region0.MinU = random.NextFloat(-10f, 60f);
                    mask.Region0.MaxU = W - random.NextFloat(-10f, 60f);
                    mask.Region0.MinV = random.NextFloat(-10f, 40f);
                    mask.Region0.MaxV = H - random.NextFloat(-10f, 40f);
                    mask.Region0.MaxDepth = random.NextFloat(0.5f, 1.2f);
                    if (mask.Count <= 0) mask.Count = 1;
                }

                int valid = 0, certain = 0, possible = 0;
                for (int v = first; v < H; v += Stride)
                    for (int u = first; u < W; u += Stride)
                    {
                        float d = depth[v * W + u];
                        if (!(d >= BumpConstants.DepthMin && d <= BumpConstants.DepthMax)) continue;
                        valid++;
                        int verdict = MayBeMasked(mask, u + 0.5f, v + 0.5f, d);
                        if (verdict > 0) certain++;
                        if (verdict >= 0) possible++;
                    }

                var input = new DepthKernelInput
                {
                    Width = W, Height = H, Stride = Stride,
                    Intrinsics = new DepthIntrinsics(f, f, W * 0.5f, H * 0.5f),
                    SensorPosition = new float3(0f, 1.6f, 0f),
                    SensorRotation = quaternion.identity,
                    Time = trial, Sequence = trial, MapTrusted = false,
                    Mask = mask
                };
                DepthFrameResult result;
                fixed (float* p = depth) BumpDepthKernel.Process(p, input, null, default, default, out result);

                float limit = BumpConstants.MaskMaxShare * valid;
                string where = "trial " + trial;
                if (certain > limit)
                {
                    Assert.That(result.MaskRefused, Is.True, where + ": over the share");
                    Assert.That(result.Masked, Is.EqualTo(0), where);
                    refused++;
                }
                else if (possible <= limit)
                {
                    Assert.That(result.MaskRefused, Is.False, where);
                    Assert.That(result.Masked, Is.InRange(certain, possible), where);
                    if (possible > 0) honoured++;
                    else untouched++;
                }
                else
                {
                    unsure++;   // the share falls between the two counts: either answer is right
                    Assert.That(result.Masked == 0 || (result.Masked >= certain && result.Masked <= possible), where);
                }
                Assert.That(result.Forgotten, Is.EqualTo(0), where + ": the map is not trusted, the layer is not touched");
            }

            TestContext.WriteLine("600 pictures: mask honoured {0}, refused {1}, nothing to take {2}, on the edge {3}", honoured, refused, untouched, unsure);
            Assert.That(honoured, Is.GreaterThan(100));
            Assert.That(refused, Is.GreaterThan(50));
            Assert.That(untouched, Is.GreaterThan(50));
        }

        [Test]
        public void BumpGuard_Mask_Limits_TheKernelsMaskIsTheSharedMaskCutAtTheCap()
        {
            // Bump Guard reads the regions itself. This holds its reading to DepthMask.Contains, the definition
            // the hands build to, and to the 0.80 m cap that only Bump Guard adds.
            var random = new Unity.Mathematics.Random(77u);
            int coveredCount = 0, beyondCap = 0;
            for (int trial = 0; trial < 2000; trial++)
            {
                DepthKernelMaskRegion a = RandomRegion(ref random), b = RandomRegion(ref random);
                var shared = new DepthMask { Count = random.NextInt(1, 3), DepthSequence = trial, Region0 = Shared(a), Region1 = Shared(b) };
                DepthKernelMask kernel = BumpGuard.KernelMask(shared, trial);
                for (int n = 0; n < 50; n++)
                {
                    float u = random.NextFloat(0f, W), v = random.NextFloat(0f, H), d = random.NextFloat(0.3f, 1.4f);
                    bool contains = shared.Contains(u, v, d);
                    bool covers = kernel.Covers(u, v, d);
                    Assert.That(covers, Is.EqualTo(contains && d <= BumpConstants.MaskMaxDepth), "trial " + trial);
                    if (covers) coveredCount++;
                    if (contains && !covers) beyondCap++;
                }
            }
            Assert.That(coveredCount, Is.GreaterThan(1000));
            Assert.That(beyondCap, Is.GreaterThan(100), "the cap was exercised");
        }

        static DepthMaskRegion Shared(in DepthKernelMaskRegion r)
        {
            return new DepthMaskRegion
            {
                MinU = r.MinU, MinV = r.MinV, MaxU = r.MaxU, MaxV = r.MaxV,
                ArmU0 = r.ArmU0, ArmV0 = r.ArmV0, ArmU1 = r.ArmU1, ArmV1 = r.ArmV1, ArmHalfWidth = r.ArmHalfWidth,
                MaxDepth = r.MaxDepth
            };
        }

        // ---- BumpGuard_Mask_Limits: the share ----

        [Test]
        public void BumpGuard_Mask_Limits_TwoOpenHandsAtArmsLengthAreMasked()
        {
            // The share is of the samples with a return. In the middle of the room every sample has one; looking
            // down 6 m of room, a third of the picture is beyond LiDAR's 5 m and the same hands are a larger share.
            var middle = new SimPose { Position = float2.zero, Yaw = 0f };
            foreach (bool longView in new[] { false, true })
                foreach (float depth in new[] { 0.32f, 0.35f, 0.40f, 0.45f, 0.50f, 0.60f })
                {
                    using (var sim = new BumpScenario(RoomHalf, RoomHalf))
                    {
                        var left = new SimHand(-0.11f, -0.05f, depth, true);
                        var right = new SimHand(0.11f, -0.05f, depth, true);
                        left.AddTo(sim.Body);
                        right.AddTo(sim.Body);
                        sim.Mask = sim.MaskFor(left, right);
                        sim.Hold(longView ? Standing : middle, 0.5f);

                        CountSamples(sim, sim.Mask, out int valid, out int covered);
                        float share = covered / (float)valid;
                        bool refused = sim.Core.LastDepth.MaskRefused;
                        TestContext.WriteLine("two open hands {0} m ahead, {1}: the mask takes {2} of {3} valid samples, {4:F1} %{5}",
                            F(depth), longView ? "6 m of room ahead" : "walls 3.5 m away", covered, valid, 100f * share, refused ? ": refused" : "");
                        Assert.That(refused, Is.EqualTo(share > BumpConstants.MaskMaxShare));
                        if (!longView || depth >= 0.45f)
                        {
                            Assert.That(refused, Is.False, "refused at " + depth);
                            Assert.That(sim.Core.LastDepth.Masked, Is.EqualTo(covered).And.GreaterThan(0));
                            Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(0), "no hand was stamped");
                            Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Clear));
                        }
                        if (!longView && depth >= 0.45f) Assert.That(share, Is.LessThan(0.20f), "arm's length is far from the limit");
                    }
                }
        }

        [Test]
        public void BumpGuard_Mask_Limits_AMaskThatWouldTakeMostOfTheViewIsIgnored()
        {
            // The same two hands 0.6 m from a wall: the wall is inside their gate, so the regions would take the
            // wall with them. Bump Guard refuses the mask and reads the image as if it had none.
            var nearTheWall = new SimPose { Position = new float2(0f, RoomHalf - 0.6f), Yaw = 0f };
            foreach (float depth in new[] { 0.40f, 0.45f, 0.50f, 0.60f })
            {
                using (var masked = new BumpScenario(RoomHalf, RoomHalf))
                using (var bare = new BumpScenario(RoomHalf, RoomHalf))
                {
                    var left = new SimHand(-0.11f, -0.05f, depth, true);
                    var right = new SimHand(0.11f, -0.05f, depth, true);
                    foreach (BumpScenario sim in new[] { masked, bare })
                    {
                        left.AddTo(sim.Body);
                        right.AddTo(sim.Body);
                    }
                    masked.Mask = masked.MaskFor(left, right);
                    for (int k = 0; k < 30; k++)
                    {
                        masked.StepFrame(nearTheWall);
                        bare.StepFrame(nearTheWall);
                        Assert.That(masked.Core.LastDepth.MaskRefused, Is.True);
                        Assert.That(masked.Core.LastDepth.Masked, Is.EqualTo(0));
                        BumpCompare.Same(masked.Core, bare.Core, "hands " + F(depth) + " m ahead, wall at 0.6 m", k);
                    }
                    CountSamples(masked, masked.Mask, out int valid, out int covered);
                    TestContext.WriteLine("two hands {0} m ahead, a wall 0.6 m ahead: the mask would take {1} of {2} valid samples, {3:F1} %: refused",
                        F(depth), covered, valid, 100f * covered / valid);
                    Assert.That(covered, Is.GreaterThan(BumpConstants.MaskMaxShare * valid));
                }
            }

            // And the plainest case: a region over the whole picture with the wall inside its gate.
            using (var masked = new BumpScenario(RoomHalf, RoomHalf))
            using (var bare = new BumpScenario(RoomHalf, RoomHalf))
            {
                masked.Mask = WholePicture(BumpConstants.MaskMaxDepth);
                for (int k = 0; k < 30; k++)
                {
                    masked.StepFrame(nearTheWall);
                    bare.StepFrame(nearTheWall);
                    Assert.That(masked.Core.LastDepth.MaskRefused, Is.True);
                    BumpCompare.Same(masked.Core, bare.Core, "whole picture, wall at 0.6 m", k);
                }
            }
        }

        [TestCase(1075, true)]
        [TestCase(1076, false)]
        public void BumpGuard_Mask_Limits_TheShareIsCountedToTheSample(int near, bool honoured)
        {
            // 64 x 48 = 3,072 samples, every one with a return, and a region over the whole picture. 35 % of them
            // is 1,075.2: a mask that would take 1,075 is honoured, one that would take 1,076 is not.
            var depth = new float[W * H];
            int first = BumpDepthKernel.FirstSample(Stride);
            int n = 0;
            for (int u = first; u < W; u += Stride)
                for (int v = first; v < H; v += Stride)
                    depth[v * W + u] = n++ < near ? 0.5f : 2.0f;
            Assert.That(n, Is.EqualTo(3072));

            float f = (H * 0.5f) / math.tan(math.radians(24f));
            var input = new DepthKernelInput
            {
                Width = W, Height = H, Stride = Stride,
                Intrinsics = new DepthIntrinsics(f, f, W * 0.5f, H * 0.5f),
                SensorPosition = new float3(0f, 1.6f, 0f),
                SensorRotation = quaternion.identity,
                Time = 1.0, Sequence = 1, MapTrusted = false,
                Mask = BumpGuard.KernelMask(WholePicture(0.6f), 0)
            };
            DepthFrameResult result;
            fixed (float* p = depth) BumpDepthKernel.Process(p, input, null, default, default, out result);
            Assert.That(result.MaskRefused, Is.EqualTo(!honoured));
            Assert.That(result.Masked, Is.EqualTo(honoured ? near : 0));
        }

        // ---- BumpGuard_Mask_Limits: the regions ----

        [Test]
        public void BumpGuard_Mask_Limits_AtMostTwoRegionsAreRead()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var left = new SimHand(-0.11f, -0.05f, 0.45f, true);
                var right = new SimHand(0.11f, -0.05f, 0.45f, true);
                left.AddTo(sim.Body);
                right.AddTo(sim.Body);
                DepthMask two = sim.MaskFor(left, right);
                sim.Mask = two;
                sim.StepFrame(Standing);
                int maskedByTwo = sim.Core.LastDepth.Masked;
                Assert.That(maskedByTwo, Is.GreaterThan(0));

                // A count that claims more regions than there are is read as two.
                DepthMask many = two;
                many.Count = 7;
                Assert.That(BumpGuard.KernelMask(many, many.DepthSequence).Count, Is.EqualTo(BumpConstants.MaskMaxRegions));
                sim.Mask = many;
                sim.StepFrame(Standing);
                Assert.That(sim.Core.LastDepth.Masked, Is.EqualTo(maskedByTwo));

                // Straight into the kernel, which does not rely on the adapter's cut.
                fixed (float* d = sim.Depth)
                {
                    DepthKernelInput input = sim.Core.MakeDepthInput(W, H, sim.Intrinsics, new float3(0f, BumpScenario.HeadHeight, -2.5f),
                        quaternion.identity, sim.Time, true, true);
                    input.Mask = BumpGuard.KernelMask(two, two.DepthSequence);
                    input.Mask.Count = 1000;
                    BumpDepthKernel.Process(d, input, sim.Core.StaticField, sim.Core.Grid, sim.Core.Dynamic, out DepthFrameResult result);
                    Assert.That(result.Masked, Is.EqualTo(maskedByTwo));

                    // One region: the other hand is an obstacle again.
                    input.Mask.Count = 1;
                    BumpDepthKernel.Process(d, input, sim.Core.StaticField, sim.Core.Grid, sim.Core.Dynamic, out result);
                    Assert.That(result.Masked, Is.GreaterThan(0).And.LessThan(maskedByTwo));
                    Assert.That(result.Stamped, Is.GreaterThan(0));

                    // No region, and a count below zero: the empty mask.
                    input.Mask.Count = -2;
                    Assert.That(input.Mask.IsEmpty, Is.True);
                    BumpDepthKernel.Process(d, input, sim.Core.StaticField, sim.Core.Grid, sim.Core.Dynamic, out result);
                    Assert.That(result.Masked, Is.EqualTo(0));
                    Assert.That(result.MaskRefused, Is.False);
                }
                Assert.That(BumpGuard.KernelMask(default(DepthMask), 0).IsEmpty, Is.True);
            }
        }

        // ---- BumpGuard_Mask_Limits: the depth image the mask was built for ----

        [Test]
        public void BumpGuard_Mask_Limits_AMaskBuiltForAnotherImageIsIgnored()
        {
            // The adapter, as the app ticks it: the fixture room, a hand 0.45 m ahead of a walker who stands in
            // its middle, and four Bump Guards that get the same sensor frames with a different mask each.
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var matching = new BumpGuard(false))
            using (var stale = new BumpGuard(false))
            using (var early = new BumpGuard(false))
            using (var none = new BumpGuard(false))
            {
                foreach (BumpGuard guard in new[] { matching, stale, early, none }) guard.SetRoom(room);
                var hand = new SimHand(0.03f, -0.05f, 0.45f, true);
                hand.AddTo(eye.Body);
                var head = new float3(0f, BumpScenario.HeadHeight, 0f);

                for (int k = 0; k < 30; k++)
                {
                    int sequence = 100 + k;
                    eye.Render(head, quaternion.identity);
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, sequence, SimHand.Mask(sequence, eye.Intrinsics, H, hand));
                    matching.Tick(in frame);
                    frame.Mask.DepthSequence = sequence - 1;
                    stale.Tick(in frame);
                    frame.Mask.DepthSequence = sequence + 1;
                    early.Tick(in frame);
                    frame.Mask = default(DepthMask);
                    none.Tick(in frame);

                    Assert.That(matching.Core.LastDepth.Masked, Is.GreaterThan(100), "the mask of this image is used");
                    Assert.That(matching.Core.DynamicCellCount, Is.EqualTo(0));
                    Assert.That(stale.Core.LastDepth.Masked, Is.EqualTo(0));
                    Assert.That(early.Core.LastDepth.Masked, Is.EqualTo(0));
                    BumpCompare.Same(stale.Core, none.Core, "a mask for the image before", k);
                    BumpCompare.Same(early.Core, none.Core, "a mask for the image after", k);
                }
                Assert.That(none.Core.DynamicCellCount, Is.GreaterThan(0), "with no mask the hand is an obstacle");
            }
        }

        // ---- BumpGuard_Mask_Limits: the static map ----

        [TestCase(0.5f)]
        [TestCase(1.0f)]
        public void BumpGuard_Mask_Limits_StaticClearanceIsTheSameWithAndWithoutAMask(float speed)
        {
            using (var hands = new BumpScenario(RoomHalf, RoomHalf))
            using (var bare = new BumpScenario(RoomHalf, RoomHalf))
            {
                var left = new SimHand(-0.11f, -0.05f, 0.45f, true);
                var right = new SimHand(0.11f, -0.05f, 0.45f, true);
                left.AddTo(hands.Body);
                right.AddTo(hands.Body);
                hands.Mask = hands.MaskFor(left, right);

                // The distance field itself, before anything is processed.
                int cells = hands.Core.Grid.Width * hands.Core.Grid.Height;
                var field = new float[cells];
                for (int k = 0; k < cells; k++) field[k] = hands.Core.StaticField[k];

                // Along the +X wall at half a metre, then straight at the +Z wall until the path reaches it.
                int frame = 0;
                float along = 3.5f;
                for (int k = 0; k < (int)(along / speed * BumpScenario.FrameRate); k++, frame++)
                {
                    var pose = new SimPose { Position = new float2(RoomHalf - 0.5f, -2.0f + speed * k / BumpScenario.FrameRate), Yaw = 0f };
                    hands.StepFrame(pose);
                    bare.StepFrame(pose);
                    AssertSameClearance(hands.Core, bare.Core, "along the wall", frame);
                }
                bool refusedNearTheWall = false, maskedBeforeTheCue = false;
                for (int k = 0; ; k++, frame++)
                {
                    var pose = new SimPose { Position = new float2(0f, 0.3f + speed * k / BumpScenario.FrameRate), Yaw = 0f };
                    if (hands.MapClearance(pose.Position) <= 0f) break;
                    hands.StepFrame(pose);
                    bare.StepFrame(pose);
                    AssertSameClearance(hands.Core, bare.Core, "at the wall", frame);
                    refusedNearTheWall |= hands.Core.LastDepth.MaskRefused;
                    maskedBeforeTheCue |= hands.Core.LastDepth.Masked > 0 && bare.Core.Level == BumpLevel.Clear;
                }

                Assert.That(maskedBeforeTheCue, Is.True, "the hands were masked during the approach");
                Assert.That(refusedNearTheWall, Is.True, "and the mask was refused once the wall was inside its gate");
                Assert.That(bare.Changes.Count, Is.GreaterThan(0), "the wall raised its warning");
                Assert.That(hands.Changes.Count, Is.EqualTo(bare.Changes.Count), "level changes");
                for (int c = 0; c < bare.Changes.Count; c++)
                {
                    Assert.That(hands.Changes[c].Time, Is.EqualTo(bare.Changes[c].Time), "time of level change " + c);
                    Assert.That(hands.Changes[c].To, Is.EqualTo(bare.Changes[c].To), "level change " + c);
                }
                for (int k = 0; k < cells; k++)
                    if (BitConverter.SingleToInt32Bits(field[k]) != BitConverter.SingleToInt32Bits(hands.Core.StaticField[k]))
                        Assert.Fail("the distance field changed at cell " + k);
            }
        }

        // clear(head) and both paths come from the static map alone while nothing dynamic is near: bit for bit
        // the same with masked hands in the picture as with no hands at all.
        static void AssertSameClearance(BumpGuardCore hands, BumpGuardCore bare, string where, int frame)
        {
            if (BitConverter.SingleToInt32Bits(hands.Now) != BitConverter.SingleToInt32Bits(bare.Now) ||
                BitConverter.SingleToInt32Bits(hands.Path04) != BitConverter.SingleToInt32Bits(bare.Path04) ||
                BitConverter.SingleToInt32Bits(hands.Path08) != BitConverter.SingleToInt32Bits(bare.Path08) ||
                hands.Level != bare.Level || hands.RawLevel != bare.RawLevel)
                Assert.Fail(where + ", frame " + frame + ": clear(head) " + hands.Now.ToString("R") + " against " + bare.Now.ToString("R") +
                            ", level " + hands.Level + " against " + bare.Level);
            float2 probe = new float2(RoomHalf - 0.2f, 0.4f);
            if (hands.ClearAt(probe, 0.0) != bare.ClearAt(probe, 0.0)) Assert.Fail(where + ", frame " + frame + ": clear(p) beside the wall");
        }

        [Test]
        public void MaskConstants_AreTheDecidedValues()
        {
            Assert.That(BumpConstants.MaskMaxRegions, Is.EqualTo(2));
            Assert.That(BumpConstants.MaskMaxRegions, Is.EqualTo(DepthMask.MaxRegions));
            Assert.That(BumpConstants.MaskMaxDepth, Is.EqualTo(0.80f));
            Assert.That(BumpConstants.MaskMaxShare, Is.EqualTo(0.35f));
            Assert.That(BumpConstants.MaskYoungSeconds, Is.EqualTo(1.0));
            Assert.That(BumpConstants.MaskHeightTolerance, Is.EqualTo(0.15f));
            // A hand nearer than LiDAR's range has no return to mask; the cap lies inside the range.
            Assert.That(BumpConstants.MaskMaxDepth, Is.GreaterThan(BumpConstants.DepthMin).And.LessThan(BumpConstants.DepthMax));
        }
    }
}
