using System.Globalization;
using System.Text;
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
    }

    /// <summary>
    /// The text of the Slicer's panel (task P2-A01, the toybox's read-outs): which body, what its slice is, where the cut is on
    /// the w range, the three turns, the view, and the tour's step. Values are shown at the precision they are drawn with, so
    /// the text is rebuilt only when a shown digit changes. Engine-free; builds into the caller's StringBuilder.
    /// </summary>
    public static class SlicerReadout
    {
        /// <summary>Cells of the w bar.</summary>
        public const int BarCells = 21;

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

        /// <summary>Writes the panel's lines into <paramref name="sb"/> (cleared first).</summary>
        public static void Build(StringBuilder sb, in SlicerReadoutState s)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            sb.Length = 0;
            sb.Append(BodyName(s.Body).ToUpperInvariant()).Append("  (").Append((int)SlicerObjects.Clamp(s.Body) + 1)
              .Append(" of ").Append(SlicerObjects.Count).Append(")\n");
            sb.Append(BodyKind(s.Body)).Append('\n');

            sb.Append("slice: ");
            switch (s.Section.Shape)
            {
                case SectionShape.None: sb.Append("nothing (the cut misses it)"); break;
                case SectionShape.Point: sb.Append("a single point"); break;
                case SectionShape.Flat: sb.Append("flat, no volume"); break;
                case SectionShape.Sphere: sb.Append("a sphere"); break;
                case SectionShape.Polyhedron:
                    sb.Append("a polyhedron, ").Append(s.Section.Faces).Append(" faces"); break;
                default:
                    sb.Append(SectionNamer.Name(s.Section.Shape)).Append(", ").Append(s.Section.Faces).Append(" faces"); break;
            }
            sb.Append('\n');

            sb.Append("w ").Append(s.W >= 0f ? "+" : "-").Append(math.abs(s.W).ToString("0.00", c)).Append("  ");
            AppendBar(sb, s.W);
            sb.Append('\n');

            sb.Append("XW ").Append(Deg(s.Xw)).Append("°   YW ").Append(Deg(s.Yw)).Append("°   ZW ").Append(Deg(s.Zw)).Append("°\n");

            sb.Append("view: ");
            if (s.View == SlicerView.Shadow) sb.Append("slice + shadow");
            else if (s.Peeking) sb.Append("slice + shadow (looking down)");
            else sb.Append("slice");

            if (s.ShowTour)
            {
                sb.Append("\n\n");
                if (s.Task != SlicerTask.Done) sb.Append("Step ").Append((int)s.Task + 1).Append(" of 5: ");
                sb.Append(SlicerTour.PromptOf(s.Task));
            }
        }

        // [----|----] over -CutRange .. +CutRange, the cut's cell drawn as '|'.
        static void AppendBar(StringBuilder sb, float w)
        {
            float t = (SlicerController.ClampCut(w) + SlicerObjects.CutRange) / (2f * SlicerObjects.CutRange);
            int at = (int)math.round(t * (BarCells - 1));
            sb.Append('[');
            for (int i = 0; i < BarCells; i++) sb.Append(i == at ? '|' : '-');
            sb.Append(']');
        }

        static int Deg(float radians) { return (int)math.round(math.degrees(SlicerController.WrapAngle(radians))); }
    }
}
