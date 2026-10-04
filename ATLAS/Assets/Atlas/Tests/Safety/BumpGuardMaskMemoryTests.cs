using System;
using System.Globalization;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// What the depth mask may make Bump Guard forget, and what it may not (hands design sections 7.1 and 9.1,
    /// rule 2 as the review decided it): BumpGuard_Mask_KeepsSeenObstacle and
    /// BumpGuard_Mask_ForgetsTheHandsOwnCells. A dynamic cell under a masked sample is forgotten only if it was
    /// first stamped less than a second ago, nothing outside the mask hit it in that image, and it stands at the
    /// sample's own height. So an obstacle that has been in view keeps its cells and its warning when a hand
    /// covers it, and the cells a hand stamped before it was detected go. The scenes are those of
    /// <see cref="BumpGuardMaskLimitsTests"/>.
    /// </summary>
    [TestFixture]
    public unsafe class BumpGuardMaskMemoryTests
    {
        const float RoomHalf = 3.5f;
        const float FrameSeconds = 1f / BumpScenario.FrameRate;

        static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }

        // ---- BumpGuard_Mask_KeepsSeenObstacle ----

        // A post nobody scanned: 10 cm square, 1.6 m high.
        static SimBox PostAt(float x, float nearFaceZ)
        {
            return SimBox.OnFloor(x, nearFaceZ + 0.05f, 0.10f, 0.10f, 1.6f);
        }

        [Test]
        public void BumpGuard_Mask_KeepsSeenObstacle_Standing()
        {
            // The walker stands 0.31 m from the post: clear(head) is 0.28 m, WARN. After two seconds a hand comes
            // to rest on the post. Its region covers the whole post and its gate reaches 20 cm behind the palm,
            // so from then on no depth sample stamps the post. The post was seen for two seconds: it stays.
            var pose = new SimPose { Position = new float2(0f, -2.48f), Yaw = 0f };
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Boxes.Add(PostAt(0f, -2.17f));
                sim.Hold(pose, 2f);
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));
                int cells = sim.Core.DynamicCellCount;
                Assert.That(cells, Is.GreaterThan(0));
                float now = sim.Core.Now;
                Assert.That(now, Is.LessThan(BumpConstants.WarnNow));

                var hand = new SimHand(0f, -0.05f, 0.305f);
                hand.AddTo(sim.Body);
                sim.Mask = sim.MaskFor(hand);
                int stampedWhileCovered = 0;
                for (int k = 0; k < 120; k++)
                {
                    sim.StepFrame(pose);
                    DepthFrameResult depth = sim.Core.LastDepth;
                    Assert.That(depth.Masked, Is.GreaterThan(100), "the hand and the post behind it are masked");
                    Assert.That(depth.MaskRefused, Is.False);
                    Assert.That(depth.Forgotten, Is.EqualTo(0), "frame " + k);
                    Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(cells), "frame " + k);
                    Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn), "frame " + k);
                    Assert.That(sim.Core.Now, Is.EqualTo(now), "frame " + k);
                    stampedWhileCovered += depth.Stamped;
                }
                // The cells did not survive by being stamped again: nothing reached them. They were kept.
                Assert.That(stampedWhileCovered, Is.EqualTo(0));
                Assert.That(sim.RisingEdges(), Is.EqualTo(1));
            }
        }

        [TestCase(0.3f)]
        [TestCase(0.5f)]
        [TestCase(1.0f)]
        public void BumpGuard_Mask_KeepsSeenObstacle_Walking(float speed)
        {
            // The walker walks at the post, which is in view the whole way. 0.9 m from it a hand comes up in
            // front of the face, on the line to the post, masked from its first image. From 0.65 m on the post is
            // inside the hand's region and inside its gate: nothing stamps it again before the warning is due.
            // The warning comes in the same frame as it does for a walker with no hand up.
            const float postZ = 2.0f;
            // From far enough for the post to have been in view for two seconds and more before the hand comes up.
            float from = 0.9f + 2.6f * math.max(speed, 0.6f);
            using (var hand = new BumpScenario(RoomHalf, RoomHalf))
            using (var bare = new BumpScenario(RoomHalf, RoomHalf))
            {
                hand.Boxes.Add(PostAt(0f, postZ));
                bare.Boxes.Add(PostAt(0f, postZ));
                var raised = new SimHand(0f, -0.08f, 0.45f, true);
                bool up = false;
                int cellsWhenCovered = -1, stampedWhileCovered = 0, coveredFrames = 0;
                for (int k = 0; ; k++)
                {
                    float z = postZ - from + speed * k / BumpScenario.FrameRate;
                    if (z >= postZ) break;
                    if (!up && postZ - z <= 0.9f)
                    {
                        up = true;
                        raised.AddTo(hand.Body);
                        hand.Mask = hand.MaskFor(raised);
                    }
                    var pose = new SimPose { Position = new float2(0f, z), Yaw = 0f };
                    hand.StepFrame(pose);
                    bare.StepFrame(pose);

                    Assert.That(hand.Core.LastDepth.Forgotten, Is.EqualTo(0), "frame " + k);
                    Assert.That(hand.Core.Level, Is.EqualTo(bare.Core.Level), "frame " + k + ", " + F(postZ - z) + " m from the post");
                    if (up && postZ - z <= 0.60f && postZ - z > 0.32f)
                    {
                        if (cellsWhenCovered < 0) cellsWhenCovered = hand.Core.DynamicCellCount;
                        Assert.That(hand.Core.DynamicCellCount, Is.EqualTo(cellsWhenCovered), "frame " + k);
                        stampedWhileCovered += hand.Core.LastDepth.Stamped;
                        coveredFrames++;
                    }
                }
                Assert.That(cellsWhenCovered, Is.GreaterThan(0));
                Assert.That(coveredFrames, Is.GreaterThan(10));
                Assert.That(stampedWhileCovered, Is.EqualTo(0), "the post was covered: it was remembered, not seen");
                Assert.That(bare.FirstChangeTo(BumpLevel.Warn, out LevelChange warn), Is.True);
                Assert.That(bare.FirstChangeTo(BumpLevel.Strong, out LevelChange strong), Is.True);
                TestContext.WriteLine("{0} m/s: WARN {1} m and STRONG {2} m from the post, with the hand up and without",
                    F(speed), F(postZ - warn.Position.y), F(postZ - strong.Position.y));
                Assert.That(postZ - strong.Position.y, Is.LessThan(0.60f), "STRONG was raised while the post was covered");
            }
        }

        [TestCase(0.9f, false)]
        [TestCase(1.1f, true)]
        public void BumpGuard_Mask_KeepsSeenObstacle_OnceItHasBeenSeenForASecond(float seenSeconds, bool kept)
        {
            // The reach of the young-cell rule, on the post of the standing test: covered by the hand's region
            // after 0.9 s in view it is forgotten, as the hand's own cells would be; after 1.1 s it is kept.
            var pose = new SimPose { Position = new float2(0f, -2.48f), Yaw = 0f };
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                sim.Hold(pose, 0.5f);
                sim.Boxes.Add(PostAt(0f, -2.17f));
                sim.Hold(pose, seenSeconds);
                int cells = sim.Core.DynamicCellCount;
                Assert.That(cells, Is.GreaterThan(0));
                Assert.That(sim.Core.Level, Is.EqualTo(BumpLevel.Warn));

                var hand = new SimHand(0f, -0.05f, 0.305f);
                hand.AddTo(sim.Body);
                sim.Mask = sim.MaskFor(hand);
                sim.Hold(pose, 1f);
                Assert.That(sim.Core.DynamicCellCount, Is.EqualTo(kept ? cells : 0));
                Assert.That(sim.Core.Level, Is.EqualTo(kept ? BumpLevel.Warn : BumpLevel.Clear));
            }
        }

        // ---- BumpGuard_Mask_ForgetsTheHandsOwnCells ----

        struct Entry
        {
            public int CellsBeforeTheMask, ImagesUntilGone, CellsAtTheEnd, CuesAfterTheMask, CellsBornAfterTheMask;
            public bool GrewAfterTheMask;
            public BumpLevel WorstFromHalfASecondOn;
            public double NotClearSeconds;
        }

        // A hand comes into view half a second into the walk and its mask arrives lagImages later.
        static Entry HandEntry(float speed, float depth, int lagImages, bool forearm)
        {
            const int raise = 30;
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                var hand = new SimHand(0.03f, -0.05f, depth, forearm);
                var e = new Entry { ImagesUntilGone = -1, WorstFromHalfASecondOn = BumpLevel.Clear };
                int maskFrame = raise + lagImages, previousCells = 0;
                double maskTime = maskFrame / (double)BumpScenario.FrameRate;
                for (int k = 0; k < maskFrame + 180; k++)
                {
                    if (k == raise) hand.AddTo(sim.Body);
                    if (k == maskFrame) sim.Mask = sim.MaskFor(hand);
                    BumpLevel before = sim.Core.Level;
                    sim.StepFrame(new float3(0f, BumpScenario.HeadHeight, -2.5f + speed * k * FrameSeconds), quaternion.identity);
                    int cells = sim.Core.DynamicCellCount;
                    if (k == maskFrame - 1) e.CellsBeforeTheMask = cells;
                    if (k >= maskFrame)
                    {
                        if (e.ImagesUntilGone < 0 && cells == 0) e.ImagesUntilGone = k - maskFrame + 1;
                        if (cells > previousCells) e.GrewAfterTheMask = true;
                        if (before == BumpLevel.Clear && sim.Core.Level != BumpLevel.Clear) e.CuesAfterTheMask++;
                        if (k >= maskFrame + 30 && sim.Core.Level > e.WorstFromHalfASecondOn) e.WorstFromHalfASecondOn = sim.Core.Level;
                    }
                    if (sim.Core.Level != BumpLevel.Clear) e.NotClearSeconds += FrameSeconds;
                    previousCells = cells;
                }
                e.CellsAtTheEnd = sim.Core.DynamicCellCount;

                // What is left was stamped before the mask existed.
                DynamicGrid g = sim.Core.Dynamic;
                for (int c = 0; c < g.Spec.Width * g.Spec.Height; c++)
                    if (g.TopHeight[c] > 0f && g.FirstHit[c] >= maskTime) e.CellsBornAfterTheMask++;
                return e;
            }
        }

        static string Show(float speed, float depth, int lag, in Entry e)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} m/s, hand {1} m ahead, mask {2,2} images late: {3} cells before the mask, gone after {4} images, {5} left, level not Clear for {6} s",
                F(speed), F(depth), lag, e.CellsBeforeTheMask, e.ImagesUntilGone < 0 ? "no number of" : e.ImagesUntilGone.ToString(), e.CellsAtTheEnd, F(e.NotClearSeconds));
        }

        static readonly float[] EntrySpeeds = { 0f, 0.5f, 1.0f };
        static readonly float[] EntryDepths = { 0.35f, 0.45f, 0.60f };
        static readonly int[] EntryLags = { 1, 3, 6, 9, 15 };

        [Test]
        public void BumpGuard_Mask_ForgetsTheHandsOwnCells()
        {
            // The frames before a hand is detected: 1 to 15 depth images (up to 250 ms) in which it is an
            // obstacle like any other. Standing or walking, what it stamped is gone within three images of the
            // mask: forgotten under the masked samples, or seen empty by the rays that pass below the hand.
            foreach (float speed in EntrySpeeds)
                foreach (float depth in EntryDepths)
                    foreach (int lag in EntryLags)
                    {
                        Entry e = HandEntry(speed, depth, lag, false);
                        string row = Show(speed, depth, lag, e);
                        TestContext.WriteLine(row);
                        Assert.That(e.CellsBeforeTheMask, Is.GreaterThan(0), row);
                        Assert.That(e.ImagesUntilGone, Is.InRange(1, 3), row);
                        Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), row);
                        Assert.That(e.GrewAfterTheMask, Is.False, row);
                        Assert.That(e.CuesAfterTheMask, Is.EqualTo(0), "a cue began after the mask existed: " + row);
                        Assert.That(e.WorstFromHalfASecondOn, Is.EqualTo(BumpLevel.Clear), row);
                    }
        }

        [Test]
        public void BumpGuard_Mask_ForgetsTheHandsOwnCells_NoMaskLate_NoCellAtAll()
        {
            foreach (float speed in EntrySpeeds)
                foreach (bool forearm in new[] { false, true })
                {
                    Entry e = HandEntry(speed, 0.45f, 0, forearm);
                    Assert.That(e.ImagesUntilGone, Is.EqualTo(1));
                    Assert.That(e.GrewAfterTheMask, Is.False);
                    Assert.That(e.CellsAtTheEnd, Is.EqualTo(0));
                    Assert.That(e.NotClearSeconds, Is.EqualTo(0.0));
                }
        }

        [Test]
        public void BumpGuard_Mask_ForgetsTheHandsOwnCells_AndTheForearmsWhileStanding()
        {
            foreach (float depth in EntryDepths)
                foreach (int lag in EntryLags)
                {
                    Entry e = HandEntry(0f, depth, lag, true);
                    string row = "with a forearm, " + Show(0f, depth, lag, e);
                    TestContext.WriteLine(row);
                    Assert.That(e.CellsBeforeTheMask, Is.GreaterThan(0), row);
                    Assert.That(e.ImagesUntilGone, Is.EqualTo(1), row);
                    Assert.That(e.CellsAtTheEnd, Is.EqualTo(0), row);
                    Assert.That(e.NotClearSeconds, Is.EqualTo(0.0), row);
                }
        }

        [Test]
        public void BumpGuard_Mask_ForgetsTheHandsOwnCells_ButNotAForearmsTrailBelowTheView()
        {
            // What the rule does not reach, measured so that it is known. A walking walker's forearm crosses the
            // lower border of the picture. Before its mask exists it stamps cells there; when the mask arrives
            // the arm has moved on with the walker, so no masked sample points at them, and they lie under the
            // lowest ray the camera has, so no ray sees them empty. They stay until their 30 s run out, and the
            // walker walks through them. Once the mask exists the arm stamps nothing more: that much is held.
            int left = 0, runs = 0;
            foreach (float speed in new[] { 0.5f, 1.0f })
                foreach (float depth in EntryDepths)
                    foreach (int lag in new[] { 6, 15 })
                    {
                        Entry e = HandEntry(speed, depth, lag, true);
                        TestContext.WriteLine("with a forearm, " + Show(speed, depth, lag, e) + ", worst level half a second on " + e.WorstFromHalfASecondOn);
                        Assert.That(e.GrewAfterTheMask, Is.False, "the arm stamped a cell after its mask existed");
                        Assert.That(e.CellsBornAfterTheMask, Is.EqualTo(0));
                        runs++;
                        if (e.CellsAtTheEnd > 0) left++;
                    }
            TestContext.WriteLine("{0} of {1} walking entries with a forearm left cells behind", left, runs);
        }

        // ---- The dynamic layer's part ----

        sealed class Layer : IDisposable
        {
            public readonly BumpGuardCore Core = new BumpGuardCore();
            public Layer()
            {
                var sdf = new float[80 * 80];
                for (int k = 0; k < sdf.Length; k++) sdf[k] = 2f;
                Core.SetRoom(sdf, 80, 80, 0.05f, -2f, -2f);
            }
            public void Dispose() => Core.Dispose();
        }

        [Test]
        public void DynamicLayer_KeepsTheTimeOfACellsFirstStamp()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);
                Assert.That(double.IsNaN(DynamicLayer.FirstHitAt(g, c)), Is.True, "an empty cell has none");
                Assert.That(double.IsNaN(DynamicLayer.FirstHitAt(g, new float2(9f, 0f))), Is.True, "nor has a point off the grid");

                DynamicLayer.Stamp(g, c, 0.5f, 1.0);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(1.0));
                // Later hits renew the last hit and may raise the cell; the first stamp stands.
                DynamicLayer.Stamp(g, c, 0.9f, 2.0);
                DynamicLayer.Stamp(g, c, 0.3f, 25.0);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(1.0));
                Assert.That(DynamicLayer.TopHeightAt(g, c), Is.EqualTo(0.9f));

                // Seen empty in three images, the cell goes; stamped again it is a new cell.
                for (int image = 1; image <= 3; image++) DynamicLayer.ClearIfSeenEmpty(g, c, 0.2f, image, 26.0 + image);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
                DynamicLayer.Stamp(g, c, 0.5f, 40.0);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(40.0));

                // A cell past its 30 s that the sweep has not reached yet no longer counts: its next hit starts it again.
                DynamicLayer.Stamp(g, c, 0.5f, 40.0 + BumpConstants.DynExpirySeconds - 0.5);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(40.0), "hit again inside its 30 s: the same cell");
                double later = 40.0 + 2.0 * BumpConstants.DynExpirySeconds;
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1), "expired, not swept");
                DynamicLayer.Stamp(g, c, 0.5f, later);
                Assert.That(DynamicLayer.FirstHitAt(g, c), Is.EqualTo(later));
            }
        }

        [Test]
        public void DynamicLayer_ForgetIfYoung_OnlyAYoungCellThatNothingHitThisImageAtTheSamplesHeight()
        {
            using (var l = new Layer())
            {
                DynamicGrid g = l.Core.Dynamic;
                float2 c = new float2(0.52f, 0.02f);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 10.0), Is.False, "nothing there");

                DynamicLayer.Stamp(g, c, 1.4f, 10.0);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 10.0), Is.False, "hit in this very image");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.0f, 10.5), Is.False, "the cell stands 0.4 m higher than the sample");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.6f, 10.5), Is.False, "the cell stands 0.2 m lower than the sample");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, float.NaN, 10.5), Is.False);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 9.0), Is.False, "an image from before the cell");
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, double.NaN), Is.False);
                Assert.That(DynamicLayer.ForgetIfYoung(g, new float2(9f, 0f), 1.4f, 10.5), Is.False, "off the grid");
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.3f, 10.5), Is.True, "half a second old, 0.1 m from the sample");
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(0));
                Assert.That(DynamicLayer.Distance(g, c, 10.5, out _), Is.EqualTo(float.PositiveInfinity));

                // The age is counted from the first stamp. A cell that every image has hit for a second and more
                // is old, however fresh its last hit.
                for (int k = 0; k <= 66; k++) DynamicLayer.Stamp(g, c, 1.4f, 20.0 + k / 60.0);
                Assert.That(DynamicLayer.ForgetIfYoung(g, c, 1.4f, 20.0 + 67 / 60.0), Is.False, "first stamped 1.12 s ago, last hit one image ago");
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));

                // To the limit: just under a second is young, a second is not.
                float2 d = new float2(-0.52f, 0.02f);
                DynamicLayer.Stamp(g, d, 1.4f, 30.0);
                Assert.That(DynamicLayer.ForgetIfYoung(g, d, 1.4f, 30.0 + BumpConstants.MaskYoungSeconds), Is.False);
                Assert.That(DynamicLayer.ForgetIfYoung(g, d, 1.4f, 30.0 + BumpConstants.MaskYoungSeconds - 0.001), Is.True);
                Assert.That(l.Core.DynamicCellCount, Is.EqualTo(1));
            }
        }
    }
}
