using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// How a hand appears and disappears (documents/ATLAS_Hands_Design.md section 5.4, with decision R2 for the
    /// loss: a lost hand holds full opacity for 0.25 s and then fades over 0.15 s). Frames are 60 Hz.
    /// </summary>
    public class HandsVisibilityTests
    {
        const float Dt = 1f / 60f;
        /// <summary>One frame, and a hair for float rounding: the tolerance of "within one frame".</summary>
        const float OneFrame = Dt + 1e-4f;

        static TrackedHand Seen()
        {
            return new TrackedHand
            {
                Tracked = true,
                Fresh = true,
                Confidence = 0.9f,
                GoodDetections = HandsConstants.EnterDetections,
                EdgeMargin = 0.5f,
                TrackedSeconds = 1f
            };
        }

        static VisibilityInputs Calm() { return new VisibilityInputs { GateActive = true }; }

        /// <summary>A hand that has been in view long enough to be at full opacity.</summary>
        static HandsVisibility Settled(out TrackedHand hand, out VisibilityInputs inputs)
        {
            var visibility = new HandsVisibility();
            hand = Seen();
            inputs = Calm();
            for (int i = 0; i < 40; i++) visibility.Update(HandSide.Left, Dt, in hand, in inputs);
            Assert.That(visibility.Opacity(HandSide.Left), Is.EqualTo(1f), "a settled hand is fully opaque");
            return visibility;
        }

        /// <summary>Frames until the opacity reaches the target from above or below, or -1 after the cap.</summary>
        static int FramesUntil(HandsVisibility v, ref TrackedHand hand, ref VisibilityInputs inputs, float target, bool rising, int cap = 600)
        {
            for (int n = 1; n <= cap; n++)
            {
                float opacity = v.Update(HandSide.Left, Dt, in hand, in inputs);
                if (rising ? opacity >= target : opacity <= target) return n;
            }
            return -1;
        }

        static void AssertTakes(int frames, float seconds, string what)
        {
            Assert.That(frames, Is.GreaterThan(0), what + ": never got there");
            Assert.That(frames * Dt, Is.EqualTo(seconds).Within(OneFrame), what);
        }

        // ---- Hands_Visibility_Ramps ----

        [Test]
        public void Hands_Visibility_Ramps()
        {
            PresenceRisesOverFadeIn();
            ALostHandHoldsFullOpacityThenFallsOverFadeOut();
            LowConfidenceFallsOverFadeOut();
            EdgeRunsFromZeroAtTheBorderToOneAtTheBand();
            WarningFallsFastAndReturnsSlowly();
            GateFallsOverGateOut();
            HiddenIsZeroAtOnceAndBackNextFrame();
            OpacityIsTheSmallestOfTheFiveFactors();
        }

        static void PresenceRisesOverFadeIn()
        {
            var v = new HandsVisibility();
            TrackedHand hand = Seen();
            VisibilityInputs inputs = Calm();

            // Tracked, but detected only once: not shown yet.
            hand.GoodDetections = HandsConstants.EnterDetections - 1;
            for (int i = 0; i < 30; i++) Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f));

            // Detected twice running: it rises, linearly, over FadeInSeconds.
            hand.GoodDetections = HandsConstants.EnterDetections;
            float first = v.Update(HandSide.Left, Dt, in hand, in inputs);
            Assert.That(first, Is.EqualTo(Dt / HandsConstants.FadeInSeconds).Within(1e-5f), "it starts to rise in the frame it is shown");
            float second = v.Update(HandSide.Left, Dt, in hand, in inputs);
            Assert.That(second, Is.EqualTo(2f * Dt / HandsConstants.FadeInSeconds).Within(1e-5f), "linearly");
            AssertTakes(2 + FramesUntil(v, ref hand, ref inputs, 1f, true), HandsConstants.FadeInSeconds, "presence rises over FadeInSeconds");
            Assert.That(v.Opacity(HandSide.Left), Is.EqualTo(1f));
        }

        static void ALostHandHoldsFullOpacityThenFallsOverFadeOut()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            hand.Fresh = false;
            float lost = 0f;
            int frame = 0;

            // The hold: not one frame short of full opacity until LostSeconds reaches LossFadeStartSeconds.
            while (true)
            {
                frame++;
                lost = frame * Dt;
                if (lost >= HandsConstants.LossFadeStartSeconds - 1e-4f) break;
                hand.LostSeconds = lost;
                Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f), "held at full opacity at " + lost + " s of loss");
            }

            // Then it falls over FadeOutSeconds and is gone when tracking ends (LossGraceSeconds).
            float previous = 1f;
            int falling = 0;
            float opacity = 1f;
            while (opacity > 0f && falling < 100)
            {
                hand.LostSeconds = frame * Dt;
                opacity = v.Update(HandSide.Left, Dt, in hand, in inputs);
                Assert.That(opacity, Is.LessThanOrEqualTo(previous), "it only falls");
                previous = opacity;
                frame++;
                falling++;
            }
            AssertTakes(falling, HandsConstants.FadeOutSeconds, "presence falls over FadeOutSeconds once the hold is over");
            float goneAt = (frame - 1) * Dt;
            Assert.That(goneAt, Is.LessThanOrEqualTo(HandsConstants.LossGraceSeconds + 1e-4f),
                "invisible by the time the filter stops tracking it");
            Assert.That(goneAt, Is.GreaterThanOrEqualTo(HandsConstants.LossGraceSeconds - 2f * Dt));

            // A hand that is no longer tracked is not drawn at all.
            hand.Tracked = false;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f));
        }

        static void LowConfidenceFallsOverFadeOut()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);

            hand.Confidence = HandsConstants.ExitConfidence;           // exactly the limit is not under it
            for (int i = 0; i < 30; i++) Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f));

            hand.Confidence = HandsConstants.ExitConfidence - 0.01f;
            AssertTakes(FramesUntil(v, ref hand, ref inputs, 0f, false), HandsConstants.FadeOutSeconds, "presence falls over FadeOutSeconds under ExitConfidence");

            // Having left, it must be detected twice running again before it is shown.
            hand.Confidence = 0.4f;
            hand.GoodDetections = 0;
            for (int i = 0; i < 30; i++) Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f));
            hand.Confidence = 0.9f;
            hand.GoodDetections = HandsConstants.EnterDetections;
            AssertTakes(FramesUntil(v, ref hand, ref inputs, 1f, true), HandsConstants.FadeInSeconds, "and then it rises over FadeInSeconds again");

            // A hand already shown stays shown between the two confidences, whatever its run of good detections.
            hand.Confidence = 0.5f * (HandsConstants.ExitConfidence + HandsConstants.EnterConfidence);
            hand.GoodDetections = 0;
            for (int i = 0; i < 30; i++) Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f));
        }

        static void EdgeRunsFromZeroAtTheBorderToOneAtTheBand()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            float band = HandsConstants.EdgeFadeBand;
            float[] margins = { -0.05f, 0f, 0.25f * band, 0.5f * band, 0.75f * band, band, 2f * band, 0.5f };
            float[] expected = { 0f, 0f, 0.25f, 0.5f, 0.75f, 1f, 1f, 1f };
            for (int i = 0; i < margins.Length; i++)
            {
                hand.EdgeMargin = margins[i];
                Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(expected[i]).Within(1e-5f), "margin " + margins[i]);
            }
        }

        static void WarningFallsFastAndReturnsSlowly()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            inputs.Warning = true;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f - Dt / HandsConstants.WarningOutSeconds).Within(1e-5f));
            AssertTakes(1 + FramesUntil(v, ref hand, ref inputs, 0f, false), HandsConstants.WarningOutSeconds, "falls over WarningOutSeconds");

            inputs.Warning = false;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(Dt / HandsConstants.WarningBackSeconds).Within(1e-5f));
            AssertTakes(1 + FramesUntil(v, ref hand, ref inputs, 1f, true), HandsConstants.WarningBackSeconds, "returns over WarningBackSeconds");

            // A short warning only goes part of the way down, and the way back starts from there.
            inputs.Warning = true;
            for (int i = 0; i < 3; i++) v.Update(HandSide.Left, Dt, in hand, in inputs);
            float low = v.Opacity(HandSide.Left);
            Assert.That(low, Is.EqualTo(1f - 3f * Dt / HandsConstants.WarningOutSeconds).Within(1e-5f));
            inputs.Warning = false;
            int back = FramesUntil(v, ref hand, ref inputs, 1f, true);
            Assert.That(back * Dt, Is.EqualTo((1f - low) * HandsConstants.WarningBackSeconds).Within(OneFrame));
        }

        static void GateFallsOverGateOut()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            inputs.GateActive = false;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f - Dt / HandsConstants.GateOutSeconds).Within(1e-5f));
            AssertTakes(1 + FramesUntil(v, ref hand, ref inputs, 0f, false), HandsConstants.GateOutSeconds, "falls over GateOutSeconds");

            // Active again: back at once (the hand's own presence still ramps a new hand in).
            inputs.GateActive = true;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f));
        }

        static void HiddenIsZeroAtOnceAndBackNextFrame()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            inputs.Hidden = true;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f), "hidden: zero in the same frame");
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f), "and while it is asked for");
            inputs.Hidden = false;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f), "back in the next frame");
        }

        static void OpacityIsTheSmallestOfTheFiveFactors()
        {
            // Each of the other factors on its own, from a settled hand: what it alone does frame by frame.
            const int frames = 60;
            float[] warning = new float[frames], gate = new float[frames], edge = new float[frames], hidden = new float[frames];
            for (int kind = 0; kind < 4; kind++)
            {
                var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
                for (int i = 0; i < frames; i++)
                {
                    hand.EdgeMargin = 0.5f;
                    inputs.Warning = false;
                    inputs.GateActive = true;
                    inputs.Hidden = false;
                    Scenario(i, ref hand, ref inputs, kind);
                    float o = v.Update(HandSide.Left, Dt, in hand, in inputs);
                    if (kind == 0) warning[i] = o;
                    if (kind == 1) gate[i] = o;
                    if (kind == 2) edge[i] = o;
                    if (kind == 3) hidden[i] = o;
                }
            }

            // All four together.
            var combined = Settled(out TrackedHand together, out VisibilityInputs both);
            for (int i = 0; i < frames; i++)
            {
                together.EdgeMargin = 0.5f;
                both.Warning = false;
                both.GateActive = true;
                both.Hidden = false;
                for (int kind = 0; kind < 4; kind++) Scenario(i, ref together, ref both, kind);
                float o = combined.Update(HandSide.Left, Dt, in together, in both);
                Assert.That(combined.Opacity(HandSide.Left), Is.EqualTo(o), "Opacity reads what Update returned");
                float expected = math.min(math.min(warning[i], gate[i]), math.min(edge[i], hidden[i]));
                Assert.That(o, Is.EqualTo(expected).Within(1e-5f), "frame " + i);
            }

            // Presence rising from nothing under a small edge factor: the edge holds it down until presence passes it.
            var rising = new HandsVisibility();
            TrackedHand entering = Seen();
            entering.EdgeMargin = 0.25f * HandsConstants.EdgeFadeBand;
            VisibilityInputs calm = Calm();
            for (int i = 1; i <= 30; i++)
            {
                float o = rising.Update(HandSide.Left, Dt, in entering, in calm);
                float presence = math.min(1f, i * Dt / HandsConstants.FadeInSeconds);
                Assert.That(o, Is.EqualTo(math.min(presence, 0.25f)).Within(1e-5f), "frame " + i);
            }
        }

        /// <summary>The input that makes one factor act: the warning from frame 5 to 14, the gate off from 20, the edge near from 30, hidden at 40.</summary>
        static void Scenario(int frame, ref TrackedHand hand, ref VisibilityInputs inputs, int kind)
        {
            if (kind == 0) inputs.Warning = frame >= 5 && frame < 15;
            if (kind == 1) inputs.GateActive = frame < 20;
            if (kind == 2) hand.EdgeMargin = frame < 30 ? 0.5f : 0.5f * HandsConstants.EdgeFadeBand;
            if (kind == 3) inputs.Hidden = frame == 40;
        }

        // ---- The rest of the behaviour ----

        [Test]
        public void Hands_Visibility_AReturningHandRisesFromWhereItWasAndDoesNotPop()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            hand.Fresh = false;
            int frame = 0;
            float opacity = 1f;
            while (opacity > 0.5f && frame < 120)          // a cap, so that a fault fails the test and does not hang it
            {
                frame++;
                hand.LostSeconds = frame * Dt;
                opacity = v.Update(HandSide.Left, Dt, in hand, in inputs);
            }
            Assert.That(opacity, Is.LessThanOrEqualTo(0.5f), "the lost hand is halfway through its fade");
            // The hand is detected again in the middle of the fade.
            hand.Fresh = true;
            hand.LostSeconds = 0f;
            float before = opacity;
            for (int i = 0; i < 20; i++)
            {
                float now = v.Update(HandSide.Left, Dt, in hand, in inputs);
                Assert.That(now - before, Is.InRange(0f, Dt / HandsConstants.FadeInSeconds + 1e-5f), "a return rises by one step at most, never a jump to full");
                before = now;
            }
            Assert.That(before, Is.EqualTo(1f));
        }

        [Test]
        public void Hands_Visibility_AHandThatIsBeingDetectedStaysFullBetweenDetections()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            // At 30 Hz the time since the last detection runs from 0 to 33 ms and back, over and over.
            for (int i = 0; i < 120; i++)
            {
                hand.Fresh = (i & 1) == 0;
                hand.LostSeconds = hand.Fresh ? 0f : 1f / 30f;
                Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f), "frame " + i);
            }
        }

        [Test]
        public void Hands_Visibility_ALossIgnoresTheConfidenceTheFilterLeavesBehind()
        {
            // Whatever confidence a held pose carries, the hold of section 5.4 is about time alone.
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            hand.Fresh = false;
            hand.Confidence = 0f;
            for (int i = 1; i * Dt < HandsConstants.LossFadeStartSeconds - 2f * Dt; i++)
            {
                hand.LostSeconds = i * Dt;
                Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(1f), "at " + i * Dt + " s");
            }
        }

        [Test]
        public void Hands_Visibility_SidesAreIndependentAndResetClearsThem()
        {
            var v = Settled(out TrackedHand left, out VisibilityInputs calm);
            Assert.That(v.Opacity(HandSide.Right), Is.EqualTo(0f), "the right hand has not been seen");

            TrackedHand right = Seen();
            VisibilityInputs warning = calm;
            warning.Warning = true;
            for (int i = 0; i < 40; i++)
            {
                v.Update(HandSide.Right, Dt, in right, in calm);
                v.Update(HandSide.Left, Dt, in left, in warning);
            }
            Assert.That(v.Opacity(HandSide.Right), Is.EqualTo(1f), "a warning given for the left hand does not touch the right");
            Assert.That(v.Opacity(HandSide.Left), Is.EqualTo(0f));

            v.Reset();
            Assert.That(v.Opacity(HandSide.Left), Is.EqualTo(0f));
            Assert.That(v.Opacity(HandSide.Right), Is.EqualTo(0f));
            // After a reset nothing is remembered: a hand is shown again from nothing.
            float first = v.Update(HandSide.Right, Dt, in right, in calm);
            Assert.That(first, Is.EqualTo(Dt / HandsConstants.FadeInSeconds).Within(1e-5f));
        }

        [Test]
        public void Hands_Visibility_AnUntrackedHandOrAnInactiveGateShowsNothingAtOnce()
        {
            var v = new HandsVisibility();
            TrackedHand hand = Seen();
            VisibilityInputs inactive = default(VisibilityInputs);
            // The gate has never been active: the hand is not shown, whatever it does.
            for (int i = 0; i < 40; i++) Assert.That(v.Update(HandSide.Left, Dt, in hand, in inactive), Is.EqualTo(0f));

            var settled = Settled(out TrackedHand tracked, out VisibilityInputs calm);
            tracked.Tracked = false;
            Assert.That(settled.Update(HandSide.Left, Dt, in tracked, in calm), Is.EqualTo(0f));
            tracked.Tracked = true;
            tracked.GoodDetections = 0;
            Assert.That(settled.Update(HandSide.Left, Dt, in tracked, in calm), Is.EqualTo(0f), "a hand that comes back is shown again from nothing");
        }

        [Test]
        public void Hands_Visibility_SurvivesGarbageInput()
        {
            var v = Settled(out TrackedHand hand, out VisibilityInputs inputs);
            hand.EdgeMargin = float.NaN;
            Assert.That(v.Update(HandSide.Left, Dt, in hand, in inputs), Is.EqualTo(0f), "an edge margin that is not a number hides the hand");
            hand.EdgeMargin = 0.5f;

            Assert.That(v.Update(HandSide.Left, float.NaN, in hand, in inputs), Is.InRange(0f, 1f));
            float before = v.Opacity(HandSide.Left);
            Assert.That(v.Update(HandSide.Left, -1f, in hand, in inputs), Is.EqualTo(before), "a negative step is no step");
            Assert.That(v.Update(HandSide.Left, 0f, in hand, in inputs), Is.EqualTo(before));

            var hitch = new HandsVisibility();
            hand.Confidence = 0.9f;
            hand.GoodDetections = HandsConstants.EnterDetections;
            Assert.That(hitch.Update(HandSide.Left, 100f, in hand, in inputs), Is.EqualTo(1f), "a long hitch completes a ramp and no more");

            hand.Confidence = float.NaN;
            hand.GoodDetections = 0;
            for (int i = 0; i < 20; i++) v.Update(HandSide.Left, Dt, in hand, in inputs);
            Assert.That(v.Opacity(HandSide.Left), Is.EqualTo(0f), "a confidence that is not a number does not keep a hand on screen");
        }

        // ---- Allocation ----

        [Test]
        public void Hands_Visibility_UpdateAllocatesNothing()
        {
            var v = new HandsVisibility();
            TrackedHand left = Seen(), right = Seen();
            VisibilityInputs inputs = Calm();

            Run(v, ref left, ref right, ref inputs, 600);               // warm up through every path

            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(v, ref left, ref right, ref inputs, 100);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 100 updates of both hands");
        }

        static void Run(HandsVisibility v, ref TrackedHand left, ref TrackedHand right, ref VisibilityInputs inputs, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                int phase = i % 240;
                left.Fresh = right.Fresh = (i & 1) == 0;
                left.LostSeconds = phase > 120 && phase < 180 ? (phase - 120) * Dt : 0f;
                right.Tracked = phase != 200;
                left.EdgeMargin = 0.01f * (phase % 12);
                right.Confidence = phase < 60 ? 0.2f : 0.9f;
                inputs.Warning = phase > 20 && phase < 40;
                inputs.GateActive = phase < 220;
                inputs.Hidden = phase == 100;
                float a = v.Update(HandSide.Left, Dt, in left, in inputs) + v.Update(HandSide.Right, Dt, in right, in inputs)
                        + v.Opacity(HandSide.Left);
                if (float.IsNaN(a)) throw new InvalidOperationException("not a number");
            }
        }
    }
}
