using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using Random = System.Random;   // not Unity.Mathematics.Random

namespace Atlas.Hands.Tests
{
    /// <summary>The walker's own bone lengths (decision R11 of the hands brief) and the running median under them.</summary>
    public class HandsLiftBoneLengthsTests
    {
        const int AllJoints = (1 << HandJoints.Count) - 1;

        /// <summary>The open template hand with every finger's three bones multiplied by the given shares, flat in its palm frame.</summary>
        static JointSet Flat(float3 shares, float scale = 1f)
        {
            JointSet hand = HandModel.RestPose(HandSide.Right, scale);
            JointSet reshaped = hand;
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int first = HandJoints.Base(finger);
                for (int bone = 1; bone <= 3; bone++)
                    reshaped[first + bone] = reshaped[first + bone - 1] + (hand[first + bone] - hand[first + bone - 1]) * shares[bone - 1];
            }
            return reshaped;
        }

        /// <summary>The hand with one finger seen shorter by a share, as a curled or a tilted finger is in a picture.</summary>
        static JointSet Foreshortened(in JointSet hand, int finger, float share)
        {
            JointSet seen = hand;
            int first = HandJoints.Base(finger);
            for (int bone = 1; bone <= 3; bone++)
                seen[first + bone] = seen[first + bone - 1] + (hand[first + bone] - hand[first + bone - 1]) * share;
            return seen;
        }

        static JointSet Noisy(in JointSet hand, Random random, float metres)
        {
            JointSet noisy = hand;
            for (int joint = 0; joint < HandJoints.Count; joint++)
                noisy[joint] += new float3(HandsLiftFixture.Gaussian(random), 0f, HandsLiftFixture.Gaussian(random)) * metres;
            return noisy;
        }

        [Test]
        public void BoneLengths_AreTheTemplatesTimesTheScaleUntilLearnt()
        {
            var bones = new BoneLengths();
            Assert.That(bones.Length(0), Is.EqualTo(0f));
            Assert.That(bones.Length(-1), Is.EqualTo(0f));
            Assert.That(bones.Length(HandJoints.Count), Is.EqualTo(0f));
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                Assert.That(bones.Length(joint), Is.EqualTo(HandModel.BoneLength(joint)));
                Assert.That(bones.Learnt(joint), Is.False);
            }

