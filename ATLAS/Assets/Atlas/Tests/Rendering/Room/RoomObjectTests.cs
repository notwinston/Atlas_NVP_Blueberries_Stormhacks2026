using NUnit.Framework;
using Atlas.Rendering.Room;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Rendering.Tests.Room
{
    /// <summary>Stand-in meshes, placeholder boxes and the room's draw accounting (Master Plan 4.5, 8.4, 8.5).</summary>
    public class RoomObjectTests
    {
        // Longest edge of each stand-in of budget_room, metres (room contract, section 5.3).
        static readonly float[] BudgetLongestEdge =
        {
            0.0935f, 0.0788f, 0.1156f, 0.0447f, 0.0879f, 0.0419f, 0.0637f, 0.0654f, 0.0913f, 0.0499f, 0.0419f, 0.0337f
        };

        [Test]
        public void StandIns_OfBudgetRoom_HaveExactly5000Triangles_AndNoLongEdge()
        {
            RoomModel room = RoomTestUtil.Fixture(RoomFixtures.BudgetRoom);
            Assert.That(room.Objects.Length, Is.EqualTo(12));
            var mesh = new AtlasMeshBuilder();
            for (int k = 0; k < room.Objects.Length; k++)
            {
                RoomObject obj = room.Objects[k];
                Assert.That(obj.Mesh, Is.EqualTo(MeshState.StandIn));
                mesh.Clear();
                RoomObjectMeshes.Initial(obj, mesh);

                Assert.That(mesh.TriangleCount, Is.EqualTo(5000), "object " + obj.Id);
                Assert.That(mesh.VertexCount, Is.EqualTo(2502), "object " + obj.Id);
                Assert.That(mesh.LongestEdge(), Is.LessThanOrEqualTo(0.25f), "object " + obj.Id);
                Assert.That(mesh.LongestEdge(), Is.EqualTo(BudgetLongestEdge[k]).Within(1e-3f), "object " + obj.Id);
                RoomTestUtil.AssertWindingAgreesWithNormals(mesh);

                // Inscribed in the box, standing on its base centre.
                float3 half = 0.5f * obj.Size;
                Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(mesh.BoundsMax.y, Is.EqualTo(obj.Size.y).Within(1e-5f));
                Assert.That(mesh.BoundsMax.x, Is.EqualTo(half.x).Within(2e-3f * obj.Size.x));
                Assert.That(mesh.BoundsMin.x, Is.EqualTo(-half.x).Within(2e-3f * obj.Size.x));
                Assert.That(mesh.BoundsMax.z, Is.LessThanOrEqualTo(half.z + 1e-6f));
                Assert.That(mesh.BoundsMax.z, Is.GreaterThan(half.z * 0.99f));
                float2 uv = RoomObjectMeshes.CategoryUv(obj.Category);
                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    Assert.That(mesh.Vertices[i].Uv0.Equals(uv), Is.True);
                    Assert.That(mesh.Vertices[i].LineWeight, Is.EqualTo(0));
                    Assert.That(math.length(mesh.Vertices[i].Normal), Is.EqualTo(1f).Within(1e-4f));
                }
            }
        }

        [Test]
        public void StandIn_IsClosed_Deterministic_AndWithinTheEdgeRuleUpTo3p5Metres()
        {
            var a = new AtlasMeshBuilder();
            var b = new AtlasMeshBuilder();
            var size = new float3(1.2f, 1.8f, 0.5f);
            StandInMeshBuilder.Build(size, ObjectCategory.Storage, a);
            StandInMeshBuilder.Build(size, ObjectCategory.Storage, b);
            Assert.That(StandInMeshBuilder.Triangles, Is.EqualTo(5000));
            Assert.That(StandInMeshBuilder.Vertices, Is.EqualTo(2502));
            for (int i = 0; i < a.VertexCount; i++)
            {
                Assert.That(b.Vertices[i].Position.Equals(a.Vertices[i].Position), Is.True);
                Assert.That(b.Vertices[i].Color, Is.EqualTo(a.Vertices[i].Color));
            }
            for (int i = 0; i < a.IndexCount; i++) Assert.That(b.Indices[i], Is.EqualTo(a.Indices[i]));

            // Closed: every directed edge has its opposite exactly once.
            var edges = new System.Collections.Generic.HashSet<long>();
            for (int t = 0; t < a.IndexCount; t += 3)
                for (int e = 0; e < 3; e++)
                    Assert.That(edges.Add(((long)a.Indices[t + e] << 32) | (uint)a.Indices[t + (e + 1) % 3]), Is.True);
            foreach (long edge in edges)
                Assert.That(edges.Contains(((edge & 0xFFFFFFFFL) << 32) | (uint)(edge >> 32)), Is.True);

            // Appending keeps what was there.
            StandInMeshBuilder.Build(size, ObjectCategory.Couch, a);
            Assert.That(a.TriangleCount, Is.EqualTo(10000));
            RoomTestUtil.AssertWindingAgreesWithNormals(a);

            var large = new AtlasMeshBuilder();
            StandInMeshBuilder.Build(new float3(StandInMeshBuilder.MaxBoxSide), ObjectCategory.Other, large);
            Assert.That(large.TriangleCount, Is.EqualTo(5000));
            Assert.That(large.LongestEdge(), Is.LessThanOrEqualTo(0.25f));
        }

        [Test]
        public void PlaceholderBoxes_MatchTheOrientedBoxes()
        {
            int checkedObjects = 0;
            for (int f = 0; f < RoomFixtures.All.Length; f++)
            {
                RoomModel room = RoomTestUtil.Fixture(RoomFixtures.All[f]);
                var mesh = new AtlasMeshBuilder();
                for (int k = 0; k < room.Objects.Length; k++)
                {
                    RoomObject obj = room.Objects[k];
                    mesh.Clear();
                    RoomObjectMeshes.Placeholder(obj, mesh);
                    checkedObjects++;

                    // Local frame: the box itself, standing on the centre of its base.
                    float3 half = 0.5f * obj.Size;
                    Assert.That(math.cmax(math.abs(mesh.BoundsMin - new float3(-half.x, 0f, -half.z))), Is.LessThan(1e-5f));
                    Assert.That(math.cmax(math.abs(mesh.BoundsMax - new float3(half.x, obj.Size.y, half.z))), Is.LessThan(1e-5f));
                    Assert.That(mesh.TriangleCount, Is.EqualTo(RoomObjectMeshes.PlaceholderTriangles(obj)));
                    Assert.That(mesh.LongestSide(), Is.LessThanOrEqualTo(RoomTestUtil.EdgeLimit));
                    RoomTestUtil.AssertWindingAgreesWithNormals(mesh);

                    bool hasBottom = false;
                    float2 uv = RoomObjectMeshes.CategoryUv(obj.Category);
                    for (int i = 0; i < mesh.VertexCount; i++)
                    {
                        hasBottom |= mesh.Vertices[i].Normal.y < -0.5f;
                        Assert.That(mesh.Vertices[i].Uv0.Equals(uv), Is.True);
                    }
                    Assert.That(hasBottom, Is.EqualTo(obj.Base.y >= 0.01f), "five faces on the floor, six above it");

                    // Room frame: the eight corners land on the oriented box's corners.
                    quaternion turn = quaternion.RotateY(obj.Yaw);
                    for (int c = 0; c < 8; c++)
                    {
                        var local = new float3((c & 1) == 0 ? -half.x : half.x, (c & 2) == 0 ? 0f : obj.Size.y, (c & 4) == 0 ? -half.z : half.z);
                        float3 expected = obj.Base + math.mul(turn, local);
                        Assert.That(math.distance(RoomObjectMeshes.ToRoom(obj, local), expected), Is.LessThan(1e-5f));
                        bool found = false;
                        for (int i = 0; i < mesh.VertexCount && !found; i++)
                            found = math.distance(RoomObjectMeshes.ToRoom(obj, mesh.Vertices[i].Position), expected) < 1e-4f;
                        Assert.That(found, Is.True, "corner " + c + " of object " + obj.Id);
                    }
                }
            }
            Assert.That(checkedObjects, Is.EqualTo(13));
        }

        [Test]
        public void PlaceholderTint_IsTheCategoryCell()
        {
            Assert.That(RoomObjectMeshes.CategoryUv(ObjectCategory.Table).Equals(AtlasPalette.Uv(AtlasPalette.CategoryBase + 2)), Is.True);
            Assert.That(RoomObjectMeshes.CategoryUv(ObjectCategory.Other).Equals(AtlasPalette.Uv(AtlasPalette.CategoryBase + 22)), Is.True);
            var table = new RoomObject { Id = 1, Category = ObjectCategory.Table, Size = new float3(1f, 0.75f, 0.6f) };
            // 4 x 3 x 3 divisions, no bottom: 2 * (12 + 2 * 12 + 2 * 9).
            Assert.That(RoomObjectMeshes.PlaceholderTriangles(table), Is.EqualTo(108));
            Assert.That(RoomObjectMeshes.Triangles(table), Is.EqualTo(108));
            table.Mesh = MeshState.StandIn;
            Assert.That(RoomObjectMeshes.Triangles(table), Is.EqualTo(5000));
            table.Mesh = MeshState.Ready;
            table.TriTarget = 4615;
            Assert.That(RoomObjectMeshes.Triangles(table), Is.EqualTo(4615));
            table.Mesh = MeshState.BuiltIn;
            Assert.That(RoomObjectMeshes.Triangles(table), Is.EqualTo(1500));
        }

        [Test]
        public void Home_OnBudgetRoom_IsInsideItsBudget()
        {
            RoomModel room = RoomTestUtil.Fixture(RoomFixtures.BudgetRoom);
            RoomDrawCost home = RoomBudget.Estimate(room);
            TestContext.WriteLine("Home on budget_room: " + home.DrawCallsPerEye + " draw calls and " + home.TrianglesPerEye
                                  + " triangles per eye (shell " + home.ShellTriangles + "); per frame "
                                  + home.DrawCallsPerFrame + " and " + home.TrianglesPerFrame);

            Assert.That(home.DrawCallsPerEye, Is.EqualTo(14));           // floor, walls, twelve objects
            Assert.That(home.DrawCallsPerFrame, Is.EqualTo(28));
            Assert.That(home.ObjectTriangles, Is.EqualTo(60000));
            Assert.That(home.ShellTriangles, Is.EqualTo(1280 + 1396));    // floor: 320 cells of four; walls as the contract's reference
            Assert.That(home.TrianglesPerFrame, Is.EqualTo(125352));
            Assert.That(home.TrianglesPerEye, Is.EqualTo(home.ShellTriangles + 60000));
            Assert.That(home.DrawCallsPerFrame, Is.LessThanOrEqualTo(80));
            Assert.That(home.TrianglesPerFrame, Is.LessThanOrEqualTo(250000));
            Assert.That(RoomBudget.FitsHome(home), Is.True);

            // The shell in the estimate is the shell that is built.
            var floor = new AtlasMeshBuilder();
            var walls = new AtlasMeshBuilder();
            RoomShellBuilder.Floor(room, floor);
            RoomShellBuilder.Walls(room, floor.TriangleCount, walls, out _);
            Assert.That(home.ShellTriangles, Is.EqualTo(floor.TriangleCount + walls.TriangleCount));

            RoomDrawCost kerbs = RoomBudget.Estimate(room, WallMode.Kerbs, false);
            Assert.That(kerbs.DrawCallsPerEye, Is.EqualTo(13));
            Assert.That(kerbs.ShellTriangles, Is.EqualTo(750));                 // 77 kerb columns of six, four posts of 72
            Assert.That(kerbs.ShellTriangles, Is.EqualTo(RoomShellBuilder.KerbTriangles(room)));
            RoomDrawCost bare = RoomBudget.Estimate(room, WallMode.Hidden, false, false);
            Assert.That(bare.DrawCallsPerEye, Is.EqualTo(0));
            Assert.That(bare.TrianglesPerEye, Is.EqualTo(0));
        }

        [Test]
        public void EveryFixture_FitsHome()
        {
            for (int f = 0; f < RoomFixtures.All.Length; f++)
            {
                RoomModel room = RoomTestUtil.Fixture(RoomFixtures.All[f]);
                RoomDrawCost cost = RoomBudget.Estimate(room);
                Assert.That(RoomBudget.FitsHome(cost), Is.True, RoomFixtures.All[f]);
                Assert.That(cost.DrawCallsPerEye, Is.EqualTo(2 + room.Objects.Length));
            }
        }
    }
}
