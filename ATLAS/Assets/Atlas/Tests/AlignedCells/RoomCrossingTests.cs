using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Atlas.Spaces.AlignedCells.Tests
{
    // Test 8 of the P1-B12 brief, kept in its own file.
    public class RoomCrossingTests
    {
        // Acceptance P1-B12 (Editor proxy for "a virtual table edge is within 5 cm of the real one at three touch points
        // after ten cell crossings"); Master Plan §4.7, §4.9; yaw sense PH-15 is only caught by this test.
        static void Run(RoomModel room, int2[] route, Vector3 parentPos, float parentYaw, bool tableAlwaysVisible)
        {
            int2 first = route[0];
            using (var rig = new Rig(room, TestHomeShell.Mode.Functional, first, enter: true))
            {
                rig.Parent.transform.SetPositionAndRotation(parentPos, Quaternion.Euler(0, parentYaw, 0));
                rig.Step();
                var view = rig.Module.RoomView;
                int n = room.objects.Length;
                var p0 = new Vector3[n]; var q0 = new Quaternion[n];
                for (int i = 0; i < n; i++) { p0[i] = view.ObjectRenderer(i).transform.localPosition; q0[i] = view.ObjectRenderer(i).transform.localRotation; }
                Assert.AreEqual(0, rig.Module.Crossings);
                for (int k = 1; k < route.Length; k++)
                    rig.GoCell(route[k].x, route[k].y, () =>
                    {
                        if (tableAlwaysVisible) Assert.IsTrue(view.ObjectRenderer(0).enabled);
                    });
                Assert.AreEqual(10, rig.Module.Crossings);
                Matrix4x4 l2w = rig.Module.Root.localToWorldMatrix;
                double worst = 0;
                for (int i = 0; i < n; i++)
                {
                    var o = room.objects[i];
                    var tr = view.ObjectRenderer(i).transform;
                    Assert.AreEqual(p0[i], tr.localPosition); Assert.AreEqual(q0[i], tr.localRotation);   // bitwise: pose is path independent
                    float w = o.size.x, h = o.size.y, d = o.size.z;
                    foreach (var p in new[] { new Vector3(-w / 2, h, -d / 2), new Vector3(0, h, -d / 2), new Vector3(w / 2, h, -d / 2) })
                    {
                        double3 e = RoomObjectCells.BoxToAnchor(o, new double3(p.x, p.y, p.z));
                        Vector3 expect = l2w.MultiplyPoint3x4(new Vector3((float)e.x, (float)e.y, (float)e.z));
                        Vector3 actual = tr.localToWorldMatrix.MultiplyPoint3x4(p);
                        double err = (expect - actual).magnitude;
                        worst = Math.Max(worst, err);
                        Assert.LessOrEqual(err, 0.05, "criterion: within 5 cm");
                        Assert.LessOrEqual(err, 1e-4, "derived: path independent, float cast only");
                    }
                }
                TestContext.WriteLine($"worst touch-point error after 10 crossings: {worst * 1000:F4} mm");
            }
        }

        static readonly int2[] TableRoute =
        {
            new int2(0,0), new int2(1,0), new int2(1,1), new int2(0,1), new int2(0,0), new int2(1,0), new int2(1,1), new int2(0,1),
            new int2(0,0), new int2(1,0), new int2(1,1)
        };

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings()
        {
            Run(Fixtures.Room("room_4x5_table"), TableRoute, Vector3.zero, 0, true);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings_Budget()
        {
            var route = new[] { new int2(1,1), new int2(2,1), new int2(2,2), new int2(1,2), new int2(0,2), new int2(0,1), new int2(0,0), new int2(1,0), new int2(1,1), new int2(1,2), new int2(1,3) };
            Run(Fixtures.Room("budget_room"), route, Vector3.zero, 0, false);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings_MovedParent()
        {
            Run(Fixtures.Room("room_4x5_table"), TableRoute, new Vector3(0.3f, 0, -0.2f), 30f, true);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings_Yawed()
        {
            var room = RoomEdit.Clone(Fixtures.Room("room_4x5_table"));
            var list = new System.Collections.Generic.List<RoomObject>(room.objects) { RoomEdit.Obj(1, 0.9f, 1.3f, 0.8f, 0.5f, 0.3f, 0.7f) };
            room.objects = list.ToArray();
            Run(room, TableRoute, Vector3.zero, 0, true);
        }
    }
}
