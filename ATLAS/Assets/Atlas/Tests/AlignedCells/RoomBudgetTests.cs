using System.Collections.Generic;
using Atlas.Kernel;
using Atlas.Rendering.Room;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomBudgetTests
    {
        // Acceptance P1-B12: "S3 on budget_room stays <= 170 draw calls and <= 200k triangles per frame"; Master Plan §8.5 S3 row, §8.4.
        // Counted from the real meshes: the flat cell, the posts, the room shell of RoomShellBuilder and the twelve stand-ins
        // at their 5,000 triangles each. The frame rate itself is the phone's (P1-A14).
        [Test]
        public void Mode2_RoomBudget_BudgetRoom()
        {
            var rig = new Rig(Fixtures.Room(RoomFixtures.BudgetRoom));
            Assert.AreEqual(12, rig.Content.ObjectCount);
            for (int i = 0; i < 12; i++) Assert.AreEqual(5000, rig.Content.GetObject(i).TriangleCount, "object " + i);
            Assert.LessOrEqual(rig.Content.ShellTriangleCount, RoomShellBuilder.MaxTriangles, "§8.4 Home shell");
            int worstDraws = 0; long worstTris = 0;
            for (int cz = 0; cz < 4; cz++)
                for (int k = 0; k < 3; k++)
                {
                    int cx = cz % 2 == 0 ? k : 2 - k;   // snake order
                    rig.GoCell(cx, cz);
                    S3Count.Measure(rig, out int draws, out long tris);
                    Assert.AreEqual(2 * (rig.Core.Instances.Count + 2 + rig.RoomView.VisibleObjectCount), draws, $"cell ({cx},{cz})");
                    Assert.LessOrEqual(draws, 170);
                    // the shell at its cap, so the bound holds for any room with this furniture
                    long capped = tris + 2 * (RoomShellBuilder.MaxTriangles - rig.Content.ShellTriangleCount);
                    Assert.LessOrEqual(capped, 200000);
                    worstDraws = System.Math.Max(worstDraws, draws); worstTris = System.Math.Max(worstTris, capped);
                }
            TestContext.WriteLine($"budget_room S3 worst: {worstDraws} draw calls, {worstTris} triangles (shell at the 3000 cap)");
            Assert.AreEqual(74, worstDraws, "23 instances, the posts, the walls (the cells bring the floor), twelve objects; both eyes");
        }

#if !ATLAS_HEADLESS
        // The same acceptance on the real component: AlignedCellsSpace made by its factory, the real RoomContent built from
        // budget_room, counted from the renderers that are switched on and the index counts of their meshes.
        [Test, Category("Unity")]
        public void Mode2_RoomBudget_BudgetRoom_RealComponent()
        {
            RoomModel room = Fixtures.Room(RoomFixtures.BudgetRoom);
            var rig = new Rig(Fixtures.Room(RoomFixtures.BudgetRoom));
            using (var stage = new UnityStage(room))
            {
                stage.Enter(WalkerPose.Start, new int2(0, 0));
                Assert.AreEqual(12, stage.Content.ObjectCount);
                for (int i = 0; i < 12; i++)
                    Assert.AreEqual(5000L, stage.Content.GetObject(i).Renderer.GetComponent<UnityEngine.MeshFilter>().sharedMesh.GetIndexCount(0) / 3, "object " + i);
                int worstDraws = 0; long worstTris = 0;
                for (int cz = 0; cz < 4; cz++)
                    for (int k = 0; k < 3; k++)
                    {
                        int cx = cz % 2 == 0 ? k : 2 - k;   // snake order
                        stage.GoCell(cx, cz); rig.GoCell(cx, cz);
                        S3Count.Measure(out int draws, out long tris, stage.Root, stage.RoomRoot.transform);
                        Assert.AreEqual(2 * (stage.Space.Core.Instances.Count + 2 + stage.Space.RoomView.VisibleObjectCount), draws, $"cell ({cx},{cz})");
                        Assert.LessOrEqual(draws, 170);
                        long capped = tris + 2 * (RoomShellBuilder.MaxTriangles - stage.Content.ShellTriangleCount);
                        Assert.LessOrEqual(capped, 200000);
                        S3Count.Measure(rig, out int counted, out long countedTris);
                        Assert.AreEqual(counted, draws, $"the engine-free count, cell ({cx},{cz})");
                        Assert.AreEqual(countedTris, tris, $"the engine-free count, cell ({cx},{cz})");
                        worstDraws = System.Math.Max(worstDraws, draws); worstTris = System.Math.Max(worstTris, capped);
                    }
                TestContext.WriteLine($"budget_room S3 worst, real component: {worstDraws} draw calls, {worstTris} triangles (shell at the 3000 cap)");
                Assert.AreEqual(74, worstDraws);
            }
        }
#endif

        // Derived from Master Plan §8.5 "up to 57 instances; furniture drawn once" and the §8.4 caps: 20 meshes, each at the
        // §4.5 target clamp(60,000 / 20, 3,000, 5,000) = 3,000, so 60,000 triangles of furniture.
        [Test]
        public void Mode2_RoomBudget_WorstCase57()
        {
            var objs = new List<RoomObject>();
            for (int i = 0; i < 5; i++)
            {
                float t = -2.7f + 1.35f * i;
                objs.Add(Fixtures.Obj(objs.Count + 1, -3.5f, t, 0.3f, 0.8f, 0.5f, 0, 3000));
                objs.Add(Fixtures.Obj(objs.Count + 1, 3.5f, t, 0.3f, 0.8f, 0.5f, 0, 3000));
                objs.Add(Fixtures.Obj(objs.Count + 1, t, -3.5f, 0.5f, 0.8f, 0.3f, 0, 3000));
                objs.Add(Fixtures.Obj(objs.Count + 1, t, 3.5f, 0.5f, 0.8f, 0.3f, 0, 3000));
            }
            foreach (RoomObject o in objs) o.Mesh = MeshState.Ready;   // generated meshes: each costs its triangle target
            var rig = new Rig(Fixtures.Rectangle(6.75f, 6.75f, 0.3f, objs.ToArray()), new int2(2, 2));
            Assert.AreEqual(5, rig.Grid.CountX); Assert.AreEqual(5, rig.Grid.CountZ);
            Assert.AreEqual(57, rig.Core.Instances.Count);
            Assert.AreEqual(20, rig.RoomView.VisibleObjectCount);
            S3Count.Measure(rig, out int draws, out long tris);
            TestContext.WriteLine($"synthetic worst: {draws} draw calls, {tris} triangles");
            for (int i = 0; i < 20; i++) Assert.AreEqual(3000, rig.Content.GetObject(i).TriangleCount, "object " + i);
            Assert.AreEqual(158, draws, "57 instances, the posts, the walls, twenty objects; both eyes"); Assert.LessOrEqual(draws, 170);
            Assert.LessOrEqual(tris + 2 * System.Math.Max(0, RoomShellBuilder.MaxTriangles - rig.Content.ShellTriangleCount), 200000);
        }
    }
}
