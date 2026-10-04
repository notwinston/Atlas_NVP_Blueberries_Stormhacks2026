using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// Pinch and two-hand recognition (documents/ATLAS_Hands_Design.md sections 6.3 and 6.4). Time is a manual
    /// clock: the display runs at 60 Hz and the detector at 30 Hz, so every second display frame carries a
    /// detection and the frame in between holds the last one.
    /// </summary>
    public class HandsGesturesTests
    {
        const double Frame = 1.0 / 60.0;
        /// <summary>A ratio that is a closed pinch, one that is an open hand, and one inside the hysteresis band.</summary>
        const float Under = HandsConstants.PinchClose - 0.05f;
        const float Open = HandsConstants.PinchOpen + 0.10f;
        const float Band = 0.5f * (HandsConstants.PinchClose + HandsConstants.PinchOpen);

        // ---- A scripted pair of hands ----

        sealed class Rig
        {
            public readonly HandsGestures G = new HandsGestures();
            public TrackedHand L = MakeHand(HandSide.Left);
            public TrackedHand R = MakeHand(HandSide.Right);
            public bool LeftHover, RightHover;
            public quaternion Head = quaternion.identity;
            public double Now = 100.0;
            public int DownL, DownR, UpL, UpR, Started, Ended;
            public double LastDownL;

            /// <summary>One display frame. fresh: a detection arrived in this frame.</summary>
            public void Step(bool fresh)
            {
                Now += Frame;
                L.Fresh = fresh;
                R.Fresh = fresh;
                L.TrackedSeconds += (float)Frame;
                R.TrackedSeconds += (float)Frame;
                G.Update(Now, in L, in R, Head, LeftHover, RightHover);
                if (G.Pinch(HandSide.Left).Down) { DownL++; LastDownL = Now; }
                if (G.Pinch(HandSide.Right).Down) DownR++;
                if (G.Pinch(HandSide.Left).Up) UpL++;
                if (G.Pinch(HandSide.Right).Up) UpR++;
                if (G.TwoHandStarted) Started++;
                if (G.TwoHandEnded) Ended++;
            }

            /// <summary>One detection at the detector's 30 Hz: a fresh frame, then a held one.</summary>
            public void Detect() { Step(true); Step(false); }

            public void Detect(float left, float right)
            {
                L.PinchRatio = left;
                R.PinchRatio = right;
                Detect();
            }

            public void Detect(float left, float right, int times)
            {
                for (int i = 0; i < times; i++) Detect(left, right);
            }

            /// <summary>Both thumb-and-index midpoints, world.</summary>
            public void Place(float3 left, float3 right)
            {
                SetPinchPoint(ref L, left);
                SetPinchPoint(ref R, right);
            }

            public PinchState Left { get { return G.Pinch(HandSide.Left); } }
            public PinchState Right { get { return G.Pinch(HandSide.Right); } }
        }

        static TrackedHand MakeHand(HandSide side)
        {
            float x = side == HandSide.Left ? -0.12f : 0.12f;
            var hand = new TrackedHand
            {
                Tracked = true,
                Fresh = true,
                Confidence = 0.9f,
                TrackedSeconds = 10f,
                GoodDetections = 10,
                PinchRatio = Open,
                PalmCentre = new float3(x, 0f, 0.45f),
                PalmNormal = new float3(0f, 0f, -1f),
                PalmVelocity = float3.zero
            };
            SetPinchPoint(ref hand, new float3(x, 0f, 0.45f));
            return hand;
        }

        static void SetPinchPoint(ref TrackedHand hand, float3 point)
        {
            hand.World[HandJoint.ThumbTip] = point - new float3(0.004f, 0f, 0f);
            hand.World[HandJoint.IndexTip] = point + new float3(0.004f, 0f, 0f);
        }

        // ---- Hands_Pinch_Hysteresis ----

        [Test]
        public void Hands_Pinch_Hysteresis()
        {
            ClosesAfterThreeFreshDetections();
            ARunIsBrokenByAnyDetectionAtOrOverTheCloseThreshold();
            OpensOnlyOverTheOpenThreshold();
            AmountRunsLinearlyFromOpenToClose();
            NoChatterOnANoisyRamp();
            HoveringFiresNothing();
            TheFirstMomentsOfAHandFireNothing();
            AFastHandFiresNothing();
            ASecondPinchTooSoonFiresNothing();
            ALowConfidenceFiresNothing();
        }

        static void ClosesAfterThreeFreshDetections()
        {
            var rig = new Rig();
            int need = HandsConstants.PinchDetections;
            rig.Detect(Under, Open, need - 1);
            Assert.That(rig.Left.Pinching, Is.False, "fewer detections than PinchDetections are not a pinch");

            // Held frames repeat the last detection and must not count as new ones.
            for (int i = 0; i < 40; i++) rig.Step(false);
            Assert.That(rig.Left.Pinching, Is.False, "held frames do not count as detections");
            Assert.That(rig.DownL, Is.EqualTo(0));

            rig.Step(true);
            PinchState p = rig.Left;
            Assert.That(p.Pinching, Is.True, "the last needed detection closes the pinch in its own frame");
            Assert.That(p.Down, Is.True, "Down on the closing frame");
            Assert.That(p.Up, Is.False);
            Assert.That(p.Amount, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(rig.Right.Pinching, Is.False, "the other hand is its own");

            rig.Step(false);
            p = rig.Left;
            Assert.That(p.Pinching, Is.True);
            Assert.That(p.Down, Is.False, "Down lasts one frame");
            Assert.That(rig.DownL, Is.EqualTo(1));
        }

        static void ARunIsBrokenByAnyDetectionAtOrOverTheCloseThreshold()
        {
            var rig = new Rig();
            int need = HandsConstants.PinchDetections;
            rig.Detect(Under, Open, need - 1);
            rig.Detect(HandsConstants.PinchClose, Open);      // exactly the threshold is not under it
            rig.Detect(Under, Open, need - 1);
            Assert.That(rig.Left.Pinching, Is.False, "a detection at the close threshold restarts the run");
            rig.Detect(Under, Open, 1);
            Assert.That(rig.Left.Pinching, Is.True, "PinchDetections in a row after the break close it");

            rig = new Rig();
            rig.Detect(Under, Open, need - 1);
            rig.Detect(Band, Open);                           // inside the band: not under the close threshold
            rig.Detect(Under, Open, need - 1);
            Assert.That(rig.Left.Pinching, Is.False, "a detection inside the band restarts the run");
            Assert.That(rig.DownL, Is.EqualTo(0));
        }

        static void OpensOnlyOverTheOpenThreshold()
        {
            var rig = new Rig();
            rig.Detect(Under, Open, HandsConstants.PinchDetections);
            Assert.That(rig.Left.Pinching, Is.True);

            rig.Detect(HandsConstants.PinchClose, Open);
            rig.Detect(Band, Open);
            rig.Detect(HandsConstants.PinchOpen, Open);       // exactly the open threshold is not over it
            Assert.That(rig.Left.Pinching, Is.True, "the pinch stays closed through the band and at the open threshold");
            Assert.That(rig.UpL, Is.EqualTo(0));

            rig.L.PinchRatio = HandsConstants.PinchOpen + 0.01f;
            rig.Step(true);
            PinchState p = rig.Left;
            Assert.That(p.Pinching, Is.False, "over the open threshold opens it in that frame");
            Assert.That(p.Up, Is.True, "Up on the opening frame");
            Assert.That(p.Down, Is.False);
            Assert.That(p.Amount, Is.EqualTo(0f));
            rig.Step(false);
            Assert.That(rig.Left.Up, Is.False, "Up lasts one frame");
            Assert.That(rig.UpL, Is.EqualTo(1));
        }

        static void AmountRunsLinearlyFromOpenToClose()
        {
            var rig = new Rig();
            float open = HandsConstants.PinchOpen, close = HandsConstants.PinchClose;
            float[] ratios = { 0.9f, open + 0.001f, open, 0.5f * (open + close), close, close - 0.1f, 0f };
            float[] expected = { 0f, 0f, 0f, 0.5f, 1f, 1f, 1f };
            for (int i = 0; i < ratios.Length; i++)
            {
                rig.L.PinchRatio = ratios[i];
                rig.Step(false);
                Assert.That(rig.Left.Amount, Is.EqualTo(expected[i]).Within(1e-5f), "ratio " + ratios[i]);
            }
            rig.L.PinchRatio = open - 0.25f * (open - close);
            rig.Step(false);
            Assert.That(rig.Left.Amount, Is.EqualTo(0.25f).Within(1e-5f), "linear between the two thresholds");
        }

        static void NoChatterOnANoisyRamp()
        {
            int singleThresholdCrossings = 0;
            for (int seed = 1; seed <= 25; seed++)
            {
                var rig = new Rig();
                uint state = (uint)seed * 2654435761u + 12345u;
                bool previous = false;
                for (int i = 0; i < 160; i++)
                {
                    // 0.70 down to 0.05, held, then back up: slowly, with noise of 0.04 each way.
                    float ramp = i < 60 ? math.lerp(0.70f, 0.05f, i / 59f)
                               : i < 100 ? 0.05f
                               : math.lerp(0.05f, 0.70f, (i - 100) / 59f);
                    state = state * 1664525u + 1013904223u;
                    float noise = ((state >> 8) / (float)(1 << 24) - 0.5f) * 0.08f;
                    float ratio = ramp + noise;
                    bool single = ratio < Band;
                    if (i > 0 && single != previous) singleThresholdCrossings++;
                    previous = single;
                    rig.Detect(ratio, Open);
                }
                Assert.That(rig.DownL, Is.EqualTo(1), "one Down on the way in, seed " + seed);
                Assert.That(rig.UpL, Is.EqualTo(1), "one Up on the way out, seed " + seed);
                Assert.That(rig.Left.Pinching, Is.False);
            }
            Assert.That(singleThresholdCrossings, Is.GreaterThan(25 * 3),
                "the noise must be enough to make one threshold chatter, or the test proves nothing");
        }

        static void HoveringFiresNothing()
        {
            var rig = new Rig();
            rig.LeftHover = true;
            rig.Detect(Under, Open, 5);
            Assert.That(rig.Left.Pinching, Is.True, "Pinching follows the fingers; only Down and Up are held back");
            Assert.That(rig.DownL, Is.EqualTo(0), "a hand hovering a target does not pinch: touch wins");

            rig.LeftHover = false;
            rig.Detect(Under, Open, 10);
            Assert.That(rig.DownL, Is.EqualTo(0), "no late Down when the hover ends with the fingers still closed");

            rig.Detect(Open, Open);
            Assert.That(rig.UpL, Is.EqualTo(0), "a pinch whose Down never fired has no Up");
            Assert.That(rig.Left.Pinching, Is.False);

            rig.Detect(Under, Open, HandsConstants.PinchDetections);
            Assert.That(rig.DownL, Is.EqualTo(1), "the same pinch with nothing hovered fires");
        }

        static void TheFirstMomentsOfAHandFireNothing()
        {
            var rig = new Rig();
            rig.L.TrackedSeconds = 0f;
            rig.Detect(Under, Open, 6);                        // about 0.2 s of TrackedSeconds
            Assert.That(rig.L.TrackedSeconds, Is.LessThan(HandsConstants.PinchQuietAfterEnterSeconds));
            Assert.That(rig.DownL, Is.EqualTo(0), "no pinch in the first PinchQuietAfterEnterSeconds");
            rig.Detect(Under, Open, 20);                       // well past the quiet time, still closed
            Assert.That(rig.L.TrackedSeconds, Is.GreaterThan(HandsConstants.PinchQuietAfterEnterSeconds));
            Assert.That(rig.DownL, Is.EqualTo(0), "no late Down when the quiet time ends with the fingers still closed");
            rig.Detect(Open, Open);
            rig.Detect(Under, Open, HandsConstants.PinchDetections);
            Assert.That(rig.DownL, Is.EqualTo(1), "after the quiet time a pinch fires");
        }

        static void AFastHandFiresNothing()
        {
            var rig = new Rig();
            float limit = HandsConstants.PinchMaxHandSpeed;
            rig.L.PalmVelocity = new float3(0.6f * limit * 1.2f, 0f, 0.8f * limit * 1.2f);     // 1.2 times the limit, sideways on
            rig.Detect(Under, Open, 5);
            Assert.That(rig.DownL, Is.EqualTo(0), "above PinchMaxHandSpeed fires nothing");
            rig.L.PalmVelocity = float3.zero;
            rig.Detect(Under, Open, 10);
            Assert.That(rig.DownL, Is.EqualTo(0), "no late Down when the hand slows with the fingers still closed");

            rig.Detect(Open, Open);
            rig.L.PalmVelocity = new float3(0f, 0f, 0.8f * limit);
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1), "below the limit it fires");

            // Exactly at the limit is not above it.
            rig.Detect(Open, Open, 15);
            rig.L.PalmVelocity = new float3(0f, 0f, limit);
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(2), "at the limit it fires");

            // Speeding up while pinching does not let go of it.
            int ups = rig.UpL;
            rig.L.PalmVelocity = new float3(0f, 0f, 3f * limit);
            rig.Detect(Under, Open, 10);
            Assert.That(rig.Left.Pinching, Is.True);
            Assert.That(rig.UpL, Is.EqualTo(ups), "no Up from a hand that merely speeds up");
            Assert.That(rig.DownL, Is.EqualTo(2));
        }

        static void ASecondPinchTooSoonFiresNothing()
        {
            // The same hand: a re-closing within PinchRefractorySeconds of its last Down.
            var rig = new Rig();
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1));
            double first = rig.LastDownL;
            rig.Detect(Open, Open, 5);
            rig.Detect(Under, Open, 3);
            Assert.That(rig.Now - first, Is.LessThan(HandsConstants.PinchRefractorySeconds));
            Assert.That(rig.DownL, Is.EqualTo(1), "a pinch within PinchRefractorySeconds of the last Down fires nothing");
            Assert.That(rig.Left.Pinching, Is.True);
            rig.Detect(Under, Open, 20);
            Assert.That(rig.DownL, Is.EqualTo(1), "no late Down when the refractory time ends with the fingers still closed");
            rig.Detect(Open, Open);
            Assert.That(rig.UpL, Is.EqualTo(1), "only the pinch that fired has an Up");

            // Just inside and just outside the time, measured from the last Down.
            foreach (bool outside in new[] { false, true })
            {
                rig = new Rig();
                rig.Detect(Under, Open, 3);
                first = rig.LastDownL;
                rig.Detect(Open, Open, outside ? 10 : 8);
                rig.Detect(Under, Open, 3);
                double gap = rig.Now - first;
                if (outside)
                {
                    Assert.That(gap, Is.GreaterThan(HandsConstants.PinchRefractorySeconds));
                    Assert.That(rig.DownL, Is.EqualTo(2), "after the refractory time a pinch fires");
                }
                else
                {
                    Assert.That(gap, Is.LessThan(HandsConstants.PinchRefractorySeconds));
                    Assert.That(rig.DownL, Is.EqualTo(1));
                }
            }

            // The other hand has its own refractory time.
            rig = new Rig();
            rig.Detect(Under, Open, 3);
            rig.Detect(Open, Open, 2);
            rig.Detect(Open, Under, 3);
            Assert.That(rig.DownL, Is.EqualTo(1));
            Assert.That(rig.DownR, Is.EqualTo(1), "a Down of the left hand does not hold back the right");
        }

        static void ALowConfidenceFiresNothing()
        {
            var rig = new Rig();
            rig.L.Confidence = HandsConstants.InteractConfidence - 0.01f;
            rig.Detect(Under, Open, 5);
            Assert.That(rig.DownL, Is.EqualTo(0), "below InteractConfidence fires nothing");
            rig.L.Confidence = HandsConstants.InteractConfidence;
            rig.Detect(Under, Open, 10);
            Assert.That(rig.DownL, Is.EqualTo(0), "no late Down when the confidence recovers with the fingers still closed");
            rig.Detect(Open, Open);
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1), "at InteractConfidence itself it fires: only below is held back");
        }

        // ---- Other behaviour of the pinch ----

        [Test]
        public void Hands_Pinch_SecondHandOfATwoHandPinchFiresNothing()
        {
            // The first hand's pinch has fired its Down when the second closes; the second fires nothing (section 6.4).
            var rig = new Rig();
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1));
            rig.Detect(Under, Under, 3);
            Assert.That(rig.Right.Pinching, Is.True);
            Assert.That(rig.DownR, Is.EqualTo(0), "the second hand of a two-hand pinch fires nothing");
            Assert.That(rig.Started, Is.EqualTo(1));
            Assert.That(rig.DownL, Is.EqualTo(1), "and the first does not fire again");
            rig.Detect(Under, Open);
            Assert.That(rig.UpR, Is.EqualTo(0), "no Up for a pinch that fired no Down");
            Assert.That(rig.Ended, Is.EqualTo(1));

            // Two hands closing on the very same detection: nobody is first, so neither fires.
            rig = new Rig();
            rig.Detect(Under, Under, 3);
            Assert.That(rig.Left.Pinching && rig.Right.Pinching, Is.True);
            Assert.That(rig.DownL + rig.DownR, Is.EqualTo(0), "a simultaneous two-hand pinch fires nothing");
            Assert.That(rig.Started, Is.EqualTo(1));

            // With the first hand open again, the second is a lone pinch.
            rig = new Rig();
            rig.Detect(Under, Open, 3);
            rig.Detect(Open, Open, 2);
            rig.Detect(Open, Under, 3);
            Assert.That(rig.DownR, Is.EqualTo(1));
        }

        [Test]
        public void Hands_Pinch_PointIsTheMidpointOfThumbTipAndIndexTip()
        {
            var rig = new Rig();
            rig.L.World[HandJoint.ThumbTip] = new float3(0.10f, 0.20f, 0.40f);
            rig.L.World[HandJoint.IndexTip] = new float3(0.14f, 0.24f, 0.50f);
            rig.Detect(Under, Open, 3);
            float3 p = rig.Left.Point;
            Assert.That(p.x, Is.EqualTo(0.12f).Within(1e-6f));
            Assert.That(p.y, Is.EqualTo(0.22f).Within(1e-6f));
            Assert.That(p.z, Is.EqualTo(0.45f).Within(1e-6f));
            Assert.That(rig.Right.Point.y, Is.EqualTo(0f).Within(1e-6f), "the right hand reports its own points");
        }

        [Test]
        public void Hands_Pinch_ALostHandLetsGo()
        {
            var rig = new Rig();
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1));

            // Still tracked but not detected: the pose is held, so the pinch is held.
            for (int i = 0; i < 20; i++) rig.Step(false);
            Assert.That(rig.Left.Pinching, Is.True);
            Assert.That(rig.UpL, Is.EqualTo(0));

            rig.L.Tracked = false;
            rig.Step(false);
            Assert.That(rig.Left.Pinching, Is.False, "a hand that is no longer tracked is not pinching");
            Assert.That(rig.Left.Up, Is.True, "and its pinch ends with an Up");
            Assert.That(rig.Left.Amount, Is.EqualTo(0f));
            rig.Step(false);
            Assert.That(rig.Left.Up, Is.False);

            // It comes back with the fingers still closed: it needs fresh detections again.
            rig.L.Tracked = true;
            rig.L.TrackedSeconds = 10f;
            rig.Detect(Under, Open, HandsConstants.PinchDetections - 1);
            Assert.That(rig.Left.Pinching, Is.False);
            rig.Detect(Under, Open, 1);
            Assert.That(rig.Left.Pinching, Is.True);
        }

        [Test]
        public void Hands_Pinch_ResetForgetsEverything()
        {
            var rig = new Rig();
            rig.Detect(Under, Under, 3);
            Assert.That(rig.G.TwoHand.Active, Is.True);
            rig.G.Reset();
            Assert.That(rig.Left.Pinching, Is.False);
            Assert.That(rig.Left.Amount, Is.EqualTo(0f));
            Assert.That(rig.G.TwoHand.Active, Is.False);
            Assert.That(rig.G.TwoHandStarted, Is.False);

            // A reset also forgets the refractory time: a Down is allowed at once.
            rig = new Rig();
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(1));
            rig.Detect(Open, Open);
            rig.G.Reset();
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(2));
        }

        [Test]
        public void Hands_Pinch_SurvivesGarbageInput()
        {
            var rig = new Rig();
            rig.L.PinchRatio = float.NaN;
            rig.Detect();
            Assert.That(rig.Left.Pinching, Is.False);
            Assert.That(rig.Left.Amount, Is.EqualTo(0f), "a ratio that is not a number reads as an open hand");
            rig.Detect(Under, Open, 3);
            Assert.That(rig.Left.Pinching, Is.True);
            rig.L.PinchRatio = float.NaN;
            rig.Detect();
            Assert.That(rig.Left.Pinching, Is.False, "and it opens a closed one");

            rig = new Rig();
            rig.L.PalmVelocity = new float3(float.NaN, 0f, 0f);
            rig.Detect(Under, Open, 3);
            Assert.That(rig.DownL, Is.EqualTo(0), "an unreadable speed does not fire");

            // A joint that is not a number leaves the point where it was, and the two-hand pinch finite.
            rig = new Rig();
            rig.Detect(Under, Under, 3);
            float3 before = rig.Left.Point;
            rig.L.World[HandJoint.IndexTip] = new float3(float.NaN, 0f, 0.45f);
            rig.Step(true);
            Assert.That(rig.Left.Point.x, Is.EqualTo(before.x));
            Assert.That(float.IsNaN(rig.G.TwoHand.Distance), Is.False);
            Assert.That(float.IsNaN(rig.G.TwoHand.Angle), Is.False);
        }

        // ---- Hands_TwoHand_AngleUnwraps ----

        static float3 LineEnd(float3 centre, float length, quaternion head, float angle, float sign)
        {
            // The line direction: the head's right axis turned about the head's forward axis by angle.
            quaternion turn = math.mul(head, quaternion.AxisAngle(new float3(0f, 0f, 1f), angle));
            return centre + sign * 0.5f * length * math.mul(turn, new float3(1f, 0f, 0f));
        }

        static void Turn(Rig rig, float3 centre, float length, float angle)
        {
            rig.Place(LineEnd(centre, length, rig.Head, angle, -1f), LineEnd(centre, length, rig.Head, angle, 1f));
        }

        [Test]
        public void Hands_TwoHand_AngleUnwraps()
        {
            var rig = new Rig();
            float3 centre = new float3(0f, -0.05f, 0.45f);
            Turn(rig, centre, 0.20f, 0f);
            rig.Detect(Under, Under, 3);
            Assert.That(rig.G.TwoHand.Active, Is.True);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(0f).Within(1e-4f), "the right hand level with the left reads 0");

            // One full turn, a degree a frame, and then two more: no step anywhere, and 2 pi after the first.
            float step = math.radians(1f);
            float previous = rig.G.TwoHand.Angle;
            for (int i = 1; i <= 1080; i++)
            {
                Turn(rig, centre, 0.20f, i * step);
                rig.Step(i % 2 == 0);
                float angle = rig.G.TwoHand.Angle;
                Assert.That(rig.G.TwoHand.Active, Is.True, "frame " + i);
                Assert.That(angle - previous, Is.EqualTo(step).Within(1e-4f), "no step at frame " + i);
                previous = angle;
                if (i == 360) Assert.That(angle, Is.EqualTo(2f * math.PI).Within(1e-3f), "a full turn reads 2 pi");
            }
            Assert.That(previous, Is.EqualTo(6f * math.PI).Within(2e-3f), "three turns read 6 pi");

            // And the other way, from a fresh start.
            rig.Detect(Open, Open);
            Assert.That(rig.Ended, Is.EqualTo(1));
            Turn(rig, centre, 0.20f, 0f);
            rig.Detect(Under, Under, 3);
            Assert.That(rig.Started, Is.EqualTo(2));
            for (int i = 1; i <= 720; i++)
            {
                Turn(rig, centre, 0.20f, -i * step);
                rig.Step(i % 2 == 0);
                if (i == 360) Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(-2f * math.PI).Within(1e-3f), "a full turn the other way reads -2 pi");
            }
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(-4f * math.PI).Within(2e-3f));
        }

        [Test]
        public void Hands_TwoHand_AngleStartsWhereTheLineIsAndContinuesPastPi()
        {
            var rig = new Rig();
            float3 centre = new float3(0f, 0f, 0.45f);
            float start = 3.0f;                                // a little short of pi
            Turn(rig, centre, 0.20f, start);
            rig.Detect(Under, Under, 3);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(start).Within(1e-4f));
            Turn(rig, centre, 0.20f, start + 0.5f);            // across the branch cut
            rig.Step(true);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(start + 0.5f).Within(1e-4f), "it goes on past pi and does not wrap to -pi");
            Turn(rig, centre, 0.20f, start - 0.5f);            // and back across it
            rig.Step(true);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(start - 0.5f).Within(1e-4f));
        }

        [Test]
        public void Hands_TwoHand_AngleIsAboutTheHeadsForwardAxis()
        {
            quaternion head = math.mul(quaternion.AxisAngle(new float3(0f, 1f, 0f), math.radians(35f)),
                                       math.mul(quaternion.AxisAngle(new float3(1f, 0f, 0f), math.radians(-20f)),
                                                quaternion.AxisAngle(new float3(0f, 0f, 1f), math.radians(12f))));
            var rig = new Rig { Head = head };
            float3 centre = new float3(0.1f, 0.2f, 0.5f);
            foreach (float angle in new[] { 0f, 0.7f, -1.2f, 2.5f })
            {
                rig.G.Reset();
                rig.Detect(Open, Open);
                Turn(rig, centre, 0.25f, angle);
                rig.Detect(Under, Under, 3);
                Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(angle).Within(1e-4f), "the line turned by " + angle + " about the head's forward axis");
            }

            // Rolling the head one way turns the line the other way, as the head sees it.
            var level = new Rig { Head = quaternion.AxisAngle(new float3(0f, 0f, 1f), math.radians(30f)) };
            level.Place(new float3(-0.1f, 0f, 0.45f), new float3(0.1f, 0f, 0.45f));
            level.Detect(Under, Under, 3);
            Assert.That(level.G.TwoHand.Angle, Is.EqualTo(-math.radians(30f)).Within(1e-4f));
        }

        [Test]
        public void Hands_TwoHand_ReportsDistanceMidpointAndTheChangesAtTheEnd()
        {
            var rig = new Rig();
            Assert.That(rig.G.TwoHand.Active, Is.False);
            Assert.That(rig.G.TwoHandStarted, Is.False);
            Assert.That(rig.G.TwoHandEnded, Is.False);

            float3 centre = new float3(0.05f, 0.1f, 0.45f);
            Turn(rig, centre, 0.20f, 0.3f);
            rig.Detect(Under, Open, 3);                        // one hand only
            Assert.That(rig.G.TwoHand.Active, Is.False, "one pinching hand is not a two-hand pinch");
            rig.Detect(Under, Under, 3);
            Assert.That(rig.Started, Is.EqualTo(1));
            TwoHandPinch two = rig.G.TwoHand;
            Assert.That(two.Active, Is.True);
            Assert.That(two.Distance, Is.EqualTo(0.20f).Within(1e-5f));
            Assert.That(two.Midpoint.x, Is.EqualTo(centre.x).Within(1e-5f));
            Assert.That(two.Midpoint.y, Is.EqualTo(centre.y).Within(1e-5f));
            Assert.That(two.Midpoint.z, Is.EqualTo(centre.z).Within(1e-5f));
            Assert.That(two.Angle, Is.EqualTo(0.3f).Within(1e-4f));

            // Spread the hands and turn the line; the started flag was a single frame.
            Turn(rig, centre, 0.35f, 0.3f + 1.0f);
            rig.Step(true);
            Assert.That(rig.G.TwoHandStarted, Is.False, "TwoHandStarted lasts one frame");
            Assert.That(rig.G.TwoHand.Distance, Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(rig.G.TwoHandDistanceChange, Is.EqualTo(0f), "the changes are for the frame it ends");

            // One hand opens: the pinch ends and reports what changed between its first frame and its last.
            rig.L.PinchRatio = Open;
            rig.Step(true);
            Assert.That(rig.G.TwoHandEnded, Is.True);
            Assert.That(rig.G.TwoHand.Active, Is.False);
            Assert.That(rig.G.TwoHandDistanceChange, Is.EqualTo(0.15f).Within(1e-5f), "end minus start, in metres");
            Assert.That(rig.G.TwoHandAngleChange, Is.EqualTo(1.0f).Within(1e-4f), "end minus start, in radians");
            rig.Step(false);
            Assert.That(rig.G.TwoHandEnded, Is.False, "TwoHandEnded lasts one frame");
            Assert.That(rig.Ended, Is.EqualTo(1));
        }

        [Test]
        public void Hands_TwoHand_AFullTurnIsReportedAtTheEnd()
        {
            var rig = new Rig();
            float3 centre = new float3(0f, 0f, 0.45f);
            Turn(rig, centre, 0.2f, 0.5f);
            rig.Detect(Under, Under, 3);
            float step = math.radians(2f);
            for (int i = 1; i <= 180; i++)
            {
                Turn(rig, centre, 0.2f, 0.5f + i * step);
                rig.Step(true);
            }
            rig.R.Tracked = false;                              // the right hand is lost: the pinch ends
            rig.Step(true);
            Assert.That(rig.G.TwoHandEnded, Is.True, "losing a hand ends the two-hand pinch");
            Assert.That(rig.G.TwoHandAngleChange, Is.EqualTo(2f * math.PI).Within(1e-3f), "one full turn");
            Assert.That(rig.G.TwoHandDistanceChange, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Hands_TwoHand_CoincidentOrEndOnPointsStaySane()
        {
            var rig = new Rig();
            float3 p = new float3(0f, 0f, 0.45f);
            rig.Place(p - new float3(0.1f, 0f, 0f), p + new float3(0.1f, 0f, 0f));
            rig.Detect(Under, Under, 3);
            float before = rig.G.TwoHand.Angle;

            // Both points on one spot: the line has no direction, so the angle keeps its value.
            rig.Place(p, p);
            rig.Step(true);
            Assert.That(rig.G.TwoHand.Active, Is.True);
            Assert.That(rig.G.TwoHand.Distance, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(float.IsNaN(rig.G.TwoHand.Angle), Is.False);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(before).Within(1e-6f));

            // One hand behind the other along the view axis: also no direction in the view's plane.
            rig.Place(p, p + new float3(0f, 0f, 0.15f));
            rig.Step(true);
            Assert.That(rig.G.TwoHand.Distance, Is.EqualTo(0.15f).Within(1e-5f));
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(before).Within(1e-6f));

            // A direction comes back: the angle follows it without a jump of a whole turn.
            rig.Place(p - new float3(0f, 0.05f, 0f), p + new float3(0f, 0.05f, 0f));
            rig.Step(true);
            Assert.That(rig.G.TwoHand.Angle, Is.EqualTo(0.5f * math.PI).Within(1e-4f));
        }

        // ---- Allocation ----

        [Test]
        public void Hands_Gestures_UpdateAllocatesNothing()
        {
            var gestures = new HandsGestures();
            TrackedHand left = MakeHand(HandSide.Left), right = MakeHand(HandSide.Right);
            quaternion head = quaternion.AxisAngle(new float3(0f, 1f, 0f), 0.3f);
            double now = 50.0;

            // Warm up through every path: closing, two-hand, angle turning, opening, a lost hand.
            Run(gestures, ref now, ref left, ref right, head, 600);

            long before = GC.GetAllocatedBytesForCurrentThread();
            Run(gestures, ref now, ref left, ref right, head, 100);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 100 updates");
        }

        static void Run(HandsGestures gestures, ref double now, ref TrackedHand left, ref TrackedHand right, quaternion head, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                now += Frame;
                int phase = i % 120;
                float ratio = phase < 60 ? 0.1f : 0.8f;
                left.PinchRatio = ratio;
                right.PinchRatio = phase < 90 ? ratio : 0.8f;
                left.Fresh = right.Fresh = (i & 1) == 0;
                left.Tracked = phase != 119;
                left.TrackedSeconds += (float)Frame;
                right.TrackedSeconds += (float)Frame;
                SetPinchPoint(ref left, new float3(-0.1f, 0.01f * i % 0.1f, 0.45f));
                SetPinchPoint(ref right, new float3(0.1f, 0f, 0.45f));
                gestures.Update(now, in left, in right, head, (i & 7) == 0, false);
                float a = gestures.Pinch(HandSide.Left).Amount + gestures.TwoHand.Angle + gestures.TwoHandDistanceChange;
                if (float.IsNaN(a)) throw new InvalidOperationException("not a number");
            }
        }
    }
}
