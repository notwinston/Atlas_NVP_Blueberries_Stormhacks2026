using System;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using UnityEditor;
using UnityEngine;
#endif

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class AlignedCellsViewTests
    {
        // Acceptance P1-B10; Master Plan §8.5 S3 row; §4.7 "an instance is one draw". The 4 x 5 m room of §4.7 (free rectangle
        // 3.4 x 4.4 m) and lane C's 4 x 5 m sample room with its table.
        [Test]
        public void Mode2_TileInstanceDrawCalls_4x5Room()
        {
            var graph = new TileGraph(5);
            var list = new CellInstanceList();
            foreach (CellGrid grid in new[] { CellGrid.FromFreeRectangle(new FreeRectangle(3.4, 4.4)), CellGrid.FromRoom(Fixtures.Room(RoomFixtures.Sample4x5Table)) })
            {
                int worst = 0; int2 at = default;
                for (int z = 0; z < grid.CountZ; z++)
                    for (int x = 0; x < grid.CountX; x++)
                    {
                        SightLines.Build(grid, graph, new int2(x, z), new CellFrame(TileId.Zero, 0), list);
                        int draws = list.Count * 2;
                        Assert.LessOrEqual(draws, 120);
                        if (draws > worst) { worst = draws; at = new int2(x, z); }
                    }
                TestContext.WriteLine($"S3 tile instances, {grid.CountX} x {grid.CountZ}: worst {worst} draw calls/frame at cell ({at.x},{at.y})");
                Assert.AreEqual(grid.CountX == 3 ? 34 : 10, worst);
            }
        }

        // Master Plan §8.3 AtlasGates.hlsl, decision 3: the clip reads the frozen block's four vectors, and its test is GateMath's.
        [Test]
        public void Mode2_GatesShaderReadsTheFourGateVectors()
        {
            string hlsl = Fixtures.ProjectText("Assets/Atlas/Rendering/Shaders/Resources/AtlasGates.hlsl");
            foreach (string gate in new[] { "_Gate0", "_Gate1", "_Gate2", "_Gate3" }) StringAssert.Contains(gate, hlsl);
            StringAssert.DoesNotContain("_Gates", hlsl);
            StringAssert.Contains("for (int i = 0; i < 4; i++)", hlsl);
            StringAssert.Contains("clip(gate.z * (g.x * d.y - g.y * d.x))", hlsl);
            StringAssert.Contains("_WorldSpaceCameraPos.xz", hlsl);
            string core = Fixtures.ProjectText("Assets/Atlas/Rendering/Shaders/Resources/AtlasCore.hlsl");
            foreach (string gate in new[] { "_Gate0", "_Gate1", "_Gate2", "_Gate3" }) StringAssert.Contains(gate, core);
            string test = Fixtures.ProjectText("Assets/Atlas/Tests/AlignedCells/AlignedCellsGatesTest.shader");
            StringAssert.Contains("Assets/Atlas/Rendering/Shaders/Resources/AtlasGates.hlsl", test);
        }

        // Master Plan §3.5 post at every corner; §4.7
        [Test]
        public void Mode2_PostMesh()
        {
            CellGrid grid = CellGrid.FromFreeRectangle(new FreeRectangle(3.4, 4.4));
            var mesh = new AtlasMeshBuilder();
            Assert.AreEqual(4 * 48, PostMesh.Build(grid, TilePalette.Default, mesh));
            Assert.AreEqual(4 * 48, mesh.TriangleCount);
            Assert.AreEqual(48, PostMesh.TrianglesPerPost);
            Assert.AreEqual(0, mesh.BoundsMin.y, 1e-6); Assert.AreEqual(2.6f, mesh.BoundsMax.y, 1e-6);
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                Assert.AreEqual(0, mesh.Vertices[i].LineWeight, "no line on a post");
                Assert.AreEqual(TilePalette.Default.Post, mesh.Vertices[i].Uv0);
                Assert.AreEqual(1f, math.length(mesh.Vertices[i].Normal), 1e-5);
            }
            for (int X = 1; X < 3; X++)
                for (int Z = 1; Z < 3; Z++)
                {
                    double2 c = grid.Corner(X, Z);
                    bool any = false;
                    for (int i = 0; i < mesh.IndexCount; i += 3)
                    {
                        float3 a = mesh.Vertices[mesh.Indices[i]].Position, b = mesh.Vertices[mesh.Indices[i + 1]].Position, d = mesh.Vertices[mesh.Indices[i + 2]].Position;
                        float3 mid = (a + b + d) / 3;
                        if (math.abs(mid.x - (float)c.x) > 0.2f || math.abs(mid.z - (float)c.y) > 0.2f) continue;
                        any = true;
                        float3 nrm = math.cross(b - a, d - a);
                        if (math.abs(nrm.y) > 1e-7f) Assert.Greater(nrm.y, 0);
                        else
                        {
                            var radial = new float3(mid.x - (float)c.x, 0, mid.z - (float)c.y);
                            Assert.Greater(math.dot(nrm, radial), 0);
                        }
                    }
                    Assert.IsTrue(any);
                    // bottom ring extremes (seam vertex is duplicated, so use min/max)
                    float x0 = 9e9f, x1 = -9e9f, z0 = 9e9f, z1 = -9e9f;
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        float3 v = mesh.Vertices[i].Position;
                        if (v.y == 0 && math.abs(v.x - (float)c.x) < 0.2f && math.abs(v.z - (float)c.y) < 0.2f)
                        { x0 = math.min(x0, v.x); x1 = math.max(x1, v.x); z0 = math.min(z0, v.z); z1 = math.max(z1, v.z); }
                    }
                    Assert.AreEqual(c.x, (x0 + x1) / 2, 1e-6); Assert.AreEqual(c.y, (z0 + z1) / 2, 1e-6);
                    Assert.AreEqual(2 * TileChart.PostRadius, x1 - x0, 1e-6);
                }
            // The corner demo has one post; the mesh of a grid appended twice is twice the mesh.
            var one = new AtlasMeshBuilder();
            Assert.AreEqual(48, PostMesh.Build(CellGrid.FromFreeRectangle(new FreeRectangle(1.2, 1.4)), TilePalette.Default, one));
            Assert.AreEqual(48, PostMesh.Build(CellGrid.FromFreeRectangle(new FreeRectangle(1.2, 1.4)), TilePalette.Default, one));
            Assert.AreEqual(96, one.TriangleCount);
        }

        // Master Plan §4.7 "an instance is one draw": the flat cell is one small mesh, within the far tile's cap (§8.4)
        [Test]
        public void Mode2_FlatCellMesh()
        {
            foreach (double g in new[] { 0.6, 0.8, 1.35 })
            {
                var gen = new TileMeshGenerator(TilePalette.Default);
                TileMeshLayout lay = gen.Build(TileMeshSpec.FlatCell(g, kerbMask: 0, postMask: 0));
                Assert.LessOrEqual(lay.TriangleCount, TileChart.TriangleCap(TileLod.Far));
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < lay.VertexCount; i++)
                {
                    float3 p = gen.Vertices[i].Position;
                    Assert.AreEqual(0f, p.y, 1e-6f);
                    lo = math.min(lo, math.min(p.x, p.z)); hi = math.max(hi, math.max(p.x, p.z));
                }
                Assert.AreEqual(-g / 2, lo, 1e-5); Assert.AreEqual(g / 2, hi, 1e-5);
            }
        }

