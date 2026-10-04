using Atlas.Kernel;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using Atlas.Rendering;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using NUnit.Framework.Constraints;
#endif

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomViewTests
    {
        static (CellGrid grid, FakeRoomContent content, AlignedCellsRoomView view) Make(RoomModel r, bool enter = true)
        {
            var grid = CellGrid.FromRoom(r);
            var content = new FakeRoomContent(r);
            var view = new AlignedCellsRoomView(content, grid);
            if (enter) view.Enter();
            return (grid, content, view);
        }

        static void Show(AlignedCellsRoomView room, CellGrid grid, int cx, int cz, CellInstanceList list)
        {
            SightLines.Build(grid, new TileGraph(5), new int2(cx, cz), new CellFrame(TileId.Zero, 0), list);
            room.Apply(list);
        }

        static bool AnyInstanceIn(CellGrid grid, RoomObject o, CellInstanceList list)
        {
            // independent of CellRange: the cells of a fine sampling of the footprint
            for (int k = 0; k < list.Count; k++)
                for (int i = 0; i <= 8; i++)
                    for (int j = 0; j <= 8; j++)
                    {
                        double3 p = RoomObjectCells.BoxToAnchor(o, new double3((i / 8.0 - 0.5) * o.Size.x, 0, (j / 8.0 - 0.5) * o.Size.z));
                        if (grid.CellAt(new double2(p.x, p.z)).Equals(list[k].Cell)) return true;
                    }
            return false;
        }

        // Criterion: row output "Home walls on the edge of the cell grid"; lane C contract §9.2 (S3: walls Full, floor hidden,
        // objects shown); decision 8: the room is the shared room content's, asked for once and never copied.
        [Test]
        public void Mode2_Room_PresentsTheSharedRoom()
        {
            var (grid, content, view) = Make(Fixtures.Room(RoomFixtures.BudgetRoom), enter: false);
            CollectionAssert.AreEqual(new[] { "Build" }, content.Calls, "nothing is asked before Enter");
            view.Enter();
            CollectionAssert.AreEqual(new[] { "Build", "SetWallMode:Full", "FloorVisible:False", "ObjectsVisible:True" }, content.Calls);
            Assert.AreEqual(0f, content.LastSeconds, "the walls stand at once: the space is registered");
            Assert.AreEqual(WallMode.Full, content.Walls);
            Assert.AreEqual(12, view.ObjectCount);
            Assert.IsTrue(view.Dirty);

            // A room content that is not built yet is left alone.
            var empty = new FakeRoomContent();
            var late = new AlignedCellsRoomView(empty, grid);
            late.Enter();
            Assert.AreEqual(0, empty.Calls.Count); Assert.AreEqual(0, late.ObjectCount);
            // ... and is presented when it is built while the space is entered.
            empty.Build(Fixtures.Room(RoomFixtures.BudgetRoom));
            CollectionAssert.AreEqual(new[] { "Build", "SetWallMode:Full", "FloorVisible:False", "ObjectsVisible:True" }, empty.Calls);
            Assert.AreEqual(12, late.ObjectCount);
            late.Exit();
            empty.Build(Fixtures.Room(RoomFixtures.Sample3x3));
            Assert.AreEqual(5, empty.Calls.Count, "after Exit the room is no longer followed");
        }

        // Criterion: Master Plan §4.7 "never duplicated per instance"; once per eye (one view per object, whatever is drawn)
        [Test]
        public void Mode2_Room_ObjectDrawnOncePerEye()
        {
            var (grid, content, view) = Make(Fixtures.Room(RoomFixtures.BudgetRoom));
            var list = new CellInstanceList();
            Show(view, grid, 0, 1, list);
            Assert.AreEqual(23, list.Count);
            int in22 = 0;
            for (int k = 0; k < list.Count; k++) if (list[k].Cell.x == 2 && list[k].Cell.y == 2) in22++;
            Assert.AreEqual(3, in22, "instances of cell (2,2)");
            for (int cz = 0; cz < 4; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    Show(view, grid, cx, cz, list);
                    Assert.AreEqual(12, view.ObjectCount);
                    Assert.AreEqual(12, content.ObjectCount, "S3 adds no object to the room");
                    Assert.LessOrEqual(view.VisibleObjectCount, 12);
                }
            Assert.AreEqual(4, content.Calls.Count, "nothing but the presentation was asked of the room");
        }

        // Criterion: Master Plan §4.7 "whenever any instance of its cell is drawn"
        [Test]
        public void Mode2_Room_ObjectVisibleIffCellDrawn()
        {
            RoomModel bud = Fixtures.Room(RoomFixtures.BudgetRoom);
            var (grid, content, view) = Make(bud);
            var list = new CellInstanceList();
            bool sawHidden = false;
            for (int depth = 0; depth <= SightLines.ViewRange; depth++)
                for (int cz = 0; cz < 4; cz++)
                    for (int cx = 0; cx < 3; cx++)
                    {
                        SightLines.Build(grid, new TileGraph(5), new int2(cx, cz), new CellFrame(TileId.Zero, 0), list, depth);
                        view.Apply(list);
                        Assert.IsFalse(view.Dirty);
                        int vis = 0;
                        for (int i = 0; i < 12; i++)
                        {
                            bool any = false;
                            for (int k = 0; k < list.Count; k++) any |= view.ObjectCells(i).Contains(list[k].Cell);
                            Assert.AreEqual(any, view.ObjectVisible(i), $"object {i} at cell ({cx},{cz})");
                            Assert.AreEqual(AnyInstanceIn(grid, bud.Objects[i], list), view.ObjectVisible(i), $"object {i} at cell ({cx},{cz}), by its footprint");
                            if (any) vis++; else sawHidden = true;
                        }
                        Assert.AreEqual(vis, view.VisibleObjectCount);
                    }
            Assert.IsTrue(sawHidden, "the 3 x 4 grid has cells out of sight");

            // sample_3x3 with four synthetic objects: every cell sees every cell, so all objects are always drawn
            RoomModel r3 = Fixtures.Rectangle(2.4f, 2.4f, 0.3f,
                Fixtures.Obj(1, 1.5f, 0, .3f, .8f, .5f, 0), Fixtures.Obj(2, -1.5f, 0, .3f, .8f, .5f, 0),
                Fixtures.Obj(3, 0, 1.5f, .5f, .8f, .3f, 0), Fixtures.Obj(4, 0, -1.5f, .5f, .8f, .3f, 0.4f));
            var (g3, c3, rv) = Make(r3);
            Assert.AreEqual(3, g3.CountX); Assert.AreEqual(3, g3.CountZ);
            for (int cz = 0; cz < 3; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    Show(rv, g3, cx, cz, list);
                    Assert.AreEqual(4, rv.VisibleObjectCount);
                }
        }

        // Criterion: Master Plan §4.5 placeholder to generated mesh swap; decision 8: the swap is the room content's, so S3 shows
        // it with no copy to update. The driver only learns that it must apply again.
        [Test]
        public void Mode2_Room_MeshSwapIsTheRoomContents()
        {
            var (grid, content, view) = Make(Fixtures.Room(RoomFixtures.BudgetRoom));
            var list = new CellInstanceList();
            Show(view, grid, 0, 0, list);
            Assert.IsFalse(view.Dirty);
            int visible = view.VisibleObjectCount;
            CellRange cells = view.ObjectCells(0);
            Assert.IsTrue(content.SetGeneratedMesh(content.GetObject(0).ObjectId, null, "abc"));
            Assert.IsTrue(view.Dirty, "the content turns the swapped renderer on: the cell rule is applied again");
            Assert.AreEqual(12, view.ObjectCount);
            Assert.AreEqual(cells, view.ObjectCells(0), "the pose is the RoomModel's");
            view.Apply(list);
            Assert.AreEqual(visible, view.VisibleObjectCount);
            view.Exit();
            Assert.DoesNotThrow(() => content.SetGeneratedMesh(content.GetObject(0).ObjectId, null, "def"));
            Assert.IsFalse(view.Dirty, "not followed after Exit");
        }

#if !ATLAS_HEADLESS
        // The same on the real room content: one renderer per object, the object's own; walls full, floor off, nothing clipped.
        [Test, Category("Unity")]
        public void Mode2_Room_RealContent_OncePerEyeUnclipped()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.BudgetRoom);
            using (var stage = new UnityStage(room))
            {
                int renderersBefore = stage.RoomRoot.GetComponentsInChildren<MeshRenderer>(true).Length;
                stage.Enter(WalkerPose.Start, new int2(0, 1));
                Assert.AreEqual(WallMode.Full, stage.Content.Walls);
                Assert.IsFalse(stage.Content.FloorVisible); Assert.IsTrue(stage.Content.ObjectsVisible);
                Assert.AreEqual(Vector4.zero, stage.Content.ShellMaterial.GetVector(AtlasShaderIDs.Gate0));
                Assert.AreEqual(0f, stage.Content.ShellMaterial.GetFloat(AtlasShaderIDs.WarpMode));
                var list = stage.Space.Core.Instances;
                for (int cz = 0; cz < 4; cz++)
                    for (int cx = 0; cx < 3; cx++)
                    {
                        stage.GoCell(cx, cz);
                        Assert.AreEqual(renderersBefore, stage.RoomRoot.GetComponentsInChildren<MeshRenderer>(true).Length, "no renderer added to the room");
                        for (int i = 0; i < stage.Content.ObjectCount; i++)
                        {
                            IRoomObjectView view = stage.Content.GetObject(i);
                            bool any = false;
                            for (int k = 0; k < list.Count; k++) any |= stage.Space.RoomView.ObjectCells(i).Contains(list[k].Cell);
                            Assert.AreEqual(any, view.Renderer.enabled, $"object {i} at cell ({cx},{cz})");
                            Assert.IsFalse(view.Renderer.HasPropertyBlock());
                            foreach (int gate in new[] { AtlasShaderIDs.Gate0, AtlasShaderIDs.Gate1, AtlasShaderIDs.Gate2, AtlasShaderIDs.Gate3 })
                                Assert.AreEqual(Vector4.zero, view.Material.GetVector(gate), "unclipped");
                        }
                    }
                // Nothing of the room is drawn under the space's own root.
                foreach (var mr in stage.Root.GetComponentsInChildren<MeshRenderer>(true))
                    Assert.IsTrue(mr.name == "Posts" || mr.name.StartsWith("Cell"), mr.name);
                stage.Space.Exit();
                for (int i = 0; i < stage.Content.ObjectCount; i++) Assert.IsTrue(stage.Content.GetObject(i).Renderer.enabled, "restored at Exit");
            }
        }

        // Criterion: Master Plan §4.5: a generated mesh that arrives while S3 is entered is the mesh S3 shows, at the same pose.
        [Test, Category("Unity")]
        public void Mode2_Room_RealContent_MeshSwapReachesTheSpace()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.Sample4x5Table);
            using (var stage = new UnityStage(room))
            {
                stage.Enter(WalkerPose.Start, new int2(0, 0));
                IRoomObjectView view = stage.Content.GetObject(0);
                Vector3 p = view.Transform.localPosition; Quaternion q = view.Transform.localRotation;
                var data = new AtlasMeshBuilder();
                data.AddBox(float3.zero, new float3(0.3f, 0.8f, 0.5f), 0f, AtlasPalette.Uv(AtlasPalette.Post));
                Mesh swap = AtlasMesh.Create(data, "swap");
                Assert.IsTrue(stage.Content.SetGeneratedMesh(view.ObjectId, swap, "0123", 0f));
                Assert.IsTrue(stage.Space.RoomView.Dirty);
                stage.Step();
                Assert.AreSame(swap, view.Renderer.GetComponent<MeshFilter>().sharedMesh);
                Assert.IsTrue(view.Renderer.enabled);
                Assert.AreEqual(p, view.Transform.localPosition); Assert.AreEqual(q, view.Transform.localRotation);
                Assert.AreEqual(MeshState.Ready, view.Data.Mesh);
            }
        }

        // Criterion: Master Plan §8.5 "no managed allocation" in steady state
        [Test, Category("Unity")]
        public void Mode2_Room_ApplyAllocatesNothing()
        {
            var (grid, content, view) = Make(Fixtures.Room(RoomFixtures.BudgetRoom));
            var list = new CellInstanceList();
            SightLines.Build(grid, new TileGraph(5), new int2(0, 1), new CellFrame(TileId.Zero, 0), list);
            view.Apply(list);
            Assert.That(() => view.Apply(list), new NotConstraint(new AllocatingGCMemoryConstraint()));
        }
#endif
    }
}
