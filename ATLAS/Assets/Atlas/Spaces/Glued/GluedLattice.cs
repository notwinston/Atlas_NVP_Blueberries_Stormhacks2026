using System;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>The three gluings of S1 (Master Plan section 3.1). The value is the glued.mode parameter.</summary>
    public enum GluedLayout : byte
    {
        /// <summary>Torus T2: both pairs of edges glued straight.</summary>
        Torus = 0,
        /// <summary>Klein bottle: the X pair straight, the Z pair with a flip in X.</summary>
        Klein = 1,
        /// <summary>3-torus T3: the torus plus a vertical period.</summary>
        Torus3 = 2,
    }

    /// <summary>One cell of the lattice: column along +X, row along +Z, layer along +Y.</summary>
    public struct GluedCell : IEquatable<GluedCell>
    {
        public int Col, Row, Layer;

        public GluedCell(int col, int row, int layer = 0)
        {
            Col = col;
            Row = row;
            Layer = layer;
        }

        public bool Equals(GluedCell other) { return Col == other.Col && Row == other.Row && Layer == other.Layer; }
        public override bool Equals(object obj) { return obj is GluedCell && Equals((GluedCell)obj); }
        public override int GetHashCode() { return (Col * 73856093) ^ (Row * 19349663) ^ (Layer * 83492791); }
        public override string ToString() { return "(" + Col + ", " + Row + ", " + Layer + ")"; }
    }

    /// <summary>
    /// One element of the gluing group, in coordinates centred on the fundamental cell: p goes to
    /// (±x, y, z) + Translation, with the minus sign when Mirror is set. Every deck transformation of the
    /// torus, the Klein bottle and the 3-torus has this form.
    /// </summary>
    public struct GluedElement
    {
        /// <summary>X is negated before the translation.</summary>
        public bool Mirror;
        /// <summary>Metres.</summary>
        public double3 Translation;

        public GluedElement(bool mirror, double3 translation)
        {
            Mirror = mirror;
            Translation = translation;
        }

        public static GluedElement Identity { get { return new GluedElement(false, double3.zero); } }

        /// <summary>Sign of the determinant of the linear part: -1 for a mirrored copy.</summary>
        public int DetSign { get { return Mirror ? -1 : 1; } }

        public double3 Apply(double3 p)
        {
            return new double3(Mirror ? -p.x : p.x, p.y, p.z) + Translation;
        }

        /// <summary>The element that applies b first, then a.</summary>
        public static GluedElement Compose(in GluedElement a, in GluedElement b)
        {
            return new GluedElement(a.Mirror != b.Mirror, a.Apply(b.Translation));
        }

        public GluedElement Inverse()
        {
            double3 t = Translation;
            return new GluedElement(Mirror, new double3(Mirror ? t.x : -t.x, -t.y, -t.z));
        }

        /// <summary>The element applied n times; a negative n applies the inverse.</summary>
        public GluedElement Power(int n)
        {
            GluedElement step = n < 0 ? Inverse() : this;
            GluedElement result = Identity;
            for (int k = math.abs(n); k > 0; k--) result = Compose(step, result);
            return result;
        }

        /// <summary>Column-vector matrix of the element.</summary>
        public float4x4 ToMatrix()
        {
            float3 t = (float3)Translation;
            return new float4x4(
                Mirror ? -1f : 1f, 0f, 0f, t.x,
                0f, 1f, 0f, t.y,
                0f, 0f, 1f, t.z,
                0f, 0f, 0f, 1f);
        }

        /// <summary>Largest difference between the two elements' translations, or infinity when one is mirrored and the other is not.</summary>
        public static double Distance(in GluedElement a, in GluedElement b)
        {
            if (a.Mirror != b.Mirror) return double.PositiveInfinity;
            return math.cmax(math.abs(a.Translation - b.Translation));
        }
    }

    /// <summary>
    /// The lattice of S1: the universal cover of the glued space, fixed to the floor (Master Plan D15). The
    /// walker simply walks through the copies, so nothing here ever moves the walker. Engine-free.
    /// </summary>
    /// <remarks>
    /// Cell (i, j) covers x from -wf/2 + i * sizeX and z from -df/2 + j * sizeZ, and the lattice continues past
    /// the free rectangle in every direction. The copy in cell (col, row, layer) is A^col B^row C^layer applied
    /// to the fundamental cell (0, 0, 0), where A shifts one cell along X, B shifts one cell along Z (with a
    /// flip in X for the Klein bottle) and C shifts one vertical period (3-torus only).
    /// </remarks>
    public readonly struct GluedLattice
    {
        /// <summary>Vertical period of the 3-torus, metres (Master Plan task P1-A09).</summary>
        public const double VerticalPeriod = 2.6;

        public readonly GluedLayout Layout;
        /// <summary>Free rectangle, metres.</summary>
        public readonly double Wf, Df;
        /// <summary>Cell size, metres.</summary>
        public readonly double SizeX, SizeZ;
        /// <summary>Cells that fit the free rectangle.</summary>
        public readonly int Nx, Nz;

        public GluedLattice(double wf, double df, GluedLayout layout)
        {
            GluedGrid grid = CellGridRules.Glued(wf, df);
            Layout = layout;
            Wf = wf;
            Df = df;
            SizeX = grid.SizeX;
            SizeZ = grid.SizeZ;
            Nx = grid.Nx;
            Nz = grid.Nz;
        }

        /// <summary>The lattice of a room: the cell size is the RoomModel's (cells.glued).</summary>
        public GluedLattice(RoomModel room, GluedLayout layout)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            double wf = Math.Round((double)room.FreeRect.Wf, 6);
            double df = Math.Round((double)room.FreeRect.Df, 6);
            GluedGrid grid = room.Glued;
            // A RoomModel that was never loop-fitted carries no grid; cut it here by the same rule.
            if (!(grid.SizeX > 0.0) || !(grid.SizeZ > 0.0) || grid.Nx < 1 || grid.Nz < 1) grid = CellGridRules.Glued(wf, df);
            Layout = layout;
            Wf = wf;
            Df = df;
            SizeX = grid.SizeX;
            SizeZ = grid.SizeZ;
            Nx = grid.Nx;
            Nz = grid.Nz;
        }

        /// <summary>The same floor grid under another gluing.</summary>
        public GluedLattice(in GluedLattice other, GluedLayout layout)
        {
            Layout = layout;
            Wf = other.Wf;
            Df = other.Df;
            SizeX = other.SizeX;
            SizeZ = other.SizeZ;
            Nx = other.Nx;
            Nz = other.Nz;
        }

        /// <summary>Centre of the fundamental cell (0, 0, 0) on the floor, room frame.</summary>
        public double3 FundamentalCentre
        {
            get { return new double3(-0.5 * Wf + 0.5 * SizeX, 0.0, -0.5 * Df + 0.5 * SizeZ); }
        }

        /// <summary>The shorter cell side, metres: the unit of the fog rule.</summary>
        public double ShortSide { get { return Math.Min(SizeX, SizeZ); } }

        /// <summary>The cell that holds a point of the room frame. The layer is 0 unless the layout is the 3-torus.</summary>
        public GluedCell CellOf(float3 position)
        {
            int col = (int)Math.Floor((position.x + 0.5 * Wf) / SizeX);
            int row = (int)Math.Floor((position.z + 0.5 * Df) / SizeZ);
            int layer = Layout == GluedLayout.Torus3 ? (int)Math.Floor(position.y / VerticalPeriod) : 0;
            return new GluedCell(col, row, layer);
        }

        /// <summary>True when the cell lies inside the free rectangle, where the walker can really stand.</summary>
        public bool InRoom(GluedCell cell)
        {
            return cell.Col >= 0 && cell.Col < Nx && cell.Row >= 0 && cell.Row < Nz;
        }

        /// <summary>Centre of a cell on its own floor, room frame.</summary>
        public double3 CellCentre(GluedCell cell)
        {
            return FundamentalCentre + new double3(cell.Col * SizeX, LayerHeight(cell.Layer), cell.Row * SizeZ);
        }

        /// <summary>One cell along +X.</summary>
        public GluedElement GeneratorA { get { return new GluedElement(false, new double3(SizeX, 0.0, 0.0)); } }

        /// <summary>One cell along +Z; with a flip in X for the Klein bottle.</summary>
        public GluedElement GeneratorB
        {
            get { return new GluedElement(Layout == GluedLayout.Klein, new double3(0.0, 0.0, SizeZ)); }
        }

        /// <summary>One vertical period; the identity unless the layout is the 3-torus.</summary>
        public GluedElement GeneratorC
        {
            get { return new GluedElement(false, new double3(0.0, LayerHeight(1), 0.0)); }
        }

        /// <summary>The copy in a cell, A^col B^row C^layer, in coordinates centred on the fundamental cell.</summary>
        public GluedElement Copy(GluedCell cell)
        {
            return new GluedElement(IsMirrored(cell),
                new double3(cell.Col * SizeX, LayerHeight(cell.Layer), cell.Row * SizeZ));
        }

        /// <summary>A copy in an odd row of the Klein bottle is mirrored in X.</summary>
        public bool IsMirrored(GluedCell cell)
        {
            return Layout == GluedLayout.Klein && (cell.Row & 1) != 0;
        }

        /// <summary>Determinant sign of the copy in a cell: (-1)^row for the Klein bottle, +1 otherwise.</summary>
        public int DetSign(GluedCell cell)
        {
            return IsMirrored(cell) ? -1 : 1;
        }

        /// <summary>The group element that carries the content of one cell onto another.</summary>
        public GluedElement Between(GluedCell from, GluedCell to)
        {
            return GluedElement.Compose(Copy(to), Copy(from).Inverse());
        }

        /// <summary>Where a point of the room frame that lies in cell `from` appears in cell `to`.</summary>
        public float3 ImageOf(GluedCell from, GluedCell to, float3 position, out bool mirrored)
        {
            GluedElement element = Between(from, to);
            double3 centre = FundamentalCentre;
            mirrored = element.Mirror;
            return (float3)(element.Apply((double3)position - centre) + centre);
        }

        /// <summary>
        /// The cells drawn round the walker's cell: n x n in the walker's layer for the torus and the Klein
        /// bottle, 3 x 3 x 3 for the 3-torus. The walker's own cell is among them. Returns the count; the
        /// array must hold <see cref="GluedRules.CellCount"/> entries. Allocates nothing.
        /// </summary>
        public int FillCopies(GluedCell walker, int copiesPerSide, GluedCell[] into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int n = GluedRules.CopiesPerSide(Layout, copiesPerSide);
            int half = (n - 1) / 2;
            int layers = Layout == GluedLayout.Torus3 ? 1 : 0;
            int count = 0;
            for (int dl = -layers; dl <= layers; dl++)
                for (int dr = -half; dr <= half; dr++)
                    for (int dc = -half; dc <= half; dc++)
                        into[count++] = new GluedCell(walker.Col + dc, walker.Row + dr, walker.Layer + dl);
            return count;
        }

        double LayerHeight(int layer)
        {
            return Layout == GluedLayout.Torus3 ? layer * VerticalPeriod : 0.0;
        }
    }

    /// <summary>The counts and distances of S1 that follow from the layout and the quality ladder. Engine-free.</summary>
    public static class GluedRules
    {
        public const int MinCopies = 3, DefaultCopies = 5, MaxCopies = 7;
        /// <summary>The 3-torus is always 3 x 3 x 3.</summary>
        public const int Torus3Copies = 3;
        /// <summary>The most cells ever drawn: 7 x 7 on the high tier.</summary>
        public const int MaxCells = MaxCopies * MaxCopies;

        /// <summary>Names of the glued.mode values; also the SceneCmd variants (lane A contract, section 14.1).</summary>
        public static readonly string[] LayoutNames = { "t2", "klein", "t3" };

        /// <summary>The layout of a glued.mode value; out of range gives the torus.</summary>
        public static GluedLayout LayoutOf(int mode)
        {
            return mode == 1 ? GluedLayout.Klein : mode == 2 ? GluedLayout.Torus3 : GluedLayout.Torus;
        }

        /// <summary>Copies per side as drawn: 3 for the 3-torus, otherwise the request brought to 3, 5 or 7.</summary>
        public static int CopiesPerSide(GluedLayout layout, int requested)
        {
            if (layout == GluedLayout.Torus3) return Torus3Copies;
            int n = math.clamp(requested, MinCopies, MaxCopies);
            return (n & 1) == 0 ? n - 1 : n;
        }

        /// <summary>Cells drawn: n x n, or 27 for the 3-torus.</summary>
        public static int CellCount(GluedLayout layout, int requested)
        {
            int n = CopiesPerSide(layout, requested);
            return layout == GluedLayout.Torus3 ? n * n * n : n * n;
        }

        /// <summary>Copies per side of a rung of the thermal ladder (Master Plan section 8.6).</summary>
        public static int CopiesForTier(Atlas.World.QualityTier tier)
        {
            return Atlas.World.QualityTable.Row(tier).GluedCopies;
        }

        /// <summary>
        /// The fog ramp, metres from the head on the floor (Master Plan section 8.5): opaque at (n - 1) / 2
        /// cells, which is as far as every cell is certain to be drawn, and starting one cell nearer (half a
        /// cell at 3 x 3). A cell is the shorter cell side.
        /// </summary>
        public static void Fog(in GluedLattice lattice, int requested, out float start, out float end)
        {
            int n = CopiesPerSide(lattice.Layout, requested);
            double cell = lattice.ShortSide;
            double far = 0.5 * (n - 1) * cell;
            double near = far - (n <= 3 ? 0.5 : 1.0) * cell;
            start = (float)near;
            end = (float)far;
        }
    }

    /// <summary>
    /// Makes glued.copies follow the thermal ladder: when the tier changes, the parameter takes the copies of
    /// the new rung. Between changes the parameter is the operator's to set. On the first frame of a visit the
    /// rung may only lower the value, so a hot phone never enters with more copies than its rung allows.
    /// </summary>
    public struct GluedTierFollower
    {
        bool m_Seen;
        Atlas.World.QualityTier m_Last;

        /// <summary>The value glued.copies should hold after this frame; equal to `current` when nothing is to change.</summary>
        public int Step(Atlas.World.QualityTier tier, int current)
        {
            int rung = GluedRules.CopiesForTier(tier);
            if (!m_Seen)
            {
                m_Seen = true;
                m_Last = tier;
                return math.min(current, rung);
            }
            if (tier == m_Last) return current;
            m_Last = tier;
            return rung;
        }
    }
}
