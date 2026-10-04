using System;
using Atlas.Spaces.Portal;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>Where the walker is in the Impossible Rooms exhibit.</summary>
    public enum RoomsStage
    {
        /// <summary>Task 1: enter the booth.</summary>
        Booth = 0,
        /// <summary>Task 2: walk the loop corridor and read the room number.</summary>
        Loop = 1,
        /// <summary>Every task the room allows is done; the exhibit menu takes over.</summary>
        Done = 2,
    }

    /// <summary>Why the loop corridor is not offered, when it is not.</summary>
    public enum RoomsLoopBlock
    {
        None = 0,
        /// <summary>min(Wf, Df) is under 2.4 m (Master Plan section 10.1.3, R6 section 3.5).</summary>
        TooSmall = 1,
        /// <summary>Standing mode: the walker does not walk, so the loop is never built.</summary>
        Standing = 2,
        /// <summary>The free rectangle is big enough but the eroded floor cannot hold the whole corridor (set only after a build).</summary>
        Floor = 3,
    }

    /// <summary>The words the exhibit puts in front of the walker.</summary>
    public static class RoomsText
    {
        /// <summary>The caption chip of S5 (Master Plan section 10.1.3).</summary>
        public const string Caption = "Flat rooms joined so that they overlap.";
        public const string BoothPrompt = "Step into the booth. It is 1 m across, and it holds a hall.";
        public const string HallPrompt = "You are in a hall that is bigger than the booth you walked into.";
        public const string LoopPrompt = "Now the corridor. Turn right at every corner, four times, and read the number on the wall.";
        public const string FallbackTooSmall = "This room is too small for the loop corridor, which needs 2.4 m by 2.4 m of free floor. The booth is the exhibit here.";
        public const string FallbackStanding = "You are standing in place, so the loop corridor is skipped. The booth is the exhibit here.";
        public const string FallbackFloor = "The free floor has no space for the whole loop corridor, so the booth is the exhibit here.";

        public static string Fallback(RoomsLoopBlock block)
        {
            switch (block)
            {
                case RoomsLoopBlock.TooSmall: return FallbackTooSmall;
                case RoomsLoopBlock.Standing: return FallbackStanding;
                default: return FallbackFloor;
            }
        }

        public static string LapPrompt(int startNumber, int arrivalNumber, int turns = RoomsLoopTracker.TurnsPerLap)
        {
            string count = turns == 4 ? "four" : turns.ToString();
            return "You started in room " + startNumber + " and, after " + count + " right turns, you are in room " + arrivalNumber
                   + ". Same floor, different room.";
        }
    }

    /// <summary>
    /// The tasks of the Impossible Rooms exhibit (Master Plan section 10.1.3: "enter the booth; walk the loop
    /// corridor and read the room number") over the P1-A11 layouts, with the fallback prompt for a room that
    /// cannot hold the loop (task P2-C15), and the numbers the S5 equation card reads. A state machine over
    /// what the walker does: it asks for a layout through <see cref="WantedLayout"/> and is told what was
    /// built and which doorways the walker crossed. Engine-free.
    /// </summary>
    public sealed class RoomsExhibitCore
    {
        readonly RoomsLoopTracker m_Tracker = new RoomsLoopTracker();
        PortalLayout m_Layout;
        bool[] m_Visited = Array.Empty<bool>();

        /// <param name="room">The scanned room: its free rectangle and standing flag decide whether the loop is offered.</param>
        public RoomsExhibitCore(RoomModel room)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            Wf = room.FreeRect.Wf;
            Df = room.FreeRect.Df;
            LoopBlock = BlockFor(Wf, Df, room.Standing);
            Begin();
        }

        public float Wf { get; private set; }
        public float Df { get; private set; }

        /// <summary>Why the loop is not offered, or None.</summary>
        public RoomsLoopBlock LoopBlock { get; private set; }
        public bool LoopAvailable { get { return LoopBlock == RoomsLoopBlock.None; } }

        public RoomsStage Stage { get; private set; }
        /// <summary>The layout the exhibit wants in force: the booth first, then the loop where the room allows it.</summary>
        public PortalLayoutKind WantedLayout { get; private set; }
        /// <summary>True once the exhibit has fallen back to the booth for want of floor for the loop.</summary>
        public bool FallbackActive { get; private set; }
        /// <summary>What the walker is asked to do now. Raises <see cref="PromptChanged"/> when it changes.</summary>
        public string Prompt { get; private set; }

        public bool BoothEntered { get; private set; }
        /// <summary>The walker finished a lap and the room number differs from where they started.</summary>
        public bool NumberRead { get; private set; }
        public bool LoopWalked { get { return m_Tracker.Laps > 0; } }
        public RoomsLoopLap LastLap { get { return m_Tracker.LastLap; } }
        public bool Completed { get { return Stage == RoomsStage.Done; } }

        public event Action<string> PromptChanged;

        /// <summary>Why a room with this free rectangle gets no loop. The rule of Master Plan section 10.1.3.</summary>
        public static RoomsLoopBlock BlockFor(float wf, float df, bool standing)
        {
            if (standing) return RoomsLoopBlock.Standing;
            return math.min(wf, df) >= PortalLayoutGenerator.MinLoopSide - 1e-4f ? RoomsLoopBlock.None : RoomsLoopBlock.TooSmall;
        }

        /// <summary>True when the loop corridor is chosen for a free rectangle: min(Wf, Df) at least 2.4 m and not standing.</summary>
        public static bool LoopFits(float wf, float df, bool standing)
        {
            return BlockFor(wf, df, standing) == RoomsLoopBlock.None;
        }

        /// <summary>Starts the exhibit again, at the booth.</summary>
        public void Begin()
        {
            Stage = RoomsStage.Booth;
            WantedLayout = PortalLayoutKind.BoothHall;
            FallbackActive = false;
            BoothEntered = false;
            NumberRead = false;
            m_Tracker.Reset(null);
            SetPrompt(RoomsText.BoothPrompt);
        }

        /// <summary>
        /// The layout in force changed (the exhibit's request was built, or the walker changed rooms.layout).
        /// Starts the loop count again. A loop asked for and not built is the fallback: the booth stands
        /// instead, and the walker is told why.
        /// </summary>
        public void OnLayoutBuilt(PortalLayout layout)
        {
            m_Layout = layout;
            m_Visited = layout != null ? new bool[layout.RoomCount] : Array.Empty<bool>();
            if (m_Visited.Length > 0) m_Visited[layout.StartRoom] = true;
            m_Tracker.Reset(layout);
            if (layout == null) return;
            if (Stage == RoomsStage.Loop && WantedLayout == PortalLayoutKind.Loop && layout.Kind != PortalLayoutKind.Loop)
                Fallback(LoopBlock == RoomsLoopBlock.None ? RoomsLoopBlock.Floor : LoopBlock);
        }

        /// <summary>The walker went through a doorway of the layout in force.</summary>
        public void OnRoomChanged(int from, int to)
        {
            PortalLayout layout = m_Layout;
            if (layout == null) return;
            if (to >= 0 && to < m_Visited.Length) m_Visited[to] = true;

            if (layout.Kind == PortalLayoutKind.BoothHall)
            {
                if (to >= 0 && to < layout.RoomCount && layout.Rooms[to].Kind == LayoutRoomKind.Hall && Stage == RoomsStage.Booth)
                {
                    BoothEntered = true;
                    if (LoopAvailable)
                    {
                        Stage = RoomsStage.Loop;
                        WantedLayout = PortalLayoutKind.Loop;
                        SetPrompt(RoomsText.LoopPrompt);
                    }
                    else
                    {
                        Fallback(LoopBlock);
                    }
                }
                else if (to >= 0 && to < layout.RoomCount && layout.Rooms[to].Kind == LayoutRoomKind.Hall)
                {
                    SetPrompt(RoomsText.HallPrompt);
                }
                return;
            }

            if (layout.Kind == PortalLayoutKind.Loop && m_Tracker.OnRoomChanged(from, to) && Stage == RoomsStage.Loop)
            {
                RoomsLoopLap lap = m_Tracker.LastLap;
                NumberRead = lap.NumberChanged;
                Stage = RoomsStage.Done;
                SetPrompt(RoomsText.LapPrompt(lap.StartNumber, lap.ArrivalNumber, lap.TurnsWalked));
            }
        }

        // The booth is what is left of the exhibit: the walker is told why and the exhibit is done.
        void Fallback(RoomsLoopBlock reason)
        {
            FallbackActive = true;
            Stage = RoomsStage.Done;
            WantedLayout = PortalLayoutKind.BoothHall;
            SetPrompt(RoomsText.Fallback(reason));
        }

        // ---- the S5 equation card (Master Plan section 10.1.3): virtual floor walked / free-rectangle area ----

        /// <summary>The free rectangle's area, m².</summary>
        public float FreeRectArea { get { return Wf * Df; } }

        /// <summary>The floor area, m², of the virtual rooms the walker has been in since the layout was built.</summary>
        public float VirtualFloorVisited
        {
            get
            {
                PortalLayout layout = m_Layout;
                if (layout == null) return 0f;
                float area = 0f;
                for (int r = 0; r < layout.RoomCount && r < m_Visited.Length; r++)
                {
                    if (!m_Visited[r] || layout.Rooms[r].Kind == LayoutRoomKind.Real) continue;
                    FloorRect[] cells = layout.Rooms[r].Cells;
                    for (int c = 0; c < cells.Length; c++) area += cells[c].Size.x * cells[c].Size.y;
                }
                return area;
            }
        }

        /// <summary>Virtual floor visited divided by the free rectangle's area: above 1 means more floor than the room has.</summary>
        public float FloorRatio { get { return FreeRectArea > 0f ? VirtualFloorVisited / FreeRectArea : 0f; } }

        void SetPrompt(string text)
        {
            if (Prompt == text) return;
            Prompt = text;
            Action<string> changed = PromptChanged;
            if (changed != null) changed(text);
        }
    }
}