            // One sighting short of enough: still the template, at the scale the lift has measured.
            JointSet walker = Flat(new float3(0.8f, 1.1f, 0.9f));
            for (int i = 0; i < BoneLengths.MinSightings - 1; i++) bones.Observe(in walker, AllJoints, 1.1f);
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                Assert.That(bones.Learnt(joint), Is.False);
                Assert.That(bones.Length(joint), Is.EqualTo(HandModel.BoneLength(joint) * 1.1f).Within(1e-6f));
            }
            bones.Observe(in walker, AllJoints, 1.1f);
            Assert.That(bones.Learnt((int)HandJoint.IndexPip), Is.True);
            Assert.That(bones.Length((int)HandJoint.IndexPip), Is.EqualTo(HandModel.BoneLength((int)HandJoint.IndexPip) * 0.8f).Within(1e-5f));

            bones.Reset();
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                Assert.That(bones.Learnt(joint), Is.False);
                Assert.That(bones.Length(joint), Is.EqualTo(HandModel.BoneLength(joint)), "the scale is forgotten with the walker");
            }
        }

        [Test]
        public void BoneLengths_LearnsAWalkersHand()
        {
            // The detector's points on a real hand: first bones a quarter shorter than the template's, middle
            // bones a tenth longer, the whole hand 8 % larger. Each sighting a millimetre out, a few of them wild.
            var random = new Random(12);
            var bones = new BoneLengths();
            float3 shares = new float3(0.75f, 1.10f, 0.95f);
            JointSet walker = Flat(shares, 1.08f);
            for (int i = 0; i < 60; i++)
            {
                JointSet seen = Noisy(in walker, random, 0.001f);
                if (i % 15 == 7) seen[(int)HandJoint.IndexTip] += new float3(0.03f, 0f, 0.02f);   // a bad detection
                bones.Observe(in seen, AllJoints, 1.08f);
            }
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                int bone = (joint - 1) % HandJoints.PerFinger;
                float truth = HandModel.BoneLength(joint) * 1.08f * (bone == 0 ? 1f : shares[bone - 1]);
                Assert.That(bones.Learnt(joint), Is.True, "joint " + joint);
                Assert.That(bones.Length(joint), Is.EqualTo(truth).Within(0.03f * truth), "joint " + joint);
            }
        }

        [Test]
        public void BoneLengths_AFingerSeenShortTeachesNothing()
        {
            var random = new Random(13);
            var bones = new BoneLengths();
            JointSet walker = Flat(new float3(1f, 1f, 1f));
            int pip = (int)HandJoint.IndexPip, tip = (int)HandJoint.IndexTip, middle = (int)HandJoint.MiddlePip;

            // First seen curled: the index finger at 60 % of its length, for a long time. Not taken for a short finger.
            JointSet curled = Foreshortened(in walker, 1, 0.6f);
            for (int i = 0; i < 300; i++) bones.Observe(Noisy(in curled, random, 0.001f), AllJoints, 1f);
            Assert.That(bones.Learnt(pip), Is.False);
            Assert.That(bones.Learnt(middle), Is.True, "the fingers seen at full length are learnt meanwhile");
            Assert.That(bones.Length(pip), Is.EqualTo(HandModel.BoneLength(pip)));

            // Then open: learnt.
            for (int i = 0; i < 40; i++) bones.Observe(Noisy(in walker, random, 0.001f), AllJoints, 1f);
            Assert.That(bones.Learnt(pip), Is.True);
            float learnt = bones.Length(pip), learntTip = bones.Length(tip);
            Assert.That(learnt, Is.EqualTo(HandModel.BoneLength(pip)).Within(0.001f));

            // Then curled again, for longer than the window of sightings: what was learnt stays.
            for (int i = 0; i < 500; i++) bones.Observe(Noisy(Foreshortened(in walker, 1, 0.3f + 0.6f * (i % 100) / 100f), random, 0.001f), AllJoints, 1f);
            Assert.That(bones.Length(pip), Is.EqualTo(learnt).Within(0.02f * learnt));
            Assert.That(bones.Length(tip), Is.EqualTo(learntTip).Within(0.03f * learntTip));

            // A longer look teaches at once what a shorter one never could: a bone can only look shorter than it is.
            JointSet longer = Flat(new float3(1.15f, 1.15f, 1.15f));
            for (int i = 0; i < BoneLengths.Window; i++) bones.Observe(Noisy(in longer, random, 0.001f), AllJoints, 1f);
            Assert.That(bones.Length(pip), Is.EqualTo(1.15f * HandModel.BoneLength(pip)).Within(0.001f));
        }

        [Test]
        public void BoneLengths_UnseenJointsTeachNothing()
        {
            var bones = new BoneLengths();
            JointSet walker = Flat(new float3(0.9f, 0.9f, 0.9f));
            int withoutRingTip = AllJoints & ~(1 << (int)HandJoint.RingTip);
            int withoutWrist = AllJoints & ~1;
            for (int i = 0; i < 40; i++) bones.Observe(in walker, withoutRingTip & withoutWrist, 1f);
            Assert.That(bones.Learnt((int)HandJoint.RingPip), Is.False, "a finger is learnt whole or not at all");
            Assert.That(bones.Learnt((int)HandJoint.RingDip), Is.False);
            Assert.That(bones.Learnt((int)HandJoint.RingMcp), Is.False, "no wrist, no palm bone");
            Assert.That(bones.Learnt((int)HandJoint.IndexDip), Is.True);

            // Sightings that are not numbers, or not a hand's, are passed over.
            var other = new BoneLengths();
            JointSet broken = walker;
            broken[(int)HandJoint.IndexTip] = new float3(float.NaN, 0f, 0f);
            broken[(int)HandJoint.MiddleTip] = new float3(3f, 0f, 3f);
            for (int i = 0; i < 40; i++) other.Observe(in broken, AllJoints, float.NaN);
            Assert.That(other.Learnt((int)HandJoint.IndexPip), Is.False);
            Assert.That(other.Learnt((int)HandJoint.MiddlePip), Is.False);
            Assert.That(other.Learnt((int)HandJoint.RingPip), Is.True);
            Assert.That(other.Length((int)HandJoint.IndexPip), Is.EqualTo(HandModel.BoneLength((int)HandJoint.IndexPip)), "a scale that is not a number is not taken");
        }

        [Test]
        public void RunningMedian_IsTheMiddleOfTheLastValues()
        {
            var median = new RunningMedian(5);
            Assert.That(median.Count, Is.EqualTo(0));
            Assert.That(median.Median, Is.EqualTo(0f));
            median.Add(3f);
            Assert.That(median.Median, Is.EqualTo(3f));
            median.Add(1f);
            Assert.That(median.Median, Is.EqualTo(2f), "the mean of the middle two of an even count");
            median.Add(2f);
            median.Add(float.NaN);
            median.Add(float.PositiveInfinity);
            Assert.That(median.Count, Is.EqualTo(3), "values that are not finite are ignored");
            Assert.That(median.Median, Is.EqualTo(2f));
            median.Add(10f);
            median.Add(11f);
            Assert.That(median.Median, Is.EqualTo(3f));   // 3 1 2 10 11
            Assert.That(median.Quantile(0.9f), Is.EqualTo(11f));
            Assert.That(median.Quantile(0.5f), Is.EqualTo(3f));
            Assert.That(median.Quantile(0f), Is.EqualTo(1f));

            // Full: each new value pushes the oldest out.
            median.Add(12f);                               // 1 2 10 11 12
            Assert.That(median.Count, Is.EqualTo(5));
            Assert.That(median.Median, Is.EqualTo(10f));
            median.Add(2f);                                // 2 10 11 12 2
            median.Add(2f);                                // 10 11 12 2 2
            Assert.That(median.Median, Is.EqualTo(10f));
            median.Add(2f);                                // 11 12 2 2 2
            Assert.That(median.Median, Is.EqualTo(2f));

            median.Clear();
            Assert.That(median.Count, Is.EqualTo(0));

            // Against a plain sort, over a long run with repeats.
            var random = new Random(4);
            var window = new float[5];
            for (int i = 0; i < 500; i++)
            {
                float value = random.Next(0, 9);
                median.Add(value);
                window[i % 5] = value;
                if (i < 4) continue;
                float[] sorted = (float[])window.Clone();
                System.Array.Sort(sorted);
                Assert.That(median.Median, Is.EqualTo(sorted[2]), "after " + (i + 1) + " values");
            }
        }
    }
}
