using System.Collections.Generic;
using System.Globalization;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Safety.Tests
{
    /// <summary>
    /// BumpGuard_Replay_WithHands (hands design section 9.1): the mask does its job and no more. The recorded
    /// walks are replayed with two synthetic raised hands in front of the camera and their mask: Bump Guard
    /// raises what it raises with no hands in the picture, and nothing else. A box nobody scanned, beyond the
    /// gate behind the hands, is warned of in the frame it is warned of with no hands. The last test measures
    /// what no mask can give back: a hand held on the walking line hides what is behind it from LiDAR.
    /// </summary>
    [TestFixture]
    public class BumpGuardReplayWithHandsTests
    {
        const float RoomHalf = 3.5f;
        const float FrameSeconds = 1f / BumpScenario.FrameRate;

        // Two raised hands, palms to the camera, 22 cm apart and 0.45 m ahead, with their forearms.
        static readonly SimHand Left = new SimHand(-0.11f, -0.05f, 0.45f, true);
        static readonly SimHand Right = new SimHand(0.11f, -0.05f, 0.45f, true);
        // The same pair held 3 cm to the right, without forearms: the left hand reaches the walking line.
        static readonly SimHand LeftOnTheLine = new SimHand(-0.08f, -0.05f, 0.45f);
        static readonly SimHand RightOfTheLine = new SimHand(0.14f, -0.05f, 0.45f);

        static string F(double value) { return value.ToString("F2", CultureInfo.InvariantCulture); }

        static void Step(BumpScenario sim, in HeadPose sample)
        {
            sim.TrackingNormal = sample.Tracking == TrackingState.Normal;
            sim.StepFrame(sample.Position, sample.Rotation);
        }

        // ---- The recorded walks ----

        [TestCase(BumpRecordings.Lap)]
        [TestCase(BumpRecordings.WallApproach)]
        public void BumpGuard_Replay_WithHands_TheRecordedWalkRaisesWhatItRaisesWithNoHands(string recording)
        {
            PoseLog log = BumpRecordings.Load(recording);
            using (var bare = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var masked = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var unmasked = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            {
                foreach (BumpScenario sim in new[] { masked, unmasked })
                {
                    Left.AddTo(sim.Body);
                    Right.AddTo(sim.Body);
                }
                masked.Mask = masked.MaskFor(Left, Right);

                int maskedFrames = 0, refusedFrames = 0, handFrames = 0;
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    Step(bare, log.Samples[k]);
                    Step(masked, log.Samples[k]);
                    Step(unmasked, log.Samples[k]);

                    string where = recording + ", frame " + k;
                    Assert.That(masked.Core.Level, Is.EqualTo(bare.Core.Level), where);
                    Assert.That(masked.Core.RawLevel, Is.EqualTo(bare.Core.RawLevel), where);
                    Assert.That(masked.Core.Now, Is.EqualTo(bare.Core.Now), where);
                    Assert.That(masked.Core.Path04, Is.EqualTo(bare.Core.Path04), where);
                    Assert.That(masked.Core.Path08, Is.EqualTo(bare.Core.Path08), where);
                    Assert.That(masked.Core.DynamicCellCount, Is.EqualTo(0), where + ": a hand was stamped");
                    if (masked.Core.LastDepth.Masked > 0) maskedFrames++;
                    if (masked.Core.LastDepth.MaskRefused) refusedFrames++;
                    if (unmasked.Core.Level != bare.Core.Level) handFrames++;
                }

                Assert.That(masked.Changes.Count, Is.EqualTo(bare.Changes.Count));
                TestContext.WriteLine("{0}: {1} frames; mask used in {2}, refused in {3} (a wall inside its gate); level changes {4} with no hands and with masked hands, {5} with unmasked hands; unmasked hands changed the level in {6} frames",
                    recording, log.Samples.Length, maskedFrames, refusedFrames, bare.Changes.Count, unmasked.Changes.Count, handFrames);
                Assert.That(maskedFrames, Is.GreaterThan(log.Samples.Length / 2), "the mask was at work for most of the walk");
                // What the mask is for: with nothing to say they are hands, the same hands raise warnings of their own.
                Assert.That(handFrames, Is.GreaterThan(30), "unmasked hands raised nothing: the walk proves nothing");
            }
        }

        [TestCase(1)]
        [TestCase(6)]
        [TestCase(15)]
        public void BumpGuard_Replay_WithHands_NoWarningBeginsOnceTheMaskExists(int lagImages)
        {
            // The hands come up on the first straight side of the lap, two seconds in, and their mask arrives 1 to
            // 15 images later, as it does when the detector needs its time. What the hands raise in those images is
            // over within half a second of the mask; from then on the lap is the lap with no hands.
            const int raise = 120;
            PoseLog log = BumpRecordings.Load(BumpRecordings.Lap);
            int maskFrame = raise + lagImages;
            using (var bare = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            using (var hands = new BumpScenario(BumpRecordings.RoomHalf, BumpRecordings.RoomHalf))
            {
                int handCueFrames = 0, cuesBegunAfterTheMask = 0, cellsAfterThreeImages = -1;
                for (int k = 0; k < log.Samples.Length; k++)
                {
                    if (k == raise)
                    {
                        LeftOnTheLine.AddTo(hands.Body);
                        RightOfTheLine.AddTo(hands.Body);
                    }
                    if (k == maskFrame) hands.Mask = hands.MaskFor(LeftOnTheLine, RightOfTheLine);
                    BumpLevel before = hands.Core.Level;
                    Step(bare, log.Samples[k]);
                    Step(hands, log.Samples[k]);

                    if (k < raise) Assert.That(hands.Core.Level, Is.EqualTo(bare.Core.Level), "frame " + k);
                    if (hands.Core.Level != bare.Core.Level) handCueFrames++;
                    if (k >= maskFrame && before == BumpLevel.Clear && hands.Core.Level != BumpLevel.Clear && bare.Core.Level == BumpLevel.Clear)
                        cuesBegunAfterTheMask++;
                    if (k == maskFrame + 2) cellsAfterThreeImages = hands.Core.DynamicCellCount;
                    if (k >= maskFrame + 30)
                    {
                        Assert.That(hands.Core.Level, Is.EqualTo(bare.Core.Level), "frame " + k);
                        Assert.That(hands.Core.DynamicCellCount, Is.EqualTo(0), "frame " + k);
                    }
                }
                TestContext.WriteLine("mask {0} images late: the hands' own cue lasted {1} s; cells three images after the mask: {2}",
                    lagImages, F(handCueFrames * FrameSeconds), cellsAfterThreeImages);
                // One unmasked image may pass unnoticed; six do not.
                if (lagImages >= 6) Assert.That(handCueFrames, Is.GreaterThan(0), "walking at 0.5 m/s, an unmasked hand 0.45 m ahead on the line raises WARN");
                Assert.That(cuesBegunAfterTheMask, Is.EqualTo(0));
                Assert.That(cellsAfterThreeImages, Is.EqualTo(0));
                Assert.That(handCueFrames * FrameSeconds, Is.LessThan(lagImages * FrameSeconds + BumpConstants.RelaxSeconds + 0.1f));
            }
        }

        // ---- A box behind the hands ----

        public enum Thing { Chair, Stool, Figure, Post }

        static SimBox BoxOf(Thing thing)
        {
            switch (thing)
            {
                case Thing.Chair: return SimBox.OnFloor(0f, 2.0f, 0.45f, 0.45f, 0.90f);
                case Thing.Stool: return SimBox.OnFloor(0f, 2.0f, 0.35f, 0.35f, 0.45f);
                case Thing.Figure: return SimBox.OnFloor(0f, 2.0f, 0.45f, 0.30f, 1.75f);
                default: return SimBox.OnFloor(0f, 2.0f, 0.10f, 0.10f, 1.60f);
            }
        }

        struct Approach
        {
            public bool Cued, Strong;
            /// <summary>Seconds from the first cue, and from the first STRONG, to the moment the head reaches the box.</summary>
            public double CueLead, StrongLead;
            public int CueFrame;
            public bool FellBackToClear;
            /// <summary>The mask had been refused by the frame of the first cue: the box filled the hands' regions inside their gate.</summary>
            public bool RefusedByTheCue;
        }

        const float ApproachFrom = 3.0f;

        // Straight at the box from 3 m, carrying the hands, masked or not, the whole way.
        static Approach WalkAt(Thing thing, float speed, IReadOnlyList<SimHand> hands, bool masked)
        {
            using (var sim = new BumpScenario(RoomHalf, RoomHalf))
            {
                SimBox box = BoxOf(thing);
                sim.Boxes.Add(box);
                if (hands != null)
                {
                    foreach (SimHand hand in hands) hand.AddTo(sim.Body);
                    if (masked) sim.Mask = hands.Count == 1 ? sim.MaskFor(hands[0]) : sim.MaskFor(hands[0], hands[1]);
                }
                var a = new Approach { CueFrame = -1 };
                int cueFrame = -1, strongFrame = -1, k = 0;
                for (; ; k++)
                {
                    float z = box.Min.z - ApproachFrom + speed * k * FrameSeconds;
                    if (z >= box.Min.z) break;
                    sim.StepFrame(new SimPose { Position = new float2(0f, z), Yaw = 0f });
                    if (cueFrame < 0 && sim.Core.LastDepth.MaskRefused) a.RefusedByTheCue = true;
                    if (cueFrame < 0 && sim.Core.Level != BumpLevel.Clear) cueFrame = k;
                    if (strongFrame < 0 && sim.Core.Level == BumpLevel.Strong) strongFrame = k;
                    if (cueFrame >= 0 && sim.Core.Level == BumpLevel.Clear) a.FellBackToClear = true;
                }
                a.Cued = cueFrame >= 0;
                a.Strong = strongFrame >= 0;
                a.CueFrame = cueFrame;
                a.CueLead = a.Cued ? (k - cueFrame) * (double)FrameSeconds : 0.0;
                a.StrongLead = a.Strong ? (k - strongFrame) * (double)FrameSeconds : 0.0;
                return a;
            }
        }

        // The walk with no hands is the yardstick of every case below: made once for each box and speed.
        static readonly Dictionary<int, Approach> s_noHands = new Dictionary<int, Approach>();

        static Approach WalkWithNoHands(Thing thing, float speed)
        {
            int key = (int)thing * 1000 + (int)math.round(speed * 100f);
            if (!s_noHands.TryGetValue(key, out Approach a))
            {
                a = WalkAt(thing, speed, null, false);
                s_noHands.Add(key, a);
            }
            return a;
        }

        static string Lead(in Approach a)
        {
            return (a.Cued ? "cue " + F(a.CueLead) + " s" : "no cue") + (a.Strong ? ", STRONG " + F(a.StrongLead) + " s" : ", no STRONG") + " before contact";
        }

        static readonly SimHand[] BothHands = { Left, Right };
        // One hand whose inner edge is 5 cm to the side of the walking line.
        static readonly SimHand[] OneHandBeside = { new SimHand(0.095f, -0.08f, 0.45f, true) };
        // One hand held on the walking line itself, in front of the face.
        static readonly SimHand[] OneHandOnTheLine = { new SimHand(0f, -0.08f, 0.45f, true) };

        [TestCase(Thing.Chair, 0.5f)]
        [TestCase(Thing.Chair, 1.0f)]
        [TestCase(Thing.Stool, 0.5f)]
        [TestCase(Thing.Stool, 1.0f)]
        [TestCase(Thing.Figure, 0.5f)]
        [TestCase(Thing.Figure, 1.0f)]
        [TestCase(Thing.Post, 0.5f)]
        [TestCase(Thing.Post, 1.0f)]
        public void BumpGuard_Replay_WithHands_ABoxBehindTheHandsIsWarnedOfAsWithNoHands(Thing thing, float speed)
        {
            Approach bare = WalkWithNoHands(thing, speed);
            Approach both = WalkAt(thing, speed, BothHands, true);
            Approach beside = WalkAt(thing, speed, OneHandBeside, true);
            TestContext.WriteLine("{0} at {1} m/s: no hands {2}; two masked hands {3}; one masked hand beside the line {4}",
                thing, F(speed), Lead(bare), Lead(both), Lead(beside));

            Assert.That(bare.Cued, Is.True);
            Assert.That(bare.CueLead, Is.GreaterThanOrEqualTo(0.6), "the approach of BumpGuard_Replay");
            foreach (Approach hands in new[] { both, beside })
            {
                Assert.That(hands.Cued, Is.True);
                Assert.That(hands.Strong, Is.True);
                Assert.That(hands.FellBackToClear, Is.False, "the warning was dropped before contact");
                if (hands.RefusedByTheCue)
                {
                    // Something wide enough to fill the hands' regions came inside their gate: the mask was refused
                    // for those images, the hands counted again, and the warning came no later than with no hands.
                    Assert.That(thing, Is.EqualTo(Thing.Figure), "only the figure is wide and tall enough");
                    Assert.That(hands.CueFrame, Is.LessThanOrEqualTo(bare.CueFrame + 1));
                    Assert.That(hands.StrongLead, Is.GreaterThanOrEqualTo(bare.StrongLead - 1.5 * FrameSeconds));
                }
                else
                {
                    Assert.That(hands.CueFrame, Is.EqualTo(bare.CueFrame).Within(1), "the warning starts within one frame of where it starts with no hands");
                    Assert.That(hands.StrongLead, Is.EqualTo(bare.StrongLead).Within(1.5 * FrameSeconds));
                }
            }
        }

        [Test]
        public void BumpGuard_Replay_WithHands_Characterisation_AHandOnTheWalkingLineHidesWhatIsBehindIt()
        {
            // Measured, not wished for. A hand held in front of the face on the walking line for a whole approach
            // stands between LiDAR and whatever is straight ahead. Unmasked, the hand itself is warned of from the
            // first step, which is the false warning the mask removes; masked, Bump Guard knows only what it can
            // see round the hand. A figure is seen above and beside it. A chair is seen late. A stool or a post
            // that the hand covers all the way is not seen at all. This is the hand hiding the obstacle, with or
            // without a mask (design section 7.1), and it is why walking with hands needs its own approach gate.
            Approach unmasked = WalkAt(Thing.Post, 1.0f, OneHandOnTheLine, false);
            TestContext.WriteLine("Post at 1.00 m/s, hand on the line, unmasked: {0}", Lead(unmasked));
            Assert.That(unmasked.Cued, Is.True);
            Assert.That(unmasked.CueFrame, Is.LessThanOrEqualTo(2), "the unmasked hand is warned of from the first step");

            foreach (Thing thing in new[] { Thing.Figure, Thing.Chair, Thing.Stool, Thing.Post })
                foreach (float speed in new[] { 0.5f, 1.0f })
                {
                    // Both speeds for the chair and the post; the figure and the stool give the same at either.
                    if (speed < 1.0f && (thing == Thing.Figure || thing == Thing.Stool)) continue;
                    Approach bare = WalkWithNoHands(thing, speed);
                    Approach masked = WalkAt(thing, speed, OneHandOnTheLine, true);
                    TestContext.WriteLine("{0} at {1} m/s: no hand {2}; hand on the line, masked {3}", thing, F(speed), Lead(bare), Lead(masked));
                    // The mask never brings a warning forward.
                    if (masked.Cued) Assert.That(masked.CueLead, Is.LessThanOrEqualTo(bare.CueLead + 1.5 * FrameSeconds));
                    switch (thing)
                    {
                        case Thing.Figure:
                            Assert.That(masked.CueFrame, Is.EqualTo(bare.CueFrame).Within(1), "a figure shows round the hand");
                            break;
                        case Thing.Chair:
                            Assert.That(masked.Cued, Is.True);
                            Assert.That(masked.CueLead, Is.LessThan(bare.CueLead - 0.2), "the chair is seen late");
                            break;
                        default:
                            Assert.That(masked.Cued, Is.False, "a " + thing + " behind the hand all the way is never seen");
                            break;
                    }
                }
        }
    }
}
