using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// What the slice is at every cut along w for the body as it is turned now (the info card's strip, task P2-A01): the
    /// walker sees where the shape changes before walking there. <see cref="Samples"/> cuts from -CutRange to +CutRange, named
    /// again only when the body, the tilt or a turn moved by more than <see cref="TurnStep"/>. Each shape has one palette
    /// colour, shared by the strip, the legend and the slice's name on the card. Engine-free, no allocation.
    /// </summary>
    public sealed class SlicerShapeStrip
    {
        /// <summary>Odd, so that one sample cuts exactly through the middle (w = 0), where the most symmetric slices are.</summary>
        public const int Samples = 49;
        /// <summary>A turn smaller than this (radians, half a degree) leaves the strip as it is.</summary>
        public const float TurnStep = 0.0087f;

        readonly SectionShape[] m_Shapes = new SectionShape[Samples];
        readonly SectionShape[] m_Kinds = new SectionShape[Samples];
        SlicerObject m_Body;
        float3 m_Turns;
        float m_Tilt;
        bool m_Valid;

        /// <summary>Goes up by one each time the strip changes.</summary>
        public int Version { get; private set; }
        /// <summary>The distinct shapes along the strip, in the order met walking from -w to +w (the misses left out).</summary>
        public int KindCount { get; private set; }
        public SectionShape Kind(int i) { return m_Kinds[i]; }
        public SectionShape this[int i] { get { return m_Shapes[i]; } }

        /// <summary>The cut offset of sample i, model units.</summary>
        public static float SampleW(int i)
        {
            return math.lerp(-SlicerObjects.CutRange, SlicerObjects.CutRange, i / (float)(Samples - 1));
        }

        /// <summary>Where cut offset w falls along the strip, 0 .. 1.</summary>
        public static float Along(float w)
        {
            return math.saturate((w + SlicerObjects.CutRange) / (2f * SlicerObjects.CutRange));
        }

        /// <summary>Forgets the strip: the next <see cref="Update"/> names every sample again.</summary>
        public void Invalidate() { m_Valid = false; }

        /// <summary>Names the samples again if the body, the tilt or a turn moved. True when the strip changed.</summary>
        public bool Update(SlicerSection section, float xw, float yw, float zw, float tilt)
        {
            var turns = new float3(xw, yw, zw);
            if (m_Valid && section.Body == m_Body && tilt == m_Tilt && math.cmax(math.abs(turns - m_Turns)) < TurnStep)
                return false;
            m_Valid = true;
            m_Body = section.Body;
            m_Turns = turns;
            m_Tilt = tilt;

            bool changed = false;
            int kinds = 0;
            for (int i = 0; i < Samples; i++)
            {
                SectionShape shape = section.Name(xw, yw, zw, SampleW(i), tilt).Shape;
                if (shape != m_Shapes[i]) changed = true;
                m_Shapes[i] = shape;
                if (shape == SectionShape.None) continue;
                bool seen = false;
                for (int k = 0; k < kinds && !seen; k++) seen = m_Kinds[k] == shape;
                if (!seen) m_Kinds[kinds++] = shape;
            }
            if (kinds != KindCount) changed = true;
            KindCount = kinds;
            if (changed || Version == 0) Version++;
            return changed;
        }

        /// <summary>The palette cell of a shape: one hue each for the named solids, grey for the rest.</summary>
        public static int Colour(SectionShape shape)
        {
            int column;
            switch (shape)
            {
                case SectionShape.Cuboctahedron: column = 0; break;
                case SectionShape.TruncatedTetrahedron: column = 1; break;
                case SectionShape.SquarePyramid: column = 2; break;
                case SectionShape.Box: column = 3; break;
                case SectionShape.Cube: column = 4; break;
                case SectionShape.RhombicDodecahedron: column = 5; break;
                case SectionShape.Tetrahedron: column = 6; break;
                case SectionShape.TriangularPrism: column = 7; break;
                case SectionShape.Sphere: column = 7; break;
                case SectionShape.TruncatedOctahedron: column = 8; break;
                case SectionShape.Octahedron: column = 9; break;
                case SectionShape.PentagonalPrism: column = 10; break;
                case SectionShape.HexagonalPrism: column = 11; break;
                default: column = 15; break;
            }
            return AtlasPalette.Object(column, 3);
        }
    }
}
