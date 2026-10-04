#if !ATLAS_HEADLESS
using Atlas.Kernel;
using Atlas.Link;
using Atlas.Rendering;
using Atlas.Spaces.Slicer;
using Atlas.World;
using NUnit.Framework;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace Atlas.Spaces.Tests
{
    /// <summary>The parts of the Slicer that need the engine: the Burst jobs, the mesh upload and the module's life cycle.</summary>
    [Category("Unity")]
    public class SlicerUnityTests
    {
        static readonly float3 Centre = new float3(0.75f, 1.25f, 0f);
        static readonly ProfilerMarker s_Marker = new ProfilerMarker("Atlas.Slicer4D");

        static SlicerSliceJob SliceJob(SlicerBuffers buffers, float offset)
        {
            SlicerController.GetTransform(buffers.Body, 0f, 0f, 0f, out float4x4 m, out float4 t);
            return new SlicerSliceJob
            {
                Vertices = buffers.Vertices, Tetrahedra = buffers.Tetrahedra, TetCell = buffers.TetCell, CellUv = buffers.CellUv,
                ModelToSlice = m, Translation = t, Normal = new float4(0f, 0f, 0f, 1f), Offset = offset,
                Centre = Centre, Scale = 0.5f, Light = Atlas.Rendering.AtlasShading.LightDirection,
                ScratchPositions = buffers.ScratchPositions, ScratchNormals = buffers.ScratchNormals,
                ScratchCells = buffers.ScratchCells, OutVertices = buffers.SliceVertices,
                OutBounds = buffers.SliceBounds, OutCounts = buffers.SliceCounts,
                Marker = s_Marker,
            };
        }

        [Test]
        public void TheSliceJob_FillsTheBuffers_AndTheMeshTakesThem()
        {
            for (int b = 0; b < SlicerObjects.Count; b++)
            {
                var buffers = new SlicerBuffers((SlicerObject)b);
                Mesh mesh = AtlasMesh.CreateDynamic("Slicer Test", buffers.SliceCapacity, buffers.SliceCapacity);
                try
                {
                    SliceJob(buffers, 0.1f).Schedule().Complete();
                    int vertexCount = buffers.SliceCounts[0];
                    Assert.That(vertexCount, Is.GreaterThan(0), buffers.Body.ToString());
                    Assert.That(vertexCount, Is.EqualTo(3 * buffers.SliceCounts[1]));
                    Assert.That(vertexCount, Is.LessThanOrEqualTo(buffers.SliceCapacity));

                    var bounds = new Bounds(0.5f * (buffers.SliceBounds[0] + buffers.SliceBounds[1]),
                                            buffers.SliceBounds[1] - buffers.SliceBounds[0]);
                    AtlasMesh.Upload(mesh, buffers.SliceVertices, vertexCount, buffers.Indices, vertexCount, bounds);
                    Assert.That(mesh.GetIndexCount(0), Is.EqualTo((uint)vertexCount));
                    Assert.That(mesh.vertexCount, Is.EqualTo(buffers.SliceCapacity), "the buffer keeps its fixed size");
                    Assert.That(mesh.bounds.extents.magnitude, Is.GreaterThan(0.05f));
                    Assert.That(mesh.bounds.Contains(buffers.SliceVertices[0].Position), Is.True);
                }
                finally
                {
                    buffers.Dispose();
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void TheShadowJob_FillsEveryBoundaryTriangle()
        {
            var buffers = new SlicerBuffers(SlicerObject.Tesseract);
            try
            {
                SlicerController.GetTransform(buffers.Body, 0.3f, 0f, 0f, out float4x4 m, out float4 t);
                new SlicerShadowJob
                {
                    Vertices = buffers.Vertices, Triangles = buffers.ShadowTriangles, TriangleCount = buffers.ShadowTriangleCount,
                    ModelToSlice = m, Translation = t, Perspective = true, Focal = 2f,
                    Centre = Centre, Scale = 0.5f, Light = Atlas.Rendering.AtlasShading.LightDirection,
                    OutVertices = buffers.ShadowVertices, OutBounds = buffers.ShadowBounds,
                }.Schedule().Complete();
                Assert.That(buffers.ShadowTriangleCount, Is.GreaterThan(0));
                Assert.That(math.all(buffers.ShadowBounds[1] > buffers.ShadowBounds[0]), Is.True);
                Assert.That(buffers.ShadowVertices[0].Uv0.y, Is.GreaterThanOrEqualTo(AtlasPalette.EmissiveV));
            }
            finally
            {
                buffers.Dispose();
            }
        }

        [Test]
        public void TheSteadyFrame_AllocatesNothing()
        {
            var buffers = new SlicerBuffers(SlicerObject.Tesseract);
            Mesh mesh = AtlasMesh.CreateDynamic("Slicer Test", buffers.SliceCapacity, buffers.SliceCapacity);
            try
            {
                var bounds = new Bounds(Vector3.zero, Vector3.one);
                // Once to warm up the job system and Burst, then measured.
                SliceJob(buffers, 0f).Schedule().Complete();
                AtlasMesh.Upload(mesh, buffers.SliceVertices, buffers.SliceCounts[0], buffers.Indices, buffers.SliceCounts[0], bounds);
                Assert.That(() =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        SliceJob(buffers, -1f + 0.1f * i).Schedule().Complete();
                        int count = buffers.SliceCounts[0];
                        AtlasMesh.Upload(mesh, buffers.SliceVertices, count, buffers.Indices, count, bounds);
                    }
                }, Is.Not.AllocatingGCMemory());
            }
            finally
            {
                buffers.Dispose();
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void TheModule_RunsItsLifeCycle_InAStandingRoomToo()
        {
            foreach (bool standing in new[] { false, true })
            {
                var room = new RoomModel { FreeRect = standing ? new FreeRect(1.4f, 1.4f) : new FreeRect(3f, 4f), Standing = standing };
                var registry = new ParamRegistry();
                var module = (SlicerSpace)SlicerSpace.Create();
                try
                {
                    Assert.That(module.Id, Is.EqualTo(SpaceId.Slicer));
                    Assert.That(module.Registered, Is.True);
                    module.Configure(room, registry);
                    Assert.That(registry.Find(SlicerParams.W), Is.Not.Null);
                    module.Enter(default(WalkerPose));

                    var frame = new FrameContext { DeltaTime = 1f / 60f, HeadPosition = new float3(-0.75f, 1.6f, 0f) };
                    module.Tick(in frame);
                    module.Upload(in frame);
                    Assert.That(module.SliceTriangles, Is.GreaterThan(0), "the tesseract through its middle");

                    // The Station changes the body and the cut; the module follows in the next frame.
                    registry.Set(SlicerParams.Object, (float)(int)SlicerObject.Cell16, ParamSource.Remote);
                    registry.Set(SlicerParams.W, 0.5f, ParamSource.Remote);
                    registry.Set(SlicerParams.View, 1f, ParamSource.Remote);
                    module.Tick(in frame);
                    module.Upload(in frame);
                    Assert.That(module.Body, Is.EqualTo(SlicerObject.Cell16));
                    Assert.That(module.Controller.W, Is.EqualTo(0.5f));

                    StateMessage state = StateMessage.Identity;
                    module.WriteState(ref state);
                    Assert.That(state.P3, Is.EqualTo(0.5f));
                    Assert.That(state.P7, Is.EqualTo(SlicerState.Pack(SlicerObject.Cell16, SlicerView.Shadow)));

                    module.Exit();
                    Assert.That(module.transform.childCount, Is.EqualTo(0), "Exit leaves nothing behind");
                }
                finally
                {
                    Object.DestroyImmediate(module.gameObject);
                }
            }
        }
    }
}
#endif