#if !ATLAS_HEADLESS
        // Master Plan §8.3 AtlasGates.hlsl; kernel.md §6.5: the include compiles on its own against the four gate vectors
        [Test, Category("Unity")]
        public void Mode2_GatesShaderCompiles()
        {
            var shader = Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
            Shader surface = AtlasLook.LoadSurfaceShader();
            Assert.IsNotNull(surface);
            Assert.IsFalse(ShaderUtil.ShaderHasError(surface));
        }

        // Master Plan §8.3 per-material block, no MaterialPropertyBlock; §4.7 posts; decision 3: MaterialPool.SetGate
        [Test, Category("Unity")]
        public void Mode2_ViewUploads()
        {
            var root = new GameObject("AlignedCellsTestRoot");
            MaterialPool pool = MaterialPool.CreateDefault(64);
            CellGrid grid = CellGrid.FromFreeRectangle(new FreeRectangle(3.4, 4.4));
            var view = new AlignedCellsView(root.transform, pool, grid);
            try
            {
                Assert.AreEqual(1, pool.Rented, "only the posts' material before anything is drawn");
                var graph = new TileGraph(5);
                var list = new CellInstanceList();
                SightLines.Build(grid, graph, new int2(0, 0), new CellFrame(TileId.Zero, 0), list);
                view.Apply(list);
                Assert.AreEqual(list.Count, view.ActiveInstanceCount);
                Assert.AreEqual(list.Count + 1, pool.Rented);
                var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
                Assert.AreEqual(SightLines.MaxInstances + 1, renderers.Length);
                int active = 0;
                var gateIds = new[] { AtlasShaderIDs.Gate0, AtlasShaderIDs.Gate1, AtlasShaderIDs.Gate2, AtlasShaderIDs.Gate3 };
                foreach (var mr in renderers)
                {
                    Assert.IsFalse(mr.HasPropertyBlock());
                    if (mr.gameObject.name == "Posts")
                    {
                        Assert.IsTrue(mr.gameObject.activeSelf);
                        Assert.AreSame(view.PostsMaterial, mr.sharedMaterial);
                        foreach (int id in gateIds) Assert.AreEqual(Vector4.zero, mr.sharedMaterial.GetVector(id));
                        continue;
                    }
                    if (!mr.gameObject.activeSelf) continue;
                    int i = active++;
                    var inst = list[i];
                    Assert.IsTrue(pool.Owns(mr.sharedMaterial));
                    Assert.AreNotSame(view.PostsMaterial, mr.sharedMaterial);
                    Assert.AreEqual(0f, mr.sharedMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                    for (int row = 0; row < 4; row++)
                    {
                        double4 g = row < inst.GateCount ? inst.Gate(row) : double4.zero;
                        Vector4 v = mr.sharedMaterial.GetVector(gateIds[row]);
                        Assert.AreEqual(g.x, v.x, 1e-5); Assert.AreEqual(g.y, v.y, 1e-5);
                        Assert.AreEqual(g.z, v.z, 1e-5); Assert.AreEqual(0, v.w, 1e-5);
                    }
                    float yaw = mr.transform.localRotation.eulerAngles.y;
                    Assert.AreEqual(((-90 * inst.Frame.Turn) % 360 + 360) % 360, yaw, 1e-3);
                    float3 tint = WorldRegionTint.Linear(Regions.RegionOf(inst.Frame.Tile));
                    Vector4 baseTint = mr.sharedMaterial.GetVector(AtlasShaderIDs.BaseTint);
                    Assert.AreEqual(tint.x, baseTint.x, 1e-6); Assert.AreEqual(tint.y, baseTint.y, 1e-6); Assert.AreEqual(tint.z, baseTint.z, 1e-6);
                }
                Assert.AreEqual(list.Count, active);

                // A shorter list hides the rest and clears the gates it no longer has.
                SightLines.Build(grid, graph, new int2(1, 1), new CellFrame(TileId.Zero, 0), list, 0);
                view.Apply(list);
                Assert.AreEqual(1, view.ActiveInstanceCount);
                foreach (int id in gateIds) Assert.AreEqual(Vector4.zero, view.InstanceMaterial(0).GetVector(id));
                view.Dispose();
                Assert.AreEqual(0, pool.Rented);
            }
            finally
            {
                view.Dispose();
                pool.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
#endif
    }
}
