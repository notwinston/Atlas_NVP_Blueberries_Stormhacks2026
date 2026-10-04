using System;
using System.Collections.Generic;
using System.Diagnostics;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests
{
    /// <summary>
    /// FloorScan on synthetic rooms (Master Plan section 11.1, test <c>FloorScan_Synthetic</c>): the free
    /// rectangle, the loop-fit, the distance field, obstacle boxes and the rule that unseen floor is never free.
    /// </summary>
    public class FloorScan_Synthetic
    {
        const float Cell = 0.05f;

        static readonly float2[] LOutline =
        {
            new float2(0f, 0f), new float2(4f, 0f), new float2(4f, 1.5f),
            new float2(2.5f, 1.5f), new float2(2.5f, 3f), new float2(0f, 3f),
        };

        // A 4 x 5 m room with a 1.0 x 0.6 m table, 0.75 m high, centred on (2.0, 2.4).
        static SyntheticRoom TableRoom()
        {
            return SyntheticRoom.Rectangle(4f, 5f).SetWalker(new float2(2f, 4f))
                .AddBox(new float2(2f, 2.4f), new float3(1.0f, 0.75f, 0.6f));
        }

        static FloorCell At(FloorGridResult r, float x, float z)
        {
            return r.CellAt(r.ToRoom(new float2(x, z)));
        }

        static bool Has(FloorCell flags, FloorCell flag)
        {
            return (flags & flag) != 0;
        }

        [Test]
        public void Rectangle_3x3()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(3f, 3f).Build());
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.Ok));
            Assert.That(r.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Fit.S, Is.EqualTo(2.0).Within(1e-5));
            Assert.That(r.Fit.L5, Is.EqualTo(1.885).Within(0.001));
            Assert.That(r.Standing, Is.False);
            Assert.That(r.Glued.Nx, Is.EqualTo(2));
            Assert.That(r.Glued.SizeX, Is.EqualTo(1.2).Within(1e-5));
            Assert.That(r.Aligned.N, Is.EqualTo(3));
            Assert.That(r.Aligned.G, Is.EqualTo(0.8).Within(1e-5));
            // The room frame: origin at the centre of the free rectangle, on the floor.
            Assert.That(math.distance(r.AnchorPosition, new float3(1.5f, 0f, 1.5f)), Is.LessThan(1e-5f));
            Assert.That(r.AnchorYaw, Is.EqualTo(0f));
            Assert.That(r.UnknownShare, Is.EqualTo(0f));
            // The walkable region is the room shrunk by 0.30 m.
            Assert.That(r.WalkableHoles, Is.Empty);
            var expected = new[] { new float2(-1.2f, -1.2f), new float2(1.2f, -1.2f), new float2(1.2f, 1.2f), new float2(-1.2f, 1.2f) };
            Assert.That(r.WalkableOuter.Length, Is.EqualTo(4));
            for (int k = 0; k < 4; k++) Assert.That(math.distance(r.WalkableOuter[k], expected[k]), Is.LessThan(1e-5f));
            // Walls are structure, not furniture.
            Assert.That(r.Boxes, Is.Empty);
        }

        [Test]
        public void Rectangle_4x5()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(4f, 5f).Build());
            Assert.That(r.Wf, Is.EqualTo(3.4f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(4.4f).Within(1e-5f));
            Assert.That(r.Fit.S, Is.EqualTo(3.0).Within(1e-5));
            Assert.That(r.Fit.L5, Is.EqualTo(2.83).Within(0.005));
            Assert.That(r.Glued.Nx, Is.EqualTo(2));
            Assert.That(r.Glued.Nz, Is.EqualTo(3));
            Assert.That(r.Aligned.N, Is.EqualTo(3));
            Assert.That(r.Aligned.M, Is.EqualTo(3));
            Assert.That(r.AnchorYaw, Is.EqualTo(0f));
        }

        [Test]
        public void Rectangle_5x4_TurnsTheFrameSoThatXIsTheShortSide()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(5f, 4f).Build());
            Assert.That(r.Wf, Is.EqualTo(3.4f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(4.4f).Within(1e-5f));
            Assert.That(r.AnchorYaw, Is.EqualTo(math.PI / 2f).Within(1e-6f));
            Assert.That(r.Width, Is.LessThan(r.Height));
            // Unity yaw +90 degrees carries +X to -Z: one metre towards -Z in the session is one metre along room +X.
            Assert.That(math.distance(r.ToRoom(new float2(2.5f, 1f)), new float2(1f, 0f)), Is.LessThan(1e-5f));
            Assert.That(math.distance(r.ToInput(new float2(1f, 0f)), new float2(2.5f, 1f)), Is.LessThan(1e-5f));
            // The room's corners in the room frame, and the grid covering them.
            Assert.That(math.distance(r.Outline[0], new float2(2f, -2.5f)), Is.LessThan(1e-5f));
            Assert.That(Has(At(r, 2.5f, 2f), FloorCell.Walk), Is.True);
            Assert.That(Has(At(r, 0.1f, 0.1f), FloorCell.Walk), Is.False);
            Assert.That(Has(At(r, 0.1f, 0.1f), FloorCell.Free), Is.True);
        }

        [Test]
        public void LShape()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Polygon(LOutline, new float2(1.25f, 1.5f)).Build());
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.Ok));
            // Analytic: the 2.5 x 3 m leg shrunk by 0.30 m per side beats the 4 x 1.5 m leg (3.4 x 0.9 m).
            Assert.That(r.Wf, Is.EqualTo(1.9f).Within(0.05f));
            Assert.That(r.Df, Is.EqualTo(2.4f).Within(0.05f));
            Assert.That(math.distance(r.AnchorPosition, new float3(1.25f, 0f, 1.5f)), Is.LessThan(0.03f));
            Assert.That(r.Fit.S, Is.EqualTo(1.5).Within(0.05));
            // The notch is outside the outline; the other leg is walkable but not part of the rectangle.
            Assert.That(Has(At(r, 3.2f, 2.2f), FloorCell.Inside), Is.False);
            Assert.That(Has(At(r, 3.2f, 2.2f), FloorCell.Free), Is.False);
            Assert.That(Has(At(r, 3.2f, 0.75f), FloorCell.Walk), Is.True);
            Assert.That(r.WalkableHoles, Is.Empty);
            Assert.That(r.WalkableOuter.Length, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void RoomWithTable()
        {
            FloorGridResult r = FloorScanCore.Run(TableRoom().Build());
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.Ok));
            // Analytic: beyond the table (z from 3.0 to 4.7) lies 3.4 x 1.7 m; in front of it only 3.4 x 1.5 m.
            Assert.That(r.Wf, Is.EqualTo(1.7f).Within(0.05f));
            Assert.That(r.Df, Is.EqualTo(3.4f).Within(0.05f));
            Assert.That(math.distance(r.AnchorPosition, new float3(2f, 0f, 3.85f)), Is.LessThan(0.03f));
            // Table cells are not free, nor is the one cell grown round them; the next cell is.
            for (float x = 1.525f; x < 2.5f; x += 0.05f)
            {
                for (float z = 2.125f; z < 2.7f; z += 0.05f)
                {
                    FloorCell cell = At(r, x, z);
                    Assert.That(Has(cell, FloorCell.Obstacle), Is.True);
                    Assert.That(Has(cell, FloorCell.Free | FloorCell.Walk), Is.False);
                }
            }
            Assert.That(Has(At(r, 1.475f, 2.4f), FloorCell.Free), Is.False);
            Assert.That(Has(At(r, 1.425f, 2.4f), FloorCell.Free), Is.True);
            Assert.That(Has(At(r, 1.425f, 2.4f), FloorCell.Walk), Is.False);
            Assert.That(Has(At(r, 1.175f, 2.4f), FloorCell.Walk), Is.True);
            // One hole in the walkable region, running clockwise round the table.
            Assert.That(r.WalkableHoles.Length, Is.EqualTo(1));
            Assert.That(SignedArea(r.WalkableHoles[0]), Is.LessThan(0f));
            Assert.That(SignedArea(r.WalkableOuter), Is.GreaterThan(0f));
            float2 table = r.ToRoom(new float2(2f, 2.4f));
            foreach (float2 p in r.WalkableHoles[0])
            {
                // After the quarter turn of the frame the table measures 0.6 m along room X and 1.0 m along Z.
                // A walkable cell's centre is more than 0.30 m from it; the cell's corner can be half a cell's
                // diagonal nearer where the hole rounds the table's corners.
                float2 d = math.max(math.abs(p - table) - new float2(0.3f, 0.5f), 0f);
                Assert.That(math.length(d), Is.GreaterThan(0.30f - 0.036f), "hole point " + p);
                Assert.That(math.length(d), Is.LessThan(0.30f + 0.08f), "hole point " + p);
            }
        }

        [Test]
        public void FloorOutsideTheConfirmedOutlineIsNotFree()
        {
            var outline = new[] { new float2(0f, 0f), new float2(3f, 0f), new float2(3f, 3f), new float2(0f, 3f) };
            var room = new SyntheticRoom();
            room.AddFloorRect(-1f, -1f, 4f, 4f).SetOutline(outline).SetWalker(new float2(1.5f, 1.5f));
            FloorGridResult r = FloorScanCore.Run(room.Build());
            Assert.That(r.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(2.4f).Within(1e-5f));
            FloorCell beyond = At(r, 3.125f, 1.5f);
            Assert.That(Has(beyond, FloorCell.Floor), Is.True);
            Assert.That(Has(beyond, FloorCell.Inside | FloorCell.Free | FloorCell.Walk), Is.False);
            Assert.That(r.SdfAt(r.ToRoom(new float2(3.125f, 1.5f))), Is.LessThan(0f));
            for (int c = 0; c < r.Cells.Length; c++)
                if (!Has(r.Cells[c], FloorCell.Inside)) Assert.That(Has(r.Cells[c], FloorCell.Free), Is.False);
        }

        [TestCase(30f)]
        [TestCase(-33f)]
        [TestCase(45f)]
        [TestCase(100f)]
        [TestCase(180f)]
        public void RotatedRoom_GivesTheSameRectangle(float degrees)
        {
            FloorGridResult straight = FloorScanCore.Run(TableRoom().Build());
            var offset = new float3(0.37f, 1.2f, -2.11f);
            float angle = math.radians(degrees);
            FloorGridResult turned = FloorScanCore.Run(TableRoom().Move(angle, offset).Build());
            Assert.That(turned.Status, Is.EqualTo(FloorGridStatus.Ok));
            Assert.That(turned.Wf, Is.EqualTo(straight.Wf).Within(1e-5f));
            Assert.That(turned.Df, Is.EqualTo(straight.Df).Within(1e-5f));
            Assert.That(turned.FloorHeight, Is.EqualTo(1.2f).Within(1e-6f));
            // The same cells, not merely the same rectangle.
            Assert.That(turned.Width, Is.EqualTo(straight.Width));
            Assert.That(turned.Height, Is.EqualTo(straight.Height));
            int walkStraight = 0, walkTurned = 0;
            for (int c = 0; c < straight.Cells.Length; c++)
            {
                if (Has(straight.Cells[c], FloorCell.Walk)) walkStraight++;
                if (Has(turned.Cells[c], FloorCell.Walk)) walkTurned++;
            }
            Assert.That(walkTurned, Is.EqualTo(walkStraight));
            // The anchor is where the straight room's anchor went.
            float c0 = math.cos(angle), s0 = math.sin(angle);
            float3 a = straight.AnchorPosition;
            var moved = new float3(c0 * a.x - s0 * a.z + offset.x, offset.y, s0 * a.x + c0 * a.z + offset.z);
            Assert.That(math.distance(turned.AnchorPosition, moved), Is.LessThan(1e-3f));
            // The dominant wall direction is the turn, folded into (-45, 45] degrees.
            // (At exactly 45 degrees either end of the range is right, so compare modulo 90.)
            float off = ((math.degrees(turned.WallDirection) - degrees) % 90f + 90f) % 90f;
            Assert.That(math.min(off, 90f - off), Is.LessThan(0.01f));
            Assert.That(math.degrees(turned.WallDirection), Is.InRange(-45.001f, 45.001f));
            // In the room frame the table is where it was, up to the frame's half turn.
            float2 t0 = straight.ToRoom(new float2(2f, 2.4f));
            float2 t1 = turned.ToRoom(new float2(c0 * 2f - s0 * 2.4f + offset.x, s0 * 2f + c0 * 2.4f + offset.z));
            Assert.That(math.min(math.distance(t0, t1), math.distance(t0, -t1)), Is.LessThan(1e-3f));
            Assert.That(turned.Boxes.Length, Is.EqualTo(1));
        }

        [Test]
        public void RotatedRoom_AxisFromOutlineOrFromWallTriangles()
        {
            float angle = math.radians(30f);
            // No wall planes: the outline's edges give the direction.
            FloorGridResult fromOutline = FloorScanCore.Run(
                SyntheticRoom.Rectangle(3f, 4f).Move(angle, float3.zero).Build(withWallPlanes: false));
            Assert.That(math.degrees(fromOutline.WallDirection), Is.EqualTo(30f).Within(0.01f));
            Assert.That(fromOutline.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(fromOutline.Df, Is.EqualTo(3.4f).Within(1e-5f));
            // No outline either: the triangles classified as wall give it, and the edge of the seen floor bounds the room.
            FloorGridResult fromMesh = FloorScanCore.Run(
                SyntheticRoom.Rectangle(3f, 4f).SetOutline(null).Move(angle, float3.zero).Build(withWallPlanes: false));
            Assert.That(math.degrees(fromMesh.WallDirection), Is.EqualTo(30f).Within(0.01f));
            Assert.That(fromMesh.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(fromMesh.Df, Is.EqualTo(3.4f).Within(1e-5f));
            Assert.That(fromMesh.Outline, Is.Null);
        }

        [Test]
        public void WallDirection_IsLengthWeightedAndFoldedTo90Degrees()
        {
            var input = new FloorGridInput
            {
                Walls = new[]
                {
                    // Four walls of a room turned 20 degrees, given as normals, and one short stray wall.
                    Wall(20f + 90f, 4f), Wall(20f + 180f, 3f), Wall(20f + 270f, 4f), Wall(20f, 3f), Wall(65f, 0.4f),
                },
            };
            double theta = FloorScanCore.DominantWallDirection(input, Array.Empty<float3>(), Array.Empty<int>(), 0);
            Assert.That(math.degrees(theta), Is.EqualTo(20.0).Within(0.5));
            input.Walls = new[] { Wall(80f, 1f) };
            Assert.That(math.degrees(FloorScanCore.DominantWallDirection(input, Array.Empty<float3>(), Array.Empty<int>(), 0)),
                Is.EqualTo(-10.0).Within(1e-3));
            input.Walls = null;
            Assert.That(FloorScanCore.DominantWallDirection(input, Array.Empty<float3>(), Array.Empty<int>(), 0), Is.EqualTo(0.0));
        }

        static WallPlane Wall(float degrees, float length)
        {
            return new WallPlane { Normal = new float2(math.cos(math.radians(degrees)), math.sin(math.radians(degrees))), Length = length };
        }

        [Test]
        public void Sdf_EmptyRoom_ReadsFiveCentimetresLessThanTheDistanceToTheWall()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(3f, 4f).Build());
            int free = 0;
            for (int j = 0; j < r.Height; j++)
            {
                for (int i = 0; i < r.Width; i++)
                {
                    float2 p = r.CellCentre(i, j);
                    float toWall = math.min(1.5f - math.abs(p.x), 2f - math.abs(p.y));
                    float sdf = r.Sdf[j * r.Width + i];
                    bool isFree = Has(r.Cells[j * r.Width + i], FloorCell.Free);
                    Assert.That(sdf > 0f, Is.EqualTo(isFree));
                    if (!isFree) continue;
                    free++;
                    Assert.That(sdf, Is.EqualTo(toWall - Cell).Within(1e-4f), "cell " + i + ", " + j);
                }
            }
            Assert.That(free, Is.EqualTo(58 * 78));
            // In the middle: 1.5 m to the wall from the origin, 1.475 m from the cell centre, less one cell.
            Assert.That(r.SdfAt(new float2(0.01f, 0.01f)), Is.EqualTo(1.425f).Within(1e-4f));
            // Outside: the distance back to the free floor, negative. The first cell beyond the wall is centred
            // 0.025 m outside it and the free floor starts 0.05 m inside.
            Assert.That(r.SdfAt(new float2(1.525f, 0.025f)), Is.EqualTo(-0.075f).Within(1e-4f));
            Assert.That(r.SdfAt(new float2(1.475f, 0.025f)), Is.EqualTo(-0.025f).Within(1e-4f));
            // Beyond a corner the distance is Euclidean.
            Assert.That(r.SdfAt(new float2(1.625f, 2.125f)), Is.EqualTo(-math.sqrt(2f) * 0.175f).Within(1e-4f));
            // WALK is exactly {sdf > 0.25}: 0.30 m from the walls.
            Assert.That(Has(r.CellAt(new float2(1.175f, 0f)), FloorCell.Walk), Is.True);
            Assert.That(Has(r.CellAt(new float2(1.225f, 0f)), FloorCell.Walk), Is.False);
            Assert.That(r.SdfAt(new float2(1.225f, 0.025f)), Is.EqualTo(0.225f).Within(1e-4f));
        }

        [Test]
        public void Sdf_NearATableCorner_IsEuclidean()
        {
            FloorGridResult r = FloorScanCore.Run(TableRoom().Build());
            // The not-free block round the table is its footprint grown by one cell: x 1.45..2.55, z 2.05..2.75.
            // From the cell centred on (1.225, 1.825) the nearest point is the corner (1.45, 2.05).
            float diagonal = r.SdfAt(r.ToRoom(new float2(1.225f, 1.825f)));
            Assert.That(diagonal, Is.EqualTo(math.sqrt(2f) * 0.225f).Within(1e-4f));
            // Straight in front of the long side.
            Assert.That(r.SdfAt(r.ToRoom(new float2(2.025f, 1.825f))), Is.EqualTo(0.225f).Within(1e-4f));
            // Inside the table: back to the free floor, negative. The cell centred on z = 2.375 is 0.275 m from
            // the table's edge at z = 2.1, and the free floor starts one grown cell further out.
            Assert.That(r.SdfAt(r.ToRoom(new float2(2.025f, 2.375f))), Is.EqualTo(-(0.275f + Cell)).Within(1e-4f));
        }

        [Test]
        public void ObstacleBox_FromATable()
        {
            FloorGridResult r = FloorScanCore.Run(TableRoom().Build());
            Assert.That(r.Boxes.Length, Is.EqualTo(1));
            ObstacleBox box = r.Boxes[0];
            Assert.That(box.FromScan, Is.True);
            float2 table = r.ToRoom(new float2(2f, 2.4f));
            Assert.That(math.distance(new float2(box.Base.x, box.Base.z), table), Is.LessThan(1e-4f));
            Assert.That(box.Base.y, Is.EqualTo(0f));
            Assert.That(box.Size.y, Is.EqualTo(0.75f).Within(1e-5f));
            Assert.That(box.Yaw, Is.EqualTo(0f));
            // The frame took a quarter turn, so the table's 1.0 m side lies along room Z.
            Assert.That(box.Size.x, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(box.Size.z, Is.EqualTo(1.0f).Within(1e-4f));
        }

        [Test]
        public void ObstacleBox_Turned()
        {
            SyntheticRoom room = SyntheticRoom.Rectangle(4f, 5f)
                .AddBox(new float2(2.5f, 3.6f), new float3(1.2f, 0.9f, 0.5f), math.radians(30f), SurfaceClass.Seat)
                .SetWalker(new float2(1.5f, 1.5f));
            FloorGridResult r = FloorScanCore.Run(room.Build());
            Assert.That(r.Boxes.Length, Is.EqualTo(1));
            ObstacleBox box = r.Boxes[0];
            float2 centre = r.ToRoom(new float2(2.5f, 3.6f));
            Assert.That(math.distance(new float2(box.Base.x, box.Base.z), centre), Is.LessThan(0.05f));
            Assert.That(box.Size.y, Is.EqualTo(0.9f).Within(1e-5f));
            // The box is fitted to whole cells, so it is never smaller than the furniture and at most a cell's
            // diagonal larger at each end.
            bool longAlongX = box.Size.x > box.Size.z;
            Assert.That(math.max(box.Size.x, box.Size.z), Is.InRange(1.2f, 1.2f + 0.15f));
            Assert.That(math.min(box.Size.x, box.Size.z), Is.InRange(0.5f, 0.5f + 0.15f));
            // Its long side points 30 degrees off the room's axes, in Unity's sense of yaw.
            float longYaw = math.degrees(box.Yaw) + (longAlongX ? 0f : 90f);
            float expected = 30f - math.degrees(r.AnchorYaw);
            float diff = math.abs(((longYaw - expected) % 180f + 180f) % 180f);
            Assert.That(math.min(diff, 180f - diff), Is.LessThanOrEqualTo(5f), "yaw of the long side");
            // Every cell under the true footprint is an obstacle.
            Assert.That(Has(At(r, 2.5f, 3.6f), FloorCell.Obstacle), Is.True);
            Assert.That(Has(At(r, 2.9f, 3.4f), FloorCell.Obstacle), Is.True);
            Assert.That(Has(At(r, 2.9f, 4.0f), FloorCell.Obstacle), Is.False);
        }

        [Test]
        public void ObstacleBoxes_MergeWithSuppliedBoxes()
        {
            // The mesh shows a table and a stool; RoomPlan reports the table (slightly off) and a cupboard the mesh missed.
            SyntheticRoom room = TableRoom()
                .AddBox(new float2(0.6f, 0.6f), new float3(0.4f, 0.45f, 0.4f), 0f, SurfaceClass.Seat)
                .AddExternalBox(new ObstacleBox { Base = new float3(2.02f, 0f, 2.41f), Size = new float3(1.0f, 0.75f, 0.6f), Category = 2, SourceId = 7 })
                .AddExternalBox(new ObstacleBox { Base = new float3(3.7f, 0f, 1.0f), Size = new float3(0.6f, 1.8f, 1.0f), Category = 7, SourceId = 8 });
            FloorGridResult r = FloorScanCore.Run(room.Move(math.radians(15f), new float3(1f, 0.3f, 2f)).Build());
            Assert.That(r.Boxes.Length, Is.EqualTo(3));
            Assert.That(r.Boxes[0].FromScan, Is.False);
            Assert.That(r.Boxes[0].SourceId, Is.EqualTo(7));
            Assert.That(r.Boxes[0].Category, Is.EqualTo(2));
            Assert.That(r.Boxes[1].SourceId, Is.EqualTo(8));
            Assert.That(r.Boxes[2].FromScan, Is.True);
            Assert.That(r.Boxes[2].Size.y, Is.EqualTo(0.45f).Within(1e-4f));
            // Supplied boxes arrive in the room frame, on the floor, square to the room.
            Assert.That(r.Boxes[0].Base.y, Is.EqualTo(0f).Within(1e-5f));
            float yaw = math.abs(r.Boxes[0].Yaw);
            Assert.That(math.min(yaw, math.abs(yaw - math.PI / 2f)), Is.LessThan(1e-3f));
            float c = math.cos(math.radians(15f)), s = math.sin(math.radians(15f));
            float2 tableInSession = new float2(c * 2.02f - s * 2.41f + 1f, s * 2.02f + c * 2.41f + 2f);
            float2 inRoom = r.ToRoom(tableInSession);
            Assert.That(math.distance(new float2(r.Boxes[0].Base.x, r.Boxes[0].Base.z), inRoom), Is.LessThan(1e-4f));
            // The cupboard blocks the floor although no triangle showed it.
            float2 cupboard = r.ToRoom(new float2(c * 3.7f - s * 1.0f + 1f, s * 3.7f + c * 1.0f + 2f));
            Assert.That(Has(r.CellAt(cupboard), FloorCell.Obstacle), Is.True);
            Assert.That(Has(r.CellAt(cupboard), FloorCell.Free), Is.False);
        }

        [Test]
        public void ThingsBelowTenCentimetresOrAboveTwoMetresDoNotBlock()
        {
            SyntheticRoom room = SyntheticRoom.Rectangle(3f, 3f)
                .AddBox(new float2(1.5f, 1.5f), new float3(1f, 0.08f, 1f), 0f, SurfaceClass.None)             // a rug
                .AddBox(new float2(1.0f, 1.0f), new float3(0.5f, 0.3f, 0.5f), 0f, SurfaceClass.None, 2.05f)     // a lamp shade
                .AddQuad(new float3(0f, 2.4f, 0f), new float3(3f, 2.4f, 0f), new float3(3f, 2.4f, 3f), new float3(0f, 2.4f, 3f), SurfaceClass.Ceiling);
            FloorGridResult r = FloorScanCore.Run(room.Build());
            Assert.That(r.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Boxes, Is.Empty);
            Assert.That(r.WalkableHoles, Is.Empty);
        }

        [Test]
        public void UnknownCellsAreNeverFree()
        {
            // Only two thirds of the floor were seen (a mirror, say): the rest is inside the outline but unknown.
            var outline = new[] { new float2(0f, 0f), new float2(3f, 0f), new float2(3f, 3f), new float2(0f, 3f) };
            var room = new SyntheticRoom();
            room.AddFloorRect(0f, 0f, 2f, 3f).AddWalls(outline, 2.4f).SetOutline(outline).SetWalker(new float2(1f, 1.5f));
            FloorGridResult r = FloorScanCore.Run(room.Build());
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.Ok));
            for (int c = 0; c < r.Cells.Length; c++)
                if (!Has(r.Cells[c], FloorCell.Floor)) Assert.That(Has(r.Cells[c], FloorCell.Free | FloorCell.Walk), Is.False);
            FloorCell unseen = At(r, 2.5f, 1.5f);
            Assert.That(Has(unseen, FloorCell.Inside), Is.True);
            Assert.That(Has(unseen, FloorCell.Floor | FloorCell.Free), Is.False);
            Assert.That(r.UnknownShare, Is.EqualTo(1f / 3f).Within(1e-4f));
            // Free floor runs from 0.05 to 2.0 m; walkable from 0.30 to 1.75 m: 1.45 m, which is standing mode.
            Assert.That(r.Wf, Is.EqualTo(1.45f).Within(1e-5f));
            Assert.That(r.Df, Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(r.Standing, Is.True);
        }

        [Test]
        public void SmallUnseenSpecksAreClosed_LargerHolesAreNot()
        {
            var outline = new[] { new float2(0f, 0f), new float2(3f, 0f), new float2(3f, 3f), new float2(0f, 3f) };
            // One 5 cm cell missing from the floor.
            FloorGridResult speck = FloorScanCore.Run(FloorWithHole(outline, 1.0f, 1.05f).Build());
            Assert.That(Has(At(speck, 1.025f, 1.025f), FloorCell.Free), Is.True);
            Assert.That(speck.UnknownShare, Is.EqualTo(0f));
            Assert.That(speck.Wf, Is.EqualTo(2.4f).Within(1e-5f));
            // A 30 cm hole stays unknown and not free, and the walkable region keeps clear of it.
            FloorGridResult hole = FloorScanCore.Run(FloorWithHole(outline, 1.0f, 1.3f).Build());
            Assert.That(Has(At(hole, 1.125f, 1.125f), FloorCell.Floor | FloorCell.Free), Is.False);
            Assert.That(hole.UnknownShare, Is.EqualTo(36f / 3600f).Within(1e-5f));
            Assert.That(hole.WalkableHoles.Length, Is.EqualTo(1));
            Assert.That(hole.Wf, Is.LessThan(2.4f - 0.5f));
        }

        // A 3 x 3 m floor with a square hole from (lo, lo) to (hi, hi) that the scan never saw.
        static SyntheticRoom FloorWithHole(float2[] outline, float lo, float hi)
        {
            var room = new SyntheticRoom();
            room.AddFloorRect(0f, 0f, 3f, lo).AddFloorRect(0f, hi, 3f, 3f).AddFloorRect(0f, lo, lo, hi).AddFloorRect(hi, lo, 3f, hi);
            return room.AddWalls(outline, 2.4f).SetOutline(outline).SetWalker(new float2(2.2f, 2.2f));
        }

        [Test]
        public void TheWalkersPathIsFloor()
        {
            var outline = new[] { new float2(0f, 0f), new float2(3f, 0f), new float2(3f, 3f), new float2(0f, 3f) };
            var room = new SyntheticRoom();
            room.AddFloorRect(0f, 0f, 2f, 3f).SetOutline(outline).SetWalker(new float2(1f, 1.5f));
            room.AddPathPoint(new float2(2.52f, 0.5f)).AddPathPoint(new float2(2.52f, 2.5f));
            FloorGridResult r = FloorScanCore.Run(room.Build());
            // Floor within 0.15 m of the path, along the whole step between the two samples, and no further.
            for (float z = 0.525f; z < 2.5f; z += 0.05f)
            {
                Assert.That(Has(At(r, 2.52f, z), FloorCell.Floor), Is.True, "z " + z);
                Assert.That(Has(At(r, 2.52f, z), FloorCell.Free), Is.True, "z " + z);
                Assert.That(Has(At(r, 2.42f, z), FloorCell.Floor), Is.True);
                Assert.That(Has(At(r, 2.62f, z), FloorCell.Floor), Is.True);
                Assert.That(Has(At(r, 2.72f, z), FloorCell.Floor), Is.False);
                Assert.That(Has(At(r, 2.27f, z), FloorCell.Floor), Is.False);
            }
            Assert.That(Has(At(r, 2.52f, 2.72f), FloorCell.Floor), Is.False);
        }

        [TestCase(2.0f, 1.4f, true)]
        [TestCase(2.05f, 1.45f, true)]
        [TestCase(2.1f, 1.5f, false)]
        public void StandingMode_BelowOnePointFiveMetresOfFreeFloor(float room, float wf, bool standing)
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(room, 3f).Build());
            Assert.That(r.Wf, Is.EqualTo(wf).Within(1e-5f));
            Assert.That(r.Standing, Is.EqualTo(standing));
            Assert.That(r.Standing, Is.EqualTo(LoopFit.IsStanding(r.Wf, r.Df)));
            Assert.That(r.Fit.S, Is.EqualTo(math.max(1.0, wf - 0.4)).Within(1e-5));
        }

        [Test]
        public void NoWalkableFloor_IsReportedAndStanding()
        {
            FloorGridResult r = FloorScanCore.Run(SyntheticRoom.Rectangle(0.5f, 0.5f).Build());
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.NoWalkableFloor));
            Assert.That(r.Standing, Is.True);
            Assert.That(r.Wf, Is.EqualTo(0f));
            Assert.That(r.WalkableOuter, Is.Empty);
            Assert.That(r.Cells.Length, Is.EqualTo(r.Width * r.Height));
            // The frame falls back to the walker's own cell.
            Assert.That(math.distance(r.AnchorPosition, new float3(0.25f, 0f, 0.25f)), Is.LessThan(0.04f));

            FloorGridResult nothing = FloorScanCore.Run(new FloorGridInput());
            Assert.That(nothing.Status, Is.EqualTo(FloorGridStatus.NoFloor));
            Assert.That(nothing.Cells, Is.Empty);
            Assert.That(nothing.Standing, Is.True);
        }

        [Test]
        public void FloorHeight_FromThePlanesOrFromTheMesh()
        {
            SyntheticRoom room = TableRoom().Move(0f, new float3(0f, 0.7f, 0f));
            FloorGridInput fromMesh = room.Build(withFloorHeight: false);
            FloorGridResult a = FloorScanCore.Run(fromMesh);
            Assert.That(a.FloorHeight, Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(a.Wf, Is.EqualTo(1.7f).Within(1e-5f));
            Assert.That(a.AnchorPosition.y, Is.EqualTo(0.7f).Within(1e-6f));

            FloorGridInput fromPlanes = room.Build(withFloorHeight: false);
            fromPlanes.Planes = new[]
            {
                new HorizontalPlane { Height = 1.45f, Area = 0.6f, IsFloor = false },    // the table top
                new HorizontalPlane { Height = 0.71f, Area = 2f, IsFloor = true },
                new HorizontalPlane { Height = 0.70f, Area = 14f, IsFloor = true },
            };
            Assert.That(FloorScanCore.Run(fromPlanes).FloorHeight, Is.EqualTo(0.70f).Within(1e-6f));
            // No plane is classified floor: the lowest large one stands in.
            fromPlanes.Planes = new[]
            {
                new HorizontalPlane { Height = 3.1f, Area = 12f },
                new HorizontalPlane { Height = 0.3f, Area = 0.2f },
                new HorizontalPlane { Height = 0.7f, Area = 9f },
            };
            Assert.That(FloorScanCore.Run(fromPlanes).FloorHeight, Is.EqualTo(0.7f).Within(1e-6f));
        }

        [Test]
        public void LargeRoom_6x8_FinishesWellUnderASecond()
        {
            // A dense floor (10 cm quads) so that the triangle count is that of a real scan.
            var outline = new[] { new float2(0f, 0f), new float2(6f, 0f), new float2(6f, 8f), new float2(0f, 8f) };
            var room = new SyntheticRoom();
            for (int j = 0; j < 80; j++)
                for (int i = 0; i < 60; i++)
                    room.AddFloorRect(i * 0.1f, j * 0.1f, (i + 1) * 0.1f, (j + 1) * 0.1f);
            room.AddWalls(outline, 2.4f).SetOutline(outline).SetWalker(new float2(3f, 4f));
            room.AddBox(new float2(1f, 1f), new float3(1f, 0.75f, 0.6f)).AddBox(new float2(5.5f, 4f), new float3(0.6f, 1.8f, 2f));
            Assert.That(room.TriangleCount, Is.GreaterThan(9000));
            FloorGridInput input = room.Build();
            FloorScanCore.Run(input);                 // warm-up: the first call pays for JIT compilation
            var watch = Stopwatch.StartNew();
            FloorGridResult r = FloorScanCore.Run(input);
            watch.Stop();
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500), "one FloorScan of a 6 x 8 m room");
            Assert.That(r.Status, Is.EqualTo(FloorGridStatus.Ok));
            Assert.That(r.Fit.S, Is.EqualTo(3.2).Within(1e-9));     // the lap square is capped

            FloorGridResult empty = FloorScanCore.Run(SyntheticRoom.Rectangle(6f, 8f).Build());
            Assert.That(empty.Wf, Is.EqualTo(5.4f).Within(1e-5f));
            Assert.That(empty.Df, Is.EqualTo(7.4f).Within(1e-5f));
            Assert.That(empty.Glued.Nx, Is.EqualTo(4));
            Assert.That(empty.Glued.Nz, Is.EqualTo(5));
            Assert.That(empty.Aligned.N, Is.EqualTo(4));
            Assert.That(empty.Aligned.M, Is.EqualTo(5));
            Assert.That(empty.Aligned.G, Is.EqualTo(1.35).Within(1e-5));
        }

        [Test]
        public void GridMath_TraceRegion_OuterRingAndHole()
        {
            // A 5 x 4 block of cells with one cell missing in the middle.
            const int w = 7, h = 6;
            var mask = new bool[w * h];
            for (int j = 1; j <= 4; j++)
                for (int i = 1; i <= 5; i++) mask[j * w + i] = true;
            mask[2 * w + 3] = false;
            var outers = new List<int2[]>();
            var holes = new List<int2[]>();
            FloorGridMath.TraceRegion(mask, w, h, outers, holes);
            Assert.That(outers.Count, Is.EqualTo(1));
            Assert.That(holes.Count, Is.EqualTo(1));
            Assert.That(outers[0], Is.EqualTo(new[] { new int2(1, 1), new int2(6, 1), new int2(6, 5), new int2(1, 5) }));
            Assert.That(holes[0], Is.EqualTo(new[] { new int2(3, 2), new int2(3, 3), new int2(4, 3), new int2(4, 2) }));
        }

        [Test]
        public void GridMath_FreeRectangle_PrefersShortSideThenAreaThenTheWalker()
        {
            // Two 4 x 4 squares joined by a corridor one cell wide.
            const int w = 12, h = 6;
            var mask = new bool[w * h];
            for (int j = 1; j <= 4; j++)
                for (int i = 1; i <= 10; i++) mask[j * w + i] = (i <= 4 || i >= 7) || j == 2;
            Assert.That(FloorGridMath.LargestSquare(mask, w, h), Is.EqualTo(4));
            Assert.That(FloorGridMath.FreeRectangle(mask, w, h, 0.05, 72, 0.45, 0.15, out int i0, out int j0, out int i1, out int j1), Is.True);
            Assert.That(new[] { i0, j0, i1, j1 }, Is.EqualTo(new[] { 7, 1, 11, 5 }));     // nearest the walker
            FloorGridMath.FreeRectangle(mask, w, h, 0.05, 72, 0.1, 0.15, out i0, out j0, out i1, out j1);
            Assert.That(new[] { i0, j0, i1, j1 }, Is.EqualTo(new[] { 1, 1, 5, 5 }));
            // The long thin corridor (10 x 1) never wins over a square, whatever its length.
            Assert.That(FloorGridMath.FreeRectangle(new bool[w * h], w, h, 0.05, 72, 0, 0, out i0, out j0, out i1, out j1), Is.False);
        }

        static float SignedArea(float2[] ring)
        {
            float total = 0f;
            for (int k = 0; k < ring.Length; k++)
            {
                float2 p = ring[k], q = ring[(k + 1) % ring.Length];
                total += p.x * q.y - q.x * p.y;
            }
            return 0.5f * total;
        }
    }
}
