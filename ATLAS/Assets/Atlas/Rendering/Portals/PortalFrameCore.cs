using System;
using Unity.Mathematics;

namespace Atlas.Rendering
{
    /// <summary>
    /// One frame of the stencil portals without the engine (Master Plan D16, task P1-A10; foundation F2):
    /// the head crossing a doorway, the doorways an eye can see, largest first, their stencil references,
    /// and for every room whether it is on, on which layer and with which clip plane. The portal renderer
    /// reads the cameras, calls <see cref="Step"/> and applies the result to its objects; a test or a
    /// rehearsal calls the same method with no camera at all. Fixed storage: a frame allocates nothing.
    /// </summary>
    public sealed class PortalFrameCore
    {
        /// <summary>Doorway directions a frame considers; the 15 largest that fit get a reference.</summary>
        public const int MaxCandidates = 32;
        /// <summary>The clip plane that clips nothing.</summary>
        public static readonly float4 NoPlane = new float4(0f, 0f, 0f, 1f);

        /// <summary>Where a room is drawn this frame.</summary>
        public struct RoomFrame
        {
            /// <summary>False while the room is switched off.</summary>
            public bool On;
            /// <summary>Layer Default for the walker's room, a room layer for a room behind a doorway or kept on.</summary>
            public int Layer;
            /// <summary>(n.xyz, d) of the clip plane, or <see cref="NoPlane"/>.</summary>
            public float4 Plane;
            /// <summary>True from the frame the room is the walker's or is shown through a doorway, until it is switched off.</summary>
            public bool WasShown;
        }

        /// <summary>The masks of one direction of a doorway this frame.</summary>
        public struct DoorFrame
        {
            /// <summary>The stencil reference its masks write; 0 = hidden.</summary>
            public int Reference;
            /// <summary>True when an eye straddles the doorway and the portal box is drawn as well.</summary>
            public bool Box;
        }

        struct Door
        {
            public int From, To;
            public float2 A, B;
            public float Height;
            public float4 Plane;
            public bool Open;
        }

        struct Candidate
        {
            public int Door, Direction, Reference;
            public float Area;
            public bool Box;
        }

        readonly PortalSlotAllocator m_Slots = new PortalSlotAllocator();
        readonly PortalSwapRule m_Swap = new PortalSwapRule(16);
        readonly Candidate[] m_Candidates = new Candidate[MaxCandidates];

        float3[] m_Min = new float3[16], m_Max = new float3[16];
        RoomFrame[] m_Rooms = new RoomFrame[16];
        Door[] m_Doors = new Door[16];
        // Two per door: forward, backward.
        DoorFrame[] m_DoorFrames = new DoorFrame[32];
        int m_RoomCount, m_DoorCount;

        int m_CurrentRoom;
        int m_Depth = 1;
        int m_CandidateCount;
        bool m_RoomsShown;
        float3 m_PreviousHead;
        bool m_HasPreviousHead;

        public int RoomCount { get { return m_RoomCount; } }
        public int DoorCount { get { return m_DoorCount; } }

        /// <summary>The room the walker is in. Changed by <see cref="Step"/> when the head crosses a doorway.</summary>
        public int CurrentRoom
        {
            get { return m_CurrentRoom; }
            set
            {
                // Placed from outside: the head did not walk there, so the next frame tests no crossing.
                m_HasPreviousHead = false;
                SetCurrent(value);
            }
        }

        /// <summary>0: doorways show the fog colour. 1: doorways show the next room (portal.depth).</summary>
        public int Depth
        {
            get { return m_Depth; }
            set { m_Depth = math.clamp(value, 0, 1); }
        }

        /// <summary>Stencil references in use after the last step: 0 .. 15.</summary>
        public int SlotsInUse { get { return m_Slots.Count; } }
        /// <summary>Doorway directions the last step found in view.</summary>
        public int VisibleDoorways { get { return m_CandidateCount; } }
        /// <summary>True when the last step shows at least one doorway mask.</summary>
        public bool MasksShown { get { return m_CandidateCount > 0; } }
        /// <summary>True when the last step draws rooms behind doorways: the features exist and the depth is 1.</summary>
        public bool RoomsShown { get { return m_RoomsShown; } }
        /// <summary>Frames in a row the switch-off condition has held for a room.</summary>
        public int HeldFrames(int room) { return m_Swap.HeldFrames(room); }

