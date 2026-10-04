using NUnit.Framework;
using Atlas.Rendering;
using Atlas.Rendering.Room;
using Atlas.Spaces.Glued;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>The cell and avatar meshes, the draw accounting and standing mode of S1 (Master Plan 8.4, 8.5, P1-A15).</summary>
    public class GluedBudgetTests
    {
        static readonly string[] SampleRooms = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table };

        static void AssertFacesAgreeWithNormals(AtlasMeshBuilder mesh)
        {
            Assert.That(mesh.IndexCount % 3, Is.EqualTo(0));
            for (int t = 0; t < mesh.IndexCount; t += 3)
            {
                var a = mesh.Vertices[mesh.Indices[t]];
                var b = mesh.Vertices[mesh.Indices[t + 1]];
                var c = mesh.Vertices[mesh.Indices[t + 2]];
                float3 front = math.cross(b.Position - a.Position, c.Position - a.Position);
                Assert.That(math.length(front), Is.GreaterThan(1e-9f), "degenerate triangle " + t / 3);
                Assert.That(math.dot(front, a.Normal + b.Normal + c.Normal), Is.GreaterThan(0f), "triangle " + t / 3);
            }
        }

        [Test]
        public void CellMesh_IsAtMost1500Triangles_ForEveryCellSizeTheRuleCanGive()
        {
            var mesh = new AtlasMeshBuilder();
            // The cell rule gives sides from 1.125 m to 2.25 m; a standing room can go lower.
            for (float sizeX = 0.9f; sizeX <= 2.26f; sizeX += 0.15f)
            {
                for (float sizeZ = 0.9f; sizeZ <= 2.26f; sizeZ += 0.15f)
                {
                    mesh.Clear();
                    GluedCellMesh.Build(sizeX, sizeZ, mesh);
                    Assert.That(mesh.TriangleCount, Is.GreaterThan(0));
                    Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.GluedCell), sizeX + " x " + sizeZ);
                    Assert.That(mesh.TriangleCount, Is.EqualTo(GluedBudget.CellTriangles(sizeX, sizeZ)));
                    AssertFacesAgreeWithNormals(mesh);

                    // The cell fills its footprint exactly and stays below the vertical period of the 3-torus.
                    Assert.That(mesh.BoundsMin.x, Is.EqualTo(-0.5f * sizeX).Within(1e-5f));
                    Assert.That(mesh.BoundsMax.x, Is.EqualTo(0.5f * sizeX).Within(1e-5f));
                    Assert.That(mesh.BoundsMin.z, Is.EqualTo(-0.5f * sizeZ).Within(1e-5f));
                    Assert.That(mesh.BoundsMax.z, Is.EqualTo(0.5f * sizeZ).Within(1e-5f));
                    Assert.That(mesh.BoundsMin.y, Is.EqualTo(0f).Within(1e-6f));
                    Assert.That(mesh.BoundsMax.y, Is.LessThan((float)GluedLattice.VerticalPeriod));
                }
            }
            Assert.That(AtlasMeshBudget.GluedCell, Is.EqualTo(1500));
        }

        [Test]
        public void CellMesh_FloorLines_FallOnTheCellEdges()
        {
            var mesh = new AtlasMeshBuilder();
            float sizeX = 1.2f, sizeZ = 1.7f;
            GluedCellMesh.Build(sizeX, sizeZ, mesh);
            int kx = GluedCellMesh.GridDivisions(sizeX), kz = GluedCellMesh.GridDivisions(sizeZ);
            Assert.That(kx, Is.EqualTo(2));
            Assert.That(kz, Is.EqualTo(3));
            int floor = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex vertex = mesh.Vertices[v];
                if (((vertex.Color >> 8) & 0xFF) == 0) continue;            // no lines: posts and the landmark
                floor++;
                Assert.That(vertex.Position.y, Is.EqualTo(0f));
                // The line coordinate is the grid square count from the low corner: edges are integers, and
                // mirroring x keeps integers integers, so a mirrored row meets its neighbours line for line.
                Assert.That(vertex.Uv1.x, Is.EqualTo((vertex.Position.x + 0.5f * sizeX) / sizeX * kx).Within(1e-4f));
                Assert.That(vertex.Uv1.y, Is.EqualTo((vertex.Position.z + 0.5f * sizeZ) / sizeZ * kz).Within(1e-4f));
            }
            Assert.That(floor, Is.EqualTo((kx + 1) * (kz + 1)));
        }

        [Test]
        public void CellMesh_Landmark_IsChiral()
        {
            // Seen from above the stair climbs counter-clockwise: each step lies to the left of the one below.
            // Its mirror image in x climbs clockwise, so a mirrored Klein row is told apart at a glance.
            double turn = 0.0, mirroredTurn = 0.0;
            for (int k = 0; k + 1 < GluedCellMesh.LandmarkSteps; k++)
            {
                float3 a = GluedCellMesh.LandmarkStep(k, out _), b = GluedCellMesh.LandmarkStep(k + 1, out _);
                Assert.That(b.y, Is.GreaterThan(a.y));
                turn += a.x * b.z - a.z * b.x;
                mirroredTurn += -a.x * b.z - a.z * -b.x;
            }
            Assert.That(turn, Is.GreaterThan(0.01));
            Assert.That(mirroredTurn, Is.EqualTo(-turn).Within(1e-9));

            // It stands off the cell's mirror plane, inside the cell.
            float2 foot = GluedCellMesh.LandmarkBase(1.2f, 1.7f);
            Assert.That(foot.x, Is.LessThan(-0.2f));
            Assert.That(math.abs(foot.x) + GluedCellMesh.LandmarkStepRadius + 0.1f, Is.LessThan(0.6f));
        }

        [Test]
        public void AvatarMesh_IsAtMost600Triangles_WithALeftAndARightHand()
        {
            var mesh = new AtlasMeshBuilder();
            GluedAvatarMesh.Build(mesh);
            Assert.That(mesh.TriangleCount, Is.GreaterThan(0));
            Assert.That(mesh.TriangleCount, Is.LessThanOrEqualTo(AtlasMeshBudget.SelfAvatar));
            Assert.That(AtlasMeshBudget.SelfAvatar, Is.EqualTo(600));
            Assert.That(mesh.TriangleCount, Is.EqualTo(GluedBudget.AvatarTriangles()));
            AssertFacesAgreeWithNormals(mesh);

            // The left shoulder (the walker's -X side) and the right one carry different colours.
            float2 left = AtlasPalette.Uv(AtlasPalette.AvatarLeft), right = AtlasPalette.Uv(AtlasPalette.AvatarRight);
            int leftCount = 0, rightCount = 0;
            for (int v = 0; v < mesh.VertexCount; v++)
            {
                AtlasVertex vertex = mesh.Vertices[v];
                if (math.all(vertex.Uv0 == left))
                {
                    leftCount++;
                    Assert.That(vertex.Position.x, Is.LessThan(0f));
                }
                else if (math.all(vertex.Uv0 == right))
                {
                    rightCount++;
                    Assert.That(vertex.Position.x, Is.GreaterThan(0f));
                }
            }
            Assert.That(leftCount, Is.GreaterThan(0));
            Assert.That(rightCount, Is.EqualTo(leftCount));
            // The body hangs from the head: nothing above the crown, nothing reaches the floor of a crouching walker.
            Assert.That(mesh.BoundsMax.y, Is.LessThanOrEqualTo(GluedAvatarMesh.HeadRadius + 1e-5f));
            Assert.That(mesh.BoundsMin.y, Is.GreaterThan(-0.9f));
        }

        [Test]
        public void Lattice_AtFiveByFive_CostsAtMost110DrawCallsAnd110kTriangles()
        {
            // The largest cell the rule can give, so the bound holds in every room.
            int cell = GluedBudget.CellTriangles(2.25f, 2.25f), avatar = GluedBudget.AvatarTriangles(), prompt = GluedBudget.PromptTriangles();
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein })
            {
                GluedDrawCost cost = GluedBudget.Lattice(layout, 5, cell, avatar, true, prompt);
                Assert.That(cost.DrawCallsPerEye, Is.EqualTo(25 + 24 + 1));
                Assert.That(cost.DrawCallsPerFrame, Is.EqualTo(100));
                Assert.That(cost.DrawCallsPerFrame, Is.LessThanOrEqualTo(110));
                Assert.That(cost.TrianglesPerFrame, Is.EqualTo(2 * (25 * cell + 24 * avatar + prompt)));
                Assert.That(cost.TrianglesPerFrame, Is.LessThanOrEqualTo(110000));
                Assert.That(GluedBudget.LatticeFits(layout, in cost), Is.True);
            }
            // Even with every cell and every avatar at its own ceiling the triangle bound holds.
            GluedDrawCost ceiling = GluedBudget.Lattice(GluedLayout.Torus, 5, AtlasMeshBudget.GluedCell, AtlasMeshBudget.SelfAvatar);
            Assert.That(ceiling.TrianglesPerFrame, Is.EqualTo(103800));
            Assert.That(GluedBudget.LatticeFits(GluedLayout.Torus, in ceiling), Is.True);
        }

        [Test]
        public void Lattice_OfTheThreeTorus_CostsAtMost120DrawCallsAnd120kTriangles()
        {
            int cell = GluedBudget.CellTriangles(2.25f, 2.25f), avatar = GluedBudget.AvatarTriangles(), prompt = GluedBudget.PromptTriangles();
            GluedDrawCost cost = GluedBudget.Lattice(GluedLayout.Torus3, 5, cell, avatar, true, prompt);
            Assert.That(cost.DrawCallsPerEye, Is.EqualTo(27 + 26 + 1));
            Assert.That(cost.DrawCallsPerFrame, Is.EqualTo(108));
            Assert.That(cost.DrawCallsPerFrame, Is.LessThanOrEqualTo(120));
            Assert.That(cost.TrianglesPerFrame, Is.LessThanOrEqualTo(120000));
            Assert.That(GluedBudget.LatticeFits(GluedLayout.Torus3, in cost), Is.True);

            GluedDrawCost ceiling = GluedBudget.Lattice(GluedLayout.Torus3, 3, AtlasMeshBudget.GluedCell, AtlasMeshBudget.SelfAvatar);
            Assert.That(ceiling.TrianglesPerFrame, Is.EqualTo(112200));
            Assert.That(GluedBudget.LatticeFits(GluedLayout.Torus3, in ceiling), Is.True);
        }

        [Test]
        public void Lattice_AccountingFollowsTheCopyCountAndTheAvatarSwitch()
        {
            GluedDrawCost three = GluedBudget.Lattice(GluedLayout.Klein, 3, 400, 200);
            Assert.That(three.DrawCallsPerEye, Is.EqualTo(9 + 8));
            Assert.That(three.TrianglesPerEye, Is.EqualTo(9 * 400 + 8 * 200));
            GluedDrawCost seven = GluedBudget.Lattice(GluedLayout.Torus, 7, 400, 200);
            Assert.That(seven.DrawCallsPerEye, Is.EqualTo(49 + 48));
            GluedDrawCost bare = GluedBudget.Lattice(GluedLayout.Torus, 5, 400, 200, false);
            Assert.That(bare.DrawCallsPerEye, Is.EqualTo(25));
            Assert.That(bare.TrianglesPerEye, Is.EqualTo(25 * 400));
        }

        [Test]
        public void SampleRooms_StayInsideTheLatticeFiguresAndTheSpaceRow()
        {
            foreach (string name in SampleRooms)
            {
                RoomModel room = RoomFixtures.LoadFromProject(name);
                foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
                {
                    GluedDrawCost lattice = GluedBudget.Lattice(room, layout, 5);
                    Assert.That(GluedBudget.LatticeFits(layout, in lattice), Is.True, name + " " + layout);
                    GluedDrawCost space = GluedBudget.Space(room, layout, 5);
                    Assert.That(GluedBudget.SpaceFits(layout, in space), Is.True, name + " " + layout);

                    // The room is drawn once: the kerbs and one draw call per object, on top of the lattice.
                    Assert.That(space.DrawCallsPerEye, Is.EqualTo(lattice.DrawCallsPerEye + 1 + room.Objects.Length), name);
                    TestContext.Out.WriteLine(name + " " + layout + ": lattice " + lattice.DrawCallsPerFrame + " draw calls, "
                        + lattice.TrianglesPerFrame + " triangles; with the room " + space.DrawCallsPerFrame + ", " + space.TrianglesPerFrame);
                }
            }
        }

        [Test]
        public void BudgetRoom_StaysWithinItsRowOfThePerSpaceTable()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.BudgetRoom);
            Assert.That(room.Objects.Length, Is.EqualTo(12));
            RoomDrawCost shell = RoomBudget.Estimate(room, WallMode.Kerbs, false, true);
            Assert.That(shell.DrawCallsPerEye, Is.EqualTo(13), "kerbs and twelve objects; the cells bring the floor");
            Assert.That(shell.ObjectTriangles, Is.EqualTo(60000));

            int cell = GluedBudget.CellTriangles(1.2f, 1.7f), avatar = GluedBudget.AvatarTriangles(), prompt = GluedBudget.PromptTriangles();

            // 5 x 5, torus and Klein bottle: 25 cells, 24 avatars, the prompt, the room once.
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein })
            {
                GluedDrawCost lattice = GluedBudget.Lattice(room, layout, 5);
                Assert.That(lattice.DrawCallsPerFrame, Is.EqualTo(100));
                Assert.That(lattice.TrianglesPerFrame, Is.EqualTo(2 * (25 * cell + 24 * avatar + prompt)));
                Assert.That(GluedBudget.LatticeFits(layout, in lattice), Is.True);

                GluedDrawCost space = GluedBudget.Space(room, layout, 5);
                Assert.That(space.DrawCallsPerFrame, Is.EqualTo(126));
                Assert.That(space.DrawCallsPerFrame, Is.LessThanOrEqualTo(150));
                Assert.That(space.TrianglesPerFrame, Is.EqualTo(lattice.TrianglesPerFrame + shell.TrianglesPerFrame));
                Assert.That(space.TrianglesPerFrame, Is.LessThanOrEqualTo(250000));
                Assert.That(GluedBudget.SpaceFits(layout, in space), Is.True);
            }

            // 3 x 3 x 3: 27 cells, 26 avatars, the prompt, the room once.
            GluedDrawCost solid = GluedBudget.Space(room, GluedLayout.Torus3, 5);
            Assert.That(solid.DrawCallsPerFrame, Is.EqualTo(134));
            Assert.That(solid.DrawCallsPerFrame, Is.LessThanOrEqualTo(160));
            Assert.That(solid.TrianglesPerFrame, Is.LessThanOrEqualTo(250000));
            Assert.That(GluedBudget.SpaceFits(GluedLayout.Torus3, in solid), Is.True);

            // The rungs of the thermal ladder: 3 x 3 is cheaper, and 7 x 7 still fits the frame's hard limits.
            GluedDrawCost serious = GluedBudget.Space(room, GluedLayout.Torus, 3);
            Assert.That(serious.DrawCallsPerFrame, Is.EqualTo(2 * (9 + 8 + 1 + 13)));
            GluedDrawCost high = GluedBudget.Space(room, GluedLayout.Torus, 7);
            Assert.That(high.DrawCallsPerFrame, Is.EqualTo(2 * (49 + 48 + 1 + 13)));
            Assert.That(high.DrawCallsPerFrame, Is.LessThanOrEqualTo(300));
            Assert.That(high.TrianglesPerFrame, Is.LessThanOrEqualTo(AtlasMeshBudget.FrameTriangles));

            // The numbers reached, per frame, both eyes.
            Assert.That(cell, Is.EqualTo(346));
            Assert.That(avatar, Is.EqualTo(182));
            Assert.That(prompt, Is.EqualTo(12));
            Assert.That(GluedBudget.Lattice(room, GluedLayout.Torus, 5).TrianglesPerFrame, Is.EqualTo(26060));
            Assert.That(GluedBudget.Space(room, GluedLayout.Torus, 5).TrianglesPerFrame, Is.EqualTo(147560));
            Assert.That(GluedBudget.Lattice(room, GluedLayout.Torus3, 5).TrianglesPerFrame, Is.EqualTo(28172));
            Assert.That(solid.TrianglesPerFrame, Is.EqualTo(149672));

            TestContext.Out.WriteLine("budget_room: cell " + cell + " triangles, avatar " + avatar + ", prompt " + prompt
                + "; 5 x 5 with the room " + GluedBudget.Space(room, GluedLayout.Torus, 5).TrianglesPerFrame
                + " triangles; T3 with the room " + solid.TrianglesPerFrame);
        }

        [Test]
        public void StandingMode_RunsInPlace_WithNoPrompt()
        {
            RoomModel room = RoomFixtures.LoadFromProject(RoomFixtures.TinyStanding);
            Assert.That(room.Standing, Is.True);
            foreach (GluedLayout layout in new[] { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 })
            {
                var lattice = new GluedLattice(room, layout);
                Assert.That(lattice.Nx, Is.EqualTo(1));
                Assert.That(lattice.Nz, Is.EqualTo(1));
                // The one cell is the whole free floor; the walker stays in it and the copies are drawn round it.
                Assert.That(lattice.CellOf(new float3(0f, 1.6f, 0f)), Is.EqualTo(new GluedCell(0, 0)));
                Assert.That(lattice.CellOf(new float3(0.69f, 1.6f, -0.69f)), Is.EqualTo(new GluedCell(0, 0)));
                var cells = new GluedCell[GluedRules.MaxCells];
                Assert.That(lattice.FillCopies(new GluedCell(0, 0), 5, cells), Is.EqualTo(GluedRules.CellCount(layout, 5)));

                Assert.That(GluedPrompt.Available(in lattice, room.Standing), Is.False);
                Assert.That(GluedPrompt.Shown(in lattice, room.Standing, 0), Is.False);
                Assert.That(GluedPrompt.Place(in lattice, float2.zero, out _, out _), Is.False, "no cell edge inside the free floor");

                // No prompt, so no draw call for it; the space still fits its row.
                GluedDrawCost cost = GluedBudget.Lattice(room, layout, 5);
                Assert.That(cost.DrawCallsPerEye, Is.EqualTo(2 * GluedRules.CellCount(layout, 5) - 1));
                Assert.That(GluedBudget.SpaceFits(layout, GluedBudget.Space(room, layout, 5)), Is.True);
            }

            // The flag alone silences the prompt, even where a cell edge could be reached.
            var wide = new GluedLattice(2.4, 2.4, GluedLayout.Torus);
            Assert.That(GluedPrompt.Available(in wide, true), Is.False);
            Assert.That(GluedPrompt.Shown(in wide, true, 0), Is.False);
        }

        [Test]
        public void WalkingMode_PromptsAtTheNearestInnerEdge_UntilTheFirstCrossing()
        {
            var lattice = new GluedLattice(2.4, 3.4, GluedLayout.Torus);    // inner edges at x = 0 and z = 0
            Assert.That(GluedPrompt.Available(in lattice, false), Is.True);
            Assert.That(GluedPrompt.Shown(in lattice, false, 0), Is.True);
            Assert.That(GluedPrompt.Shown(in lattice, false, 1), Is.False);

            // Nearest edge is x = 0, to the walker's east: the chevrons lie on it, level with the walker, pointing +X.
            Assert.That(GluedPrompt.Place(in lattice, new float2(-0.3f, -0.9f), out float2 at, out float heading), Is.True);
            Assert.That(at.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(at.y, Is.EqualTo(-0.9f).Within(1e-6f));
            Assert.That(heading, Is.EqualTo(0.5f * math.PI).Within(1e-6f));
            // From the other side they point -X.
            Assert.That(GluedPrompt.Place(in lattice, new float2(0.3f, -0.9f), out at, out heading), Is.True);
            Assert.That(heading, Is.EqualTo(-0.5f * math.PI).Within(1e-6f));
            // Nearest edge is z = 0, to the north: pointing +Z; and they stay inside the free rectangle.
            Assert.That(GluedPrompt.Place(in lattice, new float2(1.15f, -0.2f), out at, out heading), Is.True);
            Assert.That(at.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(at.x, Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(heading, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(GluedPrompt.Place(in lattice, new float2(1.15f, 0.2f), out at, out heading), Is.True);
            Assert.That(heading, Is.EqualTo(math.PI).Within(1e-6f));

            // A room with cells along one side only prompts across that side's edge.
            var narrow = new GluedLattice(1.6, 3.4, GluedLayout.Klein);
            Assert.That(narrow.Nx, Is.EqualTo(1));
            Assert.That(GluedPrompt.Place(in narrow, new float2(0.01f, -1.5f), out at, out heading), Is.True);
            Assert.That(at.y, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(heading, Is.EqualTo(0f).Within(1e-6f));

            var mesh = new AtlasMeshBuilder();
            GluedPromptMesh.Build(mesh);
            Assert.That(mesh.TriangleCount, Is.EqualTo(4 * GluedPromptMesh.Chevrons));
            AssertFacesAgreeWithNormals(mesh);
            for (int v = 0; v < mesh.VertexCount; v++) Assert.That(mesh.Vertices[v].Normal.y, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void Parameters_AreDeclaredInGroupGlued_AsTheContractSays()
        {
            var registry = new Atlas.Link.ParamRegistry();
            GluedSpace.DeclareParams(registry);
            GluedSpace.DeclareParams(registry);                              // declared at boot and again in Configure
            Assert.That(registry.Count, Is.EqualTo(3));

            Atlas.Link.ParamEntry mode = registry.Find("glued.mode");
            Assert.That(mode.Group, Is.EqualTo("glued"));
            Assert.That(mode.Kind, Is.EqualTo(Atlas.Link.ParamKind.Enum));
            Assert.That(mode.EnumCount, Is.EqualTo(3));
            Assert.That(mode.EnumName(0), Is.EqualTo("t2"));
            Assert.That(mode.EnumName(1), Is.EqualTo("klein"));
            Assert.That(mode.EnumName(2), Is.EqualTo("t3"));
            Assert.That(mode.Int, Is.EqualTo(0));
            Assert.That(mode.OnRelease, Is.True);

            Atlas.Link.ParamEntry copies = registry.Find("glued.copies");
            Assert.That(copies.Group, Is.EqualTo("glued"));
            Assert.That(copies.Min, Is.EqualTo(3f));
            Assert.That(copies.Max, Is.EqualTo(7f));
            Assert.That(copies.Int, Is.EqualTo(5));
            Assert.That(copies.OnRelease, Is.True);

            Atlas.Link.ParamEntry avatar = registry.Find("glued.avatar");
            Assert.That(avatar.Group, Is.EqualTo("glued"));
            Assert.That(avatar.Bool, Is.True);
            Assert.That(avatar.OnRelease, Is.False);

            // A value survives the second declaration.
            copies.Set(3);
            GluedSpace.DeclareParams(registry);
            Assert.That(registry.Find("glued.copies").Int, Is.EqualTo(3));
        }
    }
}
