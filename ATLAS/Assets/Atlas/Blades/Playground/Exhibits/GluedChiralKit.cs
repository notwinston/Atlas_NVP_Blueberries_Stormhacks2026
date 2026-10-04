using System;
using Atlas.Spaces.Glued;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>The result of the determinant test over the copies round a walker.</summary>
    public struct GluedChiralReport
    {
        /// <summary>Copies tested, the walker's own cell included.</summary>
        public int Copies;
        /// <summary>Copies whose matrix determinant AND landmark handedness both equal (-1)^row.</summary>
        public int Correct;
        /// <summary>Copies that are mirror images of the fundamental cell.</summary>
        public int Mirrored;

        public bool AllCorrect { get { return Copies > 0 && Correct == Copies; } }
    }

    /// <summary>One copy of the walker in another cell.</summary>
    public struct GluedAvatarCopy
    {
        public GluedCell Cell;
        public float3 Position;
        /// <summary>About +Y, Unity's sense; negated in a mirrored copy.</summary>
        public float Heading;
        public bool Mirrored;
    }

    /// <summary>
    /// The chiral kit of the Glued exhibit (Master Plan task P2-C13): the comparison of the landmark with its
    /// mirror image in the Klein rows, and the walker's copy in every other cell. It works on the cell
    /// geometry of P1-A09 (<see cref="GluedCellMesh"/>'s landmark and <see cref="GluedAvatarPose"/>), so it is
    /// the same whether the landmark is the built-in one or a hero mesh. Engine-free.
    /// </summary>
    public static class GluedChiralKit
    {
        /// <summary>
        /// Tests every copy drawn round `walker`: the determinant of the copy's matrix and the handedness of the
        /// landmark's stair (the sign of the triple product of two stair steps and the pole) must both equal
        /// (-1)^row for the Klein bottle and +1 for the torus and the 3-torus. 100% of the copies is the row of
        /// the plan.
        /// </summary>
        public static GluedChiralReport DeterminantTest(in GluedLattice lattice, GluedCell walker, int copiesPerSide)
        {
            var cells = new GluedCell[GluedRules.MaxCells * GluedRules.Torus3Copies];
            int count = lattice.FillCopies(walker, copiesPerSide, cells);
            float own = Handedness(float3x3.identity);
            var report = new GluedChiralReport { Copies = count };
            for (int i = 0; i < count; i++)
            {
                GluedCell cell = cells[i];
                int expected = ExpectedSign(in lattice, cell);
                float4x4 matrix = lattice.Copy(cell).ToMatrix();
                var linear = new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz);
                int det = math.determinant(linear) < 0f ? -1 : 1;
                int hand = Handedness(linear) * own < 0f ? -1 : 1;
                if (expected < 0) report.Mirrored++;
                if (det == expected && hand == expected) report.Correct++;
            }
            return report;
        }

        /// <summary>The sign the plan names, (-1)^row for the Klein bottle and +1 otherwise, from the row alone.</summary>
        public static int ExpectedSign(in GluedLattice lattice, GluedCell cell)
        {
            if (lattice.Layout != GluedLayout.Klein) return 1;
            return (cell.Row & 1) == 0 ? 1 : -1;
        }

        /// <summary>
        /// Signed volume of two steps of the landmark's stair and its pole, after a linear map: the sign says which
        /// hand the stair winds. Nonzero for the landmark of the cell mesh.
        /// </summary>
        public static float Handedness(float3x3 linear)
        {
            float3 a = math.mul(linear, GluedCellMesh.LandmarkStep(0, out _));
            float3 b = math.mul(linear, GluedCellMesh.LandmarkStep(3, out _));
            float3 pole = math.mul(linear, new float3(0f, GluedCellMesh.LandmarkPoleHeight, 0f));
            return math.dot(math.cross(a, b), pole);
        }

        /// <summary>
        /// The walker's copy in every cell drawn but their own: 24 for the 5 x 5 lattice, 26 for the 3 x 3 x 3.
        /// Returns the count; `into` must hold <see cref="GluedRules.MaxCells"/> times 3 entries at most.
        /// </summary>
        public static int AvatarCopies(in GluedLattice lattice, GluedCell walker, int copiesPerSide, float3 head,
                                       float heading, GluedAvatarCopy[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            var cells = new GluedCell[GluedRules.MaxCells * GluedRules.Torus3Copies];
            int count = lattice.FillCopies(walker, copiesPerSide, cells);
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (cells[i].Equals(walker)) continue;
                GluedAvatarPose.InCell(in lattice, walker, cells[i], head, heading,
                                       out float3 position, out float copyHeading, out bool mirrored);
                into[n++] = new GluedAvatarCopy { Cell = cells[i], Position = position, Heading = copyHeading, Mirrored = mirrored };
            }
            return n;
        }
    }
}
