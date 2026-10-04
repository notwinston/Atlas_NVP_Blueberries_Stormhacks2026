using System.Collections.Generic;
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
        }

        // Derived from Master Plan §8.5 "up to 57 instances; furniture drawn once" and the §8.4 caps (20 meshes)
        [Test]
        public void Mode2_RoomBudget_WorstCase57()
        {
            var objs = new List<RoomObject>();
            for (int i = 0; i < 5; i++)
            {
                float t = -2.7f + 1.35f * i;
                objs.Add(Fixtures.Obj(objs.Count + 1, -3.5f, t, 0.3f, 0.8f, 0.5f, 0, 5000));
                objs.Add(Fixtures.Obj(objs.Count + 1, 3.5f, t, 0.3f, 0.8f, 0.5f, 0, 5000));
                objs.Add(Fixtures.Obj(objs.Count + 1, t, -3.5f, 0.5f, 0.8f, 0.3f, 0, 5000));
                objs.Add(Fixtures.Obj(objs.Count + 1, t, 3.5f, 0.5f, 0.8f, 0.3f, 0, 5000));
            }
            var rig = new Rig(Fixtures.Rectangle(6.75f, 6.75f, 0.3f, objs.ToArray()), new int2(2, 2));
            Assert.AreEqual(5, rig.Grid.CountX); Assert.AreEqual(5, rig.Grid.CountZ);
            Assert.AreEqual(57, rig.Core.Instances.Count);
            Assert.AreEqual(20, rig.RoomView.VisibleObjectCount);
            S3Count.Measure(rig, out int draws, out long tris);
            TestContext.WriteLine($"synthetic worst: {draws} draw calls, {tris} triangles");
            Assert.AreEqual(2 * (57 + 1 + 1 + 20), draws); Assert.LessOrEqual(draws, 170);
            Assert.LessOrEqual(tris + 2 * System.Math.Max(0, RoomShellBuilder.MaxTriangles - rig.Content.ShellTriangleCount), 2 * (20 * 5000 + RoomShellBuilder.MaxTriangles + 57 * 250));
        }
    }
}
