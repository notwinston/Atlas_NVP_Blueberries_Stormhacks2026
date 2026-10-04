using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;   // not Unity.Mathematics.Random

namespace Atlas.Hands.Tests
{
    /// <summary>The plane through a palm's LiDAR samples (documents/ATLAS_Hands_Design.md section 4.3, step 2).</summary>
    public class HandsLiftPalmPlaneTests
    {
        /// <summary>The depth samples a hand offers the fit: HandsSynth's 28 over the palm and the five under the wrist and the knuckles.</summary>
        const int SynthSamples = 28 + 5;

        static JointSet Hand(float3x3 orientation, float3 palmCentre)
        {
            return HandsLiftFixture.Place(HandsLiftFixture.Shape(HandsLiftShape.Open, HandSide.Right), orientation, palmCentre);
        }

        static float DegreesBetween(float3 a, float3 b)
        {
            return math.degrees(math.acos(math.clamp(math.dot(math.normalize(a), math.normalize(b)), -1f, 1f)));
        }

        [Test]
        public void PalmPlane_FindsATiltedPalm()
        {
            var random = new Random(1);
            HandsDetection detection = default(HandsDetection);
            foreach (float3x3 facing in new[] { HandsLiftFixture.BackToCamera, HandsLiftFixture.PalmToCamera })
            {
                float3x3 orientation = HandsLiftFixture.Turned(facing, 25f, -15f, 30f);
                JointSet truth = Hand(orientation, new float3(0.04f, -0.03f, 0.45f));
                HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f);

                PalmPlaneFit fit = PalmPlane.Fit(in detection, in detection.Hand0);
                Assert.That(fit.Valid, Is.True);
                Assert.That(fit.Samples, Is.EqualTo(SynthSamples));
                Assert.That(math.length(fit.Normal), Is.EqualTo(1f).Within(1e-4f));
                Assert.That(math.dot(fit.Normal, fit.Point), Is.LessThan(0f), "the normal points to the camera's side");
                // The palm frame's Y axis is the palm's normal: the fit's is that line, whichever face is shown.
                float3 palmNormal = orientation.c1;
                Assert.That(DegreesBetween(fit.Normal, palmNormal.z < 0f ? palmNormal : -palmNormal), Is.LessThan(0.2f));
                Assert.That(math.abs(math.dot(palmNormal, fit.Point - truth[HandJoint.Wrist])), Is.LessThan(2e-4f), "the centre lies on the palm");
                Assert.That(fit.Distance, Is.EqualTo(fit.Point.z));
                Assert.That(fit.Distance, Is.EqualTo(0.45f).Within(0.03f));
            }
        }

