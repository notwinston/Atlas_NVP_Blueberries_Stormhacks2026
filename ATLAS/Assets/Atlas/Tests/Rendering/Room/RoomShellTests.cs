using NUnit.Framework;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>The Home shell: floor, walls, openings, kerbs and the 3,000-triangle budget (Master Plan 4.5, 8.4).</summary>
    public class RoomShellTests
    {
        static readonly string[] Fixtures = RoomFixtures.All;

        [TestCaseSource(nameof(Fixtures))]
        public void Shell_IsWithinBudget_AtFullDetail(string name)
        {
            RoomModel room = RoomTestUtil.Fixture(name);
            var floor = new AtlasMeshBuilder();
            var walls = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, floor, out float pitch, out bool strict);
            RoomShellBuilder.Walls(room, floor.TriangleCount, walls, out float band);

            TestContext.WriteLine(name + ": floor " + floor.TriangleCount + ", walls " + walls.TriangleCount + ", band " + band);
            Assert.That(floor.TriangleCount + walls.TriangleCount, Is.LessThanOrEqualTo(RoomShellBuilder.MaxTriangles));
            Assert.That(floor.TriangleCount, Is.GreaterThan(0));
            Assert.That(walls.TriangleCount, Is.EqualTo(RoomShellBuilder.WallTriangles(room, band)));
            // No fixture is large enough to need the budget rule.
            Assert.That(pitch, Is.EqualTo(0.25f));
            Assert.That(strict, Is.True);
            Assert.That(band, Is.EqualTo(0.25f));
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Floor_HasNoEdgeOverQuarterMetre_AndCoversTheOutline(string name)
        {
            RoomModel room = RoomTestUtil.Fixture(name);
            var floor = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, floor);

            Assert.That(floor.LongestEdge(), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            double outline = RoomFloorMesher.Area(room.Outline, null);
            Assert.That(RoomTestUtil.Area(floor), Is.EqualTo(outline).Within(1e-4));
            RoomTestUtil.AssertWindingAgreesWithNormals(floor);
            Assert.That(RoomTestUtil.CountTJunctions(floor), Is.EqualTo(0));

            float2 uv = AtlasPalette.Uv(AtlasPalette.Floor);
            for (int i = 0; i < floor.VertexCount; i++)
            {
                Assert.That(floor.Vertices[i].Position.y, Is.EqualTo(0f));
                Assert.That(floor.Vertices[i].Normal.y, Is.EqualTo(1f));
                Assert.That(math.distance(floor.Vertices[i].Uv0, uv), Is.LessThan(1e-6f));
                Assert.That(math.distance(floor.Vertices[i].Uv1, floor.Vertices[i].Position.xz / 0.5f), Is.LessThan(1e-5f));
                Assert.That(floor.Vertices[i].LineWeight, Is.EqualTo(255));
            }
            for (int t = 0; t < floor.TriangleCount; t++)
                Assert.That(WalkablePolygon.RingContains(room.Outline, RoomTestUtil.Centroid(floor, t).xz), Is.True, "triangle " + t);
            // The outline's own corners are vertices of the floor, exactly.
            for (int k = 0; k < room.Outline.Length; k++)
            {
                bool found = false;
                for (int i = 0; i < floor.VertexCount && !found; i++) found = floor.Vertices[i].Position.xz.Equals(room.Outline[k]);
                Assert.That(found, Is.True, "outline corner " + k);
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void WalkableFloor_MatchesThePolygonWithHoles(string name)
        {
            RoomModel room = RoomTestUtil.Fixture(name);
            var floor = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room.Walkable, floor);

            double area = RoomFloorMesher.Area(room.Walkable.Outer, room.Walkable.Holes);
            Assert.That(area, Is.GreaterThan(0.5));
            Assert.That(RoomTestUtil.Area(floor), Is.EqualTo(area).Within(0.01 * area));
            Assert.That(RoomTestUtil.Area(floor), Is.EqualTo(area).Within(1e-4));
            Assert.That(floor.LongestEdge(), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            Assert.That(floor.TriangleCount, Is.LessThanOrEqualTo(RoomShellBuilder.MaxTriangles));
            RoomTestUtil.AssertWindingAgreesWithNormals(floor);
            Assert.That(RoomTestUtil.CountTJunctions(floor), Is.EqualTo(0));
            for (int t = 0; t < floor.TriangleCount; t++)
                Assert.That(room.Walkable.Contains(RoomTestUtil.Centroid(floor, t).xz), Is.True, "triangle " + t);
        }

        [Test]
        public void Floor_OfAnOffGridPolygonWithAHole_IsExactAndCrackFree()
        {
            // Nothing here lies on the grid, the outer ring is concave and clockwise, the hole is a slanted triangle.
            var polygon = new WalkablePolygon
            {
                Outer = new[]
                {
                    new float2(-1.37f, -0.91f), new float2(-1.62f, 1.13f), new float2(0.11f, 0.42f),
                    new float2(1.83f, 1.48f), new float2(2.21f, -0.63f), new float2(0.43f, -1.29f)
                },
                Holes = new[] { new[] { new float2(-0.52f, -0.33f), new float2(0.37f, -0.48f), new float2(-0.08f, 0.12f) } }
            };
            var floor = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(polygon, floor);

            double area = RoomFloorMesher.Area(polygon.Outer, polygon.Holes);
            Assert.That(RoomTestUtil.Area(floor), Is.EqualTo(area).Within(1e-4));
            Assert.That(floor.LongestEdge(), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            Assert.That(RoomTestUtil.CountTJunctions(floor), Is.EqualTo(0));
            RoomTestUtil.AssertWindingAgreesWithNormals(floor);
            for (int t = 0; t < floor.TriangleCount; t++)
                Assert.That(polygon.Contains(RoomTestUtil.Centroid(floor, t).xz), Is.True, "triangle " + t);
        }

        [Test]
        public void Floor_IsDarkerAlongTheWalls()
        {
            RoomModel room = RoomTestUtil.Fixture(RoomFixtures.Sample3x3);
            var floor = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, floor);
            int atWall = -1, inside = -1;
            for (int i = 0; i < floor.VertexCount; i++)
            {
                float2 p = floor.Vertices[i].Position.xz;
                if (math.abs(p.x + 1.5f) < 1e-5f && math.abs(p.y) < 1e-5f) atWall = i;
                if (math.abs(p.x) < 1e-5f && math.abs(p.y) < 1e-5f) inside = i;
            }
            Assert.That(atWall >= 0 && inside >= 0, Is.True);
            Assert.That(floor.Vertices[atWall].Shade / (float)floor.Vertices[inside].Shade, Is.EqualTo(0.6f).Within(0.01f));
            Assert.That(RoomShellBuilder.Occlusion(0f), Is.EqualTo(0.6f));
            Assert.That(RoomShellBuilder.Occlusion(0.125f), Is.EqualTo(0.8f).Within(1e-6f));
            Assert.That(RoomShellBuilder.Occlusion(3f), Is.EqualTo(1f));
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Walls_StandOnTheWallSegments_FaceTheRoom_AndLeaveOpeningsOpen(string name)
        {
            RoomModel room = RoomTestUtil.Fixture(name);
            var walls = new AtlasMeshBuilder();
            RoomShellBuilder.Walls(room, 0, walls, out float band);
            Assert.That(band, Is.EqualTo(0.25f));

            RoomTestUtil.AssertWindingAgreesWithNormals(walls);
            Assert.That(RoomShellBuilder.LongestFloorPlaneEdge(walls), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            Assert.That(walls.LongestSide(), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));

            double expected = 0.0;
            for (int w = 0; w < room.Walls.Length; w++) expected += RoomTestUtil.WallArea(room.Walls[w]);
            Assert.That(RoomTestUtil.Area(walls), Is.EqualTo(expected).Within(1e-3));

            float2 uv = AtlasPalette.Uv(AtlasPalette.Wall);
            for (int i = 0; i < walls.VertexCount; i++)
            {
                float3 p = walls.Vertices[i].Position;
                float nearest = float.MaxValue;
                for (int w = 0; w < room.Walls.Length; w++)
                    nearest = math.min(nearest, RoomTestUtil.DistanceToWall(room.Walls[w], p.xz, out _));
                Assert.That(nearest, Is.LessThan(1e-4f), "vertex " + i + " is off every wall");
                Assert.That(p.y, Is.InRange(0f, room.MaxWallHeight + 1e-5f));
                Assert.That(math.distance(walls.Vertices[i].Uv0, uv), Is.LessThan(1e-6f));
                Assert.That(walls.Vertices[i].LineWeight, Is.EqualTo(0));
            }

            for (int t = 0; t < walls.TriangleCount; t++)
            {
                float3 c = RoomTestUtil.Centroid(walls, t);
                float3 n = walls.Vertices[walls.Indices[t * 3]].Normal;
                Assert.That(n.y, Is.EqualTo(0f));
                // A step along the normal from a wall lands inside the room.
                Assert.That(WalkablePolygon.RingContains(room.Outline, c.xz + 0.05f * n.xz), Is.True, "triangle " + t + " faces out");
                for (int w = 0; w < room.Walls.Length; w++)
                {
                    RoomWall wall = room.Walls[w];
                    if (RoomTestUtil.DistanceToWall(wall, c.xz, out float along) > 1e-3f) continue;
                    for (int k = 0; k < wall.Openings.Length; k++)
                    {
                        WallOpening o = wall.Openings[k];
                        bool inOpening = along > o.Offset + 1e-3f && along < o.Offset + o.Width - 1e-3f
                                         && c.y > o.Bottom + 1e-3f && c.y < o.Top - 1e-3f;
                        Assert.That(inOpening, Is.False, "triangle " + t + " closes an opening of wall " + w);
                    }
                }
            }

            // The wall ends are the scanned points themselves, at the floor and at the top.
            for (int w = 0; w < room.Walls.Length; w++)
            {
                bool a = false, b = false;
                for (int i = 0; i < walls.VertexCount; i++)
                {
                    float3 p = walls.Vertices[i].Position;
                    if (p.y != 0f) continue;
                    a |= p.xz.Equals(room.Walls[w].A);
                    b |= p.xz.Equals(room.Walls[w].B);
                }
                Assert.That(a && b, Is.True, "wall " + w);
            }
        }

        [Test]
        public void Walls_AreDarkerAtTheFloor()
        {
            RoomModel room = RoomTestUtil.Rectangle(3f, 3f);
            var walls = new AtlasMeshBuilder();
            RoomShellBuilder.Walls(room, 0, walls, out _);
            float3 normal = walls.Vertices[0].Normal;
            int foot = -1, top = -1;
            for (int i = 0; i < walls.VertexCount; i++)
            {
                if (!walls.Vertices[i].Normal.Equals(normal)) continue;
                if (walls.Vertices[i].Position.y == 0f) foot = i;
                if (walls.Vertices[i].Position.y == 2.4f) top = i;
            }
            Assert.That(walls.Vertices[foot].Shade / (float)walls.Vertices[top].Shade, Is.EqualTo(0.6f).Within(0.01f));
        }

        [Test]
        public void LargeRooms_CoarsenInTheStatedOrder_AndStayInBudget()
        {
            // 6 x 8 m: the strict floor alone would be 3,072, so cells become two triangles and wall rows double once.
            Check(RoomTestUtil.Rectangle(6f, 8f), 0.25f, false, 0.5f, 1536);
            // 9 x 9 m, the largest room RoomPlan handles.
            Check(RoomTestUtil.Rectangle(9f, 9f), 0.25f, false, 4f, 2592);
            // Larger still: the floor's pitch doubles.
            Check(RoomTestUtil.Rectangle(14f, 14f), 0.5f, false, 1f, 1568);
            // 5 x 5 m: the strict floor fits, the wall rows double.
            Check(RoomTestUtil.Rectangle(5f, 5f), 0.25f, true, 0.5f, 1600);
        }

        static void Check(RoomModel room, float expectedPitch, bool expectedStrict, float expectedBand, int expectedFloor)
        {
            var floor = new AtlasMeshBuilder();
            var walls = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, floor, out float pitch, out bool strict);
            RoomShellBuilder.Walls(room, floor.TriangleCount, walls, out float band);
            TestContext.WriteLine(room.Outline[2] * 2f + ": floor " + floor.TriangleCount + " pitch " + pitch + " strict " + strict
                                  + ", walls " + walls.TriangleCount + " band " + band);
            Assert.That(floor.TriangleCount + walls.TriangleCount, Is.LessThanOrEqualTo(RoomShellBuilder.MaxTriangles));
            Assert.That(pitch, Is.EqualTo(expectedPitch));
            Assert.That(strict, Is.EqualTo(expectedStrict));
            Assert.That(band, Is.EqualTo(expectedBand));
            Assert.That(floor.TriangleCount, Is.EqualTo(expectedFloor));
            Assert.That(RoomTestUtil.Area(floor), Is.EqualTo(RoomFloorMesher.Area(room.Outline, null)).Within(1e-3));
            // Columns stay 0.25 m whatever the rows do.
            Assert.That(RoomShellBuilder.LongestFloorPlaneEdge(walls), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            RoomTestUtil.AssertWindingAgreesWithNormals(walls);
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Kerbs_AreLow_OpenAtDoors_AndCarryCornerPosts(string name)
        {
            RoomModel room = RoomTestUtil.Fixture(name);
            const float height = 0.10f;
            var kerbs = new AtlasMeshBuilder();
            var bare = new AtlasMeshBuilder();
            RoomShellBuilder.Kerbs(room, height, kerbs);
            RoomShellBuilder.Kerbs(room, height, bare, false);

            RoomTestUtil.AssertWindingAgreesWithNormals(kerbs);
            Assert.That(RoomShellBuilder.LongestFloorPlaneEdge(kerbs), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
            Assert.That(kerbs.TriangleCount, Is.EqualTo(RoomShellBuilder.KerbTriangles(room)));
            Assert.That(kerbs.TriangleCount, Is.LessThan(RoomShellBuilder.MaxTriangles));

            // Without posts: six triangles per column, nothing higher than the kerb, nothing further out than its thickness.
            Assert.That(bare.TriangleCount % 6, Is.EqualTo(0));
            Assert.That(bare.BoundsMax.y, Is.EqualTo(height).Within(1e-6f));
            float2 kerbUv = AtlasPalette.Uv(AtlasPalette.Kerb);
            for (int i = 0; i < bare.VertexCount; i++)
            {
                float2 p = bare.Vertices[i].Position.xz;
                float nearest = float.MaxValue;
                for (int w = 0; w < room.Walls.Length; w++)
                    nearest = math.min(nearest, RoomTestUtil.DistanceToWall(room.Walls[w], p, out _));
                Assert.That(nearest, Is.LessThan(RoomShellBuilder.KerbThickness * 1.4143f + 1e-4f));
                Assert.That(math.distance(bare.Vertices[i].Uv0, kerbUv), Is.LessThan(1e-6f));
            }

            // Doors and plain openings are left open; a window is not.
            int columns = 0;
            for (int w = 0; w < room.Walls.Length; w++)
            {
                RoomWall wall = room.Walls[w];
                float closed = wall.Length;
                for (int k = 0; k < wall.Openings.Length; k++)
                {
                    WallOpening o = wall.Openings[k];
                    if (o.Kind == OpeningKind.Window) continue;
                    closed -= o.Width;
                    for (int t = 0; t < bare.TriangleCount; t++)
                    {
                        float3 c = RoomTestUtil.Centroid(bare, t);
                        if (RoomTestUtil.DistanceToWall(wall, c.xz, out float along) > RoomShellBuilder.KerbThickness + 1e-3f) continue;
                        bool inDoor = along > o.Offset + 1e-3f && along < o.Offset + o.Width - 1e-3f;
                        Assert.That(inDoor, Is.False, "kerb across the door of wall " + w);
                    }
                }
                columns += (int)math.ceil(closed / RoomShellBuilder.KerbColumn - 1e-3f);
            }
            Assert.That(RoomTestUtil.Area(bare), Is.GreaterThan(0.0));
            Assert.That(bare.TriangleCount, Is.GreaterThanOrEqualTo(6 * columns));

            // One post at every corner of the outline, 0.9 m high.
            int corners = room.Outline.Length;
            var post = new AtlasMeshBuilder();
            post.AddPost(float3.zero, RoomShellBuilder.PostRadius, RoomShellBuilder.PostHeight,
                         AtlasPalette.Uv(AtlasPalette.Post), RoomShellBuilder.PostSides);
            Assert.That(kerbs.TriangleCount - bare.TriangleCount, Is.EqualTo(corners * post.TriangleCount));
            Assert.That(kerbs.BoundsMax.y, Is.EqualTo(RoomShellBuilder.PostHeight).Within(1e-6f));
        }

        [Test]
        public void Kerbs_KeepTheirTriangles_WhenTheHeightChanges()
        {
            RoomModel room = RoomTestUtil.Fixture(RoomFixtures.BudgetRoom);
            var low = new AtlasMeshBuilder();
            var high = new AtlasMeshBuilder();
            RoomShellBuilder.Kerbs(room, 0.05f, low, false);
            RoomShellBuilder.Kerbs(room, 5f, high, false);          // clamped to 0.9
            Assert.That(high.TriangleCount, Is.EqualTo(low.TriangleCount));
            Assert.That(low.BoundsMax.y, Is.EqualTo(0.05f).Within(1e-6f));
            Assert.That(high.BoundsMax.y, Is.EqualTo(0.9f).Within(1e-6f));
        }

        [Test]
        public void Shell_IsTheSameEveryTime()
        {
            RoomModel room = RoomTestUtil.Fixture(RoomFixtures.SampleL3x4);
            var a = new AtlasMeshBuilder();
            var b = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, a);
            RoomShellBuilder.Walls(room, a.TriangleCount, a, out _);
            RoomShellBuilder.Floor(room, b);
            RoomShellBuilder.Walls(room, b.TriangleCount, b, out _);
            Assert.That(b.VertexCount, Is.EqualTo(a.VertexCount));
            Assert.That(b.IndexCount, Is.EqualTo(a.IndexCount));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That(b.Vertices[i].Position.Equals(a.Vertices[i].Position), Is.True);
                Assert.That(b.Vertices[i].Color, Is.EqualTo(a.Vertices[i].Color));
            }
            for (int i = 0; i < a.IndexCount; i++) Assert.That(b.Indices[i], Is.EqualTo(a.Indices[i]));
        }
    }
}