        /// <summary>Raised when CurrentRoom changes: (old, new).</summary>
        public event Action<int, int> RoomChanged;

        /// <summary>Registers a room by its bounds in the room frame. Returns its id (0, 1, ...). It starts on, on layer Default.</summary>
        public int AddRoom(float3 boundsMin, float3 boundsMax)
        {
            if (m_RoomCount == m_Rooms.Length)
            {
                int capacity = m_RoomCount * 2;
                Array.Resize(ref m_Rooms, capacity);
                Array.Resize(ref m_Min, capacity);
                Array.Resize(ref m_Max, capacity);
            }
            int id = m_RoomCount++;
            m_Min[id] = boundsMin;
            m_Max[id] = boundsMax;
            m_Rooms[id] = new RoomFrame { On = true, Layer = PortalStencilTable.WorldLayer, Plane = NoPlane, WasShown = false };
            m_Swap.EnsureCapacity(m_RoomCount);
            return id;
        }

        /// <summary>
        /// A doorway between two rooms: a vertical rectangle from floor point a to floor point b, of the given
        /// height, seen from fromRoom with a on the left. Both directions. Returns the door id.
        /// </summary>
        public int AddDoor(int fromRoom, int toRoom, float2 a, float2 b, float height)
        {
            if (fromRoom < 0 || fromRoom >= m_RoomCount) throw new ArgumentOutOfRangeException(nameof(fromRoom));
            if (toRoom < 0 || toRoom >= m_RoomCount) throw new ArgumentOutOfRangeException(nameof(toRoom));
            if (m_DoorCount == m_Doors.Length)
            {
                Array.Resize(ref m_Doors, m_DoorCount * 2);
                Array.Resize(ref m_DoorFrames, m_DoorCount * 4);
            }
            int id = m_DoorCount++;
            m_Doors[id] = new Door { From = fromRoom, To = toRoom, A = a, B = b, Height = height, Plane = PortalMath.DoorPlane(a, b), Open = true };
            m_DoorFrames[2 * id] = default;
            m_DoorFrames[2 * id + 1] = default;
            return id;
        }

        /// <summary>A closed doorway shows nothing and lets nobody through.</summary>
        public void SetDoorOpen(int door, bool open)
        {
            m_Doors[door].Open = open;
        }

        public bool IsDoorOpen(int door) { return m_Doors[door].Open; }

        /// <summary>Removes every room and door.</summary>
        public void Clear()
        {
            m_RoomCount = 0;
            m_DoorCount = 0;
            m_CandidateCount = 0;
            m_RoomsShown = false;
            m_Swap.Reset();
            m_Slots.Begin();
            m_CurrentRoom = 0;
            m_HasPreviousHead = false;
        }

        /// <summary>Where a room is drawn after the last step.</summary>
        public RoomFrame Room(int room) { return m_Rooms[room]; }

        /// <summary>The masks of one direction of a doorway after the last step (0 forward, 1 backward).</summary>
        public DoorFrame DoorMasks(int door, int direction) { return m_DoorFrames[2 * door + (direction & 1)]; }

        /// <summary>The room a walker stands in to see a direction of a doorway (0 forward, 1 backward).</summary>
        public int ViewerRoom(int door, int direction) { return direction == 0 ? m_Doors[door].From : m_Doors[door].To; }
        /// <summary>The room a direction of a doorway shows and leads to.</summary>
        public int TargetRoom(int door, int direction) { return direction == 0 ? m_Doors[door].To : m_Doors[door].From; }
        /// <summary>The doorway's plane with its normal towards the viewer of this direction.</summary>
        public float4 ViewerPlane(int door, int direction) { return direction == 0 ? m_Doors[door].Plane : PortalMath.Flip(m_Doors[door].Plane); }

