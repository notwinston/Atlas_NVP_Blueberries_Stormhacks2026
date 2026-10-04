using Atlas.Spaces.Portal;

namespace Atlas.Blades.Playground.Exhibits
{
    /// <summary>One finished lap of the loop corridor: four right turns.</summary>
    public struct RoomsLoopLap
    {
        /// <summary>The number on the wall of the room the lap began in.</summary>
        public int StartNumber;
        /// <summary>The number on the wall of the room it ended in.</summary>
        public int ArrivalNumber;
        /// <summary>The id of the room it ended in (the same as its number in a loop layout).</summary>
        public int ArrivalRoom;
        /// <summary>Right turns the walker made in this lap. Four when the loop's rooms end on a multiple of four turns
        /// (rooms.count 2, 4, 5 and 6); more where a room's corners carry the count past four (rooms.count 3 counts it
        /// at six), because a lap is counted at a doorway.</summary>
        public int TurnsWalked;
        /// <summary>Laps finished since the layout was built, this one included.</summary>
        public int Laps;
        /// <summary>The point of the exhibit: the walker is back on the floor they started from, in another room.</summary>
        public bool NumberChanged { get { return ArrivalNumber != StartNumber; } }
    }

    /// <summary>
    /// Follows a walker through the rooms of a loop layout and finds the laps of four right turns, with the
    /// room numbers at each end (Master Plan task P2-C16). It keeps its own count from the layout alone,
    /// so it does not depend on the order in which listeners of the portal renderer run; it counts a lap at
    /// the same crossing as <c>PortalSpaceCore.OnRoomChanged</c>, which is where Impossible Rooms logs its
    /// <c>rooms.loop</c> event. Engine-free.
    /// </summary>
    public sealed class RoomsLoopTracker
    {
        public const int TurnsPerLap = 4;

        PortalLayout m_Layout;
        int m_Turns;
        int m_StartRoom = -1;

        public int Laps { get; private set; }
        /// <summary>The newest finished lap. Default before the first.</summary>
        public RoomsLoopLap LastLap { get; private set; }
        /// <summary>Right turns made since the lap began, 0 to 3.</summary>
        public int TurnsInLap { get { return m_Turns; } }
        /// <summary>The number of the room the current lap began in, or 0 when the walker is not in the corridor.</summary>
        public int StartNumber
        {
            get { return m_Layout != null && m_StartRoom > 0 && m_StartRoom < m_Layout.RoomCount ? m_Layout.Rooms[m_StartRoom].Number : 0; }
        }

        /// <summary>Starts again on a new layout.</summary>
        public void Reset(PortalLayout layout)
        {
            m_Layout = layout;
            m_Turns = 0;
            m_StartRoom = -1;
            Laps = 0;
            LastLap = default(RoomsLoopLap);
        }

        /// <summary>The walker went through a doorway. Returns true when that finished a lap.</summary>
        public bool OnRoomChanged(int from, int to)
        {
            PortalLayout layout = m_Layout;
            if (layout == null || from < 0 || from >= layout.RoomCount || to < 0 || to >= layout.RoomCount) return false;
            LayoutRoom left = layout.Rooms[from];
            bool onwards = left.Kind == LayoutRoomKind.Corridor && left.ExitPortal >= 0
                           && layout.Portals[left.ExitPortal].To == to;
            if (!onwards)
            {
                // Out of the loop, back through the entrance or backwards: the count starts again; a walker who
                // arrives in a corridor room has started a lap there.
                m_Turns = 0;
                m_StartRoom = layout.Rooms[to].Kind == LayoutRoomKind.Corridor ? to : -1;
                return false;
            }
            if (m_StartRoom < 0) m_StartRoom = from;
            m_Turns += left.Turns;
            if (m_Turns < TurnsPerLap) return false;
            int walked = m_Turns;
            m_Turns -= TurnsPerLap;
            Laps++;
            LastLap = new RoomsLoopLap
            {
                StartNumber = layout.Rooms[m_StartRoom].Number,
                ArrivalNumber = layout.Rooms[to].Number,
                ArrivalRoom = to,
                TurnsWalked = walked,
                Laps = Laps,
            };
            // The next lap begins where this one ended.
            m_StartRoom = to;
            return true;
        }

        /// <summary>
        /// The number of the room a walker reaches after four right turns from a corridor room, read off the
        /// layout. 0 when the room is not a corridor room.
        /// </summary>
        public static int NumberAfterFourTurns(PortalLayout layout, int startRoom)
        {
            if (layout == null || startRoom < 0 || startRoom >= layout.RoomCount) return 0;
            if (layout.Rooms[startRoom].Kind != LayoutRoomKind.Corridor) return 0;
            return layout.Rooms[layout.RoomAfterRightTurns(startRoom, TurnsPerLap)].Number;
        }
    }
}
