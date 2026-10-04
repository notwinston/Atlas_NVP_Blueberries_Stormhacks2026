using System;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;
#if !ATLAS_HEADLESS
using Atlas.Kernel;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using UnityEngine;
#endif

namespace Atlas.Spaces.AlignedCells.Tests
{
    // Test 8 of the P1-B12 brief, kept in its own file.
    public class RoomCrossingTests
    {
        static readonly int2[] TableRoute =
        {
            new int2(0,0), new int2(1,0), new int2(1,1), new int2(0,1), new int2(0,0), new int2(1,0), new int2(1,1), new int2(0,1),
            new int2(0,0), new int2(1,0), new int2(1,1)
        };

        static readonly int2[] BudgetRoute =
        {
            new int2(1,1), new int2(2,1), new int2(2,2), new int2(1,2), new int2(0,2), new int2(0,1), new int2(0,0), new int2(1,0),
            new int2(1,1), new int2(1,2), new int2(1,3)
        };

        // Acceptance P1-B12 (the half that needs no phone: "a virtual table edge is within 5 cm of the real one at three touch
        // points after ten cell crossings"); Master Plan §4.7, §4.9. S3 never places an object: the room content keeps it at the
        // RoomModel's pose, so the touch points are the RoomModel's whatever was walked. What the walk changes is the tile under
        // each cell, and whether the object is drawn; with the table's cells always in sight it is drawn on every frame.
        static void Run(RoomModel room, int2[] route, bool tableAlwaysVisible)
        {
            var rig = new Rig(room, route[0]);
            Assert.AreEqual(0, rig.Core.Crossings);
            CellFrame start = rig.Core.Walker.Frame;
            for (int k = 1; k < route.Length; k++)
                rig.GoCell(route[k].x, route[k].y, () =>
                {
                    if (tableAlwaysVisible) Assert.IsTrue(rig.RoomView.ObjectVisible(0));
                });
            Assert.AreEqual(10, rig.Core.Crossings);
            Assert.AreNotEqual(start, rig.Core.Walker.Frame, "the walk changed the tile");
            int n = room.Objects.Length;
            Assert.AreEqual(n, rig.Content.ObjectCount);
            for (int i = 0; i < n; i++)
            {
                RoomObject o = rig.Content.GetObject(i).Data;
                Assert.AreSame(room.Objects[i], o);
                float w = o.Size.x, h = o.Size.y, d = o.Size.z;
                foreach (var p in new[] { new float3(-w / 2, h, -d / 2), new float3(0, h, -d / 2), new float3(w / 2, h, -d / 2) })
                {
                    double3 e = RoomObjectCells.BoxToAnchor(o, p);
                    float3 actual = math.mul(o.Rotation, p) + o.Base;       // where the room content draws it
                    double err = math.length(e - actual);
                    Assert.LessOrEqual(err, 0.05, "criterion: within 5 cm");
                    Assert.LessOrEqual(err, 1e-5, "derived: path independent, float cast only");
                }
            }
            // The room content was asked for the S3 presentation once and nothing else: no build, no clear, no swap.
            CollectionAssert.AreEqual(new[] { "Build", "SetWallMode:Full", "FloorVisible:False", "ObjectsVisible:True" }, rig.Content.Calls);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings()
        {
            Run(Fixtures.Room(RoomFixtures.Sample4x5Table), TableRoute, true);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings_Budget()
        {
            Run(Fixtures.Room(RoomFixtures.BudgetRoom), BudgetRoute, false);
        }

        [Test]
        public void Mode2_Room_TableEdgeAfterTenCrossings_Yawed()
        {
            RoomModel table = Fixtures.Room(RoomFixtures.Sample4x5Table);
            RoomModel room = Fixtures.Rectangle(table.FreeRect.Wf, table.FreeRect.Df, 0.3f,
                table.Objects[0], Fixtures.Obj(2, 0.9f, 1.3f, 0.8f, 0.5f, 0.3f, 0.7f));
            Run(room, TableRoute, true);
        }

#if !ATLAS_HEADLESS
        // The same acceptance on the real room content and the real component, with the space's root moved and turned as a
        // re-anchored room would be: each object's own transform puts the three touch points where the RoomModel has them.
        [TestCase(0f, 0f, 0f), TestCase(0.3f, -0.2f, 30f)]
        [Category("Unity")]
        public void Mode2_Room_TableEdgeAfterTenCrossings_RealContent(float px, float pz, float yawDegrees)
        {
            RoomModel room = Fixtures.Room(RoomFixtures.Sample4x5Table);
            using (var stage = new UnityStage(room))
            {
                stage.Enter(WalkerPose.Start, TableRoute[0]);
                stage.Root.SetPositionAndRotation(new Vector3(px, 0, pz), Quaternion.Euler(0, yawDegrees, 0));
                stage.Step();
                IRoomObjectView view = stage.Content.GetObject(0);
                Vector3 p0 = view.Transform.localPosition; Quaternion q0 = view.Transform.localRotation;
                for (int k = 1; k < TableRoute.Length; k++)
                    stage.GoCell(TableRoute[k].x, TableRoute[k].y, () => Assert.IsTrue(view.Renderer.enabled));
                Assert.AreEqual(10, stage.Space.Core.Crossings);
                Assert.AreEqual(p0, view.Transform.localPosition); Assert.AreEqual(q0, view.Transform.localRotation);
                Matrix4x4 roomToWorld = stage.Content.Root.localToWorldMatrix;
                RoomObject o = view.Data;
                float w = o.Size.x, h = o.Size.y, d = o.Size.z;
                foreach (var p in new[] { new Vector3(-w / 2, h, -d / 2), new Vector3(0, h, -d / 2), new Vector3(w / 2, h, -d / 2) })
                {
                    double3 e = RoomObjectCells.BoxToAnchor(o, new double3(p.x, p.y, p.z));
                    Vector3 expect = roomToWorld.MultiplyPoint3x4(new Vector3((float)e.x, (float)e.y, (float)e.z));
                    Vector3 actual = view.Transform.localToWorldMatrix.MultiplyPoint3x4(p);
                    Assert.LessOrEqual((expect - actual).magnitude, 1e-4f);
                }
            }
        }
#endif
    }
}
