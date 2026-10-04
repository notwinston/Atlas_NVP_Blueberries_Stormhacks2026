using Atlas.World;

namespace Atlas.Blades.Playground
{
    /// <summary>One line of an equation card: a label, a live number, a unit. A row whose value is not known
    /// yet (the walk has not started) carries <see cref="Available"/> false and is drawn as a dash.</summary>
    public struct EquationRow
    {
        public string Label;
        public double Value;
        /// <summary>"m", "deg", "rad" or "" for a plain number. Shown after the value.</summary>
        public string Unit;
        public bool Available;
        /// <summary>Fixed text instead of a number ("flat"), or null. Wins over Value.</summary>
        public string Text;

        public static EquationRow Number(string label, double value, string unit)
        {
            return new EquationRow { Label = label, Value = value, Unit = unit, Available = !double.IsNaN(value) && !double.IsInfinity(value) };
        }

        public static EquationRow Words(string label, string text)
        {
            return new EquationRow { Label = label, Text = text, Available = true };
        }
    }

    /// <summary>
    /// The live numbers behind an exhibit's equation card (Master Plan section 10.1.3, tasks P2-C06 and
    /// P2-A03). Lane C writes the sources of S0, S2, S3 and S4; lane A writes S1, S5 and S6 against this same
    /// interface and registers them in <see cref="EquationSources"/>. Engine-free. A source reads state that is
    /// already there: it allocates nothing in <see cref="Read"/> and runs on the main thread.
    /// </summary>
    public interface IEquationSource
    {
        /// <summary>The space whose card this fills.</summary>
        SpaceId Space { get; }
        /// <summary>The formula, as text. May depend on the dial (S2 at q = 4 is flat).</summary>
        string Template { get; }
        /// <summary>Most rows this source ever returns. The card sizes its buffer from it once.</summary>
        int MaxRows { get; }
        /// <summary>Fills rows[0 .. n) with the current values and returns n (at most MaxRows).</summary>
        int Read(EquationRow[] rows);
    }

    /// <summary>
    /// The 3-4-5 walk as the card reads it. Shaped like lane B's <c>LapWalk</c> (P1-B09) plus the lengths the
    /// space was built with, so that the real kernel binds to it at integration with one small adapter. A
    /// flat space reports Q = 4 and a world turn of zero.
    /// </summary>
    public interface IWalkReadout
    {
        /// <summary>The walk is armed: a lap is being measured.</summary>
        bool Armed { get; }
        /// <summary>Rooms per corner: 3, 4, 5 or 6.</summary>
        int Q { get; }
        /// <summary>Corners reached since the start flag.</summary>
        int Corners { get; }
        bool Closed { get; }
        /// <summary>Metres from the start flag along the geodesic; NaN before the walk starts.</summary>
        double DistanceToFlagM { get; }
        /// <summary>"World turned by N degrees", (-180, 180].</summary>
        double WorldTurnDeg { get; }
        /// <summary>The lap side s, metres (RoomModel.LapSide).</summary>
        double SideM { get; }
        /// <summary>L for q = 5 and 6, R for q = 3; unused for q = 4. Metres.</summary>
        double LengthScaleM { get; }
    }

    /// <summary>Aligned Cells (S3) as the card reads it; shaped like lane B's AlignedCellsModule.</summary>
    public interface ICellsReadout
    {
        /// <summary>The cell side g, metres (RoomModel.Aligned.G).</summary>
        double CellSideM { get; }
        /// <summary>Cell edges crossed since entering.</summary>
        int Crossings { get; }
    }
}
