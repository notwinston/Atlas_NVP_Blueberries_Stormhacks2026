using System.Collections.Generic;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    public class RoomBudgetTests
    {
        static long Tris(Mesh m) => m.GetIndexCount(0) / 3;

        // Acceptance P1-B12: "S3 on budget_room stays <= 170 draw calls and <= 200k triangles per frame"; Master Plan §8.5 S3 row, §8.4
        [Test]
        public void Mode2_RoomBudget_BudgetRoom()
        {
            using (var rig = new Rig(Fixtures.Room("budget_room"), TestHomeShell.Mode.BudgetStandIns))
            {
                var sh = rig.Shell;
                for (int i = 0; i < sh.ObjectCount; i++) Assert.AreEqual(5000, Tris(sh.ObjectMesh(i)), "object " + i);
                long shell = Tris(sh.Walls) + Tris(sh.Floor);
                Assert.LessOrEqual(shell, 3000, "§8.4 Home shell");
                int worstDraws = 0; long worstTris = 0;
                for (int cz = 0; cz < 4; cz++)
                    for (int k = 0; k < 3; k++)
                    {
                        int cx = cz % 2 == 0 ? k : 2 - k;   // snake order
                        rig.GoCell(cx, cz);
                        S3Count.Measure(rig.Module.Root, out int draws, out long tris);
                        int expected = 2 * (rig.Module.Space.Instances.Count + 3 + rig.Module.RoomView.VisibleObjectCount);
                        Assert.AreEqual(expected, draws, $"cell ({cx},{cz})");
                        Assert.LessOrEqual(draws, 170);
                        long capped = tris + 2 * (3000 - shell);
                        Assert.LessOrEqual(capped, 200000);
                        worstDraws = System.Math.Max(worstDraws, draws); worstTris = System.Math.Max(worstTris, capped);
                    }
                TestContext.WriteLine($"budget_room S3 worst: {worstDraws} draw calls, {worstTris} triangles (shell at 3000 cap)");
                Assert.AreEqual(76, worstDraws);
            }
        }

        // Derived from Master Plan §8.5 "up to 57 instances; furniture drawn once" and the §8.4 caps (20 meshes)
        [Test]
        public void Mode2_RoomBudget_WorstCase57()
        {
            var room = RoomEdit.Clone(Fixtures.Room("room_3x3"));
            room.freeRect = new FreeRect { Wf = 6.75f, Df = 6.75f };
            const float e = 3.375f + 0.3f;
            Wall W(float ax, float az, float bx, float bz) => new Wall { a = new FloorPoint { x = ax, z = az }, b = new FloorPoint { x = bx, z = bz }, height = 2.5f, openings = new Opening[0] };
            room.walls = new[] { W(-e, -e, e, -e), W(e, -e, e, e), W(e, e, -e, e), W(-e, e, -e, -e) };
            var objs = new List<RoomObject>();
            for (int i = 0; i < 5; i++)
            {
                float t = -2.7f + 1.35f * i;
                objs.Add(RoomEdit.Obj(objs.Count, -3.5f, t, 0.3f, 0.8f, 0.5f, 0));
                objs.Add(RoomEdit.Obj(objs.Count, 3.5f, t, 0.3f, 0.8f, 0.5f, 0));
                objs.Add(RoomEdit.Obj(objs.Count, t, -3.5f, 0.5f, 0.8f, 0.3f, 0));
                objs.Add(RoomEdit.Obj(objs.Count, t, 3.5f, 0.5f, 0.8f, 0.3f, 0));
            }
            room.objects = objs.ToArray();
            using (var rig = new Rig(room, TestHomeShell.Mode.PerObject, new int2(2, 2)))
            {
                Assert.AreEqual(5, rig.Grid.CountX); Assert.AreEqual(5, rig.Grid.CountZ);
                Assert.AreEqual(57, rig.Module.Space.Instances.Count);
                Assert.AreEqual(20, rig.Module.RoomView.VisibleObjectCount);
                S3Count.Measure(rig.Module.Root, out int draws, out long tris);
                TestContext.WriteLine($"synthetic worst: {draws} draw calls, {tris} triangles");
                Assert.AreEqual(160, draws); Assert.LessOrEqual(draws, 170);
                long shell = Tris(rig.Shell.Walls) + Tris(rig.Shell.Floor);
                Assert.LessOrEqual(tris + 2 * System.Math.Max(0, 3000 - shell), 200000);
            }
        }
    }
}
