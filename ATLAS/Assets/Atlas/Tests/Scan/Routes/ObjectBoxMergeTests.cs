using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>Route 3: provider boxes in the contract's form, merged with the grid's own obstacle boxes in one frame.</summary>
    public class ObjectBoxMergeTests
    {
        const uint TableBit = 1u << 1, ChairBit = 1u << 8, StorageBit = 1u << 6;

        static ScannedBox Box(float3 centre, float3 size, float yaw, uint bits, int id)
        {
            return new ScannedBox { Centre = centre, Size = size, Rotation = quaternion.RotateY(yaw), ClassBits = bits, SourceId = id };
        }

        [Test]
        public void Convert_PutsTheBaseUnderTheCentre()
        {
            ObstacleBox box;
            Assert.That(ObjectBoxMerge.TryConvert(Box(new float3(1f, 0.375f, 2f), new float3(1f, 0.75f, 0.6f), 0.5f, TableBit, 7), out box), Is.True);
            Assert.That(math.distance(box.Base, new float3(1f, 0f, 2f)), Is.LessThan(1e-6f));
            Assert.That(box.Yaw, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(box.Category, Is.EqualTo((int)ObjectCategory.Table));
            Assert.That(box.SourceId, Is.EqualTo(7));
            Assert.That(box.FromScan, Is.False);
        }

        [TestCase(0u, ObjectCategory.Unknown)]
        [TestCase(1u << 0, ObjectCategory.Couch)]
        [TestCase(1u << 8, ObjectCategory.Chair)]
        [TestCase(1u << 17, ObjectCategory.WasherDryer)]
        [TestCase(1u << 31, ObjectCategory.Other)]
        public void Convert_MapsTheCategory(uint bits, ObjectCategory expected)
        {
            ObstacleBox box;
            ObjectBoxMerge.TryConvert(Box(new float3(0f, 0.5f, 0f), new float3(1f, 1f, 1f), 0f, bits, 1), out box);
            Assert.That(box.Category, Is.EqualTo((int)expected));
        }

        [Test]
        public void Convert_RefusesBrokenBoxes()
        {
            ObstacleBox box;
            Assert.That(ObjectBoxMerge.TryConvert(Box(new float3(0f, 0f, 0f), new float3(0f, 1f, 1f), 0f, 0u, 1), out box), Is.False);
            Assert.That(ObjectBoxMerge.TryConvert(Box(new float3(0f, 0f, 0f), new float3(1f, float.NaN, 1f), 0f, 0u, 1), out box), Is.False);
            Assert.That(ObjectBoxMerge.TryConvert(Box(new float3(0f, 0f, 0f), new float3(9f, 1f, 1f), 0f, 0u, 1), out box), Is.False);
            Assert.That(ObjectBoxMerge.TryConvert(Box(new float3(float.PositiveInfinity, 0f, 0f), new float3(1f, 1f, 1f), 0f, 0u, 1), out box), Is.False);
        }

        [Test]
        public void Merge_DropsRepeatsOfOnePiece()
        {
            var boxes = new[]
            {
                Box(new float3(1f, 0.375f, 2f), new float3(1.0f, 0.75f, 0.6f), 0f, 0u, 1),
                Box(new float3(3f, 0.45f, 1f), new float3(0.5f, 0.9f, 0.5f), 0f, ChairBit, 2),
                // The table again, a little larger, a quarter turn round and with its category.
                Box(new float3(1.05f, 0.4f, 2.02f), new float3(0.65f, 0.8f, 1.05f), math.PI / 2f, TableBit, 3),
                Box(new float3(0f, 0f, 0f), new float3(0f, 0f, 0f), 0f, 0u, 4),
            };
            ObstacleBox[] merged = ObjectBoxMerge.Merge(boxes, boxes.Length);
            Assert.That(merged.Length, Is.EqualTo(2));
            Assert.That(merged[0].SourceId, Is.EqualTo(3), "the larger report stays");
            Assert.That(merged[0].Category, Is.EqualTo((int)ObjectCategory.Table));
            Assert.That(merged[1].Category, Is.EqualTo((int)ObjectCategory.Chair));
        }

        [Test]
        public void Merge_KeepsTheCategoryOfASmallerRepeat()
        {
            var boxes = new[]
            {
                Box(new float3(1f, 0.4f, 2f), new float3(1.05f, 0.8f, 0.65f), 0f, 0u, 1),
                Box(new float3(1f, 0.375f, 2f), new float3(1f, 0.75f, 0.6f), 0f, TableBit, 2),
            };
            ObstacleBox[] merged = ObjectBoxMerge.Merge(boxes, 2);
            Assert.That(merged.Length, Is.EqualTo(1));
            Assert.That(merged[0].SourceId, Is.EqualTo(1));
            Assert.That(merged[0].Category, Is.EqualTo((int)ObjectCategory.Table));
        }

        [Test]
        public void ProviderBoxesAndScanBlobs_BecomeOneListInTheRoomFrame()
        {
            // A 4 x 5 m room, turned and shifted in the session frame. The mesh shows a table and a cupboard;
            // RoomPlan reports only the table.
            const float turn = 0.3f;
            var shift = new float3(2f, 0.2f, -1f);
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f)
                .AddBox(new float2(2f, 3.2f), new float3(1f, 0.75f, 0.6f))
                .AddBox(new float2(0.3f, 4.5f), new float3(0.6f, 1.8f, 0.6f), 0f, SurfaceClass.None);
            scan.SetWalker(new float2(2f, 1.2f));
            scan.Move(turn, shift);
            float2 tableSession = ScanTestRooms.Moved(new float2(2f, 3.2f), turn, shift.xz);
            var provider = new[]
            {
                // Unity yaw runs the other way round from the session's turn.
                Box(new float3(tableSession.x, shift.y + 0.375f, tableSession.y), new float3(1f, 0.75f, 0.6f), -turn, TableBit, 1),
            };
            ScanSnapshot snapshot = ScanTestRooms.Snapshot(scan.Build());
            snapshot.Boxes = ObjectBoxMerge.Merge(provider, provider.Length);

            ScanSolution solution = ScanSolver.Solve(snapshot, "boxes");

            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.ObjectBoxes));
            int supplied, fromScan;
            ObjectBoxMerge.Count(solution.Grid.Boxes, out supplied, out fromScan);
            Assert.That(supplied, Is.EqualTo(1));
            Assert.That(fromScan, Is.EqualTo(1), "the table's blob is explained by its box; the cupboard gets a box of its own");

            RoomModel room = solution.Room;
            Assert.That(room.Objects.Length, Is.EqualTo(2));
            RoomObject cupboard = room.Objects[0], table = room.Objects[1];
            Assert.That(cupboard.Category, Is.EqualTo(ObjectCategory.Unknown));
            Assert.That(cupboard.Size.y, Is.EqualTo(1.8f).Within(1e-3f));
            Assert.That(table.Category, Is.EqualTo(ObjectCategory.Table));
            Assert.That(table.Base.y, Is.EqualTo(0f).Within(1e-4f));

            // One frame: each object sits where the mesh put it, to the 10 cm of task P1-C04.
            float2 tableRoom = solution.Grid.ToRoom(tableSession);
            float2 cupboardRoom = solution.Grid.ToRoom(ScanTestRooms.Moved(new float2(0.3f, 4.5f), turn, shift.xz));
            Assert.That(math.distance(table.Base.xz, tableRoom), Is.LessThan(0.01f));
            Assert.That(math.distance(cupboard.Base.xz, cupboardRoom), Is.LessThan(0.10f));
            // The table stands square to the walls of the room, whatever the session frame.
            float square = math.abs(math.sin(2f * table.Yaw));
            Assert.That(square, Is.LessThan(0.01f));
            Assert.That(room.Sdf.IsFree(table.Base.xz), Is.False);
            Assert.That(room.Sdf.IsFree(cupboard.Base.xz), Is.False);
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void FloorUnderAScanBox_IsNeverFree()
        {
            // An L-shaped couch in the corner: its blob gets one rectangular box, which also covers the
            // floor inside the L. That floor must not stay free while the room shows a box on it.
            SyntheticRoom scan = SyntheticRoom.Rectangle(5f, 5f)
                .AddBox(new float2(1.5f, 4.5f), new float3(3f, 0.8f, 1f), 0f, SurfaceClass.Seat)
                .AddBox(new float2(0.5f, 3f), new float3(1f, 0.8f, 2f), 0f, SurfaceClass.Seat);
            scan.SetWalker(new float2(3.5f, 1.5f));
            ScanSolution solution = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build()), "couch");

            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.FloorScan));
            Assert.That(solution.Room.Objects.Length, Is.EqualTo(1));
            Assert.That(solution.Grid.Boxes[0].FromScan, Is.True);
            float2 insideTheL = solution.Grid.ToRoom(new float2(2f, 3f));
            Assert.That(solution.Room.Sdf.IsFree(insideTheL), Is.False);
            Assert.That(RoomModelValidator.Errors(solution.Room), Is.Empty);
        }

        [Test]
        public void BoxInTheWrongPlace_DoesNotCostTheRoom()
        {
            // A phantom box that fills the whole room: the outline alone still gives a room, at route 1.
            var snapshot = new ScanSnapshot
            {
                Outline = ScanTestRooms.Rectangle(3f, 3f), Walker = new float2(1.5f, 1.5f),
                Boxes = new[] { new ObstacleBox { Base = new float3(1.5f, 0f, 1.5f), Size = new float3(3f, 1f, 3f), Category = (int)ObjectCategory.Bed } },
            };
            ScanSolution solution = ScanSolver.Solve(snapshot, "phantom");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            Assert.That(solution.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(solution.Room.Objects, Is.Empty);
            Assert.That(solution.Room.FreeRect.Wf, Is.EqualTo(2.4f).Within(1e-4f));
        }
    }
}
