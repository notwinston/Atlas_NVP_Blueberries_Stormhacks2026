using System;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>
    /// The standalone-phone plan's scan changes (task T2): RoomPlan objects become boxes with their category and
    /// attribute strings, the strings travel ObstacleBox to RoomObject.Tags, and the scanned mesh of a snapshot is
    /// carried into the room frame for the scan mesh sink.
    /// </summary>
    public class ScanTagsAndMeshTests
    {
        static RoomPlanObject Object(string category, float3 centre, float3 size, float yaw, params string[] attributes)
        {
            return new RoomPlanObject
            {
                Id = "id-" + category, Category = category, Centre = centre, Size = size,
                Rotation = quaternion.RotateY(yaw), Attributes = attributes ?? Array.Empty<string>(),
            };
        }

        // ---- categories and tags into boxes ---------------------------------------------------------------

        [Test]
        public void ClassBits_GiveEveryCategoryBack()
        {
            for (int value = 0; value < ObjectCategories.Count; value++)
            {
                var category = (ObjectCategory)value;
                Assert.That(ObjectCategories.FromBoundingBoxBits(ObjectBoxMerge.ClassBitsOf(category)), Is.EqualTo(category), category.ToString());
            }
        }

        [TestCase("sofa", ObjectCategory.Couch)]
        [TestCase("television", ObjectCategory.Screen)]
        [TestCase("washerDryer", ObjectCategory.WasherDryer)]
        [TestCase("chair", ObjectCategory.Chair)]
        [TestCase("somethingNew", ObjectCategory.Other)]
        [TestCase("", ObjectCategory.Other)]
        public void RoomPlanObject_BecomesABoxWithItsCategoryAndTags(string name, ObjectCategory expected)
        {
            RoomPlanObject obj = Object(name, new float3(1f, 0.45f, 2f), new float3(0.5f, 0.9f, 0.6f), 0.3f, "dining", "four");
            ScannedBox scanned = ScanSensors.ToScannedBox(in obj, 5);
            Assert.That(scanned.SourceId, Is.EqualTo(5));
            Assert.That(scanned.Tags, Is.EqualTo(new[] { "dining", "four" }));

            ObstacleBox box;
            Assert.That(ObjectBoxMerge.TryConvert(in scanned, out box), Is.True);
            Assert.That(box.Category, Is.EqualTo((int)expected));
            Assert.That(box.Tags, Is.EqualTo(new[] { "dining", "four" }));
            Assert.That(math.distance(box.Base, new float3(1f, 0f, 2f)), Is.LessThan(1e-6f));
            Assert.That(box.Yaw, Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void ABoxWithoutTags_ConvertsToEmptyTags()
        {
            var scanned = new ScannedBox { Centre = new float3(0f, 0.5f, 0f), Size = new float3(1f, 1f, 1f), Rotation = quaternion.identity, SourceId = 1 };
            ObstacleBox box;
            Assert.That(ObjectBoxMerge.TryConvert(in scanned, out box), Is.True);
            Assert.That(box.Tags, Is.Not.Null.And.Empty);
        }

        [Test]
        public void Merge_KeepsTheTagsOfARepeat_ThatHasThem()
        {
            // The larger report has no tags: it takes the smaller one's. And the other way round.
            var larger = new ScannedBox { Centre = new float3(1f, 0.4f, 2f), Size = new float3(1.05f, 0.8f, 0.65f), Rotation = quaternion.identity, SourceId = 1 };
            var smaller = new ScannedBox
            {
                Centre = new float3(1f, 0.375f, 2f), Size = new float3(1f, 0.75f, 0.6f), Rotation = quaternion.identity,
                ClassBits = ObjectBoxMerge.ClassBitsOf(ObjectCategory.Table), SourceId = 2, Tags = new[] { "coffee" },
            };
            ObstacleBox[] merged = ObjectBoxMerge.Merge(new[] { larger, smaller }, 2);
            Assert.That(merged.Length, Is.EqualTo(1));
            Assert.That(merged[0].SourceId, Is.EqualTo(1));
            Assert.That(merged[0].Tags, Is.EqualTo(new[] { "coffee" }));
            Assert.That(merged[0].Category, Is.EqualTo((int)ObjectCategory.Table));

            merged = ObjectBoxMerge.Merge(new[] { smaller, larger }, 2);
            Assert.That(merged.Length, Is.EqualTo(1));
            Assert.That(merged[0].SourceId, Is.EqualTo(1));
            Assert.That(merged[0].Tags, Is.EqualTo(new[] { "coffee" }));
        }

        // ---- tags into the room ----------------------------------------------------------------------------

        [Test]
        public void Tags_ReachTheRoomObjects_ThroughTheScanRoutes()
        {
            // A 4 x 5 m room, turned and shifted in the session frame, with a table and a cupboard in the mesh.
            // RoomPlan reports the table with an attribute; the cupboard is the grid's own find and has none.
            const float turn = 0.3f;
            var shift = new float3(2f, 0.2f, -1f);
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f)
                .AddBox(new float2(2f, 3.2f), new float3(1f, 0.75f, 0.6f))
                .AddBox(new float2(0.3f, 4.5f), new float3(0.6f, 1.8f, 0.6f), 0f, SurfaceClass.None);
            scan.SetWalker(new float2(2f, 1.2f));
            scan.Move(turn, shift);
            float2 tableSession = ScanTestRooms.Moved(new float2(2f, 3.2f), turn, shift.xz);
            RoomPlanObject table = Object("table", new float3(tableSession.x, shift.y + 0.375f, tableSession.y),
                new float3(1f, 0.75f, 0.6f), -turn, "TableType.dining");
            var boxes = new[] { ScanSensors.ToScannedBox(in table, 1) };
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(scan.Build());
            snapshot.Boxes = ObjectBoxMerge.Merge(boxes, boxes.Length);

            ScanSolution solution = ScanSolver.Solve(snapshot, "tags");

            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            RoomModel room = solution.Room;
            Assert.That(room.Objects.Length, Is.EqualTo(2));
            RoomObject cupboard = room.Objects[0], found = room.Objects[1];
            Assert.That(found.Category, Is.EqualTo(ObjectCategory.Table));
            Assert.That(found.Tags, Is.EqualTo(new[] { "TableType.dining" }));
            Assert.That(cupboard.Category, Is.EqualTo(ObjectCategory.Unknown));
            Assert.That(cupboard.Tags, Is.Not.Null.And.Empty);
        }

        [Test]
        public void Tags_ReachTheRoomObjects_OfAnOutlineRoom()
        {
            var boxes = new[]
            {
                new ObstacleBox { Base = new float3(2f, 0f, 2.5f), Size = new float3(1f, 0.75f, 0.6f), Category = (int)ObjectCategory.Table, Tags = new[] { "round" } },
                new ObstacleBox { Base = new float3(0.4f, 0f, 4.5f), Size = new float3(0.6f, 1.8f, 0.6f), Category = (int)ObjectCategory.Storage },
            };
            RoomModel room = RoomModelBuilder.FromOutline("tags", ScanTestRooms.Rectangle(4f, 5f), boxes, new float2(2f, 1f));
            Assert.That(room, Is.Not.Null);
            RoomObject table = null, storage = null;
            foreach (RoomObject obj in room.Objects)
            {
                if (obj.Category == ObjectCategory.Table) table = obj;
                if (obj.Category == ObjectCategory.Storage) storage = obj;
            }
            Assert.That(table, Is.Not.Null);
            Assert.That(storage, Is.Not.Null);
            Assert.That(table.Tags, Is.EqualTo(new[] { "round" }));
            Assert.That(storage.Tags, Is.Not.Null.And.Empty);
        }

        // ---- the scanned mesh in the room frame -----------------------------------------------------------

        static ScanSnapshot MeshSnapshot()
        {
            return new ScanSnapshot
            {
                Vertices = new[] { new float3(2f, 0.3f, -1f), new float3(3f, 0.3f, -1f), new float3(2f, 1.3f, 0.5f), new float3(-1f, 2f, 4f) },
                Indices = new[] { 0, 1, 2, 1, 3, 2 },
                Classes = new[] { SurfaceClass.Floor, SurfaceClass.Seat },
            };
        }

        [Test]
        public void ScanMesh_IsCarriedIntoTheRoomFrame()
        {
            ScanSnapshot snapshot = MeshSnapshot();
            var anchor = new RoomAnchor { Id = "", Position = new float3(2f, 0.3f, -1f), Yaw = 0.4f };

            ScanMesh mesh = ScanMeshes.ToRoomFrame(snapshot, anchor);

            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.TriangleCount, Is.EqualTo(2));
            Assert.That(mesh.Indices, Is.SameAs(snapshot.Indices), "the snapshot's indices are passed on, not copied");
            Assert.That(mesh.Classes, Is.EqualTo(new byte[] { ScanMesh.ClassFloor, ScanMesh.ClassSeat }));
            // The anchor is the origin of the room frame, on the floor.
            Assert.That(math.distance(mesh.Vertices[0], float3.zero), Is.LessThan(1e-6f));
            // Back to the session frame the way the room is placed: session = anchor + RotateY(yaw) * room.
            for (int k = 0; k < snapshot.Vertices.Length; k++)
            {
                float3 back = anchor.Position + math.mul(quaternion.RotateY(anchor.Yaw), mesh.Vertices[k]);
                Assert.That(math.distance(back, snapshot.Vertices[k]), Is.LessThan(1e-5f), "vertex " + k);
            }
            // Heights are measured from the floor; lengths are kept.
            Assert.That(mesh.Vertices[2].y, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(math.distance(mesh.Vertices[1], mesh.Vertices[0]), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(snapshot.Vertices[0], Is.EqualTo(new float3(2f, 0.3f, -1f)), "the snapshot is not written");
        }

        [Test]
        public void ScanMesh_IsNull_WhenThereIsNoScannedMesh()
        {
            var anchor = new RoomAnchor { Position = float3.zero, Yaw = 0f };
            Assert.That(ScanMeshes.ToRoomFrame(null, anchor), Is.Null);
            Assert.That(ScanMeshes.ToRoomFrame(new ScanSnapshot(), anchor), Is.Null);
            ScanSnapshot planes = MeshSnapshot();
            planes.MeshFromPlanes = true;
            Assert.That(ScanMeshes.ToRoomFrame(planes, anchor), Is.Null, "triangles laid over planes are no scanned mesh");
        }

        [Test]
        public void ScanMesh_HasNoClasses_WhenTheSnapshotHasNone()
        {
            ScanSnapshot snapshot = MeshSnapshot();
            snapshot.Classes = null;
            ScanMesh mesh = ScanMeshes.ToRoomFrame(snapshot, new RoomAnchor { Position = float3.zero, Yaw = 0f });
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.Classes, Is.Null);
            Assert.That(mesh.Vertices[3], Is.EqualTo(snapshot.Vertices[3]));
        }
    }
}
