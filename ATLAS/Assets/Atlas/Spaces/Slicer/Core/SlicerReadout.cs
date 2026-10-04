using System.Globalization;
using System.Text;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Slicer
{
    /// <summary>Everything the Slicer's panel shows, in one value (the panel is redrawn only when it changes).</summary>
    public struct SlicerReadoutState
    {
        public SlicerObject Body;
        public SectionInfo Section;
        /// <summary>The cut offset, model units.</summary>
        public float W;
        /// <summary>The three turns, radians.</summary>
        public float Xw, Yw, Zw;
        public SlicerView View;
        /// <summary>The shadow is shown because the walker looks down.</summary>
        public bool Peeking;
        /// <summary>The tour's step, and whether the tour is shown.</summary>
        public SlicerTask Task;
        public bool ShowTour;
        /// <summary>The shapes along w for the body as turned, or null.</summary>
        public SlicerShapeStrip Strip;
    }

    /// <summary>
    /// The text of the Slicer's info card (task P2-A01, the toybox's read-outs), as TextMeshPro rich text in two blocks around
    /// the shape strip: the body and its slice as the headline, then the shapes met along w, the cut, the turns, the view and
    /// the tour's step. Values are shown at the precision they are drawn with, so the text is rebuilt only when a shown digit
    /// changes (or the strip does). Engine-free; builds into the caller's StringBuilder without allocating.
    /// </summary>
    public static class SlicerReadout
    {
        static readonly string[] s_bodies = { "Tesseract", "5-cell", "16-cell", "24-cell", "Breathing tube", "4D ball" };
        static readonly string[] s_kinds =
        {
            "the 4D cube", "the 4D tetrahedron", "the 4D octahedron", "a 4D shape with no 3D twin",
            "a sphere moving through time", "the 4D sphere",
        };

        /// <summary>The body's name on the panel.</summary>
        public static string BodyName(SlicerObject body) { return s_bodies[(int)SlicerObjects.Clamp(body)]; }
        /// <summary>One line on what the body is.</summary>
        public static string BodyKind(SlicerObject body) { return s_kinds[(int)SlicerObjects.Clamp(body)]; }

        /// <summary>A key that changes exactly when the panel's text would: compare it before rebuilding.</summary>
        public static long Key(in SlicerReadoutState s)
        {
            long k = (long)SlicerObjects.Clamp(s.Body);
            k = k * 32 + (long)s.Section.Shape;
            k = k * 64 + s.Section.Faces;
            k = k * 512 + (long)math.round(s.W * 100f) + 256;
            k = k * 8 + (long)s.Task;
            k = k * 4 + (s.View == SlicerView.Shadow ? 1 : 0) + (s.Peeking ? 2 : 0);
            k = k * 2 + (s.ShowTour ? 1 : 0);
            k = k * 400 + Deg(s.Xw) + 200;
            k = k * 400 + Deg(s.Yw) + 200;
            k = k * 400 + Deg(s.Zw) + 200;
            return k;
        }

        /// <summary>The whole card, header then footer: what a test or a log reads.</summary>
        public static void Build(StringBuilder sb, in SlicerReadoutState s)
        {
            sb.Length = 0;
            AppendHeader(sb, in s);
            sb.Append('\n');
            AppendFooter(sb, in s);
        }

        /// <summary>The card's top block (cleared first): the body, and the slice as the headline. Rich text.</summary>
        public static void BuildHeader(StringBuilder sb, in SlicerReadoutState s)
        {
            sb.Length = 0;
            AppendHeader(sb, in s);
        }

        /// <summary>The card's block under the strip (cleared first): the strip's legend, the cut, the turns, the view and the
        /// tour's step. Rich text.</summary>
        public static void BuildFooter(StringBuilder sb, in SlicerReadoutState s)
        {
            sb.Length = 0;
            AppendFooter(sb, in s);
        }

        static void AppendHeader(StringBuilder sb, in SlicerReadoutState s)
        {
            sb.Append("<size=160%>").Append(BodyName(s.Body).ToUpperInvariant()).Append("</size>")
              .Append("<size=80%>").Append(Dim).Append("    ").Append((int)SlicerObjects.Clamp(s.Body) + 1)
              .Append(" of ").Append(SlicerObjects.Count).Append("</color></size>\n");
            sb.Append(Dim).Append(BodyKind(s.Body)).Append("</color>\n");
            sb.Append("<size=40%> </size>\n");

            sb.Append("<size=75%>").Append(Dim).Append("SLICE</color></size>\n");
            sb.Append("<size=150%>").Append(ShapeColour(s.Section.Shape));
            switch (s.Section.Shape)
            {
                case SectionShape.None: sb.Append("Nothing"); break;
                case SectionShape.Point: sb.Append("A single point"); break;
                case SectionShape.Flat: sb.Append("Flat"); break;
                case SectionShape.Sphere: sb.Append("Sphere"); break;
                default: AppendCapitalised(sb, SectionNamer.Name(s.Section.Shape)); break;
            }
            sb.Append("</color></size>\n");
            AppendFaces(sb, in s.Section);
            sb.Append('\n');
            sb.Append("<size=40%> </size>\n");
            sb.Append("<size=75%>").Append(Dim).Append("SHAPES ALONG w</color></size>");
        }

        static void AppendFooter(StringBuilder sb, in SlicerReadoutState s)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            SlicerShapeStrip strip = s.Strip;
            sb.Append("<size=85%>");
            if (strip == null || strip.KindCount == 0) sb.Append(Dim).Append("the cut misses it everywhere</color>");
            else
            {
                for (int i = 0; i < strip.KindCount; i++)
                {
                    if (i > 0) sb.Append(Dim).Append("  ·  </color>");
                    SectionShape kind = strip.Kind(i);
                    sb.Append(ShapeColour(kind)).Append(SectionNamer.Name(kind)).Append("</color>");
                }
            }
            sb.Append("</size>\n<size=50%> </size>\n");

            sb.Append(Dim).Append("w </color>").Append(s.W >= 0f ? "+" : "-").Append(math.abs(s.W).ToString("0.00", c))
              .Append(Dim).Append("     XW </color>").Append(Deg(s.Xw)).Append('°')
              .Append(Dim).Append("   YW </color>").Append(Deg(s.Yw)).Append('°')
              .Append(Dim).Append("   ZW </color>").Append(Deg(s.Zw)).Append("°\n");
            sb.Append(Dim).Append("view </color>");
            if (s.View == SlicerView.Shadow) sb.Append("slice + shadow");
            else if (s.Peeking) sb.Append("slice + shadow (looking down)");
            else sb.Append("slice");

            if (s.ShowTour)
            {
                sb.Append("\n<size=50%> </size>\n").Append(Lit);
                if (s.Task != SlicerTask.Done) sb.Append("Step ").Append((int)s.Task + 1).Append(" of 5   ");
                else sb.Append("All steps done   ");
                sb.Append("</color>").Append(SlicerTour.PromptOf(s.Task));
            }
        }

        // "8 faces:  4 triangles + 4 hexagons", or what the slice is when it has no faces.
        static void AppendFaces(StringBuilder sb, in SectionInfo f)
        {
            switch (f.Shape)
            {
                case SectionShape.None: sb.Append(Dim).Append("the cut misses the body</color>"); return;
                case SectionShape.Point: sb.Append(Dim).Append("the cut just touches a corner</color>"); return;
                case SectionShape.Flat: sb.Append(Dim).Append("the cut lies along a face: no volume</color>"); return;
                case SectionShape.Sphere: sb.Append(Dim).Append("round: no faces, no corners</color>"); return;
            }
            sb.Append(f.Faces).Append(" faces");
            int parts = 0;
            parts = Part(sb, parts, f.Triangles, "triangle", "triangles");
            parts = Part(sb, parts, f.Quads, "4-sided", "4-sided");
            parts = Part(sb, parts, f.Pentagons, "pentagon", "pentagons");
            parts = Part(sb, parts, f.Hexagons, "hexagon", "hexagons");
            Part(sb, parts, f.Others, "other", "others");
        }

        static int Part(StringBuilder sb, int parts, int count, string one, string many)
        {
            if (count <= 0) return parts;
            sb.Append(Dim).Append(parts == 0 ? ":  " : " + ").Append("</color>").Append(count).Append(' ').Append(count == 1 ? one : many);
            return parts + 1;
        }

        static void AppendCapitalised(StringBuilder sb, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            sb.Append(char.ToUpperInvariant(name[0])).Append(name, 1, name.Length - 1);
        }

        // Rich-text colour tags, made once.
        const string Dim = "<color=#8C99A6>", Lit = "<color=#FFDB4D>";
        static readonly string[] s_shapeColours = MakeShapeColours();

        /// <summary>The opening colour tag of a shape: its palette colour, a little lighter, as the strip draws it.</summary>
        public static string ShapeColour(SectionShape shape)
        {
            int i = (int)shape;
            return (uint)i < (uint)s_shapeColours.Length ? s_shapeColours[i] : s_shapeColours[s_shapeColours.Length - 1];
        }

        static string[] MakeShapeColours()
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            int count = (int)SectionShape.Polyhedron + 1;
            var tags = new string[count];
            for (int i = 0; i < count; i++)
            {
                float3 rgb = math.saturate(math.lerp(AtlasPalette.Srgb(SlicerShapeStrip.Colour((SectionShape)i)), new float3(1f), 0.3f));
                int r = (int)math.round(rgb.x * 255f), g = (int)math.round(rgb.y * 255f), b = (int)math.round(rgb.z * 255f);
                tags[i] = "<color=#" + r.ToString("X2", c) + g.ToString("X2", c) + b.ToString("X2", c) + ">";
            }
            return tags;
        }

        static int Deg(float radians) { return (int)math.round(math.degrees(SlicerController.WrapAngle(radians))); }
    }
}
