using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.Spaces.Glued;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// Decision R15: the copies show the walker's hands on the kit avatar too. The rig picks the hands' level from the
    /// room the body leaves inside <see cref="AtlasMeshBudget.SelfAvatar"/>: full (17 boxes a hand, 408 triangles for
    /// both) when body + 408 fits, reduced (the forearm, the palm and one box from each finger's knuckle to its tip:
    /// 7 boxes, 84 triangles a hand, 168 for both) when body + 168 fits, none otherwise. Engine-free.
    /// </summary>
    public class GluedHandsLevelTests
    {
        static readonly float3 Head = new float3(0.35f, 1.6f, 0.2f);
        const float Heading = 0.7f;

        // A stand-in for a body of a chosen size: closed boxes of 12 triangles each, below the head, with no hand in it.
        internal static MeshData Body(int boxes)
        {
            var body = new MeshData(24 * boxes, 36 * boxes);
            float2 uv = AtlasPalette.Uv(AtlasPalette.AvatarBody);
            for (int i = 0; i < boxes; i++)
                body.AddBox(new float3(-0.1f + 0.02f * (i % 10), -0.7f + 0.02f * (i / 10), -0.08f), new float3(0.015f), 0f, uv, true);
            return body;
        }

        static int VerticesPerHand(GluedHandsLevel level)
        {
            return GluedAvatarHands.BoxesAt(level) * GluedAvatarHands.VerticesPerBox;
        }

        // The palm's far end, as the full level has it: along the line from the wrist to the middle of the knuckles, as far as the farthest.
        static float3 PalmEnd(float3[] p)
        {
            float3 wrist = p[(int)HandJoint.Wrist];
            int[] knuckles = { (int)HandJoint.IndexMcp, (int)HandJoint.MiddleMcp, (int)HandJoint.RingMcp, (int)HandJoint.LittleMcp };
            float3 centre = float3.zero;
            foreach (int mcp in knuckles) centre += 0.25f * p[mcp];
            float3 along = math.normalize(centre - wrist);
            float far = 0f;
            foreach (int mcp in knuckles) far = math.max(far, math.dot(p[mcp] - wrist, along));
            return wrist + along * far;
        }

        // What a hand's boxes must be at any level: numbers, a unit normal on every vertex that points out of its box,
        // the one baked shade, no line, and the colour of the hand's arm.
        static void AssertBoxesAreSound(AtlasMeshBuilder data, int firstVertex, int boxes, HandSide side, string label)
        {
            float2 colour = AtlasPalette.Uv(side == HandSide.Left ? AtlasPalette.AvatarLeft : AtlasPalette.AvatarRight);
            for (int box = 0; box < boxes; box++)
            {
                int first = firstVertex + box * GluedAvatarHands.VerticesPerBox;
                float3 middle = 0.5f * (GluedAvatarHandsTests.Mean(data, first) + GluedAvatarHandsTests.Mean(data, first + 4));
                for (int v = first; v < first + GluedAvatarHands.VerticesPerBox; v++)
                {
                    AtlasVertex vertex = data.Vertices[v];
                    string at = label + " box " + box + " vertex " + (v - first);
                    Assert.That(math.all(math.isfinite(vertex.Position)), Is.True, at);
                    Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(1e-4f), at + " normal");
                    Assert.That(vertex.LineWeight, Is.EqualTo(0), at);
                    Assert.That(vertex.Shade, Is.EqualTo(AtlasShading.ToByte(AtlasShading.FaceShade(vertex.Normal))), at + " shade");
                    Assert.That(math.all(vertex.Uv0 == colour), Is.True, at + " colour");
                    Assert.That(math.dot(vertex.Normal, vertex.Position - middle), Is.GreaterThan(0f), at + " faces into its box");
                }
            }
        }

        // Both hands of a rig at the reduced level, every box where the joints say: the palm from the wrist to the
        // knuckles, the forearm from the elbow to the wrist, and for each finger one box from its knuckle to its tip.
        static void AssertReducedHandsFollowTheJoints(MeshData body, string label)
        {
            foreach (float curl in new[] { 0f, 0.5f, 1f })
            {
                var rig = new GluedAvatarRig(body);
                Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Reduced), label);
                GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, curl, out float3[] left, out float3[] right);
                Assert.That(rig.Update(hands, Head, Heading), Is.True, label + " curl " + curl);
                AtlasMeshBuilder data = rig.Data;
                Assert.That(rig.HandCount, Is.EqualTo(2));

                int stride = GluedAvatarHands.ReducedVerticesPerHand;
                Assert.That(data.VertexCount, Is.EqualTo(rig.BodyVertexCount + 2 * stride), label);
                Assert.That(data.TriangleCount, Is.EqualTo(GluedBudget.AvatarBodyTriangles(body) + 2 * GluedAvatarHands.ReducedTrianglesPerHand), label);
                for (int side = 0; side < 2; side++)
                {
                    float3[] p = side == 0 ? left : right;
                    HandSide which = side == 0 ? HandSide.Left : HandSide.Right;
                    int hand = rig.BodyVertexCount + side * stride;
                    string at = label + " curl " + curl + (side == 0 ? " left " : " right ");
                    AssertBoxesAreSound(data, hand, GluedAvatarHands.ReducedBoxesPerHand, which, at);

                    // The palm: from the wrist to the knuckles. The forearm: from the elbow to the wrist.
                    GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, hand), p[(int)HandJoint.Wrist], at + "palm start");
                    GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, hand + 4), PalmEnd(p), at + "palm end");
                    int forearm = hand + GluedAvatarHands.VerticesPerBox;
                    GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, forearm), GluedAvatarMesh.Elbow(which), at + "elbow");
                    GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, forearm + 4), p[(int)HandJoint.Wrist], at + "wrist");

                    // Each finger is one box, from the joint it grows from to its tip, however far it is curled.
                    for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                    {
                        int box = hand + (2 + finger) * GluedAvatarHands.VerticesPerBox;
                        float3 knuckle = p[HandJoints.Base(finger)], tip = p[HandJoints.Tip(finger)];
                        GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, box), knuckle, at + "finger " + finger + " knuckle");
                        GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(data, box + 4), tip, at + "finger " + finger + " tip");

                        // As thick at the knuckle as the full finger, and as thick at the tip as the full finger's last segment ends.
                        float half = GluedAvatarHands.FingerHalfWidth(finger);
                        Assert.That(math.distance(data.Vertices[box].Position, data.Vertices[box + 1].Position), Is.EqualTo(2f * half).Within(1e-5f), at + "finger " + finger + " knuckle width");
                        Assert.That(math.distance(data.Vertices[box + 4].Position, data.Vertices[box + 5].Position),
                                    Is.EqualTo(2f * half * GluedAvatarHands.FingerTaper).Within(1e-5f), at + "finger " + finger + " tip width");
                    }
                }
                GluedBudgetTests.AssertFacesAgreeWithNormals(data, rig.BodyIndexCount);

                // The body is the one the rig was given, unchanged, and the bounds hold all of it.
                var bare = new AtlasMeshBuilder();
                GluedAvatarMesh.Build(bare, body);
                Assert.That(rig.BodyVertexCount, Is.EqualTo(bare.VertexCount), label);
                for (int v = 0; v < rig.BodyVertexCount; v++)
                    Assert.That(data.Vertices[v].Position, Is.EqualTo(bare.Vertices[v].Position), label + " body vertex " + v);
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int v = 0; v < data.VertexCount; v++)
                {
                    min = math.min(min, data.Vertices[v].Position);
                    max = math.max(max, data.Vertices[v].Position);
                }
                GluedAvatarHandsTests.AssertNear(data.BoundsMin, min, label + " bounds min");
                GluedAvatarHandsTests.AssertNear(data.BoundsMax, max, label + " bounds max");
            }
        }

        // A rig at the reduced level, rebuilt every frame and gained and lost over and over: no byte is allocated, and
        // the arrays the rig was built with are the ones that hold both hands, so not even the first hand allocates.
        static void AssertRebuildsAllocateNothing(MeshData body, string label)
        {
            var rig = new GluedAvatarRig(body);
            AtlasVertex[] vertices = rig.Data.Vertices;
            int[] indices = rig.Data.Indices;
            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            for (int i = 0; i < 10; i++)
            {
                hands.Joints[0][(int)HandJoint.IndexTip] += new float3(0.001f, 0f, 0f);
                rig.Update(hands, Head, Heading + 0.01f * i);
            }
            Assert.That(rig.HandCount, Is.EqualTo(2), label);
            Assert.That(rig.Data.Vertices, Is.SameAs(vertices), label + ": the vertex array grew when the hands came");
            Assert.That(rig.Data.Indices, Is.SameAs(indices), label + ": the index array grew when the hands came");

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

            Assert.That(rebuilt, Is.EqualTo(100), label);
            Assert.That(allocated, Is.EqualTo(0), label + ": bytes allocated by 200 frames of rebuilding");
        }

        // ---- The level is picked from the room the body leaves ------------------------------------------------

        [Test]
        public void TheLevel_IsPickedFromTheRoomTheBodyLeaves()
        {
            Assert.That(AtlasMeshBudget.SelfAvatar, Is.EqualTo(600));
            Assert.That(GluedAvatarRig.LevelFor(0), Is.EqualTo(GluedHandsLevel.Full));
            Assert.That(GluedAvatarRig.LevelFor(182), Is.EqualTo(GluedHandsLevel.Full), "182 + 408 = 590, the body of before the new look");
            Assert.That(GluedAvatarRig.LevelFor(192), Is.EqualTo(GluedHandsLevel.Full), "192 + 408 = 600 is the last body with both full hands");
            Assert.That(GluedAvatarRig.LevelFor(193), Is.EqualTo(GluedHandsLevel.Reduced));
            Assert.That(GluedAvatarRig.LevelFor(216), Is.EqualTo(GluedHandsLevel.Reduced), "the built-in body: 216 + 408 = 624 is over 600, 216 + 168 = 384 is not");
            Assert.That(GluedAvatarRig.LevelFor(356), Is.EqualTo(GluedHandsLevel.Reduced), "the kit avatar: 356 + 168 = 524");
            Assert.That(GluedAvatarRig.LevelFor(432), Is.EqualTo(GluedHandsLevel.Reduced), "432 + 168 = 600 is the last body with both reduced hands");
            Assert.That(GluedAvatarRig.LevelFor(433), Is.EqualTo(GluedHandsLevel.None));
            Assert.That(GluedAvatarRig.LevelFor(600), Is.EqualTo(GluedHandsLevel.None));
            Assert.That(GluedAvatarRig.LevelFor(5000), Is.EqualTo(GluedHandsLevel.None));

            // Whatever the body, what is drawn stays inside the budget, and the level only ever falls as the body grows.
            GluedHandsLevel before = GluedHandsLevel.Full;
            for (int body = 0; body <= 1200; body++)
            {
                GluedHandsLevel level = GluedAvatarRig.LevelFor(body);
                Assert.That((int)level, Is.LessThanOrEqualTo((int)before), "body " + body);
                before = level;
                if (body <= AtlasMeshBudget.SelfAvatar)
                    Assert.That(body + GluedAvatarHands.TrianglesAt(level), Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar), "body " + body);
                Assert.That(level == GluedHandsLevel.None, Is.EqualTo(body > 432), "body " + body);
            }
        }

        [Test]
        public void EachLevel_HasItsFigures_AndTheFullLevelIsTheOneOfBefore()
        {
            Assert.That(GluedAvatarHands.BoxesAt(GluedHandsLevel.Full), Is.EqualTo(17), "a palm, a forearm and five fingers of three segments");
            Assert.That(GluedAvatarHands.BoxesAt(GluedHandsLevel.Reduced), Is.EqualTo(7), "a palm, a forearm and one box for each finger");
            Assert.That(GluedAvatarHands.BoxesAt(GluedHandsLevel.None), Is.EqualTo(0));
            Assert.That(GluedAvatarHands.TrianglesAt(GluedHandsLevel.Full), Is.EqualTo(408), "both hands");
            Assert.That(GluedAvatarHands.TrianglesAt(GluedHandsLevel.Reduced), Is.EqualTo(168), "both hands");
            Assert.That(GluedAvatarHands.TrianglesAt(GluedHandsLevel.None), Is.EqualTo(0));
            Assert.That(GluedAvatarHands.TrianglesAt(GluedHandsLevel.Full), Is.EqualTo(GluedAvatarHands.MaxTriangles));
            Assert.That(GluedAvatarHands.ReducedBoxesPerHand, Is.EqualTo(7));
            Assert.That(GluedAvatarHands.ReducedTrianglesPerHand, Is.EqualTo(84));
            Assert.That(GluedAvatarHands.ReducedVerticesPerHand, Is.EqualTo(168));
            Assert.That(GluedAvatarHands.TrianglesPerHand, Is.EqualTo(204));
            Assert.That(GluedAvatarHands.VerticesPerHand, Is.EqualTo(408));
        }

        [Test]
        public void Append_AtEachLevel_AddsItsBoxes_AndTheFullLevelIsTheDefault()
        {
            float3[] joints = GluedHandPoses.InAvatarFrame(HandSide.Right, GluedHandPoses.Wrist(HandSide.Right), 0.5f);
            var data = new AtlasMeshBuilder();

            Assert.That(GluedAvatarHands.Append(data, HandSide.Right, joints, 1f, GluedHandsLevel.Reduced), Is.EqualTo(7));
            Assert.That(data.VertexCount, Is.EqualTo(168));
            Assert.That(data.TriangleCount, Is.EqualTo(84));

            data.Clear();
            Assert.That(GluedAvatarHands.Append(data, HandSide.Right, joints, 1f, GluedHandsLevel.None), Is.EqualTo(0));
            Assert.That(data.VertexCount, Is.EqualTo(0));
            Assert.That(data.IndexCount, Is.EqualTo(0));

            data.Clear();
            Assert.That(GluedAvatarHands.Append(data, HandSide.Right, joints, 1f), Is.EqualTo(17), "no level given: the full hand, as before");
            Assert.That(data.TriangleCount, Is.EqualTo(204));
            data.Clear();
            Assert.That(GluedAvatarHands.Append(data, HandSide.Right, joints, 1f, GluedHandsLevel.Full), Is.EqualTo(17));

            // A pose with no shape (every joint at the head) has one piece with a length at either level: the forearm, from the elbow.
            foreach (GluedHandsLevel level in new[] { GluedHandsLevel.Reduced, GluedHandsLevel.Full })
            {
                data.Clear();
                Assert.That(GluedAvatarHands.Append(data, HandSide.Left, new float3[HandJoints.Count], 1f, level), Is.EqualTo(1), level.ToString());
                Assert.That(data.TriangleCount, Is.EqualTo(12), level.ToString());
            }
        }

        // ---- The kit avatar shows both hands at the reduced level ----------------------------------------------

        [Test]
        public void WithTheShippedKitAvatar_BothHandsAppearAtTheReducedLevel_AndTheAvatarStaysWithin600()
        {
            MeshData kit = GluedKitTests.Shipped("avatar");
            Assert.That(kit.TriangleCount, Is.EqualTo(356), "the kit avatar of the manifest: the figures below follow from it");

            var rig = new GluedAvatarRig(kit);
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Reduced), "356 + 408 = 764 is over 600, 356 + 168 = 524 is not");
            Assert.That(rig.CanShowHands, Is.True);
            Assert.That(rig.BodyVertexCount, Is.EqualTo(kit.VertexCount));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(356), "no hand tracked yet: the kit avatar alone");

            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(2));
            Assert.That(rig.Shows(HandSide.Left), Is.True);
            Assert.That(rig.Shows(HandSide.Right), Is.True);
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(524), "356 of the body and 2 x 84 of the hands");
            Assert.That(rig.Data.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
            Assert.That(rig.Data.VertexCount, Is.EqualTo(kit.VertexCount + 2 * 168));
            Assert.That(rig.Data.IndexCount, Is.EqualTo(3 * 524));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles(kit)), "the accounting counts what is drawn");

            // One hand lost: the other stays. Both lost: the kit avatar alone goes back, once.
            hands.Lose(HandSide.Right);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(1));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(356 + 84));
            hands.Lose(HandSide.Left);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(356));
            Assert.That(rig.Data.VertexCount, Is.EqualTo(kit.VertexCount));
            Assert.That(rig.Update(hands, Head, Heading), Is.False);
        }

        [Test]
        public void TheReducedHands_FollowTheJoints_OnTheKitAvatar()
        {
            AssertReducedHandsFollowTheJoints(GluedKitTests.Shipped("avatar"), "kit avatar");
        }

        [Test]
        public void TheReducedHands_FollowTheJoints_OnAnyBodyThatLeavesRoomForThemOnly()
        {
            // A body of 360 triangles: 360 + 408 is over the budget, 360 + 168 is not.
            AssertReducedHandsFollowTheJoints(Body(30), "stand-in body of 360 triangles");
        }

        [Test]
        public void ACurledFinger_HasABoxFromItsKnuckleToItsTip_NotAlongItsBones()
        {
            float3[] joints = GluedHandPoses.InAvatarFrame(HandSide.Right, GluedHandPoses.Wrist(HandSide.Right), 1f);
            const int index = 1;
            float3 knuckle = joints[HandJoints.Base(index)], tip = joints[HandJoints.Tip(index)];
            float chain = 0f;
            for (int k = 0; k < 3; k++) chain += math.distance(joints[HandJoints.Base(index) + k], joints[HandJoints.Base(index) + k + 1]);
            Assert.That(math.distance(knuckle, tip), Is.LessThan(chain - 0.02f), "the finger is curled: its tip is nearer its knuckle than its bones are long");

            var reduced = new AtlasMeshBuilder();
            GluedAvatarHands.Append(reduced, HandSide.Right, joints, 1f, GluedHandsLevel.Reduced);
            int box = (2 + index) * GluedAvatarHands.VerticesPerBox;
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(reduced, box), knuckle, "reduced index finger: knuckle");
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(reduced, box + 4), tip, "reduced index finger: tip");
            // Not at the joints between: the end cap of the one box is the tip, not the middle of the finger.
            Assert.That(math.distance(GluedAvatarHandsTests.Mean(reduced, box + 4), joints[HandJoints.Base(index) + 2]), Is.GreaterThan(0.005f));

            // The same pose at the full level has the three boxes along the bones: the middle joints are where its boxes meet.
            var full = new AtlasMeshBuilder();
            GluedAvatarHands.Append(full, HandSide.Right, joints, 1f, GluedHandsLevel.Full);
            int first = (2 + 3 * index) * GluedAvatarHands.VerticesPerBox;
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(full, first + 4), joints[HandJoints.Base(index) + 1], "full index finger: first end");
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(full, first + GluedAvatarHands.VerticesPerBox + 4), joints[HandJoints.Base(index) + 2], "full index finger: second end");
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(full, first + 2 * GluedAvatarHands.VerticesPerBox + 4), tip, "full index finger: tip");

            // A finger that straightens again: the reduced box follows its tip to where the tip goes.
            float3[] open = GluedHandPoses.InAvatarFrame(HandSide.Right, GluedHandPoses.Wrist(HandSide.Right), 0f);
            reduced.Clear();
            GluedAvatarHands.Append(reduced, HandSide.Right, open, 1f, GluedHandsLevel.Reduced);
            GluedAvatarHandsTests.AssertNear(GluedAvatarHandsTests.Mean(reduced, box + 4), open[HandJoints.Tip(index)], "open index finger: tip");
        }

        // ---- The full level, and a body that leaves no room, behave as they did ---------------------------------

        [Test]
        public void WithTheBuiltInBody_BothHandsAppearAtTheReducedLevel_AndEveryCountHolds()
        {
            var rig = new GluedAvatarRig();
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Reduced), "216 + 408 = 624 is over 600, 216 + 168 = 384 is not");
            Assert.That(rig.CanShowHands, Is.True);
            Assert.That(rig.BodyVertexCount, Is.EqualTo(432));

            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(2));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(216 + 168));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(384));
            Assert.That(rig.Data.VertexCount, Is.EqualTo(432 + 336));
            Assert.That(rig.Data.IndexCount, Is.EqualTo(3 * 384));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles()));
            Assert.That(GluedBudget.AvatarBodyTriangles(), Is.EqualTo(216));
            Assert.That(GluedBudget.AvatarHandsTriangles(), Is.EqualTo(168));
            Assert.That(GluedBudget.AvatarTriangles(), Is.EqualTo(384));
            AssertRebuildsAllocateNothing(null, "built-in body");
        }

        [Test]
        public void TheReducedHands_FollowTheJoints_OnTheBuiltInBody()
        {
            AssertReducedHandsFollowTheJoints(null, "built-in body");
        }

        [Test]
        public void WithABodyThatLeavesRoomForThem_TheFullLevelIsUnchanged_AndEveryCountHolds()
        {
            // The full hands as they were before the body grew: a stand-in of 6 boxes, 72 triangles, then 72 + 408 = 480.
            MeshData body = GluedAvatarHandsTests.FullHandsBody();
            var rig = new GluedAvatarRig(body);
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.Full));
            Assert.That(rig.CanShowHands, Is.True);

            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.HandCount, Is.EqualTo(2));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(72 + 408));
            Assert.That(rig.Data.VertexCount, Is.EqualTo(body.VertexCount + 816));
            Assert.That(rig.Data.IndexCount, Is.EqualTo(3 * 480));
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles(body)));
            Assert.That(GluedBudget.AvatarBodyTriangles(body), Is.EqualTo(72));
            Assert.That(GluedBudget.AvatarHandsTriangles(body), Is.EqualTo(408));
            AssertRebuildsAllocateNothing(body, "body that leaves room for the full hands");
        }

        [Test]
        public void ABodyAtTheEdgeOfEachLevel_GetsThatLevel_AndTheAvatarFillsTheBudgetExactly()
        {
            // 16 boxes: 192 triangles, the last body that carries both full hands, to 600 exactly.
            AssertLevel(Body(16), GluedHandsLevel.Full, 192, 600);
            // 17 boxes: 204 triangles, one over: the reduced hands, 372.
            AssertLevel(Body(17), GluedHandsLevel.Reduced, 204, 204 + 168);
            // 36 boxes: 432 triangles, the last body that carries both reduced hands, to 600 exactly.
            AssertLevel(Body(36), GluedHandsLevel.Reduced, 432, 600);
            // 37 boxes: 444 triangles, no room: the body alone.
            AssertLevel(Body(37), GluedHandsLevel.None, 444, 444);
        }

        static void AssertLevel(MeshData body, GluedHandsLevel level, int bodyTriangles, int drawn)
        {
            string label = "body of " + bodyTriangles + " triangles";
            Assert.That(body.TriangleCount, Is.EqualTo(bodyTriangles), label);
            foreach (float curl in new[] { 0f, 1f })
            {
                var rig = new GluedAvatarRig(body);
                Assert.That(rig.Level, Is.EqualTo(level), label);
                Assert.That(rig.CanShowHands, Is.EqualTo(level != GluedHandsLevel.None), label);
                GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, curl, out _, out _);
                Assert.That(rig.Update(hands, Head, Heading), Is.EqualTo(level != GluedHandsLevel.None), label + " curl " + curl);
                Assert.That(rig.HandCount, Is.EqualTo(level == GluedHandsLevel.None ? 0 : 2), label);
                Assert.That(rig.Data.TriangleCount, Is.EqualTo(drawn), label + " curl " + curl);
                Assert.That(rig.Data.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar), label);
                Assert.That(GluedAvatarHands.TrianglesAt(level) + bodyTriangles, Is.EqualTo(drawn), label);
            }
        }

        [Test]
        public void ABodyThatLeavesNoRoom_IsDrawnWithoutHands_AndNothingIsEverUploaded()
        {
            MeshData body = Body(37);
            var rig = new GluedAvatarRig(body);
            Assert.That(rig.Level, Is.EqualTo(GluedHandsLevel.None));
            Assert.That(rig.CanShowHands, Is.False);
            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            for (int frame = 0; frame < 50; frame++)
                Assert.That(rig.Update(hands, Head + new float3(0.01f * frame, 0f, 0f), Heading + 0.01f * frame), Is.False, "frame " + frame);
            Assert.That(rig.HandCount, Is.EqualTo(0));
            Assert.That(rig.ShowsHands, Is.False);
            Assert.That(rig.Data.TriangleCount, Is.EqualTo(444));
            Assert.That(rig.Data.VertexCount, Is.EqualTo(body.VertexCount));
            Assert.That(rig.ShowBodyOnly(), Is.False, "there is nothing to take out");
            Assert.That(GluedBudget.AvatarHandsTriangles(body), Is.EqualTo(0));
            Assert.That(GluedBudget.AvatarTriangles(body), Is.EqualTo(444));
        }

        // ---- The accounting counts what the rig draws -----------------------------------------------------------

        [Test]
        public void TheAccounting_CountsWhatTheRigDraws_ForEveryLevel()
        {
            var bodies = new[] { null, Body(16), Body(17), Body(30), Body(36), Body(37) };
            var levels = new[]
            {
                GluedHandsLevel.Reduced, GluedHandsLevel.Full, GluedHandsLevel.Reduced, GluedHandsLevel.Reduced,
                GluedHandsLevel.Reduced, GluedHandsLevel.None,
            };
            for (int i = 0; i < bodies.Length; i++)
            {
                MeshData kit = bodies[i];
                int body = GluedBudget.AvatarBodyTriangles(kit);
                int hands = GluedBudget.AvatarHandsTriangles(kit);
                string label = kit == null ? "built-in body" : "body of " + body + " triangles";
                Assert.That(hands, Is.EqualTo(GluedAvatarHands.TrianglesAt(levels[i])), label);
                Assert.That(GluedBudget.AvatarTriangles(kit), Is.EqualTo(body + hands), label);
                Assert.That(GluedBudget.AvatarTriangles(kit), Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar), label);

                // What the rig draws with both hands, in any pose, is that figure and no other.
                foreach (float curl in new[] { 0f, 0.3f, 0.7f, 1f })
                {
                    var rig = new GluedAvatarRig(kit);
                    rig.Update(GluedAvatarHandsTests.BothHands(Head, Heading, curl, out _, out _), Head, Heading);
                    Assert.That(rig.Data.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles(kit)), label + " curl " + curl);
                }
            }
        }

        [Test]
        public void TheAccounting_OfTheKitAvatar_CountsTheBodyAndBothReducedHands()
        {
            MeshData kit = GluedKitTests.Shipped("avatar");
            Assert.That(GluedBudget.AvatarBodyTriangles(kit), Is.EqualTo(356));
            Assert.That(GluedBudget.AvatarHandsTriangles(kit), Is.EqualTo(168), "2 hands of 7 boxes of 12 triangles");
            Assert.That(GluedBudget.AvatarTriangles(kit), Is.EqualTo(524));
            Assert.That(GluedBudget.AvatarTriangles(kit), Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
        }

        [Test]
        public void TheLatticeBudgets_HoldWithTheKitPiecesAndTheirHands_InEveryRoomAndLayout()
        {
            MeshData landmark = GluedKitTests.Shipped("landmark"), avatar = GluedKitTests.Shipped("avatar");
            int worn = GluedBudget.AvatarTriangles(avatar), bare = GluedBudget.AvatarBodyTriangles(avatar);
            int hands = GluedBudget.AvatarHandsTriangles(avatar), prompt = GluedBudget.PromptTriangles();
            Assert.That(worn, Is.EqualTo(524));
            Assert.That(worn - bare, Is.EqualTo(hands));

            foreach (string name in new[] { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table, RoomFixtures.BudgetRoom })
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                RoomDrawCost shell = RoomBudget.Estimate(room, WallMode.Kerbs, false, true);
                foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
                {
                    var lattice = new GluedLattice(room, layout);
                    bool prompted = GluedPrompt.Available(in lattice, room.Standing);
                    int cell = GluedBudget.CellTriangles((float)lattice.SizeX, (float)lattice.SizeZ, landmark);
                    Assert.That(cell, Is.LessThanOrEqualTo(AtlasMeshBudget.GluedCell), name);
                    foreach (int copies in new[] { 3, 5, 7 })
                    {
                        string label = name + " " + layout + " " + copies + " x " + copies;
                        GluedDrawCost plain = GluedBudget.Lattice(layout, copies, cell, bare, true, prompted ? prompt : 0);
                        GluedDrawCost handed = GluedBudget.Lattice(layout, copies, cell, worn, true, prompted ? prompt : 0);

                        // The hands ride in the avatar's draw: no draw call more, and their triangles in every copy but the walker's own.
                        Assert.That(handed.DrawCallsPerFrame, Is.EqualTo(plain.DrawCallsPerFrame), label);
                        Assert.That(handed.TrianglesPerEye - plain.TrianglesPerEye, Is.EqualTo(hands * (GluedRules.CellCount(layout, copies) - 1)), label);

                        GluedDrawCost space = handed;
                        space.DrawCallsPerEye += shell.DrawCallsPerEye;
                        space.TrianglesPerEye += shell.TrianglesPerEye;
                        if (copies == 5 || layout == GluedLayout.Torus3)
                        {
                            Assert.That(GluedBudget.LatticeFits(layout, in handed), Is.True, label + ": the lattice row");
                            Assert.That(GluedBudget.SpaceFits(layout, in space), Is.True, label + ": the space row");
                        }
                        // The rungs of the thermal ladder at 7 x 7 still fit the frame's hard limits.
                        Assert.That(space.TrianglesPerFrame, Is.LessThanOrEqualTo(AtlasMeshBudget.FrameTriangles), label);
                        Assert.That(space.DrawCallsPerFrame, Is.LessThanOrEqualTo(300), label);
                        if (name == RoomFixtures.BudgetRoom && copies == 5)
                            TestContext.Out.WriteLine(label + ": cell " + cell + ", avatar " + worn + " (" + bare + " + " + hands
                                + "): lattice " + handed.TrianglesPerFrame + " triangles, with the room " + space.TrianglesPerFrame
                                + "; without the hands " + plain.TrianglesPerFrame);
                    }
                }
            }
        }

        [Test]
        public void AMirroredCopy_ShowsTheReducedHands_OnTheirOtherSide_OnTheKitAvatar()
        {
            // The hands are in the shared mesh once. Every copy shows them through its own transform, as GluedSpace sets
            // it (position and heading from GluedAvatarPose.InCell, a scale of -1 along X when mirrored): every fingertip
            // must land where the gluing puts the walker's real one, and the left hand must stay on the left arm's side.
            MeshData kit = GluedKitTests.Shipped("avatar");
            float2 leftCell = AtlasPalette.Uv(AtlasPalette.AvatarLeft);
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
            {
                var lattice = new GluedLattice(2.4, 3.4, layout);
                GluedCell walker = lattice.CellOf(Head);
                var rig = new GluedAvatarRig(kit);
                GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out float3[] left, out float3[] right);
                Assert.That(rig.Update(hands, Head, Heading), Is.True);
                AtlasMeshBuilder data = rig.Data;
                int stride = GluedAvatarHands.ReducedVerticesPerHand;

                var cells = new GluedCell[GluedRules.MaxCells];
                int count = lattice.FillCopies(walker, 5, cells);
                int mirroredCopies = 0;
                for (int c = 0; c < count; c++)
                {
                    GluedCell cell = cells[c];
                    if (cell.Equals(walker)) continue;
                    GluedAvatarPose.InCell(in lattice, walker, cell, Head, Heading, out float3 position, out float copyHeading, out bool mirrored);
                    if (mirrored) mirroredCopies++;
                    string at = layout + " " + cell;

                    for (int side = 0; side < 2; side++)
                    {
                        float3[] p = side == 0 ? left : right;
                        int hand = rig.BodyVertexCount + side * stride;
                        for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                        {
                            int box = hand + (2 + finger) * GluedAvatarHands.VerticesPerBox;
                            float3 tip = GluedAvatarHandsTests.CopyPoint(GluedAvatarHandsTests.Mean(data, box + 4), position, copyHeading, mirrored);
                            float3 roomTip = Head + math.mul(quaternion.RotateY(Heading), p[HandJoints.Tip(finger)]);
                            GluedAvatarHandsTests.AssertNear(tip, lattice.ImageOf(walker, cell, roomTip, out _), at + " side " + side + " finger " + finger, 1e-4f);
                        }
                    }

                    // The left hand stays with the left arm: on the same side of the copy as the kit avatar's left arm, and
                    // that side is the other one in a mirrored copy.
                    quaternion turn = quaternion.RotateY(copyHeading);
                    float3 copyRight = math.mul(turn, new float3(1f, 0f, 0f));
                    float3 copyHead = GluedAvatarHandsTests.CopyPoint(float3.zero, position, copyHeading, mirrored);
                    float arm = 0f;
                    for (int v = 0; v < rig.BodyVertexCount; v++)
                    {
                        if (!math.all(data.Vertices[v].Uv0 == leftCell)) continue;
                        arm += math.dot(GluedAvatarHandsTests.CopyPoint(data.Vertices[v].Position, position, copyHeading, mirrored) - copyHead, copyRight);
                    }
                    Assert.That(Math.Sign(arm), Is.EqualTo(mirrored ? 1 : -1), at + ": the kit avatar's left arm side");
                    for (int v = rig.BodyVertexCount; v < rig.BodyVertexCount + stride; v++)
                    {
                        float sideOfCopy = math.dot(GluedAvatarHandsTests.CopyPoint(data.Vertices[v].Position, position, copyHeading, mirrored) - copyHead, copyRight);
                        Assert.That(Math.Sign(sideOfCopy), Is.EqualTo(mirrored ? 1 : -1), at + ": left hand vertex " + v);
                    }
                }
                Assert.That(mirroredCopies > 0, Is.EqualTo(layout == GluedLayout.Klein), layout + " has mirrored copies only as a Klein bottle");
            }
        }

        // ---- Nothing allocates per frame ------------------------------------------------------------------------

        [Test]
        public void ARebuildEveryFrame_AllocatesNothing_AtTheReducedLevel_OnTheKitAvatar()
        {
            AssertRebuildsAllocateNothing(GluedKitTests.Shipped("avatar"), "kit avatar");
        }

        [Test]
        public void ARebuildEveryFrame_AllocatesNothing_AtTheReducedLevel_OnAStandInBody()
        {
            AssertRebuildsAllocateNothing(Body(30), "stand-in body of 360 triangles");
        }

        [Test]
        public void AReducedHand_IsNotRebuiltWhileItStaysStill_AndIsWhenAJointOrTheHeadOrTheScaleMoves()
        {
            var rig = new GluedAvatarRig(Body(30));
            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.2f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);
            Assert.That(rig.Update(hands, Head, Heading), Is.False, "the same joints, the same head");

            hands.Joints[1][(int)HandJoint.IndexTip] += new float3(0f, 0.001f, 0f);
            Assert.That(rig.Update(hands, Head, Heading), Is.True, "a fingertip moved a millimetre");
            Assert.That(rig.Update(hands, Head, Heading), Is.False);
            Assert.That(rig.Update(hands, Head, Heading + 0.01f), Is.True, "the walker turned");
            hands.Poses[0].Scale = 1.2f;
            Assert.That(rig.Update(hands, Head, Heading + 0.01f), Is.True, "the hand's size was measured");
        }

        [Test]
        public void TheHands_FollowTheHead_NotTheRoom_AtTheReducedLevel()
        {
            var rig = new GluedAvatarRig(Body(30));
            GluedFakeHands near = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(near, Head, Heading), Is.True);
            var first = new AtlasVertex[rig.Data.VertexCount];
            Array.Copy(rig.Data.Vertices, first, first.Length);

            float3 head2 = new float3(-2.2f, 0.9f, 4.1f);
            float heading2 = -1.9f;
            GluedFakeHands far = GluedAvatarHandsTests.BothHands(head2, heading2, 0.3f, out _, out _);
            rig.Update(far, head2, heading2);
            Assert.That(rig.Data.VertexCount, Is.EqualTo(first.Length));
            for (int v = 0; v < first.Length; v++)
                GluedAvatarHandsTests.AssertNear(rig.Data.Vertices[v].Position, first[v].Position, "vertex " + v, 2e-5f);
        }

        // ---- The forearm's start on the kit avatar's arm ---------------------------------------------------------

        [Test]
        public void TheForearm_StartsAtTheSameElbow_AndThatSitsInsideTheKitAvatarsArm()
        {
            MeshData kit = GluedKitTests.Shipped("avatar");
            var rig = new GluedAvatarRig(kit);
            GluedFakeHands hands = GluedAvatarHandsTests.BothHands(Head, Heading, 0.3f, out _, out _);
            Assert.That(rig.Update(hands, Head, Heading), Is.True);

            foreach (HandSide side in new[] { HandSide.Left, HandSide.Right })
            {
                // The kit's arm on this side: every vertex in the colour of the side's arm.
                float2 colour = AtlasPalette.Uv(side == HandSide.Left ? AtlasPalette.AvatarLeft : AtlasPalette.AvatarRight);
                float3 min = new float3(float.MaxValue), max = new float3(float.MinValue);
                for (int v = 0; v < kit.VertexCount; v++)
                {
                    if (!math.all(kit.Vertices[v].Uv0 == colour)) continue;
                    min = math.min(min, kit.Vertices[v].Position);
                    max = math.max(max, kit.Vertices[v].Position);
                }
                float3 elbow = GluedAvatarMesh.Elbow(side);
                Assert.That(elbow, Is.EqualTo(new float3(side == HandSide.Left ? -0.235f : 0.235f, -0.68f, -0.08f)), "the elbow is where it was");
                Assert.That(elbow.x, Is.InRange(min.x, max.x), side + " elbow x");
                Assert.That(elbow.y, Is.InRange(min.y, max.y), side + " elbow y");
                Assert.That(elbow.z, Is.InRange(min.z, max.z), side + " elbow z");

                // The forearm's first cap is centred on it.
                int hand = rig.BodyVertexCount + (int)side * GluedAvatarHands.ReducedVerticesPerHand;
                float3 start = GluedAvatarHandsTests.Mean(rig.Data, hand + GluedAvatarHands.VerticesPerBox);
                GluedAvatarHandsTests.AssertNear(start, elbow, side + " forearm start");

                // How far its corners stand from the kit arm's bounds, metres (positive: outside).
                float3 over = float3.zero;
                for (int v = hand + GluedAvatarHands.VerticesPerBox; v < hand + GluedAvatarHands.VerticesPerBox + 4; v++)
                {
                    float3 p = rig.Data.Vertices[v].Position;
                    over = math.max(over, math.max(min - p, p - max));
                }
                TestContext.Out.WriteLine(side + ": kit arm x " + min.x + " to " + max.x + ", y " + min.y + " to " + max.y + ", z " + min.z
                    + " to " + max.z + "; elbow " + elbow + "; the forearm's first cap lies " + over + " outside those bounds");
            }
        }
    }
}
