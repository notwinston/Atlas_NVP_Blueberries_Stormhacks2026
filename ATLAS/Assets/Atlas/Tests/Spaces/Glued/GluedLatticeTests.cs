using NUnit.Framework;
using Atlas.Spaces.Glued;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>The cell index, re-centring, the copies drawn, the fog and the ladder of S1 (Master Plan task P1-A09).</summary>
    public class GluedLatticeTests
    {
        static readonly GluedLayout[] Layouts = { GluedLayout.Torus, GluedLayout.Klein, GluedLayout.Torus3 };

        [Test]
        public void Lattice_TakesTheCellSizeOfTheRoomModel()
        {
            string[] names = { RoomFixtures.Sample3x3, RoomFixtures.SampleL3x4, RoomFixtures.Sample4x5Table, RoomFixtures.BudgetRoom, RoomFixtures.TinyStanding };
            int[] nx = { 2, 1, 1, 2, 1 }, nz = { 2, 2, 2, 2, 1 };
            double[] sizeX = { 1.2, 1.9, 1.6, 1.2, 1.4 }, sizeZ = { 1.2, 1.2, 1.7, 1.7, 1.4 };
            for (int k = 0; k < names.Length; k++)
            {
                RoomModel room = RoomFixtures.LoadFromProject(names[k]);
                var lattice = new GluedLattice(room, GluedLayout.Torus);
                Assert.That(lattice.Nx, Is.EqualTo(nx[k]), names[k]);
                Assert.That(lattice.Nz, Is.EqualTo(nz[k]), names[k]);
                Assert.That(lattice.SizeX, Is.EqualTo(sizeX[k]).Within(1e-6), names[k]);
                Assert.That(lattice.SizeZ, Is.EqualTo(sizeZ[k]).Within(1e-6), names[k]);
                Assert.That(lattice.SizeX, Is.EqualTo(room.Glued.SizeX), names[k]);
                // The nx x nz cells fill the free rectangle exactly.
                Assert.That(lattice.Nx * lattice.SizeX, Is.EqualTo(lattice.Wf).Within(1e-5), names[k]);
                Assert.That(lattice.Nz * lattice.SizeZ, Is.EqualTo(lattice.Df).Within(1e-5), names[k]);
            }
        }

        [Test]
        public void Lattice_OfARoomWithoutAGrid_CutsItByTheCellRule()
        {
            var room = new RoomModel { FreeRect = new FreeRect(3.2f, 4.4f) };
            var lattice = new GluedLattice(room, GluedLayout.Torus);
            Assert.That(lattice.Nx, Is.EqualTo(2));
            Assert.That(lattice.Nz, Is.EqualTo(3));
            Assert.That(lattice.SizeX, Is.EqualTo(1.6).Within(1e-6));
            Assert.That(lattice.SizeZ, Is.EqualTo(4.4 / 3.0).Within(1e-6));
        }

        [Test]
        public void CellOf_IsTheFloorOfTheContractFormula()
        {
            var lattice = new GluedLattice(2.4, 3.4, GluedLayout.Torus);    // cells of 1.2 x 1.7
            Assert.That(lattice.CellOf(new float3(-1.19f, 1.6f, -1.69f)), Is.EqualTo(new GluedCell(0, 0)));
            Assert.That(lattice.CellOf(new float3(0.01f, 1.6f, -0.01f)), Is.EqualTo(new GluedCell(1, 0)));
            Assert.That(lattice.CellOf(new float3(-0.01f, 1.6f, 0.01f)), Is.EqualTo(new GluedCell(0, 1)));
            Assert.That(lattice.CellOf(new float3(1.19f, 1.6f, 1.69f)), Is.EqualTo(new GluedCell(1, 1)));
            // The lattice continues past the free rectangle in every direction.
            Assert.That(lattice.CellOf(new float3(-1.21f, 1.6f, -1.71f)), Is.EqualTo(new GluedCell(-1, -1)));
            Assert.That(lattice.CellOf(new float3(4.9f, 0f, -7.0f)), Is.EqualTo(new GluedCell(5, -4)));
            Assert.That(lattice.InRoom(new GluedCell(1, 1)), Is.True);
            Assert.That(lattice.InRoom(new GluedCell(2, 1)), Is.False);
            Assert.That(lattice.InRoom(new GluedCell(0, -1)), Is.False);

            // Every cell centre is in its own cell, and cell (i, j) starts at -wf/2 + i * sizeX.
            for (int row = -3; row <= 3; row++)
            {
                for (int col = -3; col <= 3; col++)
                {
                    var cell = new GluedCell(col, row);
                    double3 centre = lattice.CellCentre(cell);
                    Assert.That(lattice.CellOf((float3)centre), Is.EqualTo(cell));
                    Assert.That(centre.x - 0.5 * lattice.SizeX, Is.EqualTo(-1.2 + col * 1.2).Within(1e-9));
                    Assert.That(centre.z - 0.5 * lattice.SizeZ, Is.EqualTo(-1.7 + row * 1.7).Within(1e-9));
                }
            }
        }

        [Test]
        public void CellOf_LayerFollowsTheVerticalPeriod_OnlyInTheThreeTorus()
        {
            var flat = new GluedLattice(2.4, 2.4, GluedLayout.Klein);
            var solid = new GluedLattice(2.4, 2.4, GluedLayout.Torus3);
            Assert.That(flat.CellOf(new float3(0.1f, 3.0f, 0.1f)).Layer, Is.EqualTo(0));
            Assert.That(solid.CellOf(new float3(0.1f, 1.6f, 0.1f)).Layer, Is.EqualTo(0));
            Assert.That(solid.CellOf(new float3(0.1f, 2.7f, 0.1f)).Layer, Is.EqualTo(1));
            Assert.That(solid.CellOf(new float3(0.1f, -0.1f, 0.1f)).Layer, Is.EqualTo(-1));
        }

        [Test]
        public void Recentring_AcrossEveryEdge_MovesTheWalkersCellByOne_AndKeepsTheCoverFixed()
        {
            foreach (GluedLayout layout in Layouts)
            {
                var lattice = new GluedLattice(2.4, 3.4, layout);
                var cells = new GluedCell[GluedRules.MaxCells];
                var start = new float3(-0.6f, 1.6f, -0.85f);                 // centre of cell (0, 0)
                float3[] steps =
                {
                    new float3(1.2f, 0f, 0f), new float3(-1.2f, 0f, 0f), new float3(0f, 0f, 1.7f), new float3(0f, 0f, -1.7f)
                };
                GluedCell[] expected = { new GluedCell(1, 0), new GluedCell(-1, 0), new GluedCell(0, 1), new GluedCell(0, -1) };

                for (int e = 0; e < steps.Length; e++)
                {
                    GluedCell before = lattice.CellOf(start);
                    Assert.That(before, Is.EqualTo(new GluedCell(0, 0)));
                    // Just short of the edge the cell is the old one; just past it, the neighbour.
                    Assert.That(lattice.CellOf(start + steps[e] * 0.49f), Is.EqualTo(before), layout + " edge " + e);
                    GluedCell after = lattice.CellOf(start + steps[e] * 0.51f);
                    Assert.That(after, Is.EqualTo(expected[e]), layout + " edge " + e);

                    int count = lattice.FillCopies(after, 5, cells);
                    Assert.That(count, Is.EqualTo(GluedRules.CellCount(layout, 5)));
                    int half = (GluedRules.CopiesPerSide(layout, 5) - 1) / 2;
                    bool holdsWalker = false, holdsOld = false;
                    for (int i = 0; i < count; i++)
                    {
                        // The block is centred on the walker's new cell.
                        Assert.That(math.abs(cells[i].Col - after.Col), Is.LessThanOrEqualTo(half));
                        Assert.That(math.abs(cells[i].Row - after.Row), Is.LessThanOrEqualTo(half));
                        holdsWalker |= cells[i].Equals(after);
                        holdsOld |= cells[i].Equals(before);
                    }
                    Assert.That(holdsWalker, Is.True);
                    Assert.That(holdsOld, Is.True, "the cell just left is still drawn: no seam behind the walker");
                }

                // No seam: a cell is drawn at the same place and with the same hand whichever cell the walker
                // is in, because its transform depends on its own index alone.
                for (int row = -3; row <= 3; row++)
                {
                    for (int col = -3; col <= 3; col++)
                    {
                        var cell = new GluedCell(col, row);
                        GluedElement copy = lattice.Copy(cell);
                        double3 centre = lattice.CellCentre(cell);
                        Assert.That(math.cmax(math.abs(lattice.FundamentalCentre + copy.Translation - centre)), Is.LessThan(1e-9));
                        // Neighbours share their edge exactly.
                        double3 next = lattice.CellCentre(new GluedCell(col + 1, row));
                        Assert.That(next.x - centre.x, Is.EqualTo(lattice.SizeX).Within(1e-9));
                    }
                }
            }
        }

        [Test]
        public void Recentring_KeepsEveryCellWithinTheFogRadiusDrawn()
        {
            // Wherever the walker stands in its cell, every cell that reaches inside the fog end is in the block.
            foreach (int copies in new[] { 3, 5, 7 })
            {
                var lattice = new GluedLattice(2.4, 3.4, GluedLayout.Torus);
                GluedRules.Fog(in lattice, copies, out float start, out float end);
                int half = (copies - 1) / 2;
                Assert.That(end, Is.EqualTo(half * 1.2f).Within(1e-5f));
                Assert.That(start, Is.EqualTo(end - (copies == 3 ? 0.6f : 1.2f)).Within(1e-5f));
                Assert.That(start, Is.GreaterThanOrEqualTo(0f));
                Assert.That(end, Is.GreaterThan(start));
                // The nearest point that is not drawn lies at least `half` cells from any point of the walker's cell.
                Assert.That(half * lattice.SizeX, Is.GreaterThanOrEqualTo(end - 1e-5));
                Assert.That(half * lattice.SizeZ, Is.GreaterThanOrEqualTo(end - 1e-5));
            }
        }

        [Test]
        public void Copies_ForEachLayoutAndRequest()
        {
            // request, drawn per side for T2 and Klein
            int[,] flat = { { 1, 3 }, { 3, 3 }, { 4, 3 }, { 5, 5 }, { 6, 5 }, { 7, 7 }, { 9, 7 } };
            for (int k = 0; k < flat.GetLength(0); k++)
            {
                int n = flat[k, 1];
                Assert.That(GluedRules.CopiesPerSide(GluedLayout.Torus, flat[k, 0]), Is.EqualTo(n));
                Assert.That(GluedRules.CopiesPerSide(GluedLayout.Klein, flat[k, 0]), Is.EqualTo(n));
                Assert.That(GluedRules.CellCount(GluedLayout.Torus, flat[k, 0]), Is.EqualTo(n * n));
                Assert.That(GluedRules.CellCount(GluedLayout.Klein, flat[k, 0]), Is.EqualTo(n * n));
                // The 3-torus is 3 x 3 x 3 whatever is asked.
                Assert.That(GluedRules.CopiesPerSide(GluedLayout.Torus3, flat[k, 0]), Is.EqualTo(3));
                Assert.That(GluedRules.CellCount(GluedLayout.Torus3, flat[k, 0]), Is.EqualTo(27));
            }
            Assert.That(GluedRules.MaxCells, Is.EqualTo(49));
            Assert.That(GluedRules.LayoutOf(0), Is.EqualTo(GluedLayout.Torus));
            Assert.That(GluedRules.LayoutOf(1), Is.EqualTo(GluedLayout.Klein));
            Assert.That(GluedRules.LayoutOf(2), Is.EqualTo(GluedLayout.Torus3));
            Assert.That(GluedRules.LayoutNames, Is.EqualTo(new[] { "t2", "klein", "t3" }));
        }

        [Test]
        public void FillCopies_ListsEachCellOnce_RoundTheWalker()
        {
            var cells = new GluedCell[GluedRules.MaxCells];
            var walker = new GluedCell(4, -2);
            foreach (GluedLayout layout in Layouts)
            {
                foreach (int copies in new[] { 3, 5, 7 })
                {
                    var lattice = new GluedLattice(2.4, 2.4, layout);
                    int count = lattice.FillCopies(walker, copies, cells);
                    int n = GluedRules.CopiesPerSide(layout, copies);
                    Assert.That(count, Is.EqualTo(layout == GluedLayout.Torus3 ? 27 : n * n));

                    int own = 0, mirrored = 0;
                    for (int i = 0; i < count; i++)
                    {
                        if (cells[i].Equals(walker)) own++;
                        if (lattice.IsMirrored(cells[i])) mirrored++;
                        for (int j = 0; j < i; j++) Assert.That(cells[j], Is.Not.EqualTo(cells[i]));
                        Assert.That(math.abs(cells[i].Layer), Is.LessThanOrEqualTo(layout == GluedLayout.Torus3 ? 1 : 0));
                    }
                    Assert.That(own, Is.EqualTo(1));
                    // Klein: the odd rows of the block are mirrored. The walker is in row -2, so of n rows (n - 1) / 2 + ... are odd.
                    int oddRows = 0;
                    for (int dr = -(n - 1) / 2; dr <= (n - 1) / 2; dr++) if (((walker.Row + dr) & 1) != 0) oddRows++;
                    Assert.That(mirrored, Is.EqualTo(layout == GluedLayout.Klein ? oddRows * n : 0));
                }
            }
        }

        [Test]
        public void Copies_FollowTheQualityLadder()
        {
            Assert.That(GluedRules.CopiesForTier(QualityTier.Nominal), Is.EqualTo(5));
            Assert.That(GluedRules.CopiesForTier(QualityTier.Fair), Is.EqualTo(5));
            Assert.That(GluedRules.CopiesForTier(QualityTier.Serious), Is.EqualTo(3));
            Assert.That(GluedRules.CopiesForTier(QualityTier.Critical), Is.EqualTo(3));
            Assert.That(GluedRules.CopiesForTier(QualityTier.High), Is.EqualTo(7));

            // Entering on a cool phone keeps what the operator set; a tier change takes the rung's copies.
            var follower = new GluedTierFollower();
            Assert.That(follower.Step(QualityTier.Nominal, 5), Is.EqualTo(5));
            Assert.That(follower.Step(QualityTier.Nominal, 3), Is.EqualTo(3), "the operator's choice stands between changes");
            Assert.That(follower.Step(QualityTier.Serious, 5), Is.EqualTo(3));
            Assert.That(follower.Step(QualityTier.Serious, 3), Is.EqualTo(3));
            Assert.That(follower.Step(QualityTier.Fair, 3), Is.EqualTo(5));
            Assert.That(follower.Step(QualityTier.High, 5), Is.EqualTo(7));
            Assert.That(follower.Step(QualityTier.Nominal, 7), Is.EqualTo(5));

            // Entering on a hot phone never draws more than the rung allows, and never raises a lower choice.
            var hot = new GluedTierFollower();
            Assert.That(hot.Step(QualityTier.Critical, 5), Is.EqualTo(3));
            var cool = new GluedTierFollower();
            Assert.That(cool.Step(QualityTier.High, 5), Is.EqualTo(5));
        }

        [Test]
        public void Heading_FollowsTheHead_AndSurvivesLookingStraightUpOrDown()
        {
            Assert.That(GluedAvatarPose.Heading(quaternion.identity), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(GluedAvatarPose.Heading(quaternion.RotateY(0.9f)), Is.EqualTo(0.9f).Within(1e-5f));
            Assert.That(GluedAvatarPose.Heading(quaternion.RotateY(-2.5f)), Is.EqualTo(-2.5f).Within(1e-5f));
            // Pitch does not change the heading, even at the poles.
            foreach (float pitch in new[] { -1.5707963f, -1.0f, 0.6f, 1.5707963f })
            {
                quaternion head = math.mul(quaternion.RotateY(0.9f), quaternion.RotateX(pitch));
                Assert.That(GluedAvatarPose.Heading(head), Is.EqualTo(0.9f).Within(1e-3f), "pitch " + pitch);
            }
            Assert.That(GluedAvatarPose.Heading(new quaternion(0f, 0f, 0f, 0f), 0.4f), Is.EqualTo(0.4f));
        }
    }
}
