using Atlas.Scan.Grid;
using Atlas.Scan.Tests.Routes;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests
{
    /// <summary>
    /// Rule G13 of the room contract: the supplied boxes come first, then one box per obstacle blob they do not
    /// explain. A blob is explained when at least half its cells lie under the supplied boxes taken together,
    /// so furniture that stands in a row, box touching box, is not given a further box round the whole row.
    /// </summary>
    public class FloorScanSuppliedBoxTests
    {
        static ObstacleBox Box(ObjectCategory category, float x, float z, float w, float h, float d, int id)
        {
            return new ObstacleBox { Base = new float3(x, 0f, z), Size = new float3(w, h, d), Category = (int)category, SourceId = id };
        }

        // An oven between a cupboard and a refrigerator on the south wall of a 4 m x 5 m room, each touching the next.
        static ObstacleBox[] KitchenRow()
        {
            return new[]
            {
                Box(ObjectCategory.Oven, 2f, 0.3f, 0.6f, 0.9f, 0.6f, 1),
                Box(ObjectCategory.Storage, 1.3f, 0.3f, 0.8f, 0.9f, 0.6f, 2),
                Box(ObjectCategory.Refrigerator, 2.65f, 0.35f, 0.7f, 1.8f, 0.7f, 3),
            };
        }

        static int Count(RoomModel room, ObjectCategory category)
        {
            int n = 0;
            foreach (RoomObject o in room.Objects) if (o.Category == category) n++;
            return n;
        }

        static int FromScan(FloorGridResult grid)
        {
            int n = 0;
            foreach (ObstacleBox box in grid.Boxes) if (box.FromScan) n++;
            return n;
        }

        [Test]
        public void ThreeSuppliedBoxesInARow_AreThreeObjects_AndNoUnknownOne()
        {
            // No one of the three covers half of the row, together they cover all of it.
            RoomModel room = RoomModelBuilder.FromOutline("row", ScanTestRooms.Rectangle(4f, 5f), KitchenRow(), new float2(2f, 2.5f));
            Assert.That(room, Is.Not.Null);
            Assert.That(room.Objects.Length, Is.EqualTo(3));
            Assert.That(Count(room, ObjectCategory.Unknown), Is.EqualTo(0));
            Assert.That(Count(room, ObjectCategory.Oven), Is.EqualTo(1));
            Assert.That(Count(room, ObjectCategory.Storage), Is.EqualTo(1));
            Assert.That(Count(room, ObjectCategory.Refrigerator), Is.EqualTo(1));
            // Every rule, R18 among them: no free floor under any of the three.
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void ThreeScannedUnitsInARow_ThroughTheSolver_AreThreeObjects()
        {
            // The same row as a LiDAR scan sees it: the units' own tops and sides are in the mesh as well.
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f).SetWalker(new float2(2f, 2.5f));
            foreach (ObstacleBox unit in KitchenRow())
            {
                scan.AddBox(unit.Base.xz, unit.Size, 0f, SurfaceClass.None);
                scan.AddExternalBox(unit);
            }
            FloorGridResult grid = FloorScanCore.Run(scan.Build());
            Assert.That(grid.Status, Is.EqualTo(FloorGridStatus.Ok));
            Assert.That(grid.Boxes.Length, Is.EqualTo(3));
            Assert.That(FromScan(grid), Is.EqualTo(0));

            ScanSolution solution = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build(), true), "row");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            RoomModel room = solution.Room;
            Assert.That(room.Objects.Length, Is.EqualTo(3));
            Assert.That(Count(room, ObjectCategory.Unknown), Is.EqualTo(0));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            RoomModel back;
            string error;
            Assert.That(RoomModelJson.TryRead(RoomModelJson.Write(room), out back, out error), Is.True, error);
        }

        [Test]
        public void TwoTouchingSuppliedBoxes_AreTwoObjects_AsBefore()
        {
            ObstacleBox[] pair =
            {
                Box(ObjectCategory.Storage, 2f, 0.2f, 0.9f, 1.8f, 0.4f, 1),
                Box(ObjectCategory.Refrigerator, 1.2f, 0.35f, 0.7f, 1.8f, 0.7f, 2),
            };
            RoomModel room = RoomModelBuilder.FromOutline("pair", ScanTestRooms.Rectangle(4f, 5f), pair, new float2(2f, 2.5f));
            Assert.That(room.Objects.Length, Is.EqualTo(2));
            Assert.That(Count(room, ObjectCategory.Unknown), Is.EqualTo(0));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);

            // Two of the same size: each covers half the blob and a cell more.
            ObstacleBox[] twins =
            {
                Box(ObjectCategory.Storage, 1.5f, 0.3f, 0.6f, 0.9f, 0.6f, 1),
                Box(ObjectCategory.Storage, 2.1f, 0.3f, 0.6f, 0.9f, 0.6f, 2),
            };
            room = RoomModelBuilder.FromOutline("twins", ScanTestRooms.Rectangle(4f, 5f), twins, new float2(2f, 2.5f));
            Assert.That(room.Objects.Length, Is.EqualTo(2));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        // A scanned block of 1.0 m x 0.4 m, 160 cells, with a supplied box at each end. Each box is 0.4 m deep
        // like the block; a box counts the cells whose centre lies within one cell of its footprint.
        static FloorGridResult BlockWithABoxAtEachEnd(float westWidth, float eastWidth)
        {
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f).SetWalker(new float2(2f, 3f));
            scan.AddBox(new float2(1.5f, 1.2f), new float3(1f, 0.8f, 0.4f));
            scan.AddExternalBox(Box(ObjectCategory.Storage, 1f + 0.5f * westWidth, 1.2f, westWidth, 0.8f, 0.4f, 1));
            scan.AddExternalBox(Box(ObjectCategory.Storage, 2f - 0.5f * eastWidth, 1.2f, eastWidth, 0.8f, 0.4f, 2));
            FloorGridResult grid = FloorScanCore.Run(scan.Build());
            Assert.That(grid.Status, Is.EqualTo(FloorGridStatus.Ok));
            return grid;
        }

        [Test]
        public void ABlobHalfUnderTheSuppliedBoxesTogether_IsExplained_AndOneCellColumnLessIsNot()
        {
            // Two boxes of 0.2 m: each reaches 5 of the block's 20 columns (its 4 and one more), 80 cells of
            // 160 together. Exactly half: explained, and no box is added.
            FloorGridResult half = BlockWithABoxAtEachEnd(0.2f, 0.2f);
            Assert.That(half.Boxes.Length, Is.EqualTo(2));
            Assert.That(FromScan(half), Is.EqualTo(0));

            // The east box 0.15 m: 4 columns, 72 cells of 160 together. Under half: the block is an object of its own.
            FloorGridResult less = BlockWithABoxAtEachEnd(0.2f, 0.15f);
            Assert.That(less.Boxes.Length, Is.EqualTo(3));
            Assert.That(FromScan(less), Is.EqualTo(1));
            ObstacleBox found = less.Boxes[2];
            Assert.That(found.FromScan, Is.True);
            Assert.That(found.Category, Is.EqualTo(0));
            // It is the whole block: 1.0 m by 0.4 m, 0.8 m high.
            Assert.That(math.max(found.Size.x, found.Size.z), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(math.min(found.Size.x, found.Size.z), Is.EqualTo(0.4f).Within(1e-3f));
            Assert.That(found.Size.y, Is.EqualTo(0.8f).Within(1e-3f));
        }

        [Test]
        public void AnUnscannedObstacleBesideASuppliedBox_IsStillAnObject()
        {
            // A cupboard the provider reported, and against its side something twice as long that it did not:
            // one blob of 432 cells, of which the cupboard's box reaches 156. That is not explained.
            SyntheticRoom scan = SyntheticRoom.Rectangle(4f, 5f).SetWalker(new float2(2f, 3f));
            scan.AddBox(new float2(1.3f, 0.3f), new float3(0.6f, 0.9f, 0.6f));
            scan.AddBox(new float2(2.2f, 0.3f), new float3(1.2f, 0.5f, 0.6f));
            scan.AddExternalBox(Box(ObjectCategory.Storage, 1.3f, 0.3f, 0.6f, 0.9f, 0.6f, 1));

            ScanSolution solution = ScanSolver.Solve(ScanTestRooms.Snapshot(scan.Build(), true), "beside");
            Assert.That(solution.Failure, Is.EqualTo(ScanFailure.None));
            RoomModel room = solution.Room;
            Assert.That(room.Objects.Length, Is.EqualTo(2));
            Assert.That(Count(room, ObjectCategory.Storage), Is.EqualTo(1));
            Assert.That(Count(room, ObjectCategory.Unknown), Is.EqualTo(1));
            // The unknown object stands where the unreported thing is.
            RoomObject unknown = null;
            foreach (RoomObject o in room.Objects) if (o.Category == ObjectCategory.Unknown) unknown = o;
            float2 thing = solution.Grid.ToRoom(new float2(2.2f, 0.3f));
            float sin, cos;
            math.sincos(unknown.Yaw, out sin, out cos);
            float2 offset = thing - unknown.Base.xz;
            Assert.That(math.abs(offset.x * cos - offset.y * sin), Is.LessThan(0.5f * unknown.Size.x));
            Assert.That(math.abs(offset.x * sin + offset.y * cos), Is.LessThan(0.5f * unknown.Size.z));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);

            // Something small against a large reported box is the box's own scan running over: it is no object.
            scan = SyntheticRoom.Rectangle(4f, 5f).SetWalker(new float2(2f, 3f));
            scan.AddBox(new float2(1.6f, 0.3f), new float3(1.2f, 0.9f, 0.6f));
            scan.AddBox(new float2(2.3f, 0.3f), new float3(0.2f, 0.5f, 0.6f));
            scan.AddExternalBox(Box(ObjectCategory.Storage, 1.6f, 0.3f, 1.2f, 0.9f, 0.6f, 1));
            FloorGridResult grid = FloorScanCore.Run(scan.Build());
            Assert.That(grid.Boxes.Length, Is.EqualTo(1));
        }
    }
}
