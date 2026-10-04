using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;   // not Unity.Mathematics.Random

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The lift from 21 picture points and LiDAR depth to 21 joints in camera space
    /// (documents/ATLAS_Hands_Design.md sections 4.3 and 9.1, decisions R3 and R11 of the hands brief).
    /// </summary>
    public class HandsLiftTests
    {
        static readonly float[] Distances = { 0.35f, 0.45f, 0.60f };
        static readonly HandsLiftShape[] Shapes =
            { HandsLiftShape.Open, HandsLiftShape.Pointing, HandsLiftShape.Pinch, HandsLiftShape.Fist };

        /// <summary>
        /// How far the finger of the pointing-away shape points away from the picture's plane before it is turned
        /// up to 40 degrees each way like the other shapes, degrees: 5 to 85 degrees away once turned.
        /// </summary>
        const float PointingAwayDegrees = 45f;

        static float3 PalmAt(Random random, float distance)
        {
            // Somewhere in the middle of the picture, a little below its centre, as a raised hand is.
            return new float3(HandsLiftFixture.Uniform(random, -0.11f, 0.11f) * distance,
                              HandsLiftFixture.Uniform(random, -0.10f, 0.03f) * distance, distance);
        }

        /// <summary>A shape turned up to 40 degrees each way from square to the camera, either face to it.</summary>
        static JointSet RandomPose(Random random, HandsLiftShape shape, HandSide side, float scale, float distance, bool palmToCamera)
        {
            float yaw = HandsLiftFixture.Uniform(random, -40f, 40f);
            float pitch = HandsLiftFixture.Uniform(random, -40f, 40f);
            float roll = HandsLiftFixture.Uniform(random, -40f, 40f);
            if (shape == HandsLiftShape.Pointing) pitch += PointingAwayDegrees;
            float3x3 orientation = HandsLiftFixture.Turned(
                palmToCamera ? HandsLiftFixture.PalmToCamera : HandsLiftFixture.BackToCamera, yaw, pitch, roll);
            JointSet palmSpace = HandsLiftFixture.Shape(shape, side, scale);
            return HandsLiftFixture.Place(in palmSpace, orientation, PalmAt(random, distance));
        }

        /// <summary>Shows the lift an open hand square to the camera until it has measured it.</summary>
        static void MeasureTheHand(HandsLift lift, Random random, float scale, ref int sequence, ref double time, int frames = 40)
        {
            HandsDetection detection = default(HandsDetection);
            for (int i = 0; i < frames; i++)
            {
                float3x3 orientation = HandsLiftFixture.Turned(
                    i % 2 == 0 ? HandsLiftFixture.BackToCamera : HandsLiftFixture.PalmToCamera,
                    HandsLiftFixture.Uniform(random, -8f, 8f), HandsLiftFixture.Uniform(random, -8f, 8f),
                    HandsLiftFixture.Uniform(random, -30f, 30f));
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, scale);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, new float3(0.01f, -0.02f, 0.45f));
                time += 1.0 / 30.0;
                HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, HandSide.Right, random);
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.True, "calibration frame " + i);
            }
        }

        [Test]
        public void Hands_Lift_SyntheticPoses()
        {
            const int Trials = 200;
            var random = new Random(20261004);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float[] palm = new float[Trials], tip = new float[Trials];
            float[] allPalm = new float[Trials * Shapes.Length * Distances.Length], allTip = new float[allPalm.Length];
            int all = 0, sequence = 0;
            foreach (HandsLiftShape shape in Shapes)
            {
                foreach (float distance in Distances)
                {
                    for (int trial = 0; trial < Trials; trial++)
                    {
                        HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                        JointSet truth = RandomPose(random, shape, side, 1f, distance, trial % 4 >= 2);
                        HandsLiftFixture.Detect(ref detection, ++sequence, sequence / 30.0, in truth, side, random);

                        lift.Reset();   // one frame on its own: no memory of the hand before it
                        Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True, shape + " at " + distance);
                        Assert.That(hand.PalmFromDepth, Is.True, shape + " at " + distance);
                        Assert.That(hand.Joints.IsFinite(), Is.True);
                        palm[trial] = math.distance(hand.PalmCentre, HandsLiftFixture.PalmCentre(in truth));
                        tip[trial] = math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]);
                        allPalm[all] = palm[trial];
                        allTip[all++] = tip[trial];
                    }
                    float palm95 = HandsLiftFixture.Percentile(palm, Trials, 0.95f);
                    float tip95 = HandsLiftFixture.Percentile(tip, Trials, 0.95f);
                    TestContext.WriteLine("{0,-8} {1:0.00} m: palm P95 {2:0.0} mm, index fingertip P95 {3:0.0} mm",
                                          shape, distance, palm95 * 1000f, tip95 * 1000f);
                    Assert.That(palm95, Is.LessThanOrEqualTo(0.015f), "palm, " + shape + " at " + distance + " m");
                    Assert.That(tip95, Is.LessThanOrEqualTo(0.030f), "index fingertip, " + shape + " at " + distance + " m");
                }
            }
            TestContext.WriteLine("all poses: palm P95 {0:0.0} mm, index fingertip P95 {1:0.0} mm",
                                  HandsLiftFixture.Percentile(allPalm, all, 0.95f) * 1000f,
                                  HandsLiftFixture.Percentile(allTip, all, 0.95f) * 1000f);
        }

        [Test]
        public void Hands_Lift_SyntheticPoses_OnceTheHandIsMeasured()
        {
            // The same marks in the steady state of a session: a walker whose hand is 10 % larger than the
            // template, measured first, every pose a second after the one before so that nothing is carried over.
            const int Trials = 120;
            const float Scale = 1.10f;
            var random = new Random(77);
            var lift = new HandsLift();
            int sequence = 0;
            double time = 0.0;
            MeasureTheHand(lift, random, Scale, ref sequence, ref time);
            Assert.That(lift.ScaleMeasured, Is.True);

            HandsDetection detection = default(HandsDetection);
            float[] palm = new float[Trials * Shapes.Length], tip = new float[palm.Length];
            int count = 0;
            foreach (HandsLiftShape shape in Shapes)
            {
                for (int trial = 0; trial < Trials; trial++)
                {
                    HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                    JointSet truth = RandomPose(random, shape, side, Scale, Distances[trial % 3], trial % 4 >= 2);
                    time += 2.0;
                    HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, side, random);
                    Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                    palm[count] = math.distance(hand.PalmCentre, HandsLiftFixture.PalmCentre(in truth));
                    tip[count++] = math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]);
                }
            }
            float palm95 = HandsLiftFixture.Percentile(palm, count, 0.95f), tip95 = HandsLiftFixture.Percentile(tip, count, 0.95f);
            TestContext.WriteLine("measured hand: palm P95 {0:0.0} mm, index fingertip P95 {1:0.0} mm, scale {2:0.000}",
                                  palm95 * 1000f, tip95 * 1000f, lift.Scale);
            Assert.That(palm95, Is.LessThanOrEqualTo(0.015f));
            Assert.That(tip95, Is.LessThanOrEqualTo(0.030f));
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        [TestCase(true, false)]
        public void Hands_Lift_PointingAwayGoesAway(bool palmToCamera, bool jointDepth)
        {
            // An index finger pointing 30 to 70 degrees away from the picture's plane, either face of the hand to
            // the camera. With LiDAR under the joints the depth decides; without it the choice rests on anatomy
            // and on the straighter answer. Placed away: the first finger joint and the fingertip are both
            // farther from the camera than the knuckle.
            var random = new Random(palmToCamera ? 5 : 6);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            int away = 0;
            for (int trial = 0; trial < 100; trial++)
            {
                HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                float3x3 orientation = HandsLiftFixture.Turned(
                    palmToCamera ? HandsLiftFixture.PalmToCamera : HandsLiftFixture.BackToCamera,
                    HandsLiftFixture.Uniform(random, -30f, 30f), HandsLiftFixture.Uniform(random, 30f, 70f),
                    HandsLiftFixture.Uniform(random, -30f, 30f));
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Pointing, side);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, PalmAt(random, Distances[trial % 3]));
                HandsLiftFixture.Detect(ref detection, trial + 1, trial, in truth, side, random, jointDepth: jointDepth);
                Assert.That(truth[HandJoint.IndexTip].z - truth[HandJoint.IndexMcp].z, Is.GreaterThan(0.025f), "the fixture's finger points away");

                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                float knuckle = hand.Joints[HandJoint.IndexMcp].z;
                if (hand.Joints[HandJoint.IndexPip].z > knuckle && hand.Joints[HandJoint.IndexTip].z > knuckle) away++;
            }
            Assert.That(away, Is.EqualTo(100), "fingers placed away from the camera, of 100");
        }

        [Test]
        public void Hands_Lift_NoDepth()
        {
            var random = new Random(11);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float[] distances = { 0.25f, 0.35f, 0.45f, 0.60f };
            float worst = 0f;
            for (int trial = 0; trial < 200; trial++)
            {
                HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                float3x3 orientation = HandsLiftFixture.Turned(
                    trial % 4 >= 2 ? HandsLiftFixture.PalmToCamera : HandsLiftFixture.BackToCamera,
                    0f, 0f, HandsLiftFixture.Uniform(random, -40f, 40f));
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Open, side);
                float distance = distances[trial % distances.Length];
                JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, new float3(0.02f, -0.02f, distance));
                HandsLiftFixture.Detect(ref detection, trial + 1, trial, in truth, side, random, palmDepth: false, jointDepth: false);

                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                Assert.That(hand.PalmFromDepth, Is.False);
                Assert.That(hand.Confidence, Is.LessThanOrEqualTo(HandsConstants.ConfidenceWithoutDepth));
                Assert.That(hand.MeasuredScale, Is.EqualTo(0f), "a size measurement needs LiDAR");
                float error = math.abs(hand.PalmDistance - distance) / distance;
                worst = math.max(worst, error);
                Assert.That(error, Is.LessThan(0.10f), "distance from the hand's size at " + distance + " m");
            }
            Assert.That(lift.ScaleMeasured, Is.False);
            TestContext.WriteLine("worst distance error without depth: {0:0.0} %", worst * 100f);
        }

        /// <summary>A pointing hand as it presses a panel: back of the hand to the camera, the finger up and away.</summary>
        static float3x3 PressOrientation(Random random)
        {
            return HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, HandsLiftFixture.Uniform(random, -20f, 20f),
                                           HandsLiftFixture.Uniform(random, 30f, 60f), HandsLiftFixture.Uniform(random, -20f, 20f));
        }

        /// <summary>
        /// A hand LiDAR has only just seen (a few detections, far fewer than the walker's scale needs) reaches
        /// in to press: the size branch runs on spans LiDAR showed on this hand, so a press can still fire.
        /// </summary>
        [Test]
        public void Hands_Lift_PressReach_BeforeTheHandIsMeasured()
        {
            for (int pass = 0; pass < 6; pass++)
            {
                var random = new Random(70 + pass);
                var lift = new HandsLift();
                HandSide side = pass % 2 == 0 ? HandSide.Right : HandSide.Left;
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Pointing, side);
                float3x3 orientation = PressOrientation(random);
                float3 lateral = new float3(HandsLiftFixture.Uniform(random, -0.03f, 0.03f), HandsLiftFixture.Uniform(random, -0.03f, 0.01f), 0f);
                HandsDetection detection = default(HandsDetection);
                int sequence = 0;
                double time = 5.0;
                float lowest = 1f, worstTip = 0f;
                // Six detections square-on at 0.45 m, then the palm moves to 0.28 m in 17 mm steps and rests there.
                for (int frame = 0; frame < 28; frame++)
                {
                    float palmDistance = frame < 6 ? 0.45f : math.max(0.45f - (frame - 5) * 0.017f, 0.28f);
                    JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, lateral + new float3(0f, 0f, palmDistance));
                    time += 1.0 / 30.0;
                    HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, side, random, palmDepth: palmDistance >= HandsConstants.DepthMinMetres);
                    Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                    if (frame >= 6)
                    {
                        lowest = math.min(lowest, hand.Confidence);
                        worstTip = math.max(worstTip, math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]));
                    }
                }
                Assert.That(lift.ScaleMeasured, Is.False, "the hand is still unmeasured");
                TestContext.WriteLine("pass {0}: lowest confidence {1:0.00}, worst fingertip {2:0.0} mm", pass, lowest, worstTip * 1000f);
                Assert.That(lowest, Is.GreaterThanOrEqualTo(HandsConstants.InteractConfidence), "a press must be able to fire");
                Assert.That(worstTip, Is.LessThanOrEqualTo(0.030f), "index fingertip while pressing");
            }
        }

        [Test]
        public void Hands_Lift_PressReach()
        {
            const float Scale = 1.08f;
            var random = new Random(404);
            var lift = new HandsLift();
            int sequence = 0;
            double time = 0.0;
            MeasureTheHand(lift, random, Scale, ref sequence, ref time);
            Assert.That(lift.ScaleMeasured, Is.True);

            // 1. Each pose on its own, the palm nearer than LiDAR reaches and nothing remembered.
            const int Trials = 300;
            HandsDetection detection = default(HandsDetection);
            float[] tip = new float[Trials];
            float lowest = 1f;
            for (int trial = 0; trial < Trials; trial++)
            {
                HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Pointing, side, Scale);
                float palmDistance = HandsLiftFixture.Uniform(random, 0.20f, 0.32f);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, PressOrientation(random),
                    new float3(HandsLiftFixture.Uniform(random, -0.03f, 0.03f), HandsLiftFixture.Uniform(random, -0.03f, 0.01f), palmDistance));
                time += 5.0;
                HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, side, random, palmDepth: false);
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                Assert.That(hand.PalmFromDepth, Is.False);
                tip[trial] = math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]);
                lowest = math.min(lowest, hand.Confidence);
            }
            float tip95 = HandsLiftFixture.Percentile(tip, Trials, 0.95f);
            TestContext.WriteLine("palm at 0.20 to 0.32 m, no palm depth: index fingertip P95 {0:0.0} mm, worst {1:0.0} mm, lowest confidence {2:0.00}",
                                  tip95 * 1000f, tip[Trials - 1] * 1000f, lowest);
            Assert.That(tip[Trials - 1], Is.LessThanOrEqualTo(0.030f), "index fingertip with the palm nearer than LiDAR's range, every trial");
            Assert.That(lowest, Is.GreaterThanOrEqualTo(HandsConstants.InteractConfidence), "a press must be able to fire");

            // 2. The palm crossing 0.32 m, coming in and going out: nothing jumps where LiDAR ends. The step is
            // measured with a perfect detector, where every millimetre of it is the lift's own, and again with
            // noise as the change of the mean error over the five detections either side of the crossing.
            float worstClean = 0f, worstNoisy = 0f, worstNoisyStep = 0f;
            for (int pass = 0; pass < 24; pass++)
            {
                bool outwards = pass % 2 == 0;
                bool noisy = pass >= 12;
                HandSide side = pass % 4 < 2 ? HandSide.Right : HandSide.Left;
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Pointing, side, Scale);
                float3x3 orientation = PressOrientation(random);
                float3 lateral = new float3(HandsLiftFixture.Uniform(random, -0.03f, 0.03f), HandsLiftFixture.Uniform(random, -0.03f, 0.01f), 0f);
                const int Frames = 26;
                var errors = new float3[Frames];
                var fromDepth = new bool[Frames];
                time += 5.0;
                for (int frame = 0; frame < Frames; frame++)
                {
                    // 0.24 m per second at 30 detections per second: 8 mm a detection, between 0.22 and 0.42 m.
                    float travelled = frame * 0.008f;
                    float palmDistance = outwards ? 0.22f + travelled : 0.42f - travelled;
                    JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, lateral + new float3(0f, 0f, palmDistance));
                    time += 1.0 / 30.0;
                    float pixelNoise = noisy ? HandsLiftFixture.PixelNoise : 0f, depthNoise = noisy ? HandsLiftFixture.DepthNoise : 0f;
                    HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, side, random, pixelNoise, depthNoise);
                    Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                    errors[frame] = hand.Joints[HandJoint.IndexTip] - truth[HandJoint.IndexTip];
                    fromDepth[frame] = hand.PalmFromDepth;
                    Assert.That(hand.Confidence, Is.GreaterThanOrEqualTo(HandsConstants.InteractConfidence), "frame " + frame);
                }

                int crossings = 0;
                for (int frame = 1; frame < Frames; frame++)
                {
                    if (fromDepth[frame] == fromDepth[frame - 1]) continue;
                    crossings++;
                    if (!noisy) worstClean = math.max(worstClean, math.distance(errors[frame], errors[frame - 1]));
                    else if (frame >= 5 && frame + 5 <= Frames)
                    {
                        worstNoisyStep = math.max(worstNoisyStep, math.distance(errors[frame], errors[frame - 1]));
                        float3 before = float3.zero, after = float3.zero;
                        for (int i = 1; i <= 5; i++) before += errors[frame - i];
                        for (int i = 0; i < 5; i++) after += errors[frame + i];
                        worstNoisy = math.max(worstNoisy, math.distance(before, after) / 5f);
                    }
                }
                Assert.That(crossings, Is.EqualTo(1), "the palm crosses LiDAR's range once, pass " + pass);
                Assert.That(fromDepth[0], Is.EqualTo(!outwards));
            }
            TestContext.WriteLine("crossing 0.32 m: step {0:0.0} mm with a perfect detector; with noise {1:0.0} mm in the mean of five detections, {2:0.0} mm from one detection to the next",
                                  worstClean * 1000f, worstNoisy * 1000f, worstNoisyStep * 1000f);
            Assert.That(worstClean, Is.LessThanOrEqualTo(0.015f), "jump of the index fingertip as the palm crosses 0.32 m");
            Assert.That(worstNoisy, Is.LessThanOrEqualTo(0.015f), "jump of the mean index fingertip as the palm crosses 0.32 m, with noise");
        }

        [Test]
        public void Hands_Scale_Converges()
        {
            const float Truth = 1.15f;
            var random = new Random(31);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            Assert.That(lift.Scale, Is.EqualTo(1f));
            Assert.That(lift.ScaleMeasured, Is.False);

            // An open hand 15 % larger than the template, held about square to the camera. A good frame is one
            // the lift takes a size measurement from: LiDAR on the palm and the palm square to the camera as
            // LiDAR, with its centimetre of noise, shows it.
            int good = 0, frames = 0;
            while (good < HandsConstants.ScaleSamples)
            {
                Assert.That(lift.Scale, Is.EqualTo(1f), "the scale is 1 until 30 good measurements exist");
                Assert.That(lift.ScaleMeasured, Is.False);
                Assert.That(++frames, Is.LessThanOrEqualTo(40), "nearly every one of these frames is a good one");
                float3x3 orientation = HandsLiftFixture.Turned(
                    frames % 2 == 0 ? HandsLiftFixture.BackToCamera : HandsLiftFixture.PalmToCamera,
                    HandsLiftFixture.Uniform(random, -10f, 10f), HandsLiftFixture.Uniform(random, -10f, 10f),
                    HandsLiftFixture.Uniform(random, -40f, 40f));
                HandSide side = frames % 3 == 0 ? HandSide.Left : HandSide.Right;   // one size for both hands
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Open, side, Truth);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation,
                    new float3(0f, -0.02f, HandsLiftFixture.Uniform(random, 0.40f, 0.55f)));
                HandsLiftFixture.Detect(ref detection, frames, frames / 30.0, in truth, side, random);
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                if (hand.MeasuredScale > 0f)
                {
                    good++;
                    Assert.That(hand.MeasuredScale, Is.EqualTo(Truth).Within(0.15f));
                }
            }
            TestContext.WriteLine("scale after {0} good frames of {1}: {2:0.000} (a hand of {3:0.00})", good, frames, lift.Scale, Truth);
            Assert.That(lift.ScaleMeasured, Is.True);
            Assert.That(lift.Scale, Is.EqualTo(Truth).Within(0.03f * Truth));

            // A frame that is not square to the camera, or has no LiDAR, measures nothing.
            JointSet tilted = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, Truth),
                HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 50f, 0f, 0f), new float3(0f, 0f, 0.45f));
            HandsLiftFixture.Detect(ref detection, 100, 4.0, in tilted, HandSide.Right, random);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand turned), Is.True);
            Assert.That(turned.MeasuredScale, Is.EqualTo(0f));
            JointSet square = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, Truth),
                HandsLiftFixture.BackToCamera, new float3(0f, 0f, 0.45f));
            HandsLiftFixture.Detect(ref detection, 101, 9.0, in square, HandSide.Right, random, palmDepth: false, jointDepth: false);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand unseen), Is.True);
            Assert.That(unseen.MeasuredScale, Is.EqualTo(0f));

            // Never outside 0.80 to 1.25, whatever is shown.
            foreach (float size in new[] { 1.6f, 0.55f })
            {
                lift.Reset();
                Assert.That(lift.Scale, Is.EqualTo(1f));
                Assert.That(lift.ScaleMeasured, Is.False);
                for (int frame = 0; frame < 60; frame++)
                {
                    JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, size);
                    JointSet truth = HandsLiftFixture.Place(in palmSpace, HandsLiftFixture.BackToCamera, new float3(0f, -0.02f, 0.5f));
                    HandsLiftFixture.Detect(ref detection, frame + 1, frame / 30.0, in truth, HandSide.Right, random);
                    lift.TryLift(in detection, 0, out ObservedHand _);
                    Assert.That(lift.Scale, Is.InRange(HandsConstants.ScaleMin, HandsConstants.ScaleMax));
                }
                Assert.That(lift.ScaleMeasured, Is.True);
                Assert.That(lift.Scale, Is.EqualTo(size > 1f ? HandsConstants.ScaleMax : HandsConstants.ScaleMin));
            }
        }

        [Test]
        public void Hands_Lift_AllocatesNothing()
        {
            var random = new Random(3);
            var lift = new HandsLift();
            // Every path of the lift: two hands with LiDAR, a good size measurement, a palm with no depth.
            var detections = new HandsDetection[4];
            for (int i = 0; i < detections.Length; i++)
            {
                HandsLiftFixture.Begin(ref detections[i], i + 1, i / 30.0);
                HandsLiftShape shape = i == 1 ? HandsLiftShape.Open : i == 2 ? HandsLiftShape.Pointing : HandsLiftShape.Pinch;
                float3x3 right = i == 1 ? HandsLiftFixture.BackToCamera
                    : HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 10f * i, 30f, -15f);
                float3x3 left = HandsLiftFixture.Turned(HandsLiftFixture.PalmToCamera, -20f, 10f, 20f);
                JointSet a = HandsLiftFixture.Place(HandsLiftFixture.Shape(shape, HandSide.Right), right, new float3(0.10f, -0.03f, 0.45f));
                JointSet b = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Fist, HandSide.Left), left, new float3(-0.12f, -0.02f, 0.40f));
                HandsLiftFixture.AddHand(ref detections[i], in a, HandSide.Right, random, palmDepth: i != 3);
                HandsLiftFixture.AddHand(ref detections[i], in b, HandSide.Left, random);
            }

            int lifted = 0;
            for (int i = 0; i < 200; i++)   // warm up, and take the lift through its measurement of the hand
            {
                detections[i % 4].Sequence = i + 1;
                detections[i % 4].CaptureTime = i / 30.0;
                if (lift.TryLift(in detections[i % 4], 0, out ObservedHand _)) lifted++;
                if (lift.TryLift(in detections[i % 4], 1, out ObservedHand _)) lifted++;
            }
            Assert.That(lifted, Is.EqualTo(400));

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 200; i < 300; i++)
            {
                detections[i % 4].Sequence = i + 1;
                detections[i % 4].CaptureTime = i / 30.0;
                if (lift.TryLift(in detections[i % 4], 0, out ObservedHand _)) lifted++;
                if (lift.TryLift(in detections[i % 4], 1, out ObservedHand _)) lifted++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(lifted, Is.EqualTo(600));
            Assert.That(allocated, Is.EqualTo(0L), "bytes allocated by 200 lifts");
        }

        [Test]
        public void Hands_Lift_RejectsEarly()
        {
            var random = new Random(1);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            JointSet truth = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right),
                                                    HandsLiftFixture.BackToCamera, new float3(0f, 0f, 0.45f));

            HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.True);
            Assert.That(lift.TryLift(in detection, 1, out ObservedHand none), Is.False, "the detection holds one hand");
            Assert.That(none.Confidence, Is.EqualTo(0f));
            Assert.That(lift.TryLift(in detection, -1, out ObservedHand _), Is.False);
            Assert.That(lift.TryLift(in detection, 2, out ObservedHand _), Is.False);

            // Fewer than 12 joints above the confidence floor.
            HandsLiftFixture.Detect(ref detection, 2, 0.1, in truth, HandSide.Right, random);
            for (int joint = 0; joint < 10; joint++) Dim(ref detection.Hand0, HandJoints.Count - 1 - joint);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.False, "11 usable joints");
            HandsLiftFixture.Detect(ref detection, 3, 0.2, in truth, HandSide.Right, random);
            for (int joint = 0; joint < 9; joint++) Dim(ref detection.Hand0, HandJoints.Count - 1 - joint);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.True, "12 usable joints");

            // The wrist and all four knuckles below the floor: nothing fixes the palm.
            HandsLiftFixture.Detect(ref detection, 4, 0.3, in truth, HandSide.Right, random);
            Dim(ref detection.Hand0, (int)HandJoint.Wrist);
            Dim(ref detection.Hand0, (int)HandJoint.IndexMcp);
            Dim(ref detection.Hand0, (int)HandJoint.MiddleMcp);
            Dim(ref detection.Hand0, (int)HandJoint.RingMcp);
            Dim(ref detection.Hand0, (int)HandJoint.LittleMcp);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.False);

            // Too far to be the walker's (section 4.4, item 7), with LiDAR and without.
            JointSet far = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right),
                                                  HandsLiftFixture.BackToCamera, new float3(0f, 0f, 1.1f));
            HandsLiftFixture.Detect(ref detection, 5, 0.4, in far, HandSide.Right, random);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.False);
            HandsLiftFixture.Detect(ref detection, 6, 0.5, in far, HandSide.Right, random, palmDepth: false, jointDepth: false);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.False);

            // A detection with no intrinsics cannot be lifted.
            HandsLiftFixture.Detect(ref detection, 7, 0.6, in truth, HandSide.Right, random);
            detection.Fx = 0f;
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.False);
        }

        static void Dim(ref DetectedHand hand, int joint)
        {
            JointSample sample = hand.GetJoint(joint);
            sample.Confidence = 0.1f;
            sample.Pixel = new float2(-500f, 9000f);   // a guess that must not be used
            hand.SetJoint(joint, in sample);
        }

        [Test]
        public void Hands_Lift_FillsEveryField()
        {
            var random = new Random(8);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            for (int trial = 0; trial < 8; trial++)
            {
                HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                bool palmToCamera = trial % 4 >= 2;
                float3x3 orientation = HandsLiftFixture.Turned(
                    palmToCamera ? HandsLiftFixture.PalmToCamera : HandsLiftFixture.BackToCamera, 12f, -9f, 20f);
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Open, side);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, orientation, new float3(0.05f, -0.03f, 0.45f));
                HandsLiftFixture.Detect(ref detection, trial + 1, trial, in truth, side, random, 0f, 0f);

                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                string label = side + (palmToCamera ? ", palm to the camera" : ", back to the camera");

                for (int joint = 0; joint < HandJoints.Count; joint++)
                    Assert.That(math.distance(hand.Joints[joint], truth[joint]), Is.LessThan(0.012f), label + ", joint " + joint);
                Assert.That(hand.SideHint, Is.EqualTo((int)side));
                Assert.That(hand.PalmFromDepth, Is.True);
                Assert.That(hand.Confidence, Is.EqualTo(0.95f).Within(1e-3f), "the detector's own confidence, with nothing to take from it");
                Assert.That(math.distance(hand.PalmCentre, HandsLiftFixture.PalmCentre(in truth)), Is.LessThan(0.002f), label);
                Assert.That(hand.PalmDistance, Is.EqualTo(hand.PalmCentre.z).Within(1e-6f));

                // Out of the palm: the palm frame's Y axis, which the fixture's orientation carries in its second column.
                Assert.That(math.length(hand.PalmNormal), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(hand.PalmNormal, orientation.c1), Is.GreaterThan(0.999f), label);

                float2 pixel = detection.Project(hand.PalmCentre);
                Assert.That(math.distance(hand.PalmPixel, pixel), Is.LessThan(1e-2f));
                float margin = math.min(math.min(pixel.x, HandsLiftFixture.ImageWidth - pixel.x),
                                        math.min(pixel.y, HandsLiftFixture.ImageHeight - pixel.y)) / HandsLiftFixture.ImageHeight;
                Assert.That(hand.EdgeMargin, Is.EqualTo(margin).Within(1e-4f));

                // The pinch measure: the gap in the picture, in metres at the palm's distance, over the palm's width.
                float2 gap = detection.Project(truth[HandJoint.ThumbTip]) - detection.Project(truth[HandJoint.IndexTip]);
                float expected = math.length(gap) / HandsLiftFixture.Focal * hand.PalmDistance
                               / math.distance(truth[HandJoint.IndexMcp], truth[HandJoint.LittleMcp]);
                Assert.That(hand.PinchRatio, Is.EqualTo(expected).Within(0.02f), label);
                Assert.That(hand.PinchRatio, Is.GreaterThan(HandsConstants.PinchOpen), "an open hand is not pinching");

                Assert.That(hand.MeasuredScale, Is.EqualTo(1f).Within(0.02f), "square to the camera with LiDAR: a size measurement");
            }
        }

        [Test]
        public void Hands_Lift_PinchRatio_FallsAsTheTipsMeet()
        {
            var lift = new HandsLift();
            var random = new Random(2);
            HandsDetection detection = default(HandsDetection);
            float previous = float.MaxValue;
            for (int step = 0; step <= 4; step++)
            {
                // The template's thumb and index finger do not quite meet; bring them together by hand.
                JointSet palmSpace = HandsLiftFixture.Shape(HandsLiftShape.Pinch, HandSide.Right);
                float3 gap = palmSpace[HandJoint.ThumbTip] - palmSpace[HandJoint.IndexTip];
                palmSpace[HandJoint.ThumbTip] = palmSpace[HandJoint.IndexTip] + gap * (1f - step / 4f);
                JointSet truth = HandsLiftFixture.Place(in palmSpace, HandsLiftFixture.PalmToCamera, new float3(0f, 0f, 0.45f));
                HandsLiftFixture.Detect(ref detection, step + 1, step, in truth, HandSide.Right, random, 0f, 0f);
                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                Assert.That(hand.PinchRatio, Is.LessThan(previous));
                previous = hand.PinchRatio;
            }
            Assert.That(previous, Is.LessThan(0.02f), "tips together");
        }

        [Test]
        public void Hands_Lift_PlacesJointsTheDetectorIsUnsureOf()
        {
            var random = new Random(9);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 15f, 20f, -10f);
            JointSet truth = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right),
                                                    orientation, new float3(0f, -0.02f, 0.45f));
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f);
            int[] unsure = { (int)HandJoint.RingMcp, (int)HandJoint.MiddleDip, (int)HandJoint.MiddleTip, (int)HandJoint.LittleTip };
            foreach (int joint in unsure) Dim(ref detection.Hand0, joint);

            Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
            Assert.That(hand.Joints.IsFinite(), Is.True);
            // An open hand: a knuckle goes where the others say the palm is, a finger joint straight on from its bone.
            foreach (int joint in unsure)
                Assert.That(math.distance(hand.Joints[joint], truth[joint]), Is.LessThan(0.015f), "joint " + joint);
            Assert.That(math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]), Is.LessThan(0.01f));
            Assert.That(hand.Confidence, Is.LessThan(0.95f), "unsure joints lower the mean confidence");
        }

        [Test]
        public void Hands_Lift_TwoHands_EachKeepsItsOwnMemory()
        {
            var random = new Random(15);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float3x3 rightTurn = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 20f, 50f, 0f);
            float3x3 leftTurn = HandsLiftFixture.Turned(HandsLiftFixture.PalmToCamera, -15f, 10f, 10f);
            float[] rightTip = new float[60], leftTip = new float[60];
            for (int frame = 0; frame < 60; frame++)
            {
                JointSet right = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Pointing, HandSide.Right), rightTurn,
                                                        new float3(0.12f, -0.03f, 0.45f + 0.001f * frame));
                JointSet left = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Fist, HandSide.Left), leftTurn,
                                                       new float3(-0.12f, -0.02f, 0.42f));
                HandsLiftFixture.Begin(ref detection, frame + 1, frame / 30.0);
                // The detector does not keep its hands in one order.
                bool swapped = frame % 3 == 1;
                if (swapped)
                {
                    HandsLiftFixture.AddHand(ref detection, in left, HandSide.Left, random);
                    HandsLiftFixture.AddHand(ref detection, in right, HandSide.Right, random);
                }
                else
                {
                    HandsLiftFixture.AddHand(ref detection, in right, HandSide.Right, random);
                    HandsLiftFixture.AddHand(ref detection, in left, HandSide.Left, random);
                }
                Assert.That(lift.TryLift(in detection, swapped ? 1 : 0, out ObservedHand liftedRight), Is.True);
                Assert.That(lift.TryLift(in detection, swapped ? 0 : 1, out ObservedHand liftedLeft), Is.True);
                Assert.That(liftedRight.SideHint, Is.EqualTo(1));
                Assert.That(liftedLeft.SideHint, Is.EqualTo(0));
                rightTip[frame] = math.distance(liftedRight.Joints[HandJoint.IndexTip], right[HandJoint.IndexTip]);
                leftTip[frame] = math.distance(liftedLeft.Joints[HandJoint.IndexTip], left[HandJoint.IndexTip]);
            }
            Assert.That(HandsLiftFixture.Percentile(rightTip, 60, 0.95f), Is.LessThan(0.03f));
            Assert.That(HandsLiftFixture.Percentile(leftTip, 60, 0.95f), Is.LessThan(0.03f));
        }

        [Test]
        public void Hands_Lift_PalmNormal_FollowsTheSideAndTheLayout()
        {
            var random = new Random(21);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 10f, 25f, 15f);
            JointSet truth = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right),
                                                    orientation, new float3(0f, -0.02f, 0.45f));

            // No label and nothing remembered: the back of the hand is taken to face the camera, as it does in a reach.
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random);
            detection.Hand0.Side = -1;
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand unknown), Is.True);
            Assert.That(unknown.SideHint, Is.EqualTo(-1));
            Assert.That(unknown.PalmNormal.z, Is.GreaterThan(0f), "out of the palm is away from the camera");
            Assert.That(math.dot(unknown.PalmNormal, orientation.c1), Is.GreaterThan(0.95f));

            // The same picture called a left hand is a left hand showing its palm.
            lift.Reset();
            detection.Hand0.Side = 0;
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand left), Is.True);
            Assert.That(math.dot(left.PalmNormal, orientation.c1), Is.LessThan(-0.95f));

            // One wrong label in a run of right ones does not turn the hand over.
            lift.Reset();
            for (int frame = 0; frame < 12; frame++)
            {
                HandsLiftFixture.Detect(ref detection, frame + 1, frame / 30.0, in truth, HandSide.Right, random);
                if (frame == 8) detection.Hand0.Side = 0;
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                Assert.That(hand.SideHint, Is.EqualTo(frame == 8 ? 0 : 1), "the hint is the detector's own word");
                Assert.That(math.dot(hand.PalmNormal, orientation.c1), Is.GreaterThan(0.9f), "frame " + frame);
            }
        }

        [Test]
        public void Hands_Lift_AWallBehindTheFingertipIsNotTheFinger()
        {
            // A finger tipped towards the camera, and under its tip not the finger's depth but the room's: the
            // wall 2 m on, or a table top 2.5 cm behind the tip (a sample whose 3 x 3 pixels straddle more than
            // 3 cm never reaches the lift's choice). LiDAR chooses between answers; it places nothing.
            foreach (float behind in new[] { 2f, 0.025f })
            {
                var random = new Random(41);
                var lift = new HandsLift();
                HandsDetection detection = default(HandsDetection);
                const int Trials = 200;
                float[] tip = new float[Trials];
                for (int trial = 0; trial < Trials; trial++)
                {
                    HandSide side = trial % 2 == 0 ? HandSide.Right : HandSide.Left;
                    float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.PalmToCamera, HandsLiftFixture.Uniform(random, -20f, 20f),
                                                                   HandsLiftFixture.Uniform(random, -45f, -25f), HandsLiftFixture.Uniform(random, -20f, 20f));
                    JointSet truth = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, side), orientation, PalmAt(random, 0.5f));
                    HandsLiftFixture.Detect(ref detection, trial + 1, trial, in truth, side, random);
                    Assert.That(truth[HandJoint.IndexTip].z, Is.LessThan(truth[HandJoint.IndexMcp].z - 0.03f), "the fixture's finger comes towards the camera");
                    JointSample sample = detection.Hand0.GetJoint((int)HandJoint.IndexTip);
                    sample.Depth = truth[HandJoint.IndexTip].z + behind;
                    detection.Hand0.SetJoint((int)HandJoint.IndexTip, in sample);

                    lift.Reset();
                    Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                    tip[trial] = math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]);
                }
                float tip95 = HandsLiftFixture.Percentile(tip, Trials, 0.95f);
                TestContext.WriteLine("the room {0} m behind the fingertip: index fingertip P95 {1:0.0} mm", behind, tip95 * 1000f);
                Assert.That(tip95, Is.LessThanOrEqualTo(0.030f), "with the room " + behind + " m behind the fingertip");
            }
        }

        [Test]
        public void Hands_Lift_Confidence_IsTheWeakestOfItsTerms()
        {
            var random = new Random(52);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            JointSet open = HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right);

            // The picture's border: full inside the band of 8 % of the picture's height, nothing at the border itself.
            foreach (float inside in new[] { 0.12f, 0.08f, 0.04f, 0.02f, -0.03f })
            {
                float x = (inside * HandsLiftFixture.ImageHeight - HandsLiftFixture.Cx) / HandsLiftFixture.Focal * 0.45f;
                JointSet truth = HandsLiftFixture.Place(in open, HandsLiftFixture.BackToCamera, new float3(x, 0f, 0.45f));
                HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f);
                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                Assert.That(hand.EdgeMargin, Is.EqualTo(inside).Within(2e-3f));
                float expected = math.min(0.95f, math.saturate(inside / HandsConstants.EdgeFadeBand));
                Assert.That(hand.Confidence, Is.EqualTo(expected).Within(0.03f), "palm " + inside + " of the height inside the border");
            }

            // Bones that had to be stretched: a hand larger than the lift believes, held flat. The more the
            // bones are too long for their lengths, the less the lift trusts what it made of them.
            float previous = 0.95f;
            foreach (float size in new[] { 1.25f, 1.4f })
            {
                JointSet large = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, size),
                                                        HandsLiftFixture.BackToCamera, new float3(0f, -0.02f, 0.5f));
                HandsLiftFixture.Detect(ref detection, 2, 1.0, in large, HandSide.Right, random, 0f, 0f);
                lift.Reset();
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand stretched), Is.True);
                Assert.That(stretched.Confidence, Is.LessThan(previous - 0.1f), "a hand " + size + " times the template's size");
                Assert.That(stretched.Confidence, Is.LessThan(HandsConstants.InteractConfidence), "not to be pressed with");
                Assert.That(stretched.Joints.IsFinite(), Is.True);
                previous = stretched.Confidence;
            }

            // The detector's own doubt.
            JointSet truthAgain = HandsLiftFixture.Place(in open, HandsLiftFixture.BackToCamera, new float3(0f, 0f, 0.45f));
            HandsLiftFixture.Detect(ref detection, 3, 2.0, in truthAgain, HandSide.Right, random, 0f, 0f);
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                JointSample sample = detection.Hand0.GetJoint(joint);
                sample.Confidence = 0.4f;
                detection.Hand0.SetJoint(joint, in sample);
            }
            lift.Reset();
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand unsure), Is.True);
            Assert.That(unsure.Confidence, Is.EqualTo(0.4f).Within(1e-3f));
        }

        [Test]
        public void Hands_Lift_Reset_ForgetsTheWalkerAndTheHands()
        {
            var random = new Random(71);
            var lift = new HandsLift();
            int sequence = 0;
            double time = 0.0;
            MeasureTheHand(lift, random, 1.15f, ref sequence, ref time, 60);
            Assert.That(lift.ScaleMeasured, Is.True);
            Assert.That(lift.Scale, Is.EqualTo(1.15f).Within(0.03f));
            Assert.That(lift.Bones.Learnt((int)HandJoint.IndexPip), Is.True);
            Assert.That(lift.Bones.Length((int)HandJoint.IndexPip), Is.EqualTo(1.15f * HandModel.BoneLength((int)HandJoint.IndexPip)).Within(0.002f));

            lift.Reset();
            Assert.That(lift.ScaleMeasured, Is.False);
            Assert.That(lift.Scale, Is.EqualTo(1f));
            for (int joint = 1; joint < HandJoints.Count; joint++)
                Assert.That(lift.Bones.Length(joint), Is.EqualTo(HandModel.BoneLength(joint)));

            // And the hands: a palm with no depth is placed by the template's size again, with nothing carried on.
            HandsDetection detection = default(HandsDetection);
            JointSet truth = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right, 1.15f),
                                                    HandsLiftFixture.BackToCamera, new float3(0.01f, -0.02f, 0.45f));
            HandsLiftFixture.Detect(ref detection, ++sequence, time + 1.0 / 30.0, in truth, HandSide.Right, random, 0f, 0f, palmDepth: false, jointDepth: false);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
            Assert.That(hand.PalmDistance, Is.EqualTo(0.45f / 1.15f).Within(0.01f), "a hand 15 % larger than the template looks 15 % nearer");
            Assert.That(hand.Confidence, Is.LessThanOrEqualTo(HandsConstants.ConfidenceWithoutDepth));
        }

        [Test]
        public void Hands_Lift_RemembersAHandThroughATurnOfTheHead()
        {
            // The hand stays where it is in the room; the head, and the camera with it, turns 30 degrees between
            // two detections, which moves the hand 23 cm in the camera's own space: more than a hand is followed
            // over. It is remembered by where it is in the session.
            var random = new Random(81);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.PalmToCamera, 10f, 15f, -10f);
            JointSet first = HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right), orientation, new float3(0f, -0.02f, 0.45f));

            float4x4 pose = float4x4.TRS(new float3(1f, 1.5f, -2f), quaternion.RotateY(math.radians(40f)), new float3(1f));
            HandsLiftFixture.Begin(ref detection, 1, 0.0, in pose);
            HandsLiftFixture.AddHand(ref detection, in first, HandSide.Right, random, 0f, 0f);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand before), Is.True);
            Assert.That(math.dot(before.PalmNormal, orientation.c1), Is.GreaterThan(0.99f));
            Assert.That(before.PalmNormal.z, Is.LessThan(0f), "the palm is to the camera");

            float4x4 turned = float4x4.TRS(new float3(1f, 1.5f, -2f), quaternion.RotateY(math.radians(70f)), new float3(1f));
            float4x4 toSecondCamera = math.mul(math.inverse(turned), pose);
            JointSet second = first.Transformed(in toSecondCamera);
            Assert.That(math.distance(HandsLiftFixture.PalmCentre(in second), HandsLiftFixture.PalmCentre(in first)), Is.GreaterThan(HandsConstants.AssociateMetres));
            HandsLiftFixture.Begin(ref detection, 2, 1.0 / 30.0, in turned);
            HandsLiftFixture.AddHand(ref detection, in second, HandSide.Right, random, 0f, 0f);
            detection.Hand0.Side = -1;   // the detector does not say which hand: what was remembered must
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand after), Is.True);
            float3 truthNormal = math.mul(new float3x3(toSecondCamera.c0.xyz, toSecondCamera.c1.xyz, toSecondCamera.c2.xyz), orientation.c1);
            Assert.That(math.dot(after.PalmNormal, truthNormal), Is.GreaterThan(0.99f), "still the palm, though a hand with no label and no past shows its back");

            // With no pose in the detection the camera's own space stands in, and the same hand is a new one.
            lift.Reset();
            HandsLiftFixture.Begin(ref detection, 1, 0.0, default(float4x4));
            HandsLiftFixture.AddHand(ref detection, in first, HandSide.Right, random, 0f, 0f);
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.True);
            HandsLiftFixture.Begin(ref detection, 2, 1.0 / 30.0, default(float4x4));
            HandsLiftFixture.AddHand(ref detection, in second, HandSide.Right, random, 0f, 0f);
            detection.Hand0.Side = -1;
            Assert.That(lift.TryLift(in detection, 0, out ObservedHand stranger), Is.True);
            Assert.That(math.dot(stranger.PalmNormal, truthNormal), Is.LessThan(-0.99f), "nothing remembered: the back of the hand is taken to face the camera");
        }

        [Test]
        public void Hands_Lift_LearnsAWalkersBones()
        {
            // Decision R11: the detector's points are not anatomical joint centres. A hand whose first finger
            // bones are a quarter shorter than the template's and whose middle bones are a tenth longer, shown
            // open and square to the camera, then pointing away.
            var random = new Random(63);
            var lift = new HandsLift();
            HandsDetection detection = default(HandsDetection);
            float3 shares = new float3(0.75f, 1.10f, 0.95f);
            int sequence = 0;
            double time = 0.0;
            for (int frame = 0; frame < 45; frame++)
            {
                JointSet open = Reshaped(HandsLiftShape.Open, HandSide.Right, shares);
                float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera,
                    HandsLiftFixture.Uniform(random, -8f, 8f), HandsLiftFixture.Uniform(random, -8f, 8f), HandsLiftFixture.Uniform(random, -20f, 20f));
                JointSet truth = HandsLiftFixture.Place(in open, orientation, new float3(0f, -0.02f, 0.45f));
                time += 1.0 / 30.0;
                HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, HandSide.Right, random);
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand _), Is.True);
            }
            for (int bone = 0; bone < 3; bone++)
            {
                int joint = (int)HandJoint.IndexPip + bone;
                Assert.That(lift.Bones.Length(joint), Is.EqualTo(HandModel.BoneLength(joint) * shares[bone]).Within(0.0015f), "index bone " + bone);
            }

            const int Trials = 200;
            float[] tip = new float[Trials];
            for (int trial = 0; trial < Trials; trial++)
            {
                JointSet pointing = Reshaped(HandsLiftShape.Pointing, HandSide.Right, shares);
                float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera,
                    HandsLiftFixture.Uniform(random, -30f, 30f), HandsLiftFixture.Uniform(random, 10f, 70f), HandsLiftFixture.Uniform(random, -30f, 30f));
                JointSet truth = HandsLiftFixture.Place(in pointing, orientation, PalmAt(random, Distances[trial % 3]));
                time += 2.0;
                HandsLiftFixture.Detect(ref detection, ++sequence, time, in truth, HandSide.Right, random);
                Assert.That(lift.TryLift(in detection, 0, out ObservedHand hand), Is.True);
                tip[trial] = math.distance(hand.Joints[HandJoint.IndexTip], truth[HandJoint.IndexTip]);
            }
            float tip95 = HandsLiftFixture.Percentile(tip, Trials, 0.95f);
            TestContext.WriteLine("a hand with its own bone lengths, once learnt: index fingertip P95 {0:0.0} mm", tip95 * 1000f);
            Assert.That(tip95, Is.LessThanOrEqualTo(0.030f));
        }

        /// <summary>The template hand in a shape, with every finger's three bones multiplied by the given shares.</summary>
        static JointSet Reshaped(HandsLiftShape shape, HandSide side, float3 shares)
        {
            JointSet hand = HandsLiftFixture.Shape(shape, side);
            JointSet reshaped = hand;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int baseJoint = HandJoints.Base(finger);
                for (int bone = 0; bone < 3; bone++)
                {
                    int joint = baseJoint + 1 + bone;
                    reshaped[joint] = reshaped[joint - 1] + (hand[joint] - hand[joint - 1]) * shares[bone];
                }
            }
            return reshaped;
        }
    }
}
