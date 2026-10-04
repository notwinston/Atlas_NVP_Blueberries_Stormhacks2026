using System;
using System.Collections.Generic;
using Atlas.World;

namespace Atlas.App
{
    /// <summary>The scripted runs of Master Plan task P1-A14. The value is the index of <c>perf.run</c>.</summary>
    public enum PerfRunKind : byte
    {
        None = 0,
        /// <summary>Every built space in turn on the budget fixture (section 11.2, "per space").</summary>
        Tour = 1,
        /// <summary>The 15-minute soak of section 11.4.1, LiDAR depth on.</summary>
        Soak = 2,
        /// <summary>The same soak with depth off, driven by a pose track with nobody in the headset.</summary>
        SoakDepthOff = 3,
        /// <summary>Each quality tier forced in turn, the time until it is in force measured (task P1-A07).</summary>
        Ladder = 4,
    }

    public enum PerfStepKind : byte
    {
        /// <summary>Enter the space and stay with no measurement: the ladder check's stage.</summary>
        Enter = 0,
        /// <summary>Enter the space, let it settle, then measure for the step's seconds.</summary>
        Visit = 1,
        /// <summary>Force a quality tier and time how long it takes to be in force, then hold it.</summary>
        ForceTier = 2,
    }

    /// <summary>The layouts of a space that the register measures separately (section 11.2: S1 in both).</summary>
    public enum PerfVariant : byte
    {
        None = 0,
        /// <summary>S1 Glued as a torus, 5 x 5 copies.</summary>
        Glued5x5 = 1,
        /// <summary>S1 Glued as a three-torus, 3 x 3 x 3 copies.</summary>
        GluedT3 = 2,
        /// <summary>S2 Curved Walk on the {4,5} tiling at rho = 3, the room as tile zero (section 11.2).</summary>
        CurvedQ5 = 3,
    }

    /// <summary>One step of a run.</summary>
    public readonly struct PerfStep
    {
        public readonly PerfStepKind Kind;
        public readonly SpaceId Space;
        public readonly PerfVariant Variant;
        /// <summary>Visit: seconds measured, after the settle time. ForceTier: seconds the tier is held.</summary>
        public readonly float Seconds;
        /// <summary>ForceTier: 0 to 4.</summary>
        public readonly int Tier;
        /// <summary>The row's name: "S1 Glued 5x5".</summary>
        public readonly string Label;
        /// <summary>Visit: seconds the head stands still once the space is entered, so a curvature ramp can run.
        /// They are not measured: the step settles for these seconds when they exceed the run's settle time.</summary>
        public readonly float StandSeconds;

        public PerfStep(PerfStepKind kind, SpaceId space, PerfVariant variant, float seconds, int tier, string label,
            float standSeconds = 0f)
        {
            Kind = kind;
            Space = space;
            Variant = variant;
            Seconds = seconds;
            Tier = tier;
            Label = label;
            StandSeconds = standSeconds;
        }
    }

    /// <summary>
    /// A run as data: its steps in order and what the output must say about it. Engine-free; the tour's core
    /// plays it (Master Plan section 11.4.1: "the tour script fixes how often the space changes, and that
    /// interval is recorded with the result").
    /// </summary>
    public sealed class PerfSchedule
    {
        /// <summary>Seconds after a space was entered that are not measured: the fade and the first frames.</summary>
        public const float DefaultSettleSeconds = 3f;
        public const float DefaultVisitSeconds = 30f;
        public const float DefaultSoakSeconds = 900f;
        public const float DefaultSoakIntervalSeconds = 60f;
        public const float DefaultHoldSeconds = 3f;
        /// <summary>A visit shorter than this measures too few frames for a 99th percentile.</summary>
        public const float MinVisitSeconds = 2f;
        /// <summary>Seconds the head stands when Curved Walk is entered: the curvature ramps only while the walker
        /// stands (section 4.6) and takes 4 s (section 10.1.4), and the stand must first be recognised.</summary>
        public const float CurvedStandSeconds = 5f;

        public PerfRunKind Kind { get; private set; }
        public PerfStep[] Steps { get; private set; }
        /// <summary>Lines for the head of the output: what is not built, the soak's interval.</summary>
        public string[] Notes { get; private set; }
        public float SettleSeconds { get; private set; }
        /// <summary>Soak: seconds between two changes of space, settle time included. 0 otherwise.</summary>
        public float IntervalSeconds { get; private set; }
        /// <summary>True for a soak: one row per space over all its visits, and one row for the whole run.</summary>
        public bool Aggregate { get; private set; }
        /// <summary>Settle and measured seconds of all steps: what the run lasts, switches aside.</summary>
        public float TotalSeconds { get; private set; }

