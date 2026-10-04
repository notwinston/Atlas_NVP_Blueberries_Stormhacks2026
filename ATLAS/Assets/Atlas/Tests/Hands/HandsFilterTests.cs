using System;
using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The filter of documents/ATLAS_Hands_Design.md section 4.4: association, smoothing, prediction, loss, and
    /// left and right. Time is passed in, so every scenario is a function of the clock the rig runs.
    /// </summary>
    public class HandsFilterTests
    {
        static readonly float3 LeftWrist = new float3(-0.12f, -0.10f, 0.45f);
        static readonly float3 RightWrist = new float3(0.12f, -0.10f, 0.45f);

        struct Snap
        {
            public double Now;
            public bool Tracked, Fresh;
            public float Lost, Age, TrackedSeconds, Confidence;
            public int Good;
            public float3 Palm, Velocity, Normal;
        }

        static Snap Take(HandsFilter filter, HandSide side, double now)
        {
            ref readonly TrackedHand h = ref filter.Hand(side);
            return new Snap
            {
                Now = now, Tracked = h.Tracked, Fresh = h.Fresh, Lost = h.LostSeconds, Age = h.AgeSeconds,
                TrackedSeconds = h.TrackedSeconds, Confidence = h.Confidence, Good = h.GoodDetections,
                Palm = h.PalmCentre, Velocity = h.PalmVelocity, Normal = h.PalmNormal
            };
        }

        static ObservedHand Right(float3 wrist, float confidence = 0.9f)
        {
            return HandsFilterFixtures.Hand(HandSide.Right, wrist, quaternion.identity, 1, confidence);
        }

        // ---- Loss: the hold, and its end (decision R2) ----

        [Test]
        public void Hands_Filter_BriefLossHolds()
        {
            // A hand drifting at 2 cm/s; for 0.2 s the results carry no hand.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture >= 1.5 && capture < 1.7) return 0;
                a = Right(RightWrist + new float3(0.02f * (float)capture, 0f, 0f));
                return 1;
            };
            rig.Run(3.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            int first = frames.FindIndex(f => f.Tracked);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "the hand is tracked once its first result has arrived");
            // The first result without the hand is captured at 1.5 s and the first with it again at 1.7 s; each is
            // received 0.08 s later, in the first frame at or after that.
            double lostFrom = 1.5 + rig.Age - 1e-9, backAt = 1.7 + rig.Age - 1e-9;
            float maxStep = 0f, maxLost = 0f;
            for (int i = first; i < frames.Count; i++)
            {
                Assert.That(frames[i].Tracked, Is.True, "Tracked at " + frames[i].Now);
                if (i > first) maxStep = math.max(maxStep, math.distance(frames[i].Palm, frames[i - 1].Palm));
                maxLost = math.max(maxLost, frames[i].Lost);
                bool inGap = frames[i].Now >= lostFrom && frames[i].Now < backAt;
                if (inGap) Assert.That(frames[i].Fresh, Is.False, "no detection of the hand at " + frames[i].Now);
                if (!inGap) Assert.That(frames[i].Lost, Is.EqualTo(0f), "LostSeconds is 0 while detections keep coming, at " + frames[i].Now);
            }

            TestContext.WriteLine("brief loss: largest step between frames " + (maxStep * 1000f) + " mm, longest LostSeconds " + maxLost + " s");
            Assert.That(maxStep, Is.LessThan(0.01f), "no jump over 1 cm, in the gap or where the hand returns");
            // The loss is reported: about the gap, and still inside the hold at full opacity (R2).
            Assert.That(maxLost, Is.InRange(0.15f, HandsConstants.LossFadeStartSeconds - 0.01f));
            float previous = 0f;
            foreach (Snap f in frames)
            {
                if (f.Now < lostFrom || f.Now >= backAt) continue;
                Assert.That(f.Lost, Is.GreaterThanOrEqualTo(previous), "LostSeconds only grows in a loss");
                previous = f.Lost;
            }
            int back = frames.FindIndex(f => f.Now >= backAt && f.Fresh);
            Assert.That(back, Is.GreaterThan(first));
            Assert.That(frames[back].Lost, Is.EqualTo(0f));
        }

        [Test]
        public void Hands_Filter_BriefLossHolds_WhenTheDetectorGoesSilent()
        {
            // No results at all for 0.2 s: nothing says the hand is gone, and nothing may jump.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            rig.Silent = capture => capture >= 1.5 && capture < 1.7;
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist + new float3(0.02f * (float)capture, 0f, 0f));
                return 1;
            };
            rig.Run(3.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            int first = frames.FindIndex(f => f.Tracked);
            float maxStep = 0f;
            for (int i = first; i < frames.Count; i++)
            {
                Assert.That(frames[i].Tracked, Is.True, "Tracked at " + frames[i].Now);
                if (i > first) maxStep = math.max(maxStep, math.distance(frames[i].Palm, frames[i - 1].Palm));
            }
            Assert.That(maxStep, Is.LessThan(0.01f));
        }

        [Test]
        public void Hands_Filter_LossEnds()
        {
            // Present until 1.0 s, absent from the results until 2.0 s, then back.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture >= 1.0 && capture < 2.0) return 0;
                a = Right(RightWrist);
                return 1;
            };
            rig.Run(3.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            // The first result without the hand is captured at 1.0 s and received 0.08 s later: the frame that
            // gets it is where the loss starts.
            int missFrame = frames.FindIndex(f => f.Now >= 1.0 + rig.Age - 1e-9);
            double lostFrom = frames[missFrame].Now;
            int drop = frames.FindIndex(missFrame, f => !f.Tracked);
            Assert.That(drop, Is.GreaterThan(missFrame), "the hand stops being tracked");
            double grace = HandsConstants.LossGraceSeconds;
            Assert.That(frames[drop].Now - lostFrom, Is.GreaterThanOrEqualTo(grace - 1e-6), "not untracked before LossGraceSeconds");
            Assert.That(frames[drop - 1].Now - lostFrom, Is.LessThan(grace), "untracked as soon as LossGraceSeconds is reached");
            Assert.That(frames[drop - 1].Tracked, Is.True);
            for (int i = missFrame; i < drop; i++)
                Assert.That(frames[i].Lost, Is.EqualTo((float)(frames[i].Now - lostFrom)).Within(1e-4f), "LostSeconds counts the loss at " + frames[i].Now);

            // Untracked it stays, with nothing left in the slot.
            int back = frames.FindIndex(drop, f => f.Now >= 2.0 + rig.Age - 1e-9);
            for (int i = drop; i < back; i++)
            {
                Assert.That(frames[i].Tracked, Is.False, "still untracked at " + frames[i].Now);
                Assert.That(frames[i].Good, Is.EqualTo(0));
            }

            // It comes back as a new hand: the pose is the detection, not a blend with the old one.
            Snap returned = frames[back];
            Assert.That(returned.Tracked, Is.True);
            Assert.That(returned.Fresh, Is.True);
            Assert.That(returned.Good, Is.EqualTo(1));
            Assert.That(returned.TrackedSeconds, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(returned.Lost, Is.EqualTo(0f));
            Assert.That(math.distance(returned.Palm, Right(RightWrist).PalmCentre), Is.LessThan(1e-5f));
        }

        [Test]
        public void Hands_Filter_LossEnds_WhenTheDetectorGoesSilent()
        {
            // The results stop altogether at 1.0 s. After the result timeout the hand counts as lost, and
            // LossGraceSeconds later it is untracked.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            rig.Silent = capture => capture >= 1.0;
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist);
                return 1;
            };
            rig.Run(3.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            // The last result was captured at 29/30 s and received 0.08 s later.
            double lastArrival = 29.0 / 30.0 + rig.Age;
            int drop = frames.FindIndex(f => f.Now > 1.0 && !f.Tracked);
            Assert.That(drop, Is.GreaterThan(0));
            double expected = lastArrival + HandsFilter.ResultTimeoutSeconds + HandsConstants.LossGraceSeconds;
            Assert.That(frames[drop].Now, Is.InRange(expected - 0.02, expected + 0.04));
        }

        // ---- Prediction ----

        static float MovingError(float speed, float noise, double age, float limit, uint seed)
        {
            var rig = new HandsFilterRig { Age = age };
            rig.Filter.PredictionLimitSeconds = limit;
            var rng = new HandsFilterRng(seed);
            float3 direction = new float3(1f, 0f, 0f);
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist + direction * (speed * (float)capture));
                HandsFilterFixtures.AddNoise(ref a, ref rng, noise);
                return 1;
            };
            double sum = 0.0;
            int n = 0;
            rig.Run(6.0, source, now =>
            {
                if (now < 2.0) return;
                float3 truth = Right(RightWrist + direction * (speed * (float)now)).PalmCentre;
                sum += math.lengthsq(rig.Filter.Hand(HandSide.Right).PalmCentre - truth);
                n++;
            });
            return (float)Math.Sqrt(sum / n);
        }

        [Test]
        public void Hands_Filter_PredictionBeatsNone()
        {
            // 0.4 m/s, 3 mm of noise, 30 Hz, 80 ms old: the palm drawn against where the hand really is now.
            float none = MovingError(0.4f, 0.003f, 0.08, 0f, 11u);
            float predicted = MovingError(0.4f, 0.003f, 0.08, HandsConstants.MaxPredictSeconds, 11u);
            TestContext.WriteLine("0.4 m/s, 3 mm noise, 80 ms: unpredicted " + (none * 1000f) + " mm, predicted " + (predicted * 1000f) + " mm (rms)");
            Assert.That(predicted, Is.LessThan(none), "predicted " + predicted + " m, unpredicted " + none + " m");
            Assert.That(predicted, Is.LessThan(0.7f * none), "prediction is worth having: predicted " + predicted + " m, unpredicted " + none + " m");

            // Slower and faster hands gain as well.
            foreach (float speed in new[] { 0.1f, 0.2f, 0.8f })
            {
                float n = MovingError(speed, 0.003f, 0.08, 0f, 5u);
                float p = MovingError(speed, 0.003f, 0.08, HandsConstants.MaxPredictSeconds, 5u);
                TestContext.WriteLine(speed + " m/s: unpredicted " + (n * 1000f) + " mm, predicted " + (p * 1000f) + " mm (rms)");
                Assert.That(p, Is.LessThan(n), "at " + speed + " m/s: predicted " + p + " m, unpredicted " + n + " m");
            }
        }

        [Test]
        public void Hands_Filter_PredictionIsLimited()
        {
            // A result 0.3 s old is shown moved on by the velocity times 50 ms, not by 0.3 s.
            float speed = 0.5f;
            float3 shown = float3.zero, still = float3.zero, velocity = float3.zero;
            for (int pass = 0; pass < 2; pass++)
            {
                var rig = new HandsFilterRig { Age = 0.3 };
                rig.Filter.PredictionLimitSeconds = pass == 0 ? HandsConstants.MaxPredictSeconds : 0f;
                HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
                {
                    a = Right(RightWrist + new float3(speed * (float)capture, 0f, 0f));
                    return 1;
                };
                rig.Run(4.0, source);
                ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                if (pass == 0) { shown = h.PalmCentre; velocity = h.PalmVelocity; }
                else still = h.PalmCentre;
            }
            Assert.That(velocity.x, Is.EqualTo(speed).Within(0.03f), "the filtered velocity of a hand at constant speed");
            float ahead = shown.x - still.x;
            Assert.That(ahead, Is.EqualTo(speed * HandsConstants.MaxPredictSeconds).Within(0.003f), "moved on by 50 ms of velocity");
        }

        // ---- A still hand ----

        [Test]
        public void Hands_Filter_StillHandIsStill()
        {
            // 2 mm of detection noise on every axis of a hand that does not move. Jitter is the standard
            // deviation of the drawn palm about its true place (the One-Euro paper's measure): per axis, and
            // at the 95th percentile of 20 runs of 10 s.
            const float sigma = 0.002f;
            const int runs = 20;
            var jitter = new float[runs];
            var tipJitter = new float[runs];
            var steps = new List<float>();
            var offsets = new List<float>();
            float3 truth = Right(RightWrist).PalmCentre;
            float3 tipTruth = Right(RightWrist).Joints[HandJoint.IndexTip];
            for (int run = 0; run < runs; run++)
            {
                var rig = new HandsFilterRig();
                var rng = new HandsFilterRng(1000u + 7919u * (uint)run);
                HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
                {
                    a = Right(RightWrist);
                    HandsFilterFixtures.AddNoise(ref a, ref rng, sigma);
                    return 1;
                };
                double palmSum = 0.0, tipSum = 0.0;
                int n = 0;
                float3 previous = float3.zero;
                rig.Run(12.0, source, now =>
                {
                    if (now < 2.0) return;
                    ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                    palmSum += math.lengthsq(h.PalmCentre - truth);
                    tipSum += math.lengthsq(h.World[HandJoint.IndexTip] - tipTruth);
                    if (run == 0) offsets.Add(math.distance(h.PalmCentre, truth));
                    if (n > 0) steps.Add(math.distance(h.PalmCentre, previous));
                    previous = h.PalmCentre;
                    n++;
                });
                jitter[run] = (float)Math.Sqrt(palmSum / n / 3.0);
                tipJitter[run] = (float)Math.Sqrt(tipSum / n / 3.0);
            }
            Array.Sort(jitter);
            Array.Sort(tipJitter);
            float p95 = jitter[(int)Math.Ceiling(0.95 * runs) - 1];
            float tipP95 = tipJitter[(int)Math.Ceiling(0.95 * runs) - 1];
            steps.Sort();
            offsets.Sort();
            float stepP95 = steps[(int)(0.95 * steps.Count)];
            float offsetP95 = offsets[(int)(0.95 * offsets.Count)];
            TestContext.WriteLine("palm jitter, per axis, 95th percentile of " + runs + " runs: " + (p95 * 1000f) + " mm; index tip " + (tipP95 * 1000f)
                + " mm; frame-to-frame palm step, 95th percentile: " + (stepP95 * 1000f) + " mm; distance of the palm from its true place, 95th percentile of one run: "
                + (offsetP95 * 1000f) + " mm");
            Assert.That(p95, Is.LessThan(0.001f), "2 mm of noise in, under 1 mm of jitter out (palm)");
            Assert.That(tipP95, Is.LessThan(0.002f), "a fingertip is no noisier than the noise put in (index tip)");
        }

        // ---- Left and right ----

        static void AssertSidesApart(HandsFilter filter, double now)
        {
            ref readonly TrackedHand left = ref filter.Hand(HandSide.Left);
            ref readonly TrackedHand right = ref filter.Hand(HandSide.Right);
            if (!left.Tracked || !right.Tracked) return;
            Assert.That(math.distance(left.PalmCentre, right.PalmCentre), Is.GreaterThan(0.05f),
                "two hands never share a side: both sides show one palm at " + now);
        }

        [Test]
        public void Hands_Sides_NeverShare()
        {
            var rig = new HandsFilterRig();
            int leftHint = 0, rightHint = 1;
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                // Hints per result, by capture time: right for 1 s; then single results go wrong (a swap, two
                // unknowns in a row, both right, both left); then a whole second of "right" for both hands;
                // then a second of "left" for both; then a second of nothing; then right again.
                int index = (int)Math.Round(capture * 30.0);
                leftHint = 0;
                rightHint = 1;
                if (capture >= 1.0 && capture < 2.0)
                {
                    if (index % 10 == 0) { leftHint = 1; rightHint = 0; }
                    else if (index % 10 == 3 || index % 10 == 4) { leftHint = -1; rightHint = -1; }
                    else if (index % 10 == 6) { leftHint = 1; rightHint = 1; }
                    else if (index % 10 == 8) { leftHint = 0; rightHint = 0; }
                }
                else if (capture >= 2.0 && capture < 3.0) { leftHint = 1; rightHint = 1; }
                else if (capture >= 3.0 && capture < 4.0) { leftHint = 0; rightHint = 0; }
                else if (capture >= 4.0 && capture < 5.0) { leftHint = -1; rightHint = -1; }
                float drift = 0.008f * (float)capture;
                a = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist + new float3(drift, 0f, 0f), quaternion.identity, leftHint, 0.9f, 480f);
                b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist + new float3(drift, 0f, 0f), quaternion.identity, rightHint, 0.9f, 800f);
                return 2;
            };
            int checkedFrames = 0;
            rig.Run(6.0, source, now =>
            {
                if (now < 0.2) return;
                ref readonly TrackedHand left = ref rig.Filter.Hand(HandSide.Left);
                ref readonly TrackedHand right = ref rig.Filter.Hand(HandSide.Right);
                Assert.That(left.Tracked && right.Tracked, Is.True, "both hands are tracked at " + now);
                AssertSidesApart(rig.Filter, now);
                // The left hand is the one on the left: a wrong or missing label for a result flips nothing.
                Assert.That(left.PalmCentre.x, Is.LessThan(0f), "the left hand stays left at " + now);
                Assert.That(right.PalmCentre.x, Is.GreaterThan(0f), "the right hand stays right at " + now);
                checkedFrames++;
            });
            Assert.That(checkedFrames, Is.GreaterThan(300));
        }

        [Test]
        public void Hands_Sides_ASingleHand_KeepsItsSide_WhateverTheLabelsDo()
        {
            var rig = new HandsFilterRig();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                int index = (int)Math.Round(capture * 30.0);
                int hint = 1;
                if (capture >= 1.0)
                {
                    if (index % 9 == 0) hint = 0;          // a flipped label now and then
                    else if (index % 9 < 3) hint = -1;     // and runs of none
                }
                a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, hint, 0.9f, 300f);   // left half of the view
                return 1;
            };
            int frames = 0;
            rig.Run(4.0, source, now =>
            {
                if (now < 0.2) return;
                Assert.That(rig.Filter.Hand(HandSide.Right).Tracked, Is.True, "the hand stays on its side at " + now);
                Assert.That(rig.Filter.Hand(HandSide.Left).Tracked, Is.False, "and the other side stays empty at " + now);
                frames++;
            });
            Assert.That(frames, Is.GreaterThan(200));
        }

        [Test]
        public void Hands_Sides_WithoutAHint_FollowTheHalfOfTheViewTheyAreIn()
        {
            foreach (float pixelX in new[] { 300f, 1000f })
            {
                var rig = new HandsFilterRig();
                HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
                {
                    a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, -1, 0.9f, pixelX);
                    return 1;
                };
                rig.Run(1.0, source);
                HandSide expected = pixelX < 640f ? HandSide.Left : HandSide.Right;
                HandSide other = expected == HandSide.Left ? HandSide.Right : HandSide.Left;
                Assert.That(rig.Filter.Hand(expected).Tracked, Is.True, "a hand at picture column " + pixelX);
                Assert.That(rig.Filter.Hand(other).Tracked, Is.False);
            }

            // The centre of the view is the sense step's to set from the picture's width.
            var wide = new HandsFilterRig();
            wide.Filter.ViewCentreX = 960f;
            HandsFilterRig.Source middle = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, -1, 0.9f, 800f);
                return 1;
            };
            wide.Run(1.0, middle);
            Assert.That(wide.Filter.Hand(HandSide.Left).Tracked, Is.True, "column 800 of a 1920 wide picture is its left half");
        }

        [Test]
        public void Hands_Sides_TwoNewHandsWithTheSameLabel_AreToldApartByPosition()
        {
            foreach (int hint in new[] { 0, 1, -1 })
            {
                var rig = new HandsFilterRig();
                HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
                {
                    // Listed right-hand first, to show the order of the result does not matter.
                    a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, hint, 0.9f, 800f);
                    b = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist, quaternion.identity, hint, 0.9f, 480f);
                    return 2;
                };
                rig.Run(0.5, source);
                Assert.That(rig.Filter.Hand(HandSide.Left).Tracked && rig.Filter.Hand(HandSide.Right).Tracked, Is.True, "hint " + hint);
                Assert.That(rig.Filter.Hand(HandSide.Left).PalmCentre.x, Is.LessThan(0f), "the hand on the left of the picture is the left hand, hint " + hint);
                Assert.That(rig.Filter.Hand(HandSide.Right).PalmCentre.x, Is.GreaterThan(0f), "hint " + hint);
            }
        }

        [Test]
        public void Hands_Sides_TheMajorityOfTheLastLabelsDecides()
        {
            // A hand labelled left for a second and then right: the side follows once the right labels are the
            // majority of the last SideVotes, and not before.
            var rig = new HandsFilterRig();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                int hint = capture < 1.0 ? 0 : 1;
                a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, hint, 0.9f, 300f);
                return 1;
            };
            double flippedAt = -1.0;
            rig.Run(3.0, source, now =>
            {
                if (flippedAt < 0.0 && rig.Filter.Hand(HandSide.Right).Tracked) flippedAt = now;
            });
            Assert.That(flippedAt, Is.GreaterThan(1.0), "the label changed at 1.0 s; the side followed later than that");
            // 1.0 s is result 30; the 8th "right" label is result 37 (captured 37/30 s), received 0.08 s later.
            Assert.That(flippedAt, Is.InRange(37.0 / 30.0 + 0.08 - 1e-3, 37.0 / 30.0 + 0.08 + 1.0 / 60.0 + 1e-3));
        }

        // ---- Association and re-acquisition ----

        struct Return
        {
            public List<Snap> Frames;
            public int Back;
            public ObservedHand Seen;
        }

        // A right hand drifting at 2 cm/s, missing from the results for a gap of the given length starting at
        // 1.5 s, and found again offset by the given distance along x.
        static Return GapAndReturn(double gap, float offset)
        {
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            var seen = default(ObservedHand);
            double gapEnd = 1.5 + gap;
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture >= 1.5 && capture < gapEnd - 1e-9) return 0;
                float shift = capture >= gapEnd - 1e-9 ? offset : 0f;
                a = Right(RightWrist + new float3(0.02f * (float)capture + shift, 0f, 0f));
                if (capture >= gapEnd - 1e-9 && capture < gapEnd + 0.02) seen = a;     // the first result with the hand again
                return 1;
            };
            rig.Run(gapEnd + 1.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));
            // The first result with the hand again is captured at gapEnd and received 0.08 s later.
            int back = frames.FindIndex(f => f.Now >= gapEnd + rig.Age - 1e-9 && f.Fresh);
            return new Return { Frames = frames, Back = back, Seen = seen };
        }

        [Test]
        public void Hands_Filter_Reacquire()
        {
            // Back within the grace period and within ReacquireMetres: the filter carries on, with its velocity.
            {
                Return r = GapAndReturn(0.3, 0.05f);
                Snap s = r.Frames[r.Back];
                Assert.That(s.Tracked, Is.True);
                float toObserved = math.distance(s.Palm, r.Seen.PalmCentre);
                Assert.That(toObserved, Is.InRange(0.002f, 0.05f), "carried on: smoothed towards the detection, not snapped to it");
                Assert.That(math.length(s.Velocity), Is.GreaterThan(0.003f), "carried on: the velocity was kept");
                Assert.That(s.TrackedSeconds, Is.GreaterThan(1.5f), "the same hand");
                Assert.That(s.Good, Is.GreaterThan(2), "the detections in a row are not lost by a brief loss");
            }
            // Back within the grace period but farther than ReacquireMetres (and within the association distance): it restarts.
            {
                Return r = GapAndReturn(0.3, 0.18f);
                Snap s = r.Frames[r.Back];
                Assert.That(s.Tracked, Is.True);
                Assert.That(math.distance(s.Palm, r.Seen.PalmCentre), Is.LessThan(1e-4f), "restarted: the pose is the detection");
                Assert.That(math.length(s.Velocity), Is.EqualTo(0f).Within(1e-6f), "restarted: no velocity");
                Assert.That(s.TrackedSeconds, Is.GreaterThan(1.5f), "still the same hand: only the filter restarted");
            }
            // Back after the grace period: a new hand.
            {
                Return r = GapAndReturn(0.5, 0.02f);
                Assert.That(r.Frames[r.Back - 1].Tracked, Is.False, "the hand was given up");
                Snap s = r.Frames[r.Back];
                Assert.That(s.Tracked, Is.True);
                Assert.That(s.Good, Is.EqualTo(1));
                Assert.That(s.TrackedSeconds, Is.EqualTo(0f).Within(1e-4f));
                Assert.That(math.distance(s.Palm, r.Seen.PalmCentre), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void Hands_Filter_ReturnFarAway_TakesTheLostHandsPlace()
        {
            // Back within the grace period but beyond AssociateMetres, with the same label: the old hand does not
            // linger on the other side, and does not hold the side against the hand that is there now.
            var rig = new HandsFilterRig();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture >= 1.5 && capture < 1.8) return 0;
                float shift = capture >= 1.8 ? 0.30f : 0f;
                a = Right(RightWrist + new float3(shift, 0f, 0f));
                return 1;
            };
            bool back = false;
            rig.Run(3.0, source, now =>
            {
                ref readonly TrackedHand right = ref rig.Filter.Hand(HandSide.Right);
                if (right.Fresh && now > 1.8) back = true;
                if (!back) return;
                Assert.That(right.Tracked, Is.True, "the hand is on its own side at " + now);
                Assert.That(rig.Filter.Hand(HandSide.Left).Tracked, Is.False, "nothing is left of the old hand at " + now);
                Assert.That(right.PalmCentre.x, Is.GreaterThan(RightWrist.x + 0.25f));
            });
            Assert.That(back, Is.True);
        }

        [Test]
        public void Hands_Filter_CrossingHandsKeepTheirTracks()
        {
            // Hands carry no labels; they pass each other 6 cm apart, so only their motion tells them apart.
            var rig = new HandsFilterRig();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                float travel = 0.25f * (float)capture;
                float3 rightHand = new float3(0.2f - travel, -0.10f, 0.45f);
                float3 leftHand = new float3(-0.2f + travel, -0.04f, 0.45f);
                a = HandsFilterFixtures.Hand(HandSide.Right, rightHand, quaternion.identity, -1, 0.9f, 640f + 1000f * rightHand.x);
                b = HandsFilterFixtures.Hand(HandSide.Left, leftHand, quaternion.identity, -1, 0.9f, 640f + 1000f * leftHand.x);
                return 2;
            };
            rig.Run(1.6, source);
            // At 1.6 s the right-side hand has travelled from +0.2 to -0.2 and the other the opposite way.
            Assert.That(rig.Filter.Hand(HandSide.Right).PalmCentre.x, Is.LessThan(-0.1f), "the hand that started on the right is still one track");
            Assert.That(rig.Filter.Hand(HandSide.Left).PalmCentre.x, Is.GreaterThan(0.1f));
            Assert.That(rig.Filter.Hand(HandSide.Right).TrackedSeconds, Is.GreaterThan(1.4f), "and it was never restarted");
        }

        [Test]
        public void Hands_Filter_ALostHandCoastsAndStops()
        {
            // A hand moving at 0.5 m/s drops out of the results. It carries on along its velocity, which dies
            // away with CoastDecaySeconds, and stands still where it ends: no step at the moment of loss, no
            // coasting for ever.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture >= 2.0) return 0;
                a = Right(RightWrist + new float3(0.5f * (float)capture, 0f, 0f));
                return 1;
            };
            rig.Run(3.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            double lostFrom = 2.0 + rig.Age - 1e-9;
            int start = frames.FindIndex(f => f.Now >= lostFrom);
            float perFrame = 0.5f / 60f;
            // The frame the loss is learned in shows the pose it showed before, and the next one moves on by less than a frame of the hand's own speed.
            Assert.That(math.distance(frames[start].Palm, frames[start - 1].Palm), Is.LessThan(perFrame + 1e-3f), "no step at the moment of loss");
            Assert.That(math.distance(frames[start + 1].Palm, frames[start].Palm), Is.LessThan(perFrame + 1e-3f));
            // Velocity: the filtered velocity times exp(-lost / CoastDecaySeconds).
            float v0 = frames[start - 1].Velocity.x;
            foreach (int offset in new[] { 3, 6, 12 })
            {
                Snap s = frames[start + offset];
                float expected = v0 * (float)Math.Exp(-s.Lost / HandsConstants.CoastDecaySeconds);
                Assert.That(s.Velocity.x, Is.EqualTo(expected).Within(0.01f), "velocity " + s.Lost + " s into the loss");
            }
            // Held, then gone: it stopped well before it is given up.
            int late = frames.FindIndex(start, f => f.Lost >= 0.30f);
            Assert.That(math.distance(frames[late + 1].Palm, frames[late].Palm), Is.LessThan(5e-4f), "standing still by 0.3 s");
            // And it did not run on for more than the velocity times the prediction and the decay time.
            float travelled = frames[late].Palm.x - frames[start - 1].Palm.x;
            Assert.That(travelled, Is.InRange(0.0f, v0 * HandsConstants.CoastDecaySeconds * 1.05f));
        }

        [Test]
        public void Hands_Filter_ALiveHandThatJumpsRestartsTheFilter()
        {
            // 18 cm between two results is no hand's motion: it is a different place, not a smear to filter.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            ObservedHand seen = default(ObservedHand);
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                float shift = capture >= 1.0 ? 0.18f : 0f;
                a = Right(RightWrist + new float3(shift, 0f, 0f));
                if (capture >= 1.0 && capture < 1.02) seen = a;
                return 1;
            };
            rig.Run(2.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));
            Snap s = frames[frames.FindIndex(f => f.Now >= 1.0 + rig.Age - 1e-9 && f.Fresh)];
            Assert.That(s.Tracked, Is.True);
            Assert.That(math.distance(s.Palm, seen.PalmCentre), Is.LessThan(1e-4f));
            Assert.That(math.length(s.Velocity), Is.EqualTo(0f).Within(1e-6f));
            Assert.That(s.Lost, Is.EqualTo(0f));
            Assert.That(s.TrackedSeconds, Is.GreaterThan(0.9f), "the same hand");
        }

        [Test]
        public void Hands_Filter_ANewHandTakesTheSlotOfOneThatIsLost()
        {
            // Both hands are tracked. The right one drops out; a hand appears 40 cm from either, with the right
            // hand's label. The left hand is untouched, the right hand is that hand, and there are not three.
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            var leftFrames = new List<Snap>();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist, quaternion.identity, 0, 0.9f, 480f);
                if (capture < 1.0)
                {
                    b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, 1, 0.9f, 800f);
                    return 2;
                }
                if (capture < 1.2) return 1;                      // the right hand is not in the results
                b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist + new float3(0.4f, 0f, 0f), quaternion.identity, 1, 0.9f, 1100f);
                return 2;
            };
            rig.Run(2.5, source, now =>
            {
                frames.Add(Take(rig.Filter, HandSide.Right, now));
                leftFrames.Add(Take(rig.Filter, HandSide.Left, now));
            });
            int back = frames.FindIndex(f => f.Now >= 1.2 + rig.Age - 1e-9);
            for (int i = back; i < frames.Count; i++)
            {
                Assert.That(frames[i].Tracked, Is.True, "the right hand at " + frames[i].Now);
                Assert.That(frames[i].Palm.x, Is.GreaterThan(RightWrist.x + 0.3f), "is the hand that is there now, at " + frames[i].Now);
                Assert.That(leftFrames[i].Tracked, Is.True, "the left hand at " + frames[i].Now);
                Assert.That(leftFrames[i].Palm.x, Is.LessThan(0f));
                Assert.That(leftFrames[i].TrackedSeconds, Is.GreaterThan(1.0f), "the left hand never restarted, at " + frames[i].Now);
            }
            Assert.That(frames[back].Good, Is.EqualTo(1), "a new hand");
            Assert.That(leftFrames[back].Good, Is.GreaterThan(20), "the left hand's detections went on counting");

            // Both lost, and a single hand far from either: it needs a slot and takes the one longest unheard of.
            rig = new HandsFilterRig();
            HandsFilterRig.Source both = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                if (capture < 1.0)
                {
                    a = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist, quaternion.identity, 0, 0.9f, 480f);
                    b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, 1, 0.9f, 800f);
                    return 2;
                }
                if (capture < 1.2) return 0;
                a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist + new float3(0.5f, 0f, 0f), quaternion.identity, 1, 0.9f, 1200f);
                return 1;
            };
            rig.Run(1.4, both);
            ref readonly TrackedHand right = ref rig.Filter.Hand(HandSide.Right);
            Assert.That(right.Tracked, Is.True);
            Assert.That(right.Fresh || right.LostSeconds == 0f, Is.True, "the hand that is there is the right hand");
            Assert.That(right.PalmCentre.x, Is.GreaterThan(0.5f));
            Assert.That(rig.Filter.Hand(HandSide.Left).Tracked, Is.True, "the left hand is still held, lost");
            Assert.That(rig.Filter.Hand(HandSide.Left).LostSeconds, Is.GreaterThan(0f));
        }

        // ---- Far hands ----

        [Test]
        public void Hands_Filter_IgnoresFarHands()
        {
            // A hand more than MaxHandDistance from the camera is someone else's.
            float far = HandsConstants.MaxHandDistance + 0.3f;
            var rig = new HandsFilterRig();
            HandsFilterRig.Source farOnly = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist);
                a.PalmDistance = far;
                return 1;
            };
            rig.Run(1.0, farOnly, now =>
            {
                Assert.That(rig.Filter.Hand(HandSide.Right).Tracked || rig.Filter.Hand(HandSide.Left).Tracked, Is.False, "a far hand is never tracked, at " + now);
            });

            // Next to a near hand it takes no slot and disturbs nothing.
            rig = new HandsFilterRig();
            HandsFilterRig.Source both = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist, quaternion.identity, 0, 0.9f, 480f);
                b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, 1, 0.9f, 800f);
                b.PalmDistance = far;
                return 2;
            };
            rig.Run(1.0, both);
            Assert.That(rig.Filter.Hand(HandSide.Left).Tracked, Is.True);
            Assert.That(rig.Filter.Hand(HandSide.Right).Tracked, Is.False);
            Assert.That(rig.Filter.Hand(HandSide.Left).PalmCentre.x, Is.LessThan(0f));

            // The limit itself is inside: it is "more than".
            foreach (float distance in new[] { HandsConstants.MaxHandDistance, HandsConstants.MaxHandDistance + 0.001f })
            {
                rig = new HandsFilterRig();
                float d = distance;
                HandsFilterRig.Source edge = (double capture, ref ObservedHand a, ref ObservedHand b) =>
                {
                    a = Right(RightWrist);
                    a.PalmDistance = d;
                    return 1;
                };
                rig.Run(0.5, edge);
                Assert.That(rig.Filter.Hand(HandSide.Right).Tracked, Is.EqualTo(d <= HandsConstants.MaxHandDistance), "palm at " + d + " m");
            }

            // A tracked hand that goes beyond the limit is lost like any other, and given up.
            rig = new HandsFilterRig();
            HandsFilterRig.Source leaving = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist);
                if (capture >= 1.0) a.PalmDistance = far;
                return 1;
            };
            bool sawLoss = false;
            rig.Run(2.0, leaving, now =>
            {
                ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                if (h.Tracked && h.LostSeconds > 0.1f) sawLoss = true;
                if (now > 1.9) Assert.That(h.Tracked, Is.False, "given up at " + now);
            });
            Assert.That(sawLoss, Is.True, "held for a while first");
        }

        // ---- The fields ----

        [Test]
        public void Hands_Filter_FillsEveryField()
        {
            var rig = new HandsFilterRig();
            var frames = new List<Snap>();
            ObservedHand last = default(ObservedHand);
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = Right(RightWrist, capture < 0.5 ? 0.9f : 0.8f);
                a.EdgeMargin = 0.21f;
                a.PinchRatio = 0.37f;
                a.PalmDistance = 0.52f;
                a.PalmFromDepth = false;
                last = a;
                return 1;
            };
            rig.Run(1.0, source, now => frames.Add(Take(rig.Filter, HandSide.Right, now)));

            int fresh = 0;
            double firstTracked = -1.0;
            Snap previous = default(Snap);
            foreach (Snap f in frames)
            {
                if (!f.Tracked) continue;
                if (firstTracked < 0.0) firstTracked = f.Now;
                if (f.Fresh) fresh++;
                Assert.That(f.TrackedSeconds, Is.EqualTo((float)(f.Now - firstTracked)).Within(1e-4f), "TrackedSeconds at " + f.Now);
                Assert.That(f.Lost, Is.EqualTo(0f));
                // A result is received in the first frame at or after capture plus age, so its age is the 0.08 s
                // plus up to a frame; it grows by a frame until the next result is received.
                if (f.Fresh) Assert.That(f.Age, Is.InRange((float)rig.Age - 1e-4f, (float)rig.Age + 1f / 60f + 1e-4f), "AgeSeconds at " + f.Now);
                if (previous.Tracked && !f.Fresh) Assert.That(f.Age, Is.EqualTo(previous.Age + 1f / 60f).Within(1e-4f), "age grows between results");
                previous = f;
            }
            // The first frame that has the hand is a fresh one, and fresh frames are exactly the results received.
            Assert.That(fresh, Is.EqualTo(rig.Delivered), "Fresh is true exactly in the frames that received a detection");
            ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
            Assert.That(h.EdgeMargin, Is.EqualTo(0.21f));
            Assert.That(h.PinchRatio, Is.EqualTo(0.37f));
            Assert.That(h.PalmDistance, Is.EqualTo(0.52f));
            Assert.That(h.PalmFromDepth, Is.False);
            Assert.That(h.Confidence, Is.EqualTo(0.8f));
            Assert.That(math.length(h.PalmNormal), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(h.PalmNormal.y, Is.EqualTo(1f).Within(1e-3f));
            // The joints are where the hand is.
            ObservedHand truth = Right(RightWrist);
            for (int j = 0; j < HandJoints.Count; j++)
                Assert.That(math.distance(h.World[j], truth.Joints[j]), Is.LessThan(2e-3f), "joint " + j);
        }

        [Test]
        public void Hands_Filter_CountsGoodDetectionsInARow()
        {
            var filter = new HandsFilter();
            double now = 0.0;
            int[] confidences = { 90, 90, 90, 35, 90, 90 };      // percent
            int[] expected = { 1, 2, 3, 0, 1, 2 };
            ObservedHand none = default(ObservedHand);
            for (int i = 0; i < confidences.Length; i++)
            {
                ObservedHand a = Right(RightWrist, confidences[i] / 100f);
                filter.Observe(i / 30.0, 1, in a, in none);
                now = i / 30.0 + 0.05;
                filter.Advance(now);
                Assert.That(filter.Hand(HandSide.Right).GoodDetections, Is.EqualTo(expected[i]), "after detection " + i);
            }
            // A result that misses the hand does not end the count: the hold of a brief loss is not a new hand.
            filter.Observe(6 / 30.0, 0, in none, in none);
            filter.Advance(now + 1.0 / 30.0);
            Assert.That(filter.Hand(HandSide.Right).Tracked, Is.True);
            Assert.That(filter.Hand(HandSide.Right).GoodDetections, Is.EqualTo(2));
            ObservedHand again = Right(RightWrist, 0.9f);
            filter.Observe(7 / 30.0, 1, in again, in none);
            filter.Advance(now + 2.0 / 30.0);
            Assert.That(filter.Hand(HandSide.Right).GoodDetections, Is.EqualTo(3));
        }

        // ---- The shape of the output ----

        [Test]
        public void Hands_Filter_AMovingHandKeepsItsShape_AndItsFingersStayOnIt()
        {
            // The palm sweeps at 1 m/s while the hand turns; the fingers do not move in the palm's own frame, so
            // the filter must not smear them: every bone keeps its length, and every joint stays where it is
            // relative to the palm.
            var rig = new HandsFilterRig();
            JointSet open = HandModel.RestPose(HandSide.Right);
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                quaternion turn = quaternion.RotateY(0.8f * (float)capture);
                a = HandsFilterFixtures.Hand(in open, RightWrist + new float3(1f * (float)capture, 0f, 0f), turn, 1);
                return 1;
            };
            rig.Run(1.5, source, now =>
            {
                ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                if (!h.Tracked) return;
                for (int j = 1; j < HandJoints.Count; j++)
                {
                    float bone = math.distance(h.World[j], h.World[HandJoints.Parent(j)]);
                    Assert.That(bone, Is.EqualTo(HandModel.BoneLength(j)).Within(5e-4f), "bone to joint " + j + " at " + now);
                }
                // Relative to the palm centre, a fingertip is where the hand model puts it, in the filtered turn.
                float3 tip = h.World[HandJoint.MiddleTip] - h.PalmCentre;
                float expectedLength = math.length(open[HandJoint.MiddleTip] - new float3(0f, 0f, HandsFilterFixtures.PalmCentreZ));
                Assert.That(math.length(tip), Is.EqualTo(expectedLength).Within(1e-3f), "fingertip to palm at " + now);
            });
        }

        [Test]
        public void Hands_Filter_FingersFollowAFastCurl()
        {
            // Fingers have their own, quicker filter: a hand that closes in 0.2 s is drawn closed soon after.
            var rig = new HandsFilterRig();
            JointSet open = HandModel.RestPose(HandSide.Right);
            JointSet fist = HandModel.Pose(HandSide.Right, new float4(1f, 1f, 1f, 1f), 1f);
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                float t = math.saturate((float)(capture - 1.0) / 0.2f);
                JointSet pose = default(JointSet);
                for (int j = 0; j < HandJoints.Count; j++) pose[j] = math.lerp(open[j], fist[j], t);
                a = HandsFilterFixtures.Hand(in pose, RightWrist, quaternion.identity, 1);
                return 1;
            };
            var closed = default(ObservedHand);
            closed = HandsFilterFixtures.Hand(in fist, RightWrist, quaternion.identity, 1);
            float gapAtEnd = -1f, gapLater = -1f;
            rig.Run(2.5, source, now =>
            {
                ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                float gap = math.distance(h.World[HandJoint.IndexTip], closed.Joints[HandJoint.IndexTip]);
                // The hand finished closing at 1.2 s (captured), received 0.08 s later.
                if (now >= 1.0 + 0.2 + rig.Age + 0.30 && gapAtEnd < 0f) gapAtEnd = gap;
                if (now >= 2.4) gapLater = gap;
            });
            TestContext.WriteLine("curl: fingertip " + (gapAtEnd * 1000f) + " mm from closed 0.3 s after the last result, " + (gapLater * 1000f) + " mm later");
            Assert.That(gapAtEnd, Is.LessThan(0.01f), "within 0.3 s of the last result the closed hand is drawn closed");
            Assert.That(gapLater, Is.LessThan(0.002f));
        }

        [Test]
        public void Hands_Filter_FollowsATurningHand()
        {
            // The palm rolls at 2 rad/s about the fingers' axis. The orientation lags by the filter only: the
            // palm normal is within 6 degrees of the hand's at the time of the detection behind it.
            var rig = new HandsFilterRig();
            const float rate = 2f;
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.RotateZ(rate * (float)capture), 1);
                return 1;
            };
            float worst = 0f;
            rig.Run(2.0, source, now =>
            {
                ref readonly TrackedHand h = ref rig.Filter.Hand(HandSide.Right);
                if (!h.Tracked || now < 0.8) return;
                float3 truth = math.mul(quaternion.RotateZ(rate * (float)(now - h.AgeSeconds)), new float3(0f, 1f, 0f));
                float angle = math.degrees(math.acos(math.clamp(math.dot(truth, h.PalmNormal), -1f, 1f)));
                Assert.That(math.length(h.PalmNormal), Is.EqualTo(1f).Within(1e-4f));
                worst = math.max(worst, angle);
            });
            TestContext.WriteLine("turning hand: worst palm-normal lag " + worst + " degrees");
            Assert.That(worst, Is.LessThan(6f), "worst lag of the palm normal, degrees");
        }

        // ---- Housekeeping ----

        [Test]
        public void Hands_Filter_Reset_ForgetsEveryHand()
        {
            var rig = new HandsFilterRig();
            HandsFilterRig.Source source = (double capture, ref ObservedHand a, ref ObservedHand b) =>
            {
                a = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist, quaternion.identity, 0);
                b = HandsFilterFixtures.Hand(HandSide.Right, RightWrist, quaternion.identity, 1);
                return 2;
            };
            rig.Run(1.0, source);
            Assert.That(rig.Filter.Hand(HandSide.Left).Tracked && rig.Filter.Hand(HandSide.Right).Tracked, Is.True);
            rig.Filter.Reset();
            Assert.That(rig.Filter.Hand(HandSide.Left).Tracked, Is.False);
            Assert.That(rig.Filter.Hand(HandSide.Right).Tracked, Is.False);
            Assert.That(rig.Filter.Hand(HandSide.Right).GoodDetections, Is.EqualTo(0));

            // And it starts again from nothing, even with a capture time earlier than before.
            ObservedHand hand = Right(RightWrist);
            ObservedHand none = default(ObservedHand);
            rig.Filter.Observe(0.1, 1, in hand, in none);
            rig.Filter.Advance(0.15);
            Assert.That(rig.Filter.Hand(HandSide.Right).Tracked, Is.True);
            Assert.That(rig.Filter.Hand(HandSide.Right).Fresh, Is.True);
            Assert.That(rig.Filter.Hand(HandSide.Right).TrackedSeconds, Is.EqualTo(0f));
        }

        [Test]
        public void Hands_Filter_BadInput_ChangesNothing()
        {
            var filter = new HandsFilter();
            ObservedHand good = Right(RightWrist);
            ObservedHand none = default(ObservedHand);
            filter.Observe(0.0, 1, in good, in none);
            filter.Advance(0.05);
            float3 before = filter.Hand(HandSide.Right).PalmCentre;

            ObservedHand nan = Right(RightWrist);
            nan.PalmCentre = new float3(float.NaN, 0f, 0f);
            ObservedHand nanJoint = Right(RightWrist);
            JointSet joints = nanJoint.Joints;
            joints[HandJoint.IndexTip] = new float3(0f, float.PositiveInfinity, 0f);
            nanJoint.Joints = joints;
            ObservedHand noNormal = Right(RightWrist);
            noNormal.PalmNormal = float3.zero;
            filter.Observe(1.0 / 30.0, 1, in nan, in none);
            filter.Advance(0.07);
            filter.Observe(2.0 / 30.0, 1, in nanJoint, in none);
            filter.Advance(0.09);
            filter.Observe(3.0 / 30.0, 1, in noNormal, in none);
            filter.Advance(0.11);
            ref readonly TrackedHand h = ref filter.Hand(HandSide.Right);
            Assert.That(h.Tracked, Is.True, "the hand is held, as through any miss");
            Assert.That(math.all(math.isfinite(h.PalmCentre)), Is.True);
            Assert.That(math.distance(h.PalmCentre, before), Is.LessThan(1e-4f), "a bad detection moves nothing");
            Assert.That(h.World.IsFinite(), Is.True);

            // A result that is not newer than the last is ignored; a result with an unusable time too.
            filter.Observe(3.0 / 30.0, 1, in good, in none);
            filter.Observe(double.NaN, 1, in good, in none);
            filter.Advance(0.13);
            Assert.That(filter.Hand(HandSide.Right).Fresh, Is.False);
            // Counts outside 0 to 2 are taken as the nearest.
            filter.Observe(0.5, 7, in good, in good);
            filter.Observe(0.6, -3, in good, in good);
            filter.Advance(0.7);
            Assert.That(filter.Hand(HandSide.Right).Tracked || filter.Hand(HandSide.Left).Tracked, Is.True);
        }

        [Test]
        public void Hands_Filter_AllocatesNothing()
        {
            var filter = new HandsFilter();
            ObservedHand left = default(ObservedHand), right = default(ObservedHand);
            ObservedHand none = default(ObservedHand);
            // Both hands present, then one, then none, round and round: every path of Observe and Advance.
            for (int frame = 0; frame < 400; frame++) Step(filter, frame, ref left, ref right, in none);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 400; frame < 1600; frame++) Step(filter, frame, ref left, ref right, in none);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 1200 frames of Observe and Advance");
            Assert.That(filter.Hand(HandSide.Left).PalmCentre.x, Is.Not.NaN);
        }

        static void Step(HandsFilter filter, int frame, ref ObservedHand left, ref ObservedHand right, in ObservedHand none)
        {
            double now = frame / 60.0;
            if (frame % 2 == 0)
            {
                double capture = now - 0.08;
                float wobble = 0.05f * (float)Math.Sin(capture * 3.0);
                left = HandsFilterFixtures.Hand(HandSide.Left, LeftWrist + new float3(wobble, 0f, 0f), quaternion.RotateZ(wobble), 0);
                right = HandsFilterFixtures.Hand(HandSide.Right, RightWrist + new float3(-wobble, wobble, 0f), quaternion.RotateX(wobble), 1);
                int phase = (frame / 90) % 4;
                if (phase == 0) filter.Observe(capture, 2, in left, in right);
                else if (phase == 1) filter.Observe(capture, 1, in right, in none);
                else if (phase == 2) filter.Observe(capture, 0, in none, in none);
                else filter.Observe(capture, 2, in right, in left);
            }
            filter.Advance(now);
            // Read it all, as a consumer would.
            ref readonly TrackedHand l = ref filter.Hand(HandSide.Left);
            ref readonly TrackedHand r = ref filter.Hand(HandSide.Right);
            if (l.Tracked && r.Tracked && math.distance(l.PalmCentre, r.PalmCentre) < 0f) throw new InvalidOperationException();
        }
    }
}
