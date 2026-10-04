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

        // One box per size, side by side on the floor.
        static ObstacleBox[] Sized(params float3[] sizes)
        {
            var boxes = new ObstacleBox[sizes.Length];
            for (int k = 0; k < sizes.Length; k++)
                boxes[k] = new ObstacleBox { Base = new float3(-8f + 0.2f * k, 0f, 0f), Size = sizes[k], SourceId = 100 + k };
            return boxes;
        }

        static float3[] Repeat(float3 size, int count)
        {
            var sizes = new float3[count];
            for (int k = 0; k < count; k++) sizes[k] = size;
            return sizes;
        }

        // Every object wider than widerThan has the target wide, every other one the target narrow.
        static void AssertTargets(string what, float3[] sizes, float widerThan, int wide, int narrow)
        {
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Sized(sizes)));
            Assert.That(objects.Length, Is.EqualTo(sizes.Length), what);
            foreach (RoomObject o in objects)
                Assert.That(o.TriTarget, Is.EqualTo(o.Size.x > widerThan ? wide : narrow), what + ", object " + o.Id + " of width " + o.Size.x);
        }

        // The rule of mesh design section 5.4, in whole millimetres. The objects that receive meshes (the 32
        // largest by volume) share 120,000 triangles by surface area a = 2 (wh + hd + wd), A their sum:
        //     t = clamp(floor(120000 a / A), 3000, 15000)
        //     when the sum of t is above 120000:  t = 3000 + floor((t - 3000) (120000 - 3000 n) / sum of (t - 3000))
        // The cases are the vectors of the mesh brief (S5) and one worked out here.
        [Test]
        public void TriangleTargets_ShareTheBudget()
        {
            // One object has all the surface: 120000, cut to the most an object may have.
            AssertTargets("one object", Repeat(new float3(0.6f, 0.45f, 0.6f), 1), 0f, 15000, 15000);
            // Five equal ones: 120000 / 5 = 24000 each, cut to 15000.
            AssertTargets("five cubes", Repeat(new float3(1f, 1f, 1f), 5), 0f, 15000, 15000);
            // Three unequal ones, a = 8.24, 2.3 and 4.14 m2 of A = 14.68: 67356, 18801 and 33841, each cut to 15000.
            AssertTargets("three unequal", new[] { new float3(2f, 0.8f, 0.9f), new float3(0.5f, 0.9f, 0.5f), new float3(1.2f, 0.75f, 0.6f) },
                0f, 15000, 15000);
            // Twelve equal ones: 120000 / 12.
            AssertTargets("twelve equal", Repeat(new float3(1.8f, 0.8f, 0.9f), 12), 0f, 10000, 10000);

            // Five cubes of 1 m (a = 6 m2) and five of 0.5 m (a = 1.5 m2), A = 37.5 m2:
            // 120000 * 6 / 37.5 = 19200, cut to 15000, and 120000 * 1.5 / 37.5 = 4800. The sum is 99000: no second step.
            var mixed = new float3[10];
            for (int k = 0; k < 10; k++) mixed[k] = k % 2 == 0 ? new float3(1f, 1f, 1f) : new float3(0.5f, 0.5f, 0.5f);
            AssertTargets("five large and five small", mixed, 0.75f, 15000, 4800);

            // Eight of 1.5 x 1 x 1 (a = 8 m2) and 24 cubes of 0.2 m (a = 0.24 m2), A = 69.76 m2:
            // floor(120000 * 8 / 69.76) = 13761, and floor(120000 * 0.24 / 69.76) = 412, raised to 3000.
            // The sum 8 * 13761 + 24 * 3000 = 182088 is over, so with 8 * 10761 = 86088 above the floor and
            // 120000 - 32 * 3000 = 24000 to share: 3000 + floor(10761 * 24000 / 86088) = 6000; the small ones stay at 3000.
            var crowded = new float3[32];
            for (int k = 0; k < 32; k++) crowded[k] = k % 4 == 0 ? new float3(1.5f, 1f, 1f) : new float3(0.2f, 0.2f, 0.2f);
            AssertTargets("eight large and twenty-four small", crowded, 0.75f, 6000, 3000);
        }

        [Test]
        public void Objects_AreLargestFirst_AndOnlyThirtyTwoGetMeshes()
        {
            // The fourth vector of the mesh brief (S5): forty boxes, box i of 0.3 + 0.05 i by 0.5 by 0.4 m, handed
            // over in no particular order. The eight smallest get no mesh; the 32 others share the budget by
            // surface area (a = 1.8 w + 0.4 m2, A = 100.64 m2), which passes 120,000 once the small ones are
            // raised to 3000, so the second step of the rule spreads what is left: 119,990 in all.
            int[] expected =
            {
                5043, 4952, 4861, 4770, 4679, 4588, 4497, 4407, 4315, 4225, 4134, 4043, 3952, 3861, 3770, 3679,
                3589, 3498, 3407, 3316, 3226, 3134, 3044, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000, 3000,
                0, 0, 0, 0, 0, 0, 0, 0,
            };
            var sizes = new float3[40];
            for (int k = 0; k < 40; k++) sizes[k] = new float3(0.3f + 0.05f * (1 + (k * 37) % 40), 0.5f, 0.4f);

            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Sized(sizes)));
            Assert.That(objects.Length, Is.EqualTo(40));
            int total = 0;
            for (int k = 0; k < objects.Length; k++)
            {
                Assert.That(objects[k].Id, Is.EqualTo(k + 1));
                if (k > 0) Assert.That(objects[k].Volume, Is.LessThan(objects[k - 1].Volume));
                Assert.That(objects[k].Size.x, Is.EqualTo(0.3f + 0.05f * (40 - k)).Within(1e-5f), "object " + (k + 1));
                Assert.That(objects[k].TriTarget, Is.EqualTo(expected[k]), "object " + (k + 1));
                Assert.That(objects[k].Mesh, Is.EqualTo(MeshState.Placeholder));
                Assert.That(objects[k].AssetHash, Is.EqualTo(""));
                Assert.That(objects[k].Profile, Is.Null);
                Assert.That(objects[k].Facing, Is.EqualTo(ObjectFacingRule.Box));
                total += objects[k].TriTarget;
            }
            Assert.That(total, Is.EqualTo(119990));
        }

        [Test]
        public void Objects_AreCappedAtSixtyFour()
        {
            RoomObject[] objects = RoomModelBuilder.Objects(GridWithBoxes(Boxes(80)));
            Assert.That(objects.Length, Is.EqualTo(RoomModel.MaxObjects));
            // The sixteen that were dropped are the smallest. A kept object's size is rounded to six decimals, so its
            // volume differs from the box's own by float noise and rounding: the side is at most 1.1 m, so one side
            // moves by at most 5e-7 and the volume 0.5 * side * side by at most about 6e-7; with the float noise of
            // the three products (Mono and .NET order and widen them differently) that stays under 1e-5. Two
            // different boxes differ by at least 0.5 * (2 * 0.3 * 0.01 + 0.01 * 0.01) = 0.00305. A margin of 1e-4
            // therefore counts the boxes that are really larger, and exactly the 63 larger than the smallest kept
            // one remain.
            float smallestKept = objects[objects.Length - 1].Volume;
            int larger = 0;
            foreach (ObstacleBox box in Boxes(80)) if (box.Size.x * box.Size.y * box.Size.z > smallestKept + 1e-4f) larger++;
            Assert.That(larger, Is.EqualTo(RoomModel.MaxObjects - 1));
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