        PerfSchedule() { }

        /// <summary>"tour", "soak", "soakNoDepth", "ladder": the names of <c>perf.run</c> and of the output's run column.</summary>
        public static string RunName(PerfRunKind kind)
        {
            switch (kind)
            {
                case PerfRunKind.Tour: return "tour";
                case PerfRunKind.Soak: return "soak";
                case PerfRunKind.SoakDepthOff: return "soakNoDepth";
                case PerfRunKind.Ladder: return "ladder";
                default: return "off";
            }
        }

        public static string VariantName(PerfVariant variant)
        {
            switch (variant)
            {
                case PerfVariant.Glued5x5: return "5x5";
                case PerfVariant.GluedT3: return "3x3x3";
                case PerfVariant.CurvedQ5: return "q5";
                default: return "";
            }
        }

        public static string LabelOf(SpaceId space, PerfVariant variant)
        {
            string name = SpaceIds.Code(space) + " " + SpaceIds.DisplayName(space);
            // Curved Walk has one row in section 11.2: its variant goes in the variant column alone.
            return variant == PerfVariant.None || variant == PerfVariant.CurvedQ5 ? name : name + " " + VariantName(variant);
        }

        /// <summary>
        /// The per-space run: S0 to S6 in order, S1 in both its 5 x 5 and its 3 x 3 x 3 layout, each measured
        /// for visitSeconds. A space that is not in the build is left out and named in the notes.
        /// </summary>
        /// <param name="available">True for a space that can be entered; null takes every space as built.</param>
        public static PerfSchedule Tour(float visitSeconds, Func<SpaceId, bool> available, float settleSeconds = DefaultSettleSeconds)
        {
            float visit = Math.Max(MinVisitSeconds, visitSeconds);
            var steps = new List<PerfStep>(8);
            var notes = new List<string>(4);
            for (int i = 0; i < SpaceIds.Count; i++)
            {
                var space = (SpaceId)i;
                if (available != null && !available(space))
                {
                    notes.Add(LabelOf(space, PerfVariant.None) + " is not in this build: not measured");
                    continue;
                }
                if (space == SpaceId.Glued)
                {
                    steps.Add(Visit(space, PerfVariant.Glued5x5, visit));
                    steps.Add(Visit(space, PerfVariant.GluedT3, visit));
                }
                else if (space == SpaceId.CurvedWalk)
                {
                    steps.Add(Visit(space, PerfVariant.CurvedQ5, visit, CurvedStandSeconds));
                    notes.Add(CurvedNote(settleSeconds));
                }
                else steps.Add(Visit(space, PerfVariant.None, visit));
            }
            notes.Add("visit " + Seconds(visit) + " s per space after " + Seconds(settleSeconds) + " s settle");
            return Make(PerfRunKind.Tour, steps, notes, settleSeconds, 0f, false);
        }

        /// <summary>
        /// The soak of section 11.4.1: S1 Glued 5 x 5 and S2 Curved Walk at q = 5 in turn. In a build without
        /// one of them Home takes its place and the output says so. The run lasts totalSeconds exactly, the
        /// last visit being cut short if it must.
        /// </summary>
        public static PerfSchedule Soak(PerfRunKind kind, float totalSeconds, float intervalSeconds, Func<SpaceId, bool> available,
            float settleSeconds = DefaultSettleSeconds)
        {
            if (kind != PerfRunKind.SoakDepthOff) kind = PerfRunKind.Soak;
            bool curved = available == null || available(SpaceId.CurvedWalk);
            // Curved Walk settles for as long as the head stands, so the ramp is over before a frame is measured.
            float curvedSettle = curved ? SettleOf(settleSeconds, CurvedStandSeconds) : settleSeconds;
            float interval = Math.Max(curvedSettle + MinVisitSeconds, intervalSeconds);
            float total = Math.Max(interval, totalSeconds);
            bool glued = available == null || available(SpaceId.Glued);
            SpaceId first = glued ? SpaceId.Glued : SpaceId.Home;
            SpaceId second = curved ? SpaceId.CurvedWalk : SpaceId.Home;

            var steps = new List<PerfStep>(32);
            var notes = new List<string>(4);
            float left = total;
            int n = 0;
            while (true)
            {
                // The same space twice in a row is one long visit to SpaceLab: only the settle time is spent again.
                SpaceId space = (n & 1) == 0 ? first : second;
                float settle = space == SpaceId.CurvedWalk ? curvedSettle : settleSeconds;
                if (left <= settle + 0.5f) break;
                float span = Math.Min(interval, left);
                if (space == SpaceId.Glued) steps.Add(Visit(space, PerfVariant.Glued5x5, span - settle));
                else if (space == SpaceId.CurvedWalk) steps.Add(Visit(space, PerfVariant.CurvedQ5, span - settle, CurvedStandSeconds));
                else steps.Add(Visit(space, PerfVariant.None, span - settle));
                left -= span;
                n++;
            }
            if (curved) notes.Add(CurvedNote(settleSeconds));
            if (!curved)
                notes.Add("S2 Curved Walk is not in this build: the soak alternates " +
                          LabelOf(first, first == SpaceId.Glued ? PerfVariant.Glued5x5 : PerfVariant.None) + " and S0 Home");
            if (!glued) notes.Add("S1 Glued is not in this build: the soak stays in S0 Home");
            notes.Add("the space changes every " + Seconds(interval) + " s; the soak lasts " + Seconds(total) + " s");
            if (total < DefaultSoakSeconds) notes.Add("shorter than the 15 minutes of section 11.4.1: a rehearsal, not evidence");
            notes.Add(kind == PerfRunKind.SoakDepthOff
                ? "LiDAR depth off, pose from a track: nobody wears the headset"
                : "LiDAR depth on");
            return Make(kind, steps, notes, settleSeconds, interval, true);
        }

