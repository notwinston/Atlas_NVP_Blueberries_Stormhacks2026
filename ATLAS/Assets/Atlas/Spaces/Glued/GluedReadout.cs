using System;
using System.Globalization;
using System.Text;
using Atlas.Rendering;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>Everything the Glued info card shows, in one value (the card redraws only what changed).</summary>
    public struct GluedReadoutState
    {
        public GluedLayout Layout;
        /// <summary>Copies per side drawn: 3 to 7, 3 for the 3-torus.</summary>
        public int Copies;
        /// <summary>The walker's cell.</summary>
        public GluedCell Cell;
        /// <summary>x mod a, z mod b, a, b, the wraps and the parity of the walker's cell.</summary>
        public GluedCardValues Card;
        /// <summary>The step in force and its line; ShowTour is false on the Station, which runs no steps.</summary>
        public GluedTask Task;
        public string Step;
        public bool ShowTour;
    }

    /// <summary>
    /// The text of the Glued info card (Master Plan section 10.1.3, the S1 equation card: "x mod a, z mod b; cell a x b;
    /// wraps; copy parity (-1)^row"), as TextMeshPro rich text in three blocks: the gluing and its rule over the card,
    /// the live numbers beside the lattice plan, and the step line under them. Values are shown at the precision they
    /// are drawn with, so a block is rebuilt only when a shown digit changes. Engine-free; builds into the caller's
    /// StringBuilder without allocating.
    /// </summary>
    public static class GluedReadout
    {
        static readonly string[] s_titles = { "TORUS  T²", "KLEIN BOTTLE", "3-TORUS  T³" };
        // One line each under the headline: at most 48 characters at the rule's size fit the card.
        static readonly string[] s_rules =
        {
            "Each wall leads back in through the opposite one.",
            "The same, but front and back flip you left-right.",
            "The torus, with the ceiling glued to the floor.",
        };

        /// <summary>The longest rule or numbers line the card's width takes, characters at their size on the card.</summary>
        public const int MaxRuleLength = 50, MaxNumbersLength = 26;

        /// <summary>The gluing's name on the card.</summary>
        public static string Title(GluedLayout layout) { return s_titles[Index(layout)]; }
        /// <summary>One line on how the walls are glued.</summary>
        public static string Rule(GluedLayout layout) { return s_rules[Index(layout)]; }

        /// <summary>Changes exactly when the header would: the gluing and the cell sides (to the centimetre).</summary>
        public static long HeaderKey(in GluedReadoutState s)
        {
            // At most 3 * 10^10: well inside 64 bits.
            return ((long)Index(s.Layout) * 100000L + Cm(s.Card.A)) * 100000L + Cm(s.Card.B);
        }

        /// <summary>Changes exactly when the numbers would: x mod a and z mod b to the centimetre, the cell, the wraps, the parity.</summary>
        public static long NumbersKey(in GluedReadoutState s)
        {
            // Masks keep the key inside 64 bits; a field that steps by one still changes the key.
            long k = Cm(s.Card.XMod) & 1023;
            k = k * 1024 + (Cm(s.Card.ZMod) & 1023);
            k = k * 256 + (s.Cell.Col & 255);
            k = k * 256 + (s.Cell.Row & 255);
            k = k * 64 + (s.Cell.Layer & 63);
            k = k * 4096 + (s.Card.Wraps & 4095);
            k = k * 2 + (s.Card.Parity < 0 ? 1 : 0);
            return k * 4 + Index(s.Layout);
        }

        /// <summary>Changes exactly when the step line would.</summary>
        public static long StepKey(in GluedReadoutState s, int stepVersion)
        {
            return ((long)stepVersion * 8 + (long)s.Task) * 2 + (s.ShowTour ? 1 : 0);
        }

        /// <summary>The whole card, header, numbers and step: what a test or a log reads.</summary>
        public static void Build(StringBuilder sb, in GluedReadoutState s)
        {
            sb.Length = 0;
            AppendHeader(sb, in s);
            sb.Append('\n');
            AppendNumbers(sb, in s);
            sb.Append('\n');
            AppendStep(sb, in s);
        }

        /// <summary>The card's top block (cleared first): the gluing, which of the three it is, and its rule.</summary>
        public static void BuildHeader(StringBuilder sb, in GluedReadoutState s)
        {
            sb.Length = 0;
            AppendHeader(sb, in s);
        }

        /// <summary>The live numbers beside the plan (cleared first).</summary>
        public static void BuildNumbers(StringBuilder sb, in GluedReadoutState s)
        {
            sb.Length = 0;
            AppendNumbers(sb, in s);
        }

        /// <summary>The step line under the plan (cleared first); empty when the steps are not shown.</summary>
        public static void BuildStep(StringBuilder sb, in GluedReadoutState s)
        {
            sb.Length = 0;
            AppendStep(sb, in s);
        }

        static void AppendHeader(StringBuilder sb, in GluedReadoutState s)
        {
            sb.Append("<size=150%>").Append(Title(s.Layout)).Append("</size>")
              .Append("<size=80%>").Append(Dim).Append("    ").Append(Index(s.Layout) + 1).Append(" of 3</color></size>\n");
            sb.Append("<size=85%>").Append(Dim).Append(Rule(s.Layout)).Append("</color></size>");
        }

        static void AppendNumbers(StringBuilder sb, in GluedReadoutState s)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            GluedCardValues v = s.Card;
            sb.Append(Dim).Append("x mod a  </color>").Append(v.XMod.ToString("0.00", c)).Append(" m\n");
            sb.Append(Dim).Append("z mod b  </color>").Append(v.ZMod.ToString("0.00", c)).Append(" m\n");
            sb.Append(Dim).Append("cell a × b  </color>").Append(v.A.ToString("0.00", c)).Append(" × ")
              .Append(v.B.ToString("0.00", c)).Append(" m\n");
            sb.Append(Dim).Append("cell  </color>(").Append(s.Cell.Col).Append(", ").Append(s.Cell.Row);
            if (s.Layout == GluedLayout.Torus3) sb.Append(", ").Append(s.Cell.Layer);
            sb.Append(")\n");
            sb.Append(Dim).Append("wraps  </color>").Append(v.Wraps).Append('\n');
            sb.Append(Dim).Append("(−1)<sup>row</sup>  </color>");
            if (v.Parity < 0) sb.Append(Mirror).Append("−1  mirrored</color>");
            else sb.Append("+1  ").Append(Dim).Append("same hand</color>");
        }

        static void AppendStep(StringBuilder sb, in GluedReadoutState s)
        {
            if (!s.ShowTour) return;
            sb.Append(Lit);
            if (s.Task != GluedTask.Done) sb.Append("Step ").Append((int)s.Task + 1).Append(" of ").Append(GluedText.StepCount).Append("   ");
            else sb.Append("All steps done   ");
            sb.Append("</color>").Append(s.Step);
        }

        // Rich-text colour tags, made once: the dim of the labels, the yellow of the step, the violet of a mirrored row.
        const string Dim = "<color=#8C99A6>", Lit = "<color=#FFDB4D>", Mirror = "<color=#B9A6FF>";

        static int Index(GluedLayout layout) { return math.clamp((int)layout, 0, 2); }

        static long Cm(double metres)
        {
            if (double.IsNaN(metres) || metres <= 0.0) return 0;
            return Math.Min((long)Math.Round(metres * 100.0), 99999L);
        }
    }

    /// <summary>
    /// The lattice plan of the info card: the flat twin of S1 (Master Plan section 3.1, "lattice plan with the fundamental
    /// cell"). One square per copy drawn round the walker, laid out as the copies are; the cells of the real free floor
    /// lighter than the copies beyond the walls; mirrored Klein rows in violet; the fundamental cell framed in the
    /// landmark's gold; and in every cell a two-coloured bar, green on the left and blue on the right as the copy's controllers,
    /// which swaps in a mirrored row. The plan lies in the card's plane, z = 0 facing -z, centred on the origin, with the
    /// room's +X to the right and +Z up. The walker's dot (<see cref="BuildDot"/>) goes into the same mesh. Engine-free.
    /// </summary>
    public static class GluedPlan
    {
        /// <summary>The gap between two squares and the gold frame round the fundamental cell, as shares of a cell.</summary>
        public const float Gap = 0.1f, Frame = 0.08f;
        /// <summary>Most triangles a plan can take: 7 x 7 squares, each with its bar, the frame and the dot.</summary>
        public const int MaxTriangles = GluedRules.MaxCells * 6 + 4;

        /// <summary>The pitch of the plan's squares along x and y, metres: the cell's aspect, the longer side fitting `size` / n.</summary>
        public static float2 Pitch(in GluedLattice lattice, int copiesPerSide, float size)
        {
            int n = GluedRules.CopiesPerSide(lattice.Layout, copiesPerSide);
            double a = lattice.SizeX, b = lattice.SizeZ, longer = Math.Max(a, b);
            float pitch = size / n;
            return new float2(pitch * (float)(a / longer), pitch * (float)(b / longer));
        }

        /// <summary>Appends the plan of the copies round `walker` (its layer only, for the 3-torus).</summary>
        public static void Build(AtlasMeshBuilder into, in GluedLattice lattice, GluedCell walker, int copiesPerSide, float size)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            int n = GluedRules.CopiesPerSide(lattice.Layout, copiesPerSide);
            int half = (n - 1) / 2;
            float2 pitch = Pitch(in lattice, copiesPerSide, size);
            float2 fill = pitch * (1f - Gap);
            for (int dr = -half; dr <= half; dr++)
            {
                for (int dc = -half; dc <= half; dc++)
                {
                    var cell = new GluedCell(walker.Col + dc, walker.Row + dr, walker.Layer);
                    var centre = new float2(dc * pitch.x, dr * pitch.y);
                    bool mirrored = lattice.IsMirrored(cell);
                    bool inRoom = lattice.InRoom(cell);
                    int colour = mirrored ? (inRoom ? AtlasPalette.Booth : AtlasPalette.VirtualWall)
                                          : (inRoom ? AtlasPalette.Post : AtlasPalette.Kerb);
                    if (cell.Col == 0 && cell.Row == 0 && cell.Layer == 0)
                    {
                        // The fundamental cell: a gold frame a little behind its square.
                        float2 framed = fill + 2f * Frame * pitch;
                        Rect(into, centre, framed, 0.001f, AtlasPalette.Landmark);
                    }
                    Rect(into, centre, fill, 0f, colour);

                    // The hand of the copy: green left and blue right as its controllers, swapped in a mirrored row.
                    float2 bar = new float2(0.28f * fill.x, 0.16f * fill.y);
                    float2 low = centre + new float2(0f, -0.22f * fill.y);
                    int left = mirrored ? GluedAvatarMesh.RightHand : GluedAvatarMesh.LeftHand;
                    int right = mirrored ? GluedAvatarMesh.LeftHand : GluedAvatarMesh.RightHand;
                    Rect(into, low + new float2(-0.5f * bar.x, 0f), bar, -0.001f, left);
                    Rect(into, low + new float2(0.5f * bar.x, 0f), bar, -0.001f, right);
                }
            }
        }

        /// <summary>
        /// Where the walker's dot lies on the plan: in the middle square (the walker's cell), at their place inside the
        /// cell (x mod a, z mod b).
        /// </summary>
        public static float2 Dot(in GluedLattice lattice, int copiesPerSide, float size, in GluedCardValues card)
        {
            float2 pitch = Pitch(in lattice, copiesPerSide, size);
            double u = card.A > 0.0 ? card.XMod / card.A : 0.5;
            double v = card.B > 0.0 ? card.ZMod / card.B : 0.5;
            return new float2((float)(u - 0.5) * pitch.x, (float)(v - 0.5) * pitch.y) * (1f - Gap);
        }

        /// <summary>
        /// The walker's dot: a small arrowhead at `at` on the plan, pointing the way the walker faces. The plan's up is the
        /// room's +Z, so a heading (radians about +Y, 0 along +Z) turns the arrow clockwise. It is part of the plan's mesh,
        /// which saves the card a draw call; the plan is rebuilt when the dot moves.
        /// </summary>
        public static void BuildDot(AtlasMeshBuilder into, float size, int colour, float2 at, float heading)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            float s = 0.5f * size;
            math.sincos(heading, out float sin, out float cos);
            var normal = new float3(0f, 0f, -1f);
            float2 uv = AtlasPalette.Uv(colour);
            int first = into.VertexCount;
            AddDotVertex(into, new float2(0f, s), at, sin, cos, normal, uv);
            AddDotVertex(into, new float2(0.75f * s, -s), at, sin, cos, normal, uv);
            AddDotVertex(into, new float2(0f, -0.45f * s), at, sin, cos, normal, uv);
            AddDotVertex(into, new float2(-0.75f * s, -s), at, sin, cos, normal, uv);
            // Seen from -z with x right and y up, a clockwise walk is Unity's front face; a turn keeps it clockwise.
            into.AddTriangle(first, first + 1, first + 2);
            into.AddTriangle(first, first + 2, first + 3);
        }

        // A clockwise turn by the heading (whose sine and cosine are given), then the move to `at`, in front of the squares.
        static void AddDotVertex(AtlasMeshBuilder into, float2 p, float2 at, float sin, float cos, float3 normal, float2 uv)
        {
            var turned = new float2(p.x * cos + p.y * sin, -p.x * sin + p.y * cos);
            into.AddVertex(new float3(at.x + turned.x, at.y + turned.y, -0.002f), normal, uv, float2.zero, 1f, 0);
        }

        // A rectangle about `centre` in the plane z, facing -z.
        static void Rect(AtlasMeshBuilder into, float2 centre, float2 size, float z, int colour)
        {
            var origin = new float3(centre.x - 0.5f * size.x, centre.y - 0.5f * size.y, z);
            into.AddQuad(origin, new float3(size.x, 0f, 0f), new float3(0f, size.y, 0f), AtlasPalette.Uv(colour));
        }
    }

    /// <summary>
    /// Where the info card is carried: at the waist, in front of the walker, read by glancing down (Master Plan section
    /// 10.1.4: UI anchored to the floor or the body, never warped). S1's fog is opaque one cell away in the 3-torus, so
    /// a card fixed to a wall would be lost in it; this one stays within reach. It walks with the walker and turns
    /// lazily: only once the heading has left it by more than <see cref="FollowBeyondDeg"/>, so looking round, and
    /// dwelling on its buttons, do not swing it. Engine-free.
    /// </summary>
    public struct GluedCardAnchor
    {
        /// <summary>How far ahead of the head and how far below it the card's centre is, and its lowest height, metres.</summary>
        public const float Reach = 0.4f, Drop = 0.5f, MinHeight = 0.5f;
        /// <summary>The card turns after the heading once they differ by this much, at this rate, until within the settle angle.</summary>
        public const float FollowBeyondDeg = 40f, TurnDegPerSecond = 150f, SettleDeg = 3f;

        float m_Yaw;
        bool m_Placed, m_Turning;

        /// <summary>The card's own heading, radians about +Y (0 along +Z).</summary>
        public float Yaw { get { return m_Yaw; } }

        /// <summary>The next Step puts the card straight in front of the walker.</summary>
        public void Reset()
        {
            m_Placed = false;
            m_Turning = false;
        }

        /// <summary>One frame: the card's centre for this head and heading (radians about +Y, Unity's sense).</summary>
        public float3 Step(float3 head, float heading, float deltaTime)
        {
            if (!m_Placed)
            {
                m_Yaw = heading;
                m_Placed = true;
            }
            else
            {
                float diff = Wrap(heading - m_Yaw);
                if (math.abs(diff) > math.radians(FollowBeyondDeg)) m_Turning = true;
                if (m_Turning)
                {
                    float step = math.radians(TurnDegPerSecond) * math.max(0f, deltaTime);
                    if (math.abs(diff) <= math.max(step, math.radians(SettleDeg)))
                    {
                        m_Yaw = heading;
                        m_Turning = false;
                    }
                    else m_Yaw = Wrap(m_Yaw + math.sign(diff) * step);
                }
            }
            math.sincos(m_Yaw, out float sin, out float cos);
            return new float3(head.x + Reach * sin, math.max(MinHeight, head.y - Drop), head.z + Reach * cos);
        }

        static float Wrap(float radians)
        {
            return radians - 2f * math.PI * math.floor((radians + math.PI) / (2f * math.PI));
        }
    }
}
