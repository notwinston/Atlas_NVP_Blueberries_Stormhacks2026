using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// The depth mask on the frame path (hands design section 7.1): a sample the mask covers is no return for
    /// every reader of the image, with the map trusted or not; the masked depth step allocates nothing; what
    /// the mask costs on one image; the Burst job gives what the inline kernel gives; and Bump Guard's sources
    /// name no hands assembly (rule 1). The scenes are those of <see cref="BumpGuardMaskLimitsTests"/>.
    /// </summary>
    [TestFixture]
    public unsafe class BumpGuardMaskFrameTests
    {
        const float RoomHalf = 3.5f;
        const int W = BumpScenario.DepthW, H = BumpScenario.DepthH;
        const float FrameSeconds = 1f / BumpScenario.FrameRate;

        // ---- A sample the mask covers is no return, for every reader of the image ----

        [Test]
        public void MaskedSamples_AreNoReturnForTheSectorsTheLayerAndTheRescanCheck()
        {
            using (var masked = new BumpScenario(RoomHalf, RoomHalf))
            using (var open = new BumpScenario(RoomHalf, RoomHalf))
            using (var unmasked = new BumpScenario(RoomHalf, RoomHalf))
            {
                // 0.9 m from the +Z wall: the wall fills the rescan check, and a hand in front of it would stamp.
                var pose = new SimPose { Position = new float2(0f, RoomHalf - 0.9f), Yaw = 0f };
                var hand = new SimHand(0f, -0.05f, 0.45f, true);
                hand.AddTo(masked.Body);
                hand.AddTo(unmasked.Body);
                masked.Mask = masked.MaskFor(hand);
                for (int k = 0; k < 30; k++)
                {
                    masked.StepFrame(pose);
                    open.StepFrame(pose);
                    unmasked.StepFrame(pose);
                }
                DepthFrameResult withMask = masked.Core.LastDepth, noHand = open.Core.LastDepth, noMask = unmasked.Core.LastDepth;

                // The middle sector: the unmasked hand is its nearest return; masked, the wall behind is, as with no hand.
                Assert.That(noMask.Sector(3), Is.InRange(0.40f, 0.46f), "the hand and its forearm");
                Assert.That(withMask.Sector(3), Is.EqualTo(noHand.Sector(3)));
                Assert.That(withMask.Sector(3), Is.EqualTo(0.9f).Within(0.01f));
                // The layer: nothing stamped, nothing cleared by a masked ray.
                Assert.That(noMask.Stamped, Is.GreaterThan(0));
                Assert.That(withMask.Stamped, Is.EqualTo(0));
                Assert.That(masked.Core.DynamicCellCount, Is.EqualTo(0));
                // The rescan check: the hand's samples are not wall samples, and the wall it hides is not counted.
                // 0.45 m in front of the wall and within half a metre of the floor's edge, an unmasked hand is
                // counted as wall that stands 0.45 m off the map.
                Assert.That(noMask.WallSamples, Is.EqualTo(noHand.WallSamples));
                Assert.That(withMask.WallSamples, Is.EqualTo(noHand.WallSamples - withMask.Masked), "each masked sample hides one sample of the wall");
                Assert.That(withMask.WallDisagreement, Is.EqualTo(noHand.WallDisagreement), "the median of the wall in view is the wall's");
                Assert.That(masked.Core.RescanAdvised, Is.False);
            }
        }

        [Test]
        public void WithTheMapNotTrusted_AMaskedHandIsLeftOutOfTheSectors()
        {
            // Tracking is not Normal: live depth alone warns, from the seven sectors. A masked hand is no return
            // there either; whatever stands behind it and beyond its gate is read as with no hand.
            using (var masked = new BumpScenario(RoomHalf, RoomHalf))
            using (var open = new BumpScenario(RoomHalf, RoomHalf))
            {
                var pose = new SimPose { Position = new float2(0f, RoomHalf - 1.2f), Yaw = 0f };
                masked.TrackingNormal = open.TrackingNormal = false;
                var hand = new SimHand(0f, -0.05f, 0.45f, true);
                hand.AddTo(masked.Body);
                masked.Mask = masked.MaskFor(hand);
                masked.StepFrame(pose);
                open.StepFrame(pose);
                Assert.That(masked.Core.MapTrusted, Is.False);
                Assert.That(masked.Core.LastDepth.Masked, Is.GreaterThan(0));
                Assert.That(masked.Core.LastDepth.Forgotten, Is.EqualTo(0));
                for (int s = 0; s < BumpConstants.SectorCount; s++)
                    Assert.That(masked.Core.SectorRange(s), Is.EqualTo(open.Core.SectorRange(s)), "sector " + s);
                Assert.That(masked.Core.Now, Is.EqualTo(open.Core.Now));
                Assert.That(masked.Core.DynamicCellCount, Is.EqualTo(0));
            }
        }

        // ---- The frame path ----

#if ATLAS_HEADLESS
        [Test]
        public void BumpGuard_Mask_AllocatesNothing()
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var left = new SimHand(-0.11f, -0.05f, 0.45f, true);
                var right = new SimHand(0.11f, -0.05f, 0.45f, true);
                left.AddTo(sim.Body);
                right.AddTo(sim.Body);
                sim.Boxes.Add(SimBox.OnFloor(0f, -0.5f, 0.45f, 0.45f, 0.90f));
                DepthMask mask = sim.MaskFor(left, right);
                sim.Mask = mask;
                // Warm up: first calls may JIT. Then one image, processed over and over at later times.
                sim.Walk(new float2(0f, -2.5f), new float2(0f, 1f), 0.5f, 1f);

                BumpGuardCore core = sim.Core;
                var head = new float3(0f, BumpScenario.HeadHeight, -2.0f);
                double t = sim.Time;
                fixed (float* depth = sim.Depth)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    for (int k = 0; k < 100; k++)
                    {
                        t += FrameSeconds;
                        DepthKernelInput input = core.MakeDepthInput(W, H, sim.Intrinsics, head, quaternion.identity, t, true, true);
                        input.Mask = BumpGuard.KernelMask(mask, mask.DepthSequence);
                        core.ProcessDepth(depth, input);
                        core.Step(t, head, quaternion.identity, true, true);
                    }
                    long after = GC.GetAllocatedBytesForCurrentThread();
                    Assert.That(after - before, Is.EqualTo(0L));
                    Assert.That(core.LastDepth.Masked, Is.GreaterThan(0));
                }
            }
        }

        [Test]
        public void BumpGuard_Mask_TheAdapterAllocatesNothing()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var guard = new BumpGuard(false))
            {
                guard.SetRoom(room);
                var hand = new SimHand(0.03f, -0.05f, 0.45f, true);
                hand.AddTo(eye.Body);
                var head = new float3(0f, BumpScenario.HeadHeight, 0f);
                eye.Render(head, quaternion.identity);
                for (int k = 0; k < 10; k++)
                {
                    SensorFrame warm = eye.SensorsAt(head, quaternion.identity, k, k, SimHand.Mask(k, eye.Intrinsics, H, hand));
                    guard.Tick(in warm);
                }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int k = 10; k < 110; k++)
                {
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, k, SimHand.Mask(k, eye.Intrinsics, H, hand));
                    guard.Tick(in frame);
                }
                long after = GC.GetAllocatedBytesForCurrentThread();
                Assert.That(after - before, Is.EqualTo(0L));
                Assert.That(guard.Core.LastDepth.Masked, Is.GreaterThan(0));
            }
        }

        [Test]
        public void BumpGuard_Mask_CostPerDepthImage()
        {
            // The price of the mask on one 256 x 192 image (3,072 samples), on this machine's .NET in an
            // unoptimised test build and not under Burst. The mask adds one pass over the samples that counts
            // what it would take, a test on every sample nearer than its gate in the main pass, and, while the
            // layer holds cells, a second pass for the young-cell rule.
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var left = new SimHand(-0.11f, -0.05f, 0.45f, true);
                var right = new SimHand(0.11f, -0.05f, 0.45f, true);
                var head = new float3(0f, BumpScenario.HeadHeight, -2.5f);
                DepthKernelMask hands = BumpGuard.KernelMask(sim.MaskFor(left, right), 0);
                // The same two regions moved off the picture: every near sample is tested, none is taken.
                DepthKernelMask beside = hands;
                beside.Region0.MinU += 500f;
                beside.Region0.MaxU += 500f;
                beside.Region0.ArmU0 += 500f;
                beside.Region0.ArmU1 += 500f;
                beside.Region1 = beside.Region0;

                // No hands in the picture: the frame of today, and the same frame when a mask comes with it.
                sim.Render(head, quaternion.identity);
                double bare = MicrosecondsPerImage(sim, head, default(DepthKernelMask), false);
                double bareWithMask = MicrosecondsPerImage(sim, head, hands, false);
                double bareCells = MicrosecondsPerImage(sim, head, default(DepthKernelMask), true);
                double bareCellsWithMask = MicrosecondsPerImage(sim, head, hands, true);
                TestContext.WriteLine("no hands in the picture, empty layer: {0:F1} us with no mask, {1:F1} us with a mask ({2:+0.0;-0.0} us)", bare, bareWithMask, bareWithMask - bare);
                TestContext.WriteLine("no hands in the picture, cells in the layer: {0:F1} us with no mask, {1:F1} us with a mask ({2:+0.0;-0.0} us)", bareCells, bareCellsWithMask, bareCellsWithMask - bareCells);

                // Two hands in the picture: masked, and under a mask that is tested on every one of their samples and takes none.
                left.AddTo(sim.Body);
                right.AddTo(sim.Body);
                sim.Render(head, quaternion.identity);
                double masked = MicrosecondsPerImage(sim, head, hands, false);
                double maskedCells = MicrosecondsPerImage(sim, head, hands, true);
                double unmaskedCells = MicrosecondsPerImage(sim, head, default(DepthKernelMask), true);
                double testedCells = MicrosecondsPerImage(sim, head, beside, true);
                TestContext.WriteLine("two hands in the picture, masked: {0:F1} us with an empty layer, {1:F1} us with cells in it", masked, maskedCells);
                TestContext.WriteLine("two hands in the picture, cells in the layer: {0:F1} us with no mask, {1:F1} us with a mask that is tested on every near sample and takes none ({2:+0.0;-0.0} us)",
                    unmaskedCells, testedCells, testedCells - unmaskedCells);
                Assert.That(bare, Is.GreaterThan(0.0));
            }
        }

        static double MicrosecondsPerImage(BumpScenario sim, float3 head, DepthKernelMask mask, bool cells)
        {
            const int images = 100;
            BumpGuardCore core = sim.Core;
            double best = double.MaxValue;
            fixed (float* depth = sim.Depth)
            {
                for (int round = 0; round < 4; round++)
                {
                    core.Reset();
                    // Cells well away from the hands, as high as the ceiling: every ray checks the layer and none clears them.
                    if (cells)
                        for (int n = 0; n < 12; n++) core.StampDynamic(new float2(-1.5f + 0.05f * n, 1.0f), 2.4f, 0.0);
                    var watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int k = 0; k < images; k++)
                    {
                        DepthKernelInput input = core.MakeDepthInput(W, H, sim.Intrinsics, head, quaternion.identity, 1.0 + k * (double)FrameSeconds, true, true);
                        input.Mask = mask;
                        core.ProcessDepth(depth, input);
                    }
                    watch.Stop();
                    best = Math.Min(best, watch.Elapsed.TotalMilliseconds * 1000.0 / images);
                }
            }
            return best;
        }
