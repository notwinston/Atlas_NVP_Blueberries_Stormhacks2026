#if !ATLAS_HEADLESS
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Atlas.Hands.Tests
{
    /// <summary>
    /// The half of the hand mesh that needs the engine: the overloads that take native arrays, and the dynamic
    /// mesh that is filled from them (documents/ATLAS_Hands_Design.md section 5.2, step 4).
    /// </summary>
    [Category("Unity")]
    public class HandsSkinUnityTests
    {
        static readonly HandSide[] Sides = { HandSide.Right, HandSide.Left };

        [Test]
        public void TheNativeArrayOverloads_WriteWhatTheKernelWrites_AndTheMeshTakesThem()
        {
            foreach (HandSide side in Sides)
            {
                HandsMeshTemplate template = HandsMeshTemplate.Build(side);
                var vertices = new NativeArray<AtlasVertex>(template.VertexCount, Allocator.Persistent);
                var indices = new NativeArray<int>(template.IndexCount, Allocator.Persistent);
                Mesh mesh = AtlasMesh.CreateDynamic("Hands Test", template.VertexCount, template.IndexCount);
                try
                {
                    template.CopyIndices(indices);
                    int[] expectedIndices = HandsSkinRig.Indices(template);
                    for (int i = 0; i < template.IndexCount; i++)
                        Assert.That(indices[i], Is.EqualTo(expectedIndices[i]), side + ": index " + i);

                    JointSet joints = HandsSkinRig.Placed(HandsSkinRig.Fist(side), HandsSkinRig.Turn, HandsSkinRig.Place);
                    float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
                    HandsSkin.Skin(template, in joints, normal, HandsSkinRig.Uv, AtlasShading.LightDirection, vertices,
                                   out float3 min, out float3 max);
                    AtlasVertex[] expected = HandsSkinRig.Skin(template, in joints, normal, out float3 expectedMin, out float3 expectedMax);
                    Assert.That(min, Is.EqualTo(expectedMin));
                    Assert.That(max, Is.EqualTo(expectedMax));
                    for (int i = 0; i < template.VertexCount; i++)
                    {
                        Assert.That(vertices[i].Position, Is.EqualTo(expected[i].Position), side + ": vertex " + i);
                        Assert.That(vertices[i].Normal, Is.EqualTo(expected[i].Normal), side + ": normal " + i);
                        Assert.That(vertices[i].Color, Is.EqualTo(expected[i].Color), side + ": colour " + i);
                        Assert.That(vertices[i].Uv0, Is.EqualTo(expected[i].Uv0), side + ": palette coordinate " + i);
                    }

                    var bounds = new Bounds(0.5f * (min + max), max - min);
                    AtlasMesh.Upload(mesh, vertices, template.VertexCount, indices, template.IndexCount, bounds);
                    Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)template.IndexCount));
                    Assert.That(mesh.vertexCount, Is.EqualTo(template.VertexCount));
                    for (int i = 0; i < template.VertexCount; i += 17)
                        Assert.That(mesh.bounds.Contains(vertices[i].Position), Is.True, side + ": vertex " + i);
                }
                finally
                {
                    vertices.Dispose();
                    indices.Dispose();
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void BuffersThatAreTooSmall_AreRefused()
        {
            HandsMeshTemplate template = HandsMeshTemplate.Build(HandSide.Right);
            JointSet joints = HandModel.RestPose(HandSide.Right);
            var vertices = new NativeArray<AtlasVertex>(template.VertexCount - 1, Allocator.Persistent);
            var indices = new NativeArray<int>(template.IndexCount - 1, Allocator.Persistent);
            try
            {
                Assert.That(() => template.CopyIndices(indices), Throws.ArgumentException);
                Assert.That(() => template.CopyIndices(default(NativeArray<int>)), Throws.ArgumentException);
                Assert.That(() => HandsSkin.Skin(template, in joints, HandsSkinRig.PalmUp, HandsSkinRig.Uv, AtlasShading.LightDirection,
                                                 vertices, out _, out _), Throws.ArgumentException);
                Assert.That(() => HandsSkin.Skin(template, in joints, HandsSkinRig.PalmUp, HandsSkinRig.Uv, AtlasShading.LightDirection,
                                                 default(NativeArray<AtlasVertex>), out _, out _), Throws.ArgumentException);
                Assert.That(() => HandsSkin.Skin(null, in joints, HandsSkinRig.PalmUp, HandsSkinRig.Uv, AtlasShading.LightDirection,
                                                 vertices, out _, out _), Throws.ArgumentNullException);
            }
            finally
            {
                vertices.Dispose();
                indices.Dispose();
            }
        }

        [Test]
        public void TheSteadyFrame_AllocatesNothing()
        {
            HandsMeshTemplate left = HandsMeshTemplate.Build(HandSide.Left), right = HandsMeshTemplate.Build(HandSide.Right);
            var vertices = new NativeArray<AtlasVertex>(HandsConstants.MaxVertices, Allocator.Persistent);
            var indices = new NativeArray<int>(HandsConstants.MaxIndices, Allocator.Persistent);
            Mesh mesh = AtlasMesh.CreateDynamic("Hands Test", HandsConstants.MaxVertices, HandsConstants.MaxIndices);
            try
            {
                right.CopyIndices(indices);
                JointSet open = HandsSkinRig.Placed(HandModel.RestPose(HandSide.Left), HandsSkinRig.Turn, HandsSkinRig.Place);
                JointSet fist = HandsSkinRig.Placed(HandsSkinRig.Fist(HandSide.Right), HandsSkinRig.Turn, HandsSkinRig.Place);
                float3 normal = math.mul(HandsSkinRig.Turn, HandsSkinRig.PalmUp);
                float3 light = AtlasShading.LightDirection;
                float2 uv = HandsSkinRig.Uv;

                // Once to compile the path, then measured: skin both hands and upload one, twenty frames.
                HandsSkin.Skin(left, in open, normal, uv, light, vertices, out float3 min, out float3 max);
                AtlasMesh.Upload(mesh, vertices, right.VertexCount, indices, right.IndexCount, new Bounds(0.5f * (min + max), max - min));
                Assert.That(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        HandsSkin.Skin(left, in open, normal, uv, light, vertices, out float3 lo, out float3 hi);
                        HandsSkin.Skin(right, in fist, normal, uv, light, vertices, out lo, out hi);
                        AtlasMesh.Upload(mesh, vertices, right.VertexCount, indices, right.IndexCount, new Bounds(0.5f * (lo + hi), hi - lo));
                    }
                }, Is.Not.AllocatingGCMemory());
            }
            finally
            {
                vertices.Dispose();
                indices.Dispose();
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
#endif
