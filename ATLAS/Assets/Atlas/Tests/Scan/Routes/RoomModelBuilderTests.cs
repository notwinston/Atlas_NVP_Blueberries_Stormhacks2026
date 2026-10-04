using System;
using Atlas.Scan.Grid;
using Atlas.World;
using NUnit.Framework;
using Unity.Mathematics;

namespace Atlas.Scan.Tests.Routes
{
    /// <summary>The RoomModel builder: the fixtures come back from a scan of the same room, and the rules of sections 4.3 to 4.5 hold.</summary>
    public class RoomModelBuilderTests
    {
        // The fixture seen as a scan: floor everywhere (and beyond the outline), the outline, every object as a box.
        static FloorGridResult ScanOf(RoomModel fixture)
        {
            float2 lo = fixture.Outline[0], hi = fixture.Outline[0];
            foreach (float2 p in fixture.Outline) { lo = math.min(lo, p); hi = math.max(hi, p); }
            var scan = new SyntheticRoom();
            scan.AddFloorRect(lo.x - 1f, lo.y - 1f, hi.x + 1f, hi.y + 1f);
            scan.SetOutline(fixture.Outline);
            scan.SetWalker(float2.zero);
            foreach (RoomObject o in fixture.Objects)
                scan.AddExternalBox(new ObstacleBox { Base = o.Base, Size = o.Size, Yaw = o.Yaw, Category = (int)o.Category, SourceId = o.Id });
            return FloorScanCore.Run(scan.Build());
        }

