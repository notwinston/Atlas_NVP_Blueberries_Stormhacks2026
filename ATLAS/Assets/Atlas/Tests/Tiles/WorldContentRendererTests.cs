// [P1-B07] The renderer of the generated world: one object, one pooled material and one merged mesh per drawn tile, rented
// as tiles come into view; region tint; enlarged bounds; the drawn-tile count published for telemetry. Needs the engine.
#if !ATLAS_HEADLESS
using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Tiles.Tests
{
    [Category("Unity")]
    public class WorldContentRendererTests
    {
        const double L = 1.885;
        GameObject go; WorldContentRenderer r; MaterialPool pool; WorldGenerator world; TileSelection selection;

        [SetUp]
        public void SetUp()
        {
            pool = MaterialPool.CreateDefault(TileSelection.MaxDrawn);
            world = new WorldGenerator(1, RoomFootprint.Rectangle(3, 3, 2.0 / 1.0613));
            selection = new TileSelection(world.Graph, L);
            go = new GameObject("WorldContentRendererTest") { hideFlags = HideFlags.DontSave };
            r = go.AddComponent<WorldContentRenderer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            Assert.AreEqual(0, pool.Rented, "every tile material went back to the pool");
            Assert.IsFalse(AtlasServices.Has<ITileDrawStats>(), "the stats entry is withdrawn on release");
            pool.Dispose();
        }

        int Select(double rho)
        {
            selection.Rho = rho;
            return selection.Update(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Boost(new double2(0.2, 0.1), -1) }, TileTestPoses.NeverCull());
        }

        [Test]
        public void Materials_AreRentedPerDrawnTile_NotUpFront()
        {
            r.Configure(world, L, pool);
            Assert.AreEqual(0, pool.Rented, "Configure rents nothing");
            int n = Select(2.0);
            r.Draw(selection.Drawn, n);
            Assert.AreEqual(n, pool.Rented); Assert.AreEqual(n, r.MaterialsRented); Assert.AreEqual(n, r.TilesDrawn);
            Assert.Less(n, TileSelection.MaxDrawn);
            r.Draw(selection.Drawn, n);
            Assert.AreEqual(n, pool.Rented, "a second frame rents nothing more");
            r.Draw(selection.Drawn, 3);
            Assert.AreEqual(3, r.TilesDrawn);
            Assert.AreEqual(n, pool.Rented, "slots are kept while the count dips");
            for (int i = 0; i < r.Slots.Count; i++) Assert.AreEqual(i < 3, r.Slots[i].enabled);
            r.Trim();
            Assert.AreEqual(3, pool.Rented, "Trim gives the idle ones back");
            r.Hide();
            Assert.AreEqual(0, r.TilesDrawn);
            foreach (MeshRenderer slot in r.Slots) Assert.IsFalse(slot.enabled);
        }

        [Test]
        public void EachTile_HasItsContentMesh_Anchor_RegionTint_AndBounds()
        {
            r.Configure(world, L, pool);
            int n = Select(3.0);
            r.Draw(selection.Drawn, n);
            Assert.AreSame(r, AtlasServices.Get<ITileDrawStats>());
            var meshOfKey = new Dictionary<int, Mesh>();
            var tints = new HashSet<Vector4>();
            int triangles = 0, withProps = 0;
            for (int i = 0; i < n; i++)
            {
                TileDraw d = selection.Drawn[i];
                TileContent c = world.Content(d.Id);
                WorldTileKey key = WorldTileKey.From(c);
                MeshRenderer slot = r.Slots[i];
                Material mat = slot.sharedMaterial;
                Assert.IsTrue(slot.enabled); Assert.IsTrue(pool.Owns(mat));
                Assert.AreEqual(AtlasWarpCpu.ModeHyperbolic, mat.GetFloat(AtlasShaderIDs.WarpMode));
                Assert.AreEqual(AtlasWarpBounds.Size, slot.localBounds.size.x, "MP 8.3 enlarged bounds");
                Matrix4x4 want = WarpAnchor.ToMatrix(d.Anchor), got = mat.GetMatrix(AtlasShaderIDs.Anchor);
                for (int k = 0; k < 16; k++) Assert.AreEqual(want[k], got[k], "the tile renderer's anchor");
                float3 tint = WorldRegionTint.Linear(c.Region);
                Vector4 baseTint = mat.GetVector(AtlasShaderIDs.BaseTint);
                Assert.AreEqual(tint.x, baseTint.x, 1e-6f); Assert.AreEqual(tint.y, baseTint.y, 1e-6f); Assert.AreEqual(tint.z, baseTint.z, 1e-6f);
                tints.Add(baseTint);
                Mesh mesh = slot.GetComponent<MeshFilter>().sharedMesh;
                int code = (key.Code << 2) | (d.Lod == TileLod.Near ? 0 : d.Lod == TileLod.Mid ? 1 : 2);
                if (meshOfKey.TryGetValue(code, out Mesh known)) Assert.AreSame(known, mesh, "tiles with the same content share one mesh");
                else meshOfKey.Add(code, mesh);
                int t = (int)(mesh.GetIndexCount(0) / 3);
                Assert.AreEqual(new WorldTileMeshBuilder().Build(5, L, d.Lod, key).Triangles, t);
                triangles += t;
                if (key.HasProps) withProps++;
            }
            Assert.AreEqual(triangles, r.TrianglesDrawn);
            Assert.AreEqual(meshOfKey.Count, r.Meshes.Count, "one mesh per (level, content key)");
            Assert.AreEqual(4, tints.Count, "MP 3.4 four regions, four hues in view from tile zero");
            Assert.Greater(withProps, 0, "props are in view at rho 3");
        }

        [Test]
        public void LengthScale_RebuildsMeshesInPlace_AFewPerFrame()
        {
            r.Configure(world, L, pool);
            int n = Select(3.0);
            r.Draw(selection.Drawn, n);
            long built = r.Meshes.Builds;
            Mesh first = r.Slots[0].GetComponent<MeshFilter>().sharedMesh;
            float before = first.bounds.size.x;
            r.SetLengthScale(2.4);
            r.Draw(selection.Drawn, n);
            Assert.AreEqual(WorldTileMeshCache.MaxStaleRebuildsPerFrame, r.Meshes.Builds - built);
            Assert.AreSame(first, r.Slots[0].GetComponent<MeshFilter>().sharedMesh, "the Mesh object is kept");
            Assert.Greater(first.bounds.size.x, before * 1.2f, "and holds the larger tile");
            for (int f = 0; f < 16; f++) r.Draw(selection.Drawn, n);
            Assert.AreEqual(r.Meshes.Count, r.Meshes.Builds - built, "every mesh in view is rebuilt once");
        }
    }
}
#endif