        /// <summary>
        /// The ladder check: in Home, the tiers Fair, Serious, Critical, High and Nominal are forced in turn,
        /// so each step is a change, and each is held for holdSeconds.
        /// </summary>
        public static PerfSchedule Ladder(float holdSeconds = DefaultHoldSeconds)
        {
            float hold = Math.Max(1f, holdSeconds);
            var steps = new List<PerfStep>(6);
            steps.Add(new PerfStep(PerfStepKind.Enter, SpaceId.Home, PerfVariant.None, 0f, -1, LabelOf(SpaceId.Home, PerfVariant.None)));
            int[] order = { 1, 2, 3, 4, 0 };
            for (int i = 0; i < order.Length; i++)
                steps.Add(new PerfStep(PerfStepKind.ForceTier, SpaceId.Home, PerfVariant.None, hold, order[i], TierName(order[i])));
            var notes = new List<string>(1) { "each tier is forced as the Console forces it and must be in force within 1 s" };
            return Make(PerfRunKind.Ladder, steps, notes, 0f, 0f, false);
        }

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case 0: return "Nominal";
                case 1: return "Fair";
                case 2: return "Serious";
                case 3: return "Critical";
                case 4: return "High";
                default: return "auto";
            }
        }

        static PerfStep Visit(SpaceId space, PerfVariant variant, float seconds, float standSeconds = 0f)
        {
            return new PerfStep(PerfStepKind.Visit, space, variant, seconds, -1, LabelOf(space, variant), standSeconds);
        }

        /// <summary>Seconds of a visit that are not measured: the run's settle time, or the step's stand when longer.</summary>
        public static float SettleOf(float settleSeconds, float standSeconds)
        {
            return Math.Max(settleSeconds, standSeconds);
        }

        /// <summary>The unmeasured seconds at the start of this step's visit.</summary>
        public float SettleOf(in PerfStep step)
        {
            return step.Kind == PerfStepKind.Visit ? SettleOf(SettleSeconds, step.StandSeconds) : 0f;
        }

        static string CurvedNote(float settleSeconds)
        {
            return "S2 Curved Walk: q = 5 at rho = 3; a built-in lap stands for " + Seconds(CurvedStandSeconds) +
                   " s on entry so the curvature ramps, and " + Seconds(SettleOf(settleSeconds, CurvedStandSeconds)) +
                   " s settle before it is measured; a tracked or replayed walker must stand there himself";
        }

        static PerfSchedule Make(PerfRunKind kind, List<PerfStep> steps, List<string> notes, float settle, float interval, bool aggregate)
        {
            float total = 0f;
            for (int i = 0; i < steps.Count; i++)
                total += steps[i].Seconds + (steps[i].Kind == PerfStepKind.Visit ? SettleOf(settle, steps[i].StandSeconds) : 0f);
            return new PerfSchedule
            {
                Kind = kind,
                Steps = steps.ToArray(),
                Notes = notes.ToArray(),
                SettleSeconds = settle,
                IntervalSeconds = interval,
                Aggregate = aggregate,
                TotalSeconds = total,
            };
        }

        static string Seconds(float seconds)
        {
            return seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