        static void AssertRing(float2[] expected, float2[] actual, string what)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), what + " point count");
            for (int k = 0; k < expected.Length; k++)
                Assert.That(math.distance(expected[k], actual[k]), Is.LessThan(1e-5f), what + " point " + k);
        }

        [TestCase(RoomFixtures.Sample3x3)]
        [TestCase(RoomFixtures.SampleL3x4)]
        [TestCase(RoomFixtures.Sample4x5Table)]
        public void SampleRoom_IsReproduced(string name)
        {
            RoomModel fixture = RoomFixtures.LoadFromProject(name);
            FloorGridResult grid = ScanOf(fixture);
            Assert.That(grid.Status, Is.EqualTo(FloorGridStatus.Ok));

            RoomModel room = RoomModelBuilder.Build(grid, fixture.Id, fixture.CreatedUtc, ScanRoute.Fixture,
                RoomModel.DefaultWallHeight, fixture.Walls);

            AssertRing(fixture.Outline, room.Outline, "outline");
            Assert.That(room.FreeRect.Wf, Is.EqualTo(fixture.FreeRect.Wf).Within(1e-6f));
            Assert.That(room.FreeRect.Df, Is.EqualTo(fixture.FreeRect.Df).Within(1e-6f));
            Assert.That(room.LapSide, Is.EqualTo(fixture.LapSide).Within(1e-6f));
            Assert.That(room.Curvature.L5, Is.EqualTo(fixture.Curvature.L5).Within(1e-6f));
            Assert.That(room.Curvature.L6, Is.EqualTo(fixture.Curvature.L6).Within(1e-6f));
            Assert.That(room.Curvature.R3, Is.EqualTo(fixture.Curvature.R3).Within(1e-6f));
            Assert.That(room.Glued.Nx, Is.EqualTo(fixture.Glued.Nx));
            Assert.That(room.Glued.Nz, Is.EqualTo(fixture.Glued.Nz));
            Assert.That(room.Glued.SizeX, Is.EqualTo(fixture.Glued.SizeX).Within(1e-6));
            Assert.That(room.Glued.SizeZ, Is.EqualTo(fixture.Glued.SizeZ).Within(1e-6));
            Assert.That(room.Aligned.N, Is.EqualTo(fixture.Aligned.N));
            Assert.That(room.Aligned.M, Is.EqualTo(fixture.Aligned.M));
            Assert.That(room.Aligned.G, Is.EqualTo(fixture.Aligned.G).Within(1e-6));
            Assert.That(room.Standing, Is.EqualTo(fixture.Standing));
            Assert.That(room.Quality.UnknownShare, Is.EqualTo(fixture.Quality.UnknownShare).Within(1e-6f));
            Assert.That(math.length(room.Anchor.Position), Is.LessThan(1e-5f));
            Assert.That(room.Anchor.Yaw, Is.EqualTo(0f).Within(1e-6f));

            Assert.That(room.Sdf.Width, Is.EqualTo(fixture.Sdf.Width));
            Assert.That(room.Sdf.Height, Is.EqualTo(fixture.Sdf.Height));
            Assert.That(room.Sdf.OriginX, Is.EqualTo(fixture.Sdf.OriginX).Within(1e-6f));
            Assert.That(room.Sdf.OriginZ, Is.EqualTo(fixture.Sdf.OriginZ).Within(1e-6f));
            for (int c = 0; c < room.Sdf.Metres.Length; c++)
            {
                Assert.That(room.Sdf.Metres[c], Is.EqualTo(fixture.Sdf.Metres[c]).Within(0.00101f), "field at cell " + c);
                Assert.That(room.Sdf.Metres[c] > 0f, Is.EqualTo(fixture.Sdf.Metres[c] > 0f), "sign at cell " + c);
            }
            AssertRing(fixture.Walkable.Outer, room.Walkable.Outer, "walkable outer");
            Assert.That(room.Walkable.Holes.Length, Is.EqualTo(fixture.Walkable.Holes.Length));
            for (int k = 0; k < fixture.Walkable.Holes.Length; k++) AssertRing(fixture.Walkable.Holes[k], room.Walkable.Holes[k], "hole " + k);

            Assert.That(room.Objects.Length, Is.EqualTo(fixture.Objects.Length));
            for (int k = 0; k < fixture.Objects.Length; k++)
            {
                RoomObject want = fixture.Objects[k], got = room.Objects[k];
                Assert.That(got.Id, Is.EqualTo(want.Id));
                Assert.That(got.Category, Is.EqualTo(want.Category));
                Assert.That(math.distance(got.Base, want.Base), Is.LessThan(1e-5f));
                Assert.That(math.distance(got.Size, want.Size), Is.LessThan(1e-5f));
                Assert.That(got.Yaw, Is.EqualTo(want.Yaw).Within(1e-5f));
                Assert.That(got.Mesh, Is.EqualTo(MeshState.Placeholder));
                Assert.That(got.TriTarget, Is.EqualTo(want.TriTarget));
            }
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            Assert.That(RoomModelJson.GeometryHash(room), Is.EqualTo(RoomModelJson.GeometryHash(fixture)));
        }

        [TestCase(3f, 3f, 2.4f, 2.4f, 2.0f, 1.884526f)]
        [TestCase(4f, 5f, 3.4f, 4.4f, 3.0f, 2.826788f)]
        [TestCase(6f, 8f, 5.4f, 7.4f, 3.2f, 3.015241f)]
        public void EmptyRectangle_FollowsTheLoopFit(float w, float d, float wf, float df, float s, float l5)
        {
            RoomModel room = RoomModelBuilder.FromOutline("r", ScanTestRooms.Rectangle(w, d), null, new float2(0.5f * w, 0.5f * d));
            Assert.That(room, Is.Not.Null);
            Assert.That(room.FreeRect.Wf, Is.EqualTo(wf).Within(1e-5f));
            Assert.That(room.FreeRect.Df, Is.EqualTo(df).Within(1e-5f));
            Assert.That(room.LapSide, Is.EqualTo(s).Within(1e-5f));
            Assert.That(room.Curvature.L5, Is.EqualTo(l5).Within(1e-5f));
            Assert.That(room.Standing, Is.False);
            Assert.That(room.Quality.Route, Is.EqualTo(ScanRoute.ManualOutline));
            Assert.That(room.Walls.Length, Is.EqualTo(4));
            Assert.That(room.Walls[0].Height, Is.EqualTo(2.4f));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
            // The anchor is the centre of the free rectangle, in the frame the outline was given in.
            Assert.That(room.Anchor.Position.x, Is.EqualTo(0.5f * w).Within(1e-4f));
            Assert.That(room.Anchor.Position.z, Is.EqualTo(0.5f * d).Within(1e-4f));
        }

        [Test]
        public void SmallRoom_SelectsStandingMode()
        {
            RoomModel small = RoomModelBuilder.FromOutline("s", ScanTestRooms.Rectangle(2f, 2f), null, new float2(1f, 1f));
            Assert.That(small.FreeRect.Wf, Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(small.Standing, Is.True);
            Assert.That(small.LapSide, Is.EqualTo(1.0f).Within(1e-5f));

            // Exactly 1.5 x 1.5 m of free floor is a room to walk in.
            RoomModel enough = RoomModelBuilder.FromOutline("e", ScanTestRooms.Rectangle(2.1f, 3f), null, new float2(1f, 1.5f));
            Assert.That(enough.FreeRect.Wf, Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(enough.Standing, Is.False);

            // Long but narrow is standing too: the shorter side decides.
            RoomModel narrow = RoomModelBuilder.FromOutline("n", ScanTestRooms.Rectangle(2f, 6f), null, new float2(1f, 3f));
            Assert.That(narrow.Standing, Is.True);
        }

        [Test]
        public void RoomTooSmallToStandIn_GivesNoRoom()
        {
            Assert.That(RoomModelBuilder.FromOutline("x", ScanTestRooms.Rectangle(0.9f, 0.9f), null, new float2(0.45f, 0.45f)), Is.Null);
        }

        [Test]
        public void ClockwiseOutline_ComesOutCounterClockwise()
        {
            float2[] clockwise = ScanTestRooms.Rectangle(3f, 4f);
            Array.Reverse(clockwise);
            RoomModel room = RoomModelBuilder.FromOutline("c", clockwise, null, new float2(1.5f, 2f));
            Assert.That(WalkablePolygon.SignedArea(room.Outline), Is.GreaterThan(0f));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        [Test]
        public void TurnedRoom_KeepsItsNumbers_AndItsAnchorYaw()
        {
            float angle = math.radians(25f);
            float2[] outline = ScanTestRooms.Moved(ScanTestRooms.Rectangle(3f, 4f), angle, new float2(2f, -1f));
            RoomModel room = RoomModelBuilder.FromOutline("t", outline, null, ScanTestRooms.Moved(new float2(1.5f, 2f), angle, new float2(2f, -1f)));
            Assert.That(room.FreeRect.Wf, Is.EqualTo(2.4f).Within(0.051f));
            Assert.That(room.FreeRect.Df, Is.EqualTo(3.4f).Within(0.051f));
            Assert.That(room.Anchor.Yaw, Is.GreaterThan(-math.PI / 2f).And.LessThanOrEqualTo(math.PI / 2f));
            // The session's turn runs from +X to +Z, a yaw the other way.
            Assert.That(room.Anchor.Yaw, Is.EqualTo(-angle).Within(1e-3f));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);
        }

        static FloorGridResult GridWithBoxes(ObstacleBox[] boxes)
        {
            return new FloorGridResult { CellSize = 0.05f, Width = 400, Height = 400, OriginX = -10f, OriginZ = -10f, Boxes = boxes };
        }

        static ObstacleBox[] Boxes(int count)
        {
            // Volumes in no particular order, all different.
            var boxes = new ObstacleBox[count];
            for (int k = 0; k < count; k++)
            {
                float side = 0.3f + 0.01f * ((k * 37) % count);
                boxes[k] = new ObstacleBox { Base = new float3(-8f + 0.2f * k, 0f, 0f), Size = new float3(side, 0.5f, side), SourceId = 100 + k };
            }
            return boxes;
        }

        [TestCase(1, 5000)]
        [TestCase(12, 5000)]
        [TestCase(13, 4615)]
        [TestCase(20, 3000)]
        public void TriangleTargets_ShareTheBudget(int count, int target)
        {
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Boxes(count)));
            Assert.That(objects.Length, Is.EqualTo(count));
            foreach (RoomObject o in objects) Assert.That(o.TriTarget, Is.EqualTo(target));
        }

        [Test]
        public void Objects_AreLargestFirst_AndOnlyTwentyGetMeshes()
        {
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Boxes(27)));
            Assert.That(objects.Length, Is.EqualTo(27));
            for (int k = 0; k < objects.Length; k++)
            {
                Assert.That(objects[k].Id, Is.EqualTo(k + 1));
                if (k > 0) Assert.That(objects[k].Volume, Is.LessThanOrEqualTo(objects[k - 1].Volume));
                Assert.That(objects[k].TriTarget, Is.EqualTo(k < 20 ? 3000 : 0), "object " + (k + 1));
                Assert.That(objects[k].Mesh, Is.EqualTo(MeshState.Placeholder));
                Assert.That(objects[k].AssetHash, Is.EqualTo(""));
            }
        }

        [Test]
        public void Objects_AreCappedAtSixtyFour()
        {
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Boxes(80)));
            Assert.That(objects.Length, Is.EqualTo(RoomModel.MaxObjects));
            // The sixteen that were dropped are the smallest.
            float smallestKept = objects[objects.Length - 1].Volume;
            int larger = 0;
            foreach (ObstacleBox box in Boxes(80)) if (box.Size.x * box.Size.y * box.Size.z > smallestKept) larger++;
            Assert.That(larger, Is.LessThan(RoomModel.MaxObjects));
        }

        [Test]
        public void UnusableBoxes_AreLeftOut_AndABoxBelowTheFloorIsCut()
        {
            var boxes = new[]
            {
                new ObstacleBox { Base = new float3(0f, 0f, 0f), Size = new float3(1f, float.NaN, 1f) },
                new ObstacleBox { Base = new float3(0f, 0f, 0f), Size = new float3(1f, 0f, 1f) },
                new ObstacleBox { Base = new float3(50f, 0f, 0f), Size = new float3(1f, 1f, 1f) },          // beyond the grid
                new ObstacleBox { Base = new float3(1f, -0.2f, 1f), Size = new float3(1f, 1f, 1f), Category = 99, Yaw = 4f },
            };
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(boxes));
            Assert.That(objects.Length, Is.EqualTo(1));
            Assert.That(objects[0].Base.y, Is.EqualTo(0f));
            Assert.That(objects[0].Size.y, Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(objects[0].Category, Is.EqualTo(ObjectCategory.Unknown));
            Assert.That(objects[0].Yaw, Is.GreaterThan(-math.PI).And.LessThanOrEqualTo(math.PI));
        }

        [Test]
        public void RoomWithFurniture_ValidatesAndSurvivesItsFile()
        {
            var boxes = new[]
            {
                new ObstacleBox { Base = new float3(2f, 0f, 2.5f), Size = new float3(1f, 0.75f, 0.6f), Category = (int)ObjectCategory.Table },
                new ObstacleBox { Base = new float3(0.4f, 0f, 4.5f), Size = new float3(0.6f, 1.8f, 0.6f), Yaw = 0.3f, Category = (int)ObjectCategory.Storage },
                new ObstacleBox { Base = new float3(3.9f, 1.2f, 1f), Size = new float3(0.1f, 0.6f, 1f), Category = (int)ObjectCategory.Screen },
            };
            RoomModel room = RoomModelBuilder.FromOutline("furnished", ScanTestRooms.Rectangle(4f, 5f), boxes, new float2(2f, 1.1f),
                2.6f, ScanRoute.ObjectBoxes);
            Assert.That(room.Objects.Length, Is.EqualTo(3));
            Assert.That(room.Objects[0].Category, Is.EqualTo(ObjectCategory.Storage), "largest first");
            Assert.That(room.Walls[0].Height, Is.EqualTo(2.6f));
            Assert.That(RoomModelValidator.Errors(room), Is.Empty);

            string text = RoomModelJson.Write(room);
            RoomModel back = RoomModelJson.Read(text);
            Assert.That(RoomModelJson.Write(back), Is.EqualTo(text));
            Assert.That(RoomModelJson.GeometryHash(back), Is.EqualTo(RoomModelJson.GeometryHash(room)));
        }
    }
}
