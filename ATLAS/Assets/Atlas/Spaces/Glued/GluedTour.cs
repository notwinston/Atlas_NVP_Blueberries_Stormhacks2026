using System;
using System.Globalization;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Glued
{
    /// <summary>Where the walker is in the guided steps of S1 (Master Plan section 10.1.3, the S1 tasks).</summary>
    public enum GluedTask
    {
        /// <summary>Task 1: wrap-walk through one cell edge.</summary>
        Wrap = 0,
        /// <summary>Task 2: wave at your copies.</summary>
        Wave = 1,
        /// <summary>Task 3: switch to Klein and compare the chiral landmark.</summary>
        Klein = 2,
        /// <summary>Task 4: switch to T3 and look up.</summary>
        Torus3 = 3,
        /// <summary>Every task is done; the exhibit menu takes over.</summary>
        Done = 4,
    }

    /// <summary>The words S1 puts in front of the walker: the prompts of the Playground's chip, and the short step lines of the info card.</summary>
    public static class GluedText
    {
        /// <summary>Steps before Done.</summary>
        public const int StepCount = 4;

        /// <summary>The caption chip of S1 (Master Plan section 10.1.3).</summary>
        public const string Caption = "Every wall is a door to its opposite wall. Flat, but it wraps.";
        public const string WrapPrompt = "Walk to the chevrons on the floor and step across the line. The next cell is this room again.";
        public const string WrapSkipped = "You are standing in place, or the room is a single cell, so the wrap-walk is skipped. Look round: the room repeats in every direction. Press A to go on.";
        public const string WavePrompt = "Turn round and wave. Every copy of you waves back. Press A when you have seen your own back.";
        public const string KleinPrompt = "Switch the gluing to Klein with the right bumper, or look down at the gluing sign under your card and dwell on Klein.";
        public const string ChiralPrompt = "Find the staircase landmark in your own cell, then the one in the next row over. That row is a mirror image: your copy waves with the other hand and no turning makes the two staircases match.";
        public const string Torus3Prompt = "Switch the gluing to T3 with the right bumper or the gluing sign, then look up. Your copy stands a room-height above you.";
        public const string LookUpPrompt = "Look straight up. The floor above is this floor, and you are standing on it.";
        public const string DonePrompt = "Three kinds of gluing, one flat room. Open the exhibit menu for the next exhibit.";

        /// <summary>
        /// The step line of the info card: what to do now, short enough for three lines of the card. The Playground's
        /// prompt chip has the longer <see cref="GluedTour.Prompt"/>.
        /// </summary>
        public static string CardStep(GluedTask task, GluedLayout layout, bool skippedWrap)
        {
            switch (task)
            {
                case GluedTask.Wrap: return "Walk across the chevrons on the floor. The next cell is this room again.";
                case GluedTask.Wave:
                    return skippedWrap
                        ? "The room repeats all round you. Look, then press A or dwell on “seen it”."
                        : "Turn round and wave: that is your own back. Then press A or dwell on “seen it”.";
                case GluedTask.Klein:
                    return layout == GluedLayout.Klein
                        ? "Step into the next row. Your copy and the staircase there are mirror images."
                        : "Pick Klein on the gluing sign below, or press a bumper.";
                case GluedTask.Torus3:
                    return layout == GluedLayout.Torus3
                        ? "Look straight up: your copy stands 2.6 m above you, on this same floor."
                        : "Pick T³ on the gluing sign below, or press a bumper.";
                default: return "All four done. Try the gluings again, or open the exhibit menu.";
            }
        }

        /// <summary>The tasks' names, as logged in <c>glued.task</c>.</summary>
        public static string TaskName(GluedTask task)
        {
            switch (task)
            {
                case GluedTask.Wrap: return "wrap";
                case GluedTask.Wave: return "wave";
                case GluedTask.Klein: return "klein";
                case GluedTask.Torus3: return "t3";
                default: return "done";
            }
        }
    }

    /// <summary>The numbers the S1 equation card reads: x mod a, z mod b, the cell, the wraps and the copy parity.</summary>
    public struct GluedCardValues
    {
        /// <summary>Cell sides a and b, metres: the RoomModel's cells.glued.</summary>
        public double A, B;
        /// <summary>The walker's place inside the cell, 0 to a and 0 to b, metres.</summary>
        public double XMod, ZMod;
        public int Wraps;
        /// <summary>(-1)^row of the walker's cell for the Klein bottle, +1 otherwise.</summary>
        public int Parity;

        /// <summary>The card's numbers for a head position in a cell of a lattice, after `wraps` crossings.</summary>
        public static GluedCardValues Of(in GluedLattice lattice, GluedCell cell, float3 head, int wraps)
        {
            double a = lattice.SizeX, b = lattice.SizeZ;
            return new GluedCardValues
            {
                A = a,
                B = b,
                XMod = Mod(head.x + 0.5 * lattice.Wf, a),
                ZMod = Mod(head.z + 0.5 * lattice.Df, b),
                Wraps = wraps,
                Parity = lattice.DetSign(cell),
            };
        }

        static double Mod(double value, double side)
        {
            double r = value - Math.Floor(value / side) * side;
            return r < 0.0 ? 0.0 : r;
        }
    }

    /// <summary>
    /// The guided steps of S1, its wrap counter and its <c>glued.*</c> events (Master Plan section 10.1.3 and tasks
    /// P2-C12 to P2-C14). It follows the head over the lattice of <see cref="GluedSpace"/> (P1-A09), whose cell size is
    /// the RoomModel's (cells.glued), and counts every cell edge the head crosses the way the space does: a change of
    /// the gluing moves the lattice but crosses nothing. The space runs it, so S1 teaches the same way whether it is
    /// reached from the exhibit menu, the space menu or the Station; the Playground's exhibit only relays its prompt.
    /// Engine-free.
    /// </summary>
    public sealed class GluedTour
    {
        /// <summary>Head pitch above the horizon past which the walker is looking up (task P2-C14), degrees.</summary>
        public const float LookUpPitchDeg = GluedLookUp.PitchDeg;

        readonly RoomModel m_Room;
        readonly IEventLog m_Log;
        GluedLattice m_Lattice;
        GluedCell m_Cell;
        bool m_HasCell;
        bool m_SawMirror;
        bool m_WrapDone, m_WaveDone, m_KleinDone, m_Torus3Done, m_SkippedWrap;
        string m_Prompt = string.Empty;

        /// <param name="room">The scanned room: the cell size and the standing flag come from it.</param>
        /// <param name="log">Where glued.wrap, glued.mode and glued.task go; null keeps the steps silent.</param>
        /// <param name="start">The gluing in force when the steps start.</param>
        public GluedTour(RoomModel room, IEventLog log, GluedLayout start = GluedLayout.Torus)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            m_Room = room;
            m_Log = log;
            Begin(start);
        }

        /// <summary>The lattice in force: the room's cell grid under the current gluing.</summary>
        public GluedLattice Lattice { get { return m_Lattice; } }
        public GluedLayout Layout { get { return m_Lattice.Layout; } }
        /// <summary>Cell sides, metres: equal to the RoomModel's cells.glued (or the cell rule's value for a room without a grid).</summary>
        public double CellSizeX { get { return m_Lattice.SizeX; } }
        public double CellSizeZ { get { return m_Lattice.SizeZ; } }
        /// <summary>Cell edges crossed since Begin.</summary>
        public int Wraps { get; private set; }
        public GluedTask Task { get; private set; }
        public string Prompt { get { return m_Prompt; } }
        /// <summary>The cell the head was last in; (0, 0, 0) before the first Step.</summary>
        public GluedCell Cell { get { return m_Cell; } }
        /// <summary>The wrap-walk was skipped: standing mode, or a room of one cell.</summary>
        public bool SkippedWrap { get { return m_SkippedWrap; } }
        /// <summary>The info card's line for the step in force.</summary>
        public string CardStep { get { return GluedText.CardStep(Task, m_Lattice.Layout, m_SkippedWrap); } }
        /// <summary>The walker is looking up in T3 at a copy of themself above.</summary>
        public bool LookedUp { get; private set; }
        /// <summary>True when the room has a cell edge to walk across.</summary>
        public bool WrapWalkAvailable { get { return GluedPrompt.Available(in m_Lattice, m_Room.Standing); } }

        /// <summary>The prompt changed.</summary>
        public event Action<string> PromptChanged;

        /// <summary>Starts again: no wraps, first task, in the gluing given (the torus by default). Logs that gluing.</summary>
        public void Begin(GluedLayout start = GluedLayout.Torus)
        {
            m_Lattice = new GluedLattice(m_Room, start);
            m_Cell = default;
            m_HasCell = false;
            m_SawMirror = false;
            m_WrapDone = m_WaveDone = m_KleinDone = m_Torus3Done = m_SkippedWrap = false;
            Wraps = 0;
            LookedUp = false;
            m_Prompt = string.Empty;
            Task = GluedTask.Wrap;
            LogMode();
            Settle();
        }

        /// <summary>
        /// Once a frame, after the space's own Tick: takes the head pose and the gluing in force (glued.mode).
        /// A crossing is a change of cell with the gluing unchanged; the first call, and the call that follows a
        /// change of gluing, only place the walker.
        /// </summary>
        public void Step(float3 head, quaternion rotation, GluedLayout layout)
        {
            if (layout != m_Lattice.Layout)
            {
                m_Lattice = new GluedLattice(in m_Lattice, layout);
                m_HasCell = false;
                LogMode();
            }

            GluedCell cell = m_Lattice.CellOf(head);
            if (m_HasCell && !cell.Equals(m_Cell)) Cross(m_Cell, cell);
            m_Cell = cell;
            m_HasCell = true;

            if (m_Lattice.Layout == GluedLayout.Klein && m_Lattice.IsMirrored(cell)) m_SawMirror = true;
            if (m_Lattice.Layout == GluedLayout.Torus3 && GluedLookUp.AvatarVisible(in m_Lattice, cell, head, rotation))
                LookedUp = true;
            Settle();
        }

        /// <summary>The walker pressed A at a prompt: the wave task is done.</summary>
        public void Acknowledge()
        {
            if (Task != GluedTask.Wave) return;
            m_WaveDone = true;
            LogTask(GluedTask.Wave);
            Settle();
        }

        /// <summary>The numbers of the equation card for a head position.</summary>
        public GluedCardValues Card(float3 head)
        {
            GluedCardValues card = GluedCardValues.Of(in m_Lattice, m_Cell, head, Wraps);
            if (!m_HasCell) card.Parity = 1;
            return card;
        }

        void Cross(GluedCell from, GluedCell to)
        {
            Wraps++;
            if (!m_WrapDone)
            {
                m_WrapDone = true;
                LogTask(GluedTask.Wrap);
            }
            if (m_Log == null) return;
            string edge = from.Col != to.Col ? "x" : from.Row != to.Row ? "z" : "y";
            string json = string.Format(CultureInfo.InvariantCulture,
                "{{\"n\":{0},\"col\":{1},\"row\":{2},\"layer\":{3},\"edge\":\"{4}\",\"det\":{5}}}",
                Wraps, to.Col, to.Row, to.Layer, edge, m_Lattice.DetSign(to));
            m_Log.LogJson("glued.wrap", json);
        }

        void LogMode()
        {
            if (m_Log != null) m_Log.Log("glued.mode", "mode", GluedRules.LayoutNames[(int)m_Lattice.Layout]);
        }

        void LogTask(GluedTask done)
        {
            if (m_Log != null) m_Log.Log("glued.task", "task", GluedText.TaskName(done));
        }

        // Takes the task to the first one not done, then chooses the prompt for it. The gluing is the walker's to
        // switch, so Klein and T3 can be reached before the wave is acknowledged; they are remembered until their turn.
        void Settle()
        {
            // A room with no edge to cross, or standing mode, skips the wrap-walk instead of asking for it.
            if (!m_WrapDone && !WrapWalkAvailable)
            {
                m_WrapDone = true;
                m_SkippedWrap = true;
            }
            if (!m_KleinDone && m_SawMirror)
            {
                m_KleinDone = true;
                LogTask(GluedTask.Klein);
            }
            if (!m_Torus3Done && LookedUp)
            {
                m_Torus3Done = true;
                LogTask(GluedTask.Torus3);
            }
            Task = !m_WrapDone ? GluedTask.Wrap
                 : !m_WaveDone ? GluedTask.Wave
                 : !m_KleinDone ? GluedTask.Klein
                 : !m_Torus3Done ? GluedTask.Torus3
                 : GluedTask.Done;
            SetPrompt(PromptFor());
        }

        string PromptFor()
        {
            switch (Task)
            {
                case GluedTask.Wrap: return GluedText.WrapPrompt;
                case GluedTask.Wave: return m_SkippedWrap ? GluedText.WrapSkipped : GluedText.WavePrompt;
                case GluedTask.Klein:
                    return m_Lattice.Layout == GluedLayout.Klein ? GluedText.ChiralPrompt : GluedText.KleinPrompt;
                case GluedTask.Torus3:
                    return m_Lattice.Layout == GluedLayout.Torus3 ? GluedText.LookUpPrompt : GluedText.Torus3Prompt;
                default: return GluedText.DonePrompt;
            }
        }

        void SetPrompt(string text)
        {
            if (text == m_Prompt) return;
            m_Prompt = text;
            Action<string> changed = PromptChanged;
            if (changed != null) changed(text);
        }
    }
}
