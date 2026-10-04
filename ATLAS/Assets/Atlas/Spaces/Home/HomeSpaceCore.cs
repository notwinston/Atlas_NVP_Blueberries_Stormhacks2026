using System;
using Atlas.Link;
using Atlas.World;

namespace Atlas.Spaces.Home
{
    /// <summary>
    /// The logic of Home (S0) with no engine call, so it runs in headless tests: which room it shows, how it
    /// leaves the room content, which marks are wanted and what it reports to the Spectator
    /// (Master Plan sections 4.5 and 4.9). HomeSpace is the thin adapter around it.
    /// </summary>
    public sealed class HomeSpaceCore
    {
        /// <summary>"home.marks": the centre mark and the lap outline on the floor.</summary>
        public const string MarksParam = "home.marks";

        RoomModel m_Room;
        IRoomContent m_Content;
        ParamEntry m_Marks;
        bool m_Entered;

        /// <summary>The room given to Configure, or null.</summary>
        public RoomModel Room { get { return m_Room; } }
        public bool Configured { get { return m_Room != null; } }
        /// <summary>True from Enter until Exit.</summary>
        public bool Entered { get { return m_Entered; } }
        /// <summary>Home is the room rebuilt 1:1: always registered (Master Plan section 4.9).</summary>
        public bool Registered { get { return true; } }
        /// <summary>The room's standing flag (Master Plan section 4.4).</summary>
        public bool Standing { get { return m_Room != null && m_Room.Standing; } }

        /// <summary>The operator's switch for the floor marks. A field read: safe on the frame path.</summary>
        public bool MarksWanted { get { return m_Marks == null || m_Marks.Bool; } }
        /// <summary>The centre mark is drawn: Home is entered and the marks are switched on.</summary>
        public bool CentreMarkShown { get { return m_Entered && MarksWanted; } }
        /// <summary>The lap outline is drawn: as the centre mark, and never in standing mode.</summary>
        public bool LapMarksShown { get { return CentreMarkShown && HomeMarks.HasLapOutline(m_Room); } }

        /// <summary>Declares the parameters of group "home". Declaring again keeps the current values.</summary>
        public static ParamEntry DeclareParams(ParamRegistry registry)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            return registry.DeclareBool(MarksParam, SpaceIds.ParamGroup(SpaceId.Home), true);
        }

        /// <summary>Keeps the room and the room content. registry and content may be null (a bare test rig).</summary>
        public void Configure(RoomModel room, ParamRegistry registry, IRoomContent content)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            m_Room = room;
            m_Content = content;
            m_Marks = registry != null ? DeclareParams(registry) : null;
            m_Entered = false;
        }

        /// <summary>
        /// Shows the room as it is: walls at full height at once (the world is black), floor and every object
        /// view shown. The room content is built here only when nothing built it for this room yet.
        /// </summary>
        public void Enter()
        {
            if (m_Room == null) throw new InvalidOperationException("Home was entered before Configure.");
            IRoomContent content = m_Content;
            if (content != null)
            {
                if (!content.IsBuilt || !ReferenceEquals(content.Room, m_Room)) content.Build(m_Room);
                content.SetWallMode(WallMode.Full, 0f);
                content.FloorVisible = true;
                content.ObjectsVisible = true;
            }
            m_Entered = true;
        }

        /// <summary>Leaves the room content alone: the next space, or Home again, draws the same room.</summary>
        public void Exit()
        {
            m_Entered = false;
            m_Content = null;
            m_Marks = null;
        }

        /// <summary>
        /// Home's row of the State table (Master Plan Appendix B.1): six zeros for the tile, sheet 0, the
        /// identity transform, zeros in p[3] to p[7]. No other field is touched.
        /// </summary>
        public static void WriteState(ref StateMessage state)
        {
            state.SetTile(0L, 0L, 0L, 0L, 0L, 0L);
            state.Sheet = 0;
            state.SetXformIdentity();
            state.P3 = 0f;
            state.P4 = 0f;
            state.P5 = 0f;
            state.P6 = 0f;
            state.P7 = 0f;
        }
    }
}
