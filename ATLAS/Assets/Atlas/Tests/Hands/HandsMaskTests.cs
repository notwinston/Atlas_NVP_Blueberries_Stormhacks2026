using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using Scene = Atlas.Hands.Tests.HandsMaskFixture;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The builder of the depth mask (documents/ATLAS_Hands_Design.md section 7.1, Appendix A HD-D2): where the
    /// walker's hands are in the depth image Bump Guard is about to read. Every expected pixel in these tests comes
    /// from hands built in the camera's own space, never from the code under test.
    /// </summary>
    public class HandsMaskTests
    {
        const float Reach = HandsConstants.ReachMetres;
        static readonly float3 RightWrist = new float3(0.11f, -0.10f, Reach);
        static readonly float3 LeftWrist = new float3(-0.11f, -0.10f, Reach);

        static DepthMask BuildOne(in DepthFrame frame, in TrackedHand hand, double now = 10.0)
        {
            return new HandsMaskBuilder().Build(now, frame, default(TrackedHand), hand);
        }

        static DepthMaskRegion RegionOf(in DepthFrame frame, in TrackedHand hand)
        {
            DepthMask mask = BuildOne(frame, hand);
            Assert.That(mask.Count, Is.EqualTo(1), "one hand gives one region");
            return mask.Region0;
        }

        // ================================================================== geometry

        [Test]
        public void Hands_Mask_Geometry_BoxIsTheJointsGrownByAShareOfTheLongerSideOnEverySide()
        {
            DepthFrame frame = Scene.Frame();
            // Rolled and tilted, so the box is neither square to the picture nor flat to the camera.
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.06f, -0.08f, Reach), 20f, 15f);
            TrackedHand hand = Scene.Tracked(frame, cam);

            DepthMaskRegion r = RegionOf(frame, hand);

            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float2 p = Scene.Pixel(frame, cam[joint]);
                Assert.That(p.x, Is.InRange(r.MinU, r.MaxU), "joint " + joint + " column");
                Assert.That(p.y, Is.InRange(r.MinV, r.MaxV), "joint " + joint + " row");
            }

            Scene.Bounds(frame, cam, out float minU, out float minV, out float maxU, out float maxV);
            float grow = HandsConstants.MaskGrow * math.max(maxU - minU, maxV - minV);
            Assert.That(grow, Is.GreaterThan(10f), "the scene is big enough to tell the growth from rounding");
            Assert.That(r.MinU, Is.EqualTo(minU - grow).Within(0.05f));
            Assert.That(r.MaxU, Is.EqualTo(maxU + grow).Within(0.05f));
            Assert.That(r.MinV, Is.EqualTo(minV - grow).Within(0.05f));
            Assert.That(r.MaxV, Is.EqualTo(maxV + grow).Within(0.05f));
        }

        [Test]
        public void Hands_Mask_Geometry_PixelsAreSampleIndicesTheInverseOfUnproject()
        {
            DepthFrame frame = Scene.Frame();
            // All 21 joints at the point that sample (100, 60) sees at 0.50 m: the box is that one sample, no
            // growth (the longer side is 0), and no strip (no direction from the knuckle to the wrist).
            float3 world = frame.ToWorld(100, 60, 0.5f);
            JointSet joints = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++) joints[i] = world;
            var hand = new TrackedHand { Tracked = true, Fresh = true, Confidence = 0.9f, World = joints, PalmCentre = world };

            DepthMaskRegion r = RegionOf(frame, hand);

            Assert.That(r.MinU, Is.EqualTo(100f).Within(1e-2f));
            Assert.That(r.MaxU, Is.EqualTo(100f).Within(1e-2f));
            Assert.That(r.MinV, Is.EqualTo(60f).Within(1e-2f));
            Assert.That(r.MaxV, Is.EqualTo(60f).Within(1e-2f));
            Assert.That(r.ArmHalfWidth, Is.LessThanOrEqualTo(0f), "a hand with no direction has no forearm strip");
        }

        [TestCase(HandSide.Right, 0f, 0f)]
        [TestCase(HandSide.Left, 0f, 0f)]
        [TestCase(HandSide.Right, 90f, 0f)]
        [TestCase(HandSide.Left, 200f, 25f)]
        [TestCase(HandSide.Right, 330f, 45f)]
        public void Hands_Mask_Geometry_EverySampleOnTheHandAtItsOwnDepthIsContained(HandSide side, float roll, float tilt)
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(side, new float3(side == HandSide.Right ? 0.07f : -0.07f, -0.04f, Reach), roll, tilt);
            Scene.PaintHand(frame, cam, forearm: false);
            Scene.Lidar(frame);
            TrackedHand hand = Scene.Tracked(frame, cam);

            DepthMask mask = BuildOne(frame, hand);

            int missed = Scene.NearSamplesMissed(frame, mask, out int onTheHand);
            Assert.That(onTheHand, Is.GreaterThan(1500), "the hand really is in the picture");
            Assert.That(missed, Is.EqualTo(0), "samples of the hand that the mask lets through");
        }

        [Test]
        public void Hands_Mask_Geometry_TheForearmIsContainedToo()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            Scene.PaintHand(frame, cam, forearm: true);
            Scene.Lidar(frame);
            TrackedHand hand = Scene.Tracked(frame, cam);

            DepthMask mask = BuildOne(frame, hand);

            int missed = Scene.NearSamplesMissed(frame, mask, out int nearSamples);
            Assert.That(nearSamples, Is.GreaterThan(4000), "the hand and its forearm are in the picture");
            Assert.That(missed, Is.EqualTo(0), "samples of the hand or forearm that the mask lets through");
        }

        [Test]
        public void Hands_Mask_Geometry_ASampleBehindThePalmIsNotContained()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.05f, -0.06f, Reach));
            TrackedHand hand = Scene.Tracked(frame, cam);
            DepthMask mask = BuildOne(frame, hand);
            Assert.That(mask.Count, Is.EqualTo(1));

            float3 palm = Scene.PalmCentre(cam);
            float2 at = Scene.Pixel(frame, palm);
            Assert.That(mask.Region0.MaxDepth, Is.EqualTo(palm.z + HandsConstants.MaskBehindPalmMetres).Within(1e-3f));
            Assert.That(mask.Contains(at.x, at.y, palm.z), Is.True, "on the palm");
            Assert.That(mask.Contains(at.x, at.y, palm.z - 0.10f), Is.True, "in front of the palm, inside the box");
            Assert.That(mask.Contains(at.x, at.y, palm.z + 0.19f), Is.True, "just inside the gate");
            Assert.That(mask.Contains(at.x, at.y, palm.z + 0.21f), Is.False, "just outside the gate");
            Assert.That(mask.Contains(at.x, at.y, palm.z + 0.30f), Is.False, "0.3 m behind the palm, in the middle of the box");
        }

        [TestCase(0f)]
        [TestCase(90f)]
        [TestCase(180f)]
        [TestCase(270f)]
        public void Hands_Mask_Geometry_StripReachesTheBorderOnTheWristsSideAndNeverTheFingersSide(float roll)
        {
            DepthFrame frame = Scene.Frame();
            // The hand is centred on the picture with its wrist 9 cm from the middle, opposite the fingers.
            float s = math.sin(math.radians(roll)), c = math.cos(math.radians(roll));
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.09f * s, -0.09f * c, Reach), roll);
            TrackedHand hand = Scene.Tracked(frame, cam);
            DepthMaskRegion r = RegionOf(frame, hand);
            float z = Scene.PalmDepth(cam);

            float2 wrist = Scene.Pixel(frame, cam[HandJoint.Wrist]);
            float2 knuckle = Scene.Pixel(frame, cam[HandJoint.MiddleMcp]);
            float2 awayFromFingers = math.normalize(wrist - knuckle);

            Assert.That(r.ArmHalfWidth, Is.GreaterThan(0f));
            Assert.That(r.ArmU0, Is.EqualTo(wrist.x).Within(0.05f), "the strip starts at the wrist");
            Assert.That(r.ArmV0, Is.EqualTo(wrist.y).Within(0.05f));

            // It runs from the middle knuckle through the wrist and on: parallel to that line, and ahead of the wrist.
            float2 run = new float2(r.ArmU1 - r.ArmU0, r.ArmV1 - r.ArmV0);
            Assert.That(math.dot(math.normalize(run), awayFromFingers), Is.EqualTo(1f).Within(1e-4f), "away from the fingers");

            // It ends on the border of the image, on the wrist's side.
            float right = Scene.Width - 0.5f, bottom = Scene.Height - 0.5f;
            bool onBorder = Math.Abs(r.ArmU1 + 0.5f) < 0.02f || Math.Abs(r.ArmU1 - right) < 0.02f
                         || Math.Abs(r.ArmV1 + 0.5f) < 0.02f || Math.Abs(r.ArmV1 - bottom) < 0.02f;
            Assert.That(onBorder, Is.True, "the strip ends at (" + r.ArmU1 + ", " + r.ArmV1 + ")");
            Assert.That(r.ArmU1, Is.InRange(-0.52f, right + 0.02f));
            Assert.That(r.ArmV1, Is.InRange(-0.52f, bottom + 0.02f));

            // Samples on the border on the wrist's side are masked at the palm's depth, and not 0.3 m behind it.
            // Samples on the opposite border, the fingers' side, never are.
            float2 end = new float2(r.ArmU1, r.ArmV1);
            float2 wristsSide = new float2(math.clamp(end.x, 0f, Scene.Width - 1f), math.clamp(end.y, 0f, Scene.Height - 1f));
            Assert.That(r.Contains(wristsSide.x, wristsSide.y, z), Is.True, "the border on the wrist's side");
            Assert.That(r.Contains(wristsSide.x, wristsSide.y, z + 0.30f), Is.False, "and the gate still holds there");
            // There the strip lies outside the box, and its own gate holds: 0.05 m behind the palm, not 0.20 m.
            bool inTheBox = wristsSide.x >= r.MinU && wristsSide.x <= r.MaxU && wristsSide.y >= r.MinV && wristsSide.y <= r.MaxV;
            Assert.That(inTheBox, Is.False, "the border on the wrist's side is beyond the box");
            Assert.That(r.Contains(wristsSide.x, wristsSide.y, z + 0.04f), Is.True, "just inside the strip's gate");
            Assert.That(r.Contains(wristsSide.x, wristsSide.y, z + 0.06f), Is.False, "just outside it, though inside the box's gate");
            for (int k = 0; k < 8; k++)
            {
                float along = 8f + k * 32f;
                float2 fingersSide = roll == 0f ? new float2(along, 0f)
                                   : roll == 90f ? new float2(0f, along * 0.75f)
                                   : roll == 180f ? new float2(along, Scene.Height - 1f)
                                   : new float2(Scene.Width - 1f, along * 0.75f);
                Assert.That(r.Contains(fingersSide.x, fingersSide.y, z), Is.False,
                    "the border on the fingers' side at (" + fingersSide.x + ", " + fingersSide.y + ")");
            }
        }

        [Test]
        public void Hands_Mask_Geometry_StripIsAsWideAsTheDesignSaysPlusTheSameGrowth()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.02f, -0.09f, Reach));
            DepthMaskRegion r = RegionOf(frame, Scene.Tracked(frame, cam));

            Scene.Bounds(frame, cam, out float minU, out float minV, out float maxU, out float maxV);
            float grow = HandsConstants.MaskGrow * math.max(maxU - minU, maxV - minV);
            float expected = 0.5f * HandsConstants.MaskArmPalmWidths * Scene.PalmWidthPixels(frame, cam) + grow;
            Assert.That(r.ArmHalfWidth, Is.EqualTo(expected).Within(0.05f));
        }

        [Test]
        public void Hands_Mask_Geometry_GrowthFollowsTheHandsSpeedInThePictureTimesItsAge()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.06f, -0.06f, Reach), 10f, 10f);
            DepthMaskRegion still = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.05f));

            // Sideways, 0.3 m/s at 0.48 m is about 135 pixels a second; in 50 ms about 6.7 pixels. Then a hand moving
            // towards the camera off the centre of the picture, which the picture sees as the hand growing outwards.
            foreach (float3 velocity in new[] { new float3(0.3f, 0f, 0f), new float3(0f, -0.25f, 0.1f), new float3(0f, 0f, -1.0f) })
            {
                float expected = PixelSpeed(frame, cam, velocity) * 0.05f;
                Assert.That(expected, Is.InRange(1f, HandsConstants.MaskMotionMaxPixels - 1f), "the case is below the cap");
                DepthMaskRegion moving = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.05f, cameraVelocity: velocity));
                AssertGrownBy(still, moving, expected, "velocity " + velocity);
            }

            // The same speed with twice the age grows twice as much; with no age it grows nothing.
            float3 v = new float3(0.3f, 0f, 0f);
            AssertGrownBy(still, RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.10f, cameraVelocity: v)),
                          PixelSpeed(frame, cam, v) * 0.10f, "twice the age");
            AssertGrownBy(still, RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0f, cameraVelocity: v)), 0f, "no age");
        }

        [Test]
        public void Hands_Mask_Geometry_GrowthForMotionStopsAtTwentyPixels()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.06f, -0.06f, Reach));
            DepthMaskRegion still = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.08f));
            Assert.That(HandsConstants.MaskMotionMaxPixels, Is.EqualTo(20f));

            // 2 m/s for 80 ms would be 72 pixels.
            float3 fast = new float3(2f, 0f, 0f);
            Assert.That(PixelSpeed(frame, cam, fast) * 0.08f, Is.GreaterThan(60f));
            DepthMaskRegion capped = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.08f, cameraVelocity: fast));
            AssertGrownBy(still, capped, HandsConstants.MaskMotionMaxPixels, "capped");
            // A hand held for half a second at the same speed is capped just the same, not wider.
            DepthMaskRegion old = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.5f, cameraVelocity: fast));
            AssertGrownBy(still, old, HandsConstants.MaskMotionMaxPixels, "capped at any age");
        }

        [Test]
        public void Hands_Mask_Geometry_TheCapOfTwentyPixelsIsForTheNominalImageAndScalesWithOthers()
        {
            // The Editor's synthetic depth is 64 x 48: 20 pixels there would be a third of the picture.
            DepthFrame frame = Scene.Frame(fx: Scene.NominalFx / 4f, width: 64, height: 48);
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.04f, -0.05f, Reach));
            DepthMaskRegion still = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.08f));
            DepthMaskRegion capped = RegionOf(frame, Scene.Tracked(frame, cam, ageSeconds: 0.5f, cameraVelocity: new float3(2f, 0f, 0f)));
            AssertGrownBy(still, capped, HandsConstants.MaskMotionMaxPixels * 64f / DepthFrame.NominalWidth, "5 pixels on a 64-pixel image");
        }

        [TestCase(0.40f, 0.60f)]
        [TestCase(0.50f, 0.70f)]
        [TestCase(0.60f, 0.80f)]
        [TestCase(0.70f, 0.80f)]
        [TestCase(0.85f, 0.80f)]
        public void Hands_Mask_Geometry_DepthGateIsThePalmPlusTwentyCentimetresAndNeverBeyondEighty(float palmDepth, float gate)
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.02f, -0.04f, palmDepth));
            DepthMask mask = BuildOne(frame, Scene.Tracked(frame, cam));
            Assert.That(mask.Count, Is.EqualTo(1));
            Assert.That(mask.Region0.MaxDepth, Is.EqualTo(gate).Within(1e-3f));

            float2 at = Scene.Pixel(frame, Scene.PalmCentre(cam));
            Assert.That(mask.Contains(at.x, at.y, gate - 0.01f), Is.True, "just inside the gate");
            Assert.That(mask.Contains(at.x, at.y, gate + 0.01f), Is.False, "just outside it");
            Assert.That(mask.Contains(at.x, at.y, 0.805f), Is.False, "never beyond 0.80 m, whatever the palm");
            Assert.That(mask.Contains(at.x, at.y, 2f), Is.False);
        }

        [TestCase(0.40f, 0.45f)]
        [TestCase(0.48f, 0.53f)]
        [TestCase(0.60f, 0.65f)]
        [TestCase(0.74f, 0.79f)]
        [TestCase(0.78f, 0.80f)]
        [TestCase(0.85f, 0.80f)]
        public void Hands_Mask_Geometry_TheStripsOwnGateIsThePalmPlusFiveCentimetresAndNeverBeyondEighty(float palmDepth, float gate)
        {
            // Decision R14. The forearm is no deeper than the palm, so the strip outside the hand's box is gated
            // 0.05 m behind the palm; the box keeps its 0.20 m. Neither is ever beyond 0.80 m.
            Assert.That(HandsMaskBuilder.ArmBehindPalmMetres, Is.EqualTo(0.05f));
            DepthFrame frame = Scene.Frame();
            // The hand is held high, so that the strip runs a long way below its box.
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.02f, 0.04f, palmDepth));
            DepthMask mask = BuildOne(frame, Scene.Tracked(frame, cam));
            Assert.That(mask.Count, Is.EqualTo(1));
            DepthMaskRegion r = mask.Region0;
            Assert.That(Scene.PalmDepth(cam), Is.EqualTo(palmDepth).Within(1e-4f), "a flat hand: the palm is as deep as the wrist");
            Assert.That(r.ArmMaxDepth, Is.EqualTo(gate).Within(1e-3f));
            float boxGate = math.min(palmDepth + HandsConstants.MaskBehindPalmMetres, HandsConstants.MaskMaxDepthMetres);
            Assert.That(r.MaxDepth, Is.EqualTo(boxGate).Within(1e-3f), "the box keeps the palm plus 0.20 m");
            Assert.That(r.ArmMaxDepth, Is.LessThanOrEqualTo(r.MaxDepth));

            // A sample in the strip, well below the box.
            Assert.That(r.ArmHalfWidth, Is.GreaterThan(0f));
            float u = r.ArmU1, v = Scene.Height - 6f;
            Assert.That(v, Is.GreaterThan(r.MaxV + 2f), "the sample is below the box");
            Assert.That(mask.Contains(u, v, gate - 0.01f), Is.True, "just inside the strip's gate");
            Assert.That(mask.Contains(u, v, gate + 0.01f), Is.False, "just outside it");
            Assert.That(mask.Contains(u, v, 0.805f), Is.False, "never beyond 0.80 m, whatever the palm");
            // The same depths on the palm, inside the box: the box's own gate decides there.
            float2 palm = Scene.Pixel(frame, Scene.PalmCentre(cam));
            Assert.That(mask.Contains(palm.x, palm.y, boxGate - 0.01f), Is.True);
            Assert.That(mask.Contains(palm.x, palm.y, boxGate + 0.01f), Is.False);
            if (boxGate > gate + 0.02f)
                Assert.That(mask.Contains(palm.x, palm.y, gate + 0.01f), Is.True, "deeper than the strip's gate, on the palm: still the hand's");
        }

        [Test]
        public void Hands_Mask_Geometry_ARegionWithNoStripGateOfItsOwnUsesTheBoxs()
        {
            // The shared type's rule, which a mask from before the strip had its own gate relies on: 0, a negative
            // number or one that is not a number means the box's gate.
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.02f, 0.04f, Reach));
            DepthMaskRegion r = RegionOf(frame, Scene.Tracked(frame, cam));
            float u = r.ArmU1, v = Scene.Height - 6f;
            Assert.That(r.Contains(u, v, Reach + 0.10f), Is.False, "behind the forearm, beyond the strip's gate");
            foreach (float none in new[] { 0f, -0.3f, float.NaN })
            {
                r.ArmMaxDepth = none;
                Assert.That(r.Contains(u, v, Reach + 0.10f), Is.True, "arm gate " + none + ": the box's gate, 0.20 m behind the palm");
                Assert.That(r.Contains(u, v, Reach + 0.21f), Is.False, "arm gate " + none);
            }
        }

        [Test]
        public void Hands_Mask_Geometry_TwoHandsAreTwoRegionsLeftFirst()
        {
            DepthFrame frame = Scene.Frame();
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist);
            JointSet rightCam = Scene.Hand(HandSide.Right, RightWrist);
            TrackedHand left = Scene.Tracked(frame, leftCam), right = Scene.Tracked(frame, rightCam);

            DepthMask mask = new HandsMaskBuilder().Build(10.0, frame, left, right);

            Assert.That(mask.Count, Is.EqualTo(2));
            float2 leftPalm = Scene.Pixel(frame, Scene.PalmCentre(leftCam)), rightPalm = Scene.Pixel(frame, Scene.PalmCentre(rightCam));
            Assert.That(mask.Region0.Contains(leftPalm.x, leftPalm.y, Reach), Is.True);
            Assert.That(mask.Region1.Contains(rightPalm.x, rightPalm.y, Reach), Is.True);
            Assert.That(mask.Region0.Contains(rightPalm.x, rightPalm.y, Reach), Is.False, "the left region is not the right hand");
            Assert.That(mask.Region1.Contains(leftPalm.x, leftPalm.y, Reach), Is.False);
            Assert.That(mask.Contains(leftPalm.x, leftPalm.y, Reach) && mask.Contains(rightPalm.x, rightPalm.y, Reach), Is.True);
        }

        [Test]
        public void Hands_Mask_Geometry_TheMaskFollowsTheDepthFramesOwnPoseNotTheHandsFrame()
        {
            // The same hand in the same room, seen by two depth frames with different poses, lands in the right
            // place in each. (Each frame's expected pixel is worked out in its own camera space.)
            DepthFrame a = Scene.Frame();
            DepthFrame b = Scene.Frame();
            b.Position = a.Position + new float3(-0.12f, 0.04f, -0.08f);
            b.Rotation = math.mul(quaternion.RotateY(math.radians(-8f)), a.Rotation);

            JointSet camA = Scene.Hand(HandSide.Right, new float3(0.05f, -0.05f, Reach), 15f, 10f);
            JointSet world = camA.Transformed(a.Rotation, a.Position);
            // The same joints in b's camera space.
            quaternion back = math.inverse(b.Rotation);
            JointSet camB = default(JointSet);
            for (int i = 0; i < HandJoints.Count; i++) camB[i] = math.mul(back, world[i] - b.Position);
            var hand = new TrackedHand { Tracked = true, Fresh = true, Confidence = 0.9f, World = world, PalmCentre = Scene.PalmCentre(world) };

            DepthMaskRegion ra = BuildOne(a, hand).Region0, rb = BuildOne(b, hand).Region0;

            foreach (var (frame, cam, r) in new[] { (a, camA, ra), (b, camB, rb) })
            {
                Scene.Bounds(frame, cam, out float minU, out float minV, out float maxU, out float maxV);
                float grow = HandsConstants.MaskGrow * math.max(maxU - minU, maxV - minV);
                Assert.That(r.MinU, Is.EqualTo(minU - grow).Within(0.05f));
                Assert.That(r.MaxV, Is.EqualTo(maxV + grow).Within(0.05f));
                float gate = math.min(Scene.PalmDepth(cam) + HandsConstants.MaskBehindPalmMetres, HandsConstants.MaskMaxDepthMetres);
                Assert.That(r.MaxDepth, Is.EqualTo(gate).Within(1e-3f));
            }
            Assert.That(Scene.PalmDepth(camA), Is.Not.EqualTo(Scene.PalmDepth(camB)).Within(0.02f), "the palm's depth differs between the frames too");
            Assert.That(Math.Abs(ra.MinU - rb.MinU), Is.GreaterThan(5f), "the two frames really do see the hand in different places");
        }

        // ================================================================== lifetime

        [Test]
        public void Hands_Mask_Lifetime_LivesThroughTheGraceAndTheLingerAndNoLonger()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            const double detected = 100.0;
            float lastBox;

            // A detection arrives: the mask starts at once.
            Assert.That(builder.Build(detected, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, fresh: true)).Count, Is.EqualTo(1));

            // The hand is missed: the filter holds its pose as Tracked for the grace period, its confidence falling.
            foreach (float lost in new[] { 0.05f, 0.20f, 0.25f, 0.39f })
            {
                DepthMask held = builder.Build(detected + lost, frame, default(TrackedHand),
                    Scene.Tracked(frame, cam, 0.3f, fresh: false, lostSeconds: lost, ageSeconds: 0.05f + lost));
                Assert.That(held.Count, Is.EqualTo(1), "lost for " + lost + " s");
            }
            lastBox = builder.Build(detected + 0.39, frame, default(TrackedHand),
                Scene.Tracked(frame, cam, 0.3f, fresh: false, lostSeconds: 0.39f, ageSeconds: 0.05f)).Region0.MinU;

            // After the grace the filter stops tracking and hands over nothing. The mask lingers on its own, from the
            // last pose it saw, until the grace plus the linger after the last detection.
            float end = HandsConstants.LossGraceSeconds + HandsConstants.MaskLingerSeconds;
            Assert.That(end, Is.EqualTo(0.55f).Within(1e-6f));
            foreach (double after in new[] { 0.41, 0.45, 0.50, end - 0.01 })
            {
                DepthMask lingering = builder.Build(detected + after, frame, default(TrackedHand), default(TrackedHand));
                Assert.That(lingering.Count, Is.EqualTo(1), "untracked for " + (after - HandsConstants.LossGraceSeconds) + " s");
                Assert.That(lingering.Region0.MinU, Is.EqualTo(lastBox).Within(0.5f), "the pose it remembers");
            }

            foreach (double after in new[] { end + 0.01, 0.60, 0.80, 5.0 })
                Assert.That(builder.Build(detected + after, frame, default(TrackedHand), default(TrackedHand)).IsEmpty, Is.True, "gone after " + after + " s");
        }

        [Test]
        public void Hands_Mask_Lifetime_FollowsATrackedHandNotTheOldPose()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet first = Scene.Hand(HandSide.Right, new float3(0.02f, -0.06f, Reach));
            JointSet later = Scene.Hand(HandSide.Right, new float3(0.14f, -0.02f, Reach + 0.05f));

            DepthMaskRegion a = builder.Build(5.0, frame, default(TrackedHand), Scene.Tracked(frame, first)).Region0;
            DepthMaskRegion b = builder.Build(5.1, frame, default(TrackedHand), Scene.Tracked(frame, later, 0.3f, fresh: false, lostSeconds: 0.1f)).Region0;

            Scene.Bounds(frame, later, out float minU, out _, out _, out _);
            Assert.That(b.MinU, Is.GreaterThan(a.MinU + 10f), "the region moved with the pose");
            Assert.That(b.MinU, Is.LessThan(minU), "and is that pose's box");
            Assert.That(b.MaxDepth, Is.EqualTo(Scene.PalmDepth(later) + HandsConstants.MaskBehindPalmMetres).Within(1e-3f));
        }

        [Test]
        public void Hands_Mask_Lifetime_ALingeringMaskNeverShrinksAndAFreshDetectionRestartsIt()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            float3 drifting = new float3(0.4f, 0f, 0f);

            DepthMaskRegion tracked = builder.Build(1.0, frame, default(TrackedHand),
                Scene.Tracked(frame, cam, 0.8f, fresh: true, lostSeconds: 0.39f, ageSeconds: 0.05f, cameraVelocity: drifting)).Region0;
            DepthMaskRegion lingering = builder.Build(1.0 + 0.05, frame, default(TrackedHand), default(TrackedHand)).Region0;
            Assert.That(lingering.MinU, Is.LessThanOrEqualTo(tracked.MinU + 1e-3f), "the box does not shrink while the pose ages");
            Assert.That(lingering.MaxU, Is.GreaterThanOrEqualTo(tracked.MaxU - 1e-3f));
            Assert.That(lingering.MaxU - tracked.MaxU, Is.LessThanOrEqualTo(HandsConstants.MaskMotionMaxPixels + 1e-3f));

            // A detection inside the linger makes a fresh start: the mask runs a whole lifetime from it.
            var builder2 = new HandsMaskBuilder();
            builder2.Build(10.0, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, fresh: true));
            Assert.That(builder2.Build(10.50, frame, default(TrackedHand), default(TrackedHand)).Count, Is.EqualTo(1), "lingering");
            Assert.That(builder2.Build(10.52, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, fresh: true)).Count, Is.EqualTo(1));
            Assert.That(builder2.Build(11.00, frame, default(TrackedHand), default(TrackedHand)).Count, Is.EqualTo(1), "0.48 s after the new detection");
            Assert.That(builder2.Build(11.08, frame, default(TrackedHand), default(TrackedHand)).IsEmpty, Is.True, "0.56 s after it");
        }

        [Test]
        public void Hands_Mask_Lifetime_ResetAndABackwardsClockForgetTheHand()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            var builder = new HandsMaskBuilder();

            builder.Build(50.0, frame, default(TrackedHand), Scene.Tracked(frame, cam));
            Assert.That(builder.Build(50.2, frame, default(TrackedHand), default(TrackedHand)).Count, Is.EqualTo(1), "lingering before the reset");
            builder.Reset();
            Assert.That(builder.Build(50.21, frame, default(TrackedHand), default(TrackedHand)).IsEmpty, Is.True, "nothing after Reset");

            // A replay that seeks back: what was remembered belongs to another timeline.
            builder.Build(80.0, frame, default(TrackedHand), Scene.Tracked(frame, cam));
            Assert.That(builder.Build(2.0, frame, default(TrackedHand), default(TrackedHand)).IsEmpty, Is.True, "the clock went back");
        }

        [Test]
        public void Hands_Mask_Lifetime_EachHandHasItsOwn()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist), rightCam = Scene.Hand(HandSide.Right, RightWrist);

            Assert.That(builder.Build(1.0, frame, Scene.Tracked(frame, leftCam), Scene.Tracked(frame, rightCam)).Count, Is.EqualTo(2));
            // The right hand is detected on, the left one is lost and then untracked.
            builder.Build(1.3, frame, Scene.Tracked(frame, leftCam, 0.4f, false, 0.3f), Scene.Tracked(frame, rightCam));
            DepthMask after = builder.Build(1.45, frame, default(TrackedHand), Scene.Tracked(frame, rightCam));
            Assert.That(after.Count, Is.EqualTo(2), "the left hand's linger runs from its own last detection (1.0 s)");
            Assert.That(builder.Build(1.60, frame, default(TrackedHand), Scene.Tracked(frame, rightCam)).Count, Is.EqualTo(1), "0.60 s after it: only the right hand");
        }

        // ================================================================== confidence

        [Test]
        public void Hands_Mask_ConfidenceGate_NeedsTheInteractConfidenceFromTheDetection()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            Assert.That(HandsConstants.InteractConfidence, Is.EqualTo(0.6f));

            Assert.That(BuildOne(frame, Scene.Tracked(frame, cam, 0.59f)).IsEmpty, Is.True, "0.59 is not enough");
            Assert.That(BuildOne(frame, Scene.Tracked(frame, cam, 0.50f)).IsEmpty, Is.True, "nor is the 0.5 of a lift that had no depth");
            Assert.That(BuildOne(frame, Scene.Tracked(frame, cam, HandsConstants.InteractConfidence)).Count, Is.EqualTo(1), "exactly 0.6 is");
            Assert.That(BuildOne(frame, Scene.Tracked(frame, cam, 0.95f)).Count, Is.EqualTo(1));
            Assert.That(BuildOne(frame, Scene.Tracked(frame, cam, float.NaN)).IsEmpty, Is.True, "no confidence is no mask");
        }

        [Test]
        public void Hands_Mask_ConfidenceGate_AHandWhoseLastDetectionWasConfidentKeepsItsMaskWhileItIsLost()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);

            builder.Build(1.0, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, fresh: true));
            // Lost: whatever the confidence field decays to, the last detection is what counts.
            Assert.That(builder.Build(1.2, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.0f, false, 0.2f)).Count, Is.EqualTo(1));
            Assert.That(builder.Build(1.3, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.1f, false, 0.3f)).Count, Is.EqualTo(1));
        }

        [Test]
        public void Hands_Mask_ConfidenceGate_ALowDetectionEndsTheMaskAtOnceAndAGoodOneBringsItBack()
        {
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);

            Assert.That(builder.Build(1.00, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, true)).Count, Is.EqualTo(1));
            Assert.That(builder.Build(1.017, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.9f, false)).Count, Is.EqualTo(1), "between detections");
            Assert.That(builder.Build(1.033, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.4f, true)).IsEmpty, Is.True, "a poor detection");
            Assert.That(builder.Build(1.050, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.4f, false)).IsEmpty, Is.True, "still poor between detections");
            Assert.That(builder.Build(1.066, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.75f, true)).Count, Is.EqualTo(1), "a good one");
            // A hand that goes untracked after a poor detection has no mask to linger.
            builder.Build(1.083, frame, default(TrackedHand), Scene.Tracked(frame, cam, 0.5f, true));
            Assert.That(builder.Build(1.20, frame, default(TrackedHand), default(TrackedHand)).IsEmpty, Is.True);
        }

        [Test]
        public void Hands_Mask_ConfidenceGate_EachHandIsGatedOnItsOwnConfidence()
        {
            DepthFrame frame = Scene.Frame();
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist), rightCam = Scene.Hand(HandSide.Right, RightWrist);
            float2 leftAt = Scene.Pixel(frame, Scene.PalmCentre(leftCam)), rightAt = Scene.Pixel(frame, Scene.PalmCentre(rightCam));

            DepthMask onlyLeft = new HandsMaskBuilder().Build(10.0, frame, Scene.Tracked(frame, leftCam, 0.9f), Scene.Tracked(frame, rightCam, 0.4f));
            Assert.That(onlyLeft.Count, Is.EqualTo(1));
            Assert.That(onlyLeft.Contains(leftAt.x, leftAt.y, Reach), Is.True);
            Assert.That(onlyLeft.Contains(rightAt.x, rightAt.y, Reach), Is.False);

            DepthMask onlyRight = new HandsMaskBuilder().Build(10.0, frame, Scene.Tracked(frame, leftCam, 0.55f), Scene.Tracked(frame, rightCam, 0.9f));
            Assert.That(onlyRight.Count, Is.EqualTo(1));
            Assert.That(onlyRight.Contains(rightAt.x, rightAt.y, Reach), Is.True, "the one region is the right hand's, in the first slot");
            Assert.That(onlyRight.Contains(leftAt.x, leftAt.y, Reach), Is.False);

            Assert.That(new HandsMaskBuilder().Build(10.0, frame, Scene.Tracked(frame, leftCam, 0.5f), Scene.Tracked(frame, rightCam, 0.2f)).IsEmpty, Is.True);
        }

        // ================================================================== size limit

        [TestCase(Scene.NominalFx)]
        [TestCase(176.2f)]
        public void Hands_Mask_SizeLimit_TwoOpenHandsAtTheDefaultReachAreKept(float fx)
        {
            DepthFrame frame = Scene.Frame(fx);
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist), rightCam = Scene.Hand(HandSide.Right, RightWrist);
            Scene.PaintHand(frame, leftCam, forearm: true);
            Scene.PaintHand(frame, rightCam, forearm: true);
            Scene.Lidar(frame);
            TrackedHand left = Scene.Tracked(frame, leftCam), right = Scene.Tracked(frame, rightCam);
            var builder = new HandsMaskBuilder();

            DepthMask mask = builder.Build(10.0, frame, left, right);

            Assert.That(mask.Count, Is.EqualTo(2), "both hands are masked");
            float share = Scene.MaskedShare(frame, mask);
            Assert.That(share, Is.InRange(0.08f, HandsConstants.MaskMaxImageShare), "share of the samples the mask hides");
            Assert.That(builder.LastMaskedShare, Is.EqualTo(share).Within(1e-5f), "the builder counts what the mask hides");
            Assert.That(Scene.NearSamplesMissed(frame, mask, out int onThem), Is.EqualTo(0), "and it hides all of the hands");
            Assert.That(onThem, Is.GreaterThan(5000));

            // The area of the two regions on its own would have dropped it: that is why the limit counts samples.
            Scene.Fill(frame, 0.5f);
            Assert.That(Scene.MaskedShare(frame, mask), Is.GreaterThan(HandsConstants.MaskMaxImageShare),
                "regions alone, with something at the palm's depth everywhere");
        }

        // Hands at 0.48 m have a gate at 0.68 m. A wall inside the gate would be hidden wherever a region covers it: by
        // area the regions of two hands cover most of the picture, so the mask is dropped. A wall beyond the gate is
        // never hidden, and the same regions are kept.
        [TestCase(0.55f, false)]
        [TestCase(0.67f, false)]
        [TestCase(0.69f, true)]
        [TestCase(0.80f, true)]
        public void Hands_Mask_SizeLimit_AWallInsideTheGateDropsTheMaskAndOneBeyondItDoesNot(float wall, bool kept)
        {
            DepthFrame frame = Scene.Frame(wall: wall);
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist), rightCam = Scene.Hand(HandSide.Right, RightWrist);
            Scene.PaintHand(frame, leftCam, forearm: true);
            Scene.PaintHand(frame, rightCam, forearm: true);
            Scene.Lidar(frame);
            frame.Sequence = 33;
            var builder = new HandsMaskBuilder();

            DepthMask mask = builder.Build(10.0, frame, Scene.Tracked(frame, leftCam), Scene.Tracked(frame, rightCam));

            Assert.That(mask.DepthSequence, Is.EqualTo(33), "dropped or not, the mask names its image");
            if (kept)
            {
                Assert.That(mask.Count, Is.EqualTo(2));
                Assert.That(builder.LastMaskedShare, Is.LessThan(HandsConstants.MaskMaxImageShare));
            }
            else
            {
                Assert.That(mask.IsEmpty, Is.True, "a mask that would hide more than the limit is dropped whole");
                Assert.That(mask.Count, Is.EqualTo(0));
                Assert.That(builder.LastMaskedShare, Is.GreaterThan(HandsConstants.MaskMaxImageShare));
            }
        }

        [TestCase(Scene.Width, Scene.Height, Scene.NominalFx)]
        [TestCase(64, 48, Scene.NominalFx / 4f)]
        public void Hands_Mask_SizeLimit_TheLimitIsTheConstantOnTheSamplesBumpGuardTakes(int width, int height, float fx)
        {
            // Bump Guard takes every fourth pixel from the second: a 256 x 192 image gives 64 x 48 = 3,072 samples,
            // a 64 x 48 one gives 16 x 12 = 192. A hand far bigger than the picture covers all of them; the samples
            // that count are the ones with a return inside its gate.
            int columns = (width - 2 + 3) / 4, rows = (height - 2 + 3) / 4;
            int total = columns * rows;
            int limit = (int)Math.Floor(HandsConstants.MaskMaxImageShare * total);
            DepthFrame frame = Scene.Frame(fx, width, height);
            JointSet huge = Scene.Hand(HandSide.Right, new float3(0f, -0.45f, 0.6f), 0f, 0f, 5f);
            TrackedHand hand = Scene.Tracked(frame, huge);
            var builder = new HandsMaskBuilder();

            foreach (int masked in new[] { 0, 1, limit - 1, limit, limit + 1, limit + 2, total })
            {
                Scene.Fill(frame, 3f);
                int placed = 0;
                for (int v = 2; v < height && placed < masked; v += 4)
                    for (int u = 2; u < width && placed < masked; u += 4, placed++)
                        frame.Depth[v * width + u] = 0.6f;   // at the palm's depth: inside the gate
                Assert.That(placed, Is.EqualTo(masked));

                DepthMask mask = builder.Build(10.0, frame, default(TrackedHand), hand);

                Assert.That(builder.LastMaskedShare, Is.EqualTo(masked / (float)total).Within(1e-5f), masked + " of " + total);
                if (masked <= limit)
                {
                    Assert.That(mask.Count, Is.EqualTo(1), masked + " of " + total + " is within " + HandsConstants.MaskMaxImageShare);
                    Assert.That(mask.Region0.MinU, Is.LessThanOrEqualTo(0f), "the hand's box covers the whole picture, as the test needs");
                    Assert.That(mask.Region0.MaxU, Is.GreaterThanOrEqualTo(width - 1f));
                    Assert.That(mask.Region0.MinV, Is.LessThanOrEqualTo(0f));
                    Assert.That(mask.Region0.MaxV, Is.GreaterThanOrEqualTo(height - 1f));
                }
                else
                {
                    Assert.That(mask.IsEmpty, Is.True, masked + " of " + total + " is over " + HandsConstants.MaskMaxImageShare);
                }
            }
        }

        [Test]
        public void Hands_Mask_SizeLimit_TheStripCountsOnlyWhatIsInsideItsOwnGate()
        {
            // One hand held high, and something flat behind it that fills the picture. 0.10 m behind the palm it
            // is inside the box's gate and beyond the strip's: the builder counts the samples of the box and no
            // more, and the mask is kept. 0.03 m behind the palm it is inside both gates: the strip's samples are
            // counted too, which takes this mask over the limit.
            JointSet cam = Scene.Hand(HandSide.Right, new float3(0.02f, 0.04f, Reach));
            var builder = new HandsMaskBuilder();
            DepthFrame frame = Scene.Frame(wall: Reach + 0.10f);
            DepthMask mask = builder.Build(10.0, frame, default(TrackedHand), Scene.Tracked(frame, cam));
            Assert.That(mask.Count, Is.EqualTo(1), "0.10 m behind the palm: only the box hides it");
            DepthMaskRegion r = mask.Region0;

            // Counted here from the region's own numbers, not with its Contains: Bump Guard's samples in the box,
            // and those outside the box within the strip's half width of its segment.
            int inBox = 0, inStripOnly = 0, total = 0;
            for (int v = 2; v < Scene.Height; v += 4)
                for (int u = 2; u < Scene.Width; u += 4)
                {
                    total++;
                    if (u >= r.MinU && u <= r.MaxU && v >= r.MinV && v <= r.MaxV) inBox++;
                    else if (DistanceToSegment(new float2(u, v), new float2(r.ArmU0, r.ArmV0), new float2(r.ArmU1, r.ArmV1)) <= r.ArmHalfWidth) inStripOnly++;
                }
            Assert.That(inBox, Is.GreaterThan(100));
            Assert.That(inStripOnly, Is.GreaterThan(100), "the strip runs well below the box");
            float boxShare = inBox / (float)total, bothShare = (inBox + inStripOnly) / (float)total;
            Assert.That(boxShare, Is.LessThan(HandsConstants.MaskMaxImageShare));
            Assert.That(bothShare, Is.GreaterThan(HandsConstants.MaskMaxImageShare), "the scene tells the two counts apart by the limit");

            Assert.That(builder.LastMaskedShare, Is.EqualTo(boxShare).Within(1e-5f), "the strip's samples are beyond its gate: not counted");
            Assert.That(Scene.MaskedShare(frame, mask), Is.EqualTo(boxShare).Within(1e-5f), "and not hidden: the builder counts what the mask hides");
            // With one gate for both, as before, the strip's samples would have been hidden, and counted.
            DepthMask oneGate = mask;
            oneGate.Region0.ArmMaxDepth = 0f;
            Assert.That(Scene.MaskedShare(frame, oneGate), Is.EqualTo(bothShare).Within(1e-5f));

            // 0.03 m behind the palm: inside the strip's gate too.
            Scene.Fill(frame, Reach + 0.03f);
            DepthMask near = builder.Build(10.0, frame, default(TrackedHand), Scene.Tracked(frame, cam));
            Assert.That(builder.LastMaskedShare, Is.EqualTo(bothShare).Within(1e-5f), "box and strip are both counted");
            Assert.That(near.IsEmpty, Is.True, "and that is over the limit: the mask is dropped whole");
        }

        [Test]
        public void Hands_Mask_SizeLimit_SamplesBeyondTheGateAreNotCounted()
        {
            // The same huge hand, with the whole picture 1 m away: nothing is inside the gate, nothing is hidden.
            DepthFrame frame = Scene.Frame(wall: 1.0f);
            JointSet huge = Scene.Hand(HandSide.Right, new float3(0f, -0.45f, 0.6f), 0f, 0f, 5f);
            var builder = new HandsMaskBuilder();

            DepthMask mask = builder.Build(10.0, frame, default(TrackedHand), Scene.Tracked(frame, huge));

            Assert.That(builder.LastMaskedShare, Is.EqualTo(0f));
            Assert.That(mask.Count, Is.EqualTo(1));
            Assert.That(Scene.MaskedShare(frame, mask), Is.EqualTo(0f));
        }

        // ================================================================== sequence and empty

        [Test]
        public void Hands_Mask_SequenceAndEmpty_TheMaskNamesTheImageItWasBuiltFor()
        {
            var builder = new HandsMaskBuilder();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            foreach (int sequence in new[] { 0, 1, 41, 42, 1000000 })
            {
                DepthFrame frame = Scene.Frame(sequence: sequence);
                TrackedHand hand = Scene.Tracked(frame, cam);
                DepthMask withHand = builder.Build(10.0, frame, default(TrackedHand), hand);
                Assert.That(withHand.Count, Is.EqualTo(1));
                Assert.That(withHand.DepthSequence, Is.EqualTo(sequence));
                Assert.That(builder.Build(10.0, frame, default(TrackedHand), default(TrackedHand)).DepthSequence, Is.EqualTo(sequence),
                    "an empty mask names its image too");
            }
        }

        [Test]
        public void Hands_Mask_SequenceAndEmpty_NothingToMaskIsAnEmptyMask()
        {
            DepthFrame frame = Scene.Frame();
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            var builder = new HandsMaskBuilder();

            DepthMask none = builder.Build(10.0, frame, default(TrackedHand), default(TrackedHand));
            Assert.That(none.IsEmpty, Is.True);
            Assert.That(none.Count, Is.EqualTo(0));
            Assert.That(none.Contains(128f, 96f, 0.5f), Is.False);

            // A hand that is not tracked is not masked, whatever else its fields say.
            TrackedHand notTracked = Scene.Tracked(frame, cam);
            notTracked.Tracked = false;
            Assert.That(builder.Build(10.0, frame, default(TrackedHand), notTracked).IsEmpty, Is.True);
        }

        [Test]
        public void Hands_Mask_SequenceAndEmpty_AFrameWithoutUsableDepthGivesNothing()
        {
            JointSet cam = Scene.Hand(HandSide.Right, RightWrist);
            var builder = new HandsMaskBuilder();
            DepthFrame good = Scene.Frame(sequence: 9);
            TrackedHand hand = Scene.Tracked(good, cam);

            Assert.That(builder.Build(10.0, default(DepthFrame), default(TrackedHand), hand).IsEmpty, Is.True, "no frame at all");

            DepthFrame noBuffer = good; noBuffer.Depth = null;
            Assert.That(builder.Build(10.0, noBuffer, default(TrackedHand), hand).IsEmpty, Is.True, "no buffer");
            DepthFrame noWidth = good; noWidth.Width = 0;
            Assert.That(builder.Build(10.0, noWidth, default(TrackedHand), hand).IsEmpty, Is.True, "no width");
            DepthFrame noFocal = good; noFocal.Fx = 0f;
            Assert.That(builder.Build(10.0, noFocal, default(TrackedHand), hand).IsEmpty, Is.True, "no focal length");
            DepthFrame truncated = good; truncated.Depth = new float[100];
            Assert.That(builder.Build(10.0, truncated, default(TrackedHand), hand).IsEmpty, Is.True, "a buffer shorter than the image");

            // And the hand is still remembered through a frame like that: the next good frame masks it.
            Assert.That(builder.Build(10.02, good, default(TrackedHand), hand).Count, Is.EqualTo(1));
        }

        [Test]
        public void Hands_Mask_SequenceAndEmpty_AHandThatCannotBeProjectedIsSkippedAndTheOtherStillCounts()
        {
            DepthFrame frame = Scene.Frame();
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist);
            TrackedHand left = Scene.Tracked(frame, leftCam);
            float2 leftAt = Scene.Pixel(frame, Scene.PalmCentre(leftCam));

            // Palm nearer than 5 cm: on the lens.
            JointSet tooNear = Scene.Hand(HandSide.Right, new float3(0.01f, -0.01f, 0.04f));
            Assert.That(Scene.PalmDepth(tooNear), Is.LessThan(0.05f));
            CheckSkipped(frame, left, leftAt, Scene.Tracked(frame, tooNear), "palm nearer than 0.05 m");

            // A fingertip behind the camera, the palm in front of it: the hand's picture is not a picture.
            JointSet pointingAtYou = Scene.Hand(HandSide.Right, new float3(0.02f, -0.02f, 0.17f), 0f, -90f);
            Assert.That(pointingAtYou[HandJoint.MiddleTip].z, Is.LessThan(0f));
            Assert.That(Scene.PalmDepth(pointingAtYou), Is.GreaterThan(0.05f));
            CheckSkipped(frame, left, leftAt, Scene.Tracked(frame, pointingAtYou), "a joint behind the camera");

            // Joints that are not numbers.
            TrackedHand broken = Scene.Tracked(frame, Scene.Hand(HandSide.Right, RightWrist));
            JointSet nan = broken.World;
            nan[HandJoint.IndexTip] = new float3(float.NaN, 0f, 1f);
            broken.World = nan;
            CheckSkipped(frame, left, leftAt, broken, "NaN joints");

            // A hand wholly outside the picture, with no forearm in it, masks nothing: there is no mask.
            JointSet outside = Scene.Hand(HandSide.Right, new float3(0.9f, 0f, Reach));
            CheckSkipped(frame, left, leftAt, Scene.Tracked(frame, outside), "outside the picture");

            // But a hand above the picture, its forearm coming down into it, does mask something.
            JointSet aboveWithArm = Scene.Hand(HandSide.Right, new float3(0.0f, 0.4f, Reach));
            Assert.That(BuildOne(frame, Scene.Tracked(frame, aboveWithArm)).Count, Is.EqualTo(1), "the forearm comes down into the picture");
        }

        /// <summary>A right hand that cannot be masked gives nothing alone, and does not take the left hand's mask with it.</summary>
        static void CheckSkipped(in DepthFrame frame, in TrackedHand left, float2 leftAt, in TrackedHand skipped, string why)
        {
            Assert.That(new HandsMaskBuilder().Build(10.0, frame, default(TrackedHand), skipped).IsEmpty, Is.True, why);
            DepthMask both = new HandsMaskBuilder().Build(10.0, frame, left, skipped);
            Assert.That(both.Count, Is.EqualTo(1), why + ": the other hand is still masked");
            Assert.That(both.Region0.Contains(leftAt.x, leftAt.y, Reach), Is.True, why + ": and it is the left hand's region");
        }

        // ================================================================== allocation

        [Test]
        public void Hands_Mask_AllocatesNothing()
        {
            DepthFrame frame = Scene.Frame();
            JointSet leftCam = Scene.Hand(HandSide.Left, LeftWrist), rightCam = Scene.Hand(HandSide.Right, RightWrist);
            Scene.PaintHand(frame, leftCam, forearm: true);
            Scene.PaintHand(frame, rightCam, forearm: true);
            Scene.Lidar(frame);
            TrackedHand left = Scene.Tracked(frame, leftCam, 0.9f, true, 0f, 0.05f, new float3(0.3f, 0f, 0f));
            TrackedHand right = Scene.Tracked(frame, rightCam, 0.9f, true, 0f, 0.05f, new float3(-0.2f, 0.1f, 0f));
            TrackedHand gone = default(TrackedHand);
            DepthFrame nearWall = Scene.Frame(wall: 0.6f);   // drops the mask: the counting runs to its end
            DepthFrame invalid = default(DepthFrame);
            JointSet behind = Scene.Hand(HandSide.Right, new float3(0.02f, -0.02f, 0.17f), 0f, -90f);
            TrackedHand skipped = Scene.Tracked(frame, behind);
            var builder = new HandsMaskBuilder();
            int shape = 0;

            for (int i = 0; i < 60; i++) shape += Pass(builder, i, frame, nearWall, invalid, left, right, gone, skipped);   // warm up

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++) shape += Pass(builder, i, frame, nearWall, invalid, left, right, gone, skipped);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 1,200 builds");
            Assert.That(shape, Is.GreaterThan(0));
        }

        static int Pass(HandsMaskBuilder builder, int i, in DepthFrame frame, in DepthFrame nearWall, in DepthFrame invalid,
                        in TrackedHand left, in TrackedHand right, in TrackedHand gone, in TrackedHand skipped)
        {
            double now = 100.0 + i / 60.0;
            int count = 0;
            count += builder.Build(now, in frame, in left, in right).Count;       // two hands, counted
            count += builder.Build(now, in frame, in gone, in gone).Count;        // lingering from memory
            count += builder.Build(now, in nearWall, in left, in right).Count;    // over the limit, dropped
            count += builder.Build(now, in invalid, in left, in right).Count;     // no depth
            count += builder.Build(now, in frame, in skipped, in right).Count;    // one hand skipped
            count += builder.Build(now, in frame, in left, in gone).Count;        // one hand only
            return count + (builder.LastMaskedShare > 0f ? 1 : 0);
        }

        // ================================================================== helpers

        /// <summary>Distance from a point to a segment, pixels.</summary>
        static float DistanceToSegment(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float lengthSq = math.lengthsq(ab);
            float t = lengthSq > 1e-6f ? math.saturate(math.dot(p - a, ab) / lengthSq) : 0f;
            return math.distance(p, a + ab * t);
        }

        /// <summary>How fast the palm moves in the picture, pixels per second, by finite difference of its projection.</summary>
        static float PixelSpeed(in DepthFrame frame, in JointSet cam, float3 cameraVelocity)
        {
            float3 palm = Scene.PalmCentre(cam);
            const float dt = 1e-3f;
            return math.distance(Scene.Pixel(frame, palm), Scene.Pixel(frame, palm + cameraVelocity * dt)) / dt;
        }

        /// <summary>The box and the strip are wider than the still ones by exactly this on every side.</summary>
        static void AssertGrownBy(in DepthMaskRegion still, in DepthMaskRegion moving, float pixels, string what)
        {
            Assert.That(still.MinU - moving.MinU, Is.EqualTo(pixels).Within(0.05f), what + ": left edge");
            Assert.That(moving.MaxU - still.MaxU, Is.EqualTo(pixels).Within(0.05f), what + ": right edge");
            Assert.That(still.MinV - moving.MinV, Is.EqualTo(pixels).Within(0.05f), what + ": top edge");
            Assert.That(moving.MaxV - still.MaxV, Is.EqualTo(pixels).Within(0.05f), what + ": bottom edge");
            Assert.That(moving.ArmHalfWidth - still.ArmHalfWidth, Is.EqualTo(pixels).Within(0.05f), what + ": strip half width");
        }
    }
}