        [Test]
        public void PalmPlane_DropsWhatIsNotPalm()
        {
            var random = new Random(2);
            HandsDetection detection = default(HandsDetection);
            float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, -20f, 10f, 0f);
            JointSet truth = Hand(orientation, new float3(0f, 0f, 0.5f));
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f);
            float3 palmCentre = HandsLiftFixture.PalmCentre(in truth);
            float2 centre = detection.Project(palmCentre);

            // The wall seen between two fingers, and two samples on a sleeve 4 cm nearer than the palm.
            for (int i = 0; i < 4; i++) detection.Hand0.AddPalmSample(centre.x + 6f * i, centre.y - 5f * i, 2.1f);
            foreach (float2 pixel in new[] { centre + new float2(-20f, 30f), centre + new float2(15f, 35f) })
            {
                // Where the pixel's ray meets the palm, then 4 cm towards the camera.
                float3 ray = detection.Unproject(pixel, 1f);
                float onPalm = math.dot(orientation.c1, palmCentre) / math.dot(orientation.c1, ray);
                detection.Hand0.AddPalmSample(pixel.x, pixel.y, onPalm - 0.04f);
            }

            PalmPlaneFit fit = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(fit.Valid, Is.True);
            Assert.That(fit.Samples, Is.EqualTo(SynthSamples), "all six are dropped and nothing else");
            Assert.That(DegreesBetween(fit.Normal, -orientation.c1), Is.LessThan(0.2f));
            Assert.That(math.abs(math.dot(orientation.c1, fit.Point - truth[HandJoint.Wrist])), Is.LessThan(2e-4f));
        }

        [Test]
        public void PalmPlane_IsValidOnlyWhereLidarIsTrusted()
        {
            var random = new Random(3);
            HandsDetection detection = default(HandsDetection);
            foreach (float distance in new[] { 0.34f, 0.60f, 0.88f })
            {
                JointSet truth = Hand(HandsLiftFixture.BackToCamera, new float3(0f, 0f, distance));
                HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f);
                Assert.That(PalmPlane.Fit(in detection, in detection.Hand0).Valid, Is.True, distance + " m");
            }
            // Farther than 0.90 m: a plane is found, and it is not a hand LiDAR is trusted on.
            JointSet far = Hand(HandsLiftFixture.BackToCamera, new float3(0f, 0f, 0.95f));
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in far, HandSide.Right, random, 0f, 0f);
            PalmPlaneFit beyond = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(beyond.Valid, Is.False);
            Assert.That(beyond.Distance, Is.GreaterThan(HandsConstants.DepthMaxMetres));

            // Nearer than 0.32 m, were LiDAR to report it at all.
            JointSet near = Hand(HandsLiftFixture.BackToCamera, new float3(0f, 0f, 0.31f));
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in near, HandSide.Right, random, 0f, 0f);
            PalmPlaneFit before = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(before.Samples, Is.GreaterThanOrEqualTo(HandsConstants.PalmMinSamples));
            Assert.That(before.Valid, Is.False);

            // No depth in the frame.
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in near, HandSide.Right, random, 0f, 0f, palmDepth: false, jointDepth: false);
            PalmPlaneFit none = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(none.Valid, Is.False);
            Assert.That(none.Samples, Is.EqualTo(0));
        }

        [Test]
        public void PalmPlane_NeedsTwelveSamples()
        {
            var random = new Random(4);
            HandsDetection detection = default(HandsDetection);
            JointSet truth = Hand(HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, 10f, 10f, 0f), new float3(0f, 0f, 0.45f));

            // The palm's own samples alone: the depth under the joints is taken away.
            HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random, 0f, 0f, jointDepth: false);
            Assert.That(detection.Hand0.PalmCount, Is.EqualTo(28));
            detection.Hand0.PalmCount = HandsConstants.PalmMinSamples;
            PalmPlaneFit enough = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(enough.Valid, Is.True);
            Assert.That(enough.Samples, Is.EqualTo(HandsConstants.PalmMinSamples));

            detection.Hand0.PalmCount = HandsConstants.PalmMinSamples - 1;
            PalmPlaneFit few = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(few.Valid, Is.False);
            Assert.That(few.Samples, Is.EqualTo(HandsConstants.PalmMinSamples - 1));
        }

        [Test]
        public void PalmPlane_SamplesInALineFixNoPlane()
        {
            HandsDetection detection = default(HandsDetection);
            HandsLiftFixture.Begin(ref detection, 1, 0.0);
            detection.HandCount = 1;
            for (int i = 0; i < 20; i++) detection.Hand0.AddPalmSample(500f + 6f * i, 300f + 3f * i, 0.45f + 0.002f * i);
            PalmPlaneFit fit = PalmPlane.Fit(in detection, in detection.Hand0);
            Assert.That(fit.Valid, Is.False);

            // Samples that are no depth at all are passed over.
            detection.Hand0.PalmCount = 0;
            for (int i = 0; i < 20; i++) detection.Hand0.AddPalmSample(500f + 9f * i, 300f + 40f * (i % 4), i % 2 == 0 ? 0f : float.NaN);
            Assert.That(PalmPlane.Fit(in detection, in detection.Hand0).Samples, Is.EqualTo(0));
        }

        [Test]
        public void PalmPlane_WithACentimetreOfNoise()
        {
            var random = new Random(5);
            HandsDetection detection = default(HandsDetection);
            const int Trials = 300;
            float[] tilt = new float[Trials], distance = new float[Trials];
            for (int trial = 0; trial < Trials; trial++)
            {
                float3x3 orientation = HandsLiftFixture.Turned(HandsLiftFixture.BackToCamera, HandsLiftFixture.Uniform(random, -40f, 40f),
                                                               HandsLiftFixture.Uniform(random, -40f, 40f), HandsLiftFixture.Uniform(random, -40f, 40f));
                JointSet truth = Hand(orientation, new float3(0f, 0f, 0.45f));
                HandsLiftFixture.Detect(ref detection, 1, 0.0, in truth, HandSide.Right, random);
                PalmPlaneFit fit = PalmPlane.Fit(in detection, in detection.Hand0);
                Assert.That(fit.Valid, Is.True);
                tilt[trial] = DegreesBetween(fit.Normal, -orientation.c1);
                distance[trial] = math.abs(math.dot(orientation.c1, fit.Point - truth[HandJoint.Wrist]));
            }
            float tilt95 = HandsLiftFixture.Percentile(tilt, Trials, 0.95f), distance95 = HandsLiftFixture.Percentile(distance, Trials, 0.95f);
            TestContext.WriteLine("1 cm of depth noise on 33 samples: tilt P95 {0:0.0} degrees, centre P95 {1:0.0} mm off the palm", tilt95, distance95 * 1000f);
            Assert.That(tilt95, Is.LessThan(18f));
            Assert.That(distance95, Is.LessThan(0.005f));
        }
    }
}
