using System;
using Atlas.Link;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Portal
{
    /// <summary>
    /// The logic of Impossible Rooms that needs no engine: its parameters, the layout chosen for a room,
    /// the walk round the loop, and what it writes into State. PortalSpace is the adapter around it.
    /// </summary>
    public sealed class PortalSpaceCore
    {
        public const string LayoutParam = "rooms.layout";
        public const string CountParam = "rooms.count";
        public const string DepthParam = "portal.depth";

        static readonly string[] s_LayoutNames = { "booth", "loop" };

        ParamEntry m_Layout, m_Count, m_Depth;
        RoomModel m_Room;
        int m_LoopTurns;

        /// <summary>The layout in force. Null before Configure.</summary>
        public PortalLayout Layout { get; private set; }
        /// <summary>The room the walker is in, as last reported by the portal renderer.</summary>
        public int CurrentRoom { get; private set; }
        /// <summary>Set when rooms.layout or rooms.count changed: the geometry is rebuilt at the next Tick.</summary>
        public bool RebuildPending { get; private set; }
        /// <summary>portal.depth: 0 doorways show the fog colour, 1 doorways show the next room.</summary>
        public int Depth { get { return m_Depth != null ? m_Depth.Int : 1; } }
        /// <summary>Completed laps of four right turns since the space was entered.</summary>
        public int LoopsWalked { get; private set; }

        /// <summary>The parameters of group "portal". Safe to call again: values are kept.</summary>
        public static void DeclareParams(ParamRegistry registry)
        {
            Declare(registry, null);
        }

        static void Declare(ParamRegistry registry, PortalSpaceCore owner)
        {
            string group = SpaceIds.ParamGroup(SpaceId.Portal);
            Action<int> rebuild = owner != null ? owner.m_OnGeometryParam : null;
            ParamEntry layout = registry.DeclareEnum(LayoutParam, group, s_LayoutNames, 0, rebuild, true);
            ParamEntry count = registry.DeclareInt(CountParam, group, PortalLayoutGenerator.MinLoopRooms,
                PortalLayoutGenerator.MaxLoopRooms, PortalLayoutGenerator.DefaultLoopRooms, rebuild, true);
            ParamEntry depth = registry.DeclareInt(DepthParam, group, 0, 1, 1);
            if (owner == null) return;
            owner.m_Layout = layout;
            owner.m_Count = count;
            owner.m_Depth = depth;
        }

        readonly Action<int> m_OnGeometryParam;

        public PortalSpaceCore()
        {
            m_OnGeometryParam = OnGeometryParam;
        }

        void OnGeometryParam(int value)
        {
            RebuildPending = true;
        }

        /// <summary>Declares the parameters with this instance's handlers and generates the layout for the room.</summary>
        public PortalLayout Configure(RoomModel room, ParamRegistry registry)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            m_Room = room;
            if (registry != null) Declare(registry, this);
            return Generate();
        }

        /// <summary>Generates the layout again from the parameters as they stand.</summary>
        public PortalLayout Generate()
        {
            RebuildPending = false;
            var kind = m_Layout != null && m_Layout.Int == 1 ? PortalLayoutKind.Loop : PortalLayoutKind.BoothHall;
            int rooms = m_Count != null ? m_Count.Int : PortalLayoutGenerator.DefaultLoopRooms;
            PortalLayoutRequest request = PortalLayoutRequest.FromRoom(m_Room, kind, rooms, SeedOf(m_Room));
            Layout = PortalLayoutGenerator.Generate(in request);
            CurrentRoom = Layout.StartRoom;
            m_LoopTurns = 0;
            return Layout;
        }

        /// <summary>Leaves the parameters declared with no handler, so nothing calls into a module that is gone.</summary>
        public void Exit(ParamRegistry registry)
        {
            if (registry != null) Declare(registry, null);
            m_Layout = m_Count = m_Depth = null;
            RebuildPending = false;
        }

        /// <summary>
        /// The walker went through a doorway. Returns true when that completed four right turns round the
        /// loop: the walker stands on the floor they started from, in a different room.
        /// </summary>
        public bool OnRoomChanged(int from, int to)
        {
            CurrentRoom = to;
            PortalLayout layout = Layout;
            if (layout == null || from < 0 || from >= layout.RoomCount) return false;
            LayoutRoom left = layout.Rooms[from];
            bool onwards = left.Kind == LayoutRoomKind.Corridor && left.ExitPortal >= 0
                           && layout.Portals[left.ExitPortal].To == to;
            if (!onwards)
            {
                m_LoopTurns = 0;
                return false;
            }
            m_LoopTurns += left.Turns;
            if (m_LoopTurns < 4) return false;
            m_LoopTurns -= 4;
            LoopsWalked++;
            return true;
        }

        /// <summary>For the Spectator: the room of a received State.</summary>
        public void SetCurrentRoom(int room)
        {
            if (Layout != null) CurrentRoom = math.clamp(room, 0, Layout.RoomCount - 1);
        }

        /// <summary>tileId and sheet: the current room. p[3] layout, p[4] room count, p[5] portal depth.</summary>
        public void WriteState(ref StateMessage state)
        {
            state.Tile0 = CurrentRoom;
            state.Sheet = (short)CurrentRoom;
            PortalLayout layout = Layout;
            state.P3 = layout != null ? (float)(int)layout.Kind : 0f;
            state.P4 = layout != null ? layout.RoomCount : 0f;
            state.P5 = Depth;
        }

        /// <summary>A seed that is the same on the phone and on the Station: a hash of the room's id.</summary>
        public static uint SeedOf(RoomModel room)
        {
            uint hash = 2166136261u;
            string id = room != null ? room.Id : null;
            if (id != null)
                for (int k = 0; k < id.Length; k++) hash = (hash ^ id[k]) * 16777619u;
            return hash;
        }
    }
}
