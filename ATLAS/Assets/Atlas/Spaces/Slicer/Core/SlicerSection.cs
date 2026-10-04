using Atlas.Kernel.FourD;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>
    /// The name of the slice on screen (task P2-A02) for the body in force: a <see cref="SectionNamer"/> for each of the four
    /// regular polytopes, built when the body is chosen. The 4D ball and the breathing tube always cut into a sphere. Engine-free.
    /// <see cref="Bind"/> allocates (a change of body, off the steady frame path); <see cref="Name"/> allocates nothing.
    /// </summary>
    public sealed class SlicerSection
    {
        SectionNamer m_Namer;
        SlicerObject m_Body;

        public SlicerObject Body { get { return m_Body; } }

        /// <summary>True for the bodies whose sections are named by their faces.</summary>
        public static bool IsPolytope(SlicerObject body)
        {
            SlicerObject b = SlicerObjects.Clamp(body);
            return b == SlicerObject.Tesseract || b == SlicerObject.Cell5 || b == SlicerObject.Cell16 || b == SlicerObject.Cell24;
        }

        /// <summary>Takes the body whose sections are named next. Builds its tables: allocates.</summary>
        public void Bind(SlicerObject body)
        {
            m_Body = SlicerObjects.Clamp(body);
            m_Namer = IsPolytope(m_Body) ? new SectionNamer(SlicerObjects.Build(m_Body)) : null;
        }

        /// <summary>The section of the bound body turned by (xw, yw, zw) radians and cut at <paramref name="offset"/>.</summary>
        public SectionInfo Name(float xw, float yw, float zw, float offset, float tiltRadians = 0f)
        {
            if (m_Namer == null) return new SectionInfo { Shape = SectionShape.Sphere };
            SlicerController.GetTransform(m_Body, xw, yw, zw, out float4x4 m, out float4 t);
            return m_Namer.Analyse(in m, in t, SlicerController.CutNormal(m_Body, tiltRadians), offset);
        }

        /// <summary>The section of the bound polytope under any rotation (its rest pose not applied), cut by
        /// dot(normal, p) = offset. A smooth body gives a sphere.</summary>
        public SectionInfo Name(in float4x4 modelToSlice, in float4 translation, in float4 normal, float offset)
        {
            if (m_Namer == null) return new SectionInfo { Shape = SectionShape.Sphere };
            return m_Namer.Analyse(in modelToSlice, in translation, in normal, offset);
        }
    }
}
