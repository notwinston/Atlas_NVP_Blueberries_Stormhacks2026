using System.Collections.Generic;
using Atlas.World;
using Unity.Mathematics;

namespace Atlas.Spaces.Portal
{
    /// <summary>The layouts of Impossible Rooms. The first two values are the values of rooms.layout.</summary>
    public enum PortalLayoutKind
    {
        /// <summary>A booth in a corner of the free rectangle that contains a hall.</summary>
        BoothHall = 0,
        /// <summary>A corridor round the free rectangle whose four right turns arrive in a different room.</summary>
        Loop = 1,
        /// <summary>The floor holds no layout at all: the real room alone, with no doorway.</summary>
        RealRoomOnly = 2,
    }

    public enum LayoutRoomKind
    {
        /// <summary>Room 0: the scanned room, drawn by the room content.</summary>
        Real = 0,
        Hall = 1,
        Corridor = 2,
    }

    /// <summary>An axis-aligned piece of floor, metres, room frame (x, z).</summary>
    public struct FloorRect
    {
        public float2 Min, Max;

        public FloorRect(float2 a, float2 b)
        {
            Min = math.min(a, b);
            Max = math.max(a, b);
        }

        public float2 Size { get { return Max - Min; } }
        public float2 Centre { get { return 0.5f * (Min + Max); } }

        public bool Contains(float2 xz, float slack = 0f)
        {
            return xz.x >= Min.x - slack && xz.x <= Max.x + slack && xz.y >= Min.y - slack && xz.y <= Max.y + slack;
        }
    }

    /// <summary>Where a room may put floor: the eroded free floor of the scan.</summary>
    public interface IPortalFloor
    {
        bool Contains(float2 xz);
    }

    /// <summary>The eroded free floor of a RoomModel: inside the free rectangle, and walkable by the distance field.</summary>
    public sealed class RoomModelFloor : IPortalFloor
    {
        // The field reads exactly the walk threshold on the rim of the free rectangle; half a cell of slack
        // keeps the rim itself inside.
        const float FieldSlack = 0.5f * RoomSdf.CellSize + 0.005f;
        const float RectSlack = 0.002f;

        readonly RoomModel m_Room;

        public RoomModelFloor(RoomModel room)
        {
            m_Room = room;
        }

        public bool Contains(float2 xz)
        {
            FreeRect rect = m_Room.FreeRect;
            if (math.abs(xz.x) > 0.5f * rect.Wf + RectSlack || math.abs(xz.y) > 0.5f * rect.Df + RectSlack) return false;
            RoomSdf field = m_Room.Sdf;
            return field == null || field.Sample(xz) >= RoomSdf.WalkThreshold - FieldSlack;
        }
    }

    /// <summary>One room of a layout.</summary>
    public sealed class LayoutRoom
    {
        public int Id;
        public LayoutRoomKind Kind;
        /// <summary>The floor of the room. Empty for the real room, whose floor is the scan's.</summary>
        public FloorRect[] Cells = System.Array.Empty<FloorRect>();
        /// <summary>Corridor: the first corner of the free rectangle it covers (0 NW, 1 NE, 2 SE, 3 SW) and how
        /// many corners, which is how many right turns a walker makes in it.</summary>
        public int FirstCorner, Turns;
        /// <summary>Corridor: the portal a walker leaves through when walking on, or -1.</summary>
        public int ExitPortal = -1;
        /// <summary>The number shown on the wall, 1 .. 6; 0 shows none.</summary>
        public int Number;
        /// <summary>Region of the palette that colours the room.</summary>
        public int Hue;

        public bool ContainsFloor(float2 xz)
        {
            for (int k = 0; k < Cells.Length; k++) if (Cells[k].Contains(xz)) return true;
            return false;
        }
    }

    /// <summary>A doorway of a layout: the rectangle from A to B, A on the left as seen from the From room.</summary>
    public struct LayoutDoor
    {
        public int Id, From, To;
        public float2 A, B;
        public float Height;
    }

    /// <summary>One direction of a doorway. Every portal has a twin: the same doorway walked the other way.</summary>
    public struct LayoutPortal
    {
        public int Id, Door, From, To, Twin;
        /// <summary>The doorway's ends as seen from the From room.</summary>
        public float2 Left, Right;
    }

    /// <summary>
    /// A layout of Impossible Rooms (Master Plan task P1-A11): rooms authored in the room frame, so every
    /// portal transform is the identity, with the room graph and its checks. Engine-free.
    /// </summary>
    public sealed class PortalLayout
    {
        /// <summary>No room may have more doorways than there are stencil references.</summary>
        public const int MaxDoorsPerRoom = 15;

        public PortalLayoutKind Kind;
        /// <summary>What was asked for.</summary>
        public PortalLayoutKind Requested;
        /// <summary>True when the floor could not hold what was asked for and a simpler layout was built.</summary>
        public bool IsFallback;
        public uint Seed;
        /// <summary>The free rectangle, metres.</summary>
        public float Wf, Df;
        public float DoorHeight;

        /// <summary>Booth to hall: the booth's footprint, its height and the corner it stands in (signs of x and z).</summary>
        public FloorRect Booth;
        public float BoothHeight;
        public int2 BoothCorner;
        /// <summary>Loop: the corridor's width, the height of its walls and the leg that holds the entrance (1 east, 3 west).</summary>
        public float CorridorWidth, WallHeight;
        public int EntranceLeg;

        public readonly List<LayoutRoom> Rooms = new List<LayoutRoom>(8);
        public readonly List<LayoutDoor> Doors = new List<LayoutDoor>(8);
        public readonly List<LayoutPortal> Portals = new List<LayoutPortal>(16);
        /// <summary>The room the walker starts in: the real room.</summary>
        public int StartRoom;

