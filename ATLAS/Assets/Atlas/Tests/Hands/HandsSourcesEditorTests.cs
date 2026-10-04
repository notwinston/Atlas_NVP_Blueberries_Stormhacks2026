using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The Editor's stand-in for the detector: two hands driven by mouse and keys, written through the real camera model.</summary>
    public class HandsSourcesEditorTests
    {
        static readonly float2 Centre = new float2(640f, 360f);
        const double Frame = 1.0 / 60.0;

        /// <summary>A source, a camera it asks for, a clock, and the last result it gave.</summary>
        sealed class Rig
        {
            public readonly EditorHandsSource Source;
            public float4x4 Camera = float4x4.TRS(new float3(0f, 1.6f, 0f), quaternion.identity, new float3(1f));
            public double Time = 100.0;
            public HandsDetection Detection;
            /// <summary>Results given so far.</summary>
            public int Results;

            public Rig(bool enabled = true)
            {
                Source = new EditorHandsSource(() => Camera);
                Source.Enabled = enabled;
            }

            /// <summary>One frame of 1/60 s, with the controls of that frame. True when a result came out.</summary>
            public bool Step(EditorHandsControls controls = default(EditorHandsControls))
            {
                Time += Frame;
                Source.SetControls(in controls);
                bool got = Source.TryRead(Time, ref Detection);
                if (got) Results++;
                return got;
            }

            /// <summary>Frames enough for the detector's 80 ms and the hand's change of shape to have come through.</summary>
            public void Settle(int frames = 30)
            {
                for (int i = 0; i < frames; i++) Step();
            }
        }

        /// <summary>A hand's joints in camera space, from the picture points and the depth under them. False when any joint has no depth.</summary>
        static bool Lift(in HandsDetection d, int hand, out JointSet joints)
        {
            DetectedHand h = hand == 0 ? d.Hand0 : d.Hand1;
            joints = default(JointSet);
            bool all = true;
            for (int j = 0; j < HandJoints.Count; j++)
            {
                JointSample s = h.GetJoint(j);
                if (s.DepthConfidence < 0) { all = false; continue; }
                joints[j] = d.Unproject(s.Pixel, s.Depth);
            }
            return all;
        }

        static HandSide SideOf(in HandsDetection d, int hand)
        {
            return (hand == 0 ? d.Hand0.Side : d.Hand1.Side) == 0 ? HandSide.Left : HandSide.Right;
        }

        [Test]
        public void Hands_EditorSource_DetectionsAreWellFormed()
        {
            var camera = float4x4.TRS(new float3(0.3f, 1.6f, -0.2f), quaternion.RotateY(0.4f), new float3(1f));
            var source = new EditorHandsSource(() => camera);
            HandsDetection d = default(HandsDetection);

            // Off until the developer parameter hands.editor turns it on: the Editor behaves as it always did.
            Assert.That(source.Name, Is.EqualTo("editor"));
            Assert.That(source.Fault, Is.EqualTo(0));
            Assert.That(source.Enabled, Is.False);
            Assert.That(source.Available, Is.False);
            Assert.That(source.TryRead(1.0, ref d), Is.False);
            Assert.That(d.HandCount, Is.EqualTo(0));
            source.Enabled = true;
            Assert.That(source.Available, Is.True, "available in the Editor while enabled (these tests are built for the Editor)");
            source.Enabled = false;
            Assert.That(source.Available, Is.False);
            Assert.That(source.TryRead(2.0, ref d), Is.False);
            source.Enabled = true;

            double time = 10.0;
            int results = 0;
            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            foreach (EditorHandShape shape in new[] { EditorHandShape.Open, EditorHandShape.Pointing, EditorHandShape.Pinch, EditorHandShape.Fist })
            foreach (float distance in new[] { 0.30f, 0.45f, 0.60f })
            {
                HandSide other = side == HandSide.Left ? HandSide.Right : HandSide.Left;
                source.Show(other, false);
                source.Show(side, true);
                source.Place(side, Centre, distance);
                source.SetShape(side, shape, true);
                // The result of a frame is of the hand as it was 80 ms before: run on until the placing has come through.
                for (int i = 0; i < 30; i++)
                {
                    time += Frame;
                    if (source.TryRead(time, ref d)) results++;
                }
                string where = side + " " + shape + " at " + distance + " m";

                Assert.That(d.HandCount, Is.EqualTo(1), where);
                Assert.That(SideOf(in d, 0), Is.EqualTo(side), where);
                Assert.That(d.Version, Is.EqualTo(HandsDetection.CurrentVersion), where);
                Assert.That(d.Sequence, Is.EqualTo(results), "the sequence rises by one a result, " + where);
                Assert.That(d.ImageWidth, Is.EqualTo(1280));
                Assert.That(d.ImageHeight, Is.EqualTo(720));
                Assert.That(d.DepthWidth, Is.EqualTo(256));
                Assert.That(d.DepthHeight, Is.EqualTo(144));
                Assert.That(d.Fx, Is.EqualTo(1000f));
                Assert.That(d.Fy, Is.EqualTo(1000f));
                Assert.That(d.Cx, Is.EqualTo(640f));
                Assert.That(d.Cy, Is.EqualTo(360f));
                Assert.That(d.PollTime, Is.EqualTo(time).Within(1.0 / 30.0 + 1e-9), "polled within a result of now");
                Assert.That(d.PollTime - d.CaptureTime, Is.EqualTo(0.080).Within(1e-9), "as old as the phone's results, " + where);
                Assert.That(d.AgeSeconds, Is.EqualTo(0.080f).Within(1e-6f));
                Assert.That(d.CameraToSession, Is.EqualTo(camera), "the pose of the tracked camera, " + where);

                // A hand at the centre of the view: every joint is in the picture, seen, and where the detector would put it.
                DetectedHand hand = d.Hand0;
                Assert.That(hand.Confidence, Is.GreaterThan(0.9f), where);
                for (int j = 0; j < HandJoints.Count; j++)
                {
                    JointSample s = hand.GetJoint(j);
                    Assert.That(s.Pixel.x, Is.InRange(0f, 1280f), where + " joint " + j);
                    Assert.That(s.Pixel.y, Is.InRange(0f, 720f), where + " joint " + j);
                    Assert.That(s.Confidence, Is.GreaterThan(0.9f), where + " joint " + j);
                }

                bool allDepth = Lift(in d, 0, out JointSet joints);
                for (int j = 1; j < HandJoints.Count; j++)
                {
                    int parent = HandJoints.Parent(j);
                    if (hand.GetJoint(j).DepthConfidence < 0 || hand.GetJoint(parent).DepthConfidence < 0) continue;
                    Assert.That(math.distance(joints[j], joints[parent]), Is.EqualTo(HandModel.BoneLength(j)).Within(5e-4f),
                        "bone " + j + ", " + where);
                }
                if (distance >= 0.45f)
                {
                    Assert.That(allDepth, Is.True, where);
                    Assert.That(hand.PalmCount, Is.GreaterThan(20), "LiDAR sees the palm, " + where);
                    float3 palm = EditorHandShapes.PalmCentre(in joints);
                    Assert.That(palm.z, Is.EqualTo(distance).Within(2e-3f), where);
                    float2 pixel = d.Project(palm);
                    Assert.That(pixel.x, Is.EqualTo(640f).Within(0.5f), where);
                    Assert.That(pixel.y, Is.EqualTo(360f).Within(0.5f), where);
                    for (int i = 0; i < hand.PalmCount; i++)
                    {
                        float3 sample = hand.GetPalmSample(i);
                        Assert.That(sample.x, Is.InRange(0f, 1280f));
                        Assert.That(sample.y, Is.InRange(0f, 720f));
                        Assert.That(sample.z, Is.InRange(distance - 0.1f, distance + 0.1f));
                    }
                }
            }
        }

        [Test]
        public void Hands_EditorSource_ShapesAreOpenPointingPinchAndFist()
        {
            var rig = new Rig();
            var source = rig.Source;
            source.Show(HandSide.Left, false);
            source.Place(HandSide.Right, Centre, 0.45f);
            float Gap(in JointSet j) { return math.distance(j[HandJoint.ThumbTip], j[HandJoint.IndexTip]); }
            float Reach(in JointSet j, HandJoint tip) { return math.distance(j[tip], j[HandJoint.Wrist]); }

            source.SetShape(HandSide.Right, EditorHandShape.Open, true);
            rig.Settle();
            Assert.That(Lift(in rig.Detection, 0, out JointSet open), Is.True);
            Assert.That(Gap(in open) / HandModel.PalmWidth, Is.GreaterThan(HandsConstants.PinchOpen * 2f), "an open hand's thumb is far from its index");
            Assert.That(Reach(in open, HandJoint.MiddleTip), Is.GreaterThan(0.17f));

            source.SetShape(HandSide.Right, EditorHandShape.Pointing, true);
            rig.Settle();
            Assert.That(Lift(in rig.Detection, 0, out JointSet pointing), Is.True);
            Assert.That(Reach(in pointing, HandJoint.IndexTip), Is.GreaterThan(Reach(in pointing, HandJoint.MiddleTip) + 0.05f), "the index is out, the others are curled");
            Assert.That(Reach(in pointing, HandJoint.IndexTip), Is.GreaterThan(Reach(in pointing, HandJoint.RingTip) + 0.05f));
            Assert.That(Reach(in pointing, HandJoint.IndexTip), Is.GreaterThan(Reach(in pointing, HandJoint.LittleTip) + 0.05f));

            source.SetShape(HandSide.Right, EditorHandShape.Pinch, true);
            rig.Settle();
            Assert.That(Lift(in rig.Detection, 0, out JointSet pinch), Is.True);
            Assert.That(Gap(in pinch), Is.LessThan(1e-3f), "thumb tip and index tip touch");
            Assert.That(Gap(in pinch) / HandModel.PalmWidth, Is.LessThan(HandsConstants.PinchClose), "which the design calls closed");

            source.SetShape(HandSide.Right, EditorHandShape.Fist, true);
            rig.Settle();
            Assert.That(Lift(in rig.Detection, 0, out JointSet fist), Is.True);
            foreach (HandJoint tip in new[] { HandJoint.IndexTip, HandJoint.MiddleTip, HandJoint.RingTip, HandJoint.LittleTip })
                Assert.That(Reach(in fist, tip), Is.LessThan(0.12f), tip + " is folded in");
        }

        [Test]
        public void Hands_EditorSource_ShapesChangeOverAFewFrames_NotInOne()
        {
            var rig = new Rig();
            rig.Source.Show(HandSide.Left, false);
            rig.Source.Place(HandSide.Right, Centre, 0.45f);
            rig.Settle();
            float openReach = 0f;
            Lift(in rig.Detection, 0, out JointSet open);
            openReach = math.distance(open[HandJoint.IndexTip], open[HandJoint.Wrist]);

            // The key is pressed once; the hand closes over about 0.15 s. The result of a frame shows it 80 ms later.
            rig.Step(new EditorHandsControls { ShapeKey = 4 });
            Assert.That(rig.Source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Fist));
            var reaches = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 40; i++)
                if (rig.Step() && Lift(in rig.Detection, 0, out JointSet j)) reaches.Add(math.distance(j[HandJoint.IndexTip], j[HandJoint.Wrist]));
            Assert.That(reaches[0], Is.GreaterThan(openReach - 0.02f), "the first result after the key is still of the open hand");
            Assert.That(reaches[reaches.Count - 1], Is.LessThan(0.12f), "and the last is a fist");
            bool between = false;
            foreach (float r in reaches) if (r < openReach - 0.03f && r > 0.14f) between = true;
            Assert.That(between, Is.True, "with hands in between: the fingers close over several frames");
            for (int i = 1; i < reaches.Count; i++) Assert.That(reaches[i], Is.LessThanOrEqualTo(reaches[i - 1] + 1e-4f), "and never reopen on the way");
        }

        [Test]
        public void Hands_EditorSource_BindingsDriveTheActiveHand()
        {
            var rig = new Rig();
            var source = rig.Source;
            Assert.That(source.ActiveSide, Is.EqualTo(HandSide.Right), "the right hand is active at first");
            Assert.That(source.IsShown(HandSide.Left), Is.True);
            Assert.That(source.IsShown(HandSide.Right), Is.True);
            Assert.That(source.ShapeOf(HandSide.Left), Is.EqualTo(EditorHandShape.Open));
            float2 leftStart = source.PalmPixel(HandSide.Left);
            float2 rightStart = source.PalmPixel(HandSide.Right);
            Assert.That(leftStart.x, Is.LessThan(Centre.x));
            Assert.That(rightStart.x, Is.GreaterThan(Centre.x));
            float distanceStart = source.PalmDistance(HandSide.Right);
            Assert.That(distanceStart, Is.EqualTo(0.45f));

            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(2), "two hands in front of the camera");
            Assert.That(SideOf(in rig.Detection, 0), Is.EqualTo(HandSide.Left));
            Assert.That(SideOf(in rig.Detection, 1), Is.EqualTo(HandSide.Right));

            // The mouse moves nothing unless Left Alt is held.
            rig.Step(new EditorHandsControls { MouseDelta = new float2(50f, 20f), Wheel = 1f });
            Assert.That(source.PalmPixel(HandSide.Right), Is.EqualTo(rightStart));
            Assert.That(source.PalmDistance(HandSide.Right), Is.EqualTo(distanceStart));

            // Held, it moves the active hand across the view: right and up on the screen is right and up in the picture.
            rig.Step(new EditorHandsControls { Hold = true, MouseDelta = new float2(50f, 20f) });
            float2 moved = source.PalmPixel(HandSide.Right);
            Assert.That(moved.x, Is.EqualTo(rightStart.x + 50f * EditorHandsSource.PicturePixelsPerMousePixel).Within(1e-3f));
            Assert.That(moved.y, Is.EqualTo(rightStart.y - 20f * EditorHandsSource.PicturePixelsPerMousePixel).Within(1e-3f));
            Assert.That(source.PalmPixel(HandSide.Left), Is.EqualTo(leftStart), "the other hand stays");

            // The wheel, held, sets the distance: up is away, down is nearer, within 0.25 to 0.70 m.
            rig.Step(new EditorHandsControls { Hold = true, Wheel = 120f });
            Assert.That(source.PalmDistance(HandSide.Right), Is.EqualTo(distanceStart + EditorHandsSource.WheelStepMetres).Within(1e-5f));
            rig.Step(new EditorHandsControls { Hold = true, Wheel = -0.5f });
            rig.Step(new EditorHandsControls { Hold = true, Wheel = -0.5f });
            Assert.That(source.PalmDistance(HandSide.Right), Is.EqualTo(distanceStart - EditorHandsSource.WheelStepMetres).Within(1e-5f), "any wheel, whatever its units, is a step");
            for (int i = 0; i < 100; i++) rig.Step(new EditorHandsControls { Hold = true, Wheel = 1f });
            Assert.That(source.PalmDistance(HandSide.Right), Is.EqualTo(EditorHandsSource.MaxDistanceMetres));
            for (int i = 0; i < 100; i++) rig.Step(new EditorHandsControls { Hold = true, Wheel = -1f });
            Assert.That(source.PalmDistance(HandSide.Right), Is.EqualTo(EditorHandsSource.MinDistanceMetres));
            Assert.That(EditorHandsSource.MinDistanceMetres, Is.EqualTo(0.25f));
            Assert.That(EditorHandsSource.MaxDistanceMetres, Is.EqualTo(0.70f));

            // Q and E choose the hand.
            rig.Step(new EditorHandsControls { ChooseLeft = true });
            Assert.That(source.ActiveSide, Is.EqualTo(HandSide.Left));
            rig.Step(new EditorHandsControls { Hold = true, MouseDelta = new float2(-30f, 0f) });
            Assert.That(source.PalmPixel(HandSide.Left).x, Is.EqualTo(leftStart.x - 30f).Within(1e-3f));
            Assert.That(source.PalmPixel(HandSide.Right).y, Is.EqualTo(rightStart.y - 20f).Within(1e-3f), "the right one stayed where it was left");
            rig.Step(new EditorHandsControls { ChooseRight = true });
            Assert.That(source.ActiveSide, Is.EqualTo(HandSide.Right));

            // 1 to 4 choose the active hand's shape, and nothing else does.
            rig.Step(new EditorHandsControls { ShapeKey = 3 });
            Assert.That(source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Pinch));
            Assert.That(source.ShapeOf(HandSide.Left), Is.EqualTo(EditorHandShape.Open));
            rig.Step(new EditorHandsControls { ShapeKey = 2 });
            Assert.That(source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Pointing));
            rig.Step(new EditorHandsControls { ShapeKey = 4 });
            Assert.That(source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Fist));
            rig.Step(new EditorHandsControls { ShapeKey = 1 });
            Assert.That(source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Open));
            rig.Step(new EditorHandsControls { ShapeKey = 0 });
            rig.Step(new EditorHandsControls { ShapeKey = 5 });
            Assert.That(source.ShapeOf(HandSide.Right), Is.EqualTo(EditorHandShape.Open));

            // R shows and hides the active hand.
            rig.Step(new EditorHandsControls { ToggleActive = true });
            Assert.That(source.IsShown(HandSide.Right), Is.False);
            Assert.That(source.IsShown(HandSide.Left), Is.True);
            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(1));
            Assert.That(SideOf(in rig.Detection, 0), Is.EqualTo(HandSide.Left));
            rig.Step(new EditorHandsControls { ChooseLeft = true, ToggleActive = true });
            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(0), "both hidden: results still come, with no hand");
            Assert.That(rig.Results, Is.GreaterThan(0));
            rig.Step(new EditorHandsControls { ToggleActive = true });
            rig.Step(new EditorHandsControls { ChooseRight = true, ToggleActive = true });
            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(2));
        }

        static void AddKey(System.Collections.Generic.HashSet<string> keys, string path)
        {
            const string prefix = "<Keyboard>/";
            if (path != null && path.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(path.Substring(prefix.Length));
        }

        [Test]
        public void Hands_EditorSource_BindingsAreOnKeysTheAppLeavesFree()
        {
            // The keys the app already reads in the Editor: every key of the twin table (read from the table, so a
            // key added to it later is checked too) and the walker's (AtlasActionMap, KeyboardWalkerInput). The
            // hands' own are Left Alt, Q, E, R and 1 to 4. The mouse: no button is used (the left one is the lever,
            // the right one turns the head); the movement and the wheel count only while Left Alt is held.
            var taken = new System.Collections.Generic.HashSet<string> { "w", "a", "s", "d", "leftShift", "leftCtrl" };
            for (int i = 1; i <= Atlas.Input.InputTwins.ActionCount; i++)
            {
                ref readonly Atlas.Input.ActionTwin twin = ref Atlas.Input.InputTwins.Of((Atlas.Input.AtlasAction)i);
                AddKey(taken, twin.KeyPath);
                AddKey(taken, twin.KeyAltPath);
            }
            for (int i = 0; i < Atlas.Input.InputTwins.AxisCount; i++)
            {
                ref readonly Atlas.Input.AxisTwin twin = ref Atlas.Input.InputTwins.Of((Atlas.Input.AtlasAxis)i);
                AddKey(taken, twin.KeyPositivePath);
                AddKey(taken, twin.KeyNegativePath);
            }
            Assert.That(taken.Count, Is.GreaterThan(25), "the table was read");
            Assert.That(taken, Does.Contain("enter"));

            foreach (string key in new[] { "leftAlt", "q", "e", "r", "1", "2", "3", "4" })
                Assert.That(taken, Does.Not.Contain(key), "the hands read <Keyboard>/" + key + ", which the app uses already");
        }

        [Test]
        public void Hands_EditorSource_HonoursTheRate()
        {
            var rig = new Rig();
            int Count(int frames)
            {
                int n = 0;
                for (int i = 0; i < frames; i++) if (rig.Step()) n++;
                return n;
            }

            Assert.That(Count(120), Is.EqualTo(60), "30 Hz by default, on a 60 Hz frame");
            rig.Source.SetRate(HandsConstants.RateReduced);
            Assert.That(Count(120), Is.InRange(29, 31), "15 Hz");
            rig.Source.SetRate(HandsConstants.RateIdle);
            Assert.That(Count(120), Is.InRange(9, 11), "5 Hz");
            rig.Source.SetRate(0);
            Assert.That(Count(120), Is.EqualTo(0), "idle: nothing");
            rig.Source.SetRate(30);
            Assert.That(Count(120), Is.EqualTo(60), "running again");

            // A frame slower than the rate never gives two results.
            int slow = 0;
            for (int i = 0; i < 20; i++)
            {
                rig.Time += 0.1;
                if (rig.Source.TryRead(rig.Time, ref rig.Detection)) slow++;
            }
            Assert.That(slow, Is.EqualTo(20), "one result a frame, 10 a second");

            // Stopped, nothing; asked for a rate, it starts again.
            rig.Source.Stop();
            Assert.That(Count(30), Is.EqualTo(0));
            rig.Source.SetRate(15);
            Assert.That(Count(60), Is.InRange(14, 16));

            int before = rig.Detection.Sequence;
            Count(10);
            Assert.That(rig.Detection.Sequence, Is.GreaterThan(before), "the sequence only rises, across idling and stopping");
        }

        [Test]
        public void Hands_EditorSource_ReportsTheHandAsItWas80MillisecondsAgo()
        {
            var rig = new Rig();
            rig.Source.Show(HandSide.Left, false);
            var a = new float2(400f, 360f);
            var b = new float2(900f, 360f);
            rig.Source.Place(HandSide.Right, a, 0.45f);
            rig.Settle();

            float PalmPixelX()
            {
                Lift(in rig.Detection, 0, out JointSet j);
                return rig.Detection.Project(EditorHandShapes.PalmCentre(in j)).x;
            }
            Assert.That(PalmPixelX(), Is.EqualTo(a.x).Within(0.5f));

            // The hand jumps at t0. A result polled at t shows the frame captured at t - 0.08.
            rig.Source.Place(HandSide.Right, b, 0.45f);
            double t0 = rig.Time + Frame;
            int seenOld = 0, seenNew = 0;
            for (int i = 0; i < 30; i++)
            {
                if (!rig.Step()) continue;
                double since = rig.Time - t0;
                float x = PalmPixelX();
                if (since <= 0.06) { Assert.That(x, Is.EqualTo(a.x).Within(0.5f), "still the old hand " + since + " s after"); seenOld++; }
                else if (since >= 0.10) { Assert.That(x, Is.EqualTo(b.x).Within(0.5f), "the new hand " + since + " s after"); seenNew++; }
            }
            Assert.That(seenOld, Is.GreaterThan(0));
            Assert.That(seenNew, Is.GreaterThan(3));
        }

        [Test]
        public void Hands_EditorSource_AsksForTheCameraAtEachFrame_AndGivesTheOneOfTheCapture()
        {
            var rig = new Rig();
            double start = rig.Time;
            // The camera walks along x at 10 m/s so that every frame's pose is told apart.
            for (int i = 0; i < 60; i++)
            {
                rig.Camera = float4x4.TRS(new float3((float)(10.0 * (rig.Time + Frame - start)), 1.6f, 0f), quaternion.identity, new float3(1f));
                if (!rig.Step() || i < 10) continue;
                double capture = rig.Time - 0.080 - start;
                float x = rig.Detection.CameraToSession.c3.x;
                Assert.That(x, Is.LessThanOrEqualTo((float)(10.0 * capture) + 1e-3f), "not a pose from after the capture");
                Assert.That(x, Is.GreaterThan((float)(10.0 * (capture - Frame)) - 1e-3f), "and not older than one frame before it");
            }
        }

        [Test]
        public void Hands_EditorSource_ADetectorDoesNotReportAHandItCannotSee()
        {
            var rig = new Rig();
            var source = rig.Source;
            source.Show(HandSide.Left, false);

            // Far out of the picture: no hand.
            source.Place(HandSide.Right, new float2(-400f, 360f), 0.45f);
            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(0));

            // Half in: the joints that are out are not seen, and the ones that are in are.
            source.Place(HandSide.Right, new float2(0f, 360f), 0.45f);
            rig.Settle();
            Assert.That(rig.Detection.HandCount, Is.EqualTo(1));
            DetectedHand hand = rig.Detection.Hand0;
            int seen = 0, notSeen = 0;
            for (int j = 0; j < HandJoints.Count; j++)
            {
                JointSample s = hand.GetJoint(j);
                bool inside = s.Pixel.x >= 0f && s.Pixel.x < 1280f && s.Pixel.y >= 0f && s.Pixel.y < 720f;
                if (inside)
                {
                    Assert.That(s.Confidence, Is.GreaterThan(0.9f));
                    seen++;
                }
                else
                {
                    Assert.That(s.Confidence, Is.EqualTo(0f), "joint " + j + " is out of the picture");
                    Assert.That(s.DepthConfidence, Is.EqualTo(-1));
                    notSeen++;
                }
            }
            Assert.That(seen, Is.GreaterThan(0));
            Assert.That(notSeen, Is.GreaterThan(0));
            for (int i = 0; i < hand.PalmCount; i++)
            {
                float3 sample = hand.GetPalmSample(i);
                Assert.That(sample.x, Is.InRange(0f, 1280f));
                Assert.That(sample.y, Is.InRange(0f, 720f));
            }

            // The border of the view can be crossed both ways, and a hand placed too far out comes back to a place it can be brought in from.
            source.Place(HandSide.Right, new float2(-5000f, 9000f), 0.45f);
            Assert.That(source.PalmPixel(HandSide.Right).x, Is.GreaterThanOrEqualTo(-EditorHandsSource.OutsideMarginPixels));
            Assert.That(source.PalmPixel(HandSide.Right).y, Is.LessThanOrEqualTo(720f + EditorHandsSource.OutsideMarginPixels));
        }

        [Test]
        public void Hands_EditorSource_HandsTurnedAwayFromTheCamera_WithTheThumbsInside()
        {
            // The walker's own hands, seen from behind: the right thumb is on the left of its hand, the left thumb on the right.
            var rig = new Rig();
            rig.Settle();
            Assert.That(Lift(in rig.Detection, 0, out JointSet left), Is.True);
            Assert.That(Lift(in rig.Detection, 1, out JointSet right), Is.True);
            Assert.That(left[HandJoint.ThumbTip].x, Is.GreaterThan(left[HandJoint.LittleMcp].x), "left thumb inside");
            Assert.That(right[HandJoint.ThumbTip].x, Is.LessThan(right[HandJoint.LittleMcp].x), "right thumb inside");
            Assert.That(left[HandJoint.MiddleTip].y, Is.GreaterThan(left[HandJoint.Wrist].y), "fingers up");
            Assert.That(left[HandJoint.MiddleTip].z, Is.GreaterThan(left[HandJoint.Wrist].z), "and away");
        }

        [Test]
        public void Hands_EditorSource_AllocatesNothing()
        {
            var warm = new Rig();
            for (int i = 0; i < 120; i++) warm.Step(new EditorHandsControls { Hold = true, MouseDelta = new float2(1f, 0f), ShapeKey = i % 5 });

            var rig = new Rig();
            rig.Settle();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 300; i++)
                rig.Step(new EditorHandsControls
                {
                    Hold = true,
                    MouseDelta = new float2(math.sin(i * 0.1f) * 4f, math.cos(i * 0.1f) * 4f),
                    Wheel = i % 7 == 0 ? 1f : 0f,
                    ShapeKey = i % 60 == 0 ? 1 + (i / 60) % 4 : 0,
                    ChooseLeft = i % 90 == 0,
                    ChooseRight = i % 90 == 45,
                });
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(rig.Results, Is.GreaterThan(100));
            Assert.That(allocated, Is.EqualTo(0), "300 frames of moving hands");
        }

        [Test]
        public void Hands_EditorSource_ClockThatGoesBackStartsItOver()
        {
            var rig = new Rig();
            rig.Settle();
            int sequence = rig.Detection.Sequence;
            rig.Time = 5.0;     // the app clock was reset
            rig.Settle(10);
            Assert.That(rig.Detection.Sequence, Is.GreaterThan(sequence), "the sequence keeps rising");
            Assert.That(rig.Detection.PollTime, Is.LessThanOrEqualTo(rig.Time));
            Assert.That(rig.Detection.HandCount, Is.EqualTo(2));
        }

#if ATLAS_HEADLESS
        [Test]
        public void Hands_EditorSource_WithNoDevicesGivesTheHandsWhereTheyStart()
        {
            // No keyboard and no mouse (a headless run has none, and the Editor's are not read here): the hands are
            // just there, still, and nothing throws.
            var rig = new Rig();
            var source = rig.Source;
            float2 start = source.PalmPixel(HandSide.Right);
            for (int i = 0; i < 60; i++)
            {
                rig.Time += Frame;
                source.TryRead(rig.Time, ref rig.Detection);
            }
            Assert.That(rig.Detection.HandCount, Is.EqualTo(2));
            Assert.That(source.PalmPixel(HandSide.Right), Is.EqualTo(start));
        }
#endif
    }
}
