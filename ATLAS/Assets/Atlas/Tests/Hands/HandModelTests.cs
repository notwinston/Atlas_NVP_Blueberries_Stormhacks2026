using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>The template hand the lift and the mesh share (documents/ATLAS_Hands_Design.md section 5.1).</summary>
    public class HandModelTests
    {
        [Test]
        public void RestPose_HasAdultProportions()
        {
            Assert.That(HandModel.HandLength, Is.InRange(0.17f, 0.21f));
            Assert.That(HandModel.PalmWidth, Is.InRange(0.055f, 0.085f));
            Assert.That(HandModel.PalmLength, Is.InRange(0.08f, 0.11f));
            for (int joint = 1; joint < HandJoints.Count; joint++)
                Assert.That(HandModel.BoneLength(joint), Is.GreaterThan(0.015f), "joint " + joint);
        }

        [Test]
        public void LeftHand_IsTheMirrorOfTheRight()
        {
            for (int joint = 0; joint < HandJoints.Count; joint++)
            {
                float3 right = HandModel.Rest(HandSide.Right, joint);
                float3 left = HandModel.Rest(HandSide.Left, joint);
                Assert.That(left.x, Is.EqualTo(-right.x).Within(1e-6f));
                Assert.That(left.y, Is.EqualTo(right.y).Within(1e-6f));
                Assert.That(left.z, Is.EqualTo(right.z).Within(1e-6f));
            }
        }

        [Test]
        public void Pose_KeepsEveryBoneLength_AndCurlsTowardsThePalm()
        {
            JointSet open = HandModel.Pose(HandSide.Right, float4.zero, 0f);
            JointSet fist = HandModel.Pose(HandSide.Right, new float4(1f, 1f, 1f, 1f), 1f);
            for (int joint = 1; joint < HandJoints.Count; joint++)
            {
                int parent = HandJoints.Parent(joint);
                Assert.That(math.distance(fist[joint], fist[parent]), Is.EqualTo(HandModel.BoneLength(joint)).Within(1e-4f), "joint " + joint);
                Assert.That(math.distance(open[joint], HandModel.Rest(HandSide.Right, joint)), Is.LessThan(1e-5f), "joint " + joint);
            }
            // A curled index fingertip is on the palm's side (+Y) of the open one and nearer the wrist.
            Assert.That(fist[HandJoint.IndexTip].y, Is.GreaterThan(open[HandJoint.IndexTip].y + 0.02f));
            Assert.That(math.length(fist[HandJoint.IndexTip]), Is.LessThan(math.length(open[HandJoint.IndexTip])));
        }

        [Test]
        public void JointTree_HasFiveChainsOfFour()
        {
            Assert.That(HandJoints.Parent((int)HandJoint.Wrist), Is.EqualTo(-1));
            Assert.That(HandJoints.Parent((int)HandJoint.ThumbCmc), Is.EqualTo(0));
            Assert.That(HandJoints.Parent((int)HandJoint.IndexMcp), Is.EqualTo(0));
            Assert.That(HandJoints.Parent((int)HandJoint.IndexTip), Is.EqualTo((int)HandJoint.IndexDip));
            Assert.That(HandJoints.Base(4), Is.EqualTo((int)HandJoint.LittleMcp));
            Assert.That(HandJoints.Tip(0), Is.EqualTo((int)HandJoint.ThumbTip));
            Assert.That(HandJoints.FingerOf((int)HandJoint.RingDip), Is.EqualTo(3));
        }

        [Test]
        public unsafe void Detection_HasThePluginsSize_AndRoundTripsItsFields()
        {
            Assert.That(sizeof(DetectedHand), Is.EqualTo(1288));
            Assert.That(sizeof(HandsDetection), Is.EqualTo(HandsDetection.NativeSize));

            var detection = new HandsDetection { Fx = 880f, Fy = 880f, Cx = 640f, Cy = 360f };
            var sample = new JointSample { Pixel = new float2(700f, 300f), Confidence = 0.9f, Depth = 0.45f, DepthSpread = 0.01f, DepthConfidence = 2 };
            detection.Hand1.SetJoint((int)HandJoint.IndexTip, in sample);
            JointSample back = detection.Hand1.GetJoint((int)HandJoint.IndexTip);
            Assert.That(back.Pixel.x, Is.EqualTo(700f));
            Assert.That(back.DepthConfidence, Is.EqualTo(2));
            Assert.That(detection.Hand1.AddPalmSample(650f, 380f, 0.44f), Is.True);
            Assert.That(detection.Hand1.GetPalmSample(0).z, Is.EqualTo(0.44f));

            float3 point = detection.Unproject(sample.Pixel, 0.45f);
            float2 pixel = detection.Project(point);
            Assert.That(pixel.x, Is.EqualTo(700f).Within(1e-3f));
            Assert.That(pixel.y, Is.EqualTo(300f).Within(1e-3f));
            Assert.That(point.y, Is.GreaterThan(0f), "a pixel above the centre is up in camera space");

            detection.CameraToSession = float4x4.Translate(new float3(1f, 2f, 3f));
            Assert.That(detection.CameraToSession.c3.xyz, Is.EqualTo(new float3(1f, 2f, 3f)));
        }
    }
}
