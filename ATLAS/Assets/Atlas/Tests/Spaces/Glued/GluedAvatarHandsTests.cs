using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>A scripted IHands: the test writes the poses and the joints, the service answers with them.</summary>
    internal sealed class GluedFakeHands : IHands
    {
        public readonly HandPose[] Poses = new HandPose[2];
        public readonly float3[][] Joints = { new float3[HandJoints.Count], new float3[HandJoints.Count] };
        TwoHandPinch _twoHand;

        public bool Active { get; set; }
        public ref readonly HandPose Pose(HandSide side) { return ref Poses[(int)side]; }
        public float3 Joint(HandSide side, HandJoint joint) { return Joints[(int)side][(int)joint]; }
        public ref readonly TwoHandPinch TwoHand { get { return ref _twoHand; } }
        public void HideThisFrame() { }

        /// <summary>Marks a hand tracked at full opacity with these joints (room frame, metres).</summary>
        public void Track(HandSide side, float3[] joints, float scale = 1f)
        {
            Array.Copy(joints, Joints[(int)side], HandJoints.Count);
            Poses[(int)side] = new HandPose { Tracked = true, Confidence = 1f, Opacity = 1f, Scale = scale };
        }

        /// <summary>The hand is lost: its pose is no longer tracked.</summary>
        public void Lose(HandSide side)
        {
            Poses[(int)side] = default(HandPose);
        }
    }

    /// <summary>Hands as a walker holds them, for the Glued tests: an average adult hand, open or curled, placed in the avatar's frame.</summary>
    internal static class GluedHandPoses
    {
        // Where each finger's base sits in the palm frame (+Z wrist to knuckles, +Y out of the palm, +X thumb side
        // for a right hand), the way it points when straight, and its three bone lengths. Thumb first. Metres.
        static readonly float3[] Base =
        {
            new float3(0.024f, 0.008f, 0.022f), new float3(0.032f, 0f, 0.090f), new float3(0.010f, 0f, 0.094f),
            new float3(-0.011f, 0f, 0.088f), new float3(-0.031f, 0f, 0.078f),
        };
        static readonly float3[] Direction =
        {
            math.normalize(new float3(0.70f, 0.20f, 0.68f)), math.normalize(new float3(0.10f, 0f, 1f)),
            math.normalize(new float3(0.02f, 0f, 1f)), math.normalize(new float3(-0.06f, 0f, 1f)),
            math.normalize(new float3(-0.14f, 0f, 1f)),
        };
        static readonly float3[] Bones =
        {
            new float3(0.044f, 0.033f, 0.028f), new float3(0.043f, 0.025f, 0.022f), new float3(0.047f, 0.029f, 0.024f),
            new float3(0.043f, 0.027f, 0.023f), new float3(0.034f, 0.019f, 0.021f),
        };

        /// <summary>The 21 joints of a hand in its own palm frame, every finger bent towards the palm by `curl` (0 straight, 1 fist-like).</summary>
        public static float3[] Local(HandSide side, float curl)
        {
            var joints = new float3[HandJoints.Count];
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                float3 limits = finger == 0 ? math.radians(new float3(35f, 45f, 60f)) : math.radians(new float3(80f, 100f, 70f));
                float3 direction = Direction[finger];
                float3 axis = math.normalizesafe(math.cross(direction, new float3(0f, 1f, 0f)), new float3(1f, 0f, 0f));
                float3 position = Base[finger];
                int joint = HandJoints.Base(finger);
                joints[joint] = Mirror(side, position);
                float angle = 0f;
                for (int bone = 0; bone < 3; bone++)
                {
                    angle += curl * limits[bone];
                    position += math.mul(quaternion.AxisAngle(axis, angle), direction) * Bones[finger][bone];
                    joints[joint + 1 + bone] = Mirror(side, position);
                }
            }
            return joints;
        }

        /// <summary>The hand in the avatar's frame: palm down, fingers forward, the wrist at the given point.</summary>
        public static float3[] InAvatarFrame(HandSide side, float3 wrist, float curl)
        {
            float3[] joints = Local(side, curl);
            quaternion palmDown = quaternion.RotateZ(math.PI);
            for (int j = 0; j < joints.Length; j++) joints[j] = wrist + math.mul(palmDown, joints[j]);
            return joints;
        }

        /// <summary>The avatar-frame joints as the room frame has them for a walker with this head and heading.</summary>
        public static float3[] InRoom(float3[] avatarJoints, float3 head, float heading)
        {
            var joints = new float3[avatarJoints.Length];
            quaternion turn = quaternion.RotateY(heading);
            for (int j = 0; j < joints.Length; j++) joints[j] = head + math.mul(turn, avatarJoints[j]);
            return joints;
        }

        /// <summary>Where a walker holds the wrist of each hand, in the avatar's frame: a hand to each side, below the head, in front.</summary>
        public static float3 Wrist(HandSide side)
        {
            return new float3(side == HandSide.Left ? -0.22f : 0.22f, -0.35f, 0.30f);
        }

        static float3 Mirror(HandSide side, float3 p)
        {
            return side == HandSide.Left ? new float3(-p.x, p.y, p.z) : p;
        }
    }

    /// <summary>
    /// The copies' hands in the Glued rooms (documents/ATLAS_Hands_Design.md section 5.6): the shared avatar mesh also
    /// holds the walker's two hands and forearms, in the avatar's own frame, so every copy shows them through its own
    /// transform. Engine-free: the mesh data is checked here, the upload in GluedHandsUnityTests.
    /// </summary>
    public class GluedAvatarHandsTests
    {
        static readonly float3 Head = new float3(0.35f, 1.6f, 0.2f);
        const float Heading = 0.7f;

        // What the tracked head looks like: turned by the heading and tilted, so the heading has to be taken from it as GluedSpace does.
        static quaternion HeadRotation(float heading) { return math.mul(quaternion.RotateY(heading), quaternion.RotateX(0.3f)); }

        /// <summary>A fake that tracks both hands at the given avatar-frame joints for this head and heading.</summary>
        internal static GluedFakeHands BothHands(float3 head, float heading, float curl, out float3[] left, out float3[] right)
        {
            left = GluedHandPoses.InAvatarFrame(HandSide.Left, GluedHandPoses.Wrist(HandSide.Left), curl);
            right = GluedHandPoses.InAvatarFrame(HandSide.Right, GluedHandPoses.Wrist(HandSide.Right), curl);
            var hands = new GluedFakeHands();
            hands.Track(HandSide.Left, GluedHandPoses.InRoom(left, head, heading));
            hands.Track(HandSide.Right, GluedHandPoses.InRoom(right, head, heading));
            return hands;
        }

        internal static float3 Mean(AtlasMeshBuilder data, int first)
        {
            return 0.25f * (data.Vertices[first].Position + data.Vertices[first + 1].Position
                            + data.Vertices[first + 2].Position + data.Vertices[first + 3].Position);
        }

        internal static void AssertNear(float3 actual, float3 expected, string what, float tolerance = 1e-5f)
        {
            Assert.That(math.length(actual - expected), Is.LessThan(tolerance), what + ": " + actual + " against " + expected);
        }

        // A body that leaves room for the full hands (a few boxes, no hand in it): the built-in body (216 triangles) leaves
        // room only for the reduced ones, so the tests of the three-bone fingers give the rig this one instead.
        internal static MeshData FullHandsBody() { return GluedHandsLevelTests.Body(6); }

        // The rig's body as it stands before any hand: the same vertices and indices as GluedAvatarMesh.Build gives.
        static void AssertIsTodaysBody(AtlasMeshBuilder data, MeshData kit = null)
        {
            var fresh = new AtlasMeshBuilder();
            GluedAvatarMesh.Build(fresh, kit);
            Assert.That(data.VertexCount, Is.EqualTo(fresh.VertexCount));
            for (int v = 0; v < fresh.VertexCount; v++)
            {
                AtlasVertex a = data.Vertices[v], b = fresh.Vertices[v];
                Assert.That(math.all(a.Position == b.Position) && math.all(a.Normal == b.Normal) && a.Color == b.Color
                            && math.all(a.Uv0 == b.Uv0) && math.all(a.Uv1 == b.Uv1), Is.True, "vertex " + v);
            }
            Assert.That(data.IndexCount, Is.EqualTo(fresh.IndexCount));
            for (int i = 0; i < fresh.IndexCount; i++) Assert.That(data.Indices[i], Is.EqualTo(fresh.Indices[i]), "index " + i);
        }

        // The built-in body's own figures: the neon cube head, the visor and the two controller sticks, 18 boxes.
        static void AssertIsTheBuiltInBody(AtlasMeshBuilder data)
        {
            Assert.That(data.VertexCount, Is.EqualTo(432));
            Assert.That(data.IndexCount, Is.EqualTo(648));
            Assert.That(data.TriangleCount, Is.EqualTo(216));
            AssertIsTodaysBody(data);
        }

        [Test]
        public void WithNoService_TheMeshIsTodaysBody_AndNothingIsUploaded()
        {
            var rig = new GluedAvatarRig();
            AssertIsTheBuiltInBody(rig.Data);
            Assert.That(rig.BodyVertexCount, Is.EqualTo(432));
            Assert.That(rig.HandCount, Is.EqualTo(0));
            for (int frame = 0; frame < 100; frame++)
                Assert.That(rig.Update(null, Head + new float3(0.01f * frame, 0f, 0f), 0.01f * frame), Is.False, "frame " + frame);
            AssertIsTodaysBody(rig.Data);
            Assert.That(rig.HandCount, Is.EqualTo(0));
        }

        [Test]
        public void WithAServiceButNoTrackedHand_TheMeshIsTodaysBody_AndNothingIsUploaded()
        {
            var rig = new GluedAvatarRig();
            var hands = new GluedFakeHands { Active = true };
            // A hand that is not tracked has joints that mean nothing: they must not be read as a hand.
            for (int j = 0; j < HandJoints.Count; j++) hands.Joints[0][j] = hands.Joints[1][j] = new float3(0.1f * j, 1f, 0.5f);
            for (int frame = 0; frame < 100; frame++)
                Assert.That(rig.Update(hands, Head, 0.01f * frame), Is.False, "frame " + frame);
            AssertIsTheBuiltInBody(rig.Data);
        }

        [Test]
        public void TwoTrackedHands_StayWithinTheAvatarBudget_AndTheGeometryIsSound()
        {
            MeshData body = FullHandsBody();
            var rig = new GluedAvatarRig(body);
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Full));
            foreach (float curl in new[] { 0f, 0.5f, 1f })
            {
                GluedFakeHands hands = BothHands(Head, Heading, curl, out _, out _);
                Assert.That(rig.Update(hands, Head, Heading), Is.True, "curl " + curl);
                AtlasMeshBuilder data = rig.Data;

                Assert.That(rig.HandCount, Is.EqualTo(2));
                Assert.That(data.TriangleCount, Is.EqualTo(body.TriangleCount + 2 * GluedAvatarHands.TrianglesPerHand));
                Assert.That(data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles(body)), "the accounting counts what is built");
                Assert.That(data.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
                Assert.That(data.VertexCount, Is.EqualTo(body.VertexCount + 2 * GluedAvatarHands.VerticesPerHand));
                Assert.That(data.IndexCount, Is.EqualTo(3 * data.TriangleCount));
                GluedBudgetTests.AssertFacesAgreeWithNormals(data);

                // Every vertex is a number, every normal a unit vector, nothing is lit by a line, and the bounds hold all of it.
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int v = 0; v < data.VertexCount; v++)
                {
                    AtlasVertex vertex = data.Vertices[v];
                    Assert.That(math.all(math.isfinite(vertex.Position)), Is.True, "vertex " + v);
                    Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(1e-4f), "normal " + v);
                    Assert.That(vertex.LineWeight, Is.EqualTo(0), "vertex " + v);
                    Assert.That(vertex.Color >> 16, Is.EqualTo(0xFFFFu), "colour bytes B and A of vertex " + v);
                    // The shade is the look's one baked light on the vertex's normal, as every mesh of the look has it.
                    Assert.That(vertex.Shade, Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShade(vertex.Normal))), "shade of vertex " + v);
                    min = math.min(min, vertex.Position);
                    max = math.max(max, vertex.Position);
                }
                for (int i = 0; i < data.IndexCount; i++) Assert.That(data.Indices[i], Is.InRange(0, data.VertexCount - 1), "index " + i);
                AssertNear(data.BoundsMin, min, "bounds min");
                AssertNear(data.BoundsMax, max, "bounds max");

                // Faces agreeing with their normals does not say which way round the box is: every normal must point out
                // of its box, away from the middle of the piece, or a hand is seen inside out (the material culls back faces).
                for (int box = 0; box < 2 * GluedAvatarHands.BoxesPerHand; box++)
                {
                    int first = rig.BodyVertexCount + box * GluedAvatarHands.VerticesPerBox;
                    float3 middle = 0.5f * (Mean(data, first) + Mean(data, first + 4));
                    for (int v = first; v < first + GluedAvatarHands.VerticesPerBox; v++)
                        Assert.That(math.dot(data.Vertices[v].Normal, data.Vertices[v].Position - middle), Is.GreaterThan(0f),
                                    "box " + box + " vertex " + (v - first) + " faces into its box");
                }
            }
        }

        [Test]
        public void OnTheBuiltInBody_TwoTrackedHandsAreTheReducedOnes_AndTheAvatarIs384()
        {
            var rig = new GluedAvatarRig();
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Reduced), "216 + 408 is over 600, 216 + 168 is not");
            foreach (float curl in new[] { 0f, 1f })
            {
                GluedFakeHands hands = BothHands(Head, Heading, curl, out _, out _);
                Assert.That(rig.Update(hands, Head, Heading), Is.True, "curl " + curl);
                Assert.That(rig.HandCount, Is.EqualTo(2));
                Assert.That(rig.Data.TriangleCount, Is.EqualTo(216 + 168));
                Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles()));
                Assert.That(rig.Data.VertexCount, Is.EqualTo(432 + 2 * GluedAvatarHands.ReducedVerticesPerHand));
                Assert.That(rig.Data.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
                GluedBudgetTests.AssertFacesAgreeWithNormals(rig.Data, 3 * 216);
            }
        }

        [Test]
        public void TheGeometryOfAHand_RunsFromJointToJoint_WhereIHandsSaysTheyAre_InTheAvatarFrame()
        {
            // The hand is placed in the avatar's frame and moved into the room by the head and the heading, so a
            // wrong turn, a wrong sign or a missing offset puts every joint somewhere else.
            foreach (float heading in new[] { 0.7f, -2.4f, 3.0f, 0f })
            {
                float3 head = new float3(-1.4f, 1.1f, 2.3f);
                var rig = new GluedAvatarRig(FullHandsBody());
                GluedFakeHands hands = BothHands(head, heading, 0.4f, out float3[] left, out float3[] right);
                Assert.That(GluedAvatarPose.Heading(HeadRotation(heading)), Is.EqualTo(heading).Within(1e-5f));
                Assert.That(rig.Update(hands, head, GluedAvatarPose.Heading(HeadRotation(heading))), Is.True);

                for (int side = 0; side < 2; side++)
                {
                    float3[] p = side == 0 ? left : right;
                    int hand = rig.BodyVertexCount + side * GluedAvatarHands.VerticesPerHand;
                    AtlasMeshBuilder data = rig.Data;
                    string label = "heading " + heading + (side == 0 ? " left " : " right ");

                    // Every finger: three segments, each from its joint to the next.
                    for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                    {
                        for (int k = 0; k < 3; k++)
                        {
                            int box = hand + (2 + 3 * finger + k) * GluedAvatarHands.VerticesPerBox;
                            AssertNear(Mean(data, box), p[HandJoints.Base(finger) + k], label + "finger " + finger + " segment " + k + " start");
                            AssertNear(Mean(data, box + 4), p[HandJoints.Base(finger) + k + 1], label + "finger " + finger + " segment " + k + " end");
                        }
                    }

                    // The forearm runs from the avatar's elbow to the wrist.
                    int forearm = hand + GluedAvatarHands.VerticesPerBox;
                    AssertNear(Mean(data, forearm), GluedAvatarMesh.Elbow(side == 0 ? HandSide.Left : HandSide.Right), label + "elbow");
                    AssertNear(Mean(data, forearm + 4), p[(int)HandJoint.Wrist], label + "wrist");

                    // The palm starts at the wrist and ends on the line to the knuckles, as far as the farthest of them.
                    float3 wrist = p[(int)HandJoint.Wrist];
                    int[] knuckles = { (int)HandJoint.IndexMcp, (int)HandJoint.MiddleMcp, (int)HandJoint.RingMcp, (int)HandJoint.LittleMcp };
                    float3 centre = float3.zero;
                    foreach (int mcp in knuckles) centre += 0.25f * p[mcp];
                    float3 along = math.normalize(centre - wrist);
                    float far = 0f;
                    foreach (int mcp in knuckles) far = math.max(far, math.dot(p[mcp] - wrist, along));
                    AssertNear(Mean(data, hand), wrist, label + "palm start");
                    AssertNear(Mean(data, hand + 4), wrist + along * far, label + "palm end");
                }
            }
        }

        [Test]
        public void TheHands_TakeTheColoursOfTheirArms_AndOnlyTheirOwnHandIsAdded()
        {
            float2 leftCell = AtlasPalette.Uv(AtlasPalette.AvatarLeft), rightCell = AtlasPalette.Uv(AtlasPalette.AvatarRight);
            GluedFakeHands both = BothHands(Head, Heading, 0f, out float3[] left, out float3[] right);

            var rig = new GluedAvatarRig(FullHandsBody());
            Assert.That(rig.Update(both, Head, Heading), Is.True);
            int body = rig.BodyVertexCount;
            for (int v = body; v < body + GluedAvatarHands.VerticesPerHand; v++)
                Assert.That(math.all(rig.Data.Vertices[v].Uv0 == leftCell), Is.True, "left hand vertex " + v);
            for (int v = body + GluedAvatarHands.VerticesPerHand; v < rig.Data.VertexCount; v++)
                Assert.That(math.all(rig.Data.Vertices[v].Uv0 == rightCell), Is.True, "right hand vertex " + v);

            // Only the left hand tracked: only the left hand's geometry follows the body.
            both.Lose(HandSide.Right);
            Assert.That(rig.Update(both, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(1));
            Assert.That(rig.Shows(HandSide.Left), Is.True);
            Assert.That(rig.Shows(HandSide.Right), Is.False);
            Assert.That(rig.Data.VertexCount, Is.EqualTo(body + GluedAvatarHands.VerticesPerHand));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(rig.BodyIndexCount / 3 + GluedAvatarHands.TrianglesPerHand));
            for (int v = body; v < rig.Data.VertexCount; v++)
                Assert.That(math.all(rig.Data.Vertices[v].Uv0 == leftCell), Is.True, "vertex " + v);

            // Only the right hand tracked: it is the first and only one appended.
            both.Track(HandSide.Right, GluedHandPoses.InRoom(right, Head, Heading));
            both.Lose(HandSide.Left);
            Assert.That(rig.Update(both, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(1));
            Assert.That(rig.Shows(HandSide.Right), Is.True);
            for (int v = body; v < rig.Data.VertexCount; v++)
                Assert.That(math.all(rig.Data.Vertices[v].Uv0 == rightCell), Is.True, "vertex " + v);
            GluedBudgetTests.AssertFacesAgreeWithNormals(rig.Data);
            Assert.That(left.Length, Is.EqualTo(HandJoints.Count));
        }

        [Test]
        public void LosingTheHands_GivesBackTodaysBody_OnceAndThenNothingIsUploaded()
        {
            var rig = new GluedAvatarRig();
            GluedFakeHands hands = BothHands(Head, Heading, 0f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Data.VertexCount, Is.GreaterThan(rig.BodyVertexCount));

            hands.Lose(HandSide.Left);
            hands.Lose(HandSide.Right);
            Assert.That(rig.Update(hands, Head, Heading), Is.True, "the plain body goes back once");
            AssertIsTodaysBody(rig.Data);
            for (int frame = 0; frame < 100; frame++)
                Assert.That(rig.Update(hands, Head, Heading), Is.False, "frame " + frame);
            AssertIsTodaysBody(rig.Data);

            // And the service going away altogether is the same as the hands being lost.
            hands = BothHands(Head, Heading, 0f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Update(null, Head, Heading), Is.True);
            AssertIsTodaysBody(rig.Data);
            Assert.That(rig.Update(null, Head, Heading), Is.False);
        }

        [Test]
        public void AHandThatHasNotMoved_IsNotRebuilt_AndOneThatHasIs()
        {
            var rig = new GluedAvatarRig();
            GluedFakeHands hands = BothHands(Head, Heading, 0.2f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Update(hands, Head, Heading), Is.False, "the same joints, the same head");

            hands.Joints[1][(int)HandJoint.IndexTip] += new float3(0f, 0.001f, 0f);
            Assert.That(rig.Update(hands, Head, Heading), Is.True, "a fingertip moved a millimetre");
            Assert.That(rig.Update(hands, Head, Heading), Is.False);

            Assert.That(rig.Update(hands, Head, Heading + 0.01f), Is.True, "the walker turned: the hand is turned back by the new heading");
            Assert.That(rig.Update(hands, Head + new float3(0f, 0.01f, 0f), Heading + 0.01f), Is.True, "the head moved while the hand stayed");
            hands.Poses[0].Scale = 1.2f;
            Assert.That(rig.Update(hands, Head + new float3(0f, 0.01f, 0f), Heading + 0.01f), Is.True, "the hand's size was measured");
        }

        [Test]
        public void TheHands_FollowTheHead_NotTheRoom()
        {
            // The walker walks and turns with the hands held still relative to the head: the avatar's hands do not change.
            var rig = new GluedAvatarRig();
            GluedFakeHands near = BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(near, Head, Heading), Is.True);
            var first = new AtlasVertex[rig.Data.VertexCount];
            Array.Copy(rig.Data.Vertices, first, first.Length);

            float3 head2 = new float3(-2.2f, 0.9f, 4.1f);
            float heading2 = -1.9f;
            GluedFakeHands far = BothHands(head2, heading2, 0.3f, out _, out _);
            rig.Update(far, head2, heading2);
            Assert.That(rig.Data.VertexCount, Is.EqualTo(first.Length));
            for (int v = 0; v < first.Length; v++)
                AssertNear(rig.Data.Vertices[v].Position, first[v].Position, "vertex " + v, 2e-5f);
        }

        [Test]
        public void ATrackedHandWithAJointThatIsNotANumber_IsNotDrawn_AndTheOtherIs()
        {
            var rig = new GluedAvatarRig();
            GluedFakeHands hands = BothHands(Head, Heading, 0f, out _, out _);
            hands.Joints[0][(int)HandJoint.MiddleTip] = new float3(float.NaN, 1f, 1f);
            hands.Joints[1][(int)HandJoint.LittleDip] = new float3(1f, float.PositiveInfinity, 1f);
            rig.Update(hands, Head, Heading);
            Assert.That(rig.HandCount, Is.EqualTo(0));
            AssertIsTodaysBody(rig.Data);

            hands.Joints[1][(int)HandJoint.LittleDip] = hands.Joints[1][(int)HandJoint.LittleTip];
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Shows(HandSide.Right), Is.True);
            Assert.That(rig.Shows(HandSide.Left), Is.False);
            Assert.That(math.all(math.isfinite(rig.Data.BoundsMin)) && math.all(math.isfinite(rig.Data.BoundsMax)), Is.True);

            // A head that is not a number gives joints that are not numbers: no hand, and no fault.
            Assert.DoesNotThrow(() => rig.Update(hands, new float3(float.NaN, 0f, 0f), Heading));
            AssertIsTodaysBody(rig.Data);
        }

        [Test]
        public void AHandWithNoShape_DoesNotBreakTheMesh()
        {
            // Every joint at the head, as a service that has nothing yet might report: pieces with no length are left out.
            var rig = new GluedAvatarRig();
            var hands = new GluedFakeHands();
            hands.Track(HandSide.Left, new float3[HandJoints.Count]);
            hands.Track(HandSide.Right, new float3[HandJoints.Count]);
            rig.Update(hands, float3.zero, 0f);
            Assert.That(rig.Data.TriangleCount, Is.LessThanOrEqualTo(GluedBudget.AvatarTriangles()));
            for (int v = 0; v < rig.Data.VertexCount; v++)
            {
                Assert.That(math.all(math.isfinite(rig.Data.Vertices[v].Position)), Is.True, "vertex " + v);
                Assert.That(math.all(math.isfinite(rig.Data.Vertices[v].Normal)), Is.True, "normal " + v);
            }
            for (int i = 0; i < rig.Data.IndexCount; i++) Assert.That(rig.Data.Indices[i], Is.InRange(0, rig.Data.VertexCount - 1));
        }

        [Test]
        public void TheHandsScale_WithTheWalkersHand_AndAScaleOfNothingMeansTheTemplate()
        {
            var rig = new GluedAvatarRig(FullHandsBody());
            GluedFakeHands hands = BothHands(Head, Heading, 0f, out _, out _);
            rig.Update(hands, Head, Heading);
            int tip = rig.BodyVertexCount + (2 + 3 * 1 + 2) * GluedAvatarHands.VerticesPerBox + 4;     // the end cap of the index fingertip
            float one = math.length(rig.Data.Vertices[tip].Position - Mean(rig.Data, tip));

            hands.Poses[0].Scale = 1.5f;
            rig.Update(hands, Head, Heading);
            float big = math.length(rig.Data.Vertices[tip].Position - Mean(rig.Data, tip));
            Assert.That(big, Is.EqualTo(1.5f * one).Within(1e-4f), "the finger is thicker, the joints are where they were");

            hands.Poses[0].Scale = 0f;
            rig.Update(hands, Head, Heading);
            Assert.That(math.length(rig.Data.Vertices[tip].Position - Mean(rig.Data, tip)), Is.EqualTo(one).Within(1e-5f));
            hands.Poses[0].Scale = float.NaN;
            Assert.DoesNotThrow(() => rig.Update(hands, Head, Heading));
        }

        [Test]
        public void AMirroredCopy_ShowsTheLeftHand_OnItsOtherSide_WithNoExtraWork()
        {
            // The hands are in the mesh once. Every copy shows them through its own transform, as GluedSpace sets it
            // (position and heading from GluedAvatarPose.InCell, a scale of -1 along X when mirrored), and that must put
            // every point of the hands where the gluing puts the walker's real one.
            float2 leftCell = AtlasPalette.Uv(GluedAvatarMesh.LeftHand);
            // The side of the walker's left is read off the built-in body's left controller, the hand-held thing on that side.
            var look = new AtlasMeshBuilder();
            GluedAvatarMesh.Build(look);
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
            {
                var lattice = new GluedLattice(2.4, 3.4, layout);
                GluedCell walker = lattice.CellOf(Head);
                float heading = GluedAvatarPose.Heading(HeadRotation(Heading));
                var rig = new GluedAvatarRig(FullHandsBody());
                GluedFakeHands hands = BothHands(Head, heading, 0.3f, out float3[] left, out float3[] right);
                Assert.That(rig.Update(hands, Head, heading), Is.True);
                AtlasMeshBuilder data = rig.Data;

                var cells = new GluedCell[GluedRules.MaxCells];
                int count = lattice.FillCopies(walker, 5, cells);
                int mirroredCopies = 0;
                for (int c = 0; c < count; c++)
                {
                    GluedCell cell = cells[c];
                    if (cell.Equals(walker)) continue;
                    GluedAvatarPose.InCell(in lattice, walker, cell, Head, heading, out float3 position, out float copyHeading, out bool mirrored);
                    Assert.That(mirrored, Is.EqualTo(lattice.Between(walker, cell).Mirror));
                    if (mirrored) mirroredCopies++;

                    // Every segment end of both hands lands on the image of the walker's own joint in that cell.
                    for (int side = 0; side < 2; side++)
                    {
                        float3[] p = side == 0 ? left : right;
                        int hand = rig.BodyVertexCount + side * GluedAvatarHands.VerticesPerHand;
                        for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                        {
                            int box = hand + (2 + 3 * finger + 2) * GluedAvatarHands.VerticesPerBox;
                            float3 tip = CopyPoint(Mean(data, box + 4), position, copyHeading, mirrored);
                            float3 roomTip = Head + math.mul(quaternion.RotateY(heading), p[HandJoints.Tip(finger)]);
                            AssertNear(tip, lattice.ImageOf(walker, cell, roomTip, out _), layout + " " + cell + " side " + side + " finger " + finger, 1e-4f);
                        }
                    }

                    // The left hand stays with the left arm: on the same side of the copy as the avatar's left controller,
                    // and that side is the other one in a mirrored copy.
                    quaternion turn = quaternion.RotateY(copyHeading);
                    float3 copyRight = math.mul(turn, new float3(1f, 0f, 0f));
                    float3 copyHead = CopyPoint(float3.zero, position, copyHeading, mirrored);
                    float shoulder = 0f;
                    for (int v = 0; v < look.VertexCount; v++)
                    {
                        if (!math.all(look.Vertices[v].Uv0 == leftCell)) continue;
                        shoulder += math.dot(CopyPoint(look.Vertices[v].Position, position, copyHeading, mirrored) - copyHead, copyRight);
                    }
                    Assert.That(Math.Sign(shoulder), Is.EqualTo(mirrored ? 1 : -1), layout + " " + cell + ": the left controller's side");
                    for (int v = rig.BodyVertexCount; v < rig.BodyVertexCount + GluedAvatarHands.VerticesPerHand; v++)
                    {
                        float sideOfCopy = math.dot(CopyPoint(data.Vertices[v].Position, position, copyHeading, mirrored) - copyHead, copyRight);
                        Assert.That(Math.Sign(sideOfCopy), Is.EqualTo(mirrored ? 1 : -1), layout + " " + cell + ": left hand vertex " + v);
                    }
                }
                Assert.That(mirroredCopies > 0, Is.EqualTo(layout == GluedLayout.Klein), layout + " has mirrored copies only as a Klein bottle");
            }
        }

        // Where a point of the avatar's mesh is in the room for a copy: the transform GluedSpace gives the copy's object.
        internal static float3 CopyPoint(float3 meshPoint, float3 position, float copyHeading, bool mirrored)
        {
            var flipped = new float3(mirrored ? -meshPoint.x : meshPoint.x, meshPoint.y, meshPoint.z);
            return position + math.mul(quaternion.RotateY(copyHeading), flipped);
        }

        [Test]
        public void ARebuildEveryFrame_AllocatesNothing()
        {
            var rig = new GluedAvatarRig();
            GluedFakeHands hands = BothHands(Head, Heading, 0.3f, out _, out _);
            for (int i = 0; i < 10; i++)
            {
                hands.Joints[0][(int)HandJoint.IndexTip] += new float3(0.001f, 0f, 0f);
                rig.Update(hands, Head, Heading + 0.01f * i);
            }

            int rebuilt = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                // Every joint and the heading move, so every call rebuilds.
                hands.Joints[0][(int)HandJoint.IndexTip] += new float3(0.0005f, 0f, 0f);
                hands.Joints[1][(int)HandJoint.ThumbTip] += new float3(0f, 0.0005f, 0f);
                if (rig.Update(hands, Head, Heading + 0.001f * i)) rebuilt++;
            }
            for (int i = 0; i < 100; i++) rig.Update(i % 2 == 0 ? null : hands, Head, Heading);   // tracked and lost, over and over
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(rebuilt, Is.EqualTo(100));
            Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 200 frames of rebuilding");
        }

        [Test]
        public void TheAccounting_CountsTheAvatarWithBothHands_ForEveryPose()
        {
            // The built-in body, 216 triangles, leaves room only for the reduced hands (decision R15): 216 + 168 = 384.
            Assert.That(GluedBudget.AvatarBodyTriangles(), Is.EqualTo(216));
            Assert.That(GluedBudget.AvatarHandsTriangles(), Is.EqualTo(2 * GluedAvatarHands.ReducedTrianglesPerHand));
            Assert.That(GluedBudget.AvatarTriangles(), Is.EqualTo(384));
            Assert.That(GluedBudget.AvatarTriangles(), Is.EqualTo(GluedBudget.AvatarBodyTriangles() + GluedBudget.AvatarHandsTriangles()));
            Assert.That(GluedBudget.AvatarTriangles(), Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));

            // A hand has the same shape of mesh in every pose: the figure is one number, not a bound that a pose can break.
            var rig = new GluedAvatarRig();
            foreach (float curl in new[] { 0f, 0.3f, 0.7f, 1f })
            {
                GluedFakeHands hands = BothHands(Head, Heading, curl, out _, out _);
                rig.Update(hands, Head, Heading);
                Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles()), "curl " + curl);
            }
            // A body that leaves room for the full hands is counted with them, 204 triangles a hand.
            MeshData small = FullHandsBody();
            Assert.That(GluedBudget.AvatarHandsTriangles(small), Is.EqualTo(2 * GluedAvatarHands.TrianglesPerHand));
            Assert.That(GluedBudget.AvatarTriangles(small), Is.EqualTo(small.TriangleCount + 408));
            Assert.That(GluedAvatarHands.TrianglesPerHand, Is.EqualTo(GluedAvatarHands.BoxesPerHand * 12));
            Assert.That(GluedAvatarHands.BoxesPerHand, Is.EqualTo(1 + 1 + 5 * 3), "a palm, a forearm and five fingers of three segments");
        }
    }
}