        public int RoomCount { get { return Rooms.Count; } }

        internal LayoutRoom AddRoom(LayoutRoomKind kind)
        {
            var room = new LayoutRoom { Id = Rooms.Count, Kind = kind };
            Rooms.Add(room);
            return room;
        }

        /// <summary>Adds a doorway and its two portals. Returns the index of the portal from `from` to `to`.</summary>
        internal int AddDoor(int from, int to, float2 a, float2 b, float height)
        {
            int door = Doors.Count;
            Doors.Add(new LayoutDoor { Id = door, From = from, To = to, A = a, B = b, Height = height });
            int forward = Portals.Count;
            Portals.Add(new LayoutPortal { Id = forward, Door = door, From = from, To = to, Twin = forward + 1, Left = a, Right = b });
            Portals.Add(new LayoutPortal { Id = forward + 1, Door = door, From = to, To = from, Twin = forward, Left = b, Right = a });
            return forward;
        }

        // ---- the graph ------------------------------------------------------------------------------------

        /// <summary>Rooms a breadth-first search over the portals reaches from the start room.</summary>
        public int ReachableCount()
        {
            int n = Rooms.Count;
            if (n == 0 || StartRoom < 0 || StartRoom >= n) return 0;
            var seen = new bool[n];
            var queue = new Queue<int>(n);
            seen[StartRoom] = true;
            queue.Enqueue(StartRoom);
            int reached = 0;
            while (queue.Count > 0)
            {
                int room = queue.Dequeue();
                reached++;
                for (int k = 0; k < Portals.Count; k++)
                {
                    LayoutPortal portal = Portals[k];
                    if (portal.From != room || portal.To < 0 || portal.To >= n || seen[portal.To]) continue;
                    seen[portal.To] = true;
                    queue.Enqueue(portal.To);
                }
            }
            return reached;
        }

        public bool AllReachable { get { return ReachableCount() == Rooms.Count; } }

        /// <summary>True when every portal has a twin that leads back through the same doorway.</summary>
        public bool EveryPortalHasTwin()
        {
            for (int k = 0; k < Portals.Count; k++)
            {
                LayoutPortal portal = Portals[k];
                if (portal.Twin < 0 || portal.Twin >= Portals.Count || portal.Twin == k) return false;
                LayoutPortal twin = Portals[portal.Twin];
                if (twin.Twin != k || twin.Door != portal.Door || twin.From != portal.To || twin.To != portal.From) return false;
                // The same rectangle, seen from the other side: the ends swap.
                if (math.distance(twin.Left, portal.Right) > 1e-4f || math.distance(twin.Right, portal.Left) > 1e-4f) return false;
            }
            return true;
        }

        /// <summary>Portals that leave a room: the most stencil references the room can ask for in a frame.</summary>
        public int PortalsFrom(int room)
        {
            int count = 0;
            for (int k = 0; k < Portals.Count; k++) if (Portals[k].From == room) count++;
            return count;
        }

        public int MaxPortalsFromOneRoom()
        {
            int most = 0;
            for (int room = 0; room < Rooms.Count; room++) most = math.max(most, PortalsFrom(room));
            return most;
        }

        /// <summary>
        /// The room a walker is in after entering a corridor room and making this many right turns: the walk
        /// leaves each room through its exit when the room's turns are used up.
        /// </summary>
        public int RoomAfterRightTurns(int startRoom, int turns)
        {
            int room = startRoom;
            // A walk can never need more steps than turns: every corridor room has at least one corner.
            for (int steps = turns; steps >= 0; steps--)
            {
                LayoutRoom current = Rooms[room];
                if (current.Turns <= 0 || current.ExitPortal < 0 || turns < current.Turns) return room;
                turns -= current.Turns;
                room = Portals[current.ExitPortal].To;
            }
            return room;
        }

        // ---- the floor ------------------------------------------------------------------------------------

        /// <summary>
        /// True when every cell of every room lies inside the free rectangle and inside the floor. Cells are
        /// probed on a grid no coarser than 0.25 m, their rims included.
        /// </summary>
        public bool CellsInside(IPortalFloor floor)
        {
            var rect = new FloorRect(new float2(-0.5f * Wf, -0.5f * Df), new float2(0.5f * Wf, 0.5f * Df));
            for (int r = 0; r < Rooms.Count; r++)
            {
                FloorRect[] cells = Rooms[r].Cells;
                for (int c = 0; c < cells.Length; c++)
                    if (!RectInside(cells[c], rect, floor)) return false;
            }
            return Kind != PortalLayoutKind.BoothHall || RectInside(Booth, rect, floor);
        }

        static bool RectInside(FloorRect cell, FloorRect freeRect, IPortalFloor floor)
        {
            const float slack = 1e-3f, step = 0.25f;
            if (!freeRect.Contains(cell.Min, slack) || !freeRect.Contains(cell.Max, slack)) return false;
            if (floor == null) return true;
            float2 size = cell.Size;
            if (size.x <= 2f * slack || size.y <= 2f * slack) return false;
            int nx = math.max(1, (int)math.ceil(size.x / step)), nz = math.max(1, (int)math.ceil(size.y / step));
            // Probes sit a millimetre inside the rim, so a cell that ends on the floor's own rim passes.
            float2 low = cell.Min + slack, span = size - 2f * slack;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                    if (!floor.Contains(low + span * new float2((float)i / nx, (float)j / nz))) return false;
            return true;
        }

        /// <summary>Every check of Portal_Graph (Master Plan section 11.1) at once.</summary>
        public bool IsValid(IPortalFloor floor)
        {
            return Rooms.Count > 0 && AllReachable && EveryPortalHasTwin()
                   && MaxPortalsFromOneRoom() <= MaxDoorsPerRoom && CellsInside(floor);
        }
    }
}
