using Atlas.Kernel;
using Atlas.Link;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class AlignedCellsModuleTests
    {
        // Criterion: Master Plan §6.5, §4.9 (S3 registered)
        [Test]
        public void Mode2_Module_IdAndRegistered()
        {
            using (var rig = new Rig(Fixtures.Room("room_3x3"), enter: false))
            {
                Assert.AreEqual(SpaceId.S3, rig.Module.Id);
                Assert.IsTrue(rig.Module.Registered);
            }
            var parent = new GameObject("P"); var mat = new Material(Shader.Find("Hidden/Atlas/Tests/AlignedCellsGates"));
            var m = new AlignedCellsModule(new TileGraph(5), parent.transform, mat, () => null);
            Assert.Throws<System.InvalidOperationException>(() => m.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity }));
            m.Dispose(); Object.DestroyImmediate(mat); Object.DestroyImmediate(parent);
        }

        // Criterion: G3 "switch on one tile graph with no tile-id change"; Master Plan §4.8
        [Test]
        public void Mode2_Module_EnterKeepsTileId()
        {
            using (var rig = new Rig(Fixtures.Room("room_3x3"), enter: false))
            {
                var tile = TileId.Zero.Step(1).Step(2);
                rig.Module.Enter(new WalkerPose { Tile = tile, Local = Iso2.Boost(new double2(0.1, 0.05), -1) });
                rig.Step();
                Assert.AreEqual(tile, rig.Module.Space.Walker.Frame.Tile);
            }
        }

        // Criterion: [P1-B12 D6] the room view is built lazily once SpaceLab.Home is set
        [Test]
        public void Mode2_Module_HomeArrivesLate()
        {
            using (var rig = new Rig(Fixtures.Room("budget_room"), enter: false))
            {
                rig.HomeAvailable = false;
                rig.Module.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
                rig.Step();
                Assert.IsNull(rig.Module.RoomView);
                rig.HomeAvailable = true;
                rig.Step();
                Assert.IsNotNull(rig.Module.RoomView);
                var v = rig.Module.RoomView; var list = rig.Module.Space.Instances;
                for (int i = 0; i < v.ObjectCount; i++)
                {
                    bool any = false;
                    for (int k = 0; k < list.Count; k++) any |= v.ObjectCells(i).Contains(list[k].Cell);
                    Assert.AreEqual(any, v.ObjectRenderer(i).enabled);
                }
            }
        }

        // Criterion: S3 content disappears on leaving the space (§6.5 Exit)
        [Test]
        public void Mode2_Module_ExitHides()
        {
            using (var rig = new Rig(Fixtures.Room("budget_room")))
            {
                rig.Module.Exit();
                Assert.IsFalse(rig.Module.Root.gameObject.activeSelf);
                S3Count.Measure(rig.Module.Root, out int draws, out long tris);
                Assert.AreEqual(0, draws); Assert.AreEqual(0, tris);
                rig.Module.Enter(new WalkerPose { Tile = TileId.Zero, Local = Iso2.Identity });
                Assert.IsTrue(rig.Module.Root.gameObject.activeSelf);
            }
        }

        // Criterion: Master Plan §4.7 gates per eye in world positions; [P1-B12 D11]
        [Test]
        public void Mode2_Module_ReuploadsGatesWhenAnchorMoves()
        {
            using (var rig = new Rig(Fixtures.Room("room_3x3"), startCell: new int2(0, 0)))
            {
                var list = rig.Module.Space.Instances;
                int k = -1;
                for (int i = 0; i < list.Count && k < 0; i++) if (list[i].GateCount > 0) k = i;
                Assert.GreaterOrEqual(k, 0);
                var mat = rig.Module.Root.Find("Cell" + k).GetComponent<MeshRenderer>().sharedMaterial;
                float before = mat.GetMatrix(Atlas.Rendering.AtlasShaderIDs.Gates)[0, 0];
                rig.Parent.transform.position += new Vector3(0.5f, 0, 0);
                rig.Step();
                float after = mat.GetMatrix(Atlas.Rendering.AtlasShaderIDs.Gates)[0, 0];
                Assert.AreEqual(0.5f, after - before, 1e-5);
            }
        }

        // Criterion: Master Plan App. B.1 (State: spaceId, tileId, sheet, xform)
        [Test]
        public void Mode2_Module_WriteState()
        {
            using (var rig = new Rig(Fixtures.Room("budget_room"), startCell: new int2(1, 2)))
            {
                var st = StateMessage.Create();
                rig.Module.WriteState(ref st);
                var t = rig.Module.Space.Walker.Frame.Tile;
                Assert.AreEqual(3, st.SpaceId);
                CollectionAssert.AreEqual(new[] { t.Xu, t.Xv, t.Yu, t.Yv, t.Tu, t.Tv }, st.TileId);
                Assert.AreEqual(2 * rig.Grid.CountX + 1, st.Sheet);
                CollectionAssert.AreEqual(new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 }, st.Xform);
            }
        }

        // Criterion: Master Plan §9.3 (systems register themselves from their own scene), App. C.9 scene name; PH-4 not relied on
        [Test]
        public void Mode2_Scene_HasHost()
        {
            const string path = "Assets/Atlas/Spaces/AlignedCells/Scenes/Space_AlignedCells.unity";
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            AlignedCellsSpaceHost host = null; int count = 0;
            foreach (var go in scene.GetRootGameObjects())
                foreach (var h in go.GetComponentsInChildren<AlignedCellsSpaceHost>(true)) { host = h; count++; }
            try
            {
                Assert.AreEqual(1, count);
                var lab = new SpaceLab(Fixtures.Room("room_3x3"), new ParamRegistry());
                host.Register(lab);
                Assert.IsNotNull(host.Module);
                Assert.DoesNotThrow(() => lab.Request(SpaceId.S3));
            }
            finally
            {
                host?.Module?.Dispose();
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
