using System;
using System.Reflection;
using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The hand mesh before any joints are known (documents/ATLAS_Hands_Design.md section 5.1): what it is made
    /// of, what it costs, how each vertex is bound and that the left hand is the mirror of the right. The half
    /// of Hands_Skin_Mesh that needs no pose.
    /// </summary>
    public unsafe class HandsMeshTemplateTests
    {
        static readonly HandSide[] Sides = { HandSide.Right, HandSide.Left };

        [Test]
        public void Hands_Skin_Mesh_ThePublicSurfaceIsWhatTheViewIsWrittenAgainst()
        {
            // The hands' view fills a dynamic mesh from native arrays; those two entry points cannot run without
            // the engine, so here they are only held to their shape. HandsSkinUnityTests runs them.
            const BindingFlags instance = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            const BindingFlags statics = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
            Type template = typeof(HandsMeshTemplate), skin = typeof(HandsSkin), byRef = typeof(float3).MakeByRefType();

            MethodInfo build = template.GetMethod("Build", statics, null, new[] { typeof(HandSide) }, null);
            Assert.That(build, Is.Not.Null);
            Assert.That(build.ReturnType, Is.EqualTo(template));
            Assert.That(template.GetMethod("CopyIndices", instance, null, new[] { typeof(NativeArray<int>) }, null), Is.Not.Null);
            Assert.That(template.GetProperty("Side", instance).PropertyType, Is.EqualTo(typeof(HandSide)));
            foreach (string count in new[] { "VertexCount", "IndexCount", "TriangleCount" })
                Assert.That(template.GetProperty(count, instance).PropertyType, Is.EqualTo(typeof(int)), count);
            Assert.That(template.IsSealed, Is.True);
            Assert.That(template.GetConstructors(BindingFlags.Public | BindingFlags.Instance), Is.Empty, "a template comes from Build");

            MethodInfo pose = skin.GetMethod("Skin", statics, null, new[]
            {
                template, typeof(JointSet).MakeByRefType(), typeof(float3), typeof(float2), typeof(float3),
                typeof(NativeArray<AtlasVertex>), byRef, byRef,
            }, null);
            Assert.That(pose, Is.Not.Null);
            ParameterInfo[] parameters = pose.GetParameters();
            Assert.That(parameters[1].IsIn, Is.True, "the joints are passed by reference and not written");
            Assert.That(parameters[6].IsOut && parameters[7].IsOut, Is.True, "the bounds come back");

            // Nothing else is public: the kernel and the template's tables are the module's own.
            Assert.That(template.GetMethods(instance | statics).Length, Is.EqualTo(6), "Build, CopyIndices and four property getters");
            Assert.That(skin.GetMethods(statics).Length, Is.EqualTo(1));
            Assert.That(template.GetFields(instance | statics), Is.Empty);
            Assert.That(skin.GetFields(statics), Is.Empty);
        }

        [Test]
        public void Hands_Skin_Mesh_CountsEqualThePublishedConstants_AndFitTheBudget()
        {
            // The numbers the design quotes: about 700 triangles and 450 vertices a hand.
            Assert.That(HandsMeshTemplate.TriangleTotal, Is.EqualTo(790));
            Assert.That(HandsMeshTemplate.VertexTotal, Is.EqualTo(409));
            Assert.That(HandJoints.FingerCount * HandsMeshTemplate.FingerTriangles + HandsMeshTemplate.PalmTriangles
                        + HandsMeshTemplate.CuffTriangles, Is.EqualTo(HandsMeshTemplate.TriangleTotal));
            Assert.That(HandJoints.FingerCount * HandsMeshTemplate.FingerVertices + HandsMeshTemplate.PalmVertices
                        + HandsMeshTemplate.CuffVertices, Is.EqualTo(HandsMeshTemplate.VertexTotal));

            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                Assert.That(template.Side, Is.EqualTo(side));
                Assert.That(template.TriangleCount, Is.EqualTo(HandsMeshTemplate.TriangleTotal), side + ": triangles");
                Assert.That(template.VertexCount, Is.EqualTo(HandsMeshTemplate.VertexTotal), side + ": vertices");
                Assert.That(template.IndexCount, Is.EqualTo(3 * template.TriangleCount));
                Assert.That(template.VertexCount, Is.LessThanOrEqualTo(HandsConstants.MaxVertices));
                Assert.That(template.IndexCount, Is.LessThanOrEqualTo(HandsConstants.MaxIndices));

                // Every index names a vertex, every vertex is used, and no triangle uses a vertex twice.
                int[] indices = HandsSkinRig.Indices(template);
                var used = new bool[template.VertexCount];
                for (int t = 0; t < template.IndexCount; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    Assert.That(a >= 0 && a < template.VertexCount && b >= 0 && b < template.VertexCount
                                && c >= 0 && c < template.VertexCount, Is.True, side + ": triangle " + t / 3);
                    Assert.That(a != b && b != c && c != a, Is.True, side + ": triangle " + t / 3);
                    used[a] = used[b] = used[c] = true;
                }
                for (int i = 0; i < used.Length; i++) Assert.That(used[i], Is.True, side + ": vertex " + i + " is in no triangle");
            }
        }

        [Test]
        public void Hands_Skin_Mesh_IsSevenClosedShells_EachWoundOneWayRound()
        {
            // Five tubes, the palm and the cuff, each closed: the tubes and the cuff start inside the palm (section
            // 5.1: no seam needs stitching) and are closed there too, so no pose can show a hole. Closed and wound
            // one way round: every edge is walked once in each direction by the two triangles that share it.
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                int[] indices = HandsSkinRig.Indices(template);
                var edges = new System.Collections.Generic.HashSet<long>();
                var shell = new int[template.VertexCount];
                for (int i = 0; i < shell.Length; i++) shell[i] = i;
                for (int t = 0; t < template.IndexCount; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        long from = indices[t + k], to = indices[t + (k + 1) % 3];
                        Assert.That(edges.Add((from << 32) | to), Is.True, side + ": an edge is walked twice the same way in triangle " + t / 3);
                        int a = Root(shell, (int)from), b = Root(shell, (int)to);
                        if (a != b) shell[a] = b;
                    }
                }
                foreach (long edge in edges)
                {
                    long from = edge >> 32, to = edge & 0xFFFFFFFFL;
                    Assert.That(edges.Contains((to << 32) | from), Is.True, side + ": the mesh is open between vertices " + from + " and " + to);
                }

                int shells = 0;
                for (int i = 0; i < shell.Length; i++)
                    if (Root(shell, i) == i) shells++;
                Assert.That(shells, Is.EqualTo(HandJoints.FingerCount + 2));
                // In the order the constants give: the tubes, the palm, the cuff.
                int palm = HandJoints.FingerCount * HandsMeshTemplate.FingerVertices, cuff = palm + HandsMeshTemplate.PalmVertices;
                for (int finger = 0; finger < HandJoints.FingerCount; finger++)
                    for (int i = 1; i < HandsMeshTemplate.FingerVertices; i++)
                        Assert.That(Root(shell, finger * HandsMeshTemplate.FingerVertices + i),
                                    Is.EqualTo(Root(shell, finger * HandsMeshTemplate.FingerVertices)), side + ": finger " + finger);
                for (int i = 1; i < HandsMeshTemplate.PalmVertices; i++) Assert.That(Root(shell, palm + i), Is.EqualTo(Root(shell, palm)));
                for (int i = 1; i < HandsMeshTemplate.CuffVertices; i++) Assert.That(Root(shell, cuff + i), Is.EqualTo(Root(shell, cuff)));
            }
        }

        static int Root(int[] shell, int vertex)
        {
            while (shell[vertex] != vertex) vertex = shell[vertex] = shell[shell[vertex]];
            return vertex;
        }

        [Test]
        public void Hands_Skin_Mesh_TheIndicesStayTheSameForTheTemplatesLife()
        {
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            int[] first = HandsSkinRig.Indices(template);
            JointSet fist = HandsSkinRig.Fist(HandSide.Right);
            HandsSkinRig.Skin(template, in fist, HandsSkinRig.PalmUp, out _, out _);
            int[] second = HandsSkinRig.Indices(template);
            Assert.That(second, Is.EqualTo(first));

            // A second template of the same side is the same mesh.
            Assert.That(HandsSkinRig.Indices(HandsMeshTemplate.Build(HandSide.Right)), Is.EqualTo(first));
        }

        [Test]
        public void Hands_Skin_Mesh_ATooSmallIndexBuffer_IsRefused()
        {
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Left);
            var small = new int[template.IndexCount - 1];
            Assert.That(() =>
            {
                fixed (int* p = small) template.CopyIndices(p, small.Length);
            }, Throws.ArgumentException);
        }

        [Test]
        public void Hands_Skin_Mesh_EveryVertexHangsFromOneBoneOrFromTwoWithWeightsThatSumToOne()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                Assert.That(template.Frames.Length, Is.LessThanOrEqualTo(HandsMeshTemplate.MaxFrames));
                int blended = 0;
                for (int i = 0; i < template.VertexCount; i++)
                {
                    HandsMeshTemplate.Vertex vertex = template.Vertices[i];
                    Assert.That(vertex.Frame, Is.InRange(0, template.Frames.Length - 1), "vertex " + i);
                    Assert.That(math.length(vertex.Normal), Is.EqualTo(1f).Within(1e-5f), "vertex " + i);
                    Assert.That(HandsSkinRig.IsFinite(vertex.Position), Is.True, "vertex " + i);

                    HandsMeshTemplate.Frame frame = template.Frames[vertex.Frame];
                    float own = 1f - frame.Weight, other = frame.Weight;
                    Assert.That(own + other, Is.EqualTo(1f));
                    Assert.That(other, Is.InRange(0f, 0.5f), "the vertex's own bone never weighs less than the other");
                    if (frame.Kind == HandsMeshTemplate.FrameKind.Ring)
                    {
                        // A ring sits on a bone by the joint at one of its ends, and leans on the bone across that joint.
                        Assert.That(frame.Bone, Is.InRange(1, HandJoints.Count - 1));
                        Assert.That(frame.Joint == frame.Bone || frame.Joint == HandJoints.Parent(frame.Bone), Is.True, "vertex " + i);
                        if (other > 0f)
                        {
                            Assert.That(math.abs(frame.Other - frame.Bone), Is.EqualTo(1), "vertex " + i);
                            Assert.That(HandJoints.FingerOf(frame.Other), Is.EqualTo(HandJoints.FingerOf(frame.Bone)));
                            Assert.That((int)frame.Joint, Is.EqualTo(math.min((int)frame.Bone, (int)frame.Other)), "the joint between the two bones");
                            blended++;
                        }
                        else
                        {
                            Assert.That(frame.Other, Is.EqualTo(frame.Bone));
                        }
                    }
                    else
                    {
                        // The palm and the cuff hang from the wrist, the thumb's base and the four knuckles.
                        Assert.That(frame.Kind, Is.EqualTo(HandsMeshTemplate.FrameKind.Anchor));
                        Assert.That(IsAnchor(frame.Joint) && IsAnchor(frame.Other), Is.True, "vertex " + i);
                        if (other > 0f) Assert.That(frame.Other, Is.Not.EqualTo(frame.Joint));
                    }
                }
                // Six rings of eight a finger lean on two bones, and the point that closes the first of them.
                Assert.That(blended, Is.EqualTo(HandJoints.FingerCount * (6 * HandsConstants.FingerSides + 1)));
            }
        }

        static bool IsAnchor(int joint)
        {
            return joint == 0 || (joint - 1) % HandJoints.PerFinger == 0;
        }

        [Test]
        public void Hands_Skin_Mesh_TheLeftTemplateIsTheMirrorOfTheRight()
        {
            HandsMeshTemplate right = HandsMeshTemplate.Build(HandSide.Right);
            HandsMeshTemplate left = HandsMeshTemplate.Build(HandSide.Left);
            Assert.That(left.VertexCount, Is.EqualTo(right.VertexCount));
            Assert.That(left.IndexCount, Is.EqualTo(right.IndexCount));
            Assert.That(left.Reach, Is.EqualTo(right.Reach));
            Assert.That(left.ThumbTurn, Is.EqualTo(-right.ThumbTurn), "the thumb turns the other way");
            Assert.That(left.Frames.Length, Is.EqualTo(right.Frames.Length));

            // The same vertices on the same bones, with the side axis and the normals turned over.
            for (int i = 0; i < right.VertexCount; i++)
            {
                HandsMeshTemplate.Vertex r = right.Vertices[i], l = left.Vertices[i];
                Assert.That(l.Frame, Is.EqualTo(r.Frame), "vertex " + i);
                Assert.That(l.Position, Is.EqualTo(new float3(-r.Position.x, r.Position.y, r.Position.z)), "vertex " + i);
                Assert.That(l.Normal, Is.EqualTo(new float3(-r.Normal.x, r.Normal.y, r.Normal.z)), "vertex " + i);
            }

            // The same triangles wound the other way, so the mirrored hand is not inside out.
            int[] ri = HandsSkinRig.Indices(right), li = HandsSkinRig.Indices(left);
            for (int t = 0; t < right.IndexCount; t += 3)
            {
                Assert.That(li[t], Is.EqualTo(ri[t]), "triangle " + t / 3);
                Assert.That(li[t + 1], Is.EqualTo(ri[t + 2]), "triangle " + t / 3);
                Assert.That(li[t + 2], Is.EqualTo(ri[t + 1]), "triangle " + t / 3);
            }

            // And posed on mirrored joints the two meshes are mirror images, vertex for vertex.
            foreach (bool fist in new[] { false, true })
            {
                JointSet rightPose = fist ? HandsSkinRig.Fist(HandSide.Right) : HandModel.RestPose(HandSide.Right);
                JointSet leftPose = fist ? HandsSkinRig.Fist(HandSide.Left) : HandModel.RestPose(HandSide.Left);
                AtlasVertex[] rv = HandsSkinRig.Skin(right, in rightPose, HandsSkinRig.PalmUp, out float3 rMin, out float3 rMax);
                AtlasVertex[] lv = HandsSkinRig.Skin(left, in leftPose, HandsSkinRig.PalmUp, out float3 lMin, out float3 lMax);
                for (int i = 0; i < right.VertexCount; i++)
                {
                    float3 mirrored = new float3(-rv[i].Position.x, rv[i].Position.y, rv[i].Position.z);
                    Assert.That(math.distance(lv[i].Position, mirrored), Is.LessThan(1e-6f), "vertex " + i);
                    float3 normal = new float3(-rv[i].Normal.x, rv[i].Normal.y, rv[i].Normal.z);
                    Assert.That(math.distance(lv[i].Normal, normal), Is.LessThan(1e-5f), "vertex " + i);
                }
                Assert.That(lMin.x, Is.EqualTo(-rMax.x).Within(1e-6f));
                Assert.That(lMax.x, Is.EqualTo(-rMin.x).Within(1e-6f));
                Assert.That(lMin.yz, Is.EqualTo(rMin.yz));
                Assert.That(lMax.yz, Is.EqualTo(rMax.yz));
            }
        }

        [Test]
        public void Hands_Skin_Mesh_TheTemplateIsBuiltFromTheModelsDimensions()
        {
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            Assert.That(template.PalmLength, Is.EqualTo(HandModel.PalmLength));
            for (int joint = 1; joint < HandJoints.Count; joint++)
                Assert.That(template.BoneLengths[joint], Is.EqualTo(HandModel.BoneLength(joint)), "bone " + joint);
            Assert.That(math.abs(math.degrees(template.ThumbTurn)), Is.EqualTo(HandsMeshTemplate.ThumbTurnDegrees).Within(1e-3f));

            // Eight rings a finger: before and after each of its three joints, the last full ring and the cap's ring.
            Assert.That(template.Rings.Length, Is.EqualTo(HandJoints.FingerCount * HandsMeshTemplate.FingerRings + HandsMeshTemplate.CuffRings));
            for (int finger = 0; finger < HandJoints.FingerCount; finger++)
            {
                int tip = HandJoints.Tip(finger);
                float tipRadius = HandModel.Radius(tip);
                for (int k = 0; k < HandsMeshTemplate.FingerRings; k++)
                {
                    HandsMeshTemplate.Ring ring = template.Rings[finger * HandsMeshTemplate.FingerRings + k];
                    Assert.That(ring.Count, Is.EqualTo(HandsConstants.FingerSides));
                    int joint = template.Frames[ring.Frame].Joint;
                    Assert.That(joint, Is.EqualTo(HandJoints.Base(finger) + math.min(k / 2, 3)), "finger " + finger + " ring " + k);
                    // The tube tapers with the model's radii: no ring is wider than the joint behind it or thinner than half the tip.
                    Assert.That(ring.Radius, Is.LessThanOrEqualTo(HandModel.Radius(HandJoints.Base(finger)) + 1e-6f));
                    Assert.That(ring.Radius, Is.GreaterThanOrEqualTo(0.5f * tipRadius));
                    if (k >= 1 && k <= 6)
                    {
                        float before = HandModel.Radius(joint), after = HandModel.Radius(math.min(joint + 1, tip));
                        float behind = HandModel.Radius(math.max(joint - 1, HandJoints.Base(finger)));
                        Assert.That(ring.Radius, Is.InRange(math.min(before, after) - 1e-6f, math.max(before, behind) + 1e-6f),
                                    "finger " + finger + " ring " + k);
                    }
                }
                // The cap: its apex is half a tip radius beyond the tip joint, its last full ring one radius before the apex.
                HandsMeshTemplate.Ring last = template.Rings[finger * HandsMeshTemplate.FingerRings + 6];
                Assert.That(last.Radius, Is.EqualTo(tipRadius).Within(1e-6f));
                Assert.That(last.Centre.z, Is.EqualTo((HandsMeshTemplate.TipOvershoot - 1f) * tipRadius).Within(1e-6f));
                HandsMeshTemplate.Vertex apex = template.Vertices[last.First + 2 * HandsConstants.FingerSides];
                Assert.That(apex.Position.xy, Is.EqualTo(float2.zero));
                Assert.That(apex.Position.z, Is.EqualTo(HandsMeshTemplate.TipOvershoot * tipRadius).Within(1e-6f));
                Assert.That(apex.Normal.z, Is.EqualTo(1f).Within(1e-6f));
            }
        }
    }
}
