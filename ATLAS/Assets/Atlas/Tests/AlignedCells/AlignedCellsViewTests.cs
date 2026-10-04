using Atlas.Kernel;
using Atlas.Rendering;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class AlignedCellsViewTests
    {
        GameObject root;
        Material template;
        AlignedCellsView view;
        CellGrid grid;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("AlignedCellsTestRoot");
            template = new Material(Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates"));
            grid = CellGrid.FromFreeRectangle(new FreeRectangle(3.4, 4.4));
            view = new AlignedCellsView(root.transform, template, grid);
        }

        [TearDown]
        public void TearDown()
        {
            view?.Dispose();
            Object.DestroyImmediate(template);
            Object.DestroyImmediate(root);
        }

        // Acceptance P1-B10; Master Plan §8.5 S3 row; §4.7 "an instance is one draw"
        [Test]
        public void Mode2_TileInstanceDrawCalls_4x5Room()
        {
            Assert.AreEqual(3, grid.CountX); Assert.AreEqual(3, grid.CountZ);
            var graph = new TileGraph(5);
            var list = new CellInstanceList();
            int worst = 0; int2 at = default;
            for (int z = 0; z < 3; z++)
                for (int x = 0; x < 3; x++)
                {
                    SightLines.Build(grid, graph, new int2(x, z), new CellFrame(TileId.Zero, 0), list);
                    view.Apply(list);
                    Assert.AreEqual(list.Count, view.ActiveInstanceCount);
                    int draws = view.ActiveInstanceCount * 2;
                    Assert.LessOrEqual(draws, 120);
                    if (draws > worst) { worst = draws; at = new int2(x, z); }
                }
            TestContext.WriteLine($"S3 tile instances: worst {worst} draw calls/frame at cell ({at.x},{at.y})");
        }

        // Master Plan §8.3 AtlasGates.hlsl; kernel.md §6.5
        [Test]
        public void Mode2_GatesShaderCompiles()
        {
            var shader = Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
        }

        // Master Plan §8.3 per-material block, no MaterialPropertyBlock; §4.7 posts
        [Test]
        public void Mode2_ViewUploads()
        {
            var graph = new TileGraph(5);
            var list = new CellInstanceList();
            SightLines.Build(grid, graph, new int2(0, 0), new CellFrame(TileId.Zero, 0), list);
            view.Apply(list);
            int active = 0; Material postsMat = null;
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            Assert.AreEqual(SightLines.MaxInstances + 1, renderers.Length);
            for (int r = 0; r < renderers.Length; r++)
            {
                var mr = renderers[r];
                Assert.IsFalse(mr.HasPropertyBlock());
                if (mr.gameObject.name == "Posts")
                {
                    Assert.IsTrue(mr.gameObject.activeSelf);
                    postsMat = mr.sharedMaterial;
                    Assert.AreEqual(Matrix4x4.zero, postsMat.GetMatrix(AtlasShaderIDs.Gates));
                    continue;
                }
                if (!mr.gameObject.activeSelf) continue;
                int i = active++;
                var inst = list[i];
                var m = mr.sharedMaterial.GetMatrix(AtlasShaderIDs.Gates);
                for (int row = 0; row < 4; row++)
                {
                    double4 g = row < inst.GateCount ? inst.Gate(row) : double4.zero;
                    Assert.AreEqual(g.x, m[row, 0], 1e-5); Assert.AreEqual(g.y, m[row, 1], 1e-5);
                    Assert.AreEqual(g.z, m[row, 2], 1e-5); Assert.AreEqual(0, m[row, 3], 1e-5);
                }
                float yaw = mr.transform.localRotation.eulerAngles.y;
                Assert.AreEqual(((-90 * inst.Frame.Turn) % 360 + 360) % 360, yaw, 1e-3);
            }
            Assert.AreEqual(list.Count, active);
            Assert.IsNotNull(postsMat);
            foreach (var mr in renderers) if (mr.gameObject.name != "Posts") Assert.AreNotSame(postsMat, mr.sharedMaterial);
        }

        // Master Plan §3.5 post at every corner; §4.7
        [Test]
        public void Mode2_PostMesh()
        {
            var mesh = new Mesh();
            PostMesh.Build(grid, TilePalette.Provisional, mesh);
            Assert.AreEqual(4 * 48, mesh.triangles.Length / 3);
            var v = mesh.vertices; var t = mesh.triangles;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in v) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); }
            Assert.AreEqual(0, minY, 1e-6); Assert.AreEqual(2.6f, maxY, 1e-6);
            for (int X = 1; X < 3; X++)
                for (int Z = 1; Z < 3; Z++)
                {
                    double2 c = grid.Corner(X, Z);
                    bool any = false;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], d = v[t[i + 2]];
                        Vector3 mid = (a + b + d) / 3;
                        if (Mathf.Abs(mid.x - (float)c.x) > 0.2f || Mathf.Abs(mid.z - (float)c.y) > 0.2f) continue;
                        any = true;
                        var nrm = Vector3.Cross(b - a, d - a);
                        if (Mathf.Abs(nrm.y) > 1e-7f) Assert.Greater(nrm.y, 0);
                        else
                        {
                            var radial = new Vector3(mid.x - (float)c.x, 0, mid.z - (float)c.y);
                            Assert.Greater(Vector3.Dot(nrm, radial), 0);
                        }
                    }
                    Assert.IsTrue(any);
                    // bottom ring extremes (seam vertex is duplicated, so use min/max)
                    float x0 = 9e9f, x1 = -9e9f, z0 = 9e9f, z1 = -9e9f;
                    for (int i = 0; i < v.Length; i++)
                        if (v[i].y == 0 && Mathf.Abs(v[i].x - (float)c.x) < 0.2f && Mathf.Abs(v[i].z - (float)c.y) < 0.2f)
                        { x0 = Mathf.Min(x0, v[i].x); x1 = Mathf.Max(x1, v[i].x); z0 = Mathf.Min(z0, v[i].z); z1 = Mathf.Max(z1, v[i].z); }
                    Assert.AreEqual(c.x, (x0 + x1) / 2, 1e-6); Assert.AreEqual(c.y, (z0 + z1) / 2, 1e-6);
                }
            Object.DestroyImmediate(mesh);
        }
    }
}