        /// <summary>
        /// One frame. head is the raw head position, the eyes are the cameras' positions; the planes are each
        /// eye's six frustum planes with their normals inwards, or null when there is no camera: a doorway
        /// then counts as seen whenever the head is in front of it, and no room is seen past its doorway.
        /// featuresFound is false when the renderer has no stencil features: nothing would test the stencil
        /// and rooms that overlap would all be drawn at once, so the walker's room is shown alone, and
        /// walking through a doorway still works. Returns true when the walker's room changed.
        /// </summary>
        public bool Step(float3 head, float3 leftEye, float3 rightEye, float4[] leftPlanes, float4[] rightPlanes, bool featuresFound)
        {
            if (m_RoomCount == 0) return false;
            if (m_CurrentRoom < 0 || m_CurrentRoom >= m_RoomCount) m_CurrentRoom = 0;

            bool changed = m_HasPreviousHead && DetectCrossing(head);
            m_PreviousHead = head;
            m_HasPreviousHead = true;

            bool frusta = leftPlanes != null && rightPlanes != null;
            m_CandidateCount = featuresFound ? FindDoorways(head, leftEye, rightEye, leftPlanes, rightPlanes, frusta) : 0;
            m_RoomsShown = featuresFound && m_Depth > 0;
            AssignSlots();
            PlaceRooms(head, leftPlanes, rightPlanes, frusta);
            PlaceMasks();
            return changed;
        }

        // The current room changes when the head, not an eye, goes through a doorway (Master Plan P1-A10).
        bool DetectCrossing(float3 head)
        {
            for (int k = 0; k < m_DoorCount; k++)
            {
                ref Door door = ref m_Doors[k];
                if (!door.Open) continue;
                for (int direction = 0; direction < 2; direction++)
                {
                    if (ViewerRoom(k, direction) != m_CurrentRoom) continue;
                    float2 left = direction == 0 ? door.A : door.B, right = direction == 0 ? door.B : door.A;
                    if (!PortalMath.Crossed(ViewerPlane(k, direction), left, right, door.Height, m_PreviousHead, head)) continue;
                    int old = m_CurrentRoom;
                    SetCurrent(TargetRoom(k, direction));
                    return m_CurrentRoom != old;
                }
            }
            return false;
        }

        void SetCurrent(int room)
        {
            int old = m_CurrentRoom;
            if (room == old) return;
            m_CurrentRoom = room;
            Action<int, int> changed = RoomChanged;
            if (changed != null) changed(old, room);
        }

        // Doorways of the walker's room that an eye can see, largest first. Returns their number.
        int FindDoorways(float3 head, float3 leftEye, float3 rightEye, float4[] leftPlanes, float4[] rightPlanes, bool frusta)
        {
            int count = 0;
            for (int k = 0; k < m_DoorCount && count < MaxCandidates; k++)
            {
                ref Door door = ref m_Doors[k];
                if (!door.Open) continue;
                for (int direction = 0; direction < 2 && count < MaxCandidates; direction++)
                {
                    if (ViewerRoom(k, direction) != m_CurrentRoom) continue;
                    float4 plane = ViewerPlane(k, direction);
                    float side = PortalMath.Side(plane, head);
                    if (side < -PortalMath.BehindTolerance) continue;

                    // The portal box: an eye this close cannot rely on a flat mask, the near plane cuts it.
                    bool box = Straddles(in door, plane, leftEye) || Straddles(in door, plane, rightEye);
                    float area;
                    if (box) area = PortalMath.FullViewArea;
                    else if (frusta)
                        area = math.max(PortalMath.DoorViewArea(door.A, door.B, door.Height, leftEye, leftPlanes),
                                        PortalMath.DoorViewArea(door.A, door.B, door.Height, rightEye, rightPlanes));
                    else
                        area = side > 0f ? math.distance(door.A, door.B) * door.Height / math.max(side * side, 0.01f) : 0f;
                    if (!(area > 1e-6f)) continue;

                    // Insertion keeps the list sorted by area, largest first.
                    int at = count;
                    while (at > 0 && m_Candidates[at - 1].Area < area)
                    {
                        m_Candidates[at] = m_Candidates[at - 1];
                        at--;
                    }
                    m_Candidates[at] = new Candidate { Door = k, Direction = direction, Area = area, Box = box };
                    count++;
                }
            }
            return count;
        }

