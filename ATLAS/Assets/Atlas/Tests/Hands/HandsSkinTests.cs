using System;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// Hands_Skin_Mesh and Hands_Skin_AllocatesNothing (documents/ATLAS_Hands_Design.md sections 5.2 and 9.1):
    /// the template posed on joints. The engine-free kernel is what runs here; the overloads that take native
    /// arrays wrap it and are tested in the Editor (HandsSkinUnityTests).
    /// </summary>
    public unsafe class HandsSkinTests
    {
        static readonly HandSide[] Sides = { HandSide.Right, HandSide.Left };
        static readonly float[] Scales = { HandsConstants.ScaleMin, 1f, HandsConstants.ScaleMax };

        // ---- What every skinned hand must be ----

        // No NaN, bounds that contain every vertex, unit normals, and the look's vertex: the Slicer's two-sided
        // shade in COLOR.r, no line, the palette coordinate passed in. Fails on the first vertex that is wrong.
        static void AssertSound(HandsMeshTemplate template, AtlasVertex[] vertices, float3 min, float3 max, float3 light, string what)
        {
            if (!HandsSkinRig.IsFinite(min) || !HandsSkinRig.IsFinite(max)) Assert.Fail(what + ": the bounds are not finite");
            if (!math.all(min <= max)) Assert.Fail(what + ": the bounds are the wrong way round");
            for (int i = 0; i < template.VertexCount; i++)
            {
                AtlasVertex vertex = vertices[i];
                if (!HandsSkinRig.IsFinite(vertex.Position)) Assert.Fail(what + ": the position of vertex " + i + " is not finite");
                if (!HandsSkinRig.IsFinite(vertex.Normal)) Assert.Fail(what + ": the normal of vertex " + i + " is not finite");
                if (!math.all(vertex.Position >= min) || !math.all(vertex.Position <= max))
                    Assert.Fail(what + ": vertex " + i + " at " + vertex.Position + " is outside the bounds " + min + " to " + max);
                if (math.abs(math.length(vertex.Normal) - 1f) > 1e-4f)
                    Assert.Fail(what + ": the normal of vertex " + i + " has length " + math.length(vertex.Normal));

                float shade = AtlasShading.ShadeFloor
                            + (1f - AtlasShading.ShadeFloor) * (0.5f + 0.5f * math.abs(math.dot(vertex.Normal, light)));
                if (vertex.Shade != AtlasShading.ToByte(shade)) Assert.Fail(what + ": the shade of vertex " + i + " is " + vertex.Shade);
                if (vertex.LineWeight != 0) Assert.Fail(what + ": vertex " + i + " carries a line");
                if (vertex.Color >> 16 != 0xFFFFu) Assert.Fail(what + ": the reserved channels of vertex " + i + " are not 255");
                if (!vertex.Uv0.Equals(HandsSkinRig.Uv)) Assert.Fail(what + ": vertex " + i + " has another colour");
                if (!vertex.Uv1.Equals(float2.zero)) Assert.Fail(what + ": vertex " + i + " has a line coordinate");
            }
        }

        // Every triangle is seen from outside with its corners running clockwise: its face looks the way its
        // vertex normals do. Returns the number of triangles with no area.
        static int AssertWoundOutwards(AtlasVertex[] vertices, int[] indices, int indexCount, float least, string what)
        {
            int flat = 0;
            for (int t = 0; t < indexCount; t += 3)
            {
                AtlasVertex a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                float3 face = math.cross(b.Position - a.Position, c.Position - a.Position);
                if (math.length(face) < 1e-10f)
                {
                    flat++;
                    continue;
                }
                float agreement = math.dot(math.normalize(face), math.normalize(a.Normal + b.Normal + c.Normal));
                if (!(agreement > least)) Assert.Fail(what + ": triangle " + t / 3 + " is inside out (" + agreement + ")");
            }
            return flat;
        }

        static float3 Centre(AtlasVertex[] vertices, int first, int count)
        {
            float3 sum = float3.zero;
            for (int i = 0; i < count; i++) sum += vertices[first + i].Position;
            return sum / count;
        }

        // ---- The open hand ----

        [Test]
        public void Hands_Skin_Mesh_TheOpenHand_IsFinite_InsideItsBounds_AndWoundOutwards()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                int[] indices = HandsSkinRig.Indices(template);
                JointSet joints = HandModel.RestPose(side);
                AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, out float3 min, out float3 max);
                AssertSound(template, vertices, min, max, AtlasShading.LightDirection, side + " open");

                // Outward winding: every face agrees with its normals, none is flat, and the whole encloses a
                // positive volume about the size of a hand.
                Assert.That(AssertWoundOutwards(vertices, indices, template.IndexCount, 0.5f, side + " open"), Is.EqualTo(0));
                float3 middle = 0.5f * (joints[HandJoint.Wrist] + joints[HandJoint.MiddleMcp]);
                double volume = HandsSkinRig.SignedVolume(vertices, indices, template.IndexCount, middle);
                Assert.That(volume, Is.InRange(150e-6, 450e-6), side + ": cubic metres");

                // Bounds are the joints grown by the largest radius: never tighter than the mesh, 5 cm at most.
                float3 lo = joints[0], hi = joints[0];
                for (int j = 1; j < HandJoints.Count; j++)
                {
                    lo = math.min(lo, joints[j]);
                    hi = math.max(hi, joints[j]);
                }
                Assert.That(template.Reach, Is.InRange(0.02f, 0.05f));
                Assert.That(math.cmax(math.abs(lo - template.Reach - min)), Is.LessThan(1e-5f));
                Assert.That(math.cmax(math.abs(hi + template.Reach - max)), Is.LessThan(1e-5f));

                // The hand is where the model says: each cap ends half a tip radius beyond its tip joint, the
                // palm is as thick as the model's palm, and behind the wrist there is a short cuff and no forearm.
                for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                {
                    HandsMeshTemplate.Ring last = template.Rings[finger * HandsMeshTemplate.FingerRings + 6];
                    int tip = HandJoints.Tip(finger);
                    float3 along = math.normalize(joints[tip] - joints[tip - 1]);
                    float3 apex = vertices[last.First + 2 * HandsConstants.FingerSides].Position;
                    Assert.That(math.distance(apex, joints[tip] + along * HandsMeshTemplate.TipOvershoot * HandModel.Radius(tip)),
                                Is.LessThan(1e-5f), side + ": cap of finger " + finger);
                }
                float highest = float.MinValue, lowest = float.MaxValue, back = float.MaxValue;
                for (int i = 0; i < template.VertexCount; i++)
                {
                    if (template.Frames[template.Vertices[i].Frame].Kind != HandsMeshTemplate.FrameKind.Anchor) continue;
                    highest = math.max(highest, vertices[i].Position.y);
                    lowest = math.min(lowest, vertices[i].Position.y);
                    back = math.min(back, vertices[i].Position.z);
                }
                Assert.That(highest, Is.InRange(HandModel.PalmHalfThicknessKnuckles, HandModel.PalmHalfThicknessWrist + 0.002f));
                Assert.That(lowest, Is.InRange(-HandModel.PalmHalfThicknessWrist - 0.002f, -HandModel.PalmHalfThicknessKnuckles));
                Assert.That(back, Is.InRange(-0.045f, -0.02f));
            }
        }

        // ---- Rings land on the joints ----

        static float3 BoneAxis(in JointSet joints, int bone)
        {
            return math.normalize(joints[bone] - joints[HandJoints.Parent(bone)]);
        }

        // Where the design puts a ring, worked out here from the joints alone and with angles, which the skin
        // itself never uses.
        static float3 ExpectedCentre(HandsMeshTemplate template, in HandsMeshTemplate.Ring ring, in JointSet joints, float3 palmNormal)
        {
            HandsMeshTemplate.Frame frame = template.Frames[ring.Frame];
            float scale = math.distance(joints[HandJoint.MiddleMcp], joints[HandJoint.Wrist]) / HandModel.PalmLength;
            if (frame.Kind == HandsMeshTemplate.FrameKind.Anchor)
            {
                float3 z = math.normalize(joints[HandJoint.MiddleMcp] - joints[HandJoint.Wrist]);
                float3 y = math.normalize(palmNormal - math.dot(palmNormal, z) * z);
                float3 x = math.cross(y, z);
                float3 origin = math.lerp(joints[frame.Joint], joints[frame.Other], frame.Weight);
                return origin + scale * (ring.Centre.x * x + ring.Centre.y * y + ring.Centre.z * z);
            }

            float3 axis = BoneAxis(in joints, frame.Bone);
            if (frame.Weight > 0f)
            {
                // Turned towards the bone across the joint by its weight's share of the bend.
                float3 other = BoneAxis(in joints, frame.Other);
                float bend = math.acos(math.clamp(math.dot(axis, other), -1f, 1f));
                float3 hinge = math.normalizesafe(math.cross(axis, other));
                axis = math.mul(quaternion.AxisAngle(hinge, frame.Weight * bend), axis);
            }
            float bone = math.distance(joints[frame.Bone], joints[HandJoints.Parent(frame.Bone)]) / HandModel.BoneLength(frame.Bone);
            return joints[frame.Joint] + math.min(scale, bone) * ring.Centre.z * axis;
        }

        static void AssertRingsByTheirJoints(HandsMeshTemplate template, in JointSet joints, float3 palmNormal, string what)
        {
            AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, palmNormal, out float3 min, out float3 max);
            AssertSound(template, vertices, min, max, AtlasShading.LightDirection, what);
            float scale = math.distance(joints[HandJoint.MiddleMcp], joints[HandJoint.Wrist]) / HandModel.PalmLength;
            for (int r = 0; r < template.Rings.Length; r++)
            {
                HandsMeshTemplate.Ring ring = template.Rings[r];
                HandsMeshTemplate.Frame frame = template.Frames[ring.Frame];
                float3 centre = Centre(vertices, ring.First, ring.Count);
                float3 expected = ExpectedCentre(template, in ring, in joints, palmNormal);
                Assert.That(math.distance(centre, expected), Is.LessThan(1e-4f), what + ": ring " + r + " is not where its joint puts it");
                if (frame.Kind != HandsMeshTemplate.FrameKind.Ring) continue;

                // A ring is never farther from its joint than the template put it (a millimetre of slack), and it
                // keeps its radius however the joint bends: no thinned knuckle.
                Assert.That(math.distance(centre, joints[frame.Joint]), Is.LessThan(math.abs(ring.Centre.z) * scale + 0.001f), what + ": ring " + r);
                for (int i = 0; i < ring.Count; i++)
                {
                    float radius = math.distance(vertices[ring.First + i].Position, centre);
                    if (math.abs(radius - ring.Radius * scale) > 1e-4f) Assert.Fail(what + ": ring " + r + " has radius " + radius + " at vertex " + i);
                }
            }

            // The drawn fingertip: the cap's apex lies on the last bone's line, half a tip radius beyond the tip joint
            // (less when that bone measures short).
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int tip = HandJoints.Tip(finger);
                HandsMeshTemplate.Ring last = template.Rings[finger * HandsMeshTemplate.FingerRings + 6];
                float bone = math.distance(joints[tip], joints[tip - 1]) / HandModel.BoneLength(tip);
                float3 expected = joints[tip] + BoneAxis(in joints, tip) * (math.min(scale, bone) * HandsMeshTemplate.TipOvershoot * HandModel.Radius(tip));
                float3 apex = vertices[last.First + 2 * HandsConstants.FingerSides].Position;
                Assert.That(math.distance(apex, expected), Is.LessThan(1e-4f), what + ": the cap of finger " + finger);
            }
        }

        [Test]
        public void Hands_Skin_Mesh_EveryRingIsCentredByItsJoint_WhateverTheBonesMeasure()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);

                // In the template no ring is more than its joint's radius from the joint, and each is centred on its bone.
                for (int r = 0; r < HandJoints.FingerCount * HandsMeshTemplate.FingerRings; r++)
                {
                    HandsMeshTemplate.Ring ring = template.Rings[r];
                    int joint = template.Frames[ring.Frame].Joint;
                    Assert.That(math.abs(ring.Centre.z), Is.LessThanOrEqualTo(HandModel.Radius(joint) + 1e-6f), "ring " + r);
                    Assert.That(ring.Centre.xy, Is.EqualTo(float2.zero), "ring " + r);
                }

                uint seed = 1;
                foreach (float scale in Scales)
                {
                    var poses = new[]
                    {
                        HandModel.RestPose(side, scale),
                        HandsSkinRig.Fist(side, scale),
                        HandsSkinRig.Pinch(side, scale),
                        HandsSkinRig.Pointing(side, scale),
                        HandsSkinRig.Curled(side, 45f, 60f, 30f, 20f, scale),
                    };
                    for (int p = 0; p < poses.Length; p++)
                    {
                        string what = side + " pose " + p + " scale " + scale;
                        // In the palm frame, with the model's own bones.
                        AssertRingsByTheirJoints(template, in poses[p], HandsSkinRig.PalmUp, what);

                        // In the room, with every bone a little long or short: the rings move with the joints.
                        for (int k = 0; k < 3; k++)
                        {
                            JointSet stretched = HandsSkinRig.Stretched(in poses[p], seed++);
                            JointSet placed = HandsSkinRig.Placed(in stretched, HandsSkinRig.Turn, HandsSkinRig.Place);
                            AssertRingsByTheirJoints(template, in placed, math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp), what + " stretched " + k);
                        }
                    }
                }
            }
        }

        // ---- Fist, pinch, pointing ----

        [Test]
        public void Hands_Skin_Mesh_FistPinchAndPointing_KeepEveryTubeTheRightWayOut()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                int[] indices = HandsSkinRig.Indices(template);
                var poses = new[]
                {
                    HandsSkinRig.Fist(side),
                    HandsSkinRig.Pinch(side),
                    HandsSkinRig.Pointing(side),
                    // The fist a noisy lift reports: the knuckle at and past a right angle.
                    HandsSkinRig.Curled(side, 80f, 100f, 70f, 40f),
                    HandsSkinRig.Curled(side, 90f, 100f, 70f, 45f),
                    HandsSkinRig.Curled(side, 100f, 110f, 80f, 50f),
                    HandsSkinRig.Curled(side, 100f, 110f, 80f, 50f, HandsConstants.ScaleMin),
                    HandsSkinRig.Curled(side, 100f, 110f, 80f, 50f, HandsConstants.ScaleMax),
                    // And a hand bent back as far as a hand goes.
                    HandsSkinRig.Curled(side, -30f, -10f, -10f, -15f),
                };
                for (int p = 0; p < poses.Length; p++)
                {
                    string what = side + " pose " + p;
                    JointSet placed = HandsSkinRig.Placed(in poses[p], HandsSkinRig.Turn, HandsSkinRig.Place);
                    float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
                    AtlasVertex[] vertices = HandsSkinRig.Skin(template, in placed, normal, out float3 min, out float3 max);
                    AssertSound(template, vertices, min, max, AtlasShading.LightDirection, what);
                    // No ring has passed through its neighbour and no band is turned inside out, on the inside
                    // of a bend least of all.
                    Assert.That(AssertWoundOutwards(vertices, indices, template.IndexCount, 0f, what), Is.EqualTo(0), what + ": flat triangles");
                }
            }
        }

        [Test]
        public void Hands_Skin_Mesh_AnyPoseAHandCanTake_KeepsEveryTriangleTheRightWayOut()
        {
            // A thousand hands drawn at random from what joints allow, each finger on its own, half of them with
            // 2 mm of error on every joint as well: no band inside out, no triangle flat.
            var random = new Unity.Mathematics.Random(4242u);
            HandsMeshTemplate left = HandsMeshTemplate.Build(HandSide.Left), right = HandsMeshTemplate.Build(HandSide.Right);
            int[] leftIndices = HandsSkinRig.Indices(left), rightIndices = HandsSkinRig.Indices(right);
            for (int trial = 0; trial < 1000; trial++)
            {
                HandSide side = (trial & 1) == 0 ? HandSide.Left : HandSide.Right;
                HandsMeshTemplate template = side == HandSide.Left ? left : right;
                float scale = random.NextFloat(HandsConstants.ScaleMin, HandsConstants.ScaleMax);
                JointSet pose = HandsSkinRig.RandomPose(side, ref random, scale);
                if (trial % 4 >= 2)
                    for (int joint = 1; joint < HandJoints.Count; joint++)
                        pose[joint] = pose[joint] + random.NextFloat3Direction() * random.NextFloat(0f, 0.002f);
                string what = "trial " + trial;
                AtlasVertex[] vertices = HandsSkinRig.Skin(template, in pose, HandsSkinRig.PalmUp, out float3 min, out float3 max);
                AssertSound(template, vertices, min, max, AtlasShading.LightDirection, what);
                int flat = AssertWoundOutwards(vertices, side == HandSide.Left ? leftIndices : rightIndices, template.IndexCount, 0f, what);
                Assert.That(flat, Is.EqualTo(0), what + ": flat triangles");
            }
        }

        // True when the point lies inside the closed shell made by the triangles from firstIndex on: a ray from it
        // crosses the shell an odd number of times.
        static bool IsInside(float3 point, AtlasVertex[] vertices, int[] indices, int firstIndex, int indexCount)
        {
            double3 origin = point, direction = math.normalize(new double3(0.31, 0.89, 0.33));
            int crossings = 0;
            for (int t = firstIndex; t < firstIndex + indexCount; t += 3)
            {
                double3 a = vertices[indices[t]].Position, b = vertices[indices[t + 1]].Position, c = vertices[indices[t + 2]].Position;
                double3 ab = b - a, ac = c - a, h = math.cross(direction, ac);
                double det = math.dot(ab, h);
                if (math.abs(det) < 1e-18) continue;
                double3 s = origin - a;
                double u = math.dot(s, h) / det;
                double3 q = math.cross(s, ab);
                double v = math.dot(direction, q) / det;
                if (u < 0.0 || v < 0.0 || u + v > 1.0) continue;
                if (math.dot(ac, q) / det > 0.0) crossings++;
            }
            return (crossings & 1) == 1;
        }

        [Test]
        public void Hands_Skin_Mesh_EveryTubeStartsInsideThePalm()
        {
            // The first ring of each tube and of the cuff is open. It never shows: in every pose a hand takes it lies
            // inside the palm, which is why the palm hangs from the same joints the tubes do.
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                int[] indices = HandsSkinRig.Indices(template);
                int palmFirst = 3 * HandJoints.FingerCount * HandsMeshTemplate.FingerTriangles, palmCount = 3 * HandsMeshTemplate.PalmTriangles;
                uint seed = 31;
                var poses = new[]
                {
                    HandModel.RestPose(side),
                    HandsSkinRig.Fist(side),
                    HandsSkinRig.Pinch(side),
                    HandsSkinRig.Pointing(side),
                    HandsSkinRig.Curled(side, 25f, 35f, 20f, 12f),
                    HandsSkinRig.Curled(side, 100f, 110f, 80f, 50f),
                    HandsSkinRig.Curled(side, -30f, -10f, -10f, -15f),
                    HandsSkinRig.Stretched(HandModel.RestPose(side), seed++),
                    HandsSkinRig.Stretched(HandsSkinRig.Fist(side), seed++),
                    HandsSkinRig.Stretched(HandsSkinRig.Pinch(side), seed++),
                    HandModel.RestPose(side, HandsConstants.ScaleMin),
                    HandsSkinRig.Fist(side, HandsConstants.ScaleMax),
                };
                for (int p = 0; p < poses.Length; p++)
                {
                    JointSet placed = HandsSkinRig.Placed(in poses[p], HandsSkinRig.Turn, HandsSkinRig.Place);
                    AtlasVertex[] vertices = HandsSkinRig.Skin(template, in placed, math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp), out _, out _);
                    for (int r = 0; r <= HandJoints.FingerCount; r++)
                    {
                        HandsMeshTemplate.Ring ring = template.Rings[r * HandsMeshTemplate.FingerRings];
                        for (int i = 0; i < ring.Count; i++)
                            Assert.That(IsInside(vertices[ring.First + i].Position, vertices, indices, palmFirst, palmCount), Is.True,
                                        side + " pose " + p + ": the open ring " + r + " shows at vertex " + i);
                    }
                }
            }
        }

        [Test]
        public void Hands_Skin_Mesh_ClosingTheFist_NeverTwistsOrFlipsARing()
        {
            // A finger's frame is carried from the palm, so it stays defined when the knuckle is bent until the
            // finger runs along the palm's normal, and beyond. Half a degree of curl at every joint moves a
            // fingertip under 2 mm; a ring that flipped would jump by its own diameter and turn its normals over.
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                AtlasVertex[] before = null;
                for (int step = 0; step <= 240; step++)
                {
                    float curl = step * 0.5f;      // 0 to 120 degrees at the knuckle and the middle joint
                    JointSet joints = HandsSkinRig.Curled(side, curl, curl, math.min(curl, 90f), math.min(curl, 60f));
                    AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, out _, out _);
                    if (before != null)
                    {
                        float farthest = 0f, leastTurn = 1f;
                        for (int i = 0; i < template.VertexCount; i++)
                        {
                            farthest = math.max(farthest, math.distance(vertices[i].Position, before[i].Position));
                            leastTurn = math.min(leastTurn, math.dot(vertices[i].Normal, before[i].Normal));
                        }
                        Assert.That(farthest, Is.LessThan(0.003f), side + ": a vertex jumped at " + curl + " degrees");
                        Assert.That(leastTurn, Is.GreaterThan(0.995f), side + ": a normal jumped at " + curl + " degrees");
                    }
                    before = vertices;
                }
            }
        }

        [Test]
        public void Hands_Skin_Mesh_ASmallErrorInAJoint_NeverSpinsARing()
        {
            // A fist with up to 3 mm of error on every joint. With the knuckle near a right angle the rule "up is
            // the palm's normal made perpendicular to the finger's own first bone" spins the finger by 13 to 180
            // degrees: that normal then runs along the bone. Here a ring moves with its joints and turns by no
            // more than its bone does.
            var random = new Unity.Mathematics.Random(77u);
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            foreach (float knuckle in new[] { 80f, 85f, 90f, 95f, 100f })
            {
                JointSet clean = HandsSkinRig.Curled(HandSide.Right, knuckle, 100f, 70f, 40f);
                AtlasVertex[] reference = HandsSkinRig.Skin(template, in clean, HandsSkinRig.PalmUp, out _, out _);
                for (int trial = 0; trial < 50; trial++)
                {
                    JointSet noisy = clean;
                    for (int joint = 1; joint < HandJoints.Count; joint++)
                        noisy[joint] = clean[joint] + random.NextFloat3Direction() * random.NextFloat(0f, 0.003f);
                    AtlasVertex[] vertices = HandsSkinRig.Skin(template, in noisy, HandsSkinRig.PalmUp, out _, out _);

                    float farthest = 0f;
                    for (int i = 0; i < template.VertexCount; i++)
                        farthest = math.max(farthest, math.distance(vertices[i].Position, reference[i].Position));
                    Assert.That(farthest, Is.LessThan(0.009f), "knuckle at " + knuckle + ", trial " + trial);

                    for (int r = 0; r < HandJoints.FingerCount * HandsMeshTemplate.FingerRings; r++)
                    {
                        HandsMeshTemplate.Ring ring = template.Rings[r];
                        float3 spoke = math.normalize(vertices[ring.First].Position - Centre(vertices, ring.First, ring.Count));
                        float3 was = math.normalize(reference[ring.First].Position - Centre(reference, ring.First, ring.Count));
                        Assert.That(math.degrees(math.acos(math.saturate(math.dot(spoke, was)))), Is.LessThan(40f),
                                    "knuckle at " + knuckle + ", trial " + trial + ": ring " + r + " spun");
                    }
                }
            }
        }

        // ---- Hands that are not hands ----

        static void AssertFinite(HandsMeshTemplate template, in JointSet joints, float3 normal, string what)
        {
            AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, normal, out float3 min, out float3 max);
            AssertSound(template, vertices, min, max, AtlasShading.LightDirection, what);
        }

        [Test]
        public void Hands_Skin_Mesh_DegenerateHands_GiveFiniteVertices()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                JointSet open = HandsSkinRig.Placed(HandModel.RestPose(side), HandsSkinRig.Turn, HandsSkinRig.Place);
                float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);

                // Two coincident joints, for every bone in turn: a bone of no length.
                for (int joint = 1; joint < HandJoints.Count; joint++)
                {
                    JointSet joints = open;
                    joints[joint] = joints[HandJoints.Parent(joint)];
                    AssertFinite(template, in joints, normal, side + ": joint " + joint + " on its parent");
                }

                // A whole finger in one point, the whole hand in one point, the whole hand at the origin.
                for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                {
                    JointSet joints = open;
                    for (int k = 1; k < HandJoints.PerFinger; k++) joints[HandJoints.Base(finger) + k] = joints[HandJoints.Base(finger)];
                    AssertFinite(template, in joints, normal, side + ": finger " + finger + " in one point");
                }
                JointSet point = default(JointSet);
                AssertFinite(template, in point, normal, side + ": every joint at the origin");
                for (int joint = 0; joint < HandJoints.Count; joint++) point[joint] = HandsSkinRig.Place;
                AssertFinite(template, in point, normal, side + ": every joint in one point");

                // A bone folded straight back along the one before it.
                for (int joint = 2; joint < HandJoints.Count; joint++)
                {
                    int parent = HandJoints.Parent(joint);
                    if (parent == 0) continue;
                    JointSet joints = open;
                    joints[joint] = joints[parent] - 0.6f * (joints[parent] - joints[HandJoints.Parent(parent)]);
                    AssertFinite(template, in joints, normal, side + ": bone " + joint + " folded back");
                }

                // A finger along the palm's normal, a normal that lies in the palm, no normal at all.
                JointSet along = open;
                float3 knuckle = open[HandJoint.IndexMcp];
                for (int k = 1; k < HandJoints.PerFinger; k++) along[(int)HandJoint.IndexMcp + k] = knuckle + normal * (0.03f * k);
                AssertFinite(template, in along, normal, side + ": a finger along the normal");
                AssertFinite(template, in open, math.normalize(open[HandJoint.MiddleMcp] - open[HandJoint.Wrist]), side + ": a normal along the palm");
                AssertFinite(template, in open, math.normalize(open[HandJoint.IndexMcp] - open[HandJoint.Wrist]), side + ": a normal along the index line");
                AssertFinite(template, in open, math.normalize(open[HandJoint.ThumbCmc] - open[HandJoint.Wrist]), side + ": a normal along the thumb's base");
                AssertFinite(template, in open, float3.zero, side + ": no normal");
                AssertFinite(template, in open, normal * 250f, side + ": a normal that is not unit length");
                AssertFinite(template, in open, new float3(float.NaN, 0f, 0f), side + ": a normal that is not a number");
                AssertFinite(template, in open, new float3(float.PositiveInfinity, 0f, 0f), side + ": an endless normal");

                // A hand a thousand times too large or too small is still a hand.
                AssertFinite(template, HandModel.RestPose(side, 1000f), HandsSkinRig.PalmUp, side + ": a huge hand");
                AssertFinite(template, HandModel.RestPose(side, 0.001f), HandsSkinRig.PalmUp, side + ": a tiny hand");
            }
        }

        [Test]
        public void Hands_Skin_Mesh_AnyFiniteJoints_GiveFiniteVerticesInsideTheirBounds()
        {
            // Joints thrown anywhere: in a box the size of a hand, in a box the size of a room, at the limit of what
            // is drawn, with some of them on top of each other, and a normal that has nothing to do with them.
            var random = new Unity.Mathematics.Random(20261004u);
            HandsMeshTemplate left = HandsMeshTemplate.Build(HandSide.Left), right = HandsMeshTemplate.Build(HandSide.Right);
            float[] boxes = { 0.05f, 0.3f, 5f, HandsSkin.MaxCoordinate };
            for (int trial = 0; trial < 2000; trial++)
            {
                float box = boxes[trial % boxes.Length];
                JointSet joints = default(JointSet);
                for (int joint = 0; joint < HandJoints.Count; joint++)
                    joints[joint] = random.NextFloat3(new float3(-box), new float3(box));
                // Every third hand has joints that share a place.
                if (trial % 3 == 0)
                    for (int k = 0; k < 4; k++) joints[random.NextInt(0, HandJoints.Count)] = joints[random.NextInt(0, HandJoints.Count)];
                float3 normal = trial % 5 == 0 ? float3.zero : random.NextFloat3Direction();
                AssertFinite((trial & 1) == 0 ? left : right, in joints, normal, "trial " + trial + " in a box of " + box);
            }
        }

        [Test]
        public void Hands_Skin_Mesh_WithNoUsableNormal_ThePalmFacesTheWayTheJointsSay()
        {
            // The normal passed in is the rule; when it is missing the knuckles give one, for either hand.
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                JointSet joints = HandsSkinRig.Placed(HandModel.RestPose(side), HandsSkinRig.Turn, HandsSkinRig.Place);
                float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
                AtlasVertex[] expected = HandsSkinRig.Skin(template, in joints, normal, out _, out _);
                AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, float3.zero, out _, out _);
                for (int i = 0; i < template.VertexCount; i++)
                    Assert.That(math.distance(vertices[i].Position, expected[i].Position), Is.LessThan(1e-4f), side + ": vertex " + i);
            }
        }

        [Test]
        public void Hands_Skin_Mesh_JointsThatCannotBeDrawn_CollapseTheMeshOntoOnePoint()
        {
            // Not finite, or farther out than any room: every vertex goes to one finite point, so the triangles
            // have no area and the bounds are never NaN.
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            float[] bad = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.MaxValue, -float.MaxValue, 2f * HandsSkin.MaxCoordinate };
            foreach (float value in bad)
            {
                for (int joint = 0; joint < HandJoints.Count; joint += 4)
                {
                    for (int axis = 0; axis < 3; axis++)
                    {
                        JointSet joints = HandModel.RestPose(HandSide.Right);
                        float3 p = joints[joint];
                        p[axis] = value;
                        joints[joint] = p;
                        string what = value + " in joint " + joint;
                        AtlasVertex[] vertices = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, out float3 min, out float3 max);
                        AssertSound(template, vertices, min, max, AtlasShading.LightDirection, what);
                        Assert.That(min, Is.EqualTo(max), what);
                        for (int i = 0; i < template.VertexCount; i++)
                            if (!vertices[i].Position.Equals(min)) Assert.Fail(what + ": vertex " + i + " is somewhere else");
                    }
                }
            }
        }

        // ---- The mesh belongs to the joints, not to a frame ----

        [Test]
        public void Hands_Skin_Mesh_FollowsTheJointsThroughTheRoom_AndTakesItsSizeFromThem()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                var poses = new[] { HandModel.RestPose(side), HandsSkinRig.Fist(side), HandsSkinRig.Pinch(side) };
                foreach (JointSet pose in poses)
                {
                    AtlasVertex[] home = HandsSkinRig.Skin(template, in pose, HandsSkinRig.PalmUp, out float3 homeMin, out float3 homeMax);

                    // Turned and moved: the same mesh, turned and moved.
                    JointSet placed = HandsSkinRig.Placed(in pose, HandsSkinRig.Turn, HandsSkinRig.Place);
                    float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
                    AtlasVertex[] room = HandsSkinRig.Skin(template, in placed, normal, out _, out _);
                    for (int i = 0; i < template.VertexCount; i++)
                    {
                        float3 expected = HandsSkinRig.Place + math.mul(HandsSkinRig.Turn, home[i].Position);
                        if (math.distance(room[i].Position, expected) > 2e-5f) Assert.Fail(side + ": vertex " + i + " did not follow");
                        if (math.distance(room[i].Normal, math.mul(HandsSkinRig.Turn, home[i].Normal)) > 1e-4f) Assert.Fail(side + ": normal " + i + " did not follow");
                    }

                    // Half as large again: the size comes from the joints, radii and all.
                    JointSet large = default(JointSet);
                    for (int j = 0; j < HandJoints.Count; j++) large[j] = pose[j] * 1.5f;
                    AtlasVertex[] grown = HandsSkinRig.Skin(template, in large, HandsSkinRig.PalmUp, out float3 grownMin, out float3 grownMax);
                    for (int i = 0; i < template.VertexCount; i++)
                    {
                        if (math.distance(grown[i].Position, home[i].Position * 1.5f) > 2e-5f) Assert.Fail(side + ": vertex " + i + " did not grow");
                        if (math.distance(grown[i].Normal, home[i].Normal) > 1e-4f) Assert.Fail(side + ": normal " + i + " changed with the size");
                    }
                    Assert.That(math.distance(grownMin, homeMin * 1.5f), Is.LessThan(1e-4f));
                    Assert.That(math.distance(grownMax, homeMax * 1.5f), Is.LessThan(1e-4f));
                }
            }
        }

        [Test]
        public void Hands_Skin_Mesh_TheShadeFollowsTheLightPassedIn_AndIsTheSameFromBothSides()
        {
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            JointSet joints = HandModel.RestPose(HandSide.Right);
            float3 light = math.normalize(new float3(-0.5f, 0.2f, 0.9f));
            AtlasVertex[] lit = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, light, out float3 min, out float3 max);
            AssertSound(template, lit, min, max, light, "another light");
            AtlasVertex[] reversed = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, -light, out _, out _);
            AtlasVertex[] usual = HandsSkinRig.Skin(template, in joints, HandsSkinRig.PalmUp, out _, out _);
            int changed = 0, brightest = 0, darkest = 255;
            for (int i = 0; i < template.VertexCount; i++)
            {
                Assert.That(reversed[i].Shade, Is.EqualTo(lit[i].Shade), "vertex " + i);
                if (usual[i].Shade != lit[i].Shade) changed++;
                brightest = math.max(brightest, lit[i].Shade);
                darkest = math.min(darkest, lit[i].Shade);
            }
            Assert.That(changed, Is.GreaterThan(template.VertexCount / 2));
            // The two-sided rule runs from halfway up the floor to full: 0.775 to 1.
            Assert.That(darkest, Is.InRange(197, 205));
            Assert.That(brightest, Is.InRange(250, 255));
        }

        // ---- Cost ----

        [Test]
        public void Hands_Skin_AllocatesNothing()
        {
            HandsMeshTemplate left = HandsMeshTemplate.Build(HandSide.Left), right = HandsMeshTemplate.Build(HandSide.Right);
            var vertices = new AtlasVertex[HandsConstants.MaxVertices];
            JointSet open = HandsSkinRig.Placed(HandModel.RestPose(HandSide.Left), HandsSkinRig.Turn, HandsSkinRig.Place);
            JointSet fist = HandsSkinRig.Placed(HandsSkinRig.Fist(HandSide.Right), HandsSkinRig.Turn, HandsSkinRig.Place);
            JointSet point = default(JointSet);
            JointSet lost = open;
            lost[3] = new float3(float.NaN);
            float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
            float3 light = AtlasShading.LightDirection;
            float2 uv = HandsSkinRig.Uv;

            fixed (AtlasVertex* p = vertices)
            {
                // A few frames to let the runtime compile the path, then 100 measured.
                for (int i = 0; i < 20; i++) Frame(left, right, in open, in fist, in point, in lost, normal, uv, light, p);

                long before = GC.GetAllocatedBytesForCurrentThread();
                float3 sum = float3.zero;
                for (int i = 0; i < 100; i++) sum += Frame(left, right, in open, in fist, in point, in lost, normal, uv, light, p);
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.EqualTo(0), "bytes allocated by 100 frames");
                Assert.That(HandsSkinRig.IsFinite(sum), Is.True);
            }
        }

        // What one frame does and more: both hands, a hand that is all in one point and a hand that is not finite.
        static float3 Frame(HandsMeshTemplate left, HandsMeshTemplate right, in JointSet open, in JointSet fist,
                            in JointSet point, in JointSet lost, float3 normal, float2 uv, float3 light, AtlasVertex* vertices)
        {
            HandsSkin.Skin(left, in open, normal, uv, light, vertices, out float3 min, out float3 max);
            float3 sum = min + max;
            HandsSkin.Skin(right, in fist, normal, uv, light, vertices, out min, out max);
            sum += min + max;
            HandsSkin.Skin(right, in point, normal, uv, light, vertices, out min, out max);
            sum += min + max;
            HandsSkin.Skin(left, in lost, normal, uv, light, vertices, out min, out max);
            return sum + min + max;
        }
    }
}
