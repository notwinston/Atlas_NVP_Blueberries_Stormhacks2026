using NUnit.Framework;
using Atlas.Spaces.Glued;
using Unity.Mathematics;

namespace Atlas.Spaces.Tests
{
    /// <summary>
    /// Glued_Handedness (Master Plan section 11.1): the determinant sign of copy (row, col) is (-1)^row for the
    /// Klein bottle, the torus generators commute, and the Klein generators satisfy b a b^-1 = a^-1.
    /// </summary>
    public class Glued_Handedness
    {
        const double Tolerance = 1e-9;

        static GluedLattice Lattice(GluedLayout layout)
        {
            // A 2.4 x 3.4 m free rectangle: 2 x 2 cells of 1.2 x 1.7 m, so X and Z cannot be confused.
            return new GluedLattice(2.4, 3.4, layout);
        }

        [Test]
        public void Klein_DeterminantSignOfCopy_IsMinusOneToTheRow()
        {
            GluedLattice lattice = Lattice(GluedLayout.Klein);
            for (int row = -7; row <= 7; row++)
            {
                for (int col = -7; col <= 7; col++)
                {
                    var cell = new GluedCell(col, row);
                    int expected = (row & 1) == 0 ? 1 : -1;
                    Assert.That(lattice.DetSign(cell), Is.EqualTo(expected), "copy " + cell);
                    Assert.That(lattice.Copy(cell).DetSign, Is.EqualTo(expected), "element " + cell);

                    // The same sign from the matrix a renderer would use.
                    float4x4 m = lattice.Copy(cell).ToMatrix();
                    float det = math.determinant(new float3x3(m.c0.xyz, m.c1.xyz, m.c2.xyz));
                    Assert.That(det, Is.EqualTo((float)expected).Within(1e-6f), "matrix " + cell);
                }
            }
        }

        [Test]
        public void Klein_CopyIsTheProductOfItsGenerators()
        {
            GluedLattice lattice = Lattice(GluedLayout.Klein);
            GluedElement a = lattice.GeneratorA, b = lattice.GeneratorB;
            for (int row = -4; row <= 4; row++)
            {
                for (int col = -4; col <= 4; col++)
                {
                    GluedElement product = GluedElement.Compose(a.Power(col), b.Power(row));
                    Assert.That(GluedElement.Distance(product, lattice.Copy(new GluedCell(col, row))), Is.LessThan(Tolerance));
                }
            }
        }

        [Test]
        public void Torus_GeneratorsCommute_AndNoCopyIsMirrored()
        {
            GluedLattice lattice = Lattice(GluedLayout.Torus);
            GluedElement a = lattice.GeneratorA, b = lattice.GeneratorB;
            Assert.That(GluedElement.Distance(GluedElement.Compose(a, b), GluedElement.Compose(b, a)), Is.LessThan(Tolerance));
            for (int row = -5; row <= 5; row++)
                for (int col = -5; col <= 5; col++)
                    Assert.That(lattice.DetSign(new GluedCell(col, row)), Is.EqualTo(1));
        }

        [Test]
        public void Torus3_AllThreeGeneratorsCommute_AndTheVerticalPeriodIs2p6()
        {
            GluedLattice lattice = Lattice(GluedLayout.Torus3);
            GluedElement[] g = { lattice.GeneratorA, lattice.GeneratorB, lattice.GeneratorC };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Assert.That(GluedElement.Distance(GluedElement.Compose(g[i], g[j]), GluedElement.Compose(g[j], g[i])),
                                Is.LessThan(Tolerance));
            Assert.That(lattice.GeneratorC.Translation.y, Is.EqualTo(2.6).Within(Tolerance));
            Assert.That(lattice.DetSign(new GluedCell(1, 1, 1)), Is.EqualTo(1));
            Assert.That(lattice.CellCentre(new GluedCell(0, 0, 1)).y, Is.EqualTo(2.6).Within(Tolerance));
        }

        [Test]
        public void Klein_GeneratorsSatisfyTheirRelation_AndDoNotCommute()
        {
            GluedLattice lattice = Lattice(GluedLayout.Klein);
            GluedElement a = lattice.GeneratorA, b = lattice.GeneratorB;

            // b a b^-1 = a^-1: carried once round the flipped direction, a step east becomes a step west.
            GluedElement conjugate = GluedElement.Compose(GluedElement.Compose(b, a), b.Inverse());
            Assert.That(GluedElement.Distance(conjugate, a.Inverse()), Is.LessThan(Tolerance));
            // The same relation written as a b a = b.
            Assert.That(GluedElement.Distance(GluedElement.Compose(GluedElement.Compose(a, b), a), b), Is.LessThan(Tolerance));
            Assert.That(GluedElement.Distance(GluedElement.Compose(a, b), GluedElement.Compose(b, a)), Is.GreaterThan(1.0));

            // Twice round the flipped direction is a plain translation, which commutes with a.
            GluedElement b2 = b.Power(2);
            Assert.That(b2.Mirror, Is.False);
            Assert.That(GluedElement.Distance(GluedElement.Compose(a, b2), GluedElement.Compose(b2, a)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Elements_InverseAndPower_AreConsistent()
        {
            GluedLattice lattice = Lattice(GluedLayout.Klein);
            var cell = new GluedCell(3, -5);
            GluedElement g = lattice.Copy(cell);
            Assert.That(GluedElement.Distance(GluedElement.Compose(g, g.Inverse()), GluedElement.Identity), Is.LessThan(Tolerance));
            Assert.That(GluedElement.Distance(GluedElement.Compose(g.Inverse(), g), GluedElement.Identity), Is.LessThan(Tolerance));
            Assert.That(GluedElement.Distance(lattice.GeneratorB.Power(-3), lattice.Copy(new GluedCell(0, -3))), Is.LessThan(Tolerance));

            var p = new double3(0.3, 1.6, -0.4);
            double3 there = g.Apply(p);
            Assert.That(math.cmax(math.abs(g.Inverse().Apply(there) - p)), Is.LessThan(Tolerance));
        }

        [Test]
        public void Klein_WalkersCopy_IsMirroredExactlyInTheRowsOfOddOffset()
        {
            GluedLattice lattice = Lattice(GluedLayout.Klein);
            var head = new float3(0.35f, 1.6f, 0.2f);
            GluedCell walker = lattice.CellOf(head);
            Assert.That(walker, Is.EqualTo(new GluedCell(1, 1)));
            float heading = 0.7f;

            for (int row = -2; row <= 4; row++)
            {
                for (int col = -1; col <= 3; col++)
                {
                    var cell = new GluedCell(col, row);
                    GluedAvatarPose.InCell(in lattice, walker, cell, head, heading,
                                           out float3 position, out float copyHeading, out bool mirrored);
                    Assert.That(mirrored, Is.EqualTo(((row - walker.Row) & 1) != 0), "copy " + cell);
                    Assert.That(copyHeading, Is.EqualTo(mirrored ? -heading : heading));
                    Assert.That(lattice.CellOf(position), Is.EqualTo(cell), "the copy stands in its own cell");
                    Assert.That(position.y, Is.EqualTo(head.y));

                    // Offset from the cell centre: kept along Z, negated along X in a mirrored copy.
                    float3 own = head - (float3)lattice.CellCentre(walker);
                    float3 offset = position - (float3)lattice.CellCentre(cell);
                    Assert.That(offset.x, Is.EqualTo(mirrored ? -own.x : own.x).Within(1e-5f));
                    Assert.That(offset.z, Is.EqualTo(own.z).Within(1e-5f));
                }
            }
        }
    }
}