#endif

        // ---- Rule 1 ----

        [Test]
        public void TheSafetySources_NameNoHandsAssembly()
        {
            // Bump Guard reads a mask and never asks who made it (Master Plan section 0.5, rule 1; hands design
            // section 9.1, Safety_Independence): not in its references, and not in a word of its sources.
            string project = Environment.GetEnvironmentVariable(RoomFixtures.ProjectDirVariable) ?? System.IO.Directory.GetCurrentDirectory();
            string dir = System.IO.Path.Combine(project, "Assets", "Atlas", "Safety");
            Assume.That(System.IO.Directory.Exists(dir), "the project folder is reachable");
            foreach (string file in System.IO.Directory.GetFiles(dir, "*.cs", System.IO.SearchOption.AllDirectories))
                Assert.That(System.IO.File.ReadAllText(file).Contains("Atlas.Hands"), Is.False, file);
            string asmdef = System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "Atlas.Safety.asmdef"));
            Assert.That(asmdef.Contains("Atlas.Hands"), Is.False);
            Assert.That(asmdef.Contains("Atlas.Link"), Is.False);
            Assert.That(asmdef.Contains("Atlas.Telemetry"), Is.False);
        }

#if !ATLAS_HEADLESS
        [Test]
        [Category("Unity")]
        public void BumpGuard_Mask_TheBurstJobGivesWhatTheInlineKernelGives()
        {
            // On the phone a 256 x 192 image goes through the Burst job; headless tests run the kernel inline.
            // The same sensor frames, masks included, must make the two mask the same samples, forget the same
            // cells and raise the same level. (Counts and levels are compared, not the bits of every float: the
            // Editor's two compilers need not round alike. The walker stands a little off the grid, so that no
            // ray runs along the edge of a cell.)
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.Sample3x3);
            using (var eye = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var job = new BumpGuard(true))
            using (var inline = new BumpGuard(false))
            {
                job.SetRoom(room);
                inline.SetRoom(room);
                var hand = new SimHand(0.03f, -0.05f, 0.45f, true);
                eye.Boxes.Add(SimBox.OnFloor(0.31f, 0.93f, 0.3f, 0.3f, 1.7f));
                int masked = 0, forgotten = 0;
                var head = new float3(0.013f, BumpScenario.HeadHeight, -0.317f);
                for (int k = 0; k < 90; k++)
                {
                    // The walker stands. The hand comes up after half a second and is masked six images later.
                    if (k == 30) hand.AddTo(eye.Body);
                    eye.Render(head, quaternion.identity);
                    DepthMask mask = k >= 36 ? SimHand.Mask(k, eye.Intrinsics, H, hand) : default(DepthMask);
                    SensorFrame frame = eye.SensorsAt(head, quaternion.identity, k, k, mask);
                    job.Tick(in frame);
                    inline.Tick(in frame);

                    DepthFrameResult a = job.Core.LastDepth, b = inline.Core.LastDepth;
                    string where = "frame " + k;
                    Assert.That(a.Masked, Is.EqualTo(b.Masked), where);
                    Assert.That(a.Forgotten, Is.EqualTo(b.Forgotten), where);
                    Assert.That(a.MaskRefused, Is.EqualTo(b.MaskRefused), where);
                    Assert.That(job.Core.DynamicCellCount, Is.EqualTo(inline.Core.DynamicCellCount), where);
                    Assert.That(job.Core.Level, Is.EqualTo(inline.Core.Level), where);
                    masked += a.Masked;
                    forgotten += a.Forgotten;
                }
                Assert.That(masked, Is.GreaterThan(0));
                Assert.That(forgotten, Is.GreaterThan(0), "the hand's own cells were forgotten when its mask came");
            }
        }
#endif
    }
}
