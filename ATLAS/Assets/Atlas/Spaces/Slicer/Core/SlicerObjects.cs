using System;
using Atlas.Kernel.FourD;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>The bodies of the toybox. The value is the index of <c>slicer.object</c> and part of State.p[7].</summary>
    public enum SlicerObject : byte
    {
        Tesseract = 0,
        Cell5 = 1,
        Cell16 = 2,
        Cell24 = 3,
        /// <summary>The worldtube of a sphere that moves like a breathing nodule, in R^3 x S^1.</summary>
        Tube = 4,
        Ball = 5,
    }

    /// <summary>Where the slices come from (<c>slicer.source</c>, the fallback ladder of Master Plan section 12.2).</summary>
    public enum SlicerSource : byte
    {
        Live = 0,
        /// <summary>The cut moves in fixed steps, so the slice is rebuilt only when it reaches the next step.</summary>
        Stepped = 1,
        /// <summary>The tesseract whatever <c>slicer.object</c> says: 48 tetrahedra.</summary>
        TesseractOnly = 2,
    }

    /// <summary>What is drawn (<c>slicer.view</c>): the cross-section alone, or with the ghosted shadow round it.</summary>
    public enum SlicerView : byte
    {
        Slice = 0,
        Shadow = 1,
    }

    /// <summary>
    /// The catalogue of 4D bodies of space S6 (Master Plan section 5.5): how each is built, how it stands at
    /// rest, and the colour of each of its cells. Engine-free; the builders run at load time and allocate.
    /// </summary>
    public static class SlicerObjects
    {
        public const int Count = 6;

        /// <summary>The cut offset runs from -CutRange to +CutRange, model units: just past the circumradius of
        /// 1, so every body can be pushed clean through the cut.</summary>
        public const float CutRange = 1.1f;

        /// <summary>Cuts per side of the 4D ball: 48 * 6^3 = 10,368 tetrahedra.</summary>
        public const int BallDivisions = 6;

        /// <summary>The moving sphere: ten phases of one breath give 18,000 tetrahedra.</summary>
        public const int TubePhases = 10;
        public const float TubeDiameter = 0.7f;
        /// <summary>How far the sphere travels along z over the breath, model units.</summary>
        public const float TubeTravel = 0.9f;
        /// <summary>Length of one breath along w: the whole cut range.</summary>
        public const float TubePeriod = 2f * CutRange;

        /// <summary>Entries of the palette table handed to the slicer: one per 4D cell colour.</summary>
        public const int CellUvCount = AtlasPalette.Cell4DCount;

        static readonly string[] s_names = { "tesseract", "cell5", "cell16", "cell24", "tube", "ball" };

        /// <summary>Names of <c>slicer.object</c>, in value order. A copy: the caller may keep it.</summary>
        public static string[] Names() { return (string[])s_names.Clone(); }

        public static string Name(SlicerObject body) { return s_names[(int)Clamp(body)]; }

        /// <summary>The nearest defined body.</summary>
        public static SlicerObject Clamp(SlicerObject body)
        {
            return (int)body >= Count ? SlicerObject.Tesseract : body;
        }

        /// <summary>The next body in the list, wrapping.</summary>
        public static SlicerObject Next(SlicerObject body)
        {
            return (SlicerObject)(((int)Clamp(body) + 1) % Count);
        }

        /// <summary>Builds the body's tetrahedra. Allocates; call when the body is chosen, never per frame.</summary>
        public static Mesh4D Build(SlicerObject body)
        {
            switch (Clamp(body))
            {
                case SlicerObject.Cell5: return Polytopes4D.Cell5();
                case SlicerObject.Cell16: return Polytopes4D.Cell16();
                case SlicerObject.Cell24: return Polytopes4D.Cell24();
                case SlicerObject.Ball: return Polytopes4D.Ball(BallDivisions);
                case SlicerObject.Tube:
                    return Polytopes4D.MovingSphere(TubeDiameter,
                        Polytopes4D.OffsetsBreathing(TubePhases, TubeTravel), TubePeriod);
                default: return Polytopes4D.Tesseract();
            }
        }

        /// <summary>Number of tetrahedra of a body, known without building it.</summary>
        public static int TetCount(SlicerObject body)
        {
            switch (Clamp(body))
            {
                case SlicerObject.Cell5: return 5;
                case SlicerObject.Cell16: return 16;
                case SlicerObject.Cell24: return 96;
                case SlicerObject.Ball: return 48 * BallDivisions * BallDivisions * BallDivisions;
                case SlicerObject.Tube: return 600 * 3 * TubePhases;
                default: return 48;
            }
        }

        /// <summary>
        /// How the body stands before the walker turns it. The tesseract stands on a corner, so that pushing
        /// it through the cut gives the tetrahedron, the truncated tetrahedron and the octahedron of Master Plan
        /// section 5.1 instead of a cube that never changes.
        /// </summary>
        public static double4x4 BaseRotation(SlicerObject body)
        {
            return Clamp(body) == SlicerObject.Tesseract ? Rotation4D.CornerFirst() : double4x4.identity;
        }

        /// <summary>Added to every model vertex before the rotation, so that the body is centred on the origin
        /// and turns about its own middle. Only the tube is built off-centre.</summary>
        public static float4 Centring(SlicerObject body)
        {
            return Clamp(body) == SlicerObject.Tube
                ? new float4(0f, 0f, 0.5f * TubeTravel, -0.5f * TubePeriod)
                : float4.zero;
        }

        /// <summary>The tube is a body in space and time: its shadow is taken straight along w (the volume the
        /// sphere ever occupies). The polytopes get the perspective shadow, the cube within a cube.</summary>
        public static bool PerspectiveShadow(SlicerObject body)
        {
            return Clamp(body) != SlicerObject.Tube;
        }

        /// <summary>True for the bodies whose cells are flat: their slices have flat faces, one per cell.</summary>
        public static bool IsPolytope(SlicerObject body)
        {
            body = Clamp(body);
            return body != SlicerObject.Tube && body != SlicerObject.Ball;
        }

        /// <summary>
        /// Palette cell of a 4D cell of the body. A polytope's cells take the emissive 4D colours: each face
        /// of its slice is flat and comes from one cell, so the colour alone tells the faces apart. The ball
        /// and the tube are curved, so they take object colours, which carry the baked shade.
        /// </summary>
        public static int PaletteCell(SlicerObject body, int cell)
        {
            if (IsPolytope(body)) return AtlasPalette.Cell4D(cell);
            int index = ((cell % CellUvCount) + CellUvCount) % CellUvCount;
            // Twelve hues, 30 degrees apart. The ball's eight cells step by five so neighbours differ strongly;
            // the tube's phases run round the hue circle in order.
            int column = Clamp(body) == SlicerObject.Ball ? (index * 5) % 12 : index % 12;
            return AtlasPalette.Object(column, 3);
        }

        /// <summary>Fills the table the slicer indexes with <c>cell % CellUvCount</c>. No allocation.</summary>
        public static void FillCellUv(SlicerObject body, float2[] table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            for (int i = 0; i < table.Length; i++) table[i] = AtlasPalette.Uv(PaletteCell(body, i));
        }
    }
}