        static bool Straddles(in Door door, float4 plane, float3 eye)
        {
            return math.abs(PortalMath.Side(plane, eye)) < PortalMath.BoxDistance
                   && PortalMath.InsideDoor(door.A, door.B, door.Height, eye, PortalMath.BoxDistance);
        }

        void AssignSlots()
        {
            m_Slots.Begin();
            for (int k = 0; k < m_CandidateCount; k++)
                m_Candidates[k].Reference = m_Slots.Assign(TargetRoom(m_Candidates[k].Door, m_Candidates[k].Direction));
        }

        // At depth 0 no room is placed: doorways are still masked, and show the background's fog colour.
        void PlaceRooms(float3 head, float4[] leftPlanes, float4[] rightPlanes, bool frusta)
        {
            bool showRooms = m_RoomsShown;
            int currentRoom = m_CurrentRoom;
            m_Rooms[currentRoom] = new RoomFrame { On = true, Layer = PortalStencilTable.WorldLayer, Plane = NoPlane, WasShown = true };
            m_Swap.Reset(currentRoom);

            // Rooms behind doorways: the largest doorway into a room gives its clip plane.
            if (showRooms)
            {
                for (int k = m_CandidateCount - 1; k >= 0; k--)
                {
                    Candidate candidate = m_Candidates[k];
                    if (candidate.Reference == 0) continue;
                    int target = TargetRoom(candidate.Door, candidate.Direction);
                    if (target == currentRoom) continue;
                    m_Rooms[target] = new RoomFrame
                    {
                        On = true,
                        Layer = PortalStencilTable.Layer(candidate.Reference),
                        // Normal into the room behind the doorway: what lies in front of the doorway is not drawn.
                        Plane = PortalMath.Flip(ViewerPlane(candidate.Door, candidate.Direction)),
                        WasShown = true,
                    };
                    m_Swap.Reset(target);
                }
            }

            // Rooms no doorway shows.
            for (int k = 0; k < m_RoomCount; k++)
            {
                if (k == currentRoom) continue;
                if (showRooms && m_Slots.ReferenceOf(k) != 0) continue;
                ref RoomFrame room = ref m_Rooms[k];
                if (!room.On) continue;

                int reference = 0;
                if (showRooms && room.WasShown)
                {
                    bool leftSees = frusta && PortalMath.BoundsInFrustum(leftPlanes, m_Min[k], m_Max[k]);
                    bool rightSees = frusta && PortalMath.BoundsInFrustum(rightPlanes, m_Min[k], m_Max[k]);
                    bool inOverlap = PortalMath.InOverlap(head, m_Min[k], m_Max[k], m_Min[currentRoom], m_Max[currentRoom]);
                    // Kept on a layer of its own: no mask writes that reference, so nothing of it shows.
                    if (!m_Swap.Step(k, leftSees, rightSees, inOverlap)) reference = m_Slots.Assign(k);
                }
                if (reference != 0)
                {
                    room.Layer = PortalStencilTable.Layer(reference);
                }
                else
                {
                    room.On = false;
                    room.WasShown = false;
                    m_Swap.Reset(k);
                }
            }
        }

        void PlaceMasks()
        {
            for (int k = 0; k < 2 * m_DoorCount; k++) m_DoorFrames[k] = default;
            for (int c = 0; c < m_CandidateCount; c++)
            {
                Candidate candidate = m_Candidates[c];
                m_DoorFrames[2 * candidate.Door + candidate.Direction] = new DoorFrame { Reference = candidate.Reference, Box = candidate.Reference != 0 && candidate.Box };
            }
        }
    }
}
