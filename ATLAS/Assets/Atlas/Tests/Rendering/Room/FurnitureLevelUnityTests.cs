#if !ATLAS_HEADLESS
using Atlas.Rendering.Room;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>The level swap on the real RoomContent: two meshes per object, drawn by level.</summary>
    [Category("Unity")]
    public class FurnitureLevelUnityTests
    {
        GameObject root;
        MaterialPool pool;
        RoomContent content;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("room");
            pool = MaterialPool.CreateDefault();
            content = new RoomContent(root.transform, pool) { Instant = true };
            RoomModel room = RoomTestUtil.Rectangle(4f, 4f);
            room.Objects = new[]
            {
                new RoomObject { Id = 1, Category = ObjectCategory.Table, Size = new float3(1f, 0.75f, 0.6f), TriTarget = 3000 },
                new RoomObject { Id = 2, Category = ObjectCategory.Chair, Size = new float3(0.5f, 0.9f, 0.5f), TriTarget = 3000 },
            };
            content.Build(room);
        }

        [TearDown]
        public void TearDown()
        {
            if (content != null) content.Dispose();
            if (pool != null) pool.Dispose();
            if (root != null) Object.DestroyImmediate(root);
        }

        static Mesh MeshOfTriangles(int count, string name)
        {
            var data = new AtlasMeshBuilder(count * 3, count * 3);
            for (int t = 0; t < count; t++)
            {
                float x = 0.001f * t;
                int a = data.AddVertex(new float3(x, 0f, 0f), new float3(0f, 0f, -1f), float2.zero, float2.zero);
                int b = data.AddVertex(new float3(x, 0.1f, 0f), new float3(0f, 0f, -1f), float2.zero, float2.zero);
                int c = data.AddVertex(new float3(x + 0.05f, 0f, 0f), new float3(0f, 0f, -1f), float2.zero, float2.zero);
                data.AddTriangle(a, b, c);
            }
            return AtlasMesh.Create(data, name);
        }

        [Test]
        public void Level_SwapsTheDrawnMesh_AndLightMissingFallsBackToFull()
        {
            var full1 = MeshOfTriangles(30, "full 1");
            var light1 = MeshOfTriangles(10, "light 1");
            var full2 = MeshOfTriangles(20, "full 2");
            Assert.That(content.SetGeneratedMesh(1, full1, "aa"), Is.True);
            Assert.That(content.SetGeneratedLightMesh(1, light1, "bb"), Is.True);
            Assert.That(content.SetGeneratedMesh(2, full2, "cc"), Is.True);

            Assert.That(content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            content.TryGetObject(1, out IRoomObjectView one);
            content.TryGetObject(2, out IRoomObjectView two);
            Assert.That(one.TriangleCount, Is.EqualTo(30));
            Assert.That(one.Data.AssetHashLight, Is.EqualTo("bb"));

            content.FurnitureLevel = FurnitureLevel.Light;
            Assert.That(one.TriangleCount, Is.EqualTo(10));
            Assert.That(((RoomObjectView)one).MeshTransform.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(light1));
            Assert.That(two.TriangleCount, Is.EqualTo(20), "no light copy: the full one stays");
            Assert.That(content.TrianglesPerEye, Is.EqualTo(content.ShellTriangleCount + 30));

            content.FurnitureLevel = FurnitureLevel.Full;
            Assert.That(one.TriangleCount, Is.EqualTo(30));
        }

        [Test]
        public void LightCopyArrivingAtLight_IsDrawnAtOnce()
        {
            content.SetGeneratedMesh(1, MeshOfTriangles(30, "full"), "aa");
            content.FurnitureLevel = FurnitureLevel.Light;
            content.TryGetObject(1, out IRoomObjectView one);
            Assert.That(one.TriangleCount, Is.EqualTo(30));
            content.SetGeneratedLightMesh(1, MeshOfTriangles(10, "light"), "bb");
            Assert.That(one.TriangleCount, Is.EqualTo(10));
        }

        [Test]
        public void LightMesh_NeedsAGeneratedMesh()
        {
            var light = MeshOfTriangles(10, "light");
            Assert.That(content.SetGeneratedLightMesh(1, light, "bb"), Is.False, "the object still shows its box");
            Assert.That(content.SetGeneratedLightMesh(99, light, "bb"), Is.False);
            Assert.That(content.SetGeneratedLightMesh(1, null, "bb"), Is.False);
            Object.DestroyImmediate(light);
        }

        [Test]
        public void ResetPresentation_ReturnsTheLevelToFull()
        {
            content.SetGeneratedMesh(1, MeshOfTriangles(30, "full"), "aa");
            content.SetGeneratedLightMesh(1, MeshOfTriangles(10, "light"), "bb");
            content.FurnitureLevel = FurnitureLevel.Light;
            content.ResetPresentation();
            Assert.That(content.FurnitureLevel, Is.EqualTo(FurnitureLevel.Full));
            content.TryGetObject(1, out IRoomObjectView one);
            Assert.That(one.TriangleCount, Is.EqualTo(30));
        }

        [Test]
        public void NewFullMesh_ReleasesTheOldPair_AndClearReleasesBoth()
        {
            var full = MeshOfTriangles(30, "full");
            var light = MeshOfTriangles(10, "light");
            content.SetGeneratedMesh(1, full, "aa");
            content.SetGeneratedLightMesh(1, light, "bb");
            content.SetGeneratedMesh(1, MeshOfTriangles(40, "full again"), "cc");
            Assert.That(full == null, Is.True, "the old full copy is destroyed");
            Assert.That(light == null, Is.True, "and so is the light copy that went with it");
            content.TryGetObject(1, out IRoomObjectView one);
            Assert.That(one.Data.AssetHashLight, Is.EqualTo(""));

            var light2 = MeshOfTriangles(10, "light 2");
            content.SetGeneratedLightMesh(1, light2, "dd");
            content.Clear();
            Assert.That(light2 == null, Is.True);
        }

        // ---- with a frame loop: swaps wait their turn and cross-fade (Instant off) -------------------------------

        [Test]
        public void LightCopy_ArrivingWhileItsFullCopyIsStillQueued_WaitsWithIt()
        {
            content.Instant = false;
            content.Tick(0.016f);
            var full1 = MeshOfTriangles(30, "full 1");
            var full2 = MeshOfTriangles(20, "full 2");
            var light2 = MeshOfTriangles(8, "light 2");
            Assert.That(content.SetGeneratedMesh(1, full1, "aa"), Is.True);      // begins at once: one swap per frame
            Assert.That(content.SetGeneratedMesh(2, full2, "cc"), Is.True);      // queued behind it
            content.TryGetObject(2, out IRoomObjectView two);
            Assert.That(content.IsMeshDrawn(full2), Is.True, "waiting for its swap counts as drawn");

            Assert.That(content.SetGeneratedLightMesh(2, light2, "dd"), Is.True, "accepted while its full copy waits");
            Assert.That(two.Data.AssetHashLight, Is.EqualTo(""), "written with the swap");
            Assert.That(two.Shown, Is.Not.EqualTo(MeshState.Ready));

            content.FurnitureLevel = FurnitureLevel.Light;
            content.Tick(0.016f);                                                 // the queued swap begins
            Assert.That(two.Shown, Is.EqualTo(MeshState.Ready));
            Assert.That(two.Data.AssetHashLight, Is.EqualTo("dd"));
            Assert.That(two.TriangleCount, Is.EqualTo(8), "drawn at Light as soon as the swap has begun");
            Assert.That(content.IsMeshDrawn(light2), Is.True);
        }

        [Test]
        public void CrossFade_WhileTheLightCopyIsDrawn_FadesThatCopyOut_AndDestroysTheOtherAtOnce()
        {
            var full = MeshOfTriangles(30, "full");
            var light = MeshOfTriangles(10, "light");
            content.SetGeneratedMesh(1, full, "aa");
            content.SetGeneratedLightMesh(1, light, "bb");
            content.FurnitureLevel = FurnitureLevel.Light;
            content.TryGetObject(1, out IRoomObjectView one);
            Assert.That(one.TriangleCount, Is.EqualTo(10));

            content.Instant = false;
            content.Tick(0.016f);
            var next = MeshOfTriangles(40, "next full");
            Assert.That(content.SetGeneratedMesh(1, next, "cc", 0.3f), Is.True);

            Assert.That(full == null, Is.True, "the copy that was not drawn goes at once");
            Assert.That(light == null, Is.False, "the drawn copy fades out");
            Assert.That(content.IsMeshDrawn(light), Is.True);
            Assert.That(one.TriangleCount, Is.EqualTo(40), "the new full copy: its light copy has not come");
            Assert.That(one.Data.AssetHashLight, Is.EqualTo(""));

            content.Tick(0.1f);
            Assert.That(light == null, Is.False);
            content.Tick(0.3f);
            Assert.That(light == null, Is.True, "destroyed once the fade has ended");
            Assert.That(content.IsMeshDrawn(light), Is.False);
            content.Clear();
            Assert.That(next == null, Is.True, "Clear releases the rest");
        }

        [Test]
        public void ClearWithSwapsPending_ReleasesEveryOwnedMeshOnce_AndDrawsNothing()
        {
            content.Instant = false;
            content.Tick(0.016f);
            var full1 = MeshOfTriangles(30, "full 1");
            var light1 = MeshOfTriangles(10, "light 1");
            var full2 = MeshOfTriangles(20, "full 2");
            var light2 = MeshOfTriangles(8, "light 2");
            content.SetGeneratedMesh(1, full1, "aa");
            content.SetGeneratedLightMesh(1, light1, "bb");
            content.SetGeneratedMesh(2, full2, "cc");        // queued
            content.SetGeneratedLightMesh(2, light2, "dd");
            content.Clear();
            Assert.That(full1 == null && light1 == null && full2 == null && light2 == null, Is.True);
            Assert.That(content.IsMeshDrawn(full1), Is.False);
            Assert.DoesNotThrow(() => content.Clear());
        }

        // A mesh swap in a curved space: the outgoing copy is warped and has the warp's bounds on the frame the swap
        // begins, not only from the next Tick, or it is culled for a frame while the new mesh is still clear (a blink).
        [Test]
        public void Swap_UnderTheWarp_TheOutgoingCopyFollowsFromItsFirstFrame()
        {
            content.Instant = false;
            content.TryGetObject(1, out IRoomObjectView one);
            Matrix4x4 anchor = Matrix4x4.Translate(new Vector3(2f, 0f, 1f));
            one.Material.SetFloat(AtlasShaderIDs.WarpMode, 2f);
            one.Material.SetMatrix(AtlasShaderIDs.Anchor, anchor);
            AtlasWarpBounds.Apply(one.Renderer);

            Assert.That(content.SetGeneratedMesh(1, MeshOfTriangles(20, "generated"), "aa"), Is.True);
            var view = (RoomObjectView)one;
            Assert.That(view.IsFading, Is.True);
            Assert.That(view.OutRenderer.enabled, Is.True);
            Assert.That(view.OutRenderer.localBounds.size.x, Is.EqualTo(AtlasWarpBounds.Size));
            Assert.That(view.OutMaterial.GetFloat(AtlasShaderIDs.WarpMode), Is.EqualTo(2f));
            Assert.That(view.OutMaterial.GetMatrix(AtlasShaderIDs.Anchor), Is.EqualTo(anchor));
        }
    }
}
#endif
