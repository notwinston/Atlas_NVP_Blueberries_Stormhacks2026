using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using NUnit.Framework.Constraints;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomViewTests
    {
        GameObject root;
        Material template;
        readonly List<System.IDisposable> disposables = new List<System.IDisposable>();
        readonly List<TestHomeShell> shells = new List<TestHomeShell>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("RoomViewRoot");
            template = new Material(Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates"));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var d in disposables) d.Dispose();
            foreach (var s in shells) s.Destroy();
            disposables.Clear(); shells.Clear();
            Object.DestroyImmediate(template); Object.DestroyImmediate(root);
        }

        (CellGrid grid, TestHomeShell shell, AlignedCellsView cells, AlignedCellsRoomView room) Make(RoomModel r)
        {
            var grid = CellGrid.FromFreeRectangle(new FreeRectangle(r.freeRect.Wf, r.freeRect.Df));
            var shell = new TestHomeShell(r); shells.Add(shell);
            var cells = new AlignedCellsView(root.transform, template, grid); disposables.Add(cells);
            var room = new AlignedCellsRoomView(root.transform, template, shell, grid); disposables.Add(room);
            return (grid, shell, cells, room);
        }

        static void Show(AlignedCellsView cells, AlignedCellsRoomView room, CellGrid grid, int cx, int cz, CellInstanceList list)
        {
            SightLines.Build(grid, new TileGraph(5), new int2(cx, cz), new CellFrame(TileId.Zero, 0), list);
            cells.Apply(list); room.Apply(list);
        }

        // Criterion: row output "Home walls on the edge of the cell grid" at their true position; §4.9; [P1-B12 D2]
        [Test]
        public void Mode2_Room_WallsAndFloorAtTruePosition()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            Assert.AreSame(shell.Walls, room.WallsRenderer.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(Vector3.zero, room.WallsRenderer.transform.localPosition);
            Assert.AreEqual(Quaternion.identity, room.WallsRenderer.transform.localRotation);
            Assert.IsTrue(room.WallsRenderer.enabled);
            Assert.AreSame(shell.Floor, room.FloorRenderer.GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(new Vector3(0, -AlignedCellsRoomView.HomeFloorDrop, 0), room.FloorRenderer.transform.localPosition);
            Assert.IsTrue(room.FloorRenderer.enabled);
            Assert.AreSame(room.Material, room.WallsRenderer.sharedMaterial);
            Assert.AreSame(room.Material, room.FloorRenderer.sharedMaterial);
        }

        // Criterion: Master Plan §4.7 "never duplicated per instance"; once per eye (one renderer per object)
        [Test]
        public void Mode2_Room_ObjectDrawnOncePerEye()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            var list = new CellInstanceList();
            Show(cells, room, grid, 0, 1, list);
            Assert.AreEqual(23, list.Count);
            int in22 = 0;
            for (int k = 0; k < list.Count; k++) if (list[k].Cell.x == 2 && list[k].Cell.y == 2) in22++;
            Assert.AreEqual(3, in22, "instances of cell (2,2), the cell of object 4");
            for (int cz = 0; cz < 4; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    Show(cells, room, grid, cx, cz, list);
                    var all = root.GetComponentsInChildren<MeshRenderer>(true);
                    Assert.AreEqual(57 + 1 + 2 + 12, all.Length);
                    for (int i = 0; i < 12; i++)
                    {
                        int n = 0;
                        foreach (var mr in all) if (mr.GetComponent<MeshFilter>().sharedMesh == shell.ObjectMesh(i)) n++;
                        Assert.AreEqual(1, n, "object " + i);
                    }
                    foreach (var mr in all)
                        if (mr.gameObject.name.StartsWith("Object")) Assert.AreSame(root.transform, mr.transform.parent);
                }
        }

        // Criterion: Master Plan §4.7 "whenever any instance of its cell is drawn"
        [Test]
        public void Mode2_Room_ObjectVisibleIffCellDrawn()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            var list = new CellInstanceList();
            for (int cz = 0; cz < 4; cz++)
                for (int cx = 0; cx < 3; cx++)
                {
                    Show(cells, room, grid, cx, cz, list);
                    int vis = 0;
                    for (int i = 0; i < 12; i++)
                    {
                        bool any = false;
                        for (int k = 0; k < list.Count; k++) any |= room.ObjectCells(i).Contains(list[k].Cell);
                        Assert.AreEqual(any, room.ObjectRenderer(i).enabled, $"object {i} at cell ({cx},{cz})");
                        if (any) vis++;
                    }
                    Assert.AreEqual(vis, room.VisibleObjectCount);
                    bool corner = (cx == 0 || cx == 2) && (cz == 0 || cz == 3);
                    bool midEnd = cx == 1 && (cz == 0 || cz == 3);
                    Assert.AreEqual(corner || midEnd ? 7 : 12, room.VisibleObjectCount, $"cell ({cx},{cz})");
                }
            // room_3x3 with four synthetic objects: every cell sees every cell, so all objects are always drawn
            var r3 = RoomEdit.Clone(Fixtures.Room("room_3x3"));
            r3.objects = new[] { RoomEdit.Obj(0, 1.5f, 0, .3f, .8f, .5f, 0), RoomEdit.Obj(1, -1.5f, 0, .3f, .8f, .5f, 0),
                                 RoomEdit.Obj(2, 0, 1.5f, .5f, .8f, .3f, 0), RoomEdit.Obj(3, 0, -1.5f, .5f, .8f, .3f, 0.4f) };
            var root2 = new GameObject("Root2");
            try
            {
                var g3 = CellGrid.FromFreeRectangle(new FreeRectangle(2.4, 2.4));
                var sh = new TestHomeShell(r3); shells.Add(sh);
                var rv = new AlignedCellsRoomView(root2.transform, template, sh, g3); disposables.Add(rv);
                for (int cz = 0; cz < 3; cz++)
                    for (int cx = 0; cx < 3; cx++)
                    {
                        SightLines.Build(g3, new TileGraph(5), new int2(cx, cz), new CellFrame(TileId.Zero, 0), list);
                        rv.Apply(list);
                        Assert.AreEqual(4, rv.VisibleObjectCount);
                    }
            }
            finally { Object.DestroyImmediate(root2); }
        }

        // Criterion: Master Plan §4.7 "unclipped"; §8.3 (_Gates zero, no property blocks)
        [Test]
        public void Mode2_Room_ObjectsUnclipped()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            Assert.AreEqual(Matrix4x4.zero, room.Material.GetMatrix(AtlasShaderIDs.Gates));
            Assert.AreEqual(0f, room.Material.GetFloat(AtlasShaderIDs.WarpMode));
            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true)) Assert.IsFalse(mr.HasPropertyBlock());
        }

        // Criterion: Master Plan §4.5 placeholder to generated mesh swap keeps the pose
        [Test]
        public void Mode2_Room_MeshSwapKeepsPose()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            var tr = room.ObjectRenderer(0).transform;
            Vector3 p = tr.localPosition; Quaternion q = tr.localRotation;
            int count = root.GetComponentsInChildren<MeshRenderer>(true).Length;
            var swap = TestHomeShell.Box(new Vector3(0.3f, 0.8f, 0.5f), 2, 2, 2);
            shell.RaiseMeshChanged(0, swap);
            Assert.AreSame(swap, room.ObjectRenderer(0).GetComponent<MeshFilter>().sharedMesh);
            Assert.AreEqual(count, root.GetComponentsInChildren<MeshRenderer>(true).Length);
            Assert.AreEqual(p, tr.localPosition); Assert.AreEqual(q, tr.localRotation);
            room.Dispose();
            Assert.DoesNotThrow(() => shell.RaiseMeshChanged(0, TestHomeShell.Box(Vector3.one, 1, 1, 1)));
            Object.DestroyImmediate(swap);
        }

        // Criterion: Master Plan §8.5 "no managed allocation" in steady state
        [Test]
        public void Mode2_Room_ApplyAllocatesNothing()
        {
            var (grid, shell, cells, room) = Make(Fixtures.Room("budget_room"));
            var list = new CellInstanceList();
            SightLines.Build(grid, new TileGraph(5), new int2(0, 1), new CellFrame(TileId.Zero, 0), list);
            room.Apply(list);
            Assert.That(() => room.Apply(list), new NotConstraint(new AllocatingGCMemoryConstraint()));
        }
    }
}
